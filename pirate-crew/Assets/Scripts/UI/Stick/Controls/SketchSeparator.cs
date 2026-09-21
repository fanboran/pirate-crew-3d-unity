using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦分隔线 —— game-2 SketchSeparator（sketch_separator.gd）的 UGUI 复刻。
    ///
    /// 【口径选择】任务给的两条路线里取**源码口径**（gd SketchSeparator 是 wavy 自绘，
    /// 非贴图件）：BORDER @ 0.35 波浪线、线宽 1.3、两端各让 2px，逐公式对照。
    /// sep_h/sep_v 贴图槽在 gd 侧是原生 HSeparator/VSeparator 兜底的 stylebox
    /// （SketchStyle.separator()），不是本控件源码的渲染路径，故不取。
    ///
    /// 【沸腾】gd _process 每 WOBBLE_INTERVAL(0.12s) 重掷 seed → 确定性重掷序列
    /// （同 SketchWobbleGraphic 的哈希方案，禁 System.Random 工程纪律）。
    /// edit 模式 Update 不跑，样张取 SeedBase 初相静态帧。
    ///
    /// 【自绘层在独立文件】波浪线画在 <see cref="WavyLineGraphic"/>（**顶级类**）上：
    /// Unity 无法处理嵌套 MonoBehaviour（存 Prefab 直接失败、场景重载后组件还原不回来），
    /// 故自绘件一律顶级类 + 独立文件。详见该文件头。
    /// </summary>
    public sealed class SketchSeparator : MonoBehaviour
    {
        /// <summary>方向（gd Dir.HORIZONTAL/VERTICAL 同名）。</summary>
        public enum Direction
        {
            Horizontal,
            Vertical,
        }

        /// <summary>方向（运行时可切）。</summary>
        public Direction Dir = Direction.Horizontal;

        /// <summary>seed 基值（沸腾重掷序列由它决定，确定性）。</summary>
        public int SeedBase;

        /// <summary>建一条分隔线（纯展示件，不拦截点击）。direction 缺省水平。</summary>
        public static SketchSeparator Create(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            Direction direction = Direction.Horizontal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var separator = go.AddComponent<SketchSeparator>();
            separator.Dir = direction;
            var g = go.AddComponent<WavyLineGraphic>();
            g.Owner = separator;
            return separator;
        }

    }
}
