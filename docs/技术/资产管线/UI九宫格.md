# Beveled Pixel 九宫格规范

> **这份文档解决什么问题**：UI 的「像素斜面浮雕 / Aseprite 复刻」件**长什么样、怎么生成、怎么取用、怎么判合格**。
> 它是 [BeveledPixelSpriteBuilder.cs](../../../pirate-crew/Assets/Editor/BeveledPixelSpriteBuilder.cs) 的书面口径
> （管线 / 几何 / 令牌 / 判据），与运行时取用层 [PixelSkin.cs](../../../pirate-crew/Assets/Scripts/UI/Skin/PixelSkin.cs)。
>
> **现役架构（一句话）**：UI 贴图**主体是 Aseprite dark 主题的 sheet.png 直切件**（theme.xml `<parts>` 全表 345 件，
> 「全部移动过来，即便本端没必要有的」）；程序化生成只剩**存件**（tone 族窗体皮 +
> 焦点框，零调用方，随 W4 清退）；旧「条槽/面板两套 tone 语法」的九族贴图（Plate/Track/Tab/Fill/Ring/Pip/Sep/Shadow）
> 已整族退役（W3），其几何语言作为**历史口径**保留在 §一。
>
> **Aseprite 参考库 = UI 唯一权威**：`pirate-crew/Assets/Art/Sprites/UI/Aseprite/`（theme.xml + sheet.png）。
> 该主题扩展以 **CC-BY-4.0** 授权发布（aseprite 仓库 `data/extensions/aseprite-theme/`，本地参照库
> `external/aseprite-ref/`；作者 David Capello / Ilija Melentijevic / Nicolas Desilets）——允许复用与改编，**须署名**；
> 本仓不复制其任何贴图/字体文件之外的内容（直切件是授权允许的复制品，落 `Aseprite/Parts/` 入库并署名于
> [ui-pixel-ref/README.md](../../images/ui-pixel-ref/README.md)）。早期 Terraria 截图测量是**条槽语法**的历史来源
> （该语法随 Track 族退役，见 §一.2）；Terraria 素材的版权边界记录随提取文档退场（git 历史 `d089a69`/`2824696`）。

---

## 一、几何语言（历史口径，仍是审美与判据的基线）

**基本单位的变迁**：u 曾是 3px（3D 像素块对齐）→ 2:1（UI 与 3D 解耦）→ **×1 终局（ 定案）：
1 设计格 = 1 贴图像素 = 1 画布像素**，模板/几何落盘零倍率。`PixelSkin.Unit = 2` 现只余**画布密度**语义
（CanvasScaler scaleFactor，画布 = 屏幕 ÷ 2，1080p → 960×540 显示端整数 ×2），不再参与贴图/布局换算；
3D 侧 URP 渲染器仍 640×360（×3 块）——**UI 与 3D 是有意不同的网格**（判据只锁 3D 放大是整数倍）。

**两套边带语法，别互相推广**（第一版把它们混成一套，用户一眼判"和我给的界面不一样"——这段教训
解释了为什么面板语法与金框血条必须分开，虽已退役但约束着将来任何自绘件）：

```
条槽 Track（条槽语法，实测 ref-bars-life-mana.png）——【已随 Track 族退役】
  ① 描边 INK ② 外环 S2/S1 ③ 斜面 S4/S3 ④ 内暗线 S2/S1（与外环同色）⑤ 槽底 Floor

面板/按钮 Plate（工具对话框语法，实测 ref-pixel-tool-dialog.png）——被 Aseprite 直切件取代
  ① 描边 INK（近黑）② 唇边 S4（只在上/左）③ 脸 S3 平涂
```

由参照表直接读出、**至今仍然成立**的规则：

1. **全部件的最外一圈是近黑描边（INK）**——「全家一张皮」的签名。Aseprite dark 主题的窗口/按钮
   天然满足这条（黑环语法），复刻件与自绘件在同一屏不违和的原因就在此。
2. **光永远来自左上**（上=左、下=右，受光侧才亮）。
3. **三层带是同心环，不是"上下横贯 + 左右补边"**：最外圈必须 8 连通为 1 段、无端点
   （第一版画成横贯补边把四角截断，用户指出"4 角不是连着的"——判据 = `CheckRingClosed`，现用于 ring 族）。
4. **切角已被否决**： 走查否决豁口观感，`ChamferDepthUnits = 0`（方角）；
   对照参考截图定案——**圆角只属于按钮，面板/窗体走直角**。
5. **相邻档要分得开**：判据 `RampStepRatio = 0.90`（相邻档亮度比，参照实测 0.55~0.74 放宽到 0.90；
   用比值不用绝对差——亮度感知是相对的）。

## 二、现役管线：Aseprite dark 直切

| 件 | 路径 / 事实 |
| --- | --- |
| 直切真源 | `Assets/Art/Sprites/UI/Aseprite/theme.xml`（`<parts>` 表：id + 源矩形 x/y/w/h 或切片 w1..3/h1..3）+ `sheet.png` |
| 直切产物 | `Assets/Art/Sprites/UI/Aseprite/Parts/<part id>.png`，**全量 345 件**一件一 PNG |
| 九宫格切片 | = theme 声明值（w1..3/h1..3；整图件全 0）——`PixelArtTextureRules.ApplySprite` 落盘 |
| 入口 | `BeveledPixelSpriteBuilder.BakeAsepriteParts()`（`BuildAll` 内自动跑） |
| 判据 | 盘上 PNG 与 sheet.png 源区域**逐位一致** + 尺寸与 theme 声明一致 + 导入五值/切片一致（`VerifyAsepriteParts`）——手改盘上 PNG 或更新 sheet 后忘重烘都会被抓 |

- **加件 = 改 theme.xml 后重烘焙**，无需在任何代码里登记（旧白名单已退役）。
- 家族归类表 `AseFamilyOrder`（窗体/按钮/滑条/滚动条/…）只服务图集陈列廊分组，不是取用门槛——
  取用一律 `PixelSkin.Ase("<part id>")`。
- 直切件天然 1:1（×1 终局），无任何倍率。

## 三、程序化残段（存件，W4 清退登记）

`Targets()` 只余两族，**均为存件**——现役装配一律走 §四的直切取用器，新代码禁用这两个出口：

| 件 | 画法 | 切片 | 状态 |
| --- | --- | --- | --- |
| `Pixel_Focus`（8×8 方环） | 2px 厚方环：外 1px `HERO_BLUE`、内 1px 提白 45%（`FocusColors()`） | `(2,2,2,2)` | 【存件·零调用方】现役焦点 = `PixelSkin.WidgetFocus`（Ase `check_focus`） |
| `Pixel_Window_<Tone>`（13×24 × 7 tone） | 字母模板 `WindowTemplate`：K=INK 黑环、C/B=标题带（tone 亮档）、D=带底暗线（暗档）、E=窗体面（中档）；对应 theme window 的 3/7/3 × 15/4/5 | `(3,5,3,15)`；内容避开标题带 `WindowTitleBand = 15` | 【存件·零调用方】现役窗体 = `Ase("window")` |

**tone 三档槽位表**（改色只改 `SlotsOf`，值全是调色板槽位 id）：

| tone | hi（S4） | mid（S3） | dark（S2） | 来源 |
| --- | --- | --- | --- | --- |
| `Frame` | `UI_BEVEL_HI` | `UI_PANEL` | `UI_BEVEL_LO` | Aseprite window_face 族 #41444A / #2C2C30 / #202125 |
| `Dense` | `UI_BEVEL_HI` | `UI_BTN_BODY` | `UI_BEVEL_LO` | button_normal：唇亮 / 身 / 唇暗（#41444A / #292B30 / #202125） |
| `Light` | `UI_HOVER_HI` | `UI_BEVEL_HI` | `UI_PANEL` | 悬停面族（slider_full / check_hot_face）#575B61 / #41444A / #2C2C30 |
| `Sea` | `SEA_SHALLOW` | `SEA_MID` | `SEA_DEEP` | 本仓海图语义色 |
| `Primary` | `UI_ACCENT_HI` | `UI_ACCENT_MID` | `UI_ACCENT_DEEP` | button_selected 蓝三档原值 #6E9ADB / #4069C2 / #2A4185 |
| `Danger` | `UI_DANGER_HI` | `UI_DANGER_MID` | `UI_DANGER_DEEP` | 蓝阶 R/B 换位（亮度结构不变）；原 `UI_DANGER` 槽留血条底/落水提示 |
| `Warn` | `GLOW_WARM` | `UI_WARN` | `SHADOW_WARM` | 本仓警告/冷却语义色 |

**派生规则只有两条**（sRGB 逐通道线性混合；OkLCH 那套是量化用的，别拿到这里）：`S1 = mix(S2, INK, 0.45)`、
`Floor = mix(S1, INK, 0.5)`。**描边不派生**——直接用 `INK`。

**状态**：theme 无按压皮（Aseprite 按钮只有 normal/hot/focused/selected 四态），按压位移与面板投影
已随 ×1 终局退役（`PressOffset` / `ShadowOffset` 标 `[Obsolete(true)]`）。`HoverLift = 0.5` /
`PressSink = 0.5` 的阶梯抬升/下沉现只活在判据里（见 §五），不产贴图。

## 四、运行时取用：`PixelSkin`（装配侧唯一入口）

- **图集**：`Assets/Resources/UI/PixelSkin.asset`（`PixelSkinAsset`），烘焙末步 `GenerateAtlas` 生成/刷新；
  `ApplyBake` 是唯一写口，运行时只读。缺图红灯，绝不静默白块。
- **通用出口**：`Ase(partId)`——345 件直切件按 theme part id 原名取（如 `"button_focused"`、`"menu"`、
  `"tooltip_arrow"`）。Editor 装配与运行时同源。
- **具名取用器**（固定族字段，id 与 theme 件一一对应）：

| 取用器 | theme 件 | 说明 |
| --- | --- | --- |
| `WindowButton(state)` | `window_button_normal / _hot / _selected`（9×11） | UGUI 态映射：Normal→normal、Hovered→hot、Pressed→selected |
| `WindowIconSprite(icon)` | `window_{close,help,play,stop,center}_icon`（5×6） | 乘色换染（`Theme.Text` 为常态色） |
| `Check(selected)` / `Radio(selected)` | `check_normal/_selected`、`radio_normal/_selected`（8×8） | |
| `WidgetFocus` | `check_focus` | 复选/单选焦点环（2/6/2）——**现役焦点框** |
| `Sunken(focused)` | `sunken_normal / _focused` | 凹槽（输入框/列表底） |
| `SliderEmpty/Full(focused)`、`SliderThumb` | `slider_empty/_full(+_focused)`、`mini_slider_thumb` | 充满=压暗语法（内芯 #41444A），不是彩色 |
| `Scrollbar(thumb)` | `scrollbar_bg / _thumb` | |
| `Tooltip`、`ArrowDown(state)` | `tooltip`、`combobox_arrow_down(+_selected/_disabled)` | 悬停不换图标（theme 只给按钮底换 hot 皮） |
| `Ase("window")` / `Ase("menu")` | 窗体/菜单底 | 现役面板/窗体皮 |

- **取色令牌**：tone 只余取色职能——`LightOf/MidOf/DarkOf(tone)`（图集 `toneColors`，tone×3）、
  `Ink`、`PaperWhite`（= `UI_TEXT` #C0C0C0）、`TextColorOn(tone)`（：中档亮度
  > 140 = 浅底给墨字，否则暖白字——"本 tone 亮档字"旧口径已废：红底红字对比度崩）。
- **`Theme` 静态色表**：Aseprite dark 权威配色 13 色（`Text` #C0C0C0、`Face` #2C2C30、`Background` #41444A、
  `Disabled` #202125、`HotFace` #575B61、`Selected` #E1B85F 选中金、`TooltipFace` #4069C2 等）——
  **搬皮件的唯一取色源**，别再从 tone 阶梯里"换算"。
- 装配侧纪律：可见包边件 width/height **不低于九宫格切片和**（按钮 = theme button 上下切片和 10，
  `PlateMinRender`）；anchoredPosition 至少取整——分数像素会让色带糊宽；窗体内容避开 15px 标题带。

## 五、判据（生成器 `Verify()`，全部可复算）

| 层 | 判什么 |
| --- | --- |
| 调色板 | 缺槽预检（`UsedSlotIds`，缺槽写盘前红灯）；tone 四档明度单调（S1<S2<S3<S4）；相邻档亮度比 ≤ 0.90；Floor 与 S1 分得开；悬停阶梯单调且抬升可见 |
| 程序化件 | 盘上 PNG 与生成器输出**逐像素一致**（改参数忘重烘被抓）；尺寸/切片与目标表一致；孔洞计数（期望透明数：window 全不透明、ring 除方环形状）；外环 8 连通 1 段无端点（ring）；锁板（每像素 ∈ 该族色表：window = 本 tone 阶梯 + INK，ring = 焦点两色） |
| 直切件 | 盘上 PNG 与 sheet.png 源区域**逐位一致** + 尺寸/导入一致 |
| 图集 | `windows`=7、`toneColors`=21、`aseParts` 三数组与 theme 全表对齐、全槽非空 |
| 颗粒度 | URP 两档渲染器 `renderHeightPixels` 必须整除 1080（3D 整数倍放大）；UI `Unit` 与 3D 已解耦（） |

导入设置五值（Point / 无 mip / Uncompressed / sRGB on / alphaIsTransparency **关**）+ 切片由
`PixelArtTextureRules.ApplySprite / CheckSprite` 统一强制——**UI 直切/程序化件共用同一份五值**但
`textureType = Sprite`（不并进 `Textures/Pixel/**` 约定域的原因：域内规则定死 Default，会静默退化成白块，
见 [像素纹理.md](像素纹理.md) §3.3）。

**核心判据两条的历史**：①「色带边界落基本单位整数倍」——×1 后 1px 网格上任何带宽都合法，判据退化为
占位（保留结构防回跳）；②「外环 1 段闭合圈无端点」——把"四角轮廓线要连着"变成可复算的数字
（§一.3 的走查事故）。

```bash
# 重烘焙（程序化件 + Aseprite 全量直切 + 判据 + 图集）
"<Unity>" -batchmode -nographics -quit -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" \
  -executeMethod PirateCrew.EditorTools.BeveledPixelSpriteBuilder.BuildFromCommandLine -logFile -
# 只判据（不写盘）
… -executeMethod PirateCrew.EditorTools.BeveledPixelSpriteBuilder.VerifyFromCommandLine
```

菜单：`PirateCrew/UI/重烘焙 Beveled Pixel 九宫格`；管线：`ArtGate` 第 ⑦.5 步。
契约测试层**待补建**（验收清单见待办 [ui-九宫格契约测试补建](../../项目/待办/ui-九宫格契约测试补建.md)——
按现役资产族（Focus + 7 窗体件 + 直切件逐位判据）补建 EditMode 独立复核；当前仅有纹理导入契约测试
`PixelArtTextureImportTests`）。

## 六、与参照的实测对照

本仓发布的对照物在 [docs/images/ui-pixel-ref/](../../images/ui-pixel-ref/README.md)：
`gen-contact-sheet-2x.png`（接触表 + 字模标注）/ `gen-showcase.png`（全屏构图主图）/
`gen-components.png`（组件总表）——三张均为 **u=3 波次**的程序化产物，×1 + Aseprite 全量迁移后
**未重出**；实机验收以编辑器截图为准（重出/实机 1:1 截图归档登记在
[ui-收尾与打磨](../../项目/待办/ui-收尾与打磨.md)）。几何表的权威已让位给 theme.xml（§二）。

## 七、现状与遗留

- **已产出**：`Assets/Art/Sprites/UI/Pixel/` 34 张（7 tone 窗体 + 焦点框）+
  `Assets/Art/Sprites/UI/Aseprite/Parts/` 345 张直切件；运行时图集随烘焙生成。
  UI 灰阶/强调槽位已按 Aseprite dark 主题原值定值（`UI_*` 槽状态标注以
  [pirate_palette.json](../../../pirate-crew/Assets/Data/Palette/pirate_palette.json) 为权威）。
- **W4 存件清退**：`PixelSkin.Focus` / `Window(tone)` 零调用方，保留仅为烘焙器存件；
  连同烘焙器程序化残段一并清退时再删。
- **契约测试待补建**：见 §五 与待办。
- 其余（像素密度可调档、字体待定、`Resources/UIIcons/` 风格协调、1440p 画布等）统一登记在
  [ui-收尾与打磨](../../项目/待办/ui-收尾与打磨.md)，本文不重复清单。
