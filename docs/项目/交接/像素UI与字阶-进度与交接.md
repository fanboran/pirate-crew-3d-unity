# 像素 UI 与字阶 — 进度与交接

> 状态：进行中（创始人要求当天出可看版本用于简历投递）。本文档供任何新会话/AI 直接恢复上下文，先读完再动手。

## 一、验收口径（创始人裁决，全部有效，逐条遵守）

1. **字体 = 像素字体原生档**：字号只取像素字体的原生设计档——有什么字号做什么字号，**没有的档不硬凑、绝不放大**（12px 字体烘 24/36 = 翻倍，被否决多次）。现役四档（cmap 实测覆盖工程全部 UI 字符）：
   - **16 = 正格点黑16**（`ZhengGeDianHei16.ttf`，github.com/yzdnn/ZhengGeDianHei-16，OFL，简体 0 缺字）
   - **12 / 10 / 8 = 缝合像素**（`FusionPixel12/10/8-zh_hans.ttf`，github.com/TakWolf/fusion-pixel-font 2026.09.01，OFL；12 缺 1 字"毂"←10 兜、10 缺 2 字"胫舭"←8 兜，回退链已烘进资产）
   - 淘汰记录：方舟 16px（缺 1413 字含巨火扫）、寒蝉 16px（简体约 40%）
   - **位图口径**：samplingPointSize = 原生档、RASTER_HINTED、atlasPadding=0、图集 Point、材质 TextMeshPro/Bitmap。烘制入口 `FontAssetBuilder.ForceRebuildAll`（遥控桥命令 `fonts`）。
   - **禁伪粗**：TMP 合成加粗（FontStyles.Bold 的 multi-draw）在 12/16px 位图上=重影乱码，已全局禁用（SketchButton.ApplyVariant）。
   - **字体像素不锚定艺术像素**，字号是自己的体系。
2. **全局比例 2:1**：资产 1:1（47 张 UI 贴图已从 ×3 下采样回 1:1）、显示端整数 ×2。创始人明示这是"显示端问题"，终态 = 设置界面一个比例下拉。
3. **低清画布栈**（2026-09-24 深夜切换，创始人钦定）：画布 = 固定艺术分辨率 **960×540**（=1080p÷2），`ScaleWithScreenSize` 整数 ×2 缩放全屏。画布空间 1 单位 = 1 艺术像素，坐标天生整数。**为此已退役**：画布 pixelPerfect、UiPixelSnap 逐帧吸附、九宫格 pixelsPerUnitMultiplier 补偿（全部 =1）、UiLayout/Px 表的 ×Unit 换算。
4. **面板直角、按钮圆角**（Aseprite dark 主题语法，**源码在 `external/aseprite-ref/`**，dark 主题定义 `data/extensions/aseprite-theme/dark/theme.xml`——看它，别看截图猜）。已烘 `Pixel_Panel_*`（8×8 直角模板）。**对话框没有影子层**（源码 grep "shadow" 零命中，旧 Shadow 孩子已在 EnsurePanel 就地销毁）。
5. **按钮大小 = 文字大小**（动态）：行按钮走 HStack(controlWidths)+内层横排组自贴合；模态按钮 `UiKit.FitToLabel`（宽高全贴合，宽=TMP preferredWidth+8u）。
6. **权威配色**（theme.xml）：text #C0C0C0 / face #2C2C30 / selected #E1B85F / selected_text #41444A / 蓝分组字 #6E9ADB。

## 二、关键机制速查（新会话必读）

### 遥控桥（驱动开着的编辑器）
- 旗标文件 `external/editor-remote-play.flag`，写一行命令，编辑器内 `RemotePlayControl`（[InitializeOnLoad] 轮询）读后执行并删旗标。命令：`stop / diag / camdiag / fonts / assemble / rebake / capture:all / capture:<scene> / play:<...>`。
- **Unity 在后台时主循环停摆，旗标没人消费**——必须点一下 Unity 窗口给它焦点。
- 日志：编辑器以 `-logFile C:\Users\fanbo\AppData\Local\Temp\pc3d-intl2.log` 启动（当前实例）。

### 三条链的语义
- `fonts` = FontAssetBuilder.ForceRebuildAll（重建 4 档字体资产；也会触发 Refresh→域重编译——**常被用作"让编辑器吃新代码"的前置**，之后等 ~30-60s 再发下一条，否则跑到旧代码）。
- `rebake` = BeveledPixelSpriteBuilder.BuildAll（烘 UI 贴图）。**只有新增部件才烘**（内容生产），禁止拿烘当调整手段。
- `assemble` = SceneSetup + ManagementSceneSetup + BattleSceneSetup 的 BuildAll（空场景重建覆盖保存，菜单/管理 UI 烘进场景——历史架构，运行时化后退役）。

### 验证与编译
- 编辑器：`F:\Unity\2022.3.62f1\Editor\Unity.exe`（国服 c1 已卸载；ProjectVersion.txt 已改 `2022.3.62f1 (4af31df58517)`；AGENTS.md/交接指南路径已迁）。
- 无 UI 编译预检：`tools/headless/run.sh harness DataEditor`（秒级；MSB3245 CoreModule 警告是**已知良性组合，别"修"它**——指到真实路径反而双驱冲突 130 错）。
- 四画布工厂（均已切低清栈）：`BattleSceneSetup.CreateCanvas` / `SceneSetup.CreateCanvas` / `ManagementSceneSetup.CreateCanvas` + 运行时 `SceneLoader`（overlay，pixelPerfect=true 残留待清理）。
- **比例换档 = 改一个参数**：`PixelSkin.Unit`（现=2）+ `PixelartPilotScene.PixelScale`（现=2）+ 画布工厂 referenceResolution（已写成 1920/Unit, 1080/Unit 自动跟随）。设置下拉就接这里。

### 临时脚本（temp/，gitignored）
`verify-cycle.py`（冷启动全链）、`hot-cycle.py` / `ratio21-cycle.py` / `rebake-cycle.py` / `final-cycle.py`（热链变体：fonts→rebake→assemble→capture，事件驱动等日志完成行）。陷阱：`error CS` 要用 `(\d+,\d+): error CS` 位置匹配（.dag 路径假阳性）；旧日志/旧截图必须先删（假阳性秒过）。

## 三、当前状态（2026-09-24 晚会话收口）

**已提交（`51725ef6` + `7e8d29b3` + `cc62815d`，refactor/industrial-grade）**：
- 前批四档字体 / 2:1 画布栈 / 直角 Panel 件 / 字面量 ÷3 已在 `d07503e1`/`df87dd1c` 入库（当时交接档写"未提交"，后已提交）。
- **rebake 崩溃根除**（`51725ef6`）：`DrawOne` 按旧 36 源宽硬编码寻址模板件（28×32/16×16）越界；`MapAxis` 零中心除零守卫；接触表版面适配新 Plate 几何（colMin=10u/rowTone=12u）。
- **陈旧判据清零**（同上）：状态件色表按 Lifted/Sunk 取档（14 条误报）、Track/Panel 透明像素口径（14 条）、u 对齐判据解耦 3D 块（2 条）。**rebake 全绿：54 张 + 接触表 + 美术稿 + 图集，判据 0 条。**
- **SketchButton 删 `_GradientScale` 保序行**：fontMaterial 首访即实例化，该行只对 Bitmap 材质刷警告。
- **build 场景表修复**（`7e8d29b3`）：`d07503e1` 把 6 场景砍到 3，`SceneLoader.LoadSceneAsync` 按名加载会挂——补回 CrewManagement/LevelSelect（ToonPilot 已删不补）。
- **最终代码重装配 + 四屏重拍**（`cc62815d`）：12:47 版四屏跑的是 13:09 终版码**之前**的旧码；21:15 四屏才是终版产物。渲染器/Crew 材质/字体资产 = 装配链确定性输出同步。

**四点验收（21:15 四屏，分析工具过目）**：直角面板 ✓（按钮圆角=口径）、乱码清零 ✓、2:1 比例 ✓、字重均匀 ✓。**待创始人肉眼复核的观察项**：①主菜单「开始游戏」选中态文字贴边（边距紧）②CrewManagement 面板下半空白偏多 ③主菜单标题版本号与左下版本号是否一致（低清小字 OCR 两次读数不同，不可靠）。

**语义澄清（重要，别再绕弯）**：现行已提交语义 = **设计格 ×Unit(2) = 贴图像素 = 画布像素 1:1**（模板构建器、布局常量、实测屏三方自洽）。§一.3「画布 1 单位 = 1 艺术像素」的"艺术像素"实际指贴图像素；模板的"1 设计格"是 2 贴图像素。字体按原生档 1:1（16/12/10/8），与贴图体系独立（§一.1 口径）。**待创始人验收时定夺**：观感若 OK 就维持；若裁决"贴图回设计稿 1:1"，连锁改动 = 模板 ×Unit 落盘改 ×1（14×16/8×8、边框 4/2）+ PressOffset/ShadowOffset 改 (1,-1) + 接触表版面再适配——专项处理，别顺手改。

**PressOffset/ShadowOffset 裁决（原遗留#1 结案）**：维持 `(Unit,-Unit)`。已提交语义下 1 个按钮下沉量 = 1 设计格 = Unit 画布px，与最小视觉特征（2px）一致；(1,-1)=半个设计格，只适用于资产回 1:1 的口径。

**已知遗留（按优先序）**：
1. **创始人验收四屏 + Settings.png + confirm.png**（产物 `F:\VSCode\pirate-crew-3d-unity\pirate-crew\export\ui-pixel-4c\`，03:4x 版）+ 上面三个观察项 + ×2 语义定夺。
2. **搬皮第二批（接屏）**：已烘焙未接屏的件——tooltip（蓝底）、滚动条 bg/thumb、sunken 凹槽（列表/输入底）、组合框箭头、toolbutton；蓝字分组线（#6e9adb 分隔线+标签，等有分组语义的屏）；CrewManagement 主面板 / 选关面板上标题带（现在只有设置/暂停/确认三处）。
3. **装配器运行时化**（半天）：菜单/管理 UI 改场景加载时构建，assemble 退役；比例下拉（现在接 `PixelSkin.Unit` 一个真源，画布工厂 scaleFactor 直接跟随）接设置界面。
4. SceneLoader overlay 画布补 CanvasScaler×Unit（现在是裸 1:1，过渡遮罩全屏拉伸无碍，顺手统一）。
5. `SketchSeparator` 厚度轴 = PixelSkin.Unit（2 画布px）而分隔线纹理厚 2u（4px）——理论上半压缩，四屏观感未见异常，核对后要么改 2u 要么改纹理。
6. 删 `F:\Unity\2022.3.62f1c1` 目录（创始人已确认，未执行）。
7. Settings 截屏里滑条充盈接近 0 是截屏方法局限（直接 SetActive 不走控制器 RefreshSettingsControls）——不是 bug；真机打开设置会刷新到真实音量。

**2026-09-25 凌晨追加（创始人两项裁决的落地）**：
- **画布恒定像素密度（红警2 式，`ffcdeb0e`）**：三画布工厂 + 展示页改 `ConstantPixelSize×Unit`——1 画布单位恒 = 2 屏幕像素，画布逻辑尺寸 = 屏幕÷2 随分辨率生长（1440p→1280×720），任何分辨率整数倍。旧 `ScaleWithScreenSize` 固定参考 960×540 在 1440p 下系数 2.67 非整数（糊+错格）。
- **Aseprite dark 全部件搬皮第一批（同提交）**：theme.xml 逐件复刻 +33 件（7 tone 带标题窗体 13×24 切片 3/5/3/15、窗控钮 9×11 三态、×/?/▶/■/⊙ 图标 5×6、复选/单选 8×8、焦点框 2/6/2、sunken 4/4/4、滑条 5/6/5 四态、拇指 5×4、滚动条、tooltip 蓝底、组合框箭头）——**rebake 87 张全绿、判据 0 条**；`PixelSkin.Theme` 精确色表 + 全件取用器。接屏：设置面板 = 标题带窗体 + 金滑条 + SketchCheck 单选钮（提示文字 y=120→54 修掉插进画质行的 3:1 时代重叠）；暂停面板标题上带（隐藏≠取消暂停不挂 ×）；返回确认 = 标题带 + × 窗控钮。`Settings.png`/`confirm.png` 视觉过目全过。截屏机支持 `capture:<场景>+settings/+confirm` 后缀（第 60 帧激活隐藏弹窗；`GameObject.Find` 找不到未激活对象，走 FindObjectsByType Include）。
- **build 场景表反复被砍的根因拔除（`d61a1daa`）**：`BattleSceneSetup.RegisterBuildSettings` 硬编码三场景且在装配链最后执行，覆盖前两个装配器写的五场景表——7e8d29b3 的手工恢复每次 assemble 后都被打回。三处统一五场景表，行号锚定验证落盘 5。

## 四、踩坑与禁忌（血泪清单，违者返工）

1. **禁伪粗**（重影乱码元凶之一）；图集 Point 必须钉（Bilinear 渗邻字=另一元凶）。
2. **禁翻倍**（原生档纪律）；禁"硬凑字号"。
3. **烘/排不是调节旋钮**：烘=新增件；排=装配改动。创始人已定性"最后一遍"纪律。（例外已用掉一次：`cc62815d` 那轮 rebake 是为补齐 12:43 崩溃批的收尾+验证判据修复，非调节。）
4. 验证防假阳性：旧日志"遥控监听已装载"、旧四屏 PNG、`error CS` 裸匹配（.dag 假阳性）。
5. **批量字符串替换脚本改源码已被创始人点名禁止**——用编辑工具逐处改。
6. 双开保护：第二实例静默退出（日志 "Exiting without the bug reporter"）；僵尸进程 35528 taskkill 拒绝访问，无视即可。
7. batchmode 铁律照旧（-projectPath + -nographics，一次一个 Unity）。
8. 遥控桥 fonts 后必须等域重编译（≥30s）再发后续命令，否则跑到旧代码。
9. Harness 的 MSB3245 是良性；`external/harness/Harness.csproj` 路径已迁 `F:\Unity\2022.3.62f1`。
10. **【2026-09-24】Play 中途脚本重编译 = 幽灵 NRE**：可序列化字段（`_spawned`）跨域重载存活、非序列化引用（`_teams` 纯 C# 数组）清空 → 守卫失效每帧 NRE（BattleController/FxPool 同时中招）。**遥控工作流铁律：让编辑器吃新代码（refresh/编译）之前必须先 `stop`**。创始人手动按 Play 看效果时若后台有代码改动，同样会踩。
11. **【2026-09-24】两座桥的路径别混**：CommandBridge = **仓库根** `export/unity-command.txt`（refresh/state/call）；RemotePlayControl = `external/editor-remote-play.flag`（stop/fonts/rebake/assemble/capture/play）。
12. **【2026-09-24】焦点喂法**：编辑器后台时主循环停摆，每次焦点只换来 ~3 秒运行；忙任务（rebake/assemble/capture 全链）要用循环喂焦点。现成脚本 `temp/focus-unity.ps1`（gitignored，丢了照 §五 重建）。capture 四屏连拍期间帧也在走焦点——循环不能停。

## 五、恢复指引（新会话第一步做什么）

1. 读本文档 + `docs/项目/交接/活跃任务登记.md` 对应行。
2. `git log --oneline -6` 对齐提交；`git status` 应为净（不净=有并行会话，先对齐文件域）。
3. 按遗留清单优先序推进；每完成一项提交一次（`类型(模块): 描述` 中文格式）。
4. 驱动编辑器：改 C# 后 → 写 `refresh` 到仓库根 `export/unity-command.txt` → 跑 `powershell -File temp/focus-unity.ps1`（无则按 §四.12 重建：ShowWindow(SW_RESTORE)+SetForegroundWindow+驻留 2.4s）→ 等 ~40s 编译 → 遥控命令写 `external/editor-remote-play.flag` → 再喂焦点 → 查日志 `pc3d-intl2.log` 尾部确认消费。
5. 日志路径：`C:\Users\fanbo\AppData\Local\Temp\pc3d-intl2.log`（编辑器以 -logFile 启动）。
