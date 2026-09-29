# EditMode 两条先存测试红（331efe92 引入，与虚线移除无关）

> 两条先存测试红由 331efe92 引入：资产计数门禁写死「8 海图 + 2 关卡 = 10」（L4/L5 入库后实为 12）与 `ChemPlantOrbitCapture+OrbitRunner` 嵌套 MonoBehaviour 违规，待改计数或提为顶级类。

## 详情

**EditMode 两条先存测试红（331efe92 引入，与虚线移除无关）**：①
`LevelAssetGateTests.Assets_DeserializeIntoPayloads_EqualToGoldenJson` 的资产计数门禁写死
「8 海图 + 2 关卡 = 10」，L4/L5 关卡资产入库后实际 12（`Assets/Tests/Battle/LevelAssetGateTests.cs:93`
附近）——L4/L5 转正时改计数或把门禁改成按清单推导；②
`ScriptAssetSerializabilityTests.EveryMonoBehaviourType_IsBackedByASameNamedScriptFile`：
`PirateCrew.ArtReview.ChemPlantOrbitCapture+OrbitRunner` 嵌套 MonoBehaviour 违反
「类名=文件名」规则（Unity 找不到它的 MonoScript，存 Prefab 会失败）——按测试提示提为顶级类、
放同名文件。