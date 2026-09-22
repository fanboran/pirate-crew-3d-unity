using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **连通域三阶段**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>，排在物体 pass 之后、
    /// 描边与四趟着色之前；契约 §3 第 3 项）：v3 `ConnectivityCheck / ConnectivityFlood /
    /// ConnectivityResult` 三个 compute 的翻译改编（契约 §4.1，蓝图 §3）。
    ///
    /// 【三段在算什么】
    /// <list type="number">
    ///   <item><b>Check</b>（屏幕档）：逐细像素对四个方向各做一次「两端法线各自预测深度、比残差」，
    ///         打包成 bit7 连通 / bit6 更近 / bit5 单元中心，写进 ConnectivityDetail。</item>
    ///   <item><b>Flood</b>（屏幕档，ping-pong）：把逐方向的连通性在**一个艺术像素的 k×k 单元内**
    ///         传播；跑 <c>max(1, RoundToInt(pixelScale × iterationScale))</c> 次
    ///         （v3：RoundToInt(5 × 1.5) = 8；本仓 pixelScale = 3 ⇒ 4）。每次派发前先把当前的
    ///         Detail 整张拷成 Prev（v3 `...RendererFeature.cs:82-91` 的 `GetTemporaryRT` + `Blit`，
    ///         本仓换成两张常驻 RT——不拷贝的话邻居之间会读到半新半旧的数据）。</item>
    ///   <item><b>Result</b>（艺术画布）：遍历每个画布像素对应的 k×k 细度单元，出四个量
    ///         （r 单元内连通比例 / g 连通或更近比例 / b 单元内最大法线差 / a 跨单元四方向位）。
    ///         着色那几趟读 r/g/b，**描边那趟读 a**（本仓新增的墨线来源，契约 §4.4）。</item>
    /// </list>
    ///
    /// 【为什么判据必须在屏幕档跑】它要「跨步走中间像素」（`step` 个细像素）来判断"两个低分辨率
    /// 像素之间到底连不连"，那要求"一个艺术像素内部"有 k×k 的细粒度深度/法线——这正是屏幕档
    /// 存在的理由（蓝图 §4.2 的推论：v3 的 5× 超采样不是画质手段，是判据的输入）。
    ///
    /// 【本 Feature 管什么、不管什么】
    /// <list type="bullet">
    ///   <item><b>管</b>：Flood 的 ping-pong 中间件（Detail / Prev 两张屏幕档 ARGB32，自带
    ///         <c>enableRandomWrite</c>，在 <see cref="Dispose"/> 里**逐个**释放——
    ///         不重犯 v3 那个"释放循环在循环体里把容器置空、实际只放掉第 0 张"的泄漏 bug，
    ///         蓝图 §4.4 第 1 条）。</item>
    ///   <item><b>不管</b>：`rig.Normal0Buffer` / `rig.DepthBuffer` / `rig.ConnectivityResultBuffer`
    ///         全由 <see cref="PixelartCameraRig"/> 分配与释放，本 Feature 只读、只写。</item>
    /// </list>
    ///
    /// 【静默失效点（本类里已各自设防）】
    /// <list type="bullet">
    ///   <item>compute shader 与 graphics shader 一样**会被播放器构建剥离**（AGENTS 的"Shader.Find
    ///         的 shader 会被剥离"同一条）⇒ 只走 `checkShader/floodShader/resultShader` 三个
    ///         **序列化资产引用**。</item>
    ///   <item>kernel 按**名字**解析（`FindKernel`），解析不到就报一行明确的错并停派发——
    ///         写死序号时改一次 kernel 名就会静默跑错 kernel。</item>
    ///   <item>结果缓冲没有 `enableRandomWrite` 时 UAV 绑不上、Unity **抛不出异常也不报错**，
    ///         只是写不进去 ⇒ 这里显式检查并报一行错（`PixelartCameraRig` 侧补描述符即可）。</item>
    ///   <item>尺寸走**契约的显式全局**（`_PixelartFineWidth/Height` 等，由 rig / BeforeRender 下发），
    ///         不使用 `_ScreenParams`（契约 §2.2）。C# 侧只拿 rig 的尺寸算派发组数。</item>
    /// </list>
    /// </summary>
    public sealed class PixelartConnectivityFeature : ScriptableRendererFeature
    {
        [Tooltip("平面预测残差阈值（米）。v3 的 m_Threshold = 0.25：两端残差**同时**超过它才判「不连通」。")]
        public float threshold = 0.25f;

        [Tooltip("Flood 迭代倍率：次数 = max(1, RoundToInt(pixelScale × 本值))。v3 = 1.5。")]
        public float iterationScale = 1.5f;

        [Tooltip("Check 段 compute（kernel 名 Main）。必须是资产引用——构建会剥离只被 Shader.Find 摸到的 compute。")]
        public ComputeShader checkShader;

        [Tooltip("Flood 段 compute（kernel 名 Main）。")]
        public ComputeShader floodShader;

        [Tooltip("Result 段 compute（kernel 名 Main）。")]
        public ComputeShader resultShader;

        // ---------------- compute 侧的私有 uniform 名（不是 shader 全局；只 SetCompute* 用）----------------

        /// <summary>判据阈值（v3 `ShaderPropertyStorage.cs:22` 同名）。Check / Result 两段各下发一次。</summary>
        static readonly int kThresholdId = Shader.PropertyToID("_Threshold");

        /// <summary>
        /// Flood 的 ping-pong 备份（v3 的 `_PrevConnectivityMap`）。
        /// **它只被绑成 kernel 纹理**（`SetComputeTextureParam`），不需要也不该当全局量下发。
        /// </summary>
        static readonly int kConnectivityPrevId = PixelartPath.ConnectivityPrevId;

        /// <summary>三个 compute 的 kernel 名（v3 三个文件都叫 Main）。**按名字解析，不写死序号**。</summary>
        const string kKernelName = "Main";

        /// <summary>`[numthreads(8,8,1)]`（三个 compute 一致）。</summary>
        const int kThreadGroupSize = 8;

        // ---------------- 运行期状态 ----------------

        /// <summary>Flood 的 ping-pong 中间件（屏幕档）。**本 Feature 自管**（分配 + Dispose 释放）。</summary>
        RenderTexture m_Detail;
        RenderTexture m_Prev;
        int m_AllocatedFineWidth;
        int m_AllocatedFineHeight;

        /// <summary>Detail → Prev 的整张拷贝用 URP 自带 CoreBlit（与上屏那条同一个 shader，构建保活）。</summary>
        Material m_CopyMaterial;

        int m_CheckKernel = -1;
        int m_FloodKernel = -1;
        int m_ResultKernel = -1;
        bool m_KernelsResolved;
        bool m_MissingShaderWarned;
        bool m_NotDispatchableWarned;
        bool m_ExecutedLogged;
        bool m_IntermediatesLogged;

        sealed class Pass : ScriptableRenderPass
        {
            readonly PixelartConnectivityFeature m_Owner;
            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Connectivity");

            public Pass(PixelartConnectivityFeature owner)
            {
                m_Owner = owner;
                // 时机：物体 pass 之后（它写 G-buffer + 深度）、描边/着色之前（它们读连通域结论）。
                // 同一 RenderPassEvent 内的次序 = Feature 数组顺序，契约 §3 定死了本 Feature 是第 3 个。
                renderPassEvent = RenderPassEvent.AfterRendering;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady)
                    return;

                // 只对 Cast 相机生效：主相机是上屏器（cullingMask = 0、渲染器也不同），
                // 在它上面跑这套只是白烧一次全屏 compute。
                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                if (!m_Owner.EnsureKernels()
                    || !m_Owner.EnsureIntermediates(rig)
                    || !m_Owner.EnsureWritable(rig))
                    return;

                int fineWidth = Mathf.Max(1, rig.FineWidth);
                int fineHeight = Mathf.Max(1, rig.FineHeight);
                int artWidth = Mathf.Max(1, rig.RenderWidth);
                int artHeight = Mathf.Max(1, rig.RenderHeight);

                int groupsX = (fineWidth + kThreadGroupSize - 1) / kThreadGroupSize;
                int groupsY = (fineHeight + kThreadGroupSize - 1) / kThreadGroupSize;
                int resultGroupsX = (artWidth + kThreadGroupSize - 1) / kThreadGroupSize;
                int resultGroupsY = (artHeight + kThreadGroupSize - 1) / kThreadGroupSize;

                // Flood 次数：v3 是 RoundToInt(DownSamplingScale × 1.5)。pixelScale = 3 ⇒ 4 次。
                int floodIterations = Mathf.Max(1, Mathf.RoundToInt(rig.PixelScale * m_Owner.iterationScale));

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    m_Owner.LogExecutedOnce(rig, fineWidth, fineHeight, artWidth, artHeight, floodIterations);

                    // ---- 1) Check（屏幕档）：Normal0 + Depth → Detail ----
                    cmd.SetComputeFloatParam(m_Owner.checkShader, kThresholdId, m_Owner.threshold);
                    cmd.SetComputeTextureParam(m_Owner.checkShader, m_Owner.m_CheckKernel,
                        PixelartPath.Normal0BufferId, rig.Normal0Buffer);
                    cmd.SetComputeTextureParam(m_Owner.checkShader, m_Owner.m_CheckKernel,
                        PixelartPath.DepthBufferId, rig.DepthBuffer);
                    cmd.SetComputeTextureParam(m_Owner.checkShader, m_Owner.m_CheckKernel,
                        PixelartPath.ConnectivityDetailId, m_Owner.m_Detail);
                    cmd.DispatchCompute(m_Owner.checkShader, m_Owner.m_CheckKernel, groupsX, groupsY, 1);

                    // ---- 2) Flood × N（屏幕档，ping-pong）----
                    // 每次派发前把 Detail 整张拷成 Prev：kernel 是"读 Prev、写 Detail"，
                    // 不拷贝就会在同一张纹理上边读边写（邻居之间读到半新半旧的数据）。
                    for (int i = 0; i < floodIterations; i++)
                    {
                        Blitter.BlitTexture(cmd, m_Owner.m_Detail, m_Owner.m_Prev,
                            m_Owner.m_CopyMaterial, PixelartPath.CoreBlitNearestPass);

                        cmd.SetComputeTextureParam(m_Owner.floodShader, m_Owner.m_FloodKernel,
                            kConnectivityPrevId, m_Owner.m_Prev);
                        cmd.SetComputeTextureParam(m_Owner.floodShader, m_Owner.m_FloodKernel,
                            PixelartPath.ConnectivityDetailId, m_Owner.m_Detail);
                        cmd.DispatchCompute(m_Owner.floodShader, m_Owner.m_FloodKernel, groupsX, groupsY, 1);
                    }

                    // ---- 3) Result（艺术画布）：Detail + Normal0 + Depth → ConnectivityResult ----
                    cmd.SetComputeFloatParam(m_Owner.resultShader, kThresholdId, m_Owner.threshold);
                    cmd.SetComputeTextureParam(m_Owner.resultShader, m_Owner.m_ResultKernel,
                        PixelartPath.ConnectivityDetailId, m_Owner.m_Detail);
                    cmd.SetComputeTextureParam(m_Owner.resultShader, m_Owner.m_ResultKernel,
                        PixelartPath.Normal0BufferId, rig.Normal0Buffer);
                    cmd.SetComputeTextureParam(m_Owner.resultShader, m_Owner.m_ResultKernel,
                        PixelartPath.DepthBufferId, rig.DepthBuffer);
                    cmd.SetComputeTextureParam(m_Owner.resultShader, m_Owner.m_ResultKernel,
                        PixelartPath.ConnectivityResultId, rig.ConnectivityResultBuffer);
                    cmd.DispatchCompute(m_Owner.resultShader, m_Owner.m_ResultKernel,
                        resultGroupsX, resultGroupsY, 1);

                    // ---- 4) 把两张结论缓冲设成全局（描边与着色各趟按全局读）----
                    // Detail 是屏幕档的中间结论（调试/后续扩展用），Result 是艺术画布上的最终结论。
                    cmd.SetGlobalTexture(PixelartPath.ConnectivityDetailId, m_Owner.m_Detail);
                    cmd.SetGlobalTexture(PixelartPath.ConnectivityResultId, rig.ConnectivityResultBuffer);

                    // ---- 5) 把渲染目标还给 Cast 相机的颜色目标 ----
                    // 【为什么必须显式还】Flood 的整张拷贝走 `Blitter.BlitTexture`，它内部会
                    // `SetRenderTarget(Prev)`；而 URP 的 `SetRenderPassAttachments` 对"没配
                    // colorAttachments 的 pass"是**直接返回**（`ScriptableRenderer.cs:1533-1535`），
                    // 也就是说管线不会替我们把目标换回来。不还的话，后面某一趟若"没自己设目标就画"，
                    // 输出会静默落进 ConnectivityPrev（画面无变化、也看不到报错）。
                    cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        Pass m_Pass;

        public override void Create()
        {
            m_Pass = new Pass(this);

            // kernel 索引缓存要随 Create 失效：装配器重新写 shader 引用时 Create 会再跑一次。
            m_KernelsResolved = false;
            m_CheckKernel = -1;
            m_FloodKernel = -1;
            m_ResultKernel = -1;

            // Detail → Prev 的拷贝用 URP 自带的 CoreBlit（与上屏 blit 同一个 shader：
            // 它被 URP 运行时自身引用，播放器构建必然保活，不受"Shader.Find 被剥离"影响）。
            if (m_CopyMaterial == null)
            {
                m_CopyMaterial = PixelartPath.CreateCoreBlitMaterial();
                if (m_CopyMaterial == null)
                {
                    Debug.LogError("[PixelartConnectivityFeature] 找不到 shader「"
                        + PixelartPath.CoreBlitShaderName + "」，Flood 的整张拷贝没有实现，"
                        + "连通域这一段不会生效。");
                }
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 自守卫：装配没就绪（相机/缓冲没建好）时连 pass 都不入队。
            PixelartCameraRig rig = PixelartPath.ActiveRig;
            if (rig == null || !rig.IsReady)
                return;

            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            // 逐个释放，不做任何取巧（v3 的释放循环在循环体里把容器置空，实际只放掉第 0 张——
            // 蓝图 §4.4 第 1 条。这里两张各释放一次，且**先把字段置 null 再释放下一张**，
            // 结构上不可能复制那个 bug）。
            m_Detail = ReleaseBuffer(m_Detail);
            m_Prev = ReleaseBuffer(m_Prev);
            m_AllocatedFineWidth = 0;
            m_AllocatedFineHeight = 0;

            CoreUtils.Destroy(m_CopyMaterial);
            m_CopyMaterial = null;

            m_KernelsResolved = false;
            m_CheckKernel = -1;
            m_FloodKernel = -1;
            m_ResultKernel = -1;
        }

        // ---------------- 资源与自检 ----------------

        /// <summary>
        /// 按**名字**解析三个 kernel。解析不到就报一行明确的错（含 kernel 名与文件名），
        /// 之后不再重试也不刷日志——静默跑错 kernel 或静默不派发都是最难查的那一类。
        /// </summary>
        bool EnsureKernels()
        {
            if (m_KernelsResolved)
                return m_CheckKernel >= 0 && m_FloodKernel >= 0 && m_ResultKernel >= 0;

            if (checkShader == null || floodShader == null || resultShader == null)
            {
                if (!m_MissingShaderWarned)
                {
                    m_MissingShaderWarned = true;
                    Debug.LogError("[PixelartConnectivityFeature] compute 资产引用不全（check="
                        + (checkShader != null ? checkShader.name : "空")
                        + " flood=" + (floodShader != null ? floodShader.name : "空")
                        + " result=" + (resultShader != null ? resultShader.name : "空")
                        + "）——连通域不派发。compute 与 graphics shader 一样会被播放器构建剥离，"
                        + "必须由装配器写进这三个序列化字段（不能只靠 Shader.Find / Resources.Load）。"
                        + "重跑装配器「装配像素化路径渲染器」可重新写入引用。");
                }
                return false;
            }

            m_CheckKernel = checkShader.FindKernel(kKernelName);
            m_FloodKernel = floodShader.FindKernel(kKernelName);
            m_ResultKernel = resultShader.FindKernel(kKernelName);

            if (m_CheckKernel < 0 || m_FloodKernel < 0 || m_ResultKernel < 0)
            {
                Debug.LogError("[PixelartConnectivityFeature] 找不到 kernel「" + kKernelName
                    + "」：check=" + m_CheckKernel + " flood=" + m_FloodKernel
                    + " result=" + m_ResultKernel + "——连通域不派发（kernel 一律按名字解析，"
                    + "三个 .compute 里的 kernel 名必须都是 " + kKernelName + "）。");
                m_KernelsResolved = true;   // 名字是编译期常量，重试没有意义；不再刷日志
                return false;
            }

            m_KernelsResolved = true;
            return true;
        }

        bool EnsureIntermediates(PixelartCameraRig rig)
        {
            int fineWidth = Mathf.Max(1, rig.FineWidth);
            int fineHeight = Mathf.Max(1, rig.FineHeight);

            if (m_Detail != null && m_Prev != null
                && m_AllocatedFineWidth == fineWidth && m_AllocatedFineHeight == fineHeight)
                return true;

            // 尺寸变化（窗口拉伸 / 分辨率切换）时重分配：先放旧的两张，再建新的两张。
            m_Detail = ReleaseBuffer(m_Detail);
            m_Prev = ReleaseBuffer(m_Prev);

            m_Detail = NewIntermediate(fineWidth, fineHeight, "PixelartConnectivityDetail");
            m_Prev = NewIntermediate(fineWidth, fineHeight, "PixelartConnectivityPrev");
            m_AllocatedFineWidth = fineWidth;
            m_AllocatedFineHeight = fineHeight;

            if (!m_IntermediatesLogged)
            {
                m_IntermediatesLogged = true;
                Debug.Log("[PixelartConnectivityFeature] 中间缓冲已就绪：ConnectivityDetail / ConnectivityPrev "
                    + fineWidth + "×" + fineHeight + "（屏幕档，ARGB32，enableRandomWrite）。");
            }
            return true;
        }

        /// <summary>
        /// 结果缓冲必须开 <c>enableRandomWrite</c>，否则 compute 的 UAV 绑不上、**写入被静默丢弃**
        /// （Unity 抛不出异常）。它不是本 Feature 能改的（rig 分配），所以这里显式把话说出来。
        /// </summary>
        bool EnsureWritable(PixelartCameraRig rig)
        {
            if (rig.ConnectivityResultBuffer == null || rig.Normal0Buffer == null || rig.DepthBuffer == null)
            {
                if (!m_NotDispatchableWarned)
                {
                    m_NotDispatchableWarned = true;
                    Debug.LogError("[PixelartConnectivityFeature] rig 的 Normal0 / Depth / ConnectivityResult "
                        + "缓冲有空值——连通域不派发。");
                }
                return false;
            }

            if (!rig.ConnectivityResultBuffer.enableRandomWrite)
            {
                if (!m_NotDispatchableWarned)
                {
                    m_NotDispatchableWarned = true;
                    Debug.LogError("[PixelartConnectivityFeature] rig.ConnectivityResultBuffer 没有开 "
                        + "enableRandomWrite，compute 的 UAV 绑不上（写入会被静默丢弃、且不报错）"
                        + "——连通域不派发。修法：PixelartCameraRig 分配这张缓冲时在 "
                        + "RenderTextureDescriptor 里置 enableRandomWrite = true。");
                }
                return false;
            }

            return true;
        }

        void LogExecutedOnce(PixelartCameraRig rig, int fineWidth, int fineHeight,
            int artWidth, int artHeight, int floodIterations)
        {
            if (m_ExecutedLogged)
                return;

            m_ExecutedLogged = true;
            Debug.Log("[PixelartConnectivityFeature] 连通域三阶段已在「"
                + rig.CastCamera.name + "」上执行：Check + Flood × " + floodIterations
                + "（屏幕档 " + fineWidth + "×" + fineHeight + "，pixelScale " + rig.PixelScale
                + "）→ Result（艺术画布 " + artWidth + "×" + artHeight
                + "）；阈值 " + threshold.ToString("F3",
                    System.Globalization.CultureInfo.InvariantCulture)
                + "、`_PixelartSamplingScale` 由 rig 下发。");
        }

        /// <summary>
        /// 建一张屏幕档的中间缓冲：**ARGB32（UNorm8，sRGB 关）**、Point、无 mipmap，
        /// 并置 <c>enableRandomWrite</c>（compute 要当 UAV 写）。
        /// 位字节直存依据：UNorm8 的取值集合恰好是 k/255，`round(c × 255)` 逐位可取回
        /// （契约 §6 第 7 条：本仓不用 v3 的 SNorm16 + PackFloatInt8bit）。
        /// </summary>
        static RenderTexture NewIntermediate(int width, int height, string name)
        {
            // Unity 2022.3 的 RenderTexture.sRGB 是只读的 ⇒ sRGB / mipmap / UAV 都得在描述符上给。
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true,
            };
            var rt = new RenderTexture(desc)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        static RenderTexture ReleaseBuffer(RenderTexture rt)
        {
            if (rt == null)
                return null;
            if (rt.IsCreated())
                rt.Release();
            CoreUtils.Destroy(rt);
            return null;
        }
    }
}
