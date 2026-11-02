// ============================================================================
// WaterLabSand.shader —— 沙面复刻（I2）：Lit_Sand PS @34958 逐指令直译。
//
// 【真值源（禁止拟合）】
//   PS = Lit_Sand@34958.ps_5_0（144 实指令 + ret，5 采样）
//   VS = Lit_Sand@11358（主变体不抬顶点）
//   逐指令表与档案修正：external/sandcastle-ref/rt/reports/D-sand.md（下称「D 表」）
//   可集成底稿：同目录 D-sand.hlsl（本文件按它落笔，注释「PS:n」= 反汇编行号）
//
// 【I2 变更】焦散换真身（烘焙 Texture3D 256×256×64 R8，半相位交叉淡化采样，D 表§1/§2.3
//   段 A/B）——细节法线散度程序焦散退役；删 _Sparkle* 哈希闪沙（D 表§5-3：原作无独立闪沙，
//   「闪」即焦散项本身，干沙 gate=0）；沙色改 Carbonate 预设下发（D 表§4.2，.mat 旧值作废）；
//   坡度只门控焦散不暗化 albedo（D 表§2.4 修正③）；深水沙变体按 @57462+@85014 直译（σ≡0）。
//
// 【A 流变更（GI 消费端闭合）】段 G 环境项换 E 流 idiom：
//   indirect = albedo × lerp(unity_AmbientSky, giVoxels, inDomain)
//   （E 报告 rt/reports/E-gi-pipeline.md §1.3③，Lit_Sand@34958.ps:102-113 逐指令；
//   t2 正身 = giVoxels 全局纹理（BlurGi 输出，Gi.cs:186），cb0[40] = unity_AmbientSky——
//   I2 的 _AmbientColor 平色近似与 _DomainTintFallback 占位随本段退役）。
//   另新增 Voxelizer pass（体素化 rgb/w 来源，见该 pass 注释）。
//
// 【本仓解析场（D 表§5-7/§5-13 登记）】原作流速/水位/水深/22bit 场全由渲染缓冲逐顶点供，
//   本仓无渲染缓冲层——水位场以 η−ground 解析等价（B 流快照对账），水位深度 = max(η−ground,0)，
//   可见水深 = max(水位−0.05, 0)（5cm 水膜，同原作 VS o4.w 口径）。
//
// 【pass 布局】共用代码在 SubShader 级 HLSLINCLUDE（A 流为第二个 pass 复用同一着色链）；
//   ForwardLit 恒为第 0 个 pass（装配侧按默认 pass 消费，勿改顺序）。
// ============================================================================

Shader "PirateCrew/WaterLab/Sand"
{
    Properties
    {
        // 【三色 = Carbonate 预设（SandType.Carbonate=type0）】D 表§4.2 从 Refs 裸字节
        // 恢复的 linear 序列化值；主菜单跑该预设（Sand 材质序列化 sandColor 与
        // type0.color 逐位相同）。Linear 工程 Color 属性会被再 gamma 一次（交接§6 陷阱），
        // 故用 Vector 下发、物理量原样直达。旧 _DrySandColor/_WetSandColor 头图反定值作废。
        _SandColorDry    ("沙基色 cb0[127]（Carbonate 预设 color，linear 值）", Vector) = (0.9255, 0.8863, 0.7255, 1)
        _SandColorWet    ("湿沙色 cb0[129]（预设 wetColor，湿/干比 ≈0.85/0.86/0.89）", Vector) = (0.7890, 0.7592, 0.6476, 1)
        _SandColorThird  ("第三色 cb0[130]（预设 color2，水侧场×20 混入）", Vector) = (0.8353, 0.8078, 0.6902, 1)
        // 深水沙变体目标色（cb0[131] t=1/3 运行时值，R 抓帧实测；序列化 0.9603… 惰性残留；
        // 运行时由 Setup ApplySandReplicaValues 下发真值，此处默认值仅兜底）
        _WaterColor      ("水色 cb0[131]（深水沙变体 4× 目标）", Vector) = (0.7835, 0.9486, 1.0, 1)

        // 焦散真身：原作 Caustics 256×256×64 R8_UNorm 9 mip（path_id 932）。
        // 资产由 WaterLabCausticsImport.EnsureCausticsAsset() 从 raw 逐 mip 构建并存盘。
        _CausticsTex     ("焦散烘焙体（原作 Caustics Texture3D）", 3D) = "gray" {}
        // 沙噪声真图：原作 Lit_Sand t3，1024²（path_id 232 "Sand"）——R=逐 texel 白噪声
        // （std 0.161，±0.2·n.y 调制 albedo），G=低频流（焦散相位 base，PS:41-43）。
        _WaterLabSandMap ("沙噪声 1024²（R 白噪声 / G 低频流）", 2D) = "gray" {}

        // 显示链曝光因子（本仓 LDR 直出链的补偿旋钮，非原作项）
        _LightScale      ("显示链曝光因子", Range(0.2, 3.0)) = 1.0

        // 【A 流退役】_DomainTintFallback/_AmbientColor 两占位删除：域 tint 回退 = cb0[40]
        //   正身 unity_AmbientSky（URP 每相机上传 RenderSettings.ambientSkyColor，与原作
        //   SetupPerFrameShaderConstants 同源，E 报告 §1.1），域内 = giVoxels 采样——
        //   见 HLSLINCLUDE 的 WaterLabGiAmbient。装配侧对这两属性的 SetColor 变为惰性
        //   no-op（材质 Set 缺失属性静默忽略），WaterLabSetup.cs 清理归 B 流。
        // 太阳色 cb0[125]（默认值 = level0 SunLight 序列化色；其模长乘进焦散强度 PS:77-79）
        _SunTint         ("太阳色（level0 SunLight）", Color) = (1.0, 0.9709, 0.9403, 1.0)

        // 天光第二方向光（原作 MoonLight，level0 A 级真值）：Vector 下发不走色彩空间。
        // 方向 = 照射方向（指向光源）。不采影不衰减，见片元内注释。
        _SkyLightDir     ("天光方向（照射方向）", Vector) = (0.35, 0.90, 0.26, 0.0)
        _SkyLightColor   ("天光色（线性域）", Vector) = (0.0627, 0.1333, 0.2196, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里（单独 include Shadows 不够）
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _SandColorDry;
            float4 _SandColorWet;
            float4 _SandColorThird;
            float4 _WaterColor;
            float  _LightScale;
            float4 _SunTint;
            float4 _SkyLightDir;
            float4 _SkyLightColor;
        CBUFFER_END

        float4 _WaterLabOrigin;
        float4 _WaterLabSunDir;
        TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);
        TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
        TEXTURE2D(_WaterLabSandMap);   SAMPLER(sampler_WaterLabSandMap);
        TEXTURE3D(_CausticsTex);       SAMPLER(sampler_CausticsTex);

        // ---- GI 输入（A 流）----
        // giVoxels 全局纹理（名字面 = 原作 SetGlobalTexture("giVoxels", giVoxelsBlurred)，
        // Gi.cs:186；ReplicaGiSystem.Render 每帧发布）＝ BlurGi 的模糊输出，双线性 + Clamp
        // （RT 创建参数见 ReplicaGiSystem.NewVoxelRT）。
        TEXTURE2D(giVoxels); SAMPLER(sampler_giVoxels);
        // 域掩码 uniform（I3a 发布，ReplicaGiSystem.PushConstants）：
        //   g_GiDomainMin = (−15,0,−15)、g_GiDomainSize = (30,0,30)（WaterLabRules.DomainSize）。
        float4 g_GiDomainMin;
        float4 g_GiDomainSize;

        // 地面/水位场编码（I7）：e×5−3，量程 [−3,2] 在干沙丘顶 2.6 m 处饱和——
        // 扩量程跨 Ocean shader 域，由协调者集成轮统一改（I7 偏差注记），沙侧解码保持现行。
        float DecodeGround(float e) { return e * 5.0 - 1.0; }

        float2 WorldToDomainUv(float3 worldPos)
        {
            return (worldPos.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5;
        }

        // ------------------------------------------------------ GI 环境项 --
        // E 流 idiom（E 报告 rt/reports/E-gi-pipeline.md §1.3③，四消费 shader 同构；
        // Lit_Sand@34958.ps:102-113）。返回 lerp(unity_AmbientSky, giVoxels, inDomain)：
        //   PS:103  div  world.xz / beachSize.xz
        //   PS:104  add_sat +0.5                          → uv ∈ [0,1]
        //   PS:106  sample_l t2(giVoxels), s1, LOD 0      → 双线性，零 mip 偏置
        //   PS:107  add  gi − cb0[40]
        //   PS:108-110 mask = |x|<beachSize.x/2 && |z|<beachSize.z/2（and 0x3f800000 系）
        //   PS:111  mad  mask·(gi−cb0[40]) + cb0[40]      → 域外回退 cb0[40]
        // cb0[40] 正身 = unity_AmbientSky：URP 每相机上传
        // ConvertSRGBToActiveColorSpace(RenderSettings.ambientSkyColor)
        // （URP 14 UniversalRenderPipeline.cs:1695）＝原作上传路径同源（E 报告 §1.1 表
        // SetupPerFrameShaderConstants 行）；主菜单固定时刻 t=1/3 的 ambientSkyColor 定值
        // 由装配侧 RenderSettings 承担（H 流定案）。
        float3 WaterLabGiAmbient(float3 worldPos)
        {
            float2 center = g_GiDomainMin.xz + g_GiDomainSize.xz * 0.5;  // 域中心（原作=原点）
            float2 half_  = g_GiDomainSize.xz * 0.5;
            float2 uv  = saturate(worldPos.xz / g_GiDomainSize.xz + 0.5);            // PS:103-104
            float3 gi  = SAMPLE_TEXTURE2D_LOD(giVoxels, sampler_giVoxels, uv, 0).rgb; // PS:106
            float2 d   = worldPos.xz - center;
            float  mask = ((abs(d.x) < half_.x) && (abs(d.y) < half_.y)) ? 1.0 : 0.0; // PS:108-110
            float3 amb = unity_AmbientSky.rgb;                                        // cb0[40]
            return mask * (gi - amb) + amb;                                           // PS:107,111 字面算术
        }

        // 有符号小数：PS:44-49 / 61-66 的 sign·frac(|x|) 手工展开（D-sand.hlsl 同款）
        float SandSignedFrac(float x)
        {
            float s = (x >= -x) ? 1.0 : -1.0;   // PS:45-46 movc
            return s * frac(abs(x));            // PS:47-49
        }

        // -------------------------------------------------- 焦散双采样体 --
        // PS:41-74 逐指令（D 表§2.3 段 A/B，D-sand.hlsl DSandCaustics 直译）：
        // 两次体采样在 xy/z 各差 ±0.5 相位，按三角权 2|signedfrac(base)−0.5| 交叉淡化
        // ——「烘焙体 + 半相位淡化」结构（kill 平铺 + 沸腾动画），不读水位场。
        //   noiseG = sandMap.g @ worldXZ×0.3（低频流，相位 base）
        // 【流场代位】xy 平流项的流速 v4.xy 原作来自水渲染缓冲；本仓模拟层暂无速度
        //   纹理发布（速度在 compute buffer 内），以 0 代入（语义 = 无整体漂移；
        //   半相位机制不受影响——采样对仍按相位在体上互走）。接入点：速度场出全局纹理后回填。
        // 【死通道】第二平流源 v5.xy = 常量 (−4,−4) 的禁用场（D 表§2.2 VS 表 o5），
        //   原作即按常量参与交叉淡化相位——照抄保留，不是简化。
        float SandCaustics(float2 worldXZ, float noiseG)
        {
            const float2 flowVel   = float2(0.0, 0.0);   // v4.xy 代位（TODO: 速度场纹理接入）
            const float2 flowDead  = float2(-4.0, -4.0); // v5.xy 死通道常量（原作同值）

            float  base = noiseG + 0.5 * _Time.y;                 // PS:43（cb0[113].y = _Time.y 口径）
            float2 cuv  = worldXZ * 0.25 + flowVel * 0.1;         // PS:52-53

            float  sa = SandSignedFrac(base + 0.5) - 1.0;         // PS:44-50
            float3 s1 = float3(cuv - 0.1 * sa * flowDead - 0.5,   // PS:51,54-55
                               0.2 * (noiseG + _Time.y - 0.5));   // PS:56-59（z 时间滚动，Repeat）
            float a1 = SAMPLE_TEXTURE3D_LOD(_CausticsTex, sampler_CausticsTex, s1, 0).r; // PS:60

            float sf = SandSignedFrac(base);                      // PS:61-66
            float w  = 2.0 * abs(sf - 0.5);                       // PS:67 三角权
            float sb = 2.0 * sf - 1.0;                            // PS:68
            float3 s2 = float3(cuv - 0.1 * sb * flowDead + 0.5,   // PS:69-71
                               0.2 * (noiseG + _Time.y + 0.5));   // PS:58-59
            float a2 = SAMPLE_TEXTURE3D_LOD(_CausticsTex, sampler_CausticsTex, s2, 0).r; // PS:72

            return lerp(a2, a1, w);                               // PS:73-74
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
            float3 deepColor  : TEXCOORD2; // 深水变体 o3（默认变体不消费）
            float  sandY      : TEXCOORD3; // 抬升前沙面世界高度（PS:184 alpha）
        };

        Varyings SandVertex(Attributes IN)
        {
            Varyings OUT;
            float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
            OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
            OUT.sandY = positionWS.y;

    #if _WATERLAB_DEEP_SAND
            // 深水变体 VS @57462（D 表§2.1/§2.4-6）：顶点抬升 = 沙高 + 可见水深。
            // 本仓地形网格恒等变换（OS.xz ≈ 世界 XZ），直接以世界坐标采场。
            float2 uv = WorldToDomainUv(positionWS);
            float ground = DecodeGround(SAMPLE_TEXTURE2D_LOD(
                _WaterLabGroundTex, sampler_WaterLabGroundTex, uv, 0).r);
            float eta = DecodeGround(SAMPLE_TEXTURE2D_LOD(
                _WaterLabHeightTex, sampler_WaterLabHeightTex, uv, 0).r);
            float waterDepth = max(eta - ground, 0.0);
            float visibleDepth = max(waterDepth - 0.05, 0.0);
            // o3 = lerp(沙色, 4×水色×exp(−8.65617·σ·d), min(10·d,1))。
            // σ≡0（A-sigma.md 定案：cb0[132] 无任何上传者 → exp 项恒 1），σ 不设 uniform；
            // 4× 直译保留（原作 HDR 链量纲，LDR 直出下由输出饱和截顶）。d = 可见水深。
            OUT.deepColor = lerp(_SandColorDry.rgb,
                                 4.0 * _WaterColor.rgb,
                                 min(10.0 * visibleDepth, 1.0));
            positionWS.y += visibleDepth;
    #else
            OUT.deepColor = float3(0.0, 0.0, 0.0);
    #endif

            OUT.positionWS = positionWS;
            OUT.positionCS = TransformWorldToHClip(positionWS);
            return OUT;
        }

        // 着色主体（A 流：段 A–G 全链，两 pass 共用）。返回 albedo×(直射×阴影×水柱
        // + 天光第二灯) + albedo×lerp(unity_AmbientSky, giVoxels, inDomain)——显示链
        // 补偿（_LightScale）与输出通道由各 pass 自行落。
        float3 SandShade(Varyings IN)
        {
            float3 n = normalize(IN.normalWS);
            float ny = max(n.y, 0.0);                                        // PS:95

            // ---- 场基准（本仓解析场，D 表§5-7 登记：原作 22bit 渲染缓冲直读）----
            float2 uv = WorldToDomainUv(IN.positionWS);
            float ground = DecodeGround(SAMPLE_TEXTURE2D(
                _WaterLabGroundTex, sampler_WaterLabGroundTex, uv).r);
            float eta = DecodeGround(SAMPLE_TEXTURE2D(
                _WaterLabHeightTex, sampler_WaterLabHeightTex, uv).r);
            float waterDepth = max(eta - ground, 0.0);        // 原作 22bit 水侧场（米）
            float visibleDepth = max(waterDepth - 0.05, 0.0); // 原作 o4.w（5cm 水膜，VS 表）

            float3 albedo;
    #if _WATERLAB_DEEP_SAND
            // 深水变体 PS @85014（D 表§2.1：无噪声无焦散）——albedo 直接取顶点 o3。
            // 光照尾链与主变体同构（@85014 CB0 145 槽 ≥ 主变体，直射/第二灯全保留；
            // 该 PS 未逐指令转录，结构按 D 表§2.1 描述对位——登记待抓帧终审）。
            albedo = IN.deepColor;
    #else
            // ---- 段 A：沙噪声（PS:41-42）——R 白噪声 / G 低频流 ----
            float2 sandMap = SAMPLE_TEXTURE2D(
                _WaterLabSandMap, sampler_WaterLabSandMap, IN.positionWS.xz * 0.3).rg;

            // ---- 段 A/B：焦散体双采样半相位交叉淡化（PS:43-74）----
            float caustics = SandCaustics(IN.positionWS.xz, sandMap.g);

            // ---- 段 C：焦散强度（PS:52,75,77-80）----
            // causticsVal = len(太阳色) × exp(−1.731234×可见水深) × 体采样。
            // 「闪沙」不存在——本项即原档案误读的闪沙（D 表§2.4 修正②/§5-3）。
            float causticsVal = length(_SunTint.rgb)
                              * exp2(-1.731234 * visibleDepth)  // exp2 语义（X 流复核）：e^(−1.2·d)
                              * caustics;

            // ---- 段 C/D：干/湿/第三色（PS:81-93）----
            // 湿带驱动源是**水位深度**（≈2 cm 全湿），与水量/湿痕场无关（D 表§2.4 澄清③）；
            // 原作 v4.z=水位深度 22bit，本仓以 waterDepth 解析代位。
            float wetBand = saturate((saturate(waterDepth) - 0.001) * 52.63158); // PS:81-85
            float smoothWet = wetBand * wetBand * (3.0 - 2.0 * wetBand);         // PS:86-88
            albedo = lerp(_SandColorDry.rgb, _SandColorWet.rgb, smoothWet);      // PS:89-90
            // 第三色：sat(v5.w×20)，5 cm 满（D 表§5-5：v5.w=22bit 水侧场）。
            // 【L3-water-attrib §2.3#1 定案】原作该场海区实测 ≈0（水下=湿沙色），本仓
            // 深度代位场海区恒饱和 → 水下 albedo 错落第三色 (G/B=湿沙 51%/39%，中水
            // G−55%/B−63% 主源）。22bit 场身份 B/E 终审前**钳 0 挂起第三色 lerp**，
            // 禁止用深度族代位（透射链无罪：σ exp2 已在原作数据复核、B 绝对量闭合 1%）。
            albedo = lerp(albedo, _SandColorThird.rgb, 0.0 /*saturate(waterDepth*20) 代位作废*/); // PS:91-93

            // ---- 段 D：白噪声调制 albedo（PS:94-99；干湿皆在，无时间项）----
            albedo += 0.2 * ny * (albedo * ((2.0 * sandMap.r) - 1.0));           // PS:94,96-99
            // 坡度门控：**只门控焦散、不暗化 albedo**（D 表§2.4 修正③）
            float slopeGate = saturate((ny - 0.2) * 10.0);                       // PS:97-99

            // ---- 段 E：焦散并色（PS:100-105）----
            // 注意有两个同型 smoothstep：颜色湿带用 S=×52.63（水位，上面），
            // 焦散掩码用 T=min(sat(可见水深)×100,1)（1 cm 满），输入不同不可复用。
            float subT = min(saturate(visibleDepth) * 100.0, 1.0);               // PS:76,81-84
            float subMask = subT * subT * (3.0 - 2.0 * subT);                    // PS:100-102
            float wetMask = subMask * saturate(visibleDepth * 5.0);              // PS:103
            albedo = saturate(causticsVal * (wetMask * slopeGate) + albedo);     // PS:104-105
    #endif

            // ---- 段 F：水柱 × 阴影 × 光照（PS:106-173）----
            // column = exp(−0.721348×场)——场=逐像素 22bit 水侧场双线性（§5-7）。
            // 【同 L3-water §2.3#2】原作场海区 ≈0 → column ≈1（不衰减）；深度代位海区
            // e^(−0.5d) 多衰 10–22%。22bit 终审前短期**钳 1**（与第三色钳 0 成对）。
            float column = 1.0 /*exp2(-0.721348 * waterDepth) 代位作废*/;
            // dot(N,−太阳方向)（PS:167-169）；_WaterLabSunDir 指向太阳，零向量兜底与装配器同向
            float3 sunRaw = _WaterLabSunDir.xyz;
            float3 sunDir = (dot(sunRaw, sunRaw) < 0.25)
                ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);
            float ndl = saturate(dot(n, sunDir));
            // 阴影：URP 比较采样，语义同原作 sample_c_lz 点比较（无 PCF；越界恒 1，
            // PS:161-165——URP 阴影距离外同样返回 1）
            float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
            // 天光第二方向光（PS:170-172）：不采影不衰减，全额到达
            float ndl2 = saturate(dot(n, normalize(_SkyLightDir.xyz)));
            float3 light = _SunTint.rgb * ndl * sh * column                    // PS:166,169,173
                         + _SkyLightColor.rgb * ndl2;                          // PS:172-173

            // ---- 段 G：合成（PS:174-184 + E §1.3③ GI 项）----
            // 原作合成 = albedo×(直射×阴影×水柱 + 第二灯) + albedo×lerp(cb0[40], t2.rgb, inDomain)
            //   （D 表§2.4-5 + E 报告 §1.3③：t2 正身 = giVoxels（BlurGi 输出），
            //   cb0[40] = unity_AmbientSky；PS:113 mad o0.xyz, albedo×direct + r2×albedo）。
            // 【A 流退役】_AmbientColor 平坦近似与 _DomainTintFallback 占位删除——域外回退
            //   就是 WaterLabGiAmbient 的 unity_AmbientSky 分支。
            return albedo * (light + WaterLabGiAmbient(IN.positionWS));
        }
        ENDHLSL

        // --------------------------------------------------------------- 主 pass
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

            // 主光阴影。**不带 _MAIN_LIGHT_SHADOWS_SOFT**：原作是 sample_c_lz 点比较、
            // 无软化（D 表§2.3 段 F / §5-7），URP SOFT 关键字开启即偏离口径。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            // 深水沙变体（原作 @57462+@85014 家族，D 表§2.4-6）：顶点抬升+深水变色。
            // 主菜单是否启用属运行时状态（D 表§6 待抓帧终审）——默认关，材质开关键字即换变体。
            #pragma shader_feature_local _WATERLAB_DEEP_SAND  // 全阶段变体（曾用 _vertex：PS 的 #if 恒假，r99b 帧零变化的根因）

            half4 SandFragment(Varyings IN) : SV_Target
            {
                float3 col = SandShade(IN);
                return half4(col * _LightScale, IN.sandY);   // PS:182-184（alpha=沙面世界高度，
                                                             // 未见消费方，D 表§5-11 登记直译）
            }
            ENDHLSL
        }

        // ------------------------------------------------------ 体素化 pass --
        // 【GI-calib 定案（rt/reports/GI-calib.md §2/§7）】原作顶视第三程序段正身 =
        // Voxelizer（VS@73626 L34-170 + PS@79502..85014；旧"深水变体"命名作废——K 裁决
        // 在 ForwardLit 关它是正确的，但该代码本属此 pass）：
        //   * 顶点抬升：positionWS.y = ground + max(η−ground−0.05, 0)（抬到水面−5cm）；
        //   * albedo o3 = lerp(干沙色, 4·waterColor·exp2(−8.65617·σ·col), min(10·col,1))，
        //     σ=cb0[132]=(0.9930,0.1754,0.0558) 逐通道（R2 实读；σ≡0 旧案已翻）；
        //   * 无焦散/无沙噪声/无干湿第三色带（Voxelizer 段无 Texture3D dcl 实证）；
        //   * 光照 = 水面法线（原作 waterCells[offset0]，本仓 η 梯度解析，平水 (0,1,0)）
        //     直射×阴影 + 第二灯 + GI 反馈（WaterLabGiAmbient）；
        //   * α = 抬升后世界 Y（sceneVoxels.w 语义——GI kernel 的 DDA 高度场随此得到
        //     海面 plateau，对齐原作 .w=1.22/结构）。
        // 顶视 47.9% 面积即此水面体（原作类均值 (0.53,1.25,1.16)），G 优势真身；
        // 按其颜色替换我方水面积后总均值比原作 1.007/0.964/0.953（内容差单项闭环）。
        Pass
        {
            Name "Voxelizer"
            Tags { "LightMode" = "WaterLabVoxelizer" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   SandVoxelVertex
            #pragma fragment SandVoxelFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            Varyings SandVoxelVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.sandY = positionWS.y;

                float2 uv = WorldToDomainUv(positionWS);
                float ground = DecodeGround(SAMPLE_TEXTURE2D_LOD(
                    _WaterLabGroundTex, sampler_WaterLabGroundTex, uv, 0).r);
                float eta = DecodeGround(SAMPLE_TEXTURE2D_LOD(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, uv, 0).r);
                float waterDepth = max(eta - ground, 0.0);
                float col = max(waterDepth - 0.05, 0.0);          // 抬升量（5 cm 水膜下限同 VS 表）
                static const float3 VoxelSigma = float3(0.9930, 0.1754, 0.0558); // cb0[132] R2 实读
                OUT.deepColor = lerp(_SandColorDry.rgb,
                                     4.0 * _WaterColor.rgb * exp2(-8.65617 * VoxelSigma * col),
                                     min(10.0 * col, 1.0));
                positionWS.y += col;                              // 抬到水面−5cm
                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 SandVoxelFragment(Varyings IN) : SV_Target
            {
                // 水面法线：η 梯度解析（原作 waterCells[offset0] 的本仓等价；平水 (0,1,0)）
                float2 uv = WorldToDomainUv(IN.positionWS);
                const float texel = 1.0 / 512.0;   // 水场 512²（B 流：5.86 cm/格）
                float etaC = DecodeGround(SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv).r);
                float etaX = DecodeGround(SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv + float2(texel, 0)).r);
                float etaZ = DecodeGround(SAMPLE_TEXTURE2D(_WaterLabHeightTex, sampler_WaterLabHeightTex, uv + float2(0, texel)).r);
                // 斜率 = Δη/格距（格距=域 30m/512 格），法线 = normalize(−slopeX, 1, −slopeZ)
                // 通乘 30 消分数：∝ ((etaC−etaX)·512, 30, (etaC−etaZ)·512)
                float3 n = normalize(float3((etaC - etaX) * 512.0, 30.0, (etaC - etaZ) * 512.0));

                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25)
                    ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);
                float ndl = saturate(dot(n, sunDir));
                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float ndl2 = saturate(dot(n, normalize(_SkyLightDir.xyz)));
                // 水柱项与 ForwardLit 成对钳 1（同一 22bit 场身份，L3-water §2.3#2；禁深度代位）
                const float column = 1.0;
                float3 alb = saturate(IN.deepColor);
                float3 rgb = alb * (_SunTint.rgb * ndl * sh * column + _SkyLightColor.rgb * ndl2)
                           + alb * WaterLabGiAmbient(IN.positionWS);
                return half4(rgb, IN.positionWS.y);   // α=抬升后世界 Y（sceneVoxels.w 语义）
            }
            ENDHLSL
        }
    }
    Fallback Off
}
