using PirateCrew.Data;
using PirateCrew.UI.Stick;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 多彩卡通 UI 的设计 Token 真值源（配色 / 形状 / 字号 / 语义色槽）。
    ///
    /// 【换装（Beveled Pixel）】皮肤的真值源已交给 <see cref="PixelSkin"/>（tone 色阶 +
    /// 九宫格件）；本类的底色十个成员因被 Tests/UI/UiSkinTests.cs 钉死而暂留 StickTokens
    /// 值（详见「像素皮取色桥」段落）。像素皮新代码一律经 PixelPlate / PixelTextOn 或
    /// <see cref="PixelSkin"/> 取色，别再新写魔法值。
    ///
    /// 【风格定案（用户三轮裁决，2026-09-19）】
    ///   · 高饱和多彩：每个武器 / 职业 / 状态有自己的色相（哈迪斯式"深底 + 宝石彩图标 +
    ///     金强调"），不再收敛到单一暖木色板；
    ///   · 形状收敛：小圆角（面板 8 / 按钮 6）、无粗描边（至多 1px 细线）、无贴纸硬投影——
    ///     层级靠色彩明度差与留白，不靠装饰；
    ///   · 拟物极度抽离：默认纯平涂，不追求材质真实感；
    ///   · 图标优先：文字只留标题横幅 / 一条操作提示 / 无障碍兜底。
    ///
    /// 【单一真值】全项目字号体系（原 <c>MenuUiBuilder.FontScale</c>，用户 2026-09-14 裁决
    /// "整体下调一档"）迁移到本类 <see cref="Font"/>；皮肤形状 / 语义色也只许引用本类，
    /// 禁止散落魔法值（同 <see cref="UiTheme"/> 的纪律）。
    ///
    /// 【可测性】只含常量 / 纯函数（<see cref="ContrastRatio"/> 是 WCAG 2.1 相对亮度比的
    /// 纯 C# 实现），不触碰 GameObject，可在无头验证台断言。
    /// </summary>
    public static class UiSkin
    {
        // ------------------------------------------------------------------
        // 底色系（【P1 令牌接管】色值整体换血为 StickTokens——stick-world ui_tokens.json
        // 的同源编译层，数值与 Godot 侧一致；UI 语义名全部保留，调用方零改动。
        // 映射：InkDeep=WINDOW_BG / InkSoft=WINDOW_BG_LIGHT / TextOnInk=TEXT /
        // TextDim=TEXT_DIM / Gold=ACCENT / InkOnGold=INK / BarTrackInk=GROOVE_BG /
        // Info·Warn·Success=INFO·WARN·SUCCESS。窗底半透明是隔壁「窗户不是海报」
        // 设计语言；WCAG 标注值按 tokens RGB 直算（与 Godot 侧同口径）。
        // ------------------------------------------------------------------

        /// <summary>HUD / 卡片面板底。UI 语义 = StickTokens.WINDOW_BG（纯黑高不透明
        /// "窗户"底，透 12% 画面）。正文 <see cref="TextOnInk"/>(=TEXT) 压其上 18.0:1、
        /// 强调 <see cref="Gold"/>(=ACCENT) 10.6:1（WCAG 计算见 <see cref="ContrastRatio"/>）。</summary>
        public static readonly Color InkDeep = StickTokens.WINDOW_BG;

        /// <summary>面板底的次级档（嵌套小容器 / 选中行底）。UI 语义 = StickTokens.WINDOW_BG_LIGHT
        /// （隔壁"次窗体底"：HUD 横条/内嵌区块）。正文其上 17.7:1。</summary>
        public static readonly Color InkSoft = StickTokens.WINDOW_BG_LIGHT;

        /// <summary>深底上的正文。UI 语义 = StickTokens.TEXT（压 InkDeep 18.0:1）。</summary>
        public static readonly Color TextOnInk = StickTokens.TEXT;

        /// <summary>深底上的次级文字。UI 语义 = StickTokens.TEXT_DIM（白 55% 透明档；
        /// 混入窗底后的有效对比 5.6:1 ≥4.5，提示/角标用）。</summary>
        public static readonly Color TextDim = StickTokens.TEXT_DIM;

        /// <summary>全局强调色（标题 / 选中态 / 星级）。UI 语义 = StickTokens.ACCENT
        /// （琥珀火光；压 InkDeep 10.6:1）。</summary>
        public static readonly Color Gold = StickTokens.ACCENT;

        /// <summary>强调底上的深字（主按钮 / 选中 chip 的文字）。UI 语义 = StickTokens.INK
        /// （隔壁深墨：亮底字色同族；压 Gold(=ACCENT) 10.2:1。原 InkOnGold 同源收编，
        /// 消灭 BattleHud 与 BattleUiTheme.Tok 的两份拷贝）。</summary>
        public static readonly Color InkOnGold = StickTokens.INK;

        /// <summary>血条 / 凹槽底。UI 语义 = StickTokens.GROOVE_BG（隔壁"输入框/凹槽底"
        /// 纯黑 45%）；非文字元素，3:1 即可达标。</summary>
        public static readonly Color BarTrackInk = StickTokens.GROOVE_BG;

        /// <summary>血条受击残影（damage ghost 的白条，乘在队伍色下层慢慢追平）。</summary>
        public static readonly Color DamageGhost = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>阵亡单位 pip / 死亡标识（骨灰 #6E6A60，非靛蓝系保留原值；压窗底 3.8:1——
        /// 非文字图形 ≥3:1）。</summary>
        public static readonly Color DeadGray = Rgb(0x6E, 0x6A, 0x60);

        /// <summary>危险动作底（像素皮取 <see cref="PixelTone.Danger"/> 的暗档；图集缺失回落原深酒红）。</summary>
        public static Color Danger => PixelSkin.Asset != null
            ? (Color)PixelSkin.DarkOf(PixelTone.Danger)
            : Rgb(0x8A, 0x1F, 0x1F);

        /// <summary>语义色·信息。UI 语义 = StickTokens.INFO（压 InkDeep 11.5:1；Toast/通知/链接）。</summary>
        public static readonly Color Info = StickTokens.INFO;

        /// <summary>语义色·警告。UI 语义 = StickTokens.WARN（压 InkDeep 14.0:1；资源不足/警告通知）。</summary>
        public static readonly Color Warn = StickTokens.WARN;

        /// <summary>语义色·成功。UI 语义 = StickTokens.SUCCESS（压 InkDeep 10.4:1；完工/增益）。</summary>
        public static readonly Color Success = StickTokens.SUCCESS;

        // ------------------------------------------------------------------
        // 像素皮（Beveled Pixel）取色桥：像素件一律经 <see cref="PixelSkin"/> 取件/取色，
        // 本段只是把 tone 语义映射成"UI 令牌名"口径，便于调用点不必同时 import 两个类。
        //
        // 【为什么上方底色系字段没有一并改写】InkDeep / InkSoft / TextOnInk / TextDim /
        // Gold / InkOnGold / BarTrackInk / Info / Warn / Success 十个成员被
        // Tests/UI/UiSkinTests.cs 的 PanelBaseColors_AreStickTokensPipelined 钉死在
        // StickTokens 上——该测试文件不在本波文件域，故本次不动它们（值不变，测试仍绿）。
        // 像素皮新代码请直接取本段的桥接取色，或直接用 <see cref="PixelSkin"/> 的 API。
        // ------------------------------------------------------------------

        /// <summary>像素皮某 tone 的 Plate 底色（= 该 tone 的中档）。</summary>
        public static Color PixelPlate(PixelTone tone) => PixelSkin.MidOf(tone);

        /// <summary>像素皮某 tone 底上的正文字色（浅底给墨字 / 深底给本 tone 亮档字）。</summary>
        public static Color PixelTextOn(PixelTone tone) => PixelSkin.TextColorOn(tone);

        /// <summary>像素皮暖白（深底上最亮的正文色）。</summary>
        public static Color PixelPaperWhite => PixelSkin.PaperWhite;

        // ---- 队色 / 队名文字（pirate 玩法语义色，不随 P1 令牌接管换血；收编 BattleHud 的两份写死色） ----

        /// <summary>红队主色（血条段 / 徽章环 / 点位；图形用，非文字；压窗底 5.8:1）。</summary>
        public static readonly Color TeamRed = Rgb(0xFF, 0x3A, 0x29);

        /// <summary>蓝队主色（同上；蓝系但非旧"夜海蓝"靛蓝主色 #1C2333/#2A3350——
        /// 队色对抗语义保留不换，压窗底 4.4:1 图形达标）。</summary>
        public static readonly Color TeamBlue = Rgb(0x33, 0x66, 0xFF);

        /// <summary>红队**文字**色（#FF8A7A：TeamRed 直接做字在旧靛蓝底只有 4.4:1 的提亮档，
        /// 压新窗底 9.0:1 ≥4.5——r7 裁决沿用，只是搬家）。</summary>
        public static readonly Color TeamRedText = Rgb(0xFF, 0x8A, 0x7A);

        /// <summary>蓝队**文字**色（#7FB0FF，压窗底 9.4:1）。</summary>
        public static readonly Color TeamBlueText = Rgb(0x7F, 0xB0, 0xFF);

        /// <summary>队伍色（teamIndex 0=红，其余蓝）。文字场景请用 <see cref="TeamText"/>。</summary>
        public static Color TeamFill(int teamIndex) => teamIndex == 0 ? TeamRed : TeamBlue;

        /// <summary>队伍**文字**色（深底上 ≥4.5:1 的提亮档）。</summary>
        public static Color TeamText(int teamIndex) => teamIndex == 0 ? TeamRedText : TeamBlueText;

        // ------------------------------------------------------------------
        // 形状 / 间距（防"驾驭不住"：圆角与描边都收着来，参数全 Token 化，样板验收后可整体调档）
        // ------------------------------------------------------------------

        /// <summary>
        /// 【档位纪律（对齐 game-2 StickTokens 的分档思路，2026-09-19 用户"风格不统一"裁决后收口）】
        ///   · 圆角只有两档：容器 <see cref="RadiusPanel"/>=8（面板/大格/小地图）、控件
        ///     <see cref="RadiusChip"/>=6（按钮/chip/提示条）；全圆只许出现在 Pill 血条与圆钮
        ///     （Ring/Circle 贴图）——此前 Slot 独立 10px 档已并入 8，消灭"四档混用"；
        ///   · 底色只有三档语义：InkDeep=独立容器底 / InkSoft=容器内嵌件与浮空控件底 /
        ///     BarTrackInk=有填充压顶的凹槽底（血条族专用）；同屏近黑底不得再有第四种；
        ///   · 强调色只有 <see cref="Gold"/> 一档（主行动按钮 / 选中态 / 星级）——队色与
        ///     武器 / 职业色是内容色，只上图形与图标，不上操作按钮底（红蓝队徽章环除外）。
        /// </summary>

        /// <summary>面板 / 大图标格 / 小地图圆角（px）——"容器"档。</summary>
        public const float RadiusPanel = 8f;

        /// <summary>按钮 / chip / 提示条圆角——"控件"档。</summary>
        public const float RadiusChip = 6f;

        /// <summary>图标格（武器槽 / pip）圆角 = 容器档（原独立 10px 档已并入）。</summary>
        public const float RadiusSlot = RadiusPanel;

        /// <summary>可选细描边宽（tintable 皮肤烘在贴图里；0 = 不要描边的件）。</summary>
        public const float StrokeThin = 1f;

        /// <summary>外安全边距（沿用 HUD 紧凑口径 16）。</summary>
        public const float Safe = 16f;

        /// <summary>面板内边距。</summary>
        public const float PanelPadding = 14f;

        /// <summary>通用件间距。</summary>
        public const float Gap = 12f;

        /// <summary>按钮按压位移（px，向下"吃进"一点点，卡通手感）。</summary>
        public const float PressSinkPixels = 2f;

        /// <summary>按钮四态过渡时长（与旧 RuntimeUiBuilder/MenuUiBuilder 同档，收编双份）。</summary>
        public const float ButtonFadeSeconds = 0.09f;

        // ------------------------------------------------------------------
        // 字号（全项目唯一真值。2026-09-23 起切**像素字体栅格**：FusionPixel 12px 位图档
        // 的字形按 12 设计像素一格，显示字号必须是 12 的整数倍——非整数倍会把一格字形
        // 拉宽出半格（口径见 PixelShowcasePage 类头与交接-2026-09-23 §二）。本表单位 =
        // 1080p 画布像素；1 艺术像素 = PixelSkin.Unit(3) 画布像素，正文 36 = 12 艺术像素。
        // 字号跟控件走的纪律不变：按钮高 = 正文 + 12（艺术像素），见 <see cref="Px"/>。
        // ------------------------------------------------------------------

        /// <summary>字号档位（画布像素）。旧 UiTheme.Font* / MenuUiBuilder.FontScale
        /// 已并入本表（别名或同值），勿再新增第三份字号表。
        /// 【满精度阶梯（创始人祈使裁决 2026-09-23）】**不同大小 = 不同精度的字体**：
        /// 每个显示字号必须由**原生设计尺寸恰好等于该字号 ÷ 3** 的位图字体渲染
        /// （1 字形像素 = 1 艺术像素 = 3 屏幕像素），绝不跨档缩放同一字体——
        /// 36 = FusionPixel 12px 原生档（正文/标题），30 = ArkPixel 10px 原生档（小字/角标）。
        /// 字体与字号的绑定由 <see cref="UiKit.ResolvePixelFont"/> 在文本出口单点强制。</summary>
        public static class Font
        {
            /// <summary>主菜单游戏名 / 界面标题 / 结算横幅（FusionPixel 12px 原生档；
            /// 更大的标题档待 16px/24px 原生像素字体引入——16px 官方归档只有字形源，见待办）。</summary>
            public const int Display = 36;

            /// <summary>结算横幅（胜利 / 失败；= 原生 12px 档）。</summary>
            public const int Banner = 36;

            /// <summary>界面标题（= 原生 12px 档）。</summary>
            public const int Title = 36;

            /// <summary>区块标题 / 面板标题条（= 原生 12px 档）。</summary>
            public const int Section = 36;

            /// <summary>HUD 常读 / 名册名 / 模式开关 / 回合计时（= 原生 12px 档）。</summary>
            public const int Hud = 36;

            /// <summary>按钮 / 列表行文本 / 说明（FusionPixel 12px 原生档，1 字形像素 = 1 艺术像素）。</summary>
            public const int Body = 36;

            /// <summary>辅助提示 / 通栏提示条（ArkPixel 10px 原生档，30 = 10 艺术像素）。</summary>
            public const int Hint = 30;

            /// <summary>角标 / HP 数字 / 快捷键角标（ArkPixel 10px 原生档）。</summary>
            public const int Tiny = 30;
        }

        // ------------------------------------------------------------------
        // 像素令牌（Beveled Pixel 几何档，画布像素 = 艺术像素 × PixelSkin.Unit）。
        // 令牌表出处：交接-2026-09-23-像素UI实机验证.md §二（创始人走查定档）——
        // 正文 12 / 条高 24 / 按钮高 = 文字高 + 12 / 按钮宽 = 标签宽 + 24 / 内边距 12 /
        // 位点 12 / 环 24 / 头像格 48 / 小地图 144（艺术像素）。
        // ------------------------------------------------------------------

        /// <summary>像素几何令牌。可见包边件的尺寸必须落在 <see cref="PixelSkin.Unit"/> 整数倍上，
        /// 本表全部取自艺术像素 × 3，天然合规。</summary>
        public static class Px
        {
            /// <summary>1 艺术像素 = 3 画布像素（与 3D 像素化渲染颗粒度 1:1）。</summary>
            public const int Unit = PixelSkin.Unit;

            /// <summary>标准条高（凹槽 24 艺术像素 = 6 框 + 12 填 + 6 框）。</summary>
            public const int Bar = 24 * Unit;

            /// <summary>标准按钮高（= 正文字高 12 + 上下各 6 艺术像素）。</summary>
            public const int Button = 24 * Unit;

            /// <summary>按钮左右内边距合计（= 24 艺术像素；按钮宽 = 标签宽 + 本值）。</summary>
            public const int ButtonPadX = 24 * Unit;

            /// <summary>面板内边距（12 艺术像素）。</summary>
            public const int PanelPadding = 12 * Unit;

            /// <summary>位点 / 分隔间距（12 艺术像素）。</summary>
            public const int Pip = 12 * Unit;

            /// <summary>选人圈 / 焦点框边长（24 艺术像素）。</summary>
            public const int Ring = 24 * Unit;

            /// <summary>头像格边长（48 艺术像素）。</summary>
            public const int Avatar = 48 * Unit;

            /// <summary>小地图边长基准（144 艺术像素）。</summary>
            public const int Minimap = 144 * Unit;

            /// <summary>
            /// 按钮宽 = 标签宽 + <see cref="ButtonPadX"/>（令牌"按钮宽 = 标签宽 + 24"）。
            /// 中文标签按正文字宽逐字计（Fusion Pixel 12px 比例版的全宽 CJK 字形 = 正文字号）；
            /// 项目 UI 字符串全为中文，按字符数计与 TMP 排版一致且无头可测。下限 = 两字钮。
            /// </summary>
            public static int ButtonWidth(string label)
            {
                int labelWidth = (label?.Length ?? 0) * Font.Body;
                return Mathf.Max(2 * Font.Body, labelWidth) + ButtonPadX;
            }
        }

        // ------------------------------------------------------------------
        // 语义色槽：武器（17）——图标格底色与静物主色共用一套，UI 与图标天然同色系
        // ------------------------------------------------------------------

        /// <summary>
        /// 武器语义色（高饱和、彼此可分；同时是 <c>IconBakeTool</c> 烘焙武器静物的主色——
        /// 图标底 chip 与静物同色系，是"多彩"观感的核心来源之一）。
        /// </summary>
        public static Color WeaponColor(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Cannonball: return Rgb(0x5A, 0x6B, 0x7F);   // 铁蓝灰
                case WeaponId.CherryBomb: return Rgb(0xE2, 0x3B, 0x3B);   // 樱桃红
                case WeaponId.Dynamite: return Rgb(0xC9, 0x4F, 0x3D);     // 警示砖红
                case WeaponId.Boulder: return Rgb(0x9C, 0x7A, 0x5A);      // 岩棕
                case WeaponId.Banana: return Rgb(0xF0, 0xD0, 0x48);       // 香蕉黄
                case WeaponId.Mine: return Rgb(0x3E, 0x5C, 0x8C);         // 钢蓝（武器色系中最贴近旧靛蓝主色的一档——水雷玩法语义保留不换，仅标记）
                case WeaponId.ParachuteBomb: return Rgb(0x58, 0xA8, 0xE0); // 伞天蓝
                case WeaponId.RumBottle: return Rgb(0x4E, 0x8C, 0x5A);    // 瓶绿
                case WeaponId.PiecesOfEight: return Rgb(0xF2, 0xC1, 0x4E); // 金币金
                case WeaponId.GunpowderBarrel: return Rgb(0x8A, 0x5A, 0x2E); // 深木棕
                case WeaponId.WoodenCrate: return Rgb(0xC8, 0x9A, 0x52);  // 浅木黄
                case WeaponId.Anchor: return Rgb(0x6E, 0x87, 0xA6);       // 锚铁蓝（略提蓝：饱和度 ≥0.28 的多彩下限）
                case WeaponId.Seagull: return Rgb(0x9F, 0xC3, 0xE8);      // 云羽蓝
                case WeaponId.TidalWave: return Rgb(0x2F, 0xA8, 0xC9);    // 潮青
                case WeaponId.VoodooDoll: return Rgb(0x9A, 0x5F, 0xD0);   // 巫毒紫
                case WeaponId.Cannon: return Rgb(0xC9, 0x96, 0x2E);       // 黄铜
                case WeaponId.SweepingFlame: return Rgb(0xF0, 0x70, 0x30); // 烈焰橙
                default: return Rgb(0x8A, 0x97, 0xA8);
            }
        }

        // ------------------------------------------------------------------
        // 语义色槽：职业（7 档，与 CrewVisualCatalog.DisplayName 的中文名一一对应）
        // ------------------------------------------------------------------

        /// <summary>
        /// 战斗符号（PirateBase.CrewType，如 <c>redPirate</c>/<c>blueGunner</c>）→ 职业短名
        /// （sailor/gunner/sniper/hooker/arsonist/skeleton/captain）。职业色与图标资产
        /// （<c>Resources/UIIcons/Crew_&lt;短名&gt;</c>）都以短名为键——符号带队伍前后缀
        /// 的全部归一到同档职业。
        ///
        /// 【与 3D 外观侧同源】映射规则对齐 <c>CrewVisualCatalog.ProfessionFromBattleSymbol</c>
        /// （§4.2 导出符号 27 个全覆盖：cabinBoy→sniper、soldier→gunner、blindPirate→hooker、
        /// oldPirate/rainbowBeard→arsonist、skeletonPirate→skeleton、Captain 后缀与 bossGuy→captain、
        /// tribe→sailor、无法识别回落 sailor）——r9 出图事故：本表曾用更窄的符号命名空间，
        /// 蓝队 cabinBoy 落 unknown → 钢灰底+舵轮占位，而 3D 模型却解析正确，两侧各说各话。
        /// </summary>
        public static string CrewKey(string crewType)
        {
            if (string.IsNullOrEmpty(crewType))
                return "sailor";

            // 船长变体优先（redPirateCaptain / cabinBoyCaptain / bossGuy...）。
            if (crewType.IndexOf("Captain", System.StringComparison.Ordinal) >= 0)
                return "captain";

            switch (crewType)
            {
                case "redPirate":
                case "bluePirate":
                case "sailor":
                case "redSailor":
                case "blueSailor":
                case "tribe":
                case "tribeChief":
                    return "sailor";
                case "gunner":
                case "redGunner":
                case "blueGunner":
                case "soldier":
                    return "gunner";
                case "sniper":
                case "redSniper":
                case "blueSniper":
                case "cabinBoy":
                case "femalePirate":
                    return "sniper";
                case "hooker":
                case "redHooker":
                case "blueHooker":
                case "blindPirate":
                    return "hooker";
                case "arsonist":
                case "redArsonist":
                case "blueArsonist":
                case "oldPirate":
                case "rainbowBeard":
                    return "arsonist";
                case "skeleton":
                case "redSkeleton":
                case "blueSkeleton":
                case "skeletonPirate":
                    return "skeleton";
                case "bossGuy":
                case "bossGuyZombie":
                    return "captain";
                default:
                    return "sailor";   // 与外观侧同回落（ProfessionFromBattleSymbol default→Sailor）
            }
        }

        /// <summary>职业语义色（pips / 头像环 / 武器面板头条）。键 = 战斗符号（PirateBase.CrewType）。</summary>
        public static Color CrewColor(string crewType)
        {
            switch (CrewKey(crewType))
            {
                case "sailor": return Rgb(0x4F, 0xA3, 0xD9);       // 水手·天蓝
                case "gunner": return Rgb(0xE8, 0x84, 0x2E);       // 炮手·橙
                case "sniper": return Rgb(0x3F, 0xBF, 0xA8);       // 狙击手·青
                case "hooker": return Rgb(0xB0, 0x9A, 0x6A);       // 钩子手·黄铜
                case "arsonist": return Rgb(0xE0, 0x52, 0x38);     // 纵火狂·火红
                case "skeleton": return Rgb(0xD9, 0xD4, 0xC5);     // 骷髅·骨白
                case "captain": return Rgb(0xF2, 0xC1, 0x4E);      // 船长·金
                default: return Rgb(0x8A, 0x97, 0xA8);             // 未知·钢灰
            }
        }

        /// <summary>格底暗档系数：内容色格底向 <see cref="InkDeep"/> 压 22%——
        /// "深底让彩色跳出来"在格子尺度上的应用（静物/头像保持全彩，图底不再同色相融，
        /// r9 出图裁决：铁球贴灰蓝底 / 香蕉贴金底近隐身）。装配与运行时刷新同源调用。
        /// （InkDeep 已接管为半透明窗底，Lerp 后格底 α≈0.97，视觉近实底。）</summary>
        public static Color CellBase(Color contentColor)
        {
            return Color.Lerp(contentColor, InkDeep, 0.22f);
        }

        /// <summary>武器格底（<see cref="WeaponColor"/> 的暗档）。</summary>
        public static Color WeaponCellBase(WeaponId id) => CellBase(WeaponColor(id));

        // ------------------------------------------------------------------
        // 工具（纯函数，可无头断言）
        // ------------------------------------------------------------------

        /// <summary>0xRRGGBB → 不透明 Color。</summary>
        public static Color Rgb(int hex)
        {
            return new Color(
                ((hex >> 16) & 0xFF) / 255f,
                ((hex >> 8) & 0xFF) / 255f,
                (hex & 0xFF) / 255f,
                1f);
        }

        /// <summary>分通道 0xRR,0xGG,0xBB → 不透明 Color。</summary>
        public static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        /// <summary>整体乘 alpha（保留 RGB）。</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>sRGB 通道 → 线性亮度（WCAG 2.1 分段线性化）。</summary>
        public static float ChannelToLinear(float channel01)
        {
            return channel01 <= 0.04045f
                ? channel01 / 12.92f
                : Mathf.Pow((channel01 + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>WCAG 2.1 相对亮度（0..1）。</summary>
        public static float RelativeLuminance(Color color)
        {
            return 0.2126f * ChannelToLinear(color.r)
                 + 0.7152f * ChannelToLinear(color.g)
                 + 0.0722f * ChannelToLinear(color.b);
        }

        /// <summary>WCAG 2.1 对比度（≥4.5 = 正文达标；≥3 = 大字 / 非文字图形达标）。
        /// 本类各色对的标注值全部由它算出，测试断言同源。</summary>
        public static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float lighter = Mathf.Max(la, lb);
            float darker = Mathf.Min(la, lb);
            return (lighter + 0.05f) / (darker + 0.05f);
        }
    }
}
