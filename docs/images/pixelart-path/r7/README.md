# r7 · P4/P5 补齐后的首轮实拍（2026-09-22）

> 本轮把 v3 蓝本的 **P4（连通域三阶段 + 边缘光 + 四趟着色 + 合成）** 与 **P5（帧级调色板）**
> 全部落地，并按创始人 2026-09-22 的六项裁决调整了口径。图在这里，口径在
> [P4/P5 接口契约](../../../技术/渲染/像素化着色路径-P4P5接口契约.md)。

## 1. 本轮的架构变化（相对 r6）

| 维度 | r6 之前 | r7 |
| --- | --- | --- |
| 几何 / G-buffer | 低分辨率（= 艺术画布） | **屏幕档**（= 艺术画布 × pixelScale；照 v3：常规延迟 G-buffer 是全分辨率的） |
| G-buffer 张数 | 3（albedo / 法线 / 逐物体参数） | **7**（+ Normal0 几何法线 / Normal1 切线空间法线 / Physical 光滑度金属度 / RimLightProperty 逐物体边缘光色）+ 可采样深度 |
| 像素档位 | `pixelScale = 5` | **`pixelScale = 3`**（与 UI 的基本单位同网格；可见米数不变 ⇒ 取景不动、颗粒变细） |
| 描边 | 反向壳几何（`Cull Front` + 沿法线外扩） | **v3 口径的屏幕空间 4 邻域膨胀**（艺术画布，线宽恒为 1 艺术像素） |
| 着色 | 一趟全屏 | **四趟**（Diffuse / Specular / GI / RimLight）+ **合成** |
| 连通域 | 无 | **三阶段 compute**（Check / Flood×4 / Result）+ 漫反射里的降档门控（"内线"） |
| 边缘光 | 无 | 累加 blit + 3×3 去孤立点 compute |
| 帧级调色板 | 无 | CIEDE2000 LUT + ColorCorrection（**本轮默认关，见 §4**） |
| 实时阴影 | 无 | 机制接通（cast 相机 `renderShadows`、着色四趟的 shadow 关键字族）；**场景里暂时关掉，见 §4** |
| 附加光 | 无 | `LIGHT_LOOP` + `GetAdditionalPerObjectLight`（试点场景放了一盏暖点光验证通路） |

## 2. 实测判据（`tools/pixel-review/judge_pixelart_pilot.py`）

```
pa-mid   块边长 3  画布 640×360  色数 27  平坦 0.954  亮暗跨度 0.82
pa-wide  块边长 3                色数 26  平坦 0.971  亮暗跨度 0.80
pa-rim   块边长 3                色数 58  平坦 0.948  亮暗跨度 0.82
dbg-outline  墨线像素 17370（描边那一趟在写缓冲）
dbg-connect  连通比例 5 个取值（连通域判据算出分布了）
降档 A/B     pa-mid 与关掉降档的对照档差 0.162% 的像素 ⇒ **"内线"真的在起作用**
结论：全部核心判据通过
```

**口径提示**：`pixelScale` 5→3 后块边长期望值是 3，判据脚本从 `PixelartPilotScene.cs` 读这个数。

## 3. 两个根因（都是"只有出包/出图才现形"的静默故障）

1. **着色四趟的结果缓冲从来没发布成 shader 全局**。它们只被当渲染目标写过，而合成趟按
   `_PixelartDiffuseBuffer` 这样的**全局名**去采样 ⇒ 读到未绑定状态 ⇒ `clip(diffuse.a-1)` 把整屏剪掉，
   画面只剩清屏色。每一趟都在日志里"执行成功"，所以症状是"整屏同色、判据全 FAIL"而**没有任何报错**。
   修法：三趟画完后 `cmd.SetGlobalTexture(...)`（`PixelartShadingFeature.cs`）。
2. **没有法线贴图时，法线通道解出了倾斜 30° 的假法线**。`_BumpMap` 的 Properties 默认是内建 `"gray"`，
   而 `UnpackNormalScale` 在非 DXT5nm 分支走 `UnpackNormalmapRGorAG`：灰图 (0.5,0.5,0.5,0.5)
   解成 `xy = (-0.5, 0)`（本该恒等）。法线贴图活在**切线空间** ⇒ **同一世界朝向的面在不同物体上
   解出不同朝向** ⇒ 创始人报的「两个角色的打光方向不一致」「地面没有全局光照」是同一个根因。
   修法：用 `#pragma shader_feature_local _NORMALMAP` 把那段整体隔开（没挂贴图就不参与编译）。
   修后解析式核对：地面实测 (83,120,137) vs 期望 (81,115,129)；台面顶回到最高档 (240,205,141)，与 r6 逐位相同。

## 4. 本轮**故意没开**的两件（都留了开关与理由）

| 件 | 状态 | 为什么 |
| --- | --- | --- |
| 帧级调色板（P5） | 机制全在，**装配器默认把 palette 置空 ⇒ 那一趟整趟跳过** | 首轮实测 LUT 索引口径不对：背景蓝灰 (74,110,134) 被映射成暗紫 (75,61,79)，整屏发怪（创始人当场报「色板好怪啊」）；它还把判据的颜色假设一起带偏。开关：`PixelartPathInstaller.EnableFramePalette` |
| 实时阴影 | 机制全在（相机/关键字/`GetMainLight(shadowCoord)`），**场景里 `sun.shadows = None`** | 首轮实测地面整片被判在阴影里（解析式核对：地面实测 = 纯环境项），是阴影覆盖范围/偏移量级的调参问题，不是架构问题。开关在 `PixelartPilotSetup` 里那一行 |

## 5. 仍待办（按优先级）

1. **帧级调色板的 LUT 索引口径**（§4 第一条）：核对 `PaletteGenerationCIEDE.compute` 的
   16×16×16 烘制布局与 `PixelartColorCorrection.shader` 的 `float2(r/res + floor(b*res)/res, 1-g)`
   采样是否同一套（LUT 是 256×16 = 16 档 r × 16 档 b 铺在宽上、16 档 g 铺在高上）。
2. **描边闭合率的判据**：现任单图测法连错三种口径（底色写死 / 墨线被算进内容 / 找墨线的半径用了
   1 屏幕像素而环宽是 1 艺术像素），已降级为"仅供参考"；中间缓冲对测又因**地面铺满全屏**使
   "覆盖边界"退化而失效。正确写法要用**深度/法线不连续**定义剪影（= 连通域手里那份数据）。
3. **实时阴影调参**（§4 第二条）：URP 资产的阴影距离/级联 + 光源 bias，针对 160m 大平面 + 正交 cast 相机。
4. **附加暖光强度**：7 → 2.2 之后仍偏亮（画面上一片淡黄池）；它是一个 `LIGHT_LOOP` 里没有逐物体
   距离项的近似，最终强度属美术裁决。
5. **Bayer 档跳变率 0.18**（判据 OK）与密度图案 1.00 都正常；但**抖动幅度语义**在四趟拆分后需要复核
   （r6 时 Bayer 是 0.154、现在 0.177，同一材质参数下）。
6. 文档：`像素化着色路径.md` 的 §2（执行顺序表）与 §4（描边配方）还是**旧架构**的描述，
   要按本契约重写；本轮只落了契约与这份 README。

## 6. 交付清单（本轮改动）

- 协调者层：`PixelartPath.cs`（常量全集）、`PixelartCameraRig.cs`（两档缓冲 + UAV 位）、
  `PixelartObject.shader`（7 MRT + 物体级像素吸附）、`PixelartObjectFeature.cs`、
  `PixelartBeforeRenderFeature.cs`、`PixelartShadingFeature.cs`（四趟驱动）、
  `Editor/PixelartPathInstaller.cs`（七特征 + 资产引用 + 调色板 + URP 光照开关）、
  `Editor/PixelartPilotSetup.cs`（材质属性集 + 附加光 + 阴影开关）、
  `PlayerArtCapture.cs`（12 档出图：含 pa-rim 边缘光档、pa-mid-nodowngrade 降档 A/B 对照）
- 契约：`docs/技术/渲染/像素化着色路径-P4P5接口契约.md`（新建，冻结名字/分辨率/格式/语义）
- 判据：`tools/pixel-review/judge_pixelart_pilot.py`（自校准底色 + 降档 A/B + dbg 两档专属判据）、
  `tools/pixel-review/ink_gap_probe.py`（新建，墨线缺口定位）
- 并行 agent 交付：连通域三阶段（`Connectivity.hlsl` + 3 个 compute + Feature）、
  屏幕空间描边（shader + Feature）、边缘光（shader + compute + Feature）、
  四趟着色 shader + 调色板（`PaletteGenerationCIEDE.compute` + ColorCorrection + `PixelartPalette.cs`）
