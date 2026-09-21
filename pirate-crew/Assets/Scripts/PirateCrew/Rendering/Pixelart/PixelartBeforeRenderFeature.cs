using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **相机 snap**（Cast 渲染器，<see cref="RenderPassEvent.BeforeRendering"/>）：
    /// 在几何被画之前把视图矩阵的平移分量四舍五入到 1 低分辨率像素的整数倍，并下发
    /// <c>_PixelartUnitSize</c> / RT 尺寸。
    ///
    /// 【为什么要 snap】正交相机平移时，世界到像素的映射每一步都落在非整数像素上，
    /// 边缘与描边会"像素游动"（swimming）。把视图矩阵平移量量化到像素网格后，
    /// 画面只在必须换格时才整体跳一格——这是像素完美两步法的第一步。
    ///
    /// 【为什么不照抄 v3 的注入方式】v3 改的是**自己的**全局矩阵（`PIXELART_CAMERA_MATRIX_V`），
    /// 物体 shader 用那套矩阵做顶点变换；本条改用 <c>cmd.SetViewProjectionMatrices</c>
    /// ——标准 URP 通道（含 DrawRenderers）一律吃 <c>UNITY_MATRIX_VP</c>，
    /// 于是 snap 对整条管线生效，物体 shader 里一行矩阵代码都不用写。
    ///
    /// 【第二步（上采样 UV 反补偿）本阶段未接】那一步要在上屏 blit 时用 snap 的余量偏移 UV，
    /// 才能把"整格跳"还原成平滑移动；它属于相机控制器与上屏器的接口，见蓝图 §7.2 第 1 条。
    /// 试点场景相机静止，本阶段先只做第一步（snap 本身不漏像素）。
    /// </summary>
    public sealed class PixelartBeforeRenderFeature : ScriptableRendererFeature
    {
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

                float unitSize = rig.UnitSize;
                if (unitSize <= 0f)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    Matrix4x4 view = renderingData.cameraData.GetViewMatrix();
                    view.m03 = Mathf.Round(view.m03 / unitSize) * unitSize;
                    view.m13 = Mathf.Round(view.m13 / unitSize) * unitSize;
                    cmd.SetViewProjectionMatrices(view, renderingData.cameraData.GetProjectionMatrix());

                    cmd.SetGlobalFloat(PixelartPath.UnitSizeId, unitSize);
                    cmd.SetGlobalFloat(PixelartPath.RTHeightId, rig.RenderHeight);
                    cmd.SetGlobalFloat(PixelartPath.RTWidthId, rig.RenderWidth);
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
