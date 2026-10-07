# 审计批次·实机验收 ArtReview 静态归零

> ① cannonball 直线弹道项**已消解**(武器空壳化后全部武器统一标准炸弹,重量分流与
> `ThrowWeightSplitTests` 已随两态重构删除,实弹行为以 [StandardBombRules](../../../pirate-crew/Assets/Scripts/PirateCrew/Combat/StandardBombRules.cs) 为唯一口径);
> ② `-artReviewLevel` 出图会话结束归零静态覆盖已修,卡在待实机验收。

## 详情

**【审计批次·实机验收】ArtReview 静态归零**——
`-artReviewLevel N` 出图会话结束归零静态覆盖(`PlayerArtCapture.OnDestroy`);
验收 = 同一编辑器会话出图后再 Play,选关解析不再被劫持。
