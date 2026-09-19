using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 设计系统陈列页（game-2 component_gallery 的 Unity 等价物）：
    /// 把全部控件档位 / 字号 / 色板 / 图标集中摆进一个纯 UI Canvas，截图即得
    /// "设计系统全貌图"。
    ///
    /// 【为什么】战斗 HUD 只能看到组合后的结果，看不出"档位纪律"本身——陈列页让
    /// 改 <see cref="UiSkin"/> Token 的效果一眼全览（视觉回归自检页），也是给用户
    /// 验收"风格是否统一"的最短路径。全部件经 <see cref="UiKit"/> 装配，
    /// 陈列的就是真实控件长相，不是示意图。
    ///
    /// 【出图】播放器 <c>PlayerArtCapture</c> 的 ui-gallery / ui-gallery-icons
    /// 机位：清屏（cullingMask=0）→ 建本页 → 截图 → 整页销毁。不入任何场景。
    ///
    /// 【运行时轨道】皮肤用内存 Sprite、字体走 <see cref="UiKit.RuntimeFont"/>
    /// （Resources 副本）、武器/职业图标走 Resources/UIIcons——本页不依赖 Editor。
    /// </summary>
    public static class UiGalleryPage
    {
        /// <summary>控件档位页（按钮变体 / 血条族 / pips / 字号 / 色板 / 圆角档）。</summary>
        public const string ControlsPage = "ui-gallery";
        /// <summary>图标集页（17 武器 / 7 职业 / 13 符号）。</summary>
        public const string IconsPage = "ui-gallery-icons";

        /// <summary>建陈列页根节点（挂 parent 下，占满 1920×1080）。iconsPage=false 控件页。</summary>
        public static RectTransform Build(Transform parent, bool iconsPage)
        {
            RectTransform root = UiKit.CreateRect("UiGalleryPage", parent);
            UiKit.Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;

            TMP_FontAsset body = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Body);
            TMP_FontAsset secondary = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Secondary);
            TMP_FontAsset title = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Title);

            if (iconsPage)
                BuildIconsPage(root, body, secondary, title);
            else
                BuildControlsPage(root, body, secondary, title);

            return root;
        }

        // ------------------------------------------------------------------
        // 控件档位页
        // ------------------------------------------------------------------

        static void BuildControlsPage(RectTransform root, TMP_FontAsset body,
            TMP_FontAsset secondary, TMP_FontAsset title)
        {
            PageTitle(root, "UI 设计系统 · 组件陈列（全部件 = UiKit 真实长相 · 可交互验证）", title);

            // 窗体（对齐隔壁 component_gallery：大面板承载全部分组），双列各 4 组。
            RectTransform window = UiKit.CreatePanel("Window", root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1840f, 1016f));

            // 左列：按钮 / 标签 / 输入 / 页签；右列：滑条进度 / 列表 / 反馈 / 键鼠。
            float ly = 436f, ry = 436f;
            float lx = -900f, rx = 40f;
            BuildButtonsSection(window, lx, ref ly, body);
            BuildLabelsSection(window, lx, ref ly, body, secondary);
            BuildInputsSection(window, lx, ref ly, body, secondary);
            BuildTabsSection(window, lx, ref ly, body, secondary);

            BuildSlidersSection(window, rx, ref ry, body, secondary);
            BuildListsSection(window, rx, ref ry, body, secondary);
            BuildFeedbackSection(window, rx, ref ry, body, secondary, root);
            BuildKeymapSection(window, rx, ref ry, secondary);
        }

        // ---- 分组骨架（对齐隔壁 SECTIONS 数据驱动：标题+行）----

        static void SectionHeader(RectTransform parent, float x, ref float y, string text,
            TMP_FontAsset font)
        {
            TextMeshProUGUI header = UiKit.CreateText("SectionHeader", parent, text, UiSkin.Font.Section,
                TextAlignmentOptions.MidlineLeft, UiSkin.Gold, font);
            header.enableWordWrapping = false;
            header.rectTransform.anchorMin = header.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            header.rectTransform.pivot = new Vector2(0f, 0.5f);
            header.rectTransform.sizeDelta = new Vector2(860f, 34f);
            header.rectTransform.anchoredPosition = new Vector2(x, y);
            y -= 52f;
        }

        static void RowLabel(RectTransform parent, float x, float y, string text,
            TMP_FontAsset font)
        {
            TextMeshProUGUI label = UiKit.CreateText("RowLabel", parent, text, UiSkin.Font.Hint,
                TextAlignmentOptions.MidlineLeft, UiSkin.TextDim, font);
            label.enableWordWrapping = false;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(120f, 24f);
            label.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        static Button DemoButton(RectTransform parent, float x, float y, float width, float height,
            string label, UiKit.ButtonKind kind, TMP_FontAsset font, bool interactable = true,
            bool withIcon = false)
        {
            Button button = UiKit.ActionButton("Demo_" + label, parent,
                UiGlyphs.Glyph.Helm, label, kind, new Vector2(x + width * 0.5f, y),
                new Vector2(width, height), font, withIcon);
            button.interactable = interactable;
            return button;
        }

        // ---- (1) 按钮 / BUTTON ----

        static void BuildButtonsSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset font)
        {
            SectionHeader(parent, x, ref y, "按钮 / BUTTON", font);
            DemoButton(parent, x, y, 166f, 44f, "普通按钮", UiKit.ButtonKind.Dark, font);
            DemoButton(parent, x + 176f, y, 166f, 44f, "强调按钮", UiKit.ButtonKind.Accent, font);
            DemoButton(parent, x + 352f, y, 166f, 44f, "主行动", UiKit.ButtonKind.Primary, font);
            DemoButton(parent, x + 528f, y, 166f, 44f, "危险按钮", UiKit.ButtonKind.Danger, font);
            DemoButton(parent, x + 704f, y, 156f, 44f, "禁用按钮", UiKit.ButtonKind.Dark, font, interactable: false);
            y -= 56f;

            RowLabel(parent, x, y, "尺寸：", font);
            DemoButton(parent, x + 80f, y, 160f, 26f, "小型 26", UiKit.ButtonKind.Dark, font);
            DemoButton(parent, x + 250f, y, 160f, 32f, "标准 32", UiKit.ButtonKind.Dark, font);
            DemoButton(parent, x + 420f, y, 160f, 44f, "大型 44", UiKit.ButtonKind.Dark, font);
            y -= 56f;

            RowLabel(parent, x, y, "亮背景：", font);
            DemoButton(parent, x + 80f, y, 166f, 44f, "纸面按钮", UiKit.ButtonKind.Paper, font);
            DemoButton(parent, x + 256f, y, 176f, 44f, "纸面·主行动", UiKit.ButtonKind.Paper, font);
            UiKit.IconButton("IconSquare", parent, UiGlyphs.Glyph.Helm,
                new Vector2(x + 512f, y), new Vector2(44f, 44f), UiSkin.InkSoft, UiSkin.TextOnInk);
            y -= 58f;
        }

        // ---- (2) 标签 / LABEL ----

        static void BuildLabelsSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset body, TMP_FontAsset secondary)
        {
            SectionHeader(parent, x, ref y, "标签 / LABEL", secondary);
            FontRow(parent, x, y, "Title " + UiSkin.Font.Title + " —— 界面标题", UiSkin.Font.Title, body);
            y -= 30f;
            FontRow(parent, x, y, "Section " + UiSkin.Font.Section + " —— 区块小标题", UiSkin.Font.Section, body);
            y -= 30f;
            FontRow(parent, x, y, "Body " + UiSkin.Font.Body + " —— 按钮与正文文字", UiSkin.Font.Body, body);
            y -= 30f;
            FontRow(parent, x, y, "Hint " + UiSkin.Font.Hint + " —— 辅助说明文字", UiSkin.Font.Hint, secondary);
            y -= 30f;
            FontRow(parent, x, y, "Tiny " + UiSkin.Font.Tiny + " —— 徽标与角标", UiSkin.Font.Tiny, secondary);
            y -= 36f;

            RowLabel(parent, x, y, "语义色：", secondary);
            var semantics = new (string, Color)[]
            {
                ("强调金", UiSkin.Gold), ("信息", UiSkin.Info), ("警告", UiSkin.Warn),
                ("危险", UiSkin.TeamRedText), ("成功", UiSkin.Success),
            };
            for (int i = 0; i < semantics.Length; i++)
            {
                TextMeshProUGUI label = UiKit.CreateText("Sem" + i, parent, semantics[i].Item1,
                    UiSkin.Font.Body, TextAlignmentOptions.MidlineLeft, semantics[i].Item2, body);
                label.enableWordWrapping = false;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(150f, 26f);
                label.rectTransform.anchoredPosition = new Vector2(x + 90f + i * 158f, y);
            }
            y -= 54f;
        }

        // ---- (3) 输入 / INPUT ----

        static void BuildInputsSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset body, TMP_FontAsset secondary)
        {
            SectionHeader(parent, x, ref y, "输入 / INPUT", secondary);

            RowLabel(parent, x, y, "文本框：", secondary);
            BuildInputField(parent, new Vector2(x + 250f, y), new Vector2(320f, 44f),
                "输入海盗团名…", body);
            y -= 56f;

            RowLabel(parent, x, y, "开关：", secondary);
            BuildToggle(parent, new Vector2(x + 175f, y), new Vector2(170f, 44f), "开（选中）", body, true);
            BuildToggle(parent, new Vector2(x + 335f, y), new Vector2(110f, 44f), "关", body, false);
            BuildCheckBox(parent, new Vector2(x + 467f, y), "复选项（选中）", body, true);
            BuildCheckBox(parent, new Vector2(x + 707f, y), "复选项", body, false);
            y -= 56f;

            RowLabel(parent, x, y, "下拉：", secondary);
            DemoButton(parent, x + 90f, y, 240f, 44f, "中 · 下拉选项", UiKit.ButtonKind.Dark, font: body);
            RowLabel(parent, x + 350f, y, "（完整下拉交互待波次 B）", secondary);
            y -= 58f;
        }

        static void BuildInputField(RectTransform parent, Vector2 center, Vector2 size,
            string placeholder, TMP_FontAsset font)
        {
            RectTransform rect = UiKit.CreateRect("InputField", parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = center;

            var groove = rect.gameObject.AddComponent<Image>();
            groove.sprite = SketchSkin.Frame("groove", 0);
            groove.type = Image.Type.Sliced;
            groove.color = Color.white;
            groove.raycastTarget = true;
            var boil = rect.gameObject.AddComponent<SketchBoil>();
            boil.Slot = "groove";

            var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(rect, false);
            var areaRect = (RectTransform)area.transform;
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(12f, 4f);
            areaRect.offsetMax = new Vector2(-12f, -4f);

            TextMeshProUGUI text = UiKit.CreateText("Text", areaRect.transform, string.Empty,
                UiSkin.Font.Body, TextAlignmentOptions.MidlineLeft, UiSkin.TextOnInk, font);
            UiKit.Stretch(text.rectTransform);

            TextMeshProUGUI hint = UiKit.CreateText("Placeholder", areaRect.transform, placeholder,
                UiSkin.Font.Body, TextAlignmentOptions.MidlineLeft, UiSkin.TextDim, font);
            UiKit.Stretch(hint.rectTransform);

            var input = rect.gameObject.AddComponent<TMPro.TMP_InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = groove;
        }

        static void BuildToggle(RectTransform parent, Vector2 center, Vector2 size, string label,
            TMP_FontAsset font, bool isOn)
        {
            RectTransform rect = UiKit.CreateRect("Toggle_" + label, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = center;

            var back = rect.gameObject.AddComponent<Image>();
            back.sprite = SketchSkin.Frame("btn_normal", 0);
            back.type = Image.Type.Sliced;
            back.color = Color.white;
            back.raycastTarget = true;
            var boil = rect.gameObject.AddComponent<SketchBoil>();
            boil.Slot = "btn_normal";

            Image check = UiKit.CreateGlyph("Check", rect, UiGlyphs.Glyph.Check, UiSkin.Gold);
            check.rectTransform.anchorMin = check.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            check.rectTransform.pivot = new Vector2(0f, 0.5f);
            check.rectTransform.sizeDelta = new Vector2(22f, 22f);
            check.rectTransform.anchoredPosition = new Vector2(12f, 0f);

            TextMeshProUGUI text = UiKit.CreateText("Label", rect, label, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, UiSkin.TextOnInk, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(size.x - 48f, size.y);
            text.rectTransform.anchoredPosition = new Vector2(42f, 0f);

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = back;
            toggle.graphic = check;
            toggle.isOn = isOn;
        }

        static void BuildCheckBox(RectTransform parent, Vector2 center, string label,
            TMP_FontAsset font, bool isOn)
        {
            RectTransform rect = UiKit.CreateRect("Check_" + label, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(34f, 34f);
            rect.anchoredPosition = center;

            var back = rect.gameObject.AddComponent<Image>();
            back.sprite = SketchSkin.Frame("btn_normal", 0);
            back.type = Image.Type.Sliced;
            back.color = Color.white;
            back.raycastTarget = true;
            var boil = rect.gameObject.AddComponent<SketchBoil>();
            boil.Slot = "btn_normal";

            Image check = UiKit.CreateGlyph("Check", rect, UiGlyphs.Glyph.Check, UiSkin.Gold);
            UiKit.Stretch(check.rectTransform, 5f);

            TextMeshProUGUI text = UiKit.CreateText("Label", parent, label, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, UiSkin.TextOnInk, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(190f, 26f);
            text.rectTransform.anchoredPosition = center + new Vector2(26f, 0f);

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = back;
            toggle.graphic = check;
            toggle.isOn = isOn;
        }

        // ---- (4) 页签 / TABS ----

        static void BuildTabsSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset body, TMP_FontAsset secondary)
        {
            SectionHeader(parent, x, ref y, "页签 / TABS", secondary);
            TextMeshProUGUI pageBody = UiKit.CreateText("TabPageBody", parent,
                "「总览」页内容 —— 选中页签带金色马克笔下划线",
                UiSkin.Font.Body, TextAlignmentOptions.MidlineLeft, UiSkin.TextDim, body);
            pageBody.enableWordWrapping = false;
            pageBody.rectTransform.anchorMin = pageBody.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            pageBody.rectTransform.pivot = new Vector2(0f, 0.5f);
            pageBody.rectTransform.sizeDelta = new Vector2(860f, 26f);
            pageBody.rectTransform.anchoredPosition = new Vector2(x, y - 50f);

            string[] titles = { "总览", "编制", "科技" };
            SketchWidgets.Tabs(parent, new Vector2(x + 330f, y), new Vector2(660f, 42f),
                titles, body, 0, index =>
                {
                    pageBody.text = "「" + titles[index] + "」页内容 —— 选中页签带金色马克笔下划线";
                });
            y -= 84f;

            RowLabel(parent, x, y, "独立页签：", secondary);
            SketchWidgets.Tabs(parent, new Vector2(x + 400f, y), new Vector2(440f, 38f),
                new[] { "树", "总览", "指挥链" }, body, 1, null);
            y -= 50f;
        }

        // ---- (5) 滑条与进度 / SLIDER & PROGRESS ----

        static void BuildSlidersSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset body, TMP_FontAsset secondary)
        {
            SectionHeader(parent, x, ref y, "滑条与进度 / SLIDER & PROGRESS", secondary);

            RowLabel(parent, x, y, "滑条：", secondary);
            BuildSlider(parent, new Vector2(x + 330f, y), new Vector2(360f, 30f), 0.65f);
            y -= 56f;

            RowLabel(parent, x, y, "进度条：", secondary);
            RectTransform bar = UiKit.CreateRect("ProgressDemo", parent);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = new Vector2(360f, 22f);
            bar.anchoredPosition = new Vector2(x + 330f, y);
            var track = bar.gameObject.AddComponent<Image>();
            track.sprite = SketchSkin.Frame("progress_bg", 0);
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;
            var trackBoil = bar.gameObject.AddComponent<SketchBoil>();
            trackBoil.Slot = "progress_bg";
            RectTransform fill = UiKit.CreateRect("Fill", bar);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0.4f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = SketchSkin.Frame("progress_fill", 0);
            fillImage.type = Image.Type.Sliced;
            fillImage.color = Color.white;
            fillImage.raycastTarget = false;
            var fillBoil = fill.gameObject.AddComponent<SketchBoil>();
            fillBoil.Slot = "progress_fill";
            y -= 56f;
        }

        static void BuildSlider(RectTransform parent, Vector2 center, Vector2 size, float value)
        {
            RectTransform rect = UiKit.CreateRect("Slider", parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = center;

            var back = rect.gameObject.AddComponent<Image>();
            back.sprite = SketchSkin.Frame("groove", 0);
            back.type = Image.Type.Sliced;
            back.color = Color.white;
            back.raycastTarget = false;
            var backBoil = rect.gameObject.AddComponent<SketchBoil>();
            backBoil.Slot = "groove";

            RectTransform fillArea = UiKit.CreateRect("FillArea", rect);
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(4f, 4f);
            fillArea.offsetMax = new Vector2(-4f, -4f);
            RectTransform fill = UiKit.CreateRect("Fill", fillArea);
            fill.anchorMin = Vector2.zero;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = SketchSkin.Frame("fill", 0);
            fillImage.type = Image.Type.Sliced;
            fillImage.color = UiSkin.Gold;
            fillImage.raycastTarget = false;

            RectTransform handleArea = UiKit.CreateRect("HandleArea", rect);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(14f, 0f);
            handleArea.offsetMax = new Vector2(-14f, 0f);
            RectTransform handle = UiKit.CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(26f, 26f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = SketchSkin.Frame("pip", 0);
            handleImage.type = Image.Type.Simple;
            handleImage.color = UiSkin.Gold;
            handleImage.raycastTarget = true;

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = value;
        }

        // ---- (6) 列表 / ITEM LIST ----

        static void BuildListsSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset body, TMP_FontAsset secondary)
        {
            SectionHeader(parent, x, ref y, "列表 / ITEM LIST", secondary);
            string[] rows =
            {
                "第一海盗团 · 12 人",
                "皇家炮手队 · 8 人",
                "中央领航舰队 · 5 人",
                "北境骨侯团 · 9 人",
                "黑石商队 · 3 人",
            };
            for (int i = 0; i < rows.Length; i++)
            {
                RectTransform row = UiKit.CreateRect("ListRow_" + i, parent);
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
                row.pivot = new Vector2(0f, 0.5f);
                row.sizeDelta = new Vector2(850f, 32f);
                row.anchoredPosition = new Vector2(x, y);
                var back = row.gameObject.AddComponent<Image>();
                back.sprite = SketchSkin.Frame(i % 2 == 0 ? "panel_light" : "groove", 0);
                back.type = Image.Type.Sliced;
                back.color = Color.white;
                back.raycastTarget = false;
                var boil = row.gameObject.AddComponent<SketchBoil>();
                boil.Slot = i % 2 == 0 ? "panel_light" : "groove";
                TextMeshProUGUI text = UiKit.CreateText("Row", row, rows[i], UiSkin.Font.Body,
                    TextAlignmentOptions.MidlineLeft, UiSkin.TextOnInk, body);
                text.enableWordWrapping = false;
                UiKit.Stretch(text.rectTransform, 12f);
                y -= 36f;
            }
            y -= 16f;
        }

        // ---- (7) 反馈 / TOAST & CONFIRM ----

        static void BuildFeedbackSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset body, TMP_FontAsset secondary, RectTransform toastLayer)
        {
            SectionHeader(parent, x, ref y, "反馈 / TOAST & CONFIRM", secondary);
            MakeToastButton(parent, x, y, 190f, "信息 Toast", UiSkin.Info, "这是一条信息通知", body, toastLayer);
            MakeToastButton(parent, x + 200f, y, 190f, "警告 Toast", UiSkin.Warn, "石料储备不足", body, toastLayer);
            MakeToastButton(parent, x + 400f, y, 190f, "错误 Toast", UiSkin.TeamRedText, "编队已溃散", body, toastLayer);
            Button confirm = DemoButton(parent, x + 600f, y, 220f, 44f, "确认框", UiKit.ButtonKind.Primary,
                font: body);
            confirm.onClick.AddListener(() => ShowConfirmDemo(toastLayer, body));
            y -= 56f;
        }

        static void MakeToastButton(RectTransform parent, float x, float y, float width,
            string label, Color color, string message, TMP_FontAsset font, RectTransform toastLayer)
        {
            Button button = DemoButton(parent, x, y, width, 44f, label, UiKit.ButtonKind.Dark, font: font);
            button.onClick.AddListener(() => SketchWidgets.Toast(toastLayer, message, color, font));
        }

        static void ShowConfirmDemo(RectTransform layer, TMP_FontAsset font)
        {
            UiKit.ModalView modal = UiKit.CreateModal("ConfirmDemo", layer, new Vector2(620f, 300f));
            modal.Root.gameObject.SetActive(true);
            TextMeshProUGUI title = UiKit.CreateText("Title", modal.Card, "拆除建筑",
                UiSkin.Font.Title, TextAlignmentOptions.Center, UiSkin.Gold, font);
            UiKit.SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(480f, 48f),
                new Vector2(0f, -36f));
            TextMeshProUGUI body = UiKit.CreateText("Body", modal.Card,
                "拆除后将返还 50% 材料，确定拆除「草棚」吗？",
                UiSkin.Font.Body, TextAlignmentOptions.Center, UiSkin.TextOnInk, font);
            UiKit.SetAnchored(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520f, 60f),
                new Vector2(0f, 10f));
            Button ok = UiKit.ActionButton("Ok", modal.Card, UiGlyphs.Glyph.Check, "拆除",
                UiKit.ButtonKind.Danger, new Vector2(-140f, -96f), new Vector2(220f, 50f), font);
            Button cancel = UiKit.ActionButton("Cancel", modal.Card, UiGlyphs.Glyph.Cross, "取消",
                UiKit.ButtonKind.Dark, new Vector2(140f, -96f), new Vector2(220f, 50f), font);
            ok.onClick.AddListener(() => Object.Destroy(modal.Root));
            cancel.onClick.AddListener(() => Object.Destroy(modal.Root));
        }

        // ---- (8) 键鼠说明 / KEYMAP ----

        static void BuildKeymapSection(RectTransform parent, float x, ref float y,
            TMP_FontAsset secondary)
        {
            SectionHeader(parent, x, ref y, "键鼠说明 / KEYMAP", secondary);
            var keys = new (string, string)[]
            {
                ("左键", "选角色 / 点图标施展武器"),
                ("右键拖动", "转动视角"),
                ("滚轮", "投掷力度蓄力"),
                ("空格", "跳跃"),
            };
            foreach (var (key, desc) in keys)
            {
                SketchWidgets.KeymapRow(parent, new Vector2(x, y), key, desc, secondary);
                y -= 46f;
            }
        }

        // ------------------------------------------------------------------
        // 图标集页
        // ------------------------------------------------------------------

        static void BuildIconsPage(RectTransform root, TMP_FontAsset body,
            TMP_FontAsset secondary, TMP_FontAsset title)
        {
            PageTitle(root, "UI 图标集 · 武器 17 / 职业 7 / 符号 13", title);

            // ---- 武器：格底 = UiSkin.WeaponColor，静物 = Resources/UIIcons ----
            ColumnLabel(root, 0f, "武器（格底与静物同色系；选中金框在战斗 HUD 内体现）", secondary, 380f);
            WeaponId[] weapons = (WeaponId[])System.Enum.GetValues(typeof(WeaponId));
            float cell = 66f, gap = 8f;
            int columns = 9;
            for (int i = 0; i < weapons.Length; i++)
            {
                int column = i % columns, row = i / columns;
                Vector2 center = new Vector2(
                    -330f + column * (cell + gap),
                    322f - row * (cell + 34f));
                WeaponCell(root, weapons[i], center, cell, secondary);
            }

            // ---- 职业：头像 + 名 ----
            ColumnLabel(root, 0f, "职业（两件式底 + 职业标记）", secondary, 150f);
            string[] crew = { "sailor", "gunner", "sniper", "hooker", "arsonist", "skeleton", "captain" };
            string[] crewNames = { "水手", "炮手", "狙击手", "钩子手", "纵火狂", "骷髅", "船长" };
            float step = 96f, x0 = -(crew.Length - 1) * step * 0.5f;
            for (int i = 0; i < crew.Length; i++)
            {
                Vector2 center = new Vector2(x0 + i * step, 84f);
                RectTransform portrait = UiKit.CreateRect("Crew_" + crew[i], root);
                portrait.anchorMin = portrait.anchorMax = portrait.pivot = new Vector2(0.5f, 0.5f);
                portrait.sizeDelta = new Vector2(52f, 52f);
                portrait.anchoredPosition = center;

                var ring = portrait.gameObject.AddComponent<Image>();
                ring.sprite = SketchSkin.Frame("cell", 0);
                ring.type = Image.Type.Sliced;
                ring.color = UiSkin.CrewColor(crew[i]);
                ring.raycastTarget = false;
                var ringBoil = portrait.gameObject.AddComponent<SketchBoil>();
                ringBoil.Slot = "cell";

                Image face = UiKit.CreateRect("Icon", portrait).gameObject.AddComponent<Image>();
                face.sprite = LoadCrewIcon(crew[i]);
                face.type = Image.Type.Simple;
                face.raycastTarget = false;
                UiKit.Stretch(face.rectTransform, 4f);

                Label(root, At(center.x, center.y - 40f), crewNames[i], UiSkin.Font.Hint,
                    UiSkin.TextOnInk, secondary);
            }

            // ---- 符号：UiGlyphs 平涂 ----
            ColumnLabel(root, 0f, "符号（代码平涂；深底暖白 / 金底深墨两用）", secondary, 6f);
            UiGlyphs.Glyph[] glyphs =
            {
                UiGlyphs.Glyph.Crosshair, UiGlyphs.Glyph.Eye, UiGlyphs.Glyph.MovePad,
                UiGlyphs.Glyph.Pause, UiGlyphs.Glyph.Play, UiGlyphs.Glyph.Check,
                UiGlyphs.Glyph.Cross, UiGlyphs.Glyph.Skull, UiGlyphs.Glyph.Star,
                UiGlyphs.Glyph.Retry, UiGlyphs.Glyph.Helm, UiGlyphs.Glyph.Flag,
                UiGlyphs.Glyph.ThrowArc,
            };
            float gstep = 62f, gx0 = -(glyphs.Length - 1) * gstep * 0.5f;
            for (int i = 0; i < glyphs.Length; i++)
            {
                Image glyph = UiKit.CreateGlyph("Glyph_" + glyphs[i], root, glyphs[i],
                    i % 2 == 0 ? UiSkin.TextOnInk : UiSkin.Gold);
                glyph.rectTransform.sizeDelta = new Vector2(34f, 34f);
                glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax =
                    glyph.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                glyph.rectTransform.anchoredPosition = new Vector2(gx0 + i * gstep, -58f);
            }

            Footer(root, secondary);
        }

        static void WeaponCell(RectTransform root, WeaponId id, Vector2 center, float cell,
            TMP_FontAsset secondary)
        {
            RectTransform rect = UiKit.CreateRect("Weapon_" + id, root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(cell, cell);
            rect.anchoredPosition = center;

            var frame = rect.gameObject.AddComponent<Image>();
            frame.sprite = SketchSkin.Frame("cell", 0);
            frame.type = Image.Type.Sliced;
            frame.color = UiSkin.WeaponCellBase(id);
            frame.raycastTarget = false;
            var weaponBoil = rect.gameObject.AddComponent<SketchBoil>();
            weaponBoil.Slot = "cell";

            Image icon = UiKit.CreateRect("Icon", rect).gameObject.AddComponent<Image>();
            icon.sprite = LoadWeaponIcon(id);
            icon.type = Image.Type.Simple;
            icon.raycastTarget = false;
            UiKit.Stretch(icon.rectTransform, 8f);

            Label(root, At(center.x, center.y - cell * 0.5f - 12f), WeaponShortName(id),
                UiSkin.Font.Tiny, UiSkin.TextDim, secondary);
        }

        static string WeaponShortName(WeaponId id)
        {
            string name = id.ToString();
            var map = new Dictionary<string, string>
            {
                { "Cannonball", "铁球" }, { "CherryBomb", "樱桃弹" }, { "Dynamite", "炸药" },
                { "Boulder", "巨石" }, { "Banana", "香蕉" }, { "Mine", "水雷" },
                { "ParachuteBomb", "伞弹" }, { "RumBottle", "朗姆瓶" },
                { "PiecesOfEight", "金币" }, { "GunpowderBarrel", "火药桶" },
                { "WoodenCrate", "木箱" }, { "Anchor", "铁锚" }, { "Seagull", "海鸥" },
                { "TidalWave", "潮浪" }, { "VoodooDoll", "巫毒娃娃" }, { "Cannon", "大炮" },
                { "SweepingFlame", "烈焰" },
            };
            return map.TryGetValue(name, out string zh) ? zh : name;
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        static Vector2 At(float x, float y) => new Vector2(x, y);

        static void PageTitle(RectTransform root, string content, TMP_FontAsset title)
        {
            TextMeshProUGUI text = UiKit.CreateText("PageTitle", root, content, UiSkin.Font.Title,
                TextAlignmentOptions.Center, UiSkin.Gold, title);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(1200f, 48f);
            text.rectTransform.anchoredPosition = new Vector2(0f, 470f);
        }

        /// <summary>左对齐标签（x = 画布中心系左端点；anchor 0.5,0.5 + pivot 左中）。
        /// 列标题 / 字号样例行 / 色板名共用——r9 出图裁决的教训：anchor(0,·) 左缘锚
        /// 与中心系坐标混用会把内容摆出屏幕（列标题被裁、字号行整列缺失）。</summary>
        static void LeftLabel(RectTransform root, float x, float y, float width, string content,
            int size, Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", root, content, size,
                TextAlignmentOptions.MidlineLeft, color, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                new Vector2(0.5f, 0.5f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(width, size + 10f);
            text.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        static void ColumnLabel(RectTransform root, float x, string content,
            TMP_FontAsset secondary, float y = 388f)
        {
            LeftLabel(root, x - 280f, y, 560f, content, UiSkin.Font.Section, UiSkin.TextDim, secondary);
        }

        static void FontRow(RectTransform root, float x, float y, string content, int size,
            TMP_FontAsset font)
        {
            LeftLabel(root, x, y, 560f, content, size, UiSkin.TextOnInk, font);
        }

        static void Label(RectTransform root, Vector2 position, string content, int size,
            Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", root, content, size,
                TextAlignmentOptions.Center, color, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(160f, size + 8f);
            text.rectTransform.anchoredPosition = position;
        }

        static void Footer(RectTransform root, TMP_FontAsset secondary)
        {
            TextMeshProUGUI text = UiKit.CreateText("Footer", root,
                "颜色 / 字号 / 圆角全部出自 UiSkin Token——改一处常量，本页与全部界面同步变化（视觉回归自检页）",
                UiSkin.Font.Hint, TextAlignmentOptions.Center, UiSkin.TextDim, secondary);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(1400f, 24f);
            text.rectTransform.anchoredPosition = new Vector2(0f, -496f);
        }

        static Sprite LoadWeaponIcon(WeaponId id)
        {
            Sprite sprite = Resources.Load<Sprite>("UIIcons/Weapon_" + id);
            return sprite != null ? sprite : UiGlyphs.Get(UiGlyphs.Glyph.ThrowArc);
        }

        static Sprite LoadCrewIcon(string crewKey)
        {
            Sprite sprite = Resources.Load<Sprite>("UIIcons/Crew_" + crewKey);
            return sprite != null ? sprite : UiGlyphs.Get(UiGlyphs.Glyph.Helm);
        }
    }
}
