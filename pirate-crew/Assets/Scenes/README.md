# 场景目录与装配契约

> 本文件回答一个问题：**这个目录里的场景，内容在哪、由谁生成、改的时候该动谁。**
> 背景与证据见 [`docs/审计/专项/场景接线审计报告.md`](../../../docs/审计/专项/场景接线审计报告.md)；
> 分层总览见 [`docs/技术/架构/架构总览.md`](../../../docs/技术/架构/架构总览.md) §7。

## 一、场景是「装配清单」，内容是 Prefab

四个由装配脚本产出的场景已经**折叠**成"一个 Prefab 实例"：场景文件只剩两百行上下
（`Battle.unity` 18,772 → 186 行，`MainMenu` 10,050 → 206，`LevelSelect` 5,111 → 194，
`CrewManagement` 2,446 → 198；四个场景合计 38,034 → 784 行），全部内容搬进 Prefab 资产。
这样做的三个具体收益：场景 diff 能读、内容可在 Prefab 模式里单独打开调、Prefab 能被别的场景复用。

| 场景 | 来源 | 内容载体 | 对象数 |
| --- | --- | --- | --- |
| `Bootstrapper.unity` | 手摆（组合根 + 视频设置） | 场景本体（**有意不折叠**） | 2 |
| `MainMenu.unity` | `Editor/SceneSetup.cs` | `Assets/Prefabs/UI/MainMenuScreen.prefab` | 95 |
| `LevelSelect.unity` | `Editor/M3SceneSetup.cs` | `Assets/Prefabs/UI/LevelSelectScreen.prefab` | 43 |
| `CrewManagement.unity` | `Editor/M3SceneSetup.cs` | `Assets/Prefabs/UI/CrewManagementScreen.prefab` | 21 |
| **`Battle.unity`** | `Editor/BattleScenePipeline.cs` 八步子链 | `Assets/Prefabs/PirateCrew/Battle/BattleRig.prefab` | 217（18 根） |

`Bootstrapper` 只有 2 个对象且是入口场景，折它只增加一层间接，**有意保留手摆**。

场景文件里仍然保留的、**不属于 Prefab** 的东西是场景级设置：`RenderSettings`（雾、环境光、
天空盒）、`LightmapSettings`、`NavMeshSettings`、以及 `SceneRoots`。这些本来就必须在场景里。

## 二、Battle 的装配链（八步，顺序是代码）

八步都写在 `Assets/Editor/BattleScenePipeline.cs` 的 `Steps` 表里，**不靠文档背顺序**：

| # | 步骤 | 为什么在这个位置 |
| --- | --- | --- |
| ① | `M2BattleSceneSetup.BuildAll` | `NewScene(EmptyScene)` 全量重建——它敢扔掉旧场景，因为后面每步都会把该补的补回来 |
| ② | `FloatingIslandShowcaseMenu.PlaceIntoBattleCenter` | 空岛样板件（第 3 关地面）；在 ① 之后，否则被重建洗掉 |
| ③ | `SceneArtBaker.BuildAll` | 样板场景件烘焙（云场/危险线 → prefab + 接线） |
| ④ | `SceneAssetManifestBuilder.BuildAll` | 场景资产总清单 |
| ⑤ | `HudMinimapSceneSetup.WireMinimap` | 小地图面板/层级/引用增量接线；必须在 ① 之后（曾因此静默坏过三轮，c78fdea） |
| ⑥ | `BattleLookupWiring.Wire` | 运行期查找清退的三条显式接线（`aimThrow` / `cameraController` / `sunLight`） |
| ⑦ | `WorldMapAssetSetBuilder.BuildAll` | WorldKit 资产表 + 海面材质接线（写 `BattleController` 两个字段） |
| ⑧ | `ScenePrefabCollapse.CollapseBattle` | **折叠**：全部根收进载体根 → 存 `BattleRig.prefab` → 场景只留实例 |

```bash
U="F:/Unity/2022.3.62f1c1/Editor/Unity.exe"
P="F:/VSCode/pirate-crew-3d-unity/pirate-crew"

# 只重跑 Battle 子链（改战斗场景时的常用入口，一条命令顶原先的六步）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.BattleScenePipeline.BuildFromCommandLine -logFile -

# 整条资产管线（材质/字体/音频/角色全重建 + M1/M3 场景 + 四个场景折叠）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.ArtGate.BuildAll -logFile -
```

**铁律**：`①` 是破坏性重建，**手工摆在场景里的任何东西都会被下一次重跑抹掉**——
折叠之后这条铁律的落点从场景移到了 Prefab：手工调整要写进生成器，或写进 Prefab 并接受
"下一次重跑链会覆盖它"。顺序与步骤数由 `Assets/Tests/Battle/SceneAssemblyContractTests.cs`
冻结，换序会红。

## 三、折叠态契约（场景必须保持「纯实例」）

折叠不是一次性动作，而是一条**要一直成立**的不变量：

1. 场景里 **0 个裸对象**——没有 GameObject 块，内容全部来自 Prefab 实例；
2. **恰好 1 个 Prefab 实例块**，源指向该场景对应的 Prefab；
3. 实例 **不带任何"非默认"覆盖**——覆盖会把接线/参数散回场景文件，正是折叠要消灭的状态。

为什么这条不变量值得守：折叠后往场景里写字段，等于把"接线知识"从 Prefab 挪回场景文件，
`ScenePrefabCollapse` 的校验会当场报出来，而不是等到某天发现"小地图又空了"。

```bash
# 只读校验（漂移 → 退出码 1）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.ScenePrefabCollapse.FromCommandLine \
  -collapseMode verify -logFile -

# 重新折叠（幂等：已折叠的场景会先完全解包再折，不会"实例套实例"）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.ScenePrefabCollapse.FromCommandLine \
  -collapseMode collapse -collapseTargets "Battle" -logFile -
```

同一组断言也在 EditMode 测试里跑（`SceneAssemblyContractTests`），
所以「只跑了装配链的一半」「有人往场景里手摆对象」都会当场红。

## 四、Battle 里有什么（三类，实测口径）

折叠不改变这三类的划分——它只改变了"落盘形态"（场景 → Prefab），划分本身照旧：

### (a) 稳定结构 —— 与关卡无关（16 个根）

`Main Camera`(含 CinemachineBrain) / `CameraTarget` / `BattleVCam` / `Directional Light` /
`GlobalVolume` / `EventSystem` / `BattleCanvas`(HUD 全树，含 `BattleHud`、`BattleMinimap`) /
`BattleController` / `TurnManager` / `AimThrowController` / `BattleCameraController` /
`TrajectoryPreview` / `Team0_Red` / `Team1_Blue` / `Terrain`(BattleTerrainView) /
`SceneArt`(+ 子 `Ambient`：RuntimeSceneArt + AmbientDirector)。

### (a.5) 关卡 3 的空岛地面（1 个根）

`FloatingIslandShowcase`（16 个按材质分组的子物体）由链内第 ② 步 `PlaceIntoBattleCenter` 摆入，
并在同一处接给 `RuntimeSceneArt.skyIslandRoot`——**运行期由它按关卡号开关可见性（只有第 3 关激活）**，
所以它进 Prefab 是正确的，不该当成"多出来的东西"删掉。

### (b) 按关卡参数定尺寸的几何（1 个根）

`Water` 的平面尺寸由 `LevelCatalog` 推出（装配时烘 `level_1` 的尺寸）；
相机与 `CameraTarget` 的位置同源。运行期 `BattleController` 还会按实际关卡再校正一次
（水面高度、世界地图的相机 span 与海洋 rig）。

### (c) 运行时生成（场景里只有根/容器，内容不落盘）

地形块、场景陈设、单位、世界地图灰盒与 kit、海洋 rig、远景环带、弹体、FX、小地图点阵——
生成者与时机逐条见审计报告 §二(c)。

**切分原则**（改装配脚本前先对齐这条）：Prefab 持"结构 + 相对关系 + 资产引用"；
装配期只写"关卡参数"；运行期只造"内容"。Prefab 里**不许出现关卡数据**
（尺寸/位置/瓦片数/小地图 arena 尺寸都不许烘进 Prefab 的序列化值）。

## 五、改这个目录前先跑什么

```bash
# 转储当前场景接线（人读 txt + 机读 tsv + 统计 summary），产物默认落仓库根 external/scene-audit/
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.SceneWiringAudit.DumpBattleFromCommandLine -logFile -

# 折叠前后的比对要加 -sceneAuditStripRoot <载体根名>，两侧路径才对齐
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.SceneWiringAudit.DumpBattleFromCommandLine \
  -sceneAuditOut "<绝对目录>" -sceneAuditStripRoot BattleRig -logFile -

# 比对两次转储
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.SceneWiringAudit.DiffFromCommandLine \
  -sceneAuditDiff "a.tsv;b.tsv" -logFile -
```

**改前 dump 一份、改后 dump 一份、Diff 一次：每条差异都必须能解释**（新增/删除/字段指向变化）。
这条流程是场景结构改动的验收标准，不是可选项。
