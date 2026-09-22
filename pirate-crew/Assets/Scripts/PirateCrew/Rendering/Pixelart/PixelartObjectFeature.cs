using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **物体 pass**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>）：
    /// 把场景几何画进低分辨率 **G-buffer**（albedo / 世界法线 / 逐物体参数），**不做任何着色**。
    ///
    /// 【与"前向着色 + 事后降采"的分野】着色被搬到了下一趟（<see cref="PixelartShadingFeature"/>），
    /// 运行在低分辨率域。所以本 pass 的输出不是颜色，是数据；任何"在这里把颜色算完"的改动
    /// 都会把路径退化回旧形状（渲染篇 §3.1 推论 4 的后一半就是说的这件事）。
    ///
    /// 【为什么用 ShaderTagId 而不是画全部不透明物】本路径的材质只声明
    /// <c>LightMode = "PixelartOpaque"</c> 这一条 pass——URP 的标准不透明 pass
    /// （SRPDefaultUnlit / UniversalForward 那一族）在此材质上匹配不到任何东西，
    /// 于是同一台相机不会被画两遍（v3 用同一手法：`ShaderTagId("PixelartOpaque")`）。
    /// </summary>
    public sealed class PixelartObjectFeature : ScriptableRendererFeature
    {
        [Tooltip("参与物体 pass 的层。默认全部层（试点场景只有一个内容根，不需要精挑层）。")]
        public LayerMask layerMask = ~0;

        sealed class Pass : ScriptableRenderPass
        {
            static readonly ShaderTagId kPixelartOpaque = new ShaderTagId(PixelartPath.OpaqueShaderTagName);
            static readonly ShaderTagId kPixelartInk = new ShaderTagId(PixelartPath.InkShaderTagName);

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Object");
            readonly RenderTargetIdentifier[] m_Mrt = new RenderTargetIdentifier[3];
            FilteringSettings m_Filtering;
            bool m_Logged;

            public Pass(LayerMask layerMask)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                SetLayerMask(layerMask);
            }

            /// <summary>层掩码是 Feature 上的序列化字段，编辑器里改了要能立刻生效。</summary>
            public void SetLayerMask(LayerMask layerMask)
            {
                m_Filtering = new FilteringSettings(RenderQueueRange.opaque, layerMask);
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
                m_Mrt[1] = rig.NormalBuffer;
                m_Mrt[2] = rig.PropertyBuffer;

                if (!m_Logged)
                {
                    m_Logged = true;
                    Debug.Log("[PixelartObjectFeature] 物体 pass 已在 " + camera.name + " 上执行：MRT "
                        + rig.AlbedoBuffer.width + "×" + rig.AlbedoBuffer.height
                        + "、层掩码 " + m_Filtering.layerMask + "。");
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    // 背景先落一层（着色 pass 里没有几何的像素会被 clip 掉，留下的就是它）。
                    cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                    cmd.ClearRenderTarget(false, true, camera.backgroundColor, 1f);

                    // G-buffer：三张 MRT + 深度。深度附件是必须的——不透明物之间要靠它分前后。
                    cmd.SetRenderTarget(m_Mrt, rig.DepthBuffer);
                    cmd.ClearRenderTarget(true, true, Color.clear, 1f);
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                SortingCriteria sorting = renderingData.cameraData.defaultOpaqueSortFlags;

                // 本体先画、墨线后画。顺序不是审美偏好，是深度决定的：
                // 墨线壳是**背面外扩**，它的深度在本体背面（比本体远、比身后的地面近）。
                // 后画 + ZTest LEqual ⇒ 内部被本体挡掉（不写进去），轮廓外那一圈留在已经画好的
                // 地面之上。反过来先画墨线，它不写深度（ZWrite Off），随后画的地面会把轮廓环**整个盖掉**
                // ——实测症状就是"加了描边但一根线都看不到"。
                DrawingSettings bodySettings = CreateDrawingSettings(kPixelartOpaque, ref renderingData, sorting);
                context.DrawRenderers(renderingData.cullResults, ref bodySettings, ref m_Filtering);

                DrawingSettings inkSettings = CreateDrawingSettings(kPixelartInk, ref renderingData, sorting);
                context.DrawRenderers(renderingData.cullResults, ref inkSettings, ref m_Filtering);

                // 画完把三张 G-buffer 设成 shader 全局：下一趟着色 pass 只吃全局、不重绑目标。
                rig.PublishBuffersToShaders(cmd);
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
