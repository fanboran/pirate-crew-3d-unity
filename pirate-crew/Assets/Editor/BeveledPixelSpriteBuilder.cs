using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using PirateCrew.EditorTools.Art;
using UnityEditor;
using UnityEngine;
using Tone = PirateCrew.UI.PixelTone;
using Piece = PirateCrew.UI.PixelPiece;
using State = PirateCrew.UI.PixelState;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **Beveled Pixel UI**（像素斜面浮雕）九宫格 Sprite 的程序化生成器。
    ///
    /// 【W3：tone 贴图件族已退役】本类现役只有两条路：① <see cref="BakeAsepriteParts"/>
    /// 从 Aseprite dark theme.xml &lt;parts&gt; 全表逐件直切（面板/按钮/窗体等全部控件件）；
    /// ② <see cref="Targets"/> 里仅存的程序化件——焦点框（Pixel_Focus）与存件 tone 族窗体
    /// （Pixel_Window_*，供 <c>PixelSkin.Window</c>）。下面【一/二/三】描述的条槽/面板
    /// 两套 tone 语法（Plate/Track/Tab 与 Fill/Pip/Sep/Shadow/Ring）**已整族退役**，
    /// 仅作历史口径保留；判据与烘焙目标表都已同步收缩。
    ///
    /// 【它替掉谁】换皮前的 <see cref="GlassPanelSpriteBuilder"/>（半透明亚克力/玻璃拟态）是
    /// 上一版 UI 语言的底座；创始人裁决 UI 改走「Beveled Pixel + Chunky Pixel」后，
    /// **视觉层整体重写、机制骨架（StickUI 令牌/骨架/装配器/布局）照旧沿用**——
    /// 本类是"换皮不换骨"里的那张皮。两个生成器并存：玻璃族仍在役（4b 试点后才逐步退役），
    /// 图集目录也分开（<c>Assets/Art/Sprites/UI/Pixel/</c> vs <c>.../UI/</c>），互不覆盖。
    ///
    /// 【几何从哪来：量出来的，不是想出来的】几何关系（基本单位 u / 层带结构 / 同一色相 3 档明暗）
    /// 逐条来自 [UI 像素化标准参照](../../../docs/images/ui-pixel-ref/README.md) 与
    /// [Beveled Pixel 九宫格规范](../../../docs/技术/资产管线/UI九宫格.md) §一 的逐像素竖切表，
    /// 那两张表是对创始人指定的参照截图取竖切量出来的。
    /// **参照里有互不相同的两套语法，别互相推广**（本类第一版的教训）：
    ///   · **条槽语法**（金框血条）：实测自 `ref-bars-life-mana.png` —— 描边/外环/斜面/内暗线/槽底 五段，
    ///     强对比的装饰框，**只用在条槽（Track）上**；
    ///   · **面板/按钮语法**（工具对话框）：实测自 `ref-pixel-tool-dialog.png` ——
    ///     **近黑描边 + 受光侧一道唇边 + 平脸**，低对比、单描边。把金框语法推广到面板上
    ///     会读成"双层相框"（创始人一眼指出"和我给你的界面不一样"）。
    /// 本类只量**几何与色彩关系**，不复制它的任何贴图资源；色相一律换成
    /// <c>Assets/Data/Palette/pirate_palette.json</c> 的槽位（本工程色板），
    /// 于是"换主题只换色相"成立。
    ///
    /// 【一、两套边带语法（单位 u = 3px，从外缘向内）】
    /// <code>
    ///   条槽 Track（条槽语法，参照 bars）
    ///     ① 描边    INK（近黑，四周同色）        u
    ///     ② 外环    S2（受光侧）/ S1（背光侧）   u
    ///     ③ 斜面    S4 / S3                      u
    ///     ④ 内暗线  S2 / S1（与外环同色，规则 1）u
    ///     ⑤ 槽底    Floor                        剩余（九宫格拉伸区）
    ///
    ///   面板/按钮 Plate（工具对话框语法，参照 tool-dialog）
    ///     ① 描边    INK（近黑，四周同色）        u
    ///     ② 唇边    S4（**只在上/左**，背光侧无） u
    ///     ③ 脸      S3（平涂）                   剩余（九宫格拉伸区）
    /// </code>
    /// 由参照表直接读出的规则，别改：
    ///   0. **全部件的**最外 1u 都是近黑描边（`INK`）——**这是"全家一张皮"的签名**。
    ///      条槽最初没有这条（外环直接顶到最外缘），于是同一屏里条槽读成"另一种画风"：
    ///      面板近黑收边、条槽彩色收边（创始人 2026-09-22 走查："凹槽怎么画风和别的不一样"）。
    ///      参照条自己最外圈也是深近黑（`ref-bars-life-mana.png`），加这道反而是**贴回实测**。
    ///      受光侧的"亮"因此由**填充条自带的上亮沿**承担（Fill 族已退役），不靠槽的斜面。
    ///   1. **条槽的"内暗线"与同侧外环同色**——实测"外环 <c>#54481E</c> / 内暗线 <c>#54481E</c>"。
    ///      这条只对 **Track** 成立；对话框的面板/按钮没有内暗线（加了就是双层相框）。
    ///   2. **光永远来自左上**：上=左、下=右，受光侧才亮。
    ///      〔已知偏差〕参照里顶边比左边还亮一档，本波次不拆（要拆就得给每个 tone 第 5 档明暗），
    ///      登记为复核项（规范 §八）。
    ///   3. **三层带是同心环**：外环（描边）必须是一条**闭合圈**（沿切角斜线走）。
    ///      按"上下横贯 + 左右补边"画会把两侧外环截断、四角不闭合——走查记录见
    ///      <c>CreateTexture</c> 方法头与规范 §一.1；判据 = 外环 8 连通 1 段且无端点。
    ///
    /// 【二、三种内部（Piece）】
    ///   · <see cref="Piece.Plate"/> **凸起块**：对话框语法，内部 = S3 平涂。面板/按钮/列表行/标题条。
    ///   · <see cref="Piece.Track"/> **凹槽**：条槽语法，内部 = <c>Floor</c>（比最暗档再压一档）——
    ///     条状件（血条/施法条/进度条）的**空槽底**，填充件画在它上面（边距 6 艺术像素，
    ///     于是条的实际外观 = 近黑描边 + 外环 + 一段槽底 + 带亮沿的填充）。
    ///     [已知偏差] 实测槽底贴着一道 u 宽的次生亮带；本实现**有意压平成单色**（满格时被填充盖住），
    ///     登记为复核项。
    ///   · <see cref="Piece.Tab"/> **页签**：对话框语法 + 底边无带（border 下=0），与宿主面板贴合。
    ///
        /// 【三、状态（State）】按压 = **高光/阴影对调**（创始人裁决，不用 scale）：把上/左那套
        /// 与下/右那套整组互换，"凸"读成"凹"。位移 1px 右下**不在贴图里**——那是装配侧的事，
        /// 统一取 <see cref="PressOffset"/>（往贴图里烘位移会让九宫格切片错位）。
        /// 悬停 = **整条色阶上抬一档**（<see cref="HoverLift"/>）：S1~S3 与 Floor 各向亮侧邻档混
        /// 25%，最亮的 S4（唇边）不动——轮廓保持锐利、内部"点亮"。
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
    ///   Light   （暖白牌）  WHITE_HOT   / SAND_LIGHT  / NEUTRAL_LIGHT
    ///   Sea     （海图）    SEA_SHALLOW / SEA_MID     / SEA_DEEP
    ///   Primary （主行动点）SAND_LIGHT  / BRASS       / WOOD_DARK
    ///   Danger  （危险）    HERO_RED    / UI_DANGER   / SHADOW_DEEP
    ///   Warn    （警告）    GLOW_WARM   / UI_WARN     / SHADOW_WARM
    /// </code>
    /// 调色板 UI 族只有 3 个中性槽（**提案**态），凑不出第 4 档明暗，于是
    /// **S1 由 S2 派生**：<c>S1 = mix(S2, INK, 0.45)</c>（往本仓统一墨色 <c>INK</c> 压一步）。
    /// 派生规则只有这一条，写在 <c>Ramp.Of</c> 里，不外扩。**描边不再是派生值**——
    /// 面板语法直接用 INK（参照的工具对话框就是近黑描边，这条已由 2026-09-22 的对照走查裁决）。
    ///
    /// 【五、为什么产物是贴图而不是"平色 Image"】UGUI 的平色矩形画不出多层带（描边/唇边）
    /// 又要在任意尺寸下不变形——九宫格（<c>spriteBorder</c>）正是为此存在的机制：
    /// 只有四个角 + 四条边不拉伸，中心区被均匀拉长。故贴图尺寸必须
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
        /// 基本单位（px）。【×1 终局（2026-09-25 裁决）】1 设计格 = 1 贴图像素 = 1 画布像素，
        /// 模板/几何落盘不再乘任何倍率；本常量只余画布密度语义（CanvasScaler scaleFactor，
        /// 画布 = 屏幕 ÷ <see cref="Unit"/>）与评审图放大倍率。Aseprite 直切件天然 1:1。
        /// </summary>
        public const int Unit = PirateCrew.UI.PixelSkin.Unit;

        /// <summary>Track / Tab / 投影网格贴图边长（px，×1 后 = 设计格数）。只影响"最小可渲染尺寸"。</summary>
        public const int PlateSize = 18;

        /// <summary>Track/投影的九宫格切片（px）= 黑环 1 格 + 唇边 1 格（×1 设计格）。</summary>
        public const int PlateBorder = 2;

        /// <summary>条槽五段带切片（INK/外环/斜面/内暗线/槽底各 1 格），不随面板收档。</summary>
        public const int TrackBorder = 4;

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
        public const int ChamferDepthUnits = 0;   // 【2026-09-24 走查】豁口观感否决——方角（本类文档原话：要方角改 0）

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
            int dx = Math.Min(x, n - 1 - x);
            int dy = Math.Min(y, n - 1 - y);
            return dx + dy < depth;
        }

        /// <summary>切角像素总数（4 个角 × <c>深度×(深度+1)/2</c> 个格；×1 后 1 格 = 1px）。</summary>
        public static int ChamferPixelCount()
        {
            int d = ChamferDepthUnits;
            return d <= 0 ? 0 : 4 * (d * (d + 1) / 2);
        }

        /// <summary>Fill 贴图边长（px）：上 1 亮 / 中 4 主体 / 下 1 暗（×1 设计格）。</summary>
        public const int FillSize = 6;

        /// <summary>Fill 的上下切片边框 = 1（亮/暗带各一层）；右端另有 1 宽的"前缘"列。</summary>
        public const int FillBorder = 1;

        /// <summary>Fill 的最低可渲染高度（上下两层带）。低于它带会被压没。</summary>
        public const int FillMinRender = FillBorder * 2;

        /// <summary>
        /// 按压态的元素位移。【已退役（×1 终局）】theme &lt;parts&gt; 无 button_pressed——
        /// Aseprite 按钮只有 normal/hot/focused/selected 四态，按压位移是自创语言，
        /// 随全量对齐波整体废除（按钮按压反馈 = hot 皮保持，见 <c>SketchButton</c>）。
        /// </summary>
        [Obsolete("按压位移已随 ×1 终局退役（theme 无按压皮）", true)]
        public static readonly Vector2 PressOffset = Vector2.zero;

        /// <summary>
        /// 悬停态的色阶上抬比例：每档向亮侧邻档混这个比例（S4 顶档除外，见类头 §三）。
        /// 0.25 在实机看只够"仔细看才不同"（创始人 2026-09-22 走查"悬停和按压太不明显"），
        /// 提到 0.5 —— 抬升仍夹在原值与亮邻之间（单调与锁板都成立），但一眼可辨。
        /// </summary>
        public const float HoverLift = 0.5f;

        /// <summary>
        /// 按压态的整条下沉比例：对调高光/阴影之外，每档再向**暗侧邻档**混这个比例——
        /// "按下去 = 沉下去"。只靠对调时，1px 唇边换边在实机几乎不可读（同上走查）。
        /// </summary>
        public const float PressSink = 0.5f;

        /// <summary>选人圈 / 焦点框边长（px，×1 设计格）= 8。都是 2px 厚的方环。</summary>
        public const int RingSize = 8;

        /// <summary>选人圈 / 焦点框的九宫格切片边框 = 环厚（2px：外 1px 亮沿 + 内 1px 主体）。</summary>
        public const int RingBorder = 2;

        /// <summary>位点（页点 / 队伍槽指示）边长（px，×1 设计格）= 6。菱形。</summary>
        public const int PipSize = 6;

        /// <summary>分隔线长度轴尺寸（px）；粗细恒 2px（蚀刻槽线：暗 1 压亮 1）。</summary>
        public const int SepLength = 4;

        /// <summary>
        /// 面板投影偏移。【已退役（×1 终局）】Aseprite dark grep "shadow" 零命中——对话框没有
        /// 影子层，投影是自创语言，随全量对齐波整体废除（EnsurePanel 已就地销毁 Shadow 孩子）。
        /// </summary>
        [Obsolete("面板投影已随 ×1 终局退役（theme 无影子层）", true)]
        public static readonly Vector2 ShadowOffset = Vector2.zero;

        /// <summary>运行时图集资产路径（Resources 内；BuildAll 末尾生成，运行时 PixelSkin 经它取图）。</summary>
        public const string AtlasAssetPath = "Assets/Resources/UI/PixelSkin.asset";

        /// <summary>产物目录（与玻璃族分开，4b 试点后玻璃族逐步退役）。</summary>
        const string SpriteFolder = PixelArtTextureRules.UiSpriteFolder;

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

            /// <summary>
            /// <see cref="Lifted"/> 的镜像：整条阶梯向**暗侧**沉一档（按压态）。S4 是唯一没有暗侧
            /// 邻档语义的顶档，但也一起沉（顶档沉向 S3）——按压读的是"整块变暗"，不是某一道带。
            /// 沉完同样单调、自成一个锁板集合（判据侧按状态取表，见 <c>CheckPaletteLock</c>）。
            /// </summary>
            public Ramp Sunk(float t)
            {
                return new Ramp
                {
                    S4 = Mix(S4, S3, t),
                    S3 = Mix(S3, S2, t),
                    S2 = Mix(S2, S1, t),
                    S1 = Mix(S1, Floor, t),
                    Floor = Mix(Floor, Slot("INK"), t),
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
                    // 内容片/按钮 = Aseprite dark 主题 button_normal 三档（2026-09-24 创始人
                    // 裁决"整个游戏复用 Aseprite 这套 UI"）：唇亮 #41444A / 身 #292B30 / 唇暗 #202125。
                    // 与 Frame 共用唇色、身体压暗一档——与原版按钮/窗口的关系一致。
                    return new ToneSlots { hi = "UI_BEVEL_HI", mid = "UI_BTN_BODY", dark = "UI_BEVEL_LO" };
                case Tone.Light:
                    // 亮牌 = Aseprite 悬停面族：#575B61 / #41444A / #2C2C30（slider_full / check_hot_face 同源）。
                    return new ToneSlots { hi = "UI_HOVER_HI", mid = "UI_BEVEL_HI", dark = "UI_PANEL" };
                case Tone.Sea:
                    return new ToneSlots { hi = "SEA_SHALLOW", mid = "SEA_MID", dark = "SEA_DEEP" };
                case Tone.Primary:
                    // 主行动 = Aseprite button_selected 蓝三档原值（#6E9ADB / #4069C2 / #2A4185）。
                    return new ToneSlots { hi = "UI_ACCENT_HI", mid = "UI_ACCENT_MID", dark = "UI_ACCENT_DEEP" };
                case Tone.Danger:
                    // 危险 = 蓝阶 R/B 换位（与 Aseprite 蓝阶同亮度结构，色相转红）——
                    // 原 UI_DANGER(#CC2222) 保留给血条底/落水提示，不随 tone 走。
                    return new ToneSlots { hi = "UI_DANGER_HI", mid = "UI_DANGER_MID", dark = "UI_DANGER_DEEP" };
                case Tone.Warn:
                    return new ToneSlots { hi = "GLOW_WARM", mid = "UI_WARN", dark = "SHADOW_WARM" };
                default:
                    // Frame（面板）= Aseprite window_face 族：#41444A / #2C2C30 / #202125
                    // （background / face / editor_face 三色原值）。
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

        // ==================================================================
        // 五、调色板（真源 = JSON，不读 .asset 镜像）
        // ==================================================================

        static Dictionary<string, Color32> s_slots;

        /// <summary>本生成器引用到的全部槽位 id（判据用：缺槽必须在写盘前红灯，不能烘出品红贴图）。
        /// 【tone 族退役后】只余 tone 阶梯槽位 + 焦点框两色（HERO_BLUE / WHITE_HOT）+ 描边 INK。</summary>
        public static string[] UsedSlotIds()
        {
            var ids = new List<string> { "INK", "WHITE_HOT", "HERO_BLUE" };
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

        // ------------------------------------------------------------------
        // 七b、Aseprite dark 全部件搬皮（theme.xml 逐件复刻，2026-09-25 创始人裁决"全学"）
        // 几何/颜色全部取自 external/aseprite-ref dark 主题的 theme.xml 原值；
        // 固定色语义件（复选/滑条/滚动条/tooltip…）不走 tone 阶梯，用 ThemeXxx 精确色。
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 七b、Aseprite dark 全部件直切（theme.xml <parts> 表 → sheet.png 逐件切片）
        //
        // 【量纲终局（悬案①裁定 ×1）】sheet.png 是 1x 设计格图集（theme 根属性
        // screenscaling="2" 只是屏幕放大系数）：1 贴图像素 = 1 设计格 = 1 画布像素。
        // 直切天然满足 ×1——贴图=模板格数、九宫格切片=声明值、渲染尺寸=格数，全链 1:1 零压缩。
        //
        // 【为什么直切而不是手绘模板】裁决「原封不动复刻参考库 + 禁止自己量图、自己设计、
        // 自己推断」：字符画模板是近似转写（自己量图），直切是唯一零推断路线——
        // 件外观与 Aseprite dark 逐位一致，切片直接取 theme.xml 声明的 w1/w2/w3 h1/h2/h3。
        //
        // 【真源】入库副本 Assets/Art/Sprites/UI/Aseprite/{theme.xml, sheet.png}
        // （出处与许可见同目录 LICENSE.txt；工作台原件在 external/aseprite-ref/）。
        // ------------------------------------------------------------------

        /// <summary>直切真源目录（theme.xml + sheet.png + LICENSE.txt）。</summary>
        public const string AsepriteFolder = "Assets/Art/Sprites/UI/Aseprite";

        /// <summary>直切件落盘目录（一件一 PNG，命名 = theme part id 原名）。</summary>
        public const string AsePartsFolder = AsepriteFolder + "/Parts";

        const string AseThemeXmlPath = AsepriteFolder + "/theme.xml";
        const string AseSheetPngPath = AsepriteFolder + "/sheet.png";

        /// <summary>theme.xml &lt;parts&gt; 一行的解析结果（源矩形 + 九宫格切片声明）。</summary>
        public sealed class AsePart
        {
            public string id;
            public int x, y, w, h;
            /// <summary>九宫格切片（Unity spriteBorder 序：左/下/右/上）；未声明件全 0。</summary>
            public int borderL, borderB, borderR, borderT;
            /// <summary>是否声明了 w1..3 / h1..3（有 = 九宫格件，无 = 固定尺寸件）。</summary>
            public bool sliced;
        }

        /// <summary>
        /// 家族归类表（陈列廊面板序 = 本表序）：id 前缀 → 家族标签。
        /// **先长后短**（buttonset 先于 button、toolbutton 先于 tool、transparent_scrollbar
        /// 先于 scrollbar），同标签多条前缀自动并成一族面板；无命中落「其它」。
        /// </summary>
        static readonly (string prefix, string label)[] AseFamilyOrder =
        {
            ("window", "窗体"), ("buttonset", "按钮组"), ("button", "按钮"),
            ("toolbutton", "工具钮"), ("drop_down", "下拉按钮"), ("drop_pixels", "拖放指示"),
            ("check", "复选框"), ("radio", "单选钮"),
            ("mini_slider", "滑条"), ("slider", "滑条"),
            ("mini_scrollbar", "滚动条"), ("transparent_scrollbar", "滚动条"), ("scrollbar", "滚动条"),
            ("combobox", "组合框"), ("spin", "组合框"),
            ("tab", "页签"), ("separator", "分隔线"), ("sunken", "凹槽"), ("tooltip", "气泡"),
            ("menu", "菜单与视图"), ("list_view", "菜单与视图"), ("small_icon", "菜单与视图"),
            ("big_icon", "菜单与视图"), ("newfolder", "菜单与视图"), ("folder", "菜单与视图"),
            ("arrow_circle", "菜单与视图"),
            ("colorbar", "色板"), ("simple_color", "色板"),
            ("editor", "编辑器视图"),
            ("selection", "变换与选区"), ("outline", "变换与选区"), ("transformation", "变换与选区"),
            ("pivot", "变换与选区"), ("canvas", "变换与选区"),
            ("ink", "绘制与混合"), ("linear_gradient", "绘制与混合"), ("radial_gradient", "绘制与混合"),
            ("dynamics", "绘制与混合"), ("tiles", "绘制与混合"),
            ("no_symmetry", "对称"), ("horizontal_symmetry", "对称"), ("vertical_symmetry", "对称"),
            ("right_diagonal", "对称"), ("left_diagonal", "对称"),
            ("icon", "图标杂项"), ("corner_radius", "图标杂项"), ("warning", "图标杂项"),
            ("pal_", "图标杂项"), ("aseprite", "图标杂项"), ("flag", "图标杂项"),
            ("pinned", "图标杂项"), ("unpinned", "图标杂项"), ("one_win", "图标杂项"),
            ("multi_win", "图标杂项"), ("color", "图标杂项"),
            ("ani_", "动画与调试"), ("debug", "动画与调试"),
            ("cursor", "光标"), ("tool", "工具图标"), ("timeline", "时间轴"),
        };

        /// <summary>件 → 家族标签（陈列廊分组用；入表见 <see cref="AseFamilyOrder"/>）。</summary>
        static string AseFamilyOf(string id)
        {
            foreach ((string prefix, string label) in AseFamilyOrder)
                if (id.StartsWith(prefix)) return label;
            return "其它";
        }

        /// <summary>件 → 家族序（GenerateAtlas 按它排陈列廊面板顺序）。</summary>
        static int AseFamilyRank(string id)
        {
            for (int i = 0; i < AseFamilyOrder.Length; i++)
                if (id.StartsWith(AseFamilyOrder[i].prefix)) return i;
            return AseFamilyOrder.Length;
        }

        static Dictionary<string, AsePart> s_aseParts;
        static Color32[] s_sheetPixels;
        static int s_sheetW, s_sheetH;

        /// <summary>解析 theme.xml &lt;parts&gt; 全表（id → 源矩形 + 切片声明）。</summary>
        public static Dictionary<string, AsePart> ParseAseParts()
        {
            if (s_aseParts != null)
                return s_aseParts;
            string abs = Path.Combine(UnityProjectRoot(), AseThemeXmlPath);
            var doc = new XmlDocument();
            doc.Load(abs);
            s_aseParts = new Dictionary<string, AsePart>();
            foreach (XmlNode node in doc.SelectNodes("/theme/parts/part"))
            {
                var part = new AsePart
                {
                    id = node.Attributes["id"].Value,
                    x = int.Parse(node.Attributes["x"].Value),
                    y = int.Parse(node.Attributes["y"].Value),
                };
                // parts 两种形态：整图件 = x/y/w/h；切片件 = x/y/w1..3/h1..3，
                // **宽 = w1+w2+w3、高 = h1+h2+h3**（theme 不给切片件 w/h——首版在此 NRE）。
                if (node.Attributes["w1"] != null)
                {
                    part.sliced = true;
                    part.borderL = int.Parse(node.Attributes["w1"].Value);
                    part.borderR = int.Parse(node.Attributes["w3"].Value);
                    part.borderT = int.Parse(node.Attributes["h1"].Value);
                    part.borderB = int.Parse(node.Attributes["h3"].Value);
                    part.w = part.borderL + int.Parse(node.Attributes["w2"].Value) + part.borderR;
                    part.h = part.borderT + int.Parse(node.Attributes["h2"].Value) + part.borderB;
                }
                else
                {
                    part.w = int.Parse(node.Attributes["w"].Value);
                    part.h = int.Parse(node.Attributes["h"].Value);
                }
                s_aseParts[part.id] = part;
            }
            return s_aseParts;
        }

        /// <summary>sheet.png 原始像素缓存（File 直读 + LoadImage，不经导入设置）。</summary>
        static Color32[] EnsureAseSheet()
        {
            if (s_sheetPixels != null)
                return s_sheetPixels;
            string abs = Path.Combine(UnityProjectRoot(), AseSheetPngPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(abs)))
                    throw new InvalidOperationException("[BeveledPixelSpriteBuilder] sheet.png 解不开：" + abs);
                s_sheetW = texture.width;
                s_sheetH = texture.height;
                s_sheetPixels = texture.GetPixels32();
                return s_sheetPixels;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>直切全表：theme &lt;parts&gt; **全量 345 件**逐件从 sheet.png 切 PNG 落 Parts/
        /// （创始人 2026-09-25 裁决「全部移动过来，即便本端没必要有的」），九宫格切片 = 声明值。
        /// 旧白名单已退役——加件 = 改 theme.xml 后重烘焙，无需在本文件登记。</summary>
        public static void BakeAsepriteParts()
        {
            Dictionary<string, AsePart> table = ParseAseParts();
            EnsureAseSheet();
            EnsureFolder(AsePartsFolder);
            foreach (AsePart part in table.Values)
                BakeAsePart(part);
            AssetDatabase.Refresh();
        }

        static void BakeAsePart(AsePart part)
        {
            Color32[] sheet = EnsureAseSheet();
            if (part.x + part.w > s_sheetW || part.y + part.h > s_sheetH)
                throw new InvalidOperationException("[BeveledPixelSpriteBuilder] part "
                    + part.id + " 的源矩形越界 sheet.png。");
            var px = new Color32[part.w * part.h];
            // theme 坐标 y=0 在 sheet 顶部；Texture2D/SetPixels32 的 (0,0) 在左下——行序翻转。
            for (int row = 0; row < part.h; row++)
            {
                int srcRow = part.y + (part.h - 1 - row);
                Array.Copy(sheet, (s_sheetH - 1 - srcRow) * s_sheetW + part.x, px, row * part.w, part.w);
            }
            var texture = new Texture2D(part.w, part.h, TextureFormat.RGBA32, false);
            texture.SetPixels32(px);
            texture.Apply();
            try
            {
                string path = AsePartsFolder + "/" + part.id + ".png";
                string abs = Path.Combine(UnityProjectRoot(), path);
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                File.WriteAllBytes(abs, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                PixelArtTextureRules.ApplySprite(AssetImporter.GetAtPath(path) as TextureImporter,
                    part.sliced
                        ? new Vector4(part.borderL, part.borderB, part.borderR, part.borderT)
                        : Vector4.zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// 直切件判据：盘上 PNG 与 sheet.png 源区域**逐位一致** + 尺寸与 theme.xml 声明一致
        /// + 导入五值/切片与声明一致。直切件没有"生成器参数"，与源逐位比对就是最强判据
        /// （手改盘上 PNG 或 sheet.png 更新后忘重烘都会被抓）。
        /// </summary>
        public static void VerifyAsepriteParts(List<string> problems)
        {
            Dictionary<string, AsePart> table = ParseAseParts();
            Color32[] sheet = EnsureAseSheet();
            List<string> ids = new List<string>(table.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                AsePart part = table[id];
                string path = AsePartsFolder + "/" + id + ".png";
                string abs = Path.Combine(UnityProjectRoot(), path);
                if (!File.Exists(abs))
                {
                    problems.Add("Aseprite 直切件 " + id + "：盘上没有 " + path + "（全量迁移，缺件即重烘）。");
                    continue;
                }
                var disk = new Texture2D(2, 2);
                try
                {
                    if (!disk.LoadImage(File.ReadAllBytes(abs)))
                    {
                        problems.Add("Aseprite 直切件 " + id + "：盘上 PNG 解不开。");
                        continue;
                    }
                    if (disk.width != part.w || disk.height != part.h)
                    {
                        problems.Add("Aseprite 直切件 " + id + "：尺寸 " + disk.width + "×" + disk.height
                            + "，theme.xml 声明 " + part.w + "×" + part.h + "。");
                        continue;
                    }
                    Color32[] px = disk.GetPixels32();
                    for (int row = 0; row < part.h; row++)
                    {
                        int srcRow = part.y + (part.h - 1 - row);
                        int sheetRowStart = (s_sheetH - 1 - srcRow) * s_sheetW + part.x;
                        for (int col = 0; col < part.w; col++)
                        {
                            Color32 a = px[row * part.w + col];
                            Color32 b = sheet[sheetRowStart + col];
                            if (!Same(a, b))
                            {
                                problems.Add("Aseprite 直切件 " + id + "：像素 (" + col + "," + row
                                    + ") 与 sheet.png 源区域不一致（盘上 " + Hex(a) + " / 源 " + Hex(b)
                                    + "）——直切件必须与参考库逐位一致，重跑烘焙。");
                                row = part.h;
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(disk);
                }
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Vector4 border = part.sliced
                    ? new Vector4(part.borderL, part.borderB, part.borderR, part.borderT)
                    : Vector4.zero;
                if (importer == null)
                    problems.Add("Aseprite 直切件 " + id + "：读不到 TextureImporter。");
                else
                {
                    List<string> importProblems = PixelArtTextureRules.CheckSprite(importer, border);
                    for (int i = 0; i < importProblems.Count; i++)
                        problems.Add("Aseprite 直切件 " + id + "：" + importProblems[i]);
                }
            }
        }

        /// <summary>窗控钮 9×11（theme window_button；三态换色不换几何）。【已退役】窗控钮等
        /// theme 语义件全部改为 sheet.png 直切（见 BakeAsepriteParts），手绘模板仅存 tone 族。</summary>

        /// <summary>带标题栏的窗体 13×24（theme window：3/7/3 × 15/4/5）。
        /// B=标题带（tone 亮档），带底暗线 D，窗体 E；顶部切片 15 = 环+带+带底线。</summary>
        static readonly string[] WindowTemplate =
        {
            "KKKKKKKKKKKKK",
            "KCCCCCCCCCCCK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KBBBBBBBBBBBK",
            "KDDDDDDDDDDDK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KEEEEEEEEEEEK",
            "KKKKKKKKKKKKK",
        };

        /// <summary>通用字母模板构建：'.' = 透明，其余按 map 取色，×1 落盘（1 格 = 1 贴图像素）。</summary>
        static Texture2D BuildTemplateTexture(string[] template, Dictionary<char, Color32> map,
            Vector4 border)
        {
            int rows = template.Length;
            int cols = template[0].Length;
            for (int i = 1; i < rows; i++)
            {
                if (template[i].Length != cols)
                    throw new InvalidOperationException("[BeveledPixelSpriteBuilder] 模板第 " + i
                        + " 行宽 " + template[i].Length + " ≠ 首行 " + cols + "——参差模板会越界。");
            }
            int w = cols, h = rows;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    char ch = template[y][x];
                    px[y * w + x] = ch == '.' ? new Color32(0, 0, 0, 0) : map[ch];
                }
            }
            return ToTexture(px, w, h);
        }

        /// <summary>带标题栏的窗体（theme window：13×24，切片 左3/下5/右3/上15）。
        /// 标题带 = tone 亮档（Dense/Frame 下即 theme 的 #41444a），带底暗线 = 暗档，
        /// 窗体 = 中档——tone 阶梯在无色 tone 上正好落 theme 中性三档。</summary>
        static Texture2D BuildWindowFromTemplate(Ramp r, out Vector4 border)
        {
            var map = new Dictionary<char, Color32>
            {
                { 'K', Slot("INK") },
                { 'C', r.S4 },   // 带顶受光唇
                { 'B', r.S4 },   // 标题带面
                { 'D', r.S2 },   // 带底暗线
                { 'E', r.S3 },   // 窗体面
            };
            border = new Vector4(3, 5, 3, 15);   // 左/下/右/上（×1）
            return BuildTemplateTexture(WindowTemplate, map, border);
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

        /// <summary>焦点框两色：海蓝外缘、内缘提白——键盘焦点的"电气"读法。</summary>
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
                    if (d >= 2)
                    {
                        px[y * n + x] = new Color32(0, 0, 0, 0);   // 环内是透的（垫在目标物外沿）
                        continue;
                    }
                    px[y * n + x] = d < 1 ? outer : inner;
                }
            }
            border = new Vector4(RingBorder, RingBorder, RingBorder, RingBorder);
            return ToTexture(px, n, n);
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
        /// 全部要烘焙的资产（**只余 Ase 直切件族之外的两件程序化件**）。
        /// 【tone 贴图件族已退役（W3）】Plate/Track/Fill/Panel/Tab/Ring/Pip/Separator/Shadow
        /// 九族不再程序化烘焙；theme 控件件一律走 <see cref="BakeAsepriteParts"/> 从
        /// sheet.png 直切（现役管线）。本表只留：焦点框（Pixel_Focus）+ 存件的 tone 族
        /// 窗体皮（Pixel_Window_*；仍供 PixelSkin.Window 出口）。
        /// </summary>
        public static BakeTarget[] Targets()
        {
            var list = new List<BakeTarget>();

            list.Add(NewTarget("Pixel_Focus"));

            // 存件 tone 族窗体（×1）。其余 theme 控件件无程序化画法，走 BakeAsepriteParts。
            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
                list.Add(NewTarget(AssetNameOf(tone, Piece.Window, State.Normal)));

            return list.ToArray();
        }

        /// <summary>资产名 → 画法族（判据分流用；与 <see cref="CreateByName"/> 的路由一一对应）。
        /// 只余 ring（Pixel_Focus 方环）与 window（tone 族窗体）两族。</summary>
        public static string KindOfName(string name)
        {
            if (name == "Pixel_Focus")
                return "ring";
            return "window";
        }

        static BakeTarget NewTarget(string name)
        {
            string kind = KindOfName(name);
            int w, h;
            Vector4 border;
            switch (kind)
            {
                case "ring":
                    w = RingSize; h = RingSize;
                    border = new Vector4(RingBorder, RingBorder, RingBorder, RingBorder);
                    break;
                default:
                    // window：13×24，切片 左3/下5/右3/上15
                    w = WindowTemplate[0].Length;
                    h = WindowTemplate.Length;
                    border = new Vector4(3, 5, 3, 15);
                    break;
            }
            return new BakeTarget
            {
                name = name,
                assetPath = SpriteFolder + "/" + name + ".png",
                width = w,
                height = h,
                border = border,
                isFill = false,
                kind = kind,
            };
        }

        /// <summary>把目标名字解析回画法（<see cref="Targets"/> 的逆）。</summary>
        static Texture2D CreateByName(string name, out Vector4 border)
        {
            if (name == "Pixel_Focus")
            {
                Color32[] c = FocusColors();
                return CreateRingTexture(c[0], c[1], out border);
            }
            // Pixel_Window_<Tone>
            return BuildWindowFromTemplate(RampOf(ToneOfName(name)), out border);
        }

        /// <summary>从资产名解析 tone（<c>Pixel_Plate_Frame[_Hover|_Pressed]</c> → <c>Tone.Frame</c>）。</summary>
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

            // Aseprite dark 控件件：theme.xml <parts> 表 → sheet.png 直切（×1 全量对齐波主菜）
            BakeAsepriteParts();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GenerateAtlas();                        // 图集资产要在贴图导入之后生成

            List<string> problems = Verify();

            Debug.Log("[BeveledPixelSpriteBuilder] Beveled Pixel 重烘焙完成，共 " + targets.Length
                + " 张：" + SpriteFolder + "（图集 " + AtlasAssetPath + "）");
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

            CheckGeometryConstants(problems, PlateSize, PlateBorder, FillSize, FillBorder, 1);
            CheckUnitAlignment(problems);
            VerifyAsepriteParts(problems);
            VerifyAtlas(problems);

            return problems;
        }

        /// <summary>
        /// **UI 颗粒度判据**。
        /// ① UI 双源必须相等：<see cref="Unit"/> == <c>PixelartPilotScene.PixelScale</c>
        ///    （"一个 UI 艺术像素占几个屏幕像素"两侧真源不同步 = 装配事故）。
        /// ② 3D 侧（URP 渲染器 <c>renderHeightPixels</c>）只锁**整数倍放大**（1080 % RT 高 == 0）。
        ///    【2026-09-24 解耦】UI 裁决 2:1（Unit=2）后，3D RT 仍 640×360（×3 块）——UI 与 3D
        ///    颗粒度**有意不同网格**（创始人"资产 1:1 + 显示端整数 ×2"只针对 UI 栈）；若日后
        ///    裁决 3D RT 也换档统一，把 block == Unit 的等式恢复回来即可。
        /// </summary>
        static void CheckUnitAlignment(List<string> problems)
        {
            // 【×1 终局】旧判据①（PixelartPilotScene.PixelScale == Unit）已删：
            // Unit 不再是"UI 艺术像素比例"（贴图/布局 1:1 后无倍率可言），只剩画布密度语义，
            // 与 3D 侧 RT 比例彻底解耦（2026-09-24 解耦、2026-09-25 ×1 终局）。

            // 3D 侧（URP 渲染器 renderHeightPixels）只锁整数倍放大。
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
                // 2026-09-24 解耦：不再要求 3D 块大小 == Unit（UI 2:1、3D 仍 ×3，见方法头注释）。
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

                problems.AddRange(CheckBandsAndHoles(label, fresh, ExpectedTransparent(t.kind, t.name), t.kind));
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

        /// <summary>按画法族给期望透明像素数（"除已知形状外不该有孔洞"的已知形状就在这）。
        /// 【tone 族退役后】只余 ring（Pixel_Focus 方环）与 window（直角全不透明）。</summary>
        static int ExpectedTransparent(string kind, string name)
        {
            switch (kind)
            {
                case "ring": return RingSize * RingSize - RingOpaqueCount();
                default: return 0;   // window：直角带标题窗体，全不透明
            }
        }

        /// <summary>方环（焦点框）的不透明像素数：镜像生成器画法（厚度 2px 且不在切角里）。</summary>
        static int RingOpaqueCount()
        {
            int n = RingSize, count = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int d = Math.Min(Math.Min(x, n - 1 - x), Math.Min(y, n - 1 - y));
                    if (d < 2 && !IsChamfer(x, y, n))
                        count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 几何判据：中轴色带边界必须落在 <see cref="Unit"/> 的整数倍上；
        /// 另加"孔洞计数"：除该族已知形状（切角/环内）外不应有任何透明像素。
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

            // 外环闭合：ring（焦点框）是同心方环，走像素级"边界 1u 内"判据。
            if (kind == "ring")
                problems.AddRange(CheckRingClosed(label, px, w, h, false));

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
                    if (Math.Min(dx, dy) != 0)
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
                // 【×1 口径】页签（bottomOpen）左右带直通底边 = 两条 1px"腿"，
                // 腿脚像素在 1px 网格下必然只有 1 个环邻——那是 Tab 语义（底边无带）的
                // 必然形状，不是断点（×2 时代腿有 2px 宽互相支撑，故旧判据未暴露）。
                if (bottomOpen && cy == 0)
                    continue;   // y=0 在下（Texture2D 坐标）——腿脚在底行
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
                if (band % 1 != 0)   // ×1 后 1px 网格上任何带宽都合法——判据退化为占位（保留结构防回跳）
                {
                    problems.Add(label + "：" + axisName + " " + start + ".." + (i - 1)
                        + " 带宽 " + band + "px 不是 1 的整数倍（该带色 " + Hex(axis[start])
                        + "）——色带边界必须落在像素网格上。");
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
                case "ring":
                    // Pixel_Focus 方环（海蓝外缘 + 内缘提白）
                    allowed.AddRange(FocusColors());
                    break;
                default:
                {
                    // window：本 tone 的阶梯纯档 + 近黑描边 INK（与 BuildWindowFromTemplate 同源）。
                    Tone tone = ToneOfName(name);
                    Ramp r = RampOf(tone);
                    allowed.Add(r.S1);
                    allowed.Add(r.S2);
                    allowed.Add(r.S3);
                    allowed.Add(r.S4);
                    allowed.Add(r.Floor);
                    allowed.Add(Slot("INK"));
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

            var windows = new List<Sprite>();
            var toneColors = new List<Color32>();
            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                // 存件 tone 族窗体皮（其余 tone 族贴图件已退役）；toneColors = 取色令牌 tone×3。
                windows.Add(LoadSprite(AssetNameOf(tone, Piece.Window, State.Normal)));
                Ramp r = RampOf(tone);
                toneColors.Add(r.S4);
                toneColors.Add(r.S3);
                toneColors.Add(r.S2);
            }

            // Aseprite dark 控件件（×1 全量迁移）：BakeAsepriteParts 的直切成品，
            // 平行数组 aseParts/asePartNames/asePartFamilies 收**全表 345 件**——
            // 运行时 PixelSkin.Ase(id) 查它；陈列廊按 families 分组、数组序即面板序
            // （家族序 = AseFamilyOrder，同族内按 id 字典序）。
            var aseTable = ParseAseParts();
            List<string> aseIds = new List<string>(aseTable.Keys);
            aseIds.Sort((a, b) =>
            {
                int rank = AseFamilyRank(a).CompareTo(AseFamilyRank(b));
                return rank != 0 ? rank : string.CompareOrdinal(a, b);
            });
            var aseSprites = new List<Sprite>();
            var aseNames = new List<string>();
            var aseFamilies = new List<string>();
            var aseById = new Dictionary<string, Sprite>();
            foreach (string id in aseIds)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AsePartsFolder + "/" + id + ".png");
                aseSprites.Add(sprite);
                aseNames.Add(id);
                aseFamilies.Add(AseFamilyOf(id));
                aseById[id] = sprite;
            }
            // 全表灌进图集：唯一写口 ApplyBake（烘焙器专用，运行时只读），具名参数与
            // 序列化字段一一对照，落盘内容与旧直写逐位一致。
            asset.ApplyBake(
                windows: windows.ToArray(),
                focus: LoadSprite("Pixel_Focus"),
                // 语义取用器的固定族字段也落直切件（id 与 PixelSkin 取用器映射一一对应）
                windowButtons: new[]
                {
                    aseById["window_button_normal"],
                    aseById["window_button_hot"],
                    aseById["window_button_selected"],
                },
                windowIcons: new[]
                {
                    aseById["window_close_icon"],
                    aseById["window_help_icon"],
                    aseById["window_play_icon"],
                    aseById["window_stop_icon"],
                    aseById["window_center_icon"],
                },
                checks: new[] { aseById["check_normal"], aseById["check_selected"] },
                radios: new[] { aseById["radio_normal"], aseById["radio_selected"] },
                widgetFocus: aseById["check_focus"],
                sunken: new[] { aseById["sunken_normal"], aseById["sunken_focused"] },
                sliderEmpty: new[]
                {
                    aseById["slider_empty"], aseById["slider_empty_focused"],
                },
                sliderFull: new[]
                {
                    aseById["slider_full"], aseById["slider_full_focused"],
                },
                sliderThumb: aseById["mini_slider_thumb"],
                scrollbars: new[] { aseById["scrollbar_bg"], aseById["scrollbar_thumb"] },
                tooltip: aseById["tooltip"],
                arrowsDown: new[]
                {
                    aseById["combobox_arrow_down"],
                    aseById["combobox_arrow_down_selected"],
                    aseById["combobox_arrow_down_disabled"],
                },
                aseParts: aseSprites.ToArray(),
                asePartNames: aseNames.ToArray(),
                asePartFamilies: aseFamilies.ToArray(),
                toneColors: toneColors.ToArray(),
                ink: Slot("INK"),
                paperWhite: Slot("UI_TEXT"));   // Aseprite 正文灰 #C0C0C0（text 色原值）

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
            // 只读访问器（IReadOnlyList：判空语义与旧数组一致，Length→Count 是唯一口径差）
            if (asset.Windows == null || asset.Windows.Count != 7)
                problems.Add("图集 windows 长度 " + (asset.Windows == null ? 0 : asset.Windows.Count) + "，应为 7（7 tone）。");
            if (asset.ToneColors == null || asset.ToneColors.Count != 7 * 3)
                problems.Add("图集 toneColors 长度 " + (asset.ToneColors == null ? 0 : asset.ToneColors.Count) + "，应为 21。");
            CheckNoNull(problems, asset.Windows, "windows");
            // Aseprite 直切件：三平行数组对齐 + 长度 = theme <parts> 全表 + 无空槽
            // （空槽 = 直切漏件或 part id 对不上）。
            int aseTableCount = ParseAseParts().Count;
            if (asset.AseParts == null || asset.AsePartNames == null || asset.AsePartFamilies == null
                || asset.AseParts.Count != asset.AsePartNames.Count
                || asset.AseParts.Count != asset.AsePartFamilies.Count
                || asset.AseParts.Count != aseTableCount)
            {
                problems.Add("图集 aseParts/asePartNames/asePartFamilies 缺失或长度与 theme.xml 全表（"
                    + aseTableCount + " 件）不对齐——GenerateAtlas 应在直切后跑。");
            }
            CheckNoNull(problems, asset.AseParts, "aseParts");
            if (asset.ToneColors != null)
            {
                for (int i = 0; i < asset.ToneColors.Count; i++)
                {
                    if (asset.ToneColors[i].a != 255)
                    {
                        problems.Add("图集 toneColors[" + i + "] alpha != 255。");
                        break;
                    }
                }
            }
            if (asset.Focus == null) problems.Add("图集缺 focus。");
        }

        static void CheckNoNull(List<string> problems, IReadOnlyList<Sprite> sprites, string what)
        {
            if (sprites == null)
                return;
            for (int i = 0; i < sprites.Count; i++)
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
