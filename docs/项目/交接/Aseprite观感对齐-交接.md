# Aseprite 观感对齐 — 交接（全量复刻波·代码全落地待实机重验收）

> 状态：**执行案 §〇之一 五步已全部落地**（2026-09-25 本会话），无头验证链全绿，
> 四屏实拍确认 ×1 直切观感生效；**剩余 = 重启编辑器后的实机重拍与创始人过目**。
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
**三条路线（提案/待定，非裁决不施工）**：
a) 容器等比 ×1.5（12px 字中文版主题：带 15→22、border 4/6→6/9，语法保留但数字全变，
   不再能与库逐位对照）；
b) 正文档降到 8px 中文（theme 数字原样全部成立，8px 简体中文可读性存疑——现有
   FusionPixel8 档 cmap 全过但笔画密集）；
c) 混合：列表/按钮用 8px，标题/横幅 12px 且容器单独加高。
裁决前维持现状（12px + 带内居中）。

## 四、遗留（非本波文件域）

- `BattleSceneLighting.EnsureMaterial` Shader.Find 报错（上一会话遗留，Battle 材质链）。
- 主仓老编辑器的 Fonts/URP 噪声改动已随 merge 丢弃（feat 分支的装配版场景为准）。
- 装配器运行时化 + 完整盒模型布局（执行案 §4 的"同批"项，本波手摆等价表达已按 border 相加修正）。

## 五、纪律（不变）

- 布局数字一律 `AseLayout.Px`（×1 恒等后 theme 数字即画布像素）；取色一律 `PixelSkin.Theme.*`。
- **直切件禁手改**：判据与 sheet.png 逐位比对，改盘上 PNG 或更新 sheet.png 后必须重跑烘焙。
- 新增部件 = `AseBakeParts` 加 part id（必须能在 theme.xml `<parts>` 找到行）+ 重烘焙。
