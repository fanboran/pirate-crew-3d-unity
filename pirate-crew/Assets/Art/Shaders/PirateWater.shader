// ============================================================================
// PirateWater.shader —— 海盗军团夺宝 3D / 水面（程序化，无贴图资源依赖）
//
// 【风格契约】GDD §10.4「风格化写实」：海水三档色（浅 → 中 → 深），强高光、强边缘光。
//   三档色按 r4 实测（水体 L50 仅 48、71.5% 像素 L<60、四广角暖冷差全负）整体提亮到
//   浅 #5FB8DD / 中 #3F99C6 / 深 #2C7499：原 #4DA6D9/#2B7AB8/#1A4F7A 在 Trilight 环境光下
//   把水体读成"又暗又冷"，与"阳光明媚"的基准相反；提亮只抬明度、保持同一青蓝色相。
//   本 shader 把这两个"预设意图"落到 URP 的物理化写法上：
//   菲涅尔 + 环境反射（SH）+ 主光 GGX 镜面 + 太阳光路。
//
// ============================================================================
// 【本轮水体视觉升级：五层合成，顺序即物理层序】
//   1) 宏观波几何  Gerstner 4 条方向波（顶点位移）+ **同一组波的解析导数**法线。
//      旧版法线来自 FBM 有限差分、与顶点位移各算各的 → 低频起伏不上法线 → "像平的"。
//      现在几何与明暗同源；FBM 只作为叠加在解析法线上的高频细节层。
//      顶点阶段用位移后的世界 XZ 输出，片元用 **baseXZ（位移前的世界 XZ）** 重算解析法线，
//      保证法线对应的是"这块水面原始位置"的波面（解析解，与网格密度无关）。
//   2) 屏幕空间折射  按法线偏移 UV 采样 `_CameraOpaqueTexture`（需 URP Asset 打开 Opaque Texture）。
//      合成顺序：折射属于"水体内部"，在菲涅尔/反射（表面层）之前、泡沫（表面覆盖）之前。
//      保留 alpha 混合（不改写成不透明合成）——否则水面后的透明物（如低于水面的弹道预览点）
//      会被吃掉。代价是折射样本与真实背景会有一次轻微重复计入，故默认强度保守（见参数注释）。
//   3) 假焦散  程序化域扭曲双层噪声，按 waterDepth 衰减叠进水体色（浅台最亮、深处消失）。
//      这是"透过水看到海床光斑"的近似：真焦散需要写到海床材质上，而海床材质不在本波次改动范围。
//   4) 岸边泡沫增强  深度泡沫 + 屏幕空间轮廓线（原有）之上叠加：
//      · 滚动相位：亮带随水深相位向岸推进再灭（"涌上岸"）；
//      · 双层噪声破碎：打破"一条均匀白带"；
//      · 高度场泡沫累积 + 障碍接触带加亮（见 5）。
//   5) 高度场模拟  运行时 128×128 二维波动方程（Assets/Scripts/PirateCrew/Water/WaterWaveField2D.cs）
//      上传为 `_WaterHeightField`（R=高度 G=法线x B=法线z A=泡沫）。**只做法线扰动 + 泡沫源，
//      不做顶点位移**——顶点形状由 1) 的 Gerstner 负责，两者分工避免同一高度加两遍。
//      障碍图 `_WaterObstacleMap`（Editor/WaterAssetBuilder 烘焙）用于接触带加亮泡沫。
//      驱动不存在时 `_WaterSimEnabled` = 0（全局未设置默认 0）→ 自动退回纯 Gerstner + 深度泡沫。
//
// 【Scene Depth 的两个用途】
//   需要 URP Asset 打开 `Require Depth Texture`（BattleSceneLighting.ConfigureUrpAsset 已打开）。
//   `_CameraDepthTexture` 只含**不透明**物体深度（水面是 Transparent、不写深度，不会自遮挡）。
//     1) 浅深水色过渡：waterDepth = 场景不透明深度 − 水面深度。越浅（海床越近）越偏浅色。
//     2) 岸边泡沫线：近岸衰减 + 屏幕空间深度梯度（岛屿/海床轮廓处的白线）。
//
// 【本场景为什么必须有海床(shelf)才能看到浅深水过渡（别当多余几何删掉）】
//   竞技场是"浮在海上的沙岛"：地面顶面 y=0，水面 y=-0.2。
//   M2BattleSceneSetup.CreateSeabedShelves() 在岛外生成 5 级**只写深度、不碰撞**的环形台阶
//   （内缘-外缘 0→3→6→11→20→34，顶面从 waterWorldY-0.18 逐级降到 -2.9），
//   于是 waterDepth 呈连续"浅 → 中 → 深"三级，正好对上三档海水色，也是假焦散的可见区。
//
// 【太阳光路（r5：宽瓣=平水面光路带 + 波法线暖波光，窄瓣稀疏闪点，再补伪地平线暖带）】
//   符号约定：`_WaterSunDir` = 从水面指向光源 L（驱动发 -sun.forward；与 GetMainLight().direction 同向），
//   未接驱动（w 全 0）时退回 GetMainLight().direction；片元里**不能**再取负（r3 的符号错误）。
//   · 宽瓣主项（方向性光路带）：视线在静水面上的镜射方向 `reflect(-V, up)` 与 L 比对。为什么不用
//     放大后的波法线做主体：`_SunSpecSlopeBoost` 放大 Gerstner 法线水平分量后，法线会在每个波峰
//     周围**转一整圈**，等值线 dot(n, half)=1 闭合成环 —— 画面里就是 r4 实测的"空心椭圆霉斑"
//     （一圈亮、中心反暗：中心处法线已越过 halfVec，dot 反而最小）。平水面镜射没有这个自由度，
//     天然只有单块软斑；再对误差向量做各向异性整形（`_SunSpecLaneWidth`，只压"面内"分量）→
//     软斑沿太阳方位拉成光路带。
//   · 宽瓣辅项（任意机位保底）：从相机看，太阳常不在取景方位（本场景主光方位与相机方位差 ~40°），
//     单靠平水面镜射会整片无光；故叠一项**放大波法线**的宽瓣去够镜面解。它仍会转圈，用
//     `_SunSpecCrestBias` 的**波峰绝对值偏置（加法）**把环中心填实（波峰顶 = 法线朝上 = 原空心处，
//     恰好 |波高| 最大）。主项/辅项都乘世界高频噪声（`_SunSpecPatchScale` / `_SunSpecPatchDepth`）
//     切成 15-30px 碎块（只做 [1-depth,1] 调制，不挖洞），避免"霉斑"连成规则纹样。
//   · 窄瓣：`nGerst` 放大 `_SunSpecSlopeBoost` 后叠回高频细节，`reflect(-V, n)` 与 L 做高 exponent
//     （`_SunSpecShininess`）比对 → 极少数像素命中；再乘噪声峰掩码稀疏化 = 波光闪点。
//   · 伪地平线暖带：低角机位下平水面镜射无解（要 ~42° 液面倾角），故补一条沿太阳方位
//     ±`_SunSpecAzBandDeg` 的远处暖带（按掠射权重），对应写实海面"远处雾带里透出的 sun glitter lane"。
//   · 色相：所有高光以 `_SunSpecColor`（暖金 hue≈45）**权重混合**（lerp）而非无限相加——加法会被水体
//     蓝底拉成青白（r4 亮水 hue 168-172 的成因）。`_SunSheenStrength` 另给掠射水面一层极轻暖光泽，
//     保证"看不到太阳和地平线"的取景也有阳光感。
//
// 【Pass 与 LightMode】只有 ForwardLit（"UniversalForward"）一个 Pass。
//   不做 ShadowCaster（透明水面不投影）、不做 DepthOnly（透明物体不进不透明深度预通道；
//   若给它 DepthOnly，水面会写进 _CameraDepthTexture 变成"自己挡自己"）。
//   → 本轮改动**只影响 ForwardLit 一个 Pass**；ShadowCaster/DepthOnly 本就不存在，无需同步改。
//
// 【_DebugMode 分档】0 正常 / 1 水色 / 2 波法线 / 3 菲涅尔 / 4 泡沫 / 5 波高 / 6 波陡度
//   / 7 高度场 / 8 曲率 / 9 泡沫累积 / 10 障碍图。每档"预期画面"见片元分支注释。
//
// ============================================================================
// 【[CHECK] 本地 URP/Core 14.0.12 包内逐条核对（符号存在 ≠ include 自洽，见 §八-1）】
//   Core.hlsl                          -> URP ShaderLibrary/Core.hlsl（:48/:66 定义 TEXTURE2D_X_FLOAT）
//   Lighting.hlsl                      -> 经它引入 BRDF.hlsl（BRDFData/InitializeBRDFData）
//                                         与 GlobalIllumination.hlsl（SampleSH）、RealtimeLights.hlsl（GetMainLight）
//   Shadows.hlsl                       -> TransformWorldToShadowCoord:319 / ApplyShadowBias:471
//   DeclareDepthTexture.hlsl           -> ShaderLibrary/DeclareDepthTexture.hlsl:8 SampleSceneDepth()
//   DeclareOpaqueTexture.hlsl          -> ShaderLibrary/DeclareOpaqueTexture.hlsl:8 SampleSceneColor()
//                                         （需 URP Asset 打开 m_RequireOpaqueTexture，否则采样为黑；
//                                          本 shader 用 _WaterSimEnabled 类似的思路无法探测，故由资产开关负责）
//   LinearEyeDepth(depth, zBufferParam) -> core Common.hlsl:1158；_ZBufferParams 见 UnityInput.hlsl:72
//   _ScreenParams                      -> URP ShaderLibrary/UnityInput.hlsl:60
//   _Time                              -> URP ShaderLibrary/UnityInput.hlsl（内置）
//   DirectBRDFSpecular                 -> BRDF.hlsl:183（不含 F0，故本 shader 直接乘主光色）
//   ComputeFogFactor / MixFog          -> ShaderVariablesFunctions.hlsl:329 / :414
//   GetWorldSpaceNormalizeViewDir      -> ShaderVariablesFunctions.hlsl:125
//   **本清单只能证明符号存在**；必须进图形界面编辑器 play 一次 + read_console 确认 0 shader error。
// ============================================================================

Shader "PirateCrew/PirateWater"
{
    Properties
    {
        // ---- 三档海水色（GDD §10.4；r5 整体提亮，只抬明度不改色相）----
        _ShallowColor           ("浅水色 #5FB8DD", Color) = (0.3725, 0.7216, 0.8667, 1.0)
        _MidColor               ("中水色 #3F99C6", Color) = (0.2471, 0.6000, 0.7765, 1.0)
        _DeepColor              ("深水色 #2C7499", Color) = (0.1725, 0.4549, 0.6000, 1.0)
        _ShoreFadeDistance      ("浅→深过渡深度（世界单位）", Range(0.1, 20.0)) = 5.0

        // ---- Gerstner 宏观波（4 条方向波；顶点位移 + 解析导数法线同源）----
        // 默认值依据见 Assets/Scripts/PirateCrew/Water/WaterRules.cs 的 DefaultWaves（两者必须一致）。
        _W1Dir                  ("W1 方向(xz)", Vector) = (1.0, 0.0, 0.25, 0.0)
        _W1Length               ("W1 波长（世界单位）", Float) = 13.0
        _W1Amp                  ("W1 振幅（世界单位）", Float) = 0.055
        _W1Steep                ("W1 陡度 0-1", Range(0.0, 1.0)) = 0.65
        _W1Speed                ("W1 相速倍率", Float) = 1.0

        _W2Dir                  ("W2 方向(xz)", Vector) = (0.6, 0.0, 1.0, 0.0)
        _W2Length               ("W2 波长（世界单位）", Float) = 7.5
        _W2Amp                  ("W2 振幅（世界单位）", Float) = 0.040
        _W2Steep                ("W2 陡度 0-1", Range(0.0, 1.0)) = 0.60
        _W2Speed                ("W2 相速倍率", Float) = 1.15

        _W3Dir                  ("W3 方向(xz)", Vector) = (-0.3, 0.0, 1.0, 0.0)
        _W3Length               ("W3 波长（世界单位）", Float) = 4.2
        _W3Amp                  ("W3 振幅（世界单位）", Float) = 0.028
        _W3Steep                ("W3 陡度 0-1", Range(0.0, 1.0)) = 0.55
        _W3Speed                ("W3 相速倍率", Float) = 1.30

        _W4Dir                  ("W4 方向(xz)", Vector) = (1.0, 0.0, -0.5, 0.0)
        _W4Length               ("W4 波长（世界单位）", Float) = 2.4
        _W4Amp                  ("W4 振幅（世界单位）", Float) = 0.016
        _W4Steep                ("W4 陡度 0-1", Range(0.0, 1.0)) = 0.50
        _W4Speed                ("W4 相速倍率", Float) = 1.50

        // ---- 高频细节法线（旧的双层 FBM，现在叠加在 Gerstner 解析法线之上）----
        _WaveScaleA             ("A 层细节波尺度", Float) = 0.32
        _WaveSpeedA             ("A 层细节波速", Float) = 0.45
        _WaveStrengthA          ("A 层细节法线强度", Range(0.0, 2.0)) = 0.55
        _WaveDirectionA         ("A 层细节波方向(xz)", Vector) = (1.0, 0.0, 0.35, 0.0)
        _WaveScaleB             ("B 层细节波尺度", Float) = 0.95
        _WaveSpeedB             ("B 层细节波速", Float) = 0.85
        _WaveStrengthB          ("B 层细节法线强度", Range(0.0, 2.0)) = 0.28
        _WaveDirectionB         ("B 层细节波方向(xz)", Vector) = (-0.4, 0.0, 1.0, 0.0)

        // ---- 屏幕空间折射（需 URP Opaque Texture）----
        // 保守默认：0.035 的 UV 偏移在掠射角（法线 xz ≈ 0.3）也只有约 1% 屏幕宽度，不会撕裂水缘。
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

        // ---- 高度场模拟接入（只做法线扰动 + 泡沫源；不做顶点位移）----
        _HeightFieldNormalStrength ("高度场法线扰动强度", Range(0.0, 2.0)) = 0.35
        _HeightFieldFoamStrength   ("高度场泡沫强度", Range(0.0, 2.0)) = 0.60
        _ObstacleFoamBoost         ("障碍接触带泡沫加亮", Range(0.0, 3.0)) = 1.20

        // ---- 菲涅尔 / 反射 / 高光 ----
        _FresnelPower           ("菲涅尔指数", Range(0.5, 12.0)) = 5.0
        _FresnelStrength        ("菲涅尔强度", Range(0.0, 2.0)) = 1.0
        _ReflectionStrength     ("环境反射强度", Range(0.0, 2.0)) = 1.0
        _Smoothness             ("光滑度", Range(0.0, 1.0)) = 0.92
        _SpecularIntensity      ("主光镜面强度（风格化，非能量守恒）", Range(0.0, 8.0)) = 0.5

        // ---- 太阳光路（宽瓣软带 + 窄瓣闪点 + 伪地平线暖带）【AI 提案：r5 修"太阳不知在哪"】----
        // 为什么平水面镜射、为什么混色而不是加色：见文件头【太阳光路（r5）】。
        _SunSpecColor           ("太阳光路色（暖金 hue≈45）", Color) = (1.0, 0.86, 0.45, 1.0)
        _SunSpecBroadStrength   ("宽瓣主项强度（平水面光路带）", Range(0.0, 2.0)) = 1.6
        _SunSpecLaneShininess   ("宽瓣主项锐度（越小光路越大）", Range(6.0, 120.0)) = 24.0
        _SunSpecWaveStrength    ("宽瓣辅项强度（任意机位暖波光）", Range(0.0, 2.0)) = 0.65
        _SunSpecBroadShininess  ("宽瓣辅项锐度（波法线指数）", Range(10.0, 240.0)) = 50.0
        _SunSpecLaneWidth       ("宽瓣沿太阳方位加宽（拉成光路带）", Range(1.0, 6.0)) = 2.5
        _SunSpecPatchScale      ("宽瓣碎块噪声尺度（世界单位⁻¹）", Float) = 8.0
        _SunSpecPatchDepth      ("宽瓣碎块深度（不挖洞）", Range(0.0, 1.0)) = 0.40
        _SunSpecCrestBias       ("宽瓣波峰偏置（填实空心环）", Range(0.0, 1.0)) = 0.70
        _SunSpecAzBandDeg       ("伪地平线暖带半角（度，沿太阳方位）", Range(5.0, 40.0)) = 22.0
        _SunSpecAzBandStrength  ("伪地平线暖带强度", Range(0.0, 1.0)) = 0.55
        _SunSpecSlopeBoost      ("窄瓣法线斜率放大（拉出闪点）", Range(1.0, 16.0)) = 12.0
        _SunSpecStrength        ("窄瓣（闪点）强度", Range(0.0, 20.0)) = 8.0
        _SunSpecShininess       ("窄瓣锐度（400+ = 细闪点）", Range(20.0, 1200.0)) = 320.0
        _SunSpecGlitter         ("波光破碎强度（0=整片光路）", Range(0.0, 1.0)) = 0.55
        _SunSheenStrength       ("掠射暖光泽（阳光感保底）", Range(0.0, 1.0)) = 0.20

        // ---- 不透明度 ----
        _Opacity                ("基础不透明度", Range(0.0, 1.0)) = 0.82

        // ---- 调试 ----
        // 0 正常 / 1 水色 / 2 波法线 / 3 菲涅尔 / 4 泡沫 / 5 波高 / 6 波陡度
        // / 7 高度场 / 8 曲率 / 9 泡沫累积 / 10 障碍图
        _DebugMode              ("调试模式 0-10", Range(0.0, 10.0)) = 0.0
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
            #pragma vertex   WaterVertex
            #pragma fragment WaterFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 必须在 Lighting.hlsl 之后：Shadows.hlsl:298 用 LerpWhiteTo，而它由 Lighting.hlsl 第 4 行的
            // BRDF.hlsl → core CommonMaterial.hlsl 提供（Shadows.hlsl 自身不 include）。
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
                float4 _W1Dir; float _W1Length; float _W1Amp; float _W1Steep; float _W1Speed;
                float4 _W2Dir; float _W2Length; float _W2Amp; float _W2Steep; float _W2Speed;
                float4 _W3Dir; float _W3Length; float _W3Amp; float _W3Steep; float _W3Speed;
                float4 _W4Dir; float _W4Length; float _W4Amp; float _W4Steep; float _W4Speed;
                float  _WaveScaleA;
                float  _WaveSpeedA;
                float  _WaveStrengthA;
                float4 _WaveDirectionA;
                float  _WaveScaleB;
                float  _WaveSpeedB;
                float  _WaveStrengthB;
                float4 _WaveDirectionB;
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
                float  _FresnelPower;
                float  _FresnelStrength;
                float  _ReflectionStrength;
                float  _Smoothness;
                float  _SpecularIntensity;
                float  _SunSpecStrength;
                float  _SunSpecShininess;
                float  _SunSpecGlitter;
                float4 _SunSpecColor;
                float  _SunSpecBroadStrength;
                float  _SunSpecBroadShininess;
                float  _SunSpecLaneShininess;
                float  _SunSpecWaveStrength;
                float  _SunSpecLaneWidth;
                float  _SunSpecPatchScale;
                float  _SunSpecPatchDepth;
                float  _SunSpecCrestBias;
                float  _SunSpecAzBandDeg;
                float  _SunSpecAzBandStrength;
                float  _SunSpecSlopeBoost;
                float  _SunSheenStrength;
                float  _Opacity;
                float  _DebugMode;
            CBUFFER_END

            // ---- 高度场模拟的全局（由 WaterSimulationDriver 设置；未设置时 enabled=0）----
            // 刻意不放 UnityPerMaterial：它们是全局 uniform，不是材质属性（SRP Batcher 兼容）。
            TEXTURE2D(_WaterHeightField);
            SAMPLER(sampler_WaterHeightField);
            TEXTURE2D(_WaterObstacleMap);
            SAMPLER(sampler_WaterObstacleMap);
            float4 _WaterSimOrigin;   // (中心X, 中心Z, 域边长, 每轴格数)
            float  _WaterSimEnabled;  // 0 = 未接驱动，跳过高度场/障碍两路
            float4 _WaterSunDir;      // 从水面指向光源 L（驱动发 -sun.forward；未接时为 0 → 退回主光方向）

            // ------------------------------------------------------------------
            // 程序化噪声（与 PirateSurface / PirateTerrain 内的副本一致，刻意重复以免多一条 include 链）
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

            // 2 阶 FBM（焦散专用：域扭曲要连采几次，用 2 阶控预算）。
            float PirateFbm2(float2 p)
            {
                return 0.65 * PirateValueNoise(p) + 0.35 * PirateValueNoise(p * 2.03 + float2(17.1, 9.7));
            }

            // 单层细节波法线：FBM 高度场在水平面上的梯度（现在只作高频细节，低频交给 Gerstner）。
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
            // Gerstner 叠加（与 Assets/Scripts/PirateCrew/Water/WaterRules.cs 同式；改一边必须改另一边）
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

            PirateWaveAccum PirateGerstner(float2 baseXZ, float t)
            {
                PirateWaveAccum acc;
                acc.horiz  = float2(0.0, 0.0);
                acc.height = 0.0;
                acc.Tx     = float3(1.0, 0.0, 0.0);
                acc.Tz     = float3(0.0, 0.0, 1.0);

                PirateAddWave(acc, _W1Dir.xz, _W1Length, _W1Amp, _W1Steep, _W1Speed, baseXZ, t);
                PirateAddWave(acc, _W2Dir.xz, _W2Length, _W2Amp, _W2Steep, _W2Speed, baseXZ, t);
                PirateAddWave(acc, _W3Dir.xz, _W3Length, _W3Amp, _W3Steep, _W3Speed, baseXZ, t);
                PirateAddWave(acc, _W4Dir.xz, _W4Length, _W4Amp, _W4Steep, _W4Speed, baseXZ, t);
                return acc;
            }

            // ------------------------------------------------------------------
            // 假焦散：域扭曲的双层噪声 → 脊状化得到细亮线
            // ------------------------------------------------------------------
            float PirateCaustic(float2 worldXZ, float t)
            {
                float2 q = worldXZ * _CausticScale;
                float2 warp = float2(
                    PirateFbm2(q + float2(0.0, t * _CausticSpeed)),
                    PirateFbm2(q + float2(5.2, 1.3) - float2(t * _CausticSpeed, 0.0)));
                float n = PirateFbm2(q * 1.7 + warp * _CausticWarp + float2(t * _CausticSpeed * 0.3, 0.0));
                n = 1.0 - abs(n * 2.0 - 1.0);   // 脊状化（把噪声谷变成亮线）
                return n * n;
            }

            // 泡沫"涌岸"脉冲形状（∈[0,1]，先亮后灭）：与 WaterRules.FoamPulse 同式。
            // 相位 = t·速度 − 水深·频率：水浅处相位提前 → 亮带由深向浅推进（读作"涌上岸再消散"）。
            float WaterFoamPulseShape(float waterDepth, float t)
            {
                float ph = t * _FoamPulseSpeed - waterDepth * _FoamPulseFrequency;
                float s = 0.5 + 0.5 * sin(ph);
                return s * s;
            }

            struct AttributesWater
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsWater
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                real   fogFactor  : TEXCOORD2;
                float2 baseXZ     : TEXCOORD3;   // 位移前的世界 XZ：片元用它重算解析法线
            };

            VaryingsWater WaterVertex(AttributesWater IN)
            {
                VaryingsWater OUT;

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);

                // Gerstner 顶点位移（世界空间；水是 Cube，y 缩放只有 0.1，对象空间位移会被吃掉 90%）。
                float3 positionWS = vpi.positionWS;
                float2 baseXZ = positionWS.xz;
                PirateWaveAccum acc = PirateGerstner(baseXZ, _Time.y);
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

            half4 WaterFragment(VaryingsWater IN) : SV_Target
            {
                float3 geometricNormalWS = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float  t = _Time.y;

                // ---- 解析法线：同一组 Gerstner 波的解析导数（与顶点位移同源）----
                PirateWaveAccum acc = PirateGerstner(IN.baseXZ, t);
                float3 nGerst = normalize(cross(acc.Tz, acc.Tx));
                float  jacobian = acc.Tx.x * acc.Tz.z - acc.Tz.x * acc.Tx.z;
                float  waveHeight = acc.height;

                // ---- 高频细节法线：FBM 梯度叠加在解析法线的水平分量上 ----
                float3 waveA = PirateWaterWaveNormal(IN.positionWS.xz, _WaveDirectionA.xz, _WaveScaleA, _WaveSpeedA, _WaveStrengthA, t);
                float3 waveB = PirateWaterWaveNormal(IN.positionWS.xz, _WaveDirectionB.xz, _WaveScaleB, _WaveSpeedB, _WaveStrengthB, t);
                float3 n = normalize(float3(nGerst.x + waveA.x + waveB.x, nGerst.y, nGerst.z + waveA.z + waveB.z));
                // 与几何法线混合，保证竖直侧壁不会被当成水平面（水是 Cube，有 0.1 高的侧壁）。
                n = normalize(lerp(geometricNormalWS, n, saturate(geometricNormalWS.y)));

                // ---- 场景深度 → 浅深水三档【r3 修问题 5】----
                float2 screenUV = IN.positionCS.xy * _ScreenParams.zw;
                float  rawDepth = SampleSceneDepth(screenUV);
                float  sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float  surfaceEye = -TransformWorldToView(IN.positionWS).z;
                float  waterDepth = clamp(sceneEye - surfaceEye, 0.0, 200.0);

                // 按场景设计 §5.1 的三档水深（浅滩 0~0.5 / 中水 0.5~2 / 深水 >2）重映射：
                // _ShoreFadeDistance 仍作"到深水的完成深度"（材质设 5，r4 的 4 → 5：同深度下更多面积
                // 停留在较亮的浅/中水色 = 降等效吸收系数，修 r4"水体 L50 仅 48"）；
                // 浅→中在 0.25·S（=1.25）处完成，中→深从 0.25·S 到 S（1.25→5.0）。
                // 旧写法把浅→中压到 2.0 才完成、中→深到 4.0 —— 与 5 级海床坡（0.18~2.9）对不上，
                // 大部分水域停在中水色、深水档几乎不出现，读成"一整块平色"（r2 诊断）。
                float shallowToMid = saturate(waterDepth / max(_ShoreFadeDistance * 0.25, 0.01));
                float midToDeep = saturate((waterDepth - _ShoreFadeDistance * 0.25)
                                           / max(_ShoreFadeDistance * 0.75, 0.01));
                half3 waterColor = lerp(_ShallowColor.rgb, _MidColor.rgb, (half)shallowToMid);
                waterColor = lerp(waterColor, _DeepColor.rgb, (half)midToDeep);

                // ---- 高度场模拟（全局；驱动缺席时 enabled=0，整段跳过）----
                float2 simUV = (IN.positionWS.xz - _WaterSimOrigin.xy) / max(_WaterSimOrigin.z, 1e-4) + 0.5;
                float  hfMask = 0.0;
                float2 hfNormal = float2(0.0, 0.0);
                float  hfFoam = 0.0;
                float  hfHeight = 0.0;
                float  hfObstacle = 0.0;
                if (_WaterSimEnabled > 0.5)
                {
                    // 域外不采样；边缘 4% 淡出，避免域边界骤然截断。
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

                        // 障碍：采本格 + 四邻（约 1.5 格膨胀）→ 得到"贴障碍的接触带"，
                        // 只有接触带才吃到 _ObstacleFoamBoost（否则自由格的障碍通道恒 0，泡沫加亮永不触发）。
                        float texel = 1.0 / max(_WaterSimOrigin.w, 1.0);
                        float o = SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, uv).r;
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv + float2(texel * 1.5, 0.0))).r);
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv - float2(texel * 1.5, 0.0))).r);
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv + float2(0.0, texel * 1.5))).r);
                        o = max(o, SAMPLE_TEXTURE2D(_WaterObstacleMap, sampler_WaterObstacleMap, saturate(uv - float2(0.0, texel * 1.5))).r);
                        hfObstacle = o;

                        // 高度场只扰法线，不做顶点位移（顶点形状由 Gerstner 负责，避免双重计高）。
                        n = normalize(n + float3(hfNormal.x, 0.0, hfNormal.y) * (_HeightFieldNormalStrength * hfMask));
                    }
                }

                // ---- 假焦散：叠进水体色（在菲涅尔/反射之下，读作"透水看到海床光斑"）----
                float caustic = PirateCaustic(IN.positionWS.xz, t);
                float causticFade = (1.0 - saturate(waterDepth / max(_CausticDepthFade, 0.01)));
                causticFade *= causticFade;
                waterColor += _CausticColor.rgb * (_CausticStrength * caustic * causticFade);

                // ---- 屏幕空间折射：按法线偏移 UV 采样不透明场景色，混进水体色 ----
                // 合成顺序：折射属于"水体内部"，必须在菲涅尔/反射（表面层）与泡沫（表面覆盖）之前。
                float refrMask = saturate(waterDepth / max(_RefractionDepthFade, 0.01));
                float edgeFade = 0.06;
                refrMask *= smoothstep(0.0, edgeFade, screenUV.x) * smoothstep(0.0, edgeFade, 1.0 - screenUV.x)
                          * smoothstep(0.0, edgeFade, screenUV.y) * smoothstep(0.0, edgeFade, 1.0 - screenUV.y);
                float2 refrUV = screenUV + n.xz * _RefractionStrength;
                half3 refracted = SampleSceneColor(refrUV);
                half3 body = lerp(waterColor, refracted, saturate((half)(_RefractionBlend * refrMask)));

                // ---- 岸边泡沫 ----
                float foamFalloff = 1.0 - saturate(waterDepth / max(_FoamWidth, 0.01));
                float foamNoise = PirateFbm(IN.positionWS.xz * _FoamNoiseScale + t * _FoamSpeed * float2(0.6, 0.4));
                float foamNoise2 = PirateFbm(IN.positionWS.xz * _FoamNoiseScale2 - t * _FoamSpeed * float2(0.35, 0.5));
                // 破碎：两层噪声之差 → 打破"一条均匀白带"。
                float breakup = saturate(0.5 + (foamNoise - foamNoise2) * _FoamBreakup * 2.0);
                // 涌岸：亮带随水深相位推进再灭（水浅处相位提前 → 由深向浅推进）。
                float pulse = WaterFoamPulseShape(waterDepth, t);
                float foamShore = foamFalloff * (0.45 + 0.55 * foamNoise * breakup)
                                 * lerp(1.0, pulse, _FoamPulseStrength);
                // 屏幕空间深度梯度：岛屿/海床轮廓处的泡沫线（乘 foamFalloff 排除"水-天空"远处轮廓）。
                float depthEdge = (abs(ddx(sceneEye)) + abs(ddy(sceneEye))) * _ShorelineFoamGain;
                float foamLine = saturate(depthEdge) * foamFalloff;
                half foam = saturate(foamShore * _FoamStrength + foamLine);
                // 高度场泡沫累积 + 障碍接触带加亮（浪撞到障碍时更白）。
                half hfFoamTerm = (half)(hfFoam * _HeightFieldFoamStrength * hfMask * (1.0 + hfObstacle * _ObstacleFoamBoost));
                foam = saturate(foam + hfFoamTerm);

                // ---- 调试档（每档预期画面见注释）----
                // 1 水色：alpha 拉满，预期能清楚看到"浅→中→深"三档色块与岛外台阶边界。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(body, 1.0h);
                // 2 波法线：蓝紫平滑起伏；整片纯色说明波参数为 0。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(half3(n * 0.5 + 0.5), 1.0h);
                // 3 菲涅尔灰度：正对相机处接近黑、掠射角（远处/边缘）接近白。
                float debugFresnel = pow(saturate(1.0h - (half)dot(n, viewDirWS)), _FresnelPower) * _FresnelStrength;
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return half4(half3(saturate(debugFresnel), saturate(debugFresnel), saturate(debugFresnel)), 1.0h);
                // 4 泡沫掩码：岛缘/海床台阶一圈亮白，向外随噪声散开；整片全白说明海床太浅。
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                    return half4(foam, foam, foam, 1.0h);
                // 5 波高（顶点位移可视化）：灰底=0，波峰亮、波谷暗；
                //   整片灰=振幅为 0；出现"越来越亮的同心块"=Gerstrner 在动。归一化用 Σ振幅。
                if (_DebugMode > 4.5 && _DebugMode < 5.5)
                {
                    float ampSum = _W1Amp + _W2Amp + _W3Amp + _W4Amp;
                    float g = saturate(0.5 + 0.5 * waveHeight / max(ampSum, 1e-4));
                    return half4(g, g, g, 1.0h);
                }
                // 6 波陡度：雅可比 J 的灰度（正常≈1 白）。出现暗斑/黑斑=水平压缩过度、有自交风险；
                //   红=J<0.9 预警。默认参数下应整片接近白（对应测试 IsFoldFree）。
                if (_DebugMode > 5.5 && _DebugMode < 6.5)
                {
                    float g = saturate(jacobian);
                    float warn = saturate((0.9 - jacobian) * 5.0);
                    return half4(saturate(g * 0.85 + warn), g * 0.9, g * 0.9, 1.0h);
                }
                // 7 高度场（模拟高度）：灰底=静水；有涟漪时能看到以注入点为中心的同心环。
                //   域外（hfMask=0）为蓝底；驱动未接时整片蓝（enabled=0）。
                if (_DebugMode > 6.5 && _DebugMode < 7.5)
                {
                    float g = saturate(0.5 + 0.5 * hfHeight);
                    return half4(lerp(half3(0.05, 0.1, 0.25), half3(g, g, g), (half)hfMask), 1.0h);
                }
                // 8 曲率（高度场 ∇²h）：用纹理 2 阶差分重构（不额外占通道）。波峰/波谷处明暗交替。
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
                // 9 泡沫累积：高度场泡沫（A 通道）灰度 + 障碍接触带泛红。
                if (_DebugMode > 8.5 && _DebugMode < 9.5)
                {
                    float g = saturate(hfFoam);
                    return half4(saturate(g + hfObstacle * hfMask), g * 0.9, g * 0.8, 1.0h);
                }
                // 10 障碍图：白=障碍（岛/礁，反射墙）及其约 1.5 格接触带（故意的膨胀，用于泡沫加亮）；
                //    黑=开阔水；域外蓝。岛应在域内呈一块实心白，海床台阶（水面之下）应为黑。
                if (_DebugMode > 9.5)
                {
                    return half4(lerp(half3(0.05, 0.1, 0.25), half3(hfObstacle, hfObstacle, hfObstacle), (half)hfMask), 1.0h);
                }

                // ---- 菲涅尔反射（表面层，压在水体/折射之上）----
                half fresnel = pow(saturate(1.0h - (half)dot(n, viewDirWS)), (half)_FresnelPower) * (half)_FresnelStrength;
                half3 reflectDirWS = reflect(-viewDirWS, n);
                half3 reflectionColor = SampleSH(reflectDirWS) * (half)_ReflectionStrength;
                half3 color = lerp(body, reflectionColor, saturate(fresnel));

                // ---- 主光镜面（风格化：直接取 GGX 镜面项，不乘 0.04 电介质 F0）----
                half alphaBRDF = 1.0h;
                BRDFData brdfData;
                InitializeBRDFData(waterColor, 0.0h, half3(0.0h, 0.0h, 0.0h), (half)_Smoothness, alphaBRDF, brdfData);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                half lightAtten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 specular = DirectBRDFSpecular(brdfData, n, mainLight.direction, viewDirWS)
                               * mainLight.color * lightAtten * (half)_SpecularIntensity;

                // 主光镜面同样染暖：中性白加色会把亮部拉向青白（r4 亮水 sat 0.24-0.29 的来源之一）。
                specular *= lerp(half3(1.0h, 1.0h, 1.0h), (half3)_SunSpecColor.rgb, 0.7h);
                color = color + specular;

                // ---- 太阳光路：宽瓣软带 + 窄瓣闪点 + 伪地平线暖带【r5】----
                // 诊断与符号约定见文件头【太阳光路（r5）】。_WaterSunDir = 从水面指向光源 L；未接驱动时退回主光。
                // 绝不能在这里再取负——r3 的 -_WaterSunDir 正是"画面里完全没有光路"的第一嫌疑。
                float3 sunToLight = dot(_WaterSunDir.xyz, _WaterSunDir.xyz) > 1e-4
                    ? normalize(_WaterSunDir.xyz) : normalize(mainLight.direction);
                float2 sunAz  = normalize(sunToLight.xz + float2(1e-5, 1e-5));
                float2 sunTan = float2(-sunAz.y, sunAz.x);

                // (1) 宽瓣主项（方向性光路）：平水面镜射 reflect(-V, up)。**不做斜率放大** → 没有绕波峰
                //     一圈的环形等值线（r4 空心霉斑的根因）。只对误差向量做各向异性整形：含太阳方位的
                //     面内分量（含竖直）按 _SunSpecLaneWidth 压缩 → 该方向容忍度更大，软斑被拉成
                //     "沿太阳方位的光路带"。
                float3 reflFlat = reflect(-viewDirWS, float3(0.0, 1.0, 0.0));
                float3 devVec   = reflFlat - sunToLight;
                float  laneW = max(_SunSpecLaneWidth, 1.0);
                float3 reflShaped = normalize(sunToLight
                    + float3(sunAz.x, 0.0, sunAz.y) * (dot(devVec, float3(sunAz.x, 0.0, sunAz.y)) / laneW)
                    + float3(sunTan.x, 0.0, sunTan.y) * dot(devVec, float3(sunTan.x, 0.0, sunTan.y))
                    + float3(0.0, 1.0, 0.0) * (devVec.y / laneW));
                float broadLane = pow(saturate(dot(reflShaped, sunToLight)), max(_SunSpecLaneShininess, 1.0));

                // (1b) 宽瓣辅项（任意机位保底）：放大 Gerstner 波法线去够镜面解。放大后法线在波峰周围
                //     转圈 → 直接 pow(dot) 会"一圈亮中间暗"；用**波峰绝对值偏置**把中心填实
                //     （波峰顶 = 法线朝上 = 原空心处，恰好 |波高| 最大）。
                float3 halfVec = normalize(sunToLight + viewDirWS);
                float3 nSpecBroad = normalize(float3(nGerst.x * _SunSpecSlopeBoost, nGerst.y,
                                                     nGerst.z * _SunSpecSlopeBoost));
                float ampSumWaves = _W1Amp + _W2Amp + _W3Amp + _W4Amp;
                float crest01 = saturate(abs(waveHeight) / max(ampSumWaves, 1e-4));
                float broadWave = saturate(pow(saturate(dot(nSpecBroad, halfVec)),
                                               max(_SunSpecBroadShininess, 1.0))
                                           + _SunSpecCrestBias * crest01);

                // (1c) 碎块掩码：世界高频噪声把连续亮带/亮环切成 15-30px 小块；只做 [1-depth,1] 调制（不挖洞）。
                float patchNoise = PirateFbm(IN.positionWS.xz * _SunSpecPatchScale + float2(t * 0.21, -t * 0.17));
                float patchMask = saturate((patchNoise - 0.45) * 2.2);
                float patchMod = lerp(1.0 - _SunSpecPatchDepth, 1.0, patchMask);
                broadLane *= patchMod;
                broadWave *= patchMod;

                // (2) 窄瓣：放大低频波法线 + 叠回高频细节 → 逐像素抖动，只有极少数像素命中镜面角
                //     （天然稀疏 = 波光闪点）。_SunSpecGlitter=0 时掩码关掉，退化为连续闪带。
                float3 nSpecFlick = normalize(float3(nGerst.x * _SunSpecSlopeBoost, nGerst.y,
                                                     nGerst.z * _SunSpecSlopeBoost)
                    + float3(waveA.x + waveB.x, 0.0, waveA.z + waveB.z) * _SunSpecGlitter);
                float3 reflFlick = reflect(-viewDirWS, nSpecFlick);
                float flickTerm = pow(saturate(dot(reflFlick, sunToLight)), max(_SunSpecShininess, 1.0));

                // 闪点稀疏掩码：噪声峰才放行（与宽瓣用同族不同相位的噪声，块位置错开）。
                float sparkleNoise = PirateFbm(IN.positionWS.xz * (_SunSpecPatchScale * 1.7) + float2(t * 0.7, -t * 0.5));
                float flickMask = saturate((sparkleNoise - 0.62) * 6.0);
                flickMask = lerp(1.0, flickMask, step(0.001, _SunSpecGlitter));

                // (3) 伪地平线暖带：低角机位平水面镜射无解（要 ~42° 液面倾角），
                //     改判"视线方位是否落在太阳方位 ±_SunSpecAzBandDeg" + 掠射权重 → 远处暖带。
                float2 lookAz  = -viewDirWS.xz;
                float  azAlign = (dot(lookAz, lookAz) > 1e-6) ? dot(normalize(lookAz), sunAz) : -1.0;
                float  azBand  = smoothstep(cos(radians(_SunSpecAzBandDeg)), 1.0, azAlign);
                float  horizon = azBand * pow(saturate(1.0 - viewDirWS.y), 2.5) * _SunSpecAzBandStrength;

                // 权重（0-1）：宽瓣 = 主项(光路带) + 辅项(任意机位暖波光)；窄瓣 1-exp 软饱和
                // （避免 _SunSpecStrength=8 把权重顶满）；再加掠射暖光泽与伪地平线暖带。
                half broadWeight = saturate((half)(broadLane * _SunSpecBroadStrength
                                                   + broadWave * _SunSpecWaveStrength));
                half flickWeight = (1.0h - exp(-(half)(flickTerm * _SunSpecStrength))) * (half)flickMask;
                half sheen = (half)(pow(saturate(1.0 - viewDirWS.y), 3.0) * _SunSheenStrength);
                half sunPathWeight = saturate(broadWeight + flickWeight + sheen + (half)horizon);

                // (4) 按权重把水体色混向太阳色：饱和处即暖金色相（hue≈45）。加法会被水体蓝底
                //     （_MidColor B≈0.6 线性）拉成青白——那是 r4"最亮像素是薄荷绿"的成因，故必须混色。
                color = lerp(color, (half3)_SunSpecColor.rgb, sunPathWeight);
                color = lerp(color, _FoamColor.rgb, foam);
                color = MixFog(color, IN.fogFactor);

                // 不透明度：基础值 + 菲涅尔（掠射更实）+ 泡沫更实。
                half alpha = saturate((half)_Opacity + saturate(fresnel) * 0.35h + foam * 0.3h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
