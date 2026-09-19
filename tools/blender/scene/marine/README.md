# tools/blender/scene/marine —— 船与码头 kit（WorldKit-Marine）无头建模

> **这份目录解决什么问题**：M4 大海域世界化（docs/技术/M4-大海域世界化.md §4.3 marine 域）的 9 件
> 船与码头资产，纯 Blender 无头程序化建模，与样板 `build_scene_kit.py`（Flagship/Dock）同风格同
> 质量线的"残破/功能性扩展"。零贴图：细节全用几何（错缝板、撕裂板缘、焦黑区 Kit_WoodDark、
> 锈蚀 Kit_Iron、缆绳盘绕、破洞暗腔+焦黑框）。槽/预算/站面工具一律 `import style_tokens as ST`。
> 规格/任务书见 M4 §4.3 与会话任务单（FBX 名固定，9 件清单）。

## 复现命令

仓库根 `F:/VSCode/pirate-crew-3d-unity/` 执行：

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/scene/marine/marine_kit.py
# 可选参数：
#   --only WreckBowHalf|WreckSternHalf|MastBridge|LighthouseTower|PierLong|PierHead|
#          BuoyRing|RowboatBeached|AnchorMonument|all   只重建一件（默认 all）
#   --samples N      预览图采样数（默认 96；迭代期 48 加速）
#   --no-render      只建模 + 导出 FBX/manifest，不出预览图
```

**产物**（重跑幂等覆盖）：

| 路径 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Models/WorldKit/Marine/<名>.fbx` | 9 件资产（单网格多 Kit_ 槽） |
| `pirate-crew/Assets/Art/Models/WorldKit/Marine/<名>.standable.json` | 站面 manifest（见下） |
| `docs/images/worldkit-marine/<名>-front34.jpg / <名>-side.jpg` | 预览 1024² q90（Standard 视图变换，灰底三灯同样板） |
| `external/worldkit-marine-work/marine_kit_debug.blend` | 调参 GUI 缓存（gitignored） |

## 实测口径（2026-09-17 终版运行值，marine 预算 ≤12000 tri 全过）

| 资产 | tris | 槽数 | 包围盒 L×W×H（Blender 米） | 原点 |
| --- | --- | --- | --- | --- |
| WreckBowHalf | 5292 | 5 | 6.20×16.97×6.38 | 足印中心 z=0 海平面（艏翘 6° 视觉；水下 ≈-2.9） |
| WreckSternHalf | 5668 | 6 | 6.53×13.12×5.16 | 足印中心 z=0 海平面（船底搁浅触地 ≈-1.1） |
| MastBridge | 2642 | 8 | 4.74×18.24×6.44 | 足印中心 z=0 海平面（端墩入水 -1.2） |
| LighthouseTower | 2462 | 8 | 18.07×17.99×12.04 | 足印中心 z=0 海平面（岛台裙边入水 -1.3） |
| PierLong | 3916 | 4 | 4.00×18.00×4.15 | 足印中心 z=0 海平面（立柱入水 -2.6） |
| PierHead | 3332 | 4 | 8.00×10.95×4.21 | 足印中心 z=0 海平面（立柱入水 -2.6） |
| BuoyRing | 1028 | 3 | 1.38×1.35×2.39 | 浮体吃水接触面中心（随波姿态 9°/4°） |
| RowboatBeached | 909 | 5 | 2.33×5.13×1.49 | 龙骨触地接触面中心（搁浅侧倾 7°） |
| AnchorMonument | 1484 | 6 | 2.68×2.62×3.58 | 石墩底接触面中心 |

结构要点（残破语言与 Flagship/Dock 同源）：WreckBowHalf＝放样船壳（艏 6° 翘起只进视觉壳）+
甲板 3 段平台阶 0.5/1.0/1.5 + 断裂面撕板/肋骨 + 断裂下垂艏斜桅 + 舷侧破洞×2（凸出焦黑框）+
焦黑区 + 盘缆；WreckSternHalf＝艉楼 1.0/主甲板 0.5 双平台 + 三拱窗（黄铜框+联系梁）+ 舵残件 +
断口焦黑区 + 烧断桅桩；MastBridge＝倒伏主桅当桥（中段 1.6 断口=跳距教学点）+ 瞭望斗撕裂甲板
3×3 @+4.5 + 绳梯/垂残梯 + 帆布垂条；LighthouseTower＝圆角方岩台岛（台面岩板+系船铁环+半埋礁裙）
+ 锥柱塔身（5 道砌石环带）+ 铁栏环台 Ø4 @4.0 + 灯室平台 Ø3.5 @7.5（Kit_Ember 玻璃微亮，
仅预览 emission，Unity 侧按槽名重建不受影响）+ Kit_Iron 顶锥帽；PierLong＝18×4 错缝桥面 +
3 对入水立柱 + 两侧系船柱 + 一段断垂栏索；PierHead＝8×8 桥头 + 90° 转角引桥段 + 双系船柱 +
堆缆圈。

## 站面 manifest（<名>.standable.json）

格式与 `ST.write_standable_manifest` 完全一致：`{"asset":名, "boxes":[{"c":[x,y,z],"s":[sx,sy,sz]}, …]}`，
Blender 本地系（Z-up、米），box 可带可选 `"yaw"`（度，绕资产本地原点；协调者 2026-09-17 约定，
本套件几何全部正交故未用）。每件导出前先 `ST.check_standable_boxes`：全过则直接走 ST 写出。

**放宽档（用户 2026-09-17 裁决）**：含水平最小边 < `MIN_STANDABLE_SIZE`(4.0) 窄站面的资产
（这些窄面是关卡设计特征，规格表保留），由 `standable_write()`（marine_kit.py:196）本地放宽校验后
写出——仍强制顶面 0.5 档 / `MIN_TOP_Z` / 正高度 / 有限数，仅 min-size 降级为告警并在控制台打
`RELAXED` 行。放宽件清单：

| 资产 | 放宽 box | 其余 box |
| --- | --- | --- |
| WreckBowHalf | 第 3 级台阶（近艏收窄 ≈3.1×4.2 @+1.5） | 前两级台阶 ≈4.6×4.0 @+0.5、4.5×4.5 @+1.0 |
| MastBridge | 走道两段 1.6×7.0 @+1.5；瞭望斗 3×3 @+4.5（返工单补入） | — |
| LighthouseTower | 灯室平台 3.5×3.5 @+7.5 | 岛台 11.4×11.4 @+0.5、环台 4.0×4.0 @+4.0 |

其余 6 件：WreckSternHalf（4.4×6.8 @+0.5、4.0×4.0 @+1.0）、PierLong（4.0×17.6 @+1.0）、
PierHead（7.7×7.7 @+1.0）走 ST 全过；BuoyRing/RowboatBeached/AnchorMonument 无站面（空 boxes）。
灯塔跳距链：0.5 → 4.0 → 7.5 两跳各 3.5 = MaxUpStep（M4 §2.2）恰好达标（返工单 2026-09-17）。

## 调参行号（marine_kit.py）

| 要调什么 | 行号 | 参数 |
| --- | --- | --- |
| 出图开关/采样/种子 | 55-61 | `ONLY` / `SAMPLES` / `RENDER` / `RNG_SEED` |
| 艏半截主尺度 | 64-74 | `BOW_LEN` / `BOW_TILT_DEG` / `BOW_RAIL` / `BOW_STEPS`（3 平台阶 y 跨+顶高） |
| 艉半截主尺度 | 77-88 | `STN_LEN` / `STN_CASTLE_STEP` / `STN_CASTLE_HALF` 等 |
| 断桅桥 | 91-98 | `MB_SPAN` / `MB_WALK_W` / `MB_GAP`（断口）/ `MB_NEST_*`（瞭望斗位/尺寸/顶高） |
| 灯塔 | 102-112 | `LH_ISLAND*` / `LH_RING_TOP`（环台顶，跳距链）/ `LH_LAMP_*` / `LH_CAP_TOP` |
| 长栈桥/桥头 | 116-127 | `PL_*` / `PH_SIZE` / `PH_TOP` / `PH_STUB`（转角段矩形） |
| 浮标/小艇/锚碑 | 131-146 | `BU_*`（浮体/铃架/随波角）/ `RB_*`（小艇/姿态）/ `AM_*`（石墩/锚高） |
| 站面放宽写出 | 196 | `standable_write()`（ST 优先 → RELAXED 降级） |
| 最宽站面窗搜索 | 623 | `fit_standable_widest()`（艏段台阶取箱） |
| 各件建模入口 | 753-1496 | `build_wreck_bow` / `build_wreck_stern` / `build_mast_bridge` / `build_lighthouse` / `build_pier_long` / `build_pier_head` / `build_buoy` / `build_rowboat` / `build_anchor` |
| 预览机位 | 1683 | `ASSETS` 表 views 列（front34/side） |

## Unity 侧接线注意（本目录不管，只给口径）

- 坐标/缩放/槽名口径同 `tools/blender/scene/README.md`：`useFileUnits=true; useFileScale=false;
  globalScale=1`；`materialImportMode=None`，按 `Kit_` 槽名前缀重建 URP/Lit（hex/roughness 与
  `style_tokens.SLOTS` 同源；Kit_Iron/Kit_Brass 建议 metallic=1）。
- **碰撞不读网格**：`WorldMapAssetBuilder` 按 `.standable.json` 生成 BoxCollider（顶面严格水平、
  0.5 档）；box 在 Blender 本地系（Z-up），接线时按 M4 §4.2 转 Y-up。可选 `yaw` 字段：绕资产
  本地原点旋转（度），本套件未用但解析器请预留。
- 原点：6 个大件根节点放水面高度（y=-0.4）即可落位（z=0 海平面）；3 个小件放地面/滩面。
- Kit_Ember 灯室玻璃在 Blender 侧带预览 emission（strength 1.8，仅影响预览图）；Unity 侧若要点亮
  灯室，按槽名给该槽加自发光即可，网格无需重导。
