# 地图资产模型统一（全米；瓦片格彻底退场）

> 创始人 2026-09-30 裁决：**所有地图统一叫「地图」、统一成同一个资产接口；坐标与数值口径全米；
> 「格」彻底退场**——不是只换单位、不是保留格判定，而是格从**资产、文档、玩法表述**里全部消失。
> 现状：**消费侧已统一**（`LevelSource`），分裂只在**资产层**（`LevelAssetPayload` vs `WorldMapAssetPayload`）。
> **未动手**；本档记裁决、现状实测与影响面。

## 一、要改成什么

- **只有一种地图资产载荷**，`levelNumber` 作唯一身份键；
- **尺幅、出生点、水位、射程、跳距……一切数值一律用米**；
- **格号作为对外概念消失**：`gridX/gridY`、`widthTiles/depthTiles`、`waterTileY`、`TileWorldSize` 不再出现在资产与文档里。

## 二、现状（待改，实测）

| 层 | 关卡资产（`kind=level`） | 海图资产（`kind=world_map`，已删） | 是否统一 |
| --- | --- | --- | --- |
| **① 资产口径** | **格**（`widthTiles/depthTiles`、`LevelUnit.gridX/gridY`、`waterTileY`） | **米**（`spanX/spanZ`、`SpawnEntry.x/z`、`KitPlacementEntry` 摆位） | ✗ 两套 |
| **② 解析/构建** | `LevelGeometry.BuildBattlePlan` 格→米 | `WorldMapRuntime.BuildBattlePlan` 直接米 | 归一 |
| **③ 运行期** | 米 | 米 | ✓ 已统一 |

消费侧统一接口 = [`LevelSource`](../../../pirate-crew/Assets/Scripts/PirateCrew/Battle/Levels/LevelSourceResolver.cs)
（出战计划 / 地形 / 水位 / 图幅 / 氛围档），全仓唯一分叉点 = `LevelSourceResolver`。
资产载荷定义见 [`LevelAssetTypes.cs`](../../../pirate-crew/Assets/Scripts/PirateCrew/Data/Levels/LevelAssetTypes.cs)。

## 三、影响面

1. **schema**：`widthTiles/depthTiles` → 世界米；`LevelUnit.gridX/gridY` → 世界米（与海图的 `SpawnEntry` 并成一套出生点）；`waterTileY` → `waterWorldY`。
2. **两类载荷合并**成一个：`levelNumber` 作唯一身份键（`id` 退为字符串别名）；编成 / 武器（船员·船长·空投三套）/ 陈设 / 氛围字段并成一套。
3. **玩法数值表述换单位**（数值本身不动）：满力射程 12.5 格 → **25 米**、最大跳隙 6.5 格 → **13 米**、场地 20×15 格 → **40×30 米**（[关卡制作管线](../../设计/关卡/关卡制作管线.md) §三 的尺度常量表一并改）。
4. **工具链**：`LevelDataMigrator`、`LevelAssetValidator`、`LevelYaml`、golden JSON 同步。
5. **测试**：`LevelAssetTests` 的逐图**冻结签名**、`ShowcaseLevelSelectionTests` 的号段、对拍用例。
6. **4 张地图资产迁移**：`cloud_walk` / `sky_island` / `chem_plant` / `chem_plant_team`。
7. **文档**：[关卡数据资产](../../技术/架构/关卡数据资产.md) §1.2 / §1.3 重写。

> **实现备注**（不是玩法概念，不出现在资产与文档）：运行时地形数据总得有个存储粒度
> （现行 2 m 一格）。它只是**数据采样间隔**，不对玩家、不对资产、不对文档暴露。

## 四、坑（预先登记）

- **格 schema 是一代遗留的容器**：全米 = 连容器一起清掉，内含化石字段一并消失——
  `originalXmlPlayers` / `sourceXmlMaxChests`（自注「仅存档备查」）、`waterTileY`
  （配 `WaterY = waterTileY * 32f` 的 32px 时代换算）、`maxChests`（宝箱未实装的占位）。
- **换单位是逐条数值核对**，不是全局搜索替换：`×2` 只适用于格↔米，别把「块高 0.5」「水面 y=−0.4」这类已经是米的也乘了。
- 会动**冻结签名**的测试（`LevelAssetTests` 逐图签名、`ShowcaseLevelSelectionTests` 号段）——改完必须重跑收口。
- 「同级化」（[关卡体系瘦身-一代退场计划](../../项目/归档/关卡体系瘦身-一代退场计划.md) §三.4 的「三套摆场/材质/出生点抽公共层」）
  与本条是同一件事，**一并做，别分两轮**。
