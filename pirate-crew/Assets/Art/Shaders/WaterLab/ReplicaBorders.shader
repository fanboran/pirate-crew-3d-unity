// ============================================================================
// ReplicaBorders.shader —— Sandcastle 沙盘边框三 shader（Lit/BordersBack pid581 /
// BordersGroundFront pid584 / BordersWaterFront pid577）的单文件三 pass 移植
// （I5a）。三个 renderer 共用单位立方体 mesh（sa0 pid378，底面 y=0，16 顶点 24 索引），
// 视觉分化全部来自 Cull/clip——本 shader 保持该结构，材质勾选一个模式即对应原作
// 一个物体：
//   [Toggle] _REPLICA_MODE_BACK          → Lit/BordersBack     pass0（Cull Front）
//   [Toggle] _REPLICA_MODE_FRONTGROUND   → BordersGroundFront  pass0（Cull Back）
//   [Toggle] _REPLICA_MODE_FRONTWATER    → BordersWaterFront   pass0（Cull Back ZWrite Off）
//   [Enum(Back,1,FrontGround,2,FrontWater,3)] _Mode 为脚本侧下拉（装配器按它
//   EnableKeyword；材质面板直接勾 Toggle 亦可，二者等价）。
//
// 【几何契约（I5 装配必须满足，否则采样错位）】
//   · mesh = sa0 pid378 自定义立方体（16 顶点 24 索引——非标准 24 顶点 cube）：
//     底 y=0、AABB center(0,0.5,0) extent(0.5,0.5,0.5)；**网格 UV 编码沙盘域列
//     坐标 (X→u, Z→v)**——Back/FrontWater/GroundFront 的模拟量采样全靠它（每个
//     顶点知道自己的水柱），不是标准逐面参数化 UV；
//   · renderer 缩放 = 沙盘域尺寸（原作 Game.Prepare：borders.localScale =
//     formatProperty.size，菜单 30 m；B 流：域 [−15,+15]²、row=Z/col=X）；
//   · UV(u,v) 须与 _WaterLabOrigin 域的 (X→u, Z→v) 同向（B 流轴向）。
//
// 【反汇编依据】external/sandcastle-ref/rt/out/Borders/（20 段 *.hlslasm +
//   instruction-tables.md + H-borders.hlsl 草稿）。pass 状态取 structure.json：
//   Back 仅 1 pass（Cull Front/ZWrite On/ZTest LEqual，**无 ShadowCaster**——
//   H 报告 §1.1 表"三者 pass1"对 Back 不成立，以 structure.json 为准）；
//   FrontGround pass0/1 均 Cull Back/ZWrite On；FrontWater pass0 Cull Back/
//   ZWrite **Off**、pass1（ShadowCaster）Cull Front/ZWrite On。ZTest 全 4=LEqual。
//
// 【模拟缓冲替换说明】原作 PS 手写双线性遍历 StructuredBuffer（16B/格打包解码），
//   本仓按任务绑定替换为实验台已有全局 _WaterLabHeightTex/_WaterLabGroundTex
//   （Ocean 同名全局，WaterLabSimulation 发布）。恒等性：原作解码 ×1e-6 与本仓
//   DecodeEta(e)=e·5−3 都是**逐格仿射变换**，与双线性插值可交换——「先硬件双线性
//   再解码」≡「先解码再手写双线性」；原作手写 clamp 到 [0,size−1] 与 clamp 寻址
//   纹理采样等价。水量 = eta − ground（水面高程差 = 水柱深，原作 word0 低 24 位
//   同义）。降级变体（@2038/@2870/@2686，模拟缓冲未绑定的 keyword-off 分支）不
//   移植：本仓缓冲恒绑定。
//
// 【底数修正】DXBC 的 exp/log 指令是 exp2/log2（HLSL pow() 即编译为该序列）。
//   H-borders.hlsl 草稿按自然底写的两处衰减，按 DXBC 语义修正为 exp2：
//   · 波影 exp2(−0.721348·水量)：0.721348 = 1/(2·ln2) → 等效 e^(−水量/2)；
//   · 吸收 exp2(−d·σ·8.65617)：8.65617 ≈ 6/ln2 → 等效 e^(−6σd)。
//   （σ 按 R 抓帧定案为运行时真值 cb0[132]=(0.9930291,0.1753813,0.0557871)——
//     A 流 σ≡0 静态结论已被 R 推翻，见交接§3 R 行；σ≡0 分支不保留。）
// ============================================================================

Shader "PirateCrew/WaterLab/ReplicaBorders"
{
    Properties
    {
        // ---- 模式（三选一；[Enum] 供装配脚本读，Toggle 直接驱动 keyword）----
        [Enum(Back,1,FrontGround,2,FrontWater,3)]
        _Mode              ("模式（脚本侧；与下方 Toggle 联动说明见文件头）", Float) = 1
        [Toggle(_REPLICA_MODE_BACK)]         _ModeBack        ("模式：Back 内壁剪影", Float) = 1
        [Toggle(_REPLICA_MODE_FRONTGROUND)]  _ModeFrontGround ("模式：FrontGround 沙剖面", Float) = 0
        [Toggle(_REPLICA_MODE_FRONTWATER)]   _ModeFrontWater  ("模式：FrontWater 水剖面", Float) = 0

        // ---- Strata 层理（sa0 pid212，512² 仅 R；property 供装配绑定 Strata_Ref.png）----
        _StrataTex         ("Strata 层理图（512² 仅 R）", 2D) = "gray" {}
        // cb0[113].x（int→float）：Strata V 偏移，疑水位 level=1.0；TODO：A 流对账
        _StrataLevelOffset ("Strata 深度偏移（疑水位）", Float) = 1.0
        // cb0[127]/[128] = 运行时全局 sandColor/sandColor2（Ground.cs SetGlobals）。
        // 默认取主菜单 Carbonate 预设（D-sand.md §4.2 三重交叉验证）：
        // color=(0.9255,0.8863,0.7255)、color2=(0.8353,0.8078,0.6902)。
        // （Borders 材质序列化值 (0.920,0.846,0.695)/(0.761,0.638,0.448) 是惰性
        //  镜像，运行时被预设覆写——勿用。）
        _SandColor         ("沙色一（Carbonate color）", Vector) = (0.9255, 0.8863, 0.7255, 1)
        _SandColor2        ("沙色二（Carbonate color2）", Vector) = (0.8353, 0.8078, 0.6902, 1)

        // ---- FrontWater 剖面带着色（cb0[131]/[132]，与 Lit/Water 同槽）----
        _WaterColor        ("水色（waterColor 全局，cb0[131] t=1/3 运行时；序列化 0.9603… 惰性残留）", Vector) = (0.7835, 0.9486, 1.0, 1)
        // σ = R 抓帧运行时真值 cb0[132]（R-framedump 定案，交接§3 R 行；A 流 σ≡0
        // 静态结论已被 R 推翻）。tint 域，有效消光 6σ/m=(5.958,1.052,0.335)。
        // 装配侧 WaterLabSetup.ReplicaSigma 同源下发（WaterLabReplicaGlobals 的
        // _WaterLabSigma 同值管水面）——抓帧对账若改，三处同改。
        _Sigma             ("吸收 σ（R 抓帧 cb0[132] 真值）", Vector) = (0.9930291, 0.1753813, 0.0557871, 0)

        // ---- 双光 Lambert（cb0[123..126] 槽语义；颜色为 t=1/3 菜单真值）----
        _MoonDirection     ("月光方向（光传播方向；菜单禁用）", Vector) = (0, -1, 0, 0)
        _MoonColor         ("月光色（t=1/3 = 黑）", Vector) = (0, 0, 0, 1)
        _Ambient           ("环境光（t=1/3 运行时真值，cb0[40]）", Vector) = (0.698039, 0.564706, 0.569923, 1)
    }
    // 太阳色不进 Properties：复用实验台全局 _WaterLabSunDir（指向太阳） +
    // 下面 CBUFFER 外的 _WaterLabSunColor（WaterLab 复刻全局族，I1 统一下发）。

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #pragma target 3.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // LerpWhiteTo 在 core CommonMaterial（Shadows.hlsl 单独 include 不够——Ocean 踩过同款）
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float  _Mode;
            float  _ModeBack;
            float  _ModeFrontGround;
            float  _ModeFrontWater;
            float  _StrataLevelOffset;
            float4 _SandColor;
            float4 _SandColor2;
            float4 _WaterColor;
            float4 _Sigma;
            float4 _MoonDirection;
            float4 _MoonColor;
            float4 _Ambient;
        CBUFFER_END

        // ---- 实验台全局（沿用 Ocean shader 现有全局名，WaterLabSimulation 发布）----
        TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
        TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
        float4 _WaterLabOrigin;        // xz=域原点，y=域边长（WorldToDomainUv 同 Ocean）
        float4 _WaterLabSunDir;        // 指向太阳（= −原作 sunDirection）

        // ---- WaterLab 复刻全局族（I1/I5 统一下发；菜单 t=1/3 真值见 Properties）----
        float4 _WaterLabSunColor;

        // Strata 层理贴图（材质 property；仅 R 通道有效）
        TEXTURE2D(_StrataTex); SAMPLER(sampler_StrataTex);

        // 本仓高度场编码（Ocean.DecodeEta 同式）：存 (e+3)/5，读 e·5−3
        float ReplicaDecodeEta(float e)
        {
            return e * 5.0 - 1.0;
        }

        // 域 UV（与 Ocean.WorldToDomainUv 同式）
        float2 ReplicaWorldToDomainUv(float3 positionWS)
        {
            return (positionWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
        }

        // 双线性采样解码后的地面高程（m）
        float ReplicaSampleGround(float2 uv)
        {
            return ReplicaDecodeEta(SAMPLE_TEXTURE2D(_WaterLabGroundTex,
                sampler_WaterLabGroundTex, uv).r);
        }

        // 双线性采样水量（m）＝ eta − ground（仿射解码可交换性见文件头）
        float ReplicaSampleWater(float2 uv)
        {
            float eta    = ReplicaDecodeEta(SAMPLE_TEXTURE2D(_WaterLabHeightTex,
                               sampler_WaterLabHeightTex, uv).r);
            float ground = ReplicaSampleGround(uv);
            return eta - ground;
        }

        struct ReplicaBordersAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
        };

        // VS：Back@238 / GroundFront@322 / WaterFront@310 三者同构（世界坐标 +
        // 世界法线（等比缩放假设，rsq 归一）+ 网格 UV；Back VS 无法线输出但
        // WaterFront 输入签名已带 TEXCOORD2，统一输出由 PS 择用）。
        struct ReplicaBordersVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float2 uv         : TEXCOORD2;
        };

        ReplicaBordersVaryings ReplicaBordersVertex(ReplicaBordersAttributes IN)
        {
            ReplicaBordersVaryings OUT;
            float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
            OUT.positionWS = positionWS;
            OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
            OUT.uv = IN.uv;
            OUT.positionCS = TransformWorldToHClip(positionWS);
            return OUT;
        }
        ENDHLSL

        // ================================================================
        // pass0：Lit/BordersBack（pid581，structure.json：仅此一 pass，
        // Cull Front / ZWrite On / ZTest LEqual）。PS @2478（60 指令，全变体）。
        // ================================================================
        Pass
        {
            Name "Back"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma shader_feature_local _REPLICA_MODE_BACK
            #pragma target 3.5
            #pragma vertex ReplicaBordersVertex
            #pragma fragment ReplicaBordersFragmentBack

            half4 ReplicaBordersFragmentBack(ReplicaBordersVaryings IN) : SV_Target
            {
                #ifndef _REPLICA_MODE_BACK
                    clip(-1);   // 未勾选本模式：整 pass 出空（材质三选一）
                #endif

                // @2478 #1-45：网格 UV → 水网格（cb0[141]）手写双线性 →
                // A=水量（×1e-6）、B=地面（×1e-6）；本仓=网格 UV 直接采纹理。
                float2 uv = IN.uv;
                float  water  = ReplicaSampleWater(uv);
                float  ground = ReplicaSampleGround(uv);

                // #41-45：surface = (max(water−0.05,0) + ground) × (water>0.05)
                //（干湿门控，@2478 的 lt 0.05 + and 0x3f800000；FrontWater 无此门——勿抄错）
                float  wet     = (water > 0.05) ? 1.0 : 0.0;
                float  surface = (max(water - 0.05, 0.0) + ground) * wet;

                // #46-47：discard worldY > surface（后壁剪影随沙面+水面起伏）
                clip(surface - IN.positionWS.y);

                // #48：恒量白——无光照无贴图无材质色（PS 逐指令证实）
                return half4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }

        // ================================================================
        // pass0：Lit/BordersGroundFront（pid584，Cull Back / ZWrite On）。
        // PS @3890（101 指令，全变体）。
        // ================================================================
        Pass
        {
            Name "FrontGround"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma shader_feature_local _REPLICA_MODE_FRONTGROUND
            #pragma target 3.5
            #pragma vertex ReplicaBordersVertex
            #pragma fragment ReplicaBordersFragmentFrontGround

            half4 ReplicaBordersFragmentFrontGround(ReplicaBordersVaryings IN) : SV_Target
            {
                #ifndef _REPLICA_MODE_FRONTGROUND
                    clip(-1);
                #endif

                // @3890 #1-25：网格 UV × 地面网格（cb0[139]）双线性 → 地面高 g
                float ground = ReplicaSampleGround(IN.uv);

                // #26-27：discard worldY > ground；depth = worldY − g（剖面深度）
                clip(ground - IN.positionWS.y);
                float depth = IN.positionWS.y - ground;

                // #28-55：水量双线性——原作用世界坐标映射 worldXZ/cb0[116].xz+0.5
                //（add_sat 饱和到 [0,1]），网格 cb0[141]；本仓同映射到域 UV。
                float2 wuv = saturate(ReplicaWorldToDomainUv(IN.positionWS));
                float  water = ReplicaSampleWater(wuv);

                // #56-57：波影 exp2(−0.721348·水量)（≡e^(−水量/2)，见文件头）
                float waveShadow = exp2(water * -0.721348);

                // #58-64：双光 Lambert，波影只衰减太阳项。
                // 原作 sat(N·−cb0[123])×cb0[125] + sat(N·−cb0[124])×cb0[126]；
                // _WaterLabSunDir 指向太阳 = −原作 sunDirection，符号已换算。
                float ndlSun  = saturate(dot(IN.normalWS, _WaterLabSunDir.xyz));
                float ndlMoon = saturate(dot(IN.normalWS, _MoonDirection.xyz));
                float3 lit = ndlSun * _WaterLabSunColor.rgb * waveShadow
                           + ndlMoon * _MoonColor.rgb;

                // #65-70：Strata——U=(worldX+worldZ)×0.02（环向参数化，前/侧面连续）、
                // V=(剖面深度+cb0[113].x)×0.5，采 t2.R
                float2 strataUV;
                strataUV.x = dot(IN.positionWS.xz, float2(0.02, 0.02));
                strataUV.y = (depth + _StrataLevelOffset) * 0.5;
                float r = SAMPLE_TEXTURE2D(_StrataTex, sampler_StrataTex, strataUV).r;

                // #71-75：色 = saturate(lerp(sandColor, sandColor2, r))（@3890 的
                // mad_sat）；输出 = strata×(光) + strata×环境（cb0[40]）
                float3 albedo = saturate(lerp(_SandColor.rgb, _SandColor2.rgb, r));
                float3 col = albedo * lit + albedo * _Ambient.rgb;
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ================================================================
        // pass0：Lit/BordersWaterFront（pid577，Cull Back / ZWrite **Off**）。
        // PS @3970（78 指令，全变体）。
        // ================================================================
        Pass
        {
            Name "FrontWater"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off

            HLSLPROGRAM
            #pragma shader_feature_local _REPLICA_MODE_FRONTWATER
            #pragma target 3.5
            #pragma vertex ReplicaBordersVertex
            #pragma fragment ReplicaBordersFragmentFrontWater

            half4 ReplicaBordersFragmentFrontWater(ReplicaBordersVaryings IN) : SV_Target
            {
                #ifndef _REPLICA_MODE_FRONTWATER
                    clip(-1);
                #endif

                // @3970 #1-46：与 Back@2478 同款（网格 UV → 水量/地面），
                // surface = max(water−0.05,0) + ground——**无干湿门控**（与 Back 唯一
                // 公式差异，@3970 #47 前无 lt 0.05 项）
                float2 uv = IN.uv;
                float  water  = ReplicaSampleWater(uv);
                float  ground = ReplicaSampleGround(uv);
                float  surface = max(water - 0.05, 0.0) + ground;

                // #47-50：只画 [ground, ground+max(water−0.05,0)] 带状水体竖剖面
                //（带顶边 = 水线：光程≈0 → 背景原色发亮；带底光程最大 → 最暗）
                clip(IN.positionWS.y - ground);     // discard worldY < ground
                clip(surface - IN.positionWS.y);    // discard worldY > surface

                // #51-65：屏幕空间背景重建 + 吸收。
                // 屏幕UV = fragcoord×rcp(cb0[5].xy)；采场景色（t0=_CameraOpaqueTexture）
                // 与深度（t1.y=_CameraDepthTexture）；原作经 cb0[66..69]（逆 VP 行组）
                // +透视除重建壁后背景世界坐标，d=|背景−片元|，
                // absorb = exp2(−d·σ·8.65617)×waterColor，o = bg×absorb。
                // 本仓用 URP 等价链：SV_Position 像素坐标→UV（原作 cb0[12] 的 RT 翻转
                // 校正由 URP 采样约定内建）、ComputeWorldSpacePosition(UNITY_MATRIX_I_VP)
                // 即 cb0[66..69] 行组合+透视除。
                float2 screenUV = IN.positionCS.xy * rcp(_ScreenParams.xy);
                float  rawDepth = SampleSceneDepth(screenUV);
                float3 bgColor  = SampleSceneColor(screenUV);
                float3 bgWorld  = ComputeWorldSpacePosition(screenUV, rawDepth,
                                                            UNITY_MATRIX_I_VP);
                float  d = distance(bgWorld, IN.positionWS);
                float3 absorb = exp2(-d * _Sigma.rgb * 8.65617) * _WaterColor.rgb;
                return half4(bgColor * absorb, 1.0);
            }
            ENDHLSL
        }

        // ================================================================
        // ShadowCaster：FrontGround pass1（@8122/@9414 VS + @11298 PS）与
        // FrontWater pass1（@7146/@8438 VS + @10322 PS）的合并移植。
        // Back 无阴影 pass（structure.json：单 pass）→ Back 模式下整 pass 出空。
        // 原作 pass1 Cull：FrontGround=Back、FrontWater=**Front**；单 pass 合一取
        // Cull Back（待 I5 出图对账轮廓差异）。bias 同构：沿光推 + 法线偏移 + 近面收口
        //（原作 VS 手写 ApplyShadowBias：+sunDirection·cb0[133].x，+N·(1−sat(N·L))·
        // cb0[133].y，min(z, w)；URP ApplyShadowBias 同式，方向取 _LightDirection）。
        // @11298 的 A 项 12 位字段缺口见下方 TODO 注释。
        // ================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma shader_feature_local _REPLICA_MODE_BACK
            #pragma shader_feature_local _REPLICA_MODE_FRONTGROUND
            #pragma shader_feature_local _REPLICA_MODE_FRONTWATER
            #pragma target 3.5
            #pragma vertex ReplicaBordersShadowVertex
            #pragma fragment ReplicaBordersShadowFragment

            // URP 惯例：由使用方声明（Shadows.hlsl 只给 ApplyShadowBias）
            float3 _LightDirection;

            struct ReplicaShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;   // @11298/@10322 解码用网格 UV
            };

            ReplicaShadowVaryings ReplicaBordersShadowVertex(ReplicaBordersAttributes IN)
            {
                ReplicaShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionWS;
                OUT.uv = IN.uv;
                OUT.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 ReplicaBordersShadowFragment(ReplicaShadowVaryings IN) : SV_Target
            {
                #if defined(_REPLICA_MODE_BACK) || (!defined(_REPLICA_MODE_FRONTGROUND) && !defined(_REPLICA_MODE_FRONTWATER))
                    // Back 无阴影 pass；无模式 = 未配置 → 出空
                    clip(-1);
                #endif

                #ifdef _REPLICA_MODE_FRONTGROUND
                    // @11298 逐指令：网格 UV × ground 缓冲（cb0[139]，t0 offset8）双线性，
                    // 每格出两个量——B=24bit 地面解码（word0[12:24)+word1[0:12)）×1e-6；
                    // A=word0[0:12)（**12 位字段**）×0.002466；surface=max(A,B)，小于
                    // worldY 则 discard（水面以下沙壁不投影）。
                    // 【TODO 本仓缺口】A 的 12 位字段身份未定（H 草稿误记为 t1 水量
                    // 22bit×0.002466；缓冲打包显示它是 ground 缓冲内嵌场，疑 sandAmount
                    // 或残留水）——本仓 _WaterLabGroundTex 单通道无此数据，置 0：
                    // 阴影轮廓 = 地面。待 I7 扩通道或抓帧定身份后补齐。
                    float g = ReplicaSampleGround(IN.uv);
                    float a12 = 0.0;             // 原作 = word0[0:12) × 0.002466，暂缺
                    float surfaceG = max(a12, g);
                    clip(surfaceG - IN.positionWS.y);
                #endif

                #ifdef _REPLICA_MODE_FRONTWATER
                    // @10322：表面计算同 pass0（×1e-6 定标，网格 UV），
                    // discard worldY>surface → 水体柱投影
                    float w2 = ReplicaSampleWater(IN.uv);
                    float g2 = ReplicaSampleGround(IN.uv);
                    float surfaceW = max(w2 - 0.05, 0.0) + g2;
                    clip(surfaceW - IN.positionWS.y);
                #endif

                return 0;   // 原 PS 输出 (0,0,0,0)（深度通道无色）
            }
            ENDHLSL
        }
    }
    Fallback Off
}
