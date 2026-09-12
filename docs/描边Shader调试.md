# 描边 Shader 调试说明（M2 单位选中 / 悬停描边）

> 关联交付物：
> - `pirate-crew/Assets/Art/Shaders/PirateOutline.shader` —— 单体描边（inverted hull，含 5 档 `_DebugMode`）
> - `pirate-crew/Assets/Art/Shaders/PirateOutlinePost.shader` —— 全屏后处理描边（mask Sobel + 虚线，含 5 档 `_DebugMode`）
> - `pirate-crew/Assets/Scripts/PirateCrew/Rendering/OutlineRendererFeature.cs` —— 后处理 C# 侧（URP RendererFeature）
> - `pirate-crew/Assets/Editor/OutlineDebugCapture.cs` —— 逐档截图采集脚本
> - 设计参照：Godot 版 `outline_hover.gdshader` / `outline_selected.gdshader` / `outline_post.gdshader` 及 `pirate_base.gd` 接线

---

## ⚠️ 截图待补跑（显式标注，不要当成已完成）

**本会话没有产出任何真实截图。** 本次交付是「shader + `debug_mode` 分层 + 采集脚本 + 本文档」的**代码级交付**。

原因：本仓库的开发环境用 `-batchmode -nographics` 做无头验证，没有可用渲染路径：

- `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`，`ScreenCapture` / `Camera.Render` 拿不到真实像素；
- 本环境**不带** `-nographics` 会卡在 GfxDevice 创建（AGENTS.md 已记录），所以也无法用「无头但不加 -nographics」绕过。

采集脚本 `OutlineDebugCapture.CaptureAll()` 在检测到无渲染路径时会 **`Debug.LogError` 明确报错并返回**，不会静默失败、不生成占位图、不伪造截图。

**补跑方式（必须在有图形界面的 Unity 编辑器会话里）：**

1. 用 Unity Hub / 编辑器打开 `pirate-crew/` 工程（**不是** batchmode）；
2. 打开含单位的场景（当前是 `Assets/Scenes/Battle.unity`），确保 Game View 可见；
3. 给单位挂上 `PirateOutline` 材质（若只调后处理，则在 URP Renderer 资产上加 `OutlineRendererFeature` 并指定 shader）；
4. 菜单栏 → **`PirateCrew/Rendering/采集描边调试截图`**；
5. 到 `<仓库根>/export/outline-debug/` 确认 5 张 PNG，逐张对照下方「每档应看到什么」。

> 若确实需要命令行触发（仅在**有 GPU + 有图形界面**的机器上有意义）：
> ```bash
> "F:/Unity/2022.3.62f1c1/Editor/Unity.exe" -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" \
>   -executeMethod PirateCrew.EditorTools.OutlineDebugCapture.CaptureAll -logFile -
> ```
> **注意**：这里刻意**不加** `-batchmode -nographics`（截图必须有渲染路径）。本开发环境不能这样跑。
>
> 另：`export/` 目前**不在** `.gitignore` 里，补跑后注意别把 PNG 误提交（是否忽略由项目决定，本次未改 `.gitignore`）。

### 截图文件名 ↔ 中文含义对照

采集脚本文件名用短 slug（`ScreenCapture` 对非 ASCII 路径在部分平台会写盘失败），含义如下：

| 文件 | `_DebugMode` | 含义 |
| --- | --- | --- |
| `debug-mode-0-normal.png` | 0 | 正常合成（本体 + 描边） |
| `debug-mode-1-base-only.png` | 1 | 只显示本体不描边 |
| `debug-mode-2-expanded-hull.png` | 2 | 只显示法线外扩结果 |
| `debug-mode-3-outline-mask.png` | 3 | 只显示描边掩码（单色） |
| `debug-mode-4-depth-normal-raw.png` | 4 | 深度/法线原始数据 |

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

- **应看到**：本体消失，只剩一圈**品红色**的"胖轮廓"（外扩后的背面壳），无描边颜色、无虚线。
- **常见异常**：
  - 本体轮廓"发胖"但看不到品红 → 说明看到的是本体在档 1 的残留？不对，档 2 本体 Pass 会 `discard`；若仍见本体，是档位写错。
  - 发胖过量（壳离本体很远）→ `_OutlineWidth` 太大，或 `_OutlineExpandMode = 1` 时 `_OutlineWidth` 单位是**米**而没从小数改成 0.01~0.05。
  - **壳上有破洞 / 裂缝** → 模型法线被压平（相邻顶点法线不连续，多为硬边 / 未导入平滑法线 / 法线贴图烘焙进顶点后丢失），外扩方向在裂缝处不一致。这是 inverted hull 的经典缺陷；缓解手段：改用平滑法线（第二套 UV/顶点色存平滑法线）或改用后处理描边。
  - 品红壳只在物体一侧出现 → `Cull Front` 生效但法线方向反了（模型是内翻法线）。

### 档 3 —— 只显示描边掩码（单色剪影）

- **应看到**：一圈**不透明、无流动**的纯青色轮廓（把描边当实线画），像给物体描了实线。忽略虚线、忽略 hover 的半透明。
- **常见异常**：
  - 轮廓断断续续 → 同档 2 的法线不连续问题（外扩位移在硬边处不一致）。
  - 完全没有轮廓 → `Cull Front` 写反（改 `Cull Back` 试）；或 `_OutlineState = 0` 且 `_OutlineColor` 的 alpha 为 0（本档强制 alpha=1，所以更可能是 `Cull` 问题）。
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
| `#include ".../universal/ShaderLibrary/GlobalIllumination.hlsl"` | ✅ [URP] `ShaderLibrary/GlobalIllumination.hlsl` 存在（其内部 include `RealtimeLights.hlsl` 与 core `EntityLighting.hlsl`） |

> 取舍说明：本体光照只需 `GetMainLight()` + `SampleSH()`，所以只引较"轻"的 `GlobalIllumination.hlsl`，
> 而**不用** URP 更全的 `Lighting.hlsl`（后者会连带 BRDF / DBuffer / Debugging3D 等本 shader 用不到的模块）。
> 两者都能拿到 `Light` / `GetMainLight` / `SampleSH`，缩小编译面只为降低 shader 编译失败风险。

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
mkdir -p external/harness-rendering && cp external/m2-harness/M2Harness.csproj external/harness-rendering/
cd external/harness-rendering
dotnet build M2Harness.csproj -p:HarnessScope=Rendering
# → 已成功生成。0 个警告，0 个错误

# Editor 侧（OutlineDebugCapture.cs）：不在 Rendering scope 内，另建临时验证台
cd /f/VSCode/pirate-crew-3d-unity/external/harness-editor
dotnet build VerifyEditorOutline.csproj
# → 已成功生成。0 个警告，0 个错误
```

> **Shader 本身无法在本环境编译验证**（Unity 的 shader 编译器只在编辑器内跑，且不许启动 Unity 进程）。
> 因此 §5.1–5.4 的逐条核对就是本环境能做到的最大程度；**首次在有图形界面的编辑器里导入 shader 时，务必看 Console 有无 shader 编译错误**，这是唯一的最终验证。

---

## 六、接线方式（Unity 侧，M2 集成时照此做）

### 6.1 单体描边（必须项）

1. 用 `PirateOutline` shader 建材质（`Assets/Art/Materials/`），设好 hover/selected 两套色与宽。
2. 挂到单位（或挂到单位的"描边复制网格"上）。若沿用 Godot 的做法（复制网格），把单位本体保持原 URP/Lit 材质，描边网格只挂 `PirateOutline`。
3. 选中 / 悬停系统每帧写 `_OutlineState`：
   - 无状态 → `0`（或 `_OutlineAlpha = 0` 淡出）
   - 悬停 → `1`
   - 选中 → `2`
   公共出口建议放 `PirateCrew.PirateCrew.Rendering` 下一个 `OutlineStateBinder`（本次未做，待选中系统落地时补）。

### 6.2 全屏后处理描边（尽力项，已交付代码）

1. 打开 `Assets/Settings/URP/PC_Balanced_Renderer.asset`（与 Performant 各一份）→ Inspector → **Add Renderer Feature** → `OutlineRendererFeature`。
2. 设置：
   - `Outline Post Shader`：拖 `Assets/Art/Shaders/PirateOutlinePost.shader`（**建议显式拖入**；留空时走 `Shader.Find("PirateCrew/PirateOutlinePost")`，出包可能被剔除）。
   - `Mask Layer`：把"可选中单位"放到专用 Layer（如新建 `SelectionOutline`），这里只勾它。默认 `Everything` 只是调试方便。
   - `Render Pass Event`：`BeforeRenderingTransparents`（默认）。
   - `Outline Color` / `Edge Threshold` / `Dash *`：按第四节调。
3. 选中系统把选中单位**临时切到 mask Layer**（或反过来只让选中单位留在该 Layer），Feature 就会自动为其生成虚线描边。

### 6.3 程序集说明（本次的一处结构性决策，需知会）

`OutlineRendererFeature.cs` 用到 URP 类型，但 `Assets/Scripts/PirateCrew.Runtime.asmdef` 的 `references` 为**空**，无法引用 URP。
为不修改既有 asmdef（任务禁令），本次**新增**了 `Assets/Scripts/PirateCrew/Rendering/PirateCrew.Rendering.asmdef`（引用 `Unity.RenderPipelines.Universal.Runtime` + `Unity.RenderPipelines.Core.Runtime`），把该目录划为独立程序集 `PirateCrew.Rendering`。
- 影响范围：仅 `Assets/Scripts/PirateCrew/Rendering/**`（本次新建目录）；`PirateCrew.Runtime` 与其它模块不受影响。
- 这是**新增**文件，未改动任何既有 `.asmdef`。

---

## 七、待办 / 已知限制

1. **真实截图未产出**（见文首），5 张 PNG 待有图形界面的编辑器会话补跑。
2. `PirateOutline.shader` **没有 ShadowCaster Pass**：作为独立材质使用时单位不投影。若需要，补一个 `LightMode = "ShadowCaster"` 的极简 Pass（注意 `_LightDirection` / `ApplyShadowBias`）或让本体继续用 URP/Lit、描边走复制网格。
3. `OutlineRendererFeature` **需要手工加到 URP Renderer 资产**（本次未改 `.asset`，避免动到其他人的数据）。若希望自动化，可仿 `UrpSetup.cs` 加一个 Editor 方法。
4. `outline.gdshader` 的 `outline_near_boost`（近处增粗）未移植。
5. 后处理描边的 `_DashLength/_DashGap` 是**像素**单位，分辨率变化时观感会变；若要分辨率无关需改成 NDC 单位。
