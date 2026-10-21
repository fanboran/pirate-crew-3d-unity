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
        _StrataBandWidth    ("层理带宽（m）", Range(0.10, 1.20)) = 0.45
        _StrataStrength     ("层理强度", Range(0.0, 1.0)) = 0.12
        _RippleStrength     ("沙纹强度", Range(0.0, 1.0)) = 0.16
        // 0.10 在 LDR 直出 + 高太阳下让整片干沙布满白色闪点（r56 实证：像雪）；
        // 0.02 只留零星高光，参考的沙面主纹理交给暗颗粒与沙纹。
        _SparkleStrength    ("闪沙强度", Range(0.0, 0.5)) = 0.02
        _DetailNormalStrength ("沙面细节法线强度", Range(0.0, 1.0)) = 0.30
        _DetailNormalScale  ("沙面细节法线尺度（1/m）", Range(0.2, 8.0)) = 1.20
        _GrainNormalStrength ("颗粒法线强度", Range(0.0, 1.0)) = 0.16
        _MottleStrength     ("大尺度色斑强度", Range(0.0, 0.3)) = 0.06
        _PebbleStrength     ("干沙卵石暗点强度", Range(0.0, 0.8)) = 0.45
        _WetSheen           ("湿沙高光强度", Range(0.0, 1.0)) = 0.25
        // 显示链曝光因子。环境项由 (0.08,0.14,0.21) 抬到 (0.18,0.20,0.22) 后，
        // 受光核略增；1.08 保持参考受光干沙 (253,229,169) 同亮度，同时让阴影里的
        // 沙靠“天光 + 沙间反弹”读到形体，而不是一片死黑。
        _LightScale         ("显示链曝光因子", Range(0.2, 3.0)) = 1.08
        _CausticGain        ("焦散强度（水面坡度散度系数）", Range(0.0, 4.0)) = 0.6
        _CausticScale       ("焦散世界尺度（1/m）", Range(0.02, 2.0)) = 0.5
        _CausticSampleOffset ("焦散采样偏移（UV）", Range(0.0005, 0.05)) = 0.0156
        _WaterLabDetailNormal ("细节法线（水面小尺度坡度场＝焦散源）", 2D) = "gray" {}
        // 天光项：原作走 HDR+GI，本仓用常数项近似。旧值只保留天空色，暗部太黑；
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
                float3 _StrataColor;
                float  _StrataBandWidth;
                float  _StrataStrength;
                float  _RippleStrength;
                float  _SparkleStrength;
                float  _DetailNormalStrength;
                float  _DetailNormalScale;
                float  _GrainNormalStrength;
                float  _MottleStrength;
                float  _PebbleStrength;
                float  _WetSheen;
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

                // 大尺度色斑：低频正弦乘积只做 ±6% 的反照率起伏，打破整片沙一个亮度；
                // 参考干沙的斑驳来自真实沙面/贝壳碎屑分布，这里用最便宜的低频近似。
                float mottle = sin(IN.positionWS.x * 0.17 + sin(IN.positionWS.z * 0.11) * 1.7)
                             * sin(IN.positionWS.z * 0.13 + 1.3);
                albedo *= 1.0 + mottle * _MottleStrength;

                // 层理：原作沙层是缓弧条带，不是等高线。降低强度、加宽间距，
                // 并用低频相位扰动打断“等高线”读感；水下不画层理（交给波纹/焦散）。
                float bandCoord = ground
                                + 0.04 * sin(IN.positionWS.z * 0.43 + 1.1)
                                + 0.02 * sin(IN.positionWS.x * 0.90);
                float band = frac(bandCoord / _StrataBandWidth);
                float strata = smoothstep(0.40, 0.5, band) * (1.0 - smoothstep(0.5, 0.60, band));
                albedo = lerp(albedo, _StrataColor, strata * _StrataStrength * (1.0 - underwater));

                // 沙纹：参考的沙纹是**平行岸线的缓弧条带**（波长约 0.3–0.8 m），
                // 不是两组全场同相 sin。主频沿 x（离岸方向），z 上叠低频相位摆动；
                // 第二组低频只提供大尺度深浅变化。albedo 调制控制在 ±8%，主细节交给法线。
                float ripPhase = IN.positionWS.x * 7.5
                               + 0.90 * sin(IN.positionWS.z * 0.31 + 0.4)
                               + 0.35 * sin(IN.positionWS.z * 1.70 + 1.1);
                float ripple = sin(ripPhase) * 0.75
                             + sin(IN.positionWS.z * 2.1 - IN.positionWS.x * 0.35 + 1.3) * 0.25;
                albedo *= 1.0 + ripple * 0.5 * _RippleStrength;

                // 干沙卵石/贝壳碎屑：原作的暗点是**随机位置、大小不一、成簇**的，
                // 不是规则格点。每个格子内取一个抖动点（jittered cell），用距离场画圆点；
                // 三档尺度（约 25 cm / 11 cm / 4 cm）叠加，并按低频 mask 成簇。
                // 大档给前景可辨认的 3–6 cm 卵石，细档只提供沙粒统计感。
                float2 p0 = IN.positionWS.xz * 4.0 + 3.0;
                float2 c0 = floor(p0);
                float2 f0 = frac(p0);
                float2 j0 = float2(
                    frac(sin(dot(c0, float2(41.3, 289.1))) * 43758.5453),
                    frac(sin(dot(c0 + 11.0, float2(127.1, 311.7))) * 43758.5453));
                float d0 = length(f0 - j0);
                float pick0 = step(0.85, frac(sin(dot(c0 + 7.0, float2(269.5, 183.3))) * 43758.5453));
                float peb0 = (1.0 - smoothstep(0.10, 0.22, d0)) * pick0;

                float2 p1 = IN.positionWS.xz * 9.0;
                float2 c1 = floor(p1);
                float2 f1 = frac(p1);
                float2 j1 = float2(
                    frac(sin(dot(c1, float2(127.1, 311.7))) * 43758.5453),
                    frac(sin(dot(c1 + 17.0, float2(269.5, 183.3))) * 43758.5453));
                float d1 = length(f1 - j1);
                float pick1 = step(0.55, frac(sin(dot(c1 + 31.0, float2(41.3, 289.1))) * 43758.5453));
                float peb1 = (1.0 - smoothstep(0.05, 0.17, d1)) * pick1;

                float2 p2 = IN.positionWS.xz * 23.0 + 9.0;
                float2 c2 = floor(p2);
                float2 f2 = frac(p2);
                float2 j2 = float2(
                    frac(sin(dot(c2, float2(269.5, 183.3))) * 43758.5453),
                    frac(sin(dot(c2 + 23.0, float2(127.1, 311.7))) * 43758.5453));
                float d2 = length(f2 - j2);
                float pick2 = step(0.70, frac(sin(dot(c2 + 13.0, float2(71.9, 193.7))) * 43758.5453));
                float peb2 = (1.0 - smoothstep(0.06, 0.20, d2)) * pick2;

                // 低频成簇 mask：让卵石有疏密，而不是均匀铺满。
                float pebbleMask = 0.55 + 0.45 * sin(IN.positionWS.x * 0.31 + 1.7)
                                          * sin(IN.positionWS.z * 0.27 + 0.4);
                float pebble = saturate((peb0 + peb1 + 0.45 * peb2) * pebbleMask);
                float3 pebbleColor = _StrataColor * 0.55;
                albedo = lerp(albedo, pebbleColor,
                              pebble * _PebbleStrength * (1.0 - 0.5 * underwater));

                // 沙面法线：已有 _WaterLabDetailNormal（水面坡度场同源纹理）直接复用。
                // 两档采样——低频缓弧（_DetailNormalScale=1.2 → 约 0.83 m 周期）+
                // 高频颗粒（×8 → 约 10 cm 周期）——把法线从“整片朝天”变成有明暗起伏的沙面；
                // 这是治“沙地偏平”的主手段。
                float3 n = normalize(IN.normalWS);
                float2 dnBroad = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    IN.positionWS.xz * _DetailNormalScale + float2(0.37, 0.11)).rg * 2.0 - 1.0;
                float2 dnGrain = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    IN.positionWS.xz * _DetailNormalScale * 8.0 + float2(0.73, 0.29)).rg * 2.0 - 1.0;
                n = normalize(float3(
                    n.x + dnBroad.x * _DetailNormalStrength + dnGrain.x * _GrainNormalStrength,
                    n.y,
                    n.z + dnBroad.y * _DetailNormalStrength + dnGrain.y * _GrainNormalStrength));
                // 平行岸线的缓弧沙纹再给法线一点 x 向倾斜，让光照随沙纹起伏。
                n.x += cos(ripPhase) * 0.35 * _RippleStrength;
                n = normalize(n);

                // 闪沙（原作 Lit_Sand PS 实证：噪声 × 太阳亮度 × exp(−1.731234×湿度) × 坡度门控，
                // 加进 albedo 随光照走——干沙闪、湿沙哑）。反汇编里湿度入口并列出现
                // v4.w×5.0 与 v4.w×(−1.731234)：前者是湿度曲线的陡化（sat×5 后 smoothstep），
                // 后者进 exp。本仓噪声仍是高频格点 hash（原作是 3D 噪声，观感近似）。
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

                // 湿沙面高光：水膜覆盖的湿沙在太阳下有一层窄高光；参考 crop-shore 的
                // 白色闪点即来自这层水膜。只作用于 wet、非水下区域，强度很低。
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float3 halfDir = normalize(viewDir + sunDir);
                float sheen = pow(saturate(dot(n, halfDir)), 48.0)
                            * wet * (1.0 - underwater) * _WetSheen;
                col += _SunTint * sheen * 0.35;

                return half4(col * _LightScale, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
