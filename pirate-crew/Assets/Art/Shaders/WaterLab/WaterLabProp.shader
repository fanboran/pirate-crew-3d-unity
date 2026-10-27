// ============================================================================
// WaterLabProp.shader —— 水底参照物（礁石/卵石/海藻球）。
//
// 【为什么要它】水的「透明感」靠**透过水看到水底物体**来传达：只有沙纹时，
// 沙本身是亮的、水体只是轻微调色，观感上就是「一片亮沙」而不是「一汪清水」。
// 参考游戏的水底有礁石/珊瑚/海草（深色、有轮廓、随焦散明暗）——这些才是透明度的载体。
//
// 【光照同沙面】与原作 Lit_Sand 输出结构同式（直射×阴影×水柱 + 天光 + 焦散），
// 使礁石与沙面处于同一套光照与水体衰减下、不脱层。走**不透明队列**：
// 水体（Transparent+10）采样 _CameraOpaqueTexture 时已含它们 → 被折射+吸收。
// 每像素还乘显示链曝光因子（参考有 tonemap、本仓直出，见 WaterLabSand 同名说明）。
// ============================================================================

Shader "PirateCrew/WaterLab/Prop"
{
    Properties
    {
        _BaseColor          ("基础色（线性；按原作 Imposter 色板反定）", Color) = (0.42, 0.40, 0.38, 1.0)
        _AmbientColor       ("环境色（原作天光 + GI 的常数近似）", Color) = (0.18, 0.20, 0.22, 1.0)
        _SunTint            ("太阳色（原作太阳平行光实测）", Color) = (1.0, 0.9709, 0.9403, 1.0)
        _LightScale         ("显示链曝光因子（与沙面同核，定标见 WaterLabSand）", Range(0.2, 3.0)) = 1.08
        _ShadowStrength     ("阴影采样强度（1=正常；0=调试关闭）", Range(0.0, 1.0)) = 1.0
        _CausticGain        ("焦散强度（水面坡度散度系数）", Range(0.0, 4.0)) = 0.6
        _CausticScale       ("焦散世界尺度（1/m）", Range(0.02, 2.0)) = 0.5
        _CausticSampleOffset ("焦散采样偏移（UV）", Range(0.0005, 0.05)) = 0.0156
        _WaterLabDetailNormal ("细节法线（水面小尺度坡度场＝焦散源）", 2D) = "gray" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   PropVertex
            #pragma fragment PropFragment

            // 主光阴影（同沙面：比较采样）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _BaseColor;
                float3 _AmbientColor;
                float3 _SunTint;
                float  _LightScale;
                float  _ShadowStrength;
                float  _CausticGain;
                float  _CausticScale;
                float  _CausticSampleOffset;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);

            float DecodeEta(float e) { return e * 5.0 - 1.0; }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings PropVertex(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 PropFragment(Varyings IN) : SV_Target
            {
                float2 uv = (IN.positionWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
                float2 sUv = saturate(uv);
                float ground = DecodeEta(SAMPLE_TEXTURE2D(
                    _WaterLabGroundTex, sampler_WaterLabGroundTex, sUv).r);
                float eta = DecodeEta(SAMPLE_TEXTURE2D(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, sUv).r);
                float waterDepth = max(eta - ground, 0.0);

                float3 n = normalize(IN.normalWS);
                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25)
                    ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);
                // 半兰伯特：低模 prop 是平面着色，背光面若按硬 ndl 会整面只剩很低的
                // 环境项、读成一团黑；原作靠 GI/天光把背面托起来，这里用半兰伯特近似。
                float ndl = saturate(dot(n, sunDir) * 0.5 + 0.5);
                float sh = lerp(1.0,
                    MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS)),
                    _ShadowStrength);
                float column = exp2(-0.721348 * waterDepth);  // exp2（X 流）：e^(−0.5·d)
                float3 col = _BaseColor * (_AmbientColor + _SunTint * ndl * sh * column);

                // 焦散（同沙面源与定标：水面坡度散度，水面上一节见 WaterLabSand 的说明）
                if (_WaterLabSimEnabled > 0.5)
                {
                    float2 cs = IN.positionWS.xz * _CausticScale + _TimeParameters.x * float2(0.013, -0.011);
                    float2 e = _CausticSampleOffset.xx;
                    float2 nxA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs - float2(e.x, 0)).rg * 2.0 - 1.0;
                    float2 nxB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs + float2(e.x, 0)).rg * 2.0 - 1.0;
                    float2 nyA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs - float2(0, e.y)).rg * 2.0 - 1.0;
                    float2 nyB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs + float2(0, e.y)).rg * 2.0 - 1.0;
                    float div = (nxB.x - nxA.x) + (nyB.y - nyA.y);
                    col += _BaseColor * (-div * _CausticGain * saturate(waterDepth * 4.0));
                }

                // 形体可读性：水柱把 prop 压成剪影后，靠“顶部提亮 + 边缘光”把
                // 枝/叶/石的轮廓从背景水里拉出来。lift 用 prop 相对当地沙面的高度，
                // 不依赖 mesh 局部坐标；rim 只加在掠射边缘。两者只作用于水下，
                // 干滩的海星/滩草保持原色。
                float underwaterMask = saturate(waterDepth * 4.0);
                float lift = saturate((IN.positionWS.y - ground) / 0.70);
                col *= lerp(1.0, lerp(0.75, 1.30, lift), underwaterMask);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 2.0) * 0.30;
                col += _BaseColor * _SunTint * rim * underwaterMask;

                return half4(col * _LightScale, 1.0);
            }
            ENDHLSL
        }

        // 投影 Pass：低模 props 有形体分离和接触影，才不会读成贴着沙面的暗色团。
        // 直接复用 URP 官方 ShadowCasterPass（含 Normal Bias / Punctual 分支）。
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里；单独 include ShadowCasterPass
            // 不够（沙 shader 同款坑，见 WaterLabSand 的 include 次序）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
