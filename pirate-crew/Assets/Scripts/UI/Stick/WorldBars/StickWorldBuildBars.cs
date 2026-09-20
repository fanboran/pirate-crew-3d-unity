using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 建造中建筑头顶双进度条 —— game-2 BuildProgressIndicator 的 UGUI 移植
    /// （只读移植源：stick-world/modules/construction/scripts/build_progress_indicator.gd）。
    ///
    /// 上方材料条（蓝）、下方建造条（绿）；材料/建造双进度由宿主
    /// （ConstructionManager 对应物）创建项目时挂上、逐帧喂入。
    ///
    /// 【「建造 ≤ 材料」钳制语义——已核对 gd 源】gd 组件内**不互相钳制**：
    /// update_progress（gd:38-41）对两值各自 clamp01 后照画；上限约束在**数据层**
    /// construction_project.gd add_build_progress:243-244 收口
    /// （current_work = min(current_work + amount, material_progress × total_work)）。
    /// 本移植保持同构：SetMaterialProgress/SetBuildProgress 各自 Clamp01，
    /// 显示层不做建造≤材料钳制，约束由 pirate 侧玩法数据层负责。
    ///
    /// 【挂载方式】组件自装配，宿主挂到 world-space Canvas 下；gd 侧挂在
    /// BuildMaskLayer、z_index = WorldZ.OVERLAY_PROGRESS = 25
    /// （build_progress_indicator.gd:34；core/constants/world_z.gd:29），
    /// Unity 侧排序走 world canvas，组件不管。本批不出对账图，条本体 1:1 diff 下批。
    /// </summary>
    public sealed class StickWorldBuildBars : MonoBehaviour
    {
        /// <summary>
        /// stick-world 的 32px 网格是其自口径（gd setup:31,33 用占地格数×32 定
        /// 中心与宽度），pirate 3D 世界没有 32px 网格——换算系数 P2 接玩法时定
        /// （1.0 = gd 像素值直用）。
        /// </summary>
        public const float WorldScale = 1f;
        /// <summary>32px 网格格宽（gd:31,33 硬编码 32.0，仅 stick-world 口径）。</summary>
        public const float GdCellSize = 32f;

        // ---- 几何常量（gd 原值，注释即出处行号） ----
        /// <summary>缺省条宽 px（gd:12 _bar_width = 64.0；实际宽随占地）。</summary>
        public const float DefaultBarWidth = 64f;
        /// <summary>最小条宽 px（gd:33 maxf(width×32, 48.0)）。</summary>
        public const float MinBarWidth = 48f;
        /// <summary>条高 px（gd:19 _BAR_HEIGHT = 6.0）。</summary>
        public const float BarHeight = 6f;
        /// <summary>两条间距 px（gd:21 _BAR_GAP = 3.0）。</summary>
        public const float BarGap = 3f;
        /// <summary>条组距地面高度（gd:32 position.y = ground_y - 220.0）。</summary>
        public const float GroundOffsetY = 220f;

        /// <summary>材料条蓝（gd:24 _COLOR_MATERIAL = Color(0.25, 0.5, 0.95, 1.0)）。</summary>
        public static readonly Color MaterialColor = new Color(0.25f, 0.5f, 0.95f, 1f);
        /// <summary>建造条绿（gd:26 _COLOR_BUILD = Color(0.3, 0.85, 0.35, 1.0)）。</summary>
        public static readonly Color BuildColor = new Color(0.3f, 0.85f, 0.35f, 1f);
        /// <summary>两条底色同为 ProgressPainter.COLOR_BG（gd:23,25 引 ProgressPainter.COLOR_BG）。</summary>
        public static readonly Color BarBackgroundColor = new Color(0f, 0f, 0f, 0.6f);

        private SketchBarGraphic _materialBar;
        private SketchBarGraphic _buildBar;
        private RectTransform _materialRect;
        private RectTransform _buildRect;

        private void Awake()
        {
            float w = DefaultBarWidth * WorldScale;
            _materialBar = CreateBar("MaterialBar", out _materialRect, w);
            _materialBar.FillColor = MaterialColor;
            _buildBar = CreateBar("BuildBar", out _buildRect, w);
            _buildBar.FillColor = BuildColor;
            LayoutBars();
        }

        /// <summary>
        /// 按占地落位与定宽（gd setup:30-34 的换算部分）：条组中心 = 格中心、
        /// 距地 GroundOffsetY，宽度 = max(占地格数×32, 48)。
        /// 位置由宿主把根节点摆到世界坐标（gd 像素坐标→pirate 世界坐标的
        /// 映射 P2 接玩法时定）；本方法只负责宽度与文档化换算式。
        /// </summary>
        public void Setup(int cellX, int widthCells, float groundY)
        {
            // gd:31 center_x = (cell_x + width×0.5) × 32
            float centerX = ((float)cellX + widthCells * 0.5f) * GdCellSize * WorldScale;
            // gd:32 center_y = ground_y - 220
            float centerY = groundY - GroundOffsetY * WorldScale;
            var rt = (RectTransform)transform;
            rt.anchoredPosition = new Vector2(centerX, centerY);
            // gd:33 _bar_width = maxf(width × 32, 48)
            float w = Mathf.Max(widthCells * GdCellSize * WorldScale, MinBarWidth * WorldScale);
            _materialRect.sizeDelta = new Vector2(w, BarHeight * WorldScale);
            _buildRect.sizeDelta = new Vector2(w, BarHeight * WorldScale);
        }

        /// <summary>直接覆写条宽（pirate 侧不用 32px 网格时的出口）。</summary>
        public void SetBarWidth(float width)
        {
            float w = Mathf.Max(width, MinBarWidth * WorldScale);
            _materialRect.sizeDelta = new Vector2(w, BarHeight * WorldScale);
            _buildRect.sizeDelta = new Vector2(w, BarHeight * WorldScale);
        }

        /// <summary>材料进度 [0,1]（gd update_progress:39 各自 clamp01，不互相钳制）。</summary>
        public void SetMaterialProgress(float ratio)
        {
            _materialBar.SetProgress(ratio);
        }

        /// <summary>
        /// 建造进度 [0,1]。gd 显示层不做建造≤材料钳制（见类头——约束在
        /// construction_project.gd:243-244 数据层收口），本组件同构。
        /// </summary>
        public void SetBuildProgress(float ratio)
        {
            _buildBar.SetProgress(ratio);
        }

        /// <summary>
        /// 建单条：gd _draw:47-51 的几何——材料条在上方（gd y[-7.5,-1.5]，
        /// Unity y 取正 → 中心 +4.5），建造条在下方（中心 -4.5），水平居中。
        /// </summary>
        private SketchBarGraphic CreateBar(string name, out RectTransform rt, float width)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, BarHeight * WorldScale);
            SketchBarGraphic g = go.AddComponent<SketchBarGraphic>();
            g.BackgroundColor = BarBackgroundColor;
            return g;
        }

        /// <summary>两条竖直排布（gd:47-51：材料条中心 +4.5、建造条中心 -4.5）。</summary>
        private void LayoutBars()
        {
            float halfPitch = (BarHeight * 0.5f + BarGap * 0.5f) * WorldScale;
            _materialRect.anchoredPosition = new Vector2(0f, halfPitch);
            _buildRect.anchoredPosition = new Vector2(0f, -halfPitch);
        }
    }
}
