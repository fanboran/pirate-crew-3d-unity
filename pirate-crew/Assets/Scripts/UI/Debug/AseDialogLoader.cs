using System.Collections.Generic;
using System.Xml.Linq;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// **Aseprite widgets.xml 通用装载器**（创始人裁决「以库为源·原封复刻」；2026-09-26 走查后
    /// 升级为**逐行移植版**）：布局算法不再自造近似——Box::onSizeHint/onResize
    /// （src/ui/box.cpp:33-165）与 Grid 的条带/扩展/对齐算法（src/ui/grid.cpp:160-420）
    /// 按源码结构移植到 <see cref="AwNode"/> 树，控件边框取 theme 背景件九宫切片
    /// （本地图集 Sprite.border 即切片）。
    ///
    /// 【与源码的对应】
    /// - AwNode = ui::Widget 布局域（min/max/border/childSpacing/hint）；
    /// - Measure = Box::onSizeHint 宏（homogeneous 取最大×n + 间距；纵轴取最大）；
    /// - Layout = Box::onResize 的 LAYOUT_CHILDREN 宏（homogeneous 等分末件吃余数；
    ///   expansive 摊余宽；**跨轴子件拉伸到盒宽并夹 [min,max]**——box.cpp:129-131）；
    /// - Grid = 条带取列/行最大 hint、cell_align=horizontal 记扩展列分余宽、
    ///   cell_align=right 在格内右对齐（grid.cpp:176-214）。
    /// 【已实现的控件 hint】Widget 默认（text+border）· Button（默认+minwidth）· Entry/Expr
    /// （entry.cpp:479-491：字符宽×min(maxsize,6)+后缀+边框，行高+边框）· Separator
    /// （separator.cpp:35-60：文字时左右 style 边加倍）· Buttonset 件（图文变体 = 边框+
    /// 字+2+16 图标+1）· Check（图标 8+4+文字——**近似**：源未单独覆写 onSizeHint）。
    /// 【已知偏差】字用 FusionPixel 位图档非 Aseprite Mini；窗框高度含本端标题带 15。
    /// 未实现语义遇 LogWarning 跳过，覆盖表在交接档 §三之五。
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

            /// <summary>隐藏/显示一个盒并**重排收窗高**（隐藏的盒不占高度——
            /// 旧版只 SetActive 留空洞，new_sprite 的 advanced 即受害者）。</summary>
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
                float contentH = Root.Layout(new Rect(DebugWindowKit.Pad, DebugWindowKit.ContentTop,
                    InnerW, Root.Measure().y));
                Window.sizeDelta = new Vector2(InnerW + DebugWindowKit.Pad * 2f,
                    contentH + DebugWindowKit.Pad);
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

            // 两遍：Measure（自然尺寸）→ Layout（按窗内宽落位）——同 aseprite 先 sizeHint 后 setBounds
            Vector2 hint = root.Measure();
            float innerW = Mathf.Max(MinInnerW, hint.x);
            float contentH = root.Layout(new Rect(DebugWindowKit.Pad, DebugWindowKit.ContentTop,
                innerW, hint.y));
            window.sizeDelta = new Vector2(innerW + DebugWindowKit.Pad * 2f,
                contentH + DebugWindowKit.Pad);
            ctx.Result.Window = window;
            ctx.Result.Root = root;      // cmd 层 SetHidden/Reflow 用
            ctx.Result.InnerW = innerW;
            return ctx.Result;
        }

        const float MinInnerW = 150f;
        const float EntryCharsW = 6f;       // entry.cpp: min(maxsize, 6) 个 "w" 字符宽
        const float CheckIconGap = 4f;      // 近似：图标 8 + 4 缝 + 文字

        static XElement FirstLayoutChild(XElement el)
        {
            foreach (XElement child in el.Elements())
            {
                string n = child.Name.LocalName;
                if (n == "box" || n == "vbox" || n == "hbox" || n == "grid")
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
            public Vector2 Min = Vector2.zero;
            public Vector2 Max = new Vector2(9999f, 9999f);
            public bool Hidden;
            /// <summary>盒/格宿主件（叶子=控件本体；置 Hidden 时宿主随动失活）。</summary>
            public RectTransform Host;

            /// <summary>尺寸提示（叶子=控件 hint；盒=Box/Grid::onSizeHint 移植）。</summary>
            public abstract Vector2 Measure();

            /// <summary>在 rect 内落位（盒=onResize 移植；叶子=拉伸到 rect 并夹 min/max），
            /// 返回实际占用高（叶子）。</summary>
            public abstract float Layout(Rect rect);

            /// <summary>跨轴夹取（box.cpp:129-131：子件跨轴取盒尺寸并 clamp [min,max]）。</summary>
            protected static float ClampCross(float size, AwNode n, bool horizontalAxis)
            {
                float min = horizontalAxis ? n.Min.y : n.Min.x;
                float max = horizontalAxis ? n.Max.y : n.Max.x;
                return Mathf.Clamp(size, min, max);
            }
        }

        internal sealed class LeafNode : AwNode
        {
            public RectTransform Rect;
            public System.Func<Vector2> Hint;
            /// <summary>布局后的内件跟随回调（分隔线铺到格宽）。</summary>
            public System.Action<float> Resize;

            public override Vector2 Measure() => Hint();

            public override float Layout(Rect rect)
            {
                Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0f, 1f);
                Rect.anchoredPosition = new Vector2(Mathf.Round(rect.x), -Mathf.Round(rect.y));
                Rect.sizeDelta = new Vector2(Mathf.Round(rect.width), Mathf.Round(rect.height));
                Resize?.Invoke(rect.width);
                return rect.height;
            }
        }

        /// <summary>盒（box.cpp 移植）。Horizontal=横向排布；Homogeneous=等分；
        /// expansive 是**子件级**标志（Expansive 列表与 Kids 平行）。</summary>
        internal sealed class BoxNode : AwNode
        {
            public bool Horizontal;
            public bool Homogeneous;
            public float ChildSpacing;
            public readonly List<AwNode> Kids = new List<AwNode>();
            public readonly List<bool> Expansive = new List<bool>();

            IEnumerable<AwNode> Visible
            {
                get
                {
                    for (int i = 0; i < Kids.Count; i++)
                        if (!Kids[i].Hidden)
                            yield return Kids[i];
                }
            }

            // Box::onSizeHint（box.cpp:33-83）：
            // 横向：homogeneous 取最大（×n）否则累加；间距×(n-1)；纵向取最大。纵向盒轴互换。
            public override Vector2 Measure()
            {
                float main = 0f, cross = 0f, max = 0f;
                int n = 0;
                foreach (AwNode kid in Visible)
                {
                    Vector2 h = kid.Measure();
                    if (Horizontal)
                    {
                        if (Homogeneous) max = Mathf.Max(max, h.x);
                        else main += h.x;
                        cross = Mathf.Max(cross, h.y);
                    }
                    else
                    {
                        if (Homogeneous) max = Mathf.Max(max, h.y);
                        else main += h.y;
                        cross = Mathf.Max(cross, h.x);
                    }
                    n++;
                }
                if (Homogeneous)
                    main = max * n;
                main += ChildSpacing * Mathf.Max(0, n - 1);
                return Horizontal ? new Vector2(main, cross) : new Vector2(cross, main);
            }

            // Box::onResize LAYOUT_CHILDREN（box.cpp:102-145，轴参数化移植）：
            // 余宽 = avail − hint；homogeneous 等分（末件吃余数）；expansive 摊余宽；
            // 跨轴 = 盒宽 clamp[min,max]；主轴步进 size+spacing。
            public override float Layout(Rect rect)
            {
                List<AwNode> kids = new List<AwNode>();
                List<bool> expansive = new List<bool>();
                for (int i = 0; i < Kids.Count; i++)
                    if (!Kids[i].Hidden)
                    {
                        kids.Add(Kids[i]);
                        expansive.Add(Expansive[i]);
                    }
                int n = kids.Count;
                if (n == 0)
                    return 0f;

                float availMain = Horizontal ? rect.width : rect.height;
                float hintMain = Measure()[
                    Horizontal ? 0 : 1];
                float extra = availMain - hintMain;
                float avail = availMain - ChildSpacing * (n - 1);
                float homogeneousEach = 0f;
                if (Homogeneous)
                    homogeneousEach = Mathf.Floor(avail / n);

                float mainPos = Horizontal ? rect.x : rect.y;
                int expansiveCount = 0;
                for (int i = 0; i < n; i++)
                    if (expansive[i]) expansiveCount++;

                for (int i = 0; i < n; i++)
                {
                    float size;
                    if (Homogeneous)
                        size = i < n - 1 ? homogeneousEach : avail;   // 末件吃余数（box.cpp:117-121）
                    else
                    {
                        size = kids[i].Measure()[Horizontal ? 0 : 1];
                        if (expansive[i])
                        {
                            // 摊余宽：整除分摊，末个 expansive 吃剩余（box.cpp:126-133）
                            int seen = 0;
                            for (int j = 0; j < i; j++)
                                if (expansive[j]) seen++;
                            float share = Mathf.Floor(extra / Mathf.Max(1, expansiveCount - seen));
                            size += share;
                            extra -= share;
                            if (seen + 1 == expansiveCount)
                                size += extra;
                        }
                    }

                    Rect cell = Horizontal
                        ? new Rect(mainPos, rect.y, size, ClampCross(rect.height, kids[i], true))
                        : new Rect(rect.x, mainPos, ClampCross(rect.width, kids[i], false), size);
                    kids[i].Layout(cell);
                    mainPos += size + ChildSpacing;
                    avail -= size;
                }
                return rect.height;
            }
        }

        /// <summary>网格（grid.cpp:160-420 移植，单 span 子集）：条带取列/行最大 hint；
        /// cell_align=horizontal 记扩展列分余宽；cell_align=right/left 在格内右/左对齐；
        /// 无对齐位的子件**整格拉伸**（grid.cpp:186-214 默认分支）。</summary>
        internal sealed class GridNode : AwNode
        {
            public int Columns = 2;
            public float ChildSpacing;
            public readonly List<AwNode> Cells = new List<AwNode>();
            public readonly List<int> CellAlign = new List<int>();   // 位 0x1 RIGHT / 0x2 左待扩 / 0x4 HORIZONTAL(expand)
            List<float> _colW = new List<float>(), _rowH = new List<float>();
            List<int> _colExpand = new List<int>();

            const int AlignRight = 0x1, AlignLeft = 0x2, AlignHorizontal = 0x4;

            public static int EncodeAlign(string attr)
            {
                int a = 0;
                if (string.IsNullOrEmpty(attr)) return a;
                if (attr.Contains("right")) a |= AlignRight;
                if (attr.Contains("left")) a |= AlignLeft;
                if (attr.Contains("horizontal")) a |= AlignHorizontal;
                return a;
            }

            // Grid::calculateSize + expandStrip（span=1 子集）：条带 = 该列/行最大 hint；
            // 扩展计数来自 cell_align&HORIZONTAL。
            void CalculateStrips()
            {
                _colW = new List<float>(new float[Columns]);
                _colExpand = new List<int>(new int[Columns]);
                int rows = (Cells.Count + Columns - 1) / Columns;
                _rowH = new List<float>(new float[rows]);
                for (int i = 0; i < Cells.Count; i++)
                {
                    if (Cells[i].Hidden) continue;
                    Vector2 h = Cells[i].Measure();
                    int c = i % Columns, r = i / Columns;
                    _colW[c] = Mathf.Max(_colW[c], h.x);
                    _rowH[r] = Mathf.Max(_rowH[r], h.y);
                    if ((CellAlign[i] & AlignHorizontal) != 0)
                        _colExpand[c]++;
                }
            }

            public override Vector2 Measure()
            {
                CalculateStrips();
                float w = 0f, h = 0f;
                foreach (float cw in _colW) w += cw;
                foreach (float rh in _rowH) h += rh;
                w += ChildSpacing * Mathf.Max(0, _colW.Count - 1);
                h += ChildSpacing * Mathf.Max(0, _rowH.Count - 1);
                return new Vector2(w, h);
            }

            // Grid::distributeSize（扩展列分余宽）+ onResize 落格（右/左对齐按 hint 收窄）。
            public override float Layout(Rect rect)
            {
                Vector2 hint = Measure();
                float extraW = rect.width - hint.x;
                int expandCols = 0;
                foreach (int e in _colExpand) if (e > 0) expandCols++;
                if (expandCols > 0 && extraW > 0f)
                    for (int c = 0; c < _colW.Count; c++)
                        if (_colExpand[c] > 0)
                            _colW[c] += Mathf.Floor(extraW / expandCols);

                float y = rect.y;
                int rows = _rowH.Count;
                for (int r = 0; r < rows; r++)
                {
                    float x = rect.x;
                    for (int c = 0; c < Columns; c++)
                    {
                        int i = r * Columns + c;
                        if (i < Cells.Count && !Cells[i].Hidden)
                        {
                            float w = _colW[c], h = _rowH[r];
                            float cx = x, cy = y;
                            Vector2 req = Cells[i].Measure();
                            int align = i < CellAlign.Count ? CellAlign[i] : 0;
                            if ((align & AlignRight) != 0) { cx += w - req.x; w = req.x; }
                            else if ((align & AlignLeft) != 0) { w = req.x; }
                            Cells[i].Layout(new Rect(cx, cy, w, h));
                        }
                        x += _colW[c] + ChildSpacing;
                    }
                    y += _rowH[r] + ChildSpacing;
                }
                return y - ChildSpacing;
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
            switch (el.Name.LocalName)
            {
                case "box":
                case "vbox":
                case "hbox":
                    return BuildBox(el, parent, ctx);
                case "grid":
                    return BuildGrid(el, parent, ctx);
                case "separator":
                    return BuildSeparator(el, parent, ctx);
                case "label":
                    return BuildLabel(el, parent, ctx);
                case "entry":
                case "expr":
                    return BuildEntry(el, parent, ctx);
                case "check":
                    return BuildCheck(el, parent, ctx);
                case "buttonset":
                    return BuildButtonset(el, parent, ctx);
                case "combobox":
                    return BuildCombobox(el, parent, ctx);
                case "button":
                    return BuildButton(el, parent, ctx);
                default:
                    Debug.LogWarning("[AseDialogLoader] 未实现语义 <" + el.Name.LocalName
                        + ">（" + ctx.WidgetName + "）跳过——覆盖表登记");
                    return null;
            }
        }

        static AwNode BuildBox(XElement el, RectTransform parent, BuildContext ctx)
        {
            string n = el.Name.LocalName;
            bool vertical = n == "vbox" || (n == "box" && Attr(el, "vertical") == "true");
            RectTransform host = UiKit.CreateRect(
                el.Attribute("id")?.Value ?? (vertical ? "VBox" : "HBox"), parent);
            ctx.RegisterId(el.Attribute("id")?.Value, host);
            UiLayout.Ignore(host.gameObject);

            var box = new BoxNode
            {
                Horizontal = !vertical,
                Homogeneous = Attr(el, "homogeneous") == "true",
                ChildSpacing = 0f,     // ui::Box 默认 childspacing=0（widget.cpp）
                Host = host,
            };
            ctx.RegisterNode(el.Attribute("id")?.Value, box);

            foreach (XElement child in el.Elements())
            {
                // 空的 expansive 盒 = 弹性空位：按源码是**可见的零尺寸件**（参与摊宽、
                // 自身不渲染）——隐藏会被布局跳过，右沉语义就丢了。
                bool isSpacer = Attr(child, "expansive") == "true" && !HasLayoutChildren(child);
                AwNode node;
                if (isSpacer)
                {
                    RectTransform spacerHost = UiKit.CreateRect("Spacer", host);
                    UiLayout.Ignore(spacerHost.gameObject);
                    node = new LeafNode { Rect = spacerHost, Hint = () => Vector2.zero };
                }
                else
                {
                    node = BuildNode(child, host, ctx);
                    if (node == null)
                        node = new LeafNode { Rect = CreateDummy(host), Hint = () => Vector2.zero };
                }
                box.Kids.Add(node);
                box.Expansive.Add(isSpacer || Attr(child, "expansive") == "true");
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

        static bool HasLayoutChildren(XElement el)
        {
            foreach (XElement child in el.Elements())
            {
                string n = child.Name.LocalName;
                if (n != "tooltip")
                    return true;
            }
            return false;
        }

        static AwNode BuildGrid(XElement el, RectTransform parent, BuildContext ctx)
        {
            var grid = new GridNode
            {
                Columns = int.TryParse(Attr(el, "columns"), out int cols) ? Mathf.Max(1, cols) : 2,
            };
            foreach (XElement child in el.Elements())
            {
                AwNode node = BuildNode(child, parent, ctx);
                if (node == null)
                {
                    grid.Cells.Add(new LeafNode { Hidden = true });
                    grid.CellAlign.Add(0);
                    continue;
                }
                grid.Cells.Add(node);
                grid.CellAlign.Add(GridNode.EncodeAlign(Attr(child, "cell_align")));
            }
            return grid;
        }

        static AwNode BuildSeparator(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            if (!string.IsNullOrEmpty(text))
            {
                RectTransform host = UiKit.CreateRect("Sep", parent);
                UiLayout.Ignore(host.gameObject);
                float titleW = Mathf.Ceil(TextWidth(text));
                var label = DebugWindowKit.Label(host, text, UiSkin.Font.Tiny,
                    PixelSkin.Theme.SeparatorLabel, TMPro.TextAlignmentOptions.Left);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax =
                    label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.anchoredPosition = new Vector2(2f, 0f);
                label.rectTransform.sizeDelta = new Vector2(titleW + 4f, 12f);
                RectTransform line = (RectTransform)SketchSeparator.Create(host, "Line",
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(2f + titleW + 2f, -6f), new Vector2(100f, 1f),
                    SketchSeparator.Direction.Horizontal).transform;
                // separator.cpp:35-60：文字时宽 = textWidth（左右 style 边加倍≈+4）
                return new LeafNode
                {
                    Rect = host,
                    Hint = () => new Vector2(titleW + 8f, 12f),
                    Resize = w => line.sizeDelta = new Vector2(Mathf.Max(4f, w - titleW - 6f), 1f),
                };
            }

            RectTransform lineHost = UiKit.CreateRect("SepLine", parent);
            UiLayout.Ignore(lineHost.gameObject);
            RectTransform plain = (RectTransform)SketchSeparator.Create(lineHost, "Line",
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(100f, 1f),
                SketchSeparator.Direction.Horizontal).transform;
            return new LeafNode
            {
                Rect = lineHost,
                Hint = () => new Vector2(0f, 1f),     // 无文字分隔线：横贯（宽由格/盒给）
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
            // Widget 默认 hint = 文字尺寸 + style padding(label=1)×2
            return new LeafNode
            {
                Rect = label.rectTransform,
                Hint = () => new Vector2(TextWidth(text) + 2f, 12f),
            };
        }

        static AwNode BuildEntry(XElement el, RectTransform parent, BuildContext ctx)
        {
            bool numeric = el.Name.LocalName == "expr";
            string suffix = Attr(el, "suffix");
            // entry.cpp:486：w = "w"字符宽 × min(maxsize,6) + max(后缀宽, 2×光标宽) + 边框
            float borderW = PartBorderWidth("sunken");
            float suffixW = string.IsNullOrEmpty(suffix) ? 0f : TextWidth(suffix);
            float w = 5f * EntryCharsW + Mathf.Max(suffixW, 4f) + borderW;
            float h = 12f + PartBorderHeight("sunken");

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
            // 近似（覆盖表登记）：图标 8 + 缝 4 + 文字 + padding 1×2
            return new LeafNode
            {
                Rect = (RectTransform)check.transform,
                Hint = () => new Vector2(8f + CheckIconGap + TextWidth(text) + 2f, 16f),
            };
        }

        static AwNode BuildButtonset(XElement el, RectTransform parent, BuildContext ctx)
        {
            var box = new BoxNode { Horizontal = true, Homogeneous = true, ChildSpacing = 0f };
            var controls = new List<Button>();
            foreach (XElement itemEl in el.Elements("item"))
            {
                string text = ctx.Text(itemEl.Attribute("text")?.Value);
                string icon = itemEl.Attribute("icon")?.Value;
                Button item;
                float hintW, hintH;
                if (!string.IsNullOrEmpty(icon))
                {
                    var iconItem = SketchButtonSetIcon.Create(parent,
                        "Item_" + text, text, icon, Vector2.zero, new Vector2(60f, 36f));
                    item = iconItem;
                    // 图文变体（theme buttonset_item_text_top_icon_bottom）：边框 3/5 + 字 + 2 + 16 图标 + 1
                    hintW = Mathf.Max(TextWidth(text), 16f) + 6f;
                    hintH = 8f + 2f + 16f + 1f + 8f;
                }
                else
                {
                    var textItem = SketchButtonSet.Create(parent, "Item_" + text, text,
                        DebugWindowKit.HandFont, UiSkin.Font.Tiny,
                        Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(60f, 16f));
                    item = textItem;
                    hintW = TextWidth(text) + 6f;
                    hintH = 16f;
                }
                controls.Add(item);
                box.Kids.Add(new LeafNode
                {
                    Rect = (RectTransform)item.transform,
                    Hint = () => new Vector2(hintW, hintH),
                });
                box.Expansive.Add(false);
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
            return box;
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
            float w = 150f;   // combobox = entry hint + 箭头钮 16（近似定宽，覆盖表登记）
            TextMeshProUGUI value = AseWidgetKit.ComboBox(parent,
                el.Attribute("id")?.Value ?? "Combo", 0f, 0f, w, options.ToArray(), 0,
                popupOverlay: ctx.Window.parent);
            ctx.RegisterId(el.Attribute("id")?.Value, value);
            return new LeafNode
            {
                Rect = (RectTransform)value.transform.parent,
                Hint = () => new Vector2(w, 16f),
            };
        }

        static AwNode BuildButton(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            float minW = float.TryParse(Attr(el, "minwidth"), out float mw) ? mw : 0f;
            Vector4 border = PartBorder("button_normal");
            float hintW = Mathf.Max(minW, TextWidth(text) + border.x + border.z);
            float hintH = 16f;
            var button = SketchButton.Create(parent, "Btn_" + text,
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(hintW, hintH),
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
                Hint = () => new Vector2(hintW, hintH),
            };
        }

        // ------------------------------------------------------------------
        // 度量与件表
        // ------------------------------------------------------------------

        static string Attr(XElement el, string name) => el?.Attribute(name)?.Value;

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

        static float PartBorderWidth(string partId)
        {
            Vector4 b = PartBorder(partId);
            return b.x + b.z;
        }

        static float PartBorderHeight(string partId)
        {
            Vector4 b = PartBorder(partId);
            return b.y + b.w;
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
