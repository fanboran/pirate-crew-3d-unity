> **说明**：本文件是 AI 辅助开发的**主规则文件**。由原 Godot 项目 `.trae/rules/`（rule.md 通用规范 + rule_local.md 项目身份 + 参照库/截图两大强制规范）与姊妹项目 stick-world 的 AGENTS.md 融合改写而来，适配 Unity 工作流。

***

### 项目背景（前因后果，新会话必读）

- **这是什么**：求职作品集项目。本人 2027 届计算机本科，求职游戏客户端开发实习；本项目的定位是**简历上的 Unity 实战素材**——证明"引擎概念相通，换引擎能干活"。
- **前因**：海盗军团夺宝 3D 原版是 Godot 4 项目（位于 `../game-3/pirate-crew-3d/`，58+ 提交，已完成战役/船员管理/海盗船员三模块骨架与部分玩法）。为求职 Unity 岗位立项本仓库做 **Unity 2022.3 重制**。
- **两个版本的关系**：Godot 版是**设计参照与功能基准**（玩法规则、数值、场景内容以它为准）；本仓库是 Unity 实现，代码与场景按 C# / GameObject 规范重写（翻译规范表见 README）。Godot 版继续独立存在，两边不互相改代码。
- **节奏约束**：这是求职侧项目，主项目（stick-world，`../game-2/`）优先级更高；本项目按里程碑（M0 骨架 → M1 流转 → M2 战斗 → M3 管理循环）推进，M2 之前不碰海战/大世界等远期系统。

***

### 注意事项

- 使用中文回答问题，用中文写提交信息和 Git 日志。
- Git 提交格式：`类型(模块): 描述`，示例：`feat(pirate_crew): 实现舰船接舷战状态机`
- Unity 编辑器路径：`F:\Unity\2022.3.62f1c1\Editor\Unity.exe`
- 无头验证项目完整性（改完工程结构/manifest 后必跑）：
  ```bash
  "F:/Unity/2022.3.62f1c1/Editor/Unity.exe" -batchmode -quit -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" -logFile -
  ```
  退出码 0 = 工程可打开；非 0 先查输出里的 error 再处置。
- 渲染管线：**URP**（对应 Godot 版 Forward Plus 的 3D 定位）；3D 物理用内置 PhysX，寻路用 AI Navigation 包（NavMesh）。
- 改进待办项记录在 `docs/待办事项.md`

### 文档导航

| 要做什么 | 读哪个 |
| --- | --- |
| 了解项目目标、翻译规范、里程碑 | [README.md](README.md) |
| 查 Godot 版某系统怎么设计的 | `../game-3/pirate-crew-3d/docs/` 与 `../game-3/docs/` |
| 查 Godot 版某功能怎么实现的 | `../game-3/pirate-crew-3d/modules/`（campaign / pirate_crew / crew_management） |
| 查 AI 规则的原始出处 | `../game-3/.trae/rules/`（rule.md 通用规范） |

***

## 核心行为指令

1. **参照库强制**（自原项目 rule.md，全项目最重要的规则）：AI 无参照写代码容易 API 幻觉、边界遗漏，但**翻译移植和等效重构表现非常稳定**。每次写新功能或重写模块前——
   - 下载星标多、维护活跃、玩法相似的开源 Unity 项目到 `external/<功能名>-reference/`（已 gitignore）；
   - 读懂其核心实现后基于参照翻译改编；
   - 参照库不入库。例外：简单 bug 修复、单行改动、参数调整。
2. **图形学调试截图规范**（自原项目 rule.md，Unity 版）：Shader/渲染效果开发必须——在 shader 中实现 `debug_mode` 拆分管线步骤 → 用 `ScreenCapture.CaptureScreenshot` 或 Editor 脚本逐层截图存 `export/<功能名>-debug/` → 写一页 README 说明每张图应看到什么 → 列关键参数调参指南。
3. **测试驱动**：核心逻辑（战斗数值、船员管理状态流转）附带 Unity Test Framework（NUnit）测试，放 `pirate-crew/Assets/Tests/`。
4. **安全第一**：`pirate-crew/Assets/Scripts/Core/`（引导器/事件总线/存档）经评审后修改须谨慎——它是所有模块的地基。
5. **原子化提交 + 主动沟通**：每次提交一个独立最小功能；任务描述不清或与架构原则冲突时主动提问，不做危险假设。

***

## Unity 模块化架构原则

> 从 Godot 模块化四原则翻译而来，精神一致、载体不同。Godot 模块 → Unity 目录映射见 [README.md](README.md)。

1. **两层结构**：仓库根放文档与 AGENTS.md，Unity 工程本体放 `pirate-crew/` 子目录（Unity Hub 打开的是它，不是仓库根）。
2. **模块划分**：`Assets/Scripts/` 下按功能分 `Core/`（引导器/事件总线/存档）、`Campaign/`、`PirateCrew/`、`CrewManagement/`、`UI/`；一个模块一个 C# 命名空间（`PirateCrew.Combat` 等）。
3. **耦合原则**：模块间通信走 `Core/EventBus.cs`（静态 C# 事件中心，对应 Godot 的 event_bus autoload）；**禁止** `GameObject.Find`、跨模块 `GetComponent` 裸引用。跨模块调用的公共出口放各模块 `XxxApi.cs`。
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
├── docs/                       # 任务书与设计文档
├── external/                   # 开源参照库（gitignored，不入库）
└── pirate-crew/                # Unity 工程本体（Unity Hub 打开这个）
    ├── Assets/
    │   ├── Scripts/{Core,Campaign,PirateCrew,CrewManagement,UI}/
    │   ├── Tests/              # NUnit 测试
    │   ├── Prefabs/  Scenes/  Art/  Plugins/
    ├── Packages/               # manifest.json（AI Navigation / Cinemachine）
    └── ProjectSettings/        # Unity 工程设置（URP）
```
