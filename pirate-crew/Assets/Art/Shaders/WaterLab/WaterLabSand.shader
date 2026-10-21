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
        // 【沙色定标依据】对着 Steam 高清头图（library_hero.jpg）逐点量测：参考受光干沙
        // = (253,229,169)（线性 (0.982,0.784,0.397)）。**编码前提**：旧值 (1.0,0.83,0.38)
        // 是在二次 sRGB 编码的截图上标的——截图把蓝通道从约 0.43 抬到 0.69，看起来刚好；
        // 实机单次编码下蓝只剩 89/255，沙面成浓黄。现按单次显示编码反解：
        // albedo sRGB (1.0,0.92,0.69) → 线性 (1.0,0.831,0.434)，
        // × 光照核 (0.992,0.944,0.902) ≈ 参考线性值。
        _DrySandColor       ("干沙色（按参考头图受光沙反定）", Color) = (1.0, 0.92, 0.69, 1.0)
        _WetSandColor       ("湿沙色（同一光照核下的湿/暗带）", Color) = (0.82, 0.72, 0.52, 1.0)
        _UnderwaterSandColor ("水下沙色（同族偏暖，水色会把它拉回绿松石）", Color) = (1.0, 0.93, 0.72, 1.0)
        _StrataColor        ("层理色（深带）", Color) = (0.72, 0.61, 0.45, 1.0)
        _StrataBandWidth    ("层理带宽（m）", Range(0.05, 0.5)) = 0.16
        _StrataStrength     ("层理强度", Range(0.0, 1.0)) = 0.35
        _RippleStrength     ("沙纹强度", Range(0.0, 1.0)) = 0.08
        // 0.10 在 LDR 直出 + 高太阳下让整片干沙布满白色闪点（r56 实证：像雪）；
        // 0.02 只留零星高光，参考的沙面主纹理交给暗颗粒与沙纹。
        _SparkleStrength    ("闪沙强度", Range(0.0, 0.5)) = 0.02
        // 显示链曝光因子。参考受光干沙线性 (0.982,0.784,0.397)；光照核
        // (ambient + sun×ndl)×1.20 ≈ (0.99,0.94,0.90) 命中。旧 1.05 是按二次 sRGB
        // 编码截图标定的，实机单次编码下沙面偏暗。
        _LightScale         ("显示链曝光因子", Range(0.2, 3.0)) = 1.20
        _CausticGain        ("焦散强度（水面坡度散度系数）", Range(0.0, 4.0)) = 0.6
        _CausticScale       ("焦散世界尺度（1/m）", Range(0.02, 2.0)) = 0.5
        _CausticSampleOffset ("焦散采样偏移（UV）", Range(0.0005, 0.05)) = 0.0156
        _WaterLabDetailNormal ("细节法线（水面小尺度坡度场＝焦散源）", 2D) = "gray" {}
        // 天光项：原作走 HDR+GI，本仓用常数项近似；比原作天光色略提亮，防暗部死黑。
        _AmbientColor       ("环境色（原作天光色略提亮）", Color) = (0.08, 0.14, 0.21, 1.0)
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
                // 边界权重 ×10（原作 ×20）：×4 的过渡带宽约 25 cm，水线糊成一条灰带；
                // 岸线现在沿 z 摆动，需要更利落的湿/水下换色。
                float underwater = (_WaterLabSimEnabled > 0.5)
                    ? saturate((eta - ground) * 10.0) : 0.0;
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

                // 深色沙粒/贝壳碎屑：参考干沙不是纯色面，有细密暗点提供尺度感。
                // 9 格/m 与 23 格/m 两档阈值稀疏化，只压 albedo 6–12%，不改变均值；
                // 水下减半，避免和焦散/水色变化打架。
                float2 grainCell1 = floor(IN.positionWS.xz * 9.0);
                float grain1 = frac(sin(dot(grainCell1, float2(127.1, 311.7))) * 43758.5453);
                float2 grainCell2 = floor(IN.positionWS.xz * 23.0 + 17.0);
                float grain2 = frac(sin(dot(grainCell2, float2(269.5, 183.3))) * 43758.5453);
                float grain = (grain1 > 0.86 ? 0.10 : 0.0) + (grain2 > 0.93 ? 0.05 : 0.0);
                albedo *= 1.0 - grain * (1.0 - 0.5 * underwater);

                // 闪沙（原作 Lit_Sand PS 实证：噪声 × 太阳亮度 × exp(−1.731234×湿度) × 坡度门控，
                // 加进 albedo 随光照走——干沙闪、湿沙哑）。反汇编里湿度入口并列出现
                // v4.w×5.0 与 v4.w×(−1.731234)：前者是湿度曲线的陡化（sat×5 后 smoothstep），
                // 后者进 exp。本仓噪声仍是高频格点 hash（原作是 3D 噪声，观感近似）。
                float3 n = normalize(IN.normalWS);
                float2 spCell = floor(IN.positionWS.xz * 14.0);
                float spN = frac(sin(dot(spCell, float2(127.1, 311.7))) * 43758.5453);
                float moisture = saturate(wet * 5.0);
                moisture = moisture * moisture * (3.0 - 2.0 * moisture); // smoothstep 平滑
                float sparkle = spN * _SparkleStrength
                              * exp(-1.731234 * moisture) * (1.0 - underwater)
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
                // 【增益定标】散度量级与水面坡度同阶（p99 约 0.1–0.4），取 6.0 后
                // 反照率调制 ≈±30%（亮暗纹可读）；负半周自动成暗纹（净量为零，不改均值）。
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
