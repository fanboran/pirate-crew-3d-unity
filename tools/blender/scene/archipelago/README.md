# tools/blender/scene/archipelago —— M4 地形件 kit（12 件大号地形）无头建模管线

> **这份目录解决什么问题**：M4 大海域世界化（`docs/技术/M4-大海域世界化.md` §4.3 分工表）的
> **地形件 subagent 域**——环礁弧/泻湖心岛/梯田岛/海蚀柱/沙洲/礁阶/红树墩/火山缘环/龟甲岛/
> 沉没广场共 12 件大号地形，纯 bpy 程序化建模、零贴图、零 .blend 输入，Blender 无头批量导出
> FBX + 站面 manifest。全部色值/粗糙度/预算/导出/站面校验取自 `tools/blender/scene/style_tokens.py`
>（单一事实源），本脚本不散写任何色值与预算。
>
> **精细度纪律**（用户 2026-09-17 裁决"不许随手搓"）：侧壁地层带（逐带扰动相移=岩层节理）、
> 顶面三层拼板（低频分区换档=三档明度纪律）、水线湿沙带、细节散布（贝壳/草裙/漂木/海鸟/
> 玄武柱状节理/龟甲六边形鳞纹/铺石分格/余烬纹），全部用几何表达，shade_flat。

## 复现命令

仓库根 `F:/VSCode/pirate-crew-3d-unity/` 执行（OPTIX 不可用时自动回退 CPU；全量约 8 分钟）：

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/scene/archipelago/archipelago_kit.py
# 可选参数：
#   --only <资产名>|all   只重建一件（如 --only AtollArcA；随机种子逐件派生，单件重跑与全量一致）
#   --samples N           预览图采样数（默认 96）
#   --no-render           只建模 + 导出 FBX + manifest，不出预览图
```

**产物**（重跑幂等覆盖）：

| 路径 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Models/WorldKit/Archipelago/<名>.fbx` | 12 件地形（单网格多 Kit_ 槽） |
| `pirate-crew/Assets/Art/Models/WorldKit/Archipelago/<名>.standable.json` | 站面 manifest（WorldMapAssetBuilder 按 box 生成 BoxCollider） |
| `docs/images/worldkit-archipelago/<名>-{front34,side,back}.jpg` | 预览 1024² q90（Cycles + Standard 视图变换 + ST 灰底三灯） |
| `external/worldkit-archipelago-work/archipelago_debug.blend` | 调参 GUI 缓存（gitignored） |

## 实测口径（2026-09-17 最终轮运行值；预算 terrain ≤15000 tri/件全过，槽 ≤8/件全过）

| 资产 | 三角面 | 材质槽（Kit_） | 包围盒 L×W×H (m) | 站面档 | manifest boxes |
| --- | --- | --- | --- | --- | --- |
| AtollArcA | 1662 | RockLight/Mid/Dark, SandLight/Mid, WetSand, Coral, Sail (8) | 67.20×24.93×4.92 | +0.5×3 弧段、+1.0×2 礁台 | 5（全带 yaw） |
| AtollCore | 858 | SandLight/Mid/Dark, RockLight/Mid/Dark, Coral, WoodDark (8) | 15.42×11.23×4.11 | +0.5、+1.0×2 | 3 |
| TerraceIslandL | 2751 | GrassLight/Mid/Dark, RockMid/Dark, SandLight/Mid, WoodDark (8) | 51.84×38.88×7.31 | +0.5/+1.5/+2.5/+3.5 | 4 |
| TerraceIslandM | 1552 | 同 L (8) | 26.01×19.73×6.20 | +0.5/+1.5/+2.5 | 3 |
| SeaStackTall | 1216 | RockLight/Mid/Dark, SandDark, WetSand, Coral, Sail (7) | 14.88×14.58×9.34 | +6.0 | 1 |
| SeaStackShort | 1178 | 同 Tall (7) | 13.20×12.96×5.77 | +2.5 | 1 |
| SandBarL | 1003 | SandLight/Mid/Dark, WetSand, GrassMid/Dark, Coral, WoodDark (8) | 32.40×9.02×4.11 | +0.5（细节不超顶高） | 1 |
| ReefStepsA | 774 | RockLight/Mid/Dark, WetSand, GrassMid/Dark, Coral (7) | 22.97×5.83×5.05 | +0.5/+1.0/+1.5 | 3 |
| MangroveHummock | 2734 | GrassLight/Mid/Dark, WetSand, RockDark, WoodDark, Coral (7) | 22.24×18.24×5.16 | +1.0（根须裙不站） | 1 |
| VolcanoRimA | 3174 | RockLight/Mid/Dark, SandDark, WetSand, Ember, Coral (7) | 69.07×68.14×7.92 | +3.5/+4.5 缘段、+0.5 内坪 | 15（14 带 yaw） |
| TurtleShellIsle | 3279 | RockLight/Mid/Dark, SandMid/Dark, GrassMid/Dark, Coral (8) | 44.46×45.04×6.64 | +1.0/+2.0/+3.0/+3.5 脊、+1.0 鳍×4/尾、+2.0 首 | 10（4 带 yaw） |
| SunkenPlaza | 1226 | RockLight/Mid/Dark, SandDark, WetSand, GrassMid/Dark, Coral (8) | 30.18×30.15×6.49 | +0.5 南北两块（池带无碰撞） | 2 |

## 坐标与站面口径（Unity 侧必读）

- Blender 内 +Z 上、1 单位 = 1 米；**原点 = 足印中心、z=0 海平面**；水下裙边统一到 z=-3。
  Unity 落位：根节点 y = -0.4（水面高度，见 `docs/技术/M4-大海域世界化.md` §1）。
- 可站立顶面全部为 0.5 m 整数档、面内高低差 0（顶板拼装共用同一 z 常数）；
  manifest 在导出前经 `style_tokens.check_standable_boxes` 强制校验，违规即抛错退出。
- manifest box 格式：`{"c":[x,y,z],"s":[sx,sy,sz],"yaw":度}`（Blender 本地系 Z-up）。
  `yaw` 为绕 +Z 逆时针角（可选字段，AtollArcA 5 个、VolcanoRimA 14 个、TurtleShellIsle 4 个鳍 box 携带），
  消费顺序「先 yaw 后平移 c」；Unity 侧（Z-up→Y-up）对应绕 +Y 同值角（建议 bakeAxisConversion=true
  导入下实机核对一次符号）。
- 碰撞盒 sz = 顶面高 − 裙边底（**全高盒**），防止角色从侧壁穿进岛体。
- 站面最小边 ≥4.0 m（`ST.MIN_STANDABLE_SIZE`）；SunkenPlaza 中央圆池（R4，池底 −0.55 连海渗水）
  从站面中挖除（南北两块 box），落入按落水处理。

## 结构清单（精细度要点）

- **通用 mound**（`terrace_body`，:304）：水下裙边→水线 WetSand/Sand 带→干区，逐带法向扰动相移
  =岩层节理；每层衬底盖下沉 `CAP_DROP`（共面会 z-fight，实测铁律）；**层间竖坎立在下层轮廓处**
  （若从本层轮廓起坡，45° 视角斜坡会吃掉整个下层顶面，第 7 轮实证）。
- **顶面拼板**（`top_surface`，:379）：超椭圆**解析采样**（离散点角度采样会产生畸形板，禁用）；
  外环板+内环板+芯板三层，板间缝露衬底盖；`banded_mats`（:564）低频半区分色落实三档明度纪律。
- **逐件特征**：AtollArcA 弧段拼板+内外缘立面+礁台+浪蚀口袋；SeaStack 连续 profile 节理环
  （26 环高密度采样）+棱褶+浪蚀腰+干沙溅染带+檐口顶盘+海鸟；SandBar 跟随轮廓的三纵条沙脊+漂木；
  Mangrove 拱根裙（WoodDark 弯管插水）；Volcano hi/lo 双档缘（+4.5 段实体侧壁，ROCKL=高位编码）
  +外缘玄武柱群+内坪礁纹+Ember 余温双裂纹；Turtle 三环甲台+六边形鳞纹（两圈六方格）+脊台鳞列
  +桨状鳍×3 爪+颈桥+眼窝吻部；Plaza 咬口轮廓（**k 下限 0.30 防负缩放自交**）+7×7 铺石分格
  （对角明度渐变+缺板+微倾板）+下沉圆池（檐圈/池壁/渗水面）+斜倚断柱×2。

## 预览渲染口径

- `setup_preview_world_compat`（:689）：`style_tokens.setup_preview_world` 的同参兼容版——
  ST 原函数内部 `bpy.data.curves.new(type="TEXT")` 在 Blender 5.2.1 崩溃（该枚举 5.2 改名 "FONT"，
  且那两行是创建后立即删除的占位废代码）；本函数逐行复刻（背景/地板/三灯的色值·能量·位置与
  ST 源码同款），**ST 修复后可整体切回**。
- `aim_preview_lights`（:676）：ST 三灯只摆位置没姿态（AREA 默认 -Z），补 look-at；并按
  美术风格指南 §2.5「暗面 ≥ 同色系暗档 0.9 倍」给 ST_Fill×2.5 / ST_Rim×1.8 能量补偿
  （仅预览环境，ST 基线背光面死黑实测 RGB 49,42,36）。
- `render_views`（:732）：机位按件包围盒自适应（对角线×1.30 定距、bbox 中心为目标），
  **三视角与 ST key 灯（+X,−Y,+Z）同侧**（样板纪律：主光打亮朝镜头的面，反侧机位会拍成背光剪影）。
- `view_transform='Standard'`（防 AgX 洗色，样板铁律）。

## 调参行号（`archipelago_kit.py`）

| 要调什么 | 行号 | 参数 |
| --- | --- | --- |
| 出图开关/采样 | 59-61 | `ONLY` / `SAMPLES` / `RENDER` |
| 随机种子 | 65 | `SEED0`（逐件 crc32 派生） |
| 通用形态 | 68-75 | `SKIRT_BOT_Z` / `TOP_T` / `CAP_DROP` / `STRIP_DROP` / `SIDE_BANDS` / `SEG_ARC(_BIG)` / `JITTER` |
| 材质槽清单 | 79 | `KIT_SLOTS`（色值/粗糙度在 style_tokens.SLOTS，勿在此加色） |
| 环礁弧 | 91 | `ATOLL_ARC`（半径/弧跨/礁台角位/拼板数） |
| 心岛 | 94 | `ATOLL_CORE`（足印/礁台阶位） |
| 梯田岛 | 97 / 103 | `TERRACE_L` / `TERRACE_M`（每层 z/hw/hd/偏移） |
| 海蚀柱 | 108-109 | `STACK_TALL` / `STACK_SHORT`（足印/顶盘/档位/节理数） |
| 沙洲 | 111 | `SAND_BAR` |
| 礁阶 | 113 | `REEF_STEPS`（板足印/中心距/档位） |
| 红树墩 | 116 | `MANGROVE` |
| 火山缘环 | 118 | `VOLCANO`（内外半径/双档/缺口角/hi 段跨/玄武柱数） |
| 龟甲岛 | 123 | `TURTLE`（三环/脊/鳍×4/首尾） |
| 沉没广场 | 133 | `PLAZA`（足印/网格数/池半径与池深） |
| 预览机位微调 | 137 | `VIEWS_CFG`（d_mult 逐件拉近/拉远） |
| mound 层台主体 | 304 | `terrace_body`（裙边/竖坎/地层带逻辑） |
| 顶面拼板 | 379 | `top_surface`（层数/拼缝/檐口） |
| 顶面配色 | 564 | `banded_mats`（各件 zone_mats 表在 build_* 内） |
| 预览世界/机位 | 676-689 | `aim_preview_lights` / `setup_preview_world_compat` |
| 逐件建模入口 | 771-1475 | `build_atoll_arc` / `build_atoll_core` / `_build_terrace` / `_build_stack` / `build_sand_bar` / `build_reef_steps` / `build_mangrove` / `build_volcano` / `build_turtle` / `build_plaza` |

## Unity 侧接线（本目录不管，只给口径）

- 导入必须 `useFileUnits=true; useFileScale=false; globalScale=1`（踩坑记录见
  `tools/blender/scene/README.md`）；材质由 Kit_ 槽名重建（URP/Lit），
  本 kit 新增用到的槽：SandLight/Mid/Dark、WetSand、GrassLight/Mid/Dark、
  RockLight/Mid/Dark、Ember、Coral、WoodDark、Sail（色值/粗糙度全部同源 `style_tokens.SLOTS`）。
- `WorldMapAssetBuilder` 按 `<名>.standable.json` 生成 BoxCollider：box 带 `yaw` 时
  BoxCollider 的 localRotation = 绕本地竖直轴转 yaw（Blender +Z CCW 度 → Unity +Y 同值度），
  c 为旋转后坐标系内的最终中心（先 yaw 后平移 c）。
- SunkenPlaza 的池带（|y|<4.4）有意无碰撞（落入按落水处理）；MangroveHummock 根须裙、
  SandBarL 细节散布、各件草簇/贝壳均在站面高度以下或视觉件，不参与碰撞。
