using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **屏幕空间描边**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>）：
    /// 在**艺术画布**上跑一趟全屏，对每个像素查 4 邻域，命中的像素往
    /// <c>_PixelartOutlineBuffer.r</c> 写 1（墨线标记）。**本趟不出颜色**——墨色是全局
    /// <c>_PixelartInkColor</c>，由随后四趟着色见到标记后输出（契约 §4.4）。
    ///
    /// 【时机】排在连通域 Feature **之后**（门控要读它的结论）、着色 Feature **之前**
    /// （着色读本趟的标记）。同一 <see cref="RenderPassEvent"/> 内的次序 = 渲染器资产里
    /// Feature 数组的顺序，由装配器把控。
    ///
    /// 【为什么不是反向壳几何描边】旧口径沿面法线外扩，在箱体的左下/右下剪影边（正面墙底边）上
    /// 投影不足 1 艺术像素 ⇒ 量化后断续，角部还堆块（r6 实测定位）。创始人 2026-09-22 裁决改用
    /// 这里的 4 邻域膨胀：线宽恒为 1 艺术像素、与法线方向无关，必然闭合。
    /// 判据与位序的逐条口径写在 <c>PixelartOutline.shader</c> 的文件头。
    ///
    /// 【缓冲谁分配】描边缓冲由 <see cref="PixelartCameraRig"/> 统一分配/释放
    /// （艺术画布尺寸、ARGB32、Point、无 mipmap、sRGB 关）。本 Feature 只写它、
    /// 再把它发布成 shader 全局供着色几趟读——**不要**在这里另建 RT，那会出现两张同名缓冲。
    /// </summary>
    public sealed class PixelartOutlineFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 描边 shader 的**序列化资产引用**。装配器写入，`Create()` 优先用它。
        ///
        /// 【为什么必须有这个字段，不能只靠 Shader.Find】播放器构建只收"被资产引用链摸到"的
        /// shader；只在 C# 里 <c>Shader.Find(名字)</c> 的 shader **会被剥离**——构建日志里既没有
        /// 它的编译记录、也没有报错，运行时 `Shader.Find` 返回 null，表现是"描边静默不生效"
        /// （本仓既有像素化 Feature 的 blitShader 字段是同一坑）。
        /// </summary>
        public Shader outlineShader;

        sealed class Pass : ScriptableRenderPass
        {
            /// <summary>
            /// Blit.hlsl 的 <c>Vert</c> 无条件读它做 uv 缩放偏置（`uv * xy + zw`）；
            /// 不显式置成单位值就是 0 ⇒ uv 恒为 0（全屏读同一个点，且不报错）。
            /// </summary>
            static readonly int kBlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Outline");
            readonly Material m_Material;
            int m_PassIndex = -1;
            bool m_PassMissingLogged;
            bool m_Logged;

            public Pass(Material material)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                m_Material = material;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady)
                    return;

                // 【只对 Cast 相机生效】主相机是本路径的"上屏器"（它什么都不画）；在它上面跑这一趟
                // 会往同一个缓冲里写两遍，而且那时着色已经读过标记了。
                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                // pass 名解析失败就**跳过本帧并报错一次**（不静默：描边没了在最终画面上只是少一圈线）。
                if (!EnsurePassIndex(renderingData.cameraData.camera.name))
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    // 目标 = 艺术画布的墨线标记缓冲（rig 分配、rig 释放）。
                    // 【不绑深度附件】本趟只采样全局纹理，既不读深度也不写深度；把同一张 RT 同时当
                    // 深度附件绑上去就是"同一个资源既是 SRV 又是 DSV"（D3D11 上的非法绑定，
                    // 会静默出脏数据）——着色那趟刻意避开的正是同一个坑。
                    cmd.SetRenderTarget(rig.OutlineBuffer);
                    // 先清成 0：全屏三角形本该覆盖每个像素，但它一旦漏了边缘一行，读到的是**上一帧**的
                    // 墨线（"越用越脏"这类故障最难反查）。清一次比事后猜便宜。
                    cmd.ClearRenderTarget(false, true, Color.clear, 1f);

                    // 驱动三步：置 uv 基（见 kBlitScaleBiasId 的注）→ 全屏三角形（3 顶点）。
                    // 【为什么不用 Blitter】中间缓冲是自管的 RenderTexture 而不是 RTHandle，
                    // Blitter 那几个能吃 RenderTexture 的重载都不会替我们置 `_BlitScaleBias`。
                    cmd.SetGlobalVector(kBlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    cmd.DrawProcedural(Matrix4x4.identity, m_Material, m_PassIndex,
                        MeshTopology.Triangles, 3, 1);

                    // 发布成全局供着色几趟读（同帧内全局纹理持久；这里显式发布让依赖在代码里可读）。
                    cmd.SetGlobalTexture(PixelartPath.OutlineBufferId, rig.OutlineBuffer);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            /// <summary>
            /// 取描边 pass 的索引——**按 pass 名而不是序号**解析（序号一改就会静默描不出来）。
            /// 找不到就报错并**跳过本帧**（不静默：描边没了在最终画面上只是"少一圈线"，
            /// 从画面反推要猜很久）。
            /// </summary>
            bool EnsurePassIndex(string cameraName)
            {
                if (m_PassIndex >= 0)
                    return true;

                m_PassIndex = m_Material.FindPass(PixelartPath.OutlinePassName);
                if (m_PassIndex < 0)
                {
                    if (!m_PassMissingLogged)
                    {
                        m_PassMissingLogged = true;
                        Debug.LogError("[PixelartOutlineFeature] 描边 shader 里找不到 pass 「"
                            + PixelartPath.OutlinePassName + "」——本帧不描边（画面不会有墨线）。"
                            + "检查 shader 的 Pass 名是否被改名（契约 §2.1）。");
                    }
                    return false;
                }

                if (!m_Logged)
                {
                    m_Logged = true;
                    PixelartCameraRig rig = PixelartPath.ActiveRig;
                    Debug.Log("[PixelartOutlineFeature] 描边 pass 已在 " + cameraName + " 上执行"
                        + "（艺术画布 " + rig.RenderWidth + "×" + rig.RenderHeight
                        + "，邻域偏移 1 艺术像素、pixelScale " + rig.PixelScale
                        + "；pass " + m_PassIndex + " 按名解析）。");
                }
                return true;
            }
        }

        Pass m_Pass;
        Material m_Material;

        public override void Create()
        {
            Shader shader = outlineShader != null ? outlineShader : Shader.Find(PixelartPath.OutlineShaderName);
            if (shader == null)
            {
                Debug.LogError("[PixelartOutlineFeature] 找不到 shader「" + PixelartPath.OutlineShaderName
                    + "」（资产引用为空、编译失败或改过名？）。描边不会生效——"
                    + "重跑装配器 PirateCrew/Pixelart/装配像素化路径渲染器可重新写入引用。");
                m_Material = null;
            }
            else
            {
                m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            m_Pass = new Pass(m_Material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (PixelartPath.ActiveRig == null || m_Material == null)
                return;

            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            // 只销毁本 Feature 自己造的材质：描边缓冲归 rig（它统一分配/释放），这里碰它就是双重释放。
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
