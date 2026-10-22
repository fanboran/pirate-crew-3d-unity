using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化域透明趟**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>，
    /// 排在着色合成之后——Cast 特征顺序的第 7 位，前六位顺序是冻结契约）：
    /// 把半透明效果件画进**艺术画布**，是叠加档穿透问题的「真解」。
    ///
    /// 【机制】着色合趟（<see cref="PixelartShadingFeature"/>）已把不透明画面写进
    /// Cast 相机的颜色目标（艺术画布域）；本趟紧随其后，对同一目标做 alpha 混合——
    ///   · **同像素化**：透明件画在艺术画布上，随后的上屏 blit 点采样放大让它与画面
    ///     同颗粒（叠加档的全分辨率颗粒差异从根上消失）；
    ///   · **吃深度遮挡**：shader（`PixelartTransparent.shader`）里手动比细像素深度
    ///     （点采样 `_PixelartDepthBuffer`，连通域趟之后它指向 Patch 版，与着色各趟
    ///     同一份）——崖后的珠点被 clip 掉，等效 ZTest LEqual；
    ///   · **LightMode 自立门户**：只按 <c>ShaderTagId("PixelartTransparent")</c> 收件，
    ///     与 <c>PixelartOpaque</c> 互不相认——标准 URP 材质不会被本趟重复画，
    ///     本路径材质也不会被叠加相机的标准透明趟再画一遍。
    ///
    /// 【为什么不是挂在 Screen 渲染器 / 上屏之后】上屏之后已是全分辨率域，再画就是
    /// 「叠加档」的老问题（颗粒不对、还要自建遮挡）；画进艺术画布才两样都对。
    /// 本趟必须排在合成之后：合成本身是整屏覆盖写，先画会被冲掉。
    ///
    /// 【不绑深度附件】艺术画布没有这趟自己的深度；既有深度是屏幕档尺寸、格式是
    /// depth 附件，绑不成这趟的目标附件。深度判定全部在 shader 片元里手动做
    /// （ZWrite Off，透明件之间按透明队列背→前排序由 <c>SortingCriteria.CommonTransparent</c>
    /// 保证）。首件消费者：弹道预览（`TrajectoryPreview`，原先走叠加相机 + PhysX
    /// 射线等效遮挡）；叠加相机本身保留，其余 FX 仍走它，待特效重做线逐件迁入。
    /// </summary>
    public sealed class PixelartTransparentFeature : ScriptableRendererFeature
    {
        [Tooltip("参与透明趟的层。默认全部层（收件判据是 LightMode=PixelartTransparent + 透明队列，不是层）。")]
        public LayerMask layerMask = ~0;

        /// <summary>
        /// 透明趟 shader 的**序列化资产引用**（构建剥离防线，装配器写入）。
        /// shader 本身由 Resources 持有（<c>Assets/Resources/PixelartShaders/</c>），
        /// 运行时材质走 <see cref="PixelartShaders"/> 取用口；这个字段是播放器构建的保险。
        /// </summary>
        public Shader transparentShader;

        sealed class Pass : ScriptableRenderPass
        {
            static readonly ShaderTagId kPixelartTransparent = new ShaderTagId(PixelartPath.TransparentShaderTagName);

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Transparent");
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
                    return;     // 只有 Cast 相机画透明趟；主相机只上屏

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!m_Logged)
                    {
                        m_Logged = true;
                        global::PirateCrew.Core.Log.Info("[PixelartTransparentFeature] 透明趟已在 " + camera.name
                            + " 上执行：画进艺术画布 " + rig.RenderWidth + "×" + rig.RenderHeight
                            + "（alpha 混合 + 手动细像素深度遮挡，LightMode="
                            + PixelartPath.TransparentShaderTagName + "）。");
                    }

                    // 目标 = Cast 相机的颜色目标（= 艺术画布域；合成趟刚把不透明画面写进去，
                    // 这里在其上混合。不绑深度附件——深度判定在 shader 里手动做，见类注）。
                    cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // 只收 PixelartTransparent 这一条 LightMode 的透明队列材质；
                    // 排序背→前（CommonTransparent）保证透明件之间混合次序正确。
                    // 【队列下界必须取 3000 而不是 RenderQueueRange.transparent（3001）】
                    // 本路径的透明材质声明 `Queue = Transparent` 即 3000，用 URP 的透明档会把
                    // 它们整类漏掉（预览曾因此一个片都不画）。上界不受影响；实际收件仍被
                    // ShaderTagId 门控，不透明件不会被误收。
                    var drawingSettings = RenderingUtils.CreateDrawingSettings(
                        kPixelartTransparent, ref renderingData, SortingCriteria.CommonTransparent);
                    drawingSettings.perObjectData = PerObjectData.None;
                    var filteringSettings = new FilteringSettings(new RenderQueueRange(3000, 5000), m_LayerMask);

                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
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
