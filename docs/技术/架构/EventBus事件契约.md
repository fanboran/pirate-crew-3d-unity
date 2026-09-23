# EventBus 事件契约登记表

> **为什么要有这份表**：`Core/EventBus.cs` 是全局事件总线，跨模块通信全走它。
> 事件键不是字符串，而是**类型化频道**——每个事件在所属模块的 `XxxEvents` 类里声明一个
> `public static readonly Event<TPayload>` 字段（无载荷用非泛型 `Event`），频道实例即事件键。
> 拼错频道、频道与载荷张冠李戴都是**编译错误**（键与载荷的一致性由编译器保证）；
> 本表提供的是编译器给不了的全貌——存在哪些频道、载荷是什么、谁发布、谁订阅。
>
> **登记有两层**：
> 1. **代码侧真源**：各模块 `XxxEvents` 类的频道字段（`Core/SceneEvents.cs`、`Core/SaveEvents.cs`、
>    `PirateCrew/Battle/BattleEvents.cs`、`Campaign/CampaignEvents.cs`、`CrewManagement/CrewManagementEvents.cs`）。
>    每个事件一个频道字段，发布/订阅双方都从这里取——单一事实源，**禁止在调用点内联 `new Event()`**
>    （两个各自 `new` 出来的实例是两个不同频道，发布与订阅对不上就是"事件发了没人收到"）。
> 2. **本表（人读）**：给人和新会话 AI 看的契约全貌——频道 / 载荷类型 / 发布方 / 订阅方。
>
> **新增事件两步走**：
> 1. 在所属模块的 `XxxEvents` 类里加一个频道字段（带载荷 `Event<TPayload>`；无载荷 `Event`）；
> 2. 在本表对应模块的表格里加一行（载荷类型 / 发布方 / 订阅方）。
>
> **载荷类型变更属于破坏性变更**：频道泛型参数一改，编译器会把所有发布/订阅调用点逐个揪出来，
> 本表与所有订阅方必须同步更新。一个频道只有一个语义，不要复用。

---

## 0. API 用法（类型化频道）

```csharp
// ── 声明（只写在所属模块的 XxxEvents 类里，调用点禁止内联 new Event）──
public static readonly Event<CrewDamagedPayload> CrewDamaged = new();   // 带载荷
public static readonly Event GoBack = new();                            // 无载荷

// ── 发布：载荷类型由频道锁定，写错即编译错误 ──
EventBus.Publish(BattleEvents.CrewDamaged, new CrewDamagedPayload(id, team, dmg, hp, maxHp));
EventBus.Publish(SceneEvents.GoBack);                       // 无载荷事件

// ── 订阅 / 退订：T 由频道推断，不需要显式写 <T> ──
EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged);   // OnCrewDamaged: Action<CrewDamagedPayload>
EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
EventBus.Subscribe(SceneEvents.GoBack, OnGoBackRequest);       // 无载荷: Action

// ── 查询 / 清理 ──
EventBus.HasListeners(BattleEvents.CrewDamaged);            // 有无监听者
EventBus.ListenerCount(BattleEvents.CrewDamaged);           // 监听者数量（诊断/测试用）
EventBus.ClearEvent(BattleEvents.CrewDamaged);              // 清某个频道
EventBus.ClearAll();                                        // 清全部（慎用）
EventBus.ResetForNewSession();                              // 清订阅表 + 误用记录；由唯一入口 GameEntryPoint 进入播放前调用
```

**频道语义三条**：

1. **键即身份**：频道实例自身就是事件键，EventBus 按引用相等取键——"每个 `static readonly`
   频道字段一个频道"的语义由此成立。
2. **一个频道一种载荷**：`Event<int>` 与 `Event<string>` 是不同频道；两个 `Event<int>` 实例
   也是不同频道。订阅侧不需要 `is` 模式匹配，载荷写错从"运行期静默无效果"变成**编译错误**（CS1503）。
3. **无载荷事件用非泛型 `Event` + 无参 `Action`**，不要声明 `Event<NoPayload>` 频道
   （`NoPayload` 哨兵类型仅供 EventBus 内部表示"无载荷"，误用会被守卫拦截，见 §5）。

**逃生口 `SubscribeDynamic` / `UnsubscribeDynamic`（`Action<object>`）**：
只给"按频道表循环注册、编译期固定不了载荷类型"的动态分派用——当前全仓**唯一**使用处是
`PirateCrew/Audio/AudioService.cs`（按频道表循环订阅，处理器按频道查表取得）。语义与限制：
handler 收到该频道的**任何**载荷（无载荷频道收到 `null`，投递装箱），**不受频道泛型参数的
类型锁定保护**。业务代码一律用泛型订阅。

**误用守卫**：把载荷频道当无载荷频道用（经基类 `Event` 声明逃过重载选择的那类误用）会被
EventBus 拒绝投递、记入 `EventBus.ContractViolations` 并告警——机制与边界见 §5。

---

## 1. 已实现（M1，Core 服务层）

### 1.1 `SceneEvents.ChangeScene` —— 请求切换场景

| 项 | 内容 |
| --- | --- |
| 载荷类型 | `string` 场景名（过渡动画按默认 `true`） |
| 发布方 | `UI/MainMenuController`、`UI/LevelSelectController`、`UI/CrewManagementController`、`UI/BattleHud` 等业务侧 |
| 订阅方 | `Core/SceneLoader.OnChangeSceneRequest` |
| 备注 | 空场景名**静默忽略**（场景卸载竞态里可能发空值）。<br>**字典载荷形式不存在**：要控制过渡请直接调 `SceneLoader.Instance.ChangeScene(name, transition)` |

> **同名重载语义**（原 `"replaceTop"` 补丁已退役，`Core/SceneLoader.cs:125`）：目标场景与当前同名 = **原地重载、不压栈**——
> 用于"再来一局"这类同场景重载：栈深不变，重开后 `SceneEvents.GoBack` 仍回到进战前的场景。

### 1.2 `SceneEvents.GoBack` —— 请求返回上一场景

| 项 | 内容 |
| --- | --- |
| 载荷类型 | **无**（非泛型 `Event` 频道） |
| 发布方 | 业务侧（`UI/BattleHud` 返回按钮/返回确认弹窗、`UI/LevelSelectController` 返回按钮） |
| 订阅方 | `Core/SceneLoader.OnGoBackRequest` |
| 备注 | 走 `SceneLoader` 的返回栈；栈空时 `GoBack()` 只告警、不抛异常（M1 有测试锁定） |

### 1.3 场景流转生命周期（`SceneLoader` 广播）

> `SceneEvents.SceneLoadStarted` / `SceneEvents.SceneLoadCompleted` / `SceneEvents.SceneTransitionFinished` 的**订阅方现状**：
> `SceneEvents.SceneLoadStarted` 已由音频层订阅（`AudioService` 的动态订阅）；另两个暂无订阅方
> （发布侧保留，供过场动画/音频挂接；频道字段在 `Core/SceneEvents`）。

| 频道 | 载荷类型 | 时机 |
| --- | --- | --- |
| `SceneEvents.SceneLoadStarted` | `string` 场景名 | 开始异步加载 |
| `SceneEvents.SceneLoadCompleted` | `string` 场景名 | 场景加载完成 |
| `SceneEvents.SceneTransitionFinished` | `string` 场景名 | 淡入淡出过渡结束 |

> ⚠ **契约注意**：`SceneLoader` 有重入保护——过渡（含淡入淡出）期间的新 `SceneEvents.ChangeScene`/`SceneEvents.GoBack` 请求会被**忽略**。
> 调用方（尤其是测试）必须等 `SceneLoader.Instance.IsLoading == false` 再发下一个请求（M1 踩过这个坑）。

### 1.4 存档（`SaveManager` 广播）

> `SaveEvents.SaveCompleted` / `SaveEvents.LoadCompleted` / `SaveEvents.AutoSaveTriggered` 当前**暂无订阅方**
> （发布侧保留；频道字段在 `Core/SaveEvents`）。

| 频道 | 载荷类型 | 时机 |
| --- | --- | --- |
| `SaveEvents.SaveCompleted` | `int` 槽位号 | 存档成功 |
| `SaveEvents.LoadCompleted` | `int` 槽位号 | 读档成功 |
| `SaveEvents.AutoSaveTriggered` | `int` 槽位号（`SaveManager.AutoSaveSlot = 0`） | 触发自动存档 |

---

## 2. 已实现（M2 战斗）

> 实现以 `Scripts/PirateCrew/Battle/BattleEvents.cs`（代码侧频道声明 + 载荷结构体）为准；本表为人读登记层。
> §3.3 是 M3 新增订阅方明细（事件定义不在此重复登记）。

| 频道 | 载荷类型 | 发布方 | 订阅方 |
| --- | --- | --- | --- |
| `BattleEvents.BattleStarted` | `BattleStartedPayload` | `BattleController` | HUD（`BattleHud`）/ 环境层（`AmbientDirector`）/ `FxRoot` / `AudioService` / `CampaignApi`（**相机不订阅本事件**——它跟 `BattleEvents.TurnStarted`） |
| `BattleEvents.TurnStarted` | `TurnStartedPayload` | `TurnManager` | HUD / 相机（pan 到行动角色）/ `FxRoot` |
| `BattleEvents.TurnEnded` | `int` 队伍编号 | `TurnManager` | HUD / 相机 / `BattleController` |
| `BattleEvents.ActionSelected` | `ActionSelectedPayload` | `AimThrowController` / `BattleController` | HUD（武器面板收起）/ 相机（`BattleCameraDriver`） |
| `BattleEvents.CrewDamaged` | `CrewDamagedPayload` | `PirateBase` | HUD（血条）/ 相机（震屏）/ `FxRoot` |
| `BattleEvents.CrewDied` | `CrewDiedPayload` | `PirateBase` | HUD / 相机 / `AudioService` / `FxRoot` / `CampaignApi`（累计阵亡数供星级评价）。**胜负检查不走本事件**（`BattleController` 只订阅 `BattleEvents.TurnEnded`） |
| `BattleEvents.MatchFinished` | `MatchFinishedPayload` | `BattleController` | HUD / 相机 / `FxRoot` / `AudioService` / `CampaignApi`（结算） |
| `BattleEvents.AiThinking` | `AiThinkingPayload` | `AiController` | 相机（停止自动滚动，§6.1）——**HUD「电脑思考中」提示未兑现** |
| `BattleEvents.AiDecided` | `AiDecidedPayload` | `AiController` | `AudioService`（§6.3 特殊武器的实际表现由后续武器脚本消费，当前无其它订阅方） |
| `BattleEvents.ProjectileDetonated` | `ProjectileDetonatedPayload` | `WeaponProjectile` | 表现层（`FxRoot` 爆炸/水花、`WaterSimulationDriver` 涟漪、相机震屏、音频） |
| `BattleEvents.MineBeep` | `MineBeepPayload` | `WeaponProjectile`（mine 引信） | 音频层（§5.2 beepTimes 滴答声） |
| `BattleEvents.ShotReleased` | `float` 拖拽距离（px，`AimThrowController.cs:758`） | `AimThrowController` | 音频层（发射音） |
| `BattleEvents.CameraFocusRequested` | `Transform` 目标 | `TurnManager` / `WeaponProjectile`（voodoo 切镜）/ `AiController` / `BattleController` | 相机（`BattleCameraDriver`）/ HUD / `FxRoot` |

> **武器运行时事件备注**：`BattleEvents.ProjectileDetonated` / `BattleEvents.MineBeep` 的频道与载荷定义在
> `Battle/BattleEvents.cs`，由 `Battle/WeaponProjectile.cs` 发布（弹体引爆 / 地雷引信蜂鸣）。

> **AI 事件备注**：`BattleEvents.AiThinking` / `BattleEvents.AiDecided` 的频道与载荷定义在 `Battle/BattleEvents.cs`，
> 由 `Battle/AiController.cs` 发布（§6.1 时间片评估）。`BattleEvents.AiDecided` 同时承担「把 §6.3 特殊武器
> 的落点/目标/速度交给武器运行时」的契约职责——M2 的 `AiController` 只改行动经济与装备状态，
> 浪/海鸥/巫毒娃娃/箱体的实际表现由后续武器脚本消费该事件（见 `AiController` 类头注释）。

---

## 3. 已实现（M3，船员管理循环）

> 事件频道与载荷类型定义在
> `Scripts/CrewManagement/CrewManagementEvents.cs` 与 `Scripts/Campaign/CampaignEvents.cs`。
> M3 的模块间**命令**（关卡结算 → 发放船员奖励）走 `CampaignApi` 直接调 `CrewManagementApi` 的公开方法
> （架构原则：跨模块调用的出口放各模块 `XxxApi`），**通知**才走下面的 EventBus 事件。

### 3.1 船员管理模块广播

| 频道 | 载荷类型 | 发布方 | 订阅方 |
| --- | --- | --- | --- |
| `CrewManagementEvents.RosterUpdated` | `RosterUpdatedPayload`（已拥有数 + 当前编成 id 数组） | `CrewManagementApi` | `UI/CrewManagementController`（重建名册列表） |
| `CrewManagementEvents.CrewUnlocked` | `CrewUnlockedPayload`（船员 id + 显示名） | `CrewManagementApi.UnlockCrewsForStars` | `UI/CrewManagementController`、`UI/LevelSelectController`（提示新船员） |
| `CrewManagementEvents.RewardGranted` | `CrewRewardPayload`（海图 id + 星级 + 每人经验 + 出战 id 数组 + 新招募 id 数组） | `CrewManagementApi.GrantMapReward` | **暂无订阅方**：M3 的结算弹窗读 `CampaignApi.LastReward` 静态快照；该事件留给存档/成就/音频层接入 |

### 3.2 战役模块广播

| 频道 | 载荷类型 | 发布方 | 订阅方 |
| --- | --- | --- | --- |
| ~~`campaign_level_selected`~~ | 已随一代选关链删除（`CampaignEvents` 里无此频道），本行仅存历史 | — | — |
| `CampaignEvents.MapCompleted` | `CampaignMapCompletedPayload`（**5 字段，无序号/章节**；字段清单以 `Assets/Scripts/Campaign/CampaignEvents.cs` 为准） | `CampaignApi`（订阅 `BattleEvents.MatchFinished` 后结算） | `UI/CrewManagementController`（状态提示） |

### 3.3 M3 新增的订阅方（事件本身见 §2，实现以 `Battle/BattleEvents.cs` 为准）

`CampaignApi.Install()`（组合根调用，内部走 `EnsureBootstrapped()`）订阅下列**已有**事件，
把管理循环接到战斗结算上（不改 `PirateCrew/` 一行）：

| 已有频道 | M3 新增订阅方 | 用途 |
| --- | --- | --- |
| `BattleEvents.BattleStarted` | `CampaignApi` | 每局开头把「玩家方阵亡计数」归零 |
| `BattleEvents.CrewDied` | `CampaignApi` | 累计 `TeamIndex == 0` 的阵亡数，供 §9.3 星级评价 |
| `BattleEvents.MatchFinished` | `CampaignApi` | 评价星级 → 写关卡进度 → 发放船员经验/招募 → 广播 `CampaignEvents.MapCompleted` → 落盘槽位 1 |

> ⚠ **时序注意**：`CampaignApi` 的订阅是**静态订阅**（EventBus 的静态订阅表），跨场景存活；
> 由组合根在进入播放时挂上（`Core/GameEntryPoint` → `CampaignApi.Install()`），不再依赖
> "UI 层某个 `Awake` 恰好在某个时机跑过"。
> 若直接从编辑器打开 Battle 场景进 Play（不经过主菜单/管理界面），结算链路**仍然**会订阅——
> 但那种入口没有「待结算关卡」（`WorldMapRuntime` 没被 `SetPending`），所以不会误结算。
> 另外，「待结算关卡」只在 `WorldMapRuntime.SetPending` 之后的第一局 `BattleEvents.BattleStarted` 有效：
> 非战役入口的一局（主菜单「进入战斗」/ 2P）会把陈旧待结算关卡清掉，避免没打完就退出的那一局被误结算。

---

## 4. 不使用 EventBus 的通信

以下情况**不走** EventBus，以免把强关系伪装成松耦合：

- 同一模块内部的调用（直接方法调用 / `[SerializeField]` 引用）。
- 父子层级的生命周期通知（用 Unity 的 `Awake`/`OnEnable`/`OnDestroy` 或直接引用）。
- 每帧高频数据（用轮询或直接引用；`EventBus.Publish` 虽然已做到"订阅不变时零分配"，
  但它仍是"一对多广播"，逐帧广播本身没有意义）。

---

## 5. 类型保障与误用守卫（谁在防"静默无效果"）

**编译期（主防线）**：事件键是 `Event` / `Event<T>` 实例，频道声明处的泛型参数把「键 → 载荷类型」
锁死一次，发布方与订阅方对着**同一个频道字段**取类型：

| 错法 | 后果 |
| --- | --- |
| 频道字段名拼错 | 编译错误（字段不存在，CS0117） |
| 频道与载荷张冠李戴（`Event<int>` 频道发 `string`，或订阅 `Action<string>`） | 编译错误（CS1503） |

运行期**零契约对拍**——字符串键时代"登记表 + 运行期对拍"那套机制不再存在。

**误用守卫（编译期管不住的结构性误用）**：变量声明成基类 `Event` 会逃过重载选择，两类误用
编译器发现不了，由 EventBus 在订阅/发布时拦截：

1. 声明 `Event<NoPayload>` 频道并走泛型重载（无载荷频道就是非泛型 `Event`，两种东西）；
2. 把 `Event<T>` 载荷频道经基类引用传给无载荷重载 `Subscribe(channel, Action)` / `Publish(channel)`
   ——这条订阅/发布收不到任何投递。

EventBus 拒绝这些调用（**不投递**）、记入 `EventBus.ContractViolations`（上限 64 条，供诊断与
测试断言）并在编辑器/开发构建下告警；正式构建里整条日志调用被 `Log.Warn` 的 `[Conditional]`
删掉，静默。

**残余盲区（诚实登记）**：编译期保障把「键与载荷的一致性」收敛到**频道声明处**一处——
若频道声明本身定错（声明成 `Event<int>` 但发布/订阅两边都按 `float` 语义使用），编译器无从
判断；载荷**字段**填错（把伤害值塞进血量字段）更是任何类型系统都管不住的。这两类靠频道
声明处的 review 与集成测试兜底。

**静态残留**：Unity 关闭 Domain Reload 后静态订阅表跨播放存活。`EventBus.ResetForNewSession()`
（订阅表 + 误用记录一起清）由唯一入口 `Core/GameEntryPoint` 在进入播放前调用；测试也用它
隔离静态状态。
