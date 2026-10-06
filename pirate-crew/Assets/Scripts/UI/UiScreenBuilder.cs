using PirateCrew.Campaign;
using PirateCrew.Settings;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 三张菜单/管理屏的**运行时 UI 构建器**（MainMenu / CrewManagement / LevelSelect）。
    ///
    /// 【为什么在运行时】此前这三屏由编辑器装配器（SceneSetup / ManagementSceneSetup）烘进场景
    /// 再折叠成 Prefab——「场景烘死 UI」是装配链里唯一反行业的结构；本类把构建逻辑搬进运行时
    /// 程序集，场景只留 相机 + EventSystem + <see cref="UiScreenBoot"/>，页面加载时自建
    /// （先例：UIShowcase 的 UiShowcaseBoot 模式）。装配器仍在，但只负责生成最小场景。
    ///
    /// 【纪律与出处】全部几何/皮/字号来自 theme 直切件链（UiKit/PixelSkin/AseLayout/UiSkin），
    /// 与原编辑器装配逐值一致——移植自 SceneSetup / ManagementSceneSetup / MenuUiBuilder 的
    /// 对应方法，注释里的像素纪律与实拍依据原样保留。
    ///
    /// 【接线契约】构建器只建控件不碰真实服务；返回 refs 结构交给
    /// <see cref="UiScreenBoot"/> 注入各控制器（原 [SerializeField] 字段名零改动）。
    /// </summary>
    public static class UiScreenBuilder
    {
        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCenterAnchor = new Vector2(0.5f, 1f);
        static readonly Vector2 BottomCenterAnchor = new Vector2(0.5f, 0f);

        // ------------------------------------------------------------------
        // 字体（运行时入口；与编辑器 MenuUiBuilder 的资产同源，Resources 副本）
        // ------------------------------------------------------------------

        /// <summary>标题档（正格点黑16）。字号由各 CreateText 的档位 + ResolvePixelFont 纠偏，
        /// 传入字体只是兜底——像素纪律在 <see cref="CreateTextExact"/> 单点执行。</summary>
        public static TMP_FontAsset TitleFont =>
            _title != null ? _title : (_title = Resources.Load<TMP_FontAsset>("Fonts/ZhengGeDianHei16"));
        static TMP_FontAsset _title;

        public static TMP_FontAsset BodyFont =>
            _body != null ? _body : (_body = Resources.Load<TMP_FontAsset>("Fonts/FusionPixel12"));
        static TMP_FontAsset _body;

        public static TMP_FontAsset SecondaryFont =>
            _secondary != null ? _secondary : (_secondary = Resources.Load<TMP_FontAsset>("Fonts/FusionPixel10"));
        static TMP_FontAsset _secondary;

        // ------------------------------------------------------------------
        // 文本 / 按钮工厂（MenuUiBuilder 同名方法的运行时版，像素口径逐行一致）
        // ------------------------------------------------------------------

        /// <summary>建 TMP 文本：字号档 + ResolvePixelFont 单点纠偏 + 图集 Point + PixelSnapText
        /// 顶点取整（半格糊字缺口的三件套，见 MenuUiBuilder.CreateTextExact 注释）。</summary>
        public static TextMeshProUGUI CreateTextExact(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false)
        {
            RectTransform rect = RuntimeUiBuilder.CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            TMP_FontAsset resolved = UiKit.ResolvePixelFont(fontSize, font);
            if (resolved != null)
            {
                text.font = resolved;
                PixelAtlasPointFilter.Ensure(resolved);
            }
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = raycast;
            text.gameObject.AddComponent<PixelSnapText>();   // 顶点像素对齐（治半格糊字）
            return text;
        }

        /// <summary>令牌按钮尺寸（宽 = 标签宽 + 24 艺术像素，高 24 艺术像素；= UiSkin.Px.ButtonWidth）。</summary>
        public static Vector2 ButtonSize(string label)
        {
            return new Vector2(UiSkin.Px.ButtonWidth(label), UiSkin.Px.Button);
        }

        /// <summary>SketchButton（theme button 四态皮 + 双层禁用字）；fontSize 0 = 控件默认正文档。</summary>
        public static SketchButton CreateSketchButton(string name, Transform parent, string label,
            Vector2 anchor, Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font, int fontSize = 0)
        {
            return SketchButton.Create(parent, name, anchor, new Vector2(0.5f, 0.5f),
                anchoredPosition, size, font, label, fontSize);
        }

        /// <summary>全屏底板：theme 桌面色不透明层，不拦截点击（相机清屏色同源，UI 层统一换皮）。</summary>
        public static void CreateBackdrop(Transform parent)
        {
            RectTransform rect = RuntimeUiBuilder.CreateRect("WindowBackdrop", parent);
            RuntimeUiBuilder.Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            Color bg = PixelSkin.Theme.Face;   // theme window_face #2C2C30
            bg.a = 1f;
            image.color = bg;
            image.raycastTarget = false;
        }

        /// <summary>模态压暗遮罩：黑 60%，拦截点击承载模态语义。</summary>
        public static void CreateDimOverlay(Transform parent)
        {
            RectTransform rect = RuntimeUiBuilder.CreateRect("DimOverlay", parent);
            RuntimeUiBuilder.Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            image.raycastTarget = true;
        }

        // ------------------------------------------------------------------
        // 主菜单
        // ------------------------------------------------------------------

        /// <summary>主菜单构建产物（MainMenuController.Bind 的入参；字段名 = 原 SerializeField 名）。</summary>
        public sealed class MainMenuRefs
        {
            public SketchButton BattleButton;
            public SketchButton CrewButton;
            public SketchButton SettingsButton;
            public SketchButton ShowcaseButton;
            public SketchButton QuitButton;
            public TextMeshProUGUI StatusText;
            public TextMeshProUGUI VersionText;
            public GameObject SettingsPanel;
            public Button SettingsBackButton;
            public Button SettingsCloseButton;
            public Button SettingsRestoreButton;
            public Slider MasterSlider;
            public Slider SfxSlider;
            public Slider MusicSlider;
            public Slider AmbientSlider;
            public Button QualityHighButton;
            public Button QualitySmoothButton;
            public Button FullscreenOnButton;
            public Button FullscreenOffButton;
            public Button PixelScale2Button;
            public Button PixelScale3Button;
            public Button PixelScale4Button;
            public Button PixelScaleAutoButton;
            public Button ResolutionButton;
            public GameObject QuitConfirmPanel;
            public Button QuitConfirmOkButton;
            public Button QuitConfirmCancelButton;
        }

        /// <summary>
        /// 建主菜单（窗体化：window_with_title 装标题带 + 按钮列）。几何口径见各段注释
        /// （移植自 SceneSetup.BuildMainMenuScene）。
        /// </summary>
        public static MainMenuRefs BuildMainMenu(Transform canvas)
        {
            TMP_FontAsset hand = TitleFont;

            CreateBackdrop(canvas);

            // 主菜单**窗体化**（执行案 §4，创始人 2026-09-25 裁决）：window_with_title 容器
            // 装标题带 + 按钮列。几何（×1 设计格）：标题带 15 原生，带下内容顶随标题字高动态
            // （EnsureWindow 量行高：12 号 → 23）；按钮高 24（参考库 OK 钮）；相邻钮间距 =
            // 上钮下切片 6 + 下钮上切片 4 = 10；下 border 6。
            RectTransform menuWindow = RuntimeUiBuilder.CreateRect("MenuWindow", canvas);
            UiKit.EnsureWindow(menuWindow, PixelTone.Frame, UiStrings.MainTitle,
                hand, UiSkin.Font.Body, helpButton: false, closeButton: false);
            float contentTop = UiKit.WindowContentTopOf(menuWindow);
            Vector2 windowSize = new Vector2(132f, contentTop + 24f * 5 + 10f * 4 + 6f);
            // 奇高 + 中心锚 → 缘落半格（横线变浅的元凶）：y 补 -0.5 回整数格
            float yNudge = Mathf.Abs(windowSize.y % 2f) > 0.01f ? -0.5f : 0f;
            RuntimeUiBuilder.SetAnchored(menuWindow, CenterAnchor, windowSize, new Vector2(0f, -6.5f + yNudge));

            var menuColumn = menuWindow.gameObject.GetComponent<VerticalLayoutGroup>();
            if (menuColumn == null)
                menuColumn = menuWindow.gameObject.AddComponent<VerticalLayoutGroup>();
            menuColumn.padding = new RectOffset(
                (int)AseLayout.Px(AseLayout.WindowBorder), (int)AseLayout.Px(AseLayout.WindowBorder),
                (int)AseLayout.Px(contentTop), (int)AseLayout.Px(AseLayout.WindowBorder));
            menuColumn.spacing = AseLayout.Px(10);   // 按钮下切片 6 + 按钮上切片 4
            menuColumn.childControlWidth = false;
            menuColumn.childControlHeight = false;
            menuColumn.childForceExpandWidth = false;
            menuColumn.childForceExpandHeight = false;
            menuColumn.childAlignment = TextAnchor.UpperCenter;

            var refs = new MainMenuRefs();
            refs.BattleButton = CreateMenuButton(menuWindow, "BattleButton", UiStrings.MainBattle, hand);
            refs.CrewButton = CreateMenuButton(menuWindow, "CrewButton", UiStrings.MainCrew, hand);
            refs.SettingsButton = CreateMenuButton(menuWindow, "SettingsButton", UiStrings.MainSettings, hand);
            refs.ShowcaseButton = CreateMenuButton(menuWindow, "ShowcaseButton", UiStrings.MainShowcase, hand);
            refs.QuitButton = CreateMenuButton(menuWindow, "QuitButton", UiStrings.MainQuit, hand);

            // 左下：版本号 + 存档状态（安全边距 12；角标 Tiny 8 原生档 + 亮字 32%/55% 两档）。
            refs.VersionText = CreateTextExact("VersionText", canvas,
                UiStrings.MainVersionPrefix + " " + Application.version, UiSkin.Font.Tiny,
                TextAlignmentOptions.BottomLeft, new Color(0.93f, 0.94f, 0.96f, 0.32f), hand);
            RuntimeUiBuilder.SetAnchored(refs.VersionText.rectTransform,
                new Vector2(0f, 0f), new Vector2(133f, 12f), new Vector2(12f, 12f));

            refs.StatusText = CreateTextExact("StatusText", canvas, string.Empty,
                UiSkin.Font.Tiny, TextAlignmentOptions.BottomLeft,
                new Color(0.93f, 0.94f, 0.96f, 0.55f), hand);
            RuntimeUiBuilder.SetAnchored(refs.StatusText.rectTransform,
                new Vector2(0f, 0f), new Vector2(167f, 12f), new Vector2(12f, 12f + 13f));

            // 设置界面（真接线：音量滑条 ×4 / 画质档 / 窗口模式 / 比例 / 分辨率；默认隐藏）。
            SettingsPanelResult settings = BuildSettingsPanel(canvas);
            refs.SettingsPanel = settings.Root;
            refs.SettingsBackButton = settings.BackButton;
            refs.SettingsCloseButton = settings.CloseButton;
            refs.SettingsRestoreButton = settings.RestoreButton;
            refs.MasterSlider = settings.MasterSlider;
            refs.SfxSlider = settings.SfxSlider;
            refs.MusicSlider = settings.MusicSlider;
            refs.AmbientSlider = settings.AmbientSlider;
            refs.QualityHighButton = settings.QualityHighButton;
            refs.QualitySmoothButton = settings.QualitySmoothButton;
            refs.FullscreenOnButton = settings.FullscreenOnButton;
            refs.FullscreenOffButton = settings.FullscreenOffButton;
            refs.PixelScale2Button = settings.PixelScale2Button;
            refs.PixelScale3Button = settings.PixelScale3Button;
            refs.PixelScale4Button = settings.PixelScale4Button;
            refs.PixelScaleAutoButton = settings.PixelScaleAutoButton;
            refs.ResolutionButton = settings.ResolutionButton;

            // 退出确认框（默认隐藏；theme window 直切件底板）。
            ConfirmDialogResult quitConfirm = BuildConfirmDialog(canvas, UiStrings.MainQuitConfirm);
            refs.QuitConfirmPanel = quitConfirm.Root;
            refs.QuitConfirmOkButton = quitConfirm.OkButton;
            refs.QuitConfirmCancelButton = quitConfirm.CancelButton;

            return refs;
        }

        /// <summary>主菜单窗体按钮列的按钮：LayoutElement 声明高 24（参考库 OK 钮），
        /// 宽 = 标签宽 + 8（UiSkin.Px.ButtonWidth）；字号 0 = 控件默认正文档。</summary>
        static SketchButton CreateMenuButton(Transform parent, string name, string label, TMP_FontAsset font)
        {
            SketchButton button = CreateSketchButton(name, parent, label,
                new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(label), font);
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = UiSkin.Px.Button;
            element.minHeight = UiSkin.Px.Button;
            return button;
        }

        // ------------------------------------------------------------------
        // 设置面板（自 MenuUiBuilder 移入运行时；接线纪律原样：只建控件不碰服务）
        // ------------------------------------------------------------------

        /// <summary>设置面板构建产物（控制器按字段接线）。</summary>
        public sealed class SettingsPanelResult
        {
            public GameObject Root;
            public Button BackButton;
            public Button CloseButton;
            public Button RestoreButton;
            public Slider MasterSlider;
            public Slider SfxSlider;
            public Slider MusicSlider;
            public Slider AmbientSlider;
            public Button QualityHighButton;
            public Button QualitySmoothButton;
            public Button FullscreenOnButton;
            public Button FullscreenOffButton;
            public Button PixelScale2Button;
            public Button PixelScale3Button;
            public Button PixelScale4Button;
            public Button PixelScaleAutoButton;
            public Button ResolutionButton;
        }

        /// <summary>
        /// 搭「设置」界面（默认隐藏）：四条音量滑条 + 画质档 + 窗口模式 + 像素比例 + 分辨率 +
        /// 恢复默认/返回。【接线纪律】本方法只建控件，不改任何真实设置；应用与持久化都在控制器里做。
        /// </summary>
        public static SettingsPanelResult BuildSettingsPanel(Transform canvas)
        {
            TMP_FontAsset hand = TitleFont;

            RectTransform root = RuntimeUiBuilder.CreateRect("SettingsPanel", canvas);
            RuntimeUiBuilder.Stretch(root);

            CreateDimOverlay(root);

            // 底板：**带标题窗体**（theme window 直切件）。【尺寸偶数纪律】卡 426 + 中心锚：
            // 奇数宽居中会让左右缘落 x.5 画布格；内层行的宽同样取偶（384）。
            RectTransform panel = RuntimeUiBuilder.CreateRect("SettingsCard", root);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(426f, 384f);
            UiKit.EnsureWindow(panel, PixelTone.Frame, UiStrings.SettingsTitle, hand, UiSkin.Font.Body,
                helpButton: false, closeButton: false);

            // 右上窗控钮：theme window_button（9×11 件 + window_close_icon），行为 = CloseSettings。
            Button settingsCloseButton = UiKit.CreateWindowButton(panel, "SettingsCloseButton",
                PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Close),
                AseLayout.Px(AseLayout.CloseButtonMarginRight));

            var result = new SettingsPanelResult
            {
                Root = root.gameObject,   // 【必赋】漏赋 → 控制器 OpenSettings 首行判空早退，面板开不出来
                CloseButton = settingsCloseButton,
            };

            // 行容器：十行流式纵排（缝 3u）。高 277 = 2 线×13 + 8 行×28 + 9 缝×3
            // （行高取偶、线行取奇——奇偶相位注释见行高常量处）。
            RectTransform rows = UiKit.CreateRect("Rows", panel);
            rows.pivot = new Vector2(0.5f, 1f);
            UiKit.SetAnchored(rows, new Vector2(0.5f, 1f), new Vector2(384f, 277f), new Vector2(0f, -40f));
            UiLayout.VBox(rows, 3, default(UiPadding));

            BuildSettingsGroupLabel(rows, "GroupAudio", UiStrings.SettingsGroupAudio, hand);
            result.MasterSlider = BuildVolumeRow(rows, 0, UiStrings.SettingsFieldVolumeMaster, hand);
            result.SfxSlider = BuildVolumeRow(rows, 1, UiStrings.SettingsFieldVolumeSfx, hand);
            result.MusicSlider = BuildVolumeRow(rows, 2, UiStrings.SettingsFieldVolumeMusic, hand);
            result.AmbientSlider = BuildVolumeRow(rows, 3, UiStrings.SettingsFieldVolumeAmbient, hand);

            BuildSettingsGroupLabel(rows, "GroupVideo", UiStrings.SettingsGroupVideo, hand);
            BuildSettingsRow(rows, 4, UiStrings.SettingsFieldQuality, hand,
                out result.QualityHighButton, out result.QualitySmoothButton,
                UiStrings.SettingsOptionQualityHigh, UiStrings.SettingsOptionQualitySmooth);
            BuildSettingsRow(rows, 5, UiStrings.SettingsFieldWindowMode, hand,
                out result.FullscreenOnButton, out result.FullscreenOffButton,
                UiStrings.SettingsOptionFullscreen, UiStrings.SettingsOptionWindowed);
            BuildSettingsRowN(rows, 6, UiStrings.SettingsFieldPixelScale, hand,
                out result.PixelScale2Button, out result.PixelScale3Button,
                out result.PixelScale4Button, out result.PixelScaleAutoButton,
                UiStrings.SettingsOptionPixelScale2, UiStrings.SettingsOptionPixelScale3,
                UiStrings.SettingsOptionPixelScale4, UiStrings.SettingsOptionPixelScaleAuto);
            result.ResolutionButton = BuildResolutionRow(rows, 7, UiStrings.SettingsFieldResolution, hand);

            // 提示：角标档 + theme status_bar_text 灰，放底部通带（行区底 67 与按钮行之间）。
            TextMeshProUGUI note = CreateTextExact("SaveHint", panel, UiStrings.SettingsSaveHint,
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.Theme.StatusText, hand);
            RuntimeUiBuilder.SetAnchored(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(394f, 12f),
                new Vector2(0f, 54f));

            // 恢复默认 / 返回：同排左右对称（x=±100、y=26）。
            result.RestoreButton = CreateSketchButton("RestoreButton", panel, UiStrings.SettingsRestore,
                new Vector2(0.5f, 0f), new Vector2(-100f, 26f), ButtonSize(UiStrings.SettingsRestore), BodyFont);
            result.BackButton = CreateSketchButton("SettingsBackButton", panel, UiStrings.Back,
                new Vector2(0.5f, 0f), new Vector2(100f, 26f), ButtonSize(UiStrings.Back), BodyFont);

            root.gameObject.SetActive(false);

            return result;
        }

        /// <summary>建一行「字段名 + 音量滑条」（theme slider_empty/full 两件 + 双色数值层；
        /// 件来源与 paintSlider 实锄口径见原 MenuUiBuilder 版注释，逐值一致）。</summary>
        static Slider BuildVolumeRow(Transform rows, int index, string field, TMP_FontAsset hand)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            TextMeshProUGUI fieldLabel = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, hand);
            RuntimeUiBuilder.SetAnchored(fieldLabel.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(120f, 15f), new Vector2(8f, 0f));

            RectTransform sliderRect = RuntimeUiBuilder.CreateRect("Slider", row);
            RuntimeUiBuilder.SetAnchored(sliderRect, new Vector2(1f, 0.5f), new Vector2(186f, 16f), new Vector2(-8f, 0f));
            var sliderBack = sliderRect.gameObject.AddComponent<Image>();
            sliderBack.sprite = PixelSkin.SliderEmpty(false);   // theme slider_empty（凹槽九宫格）
            sliderBack.type = Image.Type.Sliced;
            sliderBack.pixelsPerUnitMultiplier = 1f;            // ×1 终局：贴图纹素 = 画布像素
            sliderBack.color = Color.white;                     // 像素件禁止乘色
            sliderBack.raycastTarget = true;                    // 无拇指：整条槽即拖拽/命中区

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.targetGraphic = sliderBack;
            slider.transition = Selectable.Transition.None;   // theme slider 无悬停态；禁 ColorTint 护调色板

            RectTransform fill = RuntimeUiBuilder.CreateRect("Fill", sliderRect);
            RuntimeUiBuilder.Stretch(fill);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = PixelSkin.SliderFull(false);   // theme slider_full（内芯，比空槽暗=充满）
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 1f;
            fillImage.color = Color.white;
            fillImage.raycastTarget = false;
            slider.fillRect = fill;
            // 无 handleRect：theme slider 没有拇指件，拖拽落点由 Slider 自身 rect 推算。

            // 数值文本【双色口径，paintSlider 源码实锄】：同文案画两遍——充满段上亮字、
            // 空槽段上暗字，分界线穿字形中间逐像素换色（RectMask2D 裁剪框恒宽，不随伸缩）。
            RectTransform clipFull = RuntimeUiBuilder.CreateRect("ClipFull", sliderRect);
            clipFull.anchorMin = new Vector2(0f, 0f);
            clipFull.anchorMax = new Vector2(0f, 1f);
            clipFull.pivot = new Vector2(0f, 0.5f);
            clipFull.anchoredPosition = Vector2.zero;
            clipFull.sizeDelta = Vector2.zero;
            clipFull.gameObject.AddComponent<RectMask2D>();

            RectTransform clipRest = RuntimeUiBuilder.CreateRect("ClipRest", sliderRect);
            clipRest.anchorMin = new Vector2(0f, 0f);
            clipRest.anchorMax = new Vector2(1f, 1f);
            clipRest.offsetMin = Vector2.zero;
            clipRest.offsetMax = Vector2.zero;
            clipRest.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI valueLight = CreateTextExact("ValueLight", clipFull, Percent(slider.value),
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.Theme.Text, hand);
            valueLight.enableWordWrapping = false;
            valueLight.rectTransform.anchorMin = new Vector2(0f, 0f);
            valueLight.rectTransform.anchorMax = new Vector2(0f, 1f);
            valueLight.rectTransform.pivot = new Vector2(0f, 0.5f);
            valueLight.rectTransform.anchoredPosition = Vector2.zero;
            valueLight.rectTransform.sizeDelta = new Vector2(186f, 0f);

            TextMeshProUGUI valueDark = CreateTextExact("ValueDark", clipRest, Percent(slider.value),
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.Theme.Disabled, hand);
            valueDark.enableWordWrapping = false;
            valueDark.rectTransform.anchorMin = new Vector2(1f, 0f);
            valueDark.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueDark.rectTransform.pivot = new Vector2(1f, 0.5f);
            valueDark.rectTransform.anchoredPosition = Vector2.zero;
            valueDark.rectTransform.sizeDelta = new Vector2(186f, 0f);

            // 数值刷新走件（帧对齐），不挂 onValueChanged（控制器刷新走 SetValueWithoutNotify）。
            var valueLabel = sliderRect.gameObject.AddComponent<SliderValueLabel>();
            valueLabel.LabelLight = valueLight;
            valueLabel.LabelDark = valueDark;
            return slider;
        }

        /// <summary>滑条百分比文案（0..1 → "0%".."100%"）。</summary>
        static string Percent(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        /// <summary>设置行高 = theme slider 件高 16 + 上下各 6 格呼吸。**必须偶数**（行内居中子件落整格）。</summary>
        const float SettingsRowHeight = 28f;

        /// <summary>分组标签行高。**必须奇数**（分隔线盒高 5 的居中偏移取整）。</summary>
        const float SettingsGroupRowHeight = 13f;

        /// <summary>选项块字号 = theme buttonset_item 的 mini 档映射到本工程最小原生档 8（禁放大）。</summary>
        const int ButtonSetFontSize = UiSkin.Font.Tiny;

        /// <summary>一对选项块之间的缝（theme 未声明，取 4 格）。</summary>
        const float ButtonSetGap = 4f;

        /// <summary>设置行底板：theme list_item 纯色面（#41444A），行位交 VBox 排。</summary>
        static RectTransform CreateSettingsRowBackground(Transform rows, int index)
        {
            RectTransform row = UiKit.CreateRect("Row" + index, rows);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(384f, SettingsRowHeight);

            var image = row.gameObject.AddComponent<Image>();
            image.color = PixelSkin.Theme.Background;   // theme listitem_normal_face 纯色
            image.raycastTarget = false;
            return row;
        }

        /// <summary>建一行「字段名 + 二选一选项块」（theme buttonset_item，右对齐等宽一对）。</summary>
        static void BuildSettingsRow(Transform rows, int index, string field, TMP_FontAsset hand,
            out Button primaryOption, out Button secondaryOption, string primaryLabel, string secondaryLabel)
        {
            BuildSettingsRowN(rows, index, field, hand,
                out primaryOption, out secondaryOption, out _, out _,
                primaryLabel, secondaryLabel, string.Empty, string.Empty);
            // 两档版复用 N 档实现：多建的两块空档位不建（N 版对空标签跳过）。
        }

        /// <summary>建一行「字段名 + N 个等宽选项块」（右对齐成组，从右往左排；宽按最长标签定）。</summary>
        static void BuildSettingsRowN(Transform rows, int index, string field, TMP_FontAsset hand,
            out Button option0, out Button option1, out Button option2, out Button option3,
            string label0, string label1, string label2, string label3)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            TextMeshProUGUI fieldLabel = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, hand);
            RuntimeUiBuilder.SetAnchored(fieldLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 15f),
                new Vector2(8f, 0f));

            string[] labels = { label0, label1, label2, label3 };
            Button[] options = { null, null, null, null };
            int count = 0;
            float maxLen = 0f;
            foreach (string t in labels)
            {
                if (string.IsNullOrEmpty(t))
                    continue;
                count++;
                maxLen = Mathf.Max(maxLen, t.Length);
            }

            float itemWidth = maxLen * ButtonSetFontSize + 2f * (AseLayout.Px(AseLayout.CheckBorder) + 3f);
            for (int i = labels.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(labels[i]))
                    continue;
                int filled = 0;
                for (int j = labels.Length - 1; j > i; j--)
                    if (!string.IsNullOrEmpty(labels[j]))
                        filled++;
                float x = -6f - filled * (itemWidth + ButtonSetGap);
                options[i] = SketchButtonSet.Create(row, "Option" + i, labels[i], hand,
                    ButtonSetFontSize, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(x, 0f), new Vector2(itemWidth, 16f));
            }

            option0 = options[0];
            option1 = options[1];
            option2 = options[2];
            option3 = options[3];
        }

        /// <summary>建一行「字段名 + 分辨率循环钮」（钮宽按占位串定死；候选与刷新在控制器）。</summary>
        static Button BuildResolutionRow(Transform rows, int index, string field, TMP_FontAsset hand)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            TextMeshProUGUI fieldLabel = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, hand);
            RuntimeUiBuilder.SetAnchored(fieldLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 15f),
                new Vector2(8f, 0f));

            return CreateSketchButton("ResolutionButton", row,
                UiStrings.SettingsOptionResolutionPlaceholder,
                new Vector2(1f, 0.5f), new Vector2(-6f, 0f),
                ButtonSize(UiStrings.SettingsOptionResolutionPlaceholder), BodyFont);
        }

        /// <summary>建**蓝字分组线**行（theme horizontal_separator 复刻：线从标签右缘之后起铺，
        /// 「线让开字」画法——theme 的分隔线三层会压字，实拍穿模）。</summary>
        static void BuildSettingsGroupLabel(Transform rows, string name, string label, TMP_FontAsset hand)
        {
            RectTransform row = UiKit.CreateRect(name, rows);
            row.sizeDelta = new Vector2(384f, SettingsGroupRowHeight);

            TextMeshProUGUI text = CreateTextExact("Label", row, label, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.SeparatorLabel, hand);
            RuntimeUiBuilder.SetAnchored(text.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(120f, SettingsGroupRowHeight),
                new Vector2(AseLayout.Px(AseLayout.SeparatorTextX), 0f));

            float lineX = AseLayout.Px(AseLayout.SeparatorTextX)
                + Mathf.Ceil(text.preferredWidth) + AseLayout.Px(AseLayout.SeparatorBorder);
            SketchSeparator.Create(row, "Line", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(lineX, 0f), new Vector2(384f - lineX, 1f),
                SketchSeparator.Direction.Horizontal);
        }

        // ------------------------------------------------------------------
        // 确认弹窗
        // ------------------------------------------------------------------

        /// <summary>确认弹窗构建产物。</summary>
        public sealed class ConfirmDialogResult
        {
            public GameObject Root;
            public TextMeshProUGUI Message;
            public Button OkButton;
            public Button CancelButton;
        }

        /// <summary>
        /// 搭通用确认弹窗（默认隐藏）：走 <see cref="UiKit.CreateModal"/> 标准模态路径——
        /// Dim 遮罩 + theme window 直切窗体皮 + 标题带 + 右上 × + 卡片高随内容。
        /// 与战斗侧返回确认同形同数（卡宽 160 / 正文 134×20 / 缝 3u）。语义由调用方定义。
        /// </summary>
        public static ConfirmDialogResult BuildConfirmDialog(Transform canvas, string defaultMessage)
        {
            UiKit.ModalView modal = UiKit.CreateModal("ConfirmDialog", canvas, new Vector2(160f, 68f),
                title: UiStrings.ConfirmTitle, titleFont: BodyFont, titleFontSize: UiSkin.Font.Body);

            RectTransform flow = RuntimeUiBuilder.CreateRect("Flow", modal.Card);
            UiLayout.Flexible(flow.gameObject);
            UiLayout.VBox(flow, 3, UiPadding.Uniform(2), alignment: TextAnchor.MiddleCenter, controlHeights: true);

            // 正文：窗体面上的正文档暖白字。【宽度纪律】VBox 只接管高度——rect 宽必须自己给，
            // 否则回落默认 100 宽把短文案挤成两行；短文案禁换行。
            TextMeshProUGUI message = UiKit.CreateText("Message", flow, defaultMessage,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Frame), BodyFont);
            message.enableWordWrapping = false;
            message.rectTransform.sizeDelta = new Vector2(134f, 20f);
            UiLayout.Element(message.gameObject, 134f, 20f);

            RectTransform actionRow = RuntimeUiBuilder.CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);

            return new ConfirmDialogResult
            {
                Root = modal.Root,
                Message = message,
                OkButton = CreateSketchButton("OkButton", actionRow, UiStrings.Confirm,
                    new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(UiStrings.Confirm), BodyFont),
                CancelButton = CreateSketchButton("CancelButton", actionRow, UiStrings.Cancel,
                    new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(UiStrings.Cancel), BodyFont),
            };
        }

        // ------------------------------------------------------------------
        // 船员管理 / 关卡选择（自 ManagementSceneSetup 移入运行时）
        // ------------------------------------------------------------------

        /// <summary>船员管理屏构建产物（CrewManagementController.Bind 的入参）。</summary>
        public sealed class CrewRefs
        {
            public TextMeshProUGUI SummaryText;
            public TextMeshProUGUI StatusText;
            public Transform CrewListContainer;
            public Button LevelSelectButton;
            public Button SaveButton;
            public Button BackButton;
        }

        /// <summary>建船员管理屏（移植自 ManagementSceneSetup.BuildCrewManagementScene）。</summary>
        public static CrewRefs BuildCrewManagement(Transform canvas)
        {
            TMP_FontAsset titleFont = TitleFont;
            TMP_FontAsset bodyFont = BodyFont;
            TMP_FontAsset secondaryFont = SecondaryFont;

            CreateBackdrop(canvas);

            // 文字层级：标题 Title / 概况 Body / 状态 Hint（层级靠颜色，不靠字号）。
            TextMeshProUGUI title = RuntimeUiBuilder.CreateText("Title", canvas, UiStrings.CrewTitle,
                UiSkin.Font.Title, TextAlignmentOptions.Center, PixelSkin.Theme.Text, titleFont);
            RuntimeUiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(300f, 15f),
                new Vector2(0f, -44f));

            TextMeshProUGUI summary = RuntimeUiBuilder.CreateText("SummaryText", canvas, string.Empty,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(summary.rectTransform, TopCenterAnchor, new Vector2(567f, 15f),
                new Vector2(0f, -104f));

            // 名册容器：带标题窗体（166 = 6 行×16 + 行缝 5×2 + view 内缩 14 + 窗体边 46；
            // 宽 334 取偶——中心锚 + 奇数宽会落半格相位）。
            RectTransform list = CreateTitledListPanel("CrewList", canvas,
                CenterAnchor, new Vector2(0f, 8f), new Vector2(334f, 166f),
                UiStrings.CrewRosterTitle, titleFont);

            TextMeshProUGUI status = RuntimeUiBuilder.CreateText("StatusText", canvas, string.Empty,
                UiSkin.Font.Hint, TextAlignmentOptions.Center, PixelSkin.Theme.TabNormalText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(567f, 12f),
                new Vector2(0f, 152f));

            // 屏内按钮：选关 / 保存 / 返回 = 一行居中（缝 = theme 按钮左右切片相加 8）。
            Button[] crewButtons = CreateCenteredButtonRow(canvas, "BottomActions", 72f, bodyFont,
                (UiStrings.CrewLevelSelect, ButtonSize(UiStrings.CrewLevelSelect)),
                (UiStrings.CrewSave, ButtonSize(UiStrings.CrewSave)),
                (UiStrings.BackToMainMenu, ButtonSize(UiStrings.BackToMainMenu)));

            return new CrewRefs
            {
                SummaryText = summary,
                StatusText = status,
                CrewListContainer = AddListContent(list),
                LevelSelectButton = crewButtons[0],
                SaveButton = crewButtons[1],
                BackButton = crewButtons[2],
            };
        }

        /// <summary>关卡选择屏构建产物（LevelSelectController.Bind 的入参）。</summary>
        public sealed class LevelRefs
        {
            public TextMeshProUGUI HeaderText;
            public TextMeshProUGUI ChapterNameText;
            public TextMeshProUGUI StatusText;
            public Transform ChapterContainer;
            public Transform LevelListContainer;
            public Button CrewButton;
            public Button BackButton;
            public GameObject SettlementModal;
            public TextMeshProUGUI SettlementTitle;
            public TextMeshProUGUI SettlementLevelText;
            public TextMeshProUGUI SettlementScoreText;
            public TextMeshProUGUI SettlementStarsText;
            public TextMeshProUGUI SettlementUnlockText;
            public TextMeshProUGUI SettlementFirstClearText;
            public TextMeshProUGUI SettlementStarRuleText;
            public Image[] SettlementStars;
            public Button SettlementReplayButton;
            public Button SettlementBackButton;
        }

        /// <summary>建关卡选择屏（移植自 ManagementSceneSetup.BuildLevelSelectScene）。</summary>
        public static LevelRefs BuildLevelSelect(Transform canvas)
        {
            TMP_FontAsset titleFont = TitleFont;
            TMP_FontAsset bodyFont = BodyFont;
            TMP_FontAsset secondaryFont = SecondaryFont;

            CreateBackdrop(canvas);

            TextMeshProUGUI title = RuntimeUiBuilder.CreateText("Title", canvas, UiStrings.LevelTitle,
                UiSkin.Font.Title, TextAlignmentOptions.Center, PixelSkin.Theme.Text, titleFont);
            RuntimeUiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(300f, 15f),
                new Vector2(0f, -44f));

            TextMeshProUGUI header = RuntimeUiBuilder.CreateText("HeaderText", canvas, string.Empty,
                UiSkin.Font.Hud, TextAlignmentOptions.Center, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(header.rectTransform, TopCenterAnchor, new Vector2(567f, 15f),
                new Vector2(0f, -104f));

            TextMeshProUGUI chapterName = RuntimeUiBuilder.CreateText("ChapterNameText", canvas,
                string.Empty, UiSkin.Font.Section, TextAlignmentOptions.Center,
                PixelSkin.Theme.Text, secondaryFont);
            RuntimeUiBuilder.SetAnchored(chapterName.rectTransform, TopCenterAnchor, new Vector2(400f, 15f),
                new Vector2(0f, -160f));

            // 章节页签行（容器保留契约；页签按钮已退役，当前只承载占位）。
            RectTransform chapters = RuntimeUiBuilder.CreateRect("ChapterContainer", canvas);
            RuntimeUiBuilder.SetAnchored(chapters, TopCenterAnchor, new Vector2(173f, 15f), new Vector2(0f, -72f));

            // 海图列表容器：带标题窗体「关卡列表」（240 = 10 行×16 + 行缝 9×2 + view 内缩 14
            // + 窗体边 46 + 余 2；中心 y=-35 与提示行互不叠印；宽 334 取偶）。
            RectTransform list = CreateTitledListPanel("LevelList", canvas,
                CenterAnchor, new Vector2(0f, -35f), new Vector2(334f, 240f),
                UiStrings.LevelListTitle, titleFont);

            // 出战加载说明 + 状态提示（y 让位加高后的列表窗体，三者互不叠印）。
            TextMeshProUGUI hint = RuntimeUiBuilder.CreateText("FixedArenaHint", canvas,
                UiStrings.LevelStatusFixedArena, UiSkin.Font.Hint, TextAlignmentOptions.Center,
                PixelSkin.Theme.TabNormalText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(hint.rectTransform, BottomCenterAnchor, new Vector2(567f, 12f),
                new Vector2(0f, 90f));

            TextMeshProUGUI status = RuntimeUiBuilder.CreateText("StatusText", canvas, string.Empty,
                UiSkin.Font.Hint, TextAlignmentOptions.Center, PixelSkin.Theme.TabNormalText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(567f, 12f),
                new Vector2(0f, 40f));

            Button[] levelButtons = CreateCenteredButtonRow(canvas, "BottomActions", 56f, bodyFont,
                (UiStrings.MainCrew, ButtonSize(UiStrings.MainCrew)),
                (UiStrings.Back, ButtonSize(UiStrings.Back)));

            SettlementRefs settlement = BuildSettlementModal(canvas, titleFont, bodyFont, secondaryFont);

            return new LevelRefs
            {
                HeaderText = header,
                ChapterNameText = chapterName,
                StatusText = status,
                ChapterContainer = chapters,
                LevelListContainer = AddListContent(list),
                CrewButton = levelButtons[0],
                BackButton = levelButtons[1],
                SettlementModal = settlement.Root,
                SettlementTitle = settlement.Title,
                SettlementLevelText = settlement.LevelText,
                SettlementScoreText = settlement.ScoreText,
                SettlementStarsText = settlement.StarsText,
                SettlementUnlockText = settlement.UnlockText,
                SettlementFirstClearText = settlement.FirstClearText,
                SettlementStarRuleText = settlement.StarRuleText,
                SettlementStars = settlement.Stars,
                SettlementReplayButton = settlement.ReplayButton,
                SettlementBackButton = settlement.BackButton,
            };
        }

        /// <summary>结算弹窗节点集合。</summary>
        sealed class SettlementRefs
        {
            public GameObject Root;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI LevelText;
            public TextMeshProUGUI ScoreText;
            public TextMeshProUGUI StarsText;
            public TextMeshProUGUI UnlockText;
            public TextMeshProUGUI FirstClearText;
            public TextMeshProUGUI StarRuleText;
            public Image[] Stars;
            public Button ReplayButton;
            public Button BackButton;
        }

        /// <summary>结算模态弹窗（theme window_without_title 直切件 "menu" 底；736 口径高）。</summary>
        static SettlementRefs BuildSettlementModal(Transform canvas, TMP_FontAsset titleFont,
            TMP_FontAsset bodyFont, TMP_FontAsset secondaryFont)
        {
            var refs = new SettlementRefs();

            RectTransform root = RuntimeUiBuilder.CreateRect("SettlementModal", canvas);
            RuntimeUiBuilder.Stretch(root);
            refs.Root = root.gameObject;

            CreateDimOverlay(root);

            RectTransform card = CreateStickPanel("SettlementCard", root,
                CenterAnchor, CenterAnchor, Vector2.zero, new Vector2(300f, 245f));

            TextMeshProUGUI title = RuntimeUiBuilder.CreateText("Title", card, string.Empty,
                UiSkin.Font.Display, TextAlignmentOptions.Center, PixelSkin.Theme.Text, titleFont);
            RuntimeUiBuilder.SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(800f, 44f),
                new Vector2(0f, -40f));
            refs.Title = title;

            // 星级图标（3 枚，点亮 = theme selected 金；运行时由 LevelSelectController.ApplyStars 重写）。
            var stars = new Image[StarRules.MaxStars];
            for (int i = 0; i < stars.Length; i++)
            {
                RectTransform icon = RuntimeUiBuilder.CreateRect("Star" + i, card);
                RuntimeUiBuilder.SetAnchored(icon, new Vector2(0.5f, 1f), new Vector2(72f, 72f),
                    new Vector2((i - (stars.Length - 1) * 0.5f) * 84f, -136f));

                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = RuntimeUiBuilder.StarIcon;
                image.raycastTarget = false;
                image.color = PixelSkin.Theme.Selected;
                stars[i] = image;
            }
            refs.Stars = stars;

            TextMeshProUGUI starsText = RuntimeUiBuilder.CreateText("StarsText", card, string.Empty,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(starsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(320f, 44f),
                new Vector2(0f, -220f));
            refs.StarsText = starsText;

            SketchSeparator.Create(card, "Divider", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -276f), new Vector2(759f, 3f));

            refs.LevelText = BuildSettlementRow(card, "LevelRow", -312f, bodyFont);
            refs.ScoreText = BuildSettlementRow(card, "ScoreRow", -368f, bodyFont);
            refs.UnlockText = BuildSettlementRow(card, "UnlockRow", -480f, bodyFont);
            refs.FirstClearText = BuildSettlementRow(card, "FirstClearRow", -536f, bodyFont);

            TextMeshProUGUI rule = RuntimeUiBuilder.CreateText("StarRuleText", card,
                UiStrings.SettlementStarRuleHint, UiSkin.Font.Hint, TextAlignmentOptions.Center,
                PixelSkin.Theme.StatusText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(840f, 36f),
                new Vector2(0f, -596f));
            refs.StarRuleText = rule;

            Button[] settlementButtons = CreateCenteredButtonRow(card, "SettlementActions", 52f, bodyFont,
                (UiStrings.LevelReplay, ButtonSize(UiStrings.LevelReplay)),
                (UiStrings.SettlementBackToSelect, ButtonSize(UiStrings.SettlementBackToSelect)));
            refs.ReplayButton = settlementButtons[0];
            refs.BackButton = settlementButtons[1];

            root.gameObject.SetActive(false);
            return refs;
        }

        /// <summary>结算弹窗里的「左标签 + 右值」行。</summary>
        static TextMeshProUGUI BuildSettlementRow(Transform card, string name, float y, TMP_FontAsset bodyFont)
        {
            TextMeshProUGUI text = RuntimeUiBuilder.CreateText(name, card, string.Empty, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 44f),
                new Vector2(0f, y));
            return text;
        }

        // ------------------------------------------------------------------
        // 管理侧共用装配件（自 ManagementSceneSetup 移入运行时）
        // ------------------------------------------------------------------

        /// <summary>建像素面板底（theme window_without_title 直切件 "menu"：无标题面板，
        /// 内容沿 AseLayout.PopupBorder=3 内缩）。</summary>
        static RectTransform CreateStickPanel(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            UiKit.EnsurePanel(rect, PixelTone.Frame);
            return rect;
        }

        /// <summary>建带标题带的列表窗体（theme window 直切件 + EnsureTitleLabel 唯一入口）。</summary>
        static RectTransform CreateTitledListPanel(string name, Transform parent, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, string title, TMP_FontAsset titleFont)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = CenterAnchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            UiKit.EnsureWindow(rect, PixelTone.Frame, title, titleFont, UiSkin.Font.Body,
                helpButton: false, closeButton: false);
            return rect;
        }

        /// <summary>给列表窗体加内层滚动内容区（theme view 复刻：sunken 凹槽皮 + view border
        /// padding + 行缝 1 设计格）。返回控制器应接线的容器。</summary>
        static RectTransform AddListContent(RectTransform listPanel)
        {
            RectTransform content = RuntimeUiBuilder.CreateRect("ListContent", listPanel);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            // 窗体内容区：theme window_with_title border=6；顶 = 实际内容顶（标题字大时 > 17）。
            content.offsetMin = new Vector2(
                AseLayout.Px(AseLayout.WindowBorder), AseLayout.Px(AseLayout.WindowBorder));
            content.offsetMax = new Vector2(
                -AseLayout.Px(AseLayout.WindowBorder),
                -AseLayout.Px(UiKit.WindowContentTopOf(listPanel)));

            var view = content.gameObject.AddComponent<Image>();
            view.sprite = PixelSkin.Sunken(false);
            view.type = Image.Type.Sliced;
            view.color = Color.white;   // 像素件禁止乘色
            view.raycastTarget = false;

            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.childAlignment = TextAnchor.UpperCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.spacing = AseLayout.Px(1);
            group.padding = new RectOffset(
                (int)AseLayout.Px(AseLayout.ViewBorder), (int)AseLayout.Px(AseLayout.ViewBorder),
                (int)AseLayout.Px(AseLayout.ViewBorderTop), (int)AseLayout.Px(AseLayout.ViewBorder));
            return content;
        }

        /// <summary>底部按钮行间隙：theme button 左右切片各 4 设计格相加 = 8。</summary>
        const float ButtonRowGap = 8f;

        /// <summary>建一行居中排布的底部按钮（行宽 = 各钮宽之和 + 缝，锚 BottomCenter）。
        /// 【必须 controlWidths: false】按钮无内层布局组，让 HStack 接管宽会把九宫格按钮挤成窄条
        /// （实拍：只见字不见盒）——各钮宽由 ButtonSize 造出，组只负责按缝排布 + 整行居中。</summary>
        static Button[] CreateCenteredButtonRow(Transform parent, string name, float bottomOffset,
            TMP_FontAsset font, params (string label, Vector2 size)[] buttons)
        {
            float total = ButtonRowGap * (buttons.Length - 1);
            foreach ((string _, Vector2 size) in buttons)
                total += size.x;

            RectTransform row = RuntimeUiBuilder.CreateRect(name, parent);
            RuntimeUiBuilder.SetAnchored(row, BottomCenterAnchor, new Vector2(total, UiSkin.Px.Button),
                new Vector2(0f, bottomOffset));
            UiLayout.HStack(row, (int)ButtonRowGap, default(UiPadding), controlWidths: false,
                alignment: TextAnchor.MiddleCenter);

            var result = new Button[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
                result[i] = CreateSketchButton(name + i, row, buttons[i].label,
                    CenterAnchor, Vector2.zero, buttons[i].size, font, UiSkin.Font.Body);
            return result;
        }
    }
}
