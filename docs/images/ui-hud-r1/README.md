# UI HUD 样板档案（手绘涂鸦皮肤 · game-2 移植版）

> **手绘涂鸦 UI**（2026-09-20 用户裁决：隔壁 game-2/stick-world 的 UI 素材与生成流水线
> **原封不动搬来，只换配色为本项目色板**）的样板屏验收图——战斗 HUD 与组件陈列页。
> 原始 PNG 在 `export/art-review/r13/`、`export/art-review/r13-gallery/`（不入库），此处为压缩归档版。
> 复现：`python tools/sketch_ui/gen_sketch_ui.py`（烘 96 张沸腾贴图）→ ArtGate 全链 → 播放器构建 →
> `external/build/PirateCrew3D.exe -artReviewOut <dir>`，陈列页另跑 `PirateCrew3D.exe -uiGalleryOut <dir>`。
> 设计意图与纪律见 [docs/设计/UI设计语言.md](../../设计/UI设计语言.md)。

## 风格定案（手绘涂鸦 · 本轮裁决）

- **素材与流水线原样移植**：`tools/sketch_ui/gen_sketch_ui.py` = game-2 `gen_sketch_ui_a10.py`
  逐行移植——wobbly 圆角矩形 + cos 整数频率噪声 + 三帧沸腾 + 面板 wobble 1.8 + 纸感噪点 +
  九宫格平铺自检三关，几何参数零改动；**只改了配色区**（换成 UiSkin 色板：夜海靛蓝深底 +
  金强调 + TeamRed 语义红，白系三级描边与 btn_ink 纸面槽沿用上游）。
- **tint 槽是唯一新增**：`pip/ring/fill/cell` 四槽烘白实底+墨边，运行时 `Image.color` 乘色
  （队色血条/职业色 pip/内容色武器格）——白系边乘深色会隐形，彩底件一律走墨边。
- **沸腾驱动**：`SketchBoil` 每 0.12s 换帧、实例 ID 相位逐件错开；按钮四态 = 四槽贴图切换
  （指针事件实现），ColorBlock tint 关闭。**时序铁律：OnEnable 不得 Apply**（AddComponent
  同步触发时 Slot 尚未赋值，r12 gallery 全页错图的事故）。
- **字体**：全 UI 统一 StickHand 手写体（隔壁"文字统一 StickHand"纪律）；字号八档整体上调
  （Display 52 / Banner 42 / Title 30 / Section 24 / Hud 22 / Body 18 / Hint 16 / Tiny 15）——
  2026-09-20"文字可读性很差"裁决。
- **架构纪律**（学 game-2 ui_global）：Token 单源（UiSkin）→ 按钮变体表（UiKit.ButtonKind →
  SketchSkin 槽组）→ zone 布局常量 + 装配期防撞自检 → 组件陈列页回归（ui-gallery 两图）。
- 上一版"两段卡通明暗 + 凹槽构造线"的平涂语言（r4-r11）已被手绘皮肤整体取代，
  `CartoonSpriteFactory` 仅存几何纯函数（SdRoundRect 仍被 UiGlyphs/测试引用）。

## 判读（每张图应看到什么）

| 图 | 应看到 | 实测（r13） |
| --- | --- | --- |
| `hud-showcase.jpg` | 空白背景纯 HUD：全部件呈**手绘不规则墨线边缘**（沸腾贴图九宫格） | 通过（亲眼验收） |
| `hud-showcase-armed.jpg` | 同上 + 武器面板：红蓝血条**手绘段**镜像等长 + 职业色 pips（cell 槽）+ 中央红环徽章 + 手写体提示 + 模式三钮贴右上 + 底部带同底边线；**跳跃=金实底墨边手绘钮** | 通过（亲眼验收） |
| `hud-fullscreen.jpg` | 同套 HUD 压真实战场 | 通过 |
| `hud-weaponpanel.jpg` | 底部面板：9×2 手绘格（内容色暗档底 + 静物图标）+ 武器名/说明行 + 右列（头像格/名/HP/跳跃/结束回合） | 通过 |
| `battle-45.jpg` | 玩家视角整体协调 | 通过 |
| `unit-closeup.jpg` | 头顶细血条（progress_bg/fill 槽，同样沸腾） | 通过 |
| `ui-gallery.jpg` | **组件陈列页**：按钮三变体（金实底/深底/酒红，各带手绘框）/图标钮/血条族/pips/5 档字号/8 色板（cell 槽紧凑块）/手绘槽三样——**颜色必须鲜亮**（r12 曾全页被默认 panel 槽污染成暗色，根因见上时序铁律） | 通过（修复后亲眼验收） |
| `ui-gallery-icons.jpg` | 17 武器格（内容色暗档底 + 中文名）/7 职业头像/13 符号 | 通过 |

## 已知取舍（待用户验收裁决）

- 小地图仍是矩形深底面板（圆形罗盘化留待裁决后做）；内部瓦片点阵在样板海域偏稀。
- 沸腾是 3 帧循环动画，静帧只能看到其中一帧的边缘扰动（实玩才见"线在抖"）。
- danger 按钮的 14% 红叠加槽在深底上读作"实底酒红"，与 UiSkin.Danger 标注色一致，是预期观感。
- 暂停/确认/结算 Modal 观感未在静帧覆盖（动效需实玩验收）。
- 玩家徽章环仍在用 tint ring 圆片（队色圆片+白数字），哈迪斯式"金线环"细节可再打磨。

## 关键提交

- `4ff7f92` UI 地基 / `3deb7cb` HUD 重设计 / `f89a45e` 首拍修正 / r4-r6 平涂语言三轮（见 git 历史）
- r8-r11：海图放大+内容回归、档位纪律、防撞自检（见 git 历史）
- r12：**手绘涂鸦皮肤整体移植**——tools/sketch_ui 烘焙管线、SketchSkin/SketchBoil、
  UiKit 换供给、字体全切 StickHand、字号八档上调、gallery 修复

- r13：**组件陈列页一比一复刻隔壁 8 分组**（buttons/labels/inputs/tabs/sliders/lists/feedback/keymap）
  + 大面板九砖平铺（Sketch9Slice，复刻 Godot TILE 边带行为——Sliced 拉伸是观感差异硬根因）；
  矮件纪律（高 <2×边带+砖 回单图，血条悬浮黑块实拍事故）。

门禁：EditMode **1160/0/1**、编译 0 错、装配链 exit 0、ArtGate 18 步全绿、播放器构建成功、
烘焙自检 **32 槽 ALL TILING-CLEAN**、导入参数 96 张落 .meta、防撞警告 **0**。
