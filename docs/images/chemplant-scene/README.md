# 废弃化工厂样板关 · 成品图集（第三样板关）

> 产物来源：`tools/blender/scene/chemplant/`（Blender 5.2 无头，六件并行建模 + 总装）。
> 复现命令、坐标口径、材质纪律见该目录的 [README](../../../tools/blender/scene/chemplant/README.md)。
> 实测：**六节点 / 223,240 三角面 / 18 个调色板槽 / 包围盒 56.6 × 40.1 × 26.4 m**；
> FBX = `pirate-crew/Assets/Art/Models/SceneKit/ChemPlant.fbx`（2.75 MB）。

## 一、成品图（1440×900，Cycles 44 采样，Standard 视图变换，曝光 −0.35 EV）

| 机位 | 图 | 看什么 |
| --- | --- | --- |
| 全场 3/4 鸟瞰（西南，主图） | [hero](hero.jpg) | 一场六件的整体关系：塔区/罐区/管廊/旁楼/场地/杂物的相对位置 |
| 全场 3/4 鸟瞰（东南） | [overview](overview.jpg) | 旁楼主立面 + 仓库 + 集装箱一线 |
| 主路街面（站在路上向东） | [street](street.jpg) | 人视高度：管廊净跨、路缘石/标线/车辙、门卫室、堆放物 |
| 厂内中层（自北向南） | [north](north.jpg) | 冷却塔/罐区与管廊/框架塔的前后层次 |
| 主装置区 | [towers](towers.jpg) | 精馏塔操作平台、烟囱（残缺顶口 + 褪色警示环）、钢结构框架塔 |
| 罐区 | [tanks](tanks.jpg) | 塌顶储罐、球罐、双曲线冷却塔（破口 + 人字柱）、围堰 |
| 旁楼主立面 | [building](building.jpg) | 4 层窗带（破窗/封板/掉角）、入口雨棚、外挂消防梯、仓库卷帘门 |
| 俯视平面 | [top](top.jpg) | 场地分块板缝、道路网、围墙大门、荒草分布 |

## 二、低分辨率档参考图（`-pixel2`，块边长 = 2 屏幕像素）

`hero-` / `overview-` / `street-` / `north-` / `towers-` / `tanks-` / `building-` / `top-pixel2.jpg`
（如 [hero-pixel2.jpg](hero-pixel2.jpg)）：把成品图按**块中心**降采到 1/2 再用最近邻放大回来，
块边长取 `PixelartCameraRig.PixelScaleDefault` 的值（现役 2 ＝ 1080p 下 960×540 艺术画布）。

**这是近似，不是游戏内实拍**：游戏内另有屏幕空间描边、逐物体光带量化与帧级调色板
（见 [像素化着色路径/实现口径.md](../../技术/渲染/像素化着色路径/实现口径.md)），
本组图不做色阶量化，只说明「块边长锁整数倍 + 块中心采样」这两件事。

## 三、这批图为什么长这样（口径，不是审美偏好）

- **零贴图、零金属度**：本项目的像素化着色路径里，物体 pass 只写 `Albedo`（亮部色）+
  `Physical`（光滑度/金属度）+ `Palette`（主光档数/抖动/边光/描边开关），着色跑在低分辨率艺术画布上、
  还要过帧级调色板 —— PBR 贴图与金属度的细节落不住（低分辨率下还会变成闪烁噪点）。
  所以「质感」全部由**几何密度**（板缝/法兰/爬梯/栏杆/格栅/锯齿断口/波纹板竖棱）与
  **调色板对比**（锈 vs 混凝土 vs 漆色，锈痕一律薄板贴面不整面上锈色）承担。
- **机身侧写实**：渐变天穹 + 日光 + 冷补光、`Standard` 视图变换（AgX 会洗掉调色板色值）、
  曝光 **−0.35 EV**（`Standard` 无高光滚降，不收曝光浅色混凝土会冲成白片；该值是拿 9 槽并排的
  材质标定卡 + 正交相机读回像素量出来的，见 kit README §五）；场外另加一块**只进渲染、
  不进 FBX** 的荒地平面，避免厂区看起来像悬空沙盘。

## 四、实机图（游戏本体跑出来的，不是 Blender 渲染）

| 图 | 机位 / 说明 |
| --- | --- |
| [ingame/pl5-wide.png](ingame/pl5-wide.png) | 宽机位（可见高度 32 m；正交 30°、放大 2×） |
| [ingame/pl5-mid.png](ingame/pl5-mid.png) | 中机位（14 m ＝ 游戏内正交档 7，与玩家能看到的那一档同源） |
| [ingame/pl5-close.png](ingame/pl5-close.png) | 近机位（7 m） |
| [ingame/pl5-mid-density.png](ingame/pl5-mid-density.png) | 中机位 + 抖动图案 A/B（**游戏内默认不开**，本张只为验证"抖动通路真的走通"） |
| [ingame/pl5-dbg-albedo.png](ingame/pl5-dbg-albedo.png) · [ingame/pl5-dbg-outline.png](ingame/pl5-dbg-outline.png) | 调试缓冲（albedo / 描边），证明物体 pass 与描边趟真的写了 |

**出图链**（可复现）：场景 `Assets/Scenes/PixelartChemPlantTeam.unity`（装配器
`Assets/Pixelart/Editor/PixelartChemPlantTeamSetup.cs`，取景表关卡号 **5**）
→ 开发包（`BuildScript` 加 `-buildFlavors development -buildScenes development`）
→ 播放器 `PirateCrew3D.exe -pixelartOut <目录> -pixelartLevel 5`。

**与游戏内实拍的差别**：这批图经了低分辨率艺术画布 + 逐物体 3 档色带 + 屏幕空间描边，
**没有**经帧级调色板（P5 默认整趟跳过，见 [像素化着色路径/实现口径.md](../../技术/渲染/像素化着色路径/实现口径.md) §4.3）。
场外那块土色平面是**出图替身**（本关是内陆厂区，没有海面可借），真上玩法时会被关卡地形取代。
