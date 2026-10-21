# PBR 根除批次·待实机走查

> 换血已合并进 main（像素管线零触碰）；open 档 + 必查清单已过、两个小遗留已清。
> 余一项实机观感走查（浮岛无洋红 / 海面 `PixelartOcean_Sea` 上屏）。

## 详情

**余项**：

- [ ] **实机观感走查（一眼即过）**：headless open 档已过（2026-10-14，退出码 0、
      error CS / Missing / 悬空脚本扫描全零），人眼只剩三看——进任一战斗关卡：
      ① 岛体/地形**无洋红块**（洋红 = shader 丢失，`-nographics` 下无头验证不到渲染）；
      ② 海面正常显示像素海面（非平色、非消失）；
      ③ Console 无 missing 引用警告。三看皆过即本条收官归档。

**已清两件**：

- [x] **`BattleSceneLighting.cs` Surface 口径段已清**：`SurfaceShaderName` 与
      `Surface_*` 八个材质名常量、8 个 `Build*Material` 函数、细节贴图接线段、
      `EnsureMaterial`/`Save`/`SetColor` 等只服务 Surface 段的辅助全部拆除
      （`WaterMaterial`/`TerrainMaterial` 两常量保留——`BattleSceneSetup` 仍按名取材质）；
      后处理 VolumeProfile / URP 配置 / 天空与光照场景层不受影响；harness DataEditor 编译零错误
- [x] **`Water_Ocean.mat` 悬空引用已清**：`BattleRig.prefab`（烘焙期 renderer
      enabled=false 不参与渲染）的材质引用改空引用占位（`{fileID: 0}`）；
      材质文件本体保留，删留随水体表现口径裁决定夺 → [水体表现口径.md](水体表现口径.md)

**口径（有意保留件，非遗漏）**：

- PBR 根除的范围是光照类 PBR 与旧链；以下为裁决**保留**：`PirateGradientSky` 三档
  （像素天空尚不存在，M2d 收编时换）、`Fx/Additive+Alpha` 与 `URP/Unlit` 系（非光照
  实用件非 PBR）、`PirateOutline` + Crew 族（反壳基座）、`MaterialNoise` 贴图
  （BattleSceneLighting/ArtGate 仍有活消费）。
- 船员反壳件纠缠（材质槽 / `_OutlineState` 契约）不属本批 →
  [船员描边与伤害数字.md](船员描边与伤害数字.md)（该话题唯一正主）。
