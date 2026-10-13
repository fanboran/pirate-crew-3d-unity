using UnityEngine;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 海图窗顶视缩略的**取景/映射规则**（纯 C#，无头可测）：关卡 XZ → 顶视图归一化坐标。
    ///
    /// 【是什么】战斗小地图的底图是编辑器出图的关卡顶视平色 PNG（播放器出图
    /// <c>-minimapOut</c>，见 <c>MinimapTopViewCapture</c>）；运行时只贴图 + 单位点。
    /// 出图取景（正交相机覆盖跨度）与运行时点位映射共用本类**同一份数学**，像素级对齐。
    ///
    /// 【为什么住在 Battle.Levels】这是「关卡跨度 → 图」的域换算（与
    /// <see cref="LevelGeometry"/> 同域）；<c>PirateCrew.UI</c> 引用本程序集，
    /// 放 UI 侧会让出图组件（Gameplay）反向依赖 UI 成环。
    ///
    /// 【坐标方向】屏幕上方 = 远处（-Z）、右侧 = +X；场地信箱适配居中，
    /// 短轴留白露海色。映射细节见 <see cref="TopViewMapping.WorldToNormalized"/>。
    /// </summary>
    public static class TopViewFraming
    {
        /// <summary>顶视图内容区宽（画布像素）：窗体 136 − window_with_title 左右内缩 6×2。</summary>
        public const float TopViewContentWidth = 124f;

        /// <summary>
        /// 顶视图内容区高（画布像素）：窗体 102 − 顶部标题带 17 − 底 7。
        /// 底内缩 = <c>AseLayout.WindowBorder</c> 6 + 1 px 取偶——顶视图纹理 1 纹素 =
        /// 1 艺术像素 = 2 画布像素（pixelScale 2），内容区两轴必须是偶数，
        /// 否则点采样显示时错半个像素块（与渲染管线「像素网格对齐」同口径）。
        /// </summary>
        public const float TopViewContentHeight = 78f;

        // 【纹素口径 = 画布单位】UI 画布（CanvasScaler scaleFactor=Unit=2）与像素化管线的
        // 艺术画布（屏幕 ÷ pixelScale，默认档 2）是**同一尺寸**——1 画布单位 = 1 艺术像素。
        // 顶视图纹理按内容区**原尺寸**烘焙（1 纹素 = 1 画布单位 = 1 艺术像素），RawImage
        // 1:1 显示，游戏内每像素与出图 PNG 一一对应；不要再除 pixelScale（除过会把颗粒
        // 粗化一倍，与全画面像素网格错位）。pixelScale 调档（3..5）时 UI 画布不动、主画面
        // 艺术像素变大，小地图与全画面网格的取舍同全 UI 一致。
        /// <summary>顶视图烘焙纹理宽（纹素）＝ 内容区宽（1 纹素 = 1 画布单位 = 1 艺术像素）。</summary>
        public const int TopViewTexWidth = 124;

        /// <summary>顶视图烘焙纹理高（纹素）＝ 内容区高。</summary>
        public const int TopViewTexHeight = 78;

        /// <summary>顶视图纹理长宽比（烘焙相机与点位映射共用的唯一 aspect 来源）。</summary>
        public static float TopViewTexAspect => (float)TopViewTexWidth / TopViewTexHeight;

        /// <summary>
        /// Resources 下的顶视图命名规则：<c>Minimaps/level-&lt;关卡号&gt;</c>。
        /// 出图组件产出，运行时按本规则加载。
        /// </summary>
        public static string TopViewResourcePath(int levelNumber) => "Minimaps/level-" + levelNumber;

        /// <summary>
        /// 顶视图**取景参数**的命名规则：<c>Minimaps/level-&lt;关卡号&gt;-frame</c>。
        /// 出图组件随 PNG 一起落盘（JSON），运行时点位映射读同一份参数，保证点位与底图对齐。
        /// （不能与 PNG 同名——Resources.Load 按名取用，同目录同名不同扩展会冲突。）
        /// </summary>
        public static string TopViewFrameResourcePath(int levelNumber) => "Minimaps/level-" + levelNumber + "-frame";
    }

    /// <summary>顶视图取景参数（随 PNG 落盘的 JSON 载荷，见 <see cref="TopViewFraming.TopViewFrameResourcePath"/>）。</summary>
    [System.Serializable]
    public struct TopViewFrame
    {
        /// <summary>取景中心（世界 X）。</summary>
        public float centerX;

        /// <summary>取景中心（世界 Z）。</summary>
        public float centerZ;

        /// <summary>内容跨度（世界 X，含边距；运行时按图幅长宽比信箱适配）。</summary>
        public float spanX;

        /// <summary>内容跨度（世界 Z，含边距）。</summary>
        public float spanZ;
    }

    /// <summary>
    /// 顶视取景/点位映射（纯值对象）：以取景中心 + 覆盖跨度描述正交顶视相机的取景矩形。
    /// 出图组件用它定相机位置与正交尺寸、运行时（UI 层）用它把单位 XZ 投到点位锚点——
    /// 两边消费同一份参数（出图落 <see cref="TopViewFrame"/>，运行时读取），点位与底图像素级对齐。
    /// </summary>
    public readonly struct TopViewMapping
    {
        /// <summary>取景中心（世界 X）。</summary>
        public readonly float CenterX;

        /// <summary>取景中心（世界 Z）。</summary>
        public readonly float CenterZ;

        /// <summary>取景内容跨度（米；信箱适配后的实际覆盖）。</summary>
        public readonly float SpanX;

        /// <summary>取景内容跨度（米）。</summary>
        public readonly float SpanZ;

        /// <summary>相机/图幅横向覆盖跨度（米；≥ SpanX）。</summary>
        public readonly float CoverX;

        /// <summary>相机/图幅纵向覆盖跨度（米；≥ SpanZ）。</summary>
        public readonly float CoverZ;

        TopViewMapping(float centerX, float centerZ, float spanX, float spanZ, float coverX, float coverZ)
        {
            CenterX = centerX;
            CenterZ = centerZ;
            SpanX = spanX;
            SpanZ = spanZ;
            CoverX = coverX;
            CoverZ = coverZ;
        }

        /// <summary>
        /// 按**场地矩形** (0,0)–(spanX, spanZ) 构造（中心 = 场地几何中心；信箱适配：
        /// <c>coverX = max(spanX, spanZ × aspect)</c>、<c>coverZ = coverX / aspect</c>）。
        /// 顶视图 frame 参数缺失时的兜底口径。
        /// </summary>
        public static TopViewMapping ForSpan(float spanX, float spanZ, float texAspect)
        {
            return ForContent(spanX * 0.5f, spanZ * 0.5f, spanX, spanZ, texAspect);
        }

        /// <summary>
        /// 按**内容包围盒**构造（中心与跨度来自场景陈设实测，见 <see cref="TopViewFrame"/>；
        /// 信箱适配同 <see cref="ForSpan"/>——较长轴恰好铺满、另一轴居中留白）。
        /// 跨度非正值按 1e-3 兜底（坏数据不除零）。
        /// </summary>
        public static TopViewMapping ForContent(float centerX, float centerZ, float spanX, float spanZ, float texAspect)
        {
            centerX = float.IsNaN(centerX) ? 0f : centerX;
            centerZ = float.IsNaN(centerZ) ? 0f : centerZ;
            spanX = Mathf.Max(spanX, 1e-3f);
            spanZ = Mathf.Max(spanZ, 1e-3f);
            texAspect = Mathf.Max(texAspect, 1e-3f);

            float coverX = Mathf.Max(spanX, spanZ * texAspect);
            float coverZ = coverX / texAspect;
            return new TopViewMapping(centerX, centerZ, spanX, spanZ, coverX, coverZ);
        }

        /// <summary>
        /// 世界 XZ → 图归一化坐标（0–1，UGUI 锚点口径）。取景中心 → 图心 (0.5, 0.5)；
        /// 屏幕上方 = 远处（-Z）：z 越小 v 越大。越界不裁剪（调用方用
        /// <c>MinimapRules.ClampNormalized</c> 裁）。
        /// </summary>
        public Vector2 WorldToNormalized(float worldX, float worldZ)
        {
            float u = 0.5f + (worldX - CenterX) / CoverX;
            float v = 0.5f - (worldZ - CenterZ) / CoverZ;
            return new Vector2(u, v);
        }

        /// <summary>取景内容在图内的归一化矩形（信箱留白露在海色上，供调试/校验读数）。</summary>
        public Rect ArenaRect()
        {
            float halfU = SpanX * 0.5f / CoverX;
            float halfV = SpanZ * 0.5f / CoverZ;
            Vector2 center = WorldToNormalized(CenterX, CenterZ);
            return Rect.MinMaxRect(center.x - halfU, center.y - halfV, center.x + halfU, center.y + halfV);
        }

        /// <summary>顶视正交相机的 orthographicSize（= 纵向覆盖跨度的一半；出图组件消费）。</summary>
        public float OrthoSize => CoverZ * 0.5f;
    }
}
