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

## 三、当前状态（交接时刻）

**已完成（编译 0 错误，未提交）**：
- 四档原生字体全链（FontAssetBuilder 4 spec+回退链、UiKit 阶梯解析 FontTiers、UiSkin.Font=16/12/10/8、MenuUiBuilder/TextSampleBuilder/UiSkinTests 同步、方舟16淘汰）
- 47 张 UI 贴图 1:1 下采样（PIL 每 3×3 取块心，无损）+ meta spriteBorder ÷3 + 全部 filterMode=Point
- Panel 直角件全链（PixelPiece.Panel、8×8 模板、烘制/图集/判据/SketchPanel+EnsurePanel 换皮、Shadow 层销毁）
- 低清画布栈三工厂 + Px 表 `Unit=1`（艺术像素）+ UiLayout/UiPadding 换算归一 + UiKit 模态内边距/条槽内缩归一 + PressOffset 待查（PixelSkin (1,-1) 未改，仍是 Unit 基——见"未完成"）
- 乱码双杀：伪粗禁用 + SketchButton.AddLabel 图集 Point 钉扎
- 按钮动态：RuntimeUiBuilder.LayoutRowContent 原生件自贴合 + UiKit.FitToLabel（宽高全贴合）+ 模态卡 ContentSizeFitter 纵向贴内容
- 字面量 ÷3 扫除：BattleHudBuilder（41 处）、MenuUiBuilder（12 处）、ManagementSceneSetup（10 处）、RuntimeUiBuilder、SceneSetup 主菜单（9 处）、两控制器 RowHeight 48→16、SettingsRowHeight 80→27

**未验证**：最后一发 `assemble` 旗标已写入但**未确认被消费**（编辑器后台停摆）。新会话第一步：点 Unity 窗口给焦点 → 等 ~1 分钟 → 验收四点：直角面板、乱码清零、2:1 比例、字重均匀。

**已知遗留（按优先序）**：
1. `PixelSkin.PressOffset`/`ShadowOffset` 仍是 `(Unit,-Unit)`（=2 画布 px）——低清栈下应为 `(1,-1)`。PressOffset 的消费点要 grep 确认语义。
2. 字面量扫尾：`UiShowcaseBoot` / `PixelShowcasePage`（A=PixelSkin.Unit 乘数，展示页非阻塞）/ `TextSampleBuilder` / `FontProbe` 未换算；SceneLoader overlay canvas 未切低清栈；`grep "Vector2([3-9][0-9]" ` 各装配器再扫一遍漏网。
3. **完整搬皮批**（源码参数已提取，见下）：标题栏带（window part h1=15 整条）、窗控钮（?/×）、复选框/单选（8×8 + 焦点九宫格 2/6/2）、蓝分组字 #6E9ADB、slider 焦点变体。新件 → 允许一次 rebake。
4. **装配器运行时化**（半天）：菜单/管理 UI 改场景加载时构建，assemble 退役；比例下拉（改 referenceResolution）接设置界面。
5. 提交：工作树一大批未提交（见 git status；含字体资产删除/新增、Panel 件、画布栈、字面量扫除）——验收后分批提交。
6. 迁移收尾：删 `F:\Unity\2022.3.62f1c1` 目录（创始人已确认）。
7. 杂项：EditorBuildSettings 被截成 3 场景（装配链会幂等写回 6 场景，提交前核验）；僵尸 Unity 进程 35528（36K，杀不掉，无害，锁文件已不在）。

## 四、踩坑与禁忌（血泪清单，违者返工）

1. **禁伪粗**（重影乱码元凶之一）；图集 Point 必须钉（Bilinear 渗邻字=另一元凶）。
2. **禁翻倍**（原生档纪律）；禁"硬凑字号"。
3. **烘/排不是调节旋钮**：烘=新增件；排=装配改动。创始人已定性"最后一遍"纪律。
4. 验证防假阳性：旧日志"遥控监听已装载"、旧四屏 PNG、`error CS` 裸匹配（.dag 假阳性）。
5. **批量字符串替换脚本改源码已被创始人点名禁止**——用编辑工具逐处改；曾出过占位符残留、注释粘连事故。
6. 双开保护：第二实例静默退出（日志 "Exiting without the bug reporter"）；僵尸进程 35528 taskkill 拒绝访问，无视即可（锁文件已删）。
7. batchmode 铁律照旧（-projectPath + -nographics，一次一个 Unity）。
8. 遥控桥 fonts 后必须等域重编译（≥30s）再发后续命令，否则跑到旧代码。
9. Harness 的 `external/harness/Harness.csproj` 路径已迁 `F:\Unity\2022.3.62f1`；CoreModule 的 MSB3245 是良性（见上）。

## 五、恢复指引

1. 读本文档 + `docs/项目/交接/活跃任务登记.md` 对应行。
2. `git status` 看未提交批次；`git log --oneline -5` 看最近提交。
3. 点 Unity 窗口 → 确认 assemble 消费（日志尾部"三场景已重装配"）→ 验收四点（直角/乱码/比例/字重）。
4. 按优先序推进遗留清单；每完成一项提交一次（`类型(模块): 描述` 中文格式）。
