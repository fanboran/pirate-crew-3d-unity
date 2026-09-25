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
        /// 调用方传什么字体都会被按字号纠偏）。
        /// 【像素对齐】与 <see cref="UiKit.CreateText"/> 同口径：字体图集钉 Point +
        /// 挂 <see cref="PixelSnapText"/> 顶点取整（本方法此前漏挂，属半格糊字缺口）。</summary>
        public static TextMeshProUGUI CreateTextExact(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false)
        {
            RectTransform rect = CreateRect(name, parent);
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
        /// TMP 归一化宽 <c>TmpOutlineWidth</c>=0.2（本地常量；按钮描边已随 kind 退役，仅标题用），与 ControlsSampleBuilder
        /// 样张标题同口径）。材质落成 <c>Assets/Art/Materials/UI/TmpTitleOutlineInk.mat</c> 持久资产
        /// （场景重开不丢描边；与 M3 链路仍在用的 TmpTitleOutline.mat 分开，互不污染）。
        /// </summary>
            /// <summary>TMP 归一化描边宽（归一化量纲；位图字禁伪粗，标题专用）。</summary>
        const float TmpOutlineWidth = 0.2f;

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
                    material.SetFloat("_OutlineWidth", TmpOutlineWidth);
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
            // 【尺寸偶数纪律】卡 426（原 427）+ 中心锚：奇数宽居中会让左右缘落 x.5 画布格
            // （半格相位，与卡内偶数宽件错开半像素）；内层行的宽同样取偶（384/394），
            // 这样「卡缘 → 行板缘 → 行内字段」整条链都落在整数画布格上。
            SketchPanel card = SketchPanel.Create(root.transform, "SettingsCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(426f, 322f), SketchPanel.Tone.Dark, titled: true);   // theme window 直切件（标题带随切片落位）；Create 内 Apply 前就位，编辑器即时预览不露 Plate 皮
            RectTransform panel = (RectTransform)card.transform;

            // 标题走唯一入口（带内左上、边距 5、灰字 #c0c0c0、字号 12 正文档、顶点像素对齐）
            // ——与主菜单窗体/模态标题同源，不再本处手摆。
            UiKit.EnsureTitleLabel(panel, UiStrings.SettingsTitle, hand);

            // 右上窗控钮：theme window_button（9×11 件 + window_close_icon），
            // 与模态（UiKit.CreateModal → EnsureWindow）同形——参考库里带标题窗的标题带右端
            // 一定有 ?/× 两枚（New Sprite 对话框实拍），我们此前设置卡是"空着一条灰带"。
            // 行为与「返回」同：onClick 由控制器接 CloseSettings。
            Button settingsCloseButton = UiKit.CreateWindowButton(panel, "SettingsCloseButton",
                PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Close),
                AseLayout.Px(AseLayout.CloseButtonMarginRight));

            // 【已删：标题带下的自造蚀刻线】theme 的窗体（window_with_title = window_face 底
            // + window 边框件）没有"标题带下再加一条线"这一层——带底分隔已经烘在 window 件的
            // 第 14/21 行里。全区也只有设置卡挂过这条线，自家主菜单窗体/模态都没有 → 既是
            // 库里没有的件，也破了自家一致性。如要恢复，需要创始人明确开单。

            var result = new SettingsPanelResult();
            result.Root = root.gameObject;   // 【必赋】装配侧把它写进 MainMenuController.settingsPanel；
            // 漏赋 → 场景里该引用为 null → 控制器 OpenSettings 首行 `if (settingsPanel == null) return;`
            // 直接早退：真机点「设置」**面板根本不开**（截图工具直接 SetActive 才看得见，所以
            // 历次走查都没暴露）。同轮一并修好：面板开了才会跑 RefreshSettingsControls，
            // 音量百分比与选项块当前值才会落地。
            result.CloseButton = settingsCloseButton;

            // 行容器：八行流式纵排（缝 3u）——两条蓝字分组线 + 四条音量滑条 + 两组选项块。
            // 行距/行位由布局器排，不再手算 pitch 坐标。
            // 高 215 = 2 线×13 + 6 行×28 + 7 缝×3（行高取偶、线行取奇，见两个常量处的居中相位注释）。
            // 宽 384（原 383）与卡 426 同为偶数——居中链整格对齐，见卡声明处注释。
            RectTransform rows = UiKit.CreateRect("Rows", panel);
            rows.pivot = new Vector2(0.5f, 1f);
            UiKit.SetAnchored(rows, new Vector2(0.5f, 1f), new Vector2(384f, 215f), new Vector2(0f, -40f));
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

            // 提示：角标档（像素 Tiny）+ theme status_bar_text 灰（最弱档，theme 里 status_bar_text
            // #636D79 是给状态行的），放底部通带。
            // y=54：行区底（距顶 230）与恢复默认钮（y=76）之间的空档——y=120 会插进画质行。
            TextMeshProUGUI note = CreateTextExact("SaveHint", panel, UiStrings.SettingsSaveHint,
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.Theme.StatusText, hand);
            SetAnchored(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(394f, 12f),
                new Vector2(0f, 54f));

            // 恢复默认 / 返回：手绘按钮 Dark 变体；宽 = 标签宽 + 24 艺术像素、高 24 艺术像素（令牌按钮）。
            // 同排左右对称（x=±100、y=26）：旧恢复默认 x=-300 飞出面板半宽 213、后调 y=76 又压
            // 「窗口模式」行——实拍两轮修正落位。
            result.RestoreButton = CreateSketchButton("RestoreButton", panel, UiStrings.SettingsRestore,
                new Vector2(0.5f, 0f), new Vector2(-100f, 26f), ButtonSize(UiStrings.SettingsRestore));
            result.BackButton = CreateSketchButton("SettingsBackButton", panel, UiStrings.Back,
                new Vector2(0.5f, 0f), new Vector2(100f, 26f), ButtonSize(UiStrings.Back));

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
        /// 主菜单链路专用的 theme 按钮装配出口：像素字体单档 + 字号 0 = 控件默认档
        /// （正文 12 原生档），四态皮/字色全在 <see cref="SketchButton"/> 内。
        /// 返回类型是 <see cref="Button"/> 子类，控制器 [SerializeField] Button 字段直赋兼容。
        /// 【kind 参数已退役】theme 无彩面按钮，全按钮同一张 theme button 灰面皮。
        /// </summary>
        static Button CreateSketchButton(string name, Transform parent, string label, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size)
        {
            return SketchButton.Create(parent, name, anchor, new Vector2(0.5f, 0.5f),
                anchoredPosition, size, BodyFont, label, 0f);
        }

        /// <summary>设置行高（六行布局：四条滑条 + 两组选项块；行位由行容器 VBox 排）。
        /// **必须偶数**：行内滑条与单选图标都是「行内垂直居中」的子件，行高奇数时居中偏移
        /// (H−h)/2 落 .5 → 子件整体压半画布格（实拍：手柄灰边、图标与文字对不上）。
        /// 28 = theme slider 件高 16 + 上下各 6 格呼吸。</summary>
        const float SettingsRowHeight = 28f;

        /// <summary>分组标签行高（theme horizontal_separator）。**必须奇数**：行内分隔线盒子
        /// 高 5（theme 件 separator_horz 共 5 行，点线在盒内第 2 行），居中偏移 (H−5)/2
        /// 要与 5 同奇偶才取整 → 13。</summary>
        const float SettingsGroupRowHeight = 13f;

        /// <summary>滑条件高 = theme slider_empty 件高（w1..w3 = 5/6/5，h1..h3 = 5/5/6 → 16×16）。
        /// 低于件高就是九宫格压缩（旧实现 11 高把 16 高的槽竖压 → 槽内色带糊、件底边错位）。</summary>
        const float SliderHeight = 16f;

        /// <summary>滑条槽宽（偶数纪律见 BuildVolumeRow；双色裁剪层与标签盒同源取此值）。</summary>
        const float TrackWidth = 186f;

        /// <summary>建一行「字段名 + 音量滑条」（滑条实时驱动，落盘由控制器统一做）。
        /// 【件来源】theme <c>&lt;style id="slider"&gt;</c> 只声明两个 part：<c>slider_empty</c>（槽）
        /// 与 <c>slider_full</c>（充满段）——**没有拇指件**。旧实现取 <c>mini_slider_thumb</c>（时间轴
        /// mini_slider 族的 5×4）当手柄，是跨族自造：5×4 放进 10×11 的盒里被拉成非整倍 → 灰边，
        /// 且盒顶压到槽顶、盒底穿出槽底（实拍）。本版回到库里那两件，把整条槽作为拖拽区。
        /// UGUI 的 <see cref="Slider"/> 支持 <c>handleRect == null</c>：拖动/点击落点按自身 rect 算。</summary>
        static Slider BuildVolumeRow(Transform rows, int index, string field, TMP_FontAsset hand)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            // 字段名：像素皮字色按所落 tone 取可读档（行底 = SketchPanel Light → 暖白片 → 墨字）。
            TextMeshProUGUI label = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 15f),
                new Vector2(8f, 0f));

            // 槽：theme slider_empty 九宫格（16×16 件，宽向拉中段）。宽 186 取偶 → 填充边界
            // 在偶数分档（0/25/50/75/100%）正好落整格，不出现半像素接缝。
            RectTransform sliderRect = CreateRect("Slider", row);
            SetAnchored(sliderRect, new Vector2(1f, 0.5f), new Vector2(TrackWidth, SliderHeight), new Vector2(-8f, 0f));
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
            // theme slider 无任何悬停态（<style id="slider"> 只声明常态与 focus 两档）；
            // UGUI 默认 ColorTint 会在悬停/拖拽时把 0.96/0.78 的灰乘上 slider_empty 直切件，
            // 破坏调色板——像素件的状态反馈只许换贴图，无态可换就关掉。
            slider.transition = Selectable.Transition.None;

            // 充满段：theme 里 slider_full 与 slider_empty 是同一 16×16 盒的两态（焦点态另两张），
            // 直接铺满槽盒（无内缩——theme slider 件本身已含内框），锚点由 Slider 驱动。
            RectTransform fill = CreateRect("Fill", sliderRect);
            Stretch(fill);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = PixelSkin.SliderFull(false);   // theme slider_full（内芯 #41444A，比空槽暗=充满）
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 1f;
            fillImage.color = Color.white;                          // 像素件禁止乘色
            fillImage.raycastTarget = false;
            slider.fillRect = fill;

            // 无 handleRect：theme slider 没有拇指件，拖拽落点由 Slider 自身 rect 推算。

            // 数值文本【双色口径，paintSlider 源码实锄 skin_theme.cpp:1756-1790】：同一句居中文案
            // 画两遍——充满段（暗面）上 slider_full_text #C0C0C0、空槽段（亮面）上
            // slider_empty_text #202125，分界线穿字形中间逐像素换色。单色版在高音量时是
            // 1.6:1 的暗上暗（实测该屏文字像素九成背景 = 充满段 #41444A）。
            // UGUI 复刻：两枚同文案标签各挂一块 RectMask2D 裁剪框；标签盒按**整条槽**取位
            // （paintSlider 的 calcTextInfo 用全槽 bounds 居中，裁剪只发生在绘制层）——
            // 锚裁剪框左/右缘 + 槽宽恒定盒，不随裁剪框伸缩；框宽由 SliderValueLabel 逐帧驱动。
            RectTransform clipFull = CreateRect("ClipFull", sliderRect);
            clipFull.anchorMin = new Vector2(0f, 0f);
            clipFull.anchorMax = new Vector2(0f, 1f);
            clipFull.pivot = new Vector2(0f, 0.5f);
            clipFull.anchoredPosition = Vector2.zero;
            clipFull.sizeDelta = Vector2.zero;
            clipFull.gameObject.AddComponent<RectMask2D>();

            RectTransform clipRest = CreateRect("ClipRest", sliderRect);
            clipRest.anchorMin = new Vector2(0f, 0f);
            clipRest.anchorMax = new Vector2(1f, 1f);
            clipRest.offsetMin = Vector2.zero;
            clipRest.offsetMax = Vector2.zero;
            clipRest.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI valueLight = CreateTextExact("ValueLight", clipFull, Percent(slider.value),
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.Theme.Text, hand);
            valueLight.enableWordWrapping = false;
            RectTransform lightRect = valueLight.rectTransform;
            lightRect.anchorMin = new Vector2(0f, 0f);
            lightRect.anchorMax = new Vector2(0f, 1f);
            lightRect.pivot = new Vector2(0f, 0.5f);
            lightRect.anchoredPosition = Vector2.zero;
            lightRect.sizeDelta = new Vector2(TrackWidth, 0f);

            TextMeshProUGUI valueDark = CreateTextExact("ValueDark", clipRest, Percent(slider.value),
                UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.Theme.Disabled, hand);
            valueDark.enableWordWrapping = false;
            RectTransform darkRect = valueDark.rectTransform;
            darkRect.anchorMin = new Vector2(1f, 0f);
            darkRect.anchorMax = new Vector2(1f, 1f);
            darkRect.pivot = new Vector2(1f, 0.5f);
            darkRect.anchoredPosition = Vector2.zero;
            darkRect.sizeDelta = new Vector2(TrackWidth, 0f);

            // 数值刷新走件（帧对齐），**不挂 onValueChanged**——控制器刷新走
            // SetValueWithoutNotify，不触发事件，只挂事件会停在初值（见 SliderValueLabel 注释）。
            var valueLabel = sliderRect.gameObject.AddComponent<SliderValueLabel>();
            valueLabel.LabelLight = valueLight;
            valueLabel.LabelDark = valueDark;
            return slider;
        }

        /// <summary>滑条百分比文案（0..1 → "0%".."100%"；装配时给初值，运行期由
        /// <see cref="SliderValueLabel"/> 按帧对齐）。</summary>
        static string Percent(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        /// <summary>建一行「字段名 + 二选一选项块」，返回两个选项（SketchButtonSet = theme
        /// buttonset_item；控制器 <c>SetChipSelected</c> 对它写 <c>Active</c> 换当前值皮——
        /// 件语义见控件类注释）。</summary>
        static void BuildSettingsRow(Transform rows, int index, string field, TMP_FontAsset hand,
            out Button primaryOption, out Button secondaryOption, string primaryLabel, string secondaryLabel)
        {
            RectTransform row = CreateSettingsRowBackground(rows, index);

            TextMeshProUGUI label = CreateTextExact("Field", row, field, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 15f),
                new Vector2(8f, 0f));

            // 选项：theme **buttonset_item**（16×16 九宫格，等宽一对、右对齐；当前值换 active 件）。
            // 【为什么不是 radio】参考库对话框内的二选一走 buttonset（New Sprite 的
            // RGB/Grayscale/Indexed 就是）；radio/check 是"列表里的多选项"语法。
            // 等宽 = 两块同宽（按较长标签定宽），右对齐成一对——对齐问题随之消失。
            const float ItemHeight = ButtonSetHeight;                 // 件原生高 16（h 3+8+5）
            float itemWidth = Mathf.Max(primaryLabel.Length, secondaryLabel.Length)
                * ButtonSetFontSize + 2f * (AseLayout.Px(AseLayout.CheckBorder) + 3f);
            secondaryOption = SketchButtonSet.Create(row, "Option1", secondaryLabel, hand,
                ButtonSetFontSize, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-6f, 0f), new Vector2(itemWidth, ItemHeight));
            primaryOption = SketchButtonSet.Create(row, "Option0", primaryLabel, hand,
                ButtonSetFontSize, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-6f - itemWidth - ButtonSetGap, 0f), new Vector2(itemWidth, ItemHeight));
        }

        /// <summary>选项块件高 = theme <c>buttonset_item</c> 原生高（切片 h1 3 + h2 8 + h3 5 = 16）。</summary>
        const float ButtonSetHeight = 16f;

        /// <summary>选项块字号：theme <c>&lt;style id="buttonset_item" font="mini"&gt;</c>——mini 是
        /// Aseprite 最小字档；本工程最小原生档 = 8（<see cref="UiSkin.Font.Tiny"/>），映射过来。
        /// 不放大：8px 位图字在 8 画布格里 1:1，笔画仍是整格。</summary>
        const int ButtonSetFontSize = UiSkin.Font.Tiny;

        /// <summary>一对选项块之间的缝（theme 未声明，取 4 格：两块同宽盒贴成一组，不粘连）。</summary>
        const float ButtonSetGap = 4f;

        /// <summary>设置行的底板（滑条行与选项行共用）：**theme list_item 纯色面**（#41444A），
        /// 与船员/关卡列表行同色同形态——Aseprite 的列表行就是一块纯色（无九宫格、无斜面）。
        /// 旧版用自造 tone 族 Light Plate（带斜面、灰阶还是另一档 #2C2C30），本波按库换回纯色。
        /// 位置交给行容器 VBox 排（index 只用作命名），不再手算 pitch 坐标。
        /// 宽 384 = 行容器宽（偶数纪律，见卡声明处）。</summary>
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

        /// <summary>建**蓝字分组线**行（theme horizontal_separator 复刻）：左侧蓝字
        /// （#6e9adb = separator_label，缩进 x=4 设计格）+ 右侧蚀刻点线，线**从标签右缘之后起铺**。
        ///
        /// 【为什么不是整宽压线】theme 的 horizontal_separator 是「window_face 底色 + 点线 +
        /// x=4 蓝字」三层，按库的层级字会压在线上（实拍：线从「音频」「视频」字身穿过 = 穿模）。
        /// 库里那张 window_face(#2c2c30) 与我们窗体面（window 直切件中段 #2f3136）不同色，
        /// 用底色底衬会在卡面上留一块偏色补丁，所以取"线让开字"的画法：起点 = 字宽 + 2 格缝
        /// （theme horizontal_separator border=2）。整行参与 Rows 的 VBox 流式。</summary>
        static void BuildSettingsGroupLabel(Transform rows, string name, string label, TMP_FontAsset hand)
        {
            RectTransform row = UiKit.CreateRect(name, rows);
            row.sizeDelta = new Vector2(384f, SettingsGroupRowHeight);   // 同行容器宽（见常量处奇偶注释）

            TextMeshProUGUI text = CreateTextExact("Label", row, label, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.SeparatorLabel, hand);
            SetAnchored(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, SettingsGroupRowHeight),
                new Vector2(AseLayout.Px(AseLayout.SeparatorTextX), 0f));

            float lineX = AseLayout.Px(AseLayout.SeparatorTextX)
                + Mathf.Ceil(text.preferredWidth) + AseLayout.Px(AseLayout.SeparatorBorder);
            SketchSeparator.Create(row, "Line", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(lineX, 0f), new Vector2(384f - lineX, 1f),
                SketchSeparator.Direction.Horizontal);
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
        /// 搭通用确认弹窗（默认隐藏）：**走 <see cref="UiKit.CreateModal"/> 标准模态路径**——
        /// Dim 遮罩 + theme window 直切窗体皮 + 标题带 + 右上 × 关闭钮 + 卡片高随内容
        /// （ContentSizeFitter 纵向贴合）。与战斗侧返回确认（<c>BattleHudBuilder.BuildBackConfirm</c>）
        /// 同形同数（卡宽 160 / 正文 134×20 / 缝 3u——宽取偶：内容区 148 居中 (148−134)/2 = 7 整格，
        /// 133 会落 x.5 半格）。
        /// 语义由调用方定义（退出游戏 / 放弃本局返回主菜单），本工厂不绑任何行为。
        ///
        /// 【旧装配已退役】SketchPanel Dark 手摆 240×132 + 无窗体皮 + 正文取
        /// <c>LightOf(Frame)</c> 暗字——实拍「无窗体皮 / 无标题带 / 正文字色暗」的根源。
        /// </summary>
        public static ConfirmDialogResult BuildConfirmDialog(Transform canvas, string defaultMessage)
        {
            UiKit.ModalView modal = UiKit.CreateModal("ConfirmDialog", canvas, new Vector2(160f, 68f),
                title: UiStrings.ConfirmTitle, titleFont: BodyFont, titleFontSize: UiSkin.Font.Body);

            // 流式内容：正文 + 按钮行（VBox 居中块，缝 3u）。
            RectTransform flow = CreateRect("Flow", modal.Card);
            UiLayout.Flexible(flow.gameObject);
            UiLayout.VBox(flow, 3, UiPadding.Uniform(2), alignment: TextAnchor.MiddleCenter, controlHeights: true);

            // 正文：窗体面（Frame tone）上的正文档字——取 tone 可读档暖白，不再是暗字。
            // 【宽度纪律】UiLayout.VBox 只接管高度（childControlWidth 恒 false），
            // Element 声明的首选宽不参与排版——rect 宽必须自己给，否则回落默认 100 宽，
            // 十个字的短文案被挤成两行（实拍）。短文案禁换行，居中由 VBox 承担。
            TextMeshProUGUI message = UiKit.CreateText("Message", flow, defaultMessage,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Frame), BodyFont);
            message.enableWordWrapping = false;
            message.rectTransform.sizeDelta = new Vector2(134f, 20f);
            UiLayout.Element(message.gameObject, 134f, 20f);

            RectTransform actionRow = CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);

            return new ConfirmDialogResult
            {
                Root = modal.Root,
                Message = message,
                // 确定 / 取消同为 theme 灰面皮（theme 无彩面按钮），语义由焦点蓝描边与文字表达。
                OkButton = CreateSketchButton("OkButton", actionRow, UiStrings.Confirm,
                    new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(UiStrings.Confirm)),
                CancelButton = CreateSketchButton("CancelButton", actionRow, UiStrings.Cancel,
                    new Vector2(0.5f, 0.5f), Vector2.zero, ButtonSize(UiStrings.Cancel)),
            };
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
