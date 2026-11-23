using PirateCrew.Rendering.Pixelart;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.PostLine
{
    /// <summary>
    /// **屏后描边线（PostLine）**的五趟后处理链（规格 docs/技术/渲染/调研/PostLine规格.md §2，
    /// 参照 external/ref/unity-isometric-pixel-pipeline 的 PixelRendererFeature——URP17 Render Graph 版
    /// 移植到本仓 **URP14 旧 API**：<c>Execute(ScriptableRenderContext, ref RenderingData)</c> +
    /// <c>CommandBuffer</c> + <c>Blitter.BlitCameraTexture</c> / <c>cmd.SetRenderTarget</c>，
    /// 无任何 Render Graph 类型）。挂在 PostLine 专用渲染器上，只对 Game/SceneView 的
    /// 非 Overlay、非 Cast 相机生效（<see cref="AddRenderPasses"/>）。
    ///
    /// 【五趟形状】（Pass0 可省，理由见 <see cref="PostLinePass.Execute"/> 注释）
    /// <list type="number">
    ///   <item>Point 降采：全分辨率色 → 画布（点采样取每块一个角纹素，§2.1）；</item>
    ///   <item>Outline：画布上算屏幕空间描边（剪影 + 棱线）→ RGBA 遮罩，深度/法线读全分辨率引擎缓冲；</item>
    ///   <item>Composite：<c>lerp(画布色, 遮罩色, 遮罩 alpha)</c>；</item>
    ///   <item>SharpUpscale：fwidth + smoothstep 锐利放大写回相机色目标（§2.4）。</item>
    /// </list>
    ///
    /// 【为什么用 <c>renderPassEvent = AfterRenderingPostProcessing</c>】对齐参照库：像素化的是
    /// URP 后处理（Tonemap/Bloom/Vignette）**之后**的画面（§2.0）。本仓 URP 后处理本就关着
    /// （HDR 关 + Bloom 关），该事件位不影响正确性，只保证排在 FinalBlit 之前不被裁掉。
    ///
    /// 【URP14 无 Render Graph，故无参照库 §2.0 ⚠ 的全局纹理盲区】Outline 与 Composite 两趟
    /// 在同一个 <see cref="PostLinePass.Execute"/> 里顺序录制进同一条命令缓冲，GPU 按序执行，
    /// Composite 读到的必然是 Outline 刚写完的遮罩——参照库靠 <c>AllowPassCulling(false)</c>
    /// 兜的裁剪问题在这里结构性不存在。
    /// </summary>
    public sealed class PostLineRendererFeature : ScriptableRendererFeature
    {
        /// <summary>序列化设置。默认值 = 参照库资产实测（规格 §5.4 的调优值，非代码默认）。</summary>
        [System.Serializable]
        public class Settings
        {
            [Header("低分辨率画布（仅无 rig 时生效；有 rig 时画布 = 屏幕 ÷ rig.PixelScale）")]
            [Tooltip("一个画布像素占几个屏幕像素。rig 在场时以 rig.PixelScale 为准（两链同一张画布口径），"
                + "本字段只在 PostLine 渲染器挂在无 rig 的独立相机上时兜底。")]
            [Min(1)] public int canvasPixelScale = PixelartCameraRig.PixelScaleDefault;

            [Header("管线材质（留空 = 运行时按 Resources/PixelartShaders 自建，见 MaterialFor）")]
            public Material outlineMaterial;
            public Material compositeMaterial;
            public Material upscaleMaterial;

            [Header("描边——剪影（线色 = 最近邻像素色 × lineDarken）")]
            [Range(0f, 1f)] public float lineDarken = 0.5f;
            [Range(0f, 1f)] public float lineAlpha = 1f;
            [Tooltip("剪影深度跳变阈值（视图空间米）。0.21 = 参照资产实测；阈值域与分辨率无关（§8.1）。")]
            [Range(0f, 1f)] public float zDeltaCutoff = 0.21f;
            [Tooltip("掠射阈值起点。")]
            [Range(0f, 1f)] public float angleZCutoff = 0.3f;
            [Tooltip("掠射面阈值放大倍数。")]
            [Range(0f, 4f)] public float angleZScale = 4f;

            [Header("描边——棱线（线色 = 中心像素色 × (1 + creaseBrighten)）")]
            [Range(0f, 1f)] public float creaseBrighten = 0.5f;
            [Range(0f, 1f)] public float creaseAlpha = 1f;
            [Range(0f, 2f)] public float normalSmoothLow = 0.05f;
            [Range(0f, 2f)] public float normalSmoothHigh = 0.3f;
        }

        public Settings settings = new Settings();

        PostLinePass m_Pass;

        /// <summary>本 feature 自建的材质（Dispose 时销毁；外部赋值的资产材质不动）。</summary>
        readonly System.Collections.Generic.List<Material> m_OwnedMaterials
            = new System.Collections.Generic.List<Material>();

        /// <summary>
        /// 取材质：外部赋值优先（装配器以后可以造 .mat 挂进来）；否则按名从
        /// <see cref="PixelartShaders"/>（Resources 持有）自建隐藏材质——失败返回 null
        /// （PixelartShaders 已响亮报错），链路逐趟降级（见 Pass.Execute）。
        /// </summary>
        Material MaterialFor(Material assigned, string shaderName)
        {
            if (assigned != null)
                return assigned;

            Shader shader = PixelartShaders.Load(shaderName);
            if (shader == null)
                return null;

            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            m_OwnedMaterials.Add(material);
            return material;
        }

        public override void Create()
        {
            if (settings == null)
                settings = new Settings();

            m_Pass = new PostLinePass(
                settings,
                MaterialFor(settings.outlineMaterial, PostLinePath.OutlineShaderName),
                MaterialFor(settings.compositeMaterial, PostLinePath.CompositeShaderName),
                MaterialFor(settings.upscaleMaterial, PostLinePath.SharpUpscaleShaderName));
        }

        /// <summary>
        /// 入队门控（规格 §2.0）：只对 <c>Game</c> / <c>SceneView</c> 相机；Overlay 相机
        /// （含 rig 的透明件叠加相机）画在主相机之后、写同一目标，不能像素化；rig 的 Cast 相机是
        /// 单元着色链的低分辨率代理（targetTexture=ResultBuffer），若本 feature 被误挂到
        /// Cast 渲染器上，这里挡住。
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
                return;

            if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;

            PixelartCameraRig rig = PixelartPath.ActiveRig;
            if (rig != null && rig.CastCamera != null && renderingData.cameraData.camera == rig.CastCamera)
                return;

            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            m_Pass?.CleanupRenderTargets();

            // 只销毁自建的隐藏材质；场景/装配器赋值的资产材质不归本 feature 管。
            foreach (Material material in m_OwnedMaterials)
                CoreUtils.Destroy(material);
            m_OwnedMaterials.Clear();

            m_Pass = null;
        }

        // ==================== 五趟 pass ====================

        sealed class PostLinePass : ScriptableRenderPass
        {
            readonly Settings m_Settings;
            readonly Material m_Outline;
            readonly Material m_Composite;
            readonly Material m_Upscale;

            // 三张画布 RT（规格 §2.0 的 _LowResColor / _LowResOutline / _LowResComposite）。
            // 全分辨率那份快照（_ColorCopy）不需要——理由见 Execute 里 Pass0 的注释。
            RTHandle m_LowResColor;
            RTHandle m_LowResOutline;
            RTHandle m_LowResComposite;

            // （全分辨率终点直接写相机色目标；Auto 中间纹理下与原实现一致。）

            /// <summary>"色目标是后台缓冲"只响一次（逐帧都会命中，不刷屏）。</summary>
            bool m_WarnedBackbuffer;

            public PostLinePass(Settings settings, Material outline, Material composite, Material upscale)
            {
                m_Settings = settings;
                m_Outline = outline;
                m_Composite = composite;
                m_Upscale = upscale;

                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                profilingSampler = new ProfilingSampler("PostLine PixelChain");

                // 【URP14 没有 requiresIntermediateTexture】那是 URP17 的属性（参照 PRF:89）。
                // URP14 的中间颜色纹理由"渲染器 IntermediateTextureMode = Always 且至少挂一个 feature"
                // 保证（UniversalRenderer.cs:593）——本仓全部渲染器资产均为 Always；
                // Execute 入口另有"相机色目标是后台缓冲"的兜底检查，不靠这一条单点。
                //
                // 【只声明 Depth | Normal，不声明 Color】参照声明 Color|Depth|Normal（PRF:92），
                // 但 URP14 里 Color 输入的语义是"要 _CameraOpaqueTexture"：它会额外入队一次
                // 不透明拷贝（UniversalRenderer.cs:844）。本链读的是**后处理之后**的相机色目标，
                // 从不读不透明纹理——声明它只是白付一次全分辨率拷贝。
                // Depth | Normal 触发 DepthNormals 预渲染，保障 _CameraDepthTexture /
                // _CameraNormalsTexture 两张全分辨率引擎缓冲（Outline 的输入，§2.0/§2.2）。
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            /// <summary>画布上三张 RT：尺寸随画布变化自动重建，格式/过滤模式按规格 §2.0 表。</summary>
            void EnsureCanvasTargets(in RenderTextureDescriptor cameraDescriptor, int canvasWidth, int canvasHeight)
            {
                RenderTextureDescriptor canvasDescriptor = cameraDescriptor;
                canvasDescriptor.width = canvasWidth;
                canvasDescriptor.height = canvasHeight;
                canvasDescriptor.msaaSamples = 1;               // 参照同款：链上 RT 一律无 MSAA
                canvasDescriptor.depthBufferBits = (int)DepthBits.None;
                canvasDescriptor.useMipMap = false;

                // 画布色 / 合成色：格式继承相机目标（本仓 HDR 关 → LDR 8bit）；Bilinear 是给
                // SharpUpscale 的 SAMPLE_TEXTURE2D_LOD 用的（参照 §2.0 同注释）。
                RenderingUtils.ReAllocateIfNeeded(ref m_LowResColor, canvasDescriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PostLineLowResColor");
                RenderingUtils.ReAllocateIfNeeded(ref m_LowResComposite, canvasDescriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PostLineLowResComposite");

                // 遮罩：R8G8B8A8_UNorm + Point 是硬要求——Point 防合成时串色，RGBA 的 A 通道载线强度
                // （参照源码注释口径，规格 §2.0 表）。
                RenderTextureDescriptor outlineDescriptor = canvasDescriptor;
                outlineDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                RenderingUtils.ReAllocateIfNeeded(ref m_LowResOutline, outlineDescriptor,
                    FilterMode.Point, TextureWrapMode.Clamp, name: "_PostLineLowResOutline");

            }

            /// <summary>
            /// 每帧全局下发（光族 / 尺寸族）+ 描边参数族推给遮罩材质；出参为画布尺寸。
            /// rig 在场时 rig 是尺寸单源（两链同一张画布）；无 rig 时按相机目标尺寸 ÷ 设置档位
            /// 自推（§8：本仓口径"锁 pixelScale 整数倍"），此时档位 = 屏幕高 ÷ 画布高。
            /// </summary>
            void PushFrameGlobals(CommandBuffer cmd, ref RenderingData renderingData,
                out int canvasWidth, out int canvasHeight)
            {
                Camera camera = renderingData.cameraData.camera;
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                int samplingScale;

                if (rig != null && rig.IsReady && rig.RenderWidth > 1)
                {
                    // rig 维护的画布 = 屏幕 ÷ rig.PixelScale（"锁整数倍"口径，ComputeTargetSize）。
                    canvasWidth = rig.RenderWidth;
                    canvasHeight = rig.RenderHeight;
                    samplingScale = Mathf.Max(1, rig.PixelScale);
                }
                else
                {
                    samplingScale = Mathf.Max(1, m_Settings.canvasPixelScale);
                    RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                    canvasWidth = Mathf.Max(2, descriptor.width / samplingScale);
                    canvasHeight = Mathf.Max(2, descriptor.height / samplingScale);
                }

                // 可视正交高 ÷ 画布高（与 rig.UnitSize 同式）。PostLine 只承诺正交取景；
                // 透视相机下该口径失真，但这只影响下游吸附件的精度，不影响本链正确性。
                float unitSize = camera.orthographicSize * 2f / canvasHeight;

                // 主光：rig 有 sun 用 sun（与单元着色链同一光源真值），否则场景烘焙主光。
                // 方向/颜色换算与 rig.PushLightGlobals 同口径（forward 取反 = 指向光源；
                // 全局色显式 .linear——不赌全局颜色的隐式转换路径）。
                Light light = rig != null && rig.sun != null ? rig.sun : RenderSettings.sun;
                Vector3 lightDir = Vector3.up;
                Color lightColor = Color.white;
                if (light != null)
                {
                    lightDir = -light.transform.forward;
                    float intensity = light.intensity * (rig != null ? rig.lightIntensity : 1f);
                    lightColor = light.color * intensity;
                }
                Color linearColor = lightColor.linear;

                cmd.SetGlobalVector(PostLinePath.LightDirId, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
                cmd.SetGlobalVector(PostLinePath.LightColorId, new Vector4(linearColor.r, linearColor.g, linearColor.b, linearColor.a));

                // 环境光 + 云影：ToonLit 的默认分支直接读这两族全局，缺省时环境 = 0（未受光面全黑）、
                // 云影 = 1（无影）。参照库由 DayNightCycleManager / CloudShadowManager 下发；
                // 本仓固定光照，不搬昼夜系统，由本链每帧兜底（见 PostLineEnvironment 头注）。
                PostLineEnvironment.Push(cmd);

                cmd.SetGlobalFloat(PostLinePath.UnitSizeId, unitSize);
                cmd.SetGlobalFloat(PostLinePath.CanvasWidthId, canvasWidth);
                cmd.SetGlobalFloat(PostLinePath.CanvasHeightId, canvasHeight);
                cmd.SetGlobalFloat(PostLinePath.SamplingScaleId, samplingScale);

                // 描边参数族：Settings 是普通序列化字段、没有变更检测，对齐参照 PRF 每帧 SetFloat。
                // 上采样的亚纹素补偿（PostLinePath.PanOffsetId）不在本链下发——它由相机 snap 侧
                // （规格 §10 的 PostLineCameraSnap，未在本次范围）写；未写时全局为 0 = 不补偿。
                if (m_Outline != null)
                {
                    m_Outline.SetFloat(PostLinePath.LineDarkenId, m_Settings.lineDarken);
                    m_Outline.SetFloat(PostLinePath.LineAlphaId, m_Settings.lineAlpha);
                    m_Outline.SetFloat(PostLinePath.ZDeltaCutoffId, m_Settings.zDeltaCutoff);
                    m_Outline.SetFloat(PostLinePath.AngleZCutoffId, m_Settings.angleZCutoff);
                    m_Outline.SetFloat(PostLinePath.AngleZScaleId, m_Settings.angleZScale);
                    m_Outline.SetFloat(PostLinePath.CreaseBrightenId, m_Settings.creaseBrighten);
                    m_Outline.SetFloat(PostLinePath.CreaseAlphaId, m_Settings.creaseAlpha);
                    m_Outline.SetFloat(PostLinePath.NormalSmoothLowId, m_Settings.normalSmoothLow);
                    m_Outline.SetFloat(PostLinePath.NormalSmoothHighId, m_Settings.normalSmoothHigh);
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // 相机色目标（URP14 的 RTHandle 口；旧名 cameraColorTarget 已弃用）。
                RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (cameraColor == null)
                    return;

                // 兜底：相机色目标若是后台缓冲（渲染器被改成 IntermediateTextureMode = Auto 且没有
                // 别的条件触发中间纹理），它不能当纹理采样——跳过整条链并响一次，宁可不像素化也不画错图。
                if (cameraColor.nameID == (RenderTargetIdentifier)BuiltinRenderTextureType.CameraTarget)
                {
                    if (!m_WarnedBackbuffer)
                    {
                        m_WarnedBackbuffer = true;
                        global::PirateCrew.Core.Log.Warn("[PostLineRendererFeature] 相机 "
                            + renderingData.cameraData.camera.name + " 的色目标是后台缓冲（无中间纹理），"
                            + "本链跳过——请把 PostLine 渲染器的 Intermediate Texture 设为 Always。");
                    }
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    PushFrameGlobals(cmd, ref renderingData, out int canvasWidth, out int canvasHeight);
                    EnsureCanvasTargets(renderingData.cameraData.cameraTargetDescriptor, canvasWidth, canvasHeight);

                    // ---- Pass0 CopyColor：**可省**（参照库的 _ColorCopy 快照没有移植）。
                    // 参照在 Render Graph 下先把相机色目标拷一份再消费；URP14 无 Render Graph，
                    // 而本仓渲染器 m_IntermediateTextureMode = Always ⇒ 相机色目标恒是中间纹理
                    // （不会直接写后台缓冲），本命令缓冲内 Pass1/Pass2 先读它、Pass4 最后写它，
                    // GPU 顺序执行无读写冲突——省掉一次全分辨率拷贝。

                    // ---- Pass1 降采（规格 §2.1）。源的选择分两条：
                    //   · 相机有 targetTexture（出图/离屏）：final blit 之后 RT 里就是最终场景，
                    //     直接读 RT。URP14 在本仓的 Always 中间纹理下，feature 里的
                    //     cameraColorTargetHandle 与 final blit 读的附件不是同一张（实拍：
                    //     读回全黑/天空），这是唯一可靠的源。
                    //   · 后台缓冲（游戏内）：不能读 backbuffer，仍走 cameraColor handle。
                    // 降采用 bilinear 是出图路径的妥协（点采样在 RT 直读下要另写 CoreBlit 配方）；
                    // 2:1 下与点采样差异只在块角/块心，描边仍在低分辨率域。
                    Blitter.BlitCameraTexture(cmd, cameraColor, m_LowResColor, 0f, false);

                    // ---- Pass2 Outline（规格 §2.2）：画布上算剪影 + 棱线 → RGBA 遮罩。
                    if (m_Outline != null)
                    {
                        Blitter.BlitCameraTexture(cmd, m_LowResColor, m_LowResOutline, m_Outline, 0);
                    }
                    else
                    {
                        // 降级（参照 §2.0）：遮罩材质缺失 → 无线直通，合成仍可拿纯像素化画面。
                        Blitter.BlitCameraTexture(cmd, m_LowResColor, m_LowResOutline, 0f, false);
                    }
                    // Pass3 的采样源：全局注入（URP14 等价于参照 SetGlobalTextureAfterPass；
                    // 两趟同缓冲顺序录制，读必然晚于写）。
                    cmd.SetGlobalTexture(PostLinePath.OutlineTextureId, m_LowResOutline);

                    // ---- Pass3 Composite（规格 §2.3）。必须在 Pass2 之后同一 Execute 内录制
                    // （§2.0 ⚠ 的 URP14 等价约束——结构性满足）。
                    if (m_Composite != null)
                    {
                        Blitter.BlitCameraTexture(cmd, m_LowResColor, m_LowResComposite, m_Composite, 0);
                    }
                    else
                    {
                        Blitter.BlitCameraTexture(cmd, m_LowResColor, m_LowResComposite, 0f, false);
                    }

                    // ---- Pass4 SharpUpscale（规格 §2.4）：画布合成结果锐利放大写回相机色目标。
                    // 材质缺失时退化为普通 blit（参照同款降级）。
                    if (m_Upscale != null)
                    {
                        Blitter.BlitCameraTexture(cmd, m_LowResComposite, cameraColor, m_Upscale, 0);
                    }
                    else
                    {
                        Blitter.BlitCameraTexture(cmd, m_LowResComposite, cameraColor, 0f, false);
                    }
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            /// <summary>画布 RT 释放（feature Dispose 调；RTHandle 自管生命周期）。</summary>
            public void CleanupRenderTargets()
            {
                if (m_LowResColor != null) { m_LowResColor.Release(); m_LowResColor = null; }
                if (m_LowResOutline != null) { m_LowResOutline.Release(); m_LowResOutline = null; }
                if (m_LowResComposite != null) { m_LowResComposite.Release(); m_LowResComposite = null; }
            }
        }
    }
}
