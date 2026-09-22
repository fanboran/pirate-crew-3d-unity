# Assets/Art/Rendering —— 渲染资产与「写实栈退役」的处置依据

> **这份文档解决什么问题**：`Assets/Art/Rendering/` 下的 `.asset` 是**二进制感的 Unity 资产**——
> Unity 保存时会重写整个 YAML 并丢掉手写注释，所以"为什么关掉这一项、为什么保留那一项"的
> 依据不能写在资产文件里，只能写在资产旁边。本文件就是那些依据的落点，
> 每一行都能对到一个可核对的出处（文档小节 / 文件行号）。
>
> 口径来源：[渲染管线-等距像素卡通.md](../../../../docs/技术/渲染/渲染管线-等距像素卡通.md)（渲染篇）、
> [美术翻新立项任务书](../../../../docs/技术/美术翻新-等距像素卡通立项任务书.md) §3 处置表、
> [美术风格指南](../../../../docs/设计/美术风格指南.md)。

---

## 1. 目录内容

| 资产 | 作用 | 谁引用它 |
| --- | --- | --- |
| `BattleGlobalVolumeProfile.asset` | 战斗场景的 Global Volume 后处理栈（六个组件，现全部停用） | `Assets/Scenes/Battle.unity`（唯一消费者） |

URP 的 Renderer / URPAsset 在 `Assets/Settings/URP/`，不在这里；Renderer Feature 的挂载/摘除脚本在
`Assets/Editor/`（`PixelationInstaller` 装、`UrpRendererFeatureRetire` 卸）。

---

## 2. `BattleGlobalVolumeProfile` 逐项处置（任务书 §3 表 M2a 行）

**处置结论：六个组件全部 `active: 0`（停用），参数与 override 状态原样保留，组件不删。**

「停用而不删」的理由是任务书 M1 工作项 1f 要求出「后处理栈开/关」对照图，
裁决点 #6（Tonemap / ColorAdjustments 去留）要靠那张图裁；删掉组件就没有对照物了。
M2a 定案后再删。停用是幂等的、可一键还原的中间态。

### 2.1 两类后处理，两条不同的处置理由（这是本节的核心论证）

像素管线的顺序是「几何 → 描边 → 全分辨率后处理 → point 降采 640×360 → nearest 放大」，
点采样降采把所有后处理都推到同一道闸门前，但两类后处理的**失效方式完全不同**，
不能用一句话概括（渲染篇 §1 红线 4 及其中注释）：

| 类别 | 成员 | 与点采样降采的关系 | 处置 |
| --- | --- | --- | --- |
| **逐像素颜色算子** | Tonemapping / ColorAdjustments / WhiteBalance / ShadowsMidtonesHighlights | 逐像素函数**与点采样可交换**（先变换再降采 ≡ 先降采再变换），所以它们**不破坏像素网格**，也不产生块状伪影 | 仍停用，但理由是**锁板纪律**（见 §2.3），不是几何 |
| **邻域算子** | Bloom / Vignette | 在**全分辨率域**做跨像素卷积/渐变，结果是一层柔和的连续明暗场；point 降采把它**量化进 360p 网格** ⇒ 得到的是"块状辉光"与"色带环状的暗角" | 停用，理由是它与像素块感互斥（渲染篇 §6 Bloom 行） |

这条区分值得写下来，因为反过来的推论同样重要：**"后处理必须全部放在 360p 域内"是错的**
（渲染篇 §1 红线 4 只要求"上采样之后零颜色操作"）。放在降采之前的逐像素算子是无害的；
真正的判据是「这个算子的作用半径是否跨过 RT 像素格」。

### 2.2 逐组件依据

| 组件（`.asset` 里的 m_Name） | 停用理由 | 出处 |
| --- | --- | --- |
| `Bloom`（intensity 0.42 / threshold 1.05） | 邻域算子。光晕在 360p 网格里量化成块状辉光；**柔光机制已改由手绘径向贴片承担**（`ToonPilotSetup.EnsureRadialTexture`：4 档量化 + 4×4 Bayer 中点归一抖动烘进 PNG），不依赖 bloom 也不加附加光。裁决 D1 的另一候选（低分辨率域内 bloom）若在 M1 被选中，本组件在此一键开回 | 渲染篇 §6、§7「柔光与星芒」；`ToonPilotSetup.cs` 夜色档 |
| `Vignette`（intensity 0.22） | 邻域算子。暗角是覆盖全屏的明度渐变，降采后量化成 2~3 圈色带环 —— 一个**由后处理偷偷产生、不受调色板约束**的色阶，与"色带由着色器显式控制 + 锁板"直接冲突 | 渲染篇 §6「Vignette 慎用」 |
| `Tonemapping`（Neutral） | 逐像素算子，几何上无害；但它是**非线性亮度重映射**，会把板内色推到板外（渲染篇 §7 帧级调色板演进项点名 tonemap 是漂移源之一），且 `m_SupportsHDR=0` 下 Neutral 只做轻微高光滚降，收益趋近于零 | 渲染篇 §6「Color Grading LUT 可轻用」；§7；任务书裁决点 #6（提案全拆） |
| `ColorAdjustments`（contrast 12 / saturation 10） | 逐像素算子，但 **saturation +10 把贴图色直接推离板**（锁板纪律的头号敌人）；contrast +12 会把赛璐璐色带的阶跃二次压缩 —— 色带要硬切（渲染篇 §4.1），不要被后处理再拉一次 | 渲染篇 §4；任务书裁决点 #6 |
| `WhiteBalance`（temperature 8） | 逐像素算子；全局暖偏移会让**同一个板色在不同昼夜档之间漂移**，而昼夜色档已由三档天空色板 + 扁平环境光承担（美术指南 §3.1 全局光约定） | 美术指南 §3.1；任务书裁决点 #5 |
| `ShadowsMidtonesHighlights`（shadows 偏冷 / highlights 偏暖） | 逐像素算子；它与「**替换式预制暗部色**」是同一件事的两种实现，而渲染篇 §4.2 明令暗部**不在后处理里做色调变换**（产品级管线一律用材质参数/ramp 档表达） | 渲染篇 §4.2 |

### 2.3 保留项

- **组件与参数全部保留**（只是 `active: 0`）：M1 对照图 + 裁决点 #6 的对照物。
- **Volume 资产本身保留**：Battle.unity 的 Global Volume 引用它；M2a 定案若"全拆"，
  那时再摘掉场景里的 Volume 组件与资产（属 M5 死资产清仓）。

---

## 3. `_CameraDepthTexture` / `_CameraOpaqueTexture` 依赖调查（任务书 §3 表 M2c 行）

**处置结论：两项均保持开启（`m_RequireDepthTexture: 1` / `m_RequireOpaqueTexture: 1`），
不盲关。** 因为**现役画面里有人在用**，关掉会让水面当场变黑（采样返回黑），
违反"任何时刻都有能看的画面"这条执行支柱（任务书 §0.3 支柱 1）。

### 3.1 逐消费者清单（grep 实测，`文件:行号`）

| 消费者 | 用法 | 状态 |
| --- | --- | --- |
| `Assets/Art/Shaders/PirateWater.shader:276` | `#include DeclareOpaqueTexture.hlsl` → `SampleSceneColor()`（屏幕空间折射） | **现役**：材质 `Assets/Art/Materials/Environment/Water_Ocean.mat` 被 `Assets/Scenes/Battle.unity` 引用 |
| `Assets/Art/Shaders/PirateWater.shader:273` | `DeclareDepthTexture.hlsl` → `SampleSceneDepth()`（浅深水过渡/岸边泡沫） | 同上 |
| `Assets/Art/Shaders/Ocean/PirateOcean.shader:250` | `DeclareOpaqueTexture.hlsl` → `SampleSceneColor()`（水下透射） | **现役**：材质 `Assets/Art/Materials/Environment/Ocean_Water.mat` 被 `Assets/Scenes/Battle.unity` 引用 |
| `Assets/Art/Shaders/Ocean/PirateOcean.shader:247` | `DeclareDepthTexture.hlsl` → `SampleSceneDepth()`（岸线/深度带） | 同上 |
| `Assets/Art/Shaders/PirateTerrain.shader:627`（DepthOnly pass） | **写**深度（不读）；`PirateWater` 的岸线依赖它写入 | 现役，但"写"不要求开关打开 |
| `Assets/Art/Scripts/.../ScenePropGeometry.cs:622`（注释） | 说明浅水线索之一 | 注释，非消费者 |
| `OutlineRendererFeature`（全屏 Sobel） | 读 `_CameraDepthTexture` 做梯度 | **已退役**（本轮摘除，见 §4），深度消费者因此少一个 |

### 3.2 关闭的前置条件（登记，不在本轮执行）

两项可以关的**唯一**条件：水面 shader 的两条采样路径都已退场。对应里程碑：

- `PirateWater` / `WaterTessellator`：任务书 §3 表列"M5 删除（无消费者）"——注意该表说的是
  **shader 资产**无消费者，而 `Water_Ocean.mat` 仍在 Battle 场景里挂着，所以删除前必须先在场景里
  换掉那处引用；
- `PirateOcean`：任务书 §3 表列 M2f 重做为像素海面，重做后不再需要屏幕空间折射/透射。

两条都落地后，把两项开关改 0 并**同一次提交里**重跑出图判据；在那之前，
这两项是"纯开销"（各一次全屏 copy）但**不是死配置**——不要按"审计说没人用"直接关。

---

## 4. Renderer Feature 现状（两档 `Assets/Settings/URP/PC_*_Renderer.asset`）

| Feature | Balanced | Performant | 依据 |
| --- | --- | --- | --- |
| `PirateCrew Pixelation`（`PixelationRendererFeature`，RT 高 360） | 挂载 | 挂载 | 风格基底，两档都要（渲染篇 §3） |
| `PirateCrew Outline`（`OutlineRendererFeature`，全屏 Sobel） | **已摘除** | **已摘除** | 与像素块感互斥、职责由反壳描边接管（任务书 §3 表 M2b 行） |
| `PirateCrew SSAO`（`BalancedSsaoInstaller`） | 见 `BalancedSsaoInstaller.cs` | 未装 | 退役项登记在 M2b，本轮未动（见下方"遗留"） |

摘除动作由 `Assets/Editor/UrpRendererFeatureRetire.cs` 的 `RetireAll` 执行（幂等、含孤儿子资产清理），
**原来的挂载器已改造成退役器**——否则任何人重跑一次挂载脚本就会把退役项装回去。
`OutlineRendererFeature.cs` 与 `PirateOutlinePost.shader` 本体保留（不影响画面：没有 Renderer 引用它）。

**遗留（不在本目录文件域内，登记待办）**：

1. `ProjectSettings/GraphicsSettings.asset` 的 Always Included Shaders 若仍登记
   `PirateOutlinePost.shader`，属死引用 → M5 清仓项；
2. `Assets/Editor/BattleSceneLighting.cs:791-830`（`ConfigureUrpAsset`）会在执行时**强制写回**
   `msaaSampleCount=2`、`colorGradingMode=HDR`、`shadowDistance>=100`、`cascadeCount=4` ——
   其中 MSAA 与像素口径直接冲突（渲染篇 §6 要求 MSAA 0）。这是 M0「配置漂移」的**根因**：
   资产当前值是 MSAA 0（已修），但只要有人跑一次该路径就会被静默翻回。修法（改那个脚本或用
   退役开关包起来）属其它轨道文件域，登记不改。
   `m_ShadowDistance: 224` / `m_ShadowCascadeCount: 4` / `m_MainLightShadowmapResolution: 2048`
   在 `m_MainLightShadowsSupported: 0`、`m_AdditionalLightShadowsSupported: 0` 下是**死配置**，
   清理同属该脚本的处置范围；
3. `BattleGlobalVolumeProfile` 的六个组件在 M2a 定案后删除（连同 Battle 场景的 Volume 组件）。
