// ============================================================================
// CloudSeaUnderwater.shader —— 云端漫步水下生态：海草 / 珊瑚 / 岩石 / 海星 / 鱼 / 蟹 / 贝
// 共用一支卡通着色 shader（PostLine 前向 + 投影）。
//
// 【为什么单独一支、不走 PostLineToonLit】
//   · 不描边：本 shader 没有 DepthNormals 通道，PostLine 的深度 / 法线纹理里没有这些物体，
//     描边判据看不到它们——鱼群、摇曳的海草周围不会冒出墨线（海草逐帧摆动，描了线必抖）。
//   · 影子照有：带 ShadowCaster 通道，鱼群 / 岩石 / 珊瑚在海床上投出真实的硬影。
//   · 水下不发黑：半兰伯特三档色带，背光面是「固有色 × 冷色」而不是压暗到黑；透出来还隔
//     一层水色，水下小件的固有色必须够亮才读得出。
//
// 【颜色不乘光照全局】阳光受光面 = 固有色 × _HighlightColor，直接对着调色板配。
//   坡面三档（_Bands）× 岛影两档，都是硬切。
//
// 【海草摇摆】位移权重 = UV.x 的平方。海草网格的 UV.x 存的是「离根高度比」（根 0 → 梢 1，
//   Blender 侧 kit_common.Mesher 的逐顶点辅助量）；其余网格没有 UV，UV 恒 0、位移恒 0，
//   所以珊瑚 / 鱼 / 蟹用同一支 shader 也不会晃。前向与投影两个 pass 调同一个位移函数，
//   影子和草同步摆。
//
// 【颜色默认值是 sRGB】ShaderLab 颜色默认值、材质里的颜色属性、Inspector 取色器同一口径：按
//   sRGB 写，引擎上传时转线性。改默认值直接写调色板的 sRGB 数字；脚本 SetColor 不要先 .linear。
// 【调参速查】某件东西看不清 ↔ 调亮材质的 _BaseColor；暗面太死 ↔ 提 _ShadowColor；
//   海草摆幅 / 快慢 ↔ _SwayAmplitude / _SwaySpeed；要定格动画感 ↔ _SwayFps 调到 6~8。
// ============================================================================

Shader "PirateCrew/CloudSea/CloudSeaUnderwater"
{
    Properties
    {
        [Header(Albedo And Bands)]
        _BaseColor ("固有色", Color) = (1, 1, 1, 1)
        _HighlightColor ("受光面色（乘固有色）", Color) = (1.04, 1.02, 0.96, 1)
        _ShadowColor ("背光面色（乘固有色，偏冷）", Color) = (0.6, 0.7, 0.8, 1)
        _Bands ("色带档数", Range(2, 4)) = 3
        _Wrap ("半兰伯特偏移（越大背光面越少）", Range(0.0, 0.5)) = 0.25
        [ToggleUI] _TwoSidedLight ("薄片双面受光（海草开）", Float) = 0

        [Header(Island Shadow)]
        _ShadowStep ("岛影切点（阴影衰减不低于该值算阳光）", Range(0.0, 1.0)) = 0.5
        _IslandShadowTint ("岛影乘色（偏冷青，不压死）", Color) = (0.64, 0.76, 0.86, 1)

        [Header(Sway)]
        _SwayAmplitude ("梢部摆幅（米）", Range(0.0, 0.5)) = 0.10
        _SwaySpeed ("摆动角速度（弧度/秒）", Range(0.0, 6.0)) = 1.2
        _SwayFps ("定帧帧率（次/秒，0 = 连续）", Range(0, 30)) = 0
        _SwayDirection ("摆动方向（世界 XZ）", Vector) = (1, 0.35, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 【SRP Batcher】材质属性的唯一 cbuffer，两个 pass 共享这一份定义。
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _HighlightColor;
            half4 _ShadowColor;
            float _Bands;
            float _Wrap;
            float _TwoSidedLight;

            float _ShadowStep;
            half4 _IslandShadowTint;

            float _SwayAmplitude;
            float _SwaySpeed;
            float _SwayFps;
            half4 _SwayDirection;
        CBUFFER_END

        // 顶点摆动位移（前向与投影共用）。rootWS = 未位移的世界位置。
        float3 CloudSeaSwayOffset(float3 rootWS, float2 uv)
        {
            float weight = uv.x * uv.x;
            if (weight <= 0.0 || _SwayAmplitude <= 0.0)
                return 0.0.xxx;

            // 相位取自世界 XZ：相邻植株不同步，整片海草不会像一块布一起摆。
            // _SwayFps > 0 时时间分格——逐格跳的定格动画感。
            float t = _SwayFps > 0.5 ? floor(_Time.y * _SwayFps) / _SwayFps : _Time.y;
            float swing = sin(t * _SwaySpeed + rootWS.x * 0.9 + rootWS.z * 1.1)
                + 0.35 * sin(t * _SwaySpeed * 2.3 + rootWS.z * 2.7);
            float2 dir = normalize(_SwayDirection.xz + 1e-5);
            return float3(dir.x, 0.0, dir.y) * (swing * _SwayAmplitude * weight);
        }
        ENDHLSL

        // ==================================================================
        // Pass 1 / ForwardLit：三档色带 × 岛影两档。
        // ==================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // 岛影要采主光阴影图：与 PostLineToonLit 同一组关键字（含软阴影四级）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "CloudSeaCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;   // 海草：UV.x = 离根高度比；其余网格无 UV
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += CloudSeaSwayOffset(positionWS, input.uv);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 岛影在艺术像素中心求值（CloudSeaCommon.hlsl），影子边缘落在像素格上。
                float3 ws = CloudSeaArtPixelWorld(input.positionWS, input.positionCS.xy);
                float sun = CloudSeaSunMask(ws, _ShadowStep);

                // 三档色带：半兰伯特量化，最高档 = 1。薄片（海草的背面）开了双面受光则取 |N·L|，
                // 否则整片草的背面会全落进最暗档。
                float3 normalWS = normalize(input.normalWS);
                float ndl = dot(normalWS, CloudSeaLightDir(GetMainLight().direction));
                if (_TwoSidedLight > 0.5)
                    ndl = abs(ndl);
                float halfLambert = saturate(ndl * 0.5 + 0.5 + _Wrap);
                float band = min(floor(halfLambert * _Bands), _Bands - 1.0) / (_Bands - 1.0);

                float3 color = _BaseColor.rgb * lerp(_ShadowColor.rgb, _HighlightColor.rgb, band);
                color *= lerp(_IslandShadowTint.rgb, float3(1.0, 1.0, 1.0), sun);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2 / ShadowCaster：写主光阴影图（鱼群 / 岩石 / 珊瑚 / 海草在海床上的影子）。
        // 顶点着色器是手写的——URP 官方 ShadowPassVertex 不知道海草摆动，影子会和草分离；
        // 这里在官方版本之上只多做一步位移，其余（ApplyShadowBias、近裁面夹取）逐行相同。
        // 【include 顺序硬要求】Core → CommonMaterial → ShadowCasterPass：Shadows.hlsl 的
        // LerpWhiteTo 定义在 CommonMaterial.hlsl 而不自引，漏掉中间那行会报 undeclared identifier。
        // ==================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   CloudSeaShadowVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            // Attributes / Varyings / _LightDirection / _LightPosition 都来自上面的官方 include。
            Varyings CloudSeaShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 rootWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 positionWS = rootWS + CloudSeaSwayOffset(rootWS, input.texcoord);
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

    // 自带两个 pass，不回退内置 Lit（回退会多出深度法线通道，水下物全部被描边）。
    Fallback Off
}
