using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **相机 snap 与尺寸下发**（Cast 渲染器，<see cref="RenderPassEvent.BeforeRendering"/>）：
    /// 在几何被画之前把视图矩阵的平移分量四舍五入到 1 低分辨率像素的整数倍，并下发两档尺寸与尺度量。
    ///
    /// 【为什么要 snap】正交相机平移时，世界到像素的映射每一步都落在非整数像素上，边缘与描边会
    /// "像素游动"（swimming）。把视图矩阵平移量量化到像素网格后，画面只在必须换格时才整体跳一格。
    ///
    /// 【为什么不照抄 v3 的注入方式】v3 改的是**自己的**全局矩阵（`PIXELART_CAMERA_MATRIX_V`），
    /// 物体 shader 用那套矩阵做顶点变换；本条改用 <c>cmd.SetViewProjectionMatrices</c>
    /// ——标准 URP 通道（含 DrawRenderers）一律吃 <c>UNITY_MATRIX_VP</c>，于是 snap 对整条管线生效，
    /// 物体 shader 里一行矩阵代码都不用写（物体 shader 里另有"逐物体原点吸附"那一层，
    /// 它读 <c>UNITY_MATRIX_V</c>，读到的就是这里 snap 过的矩阵——两层用的是同一套矩阵，不会打架）。
    ///
    /// 【第二步（上采样 UV 反补偿）**仍未接**】那一步要在上屏 blit 时用 snap 的余量偏移 UV。
    /// 本阶段不做的理由是**它会与"块边长恒等于 pixelScale"这条硬指标冲突**：
    /// 上屏是点采样放大，UV 一旦带上分数偏移，块边界就不再落在 pixelScale 的整数倍上，
    /// 屏幕上会出现 2/4/5 混排的块边——正是创始人报过的"像素不齐"。要接的话必须先推导出
    /// "整数屏幕像素级补偿"的写法，属独立议题（契约 §6、渲染篇 §2 的两步法）。
    /// </summary>
    public sealed class PixelartBeforeRenderFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 附加光循环上界的硬夹（见 Execute 里的理由）。**这是"接真实内容时要重标"的旋钮之一**：
        /// 全屏着色没有逐物体光源列表，条数一多就是每个像素都套一遍。
        /// </summary>
        public const int MaxAdditionalLights = 8;

        sealed class Pass : ScriptableRenderPass
        {
            public Pass()
            {
                renderPassEvent = RenderPassEvent.BeforeRendering;
                profilingSampler = new ProfilingSampler("Pixelart BeforeRender");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady || rig.CastCamera == null)
                    return;

                // 只在 Cast 相机上干活：主相机走同一条渲染器时会重复 snap（无害但没必要）。
                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    float unitSize = rig.UnitSize;
                    if (unitSize > 0f)
                    {
                        Matrix4x4 view = renderingData.cameraData.GetViewMatrix();
                        view.m03 = Mathf.Round(view.m03 / unitSize) * unitSize;
                        view.m13 = Mathf.Round(view.m13 / unitSize) * unitSize;
                        cmd.SetViewProjectionMatrices(view, renderingData.cameraData.GetProjectionMatrix());
                    }

                    // 两档尺寸与尺度量：着色/描边各趟在艺术画布上算，物体 pass 在屏幕档上算，
                    // 两边都要拿到自己那一档的尺寸（本仓不使用 _ScreenParams，契约 §2.2）。
                    cmd.SetGlobalFloat(PixelartPath.UnitSizeId, unitSize);
                    cmd.SetGlobalFloat(PixelartPath.RTHeightId, rig.RenderHeight);
                    cmd.SetGlobalFloat(PixelartPath.RTWidthId, rig.RenderWidth);
                    cmd.SetGlobalFloat(PixelartPath.FineUnitSizeId, rig.FineUnitSize);
                    cmd.SetGlobalFloat(PixelartPath.FineHeightId, rig.FineHeight);
                    cmd.SetGlobalFloat(PixelartPath.FineWidthId, rig.FineWidth);
                    cmd.SetGlobalFloat(PixelartPath.SamplingScaleId, rig.PixelScale);
                    cmd.SetGlobalFloat(PixelartPath.AAThresholdId, rig.AAThreshold);

                    // 附加光条数（v3 的 _AdditionalLightCount；着色那几趟的附加光循环要用）。
                    // 【为什么要夹一刀】这里拿到的是**相机可见附加光总数**（桌面上限 256），
                    // 而我们的着色是**一趟全屏 blit、没有逐物体光源列表**——照原值写进循环上界，
                    // 场景一多光就变成"对每个像素套全部附加光"，帧率会掉得莫名其妙且极难归因。
                    // 夹到 8 是"试点场景够用、接真实内容时再来调"的保守值（要改就改这一处）。
                    int additionalLights = Mathf.Clamp(
                        renderingData.lightData.additionalLightsCount, 0, MaxAdditionalLights);
                    cmd.SetGlobalFloat(PixelartPath.AdditionalLightCountId, additionalLights);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        Pass m_Pass;

        public override void Create()
        {
            m_Pass = new Pass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (PixelartPath.ActiveRig == null)
                return;

            renderer.EnqueuePass(m_Pass);
        }
    }
}
