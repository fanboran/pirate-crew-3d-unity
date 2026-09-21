using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **低分辨率域着色**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>，排在物体 pass 之后）：
    /// 一趟全屏 blit，读三张 G-buffer，算出最终颜色写进 Cast 相机的颜色目标（= ResultBuffer）。
    ///
    /// 【这一趟的存在意义】它跑在低分辨率域——每个低分辨率像素只着色一次，色带边界天然落在像素上，
    /// 不需要"块对齐"这类补丁；上采样之后没有任何颜色操作（渲染篇 §1 红线 4 在新架构下自动成立）。
    ///
    /// 【为什么是一趟而不是 v3 的四趟】v3 把漫反射/高光/环境光/边缘光各写一张缓冲再相加
    /// （`ShadingPass.hlsl` 的 4 个 Fragment + `CombineFragment` 求和）。那是为了让边缘光能写
    /// "本帧此像素已有边缘光"、进而让漫反射关掉抗锯齿（`applyAA`）。本阶段还没有边缘光，
    /// 也没有连通域，做多趟只是多 3 张缓冲和 4 次全屏开销——所以先合成一趟。
    /// **P4 引入连通域与边缘光时，这里要按 v3 拆回多趟**（边缘光的门控需要那张"已有边缘光"的缓冲）。
    /// </summary>
    public sealed class PixelartShadingFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 着色 shader 的**序列化资产引用**。装配器写入，`Create()` 优先用它。
        ///
        /// 【为什么必须有这个字段，不能只靠 Shader.Find】播放器构建只收"被资产引用链摸到"的 shader；
        /// 只在 C# 里 <c>Shader.Find(名字)</c> 的 shader **会被剥离**——构建日志里既没有它的编译记录、
        /// 也没有报错，运行时 `Shader.Find` 返回 null，表现是"这条 pass 静默不生效"。
        /// 本仓 r2 的洋红事故与既有像素化 Feature 的 blitShader 字段都是同一坑
        /// （见 `PixelationInstaller` 的"shader 剥离防线"注释）。实测：本路径第一版就是这样，
        /// 九张出图内容完全相同（只有背景色），播放器日志里是"找不到 shader"。
        /// </summary>
        public Shader shadingShader;

        sealed class Pass : ScriptableRenderPass
        {
            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Shading");
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

                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!m_Logged)
                    {
                        m_Logged = true;
                        Debug.Log("[PixelartShadingFeature] 着色 pass 已在 " + renderingData.cameraData.camera.name
                            + " 上执行（源 " + rig.AlbedoBuffer.width + "×" + rig.AlbedoBuffer.height + "）。");
                    }

                    // 目标 = Cast 相机的颜色目标（= ResultBuffer）：
                    // 走相机颜色目标而不是直接写 ResultBuffer，是为了让"管线最终把颜色目标搬进
                    // targetTexture"这一步替我们收尾——直接写 ResultBuffer 会在随后被管线
                    // 用（空的）颜色目标覆盖掉。
                    Blitter.BlitTexture(cmd, rig.AlbedoBuffer,
                        renderingData.cameraData.renderer.cameraColorTargetHandle, m_Material, 0);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
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
                    + "\"（资产引用为空、编译失败或改过名？）。着色 pass 不会生效——"
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
