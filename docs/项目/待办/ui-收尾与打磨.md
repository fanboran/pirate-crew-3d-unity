# UI 收尾与打磨

> UI 主体已全部落地并实机验收（Aseprite 全量复刻 port 95.8%、UiLayout 布局器、战斗 HUD 换装、像素字体四档）；剩的是打磨与收尾：像素密度可调档、16px 字阶连锁、已烘未接屏件、菜单三屏 prefab 重排、UI 类名规则清理、以及待创始人验收的四屏截图。

## 详情

### Aseprite 观感对齐·打磨波

**【Aseprite 观感对齐·打磨波】**（2026-09-25 全量复刻波代码全落地——
sheet.png 直切 108 件+×1 量纲+kind 退役+四态按钮+主菜单窗体化+像素对齐根治，
提交 `4d2dc0bf..9752f1fa`；实拍 ×2 下文字格半亮率 0%、横线相位全偶。
**打磨波第①②项已落地**（2026-09-25 深夜）：①确认框改走 `UiKit.CreateModal` 标准模态
（窗体皮/标题带/×/正文浅字，战斗侧标题一并 16→12）；②全屏中心锚奇数尺寸相位归整
（设置卡 427→426、行板 383→384、列表窗体 333→334，`tools/ui-review/phase_check.py`
实测边缘由奇转偶）；附带**标题与窗皮三处收口单轨**（`UiKit.EnsureTitleLabel` 唯一入口、
`SketchPanel.Titled` 改穿 sheet.png 直切窗皮、补两处漏挂 `PixelSnapText`）——
验收产物 `F:\VSCode\pirate-crew-3d-unity\pirate-crew\export\ui-pixel-4c\` 五屏）——
**余项按序**：~~③ MC 式像素密度可调档~~（**已落地** 2026-10，feat/ui-runtime-assembly：
`PixelScaleStore/Service` 真源单点 + 设置页「像素比例 2×/3×/4×/自动」四档 + 「分辨率循环锁定」，
槽 9 读改写；渲染 rig 经 `Core.PixelScaleState` 同档自适应）；
④ 16px 字体连锁重排（标题带/按钮边框整套标定，与密度档联动裁决）；
⑤ 已烘未接屏件按场景接入（页签/tooltip/滚动条/组合框/菜单）；
⑥ 残留两项：手绘 tone 族窗皮 `PixelSkin.Window` 已无调用点待退役、Battle 确认框
正文 rect 仍 100 宽（VBox 不控宽陷阱）；Battle HUD 屏相位未排查；
⑦ theme 色取用迁移（顺手档）：新代码取 theme 色改用 `AseThemeLayers.TryGetColor(id)`
逐步消化手抄 Color32 存量（22 处对拍测试已兜底，非急件）；~~StickTokens 遗留层~~
**已根除**（2026-10-03：删生成文件 + `UiSkin` 十底色成员与死取色桥，消费点同值落字面量
——审计 F-2「双皮肤并存」的旧侧就此清零）；微尾三件——~~`BattleTerrainView`
类头「瓦片地形」描述语~~（**已改**为「地形碰撞层」现役口径）、~~`场景.md`
一行已删 API 史实行~~（**已消**：复核该文已无此行）、`BeveledPixelSpriteBuilder.cs:1187`
注释（待 [ui-九宫格契约测试补建.md](ui-九宫格契约测试补建.md) 收官时改）。
**同轮创始人走查后加修「按钮三缺陷 + 一真缺陷」**：列表行内按钮高 24 > 行距 17 致相邻按钮
互相叠印 → 行内按钮高改 = min(行高, 令牌高)（行高即 theme button 原生 16）；
按钮内文字偏下 1 格（切口 h1=4/h3=6 字区偏上）→ 字盒底边多让 1 格，标题/行标签
Midline→Middle；船员管理/选关底部按钮手写坐标错位（整行中心 672 vs 画布 960）→
改 `CreateCenteredButtonRow` 居中一行（缝 = 左右切片相加 8）；
**新查出真缺陷**：按钮动作文案被写进不可见的影子层（`GetButtonLabel` 泛搜命中兄弟序 0
的影子）→ 选关列表「出战」按钮被挤成 6 格细条、船员已解锁行按钮无字 → 增
`SketchButton.Label/LabelOf` 显式出口，5 个调用点（含 `UiKit.FitToLabel` 两处）改走它。
**配色收敛**：theme.xml 82 条色原只搬 13 条，其余用隔壁 stick-world 的 StickTokens 调色板；
已换 全屏/相机底→theme desktop、文字→#C0C0C0 与两级平面灰、星级→selected/disabled 金与灭色、
设置行底→list_item 纯色 #41444A、分隔线→theme `separator_horz` 点状蚀刻件；
余 dim 遮罩（自家规范规定，口径=Aseprite 管组件配色排版、自家文档管交互语义）、
标题墨色描边、INFO/WARN/SUCCESS（库里无绿）、tone 族灰阶（本工程调色板槽位）待裁决。
**列表结构按 `list_item` 口径重做**：拆掉自创的行尾 44px 文字按钮（Aseprite 的行只有
「纯色三态面 + 一条左对齐文本 + 可选图标」，行内交互件只有 timeline_box 小图标开关），
行本体成命中区、状态不再复读成字、星标改行内右对齐图标。
**2D 原版调研整体归档**（2026-09-25 创始人裁决）：`docs/项目/归档/参考逆向/`，
UI 唯一权威 = Aseprite 参考库（已写进 AGENTS 导航表）。
**像素对齐根治（2026-09-26 凌晨·创始人走查「文字还是有粗有细/复选框滑条错位/分隔条穿模」）**：
真元凶 = **字体错档**——主菜单四钮与设置页四个选项块拿 16 原生档 `ZhengGeDianHei16`
按 12 号显示（scale 0.750），笔画在 1/2/3 屏像素间跳；`SketchButton.AddLabel` /
`SketchCheck.BuildLabel` 改走 `UiKit.ResolvePixelFont(字号, 族)` 就近取原生档（治一族）。
另三处：`PixelSnapText` 改**画布空间相位**取整（局部取整挡不住 rect 被父布局停半格）；
窗控钮图标禁拉伸（5×6 自然尺寸整数居中）；`SketchCheck` 图标 8×8（旧 `8*Unit`=16 压首字）。
设置面板按库收敛：滑条改 theme `slider` 族（库无拇指件，旧实现跨族取 `mini_slider_thumb`
且 11 高 < 件高 16 竖压糊边）→ 整条槽即拖拽区；行高 27→28（居中子件落整格）；
分组线从标签右缘后起铺（不再穿字）；删标题带下的自造蚀刻线（非库件）。
**实测**：五屏半格边缘占比 14.01%/3.57%/6.25% → **全 0.00%**，按钮字笔画宽度全偶，
中间灰阶只剩件角 8–16 px；harness All 1244/10/1255 与基线逐条一致。
判据入库 `tools/ui-review/pixel_fidelity.py` + 每次截图同拍实机文本度量
（`FontProbeDumper.DumpSceneTexts` → `export/unity-command-result.txt`）。
**已裁决（2026-09-25）**：Linear 下纯顶点色平涂 ±1 偏色（行板 #41444A 渲成 #404549）
**不修**——1/255 级偏差不值得动色彩空间也不必换贴图填充，工程维持 Linear；
`vertexColorAlwaysGammaSpace` 试过更错已回退。
**审计波收口（同日）**：theme 全量对表（345 parts/175 styles/82 colors，烘 111、
接线 37、未烘仅 13 件 Aseprite 编辑器工作区件；色值 13 常量全对）；
`paintSlider` 双色文本实锄（skin_theme.cpp:1756-1790）→ 滑条 Value 改双色裁剪层；
滑条 ColorTint→Transition.None；ArrowDown 态映射纠错；buttonset 标签按内容区取盒；
死分支/奇宽/Titled 参数等顺手清；aseprite 整库入 `external/aseprite-ref`@a2d18ca
（与入库真源 md5 一致）。仍待裁决：主菜单 × 窗控钮 / tab vs 左列表 / SketchCheck
退役 / 已烘未接 74 件的接线优先级。
**全量迁移 + 陈列廊波（同日创始人令「全部组件移过来做成运行时展示面板」）**：
烘焙白名单退役，theme parts **345 件全量入仓**（判据=逐位一致）；图集增
`asePartFamilies`（26 家族）；**运行时陈列廊** `PartsGalleryPage` 挂进 UIShowcase
（家族面板 × 原生尺寸 × id/九宫标注，数据驱动）。覆盖率终审：素材迁移 345/345=100%、
代码接线 49/345=14%、陈列展示 345/345=100%、style 语义复刻 25/175 全量 14%
（游戏域口径 25/111=23%）。素材终审：**按钮按下态 = button_selected 蓝面 + 白字**
（按下=selected+capture 状态位，button.cpp:168-175 + theme.cpp:69-73）；sheet 图标是
**换色不是乘色**。挂账七项见交接档 §三之三。
恢复指引与工程细节见 [Aseprite观感对齐](../交接/归档/Aseprite观感对齐.md)。
**调试场景系统 + 走查六修**（2026-09-25/26，`e80d62ed`/`bbfe85c6`）：主菜单第五钮
「调试场景」→ 可拖动启动器四面板（组件实摆/New Sprite/Aseprite 菜单栏/部件陈列廊）；
六修 = 拖动带负宽/陈列廊零视口/输入框负宽/滑块零尺寸/下拉两件套/列表悬停字灰。
**自查修复波 + 承载体系重构**（2026-09-26，创始人令「以库为源·原封复刻」）：
静态审计修 12+ 处（`5b0020f5`）；**通用 xml 装载器**落地——61 个 widgets 声明 +
en.ini 原封入 `Resources/AseWidgets/`，`AseDialogLoader`（box.cpp 语义子集）装载
new_sprite/duplicate_sprite/goto_frame 三件，手写 NewSpriteDialog 退役；公共件工厂
`AseWidgetKit` + `SketchButtonSetIcon` 进控件库。**待创始人 Play 复测**启动器五钮；
字体偏差（FusionPixel vs Aseprite Mini）待裁决；覆盖表/教训见交接档 §三之五。

**四包并行移植波（2026-09-26，覆盖率 port 24.2%→76.4%）**：Agent Team 4 代理 ×
worktree（menu/hover/layout/slider 文件域互斥）按库逐函数移植——AseMenuKit 重写
（fit_bounds/choose_side 弹层定位、was_clicked 悬停门、250ms 子菜单计时、按下开合松开执行）、
AseListbox/AseComboBox 新增（列表行去悬停、组合框金底列表、点外收且穿透、越底翻上）、
AseDialogLoader 布局引擎收口（grid span/cell_align 全路径、buttonset Grid 化、
box childSpacing 0→4）、SketchSlider 新增（整数取值/双色分区/键盘滚轮，画廊已接）；
合并链 d58bafdf→fffd9102→a7eb9690→b06f7103，harness All 1244/10/1 与基线一致；
台账/偏差清单/harness 假绿灯事故见交接档 §三之六。**余项接续**：
⑦ 设置面板滑条迁移 SketchSlider（MenuUiBuilder/MainMenuController 字段类型变更
→ 序列化断链，需重跑装配重接线）；⑨ theme for_each_layer 状态层引擎通用化（现逐件写死）。
**第三轮已落（2026-09-27 晚）**：⑨ theme for_each_layer 层引擎（AseThemeLayers 单一
真源+消费者迁移）；菜单超高滚动+滚轮；列表 Ctrl 多选；窗体 window.cpp 扩围入账。
**新挂**：⑬ UiKit.ApplyThemeButton 迁 AseThemeLayers（域外收口，退役 button 硬编码）；
⑭ SketchSlider focus 态 y 偏移迁引擎；⑮ PixelSkin 四个零引用包装器清理；
⑯ 拖动钳制界（左右 24/顶 -6）对齐源 border=6/0——需实机走查后定。
**已完成（2026-09-27 覆盖率第二轮）**：⑧ 菜单/列表键盘导航（menu/listbox.cpp 键路径逐行
移植）；⑩ combobox 宽度反推（style 级 min/max 经查零影响面收口——16 个带尺寸样式全是
编辑器专用件，三件对话框用不到）；⑪ View 竖横滚动条（AseView 移植替换 ScrollRect 近似）；
⑫ entry 编辑路径（AseEntry/AseIntEntry：光标/选区/17 命令/IntEntry 夹取，IME 无源对应
登记偏差）。**新挂条件项**：listbox onResize 行宽跟随归位到 AseListbox 自身（现由组合框
侧顶替，其它列表调用点无跟随——仅未来新增列表时需要）。

### Aseprite 全量对齐波·已裁决待执行

**【Aseprite 全量对齐波·已裁决待执行】**（交接档
[Aseprite观感对齐](../交接/归档/Aseprite观感对齐.md) §〇之一）——
**创始人 2026-09-25 深夜最终裁决：原封不动复刻参考库（theme.xml+sheet.png），
项目原 UI 设定（tone 铺色/kind 体系/×2 量纲/按压位移/投影/自创变体）全部覆盖**，
四悬案全关；验收标准唯一 = 与参考库一致；执行案五步（×1 重烘全表→令牌 ×1 化→
kind 退役四态映射→四屏重排+主菜单窗体化→与 sheet.png 并排验收）下一会话照单执行；
本会话先行落地（保留有效，7bf836a4）：管理/选关列表三件套+设置蓝字分组线（均按库）
+三处装配 bug 修复。

### 像素 UI 与字阶收口·待创始人验收

**【像素 UI 与字阶收口·待创始人验收】**（交接档
[像素UI与字阶-进度](../交接/归档/像素UI与字阶-进度.md)）——
四档原生字体 / 2:1 画布栈 / 直角 Panel 件已入库（df87dd1c+d07503e1）；09-24 晚：
rebake 崩溃与陈旧判据清零、终版重装配+四屏验收过（51725ef6/cc62815d）；
**09-25 凌晨（创始人两项裁决落地，ffcdeb0e/8a7fb195/d61a1daa）**：①画布改红警2 式
恒定像素密度（ConstantPixelSize×Unit——1440p→1280×720 随分辨率生长，整数倍无分数缩放）
②Aseprite dark 全部件搬皮第一批：theme.xml 逐件复刻 +33 件（带标题窗体/窗控钮/复选/
单选/滑条/滚动条/sunken/tooltip/箭头），rebake 87 张全绿；设置面板=标题带+金滑条+
单选钮、暂停标题上带、返回确认带 × 窗控钮；build 场景表反复被砍根因拔除。
待办：①**创始人验收** export/ui-pixel-4c 四屏 + Settings.png + confirm.png
+ 三观察项（主菜单选中钮贴边/管理界面下半空白/版本号一致性）
+ ×2 语义定夺（现行=设计格×2 进贴图，回 1:1 是专项；
审计证据：Px.Button=16 < 贴图切片和 20px 九宫格压缩变形——定夺时一并裁决令牌 ×2 方向）
②搬皮第二批接屏：~~滚动条~~（裁决跳过：无溢出）~~/sunken~~（已接 view）~~/蓝字分组线~~
（已接设置屏）/ tooltip / 组合框箭头 / 更多面板上标题带
（按钮 focused 内描边 #4069C2 / disabled 影子字在审计报告部件对比表，随接屏复刻）
——**完整剩余学习项清单见 [Aseprite观感对齐](../交接/归档/Aseprite观感对齐.md)**
（剩余：按钮聚焦态/禁用影子字/菜单反白、tooltip/组合框、引擎级、观感悬案四档
+ 哪里用什么速查 + 新会话操作速查；**列表三件套+分组线已接屏**见该档 §〇）
③~~装配器运行时化（assemble 退役）+ 比例下拉进设置~~（**已落地** 2026-10，feat/ui-runtime-assembly：三张菜单/管理屏改 `UiScreenBoot`+`UiScreenBuilder` 加载时自建，场景 = 相机+EventSystem+Boot 三个对象，三张 `Prefabs/UI/*Screen.prefab` 退役、折叠表只剩 Battle；比例下拉见打磨波③）
④SceneLoader overlay 补 CanvasScaler×Unit ⑤SketchSeparator 厚度核对

### UiLayout 流式布局器

**【布局器批次·待实机走查】UiLayout 流式布局器落地**（2026-09-24 已落 `refactor/industrial-grade`，
DataEditor 0 错误，随 Aseprite 皮批次同一轮四屏走查验收）——
① 新增 `Scripts/UI/Skin/UiLayout.cs`：UGUI LayoutGroup 族的像素纪律包装（布局器唯一入口）——
VBox/HStack/Grid（间距/内边距全 u 整数倍）+ Element 首选尺寸 + Flexible 弹性占位 + Ignore 豁免；
② 已迁移：武器格 → GridLayoutGroup（6 列，逐格坐标算式全删）、右列 → VBox（弹性占位顶开）、
暂停/结算/返回确认三卡与确认弹窗 → 流式居中块、设置六行 → 行容器 VBox（pitch 手算全删）；
③ **后续登记**：主菜单/选关/船员管理三屏是折叠 prefab（无代码装配者），内容迁移 UiLayout
需一次 prefab 重排 pass（随"菜单系布局走查"批次一并做）；HUD 屏锚件保持绝对定位是**设计**不是遗留。

### 战斗 HUD 换装·收尾件

**战斗 HUD 换装 Aseprite 观感——收尾件**（2026-09-28 换装波落地，`78db1a52..df4cd0d0`，
判据与提交链见 [Aseprite观感对齐](../交接/归档/Aseprite观感对齐.md) §三之十六；
后续观感迭代已并入 UI 系统重构线，见
[UI系统重构-进度](../交接/归档/UI系统重构-进度.md)）：
- [ ] **海图内容恢复**（换装裁决「内部内容先空着」的暂态回头路）：三处同改——
      `BattleHudBuilder.BuildMinimap` 重建 DotLayer 子树、`HudMinimapSceneSetup` 恢复层接线
      （现显式写 null）、`BattleMinimap` 撤 Start 的 dotLayer==null 休眠守卫；
      `BattleSceneWiringTests` 空窗断言同步改回 HasMinimapWiring。
      MinimapRules 规则层与无头测试未动，恢复即用；恢复时可按新窗体口径重设计点阵。
      ⚠ 海图 101–108 已裁决全删，本项去留待定（样板关是否要小地图）。
- [x] 创始人实机复测（2026-09-28 **验收成功**——换装与重构线 W2–W4 交付物一并验收：
      按钮逐态字色/禁用双层字/角标移除/设置卡新皮/三模态全过）。
- [x] **战斗 HUD 几何收敛为单一 zone 表**（**已落地，UI 重构 W1B**：
      `BattleHudZones` 21 常量单表，builder 67 处引用只消费；遗留派生量表达式化
      观察登记在重构交接档）。

### UI 脚本「类名 = 文件名、不嵌套」规则债

**UI 脚本不满足 Unity 的「类名 = 文件名、不嵌套」规则（10 处存量，已登记棘轮）**：
类型 `StickLayoutElement` / `StickLayoutGroup` / `StickContextAnchor` / `WobbledDotGraphic`
与宿主同文件不同名；`SketchSwitchGraphic` / `SketchToggleGraphic` / `StickHoverScale` /
`WindowDragHandle` / `ToastFader`（×2，`SketchWidgets` 与 `StickKit` **同名重复**）是嵌套类。
后果：这些组件**存不进 Prefab**、场景重载后还原不回来（`WavyLineGraphic` 就是同一类，
做场景 Prefab 化时才暴露、已修）。当前它们都不在任何场景里，故不阻塞；
清理由 `Tests/UI/ScriptAssetSerializabilityTests` 的 `KnownOffenders` 白名单逐条推进
（清一条删一行，白名单留僵尸项会被另一条测试抓）。
两个同名 `ToastFader` 谁留谁删属设计决定。
**⚠ 2026-09-28 全量 EditMode 实测该测试红**：DebugUi 组合框波（`61461929`）新增
12 条嵌套类违规（`AseComboBoxArrow` / `AseComboBoxPopup` / `AseListItem` /
`AseListbox` 文件名大小写等）未入白名单——先补白名单止红（或直接迁出顶级，
同 `AseWindowTitleBand` 案例，见 Aseprite观感对齐 §三之十六：嵌套类存 Prefab
必丢件，战斗折叠已实锤一次）。

### 翻新流程 UI 段（步骤 4a–4e）

- [x] 步骤 4a：**UI 九宫格生成器（Beveled Pixel）已落地**（2026-09-22）——
      `Assets/Editor/BeveledPixelSpriteBuilder.cs`、判据（原 `BeveledPixelSkinTests` 已随 tone 族
      退役删除，九宫格契约测试待补建 → [ui-九宫格契约测试补建.md](ui-九宫格契约测试补建.md)）、
      `ArtGate` ⑦.5 步；实现口径见
      [Beveled Pixel 九宫格规范](../../技术/资产管线/UI九宫格.md)。
      走查修掉两处（都已固化成判据）：① 三层带改**同心环**（外环 8 连通 1 段无端点）；
      ② 切角改**按整格切**（切角形状独立复算）
- [x] 步骤 4b：**全 UI 换装落地（不止一屏试点）**（2026-09-22，创始人放权重构）——
      生成器扩到 **47 张**（悬停态全 tone + 页签 Tab + 选人圈/焦点框/位点×2/分隔线×2/投影
      七件语义件），**u 从 2px 改 3px 与 3D 像素块 1:1 对齐**（640×360 RT 放大回 1080p，
      判据读 URP 渲染器资产反向锁）；新增运行时取用层 `PixelSkin`+图集
      `Resources/UI/PixelSkin.asset`（装配侧唯一入口）；战斗 HUD/主菜单/选关/船员管理/
      画廊/模态**全量换装**（按钮 SpriteSwap 三态 + Focus 环替代乘色 + 面板投影 + 沸腾退役
      + ColorBlock 退役），手绘皮肤四类死代码（SketchSkin/SketchBoil/Sketch9Slice/WavyLineGraphic）
      已删；四场景 + prefab 已重建。**EditMode 门禁 1307/1306/0 挂**。
      评审图：`docs/images/ui-pixel-ref/gen-showcase.png`（**实际屏幕像素主图**）/
      `gen-components.png`（组件总表）/ `gen-contact-sheet-2x.png`（带字模标注，2× 放大）。
      细账与遗留见交接指南 §39/§40/§43
- [x] 步骤 4b 修正轮：**两套语法归位**（创始人对照走查：“常态 7 色意义不明”+“和参照不一样”）——
      面板/按钮改工具对话框实测语法（近黑描边 + 受光侧唇边 + 平脸），金框语法只留条槽；
      UI 三槽改中性灰；铆钉去除；接触表加字模标注；美术稿改留白版式 + 出 1× 实际尺度版。
      判据同步（描边近黑/唇边换边），门禁 1307/1306/0。见交接 §40
- [x] 步骤 4b 二次修正轮：**条槽补上共用的近黑描边 + 组件总表重排 + 出图改名**（2026-09-22，
      创始人走查“凹槽怎么画风和别的不一样 / 为什么是 1× 的图 / 元素都偏到姥姥家了”）——
      条槽变五段（`INK` 描边 / 外环 / 斜面 / 内暗线 / 槽底），判据查“最外 1u 四边近黑”；
      组件总表按“一件一行 + 左列行名垂直居中”重排、行高间隙全取令牌表、画布高度按游标实算后裁、
      凹槽段改真实用法（生命/魔法/敌船条 + 一大内嵌槽，不再排 7 色）；
      出图去掉误导的 `-1x` 后缀（`showcase.png` / `components.png`，尺度印在图上）。
      门禁 1307/1306/0。见交接 §43
- [x] 步骤 4b 三次修正轮：**按钮比例归位**（2026-09-22，创始人第六轮走查“按钮大小也都太大了相比文字”）——
      令牌口径改成 **件高 = 件内文字高 + 12**（按钮 12+12 = 24、标题条 24+12 = 36）、
      **按钮宽 = 标签宽 + 24**（不硬撑满行）；showcase 与组件总表按新尺寸重排（见交接 §44）
- [x] 步骤 4b 四次修正轮：**状态可读性 + 页签归位 + 实机调试窗口 + 像素字体入库**（2026-09-22，
      创始人第七轮走查）——悬停上抬 0.5 / 按压对调 + `Ramp.Sunk` 沉半档 / 按压位移改整格 (3,-3)；
      页签零间隙并压宿主描边（运行时 `SketchWidgets.Tabs` 同步）；盖章文字按墨迹垂直居中；
      实机调试窗口 `UIShowcase.unity`（菜单 PirateCrew/UI/打开组件展示）+
      `PixelShowcasePage`（组件总表的运行时真件版，全页落在 3:1 艺术像素栅格）；
      缝合像素 12px 位图档入 `FontAssetBuilder` + `Assets/Art/Fonts/`（OFL 随行）。
      见交接 §45
- [x] 步骤 4c：**实机 UI 全局切像素字体 + 令牌字号 + 满精度阶梯**（2026-09-23，`122065e` v0.2.0）——
      四屏（主菜单/选关/船员管理/战斗 HUD）+ 设置/确认弹窗全部按令牌重排；
      字体链双端切 FusionPixel（编辑器 MenuUiBuilder 三属性 + 运行时 UiKit.RuntimeFont）；
      **满精度阶梯**（创始人祈使裁决“不同大小=不同精度的字体”）：FusionPixel 12px @36 正文、
      **ArkPixel 10px @30 小字**（新引入，OFL 随行）、`UiKit.ResolvePixelFont` 按字号在文本出口
      单点纠偏；`TextColorOn` 改“深底白字/浅底黑字”；ActionButton 撤图标（走查读成 emoji）；
      HUD 紧凑化（面板 246/格 60/按钮 48 高、右下提示条删除、左下文字钮）；
      版本号单一真源贯通到主菜单显示（Application.version）；EditMode 1344/1345
      （唯一红=折叠态_CrewManagement 的引擎覆盖判定，盘面三证干净，待 batchmode 权威复跑）
- [ ] 步骤 4d 遗留（**2026-09-25 清理波重写：字体项已解决/淘汰，仅剩复核类**）：
      ~~16px/24px 原生像素标题字体~~（**已解决**：现役 16 档 = 正格点黑16，ark-pixel 已淘汰，
      四档 16/12/10/8 落库——见交接档《像素UI与字阶》§一.1）；
      **武器图标重绘**（17 件静物在像素化管线下的放大读成 emoji，需 12×12/16×16 像素原生重绘，
      走 IconBakeTool 管线）；~~`UI_*` 调色板槽定稿（5 个里 3 个仍是提案态）~~（**已定稿落库**：
      三灰 + 9 新槽已落 `refactor/industrial-grade`，判据全绿）；
      复核项（切角深度 / 顶边拆两档 / 凹槽底次生亮带 / 填充亮沿偏黄 / `Danger` 暗档冷紫 /
      悬停档差实机可见度）+ 薄条两处低于 `PlateMinRender` 的例外（武器面板 HP 条 15px、头顶条
      世界空间）+ 折叠态_CrewManagement 测试的 batchmode 权威复跑；
      实机 1:1 截图归档（`docs/images/ui-pixel-ref/` 换 gen-showcase-live 等）
- [x] 步骤 4e：**动态 UI 像素比例（设置可调）**（**已落地** 2026-10，feat/ui-runtime-assembly）——
      设置页四档选项块（2×/3×/4×/自动；1× 与渲染侧相机契约域 [2,5] 冲突，不做），
      真源 `PixelScaleService`（槽 9 持久化 + sceneLoaded 重应用）；
      UI 画布 scaleFactor 与像素化渲染 rig（`Core.PixelScaleState` 自适应）同档联动；
      HUD zone 表是画布像素（CanvasScaler 缩放，不随档变，零改动）；「字号随档」不需要——
      恒定像素密度画布下整档等比，原生档纪律自然成立；
      附带：分辨率循环锁定（`VideoSettingsStore/Service`，播放器限定）；
      验收：实机切档三像素（世界块/UI 包边/字形）对齐待创始人走查（挂「用户实玩验收」）