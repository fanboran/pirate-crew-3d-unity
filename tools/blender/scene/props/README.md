# tools/blender/scene/props —— WorldKit 道具套件(18 件)无头建模

> **这份目录解决什么问题**:M4 大海域世界化的道具域产出(docs/技术/M4-大海域世界化.md §4.3 分工表)。
> `props_kit.py` 用 Blender 5.2 无头脚本**纯程序化**建模 18 件道具并导出 FBX,供
> `WorldMapComposer` 在世界地图上摆放。**零外部素材、零贴图**:全部 bpy/bmesh 图元 +
> Principled BSDF 纯色(槽值唯一来源 `tools/blender/scene/style_tokens.py`),细节全靠几何
> (木板错缝/桶箍/树干分节棱化/叶簇分层/篝火石圈/断裂斜口),全部 shade_flat。

## 复现命令

仓库根 `F:/VSCode/pirate-crew-3d-unity/` 执行(全量约 10 分钟,GPU 不可用自动回退 CPU):

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/scene/props/props_kit.py
# 可选参数:
#   --only PalmTall,RockS     只重建指定件(逗号分隔,大小写不敏感;'all' 全量)
#   --samples N               预览图采样数(默认 64;CPU 回退时自动减半)
#   --no-render               只建模 + 导出 FBX,不出预览图
```

**产物**(重跑幂等覆盖):

| 路径 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Models/WorldKit/Props/<名>.fbx` | 18 件道具(单网格多 Kit_ 槽) |
| `docs/images/worldkit-props/<名>-front34.jpg / <名>-side.jpg` | 预览 1024² q90(右前上 3/4 + 正侧;Standard 视图,灰底三灯) |
| `external/worldkit-props-work/props_debug.blend` | 调参用 GUI 缓存(gitignored) |

控制台每件打印一行 `STAT`(三角面数 / 材质槽 / 包围盒);完整运行日志示例 `external/propskit-run2.log`。

## 硬口径(与任务书/style_tokens 逐条对应)

- 1 单位 = 1 米,Z 朝上;**每件原点 = 落地接触面中心、底面 Z=0**,整体向上生长,X/Y 居中;
- 全部面 **shade_flat**(MeshAcc 的 face_smooth 恒 False);
- 材质槽只用 `ST.SLOTS` 已登记槽,每件 ≤8(`ST.print_stats` 内断言);
- 面数预算:prop ≤3000 tri / prop_small ≤600 tri(`ST.POLY_BUDGETS`;RockM/RockFlat/Driftwood/
  CrateStack/RuinColumnBroken 按任务书"升 prop"档);
- **无站面 manifest、无碰撞**(纯视觉;WorldMapComposer 统一摆)。

## 实测口径(2026-09-17 运行值,run2 全量 + run3 微调)

| FBX 名 | tri | 材质槽 | 包围盒 X×Y×Z (m) | 预算档 | 内容要点 |
| --- | --- | --- | --- | --- | --- |
| PalmTall | 957 | Grass×3 + Rope + Wood×2 + WoodDark(6) | 4.11×4.11×6.86 | prop | 直立棕榈:10 棱分节树干(鞘环鼓起+隔环错缝)+ 12 片三档绿叶(V 折带+中肋)+ 椰果串 + 冠下枯柄 |
| PalmLean | 781 | 同上(6) | 3.56×3.46×5.87 | prop | 斜身棕榈:弦角 20° 渐弯,10 叶 |
| PalmDead | 366 | Rope + WoodDark + WoodLight(3) | 1.01×1.00×4.50 | prop | 枯棕榈:无冠、顶断斜口+分叉残枝×2、9 片下垂残留叶柄(Rope) |
| RockL | 248 | Rock×3(3) | 4.51×3.22×2.50 | prop | 大岩:7 环×11 棱节理放样+顶微穹+两块贴脚副岩 |
| RockM | 148 | Rock×3(3) | 2.18×1.61×1.20 | prop(≤1500 ✓) | 中岩+1 副岩 |
| RockS | 49 | Rock×3(3) | 0.75×0.81×0.60 | prop_small | 小岩 |
| RockFlat | 107 | Rock×3(3) | 3.31×2.29×0.60 | prop | 宽扁岩:顶环抖动破碎感+矮凳视觉+1 副岩 |
| Driftwood | 253 | WoodLight + WoodMid(2) | 0.60×4.00×0.98 | prop | 漂木:7 棱错缝放样、两处剥蚀凹陷段(暗色)、端斜口、3 折枝(包围盒高 0.98 含竖向残枝,主干高 ≤0.6) |
| BarrelWood | 199 | Iron + Wood×3(4) | 0.91×0.93×1.10 | prop_small | 木桶:12 棱竖板交替色、凹进桶口、铁箍×2(贴合桶身)、桶塞 |
| CrateStack | 852 | Wood×3(3) | 1.74×1.52×1.60 | prop | 2 大 1 小错叠(上层旋转 14°/38°):暗芯+角柱+横框+横板交替色板缝 |
| Campfire | 493 | Ember + Rock×3 + WoodDark(5) | 1.89×1.76×0.62 | prop_small | 篝火:石圈 10 块(岩三档)、交叉柴堆 6 根、Ember 余烬丘、4 片棱面火锥(渐高色不变) |
| CannonEmplacement | 1458 | Brass + Iron + Rock×3 + Sand×2 + WoodDark(8) | 3.33×3.54×0.82 | prop | 炮位:沙袋圈 Ø3 双层错缝(缺口朝 -Y)+石垛 3、旧舰炮 2.2(铁身 3 箍/暗木架/铜毂辐轮×2) |
| RuinColumnBroken | 395 | GrassDark + Rock×3(4) | 1.38×1.29×2.34 | prop | 残柱:20 棱竖刻槽(半径交替 0.46/0.31)、断裂斜口(与柱身同环闭合)、残饰带、柱础+碎石+苔片 |
| RuinArch | 413 | Rock×3(3) | 5.14×1.40×3.20 | prop | 石拱:两锥度柱+14 楔块拱环、两块错位裂缝+一块缺角、落地碎石(跨按柱外缘 4.06,包围盒含碎石 5.14) |
| ShipWheelPost | 290 | Brass + WoodDark + WoodMid(3) | 1.64×0.69×2.87 | prop_small | 舵轮立柱:柱 2.2+轮 Ø1.4(12 段环+5 辐+4 把手+黄铜毂;总高 2.87 含轮系) |
| TreasureMound | 446 | Brass + Coral + WoodDark(3) | 2.16×2.06×0.81 | prop_small | 宝藏堆:暗木托盘+金丘(起伏坡)+14 枚贴坡斜立金币+3 颗 Coral 宝石+珊瑚枝 |
| GrassTuft | 322 | Grass×3(3) | 0.93×0.99×0.73 | prop_small | 草丛:14 片交叉弯叶(Light/Mid 两档混)+2 抽穗茎 |
| FernClump | 224 | Grass×3(3) | 0.93×0.99×0.76 | prop_small | 蕨丛:7 根羽叶分层(内层直立/外层斜展),每段两侧宽羽片明暗交替 |

STAT 全量 18/18 零报错、零超预算;面数合计 ≈8.6k tri(18 件总预算 18×3000 上限的 ~16%)。

### 相邻材质粗糙度配对(美术风格指南 §3.2)

槽粗糙度由 `ST.SLOTS` 单一事实源定死(脚本零散写)。逐件跨族相邻配对的 roughness 差:

| 配对 | 差 | 出现件 |
| --- | --- | --- |
| Brass 0.50 × WoodDark/WoodMid 0.72-0.74 | 0.22-0.24 ✓ | Cannon 轮毂、WheelPost 毂、Treasure 币×托盘 |
| Coral 0.70 × Brass 0.50 | 0.20 ✓ | TreasureMound |
| Ember 0.90 × WoodDark 0.74 | 0.16 ✓ | Campfire 柴×焰 |
| Iron 0.58 × WoodDark 0.74 | 0.16 ✓ | Cannon 炮身×炮架 |
| Iron 0.58 × WoodLight/Mid 0.72 | **0.14(低于 0.15)** | BarrelWood 箍×桶身 |
| Ember 0.90 × Rock 0.78 | **0.12** | Campfire 余烬×石圈 |
| Grass 0.86 × Wood 0.72-0.74 | **0.12-0.14** | 棕榈叶×干、GrassTuft/FernClump 内部 |
| Grass 0.86 × Rock 0.78 | **0.08** | RuinColumn 苔片×柱身 |

说明:低于 0.15 的四组是 `ST.SLOTS` 既定值的结构性结果(木/岩/草三大环境族彼此粗糙度接近,
同族三档互相为 0)。本 kit 的处理:① 凡能取大差的相邻配对(金属×木/草)均已优先采用;
② 同族三档的材质区分按 §2.1/§2.6 明度纪律由 albedo(亮:中:暗 ≈ 1:0.75:0.5)与 flat 棱面光照承担;
③ 若需硬达标,须改 `style_tokens.py` 的槽值(影响全部 kit 与 Unity 侧同源表),【提案/待定】留协调者裁决。

## 调参行号(`props_kit.py`)

| 要调什么 | 行号 | 参数 |
| --- | --- | --- |
| 出图开关/采样/分辨率/焦距 | 49-54 | `ONLY` / `SAMPLES` / `RENDER` / `RES` / `JPG_QUALITY` / `LENS` |
| 全局造型密度 | 57-60 | `TRUNK_SIDES` / `FROND_SEGS` / `ROCK_SIDES` / `ROCK_RINGS` |
| 产物目录 | 62-66 | `FBX_DIR` / `PREVIEW_DIR` / `WORK_DIR` |
| 节理岩形状/三档分档 | 229 | `_rock(...)`(dome/top_wobble;分档阈值在函数体 zr<0.45/0.75) |
| 棕榈叶弯垂/折脊 | 268 | `_palm_frond(...)`(segs/droop/fold/taper/rib) |
| 沙袋/金币/火锥/宝石 | 305/330/… | `_sandbag` / `_coin` / `_flame` / `_gem` / `_coral` / `_mound` |
| 棕榈树干分节强度 | 467 | `r *= 1.32`(鞘环鼓起系数) |
| 三棵棕榈规格 | 543-561 | `build_palm_tall/lean/dead` 内 `trunk_h / lean_deg / n_leaves / frond_len` |
| 各件主参数 | 565-1090 | `build_rock_l … build_fern`(每件头几行即主尺度/数量/种子) |
| 预览三灯(按件缩放) | 1190 | `setup_stage`(样板 build_scene_kit.py:734-736 位形按包围球归一) |
| 预览相机 | 1239 | `make_views`(距离系数 1.42:高件不裁顶) |

## Unity 侧接线(本目录不管,只给口径)

- 导入口径沿袭样板:`tools/blender/scene/README.md:56-62`(useFileUnits=true / useFileScale=false /
  globalScale=1;materialImportMode=None,按槽名前缀 `Kit_` 重建 URP/Lit 材质)。
- **需 Unity 侧新增/确认的槽**:`Kit_Ember` `Kit_Coral`(M4 提案槽,ST 内已注明"Unity 侧表由协调者
  同步扩展");本 kit 另用到 `Kit_WoodLight` `Kit_SandMid` `Kit_SandDark` `Kit_GrassLight`
  `Kit_GrassMid` `Kit_GrassDark` `Kit_RockLight` `Kit_RockMid` `Kit_RockDark` `Kit_Iron`
  `Kit_Brass` `Kit_Rope` `Kit_WoodMid` `Kit_WoodDark`(若 Unity 侧 M4 槽表尚未登记需一并补)。
- **双面材质**:叶片/草/蕨/棕榈叶/枯叶柄/苔片是单层开放面片(Blender 预览双面,Unity 单面会剔除背面),
  对应槽(Kit_Grass×3、Kit_Rope)材质需 **Render Face=Both**,同 `Kit_Sail` 惯例
  (`tools/blender/scene/README.md` Unity 侧接线节)。
- 道具无站面 manifest、无碰撞:`WorldMapComposer` 摆放时不生成 collider(任务书口径)。

## 迭代记录(自评闭环,三轮)

1. r1:全量出图后逐件 Read 自评 → 问题清单(取景裁切、棕榈叶稀薄、分节弱、PalmDead 残柄过细、
   漂木铅笔感+端面穿帮、篝火火焰尖塔化、炮口箍错位、残柱断口浮环、宝藏金币悬空+黑缝、蕨丛荆棘化、
   舵轮穿模)。
2. r2:14 处参数/几何修正后全量重跑(落在正确产物目录)→ 复读确认大头全消。
3. r3:剩余 7 处单件微调(棕榈分节 1.32、PalmDead 残柄加宽、残柱 20 棱、蕨丛加高、宝藏黑缝修复、
   RockFlat 顶面破碎、金币斜立加大)→ 复读确认达标,收敛。
