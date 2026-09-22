using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **低分辨率域着色**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>，排在描边之后）：
    /// 四趟全屏 blit，读两档缓冲出最终颜色：Diffuse → Specular → GI → **Combine**。
    ///
    /// 【为什么是四趟而不是一趟】v3 把漫反射/高光/环境光/边缘光各写一张缓冲再相加
    /// （`ShadingPass.hlsl` 的多个 Fragment + `CombineFragment`）。分开的理由不是"分层好看"，而是
    /// **门控要跨趟**：漫反射要读边缘光缓冲来决定"这个像素要不要关掉连通域降档"（`applyAA`），
    /// 而合成又必须等四张都算完才能做——一趟里做不完。
    ///
    /// 【这一趟跑在哪一档】**艺术画布**（每个艺术像素只着色一次，色带边界天然落在像素上，
    /// 不需要"块对齐"这类补丁；上采样之后没有任何颜色操作）。几何与 G-buffer 在屏幕档，
    /// 这里按 uv 点采样取到的是块中心那个纹素 ⇒ 与"直接在画布上光栅化"逐点等价（契约 §0）。
    /// </summary>
    public sealed class PixelartShadingFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 着色 shader 的**序列化资产引用**。装配器写入，`Create()` 优先用它。
        ///
        /// 【为什么必须有这个字段，不能只靠 Shader.Find】播放器构建只收"被资产引用链摸到"的 shader；
        /// 只在 C# 里 <c>Shader.Find(名字)</c> 的 shader **会被剥离**——构建日志里既没有它的编译记录、
        /// 也没有报错，运行时 `Shader.Find` 返回 null，表现是"这条 pass 静默不生效"。
        /// </summary>
        public Shader shadingShader;

        sealed class Pass : ScriptableRenderPass
        {
            /// <summary>Blit.hlsl 的 Vert 会读它做 uv 缩放偏置；不显式置成单位值就是 0 ⇒ uv 恒为 0。</summary>
            static readonly int kBlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Shading");
            readonly RenderTargetIdentifier[] m_ResultTargets = new RenderTargetIdentifier[3];
            Material m_Material;
            int m_DiffusePass = -1;
            int m_SpecularPass = -1;
            int m_GIPass = -1;
            int m_CombinePass = -1;
            bool m_Logged;

            public Pass(Material material)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                m_Material = material;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady || m_Material == null)
                    return;

                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!EnsurePassIndices(renderingData.cameraData.camera.name))
                    {
                        context.ExecuteCommandBuffer(cmd);
                        CommandBufferPool.Release(cmd);
                        return;
                    }

                    // 连通域结论与墨线标记由前两趟写出；这里再发布一次做保险（全局纹理在同一帧内持久，
                    // 但显式发布让"哪一趟依赖哪张缓冲"在代码里可读）。
                    rig.PublishArtBuffers(cmd);
                    cmd.SetGlobalFloat(PixelartPath.AdditionalLightCountId,
                        renderingData.lightData.additionalLightsCount);

                    // 三张结果缓冲先清成透明黑：背景像素会被 clip 掉、墨线像素提前返回，
                    // 不清的话读到的是上一帧的残留（这类"越用越脏"的故障很难反查）。
                    // 【为什么不绑成 MRT 一次清完】`CommandBuffer.SetRenderTarget` 收数组的那个重载
                    // **必须带深度附件**，而这三趟都要采样 `_PixelartDepthBuffer` 重建 positionWS——
                    // 把同一张 RT 同时当 DSV 与 SRV 在 D3D11 上是非法绑定。所以逐张清，不图省那两次提交。
                    ClearToTransparent(cmd, rig.DiffuseBuffer);
                    ClearToTransparent(cmd, rig.SpecularBuffer);
                    ClearToTransparent(cmd, rig.GIBuffer);

                    DrawFullscreen(cmd, rig.DiffuseBuffer, m_DiffusePass);
                    DrawFullscreen(cmd, rig.SpecularBuffer, m_SpecularPass);
                    DrawFullscreen(cmd, rig.GIBuffer, m_GIPass);

                    // 【必须发布成全局纹理，否则合成趟读到的是空缓冲】
                    // 这三张只被当**渲染目标**写过，`Combine` 却是按 `_PixelartDiffuseBuffer` 这样的
                    // 全局名去采样的——不发布的话它绑的是"没绑过"的状态：`clip(diffuse.a - 1)` 把整屏剪掉，
                    // 画面只剩 Cast 颜色目标上的清屏色。**这个故障在日志里一点痕迹都没有**（每一趟都"执行了"），
                    // 首轮出图实测症状就是"整屏同色、块边长检测失败"。
                    // 边缘光那张由边缘光 Feature 自己发布（同一帧内全局持久，两边都发布也不冲突）。
                    cmd.SetGlobalTexture(PixelartPath.DiffuseBufferId, rig.DiffuseBuffer);
                    cmd.SetGlobalTexture(PixelartPath.SpecularBufferId, rig.SpecularBuffer);
                    cmd.SetGlobalTexture(PixelartPath.GIBufferId, rig.GIBuffer);
                    cmd.SetGlobalTexture(PixelartPath.RimLightBufferId, rig.RimLightBuffer);

                    // 合成：写到 Cast 相机的颜色目标（= ResultBuffer）。
                    // 走相机颜色目标而不是直接写 ResultBuffer，是为了让"管线最终把颜色目标搬进
                    // targetTexture"这一步替我们收尾——直接写 ResultBuffer 会在随后被（空的）颜色目标覆盖。
                    cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                    cmd.SetGlobalVector(kBlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    cmd.DrawProcedural(Matrix4x4.identity, m_Material, m_CombinePass,
                        MeshTopology.Triangles, 3, 1);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            /// <summary>把一张缓冲清成透明黑（单目标重载，不碰深度附件）。</summary>
            static void ClearToTransparent(CommandBuffer cmd, RenderTexture target)
            {
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(false, true, Color.clear, 1f);
            }

            /// <summary>一趟全屏：目标 = 指定缓冲，全屏三角形由 shader 侧 Blit.hlsl 的 Vert 提供。</summary>
            void DrawFullscreen(CommandBuffer cmd, RenderTexture destination, int passIndex)
            {
                cmd.SetRenderTarget(destination);
                cmd.SetGlobalVector(kBlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, passIndex,
                    MeshTopology.Triangles, 3, 1);
            }

            /// <summary>
            /// 取四条 pass 的索引（**按 pass 名而不是序号**，序号一改就会静默画错 pass）。
            /// </summary>
            bool EnsurePassIndices(string cameraName)
            {
                if (m_DiffusePass >= 0 && m_SpecularPass >= 0 && m_GIPass >= 0 && m_CombinePass >= 0)
                    return true;

                m_DiffusePass = m_Material.FindPass(PixelartPath.DiffusePassName);
                m_SpecularPass = m_Material.FindPass(PixelartPath.SpecularPassName);
                m_GIPass = m_Material.FindPass(PixelartPath.GIPassName);
                m_CombinePass = m_Material.FindPass(PixelartPath.CombinePassName);

                if (m_DiffusePass < 0 || m_SpecularPass < 0 || m_GIPass < 0 || m_CombinePass < 0)
                {
                    Debug.LogError("[PixelartShadingFeature] 着色 shader 里找不到 pass："
                        + PixelartPath.DiffusePassName + "(" + m_DiffusePass + ") / "
                        + PixelartPath.SpecularPassName + "(" + m_SpecularPass + ") / "
                        + PixelartPath.GIPassName + "(" + m_GIPass + ") / "
                        + PixelartPath.CombinePassName + "(" + m_CombinePass
                        + ")——四趟着色拿不到 pass，本帧不着色（画面只会剩背景色）。");
                    return false;
                }

                if (!m_Logged)
                {
                    m_Logged = true;
                    Debug.Log("[PixelartShadingFeature] 着色四趟已在 " + cameraName + " 上执行：Diffuse "
                        + m_DiffusePass + " / Specular " + m_SpecularPass + " / GI " + m_GIPass
                        + " / Combine " + m_CombinePass + "（按 pass 名解析）。");
                }
                return true;
            }
        }

        Pass m_Pass;
        Material m_Material;

        public override void Create()
        {
            Shader shader = shadingShader != null ? shadingShader : Shader.Find(PixelartPath.ShadingShaderName);
            if (shader == null)
            {
                Debug.LogError("[PixelartShadingFeature] 找不到 shader \"" + PixelartPath.ShadingShaderName
                    + "\"（资产引用为空、编译失败或改过名？）。着色四趟不会生效——"
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

            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
