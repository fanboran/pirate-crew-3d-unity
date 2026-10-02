# Unity API 陷阱与参照库公约

> **这份文档是什么**：① 写 Unity 代码前必读的**本机 API 陷阱清单**（R1–R9，每条附本地核对命令与结论，
> 防照抄网上 Unity 6 / 旧版本教程踩坑）；② 参照库**借鉴与许可证公约**（只借鉴模式，不搬运代码）；
> ③ `external/` 工作台目录的**总登记与使用公约**。
> 核对环境：Unity 2022.3.62f1c1 + URP 14.0.12 + Cinemachine 2.9.7（本地 PackageCache）。
> 本地权威核对源：`F:\Unity\2022.3.62f1c1\Editor\Data\Managed\UnityEngine\*.xml`、
> `pirate-crew\Library\PackageCache\com.unity.cinemachine@2.9.7\`、
> `pirate-crew\Library\PackageCache\com.unity.render-pipelines.universal@14.0.12\`。
> 所有结论以本机 DLL/XML/包源码核对为准，不凭教程记忆。

---

## 1. API 陷阱清单（R1–R9）

### R1. `Rigidbody.velocity`（2022.3）≠ `Rigidbody.linearVelocity`（Unity 6）

核对命令：

```bash
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="P:UnityEngine\.Rigidbody\.(velocity|linearVelocity)"' "$MG/UnityEngine.PhysicsModule.xml"
grep -c "linearVelocity" "$MG/UnityEngine.PhysicsModule.xml"
grep -c "linearVelocity" "$MG/UnityEngine.PhysicsModule.dll"
```

结论：XML 只命中 `P:UnityEngine.Rigidbody.velocity`（PhysicsModule.xml 第 4182 行），`linearVelocity` 计数 XML=0、DLL 二进制=0。**本工程 2022.3.62f1 必须写 `rb.velocity`；`linearVelocity` 只在 Unity 6+ 存在，照抄 Unity 6 教程会编译失败。**

附带同类改名陷阱（同样已核对）：
- `Rigidbody.drag`（2022.3，XML 存在 `P:UnityEngine.Rigidbody.drag`）↔ Unity 6 `linearDamping`。本地 `linearDamping` 只命中 `P:UnityEngine.ArticulationBody.linearDamping`（PhysicsModule.xml:127），**不是 Rigidbody 的成员**，不要混用。
- `Rigidbody.angularDrag` 同理 ↔ Unity 6 `angularDamping`；本地 `angularDamping` 只属 `ArticulationBody`（PhysicsModule.xml:22）。

### R2. Cinemachine 2.9.7 用 `CinemachineVirtualCamera`，没有 `CinemachineCamera`

核对命令：

```bash
CM="F:/VSCode/pirate-crew-3d-unity/pirate-crew/Library/PackageCache/com.unity.cinemachine@2.9.7"
grep -n "public class CinemachineVirtualCamera" "$CM/Runtime/Behaviours/CinemachineVirtualCamera.cs"
grep -rn "class CinemachineCamera\b" "$CM"
grep -n '"version"' "$CM/package.json"
```

结论：`CinemachineVirtualCamera` 在 `Runtime/Behaviours/CinemachineVirtualCamera.cs:59`；`class CinemachineCamera` 无匹配（仅存在无关的 `CinemachineCameraOffset` 扩展类）；包版本 `2.9.7`。**Unity 6 的 `CinemachineCamera` / `Unity.Cinemachine` 命名空间写法在本工程不可用，必须用 `Cinemachine` 命名空间 + `CinemachineVirtualCamera`。**

已核对的 2.9.7 关键成员（可直接用）：
- `CinemachineVirtualCameraBase.Priority`（int，`Runtime/Core/CinemachineVirtualCameraBase.cs:357`；序列化字段 `m_Priority` 在第 59 行）。
- `CinemachineVirtualCamera.GetCinemachineComponent<T>()`（`Runtime/Behaviours/CinemachineVirtualCamera.cs:331`）。
- `CinemachineVirtualCamera.m_Follow` / `m_LookAt`（第 81 / 70 行，另有 `Follow`/`LookAt` 覆写属性在第 124/115 行）。
- `CinemachineFramingTransposer.m_CameraDistance`（`Runtime/Components/CinemachineFramingTransposer.cs:127`）。
- `CinemachineBrain`（`Runtime/Behaviours/CinemachineBrain.cs:90`）、`m_IgnoreTimeScale`（第 113 行）、`ActiveVirtualCamera`（第 516 行）。

### R3. `LineRenderer.SetVertexCount` 已过时 → 用 `positionCount` + `SetPositions`

核对命令：

```bash
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="[^"]*(positionCount|SetVertexCount|SetPositions)[^"]*"' "$MG/UnityEngine.CoreModule.xml" | sort -u
```

结论：本地同时存在 `P:UnityEngine.LineRenderer.positionCount`、`M:UnityEngine.LineRenderer.SetPositions(UnityEngine.Vector3[])` 与遗留的 `M:UnityEngine.LineRenderer.SetVertexCount(System.Int32)`。**本工程应写 `line.positionCount = n; line.SetPositions(arr);`**，避免过时 API 警告，也便于以后升级。

### R4. 2D 物理 API → 3D 物理 API

核对命令与结论：

```bash
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="P:UnityEngine\.Physics\.gravity"' "$MG/UnityEngine.PhysicsModule.xml"          # 命中
grep -oE '<member name="M:UnityEngine\.Rigidbody\.AddForce\(UnityEngine\.Vector3,UnityEngine\.ForceMode\)"' "$MG/UnityEngine.PhysicsModule.xml"  # 命中
grep -oE '<member name="F:UnityEngine\.ForceMode\.Impulse"' "$MG/UnityEngine.PhysicsModule.xml"       # 命中
```

- 本工程 URP 3D + 内置 PhysX（AGENTS.md），**不能用 `Physics2D.gravity` / `Rigidbody2D` / `ForceMode2D` / `CircleCollider2D` / `Physics2D.OverlapPoint`**。
- 对照替换表：`Physics2D.gravity` → `Physics.gravity`；`Rigidbody2D.velocity` → `Rigidbody.velocity`；`Rigidbody2D.isKinematic` → `Rigidbody.isKinematic`；`ForceMode2D.Impulse` → `ForceMode.Impulse`；`Physics2D.OverlapPoint` → `Physics.Raycast` / `Physics.OverlapSphere`；`Vector2.Angle` → `Vector3.Angle`。
- `Rigidbody.AddForce(Vector3, ForceMode)` 与 `ForceMode.Impulse/Force/Acceleration/VelocityChange` 均已核对存在（PhysicsModule.xml）。

### R5. `Application.LoadLevel` 已过时 → 用 `SceneManager.LoadScene` 或本工程 `SceneLoader`

核对命令：

```bash
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="M:UnityEngine\.Application\.LoadLevel[^"]*"' "$MG/UnityEngine.CoreModule.xml"      # 存在但过时
grep -oE '<member name="M:UnityEngine\.SceneManagement\.SceneManager\.LoadScene\([^"]*"' "$MG/UnityEngine.CoreModule.xml"  # 存在
```

结论：**本工程禁止用 `Application.LoadLevel`，改用 `SceneManager.LoadScene` 或现有 `PirateCrew.Core.SceneLoader`（走 EventBus 场景频道），保持导航统一。**

### R6. URP 14 渲染特性 API：`cameraColorTargetHandle` / `Blitter.BlitCameraTexture` / `RTHandle`

核对命令：

```bash
PC="F:/VSCode/pirate-crew-3d-unity/pirate-crew/Library/PackageCache"
URP="$PC/com.unity.render-pipelines.universal@14.0.12"; CORE="$PC/com.unity.render-pipelines.core@14.0.12"
grep -n "public RTHandle cameraColorTargetHandle" "$URP/Runtime/ScriptableRenderer.cs"                 # :417
grep -n "public RenderTargetIdentifier cameraColorTarget$" "$URP/Runtime/ScriptableRenderer.cs"        # :398（旧）
grep -n "public static void BlitCameraTexture" "$CORE/Runtime/Utilities/Blitter.cs"                    # :377 带 Material 重载
grep -n "public static bool ReAllocateIfNeeded" "$URP/Runtime/RenderingUtils.cs"                       # :596
grep -n "public struct RenderingData" "$URP/Runtime/UniversalRenderPipelineCore.cs"                    # :84
grep -n "public class RTHandle" "$CORE/Runtime/Textures/RTHandle.cs"                                   # :58
grep -n "public abstract partial class ScriptableRenderPass" "$URP/Runtime/Passes/ScriptableRenderPass.cs"  # :173
```

结论：
- URP 14 的标准写法是 `cameraColorTargetHandle`、`RTHandle`、`Blitter.BlitCameraTexture(cmd, src, dst, material, pass)`、`RenderingUtils.ReAllocateIfNeeded`、`RenderPassEvent`、`EnqueuePass`，与本工程 URP 14.0.12 匹配。
- **不要用** `cameraColorTarget`（`ScriptableRenderer.cs:398`，旧 `RenderTargetIdentifier` 路径）和 Unity 6 的 RenderGraph API；也不要引用 URP 15+ 才有的 `UniversalRenderPipeline.RenderGraph` 写法。
- `Blitter.BlitCameraTexture` 带 Material 的重载在 core 包 `Blitter.cs:377`，`Blitter` 类在 `Blitter.cs` 内（`public static class Blitter`）。

### R7. 输入系统：本工程用 legacy `Input`（manifest 无 Input System 包）

核对命令：

```bash
grep -i "inputsystem" "F:/VSCode/pirate-crew-3d-unity/pirate-crew/Packages/manifest.json"   # 无输出
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="[PM]:UnityEngine\.Input\.(GetMouseButton|GetAxisRaw|GetKey|mouseScrollDelta|mousePosition)[^"]*"' "$MG/UnityEngine.InputLegacyModule.xml" | sort -u
```

结论：`Input` 位于 `UnityEngine.InputLegacyModule`，本机 XML 确认存在 `Input.GetMouseButton(int)`、`GetMouseButtonDown/Up`、`GetAxis(String)`、`GetAxisRaw(String)`、`GetKey(String/KeyCode)`、`Input.mousePosition`、`Input.mouseScrollDelta`。工程 manifest 无 `com.unity.inputsystem`，因此**用 `Input.*` 即可**；新 Input System 写法会引入新包依赖，不要照抄。

### R8. `Physics.Raycast` 与相机投影签名核对

核对命令：

```bash
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="M:UnityEngine\.Physics\.Raycast\([^"]*"' "$MG/UnityEngine.PhysicsModule.xml" | head
grep -oE '<member name="M:UnityEngine\.Camera\.(ScreenToWorldPoint|ScreenPointToRay|WorldToScreenPoint)\([^"]*"' "$MG/UnityEngine.CoreModule.xml" | sort -u
```

结论：存在 `Physics.Raycast(Ray, out RaycastHit, float, int, QueryTriggerInteraction)`、`Physics.Raycast(Vector3, Vector3, out RaycastHit, float, int, QueryTriggerInteraction)`；`Camera.ScreenToWorldPoint(Vector3)`、`Camera.ScreenPointToRay(Vector3)`、`Camera.WorldToScreenPoint(Vector3)` 均存在。选型脚本按这些签名写即可；`Physics.OverlapSphere` 亦可用于「选中附近单位」（未逐一列出，如使用再核对）。

### R9. `Cinemachine` 命名空间与 URP `Shader.Find` 打包风险（经验项，非编译风险）

- Cinemachine 2.9.7 包内命名空间确为 `Cinemachine`（`CinemachineVirtualCamera.cs` 声明在 `namespace Cinemachine` 下）。
- 自实现屏幕空间描边 shader 时，`Shader.Find("Hidden/...")` 引用的 shader 必须在 `Always Included Shaders` 登记，否则构建后材质为 null。
- 全局屏幕空间描边**不支持 MSAA**；本工程 URP Asset 若开了 MSAA，需切 FXAA/SMAA。

---

## 2. 参照库借鉴与许可证公约

本项目策略（AGENTS.md 要求）：**只借鉴模式，不搬运代码**。已落盘参照库均为 MIT（clone 后本地核对 LICENSE 文件）。

### 2.1 允许的「借鉴模式」（安全）

- 架构与职责划分：状态机三段式、CameraTarget 中转、输入提供者接口、事件驱动解耦。
- 公式与数值：初速比例、抛物线采样、预测线段数、屏幕边缘平移速度——数学与玩法常识。
- 参数与阈值：拖拽最大距离、静止判定超时、速度随缩放的线性关系。
- 算法思路：深度+法线双阈值 Roberts Cross 描边、LayerMask 过滤、`Mathf.Clamp` 边界、`Vector3.Lerp` 目标锁定。
- 用文字/表格描述实现，再用自己的命名与结构重写。

### 2.2 构成「代码复制」（禁止）

- 整段拷贝任何 `.cs` 文件或大段方法体（即使 MIT 允许，也违反本项目方针）。
- 拷贝 ShaderGraph 资产——资产复制且带编辑器 GUID 依赖，不能直接移植；如需描边，自己写 HLSL/SG。
- 拷贝参照库内嵌的第三方插件（如 Demigiant DOTween）——其许可与外层仓库不同，严禁搬运；本工程也不引入 DOTween。
- 拷贝参照库的场景/Prefab/动画/贴图等二进制或 YAML 资产。
- 将参照库目录放进入库路径：`external/` 已 gitignore，参照库不入库；如需引用，只在文档里写路径与模式。

### 2.3 其它

- 无 LICENSE 文件的仓库（如 `Unity-Technologies/UniversalRenderingExamples`）不 clone、不引用。
- 所有参照库保持原样放在 `external/`（gitignored），不入库、不修改。

---

## 3. external/ 目录总登记与使用公约

> 本节登记 `external/` 的定位、现存内容与使用公约（公约已确认执行，并已同步回写 AGENTS.md：
> 无头验证台副本路径、跑测产物路径、顶层目录结构注释、参照库登记要求）。

### 3.1 定位与边界

`external/` 是 gitignore 的工作台目录，按定位只应存放四类内容：**参照库、无头验证台母本、Flash 逆向材料、Blender 建模源文件**。
不许跑测产物（日志、test-results、构建输出）堆积——见 3.3。

### 3.2 现存登记

| 路径 | 内容 | 上游 / 说明 |
| --- | --- | --- |
| `core-reference/eventbus-adammyhre` | Unity-Event-Bus | <https://github.com/adammyhre/Unity-Event-Bus>（M1 EventBus 参照） |
| `core-reference/savesystem-shapedbyrain` | save-load-system | <https://github.com/shapedbyrainstudios/save-load-system>（M1 存档参照） |
| `core-reference/sceneloader-mygamedevtools` | scene-loader | <https://github.com/mygamedevtools/scene-loader>（M1 场景流转参照） |
| `core-reference/scenetransition-lightgive` | TransitionManager | <https://github.com/LightGive/TransitionManager>（M1 场景切换参照） |
| `m2-combat-reference/angry-birds-slingshot` | 弹弓状态机+预测线 | <https://github.com/dgkanatsios/AngryBirdsStyleGame> |
| `m2-combat-reference/trajectory-dots` | 点阵抛物线轨迹 | <https://github.com/herbou/Tuto_DrawTrajectory> |
| `m2-combat-reference/urp-outlines` | URP 屏幕空间描边 | <https://github.com/Robinseibold/Unity-URP-Outlines> |
| `m2-combat-reference/rts-camera-cinemachine` | RTS 相机（Cinemachine） | <https://github.com/Nickk888SAMP/RTSCameraController-Cinemachine>（sparse checkout：Assets/Packages/ProjectSettings） |
| `comm-reference/`（预留） | 模块间通信调研参照：MessagePipe / R3 / VContainer / Zenject（+官方 SO 通道 PaddleGameSO） | **URL 研读、未落盘**；对照结论与裁决见 [架构总览 §5.6](架构总览.md)。代理恢复后 `--depth 1` 补克隆 |
| `fluid-ref/FLIP` | Unity_FLIP_Fluid_Simulation | <https://github.com/lamp-cap/Unity_FLIP_Fluid_Simulation>（水体参照） |
| `fluid-ref/HPWater` | HPWater | <https://github.com/AshenOneArt/HPWater>（水体参照） |
| `swf-decompile/` + `tools/` | game.swf、levels_all.json、ffdec 反编译器 | Flash 原版逆向材料与工具链，逆向文档的原始依据 |
| `harness/` | 无头验证台母本 | 用法见 `external/harness/README.md` |
| `*-work/`（blender-pilot / icon / scene-kit / worldkit×4） | sailor_pilot 等 .blend 源 | `tools/blender/` 管线的模型源文件；FBX 成品入 `Assets/Art/Models/SceneKit/` |

新 clone 参照库的可复现命令模板（`--depth 1`；大文件仓库用 blobless + sparse）：

```bash
BASE=F:/VSCode/pirate-crew-3d-unity/external/<登记目录>
git clone --depth 1 https://github.com/<org>/<repo>.git "$BASE/<名字>"
```

### 3.3 使用公约

1. `external/` 只收 3.2 所列四类；跑测产物（日志、test-results、构建输出、调试截图工作副本）不进本目录；
2. harness 副本与门禁日志一律写系统临时目录（如 `$TEMP/pc3d-harness-<域>/`），任务线收尾即删；AGENTS.md「无头验证台」的副本路径惯例已同步为临时目录；
3. 判图/管线小脚本用完转移到对应 `tools/` 子目录或删除，不在 `external/` 根堆积。
