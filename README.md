# 海盗军团夺宝 3D · Unity 版（Pirate Crew Treasure 3D.Unity）

> Godot 版（`../game-3/pirate-crew-3d/`）的 Unity 2022.3 重制，独立仓库。复用其设计文档与玩法规则，代码与场景按 C# / GameObject 规范重写。

## 当前状态

**M0 骨架已初始化**。用 Unity Hub → Open 选择 `pirate-crew/` 子目录，编辑器自动生成 Library 并补全工程。

- 引擎：Unity 2022.3.62f1c1（已装于 `F:\Unity\2022.3.62f1c1`）
- 已预置包：AI Navigation（寻路）、Cinemachine（相机）、TextMeshPro 走 ugui

## 目录约定（映射 Godot 版模块）

```
Assets/Scripts/
├── Core/             # 引导器、服务定位、存档（对应 core/autoload/*）
├── Campaign/         # 战役流程（对应 modules/campaign）
├── PirateCrew/       # 核心玩法：海盗船员战斗（对应 modules/pirate_crew）
└── CrewManagement/   # 船员管理循环（对应 modules/crew_management）
```

## 翻译规范（Godot → Unity）

| Godot 概念 | Unity 对应 | 规则 |
|---|---|---|
| GDScript（snake_case） | C#（PascalCase，camelCase 局部） | 一个 `.gd` 一个 `.cs`，类名即文件名 |
| 场景树 `.tscn` | Prefab 层级 | 每个可实例化场景一个 Prefab |
| 信号 `signal` | C# `event Action<T>` | 跨系统广播用事件，本地回调用 UnityEvent |
| Autoload 单例 | Bootstrapper 场景 + `DontDestroyOnLoad` 服务类 | 禁止到处 `GameObject.Find` |
| `@export` 参数 | `[SerializeField]` 字段 | Inspector 可调，保持设计师可配 |
| `res://` 路径加载 | `Resources/` 或 Addressables | MVP 阶段统一 `Resources.Load` |

## 里程碑

- **M0（本次）**：骨架初始化，Unity 可打开
- **M1**：主菜单 + 场景流转 + 存档骨架
- **M2**：单场海盗战斗可玩（参照 Godot 版 battle 场景）
- **M3**：船员管理循环打通（招募→编成→出战→结算）

## 资产与设计文档来源

- 玩法规则、数值设计：读 Godot 版 `docs/` 与 `pirate-crew-3d/config/`
- 3D 资产：Godot 版 `assets/` 可直接复制 glTF 模型（Unity 原生支持 glTF），材质需重调
