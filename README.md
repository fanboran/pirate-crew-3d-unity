# 海盗军团夺宝 3D · Unity 版（Pirate Crew 3D）

> Godot 版（`../game-3/pirate-crew-3d/`）的 Unity 2022.3 重制，独立仓库。玩法规则与数值沿用其设计基准，代码与场景按 C# / GameObject 规范重写。

## 当前状态

**M1 已完成，M2（战斗最小可玩闭环）进行中。** 用 Unity Hub → Open 选择 `pirate-crew/` 子目录（不是仓库根）。

- 引擎：Unity 2022.3.62f1c1（已装于 `F:\Unity\2022.3.62f1c1`）
- 渲染管线：**URP 14.0.12**，两档质量资产在 `Assets/Settings/URP/`
- 物理：内置 PhysX；寻路：AI Navigation 包（NavMesh）；相机：Cinemachine 2.9.7
- 测试：Unity Test Framework 1.1.33（EditMode 172 条 + PlayMode 端到端流转）
- 编辑器集成：MCP for Unity v10.0.0（编辑器打开时 AI 可直接操作场景/资产）
- 无头验证台：`external/m2-harness/`（不入库）——不启动 Unity 即可编译并跑纯 C# 测试

> ⚠ **UI 字体说明**：当前 UI 用 legacy `UnityEngine.UI.Text`。
> **TextMeshPro 不在 Unity 2022.3 的 ugui 1.0.0 里**（2023.2+/Unity 6 才并入 ugui），需要单独装 `com.unity.textmeshpro` 包。
> 升级 TMP 已列入待办，不要以为可以直接 `using TMPro`。

## 目录约定（映射 Godot 版模块）

```
pirate-crew/Assets/
├── Scripts/
│   ├── Core/             # 引导器、事件总线、场景流转、存档（对应 core/autoload/*）
│   ├── Campaign/         # 战役流程（对应 modules/campaign）—— M3
│   ├── CrewManagement/   # 船员管理循环（对应 modules/crew_management）—— M3
│   ├── PirateCrew/       # 核心玩法：海盗船员战斗（对应 modules/pirate_crew）
│   │   ├── Data/         # 纯 C# 数值目录表 + ScriptableObject 定义类
│   │   ├── Combat/       # 战斗纯逻辑：弹道/爆炸结算/回合规则/计分
│   │   ├── Battle/       # 战斗组装层：MonoBehaviour 薄壳
│   │   └── Rendering/    # 描边 RendererFeature（独立 asmdef，因为要引用 URP 程序集）
│   └── UI/               # 通用 UI 控制器
├── Data/                 # 由生成器产出的 SO 数值资产（17 武器 / 27 船员 / 3 关卡 / 1 平衡）
├── Editor/               # 编辑器脚本：场景批量创建、数值资产生成、调试截图采集
├── Tests/                # NUnit：EditMode 按模块分目录（Data/Combat/Battle）+ PlayMode
├── Prefabs/ Scenes/ Art/ Plugins/
└── Settings/URP/         # 管线资产（PC_Performant / PC_Balanced）
```

## 翻译规范（Godot → Unity）

| Godot 概念 | Unity 对应 | 规则 |
|---|---|---|
| GDScript（snake_case） | C#（PascalCase，camelCase 局部） | 一个 `.gd` 一个 `.cs`，类名即文件名 |
| 场景树 `.tscn` | Prefab 层级 | 每个可实例化场景一个 Prefab |
| 信号 `signal` | C# `event Action<T>` | 本地回调用 `UnityEvent`，跨系统广播走 EventBus |
| Autoload 单例 | Bootstrapper 场景 + `DontDestroyOnLoad` 服务类 | 禁止场景里手摆重复实例，禁止 `GameObject.Find` |
| `@export` 参数 | `[SerializeField]` 字段 | Inspector 可调，保持设计师可配 |
| `res://` 路径加载 | `Resources/` 或 Addressables | MVP 阶段统一 `Resources.Load` |
| 模块目录约定 | **asmdef** | 编译期强制模块边界；`Rendering` 因需引用 URP 而独立成程序集 |
| 信号广播 | **EventBus 字符串事件（snake_case）** | 事件名与载荷必须登记在 `docs/EventBus事件契约.md`，禁止散落魔法字符串 |
| `config/*.json` 数值 | **纯 C# Catalog + ScriptableObject** | Catalog 是真值来源（可无头测试），SO 只是它的序列化投影 |
| 手搓重力 / 瓦片 AABB 扫掠 | `Rigidbody` + `Collider` + PhysX | 3D 语义不同，属"应重写"而非逐行翻译 |
| （版本陷阱） | `Rigidbody.velocity`、`CinemachineVirtualCamera` | 2022.3 用不了 Unity 6 的 `linearVelocity` / `CinemachineCamera` |

## 里程碑

- **M0 骨架**（已完成）：工程可打开、URP 落地、asmdef + 测试接入
- **M1 流转**（已完成）：主菜单 + 场景流转 + 存档骨架，端到端 PlayMode 测试通过
- **M2 战斗**（进行中）：数值层 + 战斗纯逻辑 + 选中描边已落地；组装层 / 场景 / HUD / AI 进行中
- **M3 管理循环**：招募 → 编成 → 出战 → 结算

## 文档与数值来源

| 要查什么 | 读哪个 |
| --- | --- |
| 当前待办与归档 | [docs/待办事项.md](docs/待办事项.md) |
| **数值与玩法权威** | [docs/参考游戏逆向-海盗军团抢宝藏-静态.md](docs/参考游戏逆向-海盗军团抢宝藏-静态.md)（2D 原版 Flash 逆向，含公式/表/AI 伪代码） |
| Godot 基准与 `.gd → .cs` 对照 | [docs/M2-Godot基准摘要.md](docs/M2-Godot基准摘要.md) |
| Unity 参照库与本机 API 风险 | [docs/M2-Unity参照库调研.md](docs/M2-Unity参照库调研.md) |
| 跨模块事件契约 | [docs/EventBus事件契约.md](docs/EventBus事件契约.md) |
| 描边 shader 调参 | [docs/描边Shader调试.md](docs/描边Shader调试.md) |

> ⚠ 数值口径：Godot 版的 `config/*.json` **不是**权威——`config_manager.gd` 读的是 `*.cfg` 而目录里只有 `*.json`，
> 从未被运行时加载，只是离线规格。数值以 2D 原版 Flash 的逆向文档为准。
>
> 3D 资产：Godot 版 `assets/` 的 glTF 模型可直接复制（Unity 原生支持），材质需重调。
