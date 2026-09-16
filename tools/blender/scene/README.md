# tools/blender/scene —— 场景资产样板（大帆船 + 木栈桥）无头建模管线

> **这份目录解决什么问题**：美术验收清单（`docs/待办事项.md` r8+）登记过两个场景件缺口——
> "大帆船（含白帆）补船体、木栈桥入水立柱"。本管线用 Blender 5.2 无头脚本**纯程序化**建模这两件
> 样板资产并导出 FBX，作为 Unity 侧 `SceneArt/`（程序化场景）的替代观感升级候选。
> **零外部素材、零下载贴图**：全部 Blender 图元 + Principled BSDF 纯色，配色取项目调色板。

## 复现命令

仓库根 `F:/VSCode/pirate-crew-3d-unity/` 执行（约 90 秒，OPTIX/CUDA 不可用时自动回退 CPU）：

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/scene/build_scene_kit.py
# 可选参数：
#   --only flagship|dock   只重建一件
#   --samples N            预览图采样数（默认 96）
#   --no-render            只建模 + 导出 FBX，不出预览图
```

**产物**（重跑幂等覆盖）：

| 路径 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Models/SceneKit/Flagship.fbx` | 大帆船（单网格 5 材质槽） |
| `pirate-crew/Assets/Art/Models/SceneKit/Dock.fbx` | 木栈桥（单网格 3 材质槽） |
| `export/scene-kit-pilot/flagship-{front34,side,back}.jpg` | 船预览 1024²（艏3/4 / 正侧 / 艉3/4，中性灰背景 + 三灯） |
| `export/scene-kit-pilot/dock-{front34,side,back}.jpg` | 栈桥预览 1024²（同上） |
| `external/scene-kit-work/scene_kit_debug.blend` | 调参用 GUI 缓存（gitignored） |

控制台会打印每件资产的统计（`STAT` 行：三角面数 / 材质槽 / 包围盒）。

## 实测口径（2026-09-16 运行值）

| 项 | 大帆船 Flagship | 木栈桥 Dock | 任务硬指标 |
| --- | --- | --- | --- |
| 三角面数 | **3701** | **3404** | 3000~15000 / 件 |
| 材质槽 | Kit_WoodMid / Kit_WoodDark / Kit_Sail / Kit_Brass / Kit_Rope（5） | Kit_WoodMid / Kit_WoodDark / Kit_Rope（3） | ≤8/件，Kit_ 前缀 |
| 包围盒 L×W×H | **15.85 × 6.00 × 7.74** | **6.00 × 2.05 × 1.60** | 船 约12-16×4-6×5-7；桥 6×2×~1 |
| 原点 | 船长中点**水线**处（z=0 水线） | **桥面顶面中心**（z=0 桥面顶） | — |
| Unity 落位 | 根节点 y = **-0.4**（水面） | 根节点 y = **0.25**（= -0.4 + 桥面高 0.65） | 水面 y=-0.4 |

船高补充：水线以上 6.72（主桅顶 6.5 + 桅帽，在"主桅高 5~7"口径内）；包围盒 H 7.74 含水线下吃水 1.02（龙骨/舵）。
船长 15.85 含艏斜桁悬出（船体本体 13.0）。

### 结构清单

- **大帆船**：23 站放样船壳（半宽剖面/舷弧/内倾舷墙同 `ShipHullGeometry.cs` 口径）+ 平坦开阔主甲板
  （走道无凸起，瓦片平甲板可覆盖）+ 艏楼 2.75 / 艉楼 2.95（高出舷墙的阶梯剪影，中段走道不挡）+
  双桅（主 6.5 / 前 5.9）+ 双横帆（微鼓风弧面）+ 艏三角帆 + 栏杆帽/腰带饰条 + 黄铜炮×4 +
  艉窗/艉廊栏杆/艉灯 + 鸦巢 + 锚 + 舵 + 9 根缆绳。
- **木栈桥**：宽向 5 块板**错缝拼装**（几何板缝 = 凹槽感，不贴图）+ 边梁/托梁 + **6 根入水立柱**
  （底 -1.2，水面在 -0.65 → 插入水下 0.55；4 根高出桥面 0.34 作系船柱）+ 系缆绳圈×6 +
  底撑×3 + 尾端下水梯（梯身越过水面线，读得出"入水"）。

### 坐标与朝向（Unity 侧必读）

- Blender 内：+Z 上、船长沿 Y、**船艏朝 -Y**；FBX 导出 `axis_forward='-Z', axis_up='Y'` →
  **FBX 空间 +Y 为上、-Z 为前**；Unity 侧导入配 `bakeAxisConversion=true` 后**船艏朝 Unity +Z**。
- 缩放：Blender 1 单位 = 1 米；导出 `apply_scale_options='FBX_SCALE_NONE'`；
  Unity 导入必须 `useFileUnits=true; useFileScale=false; globalScale=1`（设 useFileScale=true 会把
  cm×0.01 乘进来，1.9 米变 0.019——实测踩过）。
- 材质：`materialImportMode=None`（2022.3 已移除 `importMaterials` 属性，用了编译错误），
  由 `Assets/Editor/SceneKitPilot/SceneKitPilotSetup.cs` 按槽名前缀 `Kit_` 用 URP/Lit 重建
  （色值常量表与 `build_scene_kit.py` 同源；Kit_Sail 设 Render Face=Both 双面）。

## 调参行号（`build_scene_kit.py`）

| 要调什么 | 行号 | 参数 |
| --- | --- | --- |
| 采样/分辨率/出图开关 | 48-52 | `ONLY` / `SAMPLES` / `RENDER` / `RES` / `JPG_QUALITY` |
| 配色 | 55-59 | `C_WOOD_MID` / `C_WOOD_DARK` / `C_SAIL` / `C_BRASS` / `C_ROPE` |
| 船主尺度 | 62-70 | `SHIP_LEN` / `SHIP_HALF_BEAM` / `SHIP_DECK_Z` / `SHIP_KEEL_Z` / `SHIP_BULWARK` / `SHIP_SHEER_AMP` / `SHIP_STATIONS` / `SHIP_MAST_TOP_*` |
| 帆 | 71-75 | `SAIL_MAIN_W/H` / `SAIL_FORE_W/H` / `SAIL_BULGE` |
| 桥主尺度 | 80-86 | `DOCK_LEN` / `DOCK_WIDTH` / `DOCK_PLANK_T` / `DOCK_PILING_R` / `DOCK_PILING_BOT` / `DOCK_MOOR_TOP` / `DOCK_GAP` |
| 船壳剖面形状 | 276 | `HULL_SECTION`（横向×垂向控制点，0=龙骨 1=栏杆） |
| 三角帆形状 | `build_flagship` 内 `tack/head/clew` 三点 + 鼓风深度 | 约 470-490 行 |
| 绳圈面数（栈桥面数大头） | 568 | `rope_ring` 的 `ma, mi`（20×8） |

## Unity 侧接线（本目录不管，只给口径）

- 编辑器组装入口：`PirateCrew.EditorTools.SceneKitPilotSetup.BuildAll`（`-executeMethod` 可调），
  产出 `Assets/Scenes/SceneKitPilot.unity`（水面 + 沙岸 + 两件资产 + 暖阳 + 双机位）。
- 运行时出图：播放器传 `-sceneKitOut <绝对目录>`，`SceneKitPilotCapture` 绕拍 6 张 1280×720 PNG 后
  自动退出（0=成功 / 1=失败）。
- **场景已由协调者手工登记**进 `ProjectSettings/EditorBuildSettings.asset`（本机 batchmode 下
  `EditorBuildSettings.scenes` API 改动不落盘，脚本侧不做登记）。
- 验收判图：Blender 侧 6 张预览在 `export/scene-kit-pilot/`；Unity 侧实机图由协调者采集后归档。
