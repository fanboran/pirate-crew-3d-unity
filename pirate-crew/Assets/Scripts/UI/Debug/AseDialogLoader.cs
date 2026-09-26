using System.Collections.Generic;
using System.Xml.Linq;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// **Aseprite widgets.xml 通用装载器**（创始人裁决「以库为源·原封复刻」；本版走查后
    /// 把残差项升级为**逐行移植**——数值/分支结构对应源码，见各段行号引用）。
    ///
    /// 【与源码的对应】
    /// - AwNode = ui::Widget 布局域（min/max/hidden/childspacing/spans/align）；
    /// - BoxNode = ui::Box（box.cpp:33-170）：
    ///   · RawMeasure = Box::onSizeHint（ADD_CHILD_SIZE / FINAL_ADJUSTMENT 宏逐行，
    ///     含 fitIn 递减；border 计入 hint）；
    ///   · Layout = Box::onResize LAYOUT_CHILDREN（box.cpp:95-140）——**注意**：源码宏
    ///     形参 (x,y,w,h) 在宏体里未使用（宏体写死 .w/.x），纵盒分支 LAYOUT_CHILDREN(y,x,h,w)
    ///     因此无效果；本移植按 onSizeHint 同类宏的可参数化语义恢复主轴/跨轴参数化
    ///     （否则 vbox 会横排，与观测行为矛盾），此为对源码宏形参失效的处理，见交接档偏差表；
    /// - GridNode = ui::Grid（grid.cpp 全文）：Cell 占位（putWidgetInCell hspan/vspan）、
    ///   calculateStripSize、expandStrip、incColSize/incRowSize、distributeStripSize
    ///   （余数给**最后一根**可扩展条）、onResize 的 align 落格——数值逐行；
    /// - ButtonSet = app::ButtonSet : ui::Grid（button_set.cpp:243-257）——条目 align
    ///   HORIZONTAL|VERTICAL，gap = buttonset 样式 gap-rows=-3 / gap-columns=-1
    ///   （theme.xml:1070），childSpacing 0（noBorderNoChildSpacing）；
    /// - 控件 hint 统一走 theme.cpp calcWidgetMetrics 口径（border + padding + text/icon
    ///   对齐合并，theme.cpp:782-829）：Button/Check/Buttonset 条目/Separator/Label；
    /// - Entry hint = entry.cpp:479-493（w 受 kMaxWidthHintForEntry=400 夹）；
    /// - XML 属性语义 = app::WidgetLoader（widget_loader.cpp:551-685）：expansive /
    ///   homogeneous / visible / width·minwidth·maxwidth·height… / border / childspacing /
    ///   box horizontal·vertical（缺省=既非横也非纵→走 else 的纵轴）/ boxfiller / grid columns /
    ///   cell_hspan·cell_vspan·cell_align（convert_align_value_to_flags 全 token，base.h 位值）。
    ///
    /// 【已知偏差（登记）】字体用 FusionPixel 位图档非 Aseprite Mini，故文本宽用本地估宽、
    /// 行盒用 <see cref="LineH"/> 常量为 Mini lineHeight 的替身；style 自带的 min/max 尺寸
    /// （如 dir_item width/height）未解析（只解析元素级 width/height/minwidth…）；
    /// combobox hint 仍近似（源 ComboBox 复合件未逐行）；窗框高度含本端标题带 15。
    /// </summary>
    public static class AseDialogLoader
    {
        /// <summary>装载结果：窗根 + id→控件字典（xml 的 id 属性，供"cmd 层"接线）。</summary>
        public sealed class Result
        {
            public RectTransform Window;
            public readonly Dictionary<string, Component> ById = new Dictionary<string, Component>();
            internal readonly Dictionary<string, AwNode> NodeById = new Dictionary<string, AwNode>();
            internal AwNode Root;
            internal float InnerW;

            public T Get<T>(string id) where T : class
            {
                return ById.TryGetValue(id, out Component c) ? c as T : null;
            }

            /// <summary>盒当前是否隐藏（cmd 层切换用）。</summary>
            public bool IsHidden(string id)
            {
                return NodeById.TryGetValue(id, out AwNode node) && node.Hidden;
            }

            /// <summary>隐藏/显示一个盒并**重排收窗高**（隐藏的盒不占高度——旧版只 SetActive
            /// 留空洞，new_sprite 的 advanced 即受害者）。</summary>
            public void SetHidden(string id, bool hidden)
            {
                if (!NodeById.TryGetValue(id, out AwNode node))
                    return;
                node.Hidden = hidden;
                if (node.Host != null)
                    node.Host.gameObject.SetActive(!hidden);
                Reflow();
            }

            /// <summary>按当前可见子件重跑布局与窗高（cmd 层动态改显隐后调用）。</summary>
            public void Reflow()
            {
                if (Window == null || Root == null)
                    return;
                float contentH = Root.Measure().y;
                Root.Layout(Mathf.RoundToInt(DebugWindowKit.Pad),
                    Mathf.RoundToInt(DebugWindowKit.ContentTop),
                    Mathf.RoundToInt(InnerW), Mathf.RoundToInt(contentH));
                Window.sizeDelta = new Vector2(InnerW + DebugWindowKit.Pad * 2f,
                    DebugWindowKit.ContentTop + contentH + DebugWindowKit.Pad);   // 窗高须含顶部 inset（内容自 ContentTop 起排）
            }
        }

        /// <summary>装载 data/widgets/&lt;widgetName&gt;.xml 为可交互窗。</summary>
        public static Result Load(string widgetName, Transform overlay, Vector2 topLeft)
        {
            TextAsset xmlAsset = Resources.Load<TextAsset>("AseWidgets/" + widgetName);
            if (xmlAsset == null)
            {
                Debug.LogError("[AseDialogLoader] 找不到声明资产 AseWidgets/" + widgetName
                    + "（菜单 PirateCrew/同步 Aseprite widgets 资产）");
                return null;
            }

            XDocument doc;
            try { doc = XDocument.Parse(xmlAsset.text); }
            catch (System.Exception e)
            {
                Debug.LogError("[AseDialogLoader] xml 解析失败 " + widgetName + "：" + e.Message);
                return null;
            }
            XElement win = doc.Root?.Element("window");
            if (win == null)
            {
                Debug.LogError("[AseDialogLoader] " + widgetName + " 无 <window> 根件。");
                return null;
            }

            var ctx = new BuildContext { WidgetName = widgetName, Result = new Result() };
            string title = ctx.Text(win.Attribute("text")?.Value, fallback: widgetName);
            RectTransform window = DebugWindowKit.CreateWindow(overlay, "AseDlg_" + widgetName, title,
                topLeft, Vector2.zero, closeButton: true, helpButton: win.Attribute("help") != null);
            ctx.Window = window;
            ctx.RegisterId(win.Attribute("id")?.Value, window);

            // 内容 = window 下第一个盒；无盒则直接排 window 子件
            XElement content = FirstLayoutChild(win) ?? win;
            AwNode root = BuildNode(content, window, ctx);
            if (root == null)
                root = new BoxNode { Horizontal = false, Host = window };   // 无盒可排（内容为空）

            // 两遍：Measure（自然尺寸）→ Layout（按窗内宽落位）——同 aseprite 先 sizeHint 后 setBounds
            Vector2 hint = root.Measure();
            float innerW = Mathf.Max(MinInnerW, hint.x);
            float contentH = hint.y;
            root.Layout(Mathf.RoundToInt(DebugWindowKit.Pad),
                Mathf.RoundToInt(DebugWindowKit.ContentTop),
                Mathf.RoundToInt(innerW), Mathf.RoundToInt(contentH));
            window.sizeDelta = new Vector2(innerW + DebugWindowKit.Pad * 2f,
                DebugWindowKit.ContentTop + contentH + DebugWindowKit.Pad);   // 窗高须含顶部 inset
            ctx.Result.Window = window;
            ctx.Result.Root = root;      // cmd 层 SetHidden/Reflow 用
            ctx.Result.InnerW = innerW;
            return ctx.Result;
        }

        const float MinInnerW = 150f;

        // ------------------------------------------------------------------
        // 本地字体度量替身（Aseprite Mini 位图字 → 本地 FusionPixel 位图档；
        // 字体偏差已在交接档登记，这里只把"行盒/字符宽"落成常量以便布局逐行对应）
        // ------------------------------------------------------------------

        /// <summary>本地 Tiny 位图字行盒（Aseprite Mini lineHeight 的替身）。</summary>
        const float LineH = 8f;
        /// <summary>本地 "w" 字符估宽（Aseprite mini font->textLength("w") 的替身）。</summary>
        const float WChar = 5f;
        /// <summary>2 × getCaretSize().w（skin_theme.cpp:1312：2*guiscale()）。</summary>
        const float Caret2 = 4f;
        /// <summary>entry.cpp:41 kMaxWidthHintForEntry。</summary>
        const float MaxEntryHintW = 400f;
        /// <summary>ui::Box/Grid 默认 childSpacing（skin_theme.cpp:1154/1174：4*guiscale）。</summary>
        const int DefaultChildSpacing = 4;

        // ------------------------------------------------------------------
        // ui::align 位标志（src/ui/base.h:41-49）——不是自造的 0x1/0x2/0x4
        // ------------------------------------------------------------------

        const int HORIZONTAL = 0x00010000;
        const int VERTICAL = 0x00020000;
        const int LEFT = 0x00040000;
        const int CENTER = 0x00080000;
        const int RIGHT = 0x00100000;
        const int TOP = 0x00200000;
        const int MIDDLE = 0x00400000;
        const int BOTTOM = 0x00800000;

        static int AlignH(int a) => a & (LEFT | CENTER | RIGHT);
        static int AlignV(int a) => a & (TOP | MIDDLE | BOTTOM);

        /// <summary>C++ 整型除法（截断向零）——guiscaled_div/除法同口径。</summary>
        static float IDiv(float a, float b)
        {
            if (Mathf.Approximately(b, 0f))
                return 0f;
            return (int)(a / b);
        }

        /// <summary>guiscaled_center(p, s1, s2)，guiscale=1（scale.h）。</summary>
        static float Center(float p, float s1, float s2)
        {
            return p + ((int)s1 / 2) - ((int)s2 / 2);
        }

        static float Sign(float v) => v < 0f ? -1f : 1f;

        static XElement FirstLayoutChild(XElement el)
        {
            foreach (XElement child in el.Elements())
            {
                string n = child.Name.LocalName;
                if (n == "box" || n == "vbox" || n == "hbox" || n == "boxfiller" || n == "grid")
                    return child;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 布局域模型（ui::Widget 的移植载体）
        // ------------------------------------------------------------------

        /// <summary>布局节点：盒（box/grid）或叶子控件。Measure/Layout 分别移植
        /// sizeHint/onResize；叶子带 UGUI 件与 hint 委托。</summary>
        internal abstract class AwNode
        {
            /// <summary>minSize（widget_loader.cpp:653-668：width→min=max，minwidth…）。</summary>
            public float MinW, MinH;
            /// <summary>maxSize（同上；缺省 INT_MAX）。</summary>
            public float MaxW = int.MaxValue, MaxH = int.MaxValue;
            public bool Hidden;
            /// <summary>widget isExpansive()（widget_loader.cpp:627-628 / BoxFiller）。</summary>
            public bool Expansive;
            /// <summary>盒/格宿主件（叶子=控件本体；置 Hidden 时宿主随动失活）。</summary>
            public RectTransform Host;

            /// <summary>未夹 min/max 的 sizeHint（盒=Box/Grid::onSizeHint 移植；叶子=控件 hint）。</summary>
            internal abstract Vector2 RawMeasure(int fitW, int fitH);

            /// <summary>sizeHint(fitIn)（widget.cpp:1480-1493：结果夹 [min,max]）。</summary>
            public Vector2 Measure(int fitW, int fitH)
            {
                Vector2 r = RawMeasure(fitW, fitH);
                return new Vector2(Mathf.Clamp(r.x, MinW, MaxW), Mathf.Clamp(r.y, MinH, MaxH));
            }

            /// <summary>sizeHint()（fitIn = (0,0)）。</summary>
            public Vector2 Measure() => Measure(0, 0);

            /// <summary>setBounds(x,y,w,h)（绝对坐标，相对窗内容原点）。</summary>
            public abstract void Layout(int x, int y, int w, int h);
        }

        internal sealed class LeafNode : AwNode
        {
            public RectTransform Rect;
            public System.Func<Vector2> Hint;
            /// <summary>布局后的内件跟随回调（分隔线铺到格宽）。</summary>
            public System.Action<float> Resize;

            internal override Vector2 RawMeasure(int fitW, int fitH) => Hint();

            public override void Layout(int x, int y, int w, int h)
            {
                Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0f, 1f);
                Rect.anchoredPosition = new Vector2(x, -y);
                Rect.sizeDelta = new Vector2(w, h);
                Resize?.Invoke(w);
            }
        }

        /// <summary>盒（box.cpp 移植）。Horizontal=横向排布；Homogeneous=等分；
        /// Expansive 是**子件级**标志（与 Kids 平行）。</summary>
        internal sealed class BoxNode : AwNode
        {
            /// <summary>ui::Box align 的 HORIZONTAL 位；false 走 else 分支（纵轴）。</summary>
            public bool Horizontal;
            public bool Homogeneous;
            /// <summary>skin_theme.cpp:1154：Box childSpacing = 4*guiscale。</summary>
            public int ChildSpacing = DefaultChildSpacing;
            /// <summary>xml border 属性（widget_loader.cpp:643-646）。</summary>
            public int Border;
            public readonly List<AwNode> Kids = new List<AwNode>();
            public readonly List<bool> Expansive2 = new List<bool>();

            // Box::onSizeHint（box.cpp:33-91）：ADD_CHILD_SIZE / FINAL_ADJUSTMENT 宏逐行。
            internal override Vector2 RawMeasure(int fitW, int fitH)
            {
                int visibleChildren = 0;
                for (int i = 0; i < Kids.Count; i++)
                    if (!Kids[i].Hidden)
                        visibleChildren++;

                float prefW = 0f, prefH = 0f;
                float fitInW = Horizontal ? fitW : 0f;
                float fitInH = Horizontal ? 0f : fitH;

                for (int i = 0; i < Kids.Count; i++)
                {
                    if (Kids[i].Hidden)
                        continue;
                    Vector2 childSize = Kids[i].Measure(Mathf.RoundToInt(fitInW), Mathf.RoundToInt(fitInH));
                    if (Horizontal)
                    {
                        if (Homogeneous) prefW = Mathf.Max(prefW, childSize.x);
                        else prefW += childSize.x;
                        prefH = Mathf.Max(prefH, childSize.y);
                        fitInW = Mathf.Max(0f, fitInW - prefW);   // ADD_CHILD_SIZE
                    }
                    else
                    {
                        if (Homogeneous) prefH = Mathf.Max(prefH, childSize.y);
                        else prefH += childSize.y;
                        prefW = Mathf.Max(prefW, childSize.x);
                        fitInH = Mathf.Max(0f, fitInH - prefH);   // ADD_CHILD_SIZE(h,w)
                    }
                }

                if (visibleChildren > 0)
                {
                    if (Horizontal)
                    {
                        if (Homogeneous) prefW *= visibleChildren;  // FINAL_ADJUSTMENT
                        prefW += ChildSpacing * (visibleChildren - 1);
                    }
                    else
                    {
                        if (Homogeneous) prefH *= visibleChildren;
                        prefH += ChildSpacing * (visibleChildren - 1);
                    }
                }
                prefW += 2 * Border;   // box.cpp:87-88 border
                prefH += 2 * Border;
                return new Vector2(prefW, prefH);
            }

            // Box::onResize LAYOUT_CHILDREN（box.cpp:95-140，主轴/跨轴参数化）。
            public override void Layout(int x, int y, int w, int h)
            {
                int visibleChildren = 0, expansiveChildren = 0;
                for (int i = 0; i < Kids.Count; i++)
                    if (!Kids[i].Hidden)
                    {
                        visibleChildren++;
                        if (Expansive2[i]) expansiveChildren++;
                    }
                if (visibleChildren == 0)
                    return;

                float innerW = w - 2 * Border;
                float innerH = h - 2 * Border;
                float availMain = Horizontal ? innerW : innerH;
                float crossSize = Horizontal ? innerH : innerW;
                // box.cpp:154-161：availSize = childrenBounds().size(); prefSize = sizeHint(availSize) - border
                Vector2 pref = Measure(Mathf.RoundToInt(innerW), Mathf.RoundToInt(innerH));
                float prefMain = (Horizontal ? pref.x : pref.y) - 2 * Border;
                float availExtraSize = availMain - prefMain;
                availMain -= ChildSpacing * (visibleChildren - 1);

                float homogeneousSize = 0f;
                if (Homogeneous)
                    homogeneousSize = IDiv(availMain, visibleChildren);   // box.cpp:99-100

                float mainPos = (Horizontal ? x : y) + Border;    // defChildPos
                float crossStart = (Horizontal ? y : x) + Border;

                int i2 = 0, j = 0;
                for (int k = 0; k < Kids.Count; k++)
                {
                    AwNode kid = Kids[k];
                    if (kid.Hidden)
                        continue;

                    float size;
                    if (Homogeneous)
                    {
                        size = i2 < visibleChildren - 1 ? homogeneousSize : availMain;   // 末件吃余
                    }
                    else
                    {
                        // size = child->sizeHint(availSize).<main>
                        Vector2 m = Horizontal
                            ? kid.Measure(Mathf.RoundToInt(availMain), Mathf.RoundToInt(crossSize))
                            : kid.Measure(Mathf.RoundToInt(crossSize), Mathf.RoundToInt(availMain));
                        size = Horizontal ? m.x : m.y;

                        if (Expansive2[k])
                        {
                            float extraSize = IDiv(availExtraSize, expansiveChildren - j);
                            size += extraSize;
                            availExtraSize -= extraSize;
                            if (++j == expansiveChildren)
                                size += availExtraSize;
                        }
                        else
                        {
                            float natural = Horizontal ? kid.Measure().x : kid.Measure().y;
                            availExtraSize -= size - natural;
                        }
                    }

                    size = Mathf.Clamp(size, Horizontal ? kid.MinW : kid.MinH, Horizontal ? kid.MaxW : kid.MaxH);
                    float cross = Mathf.Clamp(crossSize, Horizontal ? kid.MinH : kid.MinW,
                        Horizontal ? kid.MaxH : kid.MaxW);
                    if (Horizontal)
                        kid.Layout(Mathf.RoundToInt(mainPos), Mathf.RoundToInt(crossStart),
                            Mathf.RoundToInt(size), Mathf.RoundToInt(cross));
                    else
                        kid.Layout(Mathf.RoundToInt(crossStart), Mathf.RoundToInt(mainPos),
                            Mathf.RoundToInt(cross), Mathf.RoundToInt(size));

                    mainPos += size + ChildSpacing;   // defChildPos.<main> += size + childSpacing
                    availMain -= size;
                    i2++;
                }
            }
        }

        /// <summary>网格（grid.cpp 全文移植：Cell 占位 + 条带 + 扩展 + 分配 + 落格）。
        /// 单 span/多 span、cell_align 位标志、distributeStripSize 余数给最后一根可扩展条。</summary>
        internal sealed class GridNode : AwNode
        {
            public int Columns = 2;
            public bool SameWidthColumns;
            public int ChildSpacing = DefaultChildSpacing;
            public int Border;
            /// <summary>m_colgap（Style::gap().w）。</summary>
            public int ColGap;
            /// <summary>m_rowgap（Style::gap().h）。</summary>
            public int RowGap;

            public readonly List<AwNode> Cells = new List<AwNode>();
            public readonly List<int> SpanH = new List<int>();
            public readonly List<int> SpanV = new List<int>();
            public readonly List<int> Align2 = new List<int>();

            struct GCell
            {
                public int Child;          // kid index or -1
                public int PRow, PCol;     // parent cell anchor (-1 = 本格/空格)
                public int Hspan, Vspan, Align;
                public float W, H;         // Cell::w/h（calculateStripSize/expandStrip 工作变量）
            }

            List<GCell[]> _rows = new List<GCell[]>();
            int _rowCount;
            float[] _colSize = new float[0];
            int[] _colExpand = new int[0];
            float[] _rowSize = new float[0];
            int[] _rowExpand = new int[0];

            public void AddCell(AwNode node, int hspan, int vspan, int align)
            {
                Cells.Add(node);
                SpanH.Add(Mathf.Max(1, hspan));
                SpanV.Add(Mathf.Max(1, vspan));
                Align2.Add(align);
            }

            // --- putWidgetInCell / expandRows（grid.cpp:497-575） ---

            void GrowRows(int rows)
            {
                while (_rowCount < rows)
                {
                    var row = new GCell[Columns];
                    for (int c = 0; c < Columns; c++)
                        row[c].Child = -1;
                    _rows.Add(row);
                    _rowCount++;
                }
            }

            void PlaceAll()
            {
                _rows = new List<GCell[]>();
                _rowCount = 0;
                for (int i = 0; i < Cells.Count; i++)
                {
                    int hs = SpanH[i], vs = SpanV[i], al = Align2[i];
                    if (!TryPlace(i, hs, vs, al))
                    {
                        GrowRows(_rowCount + 1);
                        TryPlace(i, hs, vs, al);
                    }
                }
            }

            bool TryPlace(int i, int hspan, int vspan, int align)
            {
                for (int r = 0; r < _rowCount; r++)
                {
                    for (int c = 0; c < Columns; c++)
                    {
                        if (_rows[r][c].Child != -1)
                            continue;

                        GCell anchor = _rows[r][c];
                        anchor.Child = i;
                        anchor.PRow = -1;
                        anchor.PCol = -1;
                        anchor.Hspan = hspan;
                        anchor.Vspan = vspan;
                        anchor.Align = align;
                        _rows[r][c] = anchor;

                        int colbeg = c;
                        int colend = Mathf.Min(c + hspan, Columns);
                        int rowend = r + vspan;
                        GrowRows(rowend);

                        for (int cc = c + 1; cc < colend; cc++)
                        {
                            GCell cell = _rows[r][cc];
                            cell.Child = i;
                            cell.PRow = r;
                            cell.PCol = c;
                            cell.Hspan = colend - cc;
                            cell.Vspan = rowend - r;
                            _rows[r][cc] = cell;
                        }
                        for (int rr = r + 1; rr < rowend; rr++)
                            for (int cc = colbeg; cc < colend; cc++)
                            {
                                GCell cell = _rows[rr][cc];
                                cell.Child = i;
                                cell.PRow = r;
                                cell.PCol = c;
                                cell.Hspan = colend - cc;
                                cell.Vspan = rowend - rr;
                                _rows[rr][cc] = cell;
                            }
                        return true;
                    }
                }
                return false;
            }

            // --- calculateSize / calculateStripSize / expandStrip（grid.cpp:279-424） ---

            void CalculateSize()
            {
                PlaceAll();
                _colSize = new float[Columns];
                _colExpand = new int[Columns];
                _rowSize = new float[_rowCount];
                _rowExpand = new int[_rowCount];
                if (_rowCount == 0)
                    return;
                CalcStrip(true);
                CalcStrip(false);
                ExpandStrip(true);
                ExpandStrip(false);

                if (SameWidthColumns)
                {
                    float maxW = 0f;
                    for (int c = 0; c < Columns; c++)
                        maxW = Mathf.Max(maxW, _colSize[c]);
                    for (int c = 0; c < Columns; c++)
                        _colSize[c] = maxW;
                }
            }

            void CalcStrip(bool forCols)
            {
                int outer = forCols ? Columns : _rowCount;   // colstrip.size
                int inner = forCols ? _rowCount : Columns;   // rowstrip.size
                int alignFlag = forCols ? HORIZONTAL : VERTICAL;

                for (int a = 0; a < outer; a++)
                {
                    int expand = 0;
                    int b = 0;
                    while (b < inner)
                    {
                        GCell cell = forCols ? _rows[b][a] : _rows[a][b];
                        if (cell.Child != -1)
                        {
                            if (cell.PRow < 0)
                            {
                                if (!Cells[cell.Child].Hidden)
                                {
                                    Vector2 req = Cells[cell.Child].Measure();   // sizeHint()
                                    cell.W = req.x - (cell.Hspan - 1) * (ChildSpacing + ColGap);
                                    cell.H = req.y - (cell.Vspan - 1) * (ChildSpacing + RowGap);
                                    if ((cell.Align & alignFlag) == alignFlag)
                                        expand++;
                                }
                                else
                                    cell.W = cell.H = 0f;
                            }
                            else
                            {
                                if (!Cells[cell.Child].Hidden)
                                {
                                    int pa = _rows[cell.PRow][cell.PCol].Align;
                                    if ((pa & alignFlag) == alignFlag)
                                        expand++;
                                }
                            }
                            if (forCols) _rows[b][a] = cell;
                            else _rows[a][b] = cell;
                            b += forCols ? cell.Vspan : cell.Hspan;   // row += span-1; ++row
                        }
                        else
                            b++;
                    }
                    if (forCols)
                    {
                        _colSize[a] = 0f;
                        _colExpand[a] = expand;
                    }
                    else
                    {
                        _rowSize[a] = 0f;
                        _rowExpand[a] = expand;
                    }
                }
            }

            void ExpandStrip(bool forCols)
            {
                bool moreSpan;
                int currentSpan = 1;
                do
                {
                    moreSpan = false;
                    int outer = forCols ? Columns : _rowCount;
                    int inner = forCols ? _rowCount : Columns;
                    for (int a = 0; a < outer; a++)
                    {
                        for (int b = 0; b < inner; b++)
                        {
                            GCell cell;
                            float cellSize;
                            int cellSpan;
                            if (forCols)
                            {
                                cell = _rows[b][a];
                                cellSize = cell.W;
                                cellSpan = cell.Hspan;
                            }
                            else
                            {
                                cell = _rows[a][b];
                                cellSize = cell.H;
                                cellSpan = cell.Vspan;
                            }

                            if (cell.Child == -1 || cell.PRow >= 0 || cellSize <= 0f)
                                continue;

                            if (cellSpan == currentSpan)
                            {
                                int limit = Mathf.Min(a + cellSpan, outer);
                                int maxExpand = 0;
                                for (int i = a; i < limit; i++)
                                    maxExpand = Mathf.Max(maxExpand, forCols ? _colExpand[i] : _rowExpand[i]);

                                int expand = 0, lastExpand = 0;
                                for (int i = a; i < limit; i++)
                                {
                                    int ec = forCols ? _colExpand[i] : _rowExpand[i];
                                    if (ec == maxExpand)
                                    {
                                        expand++;
                                        lastExpand = i;
                                    }
                                }

                                float size = IDiv(cellSize, expand);   // guiscaled_div
                                for (int i = a; i < limit; i++)
                                {
                                    int ec = forCols ? _colExpand[i] : _rowExpand[i];
                                    if (ec != maxExpand)
                                        continue;
                                    if (lastExpand == i)
                                        size = forCols ? cell.W : cell.H;   // 最后一根吃本格全宽
                                    if (forCols) IncColSize(i, size);
                                    else IncRowSize(i, size);
                                }
                            }
                            else if (cellSpan > currentSpan)
                                moreSpan = true;
                        }
                    }
                    currentSpan++;
                } while (moreSpan);
            }

            void IncColSize(int col, float size)
            {
                _colSize[col] += size;
                int r = 0;
                while (r < _rowCount)
                {
                    GCell cell = _rows[r][col];
                    if (cell.Child != -1)
                    {
                        if (cell.PRow >= 0)
                        {
                            GCell parent = _rows[cell.PRow][cell.PCol];
                            parent.W -= size;
                            _rows[cell.PRow][cell.PCol] = parent;
                        }
                        else
                        {
                            cell.W -= size;
                            _rows[r][col] = cell;
                        }
                        r += cell.Vspan;
                    }
                    else
                        r++;
                }
            }

            void IncRowSize(int row, float size)
            {
                _rowSize[row] += size;
                int c = 0;
                while (c < Columns)
                {
                    GCell cell = _rows[row][c];
                    if (cell.Child != -1)
                    {
                        if (cell.PRow >= 0)
                        {
                            GCell parent = _rows[cell.PRow][cell.PCol];
                            parent.H -= size;
                            _rows[cell.PRow][cell.PCol] = parent;
                        }
                        else
                        {
                            cell.H -= size;
                            _rows[row][c] = cell;
                        }
                        c += cell.Hspan;
                    }
                    else
                        c++;
                }
            }

            // --- sumStripSize / calculateCellSize / distributeStripSize（grid.cpp:247-495） ---

            float SumStrip(bool forCols)
            {
                float gap = forCols ? ColGap : RowGap;
                int n = forCols ? Columns : _rowCount;
                float size = 0f;
                int j = 0;
                for (int i = 0; i < n; i++)
                {
                    float s = forCols ? _colSize[i] : _rowSize[i];
                    if (s > 0f)
                    {
                        size += s;
                        if (++j > 1)
                            size += ChildSpacing + gap;
                    }
                }
                return size;
            }

            float CalcCellSize(int start, int span, bool forCols)
            {
                float gap = forCols ? ColGap : RowGap;
                int n = forCols ? Columns : _rowCount;
                int limit = Mathf.Min(start + span, n);
                float size = 0f;
                int j = 0;
                for (int i = start; i < limit; i++)
                {
                    float s = forCols ? _colSize[i] : _rowSize[i];
                    if (s > 0f)
                    {
                        size += s;
                        if (++j > 1)
                            size += ChildSpacing + gap;
                    }
                }
                return size;
            }

            void DistributeSize(int rectW, int rectH)
            {
                if (_rowCount == 0)
                    return;
                DistributeStripSize(true, rectW, 2 * Border, SameWidthColumns);
                DistributeStripSize(false, rectH, 2 * Border, false);
            }

            void DistributeStripSize(bool forCols, float rectSize, float borderSize, bool sameWidth)
            {
                float gap = forCols ? ColGap : RowGap;
                int n = forCols ? Columns : _rowCount;
                float[] size = forCols ? _colSize : _rowSize;
                int[] expand = forCols ? _colExpand : _rowExpand;

                int maxExpandCount = 0;
                for (int i = 0; i < n; i++)
                    maxExpandCount = Mathf.Max(maxExpandCount, expand[i]);

                float totalReq = 0f;
                int wantmore = 0;
                int j = 0;
                for (int i = 0; i < n; i++)
                {
                    if (size[i] > 0f)
                    {
                        totalReq += size[i];
                        if (++j > 1)
                            totalReq += ChildSpacing + gap;
                    }
                    if (expand[i] == maxExpandCount || sameWidth)
                        wantmore++;
                }
                totalReq += borderSize;
                float extraTotal = rectSize - totalReq;

                if (wantmore > 0 &&
                    ((extraTotal > 0f && (maxExpandCount > 0 || sameWidth)) || extraTotal < 0f))
                {
                    for (int i = 0; i < n; i++)
                        if (size[i] == 0f && (expand[i] == maxExpandCount || sameWidth))
                            extraTotal -= Sign(extraTotal) * (ChildSpacing + gap);

                    float extraEach = IDiv(extraTotal, wantmore);
                    for (int i = 0; i < n; i++)
                    {
                        if (expand[i] == maxExpandCount || sameWidth)
                        {
                            size[i] += extraEach;
                            extraTotal -= extraEach;
                            if (--wantmore == 0)
                            {
                                size[i] += extraTotal;   // 余数给最后一根（grid.cpp:485-488）
                                extraTotal = 0f;
                            }
                        }
                    }
                }
            }

            // --- onSizeHint / onResize（grid.cpp:157-245） ---

            internal override Vector2 RawMeasure(int fitW, int fitH)
            {
                CalculateSize();
                float w = SumStrip(true) + 2 * Border;
                float h = SumStrip(false) + 2 * Border;
                return new Vector2(w, h);
            }

            public override void Layout(int x, int y, int w, int h)
            {
                CalculateSize();
                DistributeSize(w, h);
                if (_rowCount == 0)
                    return;

                float posY = y + Border;
                for (int r = 0; r < _rowCount; r++)
                {
                    float posX = x + Border;
                    for (int c = 0; c < Columns; c++)
                    {
                        GCell cell = _rows[r][c];
                        if (cell.Child != -1 && cell.PRow < 0 && !Cells[cell.Child].Hidden)
                        {
                            float cx = posX, cy = posY;
                            float cw = CalcCellSize(c, cell.Hspan, true);
                            float ch = CalcCellSize(r, cell.Vspan, false);
                            Vector2 req = Cells[cell.Child].Measure(Mathf.RoundToInt(cw), Mathf.RoundToInt(ch));

                            if ((cell.Align & LEFT) != 0) cw = req.x;
                            else if ((cell.Align & CENTER) != 0) { cx = Center(cx, cw, req.x); cw = req.x; }
                            else if ((cell.Align & RIGHT) != 0) { cx += cw - req.x; cw = req.x; }

                            if ((cell.Align & TOP) != 0) ch = req.y;
                            else if ((cell.Align & MIDDLE) != 0) { cy = Center(cy, ch, req.y); ch = req.y; }
                            else if ((cell.Align & BOTTOM) != 0) { cy += ch - req.y; ch = req.y; }

                            if (cx + cw > x + w - Border)
                                cw = x + w - Border - cx;
                            if (cy + ch > y + h - Border)
                                ch = y + h - Border - cy;

                            if (ColGap < 0 && c + cell.Hspan - 1 < Columns - 1)
                                cw += ColGap;
                            if (RowGap < 0 && r + cell.Vspan - 1 < _rowCount - 1)
                                ch += RowGap;

                            Cells[cell.Child].Layout(Mathf.RoundToInt(cx), Mathf.RoundToInt(cy),
                                Mathf.RoundToInt(cw), Mathf.RoundToInt(ch));
                        }

                        if (_colSize[c] > 0f)
                            posX += _colSize[c] + ChildSpacing + ColGap;
                    }
                    if (_rowSize[r] > 0f)
                        posY += _rowSize[r] + ChildSpacing + RowGap;
                }
            }
        }

        // ------------------------------------------------------------------
        // 构建：xml → AwNode（控件即刻建好，尺寸由 hint/Layout 管）
        // ------------------------------------------------------------------

        sealed class BuildContext
        {
            public string WidgetName;
            public Result Result;
            public RectTransform Window;

            /// <summary>@.key → 本 widget 节；@section.key → 跨节；@general.x → [general]。</summary>
            public string Text(string raw, string fallback = null)
            {
                if (string.IsNullOrEmpty(raw))
                    return fallback ?? string.Empty;
                if (!raw.StartsWith("@"))
                    return raw.Replace("&", string.Empty);
                string body = raw.Substring(1);
                int cut = body.IndexOf('.');
                if (cut < 0)
                    return fallback ?? raw;
                string section = cut == 0 ? WidgetName : body.Substring(0, cut);
                string value = AseStrings.Get(section, body.Substring(cut + 1));
                return string.IsNullOrEmpty(value) ? fallback ?? raw : value.Replace("&", string.Empty);
            }

            public void RegisterId(string id, Component c)
            {
                if (!string.IsNullOrEmpty(id) && c != null)
                    Result.ById[id] = c;
            }

            public void RegisterNode(string id, AwNode node)
            {
                if (!string.IsNullOrEmpty(id) && node != null)
                    Result.NodeById[id] = node;
            }
        }

        static AwNode BuildNode(XElement el, RectTransform parent, BuildContext ctx)
        {
            AwNode node;
            switch (el.Name.LocalName)
            {
                case "box":
                case "vbox":
                case "hbox":
                case "boxfiller":
                    node = BuildBox(el, parent, ctx);
                    break;
                case "grid":
                    node = BuildGrid(el, parent, ctx);
                    break;
                case "separator":
                    node = BuildSeparator(el, parent, ctx);
                    break;
                case "label":
                    node = BuildLabel(el, parent, ctx);
                    break;
                case "entry":
                case "expr":
                    node = BuildEntry(el, parent, ctx);
                    break;
                case "check":
                    node = BuildCheck(el, parent, ctx);
                    break;
                case "buttonset":
                    node = BuildButtonset(el, parent, ctx);
                    break;
                case "combobox":
                    node = BuildCombobox(el, parent, ctx);
                    break;
                case "button":
                    node = BuildButton(el, parent, ctx);
                    break;
                default:
                    Debug.LogWarning("[AseDialogLoader] 未实现语义 <" + el.Name.LocalName
                        + ">（" + ctx.WidgetName + "）跳过——覆盖表登记");
                    return null;
            }
            if (node == null)
                return null;
            ApplyXmlFlags(node, el);
            ApplySizeAttrs(node, el);
            return node;
        }

        /// <summary>widget_loader.cpp:551-651 的通用属性：visible/expansive（+boxfiller）。</summary>
        static void ApplyXmlFlags(AwNode node, XElement el)
        {
            if (Attr(el, "visible") == "false")
            {
                node.Hidden = true;
                if (node.Host != null)
                    node.Host.gameObject.SetActive(false);
            }
            if (el.Name.LocalName == "boxfiller" || Attr(el, "expansive") == "true")
                node.Expansive = true;
        }

        /// <summary>widget_loader.cpp:653-668：width→min=max=width；min/max 缺侧回自然 hint/INT_MAX。</summary>
        static void ApplySizeAttrs(AwNode node, XElement el)
        {
            string width = Attr(el, "width"), height = Attr(el, "height");
            string minw = Attr(el, "minwidth"), minh = Attr(el, "minheight");
            string maxw = Attr(el, "maxwidth"), maxh = Attr(el, "maxheight");
            if (width != null) { if (minw == null) minw = width; if (maxw == null) maxw = width; }
            if (height != null) { if (minh == null) minh = height; if (maxh == null) maxh = height; }
            if (minw == null && minh == null && maxw == null && maxh == null)
                return;

            Vector2 raw = node.RawMeasure(0, 0);
            node.MinW = SizeAttr(minw, raw.x);
            node.MinH = SizeAttr(minh, raw.y);
            node.MaxW = Mathf.Max(node.MinW, SizeAttr(maxw, int.MaxValue));
            node.MaxH = Mathf.Max(node.MinH, SizeAttr(maxh, int.MaxValue));
        }

        static float SizeAttr(string s, float fallback)
        {
            return s != null && int.TryParse(s, out int v) && v > 0 ? v : fallback;
        }

        static AwNode BuildBox(XElement el, RectTransform parent, BuildContext ctx)
        {
            string n = el.Name.LocalName;
            bool horizontal;
            if (n == "hbox" || n == "boxfiller")
                horizontal = true;
            else if (n == "vbox")
                horizontal = false;
            else
                // widget_loader.cpp:122-131：<box> 只认 horizontal/vertical；两者皆无 → align 无横位
                // → Box::onResize 走 else 分支（纵轴）。
                horizontal = Attr(el, "horizontal") == "true";

            RectTransform host = UiKit.CreateRect(
                el.Attribute("id")?.Value ?? (horizontal ? "HBox" : "VBox"), parent);
            ctx.RegisterId(el.Attribute("id")?.Value, host);
            UiLayout.Ignore(host.gameObject);

            var box = new BoxNode
            {
                Horizontal = horizontal,
                Homogeneous = Attr(el, "homogeneous") == "true",
                ChildSpacing = DefaultChildSpacing,   // skin_theme.cpp:1154
                Border = IntAttr(el, "border", 0),
                Host = host,
            };
            // noborders（widget_loader.cpp:639-641）先归零；childspacing 属性后置覆盖。
            if (Attr(el, "noborders") == "true")
                box.ChildSpacing = 0;
            if (Attr(el, "childspacing") != null)
                box.ChildSpacing = IntAttr(el, "childspacing", box.ChildSpacing);
            box.Expansive = n == "boxfiller";
            ctx.RegisterNode(el.Attribute("id")?.Value, box);

            foreach (XElement child in el.Elements())
            {
                AwNode node = BuildNode(child, host, ctx);
                if (node == null)
                    node = new LeafNode { Rect = CreateDummy(host), Hint = () => Vector2.zero, Hidden = true };
                box.Kids.Add(node);
                box.Expansive2.Add(node.Expansive);
            }
            return box;
        }

        static RectTransform CreateDummy(RectTransform host)
        {
            RectTransform dummy = UiKit.CreateRect("Skipped", host);
            UiLayout.Ignore(dummy.gameObject);
            dummy.gameObject.SetActive(false);
            return dummy;
        }

        static AwNode BuildGrid(XElement el, RectTransform parent, BuildContext ctx)
        {
            int columns = IntAttr(el, "columns", 0);
            if (columns <= 0)
            {
                // widget_loader.cpp:250-257：无 columns 不建 Grid（元素被丢弃）。
                Debug.LogWarning("[AseDialogLoader] <grid> 缺 columns（" + ctx.WidgetName + "）跳过");
                return null;
            }

            RectTransform host = UiKit.CreateRect(el.Attribute("id")?.Value ?? "Grid", parent);
            ctx.RegisterId(el.Attribute("id")?.Value, host);
            UiLayout.Ignore(host.gameObject);

            var grid = new GridNode
            {
                Columns = columns,
                SameWidthColumns = Attr(el, "same_width_columns") == "true",
                ChildSpacing = DefaultChildSpacing,   // skin_theme.cpp:1174
                Border = IntAttr(el, "border", 0),
                Host = host,
            };
            if (Attr(el, "noborders") == "true")
                grid.ChildSpacing = 0;
            if (Attr(el, "childspacing") != null)
                grid.ChildSpacing = IntAttr(el, "childspacing", grid.ChildSpacing);
            ctx.RegisterNode(el.Attribute("id")?.Value, grid);

            foreach (XElement child in el.Elements())
            {
                AwNode node = BuildNode(child, host, ctx);
                if (node == null)
                {
                    grid.AddCell(new LeafNode { Rect = CreateDummy(host), Hint = () => Vector2.zero, Hidden = true },
                        1, 1, 0);
                    continue;
                }
                int hspan = IntAttr(child, "cell_hspan", 1);
                int vspan = IntAttr(child, "cell_vspan", 1);
                int align = EncodeAlign(Attr(child, "cell_align"));   // convert_align_value_to_flags
                grid.AddCell(node, hspan, vspan, align);
            }
            return grid;
        }

        /// <summary>convert_align_value_to_flags（widget_loader.cpp:740-777）全 token；
        /// 位值与 ui::align 一致（base.h:41-49）。</summary>
        static int EncodeAlign(string attr)
        {
            int flags = 0;
            if (string.IsNullOrEmpty(attr))
                return flags;
            foreach (string tok in attr.Split(' '))
            {
                switch (tok)
                {
                    case "horizontal": flags |= HORIZONTAL; break;
                    case "vertical": flags |= VERTICAL; break;
                    case "left": flags |= LEFT; break;
                    case "center": flags |= CENTER; break;
                    case "right": flags |= RIGHT; break;
                    case "top": flags |= TOP; break;
                    case "middle": flags |= MIDDLE; break;
                    case "bottom": flags |= BOTTOM; break;
                    case "homogeneous": flags |= 0x01000000; break;
                }
            }
            return flags;
        }

        static AwNode BuildSeparator(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            bool hasText = !string.IsNullOrEmpty(text);
            // theme horizontal_separator：border=2；文字层 x=4 align left middle；
            // background-border part separator_horz（9×5 无切片 → calcWidgetMetrics 记入 iconHint）。
            float textW = hasText ? TextWidth(text) : 0f;
            float textH = hasText ? LineH : 0f;
            Vector2 hint = CalcHint(
                2, 2, 2, 2,   // borderHint = style border 2（applyOnlyDefinedBorders）
                0, 0, 0, 0,   // padding 0
                textW + 4f, textH, LEFT | MIDDLE,
                9f, 5f, CENTER | MIDDLE);

            if (hasText)
            {
                RectTransform host = UiKit.CreateRect("Sep", parent);
                UiLayout.Ignore(host.gameObject);
                float titleW = Mathf.Ceil(textW);
                var label = DebugWindowKit.Label(host, text, UiSkin.Font.Tiny,
                    PixelSkin.Theme.SeparatorLabel, TMPro.TextAlignmentOptions.Left);
                RectTransform lr = label.rectTransform;
                lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 0.5f);
                lr.anchoredPosition = new Vector2(4f, 0f);   // style text x=4
                lr.sizeDelta = new Vector2(titleW + 2f, LineH);
                float lineX = 4f + titleW + 2f;
                RectTransform line = (RectTransform)SketchSeparator.Create(host, "Line",
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(lineX, 0f), new Vector2(100f, 1f),
                    SketchSeparator.Direction.Horizontal).transform;
                return new LeafNode
                {
                    Rect = host,
                    Hint = () => hint,
                    Resize = w => line.sizeDelta = new Vector2(Mathf.Max(1f, w - lineX), 1f),
                };
            }

            RectTransform lineHost = UiKit.CreateRect("SepLine", parent);
            UiLayout.Ignore(lineHost.gameObject);
            RectTransform plain = (RectTransform)SketchSeparator.Create(lineHost, "Line",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(100f, 1f),
                SketchSeparator.Direction.Horizontal).transform;
            return new LeafNode
            {
                Rect = lineHost,
                Hint = () => hint,
                Resize = w => plain.sizeDelta = new Vector2(w, 1f),
            };
        }

        static AwNode BuildLabel(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            var label = DebugWindowKit.Label(parent, text, UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, TMPro.TextAlignmentOptions.Left);
            label.gameObject.name = "Label_" + (el.Attribute("id")?.Value ?? text);
            ctx.RegisterId(el.Attribute("id")?.Value, label);
            // theme label：padding=1，text align left；Widget::onSizeHint → calcWidgetMetrics。
            Vector2 hint = CalcHint(
                0, 0, 0, 0,
                1, 1, 1, 1,
                TextWidth(text), LineH, LEFT,
                0f, 0f, CENTER | MIDDLE);
            return new LeafNode
            {
                Rect = label.rectTransform,
                Hint = () => hint,
            };
        }

        static AwNode BuildEntry(XElement el, RectTransform parent, BuildContext ctx)
        {
            bool numeric = el.Name.LocalName == "expr";
            string suffix = Attr(el, "suffix");
            // entry.cpp:479-493：trailing = max(textLength(suffix), 2*caret.w)；
            // w = textLength("w") * min(maxsize,6) + trailing + border.width()，夹 400。
            int maxsize = numeric ? 1024 : IntAttr(el, "maxsize", 6);   // ExprEntry 默认 1024
            int chars = Mathf.Min(maxsize, 6);
            float borderW = PartBorder("sunken_normal").x + PartBorder("sunken_normal").z;
            float borderH = PartBorder("sunken_normal").y + PartBorder("sunken_normal").w;
            float suffixW = string.IsNullOrEmpty(suffix) ? 0f : TextWidth(suffix);
            float w = WChar * chars + Mathf.Max(suffixW, Caret2) + borderW;
            w = Mathf.Min(w, MaxEntryHintW);
            float h = LineH + borderH;

            TMP_InputField.ContentType type = numeric
                ? TMP_InputField.ContentType.IntegerNumber
                : TMP_InputField.ContentType.Standard;
            TMP_InputField field = AseWidgetKit.SunkenEntry(parent,
                el.Attribute("id")?.Value ?? "Entry", 0f, 0f, w, string.Empty, type, suffix);
            ctx.RegisterId(el.Attribute("id")?.Value, field);
            return new LeafNode
            {
                Rect = (RectTransform)field.transform,
                Hint = () => new Vector2(w, h),
            };
        }

        static AwNode BuildCheck(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            Button check = AseWidgetKit.CheckRow(parent, text, 0f, 0f, false, null);
            ctx.RegisterId(el.Attribute("id")?.Value, check);
            // theme check_box：border=2；text align left middle x=14；icon check_normal 8×8 x=2。
            // calcWidgetMetrics：text/icon 横对齐相同 → w += max(textW+14, 8+2)；纵相同 → h += max(textH,8)。
            Vector2 hint = CalcHint(
                2, 2, 2, 2,
                0, 0, 0, 0,
                TextWidth(text) + 14f, LineH, LEFT | MIDDLE,
                10f, 8f, LEFT | MIDDLE);
            return new LeafNode
            {
                Rect = (RectTransform)check.transform,
                Hint = () => hint,
            };
        }

        static AwNode BuildButtonset(XElement el, RectTransform parent, BuildContext ctx)
        {
            int columns = IntAttr(el, "columns", 0);
            if (columns <= 0)
            {
                Debug.LogWarning("[AseDialogLoader] <buttonset> 缺 columns（" + ctx.WidgetName + "）跳过");
                return null;
            }

            RectTransform host = UiKit.CreateRect(el.Attribute("id")?.Value ?? "ButtonSet", parent);
            ctx.RegisterId(el.Attribute("id")?.Value, host);
            UiLayout.Ignore(host.gameObject);

            // ButtonSet : Grid（button_set.cpp:194-208）：noBorderNoChildSpacing + buttonset 样式
            // gap-rows=-3 / gap-columns=-1（theme.xml:1070）。
            var grid = new GridNode
            {
                Columns = columns,
                SameWidthColumns = false,
                ChildSpacing = 0,      // noBorderNoChildSpacing
                Border = 0,
                ColGap = -1,
                RowGap = -3,
                Host = host,
            };
            ctx.RegisterNode(el.Attribute("id")?.Value, grid);

            var controls = new List<Button>();
            foreach (XElement itemEl in el.Elements("item"))
            {
                string text = ctx.Text(itemEl.Attribute("text")?.Value);
                string icon = itemEl.Attribute("icon")?.Value;
                int hspan = IntAttr(itemEl, "hspan", 1);
                int vspan = IntAttr(itemEl, "vspan", 1);
                bool hasIcon = !string.IsNullOrEmpty(icon) && PixelSkin.Ase(icon) != null;
                bool hasText = !string.IsNullOrEmpty(text);

                float textW = TextWidth(text);
                Vector2 iconSize = hasIcon ? PixelSkin.Ase(icon).rect.size : Vector2.zero;

                Button item;
                Vector2 hint;
                if (hasIcon)
                {
                    var iconItem = SketchButtonSetIcon.Create(host, "Item_" + text, text, icon,
                        Vector2.zero, new Vector2(60f, 36f));
                    item = iconItem;
                    if (hasText)
                    {
                        // buttonset_item_text_top_icon_bottom：border 3/5 + padding-top2/bottom1，
                        // text align top / icon align bottom（theme.xml:1108-1122）。
                        hint = CalcHint(3, 3, 3, 5, 0, 2, 0, 1,
                            textW, LineH, TOP, iconSize.x, iconSize.y, BOTTOM);
                    }
                    else
                    {
                        // buttonset_item_icon：仅 icon（theme.xml:1081-1085）。
                        hint = CalcHint(3, 3, 3, 5, 0, 0, 0, 0,
                            0f, 0f, CENTER | MIDDLE, iconSize.x, iconSize.y, CENTER | MIDDLE);
                    }
                }
                else
                {
                    var textItem = SketchButtonSet.Create(host, "Item_" + text, text,
                        DebugWindowKit.HandFont, UiSkin.Font.Tiny,
                        Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(60f, 16f));
                    item = textItem;
                    // buttonset_item_text：border 3/5 + padding=1（theme.xml:1098-1107）。
                    hint = CalcHint(3, 3, 3, 5, 1, 1, 1, 1,
                        textW, LineH, CENTER | MIDDLE, 0f, 0f, CENTER | MIDDLE);
                }

                controls.Add(item);
                grid.AddCell(new LeafNode
                {
                    Rect = (RectTransform)item.transform,
                    Hint = () => hint,
                }, hspan, vspan, HORIZONTAL | VERTICAL);   // button_set.cpp:257
            }

            // 互斥单选（buttonset 语义）
            for (int i = 0; i < controls.Count; i++)
            {
                int captured = i;
                controls[i].onClick.AddListener(() =>
                {
                    for (int j = 0; j < controls.Count; j++)
                        SetActive(controls[j], j == captured);
                });
            }
            if (controls.Count > 0)
                SetActive(controls[0], true);
            ctx.RegisterId(el.Attribute("id")?.Value, controls.Count > 0 ? controls[0] : null);
            return grid;
        }

        static void SetActive(Button item, bool on)
        {
            if (item is SketchButtonSet s)
                s.Active = on;
            else if (item is SketchButtonSetIcon i)
                i.Active = on;
        }

        static AwNode BuildCombobox(XElement el, RectTransform parent, BuildContext ctx)
        {
            var options = new List<string>();
            foreach (XElement li in el.Elements("listitem"))
                options.Add(ctx.Text(li.Attribute("text")?.Value));
            if (options.Count == 0)
                options.Add(string.Empty);
            // combobox.cpp:438-452 onSizeHint × entry.cpp:455-467 sizeHintWithText：
            // w = max(选项文字宽) + 2×光标宽(2px×2) + 词条边框宽 + 箭头钮宽，夹 kMaxWidthHintForEntry(400)。
            Vector4 cb = PartBorder("sunken2_normal");
            float maxTextW = 0f;
            foreach (string opt in options)
                maxTextW = Mathf.Max(maxTextW, TextWidth(opt));
            float w = Mathf.Min(maxTextW + 4f + cb.x + cb.z + AseComboBox.ButtonWidth, 400f);
            TextMeshProUGUI value = AseWidgetKit.ComboBox(parent,
                el.Attribute("id")?.Value ?? "Combo", 0f, 0f, w, options.ToArray(), 0,
                popupOverlay: ctx.Window.parent);
            ctx.RegisterId(el.Attribute("id")?.Value, value);
            return new LeafNode
            {
                Rect = (RectTransform)value.transform.parent,
                Hint = () => new Vector2(w, LineH + cb.y + cb.w),   // 源：lineHeight + entry border().height()
            };
        }

        static AwNode BuildButton(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            // theme button：border = button_normal 切片 4/4/4/6；text align center middle。
            Vector4 b = PartBorder("button_normal");
            Vector2 hint = CalcHint(b.x, b.w, b.z, b.y, 0, 0, 0, 0,
                TextWidth(text), LineH, CENTER | MIDDLE, 0f, 0f, CENTER | MIDDLE);
            var button = SketchButton.Create(parent, "Btn_" + text,
                Vector2.zero, Vector2.zero, Vector2.zero, hint,
                DebugWindowKit.HandFont, text, UiSkin.Font.Tiny);
            ctx.RegisterId(el.Attribute("id")?.Value, button);
            if (Attr(el, "closewindow") == "true")
            {
                RectTransform windowRoot = ctx.Window;
                button.onClick.AddListener(() => windowRoot.gameObject.SetActive(false));
            }
            return new LeafNode
            {
                Rect = (RectTransform)button.transform,
                Hint = () => hint,
            };
        }

        // ------------------------------------------------------------------
        // 度量与件表
        // ------------------------------------------------------------------

        static string Attr(XElement el, string name) => el?.Attribute(name)?.Value;

        static int IntAttr(XElement el, string name, int fallback)
        {
            string v = Attr(el, name);
            return v != null && int.TryParse(v, out int n) ? n : fallback;
        }

        /// <summary>theme.cpp calcWidgetMetrics（:782-829）的合并口径：
        /// sizeHint = border + padding + text/icon（对齐轴相同则取 max，不同则相加）。</summary>
        static Vector2 CalcHint(
            float bl, float bt, float br, float bb,
            float pl, float pt, float pr, float pb,
            float textW, float textH, int textAlign,
            float iconW, float iconH, int iconAlign)
        {
            float w = bl + br + pl + pr;
            float h = bt + bb + pt + pb;
            if (AlignH(textAlign) == AlignH(iconAlign))
                w += Mathf.Max(textW, iconW);
            else
                w += textW + iconW;
            if (AlignV(textAlign) == AlignV(iconAlign))
                h += Mathf.Max(textH, iconH);
            else
                h += textH + iconH;
            return new Vector2(w, h);
        }

        /// <summary>TMP 位图字估宽（CJK 全宽 8 / ASCII 5——同 AseMenuKit 口径）。</summary>
        static float TextWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            int wide = 0, narrow = 0;
            foreach (char c in text)
            {
                if (c > 0x2E80) wide++;
                else narrow++;
            }
            return wide * UiSkin.Font.Tiny + narrow * 5f;
        }

        static Vector4 PartBorder(string partId)
        {
            Sprite s = PixelSkin.Ase(partId);
            return s != null ? s.border : Vector4.zero;   // x=左 y=下 z=右 w=上
        }
    }

    /// <summary>data/strings/en.ini 的节解析（[section] key=value；\n 转义还原；# ; 注释）。
    /// 与声明 xml 同源拷贝为 AseWidgets/en.ini.txt（AseWidgetAssetSync）。</summary>
    internal static class AseStrings
    {
        static Dictionary<string, Dictionary<string, string>> _map;
        static bool _parsed;

        public static string Get(string section, string key)
        {
            EnsureParsed();
            if (_map != null && _map.TryGetValue(section, out var entries)
                && entries.TryGetValue(key, out string value))
                return value;
            return null;
        }

        static void EnsureParsed()
        {
            if (_parsed)
                return;
            _parsed = true;
            // Resources 路径去掉最后扩展名：盘上 en.ini.txt → 资源名 en.ini
            _map = Parse(Resources.Load<TextAsset>("AseWidgets/en.ini")?.text);
        }

        static Dictionary<string, Dictionary<string, string>> Parse(string ini)
        {
            var map = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrEmpty(ini))
            {
                Debug.LogWarning("[AseStrings] en.ini.txt 未找到——@ 文案引用将回退原串");
                return map;
            }
            string section = null;
            foreach (string raw in ini.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    if (!map.ContainsKey(section))
                        map[section] = new Dictionary<string, string>();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || section == null)
                    continue;
                map[section][line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
            }
            return map;
        }
    }
}
