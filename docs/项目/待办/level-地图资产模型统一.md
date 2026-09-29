# 地图资产模型统一（全米；一代瓦片格编辑口径退场）

> 创始人 2026-09-30 裁决：**所有地图统一叫「地图」、统一成同一个资产接口；坐标口径全米；一代（瓦片格）编辑口径彻底退场。**
> 现状：**消费侧已统一**（`LevelSource`），分裂只在**资产层**（`LevelAssetPayload` vs `WorldMapAssetPayload`）。
> **未动手**；本档记裁决、现状实测、影响面，以及一个**动手前必须先定的子问题**。

## 裁决与理由

- 运行期本来就全米（`BattlePlan` 是格→米的唯一换算口），资产层却两套口径——历史包袱。
- 全米 = 资产口径与运行期同口径、两类地图资产同构；"一代瓦片格"这条编辑口径彻底退场。

## 现状（三层口径，实测）

| 层 | 关卡资产（`kind=level`） | 海图资产（`kind=world_map`，已删） | 是否统一 |
| --- | --- | --- | --- |
| **① 资产编辑口径** | **格**（`widthTiles/depthTiles`、`LevelUnit.gridX/gridY`、`waterTileY`） | **米**（`spanX/spanZ`、`SpawnEntry.x/z`、`KitPlacementEntry` 摆位） | ✗ 两套 |
| **② 解析/构建** | `LevelGeometry.BuildBattlePlan` 格→米 | `WorldMapRuntime.BuildBattlePlan` 直接米 | 归一 |
| **③ 运行期（AI/战斗/相机/水）** | 全米 | 全米 | ✓ 已统一 |

换算口：`LevelGeometry.GridToArena` / `BuildBattlePlan`，`X = (gridX + 0.5) × TileWorldSize`（`TileWorldSize = 2`）。

消费侧统一接口 = [`LevelSource`](../../../pirate-crew/Assets/Scripts/PirateCrew/Battle/Levels/LevelSourceResolver.cs)
（出战计划 / 地形栅格 / 水位 / 图幅 / 氛围档），全仓唯一分叉点 = `LevelSourceResolver`。
资产载荷定义见 [`LevelAssetTypes.cs`](../../../pirate-crew/Assets/Scripts/PirateCrew/Data/Levels/LevelAssetTypes.cs)。

## 影响面（全米要改什么）

1. **schema**：`LevelAssetPayload` 的 `widthTiles/depthTiles` → 世界米；`LevelUnit.gridX/gridY` → 世界米
   （与 `SpawnEntry` 并成一套出生点）；`waterTileY` → `waterWorldY`。
2. **两类载荷合并**：一个 `MapAssetPayload`，`levelNumber` 作唯一身份键（`id` 退为字符串别名）；
   编成 / 武器（船员·船长·空投三套）/ 陈设 / 氛围字段并成一套。
3. **工具链**：`LevelDataMigrator`、`LevelAssetValidator`、golden JSON、`LevelAssetYaml` 同步。
4. **测试**：`LevelAssetTests` 的逐图**冻结签名**、`ShowcaseLevelSelectionTests` 的号段、对拍用例。
5. **4 张地图资产迁移**：`cloud_walk` / `sky_island` / `chem_plant` / `chem_plant_team`。
6. **文档**：[关卡数据资产](../../技术/架构/关卡数据资产.md) §1.2 / §1.3 需重写。

## 未决点（动手前必须先定）

**地形的「编辑口径」要不要也统一？** 现在两类不同：

- 关卡资产（`kind=level`）：资产里**直接存** `TerrainRaster`（逐格块数）= 手摆真值；
- 海图资产（`kind=world_map`）：资产里存 **kit 件摆位**，运行时 `WorldMapRuntime.BuildTerrainGrid` **派生**出栅格。

两种最终都是「块栅格」——`TileTerrainGrid` 是运行期唯一地形模型，也是**碰撞 / 寻路 / 逐格破坏**的底座，
**不是历史包袱、不能动**。所以全米能统一的是**摆位与尺幅**，地形仍是块栅格。剩下的二选一：

- **A｜都直接存栅格**：海图在编辑期就把 kit 摆位烘成栅格（简单，但丢掉"改件即改地形"的联动）。
- **B｜都存摆位、派生栅格**：关卡资产也改成摆位表（最统一，工程量大）。

> 【建议】先定 A/B，再动 schema——否则 schema 改两遍。

## 坑（预先登记）

- **格 schema 是一代遗留的容器**：现役这 4 张是"装进这个继承来的容器"，**不是照抄/转写原版来的**
  （照抄转写那条线不在现役内容里）。全米的真正收益 = 清掉容器里的化石字段：
  `originalXmlPlayers` / `sourceXmlMaxChests`（自注「仅存档备查」）、
  `waterTileY`（配 `WaterY = waterTileY * 32f` 的 32px 时代换算）、`maxChests`（宝箱未实装的占位）。
  ⇒ **不要写"丢掉与原版逐格对齐的便利"**——全仓无此口径，那是误记。
- `TileWorldSize = 2` 全米后从"资产口径"退成"逻辑格宽度的单一常量"，其消费者与语义要一并清。
- 「同级化」在这条之前就被登记过（[关卡体系瘦身-一代退场计划](../../项目/归档/关卡体系瘦身-一代退场计划.md) §三.4
  的「三套摆场/材质/出生点抽公共层」），本任务把它从"抽公共代码"升格为"统一资产模型"——两者一并做，别分两轮。
