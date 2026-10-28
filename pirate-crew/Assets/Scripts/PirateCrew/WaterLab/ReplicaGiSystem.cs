using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// Sandcastle Gi 系统复刻的运行时调度（I3a）：顶视 RT 渲染 + compute 派发 +
    /// 全局纹理发布。compute 本体 = <c>WaterLabGi.compute</c>（E-gi.compute.hlsl 直译）。
    ///
    /// 【原作链（E 报告 rt/reports/E-gi-pipeline.md §2.1）】beach.Draw 内：
    /// SubmitRenderRequest(topViewCamera) → 管线 ProcessRenderRequests
    /// （BeachRenderPipeline.cs:1493-1610：正交顶视相机渲 opaque×"Voxelizer" tag 进
    /// sceneVoxels → GenerateMips :1610）→ gi.Draw（Gi.cs:199：SetInt samples/precision、
    /// SetFloat lerpSpeed → Dispatch Gi :272 → BlurGi :273）→
    /// SetGlobalTexture("giVoxels", giVoxelsBlurred)（Gi.cs:186）。
    ///
    /// 【URP 落位（E 报告 §4.3/§4.4；A 流改顶视采集为手动 draw）】本类自建顶视正交
    /// 相机仅作矩阵/剔除源（enabled=false，不再让 URP 全管线渲它）；sceneVoxels 的
    /// 采集由 ReplicaGiFeature 在主相机 <c>RenderPassEvent.BeforeRenderingOpaques</c>
    /// 首部手动执行：SetRenderTarget(sceneVoxels)→Clear→opaque×"WaterLabVoxelizer"
    /// tag 单 draw（DrawRenderers，对齐原作 ProcessRenderRequests 的 TopView 分支），
    /// 紧接本类 Render（GenerateMips→Gi→BlurGi→发布）——绘制先于派发，与原作
    /// beach.Draw 内 TopView 先于 gi.Draw 的序一致；整体仍先于 opaque/transparent
    /// 消费者（深水亮度真身 = opaque 序的沙 GI 变体，E §3）。相机几何（orthoSize=
    /// 域半宽 15、正俯视、aspect=1）即原作"相机=域对齐"语义。compute 引用由 Feature
    /// 每帧回灌（沿 WaterLabWhitewaterBridge 的跨程序集单向桥先例：Gameplay→Rendering
    /// 单向引用，Rendering 不 reverse-depend 本程序集）。
    ///
    /// 【未注入 = 整体不启用】compute 引用为空时不建任何资源、不派发（一次 log），
    /// 并向消费者发布黑图兜底（域内间接光 = 0 的「无间接光」降级语义，见 Render
    /// 内注释）。消费者 idiom 见 WaterLabSand/Whitewater/ReplicaPlant 三 shader 的
    /// WaterLabGiAmbient（A 流已接）。
    /// </summary>
    public static class ReplicaGiSystem
    {
        // ------------------------------------------------------------------
        // 注入点与配置
        // ------------------------------------------------------------------

        /// <summary>
        /// Gi compute 资产（WaterLabGi.compute）。运行时注入点：
        /// ReplicaGiFeature 以子资产形式序列化持有并每帧回灌（WaterLab 白水材质
        /// 同款手法）；WaterLabSetup 集成轮（I2 域释放后）可直接赋值。
        /// </summary>
        public static ComputeShader Compute;

        /// <summary>
        /// 顶视相机 cullingMask。原作 Voxelizer 语义 = opaque 队列 × "Voxelizer"
        /// shader tag（E 报告 §1.3①）——A 流起 tag 过滤由 ReplicaGiFeature 的
        /// DrawRenderers（ShaderTagId "WaterLabVoxelizer" + opaque 队列）承担，本掩码
        /// 经相机传给 FilteringSettings 作额外的层漏斗（默认全集；水/透明物由队列与
        /// tag 自然排除）。
        /// </summary>
        public static LayerMask VoxelizerMask = ~0;

        /// <summary>顶视相机（自建隐藏；Feature 借此把自己从该相机的渲染里排除）。</summary>
        public static Camera TopViewCamera => _topView;

        /// <summary>GI 链路是否就绪（compute 已注入且资源已建）。</summary>
        public static bool Ready => Compute != null && _sceneVoxels != null;

        // ------------------------------------------------------------------
        // 真值常量（出处注释，改动须先对账）
        // ------------------------------------------------------------------

        /// <summary>
        /// sceneVoxels/giVoxels/giVoxelsBlurred 分辨率 128²（E 报告 §1.3④：
        /// FormatProperty.resolution 512&gt;&gt;2=128，与 Gi 资产字段默认一致，
        /// 高置信静态 + 运行时终验项）。与 compute 内 static voxelsResolution 成对改。
        /// </summary>
        const int VoxelsResolution = 128;

        /// <summary>每轴派发组数 = ceil(128/32) = 4（Gi.cs:40-49 DispatchX，tg=[32,32,1]）。</summary>
        const int DispatchGroups = VoxelsResolution / 32;

        // shader 全局名（消费侧 in-domain 掩码 uniform，任务书命名）。消费者 idiom 已由
        // A 流落地：WaterLabSand/WaterLabWhitewater/ReplicaPlant 三 shader 的
        // WaterLabGiAmbient（E 报告 §1.3③，indirect = albedo×(域内?giVoxels:ambientSky)）。
        static readonly int GlobalGiVoxels = Shader.PropertyToID("giVoxels");        // 原作名字面（Gi.cs:186）
        static readonly int GlobalDomainMin = Shader.PropertyToID("g_GiDomainMin");
        static readonly int GlobalDomainSize = Shader.PropertyToID("g_GiDomainSize");

        // 真值单源互指：太阳方向/颜色不在此设值，只转发既有全局（改值只动单源文件）
        static readonly int SunDirGlobal = Shader.PropertyToID("_WaterLabSunDir");     // WaterLabSimulation.PublishSunDir
        static readonly int SunColorGlobal = Shader.PropertyToID("_WaterLabSunColor"); // WaterLabReplicaGlobals（I1，cb0[125]）

        // compute uniform 名（与 WaterLabGi.compute 声明同名）
        static readonly int CDomainSize = Shader.PropertyToID("g_GiDomainSize");
        static readonly int CAmbientSky = Shader.PropertyToID("_AmbientSky");
        static readonly int CSunDirection = Shader.PropertyToID("_SunDirection");
        static readonly int CSunColor = Shader.PropertyToID("_SunColor");
        static readonly int CFrameCount = Shader.PropertyToID("_FrameCount");
        static readonly int CSamples = Shader.PropertyToID("_Samples");
        static readonly int CPrecision = Shader.PropertyToID("_Precision");
        static readonly int CLerpSpeed = Shader.PropertyToID("_LerpSpeed");

        // kernel 句柄
        const int KernelNotFound = -1;
        static int _kInit = KernelNotFound, _kGi = KernelNotFound, _kBlurGi = KernelNotFound;

        static Camera _topView;
        static RenderTexture _sceneVoxels;      // 顶视辐射图（rgb=辐射 w=世界 Y）+ mip 链
        static RenderTexture _giVoxels;         // Gi kernel 输出（时间混合）
        static RenderTexture _giVoxelsBlurred;  // BlurGi 输出 = 全局纹理 giVoxels 的真身
        static bool _initDispatched;
        static bool _loggedDisabled;

        /// <summary>
        /// 主相机渲染序内调用（ReplicaGiFeature pass，BeforeRenderingOpaques）：
        /// GenerateMips → Gi → BlurGi → SetGlobalTexture("giVoxels")。顶视 Voxelizer
        /// 采集（sceneVoxels 写入）由 Feature 在调本方法之前手动完成（同 pass 内先
        /// draw 后 dispatch——与原作 beach.Draw 内 TopView 先于 gi.Draw 的序一致）。
        /// </summary>
        // 跨程序集桥接线（Rendering 程序集的 ReplicaGiFeature 经 WaterLabGiBridge 回调）
        static ReplicaGiSystem()
        {
            PirateCrew.Rendering.WaterLabGiBridge.Render = cmd => Render(cmd);
            PirateCrew.Rendering.WaterLabGiBridge.TopViewCamera = null; // 相机在 Ensure 后由属性同步
        }

        public static void Render(CommandBuffer cmd)
        {
            if (Compute == null)
            {
                if (!_loggedDisabled)
                {
                    _loggedDisabled = true;
                    Debug.LogWarning("[ReplicaGi] compute 未注入（ReplicaGiFeature.GiCompute / ReplicaGiSystem.Compute）——GI 链路关闭，"
                        + "消费者按降级语义出画（见下）。");
                }
                // 降级兜底：发布黑图（幂等）——消费者 idiom mask·(0−ambientSky)+ambientSky
                // 得「域内间接光=0、域外=ambientSky」的确定性降级。原作 Low 档关 GI 的
                // 精确语义是「GI 变体不编译 → 只剩 unity_AmbientSky」（E 报告 §3），等值
                // 需 keyword 级变体开关且接线方在装配侧（A 流域外）——差异登记，集成轮
                // 接 keyword 后消灭。未发布任何纹理时 Unity 绑灰默认图（0.5），比黑图更
                // 偏，故显式钉黑。
                Shader.SetGlobalTexture(GlobalGiVoxels, Texture2D.blackTexture);
                return;
            }
            if (!SystemInfo.supportsComputeShaders)
                return;

            EnsureResources();
            if (_sceneVoxels == null)
                return;

            PushConstants();

            // Initialization 仅一次（Gi.cs:196 "Gi: initialization"）；进同一 cmd 保证先于 Gi。
            if (!_initDispatched)
            {
                _initDispatched = true;
                cmd.DispatchCompute(Compute, _kInit, DispatchGroups, DispatchGroups, 1);
            }

            // GenerateMips：mip 链来源（原作管线 GenerateMips，BeachRenderPipeline.cs:1610；
            // sceneVoxels.autoGenerateMips=false，同原作）。此时顶视相机（depth=-100）
            // 已在本帧更早的相机序里渲完 sceneVoxels。
            cmd.GenerateMips(_sceneVoxels);

            // Dispatch Gi（Gi.cs:272）→ BlurGi（Gi.cs:273）
            cmd.DispatchCompute(Compute, _kGi, DispatchGroups, DispatchGroups, 1);
            cmd.DispatchCompute(Compute, _kBlurGi, DispatchGroups, DispatchGroups, 1);

            // 全局注入（Gi.cs:186；消费者采的是模糊版）。每帧幂等（E 报告 §4.5 允许每帧或一次）。
            cmd.SetGlobalTexture(GlobalGiVoxels, _giVoxelsBlurred);
        }

        /// <summary>自建顶视相机与三张 RT；失败（不支持 compute）时保持 null 静默跳过。</summary>
        static void EnsureResources()
        {
            if (_sceneVoxels != null)
                return;

            if (_kInit == KernelNotFound)
            {
                _kInit = Compute.FindKernel("Initialization");
                _kGi = Compute.FindKernel("Gi");
                _kBlurGi = Compute.FindKernel("BlurGi");
            }

            // 三张 RT 格式 = ARGBHalf（= GraphicsFormat.R16G16B16A16_SFloat 的
            // RenderTextureFormat 名）。原作 C# 痕迹是 Gi.cs:130
            // SystemInfo.GetGraphicsFormat(DefaultFormat.HDR)（E 报告 §0.5 据此推
            // B10G11R11_UFloatPack32——**推断被第一手推翻**：R 抓帧同机实测相机 HDR RT =
            // R16G16B16A16_TYPELESS（rt/reports/R-framedump.md:88），即原作运行时的
            // GetGraphicsFormat(HDR) 实落 RGBA16F 兜底支；且 .w=世界 Y 语义（E §1.3①，
            // PS 直写 o0.w + compute 读 t0.w 的 DXBC 链）必须有无 alpha 的通道才能成立
            // ——B10G11R11 无 alpha，RGBA16F 自洽。按参数纪律第一手 > 静态推断，显式
            // 指定 ARGBHalf（本机 Unity 2022.3 的 GetGraphicsFormat(HDR) 会解析成
            // B10G11R11，不可用其默认；且本程序集不引用 URP core 包，GraphicsFormat
            // 枚举名不可达——ARGBHalf 是同格式的 CoreModule 名）。typed UAV load
            // （requirements=50241，E §1.1）在 DX11.1 的支持清单本就含
            // R16G16B16A16_FLOAT，UAV 读回不受影响。
            // 【终验项】L2 抓帧对 giVoxels dump 时核对位宽。
            var rtFormat = RenderTextureFormat.ARGBHalf;
            _sceneVoxels = NewVoxelRT("WaterLabSceneVoxels", rtFormat, uav: false, mips: true, depthBits: 16);
            // sceneVoxels 另有 depth=16、useMipMap=true、autoGenerateMips=false（Gi.Setup，
            // E 报告 §1.1；mip 由管线 GenerateMips 生成——本类 Render 里 cmd.GenerateMips）。
            _giVoxels = NewVoxelRT("WaterLabGiVoxels", rtFormat, uav: true, mips: false, depthBits: 0);
            _giVoxelsBlurred = NewVoxelRT("WaterLabGiVoxelsBlurred", rtFormat, uav: true, mips: false, depthBits: 0);

            // sceneVoxels 回灌桥（ReplicaGiFeature 的顶视 Voxelizer 采集目标——A 流：
            // 采集=Feature 手动 DrawRenderers，不再走 URP 相机管线）
            PirateCrew.Rendering.WaterLabGiBridge.SceneVoxels = _sceneVoxels;

            _topView = CreateTopViewCamera();
            PirateCrew.Rendering.WaterLabGiBridge.TopViewCamera = _topView;
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        /// <summary>
        /// 顶视正交相机（E 报告 §1.3④）：orthoSize = 域半宽 15（Beach.cs:332 附近
        /// orthoSize=size*0.5），恰好把域 [−15,+15]² 铺满 128² RT——这就是"分辨率 128
        /// 与域的映射"：uv = world.xz/beachSize.xz + 0.5、texel = uv·128 − 0.5
        /// （E-gi.compute.hlsl 文件头）。朝向：位于域中心正上方、rot=(90,0,0) 俯视——
        /// 相机 right=+X、up=+Z，RT 的 uv.x=(x+15)/30、uv.y=(z+15)/30，与 E 映射逐轴
        /// 一致（含 row=Z 轴向，B 流台账）。
        /// 【A 流】enabled=false：相机只作矩阵/剔除/背景色源，绘制由 ReplicaGiFeature
        /// 手动 DrawRenderers（opaque×"WaterLabVoxelizer" tag，对齐原作 TopView 分支
        /// E §1.3①；方案 a/b 权衡见 Feature 文件头）。隐藏自建、DontSave
        /// （hideFlags=HideAndDontSave）。
        /// </summary>
        static Camera CreateTopViewCamera()
        {
            const float camHeight = 50f; // 域内容 Y∈[≈0,3]+植物，量程余量充足；near/far 覆盖
            var go = new GameObject("WaterLabGiTopView")
            {
                hideFlags = HideFlags.HideAndDontSave, // 隐藏自建、DontSave（不随场景存盘/不进构建）
            };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = WaterLabRules.DomainSize * 0.5f; // 15 = 域半宽（Beach.cs:332）
            cam.aspect = 1f;                                        // 128² 方形 RT
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = camHeight * 2f;                      // 俯视全程线性覆盖域内容 Y
            cam.transform.SetPositionAndRotation(
                new Vector3(0f, camHeight, 0f),                     // 域中心固定在原点（场景装配约定，WaterLabSimulation 同）
                Quaternion.Euler(90f, 0f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);        // 原作 ClearRenderTarget(true,true)（E 报告 §1.3①）；空处 Gi kernel 走天空项
            cam.cullingMask = VoxelizerMask;                        // opaque×"WaterLabVoxelizer" tag 在 Feature 的 DrawRenderers；此为层漏斗
            cam.enabled = false;                                    // 不进 URP 相机循环——手动采集（见方法注释）
            cam.useOcclusionCulling = false;                        // 128² 顶视无遮挡收益，关掉防剔除歧义
            return cam;
        }

        static RenderTexture NewVoxelRT(string name, RenderTextureFormat colorFormat, bool uav, bool mips, int depthBits)
        {
            var desc = new RenderTextureDescriptor(VoxelsResolution, VoxelsResolution,
                colorFormat, depthBits)
            {
                enableRandomWrite = uav,      // giVoxels/giVoxelsBlurred 需 UAV（typed UAV load，E §1.1）
                useMipMap = mips,
                autoGenerateMips = false,     // mip 由管线 GenerateMips 生成（BeachRenderPipeline.cs:1610）
                msaaSamples = 1,              // 全管线无 MSAA（E 报告 §0.5 定案）
                sRGB = false,                 // Linear 工程（原作同，E 报告 §2.2）
            };
            var rt = new RenderTexture(desc)
            {
                name = name,
                filterMode = FilterMode.Bilinear, // 消费 idiom 用双线性（sample_l s1，E §1.3③）
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// 常量下发（对齐 WaterLabSimulation.PushConstants 手法：逐帧幂等，值不在
        /// compute 里写默认）。samples/precision/lerpSpeed = High 档资产序列化
        /// 48/5/0.2（F-postprocess.md §5；C# SetInt 常量反编译污染 → 运行时终验项，
        /// E 报告 §4.6/§5）。
        /// </summary>
        static void PushConstants()
        {
            var cs = Compute;
            float domain = WaterLabRules.DomainSize; // 30（I7 裁决对齐原作 Beach.bounds，B 流实测）
            cs.SetVector(CDomainSize, new Vector4(domain, 0f, domain, 0f)); // [E] beachSize cb0[116].xz

            // 真值单源转发：不在此设值（改值只动单源文件）
            cs.SetVector(CAmbientSky, RenderSettings.ambientSkyColor);   // cb0[40]，原作上传路径同源（SetupPerFrameShaderConstants）
            cs.SetVector(CSunDirection, Shader.GetGlobalVector(SunDirGlobal));     // cb0[123]，_WaterLabSunDir
            cs.SetVector(CSunColor, Shader.GetGlobalVector(SunColorGlobal));       // cb0[125]，_WaterLabSunColor
            cs.SetInt(CFrameCount, Time.frameCount);                               // cb0[113].w
            cs.SetInt(CSamples, 48);        // High 档资产序列化（F-postprocess.md §5）——待运行时终验（C# SetInt 反编译污染）
            cs.SetInt(CPrecision, 5);       // High 档资产序列化——待运行时终验
            cs.SetFloat(CLerpSpeed, 0.2f);  // High 档资产序列化——待运行时终验

            // 域掩码全局（消费侧 in-domain 用；Gi kernel 自身只用 g_GiDomainSize.xz）。
            // 消费口径（A 流已落地，三 shader 的 WaterLabGiAmbient）：
            //   inDomain = all(abs(worldPos.xz − (g_GiDomainMin.xz + g_GiDomainSize.xz×0.5)) < g_GiDomainSize.xz×0.5)
            Shader.SetGlobalVector(GlobalDomainMin, new Vector4(-domain * 0.5f, 0f, -domain * 0.5f, 0f));
            Shader.SetGlobalVector(GlobalDomainSize, new Vector4(domain, 0f, domain, 0f));
        }

        /// <summary>退出时释放自建资源（域重载/进程退出路径下由 Unity 侧兜底回收）。</summary>
        static void Shutdown()
        {
            if (_topView != null)
            {
                UnityEngine.Object.Destroy(_topView.gameObject);
                _topView = null;
            }
            PirateCrew.Rendering.WaterLabGiBridge.TopViewCamera = null;
            PirateCrew.Rendering.WaterLabGiBridge.SceneVoxels = null;
            ReleaseRT(ref _sceneVoxels);
            ReleaseRT(ref _giVoxels);
            ReleaseRT(ref _giVoxelsBlurred);
            _initDispatched = false;
            _kInit = KernelNotFound;
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            rt = null;
        }
    }
}
