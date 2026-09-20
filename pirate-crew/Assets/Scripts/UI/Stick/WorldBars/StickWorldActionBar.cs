using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 工人/玩家头顶动作进度条 —— game-2 ActionProgressIndicator 的 UGUI 移植
    /// （只读移植源：stick-world/modules/units/scripts/entity/action_progress_indicator.gd，
    /// 绘制基类 core/ui_framework/components/progress_painter.gd）。
    ///
    /// 取货/交付/敲击时显示：调用方按动作节奏逐帧喂 SetProgress
    /// （0.5s 取货 / 1.8s 敲击 / 交付节奏见 behavior_haul.gd / behavior_work.gd /
    /// stickman_entity.gd:550,598），组件只管绘制与显隐——gd 同款分工。
    ///
    /// 【挂载方式】组件自装配，宿主挂到 world-space Canvas 下、摆在单位头顶
    /// (0, MountOffsetY×WorldScale) 处；gd 侧锚点是条顶边中点（gd _draw:33 画在
    /// y=0 往下 5px），Unity 侧用 pivot(0.5,1) 复现。gd z_index=1001 绝对顶层
    /// （gd:14-17），Unity 侧排序走 world canvas，组件不管。
    /// 本批不出对账图，条本体 1:1 diff 下批。
    /// </summary>
    public sealed class StickWorldActionBar : MonoBehaviour
    {
        /// <summary>gd 像素 → pirate 3D 世界坐标换算系数，P2 接玩法时定。</summary>
        public const float WorldScale = 1f;

        /// <summary>条宽 px（gd action_progress_indicator.gd:8 _BAR_WIDTH = 40.0）。</summary>
        public const float BarWidth = 40f;
        /// <summary>条高 px（gd:9 _BAR_HEIGHT = 5.0）。</summary>
        public const float BarHeight = 5f;
        /// <summary>头顶安装高度（挂点 y 偏移；装配处 visual_controller.gd:278
        /// position = Vector2(0, -130.0)「头顶上方」）。</summary>
        public const float MountOffsetY = -130f;
        /// <summary>前景琥珀（gd:10 _COLOR_FG = Color(1.0, 0.85, 0.3, 1.0)）。</summary>
        public static readonly Color FillAmber = new Color(1f, 0.85f, 0.3f, 1f);

        /// <summary>底/描边色 = SketchBarGraphic 缺省 = ProgressPainter.COLOR_BG
        /// (0,0,0,0.6)（progress_painter.gd:12）/ COLOR_BORDER (0,0,0,0.8)（:14），
        /// 与 gd draw_bar 缺省参数逐值一致，不再覆写。</summary>

        private SketchBarGraphic _bar;

        private void Awake()
        {
            GameObject go = new GameObject("Bar", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f); // 条顶边中点 = 锚点（gd:33 y=0 顶边）
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(BarWidth, BarHeight) * WorldScale;
            _bar = go.AddComponent<SketchBarGraphic>();
            _bar.FillColor = FillAmber;
            _bar.enabled = false; // gd:17 visible = false 初始隐藏
        }

        /// <summary>
        /// 更新进度并显隐（gd set_progress:20-23：>0 显示，clamp 后 ≤0 隐藏）。
        /// </summary>
        public void SetProgress(float ratio)
        {
            _bar.SetProgress(ratio);
            _bar.enabled = _bar.Progress > 0f;
        }

        /// <summary>隐藏（gd hide_bar:25-28：清零 + 隐藏）。</summary>
        public void HideBar()
        {
            _bar.SetProgress(0f);
            _bar.enabled = false;
        }
    }
}
