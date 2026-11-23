// ============================================================================
// CloudSeaWaterLabOcean.shader —— 云端漫步海面的 **WaterLab 复刻水**（双趟透明）
//
// 【它是什么】WaterLabOcean.shader（Sandcastle Lit/Water 逐指令移植，验收帧
//   export/waterlab-r110-final）接到第 1 关海面的双链收件版：模拟核/公式/常量一个
//   不动（WaterLabRules + WaterLabSim.compute + WaterLabReplicaGlobals 真值链全部
//   照旧），只把**收件口**拆成两趟——
//   · Pass 1 `PixelartTransparent`（CellShade 像素化域）：像素化透明叠加相机收
//     （同 CloudSeaWater.shader 的口径），折射底色采 **艺术画布快照**
//     `_WaterLabPixelartScene`（PixelartWaterLabSceneFeature 发布——像素化域的
//     _CameraOpaqueTexture 里没有场景内容），深度安全门采细像素深度
     //    `_PixelartDepthBuffer`（手动设备深度比对，同 CloudSeaWater 趟 1）；
//   · Pass 2 `UniversalForward`（PostLine / 普通 URP 前向）：= WaterLabOcean 正身
//     （_CameraOpaqueTexture + _CameraDepthTexture + 主光阴影）。
//   域闸门 `_CloudSeaPixelDomain`（CloudSeaWater 同款全局）让两趟互斥，一帧只画一次。
//
// 【与 WaterLabOcean 的公式差异（逐条登记，别处逐字一致）】
//   · Pass 2 逐字照抄（含 T=exp2(−8.65617·σ·dist)、散射/镜面/α= sat(depth×100)）。
//   · Pass 1：①底色源换画布快照（URP 的 SampleSceneColor 不可用——它读
//     _CameraOpaqueTexture）；②深度门换细像素设备深度直比（reversed-Z 语义同
//     CloudSeaWater）；③散射的阴影软化项取 1（叠加相机无主光阴影链——登记差异，
//     抓帧观感若发现云影缺失再议）；④折射偏移的 aspect 项按画布纵横比。
//   · 两趟共有的**场景适配项（非原作参数）**：⑤凸出海床实体保护——细像素/场景
//     深度重建可见点世界高，与模拟海床高比较，鱼/珊瑚/礁石按离床高度让出水色
//     （_ObjectProtect* / _ObjectVeil）。原作没有这一层：它的水是 0.1–0.6 m 浅滩
//     且镜头近水平，本关俯视 + 0.3–1 m 水柱会把鱼压成水色；保护只改「水色覆盖
//     比例」，h/色带/吸收/焦散公式不动。
//
// 【渲染状态】Blend SrcAlpha OneMinusSrcAlpha + ZWrite Off（R 流 draw 3093 实锤的
//   原作状态；ZWrite On 版在 Transparent+10 会把海面深度写进 _CameraDepthTexture，
//   折射安全门自照自）。ocean 网格 90 m 盖住相机可见海面（±43 m 足迹），α = 0.78
//   半透；30 m 模拟域外由 shader 钳位采样域缘 η/地形平延，**不再做域缘 α 渐隐**
//   （旧渐隐会在画面临一圈方形域界）。简版水只做 90 m 外的远景兜底。
// ============================================================================

Shader "PirateCrew/CloudSea/WaterLabOcean"
{
    Properties
    {
        _WaterLabDetailNormal ("细节法线（原作提取贴图）", 2D) = "gray" {}
        _EdgeFade ("域缘 α 渐隐宽度（米，与域外简版水衔接）", Range(0.0, 8.0)) = 2.0
        // σ 缩放。云海浅海水柱 0.5–2.5 m，实验室真值 σ=(0.993,0.175,0.056)（exp2 语义
        // ≈6σ/m）会把红通道吸没（2 m 水柱 e^−12）→ 整片发黑；云海走原作自己的
        // σ≡0 分支（A 流定案：原作此槽无上传者，T ≡ waterColor）→ 默认 0。
        // 恢复实验室深水吸收行为改回 1。
        _SigmaScale ("σ 缩放（0 = 原作无吸收分支 T≡水色）", Range(0.0, 1.0)) = 0.0
        // 水不透明度增益：**原作为不透明水面**（α = saturate(可见水深×100) ≡ 1），
        // 「透感」来自折射底图（bg×T 染色），不是 alpha 混合——2026-11-21 创始人
        // 「透明度没必要这么低」定案：改回 1.0，域内彻底接管水面（简版水在下层不可见）。
        // 小于 1 会出现「水色+简版水+海床」三层混色（首轮灰绿浑浊根因），只作调试旋钮。
        _AlphaGain ("水不透明度（1 = 原作不透明口径）", Range(0.0, 1.0)) = 1.0
        // 高透下的海浪动态可见度：折射偏移乘子（实验室正身 0.0121 屏幕 UV 在俯视
        // 取景里太含蓄——乘 2.5 让波纹位移一眼可读，创始人 2026-11-21"海浪动态一下"）。
        _RefractionGain ("折射波纹增益（倍）", Range(1.0, 6.0)) = 2.5
        // 水体焦散：**库内 Demo 的 3D 焦散体**（Art/Textures/WaterLab/Caustics.asset，
        // WaterLabSand 同源同式：worldXZ×0.25 + 半相位交叉淡化）——焦散加到折射底图
        // （水下的沙面）上，不是水面本身：读起来就是「透过水看到沙上的动态光网」。
        _CausticsTex ("焦散体（原作 Caustics Texture3D）", 3D) = "gray" {}
        _WaterLabSandMap ("沙地噪声图（原作 sandMap：R 白噪声 / G 低频流）", 2D) = "gray" {}
        _CausticScale ("焦散世界尺度（1/m；原作 0.25）", Range(0.01, 1.0)) = 0.25
        _CausticGain ("焦散增益", Range(0.0, 8.0)) = 4.0
        // 焦散水深衰减适配：原作 exp2(−1.731·depth) 是按主菜单浅滩（<0.5 m）标定的，
        // 本关水柱 2–3 m 会把焦散吃到只剩 3%（实拍：整片看不到光网）。0.35 = 场景
        // 适配系数（只改衰减斜率，焦散体/双采样/公式结构不动）。
        _CausticDepthScale ("焦散水深衰减系数（场景适配）", Range(0.05, 1.0)) = 0.35
        // 镜面档（场景适配）：原作 pow 1500 是近水平视角的细碎波光，俯视取景下
        // 太阳高光几乎不可见；放宽到 400 + 增益 2 让「海浪」在水面上读得出来。
        _SpecSharpness ("镜面锐度（俯视适配）", Range(50, 1500)) = 400
        _SpecGain ("镜面增益", Range(0.0, 6.0)) = 2.0
        // 波浪明暗（场景适配）：模拟法线随涌浪起伏，按太阳方向做一层明暗调制——
        // 俯视取景下菲涅尔/镜面项都读不出来，这层让「海浪在动」在静帧里也可见。
        _WaveShadeStrength ("波浪明暗强度", Range(0.0, 1.0)) = 0.35
        // 云影强度（公式与全局族 = 参照库 CloudShadowManager 原样，只做线性混合）：
        // 1 = 参照库原强度（影底 0.55）；云海浅水+俯视取景下整域噪声值偏低时会把
        // 水压成暗橄榄（2026-11-21 实拍），默认 0.6 保留可见影斑、不压死水色。
        _CloudShadowStrength ("云影强度（0..1，公式=参照库）", Range(0.0, 1.0)) = 0.6
        // 场景水色染色（**本仓场景色，不是模拟参数**）：库内 Demo 的水是近水平视角、
        // 靠散射读青色；本关俯视取景下散射项 (1−NdotV)^5 几乎为 0，水面只剩「清澈
        // 见底」的沙色，读起来像湿沙面。这层按可见水深加权的青染是第 1 关的海水
        // 美术定调（与域外简版水同族），折射/焦散/云影/波形仍全部来自 Demo 公式。
        // 社区 beach-sandbox water.gdshader 的观感配方（只留前三层色带）：
        // edge → turquoise → mid_blue；absorption 按米、Vector 下发（避免 sRGB→线性二次压）。
        _BandEdge ("浅滩色（0–0.18 m）", Color) = (0.561, 0.953, 0.894, 1)
        _BandTurq ("青绿（0.18–0.55 m）", Color) = (0.106, 0.749, 0.776, 1)
        _BandMid ("中蓝（>0.55 m）", Color) = (0.086, 0.565, 0.784, 1)
        _Absorption ("吸收（每米 RGB；越小越透）", Vector) = (2.2, 0.9, 0.55, 0)
        _ShallowBody ("浅水底色（0 = 全透）", Range(0.0, 0.3)) = 0.10
        // 凸出海床的实体保护（场景适配，不是原作参数）：鱼/珊瑚/礁石不该喝整根水柱
        // 的水色——用细像素深度重建可见点世界高，与模拟海床高相减得「离床高度」；
        // 高度 ≥ _ObjectProtectFull 的实体几乎保留原色（只留 _ObjectVeil 的水下薄纱），
        // 贴沙面（高度 0）不受影响。鱼群离床 0.16–0.5 m，正好落在满额段。
        _ObjectProtectStart ("实体保护起始高度（离海床 m）", Range(0.0, 1.0)) = 0.015
        _ObjectProtectFull ("实体保护满额高度（离海床 m）", Range(0.005, 2.0)) = 0.09
        _ObjectProtectStrength ("实体保护强度（0..1）", Range(0.0, 1.0)) = 0.92
        _ObjectVeil ("保护对象残留水色", Range(0.0, 0.3)) = 0.06
        _WaterTint ("场景水色（残留槽，未用）", Color) = (0.42, 0.72, 0.95, 1)
        _WaterTintStrength ("场景水色强度（0 = 纯 Demo 水色）", Range(0.0, 1.0)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ==================================================================
        // Pass 1 / PixelartTransparent：CellShade 像素化透明叠加域。
        // ==================================================================
        Pass
        {
            Name "PixelartTransparent"
            Tags { "LightMode" = "PixelartTransparent" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PixelartDepthBuffer);        SAMPLER(sampler_PixelartDepthBuffer);
            TEXTURE2D(_WaterLabPixelartScene);      SAMPLER(sampler_WaterLabPixelartScene);

            float _CloudSeaPixelDomain;

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterLabDetailNormal_ST;
                float _EdgeFade;
                float _SigmaScale;
                float _AlphaGain;
                float _CausticGain;
                float _CausticScale;
                float _RefractionGain;
                float _CloudShadowStrength;
                float4 _WaterTint;
                float _WaterTintStrength;
                float4 _BandEdge;
                float4 _BandTurq;
                float4 _BandMid;
                float4 _Absorption;
                float _ShallowBody;
                float _ObjectProtectStart;
                float _ObjectProtectFull;
                float _ObjectProtectStrength;
                float _ObjectVeil;
                float _CausticDepthScale;
                float _SpecSharpness;
                float _SpecGain;
                float _WaveShadeStrength;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float  _WaterLabSimEnabled;
            float4 _WaterLabWaterColor;
            float4 _WaterLabSigma;
            float4 _WaterLabScatterColor;
            float4 _WaterLabSunColor;
            float4 _WaterLabSunDir;
            float  _WaterLabRefractionFactor;

            Texture2D<float4> _WaterLabHeightTex; SAMPLER(sampler_WaterLabHeightTex);
            Texture2D<float4> _WaterLabGroundTex; SAMPLER(sampler_WaterLabGroundTex);
            Texture2D<float4> _WaterLabStaticGroundTex; SAMPLER(sampler_WaterLabStaticGroundTex);
            Texture2D<float4> _WaterLabCausticTex; SAMPLER(sampler_WaterLabCausticTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);
            TEXTURE3D(_CausticsTex); SAMPLER(sampler_CausticsTex);
            TEXTURE2D(_WaterLabSandMap); SAMPLER(sampler_WaterLabSandMap);
            TEXTURE2D(_CloudNoise); SAMPLER(sampler_CloudNoise);

            // 云影全局族（参照库 CloudShadowManager 实名；PostLineToonLit 同款声明）。
            // PostLine 链由 PostLineEnvironment 每帧下发；CellShade 下 CloudSeaWeather
            // 调 PostLineEnvironment.PushCloudShadowGlobals 补推——两风格同一套公式。
            float  _CloudScale;
            float  _CloudWorldY;
            float  _CloudSpeed;
            float  _CloudContrast;
            float  _CloudThreshold;
            float  _CloudShadowMin;
            float  _CloudDivergeAngle;
            float  _CloudPower;
            float4 _CloudDirection;
            float4 _CloudLightDirection;

            float WaterWrap(float x)
            {
                float s = x >= -x ? 1.0 : -1.0;
                return s * frac(abs(x));
            }

            float CloudSeaSignedFrac(float x)
            {
                float s = (x >= -x) ? 1.0 : -1.0;
                return s * frac(abs(x));
            }

            // 库内 3D 焦散体（WaterLabSand.shader SandCaustics 逐行：worldXZ×_CausticScale，
            // 半相位双采样交叉淡化——烘焙体 + 交叉淡化压平铺、出动态光网）。
            // noiseG = 细节法线 b 通道（低频相位源，代位原作的 sandMap.g）。
            float CloudSeaCaustics(float2 worldXZ, float noiseG)
            {
                const float2 flowDead = float2(-4.0, -4.0);
                float base = noiseG + 0.5 * _Time.y;
                float2 cuv = worldXZ * _CausticScale;

                float sa = CloudSeaSignedFrac(base + 0.5) - 1.0;
                float3 s1 = float3(cuv - 0.1 * sa * flowDead - 0.5,
                                   0.2 * (noiseG + _Time.y - 0.5));
                float a1 = SAMPLE_TEXTURE3D_LOD(_CausticsTex, sampler_CausticsTex, s1, 0).r;

                float sf = CloudSeaSignedFrac(base);
                float w = 2.0 * abs(sf - 0.5);
                float sb = 2.0 * sf - 1.0;
                float3 s2 = float3(cuv - 0.1 * sb * flowDead + 0.5,
                                   0.2 * (noiseG + _Time.y + 0.5));
                float a2 = SAMPLE_TEXTURE3D_LOD(_CausticsTex, sampler_CausticsTex, s2, 0).r;
                return lerp(a2, a1, w);
            }

            float CloudSeaRotate2(float2 v, float degrees)
            {
                float r = radians(degrees);
                float c = cos(r);
                float s = sin(r);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            // 云影光值 = PostLineToonLit.PostLineCloudShadow 逐行（参照库 ToonLit
            // GetCloudNoise 的移植件）：世界点沿光向投到云层平面，双层异采样，
            // 对比度/阈值/幂次整形后作为该点光照上限。水面与地面云影同图同相。
            float CloudSeaCloudShadow(float3 worldPos)
            {
                if (abs(_CloudLightDirection.y) < 0.05)
                    return 1.0;
                if (_CloudScale <= 0.0)
                    return 1.0;

                float t = (_CloudWorldY - worldPos.y) / _CloudLightDirection.y;
                float3 hitPos = worldPos + t * _CloudLightDirection.xyz;
                float invScale = 1.0 / _CloudScale;

                float2 cloudDir1 = CloudSeaRotate2(_CloudDirection.xy, _CloudDivergeAngle);
                float2 cloudDir2 = CloudSeaRotate2(_CloudDirection.xy, -_CloudDivergeAngle);
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

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 slopeNormal  : TEXCOORD1;
                float4 screenPos    : TEXCOORD2;   // 像素化域：画布 UV（细像素深度采样）
                float4 clipPos      : TEXCOORD3;   // 原始 clip（ndc = z/w，设备深度口径）
                float  eyeDepth     : TEXCOORD4;
                float  visibleDepth : TEXCOORD5;
                float  meshWorldY   : TEXCOORD6;   // ocean 网格基准世界高（未加 η）
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.visibleDepth = 0.0;
                OUT.slopeNormal = float3(0.0, 1.0, 0.0);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.meshWorldY = positionWS.y;   // η 位移前保存（模拟 ground 的基准面）

                if (_WaterLabSimEnabled > 0.5)
                {
                    // 世界 XZ → 域 UV（_WaterLabOrigin = (centerX, size, centerZ, cells)，
                    // 与 WaterLabOcean VS 同式）。
                    float res = _WaterLabOrigin.w;
                    float2 uv = saturate((positionWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5);
                    float2 fc = max(uv * res - 0.5, 0.0);
                    fc = min(fc, res - 1.0);
                    int2 ic = (int2)fc;
                    int ix = clamp(ic.x, 0, (int)res - 1);
                    int iy = clamp(ic.y, 0, (int)res - 1);

                    // 顶点高度 = (η南 + η北) / 2（WaterLabOcean VS 同式）。
                    int iyS = max(iy - 1, 0);
                    int iyN = min(iy + 1, (int)res - 1);
                    float hS = _WaterLabHeightTex.Load(int3(ix, iyS, 0)).r * 5.0 - 1.0;
                    float hN = _WaterLabHeightTex.Load(int3(ix, iyN, 0)).r * 5.0 - 1.0;
                    positionWS.y += (hS + hN) * 0.5;

                    // 坡度法线（GB 反解，y=sqrt 重建）。
                    float4 own = _WaterLabHeightTex.Load(int3(ix, iy, 0));
                    float nx = own.g * 2.0 - 1.0;
                    float nz = own.b * 2.0 - 1.0;
                    OUT.slopeNormal = float3(nx, sqrt(max(1.0 - (nx * nx + nz * nz), 0.0)), nz);

                    // 可见水深 = η − ground（同点双线性配对）。
                    float eta = SAMPLE_TEXTURE2D_LOD(_WaterLabHeightTex,
                        sampler_WaterLabHeightTex, uv, 0).r * 5.0 - 1.0;
                    // 地面取 **t0 静止地形**：可见海床是静态 Blender 网格，活沙场的
                    // 沉积/冲蚀不能让水深跟着漂（低潮/浅滩发白的根因之一）。
                    float ground = SAMPLE_TEXTURE2D_LOD(_WaterLabStaticGroundTex,
                        sampler_WaterLabStaticGroundTex, uv, 0).r * 5.0 - 1.0;
                    OUT.visibleDepth = max(eta - ground, 0.0);
                }

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.clipPos = OUT.positionCS;
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                if (_CloudSeaPixelDomain < 0.5) clip(-1);   // PostLine：让位给前向趟

                float3 wpos = IN.positionWS;
                if (_WaterLabSimEnabled > 0.5)
                    clip(IN.visibleDepth - 0.00001);

                // ---- 遮挡门（等效 ZTest LEqual，对细像素深度的块锚点采样）----
                // 本趟没有硬件深度测试（叠加域画布无深度附件）：被更近几何（云台/角色）
                // 挡住的水面像素必须手动裁掉，否则水面叠画到前景云台上（首轮实拍：
                // 30 m 域整块压在台面上）。口径同 CloudSeaWater 趟 1。
                {
                    float2 centerUv = IN.screenPos.xy / IN.screenPos.w;
                    float storedRaw = SAMPLE_TEXTURE2D(_PixelartDepthBuffer,
                        sampler_PixelartDepthBuffer, centerUv).r;
                    float ndcDepth = IN.clipPos.z / IN.clipPos.w;
#if defined(UNITY_REVERSED_Z)
                    clip(ndcDepth - storedRaw);        // reversed-Z：数值大 = 近
#else
                    clip(storedRaw - ndcDepth * 0.5 + 0.5);
#endif
                }

                // ---- 波形与细节法线（与 WaterLabOcean PS 逐字一致）----
                float phase = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    wpos.xz * 0.05).b;
                phase += _TimeParameters.x * 0.8;
                float w1 = 2.0 * WaterWrap(phase + 0.5) - 1.0;
                float2 flow = float2(0.0, 0.0);
                float2 nA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    (wpos.xz - flow * w1) * 0.2 + 0.25).rg;
                float wrap2 = WaterWrap(phase);
                float tri = 2.0 * abs(wrap2 - 0.5);
                float w2 = 2.0 * wrap2 - 1.0;
                float2 nB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    (wpos.xz - flow * w2) * 0.2 - 0.25).rg;
                float a = 2.0 * lerp(nB.r, nA.r, tri) - 1.0;
                float b = 2.0 * lerp(nB.g, nA.g, tri) - 1.0;
                float3 detailN = float3(a, sqrt(max(1.0 - 2.0 * b * b, 0.0)), b);
                float3 N = normalize(detailN + IN.slopeNormal);

                // ---- 折射（底色 = 艺术画布快照；UV 同屏幕式，纵横比按画布）----
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 baseUV = IN.screenPos.xy / IN.screenPos.w;
                float2 refrUV = baseUV + N.xz * _WaterLabRefractionFactor * _RefractionGain * float2(1.0, -aspect);

                // 深度安全门：细像素设备深度直比（reversed-Z：存储大 = 近；同 CloudSeaWater 趟 1）。
                // 背景比水面近（折射点落到云台/角色上）→ 回退不偏移。
                float storedRaw = SAMPLE_TEXTURE2D(_PixelartDepthBuffer,
                    sampler_PixelartDepthBuffer, refrUV).r;
                float ndcDepth = IN.clipPos.z / IN.clipPos.w;
                float2 finalUV = refrUV;
#if defined(UNITY_REVERSED_Z)
                if (storedRaw > ndcDepth)
                    finalUV = baseUV;
#else
                if (storedRaw < ndcDepth * 0.5 + 0.5)
                    finalUV = baseUV;
#endif

                // ---- 凸出海床的实体保护（鱼/珊瑚/礁石不吃整根水柱的水色）----
                // 细像素深度 → 可见点世界坐标（口径 = PixelartShading.shader 的
                // PixelartPositionWSFromDepth）；海床高按**可见点自己的 XZ** 采 **t0 静止
                // 地形**（_WaterLabStaticGroundTex：活沙冲蚀后仍以静态海床网格为准，否则
                // 冲蚀区沙面会被当成凸起、整片放行水色）。离床越高，水色让位越多。
                float sceneRaw = SAMPLE_TEXTURE2D(_PixelartDepthBuffer,
                    sampler_PixelartDepthBuffer, baseUV).r;
#if !UNITY_REVERSED_Z
                sceneRaw = sceneRaw * 2.0 - 1.0;
#endif
                float3 sceneWS = ComputeWorldSpacePosition(baseUV, sceneRaw, UNITY_MATRIX_I_VP);
                float2 visibleUv = saturate(
                    (sceneWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5);
                float visibleGround = SAMPLE_TEXTURE2D_LOD(_WaterLabStaticGroundTex,
                    sampler_WaterLabStaticGroundTex, visibleUv, 0).r * 5.0 - 1.0;
                float aboveBed = sceneWS.y - (IN.meshWorldY + visibleGround);
                float protect = smoothstep(_ObjectProtectStart, _ObjectProtectFull, aboveBed)
                    * _ObjectProtectStrength;

                // 保护像素按自身 UV 采底色（否则折射偏移会把鱼身换成十几像素外的沙色）。
                float3 bg = SAMPLE_TEXTURE2D(_WaterLabPixelartScene, sampler_WaterLabPixelartScene,
                    lerp(finalUV, baseUV, protect)).rgb;

                // ---- 云影（参照库同式，与地面云影同图同相）----
                // 只压一次（在最终 col 上统一乘，见下方）：底图与水面各自再乘一次会把
                // 影底平方（0.55²=0.3），水下鱼/珊瑚全黑 —— 2026-11-21 实拍教训。
                float cloudLight = CloudSeaCloudShadow(wpos);

                // ---- 焦散（库内 3D 焦散体，WaterLabSand 同源）：加到**折射底图**上 ----
                // 读起来是「透过水看到沙面上的动态光网」，不是水面自己发亮。
                // 沙地噪声图 G 通道 = 原作 sandMap.g（worldXZ×0.3 的低频流相位）
                float noiseG = SAMPLE_TEXTURE2D(_WaterLabSandMap, sampler_WaterLabSandMap,
                    wpos.xz * 0.3).g;
                float caustics = CloudSeaCaustics(wpos.xz, noiseG);
                // 沙堡 WaterLabSand 同式：causticsVal = len(太阳色) × exp2(−1.731·水深) × 体采样，
                // 直接加到沙面 albedo（不是乘太阳色再加）。保护对象不叠沙面光网。
                float causticVal = _CausticGain * length(_WaterLabSunColor.rgb)
                    * exp2(-1.731234 * min(IN.visibleDepth * _CausticDepthScale, 6.0)) * caustics
                    * (1.0 - protect);
                bg = saturate(bg + causticVal);

                // ---- 透射 / 散射 / 镜面（WaterLabOcean 同式；阴影软化项取 1，见文件头差异③）----
                // ---- 社区 3 段深度色带 + 吸收（beach-sandbox water.gdshader 配方）----
                // 只留 edge → turquoise → mid_blue 三层；吸收比社区低（更通透）。
                float h = IN.visibleDepth;
                float3 trans = exp(-_Absorption.rgb * h);
                float3 scat = lerp(_BandEdge.rgb, _BandTurq.rgb, smoothstep(0.14, 0.28, h));
                scat = lerp(scat, _BandMid.rgb, smoothstep(0.34, 0.50, h));
                float3 opac = max(1.0 - trans, (float3)(_ShallowBody * smoothstep(0.001, 0.015, h)));
                // 实体保护：水色覆盖按保护度让位，只留 _ObjectVeil 的薄纱——鱼身保留
                // 自己的暖色，仍能读出一层「在水下」的青膜。
                opac = max(opac * (1.0 - protect), (float3)(_ObjectVeil * protect));
                float3 col = bg * (1.0 - opac) + scat * opac;

                float3 V = GetCameraPositionWS() - wpos;
                float3 Vn = normalize(V);
                float NdotV = saturate(dot(Vn, N));
                float3 H = normalize(Vn + _WaterLabSunDir.xyz);
                // 硬边 toon 闪光（社区同式；像素域里读成稀疏白点）
                float glint = smoothstep(0.5, 0.6, pow(saturate(dot(N, H)), 2500.0))
                    * (1.0 - 0.35 * max(opac.r, max(opac.g, opac.b))) * smoothstep(0.004, 0.02, h);
                col += _WaterLabSunColor.rgb * glint * 0.8;

                // ---- 波峰/波谷 toon 明暗（场景适配，非原作）：静止帧也读得出水面在起伏 ----
                // 波面法线（模拟坡度 + 细节法线）对太阳水平方位做明暗；波峰朝光侧
                // 再叠一层浅色亮带（像素域读成白沫）。_WaveShadeStrength=0 时各项归零、
                // 回到原作的平水面观感。
                float3 waveLightDir = normalize(float3(_WaterLabSunDir.x, 0.35, _WaterLabSunDir.z));
                float crest = saturate(dot(N, waveLightDir) * 0.5 + 0.5);
                col *= 1.0 + _WaveShadeStrength * (crest - 0.5) * 1.8;
                col = lerp(col, min(_BandEdge.rgb * 1.08, 1.0),
                    smoothstep(0.58, 0.70, crest) * _WaveShadeStrength * 0.35);

                col *= lerp(1.0, cloudLight, _CloudShadowStrength);

                // 全域同 α：ocean 网格 90 m 已盖住相机足迹，不再按 30 m 模拟域缘渐隐
                // （旧渐隐会在画面上留一圈方形域界）；域外简版水退到 90 m 外的远景兜底。
                float alpha = saturate(IN.visibleDepth * 100.0) * _AlphaGain;

                return float4(col, alpha);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2 / UniversalForward：PostLine / 普通 URP 前向 = WaterLabOcean 正身。
        // ==================================================================
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertForward
            #pragma fragment FragForward

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _CloudSeaPixelDomain;

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterLabDetailNormal_ST;
                float _EdgeFade;
                float _SigmaScale;
                float _AlphaGain;
                float _CausticGain;
                float _CausticScale;
                float _RefractionGain;
                float _CloudShadowStrength;
                float4 _WaterTint;
                float _WaterTintStrength;
                float4 _BandEdge;
                float4 _BandTurq;
                float4 _BandMid;
                float4 _Absorption;
                float _ShallowBody;
                float _ObjectProtectStart;
                float _ObjectProtectFull;
                float _ObjectProtectStrength;
                float _ObjectVeil;
                float _CausticDepthScale;
                float _SpecSharpness;
                float _SpecGain;
                float _WaveShadeStrength;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            float4 _WaterLabWaterColor;
            float4 _WaterLabSigma;
            float4 _WaterLabScatterColor;
            float4 _WaterLabSunColor;
            float  _WaterLabRefractionFactor;

            Texture2D<float4> _WaterLabHeightTex; SAMPLER(sampler_WaterLabHeightTex);
            Texture2D<float4> _WaterLabGroundTex; SAMPLER(sampler_WaterLabGroundTex);
            Texture2D<float4> _WaterLabStaticGroundTex; SAMPLER(sampler_WaterLabStaticGroundTex);
            Texture2D<float4> _WaterLabCausticTex; SAMPLER(sampler_WaterLabCausticTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);
            TEXTURE3D(_CausticsTex); SAMPLER(sampler_CausticsTex);
            TEXTURE2D(_WaterLabSandMap); SAMPLER(sampler_WaterLabSandMap);
            TEXTURE2D(_CloudNoise); SAMPLER(sampler_CloudNoise);

            // 云影全局族（同 Pass 1：参照库 CloudShadowManager 实名移植）。
            float  _CloudScale;
            float  _CloudWorldY;
            float  _CloudSpeed;
            float  _CloudContrast;
            float  _CloudThreshold;
            float  _CloudShadowMin;
            float  _CloudDivergeAngle;
            float  _CloudPower;
            float4 _CloudDirection;
            float4 _CloudLightDirection;

            float WaterWrapForward(float x)
            {
                float s = x >= -x ? 1.0 : -1.0;
                return s * frac(abs(x));
            }

            float CloudSeaSignedFracF(float x)
            {
                float s = (x >= -x) ? 1.0 : -1.0;
                return s * frac(abs(x));
            }

            float CloudSeaCausticsF(float2 worldXZ, float noiseG)
            {
                const float2 flowDead = float2(-4.0, -4.0);
                float base = noiseG + 0.5 * _Time.y;
                float2 cuv = worldXZ * _CausticScale;

                float sa = CloudSeaSignedFracF(base + 0.5) - 1.0;
                float3 s1 = float3(cuv - 0.1 * sa * flowDead - 0.5,
                                   0.2 * (noiseG + _Time.y - 0.5));
                float a1 = SAMPLE_TEXTURE3D_LOD(_CausticsTex, sampler_CausticsTex, s1, 0).r;

                float sf = CloudSeaSignedFracF(base);
                float w = 2.0 * abs(sf - 0.5);
                float sb = 2.0 * sf - 1.0;
                float3 s2 = float3(cuv - 0.1 * sb * flowDead + 0.5,
                                   0.2 * (noiseG + _Time.y + 0.5));
                float a2 = SAMPLE_TEXTURE3D_LOD(_CausticsTex, sampler_CausticsTex, s2, 0).r;
                return lerp(a2, a1, w);
            }

            float CloudSeaRotate2F(float2 v, float degrees)
            {
                float r = radians(degrees);
                float c = cos(r);
                float s = sin(r);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            float CloudSeaCloudShadowF(float3 worldPos)
            {
                if (abs(_CloudLightDirection.y) < 0.05)
                    return 1.0;
                if (_CloudScale <= 0.0)
                    return 1.0;

                float t = (_CloudWorldY - worldPos.y) / _CloudLightDirection.y;
                float3 hitPos = worldPos + t * _CloudLightDirection.xyz;
                float invScale = 1.0 / _CloudScale;

                float2 cloudDir1 = CloudSeaRotate2F(_CloudDirection.xy, _CloudDivergeAngle);
                float2 cloudDir2 = CloudSeaRotate2F(_CloudDirection.xy, -_CloudDivergeAngle);
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

            struct AttributesForward
            {
                float4 positionOS : POSITION;
            };

            struct VaryingsForward
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 slopeNormal  : TEXCOORD1;
                float4 screenPos    : TEXCOORD2;
                float  eyeDepth     : TEXCOORD3;
                float  visibleDepth : TEXCOORD4;
                float  meshWorldY   : TEXCOORD5;   // ocean 网格基准世界高（未加 η）
            };

            VaryingsForward VertForward(AttributesForward IN)
            {
                VaryingsForward OUT;
                OUT.visibleDepth = 0.0;
                OUT.slopeNormal = float3(0.0, 1.0, 0.0);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.meshWorldY = positionWS.y;   // η 位移前保存（模拟 ground 的基准面）

                if (_WaterLabSimEnabled > 0.5)
                {
                    float res = _WaterLabOrigin.w;
                    float2 uv = saturate((positionWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5);
                    float2 fc = max(uv * res - 0.5, 0.0);
                    fc = min(fc, res - 1.0);
                    int2 ic = (int2)fc;
                    int ix = clamp(ic.x, 0, (int)res - 1);
                    int iy = clamp(ic.y, 0, (int)res - 1);
                    int iyS = max(iy - 1, 0);
                    int iyN = min(iy + 1, (int)res - 1);
                    float hS = _WaterLabHeightTex.Load(int3(ix, iyS, 0)).r * 5.0 - 1.0;
                    float hN = _WaterLabHeightTex.Load(int3(ix, iyN, 0)).r * 5.0 - 1.0;
                    positionWS.y += (hS + hN) * 0.5;

                    float4 own = _WaterLabHeightTex.Load(int3(ix, iy, 0));
                    float nx = own.g * 2.0 - 1.0;
                    float nz = own.b * 2.0 - 1.0;
                    OUT.slopeNormal = float3(nx, sqrt(max(1.0 - (nx * nx + nz * nz), 0.0)), nz);

                    float eta = SAMPLE_TEXTURE2D_LOD(_WaterLabHeightTex,
                        sampler_WaterLabHeightTex, uv, 0).r * 5.0 - 1.0;
                    // 地面取 **t0 静止地形**：可见海床是静态 Blender 网格，活沙场的
                    // 沉积/冲蚀不能让水深跟着漂（低潮/浅滩发白的根因之一）。
                    float ground = SAMPLE_TEXTURE2D_LOD(_WaterLabStaticGroundTex,
                        sampler_WaterLabStaticGroundTex, uv, 0).r * 5.0 - 1.0;
                    OUT.visibleDepth = max(eta - ground, 0.0);
                }

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;
                return OUT;
            }

            float4 FragForward(VaryingsForward IN) : SV_Target
            {
                if (_CloudSeaPixelDomain > 0.5) clip(-1);   // CellShade：让位给像素化趟

                float3 wpos = IN.positionWS;
                if (_WaterLabSimEnabled > 0.5)
                    clip(IN.visibleDepth - 0.00001);

                float phase = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    wpos.xz * 0.05).b;
                phase += _TimeParameters.x * 0.8;
                float w1 = 2.0 * WaterWrapForward(phase + 0.5) - 1.0;
                float2 flow = float2(0.0, 0.0);
                float2 nA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    (wpos.xz - flow * w1) * 0.2 + 0.25).rg;
                float wrap2 = WaterWrapForward(phase);
                float tri = 2.0 * abs(wrap2 - 0.5);
                float w2 = 2.0 * wrap2 - 1.0;
                float2 nB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    (wpos.xz - flow * w2) * 0.2 - 0.25).rg;
                float a = 2.0 * lerp(nB.r, nA.r, tri) - 1.0;
                float b = 2.0 * lerp(nB.g, nA.g, tri) - 1.0;
                float3 detailN = float3(a, sqrt(max(1.0 - 2.0 * b * b, 0.0)), b);
                float3 N = normalize(detailN + IN.slopeNormal);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 baseUV = IN.screenPos.xy / IN.screenPos.w;
                float2 refrUV = baseUV + N.xz * _WaterLabRefractionFactor * _RefractionGain * float2(1.0, -aspect);

                float bgEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                float2 finalUV = (bgEye < IN.eyeDepth) ? baseUV : refrUV;
                // ---- 凸出海床的实体保护（同 Pass 1；鱼/珊瑚/礁石保留原色）----
                // _CameraDepthTexture → 可见点世界坐标（URP 标准口径：SampleSceneDepth +
                // ComputeWorldSpacePosition；非 reversed-Z 平台把 [0,1] 还原到 [-1,1]）。
                float sceneRaw = SampleSceneDepth(baseUV);
#if !UNITY_REVERSED_Z
                sceneRaw = sceneRaw * 2.0 - 1.0;
#endif
                float3 sceneWS = ComputeWorldSpacePosition(baseUV, sceneRaw, UNITY_MATRIX_I_VP);
                float2 visibleUv = saturate(
                    (sceneWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5);
                float visibleGround = SAMPLE_TEXTURE2D_LOD(_WaterLabStaticGroundTex,
                    sampler_WaterLabStaticGroundTex, visibleUv, 0).r * 5.0 - 1.0;
                float aboveBed = sceneWS.y - (IN.meshWorldY + visibleGround);
                float protect = smoothstep(_ObjectProtectStart, _ObjectProtectFull, aboveBed)
                    * _ObjectProtectStrength;

                // 保护像素按自身 UV 采底色（同 Pass 1：折射偏移不作用在鱼身上）。
                float3 bg = SampleSceneColor(lerp(finalUV, baseUV, protect));

                // ---- 云影（参照库同式，与地面云影同图同相）+ 焦散（库内 3D 体）----
                float cloudLight = CloudSeaCloudShadowF(wpos);
                // 沙地噪声图 G 通道 = 原作 sandMap.g（worldXZ×0.3 的低频流相位）
                float noiseG = SAMPLE_TEXTURE2D(_WaterLabSandMap, sampler_WaterLabSandMap,
                    wpos.xz * 0.3).g;
                float caustics = CloudSeaCausticsF(wpos.xz, noiseG);
                // 沙堡 WaterLabSand 同式：causticsVal = len(太阳色) × exp2(−1.731·水深) × 体采样，
                // 直接加到沙面 albedo（不是乘太阳色再加）。保护对象不叠沙面光网。
                float causticVal = _CausticGain * length(_WaterLabSunColor.rgb)
                    * exp2(-1.731234 * min(IN.visibleDepth * _CausticDepthScale, 6.0)) * caustics
                    * (1.0 - protect);
                bg = saturate(bg + causticVal);

                // ---- 社区 3 段深度色带 + 吸收（同 Pass 1；只留三层）----
                float h = IN.visibleDepth;
                float3 trans = exp(-_Absorption.rgb * h);
                float3 scat = lerp(_BandEdge.rgb, _BandTurq.rgb, smoothstep(0.14, 0.28, h));
                scat = lerp(scat, _BandMid.rgb, smoothstep(0.34, 0.50, h));
                float3 opac = max(1.0 - trans, (float3)(_ShallowBody * smoothstep(0.001, 0.015, h)));
                // 实体保护：水色覆盖按保护度让位，只留 _ObjectVeil 的薄纱——鱼身保留
                // 自己的暖色，仍能读出一层「在水下」的青膜。
                opac = max(opac * (1.0 - protect), (float3)(_ObjectVeil * protect));
                float3 col = bg * (1.0 - opac) + scat * opac;

                float3 V = GetCameraPositionWS() - wpos;
                float3 Vn = normalize(V);
                float NdotV = saturate(dot(Vn, N));
                float3 H = normalize(Vn + _WaterLabSunDir.xyz);
                float glint = smoothstep(0.5, 0.6, pow(saturate(dot(N, H)), 2500.0))
                    * (1.0 - 0.35 * max(opac.r, max(opac.g, opac.b))) * smoothstep(0.004, 0.02, h);
                col += _WaterLabSunColor.rgb * glint * 0.8;

                // ---- 波峰/波谷 toon 明暗（场景适配，非原作）：静止帧也读得出水面在起伏 ----
                // 波面法线（模拟坡度 + 细节法线）对太阳水平方位做明暗；波峰朝光侧
                // 再叠一层浅色亮带（像素域读成白沫）。_WaveShadeStrength=0 时各项归零、
                // 回到原作的平水面观感。
                float3 waveLightDir = normalize(float3(_WaterLabSunDir.x, 0.35, _WaterLabSunDir.z));
                float crest = saturate(dot(N, waveLightDir) * 0.5 + 0.5);
                col *= 1.0 + _WaveShadeStrength * (crest - 0.5) * 1.8;
                col = lerp(col, min(_BandEdge.rgb * 1.08, 1.0),
                    smoothstep(0.58, 0.70, crest) * _WaveShadeStrength * 0.35);

                col *= lerp(1.0, cloudLight, _CloudShadowStrength);

                // 全域同 α：ocean 网格 90 m 已盖住相机足迹，不再按 30 m 模拟域缘渐隐
                // （旧渐隐会在画面上留一圈方形域界）；域外简版水退到 90 m 外的远景兜底。
                float alpha = saturate(IN.visibleDepth * 100.0) * _AlphaGain;

                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
