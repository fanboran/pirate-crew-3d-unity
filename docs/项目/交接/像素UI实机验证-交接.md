# 交接：像素 UI 实机验证（2026-09-23 深夜收尾）

> **这是一份独立、自足的交接文档**：新会话读完本篇即可接手"像素 UI 实机化"这条线，
> 不需要去翻共享指南的两千行历史（共享指南顶部有指向本篇的指针）。
> 配套阅读顺序：[AGENTS.md](../../AGENTS.md)（项目铁律）→ 本篇 → [待办事项.md](待办事项.md) 的"步骤 4c"。
>
> **本线一句话**：把项目 UI 从手绘皮整体换成程序化生成的 Beveled Pixel 像素皮
> （u=3，1 艺术像素 = 1080p 下 3 屏幕像素，与 3D 渲染颗粒度 1:1），并做成
> 可交互的实机组件展示页；创始人多轮走查的排版/字体/乱码问题在本会话内收口。

---

## 一、当前状态（截至 2026-09-23 深夜，全部已提交到 refactor/industrial-grade）

### 已完成且已验证

| 事项 | 说明 | 提交 |
| --- | --- | --- |
| 条槽五段语法 | Track = `INK 描边 / 外环 / 斜面 / 内暗线 / 槽底`——面板族与条槽族**共用最外近黑描边**（"全家一张皮"签名；参照血条最外圈本来就是深近黑） | f365750 |
| 组件总表 | `components.png`（1920 宽）：面板 7 tone × 三态、凹槽真实血条用法、填充 5 色、语义件、页签、按钮；一件一行 + 左列行名垂直居中，行高/间隙全取令牌表 | f365750 |
| 按钮令牌 | **件高 = 件内文字高 + 12**（按钮 12+12=24、标题条 24+12=36）；**按钮宽 = 标签宽 + 24**；条高 24 不随字号走（几何定死 6 框+12 填+6 框） | 3f23c30 |
| 乱码根因修复 | 字体图集 `AtlasPadding` **0→4**（padding=0 时相邻字形格贴死渗色——这就是"乱码"）+ 图集 Point 过滤强制（`PixelAtlasPointFilter` 逐帧纠偏） | 4bf6f1c |
| 像素字体入库 | 缝合像素 Fusion Pixel 12px 比例版（OFL）+ `FontAssetBuilder` 位图口径档（sampling 12 / RASTER_HINTED / **padding 4** / Point 图集 / Bitmap 材质）→ `FusionPixel12-px`（Art + Resources 双份，缺字 fallback → 楷体） | 7e23bfc / 4bf6f1c |
| 实机组件展示页 | `UIShowcase.unity` + `PixelShowcasePage`（组件总表的运行时真件版，全页落在艺术像素栅格）+ `UiShowcaseBoot`（Canvas/滚动/返回主菜单）+ 整页包大对话框 | 7e23bfc / 894240c |
| 状态可读性 | 悬停 `HoverLift` 0.25→**0.5**；按压 = 高光对调 + `Ramp.Sunk` 沉半档；`PressOffset` (1,-1)→**(3,-3)**（1 艺术像素整格） | 894240c |
| 编辑模式门禁 | Unity EditMode **1308 条 / 1307 过 / 0 挂 / 1 跳过**（最后一次全量） | 894240c 前后 |

### 已修、等一次安静的 Play 复核（代码就绪）

第二实机轮（894240c）：滚动失灵（视口补 raycast 命中图）、题头行距（68 高，上边距收窄、题副空档 12）、
返回钮（156×36 → 令牌 **144×72**，文案"返回主菜单"）、整页包进大对话框（两遍法布局）。

### 明确没做（创始人主诉的"菜单排版"根因）

主菜单/选关/船员管理/HUD 等**实机界面**仍在用旧字号表（`UiSkin.Font` 18/30/24/16/15）与旧布局
——组件展示页是唯一按新令牌表建的页。逐屏对齐它 = 下方任务 1。

---

## 二、关键决策与理由（新会话别推翻了重想）

1. **u = 3**：640×360 低分 RT 最近邻放大回 1080p，1 艺术像素 = 3 屏幕像素。UI 与 3D 颗粒度
   1:1，判据 `CheckUnitAlignment` 双源锁死（`PixelartPilotScene.PixelScale` + URP 渲染器资产
   `renderHeightPixels`）。**编辑器 Game 视图不是 1080p 时（如 QHD 2560×1440），画布 Expand
   缩放 4/3——那一屏是 1 艺术像素 = 4 屏幕像素**（整数倍、锐利），属预览缩放差，不是 bug。
2. **两套边带语法**：面板/按钮 = 近黑描边 + 受光唇边 + 平脸；条槽 = 描边/外环/斜面/内暗线/槽底
   五段。别互相推广（历史上被创始人两次打回）。两族**最外 1u 都是近黑描边**（规则 0）。
3. **令牌表**（art px，基准 = 正文字号 12 = B）：正文 12 / 标题 24 / 条高 24 / 按钮高
   **24**（= 正文+12；36 是标题条的数）/ 按钮宽 = 标签宽+24 / 面板内边距 12 / 位点 12 /
   环 24 / 头像格 48 / 小地图 144 / 投影偏移 1u。
4. **像素字体显示字号必须是 12 的整数倍**（36 = 12 艺术像素、72 = 24），非整数倍会把一格拉宽。
5. **大字小字不同字体的方案**（未做，见任务 2）：候选 ark-pixel 16px（OFL，官方已归档该尺寸），
   引入时标题字塔要联动换算（24 艺术像素 = 72 画布 = 16×4.5 非整数——要么标题 token 改
   16 艺术像素，要么换 24px 设计字体）。

---

## 三、环境与工作方式（本会话沉淀的基建，别再走弯路）

### 编辑器常开 + 命令桥（2026-09-23 约定：不要为单次操作反复启动 Unity）

- 创始人会保持 Unity 编辑器开着；一切编辑器操作走**命令桥** `Assets/Editor/CommandBridge.cs`：
  往 `export/unity-command.txt` 写一行命令，结果追加 `export/unity-command-result.txt`。
  命令：`refresh`（触发资产导入+编译）/ `state`（isPlaying/compiling）/
  `call 命名空间.类型.方法`（无参 public static）/ `afterplay 类型.方法`（进 Play 第 95 帧执行，
  赶在截图机退出 Play 前）/ `list 类型`（列静态方法）/ `recompile` / `probe`（程序集真源排查）。
  命令**读后即清**（消费式），桥每 0.4s 轮询。
- **改完 C# 必须触发编译**：编辑器只在获得焦点时自动刷新——
  `powershell -File tools/ui-review/focus-unity.ps1`（把 Unity 主窗 SetForegroundWindow 制造焦点变化），
  等 30~40s 出现新的 `bridge hooked` 再发新命令。
- **实机截图**：`call UiShowcaseSceneSetup.StartCapture`（进 Play → frame>90 截图 → 自动退 Play →
  产物 `export/ui-pixel-4a/showcase-live.png`）。**发之前先 `state` 确认 isPlaying=False**。
- **编辑器窗口取证**（不抢焦点）：`tools/ui-review/capture-unity-window.ps1`
  （PrintWindow + PW_RENDERFULLCONTENT，会先还原最小化窗口）。
- **诊断探针件**（都在 Assets/Editor）：`FontProbe.DumpMetrics`（TextCore 字形度量转储——
  证明度量全对）、`FontProbeDumper.DumpSceneTexts`（实机文本对象倒排——证明对象不重复）+
  `FontAtlasDumper.Dump`（运行时图集导 PNG——证明图集干净；注意 Alpha8 要把 alpha 抄进 RGB）、
  `FontRenderProbe.Run`（编辑模式 RT 渲染一行样字，不进 Play 不抢焦点）。

### 已知坑（本会话全踩过）

1. **Bee 编译缓存卡死**：症状 = refresh ok 但新方法一直"不存在"、
   `Library/ScriptAssemblies/*.dll` mtime 不动 → 关编辑器、删 `Library/Bee`、重启。
   （本会话出现过一次；对**内容哈希相同**的 touch 无效，要真实内容变更或清缓存。）
2. **`[InitializeOnLoad]` 的 update 订阅随域重载清空**：状态机要放 `SessionState` +
   静态构造器重挂（CommandBridge / StartCapture 已处理）。
3. **进 Play 触发域重载**：play 后要用的排队逻辑用 `afterplay`（第 95 帧执行）。
4. **并行渲染线会抢 Play**：他们有 `RemotePlayControl`（`external/editor-remote-play.flag`，
   协议：场景名/play:关卡号/stop/camdiag）。发实机命令前先 `state` 确认 isPlaying=False；
   被切走了就等他们跑完再来，别对拍。
5. **unity-mcp**（MCP for Unity）已配置，但**会话启动时编辑器没开**则工具不挂载——
   编辑器开着时新起的会话可直接用 MCP 工具；否则用命令桥（等效）。
6. **无头验证台**（不启动 Unity，多 agent 并行时绕开 Library 锁）：
   `external/harness/run.sh <All|Data|Combat|DataEditor>`；手工复制 csproj 时必须带
   `-p:ProjectRoot`。注意 Assets/Art/Tests 不在任何无头域里，只能在 Unity 侧跑。

### 调色板边界（对 3D 线的承诺）

3D 帧级调色板不从色板 JSON 取色，但量化工具与 `ToonMaterialFactory` 的 UI 族映射会读 UI 槽位——
**UI 观感调整优先在生成器里做（派生/混合），不动 `pirate_palette.json` 的共享槽位**；确需改槽位先知会渲染线。

---

## 四、下一步任务分解（新会话按此推进，优先级 = 创始人主诉顺序）

### 任务 1：实机界面全局切"像素字体 + 令牌字号"（创始人主诉"菜单排版"的根治）

- `UiKit.RuntimeFont` 三档全换 `FusionPixel12-px`；
- `UiSkin.Font` 字号表改 12 的整数倍栅格（建议 Title 72 / Section 48 / Body 36 / Hint 24 /
  Tiny 24，画布像素）；
- **逐屏**过：MainMenu → LevelSelect → CrewManagement → Battle HUD；
- 每屏验收：按钮内文字上下居中（±1 屏幕像素）、无溢出、按钮 48×24（令牌）、血条 24、
  内边距 12；
- 注意：装配件文件名被并行线改过（M3UiBuilder → RuntimeUiBuilder 等），先 `git log` 对齐现状。

### 任务 2：大字字体引入（创始人："大字小字别用同一套"）

- 候选 ark-pixel 16px（OFL；官方 16px 已废弃并归档：pixel-font-studio/
  ark-pixel-font-16px-glyphs-archive，取 zh_hans TTF）；
- `FontAssetBuilder` 加档（sampling 16 / RASTER / **padding 4** / Point）；
- 标题字塔换算：24 艺术像素 = 72 画布 = 16×4.5 非整数——先决定标题 token 改 16 艺术像素
  （48 = 16×3）还是换 24px 设计字体（72 = 24×3），再动烘焙；
- 验收：标题在 1080p 下笔画均匀、无半格。

### 任务 3：实机 1:1 截图补拍

- 等并行线不在 Play：`state` → `call UiShowcaseSceneSetup.StartCapture` →
  把 `export/ui-pixel-4a/showcase-live.png` 覆盖 `docs/images/ui-pixel-ref/gen-showcase-live.png`
  （当前入库的是 PrintWindow 窗口取证图，非 1:1）。

### 任务 4：遗留复核项（4c 原清单）

`UI_*` 调色板槽定稿（5 个里 3 个提案态）、切角深度、顶边拆两档、凹槽底次生亮带、
填充亮沿偏黄、`Danger` 暗档冷紫、悬停档差实机可见度、薄条低于 `PlateMinRender` 两处例外、
徽章不按队色着色。

---

## 五、恢复顺序（新会话第一步做什么）

1. 读 [AGENTS.md](../../AGENTS.md) 注意事项 → 本篇 → [待办事项.md](待办事项.md) 的步骤 4c；
2. `git log --oneline -20` 扫一眼（本线提交：f365750 → 3f23c30 → 7e23bfc → 894240c → 4bf6f1c →
   7977934 → 本篇；渲染线：§46/§47 对应的提交穿插其中，勿动他们的 WIP）；
3. 确认 Unity 编辑器开着（没开就开一次；**启动后不要关**），命令桥 `state` 确认不在 Play；
4. 按任务 1 → 2 → 3 推进；每完成一步用命令桥/验证台收口，EditMode 门禁保持全绿。
