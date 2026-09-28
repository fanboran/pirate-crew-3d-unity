# r13 · 2:1 档位回归修正轮（描边内缝 / 地面高光 / 角色微调）

> 创始人报了两件事：**「卡线描边和内部色有缝隙了，尤其偏左下的位置」**与
> **「地面反太阳光导致旋转时颜色骤变」**；并授权角色建模微调（保持圆头+圆台身，目标"产生像素感"）。
> 本轮定位到三个独立根因、全部修复，出图对照如下。

## 1. 根因与修法（一句话版）

| 症状 | 根因 | 修法 |
| --- | --- | --- |
| 描边内侧一圈暗缝（随物体/机位漂移） | `ConnectivityResult.compute` 的聚合窗口按**奇数 k** 写死（中心±k/2 = 3×3=9 细像素）；k=2 后块只有 2×2=4——窗口跨进左/上邻块各半格、分母仍是 4，连通占比可超 1 且被邻块内容污染，内线降档在剪影内侧错误触发 | 窗口改为按块原点 `[0,k)²` 精确遍历（奇数档逐像素不变）；已登记[实现口径](../../../技术/渲染/像素化着色路径/实现口径.md)静默失效点 #9 |
| 地面/岛顶随相机旋转整片跳色 | Specular 趟对量化后高光**乘 `_Smoothness`**，0.25 的宽瓣高光带随 NdotH 扫过量化的 floor 边界整档跳变 | 配方源 `PixelartMaterialFactory` 与 60 个烘焙材质 `_Smoothness` 全部置 0（纯色带卡通一律无镜面） |
| 角色在细档下轮廓"糊"、面片浪费 | 头球 16×12 的面片在 1:3 与全场档跌破 1 艺术像素；底径 0.92 落在半像素上 | 头球 **12×8**（352→168 tri，色带阶梯变整）、底径 **0.4667**（Ø0.9333 = 36/24 px@1:2/1:3 特写全整数，锥度 0.750 整比） |

一个重要的**口径修正**随之落地：PixelScale 常量 3→2（2026-09-24 裁决）后，11 个像素场景里
序列化的 `pixelScale` 仍停在 3（没人重跑装配器）——出图判据（读常量）与实机画面（读序列化值）
劈叉。本轮把 11 个场景同步到 2，并新增运行时契约 `PixelartSceneContract` +
`PixelartSceneContractTests`（像素档位/环境光漂移当场红）。环境光同轮微调：
#3A4760 → **#37486B**（同明度蓝移，亮暖暗冷）。

## 2. 对照图（前 = 3:1 旧档 + 带高光；后 = 2:1 新档 + 全部修复）

| 图 | 看什么 |
| --- | --- |
| [pl1-mid-k3-before](pl1-mid-k3-before.png) → [pl1-mid-k2-after](pl1-mid-k2-after.png) | 云场：岛顶的**随机白色高光斑**（镜面带）在 after 中根除；细档网格更脆 |
| [pl3-mid-k3-before](pl3-mid-k3-before.png) → [pl3-mid-k2-after](pl3-mid-k2-after.png) | 空岛：角色/树冠剪影内侧的暗缝消失，色带与描边严丝合缝 |
| [pa-mid-k2-after](pa-mid-k2-after.png) | 试点中机位（2:1）：平台阶台三档分离干净，无伪影 |
| [pa-close-k2-after](pa-close-k2-after.png) | 角色特写（2:1）：12×8 头球的色带阶梯与描边 |
| [dbg-connect-k2-after](dbg-connect-k2-after.png) | 连通域占比调试图（2:1）：平坦区满值、真实棱线处回落——占比场回到 [0,1] 语义 |

**注**：「描边内缝」发生在**游戏内 2:1 档**（运行时挂 rig 读常量），修复前没有出图存证；
机制证据是代码层算术（9 采样 / 分母 4）+ 创始人实机症状，修复后 2:1 出图（上表 after）无该伪影。

## 3. 判据读数（`judge_pixelart_pilot.py`）

- 样板关 l1 / l3：**全部核心判据通过**（块边长 2 = 画布 960×540、光照未旁路 100%、场地在场）。
- 试点机制档：五项核心全过；**唯一 FAIL = 降档 A/B（0.007% < 0.1%）**——这是阈值语义在 k=2 的
  真实变化（内线降档在 2×2 块上只在"≥ 半块不连通"时触发，比 3:1 时代稀有得多），不是门控失联。
  是否为 k=2 重标 `aaScaler` 属美术裁决，已登记[待办](../../../项目/待办事项.md)「r13 后观感提案批」。

## 4. 最佳相机距离结论（本轮推断，零代码改动）

特写档 **OrthoSize = 7（可见 14 m）在像素网格上已最优**，不建议动：
1:2 档角色总高 71 px、头球 27 px（整）、底径 36 px（整）；1:3 档 48/18/24 全整；
地格（1 瓦片 = 2 m）在 1:2 档 77 px。次选 OrthoSize = 9（地格恰好 60 px 整）。
要更粗的像素颗粒，杠杆是**滚轮像素档（1:3–1:5）**，不是改相机距离。

## 5. 复现

```bash
U="F:/Unity/2022.3.62f1/Editor/Unity.exe"; P="F:/VSCode/pirate-crew-3d-unity/pirate-crew"
tools/headless/run.sh build PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs \
  -buildFlavor development -buildScenes development
E="F:/VSCode/pirate-crew-3d-unity/external/build/0.2.2/win64/PirateCrew3D.exe"
"$E" -pixelartOut "F:/VSCode/pirate-crew-3d-unity/export/pixelart-r13-fixed-pilot"
"$E" -pixelartOut "F:/VSCode/pirate-crew-3d-unity/export/pixelart-r13-fixed-l1" -pixelartLevel 1
"$E" -pixelartOut "F:/VSCode/pirate-crew-3d-unity/export/pixelart-r13-fixed-l3" -pixelartLevel 3
python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-r13-fixed-pilot
```

> ⚠ `-pixelartOut` 必须给**绝对路径**：`CreateDirectory` 按进程 CWD 解析、`ScreenCapture`
> 按播放器 Data 目录解析，相对路径会"目录建在 A 处、截图写往 B 处"全部静默丢失
> （采集入口已加 `Path.GetFullPath` 收口）。
