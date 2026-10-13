# 关卡地形岛形化(schema 3:矩形采样网格根除)

> 用户口径:场地是环形范围内的有机岛群,**矩形采样网格根除**——资产直接记录岛形。
> 行为等价:岛形栅格化与旧采样数组逐格一致,四关冻结签名摘要逐字不变。

## 详情

- **结构**:`TerrainRaster`(sizeX×sizeZ×heights 行主序数组)→ `TerrainShape`
  (`blockWorldHeight` + `TerrainIsland[]`:topY + outline 多边形 + 可选 holes);
  schema 2 → 3。
- **栅格化**:新增 `TerrainShapeRasterizer`(纯 C#,格中心 even-odd 点包含、重叠取高、
  PointOnGround 站位判定);`LevelRasterFromAsset`/`LevelAssetRules` 站位与统计改走它。
- **迁移**:`tools/level-design/migrate_terrain_to_islands.py`——高度分档 → 格边线
  边界跟随 → 环简化 → 外环/洞分组;**自检 = 栅格化回填与原 heights 逐格相等**才写盘。
  4 关产物:cloud_walk 5 岛(主角云为 8 顶点十字多边形)/ sky_island 1 岛 /
  chem_plant 2 岛 / chem_plant_team 4 岛。
- **等价判据**:`LevelSignature` 的 solid/total/digest 摘要改为吃栅格化结果——
  摘要与迁移前逐字相同 = 冻结签名表零改动即通过,行为等价被签名直接钉死。
- **JSON/YAML**:读写两侧 islands 化(`RawField` 写紧凑一行轮廓);
  golden 由 `WriteGoldenFromAssets` 以 C# 权威格式回写。
- **测试**:新增 `TerrainShapeRasterizerTests`(矩形解析解/行主序/洞/重叠/凹多边形)。
