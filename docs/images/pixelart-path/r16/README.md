# r16 · 「废弃化工厂」第二轮：打光修复 + 配色限制删除 + 弧面平滑（创始人三轮指令的落地轮）

> **这一轮回答什么**：r14/r15 出图后创始人的三条指令——①「彻底去掉配色限制」；②「打光有问题，好好修」；
> ③「圆柱形建筑在弧形面上精准使用细分曲面」。本轮全部落地并实机验证；**判据全绿**（含关卡 1 交叉验证）。
> 前置：建模与首轮实机口径见 [r14](../r14/README.md)。**r14/r15 的图里带着本轮修掉的"剔背面洞"**，
> 判读时以本轮为准。

## 复现

```bash
tools/headless/run.sh build PirateCrew.EditorTools.PixelartChemPlantSetup.BuildAll     # 烘场景
tools/headless/run.sh build PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs \
    -buildFlavor development -buildScenes development                                   # 出包
external/build/<版本>/win64/PirateCrew3D.exe -pixelartOut <本目录绝对路径> -pixelartLevel 4   # 出图
python tools/pixel-review/judge_pixelart_pilot.py docs/images/pixelart-path/r16
# 打光常量是全局的：改动后必须重烘全部 11 个像素场景
#   PixelartPilotSetup.BuildAll / PixelartLevelPilotSetup.BuildAll / PixelartWorldMapPilotSetup.BuildAll
```

## ① 打光修复（两个常量 + 一个开关）

| 量 | 原 | 现 | 为什么 |
| --- | --- | --- | --- |
| 主光色 `PixelartSceneContract.SunColorHex` | `#FFF5E0` | **`#FFFFFF`** | 暖白主光把受光面推成米黄（实测砼 `#9E988A` 的受光面成 `#B0A58F`，乘子偏暖即来自主光色）。两轮收敛：`#FFF9EE`（不够）→ 纯中性 |
| 环境光 `PixelartSceneContract.AmbientHex` | `#37486B` | **`#333C4A`** | 同明度蓝移的藏蓝把暗面推成"海军蓝"且加法项抬亮过大；改为同族更暗、去大半蓝 |
| **实时投影** `PixelartStageKit.CreateSunAndAmbient(castShadows:)` | 恒 `LightShadows.None` | 化工厂关 **`Hard`** | **这就是"完全没有光影"的根源**：着色里的 `light.shadowAttenuation` 早就接上了，但投影默认关 ⇒ 画面只有三档明暗、没有任何落地影。当年关它的原因（160 m 大平面自遮挡）用两招解决：`shadowNormalBias = 0.65` 压平铺面自吃 + **海面只承接不投影**（`ShadowCastingMode.Off`） |

打光常量是**全局契约**（11 个像素场景序列化 + `PixelartSceneContractTests` 比对）⇒ 改后必须重烘全部
像素场景（本轮已重烘）。两个 hex 都是文档标注的美术旋钮，回退 = 改回 hex + 重烘。

## ② 配色限制：彻底删除（不是留着开关关着）

- 删除：`PixelartColorCorrectionFeature`（"整幅图只许出现板上颜色"的落点趟）、`PixelartColorCorrection.shader`、
  `PixelartPalette.cs`、`Palette/Palette.asset`、`Compute/Palette/PaletteGenerationCIEDE.compute`、
  装配器开关 `EnableFramePalette`（它自首轮起就因 LUT 索引口径问题恒为 false）。
- Cast 特征顺序**七趟改六趟**（契约 §3 修订）；`Assets/Pixelart/README.md`、实现口径、契约文档同步。
- 全局 32~64 色锁板的**提案**本身也按创始人裁决作废（[美术风格指南](../../../设计/美术/风格指南.md) §3.2）。
- 色彩纪律现行口径：**逐资产在源的槽位色表**（`tools/blender/scene/style_tokens.py` 的 `SLOTS`，
  Unity 侧镜像 `WorldMapAssetSetBuilder.Slots` / `PixelartChemPlantSetup.SlotHex`）。

## ③ 弧面：平滑着色 + 提高细分（创始人：「圆柱形建筑要不在弧形面上精准使用一下细分曲面」）

- `loft` / `cyl` 增加 `smooth` 通道（`MeshAcc` 的逐面 smooth 本来就留了，本轮启用）。
- **平滑着色对三档色带的意义**：逐面 flat 时竖直曲面每面各落一档（逐面跳档）；平滑法线让 N·L 沿曲面
  连续变化 ⇒ 色带**沿曲面连续弯**，圆柱/球才读作圆的。
- 细分：冷却塔 24→**40** 边、烟囱 12→**24**、精馏塔 12→**20**、立罐 14→**20**、球罐 16→**28**；管廊长管 8→10 棱平滑。
- 冷却塔壳体同时改为**双壁闭合实体**（外壁 + 内壁 + 顶口环盖 + 底环盖）——见 ④。

## ④ 本轮抓出的最大缺陷：剔背面洞（根因修复，可复用的教训）

**症状**：实机图里塔身镂空、罐体/球罐缺面、地面水池发黑；Blender 预览（Cycles 双面渲染）完全看不出。

**根因**：`recalc_face_normals` 对**开口面**（壳体/盘/条）的朝向是启发式的，会把整片翻成朝内；
实机物体 pass **剔背面** ⇒ 翻向的面直接消失成洞。修法两件：
1. **按构造保证外法线**：本 kit 的 `join_to_object` 不再跑 recalc；`sbox` 面表按右手定则改写，
   `loft / fan / annulus / cyl / sphere_shell / patch_poly` 的绕序逐条核对。
2. **壳体闭合成实体**：冷却塔改双壁 + 环盖（开口面上的 recalc 不可靠，闭合体的法线收口是确定的）。

**工具化**：kit 预览新增 `CULL_BACKFACES = True`（默认按实机口径剔背面渲染，`--double-sided` 关）——
这条让"实机才暴露的实心度缺陷"在建模期就能照出来；本轮用它在 Blender 侧复现并修掉了全部洞。
另：单面渲染下 Cycles 的透明穿透会渲染成纯黑 ⇒ 预览里"黑斑 = 洞"，读图时按这个理解。

## 判据读数（2026-09-29 运行）

| 图 | 块边长 | RT宽 | 色数 | 跳变率 | 平坦占比 | 亮暗跨度 |
| --- | --- | --- | --- | --- | --- | --- |
| `pl4-close` | 2 | 960 | 33 | 0.050 | 0.920 | 0.81 |
| `pl4-mid` | 2 | 960 | — | — | — | — |
| `pl4-wide` | 2 | 960 | 54 | 0.151 | 0.756 | 0.84 |
| `pl4-overview` | 2 | 960 | — | — | — | — |
| `pl1-mid`（交叉验证） | 2 | 960 | 17 | 0.020 | 0.958 | 0.81 |

硬门禁全过：块边长 2 / 墨线在写缓冲 / 光照未旁路（`pl4-mid`、`pl1-mid` 与各自 albedo 图差 100%）/
场地在场（`pl4-mid` 78.3%、`pl1-mid` 61.2%、`pl1-wide` 15.0%）。**关卡 1 同轮出图作交叉验证**：
打光改动对既有关卡同样成立、判据不红。

## 图目

`pl4-wide`（32 m 宽机位）/ `pl4-mid`（14 m）/ `pl4-close`（7 m）/ `pl4-overview`（58 m 整场总览）/
`pl4-mid-density`（1-bit 密度抖动）/ `pl4-dbg-albedo` / `pl4-dbg-outline`（调试缓冲）+
`pl1-*` 六张（关卡 1 云端漫步，交叉验证打光改动）。

## 观察项（待创始人判图）

1. **投影的"冷影"**：影区 = albedo × 环境光（`#333C4A`）⇒ 偏冷灰蓝。这是 r13「亮暖暗冷」语言的延续；
   觉得影色太蓝/太深 → 改 `AmbientHex` 一个值 + 重烘。
2. **主光已完全中性**：受光面不再有暖味；想要一点"夕阳化工厂"的暖 ⇒ `SunColorHex` 往回挪（如 `#FFF6E8`）。
3. 弧面细分后整体三角面上浮（总装件 59 986 → 约 7.4 万），无预算上限（创始人裁决），实机帧率影响待实机走查。
