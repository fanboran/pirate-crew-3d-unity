// ============================================================================
// WaterLabOcean.shader —— 实验水体（W2）：URP 前向不透明水体。
//
// 【观感目标】Sandcastle 参考图：近岸透亮见沙、碎金波光、浪脊影子。
//
// 【光照模型 = Lit/Water PS 逐指令复刻】（逆向档案 §4.1 完整公式，2026-10-15 全解）
//   1. 细节法线 = 法线贴图双尺度采样（世界 XZ×0.05 / ×0.20，时间×0.8 动画，动差混合）
//      + 模拟坡度场（顶点法线），归一——碎金闪点全部由贴图高频法线供 pow1500 镜面瓣；
//   2. 折射 = 真屏幕折射（外包债①清偿）：底色采 _CameraOpaqueTexture（URP
//      CopyColorPass 在「沙/天空渲完、透明队列未开」时拷的帧色 = 原作折射底色即
//      真实沙网格渲染纹理的正身）；水体排 Transparent+10 调度位以落到拷贝之后，
//      渲染语义仍不透明（ZWrite On、无 Blend、alpha 恒 1——第十轮的不透明观感
//      原样保留，透明度只是连续颜色项）；偏移 = N.xz × refractionFactor（实测
//      0.0121，Y 轴乘纵横比），深度安全门控（折射点比当前像素近→不偏移）；
//   3. 光程 = 解析水下段（depth/max(−rayDir.y, 0.25)，深度来自模拟高度场）——
//      原作用深度重建折射点（真路径长度），URP 拷深落点在透明队列之后、
//      水体采样时 _CameraDepthTexture 无内容（r27 DBG 实锤），解析近似代之，
//      深度安全门控同因暂缺（域内无水上遮挡物，判据本就不激活）；
//   4. 吸收 T = exp(-8.65617·σ_rgb·dist) × 水色（8.65617 为原作 PS 字面量）；
//   5. col = lerp(背景×T×(0.9+0.2·NdotV⁶), 散射色, NdotV⁵ × min(阴影+0.25,1) × 0.75)
//      ——散射权重随俯视增大；阴影软化只接在散射项（+0.25 下限 = 影子最深 75%）；
//   6. col += 太阳色 × pow(NdotH,1500) × 阴影原值（反汇编 156 行：镜面乘的是未软化
//      的比较采样结果，不乘菲涅尔）；
//   7. 原作水面不投影（Lit_Water blob 只有 2VS+2PS，无 ShadowCaster）；水底波影在
//      沙侧由「阴影 × exp(-0.721×水深)」承担（逆向档案 §5），本仓同构。
//   队列：Transparent+10 调度位（排在 URP CopyColorPass 之后、白水 3050 之前），
//   渲染语义不透明（ZWrite On、无 Blend、alpha 恒 1）——「透明度」只是透过率调制
//   底色亮度的连续颜色项；当年透明队列的蓝白骤变来自 alpha=1−minT 的混合层，
//   本 shader 无 Blend，该伪影类在构造上不存在（2026-10-15 创始人拍板不透明观感）。
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
        _WaterTint              ("水色乘子（原作 waterColor 实测）", Color) = (0.9603, 0.9858, 0.9995, 1.0)
        _ScatterColor           ("散射色（原作 waterScatteringColor 实测暖橙）", Color) = (0.5684, 0.3569, 0.0, 1.0)

        // ---- 折射与焦散 ----
        _RefractionFactor       ("折射偏移系数（原作实测 0.0121）", Range(0.0, 0.1)) = 0.0121
        _CausticGain            ("焦散聚焦增益（η 拉普拉斯系数）", Range(0.0, 8.0)) = 0.2

        // ---- 太阳高光（碎金 = 细节法线 × 近镜面瓣）----
        _SpecularPower          ("镜面瓣锐度（原作 1500）", Range(64.0, 4096.0)) = 1500.0

        // ---- 泡沫 ----
        _FoamColor              ("泡沫色", Color) = (0.95, 0.97, 0.96, 1.0)
        _FoamStrength           ("模拟泡沫强度", Range(0.0, 2.0)) = 1.5

        // ---- 岸线 ----
        _DryClip                ("干区水深裁剪（m）", Range(0.005, 0.2)) = 0.02

        // ---- 细节法线（必须进 Properties 才能被材质序列化绑定；gray = 平坦）----
        _WaterLabDetailNormal   ("细节法线", 2D) = "gray" {}

        // ---- 太阳色（原作太阳平行光实测；镜面与焦散调制共用）----
        _RefSunTint             ("太阳色", Color) = (1.0, 0.9709, 0.9403, 1.0)
    }

    SubShader
    {
    Tags
    {
        "RenderType" = "Opaque"
        "Queue" = "Transparent+10"
        "RenderPipeline" = "UniversalPipeline"
        "IgnoreProjector" = "True"
    }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _Absorption;
                float3 _WaterTint;
                float3 _ScatterColor;
                float  _RefractionFactor;
                float  _CausticGain;
                float  _SpecularPower;
                float3 _FoamColor;
                float  _FoamStrength;
                float  _DryClip;
                float3 _RefSunTint;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabCausticTex); SAMPLER(sampler_WaterLabCausticTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);

            float DecodeEta(float e) { return e * 5.0 - 3.0; }

            float2 WorldToDomainUv(float3 worldPos)
            {
                return (worldPos.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
            }

            // 焦散（严格对标原作 Lit_Sand：域内叠加 causticsMap RT，随水场移动；
            // RT 由引擎侧产出——本仓由模拟侧 OutputField 每 SimStep 从 η 场烘焙，
            //第十五轮重外包：逐像素 5 采样现算撤出渲染 shader，换 1 次该 RT 采样）。
            // RT 值 = η 四邻均值−中心的带符号拉普拉斯（凹面聚光为正、凸面发散为负）。
            // 【零基线】焦散是重新分配光线（净量为零）：带符号调制，基线 0——
            // 曾误写 saturate(1+增益×lap)，基线 1.0 整项入底色＝全场 +100% 反照率
            //（r19-r23 奶白 wash 真凶，削顶与均值双双实锤）。增益留在材质侧。
            float SimCaustic(float2 domainUv)
            {
                return SAMPLE_TEXTURE2D(_WaterLabCausticTex, sampler_WaterLabCausticTex, domainUv).r * _CausticGain;
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

                // 细节法线：双尺度（20m / 5m 平铺）+ 时间×0.8 双向漂移动画
                float t1 = t * 0.8;
                float2 uvA = IN.positionWS.xz * 0.05 + t1 * float2(0.013, -0.011);
                float2 uvB = IN.positionWS.xz * 0.20 + t1 * float2(-0.017, 0.013);
                float2 dA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, uvA).rg * 2.0 - 1.0;
                float2 dB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal, uvB).rg * 2.0 - 1.0;
                // 掠射角下高频法线欠采样 → 镜面瓣出粉色摩尔带（实拍实证），远距淡出；
                // 贴图自带 mip 链抗欠采样，淡出只兜远端（18-90 档曾把出图相机的网整个
                // 打折——亮脊网变稀即是此因，r13 实证后外推）
                float detailFade = saturate(1.0 - (length(IN.positionWS - GetCameraPositionWS()) - 40.0) / 160.0);
                // 两尺度法线**相加**后归一系数：lerp 会把两套标定梯度各稀释一半
                // （亮脊网变稀的第二根因）；×0.7071 保持单尺度标定的统计强度
                float2 dn = (dA + dB) * 0.7071 * detailFade;
                float3 normalWS = normalize(vertexNormal + float3(dn.x, 0.0, dn.y));

                // viewDir：表面指向相机（vDotN = dot(viewDir, N)，符号千万别反）
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25) ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);

                float vDotN = saturate(dot(viewDir, normalWS));

                // 主光阴影 + 原作软化（min(sh+0.25,1)，影子最深 75%；波峰影带柔和不死黑）
                float sunShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float shadowSoft = min(sunShadow + 0.25, 1.0);

                // ---- 折射：真屏幕折射（外包债①清偿，对标原作「折射底色 = 真实沙网格
                // 渲染纹理」）。底色采 _CameraOpaqueTexture——URP CopyColorPass 在
                // AfterRenderingSkybox 拷的帧色（沙/天空已渲完，透明队列的水还没画，
                // 拷贝里没有自己）；水体排透明调度位即天然落在拷贝之后。
                // 偏移 = N.xz × refractionFactor（实测 0.0121，Y 轴乘纵横比）。
                // 【已知缺口】深度安全门控与「深度重建折射点」暂缺：渲染器资产
                // m_CopyDepthMode=AfterTransparents，_CameraDepthTexture 拷贝点落在
                // 透明队列之后，水体采样时纹理无内容（DBG1 实锤全帧均匀 0.196）。
                // 域内无水上遮挡物，安全门判据本就不激活；光程用解析水下段
                // （r26 口径，沙底平坦近似），沙侧真深度来自模拟高度场。
                float2 baseUV = IN.screenPos.xy / IN.screenPos.w;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 refrUV = baseUV + normalWS.xz * _RefractionFactor * float2(1.0, aspect);
                float3 bgColor = SampleSceneColor(refrUV);
                float3 rayDir = normalize(IN.positionWS - GetCameraPositionWS());
                float dist = depth / max(-rayDir.y, 0.25);
                float2 refrXZ = IN.positionWS.xz + rayDir.xz * dist;
                float2 refrDomainUv = WorldToDomainUv(float3(refrXZ.x, 0.0, refrXZ.y));

                // 焦散叠加（Lit_Sand 输出结构不变：+ albedo×焦散、不经阴影、随 T 衰减）。
                // 底色拷贝即「albedo×光」，以其为乘子的相对调制对齐原作 albedo×焦散
                //（直射≈1 时逐项等价，阴影/环境调制随底色走、零新增常量）；焦散 RT
                // 仍由模拟侧 OutputField 烘焙，域 UV 由折射点世界坐标反推
                bgColor += bgColor * SimCaustic(refrDomainUv);

                // ---- Beer–Lambert（原作 8.65617 字面量缩放；T 只调制底色，不再生成 alpha）
                float3 T = exp(-8.65617 * _Absorption.rgb * dist) * _WaterTint.rgb;

                // ---- 基底与散射混合（原作公式：底 0.9 + 曲率补偿 0.2·NdotV⁶；
                // 散射权重 = NdotV⁵ × 阴影软化 × 0.75，俯视时水体体色最重）
                float ndv2 = vDotN * vDotN;
                float ndv4 = ndv2 * ndv2;
                float3 baseCol = bgColor * T * (0.9 + 0.2 * ndv4 * ndv2);
                float scatterW = pow(vDotN, 5.0) * shadowSoft * 0.75;
                float3 col = lerp(baseCol, _ScatterColor.rgb, scatterW);

                // ---- 太阳镜面：单瓣 pow(NdotH,1500) × 阴影原值（Lit_Water PS 全文只有
                // 一对 log+exp = 一个 pow，无第二宽瓣/GGX——旧记「GGX 宽瓣」系同一项的
                // 误读，第十二轮自造的 pow48 宽瓣按严格对标删除）。亮网的观感=原作
                // 贴图的长连通脊在 pow1500 下的碎金随相机距离在屏面上融合成网
                float3 h = normalize(viewDir + sunDir);
                float ndh = saturate(dot(normalWS, h));
                float spec = pow(ndh, _SpecularPower);
                col += _RefSunTint.rgb * spec * sunShadow;   // 原作太阳平行光实测色（白），镜面与沙面同源

                // ---- 泡沫：只留模拟破碎通道（原作水体 PS 无泡沫项——白水是 Whitewater
                // 粒子系统（W4 范围），岸线亮带来自湿沙与白水粒子。按深度刷白沫带是
                // 第四轮自造近似，把整个浅水架刷白盖住水底波影（capture 44 实证），删。
                // 泡沫以模拟格为单元，半格偏移双采样平均掉格间噪声。
                float2 texel = 1.0 / _WaterLabOrigin.w;
                float foamA = SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv + texel * 0.5).a;
                float foam = saturate((field.a + foamA) * 0.5) * _FoamStrength;
                col = lerp(col, _FoamColor.rgb, foam);

                // 不透明直出（与原作同队列：透明度只是上面的颜色项，alpha 不再生效）
                return half4(col, 1.0);
            }
            ENDHLSL
        }

    }
    Fallback Off
}
