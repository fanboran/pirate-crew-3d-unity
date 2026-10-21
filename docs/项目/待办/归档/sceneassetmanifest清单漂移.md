# `SceneAssetManifestBuilder` 的清单会随目录漂移

> `pirate-crew/Assets/Art/Models/scene-assets.json` 是生成物，跑链才更新，
> 资产增删后静默落后且无门禁拦截；件数以实跑清单为准。
> 下一步二选一：纳入门禁校验，或声明其为快照。

## 详情

**问题本质**：场景资产总清单 `pirate-crew/Assets/Art/Models/scene-assets.json`
由生成器 `SceneAssetManifestBuilder.BuildAll`
（[pirate-crew/Assets/Editor/SceneAssetManifestBuilder.cs](../../../pirate-crew/Assets/Editor/SceneAssetManifestBuilder.cs)，
ArtGate 烘焙后自动调用）产出；只有跑链才重建，之后增删资产清单不跟随，
当前也没有门禁拦「清单过期」——消费方可能按过期清单行事。

**二选一（待定）**：

- **纳入门禁**：跑链后校验清单是否为最新，漂移即红；
- **声明为快照**：文档明示清单非权威、仅供导览，不拦。

清单内 assetCount 等数字不在此写死，以 `scene-assets.json` 实跑为准。归属：资产管线。
