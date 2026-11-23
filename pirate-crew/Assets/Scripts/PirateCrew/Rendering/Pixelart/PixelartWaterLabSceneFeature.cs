using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **艺术画布快照**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/> 尾位）：
    /// 把着色合成后的艺术画布（<see cref="PixelartCameraRig.ResultBuffer"/>）拷进
    /// <c>_WaterLabPixelartScene</c> 全局纹理，供像素化透明趟的 WaterLab 水面做**折射底色**。
    ///
    /// 【为什么不能直接采 ResultBuffer】透明叠加相机（栈内 Overlay）画水时写的就是同一张
    /// 画布——"边写边读同一张 RT"是未定义行为。快照在 Cast 相机渲染尾（着色已完成、
    /// 叠加相机未开画）取一次，水折射读快照，时序上结构安全。
    ///
    /// 【为什么 CellShade 不用 _CameraOpaqueTexture】像素化域的世界画在 Cast 相机的
    /// ResultBuffer 上，主相机是"上屏器"——它的不透明拷贝里没有场景内容（空/上帧 HUD）。
    /// PostLine 域的水走标准前向趟、照常采 <c>_CameraOpaqueTexture</c>，不经本 Feature。
    ///
    /// 【拷贝口径】沿 <see cref="PixelartScreenCopyFeature"/> 的血泪结论：CoreBlit Nearest
    /// pass（自写 blit 在真 GPU 会报 not supported；CoreBlit 被运行时自身引用必然保活）。
    /// 尺寸随画布（rig 是尺寸单源）逐帧对齐；快照 RT 本 Feature 自管分配/释放。
    /// </summary>
    public sealed class PixelartWaterLabSceneFeature : ScriptableRendererFeature
    {
        /// <summary>快照全局名（CloudSea 版 ocean shader 的 PixelartTransparent 趟消费）。</summary>
        public static readonly int SceneCopyId = Shader.PropertyToID("_WaterLabPixelartScene");

        sealed class Pass : ScriptableRenderPass
        {
            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart WaterLab Scene Copy");
            Material m_Material;
            RenderTexture m_SceneCopy;
            bool m_Logged;

            public Pass(Material material)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                m_Material = material;
            }

            /// <summary>快照 RT 释放（Feature Dispose 调；RT 自管生命周期）。</summary>
            public void Cleanup()
            {
                m_SceneCopy?.Release();
                m_SceneCopy = null;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady || m_Material == null)
                    return;

                // 只对 Cast 相机生效（快照语义 = 这一帧的像素化场景；主相机/叠加相机不拷）。
                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                int width = rig.RenderWidth;
                int height = rig.RenderHeight;
                if (width < 2 || height < 2)
                    return;

                // 快照 RT 尺寸随画布（锁整数倍口径下画布只在档位/分辨率变化时变）。
                if (m_SceneCopy == null || m_SceneCopy.width != width || m_SceneCopy.height != height)
                {
                    m_SceneCopy?.Release();
                    m_SceneCopy = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                    {
                        name = "WaterLabPixelartScene",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    m_SceneCopy.Create();
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!m_Logged)
                    {
                        m_Logged = true;
                        global::PirateCrew.Core.Log.Info("[PixelartWaterLabSceneFeature] 画布快照已在 "
                            + renderingData.cameraData.camera.name + " 上执行（画布 "
                            + width + "×" + height + "，发布 " + "_WaterLabPixelartScene）。");
                    }

                    // 【源必须是相机颜色目标，不是 rig.ResultBuffer】着色趟写的是
                    // cameraColorTargetHandle（见 PixelartShadingFeature 的同条注）；URP 要到
                    // 相机收尾才把颜色目标搬进 targetTexture=ResultBuffer——本站点
                    // （AfterRendering）读 ResultBuffer 拿到的是上一帧/空画布，CellShade 的
                    // 水底折射整片落空（2026-11-22 实拍：水色均匀、鱼/珊瑚全无）。拷当前
                    // 颜色目标 = 着色完成、透明趟未画的那一帧，正是水折射要的干净底图。
                    var cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    if (cameraColor.rt == null)
                        return;
                    Blitter.BlitTexture(cmd, cameraColor.rt, m_SceneCopy,
                        m_Material, PixelartPath.CoreBlitNearestPass);
                    cmd.SetGlobalTexture(SceneCopyId, m_SceneCopy);
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
                global::PirateCrew.Core.Log.Error("[PixelartWaterLabSceneFeature] 找不到 shader \""
                    + PixelartPath.CoreBlitShaderName + "\"——画布快照不生效，像素化水面折射底色落空。");
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

            m_Pass?.Cleanup();
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
