# harness 纪律与基线

> 无头测试台的基线红用例（依赖 Unity 资产上下文、脱离 Unity 运行时必抛的存量）处置待定：
> 纳入默认排除基线，还是补测试上下文。改 `Assets/Editor/**` 后必跑 `harness DataEditor`
> 已固化为纪律（见 AGENTS.md 调试规范），不再是待办。

## 详情

### 基线红用例的处置（待定）

无头验证台（`external/harness/`，定位与用法以
[external/harness/README.md](../../../external/harness/README.md) 为准）在纯 dotnet
环境下有一批用例 HEAD 基线即红：它们依赖 Unity 资产/Shader 上下文（材质、预制体、
场景装配契约）或 UnityEditor DLL，脱离 Unity 运行时必抛 `SecurityException`——
EditMode（Unity batchmode）才是它们的权威判定环境。

处置二选一，待定：

- **纳入默认排除基线**：固定为「这些用例无头必红」，与基线逐条对拍，多红即回归信号；
- **补测试上下文**：让这批用例在无头环境真的可跑。

哪些用例当前在基线里，以 harness 实跑对拍为准，不在此写死。

### 已收口两件（口径）

- 改 `Assets/Editor/**` 后必跑 `harness DataEditor`——`All`/`Runtime` 域不编译
  Assets/Editor，Editor 代码坏了要实机才暴露；已固化于 AGENTS.md 调试规范。
- 测试发现器报「没有可用测试」的失效已修复（分域输出隔离，记录见
  external/harness/README.md「测试发现器修复记录」），不再挂账。
