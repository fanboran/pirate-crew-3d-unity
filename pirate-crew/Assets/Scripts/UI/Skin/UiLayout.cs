using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>流式容器的内边距（四边独立，单位 = 艺术像素 u）。</summary>
    public struct UiPadding
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public static UiPadding Uniform(int units)
        {
            return new UiPadding { Left = units, Top = units, Right = units, Bottom = units };
        }

        public static UiPadding Symmetric(int horizontalU, int verticalU)
        {
            return new UiPadding { Left = horizontalU, Top = verticalU, Right = horizontalU, Bottom = verticalU };
        }

        internal RectOffset ToOffset()
        {
            return new RectOffset
            {
                left = Left,
                top = Top,
                right = Right,
                bottom = Bottom,
            };
        }
    }

    /// <summary>
    /// 像素流式布局器：UGUI LayoutGroup 族的像素纪律包装——本仓"布局器"唯一入口，
    /// 装配侧不再手摆 anchoredPosition 常量（"怎么全是常量"的收口）。
    ///
    /// 【语义】对齐 Aseprite 对话框的盒子/网格排版：控件按声明顺序排布，行/列间距
    /// （child spacing）与内边距全按艺术像素（画布单位）取值；子件尺寸用
    /// <see cref="Element"/> 声明首选值，让"件高 = 内容高 + 边带"这类规则落在声明处
    /// 而不是散落的坐标算式里。绝对定位仍然合法（HUD 徽章/血条这类屏锚件），
    /// 但流式内容一律走这里。
    ///
    /// 【纪律】spacing/padding 参数一律传 **u 数**（内部 ×Unit 落屏幕像素）；
    /// Element 的首选尺寸传**屏幕像素**（与 <c>UiSkin.Px</c> 同量纲）。
    /// 分数 u 的间距会把 3px 像素带糊掉——与装配尺寸纪律同一条。
    /// </summary>
    public static class UiLayout
    {
        /// <summary>
        /// 纵向盒（VBox）：子件自上而下。<paramref name="controlHeights"/>=true 时由布局器
        /// 按子件首选高接管（配合 <see cref="Flexible"/> 弹性占位把"顶上/贴底"表达成声明）；
        /// false 时子件用各自声明的首选高、布局器只管位置。
        /// </summary>
        public static RectTransform VBox(RectTransform container, int spacingU, UiPadding pad,
            bool controlHeights = false, TextAnchor alignment = TextAnchor.UpperCenter)
        {
            var layout = Require<VerticalLayoutGroup>(container);
            layout.spacing = spacingU;
            layout.padding = pad.ToOffset();
            layout.childAlignment = alignment;
            layout.childControlWidth = false;
            layout.childControlHeight = controlHeights;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return container;
        }

        /// <summary>横向盒（HBox）：子件自左向右。<paramref name="controlWidths"/>=true 时
        /// 按子件首选宽接管（配合 <see cref="Flexible"/> 做"左对齐 + 右侧沉底"的行内布局）。</summary>
        public static RectTransform HStack(RectTransform container, int spacingU, UiPadding pad,
            bool controlWidths = false, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var layout = Require<HorizontalLayoutGroup>(container);
            layout.spacing = spacingU;
            layout.padding = pad.ToOffset();
            layout.childAlignment = alignment;
            layout.childControlWidth = controlWidths;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return container;
        }

        /// <summary>网格（N 列等格）：格子尺寸传屏幕像素（u 的整数倍），行列缝传 u 数。</summary>
        public static RectTransform Grid(RectTransform container, Vector2 cellSizePx, int columns,
            int spacingU, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var grid = container.gameObject.GetComponent<GridLayoutGroup>()
                ?? container.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSizePx;
            grid.spacing = new Vector2(spacingU, spacingU);
            grid.padding = new RectOffset();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = alignment;
            return container;
        }

        /// <summary>给流式子件声明首选尺寸（屏幕像素）与弹性权重（在 control 开启的轴上生效）。</summary>
        public static LayoutElement Element(GameObject go, float preferredWidth, float preferredHeight,
            float flexibleWidth = 0f, float flexibleHeight = 0f)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        /// <summary>弹性占位件：在 control 开启的轴上吃掉全部剩余空间（把"贴底/沉右"表达成声明）。</summary>
        public static LayoutElement Flexible(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.flexibleHeight = 1f;
            return element;
        }

        /// <summary>声明本件不参与父布局（绝对定位的装饰件挂进流容器时用）。</summary>
        public static void Ignore(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        /// <summary>取/挂方向布局组；容器上若已挂**另一方向**的组则先卸（方向互斥，防叠加摆两遍）。</summary>
        static T Require<T>(RectTransform container) where T : HorizontalOrVerticalLayoutGroup
        {
            var existing = container.GetComponent<T>();
            if (existing != null)
                return existing;
            var other = container.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (other != null)
                Object.DestroyImmediate(other);
            return container.gameObject.AddComponent<T>();
        }
    }
}
