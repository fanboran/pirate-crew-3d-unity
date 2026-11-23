# CellShade 复刻保真审计

> 审计基准：本仓 commit `eb806c0c`（2026-11-07 提交，审计日 2026-11-09）；参照库 `external/sloane-urp`（Sloane Pixelart Render Pipeline，MIT）。方法：源码逐项对账（shader / compute / C# / 序列化资产 / 场景 YAML 全部一手回读）。工作区在待审文件上另有约 357 行未提交改动，本报告一律以 commit 版本为准；盘上存在但不在 commit 内的文件（`PixelartColorCorrection.shader` / `PixelartColorCorrectionFeature.cs` / `PostLine*` 系列）按「缺失（在途）」登记。未覆盖：运行时像素级比图（需 Unity 实跑）。

## 结论摘要

管线骨架（两档分辨率域、Cast/主相机分工、7 张 G-buffer + 深度、连通域三段、屏幕空间 4 邻域描边、边缘光趟、四趟着色 + 合成、点采样上屏）与参照库**逐段同构**，连通域判据本体（Check/Flood/Result 位布局、平面预测残差、越界语义、三处 v3 细节）为逐行翻译且差异全部有源码内注释留痕，属高保真移植。真正的缺口集中在**「数据喂入」与「色彩收口」两端**：①调色板/颜色校正整条链（ColorCorrection shader/feature、CIEDE2000 烘 LUT）在基准 commit 中不存在；②全仓 134 个像素材质 `_Smoothness`/`_Metallic` 恒 0、`_RimLightColor` 恒黑，金属高光、GI 金属反射、边缘光三条光照通道**管线在、数据无**；③边缘光刻意只接了「屏幕空间光向 × 连通域门控」一段，参照的 ndotl 门控、两档量化、金属 tint、32 盏独立 rim 光列表都没有载体。另有数处**已登记的刻意偏差**（抖动施加量纲 0.5/level、连通域降档 ×2、GI 简化为环境光项、锁 pixelScale 反推分辨率）——它们是裁决产物，本报告只登记、不计为失真。

严重度统计：**高危 3**（调色板缺失、材质零喂入、边缘光链缺芯）；**中危 4**（GI/反射简化、lightmap 通道缺失、PriorityManagement 未移植、描边头注与参照资产不符）；**低危/刻意偏差 6**（见对账表判定列）。

## 逐项对账表

判定口径：一致 = 数学/结构与参照逐行同源；偏差 = 刻意且留痕的改动；缺失 = 参照有、本仓无载体。

| 项 | 参照实现与位置 | 本仓实现与位置 | 判定 | 影响与建议 |
| --- | --- | --- | --- | --- |
| **1. 渲染结构：G-buffer** | 15 张命名缓冲（`Scripts/GlobalInfo/OutputBuffer.cs:8-26`）；物体阶段写 **8 MRT**（Albedo/Normal0/Normal1/Physical/Shape/Palette/RimLightProperty/**LightmapUV**，`Shaders/Includes/Inputs/Buffers.hlsl:1-8` + `Passes/DefaultPass.hlsl:42-87`），格式 R16G16B16A16_SNorm(sRGB) + `PackFloatInt8bit` 字节打包（`Scripts/MonoBehaviour/SloanePixelartCamera.cs:171-188`） | 7 MRT（`PixelartObjectFeature.cs:34,59-65`，`GbufferCount=7`）+ 可采样深度附件；**无 LightmapUV**；格式 ARGB32/ARGBHalf **直存**、显式关 sRGB（`PixelartCameraRig.cs:803-810`） | 偏差（直存替代打包，留痕 `PixelartShading.shader:29-37`）；缺失（LightmapUV） | 直存换格式是防「SNorm16 打包耦合」的有据裁决；LightmapUV 缺失意味着烘了光照图也吃不到（见高危旁的中危 5） |
| **1a. 屏幕档 Patch 副本** | 无此物 | 8 张 BlockPatch 副本 + `BlockPatch.compute`（`PixelartCameraRig.cs:815-822`、`Pixelart/Compute/BlockPatch/BlockPatch.compute`） | 本仓独有（补偶数档块锚落空） | 见第 3 项连通域；对参照是超集 |
| **1b. Cast/主相机分工** | Cast 相机 = 主相机子物体、正交、`depth=-64`、`targetTexture=ResultBuffer`（`SloanePixelartCamera.cs:279-297`）；主相机 cullingMask=0、clearFlags=Nothing、far=0.02 空转（`:104-119`） | 同构：子物体 local 恒等、正交、depth-1、`targetTexture=ResultBuffer`、HDR/MSAA/后处理全关（`PixelartCameraRig.cs:636-691`）；主相机 cullingMask=保活层（Cinemachine 兼容，注释有 v3 实测教训）；**多一台 Overlay 栈内相机画透明队列**（`:705-755`） | 一致 + 本仓扩展（透明件） | 参照无半透明方案，本仓 Overlay 相机颗粒是全分辨率且不判遮挡（已登记待办），不破坏对等性 |
| **1c. pass 顺序** | Cast 渲染器 feature 顺序（`Sloane Pixelart Cast Renderer.asset`）：BeforeRender → RenderObject(8 MRT) → Connectivity(Check→Flood×8→Result) → PriorityManagement(UV) → Shading(BeforeShading 清四缓冲/改写 `_ScreenParams` → RimLight → RimLightCorrection compute → Diffuse → Specular → GI) → Combine → **Outline（合成后上色）** → ColorCorrection；上屏 = Main 渲染器 CopyTarget | 装配链：BeforeRender(视图 snap+全局) → Object(7 MRT) → Connectivity(BlockPatch→Check→Flood×3→Result) → **Outline（只写标记，先于着色）+ 缝闭合第二趟** → RimLight(清→趟→去孤立点) → Shading(三趟各写缓冲→Combine 写相机颜色目标) → 主相机 ScreenCopyFeature(CoreBlit Nearest 上屏)（`PixelartPath.cs` 契约 §3、各 Feature `renderPassEvent=AfterRendering` 按数组序） | 偏差（描边先于着色、以标记传墨色）+ 缺失（ColorCorrection 一环） | 描边前置使墨线不吃光照/色带（本仓口径，墨色 #0D0D0F）；参照是合成后按 applyOutline 上灰/混色。两观感不同源，恢复参照行为需重组 pass 序，不建议 |
| **1d. 降采样方式与档位** | 固定 TargetResolution(006=160×160) × DownSamplingScale(=5) → 缓冲 800×800；着色域 160×160（`SloanePixelartCamera.cs:22-24,40-42`；006 场景 `:157-158`） | 锁 pixelScale(2..5) 整数倍从实际屏幕反推：艺术画布=屏幕/k、屏幕档=画布×k（`PixelartCameraRig.cs:608-634`）；k 运行时可换档（滚轮/设置页，取景不变） | 偏差（有据：v3 非 16:9 拉伸缺陷 + 「像素不齐」事故） | 两域「块中心=画布像素中心」的不变量两边都成立（`id*k+k/2`），点采样降采语义等价 |
| **1e. 上屏合成** | Combine：diffuse+specular+GI+rimLight 写相机颜色目标，`clip(diffuse.a-1)` 保背景（`ShadingPass.hlsl:232-244`）→ Outline/ColorCorrection 原地改写 → CopyTarget 上屏 | Combine 同式同 clip（`PixelartShading.shader:606-624`）→ ScreenCopyFeature CoreBlit Nearest（`PixelartScreenCopyFeature.cs:55-57`） | 一致 | 无 |
| **2. 着色：Diffuse** | `DiffuseShading`（`ShadingPass.hlsl:37-63`）：saturate(ndotl·衰减·阴影)→pow(1/2.2)→+singleLevel·offset→multiStep(level)→连通域降 1 档→法线边→clamp→lerp(transmission)；附加光 level=3、edgeLevel×0.5 | `PixelartShading.shader:182-226` 逐行同式。差异 3 处：抖动施加 `offset*(0.5/level)`（`:205`，量纲修正，r19/r24 事故）；连通域降 **2** 档（`:214`，创始人 2026-09-30）；主光 `distanceAttenuation` 强制 1（`:388`，全屏 DrawProcedural 下 `unity_LightData.z` 未定义的静默失效防护） | 一致（数学同源）+ 3 处刻意偏差（均有注释与事故编号） | 对图时色带边界与内线深度会系统性不同——这是裁决，不是失真 |
| **2a. 主光来源** | URP `GetMainLight`（方向/颜色/阴影都吃场景主光） | 同用 `GetMainLight` 取阴影，但方向/颜色被 rig 下发的 `_PixelartLightDirWS`/`_PixelartLightColor` 覆盖（`PixelartShading.shader:395-397`；`PixelartCameraRig.PushLightGlobals:1005-1032`，`lightIntensity` 旋钮） | 偏差（有据：美术旋钮 + 判据脚本对色号） | 无 |
| **2b. Specular** | `SpecularShading`（`ShadingPass.hlsl:117-166`）：exp2(5s+1)、specular=lerp(white,albedo,metallic)、multiStep(level=2)、×smoothness | `PixelartShading.shader:463-539` 逐行同式同 level=2 | 一致 | 但材质 S/M 全 0 ⇒ 全场景零高光（见材质实测） |
| **2c. GI/环境项** | `GlobalIlluminationFragment`（`ShadingPass.hlsl:168-230`）：SAMPLE_GI(lightmap/SH)+GlossyEnvironmentReflection+EnvironmentBRDF，specular=lerp(0,albedo,metallic)，NoV 两档量化且 bias 联动连通域 AA | `PixelartGI` = 环境色 × albedo（`PixelartShading.shader:551-584`）；`_PixelartAmbientColor` **有写入者**：`PixelartCameraRig.PushLightGlobals:1027`（RenderSettings ambient → .linear） | 缺失（SH/反射/Fresnel/金属反射项），有据（参照场景未烘 lightmap，观感近似单色环境光） | 金属环境反射永黑；暗部=albedo×环境色，与参照非金属观感近似。补金属反射前先补 S/M 喂入 |
| **2d. RimLight** | 光源 = 32 盏 `SloanePixelartRimlight` 组件（方向=forward，`Scripts/MonoBehaviour/SloanePixelartRimlight.cs:28-77`）经 StructuredBuffer 下发；着色 = rimProp.rgb × rimProp.a × `RimLightShading`（ndotl 门控 + 四方向 `(1-connected)`×clamp(screenLightDir) + `multiStep(modifier·ndotl, 2)` + 金属 tint specular=lerp(white,albedo,metallic)，`Shaders/Includes/RimLight.hlsl:10-25`、`ShadingPass.hlsl:246-284`）+ `RimLightCorrection.compute` 3×3 去孤立点 | 门控段逐行同式（`Pixelart/Shaders/Includes/RimLight.hlsl:78-94`，含 NaN 加固）；**ndotl/量化/金属 tint/rim 光列表四处刻意不接**（文件头 97-110 行写明「要先补契约」）；光向取主光；颜色=逐物体 `_RimLightColor.rgb`（a 恒 1，强度不可分）；去孤立点 compute 同名同构（`Pixelart/Compute/RimLightCorrection.compute`）；另加墨线/背景排除（本仓有墨线概念，参照无） | 偏差（刻意裁剪，留痕）+ 缺失（rim 光列表/金属 tint） | 即使把材质 rim 色拨亮也到不了参照的「量化两档背光带 + 金属色 rim」。接回需先登记 rim 档数全局（契约 §2.2 扩名） |
| **2e. 阴影** | URP 主光阴影，`GetShadowCoord(positionWS)`（`ShadingPass.hlsl:85-88`） | 同：`renderShadows=true`（`PixelartCameraRig.cs:674-676`），shadowCoord 同手法（`PixelartShading.shader:149-155`）；附加光不投影（同参照） | 一致 | 无 |
| **3. 连通域 Flood：窗口/边界** | Check 逐细像素四方向打包 bit7/6/5（`ConnectivityCheck.compute:20-45`）；Flood 单元内传播、`halfScale=scale/2` 半径、center 位短路（`ConnectivityFlood.compute:11-65`）；迭代 = RoundToInt(5×1.5)=8（`...RendererFeature.cs:65-66`）；Result 聚合窗 = **中心 ± halfScale**（`ConnectivityResult.compute:36-40`，奇数 k=5 恰好 5×5）；越界守卫用 `>`（多跑一行列，无害 bug）；`_Threshold=0.25`（Cast Renderer 资产） | 三 compute 逐行翻译（`Pixelart/Compute/Connectivity/`）：位布局/半径/短路/四通道序一致；迭代 = max(1, RoundToInt(k×1.5))，k=2→3（`PixelartConnectivityFeature.cs:164-165`）；**Result 窗改精确块 [0,k)²**（`ConnectivityResult.compute:53-65` 头注点名偶数 k 会采 (k+1)² 个且占比可超 1——参照靠锁 k=5 兜住）；守卫 `>`→`>=`；ping-pong 用常驻 RT；阈值默认同为 0.25 | 一致（判据本体）+ 2 处修正（偶数 k 窗口、越界守卫）+ 本仓独有 BlockPatch（锚空块补块内过半命中格） | 「偶数 k 跨块问题」参照未处理（锁奇数档回避），本仓 k=2 下以精确窗口 + BlockPatch 治理——对参照是修复而非偏离 |
| **3a. 法线差门控（Result.b）** | `CheckNormalContinuous` 用**世界**法线 z<0 判朝向（`Connectivity.hlsl:21,32`）——固定机位下凑巧成立 | 视图空间 z>0 判（`Pixelart/Compute/Connectivity/Connectivity.hlsl:190-218`，头注 ⑥ 有证伪过程：照抄会让顶面恒拿不到法线差、`_NormalEdgeLevel` 整项静默失效） | 偏差（bug 修正，留痕） | 无 |
| **3b. PriorityManagement** | 逐块按「(优先级+ε)×中心距离衰减^0.6」挑块内代表细像素，写 UV 偏移缓冲；着色读 uv 时按优先级补移（`PriorityManagement.compute:24-76`、`BuffersResolver.hlsl:7-9`、`SloanePixelartBeforeRenderRendererFeature`？实为 `...PriorityManagementRendererFeature.cs`，CenterDistanceAttenuation=0.6） | 无 UV 缓冲；以固定块锚 + BlockPatch 过半修补替代 | 缺失（语义不同：参照按物体重要性选样本，本仓按覆盖过半） | 本仓锚修补只保证「块有代表」，不保证「代表属于高优先级物体」——前景小物件被背景大平面压过时两边选样会不同。影响限于跨物体边界块 |
| **4. 描边** | `OutlinePass.hlsl:26-85`：门控（本像素 connectedToX<1 && closerThanX<1）→ 查**邻域** palette.a(applyOutline) → 命中处输出 `_OutlineColor`（SolidColor 模式=纯灰 0.2547；资产 `m_SolidColor:1`），宽度恒 1 画布像素，跑在合成之后原地改写颜色。注意：`PixelartOutline.shader:177` 称「v3 那个特征 m_Active: 0（从没跑过）」，但参照 Cast 渲染器资产上该 feature **m_Enabled: 1**（`Sloane Pixelart Cast Renderer.asset:45-53`）——注释与资产不符 | `PixelartOutline.shader` 两趟：判定趟（门控同源 + 三条出线规则：远侧/接缝/地面接触线，`closer` 定边、`connected` 只判接缝）只写标记缓冲 + 缝闭合趟（[墨][空][物体] 对向成对补墨）；墨色由着色趟输出（`_PixelartInkColor`=#0D0D0F）；另有 `_PixelartOutlineSimple` 与诊断档 8/9/10 | 偏差（规则扩展 + 标记化）+ 注释失实一处 | 本仓「近侧边无墨」缺口的结构性补法（本像素自开描边算命中）与地面分支是参照没有的扩展；头注的「v3 从没跑过」需人工核改为资产实情 |
| **5. 调色板/颜色校正** | `ColorCorrection.shader:29-43`：sRGB→clamp→`x=r/res+floor(b·res)/res, y=1-g` 采样 `_Palette`（水平条带 LUT，Resolution=palette.height）；feature 挂 Cast 渲染器、原地对 cameraColorTargetHandle（`...ColorCorrectionRendererFeature.cs:31-43`）；LUT 资产 = `Test_2.png`（256×16）；`SloanePixelartPalette.cs:18-68` + `PaletteGenerationCIEDE.compute`（CIEDE2000 全文，含 hue 项）从调色板颜色列表烘 LUT；Gold 材质另用 `DitherGrayScale` 16×16 密度图案（strength 0.038） | **基准 commit 无任何对应物**：`PixelartColorCorrection.shader` / `PixelartColorCorrectionFeature.cs` / 调色板 compute 均不在 eb806c0c（盘上为未跟踪在途文件）；抖动侧自研 ToonDither_0-8 + Bayer4 内建（`PixelartObject.shader:149-155,226-256`），幅度语义已重标定 | 缺失（整体） | 「出图即调色板」的最后一环没接上，色相/饱和收口与参照不可比。在途文件收口入 commit 是第一优先 |
| **5a. 抖动** | 屏幕域密度图案：`(g·2−1)·strength` 折 0.5 中心、着色趟 +singleLevel·offset（`DefaultPass.hlsl:70-72`） | 物体 pass 预折算（Bayer 满幅 ±0.5 间距 / 密度 ±1 间距）+ 着色趟 offset·(0.5/level)；坐标取艺术像素格（`PixelartObject.shader:226-256`、`PixelartShading.shader:196-205`） | 偏差（量纲重标定，留痕） | 两侧「strength=1」含义不同，对照参数时须换算 |
| **6. 相机/后处理** | 主相机透视参数残存但被清空（far=0.02）；cast 正交 size 4（006）；无 Volume、无 Bloom/Tonemap（场景与管线资产均无）；HDR ResultBuffer 但主相机无后处理 | 正交主相机 + `deriveOrthographicSize`（可见高=画布×wpp，默认 28m@1080p）；`volumeLayerMask=0`、`renderPostProcessing=false`、URP 资产 HDR 关；Overlay 相机同关 | 一致（都无后处理链） | 参照的「Bloom/Tonemap/Volume 设置」本身为零配置，无复刻对象 |
| **7. 上屏拉伸** | CopyTarget 直 blit，非 16:9 横向拉伸（本仓蓝图 §1.1 点名） | 锁倍数反推 + CoreBlit Nearest，残余像素 Warn 日志（`PixelartCameraRig.cs:853-872`） | 偏差（修复） | 无 |
| **8. 场景差距（006 Gold vs 战斗场景）** | 006：cast 正交 4 / 160×160 / k5；主光暖白 intensity 1；**3 盏金色点光**（intensity 3.21/3.21/2.79，色 (1,0.78,0) 等）；FrontRimLight/BackRimLight 白色 rim 组件各一；Gold/Silver（S=1 M=0.8 L=4 edge=1 rim=(0.584,0.577,0.412)）；暗底 cast bg (0.104,0.065,0.069)；Test_2 LUT | 战斗场景：PixelartCameraRig 代码装配、日光平涂（海/沙/草/岩 L2–L3 纯色材质）、无点光阵列、无 rim 光、背景=天空色、无 LUT | 缺失（氛围要素，属场景设计非管线） | 见材质实测表；如需 006 观感先补材质喂入与点光（管线已支持附加光，夹 8：`PixelartBeforeRenderFeature.cs:32,80-82`） |

## 材质喂入实测

统计对象：全仓使用 `PirateCrew/Pixelart/PixelartObject` shader 的材质（以 `_DitherStrength` 键识别），盘上共 **134 个**：`Assets/Pixelart/Materials/` 101 + `Assets/Art/Materials/` 24（Crew×11 / Lowpoly×2 / FloatingIsland×11）+ `Assets/Art/Models/WorldKit/Materials/` 9（Kit_ 派生）。commit 内已入库 125（WorldKit 9 个为盘上在途）。

| 参数 | 实测 | 来源 |
| --- | --- | --- |
| `_Smoothness` | **134/134 = 0** | 三个目录逐一 grep，无例外 |
| `_Metallic` | **134/134 = 0** | 同上 |
| `_RimLightColor` | **134/134 = (0,0,0,1) 黑** | 同上 |
| `_MainLightLevel` | L1×2（CrewProbe/GeoProbe 地面）、L2×8、L3×86、L4×34（工厂默认档） | 同上 |
| `_AAScale=0` | 33 个（GrassField T0-2 系列、Level/Cloud 大平面等） | 同上 |
| `_Priority=0` | 9 个（草叶贴片：遮挡物不出接触线） | 同上 |
| `_DitherStrength` | 全部走工厂默认 0.21（Bayer4） | `PixelartMaterialFactory.cs:68` |
| 工厂默认 | S=0、M=0、rim=黑、band=4、edge=-1/threshold=1、outline=1、snap=1（`PixelartMaterialFactory.Configure:185-189`） | 与实测分布吻合（L4 组即默认） |

**参照 006 Gold 观感要素在本仓的载体判定**：

| 要素 | 参照值 | 本仓载体 |
| --- | --- | --- |
| 金属高光 | Gold/Silver S=1 M=0.8，白/金色 specular 两档量化 | **无**（S/M 全 0，Specular 趟输出恒 0） |
| GI 金属反射 | EnvironmentBRDF specular=lerp(0,albedo,metallic) | **无**（GI=ambient×albedo 且 M=0 双重缺失） |
| 边缘光 | 两盏 rim 组件 + 材质 rim=(0.584,0.577,0.412) | **无**（材质 rim 全黑；无 rim 光组件；链路缺量化/ndotl/tint） |
| 点光氛围 | 3 盏金色 Point Light 2.79–3.21 | 管线支持（附加光夹 8）但场景无布置 |
| 暗背景 | cast bg (0.104,0.065,0.069) | 无（战斗场景清天空色） |
| 非金属色带 + 内线 | DefaultRed 等 L2 / edge=-1 / threshold=1.0 | **有**（本仓主线，L2–L4 + edge=-1 全覆盖） |
| 密度抖动 | DitherGrayScale 16×16，strength 0.038 | 有对应机制（1-bit 密度模式），默认走 Bayer 0.21 |

## 高危问题（按严重度）

1. **【高】调色板/颜色校正整链缺失于基准 commit**。参照以 LUT 收口最终色彩（`external/sloane-urp/.../Shaders/Blit/ColorCorrection.shader:29-43`，feature 挂 Cast 渲染器、palette=Test_2.png）；本仓 eb806c0c 的 `pirate-crew/Assets/Resources/PixelartShaders/` 只有 5 个 shader，`PixelartColorCorrection.shader`/`Feature.cs` 是盘上未跟踪在途文件，调色板烘炉（CIEDE2000）无对应物。后果：最终色彩无调色板域收口，「改参数对原作」缺最后一层换算。建议：在途文件验收后入库，LUT 资产与 `Resolution=palette.height` 口径对齐参照采样公式。
2. **【高】材质喂入为零：金属高光 / GI 反射 / 边缘光三条通道无数据载体**。实测 134/134 材质 S=0、M=0、rim=黑（`pirate-crew/Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartMaterialFactory.cs:185-187` 是默认来源，`Configure` 无人覆写非零值）。管线里 Specular 趟、GI 金属项、RimLight 趟全部写的是「恒 0 的数据」。后果：参照 006 Gold 的核心观感（金属金色高光、边缘光带、点光下的反射）在本仓**结构性不可见**，调 shader 参数无济于事。建议：按「最小修复顺序」第 2 条先开试点材质。
3. **【高】边缘光链缺四段**：本仓 `Pixelart/Shaders/Includes/RimLight.hlsl:107-110` 刻意只保留「屏幕空间光向 × 连通域门控」，缺 ndotl 门控、`multiStep(modifier·ndotl, 2)` 两档量化、金属 tint（specular=lerp(white,albedo,metallic)）、独立 rim 光列表（参照 32 盏 StructuredBuffer，`SloanePixelartShadingRendererFeature.cs:71-83`）。后果：即使喂入 rim 色，出来的也是「未量化的连续亮边」，与参照「按档翻转的背光带」不同源。建议：先在契约 §2.2 登记 rim 档数全局，再接回 `ndotl` 与量化两行（文件头 97-110 行已写明接线顺序）。
4. **【中】GI 为简化环境光项**：`PixelartShading.shader:551-584`（ambient×albedo）；参照 `ShadingPass.hlsl:168-230` 的 SH/lightmap、GlossyEnvironmentReflection、EnvironmentBRDF、Fresnel、NoV 量化无载体。有据（参照场景未烘 lightmap），但金属反射随之永黑。属「接受简化、按需回补」。
5. **【中】G-buffer 无 LightmapUV 通道**：参照第 8 张 MRT（`DefaultPass.hlsl:42,86`）+ `SAMPLE_GI` 路径未移植；本仓将来烘 lightmap 也吃不到。
6. **【中】PriorityManagement（优先级选块内样本）未移植**：参照 `PriorityManagement.compute:24-76` + UV 偏移采样（`BuffersResolver.hlsl:7-9`）；本仓以固定块锚 + BlockPatch 过半修补替代（`Pixelart/Compute/BlockPatch/BlockPatch.compute`）。跨物体边界块的「代表像素归属」语义不同，前景小物件可能被大平面代表。
7. **【中】描边 shader 头注与参照资产不符**：`pirate-crew/Assets/Resources/PixelartShaders/PixelartOutline.shader:177` 写「v3 那个特征 `m_Active: 0`（从没跑过）」，但参照 `Sloane Pixelart Cast Renderer.asset:45-53` 上 Outline feature `m_Enabled: 1`、SolidColor=(0.2547,0.2547,0.2547)。照注释「恢复 v3 行为」会走错基线，须人工核改注释。
8. **【低·刻意偏差组】**（均有源码内注释与裁决日期，列出来防「被改回 v3」）：抖动施加 `offset·(0.5/level)`（`PixelartShading.shader:205`）；连通域降档 ×2（`:214`）；主光 distanceAttenuation 强制 1（`:388`）；主光方向/颜色由 rig 覆写（`:395-397`）；偶数 k 精确块窗口与 `>=` 守卫（`ConnectivityResult.compute` / `ConnectivityCheck.compute` 头注）；applyAA 判 r/g/b 三通道修 v3 的「g 判两次」bug（`:366-368`）；锁 pixelScale 反推分辨率与 CoreBlit 上屏（`PixelartCameraRig.cs:608-634`）。

## 建议的最小修复顺序

1. **收口在途的调色板一环**：`PixelartColorCorrection.shader` + `PixelartColorCorrectionFeature.cs` 验收后入库（含 LUT 资产与采样公式对齐）——它已在途、且是「出图即风格」的最后一环，不动管线结构。
2. **材质喂入试点**：选 1–2 个道具（宝箱/炮铜饰件）按参照 Gold 配方喂 S=1、M=0.8、`_RimLightColor` 非黑。注意工厂注释已警告（`PixelartMaterialFactory.cs:156-163`）：直接恢复 S 会在大平面上重现「高光带整档翻面」（参照自带行为），开 S 前先按该注释做高光软化（按艺术像素抖动/提档数）。
3. **边缘光补链**：契约 §2.2 登记 rim 档数全局 → `RimLight.hlsl` 接回 ndotl + `multiStep(2)` → 再评估 32 盏 rim 光 StructuredBuffer 是否入管线（战斗场景可能只要主光背光带，不必搬全套组件）。
4. **GI 维持现状**（ambient×albedo 有裁决依据），金属反射待第 2 步喂入落地后再评估 EnvironmentBRDF 值不值得搬。
5. **注释纠偏**：改 `PixelartOutline.shader:177` 的「v3 从没跑过」为资产实情（m_Enabled=1），防止后人按错误基线「恢复」。
6. **场景氛围**：战斗场景若要对 006 观感，补点光（管线已支持，注意 `MaxAdditionalLights=8` 夹断）与暗底背景色，属场景设计不属于本审计的代码修复。
