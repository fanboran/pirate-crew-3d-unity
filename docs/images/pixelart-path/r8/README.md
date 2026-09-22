# r8 · 台柱底径调整 + 云彩关接入（2026-09-22）

> **口径更名提示（r9 起）**：本轮的 `-pixelartCloud` 开关与 `PixelartCloudScene` 已并入统一的关卡档——
> 出图改用 `-pixelartOut <目录> -pixelartLevel 1`，取景表是 `PixelartLevelScene`，档位名前缀 `pl1-`。
> 本页正文按当时的口径保留（历史记录）。

> 本轮两件事都由创始人当轮直接要求：
> ①「你把角色的台柱下直径稍微改大一圈」；②「把云朵那关应用这一套渲染管线」。
> 另外顺带落了一条工程要求：**整条路径的资产与装配脚本收进单一文件夹**（§4）。

## 1. 角色台柱底径 0.40 → 0.46（只改这一项）

| 量 | 改前 | 改后 |
| --- | --- | --- |
| Body 圆台柱底半径 | 0.40（Godot `pirate.tscn` 原值） | **0.46** |
| 底径 | 0.80 m | **0.92 m**（+15%） |
| 锥度（顶 r / 底 r） | 0.875 | **0.761** |
| 顶半径 / 柱高 / 球头 r / 总高 | 0.35 / 1.20 / 0.35 / 1.85 | **全部不动** |

**与 Godot 基准的唯一一处有意偏离**（其余仍逐值一致）。理由来自 r6 §2 的诊断：中机位里角色只有
**15 个艺术像素高**，改前底径与顶径只差 **2 个艺术像素**，读不出"台柱"；改后差 **3.7 个**。
实现是 `CrewVisualPrefabBuilder.BodyBottomRadius` 一个常量，重烘 7 个职业预制体 + 网格资产
（幂等就地覆写，GUID 不变，场景引用不漂）。

**实拍**：[pa-mid](pa-mid.jpg)（中机位全景，红/蓝船员都能看出上窄下宽）、
[pa-close](pa-close.jpg)（近机位，台柱与球头的三段明暗清楚）。判据读数与改前同档
（块边长 3 / 平坦占比 0.954 / 亮暗跨度 0.82），说明只动了造型、没动渲染口径。

## 2. 云彩关接入：`PixelartCloud` 试点场景

**内容全部来自云彩关的真实数据**（不自己编云场）：

| 内容 | 来源 | 实测 |
| --- | --- | --- |
| 云场 `CloudField.prefab`、落水危险虚线 `ShowcaseDangerBorder.prefab` | 关卡资产的烘焙件摆位表（`ShowcaseLevels.BakedPlacements(1)`），与主战斗场景 `RuntimeSceneArt` 同一张表 | 2 件全部摆入，材质换装 3 个 renderer 无未登记项 |
| 7 个船员 | 关卡出生表（`cloud_walk` 的 `units`：格坐标 + 阵营）+ 逻辑高度场 `SurfaceWorldY` | 脚底 y = 4.5 / 4.5 / 6.5 / 7.5 / 4.5 / 4.5 / 2.5，**7/7 在云台上打到支撑面**（对云场碰撞盒打向下射线验的） |
| 镜头 | `PixelartCloudScene.Target` = (20, 4.5, 15) = 竞技场心 + 云顶中位高 | 装配期断言：**镜头中心 = 关卡场心 = 云场实例摆位**（三处同值才放行） |

**海面是替身（如实标注）**：关卡的活水面（OceanRig）是自带 shader 的运行时系统，**不在本路径的
材质口径里**（本路径的物体 pass 按层拉全部不透明物体，非本路径 shader 的 renderer 会留下未定义的
MRT 内容），故本场景用一块同高度（`WaterSurfaceY` = −0.4）的平色海面占位。
它只影响观感的丰富度，不影响"云台 / 船员 / 构图"这三件要判的事。

**实拍**：[pc-wide](pc-wide.jpg)（整片云场 + 危险虚线 + 全景比例）、
[pc-mid](pc-mid.jpg)（云台各有闭合墨线、7 人的阵营色可辨）、
[pc-close](pc-close.jpg)（云上船员的台柱与描边细节）、
[pc-dbg-outline](pc-dbg-outline.jpg)（墨线缓冲，**34434 个墨线像素**）。

## 3. 判据（两套档位，程序化跑）

```
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-r8        # pa-*（试点·图元几何）
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-cloud-r8  # pc-*（云彩关·真实内容）
```

| 档位 | 块边长 | 色数 | 平坦占比 | 亮暗跨度 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `pa-mid` | 3 ✓ | 27 | 0.954 ✓ | 0.82 ✓ | 与 r7 同档（造型改动不影响判据） |
| `pa-mid-nodowngrade` | 3 ✓ | 26 | 0.956 ✓ | 0.82 ✓ | 降档 A/B：0.161% 像素改变 ⇒ 内线在起作用 ✓ |
| `pc-mid` | 3 ✓ | 21 | 0.925 ✓ | 0.77 ✓ | **云场占比 49.2%** ✓ |
| `pc-wide` | 3 ✓ | 20 | 0.957 ✓ | 0.77 ✓ | 云场占比 13.3%（宽机位以海为主，符合构图）✓ |
| `pc-dbg-outline` | — | 2 | — | — | 墨线 34434 像素 ✓（描边那一趟在写缓冲） |
| 试点九张 + 云场六张 | | | | | **结论：全部核心判据通过** |

新增判据 **云场占比**（`judge_cloud_presence`，只对 `pc-*` 生效）：用色相统计量"暖色（云）"占画面的
比例，防的是"云场件没摆进来 / 摆到镜头外 / 材质还是旧链的"——这三种从块边长、平坦度、色数上
**全都看不出来**（像素化本身完全正常）。阈值 10%，实测 mid 49.2% / wide 13.3%。

## 4. 工程侧：整条路径收进 `Assets/Pixelart/`（创始人要求）

| 收进去的 | 之前散在 |
| --- | --- |
| 5 个 shader + `Includes/RimLight.hlsl` | `Assets/Art/Shaders/Pixelart/` |
| 5 个 compute（连通域三件 + 边缘光修正 + 调色板烘焙） | `Assets/Art/Compute/{Connectivity,Palette}/`、`Assets/Art/Compute/` |
| 7 个物体材质 + 9 张抖动图案 + 调色板 LUT | `Assets/Art/Materials/Pixelart/`、`Assets/Art/Textures/Fx/Dither/`、`Assets/Settings/Pixelart/` |
| 4 个编辑器脚本（装配器 / 两个场地装配 / 共用件） | `Assets/Editor/` |

**运行时 C# 没有跟着搬**：它属于 `PirateCrew.Rendering` 程序集，而 `PirateCrew.Gameplay` 是按程序集名
引用它的——挪出 `Assets/Scripts/` 会掉进 Assembly-CSharp、引用链断掉。索引与"字面量表"写在
`Assets/Pixelart/README.md`，文档索引在 `docs/技术/渲染/像素化着色路径/README.md`。
所有路径常量（`PixelartPath` / `PixelartPalette` / `PixelartStageKit` / `DitherPatternBaker`）
已同步到新目录，两档 URP 资产、两个场景、材质引用都按 GUID 走，无引用漂移。

## 5. 本轮踩的坑（都写进代码注释了）

1. **`Hex("#DE524D")` 前缀**：共用件里 `Hex()` 只吃不带 `#` 的写法，`CrewRed` 从文档抄了带 `#` 的
   写法 ⇒ 装配在"造材质"那一步抛 `FormatException` **整条中断**。已改成两种写法都收。
2. **批处理 Unity 关停挂死**：本轮撞到两次（邻居会话那次挂了 16 分钟、持着 `Library` 锁，
   只能强杀）。判别口径：日志出现 `Batchmode quit successfully invoked` 之后进程仍活且持续吃 CPU
   ⇒ 是关停挂死，不是还在干活；日志已确认工作写完，可以强杀。
3. **`git mv` 打印的搬运方式不可信**（脚本里把函数对象当条件用），搬家本身是按 tracked/untracked
   分别处理的，结果正确——但这类"日志骗人"的写法已在脚本里去掉。

## 6. 观感上仍可调的旋钮（本轮如实标注，未擅自调）

1. **云顶读作一片白**：云的 albedo 取自关卡槽位色的暖白（`#FFF2DB`），3 档色带下亮面顶到最亮档。
   嫌平可调 `Assets/Pixelart/Materials/PixelartCloud_CloudWarmWhite` 的 `_BaseColor`（压到 `#EFE0C4`
   一带）或把 `_MainLightLevel` 降到 2。
2. **海面是单色平涂**：平面只有一个朝向 ⇒ 只有一档。真海面要等 OceanRig 走 Blender/本路径的材质口径。
3. **船员没有投影**（实时阴影本轮仍默认关，理由见 r7 与 `PixelartStageKit.CreateSunAndAmbient`）——
   所以角色"贴"在云上的感觉偏弱。这是阴影那一项修好之后会一起解决的问题。
4. **危险虚线的红是画面里最响的颜色**：它是关卡内容（禁行边界），是否在本路径里降饱和属美术裁决。
