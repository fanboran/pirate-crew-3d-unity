# harness 纪律与基线

> `harness All/Runtime` 域不编译 `Assets/Editor/**`，dotnet 环境下 10 个用例 HEAD 基线即红，无头测试发现器报"没有可用测试"；改 `Assets/Editor/**` 后须跑 `harness DataEditor`，红色用例要么排除要么补资产上下文，发现器待修。

## 详情

### harness 纪律：改 `Assets/Editor/**` 后必须跑 `harness DataEditor`

**【harness 纪律】改 `Assets/Editor/**` 后必须跑 `harness DataEditor`**——`All`/`Runtime` 域
**不编译 Assets/Editor**（本次清退批次在 MenuUiBuilder 上踩实：All 全绿但 Editor 域有一处
doc 注释被咬坏 + 误删嵌套类型，实机才暴露）。2026-09-24 已修复并重验。

### harness 基线：dotnet 环境下 10 个用例红（HEAD 基线即红，非本轮引入）

**【harness 基线】dotnet 环境下 10 个用例红（HEAD 基线即红，非本轮引入）**——
`BandMaterials_*`×4 / `BattlePipeline_*`×2 / `Prefab_Exists` / `Scene_IsExactlyOnePurePrefabInstance` /
`EveryMonoBehaviourType_IsBackedByASameNamedScriptFile` / `KnownOffendersAllowlist_HasNoStaleEntries`：
都依赖 Unity 资产/Shader 上下文，纯 dotnet 编译环境跑不了；要么从 harness 默认过滤器排除，
要么补资产上下文。EditMode（Unity batchmode）才是它们的权威判定环境。

### 无头验证台测试发现器失效排查

**无头验证台测试发现器失效排查**（2026-09-20 发现）：`dotnet test Harness.csproj -p:HarnessScope=All|Combat` 报"没有可用测试"（编译本身通过——源码兼容性仍由它证明，但用例不执行）。NUnit adapter 注册/平台设置层面的问题，先于本轮 namespace 改动存在（csproj 零改动复现）。主验证链 batchmode EditMode 1161 条全绿不受影响，harness 修复后再恢复双轨