# EventBus 事件契约登记表

> **为什么要有这份表**：`Core/EventBus.cs` 是静态字符串事件中心，跨模块通信全靠字符串键。
> 字符串键没有编译期检查——拼错一个字母就是"事件发了但没人收到"的静默故障。
> 因此所有事件名与载荷类型必须集中登记，禁止在业务代码里散落未登记的魔法字符串。
>
> **登记有两层，本表是人读的那一层**：
> 1. **代码侧真源**：每个 `XxxEvents` 常量类的 `RegisterContracts()` 把「事件名 → 期望载荷类型」
>    登记进 `Core/Events/EventCatalog.cs`（由唯一入口 `Core/GameEntryPoint` 调用）。
>    运行期 `EventBus` 会拿实际载荷类型与登记值对拍，不符即写入
>    `EventBus.ContractViolations` 并在编辑器/开发构建下告警（见 §5）。
> 2. **本表（人读）**：给人和新会话 AI 看的契约全貌——事件名 / 载荷类型 / 发布方 / 订阅方。
>
> **登记规则**：
> 1. 新增事件前先在本表登记（事件名 snake_case、载荷类型、发布方、订阅方）。
> 2. 事件名常量与载荷类型写在同一个 `XxxEvents.cs` 里，并在该类的 `RegisterContracts()` 里登记
>    （漏这一步会被 `Tests/Core/EventCatalogTests` 判失败）。
> 3. 载荷类型变更属于**破坏性变更**，必须同步更新本表与所有订阅方。
> 4. 事件名一律 snake_case；一个事件只有一个语义，不要复用。

---

## 0. API 用法（强类型）

```csharp
// 发布：载荷类型由实参推断
EventBus.Publish(BattleEvents.CrewDamaged, new CrewDamagedPayload(id, team, dmg, hp, maxHp));
EventBus.Publish(SceneEvents.GoBack);                       // 无载荷事件

// 订阅 / 退订：显式给出载荷类型（方法组无法推断 T，必须写明）
EventBus.Subscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
EventBus.Unsubscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
EventBus.Subscribe(SceneEvents.GoBack, OnGoBackRequest);     // 无载荷事件

EventBus.HasListeners(BattleEvents.CrewDamaged);             // 有无监听者
EventBus.ClearEvent(BattleEvents.CrewDamaged);               // 清某个事件
EventBus.ClearAll();                                         // 清全部
```

**载荷类型进类型系统的收益**：订阅侧不再需要 `is` 模式匹配（旧版每个订阅方都在做
`if (payload is XxxPayload x)`），载荷写错从"运行期静默无效果"变成**编译错误**。

**逃生口 `SubscribeDynamic` / `UnsubscribeDynamic`（`Action<object>`）**：
只给"按事件名表循环注册、编译期固定不了载荷类型"的动态分派用——当前全仓**唯一**使用处是
`PirateCrew/Audio/AudioService.cs` 的 `SubscribeEvents()`（订阅表是 `string[] EventNames`，
处理器按名字查表取得）。语义与限制：收到该事件的任何载荷（无载荷事件收到 `null`），
**不受契约表保护、也不会触发类型不匹配告警**。业务代码一律用泛型重载。

**同名键的载荷类型**：一个事件键在 `EventCatalog` 里只登记**一个**期望类型；
无载荷事件登记为哨兵 `EventBus.NoPayload`（`AddNoPayload`），不要用 `null`/`object` 搪塞。

---

## 1. 已实现（M1，Core 服务层）

### 1.1 `change_scene` —— 请求切换场景

| 项 | 内容 |
| --- | --- |
| 载荷类型 | `string` 场景名（过渡动画按默认 `true`） |
| 发布方 | `UI/MainMenuController`、`UI/LevelSelectController`、`UI/CrewManagementController`、`UI/BattleHud` 等业务侧 |
| 订阅方 | `Core/SceneLoader.OnChangeSceneRequest` |
| 备注 | 空场景名**静默忽略**（场景卸载竞态里可能发空值）。<br>**字典载荷形式已退役**：旧版还接受 `IDictionary<string,object>{path,transition}`（魔法字符串键），全仓零调用点，随强类型化删除；要控制过渡请直接调 `SceneLoader.Instance.ChangeScene(name, transition)` |

> **同名重载语义**（原 `"replaceTop"` 补丁已退役，`Core/SceneLoader.cs:125`）：目标场景与当前同名 = **原地重载、不压栈**——
> 用于"再来一局"这类同场景重载：栈深不变，重开后 `go_back` 仍回到进战前的场景。

### 1.2 `go_back` —— 请求返回上一场景

| 项 | 内容 |
| --- | --- |
| 载荷类型 | **无**（`EventBus.NoPayload` 哨兵） |
| 发布方 | 业务侧（`UI/BattleHud` 返回按钮/返回确认弹窗、`UI/LevelSelectController` 返回按钮） |
| 订阅方 | `Core/SceneLoader.OnGoBackRequest` |
| 备注 | 走 `SceneLoader` 的返回栈；栈空时 `GoBack()` 只告警、不抛异常（M1 有测试锁定） |

### 1.3 场景流转生命周期（`SceneLoader` 广播）

> `scene_load_started` / `scene_load_completed` / `scene_transition_finished` 的**订阅方现状**：
> `scene_load_started` 已由音频层订阅（`AudioService` 的动态订阅表）；另两个暂无订阅方
> （发布侧保留，供过场动画/音频挂接；命名常量在 `Core/SceneEvents`）。

| 事件名 | 载荷类型 | 时机 |
| --- | --- | --- |
| `scene_load_started` | `string` 场景名 | 开始异步加载 |
| `scene_load_completed` | `string` 场景名 | 场景加载完成 |
| `scene_transition_finished` | `string` 场景名 | 淡入淡出过渡结束 |

> ⚠ **契约注意**：`SceneLoader` 有重入保护——过渡（含淡入淡出）期间的新 `change_scene`/`go_back` 请求会被**忽略**。
> 调用方（尤其是测试）必须等 `SceneLoader.Instance.IsLoading == false` 再发下一个请求（M1 踩过这个坑）。

### 1.4 存档（`SaveManager` 广播）

> `save_completed` / `load_completed` / `auto_save_triggered` 当前**暂无订阅方**
> （发布侧保留；命名常量在 `Core/SaveEvents`）。

| 事件名 | 载荷类型 | 时机 |
| --- | --- | --- |
| `save_completed` | `int` 槽位号 | 存档成功 |
| `load_completed` | `int` 槽位号 | 读档成功 |
| `auto_save_triggered` | `int` 槽位号（`SaveManager.AutoSaveSlot = 0`） | 触发自动存档 |

---

## 2. 已实现（M2 战斗）

> 实现以 `Scripts/PirateCrew/Battle/BattleEvents.cs`（代码侧常量类 + 载荷结构体 + `RegisterContracts()`）为准；本表为登记表。
> §3.3 是 M3 新增订阅方明细（事件定义不在此重复登记）。

| 事件名 | 载荷类型 | 发布方 | 订阅方 |
| --- | --- | --- | --- |
| `battle_started` | `BattleStartedPayload` | `BattleController` | HUD（`BattleHud`）/ 环境层（`AmbientDirector`）/ `FxRoot` / `AudioService` / `CampaignApi`（**相机不订阅本事件**——它跟 `turn_started`） |
| `turn_started` | `TurnStartedPayload` | `TurnManager` | HUD / 相机（pan 到行动角色）/ `FxRoot` |
| `turn_ended` | `int` 队伍编号 | `TurnManager` | HUD / 相机 / `BattleController` |
| `action_selected` | `ActionSelectedPayload` | `AimThrowController` / `BattleController` | HUD（武器面板收起）/ 相机（`BattleCameraController`） |
| `crew_damaged` | `CrewDamagedPayload` | `PirateBase` | HUD（血条）/ 相机（震屏）/ `FxRoot` |
| `crew_died` | `CrewDiedPayload` | `PirateBase` | HUD / 相机 / `AudioService` / `FxRoot` / `CampaignApi`（累计阵亡数供星级评价）。**胜负检查不走本事件**（`BattleController` 只订阅 `turn_ended`） |
| `match_finished` | `MatchFinishedPayload` | `BattleController` | HUD / 相机 / `FxRoot` / `AudioService` / `CampaignApi`（结算） |
| `ai_thinking` | `AiThinkingPayload` | `AiController` | 相机（停止自动滚动，§6.1）——**HUD「电脑思考中」提示未兑现** |
| `ai_decided` | `AiDecidedPayload` | `AiController` | `AudioService`（§6.3 特殊武器的实际表现由后续武器脚本消费，当前无其它订阅方） |
| `battle_projectile_detonated` | `ProjectileDetonatedPayload` | `WeaponProjectile` | 表现层（`FxRoot` 爆炸/水花、`WaterSimulationDriver` 涟漪、相机震屏、音频） |
| `battle_mine_beep` | `MineBeepPayload` | `WeaponProjectile`（mine 引信） | 音频层（§5.2 beepTimes 滴答声） |
| `battle_shot_released` | `float` 拖拽距离（px，`AimThrowController.cs:758`） | `AimThrowController` | 音频层（发射音） |
| `camera_focus_requested` | `Transform` 目标 | `TurnManager` / `WeaponProjectile`（voodoo 切镜）/ `AiController` / `BattleController` | 相机（`BattleCameraController`）/ HUD / `FxRoot` |

> **武器运行时事件备注**：`battle_projectile_detonated` / `battle_mine_beep` 的常量与载荷定义在
> `Battle/BattleEvents.cs`，由 `Battle/WeaponProjectile.cs` 发布（弹体引爆 / 地雷引信蜂鸣）。

> **AI 事件备注**：`ai_thinking` / `ai_decided` 的常量与载荷定义在 `Battle/BattleEvents.cs`，
> 由 `Battle/AiController.cs` 发布（§6.1 时间片评估）。`ai_decided` 同时承担「把 §6.3 特殊武器
> 的落点/目标/速度交给武器运行时」的契约职责——M2 的 `AiController` 只改行动经济与装备状态，
> 浪/海鸥/巫毒娃娃/箱体的实际表现由后续武器脚本消费该事件（见 `AiController` 类头注释）。

---

## 3. 已实现（M3，船员管理循环）

> 事件名常量与载荷类型定义在
> `Scripts/CrewManagement/CrewManagementEvents.cs` 与 `Scripts/Campaign/CampaignEvents.cs`。
> M3 的模块间**命令**（关卡结算 → 发放船员奖励）走 `CampaignApi` 直接调 `CrewManagementApi` 的公开方法
> （架构原则：跨模块调用的出口放各模块 `XxxApi`），**通知**才走下面的 EventBus 事件。

### 3.1 船员管理模块广播

| 事件名 | 载荷类型 | 发布方 | 订阅方 |
| --- | --- | --- | --- |
| `crew_roster_updated` | `RosterUpdatedPayload`（已拥有数 + 当前编成 id 数组） | `CrewManagementApi` | `UI/CrewManagementController`（重建名册列表） |
| `crew_unlocked` | `CrewUnlockedPayload`（船员 id + 显示名） | `CrewManagementApi.UnlockCrewsForStars` | `UI/CrewManagementController`、`UI/LevelSelectController`（提示新船员） |
| `crew_reward_granted` | `CrewRewardPayload`（海图 id + 星级 + 每人经验 + 出战 id 数组 + 新招募 id 数组） | `CrewManagementApi.GrantMapReward` | **暂无订阅方**：M3 的结算弹窗读 `CampaignApi.LastReward` 静态快照；该事件留给存档/成就/音频层接入 |

### 3.2 战役模块广播

| 事件名 | 载荷类型 | 发布方 | 订阅方 |
| --- | --- | --- | --- |
| ~~`campaign_level_selected`~~ | 随一代选关链**已删除**（`CampaignEvents.cs`），本行仅存历史 | — | — |
| `campaign_map_completed` | `CampaignMapCompletedPayload`（**5 字段，无序号/章节**；字段清单以 `Assets/Scripts/Campaign/CampaignEvents.cs` 为准） | `CampaignApi`（订阅 `match_finished` 后结算） | `UI/CrewManagementController`（状态提示） |

### 3.3 M3 新增的订阅方（事件本身见 §2，实现以 `Battle/BattleEvents.cs` 为准）

`CampaignApi.Install()`（组合根调用，内部走 `EnsureBootstrapped()`）订阅下列**已有**事件，
把管理循环接到战斗结算上（不改 `PirateCrew/` 一行）：

| 已有事件 | M3 新增订阅方 | 用途 |
| --- | --- | --- |
| `battle_started` | `CampaignApi` | 每局开头把「玩家方阵亡计数」归零 |
| `crew_died` | `CampaignApi` | 累计 `TeamIndex == 0` 的阵亡数，供 §9.3 星级评价 |
| `match_finished` | `CampaignApi` | 评价星级 → 写关卡进度 → 发放船员经验/招募 → 广播 `campaign_map_completed` → 落盘槽位 1 |

> ⚠ **时序注意**：`CampaignApi` 的订阅是**静态订阅**（EventBus 的静态订阅表），跨场景存活；
> 由组合根在进入播放时挂上（`Core/GameEntryPoint` → `CampaignApi.Install()`），不再依赖
> "UI 层某个 `Awake` 恰好在某个时机跑过"。
> 若直接从编辑器打开 Battle 场景进 Play（不经过主菜单/管理界面），结算链路**仍然**会订阅——
> 但那种入口没有「待结算关卡」（`WorldMapRuntime` 没被 `SetPending`），所以不会误结算。
> 另外，「待结算关卡」只在 `WorldMapRuntime.SetPending` 之后的第一局 `battle_started` 有效：
> 非战役入口的一局（主菜单「进入战斗」/ 2P）会把陈旧待结算关卡清掉，避免没打完就退出的那一局被误结算。

---

## 4. 不使用 EventBus 的通信

以下情况**不走** EventBus，以免把强关系伪装成松耦合：

- 同一模块内部的调用（直接方法调用 / `[SerializeField]` 引用）。
- 父子层级的生命周期通知（用 Unity 的 `Awake`/`OnEnable`/`OnDestroy` 或直接引用）。
- 每帧高频数据（用轮询或直接引用；`EventBus.Publish` 虽然已做到"订阅不变时零分配"，
  但它仍是"一对多广播"，逐帧广播本身没有意义）。

---

## 5. 契约的三道闸门（谁在防"静默无效果"）

| 闸门 | 机制 | 位置 | 失败表现 |
| --- | --- | --- | --- |
| ① 编译期 | 泛型 API：载荷类型写错是编译错误 | `Core/EventBus.cs` | `CS1503` |
| ② 运行期 | 发布/订阅时与 `EventCatalog` 对拍；不符写入 `EventBus.ContractViolations` + 告警（编辑器/开发构建） | `Core/EventBus.cs` `VerifyContract` | Console 告警 + 违规记录可查 |
| ③ 测试 | 反射扫全仓 `*Events` 常量类：常量都有登记、登记无僵尸项、键不重复声明、主要事件集成序列零违规 | `Assets/Tests/Core/EventCatalogTests.cs` | 测试红 |

> **未覆盖的盲区（诚实登记）**：闸门 ② 只能证明"发布方用的类型 == 登记的类型"。
> 若**发布方与订阅方同时**用错类型（且与登记一致地错），②③ 都不报——
> 彻底消除需要 Roslyn 分析器做调用点静态检查，属后续工业化步骤，本次不做。
