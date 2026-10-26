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
//      原作用深度重建折射点（真路径长度）；本仓 LDR 直出链保留解析近似，但折射
//      采样点已按原作补上**深度安全门**：读 _CameraDepthTexture（拷贝点改为
//      AfterOpaques，与原作 CopyOpaqueDepth 同序），背景比水面近则不采纳偏移；
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
        // ---- 吸收（Lit/Water PS 同构：exp(-8.65617·σ·真光程)）----
        // 【单位】σ 是 **tint 域系数**（0..1 量级），不是每米消光——原作那个 8.65617 字面量
        // 就是「归一系数 → 每米」的换算；若 σ 本来就是 1/m，根本不需要乘它。
        // 【曾经两个错】① 填每米值 (0.45,0.14,0.03) 又保留 ×8.65617 → 红光有效消光 3.9/m
        // （1/e 仅 26 cm），1.3 m 处红光剩 0.6%，水底真色吃光、焦散被压在底下看不见；
        // ② 改回教科书每米值后，本仓实验域水深大多 <0.5 m，衰减几乎为零 → 水成了薄膜、
        // 失去体色（2026-10-18 两轮实拍）。
        // 【定标依据】对着 Steam 高清头图（library_hero.jpg，1920×620）逐带比色。
        // 取参考最深可见带的出射色 (16,70,119) 与它自己的受光沙底 (252,226,167) 反解
        // T = 出射/底色 = (0.0036, 0.142, 0.703)，得**通道比值**（这部分与水深无关、最可靠）：
        //   σ_R : σ_G : σ_B = 15.9 : 5.52 : 1
        // 再按**三锚点同时命中**定绝对尺度：本仓 dist = 0.2 / 0.5 / ≥2 m 分别落在参考的
        // 浅滩 (157,200,171)／中带 (74,153,161)／最深带 (16,70,119) 上
        // → **有效 σ ≈ (5.6, 1.94, 0.35) /m**。
        // 原作的 σ 静态取不到（DXBC 的 RDEF 被剥、C# 全局与材质都没有该槽），
        // 故此值是**按参考画面反定标**；要更透/更浓改这里即可。
        // 【必须是 Vector，不能是 Color】Linear 工程里 Unity 会把 Color 属性当 sRGB
        // 再转一次线性：σ 会被压成 (0.365,0.039,0.0033)，绿色消光只剩 1/5.7、
        // 蓝色只剩 1/12，深水就会发青绿（r60/r61 探针实测）。物理系数走 Vector
        // 原样进 shader，不做色彩空间转换。
        _Absorption             ("消光 σ（tint 域，×8.65617 得 1/m）", Vector) = (0.6468, 0.2241, 0.0404, 1.0)
        _WaterTint              ("水色乘子（原作 waterColor 实测）", Color) = (0.9603, 0.9858, 0.9995, 1.0)
        _ScatterColor           ("散射色（原作 waterScatteringColor 实测暖橙）", Color) = (0.5684, 0.3569, 0.0, 1.0)
        // 原作散射色是 HDR 管线的文件值，经 tonemap 压缩后才落在参考图的暖调；本仓 LDR
        // 直出若按 1.0 用，深水 R 通道会比参考高 4–5 倍（r54 深带 R=67 vs 参考 16）。
        // 0.12 按参考深带反定，属**显示域补偿**，不是原作数值。
        _ScatterStrength        ("散射显示域强度", Range(0.0, 1.0)) = 0.04

        // ---- 折射（焦散不在此处：原作焦散在沙侧 causticsMap，见 WaterLabSand）----
        _RefractionFactor       ("折射偏移系数（原作实测 0.0121）", Range(0.0, 0.1)) = 0.0121

        // ---- 太阳高光（碎金 = 细节法线 × 近镜面瓣）----
        _SpecularPower          ("镜面瓣锐度（原作 1500）", Range(64.0, 4096.0)) = 1500.0

        // ---- 泡沫：本 PS **没有**泡沫项（原作同构）----
        // 原作 Lit/Water 的 PS 全文无泡沫项，白水/泡沫一律走 Whitewater 粒子系统
        // （Lit_Whitewater PS，逆向档案 §10.5）；本仓曾把模拟泡沫通道 lerp 进水面颜色，
        // 被档案 §11 对照表标为「本仓多出」。实测该自造项的代价：泡沫场在浅水架上
        // 长期处于 0.25–0.9（56% 水面 >0.2，见 2026-10-18 诊断档），反解水面颜色的
        // 泡沫混合权重达 ≈0.85——整片海面被刷成白沫，水色/水底全被盖掉，
        // 即「一点也不像水」的直接成因。故整项删除：泡沫场只驱动白水粒子生成。

        // ---- 岸线 ----
        // 原作 Lit_Water PS 的 discard 是 v4.w（VS 逐顶点算的
        // max(amount−0.05,0)，经三角形插值）< 1e-5。本仓同样把可见水深放在
        // 顶点属性里传下来，不再在 PS 里逐像素重采样高度场——后者会在水线两侧
        // 插值出 1–10 mm 的伪水膜（实拍“前沿残留水”的直接来源）。
        _DryClip                ("可见水深裁剪（m）", Range(0.0, 0.05)) = 0.00001

        // ---- 细节法线（必须进 Properties 才能被材质序列化绑定；gray = 平坦）----
        _WaterLabDetailNormal   ("细节法线", 2D) = "gray" {}

        // ---- 太阳色（原作太阳平行光实测；镜面调制）----
        _RefSunTint             ("太阳色", Color) = (1.0, 0.9709, 0.9403, 1.0)

        // ---- 天空反射（原作 PS 步骤 8：按菲涅尔混环境色；审计 §4 B8 缺件补齐）----
        // 【Vector，不是 Color】环境色是**线性/HDR 域**的亮度，不是 sRGB 显示色。Linear 工程
        // 会把 Color 属性再压一次 gamma：旧 (0,0.2,0.8) 进 shader 实际只有 (0,0.033,0.603)，
        // 绿通道被压掉 6 倍，深水就只剩蓝、没有参考那种带青的宝蓝（r62 实测）。
        // 【定标】按参考深带线性化 (0.0024,0.053,0.216) 反定：顶部菲涅尔权重≈0.18 时，
        // (0.01,0.30,1.15) × 0.19 ≈ (0.002,0.057,0.219)，落在参考深带上。
        // 这是**显示域/HDR 近似**，不是原作文件值；原作靠体素 GI + HDR tonemap 供这个亮度。
        _SkyReflectionColor     ("天空反射色（线性/HDR 域）", Vector) = (0.01, 0.30, 1.15, 1.0)
        _SkyReflectionStrength  ("天空反射强度", Range(0.0, 12.0)) = 9.5

        // ---- 深水 GI 兜底（原作体素 Gi 的显示域近似，C 级自造项）----
        // 【为什么必须有】原作水底亮度 = 直射 + 天光第二方向光（全额）+ 体素 GI（128³
        // giVoxels 全局 buffer，逆向档案 §2/§10.2），且整条链在 HDR RT + PPv2 tonemap 上
        // （BeachRenderPipeline DefaultFormat.HDR，§源码实锤）。本仓 LDR 直出、无 GI，
        // 深水只剩「沙底×T」：G 通道在 1.5 m+ 光程下衰减到 2% 以下，出 (10,41,97) 墨蓝；
        // 参考同区间是 G=95–129 的青蓝。算账：出射差 27 灰度透过 T_G≈0.02 需要底色亮
        // 5 倍——沙侧补不平，只能在水侧兜。【结构】lerp 向环境青蓝色，权重随水下光程
        // 增长（浅水为 0，2.5 m 饱和）；色值/强度按参考最深带 (16,70,119) 反定（D 级）。
        _GIFillColor            ("GI 兜底色（线性域，加法）", Vector) = (0.003, 0.044, 0.079, 1.0)
        _GIFillStrength         ("GI 兜底强度", Range(0.0, 2.0)) = 1.0
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _Absorption;
                float3 _WaterTint;
                float3 _ScatterColor;
                float  _ScatterStrength;
                float  _RefractionFactor;
                float  _SpecularPower;
                float  _DryClip;
                float3 _RefSunTint;
                float3 _SkyReflectionColor;
                float  _SkyReflectionStrength;
                float3 _GIFillColor;
                float  _GIFillStrength;
            CBUFFER_END

            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            // 诊断档（环境变量 PC3D_WATERLAB_OCEAN_DEBUG，由 WaterLabSimulation.Awake 读取置位）：
            // >0.5 时不裁剪、把画面切成 2x2 分格输出中间量——用于定位「吸收不生效」这类
            // 只在实跑才暴露的绑定问题（遮挡/裁掉/读到 0 三种成因靠它一次分辨）。
            float  _WaterLabOceanDebug;
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);

            float DecodeEta(float e) { return e * 5.0 - 3.0; }

            float2 WorldToDomainUv(float3 worldPos)
            {
                return (worldPos.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
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
                float  eyeDepth   : TEXCOORD2; // 水面到相机的视空间深度（折射安全门比较用）
                float  visibleDepth : TEXCOORD3; // max(η−ground,0)，与原作 VS 的 v4.w 同义
            };

            Varyings WaterLabVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.visibleDepth = 0.0;

                if (_WaterLabSimEnabled > 0.5)
                {
                    float2 uv = WorldToDomainUv(positionWS);
                    float eta = DecodeEta(SAMPLE_TEXTURE2D_LOD(_WaterLabHeightTex,
                        sampler_WaterLabHeightTex, uv, 0).r);
                    float ground = DecodeEta(SAMPLE_TEXTURE2D_LOD(_WaterLabGroundTex,
                        sampler_WaterLabGroundTex, uv, 0).r);
                    // 原作 Lit_Water VS：顶点高度 = ground + max(h−0.05,0)，并把
                    // max(h−0.05,0) 作为可见水深传给 PS（PS 用它做 1e-5 裁剪）。
                    OUT.visibleDepth = max(eta - ground, 0.0);
                    positionWS.y += eta; // 网格基准 y=0（静水面）
                }

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;
                return OUT;
            }

            half4 WaterLabFragment(Varyings IN) : SV_Target
            {
                float t = _TimeParameters.x;
                float2 uv = WorldToDomainUv(IN.positionWS);
                float4 field = SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv);
                float groundRaw = SAMPLE_TEXTURE2D(_WaterLabGroundTex, sampler_WaterLabGroundTex, uv).r;

                // 可见水深用 VS 插值属性（原作同构），不逐像素重采样 η−ground。
                float depth = max(IN.visibleDepth, 0.0);

                bool dbgOn = _WaterLabOceanDebug > 0.5;
                if (_WaterLabSimEnabled > 0.5 && !dbgOn)
                    clip(depth - _DryClip);

                // ---- 法线 = 模拟坡度场（顶点法线）+ 细节法线贴图（原作：加后归一）。
                // y 增益 ≤1.5：过猛会把真实坡度放大成 60°+ 斜法线 → 菲涅尔恒 1。
                float2 slope = clamp(float2(field.g * 2.0 - 1.0, field.b * 2.0 - 1.0),
                                     float2(-0.6, -0.6), float2(0.6, 0.6));
                // y 增益 1.5→1.0：0.25 m 模拟网格的坡度在 pow1500 锐瓣下每个格子都能
                // 产出一个亮斑，合成全屏高频白噪；1.0 把法线摆幅从 ±42° 收到 ±31°，
                // 保留碎金但降低密度（参考是贴图长脊上的稀疏大颗粒）。
                float3 vertexNormal = normalize(float3(slope.x, 1.0, slope.y) * float3(1.0, 1.0, 1.0));

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
                // （亮脊网变稀的第二根因）；0.35 是「稀疏碎金」的观感档——参考头图浅水内
                // 极亮像素（lum>235）只占 0.05%，而纯相加全套强度（0.7071）下本仓是它的
                // 数倍，碎金从「稀疏亮点」变「整面白噪」。
                // 【别再往下压】实测 0.7071→0.22 时极亮占比几乎不动（0.36%→0.38%）：
                // 碎金主要来自**模拟坡度场的网格尺度法线**（原作格距 5.86 cm；本仓 1092²），
                // 细节法线只贡献表面细质。要压碎金应动本 shader 里顶点法线的 y 增益。
                float2 dn = (dA + dB) * 0.35 * detailFade;
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
                // 【已知缺口】深度重建折射点（真路径长度）暂缺；深度安全门已按原作
                // 补上：_CameraDepthTexture 的拷贝点在 AfterOpaques（见渲染器资产），
                // 与原作 CopyOpaqueDepth 同序。薄水膜处背景沙面比水面近 → 不偏移。
                float2 baseUV = IN.screenPos.xy / IN.screenPos.w;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 refrUV = baseUV + normalWS.xz * _RefractionFactor * float2(1.0, aspect);
                // 原作 PS：只有背景深度 > 水面深度才采纳折点。缺了这道门，5 mm 残水
                // 会折射采到画面另一侧的主水体，渲染成一条“断开的水带”（r80 断带根因）。
                float sceneEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                float2 sceneUV = (sceneEye > IN.eyeDepth) ? refrUV : baseUV;
                float3 bgColor = SampleSceneColor(sceneUV);
                float3 rayDir = normalize(IN.positionWS - GetCameraPositionWS());
                float dist = depth / max(-rayDir.y, 0.25);

                // ---- Beer–Lambert（原作 8.65617 字面量缩放；T 只调制底色，不再生成 alpha）
                // 焦散不在此处叠加：原作焦散在**沙侧** Lit_Sand（材质槽 causticsMap），
                // 水体只是把「已带焦散的沙渲染」折射+衰减过来——本仓同构（见 WaterLabSand）。
                float3 T = exp(-8.65617 * _Absorption.rgb * dist) * _WaterTint.rgb;

                // ---- 基底与散射混合（原作公式：底 0.9 + 曲率补偿 0.2·NdotV⁶；
                // **散射权重 = NdotV⁵ × 阴影软化 × 0.75**，与原作反汇编一致）----
                // 【为什么回到 NdotV⁵】第十八轮曾按「参考图近似俯视、水面无暖调」把它改成
                // (1−NdotV)⁵；但参考图是**近岸浅水暖沙调、只有远/深水才是宝蓝**，而
                // (1−NdotV)⁵ 恰好把暖橙散射加到掠射的远/深水——r53 实测底部深带 G 被抬到
                // 115（参考 70），水色发青绿。恢复原作 NdotV⁵ 后：俯视/近岸散射强（暖沙调）、
                // 掠射/远岸散射趋零（宝蓝不被染绿），两侧同时对上参考。
                float ndv2 = vDotN * vDotN;
                float ndv4 = ndv2 * ndv2;
                float3 baseCol = bgColor * T * (0.9 + 0.2 * ndv4 * ndv2);
                float scatterW = ndv4 * vDotN * shadowSoft * 0.75 * _ScatterStrength;
                float3 col = lerp(baseCol, _ScatterColor.rgb, scatterW);

                // ---- 太阳镜面：单瓣 pow(NdotH,1500) × 阴影原值（Lit_Water PS 全文只有
                // 一对 log+exp = 一个 pow，无第二宽瓣/GGX——旧记「GGX 宽瓣」系同一项的
                // 误读，第十二轮自造的 pow48 宽瓣按严格对标删除）。亮网的观感=原作
                // 贴图的长连通脊在 pow1500 下的碎金随相机距离在屏面上融合成网
                float3 h = normalize(viewDir + sunDir);
                float ndh = saturate(dot(normalWS, h));
                float spec = pow(ndh, _SpecularPower);
                col += _RefSunTint.rgb * spec * sunShadow;   // 原作太阳平行光实测色（白），镜面与沙面同源

                // ---- 深水 GI 兜底：原作体素 Gi + 天光全额的水底亮度，本仓的显示域近似。
                // 【加法，不是 lerp】（r2-gi 教训：lerp 权重饱和到 1 后深水被平涂成纯色、
                // 深度渐变和天空反射全被盖掉）——GI 是加法 irradiance，只做亮度提升，
                // 底色的 T 渐变保留。权重只看水下光程（dist），浅水（透底区）为 0、
                // 2.5 m 饱和；不乘阴影（原作 GI 是全局光照项，不经 shadowmap）。
                // 顺序在天空反射之前：顶部掠射的天空蓝不被兜底色压掉。
                // 定标见 Properties 注释。
                float giW = saturate(dist * 0.4) * _GIFillStrength;
                col += _GIFillColor.rgb * giW;

                // ---- 天空反射：原作 PS 步骤 8「按菲涅尔混环境色」（审计 §4 B8 缺件）----
                // 菲涅尔用**宏观视角** viewDir.y，而不是逐像素 normalWS：0.25 m 模拟坡度 +
                // 细节法线会让大量像素的 NdotV 偏小，强度 3 下天空色把整片水面刷亮
                // （r52 实证：水区 Y 103→153、顶部深带 (32,60,85)→(91,129,179)）。
                // 宏观角保留「远水掠射更蓝、近岸俯视不泛白」的空间梯度；碎金仍由
                // 细节法线走上面的镜面项。强度/色值是显示域标定，不是原作文件数值。
                float fresnel = pow(saturate(1.0 - viewDir.y), 5.0);
                col = lerp(col, _SkyReflectionColor.rgb, saturate(fresnel * _SkyReflectionStrength));

                // ---- 泡沫：不在本 PS（原作同构，白水走 Whitewater 粒子）----
                // 曾在此把模拟泡沫通道（field.a，实测浅水架上 0.25–0.9）lerp 进水面颜色，
                // 反解出的混合权重达 ≈0.85 → 整片海面刷白、水色与折射水底全被盖掉。
                // 该自造项按原作删除；泡沫场仍由模拟产出，只驱动白水粒子生成。

                // ---- 诊断分格（_WaterLabOceanDebug > 0.5）：3x3 共九格，每格三通道 ----
                // 【读数约定】出图 PNG 是显示编码（实测标准 sRGB）——按 0..1 线性直读会把
                // LabCamera 读成「幻影相机」（第十八轮误判的根因）。反解用
                // tools/waterlab/debug_tiles.py，本块数值不要肉眼估。
                // 格序：行 = floor(q.y*3)（q.y 向上，行 0 = 画面下排），列 = floor(q.x*3)。
                //   (0,0) depth/2 | dist/10 | (depth>_DryClip)
                //   (1,0) T
                //   (2,0) bgColor（折射取样原样）
                //   (0,1) frac((ws.x+32)/64) | frac((ws.z+32)/64) | 0.5   ← 世界 XZ 真值
                //   (1,1) _WorldSpaceCameraPos.xz/64+0.5 | .y/64
                //   (2,1) _ScreenParams.x/4096 | .y/4096 | _WaterLabSimEnabled
                //   (0,2) normalWS*0.5+0.5
                //   (1,2) spec*0.25 | sunShadow | shadowSoft
                //   (2,2) 最终 col（与实拍图对拍用）
                if (dbgOn)
                {
                    float2 q = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                    int tileCol = (int)clamp(floor(q.x * 3.0), 0.0, 2.0);
                    int row = (int)clamp(floor(q.y * 3.0), 0.0, 2.0);
                    float3 cpos = GetCameraPositionWS();
                    float3 dbg;
                    // 变量名不能叫 col：会遮蔽外层的 float3 col，(2,2) 格就输出成列号了。
                    if (row == 0 && tileCol == 0)      dbg = float3(depth * 0.5, dist * 0.1, depth > _DryClip ? 1.0 : 0.0);
                    else if (row == 0 && tileCol == 1) dbg = T;
                    else if (row == 0 && tileCol == 2) dbg = bgColor;
                    else if (row == 1 && tileCol == 0) dbg = float3(frac((IN.positionWS.x + 32.0) / 64.0),
                                                               frac((IN.positionWS.z + 32.0) / 64.0), 0.5);
                    else if (row == 1 && tileCol == 1) dbg = float3(cpos.x / 64.0 + 0.5, cpos.z / 64.0 + 0.5, cpos.y / 64.0);
                    else if (row == 1 && tileCol == 2) dbg = float3(_ScreenParams.x / 4096.0, _ScreenParams.y / 4096.0, _WaterLabSimEnabled);
                    else if (row == 2 && tileCol == 0) dbg = normalWS * 0.5 + 0.5;
                    else if (row == 2 && tileCol == 1) dbg = float3(spec * 0.25, sunShadow, shadowSoft);
                    else                               dbg = col;
                    return half4(dbg, 1.0);
                }

                // 不透明直出（与原作同队列：透明度只是上面的颜色项，alpha 不再生效）
                return half4(col, 1.0);
            }
            ENDHLSL
        }

    }
    Fallback Off
}
