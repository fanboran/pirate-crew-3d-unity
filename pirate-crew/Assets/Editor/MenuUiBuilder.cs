using System.Collections.Generic;
using System.IO;
using PirateCrew.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 菜单类界面（主菜单 / 船员管理 / 选关 / 设置 / 结算弹窗）的共享装配工具。
    ///
    /// 【职责】
    ///   1. 中文字体接入：按规范 §5.1 的三档选型加载 TMP 字体资产；
    ///      资产缺失时**先调用现有的 <see cref="FontAssetBuilder.BuildAll"/> 生成**（幂等，不改该文件），
    ///      仍缺失则回落到 ttf（Dynamic Font）并 <c>Debug.LogWarning</c>，绝不静默出方块字（§5.5）；
    ///   2. 程序化九宫格 Skin：把 <see cref="UiSprites"/> 生成的像素落成
    ///      <c>Assets/Art/Sprites/UI/*.png</c> 持久资产（编辑器装配的场景引用必须可序列化，
    ///      内存 Sprite 存不进场景），并按九宫格导入；
    ///   3. 统一控件工厂：木板 / 羊皮纸 / 黄铜 + TMP，四态配色走 <see cref="UiTheme"/> Token。
    ///
    /// 【设计语言】docs/UI-UX与中文本地化规范.md §1：木板 / 羊皮纸 / 黄铜三段材质语言，
    /// 配色取 §1.3 Token，字号取 §1.4；**不照抄隔壁 game-2 的黑玻璃极简风**。
    /// </summary>
    public static class MenuUiBuilder
    {
        // ------------------------------------------------------------------
        // 字体（规范 §5.1）
        // ------------------------------------------------------------------

        /// <summary>中文字体资产路径（由 FontAssetBuilder 生成）。</summary>
        public const string TitleFontAssetPath = "Assets/Art/Fonts/StickHand-Regular SDF.asset";

        /// <summary>正文中文字体资产路径。</summary>
        public const string BodyFontAssetPath = "Assets/Art/Fonts/LXGWWenKaiLite-Medium SDF.asset";

        /// <summary>次级中文字体资产路径。</summary>
        public const string SecondaryFontAssetPath = "Assets/Art/Fonts/LXGWWenKaiLite-Regular SDF.asset";

        /// <summary>缺失 SDF 资产时的 ttf 回落路径（Unity 已导入为 Dynamic Font）。</summary>
        const string TitleFontTtfPath = "Assets/Art/Fonts/StickHand-Regular.ttf";
        const string BodyFontTtfPath = "Assets/Art/Fonts/LXGWWenKaiLite-Medium.ttf";
        const string SecondaryFontTtfPath = "Assets/Art/Fonts/LXGWWenKaiLite-Regular.ttf";

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

        static readonly Dictionary<UiSprites.Kind, Sprite> SpriteCache = new Dictionary<UiSprites.Kind, Sprite>();

        /// <summary>
        /// 取持久化九宫格 Sprite（不存在则从 <see cref="UiSprites"/> 生成 PNG 并导入）。
        /// 编辑器装配的场景必须引用持久资产，否则重开场景后 Sprite 引用会丢失。
        /// </summary>
        public static Sprite GetSprite(UiSprites.Kind kind)
        {
            if (SpriteCache.TryGetValue(kind, out Sprite cached) && cached != null)
                return cached;

            string path = SpriteFolder + "/" + kind + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                sprite = GenerateSpriteAsset(kind, path);
            }

            SpriteCache[kind] = sprite;
            return sprite;
        }

        static Sprite GenerateSpriteAsset(UiSprites.Kind kind, string assetPath)
        {
            try
            {
                EnsureFolder("Assets/Art");
                EnsureFolder("Assets/Art/Sprites");
                EnsureFolder(SpriteFolder);

                Texture2D texture = UiSprites.CreateTexture(kind, out Vector4 border);
                byte[] png = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);

                string absolutePath = Path.Combine(Application.dataPath,
                    assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(absolutePath, png);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100f;
                    importer.spriteBorder = border;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                else
                {
                    Debug.LogWarning("[MenuUiBuilder] 无法读取 TextureImporter：" + assetPath
                        + "，九宫格边框可能未生效。");
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                    return sprite;

                Debug.LogWarning("[MenuUiBuilder] 生成 Skin 失败：" + assetPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MenuUiBuilder] 生成 Skin 资产异常（" + assetPath + "）：" + e.Message
                    + "\n  退回内存 Sprite（外观一致，但不会随场景持久化）。");
            }

            // 兜底：用内存 Sprite，至少保证本次装配有材质感（不阻断 HUD/场景重建）。
            return UiSprites.Get(kind);
        }

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

        /// <summary>建 TMP 文本。</summary>
        public static TextMeshProUGUI CreateText(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null)
                text.font = font;
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
                material.SetColor("_OutlineColor", UiTheme.Ink);
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

        /// <summary>建面板底（九宫格 Skin + 可选黄铜外描边）。</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, UiSprites.Kind skin, bool brassOutline = true)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetSprite(skin);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;

            if (brassOutline)
                AddOutline(rect.gameObject, UiTheme.Brass, 3f);

            return rect;
        }

        /// <summary>加 UGUI <see cref="Outline"/> 描边（黄铜框，规范 §1.7）。</summary>
        public static void AddOutline(GameObject target, Color color, float distance)
        {
            var outline = target.GetComponent<Outline>();
            if (outline == null)
                outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
        }

        /// <summary>建按钮（Skin 底 + 四态 + TMP 居中文本）。</summary>
        public static Button CreateButton(string name, Transform parent, string label, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font, UiSprites.Kind skin,
            Color? labelColor = null, int fontSize = UiTheme.FontBody)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetSprite(skin);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            UiSprites.Kind skinCopy = skin;
            ColorBlock colors = button.colors;
            Color baseColor = BaseColorOf(skinCopy);
            colors.normalColor = baseColor;
            colors.highlightedColor = UiTheme.Hover(baseColor);
            colors.pressedColor = UiTheme.Pressed(baseColor);
            colors.selectedColor = UiTheme.Hover(baseColor);
            colors.disabledColor = UiTheme.Disabled(baseColor);
            colors.fadeDuration = 0.09f;
            button.colors = colors;

            TextMeshProUGUI text = CreateText("Text", rect, label, fontSize, TextAlignmentOptions.Center,
                labelColor ?? LabelColorOf(skin), font);
            Stretch(text.rectTransform);

            return button;
        }

        /// <summary>Skin → 正常态底色（Button.color 的乘色）。</summary>
        public static Color BaseColorOf(UiSprites.Kind skin)
        {
            switch (skin)
            {
                case UiSprites.Kind.ButtonParchment: return UiTheme.Parchment;
                case UiSprites.Kind.ButtonBrass: return UiTheme.Brass;
                case UiSprites.Kind.ButtonDanger: return UiTheme.DangerDark;
                default: return UiTheme.WoodMid;
            }
        }

        /// <summary>Skin → 标签色（亮底用墨字，深底用亮字，保证对比 ≥ 4.5:1）。</summary>
        public static Color LabelColorOf(UiSprites.Kind skin)
        {
            switch (skin)
            {
                case UiSprites.Kind.ButtonParchment:
                case UiSprites.Kind.ButtonBrass:
                    return UiTheme.Ink;
                default:
                    return UiTheme.TextLight;
            }
        }

        /// <summary>全屏木板背景（主菜单 / 管理界面用；通栏条允许贴边，§1.7）。</summary>
        public static RectTransform CreateWoodBackdrop(string name, Transform parent, Color tint)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetSprite(UiSprites.Kind.PanelWood);
            image.type = Image.Type.Sliced;
            image.color = tint;
            image.raycastTarget = false;
            return rect;
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
        // 设置界面（§3.7；本轮为界面占位，选项未接线）
        // ------------------------------------------------------------------

        /// <summary>设置面板构建产物。</summary>
        public sealed class SettingsPanelResult
        {
            public GameObject Root;
            public Button BackButton;
        }

        /// <summary>
        /// 搭「设置」界面（默认隐藏）。按规范 §3.7 线框图：分类页签 + 键值行 + 恢复默认/返回。
        /// 【占位声明】所有选项控件**未接线**，面板底部有醒目说明（规范 §3.7「至少做出界面与持久化占位」）。
        /// </summary>
        public static SettingsPanelResult BuildSettingsPanel(Transform canvas)
        {
            TMP_FontAsset title = TitleFont;
            TMP_FontAsset body = BodyFont;
            TMP_FontAsset secondary = SecondaryFont;

            RectTransform root = CreateRect("SettingsPanel", canvas);
            Stretch(root);

            CreateDimOverlay("DimOverlay", root);
            RectTransform panel = CreatePanel("SettingsCard", root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1280f, 700f), UiSprites.Kind.PanelWood);

            TextMeshProUGUI titleText = CreateText("Title", panel, UiStrings.SettingsTitle,
                UiTheme.FontTitle, TextAlignmentOptions.Center, UiTheme.BrassLight, title);
            SetAnchored(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(600f, 48f),
                new Vector2(0f, -28f));
            ApplyTitleOutline(titleText);

            // 分类页签（画面选中=黄铜，其余=木板）。
            string[] tabs =
            {
                UiStrings.SettingsTabVideo, UiStrings.SettingsTabAudio,
                UiStrings.SettingsTabControl, UiStrings.SettingsTabLanguage,
            };
            float tabStart = -(tabs.Length - 1) * 0.5f * 176f;
            for (int i = 0; i < tabs.Length; i++)
            {
                UiSprites.Kind skin = i == 0 ? UiSprites.Kind.ButtonBrass : UiSprites.Kind.ButtonWood;
                Button tab = CreateButton("Tab" + i, panel, tabs[i], new Vector2(0.5f, 1f),
                    new Vector2(tabStart + i * 176f, -104f), new Vector2(160f, 44f), body, skin);
                TextMeshProUGUI label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                    label.color = LabelColorOf(skin);
            }

            // 键值行（羊皮纸行底 + 深色墨字 + 选项块）。
            BuildSettingsRow(panel, 0, UiStrings.SettingsFieldRenderStyle,
                new[] { UiStrings.SettingsOptionToon, UiStrings.SettingsOptionSmooth }, 0, body, secondary);
            BuildSettingsRow(panel, 1, UiStrings.SettingsFieldAimMode,
                new[] { UiStrings.SettingsOptionDrag, UiStrings.SettingsOptionKeyboard }, 0, body, secondary);
            BuildSettingsRow(panel, 2, UiStrings.SettingsFieldResolution,
                new[] { "1920×1080" }, 0, body, secondary);
            BuildSettingsRow(panel, 3, UiStrings.SettingsFieldWindowMode,
                new[] { UiStrings.SettingsOptionFullscreen, UiStrings.SettingsOptionWindowed }, 0, body, secondary);

            TextMeshProUGUI note = CreateText("PlaceholderNote", panel, UiStrings.SettingsPlaceholderNote,
                UiTheme.FontHint, TextAlignmentOptions.Center, UiTheme.BrassLight, secondary);
            SetAnchored(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(1180f, 30f),
                new Vector2(0f, 116f));

            Button restore = CreateButton("RestoreButton", panel, UiStrings.SettingsRestore,
                new Vector2(0.5f, 0f), new Vector2(-260f, 56f), new Vector2(240f, 52f), body,
                UiSprites.Kind.ButtonParchment);
            Button back = CreateButton("SettingsBackButton", panel, UiStrings.Back,
                new Vector2(0.5f, 0f), new Vector2(260f, 56f), new Vector2(240f, 52f), body,
                UiSprites.Kind.ButtonWood);

            root.gameObject.SetActive(false);

            return new SettingsPanelResult { Root = root.gameObject, BackButton = back };
        }

        static void BuildSettingsRow(Transform panel, int index, string field, string[] options,
            int selectedIndex, TMP_FontAsset body, TMP_FontAsset secondary)
        {
            float y = -160f - index * 72f;
            RectTransform row = CreateRect("Row" + index, panel);
            SetAnchored(row, new Vector2(0.5f, 1f), new Vector2(1150f, 56f), new Vector2(0f, y));

            var background = row.gameObject.AddComponent<Image>();
            background.sprite = GetSprite(UiSprites.Kind.PanelParchment);
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            TextMeshProUGUI label = CreateText("Field", row, field, UiTheme.FontBody,
                TextAlignmentOptions.MidlineLeft, UiTheme.Ink, body);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 40f),
                new Vector2(24f, 0f));

            for (int i = 0; i < options.Length; i++)
            {
                // 选项块：选中用黄铜，未选用木板；本轮不挂 Button（未接线，避免死按钮）。
                RectTransform chip = CreateRect("Option" + i, row);
                SetAnchored(chip, new Vector2(1f, 0.5f),
                    new Vector2(200f, 40f), new Vector2(-24f - (options.Length - 1 - i) * 212f, 0f));

                var chipImage = chip.gameObject.AddComponent<Image>();
                chipImage.sprite = GetSprite(i == selectedIndex
                    ? UiSprites.Kind.ButtonBrass
                    : UiSprites.Kind.ButtonWood);
                chipImage.type = Image.Type.Sliced;
                chipImage.color = Color.white;
                chipImage.raycastTarget = false;

                TextMeshProUGUI chipLabel = CreateText("Label", chip, options[i], UiTheme.FontHint,
                    TextAlignmentOptions.Center,
                    i == selectedIndex ? UiTheme.Ink : UiTheme.TextLight, secondary);
                Stretch(chipLabel.rectTransform);
            }
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
