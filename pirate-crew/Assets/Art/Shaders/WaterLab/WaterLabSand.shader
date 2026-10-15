// ============================================================================
// WaterLabSand.shader —— 实验水体（W3）：沙滩着色（干/湿混色 + 层理 + 程序沙纹）。
//
// 【定位】实验场景 WaterLab 的地形表面。网格顶点由装配器直接放到 TerrainHeight 高度
//   （W1 程序化剖面），本 shader 只做表面着色：
//   1) 干沙色 / 湿沙色按 _WaterLabWetTex（模拟输出，随浪线移动）混合——湿沙深且饱和；
//   2) 沿高程 2–3 条层理色带（Sandcastle Strata 层理的程序化版）；
//   3) 确定性沙纹（与 WaterLabRules.TerrainHeight 的沙纹同式的 sin 组合，读感一致）；
//   4) 光照按 Lit_Sand PS 反汇编复刻：直射 = ndl × (阴影原值 × exp(-0.721×水深))，
//      环境不采影；干沙加闪沙噪声项。逐条证据见逆向档案 §5。
//
// 【单源纪律】湿沙/地形纹理来自 WaterLabSimulation 全局发布；层理断点与
//   WaterLabRules 的地形剖面档位（OffshoreDepth/BeachHeight）对应，改动两侧同步。
// ============================================================================

Shader "PirateCrew/WaterLab/Sand"
{
    Properties
    {
        _DrySandColor       ("干沙色（原作 sandColor 实测）", Color) = (0.9255, 0.8863, 0.7255, 1.0)
        _WetSandColor       ("湿沙色（原作 wetSandColor 实测）", Color) = (0.9060, 0.8104, 0.6333, 1.0)
        _UnderwaterSandColor ("水下沙色（原作 sandColor2 实测）", Color) = (0.9294, 0.8980, 0.7529, 1.0)
        _StrataColor        ("层理色（深带）", Color) = (0.72, 0.61, 0.45, 1.0)
        _StrataBandWidth    ("层理带宽（m）", Range(0.05, 0.5)) = 0.16
        _StrataStrength     ("层理强度", Range(0.0, 1.0)) = 0.35
        _RippleStrength     ("沙纹强度", Range(0.0, 1.0)) = 0.08
        _SparkleStrength    ("闪沙强度", Range(0.0, 0.5)) = 0.10
        _AmbientColor       ("环境色（原作天光平行光实测）", Color) = (0.0627, 0.1333, 0.2196, 1.0)
        _SunTint            ("太阳色（原作太阳平行光实测）", Color) = (1.0, 0.9709, 0.9403, 1.0)
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
            #pragma vertex   SandVertex
            #pragma fragment SandFragment

            // 主光阴影（原作 Lit_Sand 同款比较采样）：地形/物体自阴影；
            // 水下明暗带来自水柱透射项 exp(-0.721×水深)，见片元内注释
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里（单独 include Shadows 不够）
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _DrySandColor;
                float3 _WetSandColor;
                float3 _UnderwaterSandColor;
                float3 _StrataColor;
                float  _StrataBandWidth;
                float  _StrataStrength;
                float  _RippleStrength;
                float  _SparkleStrength;
                float3 _AmbientColor;
                float3 _SunTint;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabWetTex);    SAMPLER(sampler_WaterLabWetTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);

            float DecodeGround(float e) { return e * 5.0 - 3.0; }

            float2 WorldToDomainUv(float3 worldPos)
            {
                return (worldPos.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
            }

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

            Varyings SandVertex(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 SandFragment(Varyings IN) : SV_Target
            {
                float2 uv = WorldToDomainUv(IN.positionWS);
                float ground = DecodeGround(SAMPLE_TEXTURE2D(
                    _WaterLabGroundTex, sampler_WaterLabGroundTex, uv).r);
                float wet = (_WaterLabSimEnabled > 0.5)
                    ? saturate(SAMPLE_TEXTURE2D(_WaterLabWetTex, sampler_WaterLabWetTex, uv).r)
                    : 0.0;

                // 干/湿混色；水下的沙不走湿沙深棕——用被阳光照亮的亮沙（透过水体看的底色，
                // 参考图近岸透底的关键就是亮底沙 + 焦散）
                float eta = DecodeGround(SAMPLE_TEXTURE2D(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, uv).r);
                float underwater = (_WaterLabSimEnabled > 0.5)
                    ? saturate((eta - ground) * 4.0) : 0.0;
                float3 albedo = lerp(_DrySandColor, _WetSandColor, wet * (1.0 - underwater));
                albedo = lerp(albedo, _UnderwaterSandColor, underwater);

                // 层理：缓坡段沿高程的等宽色带（floor 分档）
                float band = frac(ground / _StrataBandWidth);
                float strata = smoothstep(0.42, 0.5, band) * (1.0 - smoothstep(0.5, 0.58, band));
                albedo = lerp(albedo, _StrataColor, strata * _StrataStrength);

                // 程序沙纹（与 WaterLabRules.TerrainHeight 的沙纹同式）
                float ripple = sin(IN.positionWS.x * 1.7 + IN.positionWS.z * 0.6) * 0.85
                             + sin(IN.positionWS.z * 2.3 - IN.positionWS.x * 0.4) * 0.15;
                albedo *= 1.0 + ripple * 0.5 * _RippleStrength;

                // 闪沙（原作 Lit_Sand PS 实证：3D 噪声 × 太阳亮度 × exp(-1.731·湿)×坡度门控，
                // 加进 albedo 随光照走）——本仓简化为高频格点 hash，只给干燥平沙。
                float3 n = normalize(IN.normalWS);
                float2 spCell = floor(IN.positionWS.xz * 14.0);
                float spN = frac(sin(dot(spCell, float2(127.1, 311.7))) * 43758.5453);
                float sparkle = spN * _SparkleStrength
                              * saturate(1.0 - wet) * (1.0 - underwater)
                              * saturate(n.y * 4.0 - 3.0);
                albedo = saturate(albedo + sparkle);

                // 光照（逆向档案 §5 = Lit_Sand PS 逐条）：直射项 = ndl × 太阳 ×
                //   （阴影原值 × exp(-0.721×水深)）。「阴影 × 水柱透射」只乘直射项——
                //   波浪经过时水深振荡 → 水下沙直射光明暗振荡 = 原作「水底波影」的真机制
                //   （原作水面没有 ShadowCaster，blob 只有 2VS+2PS）；天光/环境项不采影不衰减。
                //   阴影用比较采样原值，无软化（min(sh+0.25,1) 只存在于原作水体 PS 散射项）。
                float waterDepth = max(eta - ground, 0.0);
                float column = exp(-0.721348 * waterDepth);
                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25) ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);
                float ndl = saturate(dot(n, sunDir));
                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float3 col = albedo * (_AmbientColor + _SunTint * ndl * sh * column);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
