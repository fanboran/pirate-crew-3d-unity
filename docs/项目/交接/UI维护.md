# UI 维护

> 一句话现状：通用控件已从调试目录抽到 `UI/Controls/`，菜单侧浮层栈（Esc 统一关闭）与列表滚动已接入，
> 三屏 Prefab 待重烘、实机待验收；世界空间 UI 与图标管线未开工。

## 详情

### 分支与工作台

- 分支 `feat/ui-maintain`，工作台 `temp/ui-maintain`，从 `main@eb806c0c` 切出。
- 主仓未提交的「设置页渲染风格切换」改动**不并入本分支**（创始人 2026-11-22 裁决：留在主仓）。

### 已落地（代码编译通过，未实机）

1. **控件库抽离**：`AseUi / AseEntry / AseIntEntry / AseListBox / AseListItem / AseView /
   AseViewWheel / AseScrollBar / AseComboBox / AseComboBoxArrow / AseComboBoxPopup /
   AseCheckBoxFace / HoverTooltip / AseWidgetKit` 从 `UI/Debug/` 迁到 `UI/Controls/`，
   命名空间改为 `PirateCrew.UI.Controls`。文件 GUID 保持不变（`git mv` 带 `.meta`）。
2. **依赖反转**：新增 `AseText`（字体/文本/字高唯一出口），调试窗体壳 `DebugWindowKit`
   反向委托；新增 `AsePopupScope`（同屏只留一层浮层的登记处），菜单会话与组合框弹层登记/反注册。
3. **菜单浮层栈**：新增 `UiModalStack`（`UiModalStack.Ensure` 运行时挂到画布）。
   主菜单设置/退出确认、选关结算已接入；Esc 关闭栈顶、设置关闭统一落盘。
   战斗内 Esc 保持 `InteractionRules.ResolveEsc` 矩阵，不并入本栈（避免两处消费）。
4. **列表滚动**：新增 `UiScrollList`（`AseView` 内核 + 行产线）。选关/船员管理列表窗体的
   内容区已换成滚动版；行数超过窗体高时右侧才出现 theme 滚动条。
5. **世界锚点**：新增 `WorldUiAnchor`（世界点 → 屏幕画布投影、整数像素落位、画布内钳制、
   相机背后自动隐藏）。**刻意不用 World Space Canvas**——像素 UI 依赖恒定像素密度的屏幕画布，
   世界画布会随相机缩放破格。头顶面板本身尚未接线（等下一批连同 Battle 重烘一起做）。
6. **文档**：`docs/技术/架构/架构总览.md` §8.1 的控件层落点与新公共件已同步。

### 系统性优化（2026-11-22）

四路网络调研（游戏 UI 架构 / UX 与 HUD / 像素 UI / Unity UGUI 工程实践）＋ 本地代码、
对比度与 2×/3× 实拍审计的结论与分级见
[审计/UI系统性审计-2026-11-22.md](../审计/UI系统性审计-2026-11-22.md)。已落地：

- 设置页手动档收敛为「2× / 自动」（3×/4× 在 1080p 会把画布缩到设置卡装不下）；
  自动档仍按屏幕高解析 3/4，旧档读入归一 2×。
- 新增 `UiSkin.HintText`（压窗底 4.75:1），生产提示/状态字全替换并加对比度契约测试。
- `UiModalStack` 焦点账本：打开清下层选中、关闭恢复触发按钮。
- 新增 `UiKeyboardFocus`：菜单初始焦点 + Tab/Shift+Tab 视觉序循环，模态内循环。

下一步（审计档 P0）：队伍色盲冗余（形状/文字）、分辨率切换倒计时还原、
战斗 Enter 误触、列表行键盘焦点表达。

### 待办

- [ ] 重烘三屏 Prefab：`SceneSetup.BuildAll`（主菜单）+ `ManagementSceneSetup.BuildAll`
      （船员管理/选关）。**生成器改了必须重装配**，否则场景里还是旧 UI。
- [ ] 实机验收：①选关列表超过 10 行时可滚动（滚动条/滚轮）②主菜单设置 Esc 关闭并落盘
      ③退出确认 Esc 关闭 ④选关结算 Esc 关闭。
- [ ] 世界空间 UI：`WorldUiAnchor` 已落（组件层），还差头顶面板本体与接线——
      两个占位图标钮（跳跃/结束本回合）、选中变化时 SetTarget、Battle 重烘。
      镜头策略另见 [待办/选中面板与镜头策略.md](../待办/选中面板与镜头策略.md)。
- [ ] 屏幕宿主：按名字注册控件，取代各屏近 30 字段的 refs 结构（先主菜单试点）。
- [ ] 图标管线：需创始人提供绘图 AI 产出的样图；本分支负责去背/调色板量化/12–16px
      下采样/对齐/注册表/预览工具。无图源前不开工。
- [ ] 命名空间与目录对齐（纯改名）：`UI/Skin → .UI.Skin`、`UI/Debug → .UI.DebugUi` 等
      六组，牵 68+ 文件 using；等控件迁移稳定后一次做。

### 恢复顺序

1. 读本档 + `docs/技术/架构/架构总览.md` §8.1。
2. `git log --oneline -8` 对齐；`git status` 应净。
3. 先跑重烘（见上），再实机走查；世界空间 UI 从 `BattleHud` 的选中状态入口接。
