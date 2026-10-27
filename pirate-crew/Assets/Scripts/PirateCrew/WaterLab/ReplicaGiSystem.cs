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
    /// 【URP 落位（E 报告 §4.3 方案 a / §4.4）】sceneVoxels 由本类自建的专用顶视
    /// 正交相机渲（相机=域对齐，更贴原作语义；URP 按 camera.depth 排序，本相机
    /// depth=-100 恒先于主相机——等效原作"管线 Render 之前"）；Gi/BlurGi 派发挂
    /// 主相机 <c>RenderPassEvent.BeforeRenderingOpaques</c>（ReplicaGiFeature，
    /// 注入点依据见其注释）。本类与 Feature 分属 Gameplay/Rendering 两程序集，
    /// Compute 引用由 Feature 每帧回灌（沿 WaterLabWhitewaterBridge 的跨程序集
    /// 单向桥先例：Gameplay→Rendering 单向引用，Rendering 不 reverse-depend 本程序集）。
    ///
    /// 【未注入 = 整体不启用】compute 引用为空时不建任何资源、不派发（一次 log）。
    /// 消费者 shader 采不到 giVoxels 全局纹理即退化为 unity_AmbientSky 常数环境光
    /// ——与原作管线资产 Low 档 globalIllumination 关档的行为一致（E 报告 §3 反证）。
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
        /// shader tag（E 报告 §1.3①）——URP 相机只有层掩码没有 tag 过滤，默认
        /// 全集渲当前 opaque 集；待各表面 shader 挂 Voxelizer pass 时（I2/I6b）由
        /// 装配侧收窄（水/透明物归专属层排除）。
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

        // shader 全局名（消费侧 in-domain 掩码 uniform，任务书命名；与
        // WaterLabSand.shader 的 GI TODO（L287-294）互指——I2 释放 Sand 域后按
        // E 报告 §1.3③ idiom 接 albedo×(域内?giVoxels:unity_AmbientSky)）
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
        /// GenerateMips → Gi → BlurGi → SetGlobalTexture("giVoxels")。
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
                        + "消费者退化为 unity_AmbientSky（原作 Low 档关 GI 语义，E 报告 §3）。");
                }
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

            // 三张 RT 均 SystemInfo.GetGraphicsFormat(DefaultFormat.HDR)（BeachRenderPipeline.cs:2206）
            // → D3D11 = B10G11R11_UFloatPack32（无 alpha；R16G16B16A16 仅平台不支持时兜底，
            // 同 Unity 原调用语义）。Gi 资产 requirements=50241 含 typed UAV load——giVoxels
            // 必须 UAV 可读回，这正是该格式的硬件理由（E 报告 §1.1）。
            _sceneVoxels = NewVoxelRT("WaterLabSceneVoxels", uav: false, mips: true, depthBits: 16);
            // sceneVoxels 另有 depth=16、useMipMap=true、autoGenerateMips=false（Gi.Setup，E 报告 §1.1；
            // mip 由管线 GenerateMips 生成——本类 Render 里 cmd.GenerateMips）。
            _giVoxels = NewVoxelRT("WaterLabGiVoxels", uav: true, mips: false, depthBits: 0);
            _giVoxelsBlurred = NewVoxelRT("WaterLabGiVoxelsBlurred", uav: true, mips: false, depthBits: 0);

            _topView = CreateTopViewCamera();
            PirateCrew.Rendering.WaterLabGiBridge.TopViewCamera = _topView;
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        /// <summary>
        /// 顶视正交相机（E 报告 §1.3④ / §4.3 方案 a）：
        /// orthographicSize = 域半宽 15（Beach.cs:332 附近 orthoSize=size*0.5），恰好把
        /// 域 [−15,+15]² 铺满 128² RT——这就是"分辨率 128 与域的映射"：
        /// uv = world.xz/beachSize.xz + 0.5、texel = uv·128 − 0.5（E-gi.compute.hlsl 文件头）。
        /// 朝向：位于域中心正上方、rot=(90,0,0) 俯视——相机 right=+X、up=+Z，RT 的
        /// uv.x=(x+15)/30、uv.y=(z+15)/30，与 E 映射逐轴一致（含 row=Z 轴向，B 流台账）。
        /// 隐藏自建、DontSave（hideFlags=HideAndDontSave），depth=-100 保证 URP 相机序
        /// 先于主相机（等效原作 TopView 渲在管线 Render 前，E 报告 §2.1）。
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
            cam.cullingMask = VoxelizerMask;                        // 原作=opaque×Voxelizer tag；本仓暂渲 opaque 全集（TODO 见文件头注释）
            cam.targetTexture = _sceneVoxels;                       // 输出重定向进 sceneVoxels（E §4.3 方案 a）
            cam.depth = -100f;                                      // URP 按 depth 排序，先于主相机渲染
            cam.enabled = true;                                     // 由 URP 相机循环自动渲染（非手动 Camera.Render）
            cam.useOcclusionCulling = false;                        // 128² 顶视无遮挡收益，关掉防剔除歧义
            return cam;
        }

        static RenderTexture NewVoxelRT(string name, bool uav, bool mips, int depthBits)
        {
            // DefaultFormat 归 UnityEngine.Experimental.Rendering 命名空间（2022.3，URP 源码同款用法）
            var desc = new RenderTextureDescriptor(VoxelsResolution, VoxelsResolution,
                SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.HDR), depthBits)
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
            cs.SetInt(CSamples, 48);        // High 档（F §5；终验项）
            cs.SetInt(CPrecision, 5);       // High 档
            cs.SetFloat(CLerpSpeed, 0.2f);  // High 档

            // 域掩码全局（消费侧 in-domain 用；Gi kernel 自身只用 g_GiDomainSize.xz）。
            // 命名与 I2 TODO（WaterLabSand.shader L287-294）互指：
            //   inDomain = all(abs(worldPos.xz − 域中心) ≤ g_GiDomainSize.xz × 0.5)
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
