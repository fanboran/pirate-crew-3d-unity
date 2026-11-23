using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Rendering.PostLine
{
    /// <summary>
    /// **屏后描边线（PostLine）路径**的共享常量。
    ///
    /// 【这条路径的形状】按 docs/技术/渲染/调研/PostLine规格.md §1/§2：几何与着色都在全分辨率
    /// 正常跑（标准 URP 前向，物体 pass 不改），后处理链把颜色**点采样**降到低分辨率画布
    /// （= 屏幕 ÷ 档位），在这一档上做屏幕空间描边（剪影 = 最近邻色压暗、棱线 = 方向对比度提亮），
    /// 合成后用 fwidth + smoothstep 锐利放大上屏。描边在画布上算，线宽恒等于 1 个画布像素。
    /// 与单元着色（CellShade，<see cref="PixelartPath"/>）的分野：那条路把**着色**搬进低分辨率域，
    /// 这条路只把**像素化**放在后处理链上。
    ///
    /// 【为什么常量集中在这里】与 <see cref="PixelartPath"/> 同一纪律：shader 名与属性名都是字符串
    /// 契约（拼错即静默失效），**只有这一处写字面量**。
    ///
    /// 【为什么光 / 尺寸 / 平移补偿族是别名而不是新名字】这些量两条链语义完全相同
    /// （主光、画布尺寸、1 画布像素的世界尺寸、上采样 UV 补偿），各起一套名字就会出现
    /// "同一个物理量两个全局"——一边推了另一边没推、shader 读错一套都不报错。
    /// 所以直接复用 <see cref="PixelartPath"/> 的 ID：同一全局只有一个名字、一个字面量
    /// （规格 §10："<c>_PixelPanOffset</c> 接 <c>PixelartPath.SnapOffsetUvId</c> 已有名，避免双全局"）。
    /// rig 在场时它每帧已推过一遍，本链 feature 再推一遍是幂等的；无 rig 时由 feature 兜底。
    /// </summary>
    public static class PostLinePath
    {
        // ==================== shader（Resources/PixelartShaders/ 持有，文件名 = 内部名末段）====================

        /// <summary>描边遮罩 shader（Pass2：剪影 + 棱线 → RGBA 遮罩，RGB = 线色，A = 强度）。</summary>
        public const string OutlineShaderName = "Hidden/PirateCrew/PostLineOutline";

        /// <summary>合成 shader（Pass3：<c>lerp(画布色, 遮罩色, 遮罩 alpha)</c>）。</summary>
        public const string CompositeShaderName = "Hidden/PirateCrew/PostLineComposite";

        /// <summary>锐利放大 shader（Pass4：fwidth + smoothstep 的"数学上平滑的锐利边"）。</summary>
        public const string SharpUpscaleShaderName = "Hidden/PirateCrew/PostLineSharpUpscale";

        /// <summary>
        /// 物体全分辨率着色 shader（PostLine 线的物体材质用它；CellShade 用 PixelartObject 的
        /// G-buffer pass）。切换由 <see cref="PostLineMaterialSwapper"/> 换装，不改场景序列化。
        /// </summary>
        public const string ToonLitShaderName = "PirateCrew/PostLineToonLit";

        // ==================== 描边参数族（材质属性；feature 每帧 SetFloat）====================
        // 【只有 9 个】参照库另有 _DepthDiffLow/High（算出后未被使用）与 _LineTint/_CreaseTint/
        // _FlipPalettes/_LineOverlay/_CreaseOverlay/_ConvexCutoff/_CreaseFeather/_ConcaveCutoff/
        // _ConcaveZCutoff（声明了但不参与计算）——全是死参数，按规格 §9 坑 11 一律不搬；
        // 参照的 _KernelRadius 也不搬：步长写死为 1 画布纹素（§9 坑 10，理由见 Outline shader 头注）。

        /// <summary>剪影线色压暗倍率：线色 = 最近邻像素色 × 本值（线自带物体固有色，不是固定墨色）。</summary>
        public static readonly int LineDarkenId = Shader.PropertyToID("_LineDarken");

        /// <summary>棱线提亮倍率：棱线色 = 中心像素色 × (1 + 本值)。</summary>
        public static readonly int CreaseBrightenId = Shader.PropertyToID("_CreaseBrighten");

        /// <summary>剪影线遮罩 alpha（合成时即混合因子）。</summary>
        public static readonly int LineAlphaId = Shader.PropertyToID("_LineAlpha");

        /// <summary>棱线遮罩 alpha 系数：最终 alpha = 棱线权重 × 本值。</summary>
        public static readonly int CreaseAlphaId = Shader.PropertyToID("_CreaseAlpha");

        /// <summary>剪影深度跳变阈值（**视图空间米**，正对相机时的基准值；与分辨率无关）。</summary>
        public static readonly int ZDeltaCutoffId = Shader.PropertyToID("_ZDeltaCutoff");

        /// <summary>掠射起点：中心法线的 <c>1 - 视图 z</c> 超过它才开始放大剪影阈值。</summary>
        public static readonly int AngleZCutoffId = Shader.PropertyToID("_AngleZCutoff");

        /// <summary>掠射面阈值放大倍数（防斜坡沿全长出剪影线）。</summary>
        public static readonly int AngleZScaleId = Shader.PropertyToID("_AngleZScale");

        /// <summary>棱线方向对比度 smoothstep 下限。</summary>
        public static readonly int NormalSmoothLowId = Shader.PropertyToID("_NormalSmoothLow");

        /// <summary>棱线方向对比度 smoothstep 上限。</summary>
        public static readonly int NormalSmoothHighId = Shader.PropertyToID("_NormalSmoothHigh");

        // ==================== 纹理全局 ====================

        /// <summary>
        /// 描边遮罩（画布尺寸，Point）：Pass2 写、Pass3 读。经 <c>cmd.SetGlobalTexture</c> 注入——
        /// 两趟在同一个 <c>Execute</c> 里顺序录制，读必然晚于写（规格 §2.0 ⚠ 的 URP14 等价约束）。
        /// </summary>
        public static readonly int OutlineTextureId = Shader.PropertyToID("_PostLineOutlineTexture");

        // ==================== 环境 / 云影全局（参照库同名）====================
        // 参照库把这组量交给 DayNightCycleManager / CloudShadowManager 每帧 SetGlobal*；
        // 本仓固定光照，不搬昼夜系统，但**公式读的就是这些实名全局**，缺一个就静默退回常量。
        // 参照库实测（external/ref/unity-isometric-pixel-pipeline）：
        //   DayNightCycleManager.cs:131  SetGlobalColor(_GlobalAmbientColor)
        //   CloudShadowManager.cs:65-78  其余 10 个云影量
        // ToonLit 侧只认 _GlobalAmbientColor（不是 CellShade 的 _PixelartAmbientColor）。

        /// <summary>
        /// 环境光（线性加数）。参照库 DayNightCycleManager 每帧下发；**不是** CellShade 的
        /// <see cref="PixelartPath.AmbientColorId"/>——两个名字是两条链各自的契约，不要合并。
        /// </summary>
        public static readonly int GlobalAmbientColorId = Shader.PropertyToID("_GlobalAmbientColor");

        /// <summary>云噪声图（灰度，sRGB+Bilinear+Repeat）。</summary>
        public static readonly int CloudNoiseId = Shader.PropertyToID("_CloudNoise");

        /// <summary>噪声世界尺度（世界米 / 一次平铺；参照默认 50）。</summary>
        public static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");

        /// <summary>云层平面高度（世界 Y；参照默认 20）。</summary>
        public static readonly int CloudWorldYId = Shader.PropertyToID("_CloudWorldY");

        /// <summary>云层漂移速度（UV/秒；由下发方按天时长标定）。</summary>
        public static readonly int CloudSpeedId = Shader.PropertyToID("_CloudSpeed");

        /// <summary>云影对比度（参照默认 2）。</summary>
        public static readonly int CloudContrastId = Shader.PropertyToID("_CloudContrast");

        /// <summary>云影阈值（参照默认 0.3）。</summary>
        public static readonly int CloudThresholdId = Shader.PropertyToID("_CloudThreshold");

        /// <summary>云影漂移方向（xy；参照默认 (1,0)）。</summary>
        public static readonly int CloudDirectionId = Shader.PropertyToID("_CloudDirection");

        /// <summary>云影最暗处的亮度下限（参照默认 0.3）。</summary>
        public static readonly int CloudShadowMinId = Shader.PropertyToID("_CloudShadowMin");

        /// <summary>两层噪声采样方向的散开角（度；参照默认 10）。</summary>
        public static readonly int CloudDivergeAngleId = Shader.PropertyToID("_CloudDivergeAngle");

        /// <summary>云影投影方向（世界空间，**指向云层**；参照默认 (0.2,-1,0.2) 归一化）。</summary>
        public static readonly int CloudLightDirectionId = Shader.PropertyToID("_CloudLightDirection");

        /// <summary>云影对比度幂次（参照默认 1）。</summary>
        public static readonly int CloudPowerId = Shader.PropertyToID("_CloudPower");

        // ==================== 与单元着色路径共用的契约全局（别名，见类注释）====================

        /// <summary>
        /// 上采样亚纹素平移补偿（xy，画布 UV 空间）——相机 snap 的第二步，由 Pass4 消费。
        /// 参照库叫 <c>_PixelPanOffset</c>；本仓接到 rig 预留的 <see cref="PixelartPath.SnapOffsetUvId"/>，
        /// 不另起名。下发方（Transform 级 snap 组件）不在本链内，未下发时全局为 0 = 不补偿。
        /// </summary>
        public static readonly int PanOffsetId = PixelartPath.SnapOffsetUvId;

        /// <summary>主光方向（世界空间，**指向光源**）。</summary>
        public static readonly int LightDirId = PixelartPath.LightDirId;

        /// <summary>主光颜色（线性，已乘强度）。</summary>
        public static readonly int LightColorId = PixelartPath.LightColorId;

        /// <summary>1 画布像素的世界尺寸（= 可视正交高 ÷ 画布高）。</summary>
        public static readonly int UnitSizeId = PixelartPath.UnitSizeId;

        /// <summary>低分辨率画布宽（像素）。Outline / SharpUpscale 的纹素步长从它推。</summary>
        public static readonly int CanvasWidthId = PixelartPath.RTWidthId;

        /// <summary>低分辨率画布高（像素）。</summary>
        public static readonly int CanvasHeightId = PixelartPath.RTHeightId;

        /// <summary>一个画布像素占几个屏幕像素（= 档位；规格 §8.2 的 Bayer 画布坐标换算也读它）。</summary>
        public static readonly int SamplingScaleId = PixelartPath.SamplingScaleId;
    }
}
