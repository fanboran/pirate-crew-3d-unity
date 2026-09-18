# M2 战斗模块 Unity 参照库调研

> 任务：为 M2「补最小可玩闭环」战斗模块储备 Unity 实现参照（遵守 AGENTS.md「参照库强制」规则）。
> 参照库 clone 到 `external/m2-combat-reference/`（`external/` 已 gitignore，不入库）。
> 本文只记录「可借鉴的模式」，不整段搬运任何外部代码；所有代码片段 ≤15 行并标注来源文件与行号。
> 核查环境：Unity 2022.3.62f1c1 + URP 14.0.12 + Cinemachine 2.9.7（本地 PackageCache）。
> 本地权威核对源：`F:\Unity\2022.3.62f1c1\Editor\Data\Managed\UnityEngine\*.xml`、
> `pirate-crew\Library\PackageCache\com.unity.cinemachine@2.9.7\`、
> `pirate-crew\Library\PackageCache\com.unity.render-pipelines.universal@14.0.12\`。

---

## 0. 需求提炼（先读逆向文档再定选型）

已读 `docs/参考游戏逆向-海盗军团抢宝藏-静态.md` 的 §5.1 / §5.2 / §5.3 / §5.4 / §6 / §8.1，确认 M2 需要四类能力：

| 能力 | 逆向文档依据 | 关键规则（简） |
| --- | --- | --- |
| (a) 回合制弹道投掷 | §5.1 / §5.4 / §6.1 | 拖拽蓄力 → 抛物线 → 命中结算；初速 = 0.25 × 拖拽距离且限速 twangMax；每帧 `vy += weight`；撞地 `vy *= -bounce`、`|vx| -= friction`；一套操作两阶段（投自己 / 用武器） |
| (b) 瞄准轨迹预测线 | §5.1 `drawTwangLine` | 15 段虚线，逐段加重力、alpha 衰减 |
| (c) 单位选中高亮 | §4.5 `characterOverlay` | 选中/悬停显示选框角；`hoverCharacter` 30px 内高亮；本队才显示三角标 |
| (d) 战斗相机 | §8.1 `advanceScrolling` | 屏幕边缘 40px / 方向键平移（每帧 10px）；自动跟镜优先级：对白 > 弹窗 > AI 决策 > 下落宝箱 > 被抛角色 > 飞行武器 > panToCharacter；相机范围按 `levelWidth/Height*32` 夹取 |

补充约束（本工程架构铁律，来自 AGENTS.md）：
- 模块间只走 `PirateCrew.Core.EventBus` 静态字符串事件中心（`Publish/Subscribe(string, Action<object>)`）。
- 禁止 `GameObject.Find`、禁止跨模块 `GetComponent` 裸引用；引用一律 `[SerializeField]` 直连。
- 命名空间：战斗 `PirateCrew.PirateCrew.Combat`，界面 `PirateCrew.UI`。
- 纯逻辑层已存在：`Assets/Scripts/PirateCrew/Combat/Ballistics.cs`（`TwangVelocity` / `FullForceDragDistance` / `PredictTrajectory`）、`TurnRules.cs`、`ExplosionResolver.cs`、`WeaponTriggerRules.cs`、`ScoreRules.cs`。**M2 要补的是表现层（输入/轨迹线/描边/相机），不要重写纯逻辑。**

---

## 1. 选型表

四个库各覆盖至少一类能力，全部 MIT、全部 clone 成功、`--depth 1`。

| 库名 | 仓库 URL | 星标 | 最近提交 | 许可证 | 对应能力 | 选它的理由 |
| --- | --- | --- | --- | --- | --- | --- |
| angry-birds-slingshot | https://github.com/dgkanatsios/AngryBirdsStyleGame | 679 | 2024-11-22 | MIT | (a) + (b) | 完整的弹弓状态机、拖拽夹取、15 段预测线、投掷后相机跟随、回合推进与「全部静止/超时」判定，是四类能力里 (a) 最完整的单库；缺点是 2D 物理 + Unity 2021.3，需做 2D→3D 迁移 |
| trajectory-dots | https://github.com/herbou/Tuto_DrawTrajectory | 28 | 2023-02-25 | MIT | (b) | 专门的「点阵轨迹」实现：预实例化 dot、按时间戳采样 `p = p0 + v·t − ½g·t²`、沿路径逐点缩小，正好对应 §5.1 的「15 段虚线 + alpha 衰减」；代码量极小，易读易改编 |
| urp-outlines | https://github.com/Robinseibold/Unity-URP-Outlines | 717 | 2024-02-18 | MIT | (c) | URP `ScriptableRendererFeature` 屏幕空间描边，代码已经是 URP 14 的 `RTHandle` / `cameraColorTargetHandle` / `Blitter.BlitCameraTexture` 写法，与本工程 URP 14.0.12 对齐；用 `LayerMask` 过滤描边对象，天然适合「选中谁描谁」 |
| rts-camera-cinemachine | https://github.com/Nickk888SAMP/RTSCameraController-Cinemachine | 162 | 2026-01-26 | MIT | (d) | **目标平台 Unity 2022.3.11f1 + Cinemachine 2.9.7，与本工程版本完全一致，零 API 迁移**；含屏幕边缘滚动、键鼠平移、目标锁定（Lerp）、边界夹取，几乎逐条对应 §8.1 |

补充说明：
- 候选落选记录：`Valera-IT/Worms`（0 星、无许可证、2026 新建）不满足「星标高/许可证宽松」；`Unity-Technologies/UniversalRenderingExamples`（2298 星）无 LICENSE 文件，存在授权不确定性，仅作背景不 clone；`angyan/Turnable`（137 星）是纯 C# 回合框架、无 Unity 相机/渲染内容，与四类能力不匹配。
- 本工程现有战斗纯逻辑（Ballistics/TurnRules）已覆盖 §5.1/§5.3/§6 的数值与流程，因此参照库只解决「Unity 表现层怎么做」，不引入外部回合框架。

### 1.1 clone 落盘位置与版本

```
external/m2-combat-reference/
├── angry-birds-slingshot/        # dgkanatsios/AngryBirdsStyleGame   @ 99ba3e2 (2024-11-22)
├── trajectory-dots/              # herbou/Tuto_DrawTrajectory         @ 9348e63 (2023-02-25)
├── urp-outlines/                 # Robinseibold/Unity-URP-Outlines    @ 46be206 (2024-02-18)
└── rts-camera-cinemachine/       # Nickk888SAMP/RTSCameraController…  @ f581e78 (2026-01-26)
```

clone 命令（可复现；rts-camera 因仓库含 ~60MB 预览 gif，用 blobless + sparse 只取 Assets/Packages/ProjectSettings）：

```bash
BASE=F:/VSCode/pirate-crew-3d-unity/external/m2-combat-reference
git clone --depth 1 https://github.com/dgkanatsios/AngryBirdsStyleGame.git       "$BASE/angry-birds-slingshot"
git clone --depth 1 https://github.com/herbou/Tuto_DrawTrajectory.git            "$BASE/trajectory-dots"
git clone --depth 1 https://github.com/Robinseibold/Unity-URP-Outlines.git       "$BASE/urp-outlines"
git clone --depth 1 --filter=blob:none --sparse \
    https://github.com/Nickk888SAMP/RTSCameraController-Cinemachine.git          "$BASE/rts-camera-cinemachine"
git -C "$BASE/rts-camera-cinemachine" sparse-checkout set Assets Packages ProjectSettings
```

---

## 2. 各库核心实现摘要

以下路径均为各参照库仓库内相对路径。行号已用 `grep -n` 在本地 clone 上核对。

### 2.1 angry-birds-slingshot —— 弹弓状态机 + 预测线（能力 a / b）

核心文件：`Assets/Scripts/SlingShot.cs`（222 行）、`Assets/Scripts/GameManager.cs`（204 行）、`Assets/Scripts/CameraFollow.cs`（40 行）、`Assets/Scripts/Bird.cs`（60 行）。
目标版本 `m_EditorVersion: 2021.3.27f1`，2D 物理（Rigidbody2D / CircleCollider2D / Physics2D.gravity）。

**解决思路**：一个三段状态机把「待机 → 玩家拖拽 → 飞行」分开，拖拽阶段实时画预测线，松手换算初速并把刚体切回动力学；飞行结束后由 GameManager 等所有刚体静止或超时，再把相机拉回、换下一只鸟（即回合推进）。

关键类与行号：

| 位置 | 内容 |
| --- | --- |
| `Assets/Scripts/SlingShot.cs:61` | `switch (slingshotState)`：Idle / UserPulling / BirdFlying |
| `Assets/Scripts/SlingShot.cs:68-77` | `Input.GetMouseButtonDown(0)` + `Physics2D.OverlapPoint` 判断是否点到鸟 |
| `Assets/Scripts/SlingShot.cs:88-97` | 拖拽点离弹弓中心 > 1.5 时按 `.normalized * 1.5f` 夹取，得到最大拖拽半径 |
| `Assets/Scripts/SlingShot.cs:107-113` | 松手：距离 > 1 才发射，否则回弹复位 |
| `Assets/Scripts/SlingShot.cs:136-151` | `ThrowBird(distance)`：`velocity = middle - birdPos`，再乘 `ThrowSpeed * distance` |
| `Assets/Scripts/SlingShot.cs:186-214` | `DisplayTrajectoryLineRenderer2(distance)`：15 段采样 |
| `Assets/Scripts/Bird.cs:16-19` | 投掷前 `isKinematic = true`、放大 Collider 方便点选 |
| `Assets/Scripts/Bird.cs:36-47` | `OnThrow()`：关 kinematic、显示 Trail、恢复 Collider 半径、切状态 |
| `Assets/Scripts/GameManager.cs:55-61` | `BirdFlying && (全部静止 || 超时 5s)` → 相机回中 + 下一回合 |
| `Assets/Scripts/GameManager.cs:146-150` | 订阅 `BirdThrown` 事件 → 设置 `cameraFollow.BirdToFollow` |
| `Assets/Scripts/CameraFollow.cs:16-28` | 跟随目标 x，并用 `Mathf.Clamp` 夹到场景边界 |

预测采样的关键片段（`Assets/Scripts/SlingShot.cs:202-208`，节选 ≤15 行）：

```csharp
for (int i = 1; i < segmentCount; i++)
{
    // space = p0 + v*t + 1/2 * a * t^2
    float time2 = i * Time.fixedDeltaTime * 5;
    segments[i] = segments[0] + segVelocity * time2
                + 0.5f * Physics2D.gravity * Mathf.Pow(time2, 2);
}
```

**可借鉴的具体模式**（不抄代码）：
1. 弹弓状态机三段式，与本工程 §3.4「严格两阶段操作」可合并：Idle=等选目标，UserPulling=拖拽蓄力，Flying=结算。
2. 「拖拽向量取反再乘系数」与 §5.1 完全同构；本工程 `Ballistics.TwangVelocity` 已实现，参照库只用于核对表现层怎么把鼠标位移喂进去。
3. **最大值夹取 + 松手阈值**：拖太短不发射（回弹），这是防误触的必要细节。
4. **「全部刚体静止或超时」作为回合结束条件**——解决「抛物线物体还在飞时不能换人」的问题，对应本工程 `TurnRules` 的 `InactivityExceeded`。
5. 投掷瞬间发事件驱动相机跟随，避免在投掷脚本里直接引用相机（与本工程 EventBus 解耦思路一致）。
6. 2D→3D 需替换的点：`Physics2D.OverlapPoint` → `Physics.Raycast`；`Physics2D.gravity` → `Physics.gravity`；`Rigidbody2D.velocity` → `Rigidbody.velocity`。

### 2.2 trajectory-dots —— 点阵抛物线轨迹（能力 b）

核心文件：`Assets/Scripts/Trajectory.cs`（72 行）、`Assets/Scripts/Ball.cs`（33 行）、`Assets/Scripts/GameManager.cs`（89 行）。仓库体量极小（`Assets/Scripts` 仅 4 个 .cs）。

**解决思路**：不用 LineRenderer，而是启动时预实例化 N 个 dot 子物体，拖拽时按等差时间戳重算每个 dot 的世界坐标，并让 dot 沿路径逐渐缩小；球体用 `isKinematic` 开关在「预演」与「发射」间切换。

关键类与行号：

| 位置 | 内容 |
| --- | --- |
| `Assets/Scripts/Trajectory.cs:28-44` | `PrepareDots()`：`Instantiate(dotPrefab)` N 个，scale 从 `dotMaxScale` 递减到 `dotMinScale` |
| `Assets/Scripts/Trajectory.cs:46-61` | `UpdateDots(ballPos, forceApplied)`：按 `timeStamp` 采样 |
| `Assets/Scripts/GameManager.cs:65-77` | 拖拽中：`direction = (startPoint - endPoint).normalized; force = direction * distance * pushForce` |
| `Assets/Scripts/GameManager.cs:79-87` | 松手：`ActivateRb()` + `Push(force)` + `Hide()` |
| `Assets/Scripts/Ball.cs:22-32` | `ActivateRb/DesactivateRb`：切 `isKinematic`，并把 `velocity/angularVelocity` 清零 |

采样公式关键片段（`Assets/Scripts/Trajectory.cs:48-51`，节选 ≤15 行）：

```csharp
timeStamp = dotSpacing;
for (int i = 0; i < dotsNumber; i++) {
    pos.x = (ballPos.x + forceApplied.x * timeStamp);
    pos.y = (ballPos.y + forceApplied.y * timeStamp)
          - (Physics2D.gravity.magnitude * timeStamp * timeStamp) / 2f;
    ...
}
```

**可借鉴的具体模式**：
1. **力公式与 §5.1 同构**：`direction = (dragStart − dragEnd).normalized`，`force = direction * distance * pushForce`；本工程把 `pushForce` 取 `Ballistics.DefaultForceScale = 0.25f` 即可，且 `distance` 超过 `FullForceDragDistance(twangMax)` 时由 `TwangVelocity` 自动限速。
2. **两种轨迹表现二选一**：LineRenderer（angry-birds 方案）适合连续线；点阵（本库方案）更贴近 §5.1「虚线 + 逐段衰减」。建议本工程默认点阵、留 LineRenderer 开关。
3. **`isKinematic` 清零速度的复位套路**：拖拽/瞄准期间物体被「冻结」，发射瞬间解冻——比反复 `SetActive(false/true)` 更平滑，也避免物理抖动。
4. 预实例化 dot 池，避免拖拽每帧 `Instantiate`/`Destroy` 造成 GC。

### 2.3 urp-outlines —— URP 屏幕空间描边（能力 c）

核心文件：`Outlines/Scripts/RendererFeatures/ScreenSpaceOutlines.cs`（173 行）、资产 `Outlines/ShaderGraphs/Outlines.shadergraph`、`Outlines/ShaderGraphs/ViewSpaceNormals.shadergraph`。

**解决思路**：一个 `ScriptableRendererFeature`，内含一个 `ScriptableRenderPass`；先把目标层对象用 `Hidden/ViewSpaceNormals` 材质渲染到一张视空间法线 RT，再让 `Hidden/Outlines` 材质结合深度与法线做 Roberts Cross 边缘检测，最后 `Blitter.BlitCameraTexture` 回写相机颜色。描边对象范围由 `LayerMask` 决定。

关键类与行号：

| 位置 | 内容 |
| --- | --- |
| `ScreenSpaceOutlines.cs:8` | `public class ScreenSpaceOutlines : ScriptableRendererFeature` |
| `ScreenSpaceOutlines.cs:11-45` | `ScreenSpaceOutlineSettings`：颜色/线宽/深度阈值/RobertsCross 倍数/法线阈值/陡角阈值 |
| `ScreenSpaceOutlines.cs:62-89` | Pass 构造：`new Material(Shader.Find("Hidden/Outlines"))`、`FilteringSettings(RenderQueueRange.opaque, layerMask)`、ShaderTagId 列表 |
| `ScreenSpaceOutlines.cs:91-104` | `OnCameraSetup`：`RenderingUtils.ReAllocateIfNeeded(ref normals, ...)` 分配 RT |
| `ScreenSpaceOutlines.cs:106-137` | `Execute`：渲法线 → `cmd.SetGlobalTexture("_SceneViewSpaceNormals", ...)` → 两次 `Blitter.BlitCameraTexture` |
| `ScreenSpaceOutlines.cs:148-159` | `[SerializeField] RenderPassEvent`、`LayerMask outlinesLayerMask`、settings；`Create()` 构造 Pass |
| `ScreenSpaceOutlines.cs:162-164` | `AddRenderPasses` → `renderer.EnqueuePass(...)` |

Pass 结构关键片段（`ScreenSpaceOutlines.cs:129-133`，节选 ≤15 行）：

```csharp
using (new ProfilingScope(cmd, new ProfilingSampler("ScreenSpaceOutlines"))) {
    Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle,
                              temporaryBuffer, screenSpaceOutlineMaterial, 0);
    Blitter.BlitCameraTexture(cmd, temporaryBuffer,
                              renderingData.cameraData.renderer.cameraColorTargetHandle);
}
```

**可借鉴的具体模式**：
1. 用 **全局 Renderer Feature + LayerMask** 实现描边；选中时把角色换到 `Outlined` 层、取消时换回普通层。这是最省事、无需给每个角色挂材质的做法。
2. 深度 + 法线双阈值描边（Roberts Cross），对低模角色比「外扩法线」更稳定，不会因网格法线翻转破面。
3. `Shader.Find` 的材质需要在 `Project Settings > Graphics > Always Included Shaders` 里登记（README 明确要求），否则打包丢失。
4. 已知限制（README「Known Issues」）：**不支持 MSAA**（需改用 FXAA/SMAA）、不支持位移 shader、需开启 URP Asset 的 Depth Texture。
5. 若不想引入全局 Feature，退路是「每角色一份外扩法线描边材质」；代价是每个角色多一次绘制，但完全局部、不碰渲染管线资产。M2 建议先走全局 Feature。

### 2.4 rts-camera-cinemachine —— Cinemachine 战斗相机（能力 d）

核心文件：`Assets/Nickk888/RTSCameraController/Scripts/RTSCameraTargetController.cs`（813 行），辅助：`Misc/ObjectSelector.cs`、`Misc/RTSCanvasController.cs`、`Input Providers/InputProvider_OldInputSystem.cs`。
`ProjectSettings/ProjectVersion.txt` = `2022.3.11f1`；`Packages/manifest.json` = `com.unity.cinemachine: 2.9.7`——与本工程版本一致。

**解决思路**：不直接移动 Cinemachine 虚拟相机，而是移动一个被虚拟相机 `Follow` 的 `CameraTarget` Transform；输入经 `IRTSCInputProvider` 接口抽象（新旧输入系统各一实现）；相机平移用屏幕边缘/按键，目标锁定用 `Vector3.Lerp` 把 `CameraTarget` 平滑拉向锁定目标；最后统一 `Clamp` 到场景边界。

关键类与行号：

| 位置 | 内容 |
| --- | --- |
| `RTSCameraTargetController.cs:57-58` | `public CinemachineVirtualCamera VirtualCamera;`（2.x 类名） |
| `RTSCameraTargetController.cs:201-202` | `CinemachineBrain _cinemachineBrain; CinemachineFramingTransposer _framingTransposer;` |
| `RTSCameraTargetController.cs:334-337` | `InitializeCinemachineBrain()`：`_cam.gameObject.GetComponent<CinemachineBrain>()` |
| `RTSCameraTargetController.cs:346-351` | `VirtualCamera.GetCinemachineComponent<CinemachineFramingTransposer>()` 取 `m_CameraDistance` 当缩放 |
| `RTSCameraTargetController.cs:435-448` | `HandleScreenSideMove`：鼠标在边缘时算出移动向量并平移 |
| `RTSCameraTargetController.cs:465-485` | `HandleKeysMove`：WASD/方向键平移 |
| `RTSCameraTargetController.cs:705-711` | `HandleBoundaries`：`Mathf.Clamp(x, BoundaryMinX, BoundaryMaxX)` + z 同 |
| `RTSCameraTargetController.cs:713-736` | `MoveTargetRelativeToCamera` + `CalculateZoomAdjustedSpeed`（速度随缩放线性调整） |
| `RTSCameraTargetController.cs:737-767` | `GetEdgeDirection`：`ScreenSidesZoneSize`（默认 75px）内返回 ±1 |
| `RTSCameraTargetController.cs:769-813` | `LockOnTarget(object target, float zoomFactor, bool hardLock)` / `CancelTargetLock()` |
| `Misc/ObjectSelector.cs:48-55` | `Physics.Raycast(ray, out RaycastHit hit)` + `hit.transform.CompareTag("Selectable")` 选中 |
| `Input Providers/InputProvider_OldInputSystem.cs:46-66` | `Input.GetMouseButton/GetAxisRaw/mouseScrollDelta/GetKey` 封装 |
| `Misc/RTSCanvasController.cs:53-64` | 订阅相机事件来开/关拖拽 UI（UI 与相机解耦） |

目标锁定片段（`RTSCameraTargetController.cs:423-433`，节选 ≤15 行）：

```csharp
internal void HandleTargetLock()
{
    if (_isLockedOnTarget)
    {
        if (_lockedOnTransform == null)
            CameraTarget.position = _hardLocked ? _lockedOnPosition
                : Vector3.Lerp(CameraTarget.position, _lockedOnPosition, TargetLockSpeed * GetTimeScale());
        else
            CameraTarget.position = _hardLocked ? _lockedOnTransform.position
                : Vector3.Lerp(CameraTarget.position, _lockedOnTransform.position, TargetLockSpeed * GetTimeScale());
        ...
    }
}
```

**可借鉴的具体模式**：
1. **CameraTarget 中转**：Cinemachine 的 `Follow` 永远指向一个空 `CameraTarget`，逻辑只改这个 Transform——这样「跟随当前行动角色」「手动平移」「锁定目标」可以共存，不用频繁切 Follow 目标，也不会和 Cinemachine 阻尼打架。
2. **边缘滚动**：`GetEdgeDirection(pos, Screen.width)` 返回 -1/0/1，`ScreenSidesZoneSize` 对应 §8.1 的 40px 边缘（本工程可设 40）。注意排除鼠标在 UI 上时（参照库用 `IsMousePositionOutsideScreen` + 事件）。
3. **`Mathf.Clamp` 边界**：对应 §8.1 相机范围 `x ∈ [-275, -275+levelWidth*32]`，边界值应从关卡配置读。
4. **速度随缩放自适应**：拉得越远平移越快，避免远视时「爬行」。
5. **输入提供者接口**：`IRTSCInputProvider` 把输入与相机逻辑分离；本工程用 legacy `Input`（manifest 无 Input System 包），可只保留一个实现，但接口思路值得保留以便以后换新输入系统。
6. **多虚拟相机优先级切换**：参照库是单 vcam + 移动 Target；若本工程要「过场机位 vs 战斗机位」，用 `CinemachineVirtualCamera.Priority`（int）切，Brain 自动混合。

---

## 3. 本工程改编方案

### 3.1 总体分层

```
PirateCrew.Core            EventBus / SceneLoader（已存在，不改）
        ↑ 只发事件字符串
PirateCrew.PirateCrew.Combat   表现层新增：CombatUnitView / SlingshotController /
                               TrajectoryView / BattleCameraDirector /
                               UnitSelectionController / CombatPlane
        ↑
PirateCrew.UI            新增：CombatHudController（只订阅事件显示文本）
```

铁律落实：
- 跨模块只 `EventBus.Publish/Subscribe`，payload 用**基元类型或 UnityEngine 类型**（int / float / Transform），不跨模块传自定义业务类型，避免 UI 依赖 Combat 命名空间。
- Combat 模块内允许 `hit.collider.GetComponent<CombatUnitView>()`（同模块）；UI 模块禁止 `GetComponent<CombatUnitView>`。
- 所有引用 `[SerializeField]` 直连，不 `GameObject.Find`。
- 相机 / 描边 / 轨迹线都由场景装配根（Bootstrapper 场景或战斗场景内的组装节点）用 Inspector 连线。

建议新增事件名（沿用现有 snake_case 约定：`go_back` / `change_scene` / `save_completed`）：

| 事件名 | 发出方 | payload | 订阅方 |
| --- | --- | --- | --- |
| `combat_unit_selected` | UnitSelectionController | `int unitId` | CombatHudController |
| `combat_unit_deselected` | UnitSelectionController | `int unitId` | CombatHudController |
| `combat_aim_updated` | SlingshotController | `float force01` | CombatHudController（力度条） |
| `combat_shot_released` | SlingshotController | `float force01` | BattleCameraDirector / 结算 |
| `combat_camera_focus_requested` | SlingshotController / TurnRules 驱动 | `Transform` | BattleCameraDirector |
| `combat_camera_release_requested` | 回合结束 | null | BattleCameraDirector |
| `combat_turn_ready` | 回合推进器 | `int teamId` | CombatHudController |

### 3.2 类骨架草案

> 以下只是「类名 / 职责 / 关键方法签名」，不是可直接编译的完整实现；方法体留待实现时按参照模式补。

**(1) `CombatPlane`（静态工具，`PirateCrew.PirateCrew.Combat`）**
职责：统一「逆向文档的 2D 逻辑坐标（y 向下、单位 px）」与「Unity 3D 世界坐标（y 向上）」的映射，避免各脚本各写一套符号。
- 逆向文档坐标为 Flash 像素；建议 1 世界单位 = 1 格（32px），保留 `const float PixelsPerUnit = 32f`。
- 提供 `Vector3 LogicToWorld(float x, float yLogic, float planeZ = 0f)`、`(float x, float yLogic) WorldToLogic(Vector3 world)`。
- 提供 `float GravityYLogic`（逻辑重力，取世界 `Physics.gravity.y` 的负向转换）供 `Ballistics.PredictTrajectory(weight:)` 使用。

**(2) `CombatUnitView : MonoBehaviour`（`PirateCrew.PirateCrew.Combat`）**
职责：一个可行动角色的视图数据载体与局部选中表现；不订阅 EventBus（由控制器调用）。
- `[SerializeField] int unitId;`、`[SerializeField] int teamId;`、`[SerializeField] Transform aimOrigin;`、`[SerializeField] Rigidbody body;`、`[SerializeField] Renderer[] outlineRenderers;`
- `public int UnitId => unitId; public int TeamId => teamId; public Transform AimOrigin => aimOrigin; public Rigidbody Body => body;`
- `public bool IsAlive { get; set; }`
- `public void SetSelected(bool selected)`：切换 `gameObject.layer` 到 `Outlined` 层（能力 c）；用缓存的 `int` 层索引，避免 `LayerMask.NameToLayer` 每帧查。
- `public void FreezeForAim(bool frozen)`：拖拽瞄准时 `body.isKinematic = frozen` + 速度清零（借鉴 2.2 `Ball`），发射前解冻。

**(3) `SlingshotController : MonoBehaviour`（`PirateCrew.PirateCrew.Combat`）**
职责：把鼠标拖拽翻译成 `Ballistics` 初速并发射；对应 §5.1 + §3.4 两阶段操作。
- `enum AimPhase { Idle, Pulling, Flying }`
- `[SerializeField] CombatUnitView activeUnit;`
- `[SerializeField] TrajectoryView trajectory;`
- `[SerializeField] Camera battleCamera;`
- `[SerializeField] LayerMask aimPlaneMask;`（用于把屏幕点投到战斗平面）
- `[SerializeField] float twangMax = 20f;`（角色 20；武器从 `WeaponDefinition.TwangMax` 读）
- `public void BeginDrag(Vector3 screenPoint)` / `public void UpdateDrag(Vector3 screenPoint)` / `public void Release()`
- `public void CancelAim()`
- 内部：`Camera.ScreenPointToRay` + `Plane.Raycast`（或对 `aimPlaneMask` 做 `Physics.Raycast`）求世界点 → `WorldToLogic` → 算 `dx,dy` → `Ballistics.TwangVelocity(dx, dy, twangMax)` → `activeUnit.Body.velocity = new Vector3(vx, 0f, vy)`（3D 映射需按 `CombatPlane` 约定）→ `trajectory.Hide()` → `FreezeForAim(false)` → `EventBus.Publish("combat_shot_released", force01)`。
- 每次 `UpdateDrag`：`EventBus.Publish("combat_aim_updated", force01)` 给力度条；并调 `trajectory.Show(...)`。
- 满力距离用 `Ballistics.FullForceDragDistance(twangMax)`，不要另写常数。

**(4) `TrajectoryView : MonoBehaviour`（`PirateCrew.PirateCrew.Combat`）**
职责：把 `Ballistics.PredictTrajectory` 的 15 个逻辑点画出来；支持点阵/连线两种模式。
- `[SerializeField] LineRenderer line;`
- `[SerializeField] GameObject dotPrefab; [SerializeField] Transform dotParent; [SerializeField] int dotCount = 15;`
- `[SerializeField] bool useDots = true; [SerializeField] float dotMinScale = 0.15f; [SerializeField] float dotMaxScale = 1f;`
- `public void Show(Vector3 logicOrigin, float vx, float vy, float weight)`
- `public void Hide()`
- 内部：调 `Ballistics.PredictTrajectory(origin.x, origin.y, vx, vy, weight, dotCount)`；`line.positionCount = points.Length; line.SetPositions(worldArr);`（**不要用已过时的 `SetVertexCount`，见 §4 R3**）。
- 点阵模式在 `Awake` 预实例化 `dotCount` 个 dot（借鉴 2.2 `PrepareDots`），`Show` 时只改位置与缩放，不增删对象。

**(5) `UnitSelectionController : MonoBehaviour`（`PirateCrew.PirateCrew.Combat`）**
职责：鼠标点选/悬停本队角色，驱动 `SetSelected` 并广播事件。
- `[SerializeField] LayerMask unitMask;`
- `[SerializeField] LayerMask aimPlaneMask;`
- `CombatUnitView _selected;`
- `void Update()`：`Input.GetMouseButtonDown(0)` → `Camera.main.ScreenPointToRay` → `Physics.Raycast(ray, out RaycastHit hit, maxDistance, unitMask, QueryTriggerInteraction.Ignore)` → `hit.collider.GetComponent<CombatUnitView>()`（同模块，允许）。
- `void Select(CombatUnitView unit)`：`_selected?.SetSelected(false)` → `_selected = unit` → `unit.SetSelected(true)` → `EventBus.Publish("combat_unit_selected", unit.UnitId)`；并 `EventBus.Publish("combat_camera_focus_requested", unit.transform)`。
- 悬停高亮：`Update` 里对非选中同队单位做 raycast，命中就 `SetHover(true)`（对应 §4.5 `hoverCharacter` 30px 内高亮）；可加节流（每 2~3 帧一次）省性能。

**(6) `BattleCameraDirector : MonoBehaviour`（`PirateCrew.PirateCrew.Combat`）**
职责：§8.1 的相机平移 + 自动跟镜；基于参照库 2.4 的「移动 CameraTarget」模式。
- `[SerializeField] CinemachineVirtualCamera battleVcam;`
- `[SerializeField] CinemachineVirtualCamera transitionVcam;`（可选，过场机位）
- `[SerializeField] Transform cameraTarget;`（battleVcam.Follow 指向它）
- `[SerializeField] float edgeZone = 40f;`（§8.1 边缘 40px）
- `[SerializeField] float panSpeed = 10f;`（§8.1 每帧 10px，换算成世界单位/秒）
- `[SerializeField] Vector2 boundsX; [SerializeField] Vector2 boundsZ;`
- `[SerializeField] float focusLerpSpeed = 8f;`
- `Transform _focusTarget; bool _isFocusing;`
- `void OnEnable()`：`EventBus.Subscribe("combat_camera_focus_requested", OnFocus); EventBus.Subscribe("combat_camera_release_requested", OnRelease);`
- `void OnDisable()`：对称 `Unsubscribe`（必须，防止场景切换后悬挂回调）。
- `void Update()`：若 `_isFocusing && _focusTarget != null` → `CameraTarget` Lerp 向目标（借鉴 `HandleTargetLock`）；否则读 `GetEdgeDirection(Input.mousePosition, Screen)` + `Input.GetAxisRaw` 做平移 → 最后 `Clamp` 到 `boundsX/boundsZ`（§8.1 范围由关卡宽高算）。
- `public void FocusOn(Transform t)` / `public void Release()`；`public void SwitchTo(CinemachineVirtualCamera vcam)` 内部改 `battleVcam.Priority / transitionVcam.Priority`。
- 边缘滚动禁用条件：鼠标在 UI 上或武器面板可见时（§8.1）；用 EventBus 收 `combat_weapon_panel_visible` 之类的布尔事件，或 `EventSystem.current.IsPointerOverGameObject()` 判断（需引用 `UnityEngine.EventSystems`）。

**(7) `CombatHudController : MonoBehaviour`（`PirateCrew.UI`）**
职责：只订阅 EventBus 显示选中单位名/血量、力度条、回合提示；不引用任何 Combat 类型。
- `[SerializeField] TMPro.TMP_Text unitLabel; [SerializeField] Slider forceGauge; [SerializeField] GameObject turnBanner;`
- `void OnEnable()`：订阅 `combat_unit_selected` / `combat_unit_deselected` / `combat_aim_updated` / `combat_turn_ready`。
- `void OnUnitSelected(object payload)`：`int unitId = (int)payload;` 显示「Unit #unitId」（若需名字，由一层的 EventBus 再广播一次 `string`，或 UI 侧查 `CrewCatalog` ScriptableObject）。

### 3.3 与现有纯逻辑的接线

- `SlingshotController` 只调用 `Ballistics.TwangVelocity` / `FullForceDragDistance` / `PredictTrajectory`，不重算公式。
- 命中结算沿用现有 `ExplosionResolver`；`SlingshotController` 在 `OnCollisionEnter`（武器脚本）或射线命中时调它，然后 `EventBus.Publish("combat_shot_resolved")`。
- 回合推进沿用 `TurnRules.ShouldAdvanceTurn` / `InactivityExceeded`；「全部刚体静止或超时」的判定可复用参照库 2.1 `BricksBirdsPigsStoppedMoving` 思路，但本工程应遍历 `CombatUnitView` 注册表而非 `GameObject.FindGameObjectsWithTag`（AGENTS 禁止 `Find`）。
- 需要一个 `CombatUnitRegistry`（场景装配时 `[SerializeField] CombatUnitView[] units;` 注入，或各 `CombatUnitView` 在自己 `OnEnable` 里 `EventBus.Publish("combat_unit_registered", this.UnitId)`，由注册表收集）。

---

## 4. API 风险清单（每条附本地核对命令与结论）

> 环境：Unity 编辑器未开，`unity_reflect`/`unity_docs` MCP 不可用；以下全部用本机 DLL/XML/包源码核对。

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

结论：本地同时存在 `P:UnityEngine.LineRenderer.positionCount`、`M:UnityEngine.LineRenderer.SetPositions(UnityEngine.Vector3[])` 与遗留的 `M:UnityEngine.LineRenderer.SetVertexCount(System.Int32)`。参照库 `SlingShot.cs:211` 用的是 `SetVertexCount`（Unity 5.5 起的遗留 API）。**本工程应写 `line.positionCount = n; line.SetPositions(arr);`**，避免过时 API 警告，也便于以后升级。

### R4. 2D 物理 API → 3D 物理 API（三个参照中两个是 2D）

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

结论：参照库 `angry-birds-slingshot/Assets/Scripts/GameManager.cs:70` 用 `Application.LoadLevel(Application.loadedLevel)`（重开本关）。**本工程禁止照抄，改用 `SceneManager.LoadScene` 或现有 `PirateCrew.Core.SceneLoader`（走 `EventBus "change_scene"`），保持导航统一。**

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
- `ScreenSpaceOutlines.cs` 用的正是 URP 14 API（`cameraColorTargetHandle`、`RTHandle`、`Blitter.BlitCameraTexture(cmd, src, dst, material, pass)`、`RenderingUtils.ReAllocateIfNeeded`、`RenderPassEvent`、`EnqueuePass`），与本工程 URP 14.0.12 完全匹配，可直接按结构重写。
- **不要用** `cameraColorTarget`（`ScriptableRenderer.cs:398`，旧 `RenderTargetIdentifier` 路径）和 Unity 6 的 RenderGraph API；也不要引用 URP 15+ 才有的 `UniversalRenderPipeline.RenderGraph` 写法。
- `Blitter.BlitCameraTexture` 带 Material 的重载在 core 包 `Blitter.cs:377`，`Blitter` 类在 `Blitter.cs` 内（`public static class Blitter`）。

### R7. 输入系统：本工程用 legacy `Input`（manifest 无 Input System 包）

核对命令：

```bash
grep -i "inputsystem" "F:/VSCode/pirate-crew-3d-unity/pirate-crew/Packages/manifest.json"   # 无输出
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="[PM]:UnityEngine\.Input\.(GetMouseButton|GetAxisRaw|GetKey|mouseScrollDelta|mousePosition)[^"]*"' "$MG/UnityEngine.InputLegacyModule.xml" | sort -u
```

结论：`Input` 位于 `UnityEngine.InputLegacyModule`，本机 XML 确认存在 `Input.GetMouseButton(int)`、`GetMouseButtonDown/Up`、`GetAxis(String)`、`GetAxisRaw(String)`、`GetKey(String/KeyCode)`、`Input.mousePosition`、`Input.mouseScrollDelta`。工程 manifest 无 `com.unity.inputsystem`，因此 **M2 用 `Input.*` 即可**；参照库 `InputProvider_NewInputSystem.cs` 会引入新包依赖，不要照抄。

### R8. `Physics.Raycast` 与相机投影签名核对

核对命令：

```bash
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="M:UnityEngine\.Physics\.Raycast\([^"]*"' "$MG/UnityEngine.PhysicsModule.xml" | head
grep -oE '<member name="M:UnityEngine\.Camera\.(ScreenToWorldPoint|ScreenPointToRay|WorldToScreenPoint)\([^"]*"' "$MG/UnityEngine.CoreModule.xml" | sort -u
```

结论：存在 `Physics.Raycast(Ray, out RaycastHit, float, int, QueryTriggerInteraction)`、`Physics.Raycast(Vector3, Vector3, out RaycastHit, float, int, QueryTriggerInteraction)`；`Camera.ScreenToWorldPoint(Vector3)`、`Camera.ScreenPointToRay(Vector3)`、`Camera.WorldToScreenPoint(Vector3)` 均存在。选型脚本按这些签名写即可；`Physics.OverlapSphere` 亦可用于「选中附近单位」（未逐一列出，如使用再核对）。

### R9. `Cinemachine` 命名空间与 URP `Shader.Find` 打包风险（经验项，非编译风险）

- 参照库 `RTSCameraTargetController.cs:4` 为 `using Cinemachine;`，2.9.7 包内命名空间确为 `Cinemachine`（`CinemachineVirtualCamera.cs` 声明在 `namespace Cinemachine` 下）。
- `urp-outlines` 的 `Shader.Find("Hidden/Outlines")` / `Hidden/ViewSpaceNormals` 要求在 `Always Included Shaders` 登记，否则构建后材质为 null（README 明确）；若我们自己实现描边 shader，必须一并登记。
- 全局屏幕空间描边 **不支持 MSAA**；本工程 URP Asset 若开了 MSAA，需切 FXAA/SMAA。

---

## 5. 许可证注意事项

四个库均为 **MIT**（已 clone 后本地核对 `LICENSE` / `License.md` / `licence` 文件）：
`angry-birds-slingshot/License.md`（Copyright 2016 Dimitris-Ilias Gkanatsios）、
`trajectory-dots/licence`（Copyright 2020 hamza herbou）、
`urp-outlines/LICENSE`（Copyright 2022 Robin Seibold）、
`rts-camera-cinemachine/LICENSE`（MIT 文本，但版权行是占位模板 `Copyright (c) [year] [fullname]`）。

MIT 允许使用、修改、分发，但要求保留版权声明与许可文本。**本项目策略（也是 AGENTS.md 要求）是「只借鉴模式，不搬运代码」**，原因与边界如下：

### 5.1 允许的「借鉴模式」（安全）

- 架构与职责划分：状态机三段式、CameraTarget 中转、输入提供者接口、事件驱动解耦。
- 公式与数值：`v = direction * distance * 0.25`、`p = p0 + v·t − ½g·t²`、15 段采样、边缘 40px 平移速度——这些是数学与玩法常识，且本工程 `Ballistics.cs` 已自行实现。
- 参数与阈值：拖拽最大距离、静止判定超时、速度随缩放的线性关系。
- 算法思路：深度+法线双阈值 Roberts Cross 描边、LayerMask 过滤、`Mathf.Clamp` 边界、`Vector3.Lerp` 目标锁定。
- 用文字/表格描述实现，再用自己的命名与结构重写。

### 5.2 构成「代码复制」（禁止）

- 整段拷贝任何 `.cs` 文件或大段方法体（即使 MIT 允许，也违反本项目「翻译改编」方针，且会让简历作品显得不是本人所写）。
- 拷贝 ShaderGraph 资产（`Outlines.shadergraph` / `ViewSpaceNormals.shadergraph`）——这是资产复制，且带编辑器 GUID 依赖，不能直接移植；如需描边，自己写 HLSL/SG。
- 拷贝 `angry-birds-slingshot/Assets/Plugins/Demigiant/DOTween/`：**这是第三方插件（Demigiant DOTween），不是该 MIT 仓库作者的代码**，其许可与 MIT 不同，严禁搬运；本工程也不打算引入 DOTween。
- 拷贝参照库的场景/Prefab/动画/贴图等二进制或 YAML 资产。
- 将参照库目录直接放进入库路径：`external/` 已 gitignore，参照库不入库；如需引用，只允许在文档里写路径与模式。

### 5.3 其它

- `rts-camera-cinemachine` 的 LICENSE 版权行是未填写的模板，若将来真的要复用其代码（本项目不会），需谨慎处理归属；仅作模式参考则无影响。
- `Unity-Technologies/UniversalRenderingExamples` 未 clone（无 LICENSE 文件），本项目中不引用。
- 所有参照库保持原样放在 `external/`（gitignored），不入库、不修改。

---

## 6. 验收与复现清单

验收标准自检：

- [x] `docs/M2-Unity参照库调研.md` 存在且 ≥250 行。
- [x] `external/m2-combat-reference/` 下 ≥2 个 clone（实际 4 个，每个能力至少 1 个）。
- [x] 每条 API 风险均附本地核对命令与结论（R1–R9）。
- [x] `git status` 只多出本文档（`external/` 在 .gitignore 第 10 行，不入库）。
- [x] 未启动任何 Unity 进程；未修改 `pirate-crew/Assets/` 下任何文件。
- [x] 未把外部仓库代码整段抄进文档（片段 ≤15 行且标注来源）。

本地核对命令速查（可一键复跑）：

```bash
# R1 velocity / linearVelocity
MG="F:/Unity/2022.3.62f1c1/Editor/Data/Managed/UnityEngine"
grep -oE '<member name="P:UnityEngine\.Rigidbody\.(velocity|linearVelocity)"' "$MG/UnityEngine.PhysicsModule.xml"
# R2 Cinemachine 类名
grep -rn "public class CinemachineVirtualCamera\|class CinemachineCamera\b" \
  "F:/VSCode/pirate-crew-3d-unity/pirate-crew/Library/PackageCache/com.unity.cinemachine@2.9.7/Runtime/Behaviours/CinemachineVirtualCamera.cs"
# R3 LineRenderer
grep -oE '<member name="[^"]*(positionCount|SetVertexCount|SetPositions)[^"]*"' "$MG/UnityEngine.CoreModule.xml" | sort -u
# R6 URP14
grep -n "cameraColorTargetHandle\|BlitCameraTexture\|ReAllocateIfNeeded" \
  "F:/VSCode/pirate-crew-3d-unity/pirate-crew/Library/PackageCache/com.unity.render-pipelines.universal@14.0.12/Runtime/ScriptableRenderer.cs"
```

后续实现顺序建议（M2）：
1. `CombatPlane` + `CombatUnitView`（先把 2D 逻辑坐标与 3D 世界打通）。
2. `TrajectoryView`（点阵模式，接 `Ballistics.PredictTrajectory`）。
3. `SlingshotController`（拖拽 → 初速 → 发射 + 力度事件）。
4. `UnitSelectionController`（点选/悬停）。
5. `BattleCameraDirector`（Cinemachine 平移 + 跟随）。
6. `urp-outlines` 屏幕空间描边接入（选中层）。
7. `CombatHudController`（订阅事件显示 HUD）。

以上均在 M2 范围内；海战/大世界等远期系统不在本次调研覆盖。

---

## 7. external/ 目录总登记与使用公约（提案/待定）

> 本节登记 `external/` 的定位、现存内容与使用公约。**7.3 使用公约已经用户确认（2026-09-18）**，
> 并已同步回写 AGENTS.md（无头验证台副本路径、跑测产物路径、顶层目录结构注释、参照库登记要求）。

### 7.1 定位与边界

`external/` 是 gitignore 的工作台目录，按定位只应存放四类内容：**参照库、无头验证台母本、Flash 逆向材料、Blender 建模源文件**。
此前每开一条任务线就复制一份 harness 副本、每轮 batchmode 门禁往根下落一批 log/xml，一个月积出 70+ 目录与 250+ 散文件，且无回收机制——公约（7.3）即针对这两个来源。

### 7.2 现存登记

| 路径 | 内容 | 上游 / 说明 |
| --- | --- | --- |
| `core-reference/eventbus-adammyhre` | Unity-Event-Bus | <https://github.com/adammyhre/Unity-Event-Bus>（M1 EventBus 参照） |
| `core-reference/savesystem-shapedbyrain` | save-load-system | <https://github.com/shapedbyrainstudios/save-load-system>（M1 存档参照） |
| `core-reference/sceneloader-mygamedevtools` | scene-loader | <https://github.com/mygamedevtools/scene-loader>（M1 场景流转参照） |
| `core-reference/scenetransition-lightgive` | TransitionManager | <https://github.com/LightGive/TransitionManager>（M1 场景切换参照） |
| `m2-combat-reference/`（4 库） | 弹弓/轨迹/描边/RTS相机 | 上游与版本见本文 §1.1，用法见 §2 |
| `fluid-ref/FLIP` | Unity_FLIP_Fluid_Simulation | <https://github.com/lamp-cap/Unity_FLIP_Fluid_Simulation>（水体参照） |
| `fluid-ref/HPWater` | HPWater | <https://github.com/AshenOneArt/HPWater>（水体参照） |
| `swf-decompile/` + `tools/` | game.swf、levels_all.json、ffdec 反编译器 | Flash 原版逆向材料与工具链，逆向文档的原始依据 |
| `m2-harness/` | 无头验证台母本 | 用法见 `external/m2-harness/README.md` |
| `*-work/`（blender-pilot / icon / scene-kit / worldkit×4） | sailor_pilot 等 .blend 源 | `tools/blender/` 管线的模型源文件；FBX 成品入 `Assets/Art/Models/SceneKit/` |

登记时另有一批当日跑测产物（bisect-*、ocean-debug-*、build/ 等）未列入上表——它们属 7.3 所指的跑测产物，随调查线收尾清除。

### 7.3 使用公约

1. `external/` 只收 7.2 所列四类；跑测产物（日志、test-results、构建输出、调试截图工作副本）不进本目录；
2. harness 副本与门禁日志一律写系统临时目录（如 `$TEMP/pc3d-harness-<域>/`），任务线收尾即删；AGENTS.md「无头验证台」的副本路径惯例已同步改为临时目录；
3. 判图/管线小脚本用完转移到对应 `tools/` 子目录或删除，不在 `external/` 根堆积。
