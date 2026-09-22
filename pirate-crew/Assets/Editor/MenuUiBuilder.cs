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
    ///   2. 控件工厂：面板/按钮统一出像素件（<see cref="PixelSkin.Plate"/> + SpriteSwap 三态），
    ///      本类是**全局字号体系的唯一入口**（<see cref="FontScale"/>，见下）。
    ///
    /// 【皮肤（换装后）】主菜单链路（<see cref="BuildSettingsPanel"/> / <see cref="BuildConfirmDialog"/>
    /// 与 SceneSetup.BuildMainMenuScene）已切 Beveled Pixel：底板走 <see cref="SketchPanel"/>、
    /// 按钮走 <see cref="SketchButton"/>、分隔线走 <see cref="SketchSeparator"/>；船员管理 / 选关 /
    /// 结算（M3SceneSetup）同族。本类玻璃族工厂（<see cref="CreatePanel"/> / <see cref="CreateButton"/> /
    /// <see cref="CreateWoodBackdrop"/>）已内部改判到像素 tone，签名与调用点不变。
    ///
    /// 【为什么本类是这个波次的主要改动点】主菜单 / 设置 / 员工管理 / HUD 的面板与按钮**全部**
    /// 经本类的 <see cref="CreatePanel"/> / <see cref="CreateButton"/> 装配（Unified 工厂），
    /// 所以换肤只需改这里 + 各构建器的布局调用，不必逐个场景重摆。
    /// </summary>
    public static class MenuUiBuilder
    {
        // ------------------------------------------------------------------
        // 字号体系（【用户裁决 2026-09-14】整体下调一档）
        // ------------------------------------------------------------------

        /// <summary>
        /// 全局字号体系 —— 比 <see cref="UiTheme"/> 的旧档整体下调一档。
        ///
        /// 【为什么在这里而不是改 UiTheme】<see cref="UiTheme"/> 是**运行期**程序集（<c>Assets/Scripts/</c>），
        /// 本波次文件域只到 <c>Assets/Editor/</c>；而 <see cref="UiTheme"/> 的常量又被
        /// <c>SceneSetup</c>/<c>M3SceneSetup</c> 等不在域内的构建器直接引用（它们传的是旧档数字）。
        /// 于是把「旧档 → 新档」的映射放在**所有文本的唯一出口** <see cref="CreateText"/> 里：
        /// 任何构建器（含域外文件）传旧档字号，渲染出来都自动是新档，
        /// 既不用改 UiTheme（不动运行期契约），也不会漏掉某个界面。
        ///
        /// 【旧→新对照（用户原话"字都太大了"）】
        /// <code>
        ///   用途                        旧   新   备注
        ///   FONT_DISPLAY 游戏名         64 → 48
        ///   FONT_BANNER  结算横幅       48 → 36
        ///   FONT_TITLE   界面标题       36 → 26
        ///   FONT_SECTION 区块标题/标题条 24 → 20   ← 任务"标题 24→20"
        ///   FONT_HUD     HUD 常读/名册名 24 → 18   ← 任务"正文 24→18"
        ///   FONT_BODY    按钮/行文本     20 → 15   ← 任务"辅助 20→15"
        ///   FONT_HINT    辅助提示        18 → 14   ← 任务"提示 18→14"
        ///   FONT_TINY    角标            16 → 13   （同比例降一档）
        /// </code>
        /// 【判据同步】docs/UI-UX与中文本地化规范.md 的 V2 判据（原「正文 ≥20px」）按同一裁决
        /// 改为「正文 ≥16px、辅助 ≥14px、角标 ≥12px」——本表的 <see cref="Hud"/>=18、<see cref="Body"/>=15、
        /// <see cref="Hint"/>=14、<see cref="Tiny"/>=13 逐档落在新下限之上。
        /// </summary>
        public static class FontScale
        {
            /// <summary>主菜单游戏名（旧 UiTheme.FontDisplay 64）。</summary>
            public const int Display = 48;

            /// <summary>结算横幅（旧 UiTheme.FontBanner 48）。</summary>
            public const int Banner = 36;

            /// <summary>界面标题（旧 UiTheme.FontTitle 36）。</summary>
            public const int Title = 26;

            /// <summary>区块标题 / 面板标题条（旧 UiTheme.FontSection 24）。</summary>
            public const int Section = 20;

            /// <summary>HUD 常读 / 名册名 / 模式开关 / 回合计时（旧 UiTheme.FontHud 24）。</summary>
            public const int Hud = 18;

            /// <summary>按钮 / 列表行文本 / 说明（旧 UiTheme.FontBody 20）。</summary>
            public const int Body = 15;

            /// <summary>辅助提示 / 通栏提示条（旧 UiTheme.FontHint 18）。</summary>
            public const int Hint = 14;

            /// <summary>角标 / HP 数字（旧 UiTheme.FontTiny 16）。</summary>
            public const int Tiny = 13;
        }

        /// <summary>
        /// 旧档字号 → <see cref="FontScale"/> 新档。
        /// 【为什么用"旧档白名单"而不是"新档白名单"】<see cref="UiTheme"/> 的旧档恰是
        /// <c>{64,48,36,24,20,18,16}</c> 七个数；新档是 <c>{48,36,26,20,18,15,14,13}</c>，两集合**有交集**
        /// （48/36/20/18 既是旧档也是新档），只按数字映射会互相污染（旧 20=正文 → 新 15；
        /// 新 20=区块标题 → 又会被降成 15）。故：<b>本方法只服务"传旧档数字"的老调用点</b>；
        /// 新代码一律走 <see cref="CreateTextExact"/> 直传 <see cref="FontScale"/> 常量，不经本映射。
        /// 未登记的数值原样透传（恒等），避免把 UI 新档数字二次降档。
        /// </summary>
        static int ScaleLegacyFont(int legacy)
        {
            switch (legacy)
            {
                case 64: return FontScale.Display;
                case 48: return FontScale.Banner;
                case 36: return FontScale.Title;
                case 24: return FontScale.Section;   // 旧 FontSection/FontHud 同为 24，统一降到 Section(20)；
                                                     // 需要 Hud(18) 的调用点请显式走 CreateTextExact。
                case 20: return FontScale.Body;
                case 18: return FontScale.Hint;
                case 16: return FontScale.Tiny;
                default: return legacy;
            }
        }

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
        // 像素皮（Beveled Pixel）—— 旧的"亚克力玻璃" Skin 段已整体换装
        // ------------------------------------------------------------------

        /// <summary>
        /// 取像素件 Sprite（旧"玻璃 tone"签名保留——域外 <c>HudMinimapSceneSetup</c> 仍传
        /// <see cref="GlassPanelSpriteBuilder.Tone"/>，内部改判 <see cref="PixelTone"/>）。
        /// 返回的是 Resources/UI/PixelSkin 图集里的持久 Sprite 子资产：Editor 装配的场景
        /// 序列化的是资产引用，重开场景/进播放器都不丢。
        /// </summary>
        public static Sprite GetGlass(GlassPanelSpriteBuilder.Tone tone, bool chip = false)
        {
            return PixelSkin.Plate(PixelOf(tone));
        }

        /// <summary>旧玻璃 tone → 像素 tone（换装映射表；Frame/Backdrop 都落 Frame tone）。</summary>
        static PixelTone PixelOf(GlassPanelSpriteBuilder.Tone tone)
        {
            switch (tone)
            {
                case GlassPanelSpriteBuilder.Tone.Light:
                case GlassPanelSpriteBuilder.Tone.ButtonLight:
                    return PixelTone.Light;
                case GlassPanelSpriteBuilder.Tone.Dense:
                case GlassPanelSpriteBuilder.Tone.Button:
                    return PixelTone.Dense;
                case GlassPanelSpriteBuilder.Tone.Sea:
                    return PixelTone.Sea;
                case GlassPanelSpriteBuilder.Tone.Primary:
                    return PixelTone.Primary;
                case GlassPanelSpriteBuilder.Tone.Danger:
                    return PixelTone.Danger;
                default:
                    return PixelTone.Frame;   // Frame / Backdrop
            }
        }

        /// <summary>旧 Skin 枚举 → 玻璃 tone（面板）。域外构建器仍传旧 Kind，这里统一改判后再落像素 tone。</summary>
        static GlassPanelSpriteBuilder.Tone PanelToneOf(UiSprites.Kind skin)
        {
            switch (skin)
            {
                case UiSprites.Kind.PanelParchment: return GlassPanelSpriteBuilder.Tone.Light;
                default: return GlassPanelSpriteBuilder.Tone.Frame;
            }
        }

        /// <summary>旧 Skin 枚举 → 玻璃 tone（按钮）。</summary>
        static GlassPanelSpriteBuilder.Tone ButtonToneOf(UiSprites.Kind skin)
        {
            switch (skin)
            {
                case UiSprites.Kind.ButtonBrass: return GlassPanelSpriteBuilder.Tone.Primary;
                case UiSprites.Kind.ButtonParchment: return GlassPanelSpriteBuilder.Tone.ButtonLight;
                case UiSprites.Kind.ButtonDanger: return GlassPanelSpriteBuilder.Tone.Danger;
                default: return GlassPanelSpriteBuilder.Tone.Button;
            }
        }

        /// <summary>
        /// 建像素面板：投影（Panel 档才有，先建——子件绘制按加入序）+ Plate 本体（后建）。
        /// 根节点不带 Image（投影必须垫在本体之下），调用方拿到的是根 RectTransform。
        /// </summary>
        public static RectTransform CreateGlassPanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, GlassPanelSpriteBuilder.Tone tone,
            GlassPanelSpriteBuilder.Geo geo = GlassPanelSpriteBuilder.Geo.Panel)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            if (geo == GlassPanelSpriteBuilder.Geo.Panel)
                AddPixelShadow(rect);

            AddPixelPlate(rect, "Plate", PixelOf(tone));
            return rect;
        }

        /// <summary>底垫投影（INK 剪影；按 <see cref="PixelSkin.ShadowOffset"/> 右下错开 1u，不拦截点击）。</summary>
        static void AddPixelShadow(RectTransform root)
        {
            RectTransform shadow = CreateRect("Shadow", root);
            Stretch(shadow);
            shadow.anchoredPosition = PixelSkin.ShadowOffset;
            var image = shadow.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.ShadowSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        /// <summary>铺满父矩形的像素 Plate 本体（不拦截点击：命中留给内容件/按钮）。</summary>
        static Image AddPixelPlate(RectTransform root, string partName, PixelTone tone)
        {
            RectTransform part = CreateRect(partName, root);
            Stretch(part);
            var image = part.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(tone);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：tone 色阶烘在贴图里
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 给已有 Image 换/套像素皮（幂等；用于"先建节点、再贴皮"的复用路径，如小地图面板）。
        /// 顺带把 <c>raycastTarget</c> 关掉：面板是装饰层，挡射线会让底下的 3D 拾取/单位点选失效。
        /// </summary>
        public static void ApplyGlassSkin(Image image, GlassPanelSpriteBuilder.Tone tone, bool chip = false)
        {
            if (image == null)
                return;

            image.sprite = PixelSkin.Plate(PixelOf(tone));
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        // （旧 GlassColors 乘色四态表已随换装删除：像素皮禁用 Image.color 乘色——状态改由
        //   UGUI SpriteSwap 三态贴图承担，禁用态走 CanvasGroup alpha，见 CreateButton。）

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

        /// <summary>
        /// 建 TMP 文本（**旧档字号入口**）：传入 <see cref="UiTheme"/> 的旧档字号常量
        /// （或任意旧档数字），内部按 <see cref="ScaleLegacyFont"/> 降到新档。
        /// 域外构建器（SceneSetup / M3SceneSetup 等）继续传旧档即可自动跟进新字号体系。
        /// 新代码请用 <see cref="CreateTextExact"/> 直传 <see cref="FontScale"/> 常量。
        /// </summary>
        public static TextMeshProUGUI CreateText(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false)
        {
            return CreateTextExact(name, parent, content, ScaleLegacyFont(fontSize), alignment, color, font, raycast);
        }

        /// <summary>建 TMP 文本（**新档字号入口**）：<paramref name="fontSize"/> 原样使用，不做旧→新映射。</summary>
        public static TextMeshProUGUI CreateTextExact(string name, Transform parent, string content, int fontSize,
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

        /// <summary>
        /// 建面板底（**已换 Beveled Pixel 皮**：Plate 九宫格 + 底垫投影）。
        ///
        /// 【兼容口径】签名保留旧的 <paramref name="skin"/>（域外构建器仍在传 <see cref="UiSprites.Kind"/>），
        /// 但内部一律改判：<c>PanelWood</c> → <see cref="PixelTone.Frame"/>（深板岩主底）、
        /// <c>PanelParchment</c> → <see cref="PixelTone.Light"/>（暖白）——
        /// 于是主菜单 / 设置 / 员工管理 / HUD 的全部面板一次性换皮，不必逐个改场景代码。
        ///
        /// 【brassOutline 参数为何保留但不再使用】旧实现靠 UGUI <see cref="Outline"/> 画 3px 黄铜框
        /// （外扩会吃掉安全边距，实测可见边比标称小 2-3px）；像素皮的包边/斜面是**烘进九宫格贴图**的，
        /// 外扩 0px —— 安全边距从此所见即所得。保留参数只为不动域外调用点。
        /// </summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, UiSprites.Kind skin, bool brassOutline = true)
        {
            RectTransform rect = CreateGlassPanel(name, parent, anchor, pivot, anchoredPosition, size,
                PanelToneOf(skin));
            return rect;
        }

        /// <summary>
        /// 给面板加"内容片"（同色系下沉一档的暗片，承载文字/标题）。
        /// 用途：面板框架内需要稳定底衬的文字区（两级结构：框架 tone + 内容片 tone）。
        /// </summary>
        public static RectTransform CreateDenseChip(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            return CreateGlassPanel(name, parent, anchor, pivot, anchoredPosition, size,
                GlassPanelSpriteBuilder.Tone.Dense, GlassPanelSpriteBuilder.Geo.Chip);
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

        /// <summary>
        /// 建按钮（**像素 Plate 底 + SpriteSwap 三态 + TMP 居中文本**）。
        ///
        /// 【换皮口径】保留旧 <paramref name="skin"/> 签名，内部改判 <see cref="ButtonToneOf"/>
        /// → <see cref="PixelOf"/>（ButtonBrass→Primary / ButtonWood→Dense / ButtonParchment→Light /
        /// ButtonDanger→Danger），域外调用点零改动。
        /// 【状态】UGUI <see cref="Selectable.Transition.SpriteSwap"/> 三态贴图（hovered/pressed/selected），
        /// <b>不做 Image.color 乘色</b>。本出口的按钮没有禁用场景（需要禁用视觉的按钮请走
        /// <see cref="SketchButton"/>，它的禁用态是 CanvasGroup alpha 0.55）。
        /// </summary>
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

            PixelTone tone = PixelOf(ButtonToneOf(skin));

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(tone, PixelState.Normal);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：tone 色阶烘在贴图里

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState states = button.spriteState;
            states.highlightedSprite = PixelSkin.Plate(tone, PixelState.Hovered);
            states.pressedSprite = PixelSkin.Plate(tone, PixelState.Pressed);
            states.selectedSprite = states.highlightedSprite;
            button.spriteState = states;

            // 【名字必须叫 "Text"】HUD 武器行建完按钮后会 <c>Find("Text")</c> 删掉居中标签、
            // 换成左对齐标签（BattleHudBuilder），改名会静默留下两个 TMP 叠加。
            TextMeshProUGUI text = CreateText("Text", rect, label, fontSize, TextAlignmentOptions.Center,
                labelColor ?? LabelColorOf(skin), font);
            Stretch(text.rectTransform);

            return button;
        }

        /// <summary>Skin → 正常态底色占位。【本波次】像素底一律 white（色阶烘在 Plate 贴图里）。</summary>
        public static Color BaseColorOf(UiSprites.Kind skin)
        {
            return Color.white;
        }

        /// <summary>
        /// Skin → 标签色。像素皮规则：字色按所落 tone 取可读档（<see cref="PixelSkin.TextColorOn"/>）——
        /// 浅底（金主按钮 / 暖白次级）给墨字，深底给本 tone 亮档字。
        /// </summary>
        public static Color LabelColorOf(UiSprites.Kind skin)
        {
            return PixelSkin.TextColorOn(PixelOf(ButtonToneOf(skin)));
        }

        /// <summary>全屏底 → **像素皮最暗档平涂**（主菜单 / 管理界面；通栏条允许贴边，§1.7）。
        /// <paramref name="tint"/> 仍是"第二层压暗 vignette"的乘色（平涂件不受像素件纪律约束）。</summary>
        public static RectTransform CreateWoodBackdrop(string name, Transform parent, Color tint)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = null;            // 全屏底走平涂（九宫格缩放会把包边拉到屏幕角）
            Color baseColor = PixelSkin.Asset != null
                ? (Color)PixelSkin.MidOf(PixelTone.Frame)
                : new Color(0x3A / 255f, 0x2A / 255f, 0x1E / 255f, 1f);
            image.color = baseColor * tint;
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
            // StickUI 复刻层单字体纪律（UiKit.RuntimeFont 同口径）：全部 StickHand。
            TMP_FontAsset hand = TitleFont;

            RectTransform root = CreateRect("SettingsPanel", canvas);
            Stretch(root);

            CreateDimOverlay("DimOverlay", root);

            // 底板：SketchPanel Dark → Plate(Frame tone) + 底垫投影（像素皮主弹窗底）。
            SketchPanel card = SketchPanel.Create(root.transform, "SettingsCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1281f, 699f), SketchPanel.Tone.Dark);
            RectTransform panel = (RectTransform)card.transform;

            // 标题：像素皮 Frame tone 上的可读浅字 + INK 墨描边（TMP 归一化宽口径同 SketchButton）。
            TextMeshProUGUI titleText = CreateTextExact("Title", panel, UiStrings.SettingsTitle,
                (int)FONT_TITLE, TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Frame), hand);
            SetAnchored(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(600f, 48f),
                new Vector2(0f, -28f));
            ApplyStickTitleOutline(titleText);

            // 标题下蚀刻分隔线（像素皮 Separator 贴图，方向由 Dir 决定）。
            SketchSeparator.Create(panel, "TitleSeparator", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(900f, 2f), SketchSeparator.Direction.Horizontal);

            var result = new SettingsPanelResult();

            // 音量四行（滑条实时改 AudioService，关面板时统一落盘）。
            result.MasterSlider = BuildVolumeRow(panel, 0, UiStrings.SettingsFieldVolumeMaster, hand);
            result.SfxSlider = BuildVolumeRow(panel, 1, UiStrings.SettingsFieldVolumeSfx, hand);
            result.MusicSlider = BuildVolumeRow(panel, 2, UiStrings.SettingsFieldVolumeMusic, hand);
            result.AmbientSlider = BuildVolumeRow(panel, 3, UiStrings.SettingsFieldVolumeAmbient, hand);

            // 画质档（二选一选项块；选中态由控制器按 VideoSettingsService 刷新）。
            BuildSettingsRow(panel, 4, UiStrings.SettingsFieldQuality, hand,
                out result.QualityHighButton, out result.QualitySmoothButton,
                UiStrings.SettingsOptionQualityHigh, UiStrings.SettingsOptionQualitySmooth);
            BuildSettingsRow(panel, 5, UiStrings.SettingsFieldWindowMode, hand,
                out result.FullscreenOnButton, out result.FullscreenOffButton,
                UiStrings.SettingsOptionFullscreen, UiStrings.SettingsOptionWindowed);

            // 提示：角标档 FONT_TINY=11 + TEXT_FAINT（stick-world 次级文字口径），放底部通带。
            TextMeshProUGUI note = CreateTextExact("SaveHint", panel, UiStrings.SettingsSaveHint,
                (int)FONT_TINY, TextAlignmentOptions.Center, TEXT_FAINT, hand);
            SetAnchored(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(1180f, 30f),
                new Vector2(0f, 116f));

            // 恢复默认 / 返回：手绘按钮 Dark 变体（高 BTN_H=32；Accent 金强调留给确认类主行动）。
            result.RestoreButton = CreateSketchButton("RestoreButton", panel, UiStrings.SettingsRestore,
                new Vector2(0.5f, 0f), new Vector2(-260f, 56f), new Vector2(240f, BTN_H),
                SketchButtonKind.Dark);
            result.BackButton = CreateSketchButton("SettingsBackButton", panel, UiStrings.Back,
                new Vector2(0.5f, 0f), new Vector2(260f, 56f), new Vector2(240f, BTN_H),
                SketchButtonKind.Dark);

            root.gameObject.SetActive(false);

            return result;
        }

        /// <summary>
        /// 主菜单链路专用的手绘按钮装配出口：StickHand 单字体 + 字号 0 = 控件默认档
        /// （gd 主题 Button = FONT_HUD），四态沸腾/五态字色全在 <see cref="SketchButton"/> 内。
        /// 返回类型是 <see cref="Button"/> 子类，控制器 [SerializeField] Button 字段直赋兼容。
        /// </summary>
        static Button CreateSketchButton(string name, Transform parent, string label, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, SketchButtonKind kind)
        {
            return SketchButton.Create(parent, name, anchor, new Vector2(0.5f, 0.5f),
                anchoredPosition, size, TitleFont, kind, label, 0f);
        }

        /// <summary>行高与行距（六行布局：四条滑条 + 两组选项块）。</summary>
        const float SettingsRowPitch = 64f;
        const float SettingsRowTop = -150f;
        const float SettingsRowHeight = 52f;

        /// <summary>建一行「字段名 + 音量滑条」（滑条实时驱动，落盘由控制器统一做）。
        /// 【换装最小半径】滑条三件套保持 UGUI 标准件（控制器按 <see cref="Slider"/> 契约接线），
        /// 只换行底板与字段名文字。</summary>
        static Slider BuildVolumeRow(Transform panel, int index, string field, TMP_FontAsset hand)
        {
            RectTransform row = CreateSettingsRowBackground(panel, index);

            // 字段名：像素皮字色按所落 tone 取可读档（行底 = SketchPanel Light → 暖白片 → 墨字）。
            TextMeshProUGUI label = CreateTextExact("Field", row, field, (int)FONT_BODY,
                TextAlignmentOptions.MidlineLeft, PixelSkin.TextColorOn(PixelTone.Light), hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 40f),
                new Vector2(24f, 0f));

            // 滑条（UGUI 标准三件套：底槽 / 填充 / 手柄；皮肤用像素件 Track / Fill / Plate）。
            RectTransform sliderRect = CreateRect("Slider", row);
            SetAnchored(sliderRect, new Vector2(1f, 0.5f), new Vector2(561f, 33f), new Vector2(-24f, 0f));
            var sliderBack = sliderRect.gameObject.AddComponent<Image>();
            sliderBack.sprite = PixelSkin.Track(PixelTone.Frame);   // 滑条背 = 凹槽件
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
            fillImage.sprite = PixelSkin.Fill(PixelFillKind.Neutral);   // 音量条 = 中性进度填充
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
            handle.sizeDelta = new Vector2(18f, 0f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = PixelSkin.Plate(PixelTone.Light);  // 手柄 = 暖白滑块
            handleImage.type = Image.Type.Sliced;
            handleImage.color = Color.white;                        // 像素件禁止乘色
            handleImage.raycastTarget = false;

            slider.handleRect = handle;
            slider.targetGraphic = handleImage;

            return slider;
        }

        /// <summary>建一行「字段名 + 二选一选项块」，返回两个选项按钮。
        /// 选中态由控制器 <c>SetChipSelected</c> 换 sprite（selected = Primary tone 悬停档 /
        /// 未选 = Dense 常态）并同步重挂 SpriteState——像素皮禁乘色。</summary>
        static void BuildSettingsRow(Transform panel, int index, string field, TMP_FontAsset hand,
            out Button primaryOption, out Button secondaryOption, string primaryLabel, string secondaryLabel)
        {
            RectTransform row = CreateSettingsRowBackground(panel, index);

            TextMeshProUGUI label = CreateTextExact("Field", row, field, (int)FONT_BODY,
                TextAlignmentOptions.MidlineLeft, PixelSkin.TextColorOn(PixelTone.Light), hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 40f),
                new Vector2(24f, 0f));

            primaryOption = CreateButton("Option0", row, primaryLabel, new Vector2(1f, 0.5f),
                new Vector2(-448f, 0f), new Vector2(201f, 39f), hand, UiSprites.Kind.ButtonWood,
                PixelSkin.TextColorOn(PixelTone.Dense), UiTheme.FontHint);
            secondaryOption = CreateButton("Option1", row, secondaryLabel, new Vector2(1f, 0.5f),
                new Vector2(-236f, 0f), new Vector2(201f, 39f), hand, UiSprites.Kind.ButtonWood,
                PixelSkin.TextColorOn(PixelTone.Dense), UiTheme.FontHint);
        }

        /// <summary>设置行的底板 + 字段名（滑条行与选项行共用）：SketchPanel Light →
        /// Plate(Light) 暖白片 + 底垫投影。</summary>
        static RectTransform CreateSettingsRowBackground(Transform panel, int index)
        {
            float y = SettingsRowTop - index * SettingsRowPitch;
            SketchPanel rowPanel = SketchPanel.Create(panel, "Row" + index,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(1150f, SettingsRowHeight), SketchPanel.Tone.Light);
            return (RectTransform)rowPanel.transform;
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
                new Vector2(639f, 279f), SketchPanel.Tone.Dark);
            RectTransform panel = (RectTransform)card.transform;

            // 正文：Frame tone 上的次级浅字（像素皮 TextColorOn 档）、FONT_HUD 常读档。
            TextMeshProUGUI message = CreateTextExact("Message", panel, defaultMessage,
                (int)FONT_HUD, TextAlignmentOptions.Center, PixelSkin.LightOf(PixelTone.Frame), TitleFont);
            SetAnchored(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(520f, 80f),
                new Vector2(0f, -96f));

            var result = new ConfirmDialogResult
            {
                Root = root.gameObject,
                Message = message,
                // 确认 = Accent 金强调（StickKit.Confirm 默认 kind 同语义）、取消 = Dark 常规；
                // 高 BTN_H=32。
                OkButton = CreateSketchButton("OkButton", panel, UiStrings.Confirm,
                    new Vector2(0.5f, 0f), new Vector2(-140f, 48f), new Vector2(220f, BTN_H),
                    SketchButtonKind.Accent),
                CancelButton = CreateSketchButton("CancelButton", panel, UiStrings.Cancel,
                    new Vector2(0.5f, 0f), new Vector2(140f, 48f), new Vector2(220f, BTN_H),
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
