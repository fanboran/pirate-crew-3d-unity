# Aseprite 观感对齐 — 交接（剩余该学的）

> 状态：进行中。2026-09-25 凌晨，创始人连续三轮追问（画布整数倍 / 部件学全 / 布局系统学全）驱动的搬皮线。
> 本文只列**还没学的**；已学的见文末「已学清单」。权威参考库：`external/aseprite-ref/data/extensions/aseprite-theme/dark/theme.xml`（1177 行，**看源码别看截图猜**）。
> 配套：部件级逐件对比表在 [UI审计与重构-进度与交接](UI审计与重构-进度与交接.md)（并行审计线，含 Balatro/RACCOIN 实地档案）。

## 一、剩余该学的（按优先序）

### ① 状态语言——「同一件皮换状态」的完整语法（接屏时逐条复刻）

theme 的状态不是自创 tone 换皮，而是**换 part / 换色 / 叠层**三种手法的组合：

| 语义 | theme 口径（styles 段） | 我们现状 |
| --- | --- | --- |
| 列表选中行 | `listitem_selected_face #e1b85f` 整行金底 + `selected_text #41444a` 深字 | 自创 tone 换皮，未学 |
| 按钮聚焦态（第 4 态） | `button_focused` part（带亮边框变体，与 hover 不同） | 只有三态（常态/悬停/按压） |
| 禁用影子字 | button disabled = **双层文字**：`text color="background" x=1 y=1` 垫底 + `newlayer` + `text color="disabled"` 盖面 | 只做 CanvasGroup 压 alpha |
| 菜单项热/高亮 | hot = `menuitem_hot_face #2c2c30` + `hot_text #7d7d7d`；highlight（选中）= **反白** `#c0c0c0` 底 + `#2c2c30` 字 | 无菜单件 |
| 页签三件套 | `tab_normal/active` 九宫格 + `tab_bottom_active/normal` 底条 + `tab_filler` 2×12 填充；字 `tab_normal_text #7d7d7d` → `tab_active_text #c0c0c0`、`tab_active_face #333333`；高 17 宽 80（dimensions） | Tab 件还是旧 36 族分层画法，**整套没按 theme 学** |
| 工具钮四态 | `toolbutton normal/hot/last/pushed`（3/10/3×3/9/4） | 已烘焙未接屏 |

### ② 已烘焙未接屏的件（87 张里躺在图集里的）

- **tooltip**（蓝底 #4069c2 + `tooltip_arrow` 带箭头变体未烘）→ 悬停提示
- **滚动条**（bg+thumb 5/6/5，宽 = `AseLayout.ScrollbarSize` 12 设计格）→ 管理屏列表滚动时
- **sunken 凹槽**（4/4/4 常态/聚焦蓝环）→ 列表底/文本域底；**view 边框语法** border=3 border-top=4
- **组合框**（`sunken2` 5/6/5 + `drop_down_button` 左右拼 + 箭头三态）→ 未来下拉选择
- **窗控图标 ▶/■/⊙**（Play/Stop/Center）→ 无对应场景，备着
- **蓝字分组线**：separator + `separator_label #6e9adb` 文字、缩进 `AseLayout.SeparatorTextX`=4 → 等有分组语义的屏（设置的「视频/音频」分组、管理屏分组）

### ③ 引擎级（与「装配器运行时化」同一批做，半天到一天）

- **盒模型布局**：Aseprite 每控件自带 border/padding，**相邻控件间距 = 两者 border 相加**（没有独立的 spacing 概念）——我们现在是 AseLayout 令牌 + VBox/HStack 手摆的等价表达；完整盒模型化 = 装配器运行时化时一起做
- **buttonset 负缝连排**：`gap-rows=-3 gap-columns=-1`——成组按钮**边框互搭**连成一排（工具栏语法），Ugui 布局器用负 spacing 实现
- **mini 变体族语义**：mini_button / mini_scrollbar / mini_slider——「什么时候用紧凑档」的规则（上下文栏/内嵌区块用 mini，对话框用标准档）
- **newlayer 分层渲染**：影子字/描边字都靠它（对应我们 TMP 的做法，语义登记即可）

### ④ 观感级（需创始人先裁决，别先动手）

- **×2 语义定夺**（悬案，审计线已列）：现行 = 设计格×2 进贴图（三方自洽）；若裁决回 1:1 是专项（模板 ×1 落盘 + PressOffset (1,-1) + 令牌 ×2 方向，连锁改）。审计证据：`Px.Button=16 < 贴图切片和 20px` 九宫格压缩变形——定夺时一并裁决
- **双层深底层次**：`workspace #333333` vs `editor_face #202125` vs `face #2c2c30`——「底→容器→件」三层深浅阶梯，我们只用了一层深底
- **状态栏语法**：`status_bar_face #333333` + `status_text #636d79`（主菜单底部的暗条）——接不接待裁决

### ⑤ 「哪里用什么」速查（styles 段直译，接屏按表取）

| 场景 | 用什么 |
| --- | --- |
| 对话框 | `window_with_title`（内容内缩 6/顶 17，标题带 15） |
| 右键菜单/下拉弹层 | `popup_window` border=3 + `menu` part |
| 文本输入 | `textedit`：`textbox_face #41444a` 底 + `textbox_text #c0c0c0`，内缩 4 |
| 列表 | `view`（border 3/顶 4）包 `sunken` 底，行 = `list_item`（border 1、字 x 1） |
| 上下文工具栏 | 高 18（`context_bar_height`），钮 = `buttonset_item` 负缝连排 |
| 色板/图标钮 | 高 16（`color_bar_buttons_height`），`mini_button`（上 1/下 5 的不对称边） |

## 二、新会话操作速查（编辑器常开工作流）

1. **改 C# 后**：`echo "refresh" > export/unity-command.txt`（**仓库根** export/，不是 pirate-crew/export/）→ 喂焦点 → 等 ~50s 编译。
2. **焦点脚本**：`temp/focus-unity.ps1`（gitignored，丢了重建——ShowWindow(9)+600ms+SetForegroundWindow+**驻留 2.4s**；编辑器后台时主循环停摆，每次焦点只换 ~3 秒运行，忙任务要循环喂）：
   ```powershell
   param([int]$ProcId = <主编辑器PID>)
   Add-Type '...user32 ShowWindow/SetForegroundWindow...'
   [FocusU]::ShowWindow($p.MainWindowHandle, 9); Start-Sleep -Milliseconds 600
   [FocusU]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 2400
   ```
3. **遥控命令**（`external/editor-remote-play.flag`，读后即删）：`stop / fonts / rebake / assemble / capture:<场景|场景+settings|场景+confirm> / play:<关> / diag / camdiag`。
4. **等完成别用 tail 猜**：记下发令前行号 `wc -l`，只认**新增行**里的完成标记（旧日志假阳性踩过两次）；capture 四屏连拍期间帧也在走焦点，循环不能停。
5. **日志**：`C:\Users\fanbo\AppData\Local\Temp\pc3d-intl2.log`。**铁律：让编辑器吃新代码前先 `stop`**（Play 中域重载 = 幽灵 NRE，见主交接档踩坑#10）。
6. **验证链**：`tools/headless/run.sh harness DataEditor`（0 错）→ rebake（87 张判据 0 条）→ assemble（"三场景已重装配"）→ capture → 分析工具过目 PNG。

## 三、纪律（违者返工）

1. **布局数字一律从 `AseLayout` 取**（`Assets/Scripts/UI/Skin/AseLayout.cs`，设计格存值 + `Px()` 换算）——别再手写画布像素字面量，theme 数字≠画布像素（差一倍，挤的根源）。
2. 取色一律 `PixelSkin.Theme.*`（theme.xml 精确色），禁近似调色。
3. 新部件 → 模板 + `ResolveThemePart` 单一真源（构建/判据共用）+ ExpectedTransparent 走模板 '.' 计数。
4. 烘 = 新增件；排 = 装配改动。别拿 rebake 当调节旋钮。
5. 观感级四悬案（×2/投影/自创变体/选中金底）先裁决后动手——清单在 [UI审计与重构-进度与交接](UI审计与重构-进度与交接.md)。

## 四、已学清单（别重复学）

- **部件几何**（87 张全绿）：Plate 14×16 / Panel 8×8 直角 / Window 13×24 带标题带（切片 3/5/3/15）/ 窗控钮 9×11 三态 / ×/? 图标 / 复选/单选 8×8 / 焦点框 2/6/2 / sunken / 滑条 5/6/5 四态 / 拇指 5×4 / 滚动条 / tooltip / 箭头（→ `PixelSkin` 取用器全通）。
- **颜色表**：`PixelSkin.Theme`（#c0c0c0/#2c2c30/#41444a/#202125/#575b61/#e1b85f/#6e9adb/#4069c2/#7d7d7d/#333…）。
- **布局令牌**：`AseLayout`（dimensions 全表 + styles 间距语法逐条，含 buttonset 负缝、mini 钮不对称边）。
- **画布**：恒定像素密度 ConstantPixelSize×Unit（红警2 式，画布=屏幕÷2 随分辨率生长）。
- **接屏**：设置面板（标题带+金滑条+单选钮）、暂停标题上带、返回确认 × 窗控钮。

主档（进度/踩坑/提交史）：[像素UI与字阶-进度与交接](像素UI与字阶-进度与交接.md)。
