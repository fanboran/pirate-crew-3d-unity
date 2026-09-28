# tools/blender/scene/chemplant —— 第三样板关「废弃化工厂」无头建模管线

> **这份目录解决什么问题**：做**一个整场景样板关**——废弃化工厂 + 旁边的办公楼，56 × 40 m 的完整厂区
> （主装置区 / 罐区 / 管廊 / 旁楼 / 场地 / 杂物六件），由 6 名建模队员（Agent）**并行分件建模**、
> 协调者总装导出 FBX 并出成品图。管线仍是 `tools/blender/scene/` 那套：Blender 无头 + 纯程序化 +
> `style_tokens` 单一风格源。
>
> **为什么是"六件并行"**：整场一次写完是串行长尾，且单文件易冲突；拆成六个**分区互不重叠**的
> `mod_*.py`（一人一文件）后，六条 Blender 进程可同时跑，总装脚本只负责按 `AREAS` 表平移与出图。

## 一、零贴图纪律（**先读这条再动手**）

本项目渲染管线是**像素化着色路径**（[docs/技术/渲染/像素化着色路径/实现口径.md](../../../../docs/技术/渲染/像素化着色路径/实现口径.md)）：
物体 pass 只写 `Albedo`（亮部色）+ `Physical`（光滑度/金属度）+ `Palette`（主光档数/抖动/边光/描边开关），
着色跑在**低分辨率艺术画布**上，还要过**帧级调色板**。推论：

- **PBR 贴图 / 金属度在链上落不住**——细节被分档量化与调色板吃掉，低分辨率下还会变成闪烁噪点。
  所以本 kit **零贴图、零金属度**：材质一律 `style_tokens.SLOTS` 里的**平色槽**，`Metallic = 0`。
- **「质感」只能由两件事承担**：① **几何密度**（板缝 / 法兰 / 铆钉排 / 爬梯 / 栏杆 / 管件 / 格栅）；
  ② **调色板对比**（锈 / 混凝土 / 漆色的相邻搭配，锈痕用**薄板与细环带贴面**做，不整面上锈色）。
- 材质槽名一律 `Kit_` 前缀（Unity 侧换装键），**签名同源表**：本 kit 用的工业槽已登记进
  `style_tokens.py` 的 `SLOTS` 与 `pirate-crew/Assets/Editor/WorldMapAssetSetBuilder.cs` 的 `Slots` 表。

## 二、目录与文件

| 文件 | 谁写的 | 干什么 |
| --- | --- | --- |
| `kit_common.py` | 协调者 | **公共几何 + 材质库**：`Mesher` 图元（box/cyl/revolve/sphere/poly_extrude/extrude_along/tube/member/ibeam/pipe_member/ladder/railing/platform/flange/grating/stairs/tank_shell）、`AREAS` 分区表、材质工厂、自检渲染。分件脚本**只能**用它 |
| `mod_site.py` | 地王 SITE-05 | 场地：地坪分块板缝 / 道路 / 围墙大门 / 荒草 / 排水 |
| `mod_towers.py` | 塔王 TOWER-01 | 主装置区：精馏塔 / 吸收塔 / 卧式反应器 / 烟囱 / 钢结构框架塔 |
| `mod_tanks.py` | 罐王 VAT-02 | 罐区：立式储罐 / 球罐 / 双曲线冷却塔 / 围堰 |
| `mod_pipes.py` | 管王 PIPE-03 | 管廊带：多层管桥 / 管束 / 阀门站 / 泵组 / 断裂坠落管段 |
| `mod_building.py` | 楼王 BLOCK-04 | 旁楼：4 层办公楼（窗带/入口/屋顶设备）+ 单层仓库附属 |
| `mod_props.py` | 破烂王 SCRAP-06 | 散落杂物：油桶 / 集装箱 / 废料堆 / 废车 / 电线杆电缆 / 路障 |
| `preview_module.py` | 协调者 | **单件自检器**：建一件 → STAT → 出图（分件脚本收工前必跑） |
| `selfcheck_kit.py` | 协调者 | 公共库图元冒烟自检（改 `kit_common.py` 后必跑） |
| `assemble_chemplant.py` | 协调者 | **总装**：逐件建模 → 按分区平移 → STAT → 导出 FBX → 出成品图 |

## 三、复现命令

```bash
B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
cd F:/VSCode/pirate-crew-3d-unity            # worktree 里则 cd <worktree>

# ① 公共库冒烟（改 kit_common.py 后必跑；--render 再出一张自检图）
"$B" -b --factory-startup -P tools/blender/scene/chemplant/selfcheck_kit.py -- --render

# ② 单件自检（建模迭代时跑这个，秒级；出图落 external/chemplant-work/<件名>_check.jpg）
"$B" -b --factory-startup -P tools/blender/scene/chemplant/preview_module.py -- towers --res 640 --samples 16
#    可选：--cam x,y,z  --target x,y,z  --no-render  --out <路径>

# ③ 总装 + 导出 FBX + 出成品图（分钟级；--pixel 3 另出低分辨率档参考图）
"$B" -b --factory-startup -P tools/blender/scene/chemplant/assemble_chemplant.py -- --samples 48 --pixel 3
#    可选：--only site,towers  --views hero,street  --no-render  --no-export  --res 1440
```

## 四、坐标与分区口径

- Blender Z-up，**1 单位 = 1 米**，地面 `z = 0`，+Y 北（背离镜头），−Y 是大门与主立面朝向。
- 场地 `56 × 40 m`（X ∈ [−28, 28]、Y ∈ [−20, 20]）；俯视分区图与 `AREAS` 表在 `kit_common.py` 顶部：

```
┌──────────────────────────────────────────────────────┐ Y=+20
│  towers  X[-27,-7] Y[5,19]    tanks X[0,26] Y[5,19]   │
│  ─── 南北支路 X[-6.5,-3.5]（Y=-8 直上 Y=18）───        │
│  pipes 管廊 X[-27,27] Y[-0.1,4.5]   ← 抬高跨过支路     │
│═══ 东西主路 Y[-8,-4]（西端 X=-28 接大门）═════════════│
│  props 南院 X[-27,0] Y[-19,-8.5]   building X[2,24]   │
└──────────────────────────────────────────────────────┘ Y=-20
 X=-28                                              X=+28
```

- **每件在本地系建模**（原点 = 自己分区盒中心在地面的投影），总装时按 `AREAS[名]['center']` 平移；
  分件脚本**不关心**自己在场地哪儿，但**不许**把主体长出自己那个盒子（管道口/悬挑 ≤1.5 m）。
- FBX 导出沿用 `style_tokens.export_fbx`：`axis_forward='-Z', axis_up='Y'`、`FBX_SCALE_NONE`、
  `path_mode='COPY'`（零贴图所以无外部依赖）⇒ Unity 侧 +Y 上、1:1 米制。

## 五、成品图（写实质感的来源）

成品渲染用**渐变天穹 + 日光 + 冷补光**，`Standard` 视图变换（AgX 会洗掉调色板色值），
**曝光收在 −0.7 EV**（`assemble_chemplant.py:EXPOSURE`）：`Standard` 没有高光滚降，
不收曝光时浅色混凝土/浅灰钢会直接冲成白片——这张数是用材质标定卡量出来的
（9 槽并排正视、正交相机、读回像素值），别随手改。

`--pixel N` 另出「低分辨率档参考图」：把成品图按块中心降采到 1/N 再用最近邻放大回来，
说明像素管线"块边长锁整数倍"这件事。**它是近似**：游戏内实拍另有描边、光带量化与帧级调色板。

## 六、Unity 侧接线（本 kit 只给口径，不含 C# 改动）

产物 FBX 落在 **`Assets/Art/Models/SceneKit/ChemPlant.fbx`**（一场景多节点：`ChemPlant_Site` /
`ChemPlant_Towers` / `ChemPlant_Tanks` / `ChemPlant_Pipes` / `ChemPlant_Building` / `ChemPlant_Props`，
根原点 = 场地中心、地面 y=0 —— 与 Unity 的 1 单位 = 1 米直接对齐）。

**接线是下一步的活，不在本 kit 内**，两条现成通道任选：

1. **照 `SceneKitPilotSetup`**（`Assets/Editor/SceneKitPilot/SceneKitPilotSetup.cs`）扩一件：
   同一组踩坑导入参数（`materialImportMode=None` / `useFileScale=false` / `bakeAxisConversion=true`
   / `useFileUnits=true` + `globalScale=1`），按 `Kit_` 槽名重建像素材质，再搭一个 showcase 场景。
2. **并入 WorldKit 资产表**：把 FBX 挪到 `Assets/Art/Models/WorldKit/<分类>/` 后跑
   `WorldMapAssetSetBuilder.BuildAll` —— 它的 `Slots` 表**已经登记了本 kit 用到的 9 个工业槽**
   （见 `WorldMapAssetSetBuilder.cs` 的「工业/废弃【提】」段），材质会自动重建。

**未知 `Kit_` 槽一律品红暴露**（零容忍纪律）：新增槽必须同时改
`style_tokens.py:SLOTS` 与上面两张 C# 表的其中一张，双侧同源。

## 七、实测口径

总装一次（`assemble_chemplant.py --samples 44 --res 1440 --pixel 2`，约 150 秒，OPTIX）：

| 分件 | 三角面 | 顶点 | 材质槽 | 就位包围盒（长×宽×高 m） | 建模人 |
| --- | --- | --- | --- | --- | --- |
| `mod_site.py` | 52,564 | 32,940 | 13 | 56.6 × 40.1 × 4.0 | 地王 SITE-05 |
| `mod_towers.py` | 44,842 | 25,958 | 10 | 19.3 × 13.8 × 26.4 | 塔王 TOWER-01 |
| `mod_tanks.py` | 33,444 | 19,220 | 11 | 25.9 × 14.0 × 14.2 | 罐王 VAT-02 |
| `mod_pipes.py` | 33,206 | 19,130 | 10 | 55.4 × 6.4 × 7.7 | 管王 PIPE-03 |
| `mod_props.py` | 30,060 | 17,200 | 15 | 32.4 × 17.4 × 7.4 | 破烂王 SCRAP-06 |
| `mod_building.py` | 29,124 | 18,503 | 13 | 24.4 × 12.0 × 15.1 | 楼王 BLOCK-04 |
| **全场（6 节点）** | **223,240** | — | **18（并集）** | **56.6 × 40.1 × 26.4** | — |

- **槽上限**：分件 10~15 槽、全场并集 18 槽，**都超过 `SLOT_LIMIT_PER_ASSET = 8`**——
  这是有意的：8 槽纪律针对「随海图换装的 WorldKit 件」，本 kit 的件是**关卡美术**
  （一整关，不参与换装）。`ST.print_stats(..., slot_limit=99)` 显式放宽，不静默放行。
  18 槽全部是 `ST.SLOTS` 已登记槽（含本 kit 引入的 9 个工业槽），无一自造。
- **越界校验**：`assemble_chemplant.py:verify_bounds` 逐件比对分区盒 + 1.5 m 容差，**六件全过**
  （最大余量：塔区 1.38 m，属烟囱爬梯/平台悬挑）。
- **FBX**：`pirate-crew/Assets/Art/Models/SceneKit/ChemPlant.fbx` = **2.75 MB**（6 子节点、零贴图依赖）。
- **成品图**：8 机位 1440×900 各约 13~17 s（OPTIX），图集与口径见
  [docs/images/chemplant-scene/README.md](../../../../docs/images/chemplant-scene/README.md)。

### 建模踩坑（本轮修掉的两个库缺陷，别回退）

1. **`Mesher.to_object` 的材质对齐**：材质/平滑是**按面下标**的平行数组，而 `me.validate()` 会删退化面；
   删面后按下标 `zip` 会让其后所有面的材质整体错位（真事故：草叶重复面把草绿穿到隔离墩上）。
   现在先自己剔退化面（三列同删）、再在 validate 后**断言面数不变**——不允许静默错位。
2. **`stairs(rail=False)` 的 `lng` 未定义**（`NameError`）：斜长已提到分支外计算。
3. **越界校验自身的坑**：设完 `obj.location` 后 `matrix_world` 仍是旧值（Blender 惰性求值），
   不 `view_layer.update()` 就量，量到的是本地坐标 —— 会得到"满天飞"的假越界。


