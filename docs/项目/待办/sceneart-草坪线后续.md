# 草坪线后续三件（空岛切 sprite / 配色统一 / 风摆 `_WIND`）

> 草坪验收任务已收线（r24 定标，创始人 2026-09-30「那就这样吧」）——收线档
> [交接/归档/像素草坪验收-交接.md](../交接/归档/像素草坪验收-交接.md) 含全部提交链、
> 定标参数、复现命令与踩坑史。开线前先读它。

## 详情

### ① 空岛草切 sprite 路线

空岛（`FloatingIslandPrimitives.AddGrassTuft` 几何版 + `GrassPatchRules` 斑块）切到
`PixelartMaterialFactory.CreateSprite` 剪影路线：复用草坪的遮罩图集与 `GrassPatchRules`，
`FloatingIslandComposer` 草簇段改 sprite 四边形烘焙，重烘 `PixelartLevelPilotSetup.BuildLevel3`
+ 战斗场景，出图对照 r24。

### ② 草坪配色 vs 空岛草皮统一（进岛前必裁）

草坪已提亮到 #6FB86A ±8% 黄绿邻近域；空岛草皮三档仍是 #4A8C4A 系。同框必跳，
两条路：空岛草皮三档整体提亮，或草坪回深。创始人裁决。

### ③ 草丛风摆 `_WIND` 变体

见 [sceneart-草丛风摆wind变体.md](sceneart-草丛风摆wind变体.md)——动冻结渲染契约，须评审。
