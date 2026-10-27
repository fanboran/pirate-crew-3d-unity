// ============================================================================
// WaterLabOcean.shader —— Lit/Water 像素级移植正身（I1，编译器式逐指令翻译）。
//
// 【移植基准】external/sandcastle-ref/rt/reports/C-water-map.md（VS@246 107 指令 +
//   PS@8682 119 指令逐指令表 + 偏差清单 D1–D16）与 C-water.hlsl（直译底稿）；
//   真值出处 = A-sigma.md 槽位表（下发于 WaterLabReplicaGlobals.cs，本文件不写死
//   任何可调真值）。每个语义块前的注释引用 C 表行号（"PS L121-132：……"），
//   供逐节对账回溯。逆向台不可本地编译 HLSL——语法按 URP 2022.3 口径自查，
//   协调者出图时验证。
//
// 【C 表净语义（PS）】
//   clip(visibleDepth − 1e−5)；
//   phase  = 法线贴图.b(worldXZ×0.05) + time×0.8；
//   w1/tri/w2 = 相位驱动的锯齿/三角/锯齿波；flow ≡ (−0.5,−0.5)（VS 死通道活行为）；
//   细节法线 = ±0.25 双偏移 @×0.20 两采样 tri 混合 → y=sqrt 重建；
//   N = normalize(细节法线 + VS 字节坡度法线)；
//   折射 UV = 屏幕UV + (N.x, N.z×aspect)×0.0121，深度安全门（背景更近→不偏移）；
//   T = exp(−8.65617·σ·dist)×waterColor，**σ≡0（A 流定案）→ T≡waterColor**；
//   base = bg×T×(0.9 + 0.2·NdotV⁶)；
//   col  = lerp(base, 散射色, (1−NdotV)⁵ × min(sh+0.25,1) × 0.75)；
//   col += 太阳色 × pow(NdotH,1500) × (1−NdotV)⁴（不乘阴影）；
//   o0 = (col, saturate(visibleDepth×100))。
//
// 【VS 净语义】顶点高度 = (η南+η北)/2（仅南北两邻格，各解码两次的编译器展开数值
//   恒等）；坡度法线 = 自格 GB 反解（×2−1，y=sqrt 重建）；流 = 恒(−4,−4)（死通道）；
//   可见水深 = 自格可见水柱（供 PS clip/α）。
//
// 【本版相对旧版的删除清单（自造项坐实，C 表 §6 D1–D12/D15）】
//   * 散射权重方向：旧 NdotV⁵ 系反向（D1），改 (1−NdotV)⁵——旧文件头
//     "与原作反汇编一致"的断言错误；
//   * 镜面乘子：旧 spec×阴影 系误读（D2，寄存器活性链：r1.w 在 PS L131 被覆写为
//     (1−NdotV)⁴ 后于 L156 消费），改 (1−NdotV)⁴ 且不乘阴影；
//   * 天空反射整段、GI 兜底整段（D3/D4，原作 PS 无此二项）；
//   * _ScatterStrength 显示域旋钮（D11，其存在是 D1 反向后硬压的补偿）；
//   * detailFade 距离淡出与旧"双尺度相加"细节法线机制（D6，原作是单相位驱动、
//     双偏移、波形混合）；
//   * sunDir 兜底常量（D15，模拟侧 PublishSunDir 恒发非零向量）；
//   * _WaterLabOceanDebug 诊断分格块（D15 设施，复刻验收不需要）。
//   吸收 σ 反定标值 (5.6,1.94,0.35)/m 一并作废（A 流 §5：σ≡0 定案，旧三锚点
//   系对 tonemap/沙侧缺项的补偿拟合）。
//
// 【遗留 TODO】
//   * 渲染状态：Blend/ZWrite/ZTest 不在 DXBC blob（C 表 §4 缺口）；PS 输出
//     α=saturate(depth×100) 暗示原作可能混合——暂保持现行 Transparent+10/ZWrite On/
//     无 Blend，集成时按抓帧定。
//   * 原作 h 的"次加数"字段（u2 高 8bit + u3 bit8-23，VS L54-62）：打包端在原作
//     compute（E 流域），我方 RT 为浮点 η 无对应字段——暂用我方 η 全精度，见 VS 注。
//   * cb1[0].x 双解读（refractionFactor 材质槽 vs ObjectToWorld m00）：值同为
//     0.0121，抓帧终证时读 PS cb1[0].x 实测闭（见折射段注）。
// ============================================================================

Shader "PirateCrew/WaterLab/Ocean"
{
    Properties
    {
        // 细节法线（唯一材质属性，Setup 材质序列化绑定用）：
        // 原作 t3 = WaterNormal2D 1024²（交接档 §3），通道 = .rg 法线 xy（×2−1 解码）、
        // .b 相位（PS L43-44）；已接原作提取贴图（WaterNormal2D_Ref.png，r98）。
        _WaterLabDetailNormal   ("细节法线", 2D) = "gray" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Transparent+10"   // 调度位：落 _CameraOpaqueTexture 拷贝之后、白水 3050 之前
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 【渲染状态 TODO】原作 Blend/ZWrite 在 ShaderLab 序列化（blob 外，C 表 §4）；
            // 暂保持现行（透明调度位 + ZWrite On + 无 Blend），集成时按抓帧定。
            Cull Back
            ZWrite On

            HLSLPROGRAM
            // target 4.5：VS 的 Texture2D.Load（texelFetch/ld）最低要求
            #pragma target 4.5
            #pragma vertex   WaterLabVertex
            #pragma fragment WaterLabFragment

            // 主光阴影（D13：原作 sample_c_lz 单次比较无 PCF、z 越界强制 1；
            // 我方 MainLightRealtimeShadow 硬件 PCF+级联为语义近似，软化项同构）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里（单独 include Shadows 不够）
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ------------------------------------------------------------------
            // uniform（真值全部由 WaterLabReplicaGlobals.cs 下发，本文件不写死）。
            // 槽位对应（A-sigma.md §2）：_WaterLabWaterColor=cb0[131]、
            // _WaterLabScatterColor=cb0[121]、_WaterLabSunColor=cb0[125]、
            // _WaterLabRefractionFactor=cb1[0].x（PS）；_WaterLabSunDir 沿现有链
            // （WaterLabSimulation.PublishSunDir，世界空间指向太阳）。
            // ------------------------------------------------------------------
            float4 _WaterLabOrigin;          // (centerX, 域边长, centerZ, 水格数)——现有链
            float4 _WaterLabSunDir;          // 指向太阳（现有链）
            float  _WaterLabSimEnabled;      // 模拟开关（D15 本仓设施：关=平面+不裁剪）
            float4 _WaterLabWaterColor;      // cb0[131]
            float4 _WaterLabSigma;           // cb0[132] 运行时真值（R 流抓帧）=（0.993,0.175,0.0558）tint 域
            float4 _WaterLabScatterColor;    // cb0[121]
            float4 _WaterLabSunColor;        // cb0[125]
            float  _WaterLabRefractionFactor; // cb1[0].x（PS）

            // 我方水场 RT（compute OutputField 发布）：R=η 编码 (η+3)/5（[-3,2]m），
            // GB=坡度法线 xz 编码 (n+1)/2，A=泡沫。typed 声明供 VS Load（≡原作
            // ld_structured 的逐格最近读取）；双线性采样路径另配同名 sampler。
            Texture2D<float4> _WaterLabHeightTex; SAMPLER(sampler_WaterLabHeightTex);
            Texture2D<float4> _WaterLabGroundTex; SAMPLER(sampler_WaterLabGroundTex);
            TEXTURE2D(_WaterLabDetailNormal); SAMPLER(sampler_WaterLabDetailNormal);

            // 原作 wrap 惯用式（PS L46-52 / L58-62 编译展开）：
            //   wrap(x) = sign(x) × frac(|x|) ∈ (−1,1)
            float WaterWrap(float x)
            {
                float s = x >= -x ? 1.0 : -1.0;   // ge+movc 符号测试（PS L47-48 / L58-59）
                return s * frac(abs(x));          // PS L49-50 / L60-61
            }

            // η 解码（我方 RT 编码 (η+3)/5，compute OutputField 发布；量纲 = 米）
            float LoadEta(int2 cell)
            {
                return _WaterLabHeightTex.Load(int3(cell, 0)).r * 5.0 - 1.0;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;   // 原作 o0/v0
                float3 positionWS  : TEXCOORD0;     // 原作 o1/v1（y=邻域均值高度）
                float3 slopeNormal : TEXCOORD1;     // 原作 o2/v2（字节坡度法线；o2.w 死输出不声明）
                float4 screenPos   : TEXCOORD2;     // 折射屏幕 UV（D14：ComputeScreenPos ≡ 原作显式 1−v 翻转）
                float  eyeDepth    : TEXCOORD3;     // 自身线性眼深（深度安全门 selfEye，PS L94-95 等价）
                float  visibleDepth : TEXCOORD4;    // 原作 v4.w：可见水深（PS clip/α 用）
                // 原作 o3/TEXCOORD2 整体死输出（PS 未声明 v3）、o4.xy 流（死通道）、
                // o4.z（PS 未读）——均不声明。
            };

            // =========================================================================
            // 顶点着色器（C 表 VS@246，107 指令逐节对应）
            // =========================================================================
            Varyings WaterLabVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz); // VS L35-39（cb1[0..3]=ObjectToWorld）
                OUT.visibleDepth = 0.0;
                OUT.slopeNormal = float3(0.0, 1.0, 0.0);

                if (_WaterLabSimEnabled > 0.5)
                {
                    float res = _WaterLabOrigin.w;

                    // --- VS L40-47：世界 XZ → 整数格索引（截断+钳位） ---
                    // 格坐标口径与 compute 端一致：worldXZ = −D/2 + (格+0.5)×格距
                    // ⇒ 连续格坐标 fc = (worldXZ/D + 0.5)×res − 0.5（格心在整数点），
                    // 与原作 `saturate(wxz/cellSize + 0.5)·res − 0.5` 同构（域中心约定差异
                    // 已折进本式）。saturate 钳负侧（L41 add_sat）、max/min 钳到 [0,res−1]
                    // （L43-46）、截断取整（L47 ftoi）。
                    float2 uv = saturate((positionWS.xz - _WaterLabOrigin.xz) / _WaterLabOrigin.y + 0.5);
                    float2 fc = max(uv * res - 0.5, 0.0);
                    fc = min(fc, res - 1.0);
                    int2 ic = (int2)fc;
                    int ix = clamp(ic.x, 0, (int)res - 1);
                    int iy = clamp(ic.y, 0, (int)res - 1);

                    // --- VS L48-61/L94-95：顶点高度 = (η南 + η北) / 2 ---
                    // 原作仅取南北两邻（iy∓1，各钳位），且同格解码两遍（编译器内联展开，
                    // VS L71-94 第二遍数值恒等）——净语义就是两邻均值，直接写一次。
                    // 【h 语义差异】原作 h = max(u2&0xFFFFFF·1e−6 − 0.05, 0)
                    //                + ((u2>>24 | u3&0x00FFFF00)&0xFFFFFF)·1e−6
                    // （24bit 主高度 + u2/u3 跨字段的"次加数"，VS L54-62）；
                    // 我方 RT 直接存浮点 η（= ground + max(h−0.05,0)，半精度无量化）。
                    // TODO(次加数)：原作次加数的打包端在其 compute（E 流域），字段语义
                    // 未定；我方浮点 η 已含全精度，无对应拆分——暂用我方 η，
                    // L2 抓帧对账时若发现高度口径差再回此对齐。
                    int iyS = max(iy - 1, 0);            // VS L48-49 南邻钳位
                    int iyN = min(iy + 1, (int)res - 1); // VS L50 北邻钳位
                    float hS = LoadEta(int2(ix, iyS));   // VS L52 南格加载
                    float hN = LoadEta(int2(ix, iyN));   // VS L53 北格加载
                    float hAvg = (hS + hN) * 0.5;        // VS L61/L94-95（(hS+hN+hS+hN)×0.25 ≡ 均值）
                    // 原作 o1 = (wx, hAvg, wz)：世界 y 直接取均值高（网格基准 y=0，
                    // 故 += 与原作的绝对赋值等价）。
                    positionWS.y += hAvg;

                    // --- VS L72-73/L101-116：坡度法线（自格 GB 反解，y=sqrt 重建） ---
                    // 【两套编码等价性推导】原作把单位法线 xz 分量 n∈[−1,1] 打包成字节
                    // byte = (n+1)×127.5，解码 n = byte/127.5 − 1 = (byte/255)×2 − 1
                    // （0.007843=2/255，VS L103/L107）；
                    // 我方 compute OutputField 写 G = Nx×0.5+0.5、B = Nz×0.5+0.5
                    // （Nx=−∂η/∂x·invLen 等，编码前已按 invLen 归一成单位法线分量；
                    // 编码前另把坡度钳 ±0.6 防水跃爆表——compute 端注释，解码侧无需关心）。
                    // 两套都是 [−1,1]→存储域 的同一仿射映射 x↦(x+1)/2（原作多一步 ×127.5
                    // 取整 = 8bit 量化损失，我方 half 浮点无量化），解码同为 n = c×2−1，
                    // 量纲一致（单位法线的 xz 分量）。y 分量原作同样 sqrt 重建（L110-115），
                    // 我方 Load 值满足 nx²+nz² = 1−invLen² ≤ 1，sqrt 域安全；原作无钳位，
                    // 保真不遮。
                    float4 own = _WaterLabHeightTex.Load(int3(ix, iy, 0)); // VS L72-73 自格
                    float nx = own.g * 2.0 - 1.0;
                    float nz = own.b * 2.0 - 1.0;
                    OUT.slopeNormal = float3(nx, sqrt(1.0 - (nx * nx + nz * nz)), nz); // VS L116

                    // --- VS L107/L113：可见水深 = max(h − 0.05, 0) ---
                    // 原作从自格 u2 纯水柱量解出（无需地面）；我方 RT 的 R 是 η（内含地面），
                    // 需与地面配对相减消去：visibleDepth = η − ground。配对口径 = 同点双线性
                    // （r98 基线行为不变：逐顶点自洽对，水线平滑；逐格 Load 版属严格
                    // own-cell 语义，待 L2 抓帧对账后再定）。地面 R 编码同 η 式 (g+3)/5
                    // （WaterLabSimulation.CreateBuffers 上传端同式）。
                    float eta = SAMPLE_TEXTURE2D_LOD(_WaterLabHeightTex,
                        sampler_WaterLabHeightTex, uv, 0).r * 5.0 - 1.0;
                    float ground = SAMPLE_TEXTURE2D_LOD(_WaterLabGroundTex,
                        sampler_WaterLabGroundTex, uv, 0).r * 5.0 - 1.0;
                    OUT.visibleDepth = max(eta - ground, 0.0);
                }

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);   // VS L96-100（VP 行展开 ≡ 内建宏）
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);     // D14：URP 宏 ≡ 原作显式 y 翻转（cb0[12] 实效 (1,0)）
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;   // PS L94-95 selfEye 的 VS 预计算
                return OUT;
            }

            // =========================================================================
            // 像素着色器（C 表 PS@8682，119 指令逐节对应）
            // =========================================================================
            float4 WaterLabFragment(Varyings IN) : SV_Target
            {
                float3 wpos = IN.positionWS;

                // --- PS L40-42：岸线裁剪（字面量 1e−5；_WaterLabSimEnabled 为 D15 本仓
                //     设施：关=诊断平面模式不裁剪） ---
                if (_WaterLabSimEnabled > 0.5)
                    clip(IN.visibleDepth - 0.00001);

                // --- PS L43-54：相位与波形、流钳位 ---
                // 相位 = 法线贴图 .b @ worldXZ×0.05（L43-44，资源 swizzle zxyw 取 .b）
                //      + 时间×0.8（L45；cb0[113].y=_Time.y ≡ URP _TimeParameters.x，A 表 §2.2 槽 113）
                float phase = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    wpos.xz * 0.05).b;
                phase += _TimeParameters.x * 0.8;
                float w1 = 2.0 * WaterWrap(phase + 0.5) - 1.0;   // L46-52 第一锯齿波
                // 流场 = 恒 (−0.5,−0.5)：原作 VS 流通道 scale 字面量 <5e−7（打印 0.000000，
                // 通道禁用）→ o4.xy 恒 (−4,−4)（VS L129/L137/L140），PS 端 clamp(±0.5)
                // （L53-54）后成常量对角流。死通道按活行为写死，不平流任何场。
                float2 flow = float2(-0.5, -0.5);

                // --- PS L55-57：采样 A（偏移 +0.25，尺度 0.2） ---
                float2 nA = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    (wpos.xz - flow * w1) * 0.2 + 0.25).rg;

                // --- PS L58-68：第二波形链（wrap2/tri/w2）与采样 B（偏移 −0.25） ---
                float wrap2 = WaterWrap(phase);          // L58-62
                float tri = 2.0 * abs(wrap2 - 0.5);      // L63-64 三角波 ∈[0,2]
                float w2 = 2.0 * wrap2 - 1.0;            // L65 第二锯齿波
                float2 nB = SAMPLE_TEXTURE2D(_WaterLabDetailNormal, sampler_WaterLabDetailNormal,
                    (wpos.xz - flow * w2) * 0.2 - 0.25).rg;

                // --- PS L69-77：三角波混合双采样 → 细节法线（y=sqrt 重建） ---
                float a = 2.0 * lerp(nB.r, nA.r, tri) - 1.0;   // L69-71
                float b = 2.0 * lerp(nB.g, nA.g, tri) - 1.0;
                // 原作无钳位（真贴图 .rg 为法线域不会双饱和），保真不遮。
                float3 detailN = float3(a, sqrt(1.0 - (a * a + b * b)), b);  // L72-77

                // --- PS L78-81：N = normalize(细节法线 + 坡度法线) ---
                float3 N = normalize(detailN + IN.slopeNormal);

                // --- PS L82-90：折射 UV ---
                // aspect = cb0[8].x/|cb0[8].y|（TODO-slot，按屏幕纵横取，同现行）；
                // 折射偏移 = (N.x, N.z×aspect) × refractionFactor。
                // 【cb1[0].x 双解读 TODO】A 表 §2.2 依 VS 用法断言 CB1=ObjectToWorld、
                // PS L82 取 m00（物体世界 X 缩放进波纹强度）；而 PS 的 CB1 只声明 1 个
                // 向量（dcl CB1[1]），与材质属性槽（materials-dump refractionFactor=0.0121）
                // 更吻合——两读法值域一致（同槽同值），真值 0.0121 由 Globals 下发，
                // 抓帧终证时读 PS cb1[0].x 实测闭。不读我方 m00（=1 会改行为）。
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 baseUV = IN.screenPos.xy / IN.screenPos.w;   // L85-88（D14：ComputeScreenPos）
                float2 refrUV = baseUV + N.xz * _WaterLabRefractionFactor * float2(1.0, aspect);

                // --- PS L91-99：深度安全门（背景比水面近 → 回退不偏移；正面一致项） ---
                // bgEye = 1/(A·raw+B)（cb0[9].zw ≡ _ZBufferParams.zw，L92-93）；
                // selfEye 由 VS 预计算（L94-95）。门形式按原作：bgEye < selfEye → flipUV。
                float bgEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                float2 finalUV = (bgEye < IN.eyeDepth) ? baseUV : refrUV;   // L96-99

                // --- PS L104：折射底色（t1 ≡ URP _CameraOpaqueTexture 语义） ---
                float3 bg = SampleSceneColor(finalUV);

                // --- PS L100-115：透射（σ≡0 折叠，数学恒等非近似） ---
                // 原作：T = exp(−8.65617·σ·dist)×waterColor，dist 由 invVP 重建折射点
                // 世界坐标后取 |refPoint − wpos|（真水下光程，L100-110）。
                // 【A 流定案】σ=cb0[132] 无任何上传者 → ≡(0,0,0) → exp 项恒 1、dist 成
                // 死变量 → T ≡ waterColor，整段 invVP 重建折叠。
                // 恢复条件：抓帧终证 σ≠0（A-sigma §6 路线①）时，按 C-water.hlsl
                // L248-255 的正身恢复整段（含 8.65617 字面量）。
                // 真身恢复（R 流抓帧终证 σ≠0）：T = exp2(−8.65617·σ·dist)×waterColor
                //（exp2 语义 = e^(−6σd)，X 流）；dist = invVP 重建折射点世界坐标的水下光程。
                // 原作 L100-110：clipP = InvVP·(ndc.x, −ndc.y, **rawDepth(finalUV)**, 1)，w 除回世界。
                // 深度用 finalUV 处的原始设备深度（L103-105 第三行乘的就是 depth 采样值，
                // 不是线性视深——r100 首版传 bgEye 致 30% 黑的根因）。
                float rawAtFinal = SampleSceneDepth(finalUV);
                float3 refrPointW = ComputeWorldSpacePosition(finalUV, rawAtFinal, UNITY_MATRIX_I_VP);
                float dist = distance(refrPointW, wpos);
                float3 T = exp2(-8.65617 * _WaterLabSigma.rgb * dist) * _WaterLabWaterColor.rgb;
                float3 bgT = bg * T;   // L115（bg×T）

                // --- PS L116-128：视线、NdotV、基底（正面一致项） ---
                float3 V = GetCameraPositionWS() - wpos;   // L116（cb0[6] 世界相机位）
                float3 Vn = normalize(V);                  // L117-119
                float NdotV = saturate(dot(Vn, N));        // L121
                float ndv2 = NdotV * NdotV;
                float3 base = bgT * (0.9 + 0.2 * ndv2 * ndv2 * ndv2);   // L122-128（NdotV⁶）

                // --- PS L129-146：散射（权重方向 D1：(1−NdotV)⁵ = 掠射增） ---
                // 阴影：D13 近似（原作 sample_c_lz 单次比较、无 PCF、z 越界强制 1；
                // 我方走 URP 主光阴影）。软化 min(sh+0.25,1) 只接散射（正面一致项，
                // 影子最深 75%）。
                float sunShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(wpos));
                float shadowSoft = min(sunShadow + 0.25, 1.0);          // L103-104/L142-143
                float om = 1.0 - NdotV;                                  // L123
                float om2 = om * om;
                float scatterW = om * om2 * om2 * shadowSoft * 0.75;     // L130-132 (1−NdotV)⁵ × L144-145
                float3 col = lerp(base, _WaterLabScatterColor.rgb, scatterW);   // L146（≡ base + w×(散射−base)）

                // --- PS L147-156：镜面（pow1500，乘 (1−NdotV)⁴，不乘阴影——D2） ---
                // 半矢量：原作 H = normalize(V̂ − cb0[123])，cb0[123] = DayCycler
                // sunDirection = 光**传播**方向（C 表 PS L120 注）；我方链给指向太阳的
                // L（_WaterLabSunDir），传播方向 = −L ⇒ H = normalize(V̂ + L)。
                float3 H = normalize(Vn + _WaterLabSunDir.xyz);
                // pow1500 编码 = log→×1500→exp（L151-154）≡ pow（单瓣，无第二宽瓣）。
                float spec = pow(saturate(dot(N, H)), 1500.0);
                col += _WaterLabSunColor.rgb * spec * (om2 * om2);   // L155-156，(1−NdotV)⁴ 不乘阴影

                // --- PS L157：α = saturate(可见水深 × 100) ---
                // 无 Blend 下 α 仅写入 RT alpha 通道；混合状态 TODO（文件头），集成时按抓帧定。
                return float4(col, saturate(IN.visibleDepth * 100.0));
            }
            ENDHLSL
        }

    }
    Fallback Off
}
