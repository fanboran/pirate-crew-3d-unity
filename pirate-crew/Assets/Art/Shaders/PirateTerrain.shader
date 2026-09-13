// ============================================================================
// PirateTerrain.shader —— 海盗军团夺宝 3D / 瓦片地形（程序化，无贴图）
//
// 【服务对象】BattleTerrainView 从 TerrainCatalog 构建的地形块（Cube 图元）。
//   关卡未转写瓦片时是平坦竞技场，本材质主要给"有瓦片地形"的关卡用。
//
// 【风格契约】GDD §10.4「风格化写实 + 高饱和块面」：
//   1. 按**世界高度**与**坡度**混合 沙 / 草 / 岩 三色（对应 GDD 色板三组色阶的中档）；
//   2. 低多边形**块面感**：
//      a. 片元用屏幕空间导数还原"逐面法线"（_FacetStrength），让平滑网格也呈现硬棱面；
//      b. 按世界坐标量化出"地块"（_BlockSize），用地块哈希做**逐块明暗差异**（_BlockTintStrength）——
//         这是低多边形风格最有效的辨识特征：相邻块亮度不同 → 一眼能看出块面结构；
//   3. `#2A2A2A` 描边兼容（**写实化后已退役**）：GDD 曾要求场景物描边为深灰 #2A2A2A，
//      地形块不走 inverted-hull（块体太多、成本不划算），改用**菲涅尔边缘压暗**模拟。
//      写实方向明确"无描边"，故 `_EdgeStrength` 默认值由 0.35 改为 **0**（旋钮与色值保留）；
//      BattleSceneLighting 生成地形材质时也显式写 0。要回到风格化只改这一个值即可。
//
// 【沙滩湿区 / 潮痕线 / 沙纹（2026-09-13 新增，与 PirateSurface 同口径）】
//   湿掩码公式与 PirateSurface 完全一致（同 _WaterLevelY / _WetBandWidth / _WetSandColor /
//   _WetLineMin/Max / _RippleScale…），保证跨两种材质的沙滩观感连续：
//     dy = worldY - _WaterLevelY；wetMask = 1 - saturate(dy / _WetBandWidth)
//   两处与 PirateSurface 的**刻意差异**（理由见片元注释）：
//   1) 只作用在**沙分支**：wet = wetMask × sandWeight（sandWeight = 1 - grass - rock）。
//      草坡/岩石不染沙色 —— 湿沙色是沙专属（本 shader 的三色分支里只有沙需要"打湿"）。
//   2) 逐块明暗（_BlockTintStrength）**放在湿合成之后**相乘：
//      若先乘 blockTint 再 lerp 到 _WetSandColor，会按 (1-wet) 的比例把块面亮度差洗平；
//      放到湿合成之后再乘，湿区仍保留"相邻块亮度不同"的低多边形辨识特征。
//   残留线 / 沙纹与 PirateSurface 同公式；沙纹同样仅湿掩码内生效、振幅硬钳 ≤0.15。
//
// 【细节噪声贴图 + 粗糙度分区（2026-09-13 r3 复验 N4 返工，程序化资产，仍属"0 外部贴图"）】
//   【为什么加】r3 复验 N4（高）：地形材质是**纯色平面**——unit-closeup 同一可见面 90px 内只有 7 种
//     颜色、单面 RGB 恒定 #E9C77E；顶面的规则格缝被读成"地砖/编织布"。判据（美术品控评审规程.md:213-214、
//     美术风格指南.md:199-204/§3.1-3.2）：
//       · P-9：同材质 200×200 窗灰度 std > 6（本 shader 的 std 现在全部来自逐块明暗与三种基色的
//         低频过渡，压不住高频能量）；
//       · P-10：纹理能量需显著高于纯色基线；
//       · §3.2 纪律 1：相邻面 smoothness 差 ≥ 0.15（沙/草/岩相邻面必须能一眼分出）。
//   【三族贴图】沙/草/岩各一套 albedo+法线（6 张 256²，算法唯一来源 = Assets/Editor/MaterialNoiseBuilder.cs，
//     存 Assets/Art/Textures/Materials/）。三族各按自己的 _XxxNoiseWorldScale 采样，再按 grass/rock 权重混合
//     **起伏量**（不是混合贴图）：混的是 (tex*2-1)，权重和恰为 1 → 平均色零漂移。
//     【r5 采样尺度：沙 0.35→0.05 / 草 0.25→0.04 / 岩 0.5→0.10（世界尺度放大 5-7×）】原尺度下贴图最细
//     八度只有 1-3cm 世界波长，近景被 mip 平均成平色、特写看不清砂粒；放大后最细八度落到 5-13cm，
//     进入近景可分辨 band。远看靠 mipmap + aniso 收敛，中低频八度兜底大色斑（不闪蚂蚁纹）。
//   【格缝去"地砖感"四条】1) 贴图 UV 走世界 XZ + 低频 FBM 扭曲（模型 UV 与 1 单位格子边界对齐，是地砖感来源）；
//     2) 逐块明暗的**格子坐标**按世界 XZ 的 FBM 平移（_BlockWarp）→ 规则方格的直边被打散；
//     3) 逐块明暗强度由 0.12 降到 0.06（写实方向不要"数字化块面"）；
//     4) 【r5 新增】格缝/块缘不再只是"压暗"（乘性压暗保留环境光色相，蓝灰天空光下会读成冷色勾缝），
//        改为按格缝掩码（距格边距离）lerp 到显式**暖灰** _SeamColor（#7C756A，HSV 饱和度 14.5% <15%，
//        亮度取 #6B5A48 与沙色的中间调）→ "地砖勾缝"读成"沙地裂纹"；块缘亮度差同步降 30%
//        （_BlockTintStrength 0.06→0.042）。
//   【默认关闭】_XxxDetailAlbedoStrength / _XxxBumpScale 的 shader 默认值都是 **0**：
//     没赋贴图的材质行为与改动前逐像素一致，且不依赖 2D 属性内置回退贴图的不可预期线性值。
//
// 【Pass 与 LightMode 唯一性】ForwardLit / ShadowCaster / DepthOnly，互不相同。
//   本 shader 必须带 DepthOnly：PirateWater 的岸边泡沫与浅深水过渡依赖 _CameraDepthTexture
//   （海床台阶用 PirateSurface，此处是水下地形块的深度来源）。
//
// 【_DebugMode 分档】各档"预期画面"写在片元对应分支处。
//
// 【[CHECK] 本地 URP 14.0.12 包内逐条核对（符号存在 ≠ include 自洽，见 §八-1）】
//   Core.hlsl / Lighting.hlsl / Shadows.hlsl -> 同 PirateSurface（BRDFData、SampleSH、GetMainLight 均由
//                                               Lighting.hlsl 的 include 链提供）
//   InitializeBRDFData          -> BRDF.hlsl:86
//   LightingPhysicallyBased      -> Lighting.hlsl:81
//   GlobalIllumination           -> GlobalIllumination.hlsl:431
//   TransformWorldToShadowCoord  -> Shadows.hlsl:319
//   MixFog / ComputeFogFactor    -> ShaderVariablesFunctions.hlsl:414 / :329
//   GetWorldSpaceNormalizeViewDir -> ShaderVariablesFunctions.hlsl:125
//   ShadowCasterPass.hlsl        -> Shaders/ShadowCasterPass.hlsl（官方阴影 Pass）
//   CommonMaterial.hlsl（core）  -> :352/359 定义 LerpWhiteTo；URP Shadows.hlsl:298 用它却**不**自己
//                                   include 它 —— ShadowCaster Pass 里必须先 include 它，
//                                   实测否则报 `undeclared identifier 'LerpWhiteTo'`（d3d11）。
//   TEXTURE2D / SAMPLER / SAMPLE_TEXTURE2D -> core Common.hlsl（经 URP Core.hlsl:16 引入）；
//   UnpackNormalScale           -> core Packing.hlsl:220（经 URP Core.hlsl:17 引入，无需额外 include；
//                                   :214 UnpackNormalmapRGorAG 读 w 通道 → 法线图 A=255）。
//   **本清单只能证明符号存在**；必须进图形界面编辑器 play 一次 + read_console 确认 0 shader error。
// ============================================================================

Shader "PirateCrew/PirateTerrain"
{
    Properties
    {
        // ---- 三色（GDD §10.4 中档；暗/亮档由噪声与逐块明暗自动产生）----
        _SandColor              ("沙地色 #C4A76A", Color) = (0.769, 0.655, 0.416, 1.0)
        _GrassColor             ("草地色 #4A8C4A", Color) = (0.290, 0.549, 0.290, 1.0)
        _RockColor              ("岩石色 #8C7B6A", Color) = (0.549, 0.482, 0.416, 1.0)

        // ---- 高度 / 坡度混合 ----
        _HeightSandGrass        ("沙→草高度阈值（世界 Y）", Float) = 0.6
        _HeightGrassRock        ("草→岩高度阈值（世界 Y）", Float) = 3.0
        _SlopeRockStart         ("岩石坡度起点(0=水平 1=竖直)", Range(0.0, 1.0)) = 0.45
        _SlopeRockEnd           ("岩石坡度终点", Range(0.0, 1.0)) = 0.72
        _BlendSoftness          ("过渡柔和度", Range(0.001, 4.0)) = 0.5

        // ---- 程序化噪声 / 块面 ----
        _NoiseScale             ("噪声尺度", Float) = 2.0
        _NoiseStrength          ("噪声强度（阈值抖动 + 明暗）", Range(0.0, 1.0)) = 0.35
        _BlockSize              ("地块尺寸（世界单位，1 = 1 瓦片）", Float) = 1.0
        _BlockTintStrength      ("逐块明暗差异", Range(0.0, 0.5)) = 0.12
        _FacetStrength          ("块面感（逐面法线强度）", Range(0.0, 1.0)) = 0.6
        // 逐块明暗的格子坐标按世界 XZ 的 FBM 平移的幅度（世界单位）。
        // 0 = 回到规则方格（旧行为，"地砖感"来源）；0.35-0.45 = 方格边界被打散成不规则块。
        _BlockWarp              ("逐块格子的世界扰动（打断地砖感）", Range(0.0, 1.0)) = 0.35

        // ---- 格缝 / 块缘着色（r5：去"地砖勾缝"的冷色，改暖灰"沙地裂纹"）----
        // 格缝 = 距格子边界的距离小于 _SeamWidth 的区域（**几何格缝**，非逐块明暗）。
        // 【为什么用显式颜色而不是继续乘性压暗】乘性压暗只改亮度、保留环境光色相：
        //   本场景环境光是蓝灰天空，压暗后的格缝会读成**饱和蓝灰勾缝**（实测 ~(63,90,120)），
        //   正是"地砖感"的来源。lerp 到显式暖灰把格缝的色相从"天空光×沙色"里拿出来，才是暖的。
        // 色值口径：亮度取 #6B5A48（暖棕）与沙色 #C4A76A 的中间调，再降到 HSV 饱和度 <15%。
        _SeamColor              ("格缝暖灰色（#7C756A，饱和度 14.5%）", Color) = (0.486, 0.459, 0.416, 1.0)
        _SeamStrength           ("格缝着色强度（0=关闭）", Range(0.0, 1.0)) = 0.18
        _SeamWidth              ("格缝宽度（占一格的比例，0.12=约 12cm @1m 格）", Range(0.005, 0.5)) = 0.12

        // ---- 细节噪声贴图（沙/草/岩三族；程序化资产，算法见 Assets/Editor/MaterialNoiseBuilder.cs）----
        // 【默认值 0 是刻意设计】没赋贴图的材质必须与"加贴图之前"逐像素一致（见文件头【默认关闭】）。
        // [NoScaleOffset]：UV 由世界 XZ×_XxxNoiseWorldScale 驱动，材质 Tiling/Offset 无意义。
        [NoScaleOffset] _SandNoiseMap  ("沙 albedo 细节图", 2D) = "gray" {}
        [NoScaleOffset] _GrassNoiseMap ("草 albedo 细节图", 2D) = "gray" {}
        [NoScaleOffset] _RockNoiseMap  ("岩 albedo 细节图", 2D) = "gray" {}
        [NoScaleOffset] _SandBumpMap   ("沙 法线细节图", 2D) = "bump" {}
        [NoScaleOffset] _GrassBumpMap  ("草 法线细节图", 2D) = "bump" {}
        [NoScaleOffset] _RockBumpMap   ("岩 法线细节图", 2D) = "bump" {}
        // r5 采样尺度：沙 0.35→0.05、草 0.25→0.04、岩 0.5→0.10（世界尺度放大 5-7×，见文件头）。
        _SandNoiseWorldScale   ("沙 世界尺度（1/该值=平铺米数）", Float) = 0.05
        _GrassNoiseWorldScale  ("草 世界尺度", Float) = 0.04
        _RockNoiseWorldScale   ("岩 世界尺度", Float) = 0.10
        _NoiseWarpStrength     ("细节 UV 扭曲（打断与格子的轴向对齐）", Range(0.0, 0.5)) = 0.18
        _SandDetailAlbedoStrength  ("沙 细节 albedo 强度（0=关闭）", Range(0.0, 1.0)) = 0.0
        _GrassDetailAlbedoStrength ("草 细节 albedo 强度（0=关闭）", Range(0.0, 1.0)) = 0.0
        _RockDetailAlbedoStrength  ("岩 细节 albedo 强度（0=关闭）", Range(0.0, 1.0)) = 0.0
        _SandBumpScale         ("沙 法线强度（0=关闭）", Range(0.0, 2.0)) = 0.0
        _GrassBumpScale        ("草 法线强度（0=关闭）", Range(0.0, 2.0)) = 0.0
        _RockBumpScale         ("岩 法线强度（0=关闭）", Range(0.0, 2.0)) = 0.0

        // ---- 粗糙度分区（§3.2 纪律 1：相邻面 smoothness 差 ≥ 0.15）----
        // _Smoothness 即**沙族**基准（保留旧属性名，材质生成脚本继续写它）；
        // 草/岩各占一个槽，取值让 沙↔草↔岩 两两差 ≥ 0.15。
        _GrassSmoothness        ("草地光滑度（与沙差 ≥0.15）", Range(0.0, 1.0)) = 0.40
        _RockSmoothness         ("岩石光滑度（与草差 ≥0.15）", Range(0.0, 1.0)) = 0.55

        // ---- PBR ----
        _Metallic               ("金属度", Range(0.0, 1.0)) = 0.0
        _Smoothness             ("光滑度", Range(0.0, 1.0)) = 0.15
        _AmbientStrength        ("环境光（SH）强度", Range(0.0, 2.0)) = 1.0

        // ---- 沙滩湿区（与 PirateSurface 同口径；仅作用在沙分支）【AI 提案】----
        _WetSandColor           ("湿沙色（湿态 albedo 目标 #A88D5C）", Color) = (0.659, 0.553, 0.361, 1.0)
        _WaterLevelY            ("水位世界 Y（与 PirateWater/LevelGeometry 一致）", Float) = -0.2
        _WetBandWidth           ("湿沙带宽度（世界单位，水线以上过渡到全干）", Float) = 0.45
        _WetDarken              ("湿区变暗强度（湿区额外压暗 0-1）", Range(0.0, 1.0)) = 0.3
        _WetSmoothnessBoost     ("湿区光滑度增益（湿沙更滑）", Range(0.0, 1.0)) = 0.25
        _WetLineMin             ("残留线相对水位下限", Float) = 0.10
        _WetLineMax             ("残留线相对水位上限", Float) = 0.22
        _WetLineDarkening       ("残留线压暗强度", Range(0.0, 1.0)) = 0.25

        // ---- 沙纹（仅湿掩码内；纯视觉，不参与玩法判定）【AI 提案】----
        _RippleScale            ("沙纹频率（弧度/世界单位，波长=2π/该值）", Float) = 16.0
        _RippleStrength         ("沙纹强度（硬钳 ≤0.15，保低模辨识度）", Range(0.0, 0.15)) = 0.10
        _RippleDistort          ("沙纹相位扭曲（低频噪声，0=绝对平行）", Range(0.0, 1.0)) = 0.35

        // ---- 描边兼容（GDD：场景物描边 #2A2A2A）—— 写实化后默认关闭 ----
        _EdgeColor              ("边缘压暗色 #2A2A2A", Color) = (0.165, 0.165, 0.165, 1.0)
        // 旧默认 0.35；写实方向无描边 → 默认 0（见文件头第 3 条）。
        _EdgeStrength           ("边缘压暗强度（0=关闭描边兼容）", Range(0.0, 1.0)) = 0.0
        _EdgePower              ("边缘压暗指数", Range(0.5, 8.0)) = 3.0

        // ---- 调试 ----
        // 0 正常 / 1 只 albedo / 2 块面法线 / 3 草岩混合系数 / 4 坡度与高度 / 5 湿掩码 / 6 沙纹 / 7 细节贴图
        // 既有 0-4 档语义不变；5、6 为沙滩湿区那轮新增；7 为细节贴图那轮新增。
        _DebugMode              ("调试模式 0=正常 1=albedo 2=法线 3=混合系数 4=坡度高度 5=湿掩码 6=沙纹 7=细节贴图", Range(0.0, 7.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 200

        // ====================================================================
        // Pass 1 / ForwardLit
        // ====================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   TerrainVertex
            #pragma fragment TerrainFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 必须在 Lighting.hlsl 之后：Shadows.hlsl:298 用 LerpWhiteTo，而它由 Lighting.hlsl 第 4 行的
            // BRDF.hlsl → core CommonMaterial.hlsl 提供（Shadows.hlsl 自身不 include）。顺序反过来会报
            // 「undeclared identifier 'LerpWhiteTo'」（2026-09-13 实测 d3d11）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SandColor;
                float4 _GrassColor;
                float4 _RockColor;
                float  _HeightSandGrass;
                float  _HeightGrassRock;
                float  _SlopeRockStart;
                float  _SlopeRockEnd;
                float  _BlendSoftness;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _BlockSize;
                float  _BlockTintStrength;
                float  _FacetStrength;
                float  _BlockWarp;
                float4 _SeamColor;
                float  _SeamStrength;
                float  _SeamWidth;
                float  _SandNoiseWorldScale;
                float  _GrassNoiseWorldScale;
                float  _RockNoiseWorldScale;
                float  _NoiseWarpStrength;
                float  _SandDetailAlbedoStrength;
                float  _GrassDetailAlbedoStrength;
                float  _RockDetailAlbedoStrength;
                float  _SandBumpScale;
                float  _GrassBumpScale;
                float  _RockBumpScale;
                float  _GrassSmoothness;
                float  _RockSmoothness;
                float  _Metallic;
                float  _Smoothness;
                float  _AmbientStrength;
                float4 _WetSandColor;
                float  _WaterLevelY;
                float  _WetBandWidth;
                float  _WetDarken;
                float  _WetSmoothnessBoost;
                float  _WetLineMin;
                float  _WetLineMax;
                float  _WetLineDarkening;
                float  _RippleScale;
                float  _RippleStrength;
                float  _RippleDistort;
                float4 _EdgeColor;
                float  _EdgeStrength;
                float  _EdgePower;
                float  _DebugMode;
            CBUFFER_END

            // 细节贴图：**纹理与采样器必须在 UnityPerMaterial CBUFFER 之外**（URP 硬要求，
            //   见 LitInput.hlsl 的 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); 分离声明）。
            TEXTURE2D(_SandNoiseMap);  SAMPLER(sampler_SandNoiseMap);
            TEXTURE2D(_GrassNoiseMap); SAMPLER(sampler_GrassNoiseMap);
            TEXTURE2D(_RockNoiseMap);  SAMPLER(sampler_RockNoiseMap);
            TEXTURE2D(_SandBumpMap);   SAMPLER(sampler_SandBumpMap);
            TEXTURE2D(_GrassBumpMap);  SAMPLER(sampler_GrassBumpMap);
            TEXTURE2D(_RockBumpMap);   SAMPLER(sampler_RockBumpMap);

            // ------------------------------------------------------------------
            // 程序化噪声（与 PirateSurface / PirateWater 内的副本一致，刻意重复以免多一条 include 链）
            // ------------------------------------------------------------------

            float PirateHash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float PirateValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = PirateHash21(i);
                float b = PirateHash21(i + float2(1.0, 0.0));
                float c = PirateHash21(i + float2(0.0, 1.0));
                float d = PirateHash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float PirateFbm(float2 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                float norm = 0.0;

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    sum  += PirateValueNoise(p) * amp;
                    norm += amp;
                    p     = p * 2.03 + float2(17.1, 9.7);
                    amp  *= 0.5;
                }
                return sum / max(norm, 1e-5);
            }

            struct AttributesTerrain
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsTerrain
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                real   fogFactor  : TEXCOORD2;
            };

            VaryingsTerrain TerrainVertex(AttributesTerrain IN)
            {
                VaryingsTerrain OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 TerrainFragment(VaryingsTerrain IN) : SV_Target
            {
                float3 positionWS = IN.positionWS;
                float3 geometricNormalWS = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                // ---- 块面感：用屏幕空间导数还原逐面法线 ----
                // 对硬边块体（Cube）结果 = 几何法线；对平滑网格会给出三角形面法线 → 低多边形棱面。
                // fnLen 保护：极薄/退化的片元上 cross 可能接近 0 → 归一化出 NaN（表现为闪点），
                // 这时回退到几何法线。
                float3 dpx = ddx(positionWS);
                float3 dpy = ddy(positionWS);
                float3 fn  = cross(dpx, dpy);
                float  fnLen = length(fn);
                float3 faceNormalWS = fnLen > 1e-8 ? fn / fnLen : geometricNormalWS;
                // 保证与几何法线同向（三角形绕序可能让 cross 反向）。
                faceNormalWS *= sign(dot(faceNormalWS, geometricNormalWS) + 1e-6);

                float3 normalWS = normalize(lerp(geometricNormalWS, faceNormalWS, saturate((half)_FacetStrength)));

                // ---- 噪声：阈值抖动（让沙/草/岩边界不规则）+ 明暗 ----
                float  nBig = PirateFbm(positionWS.xz * _NoiseScale + positionWS.y * 0.53);
                float  jitter = (nBig - 0.5) * _NoiseStrength * 2.0;

                // ---- 高度 + 坡度 → 沙/草/岩 ----
                float slope = saturate(1.0 - normalWS.y);
                float soft  = max(_BlendSoftness, 0.001);

                float rockH = smoothstep(_HeightGrassRock - soft, _HeightGrassRock + soft, positionWS.y + jitter);
                float rockS = smoothstep(_SlopeRockStart, max(_SlopeRockEnd, _SlopeRockStart + 1e-3), slope);
                float rock  = saturate(max(rockH, rockS));
                float grass = saturate(smoothstep(_HeightSandGrass - soft, _HeightSandGrass + soft, positionWS.y + jitter)
                                       * (1.0 - rock));

                half3 albedo = _SandColor.rgb;
                albedo = lerp(albedo, _GrassColor.rgb, (half)grass);
                albedo = lerp(albedo, _RockColor.rgb, (half)rock);

                // ---- 细节贴图（沙/草/岩三族；世界 XZ UV + 低频 FBM 扭曲）----
                // 【三族权重】sandWeight = 三色混合后"还剩多少沙"（权重和恰为 1）；草/岩同样由上方
                //   smoothstep 给出。此处提前声明，供细节贴图混合与下方湿掩码/粗糙度分区共用。
                half sandWeight = saturate(1.0h - (half)grass - (half)rock);
                // 【判据】P-9 同材质 200×200 窗 std > 6、P-10 高频能量显著高于纯色基线。
                // 【为什么不用模型 UV】地形是 Cube 图元，模型 UV 与 1 单位格子边界对齐 → 正是顶面
                //   "规则格缝读成地砖/编织布"的来源之一。世界空间 UV 让贴图跨块连续。
                // 【三族的平铺米数（r5 放大后）】1/_SandNoiseWorldScale=20m、1/_Grass…=25m、1/_Rock…=10m。
                //   放大的是"贴图世界尺度"（不是贴图内容）：让最细八度落进近景可分辨 band，见文件头。
                // 【扭曲尺度 0.37】低频，只挪 UV 不影响贴图 mip 选择。
                float2 detailUV = positionWS.xz;
                float2 detailWarp = float2(PirateFbm(detailUV * 0.37 + 3.1),
                                           PirateFbm(detailUV * 0.37 + 19.7)) - 0.5;
                detailWarp *= (_NoiseWarpStrength * 2.0);

                half3 sandAlb  = SAMPLE_TEXTURE2D(_SandNoiseMap,  sampler_SandNoiseMap,  detailUV * _SandNoiseWorldScale  + detailWarp).rgb;
                half3 grassAlb = SAMPLE_TEXTURE2D(_GrassNoiseMap, sampler_GrassNoiseMap, detailUV * _GrassNoiseWorldScale + detailWarp).rgb;
                half3 rockAlb  = SAMPLE_TEXTURE2D(_RockNoiseMap,  sampler_RockNoiseMap,  detailUV * _RockNoiseWorldScale  + detailWarp).rgb;

                half4 sandNrm  = SAMPLE_TEXTURE2D(_SandBumpMap,  sampler_SandBumpMap,  detailUV * _SandNoiseWorldScale  + detailWarp);
                half4 grassNrm = SAMPLE_TEXTURE2D(_GrassBumpMap, sampler_GrassBumpMap, detailUV * _GrassNoiseWorldScale + detailWarp);
                half4 rockNrm  = SAMPLE_TEXTURE2D(_RockBumpMap,  sampler_RockBumpMap,  detailUV * _RockNoiseWorldScale  + detailWarp);

                // 乘性微色斑：三张图线性均值恰 0.5 → *2 后均值恰 1.0；混的是**起伏量 (tex*2-1)**，
                // 权重和恰为 1 → 平均色零漂移（不扰动 GDD 三档色调色板）。
                // 作用点：三色混合之后、湿合成之前（与 PirateSurface 的"湿之前"纪律一致）。
                half3 detailMod = half3(1.0h, 1.0h, 1.0h)
                    + (half)grass * (grassAlb * 2.0h - 1.0h) * (half)_GrassDetailAlbedoStrength
                    + (half)rock  * (rockAlb  * 2.0h - 1.0h) * (half)_RockDetailAlbedoStrength
                    + sandWeight  * (sandAlb  * 2.0h - 1.0h) * (half)_SandDetailAlbedoStrength;
                albedo *= detailMod;
                // 调试档 7 用：细节图"乘性系数"的线性明度（均值≈1.0）。
                half detailLuma = dot(detailMod, half3(0.2126h, 0.7152h, 0.0722h));

                // 细节法线（三族按权重混合**切空间 xy**，再一次性重定向到世界 XZ 基）：
                //   混的是法线而不是最终世界扰动向量 —— 后者在权重过渡处会互相抵消出错误方向。
                //   只取 xy、丢掉 z（"平法线"的 z 贡献本来就是 0），并靠 _XxxBumpScale 的门默认 0
                //   保证"未赋贴图 = 无效果"。
                float2 nTS = UnpackNormalScale(sandNrm,  1.0h).xy * ((half)_SandBumpScale  * sandWeight)
                           + UnpackNormalScale(grassNrm, 1.0h).xy * ((half)_GrassBumpScale * (half)grass)
                           + UnpackNormalScale(rockNrm,  1.0h).xy * ((half)_RockBumpScale  * (half)rock);
                float3 bumpTex = float3(nTS.x, 0.0, nTS.y);
                bumpTex -= normalWS * dot(bumpTex, normalWS);   // 投影到切平面，避免竖直面被拉歪
                normalWS = normalize(normalWS + bumpTex);

                // ---- 湿掩码（与 PirateSurface 同公式；但仅作用在**沙分支**）----
                // sandWeight（= 三色混合后"还剩多少沙"）已在细节贴图段声明并复用。
                // 草坡/岩石不染湿沙色 —— 本 shader 的三色里只有沙需要"打湿"，湿沙色是沙专属目标色。
                float dy = positionWS.y - _WaterLevelY;
                half wetMask = saturate(1.0h - (half)(dy / max(_WetBandWidth, 1e-4)));
                half wet = wetMask * sandWeight;

                // 高水位残留线（潮痕）：与 PirateSurface 同公式（smoothstep 两端，软边=带宽 1/4），
                // 同样只在沙分支压暗。
                float lineSoft = max((_WetLineMax - _WetLineMin) * 0.25, 0.005);
                half wetLine = (half)(smoothstep(_WetLineMin - lineSoft, _WetLineMin + lineSoft, dy)
                                   * (1.0 - smoothstep(_WetLineMax - lineSoft, _WetLineMax + lineSoft, dy)))
                             * sandWeight;

                // 湿沙合成：向湿沙色靠拢 → 按 _WetDarken 额外压暗 → 潮痕线再压一档。
                albedo = lerp(albedo, _WetSandColor.rgb, wet);
                albedo *= lerp(1.0h, 1.0h - (half)_WetDarken, wet);
                albedo *= lerp(1.0h, 1.0h - (half)_WetLineDarkening, wetLine);

                // ---- 沙纹（仅湿掩码内；纯视觉，不参与玩法判定）----
                // 与 PirateSurface 同公式：λ = 2π/_RippleScale（默认 16 → λ≈0.39 世界单位）。
                float rstr  = min(_RippleStrength, 0.15);
                float2 rdir = float2(0.8575, 0.5145);   // normalize(1, 0.6)：斜向平行纹
                float ripPhase = dot(positionWS.xz * _RippleScale, rdir)
                               + PirateFbm(positionWS.xz * (_RippleScale * 0.25)) * _RippleDistort * 6.28318;
                float ripSin = sin(ripPhase);
                float ripCos = cos(ripPhase);
                float ripAmp = rstr * (float)wet;       // 干区/草岩区振幅 = 0
                // 法线扰动（|bump| ≤ ripAmp ≤ 0.15），投影到切平面避免竖直面被拉歪。
                float3 ripBump = float3(-rdir.x, 0.0, -rdir.y) * (ripCos * ripAmp);
                ripBump -= normalWS * dot(ripBump, normalWS);
                normalWS = normalize(normalWS + ripBump);
                // 明度调制（±ripAmp ≤ 0.15）。
                albedo *= 1.0h + (half)(ripSin * ripAmp);

                // ---- 逐块明暗（低多边形风格的辨识特征）----
                // 【刻意放在湿合成之后】若先乘 blockTint 再 lerp 到 _WetSandColor，块面亮度差会被
                // (1-wet) 洗平 → "湿痕把低模块面感洗掉"。后乘则湿区仍保留"相邻块亮度不同"的辨识特征。
                // 【去"地砖感"（r3 复验 N4）】把量化格坐标按世界 XZ 的低频 FBM **平移**（_BlockWarp，
                //   单位=世界单位）：1 单位方格的直边被推成不规则块 → 顶面不再读成"瓷砖+勾缝"。
                //   平移是低频（0.41 ≈ 2.4 世界单位一个起伏）→ 一个"块"整体被挪走，不会撕碎块面。
                //   低多边形辨识度靠"相邻块仍有亮度差"保留，故 _BlockTintStrength 由 0.12 降到 0.06、
                //   r5 再降 30% 到 0.042（写实方向不要"数字化块面"；取值见 BattleSceneLighting.BuildTerrainMaterial）。
                float2 blockWarp = float2(PirateFbm(positionWS.xz * 0.41 + 11.3),
                                          PirateFbm(positionWS.xz * 0.41 + 27.9)) - 0.5;
                // cellPos = 打散后的格坐标：floor 给逐块哈希、frac 给格缝掩码 —— 同一份坐标，缝与块不错位。
                float2 cellPos = (positionWS.xz + blockWarp * (_BlockWarp * 2.0)) / max(_BlockSize, 0.01);
                float2 blockCoord = floor(cellPos)
                                  + floor(positionWS.y * 3.0) * 7.0;
                half blockHash = (half)PirateHash21(blockCoord);
                half blockTint = lerp(1.0h - (half)_BlockTintStrength, 1.0h + (half)_BlockTintStrength, blockHash);
                albedo *= blockTint;

                // ---- 格缝 / 块缘暖灰（r5：把"地砖勾缝"读法改成"沙地裂纹"）----
                // 【为什么用显式颜色】旧实现只有乘性 blockTint：只压亮度、保留环境光色相，蓝灰天空光下
                //   格缝读成饱和蓝灰（实测 ~(63,90,120)）——"地砖感"的来源。lerp 到显式暖灰把色相
                //   从"天空光×沙色"里拿出来，格缝才是暖的，且不对抗低多边形块面辨识度。
                // 【掩码】frac(cellPos) 到最近格边的距离（x/z 取小者）→ 1-smoothstep 软边；
                //   cellPos 由世界 XZ 连续映射，平铺无缝。放在逐块明暗之后 = 裂纹叠在块面上。
                float2 cellFrac = frac(cellPos);
                float2 edgeDist = min(cellFrac, 1.0 - cellFrac);
                float  seam = 1.0 - smoothstep(0.0, max(_SeamWidth, 0.005), min(edgeDist.x, edgeDist.y));
                albedo = lerp(albedo, _SeamColor.rgb, (half)(seam * (float)_SeamStrength));

                // 噪声带来的轻微明暗（避免色块过于平）。
                albedo *= 1.0h - (half)(_NoiseStrength * 0.3) * (1.0h - (half)nBig);

                // ---- 描边兼容：#2A2A2A 边缘压暗（替代 inverted-hull）----
                half rim  = pow(1.0h - saturate((half)dot(normalWS, viewDirWS)), (half)_EdgePower);
                half edge = saturate(rim * (half)_EdgeStrength);
                albedo = lerp(albedo, _EdgeColor.rgb, edge);

                half metallic   = saturate((half)_Metallic);
                // ---- 粗糙度分区（§3.2 纪律 1：相邻面 smoothness 差 ≥ 0.15）----
                // 沙 = _Smoothness（0.25）/ 草 = _GrassSmoothness（0.40）/ 岩 = _RockSmoothness（0.55）
                // → 两两差 0.15，沙/草/岩相邻面一眼分得出（P-14 高光占比也随之分区）。
                // 权重与 albedo 混合同源（sandWeight 见上），故过渡带的光滑度是连续插值，不会出硬边。
                // 湿沙更滑（高光反射是"湿"的关键信号）——与 PirateSurface 同口径，只作用在沙分支。
                half smoothBase = (half)_Smoothness * sandWeight
                                + (half)_GrassSmoothness * (half)grass
                                + (half)_RockSmoothness * (half)rock;
                half smoothness = saturate(smoothBase + wet * (half)_WetSmoothnessBoost);

                // ---- 调试档 ----
                // 档 1：只 albedo。预期：沙/草/岩三色分区 + 明显的逐块亮度差（低多边形块面感）；
                //       水线附近出现暗色湿痕窄带。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(albedo, 1.0h);
                // 档 2：块面法线。预期：每个面一个纯色（因为面内法线恒定），相邻面颜色不同 → 棱面清晰。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(half3(normalWS * 0.5 + 0.5), 1.0h);
                // 档 3：R = 草地系数, G = 岩石系数, B = 沙地系数(=1-grass-rock)。预期：三通道互补。
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return half4((half)grass, (half)rock, saturate(1.0h - (half)grass - (half)rock), 1.0h);
                // 档 4：R = 坡度, G = 高度(0~8 归一), B = 0。预期：竖直面红、高处绿。
                //       条件收窄到 <4.5，否则会吞掉新增 5、6 档（既有档位语义不变）。
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                    return half4((half)slope, saturate((half)(positionWS.y / 8.0)), 0.0h, 1.0h);
                // 档 5：湿掩码灰度（**位置掩码、未乘沙权重**，全白=完全湿）。预期：y≤-0.2 纯白，
                //       随高度线性变暗，y≥+0.25 纯黑。程序化判据：水线附近灰度 >0.7、台地顶面 <0.05。
                if (_DebugMode > 4.5 && _DebugMode < 5.5)
                    return half4(half3(wetMask, wetMask, wetMask), 1.0h);
                // 档 6：沙纹灰度。预期：湿沙区为明暗相间斜纹（0.5±0.5），干区/草岩区为中性灰 0.5。
                //       判据：湿沙区灰度标准差 >0.05；草岩区标准差 ≈0（因 wet 已乘 sandWeight）。
                if (_DebugMode > 5.5 && _DebugMode < 6.5)
                {
                    // 单参 splat（half3(x)）在部分真机编译器上报"构造器参数个数错误"，显式三分量。
                    half ripGray = saturate(0.5h + 0.5h * (half)(ripSin * (float)wet));
                    return half4(ripGray, ripGray, ripGray, 1.0h);
                }
                // 档 7【本次新增】：细节贴图的乘性系数（0.5 = 系数 1.0 = 无起伏）。
                //       预期：整片有细碎噪点、不是纯 0.5 死平；沙/草/岩三族的噪点尺度与强度不同。
                //       程序化判据：200×200 窗灰度 std > 0.02（8bit 约 5）。
                //       若死平 → 三族的 _XxxDetailAlbedoStrength 都是 0，或贴图未生成
                //       （先跑 PirateCrew/渲染/生成程序化材质噪声贴图）。
                if (_DebugMode > 6.5)
                {
                    half detGray = saturate(detailLuma * 0.5h);
                    return half4(detGray, detGray, detGray, 1.0h);
                }

                // ---- PBR 光照（与 PirateSurface 同口径）----
                half alpha = 1.0h;
                BRDFData brdfData;
                InitializeBRDFData(albedo, metallic, half3(0.0h, 0.0h, 0.0h), smoothness, alpha, brdfData);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                half3 color = LightingPhysicallyBased(brdfData, mainLight, normalWS, viewDirWS);

                half3 bakedGI = SampleSH(normalWS) * (half)_AmbientStrength;
                color += GlobalIllumination(brdfData, bakedGI, 1.0h, normalWS, viewDirWS);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / ShadowCaster：地形块投到主光阴影图
        // ====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // 【include 顺序硬要求】Shadows.hlsl:298 用 LerpWhiteTo（定义在 core 的 CommonMaterial.hlsl，
            //   但 Shadows.hlsl 自己不 include 它）；ShadowCasterPass.hlsl 只带 Core.hlsl + Shadows.hlsl。
            //   故必须先 Core.hlsl → CommonMaterial.hlsl（URP 官方 Lit.shader 走 LitInput.hlsl 也是这个顺序）。
            //   实测报错：undeclared identifier 'LerpWhiteTo' at Shadows.hlsl(298) (on d3d11)。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ====================================================================
        // Pass 3 / DepthOnly：写 _CameraDepthTexture（PirateWater 的浅深水/泡沫依赖）
        // 手写极简实现，与 PirateOutline / PirateSurface 同款。
        // ====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
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
