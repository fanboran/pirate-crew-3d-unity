// ============================================================================
// WaterLabOcean.shader —— 实验水体（W2）：URP 前向透明水体。
//
// 【观感目标】Sandcastle 参考图：近岸透亮见沙、碎金波光、浪脊影子。
//
// 【光照模型 = Lit/Water PS 逐指令复刻】（逆向档案 §4.1 完整公式，2026-10-15 全解）
//   1. 细节法线 = 法线贴图双尺度采样（世界 XZ×0.05 / ×0.20，时间×0.8 动画，动差混合）
//      + 模拟坡度场（顶点法线），归一——碎金闪点全部由贴图高频法线供 pow1500 镜面瓣；
//   2. 折射偏移 = N.xz × refractionFactor（实测 0.0121，Y 轴乘纵横比），深度安全门控；
//   3. 光程 = InvVP 重建折射点世界坐标，dist = |折射点 − 表面点|（真路径长度，
//      不是 depth/vDotN 近似——透明度衰减曲线与原作一致的关键）；
//   4. 吸收 T = exp(-8.65617·σ_rgb·dist) × 水色（8.65617 为原作 PS 字面量）；
//   5. col = lerp(背景×T×(0.9+0.2·NdotV⁶), 散射色, NdotV⁵ × min(阴影+0.25,1) × 0.75)
//      ——散射权重随俯视增大；阴影软化只接在散射项（+0.25 下限 = 影子最深 75%）；
//   6. col += 太阳色 × pow(NdotH,1500) × 阴影原值（反汇编 156 行：镜面乘的是未软化
//      的比较采样结果，不乘菲涅尔）；
//   7. 原作水面不投影（Lit_Water blob 只有 2VS+2PS，无 ShadowCaster）；水底波影在
//      沙侧由「阴影 × exp(-0.721×水深)」承担（逆向档案 §5），本仓同构。
//   与原作的机制差异（URP 约束）：原作不透明队列 alpha=水量×100，本仓透明队列
//   alpha=1−min 透过率——观感等价（见逆向档案 §4.1 覆盖率表）。
//
// 【教训记录】vDotN = dot(viewDir, N)（viewDir 指向相机），写反 → 菲涅尔恒 1；
//   细节法线必须是贴图（正弦格架出网格状点阵）；法线 y 增益 ≤1.5。
// ============================================================================

Shader "PirateCrew/WaterLab/Ocean"
{
    Properties
    {
        // ---- 吸收（Lit/Water PS 同构：exp(-8.65617·σ·真光程)，σ 单位 1/m）----
        _Absorption             ("消光 σ (8.65617 缩放前, 1/m)", Color) = (0.45, 0.14, 0.03, 1.0)
        _WaterTint              ("水色乘子（cb0[131]）", Color) = (0.96, 0.99, 1.0, 1.0)
        _ScatterColor           ("掠射散射色（天空蓝）", Color) = (0.30, 0.47, 0.72, 1.0)

        // ---- 折射与焦散 ----
        _RefractionFactor       ("折射偏移系数（原作实测 0.0121）", Range(0.0, 0.1)) = 0.0121
        _CausticStrength        ("水下焦散强度", Range(0.0, 2.0)) = 0.7
        _CausticScale           ("焦散图案频率 (rad/m)", Range(0.2, 4.0)) = 0.7

        // ---- 太阳高光（碎金 = 细节法线 × 近镜面瓣）----
        _SunColor               ("太阳色", Color) = (1.0, 0.93, 0.78, 1.0)
        _SpecularPower          ("镜面瓣锐度（原作 1500）", Range(64.0, 4096.0)) = 1500.0
        _SpecularBroad          ("次级宽瓣强度", Range(0.0, 0.5)) = 0.02

        // ---- 泡沫 ----
        _FoamColor              ("泡沫色", Color) = (0.95, 0.97, 0.96, 1.0)
        _FoamStrength           ("模拟泡沫强度", Range(0.0, 2.0)) = 1.5

        // ---- 岸线 ----
        _DryClip                ("干区水深裁剪（m）", Range(0.005, 0.2)) = 0.02

        // ---- 细节法线（必须进 Properties 才能被材质序列化绑定；gray = 平坦）----
        _WaterLabDetailNormal   ("细节法线", 2D) = "gray" {}

        // ---- 解析折射底色的沙色板（与 WaterLab_Sand 同值；改两侧同步）----
        _RefSandDry             ("折射·干沙色", Color) = (0.92, 0.85, 0.69, 1.0)
        _RefSandWet             ("折射·湿沙色", Color) = (0.62, 0.50, 0.35, 1.0)
        _RefSandUnder           ("折射·水下亮沙色", Color) = (0.92, 0.85, 0.66, 1.0)
        _RefAmbient             ("折射·环境色", Color) = (0.42, 0.50, 0.58, 1.0)
        _RefSunTint             ("折射·太阳染色", Color) = (1.0, 0.95, 0.85, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   WaterLabVertex
            #pragma fragment WaterLabFragment

            // 主光阴影（软化后接在散射项，原作同构）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里（单独 include Shadows 不够）
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _Absorption;
                float3 _WaterTint;
                float3 _ScatterColor;
                float  _RefractionFactor;
                float  _CausticStrength;
                float  _CausticScale;
                float3 _SunColor;
                float  _SpecularPower;
                float  _SpecularBroad;
                float3 _FoamColor;
                float  _FoamStrength;
                float  _DryClip;
                float3 _RefSandDry;
                float3 _RefSandWet;
                float3 _RefSandUnder;
                float3 _RefAmbient;
                float3 _RefSunTint;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabWetTex);    SAMPLER(sampler_WaterLabWetTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);

            float DecodeEta(float e) { return e * 5.0 - 3.0; }

            float2 WorldToDomainUv(float3 worldPos)
            {
                return (worldPos.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
            }

            // 程序化焦散：两层干涉条纹锐化（原作的焦散来自体素 GI，本仓程序化近似）。
            float Caustic(float2 p, float t)
            {
                float a = sin(p.x * _CausticScale + t * 1.3) * sin(p.y * _CausticScale * 1.18 - t * 1.1);
                float b = sin((p.x * 0.7 + p.y) * _CausticScale * 0.83 - t * 0.8)
                        * sin((p.x - p.y * 0.6) * _CausticScale * 1.29 + t * 1.5);
                float v = a * 0.6 + b * 0.4;
                return pow(saturate(v * 0.5 + 0.5), 3.0) * 1.6;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings WaterLabVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                if (_WaterLabSimEnabled > 0.5)
                {
                    float2 uv = WorldToDomainUv(positionWS);
                    float etaEnc = SAMPLE_TEXTURE2D_LOD(_WaterLabHeightTex,
                        sampler_WaterLabHeightTex, uv, 0).r;
                    positionWS.y += DecodeEta(etaEnc); // 网格基准 y=0（静水面）
                }

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 WaterLabFragment(Varyings IN) : SV_Target
            {
                float t = _TimeParameters.x;
                float2 uv = WorldToDomainUv(IN.positionWS);
                float4 field = SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv);
                float ground = DecodeEta(SAMPLE_TEXTURE2D(_WaterLabGroundTex, sampler_WaterLabGroundTex, uv).r);

                float eta = DecodeEta(field.r);
                float depth = max(eta - ground, 0.0);

                if (_WaterLabSimEnabled > 0.5)
                    clip(depth - _DryClip);

                // ---- 法线 = 模拟坡度场（顶点法线）+ 细节法线贴图（原作：加后归一）。
                // y 增益 ≤1.5：过猛会把真实坡度放大成 60°+ 斜法线 → 菲涅尔恒 1。
                float2 slope = clamp(float2(field.g * 2.0 - 1.0, field.b * 2.0 - 1.0),
                                     float2(-0.6, -0.6), float2(0.6, 0.6));
                float3 vertexNormal = normalize(float3(slope.x, 1.0, slope.y) * float3(1.5, 1.0, 1.5));

                // 细节法线：双尺度（20m / 5m 平铺）+ 时间×0.8 动画 + 动差混合（原作同参数）
                float t1 = t * 0.8;
                float2 uvA = IN.positionWS.xz * 0.05 + t1 * float2(0.013, -0.011);
                float2 uvB = IN.positionWS.xz * 0.20 + t1 * float2(-0.017, 0.013);
                float blend = 0.5 + 0.5 * sin(t * 0.23 + dot(IN.positionWS.xz, float2(0.05, 0.043)));
                float2 dA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, uvA).rg * 2.0 - 1.0;
                float2 dB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, uvB).rg * 2.0 - 1.0;
                // 掠射角下高频法线欠采样 → 镜面瓣出粉色摩尔带（实拍实证），随距离淡出
                float detailFade = saturate(1.0 - (length(IN.positionWS - GetCameraPositionWS()) - 18.0) / 72.0);
                float2 dn = lerp(dA, dB, blend) * detailFade;
                float3 normalWS = normalize(vertexNormal + float3(dn.x, 0.0, dn.y));

                // viewDir：表面指向相机（vDotN = dot(viewDir, N)，符号千万别反）
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25) ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);

                float vDotN = saturate(dot(viewDir, normalWS));

                // 主光阴影 + 原作软化（min(sh+0.25,1)，影子最深 75%；波峰影带柔和不死黑）
                float sunShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float shadowSoft = min(sunShadow + 0.25, 1.0);

                // ---- 折射：解析水底（不依赖 _CameraOpaqueTexture——拍图链里它是空的，
                // 实证 bgColor 恒黑、透明度曲线整个塌掉）。视线在水下延伸至地形：
                // 水底 XZ = 表面 XZ + 视线水平分量 × 水下段长；按折射扭曲读地形/湿沙纹理，
                // 用与 WaterLab_Sand 同式的沙色光照出「透过水看到的底」。
                float3 rayDir = normalize(IN.positionWS - GetCameraPositionWS());
                float dist = depth / max(-rayDir.y, 0.25);
                float2 refrXZ = IN.positionWS.xz + rayDir.xz * dist + normalWS.xz * _RefractionFactor * 10.0;
                float2 refrDomainUv = WorldToDomainUv(float3(refrXZ.x, 0.0, refrXZ.y));
                float groundRefr = DecodeEta(SAMPLE_TEXTURE2D(_WaterLabGroundTex, sampler_WaterLabGroundTex, refrDomainUv).r);
                float wetRefr = saturate(SAMPLE_TEXTURE2D(_WaterLabWetTex, sampler_WaterLabWetTex, refrDomainUv).r);
                float depthRefr = max(eta - groundRefr, 0.0);
                float3 sandAlbedo = lerp(_RefSandDry.rgb, _RefSandWet.rgb, wetRefr);
                sandAlbedo = lerp(sandAlbedo, _RefSandUnder.rgb, saturate(depthRefr * 4.0));
                float3 sunRaw2 = _WaterLabSunDir.xyz;
                float3 sunDir2 = (dot(sunRaw2, sunRaw2) < 0.25) ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw2);
                // 原作的折射底色 = 真实沙网格的渲染纹理（含沙侧光照与阴影）。本仓解析等价：
                // 与 WaterLab_Sand 同式——直射项乘「阴影@折射点 × exp(-0.721×水深)」
                // （水底波影由此而来：波浪经过时折射点水深振荡 → 底色直射光明暗带）
                float3 floorPosWS = float3(refrXZ.x, groundRefr, refrXZ.y);
                float floorSh = MainLightRealtimeShadow(TransformWorldToShadowCoord(floorPosWS));
                float floorColumn = exp(-0.721 * depthRefr);
                float3 bgColor = sandAlbedo * (_RefAmbient.rgb + _RefSunTint.rgb
                    * saturate(dot(float3(0, 1, 0), sunDir2)) * 1.15 * floorSh * floorColumn);

                // 焦散乘进背景色（只在水浅处可见；原作由体素 GI 提供，本仓程序化近似）
                float caustic = Caustic(refrXZ, t) * exp(-depth * 0.55) * _CausticStrength;
                bgColor *= 1.0 + caustic;

                // ---- Beer–Lambert（原作 8.65617 字面量缩放）
                float3 T = exp(-8.65617 * _Absorption.rgb * dist) * _WaterTint.rgb;
                float minT = min(T.r, min(T.g, T.b));

                // ---- 基底与散射混合（原作公式：底 0.9 + 曲率补偿 0.2·NdotV⁶；
                // 散射权重 = NdotV⁵ × 阴影软化 × 0.75，俯视时水体体色最重）
                float ndv2 = vDotN * vDotN;
                float ndv4 = ndv2 * ndv2;
                float3 baseCol = bgColor * T * (0.9 + 0.2 * ndv4 * ndv2);
                float scatterW = pow(vDotN, 5.0) * shadowSoft * 0.75;
                float3 col = lerp(baseCol, _ScatterColor.rgb, scatterW);

                // ---- 太阳镜面（原作 pow(NdotH,1500) 近镜面瓣 + 次级宽瓣 shimmer；
                // 碎金闪点全部来自细节法线贴图的高频法线；乘阴影原值、不乘菲涅尔）
                float3 h = normalize(viewDir + sunDir);
                float ndh = saturate(dot(normalWS, h));
                float spec = pow(ndh, _SpecularPower) + pow(ndh, 96.0) * _SpecularBroad;
                col += _SunColor.rgb * spec * sunShadow;

                // ---- 泡沫：只留模拟破碎通道（原作水体 PS 无泡沫项——白水是 Whitewater
                // 粒子系统（W4 范围），岸线亮带来自湿沙与白水粒子。按深度刷白沫带是
                // 第四轮自造近似，把整个浅水架刷白盖住水底波影（capture 44 实证），删。
                // 泡沫以模拟格为单元，半格偏移双采样平均掉格间噪声。
                float2 texel = 1.0 / _WaterLabOrigin.w;
                float foamA = SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv + texel * 0.5).a;
                float foam = saturate((field.a + foamA) * 0.5) * _FoamStrength;
                col = lerp(col, _FoamColor.rgb, foam);

                // 真 alpha 透明（URP 约束；原作不透明队列 alpha=水量×100，观感等价）
                float alpha = saturate(max(foam, 1.0 - minT));
                return half4(col, alpha);
            }
            ENDHLSL
        }

    }
    Fallback Off
}
