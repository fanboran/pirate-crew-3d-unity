# WaterNormal2D_Ref.png —— 原作细节法线贴图（临时复用）

**来源**：Sandcastle 原作发行包提取的 `WaterNormal2D`（UnityPy 回读序列化纹理，
提取件存 `external/sandcastle-ref/WaterNormal2D-extracted.png`，1024×1024 RGBA）。

**通道编码**（与 shader `SAMPLE_TEXTURE2D(...).rg * 2 - 1` 的自解码约定对应）：

- R/G = 法线 xy，0..255 → [-1, 1]
- B = 相位噪声/高度（当前 shader 未消费，保留原样）
- A = 1

**导入口径**：linear 色空间（`sRGBTexture: 0`）、Repeat wrap、mip 开、**Default 纹理类型**——
不用 NormalMap 导入器（它会把通道重排成 DXT5nm 布局，破坏 RG/B 语义）、不压缩（保编码保真）。

**临时性质**：本贴图是原作资产，经项目所有者授权**临时复用**于观感对标验证
（对比程序生成版 `WaterLabDetailNormal.asset` 与原作真实水纹的差异），之后会被替换掉。
`WaterLabSetup.LoadOrCreateDetailNormal()` 优先加载本贴图，找不到时回退程序生成版。

**自愈说明**：`WaterLabSetup.BuildScene()` 重建材质目录时会把本文件连同 README 一并删除；
`WaterLabSetup.EnsureRefNormalAsset()` 会在缺失时从 `<工程根>/../external/sandcastle-ref/WaterNormal2D-extracted.png`
（可用环境变量 `PC3D_WATERLAB_REF_NORMAL_SRC` 覆盖）重新拷贝，并按固定 guid（见 .meta）恢复
导入设置，保证 git 与资产引用稳定。
