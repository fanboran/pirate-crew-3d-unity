using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// Aseprite 控件状态位（<c>ui::Style::Layer</c> flags，<c>src/ui/style.h:46</c>）。
    /// **数值即源值**——<c>kMouse=1 / kFocus=2 / kSelected=4 / kDisabled=8 / kCapture=16</c>，
    /// 因为层匹配（<see cref="AseThemeLayers"/>）按 flags 数值大小决胜（theme.cpp:47-51 的
    /// <c>compare_layer_flags</c> 就是 <c>a - b</c>）。
    /// </summary>
    [Flags]
    public enum AseStates
    {
        None = 0,
        /// <summary>鼠标悬停（<c>Widget::hasMouse()</c>）。</summary>
        Mouse = 1,
        /// <summary>键盘焦点（<c>Widget::hasFocus()</c>）。</summary>
        Focus = 2,
        /// <summary>选中（<c>Widget::isSelected()</c>；按钮按下时由 <c>setSelected(true)</c> 置位）。</summary>
        Selected = 4,
        /// <summary>禁用（<c>!Widget::isEnabled()</c>）。</summary>
        Disabled = 8,
        /// <summary>鼠标捕获（<c>Widget::hasCapture()</c>；按下时 <c>captureMouse()</c> 置位）。</summary>
        Capture = 16,
    }

    /// <summary>theme 层类型（<c>ui::Style::Layer::Type</c>，<c>src/ui/style.h:35-43</c>，枚举序同源）。</summary>
    public enum AseLayerType
    {
        None = 0,
        Background = 1,
        BackgroundBorder = 2,
        Border = 3,
        Icon = 4,
        Text = 5,
        /// <summary>分层哨兵：把同类型层切成两个独立命中组（见 <see cref="AseThemeLayers"/> 的 addLayer 语义）。</summary>
        NewLayer = 6,
    }

    /// <summary>theme 样式的**一层**（<c>ui::Style::Layer</c> 的子集：只带渲染需要的字段）。</summary>
    public sealed class AseThemeLayer
    {
        public AseLayerType Type;
        /// <summary>层状态位（0 = 无条件层，任何控件状态都命中）。</summary>
        public AseStates States;
        /// <summary>直切件 id（theme.xml &lt;parts&gt; 名，如 <c>button_hot</c>）；色层为 null。</summary>
        public string PartId;
        /// <summary>是否带 <c>color</c> 属性（含 <c>color="none"</c> → 无颜色）。</summary>
        public bool HasColor;
        /// <summary>theme 命名色解析结果（<c>&lt;colors&gt;</c> 表）。</summary>
        public Color32 Color;
        /// <summary>层偏移（theme.xml 的 <c>x</c>/<c>y</c>，设计格；×1 终局即画布像素，y 向下为正）。</summary>
        public Vector2Int Offset;

        public AseThemeLayer Clone()
        {
            return new AseThemeLayer
            {
                Type = Type,
                States = States,
                PartId = PartId,
                HasColor = HasColor,
                Color = Color,
                Offset = Offset,
            };
        }
    }

    /// <summary>展开 <c>extends</c> 后的一条样式（层表已按源 addLayer 语义归并）。</summary>
    public sealed class AseThemeStyle
    {
        public string Id;
        public readonly List<AseThemeLayer> Layers = new List<AseThemeLayer>();
    }

    /// <summary>
    /// **theme 状态层匹配引擎**——<c>src/ui/theme.cpp:53-130</c> 的 <c>for_each_layer</c> +
    /// <c>PaintWidgetPartInfo::getStyleFlagsForWidget</c> 逐函数移植，数据源 = theme.xml
    /// 的 <c>&lt;styles&gt;</c>/<c>&lt;colors&gt;</c>（运行时懒解析一次，见 <see cref="EnsureParsed"/>）。
    ///
    /// 【它替掉什么】此前每个控件各自写死「状态 → 件/色」映射（button 的 mouse 态换
    /// <c>button_hot</c>、组合框 focus 态换 <c>sunken2_focused</c>……），与主题声明两处维护、
    /// 一改就漂。现在状态映射回归**单一真源** theme.xml：控件只报自己的状态位集，
    /// 引擎按源优先级选层。移植后行为冻结（对账表见交付报告）。
    ///
    /// 【匹配语义（逐行照源）】
    /// <list type="number">
    /// <item>层在样式里按 <c>addLayer</c>（style.cpp:68-93）归并：同类型层始终连续聚成一段；
    ///   新层插到该段末尾。遇到 <c>newlayer</c> 哨兵则更新插入点，把后续同类型层切成**新段**
    ///   （button 的禁用双层字就是两条 text 段叠出来的：段一影子 + 段二盖面）。</item>
    /// <item><c>for_each_layer</c>（theme.cpp:53-78）遍历：层类型一变就把当前段选出的最优层
    ///   回调一次并清空；每层若 (a) <c>flags==0</c> 或 <c>(flags &amp; widgetFlags)==flags</c>
    ///   且 (b) 当前最优为空或 <c>best.flags &lt;= layer.flags</c>，则取代最优
    ///   （<c>compare_layer_flags</c> = <c>a-b</c>，所以**数值大者胜、相等取后**——
    ///   disabled(8) &gt; selected(4) &gt; focus(2) &gt; mouse(1)）。</item>
    /// <item>同一类型段里没命中任何层的，不回调（如 check_box 常态无 background 层 →
    ///   不铺底色；button 常态有 flags=0 的 <c>button_normal</c> → 命中）。</item>
    /// </list>
    ///
    /// 【样式级属性】本引擎只解析渲染需要的层（type/state/part/color/x/y）；border/padding/
    /// margin/尺寸等布局属性各控件仍按各自现有来源处理（件九宫切片、AseLayout），不在此重复。
    /// </summary>
    public static class AseThemeLayers
    {
        /// <summary>Resources 路径（去掉 .txt 末扩展名；盘上 = AseWidgets/theme.xml.txt）。</summary>
        const string ResourcePath = "AseWidgets/theme.xml";

        static Dictionary<string, AseThemeStyle> s_styles;
        static bool s_parsed;

        /// <summary>解析是否已发生（<see cref="EnsureParsed"/> 幂等；每帧不读文件）。</summary>
        public static bool IsParsed => s_parsed;

        /// <summary>样式数（诊断用）。</summary>
        public static int StyleCount => s_styles != null ? s_styles.Count : 0;

        /// <summary>
        /// 取一条已展开的样式。未解析/无此样式返回 false。
        /// </summary>
        public static bool TryGetStyle(string styleId, out AseThemeStyle style)
        {
            EnsureParsed();
            style = null;
            return styleId != null && s_styles.TryGetValue(styleId, out style);
        }

        // ------------------------------------------------------------------
        // 对外解析 API（输入 = 样式 id + 控件状态位集）
        // ------------------------------------------------------------------

        /// <summary>
        /// <c>for_each_layer</c> 的原始输出：按绘制序命中的全部层（含各类层；调用方自行过滤）。
        /// 为省分配请优先用下面按类型过滤的便捷出口。
        /// </summary>
        public static List<AseThemeLayer> Resolve(string styleId, AseStates states)
        {
            var result = new List<AseThemeLayer>();
            if (TryGetStyle(styleId, out AseThemeStyle style))
                ForEachLayer(states, style, result.Add);
            return result;
        }

        /// <summary>
        /// 取**底皮件**（background / background-border / border 里带 <c>part</c> 的最优层，
        /// 按绘制序取最上层 = 最后命中者）。即源里画在控件矩形上的那个九宫/直切件。
        /// 无命中返回 null（如 check_box 常态没有背景层）。
        /// </summary>
        public static string ResolveBackgroundPart(string styleId, AseStates states)
        {
            if (!TryGetStyle(styleId, out AseThemeStyle style))
                return null;
            string best = null;
            ForEachLayer(states, style, layer =>
            {
                if (layer.PartId != null && IsBackgroundFamily(layer.Type))
                    best = layer.PartId;
            });
            return best;
        }

        /// <summary>
        /// 取**底色**（background / background-border 里带 <c>color</c> 的最优层颜色）。
        /// 无命中或层色为 <c>none</c> 返回 null（check_box/radio_button 的 mouse 亮面、
        /// focus 面、disabled 面都走这里）。
        /// </summary>
        public static Color32? ResolveBackgroundColor(string styleId, AseStates states)
        {
            if (!TryGetStyle(styleId, out AseThemeStyle style))
                return null;
            bool found = false;
            Color32 c = default;
            ForEachLayer(states, style, layer =>
            {
                if (layer.HasColor && IsBackgroundFamily(layer.Type))
                {
                    c = layer.Color;
                    found = true;
                }
            });
            return found ? c : (Color32?)null;
        }

        /// <summary>
        /// 取**字色**（text 类型里最优层颜色）。button 禁用态命中的是段二
        /// <c>state="disabled"</c> 层（盖面），影子层由 <see cref="ResolveTextLayers"/> 给。
        /// </summary>
        public static Color32? ResolveTextColor(string styleId, AseStates states)
        {
            if (!TryGetStyle(styleId, out AseThemeStyle style))
                return null;
            bool found = false;
            Color32 c = default;
            ForEachLayer(states, style, layer =>
            {
                if (layer.HasColor && layer.Type == AseLayerType.Text)
                {
                    c = layer.Color;
                    found = true;
                }
            });
            return found ? c : (Color32?)null;
        }

        /// <summary>
        /// 取 text 类型命中的**全部层**（绘制序）。常态/悬停/选中/焦点都只有一条；
        /// **禁用态两条**：第一条 = 影子（theme background 色 + (x,y) 偏移），
        /// 第二条 = 盖面 disabled 色（同源里 newlayer 切段后的双层字）。
        /// </summary>
        public static List<AseThemeLayer> ResolveTextLayers(string styleId, AseStates states)
        {
            var result = new List<AseThemeLayer>();
            if (TryGetStyle(styleId, out AseThemeStyle style))
                ForEachLayer(states, style, layer =>
                {
                    if (layer.Type == AseLayerType.Text)
                        result.Add(layer);
                });
            return result;
        }

        /// <summary>取 icon 类型里最优层的件 id（combobox_button 的三态箭头走这里）。</summary>
        public static string ResolveIconPart(string styleId, AseStates states)
        {
            if (!TryGetStyle(styleId, out AseThemeStyle style))
                return null;
            string best = null;
            ForEachLayer(states, style, layer =>
            {
                if (layer.PartId != null && layer.Type == AseLayerType.Icon)
                    best = layer.PartId;
            });
            return best;
        }

        static bool IsBackgroundFamily(AseLayerType type)
        {
            return type == AseLayerType.Background
                || type == AseLayerType.BackgroundBorder
                || type == AseLayerType.Border;
        }

        // ------------------------------------------------------------------
        // for_each_layer 移植（theme.cpp:53-78）
        // ------------------------------------------------------------------

        /// <summary>
        /// <c>for_each_layer(const int flags, const Style* style, callback)</c> 逐行移植。
        /// 见类注释的匹配语义；<c>newlayer</c> 也会被回调一次（类型 = NewLayer，消费方按类型过滤掉）。
        /// </summary>
        static void ForEachLayer(AseStates flags, AseThemeStyle style, Action<AseThemeLayer> callback)
        {
            if (style == null || callback == null)
                return;

            AseThemeLayer bestLayer = null;

            foreach (AseThemeLayer layer in style.Layers)
            {
                if (bestLayer != null && bestLayer.Type != layer.Type)
                {
                    callback(bestLayer);
                    bestLayer = null;
                }

                if ((layer.States == AseStates.None || (layer.States & flags) == layer.States) &&
                    (bestLayer == null || (int)bestLayer.States <= (int)layer.States))
                {
                    bestLayer = layer;
                }
            }

            if (bestLayer != null)
                callback(bestLayer);
        }

        // ------------------------------------------------------------------
        // 解析（theme.xml → 样式表；懒解析一次）
        // ------------------------------------------------------------------

        static void EnsureParsed()
        {
            if (s_parsed)
                return;
            s_parsed = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[AseThemeLayers] 未找到 Resources/" + ResourcePath
                    + ".txt——状态层匹配将全部落空。由 AseWidgetAssetSync（菜单 PirateCrew/同步 Aseprite widgets 资产）"
                    + "从 Assets/Art/Sprites/UI/Aseprite/theme.xml 拷入。");
                s_styles = new Dictionary<string, AseThemeStyle>();
                return;
            }
            s_styles = Parse(asset.text);
        }

        /// <summary>
        /// 解析主题 XML 的 <c>&lt;colors&gt;</c> 表与 <c>&lt;styles&gt;</c> 表。
        /// 样式按**文档序**顺序展开（<c>extends</c> 基样式必须先于声明，源同理）。
        /// </summary>
        static Dictionary<string, AseThemeStyle> Parse(string xml)
        {
            var styles = new Dictionary<string, AseThemeStyle>();
            if (string.IsNullOrEmpty(xml))
                return styles;

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (Exception e)
            {
                Debug.LogError("[AseThemeLayers] theme.xml 解析失败：" + e.Message);
                return styles;
            }

            var colors = ParseColors(doc);
            XElement stylesEl = doc.Root?.Element("styles");
            if (stylesEl == null)
            {
                Debug.LogError("[AseThemeLayers] theme.xml 缺 <styles> 段。");
                return styles;
            }

            foreach (XElement styleEl in stylesEl.Elements("style"))
            {
                string id = (string)styleEl.Attribute("id");
                if (string.IsNullOrEmpty(id))
                    continue;

                var style = new AseThemeStyle { Id = id };
                string extendsId = (string)styleEl.Attribute("extends");
                if (!string.IsNullOrEmpty(extendsId) && styles.TryGetValue(extendsId, out AseThemeStyle baseStyle))
                {
                    foreach (AseThemeLayer l in baseStyle.Layers)
                        style.Layers.Add(l.Clone());
                }

                int insertionPoint = 0;   // Style(base) 的 m_insertionPoint 从 0 起（style.cpp:35）
                foreach (XElement layerEl in styleEl.Elements())
                {
                    AseThemeLayer layer = ParseLayer(layerEl, colors);
                    if (layer.Type != AseLayerType.None)
                        insertionPoint = AddLayer(style.Layers, insertionPoint, layer);
                }

                styles[id] = style;
            }
            return styles;
        }

        static Dictionary<string, Color32> ParseColors(XDocument doc)
        {
            var colors = new Dictionary<string, Color32>();
            XElement colorsEl = doc.Root?.Element("colors");
            if (colorsEl == null)
                return colors;
            foreach (XElement c in colorsEl.Elements("color"))
            {
                string id = (string)c.Attribute("id");
                string value = (string)c.Attribute("value");
                if (!string.IsNullOrEmpty(id) && TryParseHex(value, out Color32 color))
                    colors[id] = color;
            }
            return colors;
        }

        /// <summary><c>#rgb/#rrggbb/#rrggbbaa</c> 解析；失败返回 false。</summary>
        static bool TryParseHex(string value, out Color32 color)
        {
            color = default;
            if (string.IsNullOrEmpty(value) || value[0] != '#')
                return false;
            string hex = value.Substring(1);
            if (hex.Length != 6 && hex.Length != 8)
                return false;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)0xFF;
                color = new Color32(r, g, b, a);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 单层解析（skin_theme.cpp:953-1072）：元素名 → 层类型；state 子串命中位
        /// （禁用/选中/焦点/悬停/捕获，<c>find</c> 序同源 980-989）；color 查命名表
        /// （<c>none</c> → 无颜色）；x/y → 偏移（源乘 scale，本端 ×1 恒等）。
        /// </summary>
        static AseThemeLayer ParseLayer(XElement el, Dictionary<string, Color32> colors)
        {
            var layer = new AseThemeLayer();
            switch (el.Name.LocalName)
            {
                case "background": layer.Type = AseLayerType.Background; break;
                case "background-border": layer.Type = AseLayerType.BackgroundBorder; break;
                case "border": layer.Type = AseLayerType.Border; break;
                case "icon": layer.Type = AseLayerType.Icon; break;
                case "text": layer.Type = AseLayerType.Text; break;
                case "newlayer": layer.Type = AseLayerType.NewLayer; break;
                default: layer.Type = AseLayerType.None; break;
            }

            string state = (string)el.Attribute("state");
            if (!string.IsNullOrEmpty(state))
            {
                AseStates flags = AseStates.None;
                if (state.IndexOf("disabled", StringComparison.Ordinal) >= 0) flags |= AseStates.Disabled;
                if (state.IndexOf("selected", StringComparison.Ordinal) >= 0) flags |= AseStates.Selected;
                if (state.IndexOf("focus", StringComparison.Ordinal) >= 0) flags |= AseStates.Focus;
                if (state.IndexOf("mouse", StringComparison.Ordinal) >= 0) flags |= AseStates.Mouse;
                if (state.IndexOf("capture", StringComparison.Ordinal) >= 0) flags |= AseStates.Capture;
                layer.States = flags;
            }

            string colorId = (string)el.Attribute("color");
            if (!string.IsNullOrEmpty(colorId))
            {
                if (colors.TryGetValue(colorId, out Color32 c))
                {
                    layer.HasColor = true;
                    layer.Color = c;
                }
                else if (colorId == "none")
                {
                    layer.HasColor = false;
                }
                else
                {
                    // 源此处 throw（skin_theme.cpp:1024-1029）；本端不夺运行时，只登记。
                    Debug.LogWarning("[AseThemeLayers] theme.xml 层引用了未知颜色 \"" + colorId + "\"。");
                }
            }

            string part = (string)el.Attribute("part");
            if (!string.IsNullOrEmpty(part))
                layer.PartId = part;

            XAttribute x = el.Attribute("x");
            XAttribute y = el.Attribute("y");
            if (x != null || y != null)
            {
                layer.Offset = new Vector2Int(
                    x != null ? ParseInt((string)x) : 0,
                    y != null ? ParseInt((string)y) : 0);
            }
            return layer;
        }

        static int ParseInt(string s)
        {
            return int.TryParse(s, out int v) ? v : 0;
        }

        /// <summary>
        /// <c>Style::addLayer</c>（style.cpp:68-93）逐行移植：同类型层聚成连续段，新层插到
        /// 段末；<c>newlayer</c> 只推进插入点（把后续同类型层另起一段）。
        /// 返回更新后的插入点。
        /// </summary>
        static int AddLayer(List<AseThemeLayer> layers, int insertionPoint, AseThemeLayer layer)
        {
            int i, j = layers.Count;

            for (i = insertionPoint; i < layers.Count; ++i)
            {
                if (layer.Type == layers[i].Type)
                {
                    for (j = i + 1; j < layers.Count; ++j)
                    {
                        if (layer.Type != layers[j].Type)
                            break;
                    }
                    break;
                }
            }

            if (i < layers.Count)
            {
                if (layer.Type == AseLayerType.NewLayer)
                    insertionPoint = i + 1;
                else
                    layers.Insert(j, layer);
            }
            else
            {
                layers.Add(layer);
                if (layer.Type == AseLayerType.NewLayer)
                    insertionPoint = layers.Count;
            }
            return insertionPoint;
        }
    }
}
