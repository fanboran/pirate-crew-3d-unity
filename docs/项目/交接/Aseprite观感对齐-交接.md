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

## 四、遗留（非本波文件域）

- `BattleSceneLighting.EnsureMaterial` Shader.Find 报错（上一会话遗留，Battle 材质链）。
- 主仓老编辑器的 Fonts/URP 噪声改动已随 merge 丢弃（feat 分支的装配版场景为准）。
- 装配器运行时化 + 完整盒模型布局（执行案 §4 的"同批"项，本波手摆等价表达已按 border 相加修正）。

## 五、纪律（不变）

- 布局数字一律 `AseLayout.Px`（×1 恒等后 theme 数字即画布像素）；取色一律 `PixelSkin.Theme.*`。
- **直切件禁手改**：判据与 sheet.png 逐位比对，改盘上 PNG 或更新 sheet.png 后必须重跑烘焙。
- 新增部件 = `AseBakeParts` 加 part id（必须能在 theme.xml `<parts>` 找到行）+ 重烘焙。

## 六、新会话恢复指引（打磨波）

**环境现状**（2026-09-25 深夜）：
- 主仓编辑器**开着**（GUI 实例，最新代码 + 最新装配场景，PID 会变，用进程名找）；
  遥控 flag：`external/editor-remote-play.flag`（`assemble` / `capture:<场景>[+settings|+confirm]` / `stop` / `rebake`）；
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
