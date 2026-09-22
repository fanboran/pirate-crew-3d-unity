using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化着色路径**的共享常量与运行期注册表。
    ///
    /// 【这条路径的形状】按 v3 蓝本（docs/技术/渲染/蓝图-新渲染管线-v3蓝本.md）与
    /// P4/P5 接口契约（docs/技术/渲染/像素化着色路径-P4P5接口契约.md）：
    ///   ① **几何 + 7 张 G-buffer + 深度画在「屏幕档」**（= 艺术画布 × pixelScale，
    ///      也就是常规延迟渲染那种全分辨率 G-buffer）——物体 pass 不着色，只写数据；
    ///   ② 连通域判据也在屏幕档跑（它要"一个艺术像素内部的细粒度深度/法线"）；
    ///   ③ **描边、四趟着色、合成、帧级调色板全部在「艺术画布」**（= 屏幕 ÷ pixelScale）；
    ///   ④ 最后一次点采样放大上屏。
    /// 与"全分辨率前向着色 + 事后降采"（旧链）的分野不在用哪个 shader，
    /// 而在**着色发生在哪一档分辨率**。本路径独立于旧链（`PirateToon` / `PixelationRendererFeature`），
    /// 不引用它们任何一处实现。
    ///
    /// 【为什么常量集中在这里】shader 全局名、pass 名、资产路径都是字符串契约（拼错即静默失效），
    /// 同 EventBus 事件名的纪律：**只有这一处写字面量**，其余全部走常量。
    /// </summary>
    public static class PixelartPath
    {
        // ==================== shader ====================

        /// <summary>物体 pass 的 shader（只写 7 张 G-buffer，不做着色）。</summary>
        public const string ObjectShaderName = "PirateCrew/Pixelart/PixelartObject";

        /// <summary>低分辨率域着色 shader（四趟：Diffuse / Specular / GI / Combine）。</summary>
        public const string ShadingShaderName = "PirateCrew/Pixelart/PixelartShading";

        /// <summary>描边 shader（v3 口径的 4 邻域膨胀，跑在艺术画布上）。</summary>
        public const string OutlineShaderName = "PirateCrew/Pixelart/PixelartOutline";

        /// <summary>边缘光累加 shader。</summary>
        public const string RimLightShaderName = "PirateCrew/Pixelart/PixelartRimLight";

        /// <summary>帧级调色板映射 shader。</summary>
        public const string ColorCorrectionShaderName = "PirateCrew/Pixelart/PixelartColorCorrection";

        /// <summary>上屏 blit 用 URP 自带 shader（播放器构建必然保活）。</summary>
        public const string CoreBlitShaderName = "Hidden/Universal/CoreBlit";

        /// <summary>CoreBlit 的 Nearest pass（点采样放大——像素化的最后一步）。</summary>
        public const int CoreBlitNearestPass = 0;

        /// <summary>
        /// 主相机的「上屏器掩码保留位」（第 30 层，项目层表未占用）。像素化上屏器把主相机
        /// cullingMask 收缩到只剩这一位——该层没有任何可渲染物，主相机照旧画不到世界物体。
        ///
        /// 【历史】Cinemachine 时代它是**虚机保活层**：Brain 按输出相机的掩码筛选候选虚机
        /// （<c>mask &amp; (1 &lt;&lt; go.layer)</c>），掩码全 0 会让 Brain 永远选不到相机
        /// ⇒ 旋转/缩放/镜头全部失效、画面钉死在烘焙机位（r13 实机事故，c42dfdc）。
        /// 2026-09-23 相机去 Cinemachine 化后虚机不存在了，但 rig 的掩码写入逻辑保持不动
        /// （保留位无副作用），本常量即该写入逻辑的唯一事实源。
        /// </summary>
        public const int VirtualCameraKeepAliveLayer = 30;

        // ==================== pass 名（一律按名字解析，不写死序号）====================

        /// <summary>
        /// 物体 pass 的 ShaderTagId。**只有这一条 LightMode 会被画**——URP 的标准不透明 pass
        /// （SRPDefaultUnlit / UniversalForward 那一族）在此材质上匹配不到东西，
        /// 于是同一台相机不会被画两遍（v3 用同一手法）。这也是全仓唯一需要独立 LightMode 的 pass
        /// （AGENTS.md 铁律：额外 pass 必须给它一个独立且未被占用的 LightMode）。
        /// </summary>
        public const string OpaqueShaderTagName = "PixelartOpaque";

        /// <summary>描边 pass 名。</summary>
        public const string OutlinePassName = "PixelartOutline";

        /// <summary>漫反射 pass 名。</summary>
        public const string DiffusePassName = "PixelartDiffuse";

        /// <summary>高光 pass 名。</summary>
        public const string SpecularPassName = "PixelartSpecular";

        /// <summary>环境光（GI）pass 名。</summary>
        public const string GIPassName = "PixelartGI";

        /// <summary>合成 pass 名（四张相加 → Cast 相机颜色目标）。</summary>
        public const string CombinePassName = "PixelartCombine";

        /// <summary>边缘光累加 pass 名。</summary>
        public const string RimLightPassName = "PixelartRimLight";

        /// <summary>帧级调色板映射 pass 名。</summary>
        public const string ColorCorrectionPassName = "PixelartColorCorrection";

        // ==================== 资产路径（装配器用；资产名也必须集中）====================

        /// <summary>shader 资产目录。</summary>
        public const string ShaderFolder = "Assets/Pixelart/Shaders";

        /// <summary>连通域 compute 资产目录。</summary>
        public const string ConnectivityComputeFolder = "Assets/Pixelart/Compute/Connectivity";

        /// <summary>其余 compute 资产目录。</summary>
        public const string ComputeFolder = "Assets/Pixelart/Compute";

        /// <summary>调色板资产路径（装配器按场景材质色生成并写在这里）。</summary>
        public const string PaletteAssetPath = "Assets/Pixelart/Palette/Palette.asset";

        /// <summary>渲染器资产所在目录（装配器创建，本路径专用）。</summary>
        public const string RendererFolder = "Assets/Settings/URP";

        /// <summary>Cast 渲染器资产名（物体 pass + 连通域 + 描边 + 边缘光 + 着色 + 调色板）。</summary>
        public const string CastRendererName = "PixelartCast_Renderer";

        /// <summary>Screen 渲染器资产名（只有一拍上屏 blit）。</summary>
        public const string ScreenRendererName = "PixelartScreen_Renderer";

        /// <summary>
        /// 透明件叠加渲染器的资产名（第三个追加进 URP 资产的渲染器）。
        /// **它不带本路径任何一趟**：是标准 URP 渲染，只把"画什么队列"过滤成"只画 Transparent"，
        /// 让 FX / 危险虚线 / 接触阴影 / 弹道预览这些半透明内容不被像素化域漏掉
        /// （像素化域是数据缓冲 + 全屏着色，放不下混合几何）。设计与代价见安装器的 <c>EnsureOverlayRenderer</c>。
        /// </summary>
        public const string OverlayRendererName = "PixelartOverlay_Renderer";

        // ==================== 纹理全局 ====================

        /// <summary>屏幕档 G-buffer：亮部色（rgb）+ 覆盖标记（a=1 有几何）。</summary>
        public static readonly int AlbedoBufferId = Shader.PropertyToID("_PixelartAlbedoBuffer");

        /// <summary>屏幕档 G-buffer：**屏幕空间几何法线**（ddy/ddx of positionWS）——连通域判据的输入。</summary>
        public static readonly int Normal0BufferId = Shader.PropertyToID("_PixelartNormal0Buffer");

        /// <summary>屏幕档 G-buffer：切线空间法线贴图后的世界法线——**着色**用的法线。</summary>
        public static readonly int Normal1BufferId = Shader.PropertyToID("_PixelartNormal1Buffer");

        /// <summary>屏幕档 G-buffer：r=光滑度 g=金属度。</summary>
        public static readonly int PhysicalBufferId = Shader.PropertyToID("_PixelartPhysicalBuffer");

        /// <summary>屏幕档 G-buffer：r=优先级 g=未用 b=法线边阈值 a=AA 缩放。</summary>
        public static readonly int ShapeBufferId = Shader.PropertyToID("_PixelartShapeBuffer");

        /// <summary>屏幕档 G-buffer：r=主光档数 g=抖动标量 b=边光档数 a=applyOutline。</summary>
        public static readonly int PaletteBufferId = Shader.PropertyToID("_PixelartPaletteBuffer");

        /// <summary>屏幕档 G-buffer：逐物体边缘光色（rgb）。</summary>
        public static readonly int RimLightPropertyBufferId = Shader.PropertyToID("_PixelartRimLightPropertyBuffer");

        /// <summary>屏幕档深度（可采样；着色里用来重建 positionWS，阴影坐标与附加光都要它）。</summary>
        public static readonly int DepthBufferId = Shader.PropertyToID("_PixelartDepthBuffer");

        /// <summary>连通域中间缓冲（屏幕档，bit 打包的逐方向连通性）。</summary>
        public static readonly int ConnectivityDetailId = Shader.PropertyToID("_PixelartConnectivityDetailBuffer");

        /// <summary>
        /// 连通域 Flood 的 ping-pong 备份（屏幕档）。**只作为 kernel 纹理绑定，不发布成全局量**——
        /// 它没有任何 shader 阶段按 uv 采样它。
        /// </summary>
        public static readonly int ConnectivityPrevId = Shader.PropertyToID("_PixelartConnectivityPrevBuffer");

        /// <summary>连通域结论（艺术画布）。</summary>
        public static readonly int ConnectivityResultId = Shader.PropertyToID("_PixelartConnectivityResultBuffer");

        /// <summary>墨线标记（艺术画布；r=1 表示本像素是墨线）。</summary>
        public static readonly int OutlineBufferId = Shader.PropertyToID("_PixelartOutlineBuffer");

        /// <summary>漫反射结果（艺术画布）。</summary>
        public static readonly int DiffuseBufferId = Shader.PropertyToID("_PixelartDiffuseBuffer");

        /// <summary>高光结果（艺术画布）。</summary>
        public static readonly int SpecularBufferId = Shader.PropertyToID("_PixelartSpecularBuffer");

        /// <summary>环境光（GI）结果（艺术画布）。</summary>
        public static readonly int GIBufferId = Shader.PropertyToID("_PixelartGIBuffer");

        /// <summary>边缘光累加结果（艺术画布）。</summary>
        public static readonly int RimLightBufferId = Shader.PropertyToID("_PixelartRimLightBuffer");

        /// <summary>帧级调色板 LUT（2D RGB 条带）。</summary>
        public static readonly int PaletteLutId = Shader.PropertyToID("_PixelartPaletteLut");

        // ==================== 标量 / 向量全局 ====================

        /// <summary>主光方向（世界空间，**指向光源**；着色用 <c>saturate(dot(L, N))</c>）。</summary>
        public static readonly int LightDirId = Shader.PropertyToID("_PixelartLightDirWS");

        /// <summary>主光颜色（线性）。</summary>
        public static readonly int LightColorId = Shader.PropertyToID("_PixelartLightColor");

        /// <summary>环境光色（线性）——**暗部色就是这一项**（暗面 = albedo × 环境色）。</summary>
        public static readonly int AmbientColorId = Shader.PropertyToID("_PixelartAmbientColor");

        /// <summary>墨线颜色（线性）。</summary>
        public static readonly int InkColorId = Shader.PropertyToID("_PixelartInkColor");

        /// <summary>1 **艺术**像素的世界尺寸（= 2×正交size ÷ 艺术画布高）。物体级吸附也用它。</summary>
        public static readonly int UnitSizeId = Shader.PropertyToID("_PixelartUnitSize");

        /// <summary>1 **细**像素（屏幕档）的世界尺寸。</summary>
        public static readonly int FineUnitSizeId = Shader.PropertyToID("_PixelartFineUnitSize");

        /// <summary>艺术画布宽（像素）。</summary>
        public static readonly int RTWidthId = Shader.PropertyToID("_PixelartRTWidth");

        /// <summary>艺术画布高（像素）。</summary>
        public static readonly int RTHeightId = Shader.PropertyToID("_PixelartRTHeight");

        /// <summary>屏幕档宽（像素）。</summary>
        public static readonly int FineWidthId = Shader.PropertyToID("_PixelartFineWidth");

        /// <summary>屏幕档高（像素）。</summary>
        public static readonly int FineHeightId = Shader.PropertyToID("_PixelartFineHeight");

        /// <summary>一个艺术像素占几个细像素（= pixelScale；连通域判据的采样尺度）。</summary>
        public static readonly int SamplingScaleId = Shader.PropertyToID("_PixelartSamplingScale");

        /// <summary>连通域降档阈值（= aaScaler ÷ 2，v3 同口径）。</summary>
        public static readonly int AAThresholdId = Shader.PropertyToID("_PixelartAAThreshold");

        /// <summary>附加光条数（v3 的 <c>_AdditionalLightCount</c>；附加光循环要用）。</summary>
        public static readonly int AdditionalLightCountId = Shader.PropertyToID("_PixelartAdditionalLightCount");

        /// <summary>上屏 UV 补偿量（xy）——相机 snap 的第二步。</summary>
        public static readonly int SnapOffsetUvId = Shader.PropertyToID("_PixelartSnapOffsetUv");

        /// <summary>
        /// 调试档（0 = 正常出图，1 = albedo，2 = 法线，3 = 逐物体参数，4 = 覆盖标记）。
        /// 拆管线出图用：故障是"没进 G-buffer"还是"着色丢了"一眼可判。
        /// </summary>
        public static readonly int DebugModeId = Shader.PropertyToID("_PixelartDebugMode");

        // ==================== 运行期注册表 ====================

        /// <summary>
        /// 当前活跃的相机装配（同一时刻只允许一个：本路径是单场景单相机的试点形状，
        /// 多装配并存会互相覆盖 shader 全局——故后启用者接管、先启用者让位并在 OnDisable 时不回写）。
        /// </summary>
        public static PixelartCameraRig ActiveRig { get; internal set; }

        /// <summary>解析 CoreBlit shader 并造一个隐藏材质；失败返回 null（调用方负责报错）。</summary>
        public static Material CreateCoreBlitMaterial()
        {
            Shader shader = Shader.Find(CoreBlitShaderName);
            if (shader == null)
                return null;

            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }
}
