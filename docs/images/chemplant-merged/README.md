# 废弃化工厂 · 两关合并版 · 成品图集

> 产物来源：`tools/blender/scene/chemplant/assemble_chemplant_merged.py`
> （Blender 无头，复用两套化工厂 kit 的建筑零件）；复现命令与坐标口径见该脚本头注释与
> [chemplant README](../../../tools/blender/scene/chemplant/README.md)。
> 实测：**22 节点 / 约 51 万三角面 / 可玩区 96 × 68 m / 总范围约 232 × 186 m**；
> FBX = `pirate-crew/Assets/Art/Models/SceneKit/ChemPlantMerged.fbx`（约 7.0 MB）。

## 一、成品图（1440×900，Cycles 32 采样，Standard 视图变换，浅蓝天穹 / 高角暖阳）

| 机位 | 图 | 看什么 |
| --- | --- | --- |
| 全场 3/4 鸟瞰（主图） | [hero](hero.jpg) | 奶油色大板广场、三处圆形苔藓花园、浅绿田野与圆树线；两期厂区的体量关系 |
| 全场 3/4 鸟瞰（东南） | [overview](overview.jpg) | 花园、道路、连续矮墙与墙外树线的过渡 |
| 厂内中层（自北向南） | [north](north.jpg) | 北侧塔罐区/旁楼与管廊的前后层次 |
| 主路街面（人视） | [street](street.jpg) | 人视高度：浅蓝主厂房、干净双坡屋面、花坛树、绿缝广场与圆桶 |
| 南侧（自南向北） | [south](south.jpg) | 冷却塔/罐区体量，南侧花园与墙外草地 |
| 俯视平面 | [top](top.jpg) | 大板分块、绿苔缝、三处花园、道路与墙外田野 |

## 二、这批图的口径（不是审美偏好）

- **休闲化重制**：奶油混凝土 / 软蓝钢 / 珊瑚锈 / 浅苔绿；轮廓圆润、明暗柔和，
  目标不是写实废土，而是休闲游戏的可读性与可爱感。
- **干净大板广场**：6~8.5 m 奶油混凝土板，板缝是 18~30 cm 细苔藓线；没有随机碎板、
  裂缝、污渍、路坑与散落小物件。
- **三处圆形苔藓花园**：浅沙圈 + 苔藓主体 + 浅蓝水洼；草丛是 2~3 个圆顶一簇的圆草丘。
- **连续矮墙 + 干净大门**：墙顶浅色压顶、圆墙柱、墙根苔藓带；没有断壁/锈痕/残基。
- **不低模**：新增树冠/草丘/花坛/墙柱/料仓全部 16+ 段、平滑法线；管道/栏杆圆柱最低
  16 段；主厂房补高分段细肋屋面。
- **墙外**：浅绿田野、两排圆树、缓草坡、四面远景厂房、三座料仓；从场内看出去不空。

## 三、实机图

战斗场景实机出图待补：`-artReviewOut <目录> -artReviewLevel 3`
（当前走查工具出图整屏浅蓝，第 1 关同样复现，属工具/相机问题）。

## 四、像素化管线成图（游戏内渲染路径，不是 Blender 渲染）

| 图 | 档位 |
| --- | --- |
| [pixel/pl5-wide.png](pixel/pl5-wide.png) | 宽机位（可见 32 m） |
| [pixel/pl5-mid.png](pixel/pl5-mid.png) | 中机位（可见 13.7 m） |
| [pixel/pl5-close.png](pixel/pl5-close.png) | 近机位（可见 7 m） |
| [pixel/pl5-dbg-outline.png](pixel/pl5-dbg-outline.png) | 描边调试图 |

口径：物体 pass → 低分辨率域着色 → 屏幕空间描边 → 点采样上屏；块边 2 屏幕像素、
俯角 30°、逐物体 3 档色带。载体场景 `Assets/Scenes/Pixelart/PixelartChemPlantMerged.unity`
（装配器 `PixelartChemPlantMergedSetup`，取景表关卡号 5，32 个槽），
出图命令：开发包 + `-pixelartOut <目录> -pixelartLevel 5`。

休闲化材质：奶油/软蓝/珊瑚锈域板；环境光浅蓝灰；浅天空蓝背景；深蓝灰柔墨线；
所有材质关闭“法线边压暗”，只留外轮廓；平贴苔藓/水膜 `Kit_Dec*` 不描边。
图为 1920×1050：底部 30 px 是 Unity 开发包的 “Development Build” 叠字条，已裁掉，
画面内容未做修图。
