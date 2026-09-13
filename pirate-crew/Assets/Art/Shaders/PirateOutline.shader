// ============================================================================
// PirateOutline.shader —— 海盗军团夺宝 3D / M2 单位选中·悬停单体描边
//
// 【设计参照】Godot 版真实接线的那套描边 shader（本项目是 Godot 版重制）：
//   - modules/pirate_crew/shaders/outline_hover.gdshader   （悬停，inverted hull）
//   - modules/pirate_crew/shaders/outline_selected.gdshader（选中，inverted hull + 屏幕空间虚线）
//   - assets/shaders/outline.gdshader                     （基础描边，近处增粗）
//   接线在 modules/pirate_crew/scripts/characters/pirate_base.gd：
//     悬停 -> 生成一圈复制网格、material_override = outline_hover，cull_front
//     选中 -> 不启用 inverted hull，改由全屏后处理 outline_post.gdshader 画（见 PirateOutlinePost.shader）
//   本 shader 把 hover/selected 两种状态合并进同一个材质，用 _OutlineState 切换，
//   免去 Godot 版"为每个骨骼复制一圈网格、再换 material_override"的做法。
//
// 【实现路径】inverted hull（法线外扩 + 只画背面）：
//   第 1 个 Pass（Base）  ：正常画本体。**写实化后为 URP PBR**（BRDF + 主光阴影 + SH 环境光 + 雾，
//                          与 PirateSurface / PirateTerrain 同口径；旧的简单 Lambert 已退役）。
//                          保留此 Pass 结构（而非把单位换成 PirateSurface/URP Lit）是为了不打断
//                          「_OutlineState MPB 通道 + inverted hull + 选中虚线」这条选中反馈链路。
//   第 2 个 Pass（Outline）：Cull Front 只画背面，顶点沿法线外扩，得到一圈描边。
//   第 3 个 Pass（DepthOnly）：写入 URP 深度（_CameraDepthTexture / 深度预通道）。
//   第 4 个 Pass（ShadowCaster）：写入主光阴影图 —— 单位投影的来源（2026-09-13 补，
//     补前单位不投影、画面"平"；见 docs/描边Shader调试.md §七-2）。
//
// 【四个 Pass 的 LightMode 唯一性（2026-09-13 补 ShadowCaster 时特意复核）】
//   Base        = "UniversalForward"
//   Outline     = "SRPDefaultUnlit"
//   DepthOnly   = "DepthOnly"
//   ShadowCaster= "ShadowCaster"
//   四者互不相同；且都在 URP 的 ShaderTagId 取用列表内（不透明前向 DrawObjectsPass
//   取 SRPDefaultUnlit / UniversalForward / UniversalForwardOnly；阴影通道取 ShadowCaster；
//   深度通道取 DepthOnly）。任何"再加一个与既有 Pass 同 LightMode 的 Pass"都会被静默丢弃，
//   详见 §八-2 的真实事故复盘与第 2 个 Pass 上方的注释。
//   hover 与 selected 的差异（翻译自 Godot，逐条对应）：
//     | 维度     | hover                              | selected                          |
//     | 颜色     | 淡白 a≈0.22                        | 青色 #49d9d6 a≈0.949              |
//     | 粗细     | 细（_OutlineWidthHover=0.0025）     | 粗（_OutlineWidthSelected=0.006） |
//     | 线型     | 实线                               | 屏幕空间流动虚线（sin 相位）        |
//     | 语义     | "可选中"提示                        | "已选中"                          |
//   Godot 的 hover/selected 都做了"距离衰减"（pow(clip.w, 1-attenuation)），
//   使远处线条变细、近处不变，避免远处单位糊成一片；本 shader 保留该参数。
//
// 【_DebugMode 分档】图形学调试截图规范要求 shader 内含 debug_mode 拆分管线步骤。
//   注意：本环境无可用渲染路径，实际逐层截图待有图形界面的编辑器会话补跑，
//         采集脚本见 Assets/Editor/OutlineDebugCapture.cs，说明见 docs/描边Shader调试.md。
//   // [DEBUG] 档位语义（每一档的预期画面写在各 Pass 对应分支处）：
//     0  关闭调试 = 正常合成（本体 + 描边）
//     1  只显示本体不描边
//     2  只显示法线外扩结果（品红实色壳）
//     3  只显示描边掩码（单色剪影，无虚线）
//     4  显示深度/法线原始数据（本体换成法线 RGB + 视空间深度）
//     5  显示本体细节色斑系数（灰 0.5 = 无起伏；判据"200×200 窗 std > 0.02"）
//
// 【本体细节噪声（Scene_* 道具族"大色块平面"工单，本 shader 第三处用途）】
//   SceneArtBuilder 把道具按材质组合并成**无 UV 的大网格**、本体只有一个 _BaseColor →
//   每个材质组是一整片单色平面。本 shader 加一组**默认关闭**的可选细节贴图：
//     _DetailNoiseMap（albedo 乘性微色斑，线性均值 0.5）+ _DetailNoiseScale（世界 XZ UV 尺度）
//     + _DetailNoiseStrength（强度，默认 0）＋ _DetailBumpMap/_DetailBumpScale（细节法线，默认 0）。
//   贴图是**程序化资产**（算法唯一来源 Assets/Editor/MaterialNoiseBuilder.cs，512² 域名扭曲 fBm），
//   不是外部贴图 —— 与本工程 0 外部贴图的纪律一致。
//   【默认 0 的硬要求】单位材质（PirateOutlineUnit.mat，走 MPB 写 _OutlineState）与其他未赋值的
//   材质必须与加贴图之前**逐像素一致**；且 2D 属性未赋值时 Unity 会回落内置贴图（"gray" 在 Linear
//   下不是 0.5），所以强度默认 0 是唯一安全的关闭口径。赋值点见 SceneArtBuilder.EnsureOutlineMaterial。
//   【与 PirateSurface 的口径关系】同样的"世界 XZ UV + 均值保持乘性图 + 法线重定向到世界 XZ 基"；
//   差异是不做 UV 扭曲（_NoiseWarpStrength）—— 道具不是 1 单位格子的地形块，没有"与格子轴对齐"
//   的问题，省掉一次低频 FBM（本 shader 里也就没有值噪声内核）。
//
// 【为什么用 uniform 分支而不是 multi_compile 关键字】
//   _DebugMode 只在人工调试时改，且 5 档分支都是极短片段着色器分支，现代 GPU 上
//   动态分支代价可忽略；用关键字会为每个 Pass 生成 5 个变体（加上 hover/selected
//   若也用关键字则变体再翻倍），并且采集脚本每次切档都要 SetKeyword 管理全局关键字
//   状态，容易残留。故调试档与状态**全部走 uniform**。
//   （唯一代价：无法在 build 里彻底剔除调试分支；调试分支只有几行，可接受。）
//   【写实化后的例外】Base Pass 为了接收主光阴影/雾，拥有 3 组 multi_compile
//   （_MAIN_LIGHT_SHADOWS / _SHADOWS_SOFT / _FOG），这是光照本身需要的变体；
//   描边 Pass / DepthOnly / ShadowCaster 仍保持零关键字。
//
// 【[CHECK] 本地 URP 包核对结果】（完整清单见 docs/描边Shader调试.md）
//   以下每个 #include / 宏都已在
//   Library/PackageCache/com.unity.render-pipelines.universal@14.0.12 与
//   Library/PackageCache/com.unity.render-pipelines.core@14.0.12 中逐条核实存在：
//     Core.hlsl                                -> ShaderLibrary/Core.hlsl
//     Lighting.hlsl                            -> ShaderLibrary/Lighting.hlsl（:4-9 引入 BRDF/Debugging3D/GlobalIllumination/RealtimeLights/AmbientOcclusion/DBuffer）
//     TransformObjectToHClip                   -> core/SpaceTransforms.hlsl:108
//     TransformObjectToWorldNormal             -> core/SpaceTransforms.hlsl:199
//     TransformWorldToView                     -> core/SpaceTransforms.hlsl:97
//     TransformWorldToViewDir                  -> core/SpaceTransforms.hlsl:155
//     GetVertexPositionInputs / GetVertexNormalInputs -> URP/ShaderVariablesFunctions.hlsl:7/21
//     UNITY_MATRIX_P                           -> URP/ShaderLibrary/Input.hlsl:193（#define 到 OptimizeProjectionMatrix）
//     struct Light / GetMainLight              -> URP/RealtimeLights.hlsl:12/97（经 Lighting.hlsl 引入）
//     SampleSH                                 -> URP/GlobalIllumination.hlsl:21（经 Lighting.hlsl 引入）
//     _Time                                    -> URP/UnityInput.hlsl:40
//     ShadowCasterPass.hlsl                    -> URP/Shaders/ShadowCasterPass.hlsl:55/75（官方阴影 Pass）
//     ApplyShadowBias                          -> URP/Shadows.hlsl:471（ShadowCasterPass 调用）
//     **LerpWhiteTo                              -> core/CommonMaterial.hlsl:352/359
//        —— URP/Shadows.hlsl:298 调用它却**不自己 include** CommonMaterial.hlsl（只对"调用者已引入"
//           的场景自洽）。故 ShadowCaster Pass 内必须先 include CommonMaterial.hlsl，否则真实编译报
//           `undeclared identifier 'LerpWhiteTo' at Shadows.hlsl(298) (on d3d11)`（2026-09-13 实测，
//           与 §八-1 的 BRDFData 事故同型：符号存在 ≠ include 自洽）。**
//   注：core 与 URP 均未声明 ComputeScreenPos，故屏幕 UV 由 clip.xy/w 手算，未使用该函数。
//   注：本清单只能证明"符号存在"，**不能**证明 include 自洽——GlobalIllumination.hlsl 那次
//       静态核对全绿、真实编译照样失败（见下方 Base Pass 的踩坑记录）；Shadows.hlsl 的
//       LerpWhiteTo 是同一类事故的第二次实例。
// ============================================================================

Shader "PirateCrew/PirateOutline"
{
    Properties
    {
        // ---- 本体 ----
        // 写实化：本体从"Lambert+SH 平光"升级为 URP PBR（见 Base Pass），故新增金属度/光滑度。
        // 单位材质（PirateOutlineUnit.mat）与阵营色仍只写 _BaseColor（UnitOutlineBinder 的 MPB 通道），
        // 这两个参数取默认值即可；角色波次若要区分皮/革/铁，可给各部件材质覆写。
        _BaseColor              ("本体基础色", Color) = (0.85, 0.85, 0.90, 1.0)
        _Metallic               ("本体金属度", Range(0.0, 1.0)) = 0.0
        _Smoothness             ("本体光滑度", Range(0.0, 1.0)) = 0.35

        // ---- 本体细节噪声（Scene_* 道具族用；程序化资产，算法唯一来源 = Assets/Editor/MaterialNoiseBuilder.cs）----
        // 【为什么 Scene_* 族需要它】SceneArtBuilder 把道具按材质组合并成**大网格**（1 组 = 1 DrawCall），
        //   这些网格只有 position+normal、**没有 UV**，本体又只有一个 _BaseColor → 每个材质组是一整片
        //   单色平面（观感"廉价"的主因）。这里给本体加一层**世界空间 XZ 采样**的程序化噪声：
        //   色斑（乘性、均值保持）+ 细节法线，把"大色块"变成"有介质感的表面"。
        // 【默认 0 = 与加贴图之前逐像素一致（硬要求）】单位材质（PirateOutlineUnit.mat）与任何
        //   未显式赋值的材质必须保持原样；且 2D 属性未赋值时 Unity 会回落到内置贴图，其线性值不可预期
        //   （"gray" 在 Linear 下不是 0.5），所以强度默认 0 是唯一安全的"关闭"口径。
        // 【值从哪来】Scene_Wood / Scene_Rock / Scene_Foliage / Scene_Cloth / Scene_GrassRoot /
        //   Scene_GrassLight 等在 SceneArtBuilder.EnsureOutlineMaterial 里赋值；远影族
        //   （Silhouette / Cloud / SailFar，unlit）与 Flag/Metal 刻意保持纯净（雾里不需要细节）。
        //   _DetailNoiseMap 用 albedo 类贴图（木→沙族暖色色斑、岩/草→本族色斑）；
        //   _DetailBumpMap 用各族法线图（木→岩族法线给"木纹/节疤"、草→草族法线给"绒毛"）。
        [NoScaleOffset] _DetailNoiseMap ("本体细节色斑图（线性均值 0.5 的乘性图）", 2D) = "gray" {}
        _DetailNoiseScale   ("本体细节世界尺度（UV=世界XZ×该值；1/该值=平铺米数）", Float) = 0.10
        _DetailNoiseStrength ("本体细节色斑强度（0=关闭；单位材质保持 0）", Range(0.0, 1.0)) = 0.0
        [NoScaleOffset] _DetailBumpMap ("本体细节法线图（切空间；重定向到世界 XZ 基）", 2D) = "bump" {}
        _DetailBumpScale    ("本体细节法线强度（0=关闭）", Range(0.0, 2.0)) = 0.0

        // ---- 描边：三套色 + 三套宽（对应 Godot outline_hover / outline_selected）----
        // _OutlineColor 是"无状态"兜底色；实际运行时由 _OutlineState 选中 hover/selected 两套。
        _OutlineColor           ("描边兜底色（state=0）", Color) = (0.286, 0.851, 0.839, 0.949)
        _OutlineColorHover      ("悬停描边色", Color) = (1.0, 1.0, 1.0, 0.22)
        _OutlineColorSelected   ("选中描边色 #49d9d6", Color) = (0.286, 0.851, 0.839, 0.949)

        _OutlineWidth           ("描边宽度[状态兜底]", Range(0.0, 0.05)) = 0.006
        _OutlineWidthHover      ("悬停描边宽度", Range(0.0, 0.05)) = 0.0025
        _OutlineWidthSelected   ("选中描边宽度", Range(0.0, 0.05)) = 0.006

        // 0 = 无描边（透明），1 = 悬停，2 = 选中。由选中/悬停系统每帧写。
        _OutlineState           ("描边状态 0=无 1=悬停 2=选中", Range(0.0, 2.0)) = 0.0
        _OutlineAlpha           ("描边整体透明度（叠加乘算）", Range(0.0, 1.0)) = 1.0

        // ---- 外扩方式 ----
        // [0] 屏幕空间恒定粗细：距离无关，对应 Godot hover/selected 的做法（推荐）
        // [1] 世界/物体空间法线外扩：经典 inverted hull，粗细随距离变小；
        //     该模式下 _OutlineWidth* 单位变成"米"，需调到 0.01~0.05 量级（见调参文档）
        _OutlineExpandMode      ("外扩模式 0=屏幕空间恒定 1=世界空间法线", Range(0.0, 1.0)) = 0.0
        // 距离衰减：0=恒定粗细，1=远处显著变细（Godot 默认 0.4）
        _OutlineDistanceAttenuation ("距离衰减 0=恒定 1=远处显著变细", Range(0.0, 1.0)) = 0.4

        // ---- 选中虚线 ----
        _DashSpeed              ("虚线流动速度", Range(0.0, 20.0)) = 5.0
        _DashFrequency          ("虚线密度", Range(1.0, 200.0)) = 50.0

        // ---- 调试（AGENTS.md 图形学调试截图规范）----
        // 0 正常 / 1 只本体 / 2 只外扩壳 / 3 只描边掩码 / 4 深度法线原始数据 / 5 本体细节贴图系数
        _DebugMode              ("调试模式 0=正常 1=只本体 2=只外扩 3=只掩码 4=深度法线 5=细节贴图", Range(0.0, 5.0)) = 0.0
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
        LOD 100

        // ====================================================================
        // Pass 1 / Base：正常渲染本体
        // ====================================================================
        Pass
        {
            Name "PirateOutlineBase"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   BaseVertex
            #pragma fragment BaseFragment
            // 写实化（2026-09）：本体 Pass 从"简单 Lambert + SH"升级为 URP PBR，
            // 故本体要**接收主光阴影**与雾（否则没阴影的单位在写实光照里会"浮"在场景上）。
            // 关键字只加在本体 Pass；描边 Pass（SRPDefaultUnlit）保持零关键字（unlit 叠加层不需要）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            // Core.hlsl 已含 Common.hlsl / URP Input.hlsl / ShaderVariablesFunctions.hlsl，
            // 因此 TransformObjectToHClip、GetVertexPositionInputs 等无需再单独 include。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // struct Light / GetMainLight()（RealtimeLights.hlsl:12/97）与 SampleSH（GlobalIllumination.hlsl:21）
            // 由 Lighting.hlsl 的 include 链提供（Lighting.hlsl:4-9 = BRDF/Debugging3D/GlobalIllumination/
            // RealtimeLights/AmbientOcclusion/DBuffer）。
            //
            // 【踩坑记录 · 2026-09-13，务必不要再"优化"回去】
            //   这里原先只 include GlobalIllumination.hlsl（想缩小编译面），本环境无渲染路径时
            //   静态核对"用到的符号都在"看起来成立，但在真实 GPU 会话里 d3d11 直接编译失败：
            //     Shader error in 'PirateCrew/PirateOutline': unrecognized identifier 'BRDFData'
            //       at .../ShaderLibrary/GlobalIllumination.hlsl(353)
            //   原因：GlobalIllumination.hlsl 自己并不 include BRDF.hlsl，而它的
            //   GlobalIllumination(BRDFData, BRDFData, ...) 函数体用到了 BRDF.hlsl 里的 BRDFData；
            //   它只对"调用者已引入 BRDF"的场景自洽。Lighting.hlsl 正是把这一组一起引入的标准入口
            //   （URP 自带 Lit/SimpleLit 也走它）。
            //   教训：URP 的"轻量 include"不能靠文件名/符号检索推断自洽性，必须在有渲染路径的
            //         编辑器里真实编译过才算验证；此后本 shader 的任何 include 改动都要重跑一次
            //         play mode 并 read_console 确认 0 shader error。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 写实化：本体接主光阴影需要 TransformWorldToShadowCoord（Shadows.hlsl:319）。
            // 【必须在 Lighting.hlsl 之后】Shadows.hlsl:298 用 LerpWhiteTo（来自 core 的
            //   CommonMaterial.hlsl，而 Shadows.hlsl 自身不 include 它）；Lighting.hlsl 的
            //   第 4 行先引入 BRDF.hlsl → CommonMaterial.hlsl，顺序才自洽。反序会复现
            //   `undeclared identifier 'LerpWhiteTo'`（2026-09-13 实测 d3d11，见 docs/描边Shader调试.md §八）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // CBUFFER 字段顺序必须与 Properties 声明顺序一致，否则 SRP Batcher 会判定不兼容。
            // 本体 Pass 与描边 Pass 必须声明**同一份** CBUFFER（同名字段、同顺序）。
            // （DepthOnly / ShadowCaster 两个 Pass 沿用既有状态、不声明 CBUFFER —— 它们不读材质属性，
            //   代价只是这两个 Pass 不参与 SRP Batcher，与本 shader 改动前一致。）
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Metallic;
                float  _Smoothness;
                float  _DetailNoiseScale;
                float  _DetailNoiseStrength;
                float  _DetailBumpScale;
                float4 _OutlineColor;
                float4 _OutlineColorHover;
                float4 _OutlineColorSelected;
                float  _OutlineWidth;
                float  _OutlineWidthHover;
                float  _OutlineWidthSelected;
                float  _OutlineState;
                float  _OutlineAlpha;
                float  _OutlineExpandMode;
                float  _OutlineDistanceAttenuation;
                float  _DashSpeed;
                float  _DashFrequency;
                float  _DebugMode;
            CBUFFER_END

            // 细节贴图：**纹理与采样器必须在 UnityPerMaterial CBUFFER 之外**（URP 硬要求，见
            //   URP LitInput.hlsl 的 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); 分离声明）。
            //   名字刻意避开 URP 全局属性：`_BumpMap`/`_BumpScale` 被 SurfaceInput.hlsl 全局声明过，
            //   故本 shader 用 `_DetailBumpMap`/`_DetailBumpScale`（本 shader 的 include 链
            //   Core→Lighting→Shadows 并不含 SurfaceInput.hlsl，本可不改名；改名是为了将来加
            //   LitInput.hlsl 之类时不会撞车）。
            //   TEXTURE2D / SAMPLER / SAMPLE_TEXTURE2D / UnpackNormalScale 均由 Core.hlsl 的
            //   include 链提供（core Common.hlsl / Packing.hlsl），无需额外 include。
            TEXTURE2D(_DetailNoiseMap); SAMPLER(sampler_DetailNoiseMap);
            TEXTURE2D(_DetailBumpMap);  SAMPLER(sampler_DetailBumpMap);

            struct AttributesBase
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsBase
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                real   fogFactor  : TEXCOORD2;
            };

            VaryingsBase BaseVertex(AttributesBase IN)
            {
                VaryingsBase OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = p.positionCS;
                OUT.normalWS   = n.normalWS;
                OUT.positionWS = p.positionWS;
                // 雾因子在顶点算一次（URP 标准做法），与 PirateSurface / PirateTerrain 同口径。
                OUT.fogFactor  = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 BaseFragment(VaryingsBase IN) : SV_Target
            {
                // [DEBUG] 档 2/3：本体不画，只留外扩壳 / 掩码。
                //   预期画面：物体本体消失，场景里只剩一圈（档2 品红 / 档3 单色）壳。
                //   若此时仍能看到本体 -> 说明本体 Pass 的 discard 未生效（档位值传错）。
                if (_DebugMode > 1.5 && _DebugMode < 3.5)
                    discard;

                // [DEBUG] 档 4：深度/法线原始数据。
                //   预期画面：物体表面按世界法线染色 —— R=法线.x 映射, G=法线.y 映射,
                //             B=视空间深度（越近越黑、越远越蓝，50m 封顶）。
                //   常见异常：整块死灰蓝 -> 法线没传进 Varyings（normalOS 丢失或模型无法线）；
                //             整块同色 -> 模型是硬边平面（属正常，换圆球/角色模型看渐变）。
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                {
                    float3 n = normalize(IN.normalWS) * 0.5 + 0.5;
                    float  viewDepth = -TransformWorldToView(IN.positionWS).z;
                    float  d = saturate(viewDepth / 50.0);
                    return half4(n.x, n.y, d, 1.0);
                }

                // 正常档 0/1：URP PBR（写实化核心改动）。
                // 【为什么把本体 Pass 升级为 PBR，而不是让单位改用 PirateSurface / URP Lit】
                //   单位材质必须同时满足三件事：① 逐单位写 _OutlineState 的 MPB 通道；
                //   ② inverted hull 描边 Pass；③ 选中虚线。PirateSurface 没有后两者，
                //   URP/Lit 也没有 _OutlineState —— 换 shader 会把"选中反馈"整条链路打断
                //   （UnitOutlineBinder.WarnIfNotOutlineMaterial 会直接告警）。
                //   故代价最小的路径 = 保留 PirateOutline 的四个 Pass 结构，只把 Base Pass 的
                //   光照从 Lambert 换成 URP 官方 BRDF（与 PirateSurface / PirateTerrain 同口径）。
                //   代价：本体 Pass 多了阴影/雾关键字（变体增加），但这是"写实"的必要成本。
                float3 nrm = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ---- 本体细节噪声（Scene_* 道具族；默认强度 0 → 单位材质逐像素与改动前一致）----
                // 【为什么 UV 是"世界 XZ × _DetailNoiseScale"而不是模型 UV】Scene_* 族是 SceneArtBuilder
                //   落盘的**合并网格**（Assets/Art/Models/Scene/*.asset），顶点缓冲只有 position+normal、
                //   **没有 UV 通道** → 模型 UV 路径无处可依。世界 XZ 投影顺带让相邻道具/同一构件的
                //   纹理跨面连续（与 PirateSurface / PirateTerrain 同一口径）。
                // 【已知边界】世界 XZ 投影对水平面最准确、对竖直面退化（细节被"竖向挤出"成条纹）；
                //   故再叠一项 **worldY × (_DetailNoiseScale × 0.37)** —— 与 PirateSurface.shader:389-390
                //   主噪声"竖直面用 y 参与坐标，避免整面同色"是同款做法（本工程既有纪律，不是新发明）。
                //   代价：平铺严格性只在 XZ 上成立（Y 是斜切），视觉上无接缝问题。
                // 【平铺尺度】_DetailNoiseScale = 1/平铺米数：木/布 0.5-0.55（约 2m）、岩 0.45（2.2m）、
                //   草 0.7（1.4m）。见 SceneArtBuilder.EnsureOutlineMaterial 的 NoiseRecipe 预设表。
                float2 detailUV = IN.positionWS.xz * _DetailNoiseScale
                                + IN.positionWS.y * (_DetailNoiseScale * 0.37);

                // 乘性微色斑：贴图线性均值恰 0.5 → *2 后均值恰 1.0 → **不改已调好的 _BaseColor**，
                //   只加起伏（调色板 §SceneArtPalette 与 r3 已裁决的光照口径都不被扰动）。
                half3 detailAlb = SAMPLE_TEXTURE2D(_DetailNoiseMap, sampler_DetailNoiseMap, detailUV).rgb;
                half3 albedo = _BaseColor.rgb
                             * lerp(half3(1.0h, 1.0h, 1.0h), detailAlb * 2.0h, (half)_DetailNoiseStrength);

                // [DEBUG] 档 5：本体细节色斑的"乘性系数"线性明度（0.5 = 系数 1.0 = 无起伏）。
                //   预期：道具表面有细碎噪点（木=暖色微斑、岩=斑块+裂纹、草=双色 patch）；
                //         远影族（Silhouette/Cloud/SailFar，走 URP/Unlit）与 Flag/Metal 为纯 0.5 死平
                //         —— 它们刻意不给细节噪声（雾里/金属不需要）。
                //   判据：道具表面 200×200 窗灰度 std > 0.02（8bit 约 5）。
                //   若死平 → _DetailNoiseStrength=0，或贴图未生成（先跑
                //   PirateCrew/渲染/生成程序化材质噪声贴图）。
                if (_DebugMode > 4.5)
                {
                    half detGray = saturate(dot(detailAlb * 2.0h, half3(0.2126h, 0.7152h, 0.0722h)) * 0.5h);
                    return half4(detGray, detGray, detGray, 1.0h);
                }

                // 细节法线：切空间 (x,y) → 世界 XZ 基（低成本 worldspace 重定向，不引 tangent 帧）。
                //   只取 xy、丢掉 z：未赋贴图时默认 "bump" 贴图的 xy 未必恰好为 0，
                //   而 z=1 的"平法线"在这里贡献本来就是 0 —— 加上 _DetailBumpScale 默认 0，双重保险。
                //   近似性：竖直面上 XZ 基退化（V 方向被拉长），与上方细节色斑的近似同源。
                half4 detailNrm = SAMPLE_TEXTURE2D(_DetailBumpMap, sampler_DetailBumpMap, detailUV);
                float2 nTS = UnpackNormalScale(detailNrm, 1.0h).xy;
                float3 bumpTex = float3(nTS.x, 0.0, nTS.y) * _DetailBumpScale;
                bumpTex -= nrm * dot(bumpTex, nrm);     // 投影到切平面，避免竖直面被拉歪
                nrm = normalize(nrm + bumpTex);

                half alpha = 1.0h;
                BRDFData brdfData;
                // specular 传 0：URP 在非 _SPECULAR_SETUP 路径下用 kDieletricSpec 与 albedo 自行插值
                // （BRDF.hlsl:96 `lerp(kDieletricSpec.rgb, albedo, metallic)`），与 PirateSurface 一致。
                InitializeBRDFData(albedo, saturate((half)_Metallic),
                    half3(0.0h, 0.0h, 0.0h), saturate((half)_Smoothness), alpha, brdfData);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                // 直射项（已含 light.shadowAttenuation，见 Lighting.hlsl:75）。
                half3 color = LightingPhysicallyBased(brdfData, mainLight, nrm, viewDirWS);

                // 间接项：SH 环境光。**不乘阴影衰减** —— 软阴影只压直射，
                // 否则阴影里死黑、失去"写实但通透"的观感（与 PirateSurface 同一条纪律）。
                // 环境光来源已从"天空盒 SH"改为 Gradient 三色（见 BattleSceneLighting.ApplyThreePointAmbient）。
                half3 bakedGI = SampleSH(nrm);
                color += GlobalIllumination(brdfData, bakedGI, 1.0h, nrm, viewDirWS);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / Outline：inverted hull（法线外扩 + Cull Front 只画背面）
        // 对应 Godot outline_hover.gdshader / outline_selected.gdshader 的 vertex() 段
        //
        // 【LightMode 必须与 Base Pass 不同，这是本 shader 最隐蔽的一处坑】
        //   URP 的不透明前向 DrawObjectsPass 用固定的 ShaderTagId 列表取 pass
        //   （DrawObjectsPass.cs:133 = SRPDefaultUnlit / UniversalForward / UniversalForwardOnly），
        //   而 Unity 的 DrawRenderers **每个 tag 槽位只会取一个 pass**。所以若本 Pass 也标
        //   "UniversalForward"，它会和 Base Pass 撞同一个槽位、被静默丢弃——
        //   表现为"材质正常、本体正常、就是没有描边"，且 Console 无任何报错。
        //   实测（2026-09-13，play mode + _DebugMode=2）：本体 Pass 已 discard（单位消失），
        //   描边 Pass 的品红壳却一像素都没出现，据此定位。
        //   SRPDefaultUnlit 在 URP 列表里是独立槽位（也是 URP 官方"额外 unlit pass"的常规做法），
        //   故本 Pass 用它。
        // ====================================================================
        Pass
        {
            Name "PirateOutlineHull"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front          // 只画背面：外扩后背面绕到轮廓外侧，形成一圈描边
            ZWrite Off          // 描边是叠加层，不写深度（hover 半透明时必须）
            ZTest LEqual        // 被本体/其他物体挡住的部分自动剔除

            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   OutlineVertex
            #pragma fragment OutlineFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 与 Base Pass 完全相同的 CBUFFER（同名同序，SRP Batcher 才兼容）。
            // 新增的三个 _Detail* 标量也必须在这里同序出现（本 Pass 不采样细节贴图，但布局要一致）。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Metallic;
                float  _Smoothness;
                float  _DetailNoiseScale;
                float  _DetailNoiseStrength;
                float  _DetailBumpScale;
                float4 _OutlineColor;
                float4 _OutlineColorHover;
                float4 _OutlineColorSelected;
                float  _OutlineWidth;
                float  _OutlineWidthHover;
                float  _OutlineWidthSelected;
                float  _OutlineState;
                float  _OutlineAlpha;
                float  _OutlineExpandMode;
                float  _OutlineDistanceAttenuation;
                float  _DashSpeed;
                float  _DashFrequency;
                float  _DebugMode;
            CBUFFER_END

            struct AttributesOutline
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsOutline
            {
                float4 positionCS : SV_POSITION;
                float4 color      : TEXCOORD0; // rgb = 描边色, a = 该状态基础透明度
                float2 screenNDC  : TEXCOORD1; // 用于选中虚线的屏幕空间相位
                float  dashed     : TEXCOORD2; // 1 = 走虚线（选中），0 = 实线
            };

            // 按 _OutlineState 取色（Godot 用 material_override 切换，本 shader 用 uniform 选）。
            float4 OutlineColorForState()
            {
                if (_OutlineState > 1.5) return _OutlineColorSelected;
                if (_OutlineState > 0.5) return _OutlineColorHover;
                return _OutlineColor;
            }

            // 按 _OutlineState 取宽。
            float OutlineWidthForState()
            {
                if (_OutlineState > 1.5) return _OutlineWidthSelected;
                if (_OutlineState > 0.5) return _OutlineWidthHover;
                return _OutlineWidth;
            }

            VaryingsOutline OutlineVertex(AttributesOutline IN)
            {
                VaryingsOutline OUT;

                float4 positionOS = IN.positionOS;
                float  width = OutlineWidthForState();

                // ---- 外扩模式 1：世界/物体空间法线外扩（经典 inverted hull）----
                // 该模式下 width 的单位是"米"（因为直接加在 positionOS 上），
                // 所以 _OutlineWidth* 需要放到 0.01~0.05，否则 0.006 几乎看不见。
                if (_OutlineExpandMode > 0.5)
                {
                    positionOS.xyz += normalize(IN.normalOS) * width;
                }

                VertexPositionInputs p = GetVertexPositionInputs(positionOS.xyz);
                float4 positionCS = p.positionCS;

                // ---- 外扩模式 0：屏幕空间恒定粗细（Godot hover/selected 的原始做法）----
                // Godot: clip.xy += normalize(mat3(P)*view_normal).xy * width * pow(w, 1-att)
                // 逐项对应：normalize(mat3(P)*view_normal).xy 即 clipNormal.xy；
                //           pow(clip.w, 1.0-distance_attenuation) 即 distFactor。
                if (_OutlineExpandMode <= 0.5)
                {
                    float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                    float3 normalVS   = TransformWorldToViewDir(normalWS, true);
                    // UNITY_MATRIX_P 在 URP ShaderLibrary/Input.hlsl:193 定义，随 Core.hlsl 引入。
                    float3 clipNormal = mul((float3x3)UNITY_MATRIX_P, normalVS);
                    // +1e-5 防止法线正对镜头时 xy≈0 -> normalize 出 NaN（表现为闪点/破面）。
                    float2 dirCS = normalize(clipNormal.xy + float2(1e-5, 1e-5));
                    float  w = max(positionCS.w, 1e-3);
                    float  distFactor = pow(w, 1.0 - _OutlineDistanceAttenuation);
                    positionCS.xy += dirCS * width * distFactor;
                }

                OUT.positionCS = positionCS;

                // 屏幕空间虚线相位用 NDC（不随 3D 形体变形，对应 Godot 的 screen_pos = clip.xy / clip.w）。
                float w = max(positionCS.w, 1e-3);
                OUT.screenNDC = positionCS.xy / w;

                float4 color = OutlineColorForState();
                color.a *= _OutlineAlpha;
                OUT.color  = color;
                OUT.dashed = (_OutlineState > 1.5) ? 1.0 : 0.0;
                return OUT;
            }

            half4 OutlineFragment(VaryingsOutline IN) : SV_Target
            {
                // [DEBUG] 档 1：只显示本体。描边 Pass 整段丢弃。
                //   预期画面：干净的、无任何描边的单位。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    discard;

                // [DEBUG] 档 4：原始数据档，描边 Pass 整段丢弃（画面完全由 Base Pass 提供）。
                //   档 5（细节贴图）同样丢弃：否则描边色会盖在细节灰度图上干扰判读。
                if (_DebugMode > 3.5)
                    discard;

                // [DEBUG] 档 2：只显示法线外扩结果 —— 品红实色壳。
                //   预期画面：本体消失，物体轮廓发胖一圈、纯品红、无描边颜色/无虚线。
                //   常见异常：发胖过量 -> _OutlineWidth 太大；
                //             壳上有破洞/裂缝 -> 模型法线被压平或顶点色硬边（见调参文档）。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(1.0, 0.0, 1.0, 1.0);

                // [DEBUG] 档 3：只显示描边掩码 —— 单色剪影，忽略虚线、忽略 hover 半透明。
                //   预期画面：一圈不透明、无流动的纯青色轮廓（像给物体描了实线）。
                //   常见异常：轮廓断断续续 -> 模型是硬边/法线不连续；
                //             完全没有轮廓 -> Cull Front 写反了（改成 Cull Back 试试）或
                //             材质渲染队列被物体自身深度覆盖。
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return half4(_OutlineColorSelected.rgb, 1.0);

                // 正常档 0：实线 / 虚线（选中）。
                float alpha = IN.color.a;
                if (IN.dashed > 0.5)
                {
                    // 屏幕空间流动虚线，对应 Godot outline_selected.gdshader 的
                    // phase = screen_pos.y * dash_frequency + TIME * dash_speed。
                    float phase = IN.screenNDC.y * _DashFrequency + _Time.y * _DashSpeed;
                    float dash  = smoothstep(-0.15, 0.15, sin(phase));
                    alpha *= dash;
                }

                // 虚线被 sin 压到 0 的片段直接丢弃，避免叠加层上的极淡残影。
                if (alpha < 0.002)
                    discard;

                return half4(IN.color.rgb, alpha);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 3 / DepthOnly：让描边单位进入 URP 深度预通道
        // 手工极简实现（不引用 URP 自带 DepthOnlyPass.hlsl，避免引入 _BaseMap 等
        // 本 shader 不存在的 CBUFFER 字段导致编译失败）。
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

        // ====================================================================
        // Pass 4 / ShadowCaster：把单位写入主光阴影图（单位投影的唯一来源）
        //
        // 【为什么必须有这个 Pass】URP 的主光阴影图只由标了 "LightMode" = "ShadowCaster" 的
        //   Pass 填充。本 shader 自带本体 Pass（不是 URP/Lit），补 Pass 之前单位既不投影、
        //   也不进阴影图 —— 画面因此缺少一个关键深度线索（"看起来平"的主因）。
        //
        // 【实现方式】直接 include URP 官方 ShadowCasterPass.hlsl（URP/Lit 也走它），
        //   而不是手写：那里已包含 ApplyShadowBias（法线偏移防自阴影条纹）与
        //   _LightDirection / _LightPosition 的常量缓冲读取（由 ShadowUtils 在阴影通道前设置）。
        //   该文件里的 `_BaseMap` / `_Cutoff` 代码全部在 `#if defined(_ALPHATEST_ON)` 内，
        //   本 shader 不声明该关键字，故不会引用不存在的属性（这也是它比 DepthOnlyPass.hlsl
        //   好用的地方：后者在非 AlphaTest 场景仍需 _BaseMap）。
        //
        // 【Alpha 与本体一致】本体 Pass 是不透明（ZWrite On、无 Blend、alpha 恒 1），
        //   故这里同样不做 alpha 裁剪、不声明 _ALPHATEST_ON —— 阴影形状 = 本体轮廓。
        //   若将来本体接透明/镂空，必须同步给这里加 _ALPHATEST_ON 与同样的裁剪，否则阴影会变"实心"。
        //
        // 【ColorMask 0 不需要写】阴影图只关心深度，但 URP 官方 Pass 由 URP 的阴影
        //   RenderTarget 配置决定写入通道，Pass 内不写 ColorMask 也能正确只留深度；
        //   Lit.shader 写了 `ColorMask 0` 是防御性写法。这里显式写上，与官方一致（无害）。
        //
        // 【_CASTING_PUNCTUAL_LIGHT_SHADOW】本工程只有一盏方向光（URP Asset 关了点光阴影），
        //   理论上恒为方向光分支；但仍保留该 multi_compile（URP 官方 Pass 也保留），
        //   这样将来加点光/聚光阴影时不用回来补，只多 1 个变体。
        // ====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back           // 与本体 Pass 一致：阴影形状取自正面轮廓

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // 方向光阴影 vs 点/聚光阴影的 Normal Bias 公式不同（见 ShadowCasterPass.hlsl 头注释）。
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // 【include 顺序是硬要求 —— 2026-09-13 真实报错，不要再"顺手删掉"这两行】
            //   URP 的 Shadows.hlsl:298 调用 LerpWhiteTo()，该函数定义在 core 的
            //   CommonMaterial.hlsl 里，而 **Shadows.hlsl 自己并不 include 它** ——
            //   它只对"调用者已引入 CommonMaterial.hlsl"的场景自洽。
            //   实测（Unity 一次真实导入，d3d11）：
            //     Shader error in 'PirateCrew/PirateOutline': undeclared identifier 'LerpWhiteTo'
            //       at .../universal@14.0.12/ShaderLibrary/Shadows.hlsl(298) (on d3d11)
            //   根因：本 Pass 原先只 include ShadowCasterPass.hlsl，而它只带 Core.hlsl + Shadows.hlsl。
            //   URP 官方 Lit.shader 的 ShadowCaster Pass 之所以没事，是因为它先 include 了
            //   LitInput.hlsl（其第 5 行显式 include CommonMaterial.hlsl）。
            //   故此处照抄官方顺序：Core.hlsl → CommonMaterial.hlsl → ShadowCasterPass.hlsl。
            //   这是「符号存在 ≠ include 自洽」的又一实例（同类事故见 §八-1 的 BRDFData）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            // 该 include 自带 Core.hlsl + Shadows.hlsl（含 ApplyShadowBias、TransformWorldToHClip）。
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }

    // 本 shader 自带本体 + 描边 + 深度，不继承任何内置回退。
    Fallback Off
}
