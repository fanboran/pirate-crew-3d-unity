# UI HUD 样板档案（多彩卡通 · 设计系统版）

> 多彩卡通 UI（2026-09-19 用户三轮裁决 + game-2 管线对齐）的**样板屏验收图**——战斗 HUD 与组件陈列页。
> 原始 PNG 在 `export/uihud-r8/`、`export/ui-gallery-r2/`（不入库），此处为压缩归档版。
> 复现：ArtGate 全链 → 播放器构建 → `external/build/PirateCrew3D.exe -artReviewOut <dir>`，
> 陈列页另跑 `PirateCrew3D.exe -uiGalleryOut <dir>`。
> 设计意图与纪律见 [docs/设计/UI设计语言.md](../../设计/UI设计语言.md)。

## 风格定案（三轮裁决 + game-2 机制对齐）

- **高饱和多彩**：17 武器 / 7 职业各有语义色相（哈迪斯式深底 + 宝石彩图标 + 金强调），色表唯一真值在 `UiSkin.WeaponColor/CrewColor`；**格底一律暗档**（`CellBase`：内容色向 InkDeep 压 22%——静物全彩跳出灰底）。
- **两段卡通明暗**（r9 定案的构件修饰语言）：全部 tintable 件分「顶亮带 / 主段 0.88」灰度阶梯，乘色后保留——糖豆人式上受光体积；血槽另有顶 1px 反光 + 底 40% 暗带 + 两端 3px 端箍（"被箍住的容器"）。
- **形状收敛**：圆角两档（容器 8 / 控件 6）+ 全圆 Pill；1px 细边；无贴纸硬投影。
- **图标优先**：武器=静物图标格、名册=职业头像 pips、回合=徽章纯数字、动作钮=符号图标。
- **架构纪律**（学 game-2 ui_global）：Token 单源（UiSkin）→ 按钮变体表（UiKit.ButtonKind）→ zone 布局常量 + 装配期防撞自检（`[HUD 布局防撞]`）→ 组件陈列页回归（ui-gallery 两图）。

## 判读（每张图应看到什么）

| 图 | 应看到 | 实测（r6/r2） |
| --- | --- | --- |
| `hud-showcase.jpg` | **空白背景纯 HUD 演示**：中亮蓝灰纯底上 HUD 全要素一览 | 通过 |
| `hud-showcase-armed.jpg` | 同上 + 武器面板打开：顶部红蓝血条**镜像等长**（各 640，中轴对称）+ 条下职业色头像 pips（**两队都有**——r9 曾因 CrewKey 漏 §4.2 符号蓝队全灰占位）+ 中央 64px 减重徽章 + 描边提示文字 + **模式三钮贴右上角**（r10 曾因 y 轴语义混用沉到垂直中部）+ 底部带三段同底边线（[暂停/返回][面板贴底][提示条]） | 通过（r6，诊断代理 12 项复核） |
| `hud-fullscreen.jpg` | 同套 HUD 压真实战场：提示文字有描边在沙地亮部可读、部件不遮战场 | 通过（r6） |
| `hud-weaponpanel.jpg` | 底部面板：9×2 彩色图标格（**格底暗档**，铁球/香蕉/金币可辨）+ 底行武器名/说明（**不压第二排格**——r9 曾因格子 y 多加一格高下沉 70px）+ 右列（头像带底格/名/HP/跳跃/结束回合） | 通过（r6） |
| `battle-45.jpg` | 玩家视角整体协调 | 通过 |
| `unit-closeup.jpg` | 头顶细血条 + 选中描边 | 通过 |
| `ui-gallery.jpg` | **组件陈列页·控件档位**（game-2 component_gallery 等价物）：按钮三变体/图标钮/血条族（含 ghost 残影与端箍）/pips/5 档字号/8 色板/圆角两档，全部件=UiKit 真实长相（改 Token 后出此图即视觉回归） | 通过（r2） |
| `ui-gallery-icons.jpg` | **组件陈列页·图标集**：17 武器格（暗档底+中文名）/7 职业头像/13 符号 | 通过（r2） |

## 已知取舍（待用户验收裁决）

- 小地图仍是矩形深底面板（圆形罗盘化留待裁决后做）。r11 起 320×220 + 瓦片点阵/单位点
  （曾静默空白三轮：BuildAll 单独重存洗掉 BattleMinimap 组件，已把 WireMinimap 挂进重建链 +
  PlayMode 装配断言防复发）。
- 血槽端箍/底暗带在满血条上被填充遮挡，残血可辨（gallery 队条样例 0.34/0.62 段可直接核）。
- 暂停/确认/结算 Modal 观感未在静帧覆盖（动效需实玩验收）。

## 关键提交

- `4ff7f92` UI 地基（UiSkin/卡通皮肤工厂/符号库/UiKit/图标绘制器/动效扩展 + 15 测试）
- `3deb7cb` HUD 重设计（BattleHud/BattleHudBuilder/BattleUiTheme 重写 + 头顶血条 + 名册退役）
- `f89a45e` 首拍三轮修正 + 烘焙资产与 Battle 场景入库
- `61f5a9e`/`32d1d57` showcase 机位 + 档案判据
- 本轮（r4-r6）：zone 整带重排+防撞自检、两段明暗+凹槽构造线、按钮变体表、CrewKey §4.2 补全、格子 y 修正、陈列页+字体 Resources、设计语言文档

门禁：EditMode **1160/0/1**、harness 0 错（36 ECall 基线持平）、四步装配链 exit 0、ArtGate 18 步全绿、播放器 shader error **0**、防撞警告 **0**。
