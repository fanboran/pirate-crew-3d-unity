# CellShade 缺口规格

> **这份文档是什么**：参照库（代号 **CellShade**）与本仓现役像素化着色路径的**逐特性缺口清单 + 补移植规格**。
> 与 [v3蓝本.md](v3蓝本.md) 分工：蓝本回答"架构怎么改"，本规格只做两件事——**A. 逐特性核对"参照库有 / 本仓有无"**；
> **B. 参照库参数落进本仓 2:1 像素比（960×540@1080p，`pixelScale` = 2）时的换算与重标清单**。
> 蓝本已读通的内容（双相机/缓冲表/死 pass/两处 bug）不重复，只引条目。
> **未覆盖区**：001/003/004/007/008 五个示例场景未逐参数核对（003 对齐、004 UV、008 树与缺口主题正交；
> 001 降采、007 抖动图案已由蓝本 §4.2 与本仓 DitherPatternBaker 覆盖）；`Leaves.shader`/`LeavesPass.hlsl`
>（树叶双面+顶点波动）与 `OutlinePass.hlsl` 细节未入本规格（描边已按本仓口径另走，见渲染篇 §6.1）。

- **参照库**：`external/sloane-urp/`（作者 SL0ANE，MIT，Copyright (c) 2024 Sloane，见 `external/sloane-urp/LICENSE`）。
  工程版本 `ProjectSettings/ProjectVersion.txt` = **2022.3.62f1c1**（任务口径的 2022.3.43f1 与现文件不符，以文件为准——目录被本机 Unity 打开过，版本行被覆写）；URP **14.0.12**（`Packages/manifest.json`）。
  下文 `$P/` = `external/sloane-urp/Assets/SloanePixelartRenderPipeline/`。
- **本仓**：`pirate-crew/`，Unity **2022.3.62f1c1** + URP 14.0.12；缺口核对基准 = 主仓 `eb806c0c`（2026-11-07）工作区。
  本仓路径 = `Assets/Scripts/PirateCrew/Rendering/Pixelart/`（C#）+ `Assets/Resources/PixelartShaders/`（graphics shader）+ `Assets/Pixelart/`（compute/hlsl/材质）。
- **对账纪律**：材质/场景参数全部从序列化资产直接读出；关键度量（LUT 布局、CIEDE 变形、光向、量化锥半角）做了 CPU 复算并标注【复算】。

---

## 0. 缺口速览

| # | 特性 | 参照库 | 本仓 | 缺口定性 |
|---|---|---|---|---|
| 1 | Specular 高光 pass | `$P/Shaders/Blit/Shading.shader:28-42` | `PixelartShading.shader:444-540` 有同式 pass | **机制已移植**；缺口在**喂入端**（工厂把 134 个材质 `_Smoothness/_Metallic` 全写 0 ⇒ 实际关闭）与 **GI 趟的环境反射项** |
| 2 | GI（环境 BRDF） | `ShadingPass.hlsl:168-181` | `PixelartShading.shader:551-583` = `环境色 × albedo` | **有缺口**：缺 NoV 两档量化（连通域门控）与环境反射；本仓是刻意简化，补不补是裁决项 |
| 3 | RimLight 逐物件光列表（32 盏） | `SloanePixelartRimlight.cs` + `StructuredBuffer` | 光向恒取主光，无列表 | **有缺口**（5 个子项，§2.3） |
| 4 | 帧级调色板 | CIEDE2000 烘 LUT + ColorCorrection + 6 套资产 | **零**（2026-09-29 裁决整删） | **最大缺口**（§3，含本次实测的度量变形证据） |
| 5 | 连通域三阶段 | Check/Flood×8/Result | 已移植（Check/Flood×3/Result） | 已覆盖；差异见 §4（一处偶数 k 窗口 bug 未继承 v3 原样、但也未在本仓修） |
| 6 | 帧内线降档 | 降 1 档（`ShadingPass.hlsl:53`） | 降 2 档（`PixelartShading.shader:213-214`） | **刻意差异**（创始人 2026-09-30 定值），保留 |
| 7 | 006 Gold 场景配方 | 全套（§5） | 无对应物 | **配方清单**，§5 逐项 |
| 8 | URP 附加光设置 | URP 资产 `PerObjectLimit 4`、逐像素 | 两档 URP 资产 `PerObjectLimit 0`、渲染模式值 2（URP 14 中 = **PerVertex**） | **口径错、当前无可见影响**：本路径按全局条数与全局数组取光，不走逐物体索引（§7 第 1 条） |
| 9 | ShadowCaster pass | `Default.shader:101-124`（含吸附后回世界系再投，`ShadowPass.hlsl:21-60`） | `PixelartObject.shader` 单 pass、`Fallback Off`，无 | **有缺口**：本路径物体不进主光阴影图；006 金锭落地影的前提（§7 第 4 条，现有"投影"观察需实机归因） |

---

## A. 逐特性核对

## 1. Specular 高光

### 1.1 参照库公式（一手回读，`$P/Shaders/Includes/Passes/ShadingPass.hlsl`）

- pass 注入：`Shading.shader` pass 1 "Specular"（`$P/Shaders/Blit/Shading.shader:28-42`），`RenderPassEvent.AfterRendering`，清屏在 `BeforeShadingPass`（`SloanePixelartShadingRendererFeature.cs:157-164`）。
- `SpecularShading`（`ShadingPass.hlsl:117-126`）：

```hlsl
halfVec  = SafeNormalize(light.direction + viewDir)          // :119
NdotH    = saturate(dot(normal, halfVec))                    // :121
modifier = pow(NdotH, expSmoothness) * distanceAtt * shadowAtt
modifier = multiStep(modifier, level, 0.0, 0.0)              // :123 两档量化（level 恒 2.0）
return light.color * specular * modifier * smoothness        // :125
```

- `SpecularFragment`（`:128-166`）的喂入链：
  - `smoothness = physicalProp.r`、`metallic = physicalProp.g`（`:141-143`；`Physical` 缓冲由物体 pass 写，`DefaultPass.hlsl:66-67` = `_Smoothness/_Metallic` 原值）；
  - `expSmoothness = exp2(5.0 * smoothness + 1.0)`（`:142`）；
  - `specular = lerp(float3(1,1,1), albedo.rgb, metallic)`（`:144`）——金属体高光取体色；
  - `viewDir = GetViewDir()` = `PIXELART_CAMERA_MATRIX_I_V` 第 3 列（`:112-115`，正交常量视线）；
  - 主光 + `LIGHT_LOOP` 附加光都调，`level` 恒 **2.0**（`:158`、`:162`）；
  - 输出写 `_SpecularBuffer`（320×180，`R16G16B16A16_SNorm`），合成趟相加（`CombineFragment:239-243`）。
- `_SpecularHighlights` / `_SpecColor` 属性在 `$P` 的 shader 源码内**零消费**（全库 grep 无命中）——它们只存在于 mat 序列化块里，是 URP Lit 模板残留，**不是消费链的一部分**；不要为对齐它们立项。

### 1.2 本仓现状

- `PixelartShading.shader` 第 2 趟 "PixelartSpecular"（`:444-540`）：`SpecularShading`（`:463-473`）与 v3 `:117-126` 逐行同式；`expSmoothness = exp2(5s+1)`（`:499`）、`specular = lerp(white, albedo, metallic)`（`:502`）、正交常量视线 `PixelartViewDirWS()`（`:162-165`）、主光/附加光 `level` 恒 2.0（`:519/:527`）、`distanceAttenuation` 强制 1（`:514`）、光向/光色以 rig 下发全局覆盖（`:516-518`）。`Physical` 缓冲（ARGBHalf 直存）由 `PixelartObject.shader:301` 写。
- **缺口不在 shader，在喂入端**：`PixelartMaterialFactory.Configure` 把每个材质写死 `_Smoothness = 0`、`_Metallic = 0`、`_RimLightColor = black`（`PixelartMaterialFactory.cs:185-187`）；全仓扫描【复算】：吃 `PixelartObject` shader 的材质 **134 个、`_Smoothness/_Metallic` 非零者 0 个**。⇒ Specular/GI 金属两条通路**机制在、数值恒零**，画面贡献为 0。
- 关闭是**裁决不是疏漏**（`PixelartMaterialFactory.cs:156-163` 注释）：`level=2` 两档量化在大平面上随相机方位整档翻面（创始人"地面反太阳光、旋转时颜色骤变"）；恢复前提 = 先软化量化。

### 1.3 补移植规格（按"闪闪发光"目标倒推）

1. **量化软化（前置，否则跳变原样重现）**【提案】：把 `MultiStep(modifier, 2, 0, 0)`（`PixelartShading.shader:470`）的翻档边界做亚像素抖动——用现成 `Palette.g` 抖动偏移施加式（`偏移 × 0.5/level`，`:205` 同款）把 `modifier` 在量化前偏移，使高光带边界带 1 艺术像素的密度过渡；`level` 2→4 可作对照档。落点：`PixelartShading.shader` pass 2 与 `SpecularShading`。
2. **金属喂入**：`PixelartMaterialFactory.Configure` 增加可选参数 `smoothness/metallic`（默认维持 0，不回退既有画面）；金属件（铁锭/黄铜/钢）显式给 `(_Smoothness, _Metallic)`。落点：`PixelartMaterialFactory.cs:168-193`；参照值直接抄 §5.2 的 Gold/Silver/Rock 行。
3. **GI 反射项（与高光配套，见 §2 缺口 2）**：金属体在 v3 的亮部第二来源是 GI 趟的 `EnvironmentBRDF`（反射探针/天空盒 + fresnel）；本仓 GI 只剩 `环境色 × albedo`，金属开 `_Metallic` 后 Diffuse 趟 `× (1-metallic)`（`:431`）会**只减不加**，画面变暗。⇒ 补金属时必须同时给 GI 趟补反射项，二选一：
   - a【提案】轻量版：`gi = lerp(环境色, 反射色, fresnel^4)`，反射色取 `RenderSettings.ambientLight`（或探针），fresnel = `Pow4(1-NoV)`，`NoV` 走 v3 同款两档量化（`multiStep(NoV, 2, 0, levelBias)`，`levelBias` 连通域门控，见 `ShadingPass.hlsl:173/:227`）；
   - b. 完整版：搬 `GlobalIlluminationShading`（`ShadingPass.hlsl:168-181`）+ `BRDFData` 手工填充段（`:205-218`）+ `GlossyEnvironmentReflection`。
   - 刻意差异可保留项：`NoV` 量化是 v3 给"边缘一圈提亮"的手段，若只要金属亮部可先不做。
4. **不必移植**：`transmission` 参数（v3 `:58`，两处调用恒 0.0）、`FibonacciSphereMap`（`:78/:197` 注释掉的法线阶梯化，`FibonacciSphere.hlsl` 全文）、`_SpecularHighlights/_SpecColor`（无消费）。

---

## 2. RimLight 边缘光

### 2.1 参照库链路（一手回读）

| 环节 | 位置 | 内容 |
|---|---|---|
| 光源组件 | `$P/Scripts/MonoBehaviour/SloanePixelartRimlight.cs:22-78` | `[ExecuteAlways]`；`m_Color`（:32-33）；方向 = **组件 forward**（:39/:53）；静态字典汇集，`RefreshRimLights` 每帧把最多 **32** 盏（`MAX_RIMLIGHTS` :28）拷进定长数组（:65-77，**无越界保护**，>32 盏即崩，蓝本 §4.4） |
| 上传 | `SloanePixelartShadingRendererFeature.cs:71-83` | `ComputeBuffer(32, sizeof(float)*6)`（:73）；blit 前回调 `SetGlobalBuffer(_RimLights)` + `SetGlobalInt(_RimLightsCount)`（:81-82） |
| 着色 | `ShadingPass.hlsl:246-284` RimLightFragment | 逐盏循环 `for(i < _RimLightsCount)`（:277-281）；每盏：`rimLightProp.rgb × rimLightProp.a × RimLightShading(...)`（:280）。**主光/附加光参与 rim 的那段被整段注释**（:269-275）——v3 的边缘光**只**吃这 32 盏自定义光 |
| 门控函数 | `$P/Shaders/Includes/RimLight.hlsl:10-25` | `ndotl = saturate(dot(direction, normal)) * factor`（:12-14，**要表面朝向那盏 rim 光**）；屏幕空间光向四方向 clamp（:16-20）；`modifier = multiStep(modifier * ndotl, level, 0, 0)`（:22，**两档量化**，调用点 level 恒 2.0）；返回 `color * specular * modifier`（:24，**乘高光色** = `lerp(white, albedo, metallic)`，`ShadingPass.hlsl:262`） |
| 门控参数 | `ShadingPass.hlsl:280` | 四方向入参 = `connectedToX | !closerThanX`（连通**或**不比邻域近 ⇒ 该路关闭） |
| 清洁工 | `$P/ComputerShaders/RimLightCorrection.compute` | 3×3 去孤立点（就地，kernel "Main"，:7-41）；由 `m_RimLightComputeShader`（Cast Renderer.asset:30）走 `ComputeShaderPass` 派发（`SloanePixelartShadingRendererFeature.cs:66-69/:92-94`） |
| 逐物体颜色/开关 | `DefaultPass.hlsl:85` | `outRimLight = _RimLightColor`（rgba 原样；**a = 0 的材质整件无边缘光**，如 DefaulBlue.mat `_RimLightColor a: 0`） |
| applyAA 抑制 | `ShadingPass.hlsl:93/:222` | rim 非零 ⇒ `applyAA = 0`（降档抑制）；**bug：`g` 判两次、`b` 没判**（蓝本 §4.4 第 2 条） |

### 2.2 本仓现状

- `PixelartRimLightFeature.cs`（清 → 全屏累加 → `RimLightCorrection.compute` 三步，`:106-107`）与 `PixelartRimLight.shader`（覆盖 clip :83-85、墨线排除 :88-90、门控调用 :99-101）、`Assets/Pixelart/Shaders/Includes/RimLight.hlsl`（四方向门控 :78-94、`connected | !closer` 语义 :66-74、位解码 :60-63）、`RimLightCorrection.compute`（3×3，:42-84）——**门控/清洁工/缓冲三件与 v3 同构**。
- **刻意差异（有注释、不要改回）**：光向取主光 `_PixelartLightDirWS`（`RimLight.hlsl` 文件头 :18-21）；ndotl 与 multiStep 两步**故意没接**（:98-106：接 ndotl = "只有朝主光的面才亮"是背光收窄裁决；接量化需要新登记档数全局）；光向沿视轴的 normalize(0) 加固（:84-86）；`applyAA` 判 r/g/b 三通道（`PixelartShading.shader:366-368`，修 v3 蓝通道 bug）。
- 材质侧：`_RimLightColor` 全部黑、`rimlight.a` 恒写 1（`PixelartObject.shader:305`），shader 不读 `.a`。

### 2.3 缺口清单（参照库有 → 本仓无）

| # | 缺口 | 参照库证据 | 补移植规格 |
|---|---|---|---|
| 2-1 | **逐物件边缘光色 × 光源色的乘积**：v3 亮色 = `rimProp.rgb × rim光色 × specular 色`；本仓 = `rimProp.rgb × 门控`（无光源色、无金属 tint） | `RimLight.hlsl:24`、`ShadingPass.hlsl:280` | 【提案】落点 `PixelartRimLightShading`（`RimLight.hlsl:107-110`）：返回式乘 `lightColor`（rig 已有 `_PixelartLightColor`）；金属 tint（`lerp(white,albedo,metal)`）随 §1.3 第 2 条一起接 |
| 2-2 | **ndotl 朝向项**：只有面朝 rim 光才亮 | `RimLight.hlsl:12-14` | 要接需先推翻"背光侧收窄"裁决（`RimLight.hlsl:98-102` 注释）——**归美术裁决，不默认接** |
| 2-3 | **两档量化**：`multiStep(modifier*ndotl, 2, 0, 0)` 把边缘光撕成 0/1 两态（放大后是硬边亮块，Gold 场景"闪"的来源之一） | `RimLight.hlsl:22` | 【提案】同 2-1 落点；需先在 `PixelartPath.cs` 登记档数全局（现无合法名字，`RimLight.hlsl:103-106` 注明） |
| 2-4 | **逐物体开关 `.a`**：`rimProp.a = 0` 整件无边缘光 | `ShadingPass.hlsl:280`、`DefaultPass.hlsl:85` | 落点：`PixelartObject.shader:305` 把 `.a` 改写 `_RimLightColor.a`，`PixelartRimLight.shader:96-100` 采样后乘 `.a`（ARGBHalf 直存，无解码） |
| 2-5 | **多盏 rim 光列表**（32 盏 StructuredBuffer，方向=组件 forward，场景里摆 Front/Back 两盏夹出双侧轮廓光） | `SloanePixelartRimlight.cs` 全文、`ShadingPass.hlsl:277-281` | 【提案】本仓版：新建 `PixelartRimLight` MonoBehaviour（方向=forward、颜色；上限 32，**越界要修**：v3 无保护）+ `ComputeBuffer(32, 24B)` + `SetGlobalBuffer`（落点 `PixelartRimLightFeature.Pass.Execute` 的 DrawRimLight 前）；shader 侧 `StructuredBuffer<RimLight>` + 循环（`#pragma target 4.5` 已满足）。**最小替代**：不接列表，把"边缘光方向"做成 rig 第二个下发全局（背光方向），可复现 Gold 的 Front/Back 双灯观感的 80% |
| 2-6 | applyAA 抑制里的蓝通道 | `ShadingPass.hlsl:93/:222`（g 判两次） | **已修**（`PixelartShading.shader:366-368`），无缺口 |

场景配置参照（怎么摆灯）：Gold 场景 Front/Back 两盏的完整参数见 §5.4；005 Rocks 用 **暖 Front（1,0.824,0.392）+ 冷蓝 Back（0,0.271,0.783）** 双色对打（`005 Rocks.unity`，解析方向见 §5.4 末）。

---

## 3. 帧级调色板

### 3.1 参照库三件套

**烘制（`$P/ComputerShaders/Palette/PaletteGenerationCIEDE.compute`）**——`[numthreads(8,8,8)]`（:134）三维派发，C# 侧必须自己 `Dispatch`（`SloanePixelartPalette.cs:62-63`，通用 `ComputeShaderPass` z 恒 1 装不下）：

- 输出布局（:139-140）：`outputCoord = uint2(z*res + x, res - y - 1)`，`inputColor = ((x+0.5)/res, (y+0.5)/res, (z+0.5)/res)` ⇒ **2D 条带：宽 = res²（横向 res 个蓝分片 × 片内 res 个红）、高 = res（绿）**；RT = `R8G8B8A8_UNorm`、宽 = 高²（`SloanePixelartPalette.cs:20-28`）；`GenerateTexture2D(16)` 读回 PNG（:33-41）。
- 色差度量（:45-124）：`rgb2lab`（:114-117）把 Lab **归一化**：`L/100`、`a/127`、`b/127` 再仿射到 [0,1]；`calculate_CIEDE2000`（:45-95，含度数版 `my_sin/my_cos` :16-17、`epsilon = 1e-5` :14）直接吃这对归一值。调色板侧同样归一（:119-124）。
- 最近邻：逐格遍历 `_Palette[i]` 取 `argmin`（:146-154），写出**调色板色原值**（:156）。
- 资产链：`SloanePixelartPalette`（ScriptableObject，`List<Color>`，`SloanePixelartPalette.cs:11-14`）→ 编辑器两按钮："Generate Texture2D" 存 PNG、"Load Colors from Hex File"（每行一个 hex，`SloanePixelartPaletteEditor.cs:32-47`）。
- **六套现成资产**（`$P/Assets/Palette/`，色数【复算】）：`TestPalette0` 46 色 / `TestPalette1` 64 / `TestPalette2` 76 / `TestPalette3` 124 / `TestPalette4` 4 / `TestPalette_5` 8；对应 LUT `Assets/Textures/ColorLUT/Test_0..5.png`，**全部 256×16**（16³ 全域映射，重烘实测与对应 asset 色 100% 重合）。
- **现行绑定**：Cast 渲染器 asset 的 ColorCorrection 特征挂 **Test_2**（= TestPalette2，76 色；`Sloane Pixelart Cast Renderer.asset:198-203`，`m_Active: 1`）——即所有示例场景（含 006 Gold）**都在过这趟 76 色映射**。`DefaultLut.mat`（`$P/Assets/Materials/`）**不是 LUT**：它是 Default.shader 的示例材质（`_BaseMap` = Test_1.png），只用在 004 UV 场景，别被名字误导。
- LUT 导入口径（Test_2.png.meta）：Point（filterMode 0）、Clamp（wrapU/V/W 0）、无 mip、sRGB 纹理标记开（`sRGBTexture: 1`）。

**上屏（`$P/Shaders/Blit/ColorCorrection.shader:29-43`）**：

```hlsl
c = tex2D(_MainTex, uv).rgb
c = LinearToSRGB(c)                        // :36 索引域是 sRGB 编码
c = clamp(c, 0.001, 0.999)                 // :37
uv_lut = float2(c.r / res + floor(c.b * res) / res, 1.0 - c.g)   // :39（u = 蓝片基址 + 片内红；v 翻转）
c = _Palette.Sample(point_clamp, uv_lut).rgb   // :40
```

`res = _Resolution = m_Palette.height`（`SloanePixelartColorCorrectionRendererFeature.cs:39-43`）。pass 目标 = 源 = Cast 相机颜色目标（就地，`SetupRenderPasses:31-37`），排在 Combine 之后（Cast Renderer.asset:114-123 第 9 位）。

### 3.2 【复算·一手验证】参照库的度量是"CIEDE2000 变形"，且 LUT 布局与采样公式互逆

用 Python 按 compute 原式重烘 `Test_2.png`（76 色板）全 16³ = 4096 格：

| 度量 | 与 Test_2.png 失配格数 |
|---|---|
| **按 compute 原式（归一化 Lab 喂 CIEDE2000）** | **0 / 4096** |
| 标准 CIEDE2000（真 Lab） | 1474 / 4096 |

结论：**复现必须连"归一化 Lab"一起抄**——把 Lab 除以 (100,127,127) 再算 ΔE，色相权重被整体改变，这不是实现误差而是作者的度量定义；按标准 CIEDE2000 重烘会得到一张"每格都差一点"的 LUT，观感发散且难排查。同法验证了布局互逆：`uv_lut = (r/res + floor(b*res)/res, 1-g)` 正向命中的格心色 = 板上最近色（第 3.1 节公式逐项成立）。

**sRGB 域口径**【复算】：compute 的 `rgb2lab`（:97-101）内部先做 sRGB→线性（`pow((c+0.055)/1.055, 2.4)`），即**索引域与板色域都是 sRGB 编码值**；上屏端 `LinearToSRGB`（:36）把线性合成结果抬回 sRGB 再查表——两端口径闭合。调色板 asset 里的 `Colors` 是 sRGB 域十进制（如 TestPalette2 首色 `(0.0902, 0.1255, 0.2196)` = `#172038`）。

### 3.3 本仓现状

**零**。原移植（commit `564b4a33`：`PixelartColorCorrection.shader` / `PixelartColorCorrectionFeature.cs` / `PixelartPalette.cs` / `PaletteGenerationCIEDE.compute` / `Palette.asset`）于 2026-09-29 经创始人裁决**整趟删除**（commit `d5e7aec3`；现状见 `Assets/Pixelart/README.md` §4 与渲染篇 §9 ⚠），不是留开关关着。当年默认关的直接原因：首轮实拍 LUT 映射错色（背景蓝灰被映成暗紫，r7 README §4），归因为"LUT 索引口径"未定位到位。

【复算·旧移植尸检（供重写时避坑，对象已删，读 git 历史 `564b4a33` 可复核）】旧 `PixelartPalette.cs` 烘 LUT 用 `RenderTextureFormat.ARGB32 + sRGB=false`（第 176 行注释"非 sRGB：存原值"）而 v3 用 `R8G8B8A8_UNorm` 且**贴图导入 sRGB 标记开**——在 sRGB 工程（两侧 `ProjectSettings` `m_ActiveColorSpace: 1`）里，GPU 采样 sRGB 标记纹理时硬件先做 sRGB→线性解码，`LinearToSRGB` 再编码回去等于无损；而旧移植的非 sRGB RT **不解码**，`LinearToSRGB(linear值)` 与写入的 sRGB 板值错一层 ⇒ 查表恒偏。这与"背景映成暗紫"的症状一致，是比"布局互逆"更可疑的根因：布局公式两侧逐字相同、不可能错。

### 3.4 补移植规格【提案·整体作废过一次，重立项需创始人点头】

1. **是否重立**：全局 32~64 色锁板提案已随 §3.2 作废（`docs/设计/美术.md:40` §3.2【已取消】）；本规格只备好配方，**重开 = 美术/创始人裁决**。006 Gold 的"闪"实测带 76 色映射参与（金锭读作棕橙而非金色，见 §5.5），若目标是"原样复现 006"，调色板是配方的一部分。
2. **烘制端**（编辑器侧一次性）：新建 `Pixelart/Palette/PaletteGenerationCIEDE.compute`（**逐行照抄** `$P` 版含归一化 Lab 与 epsilon）+ `PixelartPalette`（ScriptableObject `List<Color>` + hex 导入 + "烘 LUT 存 PNG"按钮）。派发自算 `ceil(res/8)³`（三维）。产物入 `Assets/Art/Textures/ColorLUT/`，**导入参数**：Point/Clamp/无 mip/不压缩/`sRGBTexture: true`（照 Test_2.png.meta）。
3. **运行端**：Cast 渲染器追加 `PixelartColorCorrectionFeature`（排在 CombineFeature 之后、TransparentFeature 之前——透明趟画在最终色上，调色板应在透明**之前**跑才不会映射效果件颜色，或之后跑接受效果件同映射，**归裁决**）；就地 blit 需临时 RT 中转（v3 同为就地，其 `ShaderBlitPass` 有 DuplicateCase 分支，`ShaderBlitPass.cs:62-75`；本仓旧移植用 `Blitter.BlitTexture` 中转，照抄旧法即可）。
4. **采样公式**（shader 全文，逐字）：

```hlsl
float res = _PaletteLut.height;                    // 或 GetDimensions（旧移植做法，契约不签新全局时用）
float3 c = SAMPLE(_BlitTexture, uv).rgb;
c = LinearToSRGB(c);
c = clamp(c, 0.001, 0.999);
float2 lutUV = float2(c.r / res + floor(c.b * res) / res, 1.0 - c.g);
return float4(SAMPLE(_PaletteLut, lutUV).rgb, 1);
```

   `_Resolution` 全局：v3 用独立 int（`ShaderPropertyStorage.cs:25`）；本仓走 `GetDimensions`（旧移植先例）或进契约表，二选一，不要绕 `_ScreenParams`。
5. **配色纪律**：本仓现行色彩口径 = 逐资产源槽位色表（`tools/blender/scene/style_tokens.py` SLOTS，r16 裁决）；调色板趟若重立，与槽位色表的关系（替代/叠加）须一并裁决。
6. **验收判据**【提案】：整帧色数 ≤ 板色数（无损 PNG 直方图）；`sRGB(LUT[查找(渲染像素)])` 与实际像素逐位一致（防索引域错层复发）；板色 hex 与 `List<Color>` 一致。

---

## 4. 连通域三阶段：参数差异核对（已移植，只列差）

两侧同源已由蓝本确认；本仓文件：`Assets/Pixelart/Compute/Connectivity/{Connectivity.hlsl, ConnectivityCheck.compute, ConnectivityFlood.compute, ConnectivityResult.compute}` + `PixelartConnectivityFeature.cs`。

| 参数/行为 | 参照库 | 本仓 | 定性 |
|---|---|---|---|
| `_Threshold` | 0.25（Cast Renderer.asset:180；C# 默认 0.5 与 asset 不符，asset 赢） | 0.25（`PixelartConnectivityFeature.cs:56`） | 一致 |
| `m_IterationScale` / Flood 次数 | 1.5 × `DownSamplingScale`(5) = **8**（`...ConnectivityMapGenerationRendererFeature.cs:65-66`） | 1.5 × `pixelScale`(2) = **3**（`:59/:165`） | 公式同源，随 k 缩放；**注意 `RoundToInt` 是银行家舍入**：k=4 时 `RoundToInt(6.0)=6`、k=3→4（4×1.5=6.0 精确无歧义；2×1.5=3.0 同），现役档无数值歧义 |
| 降档幅度 | `ndotl -= applyAA * singleLevel`（降 1 档，`ShadingPass.hlsl:53`） | `-= applyAA * singleLevel * 2.0`（降 2 档，`PixelartShading.shader:213-214`） | **刻意差异**（创始人 2026-09-30），保留 |
| `_ConnectivityAntialiasingThreshold` | `m_AAScaler/2 = 0.75`（asset 1.5，`SloanePixelartShadingRendererFeature.cs:19` 脚本默认 1.75 未生效） | `aaScaler/2 = 0.75`（`PixelartCameraRig.cs:142/:367`） | 一致 |
| Flood 传播窗 | `halfScale = k/2 = 2`，窗 = `cellCenter±2` = **恰好整块**（`ConnectivityFlood.compute:14-16/:59-60`） | `halfScale = k/2 = 1`，窗 = `cellCenter±1` = **跨 3 个细像素 = 1.5 块**（`ConnectivityFlood.compute:45-46/:99-100`） | **偶数 k 不匹配**：k=2 时块宽 2、锚在偏 (1,1)，±1 窗越进邻块半格——与渲染篇 §8 静默失效点 9 同族（Result 已按块原点修，`ConnectivityResult.compute:53-60`；**Flood 未同步修**）。修法【提案】：窗改为 `target ∈ [blockOrigin, blockOrigin+k)`（按块原点锚），落点 `PixelartConnectivityFlood.compute` 的 `PixelartConnectivityFlood` 与 `Main` |
| Result 聚合窗 | `center ± halfScale`（奇数 k 恰为块；`ConnectivityResult.compute:36-38`） | 块原点 `[0,k)²` 显式遍历 + clamp（`ConnectivityResult.compute:60/:74/:78`） | 本仓已修（偶数 k 正确），**不要改回 v3 写法** |
| 判据本体 | `Connectivity.hlsl:42-104`：两端法线各自预测深度残差、双端超阈才断（:100）；空邻接门（:98-99）；`CheckNormalContinuous` 的 z 门 = **视图法线 z < 0** 才计（:21/:32） | `Connectivity.hlsl:190-221/:228-312`：同式；z 门 = **视图法线 z > 0**（:204/:213）——坐标系语义差（本仓深度/法线口径自洽），非缺陷；跨步走查 `pixelSpacing = -ViewStep(next-center)`（:260）与 v3 的视图空间逐步重建等价（本仓以 `_PixelartFineUnitSize` 直算步长，免视图重建） | 已按本仓坐标系一致化，留痕在文件头 |
| 深度线性化 | `LINEAR_DEPTH`：透视 `LinearEyeDepth` / 正交 `lerp(near,far,raw)`（`Connectivity.hlsl:5`） | 正交 `LinearDepthToEyeDepth`（URP 自带 reversed-Z 分支；`Connectivity.hlsl:153-159`） | 等价（reversed-Z 处理更直接） |
| 打包 | `PackFloatInt8bit(0, bits, 256)` 进 SNorm16（`ConnectivityCheck.compute:33-42`） | `bits/255` 直存 UNorm8 + `round(a*255)` 解码（`Connectivity.hlsl:167/:176`） | 数值逐位一致（蓝本已证），刻意差异，保留 |
| 位序 | bit7..4 connected 右/左/上/下，bit3..0 closer（`BuffersResolver.hlsl:23-31`、`ConnectivityResult.compute:85`） | 同序（`ConnectivityResult.compute:109-113`；契约 §7 #2 冻结） | 一致 |

---

## 5. 006 Gold 场景：完整配方（"闪闪发光"逐项清单）

> 全部数值直接读自 `$P/Scenes/006 Gold.unity`（fileID 见各处）与被引材质。方向量【复算】= 由序列化四元数算出（相机 16° 俯角，fwd=(0,−0.276,0.961)）。

### 5.1 相机与分辨率

| 项 | 值 | 出处 |
|---|---|---|
| 主相机 clearFlags / cullingMask / far | Nothing(4) / 0 / **0.02** | `006 Gold.unity:240/:268-270/:263` |
| 主相机背景色（RGBA） | `(0.1921569, 0.3019608, 0.4745098, 0)` ≈ sRGB `#314D79`（被 ClearFlags=Nothing 屏蔽，实际无效值） | `:241` |
| 主相机位姿 | pos `(0, 2.4, -8)`、欧拉 **(16, 0, 0)**（非 30° 俯角——演示场景，勿当玩法口径） | `:290-291/:297` |
| `SloanePixelartCamera` | `m_TargetResolution = (160, 160)`（**方屏**）、`m_DownSamplingScale = 5` ⇒ 超采样 800×800、画布 **160×160**、`unitSize = ortho×2/160` | `:157-158`、`SloanePixelartCamera.cs:47` |
| Cast 相机 | 主相机子物体、**local pos (0,0,−9.92)**（不重合，沿 fwd 抬了 9.92m）、正交、`orthographic size = 4`、`depth = −64`、renderer 索引 2（Cast）、cullingMask 全开、bg `(0.10377, 0.06510, 0.06949, 1)`（线性值直写 ⇒ 显示域 ≈ (91,72,75) 暗紫灰【复算】） | `:498-607`、`SloanePixelartCamera.cs:290-296` |
| 每艺术像素 | **0.05 m/px**（unitSize = 2×4/160）；金锭投影高 ≈ 0.44 m ≈ **9 艺术像素** | 【复算】 |
| ResultBuffer | HDR 格式、Point、320 上限不适用（此处 160×160） | `SloanePixelartCamera.cs:261-274` |

### 5.2 材质（`Default.shader` = `Sloane/Pixelart/Default`，guid 159f54a0…）

| 属性 | **Gold.mat**（左锭） | **Silver.mat**（右锭，任务称"铁锭"实为银） | 出处（两文件同构） |
|---|---|---|---|
| `_BaseColor` | `(1, 0.8441, 0.4953)` sRGB ≈ `#FFD87E` | `(0.8538, 1, 0.9846)` ≈ `#DAFFFB` | Gold.mat:130 / Silver.mat:130 |
| `_Smoothness` | **1** | **1** | :121 |
| `_Metallic` | **0.8** | **0.8** | :113 |
| `_MainLightLevel`（色带档数） | 4 | 4 | :112 |
| `_EdgeLevel`（法线边加成） | **+1**（**提亮**，不是本仓默认的 −1 压暗；÷128×0.5+0.5 编码后 = paletteProp.b，解包 `(b*2−1)*128`） | +1 | :106、`DefaultPass.hlsl:73`、`ShadingPass.hlsl:94` |
| `_NormalEdgeThreshold` | 0.9 | 0.8 | :115 |
| `_AAScale` | 1（属性默认） | 1 | Default.shader:18 |
| `_DiffuseDitherStrength` | **0.038**（轻抖动） | 0.038 | :103 |
| `_DiffuseDitherPalette` | **dither_6.png（16×16 密度图案）**，tiling **(16,16)** | 同 | :46-49 |
| `_Priority` | 1 | 1 | :118 |
| `_RimLightColor` | `(0.5843, 0.5773, 0.4118, 1)` ≈ `#959369` 暗金灰 | 同 | :133 |
| `_ApplyOutline` | 默认 1 | 默认 1 | Default.shader:20 |
| instancing | 开 | 关 | Gold.mat:17 |
| （无消费残留） | `_SpecularHighlights: 1`、`_SpecColor (0.2,0.2,0.2)`、`_EnvironmentReflections/_GlossyReflections` | 同 | :110-134——URP Lit 模板残留，`$P` 源码零消费 |

**"闪"的构成**【复算】：`expSmoothness = exp2(5×1+1) = 64` ⇒ 两档量化的亮档要求 N·H ≥ 0.989（半角锥 **±8.4°**）——金属平面上一条极窄的量化高光带；乘 `specular = 0.2+0.8×albedo`（线性 ≈ (1, 0.745, 0.368)）；金锭匀速自转（SpinDisplay，§5.6）让 N·H 周期性扫过 ±8.4° 锥 ⇒ 高光带周期性翻档 = **闪烁**；GI 趟（smoothness=1 ⇒ `exp2(11)` 反射、`grazingTerm = 1+reflectivity`）提供第二层 fresnel 边缘亮。**没有调色板时 006 的"闪"已经存在**（量化高光 + 自转）；调色板把白色高光带**重新映射成板上最近色**（76 色板的高亮段），改变的是闪的"色相"不是"有无"。

### 5.3 光照

| 灯 | 值 | 出处 |
|---|---|---|
| Directional Light | 色 `(1, 0.9829, 0.8821)`、强度 **1**、**软阴影开**（m_Shadows.m_Type=1；软阴影实际由 URP 资产 `m_SoftShadowsSupported: 0` 关闭 ⇒ 生效为硬阴影）、欧拉 **(50.302, 75.713, 60.778)**（指向光源方向【复算】世界系 ≈ (−0.619, 0.769, −0.158)，屏幕系 ≈ (−0.66, 0.75)）、`m_UsePipelineSettings: 0`（绕开管线资产默认） | `006 Gold.unity:757-856` |
| Point Light（顶） | 色 `(1, 0.9232, 0)` 暖黄、强度 **2.79**、Range 10、pos **(−0.14, 2.76, 0)**（正上方）、无阴影 | `:1112-1229` |
| Point Light (1)（右下） | 色 `(1, 0.7837, 0)` 橙、强度 **3.21**、Range 10、pos **(2.76, −1.68, −1.451)**、无阴影 | `:621-738` |
| Point Light (2)（左下） | 色 `(0.9151, 0.7029, 0.5914)` 粉橙、强度 **3.21**、Range 10、pos **(−2.73, −1.68, −1.451)**、无阴影 | `:994-1111` |
| 环境光 | sky `(0.1969, 0.2241, 0.2358)`、equator `(0.114, 0.125, 0.133)`、ground `(0.047, 0.043, 0.035)`、`AmbientMode: 0`（天空盒三阶） | `:23-25/:27` |
| 天空盒 | Unity 默认程序化天空盒（fileID 10304 内建） | `:29` |
| 反射探针 | **场景内无**；GI 反射走 `m_DefaultReflectionMode 0`（天空盒反射，默认 128 分辨率） | `:35-41` |

### 5.4 边缘光（两盏 `SloanePixelartRimlight` 组件）

| 灯 | 色 | 位姿 | 方向【复算】 |
|---|---|---|---|
| **FrontRimLight** | `(1, 1, 1)` 白 | pos (0, 5, 0)、欧拉 (0, −92.155, 0) | 世界 forward ≈ (−0.999, 0, −0.038)；视图空间 xy ≈ (−1.0, −0.01) ⇒ **屏幕正左** |
| **BackRimLight** | `(1, 1, 1)` 白 | pos (0, 5, 0)、欧拉 (35.362, −223.959, 22.381) | 世界 forward ≈ (0.566, −0.579, −0.587)；视图空间 xy ≈ (0.62, −0.79) ⇒ **屏幕右下** |

出处：`006 Gold.unity:320-364`（Front）、`:857-901`（Back）。物体侧 `_RimLightColor`（暗金灰 §5.2）× 白灯 × 门控（`connected|!closer` 四方向 × 屏幕光向 × 两档量化 × ndotl）= 金锭轮廓的**左缘 + 右下缘双窄亮边**。005 Rocks 的变式：Front `(1, 0.824, 0.392)` 暖 / Back `(0, 0.271, 0.783)` 冷蓝（同位置同角度）——双色对打。

### 5.5 调色板与背景

- 调色板趟在**渲染器资产**上（§3.1），场景无需配置；006 实际过 **Test_2（76 色）** 映射。实测截图（`external/sloane-captures/gold-1.png`，1080p）里金色读作棕橙、高光带读作奶黄——即 LUT 重映射后的成品。
- 有效背景 = Cast 相机 bg（暗紫灰，§5.1）**经调色板趟映射后**的色。
- 无后处理（`m_RenderPostProcessing: 0`，`:521`）、无 AA（`:523`）。

### 5.6 动画与杂项

- 两根金锭都是 `GoldIngot.fbx`（guid 6f373f2a…）prefab 实例，**scale 1.6**；左：pos (1.06, −0.160, 0.084)、欧拉 (−35.954, 26.41, 0)；右：pos (−0.925, −0.154, 0.084)、欧拉 (−37.797, 23.435, 4.954)（`006 Gold.unity:366-456/:902-993`）。FBX 法线导入 = Compute（`normalImportMode: 0` + `normalCalculationMode: 4`）、tangent 导入开（`GoldIngot.fbx.meta:74-76`）。
- 两根各挂 `SpinDisplay`（`Assets/Scripts/SpinDisplay.cs:24-52`）：`rotationSpeed 1`、`m_ApplyRange 0`（方块波模式：每 0.08333 s 步进一次，`interval` 字段；角度按 `cos` 方波扫 0→360°）——**周期性整周自转**，这是高光带周期性翻档（闪）的直接驱动。脚本字段名 `rotationSpeed: 1` 在场景里两处一致（`:316-319/:475-478`）。
- 主相机 far 0.02 + cullingMask 0（纯上屏器）；AudioListener 在主相机上（无关）。

### 5.7 002 Lighting / 005 Rocks 简述

- **002 Lighting**（`$P/Scenes/002 Lighting.unity`，926 行）：分辨率 **120×80 / down 5**（画布 120×80）；Cast 相机 ortho size **2**（unitSize 仍 0.05 m/px，可视 4×2.67 m）；主光强度 **1.2**、**软阴影开**、欧拉 (60, −42.8, −33.4)；三件演示体：Plane 64×64 地面（DefaulBlue：`_Smoothness 0.5`、`_EdgeLevel −1`、`_MainLightLevel 4`、rim 色 `(1,1,1,0)` a=0 ⇒ 无边缘光）、Cube 0.5³（DefaultRed：`_Smoothness 0`、`_Metallic 0`——非金属对照）、Sphere（DefaultGreen：`_Smoothness 0.443`、`_MainLightLevel 3`）。Group 带慢速 SpinDisplay（rotationSpeed 10）。**无 rim 光组件**。价值 = 同一 `unitSize` 下三档光滑度/档数的对照矩阵。
- **005 Rocks**（`$P/Scenes/005 Rocks.unity`）：分辨率 **240×160 / down 5**；Directional 与 006 同角度同强度、**硬阴影**；暖 Front + 冷蓝 Back rim 双灯（§5.4 末）；地形 Plane 128×128（RockGround：`_Smoothness 0.05`、`_Metallic 0.2`、`_NormalEdgeThreshold 0.1`——**低阈值让地面法线噪动出内线**）；岩石件全用 Rock.mat（`_Smoothness 0.05`、`_Metallic 0.2`、`_NormalEdgeThreshold 0.82`、`_DiffuseDitherStrength 0.008`、dither_0 4×4 tiling(4,4)、`_ApplyOutline 1`、rim 色 (0.584,0.577,0.412,1)）；整场绕 Y 慢转（SpinDisplay rotationSpeed 1、range 10、interval 0.111）。价值 = **连通域内线 + 描边 + 硬阴影**的组合参照（石阶暗线/柱面亮边即蓝本 §3.1 所指场景）。

---

## B. 2:1（pixelScale = 2）适配换算

本仓口径：画布 960×540@1080p（`pixelScale = 2`，`PixelartCameraRig.cs:66`、`PixelScaleStore.cs:39`；取景恒定 wpp = 0.0416667 m/px、ortho 11.25，`CameraFraming.cs:78/:85`）；屏幕档（细）= 1920×1080。参照库默认 320×180 / down 5（细 1600×900），006 甚至 160×160 / down 5。

### 6.1 不随分辨率变（照抄即可）

| 项 | 理由 |
|---|---|
| 材质全套（§5.2） | `_Smoothness/_Metallic/_MainLightLevel/_EdgeLevel/_NormalEdgeThreshold/_DitherStrength/_RimLightColor` 都是无量纲或颜色，与像素比无关 |
| `_Threshold = 0.25` 初值 | 单位是**视图空间米**（深度残差），世界量；但见 6.2 第 3 条的重标提示 |
| `_ConnectivityAntialiasingThreshold = 0.75`、降档 2 档 | connect 是 0..1 比例；本仓已在此值下验收 |
| LUT 烘焙与采样公式（§3） | 分辨率无关（16³ 固定域） |
| RimLightCorrection 3×3、墨线 1 艺术像素、applyAA 门控 | 都以"艺术像素"为单位，两侧定义一致 |
| 相机矩阵 snap、物体原点吸附 | 公式 `unitSize = ortho×2/画布高` 两侧逐字一致，随本仓画布自动换算 |

### 6.2 必须重标/换算的项

| # | 项 | 换算 | 依据 |
|---|---|---|---|
| 1 | **景别与颗粒二选一**（006 框景迁入） | 画布高比 = 540/160 = **3.375**。同一物体要同时保住"占画面比例"和"占艺术像素数"必须把世界缩 3.375×（含灯位/Range，URP 距离衰减是世界系）：正交 4→**1.185**、灯位/Range ×0.296、金锭 scale 1.6 不动（相对大小不变、仍是 ~9 艺术像素高，unitSize = 2×1.185/540 = 0.00439 m/px）。**替代**：不缩世界、正交 4 保持 ⇒ 金锭 ~30 艺术像素高（观感细 3.4×），灯参数不动。**推荐前者**（复现观感）【提案】 | §5.1；`wpp` 口径见渲染篇 §3.1 |
| 2 | Flood 迭代次数 | `max(1, RoundToInt(k×1.5))`：k=2 ⇒ **3**（已实现，`PixelartConnectivityFeature.cs:165`） | §4 |
| 3 | Flood 传播窗（k=2 唯一**必须修**的算法项） | `halfScale=±1` 窗跨 1.5 块 ⇒ 改按块原点 `[0,k)²` 界定 target | §4；渲染篇 §8 失效点 9 |
| 4 | `_Threshold` 重标（第 2 轮） | 细像素间距 = unitSize/k：参照 0.01 m（0.05/5）vs 本仓 0.0208 m（0.0417/2）——单步残差比例放大 ~2×，同一阈值对斜边的容忍度变松。初值 0.25 起步，对"台阶暗线出现/消失"做 A/B 后定值；**不得**用改地形掩盖 | §4；AGENTS 对账纪律 |
| 5 | 降档阈值语义 | connect ∈ {0, .25, .5, .75, 1}（k=2 只有 5 档）vs k=5 的 21 档；`< 0.75` 实际 = "连通格 ≤ 50%"。现役 0.75 已在 k=2 下验收，**保持**；迁 v3 演示参数时不要把它当连续量微调 | 【复算】 |
| 6 | 抖动坐标系 | 保持本仓"贴屏幕 + 块对齐"（`floor(positionCS.xy / pixelScale)`，`PixelartObject.shader:226-229`）；v3 的 `positionSSO` 贴物体口径会违反渲染篇红线 4，**不迁**。图案本身已由 DitherPatternBaker 复刻（dither_6 同款 16×16 在册） | 渲染篇 §6 抖动两范式 |
| 7 | 线宽/亮块的屏幕观感 | 1 艺术像素 = 本仓 **2 屏幕像素**@1080p vs 参照库 6 屏幕像素（320×180 满屏）——墨线与量化高光带在同等物理窗距下细 3×。要复现 006 观感靠 §6.2#1 的世界缩比（艺术像素数不变），不要加宽线宽 | 【复算】 |
| 8 | 背景色写入域 | 本仓 ResultBuffer = **ARGB32 非 HDR**、背景 = 主相机 bg 色裸写线性 RT，须 `.linear` 预补偿（渲染篇 §6）；v3 ResultBuffer 是 HDR 格式且 bg 由 Cast 相机 ClearFlags=Solid 写入。迁 006 配方时 bg `(0.10377,0.06510,0.06949)` 按**线性直写**对待（它就是线性值），映射后才是最终观感 | 渲染篇 §6；§5.1 |
| 9 | 附加光条数 | 006 有 3 盏点光：本路径条数 = `renderingData.lightData.additionalLightsCount` 钳到 **8**（`PixelartBeforeRenderFeature.cs:32/:75-82`），足够；该计数**不受** URP 资产 `m_AdditionalLightsPerObjectLimit: 0` 钳制（`UniversalRenderPipeline.cs:1501-1504`：逐物体上限只进 `maxPerObjectAdditionalLightsCount`），`GetAdditionalPerObjectLight(i)` 按全局数组下标取光（`RealtimeLights.hlsl:153-161`）。⇒ **迁入无阻塞**；不要改用 URP 的 `GetAdditionalLightsCount()`（`RealtimeLights.hlsl:296-306`，它取 `min(总数, unity_LightData.y)`，在上限 0 时恒 0） | 【复算】§7 第 1 条 |
| 10 | 主光阴影 | 006 开阴影（§5.3）；本仓机制已接、试点 `sun.shadows = None`（渲染篇 §10）。迁 006 配方时按 r16 经验：`shadowNormalBias ≈ 0.65` + 大平面只承接不投影 | r16 README |

---

## 7. 附带发现（核对过程中的工程事实，与本规格直接相关）

1. **安装器枚举注释与 URP 14 相反**：`PixelartPathInstaller.cs:402` 注释 "0=Disabled 1=PerVertex 2=PerPixel"，URP 14.0.12 实际 `PerPixel = 1`、`PerVertex = 2`（`UniversalRenderPipelineAsset.cs:236-256`）；安装器写入 2（`:421`）= **PerVertex**，两档 PC URP 资产现值即 2（`Assets/Settings/URP/PC_*_URPAsset.asset:42`）。对本路径的实际影响【复算】：附加光常量在逐顶点/逐像素两种模式下**都会上传**（`ForwardLights.cs:492-500` 无条件调 `SetupAdditionalLightConstants`），本仓 shader 双关键字兜住（`PixelartShading.shader:302-309`）⇒ 附加光照常生效；PerVertex 只关掉附加光阴影图（`UniversalRenderPipeline.cs:1465`），而本路径附加光走 `GetAdditionalPerObjectLight` 本就不投影（`PixelartShading.shader:521-522`）⇒ **今天零可见影响**。但 `PixelartShading.shader:306-308` 那句"逐顶点模式下 `_AdditionalLightsPosition` 是空的 ⇒ 贡献为 0"与 URP 实际行为不符，属注释口径错。同组的 `m_AdditionalLightsPerObjectLimit: 0`（同文件 :43）只影响走 `GetAdditionalLightsCount()` 的标准 URP shader，本路径不受影响（§6.2#9）。
2. **v3 的 ReleaseBuffers 泄漏、PriorityManagement 死 pass、Debug/Outline 关闭**——蓝本 §4 已结案，本规格无新增。
3. **`SpinDisplay.interval` 与"闪"的节拍**：0.08333 s/步 ≈ 12 步/s 的方波自转；对照实验时先锁帧再对拍，避免把步进感误判为高光抖动。
4. **本仓 `PixelartObject.shader` 无 ShadowCaster pass**（静态事实）：全文件单 pass（`:92-309`）且 `Fallback Off`（`:312`）；像素场景装配器有"异物 shader"审计，保证场内 renderer 全是本路径物体 shader（`PixelartStageKit.cs:236-248`）。URP 主光阴影图只收 `LightMode = ShadowCaster` 的 pass；v3 有专门 pass，且先做与可见几何同款的原点吸附、再把视图空间位置带回世界系投影（`Default.shader:101-124`、`ShadowPass.hlsl:21-60`）。**推论（待实机归因）**：本路径物体不进阴影图，`shadowAttenuation` 恒为 1；r7"地面整片被判在阴影里"、r16"冷影/normalBias 0.65 修自遮挡"两条观察与此推论冲突（`shadowNormalBias` 只在 ShadowCaster pass 里生效），须用 Frame Debugger 看 MainLightShadow 的 DrawShadows 实际 caster 数后再定性，**本规格不据此改判既有结论**。补移植规格【提案】：给 `PixelartObject.shader` 补 ShadowCaster pass，移植 `ShadowPass.hlsl:21-60` 的结构，原点吸附与可见 pass 共用 `PixelartOriginSnap`（`PixelartObject.shader:190-200`，含 x 轴 −1/4 相位），否则影子与形体差半格；`_SPRITE` 剪影件同样 `clip`。这是复现 006 金锭落地影（主光硬阴影，§5.3）的前提。
5. **安装器两处口径**（第 1 条）合并为低优先级修正：`PixelartPathInstaller.EnsureUrpLightingSettings` 写入值改 1（PerPixel）并改正注释；`m_AdditionalLightsPerObjectLimit` 补写 4（对齐 v3 资产 `Sloane Pixelart Render Pipeline.asset:42`）只为让标准 URP shader 与本路径同口径，本路径画面不变。
6. 006 的"金色"在过 76 色板后读作棕橙（§5.5）；若创始人验收时以 `gold-1.png` 为观感锚，则 **Gold.mat 原色不等于最终色**，调色板是配方的必要环节，不能"先复现无板版再对照"。

## 8. 未覆盖区（如实登记）

- `$P/Scenes/001/003/004/007/008.unity` 未逐参数核对（理由见文档头）。
- `Leaves.shader`/`LeavesPass.hlsl`（树叶几何壳双面 + 顶点摆动）、`OutlinePass.hlsl`/`Outline.shader`（v3 关闭态描边）未入规格——前者本仓无树叶资产需求，后者本仓描边已按改造版现役（渲染篇 §6.1）。
- `SloanePixelartObject.cs` 的 `_SnapOffset` 逐物体 snap 矩阵（C# 侧）：蓝本 §1.4 已定性（与顶点着色器层重叠、取一层），本仓取顶点层（`PixelartObject.shader:190-199`），C# 层不移植，本规格未重复核对。
- 音频/审计类组件（AudioListener、NavMesh、LightmapSettings）与观感无关，未抄。
