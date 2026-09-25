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
