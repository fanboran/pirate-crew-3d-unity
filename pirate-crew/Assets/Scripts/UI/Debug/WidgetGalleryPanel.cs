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
            float y = DebugWindowKit.ContentTopOf(window);

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
                UiKit.PlaceTopLeft(rect, x, y, new Vector2(w, 12f));   // tab 件原生高 12
                x += w;
            }

            // 内容面：tab_active_face #333333（theme tab_bottom focus 面）
            RectTransform content = UiKit.CreateRect("TabContent", window);
            UiKit.PlaceTopLeft(content, DebugWindowKit.Pad, (y + 12f), new Vector2(ContentW, 30f));
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
            // 滚动视图 = theme view 样式（window_face 底 + sunken 边框 3/顶4），条按需出在
            // 框内右缘、视口被条挤窄（scroll_helper.cpp:75-81）——源里没有「框外独立条」的组合
            AseView scroll = AseWidgetKit.ScrollView(window, "ScrollDemo",
                DebugWindowKit.Pad, y, 150f, 46f);

            RectTransform content = UiKit.CreateRect("Content", scroll.Viewport);
            UiKit.SetAnchored(content, new Vector2(0f, 1f), new Vector2(130f, 8 * 11f + 4f),
                Vector2.zero);
            for (int i = 0; i < 8; i++)
                DebugWindowKit.PlaceLabel(content, "第 " + (i + 1) + " 行", UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text, 5f, i * 11f, 130f);

            scroll.AttachToView(content);
            scroll.SetContentHint(130, 8 * 11 + 4);   // Viewport::calculateNeededSize 由挂载方供给
            scroll.UpdateView();

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

    }
}
