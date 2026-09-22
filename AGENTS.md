> **说明**：本文件是 AI 辅助开发的**主规则文件**。由原 Godot 项目 `.trae/rules/`（rule.md 通用规范 + rule_local.md 项目身份 + 参照库/截图两大强制规范）与姊妹项目 stick-world 的 AGENTS.md 融合改写而来，适配 Unity 工作流。

***

### 项目背景（前因后果，新会话必读）

- **这是什么**：求职作品集项目。本人 2027 届计算机本科，求职游戏客户端开发实习；本项目的定位是**简历上的 Unity 实战素材**——证明"引擎概念相通，换引擎能干活"。
- **前因**：海盗军团夺宝 3D 原版是 Godot 4 项目（位于 `../game-3/pirate-crew-3d/`，58+ 提交，已完成战役/船员管理/海盗船员三模块骨架与部分玩法）。为求职 Unity 岗位立项本仓库做 **Unity 2022.3 重制**。
- **两个版本的关系**：Godot 版是**设计参照与功能基准**（玩法规则、数值、场景内容以它为准）；本仓库是 Unity 实现，代码与场景按 C# / GameObject 规范重写（翻译规范表见 README）。Godot 版继续独立存在，两边不互相改代码。
- **节奏约束**：这是求职侧项目，主项目（stick-world，`../game-2/`）优先级更高；本项目按里程碑（M0 骨架 → M1 流转 → M2 战斗 → M3 管理循环）推进，M2 之前不碰海战/大世界等远期系统。

***

### 注意事项

> ⚠ **空间模型铁律（2026-09-13 血的教训，本项目的定位约束）**：本工程是 **3D 重制**，参照 Godot 基准
> （XZ 地面竞技场 + 45° 三维相机 + 单位沿 Z 分路）。**竞技场是 XZ 水平面，重力沿 -Y，地面顶面 y=0**。
> 第一版曾把 Flash 原版的 2D 侧视坐标 1:1 搬进 Unity 的 XY 竖直平面、并用 `FreezePositionZ` 焊死深度，
> 结果被用户一眼看穿是"披着 3D 引擎的 2D 游戏"——**不要重犯**。
> 动坐标/相机/投掷/爆炸之前先读 [docs/设计/M2-3D空间模型对齐.md](docs/设计/M2-3D空间模型对齐.md)：
> 其中定死了 **Godot 出空间结构、Flash 逆向文档出数值** 的分工（Godot 版的弹道/伤害层是自相矛盾的占位实现，
> 不能照抄它的数值）。

- ⚠ **建模分工铁律（2026-09-17 用户两次纠正后定案，别再搞反）**：
  **① 角色模型不许改**——现有程序化低模船员（胶囊+三角帽，`CrewVisualPrefabBuilder` 产出）就是最终风格
  （"这就是低多边形风格的游戏"），任何换网格/加细节的想法一律先提案；
  **② 场景/环境建模走 Blender**——岛台、船体、栈桥、陈设等场景资产由 Blender 无头管线建模后导入
  （`tools/blender/scene/`，FBX 入 `Assets/Art/Models/SceneKit/`），这是用户明确要求的主线；
  ③ 应用图标也已走 Blender 管线（`tools/blender/icon/`）。
  样板先行：新场景资产先 showcase 给用户过目，过审才接入战斗场景与批量。
- 使用中文回答问题，用中文写提交信息和 Git 日志。
- Git 提交格式：`类型(模块): 描述`，示例：`feat(pirate_crew): 实现舰船接舷战状态机`
- Unity 编辑器路径：`F:\Unity\2022.3.62f1c1\Editor\Unity.exe`
- 无头验证项目完整性（改完工程结构/manifest 后必跑）：
  ```bash
  "F:/Unity/2022.3.62f1c1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" -logFile -
  ```
  退出码 0 = 工程可打开；非 0 先查输出里的 error 再处置。
- 无头跑测试（M1 起有 EditMode/PlayMode 测试）：
  ```bash
  "F:/Unity/2022.3.62f1c1/Editor/Unity.exe" -batchmode -nographics -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" -runTests -testPlatform EditMode -testResults "$TEMP/pc3d-test-results.xml" -logFile -
  ```
  （PlayMode 把 `EditMode` 换 `PlayMode`；test-results 写系统临时目录，跑测产物不落 `external/`。）
- **batchmode 铁律**：必须显式带 `-projectPath` 且加 `-nographics`。缺 `-projectPath` 会打开 EditorPrefs 里的"最近工程"（可能污染/锁住别的项目——本项目曾因此产生杀不死的僵尸进程卡住 `Temp/UnityLockfile`，只能重启机器清理）；当前环境不带 `-nographics` 会卡在 GfxDevice 创建。一次只跑一个 Unity 进程。
- **无头验证台**（M2 起，不入库）：`external/m2-harness/` 用 `dotnet` 引用 Unity 已编译程序集 + NuGet NUnit，**不启动 Unity 就能编译工程源码并跑纯 C# 测试**，用于多 agent 并行时绕开 Library 独占锁。
  ```bash
  # 首选 run.sh（自动建副本 + 传 ProjectRoot + 跑完即删）：
  external/m2-harness/run.sh <All|Data|Combat|DataEditor>
  # 手工副本时【必须显式传 -p:ProjectRoot】——csproj 用自身位置反查工程根，
  # 副本放临时目录后反查会指向不存在的路径，Compile 通配符抓不到任何文件，
  # 编译会"0 错误"地空转过（2026-09-22 实测踩坑）：
  H="$TEMP/pc3d-harness-<域>" && mkdir -p "$H" && cp external/m2-harness/M2Harness.csproj "$H"/
  cd "$H" && dotnet test M2Harness.csproj -p:ProjectRoot="$PWD_ORIG/pirate-crew" -p:HarnessScope=<All|Data|Combat|DataEditor>
  ```
  **边界**：`GameObject` / `MonoBehaviour` / `ScriptableObject` 的实例化走原生 `ECall`，脱离 Unity 运行时必抛 `SecurityException`；所以战斗数值/回合规则这类核心逻辑**刻意写成纯 C# 静态类**以便无头测试，MonoBehaviour 胶水层仍由 batchmode 收口。详见 `external/m2-harness/README.md`。
- **GitHub 查询纪律**：查 GitHub 上的代码/仓库/README 用 WebFetch 或 `git clone --depth 1`；**不要用匿名 `curl` 硬打 `api.github.com`**（匿名限额 60 次/时，触发限流后连正常诊断都会被污染）。
- **`export/` 产物入库策略**：`export/` 是出图**暂存目录**，整目录 gitignore、零例外——原始 PNG、过程截图、评审画廊等中间数据全部本地留存（判图脚本复跑也以它为输入）。**被文档引用的图与档案包入库到 `docs/images/`**：README / docs / tools 文档实际引用的展示图、判据档案、调试 README 及各轮 `诊断报告.md` 都在这里，包内相对结构原样保留（如 `docs/images/outline-debug/`、`docs/images/art-review/<轮次>/诊断报告.md`）。入库流程：截图压成 1280 宽 JPEG（单张几百 KB 内）→ 拷进 `docs/images/<包名>/` → 在文档里引用它。
- 渲染管线：**URP**（对应 Godot 版 Forward Plus 的 3D 定位）；3D 物理用内置 PhysX，寻路用 AI Navigation 包（NavMesh）。
- 改进待办项记录在 `docs/项目/待办事项.md`
- **Unity MCP**：本工程已装 MCP for Unity（v10.0.0），ZCode 已配 `unity-mcp` server——Unity 编辑器打开本工程时，新会话的 AI 可直接用 manage_scene / manage_gameobject / manage_asset / read_console 工具操作编辑器（写完脚本先 read_console 查编译错误再用）；编辑器没开时这些工具不可用，改用 batchmode 验证

### 文档写作规范（借鉴姊妹项目 stick-world，本仓库同样适用）

- **普通文档只写「是什么 / 怎么设计 / 为什么这么设计」**，不写「什么时候改的 / 之前是什么」这类变更记录。**Git 本身就是文档的历史版本**，变更过程交给提交历史，不在正文复述。
- **AI 提案必须显式标注**：凡是 AI 生成、未经确认的设计/数值/命名提案，须在所在文档标注「提案/待定」；其他文档引用时不得当作已确认设定——防止提案被反复复读成"事实"（走查时会把提案当基准，越滚越偏）。
- 例外：**专门记录变更的文档**（`docs/项目/待办事项.md` 的归档区）可以写变更过程与提交哈希。
- **引用要可核对**：数值、行为、断言的出处写「`ai_controller.gd:17`」这种带文件行号的引用，不写"某处""文档里说"。本次 M2 的所有逆向结论都按这个要求落档。

### 会话交接（长任务跨对话）

- 长任务（M2/M3 这类多阶段实施）会跨多个对话会话。当上下文过长、判断质量可能下降时，**主动建议用户开新对话**，不要硬撑。
- **交接前必须**：把进度 / 下一步任务分解 / 关键决策与理由 / 新会话恢复指引写进 [docs/项目/交接与恢复指南.md](docs/项目/交接与恢复指南.md)（任务板更新到 `docs/项目/待办事项.md`）并提交。
- **新会话恢复**：用户说「继续」时，先读 `docs/项目/交接与恢复指南.md` → `docs/项目/待办事项.md` → 本文「注意事项」，再干活，不重新摸底。
- **多 subagent 并行时文件域必须互不重叠**；涉及 Unity 的验证（batchmode）由协调者串行执行——Library 锁是独占资源，一次只能一个 Unity 进程。

### 文档导航

| 要做什么 | 读哪个 |
| --- | --- |
| **先看这个：新会话恢复入口** | [docs/项目/交接与恢复指南.md](docs/项目/交接与恢复指南.md)（项目状态 / 环境铁律 / 关键决策与理由 / 遗留 TODO / 恢复顺序） |
| 当前待办 / 已完成归档 | [docs/项目/待办事项.md](docs/项目/待办事项.md) |
| **查文档总目录（按设计/技术/审计/项目分类）** | [docs/README.md](docs/README.md) |
| **架构怎么分层、代码该放哪、生命周期怎么走** | [docs/技术/架构/架构总览.md](docs/技术/架构/架构总览.md)（程序集/目录/唯一入口→组合根/事件契约/数据资产/场景接线/改动落点导航） |
| **环境怎么搭、规范是什么、命令怎么跑（新人/新会话）** | [docs/项目/开发者指南.md](docs/项目/开发者指南.md)（含无头验证台用法与能力边界、常见任务配方） |
| 出包怎么出、版本号从哪来、CI 与上架清单 | [docs/项目/构建与发布手册.md](docs/项目/构建与发布手册.md) |
| **本次工业级重构的范围/判据/执行记录** | [docs/项目/工业级重构总纲.md](docs/项目/工业级重构总纲.md) 与 [docs/项目/重构迁移报告.md](docs/项目/重构迁移报告.md) |
| **当前主任务书（美术翻新立项，提案待确认）** | [docs/技术/美术翻新-等距像素卡通立项任务书.md](docs/技术/美术翻新-等距像素卡通立项任务书.md)（里程碑 M0→M5 / 技术方案总纲 / 裁决点登记表 / 风险登记册 / 排期） |
| 查渲染/资产实现口径与外部调研 | [docs/技术/渲染/README.md](docs/技术/渲染/README.md) 与 [docs/技术/资产管线/README.md](docs/技术/资产管线/README.md)（等距像素卡通口径 + 赛璐璐/描边/像素化/量化四份调研，来源均带 URL） |
| **做 UI 像素化（Beveled Pixel）前必读** | [docs/images/ui-pixel-ref/README.md](docs/images/ui-pixel-ref/README.md)（创始人指定的标准参照 + **逐像素量出的斜面几何表**：2px 基本单位、框架只用同色相三档明暗、格子件字形单元 16px）+ [美术风格指南](docs/设计/美术风格指南.md) §5；参照素材在 `external/terraria-ref/`（137 张成就图标 + 41 张 UI 原始件，版权原因不入库） |
| 查资产架构三层分离（烘焙器岗位） | [docs/技术/架构/管线合并-糖豆人式资产架构任务书.md](docs/技术/架构/管线合并-糖豆人式资产架构任务书.md)（程序化生成改岗编辑器烘焙器，几何退出运行时） |
| 了解项目目标、翻译规范、里程碑 | [README.md](README.md) |
| **查数值与玩法的权威依据（公式/武器表/回合规则/AI 伪代码）** | [docs/技术/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md](docs/技术/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md)（2D 原版 Flash 逆向，868 行） |
| 查 Godot 版某文件该翻译成什么 / 哪些是空骨架 | [docs/技术/参考逆向/M2-Godot基准摘要.md](docs/技术/参考逆向/M2-Godot基准摘要.md)（`.gd → .cs` 对照 + 空 TODO 清单） |
| 查 Unity 实现参照 / 本机 API 陷阱 | [docs/技术/架构/M2-Unity参照库调研.md](docs/技术/架构/M2-Unity参照库调研.md)（4 个参照库 + R1-R9 风险清单） |
| 加跨模块事件 / 查事件契约 | [docs/技术/架构/EventBus事件契约.md](docs/技术/架构/EventBus事件契约.md)（事件名与载荷登记表，禁止散落魔法字符串） |
| 调描边 shader 参数 | [docs/技术/渲染/描边Shader调试.md](docs/技术/渲染/描边Shader调试.md) |
| **改 Battle 场景 / 装配链前必读** | [pirate-crew/Assets/Scenes/README.md](pirate-crew/Assets/Scenes/README.md)（**折叠态契约**：四个场景各是一个 Prefab 实例，不许往里手摆东西 / **八步装配链**顺序即代码 / 改前必跑的转储比对命令） |
| 查场景接线的实测证据与修复计划 | [docs/审计/专项/场景接线审计报告.md](docs/审计/专项/场景接线审计报告.md)（372 对象/23 根/26 条跨根引用/Find 存量根因，基准 `3c6b51d`） |
| **查 3D 空间模型 / 坐标口径（动坐标、相机、投掷、爆炸前必读）** | [docs/设计/M2-3D空间模型对齐.md](docs/设计/M2-3D空间模型对齐.md)（**XZ 竞技场** + Flash 数值的分工契约） |
| （已归档）M2 3D 化并行分工记录 | [docs/项目/归档/M2-3D化-并行推进与交接.md](docs/项目/归档/M2-3D化-并行推进与交接.md) |
| 查 Godot 版某系统怎么设计的 | `../game-3/pirate-crew-3d/docs/` 与 `../game-3/docs/` |
| 查 Godot 版某功能怎么实现的 | `../game-3/pirate-crew-3d/modules/`（campaign / pirate_crew / crew_management） |
| 查 AI 规则的原始出处 | `../game-3/.trae/rules/`（rule.md 通用规范） |

***

## 核心行为指令

1. **参照库强制**（自原项目 rule.md，全项目最重要的规则）：AI 无参照写代码容易 API 幻觉、边界遗漏，但**翻译移植和等效重构表现非常稳定**。每次写新功能或重写模块前——
   - 下载星标多、维护活跃、玩法相似的开源 Unity 项目到 `external/<功能名>-reference/`（已 gitignore），clone 完成后在 [docs/技术/架构/M2-Unity参照库调研.md](docs/技术/架构/M2-Unity参照库调研.md) §7 登记（库名 / 上游 URL / 用途）；
   - 读懂其核心实现后基于参照翻译改编；
   - 参照库不入库。例外：简单 bug 修复、单行改动、参数调整。
2. **图形学调试截图规范**（自原项目 rule.md，Unity 版）：Shader/渲染效果开发必须——在 shader 中实现 `debug_mode` 拆分管线步骤 → 用 `ScreenCapture.CaptureScreenshot` 或 Editor 脚本逐层截图存 `export/<功能名>-debug/` → 写一页 README 说明每张图应看到什么 → 列关键参数调参指南。
   - **改动 shader 必须先在**有渲染路径的编辑器里**验证编译**："`read_console` 里 0 条 shader error"是唯一算数的标准。静态核对 `#include` / 符号**只能证明"符号存在"**，证明不了 include 链自洽、更证明不了额外 Pass 会被执行——两个真实事故：① `GlobalIllumination.hlsl` 自身不 include `BRDF.hlsl` 却在函数体用 `BRDFData`；② 描边 Pass 与本体 Pass 同标 `LightMode="UniversalForward"`，被 URP **静默丢弃**（本体正常、就是没描边、Console 无报错）。完整复盘见 [docs/技术/渲染/描边Shader调试.md](docs/技术/渲染/描边Shader调试.md) §八。
   - **URP 里加额外 Pass 必须给它一个独立且未被占用的 `LightMode`**（本项目描边 Pass 用 `"SRPDefaultUnlit"`），不能复用本体 Pass 的 `UniversalForward`。
   - **出图优先走 MCP，不另开 Unity 进程**（`Library/` 锁独占）：编辑器开着时用 `execute_menu_item` 跑采集菜单、`manage_camera` 取图、`read_console` 查 shader 报错；采集期间**编辑器不能暂停**（画面不重绘）。注意 `manage_camera` 的 `output_folder` 要显式指定，默认会往 `Assets/Screenshots/` 落图，会把临时截图混进 Unity 资产。判图要用**程序化判据**（按色相扫像素 + 区域主色），不要只靠"看着像"。
   - 参考模板：`docs/images/outline-debug/`（README 含每张图的预期 + 实测像素判据 + 复现步骤）。
3. **测试驱动**：核心逻辑（战斗数值、船员管理状态流转）附带 Unity Test Framework（NUnit）测试，放 `pirate-crew/Assets/Tests/`。
4. **安全第一**：`pirate-crew/Assets/Scripts/Core/`（引导器/事件总线/存档）经评审后修改须谨慎——它是所有模块的地基。
5. **原子化提交 + 主动沟通**：每次提交一个独立最小功能；任务描述不清或与架构原则冲突时主动提问，不做危险假设。

***

## Unity 模块化架构原则

> 从 Godot 模块化四原则翻译而来，精神一致、载体不同。Godot 模块 → Unity 目录映射见 [README.md](README.md)。

1. **两层结构**：仓库根放文档与 AGENTS.md，Unity 工程本体放 `pirate-crew/` 子目录（Unity Hub 打开的是它，不是仓库根）。
2. **模块划分**：`Assets/Scripts/` 下按功能分 `Core/`（引导器/事件总线/存档）、`Campaign/`、`PirateCrew/`、`CrewManagement/`、`UI/`；一个模块一个 C# 命名空间（`PirateCrew.Combat` 等）。
3. **耦合原则**：模块间通信走 `Core/EventBus.cs`（静态 C# 事件中心，对应 Godot 的 event_bus autoload）；**禁止** `GameObject.Find`、跨模块 `GetComponent` 裸引用。跨模块调用的公共出口放各模块 `XxxApi.cs`。
   - **事件契约必须登记**：EventBus 用字符串键，没有编译期检查——拼错一个字母就是"发了但没人收到"的静默故障。事件名一律 snake_case，且必须登记在 [docs/技术/架构/EventBus事件契约.md](docs/技术/架构/EventBus事件契约.md)（事件名 / 载荷类型 / 发布方 / 订阅方）；代码侧用常量类承载（如 `BattleEvents.cs`），禁止散落魔法字符串。载荷类型变更属破坏性变更，须同步更新登记表与所有订阅方。
   - **不该用 EventBus 的场景**：同一模块内部调用（直接方法调用或 `[SerializeField]` 引用）、父子层级生命周期通知、每帧高频数据（`Publish` 会分配委托快照，高频吃 GC）。别把强关系伪装成松耦合。
4. **命名规范**：C# 类型与文件 PascalCase（文件名=类名）；Godot 版搬来的 snake_case 资产入 Assets 时重命名。Prefab 按模块归位，每个可实例化场景一个 Prefab。
5. **依赖分层**：`Core/`（服务层）← 玩法模块（Campaign/PirateCrew/CrewManagement）← Bootstrapper 场景（组装根，`DontDestroyOnLoad` 挂全局服务）。高层可依赖低层，反向禁止。
6. **单例约定**：全局服务（存档、事件总线、场景流转）由 Bootstrapper 场景创建并 `DontDestroyOnLoad`，禁止场景里手工摆放重复实例。
7. **翻译纪律**（对应 README 翻译规范表）：一个 `.gd` 对一个 `.cs`；信号 → `event Action<T>`；`@export` → `[SerializeField]`；Godot 场景树语义 → Prefab 层级，**先读 Godot 版实现再动手**。

***

## 顶层目录结构

```
pirate-crew-3d-unity/           # 仓库根（文档与规则）
├── AGENTS.md                   # 本文件
├── README.md                   # 项目门面（翻译规范表在此）
├── docs/                       # 分类文档（设计/技术/审计/项目/images，总目录 docs/README.md）
├── external/                   # 工作台（gitignored）：参照库 / harness母本 / 逆向材料 / Blender建模源四类；跑测产物禁入，公约见调研文档§7
└── pirate-crew/                # Unity 工程本体（Unity Hub 打开这个）
    ├── Assets/
    │   ├── Scripts/{Core,Campaign,PirateCrew,CrewManagement,UI}/
    │   ├── Tests/              # NUnit 测试
    │   ├── Prefabs/  Scenes/  Art/  # Plugins/ 等目录在首次放入资产时再建（空目录不入 git，防孤儿 meta）
    ├── Packages/               # manifest.json（AI Navigation / Cinemachine）
    └── ProjectSettings/        # Unity 工程设置（URP）
```
