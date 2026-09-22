using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **帧级调色板（P5）**（Cast 渲染器，<see cref="RenderPassEvent.AfterRendering"/>，**最后一趟**）：
    /// 把已经合成好的画面逐像素换成调色板上最近的那个色（LUT 点采样，见
    /// <c>PixelartColorCorrection.shader</c> 的采样公式与 <c>PaletteGenerationCIEDE.compute</c> 的布局）。
    ///
    /// 【为什么它是最后一趟】调色板是"整幅图只许出现板上的颜色"这条约束的落点：
    /// 放在合成之后 ⇒ 它看着的就是最终画面；放在合成之前则会被随后的加法（高光/边缘光/环境光）
    /// 混出板外的颜色。契约 §3 把它的次序写死为第 7 位。
    ///
    /// 【可回退】<see cref="palette"/> 为空（或调色板里没有色 / 没有 compute）⇒ **整趟跳过**，
    /// 画面就是没映射的结果。这是本轮"每个子件能单独关"的一条（契约 §7）。
    ///
    /// 【为什么先拷一张临时 RT】源与目标都是 Cast 相机的颜色目标：同一个资源同时当 SRV 与 RTV
    /// 在 D3D11 上是**非法绑定**（表现是读到零/脏值，且不报错）。故：
    /// ① 颜色目标 → 临时 RT（CoreBlit Nearest，逐像素拷贝、不做任何颜色操作）；
    /// ② 临时 RT --调色板材质--> 颜色目标（就地映射）。
    /// 临时 RT 走 <c>cmd.GetTemporaryRT/ReleaseTemporaryRT</c>——它的生命周期挂在命令缓冲上，
    /// 比 C# 侧 <c>RenderTexture.GetTemporary/ReleaseTemporary</c> 少一类"cmd 还没执行就归还"的时序坑。
    /// </summary>
    public sealed class PixelartColorCorrectionFeature : ScriptableRendererFeature
    {
        /// <summary>调色板映射 shader 的**序列化资产引用**（装配器写入；理由见其它 Feature 的同类注释：
        /// 只在 C# 里 Shader.Find 的 shader 会被播放器构建剥离）。</summary>
        public Shader colorCorrectionShader;

        /// <summary>帧级调色板资产（装配器写入）。**为空 = 本趟整趟跳过**（不映射，可回退）。</summary>
        public PixelartPalette palette;

        sealed class Pass : ScriptableRenderPass
        {
            /// <summary>临时拷贝 RT 的名字（同一个 cmd 里 Get/Release 配对，用名字 ID 而不是 Shader 全局）。</summary>
            static readonly int kTempTargetId = Shader.PropertyToID("_PixelartColorCorrectionTemp");

            readonly ProfilingSampler m_Sampler = new ProfilingSampler("Pixelart Color Correction");
            readonly Material m_Material;
            readonly Material m_CopyMaterial;
            RenderTexture m_Lut;
            int m_ColorCount;
            bool m_Logged;
            bool m_LoggedSkip;

            public Pass(Material material, Material copyMaterial)
            {
                renderPassEvent = RenderPassEvent.AfterRendering;   // 最后一趟（契约 §3 第 7 项）
                m_Material = material;
                m_CopyMaterial = copyMaterial;
            }

            /// <summary>
            /// 本帧要用的 LUT（由 <c>SetupRenderPasses</c> 烘好传进来；null = 跳过本趟）。
            /// 【色数为什么要一起传】日志里要打出来，而 `Pass` 是嵌套类、拿不到外层 Feature 的实例字段
            /// （裸写 `palette` 是编译错 CS0120），所以只把用得到的数字带进来。
            /// </summary>
            public void SetLut(RenderTexture lut, int colorCount)
            {
                m_Lut = lut;
                m_ColorCount = colorCount;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // 【自守卫】没有装配（编辑器里没进播放、或 rig 没就绪）就什么都不做。
                PixelartCameraRig rig = PixelartPath.ActiveRig;
                if (rig == null || !rig.IsReady || m_Material == null || m_CopyMaterial == null)
                    return;

                // 只对 Cast 相机生效：主相机（上屏器）不该再映射一遍。
                if (renderingData.cameraData.camera != rig.CastCamera)
                    return;

                if (m_Lut == null)
                {
                    if (!m_LoggedSkip)
                    {
                        m_LoggedSkip = true;
                        Debug.LogWarning("[PixelartColorCorrectionFeature] 调色板为空/不可用——"
                            + "本趟整趟跳过（画面为未映射的结果）。检查 "
                            + PixelartPath.PaletteAssetPath + " 的 colors 与 bakeComputeShader。");
                    }
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_Sampler))
                {
                    if (!m_Logged)
                    {
                        m_Logged = true;
                        Debug.Log("[PixelartColorCorrectionFeature] 调色板映射已在 "
                            + renderingData.cameraData.camera.name + " 上执行（LUT "
                            + m_Lut.width + "×" + m_Lut.height + "，色数 "
                            + m_ColorCount + "）。");
                    }

                    cmd.SetGlobalTexture(PixelartPath.PaletteLutId, m_Lut);

                    // 临时 RT 与结果缓冲同尺寸同格式（都是艺术画布、ARGB32、非 sRGB）。
                    var desc = new RenderTextureDescriptor(rig.RenderWidth, rig.RenderHeight,
                        RenderTextureFormat.ARGB32, 0)
                    {
                        sRGB = false,
                        useMipMap = false,
                        autoGenerateMips = false,
                        msaaSamples = 1,
                    };
                    cmd.GetTemporaryRT(kTempTargetId, desc, FilterMode.Point);

                    var colorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

                    // ① 拷贝（Nearest = pass 0：逐像素、不做颜色操作）
                    Blitter.BlitTexture(cmd, colorTarget, kTempTargetId, m_CopyMaterial,
                        PixelartPath.CoreBlitNearestPass);
                    // ② 映射回颜色目标（`_BlitTexture` 由 Blitter 绑到临时 RT）
                    Blitter.BlitTexture(cmd, kTempTargetId, colorTarget, m_Material, 0);

                    cmd.ReleaseTemporaryRT(kTempTargetId);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        Pass m_Pass;
        Material m_Material;
        Material m_CopyMaterial;
        bool m_LoggedNoPalette;

        public override void Create()
        {
            Shader shader = colorCorrectionShader != null
                ? colorCorrectionShader
                : Shader.Find(PixelartPath.ColorCorrectionShaderName);
            if (shader == null)
            {
                Debug.LogError("[PixelartColorCorrectionFeature] 找不到 shader \""
                    + PixelartPath.ColorCorrectionShaderName
                    + "\"（资产引用为空、编译失败或改过名？）。帧级调色板不会生效——"
                    + "重跑装配器 PirateCrew/Pixelart/装配像素化路径渲染器可重新写入引用。");
                m_Material = null;
            }
            else
            {
                m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            // 拷贝那一趟借 URP 自带的 CoreBlit（与上屏 blit 同一个 shader，构建必然保活）。
            m_CopyMaterial = PixelartPath.CreateCoreBlitMaterial();
            if (m_CopyMaterial == null)
                Debug.LogError("[PixelartColorCorrectionFeature] 找不到 shader \""
                    + PixelartPath.CoreBlitShaderName + "\"——调色板那一趟会被跳过。");

            m_Pass = new Pass(m_Material, m_CopyMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 【整趟跳过的第一道门】调色板资产为空 ⇒ **连 pass 都不入队**（真正的"整趟跳过"）。
            // 第二道门在 Execute：资产在、但里面没有色或没有 compute ⇒ BuildLut 返回 null，照样跳过。
            if (PixelartPath.ActiveRig == null || m_Material == null)
                return;

            if (palette == null)
            {
                if (!m_LoggedNoPalette)
                {
                    m_LoggedNoPalette = true;
                    Debug.Log("[PixelartColorCorrectionFeature] 没有调色板资产——帧级调色板整趟跳过"
                        + "（画面 = 未映射的合成结果，可回退）。期望路径：" + PixelartPath.PaletteAssetPath);
                }
                return;
            }

            renderer.EnqueuePass(m_Pass);
        }

        /// <summary>
        /// 每帧把 LUT 备好（**惰性**：调色板没变就是取缓存，一行 Dispatch 都不发）。
        /// 【为什么在这里烘而不是 Execute 里】此刻还没有任何渲染目标被绑到立即上下文上，
        /// 一次同步 Dispatch 不会与"某个正在当 RTV 的 RT"打架；而且烘失败（返回 null）
        /// 能在 Execute 之前就被跳过，避免记录一堆无用的命令。
        /// </summary>
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            PixelartCameraRig rig = PixelartPath.ActiveRig;
            if (rig == null || !rig.IsReady || renderingData.cameraData.camera != rig.CastCamera)
                return;

            m_Pass.SetLut(palette != null ? palette.BuildLut() : null,
                palette != null ? palette.ColorCount : 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            CoreUtils.Destroy(m_Material);
            m_Material = null;
            CoreUtils.Destroy(m_CopyMaterial);
            m_CopyMaterial = null;
        }
    }
}
