# EventBus 事件契约登记表

> **为什么要有这份表**：`Core/EventBus.cs` 是静态字符串事件中心，跨模块通信全靠字符串键。
> 字符串键没有编译期检查——拼错一个字母就是"事件发了但没人收到"的静默故障。
> 因此所有事件名与载荷类型必须集中登记，禁止在业务代码里散落未登记的魔法字符串。
>
> **登记规则**：
> 1. 新增事件前先在本表登记（事件名 snake_case、载荷类型、发布方、订阅方）。
> 2. 事件名常量与载荷类型在代码里要有对应载体（如 `BattleEvents.cs`），本表与代码必须一致。
> 3. 载荷类型变更属于**破坏性变更**，必须同步更新本表与所有订阅方。
> 4. 事件名一律 snake_case；一个事件只有一个语义，不要复用。

---

## 1. 已实现（M1，Core 服务层）

### 1.1 `change_scene` —— 请求切换场景

| 项 | 内容 |
| --- | --- |
| 载荷 | `string` 场景名（`transition` 默认 `true`）<br>**或** `IDictionary<string, object>`：`"path"`（`string`，必填）、`"transition"`（`bool`，可选，缺省 `true`） |
| 发布方 | `UI/MainMenuController`（Battle 按钮）等业务侧 |
| 订阅方 | `Core/SceneLoader.OnChangeSceneRequest` |
| 备注 | 两种载荷形式都接受；字典形式用于显式控制过渡。缺 `"path"` 或类型不符时**静默忽略**（不抛异常） |

### 1.2 `go_back` —— 请求返回上一场景

| 项 | 内容 |
| --- | --- |
| 载荷 | 无（`null`） |
| 发布方 | 业务侧（如 `UI/BattlePlaceholder` 返回按钮） |
| 订阅方 | `Core/SceneLoader.OnGoBackRequest` |
| 备注 | 走 `SceneLoader` 的返回栈；栈空时 `GoBack()` 不抛异常（M1 有测试锁定） |

### 1.3 场景流转生命周期（`SceneLoader` 广播）

| 事件名 | 载荷 | 时机 |
| --- | --- | --- |
| `scene_load_started` | `string` 场景名 | 开始异步加载 |
| `scene_load_completed` | `string` 场景名 | 场景加载完成 |
| `scene_transition_finished` | `string` 场景名 | 淡入淡出过渡结束 |

> ⚠ **契约注意**：`SceneLoader` 有重入保护——过渡（含淡入淡出）期间的新 `change_scene`/`go_back` 请求会被**忽略**。
> 调用方（尤其是测试）必须等 `SceneLoader.Instance.IsLoading == false` 再发下一个请求（M1 踩过这个坑）。

### 1.4 存档（`SaveManager` 广播）

| 事件名 | 载荷 | 时机 |
| --- | --- | --- |
| `save_completed` | `int` 槽位号 | 存档成功 |
| `load_completed` | `int` 槽位号 | 读档成功 |
| `auto_save_triggered` | `int` 槽位号（`SaveManager.AutoSaveSlot = 0`） | 触发自动存档 |

---

## 2. 提案/待定（M2 战斗，尚未落地）

> ⚠ 以下事件名为**提案**，用于 M2 战斗模块的跨模块通信，实现以 `Scripts/PirateCrew/Battle/BattleEvents.cs` 为准。
> 代码落地后须把本节内容上移为「已实现」，并补全实际载荷类型。

| 提案事件名 | 拟用载荷 | 拟发布方 | 拟订阅方 |
| --- | --- | --- | --- |
| `battle_started` | 关卡序号 | `BattleController` | HUD / 相机 |
| `turn_started` | 队伍编号 | `TurnManager` | HUD / 相机（pan 到行动角色） |
| `turn_ended` | 队伍编号 | `TurnManager` | HUD |
| `action_selected` | 角色 + 动作类型 | `AimThrowController` | HUD（武器面板收起） |
| `crew_damaged` | 角色 + 伤害值 | `PirateBase` / `BattleController` | HUD（血条） |
| `crew_died` | 角色 | `PirateBase` | HUD / `BattleController`（胜负检查） |
| `match_finished` | 对局结果（`TurnRules.MatchOutcome`）+ 得分 | `BattleController` | HUD / 结算流程 / 存档 |

---

## 3. 不使用 EventBus 的通信

以下情况**不走** EventBus，以免把强关系伪装成松耦合：

- 同一模块内部的调用（直接方法调用 / `[SerializeField]` 引用）。
- 父子层级的生命周期通知（用 Unity 的 `Awake`/`OnEnable`/`OnDestroy` 或直接引用）。
- 每帧高频数据（用轮询或直接引用；EventBus 每次 `Publish` 都要分配委托快照，高频会吃 GC）。
