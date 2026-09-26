using System.Collections;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// **组件实摆面板**（学 game-2 ComponentGallery：组件库的"活文档"）——每个控件按
    /// 实际使用形态摆一遍、全部可交互（创始人 2026-09-25："面板上面按照实际摆放模式放着
    /// 一个个组件…全部用正确预期的摆放方式…有交互效果"）。
    ///
    /// 每族一件、theme 语义逐条对表：按钮四态（含按下蓝面白字/禁用双层字）、buttonset
    /// 三连切换、check/radio（悬停亮面 #575B61 + 图标切换）、滑条（双色百分比）、
    /// 输入框（sunken 凹槽）、页签（tab 件切换 + #333 内容面）、列表（**只有选中金底**——
    /// theme list_item 无 state="mouse"，行不随悬停变色）、滚动条（theme scrollbar 件）、
    /// 组合框（listbox 语义下拉：金底选中/点外收/越底翻上）、悬停气泡（tooltip 件）。
    /// </summary>
    public static class WidgetGalleryPanel
    {
        const float ContentW = 316f;
        const float BtnH = 16f;

        public static RectTransform Build(Transform overlay, Vector2 topLeft)
        {
            RectTransform window = DebugWindowKit.CreateWindow(overlay, "WidgetGallery",
                "组件实摆（theme 语义 · 全交互）", topLeft, new Vector2(ContentW + DebugWindowKit.Pad * 2f, 470f));
            float y = DebugWindowKit.ContentTop;

            // ---- 按钮 BUTTON ----
            DebugWindowKit.Section(window, "按钮 BUTTON —— 四态：悬停亮面 / 按下蓝面白字 / 禁用双层字", ContentW, ref y);
            float x = DebugWindowKit.Pad;
            x += MakeButton(window, "普通", x, y) + 6f;
            x += MakeButton(window, "选中 Sticky", x, y, sticky: true) + 6f;
            MakeButton(window, "禁用", x, y, disabled: true);
            y += BtnH + 6f;

            // ---- 按钮组 BUTTONSET ----
            DebugWindowKit.Section(window, "按钮组 BUTTONSET —— 当前值换亮面（无彩色）", ContentW, ref y);
            y = BuildButtonsetTrio(window, y);

            // ---- 复选与单选 CHECK · RADIO ----
            DebugWindowKit.Section(window, "复选与单选 CHECK·RADIO —— 悬停亮面 / 图标切换", ContentW, ref y);
            y = BuildChecks(window, y);

            // ---- 滑条 SLIDER ----
            DebugWindowKit.Section(window, "滑条 SLIDER —— 双色百分比（空槽深字 / 充满浅字）", ContentW, ref y);
            y = BuildSlider(window, y);

            // ---- 输入 ENTRY ----
            DebugWindowKit.Section(window, "输入 ENTRY —— sunken 凹槽 + 8px 位图字", ContentW, ref y);
            y = BuildEntry(window, y);

            // ---- 页签 TAB ----
            DebugWindowKit.Section(window, "页签 TAB —— 未选灰字 / 选中白字 + #333 内容面", ContentW, ref y);
            y = BuildTabs(window, y);

            // ---- 列表 LIST ----
            DebugWindowKit.Section(window, "列表 LIST —— 唯一态选中金底（list_item 无悬停层）", ContentW, ref y);
            y = BuildList(window, y);

            // ---- 滚动 SCROLLBAR + 组合框 COMBOBOX + 气泡 TOOLTIP ----
            DebugWindowKit.Section(window, "滚动 SCROLLBAR · 组合框 COMBOBOX · 气泡 TOOLTIP", ContentW, ref y);
            y = BuildScrollComboTip(window, y);

            return window;
        }

        // ------------------------------------------------------------------

        /// <summary>建一枚 theme button（原生高 16；宽按文字实收），返回实宽。</summary>
        static float MakeButton(RectTransform parent, string label, float x, float y,
            bool sticky = false, bool disabled = false, System.Action clicked = null)
        {
            SketchButton button = SketchButton.Create(parent, "Btn_" + label,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(90f, BtnH), DebugWindowKit.HandFont, label, UiSkin.Font.Tiny);
            button.Sticky = sticky;
            if (disabled)
                button.interactable = false;
            if (clicked != null)
                button.onClick.AddListener(() => clicked());
            RectTransform rect = (RectTransform)button.transform;
            float w = Mathf.Ceil(button.Label.preferredWidth) + 16f;
            rect.sizeDelta = new Vector2(w, BtnH);
            return w;
        }

        static float BuildButtonsetTrio(RectTransform window, float y)
        {
            string[] options = { "RGB", "灰度", "索引" };
            var items = new SketchButtonSet[options.Length];
            float x = DebugWindowKit.Pad;
            for (int i = 0; i < options.Length; i++)
            {
                items[i] = SketchButtonSet.Create(window, "Mode" + i, options[i],
                    DebugWindowKit.HandFont, UiSkin.Font.Tiny,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                    new Vector2(72f, BtnH));
                RectTransform rect = (RectTransform)items[i].transform;
                float w = Mathf.Ceil(items[i].Label.preferredWidth) + 12f;
                rect.sizeDelta = new Vector2(Mathf.Max(48f, w), BtnH);
                x += rect.rect.width + 4f;
            }
            for (int i = 0; i < items.Length; i++)
            {
                int captured = i;
                items[i].onClick.AddListener(() =>
                {
                    for (int j = 0; j < items.Length; j++)
                        items[j].Active = j == captured;
                });
            }
            items[0].Active = true;
            return y + BtnH + 6f;
        }

        static float BuildChecks(RectTransform window, float y)
        {
            AseWidgetKit.CheckRow(window, "启用音效", DebugWindowKit.Pad, y, true, null);
            AseWidgetKit.CheckRow(window, "自动存档", DebugWindowKit.Pad + 110f, y, false, null);
            AseWidgetKit.CheckRow(window, "窗口", DebugWindowKit.Pad, y + 20f, false, null, kind: "radio");
            AseWidgetKit.CheckRow(window, "全屏", DebugWindowKit.Pad + 110f, y + 20f, true, null, kind: "radio");
            return y + 40f + 6f;
        }

        static float BuildSlider(RectTransform window, float y)
        {
            DebugWindowKit.PlaceLabel(window, "音量", UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, DebugWindowKit.Pad, y + 2f, 60f);

            // SketchSlider = slider.cpp 逐函数移植（整数取值/绝对跟手/双色分区文本内置），
            // 旧 fillRect/裁剪框/SliderValueLabel 补丁链不再需要。源缺省文案 "%d" 不带 %，
            // 画廊保留 "70%" 观感 → 接 valueToText。
            SketchSlider.Create(window, "Slider",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-DebugWindowKit.Pad, -y),
                new Vector2(180f, 16f), 0, 100, 70, DebugWindowKit.HandFont, UiSkin.Font.Tiny,
                v => v + "%");
            return y + 22f;
        }

        static float BuildEntry(RectTransform window, float y)
        {
            DebugWindowKit.PlaceLabel(window, "名字", UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, DebugWindowKit.Pad, y + 2f, 60f);

            // 公共件工厂：sunken 凹槽 + TMP 输入（theme textedit 语义；名字字段＝标准文本）
            float x = ContentW + DebugWindowKit.Pad - 180f;
            AseWidgetKit.SunkenEntry(window, "NameEntry", x, y, 180f, "海盗",
                TMPro.TMP_InputField.ContentType.Standard);
            return y + 18f;
        }

        static float BuildTabs(RectTransform window, float y)
        {
            string[] titles = { "船员", "战利品" };
            var tabs = new Image[titles.Length];
            var labels = new TextMeshProUGUI[titles.Length];
            float x = DebugWindowKit.Pad;
            for (int i = 0; i < titles.Length; i++)
            {
                RectTransform rect = UiKit.CreateRect("Tab" + i, window);
                var image = rect.gameObject.AddComponent<Image>();
                AseUi.SetRawPart(image, "tab_normal");
                image.raycastTarget = true;
                tabs[i] = image;

                TextMeshProUGUI label = DebugWindowKit.Label(rect, titles[i], UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text);
                UiKit.SetAnchored(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(60f, 8f),
                    new Vector2(0f, -3f));
                label.alignment = TextAlignmentOptions.Center;
                labels[i] = label;

                float w = Mathf.Ceil(label.preferredWidth) + 10f;
                UiKit.SetAnchored(rect, new Vector2(0f, 1f), new Vector2(w, 12f), new Vector2(x, -y));   // tab 件原生高 12
                x += w;
            }

            // 内容面：tab_active_face #333333（theme tab_bottom focus 面）
            RectTransform content = UiKit.CreateRect("TabContent", window);
            UiKit.SetAnchored(content, new Vector2(0f, 1f), new Vector2(ContentW, 30f),
                new Vector2(DebugWindowKit.Pad, -(y + 12f)));
            var contentFace = content.gameObject.AddComponent<Image>();
            contentFace.color = new Color32(0x33, 0x33, 0x33, 0xFF);
            contentFace.raycastTarget = false;

            TextMeshProUGUI[] pages =
            {
                DebugWindowKit.PlaceLabel(content, "第一页：船员列表（示意）", UiSkin.Font.Tiny, PixelSkin.Theme.Text, 6f, 4f, ContentW),
                DebugWindowKit.PlaceLabel(content, "第二页：战利品（示意）", UiSkin.Font.Tiny, PixelSkin.Theme.Text, 6f, 4f, ContentW),
            };

            int selected = 0;
            void Apply(int index)
            {
                selected = index;
                for (int j = 0; j < tabs.Length; j++)
                {
                    bool on = j == selected;
                    AseUi.SetRawPart(tabs[j], on ? "tab_active" : "tab_normal");
                    labels[j].color = on ? PixelSkin.Theme.Text : new Color32(0x7D, 0x7D, 0x7D, 0xFF);
                    pages[j].gameObject.SetActive(on);
                }
            }
            for (int i = 0; i < tabs.Length; i++)
            {
                int captured = i;
                tabs[i].gameObject.AddComponent<MenuTileLite>().Bind(() => Apply(captured));
            }
            Apply(0);
            return y + 12f + 30f + 6f;
        }

        static float BuildList(RectTransform window, float y)
        {
            // 列表（theme list_item）：行按下即选、按住拖动扫选；选中 = 唯一态金底 #E1B85F + 深字 #41444A；
            // 常态 #41444A + 字 #C0C0C0。**行没有悬停态**——list_item 样式无 state="mouse"
            // （旧版悬停变暗 #2C2C30 + 字灰借的是 recent_file/menuitem_hot 的语法）。
            string[] rowsText = { "快帆船 · 出战", "卡拉维尔 · 修整", "盖伦 · 锁定" };
            AseListBox.Create(window, "ShipList", DebugWindowKit.Pad, y, ContentW, rowsText, -1);
            return y + rowsText.Length * AseListBox.RowHeight + 4f;
        }

        static float BuildScrollComboTip(RectTransform window, float y)
        {
            // 滚动条：theme scrollbar 件（16 宽）+ sunken 视口内 8 行假内容
            RectTransform viewport = UiKit.CreateRect("ScrollDemo", window);
            UiKit.SetAnchored(viewport, new Vector2(0f, 1f), new Vector2(150f, 46f),
                new Vector2(DebugWindowKit.Pad, -y));
            var viewBorder = viewport.gameObject.AddComponent<Image>();
            AseUi.SetRawPart(viewBorder, "sunken_normal");
            viewBorder.raycastTarget = true;
            // 裁剪：滚出视口的内容被裁掉（源 drawable region 逐祖先求交）
            AseUi.ClipViewport(viewport);

            RectTransform content = UiKit.CreateRect("Content", viewport);
            UiKit.SetAnchored(content, new Vector2(0.5f, 1f), new Vector2(150f, 8 * 11f + 4f), Vector2.zero);
            for (int i = 0; i < 8; i++)
                DebugWindowKit.PlaceLabel(content, "第 " + (i + 1) + " 行", UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text, 5f, i * 11f, 130f);

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            RectTransform bar = UiKit.CreateRect("VBar", window);
            UiKit.SetAnchored(bar, new Vector2(0f, 1f), new Vector2(16f, 46f),
                new Vector2(DebugWindowKit.Pad + 154f, -y));
            var barBg = bar.gameObject.AddComponent<Image>();
            AseUi.SetRawPart(barBg, "scrollbar_bg");   // theme scrollbar 直切件
            barBg.raycastTarget = false;

            RectTransform handle = UiKit.CreateRect("Handle", bar);
            UiKit.Stretch(handle);   // 拉伸锚（Scrollbar 按值沿轴驱动 anchor）
            var handleImage = handle.gameObject.AddComponent<Image>();
            AseUi.SetRawPart(handleImage, "scrollbar_thumb");
            handleImage.raycastTarget = true;

            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.size = Mathf.Clamp01(46f / (8 * 11f + 4f));   // 滑块大小 = 可视/内容比
            scroll.verticalScrollbar = scrollbar;

            // 组合框（两件套：sunken2 词条 + mini_button 箭头钮；弹层 = View(sunken) 里的 ListBox，
            // 金底选中/点外收/越底翻上——combobox.cpp 语义，实现见 AseComboBox）
            AseWidgetKit.ComboBox(window, "ResolutionCombo", DebugWindowKit.Pad + 180f, y + 2f,
                84f, new[] { "1920 × 1080", "1280 × 720", "960 × 540" }, 0);

            // 气泡：悬停 0.3s 弹 theme tooltip
            MakeButton(window, "悬停看气泡 →", DebugWindowKit.Pad + 180f, y + 24f,
                clicked: () => { });
            Transform hoverTarget = window.Find("Btn_悬停看气泡 →");
            if (hoverTarget != null)
                hoverTarget.gameObject.AddComponent<HoverTooltip>().Bind("theme tooltip 蓝底 #4069C2 + #C0C0C0 字");

            return y + 64f;
        }

        // ------------------------------------------------------------------
        // 小交互件
        // ------------------------------------------------------------------

        /// <summary>轻量点击件（页签/列表行用——Image+文字的组合，无 Selectable 状态机）。</summary>
        sealed class MenuTileLite : MonoBehaviour, IPointerClickHandler
        {
            System.Action _click;

            public void Bind(System.Action click) => _click = click;

            public void OnPointerClick(PointerEventData eventData) => _click?.Invoke();
        }

        /// <summary>悬停 0.3s 弹 theme tooltip（蓝底 #4069C2 + #C0C0C0 字），移开即收。</summary>
        sealed class HoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            string _text;
            Coroutine _pending;
            GameObject _tip;

            public void Bind(string text) => _text = text;

            public void OnPointerEnter(PointerEventData eventData)
            {
                _pending = StartCoroutine(ShowLater());
            }

            public void OnPointerExit(PointerEventData eventData) => Hide();

            // 悬停中宿主窗被 × 关掉（SetActive(false)）不会派发 PointerExit——
            // 不在失活时收气泡，蓝底 tooltip 会孤儿一样常驻画布顶层。
            void OnDisable() => Hide();

            void Hide()
            {
                if (_pending != null)
                {
                    StopCoroutine(_pending);
                    _pending = null;
                }
                if (_tip != null)
                {
                    Destroy(_tip);
                    _tip = null;
                }
            }

            IEnumerator ShowLater()
            {
                yield return new WaitForSeconds(0.3f);   // tooltips.cpp:29 kDefaultTooltipDelayMsecs=300

                RectTransform canvas = AseUi.OverlayOf(transform);
                RectTransform tip = UiKit.CreateRect("AseTooltip", canvas);
                tip.anchorMin = tip.anchorMax = tip.pivot = new Vector2(0f, 1f);
                var bg = tip.gameObject.AddComponent<Image>();
                AseUi.SetRawPart(bg, "tooltip");   // theme tooltip（九宫 5/6/5×5/5/6）
                bg.raycastTarget = false;

                TextMeshProUGUI text = UiKit.CreateText("Text", tip, _text, UiSkin.Font.Tiny,
                    TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
                text.enableWordWrapping = false;
                text.raycastTarget = false;
                // 件是九宫（5/6/5 × 5/5/6）——文字按内容区 inset 摆，不压边框
                float textW = Mathf.Ceil(text.preferredWidth);
                UiKit.SetAnchored(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(textW, 12f),
                    Vector2.zero);
                tip.sizeDelta = new Vector2(textW + 12f, 22f);

                // 气泡水平居中于目标钮、底边高于钮顶 2px：(0,1) 点锚 = 一行换算（EdgesOf）
                // + 一行落位（PlaceByEdges）。tip 已量宽——左缘 = 钮中心 − 半宽（等价旧
                // 底边中心锚语义，漏减半宽会整体右偏半气泡）。
                RectTransform target = (RectTransform)transform;
                Vector2 edges = AseUi.EdgesOf(target, canvas);
                AseUi.PlaceByEdges(tip,
                    edges.x + target.rect.width * 0.5f - tip.sizeDelta.x * 0.5f,
                    edges.y - 2f);
                tip.SetAsLastSibling();
                _tip = tip.gameObject;
            }
        }
    }
}
