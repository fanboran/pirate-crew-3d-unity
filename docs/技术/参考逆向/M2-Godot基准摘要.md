# M2 Godot 基准摘要（供 C# 翻译使用）

> 生成日期：2026-09-12　里程碑：M2「补最小可玩闭环」战斗模块
> 依据（只读）：`F:\VSCode\game-3\pirate-crew-3d\modules\pirate_crew\**`、`core\`、`config\`、`docs\`
> 对照基线：`docs/技术/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md`（Nitrome 原版 Flash 逆向，868 行）
> 用途：遵守 AGENTS.md「翻译纪律：先读 Godot 版实现再动手」。本文件是**摘要/翻译指令**，不是设计文档；冲突时以 Flash 原版玩法为准。

---

## 0. 阅读说明与文件计数勘误

- 任务书写「29 个 .gd / 共 2380 行」，但 `modules/pirate_crew/**` 下实际 **只有 26 个 `.gd` 文件，合计 2380 行**（行数与任务书完全吻合）。
- 差 3 的原因是：该目录另有 **3 个 `.gdshader`**（`outline_hover` / `outline_post` / `outline_selected`），26+3=29；`.gdshader` 不是 `.gd`，本表不按脚本职责收录，但在第 3 节翻译表中给出 URP 对应做法。
- `scripts/debug_capture.gd.uid` 存在但**没有对应 `.gd`**（残留 uid），不计数。
- 下文第 1 节 **26 个 `.gd` 全部逐一覆盖**，无遗漏。
- 「状态」列口径：**运行时在跑** = `battle.tscn` / `main_menu.tscn` 实例化或经 `class_name` 被实际调用；**空骨架/未接线** = 全工程无实例化、无调用点，或函数体为 `pass` / 常量返回。

---

## 1. 文件职责总表（26 个 .gd 全覆盖）

### 1.1 模块接口与协调层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `modules/pirate_crew/api.gd` | 28 | `PirateCrewAPI`（extends Node）；signal `battle_started(team)`、`battle_ended(winner_team)`、`crew_selected(crew)`、`crew_died(crew)` | — | `core/autoload/event_bus.gd` | **空骨架/未接线**：未注册 autoload、4 个信号从未 emit、`get_current_team()` 恒返回 0、`EventBus.publish("battle_start")` 无任何订阅者 |
| `modules/pirate_crew/scripts/battle.gd` | 268 | `BattleScene`（extends Node3D，无 class_name）；持有 `CrewSelectionHandler` / `PlayerStateMachine` / `BattleHUD` | `projectiles:Node3D`、`trajectory_preview:Node`、`aim_controller:Node`、`turn_manager:Node`、`selection_mask_viewport:SubViewport`、`outline_post:MeshInstance3D` | `gunpowder_barrel.gd`、`battle_hud.tscn`、`crew_selection.gd`、`player_state_machine.gd`；场景节点 `Team1/Team2/Camera3D/DebugFreeCamera/TrajectoryPreview/AimController/TurnManager/SelectionMaskViewport/OutlinePostProcess` | **运行时在跑**（`battle.tscn` 主脚本）。但 `end_turn` 从不调用、胜负只 print、`_on_aim_started`(L157)/`_on_turn_started`(L194) 为 `pass` |

### 1.2 角色层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/characters/pirate_base.gd` | 246 | `PirateBase`（extends CharacterBody3D）；signal `hp_changed(current,max)`、`crew_died` | `hp=100`、`max_hp=100`、`team=0`、`team_color`、`crew_type="sailor"`、`weight=1.0`、`outline_material`（废弃）、`outline_color/outline_width/dash_speed/dash_frequency/distance_attenuation` | `cel_contrast.tres`、`outline_selected.gdshader`、`outline_hover.gdshader`；被 battle.gd 通过 `has_method("take_damage")` 识别 | **运行时在跑**。`throw_to` 是简化类刚体（L227-231 注释），`reset_to_normal` 全工程无调用 |
| `scripts/characters/crew_types/sailor.gd` | 8 | `Sailor` extends `pirate_base.gd` | 无（仅继承） | `pirate_base.gd` | **空骨架**：只有 class_name + 注释，无任何属性/技能覆写 |
| `scripts/characters/crew_types/gunner.gd` | 8 | `Gunner` extends `pirate_base.gd` | 无 | 同上 | **空骨架**：注释称「力量加成，投掷距离更远」，代码零实现 |
| `scripts/characters/crew_types/sniper.gd` | 8 | `Sniper` extends `pirate_base.gd` | 无 | 同上 | **空骨架**：注释称「精准度高，轨迹更直」，代码零实现 |

### 1.3 战斗逻辑层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/combat/turn_manager.gd` | 56 | `TurnManager`（extends Node）；signal `turn_started(team)`、`turn_ended` | `team_count=2` | 被 battle.gd `_init_turn_manager` 调用 | **半接线**：battle 只调用一次 `start_turn()`；`end_turn()`(L40) 全工程无调用 → 回合永不推进 |
| `scripts/combat/damage_calculator.gd` | 15 | `DamageCalculator`（纯静态类，无 extends） | — | 无 | **死代码**：全工程零引用 |
| `scripts/combat/player_state_machine.gd` | 72 | `PlayerStateMachine`（extends RefCounted）；signal `state_changed(top,role)`；enum `TopMode{MOVEMENT,OPERATION}`、`RoleState{NONE,DEFAULT,FOCUS,AIMING}` | — | 被 battle.gd / crew_selection.gd 持有 | **运行时在跑**；`FOCUS` 枚举从未被设置（聚焦子模式未实现） |
| `scripts/combat/ai_controller.gd` | 18 | `AIController`（extends Node）；signal `ai_action_completed` | `enemy_pirates:Node3D`、`player_pirates:Node3D` | 无 | **空 TODO**：`take_turn()` 只 emit 信号（L17 TODO、L18 emit），全工程无实例化 |
| `scripts/combat/crew_selection.gd` | 312 | `CrewSelectionHandler`（extends RefCounted）；signal `focus_requested(crew)` | `max_drag_distance` 在 aim_controller；本类无 export | `battle.gd`、`BattleHUD`、`PlayerStateMachine`、`debug_overlay.tscn` | **运行时在跑**（battle 在 `_ready` new 出来）。`focus_requested` 仅接到 battle.gd:44 的 print；L187 注释明说瞄准逻辑留待「任务6」 |

### 1.4 瞄准层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/aiming/aim_controller.gd` | 108 | `AimController`（extends Node）；signal `aim_started`、`aim_updated(dir,power)`、`aim_executed(dir,power)`、`aim_cancelled` | `max_drag_distance=200.0` | battle.gd 连信号；`_unhandled_input` 读鼠标 | **运行时在跑**（battle.tscn `AimController` 节点）。`_compute_direction`(L98-101) 注释自认「简化」；方向映射与相机无关，是拍脑袋的屏幕→世界映射 |
| `scripts/aiming/trajectory_preview.gd` | 91 | `TrajectoryPreview`（extends Node3D） | `gravity=-18.0`、`max_points=30`、`time_step=0.08`、`speed_scale=18.0`、`point_color`、`point_size=0.15`、`point_interval=3` | battle.gd `_on_aim_updated/_on_aim_executed` | **运行时在跑**。`speed_scale=18` / `gravity=-18` 与 battle.gd 实际发射 `THROW_FORCE=12` 不一致（预览≠实弹） |

### 1.5 武器层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/weapons/projectile_base.gd` | 92 | 匿名（extends RigidBody3D）；signal `projectile_landed(position)` | `damage_center=40.0`、`damage_edge=15.0`、`blast_radius=3.0`、`bounce_count=1` | `pirates` 组；`take_damage` 方法约定 | **未接线**：无任何 `.tscn`/`preload` 使用它；battle 直接 new `gunpowder_barrel.gd` |
| `scripts/weapons/gunpowder_barrel.gd` | 106 | 匿名（extends RigidBody3D） | `damage_center=60.0`、`damage_edge=20.0`、`blast_radius=4.0`、`explosion_scale=2.0` | battle.gd `_GUNPOWDER_BARREL_SCRIPT` preload 后动态 `set_script`；`pirates` 组 | **运行时在跑**（唯一实际投出的武器）。用 `Tween`/`create_tween` 做爆炸特效 |

### 1.6 关卡层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/level/destructible.gd` | 22 | `Destructible`（extends StaticBody3D）；signal `destroyed` | `max_hp=50` | 无 | **未接线**：无场景实例化、无引用；破坏后 `queue_free` 无掉落/碎片 |
| `scripts/level/hazard.gd` | 21 | `Hazard`（extends Area3D）；signal `hazard_triggered(body)` | `damage=40`、`hazard_type="lava"` | `Area3D.body_entered` | **未接线**：battle.tscn 无实例 |
| `scripts/level/island_generator.gd` | 20 | `IslandGenerator`（extends Node3D） | `level_config_path=""` | `config/level_config.json`（路径需手填） | **空骨架**：`generate()` 为 `pass`（L20）；`_ready` 仅在 path 非空时调用，而默认空 |

### 1.7 UI 层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/ui/hud.gd` | 183 | `BattleHUD`（extends CanvasLayer）；signal `camera_mode_changed(is_movement_mode)` | `turn_manager:Node` | `battle_hud.tscn`；动态 `load()` crosshair/weapon_menu 脚本；`ModeSwitchButton` | **运行时在跑**（脚手架级）。TopRightPanel 的回合/存活标签无代码刷新；`_weapon_labels` 永远空 → 底部武器滚动无显示；`set_role_mode`(L165) 是空壳 |
| `scripts/ui/weapon_menu.gd` | 121 | 匿名（extends Control）；signal `weapon_selected(weapon_index)` | — | `hud.gd` 动态 `set_script` | **运行时在跑**（右键弹窗）。武器表是 3 个占位中文名 `["跳跃","炸弹","燃烧瓶"]` |
| `scripts/ui/weapon_wheel.gd` | 19 | `WeaponWheel`（extends Control）；signal `weapon_selected(weapon_id)` | — | 无 | **死代码**：全工程无实例化/无引用 |
| `scripts/ui/crosshair.gd` | 53 | 匿名（extends Control） | `color_default`、`color_aiming`、`arm_length=10.0`、`line_width=2.0` | `hud.gd` 动态 `set_script` | **运行时在跑**（`_draw` 画十字，随 hover 变色） |
| `scripts/ui/main_menu.gd` | 37 | 匿名（extends Control） | — | `main_menu.tscn`；`SceneManager.change_scene` | **运行时在跑**。战役/船员管理按钮是 TODO+pass（L27-28、L32-33） |
| `scripts/ui/mode_switch_button.gd` | 214 | `ModeSwitchButton`（`@tool` extends Button）；signal `mode_changed(is_movement_mode)` | `movement_text="移动"`、`static_text="操作"` | `battle_hud.tscn` | **半接线**：移动/操作切换在跑；`set_role_mode`(L129-136) 的翻面动画全工程无调用 |

### 1.8 相机层

| 路径 | 行数 | 关键类 / 信号 | `@export` 参数 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `scripts/camera/orbit_camera.gd` | 87 | 匿名（extends Camera3D） | `target:Node3D`、`min_distance=2.0`、`max_distance=40.0`、`rotate_speed=0.005`、`zoom_speed=0.5`、`min_pitch=10°`、`max_pitch=80°` | `battle.tscn` `Camera3D` | **运行时在跑**。但 `battle.tscn` **未给 `target` 赋值**（场景里的 `CameraTarget` 没被接上）→ `_initialize` L28-36 自建原点虚拟 target，相机绕世界原点而非目标 |
| `scripts/camera/debug_free_camera.gd` | 167 | 匿名（extends Camera3D）；signal `free_camera_toggled(enabled)` | `move_speed=10.0`、`fast_speed_multiplier=3.0`、`mouse_sensitivity=0.003` | `battle.tscn` `DebugFreeCamera`；battle.gd 监听信号并 `activate/deactivate` | **运行时在跑**（M2 的默认移动相机） |

---

## 2. 空 TODO / 未接线清单（带 `文件:行号`）

### A. 显式 `TODO`

| 位置 | 内容 |
| --- | --- |
| `scripts/combat/ai_controller.gd:17` | `# TODO: Phase 2 实现 AI 决策`（`take_turn` L16-18 只 emit） |
| `scripts/ui/main_menu.gd:27` | `# TODO: Phase 3 — 跳转到关卡选择界面`（L28 `pass`） |
| `scripts/ui/main_menu.gd:32` | `# TODO: Phase 2 — 跳转到船员管理界面`（L33 `pass`） |
| `core/utils/constants.gd:12,15,18,21` | 场景路径 / UI 布局 / 硬边界 / 系统标识四处全部 `# TODO: 项目初始化时填写` |

### B. 空实现 / `pass`

| 位置 | 内容 |
| --- | --- |
| `scripts/level/island_generator.gd:20` | `func generate(): pass`（岛屿生成完全没写） |
| `scripts/battle.gd:157-158` | `_on_aim_started(): pass`（开始瞄准无反馈） |
| `scripts/battle.gd:194-195` | `_on_turn_started(_team): pass`（回合开始无处理） |
| `scripts/combat/turn_manager.gd:28-31` | `_ready(): pass`（注释说由 battle 显式调用，但 battle 只调 `start_turn`，不调 `end_turn`） |

### C. 返回常量 / 占位

| 位置 | 内容 |
| --- | --- |
| `modules/pirate_crew/api.gd:27-28` | `get_current_team()` 恒 `return 0` |
| `scripts/ui/hud.gd:26` | `const WEAPONS = ["跳跃","炸弹","燃烧瓶"]` 占位（与真实 17 武器无关） |
| `scripts/ui/weapon_menu.gd:15` | 同上占位 `const WEAPONS` |
| `scripts/ui/hud.gd:114-121` | `_setup_bottom_weapon_panel()` 清空 `bottom_panel` 子节点与 `_weapon_labels` 后**从不重建** → `scroll_bottom_weapon`(L131) 改索引但无 label 可刷新 |
| `scripts/ui/mode_switch_button.gd:198-199` | `_target_thumb_offset()` 返回常量表达式（本身没问题，但配合 C 组说明滑块只在移动/操作间动） |

### D. 定义了但全工程零引用的死代码

| 位置 | 内容 |
| --- | --- |
| `scripts/combat/damage_calculator.gd:1-15` | `DamageCalculator` 全工程无引用（伤害实际写在 `projectile_base`/`gunpowder_barrel` 内联） |
| `scripts/ui/weapon_wheel.gd:1-19` | `WeaponWheel` 无实例化、无引用 |
| `scripts/weapons/projectile_base.gd:1-92` | 无场景/无 preload 使用；battle 只 new 火药桶 |
| `scripts/level/destructible.gd:1-22` | 无场景实例化 |
| `scripts/level/hazard.gd:1-21` | 无场景实例化 |
| `core/services/audio_manager.gd:1-191` | **未注册 autoload**（`project.godot` autoload 只有 EventBus/SceneManager/ConfigManager/SaveManager/DataManager/RenderDebugCapture/_mcp_game_helper），故其 EventBus 订阅也不生效 |
| `core/autoload/data_manager.gd:29,33` | 注册了 autoload，但 `data_get`/`data_set` 无任何调用方 |
| `core/autoload/save_manager.gd` | 注册了 autoload，但战斗/菜单无调用方 |
| `core/autoload/config_manager.gd:19` | 读取 `res://config/%s.cfg`，而 `config/` 下只有 `*.json`（`weapon/crew/balance/level_config.json`）→ **JSON 数值从未被运行时加载**，仅是离线设计规格 |

### E. 写了但没接线 / 半成品

| 位置 | 内容 |
| --- | --- |
| `scripts/combat/turn_manager.gd:40` | `end_turn()` 无调用方；`battle.gd:191` 只 `start_turn()` 一次 → 战斗卡死在 team0 回合 |
| `scripts/combat/crew_selection.gd:44-48,187` | L187 注释「任务6实现瞄准逻辑，当前仅状态+UI切换」；`_enter_aiming_mode` 只切状态与 UI，不接管瞄准输入 |
| `scripts/combat/crew_selection.gd:9,44` | `focus_requested` 仅连到 `battle.gd:44` 的 `print`，聚焦子模式未实现 |
| `scripts/combat/player_state_machine.gd:29` | `RoleState.FOCUS` 枚举存在但全工程无 `transition_role(FOCUS)` 调用 |
| `scripts/ui/hud.gd:165-168` | `set_role_mode(_active)` 只 `aim_label.visible=false`，未调 `mode_switch.set_role_mode` |
| `scripts/ui/mode_switch_button.gd:129-136` | `set_role_mode()` 翻面动画从无调用方（死逻辑） |
| `scripts/battle.gd:246-258` | `_check_victory()` 只 `print` 胜/平，不切场景、不弹结算、不 `EventBus.publish` |
| `scripts/battle.gd:24,232-233` | `THROW_FORCE=12` 与 `trajectory_preview.speed_scale=18` 不一致：预览抛物线≠实弹轨迹 |
| `scripts/battle.gd:208-233` | 动态 new 火药桶并手搓 CollisionShape/Mesh/Material（无 Prefab），碰撞层掩码全 `0xFFFFFFFF` |
| `scripts/characters/pirate_base.gd:227-246` | `throw_to` 注释自认「简化实现…完整实现应替换为 RigidBody3D 并 apply_impulse」；`reset_to_normal` 无调用方 |
| `scripts/characters/pirate_base.gd:34` | `outline_material` 注释「已废弃，保留兼容旧 .tscn」——应删 |
| `scripts/aiming/aim_controller.gd:28,98-101` | `max_drag_distance=200`（原版满力仅 80px），方向映射自认「简化」且不随相机 |
| `scripts/level/island_generator.gd:11-16` | `level_config_path` 默认空，无场景实例化 → 永不生成 |
| `modules/pirate_crew/api.gd:11-14,20,24` | 4 个信号从未 emit；`EventBus.publish("battle_start"/"battle_end")` 无订阅者；api 也未注册 autoload |
| `battle.tscn` `Camera3D` 节点 | `orbit_camera.target` 未赋值，同名 `CameraTarget` 节点闲置 |
| `scripts/ui/hud.gd` TopRightPanel | `battle_hud.tscn` 里「回合/玩家存活/敌方存活」标签写死 `—/3/3`，脚本无刷新逻辑 |

> 小计：显式 TODO **4 处**（含 constants 内 4 行算 1 组）；`pass` **6 处**；死代码文件 **7 个**；半接线 **17 处**。**最关键的三处**：`ai_controller.gd:17`（AI 全空，M2 必须按静态文档 §6 自建）、`turn_manager.end_turn` 无人调用（回合推不动）、`config/*.json` 从未被运行时加载（数值层要另建真值来源）。

---

## 3. `.gd → .cs` 翻译对照表

命名空间约定：玩法/战斗逻辑 → `PirateCrew.PirateCrew`（可按子目录再分 `PirateCrew.Combat` 等，但**不要**与既有 `PirateCrew.Core` / `PirateCrew.UI` 冲突）；UI → `PirateCrew.UI`。

### 3.1 通用机制映射

| Godot | Unity / C# |
| --- | --- |
| `signal x(a,b)` | `event Action<A,B> X;`（模块内）；跨模块改 `Core.EventBus` 字符串事件 |
| `@export var x` | `[SerializeField] private ... x;`（需外部读写则 `public` 属性） |
| `@onready var x = $A/B` | `[SerializeField]` 直连（Inspector 拖引用），**禁止** `GameObject.Find`/`transform.Find` |
| `_physics_process(delta)` | `FixedUpdate()`（默认 0.02s 固定步长） |
| `_process(delta)` | `Update()` |
| `_unhandled_input(event)` | `Update()` 读 `Input` / `OnGUI`；M2 用 legacy Input（Unity 2022.3 默认） |
| `await get_tree().create_timer(t).timeout` | `IEnumerator` + `yield return new WaitForSeconds(t)` 或累加计时器 |
| `Timer` 节点 | 累加 `float` 计时器（GameObject 上）或 `Coroutine` |
| `create_tween()` / `tween_property` | 协程 `Lerp`，或引 DOTween（外部依赖，谨慎）；URP 无内置 tween |
| `CharacterBody3D` + `move_and_slide()` | `Rigidbody`（`useGravity`、`AddForce`/`velocity`、`MovePosition`）；见 3.3 |
| `RigidBody3D` + `apply_impulse` | `Rigidbody` + `AddForce(vec, ForceMode.Impulse)` |
| `StaticBody3D` | `Collider`（BoxCollider 等，无 Rigidbody） |
| `Area3D.body_entered` | `OnTriggerEnter(Collider)`（Collider 勾 `isTrigger`）/ `OnCollisionEnter` |
| `PhysicsRayQueryParameters3D` + `direct_space_state.intersect_ray` | `Physics.Raycast(ray, out hit, dist, layerMask)` |
| `get_nodes_in_group("pirates")` | BattleController 维护 `List<PirateBase>` 注册表（选/死时增删），**不要**每帧 `FindObjectsOfType` |
| `preload(...) / PackedScene.instantiate()` | `[SerializeField] GameObject prefab` + `Instantiate(prefab, parent)` |
| `class_name X extends "path.gd"` | C# 类继承（`class Gunner : PirateBase`） |
| `@tool` | `[ExecuteAlways]`（自定义 Editor 绘制另用 `OnDrawGizmos`） |
| `Input.set_mouse_mode(CAPTURED)` | `Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;` |
| `wrapi(i+d,0,n)` | `((i+d)%n+n)%n` |
| `_notification(WM_WINDOW_FOCUS_OUT)` | `OnApplicationFocus(bool)` |
| `SubViewport`（mask）+ 后处理 shader 描边 | URP Renderer Feature：额外相机渲 mask RT，再用全屏 pass 合成（工作量最大，单列 Wave 2） |
| `.gdshader` 描边（inverted hull / hover） | ShaderLab：`Cull Front` + 顶点外扩做描边；`outline_hover` → 附加 pass 或屏幕空间边缘 |

### 3.2 逐文件对照

| `.gd` | 目标 C# 类（命名空间） | 机制改法与备注 |
| --- | --- | --- |
| `api.gd` | `PirateCrewApi`（`PirateCrew.PirateCrew`，静态门面） | 4 信号改 `Core.EventBus` 字符串事件（`battle_started`/`battle_ended`/`crew_selected`/`crew_died`）；`get_current_team()` 应委托 `TurnManager`，**不要**照抄 `return 0` |
| `scripts/battle.gd` | `BattleController : MonoBehaviour`（`PirateCrew.PirateCrew`） | 6 个 `@export` → `[SerializeField]`；`_process`→`Update`；`_unhandled_input`→`Update` 内 `Input.GetKeyDown`；船员容器由 `Team1/Team2` 改名 `[SerializeField] Transform[] teams`；`spawn_projectile` 改为 `Instantiate(barrelPrefab)`；`_check_victory` 要接 `EventBus` + 结算 UI（非 print） |
| `scripts/characters/pirate_base.gd` | `PirateBase : MonoBehaviour`（挂 `Rigidbody`+`Collider`） | `CharacterBody3D`→`Rigidbody`；`take_damage` 纯 C#；`hp_changed`/`crew_died` 本地 `event`，`crew_died` 同时 `EventBus.Publish("crew_died")`；描边 `_create_outline_meshes` 的 SurfaceTool 手工合成在 URP 用 `Cull Front` 双 pass 替代；`throw_to` 直接 `AddForce(dir*power/weight, Impulse)`，删掉 `_is_thrown`/`_THROWN_GRAVITY` 手搓重力；删 `outline_material` |
| `crew_types/{sailor,gunner,sniper}.gd` | `Sailor/Gunner/Sniper : PirateBase`（`PirateCrew.PirateCrew`） | 原版无属性分化（见 §4.1），M2 **建议不建 3 类**，用数据驱动 `CrewStats`；若为兼容旧场景保留空类即可 |
| `scripts/combat/turn_manager.gd` | `TurnManager : MonoBehaviour`（`PirateCrew.PirateCrew`，或纯逻辑 `TurnRules` + 薄壳） | `turn_started/turn_ended` → **EventBus** 字符串事件（Battle/HUD/AI 多方依赖）；按 Flash §3 改为 `inactivity > 10 帧` 推进，废弃 `max_turn_time` 计时口径；补齐 `EndTurn()` 的调用链 |
| `scripts/combat/damage_calculator.gd` | `DamageCalculator`（纯静态，`PirateCrew.PirateCrew`） | 数值口径按 §4.3 重写为 `maxDamage*(1-d/r)`，r=size/2+20（3D 化需定尺度）；**不要**照抄「20m 阈值 + 0.5 保底」 |
| `scripts/combat/player_state_machine.gd` | `PlayerStateMachine`（纯 C#，非 MonoBehaviour） | `signal state_changed` → `event Action<TopMode,RoleState>`；枚举照译；`FOCUS` 要么实现要么删 |
| `scripts/combat/ai_controller.gd` | `AIController : MonoBehaviour`（`PirateCrew.PirateCrew`） | **不要照抄空 TODO**；按静态文档 §6 重写：`aiThink` 时间片（每帧 30ms）+ `randomThrows(50)` 自抛 + `luck` 加权武器模拟 + `evilness` + `success` 取最大；纯评估逻辑放 `Scripts/PirateCrew/Combat/AiEvaluator.cs` 以便无头测试 |
| `scripts/combat/crew_selection.gd` | `CrewSelectionController`（`PirateCrew.PirateCrew`） | `RefCounted`→普通类；射线改 `Physics.Raycast`；`_pick_crew` 靠 `Collider.GetComponentInParent<PirateBase>()`；组注册表替代 `get_nodes_in_group`；右键弹菜单改 `EventBus` 或直接引用 HUD |
| `scripts/aiming/aim_controller.gd` | `AimController : MonoBehaviour`（`PirateCrew.PirateCrew`） | 4 信号 → 本地 `event`；`_unhandled_input`→`Update`；方向计算按 Flash §5.1 改为「拖拽屏移 × 0.25，方向取反，上限 twangMaxForce」，`max_drag_distance` 改为由武器 `twangMax` 反推 |
| `scripts/aiming/trajectory_preview.gd` | `TrajectoryPreview : MonoBehaviour`（`PirateCrew.PirateCrew`） | 30 个 Sphere 预生成改 `LineRenderer` 或对象池；`speed_scale` 必须与实际发射同源（用同一 `Ballistics` 纯逻辑，W1-D 已规划）；`_ready` 建球改 `Awake` |
| `scripts/weapons/projectile_base.gd` | `ProjectileBase : MonoBehaviour`（`Rigidbody`，`PirateCrew.PirateCrew`） | **建议不逐行翻译**，作为抽象基类保留字段 `damageCenter/damageEdge/blastRadius/bounceCount`；碰撞用 `OnCollisionEnter`；AOE 用 `Physics.OverlapSphere` + registry；`projectile_landed` 本地事件 |
| `scripts/weapons/gunpowder_barrel.gd` | `GunpowderBarrel : ProjectileBase`（`PirateCrew.PirateCrew`） | `_ready` 连信号改 `Awake/OnEnable`；`create_tween` 爆炸特效改协程缩放 + `Destroy`；数值须回归 §4.2/§4.3 |
| `scripts/level/destructible.gd` | `Destructible : MonoBehaviour`（`PirateCrew.PirateCrew`） | `StaticBody3D`→`Collider`；`destroyed` 事件；M2 **可不移植**（见 §5.3） |
| `scripts/level/hazard.gd` | `Hazard : MonoBehaviour`（`PirateCrew.PirateCrew`） | `Area3D.body_entered`→`OnTriggerEnter`；M2 可不移植 |
| `scripts/level/island_generator.gd` | `IslandGenerator : MonoBehaviour`（`PirateCrew.PirateCrew`） | 空实现，M2 **不移植**；3D 地形程序化生成另立里程碑 |
| `scripts/ui/hud.gd` | `BattleHUD : MonoBehaviour`（`PirateCrew.UI`） | `CanvasLayer`→UGUI Canvas；动态 `load()+set_script` 改 `[SerializeField]` 直连 Prefab；`camera_mode_changed` → `event Action<bool>`；底部武器面板需真正生成按钮；TopRight 标签接数据 |
| `scripts/ui/weapon_menu.gd` | `WeaponMenu : MonoBehaviour`（`PirateCrew.UI`） | `weapon_selected` → `event Action<int>`；`_input` 改 `Update` 或实现 `IPointerClickHandler`；占位武器表换成 17 武器数据 |
| `scripts/ui/weapon_wheel.gd` | （不移植） | 死代码，无引用 |
| `scripts/ui/crosshair.gd` | `Crosshair : MonoBehaviour`（`PirateCrew.UI`） | `_draw` 画线改 UGUI `Image` 拼装或 `OnPopulateMesh`；`@export` 颜色/尺寸 → `[SerializeField]` |
| `scripts/ui/main_menu.gd` | `MainMenuController`（M1 已有 `PirateCrew.UI.MainMenuController`） | **并入现有 M1 类**，不要新建重复；战役/船员入口按 M3 再补 |
| `scripts/ui/mode_switch_button.gd` | `ModeSwitchButton : MonoBehaviour`（`PirateCrew.UI`） | `@tool`→`[ExecuteAlways]`；`_draw` 自定义外观改 UGUI `Image` + `Slider`/`Toggle` 组合；`set_role_mode` 翻面动画用协程 `scale.x` 缓动；`mode_changed` → `event Action<bool>` |
| `scripts/camera/orbit_camera.gd` | `OrbitCamera : MonoBehaviour`（`PirateCrew.PirateCrew`） | 建议改用 **Cinemachine**（AGENTS 已装包）：`CinemachineVirtualCamera` + `OrbitalFollow`/`CinemachineBrain` 收收平移；或保留手写 `Update` 中的 `transform` 绕 target 计算（`target` 用 `[SerializeField] Transform`） |
| `scripts/camera/debug_free_camera.gd` | `DebugFreeCamera : MonoBehaviour`（`PirateCrew.PirateCrew`） | `_process`→`Update`；`Input.is_key_pressed`→`Input.GetKey`；鼠标捕获改 `Cursor.lockState`；`free_camera_toggled` → `event Action<bool>`；`_notification`→`OnApplicationFocus` |

### 3.3 哪些 `signal` 该改走 `EventBus` 字符串事件

| 源信号 | 去向 | 理由 |
| --- | --- | --- |
| `api.gd` 全部 4 信号 | **EventBus** | 这是模块对外契约（Campaign/CrewManagement 会消费） |
| `TurnManager.turn_started / turn_ended` | **EventBus** | Battle、HUD、AI、相机四方依赖，且跨「回合系统」边界 |
| `PirateBase.crew_died` | **EventBus**（同时保留本地 `event`） | 胜负判定/结算/名册在别的模块 |
| `PirateBase.hp_changed` | 本地 `event` | 仅自身血条/UI 关心 |
| `CrewSelectionHandler.focus_requested` | 本地 `event` | Battle 内部，暂不跨模块 |
| `AimController.aim_*` | 本地 `event` | 同一 GameObject 域 |
| `BattleHUD.camera_mode_changed` | 本地 `event` | UI 与 Battle 同场景 |
| `ModeSwitchButton.mode_changed` | 本地 `event` | UI 内部 |
| `WeaponMenu.weapon_selected` / `WeaponWheel.weapon_selected` | 本地 `event` | UI→Battle，prefab 引用直连即可 |
| `DebugFreeCamera.free_camera_toggled` | 本地 `event` | Battle 直接持有引用 |
| `Destructible.destroyed` / `Hazard.hazard_triggered` | **EventBus**（若移植） | 关卡层与玩法层解耦 |
| `ProjectileBase.projectile_landed` | 本地 `event` | 武器内部 |

---

## 4. 数值差异比对（口径：**Flash 原版是玩法权威基准**）

对照源：`config/{weapon,crew,balance,level}_config.json` + 各 `.gd` 内 `@export` 默认值 + `battle.tscn` 实例覆写 vs 静态文档 §4.1 / §5.2 / §5.3 / §3。

### 4.1 角色属性（Flash §4.1：全部 100 HP / weight=1 / 无差异）

| 项 | Flash 原版 | Godot config | Godot 场景实际 | 判定与建议 | 理由 |
| --- | --- | --- | --- | --- | --- |
| 角色种类 | 1 个 `Character` 类 + 20 种纯美术 | 6 种（sailor/gunner/sniper/hooker/pyromaniac/skeleton） | sailor/gunner/captain | **需回归基准**：属性不应按种类分化 | 原版「只有美术、luck、初始武器不同」，属性差异是 Godot 无意义漂移 |
| HP | 全 **100** | 60–110（skeleton 60 … hooker 110） | Pirate3=120、Enemy1/2=80 | **需回归基准**：M2 统一 100；若要保留坦克/脆皮，须写明是 3D 化设计 | 原版 TidalWave/爆炸对满血残血一视同仁 |
| weight | 全 **1** | 0.6–1.2 | captain 1.4/1.5、gunner 1.2 | **需回归基准**：weight=1；击退不乘体重 | 原版 `weight` 仅作重力常量，击退公式不含体重 |
| 移动能力 | **无**（角色不能走，只能被抛/弹弓） | `move_range` 6–10 | 未接 | **需回归基准/删**：`move_range` 在原版无对应概念 | Flash 是纯抛物线战斗，无走位 |
| luck | 每角色 1/2/5/10（关卡 XML） | **无** | 无 | **需补**：AI 评估次数基数，M2 AI 必需 | 静态文档 §6.3 公式依赖 luck |
| 选中/悬停 | 选框角 + 血条 28 帧 + 30px hover | 描边 shader + mask viewport | 在跑 | 机制可保留（3D 化合理） | 视觉载体不同属有意设计 |
| 落水即死 | **全局规则**（所有关恒有水面） | 仅 `shipwreck_cove` 标 `water_instant_death` | 无 | **需回归基准**：落水即死应为全局 | 原版每关都有 `<obj type="water">` |

### 4.2 武器（Flash §5.2 共 17 种 vs Godot 6 种）

| Flash 武器（size, maxDamage） | Godot 对应 | Godot config 数值 | Godot 运行时实际 | 判定与建议 | 理由 |
| --- | --- | --- | --- | --- | --- |
| cherryBomb（80, 40） | `bomb`? | `bomb` 50/r2.5 | — | 需重命名/对齐，**以 Flash 为准** | cherryBomb 是最弱新手武器，Godot 无直接对应 |
| dynamite（250, 70） | `bomb`（50/r2.5） | — | 需回归 250/70 | Flash 为准 |
| gunpowderBarrel（150, 30） | `gunpowder_barrel` | 35/r2.0 | `damage_center=60/damage_edge=20/blast_radius=4.0` | **三方都不一致**：config(35/2.0)≠代码(60/20/4.0)≠Flash(150/30)。**M2 以 Flash 为准**并统一真值 | 数值层要单一真值来源（W1-C 已规划） |
| boulder | 无 | — | — | 缺失，M2 可选补 | — |
| banana（160, 80，twangMax 30） | `knife`? | knife 25/r0.5 | — | `knife` **原版不存在**，需删或重定义 | 原版无飞刀 |
| mine（250, 70，跨回合） | 无 | — | — | 缺失 | — |
| parachuteBomb（160, 50，twangMax 30） | 无 | — | — | 缺失 | — |
| rumBottle（80, 25 → SweepingFlame 30/段） | `fire_bomb` | 20/r1.5/fire_duration 3.0 | — | 概念近似、实现不同；**以 Flash 为准** | 原版是蔓延火焰，不是区域持续伤害 |
| piecesOfEight（50, 25，可复用 8 次） | 无 | — | — | 缺失 | — |
| woodenCrate | 无 | — | — | 缺失（掩体类） | — |
| anchor（60 固定伤害） | `hook`? | hook 15「拉拽敌人或攀爬」 | — | **需回归基准**：原版 anchor 是垂直下砸 48px 宽、60 伤害，**不产生位移**；Godot hook 语义是编造 | — |
| seagull（50/发） | 无 | — | — | 缺失 | — |
| tidalWave（5/帧） | 无 | — | — | 缺失 | — |
| voodooDoll | 无 | — | — | 缺失 | — |
| cannon（经 cannonball 100/50） | 无 | — | — | 缺失 | — |
| SweepingFlame（30/段） | — | — | — | 缺失 | — |
| （原版无） | `poison_bomb` | 10/r3.0/poison 5.0 | — | **原版不存在**，建议删或明确标为 3D 新增 | 毒系统是 Godot 新增 |
| 武器数量 | **17** | 6 | 1（实际只投火药桶） | **需补至 17**（W1-C 已规划） | Flash 为权威基准 |

> 另注：`projectile_base.gd` 默认 `damage_center=40/damage_edge=15/radius=3.0` 是**无人使用的第三套数值**，翻译时不要当基准。

### 4.3 爆炸 / 伤害 / 击退（Flash §5.3）

| 项 | Flash 公式 | Godot | 判定与建议 | 理由 |
| --- | --- | --- | --- | --- |
| 半径 | `radius = size/2 + 20`（px） | `blast_radius` 直接给米（火药桶 4.0） | **3D 化需重定尺度**：先定「1 米 = ? px」再换算，或直接为 3D 另定半径并记录 | 2D px 与 3D m 量纲不同，属必要改动 |
| 伤害衰减 | `maxDamage * (1 - d/radius)` 线性，**边缘趋 0、无最小保底** | `lerp(damage_center, damage_edge, d/r)`（边缘 = damage_edge 非 0）；`DamageCalculator` 另有「20m 阈值 + 速率 0.02 + 最小 0.5」 | **需回归基准**：删最小伤害保底；`DamageCalculator` 的阈值衰减是漂移且该文件本来就没人用 | 原版边缘无保底是核心手感 |
| d==0 除零 | `nx=dx/d` 未防 0（Flash 里极小概率） | `t = dist/r` 但 `r>0` 判断有；`lerp` 无除零 | **M2 必须补 d==0 边界**（W1-D 已列） | 纯 C# 测试要覆盖 |
| 击退 | `k=0.06*falloff*maxDamage`；水平 `dir*5k`；垂直 `dir*5k - 6k`（总额外上抛） | 无对应；balance 里 `base_force=8.0`、`weight_resistance=0.8`、`ragdoll_threshold=30` | **需回归基准**：用 5k/6k，删 weight_resistance/ragdoll | 原版击退与体重无关 |
| evilness | 施暴者累加 falloff，AI 优先后续打 | 无 | **需补**（AI 依赖） | §6 AI 打分乘 `(1+evilness)` |
| 箱体连锁 | 命中箱体 AABB 最近点即 `box.explode()` | 无 | 可选补 | 火药桶连锁是原版特性 |

### 4.4 投掷 / 重力 / 物理（Flash §5.1、§5.4）

| 项 | Flash | Godot | 判定与建议 | 理由 |
| --- | --- | --- | --- | --- |
| 投掷初速 | `0.25 × 拖拽距离`，方向取反 | `THROW_FORCE=12`、`trajectory speed_scale=18`、`max_drag_distance=200` | **需回归基准**：0.25 系数 + 上限；且预览与实弹必须同源 | 三处数值互不一致，照抄会得到「预览≠实弹」 |
| 满力拖距 | `twangMaxForce/0.25`：默认 80px，twangMax30 的武器 120px | 200px | **需回归基准**：按武器 twangMax 反推 | — |
| 速度上限 | 角色 20；香蕉/跳伞/朗姆 30 | 无统一上限 | **需补** | — |
| 重力 | 每帧 `vy += weight`（weight=1，25fps，即 1px/帧²） | 工程物理默认 9.8；`pirate_base._THROWN_GRAVITY=-9.8`；`trajectory.gravity=-18` | **需统一**：先定 3D 重力常量，再让预览/实弹/被抛共用；不要三套 | 量纲不同属必要改动，但要单一口径 |
| 碰撞 | 2D 分轴 AABB 扫掠（tile>>5），撞地 `vy*=-bounce`、`|vx|-=friction`、撞墙 `vx*=-0.4` | 3D PhysX + RigidBody | **应重写**（3D 语义不同），见 §5.2 | — |
| 地雷跨回合常驻 | `limitedToTurn=false`、引信 60 帧 | 无 | 可选补 | — |

### 4.5 回合 / AI / 经济（Flash §3、§6、§7.3）

| 项 | Flash | Godot | 判定与建议 | 理由 |
| --- | --- | --- | --- | --- |
| 回合推进 | `inactivity > 10 帧`（≈0.4s 无活动）→ nextTurn | `max_turn_time=60` 且 `end_turn` 无人调用 | **需回归基准**：静止帧计数，不用计时器；并补齐调用链 | 原版无回合倒计时 |
| 每回合行动 | 每队 1 个角色，该角色「抛自己 1 次 + 用武器 1 次」 | 未定义 | **需补**（W1-D 三路径：抛自己/用武器/end go） | 原版严格两阶段 |
| 保底武器 | 每回合若无武器 → push `cannonball` | 无 | **需补** | §3.2 `startTurn` |
| AI | 完整：50 次随机自抛 + `luck` 次武器模拟 + 5 类特殊武器 + evilness | `ai_controller.gd` 空 TODO | **M2 核心补全点**：按 §6 自建 | 静态文档 §9.4 明确标注「最有价值的补全点」 |
| 胜负 | 一方全灭；玩家赢 `score += floor(avgHealth*20 - turns*25)` | `_check_victory` 只 print；`win_gold_*` 金币配置 | **需回归基准**：用得分公式，**删金币**（原版无经济系统） | 原版只有关卡得分+解锁 |
| 关卡数 | **33**（1P 1-15 / 2P 16-33） | 4 | **需补**：M2 先做 3 关代表（W1-C 已规划） | — |
| 比分/存档 | SharedObject `so_...`，解锁+分数 | M1 SaveManager 已备 | M3 再接 | — |

---

## 5. M2 落地建议

### 5.1 值得 1:1（或近 1:1）翻译的文件

1. `player_state_machine.gd` → 纯 C# `PlayerStateMachine`（除 `FOCUS` 待定，逻辑无 3D 依赖）。
2. `crew_selection.gd` 的**输入/双击/hover 编排**思路 → `CrewSelectionController`（射线实现换 `Physics.Raycast`）。
3. `mode_switch_button.gd` 的**状态语义**（移动/操作互斥）→ UGUI 版；自定义绘制部分重写。
4. `debug_free_camera.gd` → `DebugFreeCamera`（Minecraft 风格键位可直接照搬，仅 API 换名）。
5. `crosshair.gd` 的「中心准星 + 命中变色」→ UGUI 版。
6. `battle_hud.tscn` 的面板布局信息可作 UGUI 结构参考（TopRight 状态、BottomCenter 武器、BottomLeft 名册）。

### 5.2 应重写（3D 语义/架构不同，不要逐行翻译）

1. **物理与碰撞**：`pirate_base.gd` 的 `CharacterBody3D`+手搓重力、`projectile_base.gd`/`gunpowder_barrel.gd` 的 `RigidBody3D`+`apply_impulse`、Flash §5.4 的 2D 瓦片 AABB 扫掠 → 全部改 **Unity PhysX**（`Rigidbody` + `Collider` + `OnCollisionEnter`），投掷用 `ForceMode.Impulse`。
2. **伤害/爆炸结算**：`DamageCalculator`/两个武器脚本的内联 lerp → 抽到 W1-D 的 `ExplosionResolver` 纯 C#（`Physics.OverlapSphere` 只负责取候选，打分纯逻辑）。
3. **轨迹预览**：`trajectory_preview.gd` 的 30 球 + `speed_scale` → `LineRenderer`/对象池，且**必须与 `Ballistics` 纯逻辑同源**（消除预览≠实弹）。
4. **瞄准方向映射**：`aim_controller._compute_direction` 的屏幕→世界硬映射 → 用相机 `ScreenPointToRay` 投到地面/水平面再取反，或直接相机相对方向。
5. **回合/AI**：`turn_manager`+`ai_controller` → 纯逻辑 `TurnRules`/`AiEvaluator` + MonoBehaviour 薄壳，按 Flash §3/§6。
6. **描边**：`pirate_base.gd` 的 SurfaceTool 运行时合成 inverted-hull + SubViewport mask + post shader → URP Renderer Feature / ShaderLab 双 pass（Wave 2，遵守图形学调试截图规范）。
7. **HUD**：`hud.gd` 的运行时 `load()+set_script` 动态造 UI → UGUI Prefab + `[SerializeField]` 直连。

### 5.3 不移植（未接线/死代码/超出 M2 口径）

1. `scripts/level/island_generator.gd`（空实现，程序化地形另立里程碑）。
2. `scripts/level/destructible.gd`、`scripts/level/hazard.gd`（无场景接线，M2 最小闭环不需要）。
3. `scripts/combat/damage_calculator.gd`（死代码，逻辑并入纯 C# 结算器）。
4. `scripts/ui/weapon_wheel.gd`（死代码，用武器菜单/面板替代）。
5. `modules/pirate_crew/api.gd` 的信号壳（改为 `Core.EventBus` 契约 + `PirateCrewApi` 静态门面）。
6. `core/autoload/config_manager.gd` 与 `config/*.cfg` 读取路径（JSON 从未被加载；M2 用 `Scripts/PirateCrew/Data/` 纯 C# 真值来源替代）。
7. `core/services/audio_manager.gd`（未注册 autoload；音频非 M2 范围）。
8. `data_manager.gd` / `save_manager.gd`（M1 已有 Unity 版，不重复）。

### 5.4 翻译时**不要照抄**的 Godot 侧内部不一致（避免把 bug 搬进 C#）

- `config/weapon_config.json` 的火药桶 35/2.0 ≠ `gunpowder_barrel.gd` 的 60/20/4.0 ≠ Flash 150/30 → 统一以 Flash 为准，单一真值。
- `THROW_FORCE=12` ≠ `trajectory_preview.speed_scale=18` ≠ `gravity=-18` ≠ `_THROWN_GRAVITY=-9.8` → 全部归一到 `Ballistics` 常量。
- `pirate_base.gd` 的 `weight` 语义（原版仅重力）与 config 的 0.6–1.2「重量影响投掷距离」不是一回事；若保留，须明确是 3D 设计。
- `battle.tscn` 覆写的 `hp=120/80`、`weight=1.4/1.5`、`crew_type="captain"`（config 里没有 captain）都是场景级漂移，M2 统一回 100/1。

### 5.5 与 Flash 基准的取舍总原则（本摘要口径）

1. **玩法规则、公式、数值、战斗节奏** → Flash 原版权威，Godot 漂移一律「需回归基准」。
2. **3D 载体**（物理引擎、坐标、相机、UI 框架、渲染管线）→ 按 Unity/URP 重写，注明这是必要改动。
3. **Godot 有意新增且自洽的设计**（如 3D 描边 hover 反馈、双击聚焦、自由相机）→ 可保留，但要在计划里写明「非原版」。
4. **未接线/空 TODO** → 一律不作为翻译来源；AI 按静态文档 §6 自建。

---

## 6. 附录：关键行号速查

| 事项 | 位置 |
| --- | --- |
| AI 空 TODO | `ai_controller.gd:17` |
| 回合不推进 | `turn_manager.gd:40`（`end_turn` 无调用）+ `battle.gd:191` |
| 胜负只 print | `battle.gd:246-258` |
| 预览≠实弹 | `battle.gd:24` vs `trajectory_preview.gd:15,21` |
| 底部武器面板空 | `hud.gd:114-121`（清空不重建） |
| 占位武器表 | `hud.gd:26`、`weapon_menu.gd:15` |
| role 模式 UI 空壳 | `hud.gd:165-168`、`mode_switch_button.gd:129-136` |
| 瞄准逻辑留待任务6 | `crew_selection.gd:187` |
| 火药桶运行时数值 | `gunpowder_barrel.gd:16-20` |
| config 火药桶数值 | `config/weapon_config.json:3-9` |
| JSON 从未被加载 | `core/autoload/config_manager.gd:19` |
| Flash 武器总表 | `docs/技术/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md` §5.2（L407-429） |
| Flash 爆炸公式 | 同上 §5.3（L431-458） |
| Flash AI 伪代码 | 同上 §6（L513-637） |
| Flash 回合流程 | 同上 §3（L125-244） |
| Flash 与 Godot 对照 | 同上 §9（L758-808） |

---

*本文件由 W1-B 子任务产出，只读引用 Godot 仓库；未修改 `pirate-crew/` 下任何文件。*
