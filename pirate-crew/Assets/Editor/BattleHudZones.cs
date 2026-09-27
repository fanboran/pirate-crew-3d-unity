using PirateCrew.UI;   // 仅 XML 注释：<see cref="UiSkin.Px.ButtonWidth"/>

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 布局常量单一真源（zone 表）：全部几何 / 摆位数字只此一处定义，
    /// <see cref="BattleHudBuilder"/> 只消费（引用点一律写 <c>BattleHudZones.Xxx</c>），
    /// builder 自身不再持有任何布局常量。
    ///
    /// 【为什么】历史上三起布局事故——按钮高度档错、格区行数整数除法、字盒分家——
    /// 共性根因都是"布局数字没有单一真源"：同一语义的数在多处各写一遍，改一处漏一处。
    /// 收敛成一张表后走"定位归表"：对齐关系在这里一次算清，部件不再各自手写坐标。
    /// 详见 docs/项目/交接/UI系统重构-进度与交接.md。
    ///
    /// 【纪律】**改数字必须只改这里**——任何部件里的坐标都应引用本表常量；表内改动
    /// 会在 <see cref="BattleHudBuilder"/> Build 末尾的防撞自检里暴露越界 / 相撞。
    /// </summary>
    public static class BattleHudZones
    {
        public const float Safe = 4f;   // HUD 几何边缘安全距（画布像素）

        // ------------------------------------------------------------------
        // HUD zone 表（对齐 game-2 hud_zone_layout 的"定位归表"思路——对齐关系在这里
        // 一次算清，部件不再各自手写坐标；越界/相撞由 Build 末尾的防撞自检兜底）。
        //
        // 尺寸纪律：可见包边件不低于九宫格切片和（theme button 上下切片和 = 10、
        // window 边框 3、sunken 边框 3），anchoredPosition 至少取整。
        // 文字尺寸不在纪律内（原生档四档，见 UiSkin.Font）。
        // ------------------------------------------------------------------

        /// <summary>顶部带垂直中心**距屏顶**的像素（血条/徽章的锚顶偏移）。</summary>
        public const float TopBandFromTop = 8f;

        // ---------------- 顶栏：双队血条 + pips（theme 平涂） ----------------

        /// <summary>队血条宽 / 高（红蓝镜像等长；段宽运行时按实际人数重排）。
        /// 平涂槽时代高度 8 = 槽底 8 格、段上下各让 1 格露槽 → 填充带 6 格。</summary>
        public const float TeamBarWidth = 99f;
        public const float TeamBarHeight = 8f;

        /// <summary>段间距 / 段区两端内边距（与 BattleHud.SegmentGap/SegmentInset 同源=1：
        /// 平涂槽 1 格缝即分段读感。段宽向下取整保平涂段边缘落整格）。</summary>
        public const float SegmentGap = 1f;
        public const float SegmentInset = 1f;

        /// <summary>红条左端 = 小地图右缘 + 缝；蓝条右端镜像同值。
        /// 海图加大到 144 宽（2026-09-28 创始人「面积为什么这么小」）→ 4 + 144 + 8。</summary>
        public const float TeamBarInsetX = 156f;

        /// <summary>pip 尺寸 / 间距（血条正下方一排队色方格，阵亡「×」+ 压暗）。</summary>
        public const float PipSize = 6f;
        public const float PipGap = 2f;

        /// <summary>回合徽章 = theme button_selected 金面钮（12×12，button 切片和 10 的最小安全档）；
        /// 数字用 Tiny 档（两位数回合号在 12 格内不溢）。</summary>
        public const float BadgeSize = 12f;

        /// <summary>模式钮行：血条带下方独立一行（与蓝条 x 区段重叠，靠 y 错层避撞）。</summary>
        public const float ModeButtonDrop = 16f;

        /// <summary>模式文字钮尺寸（宽 = <see cref="UiSkin.Px.ButtonWidth"/>）。
        /// 高 20（2026-09-28 创始人「垂直方向按钮太小」：12 号字上下各留 4 格，
        /// 不再贴 theme 原生 16 的紧凑档）。</summary>
        public const float ModeButtonHeight = 20f;

        // ---------------- 底部带：武器面板（theme 窗体） ----------------

        /// <summary>武器面板：贴底居中（bottom = Safe）。窗体几何：边框 6 / 内容顶随标题
        /// 字高（12 号 → 23）。宽 406 = 6 + 格区 394 + 6；高 124 = 内容顶 23 + 格区 58 +
        /// 缝 2 + 行动行 20 + 缝 2 + 信息行 12 + 底边 6 + 1 余。</summary>
        public const float WeaponPanelWidth = 406f;
        public const float WeaponPanelHeight = 124f;

        /// <summary>武器文字格尺寸 / 间距 / 列数（6×3 = 18 格，17 武器 + 1 空）。
        /// 格宽 64 = 最长武器名「降落伞炸弹」5 字 × 10 原生档 = 50 + 余量；格高 18 =
        /// 10 号字上下各留 4 格（文字降一号建层级，2026-09-28 创始人令——全 12 号
        /// 「整个面板文字都一样大」）。</summary>
        public const float WeaponCell = 64f;
        public const float WeaponCellHeight = 18f;
        public const float WeaponCellGap = 2f;
        public const int WeaponColumns = 6;

        /// <summary>HUD 按钮高（20：12 号字上下各留 4 格——2026-09-28 创始人
        /// 「垂直方向按钮大小太小」，theme 原生 16 装 12 号字只剩 2 格余量太挤）。</summary>
        public const float HudButtonHeight = 20f;

        /// <summary>小地图窗尺寸（2026-09-28 创始人「面积为什么这么小」：64×44 太小，
        /// 提到 144×96 占角面积；内容仍为清空暂态）。</summary>
        public const float MinimapWidth = 144f;
        public const float MinimapHeight = 96f;
    }
}
