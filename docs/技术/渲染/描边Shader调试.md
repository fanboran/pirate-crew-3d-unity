# 描边 Shader 调试说明（M2 单位选中 / 悬停描边）

> ⚠ **口径已升级（2026-09-21，等距像素卡通方向）**：本文的宽度口径（NDC 半屏高比例、0.002~0.012、1080p 校准例）与
> `_OutlineDistanceAttenuation` 属**透视时代**。现行口径见[渲染管线-等距像素卡通.md](渲染管线-等距像素卡通.md) §5：
> 线宽在 **RT 空间**定义（`2 ÷ RT高 × 像素数`）；且**正交下 `clip.w ≡ 1`，`_OutlineDistanceAttenuation` 恒等于 1（空操作）**——
> 调它不会有任何效果。风格描边（墨色卡线）现由 `PirateToon` 的 ToonInk pass 承担；本文的 `PirateOutline*` 与全屏 Sobel
> `PirateOutlinePost` 是**选中/悬停反馈**系统（`OutlineRendererFeature` 已列入立项 M2b 退役）。
> 下文 §八 的踩坑记录（独立 LightMode 铁律等）**依然有效**，保留为知识库。

> 关联交付物：
> - `pirate-crew/Assets/Art/Shaders/PirateOutline.shader` —— 单体描边（inverted hull，含 5 档 `_DebugMode`）
> - `pirate-crew/Assets/Art/Shaders/PirateOutlinePost.shader` —— 全屏后处理描边（mask Sobel + 虚线，含 5 档 `_DebugMode`）
> - `pirate-crew/Assets/Scripts/PirateCrew/Rendering/OutlineRendererFeature.cs` —— 后处理 C# 侧（URP RendererFeature）
> - `pirate-crew/Assets/Editor/OutlineDebugCapture.cs` —— 逐档截图采集脚本
> - 设计参照：Godot 版 `outline_hover.gdshader` / `outline_selected.gdshader` / `outline_post.gdshader` 及 `pirate_base.gd` 接线

---

## ✅ 截图已产出（2026-09-13，图形界面编辑器 + play mode）

**5 档截图已实际采集并逐档核验**，产物在仓库根 [`docs/images/outline-debug/`](../../images/outline-debug/)：

| 文件 | `_DebugMode` | 实测判据（2560×1440 全图扫描） |
| --- | --- | --- |
| `debug-mode-0-normal.png` | 0 | 青色系像素 **1188**（全部在单位带内）+ 本体色 `(161,63,62)` → 本体 + 青色虚线描边 |
| `debug-mode-1-base-only.png` | 1 | 青色 **0**、品红 0，只有本体色 → 干净的、无描边的单位 |
| `debug-mode-2-expanded-hull.png` | 2 | 品红 `(255,0,255)` **5346**、本体色 0 → 本体 discard、只剩外扩壳 |
| `debug-mode-3-outline-mask.png` | 3 | 青 `(73,215,211)` **5346**、品红 0 → 不透明青色剪影，无虚线 |
| `debug-mode-4-depth-normal-raw.png` | 4 | 数据色 `(127,127,50)`（≈ 法线 0.5/0.5 + 深度 0.2）、无本体色无描边 |

另有两张非档位图：`01-live-selection-cyan-outline.png`（真实选中通路：全场只有 1 个单位有描边）、
`00-evidence-shader-compile-error-magenta.png`（着色器编译失败时全场品红的故障证据）。

> **扫描判据必须带色相条件**：用宽松的通道容差（`|r-150|<45` 等）会把 HUD 里浅灰/淡蓝文字的抗锯齿像素
> 算成"青色"（实测 451 px 假阳性）。正确判据：`(g-r)>60 && (b-r)>60 && g>150`。

**两处与本文档原先描述不符的实测修正（重要）**：

1. **档 2/档 3 是"实心剪影"，不是"一圈轮廓"**。对实心凸体（单位是 Cube），背面壳的投影并集等于整个剪影，
   只有在本体 Pass 盖住中间之后才看得出"环"。原文档写"只剩一圈品红胖轮廓"会让后来者以为档 2 坏了。
2. **本工程未开启 URP Depth Texture 的结论仍然成立**（两套 URP Asset 都是 `m_RequireDepthTexture: 0`），
   档 4 用的是物体自身法线/视空间深度，不依赖 `_CameraDepthTexture`——实测颜色与"相机 z=-10、物体 z=0"吻合。

### 复现与注意事项

- 必须**图形界面编辑器 + play mode**（无渲染路径的 `-batchmode -nographics` 拿不到像素）。
- **编辑器暂停时不能采集**：`Time.frameCount` 不推进 → 改档后画面不重绘。采集脚本会**明确报错退出**而不是挂死。
- **最后一档的复位要延迟**：`ScreenCapture.CaptureScreenshot` 是异步的（在当前帧末尾取像素），
  若在同一帧里紧接着把 `_DebugMode` 复位成 0，最后一张会拍成复位后的样子。
  实测曾把档 4 拍成"红色本体 + 稀疏青描边"（= 档 0 的样子）。脚本已用独立的收尾阶段规避。
- 采集脚本会临时把场内所有 `UnitOutlineBinder` 强制为选中态（`DebugForcedState = 2`），
  这样档 0 才能看到描边；采集结束复位为 -1。`_DebugMode` 改的是**材质资产**，中断时可能残留，重跑一次即可复位。

---

## 一、Godot 版描边实现路径摘要（先读懂再移植）

Godot 版有**三条并行**的描边路径，按状态选用（接线见 `modules/pirate_crew/scripts/characters/pirate_base.gd`）：

| shader | 行数 | 实现路径 | 用在哪 |
| --- | --- | --- | --- |
| `assets/shaders/outline.gdshader` | 27 | **inverted hull**：`cull_front, unshaded, depth_draw_never`；顶点把法线投到裁剪空间后按 `1/VIEWPORT_SIZE * w` 偏移 | 通用基础描边 |
| `outline_hover.gdshader` | 26 | **inverted hull**：`cull_front, unshaded`；法线 → 裁剪空间 xy → `normalize * outline_width * pow(clip.w, 1-attenuation)` | **悬停** |
| `outline_selected.gdshader` | 39 | 同上 + **屏幕空间流动虚线**（`sin(screen_pos.y * freq + TIME * speed)`） | **选中**（备用） |
| `outline_post.gdshader` | 126 | **全屏后处理**：采样 SubViewport mask → 二值化 → 3×3 Sobel → 阈值 → 沿边缘切线切虚线 | **选中（实际启用）** |

**关键区别：本项目实际接线是「hover 用 inverted hull，selected 用全屏后处理」。**
`pirate_base.gd` 的 `_update_outline_visual()` 只把 `outline_hover.gdshader` 挂到复制网格上；
`battle.gd` 把 `selection_mask_viewport.get_texture()` 喂给 `outline_post` 的 `mask_texture`。
即 Godot 版里 `outline_selected.gdshader`（inverted hull 版选中）其实**没被启用**，选中走的是后处理。

### hover 与 selected 差在哪

| 维度 | hover | selected |
| --- | --- | --- |
| **实现载体** | inverted hull（复制一圈网格 + `material_override`） | 全屏后处理（mask SubViewport + Sobel） |
| 颜色 | 淡白 `vec4(1,1,1,0.22)` | 青色 `#49d9d6` `vec4(0.286,0.851,0.839,0.949)` |
| 粗细 | 细（`outline_width = 0.0025`） | 粗（`outline_width = 0.006`；后处理另由 `outline_thickness` 控制，但该参数在 fragment 里**未被使用**） |
| 线型 | 实线 | 屏幕空间**流动虚线** |
| 距离处理 | `pow(clip.w, 1 - 0.4)` → 远处变细 | 同上 |
| 语义 | "可选中"提示 | "已选中" |

**共同点（都要保留的调参内核）**：两条路径都做「距离衰减」`pow(clip.w, 1 - distance_attenuation)`，让远处线条变细，避免远处单位糊成一片。

### Unity 侧的取舍（翻译决策，记录在此避免以后反复）

1. **inverted hull 合并两态**：Godot 为 hover/selected 各准备一个材质 + 一圈复制网格。Unity 侧 `PirateOutline.shader` 用 `_OutlineState`（0/1/2）在**同一个材质内**切换颜色/宽度/虚线，省掉复制网格与管理成本（Unity 的 SkinnedMeshRenderer 复制网格比 Godot 麻烦）。
2. **后处理沿用 Godot 的「mask + Sobel」路线**，而不是 URP 常见的「深度 + 法线边缘检测」路线。原因见第五节（本工程 `m_RequireDepthTexture: 0`，深度法线不可用）。
3. `outline.gdshader` 的 `outline_near_boost`（近处增粗）是基础描边专用；Unity 版未移植该分支（hover/selected 都不需要），如需可仿 `_OutlineDistanceAttenuation` 加一个负向 boost。

---

## 二、`PirateOutline.shader` 逐档「应看到什么」+ 常见异常

调试档由 `_DebugMode`（float，0–4）控制；采集脚本会依次写入 0→4 并截图。

### 档 0 —— 正常合成（本体 + 描边）

- **应看到**：单位本体正常着色（简单 Lambert + SH 环境光），轮廓外一圈描边。
  - `_OutlineState = 0` → 兜底色（默认青）；`= 1` → 淡白细实线；`= 2` → 青色粗线 + 沿屏幕 Y 流动的虚线。
- **常见异常**：
  - 完全没有描边 → `_OutlineState` 还是 0 且兜底色 alpha 太低，或 `Cull Front` 被改成了 `Cull Back`，或外扩宽度接近 0。
  - 描边糊住整个本体 → `_OutlineWidth` 过大（见第三节区间表）。
  - 描边被本体"切掉"一半 → 本体 Pass 的 `ZWrite On` 与描边 `ZTest LEqual` 配合正常；若把描边 `ZTest` 改成 `Always` 就会出现描边压过本体。

### 档 1 —— 只显示本体不描边

- **应看到**：干净的、**没有任何描边**的单位。用于确认「本体着色本身没问题」。
- **常见异常**：
  - 仍有描边 → 描边 Pass 的 `discard` 分支没命中（`_DebugMode` 没写进材质 / 写到了错误材质）。
  - 本体全黑 → 场景缺平行光（`GetMainLight()` 无光时 color 为 0），或模型法线朝内。

### 档 2 —— 只显示法线外扩结果（品红实色壳）

- **应看到**：本体消失，单位位置变成**品红实色块**（外扩后的背面壳），无描边颜色、无虚线。
  ⚠ **对实心凸体（本工程的单位是 Cube）这里是"实心剪影"，不是"一圈环"**：背面壳的投影并集等于整个剪影，
  要等本体 Pass 盖住中间才看得出环。2026-09-13 实测：单位区域 675 个像素全是 `(255,0,255)`。
  如果模型是薄壳/镂空件，才会看到真正的"环"。
- **常见异常**：
  - 本体轮廓"发胖"但看不到品红 → 说明看到的是本体在档 1 的残留？不对，档 2 本体 Pass 会 `discard`；若仍见本体，是档位写错。
  - 发胖过量（壳离本体很远）→ `_OutlineWidth` 太大，或 `_OutlineExpandMode = 1` 时 `_OutlineWidth` 单位是**米**而没从小数改成 0.01~0.05。
  - **壳上有破洞 / 裂缝** → 模型法线被压平（相邻顶点法线不连续，多为硬边 / 未导入平滑法线 / 法线贴图烘焙进顶点后丢失），外扩方向在裂缝处不一致。这是 inverted hull 的经典缺陷；缓解手段：改用平滑法线（第二套 UV/顶点色存平滑法线）或改用后处理描边。**Cube 的六个面各自法线不连续，理论上有裂缝，但裂缝落在本体剪影内部 → 被本体 Pass 盖住，所以实测看不到破洞。**
  - 品红壳只在物体一侧出现 → `Cull Front` 生效但法线方向反了（模型是内翻法线）。
  - **完全没有品红（本体也没了）** → 描边 Pass 根本没被执行。**最常见原因是它对 Base Pass 用了同一个 `LightMode`**，见 §八-2。

### 档 3 —— 只显示描边掩码（单色剪影）

- **应看到**：单位位置是**不透明、无流动**的纯青色实心剪影（把描边当实线画），忽略虚线、忽略 hover 的半透明。
  同样是"实心"而非"一圈"，原因见档 2。2026-09-13 实测：单位区域 675 个像素全是 `(73,215,211)`。
- **常见异常**：
  - 轮廓断断续续 → 同档 2 的法线不连续问题（外扩位移在硬边处不一致）。
  - 完全没有轮廓 → 见档 2 最后一条（`LightMode` 撞车）；或 `Cull Front` 写反（改 `Cull Back` 试）。
  - 轮廓颜色不是青 → 本档固定用 `_OutlineColorSelected.rgb`，若不同说明改到了别处。

### 档 4 —— 深度 / 法线原始数据

- **应看到**：本体表面换成数据可视化：**R = 世界法线 .x 映射**，**G = 世界法线 .y 映射**，**B = 视空间深度**（越近越黑、越远越蓝，50m 封顶）。描边 Pass 在本档整段丢弃，所以画面里**没有描边**。
- **常见异常**：
  - 整块死灰蓝无渐变 → 法线没传进 Varyings（`normalOS` 丢失 / 模型无法线 / 着色器被换成无 NORMAL 语义的网格）。
  - 整块同色 → 模型是平面/硬边（正常，换角色或球体看渐变）。
  - B 通道全黑 → 相机近裁面设置过大或物体贴在相机上；B 全蓝 → 物体离相机 > 50m（`viewDepth / 50.0` 封顶，属预期）。

> 说明：本档显示的是**物体自身**的深度/法线，不依赖 `_CameraDepthTexture`。这是刻意设计——本工程未开启 URP Depth Texture（见第五节），依赖它会直接输出死色。

---

## 三、关键参数调参指南（`PirateOutline.shader`）

材质面板参数与 Godot 的对应关系：

| Unity 属性 | 对应 Godot | 单位 | 合理区间 | 说明 |
| --- | --- | --- | --- | --- |
| `_OutlineWidth` | `outline_width`（兜底/未用） | 模式 0：**NDC 归一化单位**（≈半屏高度的比例）；模式 1：**米** | 模式 0：**0.002 ~ 0.012**；模式 1：**0.01 ~ 0.05 m** | 模式 0 下 0.006 ≈ 1000px 高画面里约 3px 单边宽 |
| `_OutlineWidthHover` | hover `outline_width` | 同上 | **0.002 ~ 0.004** | 悬停要"细而淡" |
| `_OutlineWidthSelected` | selected `outline_width` | 同上 | **0.005 ~ 0.010** | 选中要"粗而亮" |
| `_OutlineColorHover` | hover `outline_color` | RGBA | 建议 `(1,1,1,0.18~0.30)` | 淡白、低 alpha 才不喧宾夺主 |
| `_OutlineColorSelected` | selected `outline_color` | RGBA | 建议 `#49d9d6`，alpha `0.85~1.0` | 与 hover 拉开色相（青 vs 白）比只靠亮度差更清晰 |
| `_OutlineState` | 由 `pirate_base.gd` 切材质 | 0/1/2 | — | 0 无 / 1 悬停 / 2 选中；由选中系统每帧写 |
| `_OutlineAlpha` | — | 0~1 | 1.0 | 整体乘算，做淡入淡出动画用 |
| `_OutlineExpandMode` | — | 0/1 | 0 | 0=屏幕空间恒定（推荐，Godot 等价做法）；1=世界空间经典 inverted hull |
| `_OutlineDistanceAttenuation` | `distance_attenuation` | 0~1 | **0.3 ~ 0.5**（Godot 默认 0.4） | 0=远近一样粗（远处会糊）；1=远处迅速变细（远处看不见） |
| `_DashSpeed` | `dash_speed` | 越大越快 | **3 ~ 8** | 只对 `_OutlineState = 2` 生效 |
| `_DashFrequency` | `dash_frequency` | 屏幕空间频率 | **30 ~ 70**（Godot 默认 50） | 越大虚线越密；太小会变成"长条" |

**宽度单位直观校准**（模式 0）：`_OutlineWidth = w` 时，单边描边在屏幕上约 `w * 半屏高度(px)` 像素宽。
例：Game View 1080p（半高 540px），`w = 0.006` → 约 **3.2 px**；`w = 0.0025` → 约 **1.35 px**（Godot hover 的观感）。

**hover / selected 配色建议**：
- 用**色相**区分而不是只调 alpha —— 淡白（hover）vs 青（selected）在深色场景里辨识度高得多，也照顾色弱玩家。
- 选中同时加**虚线**这一"形状通道"，即使玩家对颜色不敏感也能区分（多重编码）。
- alpha 别超 1.0；hover 建议 ≤ 0.30，避免遮住单位细节。

**常见调参症状速查**：
| 症状 | 先调 |
| --- | --- |
| 远处单位糊成一团 | 调大 `_OutlineDistanceAttenuation`（让远处更细） |
| 描边粗细随距离乱跳 | 确认 `_OutlineExpandMode = 0`（恒定粗细） |
| 描边在狭窄部位互相穿插 | 调小 `_OutlineWidth`，或改后处理方案 |
| 虚线像静止/闪烁 | 检查 `_Time.y * _DashSpeed`，`_DashSpeed = 0` 会静止 |
| **描边只有两三段短线、不成一圈** | 虚线周期与单位屏幕尺寸同量级：`_DashFrequency` 调小（更长虚线）**或**把战斗相机拉近（当前正交尺寸 10，整关可见，单位只有约 27×36px） |

> **2026-09-13 实测的观感记录**：选中态在 2560×1440 下，`_DashFrequency = 50` 对应约 45px 实线 + 45px 空白，
> 而单位周长约 126px → 一圈上只能落 1~2 个完整虚线段，视觉上像"角落有两三道短线"。
> 这是**相机取景 + 单位尺寸**与虚线频率的合成结果，不是描边失效（判据：档 3 的实心掩码是完整剪影）。
> 改法有两条独立杠杆（调 `_DashFrequency`、调相机正交尺寸/跟随），都属美术取向，留观感验收决定。

---

## 四、`PirateOutlinePost.shader` 逐档「应看到什么」+ 常见异常

参数与 Godot `outline_post.gdshader` 一一对应（档位语义逐字继承）：

| 档 | Godot `debug_mode` | 画面 |
| --- | --- | --- |
| 0 | 0 | 正常流动虚线描边（最终效果） |
| 1 | 1 | 原始 mask（未二值化；白=选中单位，其余黑） |
| 2 | 2 | 二值化 mask（`step(0.5)` 之后；纯白剪影） |
| 3 | 3 | Sobel 边缘强度灰度图（越亮=边缘越强） |
| 4 | 4 | 纯边缘 mask（阈值后、无虚线；连续实线轮廓） |

**逐档常见异常**：

- **档 1 整屏全黑** → mask Pass 没画进去：Layer 不匹配（`maskLayer` 没勾选单位所在 Layer）、`LightMode` Tag 与 DrawingSettings 的 `ShaderTagId` 不一致、或 `outlinePostShader` 字段为空导致 Feature 没生效。
- **档 1 整屏全白** → mask RT 没被 Clear，或 `maskLayer = Everything` 且场景里到处是物体。
- **档 2 有剪影但档 3 无亮线** → Sobel 采样坐标错（`_ScaledScreenParams` 与实际 RT 尺寸不符，多发生在 renderScale ≠ 1）。
- **档 3 有亮线但档 0 无虚线** → 阈值 `_EdgeThreshold` 偏高把边缘切没了；或虚线 `_DashLength/_DashGap` 比例极端（period 极小 → 全断）。
- **档 4 线条断续** → `_EdgeThreshold` 偏高，或 mask 分辨率不足。
- **后处理描边"套在错误物体上"** → `maskLayer` 覆盖了地面等非单位物体；收窄 Layer。

### 后处理参数调参指南

| 参数 | 对应 Godot | 区间 | 说明 |
| --- | --- | --- | --- |
| `_OutlineColor` | `outline_color` | `#49d9d6` | 选中描边色 |
| `_EdgeThreshold` | `edge_threshold` | **0.1 ~ 0.3**（默认 0.2） | **核心参数**：越大→边缘越少越细；越小→描边越粗越多甚至糊成一片 |
| `_DashLength` / `_DashGap` | `dash_length/gap` | 8 / 6 | 虚线长短与间隔（**像素**单位，随分辨率变化） |
| `_DashSpeed` | `dash_speed` | 5 | 流动速度 |

**关于「深度阈值 / 法线阈值」**（任务要求说明）：

本实现走的是 **mask Sobel** 路线（与 Godot 一致），**没有** `_DepthThreshold` / `_NormalThreshold` 这两个参数 —— 因为边缘来源不是场景深度/法线，而是"选中单位 mask"。所以**不需要**调深度/法线阈值。

若将来要改用 URP 常见的「深度 + 法线边缘检测」路线（本地参照实现：`external/m2-combat-reference/urp-outlines/Outlines/Scripts/RendererFeatures/ScreenSpaceOutlines.cs`，用 Roberts Cross + 法线阈值），**前置条件是先开启深度/法线纹理**：

> **实测结论（重要）**：本工程当前两套 URP 资产都是 **未开启** 的，直接抄那条路线会输出死色：
> ```
> Assets/Settings/URP/PC_Balanced_URPAsset.asset:22   m_RequireDepthTexture: 0
> Assets/Settings/URP/PC_Balanced_URPAsset.asset:23   m_RequireOpaqueTexture: 0
> Assets/Settings/URP/PC_Performant_URPAsset.asset:22 m_RequireDepthTexture: 0
> Assets/Settings/URP/PC_Performant_URPAsset.asset:23 m_RequireOpaqueTexture: 0
> ```
> 需要时在 URP Asset Inspector 勾 **Depth Texture**，并在 Renderer 勾 **Depth Normals** 预通道，
> 然后在 shader 里 `#include ".../DeclareDepthTexture.hlsl"` / `".../DeclareNormalsTexture.hlsl"`
> 用 `SampleSceneDepth` / `SampleSceneNormals` 采样（两个文件与函数均已本地核对存在）。
>
> 那条路线的调参经验（供改造时参考）：
> - **深度阈值**（Roberts Cross 差值）：越大→只保留深度突变强的边；对"地面与单位"这类大深度差很敏感，建议从 **1.0 ~ 3.0**（世界单位）起调；太小会把斜面/地面纹理当边缘，太大则轮廓断裂。
> - **法线阈值**（相邻法线点积差）：越大→只保留法线夹角大的边；建议从 **0.3 ~ 0.5** 起调；太小会把量化噪声当边（尤其在 8bit 法线缓冲上）。
> - 两者是**或**关系（任一超阈值即成边），所以通常把深度阈值调紧、法线阈值放宽，用深度抓"物体轮廓"、用法线抓"褶皱/棱边"。
> - 记得配合 `_SteepAngleThreshold/_SteepAngleMultiplier`（陡角抑制）避免地面掠射角处整片变黑。

---

## 五、本地 URP 包核对结果（每个 `#include` / 宏逐条核实）

依据：`pirate-crew/Library/PackageCache/com.unity.render-pipelines.universal@14.0.12`（记作 **[URP]**）
与 `.../com.unity.render-pipelines.core@14.0.12`（记作 **[CORE]**）。全部为**本地实际存在**，非凭记忆。

### 5.1 `PirateOutline.shader` 的 include

| 写法 | 本地核对结果 |
| --- | --- |
| `#include ".../universal/ShaderLibrary/Core.hlsl"` | ✅ [URP] `ShaderLibrary/Core.hlsl` 存在 |
| `#include ".../universal/ShaderLibrary/Lighting.hlsl"` | ✅ [URP] `ShaderLibrary/Lighting.hlsl` 存在（`:4-9` 引入 `BRDF.hlsl` / `Debugging3D.hlsl` / `GlobalIllumination.hlsl` / `RealtimeLights.hlsl` / `AmbientOcclusion.hlsl` / `DBuffer.hlsl`） |

> **取舍说明（含一次实测翻车，见 §八）**：本体光照只需 `GetMainLight()` + `SampleSH()`。
> 早先版本为了"缩小编译面"，只 include `GlobalIllumination.hlsl`（它内部会引 `RealtimeLights.hlsl`，
> 符号检索显示 `Light`/`GetMainLight`/`SampleSH` 都在）——**静态核对全绿，真实编译却失败**：
> `GlobalIllumination.hlsl` 自身并不 include `BRDF.hlsl`，而它的 `GlobalIllumination(BRDFData, ...)` 函数体用到了 `BRDFData`。
> 现在改回 URP 自带 Lit/SimpleLit 也走的 `Lighting.hlsl`。
> 教训：**include 的"自洽性"不能靠文件名/符号检索推断，必须在有渲染路径的编辑器里真实编译过**。

**include 链（核对结论）**：`Core.hlsl` → `[CORE] Common.hlsl`、`Packing.hlsl`、`Version.hlsl`、`[URP] Input.hlsl`（Core.hlsl:19）、`ShaderVariablesFunctions.hlsl`（Core.hlsl:165）、`Deprecated.hlsl`（Core.hlsl:166）；
`[URP] Input.hlsl:217` → `[CORE] SpaceTransforms.hlsl`。
→ 因此**只 include `Core.hlsl`** 就已具备变换/顶点输入/宏，无需再单独引 `SpaceTransforms.hlsl`。

### 5.2 `PirateOutline.shader` 用到的宏 / 函数

| 宏 / 函数 | 本地核对结果（文件:行） |
| --- | --- |
| `TransformObjectToHClip` | ✅ [CORE] `ShaderLibrary/SpaceTransforms.hlsl:108` |
| `TransformObjectToWorldNormal` | ✅ [CORE] `ShaderLibrary/SpaceTransforms.hlsl:199` |
| `TransformWorldToView` | ✅ [CORE] `ShaderLibrary/SpaceTransforms.hlsl:97` |
| `TransformWorldToViewDir` | ✅ [CORE] `ShaderLibrary/SpaceTransforms.hlsl:155` |
| `GetVertexPositionInputs` | ✅ [URP] `ShaderLibrary/ShaderVariablesFunctions.hlsl:7` |
| `GetVertexNormalInputs` | ✅ [URP] `ShaderLibrary/ShaderVariablesFunctions.hlsl:21` |
| `UNITY_MATRIX_P` | ✅ [URP] `ShaderLibrary/Input.hlsl:193` → `#define UNITY_MATRIX_P OptimizeProjectionMatrix(glstate_matrix_projection)`；`OptimizeProjectionMatrix` 定义在 [URP] `ShaderLibrary/UnityInput.hlsl:282`，`glstate_matrix_projection` 在 `UnityInput.hlsl:20/213`，`UnityInput.hlsl` 经 `Input.hlsl:208` 引入 |
| `struct Light` / `GetMainLight` | ✅ [URP] `ShaderLibrary/RealtimeLights.hlsl:12` / `:97`（经 `GlobalIllumination.hlsl` 引入） |
| `SampleSH` | ✅ [URP] `ShaderLibrary/GlobalIllumination.hlsl:21` |
| `_Time` | ✅ [URP] `ShaderLibrary/UnityInput.hlsl:40`（`float4 _Time`） |
| `_ScaledScreenParams` | ✅ [URP] `ShaderLibrary/Input.hlsl:92` |
| `CBUFFER_START` / `CBUFFER_END` | ✅ [CORE] `ShaderLibrary/API/{D3D11,Vulkan,Metal,GLCore,GLES3,...}.hlsl`（按平台，如 `API/D3D11.hlsl:22-23`），经 `Common.hlsl` 的平台分支引入 |
| `ComputeScreenPos` | ❌ **[CORE] `ShaderLibrary/` 下不存在** → 已确认并在 shader 里**避免使用**，屏幕 UV 改为手算 `clip.xy / clip.w` |

### 5.3 `PirateOutlinePost.shader` 的 include

| 写法 | 本地核对结果 |
| --- | --- |
| `#include ".../universal/ShaderLibrary/Core.hlsl"` | ✅ 同 5.1 |
| `#include ".../core/Runtime/Utilities/Blit.hlsl"` | ✅ [CORE] `Runtime/Utilities/Blit.hlsl` 存在。**注意：`[CORE]/ShaderLibrary/` 下没有 `Blit.hlsl`**，正确路径在 `Runtime/Utilities/`；依据是 [URP] 自带 `Shaders/Utils/Blit.shader` 里的实际 include 行 |

### 5.4 `PirateOutlinePost.shader` 用到的符号

| 符号 | 本地核对结果（文件:行） |
| --- | --- |
| `_BlitTexture` | ✅ [CORE] `Runtime/Utilities/Blit.hlsl:10`（`TEXTURE2D_X(_BlitTexture)`） |
| `Varyings` / `Vert(Attributes)` | ✅ [CORE] `Runtime/Utilities/Blit.hlsl:36` / `:43`（全屏三角形顶点着色器） |
| `_BlitScaleBias` | ✅ [CORE] `Runtime/Utilities/Blit.hlsl:13` |
| `sampler_PointClamp` | ✅ [CORE] `ShaderLibrary/GlobalSamplers.hlsl:7`（经 `Blit.hlsl` 引入） |
| `sampler_LinearClamp` | ✅ [CORE] `ShaderLibrary/GlobalSamplers.hlsl:8`（同上） |
| `TEXTURE2D_X` | ✅ [URP] `ShaderLibrary/Core.hlsl:44`（XR/数组）与 `:62`（普通）两个分支 |
| `SAMPLE_TEXTURE2D_X` | ✅ [URP] `ShaderLibrary/Core.hlsl:52` / `:70` |
| `_ScaledScreenParams` | ✅ [URP] `ShaderLibrary/Input.hlsl:92` |
| `_Time` | ✅ [URP] `ShaderLibrary/UnityInput.hlsl:40` |
| `TransformObjectToHClip`（mask Pass） | ✅ [CORE] `ShaderLibrary/SpaceTransforms.hlsl:108` |
| `#pragma vertex Vert` | ✅ 与 [CORE] `Blit.hlsl:43` 的 `Vert` 签名匹配（`Attributes` 用 `SV_VertexID`） |

### 5.5 C# 侧（`OutlineRendererFeature.cs`）核对与验证

| API | 本地核对结果 |
| --- | --- |
| `ScriptableRendererFeature`（`Create` / `AddRenderPasses` / `Dispose(bool)`） | ✅ [URP] `Runtime/ScriptableRendererFeature.cs:12/23/37/103` |
| `ScriptableRenderPass.Execute(ScriptableRenderContext, ref RenderingData)`（abstract） | ✅ [URP] `Runtime/Passes/ScriptableRenderPass.cs:675` |
| `OnCameraSetup(CommandBuffer, ref RenderingData)` | ✅ [URP] `Runtime/Passes/ScriptableRenderPass.cs:631` |
| `ConfigureTarget(RTHandle)` | ✅ [URP] `Runtime/Passes/ScriptableRenderPass.cs:579` |
| `ConfigureClear(ClearFlag, Color)` | ✅ [URP] `Runtime/Passes/ScriptableRenderPass.cs:615` |
| `CreateDrawingSettings(List<ShaderTagId>, ref RenderingData, SortingCriteria)` | ✅ [URP] `Runtime/Passes/ScriptableRenderPass.cs:771` |
| `cameraColorTargetHandle` | ✅ [URP] `Runtime/ScriptableRenderer.cs:417` |
| `DrawingSettings.overrideMaterialPassIndex` | ✅ [URP] 在 `Runtime/Passes/RenderObjectsPass.cs:134` 实际赋值 |
| `context.DrawRenderers(...)` | ✅ [URP] 在 `Runtime/Passes/RenderObjectsPass.cs:174` 实际调用（同一套签名） |
| `RenderingUtils.ReAllocateIfNeeded(ref RTHandle, ...)` | ✅ [URP] `Runtime/RenderingUtils.cs:596` |
| `Blitter.BlitCameraTexture(cmd, RTHandle, RTHandle, Material, int)` | ✅ [CORE] `Runtime/Utilities/Blitter.cs:377` |
| `Blitter.BlitCameraTexture(cmd, RTHandle, RTHandle)` | ✅ [CORE] `Runtime/Utilities/Blitter.cs:342` |
| `CoreUtils.Destroy(UnityObject)` | ✅ [CORE] `Runtime/Utilities/CoreUtils.cs:1142` |
| `ProfilingSampler(string)` | ✅ [CORE] `Runtime/Debugging/ProfilingScope.cs:81` |
| `cameraData.defaultOpaqueSortFlags` | ✅ [URP] `Runtime/UniversalRenderPipelineCore.cs:658` |
| `cameraData.renderer` | ✅ [URP] `Runtime/UniversalRenderPipelineCore.cs:738` |
| `DisallowMultipleRendererFeature(string)` | ✅ [URP] `Runtime/RendererFeatures/DisallowMultipleRendererFeature.cs:20` |

### 5.6 验证命令与结果

```bash
# C# 侧（RendererFeature）：0 错误 0 警告
cd /f/VSCode/pirate-crew-3d-unity
mkdir -p external/harness-rendering && cp external/harness/Harness.csproj external/harness-rendering/
cd external/harness-rendering
dotnet build Harness.csproj -p:HarnessScope=Rendering
# → 已成功生成。0 个警告，0 个错误

# Editor 侧（OutlineDebugCapture.cs）：不在 Rendering scope 内，另建临时验证台
cd /f/VSCode/pirate-crew-3d-unity/external/harness-editor
dotnet build VerifyEditorOutline.csproj
# → 已成功生成。0 个警告，0 个错误
```

> **Shader 本身无法在本环境编译验证**（Unity 的 shader 编译器只在编辑器内跑，且不许启动 Unity 进程）。
> 因此 §5.1–5.4 的逐条核对就是本环境能做到的最大程度；**首次在有图形界面的编辑器里导入 shader 时，务必看 Console 有无 shader 编译错误**，这是唯一的最终验证。

---

## 六、接线方式（Unity 侧，**已实现**）

### 6.1 单体描边（已接线，这是当前生效的唯一描边路径）

| 环节 | 载体 |
| --- | --- |
| 状态判定（纯逻辑） | `Assets/Scripts/PirateCrew/Battle/OutlineStateRules.cs`——`Resolve(alive, selected, hovered)` → 0/1/2，5 条 NUnit 测试在 `Assets/Tests/Battle/OutlineStateRulesTests.cs` |
| 状态位来源 | `PirateBase.SetSelected / SetHover`（原本只有状态位、无表现）。选中状态的唯一性由 `BattleTeam.Select / StartTurn / FinishTurn` 同步（`TurnManager` 会直接调 `BattleTeam.Select`，只钩 `BattleController` 会漏） |
| 悬停状态位 | `AimThrowController.UpdateHover()`——鼠标 30px 内最近的本队存活角色（§4.5 `Controller.hoverCharacter`）；拖拽中或鼠标在 UGUI 上时不悬停 |
| 状态位 → 材质属性 | `Assets/Scripts/PirateCrew/Battle/UnitOutlineBinder.cs`：每帧把档位写进 `MaterialPropertyBlock`（`_OutlineState`），并按队伍写 `_BaseColor`（纯表现，红/蓝队可区分） |
| 单位材质 | `Assets/Prefabs/PirateCrew/Materials/PirateOutlineUnit.mat`（`PirateOutline` shader），由 `BattleSceneSetup.EnsureOutlineMaterial()` 幂等生成 |
| 预制体装配 | `BattleSceneSetup.BuildPiratePrefab()`——Cube + `PirateBase` + `UnitOutlineBinder`，材质即上面那个 |

**为什么用 `MaterialPropertyBlock` 而不是 `renderer.material`**：后者会给每个单位克隆一份材质实例
（12 个单位 = 12 份克隆），MPB 是逐渲染器覆盖，共享材质资产不被改脏。代价是该渲染器退出 SRP Batcher 批次，
对十余个单位的量级没有实际影响（实测 `_OutlineState` 能正确落到每个渲染器上，且只有被选中的那一个画出描边）。

**"状态 0 必须不可见"的实现**：shader 的 `OutlineColorForState()` 在 state==0 时回落到 `_OutlineColor`，
所以材质里把 `_OutlineColor` 的 **alpha 设为 0**——片元里 `alpha < 0.002` 会 `discard`，
未悬停/未选中的单位就不会顶着一圈青边。

### 6.2 全屏后处理描边（代码保留，**当前未启用**）

`OutlineRendererFeature` 仍然挂在 `Assets/Settings/URP/PC_Balanced_Renderer.asset` 上且 `isActive=true`，
但它的 `maskLayer.m_Bits` 已从调试期的 `4294967295`（Everything）**收窄为 `0`（Nothing）**。

- 原因：mask Pass 会把 `maskLayer` 上的**全部不透明物体**画成白剪影再做 Sobel，`Everything` 会把地面也算进去，
  于是整屏边缘（含地平线）都被描成青色虚线，既污染观感也污染调试截图。`OutlineRendererFeature.cs` 的注释本来就写着
  "默认 Everything 只是便于立刻看到效果（调试），正式使用请只勾选单位所在 Layer"——现在按该说明收窄。
- 本工程的**选中反馈已由 6.1 的 inverted hull 承担**（hover/selected 两态合并进同一材质，是刻意的翻译简化），
  所以后处理这条路径目前是冗余的；保留代码是为了将来若"选中"想要更粗的 Sobel 虚线轮廓时可以用。
- 要启用：新建一个只放"可选中单位"的 Layer → 运行时把选中单位切到该 Layer → 把 `maskLayer` 指到它。
  启用前请先确认它与 6.1 不会同时画（否则会出现两套描边叠加）。

### 6.3 程序集说明

`OutlineRendererFeature.cs` 用到 URP 类型，位于 `Assets/Scripts/PirateCrew/Rendering/`，
该目录带独立 asmdef（`PirateCrew.Rendering.asmdef`，引用 URP/Core 运行时程序集），
因为 `PirateCrew.Gameplay.asmdef` 无法引用 URP。
而 `OutlineStateRules` / `UnitOutlineBinder` **只用 UnityEngine 类型**（MPB + `Shader.PropertyToID`），
所以放在 `Battle/`（`PirateCrew.Gameplay`）里，不需要碰 URP 程序集。

---

## 七、待办 / 已知限制

1. ~~真实截图未产出~~ → **已产出并核验**（见文首与 `docs/images/outline-debug/`）。
2. `PirateOutline.shader` **没有 ShadowCaster Pass**：单位材质换成它之后单位不投影，且本体光照是 shader 内的简单 Lambert + SH（不再是 URP/Lit）。若观感验收要求阴影/更丰富光照，补一个 `LightMode = "ShadowCaster"` 的极简 Pass（注意 `_LightDirection` / `ApplyShadowBias`），或改成"本体保持 URP/Lit + 描边走复制网格"（代价是共面 z-fighting 与网格复制管理）。
3. ~~`OutlineRendererFeature` 需要手工加到 URP Renderer 资产~~ → **已挂上**（`PC_Balanced_Renderer.asset`，`isActive=true`）。但 `maskLayer` 已收窄为 `0`，等于当前不参与渲染，见 §6.2。
4. `outline.gdshader` 的 `outline_near_boost`（近处增粗）未移植。
5. 后处理描边的 `_DashLength/_DashGap` 是**像素**单位，分辨率变化时观感会变；若要分辨率无关需改成 NDC 单位。
6. **实体描边的虚线在小单位上偏碎**（见 §三 末尾的实测记录）——观感问题，留验收决定。

---

## 八、两次真实事故复盘（URP / 编辑器行为，务必先读）

文档前面所有"逐条核对"都只能证明符号存在，**不能**证明 include 自洽、也不能证明 pass 会被执行。
以下两次都是"静态核对全绿、真实运行才暴露"，且 Console 的报错方式完全不同（一次明确报错、一次完全静默）。

### 八-1 着色器编译失败：`unrecognized identifier 'BRDFData'`

- **现象**：`PirateOutline.shader` 只 include `GlobalIllumination.hlsl`。进 play mode 后 Console：
  `Shader error in 'PirateCrew/PirateOutline': unrecognized identifier 'BRDFData' at .../ShaderLibrary/GlobalIllumination.hlsl(353) (on d3d11)`；
  全场景里所有用该 shader 的单位变成**品红**（URP 错误材质）。故障留档见 `docs/images/outline-debug/00-evidence-shader-compile-error-magenta.png`。
- **根因**：`GlobalIllumination.hlsl` 自身不 include `BRDF.hlsl`，而它的 `GlobalIllumination(BRDFData, ...)` 函数体用到了 `BRDFData`。
  它只对"调用者已引入 BRDF"的场景自洽（`Lighting.hlsl` 就是那个调用者）。
- **修法**：include 改成 `Lighting.hlsl`（URP 自带 Lit/SimpleLit 也走它）。
- **教训**：本工程在无渲染路径下只能做静态核对，**任何 shader include 改动都必须进图形界面编辑器 play 一次 + `read_console` 确认 0 条 shader error**。

### 八-2 描边 Pass 被静默丢弃：额外 pass 与 Base Pass 撞同一个 `LightMode`

- **现象**：材质、本体、状态位全都正常（`mpbState=2` 确实写进了渲染器），**就是一像素描边都没有，Console 一条报错都没有**。
- **定位过程（可复用）**：用 `_DebugMode = 2`（只画外扩壳）——本体 Pass 如期 `discard`（单位从画面消失），
  但品红壳一像素都没出现 → 说明描边 Pass **从未被执行**，而不是"画了但看不见"。
- **根因**：`PirateOutline` 的描边 Pass 与本体 Pass 都标了 `"LightMode" = "UniversalForward"`。
  URP 的不透明前向 `DrawObjectsPass` 用固定 ShaderTagId 列表取 pass（`DrawObjectsPass.cs:133` =
  `SRPDefaultUnlit` / `UniversalForward` / `UniversalForwardOnly`），而 Unity 的 `DrawRenderers`
  **每个 tag 槽位只取一个 pass** → 同名的第二个 Pass 被静默丢弃。
- **修法**：描边 Pass 改标 `"LightMode" = "SRPDefaultUnlit"`（URP 列表里的独立槽位，也是 URP 官方"额外 unlit pass"的常规做法）。
- **教训**：URP 里"加一个 pass"不等于"这个 pass 会被执行"。要在 shader 里加额外 pass，必须给它一个
  URP ShaderTagId 列表内**独立且未被占用**的 `LightMode`；且这种失败是**完全静默**的，只能靠出图/逐档调试发现。

### 八-3 采集脚本的两个坑（编辑器行为，非 shader 问题）

- **编辑器暂停时采集会挂死**：play mode 暂停 → `Time.frameCount` 不推进 → "等真实渲染帧"永远等不到。
  脚本现在检测到暂停会 `Debug.LogError` 明确退出。参考：无头验证台/自动化里凡是"等帧"的逻辑都要考虑暂停态。
- **`ScreenCapture.CaptureScreenshot` 是异步的**：它在**当前帧末尾**取像素，所以"请求截图"之后**不能立刻**复位改档状态。
  实测曾把最后一档（档 4）拍成复位后的档 0 的样子。脚本现在用一个独立的收尾阶段延迟复位。

