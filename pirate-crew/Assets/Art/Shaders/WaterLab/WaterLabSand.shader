// ============================================================================
// WaterLabSand.shader —— 实验水体（W3）：沙滩着色。
//
// 【原作对齐口径：Lit_Sand PS @34958 逐条，逆向档案 §10.2】
//   1) albedo = 干沙色 → 湿沙色（按水量/湿度曲线）→ 水下亮沙；**没有层理/沙纹/
//      卵石 albedo**。原作 `Sand` 材质的 `sandMap` 是 1024² RG 流噪声：R 是逐 texel
//      白噪声（std 0.161，worldXZ×0.3），只给沙粒明暗；G 是低频流场，供焦散 UV
//      扰动。`Strata` 纹理只被水岸边框材质使用，不在 Lit_Sand 里。
//   2) 沙面法线来自**地形高度场**（Ground.UpdateTangent 的 packed 8-bit 法线），
//      不是法线贴图。本仓地形网格直接由 TerrainHeight 生成，法线取几何法线即可。
//   3) 直射项 = ndl × 太阳色 ×（阴影原值 × exp(-0.721348×水深)）；环境项不采影。
//   4) 焦散在沙侧（原作烘焙 causticsMap），本仓用细节法线散度近似。
//   5) 闪沙 = 噪声 × |太阳色| × exp(-1.731234×湿度) × 坡度门控。
//
// 【本仓唯一保留的近似】焦散源用 `_WaterLabDetailNormal` 求散度；原作是
//   Texture3D 烘焙体。其余颜色/湿度/噪声项按上述原作结构落笔。
// ============================================================================

Shader "PirateCrew/WaterLab/Sand"
{
    Properties
    {
        // 【沙色定标依据】对着 Steam 高清头图逐点量测：参考受光干沙
        // = (253,229,169)（线性 (0.982,0.784,0.397)）。按单次显示编码反解：
        // albedo sRGB (1.0,0.92,0.69) → 线性 (1.0,0.831,0.434)，
        // × 光照核 (0.992,0.944,0.902) ≈ 参考线性值。
        _DrySandColor       ("干沙色（按参考头图受光沙反定）", Color) = (1.0, 0.92, 0.69, 1.0)
        _WetSandColor       ("湿沙色（同一光照核下的湿/暗带）", Color) = (0.82, 0.72, 0.52, 1.0)
        _UnderwaterSandColor ("水下沙色（同族偏暖，水色会把它拉回绿松石）", Color) = (1.0, 0.93, 0.72, 1.0)

        // 沙粒噪声：原作 sandMap.R 是 1024² 白噪声，采样世界尺度 ×0.3（纹理周期
        // 3.33 m，单 texel ≈3.3 mm），以 normal.y 加权调制颜色（±0.2×n.y）。
        _GrainStrength      ("沙粒噪声强度（原作 ±0.2×n.y）", Range(0.0, 0.6)) = 0.20
        _GrainScale         ("沙粒噪声尺度（1/m）", Range(20.0, 600.0)) = 300.0

        // 闪沙：原作 3D 噪声 × exp(-1.731×湿度)；LDR 下 0.10 像满地下雪，0.02 只留
        // 零星高光。尺度取 14/m（约 7 cm 一格并旋转采样），作为远距可读的稀疏闪点。
        _SparkleStrength    ("闪沙强度", Range(0.0, 0.5)) = 0.02
        _SparkleScale       ("闪沙尺度（1/m）", Range(2.0, 100.0)) = 14.0

        _LightScale         ("显示链曝光因子", Range(0.2, 3.0)) = 1.08
        _CausticGain        ("焦散强度（水面坡度散度系数）", Range(0.0, 4.0)) = 0.6
        _CausticScale       ("焦散世界尺度（1/m）", Range(0.02, 2.0)) = 0.5
        _CausticSampleOffset ("焦散采样偏移（UV）", Range(0.0005, 0.05)) = 0.0156
        _WaterLabDetailNormal ("细节法线（水面小尺度坡度场＝焦散源）", 2D) = "gray" {}

        // 天光项：原作走 HDR+GI，本仓用常数项近似。旧值只保留天空色、暗部太黑；
        // 抬高到接近“天空 + 沙面反弹”的合成亮度，红色分量同步抬起，阴影不再发青。
        _AmbientColor       ("环境色（原作天光 + GI 的常数近似）", Color) = (0.18, 0.20, 0.22, 1.0)
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
                float  _GrainStrength;
                float  _GrainScale;
                float  _SparkleStrength;
                float  _SparkleScale;
                float  _LightScale;
                float  _CausticGain;
                float  _CausticScale;
                float  _CausticSampleOffset;
                float3 _AmbientColor;
                float3 _SunTint;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabWetTex);    SAMPLER(sampler_WaterLabWetTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);

            float DecodeGround(float e) { return e * 5.0 - 3.0; }

            float2 WorldToDomainUv(float3 worldPos)
            {
                return (worldPos.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
            }

            float Hash12(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 旋转采样坐标：打断轴对齐格点在屏幕上的行/列读感。
            float2 Rotate2(float2 p, float a)
            {
                float s = sin(a), c = cos(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
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

                float eta = DecodeGround(SAMPLE_TEXTURE2D(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, uv).r);
                // 边界权重 ×10（原作 ×20）：×4 的过渡带宽约 25 cm，水线糊成一条灰带；
                // 岸线现在沿 z 摆动，需要更利落的湿/水下换色。
                float underwater = (_WaterLabSimEnabled > 0.5)
                    ? saturate((eta - ground) * 10.0) : 0.0;
                float3 albedo = lerp(_DrySandColor, _WetSandColor, wet * (1.0 - underwater));
                albedo = lerp(albedo, _UnderwaterSandColor, underwater);

                float3 n = normalize(IN.normalWS);

                // 沙粒明暗：原作 sandMap.R（白噪声）以 normal.y 加权调制颜色。
                // 纹理在远处由 mip 链平均掉；本仓无 1024² 沙噪声资产，用逐 3.3 mm
                // 哈希噪声等价，并按距离淡出，避免远距摩尔成规则点阵。
                float dist = length(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float grainFade = saturate(1.0 - (dist - 8.0) / 32.0);
                float grain = Hash12(floor(IN.positionWS.xz * _GrainScale)) - 0.5;
                albedo *= 1.0 + grain * 2.0 * _GrainStrength * saturate(n.y) * grainFade;

                // 闪沙（原作 Lit_Sand PS 实证：噪声 × 太阳亮度 × exp(−1.731234×湿度)
                // × 坡度门控，加进 albedo 随光照走——干沙闪、湿沙哑）。
                float2 spCell = floor(Rotate2(IN.positionWS.xz, 0.43) * _SparkleScale);
                float spN = Hash12(spCell);
                float moisture = saturate(wet * 5.0);
                moisture = moisture * moisture * (3.0 - 2.0 * moisture); // smoothstep 平滑
                float sparkle = spN * _SparkleStrength
                              * exp(-1.731234 * moisture) * (1.0 - underwater)
                              * saturate(n.y * 4.0 - 3.0);
                albedo = saturate(albedo + sparkle);

                // 光照（逆向档案 §10.2 = Lit_Sand PS 逐条）：直射项 = ndl × 太阳 ×
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

                // ---- 焦散（对标原作 Lit_Sand 输出结构：
                //   o0 = albedo×(直射×阴影×水柱 + 天光) + albedo×causticsMap）
                // 原作焦散在**沙侧**（材质槽 causticsMap，是一张烘焙 Texture3D）、水侧没有
                // 焦散项；水体只是把「已带焦散的沙渲染」折射+衰减过来 → 本仓同构：这里加，
                // 水体 PS 不再加（否则双重计入）。
                // 【源：水面坡度场的散度】光被波面折射汇聚处（−∇·∇h = −∇²h）变亮——这是
                // 真实焦散网的物理机制，其空间频率来自水面**小尺度**波纹。
                // 模拟侧 η 场只有长涌浪：它的 2 格拉普拉斯 p99 仅 0.04–0.12（放大 6× 才
                // ±30% 调制），且峰值全挤在岸线最陡处 → 放大后成一条硬边而不是网
                //（2026-10-18 实测）。故改用**细节法线贴图**（水面小尺度坡度场）求散度：
                // 四个偏移点采样 → 中心差分 → 汇聚焦散。与水体 PS 的细节法线同源同尺度。
                float caustic = 0.0;
                if (_WaterLabSimEnabled > 0.5)
                {
                    float2 cs = IN.positionWS.xz * _CausticScale + _TimeParameters.x * float2(0.013, -0.011);
                    float2 e = _CausticSampleOffset.xx;
                    float2 nxA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs - float2(e.x, 0)).rg * 2.0 - 1.0;
                    float2 nxB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs + float2(e.x, 0)).rg * 2.0 - 1.0;
                    float2 nyA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs - float2(0, e.y)).rg * 2.0 - 1.0;
                    float2 nyB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, cs + float2(0, e.y)).rg * 2.0 - 1.0;
                    float div = (nxB.x - nxA.x) + (nyB.y - nyA.y); // ∇·(n.xz) ∝ ∇²h
                    caustic = -div * _CausticGain * underwater;
                }
                col += albedo * caustic;

                return half4(col * _LightScale, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
