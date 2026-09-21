# Blender 导出模板使用说明（平滑法线 / 量化 / 纹素密度）

> **这份文档是什么**：`tools/blender/pixel/` 这套**像素化导出模板**的用法与实测口径——
> 三个函数各做什么、命令行怎么调、kit 脚本怎么接入、有哪些 Blender 版本差异必须知道。
> 它是 [像素纹理资产管线.md](像素纹理资产管线.md) §6「Blender 导出模板新增」三件事的落地说明。
>
> **前置阅读**：[调色板与量化手册.md](调色板与量化手册.md)（板与 OkLab 口径）、
> [渲染管线-等距像素卡通.md](../渲染/渲染管线-等距像素卡通.md) §5（反壳描边为什么需要平滑法线）。

---

## 1. 三个函数（资产篇 §6 的三件事）

| # | 函数 | 解决的问题 | 不做它会怎样 |
| --- | --- | --- | --- |
| ① | `bake_smooth_normals_to_vertex_colors(obj)` | 把**平滑法线**烘进顶点色：`R` = 色带阈值偏移（0.5 中性）、`GBA` = 法线（`×0.5+0.5` 编码，属性名 `SmoothNormal`） | 低模硬边处反壳描边的外扩方向一分为三 ⇒ **描边在硬边开裂**（渲染篇 §5「低模硬边会裂」） |
| ② | `quantize_vertex_colors(obj, palette)` | 把**平涂色**顶点色（属性名 `Col`）按 OkLab 最近邻锁进全局板 | 顶点色漂在板外，无法参与逐字节回归，风格统一失效 |
| ③ | `check_texel_density(objects, …)` | 纹素密度 `32px/m ±0.5%` + 每面 Jacobian 长短轴比 `< 1.01` | 密度纪律只能靠人眼看，量产后必然失控（调研 §4：行业里 32px/m 是无人区，护栏只能自建） |

另有两个配套件：`unwrap_and_lock_uv_density(obj)`（UV 展开 + 密度锁定，见 §4）与
`export_fbx(objects, path)`（沿用既有导出铁律 + 顶点色/平滑法线的版本自适应）。

### 1.1 顶点色的两个互斥用途（本模块的硬边界）

顶点色在这条管线里承载**两种完全不同**的数据，混用是最难查的一类静默故障：

| 属性名 | 承载 | 通道分配 | 能否量化 |
| --- | --- | --- | --- |
| `SmoothNormal` | 反壳描边的外扩方向（数据） | `R` = 阈值偏移（0.5 中性）、`GBA` = 平滑法线 | **禁止**。量化它 = 把法线映射到板色上 = 外扩方向全错，而画面**不一定立刻炸** |
| `Col`（Blender 默认名） | 美术刷的平涂装饰色（颜色） | RGBA 常规颜色语义 | 可以，就是要量化它 |

`quantize_vertex_colors` 因此按属性名白名单工作：传入 `SmoothNormal` **直接抛错**，
错误信息里给出可用属性名。自证用例专门钉住这条边界（`--self-test` 的 `GUARD` 行）。

### 1.2 平滑法线的算法（为什么不用 `corner_normals`）

1. **按位置容差合并**（1e-4 m）：硬边顶点位置相同、只有法线不同，正是要合并的对象；
2. 多边形的每个角累加 `面法线 × 权重`，权重 = 该角的角度（**角度加权**优于等权：细长三角形不会拖偏平均方向）；
3. 每个角取所属合并组的平均方向并归一化。

**不能用 `mesh.corner_normals`**：它本来就是**分裂后**的着色法线，硬边处已经一分为三 ——
拿它烘顶点色等于把开裂原样搬进数据里。自证用例用硬边立方体验证：
8 个角顶点各自的 3 个 corner，`GBA` 必须**完全一致**（`inconsistent=0`）。

---

## 2. 命令行

### 2.1 单件资产：`step_asset.py`

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/pixel/step_asset.py -- \
    --fbx <输入.fbx> --out <输出.fbx> --tex-size 64 [--unwrap-uv] [--roundtrip] [--allow-density-fail]
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `--fbx` / `--out` | 必填 | 输入/输出 FBX（工程内相对路径或绝对路径） |
| `--tex-size` | 64 | 该资产的目标贴图边长（px，POT）。密度按它换算，**必须与资产篇 §2 的尺寸表一致** |
| `--texel-target` | 32 | 密度目标（px/米） |
| `--palette` | 板真源 JSON | 量化用的板 |
| `--no-smooth-normals` / `--no-quantize` | 关 | 跳过对应工序（排障用） |
| `--unwrap-uv` | 关 | 对没有 UV 的网格做 Smart UV Project 并把密度锁到目标（见 §4） |
| `--roundtrip` | 关 | 导出后重新导入，打印顶点色通道范围（验证编码没被色彩管理污染） |
| `--allow-density-fail` | 关 | 密度不合规只报告不退 2 |
| `--self-test` | — | **不依赖任何真实资产**跑正反对照自证全部判据 |

**退出码**：`0` 全过 / `1` 用法或资产错误 / `2` 密度未达标。

### 2.2 批量体检：`check_worldkit_density.py`

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/pixel/check_worldkit_density.py -- \
    --root pirate-crew/Assets/Art/Models/WorldKit --tex-size 64 \
    --out "$TEMP/pc3d-worldkit-density.json" [--limit N] [--self-test]
```

输出：人读表格（`asset / tris / uvless / density / jac_max / outliers / verdict`）+ JSON 报告
（逐资产 + 逐网格，`sort_keys=True` 保证可 diff）。**退出码**：0 全达标 / 1 有资产未达标 / 2 用法错误。
默认 `--out` 落系统临时目录（跑测产物不入 `external/`）。

### 2.3 判据为什么要"正反对照"（判据三律第 1 条）

`--self-test` 造一个 1 米立方体，六个面各占一个 UV 方块并按 `tex_size=64` 反解出让密度**恰好 32px/m**
的方块尺寸（0.5 UV 单位），然后：

| 用例 | 构造 | 期望 |
| --- | --- | --- |
| **正对照** | 方块 0.5×0.5 | 密度 **32.0000**、`jac_max 1.00000`、0 problem |
| **负对照 1** | UV 放大 3 倍 | 密度 **96.0000** ⇒ 必须被抓出 |
| **负对照 2** | U 方向拉 3 倍、V 不变 | `jac_max 3.0000` ⇒ 必须被抓出（**texel 非方形**） |
| 平滑法线 | 硬边立方体 | 8 角 × 3 corner 的 GBA 一致；`R ≈ 0.5`（8bit 步长内） |
| 量化真跑 | 平涂属性 `Col` 喂 4 个非板色 | 24 个 corner 全部落板（离板 ≤ 1 个 8bit 步长），改动数 > 0 |
| 量化边界 | 传 `SmoothNormal` | 必须抛错 |
| OkLab | 官方 4 组 XYZ | 最大偏差 0.000403 ≤ 1e-3 |

实测输出（2026-09-21，Blender 5.2.2）：

```
POS  density=32.0000 jac_max=1.00000 problems=[]
NEG1 density=96.0000 jac_max=1.00000 problems=['…聚合纹素密度 96.000 px/米 不在 31.840~32.160…']
NEG2 density=55.4256 jac_max=3.00000 problems=['…密度 55.426…', '…Jacobian 长短轴比 3.0000 ≥ 1.0100…']
SMOOTH corner_groups=8 inconsistent=0 channel_range={'min': (0.5029, 0.2122, 0.2122, 0.2118), 'max': (0.5029, 0.7913, 0.7913, 0.7882)}
OKLAB worst_dev=0.000403
GUARD ok: 拒绝对 'SmoothNormal' 量化：该属性承载反壳描边的平滑法线数据，不是颜色…
QUANT corners=24 改动=24 用到板色=4 最大 OkLab 偏移=0.0924 离板最大 8bit 偏差=1
SELFTEST PASSED
```

---

## 3. 判据的区间口径（哪些是硬失败、哪些只计数）

| 判据 | 口径 | 为什么 |
| --- | --- | --- |
| **聚合**密度 ∈ 32px/m ±0.5% | **硬失败** | 资产篇 §6 的断言；一个资产一个数，与 UV 布局相关 |
| 每面 Jacobian 长短轴比 < 1.01 | **硬失败** | texel 方正性；点采样下非方形 texel 比密度误差更显眼（调研 §4） |
| 单面密度对聚合值的偏离 | **只计数**（`outliers` 列） | 非均匀 UV 展开（浮雕/倒角面）天然有分布；把它当失败会让判据失去意义。数字供人判断展开质量 |
| 无 UV 网格 | **硬失败**（记一条 problem） | 密度无从核算 —— 这不是"密度不对"，而是"缺一道工序"（§4） |

`density = tex_size × √(uv_area ÷ world_area)` —— **必须开方**：
`uv_area × tex_size ÷ world_area` 的量纲是 px²/m²，只在 `world_area = 1` 时数值碰巧相等（资产篇 §6）。

**注意聚合口径的边界**：聚合 `uv_area` 包含 UV 岛之间的 padding。
打包得很松的 UV（大量空白）会把聚合密度**抬高**（同样的世界面积摊到更多 UV 面积上），
因此"聚合达标"不自动等于"每个面都达标"——这正是 `outliers` 列存在的意义。

---

## 4. UV 展开与密度锁定（存量资产的补齐步骤）

**实测发现（2026-09-21）**：`Assets/Art/Models/` 下 **49 件 FBX 全部没有 UV 层**
（WorldKit 47 件 + SceneKit 的 `Flagship` / `Dock`），逐件清单见
`check_worldkit_density.py` 的 JSON 报告。批量体检结果：**不合规 49/49，根因全是"没有 UV"**。

⇒ 资产篇 §6 假设的"按纹素密度展开 UV"这一步在现有资产里**从未发生**：
kit 脚本只出几何与 `Kit_*` 材质槽，不展开 UV。所以像素纹理管线对存量资产的第一个真实动作
是**建模工序（UV 展/拆缝）**，不是调密度。

`--unwrap-uv` 提供的是**机械补齐**路径（不是替代建模）：Smart UV Project 出岛 →
**整体等比缩放**把聚合密度锁到 32.000（`k = target/current`，等比缩放不改变 Jacobian 比，
所以密度与方正性互不牵制）→ 复算。

实测 `BarrelWood.fbx`（199 三角面、4 个材质槽、313 顶点）：

```
STEP 平滑法线→顶点色 BarrelWood   R=0.503 G=0.000 B=0.000 A=0.000
STEP UV 展开+密度锁定 BarrelWood  密度 21.977 → 32.000
STEP 导出 FBX …/BarrelWood_pixel.fbx（1 个对象，26620 字节）
ROUNDTRIP 重新导入：color_attributes=['SmoothNormal']  min=(0.5029, 0.0, 0.0, 0.0) max=(0.5029, 1.0, 1.0, 1.0)
asset          tris  uvless  density  jac_max  outliers  verdict
BarrelWood      199       0   32.000  2.36555       195  FAIL(1)
DENSITY-FAIL BarrelWood：Jacobian 长短轴比 2.3655 ≥ 1.0100（texel 非方形）
```

**三条结论**：

1. **聚合密度可以精确锁到 32.000**（解析缩放，一次到位）；
2. **Smart UV Project 不保证 texel 方正性**（实测 `jac_max 2.37`、195/199 面密度落在 ±0.5% 之外）——
   它是平面投影，与投影轴成角的面必然被拉伸。**要过 Jacobian 判据必须按缝作者化展 UV**
   （接缝规划 + Follow Active Quads 之类），那是 M3 资产批次的建模工作量，不是工具能替代的；
3. 判据正确地把"缺建模工序"变成了一个**数字**（2.3655），而不是一句"看着还行"。

**round-trip 的编码结论**：`SmoothNormal` 的 `R` 通道导出再导入后仍是 `0.5029`（= 写入值），
说明顶点色**数据通道没有被色彩管理污染**——这依赖 §5 的 `colors_type` 选择。

---

## 5. Blender 版本差异（5.2.2 实测，勿凭旧文档直觉改）

### 5.1 `colors_type`：数据通道必须选 `SRGB`

写入已知值（`R=0.5, G=0.25, B=0.5, A=0.75`）后导出再导入的实测结果：

| `colors_type` | 回读值（R, G, B, A） | 判定 |
| --- | --- | --- |
| `LINEAR` | `(0.2159, 0.0513, 0.2159, 0.749)` | **数据被污染**：数值被当作 sRGB 做了线性化（`linear(0.25) = 0.0513`） |
| **`SRGB`** | `(0.5029, 0.2502, 0.5029, 0.749)` | **恒等变换**（8bit 步长内）—— 本模块默认值 |
| `NONE` | 顶点色整个丢失 | 不可用 |

名字听上去像"要加 gamma"，实际语义是"文件里的数值就是作者数值"。
本管线的 `GBA` 存的是**法线数据**，任何色彩空间转换都是错的 ⇒ 必须 `SRGB`。
alpha 通道在两种模式都不转换（`0.749`）。

### 5.2 `mesh_smooth_type`：本版**没有** "Normals Only"

Blender 5.2.2 的枚举是 `OFF / FACE / EDGE / SMOOTH_GROUP`。
资产篇 §6 与调研-反向壳 §6 写的 **"Smoothing: Normals Only" 是 Blender 2.8x 时代的选项名**，
在本版不存在。自定分裂法线在本版经 **`SMOOTH_GROUP`（锐边标记）**传递，故本模块默认用它；
传 `smooth_type=None` 则不传该参数、完全交给 Blender 默认值。
**这条口径以本文为准**（`pixel_export.export_fbx` 的探测式传参是执行体：只传目标 Blender
真的有的参数，避免跨版本升级时静默 TypeError）。

---

## 6. kit 脚本怎么接入

kit 脚本（`tools/blender/scene/<kit>/*.py`）在导出前加三步即可：

```python
import sys, os
sys.path.insert(0, os.path.join(REPO_ROOT, "tools", "blender", "export"))
import pixel_export as PX

PX.oklab_selftest()                                   # 转换自检（漂移即抛错，别让污染过夜）
for obj in export_objects:
    PX.bake_smooth_normals_to_vertex_colors(obj)      # ① 平滑法线 → 顶点色
    if obj.data.color_attributes.get(PX.FLAT_COLOR_ATTRIBUTE):
        PX.quantize_vertex_colors(obj, PX.load_palette_lab())   # ② 平涂色锁板（有才做）
PX.assert_texel_density(export_objects, texel_size_px=128)      # ③ 密度硬门禁（不达标就抛）
PX.export_fbx(export_objects, fbx_path)               # 导出（单位/轴向铁律不变）
```

`assert_texel_density` 是**硬版本**（有问题就抛），`check_texel_density` 是返回结果的软版本
（批量体检用）——"不达标要报错而不是警告"这条要求落在硬版本上。
**注意**：现有 kit 资产没有 UV，直接接 ③ 会让所有 kit 脚本导出即失败。
接入顺序建议：先接 ①②（不动几何、不阻塞现有产出），UV 展开完成后再接 ③。

---

## 7. 验收判据

| 层级 | 判据 | 命令 |
| --- | --- | --- |
| 模板自身 | 正反对照 + 平滑法线合并 + 量化边界 + OkLab 回归 | `step_asset.py --self-test`（退出码 0） |
| 单件资产 | 密度 32±0.5% 且 Jacobian < 1.01；round-trip 顶点色通道不变 | `step_asset.py --fbx … --unwrap-uv --roundtrip` |
| 批量资产 | 逐件表格 + JSON 报告，无 UV 资产单列 | `check_worldkit_density.py --root … --out …` |
| 与板一致 | 顶点色量化离板 ≤ 1 个 8bit 步长 | `--self-test` 的 `QUANT` 行 |
| 编码未污染 | round-trip 的 `SmoothNormal` 通道范围与写入值一致 | `--roundtrip` |

---

## 8. 遗留（登记不改，属 M3 资产批次）

1. **49 件 FBX 无 UV**（见 §4）——像素纹理管线对存量资产的第一道工序是建模（UV 展/拆缝），
   不是调参数。这是本轨道**最重要的交付发现**；
2. **Smart UV Project 过不了 Jacobian 判据** ⇒ 需要接缝规划（M3 逐件处理）；
3. 现有 `style_tokens.export_fbx` 与 `build_scene_kit.py` 的导出参数仍是旧口径
   （`mesh_smooth_type='FACE'`、无顶点色、无 `colors_type`）——本模块的 `export_fbx` 是新口径入口，
   两者尚未合并（合并属 M3/M5 的 kit 脚本收口，避免在途资产产出中断）；
4. 纹素密度终值（32px/m）仍是【AI 提案·试产校准】（美术指南待定项 #5），
   若试产改值，`--texel-target` 与 `TARGET_PX_PER_METER` 同步改，并重跑批量体检。
