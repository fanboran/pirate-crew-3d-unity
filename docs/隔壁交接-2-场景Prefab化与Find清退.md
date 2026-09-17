# 隔壁专项交接 2：场景 Prefab 化与 Find 存量清退（架构 Track 8④）

> **临时文档**：任务完成并回写结果后删除本文档。你（新会话 AI）只做本任务，不顺手修别的。
> 进项目先读 AGENTS.md（工作区根），再读本文。本文重述关键铁律，AGENTS.md 是全集。

## 任务是什么

把 Battle 场景的组装方式从「生成式场景 + 顺序敏感三步脚本链」重构为
「稳定结构 Prefab 化 + `[SerializeField]` 显式连线」，随后清退运行时代码里的
`FindObjectOfType` 存量。这是五审交叉后公认的**影响面最大的结构工程**（难度 ★★★★），
所以单独开本对话做，分阶段推进、每阶段可回退。

## 为什么（审计证据）

- `docs/审计/架构审计报告.md` P0-2（34539 行 Battle.unity 是生成产物却当真源入库；
  接线知识活在一次性 Editor 脚本而非 Prefab 连线；程序化装配没有静态引用可连，
  **直接催生** P2-7 的 Find 存量——审计里有明确因果链）、P2-7（Find 存量表）、
  P2-9（Prefab 未按模块归位）。
- `docs/审计/代码审计报告.md` §三.4（同一批 Find 的明细，含 BattleCameraController
  每帧扫描的热路径）。
- 现实教训：视觉审计批次 A 修的"水模拟被 SetActive(false) 整条杀死"
  （`docs/审计/视觉审计报告.md` §二.1）就是这类接线病的典型发作——没有显式连线的
  场景，退役/接管关系只能靠隐式约定，一改就断。

## 前置条件（开工前核对，未满足就先等）

1. **场景接线审计**（本对话自己做，作为第一阶段）：打开编辑器实测 Battle.unity 内部层级，
   统计哪些是"可 Prefab 化的稳定结构"、哪些是"按关卡生成的内容"。
   `docs/审计/README.md` 待补表第一行本来就要求这项先做。
2. **主协调线的 Track 8①② 已完成**：BattleController 反向依赖修正 + asmdef/目录/命名空间
   三合一。本任务会大规模移动/重建场景与脚本引用，必须等 asmdef 边界立稳之后再动，
   否则两件大工程互相踩。核对方法：git log 里有
   `refactor(arch): …asmdef…` 类提交、`Assets/Scripts/` 下已是多 asmdef 结构。
   若未完成，本对话只做第 1 阶段（审计+方案），不动场景。

## 现状事实（2026-09-17 审计基准）

- `Assets/Scenes/Battle.unity` 34539 行；MainMenu 8030、LevelSelect 4002、CrewManagement 1849、
  Bootstrapper 220。
- 生成链（顺序敏感，交接文档 §2.1）：
  `Editor/M2BattleSceneSetup.BuildAll → Editor/HudMinimapSceneSetup.WireMinimap → Editor/M3SceneSetup.BuildAll`。
- Find 存量（清退清单，架构审计 P2-7 + 代码审计 §三.4）：
  `BattleCameraController.cs:397/412/1107`（aimThrow，前两处在每帧路径上——优先修）、
  `UI/BattleHud.cs:326`（相机，有缓存可容忍）、`BattleController.cs:362`（跨模块找 AmbientDirector）、
  `BattleController.cs:378-388`（按名字前缀 "IslandTile_" 找 UI Image——隐式字符串契约）、
  `Audio/AudioService.cs:994`（AudioListener 兜底）、`Fx/FxRoot.cs:145/307`（battle_started
  时找 BattleController / 回落找 PirateBase[]）、`Water/WaterSimulationDriver.cs:348`（找 Light）。
  行号可能漂移，以当前代码为准逐个核对。

## 工作分解（五阶段，每阶段独立提交、门禁全绿才进下一阶段）

> ### ✅ 阶段 1 已完成（2026-09-17，基准 `3c6b51d`）
>
> **前置核对结果：Track 8①② 未完成**（`Assets/Scripts/` 下仍是 `PirateCrew.Runtime` +
> `PirateCrew.Rendering` 两个 asmdef，无三合一提交）→ 按本文规定**只做了阶段 1，未动场景**。
>
> 交付物：
> - `pirate-crew/Assets/Editor/SceneWiringAudit.cs`（转储/比对工具，阶段 3 的等价性验收仪器）；
> - [`docs/审计/场景接线审计报告.md`](审计/场景接线审计报告.md)（F-1…F-9 + 四 Prefab 切分契约 + 阶段 2–5 细化）；
> - `pirate-crew/Assets/Scenes/README.md`（场景契约：装配链铁律 + 三类切分 + 改前必跑转储）。
>
> 实测要点：372 对象 / 23 根 / Prefab 实例 0 / 1741 引用（跨根 26 条，且**全部是场景内互指**→
> 整体收进单 Prefab 即可全部内化）；Find 存量按根因分三类（字段未序列化 / 字段不存在 / 全局兜底）；
> 163 个 `IslandTile_*` 按 level 1 烘死混在 HUD 里；新发现 `Scene_SandWet.mat` 缺失致运行时该组静默跳过。
>
> **阶段 2–5 的开工条件**：等 Track 8①② 落地（否则与 asmdef 大搬家互相踩）。届时按审计报告 §五 推进。

1. **场景接线审计**：编辑器实测层级 → 产出「归 Prefab / 留生成」切分契约
   （写进 AGENTS.md 场景节或 `Assets/Scenes/README.md`，标注哪些结构手工、哪些生成）。
2. **试点 Prefab 化**：HUD 栈（BattleHudBuilder 产物）、相机 rig、队伍挂点、服务宿主——
   Prefab 内连好 `[SerializeField]`，入库 `Assets/Prefabs/` 按模块归位（现只有 PirateCrew/ 子目录）。
3. **Setup 脚本收缩**：三个 Setup 脚本只保留"按关卡参数生成内容"的部分，
   稳定结构改为实例化 Prefab；**重跑 Setup 链后场景行为必须等价**（这是验收标准，
   不是"能跑就行"）。
4. **Find 清退**：上表逐处替换为 `[SerializeField]` 引用/装配期注入；
   FxRoot 那两处若需给 `crew_damaged`/`crew_died` 载荷补 Position 字段，
   走 EventBus 契约流程（改 `docs/EventBus事件契约.md` + 常量类，载荷变更属破坏性变更）。
5. **收尾**：场景文件 diff 人工复核（不允许静默丢对象）；交接文档 §2.1 的生成链描述更新。

## 红线

1. `Assets/Scripts/Core/`（引导器/事件总线/存档）经评审后修改须谨慎——本任务原则上不碰 Core。
2. 每阶段门禁：batchmode 编译 0 错 + EditMode 全绿 + PlayMode 7 条全绿 +
   重跑 Setup 链等价性验证。
3. 一次只跑一个 Unity 进程（Library 独占锁；多对话并行时错开 batchmode 时段）。
4. 角色模型不许改；场景资产走 Blender 管线（本任务不新建场景资产，只重组）。

## 环境速查

Unity：`F:\Unity\2022.3.62f1c1\Editor\Unity.exe`；batchmode 必须带
`-projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew"` 且 `-nographics`。
提交格式 `类型(模块): 描述`，中文。

## 完成后

1. 回写 `docs/审计/架构审计报告.md`（文末加"修复回写"节，P0-2/P2-7/P2-9 逐项销号，附提交哈希）
   与 `docs/审计/汇总-五审交叉与修复统筹.md` §七；
2. AGENTS.md 若新增场景契约（哪些归 Prefab/哪些归生成），同步更新；
3. 待办板对应项打钩；删除本文档。
