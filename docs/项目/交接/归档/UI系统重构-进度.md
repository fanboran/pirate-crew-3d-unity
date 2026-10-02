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

- [x] **W1（并行三线，2026-09-28 完结，ac1e8774/14559476/c911b52f，merge 1aac3e43）**
  - [x] W1A：Debug 域 12 个嵌套/异名 MonoBehaviour 清退（五嵌套 + 六异名 +
        AseListbox→AseListBox 改名保 GUID；逐字节校验搬移；棘轮红转绿）
  - [x] W1B：`BattleHudZones` 单一表（21 常量纯搬移零数值变化，67 处引用全名化；
        遗留观察：WeaponPanelHeight 手加总含三处表外依赖——派生量表达式化待后续裁决）
  - [x] W1C：**场景表四连砍根因破案**——`BattleSceneSetup.RegisterBuildSettings` 残留
        5 场景硬编码（战斗装配链每跑必摘 UIShowcase）；三装配器统一走
        `BuildScenes.EditorRegistrationScenes()` 单一真源 + `BuildScenesContractTests`
        回归钉死；run.sh/Scenes README/架构总览 Unity 路径 62f1c1→62f1
  - 门：harness 双域 0 错；主仓 EditMode **1311/1315**（仅剩 BeveledPixel 三红 = W3 靶）
- [x] **W2（控件产线合一，2026-09-28 完结，a25daea6，merge ff263d5d）**
  - SketchButton 状态引擎整族（AseButtonBase/SketchButton/Set/SetIcon/Slider 五件，
    git mv 保 GUID）上移 `Skin/`，**命名空间不动**（消灭分家靠目录+工厂单点，
    不做命名空间化妆）；Stick/Controls 余件加退役注记
  - `UiKit.ActionButton` 薄壳化 = `SketchButton.Create`——游戏内 **31 枚按钮全部变成
    主菜单同款元素**（theme 四态/逐态字色/禁用双层字/同款字盒），创始人
    「游戏内没完全用主菜单 UI 元素、别生造新结构」批评的直接回应
  - 战斗运行侧选中态迁 `SketchButton.Sticky`；**模式钮 1/2/3 角标删除**（创始人令，
    键盘 1/2/3 功能保留）；四场景重装配（BattleRig 实测 31×SketchButton GUID）
  - 验收：菜单/战斗按钮放大比对同源（灰面/近黑 1px 边/直角/浅灰字/居中对称/贴合宽）；
    装配链跑完 EditorBuildSettings **首次零被动**（W1C 根因修复实战验证）
- [x] **W3（tone 族退役，2026-09-28 完结，6a50654a，merge 8fe3cbe5）**
  - 展示页旧 tone 演示退役（PartsGalleryPage 独占展示场景，PixelFont 出口迁移）；
  - 设置面板：四个选项块本就已是 SketchButtonSet（侦察误报），SetChipSelected 的
    Plate 死分支删除；设置卡 SketchPanel→UiKit.EnsureWindow（Ase 窗体皮，实拍四项过）
  - SketchPanel 三用户（ManagementSceneSetup 结算卡/列表窗、MenuUiBuilder 设置卡、
    DebugWindowKit 双窗）迁 UiKit；**无标题面板皮裁决 = Ase "menu" 件**（border 3，
    与调试窗旧口径同源）；SketchPanel 删除
  - 族亡：106 张 tone PNG + PixelSkin 九出口 + UiKit tone 工厂 + 烘焙分支（-1568 行）
    + BeveledPixelSkinTests（三红闭案）删除；FocusRing 迁 check_focus；tone **取色 API
    保留**（调色板令牌与贴图无关）；皮肤资产重烘焙（8 张 = Focus+7 窗体件）
  - 回归：harness 双域 0 错；**主菜单逐像素 0.00% 差异**（迁移零回归铁证）；
    设置/展示/战斗四图目检全过；四场景重折叠 186 行、SketchPanel GUID 全 Prefab 清零
- [x] **W4（收官，2026-09-28）**：主仓 EditMode 终验**全绿**（1305/1306，0 失败，
      唯一跳过为登记在案的历史跳过）——重构前三红的 BeveledPixel 判据随族闭案；
      **近死件裁决**：PixelSkin.Window/Focus（零调用）降级为登记存件（出口加防误用
      注释，烘焙器程序化残段不动——联动三处，收益/风险比不划算）；**字体 GUID 政策
      落档**：消费契约 = 按 Resources 路径加载，任何资产/场景不许持字体 GUID 引用，
      ForceRebuildAll 换 GUID 是有意行为（builder 注释 + 本档双登记）。
      PlayMode/实机走查交创始人（清单见下）。

## 重构收官快照（2026-09-28）

- 提交链：W1 `ac1e8774/14559476/c911b52f`（merge `1aac3e43`）→ W2 `a25daea6`
  （merge `ff263d5d`）→ W3 `6a50654a`（merge `8fe3cbe5`）→ W4 注释与文档。
- 单一真源达成：控件产线 = Skin\SketchButton 家族（游戏内 31 按钮 = 主菜单同款）；
  皮肤 = PixelSkin 的 Ase 直切件 + Theme 色 + 平涂色块；战斗布局 = BattleHudZones；
  字盒口径 = 控件工厂单点；场景表 = BuildScenes.EditorRegistrationScenes；
  嵌套/异名 MonoBehaviour = 0（棘轮全绿）。
- 遗留（登记不阻塞）：①烘焙器程序化残段（Pixel_Window_*/Focus 存件 + Edge/
  PixelPiece 几何常量）——下次动烘焙器时一并清退；②RuntimeUiBuilder 仍保留场景
  控制器在用的 CreateRect/SetAnchored 等基础件（其按钮/文案建件已无调用方）；
  ③StickTokens.generated 的两个 SketchPanel 死常量；④WeaponPanelHeight 派生量
  表达式化（W1B 观察 2）。

## 创始人实机走查清单（Play/PlayMode）

进战斗走一轮：①所有按钮（武器格/跳跃/结束回合/模式钮/模态）悬停与按下应有
逐态字色变化（悬停字变亮、按下金字白面——与主菜单一致）；②未拥有的武器格 =
禁用双层字（灰字带影子）；③模式钮无 1/2/3 角标但键盘 1/2/3 仍可切；④设置面板
卡片为新 Ase 窗体皮（带「设置」标题带）；⑤暂停/结算/返回确认三模态观感。

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
