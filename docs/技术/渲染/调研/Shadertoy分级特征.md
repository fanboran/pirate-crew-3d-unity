# Shadertoy「1 组」分级特征分析

> 对象：external/shadertoy/ 下 57 个 Shadertoy 存档的创始人个人分级（存档与工具不入库：分级页 index.html、分级数据 ratings.json 均在该目录）。
> 图像证据：[sheet_S.png](../../../images/shadertoy-sheets/sheet_S.png) / [sheet_A.png](../../../images/shadertoy-sheets/sheet_A.png) / [sheet_B.png](../../../images/shadertoy-sheets/sheet_B.png)（按档拼贴，档内按点赞排序）。

## 一、评判标准

| 档 | 创始人的话 | 设计语言 |
| --- | --- | --- |
| S | 太美了，像壁纸一样 | 主动收藏级——挂上桌面每次开屏都想多看一眼 |
| A | 也很美，但没有 S 惊艳 | 欣赏级——「确实好看」，但视线不会被钉住 |
| B | 系统默认壁纸还是挺喜欢的 | 被动接受级——干净和谐不吵闹，不会主动选 |
| C / D | 没啥亮点 | 无画面记忆点——但工程与文档质量本身仍是好 Shader |

## 二、总规律

**评分评的是画面的情绪传达，不是技术含金量。**

- S 档平均点赞最高（748），社区经典浓度最高；D 档平均 LOC 最大（561 行）、pass 最多（2.4）、流体模拟词频 13 次/篇——工程最重却垫底。
- 模拟严谨度、游戏性、教程价值对评分均无正贡献：流体模拟重灾区在 D（Multiscale MIP Fluid、Where the River Goes），游戏复刻在 D（Bricks Game、Happy Jumping），PBR 教程在 D。
- 口味独立于社区热度：Dot Paint Soup（3 赞冷门）评 S，Protean clouds（1601 赞名品）评 D。

## 三、S 档：两个壁纸公式（暗部占比 0.46±0.38 的巨大方差 = 双峰分布）

### 公式一：暗场能量型（20 个中 11 个）

Rhodium liquid carbon、Flame、Plasma Globe、Sparks drifting、Volumetric explosion、Auroras、Star Nest、The Drive Home、Hex Glitch、Music - Pirates、Dry ice 2

结构：**画面大面积黑暗（多数暗部占比 ≥0.75）+ 唯一自发光主体 + 单一主色调统治**（火焰橙 / 等离子紫 / 极光绿 / 碳流红 / 极光绿）。黑暗是舞台，发光体是唯一主角，视线被强制钉在一个点。该组平均辉光占比约为 A 档 2 倍。

### 公式二：满屏图案型（20 个中 9 个）

Seascape、Octagrams、Geodesic tiling、Playing marble、Dot Paint Soup、Enscape Cube、Heartfelt、expansive reaction-diffusion、Phantom Star

结构：不靠黑暗，靠**单一主题 + 高纹理密度统治整个画面**。Seascape（细节密度 60.8）满屏浪花、Octagrams 全屏同种蓝色水晶几何、Dot Paint Soup 全屏淡彩斑点。注意细节密度本身不是决定项——全场细节密度最高的 Voxel Edges（85.8）与 20221105_inercia（84.0）都在 D 档：细节多而无主只是「图案样品」，细节被单一主题统治时才转化为壁纸耐看性。

### S 的惊艳配方（一句话）

> **一个统治画面的单一主色 +（大面积黑暗衬托的唯一发光体 或 高密度满屏图案）+ 一个情绪钩子（雨窗的悲伤、夜驾的孤独、月夜海的宁静）。**

## 四、A 档：均匀照明的风景与教材

13 个中 12 个暗部占比 <0.2——几乎全是白天感、均匀照明的画面（iq 云海、Snail 微距、Elevated 山景、Journey 沙漠、2D Clouds、Warping 大理石纹、up in the cloud sea）。另有教学构图子类：2d signed distance functions（形状+图例面板）、VolumetricIntegration（灰雾演示）。

A 比 S 少的三口气：

1. **没有黑暗舞台**——无戏剧性明暗对比，画面是「平的」；
2. **没有唯一发光焦点**——视线在整个画面游走，抓不住落点，即「没有惊艳」的直接原因；
3. **多色并置而非单色统治**（A 档彩度 31.7 vs S 档 17.3）——Journey 橙红金、Cloud Ten 橙蓝对撞、molten bismuth 青绿混杂；S 档几乎都是一个主色说了算。

分界线反例验证：A 档中 Let's self reflect（暗部 0.81，暗场+折射宝石）与 Zippy Zaps（辉光占比 A 档最高 0.098，满屏电光但背景偏亮）各自踩中公式一的一半——前者有暗场无叙事，后者有辉光无暗场——恰好停在 A，说明两个要素缺一不可。

## 五、B 档：「系统默认壁纸」的科学性

B 档量化指纹：主色调浓度最高（0.88）、细节密度最低（25.7）、光照均匀（快速海洋、纯蓝焦散、绿色黏液）——典型的单色平铺低刺激纹理。OS 默认壁纸的设计标准恰恰是：不干扰图标文字、无焦点竞争、低视觉噪音。「系统默认我还是挺喜欢的」等价于说 B 档全部符合 OS 壁纸设计规范——合格的背景，缺一记让人主动选择的重拳。

## 六、C / D 档

- C 档（各档平均细节密度最高，45.3，但无主次）：短小单技法演示/文化梗——分形金字塔、复古富士、湿石头、手绘滤镜；技法定义清晰但一眼看穿。
- D 档：图表感/玩具感/游戏感——文字面板（PBR 教程）、计分 HUD（打砖块）、玩具角色（Happy Jumping）、俯视模拟图（河流、MIP 流体）。在看「工程/游戏」，不再是「画面」。
- 两档作为整体可充当**反面检查表**：HUD 感、白底图表感、俯视素材感进入美术表现层 = 用户评 D 的特征。

## 七、量化指标（官方缩略图 320px 采样，PIL 统计）

| 指标 | S | A | B | C | D |
| --- | --- | --- | --- | --- | --- |
| 暗部占比（亮度<45/255 像素份额） | 0.462±0.380 | 0.101±0.214 | 0.389±0.332 | 0.294±0.233 | 0.230±0.332 |
| 辉光占比（亮度≥225） | 0.040 | 0.021 | 0.015 | 0.164 | 0.088 |
| 明暗对比（灰度标准差） | 41.9 | 41.2 | 46.1 | 59.8 | 40.3 |
| 饱和度均值 | 98 | 96 | 109 | 104 | 87 |
| 彩度（Hasler–Süsstrunk） | 17.3 | 31.7 | 16.3 | 20.5 | 25.1 |
| 细节密度（边缘图标准差） | 35.1 | 33.3 | 25.7 | 45.3 | 41.1 |
| 主色调浓度（饱和像素最大 60° 色相扇区份额） | 0.75 | 0.78 | 0.88 | 0.92 | 0.75 |

代码指纹均值：点赞 S748 / A703 / B552 / C568 / D617；平均年份 S2017.2 / A2017.0 / B2016.4 / C2017.0 / D2019.2；LOC S304 / A413 / B363 / C189 / D561；pass 数 S1.9 / A1.5 / B1.9 / C1.2 / D2.4；含音频通道 S1 / B1 / C1 / D1 个；鼠标交互 S 档 13/20。

方法与局限：指标为缩略图单帧启发式统计（非 shader 运行时全貌），细节密度受官方选帧影响；S 档内部双峰证明单一指标无法定义「惊艳」，真正分界在构图语义层（焦点唯一性 / 情绪叙事 / 主题统治）。

## 八、分档清单（57 个）

### S（20）
Seascape、Star Nest、Heartfelt、expansive reaction-diffusion、Flame、Playing marble、The Drive Home、Plasma Globe、Auroras、Rhodium liquid carbon、Hex Glitch、Volumetric explosion、Music - Pirates、Octagrams、Dry ice 2、Geodesic tiling、Sparks drifting、Phantom Star、Enscape Cube、Dot Paint Soup

### A（13）
Clouds、Snail、Elevated、Tribute - Journey!、2D Clouds、Zippy Zaps、Cloud Ten、Warping - procedural 2、VolumetricIntegration、up in the cloud sea、Let's self reflect、molten bismuth、2d signed distance functions

### B（7）
Very fast procedural ocean、Sirenian Dawn、Goo、Tileable Water Caustic、Ether、Planet Shadertoy、Liquid Toy

### C（5）
fractal pyramid、[NV15] Space Curvature、notebook drawings、Cyber Fuji 2020、Wet stone

### D（12）
Protean clouds、Happy Jumping、Voxel Edges、Multiscale MIP Fluid、The Universe Within、Paint streams、[SH17C] Physically Based Shading、20221105_inercia intended one、Bricks Game、Where the River Goes、LTH、Spectral Caustic

（名称 → 目录对照直接看 external/shadertoy/ 下的 index.html 卡片或 ratings.json；点赞数为存档时快照值。）

## 九、项目落点（等距像素海盗游戏）

1. **主菜单 / 加载页 / 宣传图**照 S 公式一：暗场 + 唯一光源 + 单主色。海盗题材天然契合——月夜锚地篝火、灯塔光、风暴闪电；参考 Music - Pirates 的月夜海、The Drive Home 的叙事感、Heartfelt 的情绪钩子。
2. **场景光照技法**翻 A 档：iq Clouds 是体积云标准答案、Elevated 是地形雾——A 档用来学，S 档用来抄氛围。
3. **UI 底板**按 B 档标准选低刺激平铺纹理（Tileable Water Caustic 的平铺思路直接可用）。
4. **水体复刻教训**：Seascape（S）与 afl_ext 快速海洋（B）同为程序化海面，差距在浪花细节密度与光照戏剧性，不在模拟复杂度——复刻时抄构图与光照氛围，不抄 Navier-Stokes。
5. **反面检查表**见第六节——像素游戏天然容易做出「图表感/玩具感」，D 档特征清单用于自检。
