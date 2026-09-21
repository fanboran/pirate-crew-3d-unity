using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **上屏**（Screen 渲染器，<see cref="RenderPassEvent.AfterRendering"/>）：
    /// 把 ResultBuffer 点采样放大贴到主相机的颜色目标上——整条路径的最后一步，
    /// 也是"像素化"这个观感的物理来源（低分辨率格子在屏幕上是硬的）。
    ///
    /// 【为什么用 CoreBlit 的 Nearest pass 而不是 cmd.Blit】本仓已有血泪结论：
    /// 自写 blit shader 在播放器真实 GPU 会话报 "not supported on this GPU"；
    /// CoreBlit 被 URP 运行时自身引用，播放器构建必然保活。
    /// 它的 pass 0 内部用 <c>sampler_PointClamp</c>，点采样不依赖源 RT 的 filterMode（双保险）。
    ///
    /// 【挂错渲染器的后果】主相机若挂成 Cast 渲染器，它会再跑一遍物体/着色 pass：
    /// 先清掉刚画好的 G-buffer，再用空内容着色并把结果铺到屏幕上——表现是"全屏背景色"。
    /// 装配器会把两个索引写进 <see cref="PixelartCameraRig"/>，运行时由它钉住。
    /// </summary>
    public sealed class PixelartScreenCopyFeature : ScriptableRendererFeature
    {
        sealed class Pass : ScriptableRenderPass
        {
            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Screen Copy");
            Material m_Material;
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

                if (renderingData.cameraData.camera != rig.ScreenCamera)
                    return;     // 只有主相机负责上屏

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!m_Logged)
                    {
                        m_Logged = true;
                        Debug.Log("[PixelartScreenCopyFeature] 上屏 blit 已在 "
                            + renderingData.cameraData.camera.name + " 上执行（源 "
                            + rig.ResultBuffer.width + "×" + rig.ResultBuffer.height + "）。");
                    }

                    Blitter.BlitTexture(cmd, rig.ResultBuffer,
                        renderingData.cameraData.renderer.cameraColorTargetHandle,
                        m_Material, PixelartPath.CoreBlitNearestPass);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        Pass m_Pass;
        Material m_Material;

        public override void Create()
        {
            m_Material = PixelartPath.CreateCoreBlitMaterial();
            if (m_Material == null)
            {
                Debug.LogError("[PixelartScreenCopyFeature] 找不到 shader \""
                    + PixelartPath.CoreBlitShaderName + "\"——URP 包异常或被改名，屏幕不会有画面。");
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
