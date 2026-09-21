// ============================================================================
// PirateToon.shader —— 等距像素卡通 · 赛璐璐本体 + 常驻反向壳描边
//
// 【规格出处】docs/技术/渲染/渲染管线-等距像素卡通.md §4（赛璐璐）与 §5（描边）；
//   技术依据 docs/技术/渲染/调研-赛璐璐与色带.md / 调研-反向壳描边.md。
//   本 shader 是步骤 2（M1 工作项 1a/1b）的核心载体：试点对象先接它，批量换血（M2e）后见分晓。
//
// 【Pass 结构与 LightMode 唯一性】（铁律，见描边Shader调试.md §八-2 的静默丢弃事故）
//   Pass 1 ToonBase   = "UniversalForward"   赛璐璐本体
//   Pass 2 ToonInk    = "SRPDefaultUnlit"    常驻墨色反向壳描边（独立槽位，绝不复用 UniversalForward）
//   Pass 3 DepthOnly  = "DepthOnly"          极简深度（Depth 需求随海面方案裁决，先保留无害）
//   无 ShadowCaster：等距像素卡通下实时阴影全关（渲染篇 §6），落地感由 blob shadow 承担。
//
// 【赛璐璐要点（逐条对应渲染篇 §4）】
//   1) 替换式预制暗部色：暗部是 _ShadowColor 整色替换，**不做纯乘暗、不在 shader 里跑 HSV**；
//      3 档模式 = 暗侧内部用 _ShadowThreshold2 再切一刀（每档阈值+颜色独立暴露）。
//   2) 硬切主路径 + 羽化参数：_ShadowFeather=0 时 step() 硬切（640×360 nearest 自动像素化边界）；
//      羽化是 0~1 参数（M1 裁决 #2「硬切 vs 羽化」的对照开关）。
//   3) Bayer 4×4 ordered dithering 加在**档位选择值**上（t = NdotL 重映射 + 顶点色R偏移 + (bayer-0.5)/档数）；
//      矩阵**中点归一化 (m+0.5)/16**（直接 /16 整体偏亮，调研-赛璐璐 §4）。
//      Bayer 像素坐标按像素化块对齐（screenPx ÷ 块尺寸再取整）——渲染篇红线 4 要求颜色处理
//      在 360p 域内一致：块内所有屏幕像素取同一 Bayer 相位，nearest 上采样后才不会出中间调。
//   4) 单平行光 + 扁平环境光走**全局 uniform**（_ToonLightDirWS/_ToonLightColor/_ToonAmbientColor，
//      由 ToonLightDriver 每帧从 RenderSettings.sun 下发），**不 include Lighting.hlsl、不取
//      shadowCoord、无任何 shadow 关键字**——砍掉全部 shadow variant（渲染篇 §4.6）。
//   5) 顶点色 R = 色带阈值偏移（Xrd 工作流）：R=0.5 无偏移，美术刷顶点色即可逐区域挪切分点；
//      GBA = 平滑法线（×0.5+0.5 编码，仅描边 Pass 消费，裁决 #8 的编码提案）。
//   6) 无高光、无 rim（rim 与描边叠加会"双倍边缘"，渲染篇 §4.3）。
//
// 【描边要点（逐条对应渲染篇 §5）】
//   - ToonRP 正交线宽公式：posCS.xy += clipN.xy（x 乘 1/aspect 修正）× (2/RT高) × _OutlinePixels；
//     正交 w≡1 自动等宽；**不对 clipN.xy 二次归一**（法线朝屏时数值不稳）。
//     _PixelRTHeight/_PixelRTWidth 由 PixelationRendererFeature.OnCameraSetup 全局下发（=360 档），
//     RT 1px 线在 1080p 屏幕上放大为 3px，线宽天然均匀（渲染篇红线 1：描边在像素化之前）。
//   - 墨色 _InkColor 与 UI 令牌 INK 同色（3D/2D 描边同色统一）；试点材质由装配脚本从
//     StickTokens 派生，默认值只是兜底。
//   - Cull Front / ZWrite Off / ZTest LEqual / Offset 1 1（斜面近平行处防 z-fight）。
//   - 平滑法线读顶点色 GBA；未烘焙网格（color.a < 0.02）回退 normalOS——低模硬边直接外扩会
//     裂线（风险 R1），烘焙路径见 SmoothNormalsBaker。
//
// 【include 纪律】本 shader 只 include Core.hlsl（不用 Lighting.hlsl——单光走全局 uniform，
//   这正是砍 shadow variant 的实现方式）。MixFog/ComputeFogFactor 由 Core.hlsl 提供。
//   改 include 必须重过「真实 GPU 会话零 shader error」门（AGENTS 核心指令 2）。
//
// [_DebugMode 分档]（图形学调试截图规范）
//   0 正常合成 / 1 档位灰阶（色带切分位置）/ 2 NdotL 重映射原始值 / 3 只描边（本体 discard）
//   / 4 Bayer 图案 / 5 只亮部色 / 6 只暗部色
// ============================================================================

Shader "PirateCrew/PirateToon"
{
    Properties
    {
        // ---- 赛璐璐本体（2 档起步；3 档 = 暗侧内部再切一刀）----
        _BaseColor              ("亮部色", Color) = (0.93, 0.85, 0.68, 1.0)
        _ShadowColor            ("暗部色（替换式预制色，板内选色）", Color) = (0.45, 0.43, 0.60, 1.0)
        _ShadowThreshold        ("主色带阈值（t 低于它进暗部）", Range(0.0, 1.0)) = 0.50
        [ToggleUI] _BandCount   ("档数 2 或 3", Range(2.0, 3.0)) = 2.0
        _ShadowColor2           ("暗部内部深档色（档数=3 时生效）", Color) = (0.32, 0.30, 0.45, 1.0)
        _ShadowThreshold2       ("暗部内部切分阈值（须低于主阈值）", Range(0.0, 1.0)) = 0.25
        _ShadowFeather          ("色带羽化宽度 0=硬切（裁决#2 对照开关）", Range(0.0, 1.0)) = 0.0

        // ---- dither 与顶点色 ----
        _DitherStrength         ("Bayer 抖动强度（0=关；默认关——参照图无有序抖动，开大会出编织网）", Range(0.0, 1.0)) = 0.0
        _VertexThresholdStrength("顶点色R阈值偏移强度（0=忽略顶点色）", Range(0.0, 1.0)) = 1.0

        // ---- 常驻描边 ----
        _InkColor               ("描边墨色（与 UI 令牌 INK 同色）", Color) = (0.09, 0.08, 0.11, 1.0)
        _OutlinePixels          ("描边线宽（RT 像素数；1080p 屏幕×3）", Range(0.25, 4.0)) = 1.0

        // ---- 自发光（灯体/宝石；裁决 D1：夜色基调 + 自发光灯，柔光靠手绘径向贴片而非 bloom）----
        _EmissiveColor          ("自发光色（黑 = 关）", Color) = (0, 0, 0, 1)
        _EmissiveStrength       ("自发光强度", Range(0.0, 4.0)) = 0.0

        // ---- 调试 ----
        _DebugMode              ("调试 0正常 1档位 2光照 3只描边 4Bayer 5亮部 6暗部", Range(0.0, 6.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry+50"
            "IgnoreProjector" = "True"
        }
        LOD 100

        // ====================================================================
        // Pass 1 / ToonBase：赛璐璐本体
        // ====================================================================
        Pass
        {
            Name "ToonBase"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   ToonBaseVertex
            #pragma fragment ToonBaseFragment
            // 雾关键字与既有 shader 同口径（裁决 #5 之前保留；试点场景无雾，不产生额外变体负担）。
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // CBUFFER 字段顺序 = Properties 声明顺序（SRP Batcher 兼容），三个 Pass 共用同一份。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowThreshold;
                float  _BandCount;
                float4 _ShadowColor2;
                float  _ShadowThreshold2;
                float  _ShadowFeather;
                float  _DitherStrength;
                float  _VertexThresholdStrength;
                float4 _InkColor;
                float  _OutlinePixels;
                float4 _EmissiveColor;
                float  _EmissiveStrength;
                float  _DebugMode;
            CBUFFER_END

            // 单光与环境光走全局 uniform（ToonLightDriver 每帧下发；渲染篇 §4.6「跳过 shadow
            // variant」的实现）。带初始化值兜底：未下发（材质预览/未挂驱动）时也有光可看。
            float4 _ToonLightDirWS    = float4(-0.45, 0.75, -0.30, 0.0); // 指向光源（左上惯例）
            float4 _ToonLightColor    = float4(1.0, 1.0, 1.0, 1.0);
            float4 _ToonAmbientColor  = float4(0.22, 0.24, 0.30, 1.0);
            float  _PixelRTHeight     = 360.0;

            // Bayer 4×4（中点归一化在采样处 +0.5/16；直接 /16 会整体偏亮，调研-赛璐璐 §4）。
            static const int kBayer4[16] = {
                 0,  8,  2, 10,
                12,  4, 14,  6,
                 3, 11,  1,  9,
                15,  7, 13,  5 };

            struct AttributesBase
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;       // R=阈值偏移（0.5 无偏移）；GBA=平滑法线编码
            };

            struct VaryingsBase
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
                float4 vertColor  : COLOR0;
            };

            // 色带切分：_ShadowFeather=0 走 step() 硬切（主路径），>0 走 smoothstep 羽化
            // （过渡全宽 = feather；M1 裁决 #2 的对照参数）。
            half BandCut(half t, half threshold, half feather)
            {
                if (feather < 0.001)
                    return step(threshold, t);
                half f = max(feather, 0.001) * 0.5;
                return smoothstep(threshold - f, threshold + f, t);
            }

            VaryingsBase ToonBaseVertex(AttributesBase IN)
            {
                VaryingsBase OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = p.positionCS;
                OUT.normalWS   = n.normalWS;
                OUT.fogFactor  = ComputeFogFactor(p.positionCS.z);
                OUT.vertColor  = IN.color;
                return OUT;
            }

            half4 ToonBaseFragment(VaryingsBase IN) : SV_Target
            {
                // [DEBUG] 档 3：只描边——本体整面丢弃，画面只剩反壳墨线（描边 Pass 不受 _DebugMode 影响）。
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    discard;

                float3 nrm = normalize(IN.normalWS);

                // 档位选择值 t：NdotL 重映射到 [0,1]（替换式色带下重映射不压缩对比——颜色不乘 t）
                // + 顶点色 R 偏移（Xrd：R=0.5 无偏移）+ Bayer dither（幅度 ≈ 1/档数个量化步长）。
                half t = saturate(dot(nrm, normalize(_ToonLightDirWS.xyz)) * 0.5 + 0.5);
                t += (IN.vertColor.r - 0.5) * _VertexThresholdStrength;

                // Bayer 坐标按像素化块对齐：块内所有屏幕像素取同一相位（红线 4——nearest 上采样
                // 后不得引入中间色）。块尺寸 = 屏幕高 ÷ RT 高（1080/360 = 3）。
                float pixelScale = max(_ScreenParams.y / max(_PixelRTHeight, 1.0), 1.0);
                uint2 bayerPx = (uint2)floor(IN.positionCS.xy / pixelScale);
                half bayer = (kBayer4[(bayerPx.y & 3) * 4 + (bayerPx.x & 3)] + 0.5) / 16.0;
                t += (bayer - 0.5) * _DitherStrength / max(_BandCount, 2.0);
                t = saturate(t);

                // [DEBUG] 档 4：Bayer 图案本体（网格灰阶；确认相位与块对齐——每个 3×3 屏幕块应为同色）。
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                    return half4(bayer.xxx, 1.0);

                // [DEBUG] 档 2：NdotL 重映射原始值（灰阶连续渐变；对照档 1 看切分位置）。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                {
                    half raw = saturate(dot(nrm, normalize(_ToonLightDirWS.xyz)) * 0.5 + 0.5);
                    return half4(raw.xxx, 1.0);
                }

                // 色带：主切（暗↔亮）+ 可选暗侧内部切（3 档模式，th2 < th1，t 更低 → 更深档）。
                half  kMain = BandCut(t, (half)_ShadowThreshold, (half)_ShadowFeather);
                half3 darkSide = _ShadowColor.rgb;
                if (_BandCount > 2.5)
                {
                    half kInner = BandCut(t, (half)_ShadowThreshold2, (half)_ShadowFeather);
                    darkSide = lerp(_ShadowColor2.rgb, _ShadowColor.rgb, kInner);
                }
                half3 albedo = lerp(darkSide, _BaseColor.rgb, kMain);

                // 光色乘在亮暗两侧（替换色已含色相偏移，光色只给整体明暗/暖冷），环境光加法。
                half3 color = albedo * _ToonLightColor.rgb + _ToonAmbientColor.rgb;

                // 自发光加在色带之后：灯体/宝石不受色带切分影响，夜里是一块纯亮（裁决 D1）。
                color += _EmissiveColor.rgb * _EmissiveStrength;

                // [DEBUG] 档 1：档位灰阶（kMain 直接输出——看色带切分位置与 dither 撕边形态；
                //   3 档时输出 0=深档 0.5=暗档 1=亮档的离散灰阶）。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                {
                    half bandGray = kMain;
                    if (_BandCount > 2.5)
                        bandGray = lerp(lerp(0.0, 0.5, BandCut(t, (half)_ShadowThreshold2, (half)_ShadowFeather)),
                                        1.0, kMain);
                    return half4(bandGray.xxx, 1.0);
                }
                // [DEBUG] 档 5/6：平涂亮部色 / 暗部色（对照"色带两侧各是什么色"）。
                if (_DebugMode > 4.5 && _DebugMode < 5.5)
                    return half4(_BaseColor.rgb, 1.0);
                if (_DebugMode > 5.5)
                    return half4(_ShadowColor.rgb, 1.0);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / ToonInk：常驻墨色反向壳描边（Cull Front + 顶点沿平滑法线外扩）
        //
        // 【与 PirateOutline 描边 Pass 的关系】那套是「选中/悬停反馈」（_OutlineState 三态切换、
        //   半透明、青色虚线）；本 Pass 是「风格层常驻墨线」（不透明 INK、无状态、正交线宽公式）。
        //   单位切 Toon（M2e/M3 批次）时选中反馈如何融合另行裁决，本 Pass 不背状态机。
        // ====================================================================
        Pass
        {
            Name "ToonInk"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            // 【描边物的渲染序（六轮实测的完整因果链）】
            //   ① SubShader 队列 Geometry+50：描边物（岛台/船员）整体晚于普通不透明（海面，
            //     装配里把海面材质覆写回 2000）画——否则海面 fill 全屏的本体会盖掉轮廓外
            //     的壳环（壳 ZWrite Off 不留深度，实测环 0 像素）。
            //   ② 同一 renderer 内壳先画（SRPDefaultUnlit 在 DrawObjectsPass 的 tag 列表
            //     第一位）、本体后画——重叠区由本体自然盖回，只余轮廓环。
            //   ③ 壳沿视线拉近（见 vertex）：45° 俯视下前下缘轮廓外的海面比壳更近
            //     （深度差 ~0.4u），不拉近时前缘环输给海面深度（LEqual 全灭的另一半根因）。
            // 不透明墨线：无 Blend（区别于 PirateOutline 的半透明反馈线）。

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   ToonInkVertex
            #pragma fragment ToonInkFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowThreshold;
                float  _BandCount;
                float4 _ShadowColor2;
                float  _ShadowThreshold2;
                float  _ShadowFeather;
                float  _DitherStrength;
                float  _VertexThresholdStrength;
                float4 _InkColor;
                float  _OutlinePixels;
                float4 _EmissiveColor;
                float  _EmissiveStrength;
                float  _DebugMode;
            CBUFFER_END

            float _PixelRTHeight = 360.0;

            struct AttributesInk
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;       // GBA = 平滑法线（×0.5+0.5）；a<0.02 = 未烘焙
            };

            struct VaryingsInk
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsInk ToonInkVertex(AttributesInk IN)
            {
                VaryingsInk OUT;

                // 平滑法线：顶点色 GBA 解码（×2-1）；未烘焙网格回退 normalOS（硬边低模上会裂线，
                // 属已知边界——烘焙路径 SmoothNormalsBaker 在装配链里，正常产物都带编码法线）。
                float3 smoothN = IN.color.a > 0.02
                    ? IN.color.gba * 2.0 - 1.0
                    : IN.normalOS;

                float3 normalWS = TransformObjectToWorldNormal(smoothN);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);

                // 全程在视空间做（世界单位，正交下 clip 是视空间的线性缩放）：
                //   1 RT 像素 = 2·orthoSize/RT高（世界单位）= 2 / (RT高 × |P.m11|)。
                // 【|P.m11| = 1/orthoSize（正交）；abs 必须】D3D 下 UNITY_MATRIX_P 经 GL 翻转，
                //   m11 为负——max(m11,1e-6) 钳位曾把外扩系数放大 ~5000 倍（壳推出屏幕、
                //   调试档 100% 全黑的实测事故）。
                // 【不对 normalVS.xy 归一】法线朝屏时数值不稳；倾斜面的自然变细由分量自带
                //   （ToonRP 口径）。
                float pixelWorld = (2.0 * _OutlinePixels)
                        / (max(_PixelRTHeight, 1.0) * abs(UNITY_MATRIX_P._m11) + 1e-6);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 positionVS = TransformWorldToView(positionWS);

                // 屏幕方向外扩 + 沿视线拉近（5 倍线宽量级，覆盖 45° 俯视下前缘 ~0.4u 的
                // 深度差；量级 = 拉近穿透前景的距离，像素风下不可见）。
                positionVS.xy += normalVS.xy * pixelWorld;
                positionVS.z  -= pixelWorld * 5.0;

                OUT.positionCS = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
                return OUT;
            }

            half4 ToonInkFragment(VaryingsInk IN) : SV_Target
            {
                // 描边开关：_OutlinePixels=0 时整 pass 丢弃（海面等大平面不描边——拉近补丁
                // 会让大平面的壳整面盖住本体，大平面描边也无观感意义）。
                if (_OutlinePixels < 0.01)
                    discard;
                // 常驻墨线（档 3「只描边」由本体 Pass discard 配合实现）。
                return half4(_InkColor.rgb, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 3 / DepthOnly：极简深度（与 PirateOutline 同款手写实现；Depth 需求随
        // 海面方案（M2f 裁决 #7）再定，保留本 Pass 让深度图语义完整）。
        // ====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVertex
            #pragma fragment DepthFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesDepth { float4 positionOS : POSITION; };
            struct VaryingsDepth   { float4 positionCS : SV_POSITION; };

            VaryingsDepth DepthVertex(AttributesDepth IN)
            {
                VaryingsDepth OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFragment(VaryingsDepth IN) : SV_Target
            {
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
