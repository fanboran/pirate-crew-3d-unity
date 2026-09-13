// ============================================================================
// PirateAmbientWind.shader —— 环境动效「顶点风摆」表面（植被 / 旗帜 / 帆布 / 缆绳垂片）
//
// 【解决什么问题】场景里的棕榈叶、灌木、草丛、旗帜在静止帧里像贴纸（任务书：风是"活着"的最低门槛）。
//   本 shader 在**顶点阶段**按世界坐标做正弦摆动，因此：
//     · 不需要骨骼 / SkinnedMesh / 逐对象 Transform 动画；
//     · 与既有静态合并网格兼容（SceneArt 的 Foliage / FlagRed / FlagBlue / Cloth 都是整组合并的
//       单网格，无法用 Transform 逐株摆——只有顶点动画能在不拆网格的前提下让它们动起来）。
//
// 【与既有 4 个 shader 的关系】本文件是**新增**，不改 PirateSurface / PirateTerrain / PirateWater /
//   PirateOutline。它的光照最短路径刻意照抄 PirateSurface 已在本工程图形界面编译通过的那一套
//   （Core.hlsl → Lighting.hlsl → Shadows.hlsl，见 docs/描边Shader调试.md §八-1 的事故复盘）。
//
// 【ShadowCaster Pass（与本体共享同一份风摆位移）】风摆 shader 现在**有** ShadowCaster Pass，
//   WindShadowVertex 复用 SubShader 级 HLSLINCLUDE 里唯一一份 ApplyWindDisplacement，
//   因此影子形状与摆动的枝叶**严格同相**（不会出现"影子在动、本体不动"的割裂）。
//   —— 早先版本曾因"怕两处位移不一致"故意不写阴影 Pass、并让 AmbientWindBinder 把
//      Renderer.shadowCastingMode 设为 Off；根因是"位移代码有两份"，现在用 HLSLINCLUDE
//      从结构上消除了这个风险，绑定器也已改回 On（植被重新投影 + 受影）。
//
// 【本体接收阴影】ForwardLit 声明了 _MAIN_LIGHT_SHADOWS/_MAIN_LIGHT_SHADOWS_CASCADE，
//   用 TransformWorldToShadowCoord + GetMainLight(shadowCoord) 把主光阴影衰减乘进直射项，
//   故植被/旗帜能接收其他物件（棕榈、箱桶、单位）投下的影子；SH 环境光**不**乘阴影
//   （照抄 PirateSurface.shader:451-454 的取舍），阴影里约为受光处的 0.6~0.7 亮度、不死黑。
//
// 【Pass / LightMode 唯一性（URP 陷阱）】本 shader 有两个 Pass，LightMode 互不相同：
//     ForwardLit   = "UniversalForward"
//     ShadowCaster = "ShadowCaster"
//   额外 Pass 若与既有 Pass 撞 LightMode 会被 URP **静默丢弃**（Console 无报错），
//   完整复盘见 docs/描边Shader调试.md §八-2，故 ShadowCaster 不得复用 UniversalForward。
//
// 【_DebugMode 分档（图形学调试截图规范）】
//   0 = 正常；1 = 基色（无光照无雾）；2 = 风摆权重灰度；3 = 世界法线。
//   ⚠ 本 shader 尚未在图形界面编辑器里跑过 read_console —— 首次进编辑器必须核对
//     "0 条 shader error"，静态清单只能证明符号存在（AGENTS.md 图形学调试截图规范）。
//
// 【[CHECK] 本地 URP 14.0.12 包内逐条核对（与 PirateSurface 同一套符号）】
//   GetVertexPositionInputs      -> Core.hlsl / ShaderVariablesFunctions.hlsl
//   TransformObjectToWorld       -> SpaceTransforms.hlsl（经 Core.hlsl）
//   TransformWorldToHClip        -> SpaceTransforms.hlsl
//   ComputeFogFactor / MixFog    -> ShaderVariablesFunctions.hlsl:329 / :414
//   GetMainLight(float4)         -> RealtimeLights.hlsl:118（经 Lighting.hlsl 引入）
//   TransformWorldToShadowCoord  -> Shadows.hlsl:319
//   SampleSH                     -> GlobalIllumination.hlsl:21（经 Lighting.hlsl 引入）
//   Shadows.hlsl 必须放在 Lighting.hlsl **之后**（否则 LerpWhiteTo 未定义，实测报错）。
//   ApplyShadowBias              -> Shadows.hlsl:471（ShadowCaster 自定义顶点用）
//   ShadowCasterPass.hlsl        -> Shaders/ShadowCasterPass.hlsl（提供 Attributes / Varyings /
//                                   ShadowPassFragment 与 _LightDirection / _LightPosition；
//                                   其内部自带 Core.hlsl + Shadows.hlsl）
//   CommonMaterial.hlsl（core）  -> :352/359 定义 LerpWhiteTo；Shadows.hlsl:298 用它却**不**自己
//                                   include 它 —— ShadowCaster Pass 里必须先 include 它，
//                                   否则报 `undeclared identifier 'LerpWhiteTo'`（同 PirateSurface 事故复盘）。
//   **本清单只能证明符号存在**；必须进图形界面编辑器 play 一次 + read_console 确认 0 shader error。
// ============================================================================
Shader "PirateCrew/Ambient/Wind"
{
    Properties
    {
        _BaseColor        ("基色（中间调）", Color) = (0.373, 0.659, 0.235, 1.0)
        _BaseColorDark    ("暗档色（背光/根部）", Color) = (0.200, 0.353, 0.165, 1.0)
        _AmbientStrength  ("环境光强度", Range(0.0, 3.0)) = 1.0
        _Emission         ("自发光强度", Range(0.0, 1.0)) = 0.0

        // ---- 风（数值默认值对应"海风缓推"；运行时由 AmbientWindBinder 按类写入）----
        _WindDirection     ("风向 XZ（自动归一化）", Vector) = (0.92, 0.39, 0.0, 0.0)
        _WindStrength      ("风摆幅（世界单位）", Range(0.0, 1.0)) = 0.09
        _WindSpeed         ("风角速度（弧度/秒）", Range(0.0, 4.0)) = 0.55
        _WindHeight        ("权重高度（世界单位）", Range(0.01, 12.0)) = 1.2
        _WindAnchorY       ("权重锚点 Y（根/悬挂点）", Float) = 0.0
        _WindWeightDirection ("权重方向 1=向上 −1=向下", Range(-1.0, 1.0)) = 1.0
        _WindDensity       ("相位空间密度", Range(0.0, 4.0)) = 0.35
        _WindFlutter       ("高频抖动（叶片沙沙）", Range(0.0, 1.0)) = 0.25

        // 摆幅下限：合并网格里同组既有 5 米高的棕榈叶、也有 0.3 米高的草丛，
        // 只按世界高度加权会让草完全静止（高度权重≈0.01）。给一个下限让矮植被也摆。
        _WindFloor         ("摆幅下限（0=根完全静止）", Range(0.0, 1.0)) = 0.35

        _DebugMode ("调试模式 0=正常 1=基色 2=风权重 3=法线", Range(0.0, 3.0)) = 0.0
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
        LOD 150

        // ====================================================================
        // SubShader 级 HLSLINCLUDE：Core.hlsl + UnityPerMaterial + 唯一一份风摆位移。
        // 它会**前插到本 SubShader 的每个 Pass**，因此 ForwardLit 与 ShadowCaster
        // 用的是同一份 ApplyWindDisplacement —— 影子与本体天然同步（不存在两份位移漂移）。
        // 顺带好处：两个 Pass 都声明了同一份 UnityPerMaterial，满足 SRP Batcher 的布局要求。
        // 【include 顺序】这里只放 Core.hlsl，保证任何 Pass 代码之前已有 TransformObjectToWorld /
        //   TransformWorldToHClip / _Time；Lighting.hlsl、Shadows.hlsl 由 ForwardLit 自己按序引入。
        // ====================================================================
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseColorDark;
            float  _AmbientStrength;
            float  _Emission;
            float4 _WindDirection;
            float  _WindStrength;
            float  _WindSpeed;
            float  _WindHeight;
            float  _WindAnchorY;
            float  _WindWeightDirection;
            float  _WindDensity;
            float  _WindFlutter;
            float  _WindFloor;
            float  _DebugMode;
        CBUFFER_END

        // ------------------------------------------------------------------
        // 风摆位移：返回"已位移的世界坐标"。ForwardLit 与 ShadowCaster 共用本函数。
        // 与 C# 侧 WindRules 的公式保持一致（同相位、同阵风包络），
        // 便于测试用 WindRules.Sway / GustScale 复算 shader 的数值。
        // ------------------------------------------------------------------
        float3 ApplyWindDisplacement(float3 positionWS, out float outWeight)
        {
            // 权重：离锚点越远摆得越大，取平方让根部完全静止（否则整株平移像"滑步"）。
            float signedDist = _WindWeightDirection >= 0.0
                ? (positionWS.y - _WindAnchorY)
                : (_WindAnchorY - positionWS.y);
            float w = saturate(signedDist / max(_WindHeight, 0.01));
            w = w * w;
            // 摆幅下限：矮植被（草丛）也保留一部分摆动，否则在合并网格里完全静止。
            w = _WindFloor + (1.0 - _WindFloor) * w;
            outWeight = w;

            // 相位 = _Time.y × 角速度 + 空间偏移（相邻植株错相，整片植被不会同步抽搐）。
            float phase = _Time.y * _WindSpeed
                        + (positionWS.x + positionWS.z * 0.73) * _WindDensity;

            // 阵风包络（低频），与 WindRules.GustScale 同形：恒为正值，量级 [0.7, 1.0]。
            float gust = 0.70 + 0.30 * sin(phase * 0.31);

            // 主摆 + 次摆 + 高频抖动。
            float sway = sin(phase) + 0.35 * sin(phase * 2.7 + 1.3);
            float flutter = _WindFlutter * sin(phase * 5.3 + positionWS.y * 3.1);

            float offset = (sway + flutter) * _WindStrength * gust * w;

            // 风向自动归一化（零向量时回落到 +X，避免 normalize(0) 产生 NaN）。
            float2 wdir = _WindDirection.xy;
            float wlen = max(length(wdir), 1e-4);
            wdir /= wlen;

            positionWS.xz += wdir * offset;
            return positionWS;
        }
        ENDHLSL

        // ====================================================================
        // Pass 1 / ForwardLit：主光（含阴影）+ SH 环境光 + 雾
        // ====================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 双面：叶片/旗帜/垂片是零厚度贴片，45° 俯视下正反两面都可能被看到。
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   WindVertex
            #pragma fragment WindFragment

            // 主光阴影：三档（无 / 单 cascade / 多 cascade）——植被据此**接收**其他物件的投影。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            // include 顺序照抄 PirateSurface（已实测编译通过的那条链）。
            // Core.hlsl 已由 SubShader 级 HLSLINCLUDE 前插（此处重复 include 被 include guard 吸收）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 必须在 Lighting.hlsl 之后：Shadows.hlsl:298 用 LerpWhiteTo，而该符号来自
            // Lighting.hlsl 内部先引入的 core/CommonMaterial.hlsl（见 PirateSurface 事故复盘）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct AttributesWind
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsWind
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  windWeight : TEXCOORD2;
                real   fogFactor  : TEXCOORD3;
            };

            VaryingsWind WindVertex(AttributesWind IN)
            {
                VaryingsWind OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);

                float weight;
                posWS = ApplyWindDisplacement(posWS, weight);

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                OUT.windWeight = weight;
                return OUT;
            }

            half4 WindFragment(VaryingsWind IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);

                // ---- 调试档 ----
                // 档 1 预期：整片纯基色、无立体感 —— 用于确认顶点风位移是否生效（动帧对比）。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(_BaseColor.rgb, 1.0h);
                // 档 2 预期：根/悬挂点黑、末梢白；若整片同灰度说明锚点或权重方向配错。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(half3(IN.windWeight, IN.windWeight, IN.windWeight), 1.0h);
                // 档 3 预期：R/G/B 平滑渐变（世界法线）。
                if (_DebugMode > 2.5)
                    return half4(half3(normalWS * 0.5 + 0.5), 1.0h);

                // ---- 光照：主光（含阴影）+ SH 环境光 ----
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                // 双面光照：取正反两面 lambert 的较大者（零厚度贴片没有"背面"概念）。
                half ndlFront = saturate(dot(normalWS, mainLight.direction));
                half ndlBack  = saturate(dot(-normalWS, mainLight.direction));
                half ndl = max(ndlFront, ndlBack);

                // 环境光不乘阴影（与 PirateSurface 同口径）：阴影里保留 SH 托底，
                // 直射项乘 shadowAttenuation 后，阴影约为受光处的 0.6~0.7、不会死黑。
                half3 ambient = SampleSH(normalWS) * (half)_AmbientStrength;
                half3 direct = mainLight.color * ndl * (half)mainLight.shadowAttenuation;

                // 背光/根部压到暗档色，让"从根到梢"有明度层次（风格化色阶，非真实 AO）。
                half leafy = saturate(ndl * 0.65h + 0.35h);
                half3 albedo = lerp((half3)_BaseColorDark.rgb, (half3)_BaseColor.rgb, leafy);

                half3 color = albedo * (direct + ambient) + albedo * (half)_Emission;
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / ShadowCaster：把风摆后的顶点投进主光阴影图。
        // 自定义 WindShadowVertex 复用 HLSLINCLUDE 的 ApplyWindDisplacement，
        // 其余（ApplyShadowBias / _LightDirection / _LightPosition / 片元）全部走 URP 官方
        // ShadowCasterPass.hlsl，与 PirateSurface 的 ShadowCaster 同一实现路径。
        // ====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            // 与 ForwardLit 一致：零厚度叶片双面投影，否则影子会缺一半。
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   WindShadowVertex
            #pragma fragment ShadowPassFragment
            // 官方 ShadowCaster 同款：点光/聚光投影时用 _LightPosition，方向光用 _LightDirection。
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // 【include 顺序硬要求】Shadows.hlsl:298 用 LerpWhiteTo（定义在 core 的 CommonMaterial.hlsl，
            //   但 Shadows.hlsl 自己不 include 它）；ShadowCasterPass.hlsl 自带 Core.hlsl + Shadows.hlsl。
            //   故必须先 Core.hlsl → CommonMaterial.hlsl（URP 官方 Lit.shader 走 LitInput.hlsl 也是这个顺序）。
            //   实测报错：undeclared identifier 'LerpWhiteTo' at Shadows.hlsl(298) (on d3d11)。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            // 自定义阴影顶点：先做与 ForwardLit 相同的风摆位移，再走官方的 Normal Bias → 阴影裁剪空间。
            // （ApplyShadowBias: Shadows.hlsl:471；_LightDirection/_LightPosition 由 ShadowCasterPass.hlsl 声明。）
            Varyings WindShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float weight;   // 阴影 Pass 不需要风权重，函数签名要求 out 参数
                positionWS = ApplyWindDisplacement(positionWS, weight);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }
            ENDHLSL
        }
    }

    // 不继承内置回退：回退会把材质悄悄换成 URP/Lit 的纯色外观，掩盖"shader 没找到"的问题。
    Fallback Off
}
