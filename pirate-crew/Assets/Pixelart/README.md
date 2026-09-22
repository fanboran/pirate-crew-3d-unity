# 像素化着色路径（Pixelart Path）——本文件夹收纳什么

> **这是整条路径的单一入口。** 创始人 2026-09-22 要求「单独提取一个渲染管线文件夹，把相关信息
> 全部提取到单一文件夹」——本文件夹就是那个落点：**资产 + 编辑器装配 + 出图判据**都在这儿。
> 运行时 C# 因为程序集（asmdef）边界**必须**留在 `Assets/Scripts/`（理由见 §2），
> 但它只有 12 个文件、且在本文件 §3 逐条点名。

## 1. 目录（AssetDatabase 路径）

| 子目录 | 装什么 | 谁产出 |
| --- | --- | --- |
| `Shaders/` | `PixelartObject`（几何 pass，写 7 张 MRT）、`PixelartOutline`（屏幕空间 4 邻域膨胀）、`PixelartRimLight`、`PixelartShading`（四趟：GI/漫反射/高光/合成）、`PixelartColorCorrection`（帧级调色板） + `Shaders/Includes/RimLight.hlsl` | 手写 |
| `Compute/` | `Connectivity/`（连通域三阶段：Check / Flood / Result + 共用 `Connectivity.hlsl`）、`RimLightCorrection.compute`、`Palette/PaletteGenerationCIEDE.compute` | 手写 |
| `Materials/` | 本路径专用的物体材质（`PixelartPilot_*` 试点件、`PixelartCrew_*` 角色三色、`PixelartCloud_*` 云场件）。**全部只吃 `PixelartObject`**，`_MainLightLevel` = 色带档数 | `Editor/PixelartStageKit.EnsureMaterial` |
| `Textures/Dither/` | 九张 v3 口径的 1-bit 密度图案（`_DitherMode = 1` 时用） | `Assets/Editor/DitherPatternBaker.cs` |
| `Palette/Palette.asset` | 帧级调色板 LUT（**当前默认不启用**，见 §4） | `Editor/PixelartPathInstaller.EnsurePaletteAsset` |
| `Editor/` | 装配器（`PixelartPathInstaller`）、三个试点场景装配（`PixelartPilotSetup` / `PixelartLevelPilotSetup` / `PixelartWorldMapPilotSetup`）、各场景共用件（`PixelartStageKit`） | — |

**渲染器资产不在这里**：`Cast` / `Screen` 两个 URP 渲染器资产在 `Assets/Settings/URP/`
（与既有两档 URP 资产同目录，装配器按固定顺序追加并记索引）。

## 2. 刻意留在外面的东西（改动前先看这里）

| 东西 | 位置 | 为什么不在本文件夹 |
| --- | --- | --- |
| 运行时 C#（rig / Feature / 常量） | `Assets/Scripts/PirateCrew/Rendering/Pixelart/` | 属于 `PirateCrew.Rendering` 程序集；挪出该目录会掉进 Assembly-CSharp，而 `PirateCrew.Gameplay` 是按程序集名引用它的 |
| 场景常量（取景/像素档位） | `Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartPilotScene.cs`、`PixelartLevelScene.cs` | 同上；**取景口径的唯一来源**，装配器与出图脚本都读它 |
| 出图脚本（播放器侧） | `Assets/Scripts/PirateCrew/ArtReview/PlayerArtCapture.cs` | 与既有出图链同文件（`-artReviewOut` / `-toonPilotOut` / `-pixelartOut` 共用一个入口） |
| 判据脚本 | `tools/pixel-review/judge_pixelart_pilot.py`（另 `ink_gap_probe.py`） | 仓库工具目录（Python） |
| 场景（试点 + 样板关 + 八张海图） | `Assets/Scenes/PixelartPilot.unity`、`PixelartCloud.unity`（关卡 1）、`PixelartSkyIsland.unity`（关卡 3）、`PixelartMap101.unity`…`PixelartMap108.unity`（海图 101–108）；**关卡 2 已删除**（2026-09-22），关卡号有意不连续 | Unity 场景必须在 `Assets/Scenes/`（Build Settings 与出图链按名切换） |
| 档案（每轮出图 + 判据读数） | `docs/images/pixelart-path/r*/README.md` | 文档区 |
| 实现口径 / 接口契约 | `docs/技术/渲染/像素化着色路径.md`、`像素化着色路径-P4P5接口契约.md` | 文档区 |

## 3. 字面量表：值只在单一来源里，这里给「去哪找」

**本表故意不抄值**——抄一份就是第二个来源，迟早漂。要精确值时 grep 左列的名字。

| 想找什么 | 单一来源 |
| --- | --- |
| 像素档位（一个艺术像素占几屏幕像素）、俯角/方位/机位距离、可见米数梯子 | `PixelartPilotScene`（`PixelScale` / `PitchDegrees` / `AzimuthDegrees` / `CameraDistance` / `Wide|Mid|CloseVisibleMeters`）。**俯角也是游戏内相机的来源**：`BattleCameraController.OrthoPitchDegrees` 直接引用 `PitchDegrees`（30°），出图与游戏内必须是同一个投影 |
| **真实内容关卡的场景名/构图中心/可见米数** | `PixelartLevelScene`（`All` 表；一行一关，含 `ShotPrefix` 与 `CameraDistanceFor`）；样板关 1/3 + 海图 101–108 |
| 双档缓冲尺寸、G-buffer/结果缓冲的分配与随机写位 | `PixelartCameraRig`（`NewBuffer` / `NewColor` 调用处） |
| 全局纹理与常量名（`_Pixelart*`）、pass 名、asset 路径、特征顺序 | `PixelartPath`（**唯一登记处**，别在别处写字面量） |
| 装配顺序（7 个 Feature 的执行次序） | `PixelartPathInstaller.CastFeatureOrder` |
| 关卡试点场景装配（含空岛合成、派生材质） | `PixelartLevelPilotSetup`（编辑器侧）；共用件 `PixelartStageKit` |
| 海图试点场景装配（`WorldMapComposer.Build` 合成 + 远景环排除 + 站面三档显式材质） | `PixelartWorldMapPilotSetup`（编辑器侧） |
| 逐物体材质参数的配方（色带档/描边开关/吸附/抖动图案） | `PixelartStageKit.EnsureMaterial` |
| 太阳高度/方位、环境暗部色的取值理由 | `PixelartStageKit.CreateSunAndAmbient`（为什么 58° 写在那里） |
| 出图档位（机位/抖动/调试档） | `PlayerArtCapture.PilotShots` / `LevelShots` |
| 判据阈值（块边长/平坦占比/亮暗跨度/场地占比/光照未旁路） | `tools/pixel-review/judge_pixelart_pilot.py` 顶部常量区（**两档判据**见模块 docstring） |
| 调试档编号（0–5、8/9 的语义） | 契约文档 §8 落地口径表 |

## 4. 怎么跑（全部 batchmode，一次只跑一个 Unity 进程）

```bash
U="F:/Unity/2022.3.62f1c1/Editor/Unity.exe"
P="F:/VSCode/pirate-crew-3d-unity/pirate-crew"

# ① 装配渲染器（两档 URP 资产各追加 Cast/Screen；幂等）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartPathInstaller.Install -logFile -

# ② 烘试点场景（图元几何，验机制）、样板关试点场景（L01 云场 / L03 空岛，真实内容）、
#    八张海图试点场景（关卡 101–108，真实内容；一次烘一批）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartPilotSetup.BuildAll -logFile -
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartLevelPilotSetup.BuildAll -logFile -
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartWorldMapPilotSetup.BuildAll -logFile -
#    只烘一张海图（调试）：把 BuildAll 换成 BuildLevel101…BuildLevel108

# ③ 出包（开发版；全部试点场景都在开发场景集里，不进发行包）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs \
  -buildFlavor development -buildScenes development -logFile -

# ④ 出图 + 判据（判据必须跑程序化那一套，不靠"看着像"）
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-r8"                  # 试点场景（档位名 pa-*）
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-l1-r9" -pixelartLevel 1   # 关卡 1（pl1-*）
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-l3-r9" -pixelartLevel 3   # 关卡 3（pl3-*）
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-map101" -pixelartLevel 101 # 海图（pl101-*）
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-map108" -pixelartLevel 108 # 海图（pl108-*）
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-r8
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-l1-r9
```

**已知的两个默认关闭项**（都留了开关，别当成漏做）：

1. **实时阴影**：机制接通（Cast 相机 `renderShadows`、着色里的阴影关键字族、URP 资产开关），
   但 160m 大平面在阴影贴图里自遮挡 ⇒ 先关；开关在 `PixelartStageKit.CreateSunAndAmbient`。
2. **帧级调色板**：LUT 索引口径未对（会把背景蓝灰映射成暗紫）⇒ 装配器里 `EnableFramePalette = false`；
   修好后改回 `true` 并重跑装配器 + 出包。

## 5. 判据与档案

- 判据分**两档**：机制档（`pa-*`，试点场景）四项全为硬门禁；观感档（`pl<关卡>-*`，真实内容）
  硬门禁是块边长/色数/墨线/**光照未旁路**（同机位比对最终图与 albedo 调试图）/场地在场，
  平坦占比与亮暗跨度在这档只打印读数（真实关卡结构密，见判据脚本的模块 docstring）。
- 每轮实拍与读数归档在 `docs/images/pixelart-path/r<轮次>/README.md`；
  最新一轮（r12）是**十关的中机位统一到 14 m**（创始人：比 16 m 略近；14 m = 游戏内正交档 7）；
  r11 是八张海图的观感图（wide 32 m）+ 整图总览；r10 是关卡 1/3 的全套效果图（各 6 档）；
  r9 是三个关卡接入 + README 换图（第 2 关随后被删除，其缺陷记录留档）；
  r8 含**角色台柱底径调整**与**云彩关（关卡 1）接入**。
