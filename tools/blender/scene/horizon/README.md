# tools/blender/scene/horizon —— 远景装饰 kit（HorizonKit，8 件）无头建模管线

> **这份目录解决什么问题**：M4 大海域世界化的「远景是舞台幕布」层（`docs/技术/M4-大海域世界化.md` §1/§2.1）——
> 地图不可达区域堆纯剪影装饰，让地平线永远有内容。8 件远景件全部 **Blender 5.2 无头纯程序化**建模、
> 零贴图、shade_flat、**无站面无碰撞**（不产 standable manifest）。低多边形但精细度靠**轮廓层次**取胜：
> 山脊起伏、云团堆叠、鲸背弧线、螺旋盘绕、肋拱错落——不做表面小细节（远景看的是剪影与体量感）。

## 复现命令

仓库根 `F:/VSCode/pirate-crew-3d-unity/` 执行（全量约 4 分钟，OPTIX 不可用自动回退 CPU）：

```bash
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/scene/horizon/horizon_kit.py
# 可选参数：
#   --only <FBX名>   只重建一件（DistantIsleS / DistantIsleM / DistantIsleL / CloudBankL /
#                     WhaleSurfacing / LeviathanTentacle / GiantRibs / FarFleet）
#   --samples N      预览图采样数（默认 64）
#   --no-render      只建模 + 导出 FBX，不出预览图
```

**产物**（重跑幂等覆盖）：

| 路径 | 内容 |
| --- | --- |
| `pirate-crew/Assets/Art/Models/WorldKit/Horizon/<名>.fbx` | 8 件远景件（每件单网格 ≤2 材质槽） |
| `docs/images/worldkit-horizon/<名>-front34.jpg / -side.jpg` | 每件 2 视角预览 1024² q90（中性灰底三灯，`view_transform=Standard` 防 AgX 洗色） |
| `external/worldkit-horizon-work/horizon_kit_debug.blend` | 调参用 GUI 缓存（gitignored） |

预览相机**拉远到 150–340 单位**（`horizon_kit.py` 参数区 `CAM` 表），模拟玩家从可玩区远眺的真实观景距离——
图里件显得小是刻意的：判读的是「远观剪影是否成画、两级剪影色是否拉开」。

## 实测口径（2026-09-17 运行值，`STAT` 行）

| 件 | tri | 材质槽 | 包围盒 L×W×H (Blender XYZ) | 规格 | 原点 |
| --- | --- | --- | --- | --- | --- |
| DistantIsleS | **248** | FarNear | 40.0 × 11.0 × 9.6 | 宽 40 / 3 山头 / 最高 ≤8 | 足印中心 z=0 海平面 |
| DistantIsleM | **884** | FarNear + FarFar | 98.0 × 34.2 × 18.2 | 主体宽 90（远坡探出）/ 脊线最高 ~16 / 海蚀拱 ×1 | 同上 |
| DistantIsleL | **1104** | FarNear + FarFar | 180.0 × 80.3 × 29.8 | 宽 180 / 层峦 3 深 / 最高 ~27 | 同上 |
| CloudBankL | **1170** | Cloud | 79.4 × 17.1 × 7.2 | 80×20×8 / 7 团平底云 | 同上（云悬空，底 z=0.6 削平） |
| WhaleSurfacing | **384** | FarNear | 25.0 × 6.8 × 8.1 | 长 25 / 背弧顶缘出水 ~4 / 背鳍+尾鳍两片 | 同上 |
| LeviathanTentacle | **1228** | FarNear | 16.2 × 15.0 × 18.9 | 高 18 / 螺旋 2.5 圈 / 吸盘棱面环 ×12 | 同上 |
| GiantRibs | **2590** | FarNear | 42.0 × 7.9 × 19.2 | ~40 宽拱廊 / 7 根肋弧 + 断脊柱 | 同上 |
| FarFleet | **642** | FarNear + FarSail | 35.0 × 14.5 × 8.9 | 3 艘小帆船（各 8 长）错落 | 同上 |

全部过 `style_tokens.budget_guard('horizon')` ≤3000 tri/件闸；每件 ≤2 材质槽（脚本内另行断言，任务硬约束）。

## 设计说明（每件轮廓思路）

- **远岛三件**共用「山脊放样」器：沿 X 取站 × 每站 6 点山形剖面（水线→肩→脊，flat 棱面感），
  轮廓 = 高斯峰叠加（主起伏）+ 高频正弦 jag（脊线小锯齿）+ 平面走向微摆。峰越陡剪影越「山」。
  - **S**：3 峰独立成头，体量最小。
  - **M**：主体拆两段放样，中间豁口由**海蚀拱**（两腿 + 半圆弧管）跨接——拱洞透光、
    背后透出 FarFar 远坡，这是「明显海蚀拱」的唯一可读解（拱叠在山影前会被埋没）。
  - **L**：三排山脊沿 Y 纵深排布（近排 FarNear 最高 → 中排/远排 FarFar 递矮递淡），
    正面与侧面都读得出「层峦 3 深」；两级剪影色由槽色直接分层。
- **CloudBankL**：7 团确定性伪随机的扁棱面椭球簇（主球 + 2–3 副球），球心压低使球底被
  `z_cut` 削平 → 一条底平的积云带压着海平线；顶钳制在规格高内。
- **WhaleSurfacing**：轴线（正弦背弧 + 尾端上翘项）× 椭圆截面放样；「背弧出水 4」按
  **截面顶缘**最高计（轴线峰 = 4 − 峰处截面半径）；背鳍 = XZ 轮廓竖直薄片、尾鳍 = XY 轮廓
  水平薄片（靠尾端上翘立出水面上——水线下会被海面挡住，预览地板即海平面）。
- **LeviathanTentacle**：螺旋轴线（2.5 圈、半径随高度收缩）× 八棱截面（棱面感）；吸盘为朝螺旋轴心、
  略上仰的喇叭短管 ×12；尖端外弯小钩。
- **GiantRibs**：7 根圆弧管（角范围 >π，两腿插水下）沿 X 排开，半径错落成拱廊起伏，中段粗两端细；
  弧顶关节鼓包；一条两段错位的断脊柱立在后排（断口截面可见）。
- **FarFleet**：单艘 = 七点剖面放样船壳 + 双桅 + 艏斜桁 + 双横帆（微鼓风弧面）+ 前三角帆，
  局部建模后经旋转/缩放/平移并入；三艘错落（位置/朝向/缩放均不同，含透视暗示的远艇）。

## 坐标与朝向（Unity 侧必读）

- Blender 内：+Z 上、XY 海平面（z=0 海平面）、原点 = 足印中心、**正面朝 -Y**、主展开轴沿 X；
  底部插入水下（z −0.6 ~ −4.2，落位时根节点贴水面 y=-0.4 即可，水下部分自然被海面遮住）。
- FBX 导出（`axis_forward='-Z', axis_up='Y'`）后 **+Y 为上、正面朝 -Z**；Unity 导入配
  `bakeAxisConversion=true` 后正面朝 Unity +Z、宽沿 Unity X。
- 缩放：Blender 1 单位 = 1 米；导出 `apply_scale_options='FBX_SCALE_NONE'`；Unity 导入必须
  `useFileUnits=true; useFileScale=false; globalScale=1`（`useFileScale=true` 会把 cm×0.01 乘进来，
  实测踩坑见 `tools/blender/scene/README.md`）。
- 材质：`materialImportMode=None`，按槽名前缀 `Kit_` 用 URP/Lit 重建（色值与
  `style_tokens.SLOTS` 同源）；远景件**不要**生成碰撞体（无 standable manifest，不可达区域装饰）。
- 远景件摆放距离参考（`docs/设计/美术风格指南.md` §4.4 雾绑定 Q-9）：剪影 Z 落在雾 end 的 70% 以内
  （end=140 时 Z≈-38~-70）；FarFar 槽件可更远，靠雾色同化自然分层。

## 调参行号（`horizon_kit.py`）

| 要调什么 | 位置 | 参数 |
| --- | --- | --- |
| 采样/出图开关/曝光 | 参数区 | `ONLY` / `SAMPLES` / `RENDER` / `LIGHT_BOOST`（2.5 = ST 灯位下远景大件的实测曝光修正） |
| 观景相机距离/高度 | 参数区 `CAM` | 每件 `dist`（150–340）/ `h` |
| 远岛山形 | 参数区 `ISLE_S/M/L` | `peaks`（主起伏）/ `jag`（脊线锯齿）/ `hw_*`（体厚）/ `y_*`（平面摆动） |
| 海蚀拱 | `ISLE_M` 的 `arch_*` | 腿位/腿高/弧升/管径 |
| 云 | 参数区 `CLOUD` | 团位 `xs` / 削平高 `z_cut` / 顶钳 `z_top_max` / 球径 / 副球数（`seed` 固定可复现） |
| 鲸 | 参数区 `WHALE` | 轴线三项 / 截面比例 / 鳍轮廓点 |
| 触手 | 参数区 `TENTACLE` | 圈数/半径/高度分布 / 吸盘阵列 / 尖钩 |
| 肋拱 | 参数区 `RIBS` | 肋位 `xs` / 弧径 `radii` / 圆心高 `cz` / 断脊柱 `spine` |
| 船队 | 参数区 `FLEET` | 三艘摆位 `ships` / 帆桅几何 |

## 已知边界

- `style_tokens.setup_preview_world` 在 Blender 5.2 因 `bpy.data.curves.new(type="TEXT")`
  枚举改名（`FONT`）直接崩溃——本 kit 在脚本内**本地复刻**了同一套预览台
  （灯位/能量/色值全部取 `style_tokens` 常量与函数体同源值，未散写），并留 `LIGHT_BOOST` 旋钮。
  待协调者修 tokens 后可切回调用。
- 预览地板 = z≈0 的不透明灰盘（当海平面用）：任何 z<0 的建模细节在预览图里不可见，
  排查「某部件不见了」先查它的 z 是否在水下。
- 远景件与 Unity 侧 `WorldMapComposer` 的远景环接线（摆放表/氛围档联动）由协调者完成，本 kit 只出资产。
