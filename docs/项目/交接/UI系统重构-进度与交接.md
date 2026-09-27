# UI 系统重构（2026-09-28 创始人令「系统且完整地重构整个UI系统」）——进度与交接

> 执行者：AI（架构自裁已获创始人授权：「你自己用着不舒服你自己承受，你觉得怎么设计好，
> 这都是你的脚手架」）。本档是跨会话恢复的唯一入口，每波收官即更新。

## 目标架构（裁决版）

**维持「代码工厂 + 装配脚本生成 Prefab」路线**（像素纪律——整格坐标/字号原生档/
件禁乘色/1px 特征线不拉伸——必须代码强制，Prefab 手摆与 UI Toolkit 都兜不住）。
重构不是换路线，是**消灭分裂与散落**，让每个概念只有一个真源：

| 概念 | 唯一真源（目标） | 现状病灶 |
| --- | --- | --- |
| 控件产线 | `Skin/UiKit.cs`（窗口/按钮/钮组/滑条/文本/条/模态/滚动视图） | SketchButton 系（Stick/）与 UiKit.ActionButton 双产线；设置面板还用 SketchButtonSet/SketchSlider |
| 字盒口径 | UiKit 单点（底 3/顶 1 校准，`8a54563f` 已收口 ActionButton） | SketchButton 自带同款——迁线时不得丢失 |
| 布局数字 | 每场景一张 zone 表（`BattleHudZones` 首块） | 散在各 builder 常量（高度档/整数除法/字盒分家三起事故的共性根因） |
| 皮肤出口 | `PixelSkin`：Ase 直切件 + Theme 色 + 平涂色块 | beveled tone 族（Plate/Track/Fill/Panel/Tab/Ring/Pip/Separator/Shadow）与 Ase 件并存，判据测试与 bake 代差漂移 |
| 调试 UI | `UI/Debug/` 只留面板与装载器，公共件上移 UiKit | DebugUi 自带 window kit 与 UiKit 重叠；12 个嵌套类存不进 Prefab（棘轮红） |

## 波次与状态

- [ ] **W1（并行三线）**
  - [ ] W1A：Debug 域嵌套 MonoBehaviour 清退成顶级同名文件（含 AseListbox.cs→
        AseListBox.cs 文件名大小写对齐，.meta 随迁保 GUID）→ 棘轮测试只剩存量白名单
  - [ ] W1B：`Assets/Editor/BattleHudZones.cs` 单一表 + BattleHudBuilder 只消费
        （**纯搬移，数字零变化**）
  - [ ] W1C：tools/headless/run.sh Unity 路径 `2022.3.62f1c1`→`62f1`（Scenes README/
        架构总览示例同修）+ EditorBuildSettings 回归断言（EditMode 测试绑
        BuildScenes 场景集）+ BuildScenes.cs 过期注释
  - [ ] 门：harness 双域 → 桥重装 → Battle/MainMenu 快拍 → 原子提交×3 → 合并主仓
- [ ] **W2（串行枢纽）控件产线合一**：UiKit 吸收 SketchButton（四态+字盒校准+禁用
      双层影子字）、SketchButtonSet（选项 chip）、SketchSlider（滑条）；SceneSetup
      菜单列/MainMenuController 设置面板迁线；RuntimeUiBuilder 并入或退役；
      Stick/Controls 死件删除；四场景重装配 + `+settings` 实拍回归
- [ ] **W3 beveled tone 族退役**：余下消费者（DebugWindowKit/PixelShowcasePage/
      MainMenuController 残件/SketchPanel）迁 Ase 件或平涂；PixelSkin tone API 与
      烘焙分支删除；BeveledPixelSkinTests 随族退场（三红销案）；展示页按 Ase 件重写
      或退役；四屏实拍回归
- [ ] **W4 收官**：全场景重装配 + 四屏 + 弹窗实拍全家福；主仓 EditMode 全量
      （目标：仅剩登记存量）；PlayMode 交创始人实机；字体图集 GUID/增量政策
      （FontAssetBuilder 重跑换 GUID 的存量登记项一并裁决）

## 铁律（每波通用）

- worktree `temp/ase-refactor`（分支 refactor/ase-ui-tighten），每波门后合并主仓；
- 多 subagent 并行**文件域互不重叠**，Unity 侧验证（桥/装配/拍摄）一律协调者串行；
- 每波收口跑 harness Runtime+DataEditor（直连命令；run.sh 修好前别信它的退出码）；
- 视觉验收走 `capture:<场景>[+overlay]` 旗标 + 放大目检；数字断言用像素丈量不靠目测；
- 原子提交中文规范；场景/Prefab 是装配产物，改生成器后必须重装配再提交；
- 提交前查 EditorBuildSettings/URP/Bootstrapper 写回噪声（四次前科）。

## 已知雷区登记（波次中遇到先记这里）

- 旧 GUI 编辑器长会话二次进 Play 会 NRE 洪水（CheckMatchOver），重启编辑器即愈，
  非代码 bug（2026-09-28 取证：干净域零复现）；
- 拍摄机 overlay 档现支持 settings/confirm/pause（`UiPixelScreenCapture.OverlayTargetName`）；
- 战斗武器格文字 10 号、行动行 12 号是层级裁决；按钮高 20、字盒底 3/顶 1 是令牌。
