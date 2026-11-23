using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **边缘光**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>，排在描边之后、
    /// 着色（Diffuse）之前——契约 §3 的第 5 序）：三步一条链，
    /// <list type="number">
    ///   <item>把艺术画布的 <c>_PixelartRimLightBuffer</c> 清空；</item>
    ///   <item>一趟全屏 <see cref="PixelartPath.RimLightPassName"/>，把边缘光累加进去；</item>
    ///   <item><c>RimLightCorrection.compute</c> 的 3×3 去孤立点（就地）。</item>
    /// </list>
    ///
    /// 【为什么必须早于 Diffuse】着色趟要读边缘光缓冲来决定"这个像素要不要关掉连通域降档"
    /// （v3 `ShadingPass.hlsl:92-93` 的 <c>applyAA</c>：已有边缘光的像素不再减一档，
    /// 否则边缘光落在色带边界上会被再切一刀）。合成趟则把边缘光当第四项相加
    /// （v3 `CombineFragment`：diffuse + specular + GI + rimLight）。**次序是契约的一部分**。
    ///
    /// 【边缘光不是墨线】两者正交、只共享"边缘"这个词：墨线是**画上去的线**
    /// （<c>_PixelartOutlineBuffer.r</c>，描边趟写），边缘光是**光照层的一项**（本趟写在
    /// 自己的缓冲里）。**反向关系**：本趟必须**避开**墨线像素（shader 里判
    /// <c>_PixelartOutlineBuffer.r > 0.5</c> 就输出 0），否则合成时墨线会被边缘光染色。
    ///
    /// 【本趟读什么、写什么】读：<c>_PixelartConnectivityResultBuffer</c>（连通域趟写内容）、
    /// <c>_PixelartOutlineBuffer</c>（描边趟写内容）、<c>_PixelartRimLightPropertyBuffer</c>、
    /// <c>_PixelartAlbedoBuffer</c> 与 <c>_PixelartPhysicalBuffer</c>（物体趟写）、
    /// <c>_PixelartLightDirWS</c> / <c>_PixelartLightColor</c>（rig 每帧下发）。
    /// 前两张的**绑定**由本趟显式发布（<see cref="PixelartCameraRig.PublishArtBuffers"/>）——
    /// 缓冲都在 rig 上，rig 是权威来源，本趟不该依赖另一个 Feature 的发布次序。
    /// 写：<c>_PixelartRimLightBuffer</c>（本趟发布，供 Diffuse 的 applyAA 门控与 Combine 求和）；
    /// 另在累加前下发 <c>_PixelartRimLightDirWS</c>（边缘光方向）与 <c>_PixelartRimLevel</c>
    /// （量化档数）两个全局，见 <see cref="backlight"/> / <see cref="rimLevel"/>。
    ///
    /// 【最容易的故障不是报错，而是静默为 0】连通域结论那张缓冲若还是空的（连通域趟没跑），
    /// 打包字节全 0 ⇒ 四方向的"更近"位全 0 ⇒ 四路门控全被关掉 ⇒ 边缘光整屏为 0，**不报任何错**。
    /// 出图时"边缘光一点都没有"先查连通域趟有没有跑（Console 里找 Connectivity 那行"已执行"）。
    /// </summary>
    public sealed class PixelartRimLightFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 边缘光累加 shader 的**序列化资产引用**。装配器写入，`Create()` 优先用它。
        ///
        /// 【为什么必须有这个字段，不能只靠 Shader.Find】播放器构建只收"被资产引用链摸到"的
        /// shader；只在 C# 里 <c>Shader.Find(名字)</c> 的 shader **会被剥离**——构建日志里既没有
        /// 它的编译记录、也没有报错，运行时返回 null，表现是"这一趟静默不生效"。
        /// </summary>
        public Shader rimLightShader;

        /// <summary>
        /// 3×3 去孤立点 compute 的**序列化资产引用**。装配器写入。
        /// compute **没有** <c>Shader.Find</c> 那样的按名兜底（那是 <c>Shader</c> 的 API），
        /// 所以这个字段是唯一入口：为空 ⇒ 报错并跳过本趟（清洁工缺失时"边缘光有没有被清干净"
        /// 无从保证，宁可让缺口显形，不要让它以"偶尔多一个亮点"的样子混过去）。
        /// </summary>
        public ComputeShader rimLightCorrectionShader;

        /// <summary>
        /// 边缘光方向取**背光**（= −主光方向）。CellShade 缺口规格 §2.3 第 2-5 条的最小替代：
        /// 不接参考库的 32 盏 rim 光列表，把"边缘光方向"做成第二个光向全局——与主光那侧的受光面
        /// 夹出双侧轮廓，对应参考库 Gold 场景 Front/Back 双灯。关 = 沿用主光方向。
        /// </summary>
        [Tooltip("边缘光方向取背光（= −主光方向）；关 = 沿用主光方向。现役材质边缘光色全黑，任一取值画面不变。")]
        public bool backlight = true;

        /// <summary>边缘光量化档数（全局 <c>_PixelartRimLevel</c>）。参考库调用点恒 2 = 门控撕成 0/1 两态。</summary>
        [Tooltip("边缘光量化档数。参考库恒 2（门控撕成 0/1 两态）；≤ 1 = 不量化。")]
        public float rimLevel = 2f;

        sealed class Pass : ScriptableRenderPass
        {
            /// <summary>Blit.hlsl 的 Vert 会读它做 uv 缩放偏置；不显式置成单位值就是 0 ⇒ uv 恒为 0。</summary>
            static readonly int kBlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            /// <summary>
            /// 本趟下发的两个边缘光全局（`Includes/RimLight.hlsl` 读）。写方只有本趟、读方只有
            /// 边缘光链 ⇒ 名字跟写入方放在一起。
            /// </summary>
            static readonly int kRimLightDirId = Shader.PropertyToID("_PixelartRimLightDirWS");
            static readonly int kRimLevelId = Shader.PropertyToID("_PixelartRimLevel");

            /// <summary>清洁工 kernel 名（v3 `RimLightCorrection.compute` 的 kernel 名，逐字一致）。</summary>
            const string kCorrectionKernelName = "Main";

            /// <summary>清洁工 kernel 的线程组边长，必须与 `[numthreads(8,8,1)]` 一致。</summary>
            const int kCorrectionThreadGroup = 8;

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart RimLight");
            readonly PixelartRimLightFeature m_Owner;
            Material m_Material;
            ComputeShader m_Correction;
            int m_PassIndex = -1;
            int m_KernelIndex = -1;
            bool m_IndicesReported;
            bool m_Logged;

            public Pass(PixelartRimLightFeature owner, Material material, ComputeShader correction)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                m_Owner = owner;
                m_Material = material;
                m_Correction = correction;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady || m_Material == null)
                    return;

                // 只有 Cast 相机跑这条路径：主相机是被清空的"上屏器"，在它上面重跑一趟
                // 只会把物体趟刚画的东西冲掉（结构性问题，不是配置疏漏）。
                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                RenderTexture rimLightBuffer = rig.RimLightBuffer;
                if (rimLightBuffer == null || !EnsureIndices())
                    return;     // 引用的资产或 pass/kernel 缺失：已报错，本帧整趟跳过

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    // 自己发布本趟要读的两张艺术画布缓冲（连通域结论 / 墨线标记）：
                    // 这两张的**内容**由连通域趟与描边趟写，但"设成 shader 全局"这件事本趟不依赖
                    // 它们的发布次序——缓冲都在 rig 上，rig 是权威来源，显式发布一次即可
                    // （着色 Feature 用同一手法做保险）。少了这一步，本趟读的就是"上一帧或空"的绑定。
                    rig.PublishArtBuffers(cmd);

                    PushRimLightGlobals(cmd, rig);
                    DrawRimLight(cmd, rimLightBuffer);
                    RunCorrection(cmd, rimLightBuffer);

                    // 发布给后续趟：Diffuse 读它做 applyAA 门控、Combine 读它做求和。
                    // （连通域/墨线两张已由上面的 PublishArtBuffers 发布；边缘光这张由本趟负责，
                    //   因为分配它的是 rig、写它的只有本趟，没有别人知道该发布它。）
                    cmd.SetGlobalTexture(PixelartPath.RimLightBufferId, rimLightBuffer);

                    LogExecutedOnce(renderingData.cameraData.camera.name, rimLightBuffer);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            /// <summary>
            /// 下发边缘光方向与量化档数（累加前）。主光解析与 rig 的 <c>PushLightGlobals</c> 同源
            /// （<c>sun</c> 字段 → <c>RenderSettings.sun</c> → 兜底 up，指向光源），背光取其反向——
            /// 两个光向全局永远精确反向，不会因各算一份而漂开。
            /// </summary>
            void PushRimLightGlobals(CommandBuffer cmd, PixelartCameraRig rig)
            {
                Light light = rig.sun != null ? rig.sun : RenderSettings.sun;
                Vector3 mainLightDir = light != null ? -light.transform.forward : Vector3.up;
                Vector3 rimLightDir = m_Owner.backlight ? -mainLightDir : mainLightDir;
                cmd.SetGlobalVector(kRimLightDirId, new Vector4(rimLightDir.x, rimLightDir.y, rimLightDir.z, 0f));
                cmd.SetGlobalFloat(kRimLevelId, m_Owner.rimLevel);
            }

            /// <summary>清空 + 一趟全屏累加（契约 §4.3 的前两步）。</summary>
            void DrawRimLight(CommandBuffer cmd, RenderTexture target)
            {
                // ① 清空。写满全屏本来就不需要清，但它在这里是**契约的一部分**（次序是
                //    "清 → 累加 → 去孤立点"），而且给了一个兜底：blit 万一出问题（pass 索引错、
                //    uv 错），结果是"全 0"而不是"上一帧的边缘光冒充这一帧"——后者极难反查。
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(false, true, Color.clear, 1f);

                // ② 全屏三角形（shader 侧 Blit.hlsl 的 Vert 提供顶点）。目标仍是刚才那张缓冲，
                //    但**不绑深度附件**：艺术画布缓冲没有深度，而且"把一张缓冲同时当深度附件与
                //    采样源"是非法绑定（着色趟的注释里记了同一条）。
                cmd.SetGlobalVector(kBlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, m_PassIndex,
                    MeshTopology.Triangles, 3, 1);
            }

            /// <summary>3×3 去孤立点：就地在边缘光缓冲上跑（就地读-写安全的理由见 compute 文件头）。</summary>
            void RunCorrection(CommandBuffer cmd, RenderTexture target)
            {
                cmd.SetComputeTextureParam(m_Correction, m_KernelIndex, PixelartPath.RimLightBufferId, target);
                cmd.DispatchCompute(m_Correction, m_KernelIndex,
                    Mathf.CeilToInt(target.width / (float)kCorrectionThreadGroup),
                    Mathf.CeilToInt(target.height / (float)kCorrectionThreadGroup), 1);
            }

            /// <summary>
            /// 取 pass 与 kernel 的索引（**按名字解析，不写死序号**：序号一改就会静默跑错趟）。
            /// 失败只报一次错（每帧报一次会把 Console 刷满，反而看不见别的错）。
            /// </summary>
            bool EnsureIndices()
            {
                if (m_PassIndex >= 0 && m_KernelIndex >= 0)
                    return true;

                if (m_PassIndex < 0)
                    m_PassIndex = m_Material.FindPass(PixelartPath.RimLightPassName);
                if (m_KernelIndex < 0 && m_Correction != null)
                    m_KernelIndex = m_Correction.FindKernel(kCorrectionKernelName);

                if (m_PassIndex >= 0 && m_KernelIndex >= 0)
                    return true;

                if (!m_IndicesReported)
                {
                    m_IndicesReported = true;
                    global::PirateCrew.Core.Log.Error("[PixelartRimLightFeature] 边缘光趟的索引解析失败：pass 「"
                        + PixelartPath.RimLightPassName + "」= " + m_PassIndex
                        + "（RimLight shader 里没有这条 pass？）、kernel 「" + kCorrectionKernelName
                        + "」= " + m_KernelIndex + "（RimLightCorrection.compute 里没有这个 kernel？）"
                        + "——本帧起整趟跳过（画面不会有边缘光）。重跑装配器 "
                        + "PirateCrew/Pixelart/装配像素化路径渲染器 可重新写入资产引用。");
                }

                return false;
            }

            /// <summary>
            /// 打一行"已执行"（这条路径的自检纪律：三个 Feature 各打一行，日志齐全才算真的在跑）。
            /// 顺带把本趟的**输入依赖**写清楚——边缘光最容易的故障不是报错，而是"静默为 0"。
            /// </summary>
            void LogExecutedOnce(string cameraName, RenderTexture target)
            {
                if (m_Logged)
                    return;

                m_Logged = true;
                global::PirateCrew.Core.Log.Info("[PixelartRimLightFeature] 边缘光趟已在 " + cameraName + " 上执行：pass "
                    + m_PassIndex + "（按名「" + PixelartPath.RimLightPassName + "」解析）、清洁工 kernel "
                    + m_KernelIndex + "（「" + kCorrectionKernelName + "」）、艺术画布 "
                    + target.width + "×" + target.height
                    + "。输入依赖 _PixelartConnectivityResultBuffer（连通域趟写内容）与 _PixelartOutlineBuffer"
                    + "（描边趟写内容），绑定由本趟用 rig.PublishArtBuffers 显式发布；若那两趟没跑，"
                    + "读到的是空缓冲 ⇒ 门控恒为 0 ⇒ 边缘光整屏为 0 且不报错；"
                    + "本趟已发布 _PixelartRimLightBuffer 供着色各趟读取；边缘光方向 = "
                    + (m_Owner.backlight ? "背光（−主光）" : "主光") + "、量化档数 "
                    + m_Owner.rimLevel.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "。");
            }
        }

        Pass m_Pass;
        Material m_Material;

        public override void Create()
        {
            // 取用统一走 PixelartShaders（全仓裸 Shader.Find 只许在它内部，审计 R7）：
            // 本 shader 由 Resources 持有（必然入包）；序列化引用是主路，这里只兜"引用为空"的场景。
            Shader shader = rimLightShader != null ? rimLightShader : PixelartShaders.Load(PixelartPath.RimLightShaderName);
            if (shader == null)
            {
                global::PirateCrew.Core.Log.Error("[PixelartRimLightFeature] 找不到 shader 「" + PixelartPath.RimLightShaderName
                    + "」（资产引用为空、编译失败或改过名？）。边缘光趟不会生效——"
                    + "重跑装配器 PirateCrew/Pixelart/装配像素化路径渲染器 可重新写入引用。");
                m_Material = null;
                m_Pass = null;
                return;
            }

            m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            if (rimLightCorrectionShader == null)
            {
                // 报错并整趟不接：compute **没有**按名查找的兜底 API（那是 Shader 的 API），
                // 序列化引用是唯一入口。去孤立点是契约里这一趟的第三步，缺了它就不算这一趟跑齐了。
                global::PirateCrew.Core.Log.Error("[PixelartRimLightFeature] 没有引用 RimLightCorrection.compute"
                    + "（rimLightCorrectionShader 字段为空）。本趟不会接入（画面不会有边缘光）——"
                    + "重跑装配器可写入该引用。");
                m_Pass = null;
                return;
            }

            m_Pass = new Pass(this, m_Material, rimLightCorrectionShader);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (PixelartPath.ActiveRig == null || m_Pass == null || m_Material == null)
                return;

            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            m_Pass = null;
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
