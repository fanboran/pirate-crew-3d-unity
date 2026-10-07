# 云端漫步 / 天空之岛岛体 Blender 重做(照化工厂样板)

> 建模/站面派生/接线/重烘/实拍已全部完成(harness 970 绿、八步链全绿、折叠通过、EditMode 1037 绿);
> 两关实拍图在 `temp/island-review*` 与 `docs/images/level-islands/`,**待用户过审**后归档收口。

## 详情

> 【创始人口径(2026-10-06,天空之岛回退案)】当初的"重做成 Blender 的"指**场景里的各个元素可以是
> Blender 的**——Unity 场景保持原装配结构(元素清单/摆位/层级不动),逐个零件换 Blender 导入的 mesh;
> **不是**把整个场景压成一个 Blender 总装 FBX,更不是重发明形制。本档的格板路线(照化工厂样板
> 做 2 m 格切板总装 FBX 顶掉程序化装配)是对该指令的错误执行,已回退:`PlaceIntoBattleCenter`
> 恢复程序化空岛装配(`FloatingIslandComposer` = 过审老版的构图真源,一直存活,无需"翻译"),
> `SkyIsland.fbx` 与 `build_sky_island.py` 产线废弃待删。后续元素级 Blender 化以此为口径。

### 现状(建模/派生/接线已落盘)

- **岛形驱动管线**:`tools/blender/scene/islands/`(公共库 `island_common.py` + 两关 build 脚本
  + README 口径)。顶面按 2 m 格逐格切板(格板并集外缘 = 关卡轮廓,凹多边形绝不自交),
  外缘立面贴合轮廓线段;云台 = 云唇 + 鼓形云身(瓣状调制)+ 云乳,大岛 = 草三档格板 +
  岩壁地层带 + 倒锥收底;浮空岛群为装饰件(超椭圆自由足印,不进 terrain)。
- **产物**:`CloudWalk.fbx`(5 节点)/ `SkyIsland.fbx`(6 节点)入 SceneKit;
  预览三视角在 `docs/images/level-islands/`。
- **站面派生**:build 随件导出站面快照,`tools/level-design/sync_island_terrain.py` 回填
  golden terrain 段——首跑 **IN SYNC**(视觉与数据钉在同一根轴,冻结签名不动)。
- **接线改造**:`SceneArtBaker` 重写为接线收口器(程序化云场烘焙退役,FBX 导入参数显式化,
  与 ChemPlant.fbx 同口径);`BattleSceneSetup` 的 `cloudFieldPrefab` 改引 `CloudWalk.fbx`;
  `PlaceIntoBattleCenter` 改实例化 `SkyIsland.fbx`。`CloudFieldGeometry` /
  `IslandShellGeometry` 类保留(像素试点场等仍消费),只退役两关接线。
- **验证**:harness DataEditor 编译 0 错、All 970 绿(0 失败)。

### 余下步骤

1. ~~重烘 + 折叠校验 + EditMode~~(已完成:八步链全绿、折叠通过、EditMode 1037 绿——唯一失败
   `CrewTeamTintPart` 属并行会话的在建文件域,与本任务无关)。
2. **用户过审**:两关实拍走查(实拍已出:`temp/island-review/` 第 1 关、`temp/island-review-l3/`
   第 3 关,三档正交;Blender 全景在 `docs/images/level-islands/`);像素化判据
   (块化/墨线/光照未旁路)实拍已核,落色走 `PixelartContentConverter`,与化工厂同链。
   过审后本任务归档。

### 边界

- 角色模型不涉(铁律不动)。
- `IslandShellGeometry` 的「单格台地壳」能力在其他样板场仍在消费,退役范围只限两关接线。
- 站面派生路线打通后,可回头补化工厂的可站面 manifest(其未做项)。
