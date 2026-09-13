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
// 【不写 ShadowCaster Pass（有意）】风摆 Pass 与阴影 Pass 的顶点位置不一致会产生"影子在动、本体不动"
//   的割裂；而给 ShadowCaster 复制一份风摆位移又要在没有图形界面编译验证的前提下手写
//   ApplyShadowBias 路径，风险高于收益。故本 shader 只做前向 Pass，
//   绑定器（AmbientWindBinder）会把使用它的 Renderer 的 shadowCastingMode 设为 Off。
//   代价：植被/旗帜不投影（远景物，观感损失可忽略）。
//
// 【Pass / LightMode 唯一性（URP 陷阱）】本 shader 只有一个 Pass，LightMode = "UniversalForward"，
//   不存在与自身撞名被静默丢弃的问题。
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

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            // include 顺序照抄 PirateSurface（已实测编译通过的那条链）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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

            // ------------------------------------------------------------------
            // 风摆位移：返回"已位移的世界坐标"。
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
    }

    // 无 ShadowCaster：绑定器会把 Renderer.shadowCastingMode 设为 Off（见文件头注释）。
    Fallback Off
}
