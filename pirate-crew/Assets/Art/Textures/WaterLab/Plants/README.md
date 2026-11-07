# WaterLab 植物 imposter 图集（I6a 临时复用）

来源：Sandcastle Demo（Steam 已购）`Sandcastle_Data/sharedassets0.assets` 解包提取，
原件与提取脚本在 `external/sandcastle-ref/extracted/plants/`（含 manifest.json 逐格均色）。
**授权口径：已购游戏的复刻研究临时复用，仅水体复刻实验（temp/water-lab）使用，不得用于正式发行内容。**

- 13 张 `*_ImposterAtlas.png`：八面体视向 imposter 图集，与 `ReplicaPlantSystem.ReplicaPlantTable`
  的 13 组摆放参数一一对应；每格恒 64px，格数（8/10/12/14/16）由各材质 `_atlasMap_ST.scale` 承载
  （原作 cb1[0] 真身，见 `external/sandcastle-ref/rt/reports/G-plants.md` §1/§4.2）。
  alpha 通道双职责：覆盖率（Forward 权重 K=2.5 / ShadowCaster K=10）+ 归一化高度场（SV_Depth 逐像素雕刻）。
- `PlanterThumbnail.png`：原作 UI 缩略图，不参与渲染，随包提取留存。
- 导入参数（原作口径）：sRGB（RGB 通道；alpha 不受 gamma 影响，高度场原值采样）、
  生成 mip（PS 用 lod0，需求以验收抓帧终证）、双线性、Clamp、Standalone 压缩（DXT5=原作 format 12）。
