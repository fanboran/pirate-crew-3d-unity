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
    /// 详见 docs/项目/交接/归档/UI系统重构-进度.md。
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

        /// <summary>顶部带垂直中心**距屏顶**的像素（血条的锚顶偏移）。</summary>
        public const float TopBandFromTop = 8f;

        // ---------------- 顶栏：双队血条 + pips（theme 平涂） ----------------

        /// <summary>队血条**标准段长**（每单位一段，段宽即此值）：以原固定条宽的三分之一
        /// 为一个标准血条，几个单位就是几段加起来。条总长随编成人数伸缩
        /// = 两端 inset + n×段长 + (n-1)×段缝；装配期按上限段数预排骨架、运行时按实际
        /// 人数重排——Builder 与 <c>BattleHud.TeamBars</c> 两端同式，常量同源
        /// （运行时侧同值在 TeamBars.SegmentLength），改一处必改两处。</summary>
        public const float TeamBarSegmentLength = 33f;

        /// <summary>队血条**基准宽**（分段伸缩前的固定条宽）：血条本体不再恒宽，
        /// 现仅作状态指示钮的对齐基准（与其右缘同列的口径锚）。</summary>
        public const float TeamBarBaseWidth = 99f;
        public const float TeamBarHeight = 8f;

        /// <summary>段间距 / 段区两端内边距（与 BattleHud.SegmentGap/SegmentInset 同源=1：
        /// 平涂槽 1 格缝即分段读感。段宽向下取整保平涂段边缘落整格）。</summary>
        public const float SegmentGap = 1f;
        public const float SegmentInset = 1f;

        /// <summary>红条右缘 / 蓝条左缘**距屏幕中轴**的像素（镜像等长）= 72（海图半宽）+ 8（缝）。
        /// 双条锚中轴、贴中央海图两侧，任意画布宽度下条与海图的缝恒 8；条长随人数伸缩时
        /// 固定缘不动、向各自外侧伸展（红条 pivot 右 / 蓝条 pivot 左）。</summary>
        public const float TeamBarFromCenter = 80f;

        /// <summary>pip 尺寸 / 间距（血条正下方一排队色方格，阵亡「×」+ 压暗）。</summary>
        public const float PipSize = 6f;
        public const float PipGap = 2f;

        /// <summary>回合徽章中心**距屏顶**的像素：海图（顶部正中）底缘 = Safe + MinimapHeight，
        /// 徽章中心再让 10 格缝 → 110；回合提示盘接其下 2 格。</summary>
        public const float TurnBadgeFromTop = 110f;

        /// <summary>回合徽章 = theme button_selected 金面钮（16×16 = 该件原生高 4+6+6，1:1 无拉伸；
        /// 数字 Tiny 8px 落在金面字区内——12 格档数字压描边穿模，2026-10-05 走查废弃）。</summary>
        public const float BadgeSize = 16f;

        /// <summary>状态指示钮（交互操作契约 §G4，2026-10-05 走查"我到底是哪个状态"）：
        /// 蓝队血条/pips 正下方右对齐的按钮皮指示件——自由镜头 = 常态灰面，选中/操作中/执行中 =
        /// 金面 sticky。宽按标签自适应（builder 内 FitToLabel），高 20 = HudButtonHeight；
        /// 距屏顶 34 = 顶带 8 + 血条 8 + 缝 6 + pips 6 + 缝 6。</summary>
        /// <summary>状态指示钮右缘距屏幕中轴 = TeamBarFromCenter + TeamBarBaseWidth（对齐
        /// 基准宽右缘；血条本体改按人数分段伸缩后不再跟随条长）。</summary>
        public const float StateIndicatorFromCenterX = TeamBarFromCenter + TeamBarBaseWidth;
        public const float StateIndicatorFromTop = 34f;
        public const float StateIndicatorHeight = 20f;

        /// <summary>操作 HUD 面板：与武器面板共用贴底位（互斥弹出，见 UI 设计语言 §5.4）。
        /// 宽 210 = 边框 6 + 读数区 198 + 边框 6（读数标签/值放宽，修 180 档「方向」二字贴边）；
        /// 高 64 = 内容顶 23 + 读数行 12 + 缝 2 + 按钮行 20 + 底边 6 + 1 余。</summary>
        public const float OperationPanelWidth = 210f;
        public const float OperationPanelHeight = 64f;

        /// <summary>操作读数行高（12 号字一行的 Hud 档）。</summary>
        public const float OperationReadoutRowHeight = 12f;

        /// <summary>操作提示裸字（交互操作契约 §G2）：屏幕**正右方贴边**的无底悬浮字——
        /// 静态两行常驻（「点击单位选中」「右键拖动旋转视角」），新玩家基础操作引导，
        /// 不随交互状态刷新。宽按最宽行（8 字 × Hud 字号）+ 余量，右对齐；高容两行。</summary>
        public const float SideHintFromRight = Safe;
        public const float SideHintWidth = 104f;
        public const float SideHintHeight = 32f;

        /// <summary>回合提示盘（「玩家 1，该你了 / 敌方行动中」）：徽章正下方墨底盘 + 白字——
        /// 裸白字压浅色岛面读作浅灰，半透明描边救不回对比度（21 走查裁决）。
        /// 宽按文案自适应（单行，运行时刷新），夹在 [Min, Max]。</summary>
        public const float TurnHintPlateMinWidth = 120f;
        public const float TurnHintPlateMaxWidth = 300f;
        public const float TurnHintPlateHeight = 24f;

        // ---------------- 底部带：武器面板（theme 窗体） ----------------

        /// <summary>武器面板：贴底**靠左**（锚左下，x = Safe）。窗体几何：边框 6 / 内容顶随标题
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

        /// <summary>小地图窗尺寸（136×102 = 4:3，与场地长宽比 40×30 一致——顶视图信箱留白最小化；
        /// 面积与旧 144×96 基本持平。居画布顶部正中，标题带「海图」）。</summary>
        public const float MinimapWidth = 136f;
        public const float MinimapHeight = 102f;
    }
}
