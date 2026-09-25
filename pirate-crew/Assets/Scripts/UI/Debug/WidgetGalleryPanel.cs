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
    /// 输入框（sunken 凹槽）、页签（tab 件切换 + #333 内容面）、列表（选择金底 +
    /// 悬停变暗 #2C2C30——recent_file 语法）、滚动条（theme scrollbar 件）、
    /// 组合框（弹 Ase 菜单）、悬停气泡（tooltip 件）。
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
            DebugWindowKit.Section(window, "列表 LIST —— 悬停变暗 / 选择金底", ContentW, ref y);
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
            var check = MakeCheckRow(window, "启用音效", "check", y, DebugWindowKit.Pad, true);
            MakeCheckRow(window, "自动存档", "check", y, DebugWindowKit.Pad + 110f, false);
            var radio = MakeCheckRow(window, "窗口", "radio", y + 20f, DebugWindowKit.Pad, false);
            MakeCheckRow(window, "全屏", "radio", y + 20f, DebugWindowKit.Pad + 110f, true);
            return y + 40f + 6f;
        }

        /// <summary>theme check_box/radio_button 行：常态透明底，悬停亮面 #575B61，
        /// 图标 8×8 @x=2，文字 @x=14；点击换图标（radio 组由调用方约定初值）。</summary>
        static Button MakeCheckRow(RectTransform parent, string label, string kind,
            float y, float x, bool isOn)
        {
            var rowGo = new GameObject(kind + "_" + label, typeof(RectTransform));
            RectTransform rect = rowGo.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(120f, 16f);
            rect.anchoredPosition = new Vector2(x, -y);

            var face = rowGo.AddComponent<Image>();
            face.color = new Color(0f, 0f, 0f, 0f);   // 常态无面（theme check_box）
            face.raycastTarget = true;

            var icon = UiKit.CreateRect("Icon", rect);
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 1f);
            icon.sizeDelta = new Vector2(8f, 8f);
            icon.anchoredPosition = new Vector2(2f, -4f);   // theme x=2 垂直居中
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;

            TextMeshProUGUI text = DebugWindowKit.PlaceLabel(rect, label, UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, 14f, 0f, 104f, TextAlignmentOptions.Left);
            ((RectTransform)text.transform).pivot = new Vector2(0f, 0.5f);
            ((RectTransform)text.transform).anchoredPosition = new Vector2(14f, -8f);

            var button = rowGo.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var hover = rowGo.AddComponent<HoverFace>();
            hover.Bind(face, new Color32(0x57, 0x5B, 0x61, 0xFF));   // check_hot_face

            bool on = isOn;
            ApplyIcon(iconImage, kind, on);
            button.onClick.AddListener(() =>
            {
                on = !on;
                ApplyIcon(iconImage, kind, on);
            });
            return button;
        }

        static void ApplyIcon(Image icon, string kind, bool on)
        {
            icon.sprite = PixelSkin.Ase(kind == "radio"
                ? (on ? "radio_selected" : "radio_normal")
                : (on ? "check_selected" : "check_normal"));
        }

        static float BuildSlider(RectTransform window, float y)
        {
            // 与设置面板同款：empty 槽 + full 填充 + 双色裁剪层文本（skin_theme.cpp 双色口径）
            TextMeshProUGUI field = DebugWindowKit.PlaceLabel(window, "音量", UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, DebugWindowKit.Pad, y + 2f, 60f);

            RectTransform track = UiKit.CreateRect("Slider", window);
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(1f, 1f);
            track.sizeDelta = new Vector2(180f, 16f);
            track.anchoredPosition = new Vector2(-DebugWindowKit.Pad, -y);

            var back = track.gameObject.AddComponent<Image>();
            back.sprite = PixelSkin.SliderEmpty(false);
            back.type = Image.Type.Sliced;
            back.pixelsPerUnitMultiplier = 1f;
            back.color = Color.white;
            back.raycastTarget = true;

            var slider = track.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.7f;
            slider.targetGraphic = back;
            slider.transition = Selectable.Transition.None;

            RectTransform fill = UiKit.CreateRect("Fill", track);
            fill.anchorMin = fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 1f);
            fill.sizeDelta = new Vector2(0f, 0f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = PixelSkin.SliderFull(false);
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 1f;
            fillImage.color = Color.white;
            fillImage.raycastTarget = false;
            slider.fillRect = fill;

            RectTransform clipFull = UiKit.CreateRect("ClipFull", track);
            clipFull.anchorMin = new Vector2(0f, 0f);
            clipFull.anchorMax = new Vector2(0f, 1f);
            clipFull.pivot = new Vector2(0f, 0.5f);
            clipFull.anchoredPosition = Vector2.zero;
            clipFull.sizeDelta = Vector2.zero;
            clipFull.gameObject.AddComponent<RectMask2D>();

            RectTransform clipRest = UiKit.CreateRect("ClipRest", track);
            clipRest.anchorMin = new Vector2(0f, 0f);
            clipRest.anchorMax = new Vector2(1f, 1f);
            clipRest.offsetMin = Vector2.zero;
            clipRest.offsetMax = Vector2.zero;
            clipRest.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI light = DualLabel(clipFull, true);
            TextMeshProUGUI dark = DualLabel(clipRest, false);

            var valueLabel = track.gameObject.AddComponent<SliderValueLabel>();
            valueLabel.LabelLight = light;
            valueLabel.LabelDark = dark;
            return y + 22f;
        }

        static TextMeshProUGUI DualLabel(RectTransform clip, bool anchorLeft)
        {
            TextMeshProUGUI text = UiKit.CreateText(anchorLeft ? "ValueLight" : "ValueDark", clip,
                "70%", UiSkin.Font.Tiny, TextAlignmentOptions.Center,
                anchorLeft ? (Color)PixelSkin.Theme.Text : PixelSkin.Theme.Disabled, DebugWindowKit.HandFont);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(anchorLeft ? 0f : 1f, 0f);
            rect.anchorMax = new Vector2(anchorLeft ? 0f : 1f, 1f);
            rect.pivot = new Vector2(anchorLeft ? 0f : 1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(180f, 0f);
            return text;
        }

        static float BuildEntry(RectTransform window, float y)
        {
            TextMeshProUGUI field = DebugWindowKit.PlaceLabel(window, "名字", UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, DebugWindowKit.Pad, y + 2f, 60f);

            RectTransform entry = UiKit.CreateRect("Entry", window);
            entry.anchorMin = entry.anchorMax = entry.pivot = new Vector2(1f, 1f);
            entry.sizeDelta = new Vector2(180f, 12f);
            entry.anchoredPosition = new Vector2(-DebugWindowKit.Pad, -y);

            var sunken = entry.gameObject.AddComponent<Image>();
            sunken.sprite = PixelSkin.Sunken(false);      // theme view/textedit 凹槽
            sunken.type = Image.Type.Sliced;
            sunken.pixelsPerUnitMultiplier = 1f;
            sunken.color = Color.white;
            sunken.raycastTarget = true;

            RectTransform textArea = UiKit.CreateRect("Text", entry);
            // 拉伸锚 + inset（文字区铺满凹槽内容区——点锚配 inset 会得负宽，实机走查抓出）
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(4f, 1f);
            textArea.offsetMax = new Vector2(-4f, -1f);
            TextMeshProUGUI input = textArea.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UiKit.ResolvePixelFont(UiSkin.Font.Tiny, DebugWindowKit.HandFont);
            if (font != null)
                input.font = font;
            input.fontSize = UiSkin.Font.Tiny;
            input.color = PixelSkin.Theme.Text;
            input.raycastTarget = false;

            var inputField = entry.gameObject.AddComponent<TMP_InputField>();
            inputField.textComponent = input;
            inputField.textViewport = textArea;
            inputField.text = "海盗";
            inputField.caretColor = PixelSkin.Theme.Text;
            inputField.selectionColor = new Color32(0x40, 0x69, 0xC2, 0x60);
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
                var tabGo = new GameObject("Tab" + i, typeof(RectTransform));
                RectTransform rect = tabGo.GetComponent<RectTransform>();
                rect.SetParent(window, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, -y);
                var image = tabGo.AddComponent<Image>();
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
                image.raycastTarget = true;
                tabs[i] = image;

                TextMeshProUGUI label = DebugWindowKit.Label(rect, titles[i], UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text);
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.anchoredPosition = new Vector2(0f, -3f);
                labelRect.sizeDelta = new Vector2(60f, 8f);
                label.alignment = TextAlignmentOptions.Center;
                labels[i] = label;

                float w = Mathf.Ceil(label.preferredWidth) + 10f;
                rect.sizeDelta = new Vector2(w, 12f);   // tab 件原生高 12
                x += w;
            }

            // 内容面：tab_active_face #333333（theme tab_bottom focus 面）
            RectTransform content = UiKit.CreateRect("TabContent", window);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = new Vector2(DebugWindowKit.Pad, -(y + 12f));
            content.sizeDelta = new Vector2(ContentW, 30f);
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
                    tabs[j].sprite = PixelSkin.Ase(on ? "tab_active" : "tab_normal");
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
            string[] rowsText = { "快帆船 · 出战", "卡拉维尔 · 修整", "盖伦 · 锁定" };
            var rows = new Image[rowsText.Length];
            var texts = new TextMeshProUGUI[rowsText.Length];
            int selected = -1;
            for (int i = 0; i < rowsText.Length; i++)
            {
                var rowGo = new GameObject("Row" + i, typeof(RectTransform));
                RectTransform rect = rowGo.GetComponent<RectTransform>();
                rect.SetParent(window, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(ContentW, 14f);
                rect.anchoredPosition = new Vector2(DebugWindowKit.Pad, -(y + i * 15f));

                var face = rowGo.AddComponent<Image>();
                face.color = PixelSkin.Theme.Background;   // listitem_normal_face #41444A
                face.raycastTarget = true;
                rows[i] = face;

                TextMeshProUGUI text = DebugWindowKit.PlaceLabel(rect, rowsText[i], UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text, 2f, 0f, ContentW - 4f);
                text.rectTransform.pivot = new Vector2(0f, 0.5f);
                text.rectTransform.anchoredPosition = new Vector2(2f, -7f);
                texts[i] = text;

                var hover = rowGo.AddComponent<HoverFace>();
                // recent_file 语法：悬停 = 变暗面 #2C2C30 + **字变灰 #7d7d7d**（menuitem_hot 对）
                hover.Bind(face, new Color32(0x2C, 0x2C, 0x30, 0xFF), PixelSkin.Theme.Background,
                    text, new Color32(0x7D, 0x7D, 0x7D, 0xFF), PixelSkin.Theme.Text);

                int captured = i;
                rowGo.AddComponent<MenuTileLite>().Bind(() =>
                {
                    selected = captured;
                    for (int j = 0; j < rows.Length; j++)
                    {
                        bool on = j == selected;
                        rows[j].color = on ? PixelSkin.Theme.Selected : PixelSkin.Theme.Background;
                        texts[j].color = on ? PixelSkin.Theme.SelectedText : PixelSkin.Theme.Text;
                    }
                });
            }
            return y + 3 * 15f + 4f;
        }

        static float BuildScrollComboTip(RectTransform window, float y)
        {
            // 滚动条：theme scrollbar 件（16 宽）+ sunken 视口内 8 行假内容
            RectTransform viewport = UiKit.CreateRect("ScrollDemo", window);
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = new Vector2(DebugWindowKit.Pad, -y);
            viewport.sizeDelta = new Vector2(150f, 46f);
            var viewBorder = viewport.gameObject.AddComponent<Image>();
            viewBorder.sprite = PixelSkin.Sunken(false);
            viewBorder.type = Image.Type.Sliced;
            viewBorder.color = Color.white;
            viewBorder.raycastTarget = true;

            RectTransform content = UiKit.CreateRect("Content", viewport);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(150f, 8 * 11f + 4f);
            for (int i = 0; i < 8; i++)
                DebugWindowKit.PlaceLabel(content, "第 " + (i + 1) + " 行", UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text, 5f, i * 11f, 130f);

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            RectTransform bar = UiKit.CreateRect("VBar", window);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);
            bar.anchoredPosition = new Vector2(DebugWindowKit.Pad + 154f, -y);
            bar.sizeDelta = new Vector2(16f, 46f);
            var barBg = bar.gameObject.AddComponent<Image>();
            barBg.sprite = PixelSkin.Ase("scrollbar_bg");   // theme scrollbar 直切件
            barBg.type = Image.Type.Sliced;
            barBg.pixelsPerUnitMultiplier = 1f;
            barBg.color = Color.white;
            barBg.raycastTarget = false;

            RectTransform handle = UiKit.CreateRect("Handle", bar);
            // 标准滚动条滑块：拉伸锚（Scrollbar 按值沿轴驱动 anchor）
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = Vector2.zero;
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = PixelSkin.Ase("scrollbar_thumb");
            handleImage.type = Image.Type.Sliced;
            handleImage.pixelsPerUnitMultiplier = 1f;
            handleImage.color = Color.white;
            handleImage.raycastTarget = true;

            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.size = Mathf.Clamp01(46f / (8 * 11f + 4f));   // 滑块大小 = 可视/内容比
            scroll.verticalScrollbar = scrollbar;

            // 组合框（theme 形状 = 两件套）：sunken 凹槽词条 + 右侧 mini_button 箭头钮
            // （theme.xml combobox = sunken2 边框；combobox_button = mini_button + arrow 图标）
            float comboY = y;
            float comboX = DebugWindowKit.Pad + 180f;
            RectTransform comboEntry = UiKit.CreateRect("ComboEntry", window);
            comboEntry.anchorMin = comboEntry.anchorMax = comboEntry.pivot = new Vector2(0f, 1f);
            comboEntry.anchoredPosition = new Vector2(comboX, -(comboY + 2f));
            comboEntry.sizeDelta = new Vector2(84f, 12f);
            var entryFace = comboEntry.gameObject.AddComponent<Image>();
            entryFace.sprite = PixelSkin.Sunken(false);
            entryFace.type = Image.Type.Sliced;
            entryFace.pixelsPerUnitMultiplier = 1f;
            entryFace.color = Color.white;
            entryFace.raycastTarget = true;
            TextMeshProUGUI comboLabel = UiKit.CreateText("Value", comboEntry, "1920 × 1080",
                UiSkin.Font.Tiny, TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
            comboLabel.enableWordWrapping = false;
            comboLabel.raycastTarget = false;
            RectTransform labelRect = comboLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(4f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);

            var comboGo = new GameObject("ComboBoxButton", typeof(RectTransform));
            RectTransform combo = comboGo.GetComponent<RectTransform>();
            combo.SetParent(window, false);
            combo.anchorMin = combo.anchorMax = combo.pivot = new Vector2(0f, 1f);
            combo.anchoredPosition = new Vector2(comboX + 84f, -comboY);
            combo.sizeDelta = new Vector2(16f, BtnH);
            var comboFace = comboGo.AddComponent<Image>();
            comboFace.sprite = PixelSkin.Ase("buttonset_item_normal");   // mini_button = buttonset 件
            comboFace.type = Image.Type.Sliced;
            comboFace.pixelsPerUnitMultiplier = 1f;
            comboFace.color = Color.white;
            var comboButton = comboGo.AddComponent<Button>();
            comboButton.transition = Selectable.Transition.SpriteSwap;
            comboButton.spriteState = new SpriteState
            {
                highlightedSprite = PixelSkin.Ase("buttonset_item_hot"),
                pressedSprite = PixelSkin.Ase("buttonset_item_pushed"),
                disabledSprite = PixelSkin.Ase("buttonset_item_normal"),
            };
            RectTransform arrow = UiKit.CreateRect("Arrow", combo);
            arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.anchoredPosition = new Vector2(0f, 0.5f);
            arrow.sizeDelta = new Vector2(9f, 8f);   // combobox_arrow_down 原生 9×8，居中
            var arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = PixelSkin.Ase("combobox_arrow_down");
            arrowImage.raycastTarget = false;

            string[] resolutions = { "1920 × 1080", "1280 × 720", "960 × 540" };
            AseMenuKit.Item[] menuItems = new AseMenuKit.Item[resolutions.Length];
            for (int i = 0; i < resolutions.Length; i++)
            {
                int captured = i;
                menuItems[i] = AseMenuKit.Item_(resolutions[i], action: () =>
                    comboLabel.text = resolutions[captured], check: i == 0);
            }
            comboButton.onClick.AddListener(() => AseMenuKit.OpenPopup(
                window.parent, combo, menuItems));

            // 气泡：悬停 0.5s 弹 theme tooltip
            MakeButton(window, "悬停看气泡 →", DebugWindowKit.Pad + 180f, comboY + 24f,
                clicked: () => { });
            Transform hoverTarget = window.Find("Btn_悬停看气泡 →");
            if (hoverTarget != null)
                hoverTarget.gameObject.AddComponent<HoverTooltip>().Bind("theme tooltip 蓝底 #4069C2 + #C0C0C0 字");

            return y + 64f;
        }

        // ------------------------------------------------------------------
        // 小交互件
        // ------------------------------------------------------------------

        /// <summary>悬停换底色/字色（check 系亮面 / 列表变暗+字灰——两向都支持）。</summary>
        sealed class HoverFace : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            Image _face;
            TextMeshProUGUI _text;
            Color32 _hoverFace;
            Color32 _normalFace;
            Color32 _hoverText;
            Color32 _normalText;
            bool _hasFacePair;
            bool _hasTextPair;

            public void Bind(Image face, Color32 hover)
            {
                _face = face;
                _hoverFace = hover;
            }

            public void Bind(Image face, Color32 hover, Color32 normal)
            {
                Bind(face, hover);
                _normalFace = normal;
                _hasFacePair = true;
            }

            public void Bind(Image face, Color32 hover, Color32 normal,
                TextMeshProUGUI text, Color32 hoverText, Color32 normalText)
            {
                Bind(face, hover, normal);
                _text = text;
                _hoverText = hoverText;
                _normalText = normalText;
                _hasTextPair = true;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_face != null)
                    _face.color = _hoverFace;
                if (_text != null && _hasTextPair)
                    _text.color = _hoverText;
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (_face != null && _hasFacePair)
                    _face.color = _normalFace;
                else if (_face != null)
                    _face.color = new Color(0f, 0f, 0f, 0f);
                if (_text != null && _hasTextPair)
                    _text.color = _normalText;
            }
        }

        /// <summary>轻量点击件（页签/列表行用——Image+文字的组合，无 Selectable 状态机）。</summary>
        sealed class MenuTileLite : MonoBehaviour, IPointerClickHandler
        {
            System.Action _click;

            public void Bind(System.Action click) => _click = click;

            public void OnPointerClick(PointerEventData eventData) => _click?.Invoke();
        }

        /// <summary>悬停 0.5s 弹 theme tooltip（蓝底 #4069C2 + #C0C0C0 字），移开即收。</summary>
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

            public void OnPointerExit(PointerEventData eventData)
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
                yield return new WaitForSeconds(0.5f);

                RectTransform canvas = GetComponentInParent<Canvas>().transform as RectTransform;
                var tipGo = new GameObject("AseTooltip", typeof(RectTransform));
                RectTransform tip = tipGo.GetComponent<RectTransform>();
                tip.SetParent(canvas, false);
                tip.anchorMin = tip.anchorMax = tip.pivot = new Vector2(0.5f, 0f);
                var bg = tipGo.AddComponent<Image>();
                bg.sprite = PixelSkin.Ase("tooltip");   // theme tooltip（九宫 5/6/5×5/5/6）
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = 1f;
                bg.color = Color.white;
                bg.raycastTarget = false;

                TextMeshProUGUI text = UiKit.CreateText("Text", tip, _text, UiSkin.Font.Tiny,
                    TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
                text.enableWordWrapping = false;
                text.raycastTarget = false;
                // 件是九宫（5/6/5 × 5/5/6）——文字按内容区 inset 摆，不压边框
                float textW = Mathf.Ceil(text.preferredWidth);
                RectTransform textRect = text.rectTransform;
                textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
                textRect.pivot = new Vector2(0.5f, 0.5f);
                textRect.sizeDelta = new Vector2(textW, 12f);
                tip.sizeDelta = new Vector2(textW + 12f, 22f);

                RectTransform target = (RectTransform)transform;
                Vector3[] corners = new Vector3[4];
                target.GetWorldCorners(corners);
                Vector2 local = (Vector2)canvas.InverseTransformPoint(corners[1]);   // 左上角
                tip.anchoredPosition = new Vector2(local.x + target.rect.width * 0.5f, local.y + 2f);
                tip.SetAsLastSibling();
                _tip = tipGo;
            }
        }
    }
}
