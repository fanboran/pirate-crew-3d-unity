# 废弃化工厂 · 两关合并版 · 成品图集

> 产物来源：`tools/blender/scene/chemplant/assemble_chemplant_merged.py`
> （Blender 无头，复用两套化工厂 kit 的构件）；复现命令与坐标口径见该脚本头注释与
> [chemplant README](../../../tools/blender/scene/chemplant/README.md)。
> 实测：**31 节点 / 约 55 万三角面 / 可玩区 96 × 68 m / 总范围约 169 × 128 m**；
> FBX = `pirate-crew/Assets/Art/Models/SceneKit/ChemPlantMerged.fbx`（约 11.8 MB）。

## 一、成品图（1440×900，Cycles 32 采样，Standard 视图变换，曝光 −0.35 EV）

| 机位 | 图 | 看什么 |
| --- | --- | --- |
| 全场 3/4 鸟瞰（西南，主图） | [hero](hero.jpg) | 两期厂区的整体关系：南侧冷却塔、西侧办公楼/主厂房、北侧主装置区/罐区/旁楼、东侧精馏塔群 |
| 全场 3/4 鸟瞰（东南） | [overview](overview.jpg) | 冷却塔与主厂房、办公楼、罐区的围合关系；不规则围墙与墙外树线 |
| 厂内中层（自北向南） | [north](north.jpg) | 北侧塔罐区/旁楼与管廊的前后层次；墙外草地、远景厂房与树线填满天际线 |
| 主路街面（人视） | [street](street.jpg) | 人视高度：主厂房/管廊/塔群的前后关系，墙根积水、灌木与废料 |
| 南侧（自南向北） | [south](south.jpg) | 冷却塔作为南侧主地标的体量；南院杂物与罐区 |
| 俯视平面 | [top](top.jpg) | 地坪分块/裂缝/积水、道路网、设备足印与围墙轮廓、墙外视觉延伸区 |

## 二、这批图的口径（不是审美偏好）

- **合并方式**：只保留两套 kit 的建筑零件，布局彻底重构；不是两个整场件并排。
  围墙为不规则折线 + 四处坍塌缺口，缺口用废料堆、土坡、树丛封口。
- **视觉边界 ≠ 玩法边界**：可玩区止于围墙；墙外是草地/泥地/树线/远景厂房/堆场/土坡/电杆组成的
  **视觉连续、逻辑不可达**延伸区——从场内看出去不出现「一圈空海」，也不会读成悬空沙盘。
- **废弃质感（内外同一种荒地表皮）**：地坪是「泥土基面 + 残破水泥板阵列」——板间留缝、
  缝底压深色土条、缝里全长矮草，约 18% 的板整块缺失；**完整板面不撒草**，草只从板缝、
  缺板、裂缝、板边和墙脚长出来，树/灌木脚下有破板露土的树坑，道路只在裂缝/坑洞里长草。
  草色用中绿 + 土褐（不用鲜绿），墙根草甸与墙外连成一片。墙外同样以土色为底、
  草/碎石/泥斑混合，不是均匀草坪。另有灌木、枯树与阔叶树、藤蔓、大积水洼、裂缝、
  翘起水泥板、瓦砾、油桶、托盘木箱、倒伏电杆、废弃铁路支线；全部零贴图、零金属度，
  细节由几何密度与平色槽对比承担。
- **场外补地**：渲染时另加一块只进渲染、不进 FBX 的远景地面（z = −0.6），避免镜头越过
  总范围时读成悬空。

## 三、实机图

战斗场景实机出图待补：`-artReviewOut <目录> -artReviewLevel 3`
（合并件由 `RuntimeSceneArt` 按 `bakedPieces` 实例化）。

## 四、像素化管线成图（游戏内渲染路径，不是 Blender 渲染）

| 图 | 档位 |
| --- | --- |
| [pixel/pl5-wide.png](pixel/pl5-wide.png) | 宽机位（可见 32 m） |
| [pixel/pl5-mid.png](pixel/pl5-mid.png) | 中机位（可见 13.7 m） |
| [pixel/pl5-close.png](pixel/pl5-close.png) | 近机位（可见 7 m） |
| [pixel/pl5-dbg-outline.png](pixel/pl5-dbg-outline.png) | 描边调试图 |

口径：物体 pass → 低分辨率域着色 → 屏幕空间描边 → 点采样上屏；块边 2 屏幕像素、
俯角 30°、逐物体 3 档色带。载体场景 `Assets/Scenes/Pixelart/PixelartChemPlantMerged.unity`
（装配器 `PixelartChemPlantMergedSetup`，取景表关卡号 5），
出图命令：开发包 + `-pixelartOut <目录> -pixelartLevel 5`。

草叶是扇状草卡（一整张共面多边形，锯齿只在外轮廓），`Kit_GrassMid` 正常描边、
压成 1 档并关闭法线边压暗；树冠/灌木走 `Kit_GrassDark`，描边不变。平贴的泥/苔/
裂缝/水膜走 `Kit_Dec*` 槽并关闭描边，避免平贴片被描成“贴纸”。
图为 1920×1050：底部 30 px 是 Unity 开发包的 “Development Build” 叠字条，已裁掉，
画面内容未做修图。
