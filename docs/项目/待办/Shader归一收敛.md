# Shader 归一收敛

> 一句话现状：血统表已落档（渲染管线.md §0）；余三件收敛项——PostLine 二风格裁决、PirateBase 反壳描边核对、云海效果 shader 命名泛化；CellShade 缺口提案的排期等创始人工作树里这批 shader WIP（Flood 传播窗等）收口后开工。

## 详情

### 1. PostLine 二风格去留（创始人裁决项）

- PostLine 四 shader（ToonLit/Composite/Outline/SharpUpscale）+ 装配链（`Rendering/PostLine/`）已接线，提案口径见 [双风格切换.md](../../技术/渲染/管线/双风格切换.md)（待实机验收）。
- 裁决保留 ⇒ 双风格转正，实机验收后按其口径归档；裁决归一 ⇒ 删四 shader + 装配链 + 双风格切换/PostLine规格两份调研档降级为沿革，全仓只剩 CellShade 一套。

### 2. PirateBase 反壳描边核对（老链最后活口）

- `PirateOutlineUnit.mat`（shader = 老链反壳 `PirateOutline`）挂在 `PirateBase.prefab` 根物体 MeshRenderer 上，经 `BattleRig.prefab` 接进战斗场景。
- 待实机一眼定性：伤害数字待办已裁定选中反馈走立方体角框（SelectionCube）、船员不再做描边——若该 renderer 仍有画面职责则迁像素路径方案，若纯残留则删材质 + shader（`PirateOutline`）+ 预制引用。

### 3. 云海效果 shader 命名泛化（等第二消费者）

- `PirateCrew/CloudSea/Water` 与 `CloudSeaRain` 是插在主链透明趟（`LightMode = "PixelartTransparent"`）上的双 pass 插件，机制通用、命名是云海品牌牌（历史：生于云端漫步场景重做）。
- 其它关卡要水/雨/光柱/彩虹/涟漪时接入成本为零管线改动（写带标签的 pass 或直接复用）。泛化命名（如挪到 `PirateCrew/Fx/Transparent/`）**等特效重做开线或第二个消费者出现时一并做**，涉及 shader 内名 + Resources 文件名约定 + `CloudSeaPath.cs` 契约三处同步，单消费者下不提前抽象。

### 4. 缺口余项队列（等 WIP 收口后按序开工）

- **进行中**（创始人工作树 WIP，7 文件 +184/−61，diff 实证）：Flood 传播窗按块原点锚（§7 唯一必须修）、边缘光 2-4 逐物体开关 / 2-3 两档量化 / 2-1 光色乘项 / 2-5 背光虚拟灯最小替代、Specular 量化软化（抖动偏移，修"工厂关 `_Smoothness`"的根因）、GI 金属掠射反射、金属 tint 进边缘光。
- **余项队列**：① **ShadowCaster pass**（画面上最明显的剩余差距，移植配方 §7 第 4 条已写好）② **Specular 喂入端**（软化已修根因，工厂选择性开金属 `_Smoothness`）③ GI NoV 两档量化（裁决项）④ 帧级调色板重移植（**创始人纠结中，走实拍裁决**：不现在抽象定夺——按"默认关可选趟"重移植（源+配方齐、可逆、不阻塞队列），然后出三态实拍（无限色 / 低色数板 ~46 色 / 高色数板 ~124 色）同场景对比，看图拍板；看完仍不要则趟默认关、配方留档）⑤ rim 光 32 盏全量版（规格 2-5 有完整设计；WIP 最小替代实测够用则缓）⑥ PostLine GrassBlade 草叶系统（对应"草坪线三件"待办，开线时评估移植）。
- **对照素材**：CellShade 库自带 **8 个编号 Demo 场景**（001 降采样 / 002 光照 / 003 对齐 / 004 UV / 005 岩石 / 006 金锭 / 007 抖动图案 / 008 树）+ SampleScene / Skull——每个 Demo 即一个特性的标准答案，A/B 对照按此矩阵铺开，不止 006。
- **参照库颜色/质感审计（2026-11-23 定版）**：① **视频调色板定量锁定 = TestPalette3（124 色）**——从视频帧提取 22 个主色与该色板逐一比对，22/22 命中、平均距离 9.6（其余四套均不匹配）。② **出厂 `Test_N.png` 全是布局测试条，不是调色板 LUT**——正确 LUT 必须用库自带烘焙管线现烘（`SloanePixelartPalette.GenerateTexture2D`，无头入口 `SloanePaletteBaker.Bake3` → `Baked_TestPalette3.png`）。③ **暗面死黑的根因 = 批模式 SH 全局不更新**（ambient 探针不构建，`SAMPLE_GI` 恒 0）——编辑器环境里环境光正常照亮暗面。工作台用 GI 常量兜底（场景存的 Trilight 天光色 `0.197/0.224/0.236` 注入 `GlobalIlluminationFragment`），暗面恢复到视频档位。④ 其余定版项：背景保护修复（深度 alpha + 灰清屏）、方向光 ×2、按场景原生画布 ×3~6 点采样、5 时刻连拍。⑤ **表面抖动细节 = 材质 `_DiffuseDitherStrength`**：出厂 Rock.mat 仅 0.008（等于关闭），视频录制时作者调到了高值；工作台调到 0.15 后阴面/亮面出现视频同款斑驳纹理（对象趟屏幕空间采 `_DiffuseDitherPalette`（`DefaultPass.hlsl:70-71`），着色趟用它微移色带边界）。库内**无使用文档**（根 README 仅简介+Discord，Assets/Readme.asset 是 Unity 模板），视频设置只能从视频帧反推。⑤ **质感公式实证**：视频质感 = 真调色板 LUT 量化 + 环境光 GI（暗面可读）+ 色带 + 描边——**这为「帧级调色板重移植」提供了决定性正面证据，验证方法 = 视频主色提取 vs 色板对账。** ⑥ **连带审计项（待办）**：我们的 PixelartShading 合成趟由同一套源码移植——需核对本仓 ①背景像素的天空盒反射染色、②SH/环境光探针失效时暗面死黑，两个同源问题。
- 纪律：缺口提案开工前必须先收口当前 WIP，避免同文件域撞车。
