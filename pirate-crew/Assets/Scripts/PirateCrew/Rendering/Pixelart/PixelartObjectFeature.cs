using System.Collections.Generic;
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
            FilteringSettings m_BackgroundFiltering;
            FilteringSettings m_ForegroundFiltering;
            bool m_Logged;

            public Pass(LayerMask layerMask)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;
                SetLayerMask(layerMask);
            }

            /// <summary>
            /// 层掩码是 Feature 上的序列化字段，编辑器里改了要能立刻生效。
            /// 同时按 renderQueue 把"大平面（背景）"与"描边物（前景）"分成两段——
            /// 两段的绘制次序是描边成立的前提，见 <see cref="Execute"/>。
            /// </summary>
            public void SetLayerMask(LayerMask layerMask)
            {
                m_LayerMaskValue = layerMask.value;
                m_BackgroundFiltering = new FilteringSettings(
                    new RenderQueueRange(0, PixelartPath.BackgroundPlaneRenderQueue), layerMask);
                m_ForegroundFiltering = new FilteringSettings(
                    new RenderQueueRange(PixelartPath.ForegroundRenderQueueMin, 5000), layerMask);
            }

            int m_LayerMaskValue = ~0;

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

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    // 背景先落一层（着色 pass 里没有几何的像素会被 clip 掉，留下的就是它）。
                    cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                    cmd.ClearRenderTarget(false, true, camera.backgroundColor, 1f);

                    // G-buffer：三张 MRT + 深度。深度附件是必须的——不透明物之间要靠它分前后。
                    cmd.SetRenderTarget(m_Mrt, rig.DepthBuffer);
                    cmd.ClearRenderTarget(true, true, Color.clear, 1f);

                    CollectPixelartRenderers(camera, m_Background, m_Foreground);

                    Material anyMaterial = m_Foreground.Count > 0
                        ? m_Foreground[0].sharedMaterial
                        : (m_Background.Count > 0 ? m_Background[0].sharedMaterial : null);
                    if (anyMaterial == null || !EnsurePassIndices(anyMaterial, camera.name))
                    {
                        context.ExecuteCommandBuffer(cmd);
                        CommandBufferPool.Release(cmd);
                        return;
                    }

                    // 背景（大平面）：只画本体，先整片画完。
                    for (int i = 0; i < m_Background.Count; i++)
                        cmd.DrawRenderer(m_Background[i], m_Background[i].sharedMaterial, 0, m_BodyPassIndex);

                    // 前景：逐物体"壳 → 本体"，**远的先画**（理由见 FartherFirst 的注）。
                    for (int i = 0; i < m_Foreground.Count; i++)
                    {
                        MeshRenderer renderer = m_Foreground[i];
                        Material material = renderer.sharedMaterial;
                        if (material.GetFloat(kOutlinePixelsId) > 0.01f)
                            cmd.DrawRenderer(renderer, material, 0, m_InkPassIndex);
                        cmd.DrawRenderer(renderer, material, 0, m_BodyPassIndex);
                    }

                    rig.PublishBuffersToShaders(cmd);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            static readonly int kOutlinePixelsId = Shader.PropertyToID("_OutlinePixels");
            readonly List<MeshRenderer> m_Background = new List<MeshRenderer>();
            readonly List<MeshRenderer> m_Foreground = new List<MeshRenderer>();
            int m_BodyPassIndex = -1;
            int m_InkPassIndex = -1;

            /// <summary>
            /// 取两条 pass 的索引（**按 pass 名而不是序号**，序号一改就会静默画错 pass）。
            /// </summary>
            bool EnsurePassIndices(Material material, string cameraName)
            {
                if (m_BodyPassIndex >= 0 && m_InkPassIndex >= 0)
                    return true;

                m_BodyPassIndex = material.FindPass(PixelartPath.OpaqueShaderTagName);
                m_InkPassIndex = material.FindPass(PixelartPath.InkShaderTagName);
                if (m_BodyPassIndex < 0 || m_InkPassIndex < 0)
                {
                    Debug.LogError("[PixelartObjectFeature] 物体 shader 里找不到 pass \""
                        + PixelartPath.OpaqueShaderTagName + "\"(=" + m_BodyPassIndex + ") 或 \""
                        + PixelartPath.InkShaderTagName + "\"(=" + m_InkPassIndex
                        + ")——逐物体绘制拿不到 pass，本帧不画几何。");
                    return false;
                }

                Debug.Log("[PixelartObjectFeature] 逐物体绘制已在 " + cameraName + " 上执行：MRT "
                    + m_Mrt[0].ToString() + "；本体 pass 索引 " + m_BodyPassIndex
                    + " / 墨线 pass 索引 " + m_InkPassIndex + "（按 pass 名解析）。");
                return true;
            }

            /// <summary>
            /// 收集走本路径物体 shader 的 renderer，按 renderQueue 分背景/前景，按**近→远**排序。
            ///
            /// 【为什么必须自己排 + 逐物体提交】见下面"绘制次序"那条注：DrawRenderers 是**按 tag 分批**的
            /// （先把所有壳画完、再画所有本体），那样任何物体的环都会被随后画的本体盖掉——
            /// 整个场景只剩一圈外轮廓、每个物体没有自己的描边。逐物体"壳→本体"成对提交才有独立描边，
            /// 而"近→远"是为了让远处的环被近处的物体正确遮住（靠 LEqual）。
            ///
            /// 【代价与后续】自己排就绕开了引擎的可见性裁剪（本路径物体量级几十个，可忽略）；
            /// 接进真实内容若成为瓶颈，应改用 CullingResults 的可见列表 + RendererList。
            /// </summary>
            void CollectPixelartRenderers(Camera camera, List<MeshRenderer> background, List<MeshRenderer> foreground)
            {
                background.Clear();
                foreground.Clear();

                Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
                MeshRenderer[] all = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    MeshRenderer renderer = all[i];
                    Material material = renderer.sharedMaterial;
                    if (material == null || material.shader == null
                        || material.shader.name != PixelartPath.ObjectShaderName)
                        continue;
                    if ((m_LayerMaskValue & (1 << renderer.gameObject.layer)) == 0)
                        continue;

                    if (material.renderQueue < PixelartPath.ForegroundRenderQueueMin)
                        background.Add(renderer);
                    else
                        foreground.Add(renderer);
                }

                background.Sort((a, b) => FartherFirst(a, b, eye));
                foreground.Sort((a, b) => FartherFirst(a, b, eye));
            }

            /// <summary>
            /// **远 → 近**。这个方向是"逐物体独立描边"成立的关键，配 <see cref="Execute"/> 里
            /// "壳（ZTest Always）→ 本体（LEqual、ZWrite On）"这条次序：
            ///   · 每个物体的壳先把**它整个剪影**画上去（连环），随后它自己的本体盖回内部 ⇒ 只余环；
            ///   · 环会盖在**更远的**物体上（正确：近物的轮廓本该压在远物上）；
            ///   · 比它更近的物体**随后才画**，它们的本体把覆盖处的环盖掉（正确：近物遮住远物的轮廓）。
            /// 反过来（近→远）时，远物的壳会盖到近物身上，就必须靠 LEqual 挡——而 LEqual 又会让
            /// 环跟先画完的大平面抢深度（实测近侧下缘整条丢光）。两个约束只有"远→近 + Always"同时满足。
            /// </summary>
            static int FartherFirst(Renderer a, Renderer b, Vector3 eye)
            {
                float da = (a.bounds.center - eye).sqrMagnitude;
                float db = (b.bounds.center - eye).sqrMagnitude;
                return db.CompareTo(da);
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
