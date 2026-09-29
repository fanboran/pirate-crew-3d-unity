# 审计批次·实机验收 cannonball 直线弹道 + ArtReview 静态归零

> 两项代码均已修（cannonball weight=0 改水平直线飞行、`-artReviewLevel` 出图会话结束归零静态覆盖），卡在待实机验收。

## 详情

**【审计批次·实机验收】cannonball 直线弹道 + ArtReview 静态归零**（2026-09-24 代码已修，待实机）——
① cannonball（weight=0）不再被强制仰角抬升，改水平直线飞行（忠于逆向「无重力」口径），
修掉「飞 5 单位静默消失」的 P0；实机验收 = 满蓄力 cannonball 能落到远处并引爆
（分流函数与预览一致性已由 `Tests/Battle/ThrowWeightSplitTests.cs` 钉住）。
② `-artReviewLevel N` 出图会话结束归零静态覆盖（`PlayerArtCapture.OnDestroy`）；
验收 = 同一编辑器会话出图后再 Play，选关解析不再被劫持。