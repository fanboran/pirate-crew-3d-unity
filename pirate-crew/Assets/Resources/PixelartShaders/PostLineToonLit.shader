// ============================================================================
// PostLineToonLit.shader —— PostLine 线的物体着色 shader（全分辨率前向 ToonLit）
//
// 【这条线是什么】PostLine（屏后描边线）风格里的物体 pass：几何与着色都在全分辨率
//   正常跑（本 shader 出色），像素化与描边全部发生在其后的后处理链
//   （降采 → 屏幕空间描边 → 合成 → 锐利上采样，由 PostLineRendererFeature 驱动）。
//   与 CellShade（PixelartObject 写 G-buffer + 低分辨率域着色）的分野只在
//   「着色发生在哪一档分辨率」。材质双 pass 口径（切风格不换材质，
//   docs/技术/渲染/管线/双风格切换.md §3.3）：同一材质同时携带
//     LightMode = "PixelartOpaque"   —— CellShade 物体 pass（另一张 shader）
//     LightMode = "UniversalForward"（本文件）—— PostLine 全分辨率着色 pass
//
// 【移植来源】参照库（PostLine）ToonLit.shader，Shader "Custom/ToonLit"
//   （external/ref/unity-isometric-pixel-pipeline，HEAD f08bc6b，Unity 6 / URP 17）
//   的源码级移植；移植规格与坑清单见 docs/技术/渲染/调研/PostLine规格.md §3/§8/§9。
//   参照库许可（MIT），按规格 §0 要求在文件头保留其版权声明：
//     Copyright (c) 2026 bababuyyy — MIT License
//     （原文见 external/ref/unity-isometric-pixel-pipeline/LICENSE）
//
// 【四 pass 与 SRP Batcher】UniversalForward / ShadowCaster / DepthOnly / DepthNormals。
//   四个 pass 的 UnityPerMaterial CBUFFER 只有**一份**——在 SubShader 的 HLSLINCLUDE 里
//   定义一次，每段 HLSLPROGRAM 自动整段前置。这从结构上保证四个 pass 的
//   cbuffer 逐字节一致（SRP Batcher 兼容性就是靠这个判定；分 pass 各写一份迟早漂移）。
//   【贴图/全局/辅助函数不进 HLSLINCLUDE】只有 ForwardLit 消费它们；留在共用块里
//   会被另外三个 pass（默认 target 2.0、不声明任何关键字的编译单元）一并前置编译，
//   徒增编译风险面。共用块只留 SRP Batcher 必需的 cbuffer。
//
// 【与参照库的六处刻意差异】每处都有规格依据，不要"改回去"：
//   ① Patch 默认值两道闸：_Color2/_Color3 默认 = _BaseColor 默认（白），不是参照库的绿系值；
//      噪声图默认 "black"，不是参照库的 "white"——规格坑 12：参照库无噪声图时 "white" 采样
//      恒 1 > 阈值 ⇒ 恒取 _Color3，绿默认值会让无图材质整件变绿。只改颜色默认值不够：
//      材质改了 _BaseColor 却没挂噪声图、也没同步 _Color2/3 时会整件变成白色的 _Color3。
//      "black" 采样恒 0 < 任意阈值 ⇒「没挂噪声图 = 没有斑块」，不再依赖颜色默认值。
//      对参照库全部材质逐像素等价（无图材质的 _Color2/3 本就抄成 _BaseColor，规格 §5.1；
//      有图材质不受默认图影响）。
//   ② Bayer 抖动索引 = 画布像素 floor(SV_Position.xy / _PixelartSamplingScale)，
//      不是参照库的全分辨率 SV_Position——规格 §8.2：2:1 降采后全分辨率索引只会踩到
//      Bayer 矩阵的偶数行/列（4×4 图案退化为 2×2 且系统性偏暗）。
//      _PixelartSamplingScale 与 PixelartPath.SamplingScaleId 同名（rig 每帧下发）；
//      全局缺失（=0）时 max(·,1) 兜底成 k=1，即参照库行为。
//   ③ 抖动幅度口径 = 本仓「满幅（强度 1）= ±0.5 个色带档间距」，不是参照库的
//      *0.25（满幅 ±0.125 光照）——规格 §8.2【提案：按本仓口径】，换算与
//      PixelartShading 的 offset*(0.5/level) 同一量纲：offset=(bayer-0.5)*2 ∈ [-1,1]。
//   ④ DepthNormals **直写归一化世界法线** half4(normalize(n), 0)，不是参照库的 n*0.5+0.5——
//      规格坑 7：URP 法线纹理是 R8G8B8A8_SNorm（[-1,1] 恒等存取），偏置写法会让 Outline 的
//      角度自适应阈值随相机方位角在 0.21~1.05 m 间漂移（描边忽粗忽无）。
//      URP 自带 Lit 的 DepthNormals 同为直写（LitDepthNormalsPass.hlsl 口径）。
//   ⑤ 死参数不搬（规格坑 11）：_MidtoneColor（参照库 palette 分支用 albedo 顶替）等一概没有；
//      开关用 [ToggleUI] 不产生关键字（坑 17：参照库 [Toggle] 生成了无人接收的 _USEDITHER_ON）。
//   ⑥ 云影多一道 _CloudScale<=0 护栏：云影系统未下发时该全局为 0，参照库的 1/0=Inf
//      会把采样坐标打成 Inf/NaN 污染整幅光照；接线后 scale>0，行为与参照库逐位一致。
//      【参数实名】云影族按规格/参照库实名移植（_CloudShadowMin = 影底亮度下限），
//      本仓文档里没有 _CloudShadowStrength 这个量。
//
// 【光照来源】主光方向/颜色优先吃 shader 全局 _PixelartLightDirWS / _PixelartLightColor
//   （rig 每帧下发，PixelartPath.LightDirId/LightColorId 同名）；环境光吃
//   _GlobalAmbientColor（**参照库实名全局，不是 CellShade 的 _PixelartAmbientColor**）。
//   环境与云影由 PostLineEnvironment 在 PostLineRendererFeature.PushFrameGlobals 里每帧
//   按参照 CloudShadowManager 的常数下发（2026-11-09 接线；此前这两族零写入，
//   环境恒 0 → 未受光面纯黑）。
//   【缺失回退】「已下发」判据照 PixelartShading 的同款：方向向量长度平方 > 1e-8。
//   未接线（材质预览/feature 未挂）时回退 GetMainLight() + SampleSH()——
//   不回退的话预览里主光与环境全为 0，画面全黑且无报错（本仓记过的静默失效类）。
//   阴影衰减始终来自 URP 主光阴影图（_MAIN_LIGHT_SHADOWS 族关键字 +
//   TransformWorldToShadowCoord），与谁下发方向/颜色无关。
//
// 【贴图导入硬要求】_Noise2/_Noise3/_CloudNoise 三张噪声图必须 sRGB + Bilinear + Repeat
//   导入（规格 §6.7）——所有阈值（0.604/0.661 等）作用在**线性域**，导入组合错了
//   覆盖率差数倍；Repeat 是世界 XZ 平铺采样的前提（sampler 直接吃导入设置）。
// ============================================================================

Shader "PirateCrew/PostLineToonLit"
{
    Properties
    {
        [Header(Base)]
        _BaseColor ("固有色（无贴图，纯色）", Color) = (1, 1, 1, 1)

        [Header(Palette)]
        _HighlightColor ("高光色", Color) = (1, 1, 1, 1)
        _ShadowColor ("阴影色", Color) = (0.3, 0.3, 0.4, 1)
        // [ToggleUI] 只画勾选框、不产生 shader 关键字（差异⑤，规格坑 17）；shader 只读 float。
        [ToggleUI] _UsePalette ("使用调色板（关 = 光色直乘分支）", Float) = 0

        [Header(Bands)]
        _Cuts ("色带档数", Range(1, 8)) = 3
        _Steepness ("光照斜率", Range(1, 8)) = 1.0
        _Wrap ("半兰伯特偏移", Range(-2.0, 2.0)) = 0.0
        _ThresholdGradientSize ("色带边界平滑带（占一档的比例；0 = 硬边）", Range(0.0, 1.0)) = 0.2

        [Header(Dither)]
        [ToggleUI] _UseDither ("Bayer 抖动", Float) = 0
        _DitherStrength ("抖动强度（1 = 满幅 ±0.5 个档间距）", Range(0.0, 1.0)) = 0.1

        [Header(Patch Layer 1)]
        // 【两道闸，差异①】颜色默认 = _BaseColor 默认；噪声图默认 "black"（采样恒 0，
        // 不触发任何斑块）⇒ 没挂噪声图的材质恒显 _BaseColor，与 _Color2/3 填什么无关。
        _Color2 ("斑块色 2", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _Noise2 ("斑块噪声 2（灰度；须 sRGB+Repeat 导入，规格 §6.7）", 2D) = "black" {}
        _Noise2Scale ("噪声 2 世界尺度（UV = 世界XZ × 该值）", Range(0.0, 1.0)) = 0.005
        _Noise2Threshold ("噪声 2 阈值（线性域）", Range(0.0, 1.0)) = 0.604

        [Header(Patch Layer 2)]
        _Color3 ("斑块色 3（后判优先，覆盖色 2）", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _Noise3 ("斑块噪声 3（灰度；须 sRGB+Repeat 导入）", 2D) = "black" {}
        _Noise3Scale ("噪声 3 世界尺度", Range(0.0, 0.1)) = 0.003
        _Noise3Threshold ("噪声 3 阈值（线性域）", Range(0.0, 1.0)) = 0.661
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // ------------------------------------------------------------------
        // 四 pass 共用块：只有 SRP Batcher 必需的 UnityPerMaterial。
        // 【贴图/全局/辅助函数不在这里】它们只被 ForwardLit 消费，且含动态下标数组，
        // 留在共用块会被另外三个 pass（默认 target 2.0）一并前置编译——见文件头。
        // 【结构体也不在这里】URP 标准 pass include（ShadowCasterPass.hlsl 等）自带
        // 同名 Attributes/Varyings，共用块里再定义一份会重定义冲突。
        // ------------------------------------------------------------------
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 【SRP Batcher】材质属性的唯一 cbuffer，四个 pass 共享这一份定义。
        // 字段必须与 Properties 一一对应（贴图除外——纹理不进 UnityPerMaterial）；
        // 全局量（_PixelartLightDirWS 等）严禁塞进来（SetGlobal 下发的量进逐材质
        // cbuffer 会破坏 SRP Batcher 的合批判定）。
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _HighlightColor;
            half4 _ShadowColor;
            float _UsePalette;

            int   _Cuts;
            float _Steepness;
            float _Wrap;
            float _ThresholdGradientSize;

            float _UseDither;
            float _DitherStrength;

            half4 _Color2;
            float _Noise2Scale;
            float _Noise2Threshold;

            half4 _Color3;
            float _Noise3Scale;
            float _Noise3Threshold;
        CBUFFER_END
        ENDHLSL

        // ==================================================================
        // Pass 1 / ForwardLit：全分辨率前向着色。
        //   片元流程（规格 §3.2①→⑥，参照库 :179-261 逐行）：
        //   ① Patch 选色（世界 XZ 噪声，后判优先）→ ② 主光（wrap×steepness）
        //   → ③ 云影取 min（先于色带）→ ④ Bayer 抖动 → ⑤ 色带量化（GLSLmod + 梯度带）
        //   → ⑥ 阴影 × 最终色（palette 插值 / 光色直乘 + 环境光）。
        // ==================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            // 3.5：Bayer 静态数组的动态下标在 GLES2 类目标上不可靠（参照库同因）。
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag

            // 主光阴影族：TransformWorldToShadowCoord / MainLightRealtimeShadow 按这组关键字出变体。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // 【软阴影四个关键字全声明】URP14 在部分平台用 LOW/MEDIUM/HIGH 分级关键字
            //（ShadowUtils.SetSoftShadowQualityShaderKeywords；URP 官方 Lit 与本仓
            // PixelartShading 同口径）——只声明 _SHADOWS_SOFT 会在那些平台静默退成硬阴影。
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // 【不做 _FORWARD_PLUS 变体】本仓渲染器资产全为 Forward（m_RenderingMode: 0），
            // 且主光方向/颜色以下发全局为准（Forward+ 的主光缓冲差异不进入本 pass 的取数路径）。
            #pragma multi_compile_instancing

            // GetMainLight / SampleSH / TransformWorldToShadowCoord 的标准入口。
            // 【顺序自洽，勿拆】Lighting.hlsl 第 4 行先引 BRDF.hlsl（→ core CommonMaterial.hlsl），
            // RealtimeLights.hlsl（→ Shadows.hlsl）排在其后——Shadows.hlsl 里 LerpWhiteTo
            // 依赖 CommonMaterial 却不自引的坑（PirateOutline 2026-09-13 实测事故同款）
            // 由这条链自洽。只 include Shadows.hlsl 会复现 undeclared identifier。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ---------------- 材质贴图 ----------------
            // 采样器状态直接吃导入设置：噪声图必须 Repeat 包裹（文件头「贴图导入硬要求」）。
            TEXTURE2D(_Noise2);     SAMPLER(sampler_Noise2);
            TEXTURE2D(_Noise3);     SAMPLER(sampler_Noise3);

            // ---------------- 云影全局族（参照库实名；由 PostLine 云影下发侧每帧维护）----------------
            // 未接线时全部为 0，PostLineCloudShadow 的护栏直接返回 1（见文件头差异⑥）。
            TEXTURE2D(_CloudNoise); SAMPLER(sampler_CloudNoise);
            float  _CloudScale;
            float  _CloudWorldY;
            float  _CloudSpeed;
            float  _CloudContrast;
            float  _CloudThreshold;
            float4 _CloudDirection;
            float  _CloudShadowMin;
            float  _CloudDivergeAngle;
            float4 _CloudLightDirection;
            float  _CloudPower;

            // ---------------- 光照与环境全局（缺失回退见文件头）----------------
            float4 _PixelartLightDirWS;    // 主光方向（世界空间，指向光源；rig/PostLine feature 下发）
            float4 _PixelartLightColor;    // 主光颜色（线性）
            float4 _GlobalAmbientColor;    // 环境光（线性加数；SetGlobalColor 不做 sRGB 转换，规格坑 15）

            // 1 画布像素 = 几个屏幕像素（= pixelScale，rig 经 PixelartPath.SamplingScaleId 下发）。
            // Bayer 抖动索引按画布像素取（差异②，规格 §8.2）。
            float _PixelartSamplingScale;

            // GLSL 语义的 mod（HLSL 的 fmod 对负数行为不同；参照库 :117-120 用的就是这条实现）。
            float PostLineGLSLMod(float x, float y)
            {
                return x - y * floor(x / y);
            }

            float2 PostLineRotate2(float2 v, float angleDeg)
            {
                float rad = radians(angleDeg);
                float c = cos(rad);
                float s = sin(rad);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            // ------------------------------------------------------------------
            // 云影光值（参照库 ToonLit :130-151 GetCloudNoise 逐行移植；公式见规格 §3.2 说明）：
            // 从世界点沿 _CloudLightDirection 投到 _CloudWorldY 高度平面，同图双层异采样，
            // 对比度/阈值/幂次整形后作为「该点光照上限」参与 min。
            // ------------------------------------------------------------------
            float PostLineCloudShadow(float3 worldPos)
            {
                // 光几乎水平（y<0.05）时防除零——参照库原护栏；全局未接线（=0）时同样命中，
                // 兼任"未接线自禁用"。
                if (abs(_CloudLightDirection.y) < 0.05)
                    return 1.0;
                // 本仓加的护栏（差异⑥）：_CloudScale 未下发时为 0，1/0=Inf 会污染采样坐标。
                if (_CloudScale <= 0.0)
                    return 1.0;

                float t = (_CloudWorldY - worldPos.y) / _CloudLightDirection.y;
                float3 hitPos = worldPos + t * _CloudLightDirection.xyz;
                float invScale = 1.0 / _CloudScale;

                // 两层同图异采样：方向 ±散角、尺度 ×0.8、时移 ×0.89×1.047（参照库原常数）。
                float2 cloudDir1 = PostLineRotate2(_CloudDirection.xy, _CloudDivergeAngle);
                float2 cloudDir2 = PostLineRotate2(_CloudDirection.xy, -_CloudDivergeAngle);
                float2 cloudTimeDir1 = _Time.y * _CloudSpeed * normalize(cloudDir1);
                float2 cloudTimeDir2 = _Time.y * _CloudSpeed * normalize(cloudDir2);
                float sample1 = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise,
                    hitPos.xz * invScale + cloudTimeDir1).r;
                float sample2 = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise,
                    hitPos.xz * (invScale * 0.8) + (cloudTimeDir2 * 0.89 * 1.047)).r;
                float cloudSample = sample1 * sample2;
                float lightValue = saturate(cloudSample + _CloudThreshold);
                lightValue = (lightValue - 0.5) * _CloudContrast + 0.5;
                lightValue = clamp(lightValue + _CloudThreshold, _CloudShadowMin, 1.0);
                lightValue = pow(lightValue, _CloudPower);
                return lightValue;
            }

            // ------------------------------------------------------------------
            // 4×4 Bayer 矩阵（参照库 :154-168 同表）。入参必须是**画布像素坐标**
            //（片元里做 floor(SV_Position.xy / _PixelartSamplingScale)，差异②）。
            // ------------------------------------------------------------------
            float PostLineBayer4x4(float2 pixelPos)
            {
                int x = int(fmod(pixelPos.x, 4.0));
                int y = int(fmod(pixelPos.y, 4.0));
                // static const：防部分目标平台把数组误当可写缓冲分配（参照库同注）。
                static const float bayer[16] =
                {
                     0.0,  8.0,  2.0, 10.0,
                    12.0,  4.0, 14.0,  6.0,
                     3.0, 11.0,  1.0,  9.0,
                    15.0,  7.0, 13.0,  5.0
                };
                return bayer[y * 4 + x] / 16.0;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS  = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ---- ① Patch 系统：世界 XZ 噪声选色（后判优先，Color3 覆盖 Color2）----
                float noise2Val = SAMPLE_TEXTURE2D(_Noise2, sampler_Noise2,
                    input.positionWS.xz * _Noise2Scale).r;
                float noise3Val = SAMPLE_TEXTURE2D(_Noise3, sampler_Noise3,
                    input.positionWS.xz * _Noise3Scale).r;
                half3 albedo = _BaseColor.rgb;
                if (noise2Val > _Noise2Threshold) albedo = _Color2.rgb;
                if (noise3Val > _Noise3Threshold) albedo = _Color3.rgb;

                // ---- ② 主光：阴影坐标照常走 URP 阴影图；方向/颜色优先吃下发全局 ----
                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // 「已下发」判据照 PixelartShading：方向向量长度平方 > 1e-8。
                // 未接线时保留 URP 主光（材质预览不至于全黑）；已接线时覆盖方向与颜色，
                // 阴影衰减仍取 URP 阴影图。方向口径两边一致（都指向光源），可直接赋值。
                bool useGlobals = dot(_PixelartLightDirWS.xyz, _PixelartLightDirWS.xyz) > 1e-8;
                if (useGlobals)
                {
                    mainLight.direction = normalize(_PixelartLightDirWS.xyz);
                    mainLight.color = _PixelartLightColor.rgb;
                }

                float diffuseAmount = dot(normalWS, mainLight.direction) + _Wrap;
                diffuseAmount *= _Steepness;

                // ---- ③ 云影：取 min，先于色带（参照库 :194-195）----
                float cloudLight = PostLineCloudShadow(input.positionWS);
                diffuseAmount = min(diffuseAmount, cloudLight);

                // ---- ④ Bayer 抖动（差异②③：画布像素索引 + 本仓幅度口径）----
                if (_UseDither > 0.5)
                {
                    // 全局缺失（=0）时 max(·,1) 兜底成 k=1 = 参照库的全分辨率索引行为。
                    float2 ditherPixel = floor(input.positionHCS.xy / max(_PixelartSamplingScale, 1.0));
                    float bayerVal = PostLineBayer4x4(ditherPixel);
                    // 满幅（强度 1）= ±0.5 个档间距：offset=(bayer-0.5)*2 ∈ [-1,1]，
                    // 幅度 = offset × 强度 × 0.5/档数（与 PixelartShading 的 offset*(0.5/level) 同量纲）。
                    float ditherOffset = (bayerVal - 0.5) * 2.0;
                    diffuseAmount = clamp(
                        diffuseAmount + ditherOffset * _DitherStrength * (0.5 / float(_Cuts)),
                        0.0, 1.0);
                }

                // ---- ⑤ 色带量化：GLSLmod 向上取档 + 梯度带（参照库 :204-236 逐行移植）----
                float cutsInv = 1.0 / float(_Cuts);
                float cut = cutsInv;

                // 基础量化：GLSLmod(1-diffuse, cut) 把 diffuse 抬到上一档边界（向上取档）。
                float originalIndex = ceil(diffuseAmount * float(_Cuts));
                float originalStepped = saturate(originalIndex * cut);
                float diffuseStepped = saturate(diffuseAmount + PostLineGLSLMod(1.0 - diffuseAmount, cutsInv));

                if (_ThresholdGradientSize > 0.0)
                {
                    // 最近档阈值：档边界两侧 halfWidth 宽的平滑带，过带即跳到下一档。
                    float nearestK = floor(diffuseAmount / cut + 0.5);
                    float threshold = nearestK * cut;

                    if (nearestK >= 0.0 && nearestK <= float(_Cuts))
                    {
                        float halfWidth = 0.5 * cut * _ThresholdGradientSize;
                        float low = max(0.0, threshold - halfWidth);
                        float high = min(1.0, threshold + halfWidth);

                        float blend = 0.0;
                        if (high > low)
                            blend = smoothstep(low, high, diffuseAmount);
                        else
                            blend = step(threshold, diffuseAmount);
                        float leftValue = threshold;
                        float rightValue = min(threshold + cut, 1.0);
                        diffuseStepped = saturate(lerp(leftValue, rightValue, blend));
                    }
                    else
                    {
                        diffuseStepped = originalStepped;
                    }
                }

                // ---- ⑥ 阴影与最终色（参照库 :238-260）----
                float shadow = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float lit = diffuseStepped * shadow;

                half3 finalColor;
                if (_UsePalette > 0.5)
                {
                    // 三色调色板：albedo（含斑块选色）在阴影色与高光色之间按 lit 插值。
                    finalColor = albedo * lerp(_ShadowColor.rgb, _HighlightColor.rgb, lit);
                }
                else
                {
                    // 参照库默认分支：环境光 = _GlobalAmbientColor（线性加数，规格坑 15：
                    // SetGlobalColor 不做 sRGB→线性，数值本身就是线性语义，配色对账按线性读值）。
                    // 未接线回退：URP 球谐环境（材质预览不至于全黑）。
                    float3 ambient;
                    if (useGlobals)
                        ambient = _GlobalAmbientColor.rgb;
                    else
                        ambient = SampleSH(normalWS);
                    float3 finalLighting = diffuseStepped * mainLight.color * shadow;
                    finalColor = albedo * (finalLighting + ambient);
                }

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2 / ShadowCaster：写主光阴影图（URP14 官方 ShadowCasterPass.hlsl，
        // 含 ApplyShadowBias 法线偏移防自阴影条纹与 _LightDirection/_LightPosition 读取）。
        // 【include 顺序硬要求，勿"顺手删"中间一行】Core → CommonMaterial → ShadowCasterPass：
        // Shadows.hlsl:298 的 LerpWhiteTo 定义在 core 的 CommonMaterial.hlsl，而 Shadows.hlsl
        // 不自引——漏掉中间那行的真实报错见 PirateOutline.shader Pass 4 的踩坑记录。
        // 文件里 _BaseMap/_Cutoff 的引用全在 _ALPHATEST_ON 分支内，本 shader 不声明该
        // 关键字 ⇒ 不需要声明这两个属性（不透明物体，阴影形状 = 正面轮廓）。
        // ==================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // 方向光 vs 点/聚光阴影的 Normal Bias 公式不同（URP 官方 Pass 同款保留；
            // 本工程当前只有方向光阴影，多出的变体仅在将来加点/聚光阴影时启用）。
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            // 自带 Core.hlsl + Shadows.hlsl（ApplyShadowBias / TransformWorldToHClip）。
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ==================================================================
        // Pass 3 / DepthOnly：写 URP 深度预通道（_CameraDepthTexture 的来源之一；
        // PostLine 链 Outline 的剪影判定读它）。URP14 官方 DepthOnlyPass.hlsl——
        // 同样只在 _ALPHATEST_ON 分支引用 _BaseMap/_Cutoff，本 shader 安全。
        // ==================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // ==================================================================
        // Pass 4 / DepthNormals：写 _CameraNormalsTexture（PostLine Outline 棱线判据读它）。
        // 【直写归一化世界法线，刻意与参照库不同——差异④】参照库写 n*0.5+0.5，而 URP 的
        // 法线纹理首选 R8G8B8A8_SNorm（[-1,1] 恒等存取）：偏置写法读回来整体带 +0.5，
        // Outline 的角度自适应阈值随相机方位角漂移（规格坑 7 的实测量级见 §8.3 表）。
        // 直写 half4(normalize(n), 0) 与 URP 自带 Lit 的 DepthNormals 同口径
        //（LitDepthNormalsPass.hlsl：NormalizeNormalPerPixel 直写）。
        //
        // 【手写而不引 DepthNormalsPass.hlsl】那个 include 的 Attributes 要求 TANGENT/TEXCOORD0
        // 并走 GetVertexNormalInputs（切线空间法线贴图管线）；本风格无贴图、只要平滑法线，
        // 与参照库一致的 POSITION+NORMAL 输入即可，也让无切线的网格不必喂未定义属性。
        // ==================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // 直写 [-1,1] 原值；SNorm 附件存取恒等，Outline 读回即世界法线。
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    // 自带全部四 pass，不继承任何内置回退（回退到内置 Lit 会绕开整套 PostLine 口径）。
    Fallback Off
}
