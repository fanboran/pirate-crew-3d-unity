# 船员经验系统拆除(保留存档字段)

> 用户口径:**船员没有经验/等级/成长系统**。
> **代码与测试已拆除**(本文件记录拆除范围);harness 相关四域全绿
> (CrewManagement 10 / Campaign 20 / Settlement 24 / UiTextRules 11,零失败)。
> 余:Unity EditMode 复跑 + LevelSelect 重装配(见文末)。

## 已执行(纯 C# / UI 层)

1. `CrewManagement/CrewProgression.cs` — 删 `CrewProgressionRules` 曲线类与
   `GrantXp/GetLevel/GetXpToNextLevel/GetLevelProgress`;账本只留
   `GetXp/SetXp/CrewIds/Reset`(存档通道),类头注明「仅存档兼容保留」。
2. `CrewManagement/CrewManagementApi.cs` — `GrantMapReward` 删发经验段;
   `Progression` 出口保留(注释改「仅存档兼容」)。
3. `CrewManagement/CrewManagementEvents.cs` — `CrewRewardPayload` 删 `XpPerCrew`
   字段(构造签名同步)。
4. `UI/SettlementPanelRules.cs` — 删 `RowKind.Xp`、`PanelInput.XpPerCrew` 与对应行逻辑。
5. `UI/BattleHud.Settlement.cs` — 结算面板经验行删。
6. `UI/LevelSelectController.cs` — `settlementXpText` 字段与消费段删。
7. `UI/CrewManagementController.cs` — 名册行只剩船员名(未解锁行保留门槛文案)。
8. `UI/UiStrings.cs` / `UI/UiTextRules.cs` — `CrewRow`/`CrewRowFormat`/
   `SettlementXp`/`SettlementRowXpFormat` 删。
9. 测试 — `CrewProgressionTests.cs` 整删;`CrewManagementApiTests` /
   `CampaignSettlementTests` / `SettlementPanelRulesTests` / `UiTextRulesTests`
   的经验断言删除或改造(存档往返用 `SetXp` 直写)。
10. **存档** — `CrewManagementSaveCodec` 未动:三个键(名册/编成/经验)格式不变,
    旧档兼容,经验字段原样搬运。

## 余项(Unity 侧收口,跑 batchmode 时顺带)

- Unity EditMode 复跑(本拆除的用例域无头台已绿,EditMode 收口随下次 batchmode)。
- `LevelSelect` 折叠 prefab 里 `settlementXpText` 的序列化引用悬空——
  重跑该场景装配链即消(生成器是唯一真源)。
- harness `All` 现存 1 条失败 `AmbientTimeOfDayTests.Noon_MatchesBattleSceneLightingCurrentValues`
  归属并行在建改动(光照三文件未提交),与本拆除无关。
