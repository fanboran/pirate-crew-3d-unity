# UI 审计——2026-09-17

> **审计方式**：静态代码走查（运行时 UI 脚本 `Assets/Scripts/UI/` 全量 + Editor 装配脚本
> `BattleHudBuilder` / `M3SceneSetup` / `HudMinimapSceneSetup` / `M2BattleSceneSetup` + 数据目录层），
> 未启动编辑器实测。凡由代码推断、未经实机截图验证的结论，正文已标注。
> **性质**：问题清单是**事实**（附可核对行号）；「建议修复顺序」一节是**提案/待定**，未经用户裁决。

## 审计范围

| 层 | 文件 |
| --- | --- |
| 运行时 | `BattleHud.cs`(1088 行) / `BattleMinimap.cs` / `LevelSelectController.cs` / `MainMenuController.cs` / `CrewManagementController.cs` / `M3UiBuilder.cs` / `UiTheme.cs` / `UiTextRules.cs` / `UiStrings.cs` / `UiMotion(.Rules).cs` / `MinimapRules.cs` / `HudProjectionRules.cs` |
| Editor 装配 | `BattleHudBuilder.cs` / `M3SceneSetup.cs` / `HudMinimapSceneSetup.cs` / `M2BattleSceneSetup.cs`(UI 相关部分) / `SceneSetup.cs` / `MenuUiBuilder.cs`(字号体系) |

## 总评

**骨架质量高于一般作品集水准**：事件驱动刷新（不做 Update 轮询，模式系统一处显式豁免）、
Canvas 全部 `ScaleWithScreenSize` 1920×1080 / match 0.5、WCAG 对比度逐条推导并写入注释、
程序化九宫格/亚克力玻璃材质、动效规则抽成纯 C# 可无头测试、血条 28 帧逆向口径保真。

问题集中在两类：**M4 大海域"先做世界、后补 UI"的欠账**（P0 全部与之相关），
以及一批**装配了但从未接线的死 UI**（P1）。后者对作品集评审的伤害最大——
"看着就假"的细节（永不动的时间表、和实际行为相反的提示）比缺功能更扣分。

---

## P0 —— 结构性断层（玩家可感知的"错"）

### 1. 8 张世界地图没有任何 UI 入口

M4 的 8 张语义图（`WorldMapCatalog`，关卡号 101–108）目前只能通过命令行
`-worldMap <id>` 或代码调用 `WorldMapRuntime.SetPending` 进入（`WorldMapRuntime.cs:26`，
其注释自述"未来的选关 UI 走这条"）。选关界面绑定的仍是旧战役目录
`CampaignCatalog`（3 章 × 5 关 =「第 1 关…第 15 关」，`LevelSelectController.cs:291`、
`CampaignCatalog.cs:155`）。两套关卡体系在 UI 层完全脱节。

→ 即 `docs/交接与恢复指南.md` §20 待办第 4 项「战役 UI 重绑 8 图」，尚未开工。

### 2. 小地图是"level_1 专用"，打任何其他关卡都是错的

- 岛形（沙/草逐格）在**编辑期**按 level_1 的瓦片烘进 Battle.unity
  （`HudMinimapSceneSetup.cs:152`；关卡号解析在 level 资产为空时回落 level_1）；
- 换算范围写死 50×17（`BattleMinimap.cs:45-47` 序列化默认值，由装配脚本按旧关卡瓦片数写入）；
- 装配时刻意把 `terrain` 引用置空、运行期不再重绘
  （`HudMinimapSceneSetup.cs:634`「地形被炸后小地图不再变化」的既记录取舍）。

后果（由代码推断，未实机验证）：
- 选关打第 2–15 关：小地图显示的仍是 level_1 的岛形；
- 进 150×150 世界图：`WorldMapRuntime` / `WorldMapComposer` 对小地图**零适配**
  （全目录 grep 无命中），单位点位经 `MinimapRules.ClampNormalized` 压进左上约 1/9 区域堆在角上，
  底下还垫着一张不相关的 level_1 岛。

### 3. 选关页挂着一句"假提示"，与实际行为相反

界面常驻文案「本轮战斗固定加载第 1 关竞技场，选关只决定结算归属」
（`UiStrings.cs:260`，摆放在选关页底部 `M3SceneSetup.cs:174-179`）。
但当前实现恰恰相反：装配时**刻意不**给 BattleController 注入 level 资产、
让 BuildPlan 走「CampaignApi 待战关」分支（`M2BattleSceneSetup.cs:835` 注释、
`BattleController.cs:248-252`）——选哪关就真加载哪关的关卡数据。
`LevelSelectController.cs:14-15` 的类头注释也还是这句过时声明。
（该提示在 M3 当时应属实，后续接线改掉了行为、没改文案。）

---

## P1 —— 死 UI（装配了、没接线）

### 4. 顶栏「回合 1/20」「时间 0:00」是永远不动的假表

`BattleHudBuilder.cs:229-232` 建两个文字槽并写死初值 `TurnCounter(1, 20)` / `Timer(0)`；
全工程仅 `UiTextRules.cs:187` / `UiTextRules.cs:193` 两个格式化函数，**没有任何运行时脚本
更新这两个槽**；`TurnManager` 中不存在回合上限与计时逻辑。玩家整局看到的都是
「回合 1/20、时间 0:00」。

### 5. 瞄准/聚焦标签永久隐藏

`BattleHudBuilder.cs:791-803` 创建 `AimLabel` / `FocusLabel`，`SetActive(false)` 后
无任何脚本引用（注释自述"等玩法接线后由逻辑控制显隐"，一直没接）。
其职责已被 r12 模式提示条接管的话，这两个节点应删除。

---

## P2 —— 一致性与体验

### 6. 字号体系双轨制，且与规范文档脱节

战斗 HUD 走 `MenuUiBuilder.FontScale`（Body 15 / Hud 18 / Tiny 13，用户裁决"下调一档"）；
M3 菜单场景走 `UiTheme.Font*`（Body 20 / Hud 24）。同一项目两套字号；
且 `UiTheme.cs:74` 注释仍写「正文下限 20 为硬约束（§1.4）」——裁决落地后规范文档未同步。

### 7. 菜单列表无滚动、容器大量留白；接 8 图必然溢出

- 选关列表容器 1000×560，只放 5 行 × 50px = 250px（`M3SceneSetup.cs:170-172` +
  `LevelSelectController.cs:29` RowHeight 44 + 间距 6），下半截全空；
- 船员列表容器 1000×600，只放 7 行 × 50px = 350px（`M3SceneSetup.cs:94-96`，全目录 7 名船员）；
- 全工程仅武器面板有 ScrollRect（`BattleHudBuilder.cs:695`），菜单列表没有滚动兜底。
  一旦按待办把 8 图塞进选关页，直接溢出面板。

### 8. 关卡名无语义

15 关显示名全部为「第 N 关」（`CampaignCatalog.cs:155`）。与 8 图的语义命名
（搁浅圣母号、龟背岛…）并存后，选关页观感割裂会更明显。

### 9. juice 不均匀

战斗内：面板滑入/淡出、回合横幅 punch、受击行弹跳、按钮 punch + UiClick/UiError/UiPanelOpen
音效均已接线（`f460b70`）。主菜单设置面板、选关结算弹窗、船员管理列表：
无开合动效、无 UI 音效。同一游戏两种手感。

---

## P3 —— 细节卫生

- **字符串找节点**：`BattleHud` 模式系统用 `DeepFind` 按名字全树递归搜
  `MoveSegment`/`Crosshair`/`HintText`（`BattleHud.cs:259-265`、`BattleHud.cs:382-393`）——
  装配侧改名即静默失联。与「禁止 GameObject.Find」的架构约定精神相悖
  （代码注释解释了原因：组件与目标是兄弟分支，算已知取舍）。
- **世界图关卡号溢出文案口径**：名册标题会显示「船员名册 · 第 101 关」
  （`UiStrings.cs:297` 格式串 + `BattleHud.cs:703` 传入 101–108）。
- **结算明细区贴边**：战斗结算 `SettlementLines` 固定 600×180（`BattleHudBuilder.cs:894-897`），
  星级+评分+经验+招募+首通行数多时约 175px，贴近上限；改文案长度即溢出。
- **准星每帧 SetActive**（`BattleHud.cs:317-318`）：无实际开销，纯 nit。

---

## 建议修复顺序（**提案/待定，未经裁决**）

1. **P0-1 + P2-7 合并做**：选关 UI 重绑 8 图时顺手给列表加 ScrollRect——
   本来就是交接文档排定的活，一次改动同时消掉两个问题。
2. **P0-2**：进战斗时按实际地图重设小地图 `arenaWidth/arenaDepth` 并重烘（或清空）岛层；
   若世界图短期不做小地图，先在世界图上隐藏小地图面板也算止血。
3. **P1-4 / P1-5 / P0-3** 是低成本"诚实性"修复：要么接上线、要么删掉死件、要么改掉假提示。
   这三项工作量都在小时级，对评审观感的性价比最高。
4. P2-6：二选一——把裁决后的字号档写回 `UiTheme`/规范文档，或菜单场景迁移到 `FontScale`。
