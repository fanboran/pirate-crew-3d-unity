using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;   // UIBehaviour 实际在此命名空间（uGUI 包），补 using 供 StickLayoutGroup 继承
using UnityEngine.UI;

// sealed 组件 override UIBehaviour 的 protected 生命周期方法（OnTransformChildrenChanged 等）
// 必然触发 CS0628（sealed 类中的 protected 成员派生类不可见）——Unity 组件的标准写法，纯噪音。
#pragma warning disable CS0628

namespace PirateCrew.UI.Stick
{
    /// <summary>主轴排列方向。</summary>
    public enum StickOrientation
    {
        Vertical,
        Horizontal,
    }

    /// <summary>主轴对齐（gd BoxContainer.alignment：ALIGNMENT_BEGIN / CENTER / END）。
    /// 仅当无任何子项声明扩张权重时生效（有扩张项时剩余空间被其吃满，无可推让）。</summary>
    public enum StickAlign
    {
        Begin,
        Center,
        End,
    }

    /// <summary>交叉轴对齐（gd size_flags：SIZE_FILL / SHRINK_BEGIN / SHRINK_CENTER / SHRINK_END）。</summary>
    public enum StickCrossAlign
    {
        Fill,
        Begin,
        Center,
        End,
    }

    /// <summary>
    /// 子项布局声明（挂在参与 StickLayoutGroup 排列的直接子项上）。
    /// 对齐 Godot Control 的三个布局属性：
    ///   <see cref="Grow"/> = size_flags EXPAND 权重（size_flags_stretch_ratio，默认 1；0 = 不扩张）；
    ///   <see cref="CrossAlign"/> = size_flags 交叉轴语义（默认 Fill，对齐 gd 默认 SIZE_FILL）；
    ///   <see cref="MinSize"/> = custom_minimum_size。
    /// 未挂本组件的子项按"不扩张 + Fill"参与。
    /// </summary>
    public sealed class StickLayoutElement : MonoBehaviour
    {
        /// <summary>扩张权重（0 = 不扩张，正数按比例分剩余空间）。</summary>
        public float Grow = 0f;

        /// <summary>交叉轴对齐。</summary>
        public StickCrossAlign CrossAlign = StickCrossAlign.Fill;

        /// <summary>声明最小尺寸（Godot custom_minimum_size 的等价物，最终 min 取声明与测量的大者）。</summary>
        public Vector2 MinSize = Vector2.zero;
    }

    /// <summary>
    /// 垂直/水平堆叠布局 —— Godot BoxContainer 数学的最小移植（**禁止换用 Unity LayoutGroup**：
    /// 两者 min size 分配 / separation / 扩张语义不同源，混用永远调不齐；此为已定裁决）。
    ///
    /// 数学（与 Godot 容器逐条对应）：
    ///  1. 参与子项 = 全部 activeSelf 的直接 RectTransform 子项；
    ///  2. 子项 min 主/交叉尺寸 = max(内部测量, 声明 MinSize)。内部测量只信
    ///     TMP_Text（gd Label/文本测量）与嵌套 StickLayoutGroup（容器递归求和）——
    ///     UGUI Image 的 preferred（贴图原尺寸）不参与（gd 的 StyleBoxTexture 不抬 min）；
    ///  3. 总 min 主轴 = Σ子项 min + (n-1) × Separation（gd theme separation）；
    ///  4. 剩余空间 = 可用主轴 − 总 min；按 Grow 权重比例分配给扩张子项（全 0 时不分配，
    ///     改由 <see cref="MainAlign"/> 把整块推 Begin/Center/End）；
    ///  5. 交叉轴按子项 CrossAlign 放置/拉伸，可用宽 = 容器交叉尺寸 − padding。
    ///
    /// 驱动方式：自身脏标记（子项增删 / 自身尺寸变化 / 手动 <see cref="SetLayoutDirty"/>）
    /// 在 LateUpdate 重排——不走 Unity LayoutRebuilder，避免两套布局系统互相踩。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickLayoutGroup : UIBehaviour, ILayoutElement
    {
        // UIBehaviour（非 Graphic）没有 rectTransform 属性，这里显式补一个等价访问器。
        private RectTransform rectTransform => (RectTransform)transform;

        public StickOrientation Orientation = StickOrientation.Vertical;

        /// <summary>子项间距（gd add_theme_constant_override("separation", n)）。</summary>
        public float Separation = 8f;

        /// <summary>主轴整块对齐（无扩张子项时生效）。</summary>
        public StickAlign MainAlign = StickAlign.Begin;

        public float PaddingLeft = 0f;
        public float PaddingRight = 0f;
        public float PaddingTop = 0f;
        public float PaddingBottom = 0f;

        /// <summary>主轴尺寸收缩为内容 min（godot 容器自适应；Confirm 紧凑窗口用）。
        /// 注意只收缩不放大：内容超出时保持容器给定尺寸交由滚动/裁剪处理。</summary>
        public bool AutoSizeMainAxis = false;

        readonly List<RectTransform> _items = new List<RectTransform>();
        bool _dirty = true;

        // ---------------- 脏标记与驱动 ----------------

        // OnTransformChildrenChanged 是 MonoBehaviour 魔术方法（引擎反射调用，非 virtual），
        // 不能 override——去关键字留同名方法即可被引擎回调。
        protected void OnTransformChildrenChanged()
        {
            SetLayoutDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            SetLayoutDirty();
        }

        /// <summary>子项内容尺寸变化（如文本改字）时手动标记（gd 侧 Container 靠信号自动感知，
        /// Unity 无对应子项事件，调用方负责）。</summary>
        public void SetLayoutDirty()
        {
            _dirty = true;
        }

        void LateUpdate()
        {
            if (!_dirty)
                return;
            _dirty = false;
            PerformLayout();
        }

        // ---------------- Godot BoxContainer 数学 ----------------

        void CollectItems()
        {
            _items.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i) is RectTransform child && child.gameObject.activeSelf)
                    _items.Add(child);
            }
        }

        /// <summary>子项最小尺寸：max(可信测量, 声明 MinSize)。可信测量 = TMP 文本与嵌套本布局。</summary>
        static Vector2 MinSizeOf(RectTransform item)
        {
            Vector2 m = Vector2.zero;
            ILayoutElement le = item.GetComponent<ILayoutElement>();
            if (le is TMP_Text)
            {
                // 文本：gd 的 min size 即文本测量（无 min/preferred 之分），取 preferred
                m = new Vector2(Mathf.Max(0f, le.preferredWidth), Mathf.Max(0f, le.preferredHeight));
            }
            else if (le is StickLayoutGroup)
            {
                // 嵌套容器：递归报告（本类 ILayoutElement 即容器求和数学）
                m = new Vector2(Mathf.Max(0f, le.minWidth), Mathf.Max(0f, le.minHeight));
            }
            StickLayoutElement declared = item.GetComponent<StickLayoutElement>();
            if (declared != null)
                m = Vector2.Max(m, declared.MinSize);
            return m;
        }

        /// <summary>执行一次排列（主轴游标推进 + 交叉轴按对齐落位）。</summary>
        public void PerformLayout()
        {
            CollectItems();
            int n = _items.Count;
            Rect rect = rectTransform.rect;

            float padMain = Orientation == StickOrientation.Vertical
                ? PaddingTop + PaddingBottom
                : PaddingLeft + PaddingRight;
            float padCross = Orientation == StickOrientation.Vertical
                ? PaddingLeft + PaddingRight
                : PaddingTop + PaddingBottom;

            float[] mins = new float[n];
            float[] crosses = new float[n];
            float[] grows = new float[n];
            float sumMin = 0f, sumGrow = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector2 m = MinSizeOf(_items[i]);
                StickLayoutElement el = _items[i].GetComponent<StickLayoutElement>();
                mins[i] = Orientation == StickOrientation.Vertical ? m.y : m.x;
                crosses[i] = Orientation == StickOrientation.Vertical ? m.x : m.y;
                grows[i] = el != null ? Mathf.Max(0f, el.Grow) : 0f;
                sumMin += mins[i];
                sumGrow += grows[i];
            }

            float total = n > 0 ? sumMin + Separation * (n - 1) : 0f;

            // 自适应：主轴尺寸 = 内容 min + padding（只收缩到 min，不小于既有给定值时放大无意义）
            if (AutoSizeMainAxis)
            {
                Vector2 size = rectTransform.sizeDelta;
                float want = total + padMain;
                float current = (Orientation == StickOrientation.Vertical ? rect.height : rect.width);
                if (!Mathf.Approximately(current, want))
                {
                    if (Orientation == StickOrientation.Vertical)
                        rectTransform.sizeDelta = new Vector2(size.x, want);
                    else
                        rectTransform.sizeDelta = new Vector2(want, size.y);
                    rect = rectTransform.rect;
                }
            }

            float availMain = (Orientation == StickOrientation.Vertical ? rect.height : rect.width) - padMain;
            float availCross = (Orientation == StickOrientation.Vertical ? rect.width : rect.height) - padCross;
            float extra = Mathf.Max(0f, availMain - total);

            // 起始游标：有扩张子项时从 0 开始（剩余被吃满）；否则按整块对齐推让
            float cursor = 0f;
            if (sumGrow <= 0f)
            {
                float slack = Mathf.Max(0f, availMain - total);
                if (MainAlign == StickAlign.Center)
                    cursor = slack * 0.5f;
                else if (MainAlign == StickAlign.End)
                    cursor = slack;
            }

            for (int i = 0; i < n; i++)
            {
                float growExtra = sumGrow > 0f && grows[i] > 0f ? extra * (grows[i] / sumGrow) : 0f;
                float mainSize = mins[i] + growExtra;

                StickLayoutElement el = _items[i].GetComponent<StickLayoutElement>();
                StickCrossAlign ca = el != null ? el.CrossAlign : StickCrossAlign.Fill;
                float crossSize;
                float crossPos;
                switch (ca)
                {
                    case StickCrossAlign.Begin:
                        crossSize = crosses[i];
                        crossPos = 0f;
                        break;
                    case StickCrossAlign.Center:
                        crossSize = crosses[i];
                        crossPos = Mathf.Max(0f, availCross - crossSize) * 0.5f;
                        break;
                    case StickCrossAlign.End:
                        crossSize = crosses[i];
                        crossPos = Mathf.Max(0f, availCross - crossSize);
                        break;
                    default:   // Fill：拉伸到可用交叉宽（gd SIZE_FILL 默认）
                        crossSize = Mathf.Max(crosses[i], availCross);
                        crossPos = 0f;
                        break;
                }

                Place(_items[i], cursor, mainSize, crossPos, crossSize);
                cursor += mainSize + Separation;
            }
        }

        void Place(RectTransform item, float mainPos, float mainSize, float crossPos, float crossSize)
        {
            if (Orientation == StickOrientation.Vertical)
            {
                // 从父顶边向下排（Unity y 向上，故 anchoredPosition.y 取负）
                item.anchorMin = new Vector2(0f, 1f);
                item.anchorMax = new Vector2(0f, 1f);
                item.pivot = new Vector2(0f, 1f);
                item.sizeDelta = new Vector2(crossSize, mainSize);
                item.anchoredPosition = new Vector2(PaddingLeft + crossPos, -(PaddingTop + mainPos));
            }
            else
            {
                // 从父左边向右排
                item.anchorMin = new Vector2(0f, 0f);
                item.anchorMax = new Vector2(0f, 0f);
                item.pivot = new Vector2(0f, 0f);
                item.sizeDelta = new Vector2(mainSize, crossSize);
                item.anchoredPosition = new Vector2(PaddingLeft + mainPos, PaddingBottom + crossPos);
            }
        }

        // ---------------- ILayoutElement：向嵌套的父布局报告容器求和结果 ----------------

        void ILayoutElement.CalculateLayoutInputHorizontal() { }
        void ILayoutElement.CalculateLayoutInputVertical() { }

        (float main, float cross) ContentMin()
        {
            CollectItems();
            float sumMin = 0f, maxCross = 0f;
            for (int i = 0; i < _items.Count; i++)
            {
                Vector2 m = MinSizeOf(_items[i]);
                if (Orientation == StickOrientation.Vertical)
                {
                    sumMin += m.y;
                    maxCross = Mathf.Max(maxCross, m.x);
                }
                else
                {
                    sumMin += m.x;
                    maxCross = Mathf.Max(maxCross, m.y);
                }
            }
            float main = _items.Count > 0 ? sumMin + Separation * (_items.Count - 1) : 0f;
            return (main, maxCross);
        }

        public float minWidth
        {
            get
            {
                (float main, float cross) c = ContentMin();
                return (Orientation == StickOrientation.Horizontal ? c.main : c.cross) + PaddingLeft + PaddingRight;
            }
        }

        public float preferredWidth => minWidth;

        public float minHeight
        {
            get
            {
                (float main, float cross) c = ContentMin();
                return (Orientation == StickOrientation.Vertical ? c.main : c.cross) + PaddingTop + PaddingBottom;
            }
        }

        public float preferredHeight => minHeight;

        public float flexibleWidth => -1f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 1;
    }
}
