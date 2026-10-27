# Sandcastle 水体逆向

> **本文是什么**：Steam 免费演示《Sandcastle Demo》（开发者 Fabien Weibel / Bubblebird Studio，
> Unity IL2CPP + 自研渲染管线 + D3D12）的水体/沙滩/泡沫技术架构的逆向档案，
> 是[实验水体](../管线/实验水体.md)的选型主依据。
> 只写「它是什么、怎么实现的、证据是什么」；**不写怎么抄**——实施设计在实验水体文档。
> 逆向属合法研究：本机已购买的游戏、本地静态分析、只提取技术结构不搬运资产。

---

## 1. 方法与证据强度

| 手段 | 产物 | 强度 |
| --- | --- | --- |
| UnityPy 解包 `sharedassets0.assets` / `level0` | Shader / ComputeShader / Material / Texture / MonoBehaviour 全量清单（shader 58、compute 22、Texture2D、Texture3D、RenderTexture 等逐类清点） | 第一手 |
| LZ4 解压 shader blob + `d3dcompiler_47.D3DDisassemble` 反汇编 DXBC | 渲染 shader 全 pass 指令流、compute 全 kernel 指令流 | 第一手 |
| UnityPy 读 ComputeShader 变体表 | 全部 kernel 名、线程组尺寸、in/out buffer 与 texture 绑定名 | 第一手 |
| **Cpp2IL ILR `Assembly-CSharp.dll` + `ilspycmd` 反编译** | **C# 方法体全文**（模拟常量、dispatch 序、剔除架构、预设读取） | 第一手 |
| Cpp2IL `diffable-cs` 字段偏移签名 | 嵌套可序列化类的字节布局（用于直读资产预设） | 第一手 |
| [80.lv 采访](https://80.lv/articles/creating-cozy-sandbox-sandcastle-with-a-focus-on-water-sand-simulation) | 开发者口述：自研无插件、基于科研论文、水沙统一框架、双分辨率、噪声去干涉伪影 | 二手（口述） |

Il2CppDumper 不支持该作 metadata v39（Unity 6.2）——**但 Cpp2IL dev 分支（2026 版，需 .NET 10 SDK 编译）已支持**：用便携安装的 .NET 10 SDK（`dotnet-install.ps1 -Channel 10.0`，不入系统）编译 Cpp2IL，产出 dummy DLL + `diffable-cs`（类/字段/方法签名）+ `dll_il_recovery`（ILR DLL）。**ILR DLL 的 IL 是完好的**——用 `ilspycmd` 反编译可直接得到可读方法体（`external/sandcastle-ref/cs-full/`，251 个源文件）。因此 C# 侧**不止签名闭合，方法体也已闭合**：模拟常量真值见 §8、预设真值见 §12。复现脚本在 `external/sandcastle-ref/`（gitignored 工作台，不入库）。

## 8. 模拟常量真值（Cpp2IL ILR 从构造器初始化器恢复，2026-10-14）

| 类 | 字段 | 真值 | 备注 |
| --- | --- | --- | --- |
| Water | `gravityForce` | **10.0**（[Range 0-20]）构造默认；**16.0**（level0 场景实例） | 本仓已对齐 16 |
| Water | `evaporationFactor` | **0.15**（构造默认）；**0.1**（场景实例） | 本仓湿沙蒸发恰好也调到 0.15 |
| Water | `level` | **1.0** | 本仓 SeaLevel=0（相对地面，等价） |
| Water | `floorBlurIterations` | **3**（场景实例） | 底床平滑迭代数 |
| Water | `enableSpawn` / `enableEvaporation` | **true / true**（场景实例） | 涌浪泵与蒸发开关 |
| Water | `spawnRate` | **20.0**（场景实例） | 沙滩道具（Findable）生成步频；**与水量泵无关**（§9 更正） |
| Water | `chunks` | Vector2Int(4, 4) 构造默认；场景实例 **(8, 8)** | 域分块 |
| Ground | `gravityForce` | **0.1** | 沙=高粘性流体，「重力」是阻尼量级 |
| Ground | `noiseFactor` | **0.0001** | 双分辨率解耦噪声 |
| Air | `gravityForce` | **10.0** | |
| Air | `evaporationFactor` / `advectionFactor` | **0.2 / 0.5** | |
| Whitewater | `capacity` | **32768** 粒子 | W4 浪花粒子的预算参考 |
| Whitewater | `frustumOverscan` | 0.1 | 视锥剔除外扩 |
| Water | `WATER_SATURATION_POINT` | **0.05** | **结合水基准**：`depth=max(h−0.05,0)`，同时用于 η/通量/水面高度/可见深度；h≤0.05 不显形 |
| Water | `maxVelocity`（每帧下发） | **`0.5·√gravityForce`** | 不是独立常量，由 g 派生 |
| Water | 子步 `deltaTime` | **`cellSize×0.5 / √gravityForce`** | Courant=0.5 的 CFL 显式步长，配累加器 |
| Water | `waveAmplitude`（每帧下发） | `saturate(playTime×0.05) × max(四列预设项)` | 开盘 20 s 内渐起（见 §12 预设） |
| Layer（所有域基类） | `resolution` / `THREADS_GROUP_SIZE` | 默认 64 / 32 | 每域各自解析度；`cellSize = beach.bounds.size / resolution` |
| Gi | `resolution` | 128 | 体素 GI 网格 |

场景实例真值来源：`level0` 序列化文件里 `Water` MonoBehaviour 的裸字节（UnityPy 按 m_Script PPtr 跨文件解析类名后直读，`external/sandcastle-ref/dump_configurator_raw.py`）。

**派生量在新一轮反编译中补全**（`Water.Run` 方法体，`external/sandcastle-ref/cs-audit/Water.cs`）：原作每帧只下发 `gravityForce`，`maxVelocity`/子步 `deltaTime` 全部由它**现场派生**；`advectionFactor`/`damping`/`kappa` 取自配置（`WaterProperty` 预设，见 §12）。

**已从序列化资产回读的配置真值**（`Refs` 的 `waters`/`waves`/`formats` 列表，2026-10-21）：
`WaterProperty` 四档全部 `advectionFactor=0.2`、`damping=0`；`kappa` Regular/Clear/Bahamas=1000、Syrup=2。
`WavesProperty` Regular 四列第三分量 0.1/0.15/0.2/0 → `waveAmplitude` 峰值 0.2 m（CPU 取四列 **max**，不求和）。
`FormatProperty` 15/30/60 m 分别配 256/512/1024²，格距恒 5.86 cm（§8.1）。

### 8.1 域与网格分辨率（`FormatProperty` 预设，2026-10-18 直读）

域尺寸与网格来自 `Refs` 的 `formats` 预设（`Beach.FormatProperty = {Vector2 size(m); Vector2Int resolution}`），
`Layer.Setup` 据此算 `cellSize = bounds.size / resolution`：

| 档 | 域 (m) | 网格 | 格距 |
| --- | --- | --- | --- |
| 1 | 30×15 | 512×256 | ≈5.86 cm |
| 2 | 15×15 | 256×256 | ≈5.86 cm |
| 3 | 30×30 | 512×512 | ≈5.86 cm |
| 4 | 60×60 | 1024×1024 | ≈5.86 cm |

即原作**模拟格距恒定 ≈5.86 cm**，域越大格数越多（另 `resolutions` 三档按位移调解析度、`Water.chunks` 分块调度）。本仓实验室域取 64 m/1092²（格距 5.861 cm），保留原取景机位，格距与原作一致。

### 8.2 参数可恢复性分级（避免再把“工具解不出”当成“原作没有”）

| 级别 | 内容 | 处理方式 |
| --- | --- | --- |
| A | 构造默认值、场景实例序列化字段、`Refs` 预设（`gravityForce`/`level`/`spawnRate`、`WavesProperty`/`WaterProperty`/`FormatProperty`） | 直接从发行包回读并逐项对齐 |
| B | 由 A 在 `Water.Run` 现场派生的量（`maxVelocity`、子步 `dt`） | 按公式对齐，harness 契约测试 |
| C | 运行时状态场（水格高 4 bit 耦合权重场——由关卡初始水场/笔刷写入；`obstaclesMap` 的运行时障碍高度分布） | 静态不可恢复；实现里必须标“自推/近似”并写理由，不得声称为原作值 |
| D | 渲染侧不可从资产静态取出的量（σ、散射色等） | 按成品画面反定；属显示定标，不是模拟参数 |

**纪律**：任何“抄不到”结论必须先过这张分级表；A/B 级拿不到是调查不足，C 级才允许自推，
D 级只允许按参考图定标。参数对账与验收门见[实验水体 §2.4](../管线/实验水体.md)。


## 2. 总架构：三场一光的 GPU 物质模拟

游戏是一个 64 m 级沙滩沙盘，**水、沙、泡沫、空气全部是 GPU 格点模拟**（各自独立 compute 资产），渲染走自研 SRP（`BeachRenderPipeline`），另有体素光线追踪 GI：

| Compute 资产 | 职责 | 关键 kernel |
| --- | --- | --- |
| `Water` | 浅水方程海浪 | `Initialization` → `UpdateFloor`(读 groundCells) → `BlurFloor` → `HeightIntegration` → `VelocityIntegration` → `Advection` → `UpdateAmount`(读 obstaclesMap/airCells) → `UpdateTangent` → `SubtractFloor` → `CopyGroundHeight` → `Paint`(笔刷交互) |
| `Ground` | 沙地（同构求解器+高粘性） | `Initialization` → `HeightIntegration`(**读 waterCells**＝湿沙渗透) → `VelocityIntegration` → `UpdateTangent` → `UpdateObstacleHeight` → `Paint`；沉积参数全套（`sedimentCapacity/Suspension/Deposition`、`waterErosionFactor` 等） |
| `Whitewater` | 白水/泡沫 | `Initialization` → `Update`(读 waterCells + whitewater 双缓冲) → `Culling`(写 drawList/drawShadowList + args ＝ **GPU indirect 实例化**，视锥+阴影视锥双剔除) |
| `Air` | 气相（浪花气溶胶类） | `Initialization` → `HeightIntegration` → `VelocityIntegration` → `Advection` → `Paint` |
| `Gi` | 体素 GI | `Initialization`(giVoxels/giVoxelsBlurred/sceneVoxels) → `Raycast`(raycastResults) → `BlurGi` |

常量表（`$Globals`，名字是第一手、数值在 C# 侧未闭合）：`waterResolution`/`groundResolution`/`airResolution`（**双/多分辨率**）、`beachMin/beachMax/beachSize`、`waterCellSize`、`deltaTime`、`gravityForce`、`maxVelocity`、`damping`、`kappa`、`advectionFactor`、`waveAmplitude`、`customWave`、`level`（海平面）、`evaporationFactor`、`sunColor`、`playTime`、笔刷全套（`brushPatternStencil/brushData/brushMode/brushWorldToObject/…`）。

**采访印证**（80.lv）：模拟完全自研无插件、「上网找科研论文」、沙=同一求解器加「大得多的粘性与阻尼」、水沙**双分辨率**耦合出现干涉伪影、靠在沙侧加随机噪声解耦——副产品是沙粒质感。

## 3. Water 求解器数值形态（反汇编判读）

- 格点：`RWStructuredBuffer<float4>` stride 16（h, vx, vz, 泡沫/标志），`dcl_thread_group 32×32×1`；
- 高度场以 **24 bit 定点**打包进相邻字段（`ushr 12`/`bfi`/`and 0x00ffffff` + `×1e-6` 还原）——带宽优化；
- `HeightIntegration`：4 邻域通量 + **5 cm 饱和点**：`depth = max(h − 0.05, 0)`、`η = ground + depth`，通量用 depth 而非原始 h（`WATER_SATURATION_POINT = 0.05`，C# 与 shader 字面量互证）+ 边界 ghost 格（越界取镜像/虚拟值，`ige/movc` 分支）。`h` 的前 5 cm 是结合水：渲染侧同样取 `max(h − 0.05, 0)` 作可见水深（§10.3），所以岸线是 h=0.05 等值线，不是 h=0；
- `Advection`：半拉格朗日回溯 `uv_back = uv − v·advectionFactor·dt/cellSize`，在
  `uv_back·resolution − 0.5` 处对 h 双线性采样后写回；**只平流 h**，速度/泡沫字节保留。
  `advectionFactor=0.2`（Refs 真值）。这是原作对 h 的唯一数值耗散项，也是 surf zone
  把 bore 前缘摊开的主要机制；它不严格守恒，原作靠 `UpdateAmount` 的潮位松弛补回。
- `UpdateAmount`（每帧一次）：蒸发 → `obstaclesMap` 泵 → 潮位松弛。泵只在
  `ground < obstacle ≤ η < obstacle+0.1` 的格上 `amount += 0.15·sin(8·playTime+0.0378·uv.y)·dt`
  （相位空间项乘归一化 uv.y，几乎全场同相）；环境浪主体是全场潮位松弛
  `max(level−ground+clamp(waveAmplitude·sin(playTime),−0.5,∞),0.05)`，每格按 4 bit 权重/15
  向目标松弛（每帧比例，不乘 dt）。权重是运行时字段，静态不可恢复；本仓用初始淹没区
  满耦合/干区零耦合的静态代理。详见 §9。

> **⚠「全场潮位松弛/每格 4bit 权重」已被 2026-10-27 B2 运行时实测修正**：权重场 96.9% 为 0
>（无全场松弛），仅背边缘 16 格桨带 1..15/15 ramp（造波机）；详见 rt/reports/B2-phase-weights.md。

结论：**标准 2D 浅水方程（SWE）格点求解器**，∂h/∂t = −∇·(hv)、∂v/∂t = −g∇h − 阻尼，半拉格朗日平流，干湿边界处理。与本仓 `WaterWaveField2D` 的波动方程（涟漪，无质量输运）是**不同方程**——波浪推岸、涌水、退潮这类行为只有 SWE 有。

## 4. Lit/Water 像素着色器逐段还原

指令流（ps_5_0，4 纹理 + CB0[138] + CB1[1]，材质传 `refractionFactor`）：

1. **alpha 裁剪**：水量阈值 −1e-5 以下 discard（模拟场直接驱动可见域，岸线即干湿线）；
2. **细节法线**：世界 XZ × 0.05 采样 `WaterNormal2D`（1024²）双通道拆包 `*2−1`，与顶点传入的大波法线合成后归一（`sqrt(1−t²)` 重建 y）；
3. **屏幕折射（带深度安全检查）**：UV 偏移 = N.xz × `refractionFactor`（材质实测 0.012）；采样场景深度图（`z_buffer_param` 风格 `mad+rcp` 线性化），**仅当折射采样点的深度比当前像素更远才采用偏移**（`lt→and 0x3f800000` 门控），防止折射采到前景物体；
4. **折射点世界坐标重建**：`InvVP` 矩阵（cb0[66..69]）× NDC + 1/depth → 世界坐标，`dist = length(worldRefract − worldSelf)` 得水下光程；
5. **Beer–Lambert 水体吸收/散射**：`T = exp(−σ_t·dist)`（`exp` 指令 + 8.656 缩放，RGB 三通道独立 σ_t）× `waterScatteringColor`（材质实测 **橙黄色** (0.57, 0.36, 0)——浅水散射近似底沙色），乘进折射背景色；
6. **菲涅尔**：NdotV 的 pow5；
7. **太阳高光 + 阴影**：`sample_c_lz` shadowmap 比较采样（worldToShadow 矩阵 + 边界检查），单一 `pow(NdotH,1500)` 高光乘阴影（全文仅一对 log+exp=pow，无第二宽瓣/GGX——本行旧记「GGX 高光」系同一项误读，2026-10-16 以指令计数纠正）；
8. 天空反射项按菲涅尔混合（环境色常量）。

### 4.1 Lit/Water PS 完整公式与覆盖率（159 行指令全解，2026-10-15）

> **⚠ 本节两处结论已被 2026-10-27 C 流逐指令复核反转**：散射权重实为
> **(1−NdotV)⁵**（掠射强，本节"NdotV⁵"反了）；镜面乘 **(1−NdotV)⁴ 且不乘阴影**
>（本节"×阴影原值"误读 L156 寄存器来源）。另天空反射项/流场平流活性等修正
> 见 `external/sandcastle-ref/rt/reports/C-water-map.md`（实现以该报告为准）。
> 历史注：旧"NdotV⁵ 恢复后色带对齐"是在 σ≠0 误设下的补偿拟合，σ≡0 定案后失效。

完整伪代码（寄存器流逐行还原；cb0 索引为原作常量缓冲槽位）：

```
输入：v0=屏幕坐标, v1=世界坐标, v2=顶点法线(模拟场差分), v4.xy=流场扰动, v4.w=水量
α裁剪:  水量 < 1e-5 → discard
法线:   N = normalize( v2 + WaterNormal2D细节 )
        细节 = 双尺度采样(世界XZ×0.05 与 ×0.20) 按时间锯齿动差混合,
        采样坐标 ± clamp(流场,±0.5) 扰动, 时间项 cb0[113].y×0.8, 解包×2−1
折射:   偏移 = N.xz × refractionFactor(0.0121)，Y 乘纵横比；深度安全门控（同本仓）
光程:   折射点世界坐标 = InvVP × (折射UV的NDC, 折射UV深度)；
        dist = |折射点世界坐标 − 表面世界坐标|（真路径长度）
吸收:   T = exp(−8.65617 · σ_rgb · dist) × cb0[131]水色
背景:   bgColor = 不透明纹理(折射UV) —— 真实沙网格的渲染结果，
        水底的光照/阴影全部由 Lit_Sand 画好再被折射采样（§10），水体 PS 自身不算地板光照
基底:   base = bgColor × T × (0.9 + 0.2·NdotV⁶)
混合:   col = lerp(base, cb0[121]散射色, NdotV⁵ × min(shadow+0.25, 1) × 0.75)
        ——散射权重随俯视增大（反汇编链 nv·nv²·nv² = NdotV⁵·0.75）；阴影软化(+0.25
        下限/最深 75%)且只作用于散射项；阴影在表面位置采样一次（比较采样）
高光:   col += cb0[125]太阳色 × pow(NdotH, 1500) × shadow
        ——镜面乘的是未软化的比较采样原值（movc 直出），不乘菲涅尔；
        碎金全部来自细节法线贴图的高频法线 + 近镜面瓣，无独立 glitter 随机场
alpha:  saturate(水量 × 100)   ← 不透明队列，「透明」全在 shader 内的吸收
```

覆盖率表（本仓 `WaterLabOcean.shader` 对照）：

| 原作环节 | 本仓状态 | 差异与裁决 |
| --- | --- | --- |
| α 裁剪 | ✅ 等价 | depth<0.02 clip |
| 顶点法线（模拟场差分） | ✅ 等价 | OutputField 里邻域差分编码，shader 解码 |
| 细节法线双尺度 | ✅ 已复刻 | 程序化可平铺法线贴图（512² 域扭曲+脊状 fbm，`WaterLabDetailNormal` 生成器，形态与统计对齐原作抽图 `external/sandcastle-ref/`）替代原作贴图，采样参数同（0.05/0.20、时间×0.8、相加合成）；流场扰动未复刻（原作 v4.xy 来自 VS，等价信息在我们是模拟速度场，暂未接入） |
| 折射 | ✅ 已复刻 | refractionFactor=0.0121 照抄 + 纵横比修正 + 深度安全门控 |
| 真光程 InvVP 重建 | ✅ 已复刻 | UNITY_MATRIX_I_VP + ComputeWorldSpacePosition |
| 吸收 8.65617·σ·dist | ✅ 结构照抄 | **σ（cb0[132]）静态不可取**：不在 `WaterType` 预设里（四档 waters 只有 `color/scatteringColor/advectionFactor/kappa/damping`，见 §12）、C# 无该全局（全局名全表见 §13）、材质也无该槽，且 DXBC 的 RDEF 反射块被剥离（只有 ISGN/OSGN/SHEX）→ 名字与默认值都拿不到。**注意单位**：8.65617 是「归一系数→每米」的换算，σ 属 tint 域，本仓按判据定标（详见实验水体文档） |
| 基底 0.9+0.2·NdotV⁶ | ✅ 已复刻 | — |
| 散射混合 NdotV⁵×阴影×0.75 | ✅ 已复刻 | 阴影软化 min(+0.25,1) 同款；散射色原作文件值 (0.57,0.36,0) 暖橙按蓝天空色改（色管与后处理不同，见 §5.1） |
| pow(NdotH,1500) 高光 | ✅ 已复刻 | ×阴影原值（原作同构）；+ 次级宽瓣 pow96×0.06 供近距离 shimmer（原作无，观感补） |
| alpha | ✅ 机制差异观感等价 | 原作不透明队列水量×100；本仓 URP 透明队列 1−min 透过率（_CameraOpaqueTexture 自采样限制，§5.1） |
| 体素 GI 背景 | ❌ 未复刻 | 原作海底亮度来自体素 GI + 环境光；本仓单平行光 + 平坦环境色，背景亮度结构不同（已知观感差的最大来源） |
| Whitewater 粒子 | ❌ 未复刻 | W4 范围 |
| 流场扰动细节法线 | ❌ 未接入 | 高频项，观感影响小 |

这正是 [HPWater](../../../项目/归档/参考逆向/) BSDF 库 diffT/diffR 思路的**单 pass 前向简化版**：HPWater 用 thickness 圆锥近似 + 非线性光程 + Henyey 相位背光透射；Sandcastle 直接用「折射点距离」当光程、常量 σ_t、无相位项。两者同族，**Sandcastle 版可在 URP 前向单 pass 内落地**。

## 5. 材质参数实测（观感定标起点）

| 材质 | 关键参数 |
| --- | --- |
| Water | `refractionFactor 0.0121`、`waterColor (0.96,0.99,1.0)`、`waterScatteringColor (0.57,0.36,0)`、`_Cull Off`、法线图 WaterNormal2D |
| Whitewater | `baseColor 白`、`groundColor (0.62,0.58,0.56)`、`ironOxidesColor`（铁锈色调） |
| Borders 三件套 | `sandMap`/`strataMap`（Strata 512² 层理图）、`backgroundColor`/`rockColor`/`sandColor` 两档、`refractionFactor 0.01` |

「BordersBack / BordersGroundFront / BordersWaterFront」三 shader 画沙盘剖面的侧壁与水线截面（正面能看到水的垂直截面），是「沙盘水族箱」观感的来源；层理（strata）纹理做沙层剖面。本仓实验场景不做剖面，此件留给将来正式场景。

### 5.1 参数照抄对照表（哪些抄了、哪些为何重调）

**能照抄的（资产里明文，已照抄为起点）**：`refractionFactor 0.0121`（我们 0.05——因我们法线坡度场比他们的细节法线贴图弱，需要更大偏移达到同等扭曲读感）；水体不透明 + ZWrite、干湿 clip、深度安全折射、折射点世界坐标重建光程、exp(-σ·d) 三通道吸收结构——**这些是照抄的**。

**已从序列化资产回读（原「抄不到」结论作废）**：`gravityForce=16`（场景实例）、
`maxVelocity=0.5√g=2 m/s`、子步 `dt=0.5·dx/√g`、`advectionFactor=0.2`、`damping=0`、
`kappa=1000`（Regular；Syrup=2）、`waveAmplitude` 峰值 0.2 m（Regular 四列 max）、
域/网格 15/30/60 m 对应 256/512/1024²（格距 5.86 cm）。仍不可静态恢复的只有：
水格高 4 bit 的运行时耦合权重场（由关卡初始水场/笔刷写入）和 `obstaclesMap` 的运行时
障碍高度分布；`WavesProperty` 四列只贡献 `max` 标量峰值，不存在运行时三角组合。

**抄了但必须重调的（同值不同画面）**：`waterScatteringColor (0.57,0.36,0)` 橙色照抄后浅水发浑——他们的管线是自研 SRP + HDR 调色/曝光/tonemapping，同一线性值过他们的后处理才是画面上的样子；我们 URP 直出没有那层，最终色按 URP 前向 + 无后处理的显示链重调（σ_t、散射蓝、菲涅尔天空色都是）。**结论：结构照抄、数值对齐「显示观感」而非「文件数值」——两边的色管与后处理不同，文件数值对齐没有意义。**

## 6. 与参照库的融会判定

- **HPWater（技法库）**：其 BSDF 库的 thickness→光程近似（波峰薄透亮/波谷厚深色）与非线性光程修正，比 Sandcastle 的常量距离更有层次，实验水体吸收项按此增强；其 HDRP/RenderGraph 管线壳层依旧不搬（判定与[海面](../管线/海面.md) §2 一致）。
- **FLIP（不引入）**：容器内液体求解器，对海面仍是错误工具（判定维持）；局部交互液体的候选地位不变。
- **Crest**：不引入（URP 在付费版），判定维持。

## 7. 对接本仓的落点

- 域尺度 64 m、双分辨率（水密沙疏）、SWE + 半拉格朗日 + 干湿阈值 0.05、白水浓度场 + （后续）GPU indirect 粒子——进 [实验水体](../管线/实验水体.md)；
- 折射/吸收/菲涅尔/高光的单 pass 前向水体着色（含深度安全检查与 InvVP 光程重建）——进实验水体的渲染 shader；
- URP 侧前提：`m_RequireDepthTexture`/`m_RequireOpaqueTexture` 现役均为开，实验场景可直接用；Linear 色空间；
- 波浪生成机制（涌浪泵 + 潮位松弛，§9）——进实验水体的波源 kernel；水底波影不在水体侧：
  原作水面无 ShadowCaster（§10），波影在沙 shader 的「阴影 × exp(−0.721×水深)」项。

## 9. 波浪生成机制实锤（UpdateAmount / Paint 反汇编，2026-10-14）

Water compute 的 11 个 kernel 全部反汇编后逐条判读（每个 kernel 两份等价编译产物互为对账）：

- **环境浪 = 全场潮位松弛（主体）+ `obstaclesMap` 正弦泵（局部扰动）**，都在 `UpdateAmount` kernel 里（全 compute 唯一含三角函数的 kernel，两条 `sinc`；2026-10-21 按 DXBC 逐行重解，取代旧「spawnMap 涌浪泵」读法）：
  1. **调度位置**：`Water.Run` 在全部 simulation 子步之后**每帧只派发一次** `UpdateAmount`，`deltaTime` 是帧 dt（子步才用 `0.5·dx/√g`）；kernel 内顺序为 蒸发 → 泵 → 两段潮位松弛。
  2. **潮位松弛（环境浪主体）**：`waveAmplitude = max(0.01, saturate(playTime×0.05) × max(WavesProperty 四列第三分量))`（Regular 峰值 0.2 m，20 s 渐起）。全场目标水量 `max(level − ground + clamp(waveAmplitude·sin(playTime), −0.5, +∞), 0.05)`——**目标是空间均匀的纯时间函数，没有行波项**；`customWave>0` 时改为 `level + customWave − ground`（不夹 0.05）。每格按自身 4 bit 耦合权重 `w/15` 向目标松弛（每帧一次的比例，不乘 dt）。权重是水格高 4 bit 的运行时场，由关卡初始水场/笔刷写入，静态不可恢复。
  3. **泵（局部扰动）**：`UpdateAmount` 绑定的是 **`obstaclesMap`**（Beach 的俯视 RGHalf RT，point 采样），不是 spawnMap。门控 = `ground < obstacleHeight` 且 `obstacleHeight ≤ η < obstacleHeight+0.1`；满足时 `amount += 0.15·sin(8·playTime + 0.0378·uv.y)·dt`。相位空间项乘的是**归一化 uv.y**（跨全域仅 0.0378 rad），旧记「0.0378 rad/格 → λ≈41.6 m」是把 uv 当格坐标的误读。`Water.Spawn()` 是沙滩道具的生成逻辑，与水量泵无关；`spawnRate=20` 只控制道具生成步频。
  4. **蒸发**：`h -= exp(-0.721348 × A) × sunColor.r × evaporationFactor × 0.001 × dt`。A = **airCells.water（水汽）**在空气网格（`airResolution`）上的双线性采样，22 bit 取自空气格 byte offset 4 的 bits 12..33；`evaporationFactor=cb0[158].y`（`enableEvaporation=false` 时 CPU 下发 0）、`sunColor.r=cb0[125].x`、`dt=cb0[157].z`。`0.721348` 与沙面透光衰减共用同一常数。所以蒸发由「阳光 × 干空气」驱动、被水汽抑制，**不是「水越浅蒸发越快」**；它只作用在水场 h 上，湿沙湿度场不由此项直接更新。满耦合水格随后被潮位拉回目标，故可见影响主要落在未耦合的干/浅格。
- **`Paint` kernel**：CPU 写 `input` 缓冲（stride 16，24bit 定点），kernel 双线性溅射进水场——笔刷/泼水/造波机道具（`WaveGenerator`）的注水通道，非环境浪。
- **渲染网格无三角函数**：`Lit/Water` 两个 vs 全无 `sinc`——海面起伏全部来自模拟场，无「视觉波」叠层。
- **`WaterCell.wave`/`tangent` 字段**：`UpdateTangent` 只做邻域差分（法线用），无波形生成。

结论：Sandcastle 的「不规律海浪」不是多列 Gerstner 叠加，而是**全场潮位松弛 + 每格静态耦合权重 + 地形浅化/岸线进退 + 障碍物泵**四者涌现——`WavesProperty` 四列只贡献一个标量峰值（`max`，不求和），不存在运行时三角组合；泵的空间相位项跨全域只有 0.0378 rad，本质是近均匀的局部扰动。渲染端无程序浪形（§10）。

## 10. Lit_Sand PS 与 Lit_Water VS 实锤（水底波影与浪形，反汇编逐条，2026-10-15）

> **⚠ 本节五处修正（2026-10-27 D 流逐指令复核）**：①"闪沙"项不存在=焦散项本身；
> ②第二平流场恒 −4 禁用；③坡度只门控焦散不暗化 albedo；④白水粒子=贴水皮水平扁
> 四边形非 billboard；⑤另有域贴图加性 GI 项与深水沙变体。逐条见
> `external/sandcastle-ref/rt/reports/D-sand.md`（实现以该报告为准）。

### 10.1 Lit_Water 无 ShadowCaster——水面不投影

`Lit_Water.blob` 全量 DXBC 容器扫描：只有 4 个程序（2 VS 变体 + 2 PS 变体），无深度-only pass。**原作水面不往任何东西上投影**——沙滩上/水底看到的「波影」不来自 shadowmap。

### 10.2 Lit_Sand PS（@34958 最全变体，逐段）

- **直射项 = ndl × 太阳色 × (阴影比较采样原值 × exp(−0.721348 × 水侧模拟场))**：阴影无 PCF 无软化（`sample_c_lz` 点比较 + 视锥外恒 1），exp 项的场按**逐像素 4 邻双线性**从结构化缓冲解包（22bit 定点 ×1e-6，0–4.19 m 量程）。**「水底波影」的真身就是这个 exp 项**：波浪经过时水深振荡 → 水下沙直射光明暗振荡 → 移动明暗带。太阳透过水柱的 Beer–Lambert 衰减（0.721348/m，精确到反汇编字面量），乘在直射项上。
- **焦散叠加**：材质槽 **`causticsMap`**（`Sand` 材质实锤：指向资产 **`Texture3D "Caustics" 256×256×64（9 mip）`**——**烘焙体积贴图，不在引擎侧由水场现渲染**）。`Lit_Sand` PS 里
  `dcl_resource_texture3d ... t4` 后 `sample_indexable(texture3d) ... r3.xyzx, t4.yxzw, s3`：采样坐标
  `xy = worldXZ×0.25 + 流场×0.1`、`z = 0.2×(sandMap.y + cb0[113].y)`——**xy 由世界坐标+水流平流驱动，z 由时间滚动**，
  即焦散是「授权烘焙体 + 时间动画 + 水流扰动」，**不读水位场**。旧判漏因：打包 blob 剥掉资源名，t2/t3 成无名编号无法对号；
  补读材质 PPtr 后对上真实资产。
  同结构下 sandMap（t3，`worldXZ×0.3` 静态细节）提供闪沙相位。

- **天光第二盏方向光不采影、不衰减**（cb0[124]/cb0[126]）——浅水亮绿松石的来源：直射被水柱压暗但天光全额到达。
- **场身份判据**：Ground 侧 kernel 打包全为 24bit（0x00ffffff），该场为 22bit（0x003fffff）且 Water 侧 kernel 读写同款打包（含双线性+邻域差分）→ 水侧字段（水深/水量族），不是地面高度 AO。
- **闪沙**：3D 噪声值 × |太阳色| × exp(−1.731·湿度) × 坡度门控 × 湿度平滑曲线，加进 albedo 随光照走——干沙闪、湿沙哑。
- **细节 UV 被水流平流**：噪声采样坐标 += 水流分量 × 0.1——沙纹跟着水走。
- **沙色**：干/湿两色按湿度曲线插值 + 第三色（边界权重 v5.w×20）+ 噪声调制 + 坡度暗化；alpha = 世界高度 y。
  材质实测：`sandColor=(0.9255,0.8863,0.7255)`、`sandColor2=(0.9294,0.8980,0.7529)`、`wetSandColor=(0.9060,0.8104,0.6333)`。
- **场景灯实测（sharedassets0）**：太阳平行光 `(1.0,0.9709,0.9403)×1.0`；天光第二平行光
  `(0.0627,0.1333,0.2196)×1.0`（暗蓝）；另有三盏白光点光 ×2.0（装饰，不进海面口径）。
- **Lit_Sand 有深度-only PS（`mov o0,0`）+ 读地面缓冲按高度位移的 shadow VS**——地形（沙堆）是投影者，沙滩自阴影来自这里。

### 10.3 Lit_Water VS——渲染端无程序浪形

两个 vs_5_0 全文无任何三角函数/噪声：顶点 Y = 水场高度 4 邻**盒式平均**（24bit 定点 ×1e-6，−0.05 基准，max 0），法线 = 格子速度字节重建 `(vx, √(1−vx²−vz²), vz)`，水流扰动量与泡沫量随 TEXCOORD 输出给 PS。**可见水深同样是 `max(h−0.05,0)`**——h≤5 cm 时水面顶点就贴回沙面（`ground`），画面上没有薄水膜；岸线是 h=0.05 等值线。**浪面轮廓 100% 来自 SWE 场**；「尖/碎」的观感 = 长涌浅化陡化（§9）+ 细节法线贴图 + Whitewater 泡沫粒子三者叠加。

### 10.4 对本仓的裁决

- 沙 shader：直射项乘「阴影原值 × exp(−0.721×水深)」，环境项不采影；加闪沙项——已照此改 `WaterLabSand.shader`。
- 水体 shader：删 ShadowCaster（原作没有）；解析折射底色的光照与沙 shader 同式（原作底色=沙渲染纹理，等价物必须同光）——已照此改 `WaterLabOcean.shader`。
- 波源：原作是全场潮位松弛（`waveAmplitude` 标量 max）+ `obstaclesMap` 局部泵，没有行波项；
  本仓按此改为 `SurfaceUpdate`（见[实验水体 §2.2/§2.4](../管线/实验水体.md)）。

### 10.5 Lit_Whitewater PS（@12070 主变体，86 条实指令）

资源：`t0`/`t2` = `texture2d`；**`t1` = `structured`(stride 16) = 水侧模拟场**；`s0` = 比较采样器（收阴影）；`CB0[140]`。

逐段：
1. **域裁剪**：`worldXZ` 超出 `±cb0[116].xz/2` 或 `y<0` → `discard`；域内掩码存 1.0。
2. **billboard 径向 alpha**：`uv` 距中心的径向距离 → `alpha = (0.5 − ‖uv−0.5‖) × v2.z`（`v2.z` = 寿命/尺寸），
   中心实、边缘虚，无形状贴图（形状靠程序化 falloff）。
3. **域 UV 采样**：`uv = worldXZ / domainSize + 0.5`，`sample_l` 采 `t2` 得粒子色；
   按域内掩码在「采样色 ↔ `cb0[40]` 底色」间 lerp，再 ×0.95。
4. **透水衰减**：对 `t1` 水位场做 **4 邻双线性**（22bit 定点解包）得场值，乘 **`exp(−0.721348 × 场值)`**——
   与 `Lit_Sand` 直射项**同一常数**：粒子被水淹没时被水柱压暗。
5. 收主光阴影（比较采样）。

即原作白水粒子不是「贴一张白花贴图」：它是**程序化径向 falloff + 世界域遮罩 + 水柱衰减 + 阴影**的组合。


### 10.6 沙地与水下装饰的观感真值（2026-10-21，第一手材质/资产回读）

- **初始地形是高度图资产，运行时才是可塑高度场**：`Refs.worldHeightMap` 是
  **2048×1024 Texture2D `WorldHeightMap`**（`sharedassets0.assets` path 163，灰度）。
  `LayoutWorld` 建 `Brush(Operation.Add, shape=Box, easing=Constant)`，把
  `brush.patternStencil` 指向它，再 `beach.ground.Paint(...)` 刷进 Ground 层，
  随后 `UpdateTangent()`。即：**沙盘的初始高度分布来自这张贴图资产**；之后由
  Ground compute（`sandAmount`/速度/高度积分）在运行时变形。资产本身不是 mesh，
  也不是运行时唯一的高度源。
- **沙材质** `Sand`（自研 `Lit/Sand`）：`sandColor=(0.9255,0.8863,0.7255)`、
  `sandColor2=(0.9294,0.8980,0.7529)`、`wetSandColor=(0.9060,0.8104,0.6333)`
  （序列化 linear）。**湿沙只比干沙暗 2–13%**：`wetSandColor/sandColor` 的 linear
  比例 ≈ `(0.979,0.914,0.873)`，不是深棕湿带。湿沙不是独立贴图：VS 把
  `max(water−0.05,0)` 传入，PS 先 `sat×52.63` 再 `3t²−2t³` smoothstep——约 2 cm
  水膜即全湿；随后按水深 ×20 与坡度因子继续压向第三色。
- **Lit_Sand 没有层理/沙纹/卵石 albedo**：`sandMap` 指向 1024² Texture2D `Sand`。
  实测该贴图 **R 通道是逐 texel 白噪声**（mean 0.508、std 0.161、4 px 自相关≈0；
  采样 `worldXZ×0.3` → 单 texel ≈3.3 mm），只给沙粒明暗（PS 以 `normal.y` 加权，
  幅度约 ±0.2×n.y）；**G/B 是低频平滑流场**（std≈0.042、64 px 自相关 0.42），
  只驱动焦散 UV 扰动。`Strata`（512²，R 通道）只被 `BordersWaterFront` 材质使用，
  不在 Lit_Sand 里。沙面法线来自 GPU 高度场（`Lit_Sand@702.vs` 从地面缓冲解
  8-bit 法线），没有法线贴图。焦散是烘焙 `Texture3D "Caustics" 256×256×64`、
  1 byte/voxel，PS 双采样时间 lerp，强度乘 `exp(−1.731×水深)`。
- **暗颗粒是砾石粒子，不是 shader 点阵**：`Gravel` 是独立的 GPU 粒子层
  （`GravelParticle{position,age}`，走 Ground compute 的 `gravel` buffer），
  材质 `Gravel` 的 `baseColor1/2` 来自 `sandParameters.gravelColor/gravelColor2`。
  参考图里随机大小/疏密的深色小点是这些实体粒子，沙 shader 不画卵石。
- **`WorldHeightMap` 本体的灰度分布（2026-10-26 补测，`extracted/tex_..._163_...png`）**：
  2048×1024 灰度，min 0 / max 249 / mean 107 / std 49，直方图**双峰**——
  低带 [32,96) 约 40%（水域）、高带 [128,208) 约 43%（滩丘）、中间过渡带 [96,128) 仅约 9%，
  两峰陡峭分立。即初始地形的深水区与干滩是两个平台、缓坡带很窄；幅度（灰度→米）映射
  仍不可恢复（brush strength 在 ILR 反编译里被内存偏移读取打断），与上文同标 D 级。
- **水下装饰**：珊瑚/海草是 GPU Imposter 广告牌（`Plant.cs`，capacity 32768，
  sizeRange 默认 0.75），图集不透明像均色见下表；礁石/卵石/贝壳/海星是实体网格
  （Rock_01–06 473–2576 顶点、Pebble_01–06 54–67 顶点、贝壳 187–712 顶点）。
  分布为 loose 群簇（单簇 0.3–0.8 m，间距 1–3 m），浅水可辨色、深水退成暗青剪影。

| Imposter | 均色 RGB（sRGB） | 观感 |
| --- | --- | --- |
| GroovedBrainCoral | (0.687,0.440,0.128) | 橙褐脑珊瑚 |
| CarnationCoral | (0.740,0.521,0.445) | 粉珊瑚 |
| LettuceCoral | (0.610,0.560,0.431) | 灰绿生菜珊瑚 |
| Kelp | (0.678,0.526,0.221) | 橄榄褐海带 |
| Seagrass | ≈(0.36,0.46,0.21) | 绿色海草 |

- **退潮薄水膜的原作口径**：`Lit_Water@246.vs` 顶点取 4 邻 `max(h−0.05,0)` 的
  盒式平均，水高 = `ground + 可见水深`，并把该可见水深作为 `o4.w` 传给 PS；
  `Lit_Water@8682.ps` 首条即 `v4.w − 1e−5 < 0 → discard`。**PS 不逐像素重采样
  高度场**——岸线严格由顶点插值的可见水深决定；h≤5 cm 的格子水面直接贴回沙面。
- **不可恢复**：七种沙型预设（`SandType` 的 color/wetColor/sedimentColor 是运行时
  构造配置，序列化 MonoBehaviour 无 typetree）；Caustics 体素内容；`WorldHeightMap`
  的世界尺度映射（`FormatProperty.size` 未闭合；贴图本体 2048×1024 可读）。

## 11. 水体渲染代码量对照（2026-10-16，全量反汇编精读）

**原作 Lit_Water 全部程序＝1 VS + 1 PS**（blob 内两份「变体」字节级相同，非两个实现）：
VS 107 条指令、PS 119 条指令（7 次纹理采样），合计 **226 条指令、7 采样、4 张 texture2D +
1 比较采样器**。参照系：Lit_Sand PS 145 条指令 5 采样（水底渲染在原作是独立管线，
水体 PS 只采一张屏幕纹理就拿到全部「透过水看到的底」）。

**VS（107 条）逐块**：域 UV（世界 XZ÷域宽+0.5）→ 结构化缓冲 **6 次 load** 做 4 邻盒式
平均（24bit 定点 ×1e-6、−0.05 基准、max 0，第二 24bit 场累加）＝顶点高度；速度字节重建
法线 `(vx, √(1−vx²−vz²), vz)`；泡沫字节 ×1/255、流场/湿度 24bit 场随 TEXCOORD 出口。
无三角函数、无噪声、无程序浪形（§10.3 再证）。

**PS（119 条）逐块**：alpha 裁剪（水量<1e-5 discard）→ 细节法线双尺度
（WaterNormal2D ×0.05/×0.20，锯齿动差混合，**流场平流**：采样坐标 += clamp(流,±0.5)）→
深度安全屏幕折射（UV 偏移=N.xz×0.0121、Y×纵横比、折点须更远才采纳）→ InvVP 重建折射点
→ Beer–Lambert exp(−8.65617σ·dist)×waterColor → 底色×T×(0.9+0.2·ndv⁶) → 散射
lerp(ndv⁵×阴影软化×0.75) → 单一 pow(ndh,1500)×阴影原值 → 天空反射按菲涅尔混环境色 →
alpha=saturate(水量×100)。阴影 1 次比较采样；无泡沫项、无闪沙、无焦散计算
（焦散在沙侧 Lit_Sand，采样烘焙 `Texture3D Caustics`，见 §10.2）。

**本仓对照**（`WaterLabOcean.shader`：VS 位移 + 单 PS）：

| 维度 | 原作 | 本仓 | 差距判读 |
| --- | --- | --- | --- |
| 水底成像 | 屏幕折射 1 采样拿现成沙渲染 | 透明调度位 + URP 内建帧色拷贝，`SampleSceneColor` 采真沙渲染（B 路线，已落地） | ✅ 机制等价（本仓另有解析底色作为退回路径） |
| 焦散 | 采样烘焙 `Texture3D Caustics`（xy=世界XZ+水流、z=时间滚动） | η 拉普拉斯现算（5 采样） | 机制不同：原作是授权烘焙体+时间动画，不读水位场；本仓与浪同源（第十五轮外包债②收官） |
| 细节法线平流 | 流场平流（VS 出逐顶点流） | 仅时间漂移 | 未对齐残留（数据在本仓是模拟速度场，接法已明） |
| 泡沫 | PS 无（白水=Whitewater 粒子） | 同前（自造项已删） | ✅ 已对齐。本仓曾把模拟泡沫通道 lerp 进水面颜色（本表原记「本仓多出」）；实测该场在浅水架长期处于 0.25–0.9、覆盖 56% 水面，由出射色反解的水面泡沫混合权重达 **≈0.85** → 整片海面被刷成白沫，水色与折射水底全被盖掉。删除后水面立刻恢复「透底 + 体色」（第十八轮） |
| 折射深度安全门控 | 折点更远才采纳 | **已实现**：`_CameraDepthTexture` 拷贝点改到 `AfterOpaques`，折点背景更近则不偏移（r81） | ✅（光程仍用解析近似） |
| 天空反射 | 按菲涅尔混环境色 | **已补**：宏观视角 `(1−viewDir.y)⁵` × 强度 3，色值/强度按参考深带做显示域标定（r54） | ✅（显示域近似） |
| 法线合成 | 锯齿动差混合 | 双尺度相加 | 近似（原式含时间锯齿项） |
| 阴影/天光/水柱/吸收 | 同式 | 同式（常数 0.721348/8.65617/0.0121/实测灯色全对齐） | ✅ |
| 散射项权重 | 实测色 `waterScatteringColor`=(0.5684,0.3569,0) 暖橙；**权重 = NdotV⁵**（反汇编 nv·nv²·nv²） | 同（第十九轮恢复） | 第十八轮曾误改 `(1−NdotV)⁵`，依据是「参考近似俯视且水面无暖调」；实际参考是**近岸浅水暖沙调、远/深水才宝蓝**，而 `(1−NdotV)⁵` 把暖散射加到掠射深水（r53 实测深带 G 115 vs 参考 70）。r54 恢复 NdotV⁵ 后深带 `B−R=+110`（参考 +103） |

**量级结论**：原作水体表面着色的「指令预算」极小（226 条/7 采样）——小是**架构红利**
（沙底成像外包给沙管线、焦散外包给烘焙体、白水外包给粒子系统）。本仓三件均已补齐（真折射 / 焦散 / 白水粒子，
第十五、十六轮），但焦散走的是**机制不同的模拟派生替代**，白水仍未做原作的**双剔除与水下衰减**——
即「有对应物」但未逐项对齐，细则见 [水体实现覆盖率审计报告](../../../项目/审计/水体实现覆盖率审计报告-2026-10-18.md)。

## 12. 预设真值（`Refs` ScriptableObject 直读，2026-10-18）

`Refs`（`ScriptableObject`，`sharedassets0.assets`）持有 `List<Beach.WavesProperty>` / `List<Beach.WaterProperty>`
等预设；`Game.cs` 以 `configuration.wavesParameters = wavesProperty.defaultParameters` 取用。
字段布局取自 Cpp2IL 字段偏移（`WavesProperty` 序列化 80 B，`a/b/c/d` 在 +28），逐字节解出：

**`waves`（5 档浪型，`Parameters{a,b,c,d: Vector3}`）**

| 档 | a | b | c | d |
| --- | --- | --- | --- | --- |
| Regular | (0.2, 2, 0.1) | (0.16, 4, 0.15) | (0.07, 20, 0.2) | (0, 200, 0) |
| Calm | (0.127, 2, 0.02) | (0.063, 4, 0.05) | (0, 20, 0.1) | (0, 200, 0) |
| Agitated | (0.15, 2, 0.2) | (0.27, 4, 0.4) | (0.107, 20, 0.6) | (0.012, 200, 0.8) |
| Storm | (0.1, 2, 1) | (0.184, 4, 1.25) | (0.154, 9, 1.5) | (0.33, 5, 1.75) |
| Rogue | (0.1, 2, 0.1) | (0.22, 4, 0.15) | (0.154, 9, 0.2) | (0.02, 218, 2.5) |

四列对应 CPU 侧的四个波/泵分量（`Water.Run` 里 `waveAmplitude = saturate(playTime×0.05)×max(四列项)`）；
各列第二分量取值 2/4/9/20/200/218 呈「周期族」分布，第三分量为该列幅值权重。

**`waters`（4 档水体，`Parameters{color, scatteringColor, advectionFactor, kappa, damping}`）**

| 档 | color | scatteringColor | advectionFactor | kappa | damping |
| --- | --- | --- | --- | --- | --- |
| 0 | (0.898, 0.973, 1) | (1, 0.494, 0.263) | 0.2 | 1000 | 0 |
| 1 | (0.961, 0.988, 1) | (0.584, 0.322, 0) | 0.2 | 1000 | 0 |
| 2 | (0.769, 0.937, 1) | (0.824, 0.310, 0.035) | 0.2 | 1000 | 0 |
| 3 | (1, 0.675, 0.710) | (0.286, 0.667, 0.576) | 0.2 | 2 | 0 |

其中 0.584/0.322/0 即旧档所记「材质实测橙色散射色 (0.57,0.36,0)」的来源（档 1 的散射色）。

## 13. C# 侧全局 shader 属性全表（`SetGlobal*` 穷举，2026-10-18）

从 251 个反编译源文件穷举 `Shader.SetGlobal*`，**全局属性只有**：

- color：`wetSandColor`、`waterScatteringColor`、`waterColor`、`sedimentColor`、`sandColor`、`sandColor2`
- vector：`waterResolution`、`waterCellSize`、`groundResolution`、`groundCellSize`、`airResolution`、
  `airCellSize`、`voxelsResolution`、`readbackResolution`、`beachSize`、`beachMin`、`beachMax`、
  `beachExtents`、`beachCenter`、`sunDirection`、`moonDirection`、`cursorPosition`、`cursorVelocity`
- texture / buffer：`_MainTex`、`_AtmosphereTexture`、`obstaclesMap`、`giVoxels`、`_BlurredMask`、
  `waterCells`（`SetGlobalBuffer`）

外加一个 `ShaderBindings` 静态类（只装 `unity_*` 内建：`_Time`、`unity_DeltaTime`、环境光三色、
雾、相机矩阵与投影参数等）。

**结论**：`Lit/Water` PS 里的吸收系数 `cb0[132]`（σ）**不是**全局属性（也没有任何名字带
absor/extinct 的项）、不在材质 `Water` 的 saved properties 里、也不在 §12 的水体预设字段里，
而 DXBC 的 RDEF 反射块被剥离 → **静态不可取**。同理 `cb0[121]`/`cb0[131]` 的槽名也无法从
反射确认，只能按用途反推（131 乘进透射项 → 水色乘子；121 是散射混合的目标色）。



## 14. 从 Steam 高清头图反定材质（2026-10-18）

> **⚠ 本节 σ 部分已作废（2026-10-27 A 流静态终证）**：σ≡(0,0,0)（cb0[132] 无上传者，
> global-metadata 字面量全扫证据链，见水体复刻台账）。下文的 σ 通道比值/三锚点/绝对尺度
> 是给 tonemap/沙侧缺项打的补偿拟合，不得再用于实现；waterColor/scatteringColor 反定仍有效。

σ 静态取不到（§13），于是改用**成品画面**反定：Steam 客户端 `appcache/librarycache/<appid>/`
里存着官方高清头图（本机 `4894830/…/library_hero.jpg`，1920×620），是实机渲染的成品，
可直接当比色基准。方法：

1. 头图按「蓝红差」分带，逐带取水像素均值 —— 得到参考从浅滩到离岸的完整色带：
   `(157,200,171) (146,195,173) (113,177,167) (74,153,161) (46,126,146) (28,103,136) (19,83,126) (16,70,119)`；
2. 取最深带出射色与**同一张图里**的受光干沙 `(252,226,167)`，按 `出射 = 沙底 × T` 反解
   `T = (0.0036, 0.142, 0.703)`；
3. T 的**通道比值**与水深无关，是最可靠的部分：`σ_R : σ_G : σ_B = 15.9 : 5.52 : 1`。
   该比值可用参考自身交叉验证——相邻带的 T 比值给出的光深比在两两比较中一致（≈1.59）。
4. 绝对尺度靠本仓实验域的光深锚定：取本仓 `dist = 0.2 / 0.5 / ≥2 m` 分别落在参考的
   浅滩 `(157,200,171)`／中带 `(74,153,161)`／最深带 `(16,70,119)` 上（三锚点同时命中）
   ⇒ 有效 `(5.6, 1.94, 0.35)/m`。

**顺带反定出的两条事实**：

- **沙色**：参考受光干沙 `(252,226,167)`，色度 85、亮度 Y=227。本仓曾出 `(199,186,162)`、
  色度只有 36 —— **问题在色度不在亮度**，「沙跟纯白似的、没有透明感」的量测对应物就是它。
- **水面极亮像素占比**：参考浅水内 `lum>235` 只占 **0.03%**，而本仓同尺度统计是它的 ~10 倍。
  即参考的水面是「平滑水面 + 稀疏碎金」，不是「整面白噪」；这一条反定了细节法线强度。

**这条路线可复用**：任何「材质常数静态取不到」的项，只要官方有成品图，就能按
「底色 × 吸收」的物理式反解出通道比值，再用本地水深/光照锚定绝对值。

## 15. 渲染管线与后处理实锤（2026-10-26，C# 源码级）

「参考画面为什么亮/透」的管线层证据（本节全部 A/B 级源码实锤，数值另标）：

- **颜色缓冲是 HDR**：`BeachRenderPipeline.cs` `colorFormat =
  SystemInfo.GetGraphicsFormat(DefaultFormat.HDR)`——所有相机 RT 按 HDR 格式分配，
  是 tonemap/bloom 生效的前提。
- **PostProcessing Stack v2 默认开**：`BeachRenderPipelineAsset.postProcessing = true`
  （字段默认值）；`FinalBlit` 无条件构造 `PostProcessRenderContext` 并 `Render`——
  后处理是主路径，不是可选装饰。Photo 模式有独立 profile 可切换。
- **全局 PostProcessVolume + 景深**：`Game.globalPostProcessVolume` 挂全局 profile；
  `CameraRig.cs` `profile.TryGetSettings<DepthOfField>(...)`——C# 显式引用的只有 DoF
  （对焦系统开关），其余效果（Bloom/ColorGrading 等）纯走 profile 资产。
- **profile 具体数值静态不可取**：typetree 大面积剥离（`sharedassets0.assets`
  3679 个 MonoBehaviour 仅 189 个可读、`resources.assets` 603 个全剥），
  按可恢复性分级标 **D 级**——复刻时按参考画面反定，与 σ 同法。
- **体素 GI 的着色地位**：`Gi` compute（128³，`sceneVoxels → Gi → BlurGi →
  giVoxelsBlurred`）产出 `giVoxels` 全局 buffer（§13 全局表），由 Lit_* shader 采样作
  环境 irradiance——水底/沙底的环境亮度兜底就是它，不是「平光环境色」。
- **初始地形来源**（§10.6 补充）：`WorldHeightMap` 直方图双峰（40% 水域低带 /
  43% 滩丘高带 / 9% 过渡）——参考画面的构成是「深水宝蓝带 + 宽缓坡浅水」，
  深水带真实存在，其亮度由上面三层（HDR/tonemap/GI）兜住。

对本仓的含义：LDR 直出链复刻 Lit_Water/Lit_Sand 的逐指令公式是对的，
但**画面亮度结构差三件套**——HDR RT、PPv2 tonemap、体素 GI，全部缺席。
结构复刻不了的部分按 D 级反定（σ、散射强度、天空反射、GI 兜底色同属此类）。
