> **这是同目录另一套 kit（单文件版 `chemplant_kit.py`，分支 `feat/chemplant-showcase-l04`）的原 README**，
> 原样保留以免丢口径。两套 kit 的关系与各自适用范围见 [README.md](README.md) 顶部。

# tools/blender/scene/chemplant —— 第三手搓样板关「废弃化工厂」无头建模管线

> **这份目录解决什么问题**：样板关 1（云端漫步）/ 3（天空之岛）之后的手搓关卡美术件缺口。
> `chemplant_kit.py` 用 Blender 5.2 无头脚本**纯程序化**建模一座废弃化工厂的场地总装件
> （`ChemPlant_Level`）与 12 件可单独复用的构件，导出 FBX 供 Unity 侧摆场。
> **零外部素材、零下载贴图**：全部 bpy/bmesh 图元 + Principled BSDF 纯色，槽值唯一来源
> `tools/blender/scene/style_tokens.py`（工业槽位见 ST.SLOTS 的「工业/废弃【提】」段），
> 全部面 shade_flat——细节全靠几何，不靠贴图。

## 与像素化着色路径的关系（先读这条）

本 kit 的材质**只有 `Base Color`（albedo）一个量参与着色**：像素化着色路径的物体材质由
`PixelartMaterialFactory.Create(name, albedo)` 造（`pirate-crew/Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartMaterialFactory.cs`），
色带/描边/调色板都在屏幕空间那几趟里算，**粗糙度/金属度不参与**——所以
`ST.SLOTS` 里新加的工业槽位，Unity 侧真正要同步的只有 **槽名 + hex 两个值**
（表在 `pirate-crew/Assets/Editor/WorldMapAssetSetBuilder.cs` 的 `Slots`，未登记的 `Kit_` 槽会成品红）。

预览图按这条路径的成图口径出：几何渲进屏幕档 → 合成器**像素化 `block = 2`**
（与 `PixelartCameraRig.PixelScaleDefault` 同值＝1080p 下 960×540 艺术画布）→ 出图。
**不做全图色阶量化**（`PIXEL_STEPS = 0`）：游戏里的色带作用在光照上、逐物体量化，
对整幅成图逐通道量化会把 `#8C4A28` 这类低饱和锈色推成饱和红、把灰底推成粉彩环带（实测踩过）。

## 复现命令

仓库根 `F:/VSCode/pirate-crew-3d-unity/` 执行（全量约 12 分钟，OPTIX/CUDA 不可用时自动回退 CPU）：

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/scene/chemplant/chemplant_kit.py
# 可选参数：
#   --only level|hall|ChemPlant_Stack,…   逗号分隔；名字可简写（'level' == ChemPlant_Level）
#   --samples N                           预览采样数（默认 48；CPU 回退时降到 1/3）
#   --no-render                           只建模导出 FBX，不出预览图（全量约 1 秒）
```

**产物**（重跑幂等覆盖）：

| 路径 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Models/WorldKit/ChemPlant/<名>.fbx` | 12 分件 + `ChemPlant_Level.fbx`（单网格多 Kit_ 槽） |
| `docs/images/chemplant-kit/ChemPlant_Level-<机位>.jpg` | 场地成品图 1920×1080 q90（像素块边长 2；七机位） |
| `docs/images/chemplant-kit/<件名>-{front34,side}.jpg` | 分件预览 1024×576 q90 |
| `external/chemplant-kit-work/chemplant_debug.blend` | 调参用 GUI 缓存（gitignored） |

控制台每件打印一行 `STAT`（三角面数 / 材质槽 / 包围盒）。

## 实测口径（2026-09-29 运行值）

| 件 | 三角面 | 槽数 | 包围盒 L×W×H（Blender X,Y,Z，米） | 说明 |
| --- | --- | --- | --- | --- |
| `ChemPlant_Level` | **59 986** | 16 | **71.19 × 48.21 × 26.65** | 场地总装（砼地坪 68×48 + 全部构件摆位 + 场内废件） |
| `ChemPlant_Hall` | 4 424 | 10 | 20.20 × 13.70 × 10.03 | 主厂房 16×10、檐 7.2 / 脊 9.0，正面坡塌口露桁架 |
| `ChemPlant_ColumnTall` | 5 550 | 10 | 9.27 × 8.22 × 19.89 | 精馏塔 H≈18.1（含穹顶/折弯放空），三层平台 |
| `ChemPlant_ColumnShort` | 3 814 | 11 | 9.27 × 7.78 × 15.06 | 同上矮塔（10.5 m 身 + 穹顶） |
| `ChemPlant_PipeRack` | 1 896 | 10 | 17.30 × 6.87 × 7.59 | 管廊 16 m、两层横梁、含塌腰段与垂吊断管 |
| `ChemPlant_SphereTank` | 4 072 | 9 | 10.77 × 10.30 × 13.73 | 球罐 R=4.0 支于 6 腿 + 赤道走道 |
| `ChemPlant_TankFarm` | 5 196 | 9 | 21.61 × 10.90 × 10.34 | 三立式罐（3.2/2.6/2.2 m 半径）+ 围堰 + 联通管 |
| `ChemPlant_Stack` | 4 046 | 9 | 12.03 × 8.20 × 26.48 | 烟囱 26 m、顶口残缺、护笼爬梯、底部烟道 |
| `ChemPlant_CoolingTower` | 1 586 | 9 | 25.93 × 20.01 × 22.45 | 冷却塔 h=22、底 r8.0→喉 4.8→顶 5.6、12 腿架空 |
| `ChemPlant_Office` | 6 566 | 12 | 18.44 × 13.89 × 19.29 | 4 层办公楼 14×9（含消防梯/水箱/女儿墙缺口） |
| `ChemPlant_Debris` | 332 | 7 | 6.17 × 5.44 × 2.04 | 废料堆（砼块/断管/塌板/钢筋/破木托） |
| `ChemPlant_Fence` | 480 | 4 | 6.50 × 1.37 × 2.40 | 围栏段 6 m（`toppled=True` 出倒伏段） |
| `ChemPlant_PumpSkid` | 580 | 8 | 6.01 × 4.20 × 1.40 | 泵组基座（两泵 + 电机 + 阀轮 + 短管） |

分件合计 ≈ 38.5k 三角面；场地总装 60.0k。**面数无预算上限**（创始人 2026-09-29 对本关明确
「没有面数限制」）——但注意这与 `ST.POLY_BUDGETS` 的 WorldKit 件预算不同口径：本 kit 是关卡美术。
**槽数上限**：分件 12 / 总装 16（`PIECE_SLOT_LIMIT` / `LEVEL_SLOT_LIMIT`）——`ST.SLOT_LIMIT_PER_ASSET = 8`
那条纪律针对**随海图换装的 WorldKit 件**；本 kit 里"锈三档 + 砼三档 + 钢两档 + 褪色漆"共存正是
废弃质感的载体，故经 `ST.print_stats(slot_limit=…)` 显式放宽并在上表记录实际值。

## 场地布局（`ChemPlant_Level`，Blender 局部系；X 东、Y 北、Z 上，原点 = 场地中心）

| 位 | 构件 | 位（x, y） | yaw |
| --- | --- | --- | --- |
| 西侧 | 办公楼（入口朝东对厂区） | (-19, 6) | 0° |
| 北侧 | 主厂房（正面卷帘门朝南对院子） | (-1, 14) | 0° |
| 北侧 | 管廊（东端接厂房出墙管桥） | (13, 14) | 0° |
| 中东 | 精馏塔 高 / 矮 | (13, 7.5) / (18.5, 1) | 0° |
| 东侧 | 球罐 | (24, 3.5) | 0° |
| 中南 | 罐区（围堰开口朝南） | (0, -9) | 0° |
| 东北 | 烟囱 | (30, 15) | 0° |
| 西南 | 冷却塔 + 池 | (-17, -13) | 0° |
| 场内 | 泵组基座 ×2、倒伏大管、围栏 10 段（含 1 段倒伏）、废料堆 ×9、锈桶簇 ×8、碎石堆 ×10、荒草 ≈80 簇 | — | — |

场地边界 68 × 48 m，围栏沿南/东/西边成列（段间留缺口＝已塌）。**这是玩法尺度的场地**：
整场正交布局、无高差台阶，1 格 = 2 单位口径下约 34×24 格。

## 废弃质感的做法（不贴图，全靠几何 + 落面）

- **锈痕**：`streak_matf(base, seed, sides, cover)` —— 按「环带 × 扇区」确定性落 `Kit_Rust`/`Kit_RustDark`
  面：一部分扇区自第二环带起整条竖流，另有点状锈斑；塔/罐/烟囱/球罐各给不同 seed。
- **破窗**：`window_band(...)` 一条窗带 = 内侧暗背板 + 竖梃 + 玻璃片；每 bay 按
  `(k*13 + |a0|*7 + seed) % 5` 分派：0 = 空洞（玻璃全掉，透到暗背板）、1 = 钉了块烂木板、
  2 = 只剩一角残玻璃、其余 = 完整深色玻璃。**空洞是真几何空洞**，不是贴图。
- **塌顶 / 断墙**：屋面按 X 分段放样，中段不铺板 → 露出桁架 + 断檩 + 一块歪斜下垂的挂板；
  办公楼顶层消防梯整段不建，只留两端断梁 + 折弯斜梁 + 悬垂栏杆。
- **罐体凹瘪**：`_v_tank(dent=True)` 顶两环的 4 个扇区半径 ×0.84 → 凹进去；顶锥面省掉并补
  一圈暗色内盖，读作"顶被掀掉"。
- **荒草 / 苔痕**：`weeds()` 每簇 4 根细瘦歪斜草叶（薄板三棱柱，`Kit_GrassDark/GrassMid`）；
  墙根另有一条贴地苔带。
- **裂缝 / 积水 / 泥地**：`crack()` 折线薄片（`Kit_ConcreteDark`）、`patch_poly()` 贴地不规则薄片
  （`Kit_WetSand` 积水 / `Kit_SandDark` 泥地 / `Kit_RustDark` 锈水）。

## 坐标与朝向（Unity 侧必读）

- Blender 内：+Z 上、X 东、Y 北；FBX 导出 `axis_forward='-Z', axis_up='Y'`（`ST.export_fbx`）。
- 每件原点 = 落地接触面中心、底面 Z=0；场地总装件原点 = 场地中心、地坪顶面 Z=0。
- 缩放：Blender 1 单位 = 1 米；导出 `FBX_SCALE_NONE`；Unity 导入口径与 WorldKit 全套一致
  （`useFileUnits=true; useFileScale=true; bakeAxisConversion=true; materialImportMode=None`，
  见 `WorldMapAssetSetBuilder.ApplyImportSettings` 的实测注释）。
- 材质由 Unity 侧按 `Kit_` 槽名前缀重建（`WorldMapAssetSetBuilder.ApplyKitMaterials`）。

## Unity 侧接线（现状）

FBX 落在 `Assets/Art/Models/WorldKit/ChemPlant/`（**跟随 WorldKit 目录**是为了复用现成的
`WorldMapAssetSetBuilder.BuildAll`：无头一条命令即完成导入设置 + `Kit_` 换装 + 资产表登记）。
新槽位已同步进 `WorldMapAssetSetBuilder.Slots` 与本 kit 的 `style_tokens.SLOTS`（两处同源，改一处必改另一处）。

**尚未接线**（下一步，未做）：ChemPlant 件目前不进任何场景——既没有摆位数据（样板关走
`ShowcaseLevels.BakedPlacements`），也没有进选关页；关卡设计文档见
[docs/设计/关卡/L04-废弃化工厂.md](../../../../docs/设计/关卡/L04-废弃化工厂.md)（提案/待定）。

## 调参行号（`chemplant_kit.py`）

| 要调什么 | 位置 |
| --- | --- |
| 采样 / 分辨率 / 像素块 / 色阶量化 | 45-56 行（`SAMPLES` / `KIT_W,H` / `LEVEL_W,H` / `PIXEL_BLOCK` / `PIXEL_STEPS`） |
| 槽上限（分件 / 总装） | 58-60 行（`PIECE_SLOT_LIMIT` / `LEVEL_SLOT_LIMIT`） |
| 厂房主尺度 | `build_hall` 上方的 `HALL_L / HALL_W / HALL_Z0 / HALL_EAVE / HALL_RIDGE` |
| 烟囱 / 冷却塔 / 罐区尺度 | 各 `build_*` 开头的 `zs`/`rs`（环带高度与半径表） |
| 场地布局（摆位表） | `build_level` 的 `place(acc, build_*, x, y)` 段 |
| 场内废件散布（桶/料/草/碎石） | `build_level` 的三段 `for cx, cy in (…)` 表 |
| 机位（场地七机位 / 分件两机位） | `make_level_views()` / `make_views()` |
| 影棚灯光 | `setup_stage(...)` 调用处（场地在 `main` 里传 `key/fill/rim/sky/key_h/ground_hex`） |