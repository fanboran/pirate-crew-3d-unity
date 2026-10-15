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
| UnityPy 解包 `sharedassets0.assets` | 58 个 Shader / 22 个 ComputeShader / 276 纹理 / 244 网格 / 材质属性表 清单 | 第一手 |
| LZ4 解压 shader blob + `d3dcompiler_47.D3DDisassemble` 反汇编 DXBC | `Lit/Water` 等 8 个渲染 shader 全 pass 指令流、5 个 compute 全 kernel 指令流 | 第一手 |
| compute 资产头部明文字符串提取 | 全部 kernel 名、StructuredBuffer 名、常量缓冲成员名（数值参数如 `gravityForce` 等） | 第一手 |
| [80.lv 采访](https://80.lv/articles/creating-cozy-sandbox-sandcastle-with-a-focus-on-water-sand-simulation) | 开发者口述：自研无插件、基于科研论文、水沙统一框架、双分辨率、噪声去干涉伪影 | 二手（口述） |

Il2CppDumper 不支持该作 metadata v39（Unity 6.2）——**但 Cpp2IL dev 分支（2026 版，需 .NET 10 SDK 编译）已支持**，2026-10-14 二次逆向用便携安装的 .NET 10 SDK（`dotnet-install.ps1 -Channel 10.0`，不入系统）编译 Cpp2IL 成功解析，产出 dummy DLL + `diffable-cs`（类/字段/方法签名）+ `dll_il_recovery`（恢复 IL 方法体）。**C# 侧已闭合**：模拟常量真值见 §8。复现脚本在 `external/sandcastle-ref/`（gitignored 工作台，不入库）。

## 8. 模拟常量真值（Cpp2IL ILR 从构造器初始化器恢复，2026-10-14）

| 类 | 字段 | 真值 | 备注 |
| --- | --- | --- | --- |
| Water | `gravityForce` | **10.0**（[Range 0-20]）构造默认；**16.0**（level0 场景实例） | 本仓用 9.81，量级一致 |
| Water | `evaporationFactor` | **0.15**（构造默认）；**0.1**（场景实例） | 本仓湿沙蒸发恰好也调到 0.15 |
| Water | `level` | **1.0** | 本仓 SeaLevel=0（相对地面，等价） |
| Water | `floorBlurIterations` | **3**（场景实例） | 底床平滑迭代数 |
| Water | `enableSpawn` / `enableEvaporation` | **true / true**（场景实例） | 涌浪泵与蒸发开关 |
| Water | `spawnRate` | **20.0**（场景实例） | 涌浪泵目标水量（见 §9 泵机制） |
| Water | `chunks` | Vector2Int(4, 4) 构造默认；场景实例 **(8, 8)** | 域分块 |
| Ground | `gravityForce` | **0.1** | 沙=高粘性流体，「重力」是阻尼量级 |
| Ground | `noiseFactor` | **0.0001** | 双分辨率解耦噪声 |
| Air | `gravityForce` | **10.0** | |
| Air | `evaporationFactor` / `advectionFactor` | **0.2 / 0.5** | |
| Whitewater | `capacity` | **32768** 粒子 | W4 浪花粒子的预算参考 |
| Whitewater | `frustumOverscan` | 0.1 | 视锥剔除外扩 |

场景实例真值来源：`level0` 序列化文件里 `Water` MonoBehaviour 的裸字节（UnityPy 按 m_Script PPtr 跨文件解析类名后直读，`external/sandcastle-ref/dump_configurator_raw.py`）。

五档浪型预设（`WavesType`：Regular/Calm/Agitated/Storm/Rogue）的 `Parameters`（a/b/c/d 四列 Vector3）**已定性为不可恢复**：全游戏无 Addressables/bundle（`StreamingAssets` 为空，内容全在散装 assets）；五档配置是**运行时代码构造**，而 Cpp2IL v39 的 `dll_il_recovery` 产出方法体为乱码（token 流不可辨，2026-10-14 dump `Water.Spawn` 实证）——数值常量取不出。本仓按同构「四列波 + 潮位」自推数值（见 §9 与实验水体文档），预设只作为结构参照。

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
- `HeightIntegration`：4 邻域通量 + **干湿阈值 0.05**（浅于 0.05 的格不参与流动，防干湿振荡）+ 边界 ghost 格（越界取镜像/虚拟值，`ige/movc` 分支）；
- `Advection`：半拉格朗日回溯（`sample_l` 用速度场回退采样坐标，`frc` 处理亚格偏移，含 brush wrap 变体）；
- `UpdateFloor/SubtractFloor/BlurFloor`：水深与地形高的耦合三步（`h' = max(h−ground, 0)` 类语义 + 底床平滑）。

结论：**标准 2D 浅水方程（SWE）格点求解器**，∂h/∂t = −∇·(hv)、∂v/∂t = −g∇h − 阻尼，半拉格朗日平流，干湿边界处理。与本仓 `WaterWaveField2D` 的波动方程（涟漪，无质量输运）是**不同方程**——波浪推岸、涌水、退潮这类行为只有 SWE 有。

## 4. Lit/Water 像素着色器逐段还原

指令流（ps_5_0，4 纹理 + CB0[138] + CB1[1]，材质传 `refractionFactor`）：

1. **alpha 裁剪**：水量阈值 −1e-5 以下 discard（模拟场直接驱动可见域，岸线即干湿线）；
2. **细节法线**：世界 XZ × 0.05 采样 `WaterNormal2D`（1024²）双通道拆包 `*2−1`，与顶点传入的大波法线合成后归一（`sqrt(1−t²)` 重建 y）；
3. **屏幕折射（带深度安全检查）**：UV 偏移 = N.xz × `refractionFactor`（材质实测 0.012）；采样场景深度图（`z_buffer_param` 风格 `mad+rcp` 线性化），**仅当折射采样点的深度比当前像素更远才采用偏移**（`lt→and 0x3f800000` 门控），防止折射采到前景物体；
4. **折射点世界坐标重建**：`InvVP` 矩阵（cb0[66..69]）× NDC + 1/depth → 世界坐标，`dist = length(worldRefract − worldSelf)` 得水下光程；
5. **Beer–Lambert 水体吸收/散射**：`T = exp(−σ_t·dist)`（`exp` 指令 + 8.656 缩放，RGB 三通道独立 σ_t）× `waterScatteringColor`（材质实测 **橙黄色** (0.57, 0.36, 0)——浅水散射近似底沙色），乘进折射背景色；
6. **菲涅尔**：NdotV 的 pow5；
7. **太阳高光 + 阴影**：`sample_c_lz` shadowmap 比较采样（worldToShadow 矩阵 + 边界检查），GGX 高光乘阴影；
8. 天空反射项按菲涅尔混合（环境色常量）。

### 4.1 Lit/Water PS 完整公式与覆盖率（159 行指令全解，2026-10-15）

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
| 细节法线双尺度 | ✅ 已复刻 | 程序化可平铺法线贴图（256² 四倍频值噪声）替代原作贴图，采样参数同（0.05/0.20、时间×0.8、动差混合）；流场扰动未复刻（原作 v4.xy 来自 VS，等价信息在我们是模拟速度场，暂未接入） |
| 折射 | ✅ 已复刻 | refractionFactor=0.0121 照抄 + 纵横比修正 + 深度安全门控 |
| 真光程 InvVP 重建 | ✅ 已复刻 | UNITY_MATRIX_I_VP + ComputeWorldSpacePosition |
| 吸收 8.65617·σ·dist | ✅ 结构照抄 | σ 数值不可恢复（WaterType 预设驱动，同 a/b/c/d 一档），按曲线形状自定 |
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

**抄不到的（值在 IL2CPP C# 里，metadata v39 无工具可解）**：模拟参数全部数值（`gravityForce/damping/kappa/advectionFactor`、双分辨率具体值、波源参数）——本仓按物理常识与 CFL 稳定域自定，harness 数值测试钉行为契约（波速 √(gh)、质量守恒）。

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

- **环境浪 = 涌浪泵 + 潮位松弛，都在 `UpdateAmount` kernel 里**（全 compute 唯一含三角函数的 kernel，两条 `sinc`）：
  1. **涌浪泵**：`spawnMap` 纹理圈定的注水带内 `amount += 0.15·sin(空间相位 + 8·相位)·dt`（0.15 是 shader 字面量）——振荡注水把水「泵」进海盆，浅化后自然陡化成卷浪；CPU 侧 `Water.Spawn()` 往 `spawnMap` 写目标水量（场景实例 `spawnRate=20`）；
  2. **潮位松弛**：每格水面高程向 `seaLevel + amp·sin(相位)` 松弛（下限 clamp −0.5、目标水量下限 0.05），松弛率是**每格 8bit 权重 ÷15**——空间可变权重即波形整形；
  3. **蒸发**：`amount -= damping·evaporationFactor·0.001·exp(-0.72·ground)·dt`——高地蒸发快（滩面比海盆干得快）。
- **`Paint` kernel**：CPU 写 `input` 缓冲（stride 16，24bit 定点），kernel 双线性溅射进水场——笔刷/泼水/造波机道具（`WaveGenerator`）的注水通道，非环境浪。
- **渲染网格无三角函数**：`Lit/Water` 两个 vs 全无 `sinc`——海面起伏全部来自模拟场，无「视觉波」叠层。
- **`WaterCell.wave`/`tangent` 字段**：`UpdateTangent` 只做邻域差分（法线用），无波形生成。

结论：Sandcastle 的「不规律海浪」不是多列 Gerstner 叠加，而是**振荡泵 + 每格可变松弛 + 地形浅化**三者涌现——泵的 sin 相位带空间项（λ≈41.6 m 单条长涌浪）、四列 `WavesProperty` 参数在 CPU 侧改泵/潮位的振幅与节奏（数值不可恢复，见 §8），渲染端无程序浪形（§10）。本仓复刻取同构机制：主涌浪放到 λ=40 m（长涌在缓坡上浅化陡化成 bore 是「不规律」的主体），互质短波列只留小幅次级纹理，全场潮位弱松弛承担「浪成组上岸」。

## 10. Lit_Sand PS 与 Lit_Water VS 实锤（水底波影与浪形，反汇编逐条，2026-10-15）

### 10.1 Lit_Water 无 ShadowCaster——水面不投影

`Lit_Water.blob` 全量 DXBC 容器扫描：只有 4 个程序（2 VS 变体 + 2 PS 变体），无深度-only pass。**原作水面不往任何东西上投影**——沙滩上/水底看到的「波影」不来自 shadowmap。

### 10.2 Lit_Sand PS（@34958 最全变体，逐段）

- **直射项 = ndl × 太阳色 × (阴影比较采样原值 × exp(−0.721 × 水侧模拟场))**：阴影无 PCF 无软化（`sample_c_lz` 点比较 + 视锥外恒 1），exp 项的场按**逐像素 4 邻双线性**从结构化缓冲解包（22bit 定点 ×1e-6，0–4.19 m 量程）。**「水底波影」的真身就是这个 exp 项**：波浪经过时水深振荡 → 水下沙直射光明暗振荡 → 移动明暗带。太阳透过水柱的 Beer–Lambert 衰减（0.721/m ≈ 半深 0.96 m），乘在直射项上。
- **天光第二盏方向光不采影、不衰减**（cb0[124]/cb0[126]）——浅水亮绿松石的来源：直射被水柱压暗但天光全额到达。
- **场身份判据**：Ground 侧 kernel 打包全为 24bit（0x00ffffff），该场为 22bit（0x003fffff）且 Water 侧 kernel 读写同款打包（含双线性+邻域差分）→ 水侧字段（水深/水量族），不是地面高度 AO。
- **闪沙**：3D 噪声值 × |太阳色| × exp(−1.731·湿度) × 坡度门控 × 湿度平滑曲线，加进 albedo 随光照走——干沙闪、湿沙哑。
- **细节 UV 被水流平流**：噪声采样坐标 += 水流分量 × 0.1——沙纹跟着水走。
- **沙色**：干/湿两色按湿度曲线插值 + 第三色（边界权重 v5.w×20）+ 噪声调制 + 坡度暗化；alpha = 世界高度 y。
- **Lit_Sand 有深度-only PS（`mov o0,0`）+ 读地面缓冲按高度位移的 shadow VS**——地形（沙堆）是投影者，沙滩自阴影来自这里。

### 10.3 Lit_Water VS——渲染端无程序浪形

两个 vs_5_0 全文无任何三角函数/噪声：顶点 Y = 水场高度 4 邻**盒式平均**（24bit 定点 ×1e-6，−0.05 基准，max 0），法线 = 格子速度字节重建 `(vx, √(1−vx²−vz²), vz)`，水流扰动量与泡沫量随 TEXCOORD 输出给 PS。**浪面轮廓 100% 来自 SWE 场**；「尖/碎」的观感 = 长涌浅化陡化（§9）+ 细节法线贴图 + Whitewater 泡沫粒子三者叠加。

### 10.4 对本仓的裁决

- 沙 shader：直射项乘「阴影原值 × exp(−0.721×水深)」，环境项不采影；加闪沙项——已照此改 `WaterLabSand.shader`。
- 水体 shader：删 ShadowCaster（原作没有）；解析折射底色的光照与沙 shader 同式（原作底色=沙渲染纹理，等价物必须同光）——已照此改 `WaterLabOcean.shader`。
- 波源：主涌浪 λ=40 m（对齐泵的 41.6 m），见 §9 结论与 `WaterLabRules.WaveTrains`。
