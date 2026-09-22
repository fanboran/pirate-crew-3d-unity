using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **物体 pass**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>）：
    /// 把场景几何画进**屏幕档**的 7 张 G-buffer（albedo / normal0 / normal1 / physical / shape /
    /// palette / rimlight）+ 可采样深度，**不做任何着色**。
    ///
    /// 【与"前向着色 + 事后降采"的分野】着色被搬到了后面的四趟（<see cref="PixelartShadingFeature"/>），
    /// 运行在低分辨率域（艺术画布）。所以本 pass 的输出不是颜色、是数据；任何"在这里把颜色算完"的
    /// 改动都会把路径退化回旧形状。
    ///
    /// 【为什么用 ShaderTagId 而不是画全部不透明物】本路径的材质只声明
    /// <c>LightMode = "PixelartOpaque"</c> 这一条 pass——URP 的标准不透明 pass 匹配不到任何东西，
    /// 于是同一台相机不会被画两遍；同时我们仍然吃引擎的**可见性剔除 + 排序**（v3 同法）。
    ///
    /// 【为什么不再逐物体提交/远→近排序】那是"反向壳描边"时代的要求（壳要与大平面抢深度、
    /// 又要成对"壳→本体"）。描边改成艺术画布的屏幕空间膨胀之后，几何只靠深度测试分前后
    /// （本体 `ZTest LEqual` + `ZWrite On`），手工排序与 `CommandBuffer.DrawRenderer` 一并撤掉。
    /// </summary>
    public sealed class PixelartObjectFeature : ScriptableRendererFeature
    {
        [Tooltip("参与物体 pass 的层。默认全部层（试点场景只有一个内容根，不需要精挑层）。")]
        public LayerMask layerMask = ~0;

        sealed class Pass : ScriptableRenderPass
        {
            static readonly ShaderTagId kPixelartOpaque = new ShaderTagId(PixelartPath.OpaqueShaderTagName);

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Object");
            readonly RenderTargetIdentifier[] m_Mrt = new RenderTargetIdentifier[7];
            LayerMask m_LayerMask;
            bool m_Logged;

            public Pass(LayerMask layerMask)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                m_LayerMask = layerMask;
            }

            public void SetLayerMask(LayerMask layerMask)
            {
                m_LayerMask = layerMask;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady)
                    return;

                Camera camera = renderingData.cameraData.camera;
                if (camera != rig.CastCamera)
                    return;     // 只有 Cast 相机画几何；主相机只上屏

                m_Mrt[0] = rig.AlbedoBuffer;
                m_Mrt[1] = rig.Normal0Buffer;
                m_Mrt[2] = rig.Normal1Buffer;
                m_Mrt[3] = rig.PhysicalBuffer;
                m_Mrt[4] = rig.ShapeBuffer;
                m_Mrt[5] = rig.PaletteBuffer;
                m_Mrt[6] = rig.RimLightPropertyBuffer;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!m_Logged)
                    {
                        m_Logged = true;
                        Debug.Log("[PixelartObjectFeature] 物体 pass 已在 " + camera.name
                            + " 上执行：屏幕档 " + rig.FineWidth + "×" + rig.FineHeight
                            + "，7 张 G-buffer + 可采样深度（艺术画布 " + rig.RenderWidth + "×"
                            + rig.RenderHeight + "）。");
                    }

                    // ① Cast 相机的颜色目标（= ResultBuffer）先落一层背景色：
                    //    着色 pass 里没有几何的像素会被 clip 掉，留下的就是它。
                    cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                    cmd.ClearRenderTarget(false, true, camera.backgroundColor, 1f);

                    // ② G-buffer：7 张 MRT + 深度。深度附件是必须的——不透明物之间要靠它分前后，
                    //    而且着色几趟要靠它重建 positionWS（阴影坐标与附加光）。
                    cmd.SetRenderTarget(m_Mrt, rig.DepthBuffer);
                    cmd.ClearRenderTarget(true, true, Color.clear, 1f);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    var drawingSettings = RenderingUtils.CreateDrawingSettings(
                        kPixelartOpaque, ref renderingData, SortingCriteria.CommonOpaque);
                    drawingSettings.perObjectData = PerObjectData.None;
                    var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);

                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);

                    rig.PublishGbuffers(cmd);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        Pass m_Pass;

        public override void Create()
        {
            m_Pass = new Pass(layerMask);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (PixelartPath.ActiveRig == null)
                return;

            m_Pass.SetLayerMask(layerMask);
            renderer.EnqueuePass(m_Pass);
        }
    }
}
