using System;
using System.Collections.Generic;
using System.IO;
using PirateCrew.EditorTools.Art;
using UnityEditor;
using UnityEngine;
using Tone = PirateCrew.UI.PixelTone;
using Piece = PirateCrew.UI.PixelPiece;
using State = PirateCrew.UI.PixelState;
using FillKind = PirateCrew.UI.PixelFillKind;

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
    /// 【几何从哪来：量出来的，不是想出来的】几何关系（基本单位 u / 外环-斜面-内暗线 三层 /
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
        /// 悬停 = **整条色阶上抬一档**（<see cref="HoverLift"/>）：S1~S3 与 Floor 各向亮侧邻档混
        /// 25%，最亮的 S4（受光外环）不动——轮廓保持锐利、内部"点亮"。
        /// 这是「悬停 = 换一档明暗的整套贴图」的派生实现：不用多造 tone，也不出板
        /// （抬完的 5 档自成一个锁板集合）。
        ///
        /// 【三b、语义件（Singles）】除 Plate/Track/Fill 三族外，UI 还需要几件**只有一种画法**的
        /// 小件：选人圈（战场选中标记）/ 键盘焦点框 / 位点（页点·队伍槽）/ 蚀刻分隔线 / 面板投影 /
        /// 页签（底边无带、与宿主面板贴合）。它们不走 tone 表，各自固定一组槽位色，
        /// 画法与判据成对出现（各 Create* 与 <see cref="Targets"/>）。
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
    /// <c>≥ 2 × border</c>（<see cref="PlateMinRender"/> = 24px），装配侧低于这个尺寸就会
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
        // 一、几何常量（单位 u = 基本像素；改这些数字前先读类头 §一）
        // ==================================================================

        /// <summary>
        /// 基本单位（像素）。**u = 3 = 1080p 下一个 3D 像素块**（PixelationRendererFeature 的
        /// 640×360 RT 最近邻放大回 1920×1080，1080÷360=3）——UI 颗粒度与 3D 渲染 1:1 对齐
        /// （创始人 2026-09-22 要求）。真源在运行时 <c>PixelSkin.Unit</c>，判据
        /// <see cref="CheckUnitAlignment"/> 会读 URP 渲染器资产里的 renderHeightPixels 反向锁这条。
        /// </summary>
        public const int Unit = PirateCrew.UI.PixelSkin.Unit;

        /// <summary>Plate / Track 贴图边长（px）。只影响"最小可渲染尺寸"，不影响拉伸后的外观。</summary>
        public const int PlateSize = 36;

        /// <summary>Plate / Track 的九宫格切片边框（px）= 3 层带（u×3）+ u 的内容余量。</summary>
        public const int PlateBorder = 4 * Unit;

        /// <summary>低于这个尺寸装配就不能用九宫格（角会切进内容区）。装配侧应断言。</summary>
        public const int PlateMinRender = PlateBorder * 2;

        /// <summary>
        /// 切角深度（**单位数**，不是像素数；1 单位 = <see cref="Unit"/> px）。
        /// 1 = 每个角切掉**一个 2×2 的整格**（默认）；2 = 切两格（4px → 2px 阶梯）；0 = 方角。
        ///
        /// 【为什么默认 1】两处实测摆在一起看就清楚了：
        ///   · 参照的**面板**角（成就内板）切掉的是**一个格子**（1px 体系里 1 格）→ 换算到 2px 体系 = 1 格 = 2px；
        ///   · 参照的**条状件端头**是 4px → 2px → 0 的阶梯（= 2 格），但那是"条的斜切端"，不是面板角。
        /// 面板角取前者。试过 2 格：整块（12u 见方）看起来开始"变圆"，与 chunky 的取向相抵。
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
            // 拷进局部量再比较：直接比 const 会被编译器折叠成恒真/恒假，给守卫报 CS0162
            // （"改成 0 = 全族方角"的用法要在，不能删守卫）。
            int depth = ChamferDepthUnits;
            if (depth <= 0)
                return false;
            int dx = Math.Min(x, n - 1 - x) / Unit;
            int dy = Math.Min(y, n - 1 - y) / Unit;
            return dx + dy < depth;
        }

        /// <summary>切角像素总数（4 个角 × <c>深度×(深度+1)/2</c> 个格 × 每格 <c>Unit²</c> 像素）。</summary>
        public static int ChamferPixelCount()
        {
            int d = ChamferDepthUnits;
            return d <= 0 ? 0 : 4 * (d * (d + 1) / 2) * Unit * Unit;
        }

        /// <summary>Fill 贴图边长（px）：上 u 亮 / 中 4u 主体 / 下 u 暗（与实测 <c>HP_Fill.png</c> 逐段同构）。</summary>
        public const int FillSize = 6 * Unit;

        /// <summary>Fill 的上下切片边框 = u（亮/暗带各一层）；右端另有 u 宽的"前缘"列。</summary>
        public const int FillBorder = Unit;

        /// <summary>Fill 的最低可渲染高度（上下两层带）。低于它带会被压没。</summary>
        public const int FillMinRender = FillBorder * 2;

        /// <summary>
        /// 按压态的元素位移（UI 像素）：右下 1px。**装配侧用这个常量**（真源 = 运行时
        /// <c>PixelSkin.PressOffset</c>），别再各写各的（贴图里不烘位移，烘了九宫格切片就错位）。
        /// </summary>
        public static readonly Vector2 PressOffset = PirateCrew.UI.PixelSkin.PressOffset;

        /// <summary>悬停态的色阶上抬比例：每档向亮侧邻档混这个比例（S4 顶档除外，见类头 §三）。</summary>
        public const float HoverLift = 0.25f;

        /// <summary>选人圈 / 焦点框边长（px）= 8u。都是 2u 厚的方环，带与 Plate 同款的切角。</summary>
        public const int RingSize = 8 * Unit;

        /// <summary>选人圈 / 焦点框的九宫格切片边框 = 环厚（2u：外 u 亮沿 + 内 u 主体）。</summary>
        public const int RingBorder = 2 * Unit;

        /// <summary>位点（页点 / 队伍槽指示）边长（px）= 6u。菱形——切角语言的 45° 对角线推到底。</summary>
        public const int PipSize = 6 * Unit;

        /// <summary>分隔线长度轴尺寸（px）；粗细恒 2u（蚀刻槽线：暗 u 压亮 u）。</summary>
        public const int SepLength = 4 * Unit;

        /// <summary>
        /// 面板投影相对面板本体的偏移（UI 像素）：右下 1u。装配侧把 <c>Pixel_Shadow</c>
        /// 垫在面板下、按这个常量错位——硬边无渐变的错位剪影是像素 UI 表达层次的标准件。
        /// （真源 = 运行时 <c>PixelSkin.ShadowOffset</c>。）
        /// </summary>
        public static readonly Vector2 ShadowOffset = PirateCrew.UI.PixelSkin.ShadowOffset;

        /// <summary>美术稿（showcase）：把全部件拼成一块战斗 HUD 风格的构图，落 export/ 评审。</summary>
        const string ShowcaseRelativePath = "export/ui-pixel-4a/showcase-2x.png";

        /// <summary>运行时图集资产路径（Resources 内；BuildAll 末尾生成，运行时 PixelSkin 经它取图）。</summary>
        public const string AtlasAssetPath = "Assets/Resources/UI/PixelSkin.asset";

        /// <summary>产物目录（与玻璃族分开，4b 试点后玻璃族逐步退役）。</summary>
        const string SpriteFolder = PixelArtTextureRules.UiSpriteFolder;

        /// <summary>接触表（评审用，落 <c>export/</c> 暂存目录，不入库）。</summary>
        const string SheetRelativePath = "export/ui-pixel-4a/contact-sheet-2x.png";

        /// <summary>接触表的整数放大倍率（2px 带在 1:1 下太小，评审需要放大看；整数倍才不糊）。</summary>
        const int SheetZoom = 2;

        // ==================================================================
        // 二、枚举与烘焙目标
        // ==================================================================

        // tone / piece / state / fill 的枚举定义在运行时 PirateCrew.UI（PixelSkin.cs）——
        // 一处定义、两侧同型，图集资产的下标算式（tone×3+state 等）才不会出现两套真相。
        // 文件顶部的 using 别名让本文件继续写短名（Tone/Piece/State/FillKind）。

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
            /// <summary>
            /// 画法族（plate / tab / fill / ring / pip / sep / shadow）——判据按它分流
            /// （期望透明数、是否查外环闭合、锁板色表）。名→族的推导见 <see cref="KindOfName"/>。
            /// </summary>
            public string kind;
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

            /// <summary>
            /// 整条阶梯向亮侧抬一档（悬停态）：S1~S3 与 Floor 各与**亮侧邻档**混 t，
            /// S4 已是顶不再抬——受光外环不动，轮廓保持锐利、内部"点亮"。
            /// 抬完每档都夹在原值与亮邻之间，单调性保持；但相邻档差缩小到 <c>(1-t)</c>，
            /// 所以悬停阶梯只查单调、不再查档距（档距判据服务的是**常态**浮雕可读性，见 Verify）。
            /// </summary>
            public Ramp Lifted(float t)
            {
                return new Ramp
                {
                    S4 = S4,
                    S3 = Mix(S3, S4, t),
                    S2 = Mix(S2, S3, t),
                    S1 = Mix(S1, S2, t),
                    Floor = Mix(Floor, S1, t),
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
            // 悬停 = 整条色阶上抬一档（S4 不动）；按压 = 高光/阴影对调。两条互斥、
            // 都不改变分层几何——所以先定阶梯、再画同心环，环的画法对状态无感知。
            Ramp r = RampOf(tone);
            if (state == State.Hovered)
                r = r.Lifted(HoverLift);

            Edge lit = Edge.Lit(r);
            Edge shade = Edge.Shade(r);
            Edge top = state == State.Pressed ? shade : lit;
            Edge bottom = state == State.Pressed ? lit : shade;
            Color32 body = piece == Piece.Track ? r.Floor : r.S3;

            // 页签（Tab）：底边无带——到下边界的距离不参与分层（dy 只算上边界），
            // 于是左右带一路通到底、底边是平底（border 下=0），与宿主面板顶边贴合。
            bool tab = piece == Piece.Tab;
            bool track = piece == Piece.Track;

            int n = PlateSize;
            var px = new Color32[n * n];

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // 页签只切上两角（下两角是方角，贴面板的那条边要直）。
                    if (tab ? IsChamferTop(x, y, n) : IsChamfer(x, y, n))
                    {
                        // 切角：显式写全零（不是"没画"）——alphaIsTransparency 关掉后
                        // Unity 不会把 RGB 膨胀进这里，切角边缘因此不会带出板外色。
                        px[y * n + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    int dx = Math.Min(x, n - 1 - x);
                    int dy = tab ? n - 1 - y : Math.Min(y, n - 1 - y);
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

            // 黄铜铆钉：只钉在 Frame 凸起块的四角（标题条/边框的角接缝上——真实铆钉的位置）。
            // 1u 见方，左上像素提白、右下压木暗：把"光来自左上"缩进 2×2…u×u 的一格里讲完。
            // 别的 tone 不钉（内容片/按钮满屏都是，钉了就是噪点）。
            if (tone == Tone.Frame && piece == Piece.Plate)
                DrawStuds(px, n);

            border = tab
                ? new Vector4(PlateBorder, 0f, PlateBorder, PlateBorder)
                : new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder);
            return ToTexture(px, n, n);
        }

        /// <summary>页签的切角判据：只对上两角生效（dy 只算到上边界的距离）。</summary>
        static bool IsChamferTop(int x, int y, int n)
        {
            int depth = ChamferDepthUnits;      // 局部量防 CS0162（同 IsChamfer）
            if (depth <= 0)
                return false;
            int dx = Math.Min(x, n - 1 - x) / Unit;
            int dy = (n - 1 - y) / Unit;
            return dx + dy < depth;
        }

        /// <summary>铆钉四枚：贴角 1u 处的 u×u 色块（Frame Plate 专用）。</summary>
        static void DrawStuds(Color32[] px, int n)
        {
            Color32[] stud = StudColors();   // [0] 主体 / [1] 亮 / [2] 暗
            int o = Unit;
            int[] xs = { o, n - Unit - o };
            int[] ys = { o, n - Unit - o };
            for (int sx = 0; sx < 2; sx++)
            {
                for (int sy = 0; sy < 2; sy++)
                {
                    int bx = xs[sx], by = ys[sy];
                    for (int j = 0; j < Unit; j++)
                    {
                        for (int i = 0; i < Unit; i++)
                        {
                            int xx = bx + i, yy = by + j;
                            // 贴画面左上的那 1px = 亮、贴右下的那 1px = 暗、其余 = 主体
                            int leftness = sx == 0 ? i : Unit - 1 - i;
                            int topness = sy == 0 ? Unit - 1 - j : j;   // y 向上：下角原点的顶是 j 大的一侧
                            int score = leftness + topness;
                            px[yy * n + xx] = score == 2 * (Unit - 1) ? stud[1]
                                : score == 0 ? stud[2]
                                : stud[0];
                        }
                    }
                }
            }
        }

        /// <summary>铆钉三色：BRASS 主体 + 提白亮档 + 木暗暗档（锁板判据复用同一张表）。</summary>
        public static Color32[] StudColors()
        {
            return new[]
            {
                Slot("BRASS"),
                Mix(Slot("BRASS"), Slot("WHITE_HOT"), 0.45f),
                Mix(Slot("BRASS"), Slot("WOOD_DARK"), 0.35f),
            };
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

        // ==================================================================
        // 六b、语义件（Singles）：色表与画法成对，锁板判据直接复用色表
        // ==================================================================

        /// <summary>选人圈两色（外 1px / 内 1u-1px）：暖金往外提白一档——能量从边缘发出来的读法。</summary>
        public static Color32[] RingColors()
        {
            Color32 glow = Slot("GLOW_WARM");
            return new[] { Mix(glow, Slot("WHITE_HOT"), 0.30f), glow };
        }

        /// <summary>焦点框两色：海蓝外缘、内缘提白——键盘焦点的"电气"读法，与选人圈的暖金区分。</summary>
        public static Color32[] FocusColors()
        {
            Color32 blue = Slot("HERO_BLUE");
            return new[] { blue, Mix(blue, Slot("WHITE_HOT"), 0.45f) };
        }

        /// <summary>
        /// 方环（选人圈 / 焦点框共用画法）：**2u 厚**、带同款切角——外 u 一色（亮沿）、
        /// 内 u 一色（主体）。为什么 2u 而不是 1u：环要在"外缘亮、内缘主色"的双档读法下
        /// 仍落在 u 网格上，厚度只能取 2u（1u 厚里切 1px 亮沿 = 离格带，判据必拦）。
        /// 九宫格 border = 环厚（2u）：拉伸只把环拉大，厚度永远 2u。
        /// </summary>
        public static Texture2D CreateRingTexture(Color32 outer, Color32 inner, out Vector4 border)
        {
            int n = RingSize;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (IsChamfer(x, y, n))
                    {
                        px[y * n + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    int d = Math.Min(Math.Min(x, n - 1 - x), Math.Min(y, n - 1 - y));
                    if (d >= 2 * Unit)
                    {
                        px[y * n + x] = new Color32(0, 0, 0, 0);   // 环内是透的（垫在目标物外沿）
                        continue;
                    }
                    px[y * n + x] = d < Unit ? outer : inner;
                }
            }
            border = new Vector4(RingBorder, RingBorder, RingBorder, RingBorder);
            return ToTexture(px, n, n);
        }

        /// <summary>
        /// 位点：PipSize 里的 u 格菱形（<c>|2cx-(格数-1)| + |2cy-(格数-1)| ≤ 格数-2</c> 的格）。
        /// on 态按"光来自左上"分三档（<c>cx-cy</c>：左上边缘亮 / 对角线中 / 其余暗），
        /// 顶端再点 1px 白高光——一颗朝左上发光的小宝石；
        /// off 态是**中心对称**的空插座（均色暗菱 + 中心 2×2 格压深）——满/空一眼可辨。
        /// 菱形不是外星形状：切角语言的 45° 对角线推到底就是它。
        /// </summary>
        public static Texture2D CreatePipTexture(bool on, out Vector4 border)
        {
            int n = PipSize;
            int cells = n / Unit;
            Color32 hi = Slot("SAND_LIGHT");
            Color32 mid = Slot("BRASS");
            Color32 lo = Slot("WOOD_DARK");
            Color32 offBase = Slot("UI_BEVEL_LO");
            Color32 offSocket = PipSocketColor();
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int cx = x / Unit, cy = y / Unit;
                    int a = Math.Abs(2 * cx - (cells - 1)) + Math.Abs(2 * cy - (cells - 1));
                    if (a > cells - 2)
                    {
                        px[y * n + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    if (!on)
                    {
                        bool core = (cx == cells / 2 - 1 || cx == cells / 2) && (cy == cells / 2 - 1 || cy == cells / 2);
                        px[y * n + x] = core ? offSocket : offBase;
                        continue;
                    }
                    int diag = cx - cy;      // y 向上：小 = 左上侧
                    px[y * n + x] = diag <= -2 ? hi : diag == -1 ? mid : lo;
                }
            }
            // on 态：顶格（菱形最高点）左上角点 1px 白——整颗位点的"眼神光"
            if (on)
            {
                int topCellX = (cells - 1) / 2;        // 顶行（cy 最大）居中偏左的格（受光侧）
                int topCellY = cells - 2;
                px[(topCellY * Unit + Unit - 1) * n + topCellX * Unit] = Slot("WHITE_HOT");
            }
            border = Vector4.zero;       // 固定尺寸件（原生渲染，不拉伸）
            return ToTexture(px, n, n);
        }

        /// <summary>off 位点的插座底色（暗档往墨压一步）——与画法成对，锁板判据复用。</summary>
        public static Color32 PipSocketColor()
        {
            return Mix(Slot("UI_BEVEL_LO"), Slot("INK"), 0.35f);
        }

        /// <summary>蚀刻分隔线两色：暗（上/左）压亮（下/右）——凹槽读法，与面板的凸浮雕相反。</summary>
        public static Color32[] SepColors()
        {
            return new[]
            {
                Mix(Slot("UI_BEVEL_LO"), Slot("INK"), 0.35f),
                Mix(Slot("UI_PANEL"), Slot("WHITE_HOT"), 0.22f),
            };
        }

        /// <summary>
        /// 蚀刻分隔线：暗 u + 亮 u（暗贴上/左、亮贴下/右）。长度轴拉伸（border=0）、粗细轴不拉伸（border=u）。
        /// 为什么是"凹"而不是"凸"：分隔线活在凸面板内部，要读成"刻进去的线"——
        /// 与面板的受光方向正好互补，这是同一套光模型的一体两面。
        /// </summary>
        public static Texture2D CreateSepTexture(bool horizontal, out Vector4 border)
        {
            Color32[] c = SepColors();
            int w = horizontal ? SepLength : 2 * Unit;
            int h = horizontal ? 2 * Unit : SepLength;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool dark = horizontal ? y >= Unit : x < Unit;
                    px[y * w + x] = dark ? c[0] : c[1];
                }
            }
            border = horizontal
                ? new Vector4(0f, Unit, 0f, Unit)
                : new Vector4(Unit, 0f, Unit, 0f);
            return ToTexture(px, w, h);
        }

        /// <summary>
        /// 面板投影：Plate 同款轮廓（含切角）的纯 INK 剪影。装配侧垫在面板下、按
        /// <see cref="ShadowOffset"/> 右下错 1u——硬边无渐变的错位剪影是像素 UI 表达层次的标准件。
        /// 只有 INK 一色（锁板判据据此只放行 INK）。
        /// </summary>
        public static Texture2D CreateShadowTexture(out Vector4 border)
        {
            int n = PlateSize;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    px[y * n + x] = IsChamfer(x, y, n)
                        ? new Color32(0, 0, 0, 0)
                        : Slot("INK");
                }
            }
            border = new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder);
            return ToTexture(px, n, n);
        }

        /// <summary>
        /// 按九宫格语义把一张贴图画进画布矩形（源宽高分开给——分隔线是 4u×2u 的长条，
        /// 不是正方形；纵向按源宽去取行会越界）。
        /// 接触表用它**自证切片语义成立**——"读 PNG 色带"只证明画对了，
        /// "拉长后角与边不变形"才证明九宫格可用，而那个只有真画一遍才知道。
        /// 坐标一律 <c>y=0 在下</c>（与 <c>Texture2D.SetPixels32</c> 同口径）。
        /// </summary>
        public static void DrawNineSliced(Color32[] src, int srcW, int srcH, Vector4 border,
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
                int sy = MapAxis(j, h, srcH, bB, bT);
                for (int i = 0; i < w; i++)
                {
                    int sx = MapAxis(i, w, srcW, bL, bR);
                    dst[(oy + j) * dstW + ox + i] = src[sy * srcW + sx];
                }
            }
        }

        /// <summary>
        /// 一维九宫格映射：低端不伸缩（逐像素取源）→ 中心区**均匀最近邻拉伸**（与 UGUI 同口径）
        /// → 高端不伸缩。边框为 0 的一侧自然退化成"中心区占满整段"。
        ///
        /// 【为什么中心是均匀拉伸而不是坍缩到切片起点那一像素】border 全 0 的件（位点）
        /// 在坍缩写法下会把整张图压成第一列——位点画到 2× 尺寸时就是一根竖条。UGUI 的
        /// 九宫格语义本来就是"源中心区整体缩放到目标中心区"，这里对齐引擎行为。
        /// </summary>
        static int MapAxis(int i, int dstLen, int srcLen, int bLo, int bHi)
        {
            if (i < bLo)
                return i;
            int hiStart = dstLen - bHi;
            if (i >= hiStart)
                return srcLen - bHi + (i - hiStart);
            int srcCenter = srcLen - bLo - bHi;
            int dstCenter = dstLen - bLo - bHi;
            return bLo + (int)((long)(i - bLo) * srcCenter / dstCenter);
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

        /// <summary>资产名（<c>Pixel_&lt;Piece&gt;_&lt;Tone&gt;[_Hover|_Pressed].png</c>）。</summary>
        public static string AssetNameOf(Tone tone, Piece piece, State state)
        {
            return "Pixel_" + piece + "_" + tone + StateSuffix(state);
        }

        static string StateSuffix(State state)
        {
            switch (state)
            {
                case State.Pressed: return "_Pressed";
                case State.Hovered: return "_Hover";
                default: return "";
            }
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

        /// <summary>页签 Sprite（底边无带；未选中用 Dense、选中用宿主内容 tone）。</summary>
        public static Sprite GetTab(Tone tone)
        {
            return Get(tone, Piece.Tab, State.Normal);
        }

        /// <summary>选人圈 Sprite（暖金方环）。</summary>
        public static Sprite GetRing()
        {
            return GetOrBake("Pixel_Ring", path =>
            {
                Color32[] c = RingColors();
                Texture2D texture = CreateRingTexture(c[0], c[1], out Vector4 border);
                return Generate(path, texture, border);
            });
        }

        /// <summary>键盘焦点框 Sprite（蓝白方环）。</summary>
        public static Sprite GetFocus()
        {
            return GetOrBake("Pixel_Focus", path =>
            {
                Color32[] c = FocusColors();
                Texture2D texture = CreateRingTexture(c[0], c[1], out Vector4 border);
                return Generate(path, texture, border);
            });
        }

        /// <summary>位点 Sprite（on=黄铜宝石 / off=中性暗）。</summary>
        public static Sprite GetPip(bool on)
        {
            return GetOrBake(on ? "Pixel_Pip_On" : "Pixel_Pip_Off", path =>
            {
                Texture2D texture = CreatePipTexture(on, out Vector4 border);
                return Generate(path, texture, border);
            });
        }

        /// <summary>蚀刻分隔线 Sprite。</summary>
        public static Sprite GetSeparator(bool horizontal)
        {
            return GetOrBake(horizontal ? "Pixel_Sep_H" : "Pixel_Sep_V", path =>
            {
                Texture2D texture = CreateSepTexture(horizontal, out Vector4 border);
                return Generate(path, texture, border);
            });
        }

        /// <summary>面板投影 Sprite（INK 剪影）。</summary>
        public static Sprite GetShadow()
        {
            return GetOrBake("Pixel_Shadow", path =>
            {
                Texture2D texture = CreateShadowTexture(out Vector4 border);
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
        /// **有意不是"全枚举直积"**：凹槽没有按压/悬停态（槽不会更凹、也点不亮）、
        /// 页签没有状态（"选中"是换 tone 不是换状态）、填充没有状态（状态属于容器不属于内容）。
        /// 悬停全 7 tone 都烘：图集是 tone×state 整网格，缺一格运行时下标就对不上。
        /// </summary>
        public static BakeTarget[] Targets()
        {
            var list = new List<BakeTarget>();

            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                list.Add(NewTarget(AssetNameOf(tone, Piece.Plate, State.Normal)));
                list.Add(NewTarget(AssetNameOf(tone, Piece.Plate, State.Hovered)));
                list.Add(NewTarget(AssetNameOf(tone, Piece.Plate, State.Pressed)));
                list.Add(NewTarget(AssetNameOf(tone, Piece.Track, State.Normal)));
            }

            foreach (FillKind kind in Enum.GetValues(typeof(FillKind)))
                list.Add(NewTarget(AssetNameOf(kind)));

            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
                list.Add(NewTarget(AssetNameOf(tone, Piece.Tab, State.Normal)));

            list.Add(NewTarget("Pixel_Ring"));
            list.Add(NewTarget("Pixel_Focus"));
            list.Add(NewTarget("Pixel_Pip_On"));
            list.Add(NewTarget("Pixel_Pip_Off"));
            list.Add(NewTarget("Pixel_Sep_H"));
            list.Add(NewTarget("Pixel_Sep_V"));
            list.Add(NewTarget("Pixel_Shadow"));

            return list.ToArray();
        }

        /// <summary>资产名 → 画法族（判据分流用；与 <see cref="CreateByName"/> 的路由一一对应）。</summary>
        public static string KindOfName(string name)
        {
            if (name.StartsWith("Pixel_Fill_", StringComparison.Ordinal))
                return "fill";
            if (name.StartsWith("Pixel_Tab_", StringComparison.Ordinal))
                return "tab";
            if (name == "Pixel_Ring" || name == "Pixel_Focus")
                return "ring";
            if (name.StartsWith("Pixel_Pip_", StringComparison.Ordinal))
                return "pip";
            if (name.StartsWith("Pixel_Sep_", StringComparison.Ordinal))
                return "sep";
            if (name == "Pixel_Shadow")
                return "shadow";
            return "plate";
        }

        static BakeTarget NewTarget(string name)
        {
            string kind = KindOfName(name);
            bool isFill = kind == "fill";
            int w, h;
            Vector4 border;
            switch (kind)
            {
                case "fill":
                    w = FillSize; h = FillSize;
                    border = new Vector4(0f, FillBorder, FillBorder, FillBorder);
                    break;
                case "tab":
                    w = PlateSize; h = PlateSize;
                    border = new Vector4(PlateBorder, 0f, PlateBorder, PlateBorder);
                    break;
                case "ring":
                    w = RingSize; h = RingSize;
                    border = new Vector4(RingBorder, RingBorder, RingBorder, RingBorder);
                    break;
                case "pip":
                    w = PipSize; h = PipSize;
                    border = Vector4.zero;
                    break;
                case "sep":
                    w = SepLength; h = 2 * Unit;      // 水平件；垂直件在 CreateByName 里互换
                    border = new Vector4(0f, Unit, 0f, Unit);
                    break;
                case "shadow":
                    w = PlateSize; h = PlateSize;
                    border = new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder);
                    break;
                default:
                    w = PlateSize; h = PlateSize;
                    border = new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder);
                    break;
            }
            if (kind == "sep" && name == "Pixel_Sep_V")
            {
                w = 2 * Unit; h = SepLength;
                border = new Vector4(Unit, 0f, Unit, 0f);
            }
            return new BakeTarget
            {
                name = name,
                assetPath = SpriteFolder + "/" + name + ".png",
                width = w,
                height = h,
                border = border,
                isFill = isFill,
                kind = kind,
            };
        }

        /// <summary>把目标名字解析回画法（<see cref="Targets"/> 的逆）。</summary>
        static Texture2D CreateByName(string name, out Vector4 border)
        {
            if (name == "Pixel_Ring")
            {
                Color32[] c = RingColors();
                return CreateRingTexture(c[0], c[1], out border);
            }
            if (name == "Pixel_Focus")
            {
                Color32[] c = FocusColors();
                return CreateRingTexture(c[0], c[1], out border);
            }
            if (name == "Pixel_Pip_On" || name == "Pixel_Pip_Off")
                return CreatePipTexture(name == "Pixel_Pip_On", out border);
            if (name == "Pixel_Sep_H" || name == "Pixel_Sep_V")
                return CreateSepTexture(name == "Pixel_Sep_H", out border);
            if (name == "Pixel_Shadow")
                return CreateShadowTexture(out border);

            if (name.StartsWith("Pixel_Fill_", StringComparison.Ordinal))
            {
                var kind = (FillKind)Enum.Parse(typeof(FillKind), name.Substring("Pixel_Fill_".Length));
                return CreateFillTexture(kind, out border);
            }

            string rest = name.Substring("Pixel_".Length);
            int cut = rest.IndexOf('_');
            var piece = (Piece)Enum.Parse(typeof(Piece), rest.Substring(0, cut));
            State state = ParseStateTail(rest.Substring(cut + 1));
            return CreateTexture(ToneOfName(name), piece, state, out border);
        }

        /// <summary>从资产名解析 tone（<c>Pixel_Plate_Frame[_Hover|_Pressed]</c> → <c>Tone.Frame</c>）。</summary>
        static Tone ToneOfName(string name)
        {
            string rest = name.Substring("Pixel_".Length);
            int cut = rest.IndexOf('_');
            return (Tone)Enum.Parse(typeof(Tone), ParseToneTail(rest.Substring(cut + 1)));
        }

        /// <summary>从目标名字解析状态后缀。</summary>
        static State ParseStateTail(string tail)
        {
            if (tail.EndsWith("_Pressed", StringComparison.Ordinal))
                return State.Pressed;
            if (tail.EndsWith("_Hover", StringComparison.Ordinal))
                return State.Hovered;
            return State.Normal;
        }

        static string ParseToneTail(string tail)
        {
            return tail.EndsWith("_Pressed", StringComparison.Ordinal)
                ? tail.Substring(0, tail.Length - "_Pressed".Length)
                : tail.EndsWith("_Hover", StringComparison.Ordinal)
                    ? tail.Substring(0, tail.Length - "_Hover".Length)
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
            GenerateAtlas();                        // 图集资产要在贴图导入之后生成

            List<string> problems = Verify();
            string sheet = BuildContactSheet();
            string showcase = BuildShowcaseSheet();

            Debug.Log("[BeveledPixelSpriteBuilder] Beveled Pixel 重烘焙完成，共 " + targets.Length
                + " 张：" + SpriteFolder + "（接触表 " + sheet + "；美术稿 " + showcase
                + "；图集 " + AtlasAssetPath + "）");
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
                // 打全堆栈：只打 Message 会把真出错的位置吞掉（无头排查只能靠日志）。
                Debug.LogError("[BeveledPixelSpriteBuilder] 命令行烘焙失败：" + e);
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
                foreach (Tone tone in Enum.GetValues(typeof(Tone)))
                {
                    // 悬停阶梯只查两件事：仍单调（档序不乱）、抬升可见（body 与常态确实不同色）。
                    // 档距判据（CheckSteps）不适用——抬升的定义就是把档距压到 (1-HoverLift)，
                    // 那 25% 的牺牲换"整块点亮"的读法，是设计而不是缺陷。
                    Ramp normal = RampOf(tone);
                    Ramp hover = normal.Lifted(HoverLift);
                    if (!(Lum(hover.S1) < Lum(hover.S2) && Lum(hover.S2) < Lum(hover.S3) && Lum(hover.S3) < Lum(hover.S4)))
                        problems.Add("tone " + tone + " 的悬停色阶不单调（Lifted 把档序搞乱了）。");
                    if (Same(hover.S3, normal.S3))
                        problems.Add("tone " + tone + " 的悬停底色与常态相同（抬升不可见，HoverLift 失效？）。");
                }
            }

            BakeTarget[] targets = Targets();
            for (int i = 0; i < targets.Length; i++)
                problems.AddRange(VerifyTarget(targets[i]));

            CheckGeometryConstants(problems, PlateSize, PlateBorder, FillSize, FillBorder, Unit);
            CheckUnitAlignment(problems);
            VerifyAtlas(problems);

            return problems;
        }

        /// <summary>
        /// **UI 颗粒度 ↔ 3D 像素块对齐判据**（创始人 2026-09-22 要求）：读两档 URP 渲染器资产里
        /// PixelationRendererFeature 的 <c>renderHeightPixels</c>，要求
        /// <c>Unit == 1080 / renderHeightPixels</c>（1080p 基准下一个 3D 像素块的屏幕像素数）。
        /// 改 RT 档而不同步改 Unit（或反之），四四方方的 UI 带就会和 3D 的块错半格——
        /// 这条判据把"对齐"从口头约定变成可复算的数字。
        /// </summary>
        static void CheckUnitAlignment(List<string> problems)
        {
            const int canonicalHeight = 1080;      // 基准出图分辨率（Canvas 参考分辨率同为 1920×1080）
            string[] rendererAssets =
            {
                "Assets/Settings/URP/PC_Balanced_Renderer.asset",
                "Assets/Settings/URP/PC_Performant_Renderer.asset",
            };
            var heights = new List<int>();
            foreach (string path in rendererAssets)
            {
                string abs = Path.Combine(UnityProjectRoot(), path);
                if (!File.Exists(abs))
                {
                    problems.Add("u 对齐判据：找不到渲染器资产 " + path + "。");
                    continue;
                }
                foreach (string line in File.ReadAllLines(abs))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("renderHeightPixels:", StringComparison.Ordinal))
                        continue;
                    int value;
                    if (int.TryParse(trimmed.Substring("renderHeightPixels:".Length).Trim(), out value))
                        heights.Add(value);
                    break;
                }
            }
            if (heights.Count == 0)
            {
                problems.Add("u 对齐判据：两档渲染器资产里都没读到 renderHeightPixels"
                    + "（像素化 Feature 未挂载或字段改名）——UI 与 3D 的颗粒度对齐无从校验。");
                return;
            }
            for (int i = 0; i < heights.Count; i++)
            {
                if (heights[i] <= 0 || canonicalHeight % heights[i] != 0)
                {
                    problems.Add("u 对齐判据：renderHeightPixels=" + heights[i]
                        + " 不能整除基准高度 " + canonicalHeight + "（3D 放大不再是整数倍，块会抖）。");
                    continue;
                }
                int block = canonicalHeight / heights[i];
                if (block != Unit)
                    problems.Add("u 对齐判据：UI 基本单位 Unit=" + Unit + " ≠ 3D 像素块 "
                        + block + "px（1080/" + heights[i] + "）。改 RT 档必须同步改 PixelSkin.Unit 并重烘焙。");
            }
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

                problems.AddRange(CheckBandsAndHoles(label, fresh, ExpectedTransparent(t.kind), t.kind));
                problems.AddRange(CheckPaletteLock(label, t, fresh));
                if (t.kind == "tab")
                    problems.AddRange(CheckTabBottomFlat(label, fresh, t));
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

        /// <summary>按画法族给期望透明像素数（"除已知形状外不该有孔洞"的已知形状就在这）。</summary>
        static int ExpectedTransparent(string kind)
        {
            switch (kind)
            {
                case "fill": return 0;
                case "tab": return ChamferPixelCount() / 2;    // 只切上两角
                case "ring": return RingSize * RingSize - RingOpaqueCount();
                case "pip": return PipSize * PipSize - PipOpaqueCount();
                case "sep": return 0;
                case "shadow": return ChamferPixelCount();
                default: return ChamferPixelCount();
            }
        }

        /// <summary>方环（选人圈/焦点框）的不透明像素数：镜像生成器画法（厚度 2u 且不在切角里）。</summary>
        static int RingOpaqueCount()
        {
            int n = RingSize, count = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int d = Math.Min(Math.Min(x, n - 1 - x), Math.Min(y, n - 1 - y));
                    if (d < 2 * Unit && !IsChamfer(x, y, n))
                        count++;
                }
            }
            return count;
        }

        /// <summary>位点菱形的不透明像素数（含 on 态高光 1px——它替换的是已有格色，不改变计数）。</summary>
        static int PipOpaqueCount()
        {
            int n = PipSize, cells = n / Unit, count = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int cx = x / Unit, cy = y / Unit;
                    int a = Math.Abs(2 * cx - (cells - 1)) + Math.Abs(2 * cy - (cells - 1));
                    if (a <= cells - 2)
                        count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 页签专属判据：**底边必须是平底**——中轴竖切的最下 3 层带厚度（3u）里全是底色，
        /// 不许出现环/斜面/内暗线色。页签的意义就是底边与宿主面板贴合，带了就是普通 Plate。
        /// </summary>
        static List<string> CheckTabBottomFlat(string label, Texture2D tex, BakeTarget t)
        {
            var problems = new List<string>();
            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            Ramp r = RampOf(ToneOfName(t.name));
            for (int y = 0; y < 3 * Unit; y++)
            {
                Color32 c = px[y * w + w / 2];
                if (!Same(c, r.S3))
                {
                    problems.Add(label + "：底边第 " + y + " 行的中列是 " + Hex(c)
                        + "，应为底色 " + Hex(r.S3) + "——页签底边必须无带（带它的是 Plate 不是 Tab）。");
                    break;
                }
            }
            return problems;
        }

        /// <summary>
        /// 几何判据：中轴色带边界必须落在 <see cref="Unit"/> 的整数倍上。
        /// 这就是参照 README §六 的"色带边界必须落在网格上"——**奇数宽的带 = 参数写错**。
        /// 另加"孔洞计数"：除该族已知形状（切角/环内/菱形外）外不应有任何透明像素。
        /// </summary>
        static List<string> CheckBandsAndHoles(string label, Texture2D tex, int expectedTransparent, string kind)
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
                    + " 个（超出该族已知形状 = 画法被改坏了）。");

            // 外环闭合：plate/tab/shadow 查；ring 族整张就是环，也查；fill/pip/sep 无环可查。
            if (kind == "plate" || kind == "tab" || kind == "ring" || kind == "shadow")
                problems.AddRange(CheckRingClosed(label, px, w, h, kind == "tab"));

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
        static List<string> CheckRingClosed(string label, Color32[] px, int w, int h, bool bottomOpen)
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
                    // 页签底边无带：判"外环像素"时到下边界的距离不参与（否则整条底边会被误记成环）
                    int dy = bottomOpen ? h - 1 - y : Math.Min(y, h - 1 - y);
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
        /// 锁板判据：画出来的每个不透明像素都必须在该画法族的色表里。这条挡的是
        /// "渐变/噪点/抗锯齿会带出板外色"——本族一律平涂，出现板外色说明有人往生成器里加了渐变。
        /// 各族的色表与画法取同一张源（Ramp/Fill/各 Colors() 表），色表和画法不会各改各的。
        /// </summary>
        static List<string> CheckPaletteLock(string label, BakeTarget t, Texture2D tex)
        {
            var allowed = new List<Color32>();
            string name = t.name;
            string kind = t.kind ?? KindOfName(name);
            switch (kind)
            {
                case "fill":
                {
                    var fillKind = (FillKind)Enum.Parse(typeof(FillKind), name.Substring("Pixel_Fill_".Length));
                    Fill f = FillOf(fillKind);
                    allowed.Add(f.Body);
                    allowed.Add(f.Dark);
                    allowed.Add(f.Hi);
                    // 前缘列对三层带各算一次：亮沿→亮沿、主体→Tip、暗沿→mix(暗沿, 亮沿)
                    allowed.Add(f.Tip);
                    allowed.Add(Mix(f.Dark, f.Hi, Fill.TipMix));
                    break;
                }
                case "ring":
                    // Pixel_Focus 与 Pixel_Ring 共用方环画法，各查各的色表
                    allowed.AddRange(name == "Pixel_Focus" ? FocusColors() : RingColors());
                    break;
                case "pip":
                    allowed.Add(Slot("SAND_LIGHT"));
                    allowed.Add(Slot("BRASS"));
                    allowed.Add(Slot("WOOD_DARK"));
                    allowed.Add(Slot("UI_BEVEL_LO"));
                    allowed.Add(PipSocketColor());
                    if (name == "Pixel_Pip_On")
                        allowed.Add(Slot("WHITE_HOT"));     // 顶端 1px 高光
                    break;
                case "sep":
                    allowed.AddRange(SepColors());
                    break;
                case "shadow":
                    allowed.Add(Slot("INK"));               // 投影 = 纯墨剪影
                    break;
                default:
                {
                    // plate / tab：本 tone 的色阶（悬停态是抬升后的阶梯——自成一张锁板表）
                    Tone tone = ToneOfName(name);
                    Ramp r = ParseStateTail(name.Substring("Pixel_".Length)) == State.Hovered
                        ? RampOf(tone).Lifted(HoverLift)
                        : RampOf(tone);
                    allowed.Add(r.S1);
                    allowed.Add(r.S2);
                    allowed.Add(r.S3);
                    allowed.Add(r.S4);
                    allowed.Add(r.Floor);
                    // Frame 凸起块的四角铆钉三色
                    if (tone == Tone.Frame && name.StartsWith("Pixel_Plate_", StringComparison.Ordinal))
                        allowed.AddRange(StudColors());
                    break;
                }
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
                        label + "：像素 #" + i + " 的色 " + Hex(px[i]) + " 不在本画法族的色表里（"
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
        /// 出接触表：每个 tone 一行（最小尺寸块 / 常态 / 悬停 / 按压 / 凹槽 / 槽内 60% 填充），
        /// 另加页签区（7 tone 的 Tab 贴在宿主面板顶边上）、语义件区（投影/选人圈/焦点框/位点/分隔线）
        /// 与 5 种填充条。**这张图是换装的评审靶子**，也是九宫格语义的自证：
        /// 块被拉到 96px 宽时四角与四条边必须与 36px 时完全一样（只有中心被拉长）。
        /// 底纹是 6px 棋盘（不是为了好看——透明切角/环内只在有底纹的图上读得出来）。
        /// </summary>
        public static string BuildContactSheet()
        {
            int u = Unit;
            int pad = 4 * u;
            int gap = 4 * u;
            int rowTone = 10 * u;
            int rowFill = 8 * u;
            int colMin = 2 * PlateBorder;   // 最小可渲染尺寸（证明 border×2 时四角没被切进内容区）
            int colWide = 32 * u;
            int tabH = 7 * u;
            int hostH = 10 * u;
            int rowSingles = 20 * u;

            int cols = pad + colMin + gap + (colWide + gap) * 5;
            int rows = pad
                + (rowTone + gap) * 7
                + (tabH + hostH + gap)
                + (rowSingles + gap)
                + (rowFill + gap) * 5
                + pad;

            var px = new Color32[cols * rows];
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    bool odd = ((x / (2 * u)) + (y / (2 * u))) % 2 == 1;
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

                // ① 最小可渲染尺寸 → ② 常态 → ③ 悬停 → ④ 按压 → ⑤ 凹槽 → ⑥ 槽内 60% 填充
                DrawOne(px, cols, tone, Piece.Plate, State.Normal, x, y + (rowTone - colMin) / 2, colMin, colMin);
                x += colMin + gap;
                DrawOne(px, cols, tone, Piece.Plate, State.Normal, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Plate, State.Hovered, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Plate, State.Pressed, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Track, State.Normal, x, y, colWide, rowTone);
                x += colWide + gap;
                DrawOne(px, cols, tone, Piece.Track, State.Normal, x, y, colWide, rowTone);
                DrawTrackFill(px, cols, tone, FillOfTone(tone), x, y, colWide, rowTone, 0.6f);
            }

            // 页签区：宿主面板（Dense）+ 7 个 tone 的页签贴在顶边上——
            // 页签的评审要点是"底边无带、与面板贴合后连成一体"。
            {
                int hostW = 60 * u;
                int hostY = cursorTop - tabH - hostH;
                cursorTop = hostY - gap;
                DrawOne(px, cols, Tone.Dense, Piece.Plate, State.Normal, pad, hostY, hostW, hostH);
                int tx = pad + 2 * u;
                for (int r = 0; r < tones.Length; r++)
                {
                    DrawOne(px, cols, tones[r], Piece.Tab, State.Normal, tx, hostY + hostH, 16 * u, tabH);
                    tx += 18 * u;
                }
            }

            // 语义件区：投影 / 选人圈 / 焦点框（套在浅按钮外）/ 位点 / 分隔线 / 危险条
            {
                int y = cursorTop - rowSingles;
                int cy = y + (rowSingles - 12 * u) / 2;      // 行内垂直居中基线
                cursorTop = y - gap;
                int x = pad;

                // 投影演示：阴影在右下错 1u，本体盖在上面
                DrawShadowAt(px, cols, x, cy, 24 * u, 12 * u);
                DrawOne(px, cols, Tone.Frame, Piece.Plate, State.Normal, x, cy, 24 * u, 12 * u);
                x += 26 * u + gap;

                DrawSized(px, cols, "Pixel_Ring", x, cy + (12 * u - 16 * u) / 2, 16 * u, 16 * u);
                x += 18 * u + gap;

                // 焦点框套一个浅按钮（焦点框比按钮外扩 2px）
                DrawOne(px, cols, Tone.Light, Piece.Plate, State.Normal, x + 2, cy + 5 * u / 3, 12 * u, 6 * u + 6);
                DrawSized(px, cols, "Pixel_Focus", x, cy + 2 * u, 12 * u + 4, 6 * u + 10);
                x += 14 * u + gap;

                DrawSized(px, cols, "Pixel_Pip_On", x, cy + 5 * u, PipSize, PipSize);
                DrawSized(px, cols, "Pixel_Pip_Off", x + PipSize + u, cy + 5 * u, PipSize, PipSize);
                DrawSized(px, cols, "Pixel_Pip_On", x, cy + 9 * u, PipSize * 2, PipSize * 2);
                x += PipSize * 3 + u * 2 + gap;

                DrawSized(px, cols, "Pixel_Sep_H", x, cy + 5 * u, 32 * u, 2 * u);
                DrawSized(px, cols, "Pixel_Sep_V", x + 34 * u, cy, 2 * u, 16 * u);
                x += 36 * u + gap;

                DrawOne(px, cols, Tone.Danger, Piece.Track, State.Normal, x, cy + 3 * u, 32 * u, 8 * u);
                DrawFill(px, cols, FillKind.Red, x + 3 * u, cy + 5 * u, 26 * u, 4 * u);
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

            return WriteZoomedSheet(px, cols, rows, SheetRelativePath, SheetZoom);
        }

        /// <summary>按名字把语义件按九宫格画到画布（接触表/美术稿共用）。</summary>
        static void DrawSized(Color32[] canvas, int canvasW, string name, int ox, int oy, int w, int h)
        {
            if (w <= 0 || h <= 0)
                return;
            Texture2D tex = CreateByName(name, out Vector4 border);
            Color32[] src = tex.GetPixels32();
            DrawNineSliced(src, tex.width, tex.height, border, canvas, canvasW, ox, oy, w, h);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>画面板投影（九宫格；w/h 是**面板本体**的尺寸，投影同尺寸）。</summary>
        static void DrawShadowAt(Color32[] canvas, int canvasW, int ox, int oy, int w, int h)
        {
            int dx = Mathf.RoundToInt(ShadowOffset.x);
            int dy = Mathf.RoundToInt(ShadowOffset.y);
            DrawSized(canvas, canvasW, "Pixel_Shadow", ox + dx, oy + dy, w, h);
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
            DrawNineSliced(src, PlateSize, PlateSize, border, canvas, canvasW, ox, oy, w, h);
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
            DrawNineSliced(src, FillSize, FillSize, border, canvas, canvasW, ox, oy, w, h);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>整数倍放大后落盘（最近邻；像素件放大用插值会糊，也会带出板外色）。</summary>
        static string WriteZoomedSheet(Color32[] px, int cols, int rows, string relativePath, int zoom)
        {
            int zw = cols * zoom;
            var zoomed = new Color32[zw * rows * zoom];
            for (int y = 0; y < rows * zoom; y++)
            {
                for (int x = 0; x < zw; x++)
                    zoomed[y * zw + x] = px[(y / zoom) * cols + (x / zoom)];
            }
            Texture2D sheet = ToTexture(zoomed, zw, rows * zoom);
            string abs = Path.Combine(RepoRoot(), relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            return relativePath;
        }

        // ==================================================================
        // 十一b、美术稿（showcase）：全件合成的战斗 HUD 风格构图——换装的"靶子图"
        // ==================================================================

        /// <summary>
        /// 3×5 像素字模（每字形 5 行、每行 3 位，msb=左）。只为美术稿标注用——
        /// 游戏内文字走 TMP 字体资产，不在这里造第二套字体系统。
        /// </summary>
        static readonly Dictionary<char, int[]> MiniFont = new Dictionary<char, int[]>
        {
            { ' ', new[] { 0, 0, 0, 0, 0 } },
            { 'A', new[] { 2, 5, 7, 5, 5 } },
            { 'C', new[] { 7, 4, 4, 4, 7 } },
            { 'D', new[] { 6, 5, 5, 5, 6 } },
            { 'E', new[] { 7, 4, 6, 4, 7 } },
            { 'F', new[] { 7, 4, 6, 4, 4 } },
            { 'G', new[] { 3, 4, 5, 5, 3 } },
            { 'H', new[] { 5, 5, 7, 5, 5 } },
            { 'I', new[] { 7, 2, 2, 2, 7 } },
            { 'K', new[] { 5, 5, 6, 5, 5 } },
            { 'L', new[] { 4, 4, 4, 4, 7 } },
            { 'M', new[] { 5, 7, 7, 5, 5 } },
            { 'N', new[] { 4, 6, 5, 3, 1 } },
            { 'O', new[] { 2, 5, 5, 5, 2 } },
            { 'P', new[] { 6, 5, 6, 4, 4 } },
            { 'Q', new[] { 2, 5, 5, 6, 1 } },
            { 'R', new[] { 6, 5, 6, 5, 5 } },
            { 'S', new[] { 3, 4, 2, 1, 6 } },
            { 'T', new[] { 7, 2, 2, 2, 2 } },
            { 'U', new[] { 5, 5, 5, 5, 7 } },
            { 'W', new[] { 5, 5, 7, 7, 5 } },
            { 'Y', new[] { 5, 5, 2, 2, 2 } },
            { '1', new[] { 2, 6, 2, 2, 7 } },
            { '2', new[] { 6, 1, 2, 4, 7 } },
            { '7', new[] { 7, 1, 2, 2, 2 } },
        };

        /// <summary>画一行像素字（字宽 3+1px、高 5px，y=文字底部）。先落阴影再落本体。</summary>
        static void DrawText(Color32[] canvas, int canvasW, int x, int y, string text, Color32 main, Color32 shadow)
        {
            for (int c = 0; c < text.Length; c++)
            {
                int[] glyph;
                if (!MiniFont.TryGetValue(text[c], out glyph))
                    continue;
                for (int r = 0; r < 5; r++)
                {
                    for (int b = 0; b < 3; b++)
                    {
                        if ((glyph[r] & (1 << (2 - b))) == 0)
                            continue;
                        int gx = x + c * 4 + b;
                        int gy = y + (4 - r);
                        if (shadow.a != 0)
                            canvas[(gy - 1) * canvasW + gx + 1] = shadow;
                        canvas[gy * canvasW + gx] = main;
                    }
                }
            }
        }

        /// <summary>在 <c>(cx,cy)</c> 矩形中心画一行像素字（按钮标签用）。</summary>
        static void DrawTextCentered(Color32[] canvas, int canvasW, int cx, int cy,
            string text, Color32 main, Color32 shadow)
        {
            DrawText(canvas, canvasW, cx - (text.Length * 4 - 1) / 2, cy - 2, text, main, shadow);
        }

        /// <summary>
        /// 美术稿：把全部件按战斗 HUD 的真实构图拼出来——船员卡（标题/名牌/血蓝条/属性行/
        /// 三态按钮+焦点框）、页签组、海图小地图（选人圈+位点+敌点）、敌方条与页点。
        /// 这是给创始人看的"成品长什么样"，也是接触表之外的**构图级自证**：
        /// 件与件的间距/内边距全部按 u 取整，拼起来没有半格错位。
        /// 深海底色背景；标注字用内置 3×5 字模（游戏内文字走 TMP，不共享这套）。
        /// </summary>
        public static string BuildShowcaseSheet()
        {
            int u = Unit;
            int W = 160 * u, H = 100 * u;
            var px = new Color32[W * H];

            // 背景：压暗的海渊色（比 Sea tone 更低一档，让所有面板浮起来）
            Color32 backdrop = Mix(Slot("SEA_DEEP"), Slot("INK"), 0.30f);
            for (int i = 0; i < px.Length; i++)
                px[i] = backdrop;

            Color32 white = Slot("WHITE_HOT");
            Color32 ink = Slot("INK");

            // ---- 右上：海图小地图（投影 + Sea 面板 + Sea 凹槽内底 + 选人圈 + 位点 + 敌点）----
            DrawShadowAt(px, W, 86 * u, 66 * u, 24 * u, 24 * u);
            DrawOne(px, W, Tone.Sea, Piece.Plate, State.Normal, 86 * u, 66 * u, 24 * u, 24 * u);
            DrawOne(px, W, Tone.Sea, Piece.Track, State.Normal, 90 * u, 70 * u, 16 * u, 16 * u);
            DrawSized(px, W, "Pixel_Ring", 95 * u, 75 * u, 8 * u, 8 * u);          // 选人圈（原生 8u，整数倍）
            DrawSized(px, W, "Pixel_Pip_Off", 92 * u, 84 * u, PipSize, PipSize);   // 空槽位
            DrawSized(px, W, "Pixel_Fill_Red", 100 * u, 72 * u, 2 * u, 2 * u);     // 敌方点
            DrawText(px, W, 87 * u, 88 * u, "MAP", white, ink);

            // ---- 左上：页签组（宿主 Dense 面板 + 选中 Light / 未选中 Dense×2）----
            DrawOne(px, W, Tone.Dense, Piece.Plate, State.Normal, 6 * u, 70 * u, 56 * u, 18 * u);
            DrawOne(px, W, Tone.Light, Piece.Tab, State.Normal, 8 * u, 88 * u, 16 * u, 7 * u);
            DrawOne(px, W, Tone.Dense, Piece.Tab, State.Normal, 26 * u, 88 * u, 16 * u, 7 * u);
            DrawOne(px, W, Tone.Dense, Piece.Tab, State.Normal, 44 * u, 88 * u, 16 * u, 7 * u);
            DrawText(px, W, 9 * u, 82 * u, "LOG", white, ink);

            // ---- 右下：敌方牌 + 敌方血条（30%）+ 页点 ----
            DrawShadowAt(px, W, 86 * u, 30 * u, 24 * u, 8 * u);
            DrawOne(px, W, Tone.Danger, Piece.Plate, State.Normal, 86 * u, 30 * u, 24 * u, 8 * u);
            DrawText(px, W, 86 * u + 2, 34 * u, "FOE", white, ink);
            DrawOne(px, W, Tone.Danger, Piece.Track, State.Normal, 86 * u, 8 * u, 24 * u, 10 * u);
            DrawFill(px, W, FillKind.Red, 87 * u, 11 * u, 7 * u, 4 * u);
            DrawSized(px, W, "Pixel_Pip_On", 86 * u, 22 * u, PipSize, PipSize);
            DrawSized(px, W, "Pixel_Pip_On", 93 * u, 22 * u, PipSize, PipSize);
            DrawSized(px, W, "Pixel_Pip_Off", 100 * u, 22 * u, PipSize, PipSize);

            // ---- 左侧主卡：船员卡（这是 HUD 的核心构图）----
            DrawShadowAt(px, W, 6 * u, 6 * u, 76 * u, 60 * u);
            DrawOne(px, W, Tone.Frame, Piece.Plate, State.Normal, 6 * u, 6 * u, 76 * u, 60 * u);
            DrawText(px, W, 10 * u, 57 * u, "CREW", white, ink);
            DrawSized(px, W, "Pixel_Sep_H", 10 * u, 54 * u, 68 * u, 2 * u);

            // 名牌（Light）+ 队长位点
            DrawOne(px, W, Tone.Light, Piece.Plate, State.Normal, 10 * u, 48 * u, 32 * u, 6 * u);
            DrawText(px, W, 12 * u, 50 * u, "CAPTAIN", ink, new Color32(0, 0, 0, 0));
            DrawSized(px, W, "Pixel_Pip_On", 44 * u, 47 * u, PipSize, PipSize);

            // 血条 / 蓝条：Track 12u 高（= 参照条"6 框架 + 6 填充 + …"的同比例放大）
            DrawText(px, W, 11 * u, 40 * u, "HP", white, ink);
            DrawOne(px, W, Tone.Frame, Piece.Track, State.Normal, 18 * u, 34 * u, 58 * u, 12 * u);
            DrawFill(px, W, FillKind.Red, 19 * u, 37 * u, 33 * u, 6 * u);
            DrawText(px, W, 11 * u, 26 * u, "MP", white, ink);
            DrawOne(px, W, Tone.Frame, Piece.Track, State.Normal, 18 * u, 20 * u, 58 * u, 12 * u);
            DrawFill(px, W, FillKind.Blue, 19 * u, 23 * u, 24 * u, 6 * u);

            // 属性行（Dense）+ 分隔线
            DrawOne(px, W, Tone.Dense, Piece.Plate, State.Normal, 10 * u, 18 * u, 68 * u, 6 * u);
            DrawText(px, W, 12 * u, 20 * u, "ATK 12  SPD 7", white, ink);
            DrawSized(px, W, "Pixel_Sep_H", 10 * u, 17 * u, 68 * u, 2 * u);

            // 三态按钮同台：常态+焦点框 / 悬停 / 按压
            DrawOne(px, W, Tone.Primary, Piece.Plate, State.Normal, 10 * u, 10 * u, 18 * u, 8 * u);
            DrawSized(px, W, "Pixel_Focus", 10 * u - 2, 10 * u - 2, 18 * u + 4, 8 * u + 4);
            DrawTextCentered(px, W, 10 * u + 9 * u, 14 * u, "OK", ink, Slot("SAND_LIGHT"));
            DrawOne(px, W, Tone.Light, Piece.Plate, State.Hovered, 31 * u, 10 * u, 20 * u, 8 * u);
            DrawTextCentered(px, W, 31 * u + 10 * u, 14 * u, "MENU", ink, new Color32(0, 0, 0, 0));
            DrawOne(px, W, Tone.Danger, Piece.Plate, State.Pressed, 54 * u, 10 * u, 20 * u, 8 * u);
            DrawTextCentered(px, W, 54 * u + 10 * u, 14 * u, "QUIT", white, ink);

            return WriteZoomedSheet(px, W, H, ShowcaseRelativePath, SheetZoom);
        }

        // ==================================================================
        // 十一c、运行时图集资产（PixelSkinAsset）：装配侧唯一的取图入口
        // ==================================================================

        /// <summary>
        /// 生成/刷新 <see cref="AtlasAssetPath"/> 的图集资产（Resources 内）。
        /// 贴图烘焙完才能跑（依赖 Sprite 导入完成）；运行时 <c>PixelSkin</c> 惰性加载它。
        /// 缺图会写成 null 并由判据/运行时双重红灯，绝不静默。
        /// </summary>
        public static void GenerateAtlas()
        {
            EnsureFolder("Assets/Resources/UI");

            var asset = AssetDatabase.LoadAssetAtPath<PirateCrew.UI.PixelSkinAsset>(AtlasAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PirateCrew.UI.PixelSkinAsset>();
                AssetDatabase.CreateAsset(asset, AtlasAssetPath);
            }

            var plates = new List<Sprite>();
            var tracks = new List<Sprite>();
            var tabs = new List<Sprite>();
            var toneColors = new List<Color32>();
            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                // 图集网格 = tone×state 全直积（运行时下标算式固定为 tone*3+state）
                foreach (State state in Enum.GetValues(typeof(State)))
                    plates.Add(LoadSprite(AssetNameOf(tone, Piece.Plate, state)));
                tracks.Add(LoadSprite(AssetNameOf(tone, Piece.Track, State.Normal)));
                tabs.Add(LoadSprite(AssetNameOf(tone, Piece.Tab, State.Normal)));
                Ramp r = RampOf(tone);
                toneColors.Add(r.S4);
                toneColors.Add(r.S3);
                toneColors.Add(r.S2);
            }
            var fills = new List<Sprite>();
            foreach (FillKind kind in Enum.GetValues(typeof(FillKind)))
                fills.Add(LoadSprite(AssetNameOf(kind)));

            asset.plates = plates.ToArray();
            asset.tracks = tracks.ToArray();
            asset.tabs = tabs.ToArray();
            asset.fills = fills.ToArray();
            asset.ring = LoadSprite("Pixel_Ring");
            asset.focus = LoadSprite("Pixel_Focus");
            asset.pipOn = LoadSprite("Pixel_Pip_On");
            asset.pipOff = LoadSprite("Pixel_Pip_Off");
            asset.separatorH = LoadSprite("Pixel_Sep_H");
            asset.separatorV = LoadSprite("Pixel_Sep_V");
            asset.shadow = LoadSprite("Pixel_Shadow");
            asset.toneColors = toneColors.ToArray();
            asset.ink = Slot("INK");
            asset.paperWhite = Slot("WHITE_HOT");

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        static Sprite LoadSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + "/" + name + ".png");
        }

        /// <summary>图集资产判据：数组长度对齐网格约定、无空槽（空槽 = 烘焙漏件或名字对不上）。</summary>
        static void VerifyAtlas(List<string> problems)
        {
            var asset = AssetDatabase.LoadAssetAtPath<PirateCrew.UI.PixelSkinAsset>(AtlasAssetPath);
            if (asset == null)
            {
                problems.Add("图集资产缺失：" + AtlasAssetPath + "（GenerateAtlas 应在烘焙后跑）。");
                return;
            }
            if (asset.plates == null || asset.plates.Length != 7 * 3)
                problems.Add("图集 plates 长度 " + (asset.plates == null ? 0 : asset.plates.Length) + "，应为 21（7 tone×3 state）。");
            if (asset.tracks == null || asset.tracks.Length != 7)
                problems.Add("图集 tracks 长度 " + (asset.tracks == null ? 0 : asset.tracks.Length) + "，应为 7。");
            if (asset.tabs == null || asset.tabs.Length != 7)
                problems.Add("图集 tabs 长度 " + (asset.tabs == null ? 0 : asset.tabs.Length) + "，应为 7。");
            if (asset.fills == null || asset.fills.Length != 5)
                problems.Add("图集 fills 长度 " + (asset.fills == null ? 0 : asset.fills.Length) + "，应为 5。");
            if (asset.toneColors == null || asset.toneColors.Length != 7 * 3)
                problems.Add("图集 toneColors 长度 " + (asset.toneColors == null ? 0 : asset.toneColors.Length) + "，应为 21。");
            CheckNoNull(problems, asset.plates, "plates");
            CheckNoNull(problems, asset.tracks, "tracks");
            CheckNoNull(problems, asset.tabs, "tabs");
            CheckNoNull(problems, asset.fills, "fills");
            if (asset.toneColors != null)
            {
                for (int i = 0; i < asset.toneColors.Length; i++)
                {
                    if (asset.toneColors[i].a != 255)
                    {
                        problems.Add("图集 toneColors[" + i + "] alpha != 255。");
                        break;
                    }
                }
            }
            if (asset.ring == null) problems.Add("图集缺 ring。");
            if (asset.focus == null) problems.Add("图集缺 focus。");
            if (asset.pipOn == null) problems.Add("图集缺 pipOn。");
            if (asset.pipOff == null) problems.Add("图集缺 pipOff。");
            if (asset.separatorH == null) problems.Add("图集缺 separatorH。");
            if (asset.separatorV == null) problems.Add("图集缺 separatorV。");
            if (asset.shadow == null) problems.Add("图集缺 shadow。");
        }

        static void CheckNoNull(List<string> problems, Sprite[] sprites, string what)
        {
            if (sprites == null)
                return;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null)
                {
                    problems.Add("图集 " + what + "[" + i + "] 是空槽——烘焙漏件或资产名对不上。");
                    return;
                }
            }
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
