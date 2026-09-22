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
| `Editor/` | 装配器（`PixelartPathInstaller`）、两个试点场景装配（`PixelartPilotSetup` / `PixelartCloudPilotSetup`）、两场景共用件（`PixelartStageKit`） | — |

**渲染器资产不在这里**：`Cast` / `Screen` 两个 URP 渲染器资产在 `Assets/Settings/URP/`
（与既有两档 URP 资产同目录，装配器按固定顺序追加并记索引）。

## 2. 刻意留在外面的东西（改动前先看这里）

| 东西 | 位置 | 为什么不在本文件夹 |
| --- | --- | --- |
| 运行时 C#（rig / Feature / 常量） | `Assets/Scripts/PirateCrew/Rendering/Pixelart/` | 属于 `PirateCrew.Rendering` 程序集；挪出该目录会掉进 Assembly-CSharp，而 `PirateCrew.Gameplay` 是按程序集名引用它的 |
| 场景常量（取景/像素档位） | `Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartPilotScene.cs`、`PixelartCloudScene.cs` | 同上；**取景口径的唯一来源**，装配器与出图脚本都读它 |
| 出图脚本（播放器侧） | `Assets/Scripts/PirateCrew/ArtReview/PlayerArtCapture.cs` | 与既有出图链同文件（`-artReviewOut` / `-toonPilotOut` / `-pixelartOut` 共用一个入口） |
| 判据脚本 | `tools/pixel-review/judge_pixelart_pilot.py`（另 `ink_gap_probe.py`） | 仓库工具目录（Python） |
| 试点场景 | `Assets/Scenes/PixelartPilot.unity`、`PixelartCloud.unity` | Unity 场景必须在 `Assets/Scenes/`（Build Settings 与出图链按名切换） |
| 档案（每轮出图 + 判据读数） | `docs/images/pixelart-path/r*/README.md` | 文档区 |
| 实现口径 / 接口契约 | `docs/技术/渲染/像素化着色路径.md`、`像素化着色路径-P4P5接口契约.md` | 文档区 |

## 3. 字面量表：值只在单一来源里，这里给「去哪找」

**本表故意不抄值**——抄一份就是第二个来源，迟早漂。要精确值时 grep 左列的名字。

| 想找什么 | 单一来源 |
| --- | --- |
| 像素档位（一个艺术像素占几屏幕像素）、俯角/方位/机位距离、可见米数梯子 | `PixelartPilotScene`（`PixelScale` / `PitchDegrees` / `AzimuthDegrees` / `CameraDistance` / `Wide|Mid|CloseVisibleMeters`） |
| 云彩关的构图中心与可见米数 | `PixelartCloudScene`（`Target` / `LevelNumber` / `Wide|Mid|CloseVisibleMeters`） |
| 双档缓冲尺寸、G-buffer/结果缓冲的分配与随机写位 | `PixelartCameraRig`（`NewBuffer` / `NewColor` 调用处） |
| 全局纹理与常量名（`_Pixelart*`）、pass 名、asset 路径、特征顺序 | `PixelartPath`（**唯一登记处**，别在别处写字面量） |
| 装配顺序（7 个 Feature 的执行次序） | `PixelartPathInstaller.CastFeatureOrder` |
| 逐物体材质参数的配方（色带档/描边开关/吸附/抖动图案） | `PixelartStageKit.EnsureMaterial` |
| 太阳高度/方位、环境暗部色的取值理由 | `PixelartStageKit.CreateSunAndAmbient`（为什么 58° 写在那里） |
| 出图档位（机位/抖动/调试档） | `PlayerArtCapture.PilotShots` / `CloudShots` |
| 判据阈值（块边长/平坦占比/亮暗跨度/云场占比） | `tools/pixel-review/judge_pixelart_pilot.py` 顶部常量区 |
| 调试档编号（0–5、8/9 的语义） | 契约文档 §8 落地口径表 |

## 4. 怎么跑（全部 batchmode，一次只跑一个 Unity 进程）

```bash
U="F:/Unity/2022.3.62f1c1/Editor/Unity.exe"
P="F:/VSCode/pirate-crew-3d-unity/pirate-crew"

# ① 装配渲染器（两档 URP 资产各追加 Cast/Screen；幂等）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartPathInstaller.Install -logFile -

# ② 烘试点场景（图元几何，验机制）与云彩关试点场景（L01 真实内容，验观感）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartPilotSetup.BuildAll -logFile -
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.PixelartCloudPilotSetup.BuildAll -logFile -

# ③ 出包（开发版；两个试点场景都在开发场景集里，不进发行包）
"$U" -batchmode -nographics -quit -projectPath "$P" \
  -executeMethod PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs \
  -buildFlavor development -buildScenes development -logFile -

# ④ 出图 + 判据（判据必须跑程序化那一套，不靠"看着像"）
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-r8"                 # 试点场景
"$OUT/PirateCrew3D.exe" -pixelartOut "$OUT/export/pixelart-cloud-r8" -pixelartCloud   # 云彩关
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-r8
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-cloud-r8
```

**已知的两个默认关闭项**（都留了开关，别当成漏做）：

1. **实时阴影**：机制接通（Cast 相机 `renderShadows`、着色里的阴影关键字族、URP 资产开关），
   但 160m 大平面在阴影贴图里自遮挡 ⇒ 先关；开关在 `PixelartStageKit.CreateSunAndAmbient`。
2. **帧级调色板**：LUT 索引口径未对（会把背景蓝灰映射成暗紫）⇒ 装配器里 `EnableFramePalette = false`；
   修好后改回 `true` 并重跑装配器 + 出包。

## 5. 判据与档案

- 判据：块边长（像素化是否生效）、跳变率/平坦占比（色带是否平）、色数、**亮暗跨度**
  （"光照被整屏旁路"的回归判据）、云场占比（`pc-*`，云台是否在场）、降档 A/B、墨线像素数。
- 每轮实拍与读数归档在 `docs/images/pixelart-path/r<轮次>/README.md`；
  最新一轮（r8）含**角色台柱底径调整**与**云彩关接入**两项的实拍。
