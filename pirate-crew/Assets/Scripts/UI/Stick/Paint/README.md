# Stick/Paint —— 手绘自绘核心（Painter 核）

game-2/stick-world 的 boiling line 自绘数学移植到 UGUI：`SketchDrawMath`（确定性数学层）+
`SketchWobbleGraphic`（沸腾面板/描边自绘）+ `SketchBarGraphic`（扁平进度条自绘）。
纯运行时程序集代码（`PirateCrew.UI` asmdef），无第三方依赖、无 unsafe、无 Editor 特化。

移植源（只读，均在 game-2 仓库）：

- `modules/ui_global/scripts/sketch/sketch_draw.gd`（SketchDraw 绘制库，233 行）
- `core/ui_framework/components/progress_painter.gd`（进度条绘制语义，34 行）
- 调用方式参考：`sketch_panel.gd`、`sketch_hslider.gd`

## 移植对照表

| game-2 (gd) | 出处（行号） | 本目录 (cs) |
| --- | --- | --- |
| `WOBBLE_AMP = 0.95` | sketch_draw.gd:17 | `SketchDrawMath.WobbleAmp` |
| `WOBBLE_INTERVAL = 0.12` | sketch_draw.gd:19 | `SketchDrawMath.WobbleInterval` |
| `OUTLINE_WIDTH = 1.6` | sketch_draw.gd:21 | `SketchDrawMath.OutlineWidth` |
| `SEG_LEN = 18` | sketch_draw.gd:23 | `SketchDrawMath.SegLen` |
| `CORNER_R = 7` | sketch_draw.gd:25 | `SketchDrawMath.CornerR` |
| `ARC_STEPS = 4` | sketch_draw.gd:27 | `SketchDrawMath.ArcSteps` |
| `wobble(i, seed)` | sketch_draw.gd:53 | `SketchDrawMath.Wobble` |
| `amp_for(r)` | sketch_draw.gd:60 | `SketchDrawMath.AmpFor` |
| `wobbly_rect_path(...)` | sketch_draw.gd:87 | `SketchDrawMath.WobblyRectPath` |
| `_append_arc(...)` | sketch_draw.gd:155 | `SketchDrawMath.AppendArc`（gd 私有，数学层放开为公共） |
| `draw_wavy_line` 顶点段 | sketch_draw.gd:165 | `SketchDrawMath.WavyLinePoints`（粗线绘制未接组件，见差异 8） |
| `fposmod(v, 1.0)` | gd 内建 | `Wobble` 内联为 `v - floor(v)` |
| `draw_panel` 填充 op | sketch_draw.gd:78 | `SketchWobbleGraphic` Fill / FillAndOutline 模式（`WriteFan`） |
| `draw_panel` 描边 op | sketch_draw.gd:80-82 | Outline / FillAndOutline 模式（`WriteLoop`） |
| `draw_panel` 退化保护 | sketch_draw.gd:70-75 | `OnPopulateMesh` 开头的 `<8px` 回退分支 |
| `COLOR_BG = (0,0,0,0.6)` | progress_painter.gd:12 | `SketchBarGraphic.BackgroundColor` |
| `COLOR_BORDER = (0,0,0,0.8)` | progress_painter.gd:14 | `SketchBarGraphic.BorderColor` |
| `set_progress_value` | progress_painter.gd:21 | `SketchBarGraphic.SetProgress`（变了才 dirty） |
| `draw_bar` | progress_painter.gd:28 | `SketchBarGraphic.OnPopulateMesh`（bg/前景/描边逐句对应） |

### 未移植项（gd 有、UGUI 无对应语义或不在本次范围）

| gd | 出处 | 处理 |
| --- | --- | --- |
| `empty_texture` / `blank_texture` | sketch_draw.gd:33/45 | Godot 图标槽占位用 ImageTexture；UGUI 隐藏图标不需要，不移植 |
| `draw_progress`（wobbly 进度条） | sketch_draw.gd:184 | 高层 op（两次 draw_panel）；需要时叠两个 `SketchWobbleGraphic`（轨道 FillAndOutline + 填充 Fill）复现 |
| `draw_gear` | sketch_draw.gd:202 | 齿轮图标（楔形/圆孔多层 op），不在 Painter 核范围 |
| `draw_wavy_line` 的粗线绘制 | sketch_draw.gd:180 | 路径数学已移植（`WavyLinePoints`）；其 draw op 与 `WriteLoop` 同构，滑条/分隔线接入时复用 |

## 公式保真度

- **逐位一致**：`Wobble` 的噪声式 `sin(i*127.1 + seed*0.3117) * 43758.5453 → 取小数 − 0.5`。
  gd float 是 64 位，小数取模会把精度差放大，故 cs 侧该式保持 double 运算后收窄 float；
  `sin`/`floor` 与 gd 同为 64 位库函数（理论上仅平台 libm 末位 ulp 差异）。
- **逐式一致（float 精度）**：`AmpFor` 的 clamp、`WobblyRectPath` 的圆角钳制/每边分段数
  `max(2, round((边长−2r)/18))`、四边+四角的采样循环与噪声索引连续递增、`AppendArc` 的
  `max(rr, 0.55r)` 自交保护、`WavyLinePoints` 的 `wobble(i*3,seed)*0.95*1.4`。
  gd 这些位置计算是 64 位，cs 用 float，顶点坐标差异在 1e-4 px 量级，视觉不可分辨；
  段数恰在 .5 边界时 `roundi`（away from zero）与 `Mathf.RoundToInt` 同规则，不受影响。
- **语义复刻（非公式）**：`draw_panel` 的两个绘制 op 与退化保护、`draw_bar` 的三段
  绘制顺序与颜色常量。填充三角化 gd 用耳切三角化，cs 用首点扇形——本路径近凸
  （扰动幅 ≤1.3px、弧半径下限 0.55r），两者三角形等价；极端扰动下扇形可能出现
  细长三角形，视觉影响为亚像素级。

## AA 方案（Godot 引擎 AA → UGUI 网格羽化）

Godot 的 AA 画在引擎里，gd 脚本只传 `antialiased=true`（sketch_draw.gd:82）；UGUI 网格
没有内建 AA，这里用顶点色 alpha 线性衰减带手工逼近：

- **描边层**（`WriteLoop`）：核心带宽 = OutlineWidth、全 alpha（miter 关节，折角 >~70°
  钳成近似斜切防尖刺）；核心带内外各挂一条 1px 羽化带，alpha 从 `color.a` 线性衰减到 0。
  对应 Godot polyline 的核心线 + 双侧 feather。
- **填充层**（`WriteFan` + `WriteFeatherRing`）：Godot `draw_colored_polygon` 本无 AA
  （硬边）；为避免 UGUI 下单独 Fill 模式硬边闪烁，沿轮廓法向外扩 1px 加一圈
  alpha→0 羽化环。FillAndOutline 模式下该环被描边核心带+羽化覆盖，外观与 Godot 的
  「硬边填充 + AA 描边」合成轮廓基本重合。
- **近似程度**：Godot 的 feather 宽度/衰减曲线由引擎按线宽与 DPI 决定，非公开常量；
  本实现取 1px 线性衰减，1x 缩放下与引擎观感接近，canvas 缩放较大时羽化带会被一起
  放大（与引擎按屏幕像素 AA 的行为有差），属可接受近似。

## 已知语义差异清单（有意为之）

1. **重掷随机源**：gd 侧 boiling 重掷用 `randi()`（非确定，sketch_hslider.gd:44）；
   cs 侧禁 System.Random 且要求同 seed 同输出，改用 wobble 同族哈希的确定重掷序列
   （`SeedBase` 定序列、tick 定相位）。视觉节奏一致，序列内容不同。
2. **节拍时钟**：gd `_process` 的 delta 受 `Engine.time_scale` 影响；cs 用
   `Time.unscaledTime`——与本工程 `SketchBoil.cs` 口径一致，UI 沸腾不随游戏暂停停摆。
3. **默认色**：gd draw_panel/draw_bar 的颜色全部由调用方传参；cs 组件形态需要缺省值，
   取 `StickTokens`（键名 = `assets/config/ui_tokens.json` 原键）：面板底 `WINDOW_BG`、
   描边 `INK`、进度条前景 `ACCENT`（`FillColor` 无 gd 对应默认，为组件形态新增）。
4. **绘制目标**：gd 直接画在 Control 的 `_draw` 上（引擎自动重绘）；cs 是独立
   `MaskableGraphic`（CanvasRenderer 承载），`raycastTarget=false`（gd 的 `_draw` 不参与
   命中，等价）。`FillAndOutline` 单色对应 draw_panel 一次调用；底/描边异色时叠两个
   组件（Fill + Outline），对应 gd 一次传两个色的调用形态。
5. **坐标系与绕序**：Godot 画布 y 向下、UGUI 局部 y 向上，gd 的"顺时针"路径在 UGUI
   里呈反向绕序——UI/Default shader Cull Off，不裁剪面，无视觉影响；坐标公式本身
   与手性无关，直接用 UGUI rect 生成。
6. **draw_rect unfilled 描边**：Godot 4 实现为四角闭合 polyline；cs 用四点闭合 miter
   线带等价复刻（角部单次覆盖，不会出现四条 quad 拼角的 alpha 双重混合）。
7. **像素对齐**：进度条/退化矩形的 1px 边基于 `GetPixelAdjustedRect()`；canvas
   referencePixelsPerUnit 与缩放配置属画布级设置，本层不另行取整。
8. **波浪线粗线**：`WavyLinePoints` 目前只出路径，粗线绘制（`WriteLoop` 同构）尚未接
   组件模式，滑条/分隔线移植时补。

## 顶点预算

周界点数 `N ≈ 2*(w+h)/18 + 16`（每边 `max(2, round(边长/18))` + 四角 4 点）。
单组件最坏（FillAndOutline）：羽化描边带 `6N` + 填充扇 `N` + 羽化环 `2N` ≈ `9N`。
400×300 面板 N≈94 → ~850 顶点；800×600 N≈160 → ~1440 顶点；典型 UI 尺寸均在千级。
分段数与 gd 完全一致，不做降采样。
