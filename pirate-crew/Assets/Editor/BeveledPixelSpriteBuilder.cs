using System;
using System.Collections.Generic;
using System.IO;
using PirateCrew.EditorTools.Art;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **Beveled Pixel UI**（像素斜面浮雕）九宫格 Sprite 的程序化生成器。
    ///
    /// 【它替掉谁】换皮前的 <see cref="GlassPanelSpriteBuilder"/>（半透明亚克力/玻璃拟态）是
    /// 上一版 UI 语言的底座；创始人裁决 UI 改走「Beveled Pixel + Chunky Pixel」后，
    /// **视觉层整体重写、机制骨架（StickUI 令牌/骨架/装配器/布局）照旧沿用**——
    /// 本类是"换皮不换骨"里的那张皮。两个生成器并存：玻璃族仍在役（4b 试点后才逐步退役），
    /// 图集目录也分开（<c>Assets/Art/Sprites/UI/Pixel/</c> vs <c>.../UI/</c>），互不覆盖。
    ///
    /// 【几何从哪来：量出来的，不是想出来的】三个常量（基本单位 2px / 外环-斜面-内暗线 三层 /
    /// 同一色相 3 档明暗）逐条来自
    /// [UI 像素化标准参照](../../../docs/images/ui-pixel-ref/README.md) §二 的逐像素竖切表，
    /// 那一节是对创始人指定的 Terraria 截图片段取竖切量出来的（含**内暗线**这道——
    /// 它是"平面色块"变成"有厚度的块"的唯一来源）。
    /// 本类只量**几何与色彩关系**，不复制它的任何贴图资源；色相一律换成
    /// <c>Assets/Data/Palette/pirate_palette.json</c> 的槽位（本工程色板），
    /// 于是"换主题只换色相"成立。三类参照件与它们的取用边界见
    /// [Beveled Pixel 九宫格规范](../../../docs/技术/资产管线/BeveledPixel九宫格规范.md)。
    ///
    /// 【一、几何（单位 u = 2px，从外缘向内）】
    /// <code>
    ///   序号  层      上/左（受光侧）  下/右（背光侧）   厚度
    ///   ①    外环     S2（同色相暗）   S1（更暗一档）    u
    ///   ②    斜面     S4（同色相亮）   S3（同色相中）    u
    ///   ③    内暗线   S2               S1                u
    ///   ④    内部     见「二、两种内部」                 剩余（九宫格拉伸区）
    /// </code>
    /// 三条由参照表直接读出的规则，别改：
    ///   1. **内暗线**（③）两侧颜色与同侧外环（①）**同色**——实测"外环 <c>#54481E</c> / 内暗线 <c>#54481E</c>"、
    ///      "外环 <c>#2F261F</c> / 内暗线 <c>#2F261F</c>"。这不是巧合，是"外圈把块勒住 + 内圈把面压下去"的同一条线；
    ///   2. **背光侧不是纯黑**（是 S3 中间档）——这是"铁牌/木牌"而不是"描边"的关键；
    ///   3. **光永远来自左上**：上=左、下=右，四边只有两套色。
    ///      实测参照里顶边比左边还亮一档（<c>#6C76C7</c> vs <c>#5A65C0</c>），本波次不拆这一档
    ///      （要拆就得给每个 tone 第 5 档明暗），已登记为 4b 复核项；
    ///   4. **三层带是同心环**：外环必须是一条**闭合圈**（沿切角斜线走），斜面与内暗线是它里面
    ///      两条更小的闭合圈。按"上下横贯 + 左右补边"画会把两侧外环截断、四角不闭合——
    ///      走查记录见 <c>CreateTexture</c> 方法头与规范 §一.1；判据 = 外环 8 连通 1 段且无端点。
    ///
    /// 【二、两种内部（Piece）】
    ///   · <see cref="Piece.Plate"/> **凸起块**：内部 = S3（同色相中间档，平涂、无渐变——参照表
    ///     "基色平涂"）。面板 / 按钮 / 列表行 / 标题条都用它。
    ///   · <see cref="Piece.Track"/> **凹槽**：内部 = <c>Floor</c>（比最暗档再压一档）——条状件
    ///     （血条/施法条/进度条）的**空槽底**，填充件画在它上面。
    ///     实测空槽底带一道贴着上内暗线的 2px 次生亮带（<c>#801034</c> 压 <c>#551B1C</c>）；
    ///     本波次**有意压平成单色**：满格时它整条被填充盖住、半格时才露出 2px，
    ///     收益不抵"多一条派生规则"的维护成本。已登记为 4b 复核项。
    ///
    /// 【三、状态（State）】按压 = **高光/阴影对调**（创始人裁决，不用 scale）：把上/左那套
    /// 与下/右那套整组互换，"凸"读成"凹"。位移 1px 右下**不在贴图里**——那是装配侧的事，
    /// 统一取 <see cref="PressOffset"/>（往贴图里烘位移会让九宫格切片错位）。
    ///
    /// 【四、令牌：色从哪来】每个 tone 三档明暗**全部取调色板槽位 id**（不写死 hex）：
    /// <code>
    ///   Frame   （面板）    UI_BEVEL_HI / UI_PANEL    / UI_BEVEL_LO
    ///   Dense   （内容片）  UI_PANEL    / UI_BEVEL_LO  / 派生（面板最暗档再压一档）
    ///   Light   （暖白牌）  WHITE_HOT   / SAIL_CANVAS / NEUTRAL_LIGHT
    ///   Sea     （海图）    SEA_SHALLOW / SEA_MID     / SEA_DEEP
    ///   Primary （主行动点）SAND_LIGHT  / BRASS       / WOOD_DARK
    ///   Danger  （危险）    HERO_RED    / UI_DANGER   / HERO_RED_DEEP
    ///   Warn    （警告）    GLOW_WARM   / UI_WARN     / SHADOW_WARM
    /// </code>
    /// 调色板 UI 族目前只有 3 个中性槽（且都是**提案**态），凑不出第 4 档明暗，于是
    /// **S1 由 S2 派生**：<c>S1 = mix(S2, INK, 0.45)</c>（往本仓统一墨色 <c>INK</c> 压一步）。
    /// 派生规则只有这一条，写在 <c>Ramp.Of</c> 里，不外扩。
    /// 【提案/待定】S1 的派生（尤其浅色牌 Light 的外环是否该直接改用 <c>INK</c> 近黑——
    /// 第三张参照图的工具控件就是近黑描边）由 4b 一屏试点出图裁决。
    ///
    /// 【五、为什么产物是贴图而不是"平色 Image"】UGUI 的平色矩形画不出 3 层带（外环/斜面/内暗线）
    /// 又要在任意尺寸下不变形——九宫格（<c>spriteBorder</c>）正是为此存在的机制：
    /// 只有四个角 + 四条边不拉伸，中心 1px 被拉长。故贴图尺寸必须
    /// <c>≥ 2 × border</c>（<see cref="PlateMinRender"/> = 16px），装配侧低于这个尺寸就会
    /// 把角切进内容区。判据见 <see cref="Verify"/>。
    ///
    /// 【六、像素导入纪律】本族**不在** <see cref="PixelArtTextureRules.PixelRoot"/> 约定域内
    /// （那套规则把 textureType 定死为 Default，Sprite 会被判成"没有 sprite"而静默退化成白块），
    /// 但**共用同一份五值**（Point / 无 mip / Uncompressed / sRGB on / alphaIsTransparency **关**）——
    /// 值取自 <see cref="PixelArtTextureRules.ApplySprite"/>，规格只有一处。
    /// <c>alphaIsTransparency</c> 必须关还有个具体后果：本族的四个角是**切角**（见 <c>IsChamfer</c>），
    /// 开后 Unity 会把 RGB 膨胀进那些透明像素，切角边缘会带出板外色。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/UI/重烘焙 Beveled Pixel 九宫格
    ///   无头: -batchmode -nographics -quit -projectPath &lt;工程&gt;
    ///         -executeMethod PirateCrew.EditorTools.BeveledPixelSpriteBuilder.BuildFromCommandLine   （烘焙 + 判据 + 接触表）
    ///         -executeMethod PirateCrew.EditorTools.BeveledPixelSpriteBuilder.VerifyFromCommandLine  （只判据，不写盘）
    ///   管线: <see cref="ArtGate"/> 的 ⑦.5 步（纯 CPU 像素，不依赖渲染路径）
    /// </summary>
    public static class BeveledPixelSpriteBuilder
    {
        // ==================================================================
        // 一、几何常量（单位 u = 2px；改这些数字前先读类头 §一）
        // ==================================================================

        /// <summary>基本单位（像素）。参照表的九段色带/六段框架都是它的整数倍。</summary>
        public const int Unit = 2;

        /// <summary>Plate / Track 贴图边长（px）。只影响"最小可渲染尺寸"，不影响拉伸后的外观。</summary>
        public const int PlateSize = 32;

        /// <summary>Plate / Track 的九宫格切片边框（px）= 3 层带（u×3=6）+ u 的内容余量。</summary>
        public const int PlateBorder = 8;

        /// <summary>低于这个尺寸装配就不能用九宫格（角会切进内容区）。装配侧应断言。</summary>
        public const int PlateMinRender = PlateBorder * 2;

        /// <summary>
        /// 切角深度（**单位数**，不是像素数；1 单位 = <see cref="Unit"/> px）。
        /// 1 = 每个角切掉**一个 2×2 的整格**（默认）；2 = 切两格（4px → 2px 阶梯）；0 = 方角。
        ///
        /// 【为什么默认 1】两处实测摆在一起看就清楚了：
        ///   · 参照的**面板**角（成就内板）切掉的是**一个格子**（1px 体系里 1 格）→ 换算到 2px 体系 = 1 格 = 2px；
        ///   · 参照的**条状件端头**是 4px → 2px → 0 的阶梯（= 2 格），但那是"条的斜切端"，不是面板角。
        /// 面板角取前者。试过 2 格：32px 的块看起来开始"变圆"，与 chunky 的取向相抵。
        /// 【4b 复核项】嫌角小改 `2`（条状件端头那档），要方角改 `0`。改一处即全族生效。
        /// </summary>
        public const int ChamferDepthUnits = 1;

        /// <summary>
        /// 某点是否落在切角里（<c>true</c> = 透明）。
        ///
        /// 判据 <c>floor(dx / u) + floor(dy / u) &lt; 深度</c>：把到两边界的距离**量化到基本单位**再相加，
        /// 于是切掉的永远是**整格**（45° 折线落在 2px 网格上），不会出现半个格子的锯齿。
        ///
        /// 【实测依据】参照条状件端头从角向内的切深是 **4px（第一带）→ 2px（第二带）→ 0px**——
        /// 逐格累计正好是"深度 2 格"。若按像素判（<c>dx + dy &lt; 2</c>），得到的是
        /// 2px → 1px → 0px 的 45° 锯齿：几何上是 45° 斜边，但在 2px 体系里**离格**，
        /// 视觉上既不平也不斜，读成"缺角"。这是本类第一次实现时踩的坑。
        /// </summary>
        static bool IsChamfer(int x, int y, int n)
        {
            if (ChamferDepthUnits <= 0)
                return false;
            int dx = Math.Min(x, n - 1 - x) / Unit;
            int dy = Math.Min(y, n - 1 - y) / Unit;
            return dx + dy < ChamferDepthUnits;
        }

        /// <summary>切角像素总数（4 个角 × <c>深度×(深度+1)/2</c> 个格 × 每格 <c>Unit²</c> 像素）。</summary>
        public static int ChamferPixelCount()
        {
            int d = ChamferDepthUnits;
            return d <= 0 ? 0 : 4 * (d * (d + 1) / 2) * Unit * Unit;
        }

        /// <summary>Fill 贴图边长（px）：上 u 亮 / 中 2×u 主体 / 下 u 暗（实测 <c>HP_Fill.png</c> 12×12 同构）。</summary>
        public const int FillSize = 12;

        /// <summary>Fill 的上下切片边框 = u（亮/暗带各一层）；右端另有 u 宽的"前缘"列。</summary>
        public const int FillBorder = Unit;

        /// <summary>Fill 的最低可渲染高度（上下两层带）。低于它带会被压没。</summary>
        public const int FillMinRender = FillBorder * 2;

        /// <summary>
        /// 按压态的元素位移（UI 像素）：右下 1px。**装配侧用这个常量**，
        /// 别再各写各的（贴图里不烘位移，烘了九宫格切片就错位）。
        /// </summary>
        public static readonly Vector2 PressOffset = new Vector2(1f, -1f);

        /// <summary>产物目录（与玻璃族分开，4b 试点后玻璃族逐步退役）。</summary>
        const string SpriteFolder = PixelArtTextureRules.UiSpriteFolder;

        /// <summary>接触表（评审用，落 <c>export/</c> 暂存目录，不入库）。</summary>
        const string SheetRelativePath = "export/ui-pixel-4a/contact-sheet-2x.png";

        /// <summary>接触表的整数放大倍率（2px 带在 1:1 下太小，评审需要放大看；整数倍才不糊）。</summary>
        const int SheetZoom = 2;

        // ==================================================================
        // 二、枚举与烘焙目标
        // ==================================================================

        /// <summary>色身份（一个 tone = 一条同色相的 4 档明暗阶梯）。</summary>
        public enum Tone
        {
            /// <summary>主面板（调色板 UI 族三槽的原生用途）。</summary>
            Frame,
            /// <summary>内容片/列表行（比面板整体低一档）。</summary>
            Dense,
            /// <summary>暖白牌（标题板 / 浅按钮）。</summary>
            Light,
            /// <summary>海图（小地图面板 / 航海相关容器）。</summary>
            Sea,
            /// <summary>主行动点（黄铜，深字）。</summary>
            Primary,
            /// <summary>危险动作（红）。</summary>
            Danger,
            /// <summary>警告/冷却（暖橙）。</summary>
            Warn,
        }

        /// <summary>结构（内部是什么）。</summary>
        public enum Piece
        {
            /// <summary>凸起块：内部 = 同色相中间档（平涂）。面板/按钮/列表行。</summary>
            Plate,
            /// <summary>凹槽：内部 = 比最暗档再压一档。条状件的空槽底。</summary>
            Track,
        }

        /// <summary>状态。</summary>
        public enum State
        {
            /// <summary>常态（上/左受光）。</summary>
            Normal,
            /// <summary>按压态（高光/阴影整组对调）。</summary>
            Pressed,
        }

        /// <summary>填充色身份（条状件画在 <see cref="Piece.Track"/> 上的那一层）。</summary>
        public enum FillKind
        {
            /// <summary>红（血量 / 红队）。</summary>
            Red,
            /// <summary>蓝（魔法 / 蓝队）。</summary>
            Blue,
            /// <summary>暖橙（冷却 / 蓄力）。</summary>
            Warn,
            /// <summary>海蓝（水量 / 航行进度）。</summary>
            Sea,
            /// <summary>暖白（中性进度，如装配进度条）。</summary>
            Neutral,
        }

        /// <summary>一条烘焙目标（公开可反射：契约测试按它逐张断言尺寸/切片/导入设置）。</summary>
        [Serializable]
        public sealed class BakeTarget
        {
            /// <summary>资产名（不含扩展名）。</summary>
            public string name;
            /// <summary>工程内资产路径（含扩展名）。</summary>
            public string assetPath;
            /// <summary>贴图宽（px）。</summary>
            public int width;
            /// <summary>贴图高（px）。</summary>
            public int height;
            /// <summary>九宫格切片边框 <c>(left, bottom, right, top)</c>。</summary>
            public Vector4 border;
            /// <summary>是否为填充件（判据要区分：填充无切角、透明确认数为 0）。</summary>
            public bool isFill;
        }

        // ==================================================================
        // 三、几何：带色与切角
        // ==================================================================

        /// <summary>一条边上的三层带（从外缘向内）：外环 / 斜面 / 内暗线。</summary>
        struct Edge
        {
            public Color32 Ring, Bevel, Line;

            /// <summary>受光侧（上/左）：外环取 S2、斜面取最亮的 S4、内暗线与外环同色（规则 1）。</summary>
            public static Edge Lit(Ramp r)
            {
                return new Edge { Ring = r.S2, Bevel = r.S4, Line = r.S2 };
            }

            /// <summary>背光侧（下/右）：斜面取中间档 S3——**不是纯黑**（规则 2）。</summary>
            public static Edge Shade(Ramp r)
            {
                return new Edge { Ring = r.S1, Bevel = r.S3, Line = r.S1 };
            }
        }

        // ==================================================================
        // 四、四档明暗阶梯（Ramp）与调色板取色
        // ==================================================================

        /// <summary>一条 tone 的四档明暗 + 凹槽底。<c>S4</c> 最亮、<c>S1</c> 最暗。</summary>
        struct Ramp
        {
            public Color32 S4, S3, S2, S1, Floor;

            /// <summary>
            /// 由三档槽位色构造：<c>S1 = mix(S2, INK, 0.45)</c>（调色板 UI 族只有三档，第四档派生），
            /// <c>Floor = mix(S1, INK, 0.5)</c>（凹槽底，比**最暗档**再压一档——凹槽要压到底，
            /// 否则底内暗线与槽底同色，那道线在槽里等于没画）。
            /// **派生只有这两条**——多一条就会多一个地方要同步。
            /// 【色空间】按 sRGB（Gamma 直存）逐通道线性混合：本工程 <c>m_ActiveColorSpace = 0</c>，
            /// 参照表给的关系也是设计师在 Gamma 下调出来的；OkLCH 那一套（<see cref="ArtPaletteMath"/>）
            /// 是**量化**用的，别拿到这里来"更正确"。
            /// </summary>
            public static Ramp Of(Color32 hi, Color32 mid, Color32 dark)
            {
                Color32 ink = Slot("INK");
                Color32 s1 = Mix(dark, ink, 0.45f);
                return new Ramp
                {
                    S4 = hi,
                    S3 = mid,
                    S2 = dark,
                    S1 = s1,
                    Floor = Mix(s1, ink, 0.5f),
                };
            }
        }

        /// <summary>逐通道线性混合（四舍五入到 byte）。未混合的通道**逐字节等于**槽位值。</summary>
        static Color32 Mix(Color32 a, Color32 b, float t)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
                (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
                (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
                255);
        }

        /// <summary>感知亮度（WCAG 2.1 相对亮度的简化式，仅用于**单调性判据**，不是物理量）。</summary>
        static float Lum(Color32 c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        /// <summary>tone → 三档槽位（<c>dark</c> 为空表示从 <c>deriveDarkFrom</c> 的暗档再压一档）。</summary>
        struct ToneSlots
        {
            public string hi, mid, dark, deriveDarkFrom;
        }

        /// <summary>tone 的三档槽位表。**改色只改这里**（值都是槽位 id，不写死 hex）。</summary>
        static ToneSlots SlotsOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Dense:
                    // 内容片 = 面板整体低一档：亮的用面板中档、中的用面板暗档、暗的再压一步。
                    return new ToneSlots { hi = "UI_PANEL", mid = "UI_BEVEL_LO", deriveDarkFrom = "UI_BEVEL_LO" };
                case Tone.Light:
                    // 暖白牌取"羊皮纸"三档：亮档近白、中档沙色、暗档中灰。**中档不能用 SAIL_CANVAS**
                    // ——它跟 WHITE_HOT 的亮度只差 7.6，斜面会读不出来（判据 RampStepRatio 抓过一次）。
                    return new ToneSlots { hi = "WHITE_HOT", mid = "SAND_LIGHT", dark = "NEUTRAL_LIGHT" };
                case Tone.Sea:
                    return new ToneSlots { hi = "SEA_SHALLOW", mid = "SEA_MID", dark = "SEA_DEEP" };
                case Tone.Primary:
                    return new ToneSlots { hi = "SAND_LIGHT", mid = "BRASS", dark = "WOOD_DARK" };
                case Tone.Danger:
                    // 暗档用 SHADOW_DEEP（冷紫）而不是 HERO_RED_DEEP：后者与 UI_DANGER 亮度比 0.93，
                    // 两档几乎同亮、斜面读不出来。冷紫暗部正是板里 SHADOW 族存在的理由
                    //（渲染篇 §4.2 替换式暗部：暗部不靠乘暗，靠换色相）。
                    return new ToneSlots { hi = "HERO_RED", mid = "UI_DANGER", dark = "SHADOW_DEEP" };
                case Tone.Warn:
                    return new ToneSlots { hi = "GLOW_WARM", mid = "UI_WARN", dark = "SHADOW_WARM" };
                default:
                    return new ToneSlots { hi = "UI_BEVEL_HI", mid = "UI_PANEL", dark = "UI_BEVEL_LO" };
            }
        }

        /// <summary>tone 的成套明暗。</summary>
        static Ramp RampOf(Tone tone)
        {
            ToneSlots s = SlotsOf(tone);
            // dark 缺省 = 从 deriveDarkFrom 的暗档再压一档（Dense 用：它没有第四个 UI 槽可用）。
            Color32 dark = string.IsNullOrEmpty(s.dark)
                ? Mix(Slot(s.deriveDarkFrom), Slot("INK"), 0.45f)
                : Slot(s.dark);
            return Ramp.Of(Slot(s.hi), Slot(s.mid), dark);
        }

        /// <summary>填充色的三档（主体 / 暗沿 / 亮沿）+ 前缘色。</summary>
        struct Fill
        {
            public Color32 Body, Dark, Hi, Tip;
            /// <summary>前缘提亮规则：<c>mix(带色, Hi, 0.28)</c>——见 <see cref="CreateFillTexture"/>。</summary>
            public const float TipMix = 0.28f;
        }

        static string FillBodySlot(FillKind kind)
        {
            switch (kind)
            {
                case FillKind.Blue: return "HERO_BLUE";
                case FillKind.Warn: return "UI_WARN";
                case FillKind.Sea: return "SEA_MID";
                // 中性填充分"沙/羊皮纸"档（SAND_MID）：不能用 SAIL_CANVAS——它跟 WHITE_HOT 的亮沿
                // 只差 3%，亮沿会读不出来（判据 RampStepRatio 抓过一次）。
                case FillKind.Neutral: return "SAND_MID";
                default: return "HERO_RED";
            }
        }

        static string FillDarkSlot(FillKind kind)
        {
            switch (kind)
            {
                case FillKind.Blue: return "HERO_BLUE_DEEP";
                case FillKind.Warn: return "SHADOW_WARM";
                case FillKind.Sea: return "SEA_DEEP";
                case FillKind.Neutral: return "SAND_DARK";
                default: return "HERO_RED_DEEP";
            }
        }

        /// <summary>亮沿槽位；<c>null</c> = 派生平移到 <c>WHITE_HOT</c> 的 55%（除中性档以外的全部）。</summary>
        static string FillHiSlot(FillKind kind)
        {
            return kind == FillKind.Neutral ? "WHITE_HOT" : null;
        }

        /// <summary>
        /// 填充色表。主体与暗沿**取槽位**；亮沿**派生**（<c>mix(主体, WHITE_HOT, 0.55)</c>）——
        /// 依据是实测：<c>HP_Fill.png</c> 的上沿 <c>#FFA68B</c> 是主体 <c>#F53C40</c> 往白里提的暖调，
        /// 板里没有这一档，硬加槽位是拿"条状件专用色"去占全局板位。
        /// 【4b 复核项】实测上沿的青通道抬得比蓝通道多（<c>#FFA68B</c>：g166/b139），
        /// 本规则给的 <c>#FAA7A9</c> 偏中性；差异只在"提亮是否偏黄"这一档，试点出图再看要不要补色相偏移。
        /// </summary>
        static Fill FillOf(FillKind kind)
        {
            Color32 body = Slot(FillBodySlot(kind));
            string hiSlot = FillHiSlot(kind);
            Color32 hi = hiSlot != null ? Slot(hiSlot) : Mix(body, Slot("WHITE_HOT"), 0.55f);
            return new Fill
            {
                Body = body,
                Dark = Slot(FillDarkSlot(kind)),
                Hi = hi,
                // 前缘（右端 u 列）= 带色往亮沿提一档。实测 <c>HP_Fill</c> 右端 2px 列整列提亮（<c>#F66451</c>）：
                // 那是"条在生长"唯一的视觉锚点——没有它，半格的条两端一样暗，读不出方向。
                Tip = Mix(body, hi, Fill.TipMix),
            };
        }

        // ==================================================================
        // 五、调色板（真源 = JSON，不读 .asset 镜像）
        // ==================================================================

        static Dictionary<string, Color32> s_slots;

        /// <summary>本生成器引用到的全部槽位 id（判据用：缺槽必须在写盘前红灯，不能烘出品红贴图）。</summary>
        public static string[] UsedSlotIds()
        {
            var ids = new List<string> { "INK", "WHITE_HOT" };
            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                ToneSlots s = SlotsOf(tone);
                ids.Add(s.hi);
                ids.Add(s.mid);
                if (!string.IsNullOrEmpty(s.dark))
                    ids.Add(s.dark);
                if (!string.IsNullOrEmpty(s.deriveDarkFrom))
                    ids.Add(s.deriveDarkFrom);
            }
            foreach (FillKind kind in Enum.GetValues(typeof(FillKind)))
            {
                ids.Add(FillBodySlot(kind));
                ids.Add(FillDarkSlot(kind));
                string hi = FillHiSlot(kind);
                if (!string.IsNullOrEmpty(hi))
                    ids.Add(hi);
            }
            return ids.ToArray();
        }

        /// <summary>
        /// 按槽位 id 取色（**读 JSON 真源**，不读 <c>PaletteAsset</c> 镜像——
        /// 手改 JSON 后忘跑镜像生成是真实的坑，生成器不该踩）。装配侧要"平色底"（全屏背景）也走这里。
        /// 缺槽返回**品红**并报错（沿用 <see cref="PaletteAsset.ColorOf"/> 的"一眼可见"口径）；
        /// 正式的缺槽红灯在 <see cref="Verify"/> / <see cref="BuildAll"/> 的预检里。
        /// </summary>
        public static Color32 Slot(string id)
        {
            EnsurePalette();
            if (s_slots.TryGetValue(id, out Color32 c))
                return c;
            Debug.LogError("[BeveledPixelSpriteBuilder] 调色板没有槽位 " + id
                + "（真源 " + PaletteAssetBuilder.JsonPath + "）——该色会渲染成品红。");
            return new Color32(255, 0, 255, 255);
        }

        static void EnsurePalette()
        {
            if (s_slots != null)
                return;

            s_slots = new Dictionary<string, Color32>();
            string abs = Path.Combine(UnityProjectRoot(), PaletteAssetBuilder.JsonPath);
            if (!File.Exists(abs))
            {
                Debug.LogError("[BeveledPixelSpriteBuilder] 读不到调色板真源：" + abs);
                return;
            }

            PaletteJson.Root root = PaletteJson.Parse(File.ReadAllText(abs));
            for (int i = 0; i < root.slots.Count; i++)
            {
                PaletteSlot slot = root.slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.id))
                    continue;
                if (!PaletteAsset.ParseHex(slot.hex, out Color color))
                {
                    Debug.LogError("[BeveledPixelSpriteBuilder] 槽位 " + slot.id + " 的 hex 非法：" + slot.hex);
                    continue;
                }
                s_slots[slot.id] = new Color32(
                    (byte)Mathf.RoundToInt(color.r * 255f),
                    (byte)Mathf.RoundToInt(color.g * 255f),
                    (byte)Mathf.RoundToInt(color.b * 255f),
                    255);
            }
        }

        /// <summary>Unity 工程根（<c>&lt;仓库&gt;/pirate-crew</c>）。</summary>
        static string UnityProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        /// <summary>仓库根（<c>export/</c> 在它下面）。</summary>
        static string RepoRoot()
        {
            DirectoryInfo parent = Directory.GetParent(UnityProjectRoot());
            return parent == null ? UnityProjectRoot() : parent.FullName;
        }

        // ==================================================================
        // 六、画图（纯像素，无 GPU 依赖）
        // ==================================================================

        /// <summary>
        /// 画 Plate / Track 的九宫格贴图。<paramref name="border"/> 通过
        /// <c>TextureImporter.spriteBorder</c> 落成切片；四个角与四条边不拉伸、中心 1px 拉长。
        ///
        /// 【三层带是**同心环**，不是"上下横贯 + 左右补边"】判据按**离最近边界的距离**分层：
        /// <code>
        ///   layer = min(dx, dy) / u      // dx = min(x, n-1-x)，dy = min(y, n-1-y)
        ///   layer 0 = 外环 · 1 = 斜面 · 2 = 内暗线 · ≥3 = 内容
        ///   受光/背光：按"离哪条边更近"定——竖直边（左/右）或水平边（上/下）；
        ///              到两边等距的那个**角像素**归竖直边（让外环在角上不断线）。
        /// </code>
        /// 于是外环是一条**闭合的**环（切角沿 45° 斜线走），斜面与内暗线是它里面两条更小的闭合环。
        ///
        /// 【为什么不是"上下带整幅横贯"】那是本类第一版的写法（依据是实测的**平铺中段件**
        /// <c>HP_Panel_Middle.png</c>——它的顶部 6 行确实整幅宽度都是顶边带，但**它根本没有左右带**，
        /// 所以那段测量无法决定角上谁赢）。按"上下横贯"画出来后，水平带的**斜面**会一直顶到块的左右外缘：
        /// 左/右两条边的外环在那 2px 上被一道亮带截断，**四角的轮廓线不闭合**——
        /// 创始人看图一眼指出"4 角不是连着的"，走查后改成同心环。参照的面板件
        /// （<c>Achievement_InnerPanelTop</c>，6×22）也正是这个结构：竖列 x=0 的外环通到顶行，
        /// 顶行的亮带只铺在 x=1..4（内宽），**不外扩到竖列里去**。
        /// </summary>
        public static Texture2D CreateTexture(Tone tone, Piece piece, State state, out Vector4 border)
        {
            Ramp r = RampOf(tone);
            Edge lit = Edge.Lit(r);
            Edge shade = Edge.Shade(r);
            Edge top = state == State.Pressed ? shade : lit;
            Edge bottom = state == State.Pressed ? lit : shade;
            Color32 body = piece == Piece.Track ? r.Floor : r.S3;

            int n = PlateSize;
            var px = new Color32[n * n];

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (IsChamfer(x, y, n))
                    {
                        // 切角：显式写全零（不是"没画"）——alphaIsTransparency 关掉后
                        // Unity 不会把 RGB 膨胀进这里，切角边缘因此不会带出板外色。
                        px[y * n + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    int dx = Math.Min(x, n - 1 - x);
                    int dy = Math.Min(y, n - 1 - y);
                    int layer = Math.Min(dx, dy) / Unit;
                    bool vertical = dx <= dy;                 // 平局 → 竖直边（角像素不断线）
                    bool isLit = vertical ? x * 2 < n : y * 2 >= n;   // 竖直：左亮；水平：上亮

                    Color32 c;
                    if (layer >= 3)
                        c = body;
                    else if (layer == 1)
                        c = isLit ? top.Bevel : bottom.Bevel;  // 斜面：受光取最亮档、背光取中间档
                    else
                        c = isLit ? top.Ring : bottom.Ring;    // 外环与内暗线同色（规则 1）

                    px[y * n + x] = c;
                }
            }

            border = new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder);
            return ToTexture(px, n, n);
        }

        /// <summary>
        /// 画填充条的九宫格贴图：上 u 亮沿 / 中 2u 主体 / 下 u 暗沿，右端 u 列是"前缘"（提亮）。
        /// 切片边框 <c>(0, u, u, u)</c>：**左端不切片**（条的尾端要硬边，跟槽的起点贴合），
        /// 右端切片（前缘列不被拉长），上下切片（亮/暗沿保持 u 厚）。
        /// </summary>
        public static Texture2D CreateFillTexture(FillKind kind, out Vector4 border)
        {
            Fill f = FillOf(kind);
            int n = FillSize;
            var px = new Color32[n * n];

            for (int y = 0; y < n; y++)
            {
                int fromTop = n - 1 - y;
                Color32 band = fromTop < Unit ? f.Hi : (fromTop >= n - Unit ? f.Dark : f.Body);
                for (int x = 0; x < n; x++)
                {
                    // 右端 u 列的"前缘"：整列往亮沿提一档。亮沿自身混合后不变（Mix(hi,hi)=hi），
                    // 于是这条规则对三层带只有一处行为差异——不需要按带分支。
                    px[y * n + x] = x >= n - FillBorder ? Mix(band, f.Hi, Fill.TipMix) : band;
                }
            }

            border = new Vector4(0f, FillBorder, FillBorder, FillBorder);
            return ToTexture(px, n, n);
        }

        static Texture2D ToTexture(Color32[] px, int w, int h)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels32(px);
            texture.filterMode = FilterMode.Point;      // 像素硬边（与导入设置同口径）
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 按九宫格语义把一张贴图（<paramref name="src"/>，边长 <paramref name="srcSize"/>）画进画布矩形。
        /// 接触表用它**自证切片语义成立**——"读 PNG 色带"只证明画对了，
        /// "拉长后角与边不变形"才证明九宫格可用，而那个只有真画一遍才知道。
        /// 坐标一律 <c>y=0 在下</c>（与 <c>Texture2D.SetPixels32</c> 同口径）。
        /// </summary>
        public static void DrawNineSliced(Color32[] src, int srcSize, Vector4 border,
            Color32[] dst, int dstW, int ox, int oy, int w, int h)
        {
            int bL = Mathf.RoundToInt(border.x), bB = Mathf.RoundToInt(border.y);
            int bR = Mathf.RoundToInt(border.z), bT = Mathf.RoundToInt(border.w);

            // 越界要说人话：接触表的版面算错时，报出"哪块矩形、画布多大"，
            // 别抛一句 Index was outside the bounds of the array 让人反查布局。
            if (ox < 0 || oy < 0 || ox + w > dstW || (oy + h) * dstW > dst.Length)
            {
                throw new ArgumentOutOfRangeException("DrawNineSliced",
                    "矩形 (" + ox + "," + oy + ") " + w + "×" + h + " 超出画布 "
                    + dstW + "×" + (dst == null ? 0 : dst.Length / dstW) + "。");
            }

            for (int j = 0; j < h; j++)
            {
                int sy = MapAxis(j, h, srcSize, bB, bT);
                for (int i = 0; i < w; i++)
                {
                    int sx = MapAxis(i, w, srcSize, bL, bR);
                    dst[(oy + j) * dstW + ox + i] = src[sy * srcSize + sx];
                }
            }
        }

        /// <summary>
        /// 一维九宫格映射：低端不伸缩（逐像素取源）→ 中心坍到切片起点那一像素（= 等价 1:1 拉伸）
        /// → 高端不伸缩。边框为 0 的一侧自然退化成"整段都是中心"。
        /// </summary>
        static int MapAxis(int i, int dstLen, int srcLen, int bLo, int bHi)
        {
            if (i < bLo)
                return i;
            int hiStart = dstLen - bHi;
            if (i >= hiStart)
                return srcLen - bHi + (i - hiStart);
            return bLo;
        }

        // ==================================================================
        // 七、公开取用（装配侧走这里，别自己去 LoadAssetAtPath）
        // ==================================================================

        static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        /// <summary>tone 的亮档（装配侧要拿同族色给文字/线框时用，别再自己调色）。</summary>
        public static Color32 LightOf(Tone tone) { return RampOf(tone).S4; }
        /// <summary>tone 的中间档（= Plate 的底色）。</summary>
        public static Color32 MidOf(Tone tone) { return RampOf(tone).S3; }
        /// <summary>tone 的暗档。</summary>
        public static Color32 DarkOf(Tone tone) { return RampOf(tone).S2; }

        /// <summary>资产名（<c>Pixel_&lt;Piece&gt;_&lt;Tone&gt;[_Pressed].png</c>）。</summary>
        public static string AssetNameOf(Tone tone, Piece piece, State state)
        {
            return "Pixel_" + piece + "_" + tone + (state == State.Pressed ? "_Pressed" : "");
        }

        /// <summary>填充件资产名（<c>Pixel_Fill_&lt;Kind&gt;.png</c>）。</summary>
        public static string AssetNameOf(FillKind kind)
        {
            return "Pixel_Fill_" + kind;
        }

        /// <summary>取（必要时生成）tone × 结构 × 状态的九宫格 Sprite。</summary>
        public static Sprite Get(Tone tone, Piece piece = Piece.Plate, State state = State.Normal)
        {
            string name = AssetNameOf(tone, piece, state);
            return GetOrBake(name, path =>
            {
                Texture2D texture = CreateTexture(tone, piece, state, out Vector4 border);
                return Generate(path, texture, border);
            });
        }

        /// <summary>取（必要时生成）填充条 Sprite。</summary>
        public static Sprite GetFill(FillKind kind)
        {
            string name = AssetNameOf(kind);
            return GetOrBake(name, path =>
            {
                Texture2D texture = CreateFillTexture(kind, out Vector4 border);
                return Generate(path, texture, border);
            });
        }

        static Sprite GetOrBake(string name, Func<string, Sprite> bake)
        {
            if (s_cache.TryGetValue(name, out Sprite cached) && cached != null)
                return cached;

            string path = SpriteFolder + "/" + name + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                sprite = bake(path);

            s_cache[name] = sprite;
            return sprite;
        }

        // ==================================================================
        // 八、烘焙目标表（接触表顺序 = 这个顺序；契约测试也读它）
        // ==================================================================

        /// <summary>
        /// 全部要烘焙的资产（声明顺序 = 接触表的行序）。
        /// **有意不是"全枚举直积"**：凹槽没有按压态（槽不会更凹）、填充没有状态
        /// （状态属于容器不属于内容），直积会烘出 9 张永远没人用的贴图。
        /// </summary>
        public static BakeTarget[] Targets()
        {
            var list = new List<BakeTarget>();

            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                list.Add(NewTarget(AssetNameOf(tone, Piece.Plate, State.Normal)));
                list.Add(NewTarget(AssetNameOf(tone, Piece.Plate, State.Pressed)));
                list.Add(NewTarget(AssetNameOf(tone, Piece.Track, State.Normal)));
            }

            foreach (FillKind kind in Enum.GetValues(typeof(FillKind)))
                list.Add(NewTarget(AssetNameOf(kind)));

            return list.ToArray();
        }

        static BakeTarget NewTarget(string name)
        {
            bool isFill = name.StartsWith("Pixel_Fill_", StringComparison.Ordinal);
            return new BakeTarget
            {
                name = name,
                assetPath = SpriteFolder + "/" + name + ".png",
                width = isFill ? FillSize : PlateSize,
                height = isFill ? FillSize : PlateSize,
                border = isFill
                    ? new Vector4(0f, FillBorder, FillBorder, FillBorder)
                    : new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder),
                isFill = isFill,
            };
        }

        /// <summary>把目标名字解析回画法（<see cref="Targets"/> 的逆）。</summary>
        static Texture2D CreateByName(string name, out Vector4 border)
        {
            if (name.StartsWith("Pixel_Fill_", StringComparison.Ordinal))
            {
                var kind = (FillKind)Enum.Parse(typeof(FillKind), name.Substring("Pixel_Fill_".Length));
                return CreateFillTexture(kind, out border);
            }

            string rest = name.Substring("Pixel_".Length);
            int cut = rest.IndexOf('_');
            var piece = (Piece)Enum.Parse(typeof(Piece), rest.Substring(0, cut));
            State state = rest.Substring(cut + 1).EndsWith("_Pressed", StringComparison.Ordinal)
                ? State.Pressed
                : State.Normal;
            return CreateTexture(ToneOfName(name), piece, state, out border);
        }

        /// <summary>从资产名解析 tone（<c>Pixel_Plate_Frame[_Pressed]</c> → <c>Tone.Frame</c>）。</summary>
        static Tone ToneOfName(string name)
        {
            string rest = name.Substring("Pixel_".Length);
            int cut = rest.IndexOf('_');
            return (Tone)Enum.Parse(typeof(Tone), ParseToneTail(rest.Substring(cut + 1)));
        }

        static string ParseToneTail(string tail)
        {
            return tail.EndsWith("_Pressed", StringComparison.Ordinal)
                ? tail.Substring(0, tail.Length - "_Pressed".Length)
                : tail;
        }

        // ==================================================================
        // 九、入口：烘焙 / 判据 / 接触表
        // ==================================================================

        /// <summary>
        /// 重烘焙全部九宫格 + 跑判据 + 出接触表。**不终止进程**（管线步骤可复用）；
        /// 判据不过抛 <see cref="InvalidOperationException"/>，命令行入口据此给非零退出码。
        /// </summary>
        [MenuItem("PirateCrew/UI/重烘焙 Beveled Pixel 九宫格")]
        public static void BuildAll()
        {
            s_cache.Clear();
            s_slots = null;                     // 强制重读真源（同一次会话里改过板也能生效）
            EnsurePalette();

            // 预检：缺槽必须在写盘之前红灯，否则会烘出一批品红贴图。
            var missing = new List<string>();
            foreach (string id in UsedSlotIds())
            {
                if (!s_slots.ContainsKey(id))
                    missing.Add(id);
            }
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "[BeveledPixelSpriteBuilder] 调色板缺少槽位：" + string.Join(", ", missing.ToArray())
                    + "（真源 " + PaletteAssetBuilder.JsonPath + "）。缺槽时烘焙会画成品红——先补板再加 tone。");
            }

            BakeTarget[] targets = Targets();
            for (int i = 0; i < targets.Length; i++)
            {
                Texture2D texture = CreateByName(targets[i].name, out Vector4 border);
                WriteSprite(targets[i].assetPath, texture, targets[i].border);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            List<string> problems = Verify();
            string sheet = BuildContactSheet();

            Debug.Log("[BeveledPixelSpriteBuilder] Beveled Pixel 九宫格重烘焙完成，共 " + targets.Length
                + " 张：" + SpriteFolder + "（接触表 " + sheet + "）");
            LogRampReport();

            if (problems.Count > 0)
            {
                for (int i = 0; i < problems.Count; i++)
                    Debug.LogError("[BeveledPixelSpriteBuilder] " + problems[i]);
                throw new InvalidOperationException(
                    "[BeveledPixelSpriteBuilder] 判据不过 " + problems.Count + " 条，资产已写盘但不可用（见上）。");
            }
        }

        /// <summary>无头入口：烘焙 + 判据，按结果给退出码（**唯一该终止进程的地方**）。</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                BuildAll();
            }
            catch (Exception e)
            {
                Debug.LogError("[BeveledPixelSpriteBuilder] 命令行烘焙失败：" + e.Message);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        /// <summary>无头入口：只跑判据（不写盘），按结果给退出码。</summary>
        public static void VerifyFromCommandLine()
        {
            List<string> problems = Verify();
            for (int i = 0; i < problems.Count; i++)
                Debug.LogError("[BeveledPixelSpriteBuilder] " + problems[i]);
            Debug.Log("[BeveledPixelSpriteBuilder] 判据 "
                + (problems.Count == 0 ? "全部通过" : problems.Count + " 条不通过"));
            EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        /// <summary>把每条 tone 的实测色阶打进日志（评审时不用开取色器；也是改参数的对照表）。</summary>
        static void LogRampReport()
        {
            var sb = new System.Text.StringBuilder("[BeveledPixelSpriteBuilder] 色阶实测（S4亮→S1暗 / 槽底）：\n");
            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                Ramp r = RampOf(tone);
                sb.Append("  ").Append(tone.ToString().PadRight(8))
                    .Append(Hex(r.S4)).Append(" / ").Append(Hex(r.S3)).Append(" / ").Append(Hex(r.S2))
                    .Append(" / ").Append(Hex(r.S1)).Append("  · 槽底 ").Append(Hex(r.Floor))
                    .Append('\n');
            }
            foreach (FillKind kind in Enum.GetValues(typeof(FillKind)))
            {
                Fill f = FillOf(kind);
                sb.Append("  Fill:").Append(kind.ToString().PadRight(8))
                    .Append(Hex(f.Hi)).Append(" / ").Append(Hex(f.Body)).Append(" / ").Append(Hex(f.Dark))
                    .Append("  · 前缘 ").Append(Hex(f.Tip)).Append('\n');
            }
            Debug.Log(sb.ToString());
        }

        // ==================================================================
        // 十、判据（程序化，不看"像不像"）
        // ==================================================================

        /// <summary>
        /// 全部判据：返回违反项清单（空 = 通过）。**每一条都是可复算的数字**：
        /// 阶梯单调、几何成立（色带边界落偶数行）、颜色锁在 tone 的色阶里、
        /// 盘上资产与生成器输出逐像素一致（改了参数忘重烘焙就会被抓到）。
        ///
        /// 编辑器的契约测试（<c>Assets/Art/Tests/BeveledPixelSkinTests.cs</c>）另做一遍
        /// **不依赖本类的独立复核**：直接读盘上 PNG，量色带边界与"光来自左上"的明暗方向。
        /// </summary>
        public static List<string> Verify()
        {
            var problems = new List<string>();
            EnsurePalette();

            foreach (string id in UsedSlotIds())
            {
                if (!s_slots.ContainsKey(id))
                    problems.Add("调色板缺槽位：" + id);
            }

            if (problems.Count == 0)
            {
                foreach (Tone tone in Enum.GetValues(typeof(Tone)))
                {
                    Ramp r = RampOf(tone);
                    if (!(Lum(r.S1) < Lum(r.S2) && Lum(r.S2) < Lum(r.S3) && Lum(r.S3) < Lum(r.S4)))
                    {
                        problems.Add("tone " + tone + " 的四档明度不单调（必须 S1<S2<S3<S4）："
                            + Hex(r.S1) + " < " + Hex(r.S2) + " < " + Hex(r.S3) + " < " + Hex(r.S4)
                            + "，实测 " + Lum(r.S1).ToString("F1") + "/" + Lum(r.S2).ToString("F1")
                            + "/" + Lum(r.S3).ToString("F1") + "/" + Lum(r.S4).ToString("F1")
                            + "——三档槽位挑错了色相（如把亮槽放进了暗位）。");
                    }
                    problems.AddRange(CheckSteps("tone " + tone,
                        new[] { r.S4, r.S3, r.S2, r.S1 },
                        new[] { "S4亮", "S3中", "S2暗", "S1更深" }));
                    if (Lum(r.Floor) >= RampStepRatio * Lum(r.S1))
                    {
                        problems.Add("tone " + tone + " 的凹槽底与最暗档分不开（Floor " + Hex(r.Floor)
                            + " vs S1 " + Hex(r.S1) + "，亮度比 "
                            + (Lum(r.Floor) / Lum(r.S1)).ToString("F2") + "）：槽内那道底内暗线会等于没画。");
                    }
                }
                foreach (FillKind kind in Enum.GetValues(typeof(FillKind)))
                {
                    Fill f = FillOf(kind);
                    problems.AddRange(CheckSteps("填充 " + kind,
                        new[] { f.Hi, f.Body, f.Dark },
                        new[] { "亮沿", "主体", "暗沿" }));
                }
            }

            BakeTarget[] targets = Targets();
            for (int i = 0; i < targets.Length; i++)
                problems.AddRange(VerifyTarget(targets[i]));

            CheckGeometryConstants(problems, PlateSize, PlateBorder, FillSize, FillBorder, Unit);

            return problems;
        }

        /// <summary>
        /// 几何常量之间的自洽（值全在类头 §一；这里挡的是"有人只改了一个常量"）。
        /// 参数化而不是直接比常量：直接比是**编译期可折叠的恒真式**，编译器会为
        /// 永不执行的 <c>problems.Add</c> 报 CS0162，于是这条守卫会被警告"劝退"。
        /// </summary>
        static void CheckGeometryConstants(List<string> problems,
            int plateSize, int plateBorder, int fillSize, int fillBorder, int unit)
        {
            if (plateSize < 2 * plateBorder)
                problems.Add("PlateSize(" + plateSize + ") 小于 2×border(" + plateBorder * 2
                    + ")：九宫格中心区为空，切角会切进内容。");
            if (plateSize < 6 * unit)
                problems.Add("PlateSize(" + plateSize + ") 小于 3 层带×2(" + 6 * unit
                    + ")：上下带会互相压到，中间没有内容区。");
            if (fillSize < 2 * fillBorder)
                problems.Add("FillSize(" + fillSize + ") 小于 2×border(" + fillBorder * 2 + ")。");
            if (plateSize % unit != 0 || plateBorder % unit != 0 || fillSize % unit != 0
                || fillBorder % unit != 0)
                problems.Add("尺寸或边框不是基本单位 " + unit + " 的整数倍（色带边界会落到奇数行）。");
        }

        static List<string> VerifyTarget(BakeTarget t)
        {
            var problems = new List<string>();
            string label = t.name;
            string abs = Path.Combine(UnityProjectRoot(), t.assetPath);

            if (!File.Exists(abs))
            {
                problems.Add(label + "：盘上没有这个资产（" + t.assetPath + "）。");
                return problems;
            }

            Texture2D fresh = CreateByName(t.name, out Vector4 border);
            var disk = new Texture2D(2, 2);
            bool loaded = disk.LoadImage(File.ReadAllBytes(abs));
            try
            {
                if (!loaded)
                {
                    problems.Add(label + "：盘上 PNG 解不开。");
                    return problems;
                }
                if (disk.width != t.width || disk.height != t.height)
                    problems.Add(label + "：尺寸 " + disk.width + "×" + disk.height
                        + "，应为 " + t.width + "×" + t.height + "。");
                if (border != t.border)
                    problems.Add(label + "：生成器给的切片边框 " + border + " 与目标表 " + t.border + " 不一致。");

                Color32[] a = fresh.GetPixels32();
                Color32[] b = disk.GetPixels32();
                if (a.Length == b.Length)
                {
                    int diff = -1;
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (!Same(a[i], b[i])) { diff = i; break; }
                    }
                    if (diff >= 0)
                    {
                        problems.Add(label + "：盘上资产与生成器输出不一致（首个差异像素 #" + diff
                            + " 盘上 " + Hex(b[diff]) + " / 生成 " + Hex(a[diff])
                            + "）——改了生成器参数但没重烘焙。");
                    }
                }

                problems.AddRange(CheckBandsAndHoles(label, fresh, t.isFill ? 0 : ChamferPixelCount(), t.isFill));
                problems.AddRange(CheckPaletteLock(label, t, fresh));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fresh);
                UnityEngine.Object.DestroyImmediate(disk);
            }

            var importer = AssetImporter.GetAtPath(t.assetPath) as TextureImporter;
            if (importer == null)
            {
                problems.Add(label + "：读不到 TextureImporter。");
            }
            else
            {
                List<string> importProblems = PixelArtTextureRules.CheckSprite(importer, t.border);
                for (int i = 0; i < importProblems.Count; i++)
                    problems.Add(label + "：" + importProblems[i]);
            }

            return problems;
        }

        /// <summary>
        /// 几何判据：中轴色带边界必须落在 <see cref="Unit"/> 的整数倍上。
        /// 这就是参照 README §六 的"色带边界必须落在偶数行"——**1px 或 3px 的带 = 参数写错**。
        /// 另加"孔洞计数"：除切角外不应有任何透明像素。
        /// </summary>
        static List<string> CheckBandsAndHoles(string label, Texture2D tex, int expectedTransparent, bool isFill)
        {
            var problems = new List<string>();
            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height;

            var column = new Color32[h];
            for (int y = 0; y < h; y++)
                column[y] = px[y * w + w / 2];
            problems.AddRange(CheckBands(label + " 竖切 x=" + w / 2, column, "行"));

            var row = new Color32[w];
            for (int x = 0; x < w; x++)
                row[x] = px[(h / 2) * w + x];
            problems.AddRange(CheckBands(label + " 横切 y=" + h / 2, row, "列"));

            int transparent = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a != 255)
                    transparent++;
            }
            if (transparent != expectedTransparent)
                problems.Add(label + "：透明像素 " + transparent + " 个，应为 " + expectedTransparent
                    + " 个（凸起块/凹槽只该有切角，填充件不该有孔洞）。");

            if (!isFill)
                problems.AddRange(CheckRingClosed(label, px, w, h));

            return problems;
        }

        /// <summary>
        /// **外环必须是一条闭合圈**：三层带是同心环，所以最外那一圈像素（到边界距离在 1 个单位内的
        /// 非透明像素）必须 <b>8 连通为 1 个分量</b>、且没有任何"端点"（每个外环像素至少有两个外环邻居）。
        ///
        /// 【为什么要这条】本类第一版把三层带画成"上下横贯 + 左右补边"，结果水平带的**斜面**
        /// 一直顶到块的左右外缘，把左/右两条边在那 2px 上截断——四个角的轮廓线不闭合。
        /// 创始人看图直接问"为什么 4 角还是不是连着的"，走查后改成同心环。
        /// 人眼能看出"断"，但只有把它变成这条可复算的判据，才拦得住下一次回归。
        /// </summary>
        static List<string> CheckRingClosed(string label, Color32[] px, int w, int h)
        {
            var problems = new List<string>();
            var isRing = new bool[px.Length];
            int count = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (px[i].a == 0)
                        continue;
                    int dx = Math.Min(x, w - 1 - x);
                    int dy = Math.Min(y, h - 1 - y);
                    if (Math.Min(dx, dy) / Unit != 0)
                        continue;
                    isRing[i] = true;
                    count++;
                }
            }

            // 8 连通分量数
            var seen = new bool[px.Length];
            int components = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < px.Length; i++)
            {
                if (!isRing[i] || seen[i])
                    continue;
                components++;
                seen[i] = true;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    int cx = cur % w, cy = cur / w;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            if (ox == 0 && oy == 0)
                                continue;
                            int nx = cx + ox, ny = cy + oy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                continue;
                            int ni = ny * w + nx;
                            if (isRing[ni] && !seen[ni])
                            {
                                seen[ni] = true;
                                stack.Push(ni);
                            }
                        }
                    }
                }
            }

            if (components != 1)
            {
                problems.Add(label + "：外环有 " + components + " 段（应为 1 段闭合圈）——"
                    + "上/下带整幅横贯会把它截断，四角的轮廓就[不连着]了（本类第一版就是这个毛病）。");
            }

            int ends = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (!isRing[i])
                    continue;
                int cx = i % w, cy = i / w;
                int neighbours = 0;
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;
                        int nx = cx + ox, ny = cy + oy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        if (isRing[ny * w + nx])
                            neighbours++;
                    }
                }
                if (neighbours < 2)
                    ends++;
            }
            if (ends > 0)
            {
                problems.Add(label + "：外环有 " + ends + " 个端点（外环像素的邻居少于 2 个）——"
                    + "闭合圈不该有端点，出现了说明某处只有 1px 相连或断开了。");
            }

            return problems;
        }

        static List<string> CheckBands(string label, Color32[] axis, string axisName)
        {
            var problems = new List<string>();
            int start = 0;
            for (int i = 1; i <= axis.Length; i++)
            {
                if (i < axis.Length && Same(axis[i], axis[start]))
                    continue;

                int band = i - start;
                if (band % Unit != 0)
                {
                    problems.Add(label + "：" + axisName + " " + start + ".." + (i - 1)
                        + " 带宽 " + band + "px 不是 " + Unit + " 的整数倍（该带色 " + Hex(axis[start])
                        + "）——色带边界必须落在偶数行。");
                }
                start = i;
            }
            return problems;
        }

        /// <summary>
        /// 锁板判据：画出来的每个不透明像素都必须是该 tone 色阶里的色
        /// （填充另加前缘提亮的三个变体）。这条挡的是"渐变/噪点/抗锯齿会带出板外色"——
        /// 本族一律平涂，出现板外色说明有人往生成器里加了渐变。
        /// </summary>
        static List<string> CheckPaletteLock(string label, BakeTarget t, Texture2D tex)
        {
            var allowed = new List<Color32>();
            if (t.isFill)
            {
                var kind = (FillKind)Enum.Parse(typeof(FillKind), t.name.Substring("Pixel_Fill_".Length));
                Fill f = FillOf(kind);
                allowed.Add(f.Body);
                allowed.Add(f.Dark);
                allowed.Add(f.Hi);
                // 前缘列对三层带各算一次：亮沿→亮沿、主体→Tip、暗沿→mix(暗沿, 亮沿)
                allowed.Add(f.Tip);
                allowed.Add(Mix(f.Dark, f.Hi, Fill.TipMix));
            }
            else
            {
                Ramp r = RampOf(ToneOfName(t.name));
                allowed.Add(r.S1);
                allowed.Add(r.S2);
                allowed.Add(r.S3);
                allowed.Add(r.S4);
                allowed.Add(r.Floor);
            }

            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a == 0)
                    continue;
                bool ok = false;
                for (int k = 0; k < allowed.Count; k++)
                {
                    if (Same(px[i], allowed[k])) { ok = true; break; }
                }
                if (!ok)
                {
                    var names = new List<string>();
                    for (int k = 0; k < allowed.Count; k++)
                        names.Add(Hex(allowed[k]));
                    return new List<string>
                    {
                        label + "：像素 #" + i + " 的色 " + Hex(px[i]) + " 不在本 tone 的色阶里（"
                        + string.Join("/", names.ToArray()) + "）——本族平涂，出现板外色说明画法被改了。",
                    };
                }
            }
            return new List<string>();
        }

        /// <summary>
        /// 相邻两档的**相对亮度比**下限：<c>Lum(暗档) ≤ 0.90 × Lum(亮档)</c>。
        ///
        /// 【这个数从哪来】参照条实测的相邻档亮度比全部落在 <b>0.55~0.74</b>
        /// （<c>#B9973B→#8C6F2A</c> 0.74 / <c>#8C6F2A→#54481E</c> 0.64 / <c>#54481E→#2F261F</c> 0.55）。
        /// 本判据放宽到 0.90，是留给"本工程色相跨度大、档间可以更密"的余量；
        /// 一旦超过 0.90 就说明两档**几乎同亮**——斜面读不出来，等于没做浮雕。
        ///
        /// 【为什么用比值而不是绝对差】亮度感知是相对的（Weber）：
        /// 深色档之间差 10 个单位是肉眼可辨的一大步（Frame 的 <c>#1E252F→#16191D</c> 只差 11.6），
        /// 而浅色档之间差 10 个单位几乎看不出来（Light 曾用 <c>#F0F0F0 / #F5E8C8</c> 差 7.6）。
        /// 绝对差阈值会在深色端误伤、在浅色端放过——比值阈值两边都对。
        /// </summary>
        const float RampStepRatio = 0.90f;

        /// <summary>逐档查"相邻档是否分得开"（<paramref name="shades"/> 必须由亮到暗）。</summary>
        static List<string> CheckSteps(string label, Color32[] shades, string[] names)
        {
            var problems = new List<string>();
            for (int i = 0; i + 1 < shades.Length; i++)
            {
                float hi = Lum(shades[i]);
                float lo = Lum(shades[i + 1]);
                if (hi <= 0f)
                    continue;
                if (lo >= RampStepRatio * hi)
                {
                    problems.Add(label + " 的 " + names[i] + "→" + names[i + 1] + " 两档分不开："
                        + Hex(shades[i]) + "（亮度 " + hi.ToString("F1") + "）→ " + Hex(shades[i + 1])
                        + "（" + lo.ToString("F1") + "），比值 " + (lo / hi).ToString("F2")
                        + " ≥ " + RampStepRatio.ToString("F2")
                        + "——这两档在画面上几乎同亮，斜面会读不出来（换槽位，或把档位重新安排）。");
                }
            }
            return problems;
        }

        static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static string Hex(Color32 c)
        {
            return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2")
                + (c.a == 255 ? "" : " a" + c.a);
        }

        // ==================================================================
        // 十一、接触表（评审出图；落 export/ 暂存目录）
        // ==================================================================

        /// <summary>
        /// 出接触表：每个 tone 一行（最小尺寸块 / 常态块 / 按压块 / 凹槽 / 槽内 60% 填充），
        /// 另加一区 5 种填充条。**这张图是 4b 试点的评审靶子**，也是九宫格语义的自证：
        /// 块被拉到 96px 宽时四角与四条边必须与 32px 时完全一样（只有中心被拉长）。
        /// 底纹是 8px 棋盘（不是为了好看——透明切角只在有底纹的图上读得出来）。
        /// </summary>
        public static string BuildContactSheet()
        {
            const int pad = 8;
            const int gap = 8;
            const int rowTone = 32;
            // 填充演示行取 24：槽 24 - 内边距 3u×2 = 12，正好是填充件的天然高度——
            // 于是这一行**逐像素复现参照条的比例**（6 框架 + 12 填充 + 6 框架 = 24），
            // 评审时能直接拿它跟参照截图片段比高度。
            const int rowFill = 24;
            const int colMin = 16;
            const int colWide = 96;

            int cols = pad + colMin + gap + (colWide + gap) * 4;
            int rows = pad + (rowTone + gap) * 7 + gap + (rowFill + gap) * 5 + pad;

            var px = new Color32[cols * rows];
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    bool odd = ((x / 4) + (y / 4)) % 2 == 1;
                    px[y * cols + x] = odd
                        ? new Color32(0xA8, 0xA8, 0xA8, 255)
                        : new Color32(0x80, 0x80, 0x80, 255);
                }
            }

            // 版面用"从顶往下的游标"排（画布 y=0 在下，故每排一行游标就减）——
            // 用 y0 - r*step 那种反推在块数不一致时必然越界。
            int cursorTop = rows - pad;

            Tone[] tones = (Tone[])Enum.GetValues(typeof(Tone));
            for (int r = 0; r < tones.Length; r++)
            {
                Tone tone = tones[r];
                int y = cursorTop - rowTone;
                cursorTop = y - gap;
                int x = pad;

                // ① 最小可渲染尺寸（16px）：证明 border×2 时四角没被切进内容区
                DrawOne(px, cols, tone, Piece.Plate, State.Normal, x, y + (rowTone - colMin) / 2, colMin, colMin);
                x += colMin + gap;

                // ② 常态块 → ③ 按压块 → ④ 凹槽 → ⑤ 槽内 60% 填充
                DrawOne(px, cols, tone, Piece.Plate, State.Normal, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Plate, State.Pressed, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Track, State.Normal, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Track, State.Normal, x, y, colWide, rowTone);
                DrawTrackFill(px, cols, tone, FillOfTone(tone), x, y, colWide, rowTone, 0.6f);
            }

            // 填充区：5 种填充各自满格画在 Frame 凹槽里（两列宽，读得出前缘在右端）
            FillKind[] fills = (FillKind[])Enum.GetValues(typeof(FillKind));
            for (int r = 0; r < fills.Length; r++)
            {
                int y = cursorTop - rowFill;
                cursorTop = y - gap;
                int x = pad + colMin + gap;
                int w = colWide + gap + colWide;
                DrawOne(px, cols, Tone.Frame, Piece.Track, State.Normal, x, y, w, rowFill);
                int inset = 3 * Unit;
                DrawFill(px, cols, fills[r], x + inset, y + inset, w - inset * 2, rowFill - inset * 2);
            }

            if (cursorTop < pad)
            {
                throw new InvalidOperationException("[BeveledPixelSpriteBuilder] 接触表版面溢出："
                    + "画完后剩余高度 " + cursorTop + " < pad " + pad + "——rows 的算式与行数不匹配。");
            }

            // 整数倍放大（最近邻；像素件放大用插值会糊，也会带出板外色）
            int zw = cols * SheetZoom;
            var zoomed = new Color32[zw * rows * SheetZoom];
            for (int y = 0; y < rows * SheetZoom; y++)
            {
                for (int x = 0; x < zw; x++)
                    zoomed[y * zw + x] = px[(y / SheetZoom) * cols + (x / SheetZoom)];
            }

            Texture2D sheet = ToTexture(zoomed, zw, rows * SheetZoom);
            string abs = Path.Combine(RepoRoot(), SheetRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            return SheetRelativePath;
        }

        /// <summary>tone → 该 tone 的槽内填充色（海图配海蓝、危险配红、其余配暖橙/中性/蓝）。</summary>
        static FillKind FillOfTone(Tone tone)
        {
            switch (tone)
            {
                case Tone.Sea: return FillKind.Sea;
                case Tone.Danger: return FillKind.Red;
                case Tone.Primary:
                case Tone.Warn: return FillKind.Warn;
                case Tone.Light: return FillKind.Neutral;
                default: return FillKind.Blue;
            }
        }

        static void DrawOne(Color32[] canvas, int canvasW, Tone tone, Piece piece, State state,
            int ox, int oy, int w, int h)
        {
            Texture2D tex = CreateTexture(tone, piece, state, out Vector4 border);
            Color32[] src = tex.GetPixels32();
            DrawNineSliced(src, PlateSize, border, canvas, canvasW, ox, oy, w, h);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>填充画在凹槽的**内容区**里：3 层带 = 3u 的边距，宽度按比例。</summary>
        static void DrawTrackFill(Color32[] canvas, int canvasW, Tone tone, FillKind kind,
            int ox, int oy, int w, int h, float ratio)
        {
            int inset = 3 * Unit;
            int innerW = w - inset * 2;
            DrawFill(canvas, canvasW, kind, ox + inset, oy + inset,
                Mathf.RoundToInt(innerW * Mathf.Clamp01(ratio)), h - inset * 2);
        }

        static void DrawFill(Color32[] canvas, int canvasW, FillKind kind, int ox, int oy, int w, int h)
        {
            if (w <= 0 || h <= 0)
                return;
            Texture2D tex = CreateFillTexture(kind, out Vector4 border);
            Color32[] src = tex.GetPixels32();
            DrawNineSliced(src, FillSize, border, canvas, canvasW, ox, oy, w, h);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ==================================================================
        // 十二、落盘 + Sprite 导入
        // ==================================================================

        static void WriteSprite(string assetPath, Texture2D texture, Vector4 border)
        {
            EnsureFolder(PixelArtTextureRules.UiSpriteFolder);
            try
            {
                byte[] png = texture.EncodeToPNG();
                string abs = Path.Combine(UnityProjectRoot(), assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                File.WriteAllBytes(abs, png);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            PixelArtTextureRules.ApplySprite(AssetImporter.GetAtPath(assetPath) as TextureImporter, border);
        }

        /// <summary>
        /// 生成 → 落盘 → 九宫格导入。落盘失败时退回内存 Sprite（外观一致但不持久化），
        /// 绝不静默返回 null（否则调用方拿到白块，且"看着有 UI、其实没材质"）。
        /// </summary>
        static Sprite Generate(string assetPath, Texture2D texture, Vector4 border)
        {
            try
            {
                WriteSprite(assetPath, texture, border);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                    return sprite;
                Debug.LogWarning("[BeveledPixelSpriteBuilder] 盘上读不到 Sprite：" + assetPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BeveledPixelSpriteBuilder] 生成异常（" + assetPath + "）：" + e.Message
                    + "\n  退回内存 Sprite（外观一致，但不随场景持久化）。");
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }

            Texture2D fallbackTexture = CreateByName(Path.GetFileNameWithoutExtension(assetPath), out Vector4 b);
            var fallback = Sprite.Create(fallbackTexture,
                new Rect(0f, 0f, fallbackTexture.width, fallbackTexture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, b);
            fallback.name = Path.GetFileNameWithoutExtension(assetPath);
            return fallback;
        }

        static void EnsureFolder(string path)
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
