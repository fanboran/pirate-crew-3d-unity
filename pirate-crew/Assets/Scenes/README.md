# 场景目录与装配契约

> 本文件回答一个问题：**这个目录里的场景，哪些是人手摆的、哪些是脚本生成的、改的时候该动谁。**
> 背景与证据见 [`docs/审计/场景接线审计报告.md`](../../../docs/审计/场景接线审计报告.md)（实测基准 `3c6b51d`）。
> 「归 Prefab / 留生成」的切分契约是**提案/待定**（尚未执行，需用户过审后按阶段落地）。

## 一、五个场景与它们的来源

| 场景 | 来源 | 谁生成/维护 |
| --- | --- | --- |
| `Bootstrapper.unity` | 手摆（1 个 `Bootstrapper` + `VideoSettings`） | `Editor/SceneSetup.cs` 可重建 |
| `MainMenu.unity` | 手摆（UI 全树） | `Editor/SceneSetup.cs` 可重建 |
| `CrewManagement.unity` | 手摆（UI 全树） | `Editor/M3SceneSetup.cs` 可重建 |
| `LevelSelect.unity` | 手摆（UI 全树） | `Editor/M3SceneSetup.cs` 可重建 |
| **`Battle.unity`** | **生成产物**（372 对象 / 23 根 / 3.4 万行） | `Editor/M2BattleSceneSetup.cs` 全量重建 + `Editor/HudMinimapSceneSetup.cs` 增量接线 |

**为什么 Battle 是生成式的**：它的内容由关卡数据（`LevelCatalog` / `PlatformClusterLayout` /
`WorldMapCatalog`）驱动，对象数量与尺寸都随关卡变化，手摆无法维护。
**代价**（必须知道的）：场景里的接线知识活在一次性 Editor 脚本里，`Find` 存量由此而来（审计 P2-7）。

## 二、Battle 场景的装配链（顺序敏感）

```bash
U="F:/Unity/2022.3.62f1c1/Editor/Unity.exe"
P="F:/VSCode/pirate-crew-3d-unity/pirate-crew"

# ① 全量重建 Battle（NewScene → 造全部对象 → SaveScene）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.M2BattleSceneSetup.BuildAll -logFile -
# ② 增量接小地图（必须在 ① 之后：① 会重建场景，① 之后的接线全丢）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.HudMinimapSceneSetup.WireMinimap -logFile -
# ③ 重建 M3 两场景 + 写 Build Settings 5 场景（与 Battle 无关，放在最后是因为它写构建列表）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.M3SceneSetup.BuildAll -logFile -
```

**铁律**：`①` 是破坏性重建（`EditorSceneManager.NewScene(EmptyScene)`），**手工在 Battle 里摆的任何东西
都会被下一次 `BuildAll` 抹掉**；`①` 之后必须重跑 `②`，否则小地图未接线（该约束目前没有测试钉住，
登记在审计报告 F-1）。

## 三、Battle 里有什么（三类，实测口径）

### (a) 稳定结构 —— 与关卡无关（16 个根）

`Main Camera`(含 CinemachineBrain) / `CameraTarget` / `BattleVCam` / `Directional Light` /
`GlobalVolume` / `EventSystem` / `BattleCanvas`(HUD 全树 347 对象，含 `BattleHud`、`BattleMinimap`) /
`BattleController` / `TurnManager` / `AimThrowController` / `BattleCameraController` /
`TrajectoryPreview` / `Team0_Red` / `Team1_Blue` / `Terrain`(BattleTerrainView) /
`SceneArt`(+ 子 `Ambient`：RuntimeSceneArt + AmbientDirector)。

### (b) 按关卡参数定尺寸的几何（8 个根）

`Water` / `Seabed_Far` / `Seabed_L0..L4` 的尺寸、以及相机与 `CameraTarget` 的位置，
全部由 `LevelCatalog.Get(LevelNumber)`（装配时 `LevelNumber = 1`）推出；
运行期 `BattleController` 还会再校正一次（水面高度、世界地图的相机 span 与海洋 rig）。

### (c) 运行时生成（场景里只有根/容器，内容不落盘）

地形块、场景陈设、单位、世界地图灰盒与 kit、海洋 rig、远景环带、弹体、FX、小地图点阵——
生成者与时机逐条见审计报告 §二(c)。

## 四、切分契约（提案/待定）

1. **稳定结构归 Prefab，参数化内容归生成**：Prefab 持"结构 + 相对关系 + 资产引用"；
   装配期只写"关卡参数"；运行期只造"内容"。
2. **Prefab 里不许出现关卡数据**：任何由 `LevelCatalog` 推出的数值（尺寸/位置/瓦片数/小地图 arena 尺寸）
   都不许烘进 Prefab 的序列化值。
3. **生成脚本不许再连稳定结构的引用**：Prefab 化完成后，装配脚本只实例化 + 写参数，
   不再用 `SerializedObject` 写稳定结构的字段。
4. 目标结构（4 个 Prefab，落在 `Assets/Prefabs/PirateCrew/Battle/`）与分两步走的落地顺序、
   以及「改动前后必须等价」的验证方法（`SceneWiringAudit` 转储 diff），见审计报告 §四/§六。

## 五、改这个目录前先跑什么

```bash
# 转储当前场景接线（人读 txt + 机读 tsv + 统计 summary），产物默认落仓库根 external/scene-audit/
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.SceneWiringAudit.DumpBattleFromCommandLine -logFile -
```

改前 dump 一份、改后 dump 一份、`Diff` 一次：**每条差异都必须能解释**（新增/删除/字段指向变化）。
这条流程是 Battle 场景任何结构改动的验收标准，不是可选项。
