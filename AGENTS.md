### 注意事项

- 使用中文回答问题，用中文写提交信息和 Git 日志。
- **参照游戏（Nitrome《Mutiny》/「海盗军团抢宝藏」）只作灵感库——玩法借概念，地图/关卡一律自研。**
  立项至今口径未变：不是转写、不是照抄、不是移植。所谓"翻译关卡"仅指取一个概念当灵感
  （例：原版某关有两艘船 → 我们建模两艘大船并排行驶），**不搬布局、不搬瓦片**。
  任何文档 / 代码注释 / 新会话**不得把现役内容描述成"抄原版 / 转写 / 移植关卡"**；
  见到这类表述按缺陷处理（归档文档除外——那是历史记录）。
- 可以调用Subagent组成Agent team。
- 动坐标/相机/投掷/爆炸之前先读 [docs/设计/3D空间模型对齐.md](docs/设计/3D空间模型对齐.md)。
- 角色模型不许改——现有程序化低模船员就是最终风格，这就是低多边形风格的游戏。
  场景/环境建模走 Blender——场景资产由 Blender 无头管线建模后导入（`tools/blender/scene/`，FBX 入 `Assets/Art/Models/SceneKit/`）。
- 应用图标也已走 Blender 管线（`tools/blender/icon/`）。
- Git 提交格式：`类型(模块): 描述`，示例：`feat(pirate_crew): 实现舰船接舷战状态机`。
- 查 GitHub 上的代码/仓库/README 用 WebFetch 或 `git clone --depth 1`；**不要用匿名 `curl` 硬打 `api.github.com`**（匿名限额 60 次/时，触发限流后连正常诊断都会被污染）。
- 被文档引用的图与档案包入库到 `docs/images/`。
- 渲染管线：**等距像素卡通**（像素化着色路径，唯一渲染路径，口径见 [docs/技术/渲染/像素化着色路径/实现口径.md](docs/技术/渲染/像素化着色路径/实现口径.md)）；3D 物理用内置 PhysX，寻路用 AI Navigation 包（NavMesh）。
- 改进待办项：`docs/项目/待办事项.md` 只放**一行一条的索引**，每个任务的详情放
  `docs/项目/待办/<任务名>.md`（**一任务一文件**，格式 `# 标题` + `> 一句话现状/卡点` + `## 详情`）；
  任务收官时把该文件移入 `docs/项目/待办/归档/`，并在该目录 `README.md` 的清单里加一行
（索引文件只留一个指向归档目录的入口）。

### 调试规范

- Unity 编辑器路径：`F:\Unity\2022.3.62f1\Editor\Unity.exe`。
- **无头验证首选分级运行器**（单飞锁 / 日志落 `$TEMP` / 失败自动摘 error / `open`+`test` 两档 30 分钟结果缓存）：
  ```bash
  tools/headless/run.sh <open|test|build|harness> [参数]
  ```
  - `open` — 整工程可打开验证（分钟级）。**只在改了 manifest / Packages / ProjectSettings / 程序集定义 / .meta 结构后跑**；仅改 C# 或资产内容用 `harness` 档。
  - `test [EditMode|PlayMode] [过滤器]` — Unity Test Framework，默认 EditMode。过滤器是**测试名子串**（如 `BattleSceneWiringTests`），可只跑一块；纯 C# 域测试优先 `harness` 档（秒级）。
  - `build <类名.方法名> [透传参数…]` — 无头构建/烘培（如 `BuildScript.BuildFromCommandLineArgs`），建议后台发起。
  - `harness <All|Data|Combat|DataEditor|Battle|Runtime>` — 代理 `external/harness/run.sh`，不开 Unity、绕开 Library 独占锁。
- **启动次数是成本，用例数量不是**：单次 Unity 冷启动 ≈2.5 分钟，而 EditMode 1300 条用例本体只要 ≈16 秒——
  提速要**少启动**（选最小档 / 吃缓存 / 只跑过滤器），**不要靠少写测试**。同 HEAD + 同工作区重跑命令即命中缓存
  （强制真跑加 `PC3D_NO_CACHE=1`）。一次验收**不默认 open/EditMode/PlayMode 三档全跑**；
  按改动面选档的口径见 [docs/技术/无头验证与启动成本优化.md](docs/技术/无头验证与启动成本优化.md) §6。
- 等无头任务用**后台完成通知**，禁止 `sleep` 轮询。
- 直接调 Unity 的等价命令（不经运行器手动跑时）：
  ```bash
  # 无头验证项目完整性，退出码 0 = 工程可打开
  "F:/Unity/2022.3.62f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" -logFile -
  # 无头跑测试（PlayMode 把 testPlatform 换掉；test-results 写系统临时目录，跑测产物不落 external/）
  "F:/Unity/2022.3.62f1/Editor/Unity.exe" -batchmode -nographics -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" -runTests -testPlatform EditMode -testResults "$TEMP/pc3d-test-results.xml" -logFile -
  ```
- **batchmode 铁律**：必须显式带 `-projectPath` 且加 `-nographics`。缺 `-projectPath` 会打开 EditorPrefs 里的"最近工程"（可能污染/锁住别的项目——本项目曾因此产生杀不死的僵尸进程卡住 `Temp/UnityLockfile`，只能重启机器清理）；当前环境不带 `-nographics` 会卡在 GfxDevice 创建。一次只跑一个 Unity 进程。
- 无头验证台（不入库）：`external/harness/` 用 `dotnet` 引用 Unity 已编译程序集 + NuGet NUnit，**不启动 Unity 就能编译工程源码并跑纯 C# 测试**，多 agent 并行时绕开 Library 独占锁。边界：`GameObject` / `MonoBehaviour` / `ScriptableObject` 的实例化走原生 `ECall`，脱离 Unity 运行时必抛 `SecurityException`；所以战斗数值/回合规则这类核心逻辑**刻意写成纯 C# 静态类**以便无头测试，MonoBehaviour 胶水层仍由 batchmode 收口。用法与手工副本配方（必须显式传 `-p:ProjectRoot`，否则副本反查不到工程根会"0 错误"空转）详见 `external/harness/README.md`。
- 验收优先不出包
- 尽量不要靠模拟点击
- 耗时的活先说一声、放后台：烘场景 / 出包 / 全套测试都是分钟级命令，跑之前跟用户说一句，并用后台执行，别让他对着卡住的终端等；能靠读代码或日志回答的问题就别跑命令。

### Worktree 公约（并行分支工作台）

- 独立任务一律开 worktree 干：`git worktree add temp/<目录名> -b <分支名>`。worktree 统一放仓库根
  `temp/` 下（已被根 .gitignore 的 `[Tt]emp/` 规则整体忽略，不会污染主仓 git status）。
- 一个任务 = 一个 worktree = 一个分支；多 subagent 并行时天然文件域隔离，比口头约定文件域更可靠。
- 任务收官（已合并/已废弃）后 `git worktree remove temp/<目录名>` 清理，别让 temp/ 堆尸体。
- worktree 是完整检出：Unity 首次打开会各自重新导入 Library（分钟级）；Library 锁按目录隔离，
  不同 worktree 可各跑一个 Unity，但同一 worktree 内仍一次只能一个 Unity 进程。
- 在 worktree 里跑无头验证台必须显式指工程根：`external/harness/run.sh` 加
  `-p:ProjectRoot=<worktree绝对路径>/pirate-crew`（否则反查回主仓，验了个寂寞）。
- 仓库外也有历史 worktree（如 `../pirate-crew-3d-unity-ui`）——新开的一律收进 `temp/`，旧的不强制迁移。

### 文档写作规范

- 专门记录变更的文档（`docs/项目/待办事项.md` 的归档区）可以写变更过程与提交哈希，普通文档只写「是什么 / 怎么设计 / 为什么这么设计」，不写「什么时候改的 / 之前是什么」这类变更记录，比如“（2026-09-22变更）”。Git 本身就是文档的历史版本。
- 凡是 AI 生成、未经确认的设计/数值/命名提案，须在所在文档标注「提案/待定」；其他文档引用时不得当作已确认设定——防止提案被反复复读成"事实"
- 数值、行为、断言的出处写带文件的引用，不写"某处""文档里说"。

### 会话交接（长任务跨对话）

- 当上下文过长、判断质量可能下降时，主动建议用户开新对话。
- **交接独立成文（学自 game-2，一个交接一个文档）**：交接档统一放 `docs/项目/交接/`，命名
  `<主题名>-交接.md`（进行中的批次用 `-进度与交接.md`），**不带日期**；
  [docs/项目/交接与恢复指南.md](docs/项目/交接与恢复指南.md) 是长期知识库（决策史/踩坑史/口径），
  **不堆放单轮交接**，只留指路行。
- **交接前必须**：① 更新该任务的交接档（进度 / 下一步任务分解 / 关键决策与理由 / 新会话恢复指引）；
  ② 在 [docs/项目/交接/活跃任务登记.md](docs/项目/交接/活跃任务登记.md) 同步对应行（待创始人人工
  验收的产物写**完整 Windows 绝对路径**，让他能直接粘贴进资源管理器）；③ 任务板更新到
  [docs/项目/待办事项.md](docs/项目/待办事项.md)（改索引行 + 对应 `docs/项目/待办/<任务名>.md` 详情文件）；④ git 提交。
- **新会话恢复**：用户说「继续 <任务名>」时，先读 [docs/项目/交接/活跃任务登记.md](docs/项目/交接/活跃任务登记.md)
  找到对应交接档恢复上下文，再干活，不重新摸底。
- **临时文档干完即删**：任务收官无待验收项的交接档移入 `docs/项目/交接/归档/`；审计快照类用完直接删。
- **多 subagent 并行时文件域必须互不重叠**；涉及 Unity 的验证（batchmode）由协调者串行执行——
  Library 锁是独占资源，一次只能一个 Unity 进程。

### 文档导航

| 要做什么 | 读哪个 |
| --- | --- |
| **新会话恢复入口** | [docs/项目/交接与恢复指南.md](docs/项目/交接与恢复指南.md)（项目状态 / 关键决策 / 遗留 TODO / 恢复顺序） |
| 当前待办索引 / 已完成归档 | [docs/项目/待办事项.md](docs/项目/待办事项.md)（一行一条；详情在 [docs/项目/待办/](docs/项目/待办/)，一任务一文件；已完成的在 [docs/项目/待办/归档/](docs/项目/待办/归档/)，清单见其 README） |
| **架构怎么分层、代码该放哪** | [docs/技术/架构/架构总览.md](docs/技术/架构/架构总览.md)（程序集 / 目录 / 场景接线 / 改动落点导航） |
| **改 UI（组件/排版/列表/对话框）前查口径** | `pirate-crew/Assets/Art/Sprites/UI/Aseprite/theme.xml` + `sheet.png`（**Aseprite 参考库 = UI 唯一权威**；量测与署名见 [docs/images/ui-pixel-ref/README.md](docs/images/ui-pixel-ref/README.md)）；复刻承载三层与调试面板见 [架构总览 §8.1](docs/技术/架构/架构总览.md)（对话框声明原封拷贝在 `Resources/AseWidgets/`，`AseDialogLoader` 装载） |
| 查玩法数值的历史记录（公式/武器表/回合规则/AI 伪代码） | [docs/项目/归档/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md](docs/项目/归档/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md)（2D 原版 Flash 逆向，**已归档**：只作玩法数值与关卡灵感，不再是设计依据） |
| 加跨模块事件 / 查事件契约 | [docs/技术/架构/EventBus事件契约.md](docs/技术/架构/EventBus事件契约.md)（事件名与载荷登记表） |
| **改 Battle 场景 / 装配链前必读** | [pirate-crew/Assets/Scenes/README.md](pirate-crew/Assets/Scenes/README.md)（**折叠态契约** / **八步装配链** / 改前必跑转储比对） |
| 查其他文档（设计/技术/审计/项目分类总目录） | [docs/README.md](docs/README.md) |

***

## 核心行为指令

1. **测试驱动**：核心逻辑（战斗数值、船员管理状态流转）附带 Unity Test Framework（NUnit）测试，放 `pirate-crew/Assets/Tests/`。
2. **安全第一**：`pirate-crew/Assets/Scripts/Core/`（引导器/事件总线/存档）经评审后修改须谨慎——它是所有模块的地基。
3. **原子化提交 + 主动沟通**：每次提交一个独立最小功能；任务描述不清或与架构原则冲突时主动提问，不做危险假设。
4. **重构验收 = 行为规格，等价 ≠ 正确**：玩家可感知的行为先立契约（输入 → 可观察结果，例：[docs/技术/相机行为契约.md](docs/技术/相机行为契约.md)），实现/测试/文档三处同源同改；「与旧实现逐位等价」只允许用于**已裁决为正确**的行为——等价性继承会把错误一起继承（案例：r13 相机「环绕不重瞄」被逐位复刻、被探针验证、被测试固化，三层全绿仍是错的）。
5. **执行前预告**：每次动手（改文件 / 跑命令 / 写文档）前，先用自己的话复述用户要求并预告接下来的动作与落点（改哪个文件、跑哪条命令、预期结果），让用户能当场发现理解偏差；预告与意图对不上就先问，不闷头执行。多步任务按阶段分段预告。

***

## 架构与目录

分层 / 程序集依赖 / 模块划分 / 目录结构的**权威口径**在 [docs/技术/架构/架构总览.md](docs/技术/架构/架构总览.md)，
本文件不重复——判断「代码该放哪、模块怎么连」直接读它。只留两条别处没有的铁律：

- 仓库根放文档与规则，Unity 工程本体在 `pirate-crew/` 子目录（Unity Hub 打开的是它，不是仓库根）。
- `external/` 是工作台（gitignored）：参照库 / harness 母本 / 逆向材料 / Blender 建模源；跑测产物禁入，公约见调研文档§7。
