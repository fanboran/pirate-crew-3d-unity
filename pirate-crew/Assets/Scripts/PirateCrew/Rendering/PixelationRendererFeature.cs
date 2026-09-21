using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// 等距像素卡通的**全屏像素化** RendererFeature（docs/技术/渲染管线-等距像素卡通.md §3；
    /// 方向裁决见 docs/设计/美术风格指南.md §1.2——创始人 2026-09-21）。
    ///
    /// 【工作流程】<c>AfterRenderingPostProcessing</c> 时机抓相机颜色：
    ///   屏幕分辨率颜色 → nearest blit 到低分辨率 Point RT（高 360，宽随屏幕宽高比）
    ///   → nearest blit 放大回屏幕。两步全 Point，块感才干净。
    ///
    /// 【为什么用官方 CoreBlit 而不自写 blit shader】blit 材质是 URP 自带的
    ///   <c>Hidden/Universal/CoreBlit</c> 的 **pass 0 "Nearest"**（Shaders/Utils/CoreBlit.shader，
    ///   片元 FragNearest 内部 <c>sampler_PointClamp</c>）。首版自写 Hidden/PirateCrew/PixelBlit
    ///   在播放器真实 GPU 会话报 "not supported on this GPU"（编辑器/构建期都不暴露，
    ///   与 §4.4 血泪教训同族——已删）。CoreBlit 被 URP 运行时（Blitter）自身引用，
    ///   播放器构建必然保活，无 r2 式剥离风险。
    ///
    /// 【为什么不用 URP Render Scale】放大是线性过滤，会糊；两条红线见渲染篇 §1：
    ///   ① 描边必须在像素化之前（本 Feature 挂 <c>AfterRenderingPostProcessing</c>，
    ///     天然排在描边等一切 3D 内容之后）；
    ///   ② UI 必须在像素化之后——UI 走 ScreenSpaceOverlay Canvas，由引擎在相机渲染
    ///     之后叠加，不经过本 pass（Battle/M3 场景的 Canvas 均为 Overlay）。
    /// </summary>
    [DisallowMultipleRendererFeature("PirateCrew Pixelation")]
    public class PixelationRendererFeature : ScriptableRendererFeature
    {
        /// <summary>URP 自带 blit shader（pass 0 = Nearest 点采样）。</summary>
        public const string CoreBlitShaderName = "Hidden/Universal/CoreBlit";

        /// <summary>CoreBlit 的 Nearest pass 索引（Shaders/Utils/CoreBlit.shader "// 0: Nearest"）。</summary>
        const int kNearestPassIndex = 0;

        [System.Serializable]
        public class PixelationSettings
        {
            [Tooltip("blit 材质的 shader。默认/留空 = URP 自带 Hidden/Universal/CoreBlit（pass 0 Nearest）。")]
            public Shader blitShader;

            [Tooltip("本 Pass 在管线中的插入时机。像素化必须在 3D 与后处理全部完成之后（渲染篇 §1 顺序红线）。")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

            [Tooltip("低分辨率 RT 的高度（像素）。360 = 1080p 的整数四分之一（16:9 下即 640×360，美术指南待定项 #2 起步档）；宽度按屏幕宽高比自适应。")]
            [Min(64)] public int renderHeightPixels = 360;
        }

        public PixelationSettings settings = new PixelationSettings();

        Material m_Material;
        PixelationPass m_Pass;
        bool m_ShaderMissingLogged;

        /// <summary>供外部（采集脚本/诊断）查询本 Feature 是否就绪。</summary>
        public bool IsReady => m_Material != null && m_Pass != null;

        public override void Create()
        {
            Shader shader = settings.blitShader != null
                ? settings.blitShader
                : Shader.Find(CoreBlitShaderName);

            if (shader == null)
            {
                m_Material = null;
                m_ShaderMissingLogged = false;
                m_Pass = new PixelationPass(null, settings);
                return;
            }

            m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            m_Pass = new PixelationPass(m_Material, settings);
            m_Pass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Material == null)
            {
                // 明确报错而不是静默不生效（同 OutlineRendererFeature 的纪律）。
                if (!m_ShaderMissingLogged)
                {
                    m_ShaderMissingLogged = true;
                    Debug.LogError("[PixelationRendererFeature] 未找到 shader \"" + CoreBlitShaderName
                        + "\"——URP 包异常或被改名，请核对 com.unity.render-pipelines.universal 版本。");
                }
                return;
            }

            // 编辑器工作视图保持清晰可辨：SceneView/预览/反射不像素化；游戏相机照常。
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection
                || type == CameraType.SceneView
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
        // 内部 Pass：两步 nearest blit（屏幕 → 低RT → 屏幕）
        // ================================================================
        private class PixelationPass : ScriptableRenderPass
        {
            const string kLowResRtName = "_PiratePixelRt";

            readonly Material m_Material;
            readonly PixelationSettings m_Settings;
            RTHandle m_LowRes;
            readonly ProfilingSampler m_Sampler = new ProfilingSampler("PirateCrew Pixelation");

            public PixelationPass(Material material, PixelationSettings settings)
            {
                m_Material = material;
                m_Settings = settings;
                renderPassEvent = settings.renderPassEvent;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;

                // RT 高锁定 settings 档（360），宽随相机宽高比（16:9 → 640×360），
                // 偶数对齐避免半像素列。depth/MSAA 全关——本 pass 只搬颜色。
                int height = m_Settings.renderHeightPixels;
                int width = Mathf.CeilToInt(height * (desc.width / (float)desc.height) * 0.5f) * 2;
                desc.width = width;
                desc.height = height;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                // 下发实际 RT 尺寸给 Toon 全局 uniform（PirateToon 描边线宽公式 2/RT高 与
                // Bayer 块对齐都要它，渲染篇 §5/红线 4）。放在这里而不是每帧 Execute：
                // 尺寸只在相机 setup 时变化（分辨率切换/进入 Game 视图）。
                Shader.SetGlobalFloat(ToonShaderGlobals.PixelRTHeight, height);
                Shader.SetGlobalFloat(ToonShaderGlobals.PixelRTWidth, width);

                // FilterMode.Point 与 shader 的 sampler_PointClamp 双保险（渲染篇三大坑 #1：灰边）。
                RenderingUtils.ReAllocateIfNeeded(ref m_LowRes, desc, FilterMode.Point,
                    TextureWrapMode.Clamp, name: kLowResRtName);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Material == null || m_LowRes == null)
                    return;

                ref CameraData cameraData = ref renderingData.cameraData;
                RTHandle cameraColor = cameraData.renderer.cameraColorTargetHandle;
                if (cameraColor == null)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    // 两步全走 CoreBlit 的 Nearest pass（pass 0，点采样）：
                    Blitter.BlitCameraTexture(cmd, cameraColor, m_LowRes, m_Material, kNearestPassIndex);
                    Blitter.BlitCameraTexture(cmd, m_LowRes, cameraColor, m_Material, kNearestPassIndex);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                m_LowRes?.Release();
                m_LowRes = null;
            }
        }
    }
}
