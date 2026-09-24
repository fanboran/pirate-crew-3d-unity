namespace PirateCrew.UI
{
    /// <summary>
    /// Aseprite dark 主题的**布局令牌**（theme.xml <c>&lt;dimensions&gt;</c> + <c>&lt;styles&gt;</c> 段的
    /// 间距语法逐条登记，2026-09-25 搬皮第二批）。参考库：
    /// <c>external/aseprite-ref/data/extensions/aseprite-theme/dark/theme.xml</c>。
    ///
    /// 【单位口径】本表一律存**设计格**（= theme.xml 原值；1 设计格 = <see cref="PixelSkin.Unit"/>
    /// 画布像素），消费点用 <see cref="Px"/> 换算——**别把 theme 数字当画布像素直用**（差一倍，
    /// 2026-09-25 前的窗体边距/复选偏移全犯了这个错，观感挤的根源之一）。
    ///
    /// 【职责边界】只放"控件周围留多少空、文字图标偏多少"的数字；件本身的几何
    /// （尺寸/九宫格切片）在 Editor 侧 BeveledPixelSpriteBuilder 的模板里，别在这里重复。
    /// </summary>
    public static class AseLayout
    {
        // ---------------- <dimensions> 表（哪里用什么尺寸） ----------------

        /// <summary>scrollbar_size。</summary>
        public const int ScrollbarSize = 12;
        /// <summary>mini_scrollbar_size。</summary>
        public const int MiniScrollbarSize = 6;
        /// <summary>tabs_height（页签行高）。</summary>
        public const int TabsHeight = 17;
        /// <summary>tabs_width（页签最小宽）。</summary>
        public const int TabsWidth = 80;
        /// <summary>tabs_bottom_height（页签底条）。</summary>
        public const int TabsBottomHeight = 5;
        /// <summary>docked_tabs_height（停靠页签高）。</summary>
        public const int DockedTabsHeight = 12;
        /// <summary>color_slider_height（颜色滑条高）。</summary>
        public const int ColorSliderHeight = 14;
        /// <summary>context_bar_height（上下文工具栏高）。</summary>
        public const int ContextBarHeight = 18;
        /// <summary>color_bar_buttons_height（色板按钮高）。</summary>
        public const int ColorBarButtonsHeight = 16;

        // ---------------- <styles> 段：窗体 ----------------

        /// <summary>window_with_title border（左/右/下内容内缩）。</summary>
        public const int WindowBorder = 6;
        /// <summary>window_with_title border-top=17（= 标题带 15 整条 + 带下 2 格缝）。</summary>
        public const int WindowBorderTop = 17;
        /// <summary>window_without_title / menu border=3（无标题弹窗内容内缩）。</summary>
        public const int PopupBorder = 3;
        /// <summary>window_title_label margin-left。</summary>
        public const int TitleMarginLeft = 5;
        /// <summary>window_title_label margin-top。</summary>
        public const int TitleMarginTop = 5;
        /// <summary>window_button 系 margin-top。</summary>
        public const int WindowButtonMarginTop = 3;
        /// <summary>window_close_button margin-right（最右钮）。</summary>
        public const int CloseButtonMarginRight = 3;
        /// <summary>其余窗控钮 margin-right=1（? 在 × 左侧一格）。</summary>
        public const int WindowButtonGap = 1;

        // ---------------- <styles> 段：控件 ----------------

        /// <summary>check_box / radio_button border。</summary>
        public const int CheckBorder = 2;
        /// <summary>check/radio 图标 x。</summary>
        public const int CheckIconX = 2;
        /// <summary>check/radio 文字 x（= 图标 2 + 图标宽 8 + 缝 4）。</summary>
        public const int CheckTextX = 14;
        /// <summary>slider border-top（滑条内容上内缩）。</summary>
        public const int SliderBorderTop = 4;
        /// <summary>slider border-bottom。</summary>
        public const int SliderBorderBottom = 5;
        /// <summary>list_item border（列表行内缩）。</summary>
        public const int ListItemBorder = 1;
        /// <summary>list_item text x。</summary>
        public const int ListItemTextX = 1;
        /// <summary>list_header_label padding。</summary>
        public const int ListHeaderPadding = 2;
        /// <summary>horizontal_separator border。</summary>
        public const int SeparatorBorder = 2;
        /// <summary>horizontal_separator 文字 x（蓝分组字 #6E9ADB 的缩进）。</summary>
        public const int SeparatorTextX = 4;
        /// <summary>view border（视图区左/右/下内缩）。</summary>
        public const int ViewBorder = 3;
        /// <summary>view border-top。</summary>
        public const int ViewBorderTop = 4;
        /// <summary>textbox_text border（文本框内缩）。</summary>
        public const int TextBoxBorder = 4;
        /// <summary>label padding。</summary>
        public const int LabelPadding = 1;
        /// <summary>mini_button padding-top。</summary>
        public const int MiniButtonPaddingTop = 1;
        /// <summary>mini_button border（左/右）。</summary>
        public const int MiniButtonBorder = 3;
        /// <summary>mini_button border-bottom（按钮下沿特厚——按压重心的视觉配重）。</summary>
        public const int MiniButtonBorderBottom = 5;
        /// <summary>buttonset gap-rows（**负缝**：成组按钮边框互搭连排）。</summary>
        public const int ButtonsetGapRows = -3;
        /// <summary>buttonset gap-columns。</summary>
        public const int ButtonsetGapColumns = -1;
        /// <summary>combobox_button padding=0。</summary>
        public const int ComboboxButtonPadding = 0;

        /// <summary>设计格 → 画布像素（唯一换算出口）。</summary>
        public static float Px(int cells)
        {
            return cells * PixelSkin.Unit;
        }
    }
}
