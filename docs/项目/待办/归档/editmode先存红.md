# EditMode 两条先存测试红（331efe92 引入，与虚线移除无关）

> **已完成**（2026-10-02 EditMode 全量实测验证）：两条先存红均已消——EditMode 1074 条 0 失败。

## 详情

**EditMode 两条先存测试红（331efe92 引入）**：

- ① `LevelAssetGateTests.Assets_DeserializeIntoPayloads_EqualToGoldenJson` 资产计数门禁写死
  「8 海图 + 2 关卡 = 10」——**已修**（海图 101–108 删除批次顺手改为「0 海图 + 4 关卡 = 4」，
  `Assets/Tests/Battle/LevelAssetGateTests.cs:93`）。
- ② `ScriptAssetSerializabilityTests.EveryMonoBehaviourType_IsBackedByASameNamedScriptFile`：
  `PirateCrew.ArtReview.ChemPlantOrbitCapture+OrbitRunner` 嵌套 MonoBehaviour 违反
  「类名=文件名」规则——**已修**（提为顶级类，落
  `Assets/Scripts/PirateCrew/ArtReview/OrbitRunner.cs` 同名文件；与宿主零耦合，
  唯一交互 `Setup(outDir, level)` 传参不变）。
- 全量EditMode 复验（2026-10-02）：1074 条 / 通过 1038 / 失败 0 / 跳过 2 / inconclusive 33。
