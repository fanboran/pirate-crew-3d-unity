// ============================================================================
// PirateSurface.shader —— 海盗军团夺宝 3D / 通用「风格化写实 → 程序化 PBR」表面
//
// 【本工程为什么需要它】全工程 0 贴图（不引入来源不明的外部贴图），8 个旧 .mat 全是
//   URP/Lit 纯色。URP/Lit 无贴图时几乎无表面变化 → 画面像"灰模上了色"。
//   本 shader 用**程序化噪声**在片元里生成 albedo 变化 / 粗糙度分区 / 细节法线扰动，
//   从而在没有一张贴图的前提下拿到 PBR 材质的"材质感"。
//
// 【风格契约】GDD §10.4「风格化写实（Stylized PBR）」：
//   高饱和块面 + URP Lit 级 PBR + 保留 inverted-hull 描边（单位描边见 PirateOutline.shader）。
//   因此本 shader 的 PBR 走 URP 官方 BRDF（BRDF.hlsl / Lighting.hlsl），
//   而不是自写的 Lambert —— 这样与 URP/Lit 的地面/其他物体光照口径一致。
//
// 【三档色阶的出处】GDD §10.4 调色板给每种材质三档色（如沙地 #E8D5A3 → #C4A76A → #8B7355）。
//   本 shader 用 _BaseColorA/B/C 三个槽承载这三档，由 FBM 噪声的取值选档，
//   于是"三档色阶"在空间上自然分布，而不是整块纯色。
//
// 【沙滩湿区 / 潮痕线 / 沙纹（2026-09-13 新增，借"沙堡类"观感原理，纯 shader 零贴图）】
//   目的：让"干沙/湿沙"由**世界高度（离水位多高）**驱动，而不是靠摆两个材质。
//   1) 湿掩码（唯一一条湿链路，与既有 _Wetness 合并）：
//        dy      = worldY - _WaterLevelY
//        wetMask = 1 - saturate(dy / _WetBandWidth)
//        wet     = saturate(max(_Wetness, wetMask))     // 取 max 不是相加，理由见片元注释
//      颜色合成：作用在**三档色阶 + 噪声之后的最终 albedo**上（理由见片元注释）：
//        albedoWet = lerp(albedo, _WetSandColor.rgb, wet)           // 向湿沙色靠拢
//        albedoWet *= lerp(1, 1-_WetDarken, wet)                    // 湿区额外压暗（复用既有 _WetDarken）
//        albedoWet *= lerp(1, 1-_WetLineDarkening, wetLine)         // 潮痕线再压一档
//      光滑度：smoothness = _Smoothness + wet * _WetSmoothnessBoost（湿沙更滑 → 高光反射是"湿"的关键信号）。
//   2) 高水位残留线（潮痕）：dy 落在 [_WetLineMin, _WetLineMax]（默认 0.10~0.22）的窄带里再压暗一档，
//      smoothstep 两端各一次（软边 = 带宽的 1/4）→ 退潮留下的湿痕。
//   3) 沙纹：仅湿掩码内生效，解析正弦 + FBM 相位扭曲，调制**法线与轻微明度**：
//        h = sin(worldXZ·_RippleScale·dir + FBM·_RippleDistort·2π)，波长 λ = 2π/_RippleScale
//        振幅 = min(_RippleStrength, 0.15) × wet —— 上限 0.15 是硬钳，避免毁掉低模块面辨识度。
//   【提案/待定】湿沙色 #A88D5C、残留线 0.10~0.22、_RippleScale=16（λ≈0.39 单位）均为 AI 提案：
//     用量化的观感原理（湿=更暗/更滑、退潮留线、沙面有纹）落地，未抄任何商业游戏实现；
//     _WetSandColor 是"湿态 albedo 目标色"，非沙材质（岩/木/金属）若恰好落入湿带，应由材质覆写该色，
//     否则会被染成沙色（见报告"建议 BattleSceneLighting 显式值"与遗留项）。
//
// 【细节噪声贴图（2026-09-13 r3 复验 N4 返工，程序化资产，仍属"0 外部贴图"）】
//   【为什么加】r3 复验 N4（高）：地形/沙地是**纯色平面**——同一可见面 90px 内只有 7 种颜色、
//     单面 RGB 恒定 #E9C77E。片元里的 FBM 只给低频斑块，压不出像素级纹理能量：
//       · 判据 P-9（美术品控评审规程.md:213）：同材质 200×200 窗灰度 std > 6 —— 不达标；
//       · 判据 P-10（同文 :214）：高频能量需显著高于纯色基线 —— 不达标；
//       · 判据 §3.2 纪律 1（美术风格指南.md:199-204）：相邻面 smoothness 差 ≥ 0.15。
//   【做法】构建期用 CPU 噪声生成 **512²** 贴图（v2 提档前为 256²；算法唯一来源 =
//     Assets/Editor/MaterialNoiseBuilder.cs，v2 用「两轮低频域名扭曲 fBm」替代纯 value-FBM），
//     存 Assets/Art/Textures/Materials/，材质引用之。这是**自产程序化资产**，
//     与 FxAssetBuilder（特效贴图）/ AudioAssetBuilder（wav 导入）同一管线哲学，不是外部贴图。
//   【两张图的口径（踩不对就整体变色）】
//     · _DetailNoiseMap：**均值保持的乘性微色斑图**。像素 = 线性域系数（均值恰 0.5）的 sRGB 编码；
//       采样后 *2 → 均值恰为 1.0 → **不改材质已调好的平均色**（GDD 三档色调色板零漂移），只加起伏。
//       albedo 的导入设置是 sRGB=true，所以贴图里存 sRGB 编码才会被硬件解码回我们算的线性系数。
//     · _BumpMap：切空间法线 n*0.5+0.5、**A=255**（桌面 core Packing.hlsl:214 UnpackNormalmapRGorAG
//       会做 packed.x *= packed.w，A=1 时 RGBA 布局才成立）；导入为 NormalMap 类型 + 线性（sRGB=false）。
//   【世界空间 XZ UV，不用模型 UV】UV = worldXZ * _NoiseWorldScale。
//     【v2 贴图提档后本 shader 无需改动】贴图从 256²→512²、内容从纯 value-FBM→域名扭曲 fBm，
//     采样口径（世界尺度、均值保持、法线重定向）**全都不变**：细粒变清楚是贴图端的事，
//     mip 会自动为更细的贴图选择更粗的 mip（导入侧 mipmap=true + aniso 8）。
//     【r5 采样尺度：0.4 → 0.05（世界尺度放大 8×，一张 512² 铺 20m）】原 0.4 时贴图最细八度
//     （周期 192）的世界波长只有 ~1.3cm，近景被 mip 平均成平色、unit-closeup 的 120×120 窗 std
//     只有 2-3；把 UV 尺度降下来后最细八度的世界波长抬到 ~10cm，落进近景可分辨 band，
//     特写才看得到砂粒/色斑起伏。远看不会闪成"噪点蚂蚁纹"：贴图导入开 mipmap + aniso=4，
//     最细八度远距离自然收敛到均值；周期 4/16 的中低频八度兜底大尺度色斑（不靠高频撑着）。
//     理由：地形/地面是 Cube 图元，**模型 UV 与 1 单位格子边界对齐**，
//     正是"顶面格缝读成地砖/编织布"的来源；世界空间 UV 让贴图跨块连续。
//     再叠一层低频 FBM 扭曲（_NoiseWarpStrength）把任何残留的轴向对齐彻底打断。
//     【已知边界】世界 XZ 投影对水平面最准确；竖直面（法线 ⟂ XZ）的 V 方向退化 → 细节被拉成横纹。
//     与既有"细节法线只用 XZ 梯度"是同一近似（见上方【已知边界】2），风格化写实下可接受。
//   【默认关闭】_DetailAlbedoStrength / _BumpScale 的 shader 默认值都是 **0**，
//     即"没赋贴图的材质（木/布/铜/铁）行为与改动前完全一致"，避免依赖 2D 属性的默认回退贴图
//     （未赋值的 2D 属性会用内置贴图，"gray" 在 Linear 下解码成 0.214，乘 2 会把 albedo 压暗一半）。
//     沙/草/岩/地形材质的实际取值见 BattleSceneLighting.ApplyDetailTexture。
//
// 【色空间注意】本工程 ProjectSettings m_ActiveColorSpace = 1（**Linear**，PBR 物理正确的前提）。
//   sRGB 十六进制直接归一化赋值即可与 GDD 色板在屏幕上一致（引擎对普通 Color 材质属性自动做
//   sRGB→Linear，见 BattleSceneLighting 类头 :26-36 的证据链；材质生成脚本注释里也标注了）。
//   【细节贴图的换算必须自己算】_DetailNoiseMap 是"线性系数 × sRGB 编码"的自产数据，
//   与普通颜色属性的自动转换无关：生成端负责 SrgbToLinear/LinearToSrgb 往返一致（见 MaterialNoiseBuilder）。
//
// 【已知边界（提案/待定）】
//   1. 本 Pass 只取**主方向光**（含阴影）+ SH 环境光 + 雾，不支持点光/聚光。
//      理由：本工程场景只有一盏方向光（PirateOutline/M2BattleSceneSetup 同款），
//      加 additional lights 会为每个光源变体翻倍编译量而当前无收益。
//      将来加火把/灯笼时，按 URP/Lit 的 `#pragma multi_compile _ _ADDITIONAL_LIGHTS` 段补即可。
//   2. 细节法线是"世界 XZ 平面噪声梯度"，对水平面最准确、对竖直面是近似（风格化可接受）。
//      如需更强的法线，应改为三平面投影（triplanar）采样程序化高度场。
//   3. 无 _ALPHATEST_ON / 透明分支：本 shader 只服务不透明材质（沙/草/岩/木/金属/布）。
//
// 【Pass 与 LightMode 唯一性（URP 陷阱，见 docs/描边Shader调试.md §八-2）】
//   ForwardLit   = "UniversalForward"
//   ShadowCaster = "ShadowCaster"
//   DepthOnly    = "DepthOnly"
//   三者互不相同；额外 Pass 若与既有 Pass 撞 LightMode 会被 **静默丢弃**（Console 无报错）。
//
// 【_DebugMode 分档】图形学调试截图规范要求 shader 内含 debug_mode 拆分管线步骤。
//   各档"预期画面"写在片元对应分支处；实际截图需在有渲染路径的编辑器里补跑。
//   档 7 = 细节贴图（本次新增）：中灰 0.5 = 乘性系数 1.0（无起伏），判据"200×200 窗 std > 0.02"。
//
// 【[CHECK] 本地 URP 14.0.12 包内逐条核对（符号存在 ≠ include 自洽，见 §八-1）】
//   Core.hlsl                          -> ShaderLibrary/Core.hlsl（含 API 的 TEXTURE2D_X_* 等定义）
//   Lighting.hlsl                      -> ShaderLibrary/Lighting.hlsl:4-9 引入 BRDF/Debugging3D/
//                                         GlobalIllumination/RealtimeLights/AmbientOcclusion/DBuffer
//   Shadows.hlsl                       -> ShaderLibrary/Shadows.hlsl（显式 include，见下）
//   InitializeBRDFData                 -> BRDF.hlsl:86（经 Lighting.hlsl 引入）
//   LightingPhysicallyBased            -> Lighting.hlsl:81（内部乘 light.shadowAttenuation，见 :75）
//   GlobalIllumination(brdfData,gi,occ,n,v) -> GlobalIllumination.hlsl:431
//   SampleSH                           -> GlobalIllumination.hlsl:21
//   TransformWorldToShadowCoord        -> Shadows.hlsl:319
//   ApplyShadowBias                    -> Shadows.hlsl:471（ShadowCasterPass 用）
//   GetMainLight(float4)               -> RealtimeLights.hlsl:118（经 Lighting.hlsl 引入）
//   ComputeFogFactor / MixFog          -> ShaderVariablesFunctions.hlsl:329 / :414
//   GetWorldSpaceNormalizeViewDir      -> ShaderVariablesFunctions.hlsl:125
//   ShadowCasterPass.hlsl              -> Shaders/ShadowCasterPass.hlsl（官方阴影 Pass，自带 Core+Shadows）
//   CommonMaterial.hlsl（core）        -> :352/359 定义 LerpWhiteTo；URP Shadows.hlsl:298 用它却**不**自己
//                                         include 它 —— ShadowCaster Pass 里必须先 include 它，
//                                         实测否则报 `undeclared identifier 'LerpWhiteTo'`（d3d11）。
//   TEXTURE2D / SAMPLER / SAMPLE_TEXTURE2D -> core Common.hlsl（经 URP Core.hlsl:16 引入）的纹理宏，
//                                         URP/Lit 同款写法（Lit.shader:26 _BumpMap + LitInput.hlsl 的
//                                         TEXTURE2D(_BaseMap)/SAMPLER(sampler_BaseMap) 分离声明）。
//   UnpackNormalScale                 -> core Packing.hlsl:220（经 URP Core.hlsl:17 引入，**无需**额外
//                                         include；本 Pass 的 include 链不变）。
//                                         :214 UnpackNormalmapRGorAG 读 w 通道 → 我们的法线图 A=255。
//   **本清单只能证明符号存在**；必须进图形界面编辑器 play 一次 + read_console 确认 0 shader error。
// ============================================================================

Shader "PirateCrew/PirateSurface"
{
    Properties
    {
        // ---- 三档色阶（GDD §10.4：A=暗档、B=中档、C=亮档）----
        _BaseColorA             ("色阶 A（暗档）", Color) = (0.545, 0.451, 0.333, 1.0)
        _BaseColorB             ("色阶 B（中档）", Color) = (0.769, 0.655, 0.416, 1.0)
        _BaseColorC             ("色阶 C（亮档）", Color) = (0.910, 0.835, 0.639, 1.0)
        _ColorRampContrast      ("色阶对比（越大越分明）", Range(0.1, 4.0)) = 1.4
        _ColorRampBias          ("色阶分布偏移（0.5=居中）", Range(0.0, 1.0)) = 0.5

        // ---- 程序化噪声 ----
        _NoiseScale             ("主体噪声尺度（1/世界单位）", Float) = 3.5
        _NoiseStrength          ("主体噪声明暗强度", Range(0.0, 1.0)) = 0.35
        // 噪声在 XZ 上的拉伸：(1,1)=各向同性；(0.2,1)=沿 Z 拉长成条纹 —— 木纹/岩层用。
        _NoiseStretch           ("噪声拉伸 XY（木纹用，1=不拉伸）", Vector) = (1.0, 1.0, 0.0, 0.0)
        _DetailNoiseScale       ("细节噪声尺度", Float) = 14.0
        _DetailNormalStrength   ("细节法线强度", Range(0.0, 2.0)) = 0.35

        // ---- 细节噪声贴图（程序化资产；算法见 Assets/Editor/MaterialNoiseBuilder.cs）----
        // 【默认值 0 是刻意设计】没赋贴图的材质必须与"加贴图之前"逐像素一致；
        // 且 2D 属性未赋值时 Unity 会用内置回退贴图，其线性值不可预期（见文件头【默认关闭】）。
        // [NoScaleOffset]：UV 由世界 XZ×_NoiseWorldScale 驱动，材质上的 Tiling/Offset 无意义，隐藏掉。
        [NoScaleOffset] _DetailNoiseMap ("细节图（albedo 微色斑；线性均值 0.5 的乘性图）", 2D) = "gray" {}
        // r5：默认 0.4 → 0.05（世界尺度放大 8×，一张 512² 铺 20m）；材质实际值见 BattleSceneLighting.ApplyDetailTexture。
        _NoiseWorldScale        ("细节图世界尺度（UV=世界XZ×该值；1/该值=平铺米数）", Float) = 0.05
        _NoiseWarpStrength      ("细节 UV 扭曲（打断与格子的轴向对齐）", Range(0.0, 0.5)) = 0.18
        _DetailAlbedoStrength   ("细节 albedo 强度（0=关闭；未赋贴图的材质保持 0）", Range(0.0, 1.0)) = 0.0
        [NoScaleOffset] _BumpMap ("细节法线图（切空间；重定向到世界 XZ 基）", 2D) = "bump" {}
        _BumpScale              ("细节法线强度（0=关闭；未赋贴图的材质保持 0）", Range(0.0, 2.0)) = 0.0

        // ---- PBR ----
        _Metallic               ("金属度", Range(0.0, 1.0)) = 0.0
        _Smoothness             ("光滑度", Range(0.0, 1.0)) = 0.2
        _AmbientStrength        ("环境光（SH）强度", Range(0.0, 2.0)) = 1.0

        // ---- 边缘磨损 / 湿润（可选）----
        _EdgeWear               ("边缘磨损强度", Range(0.0, 1.0)) = 0.0
        _WearColor              ("磨损色", Color) = (0.85, 0.80, 0.70, 1.0)
        _Wetness                ("湿润度（材质手工，与潮位湿掩码取 max）", Range(0.0, 1.0)) = 0.0
        _WetSmoothnessBoost     ("湿润光滑度增益（湿沙更滑）", Range(0.0, 1.0)) = 0.25
        _WetDarken              ("湿润变暗强度（湿区额外压暗 0-1）", Range(0.0, 1.0)) = 0.3

        // ---- 沙滩湿区（由世界高度/水位驱动；与上方 _Wetness 合并成一条链路）----
        // 湿沙色为「湿态 albedo 目标色」，沙地取暗档 #8B7355 与中档 #C4A76A 之间压暗的 #A88D5C（【AI 提案】）。
        _WetSandColor           ("湿沙色（湿态 albedo 目标 #A88D5C）", Color) = (0.659, 0.553, 0.361, 1.0)
        _WaterLevelY            ("水位世界 Y（与 PirateWater/LevelGeometry 一致）", Float) = -0.2
        _WetBandWidth           ("湿沙带宽度（世界单位，水线以上过渡到全干）", Float) = 0.45
        // 高水位残留线：水位之上 [_WetLineMin, _WetLineMax] 的窄带再压暗一档（潮痕）【AI 提案】
        _WetLineMin             ("残留线相对水位下限", Float) = 0.10
        _WetLineMax             ("残留线相对水位上限", Float) = 0.22
        _WetLineDarkening       ("残留线压暗强度", Range(0.0, 1.0)) = 0.25

        // ---- 沙纹（仅在湿掩码内生效；纯视觉，不参与任何玩法判定）【AI 提案】----
        _RippleScale            ("沙纹频率（弧度/世界单位，波长=2π/该值）", Float) = 16.0
        _RippleStrength         ("沙纹强度（硬钳 ≤0.15，保低模辨识度）", Range(0.0, 0.15)) = 0.10
        _RippleDistort          ("沙纹相位扭曲（低频噪声，0=绝对平行）", Range(0.0, 1.0)) = 0.35

        // ---- 调试（AGENTS.md 图形学调试截图规范）----
        // 0 正常 / 1 只 albedo / 2 噪声 / 3 法线 / 4 光滑度·金属度 / 5 湿掩码 / 6 沙纹 / 7 细节贴图
        // 既有 0-4 档语义不变；5、6 为沙滩湿区那轮新增；7 为细节贴图那轮新增（语义见片元分支注释）。
        _DebugMode              ("调试模式 0=正常 1=albedo 2=噪声 3=法线 4=光滑度金属度 5=湿掩码 6=沙纹 7=细节贴图", Range(0.0, 7.0)) = 0.0
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
        // Pass 1 / ForwardLit：主光（含阴影）+ SH 环境光 + 雾
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
            #pragma vertex   SurfaceVertex
            #pragma fragment SurfaceFragment

            // 主光阴影：三档（无 / 单 cascade / 多 cascade）。
            // _SHADOWS_SOFT 由 URP Asset 的软阴影开关驱动（见 BattleSceneLighting.ConfigureUrpAsset）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // 雾：线性雾（RenderSettings.fogMode = Linear，见 BattleSceneLighting.ApplySceneAtmosphere）。
            #pragma multi_compile_fog

            // Core.hlsl 提供顶点/空间变换与 _Time；Lighting.hlsl 是 URP 的标准光照入口，
            // 它按顺序引入 BRDF / GlobalIllumination / RealtimeLights / AmbientOcclusion，
            // 保证 BRDFData 这类"被引用但自身不 include"的符号有定义
            // （事故复盘见 docs/描边Shader调试.md §八-1：只 include GlobalIllumination.hlsl 会编译失败）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 显式声明对 Shadows.hlsl 的依赖（TransformWorldToShadowCoord:319 / ApplyShadowBias:471）。
            // 【必须在 Lighting.hlsl 之后，否则报错】Shadows.hlsl:298 用 LerpWhiteTo，
            //   而该符号来自 core 的 CommonMaterial.hlsl —— Shadows.hlsl 自身**不** include 它。
            //   Lighting.hlsl 内部顺序恰好自洽：第 4 行先 BRDF.hlsl（→ CommonMaterial.hlsl），
            //   第 7 行再 RealtimeLights.hlsl（其第 7 行 include Shadows.hlsl）。
            //   把 Shadows.hlsl 提到 Lighting.hlsl 之前就会复现
            //   `undeclared identifier 'LerpWhiteTo'`（2026-09-13 实测 d3d11 报错，见 §八-1 同类事故）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // CBUFFER 字段顺序与 Properties 声明顺序一致（本 Pass 是唯一采样材质属性的 Pass）。
            // 【SRP Batcher 提醒（提案/待定）】Unity 的 SRP Batcher 兼容性要求 shader 的**每个** Pass
            // 都声明同一份 UnityPerMaterial；本 shader 的 ShadowCaster / DepthOnly 没有声明
            // （它们的函数不读材质属性，声明只是为了让 SRP Batcher 认可布局，属纯性能优化）。
            // 本工程场景只有十余个渲染器，未开 SRP Batcher 的代价可忽略；若将来批量实例化同类物体，
            // 再给这两个 Pass 补上同一份 CBUFFER 即可。注意 URP 官方 Lit 是通过在各 Pass 都 include
            // LitInput.hlsl 来满足这一点的。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColorA;
                float4 _BaseColorB;
                float4 _BaseColorC;
                float  _ColorRampContrast;
                float  _ColorRampBias;
                float  _NoiseScale;
                float  _NoiseStrength;
                float4 _NoiseStretch;
                float  _DetailNoiseScale;
                float  _DetailNormalStrength;
                float  _NoiseWorldScale;
                float  _NoiseWarpStrength;
                float  _DetailAlbedoStrength;
                float  _BumpScale;
                float  _Metallic;
                float  _Smoothness;
                float  _AmbientStrength;
                float  _EdgeWear;
                float4 _WearColor;
                float  _Wetness;
                float  _WetSmoothnessBoost;
                float  _WetDarken;
                float4 _WetSandColor;
                float  _WaterLevelY;
                float  _WetBandWidth;
                float  _WetLineMin;
                float  _WetLineMax;
                float  _WetLineDarkening;
                float  _RippleScale;
                float  _RippleStrength;
                float  _RippleDistort;
                float  _DebugMode;
            CBUFFER_END

            // 细节贴图：**纹理与采样器必须在 UnityPerMaterial CBUFFER 之外**（URP 硬要求，
            //   见 LitInput.hlsl：TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); 与 CBUFFER 分离声明）。
            //   放进 CBUFFER 会破坏 SRP Batcher 布局并报错。
            // 【名字撞车风险（已核对，别随手加 include）】URP 的 SurfaceInput.hlsl:13-14 也在全局声明
            //   TEXTURE2D(_BumpMap)/SAMPLER(sampler_BumpMap)、LitInput.hlsl:24 声明 half _BumpScale。
            //   本 shader 的 include 链（Core→Lighting→Shadows）**不**含这两个头（SurfaceInput.hlsl 只被
            //   LitInput/SimpleLitInput/BakedLitInput/UnlitInput/Particles.hlsl 引入），故不冲突；
            //   将来若给本 shader 加 LitInput.hlsl 之类，必须先删掉本地这两行声明。
            TEXTURE2D(_DetailNoiseMap); SAMPLER(sampler_DetailNoiseMap);
            TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);

            // ------------------------------------------------------------------
            // 程序化噪声（本工程不引入外部贴图，全部在 shader 里生成；与 PirateWater /
            // PirateTerrain 内的同名实现是刻意重复的纯函数副本 —— 避免再引入一个 .hlsl
            // include 链，多一条 include 就多一条"静态核对全绿、真实编译才炸"的风险面）。
            // ------------------------------------------------------------------

            // 整数哈希：把 2D 坐标散列到 [0,1)。用 frac 大乘法而非 sin(大数)，
            // 避免不同 GPU 上 sin 精度差异导致噪声图案不稳定。
            float PirateHash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 值噪声：双线性插值 + smoothstep 缓和。
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

            // 4 阶 FBM。返回 [0,1]。
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

            // 细节法线：用 FBM 高度场在世界 XZ 上的有限差分近似梯度，扰动几何法线。
            // _DebugMode 3 可单独查看扰动后的法线。
            float3 PirateDetailNormal(float3 positionWS, float3 normalWS, float scale, float strength)
            {
                float2 p  = positionWS.xz * scale;
                float  e  = 0.15;
                float  h  = PirateFbm(p);
                float  hx = PirateFbm(p + float2(e, 0.0));
                float  hy = PirateFbm(p + float2(0.0, e));

                float3 bump = float3(-(hx - h) / e, 0.0, -(hy - h) / e) * strength;
                if (strength <= 1e-4)
                    return normalWS;

                // 只在切平面内扰动：把 bump 投影到与几何法线正交的平面，避免竖直面上法线被拉歪。
                bump -= normalWS * dot(bump, normalWS);
                return normalize(normalWS + bump);
            }

            struct AttributesSurface
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsSurface
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                real   fogFactor  : TEXCOORD2;
            };

            VaryingsSurface SurfaceVertex(AttributesSurface IN)
            {
                VaryingsSurface OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = GetVertexNormalInputs(IN.normalOS).normalWS;
                // 雾因子在顶点算一次（URP 标准做法），片元用 MixFog 混。
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 SurfaceFragment(VaryingsSurface IN) : SV_Target
            {
                float3 normalWS  = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ---- 主体噪声 → 三档色阶 ----
                // _NoiseStretch 把噪声在水平面上拉伸（木纹用）；竖直面用 y 参与坐标，避免整面同色。
                float2 noiseUV = IN.positionWS.xz * _NoiseScale * _NoiseStretch.xy
                               + IN.positionWS.y * (_NoiseScale * 0.37);
                float  n  = PirateFbm(noiseUV);
                half   nh = (half)n;

                // t 把噪声 [0,1] 重映射到色阶选择，再由两段 lerp 选出 A/B/C 三档。
                half t = saturate((nh - 0.5h) * (half)_ColorRampContrast + (half)_ColorRampBias);
                half3 albedo = lerp(_BaseColorA.rgb, _BaseColorB.rgb, saturate(t * 2.0h));
                albedo = lerp(albedo, _BaseColorC.rgb, saturate(t * 2.0h - 1.0h));
                // 噪声本身再叠一层轻微明暗（避免三档边界过于"贴纸感"）。
                albedo *= 1.0h - (half)_NoiseStrength * 0.35h * (1.0h - nh);

                // ---- 细节贴图（世界 XZ UV + 低频扭曲；程序化资产，见文件头）----
                // 【判据】同一可见面 200×200 窗灰度 std > 6（P-9）、窗内高频能量显著高于纯色基线（P-10）。
                // 【为什么 UV 用世界 XZ 而不是模型 UV】模型 UV 与 1 单位格子边界对齐 → 正是"地砖感"来源；
                //   世界空间 UV 让贴图跨块连续，再加一层低频 FBM 扭曲打断任何残留的轴向对齐。
                // 【扭曲的坐标尺度用 0.37（约 2.7 世界单位一个起伏）】远低于贴图频率，
                //   只挪动 UV 不破坏贴图自身的 mip 选择（扭曲本身是低频，导数贡献可忽略）。
                float2 detailUV = IN.positionWS.xz * _NoiseWorldScale;
                float2 detailWarp = float2(PirateFbm(detailUV * 0.37 + 3.1),
                                           PirateFbm(detailUV * 0.37 + 19.7)) - 0.5;
                detailUV += detailWarp * (_NoiseWarpStrength * 2.0);

                half3 detailAlb = SAMPLE_TEXTURE2D(_DetailNoiseMap, sampler_DetailNoiseMap, detailUV).rgb;
                half4 detailNrm = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, detailUV);
                // 乘性微色斑：贴图线性均值恰 0.5 → *2 后均值恰 1.0 → **不改平均色**，只加起伏。
                //   _DetailAlbedoStrength 默认 0 → 未赋贴图的材质（木/铜/铁/布）与改动前逐像素一致。
                //   作用点在"三档色阶之后、湿合成之前"：与湿是两条独立链路，顺序不影响湿的语义。
                albedo *= lerp(half3(1.0h, 1.0h, 1.0h), detailAlb * 2.0h, (half)_DetailAlbedoStrength);
                // 调试档 7 用：细节图"乘性系数"的线性明度（均值≈1.0，>1 偏亮斑、<1 偏暗斑）。
                half detailLuma = dot(detailAlb * 2.0h, half3(0.2126h, 0.7152h, 0.0722h));
                // 细节法线：切空间 (x,y) → 世界 XZ 基（低成本 worldspace 重定向，不引 tangent 帧）。
                //   只取 xy、丢掉 z：未赋贴图时默认 "bump" 贴图的 xy 未必恰好为 0，
                //   而 z=1 的"平法线"在这里贡献本来就是 0 —— 加上 _BumpScale 默认 0，双重保险。
                //   近似性：竖直面上 XZ 基退化（V 方向被拉长），与上方"细节法线只用 XZ 梯度"同一近似。
                float2 nTS = UnpackNormalScale(detailNrm, 1.0h).xy;
                float3 bumpTex = float3(nTS.x, 0.0, nTS.y) * _BumpScale;
                bumpTex -= normalWS * dot(bumpTex, normalWS);   // 投影到切平面，避免竖直面被拉歪
                float3 nrm = normalize(PirateDetailNormal(IN.positionWS, normalWS, _DetailNoiseScale, _DetailNormalStrength)
                                     + bumpTex);

                // ---- 湿掩码（**唯一一条**湿链路：材质湿润度 _Wetness ∪ 世界高度潮位）----
                // 位置湿掩码：水下 / 水线附近 = 1，高于「水位 + 带宽」= 0。
                //   wetMask = 1 - saturate((worldY - _WaterLevelY) / _WetBandWidth)
                // 默认 _WaterLevelY = -0.2（与 PirateWater.shader:20 / LevelGeometry 一致）、
                // _WetBandWidth = 0.45 → 水线 y=-0.2 到 y=+0.25 之间线性从全湿过渡到全干。
                // 与 _Wetness 的合并用 max 而非相加：
                //   _Wetness 是材质手工湿润度（物体级），wetMask 是潮位几何（位置级），二者是**独立的两个湿来源**；
                //   相加会在两者同时满足处叠出 >1 的压暗与过亮高光（两套湿逻辑打架），
                //   max = "谁更湿听谁的"，既不互相覆盖也不重复计费。
                float dy = IN.positionWS.y - _WaterLevelY;
                half wetMask = saturate(1.0h - (half)(dy / max(_WetBandWidth, 1e-4)));
                half wet = saturate(max((half)_Wetness, wetMask));

                // ---- 高水位残留线（潮痕）----
                // 在「水位 + [_WetLineMin, _WetLineMax]」（默认 0.10~0.22）的窄带里再压暗一档。
                // 两端各一次 smoothstep（软边 = 带宽的 1/4）→ 上下沿柔和，不是硬切色带。
                // 默认带落在湿带内部靠上沿（湿掩码约 0.51~0.78 处）→ 叠出一道比周围更深的窄痕。
                float lineSoft = max((_WetLineMax - _WetLineMin) * 0.25, 0.005);
                half wetLine = (half)(smoothstep(_WetLineMin - lineSoft, _WetLineMin + lineSoft, dy)
                                   * (1.0 - smoothstep(_WetLineMax - lineSoft, _WetLineMax + lineSoft, dy)));

                // ---- 湿润合成：作用在**三档色阶 + 噪声之后的最终 albedo** 上 ----
                // 顺序理由：湿是"表面状态"（水膜填平微表面），不是第四档色阶。
                //   必须在色阶/噪声**之后**：若在色阶之前染湿色，会被随后 A/B/C 的选档整段覆盖；
                //   作用在最终 albedo 上，才能让同一片沙无论当前落在哪一档都被均匀打湿。
                // 先向 _WetSandColor 靠拢（湿沙色），再按既有 _WetDarken 额外压暗
                //   （不新增第二个"压暗"旋钮，避免与 _WetDarken 语义重叠）。
                half3 albedoWet = lerp(albedo, _WetSandColor.rgb, wet);
                albedoWet *= lerp(1.0h, 1.0h - (half)_WetDarken, wet);
                albedoWet *= lerp(1.0h, 1.0h - (half)_WetLineDarkening, wetLine);

                // 湿润 = 变暗 + 变光滑（水膜填平微表面；高光反射是"湿"的关键信号）。
                half wetSmooth = wet * (half)_WetSmoothnessBoost;

                // ---- 沙纹（仅湿掩码内生效；纯视觉，不参与任何玩法判定）----
                // 高度场：h = sin(worldXZ·_RippleScale·dir + FBM·_RippleDistort·2π)
                //   波长 λ = 2π / _RippleScale；默认 16 → λ≈0.39 世界单位。
                //   按 §3.5 的「1 单位≈32px」口径，λ≈12.5px，45° 相机下肉眼稳定可辨。
                //   相位用低频 FBM 扭曲（扭曲噪声频率 = _RippleScale*0.25，即约 4 倍波长）→ 波峰弯曲，非机械斑马线。
                // 强度硬钳：振幅 = min(_RippleStrength, 0.15) × wet，法线与明度共用该振幅
                //   （Range 只约束 Inspector，材质 YAML 可能存更大值，故代码再钳一次）。
                float rstr  = min(_RippleStrength, 0.15);
                float2 rdir = float2(0.8575, 0.5145);   // normalize(1, 0.6)：斜向平行纹
                float ripPhase = dot(IN.positionWS.xz * _RippleScale, rdir)
                               + PirateFbm(IN.positionWS.xz * (_RippleScale * 0.25)) * _RippleDistort * 6.28318;
                float ripSin = sin(ripPhase);
                float ripCos = cos(ripPhase);
                float ripAmp = rstr * (float)wet;       // 干区振幅 = 0 → 沙纹自动不生效
                half  ripWave = (half)(ripSin * ripAmp);

                // 沙纹法线：沿波传播方向的斜率扰动（|bump| ≤ ripAmp ≤ 0.15）。
                float3 ripBump = float3(-rdir.x, 0.0, -rdir.y) * (ripCos * ripAmp);
                ripBump -= nrm * dot(ripBump, nrm);     // 投影到切平面，避免竖直面被拉歪
                nrm = normalize(nrm + ripBump);

                half rim  = pow(1.0h - saturate((half)dot(nrm, viewDirWS)), 2.0h);
                half wear = saturate(rim * (half)_EdgeWear);

                half metallic   = saturate((half)_Metallic);
                half smoothness = saturate((half)_Smoothness + wetSmooth);
                // 沙纹明度调制（±ripAmp，即 ±0.15 以内）+ 边缘磨损
                albedoWet *= 1.0h + ripWave;
                albedo = lerp(albedoWet, _WearColor.rgb, wear);

                // ---- 调试档（预期画面见各分支注释）----
                // 档 1：只 albedo（无光照/无阴影/无雾）。预期：能看到三档色阶与噪声，整体偏亮无立体感；
                //       水线附近应出现一道比周围更暗的湿痕窄带（残留线）。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(albedo, 1.0h);
                // 档 2：噪声灰度。预期：平滑云雾状图案，明暗从 0 到 1；若整片同色 -> 噪声坐标恒定
                //        （常见原因：物体无世界坐标差异，如坐标轴对齐的贴地薄板）。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(half3(nh, nh, nh), 1.0h);
                // 档 3：扰动后的世界法线。预期：R/G/B 平滑渐变；竖直面/水平面颜色明显不同。
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return half4(half3(nrm * 0.5 + 0.5), 1.0h);
                // 档 4：R/G/B = 光滑度, A = 金属度。预期：金属材质（黄铜/铁）A 通道为纯白。
                //       注意条件收窄到 <4.5：否则会吞掉新增的 5、6 档（既有档位语义保持不变）。
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                    return half4(half3(smoothness, smoothness, smoothness), metallic);
                // 档 5：湿掩码灰度（全白 = 完全湿）。预期：y≤-0.2（水线及以下）为纯白（1），
                //       随高度线性变暗，y≥+0.25 为纯黑（0）。程序化判据：水线附近像素灰度 >0.7，
                //       高台顶面像素灰度 <0.05；若整片同色 → _WaterLevelY 与实际水位不符，
                //       或物体各面没有世界 Y 分层（如坐标轴对齐的薄板）。
                if (_DebugMode > 4.5 && _DebugMode < 5.5)
                    return half4(half3(wet, wet, wet), 1.0h);
                // 档 6：沙纹灰度。预期：湿区内为明暗相间的斜向平行条纹（0.5±0.5），干区为中性灰 0.5 死平。
                //       程序化判据：湿区采样灰度标准差 >0.05（条纹可辨），干区标准差 ≈0；
                //       若湿区也是死平 → _RippleStrength=0，或湿掩码全 0（水位/带宽设错）。
                if (_DebugMode > 5.5 && _DebugMode < 6.5)
                {
                    // 单参 splat（half3(x)）在部分真机编译器上报"构造器参数个数错误"，显式三分量。
                    half ripGray = saturate(0.5h + 0.5h * (half)(ripSin * (float)wet));
                    return half4(ripGray, ripGray, ripGray, 1.0h);
                }
                // 档 7【本次新增】：细节贴图的乘性系数（0.5 = 系数 1.0 = 无起伏，>0.5 亮斑、<0.5 暗斑）。
                //       预期：整片有细碎噪点（不是纯 0.5 死平）；贝壳为稀疏亮点。
                //       程序化判据：200×200 窗灰度 std > 0.02（对应 8bit 约 5）。
                //       若死平 → _DetailAlbedoStrength=0 或贴图未赋/未生成（先跑 MaterialNoiseBuilder）。
                if (_DebugMode > 6.5)
                {
                    half detGray = saturate(detailLuma * 0.5h);
                    return half4(detGray, detGray, detGray, 1.0h);
                }

                // ---- PBR 光照 ----
                half alpha = 1.0h;
                BRDFData brdfData;
                // specular 传 0：非 _SPECULAR_SETUP 路径下 URP 会用 kDieletricSpec 与 albedo 自行插值
                // （见 BRDF.hlsl:96 `lerp(kDieletricSpec.rgb, albedo, metallic)`）。
                InitializeBRDFData(albedo, metallic, half3(0.0h, 0.0h, 0.0h), smoothness, alpha, brdfData);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                // 直射项（已含 light.shadowAttenuation，见 Lighting.hlsl:75）。
                half3 color = LightingPhysicallyBased(brdfData, mainLight, nrm, viewDirWS);

                // 间接项：SH 环境光（GDD §10.4 环境光浅蓝 #C8DDF0 由天空盒 SH 提供）。
                // 注意环境光不乘阴影衰减——软阴影只压直射，否则阴影里会死黑、失去风格化的通透感。
                half3 bakedGI = SampleSH(normalWS) * (half)_AmbientStrength;
                color += GlobalIllumination(brdfData, bakedGI, 1.0h, nrm, viewDirWS);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / ShadowCaster：投到主光阴影图
        // 用 URP 官方 ShadowCasterPass.hlsl（含 ApplyShadowBias 与 _LightDirection 常量缓冲），
        // 与 PirateOutline 的 ShadowCaster Pass 保持同一实现路径。
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
        // Pass 3 / DepthOnly：写 _CameraDepthTexture
        // 【必需】PirateWater.shader 的岸边泡沫线/浅深水过渡全靠场景深度差；若本 shader 不写深度，
        //   用本材质的物体在水面之后会被当成"没有东西"→ 泡沫线位置错误。
        //   手写极简实现（不引用 URP DepthOnlyPass.hlsl，避免其 _BaseMap 依赖），
        //   与 PirateOutline.shader 的 DepthOnly Pass 同一写法。
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

    // 不继承内置回退：回退会把材质悄悄换成 URP/Lit 的纯色外观，掩盖"shader 没找到"的问题。
    Fallback Off
}
