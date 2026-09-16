// ============================================================================
// PirateOcean.shader —— 海盗军团夺宝 3D / M4 大海域海面（茫茫大海的仿真化）
//
// 【定位】M4 §5（docs/M4-大海域世界化.md）：替换"外扩 400u Cube + 4 波 Gerstner"的旧水面
//   （Assets/Art/Shaders/PirateWater.shader，保留作档案）。本 shader 沿用旧件的**已验证结构**
//   （同一套 URP include 链 / CBUFFER 纪律 / 调试分档），在其上新增四层：
//
//   S) 长涌 swell ×2（波长 120/72u、振幅 1.10/0.65，外海"茫茫大海"的主载体）与现有 4 波 chop
//      叠加为 6 波 Gerstner；位移与法线仍同一组解析导数（与顶点位移同源，沿用旧件结论）。
//   E) 长涌近岸包络：距竞技场中心 ≤ 保护半径时长涌振幅压到 0（波峰硬约束：波峰最高点 < -0.10，
//      不穿岛基湿沙带——水面 -0.4、chop 振幅和 0.278 → 近岸波峰 -0.122 ✓），保护圈外 260u 内
//      smoothstep 爬到 1（外海全涌）。包络参数由 OceanRig 以全局 _OceanArenaCenter 发布，
//      C# 契约在 OceanRules.SwellEnvelope（两边同式）。
//   W) 波峰白帽：雅可比（陡度）阈值触发 + 波高门槛 + 世界空间双层噪声碎化边缘 +
//      近岸降权（OceanRules.WhitecapMask 同式）。
//   H) 地平线融合：位移在 2000→3000u 淡出到 0（远场只留法线扰动，海天线不闪）、
//      2600→3900u 颜色融进雾色 #B0D4F1 且 alpha→1（海天线干净）；网格裙边由 OceanRig
//      铺到 4200u ≥ 4000u 契约。另有"网格密度 LOD"：波长不足以被当前环宽解析（λ < 3×环宽）的
//      短波几何位移淡出（OceanRules.WaveGeometricFade 同式），法线不受影响 → 远处无几何 boiling。
//   另加：波背背光透射 + 薄层散射——技法吸收自 HPWater BSDF 的 diffT 项
//   （G_backlit × Beer-Lambert 透射 × Henyey 相位；厚度用绝对波高近似，近岸自动弱、外海强；
//   详见 docs/M4-海面选型与实现.md 的 HPWater 吸收表）。
//
// 【调色板】美术风格指南 §2.1 海水三档：浅 #4DA6D9 / 中 #2B7AB8 / 深 #1A4F7A；雾色 #B0D4F1。
//   （旧 PirateWater 的 r6 回蓝亮档 #4FA8CC/#2E86B5/#1E5E88 保留在旧材质上，可 A/B 对比。）
//
// 【兼容】_WaterSimEnabled/_WaterHeightField/_WaterObstacleMap/_WaterSimOrigin/_WaterSunDir
//   与旧件同名同义：WaterSimulationDriver 不需要任何改动即可把涟漪/泡沫/太阳方向注入本 shader
//   （驱动发全局，本 shader 按世界 XZ 采样，与水面网格域无关）。驱动缺席时自动跳过（兜底 0）。
//
// 【Pass 与 LightMode】只有 ForwardLit（"UniversalForward"）一个 Pass；透明水面不投影、
//   不写深度（沿旧件结论：写深度会自己挡自己）。无额外 RenderFeature 依赖。
//
// 【_DebugMode 分档】0 正常 / 1 水色 / 2 波法线 / 3 菲涅尔 / 4 泡沫 / 5 波高 / 6 波陡度
//   / 7 高度场 / 8 曲率 / 9 泡沫累积 / 10 障碍图（沿旧件语义）
//   / 11 白帽掩码 / 12 包络(R=长涌包络 G=位移远淡 B=地平线淡) / 13 局部环宽灰度。
//
// 【验证门禁（AGENTS 图形学铁律）】本 shader 未经 Unity 编译验证（水体 subagent 不启动 Unity）。
//   协调者必须：batchmode 打开工程 → log 里 grep "shader error" 0 条 →
//   编辑器里有渲染路径时按 export/<功能名>-debug 惯例逐档截图核对。
// ============================================================================

Shader "PirateCrew/Ocean"
{
    Properties
    {
        // ---- 三档海水色（美术风格指南 §2.1：#4DA6D9 / #2B7AB8 / #1A4F7A）----
        _ShallowColor           ("浅水色 #4DA6D9", Color) = (0.3020, 0.6510, 0.8510, 1.0)
        _MidColor               ("中水色 #2B7AB8", Color) = (0.1690, 0.4780, 0.7220, 1.0)
        _DeepColor              ("深水色 #1A4F7A", Color) = (0.1020, 0.3100, 0.4780, 1.0)
        _ShoreFadeDistance      ("浅→深过渡深度（世界单位）", Range(0.1, 20.0)) = 5.0

        // ---- 长涌 swell ×2（外海全涌；近岸被包络压 0，见文件头 E）----
        // 默认值 = OceanRules.DefaultSwellWaves（两边必须一致）。
        _S1Dir                  ("S1 方向(xz)", Vector) = (1.0, 0.0, 0.15, 0.0)
        _S1Length               ("S1 波长（世界单位）", Float) = 120.0
        _S1Amp                  ("S1 振幅（世界单位）", Float) = 1.10
        _S1Steep                ("S1 陡度 0-1", Range(0.0, 1.0)) = 0.75
        _S1Speed                ("S1 相速倍率", Float) = 1.0

        _S2Dir                  ("S2 方向(xz)", Vector) = (0.55, 0.0, 1.0, 0.0)
        _S2Length               ("S2 波长（世界单位）", Float) = 72.0
        _S2Amp                  ("S2 振幅（世界单位）", Float) = 0.65
        _S2Steep                ("S2 陡度 0-1", Range(0.0, 1.0)) = 0.70
        _S2Speed                ("S2 相速倍率", Float) = 1.05

        // ---- 现有 4 波 chop（默认值 = WaterRules.DefaultWaves，格 1→2 单位 ×2 口径）----
        _W1Dir                  ("W1 方向(xz)", Vector) = (1.0, 0.0, 0.25, 0.0)
        _W1Length               ("W1 波长（世界单位）", Float) = 26.0
        _W1Amp                  ("W1 振幅（世界单位）", Float) = 0.110
        _W1Steep                ("W1 陡度 0-1", Range(0.0, 1.0)) = 0.65
        _W1Speed                ("W1 相速倍率", Float) = 1.0

        _W2Dir                  ("W2 方向(xz)", Vector) = (0.6, 0.0, 1.0, 0.0)
        _W2Length               ("W2 波长（世界单位）", Float) = 15.0
        _W2Amp                  ("W2 振幅（世界单位）", Float) = 0.080
        _W2Steep                ("W2 陡度 0-1", Range(0.0, 1.0)) = 0.60
        _W2Speed                ("W2 相速倍率", Float) = 1.15

        _W3Dir                  ("W3 方向(xz)", Vector) = (-0.3, 0.0, 1.0, 0.0)
        _W3Length               ("W3 波长（世界单位）", Float) = 8.4
        _W3Amp                  ("W3 振幅（世界单位）", Float) = 0.056
        _W3Steep                ("W3 陡度 0-1", Range(0.0, 1.0)) = 0.55
        _W3Speed                ("W3 相速倍率", Float) = 1.30

        _W4Dir                  ("W4 方向(xz)", Vector) = (1.0, 0.0, -0.5, 0.0)
        _W4Length               ("W4 波长（世界单位）", Float) = 4.8
        _W4Amp                  ("W4 振幅（世界单位）", Float) = 0.032
        _W4Steep                ("W4 陡度 0-1", Range(0.0, 1.0)) = 0.50
        _W4Speed                ("W4 相速倍率", Float) = 1.50

        // ---- 网格密度（OceanRig/OceanGridRules 落盘口径；shader 只用来算 LOD 淡出）----
        _GridCellSize           ("近场格距（世界单位）", Float) = 1.6
        _GridUniformRadius      ("均匀区半径（世界单位）", Float) = 128.0
        _GridRingGrowth         ("环宽增长率", Float) = 1.25

        // ---- 顶点位移远场淡出（海天线不闪）----
        _DisplaceFadeStart      ("位移淡出起点（距相机，世界单位）", Range(200.0, 4000.0)) = 2000.0
        _DisplaceFadeEnd        ("位移淡出终点（距相机，世界单位）", Range(400.0, 5000.0)) = 3000.0

        // ---- 地平线融合（雾色 #B0D4F1，美术风格指南 §2.1/品控 Q-12）----
        _HorizonColor           ("地平线/雾色 #B0D4F1", Color) = (0.6902, 0.8314, 0.9451, 1.0)
        _HorizonFadeStart       ("地平线融合起点（距相机，世界单位）", Range(400.0, 6000.0)) = 2600.0
        _HorizonFadeEnd         ("地平线融合终点（距相机，世界单位）", Range(800.0, 8000.0)) = 3900.0

        // ---- 高频细节法线（旧的双层 FBM，叠加在 Gerstner 解析法线之上；远处淡出防闪点）----
        _WaveScaleA             ("A 层细节波尺度", Float) = 0.32
        _WaveSpeedA             ("A 层细节波速", Float) = 0.45
        _WaveStrengthA          ("A 层细节法线强度", Range(0.0, 2.0)) = 0.55
        _WaveDirectionA         ("A 层细节波方向(xz)", Vector) = (1.0, 0.0, 0.35, 0.0)
        _WaveScaleB             ("B 层细节波尺度", Float) = 0.95
        _WaveSpeedB             ("B 层细节波速", Float) = 0.85
        _WaveStrengthB          ("B 层细节法线强度", Range(0.0, 2.0)) = 0.28
        _WaveDirectionB         ("B 层细节波方向(xz)", Vector) = (-0.4, 0.0, 1.0, 0.0)
        _DetailFarFadeStart     ("细节法线淡出起点（距相机）", Range(100.0, 3000.0)) = 600.0
        _DetailFarFadeEnd       ("细节法线淡出终点（距相机）", Range(200.0, 4000.0)) = 1600.0

        // ---- 波峰白帽（陡度阈值触发 + 噪声碎化；OceanRules.WhitecapMask 同式）----
        _WhitecapStrength       ("白帽整体强度", Range(0.0, 2.0)) = 1.0
        _WhitecapJacobianThreshold ("白帽雅可比阈值（低于=陡）", Range(0.2, 1.0)) = 0.85
        _WhitecapSoftness       ("白帽软区宽度", Range(0.02, 0.5)) = 0.15
        _WhitecapShoreScale     ("近岸白帽保留比例", Range(0.0, 1.0)) = 0.35
        _WhitecapNoiseScale     ("白帽噪声尺度（1/世界单位）", Float) = 0.08

        // ---- 波背背光透射/薄层散射（技法吸收自 HPWater BSDF diffT：G×Beer-Lambert×Henyey 相位）----
        _SssColor               ("散射色（提案/待定 #8FD4EC）", Color) = (0.5608, 0.8314, 0.9255, 1.0)
        _SssStrength            ("背光透射强度", Range(0.0, 1.5)) = 0.5
        _SssHeightRef           ("等效厚度参考波高（世界单位）", Range(0.1, 3.0)) = 0.9
        _SssPathScale           ("等效光程缩放（世界单位）", Range(0.5, 8.0)) = 2.5
        _SssExtinction          ("消光系数", Range(0.0, 4.0)) = 1.2
        _SssPhaseG              ("前向相位 g（0=各向同性，0.999=窄峰）", Range(0.0, 0.95)) = 0.6

        // ---- 屏幕空间折射（需 URP Opaque Texture）----
        _RefractionStrength     ("折射 UV 偏移强度（保守）", Range(0.0, 0.15)) = 0.035
        _RefractionBlend        ("折射混入比例", Range(0.0, 1.0)) = 0.22
        _RefractionDepthFade    ("折射起效水深（世界单位）", Range(0.1, 10.0)) = 1.6

        // ---- 假焦散（程序化，按深度衰减）----
        _CausticColor           ("焦散色", Color) = (0.80, 1.0, 0.92, 1.0)
        _CausticStrength        ("焦散强度", Range(0.0, 2.0)) = 0.30
        _CausticScale           ("焦散尺度（1/世界单位）", Float) = 0.55
        _CausticSpeed           ("焦散流动速度", Range(0.0, 2.0)) = 0.35
        _CausticWarp            ("焦散域扭曲强度", Range(0.0, 2.0)) = 0.60
        _CausticDepthFade       ("焦散消失水深（世界单位）", Range(0.1, 10.0)) = 2.2

        // ---- 岸边泡沫 ----
        _FoamColor              ("泡沫色", Color) = (0.94, 0.97, 1.0, 1.0)
        _FoamWidth              ("近岸泡沫厚度（世界单位）", Range(0.05, 8.0)) = 1.6
        _FoamNoiseScale         ("泡沫噪声尺度", Float) = 5.0
        _FoamNoiseScale2        ("泡沫破碎噪声尺度", Float) = 13.0
        _FoamSpeed              ("泡沫流动速度", Range(0.0, 2.0)) = 0.25
        _FoamStrength           ("泡沫强度", Range(0.0, 1.5)) = 0.85
        _ShorelineFoamGain      ("轮廓泡沫强度（屏幕空间深度梯度）", Range(0.0, 5.0)) = 1.6
        _FoamBreakup            ("泡沫破碎强度", Range(0.0, 1.0)) = 0.45
        _FoamPulseSpeed         ("泡沫涌岸速度", Range(0.0, 2.0)) = 0.55
        _FoamPulseFrequency     ("泡沫涌岸频率（每世界单位）", Range(0.0, 4.0)) = 1.2
        _FoamPulseStrength      ("泡沫涌岸强度", Range(0.0, 1.0)) = 0.60

        // ---- 高度场模拟接入（与旧件同名全局；只做法线扰动 + 泡沫源）----
        _HeightFieldNormalStrength ("高度场法线扰动强度", Range(0.0, 2.0)) = 0.35
        _HeightFieldFoamStrength   ("高度场泡沫强度", Range(0.0, 2.0)) = 0.60
        _ObstacleFoamBoost         ("障碍接触带泡沫加亮", Range(0.0, 3.0)) = 1.20

        // ---- 大尺度低频破坏噪声（打断 tiling 自相关，沿旧件）----
        _BreakupScale           ("基础色破坏噪声频率（1/世界单位）", Float) = 0.0222
        _BreakupTintDepth       ("基础色破坏幅度（±比例）", Range(0.0, 0.15)) = 0.04
        _BreakupSpeed           ("基础色破坏流动速度", Float) = 0.06

        // ---- 菲涅尔 / 反射 / 高光 ----
        _FresnelPower           ("菲涅尔指数", Range(0.5, 12.0)) = 5.0
        _FresnelStrength        ("菲涅尔强度", Range(0.0, 2.0)) = 1.0
        _ReflectionStrength     ("环境反射强度", Range(0.0, 2.0)) = 0.7
        _Smoothness             ("光滑度", Range(0.0, 1.0)) = 0.92
        _SpecularIntensity      ("主光镜面强度（风格化）", Range(0.0, 8.0)) = 0.5

        // ---- 太阳光路（r7 收口权重，沿旧件；宽瓣光路带 + 任意机位暖波光 + 窄瓣闪点）----
        _SunSpecColor           ("太阳光路色（暖金 hue≈45）", Color) = (1.0, 0.86, 0.45, 1.0)
        _SunSpecBroadStrength   ("宽瓣主项强度（平水面光路带）", Range(0.0, 2.0)) = 0.8
        _SunSpecLaneShininess   ("宽瓣主项锐度（越小光路越大）", Range(6.0, 120.0)) = 24.0
        _SunSpecWaveStrength    ("宽瓣辅项强度（任意机位暖波光）", Range(0.0, 2.0)) = 0.4
        _SunSpecBroadShininess  ("宽瓣辅项锐度（波法线指数）", Range(10.0, 240.0)) = 50.0
        _SunSpecLaneWidth       ("宽瓣沿太阳方位加宽（拉成光路带）", Range(1.0, 6.0)) = 2.5
        _SunSpecPatchScale      ("宽瓣碎块噪声尺度（世界单位⁻¹）", Float) = 8.0
        _SunSpecPatchDepth      ("宽瓣碎块深度（不挖洞）", Range(0.0, 1.0)) = 0.40
        _SunSpecCrestBias       ("宽瓣波峰偏置（填实空心环）", Range(0.0, 1.0)) = 0.70
        _SunSpecSlopeBoost      ("窄瓣法线斜率放大（拉出闪点）", Range(1.0, 16.0)) = 12.0
        _SunSpecStrength        ("窄瓣（闪点）强度", Range(0.0, 20.0)) = 5.0
        _SunSpecShininess       ("窄瓣锐度（400+ = 细闪点）", Range(20.0, 1200.0)) = 320.0
        _SunSpecGlitter         ("波光破碎强度（0=整片光路）", Range(0.0, 1.0)) = 0.55
        _SunSheenStrength       ("掠射暖光泽（阳光感保底）", Range(0.0, 1.0)) = 0.10

        // ---- 不透明度 ----
        _Opacity                ("基础不透明度", Range(0.0, 1.0)) = 0.82

        // ---- 调试 ----
        _DebugMode              ("调试模式 0-13", Range(0.0, 13.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 透明水面：不写深度（否则会挡住自己与后续透明物），但仍做深度测试被不透明物体遮挡。
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   OceanVertex
            #pragma fragment OceanFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 必须在 Lighting.hlsl 之后（Shadows.hlsl 依赖其 include 链的 LerpWhiteTo，旧件实测）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            // _CameraDepthTexture + SampleSceneDepth()（需 URP Asset 打开 Require Depth Texture）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // _CameraOpaqueTexture + SampleSceneColor()（需 URP Asset 打开 Opaque Texture）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _MidColor;
                float4 _DeepColor;
                float  _ShoreFadeDistance;
                float4 _S1Dir; float _S1Length; float _S1Amp; float _S1Steep; float _S1Speed;
                float4 _S2Dir; float _S2Length; float _S2Amp; float _S2Steep; float _S2Speed;
                float4 _W1Dir; float _W1Length; float _W1Amp; float _W1Steep; float _W1Speed;
                float4 _W2Dir; float _W2Length; float _W2Amp; float _W2Steep; float _W2Speed;
                float4 _W3Dir; float _W3Length; float _W3Amp; float _W3Steep; float _W3Speed;
                float4 _W4Dir; float _W4Length; float _W4Amp; float _W4Steep; float _W4Speed;
                float  _GridCellSize;
                float  _GridUniformRadius;
                float  _GridRingGrowth;
                float  _DisplaceFadeStart;
                float  _DisplaceFadeEnd;
                float4 _HorizonColor;
                float  _HorizonFadeStart;
                float  _HorizonFadeEnd;
                float  _WaveScaleA;
                float  _WaveSpeedA;
                float  _WaveStrengthA;
                float4 _WaveDirectionA;
                float  _WaveScaleB;
                float  _WaveSpeedB;
                float  _WaveStrengthB;
                float4 _WaveDirectionB;
                float  _DetailFarFadeStart;
                float  _DetailFarFadeEnd;
                float  _WhitecapStrength;
                float  _WhitecapJacobianThreshold;
                float  _WhitecapSoftness;
                float  _WhitecapShoreScale;
                float  _WhitecapNoiseScale;
                float4 _SssColor;
                float  _SssStrength;
                float  _SssHeightRef;
                float  _SssPathScale;
                float  _SssExtinction;
                float  _SssPhaseG;
                float  _RefractionStrength;
                float  _RefractionBlend;
                float  _RefractionDepthFade;
                float4 _CausticColor;
                float  _CausticStrength;
                float  _CausticScale;
                float  _CausticSpeed;
                float  _CausticWarp;
                float  _CausticDepthFade;
                float4 _FoamColor;
                float  _FoamWidth;
                float  _FoamNoiseScale;
                float  _FoamNoiseScale2;
                float  _FoamSpeed;
                float  _FoamStrength;
                float  _ShorelineFoamGain;
                float  _FoamBreakup;
                float  _FoamPulseSpeed;
                float  _FoamPulseFrequency;
                float  _FoamPulseStrength;
                float  _HeightFieldNormalStrength;
                float  _HeightFieldFoamStrength;
                float  _ObstacleFoamBoost;
                float  _BreakupScale;
                float  _BreakupTintDepth;
                float  _BreakupSpeed;
                float  _FresnelPower;
                float  _FresnelStrength;
                float  _ReflectionStrength;
                float  _Smoothness;
                float  _SpecularIntensity;
                float4 _SunSpecColor;
                float  _SunSpecBroadStrength;
                float  _SunSpecBroadShininess;
                float  _SunSpecLaneShininess;
                float  _SunSpecWaveStrength;
                float  _SunSpecLaneWidth;
                float  _SunSpecPatchScale;
                float  _SunSpecPatchDepth;
                float  _SunSpecCrestBias;
                float  _SunSpecSlopeBoost;
                float  _SunSpecStrength;
                float  _SunSpecShininess;
                float  _SunSpecGlitter;
                float  _SunSheenStrength;
                float  _Opacity;
                float  _DebugMode;
            CBUFFER_END

            // ---- 全局：水面模拟（WaterSimulationDriver 发布，与旧件同名 → 涟漪注入零适配）----
            TEXTURE2D(_WaterHeightField);
            SAMPLER(sampler_WaterHeightField);
            TEXTURE2D(_WaterObstacleMap);
            SAMPLER(sampler_WaterObstacleMap);
            float4 _WaterSimOrigin;   // (中心X, 中心Z, 域边长, 每轴格数)
            float  _WaterSimEnabled;  // 0 = 未接驱动，跳过高度场/障碍两路
            float4 _WaterSunDir;      // 从水面指向光源 L；未接时为 0 → 退回主光方向

            // ---- 全局：OceanRig 发布（长涌包络 + 网格中心）。刻意不放 UnityPerMaterial（全局 uniform，SRP Batcher 兼容）----
            float4 _OceanArenaCenter; // (中心X, 中心Z, 近岸保护半径, 全涌半径)
            float4 _OceanGridCenter;  // (网格中心X, 网格中心Z, 0, 0)

            // ------------------------------------------------------------------
            // 程序化噪声（与 PirateWater/PirateSurface 内副本一致，刻意重复免 include 链）
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

            float PirateFbm2(float2 p)
            {
                return 0.65 * PirateValueNoise(p) + 0.35 * PirateValueNoise(p * 2.03 + float2(17.1, 9.7));
            }

            // 单层细节波法线：FBM 高度场在水平面上的梯度（只作高频细节，低频交给 Gerstner）。
            float3 PirateWaterWaveNormal(float2 worldXZ, float2 direction, float scale, float speed, float strength, float time)
            {
                float2 dir = normalize(direction + float2(1e-5, 1e-5));
                float2 q   = worldXZ * scale + dir * (time * speed);

                float e  = 0.15;
                float h  = PirateFbm(q);
                float hx = PirateFbm(q + float2(e, 0.0));
                float hy = PirateFbm(q + float2(0.0, e));

                float3 n = float3(-(hx - h) / e, 1.0, -(hy - h) / e);
                n.xz *= strength;
                return normalize(n);
            }

            // ------------------------------------------------------------------
            // Gerstner 叠加（与 WaterRules/OceanRules 同式；改一边必须改另一边）
            // ------------------------------------------------------------------

            struct PirateWaveAccum
            {
                float2 horiz;   // 水平位移 (Δx, Δz)
                float  height;  // 垂直位移
                float3 Tx;      // ∂P/∂x
                float3 Tz;      // ∂P/∂z
            };

            void PirateAddWave(inout PirateWaveAccum acc, float2 dir, float wavelength,
                               float amp, float steep, float speed, float2 baseXZ, float t)
            {
                float len = max(wavelength, 0.05);
                float k   = 6.28318530718 / len;
                dir = (dot(dir, dir) < 1e-8) ? float2(1.0, 0.0) : normalize(dir);
                amp   = max(amp, 0.0);
                steep = saturate(steep);

                // 深水色散 ω = speed·√(g·k)
                float theta = k * dot(dir, baseXZ) - max(speed, 0.0) * sqrt(9.81 * k) * t;
                float s = sin(theta);
                float c = cos(theta);

                float QAk = steep * amp * k;
                float Ak  = amp * k;

                acc.horiz.x += QAk * dir.x * c;
                acc.horiz.y += QAk * dir.y * c;
                acc.height  += amp * s;

                acc.Tx += float3(-QAk * dir.x * dir.x * s, Ak * dir.x * c, -QAk * dir.x * dir.y * s);
                acc.Tz += float3(-QAk * dir.x * dir.y * s, Ak * dir.y * c, -QAk * dir.y * dir.y * s);
            }

            // 单条波的几何解析淡出（OceanRules.WaveGeometricFade 同式）：
            // 每波长 ≥3 顶点（旧 WaterMeshRules 达标线）→ 完整位移；跌破 2.1 顶点/波 → 0，
            // 防欠采样短波在粗网格处 boiling。法线不受影响。
            float OceanGeometricFade(float wavelength, float cell)
            {
                float ratio = max(wavelength, 0.5) / (3.0 * max(cell, 1e-4));
                return saturate((ratio - 0.7) / 0.3);
            }

            // 距网格中心 r 处的环宽（OceanGridRules.RingWidthAtRadius 同式闭式解）。
            float OceanLocalCellSize(float2 baseXZ)
            {
                float d = distance(baseXZ, _OceanGridCenter.xy);
                if (d <= _GridUniformRadius)
                    return _GridCellSize;
                float x = 1.0 + (d - _GridUniformRadius) * (_GridRingGrowth - 1.0) / _GridCellSize;
                float k = floor(log(max(x, 1.0)) / log(max(_GridRingGrowth, 1.0001)));
                return _GridCellSize * pow(_GridRingGrowth, max(k, 0.0));
            }

            // 三路淡出一次算齐：位移远淡 / 长涌包络 / 局部环宽。
            void OceanResolveFades(float3 positionWS, float2 baseXZ,
                                   out float farFade, out float swellEnv, out float cell)
            {
                float dCam = distance(positionWS, _WorldSpaceCameraPos.xyz);
                farFade = 1.0 - smoothstep(_DisplaceFadeStart, _DisplaceFadeEnd, dCam);

                float dArena = distance(baseXZ, _OceanArenaCenter.xy);
                float e0 = _OceanArenaCenter.z;
                float e1 = max(_OceanArenaCenter.w, e0 + 1.0);   // 未接 OceanRig 时的除零兜底
                swellEnv = smoothstep(e0, e1, dArena);

                cell = OceanLocalCellSize(baseXZ);
            }

            PirateWaveAccum OceanGerstner(float2 baseXZ, float t, float farFade, float swellEnv, float cell)
            {
                PirateWaveAccum acc;
                acc.horiz  = float2(0.0, 0.0);
                acc.height = 0.0;
                acc.Tx     = float3(1.0, 0.0, 0.0);
                acc.Tz     = float3(0.0, 0.0, 1.0);

                float gS1 = swellEnv * farFade * OceanGeometricFade(_S1Length, cell);
                PirateAddWave(acc, _S1Dir.xz, _S1Length, _S1Amp * gS1, _S1Steep, _S1Speed, baseXZ, t);
                float gS2 = swellEnv * farFade * OceanGeometricFade(_S2Length, cell);
                PirateAddWave(acc, _S2Dir.xz, _S2Length, _S2Amp * gS2, _S2Steep, _S2Speed, baseXZ, t);

                float gW1 = farFade * OceanGeometricFade(_W1Length, cell);
                PirateAddWave(acc, _W1Dir.xz, _W1Length, _W1Amp * gW1, _W1Steep, _W1Speed, baseXZ, t);
                float gW2 = farFade * OceanGeometricFade(_W2Length, cell);
                PirateAddWave(acc, _W2Dir.xz, _W2Length, _W2Amp * gW2, _W2Steep, _W2Speed, baseXZ, t);
                float gW3 = farFade * OceanGeometricFade(_W3Length, cell);
                PirateAddWave(acc, _W3Dir.xz, _W3Length, _W3Amp * gW3, _W3Steep, _W3Speed, baseXZ, t);
                float gW4 = farFade * OceanGeometricFade(_W4Length, cell);
                PirateAddWave(acc, _W4Dir.xz, _W4Length, _W4Amp * gW4, _W4Steep, _W4Speed, baseXZ, t);
                return acc;
            }

            // 有效振幅和（白帽/调试的归一化分母，与 OceanGerstner 的衰减口径一致）。
            float OceanEffectiveAmpSum(float farFade, float swellEnv, float cell)
            {
                return _S1Amp * swellEnv * farFade * OceanGeometricFade(_S1Length, cell)
                     + _S2Amp * swellEnv * farFade * OceanGeometricFade(_S2Length, cell)
                     + _W1Amp * farFade * OceanGeometricFade(_W1Length, cell)
                     + _W2Amp * farFade * OceanGeometricFade(_W2Length, cell)
                     + _W3Amp * farFade * OceanGeometricFade(_W3Length, cell)
                     + _W4Amp * farFade * OceanGeometricFade(_W4Length, cell);
            }

            // 白帽掩码（OceanRules.WhitecapMask 同式）：陡度阈值 × 波高门槛 × 噪声碎化 × 近岸降权。
            float OceanWhitecapMask(float jacobian, float heightNorm, float noise01, float swellEnv)
            {
                float steep = saturate((_WhitecapJacobianThreshold - jacobian) / max(_WhitecapSoftness, 1e-3));
                float crest = saturate((heightNorm - 0.35) / 0.4);
                float gain  = 0.55 + saturate(noise01) * 0.9;
                float shore = lerp(_WhitecapShoreScale, 1.0, saturate(swellEnv));
                return saturate(steep * crest * gain * shore);
            }

            // ------------------------------------------------------------------
            // 假焦散 / 泡沫脉冲（沿旧件，同式）
            // ------------------------------------------------------------------
            float PirateCaustic(float2 worldXZ, float t)
            {
                float2 q = worldXZ * _CausticScale;
                float2 warp = float2(
                    PirateFbm2(q + float2(0.0, t * _CausticSpeed)),
                    PirateFbm2(q + float2(5.2, 1.3) - float2(t * _CausticSpeed, 0.0)));
                float n = PirateFbm2(q * 1.7 + warp * _CausticWarp + float2(t * _CausticSpeed * 0.3, 0.0));
                n = 1.0 - abs(n * 2.0 - 1.0);
                return n * n;
            }

            float WaterFoamPulseShape(float waterDepth, float t)
            {
                float ph = t * _FoamPulseSpeed - waterDepth * _FoamPulseFrequency;
                float s = 0.5 + 0.5 * sin(ph);
                return s * s;
            }

            struct AttributesOcean
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsOcean
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                real   fogFactor  : TEXCOORD2;
                float2 baseXZ     : TEXCOORD3;   // 位移前的世界 XZ：片元用它重算解析法线
            };

            VaryingsOcean OceanVertex(AttributesOcean IN)
            {
                VaryingsOcean OUT;

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);

                float3 positionWS = vpi.positionWS;
                float2 baseXZ = positionWS.xz;

                float farFade, swellEnv, cell;
                OceanResolveFades(positionWS, baseXZ, farFade, swellEnv, cell);

                PirateWaveAccum acc = OceanGerstner(baseXZ, _Time.y, farFade, swellEnv, cell);
                positionWS = float3(baseXZ.x + acc.horiz.x,
                                    positionWS.y + acc.height,
                                    baseXZ.y + acc.horiz.y);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS   = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.baseXZ     = baseXZ;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 OceanFragment(VaryingsOcean IN) : SV_Target
            {
                float3 geometricNormalWS = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float  t = _Time.y;

                // ---- 解析法线：6 波 Gerstner 的解析导数（含包络/远淡/网格 LOD，与顶点位移同源）----
                float farFade, swellEnv, cell;
                OceanResolveFades(IN.positionWS, IN.baseXZ, farFade, swellEnv, cell);

                PirateWaveAccum acc = OceanGerstner(IN.baseXZ, t, farFade, swellEnv, cell);
                float3 nGerst = normalize(cross(acc.Tz, acc.Tx));
                float  jacobian = acc.Tx.x * acc.Tz.z - acc.Tz.x * acc.Tx.z;
                float  waveHeight = acc.height;
                float  effAmpSum = max(OceanEffectiveAmpSum(farFade, swellEnv, cell), 1e-3);

                // ---- 高频细节法线：FBM 梯度（远处淡出防闪点）----
                float detailFade = 1.0 - smoothstep(_DetailFarFadeStart, _DetailFarFadeEnd,
                                                    distance(IN.positionWS, _WorldSpaceCameraPos.xyz));
                float3 waveA = PirateWaterWaveNormal(IN.positionWS.xz, _WaveDirectionA.xz, _WaveScaleA, _WaveSpeedA, _WaveStrengthA * detailFade, t);
                float3 waveB = PirateWaterWaveNormal(IN.positionWS.xz, _WaveDirectionB.xz, _WaveScaleB, _WaveSpeedB, _WaveStrengthB * detailFade, t);
                float3 n = normalize(float3(nGerst.x + waveA.x + waveB.x, nGerst.y, nGerst.z + waveA.z + waveB.z));
                // 与几何法线混合（网格法线全朝上 → 等价于直接用解析法线；保留旧件的侧壁保护逻辑）。
                n = normalize(lerp(geometricNormalWS, n, saturate(geometricNormalWS.y)));

                // ---- 场景深度 → 浅深水三档（沿旧件）----
                float2 screenUV = IN.positionCS.xy * _ScreenParams.zw;
                float  rawDepth = SampleSceneDepth(screenUV);
                float  sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float  surfaceEye = -TransformWorldToView(IN.positionWS).z;
                float  waterDepth = clamp(sceneEye - surfaceEye, 0.0, 200.0);

                float shallowToMid = saturate(waterDepth / max(_ShoreFadeDistance * 0.25, 0.01));
                float midToDeep = saturate((waterDepth - _ShoreFadeDistance * 0.25)
                                           / max(_ShoreFadeDistance * 0.75, 0.01));
                half3 waterColor = lerp(_ShallowColor.rgb, _MidColor.rgb, (half)shallowToMid);
                waterColor = lerp(waterColor, _DeepColor.rgb, (half)midToDeep);

                // ---- 大尺度低频破坏噪声（沿旧件）----
                float2 breakupXZ = IN.positionWS.xz;
                float  breakupN1 = PirateFbm(breakupXZ * _BreakupScale
                                            + float2(t * _BreakupSpeed, -t * _BreakupSpeed * 0.7));
                float  breakupN2 = PirateFbm(breakupXZ * (_BreakupScale * 0.53) + float2(41.7, 13.3)
                                            + float2(-t * _BreakupSpeed * 0.31, t * _BreakupSpeed * 0.23));
                float  breakup = 0.5 * (breakupN1 + breakupN2);
                waterColor *= 1.0 + (breakup - 0.5) * 2.0 * _BreakupTintDepth;

                // ---- 高度场模拟（全局；驱动缺席时 enabled=0，整段跳过）----
                float2 simUV = (IN.positionWS.xz - _WaterSimOrigin.xy) / max(_WaterSimOrigin.z, 1e-4) + 0.5;
                float  hfMask = 0.0;
                float2 hfNormal = float2(0.0, 0.0);
                float  hfFoam = 0.0;
                float  hfHeight = 0.0;
                float  hfObstacle = 0.0;
                if (_WaterSimEnabled > 0.5)
                {
                    bool inside = simUV.x >= 0.0 && simUV.x <= 1.0 && simUV.y >= 0.0 && simUV.y <= 1.0;
                    if (inside)
                    {
                        float2 edge = smoothstep(float2(0.0, 0.0), float2(0.04, 0.04), simUV)
                                    * smoothstep(float2(0.0, 0.0), float2(0.04, 0.04), 1.0 - simUV);
                        hfMask = edge.x * edge.y;

                        float2 uv = saturate(simUV);
                        float4 hf = SAMPLE_TEXTURE2D(_WaterHeightField, sampler_WaterHeightField, uv);
                        hfHeight = hf.r * 2.0 - 1.0;
                        hfNormal = hf.gb * 2.0 - 1.0;
                        hfFoam = hf.a;

                        // 障碍：采本格 + 四邻（约 1.5 格膨胀）→ 接触带泡沫加亮（沿旧件）。
                        float texel = 1.0 / max(_WaterSimOrigin.w, 1.0);
                        float o = SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, uv).r;
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv + float2(texel * 1.5, 0.0))).r);
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv - float2(texel * 1.5, 0.0))).r);
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv + float2(0.0, texel * 1.5))).r);
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv - float2(0.0, texel * 1.5))).r);
                        hfObstacle = o;

                        n = normalize(n + float3(hfNormal.x, 0.0, hfNormal.y) * (_HeightFieldNormalStrength * hfMask));
                    }
                }

                // ---- 假焦散 ----
                float caustic = PirateCaustic(IN.positionWS.xz, t);
                float causticFade = (1.0 - saturate(waterDepth / max(_CausticDepthFade, 0.01)));
                causticFade *= causticFade;
                waterColor += _CausticColor.rgb * (_CausticStrength * caustic * causticFade);

                // ---- 主光上提（背光透射与太阳光路共用；阴影按工程变体取，沿旧件）----
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif
                half lightAtten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float3 sunToLight = dot(_WaterSunDir.xyz, _WaterSunDir.xyz) > 1e-4
                    ? normalize(_WaterSunDir.xyz) : normalize(mainLight.direction);

                // ---- 屏幕空间折射（水体内部层，先于表面反射/泡沫）----
                float refrMask = saturate(waterDepth / max(_RefractionDepthFade, 0.01));
                float edgeFade = 0.06;
                refrMask *= smoothstep(0.0, edgeFade, screenUV.x) * smoothstep(0.0, edgeFade, 1.0 - screenUV.x)
                          * smoothstep(0.0, edgeFade, screenUV.y) * smoothstep(0.0, edgeFade, 1.0 - screenUV.y);
                float2 refrUV = screenUV + n.xz * _RefractionStrength;
                half3 refracted = SampleSceneColor(refrUV);
                half3 body = lerp(waterColor, refracted, saturate((half)(_RefractionBlend * refrMask)));

                // ---- 波背背光透射 + 薄层散射（技法吸收自 HPWater BSDF 的 diffT 项）----
                // HPWater 原式（HDRP 延迟，HPWaterBSDFLibary.hlsl Part2/3）：
                //   backlit = LightColor × saturate(-NdotL) × exp(-σ·d·scale) × HenyeyPhase(dot(V,-L), g≈0.9998)
                // 本工程适配（URP 前向 + 低多边形风格化）：厚度用绝对波高近似（HPWater 同样
                // "thickness 由高度场近似得到"），相位 g 降到宽辉光区间（0.9998 窄峰在低模上不可见）；
                // 近岸波矮 → 等效厚度小 → 自动弱，与"外海涌大更透光"的观感一致。
                float NdotLsun = dot(n, sunToLight);
                float sssThickness = saturate(waveHeight / max(_SssHeightRef, 0.01));
                float T_backlit = exp(-_SssExtinction * sssThickness * _SssPathScale);
                float sssCos = dot(viewDirWS, -sunToLight);
                float gPhase = saturate(_SssPhaseG);
                float P_backlit = (1.0 - gPhase * gPhase)
                    / (12.5663706 * pow(max(1.0 + gPhase * gPhase - 2.0 * gPhase * sssCos, 1e-3), 1.5));
                float G_backlit = saturate(-NdotLsun);            // 波背朝向光源（HPWater Part3）
                float G_sss = saturate(1.0 - max(NdotLsun, 0.0)); // 侧面薄层散射（HPWater G_sss = 1 - G_entry）
                half3 sssTerm = (half3)mainLight.color
                    * (half)((G_backlit * T_backlit * P_backlit + G_sss * T_backlit * 0.15) * _SssStrength * lightAtten);
                body += _SssColor.rgb * sssTerm;

                // ---- 岸边泡沫（沿旧件）----
                float foamFalloff = 1.0 - saturate(waterDepth / max(_FoamWidth, 0.01));
                float foamNoise = PirateFbm(IN.positionWS.xz * _FoamNoiseScale + t * _FoamSpeed * float2(0.6, 0.4));
                float foamNoise2 = PirateFbm(IN.positionWS.xz * _FoamNoiseScale2 - t * _FoamSpeed * float2(0.35, 0.5));
                float foamBreakup = saturate(0.5 + (foamNoise - foamNoise2) * _FoamBreakup * 2.0);
                float pulse = WaterFoamPulseShape(waterDepth, t);
                float foamShore = foamFalloff * (0.45 + 0.55 * foamNoise * foamBreakup)
                                 * lerp(1.0, pulse, _FoamPulseStrength);
                float depthEdge = (abs(ddx(sceneEye)) + abs(ddy(sceneEye))) * _ShorelineFoamGain;
                float foamLine = saturate(depthEdge) * foamFalloff;
                half foam = saturate(foamShore * _FoamStrength + foamLine);
                half hfFoamTerm = (half)(hfFoam * _HeightFieldFoamStrength * hfMask * (1.0 + hfObstacle * _ObstacleFoamBoost));
                foam = saturate(foam + hfFoamTerm);

                // ---- 波峰白帽：雅可比陡度阈值 × 波高 × 世界空间噪声碎化 × 近岸降权 ----
                float wcNoise = PirateFbm(IN.positionWS.xz * _WhitecapNoiseScale + float2(t * 0.11, -t * 0.07)) * 0.65
                              + PirateFbm(IN.positionWS.xz * (_WhitecapNoiseScale * 2.7) + float2(-t * 0.05, t * 0.09)) * 0.35;
                float heightNorm = 0.5 + 0.5 * (waveHeight / effAmpSum);
                float whitecap = OceanWhitecapMask(jacobian, heightNorm, wcNoise, swellEnv) * _WhitecapStrength;
                foam = saturate(foam + (half)whitecap);

                // ---- 调试档（0-10 沿旧件语义；11-13 本件新增）----
                // 1 水色：三档色块 + 焦散 + 折射。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(body, 1.0h);
                // 2 波法线（含细节层）。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(half3(n * 0.5 + 0.5), 1.0h);
                // 3 菲涅尔灰度。
                float debugFresnel = pow(saturate(1.0h - (half)dot(n, viewDirWS)), _FresnelPower) * _FresnelStrength;
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return half4(half3(saturate(debugFresnel), saturate(debugFresnel), saturate(debugFresnel)), 1.0h);
                // 4 全部泡沫掩码（岸沫 + 高度场 + 白帽）。
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                    return half4(foam, foam, foam, 1.0h);
                // 5 波高：灰底=0，波峰亮（归一化用有效振幅和）。
                if (_DebugMode > 4.5 && _DebugMode < 5.5)
                {
                    float g = saturate(0.5 + 0.5 * waveHeight / effAmpSum);
                    return half4(g, g, g, 1.0h);
                }
                // 6 波陡度（雅可比）：暗斑/红 = 压缩过度有自交风险。
                if (_DebugMode > 5.5 && _DebugMode < 6.5)
                {
                    float g = saturate(jacobian);
                    float warn = saturate((0.9 - jacobian) * 5.0);
                    return half4(saturate(g * 0.85 + warn), g * 0.9, g * 0.9, 1.0h);
                }
                // 7 高度场。
                if (_DebugMode > 6.5 && _DebugMode < 7.5)
                {
                    float g = saturate(0.5 + 0.5 * hfHeight);
                    return half4(lerp(half3(0.05, 0.1, 0.25), half3(g, g, g), (half)hfMask), 1.0h);
                }
                // 8 曲率（高度场 ∇²h）。
                if (_DebugMode > 7.5 && _DebugMode < 8.5)
                {
                    float texel = 1.0 / max(_WaterSimOrigin.w, 1.0);
                    float dx = _WaterSimOrigin.z * texel;
                    float2 uv = saturate(simUV);
                    float hC = SAMPLE_TEXTURE2D(_WaterHeightField, sampler_WaterHeightField, uv).r * 2.0 - 1.0;
                    float hL = SAMPLE_TEXTURE2D(_WaterHeightField, sampler_WaterHeightField, saturate(uv - float2(texel, 0))).r * 2.0 - 1.0;
                    float hR = SAMPLE_TEXTURE2D(_WaterHeightField, sampler_WaterHeightField, saturate(uv + float2(texel, 0))).r * 2.0 - 1.0;
                    float hD = SAMPLE_TEXTURE2D(_WaterHeightField, sampler_WaterHeightField, saturate(uv - float2(0, texel))).r * 2.0 - 1.0;
                    float hU = SAMPLE_TEXTURE2D(_WaterHeightField, sampler_WaterHeightField, saturate(uv + float2(0, texel))).r * 2.0 - 1.0;
                    float curv = (hL + hR + hD + hU - 4.0 * hC) / max(dx * dx, 1e-6);
                    float g = saturate(0.5 + curv * 4.0);
                    return half4(lerp(half3(0.05, 0.1, 0.25), half3(g, g, g), (half)hfMask), 1.0h);
                }
                // 9 泡沫累积。
                if (_DebugMode > 8.5 && _DebugMode < 9.5)
                {
                    float g = saturate(hfFoam);
                    return half4(saturate(g + hfObstacle * hfMask), g * 0.9, g * 0.8, 1.0h);
                }
                // 10 障碍图。
                if (_DebugMode > 9.5 && _DebugMode < 10.5)
                {
                    return half4(lerp(half3(0.05, 0.1, 0.25), half3(hfObstacle, hfObstacle, hfObstacle), (half)hfMask), 1.0h);
                }
                // 11 白帽掩码：外海波峰亮白碎块；近岸（包络 0）只剩零星暗点。
                if (_DebugMode > 10.5 && _DebugMode < 11.5)
                    return half4(whitecap, whitecap, whitecap, 1.0h);
                // 12 包络：R=长涌包络（竞技场黑→外海白）G=位移远淡 B=地平线淡。
                if (_DebugMode > 11.5 && _DebugMode < 12.5)
                    return half4(saturate(swellEnv), saturate(farFade), 1.0h, 1.0h);
                // 13 局部环宽灰度（网格 LOD）：近场黑、远处渐亮 = 环宽在长。
                if (_DebugMode > 12.5)
                    return half4(saturate(cell / 32.0), saturate(cell / 64.0), saturate(cell / 128.0), 1.0h);

                // ---- 菲涅尔反射（表面层）----
                half fresnel = pow(saturate(1.0h - (half)dot(n, viewDirWS)), (half)_FresnelPower) * (half)_FresnelStrength;
                half3 reflectDirWS = reflect(-viewDirWS, n);
                half3 reflectionColor = SampleSH(reflectDirWS) * (half)_ReflectionStrength;
                half3 color = lerp(body, reflectionColor, saturate(fresnel));

                // ---- 主光镜面（沿旧件：风格化 GGX 项，不乘 F0；mainLight/lightAtten 已在上文获取）----
                half alphaBRDF = 1.0h;
                BRDFData brdfData;
                InitializeBRDFData(waterColor, 0.0h, half3(0.0h, 0.0h, 0.0h), (half)_Smoothness, alphaBRDF, brdfData);

                half3 specular = DirectBRDFSpecular(brdfData, n, mainLight.direction, viewDirWS)
                               * mainLight.color * lightAtten * (half)_SpecularIntensity;
                // 主光镜面染暖（沿旧件：防中性白加色拉青）。
                specular *= lerp(half3(1.0h, 1.0h, 1.0h), (half3)_SunSpecColor.rgb, 0.7h);
                color = color + specular;

                // ---- 太阳光路（沿旧件 r7：宽瓣光路带 + 任意机位暖波光 + 窄瓣闪点 + 掠射 sheen）----
                // sunToLight 已在上文（背光透射处）解析：_WaterSunDir 优先、未接时退回主光方向。
                float2 sunAz  = normalize(sunToLight.xz + float2(1e-5, 1e-5));
                float2 sunTan = float2(-sunAz.y, sunAz.x);

                // 宽瓣主项：平水面镜射 + 各向异性整形 → 沿太阳方位的光路带（不做斜率放大，防空心霉斑）。
                float3 reflFlat = reflect(-viewDirWS, float3(0.0, 1.0, 0.0));
                float3 devVec   = reflFlat - sunToLight;
                float  laneW = max(_SunSpecLaneWidth, 1.0);
                float3 reflShaped = normalize(sunToLight
                    + float3(sunAz.x, 0.0, sunAz.y) * (dot(devVec, float3(sunAz.x, 0.0, sunAz.y)) / laneW)
                    + float3(sunTan.x, 0.0, sunTan.y) * dot(devVec, float3(sunTan.x, 0.0, sunTan.y))
                    + float3(0.0, 1.0, 0.0) * (devVec.y / laneW));
                float broadLane = pow(saturate(dot(reflShaped, sunToLight)), max(_SunSpecLaneShininess, 1.0));

                // 宽瓣辅项：放大波法线 + 波峰绝对值偏置填实环心（任意机位保底）。
                float3 halfVec = normalize(sunToLight + viewDirWS);
                float3 nSpecBroad = normalize(float3(nGerst.x * _SunSpecSlopeBoost, nGerst.y,
                                                     nGerst.z * _SunSpecSlopeBoost));
                float crest01 = saturate(abs(waveHeight) / effAmpSum);
                float broadWave = saturate(pow(saturate(dot(nSpecBroad, halfVec)),
                                               max(_SunSpecBroadShininess, 1.0))
                                           + _SunSpecCrestBias * crest01);

                // 碎块掩码：高频噪声把亮带切成小块（不挖洞）。
                float patchNoise = PirateFbm(IN.positionWS.xz * _SunSpecPatchScale + float2(t * 0.21, -t * 0.17));
                float patchMask = saturate((patchNoise - 0.45) * 2.2);
                float patchMod = lerp(1.0 - _SunSpecPatchDepth, 1.0, patchMask);
                broadLane *= patchMod;
                broadWave *= patchMod;

                // 窄瓣：极少数像素命中镜面角 = 波光闪点。
                float3 nSpecFlick = normalize(float3(nGerst.x * _SunSpecSlopeBoost, nGerst.y,
                                                     nGerst.z * _SunSpecSlopeBoost)
                    + float3(waveA.x + waveB.x, 0.0, waveA.z + waveB.z) * _SunSpecGlitter);
                float3 reflFlick = reflect(-viewDirWS, nSpecFlick);
                float flickTerm = pow(saturate(dot(reflFlick, sunToLight)), max(_SunSpecShininess, 1.0));

                float sparkleNoise = PirateFbm(IN.positionWS.xz * (_SunSpecPatchScale * 1.7) + float2(t * 0.7, -t * 0.5));
                float flickMask = saturate((sparkleNoise - 0.62) * 6.0);
                flickMask = lerp(1.0, flickMask, step(0.001, _SunSpecGlitter));

                half broadWeight = saturate((half)(broadLane * _SunSpecBroadStrength
                                                   + broadWave * _SunSpecWaveStrength));
                half flickWeight = (1.0h - exp(-(half)(flickTerm * _SunSpecStrength))) * (half)flickMask;
                half sheen = (half)(pow(saturate(1.0 - viewDirWS.y), 3.0) * _SunSheenStrength);
                half sunPathWeight = saturate(broadWeight + flickWeight + sheen);

                // 混色（不是加色）：暖金只落在镜射权重处，水体整体读蓝（r7 结论）。
                color = lerp(color, (half3)_SunSpecColor.rgb, sunPathWeight);
                color = lerp(color, _FoamColor.rgb, foam);
                color = MixFog(color, IN.fogFactor);

                // ---- 地平线融合：颜色融进雾色、alpha 收满 → 海天线干净（位移已在远处淡 0）----
                float horizonT = smoothstep(_HorizonFadeStart, _HorizonFadeEnd,
                                            distance(IN.positionWS, _WorldSpaceCameraPos.xyz));
                color = lerp(color, (half3)_HorizonColor.rgb, (half)horizonT);

                half alpha = saturate((half)_Opacity + saturate(fresnel) * 0.35h + foam * 0.3h);
                alpha = lerp(alpha, 1.0h, (half)horizonT);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
