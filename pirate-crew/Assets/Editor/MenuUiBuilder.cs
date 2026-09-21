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
    ///   2. 程序化九宫格 Skin：把 <see cref="UiSprites"/> / <see cref="GlassPanelSpriteBuilder"/>
    ///      生成的像素落成 <c>Assets/Art/Sprites/UI/*.png</c> 持久资产（编辑器装配的场景引用必须可序列化，
    ///      内存 Sprite 存不进场景），并按九宫格导入；
    ///   3. 统一控件工厂：亚克力玻璃（半透明）+ TMP，四态配色走 <see cref="UiTheme"/> Token；
    ///      本类是**全局字号体系的唯一入口**（<see cref="FontScale"/>，见下）。
    ///
    /// 【双栈现状】主菜单链路（<see cref="BuildSettingsPanel"/> / <see cref="BuildConfirmDialog"/> 与
    /// SceneSetup.BuildMainMenuScene）已切 StickUI 复刻层：底板走 <see cref="SketchPanel"/>（Tiled 单图）、
    /// 按钮走 <see cref="SketchButton"/>（六变体四态沸腾）、分隔线走 <see cref="SketchSeparator"/>，
    /// 颜色/字号一律 <see cref="StickTokens"/> 令牌（stick-world ui_tokens.json 同源）。
    /// 船员管理 / 选关 / 结算（M3SceneSetup）P2 起已切 StickUI 复刻层，本类玻璃族工厂仅余 MainMenu 设置面板内部豁免项在用
    /// （<see cref="CreatePanel"/> / <see cref="CreateButton"/> / <see cref="CreateWoodBackdrop"/>），
    /// 这些方法的视觉语义不得随主菜单换装漂移。
    ///
    /// 【设计语言：半透明亚克力（液态玻璃）】M3 链路的皮肤：木板 / 羊皮纸的"纯色海报感"
    /// 全量替换为半透明亚克力 —— 场景从面板底下透出来，靠「半透明底 + 顶部高光带 + 双色 1px 描边
    /// + 3px 更透的厚度带 + ±2% 噪点」做玻璃拟态（UGUI 无真模糊，取舍说明见
    /// <see cref="GlassPanelSpriteBuilder"/> 的类注释）。风格参照隔壁 game-2（stick-world）的
    /// 黑玻璃语言（WINDOW_BG 黑 88% / BORDER_PANEL 白 30% / RADIUS_PANEL 6 / 2~3% 纸感噪点），
    /// 但**资产全部自产**（<see cref="GlassPanelSpriteBuilder"/> 逐像素程序化生成），不复制其 PNG。
    /// 唯一强调色仍是黄铜金 <see cref="UiTheme.Brass"/>（对应 game-2 的琥珀）。
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
        // 亚克力玻璃 Skin（本波次；生成器见 GlassPanelSpriteBuilder）
        // ------------------------------------------------------------------

        /// <summary>
        /// 取亚克力玻璃九宫格 Sprite（<paramref name="chip"/> = true 取 44px 小件档，否则 64px 面板档）。
        /// 生成/落盘/九宫格导入全部由 <see cref="GlassPanelSpriteBuilder"/> 负责（自产资产，非参照库 PNG）。
        /// </summary>
        public static Sprite GetGlass(GlassPanelSpriteBuilder.Tone tone, bool chip = false)
        {
            return GlassPanelSpriteBuilder.Get(
                tone, chip ? GlassPanelSpriteBuilder.Geo.Chip : GlassPanelSpriteBuilder.Geo.Panel);
        }

        /// <summary>旧 Skin 枚举 → 玻璃 tone（面板）。域外构建器仍传旧 Kind，这里统一改判到玻璃族。</summary>
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

        /// <summary>建亚克力玻璃面板（显式指定 tone；新代码用这个）。</summary>
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

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GlassPanelSpriteBuilder.Get(tone, geo);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 【纪律】玻璃色已烘进贴图，禁止再用 color 调色（会把 alpha 乘坏）
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>
        /// 给已有 Image 换/套玻璃皮（幂等；用于"先建节点、再贴皮"的复用路径，如小地图面板）。
        /// 顺带把 <c>raycastTarget</c> 关掉：面板是装饰层，挡射线会让底下的 3D 拾取/单位点选失效
        /// （可点的是按钮自己的 Image，不走本方法）。
        /// </summary>
        public static void ApplyGlassSkin(Image image, GlassPanelSpriteBuilder.Tone tone, bool chip = false)
        {
            if (image == null)
                return;

            image.sprite = GetGlass(tone, chip);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        /// <summary>
        /// 玻璃按钮的底色乘色（四态）——<b>乘在烘焙好的玻璃 Sprite 上</b>，不是最终色。
        ///
        /// 【为什么 hover 用"提亮 + 加 alpha"】玻璃的透度来自贴图 alpha，hover 要让面板"更实更亮"
        /// （game-2 的 BTN_BG 7% → HOVER 15% 同思路）：亮 22% + alpha ×1.15
        /// （0.80 → 0.92），最坏底（纯白）下浅字 #F5E8C8 仍有 <b>5.13:1</b>；
        /// 若只提亮不加 alpha（维持 0.80），合成色被白底抬到 4.06:1 —— <b>不合格</b>，故 alpha 必须同升。
        ///
        /// 【disabled 态为何低于 4.5:1】禁用态压到 ×0.58 / alpha ×0.55（合成 2.16:1）是**有意**的：
        /// 禁用控件的"读不出"本身就是状态信号，且 WCAG 2.1 §1.4.3 明确把 inactive 控件排除在
        /// 对比度要求之外。真正要保证的是"禁用 ≠ 正常"可辨（V3：相邻态通道差 ≥12/255 ——
        /// disabled 与 normal 的差远超阈值）。
        /// </summary>
        static ColorBlock GlassColors(GlassPanelSpriteBuilder.Tone tone)
        {
            Color normal, hover, pressed, disabled;
            switch (tone)
            {
                case GlassPanelSpriteBuilder.Tone.Primary:
                    // 金主按钮：底已很亮，hover 只轻微提亮（保持深墨字的对比）。
                    normal = Color.white;
                    hover = new Color(1.12f, 1.12f, 1.14f, 1.02f);
                    pressed = new Color(0.82f, 0.82f, 0.84f, 1.06f);
                    disabled = new Color(0.55f, 0.55f, 0.57f, 0.55f);
                    break;
                case GlassPanelSpriteBuilder.Tone.Danger:
                    normal = Color.white;
                    hover = new Color(1.18f, 1.12f, 1.12f, 1.00f);
                    pressed = new Color(0.78f, 0.74f, 0.74f, 1.02f);
                    disabled = new Color(0.6f, 0.55f, 0.55f, 0.55f);
                    break;
                case GlassPanelSpriteBuilder.Tone.ButtonLight:
                    normal = Color.white;
                    hover = new Color(1.06f, 1.06f, 1.06f, 1.06f);
                    pressed = new Color(0.86f, 0.86f, 0.88f, 1.08f);
                    disabled = new Color(0.62f, 0.62f, 0.64f, 0.55f);
                    break;
                default:
                    normal = Color.white;
                    hover = new Color(1.22f, 1.22f, 1.26f, 1.15f);   // 提亮 + 变实（见方法注释的对比度推导）
                    pressed = new Color(0.72f, 0.72f, 0.74f, 1.12f);
                    disabled = new Color(0.58f, 0.58f, 0.60f, 0.55f);
                    break;
            }

            var block = new ColorBlock
            {
                normalColor = normal,
                highlightedColor = hover,
                pressedColor = pressed,
                selectedColor = hover,
                disabledColor = disabled,
                colorMultiplier = 1f,
                fadeDuration = 0.09f,
            };
            return block;
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
        /// 建面板底（**本波次起改为亚克力玻璃**）。
        ///
        /// 【兼容口径】签名保留旧的 <paramref name="skin"/>（域外构建器仍在传 <see cref="UiSprites.Kind"/>），
        /// 但内部一律改判到玻璃族：<c>PanelWood</c> → <see cref="GlassPanelSpriteBuilder.Tone.Frame"/>（深色框架）、
        /// <c>PanelParchment</c> → <see cref="GlassPanelSpriteBuilder.Tone.Light"/>（暖白）——
        /// 于是主菜单 / 设置 / 员工管理 / HUD 的全部面板一次性换皮，不必逐个改场景代码。
        ///
        /// 【brassOutline 参数为何保留但不再使用】旧实现靠 UGUI <see cref="Outline"/> 画 3px 黄铜框
        /// （外扩会吃掉安全边距，实测可见边比标称小 2-3px）；玻璃描边是**贴着圆角烘进贴图**的
        /// 双色 1px 线，外扩 0px —— 安全边距从此所见即所得。保留参数只为不动域外调用点。
        /// 需要"当前态"额外描边时，调用方仍可显式调 <see cref="AddOutline"/>（如模式开关的激活段）。
        /// </summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, UiSprites.Kind skin, bool brassOutline = true)
        {
            RectTransform rect = CreateGlassPanel(name, parent, anchor, pivot, anchoredPosition, size,
                PanelToneOf(skin));
            return rect;
        }

        /// <summary>
        /// 给面板/色块加亚克力"内容片"（较实的玻璃条，承载文字/金标题）。
        /// 用途：面板框架（0.70，较透）内需要稳定底衬的文字区 —— 玻璃拟态的两级结构
        /// （game-2 的 window_panel + menu_hover 同构）。
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
        /// 建按钮（**亚克力玻璃底 + 四态乘色 + TMP 居中文本**）。
        ///
        /// 【换皮口径】与 <see cref="CreatePanel"/> 同理：保留旧 <paramref name="skin"/> 签名，
        /// 内部改判 <see cref="ButtonToneOf"/>（ButtonBrass→金主按钮 / ButtonWood→深色玻璃 /
        /// ButtonParchment→暖白玻璃 / ButtonDanger→深酒红玻璃），域外调用点零改动即换皮。
        /// 【四态】走 <see cref="GlassColors"/> 的乘色（hover = 提亮 22% + alpha 0.80→0.92 变实），
        /// 玻璃透度由贴图烘焙，乘色只做状态差 —— 这是 UGUI 里"半透明按钮"唯一不脏的做法。
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

            GlassPanelSpriteBuilder.Tone tone = ButtonToneOf(skin);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetGlass(tone, chip: true);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 玻璃色已烘进贴图（见 ApplyGlassSkin 的纪律说明）

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = GlassColors(tone);

            // 【名字必须叫 "Text"】HUD 武器行建完按钮后会 <c>Find("Text")</c> 删掉居中标签、
            // 换成左对齐标签（BattleHudBuilder），改名会静默留下两个 TMP 叠加。
            TextMeshProUGUI text = CreateText("Text", rect, label, fontSize, TextAlignmentOptions.Center,
                labelColor ?? LabelColorOf(skin), font);
            Stretch(text.rectTransform);

            return button;
        }

        /// <summary>Skin → 正常态底色（Button.color 的乘色）。【本波次】玻璃底一律 white（色已烘焙）。</summary>
        public static Color BaseColorOf(UiSprites.Kind skin)
        {
            return Color.white;
        }

        /// <summary>
        /// Skin → 标签色。玻璃族规则：亮玻璃（金主按钮 / 暖白次级）用深墨字，深玻璃用浅米字。
        /// 对比度（最坏底 = 纯白场景，指数见 <see cref="GlassPanelSpriteBuilder"/> 类注释）：
        /// 深墨 #2A2A2A 压金玻璃 ≥5.79:1、压暖白玻璃 ≥8.56:1；浅米 #F5E8C8 压深玻璃 ≥9.56:1。
        /// </summary>
        public static Color LabelColorOf(UiSprites.Kind skin)
        {
            switch (skin)
            {
                case UiSprites.Kind.ButtonBrass:
                case UiSprites.Kind.ButtonParchment:
                    return UiTheme.Ink;
                default:
                    return UiTheme.TextLight;
            }
        }

        /// <summary>全屏木板背景 → **全屏亚克力底**（主菜单 / 管理界面；通栏条允许贴边，§1.7）。</summary>
        public static RectTransform CreateWoodBackdrop(string name, Transform parent, Color tint)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetGlass(GlassPanelSpriteBuilder.Tone.Backdrop);
            image.type = Image.Type.Sliced;
            image.color = tint;             // 这里颜色是**有意**的乘色（第二层压暗 vignette 靠它）
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

            // 底板：SketchPanel Dark（Tiled 单图 + 沸腾帧轮换；stick-world 大弹窗主底）。
            SketchPanel card = SketchPanel.Create(root.transform, "SettingsCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1280f, 700f), SketchPanel.Tone.Dark);
            RectTransform panel = (RectTransform)card.transform;

            // 标题：TEXT 亮字 + INK 墨描边（stick-world 标题档 FONT_TITLE=24），直接压面板底。
            TextMeshProUGUI titleText = CreateTextExact("Title", panel, UiStrings.SettingsTitle,
                (int)FONT_TITLE, TextAlignmentOptions.Center, TEXT, hand);
            SetAnchored(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(600f, 48f),
                new Vector2(0f, -28f));
            ApplyStickTitleOutline(titleText);

            // 标题下手绘波浪分隔线（SketchSeparator 自绘，BORDER@0.35）。
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

            // 字段名：TEXT 亮字压 panel_light 暗底（stick-world Light 档同为暗色系，浅字才可读）。
            TextMeshProUGUI label = CreateTextExact("Field", row, field, (int)FONT_BODY,
                TextAlignmentOptions.MidlineLeft, TEXT, hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 40f),
                new Vector2(24f, 0f));

            // 滑条（UGUI 标准三件套：底槽 / 填充 / 手柄；皮肤用玻璃小件）。
            RectTransform sliderRect = CreateRect("Slider", row);
            SetAnchored(sliderRect, new Vector2(1f, 0.5f), new Vector2(560f, 32f), new Vector2(-24f, 0f));
            sliderRect.gameObject.AddComponent<Image>().sprite = GetGlass(GlassPanelSpriteBuilder.Tone.Button, chip: true);
            sliderRect.gameObject.GetComponent<Image>().type = Image.Type.Sliced;

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
            fillImage.sprite = GetGlass(GlassPanelSpriteBuilder.Tone.Primary, chip: true);
            fillImage.type = Image.Type.Sliced;
            fillImage.raycastTarget = false;
            slider.fillRect = fill;

            RectTransform handleArea = CreateRect("Handle Slide Area", sliderRect);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            RectTransform handle = CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(16f, 0f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = GetGlass(GlassPanelSpriteBuilder.Tone.ButtonLight, chip: true);
            handleImage.type = Image.Type.Sliced;
            handleImage.raycastTarget = false;

            slider.handleRect = handle;
            slider.targetGraphic = handleImage;

            return slider;
        }

        /// <summary>建一行「字段名 + 二选一选项块」，返回两个选项按钮（选中态由控制器刷新）。
        /// 【换装最小半径】选项块保持玻璃 <see cref="CreateButton"/>：控制器
        /// <c>SetChipSelected</c> 靠 image.color 乘色表达选中态，与手绘四态贴图机制冲突，
        /// 换手绘须先重定义选中态语义（本波次不动控制器）。</summary>
        static void BuildSettingsRow(Transform panel, int index, string field, TMP_FontAsset hand,
            out Button primaryOption, out Button secondaryOption, string primaryLabel, string secondaryLabel)
        {
            RectTransform row = CreateSettingsRowBackground(panel, index);

            TextMeshProUGUI label = CreateTextExact("Field", row, field, (int)FONT_BODY,
                TextAlignmentOptions.MidlineLeft, TEXT, hand);
            SetAnchored(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 40f),
                new Vector2(24f, 0f));

            primaryOption = CreateButton("Option0", row, primaryLabel, new Vector2(1f, 0.5f),
                new Vector2(-448f, 0f), new Vector2(200f, 40f), hand, UiSprites.Kind.ButtonWood,
                UiTheme.TextLight, UiTheme.FontHint);
            secondaryOption = CreateButton("Option1", row, secondaryLabel, new Vector2(1f, 0.5f),
                new Vector2(-236f, 0f), new Vector2(200f, 40f), hand, UiSprites.Kind.ButtonWood,
                UiTheme.TextLight, UiTheme.FontHint);
        }

        /// <summary>设置行的底板 + 字段名（滑条行与选项行共用）：
        /// SketchPanel Light（Tiled 单图，stick-world「HUD 横条/内嵌区块」档）。</summary>
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

            // 底板：SketchPanel Dark（stick-world 大弹窗主底）；消息直接压面板，不再垫内容片。
            SketchPanel card = SketchPanel.Create(root.transform, "ConfirmCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(640f, 280f), SketchPanel.Tone.Dark);
            RectTransform panel = (RectTransform)card.transform;

            // 正文：TEXT_DIM 暗字（StickConfirmDialog 消息档同色）、FONT_HUD=17 常读档。
            TextMeshProUGUI message = CreateTextExact("Message", panel, defaultMessage,
                (int)FONT_HUD, TextAlignmentOptions.Center, TEXT_DIM, TitleFont);
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
