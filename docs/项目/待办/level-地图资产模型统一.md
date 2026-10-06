# 地图资产模型统一（全米；瓦片格彻底退场）

> 创始人 2026-09-30 裁决：**所有地图统一叫「地图」、统一成同一个资产接口；坐标与数值口径全米；
> 「格」彻底退场**——不是只换单位、不是保留格判定，而是格从**资产、文档、玩法表述**里全部消失。
> 现状：**消费侧已统一**（`LevelSource`），分裂只在**资产层**（`LevelAssetPayload` vs `WorldMapAssetPayload`）。
> **进度**：**资产层已落**（提交 `refactor(level): 地图资产全米`）——`sizeX/sizeZ`（米）、
> `TerrainRaster.heights`（米）替代块数、`LevelUnit.x/z`（米）、`waterTileY → waterWorldY`；
> 4 张地图的 golden 与 `.asset` 已由 `tools/level-design/migrate_levels_to_meters.py` 迁移，
> 原「solid/total/digest」摘要**逐字未变**（= 行为等价证据）。
> **遗留**：运行时的**采样粒度**仍叫「格」（`LevelGeometry.TileWorldSize`、`BattlePlan.WidthTiles`、
> `TileTerrainGrid`、`SpawnPlanEntry.GridX/GridY`）——消费它们的 `BattleController` /
> `BattleSceneSetup` / `BattleSceneWiringTests` 是**创始人在建文件**，待其提交后收口。

## 详情

### 一、要改成什么

- **只有一种地图资产载荷**，`levelNumber` 作唯一身份键；
- **尺幅、出生点、水位、射程、跳距……一切数值一律用米**；
- **格号作为对外概念消失**：`gridX/gridY`、`widthTiles/depthTiles`、`waterTileY`、`TileWorldSize` 不再出现在资产与文档里。

### 二、现状（待改，实测）

| 层 | 关卡资产（`kind=level`） | 海图资产（`kind=world_map`，已删） | 是否统一 |
| --- | --- | --- | --- |
| **① 资产口径** | **格**（`widthTiles/depthTiles`、`LevelUnit.gridX/gridY`、`waterTileY`） | **米**（`spanX/spanZ`、`SpawnEntry.x/z`、`KitPlacementEntry` 摆位） | ✗ 两套 |
| **② 解析/构建** | `LevelGeometry.BuildBattlePlan` 格→米 | `WorldMapRuntime.BuildBattlePlan` 直接米 | 归一 |
| **③ 运行期** | 米 | 米 | ✓ 已统一 |

消费侧统一接口 = [`LevelSource`](../../../pirate-crew/Assets/Scripts/PirateCrew/Battle/Levels/LevelSourceResolver.cs)
（出战计划 / 地形 / 水位 / 图幅 / 氛围档），全仓唯一分叉点 = `LevelSourceResolver`。
资产载荷定义见 [`LevelAssetTypes.cs`](../../../pirate-crew/Assets/Scripts/PirateCrew/Data/Levels/LevelAssetTypes.cs)。

### 三、影响面

1. **schema**：`widthTiles/depthTiles` → 世界米；`LevelUnit.gridX/gridY` → 世界米（与海图的 `SpawnEntry` 并成一套出生点）；`waterTileY` → `waterWorldY`。
2. **两类载荷合并**成一个：`levelNumber` 作唯一身份键（`id` 退为字符串别名）；编成 / 武器（船员·船长·空投三套）/ 陈设 / 氛围字段并成一套。
3. **玩法数值只管用米写，不回头换算**：射程 / 跳距这些现值是 AI 占位、创始人尚未填数，
   不必"把 12.5 格换成 25 米"——将来填数时**直接以米落笔**即可。文档与代码里凡是"格"当单位的地方一律改写。
4. **不声明场地尺寸；范围 = 内容的自然边界**：现在资产里存 `widthTiles/depthTiles` / `spanX/spanZ`
   这种"先声明一块场地再往里填"的写法，正是八张海图"空尺度"（约 75% 图面无人可到）的病根。
   新模型**只存内容**，范围由内容边界推出；界外没有地面 ⇒ 就是海 ⇒ 落水即死。
   - **代码层不需要、也没有"活动范围限制"**：玩家能去哪只由"那里有没有地面"决定。
     尺寸字段的唯一用途是配**水面域 / 海面罩 / 相机取景**（`WaterSimulationDriver.ConfigureWorldDomain`、
     `OceanRig`、`CameraWorldSpan`）——是视觉与模拟范围，不是边界墙。
   - **"别把图做大"是设计口径、不是代码约束**：见 [大海域世界化（归档）](../归档/M4-世界化/大海域世界化.md)
     §0 裁决 2 与 §1「不无限摊平可玩区（回合制节奏保护）」——该口径为已删 M4 线所作，
     重做时随 [环境表现 §4.4](../../设计/场景.md) 逐条重新确认；靠**设计时把图做小**落地。
   - 建关侧的**校验规则**（[WorldMapRules](../../../pirate-crew/Assets/Scripts/PirateCrew/Battle/WorldMaps/WorldMapRules.cs)：
     跳隙 ≤ 13u、上跳 ≤ 3.5u、出生点必须同连通分量）是**设计期校验器**，拦的是"这图能不能打"，不是玩家。
5. **工具链**：`LevelDataMigrator`、`LevelAssetValidator`、`LevelYaml`、golden JSON 同步。
6. **测试**：`LevelAssetTests` 的逐图**冻结签名**、`ShowcaseLevelSelectionTests` 的号段、对拍用例。
7. **4 张地图资产迁移**：`cloud_walk` / `sky_island` / `chem_plant` / `chem_plant_team`。
8. **文档**：[关卡数据资产](../../技术/架构/关卡数据资产.md) §1.2 / §1.3 重写。

> **实现备注**（不是玩法概念，不出现在资产与文档）：运行时地形数据总得有个存储粒度
> （现行 2 m 一格）。它只是**数据采样间隔**，不对玩家、不对资产、不对文档暴露。

### 四、坑（预先登记）

- **格 schema 是一代遗留的容器**：全米 = 连容器一起清掉，内含化石字段一并消失——
  `originalXmlPlayers` / `sourceXmlMaxChests`（自注「仅存档备查」）、`waterTileY`
  （配 `WaterY = waterTileY * 32f` 的 32px 时代换算）、`maxChests`（宝箱未实装的占位）。
- **换单位是逐条数值核对**，不是全局搜索替换：`×2` 只适用于格↔米，别把「块高 0.5」「水面 y=−0.4」这类已经是米的也乘了。
- 会动**冻结签名**的测试（`LevelAssetTests` 逐图签名、`ShowcaseLevelSelectionTests` 号段）——改完必须重跑收口。
- 「同级化」（[关卡体系瘦身-一代退场计划](../../项目/归档/关卡体系瘦身-一代退场计划.md) §三.4 的「三套摆场/材质/出生点抽公共层」）
  与本条是同一件事，**一并做，别分两轮**。
