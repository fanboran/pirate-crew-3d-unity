using System.Collections.Generic;
using System.Xml.Linq;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// **Aseprite widgets.xml 通用装载器**（创始人 2026-09-25 裁决「以库为源·原封复刻」的承载）：
    /// 解析 Assets/Resources/AseWidgets/&lt;名字&gt;.xml（自 aseprite data/widgets 原封拷贝，
    /// 见 AseWidgetAssetSync），用 Stick 控件库 + AseWidgetKit 实例化成 UGUI——
    /// 布局数字与文案全部来自声明本身，不人工转抄。
    ///
    /// 【盒模型语义（src/ui/box.cpp 实锄）】
    /// - 纵向盒：子件自上而下，跨轴**按内容宽**不拉伸；hbox 的 expansive 空位 / cell_align=right
    ///   表示该行整行占宽并右对齐（onResize 的余宽摊派宏义）。
    /// - 横向盒：homogeneous → 子件等分宽；空盒 expansive → 弹性空位。
    /// 【已实现语义】window(text/help) box/vbox/hbox(vertical/expansive/homogeneous/cell_align)
    /// grid(columns=2) separator(text/left) label entry/expr(suffix/magnet 忽略) buttonset(columns)
    /// item(text/icon/style) check combobox+listitem button(text/minwidth/closewindow)。
    /// 【未实现语义】遇则 LogWarning 跳过不崩——覆盖表登记在
    /// docs/项目/交接/Aseprite观感对齐-交接.md（magnet 焦点序/maxsize/cell_align 水平拉伸等）。
    /// 【已知偏差】字用本端 FusionPixel 位图档而非 Aseprite Mini（字体单轨纪律，待裁决）。
    /// </summary>
    public static class AseDialogLoader
    {
        /// <summary>装载结果：窗根 + id→控件字典（xml 的 id 属性，供"cmd 层"接线）。</summary>
        public sealed class Result
        {
            public RectTransform Window;
            public readonly Dictionary<string, Component> ById = new Dictionary<string, Component>();

            public T Get<T>(string id) where T : class
            {
                return ById.TryGetValue(id, out Component c) ? c as T : null;
            }
        }

        /// <summary>装载 data/widgets/&lt;widgetName&gt;.xml 为可交互窗（一次构建，幂等性由调用方管理）。</summary>
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

            var ctx = new BuildContext
            {
                WidgetName = widgetName,
                Result = new Result(),
            };
            string title = ctx.Text(win.Attribute("text")?.Value, fallback: widgetName);
            RectTransform window = DebugWindowKit.CreateWindow(overlay, "AseDlg_" + widgetName, title,
                topLeft, Vector2.zero, closeButton: true, helpButton: win.Attribute("help") != null);
            ctx.Window = window;
            ctx.RegisterId(win.Attribute("id")?.Value, window);

            // 内容 = window 下第一个盒（<gui><window><box …>）；无盒则直接排 window 子件
            Node root = Build(FirstLayoutChild(win) ?? win, window, ctx);

            float innerW = Mathf.Max(MinInnerW, root.NaturalW);
            float bottom = root.Place(DebugWindowKit.Pad, DebugWindowKit.ContentTop, innerW);
            window.sizeDelta = new Vector2(innerW + DebugWindowKit.Pad * 2f,
                bottom + DebugWindowKit.Pad);
            ctx.Result.Window = window;
            return ctx.Result;
        }

        const float MinInnerW = 150f;
        const float EntryW = 96f;
        const float GridColGap = 4f;
        const float RowGap = 2f;

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
        // 构建：xml 元素 → 布局节点（控件即刻建好，位置由 Place 落）
        // ------------------------------------------------------------------

        sealed class BuildContext
        {
            public string WidgetName;
            public Result Result;
            public RectTransform Window;

            /// <summary>@.key → 本 widget 节；@section.key → 跨节；@general.x → [general]。& 助记符剥除。</summary>
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
        }

        abstract class Node
        {
            public float NaturalW;
            public bool FillW;      // 整行占宽（buttonset / separator / 右对齐 hbox）

            /// <summary>在 (x, y)（父件顶左）按可用宽落位，返回行底 y'。</summary>
            public abstract float Place(float x, float y, float width);
        }

        sealed class VBoxNode : Node
        {
            public readonly List<Node> Children = new List<Node>();

            public override float Place(float x, float y, float width)
            {
                float cursor = y;
                foreach (Node child in Children)
                {
                    float w = child.FillW ? width : child.NaturalW;
                    cursor = child.Place(x, cursor, w) + RowGap;
                }
                return Mathf.Max(y, cursor - RowGap);
            }
        }

        sealed class HBoxNode : Node
        {
            public readonly List<Node> Children = new List<Node>();
            public bool Homogeneous;
            public bool AlignRight;     // expansive 空位 / cell_align=right
            public float RowH;

            public override float Place(float x, float y, float width)
            {
                // 排子件（homogeneous 等分 = 最大子件自然宽——盒本体跨轴不拉伸，box.cpp 语义）
                float each = 0f;
                if (Homogeneous)
                    foreach (Node child in Children)
                        each = Mathf.Max(each, child.NaturalW);

                float cursor = x;
                foreach (Node child in Children)
                {
                    float w = Homogeneous ? each : child.NaturalW;
                    child.Place(cursor, y, w);
                    cursor += w + 4f;   // 子件缝 4（与 NaturalW 累加口径一致）
                }
                float used = Mathf.Max(0f, cursor - x - 4f);

                if (AlignRight && used < width)
                    foreach (Node child in Children)
                        ShiftRight(child, width - used);
                return y + RowH;
            }

            static void ShiftRight(Node node, float dx)
            {
                if (node is LeafNode leaf)
                    leaf.ShiftX(dx);
                else if (node is HBoxNode h)
                    foreach (Node c in h.Children)
                        ShiftRight(c, dx);
                else if (node is GridNode g)
                    foreach (Node c in g.Children)
                        ShiftRight(c, dx);
            }
        }

        sealed class GridNode : Node
        {
            public readonly List<Node> Children = new List<Node>();
            public int Columns = 2;
            public float Col0W;

            public override float Place(float x, float y, float width)
            {
                float cursor = y;
                float col1 = Mathf.Max(EntryW, width - Col0W - GridColGap);
                for (int i = 0; i < Children.Count; i += Columns)
                {
                    float rowY = cursor;
                    for (int c = 0; c < Columns && i + c < Children.Count; c++)
                    {
                        float cx = c == 0 ? x : x + Col0W + GridColGap;
                        float cw = c == 0 ? Col0W : col1;
                        Children[i + c].Place(cx, rowY + 2f, cw);
                    }
                    cursor += 16f;
                }
                return cursor - (Children.Count > 0 ? 2f : 0f);
            }
        }

        sealed class LeafNode : Node
        {
            public RectTransform Rect;
            public float H;
            public bool WSet;       // 控件宽按给定行宽收（按钮/选项块/分隔线），否则用自然宽

            /// <summary>行宽落定后的内件跟随回调（分隔线铺到行宽）。</summary>
            public System.Action<float> Resize;

            public override float Place(float x, float y, float width)
            {
                float w = WSet ? width : Mathf.Max(width, NaturalW);
                Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0f, 1f);
                Rect.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
                Rect.sizeDelta = new Vector2(Mathf.Round(w), H);
                Resize?.Invoke(w);
                return y + H;
            }

            public void ShiftX(float dx)   // 右沉（Place 后平移）
            {
                Rect.anchoredPosition += new Vector2(Mathf.Round(dx), 0f);
            }
        }

        // ------------------------------------------------------------------
        // 元素分派
        // ------------------------------------------------------------------

        static Node Build(XElement el, RectTransform parent, BuildContext ctx)
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

        static Node BuildBox(XElement el, RectTransform parent, BuildContext ctx)
        {
            string n = el.Name.LocalName;
            bool vertical = n == "vbox" || (n == "box" && Attr(el, "vertical") == "true");
            RectTransform host = UiKit.CreateRect(
                el.Attribute("id")?.Value ?? (vertical ? "VBox" : "HBox"), parent);
            ctx.RegisterId(el.Attribute("id")?.Value, host);
            UiLayout.Ignore(host.gameObject);

            var nodes = new List<Node>();
            bool hasExpansiveSpacer = false;
            foreach (XElement child in el.Elements())
            {
                // 空的 expansive 盒 = 弹性空位（右沉语义），不建控件
                if (Attr(child, "expansive") == "true" && !HasLayoutChildren(child))
                {
                    hasExpansiveSpacer = true;
                    continue;
                }
                Node node = Build(child, host, ctx);
                if (node != null)
                    nodes.Add(node);
            }

            if (vertical)
            {
                var vbox = new VBoxNode();
                foreach (Node node in nodes)
                {
                    vbox.Children.Add(node);
                    vbox.NaturalW = Mathf.Max(vbox.NaturalW, node.NaturalW);
                }
                return vbox;
            }

            // 横向盒：homogeneous 等分（= 最大子件自然宽）；expansive 空位 / cell_align=right 右沉
            var hbox = new HBoxNode
            {
                Homogeneous = Attr(el, "homogeneous") == "true",
                AlignRight = Attr(el, "cell_align") == "right" || hasExpansiveSpacer,
            };
            float rowH = 0f;
            foreach (Node node in nodes)
            {
                hbox.Children.Add(node);
                hbox.NaturalW += node.NaturalW + 4f;
                rowH = Mathf.Max(rowH, node is LeafNode leaf ? leaf.H : 16f);
            }
            hbox.RowH = rowH;
            hbox.NaturalW = Mathf.Max(0f, hbox.NaturalW - 4f);
            hbox.FillW = hbox.AlignRight;
            return hbox;
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

        static Node BuildGrid(XElement el, RectTransform parent, BuildContext ctx)
        {
            var grid = new GridNode
            {
                Columns = int.TryParse(Attr(el, "columns"), out int cols) ? Mathf.Max(1, cols) : 2,
            };
            float col0 = 0f, rowH = 0f;
            foreach (XElement child in el.Elements())
            {
                Node node = Build(child, parent, ctx);
                if (node == null)
                    continue;
                grid.Children.Add(node);
                int index = grid.Children.Count - 1;
                if (index % grid.Columns == 0)
                    col0 = Mathf.Max(col0, node.NaturalW);
                rowH = Mathf.Max(rowH, node is LeafNode leaf ? leaf.H : 16f);
            }
            grid.Col0W = Mathf.Ceil(col0) + 2f;
            grid.NaturalW = grid.Col0W + GridColGap + EntryW;
            return grid;
        }

        static Node BuildSeparator(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            if (!string.IsNullOrEmpty(text))
            {
                RectTransform host = UiKit.CreateRect("Sep", parent);
                UiLayout.Ignore(host.gameObject);
                float titleW = Mathf.Ceil(TmpWidth(text));
                var label = DebugWindowKit.Label(host, text, UiSkin.Font.Tiny,
                    PixelSkin.Theme.SeparatorLabel, TMPro.TextAlignmentOptions.Left);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax =
                    label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.anchoredPosition = new Vector2(4f, 0f);
                label.rectTransform.sizeDelta = new Vector2(titleW + 4f, 13f);
                RectTransform line = ((RectTransform)SketchSeparator.Create(host, "Line",
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(4f + titleW + 2f, -6f), new Vector2(100f, 1f),
                    SketchSeparator.Direction.Horizontal).transform);
                return new LeafNode
                {
                    Rect = host,
                    H = 17f,
                    NaturalW = 100f,
                    FillW = true,
                    WSet = true,
                    Resize = w => line.sizeDelta = new Vector2(w - titleW - 8f, 1f),
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
                H = 9f,
                NaturalW = 100f,
                FillW = true,
                WSet = true,
                Resize = w => plain.sizeDelta = new Vector2(w, 1f),
            };
        }

        static Node BuildLabel(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            var label = DebugWindowKit.Label(parent, text, UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, TMPro.TextAlignmentOptions.Left);
            label.gameObject.name = "Label_" + (el.Attribute("id")?.Value ?? text);
            ctx.RegisterId(el.Attribute("id")?.Value, label);
            return new LeafNode
            {
                Rect = label.rectTransform,
                H = 12f,
                NaturalW = Mathf.Ceil(TmpWidth(text)) + 4f,
                WSet = true,
            };
        }

        static Node BuildEntry(XElement el, RectTransform parent, BuildContext ctx)
        {
            bool numeric = el.Name.LocalName == "expr";
            string suffix = Attr(el, "suffix");
            TMP_InputField field = AseWidgetKit.SunkenEntry(parent,
                el.Attribute("id")?.Value ?? "Entry", 0f, 0f, EntryW, string.Empty,
                numeric ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard,
                suffix);
            ctx.RegisterId(el.Attribute("id")?.Value, field);
            return new LeafNode
            {
                Rect = (RectTransform)field.transform,
                H = 16f,
                NaturalW = EntryW,
                WSet = true,
            };
        }

        static Node BuildCheck(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            Button check = AseWidgetKit.CheckRow(parent, text, 0f, 0f, false, null);
            ctx.RegisterId(el.Attribute("id")?.Value, check);
            return new LeafNode
            {
                Rect = (RectTransform)check.transform,
                H = 16f,
                NaturalW = Mathf.Ceil(TmpWidth(text)) + 18f,
                WSet = true,
            };
        }

        static Node BuildButtonset(XElement el, RectTransform parent, BuildContext ctx)
        {
            var items = new List<(string text, string icon)>();
            foreach (XElement itemEl in el.Elements("item"))
                items.Add((ctx.Text(itemEl.Attribute("text")?.Value),
                    itemEl.Attribute("icon")?.Value));

            var hbox = new HBoxNode { Homogeneous = true, FillW = true };
            var controls = new List<Button>();
            float rowH = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                Button item;
                float h;
                if (!string.IsNullOrEmpty(items[i].icon))
                {
                    var iconItem = SketchButtonSetIcon.Create(parent,
                        "Item" + i, items[i].text, items[i].icon, Vector2.zero, new Vector2(60f, 36f));
                    item = iconItem;
                    h = 36f;
                }
                else
                {
                    var textItem = SketchButtonSet.Create(parent, "Item" + i, items[i].text,
                        DebugWindowKit.HandFont, UiSkin.Font.Tiny,
                        Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(60f, 16f));
                    item = textItem;
                    h = 16f;
                }
                int captured = i;
                controls.Add(item);
                rowH = Mathf.Max(rowH, h);
                hbox.Children.Add(new LeafNode
                {
                    Rect = (RectTransform)item.transform,
                    H = h,
                    NaturalW = Mathf.Ceil(TmpWidth(items[i].text)) + 24f,
                    WSet = true,
                });
            }
            // 互斥单选（buttonset 语义）：点谁谁 Active，其余灭
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
            hbox.RowH = rowH;
            hbox.NaturalW = 0f;     // FillW：等分整行，自然宽无意义
            ctx.RegisterId(el.Attribute("id")?.Value, controls.Count > 0 ? controls[0] : null);
            return hbox;
        }

        static void SetActive(Button item, bool on)
        {
            if (item is SketchButtonSet s)
                s.Active = on;
            else if (item is SketchButtonSetIcon i)
                i.Active = on;
        }

        static Node BuildCombobox(XElement el, RectTransform parent, BuildContext ctx)
        {
            var options = new List<string>();
            foreach (XElement li in el.Elements("listitem"))
                options.Add(ctx.Text(li.Attribute("text")?.Value));
            if (options.Count == 0)
                options.Add(string.Empty);
            TextMeshProUGUI value = AseWidgetKit.ComboBox(parent,
                el.Attribute("id")?.Value ?? "Combo", 0f, 0f, 150f, options.ToArray(), 0);
            ctx.RegisterId(el.Attribute("id")?.Value, value);
            return new LeafNode
            {
                Rect = (RectTransform)value.transform.parent,
                H = 16f,
                NaturalW = 150f,
                WSet = true,
            };
        }

        static Node BuildButton(XElement el, RectTransform parent, BuildContext ctx)
        {
            string text = ctx.Text(el.Attribute("text")?.Value);
            float minW = float.TryParse(Attr(el, "minwidth"), out float mw) ? mw : 0f;
            float natural = Mathf.Max(minW, Mathf.Ceil(TmpWidth(text)) + 16f);
            var button = SketchButton.Create(parent, "Btn_" + text,
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(natural, 16f),
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
                H = 16f,
                NaturalW = natural,
                WSet = true,
            };
        }

        static string Attr(XElement el, string name) => el?.Attribute(name)?.Value;

        /// <summary>TMP 同步测宽（构建期一次，供自然宽）。</summary>
        static float TmpWidth(string text)
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
    }

    /// <summary>data/strings/en.ini 的节解析（[section] key=value；\n 转义还原；# ; 注释）。
    /// 与声明 xml 同源拷贝为 AseWidgets/en.ini.txt（AseWidgetAssetSync）。</summary>
    internal static class AseStrings
    {
        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> _map;
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
            _map = Parse(Resources.Load<TextAsset>("AseWidgets/en.ini.txt")?.text);
        }

        static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> Parse(string ini)
        {
            var map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
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
                        map[section] = new System.Collections.Generic.Dictionary<string, string>();
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
