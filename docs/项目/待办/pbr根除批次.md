# PBR 根除批次·待实机走查

> 换血已合并进 main（像素管线零触碰）；余一个验收动作与两个小遗留：batchmode open 档 +
> 必查清单未跑、`BattleSceneLighting` Surface 口径段待清、`Water_Ocean.mat` 悬空引用待清。

## 详情

**余项**：

- [ ] **验收走查未跑**：batchmode open 档 + 必查清单——浮岛无洋红、海面显示
      `PixelartOcean_Sea`、已删资产（ToonPilot 族等）无 missing 引用、删除资产重导入无红
- [ ] **`BattleSceneLighting.cs` Surface 口径段待清**（`pirate-crew/Assets/Editor/BattleSceneLighting.cs`）：
      `SurfaceShaderName = "PirateCrew/PirateSurface"` 与 `Surface_*` 八个材质名常量仍在，
      而该 shader 已删——重跑 `BuildAll` 报错跳过（不崩），待清
- [ ] **`Water_Ocean.mat` 悬空引用**：材质的 shader guid 全仓无对应文件；引用落点为
      `pirate-crew/Assets/Prefabs/PirateCrew/Battle/BattleRig.prefab`（烘焙期 renderer
      enabled=false 不参与渲染）。清引用或删材质随水体表现定夺 →
      [水体表现口径.md](水体表现口径.md)

**口径（有意保留件，非遗漏）**：

- PBR 根除的范围是光照类 PBR 与旧链；以下为裁决**保留**：`PirateGradientSky` 三档
  （像素天空尚不存在，M2d 收编时换）、`Fx/Additive+Alpha` 与 `URP/Unlit` 系（非光照
  实用件非 PBR）、`PirateOutline` + Crew 族（反壳基座）、`MaterialNoise` 贴图
  （BattleSceneLighting/ArtGate 仍有活消费）。
- 船员反壳件纠缠（材质槽 / `_OutlineState` 契约）不属本批 →
  [船员描边与伤害数字.md](船员描边与伤害数字.md)（该话题唯一正主）。
