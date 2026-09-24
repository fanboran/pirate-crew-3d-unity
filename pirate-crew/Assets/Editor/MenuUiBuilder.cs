using System.Collections.Generic;
using System.IO;
using PirateCrew.UI;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
// StickTokens 令牌是 Stick 复刻层的单一真相源，using static 提到顶层免逐处限定（同 SketchButton.cs）。
using static PirateCrew.UI.Stick.StickTokens;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 菜单类界面（主菜单 / 船员管理 / 选关 / 设置 / 结算弹窗）的共享装配工具。
    ///
    /// 【职责】
    ///   1. 中文字体接入：按规范 §5.1 的三档选型加载 TMP 字体资产；
    ///      资产缺失时**先调用现有的 <see cref="FontAssetBuilder.BuildAll"/> 生成**（幂等，不改该文件），
    ///      仍缺失则回落到 ttf（Dynamic Font）并 <c>Debug.LogWarning</c>，绝不静默出方块字（§5.5）；
    ///   2. 控件装配：设置面板 / 确认弹窗（<see cref="BuildSettingsPanel"/> / <see cref="BuildConfirmDialog"/>），
    ///      件全部走像素皮（<see cref="SketchPanel"/> / <see cref="SketchButton"/> / <see cref="SketchSeparator"/>）；
    ///      字号真值 = <see cref="UiSkin.Font"/>（文本出口统一经 <see cref="UiKit.ResolvePixelFont"/> 解析字体档）。
    ///
    /// 【历史包袱已清退】旧玻璃族工厂（CreatePanel/CreateButton/CreateGlassPanel/GetSprite/
    /// ApplyGlassSkin 等 UiSprites + GlassPanelSpriteBuilder 兼容层）随 UiSprites / GlassPanelSpriteBuilder
    /// 一并退役（2026-09-24 UI 清退批次）——主菜单 / 船员管理 / 选关的底板与按钮全走
    /// <see cref="SketchPanel"/> / <see cref="SketchButton"/>。
    /// </summary>
    public static class MenuUiBuilder
    {
        // ------------------------------------------------------------------
        // 字号体系（【像素栅格并档】真值源 = UiSkin.Font，本表只是 Editor 侧别名）
        // ------------------------------------------------------------------


        // ------------------------------------------------------------------
        // 字体（【像素字体全局切换】三档统一 Fusion Pixel 12px 位图档；2026-09-23）
        // ------------------------------------------------------------------

        /// <summary>像素字体资产路径（Art 侧；原生档纪律——字号档 = 字体原生设计档，不放大）。
        /// 标题族 16 = 正格点黑16（简体全过）；正文 12 / 次级 10 = 缝合像素。</summary>
        public const string TitleFontAssetPath = "Assets/Art/Fonts/ZhengGeDianHei16.asset";

        /// <summary>正文/按钮（12 原生档，缝合像素）。</summary>
        public const string BodyFontAssetPath = "Assets/Art/Fonts/FusionPixel12.asset";

        /// <summary>次级说明（10 原生档，缝合像素）。</summary>
        public const string SecondaryFontAssetPath = "Assets/Art/Fonts/FusionPixel10.asset";

        /// <summary>缺失位图资产时的 ttf 回落路径（Unity 已导入为 Dynamic Font）。</summary>
        const string TitleFontTtfPath = "Assets/Art/Fonts/ZhengGeDianHei16.ttf";
        const string BodyFontTtfPath = "Assets/Art/Fonts/FusionPixel12-zh_hans.ttf";
        const string SecondaryFontTtfPath = "Assets/Art/Fonts/FusionPixel10-zh_hans.ttf";

        static TMP_FontAsset _title;
        static TMP_FontAsset _body;
        static TMP_FontAsset _secondary;
        static bool _fontEnsured;

        /// <summary>标题字体（StickHand 手写体）。</summary>
        public static TMP_FontAsset TitleFont => _title != null ? _title : (_title = LoadFont(TitleFontAssetPath, TitleFontTtfPath, "标题"));

        /// <summary>正文中文字体（霞鹜文楷 Medium）。</summary>
        public static TMP_FontAsset BodyFont => _body != null ? _body : (_body = LoadFont(BodyFontAssetPath, BodyFontTtfPath, "正文"));

        /// <summary>次级中文字体（霞鹜文楷 Regular）。</summary>
        public static TMP_FontAsset SecondaryFont =>
            _secondary != null ? _secondary : (_secondary = LoadFont(SecondaryFontAssetPath, SecondaryFontTtfPath, "次级"));

        /// <summary>确保字体资产存在（幂等；内部会触发一次 FontAssetBuilder）。</summary>
        public static void EnsureFonts()
        {
            if (_fontEnsured)
                return;

            _fontEnsured = true;

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontAssetPath) == null
                || AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontAssetPath) == null)
            {
                Debug.LogWarning("[MenuUiBuilder] 未找到中文 TMP 字体资产，先调用 FontAssetBuilder.BuildAll 生成。");
                FontAssetBuilder.BuildAll();
            }

            // 触发三档加载（各自缺失时告警并回落 ttf）。
            _ = TitleFont;
            _ = BodyFont;
            _ = SecondaryFont;
        }

        static TMP_FontAsset LoadFont(string assetPath, string ttfPath, string role)
        {
            TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (asset != null)
                return asset;

            Font ttf = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (ttf != null)
            {
                Debug.LogWarning("[MenuUiBuilder] 缺 TMP 字体资产 " + assetPath
                    + "（角色：" + role + "），回落 ttf：" + ttfPath
                    + "。请跑菜单 PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）后重建场景。");
                return TMP_FontAsset.CreateFontAsset(ttf);
            }

            Debug.LogWarning("[MenuUiBuilder] 字体资产与 ttf 都缺失（角色：" + role
                + "）。中文会显示为方块——请先导入 Assets/Art/Fonts/ 下的 ttf，"
                + "再执行 Window/TextMeshPro/Import TMP Essential Resources，"
                + "然后跑 PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）。");
            return null;
        }

        // ------------------------------------------------------------------
        // 程序化 Skin 落盘（九宫格 PNG）
        // ------------------------------------------------------------------

        const string SpriteFolder = "Assets/Art/Sprites/UI";


        // ------------------------------------------------------------------
        // 像素皮（Beveled Pixel）—— 旧的"亚克力玻璃" Skin 段已整体换装
        // ------------------------------------------------------------------


        // （旧 GlassColors 乘色四态表已随换装删除：像素皮禁用 Image.color 乘色——状态改由
        //   UGUI SpriteSwap 三态贴图承担，禁用态走 CanvasGroup alpha，见 SketchButton。）

        // ------------------------------------------------------------------
        // 控件工厂
        // ------------------------------------------------------------------

        /// <summary>建一个带 RectTransform 的 UI 对象。</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>按锚点 + 尺寸摆放（anchoredPosition 相对锚点）。</summary>
        public static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>铺满父容器。</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }


        /// <summary>建 TMP 文本（**新档字号入口**）：<paramref name="fontSize"/> 原样使用；
        /// 字体由 <see cref="UiKit.ResolvePixelFont"/> 按字号单点解析（满精度阶梯，
        /// 调用方传什么字体都会被按字号纠偏）。</summary>
        public static TextMeshProUGUI CreateTextExact(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            TMP_FontAsset resolved = UiKit.ResolvePixelFont(fontSize, font);
            if (resolved != null)
                text.font = resolved;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = raycast;
            // 标题加深色描边提升可读性（规范 §5.3：描边只给标题）。
            return text;
        }

        /// <summary>
        /// 给标题类文本挂「深色描边」材质（Outline Thickness 0.15、色 #2A2A2A，规范 §5.3）。
        /// 材质落成 <c>Assets/Art/Materials/UI/TmpTitleOutline.mat</c> 持久资产，避免场景重开后丢描边。
        /// </summary>
        public static void ApplyTitleOutline(TextMeshProUGUI text)
        {
            if (text == null || text.font == null)
                return;

            Material material = GetOrCreateTitleMaterial(text.font);
            if (material != null)
                text.fontSharedMaterial = material;
        }

        static Material GetOrCreateTitleMaterial(TMP_FontAsset font)
        {
            const string path = "Assets/Art/Materials/UI/TmpTitleOutline.mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            try
            {
                EnsureFolder("Assets/Art/Materials");
                EnsureFolder("Assets/Art/Materials/UI");

                material = new Material(font.material) { name = "TmpTitleOutline" };
                material.SetColor("_OutlineColor", (Color)PixelSkin.Ink);
                material.SetFloat("_OutlineWidth", 0.15f);
                material.EnableKeyword("OUTLINE_ON");
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MenuUiBuilder] 生成标题描边材质失败（" + path + "）：" + e.Message
                    + "\n  标题将不带描边，改用亮/暗底对比保证可读性。");
                return null;
            }
        }

        /// <summary>
        /// 给标题类文本挂 StickUI 口径的「墨色描边」（INK 3px 档：色 <see cref="StickTokens.INK"/>、
        /// TMP 归一化宽 <see cref="SketchButton.TmpOutlineWidth"/>=0.2，与 ControlsSampleBuilder
        /// 样张标题同口径）。材质落成 <c>Assets/Art/Materials/UI/TmpTitleOutlineInk.mat</c> 持久资产
        /// （场景重开不丢描边；与 M3 链路仍在用的 TmpTitleOutline.mat 分开，互不污染）。
        /// </summary>
        public static void ApplyStickTitleOutline(TextMeshProUGUI text)
        {
            if (text == null || text.font == null)
                return;

            const string path = "Assets/Art/Materials/UI/TmpTitleOutlineInk.mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                try
                {
                    EnsureFolder("Assets/Art/Materials");
                    EnsureFolder("Assets/Art/Materials/UI");

                    material = new Material(text.font.material) { name = "TmpTitleOutlineInk" };
                    material.SetColor("_OutlineColor", INK);
                    material.SetFloat("_OutlineWidth", SketchButton.TmpOutlineWidth);
                    material.EnableKeyword("OUTLINE_ON");
                    AssetDatabase.CreateAsset(material, path);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[MenuUiBuilder] 生成 Stick 标题描边材质失败（" + path + "）："
                        + e.Message + "\n  标题将不带描边，仅靠 TEXT 亮字压暗底保可读性。");
                }
            }

            if (material != null)
                text.fontSharedMaterial = material;
        }


        /// <summary>全屏压暗遮罩（模态层 z=40，§1.7）。</summary>
        public static RectTransform CreateDimOverlay(string name, Transform parent, float alpha = 0.6f)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, alpha);
            image.raycastTarget = true;   // 挡住底下的点击，符合模态语义。
            return rect;
        }

        // ------------------------------------------------------------------
        // 设置界面（§3.7；音量/画质/窗口模式真接线，控制器为 MainMenuController）
        // ------------------------------------------------------------------

        /// <summary>设置面板构建产物（控制器按字段名接线）。</summary>
        public sealed class SettingsPanelResult
        {
            public GameObject Root;
            public Button BackButton;
            public Button RestoreButton;
            public Slider MasterSlider;
            public Slider SfxSlider;
            public Slider MusicSlider;
            public Slider AmbientSlider;
            public Button QualityHighButton;
            public Button QualitySmoothButton;
            public Button FullscreenOnButton;
            public Button FullscreenOffButton;
        }

        /// <summary>
        /// 搭「设置」界面（默认隐藏）：四条音量滑条 + 画质档 + 窗口模式 + 恢复默认/返回。
        /// 【接线纪律】本方法只建控件，不改任何真实设置；应用与持久化都在
        /// <c>MainMenuController</c>（运行时）里做——构建器拿不到运行时服务。
        /// </summary>
        public static SettingsPanelResult BuildSettingsPanel(Transform canvas)
        {
            // 像素字体单字体纪律（UiKit.RuntimeFont 同口径）：三档统一 Fusion Pixel 位图档。
            TMP_FontAsset hand = TitleFont;

            RectTransform root = CreateRect("SettingsPanel", canvas);
            Stretch(root);

            CreateDimOverlay("DimOverlay", root);

            // 底板：SketchPanel Dark → **带标题窗体**（theme window：顶 15u 标题带）。
            SketchPanel card = SketchPanel.Create(root.transform, "SettingsCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(427f, 322f), SketchPanel.Tone.Dark);
            card.Titled = true;   // 换 Window 九宫格（标题带随切片落位）
            RectTransform panel = (RectTransform)card.transform;

            // 标题：带内左上（theme window_title_label：margin 5/5 设计格，灰字 #c0c0c0）。
            TextMeshProUGUI titleText = CreateTextExact("Title", panel, UiStrings.SettingsTitle,
                UiSkin.Font.Title, TextAlignmentOptions.MidlineLeft, PixelSkin.Theme.Text, hand);
            SetAnchored(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(383f, 30f),
                new Vector2(AseLayout.Px(AseLayout.TitleMarginLeft), -AseLayout.Px(AseLayout.TitleMarginTop)));

            // 标题带下蚀刻分隔线（像素皮 Separator 贴图，方向由 Dir 决定）。
            SketchSeparator.Create(panel, "TitleSeparator", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -35f), new Vector2(300f, 1f), SketchSeparator.Direction.Horizontal);

            var result = new SettingsPanelResult();

            // 行容器：八行流式纵排（缝 3u）——两条蓝字分组线 + 四条音量滑条 + 两组选项块。
            // 行距/行位由布局器排，不再手算 pitch 坐标。高 212 = 2 线×12 + 6 行×27 + 7 缝×3 + 余 5。
            RectTransform rows = UiKit.CreateRect("Rows", panel);
            rows.pivot = new Vector2(0.5f, 1f);
            UiKit.SetAnchored(rows, new Vector2(0.5f, 1f), new Vector2(383f, 212f), new Vector2(0f, -40f));
            UiLayout.VBox(rows, 3, default(UiPadding));

            // 音频组（theme separator_label 蓝字分组线）+ 音量四行（滑条实时改 AudioService，
            // 关面板时统一落盘）。
            BuildSettingsGroupLabel(rows, "GroupAudio", UiStrings.SettingsGroupAudio, hand);
            result.MasterSlider = BuildVolumeRow(rows, 0, UiStrings.SettingsFieldVolumeMaster, hand);
            result.SfxSlider = BuildVolumeRow(rows, 1, UiStrings.SettingsFieldVolumeSfx, hand);
            result.MusicSlider = BuildVolumeRow(rows, 2, UiStrings.SettingsFieldVolumeMusic, hand);
            result.AmbientSlider = BuildVolumeRow(rows, 3, UiStrings.SettingsFieldVolumeAmbient, hand);

            // 视频组 + 画质档（二选一选项块；选中态由控制器按 VideoSettingsService 刷新）。
            BuildSettingsGroupLabel(rows, "GroupVideo", UiStrings.SettingsGroupVideo, hand);
            BuildSettingsRow(rows, 4, UiStrings.SettingsFieldQuality, hand,
                out result.QualityHighButton, out result.QualitySmoothButton,
                UiStrings.SettingsOptionQualityHigh, UiStrings.SettingsOptionQualitySmooth);
            BuildSettingsRow(rows, 5, UiStrings.SettingsFieldWindowMode, hand,
                out result.FullscreenOnButton, out result.FullscreenOffButton,
                UiStrings.SettingsOptionFullscreen, UiStrings.SettingsOptionWindowed);

            // 提示：角标档（像素 Tiny 24）+ TEXT_FAINT（stick-world 次级文字口径），放底部通带。
            // y=54：行区底（距顶 230）与恢复默认钮（y=76）之间的空档——y=120 会插进画质行。
            TextMeshProUGUI note = CreateTextExact("SaveHint", panel, UiStrings.SettingsSaveHint,
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, TEXT_FAINT, hand);
            SetAnchored(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(393f, 12f),
                new Vector2(0f, 54f));

            // 恢复默认 / 返回：手绘按钮 Dark 变体；宽 = 标签宽 + 24 艺术像素、高 24 艺术像素（令牌按钮）。
            // 同排左右对称（x=±100、y=26）：旧恢复默认 x=-300 飞出面板半宽 213、后调 y=76 又压
            // 「窗口模式」行——实拍两轮修正落位。
            result.RestoreButton = CreateSketchButton("RestoreButton", panel, UiStrings.SettingsRestore,
                new Vector2(0.5f, 0f), new Vector2(-100f, 26f), ButtonSize(UiStrings.SettingsRestore),
                SketchButtonKind.Dark);
            result.BackButton = CreateSketchButton("SettingsBackButton", panel, UiStrings.Back,
                new Vector2(0.5f, 0f), new Vector2(100f, 26f), ButtonSize(UiStrings.Back),
                SketchButtonKind.Dark);

            root.gameObject.SetActive(false);

            return result;
        }

        /// <summary>令牌按钮尺寸（宽 = 标签宽 + 24 艺术像素，高 24 艺术像素）。
        /// 装配侧的便捷出口，与 <see cref="UiSkin.Px.ButtonWidth"/> 同式。</summary>
        public static Vector2 ButtonSize(string label)
        {
            return new Vector2(UiSkin.Px.ButtonWidth(label), UiSkin.Px.Button);
        }

        /// <summary>
        /// 主菜单链路专用的手绘按钮装配出口：像素字体单档 + 字号 0 = 控件默认档
        /// （像素皮下 = 正文 12 艺术像素，见 SketchButton.AddLabel），四态字色全在
        /// <see cref="SketchButton"/> 内。
        /// 返回类型是 <see cref="Button"/> 子类，控制器 [SerializeField] Button 字段直赋兼容。
        /// </summary>
        static Button CreateSketchButton(string name, Transform parent, string label, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, SketchButtonKind kind)
        {
            return SketchButton.Create(parent, name, anchor, new Vector2(0.5f, 0.5f),
                anchoredPosition, size, BodyFont, kind, label, 0f);
        }

        /// <summary>行高（六行布局：四条滑条 + 两组选项块；行位由行容器 VBox 排，见 BuildSettingsPanel）。</summary>
        const float SettingsRowHeight = 27f;

        /// <summary>建一行「字段名 + 音量滑条」（滑条实时驱动，落盘由控制器统一做）。
        /// 【换装最小半径】滑条三件套保持 UGUI 标准件（控制器按 <see cref="Slider"/> 契约接线），
        /// 只换行底板与字段名文字。</summary>
        static Slider BuildVolumeRow(Transform rows, int index, string field, TMP_FontAsset hand)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            // 字段名：像素皮字色按所落 tone 取可读档（行底 = SketchPanel Light → 暖白片 → 墨字）。
            TextMeshProUGUI label = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, PixelSkin.TextColorOn(PixelTone.Light), hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 15f),
                new Vector2(8f, 0f));

            // 滑条（UGUI 标准三件套：底槽 / 填充 / 手柄；皮肤用像素件 Track / Fill / Plate）。
            RectTransform sliderRect = CreateRect("Slider", row);
            SetAnchored(sliderRect, new Vector2(1f, 0.5f), new Vector2(187f, 11f), new Vector2(-8f, 0f));
            var sliderBack = sliderRect.gameObject.AddComponent<Image>();
            sliderBack.sprite = PixelSkin.SliderEmpty(false);   // theme slider_empty（凹槽语法）
            sliderBack.type = Image.Type.Sliced;
            sliderBack.color = Color.white;                         // 像素件禁止乘色
            sliderBack.raycastTarget = false;

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;

            RectTransform fillArea = CreateRect("Fill Area", sliderRect);
            fillArea.anchorMin = new Vector2(0f, 0f);
            fillArea.anchorMax = new Vector2(1f, 1f);
            fillArea.offsetMin = new Vector2(8f, 6f);
            fillArea.offsetMax = new Vector2(-8f, -6f);

            RectTransform fill = CreateRect("Fill", fillArea);
            Stretch(fill);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = PixelSkin.SliderFull(false);   // theme slider_full（金色充满段）
            fillImage.type = Image.Type.Sliced;
            fillImage.color = Color.white;                          // 像素件禁止乘色
            fillImage.raycastTarget = false;
            slider.fillRect = fill;

            RectTransform handleArea = CreateRect("Handle Slide Area", sliderRect);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            RectTransform handle = CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(10f, 0f);   // 宽 = 拇指件 5u；高铺满（preserveAspect 锁纵横）
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = PixelSkin.SliderThumb;  // theme mini_slider_thumb（金色 5×4）
            handleImage.type = Image.Type.Simple;
            handleImage.preserveAspect = true;
            handleImage.color = Color.white;                        // 像素件禁止乘色
            handleImage.raycastTarget = false;

            slider.handleRect = handle;
            slider.targetGraphic = handleImage;

            return slider;
        }

        /// <summary>建一行「字段名 + 二选一单选钮」，返回两个选项（SketchCheck = Button 子类，
        /// 控制器 <c>SetChipSelected</c> 对它走 IsOn 换图标分支——theme radio：选中 = 中心点）。</summary>
        static void BuildSettingsRow(Transform rows, int index, string field, TMP_FontAsset hand,
            out Button primaryOption, out Button secondaryOption, string primaryLabel, string secondaryLabel)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            TextMeshProUGUI label = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, PixelSkin.TextColorOn(PixelTone.Light), hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 15f),
                new Vector2(8f, 0f));

            // 选项：theme radio（8×8 图标 + 文字；悬停铺 HotFace），行内右侧对齐一对。
            float w1 = 14f * 2f + UiSkin.Font.Body * (secondaryLabel.Length + 1f);
            secondaryOption = SketchCheck.Create(row, "Option1", secondaryLabel, radio: true, hand,
                UiSkin.Font.Body, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-6f, 0f), SettingsRowHeight - 6f);
            primaryOption = SketchCheck.Create(row, "Option0", primaryLabel, radio: true, hand,
                UiSkin.Font.Body, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-6f - w1 - 8f, 0f), SettingsRowHeight - 6f);
        }

        /// <summary>设置行的底板（滑条行与选项行共用）：SketchPanel Light → Plate(Light) 片 + 底垫投影。
        /// 位置交给行容器 VBox 排（index 只用作命名），不再手算 pitch 坐标。</summary>
        static RectTransform CreateSettingsRowBackground(Transform rows, int index)
        {
            SketchPanel rowPanel = SketchPanel.Create(rows, "Row" + index,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(383f, SettingsRowHeight), SketchPanel.Tone.Light);
            return (RectTransform)rowPanel.transform;
        }

        /// <summary>建**蓝字分组线**行（theme horizontal_separator 复刻）：整宽蚀刻线垂直居中 +
        /// 左侧蓝字（#6e9adb = separator_label，缩进 x=4 设计格）直接压线——线从字隙穿过，
        /// 与 theme 渲染层级（background → 线 → 字）同构。作为 Rows 的 VBox 行参与流式，高 12。</summary>
        static void BuildSettingsGroupLabel(Transform rows, string name, string label, TMP_FontAsset hand)
        {
            RectTransform row = UiKit.CreateRect(name, rows);
            row.sizeDelta = new Vector2(383f, 12f);

            SketchSeparator.Create(row, "Line", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(383f, 1f), SketchSeparator.Direction.Horizontal);

            TextMeshProUGUI text = CreateTextExact("Label", row, label, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, PixelSkin.Theme.SeparatorLabel, hand);
            SetAnchored(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 12f),
                new Vector2(AseLayout.Px(AseLayout.SeparatorTextX), 0f));
        }

        /// <summary>确认弹窗构建产物。</summary>
        public sealed class ConfirmDialogResult
        {
            public GameObject Root;
            public TextMeshProUGUI Message;
            public Button OkButton;
            public Button CancelButton;
        }

        /// <summary>
        /// 搭通用确认弹窗（默认隐藏）：压暗遮罩 + 面板 + 正文 + 确定/取消。
        /// 语义由调用方定义（退出游戏 / 放弃本局返回主菜单），本工厂不绑任何行为。
        /// </summary>
        public static ConfirmDialogResult BuildConfirmDialog(Transform canvas, string defaultMessage)
        {
            RectTransform root = CreateRect("ConfirmDialog", canvas);
            Stretch(root);

            CreateDimOverlay("DimOverlay", root);

            // 底板：SketchPanel Dark → Plate(Frame tone) + 底垫投影；消息直接压面板，不再垫内容片。
            SketchPanel card = SketchPanel.Create(root.transform, "ConfirmCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(240f, 132f), SketchPanel.Tone.Dark);
            RectTransform panel = (RectTransform)card.transform;

            // 流式内容（UiLayout）：正文 + 按钮行——居中块，纵缝 3u，不再手摆 y 坐标。
            RectTransform flow = CreateRect("Flow", panel);
            Stretch(flow);
            UiLayout.VBox(flow, 3, UiPadding.Uniform(2), alignment: TextAnchor.MiddleCenter);

            // 正文：Frame tone 上的正文浅字（像素皮 TextColorOn 档）、正文字号。
            TextMeshProUGUI message = CreateTextExact("Message", flow, defaultMessage,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.LightOf(PixelTone.Frame), TitleFont);
            UiLayout.Element(message.gameObject, 560f, 96f);

            RectTransform actionRow = CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);

            var result = new ConfirmDialogResult
            {
                Root = root.gameObject,
                Message = message,
                // 确认 = Accent 金强调（StickKit.Confirm 默认 kind 同语义）、取消 = Dark 常规；
                // 令牌按钮（宽 = 标签宽 + 8 艺术像素、高 16u）。
                OkButton = CreateSketchButton("OkButton", actionRow, UiStrings.Confirm,
                    new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(UiStrings.Confirm),
                    SketchButtonKind.Accent),
                CancelButton = CreateSketchButton("CancelButton", actionRow, UiStrings.Cancel,
                    new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(UiStrings.Cancel),
                    SketchButtonKind.Dark),
            };

            root.gameObject.SetActive(false);
            return result;
        }

        /// <summary>确保工程内文件夹存在。</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
