using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化着色路径**（v3 蓝本重写线）的共享常量与运行期注册表。
    ///
    /// 【这条路径是什么】把着色从"全分辨率前向着色 + 事后降采"改成 **v3 的形状**：
    ///   ① 物体 pass 不投影、不着色，只往低分辨率 G-buffer 写数据（albedo / 法线 / 逐物体参数）；
    ///   ② 全部着色数学在**低分辨率域**跑（一趟全屏 pass 读 G-buffer 出最终色）；
    ///   ③ 最后一次 nearest 放大上屏。
    /// 架构与判据见 docs/技术/渲染/蓝图-新渲染管线-v3蓝本.md §0/§5/§6（P1-P3）；
    /// 本路径**独立于既有视觉链**（`PirateToon` / `PixelationRendererFeature`），
    /// 后者的处置见立项任务书，本路径不引用它们任何一处实现。
    ///
    /// 【为什么常量集中在这里】shader 全局名与 shader 名都是字符串契约（拼错即静默失效），
    /// 同 EventBus 事件名的纪律：只有这一处写字面量，其余全部走常量。
    /// </summary>
    public static class PixelartPath
    {
        // ---------------- shader / 材质 ----------------

        /// <summary>物体 pass 的 shader（只写 G-buffer，不做着色）。</summary>
        public const string ObjectShaderName = "PirateCrew/Pixelart/PixelartObject";

        /// <summary>低分辨率域着色 shader（一趟全屏，读 G-buffer 出最终色）。</summary>
        public const string ShadingShaderName = "PirateCrew/Pixelart/PixelartShading";

        /// <summary>上屏 blit 用 URP 自带 shader（与既有像素化同一条，播放器构建必然保活）。</summary>
        public const string CoreBlitShaderName = "Hidden/Universal/CoreBlit";

        /// <summary>CoreBlit 的 Nearest pass（点采样放大——像素化的最后一步）。</summary>
        public const int CoreBlitNearestPass = 0;

        /// <summary>物体 pass 的 ShaderTagId：材质里只有这一条 LightMode 的 pass 会被画。</summary>
        public const string OpaqueShaderTagName = "PixelartOpaque";

        /// <summary>墨线（反向壳）pass 的 ShaderTagId：先于本体画，只留轮廓外一圈。</summary>
        public const string InkShaderTagName = "PixelartInk";

        /// <summary>渲染器资产所在目录（装配器创建，本路径专用，不碰既有两档渲染器）。</summary>
        public const string RendererFolder = "Assets/Settings/URP";

        /// <summary>Cast 渲染器资产名（物体 pass + 低分辨率域着色）。</summary>
        public const string CastRendererName = "PixelartCast_Renderer";

        /// <summary>Screen 渲染器资产名（只有一拍上屏 blit）。</summary>
        public const string ScreenRendererName = "PixelartScreen_Renderer";

        // ---------------- shader 全局 ----------------

        /// <summary>G-buffer：albedo（rgb）+ 覆盖标记（a=1 有几何）。</summary>
        public static readonly int AlbedoBufferId = Shader.PropertyToID("_PixelartAlbedoBuffer");

        /// <summary>G-buffer：世界法线（三通道，[-1,1] 原样存）。</summary>
        public static readonly int NormalBufferId = Shader.PropertyToID("_PixelartNormalBuffer");

        /// <summary>G-buffer：逐物体着色参数（r=档数 g=抖动偏移 b=法线边加成档 a=AA 缩放）。</summary>
        public static readonly int PropertyBufferId = Shader.PropertyToID("_PixelartPropertyBuffer");

        /// <summary>主光方向（世界空间，**指向光源**；着色用 <c>saturate(dot(L, N))</c>）。</summary>
        public static readonly int LightDirId = Shader.PropertyToID("_PixelartLightDirWS");

        /// <summary>主光颜色（线性）。</summary>
        public static readonly int LightColorId = Shader.PropertyToID("_PixelartLightColor");

        /// <summary>环境光色（线性）——v3 的暗部来源就是它（albedo × 环境项）。</summary>
        public static readonly int AmbientColorId = Shader.PropertyToID("_PixelartAmbientColor");

        /// <summary>1 低分辨率像素的世界长度（= 2×正交size ÷ RT高），相机 snap 与逐物体对齐共用。</summary>
        public static readonly int UnitSizeId = Shader.PropertyToID("_PixelartUnitSize");

        /// <summary>低分辨率 RT 的高度（像素）。</summary>
        public static readonly int RTHeightId = Shader.PropertyToID("_PixelartRTHeight");

        /// <summary>低分辨率 RT 的宽度（像素）。</summary>
        public static readonly int RTWidthId = Shader.PropertyToID("_PixelartRTWidth");

        // ---------------- 运行期注册表 ----------------

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
