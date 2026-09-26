# Aseprite 观感对齐 — 交接（全量复刻波·代码全落地，进入打磨波）

> 状态：**全量复刻波执行完毕**（2026-09-25，提交 `4d2dc0bf..9752f1fa`），
> 渲染链五层取证全干净（图集/shader/faceInfo/顶点 snap/相位），实拍 ×2 下
> **文字格半亮率 0%、横线相位全偶**；**打磨波第①②项已落地**（2026-09-25 深夜：
> 确认框收口标准模态 + 标题与窗皮三处单轨 + 全屏中心锚相位排查——见「打磨波①②执行记录」）——
> 余项三项见「打磨清单」，新会话恢复指引见 §六。
> 权威参考库：`external/aseprite-ref/data/extensions/aseprite-theme/dark/theme.xml`（1177 行）+ 同目录 sheet.png。

## 〇、本会话做了什么（执行案 §〇之一 五步收口全记录）

**主分支 `refactor/industrial-grade`，三个提交**：
`4d2dc0bf`（全波主体）→ `36b0d336`（测试同步）→ `8b937abd`（merge 验证分支四修）。

### 技术路线升级：手绘模板 → sheet.png 直切（比执行案更彻底，同一裁决语义）

- **真源入库**：`Assets/Art/Sprites/UI/Aseprite/{theme.xml, sheet.png, LICENSE.txt}`（CC-BY-4.0，出处见 LICENSE）。
  sheet.png 是 **1x 设计格图集**（theme 根 `screenscaling="2"` 只是屏幕放大系数）——与 ×1 裁决天然吻合。
- **烘焙器新管线 `BakeAsepriteParts`**：`System.Xml` 解析 theme.xml `<parts>` 全表 →
  白名单 **108 件**（styles 段引用的全部控件件；cursor/tool 图标/timeline 等编辑器专属件不烘）
  → 逐件从 sheet.png 切 PNG 落 `Assets/Art/Sprites/UI/Aseprite/Parts/<partId>.png`，
  九宫格切片 = 声明值 w1/w3/h1/h3（Unity border = (w1, h3, w3, h1)）。
- **判据升级为逐位比对**：`VerifyAsepriteParts` 盘上 PNG 与 sheet.png 源区域逐像素一致 +
  尺寸/切片与 theme.xml 声明一致——直切件没有"生成器参数"，与源逐位一致就是最强判据。
- 旧手绘 theme 语义件模板段（窗控钮/复选/滑条/凹槽/tooltip/箭头等约 490 行）整体退役；
  **tone 族（Plate/Track/Panel/Window/Tab/Fill/Ring/Focus/Pip/Sep/Shadow）保留**（战斗 HUD 在用），
  模板落盘 ×1 化（1 模板格 = 1 贴图像素）。

### 五步收口对应

1. **×1 重烘全表 + 补缺件** ✅：61 张 tone 件 ×1 + 108 件直切（含 button_focused/selected、
   tab 系、tooltip_arrow、menu、colorbar、drop_down、buttonset、toolbutton 等执行案点名件）。
2. **令牌 ×1 化** ✅：`AseLayout.Px` 恒等；`PixelSkin.Unit`=2 只余画布密度语义
   （CanvasScaler scaleFactor）；`WindowTitleBand=15`；`UiSkin.Px.Button=24`
   （正文档 12 + 上下切片 10 + 2 = 参考库 OK 钮实测高）。
   **PressOffset/ShadowOffset/UiPressSink 退役**（theme 无按压皮/影子层）。
3. **kind 退役 + 四态映射** ✅：SketchButton 重写为 theme button 四态
   （normal/hot(highlighted)/focused(selected)/selected(Sticky 业务选中)），
   disabled = 常态皮 + **双层影子字**（theme.xml:617-619：background 色 (x+1,y+1) 垫底 +
   disabled 色 #202125 盖面）；`SketchButtonKind/ButtonVariants/UiKit.ButtonKind` 删除，
   12 个调用点文件机械清零。
4. **四屏装配重排** ✅：主菜单窗体化（window_with_title 容器 + 标题带 + VBox 按钮列，
   间距 = border 相加 = 10）；EnsureWindow 标题区 = 带内全高 + 禁 wrap + 字号 12
   （15 格带按 8px 字设计，16px 实拍压带）；装饰层（Plate/标题/窗控钮）对内容 VBox 免排。
5. **capture 对照** ◐：worktree 实拍四屏有效（见下），**修复后重拍被环境挡住（见二）**。

### 两个关键 schema 发现（theme.xml 精读）

- parts 的**切片件没有 w/h**——宽 = w1+w2+w3、高 = h1+h2+h3（首版按整图件解析 NRE，已修）。
- button_normal 脸色 #292b30（41,43,48）、hot 脸 #41444a——直切后与库逐位一致，无需转写。

## 一、无头验证链（全绿，worktree `temp/ase-x1`，分支 `feat/ase-x1-verify` 已 merge）

- **环境修复**（登记）：① worktree manifest 临时移除 `com.coplaydev.unity-mcp`
  （离线环境 git 依赖解析失败；MCP 桥与验证链无关，**主仓 manifest 未动**——
  但 merge 已把该移除带进主分支，联网重装时需自行恢复或保持移除）；
  ② `GraphicsCaptureCompat`（Core 程序集）反射垫片：**-nographics 编译域剔除
  ScreenCaptureModule**（CS0103），三处出图钩子调用改走它——这是预存在的无头编译
  边界（无头链自这两个出图钩子加入后就没全量编译过），非本波引入。
- **rebake**：61 张 tone 件 + 108 件直切 + 判据全过 + 图集重生成（aseParts/asePartNames 平行数组）。
- **装配**：SceneSetup.BuildAll + ManagementSceneSetup.BuildAll 双绿。
- **Tab 判据口径**：1px 网格下页签"腿脚"（左右带直通底边的 1px 端头）豁免端点判定
  （×2 时代腿 2px 宽互相支撑故未暴露）；Unity 坐标 y=0 在下，腿脚在 cy==0。

## 二、环境事故记录（本会话踩坑，新会话必读）

1. **主仓常开编辑器（Administrator 启动）救不活，需人工重启**：
   - 现象链：file watcher 失灵（touch/Ctrl+R Refresh 0.012s 空转、踢不醒）→ 新建 .cs 文件能
     触发编译但 Bee 读到 **stale 文件快照**（报大量我从没写过的老 API 错误
     CS0246 TextFittedSize / CS0117 FitMode.Preferred）→ 清 Library/Bee 无效。
   - 进程属 Administrator，当前 shell 杀不掉（拒绝访问）。
   - **处置：创始人下次手动关闭它并重启编辑器**（无未保存工作，场景都是装配产物）。
2. **GUI 第二实例起不来**：`[Licensing::Module] Error: Access token is unavailable`——
   许可单会话被老编辑器占用。**重启后单实例即可**。
3. **capture 产物在 worktree**：`temp/ase-x1/pirate-crew/export/ui-pixel-4c/`（四屏 + settings/confirm）。
   主仓重拍后产物落 `pirate-crew/export/ui-pixel-4c/`（CaptureDir 相对工程根）。
4. **遥控 flag 路径是相对工程根的 `../external/`**——worktree 工程的 flag 在
   `temp/ase-x1/external/editor-remote-play.flag`，不是主仓 external/（本会话写错过一次）。

## 三、待创始人验收（重启编辑器后按序）

1. 主仓确认 worktree 四修已 merge（`8b937abd`），**重启编辑器**（关 Administrator 老实例）。
2. 重跑：`rebake` → `assemble` → 六屏 capture
   （`capture:MainMenu` / `:LevelSelect` / `:CrewManagement` / `:Battle` / `:MainMenu+settings` / `:MainMenu+confirm`）。
3. 过目对照（worktree 首拍已确认的方向）：
   - ✅ ×1 直切观感：按钮唇边清晰零压缩（对照旧实拍 `export/ui-pixel-4c/MainMenu.png` 的糊边）
   - ✅ theme 灰面按钮四态（kind 彩面全废）、金底选中行、禁用行暗底、蓝字分组线、滑条/复选全按库
   - 已修待确认：标题带内 12px 字居中（旧拍压带/残影）、主菜单窗体 132 宽、星级让位
   - 观察项：confirm overlay 本轮未激活（与 settings 同 flag 通道，settings 成功——待重拍定位）
   - Battle 屏未细看（本波未重建 Battle 场景，HUD 不在文件域）

## 三之一、主仓重拍结果（2026-09-25 晚，创始人关闭旧编辑器后）

rebake/装配/六屏 capture 全部重跑成功（编辑器 PID 16908，许可离线告警不阻塞）：

- **MainMenu**：残影消失、窗体 132 宽、按钮列居中——修复生效；**新 bug：标题带内文字整个
  不显示**（12px「海盗军团夺宝 3D」应放得下 108 格区——待查，疑 Truncate/alignment 与
  rect 定位交互）。
- **settings / CrewManagement / LevelSelect**：与 worktree 首拍逐字节相同——**根因找到**：
  标题有三套实现，`UiKit.EnsureWindow`（主菜单/模态，已修 12px）之外，
  **MenuUiBuilder.BuildSettingsPanel 与 ManagementSceneSetup（SketchPanel.Titled）各自
  独立装配标题**（不走 EnsureWindow），仍是 16px 压带旧实现——下一会话第一项：三套标题
  收口到 EnsureWindow 单轨，顺带修 MainMenu 标题不显示。
- confirm overlay 仍未激活（FindObjectsByType 对 inactive 对象的查找待查）。

### 中文观感口径（创始人问「Aseprite 有适配过中文吗」——没有，需自定口径）

theme.xml:8-11 字体只有 Aseprite/Aseprite Mini 内置拉丁位图字，**官方从未做 CJK 适配**。
theme 全部排版参数（带 15、按钮 border 4/6、行高、textbox 内缩 4）按 **8px 拉丁字**设计；
我们正文档 12px（1.5×）、标题 16px（2×）硬塞，就是「排版总差一口气」的根源。
**裁决（2026-09-25 晚创始人）**：按 12px 中文微调各组件（标题三套收口 12px 已落地；
其余组件按钮 24/行高 36 体系本就匹配 12px，保持）。

### 像素对齐根治（2026-09-25 晚，`1eacb00c`）

创始人报告「文字像素没对齐屏幕像素、横线变浅跨两像素、笔画粗细不一」——像素取证实锤：
**半画布格偏移**（窗体带/描边屏幕相位全奇数 = 元素缘落 x.5 画布格，2× 屏放大摊成两行各半亮度）。
两板斧落地：
- `PixelSnapText`（BaseMeshEffect）：TMP 浮点排版顶点 round 到画布网格，三处 TMP 创建点统一挂载；
- 奇高+中心锚补半格（主菜单窗体 y -6→-6.5）。
实测：窗体带 y=408 / 按钮描边 y=438 全偶数相位（修复前 403/407/431 全奇）；
MainMenu 标题完整显示（Truncate 回退 Overflow）；confirm 弹窗激活（overlay 名
BackConfirmDialog→ConfirmDialog 实锤修正）。**新纪律**：中心锚容器尺寸优先偶数，
否则位置补 0.5；TMP 一律经三处工厂创建（自动带 snap）。

### 打磨清单（下一轮）

1. ~~确认框本体改走 CreateModal/EnsureWindow 标准路径~~ ✅ **已落地**（2026-09-25 深夜，见下「执行记录」①）。
2. ~~其余屏奇数尺寸/中心锚排查~~ ✅ **已落地**（同批次，设置面板/确认框/管理选关列表窗体逐个相位检查并归整）。
3. **MC 式像素密度可调档**（创始人 2026-09-25 深夜方向：像 MC GUI Scale 一样的多档
   下拉，默认 ×2）——设计要点：Unit 从 const 改运行时可变（编译期折叠要清：唯一
   运行时消费点 = CanvasScaler.scaleFactor；烘焙/布局已 ×1 无折叠）+ 设置面板加档位
   行 + 全屏/窗口尺寸非整倍时向下取整留黑边（MC 同款行为）。**注意**：编辑器 Game
   视图自由尺寸下任何固定倍率都糊（窗口 ÷ Unit 非整数），这是恒定像素密度栈的物理
   边界，档位化+黑边是标准解。
4. **16px 字体重排专项**（创始人提议「换成 16px 字体」）：16px 字装不进 15 格标题带/
   24 高按钮——需要整套容器连锁重排（标题带 15→18~20、按钮 border 重标定），不是
   改一个字号常量；与密度档联动裁决（×3 密度 + 12px 字 = 36 屏字高，或 ×2 + 16px 字
   = 32 屏——两路观感近似，前者保 theme 数字、后者保画布密度）。×3 对比实拍上一轮
   未拍成（常量改动的编译窗口没吃进，两图相同）——先改后等编译日志确认再装配。
5. **已烘未接屏件按场景渐进**（页签/tooltip/滚动条/组合框/右键菜单 menu part——
   场景依赖裁决见 §一 白名单注释）。
6. **待办（本轮新发现，未做）**：`PixelSkin.Window(tone)` 手绘 tone 族窗皮已无调用点
   （`SketchPanel.Titled` 改走直切件后），与烘焙器 windows 数组（7 件）+ 校验器断言
   一同构成残留；Battle 返回确认框正文 rect 仍宽 100（同「VBox 不控宽」陷阱，
   长文案换行点偏窄）——两者都留给下一轮，改前先确认 Battle HUD 无别的消费者。

### 打磨波①②执行记录（2026-09-25 深夜会话）

分两轮「改码 → harness 编译 → 编辑器 recompile → assemble → 四屏 capture → 像素探针」推进。

**① 确认框收口**（`MenuUiBuilder.BuildConfirmDialog` 整体重写）
- 改走 `UiKit.CreateModal`：Dim 遮罩 + theme window 直切窗体皮 + 标题带「确认」+ 右上 × +
  卡片高随内容（ContentSizeFitter）；与战斗侧 `BattleHudBuilder.BuildBackConfirm` 同形同数
  （卡宽 160 / 正文 133×20 / 缝 3u）。
- 旧装配（SketchPanel Dark 手摆 240×132 + 无窗体皮 + 正文 `LightOf(Frame)` 暗字）退役；
  返回契约（Root/Message/OkButton/CancelButton）不变，`MainMenuController` 零改动。
- **新发现的陷阱**：`UiLayout.VBox` 硬编码 `childControlWidth = false`，`Element()` 声明的
  首选宽**不参与排版**——正文回落 rect 默认 100 宽，十个字被挤成两行（实拍实证）。
  现改为「显式给 rect 宽 + 短文案禁换行」，并在调用处写明纪律。
- 战斗侧同款标题字号 16→12（`CreateModal` 的 15 格带装不下 16px，与 12px 裁决对齐）。

**标题与窗皮单轨**（§三之一点名的「三套标题收口」）
- 新增 `UiKit.EnsureTitleLabel(window, title, font, size, rightReserve)`：带内左上、
  **字号缺省 12**、字色 `PixelSkin.Theme.Text`、右让窗控钮位、幂等复用同名孩子、
  **自带 `PixelSnapText`**。
- 三处调用点改调它：`EnsureWindow`（主菜单/模态）、`MenuUiBuilder.BuildSettingsPanel`、
  `ManagementSceneSetup.CreateTitledListPanel`（后者原来是 16px 压带的旧实现）。
- `SketchPanel.Titled` 窗皮由手绘 tone 族 `PixelSkin.Window` 改为**直切件 `Ase("window")`**
  ——设置面板与列表窗体此前穿的是手绘近似件，与主菜单/模态分叉；两者几何同为 13×24 /
  切片 3·5·3·15，故换件不动布局。
- 补两处漏挂的顶点对齐件：`UiKit.EnsureWindow` 的标题标签、`MenuUiBuilder.CreateTextExact`
  （此前只有 `UiKit.CreateText` / `RuntimeUiBuilder.CreateText` / `SketchButton` 三处挂）。

**② 中心锚奇数尺寸相位排查**（判据：画布 = 屏/2，整数画布格 ⇒ 屏幕坐标必偶）
- 修复手法取「**尺寸偶数化**」而非「位置补 0.5」——因为面板内按钮是偶宽（12×字数+8），
  补半格会把相位缺陷转移给按钮，偶数尺寸则整链归整。
- 改动：设置面板卡 427→426、行容器/行板/分组行 383→384、提示行 393→394；
  管理/选关列表窗体 333→334。
- 实拍相位（屏坐标 = 边缘起点）：设置卡左 533→**536**、行板左缘 `[579,580]`→**`[578,579]`**、
  行板右缘 `[1339,1340]`→**`[1340,1341]`**、列表窗体左 `[629,…]`→**`[628,629]`**；
  确认框卡左 **802**、上 **468**，主菜单窗体顶 **406**、标题墨迹自画布 419 起（窗缘 414 + 5 设计格
  内边距，看着"贴边"是窗皮最外 1 格本身为暗列）——**全部偶数起点**。
- 边缘剖面均为 2 屏像素同值（如 `68 68`）= 1 画布像素线整格落位，无跨格半亮。
- **两条纪律的分工（本轮实测支撑的机制推断，标注待确认）**：`PixelSnapText` round 的是
  Graphic **局部**坐标，所以它治的是"rect 内笔画一致（2 格不撑成 3 格）"；画布网格对齐
  由 **rect 相位**决定——rect 落半格时，局部取整也救不回来（世界相位仍带 .5）。
  实测证据：设置面板字段名墨迹随行板 383→384 整体平移 2 屏像素（595→594）。
  **待确认**：拿一个 rect 明确落在半画布格的文本，挂/不挂 `PixelSnapText` 各拍一张即可定论。
- 复核工具已入库：`tools/ui-review/phase_check.py`（沿扫描线切等值游程，2 屏像素长的
  游程 = 1 画布像素线，起点奇偶即判整格/半格；支持 `--pair` 对照改前图）。
- 未覆盖：Battle HUD 全屏件（本轮不在文件域，未排查）。

### 按钮三缺陷（2026-09-25 深夜·创始人走查后同轮修复）

创始人实机走查提出三条：「按钮之间互相不要重叠 / 文字在按钮内摆位不要偏移 / 各按钮相对位置适当对齐」。
逐条量测定性后修，四处代码改动：

**缺陷 1：列表行内按钮高于行距 → 相邻按钮互相叠印**
- 实锤：行 = 16 设计格 + 行缝 1 = **行距 17**，而 `LayoutRowContent` 把动作按钮高写成
  全局令牌 `UiSkin.Px.Button` = **24** → 每钮压住下一行 7 格。沿按钮左描边竖切：每 17 格
  只见一段被下钮盖掉的描边（13 格亮 + 1 格暗 + 2 格面），钮盒的上下缘不闭合。
- 修：`rect.sizeDelta.y = Mathf.Min(rowHeight, UiSkin.Px.Button)` —— 行高本身就是 theme button
  的**原生高**（14×16，切片 h1=4/h2=6/h3=6），按行高摆即 1:1 无压缩、无重叠。
  实拍复核：每 17 格内为完整闭合钮（13 格高光列 + 2 格底斜角 + 1 格缝），改前的"上缘被金底行切掉"消失。

**缺陷 2：文字在按钮内偏下 1 格**
- 实锤：主菜单首钮盒 = 屏 438..486，sprite 字区（两条高光行之间）= [4, h-6] → 盒高 24 时字区中心 =
  距顶 10.5 格，而字盒按几何居中（上下各让 2）→ 字心落 12 格 = **偏下 1 格**（实测墨迹中心 462 屏像素
  vs 字区中心 460）。
- 修：`SketchButton.AddLabel` 字盒改 [3, h-1]（底边多让 1 格）→ 字心 = 几何中心 + 1 格。
  实拍复核：墨迹中心 462 → **460.5**，与字区中心 460 对齐。
- **同轮顺带**：标题与列表行标签原用 `TextAlignmentOptions.MidlineLeft`（按字体基线中线对齐），
  中文墨迹整体偏上——主菜单标题实测比带心高 3.5 屏像素。改用 `Left`（= Middle+Left，与按钮字
  `Center` 同口径）后带心差降到 1.0 屏像素（理论残差：字区行数为偶 ⇒ 中心是半整数，整格文字只能差 0.5 格）。
- 残留：行标签（Middle 于行盒）与行内按钮字（Middle 于 sprite 字区）有 0.5 格理论差——两者所属
  盒子不同，各自居中即最优，不硬凑。
- **未一并扫（本轮范围外）**：仍用 `TextAlignmentOptions.MidlineLeft` 的文本（设置面板字段名、
  管理屏概况/状态行、HUD 文字等）比 Middle 偏高约 1 画布格，与标题/行标签属同一族问题；
  改动面广（多屏 + 战斗 HUD），需重拍复核，留给下一轮统一——判据同 `phase_check.py`：
  文本墨迹中心 vs 所属盒中心。

**缺陷 3：底部按钮手写坐标错位**
- 实锤：船员管理三钮写成 `(-264, 72) (-24, 72) (42, 24)`——前两钮相距 376 屏像素（188 设计格）、
  第三钮错行且不居中，整行中心 **672**（画布中心应 960）；选关两钮 `(-84, 64) (40, 21)`，
  第二钮还压状态行（y=21 与状态 y=40 叠印）。
- 修：`ManagementSceneSetup.CreateCenteredButtonRow` —— 一行居中排布（缝 = theme 按钮左右切片
  相加 8，与主菜单按钮列的纵向缝 10 = 6+4 同一条"缝 = border 相加"口径），两屏各一行。
  实拍复核：船员管理三钮宽 52/52/64 格、缝**均为 12 格**（盒内暗列内缩后）、整行中心 **960 = 画布中心**；
  选关两钮同口径，行位 y=56 让开上方提示行（90）与下方状态行（40）。

**本轮新踩的坑（写进纪律）**：
`UiLayout.HStack(controlWidths: true)` 会去问子件身上**优先级最高的 ILayoutElement**——按钮身上
若没有内层布局组，就会落到它自己的 `Image`（九宫格切片件的首选宽 = 切片和 ≈ 8 格），按钮被挤成窄条
（实拍：只剩文字不见盒）。底部按钮行因此改 `controlWidths: false`（组只按各钮自身尺寸排布 + 居中）；
列表行内按钮恰好有 `LayoutRowContent` 加的内层 HLG 提供首选宽，故那处不受影响。

**缺陷 4（同轮顺带查出的真缺陷）：按钮动作文案写到影子层 → 文字不显示、按钮被挤窄**
- 症状：选关列表整列「出战/出海」按钮只剩细条（实测宽 6 设计格）、船员列表已解锁行按钮无字；
  禁用（未解锁）行反而"看得见字"——因为禁用态会把影子层激活，那层恰好收到了文案。
- 根因：`SketchButton` 按 theme 绘制序把影子层（`LabelShadow`）插到**兄弟序 0**（压在标签下，
  绘制顺序正确），而 `RuntimeUiBuilder.GetButtonLabel` 用
  `GetComponentInChildren&lt;TextMeshProUGUI&gt;(true)` 泛搜 → 先命中那层**不可见的**影子。
  文案写进影子、可见标签留空；按钮的宽由内层 HLG 按"标签首选宽"算 → 空文案 ⇒ 收窄到切片和。
- 修：`SketchButton` 增显式出口 `Label`（按名取 `Label` 孩子，场景重载后兜底重取）+
  静态 `LabelOf(Button)`；`RuntimeUiBuilder.GetButtonLabel`、`UiKit.FitToLabel`（两处重载，
  原先会按**影子层的旧文案**算宽）、`LevelSelectController` 结算按钮两处共 5 个调用点改走它。
  实拍：选关行按钮 6 格 → **30 格**（含居中「出战/出海」文字）。
- 同族顺带：结算弹窗那对按钮也是手写坐标（`(-120,52)/(84,52)`，整对中心 -18 ≠ 卡心、缝 160 格）
  → 同 `CreateCenteredButtonRow` 收口。

**两条可复用纪律**：
1. **行内件高 ≤ 行距**——行高不是"内容能多大"，超过就会压邻行；
2. **按钮字盒底边比顶边多让 1 格**——theme button 切片 h1=4 / h3=6 不对称，几何居中必然偏低 1 格；
3. **取按钮文案只走 `SketchButton.Label` / `LabelOf`**——泛搜必命中不可见的影子层。

### 列表按 `list_item` 口径重做（2026-09-25 深夜·创始人走查「这真的是 Aseprite 的列表用法吗」）

**结论：不是。** 我们此前的列表行 = 行文本（用全角空格凑「名称　尺寸　状态」）+ 行尾一枚 44px
**文字按钮**（出战/出海/未解锁）——这套是自创的，Aseprite 的库里没有。theme.xml 的列表只有两条 style：

```xml
<style id="list_item" border="1">                     <!-- 行 = 纯色三态面 + 一条左对齐文本 + 可选图标 -->
  <background color="listitem_normal_face"/>                     <!-- #41444a -->
  <background color="listitem_selected_face" state="selected"/>  <!-- #e1b85f -->
  <background color="face" state="disabled"/>
  <text color="listitem_normal_text" align="left middle" x="1"/>
</style>
<style id="list_header_label" padding="2"><text color="text" align="left" x="2"/></style>
```

- 行本体**就是**命中区（面三态 = 它的按钮皮），`list_item` 没有「行内按钮」这个能力；
- 行内允许的交互件只有 **`timeline_box` 那种小图标开关**（时间轴图层行的 eye / padlock /
  continuous，theme.xml:859-888）——**不是** 44px 文字按钮；
- 图标可右对齐（`align="right"` 在 theme.xml:556/577 有实例）；多列信息走
  `list_header_label` 表头 + 列对齐（文件列表族配色 `filelist_even/odd_row_face`）；
- 动作放在**独立的按钮行/工具栏**（对话框底部的 button 行）。

**改法（本轮）**：
1. 行尾文字按钮全部拆掉；行根挂 `Button`（`transition=None`，面三态承担视觉）= 行即命中区，
   单击行 = 进该关 / 上阵·撤下；锁定行 = `interactable=false` + list_item 的 disabled 面。
2. 行文本只留内容（名称 + 图幅 / 名称 + 等级经验 / 锁定行的解锁条件），**状态词不再复读**
   （原「可出战/已通关/未解锁」= 行态与图标的本职）。
3. 星标改**行内右对齐图标组**（`UiLayout.Ignore` 不吃行 HStack），右边距 = list_item border（1 设计格）。
4. 代码：`RuntimeUiBuilder.CreateRow` 挂行按钮 + 新出口 `RowButton()`；`LayoutRowContent(row, label, h)`
   删掉按钮通道（含"按钮高 ≤ 行距"那段）；`CreateButton()` 无调用者即删；`LevelListRow` 去掉 ActionLabel。

**残留（下一轮）**：view 的滚动条未接（内容未溢出，暂不需要；已烘的 `mini_scrollbar` 件在打磨清单第⑤项里）；
行内 `timeline_box` 小图标开关未做（若要把"上阵"做成行内开关而不是整行点击，照 theme.xml:859 那族烘件即可）；
`UiStrings` 的 LevelFight / WorldSetSail / CrewEnlist / CrewRemove / CrewLocked / WorldRowAvailable /
WorldRowCleared 七条暂无引用（前四条是动作动词，若恢复底部动作钮会用到，暂留）。

### 配色审计（2026-09-25 深夜·创始人追问「真的全学来了吗？比如配色」）

**结论：没有全学来。** theme.xml 有 **82 条 `<color>`**，我们只搬了 **13 条**
（`PixelSkin.Theme`：text / button_selected_text / face / background / disabled / check_hot_face /
selected / selected_text / separator_label / tooltip_face / tab_normal_text / workspace /
status_bar_text）。屏上其余颜色来自 **StickTokens**——那是隔壁 Godot 版
（`Resources/UI/StickWorld/ui_tokens.json`）的调色板，不是 Aseprite 的：

| 自造色（StickTokens） | 值 | 用在哪 | theme 里的对应 |
| --- | --- | --- | --- |
| `TEXT` | 近白 #EDEFF5 | 管理屏标题/概况/结算行 | `text` **#C0C0C0** |
| `TEXT_DIM` / `TEXT_FAINT` | 白 55% / 32% 透明 | 状态行/提示/角标 | `tab_normal_text` #7D7D7D、`status_bar_text` #636D79（**平面灰**，非 alpha 白） |
| `ACCENT` | #F2AD40 | 结算星级、强调 | `selected` **#E1B85F** |
| `WINDOW_BG` | 近黑 88% 半透 | 全屏底、结算卡底 | `desktop` = `window_face` **#2C2C30** |
| `INK` @0.35 | 近黑压透明 | 未点亮星 | `disabled` #202125 |
| `MODAL_DIM` | 黑 60% | 模态压暗 | **库里没有**（Aseprite 无 dim；这是我们自己的设计文档规定的，见下"分工"） |
| `INFO`/`WARN`/`SUCCESS` | 蓝/金/绿 | 语义提示（Toast 等） | 库里**没有绿**；只有金 `selected`、蓝 `link_text` #6E9ADB、红 `flag_active` #C75A68 |
| `GROOVE_BG` + 相机清屏暖棕 #241A12 | — | HUD 条槽底 / 场景底色 | 凹槽走 `sunken_normal` 件；桌面 = `window_face` |

**本轮改掉的（提交见下）**：
1. 全屏底 + 相机清屏 → theme `desktop`（`window_face` #2C2C30，不透明）；
2. 管理屏/选关屏标题、概况、结算行文字 → `PixelSkin.Theme.Text`（#C0C0C0）；
3. 状态行/提示/角标 → `Theme.TabNormalText`（#7D7D7D）/ `Theme.StatusText`（#636D79），
   不再用 alpha 白；
4. 星级点亮/熄灭 → `Theme.Selected` / `Theme.Disabled`；
5. 结算卡底 → `Theme.Face`；
6. 设置面板行底 → **theme list_item 纯色面**（#41444A，与列表行同色同形态），
   字段名文字 → `Theme.Text`；旧的自造 tone 族 Light Plate（带斜面、灰阶偏暗一档）退役；
7. 分隔线 → theme 直切件 **`separator_horz` / `separator_vert`**（sheet.png 32,80 / 32,96，
   9×5 / 5×9 的**点状蚀刻线**：3 像素周期 2 实 1 虚，色 #202125），`Image.Type.Tiled`
   （拉伸会把点拉成实线）；旧的自烘 1u 实线件退役。

**仍未对齐（下一轮候选，均需你点头再动）**：
- **模态 dim 遮罩**：库里没有。我们自己的 [UI-UX 规范](../UI-UX与中文本地化规范.md) §模态层规定了
  "全屏遮罩 黑 60%"——**建议口径：Aseprite 管组件/配色/排版，我们自己的设计文档管交互语义**，
  dim 属后者，故保留；
- **标题墨色描边**：Aseprite 不给文字描边（标题就是 `text` #C0C0C0 压在 `window_titlebar_face` 上）；
  我们的 `ApplyStickTitleOutline` 是自加件（规范 §5.3 自己规定的），留待裁决；
- **语义色**：INFO/WARN/SUCCESS 在库里无对应（无绿色），可收敛到 `link_text` 蓝 / `selected` 金 /
  `flag_active` 红 三色；`UiSkin` 里那批别名被 `UiSkinTests` 钉在 StickTokens 上，要改需同改测试；
- **tone 族（Plate/Track/Panel/Tab/Ring/Pip/Sep/Shadow）**：灰阶来自本工程调色板槽位（非 theme 值），
  战斗 HUD 仍在用；收敛=逐个换成 theme 件的色或改用直切件，是一整批活。
- 82 条色里其余未用到的（`detail_text`、`entry_suffix`、`menu*`、`timeline*`、`filelist*`、
  `popup_window_border`、`select_box_*` 等）多数对应编辑器专属功能（时间轴/文件列表/选择框），
  我们暂无对应屏——用到哪个再搬哪个。

### 像素对齐总账（2026-09-26 凌晨·创始人走查「文字还是有粗有细、复选框和滑条错位、分隔条穿模」）

**这一轮把"像素对齐/二值化"从肉眼看改成可复算的判据，并抓出四个真根因。**

**新探针 `tools/ui-review/pixel_fidelity.py`**（判据，可复跑）：
- 平涂色板 = 出现次数 ≥ 图像 0.01% 的颜色；像素不等于任何平涂色 → 判"非平涂"；
  再反解插值端点与系数 t，`0.15 < t < 0.85` 计为**中间灰阶**（真正的抗锯齿污点）。
  判据 =「文字颜色深浅是否彻底二值化」：中间灰阶连通块应只剩件角的零星几像素。
- 边缘相位审计（脚本外三行 python）：1 画布格 = 2 屏幕像素，故**所有可见边缘必须落在偶数屏幕坐标**；
  改前/改后逐屏比值就是"是否真的落在显示像素上"。

| 屏 | 改前半格边缘占比 | 改后 | 按钮字笔画宽度（屏幕像素） |
| --- | --- | --- | --- |
| MainMenu | 14.01%（722 条） | **0.00%** | 全偶：2/4/6/…/22，奇数 **0 条** |
| settings | 3.57%（416） | **0.00%** | 全偶 |
| confirm | 6.25%（381） | **0.00%** | 全偶 |
| CrewManagement / LevelSelect | 0.00% | 0.00% | 全偶 |
| 中间灰阶（二值化） | settings 168 px（滑条手柄糊） | settings 8 / confirm 16 px（只剩件角） | — |

**根因一（元凶）：字体错档——显示字号 ≠ 字体原生档**。主菜单四钮与设置页四个选项块的字，
传的是 **16 原生档**的 `ZhengGeDianHei16`、却按 **12** 号显示 → `scale = 12/16 = 0.750`，
非整数缩放让同一笔画在 1/2/3 屏幕像素之间跳（实拍笔画宽度 1,2,3,4 混排）。
证据＝实机度量转储（`FontProbeDumper.DumpSceneTexts`，现随每次截图同拍一份）：
`font=ZhengGeDianHei16 size=12 nativePoint=16 scale=0.750` ×12 条。
**修法（控件层，一次治一族）**：`SketchButton.AddLabel` / `SketchCheck.BuildLabel` 一律走
`UiKit.ResolvePixelFont(字号, 传入字体)` 就近取原生档——与 `UiKit.CreateText` /
`MenuUiBuilder.CreateTextExact` 同一口径，调用方再传错"族"也不会错档。

**根因二：`PixelSnapText` 只 round 局部坐标**。局部整数挡不住"父布局把 rect 停在半格上"
（设置页行内居中的 15 高字盒相位 = 0.5）。改为**按画布空间相位取整**：把本地原点换算到
画布像素空间取小数部分，`round(局部 + 相位) − 相位`，与 rect 停在哪无关。

**根因三：图标被拉伸**。窗控钮图标（theme `window_close_icon` 5×6）旧实现 `Stretch` 进
9×11 钮盒（1.8/1.83 倍）→ 竖线落小数格（confirm 屏最后 22 个半格像素）。改为按件自然尺寸
**整数居中**（同 Aseprite 的整数除法 `(9−5)/2=2`、`(11−6)/2=2`）。

**根因四：`SketchCheck` 图标尺寸是 ×Unit 时代残留**（`8 * PixelSkin.Unit` = 16 宽），
而 theme 图标件就是 8×8、文字起点 x=14 → 图标压掉首字（实拍四个选项块首字被深色图标盖住）。
改回 8×8；标签对齐 `MidlineLeft → Left`（theme `align="left middle"`）。

**设置面板其余按库收敛**：
1. **滑条改 theme `slider` 族**：库只声明 `slider_empty`（槽）+ `slider_full`（充满段）两张
   16×16 九宫格（w 5/6/5、h 5/6/6），**没有拇指件**。旧实现取时间轴 `mini_slider_thumb`（5×4）
   当手柄＝跨族自造，且盒子 11 高 < 件高 16 → 九宫格竖压（实拍槽内色带糊、手柄顶到槽顶、
   下缘穿出槽底）。现按件高 16 摆、整条槽即拖拽区（UGUI `Slider` 支持 `handleRect == null`，
   拖动/点击落点按自身 rect 算）；宽取 186 偶（填充边界在偶数分档落整格）。
2. **行高 27 → 28**（取偶）：行内滑条/单选图标是"行内垂直居中"的子件，行高奇数时居中偏移
   (H−h)/2 落 .5 → 子件整体压半格。分组标签行取 **13**（奇数）是因为行内线盒高 5，同奇偶才取整。
3. **分组线让开文字**：theme 的 `horizontal_separator` 是"window_face 底色 + 点线 + x=4 蓝字"
   三层，照库字会压在线上（实拍「音频」「视频」被线穿过）。库里那张 `window_face`(#2c2c30)
   与我们窗体面（window 直切件中段 **#2f3136**）不同色，用底色底衬会在卡面留偏色补丁 →
   取"线从标签右缘之后起铺"（起点 = 字宽 + theme `border=2`）。
4. **删掉标题带下的自造蚀刻线**（`SettingsPanel/TitleSeparator`）：theme 的窗体没有这一层
   （带底分隔已烘在 `window` 件第 14/21 行），全区也只有设置卡挂过它 → 既非库件又破自家一致性。
   要恢复请创始人开单。
5. **标题字盒按 theme `window_title_label margin-top="5"`** 上留 5 格：旧实现铺满整条标题带，
   12px 字行高约 15.6 > 带高 15，TMP 居中后墨迹顶到带上沿、贴住窗体边框（实拍「设置」顶被切）。

**查实但本轮不动的一项（需裁决）**：画布**顶点色在 Linear 色彩空间被 8 位量化**，
`Image.color` 指定的色与 theme 值差 ±1（实拍：行板指定 `#41444A` 渲成 `#404549`，
而同色的贴图像素渲得准）。试过 `Canvas.vertexColorAlwaysGammaSpace = true`——字色被推成
225（更错，已回退）。根治要么"平涂件改用 1×1 贴图给色"（sprite 路精确），要么工程切
Gamma 空间（影响 3D 全链）。±1 肉眼不可见，等裁决。

### 参考回归对照：选择框 / 拉动条 / 标题带（2026-09-26 凌晨·创始人追问「真的对齐参考项目了吗」）

拿 `theme.xml` + `sheet.png`（权威源）与 `docs/images/ui-pixel-ref/ref-pixel-tool-dialog.png`
（Aseprite 自己的 New Sprite 对话框实拍，注意它是缩放过的截屏、颜色有噪，只作结构对照）逐项核。

**① 选择框（check_box / radio_button）——两处真缺陷**

- 规格核对 ✓：图标件就是 8×8（`check_normal` 空框 / `check_selected` 蓝勾 / `radio_normal` 凹点 /
  `radio_selected` 凸点），图标 x=2、文字 x=14（theme 原值），常态**无面**（只有 mouse/focus/disabled
  三态有 `<background>`）。实机量：图标墨迹左偏 3 画布格 = 件内 1 格留白 + x=2 ✓。
- **缺陷 A（遮蔽基类 OnEnable）**：`SketchCheck` 写的是 `private void OnEnable()`——Unity 按名派发
  只认最派生类那一个，`Selectable.OnEnable` 里的**状态机初始化 / `s_Selectables` 登记 / 颜色态应用
  全都没跑**：常态颜色 `normalColor`（alpha 0）从未生效，画布渲染器颜色停在装配时的白 →
  **选项块在常态就顶着 `check_hot_face`(#575B61) 的面渲染**（实拍四块全带悬停面，参考库里
  checkbox 常态是纯平的）。改为 `protected override void OnEnable() { base.OnEnable(); ApplyIcon(); }`。
- **缺陷 B（字段不序列化）**：`_icon` 私有字段场景重载后丢失 → `IsOn = true` 设了也不换图
  （**选中态永远不显示**，四块全是未选中图标）。加 `Icon` 属性按名兜底重取（同 `SketchButton.Label`
  那次的坑）。实机验证（截图同拍临时点亮 Option0）：Option0 暗墨 8×8 满格 = `radio_selected`、
  Option1 = `radio_normal` ✓。
- 焦点面按 theme 改 `check_focus_face` = **#41444A**（原先映射到悬停白）；焦点**描边件** `check_focus`
  （9 切片 2/6/2）仍已烘未接，见打磨清单第⑤项。
- 待创始人一句话的 2 格问题：theme 的 `x="2"` / `x="14"` 是相对**客户区**（border=2 → 绝对 4/16）
  还是相对控件（绝对 2/14）。现取绝对读数（与 `list_item` 的 `x="1"` 同一读法）；
  参考截图分辨率不足以判定，不改。

**② 拉动条（slider）——件与色都对上了**

- theme `<style id="slider">` 只声明 `slider_empty` / `slider_empty_focused` 两个 background part
  与一个文本层；`slider_full`（同样 16×16、w 5/6/5 h 5/5/6）由 Slider 控件代码叠画。
- 件色实测（逐像素）：`slider_empty` 内芯 **#575B61（浅）**、`slider_full` 内芯 **#41444A（深）**
  —— 库里"充满"是**压暗**语法，**不是金色**（旧代码注释"金色充满段"是错的，已改）。
- 实机验证（截图同拍临时灌梯值 0.75 / 0.5 / 0.25 / 0）：fillAnchor `0..值` ✓ 左起 ✓ 比例实测
  0.75 / 0.23 / 0.49 / 0.00 ✓、`handle=null`（库无拇指件，拖拽落点由 Slider 自身 rect 算）✓。
- 库里 `slider` 还带数值文本层（`slider_empty_text` = #202125，居中）——我们的滑条不显示数值
  （调用方未设文本），与库不冲突；要在槽内显示百分比时件与色都齐。

**③ 标题带（灰带）——长度没问题，缺的是右端窗控钮**

- 实测：灰带（`window_titlebar_face` #41444A）在各窗体都**满宽**（主菜单 128/130、设置卡 422/424、
  列表窗体 330/332 画布格；少的 2 格是 `window` 件自身的角像素），带高 = `window` 件 h1 = 15 设计格，
  标题左对齐 margin-left 5、字色 `window_titlebar_text` #C0C0C0 ✓ —— 与参考截图结构一致
  （标题左对齐、带满宽、带下**没有**额外分割线：这也反证本波删掉自造标题分隔线是对的）。
- **真缺口**：参考里带标题窗的标题带**右端必有窗控钮（? ×）**；我们只有模态有 ×
  （`UiKit.CreateModal → EnsureWindow`），设置卡与主菜单窗是"空着一条灰带"。
  → 设置卡补 `window_button` 9×11 + `window_close_icon`（`UiKit.CreateWindowButton`，
  与「返回」同动作 `CloseSettings`；控制器 `settingsCloseButton` 字段 + `SceneSetup` 装配接线）。
- 主菜单窗要不要也补 ×（语义 = 退出游戏，已另有「退出游戏」钮）留创始人定。

### 第二轮：选项块改 buttonset + 滑条数值 + 全量覆盖清点（2026-09-26 凌晨）

创始人二次走查：「位置不对齐、感觉是扁的、拉动条怎么看拉到百分之多少、原软件设置面板里
真是这样的拉动条吗、窗控钮缺了我都没发现说明你在我不知道的地方缺得更多」。

**① 选项块改 theme `buttonset_item`（radio → buttonset）**
- 判据：参考库里对话框内的二选一/三选一（New Sprite 的 RGB / Grayscale / Indexed、
  Transparent / White / Black）**走的就是 buttonset**——一排等宽九宫格按钮，当前值换
  `buttonset_item_active` 件；而 radio/check（8×8 图标 + 文字、常态无面）是**列表里的多选项**
  语法。设置页「画质」「窗口模式」两组是对话框内的二选一 → 该走 buttonset。
- 新控件 `SketchButtonSet`（Button 子类，六件换皮：normal / hot(mouse) / hot_focused /
  focused(键盘焦点) / pushed / active(当前值)，件 16×16 切片 3/10/3 × 3/8/5）✓ 六件都已在
  `aseParts` 表内。
- 布局改成**两块同宽、右对齐成一对**（宽按较长标签算）→ "位置不对齐"随之消失；
  字号按 theme `<style font="mini">` 映射到本工程最小原生档 8（`UiSkin.Font.Tiny`），
  不再出现"8px 图标配 12px 字"的悬殊比例（这也是"扁/小"的来源之一）。
- 实机验证：点亮第一项 → 该件渲染为 `buttonset_item_active`（亮面）、另一项 `normal`（暗面）✓。
- `SketchCheck`（theme radio/check 控件）保留但**设置页已无调用点**——theme 组件库留还是清退，待创始人定。

**② 滑条槽内显示数值**
- theme `<style id="slider">` 本就带 `<text color="slider_empty_text" align="center middle"/>`
  ——Aseprite 的滑条靠**槽内数值**告诉你拉到多少（库里没有拇指件，光看槽色分不清）。
- 补上：槽内居中 8px 百分比（色 = `slider_empty_text` #202125 = theme `disabled`），
  `onValueChanged` 时刷新（0..1 → "0%".."100%"）。实拍：四条槽内各显示 "0%" ✓。

**③ 全量覆盖清点（回答「还有多少是我不知道的缺口」）**

`theme.xml` 现状：**345 个 `<part>`（件）+ 175 条 `<style>`（控件风格）**；我们**已烘 112 件**，
代码直引 20 余件。逐族清点（件数 / 已烘 / 说明）：

| 族 | theme | 已烘 | 说明 |
| --- | --- | --- | --- |
| timeline | 49 | 0 | Aseprite 时间轴专属（帧/洋葱皮/播放头），**我们无对应屏** |
| cursor | 36 | 0 | 编辑器光标，无对应屏 |
| icon / tool | 29+29 | 0 | 工具/图标栏（铅笔/橡皮/图层…），无对应屏 |
| drop | 14 | 8 | 下拉箭头/分隔件；**已烘未接** |
| tab | 13 | 11 | 页签（Aseprite 设置/面板顶栏）；**已烘未接** |
| combobox | 12 | 12 | 组合框箭头族；已接 3（其余未接） |
| mini | 10 | 10 | mini 滑条/滚动条；已接 1 |
| window | 9 | 9 | 窗体/窗控钮/图标 ✓ 已接 6 |
| buttonset | 6 | 6 | **本轮接入**（设置页选项块） |
| colorbar | 6 | 6 | Aseprite 色板取色条，无对应屏（已烘未接） |
| sunken / sunken2 | 6 | 6 | 凹槽件（HUD 槽底仍在用自造 tone 族，未换库件） |
| slider | 4 | 4 | ✓ 已接（空/满 × 常态/焦点） |
| radio / check | 4+4 | 8 | ✓ 已接（`SketchCheck`，设置页改 buttonset 后暂无调用点） |
| scrollbar | 2 | 2 | 滚动条；已烘未接（列表内容暂未溢出） |
| separator / arrow / tooltip | 2+2+2 | 6 | 分隔线 ✓ / 箭头未接 / tooltip 已接 |
| 其余（pal/canvas/ani/debug/selection/outline/tiles/aseprite…） | ~110 | 0 | 全是 Aseprite 编辑器专属（调色板/画布边框/动画/调试/选区），**我们无对应屏** |

**结论**：未烘的 234 件里 ~180 件是 Aseprite 编辑器专属（无对应屏，**不必补**）；
真正"该有而没接"的是 **`drop`（下拉）、`tab`（页签）、`scrollbar`（滚动条）、
`colorbar`、`sunken` 族** + 若干 combobox 箭头——即打磨清单第⑤项"已烘未接屏件"，
按屏需要逐个接（其中 `tab` 与设置页的"音频/视频分组"是同一件事的两种画法：Aseprite
1.2 设置页用页签、1.3 用左侧列表，**要哪种请创始人定**）。

**④ 标题带高度**：创始人明确"长度指高度"，并裁决"没有设置文字高……就当是有意设计" →
记录现状：带高 = `window` 件 h1 = **15 设计格**（不随标题字号推导），标题字 12px 行高约 15.6
居中落在带内（`EnsureTitleLabel` 的 margin-top 5 与带底留白见上节）。

### 第三轮：拽出并修掉一个 P0（设置面板真机打不开）+ 数值/选中态落地（2026-09-26 凌晨）

**① P0：设置面板在真机里根本打不开（`settingsPanel` 引用为空）**
- 起因：截图走查时发现"选项块不亮、滑条全 0%"，于是给截图流程加了**实机状态倒排**
  （`FontProbeDumper.DumpControlStates`：控件态 + 视频服务就绪情况）。数据指向
  控制器没跑 → 一路查到装配侧：**`SettingsPanelResult.Root` 从来没被赋值** →
  `SceneSetup` 把 null 写进 `MainMenuController.settingsPanel` →
  `OpenSettings()` 首行 `if (settingsPanel == null) return;` **直接早退**：
  **真机点「设置」按钮面板不会出现**（只有截图工具直接 `SetActive` 才看得见，所以历次
  走查都没暴露）。修：`result.Root = root.gameObject;`。
- 验证：场景里 `settingsPanel: {fileID: 600865779}` ✓；截图诊断打印
  「面板已激活=True 选项块 active 数=2」✓。
- 【教训】"截图能看见"不等于"真机能用"。凡是隐藏面板，走查必须走**用户路径**（点按钮），
  没有按钮就走 `onClick.Invoke()`，别再直接 SetActive。

**② 选项块当前值 / 滑条数值真正落地**
- 选项块：控制器 `RefreshVideoChips` → 新控件 `SketchButtonSet.Active` → 换
  `buttonset_item_active`（蓝面）✓ 实拍当前选择高亮。
- 滑条：theme `<style id="slider">` 的数值文本层补上（8px 百分比居中于槽内）。
  **不能只挂 `onValueChanged`**：控制器刷新走 `SetValueWithoutNotify`（刻意不触发事件），
  只挂事件会停在初值——新增 `SliderValueLabel`（帧对齐）解决 ✓ 实拍 80%/80%/70%/50%
  与填充长度一一对应。
- 截图流程改成走**用户路径**：`capture:MainMenu+settings` 现在会 `onClick.Invoke()`
  点「设置」钮，拿到的就是真实状态；点不到才回落到直接 SetActive（并打警告）。

**③ 残留一项（已知、已量化、未解）**：滑条槽内百分比文字整列压在半画布格上
（settings 屏 392 条奇数屏像素；**面板内其余文本——按钮/字段/分组标签——全 0**，
即按钮标签 160 偶 0 奇、字段名 144 偶 0 奇、分组标签 258 偶 0 奇、只有该文本 0 偶 104 奇）。
已排除：字号档（`FusionPixel8` size=8 nativePoint=8 scale=1.000 ✓）、缺 `PixelSnapText`
（已加兜底 Ensure，仍不变）、相位漂移（已加"相位变化即重算网格"仍不变）。
未解原因指向该文本的**居中排版偏移是分数值**（同一行文字宽度随 "50%"/"80%" 变化，
居中起点 = (186−文字宽)/2 落小数）而其余文本的 rect 宽/文字宽同为整数。
字仍然清晰（无中间灰阶），影响仅"列落在奇数屏幕像素"。列为待处理项。

**④ 编辑器编译通道的可靠性提醒**：`refresh` + `recompile` 有时**不重建运行时程序集**
（本轮实测 `PirateCrew.UI.dll` mtime 迟迟不更新，导致连拍两轮都是旧构建——症状是
"改了代码但屏上没变"）。**每次重拍前先核 `Library/ScriptAssemblies/PirateCrew.UI.dll`
的 mtime 晚于源文件改动**，否则验证的是旧二进制（本项目之前也踩过同类坑：
file watcher 只认创建事件）。

### 第四轮：选项块"当前值"用哪张件——创始人一问戳破的误判（2026-09-26 凌晨）

创始人："原项目二选一你确定有颜色？？？？"

**核件的结论：我上一轮用错了件。** 逐像素打了三张件：
- `buttonset_item_normal`(96,16)：面 #292B30、底边凸起（常态）✓
- `buttonset_item_hot`(112,0)：面 **#41444A**（更亮）、底边**下沉**（另一种"按下/选中"语法）
- `buttonset_item_active`(112,32)：面 **#4069C2 + 底边 #2A4185**——**确实是蓝面件**

但**theme 自己的风格表**写明当前值走谁：
```
<style id="buttonset_item" font="mini" border="3" border-bottom="5">
  <background-border part="buttonset_item_normal"/>
  <background-border part="buttonset_item_hot"  state="selected"/>   ← 当前选中
  <background-border part="buttonset_item_hot"  state="mouse"/>      ← 鼠标悬停
  <background-border part="buttonset_item_hot_focused" state="selected focus"/>
```
`buttonset_item_active` 是**另一条独立 `<style>`**（Aseprite 用它表达"正在执行/激活"那一类语义），
不是"当前选中值"。参考图佐证：New Sprite 的 RGB / Grayscale / Indexed 三连按钮
**三块同色、没有任何彩色**（实拍该处三块面同为 #292B30 系），选中只体现在"面更亮 + 底边下沉"。

**改法**：`SketchButtonSet.Active` → 换 `buttonset_item_hot`（无彩色，只是更亮+下沉）。
实拍复核：高画质 / 全屏（当前值）面变亮下沉，流畅 / 窗口保持常态凸起；屏上**蓝面像素 0**。

【教训】"参考图看着像彩色"和"库里有一张彩色件"都不够——**以 theme 自己的风格表 state→part
映射为准**，再拿参考图证伪。这条已补进本档 §五纪律。

## 三之二、审计波 + 修复波（2026-09-25，创始人问「覆盖率/误用/Gamma」三连）

**审计结论（数字）**：theme.xml = 345 parts / 175 styles / 82 colors；烘焙白名单 111 件
（styles 引用而未烘的仅 13 件，全是 timeline/aseprite_face 等 Aseprite 编辑器工作区件，
排除有据）；代码真正接线 37 件；`PixelSkin.Theme` 13 常量与 theme 值逐一相符。
175 条 style 全量解析成 state→part 语义表逐条对表——buttonset 八态、窗控钮、分隔线
x=4/border=2、标题带 margin 5/5 全对，我们 `AseLayout` 常量逐值一致。

**创始人裁决（2026-09-25）**：
- **Linear 下纯顶点色平涂 ±1 偏色不修**（行板 #41444A→#404549，实测该屏 20.6 万 px）——
  连 1×1 色块贴图都不必换，工程**维持 Linear**。此题归档，别再翻。
- 仍待裁决：主菜单要不要 × 窗控钮（语义=退出）／设置分区 tab 还是左列表／`SketchCheck`
  退役还是留给列表多选项／已烘未接 74 件的接线优先级。

**src/ui 源码（"他们的代码"）**：aseprite **整库**已检出到 `external/aseprite-ref`
（HEAD `a2d18ca`，与入库真源 md5 一致，勿追新以免与烘焙件串版本）。关键实锄：
`paintSlider`（`src/app/ui/skin/skin_theme.cpp:1667-1794`）——①数值文本**画两遍**：
充满段（暗面）`slider_full_text` #C0C0C0、空槽段 `slider_empty_text` #202125，
分界线穿字形中间逐像素换色；②全尺寸滑条的 focused 件跟**键盘焦点**走，mini 滑条才跟
鼠标走；③左键=落点绝对拖、右键=从按下值相对拖、滚轮步进。

**修复波（8 文件，随本波提交）**：
1. `SliderValueLabel` 重写 + `MenuUiBuilder.BuildVolumeRow`：Value 改**双色裁剪层**
   （两枚同文案 TMP 各挂 RectMask2D，标签盒按整槽取位、锚裁剪框左/右缘 + 槽宽恒定盒，
   框宽 = fillRect 实宽取整逐帧驱动）——单色版高音量时 1.6:1 暗上暗，实测文字像素
   九成背景 = #41444A；
2. 同处 `slider.transition = Transition.None`——theme slider 无任何 hover 态，UGUI 默认
   ColorTint 会把 0.96/0.78 灰乘上调色板件（全工程唯一乘色残留，就此清除）；
3. `PixelSkin.ArrowDown`：Pressed→`combobox_arrow_down_selected`（弹开），悬停不换图标
   （旧版 Pressed→disabled / Hovered→selected 均自造，零调用雷）；
4. `SketchButtonSet`：标签改按件**内容区**取盒（左右 3/底 5/顶 3，中心 +1 律，旧借
   CheckBorder=2 差 1）；`Active` setter 补 `DoStateTransition`（悬停中切值不露旧皮）；
5. `SketchButton.Sticky` setter 即时重挂皮（旧裸自动属性切了不刷新）；
6. `MainMenuController.SetChipSelected` 删 `SketchCheck` 死分支 + 过时注释同步；
7. `SketchPanel.Create` 加 `titled` 参数（Apply 前就位，编辑器预览不露 Plate 皮）；
8. 确认框正文 133→134（内容区 148 居中整格；`MenuUiBuilder` + `BattleHudBuilder` 两处）。
   `SliderValueLabel` 顺带把每帧 GetComponent 改缓存。

**验证状态**：harness All 与整工程 `open` 编译见提交信息；**拍屏复核未做**——主仓编辑器
当时未开（遥控通道休眠），双色滑条/相位待编辑器重开后按 §六通道 `recompile → assemble →
capture:MainMenu+settings+confirm` 补拍（双色判据：0% 行文字全 #202125、80% 行文字主体
#C0C0C0，且无单色旧观感）。

## 三之三、全量迁移 + 陈列廊 + 覆盖率终审（2026-09-25，创始人令「全部组件移过来做成运行时展示面板」）

**覆盖率四张表（口径与分母全部可复算，theme@external/aseprite-ref a2d18ca）**：

| 层 | 分母 | 已达成 | 率 |
| --- | --- | --- | --- |
| 部件素材迁移（theme parts） | 345 | 烘焙 345（判据=与 sheet.png 逐位一致） | **100%** |
| 部件代码接线（正式 UI 取用） | 345 | 49 件 | 14% |
| 部件运行时展示（陈列廊） | 345 | 345 件 | **100%** |
| style 语义复刻 | 全量 175 / 游戏域 111（排除 timeline/workspace 等 64 条编辑器专属） | 25 条已复刻或等价 | 全量 14% / 游戏域 23% |

实现侧（skin_theme.cpp 绘制语义，agent 穷举）：`SkinTheme` 8 个 paint override 中——
已复刻/等价 3（paintSlider 核心、paintListBox 等价纯色底、paintViewViewport 等价）、
无游戏场景 3（entry/comboboxEntry/textbox——游戏无文本输入）、素材+语义备妥未接 2
（menu/menuItem）；基类 11 个实现走 Style 层驱动（对应我们的"theme 语义表→控件装配"通道，
即上表 style 口径）；drawText/caret 系由 TMP 等价承担。现代 aseprite 大部分控件
（button/check/tab/…）不 override paint，全靠 `<style>` 层声明——**style 表就是实现侧的
主体**，部件层 + style 层两张表合起来才是完整覆盖口径。

**全量迁移落地**：`BeveledPixelSpriteBuilder` 白名单退役，烘焙/判据/图集全部枚举
theme.xml `<parts>` 全表；`PixelSkinAsset` 增 `asePartFamilies` 平行数组（家族归类表
`AseFamilyOrder`，26 个家族面板序=表序）。**运行时陈列廊** `PartsGalleryPage` 挂进
`UIShowcase` 场景滚动页（演示页下方）：按家族分面板、件原生尺寸 ×1、id/尺寸/九宫标注、
数据驱动零硬编码（theme 加件重烘焙自动长出来）。入口：编辑器菜单
PirateCrew/UI/打开组件展示 或直接播放 UIShowcase 场景；**主菜单「组件展示」按钮**
（创始人 2026-09-25 令：设置与退出之间第五钮，`OnShowcaseClicked` 走
`EventBus ChangeScene → SceneNames.UIShowcase`；展示窗「返回主菜单」原路回；
SceneSetup/ManagementSceneSetup 的构建表固定清单加第 6 场景——**登记 UIShowcase
必须改这两份同源清单**，单点插入会被整表覆写）。

**素材对错终审结论（源码+逐像素，agent 交叉）**：
- 确认用错并已修：**普通按钮按下态**——Aseprite 按下 = selected+capture 状态位
  （button.cpp:168-175），层匹配取最大 flags（theme.cpp:69-73）命中 `state="selected"`
  → **button_selected 蓝面 #4069C2 + 白字**；旧实现按下用 button_hot（"theme 无按压皮"
  是误读）。`SketchButton`/`UiKit.ApplyThemeButton` 两处已改，按下字色同步转白。
- 判定正确（逐像素复核）：窗控钮图标（sheet 本体即 #C0C0C0，非白蒙版——**禁止**再乘
  Theme.Text）；分隔线 2 实 1 虚周期 3 色 #202125；slider 空/满内芯 #575B61/#41444A；
  buttonset 四态映射；列表行三态；sunken 用于 view 语义。
- sheet 图标是"**换色**不是乘色"（color_selector.cpp:575-583 反证）——将来给白蒙版件
  染色要走换贴图/预制变体，不走 Image.color 乘。

**挂账（存疑项，待裁决/待接线）**：①列表行无 hover——可点击列表按 recent_file 语法
悬停应变暗 #2C2C30（不是 check 系变亮）；②buttonset 业务 Active × 键盘焦点组合态应
`buttonset_item_hot_focused`（游戏无键盘导航，暂不触发）；③窗控图标按下应转白 #FFFFFF
（仅按压瞬间可见）；④SketchCheck 焦点底色 ColorTint 相乘算错（≈#16181C 非 #41444A）
且 check_focus 蓝框未接——控件零调用，随退役裁决一并处置；⑤通用焦点环自造
（Pixel_Focus）与 theme check_focus 两套并存；⑥Tooltip/Scrollbar/ArrowDown/SliderThumb/
WidgetFocus 包装器零调用（死码或待接线）；陈列廊滚动条自身仍手搓 tone 件，可换
theme scrollbar 件；⑦styles 未复刻大块：tab 族/combobox 族/drop_down 族/textedit 族
——即「已烘未接」族，接线优先级待排。

## 三之四、调试场景系统（2026-09-25，创始人令「实摆面板 + Aseprite 菜单复刻 + 可拖动调试菜单」）

**流程（创始人规格）**：主菜单按钮 =「**调试场景**」→ 弹出**可拖动启动器窗**（调试菜单）→
窗里一堆按钮各自打开具体调试面板；**主菜单窗口本身也可拖**（按住标题带）。
全部**运行时构建**（`Assets/Scripts/UI/Debug/`，零场景手术；场景只需重装配刷按钮文案）：

- `WindowDragger`：标题带透明命中板 + IDrag 平移（画布内夹取）——主菜单窗与所有调试窗共用。
- `DebugWindowKit`：窗工厂（SketchPanel titled + 标题 + × + 拖动带）+ 文本/蓝字分组线排版件。
- `AseMenuKit`：**Aseprite 菜单系统复刻**——菜单栏平铺项 + 下拉（theme `menu` 直切件边框 +
  menuitem 行）；**悬停语义 = highlight 档**（亮面 #C0C0C0 + 深字 #2C2C30——menu.cpp:549
  拾取即高亮，源码实锄；theme 表里"面不变字变灰"的 hot 档不是鼠标路径）；分隔线/右对齐
  快捷键/勾选（check_selected @x=2）/子菜单（combobox_arrow_right）/全屏捕获板外点关闭。
- `WidgetGalleryPanel`：**组件实摆面板**（学 game-2 ComponentGallery「活文档」）——每族一件
  全交互：按钮四态（按下蓝面白字/禁用双层字）、buttonset 三连切换、check/radio（悬停亮面
  #575B61 + 图标切换）、双色百分比滑条、sunken 输入框（TMP InputField + 8px 位图字）、
  页签切换（tab 件 + #333 内容面）、列表（悬停变暗 #2C2C30 + 选择金底）、theme scrollbar
  真滚动、组合框（弹 Ase 菜单）、悬停 0.5s 气泡（tooltip 件）。
- `NewSpriteDialog`：**示例图对话框一模一样复刻**（New Sprite：Size 数字输入 + px + Link
  勾选 / Color Mode RGB·Grayscale·Indexed 三连 / Background Transparent·White·Black
  三连 / OK·Cancel，全部可交互）。
- `DebugMenuHost`：启动器（四按钮：组件实摆 / 新建精灵对话框 / Aseprite 菜单栏 / 部件陈列廊）
  ；陈列廊窗复用 `PartsGalleryPage`（新增 width 参数列数自适应）+ theme 滚动条。
- 接线：`MainMenuController.Awake` 给 MenuWindow 挂拖动带；`OnShowcaseClicked` 改开
  `DebugMenuHost.Toggle`（不再切场景；UIShowcase 场景保留作编辑器入口）。

**验证**：harness All 1244/10/1 基线一致、Runtime 0 错；batchmode/遥控重装配后
MainMenu 第五钮「调试场景」实拍落位（按钮列 6 文字簇 = 标题+五钮）。
**待创始人实机走查**：Play MainMenu → 调试场景 → 四面板交互（拖动/下拉/输入/气泡）。

## 三之五、自查修复波 + 复刻承载体系重构：xml 通用装载器（2026-09-26，创始人令「以库为源·原封复刻」）

**起因**：创始人走查四面板报「一堆各种各样的Bug」并质问「为啥照参考图做而不是把库里
的场景找出来复刻（图片也用原图）」「小界面几百行、没公共代码？架构这么烂？」
「架构级修改不策划直接上？」。回应：静态审计 + 规划审批后落了三件事。

**裁决与口径（后续复刻工作的依据）**：
- **以库为源**：界面复刻自 `external/aseprite-ref` 的声明（`data/widgets/*.xml` +
  `data/strings/en.ini` + theme.xml 件/样式），不照截图目测。
- **图标就是件**：theme `<parts>` 表里 icon_* 与窗框/按钮同表（如 icon_rgb=x0 y256
  16×16），随 345 件迁移已入库，复刻直取图集原图不乘色。
- **承载 = 通用 xml 装载器**（创始人三选一裁决）：布局数字直接来自声明文件。
- **控件层 = Stick 控件库**（代码控件类 + 工厂，生产/调试同源）：Unity 2022 LTS 行业
  主流是 UGUI + 可复用件 + 轻量工厂（prefab 承载像素约束存不住——顶点对齐/字体档/
  禁乘色必须代码强制）；UI Toolkit 运行时 2023 才趋稳，不采。

**交付（四个原子提交）**：
1. `5b0020f5` fix(ui) 自查修复波：拖动带被 VerticalLayoutGroup 吞（主菜单拖不动，豁免
   布局）｜滑条浅字首帧零宽卡死（几何重排移出 percent 早退）｜部件陈列廊窗越界 100px
   且盖启动器｜三面板默认位互叠｜tooltip 孤儿（窗口关闭不清）｜子菜单弹向行下方（改
   右侧弹）｜弹层夹取按宿主窗宽｜列表行 1px 缝｜菜单估宽截字｜File→New… 真弹窗；
   新增公共件工厂 `AseWidgetKit`（SunkenEntry/CheckRow/ComboBox/HoverFace）与控件库
   `SketchButtonSetIcon`（buttonset_item_text_top_icon_bottom 变体）。
2. feat(ui) 资产：61 个 widgets xml + en.ini → `Assets/Resources/AseWidgets/`
   （原封拷贝，en.ini 落 en.ini.txt）；幂等同步工具
   `Assets/Editor/AseWidgetAssetSync.cs`（菜单 PirateCrew/同步 Aseprite widgets 资产，
   meta 只在缺失时生成、GUID 稳定）。
3. feat(ui) `Assets/Scripts/UI/Debug/AseDialogLoader.cs`：System.Xml 解析 + 两遍布局
   （自然尺寸→定宽落位），`@.key`/`@general.x` 文案引用、`&` 助记符剥除；手写
   NewSpriteDialog.cs 退役删除；DebugMenuHost 增「库对话框 Duplicate/Goto」验证钮。
4. 本 docs 提交。

**装载器覆盖表（未实现语义遇则 LogWarning 跳过，逐条补）**：
- 已实现：window(text/help→?钮)｜box/vbox/hbox(vertical/expansive 空位/homogeneous/
  cell_align=right)｜grid(columns=2，首列=最宽标签)｜separator(text=蓝字点线/无线)｜
  label｜entry/expr(suffix=框内后缀/IntegerNumber)｜buttonset(columns，互斥单选，
  icon 项走 SketchButtonSetIcon)｜item(text/icon)｜check｜combobox+listitem｜
  button(text/minwidth/closewindow)。
- 未实现（登记待补）：magnet 焦点序｜maxsize｜cell_align=horizontal（字段拉伸）｜
  style 属性细粒度映射（现按「有无 icon」二分，icon_rgb 等走图文变体）｜窗高动态
  （advanced 盒显隐不改窗高，留占位）｜link/tooltip/其他控件型。

**已知偏差（待创始人裁决）**：字用本端 FusionPixel 位图档而非 Aseprite Mini 原字体
（单字体纪律；若要原味需引入 aseprite 自带字体位图并进烘焙管线——提案/待定）。

**验证**：harness Runtime 0 错 / Data 33/33；实机走查待创始人（编辑器 Play MainMenu →
调试场景 →「新建精灵（xml 装载）」「库对话框 Duplicate/Goto」）。取证通道：
`capture:MainMenu+dbg-widget|dbg-newsprite|dbg-menubar|dbg-parts`（遥控旗标）或编辑器
关闭时 `-executeMethod PirateCrew.EditorTools.UiPixelScreenCapture.CaptureDebugPanels`
（GUI 编辑器自动四连采 + 自退；编辑器开着时该方法进不去——Library 锁）。

**教训（写进流程）**：①横盒/点锚上误用拉伸锚 inset 的 Bug 家族（上波）之后，本波又
抓「布局组吞拖动带」——给调试件挂子物体前先问父件有没有 LayoutGroup，有则
`UiLayout.Ignore`。②遥控 stop 会打断创始人的现场 Play——遥控通道只在创始人不在场时
用；在场就直说「请你看」。③架构级改动先进规划模式过审批，不再边写边定。

**走查二波（2026-09-26，创始人报「悬浮/滑动/下拉/按钮延迟/拖动受限/窗口不置顶/组件全飞」
并质问是否逐行级复刻）**——承认：装载器首版是语义近似非移植。本轮：
- **交互六修**：①窗体按下即置顶（WindowDragger 增 IPointerDownHandler——最后点击的窗
  永远浮顶）；②拖动自由（只保标题带 16px 可抓，可推到大半出屏——废「留 3/4 在屏」
  自造限制）；③组合框弹层宿主改调试根 overlay（原 parent.parent 会把弹层夹进窗内/被
  别窗盖住——new_sprite 的比例下拉即受害者）；④列表选中行锁定悬停换色（HoverFace.Locked
  ——选中金底不再被「移开恢复常态」覆写）；⑤SliderValueLabel 加 DefaultExecutionOrder(1000)
  （与 Slider 的分数锚点不再来回拉锯）；⑥**首开卡顿**=一次性同步构建（陈列廊 345 格≈
  1400 物件同帧生成）——DebugBuildQueue：闭包延一帧 + 陈列廊按家族面板分帧（每帧 24 步，
  content 高度随建随长）。
- **装载器布局引擎重写为逐行移植**（回应「逐行级复刻排版和显示的代码逻辑」）：
  AwNode 树移植 ui::Widget 布局域——Box::onSizeHint/onResize（box.cpp:33-165：homogeneous
  取最大×n/等分末件吃余数、expansive 摊余宽、**跨轴子件拉伸到盒宽夹 [min,max]**——
  首版缺这层就是「组件全飞」的根）、Grid 条带/扩展/对齐（grid.cpp:160-420：
  cell_align=horizontal 记扩展列分余宽、right 格内右对齐、无对齐位整格拉伸）、
  Entry::onSizeHint（entry.cpp:479-491 字符宽公式）、Separator（separator.cpp:35-60）、
  控件边框取 theme 背景件九宫切片（本地图集 Sprite.border）。goto_frame 的
  cell_align=right 在盒内按源码语义忽略（只有 grid 消费该标志），覆盖表登记。
- 遗留近似（覆盖表）：check 行 hint（源未覆写 onSizeHint，按图标 8+4+文字）；combobox
  定宽 150；窗框含本端标题带。提交：`fix(ui): 走查二波六修+装载器布局引擎重写`。

**走查三波（2026-09-26 晚，创始人报「主菜单拖到中线卡住/启动器要点两下/新页面在主菜单后面/底部三件错」）**：
①主菜单是**中心锚**窗（0.5,0.5），拖动夹取按左上锚语义写——坐标系牛头不对马嘴，那条
「限制线」就是画布中线；拖动改为「左上角画布位 + 任意点锚换算回 anchoredPosition」通用算法。
②启动器 Build 完即激活，首次点调试场景反而把它藏了（要点两下）——Build 尾 SetActive(false)。
③置顶口径统一为**画布级**（沿父链提到画布直属根）：点主菜单只提主菜单、点/开面板提调试树根，
新开页面永远盖过主菜单（旧版窗内置顶，主菜单一被点就压过整棵调试树）。④底部三件实为三真 Bug：
组合框 Value 标签点锚配 offset 得**负宽**（树转储实锄 -20x0）→ 拉伸锚；advanced 盒隐藏了但
**高度仍占位**（底部空 36px）→ Result.SetHidden/Reflow（隐藏即重排收窗高）；输入框无默认值
（xml 不带，cmd 层职责）→ LoadNewSprite 设 32/32。⑤取证链两处自坑登记：DebugOpenButton 名字
与启动器标签不同步（三轮取证静默扑空——**改启动器标签必须同步取证映射**）；中间坏版本被
编辑器编译失败卡住、dll 假更新（判定「编译落地」必须同时看**错误计数没涨**）。验证：终拍两张
（dbg-newsprite/dbg-widget）+ 运行时树转储逐行核过（Spacer 63 + OK/Cancel 60+60 右对齐、
advanced 塌缩后按钮 -167）。提交 `aa60088e`。

## 三之六、四包并行移植波：覆盖率 24.2% → 76.4%（2026-09-26，创始人令「组成 Agent Team 拉覆盖率」）

**编排**：4 个 worktree（`temp/ui-menu|hover|layout|slider`）× 4 分支 × 4 代理，文件域互斥，
全部「以库为源」逐函数移植；协调者串行合并 + 真编译门 + 抽查对账 + 本节文档。

**覆盖率台账**（`temp/coverage2.py`，76 条语义块 / 4328 源行，函数级行数加权；代理报告 +
协调者四处逐行对账：`choose_side`/`ValueFromPointer`/`DistributeStripSize`/`openListBox`）：

| 状态 | 占比 | 说明 |
| --- | --- | --- |
| port 逐行移植 | **76.4%** | 上轮 24.2% |
| approx 近似 | 1.8% | View 滚动条（ScrollRect 替身，仅竖向） |
| subst 替代 | 2.7% | UGUI 捕获板/指针事件/BuildPopup 窗体 |
| miss 未做 | 19.0% | 见下偏差清单 |

**四包要点与创始人可感知的行为变化**：
- **菜单包**（AseMenuKit 重写，合并 `a7eb9690`）：下拉一层=栏项正下左对齐钳进画布、二层=
  行左上 3px + choose_side 取交叠少侧（menu.cpp:115-146 / fit_bounds.cpp:110-157）；**冷态悬停
  不高亮**（要有菜单展开后才悬停切换——was_clicked 门）；按下开合、**松开执行**；行内悬停
  250ms 展子菜单；点弹层边框=收全部、点栏空白=收全部、点分隔线=不动。行几何纠偏：文字 x=11、
  勾选 x=2、快捷键右让 6、子菜单箭头改源码三段竖线（弃 9×8 combobox_arrow_right 件）。
- **悬浮/列表/下拉包**（合并 `fffd9102`，新增 AseListbox.cs/AseComboBox.cs）：**列表行不再
  悬停变暗**（theme 的 list_item 无 mouse 层——旧版是自发明）；复选行悬停面只覆盖件本身；
  组合框=金底选中列表贴正下方、**再点可收**、点外收且这一击穿透到下层控件、越画布底翻上方、
  Esc/回车/空格收；词条面换 combobox 件 sunken2、开弹层时显焦点框。
- **布局包**（AseDialogLoader 重写核心，合并 `d58bafdf`）：grid 全 span/cell_align 路径、
  buttonset 改 Grid 装配、box childSpacing 0→4、check/entry/separator 高度公式化——**视觉比
  旧版更松/更高，属预期**（公式来自源，旧版是错紧）。
- **滑条包**（新增 SketchSlider.cs，合并 `b06f7103`）：整数取值、点击即跳变跟手、整槽件+
  裁剪分区（修假圆端盖）、双色值文本整数居中（修奇数屏像素）、键盘/滚轮/键缓冲；画廊已接
  （`WidgetGalleryPanel.BuildSlider` 撤 77 行补丁链）。**设置面板第二条滑条使用点未迁移**（见待办）。

**登记偏差（miss/approx 清单，拉下轮覆盖率的靶子）**：菜单/列表键盘导航、entry 编辑路径
（光标/选区/键入）、listbox 多选、kSetCursor、theme for_each_layer 通用层引擎（现逐件写死）、
View 滚动条横向、combobox onSizeHint 反推宽度（仍调用方给定）、style 级 min/max 未解析、
gfx `x2()` 闭区间与 Unity `xMax` 的 1px 口径差、字体度量替身（行盒常量 LineH=8/文本估宽）。

**harness 假绿灯事故（新会话必读）**：worktree 编译门命令
`run.sh Runtime -p:ProjectRoot=<worktree>` 的 `-p:` 参数被 run.sh 当第二位置参数（FILTER）
**静默丢弃**，实际编译主仓 →「0 错误」空转（本波两个代理被坑、协调者的可行性实测也是空转，
`#error` 探针证伪才暴露）。**已修 `external/harness/run.sh`**（external/ 不入库，重装需重打）：
`-p:` 直通 MSBuild + worktree 无 Library 时 ScriptAssemblies 自动回退主仓。A3/A4 两包当初
验的是假门，合并后真门各暴露 1/5 处编译伤已修（`8d59a3f2` RawMeasure 访问级、SketchSlider
去 sealed/补 `_rect`/`RectInt.width`）。

**合并链**：`d58bafdf`(layout) → `fffd9102`(hover) → `a7eb9690`(menu) → `b06f7103`(slider)，
跨包集成修正 `b3fc5cf7`（AseComboBox 关旧弹层改走 CloseAll）+ 画廊滑条胶水。全量体检
1244/10/1 与既有基线逐位一致（10 条为非 UI 域既有失败）。四个 temp/ worktree 已清理。

## 三之七、覆盖率第二轮：键盘导航/entry 编辑/View 滚动条三包（2026-09-27，创始人令「继续提高覆盖率」）

**编排**：3 代理 × worktree（`temp/ui-kbd|entry|scroll`）文件域互斥 + 协调者合并与四笔修正。
**覆盖率 port 76.4% → 93.5%**（台账 v3 `temp/coverage3.py`，92 条语义块 / 5821 源行；
subst 3.8%、miss 2.7%、approx 清零）。协调者对账七处逐行核过（choose_side / ValueFromPointer /
DistributeStripSize / openListBox / find_nextitem / GetCaretFromMouse / getScrollBarInfo）。

**三包交付**：
- **键盘导航包**（AseMenuKit/AseListbox，合并 `7cab08d5` 前序）：menu.cpp:611-795+1379-1477
  （Esc/箭头父子切换/Enter/Alt 助记符/cancelMenuLoop/find_next·previtem）与 listbox.cpp:263-328+
  397-438（Home/End/PageUp/advanceIndex/makeChildVisible/centerScroll）逐行移植；键盘泵走
  Update+GetKeyDown（SketchSlider 先例）。顺带修真缺陷：closeSubmenu 不清父层高亮
  （menu.cpp:1254-1290，上轮多清导致 Esc 后键盘接不回）。焦点模型替身：弹层链开着=最深弹层
  接管、挂在开着的组合框弹层下=列表接管（源 hasFocus 无对应物）。
- **entry 编辑包**（新增 AseEntry/AseIntEntry，AseWidgetKit.SunkenEntry 改走，签名不变）：
  entry.cpp 编辑路径全文（光标/选区/17 个编辑命令/词移动/双击选词/getCaretFromMouse 滚动分支/
  闪烁计时器）+ textcmd.cpp 键位表 + int_entry.cpp 全分支 + skin paintEntry/drawEntryCaret
  （光标宽按源 2px）。自建控件不用 TMP_InputField（其自带编辑语义会打架），显示层 TMP 只读
  标签+自绘选区/光标 quad。
- **View 滚动条包**（新增 AseView/AseScrollBar，组合框弹层撤 ScrollRect 近似）：
  view.cpp/viewport.cpp/scroll_helper.cpp/scroll_bar.cpp 滚动主体逐行移植；条宽 12
  （`<dim scrollbar_size>`，正好接上闲置令牌 AseLayout.ScrollbarSize）；横向条按 IfNeeded 补上；
  弹层高度钳位顺序修正（先钳视口再加边框——被钳时比旧版高 7px，滚动判定才对）。

**协调者四笔修正**（`181a7f37`/`aaa2ba07`/`c7090860`/`51307780`）：
1. 滑条滚轮符号：aseprite Windows 滚轮 `wheelDelta.y=-1` 与 UGUI `scrollDelta.y=+1` **反号**
   （state_with_wheel_behavior.cpp:285-290），滑条包直接代入错了号——修正为「上滚=增值」，
   与 View 同口径。
2. 装载器组合框宽度撤 150 硬编码，按 combobox.cpp:438-452 × entry.cpp:455-467 反推
   （max 选项宽 + 2×光标宽 + 词条边框 + 钮宽，夹 400）。
3. gfx `x2()=x+w-1` 闭区间口径补齐 7 处（menu 5 + combobox 2——钳制比 xMax 紧 1px）。
4. 滚动条拇指下限补减轨道件九宫边框（源 `scrollbar_size*2-border_width`，scrollbar_bg 切片
   5+5=10，漏减会把下限从 14 抬到 24）。

**跨包裁决记录**：
- pointerUp 收弹层疑虑 = **误报**：UGUI `ExecuteEvents.Execute` 沿父链冒泡找处理器，行自身
  不实现 IPointerUpHandler，抬起从行冒到弹层根正常收（且按行抬在外面也收=源 capture 语义）。
- style 级 min/max 解析 = **零影响收口**：theme 全表仅 16 个带 width/height 的样式
  （dir_item/pivot_dir/debugger_button 等编辑器专用件），装载的三件对话框一个用不到，不写死代码。

**登记偏差（新）**：焦点系统替身（见上）；助记符机制在位但栏上总闸=源 main_menu_bar 的 kNo
且现役标签无 `&` → 自然空转；IME/dead-key/右键编辑菜单无源对应；`was_clicked=false` 用
Input.anyKeyDown 近似（手柄键会误触）；preciseWheel 以非整档 delta 近似；entry 字符盒几何用
TMP `xAdvance` 累加近似源的 shaper charBounds；scrollbar static 抓取状态改实例字段。

**harness 并发竞态**（本轮新坑，已修 run.sh，external 不入库）：多代理并行跑同域时固定目录
`.run-<域>` 被对方 EXIT trap 互删（MSB1009 间歇报错，两代理被迫自建副本）。修法：锁目录
`mkdir .run-<域>.lock` 抢到用固定目录（保 --keep 增量），抢不到退 PID 目录。

**剩余 miss（2.7%）**：菜单滚轮滚动（797-800）、listbox 多选（76-110）、theme for_each_layer
层引擎（53-130）、scrollbar enter/leave invalidate（183-188）、slider kSetCursor（264-296）。
subst（3.8%）= UGUI 捕获板/BuildPopup/setMouse/onSetViewScroll 局部重绘→全量刷新，皆合理替代。

**合并链**：`7cab08d5`(kbd) → `dcd4b4c4`(entry 在前) 顺序实为 kbd→entry→scroll→四修正；
全量 1244/10/1 与基线逐位一致。三个 worktree 已清理。

## 三之八、覆盖率第三轮：层引擎/菜单滚动/窗体扩围（2026-09-27，创始人令「继续提高覆盖率」）

**编排**：3 代理 × worktree（`temp/ui-layer|menuscroll|window`）+ 协调者合并与两项修正。
**覆盖率 port 93.5% → 95.8%**（台账 v4 `temp/coverage4.py`，99 条 / 6047 源行）；
miss 157 → **34 行**（只剩 slider kSetCursor，裁决为**永久登记偏差**：OS 级水平缩放光标，
theme 无对应件、Unity 无内建，造素材=自发明——不造假货）；subst 3.7% 全为合理替代。

**x2 口径风波（重要教训，新会话必读）**：第二轮我凭记忆断定 gfx `x2()=x+w-1`（闭区间），
把 menu/combobox 七处钳制收紧了 1px；本轮菜单滚动代理从滚动条贴边行为提出反证。协调者
用检出源码的**像素级用法**裁决：`window.cpp:241/265` 命中测试 `x < cpos.x2()` 与
`x <= pos.x2()-1` 只有开区间才能让右边框命中区恰好含最外列像素，`theme.cpp:431` 平铺循环
同证——**`x2()=x+w` 开区间，与 Unity xMax 同口径**，七处已回退（`dcd…`→修正提交）。
教训入纪律：**判据必须来自检出源码的像素级用法（命中测试/画线循环），不能凭记忆的库约定**。

**三包交付**：
- **theme 层引擎包**（新增 AseThemeLayers.cs，`81a4d942`/`20fcbdbc`）：
  compare_layer_flags/for_each_layer 逐行移植 + theme.xml 入 Resources（AseWidgetAssetSync
  同步清单加条）；**消费者迁单一真源**（SketchButton 族/Check/Separator/Panel/AseWidgetKit/
  AseComboBox/AseView），迁移对账表逐位校验（真实 .cs 对真实 theme.xml 跑出）。三个状态
  差异登记：[F] button 键盘焦点字色 白→#C0C0C0（源 button 无 focus 文字层；工程无键盘导航
  故不可见，创始人可裁决回退）；[D] check_box 禁用面 透明→#2C2C30（SketchCheck 零实例化）；
  [B] combobox 箭头钮 Selected 态旧为 null（图形消失）→ 修为 buttonset_item_hot。
- **菜单滚动+列表多选包**（`d91c738b`/`88205ce9`）：scroll_window.cpp add_scrollbars +
  菜单滚轮（超高菜单出滚动条）；inBar 下拉**不再因超高上移**（menu.cpp:909 无上界钳，
  放不下交滚动）；listbox multiselect——**源只有 Ctrl/Cmd 无 Shift**（listbox.cpp:87），
  区间取反/快照重拍逐行。
- **窗体包（扩围入账）**（`95c1c6fb`）：window.cpp 适用子集逐行——变体/标题带几何/标题字
  origin 与右裁（右让 18→12、双钮 22）/关闭钮三态/onSizeHint/limitSize（新增窗最小 12×23）/
  limitPosition 的 titlebarH（拖动带 16→17）。native window/多显示器/resize 命中区等登记
  范围外。**手摆值纠错**：内容顶 21→**17**（旧值把标题带 15 与 inset 6 叠加错了）——全部
  调试面板内容整体上移 4px，属修对。

**协调者两项修正**：①x2 七处回退（见上）；②装载器窗高补顶部 inset（ContentTop+contentH+Pad，
治内容越窗底压下边框的既有缺陷，xml 对话框会高 17px、按钮不再骑边框）。

**登记偏差/遗留**：`UiKit.ApplyThemeButton`（域外，Skin/UiKit.cs:512-530）仍写死 button 四态，
下轮迁引擎；SketchSlider focus 态 y 偏移下轮迁；拖动钳制左右界 24/顶界 -6 与源 border=6/0
不同（登记保留）；标题 TMP Overflow 不真裁（源 limitTitleLabelBounds 是裁）；window.cpp
native/多显示器/resize 面 9 项范围外登记（DebugWindowKit.cs:49-59）；PixelSkin.ArrowDown/
Check/Radio/Scrollbar 包装器迁移后零引用待清（域外）。

**待创始人复测新增**：对话框窗高变高 17px（按钮不再压下边框）、全部调试窗内容上移 4px、
超高菜单出现滚动条+滚轮、列表 Ctrl 点选区间、标题带拖动区 17px。

## 三之九、创始人六诉静态取证 + 五修（2026-09-27，「你逗我呢」与「真的对齐了？」两连）

创始人复测六诉：悬停气泡不弹 / 组合框弹层消失（先「很远」后「没有」）/ 滚动内容溢出窗界 /
陈列廊内容偏上 / 新建选项切换出蓝描边 / 质疑覆盖率口径。静态穷尽取证结论与修复
（commit `dac0ae95`，harness Runtime 0 错 + Data 33/33）：

1. **组合框弹层消失 = 双重锚点**（AseComboBox.OpenListBox 旧 :202）：
   `anchoredPosition = (anchorX + left, anchorY - top)` 把锚参考点叠加了两遍——
   anchors/pivot=(0,1) 下 anchoredPosition 本就是「距宿主左缘/距顶缘」，再叠 anchorX/anchorY
   后实际落点 = 2×锚点 + 偏移，960×540 画布上整体飞出左上。**修**：`(left, -top)`；弹层宿主
   并轨 `AseMenuKit.FindOverlay`（源：combobox.cpp:615+652 openWindow 挂 Manager 顶层，
   本来就不是窗的子件）——菜单/组合框弹层从此同一架构，装载器也去掉显式 popupOverlay 传参。
2. **悬停气泡不弹 = 同类锚漏算**（WidgetGalleryPanel.HoverTooltip 旧 :389）：
   `InverseTransformPoint` 得到画布 pivot 局部坐标直接塞给锚点 (0.5,0) 的 anchoredPosition，
   垂直错 270px，按钮在画布下半区时整个出屏。**修**：换算成「距左缘/距顶缘」边距 +
   锚改 (0,1)；延迟 0.5s→0.3s 对齐源（tooltips.cpp:29 kDefaultTooltipDelayMsecs=300）。
3. **滚动溢出 = 滚动演示区 viewport 没挂 RectMask2D 也没告诉 ScrollRect viewport**
   （WidgetGalleryPanel.BuildScrollComboTip）：8 行×11 内容在 46px 视口外裸画。**修**：
   补 RectMask2D + scroll.viewport（源等价物 = drawable region 逐祖先求交，
   widget.cpp:926-932 + view.cpp:197-200）。部件陈列廊本身 mask 一直在（DebugMenuHost :230）。
4. **陈列格「没摆正/偏上」= 件图贴顶 2px**（PartsGalleryPage.Cell）：图区 36 高、16 高件
   18px 沉底。**修**：图区内垂直居中（取整）。
5. **buttonset「蓝描边」裁决 + 修**：PIL 逐像素实测主题件——焦点虚线环是**琥珀 #786050**
   （hot_focused/focused 两件同环，非蓝非灰）；全 buttonset 件表唯一蓝件是 pushed 面蓝
   #7C919D（theme.xml:1073 按住期间，源语义如此）；`buttonset_item_active` 是灰紫 #655961
   且全库无引用。源语义（theme.cpp:47-78 层引擎 + button_set.cpp:321-341 requestFocus）：
   切换后当前项 = selected+focus → **hot_focused（白面+琥珀虚线环，原版就有环）**；
   其余项 normal 无任何记号。我们的三处 FlagsFor 把 UGUI Selected 映射成**纯 Focus** 且丢
   业务位——当前项永远出不了 hot_focused。**修**：SketchButtonSet/SetIcon/Button 三处
   Selected → `Focus | (业务位 ? Selected : None)`。

**覆盖率口径答复**（创始人问「怎么算的」）：函数级台账（temp/coverage4.py，99 项按源码行数
加权，状态 port/approx/subst/miss，port 95.8%）——分母只有 aseprite C++ 行，**不含 Unity
胶水**（坐标换算/UGUI 事件映射/裁剪/装配），本轮 5 个 bug 有 4 个恰在胶水层：覆盖率量搬运
量、不量画面正确性。教训进长期库：**坐标系换算（pivot 局部 ↔ 锚参考系）是 Unity 侧胶水，
不在覆盖率分母里，必须靠拍屏取证收口**。

**蓝描边遗留一口**：若创始人仍见**常驻**蓝（非按住瞬间），静态穷尽已证明代码无此路径
（唯一蓝件 pushed 仅 Pressed 态触发，层表两库逐行一致）——需一张截图定位。
**待复测**：气泡 0.3s 弹出、组合框点开选项列表在正下方、滚动演示区内容裁进 sunken 边框内、
陈列格件图居中、新建对话框切换后当前项白面+琥珀虚线环（原版同款）。
探针（commit `7b41ef5e`）已含气泡悬停样本 + dbg-probe 延时拍帧，修复后取证用。

## 三之十、紧致化重构波：四单点内核 + AseButtonBase + 五波收敛（2026-09-27，创始人令「按 Unity 最佳实践翻译/紧致化/彻底提取公共操作/agent 迭代」）

**架构落地**（对应 §三之九 的四不变量对策）：
- `AseUi`（UI/Debug/AseUi.cs）四单点：`OverlayOf`（弹层宿主=画布根）、`EdgesOf`/`PlaceByEdges`
  （世界→宿主边距唯一换算；双重锚点/锚漏算两案的病根收敛为一处实现）、`SetPart`/`SetRawPart`
  （件挂皮；后者带 Tiled 参数给点状蚀刻线）、`ClipViewport`（视口裁剪必经门）。
- `AseButtonBase : Button`（Stick/Controls/）——`Active`/`StyleId`/`FlagsOf`/`InitAseSkin`/
  `ApplySkin` 公共化；三按钮类删 313 行。**UGUI SelectionState 是 protected，状态映射只能
  住 Selectable 子类**——外部静态类放不了（CS0122 实证）。
- 五波 agent（文件域互斥、各自 worktree、harness 真门）：W1 View族 -11 / W2 AseMenuKit -47
  （FindOverlay 退役）/ W3 装载器族 -31（popupOverlay 退役）/ W4 Stick族 -138 /
  W5 面板族 -70——**合计净 -297 行**。
- 协调者收口两笔：tooltip 半宽修正（见下）+ 组合框弹层宿主统一 OverlayOf
  （W3 登记：嵌套在 vbox/grid 里的装载器组合框，旧 `parent.parent` 会把弹层挂进窗内）。

**验证闭环（本轮核心基建，后续每波照用）**：`temp/ase-refactor` 集成 worktree + 独立验证
编辑器（CWD=工程目录，桥/flag 路径按 worktree 隔离）——基线七拍（MainMenu+六个 dbg overlay）
→ 合波 → refresh+recompile → 同机位重拍 → `external/ui-refactor-diff.py` 逐像素 diff。
**结果：六张逐位全同（0/2073600）；dbg-probe 唯一差异带 = 气泡水平半宽修正**——树转储
铁证（基线气泡文本 x=487.5 相位 0.5px，修后 x=391 相位 0），系 §三之九 五修里我自己埋的
半宽偏移（转左上锚时忘减半宽），W5 忠实保留、收口时真居中。基线存 `external/refactor-baseline/`。

**流水线教训（三条，均已在本轮踩实）**：
1. **新文件必须先 `refresh` 再 `recompile`**——RequestScriptCompilation 只重编已导入源，
   新增 AseButtonBase.cs 不导入就编译=新旧混合 CS0246（harness 直编源码测不出这类）；
2. Bash 工作目录在调用间持久——worktree 曾因此建进 `pirate-crew/Temp/`（Unity 自清目录）
   而失踪；**一律绝对路径**；
3. 桥按内容去重：连续两发 recompile 第二发会被 `_lastCommand` 吞掉，中间插一发 refresh 换键。

**登记遗留（下轮候选）**：WindowDragger 拖动钳制 24/-6 vs 源 border=6/0（window.cpp:782-810）；
`_overlayW/_overlayH` workarea 快照过期隐患（W2 登记）；`EdgesOf` 仅左上角出口（右下角
两波仍手写同式）；带可见底色的视口须先挂 Image 再 ClipViewport（W5 口径）；
AseButtonBase.Active setter 冗余一次 DST（幂等，按现行行为保留）。

## 三之十一、新建精灵歪根治：幽灵宿主 + 坐标空间混用（2026-09-27，创始人问「为什么这些问题一个没修复」）

**认账**：§三之十 的 diff 证明的是「重构没改变行为」，不是「行为正确」——基线与重拍都带着
同一个布局缺陷，逐位全同恰说明缺陷被冻结。创始人点名的「新建精灵歪」从始至终没进过任何
修复名单。本轮用验证闭环（图析 + 装载器插桩 + 字体转储坐标 + 像素扫描）四层证据链根治。

**根因（两层，都在 AseDialogLoader）**：
1. **盒/格宿主从未落位**：BoxNode/GridNode 的 Layout 只摆子件、不摆宿主——宿主是
   `UiKit.CreateRect` 的 Unity 裸默认（拉伸锚 + sizeDelta(100,100) + 垂直居中），等于一个
   比窗口矮 100px 的幽灵矩形；子件锚在幽灵顶上，首行被压低 (窗口高−100)/2+17 = 67px
   （new_sprite 窗高 223 时算得 67，与图析/像素扫描分毫吻合）。**修**：Layout 开头
   `SetAnchored(Host, (0,1), (w,h), (x,-y))`（源：widget setBounds——盒的 bounds 就是
   它的真实矩形）。
2. **坐标空间混用**：只修 1 会暴露第二层——Layout(x,y) 全树传窗口绝对坐标，宿主却层层
   嵌套相对，嵌套盒（buttonset 格/OK 行 HBox）层层叠加宿主偏移（实测 OK 行跌出画布到
   y-up −216、advanced 隐盒内容可见）。**修**：逐层相对制——子件坐标从 Border 起算盒内
   相对（`mainPos = Border`），宿主落绝对位。
   验收（像素扫描 + 字体转储）：标题带→Size 首行 67px→**6px**（源期望 ~4±2）；三段行距
   与布局账本逐位吻合；OK/Cancel 回窗内贴底；advanced 隐藏生效。

**方法论沉淀（这轮新增的取证手段，后续照用）**：①图像分析走 CDN URL
（Read PNG → 拿 URL → `mcp__4_5v_mcp__analyze_image`），估计值只用于定位、结论必须落
像素扫描/树转储硬数；②装载器插桩（dlgdbg/boxdbg 一行 Debug.Log 打 mainPos/size/hint）
——布局类问题十分钟出逐子件账本，用完即拆；③**该编辑器 file watcher 只认文件创建事件**，
改动要吃进去必须「新建一个 dummy .cs → refresh」（改名/新建皆可）——§六 的 recompile
通道对「改文件」场景不完整，此为补全口径。

## 三之十二、图标内缩源口径 + 滚动组合收口 AseView（2026-09-27，创始人问「图标还是有点偏」+「滚动条和旁边的内容展示框是这么用的吗」）

**两问两判据（源）**：
1. **图文块内缩**：`buttonset_item` border=3/底5（theme.xml:1067），icon-bottom 变体
   padding-top 2 / padding-bottom 1（:1114）→ 文字顶缩进 = 3+2 = **5**、图标底缩进 =
   5+1 = **6**。此前实现在 −2/+1，边框与内距都没扣——图标被抬高 5px 贴着文字，即创始人
   的「还是有点偏」。修在 `SketchButtonSetIcon.Create`（label −5 / icon +6）。像素验收：
   红圈底缘距按钮底框恰 6 坎。
2. **滚动组合**：源里**不存在「框外独立条」**——`View` 自带 `view` 样式（border 3/顶4 +
   window_face 底 + sunken_normal 边框件，theme.xml:581-585），`setup_scrollbars`
   （scroll_helper.cpp:75-81）把条放在**框内右缘**（viewportArea.x2()）、宽
   `scrollbar_size`=12（theme.xml:12-13；件原生 16 宽九宫压缩），视口被条挤窄。
   三处手搓组合（16 宽条在框外留 4px 缝 / 自制 Track-Plate 件）全部是误用。

**修法（收口到已有移植件，不新造轮子）**：`AseView`/`AseScrollBar`（view.cpp +
scroll_bar.cpp 逐函数移植，组合框弹层一直在用）就是公共件——
- `AseWidgetKit.ScrollView(...)`（点锚整装）/ `PaintViewSkin(...)`（只贴两层皮）新工厂，
  组合框弹层的内联皮同步去重（dbg-probe 弹层区红差异 = 0，逐位等价）；
- 三处滚动点全改走它：画廊面板 ScrollDemo（`WidgetGalleryPanel.BuildScrollComboTip`）、
  部件陈列廊窗（`DebugMenuHost.BuildPartsGalleryWindow`，滚动区恢复整宽、条在框内吃 12）、
  全屏陈列页（`UiShowcaseBoot.BuildScroll`，workspace_view 同款组合）；
- 滚轮转发 `AseViewWheel` 从 content 移挂 **viewport 层**：空白接光面命中时冒泡链不经过
  content，挂本体漏空白区滚轮。

**验收**：七拍 diff——MainMenu/menubar 零差；dup 42802（既有有意修复，逐位复现）；
newsprite 93408（前值 +181 = 图标内缩）；widget 7252（ScrollDemo 新组合，轨道扫描证实
条 24px=12 坎贴框右内缘、框内 window_face 底）；parts 39%（陈列廊窗整面换组合，目检
通过）；probe = 前值 + ~5200 且红差异全部落在画廊面板带（弹层零差 → 去重等价）。

**流水线新坑（本会话实锤，比 §三之十一的口径更狠）**：watcher 会**中途死掉**——连
「新建文件」都不再触发导入，且 `CompileScripts: 3ms` 这种小数字会伪装成「编译过了」。
**真正的地面真相是 `Library/ScriptAssemblies/PirateCrew.UI.dll` 的 mtime**：本会话曾
出现 dll 停在 05:28、05:29 后的改动全部没进程序集（newsprite +181 变了而滚动零差的
矛盾就是这么来的）。可靠通道 = 命令桥 `refresh`（AssetDatabase.Refresh 强制扫，
日志可见 `Asset File Changes: changed=N` 与 60s+ 的真实编译段）。另：验证编辑器曾在
大 refresh 后自然退出（原因未取证），重开 GUI 实例即可（CWD=worktree 保证桥/旗标
路径隔离；`-logFile` 换新文件防追加混淆）。

## 三之十三、四诉四修：点穿/拖条拖窗/展示页错档/拍摄机跳帧（2026-09-27，创始人四连诉）

**① 透过前面的面板点到后面的面板**：根因 = `SketchPanel` 窗皮九宫格
`raycastTarget = false`（SketchPanel.cs:116）——窗体整个底面不接光，点前窗空白直穿
到被盖住的后窗控件；且穿过去的 pointerDown 冒泡到后窗根触发它 `RaiseToCanvasTop`，
「后面的面板置顶了」的错乱观感即此。**修**：窗皮接光（命中区 = 整个 bounds，
widget.cpp hitTest 语义），`AseWidgetKit.PaintViewSkin` 的 view 底面同修。

**② 拖滚动条 = 拖整个窗**：UGUI 拖动目标 = 按下命中对象沿父链**最近的 IDragHandler**
——`AseScrollBar` 只实现了指针接口没实现拖动接口，最近实现者是窗根 `WindowDragger`。
**修 a**：`AseScrollBar` 补 IBegin/IDrag/IEndDrag 空壳（阻断冒泡；实际拖动仍走源式
按下轮询，保留「点轨道=翻页不抓取」语义）。**修 b（① 引出的第四真 bug）**：窗皮接光
后按住窗体任意空白移动都会冒泡成整窗拖动——`WindowDragger` 加**拖动资格裁决**：
OnPointerDown 记录按下点是否落在标题带命中板（`RectangleContainsScreenPoint`），
不在带内则本次拖动系列全忽略（源 window.cpp：move 只认 titlebar 命中）。

**③ 展示页「图标偏移」+ 列宽错档**：`UiShowcaseBoot` 在 Start 同帧读 stretch rect——
`CanvasScaler` 的 scaleFactor 到自己的 Update 才应用（实测 Start 帧 rect=1920 屏幕像素
口径，稳定后 960 艺术像素口径），用它算 viewportW=1902 → 16 列铺进 942 宽视口
（后一半裁掉、横条误出）。**修**：建页延一帧（协程 yield null）等 scaler 稳定。
插桩验证：view.rect 960×540、viewport 942、content 贴顶零偏移、7 列无横条。

**④ 拍摄机 UIShowcase 停摆**：`UiPixelScreenCapture` 帧判定用 `Time.frameCount == 60`
精确等——首帧重载（345 格同步构建）会跳帧，状态机永等。**修**：`==` 全改 `>=`。

**「上边空白一节、往下拉一段才是正文」未能复现**：陈列廊窗顶部 +4 坎 = view 框顶厚
（源组合必然）；展示页当前实拍页头贴顶。已修的 ②/③ 都可能造成该观感（拖窗把窗拖高、
列宽错档把正文挤到视口外），若仍现需创始人指认具体面板与操作序列。

**验收（七拍 diff 全归因）**：MainMenu/menubar 零差；dup 42802、newsprite 93408 与
前轮逐位复现；parts 39% = 滚动组合；probe 29% = **「最后点击的窗浮顶」源语义生效**
（点击画廊窗组合框→窗浮顶，遮挡关系变化；实拍目检窗完整在位、弹层开着）；
widget 0.38% = ScrollDemo 组合 + 小量叠序。提交 `84aef7a3`/`05fbf19c`/`62755ba9`，
merge `5b3e1d44`。

## 四、遗留（非本波文件域）

- `BattleSceneLighting.EnsureMaterial` Shader.Find 报错（上一会话遗留，Battle 材质链）。
- 主仓老编辑器的 Fonts/URP 噪声改动已随 merge 丢弃（feat 分支的装配版场景为准）。
- 装配器运行时化 + 完整盒模型布局（执行案 §4 的"同批"项，本波手摆等价表达已按 border 相加修正）。

## 五、纪律（不变）

- 布局数字一律 `AseLayout.Px`（×1 恒等后 theme 数字即画布像素）；取色一律 `PixelSkin.Theme.*`。
- **直切件禁手改**：判据与 sheet.png 逐位比对，改盘上 PNG 或更新 sheet.png 后必须重跑烘焙。
- 新增部件 = `AseBakeParts` 加 part id（必须能在 theme.xml `<parts>` 找到行）+ 重烘焙。
- **件的"语义"以 theme 风格表的 state→part 映射为准**（2026-09-26 加）：库里存在某张件
  ≠ 那个状态就该用它。例：`buttonset_item_active` 是蓝面件，但当前选中值按风格表走
  `buttonset_item_hot`（无彩色）——参考图只作证伪，不作首因。
- **字号纪律（2026-09-26 加）**：显示字号**必须等于**字体原生档，且新建文字控件一律经
  `UiKit.ResolvePixelFont(字号, 传入字体)` 取档——**禁止** `label.font = 调用方给的族`
  再配一个别的字号（0.75 倍缩放就是"笔画时粗时细"）。件/图标一律按件自然尺寸画，
  禁 `Stretch` 进非整倍盒子。
- **像素对齐自检（改完 UI 必跑）**：① `python tools/ui-review/pixel_fidelity.py <png>`
  中间灰阶块应只剩件角零星像素；② 边缘相位审计——**所有可见边缘必须落偶数屏幕坐标**
  （1 画布格 = 2 屏幕像素），奇数条数应为 0；③ 按钮字笔画宽度直方图应全为偶数。
  三项任一不达标就是"没落在显示像素上"，别用肉眼判。

## 六、新会话恢复指引（打磨波）

**环境现状**（2026-09-25 审计波收口时更新）：
- 主仓编辑器**未开**（遥控通道 `export/unity-command.txt` 与 `external/editor-remote-play.flag`
  都休眠）——拍屏/装配前需先开编辑器；**launch 前先确认没有实例在跑**（多起实例会撞
  "工程已被打开"弹错误框），用 `Get-Process Unity | Select Id,StartTime,MainWindowTitle` 核对；
  遥控 flag：`external/editor-remote-play.flag`（`assemble` / `capture:<场景>[+settings|+confirm]` / `stop` / `rebake`）；
- 每次 `capture:` 会同时把**实机文本度量**（字体/原生档/scale/画布相位）追加到
  `export/unity-command-result.txt`——查"文字没落整格"先看这里，比截图快；
- 改 UI 后照 §五 的三项自检跑一遍（`tools/ui-review/pixel_fidelity.py` + 边缘相位审计 + 笔画宽度直方图）；
- 工作区有字体/URP/场景 asset 的自动触碰噪声（编辑器常开产物，非本波改动，提交时别带上）；
- worktree `temp/ase-x1`（分支 `feat/ase-x1-verify` 已 merge）保留作对照，物证：
  `temp/ase-x1/pirate-crew/export/ui-pixel-4c/` 六屏首拍；主仓同目录是重拍版。

**改代码后怎么让编辑器吃进去（2026-09-25 深夜会话已验证的干净通道）**：
1. 写 `export/unity-command.txt` 一行 `recompile`（`CommandBridge` 每 0.4s 轮询，
   结果落 `export/unity-command-result.txt`）——比"新建 Probe.cs 喂焦点"干净，不再往
   `Assets/Editor/` 里塞临时文件、也不用删；
2. **必须查 `%TEMP%\pc3d-main-gui.log` 确认新段落出现 `Tundra build success` +
   `Reloading assemblies` + `遥控监听已装载`，且 `error CS` 计数为 0**——
   "touch 已有文件"不触发编译（file watcher 只认创建事件），光看 `CompileScripts: 4.4ms`
   这种小数字会误判（真正编译在 Tundra 段，十几秒）；
3. 确认重载完成后再发 `assemble` / `capture:<场景>[+settings|+confirm]` flag。

**无头链**（编辑器关闭时/批量验证）：`rebake` =
`Unity.exe -batchmode -nographics -quit -projectPath <主仓>/pirate-crew -executeMethod
PirateCrew.EditorTools.BeveledPixelSpriteBuilder.BuildFromCommandLine`；
装配 `SceneSetup.BuildAll` / `ManagementSceneSetup.BuildAll`；
**batchmode 与 GUI 实例不能同工程并行**（Library 锁）；batchmode 异常退出会残留
`Temp/UnityLockfile`，确认进程已死可直接删。
无头验证台的 `Runtime` / `DataEditor` 两个域**不开 Unity 就能抓这两类文件的编译错误**
（秒级），改完先跑它；全量 `All` 域当前基线 = 通过 1244 / 失败 10 / 跳过 1（10 条都是
世界海图材质带、战斗装配契约、脚本资产卫生三个非 UI 域的既有失败，与 UI 改动无关——
本轮已用「git stash 掉 UI 改动再跑」逐条比对确认）。

**打磨波执行序**（按优先级）：
① ~~确认框改走 CreateModal~~ ✅；② ~~奇数尺寸/中心锚相位排查~~ ✅；
③ **MC 式密度档**（Unit 运行时化 + 设置面板档位 + 非整倍窗口取整留黑边）——做时用
`tools/ui-review/phase_check.py` 复查非倍率下的相位；④ **16px 字连锁重排**（与密度档
联动裁决）；⑤ 已烘未接屏件按场景接入；⑥ 打磨清单第 6 条两项残留（手绘窗皮退役 +
Battle 确认框正文宽度）。
每项做完走「改码 → harness `Runtime`/`DataEditor` 编译 → `recompile` → `assemble` →
`capture` → `phase_check.py` 相位复核」同一套。
