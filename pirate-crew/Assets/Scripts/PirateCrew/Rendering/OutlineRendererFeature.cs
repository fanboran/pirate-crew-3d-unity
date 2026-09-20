using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// M2 全屏后处理描边（对应 Godot modules/pirate_crew/shaders/outline_post.gdshader）。
    ///
    /// 【工作流程】
    ///   1. Pass 0 "SelectionMask"：把 <see cref="_settings.maskLayer"/> 层上的对象
    ///      （即"已选中单位"）用 overrideMaterial 画成纯白，写进一张全屏 mask RT。
    ///      这一 Pass 只画剪影，ZWrite Off / ZTest Always，不碰相机深度缓冲。
    ///   2. Pass 1 "PostOutline"：全屏 Sobel 边缘检测 mask，得到轮廓后沿边缘切线切流动虚线，
    ///      与相机颜色合成（输出到临时 RT 再拷回相机颜色，对应 Godot 的全屏 quad）。
    ///
    /// 【与 Godot 版接线方式的差异（pirate_base.gd / battle.gd）】
    ///   Godot：SubViewport + 独立 mask 相机 + selection_mask_viewport 手工接纹理。
    ///   URP ：RendererFeature 自己分配 mask RT 并直接用 overrideMaterial 画，少一个相机。
    ///
    /// 【使用前提（务必阅读 docs/描边Shader调试.md）】
    ///   - 在 URP Renderer 资产（Assets/Settings/URP/PC_*_Renderer.asset）Inspector 里
    ///     "Add Renderer Feature" 添加本 Feature，并指定 maskLayer / 描边色。
    ///   - 把"可选中单位"放到专用 Layer（如 SelectionOutline），maskLayer 只勾选它。
    ///     默认 Everything 只是便于立刻看到效果（调试），正式使用请收窄。
    ///
    /// 【程序集说明】
    ///   本文件位于 Assets/Scripts/PirateCrew/Rendering/，该目录带独立 asmdef
    ///   （PirateCrew.Rendering.asmdef，引用 URP/Core 运行时程序集）。
    ///   原因：PirateCrew.Gameplay.asmdef 的 references 为空，无法引用 URP 类型。
    /// </summary>
    [DisallowMultipleRendererFeature("PirateCrew Outline")]
    public class OutlineRendererFeature : ScriptableRendererFeature
    {
        /// <summary>后处理 shader 的完整 Shader 名，用于 Shader.Find 回退。</summary>
        public const string OutlinePostShaderName = "PirateCrew/PirateOutlinePost";

        [System.Serializable]
        public class OutlineSettings
        {
            [Tooltip("后处理描边 shader。留空则按 Shader.Find(\"PirateCrew/PirateOutlinePost\") 查找；" +
                     "正式出包建议显式拖入，避免 Shader 被剔除。")]
            public Shader outlinePostShader;

            [Tooltip("本 Pass 在 URP 管线中的插入时机。不透明物体画完之后、半透明之前最合适。")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

            [Tooltip("哪些 Layer 上的物体会被画进 mask（即会被描边的对象）。" +
                     "调试可设 Everything；正式使用请只勾选单位所在 Layer。")]
            public LayerMask maskLayer = ~0;

            [Tooltip("描边颜色，默认对应 Godot #49d9d6f2")]
            public Color outlineColor = new Color(0.286f, 0.851f, 0.839f, 0.949f);

            [Range(0.01f, 0.9f)]
            [Tooltip("Sobel 边缘阈值：越大描边越细/越少。Godot 默认 0.2")]
            public float edgeThreshold = 0.2f;

            [Range(2.0f, 20.0f)] public float dashLength = 8.0f;
            [Range(2.0f, 20.0f)] public float dashGap = 6.0f;
            [Range(0.0f, 20.0f)] public float dashSpeed = 5.0f;

            [Range(0, 4)]
            [Tooltip("调试档：0 正常 / 1 原始mask / 2 二值mask / 3 Sobel灰度 / 4 纯边缘")]
            public int debugMode = 0;
        }

        public OutlineSettings settings = new OutlineSettings();

        Material m_Material;
        OutlinePass m_Pass;
        bool m_ShaderMissingLogged;

        /// <summary>供外部（如采集脚本 / 状态系统）查询本 Feature 是否可用。</summary>
        public bool IsReady => m_Material != null && m_Pass != null;

        public override void Create()
        {
            Shader shader = settings.outlinePostShader != null
                ? settings.outlinePostShader
                : Shader.Find(OutlinePostShaderName);

            if (shader == null)
            {
                m_Material = null;
                m_ShaderMissingLogged = false;
                m_Pass = new OutlinePass(null, settings);
                return;
            }

            m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            m_Pass = new OutlinePass(m_Material, settings);
            m_Pass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Material == null)
            {
                // 明确报错而不是静默不生效：shader 缺失是本 Feature 最常见的"没反应"原因。
                if (!m_ShaderMissingLogged)
                {
                    m_ShaderMissingLogged = true;
                    Debug.LogError("[OutlineRendererFeature] 未找到 shader \"" + OutlinePostShaderName
                        + "\"。请在 Renderer Feature 的 Outline Post Shader 字段显式指定 "
                        + "Assets/Art/Shaders/PirateOutlinePost.shader，或确认 Shader.Find 能找到它。");
                }
                return;
            }

            // 材质球预览 / 反射探针这类离屏相机不参与，避免污染与无谓开销。
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection
                || renderingData.cameraData.isPreviewCamera)
                return;

            m_Pass.renderPassEvent = settings.renderPassEvent;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            m_Pass?.Dispose();
            m_Pass = null;

            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }

        // ================================================================
        // 内部 Pass
        // ================================================================
        private class OutlinePass : ScriptableRenderPass
        {
            const int kMaskPassIndex = 0;
            const int kCompositePassIndex = 1;
            const string kMaskRtName = "_PirateSelectionMask";
            const string kTempRtName = "_PirateOutlineTemp";

            static readonly int s_SelectionMaskId = Shader.PropertyToID("_SelectionMask");
            static readonly int s_OutlineColorId = Shader.PropertyToID("_OutlineColor");
            static readonly int s_EdgeThresholdId = Shader.PropertyToID("_EdgeThreshold");
            static readonly int s_DashLengthId = Shader.PropertyToID("_DashLength");
            static readonly int s_DashGapId = Shader.PropertyToID("_DashGap");
            static readonly int s_DashSpeedId = Shader.PropertyToID("_DashSpeed");
            static readonly int s_DebugModeId = Shader.PropertyToID("_DebugMode");

            readonly Material m_Material;
            readonly OutlineSettings m_Settings;
            readonly List<ShaderTagId> m_ShaderTagIds = new List<ShaderTagId>
            {
                // 与 PirateOutlinePost.shader 的 Pass 0 "SelectionMask" 的 LightMode 一致
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("LightweightForward"),
            };

            FilteringSettings m_FilteringSettings;
            RTHandle m_Mask;
            RTHandle m_Temp;
            readonly ProfilingSampler m_Sampler = new ProfilingSampler("PirateCrew Outline Post");

            public OutlinePass(Material material, OutlineSettings settings)
            {
                m_Material = material;
                m_Settings = settings;
                renderPassEvent = settings.renderPassEvent;
                // 只画不透明队列的选中对象（单位本体都是不透明）。
                m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque, settings.maskLayer);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;   // mask 不需要深度（Pass 是 ZTest Always / ZWrite Off）
                desc.msaaSamples = 1;       // mask 做点采样的 Sobel，MSAA 反而会引入边缘噪声

                // mask 用 Point（对应 Godot filter_nearest），临时色用 Bilinear（拷回相机颜色）。
                RenderingUtils.ReAllocateIfNeeded(ref m_Mask, desc, FilterMode.Point,
                    TextureWrapMode.Clamp, name: kMaskRtName);
                RenderingUtils.ReAllocateIfNeeded(ref m_Temp, desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: kTempRtName);

                // 只绑颜色目标，不绑相机深度：避免 mask 绘制干扰相机深度缓冲。
                ConfigureTarget(m_Mask);
                ConfigureClear(ClearFlag.Color, Color.clear);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Material == null || m_Mask == null || m_Temp == null)
                    return;

                ref CameraData cameraData = ref renderingData.cameraData;
                RTHandle cameraColor = cameraData.renderer.cameraColorTargetHandle;
                if (cameraColor == null)
                    return;

                // 每帧同步 Inspector 参数，改动即时生效（无需重建 Feature）。
                m_Material.SetColor(s_OutlineColorId, m_Settings.outlineColor);
                m_Material.SetFloat(s_EdgeThresholdId, m_Settings.edgeThreshold);
                m_Material.SetFloat(s_DashLengthId, m_Settings.dashLength);
                m_Material.SetFloat(s_DashGapId, m_Settings.dashGap);
                m_Material.SetFloat(s_DashSpeedId, m_Settings.dashSpeed);
                m_Material.SetFloat(s_DebugModeId, m_Settings.debugMode);

                CommandBuffer cmd = CommandBufferPool.Get();
                // 先把上一步可能残留的命令刷掉，保证 mask 绘制发生在干净的上下文里。
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // ---- 1) 选中对象 -> mask（立即执行；overrideMaterial 的 Pass 0）----
                SortingCriteria sorting = cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawSettings = CreateDrawingSettings(m_ShaderTagIds, ref renderingData, sorting);
                drawSettings.overrideMaterial = m_Material;
                drawSettings.overrideMaterialPassIndex = kMaskPassIndex;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

                // ---- 2) 全屏 Sobel 合成（缓冲到 cmd；Blitter 用 Pass 1）----
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    cmd.SetGlobalTexture(s_SelectionMaskId, m_Mask.nameID);
                    Blitter.BlitCameraTexture(cmd, cameraColor, m_Temp, m_Material, kCompositePassIndex);
                    Blitter.BlitCameraTexture(cmd, m_Temp, cameraColor);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                m_Mask?.Release();
                m_Mask = null;
                m_Temp?.Release();
                m_Temp = null;
            }
        }
    }
}
