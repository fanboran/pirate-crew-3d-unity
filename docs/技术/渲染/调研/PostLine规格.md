# PostLine 规格

> **这份文档是什么**：参照库 `external/ref/unity-isometric-pixel-pipeline`（代号 **PostLine**，命名口径见
> [管线/双风格切换.md](../管线/双风格切换.md) §1）的**源码级移植规格**——后处理 5 pass、ToonLit、GrassBlade、
> 昼夜/云影的全部公式与实测参数、移植到本仓 2:1 像素比（960×540 @1080p）的换算、已知坑与落点清单。
> 并行的另一参照库（Sloane，代号 CellShade）不在本文范围（其规格即 [v3蓝本.md](v3蓝本.md)）。
>
> | 项 | 值 |
> |---|---|
> | 参照库路径 | `external/ref/unity-isometric-pixel-pipeline/`（**参照库不入库**，复现命令见 [交接与恢复指南](../../../项目/交接与恢复指南.md)） |
> | HEAD | `f08bc6bf34b81c265171972610b23a8ef7c8d019`（2026-05-16，"Update: day/night cycle, updated shaders and documentation"；工作区仅 EditorBuildSettings/ShaderGraphSettings 两处无关改动与一个出图临时脚本 `Assets/Editor/V3DemoCapture.cs`） |
> | Unity / URP | Unity 6000.3.5f2（`ProjectSettings/ProjectVersion.txt:1`）+ URP/core 17.3.0（`Packages/manifest.json`、PackageCache `package.json`） |
> | 许可 | MIT（Copyright (c) 2026 bababuyyy，`LICENSE`）——移植须在文件头保留其版权与许可声明 |
>
> **引用缩写**：`R/` = 参照库仓库根；`URP17/` = 参照库 `Library/PackageCache/com.unity.render-pipelines.universal@1e87cf1dccb8/`；`CORE17/` = 同目录 `com.unity.render-pipelines.core@ce614113b3ba/`；`URP14/` = 本仓 `pirate-crew/Library/PackageCache/com.unity.render-pipelines.universal@14.0.12/`。
> **标注**：【实测】= 参照库源码/资产读出的既成事实（可复核）；【换算】= 本文按公式推演的数值；【提案】= AI 生成的移植建议，未经创始人确认。

---

## 1. 总览：观感机制

一句话：**几何与着色都在全分辨率正常跑（前向 ToonLit），然后把颜色降采到内部低分辨率（640×360），
在这一档上做屏幕空间描边（剪影=最近邻色压暗、棱线=方向对比度提亮），合成后用 fwidth+smoothstep 锐利放大上屏**。
描边在低分辨率上算，所以线宽恒等于 1 个低分辨率像素（`R/README.md:31-44`）。

与 CellShade 的分野：PostLine 不改物体 pass（`UniversalForward` 照常出色）、没有 G-buffer/连通域，
"像素化"只发生在后处理链；CellShade 是物体 pass 写数据、着色搬进低分辨率域（`双风格切换.md:18-19`）。

**README 与源码的三处出入**（以源码为准）：

| README 说法 | 源码事实 |
|---|---|
| Downsample 用 bilinear（`R/README.md:56`） | `Blitter.BlitTexture(..., bilinear: false)` → **Nearest pass**（`R/Assets/Rendering/RenderFeatures/PixelRendererFeature.cs:170`；`CORE17/Runtime/Utilities/Blitter.cs:519`、`BlitShaderPassNames.Nearest=0` `:69-72`；`CORE17/Runtime/Utilities/Blit.hlsl:100-102` `FragNearest` = `sampler_PointClamp`）。即**降采是点采样，取每块左下角那一个全分辨率纹素** |
| 材质目录 `Materials/Toon/`、纹理 `CloudNoise_v5.png` 等（`R/README.md:233-263`） | 实际是 `Assets/Toon/`、`Assets/Materials/Pipeline/`；噪声图为 `CloudNoise.png`/`CloudNoise2.png`/`Albedo2Noise.png`/`Albedo3Noise.png`/`WindNoise.png`（256×256 灰度 PNG） |
| SharpUpscale shader 名 `Hidden/SharpUpscale`（`R/README.md:319`） | 实际 `Custom/SharpUpscale`（`R/Assets/Shaders/PostProcess/SharpUpscaleShader.shader:1`） |

---

## 2. 后处理 5 pass（PixelRendererFeature）

### 2.0 骨架

`R/Assets/Rendering/RenderFeatures/PixelRendererFeature.cs`（下称 PRF）。Unity 6 Render Graph API（`RecordRenderGraph`，:102）。

- 注入条件：只对 `CameraType.Game` / `SceneView` 入队（:74-80）。
- `renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing`（:68-71）——**在 URP 后处理（Tonemap/Bloom/Vignette）之后**才像素化（对照：参照库相机 `m_RenderPostProcessing: 1`、`R/Assets/Scenes/Test/ShaderTest.unity:520`）。
- `requiresIntermediateTexture = true`（:89）+ `ConfigureInput(Color|Depth|Normal)`（:92）→ URP 保证中间颜色纹理 + `_CameraDepthTexture` + `_CameraNormalsTexture` 存在（URP14 对应机制：pass input 汇总 → `requiresColorTexture`/`requiresNormalsTexture`，`URP14/Runtime/UniversalRenderer.cs:1460-1475`）。
- 材质缺失时的降级：outline 或 composite 材质为空 → 跳过 Pass2/3，直接用无材质 blit 把低分辨率色拷进合成目标（:142、:242-258）；upscale 材质为空 → Pass4 退化为普通 blit（:272-275）。

**四张临时纹理**（:114-140）：

| 纹理 | 尺寸 | 格式 | FilterMode | 用途 |
|---|---|---|---|---|
| `_ColorCopy` | = 相机目标（全分辨率） | 复制相机目标描述，MSAA=None（:114-118） | （继承） | Pass0 备份，避免读写同一目标 |
| `_LowResColor` | 设置的 width×height | `cameraTargetDescriptor.graphicsFormat`（参照库 HDR 开 → B10G11R11_UNorm，见 §9 坑 8；本仓 HDR 关 → B8G8R8A8_UNorm） | **Bilinear**（:121-129） | 低分辨率场景色；Bilinear 是给 SharpUpscale 的 `SAMPLE_LOD` 用的（:120 注释） |
| `_LowResOutline` | 同上 | **`R8G8B8A8_UNorm`**，**FilterMode.Point**（:132-136）——注释明说"Point 防串色、RGBA 的 A 通道载线强度是硬要求" | Point | 描边遮罩（RGB=线色，A=强度） |
| `_LowResComposite` | 同上 | 同 `_LowResColor`，Bilinear（:139-140） | Bilinear | 合成结果，交给 Pass4 放大 |

执行顺序与伪代码（:144-277）：

```
Pass0 "CopyColor"        colorCopy   ← cameraTarget                    # 无材质 blit（:145-157）
Pass1 "Downsample_Color" lowResColor ← colorCopy                       # 无材质 blit，bilinear:false=Nearest（:160-172）
Pass2 "Outline_LowRes"   lowResOutline ← material(outline) 采样 colorCopy
                             # 全部参数逐条 SetFloat/SetVector 后经 Blitter 带材质 blit（:178-229）
                             # builder.SetGlobalTextureAfterPass(destination, _OutlineTexture)（:220）
                             # builder.AllowPassCulling(false)（:223，防止被剔除）
Pass3 "Composite_Outline" lowResComposite ← BlitMaterialParameters(lowResColor→lowResComposite, compositeMat, pass0)
                             # renderGraph.AddBlitPass（:232-240）
Pass4 "Upsample_Sharp"   cameraTarget ← BlitMaterialParameters(lowResComposite→cameraTarget, upscaleMat, pass0)（:261-277）
```

⚠ **URP14 移植注意**：17.3 的 `AddBlitPass(material)` 只声明 source/destination 两个依赖
（`CORE17/Runtime/RenderGraph/RenderGraphUtilsBlit.cs:938-1055`，`UseTexture(source)` + `UseTexture(destination, Write)`），
Pass3 采样的 `_OutlineTexture` 是 `SetGlobalTextureAfterPass` 注入的全局——**不进依赖图**，Render Graph 的
裁剪/验证对它不可见。参照库靠 `AllowPassCulling(false)`（:223）兜 Pass2；Pass3 若在 17.x 被异步裁剪排程
仍可能读到未就绪的贴图。URP14 没有 Render Graph 主路径，无此坑，但等价约束是：**Pass3 必须排在 Pass2 之后
且两趟都在同一 `ScriptableRenderPass.Execute` 里顺序录制**。

### 2.1 Pass0 CopyColor / Pass1 Downsample

两趟都是无材质 `Blitter.BlitTexture(cmd, src, Vector4(1,1,0,0), mipLevel:0, bilinear:false)`（:155、:170）。
scaleBias (1,1,0,0) = UV 1:1 映射；`bilinear:false` 选 Blit shader 的 `FragNearest`（PointClamp）。
**Pass1 实为点采样降采**：每个低分辨率像素取全分辨率块的一个角纹素（非块中心）。对本仓 2:1（偶数 k）
意味着取的是偶数行/列——与连通域 BlockPatch 锚点选择（偏 (1,1) 角格，渲染篇 §5）同族问题，见 §8。

### 2.2 Pass2 Outline（剪影 + 棱线）

`R/Assets/Shaders/PostProcess/OutlineShader.shader`（下称 OS）。`Cull Off ZWrite Off ZTest Always`（:5-6）；
输入采样 `SampleSceneDepth` / `SampleSceneNormals`（DeclareDepthTexture.hlsl:16、DeclareNormalsTexture.hlsl:17）——
**深度/法线仍是全分辨率引擎缓冲**，只是采样 UV 按 1 内部像素步进（:59 `step_uv = _BlitTexture_TexelSize.xy * _KernelRadius`；
`_BlitTexture` 此处= blit 源 `_ColorCopy`，TexelSize 即全分辨率纹素尺寸——kernel 步长实际是**全分辨率纹素 × kernelRadius**，
按内部分辨率步进靠的是 UV 差 1/width 这一事实本身，与 TexelSize 数值无关，见 §9 坑 10）。

顶点用 Blit.hlsl 的 `Vert`。片元完整流程（:56-163）：

```
uv = texcoord; step = _BlitTexture_TexelSize.xy * _KernelRadius              # OS:59

# ① 3×3 网格：9 点视图空间位置 + 视图空间法线（OS:66-77）
for k in 0..8:
    off = (k%3-1, k/3-1); uvk = uv + off*step
    rawDepth = SampleSceneDepth(uvk)
    vp[k] = ReconstructViewPos(uvk, rawDepth)          # clip→view：mul(UNITY_MATRIX_I_P, float4(uv*2-1, d, 1))，y 按 UNITY_UV_STARTS_AT_TOP 翻转，除 w（OS:45-54）
    n_ws    = SampleSceneNormals(uvk)                  # 世界空间法线
    vn[k]   = mul((float3x3)UNITY_MATRIX_V, n_ws)      # →视图空间；视图 +z 朝远离相机方向，故 vn.z 越大越近（OS:76）

# ② 剪影角度自适应阈值（OS:80-82）
facing  = 1 - vn[4].z                                  # 0=正对相机，越大越掠射
t01     = saturate((facing - _AngleZCutoff) / (1 - _AngleZCutoff))
z_thresh = _ZDeltaCutoff * (t01 * _AngleZScale + 1)    # 掠射面阈值放大 _AngleZScale 倍，防斜坡全长出线

# ③ 棱线：4 正邻方向对比度（OS:84-114）
cardinals = {1,3,5,7}                                  # 上/左/下/右（k%3, k/3 排布下 = 右/上/左/下，对称无影响）
for i in 0..3:
    depthDifference    += clamp(vp[c_i].z - vp[4].z, 0, 1)   # 四邻比中心近多少（累计，截 [0,1]）
    invDepthDifference += clamp(vp[4].z - vp[c_i].z, 0, 1)   # 中心比四邻近多少
invDepthDifference = clamp(invDepthDifference, 0, 1)
invDepthDifference = clamp(smoothstep(0.9, 0.9, invDepthDifference) * 10, 0, 1)   # OS:99 —— 边沿相等的 smoothstep = 阶跃；中心显著更近(≥0.9 视图米)→1
depthDifference    = smoothstep(_DepthDiffLow, _DepthDiffHigh, depthDifference)   # OS:100 —— 算出后未被使用（死值，§9 坑 4）
d_i    = 1 - dot(vn[4], vn[c_i])                        # 各正邻法线差
contrast = max(abs(d_0 - d_3), abs(d_1 - d_2))          # 方向对比度：对面两邻差值取 max（OS:108-111）——曲面两侧等量变化→≈0，真棱线单侧剧变→大
normalDifference = smoothstep(_NormalSmoothLow, _NormalSmoothHigh, contrast)      # OS:113
crease_weight = saturate(normalDifference - invDepthDifference)                   # OS:114 —— 近剪影带内抑制棱线

# ④ 剪影：8 邻域深度不连续（OS:117-140）
has_line = false; closest = 4; max_z = vp[4].z
for idx in 8 邻:
    if vp[idx].z - vp[4].z > z_thresh: has_line = true      # 邻居比中心近超过阈值 → 中心在剪影远侧
    if vp[idx].z > max_z: max_z = vp[idx].z; closest = idx  # 记录最近邻（线色来源）

# ⑤ 输出 RGBA 遮罩（OS:142-162）
if has_line:   rgb = SAMPLE(_BlitTexture, uv[closest]).rgb * _LineDarken;  a = _LineAlpha
elif crease_weight > 0: rgb = SAMPLE(_BlitTexture, uv).rgb * (1 + _CreaseBrighten); a = crease_weight * _CreaseAlpha
else:          rgb = 0; a = 0
```

要点：
- **线色 = 最近邻像素色 × `_LineDarken`**（:150-151），不是固定墨色——线自带物体固有色。
- **棱线 = 中心像素色 × `(1+_CreaseBrighten)`**（:157-158），alpha 由对比度平滑权重调制。
- 剪影优先于棱线（`else if`，:154）：同一个像素不会既压暗又提亮。
- 阈值 `z_thresh` 与深度差的比较发生在**视图空间米**（正交投影重建，:47-53），与分辨率无关，只与世界尺度和相机角度有关（量化见 §8）。
- `_LineTint`/`_CreaseTint`/`_FlipPalettes`/`_LineOverlay`/`_CreaseOverlay`/`_ConvexCutoff`/`_CreaseFeather`/`_ConcaveCutoff`/`_ConcaveZCutoff` 在 shader 里**声明了但没有使用**（:31-42 注释自认"Preserved for C# compatibility"）——C# 侧每帧照常下发（PRF:178-202），是死参数。

### 2.3 Pass3 Composite

`R/Assets/Shaders/PostProcess/CompositeShader.shader`：采样 `_BlitTexture`（=低分辨率色）与全局 `_OutlineTexture`，
`result = lerp(color.rgb, outline.rgb, outline.a)`，alpha 恒 1（:26-34）。即遮罩 alpha 直接当混合因子。

### 2.4 Pass4 SharpUpscale

`R/Assets/Shaders/PostProcess/SharpUpscaleShader.shader`：

```
uv = texcoord + _PixelPanOffset.xy                          # :25 亚纹素平移补偿（由相机控制器下发，§7.1）
px   = _BlitTexture_TexelSize.xy                            # :27 1/width, 1/height（画布纹素）
fw   = clamp(fwidth(uv) / px, 1e-5, 1.0)                    # :29 屏幕像素:画布纹素的密度比（≤1）
grid = uv / px - 0.5 * fw                                   # :30
blend = smoothstep(1 - fw, 1, frac(grid))                   # :31 纹素边界处的平滑窗口
finalUV = (floor(grid) + 0.5 + blend) * px                  # :32 块内坐标 + 边界过渡
color = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, finalUV, 0)   # :34
```

性质：整数倍放大时 `fwidth(uv)/px = 1/k ≤ 1`，过渡窗口宽度 = 1 屏幕像素，介于 Point（k=1 退化）与 Bilinear
之间——"数学上平滑的锐利边"。`_PixelPanOffset` 是 UV 空间量（画布 0..1），非纹素整数倍，产生**亚纹素级
滚动补偿**（像素网格静止、画面内容平滑移动）。采样用 LinearClamp + LOD0。

### 2.5 独立版 OutlineRendererFeature（不复用）

`R/Assets/Rendering/RenderFeatures/OutlineRendererFeature.cs` 是**另一个**全分辨率描边 feature
（`RenderPassEvent.BeforeRenderingPostProcessing` :54；两趟 blit 临时纹理来回拷 :96-152；无 Composite/上采样）。
**它没有被挂到渲染器上**：`R/Assets/Rendering/PC_Renderer.asset:27-29` 的 `m_RendererFeatures` 只含 SSAO（m_Active:0）
与 PixelRendererFeature；该 asset 文件内残留的 OutlineRendererFeature 块（:59-88，kernelRadius=2）不在列表里。
README 亦标注 "Standalone version (not used in 5-pass pipeline)"（`R/README.md:243`）。移植时忽略。

---

## 3. ToonLit.shader

`R/Assets/Shaders/ToonLighting/ToonLit.shader`，`Shader "Custom/ToonLit"`（:1），Opaque/Geometry（:38-44）。
4 pass：`UniversalForward` / `ShadowCaster` / `DepthOnly` / `DepthNormals`，CBUFFER 全 pass 一致（SRP Batcher，:93-115 等）。

### 3.1 参数面板（Properties，:3-34）

| 参数 | 默认 | 范围 | 语义 |
|---|---|---|---|
| `_BaseColor` | (1,1,1,1) | — | 固有色（无贴图，纯色） |
| `_HighlightColor` | (1,1,1,1) | — | Palette 高光色 |
| `_MidtoneColor` | (0.7,0.7,0.7,1) | — | Palette 中间色（**声明但 frag 未用**，:246 用 albedo 顶替） |
| `_ShadowColor` | (0.3,0.3,0.4,1) | — | Palette 阴影色 |
| `_UsePalette` | 0 | Toggle | 调色板模式开关 |
| `_Cuts` | 3 | 1..8 | 色带档数 |
| `_Steepness` | 1 | 1..8 | 光照斜率 |
| `_Wrap` | 0 | -2..2 | 半兰伯特偏移 |
| `_ThresholdGradientSize` | 0.2 | 0..1 | 色带边界的平滑带宽度（占一档的比例） |
| `_UseDither` | 0 | Toggle | 4×4 Bayer 抖动开关 |
| `_DitherStrength` | 0.1 | 0..1 | 抖动幅度 |
| `_Color2` / `_Noise2` / `_Noise2Scale` / `_Noise2Threshold` | (0.3,0.5,0.2) / "white" / 0.005 / 0.604 | scale 0..1 | Patch 第 1 层 |
| `_Color3` / `_Noise3` / `_Noise3Scale` / `_Noise3Threshold` | (0.25,0.45,0.15) / "white" / 0.003 / 0.661 | scale 0..0.1 | Patch 第 2 层 |

全局量（非材质）：`_CloudNoise`/`_CloudScale`/`_CloudWorldY`/`_CloudSpeed`/`_CloudContrast`/`_CloudThreshold`/
`_CloudDirection`/`_CloudShadowMin`/`_CloudDivergeAngle`/`_CloudLightDirection`/`_CloudPower`（:78-88）与
`_GlobalAmbientColor`（:90）。

关键字：`_MAIN_LIGHT_SHADOWS(_CASCADE/_SCREEN)`、`_SHADOWS_SOFT`、`_FORWARD_PLUS`（:55-57）。

### 3.2 ForwardLit 公式（:179-261）

```
# ① Patch 系统：世界 XZ 噪声选色（:181-186）
albedo = _BaseColor
if tex2D(_Noise2, ws.xz * _Noise2Scale).r > _Noise2Threshold: albedo = _Color2     # 后判优先
if tex2D(_Noise3, ws.xz * _Noise3Scale).r > _Noise3Threshold: albedo = _Color3     # 覆盖 Color2

# ② 光照（:188-192）
L = GetMainLight(TransformWorldToShadowCoord(ws));  N = normalize(normalWS)
diffuse = (dot(N, L.direction) + _Wrap) * _Steepness

# ③ 云影（取 min，先于色带，:194-195）
diffuse = min(diffuse, GetCloudNoise(ws))

# ④ Bayer 抖动（:196-202）
if _UseDither > 0.5:
    b = Bayer4x4(input.positionHCS.xy)                    # SV_Position=全分辨率像素坐标（:199）
    diffuse = clamp(diffuse + (b - 0.5) * _DitherStrength * 0.25, 0, 1)

# ⑤ 色带量化（:204-236）
cut = 1 / _Cuts
stepped = saturate(diffuse + GLSLmod(1 - diffuse, cut))            # 基础量化：向上取档
if _ThresholdGradientSize > 0:
    k = floor(diffuse / cut + 0.5); th = k * cut                    # 最近档阈值
    if 0 <= k <= _Cuts:
        halfW = 0.5 * cut * _ThresholdGradientSize
        blend = smoothstep(th - halfW, th + halfW, diffuse)         # 边界平滑带
        stepped = saturate(lerp(th, min(th + cut, 1), blend))       # 过带后跳到下一档
    else:
        stepped = saturate(ceil(diffuse / cut) * cut)

# ⑥ 阴影与最终色（:238-260）
shadow = L.distanceAttenuation * L.shadowAttenuation
lit    = stepped * shadow
if _UsePalette > 0.5:
    color = albedo * lerp(_ShadowColor, _HighlightColor, lit)       # :248（MidtoneColor 未参与）
else:
    color = albedo * (stepped * L.color * shadow + _GlobalAmbientColor.rgb)   # :255-257
```

说明：
- **`GetCloudNoise`**（:130-151，草 shader 同式 :201-224）：从世界点沿 `_CloudLightDirection`（归一化）投到
  `_CloudWorldY` 高度平面：`t = (_CloudWorldY - ws.y) / _CloudLightDirection.y`（y 分量 <0.05 时直接返回 1 防除零，:133）；
  `hitPos = ws + t * dir`；两层同图异采样：`s1 = tex(_CloudNoise, hitPos.xz/_CloudScale + t·v·dir1)`、
  `s2 = tex(_CloudNoise, hitPos.xz/(_CloudScale·0.8) + t·v·dir2·0.89·1.047)`，`dir1/2` 为 `_CloudDirection.xy` 旋转
  ±`_CloudDivergeAngle`；`v = pow(clamp(((saturate(s1·s2 + _CloudThreshold) - 0.5)·_CloudContrast + 0.5) + _CloudThreshold, _CloudShadowMin, 1), _CloudPower)`。
- **Bayer 矩阵**（:154-168）：标准 4×4，`bayer[y*4+x]/16`，索引取 `fmod(SV_Position.xy, 4)`。
- **`_GlobalAmbientColor` 语义**：`Shader.SetGlobalColor` **不做 sRGB→线性转换**（直传 `(Vector4)value`，UnityCsReference
  `Shader.cs` 两个 overload）——Gradient 资产里存的数值就是 shader 线性运算的加数。场景正午环境 (0.149,0.149,0.2)
  在线性语义下只相当于 sRGB 观感的 ≈(0.019,0.019,0.033)【换算】，这是参照库暗部极暗的直接原因。
- **Patch 噪声是 sRGB 导入的灰度图**（§6.7），采样值为线性化后的值——阈值比较发生在**线性域**【实测】。

### 3.3 其余三 pass

- **ShadowCaster**（:265-327）：`ApplyShadowBias(positionWS, normalWS, _LightDirection)` 后变换，frag 返回 0（:318-325）。
- **DepthOnly**（:329-386）：只变换，frag 返回 0。
- **DepthNormals**（:388-449）：`return half4(normalize(normalWS) * 0.5 + 0.5, 0)`（:446）。
  ⚠ **法线编码错配**：URP `_CameraNormalsTexture` 首选 `R8G8B8A8_SNorm`（`URP17/Runtime/Passes/DepthNormalOnlyPass.cs:64-72`；
  URP14 同 `:49-57`）。SNorm 对写入值 v∈[-1,1] 的存取是恒等（存 `round(v·127)`、取 `raw/127`），
  **不会**把 UNorm 惯例的 `n*0.5+0.5` 还原成 n → Outline 读到的法线整体带 +0.5 偏置，"角度自适应阈值"的
  `facing = 1 - vn.z` 被系统性抬高且**随相机方位角变化**（实效阈值推演见 §8.3）；SNorm 不可用回退 Float 格式时
  同样带偏置。URP 自带 Lit 的 DepthNormals 直写归一化法线不偏置（`URP17/Shaders/LitDepthNormalsPass.hlsl:142`）。
  **本仓移植必须直写 `normalize(normalWS)`**【提案】。

---

## 4. GrassBlade.shader

`R/Assets/Shaders/ToonLighting/GrassBlade.shader`，`Shader "Custom/GrassBlade"`（:1），
TransparentCutout/AlphaTest 队列、**`DisableBatching = True`**（:57-64）。**只有 2 个 pass**（ForwardLit + ShadowCaster），
刻意不写 DepthOnly/DepthNormals——每根草自己出描边会变视觉噪声（`R/README.md:418`）。

### 4.1 参数（Properties，:3-53）

| 组 | 参数（默认） |
|---|---|
| 造型 | `_GrassTexture`（草精灵，alpha 裁切）、`_BaseColor`(0.35,0.55,0.2)、`_Cutoff`(0.5) |
| Patch | 与 ToonLit 同名同默认（`_Color2/_Noise2/_Noise2Scale/_Noise2Threshold/_Color3/_Noise3/...`，:9-18） |
| 点缀 Accent×2 | `_AccentTexture1/2`、`_AccentColor1`(0.4,0.6,0.1)/`_AccentColor2`(0.5,0.7,0.15)、`_AccentFrequency1`(0.001, 范围0..0.05)/`_AccentFrequency2`(0.1)、`_AccentHeight1/2`(0.5)、`_AccentScale1/2`(1.0)，:20-32 |
| 色带 | `_Cuts`(3)/`_Steepness`(1)/`_Wrap`(0)/`_ThresholdGradientSize`(0.2)，:34-37 |
| 风 | `_WindNoise`("gray")、`_WindNoiseScale`(0.071, 范围0..0.2)、`_WindNoiseSpeed`(0.025)、`_WindNoiseThreshold`(0.365, -1..1)、`_WindDirection`((0,1))、`_WindSwayAngle`(60°, 0..180)、`_NoiseDivergeAngle`(10°, 0..45)，:39-46 |
| 其他 | `_FakePerspectiveScale`(0.3, -0.15..0.6)、`_UseDither`(0)/`_DitherStrength`(0.1)，:49-52 |

实例化数据：`UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainNormal)`（:163-165）——地面法线由 spawner 逐实例注入。
关键字：`multi_compile_instancing` + 主光阴影族（:78-81）。

### 4.2 ForwardLit（:66-391）

```
# 顶点（:226-293）
instancePosWS = UNITY_MATRIX_M._m03_m13_m23                       # 实例根世界位（:232）
scaleX/scaleY = 模型矩阵第一/二列的长度（:233-234）
seed1 = HashPos(instancePosWS.xz); seed2 = HashPos(instancePosWS.xz + (π,π))   # frac(sin(dot(p,(12.9898,78.233)))*43758.5453)（:167-170,235-237）
if seed1 < _AccentFrequency1: posOS.y += _AccentHeight1; posOS *= _AccentScale1   # 点缀变形（:239-249）
elif seed2 < _AccentFrequency2: 同上第二套
# 风（:251-267）
dir1/2 = _WindDirection.xy 旋转 ±_NoiseDivergeAngle
w1 = tex2Dlod(_WindNoise, instancePosWS.xz*_WindNoiseScale      + _Time.y*_WindNoiseSpeed*normalize(dir1)).r
w2 = tex2Dlod(_WindNoise, instancePosWS.xz*_WindNoiseScale*0.8  + _Time.y*_WindNoiseSpeed*(normalize(dir2)*0.89 + (1.047,1.047))).r
wind = saturate(w1*w2 + _WindNoiseThreshold); wind = (wind - 0.5) * 2      # [-1,1]
windAngle = wind * radians(_WindSwayAngle) * 0.5
posOS.xy 绕原点旋转 windAngle（:271-275）
# Billboard（:277-282）
positionWS = instancePosWS + cameraRight * posOS.x * scaleX + cameraUp * posOS.y * scaleY   # 全相机对齐 billboard（含 up）
normalWS = _TerrainNormal（长度<0.1 时取 (0,1,0)，:289-290）
```

```
# 片元（:295-389）
# Fake perspective（:300-306）
uv.x -= 0.5
fp = wind * _FakePerspectiveScale * dot(-UNITY_MATRIX_V[2].xyz 的水平分量, normalize(_WindDirection.xy))
uv.x *= uv.y * fp + 1.0;  uv.x += 0.5;  uv.x = clamp(uv.x, 0, 1)     # 顶端随风摆动的梯形畸变（uv.y=1 在叶尖）
# 选图与配色（:309-331）
accent1/accent2/草叶 按 seed 分支：tex2D + clip(a - _Cutoff)；草叶 albedo 走与地面相同的 Patch（采样点=实例根 xz）
# 光照（:334-348）
billboardWS = instancePosWS + (0, 0.15, 0) + ((seed1,seed2) - 0.03) * 0.03    # 采样点抬升 + 微抖，防 z-fight/走样（:335-336）
L = GetMainLight(TransformWorldToShadowCoord(billboardWS))
diffuse = (dot(N_terrain, L.direction) + _Wrap) * _Steepness
diffuse = min(diffuse, GetCloudNoise(instancePosWS))                          # 云影取实例根位置
(+ 可选 Bayer，同 ToonLit :344-349)
色带量化：与 ToonLit 逐行相同（:351-383）
final = albedo * (stepped * L.color * L.shadowAttenuation + _GlobalAmbientColor)   # 无 Palette 分支（:385-388）
```

### 4.3 ShadowCaster（:393-571）

与 ForwardLit 相同的实例/风/billboard 顶点变换（:486-542），法线固定传 `(0,1,0)` 进 `ApplyShadowBias`（:543）；
frag 按 seed 选同一张图 `clip(a - _Cutoff)`（:549-568）。草投影，且 spawner 关闭 cast（§7.4）→ 该 pass 实际不生效。

---

## 5. 材质清单与实测值

### 5.1 Toon 材质（`R/Assets/Toon/*.mat`，shader GUID `e3231bce...`=ToonLit）

所有材质 `_Cuts=3`、`_Steepness=1`、`_Wrap=0`、`_UsePalette=0`、`_ShadowColor=(0.3,0.3,0.4)`、`_HighlightColor=(1,1,1)`；
未接噪声图的材质 `_Noise2/3 = {fileID: 0}`（默认 "white" → 采样值恒 1 > 任意阈值）。

| 材质 | `_BaseColor` | `_Color2`/`_Color3` | 噪声阈值/尺度 | `_ThresholdGradientSize` | `_UseDither`/`_DitherStrength` | 引用 |
|---|---|---|---|---|---|---|
| MAT_ToonGreenGrass（地面） | (0.478,0.620,0.353) | (0.553,0.710,0.353) / (0.290,0.431,0.165) | **0.3/0.05、0.4/0.03**（接 Albedo2Noise/Albedo3Noise） | **1** | **1** / 0.01 | :104-141 |
| MAT_ToonBeige | (0.831,0.769,0.659) | 同 BaseColor（无效化） | 默认 0.604/0.005、0.661/0.003（无图） | 0 | 0 / 0.1 | :102-144 |
| MAT_ToonBrown | (0.627,0.502,0.376) | 同 BaseColor | 同上 | 0.2 | 0 / 0.1 | :103-144 |
| MAT_ToonEsmerald | (0.202,0.631,0.192) | 同 BaseColor | 同上 | 0 | 0 / 0.1 | :116-157 |
| MAT_ToonGrey | (0.533,0.565,0.627) | 同 BaseColor | 同上 | 0 | 0 / 0.1 | :116-157 |
| MAT_ToonRed | (0.632,0.210,0.194) | 同 BaseColor | 同上 | 0 | 0 / 0.1 | :116-157 |
| MAT_ToonTerracotta | (0.753,0.565,0.471) | 同 BaseColor | 同上 | 0 | 0 / 0.1 | :116-157 |

⚠ **默认阈值陷阱**：未接噪声图的材质（"white" 恒 1 > 0.604/0.661）**实际恒取 `_Color3`**，而它们的
`_Color2/_Color3` 都抄成了 `_BaseColor` 同值——所以画面上看不出问题；若移植时 `_Color2/3` 用 shader 默认的
绿系值就会整件变绿。噪声图 sRGB 导入 → 阈值作用在线性域：GreenGrass 的 0.3/0.4 线性阈值对应
贴图灰值覆盖率 ≈20.6% / 7.5%【换算】。

### 5.2 草材质（`R/Assets/Grass/MAT_Grass.mat`，shader GUID `c7438b4d...`=GrassBlade，`m_EnableInstancingVariants: 1` :18）

| 参数 | 值 | 引用 |
|---|---|---|
| `_GrassTexture` | grassleaf.png（24×24 RGBA，:61）；`_AccentTexture1` accentleaf.png（:29）；`_AccentTexture2` foliage_sprite.png（32×32，:33） | |
| `_WindNoise` / `_Noise2` / `_Noise3` | WindNoise.png（:97）/ Albedo2Noise.png（:73）/ Albedo3Noise.png（:77） | |
| `_BaseColor` / `_Color2` / `_Color3` | (0.478,0.620,0.353) / (0.553,0.710,0.353) / (0.290,0.431,0.165)（=地面 GreenGrass 同值 → 草与地无缝） | :173-176 |
| `_AccentColor1` / `_AccentColor2` | (0.290,0.431,0.165) / (0.831,0.784,0.290) | :171-172 |
| `_AccentFrequency1/2`、`_AccentHeight1/2`、`_AccentScale1/2` | 0.001 / 0.01；0.5 / 0.5；1 / 1 | :114-119 |
| `_Cutoff` / `_Cuts` / `_Steepness` / `_Wrap` / `_ThresholdGradientSize` | 0.5 / 3 / 1 / 0 / 1 | :129-166 |
| `_WindNoiseScale/Speed/Threshold`、`_WindSwayAngle`、`_NoiseDivergeAngle`、`_WindDirection` | 0.071 / **0.2**（shader 默认 0.025）/ 0.365 / 60 / 10 / (0,1) | :161-179 |
| `_FakePerspectiveScale` | **0.05**（shader 默认 0.3） | :137 |
| `_UseDither` / `_DitherStrength` | 1 / 0.01 | :160-166 |
| 孤儿字段 | `_NoiseScale`(0.1)、`_AccentColor`——旧版改名残留，shader 不读 | :150 区、:170 区 |

### 5.3 管线材质（`R/Assets/Materials/Pipeline/`）

三张材质只提供 shader 绑定（GUID 互查：MAT_Outline `61d77b0a`→OutlineShader `d3d631b2`；MAT_Composite
`25ffe5f1`→CompositeShader `17a5b2aa`；MAT_SharpUpscale `3aa9b113`→SharpUpscale `79dbcd39`），
m_Floats/m_Colors 全是 Lit 类残留值（:84-119），**outline 参数全部由 Feature 每帧 `SetFloat` 下发**（PRF:178-208）。

### 5.4 渲染器资产实测（`R/Assets/Rendering/PC_Renderer.asset`）

PixelRendererFeature 块（:115-154）：

| 参数 | 代码默认 | **资产实测** | 引用 |
|---|---|---|---|
| width / height | 640 / 360 | **640 / 360** | :129-130 |
| outline/composite/upscale 材质 | — | 三张管线材质（§5.3） | :131-133 |
| lineTint / creaseTint | (0.05,0.05,0.08) / (1,0.55,0.1) | (0.102,0.102,0.102) / (1,1,1)（均未参与 shader 计算） | :134-135 |
| lineDarken | 0.4 | **0.5** | :137 |
| creaseBrighten | 0.3 | **0.5** | :138 |
| lineOverlay / lineAlpha | false / 1 | 1 / 1（lineOverlay 未参与 shader 计算） | :139-140 |
| creaseOverlay / creaseAlpha | true / 1 | 1 / 1 | :141-142 |
| kernelRadius | 1.0 (0.5..4) | **1** | :147 |
| zDeltaCutoff | 0.25 | **0.21** | :148 |
| angleZCutoff | 0.50 | **0.3** | :149 |
| angleZScale | 2.0 | **4** | :150 |
| depthDiffLow / depthDiffHigh | 0.25 / 0.3 | **0 / 0.05**（死参数，见 §9 坑 4） | :151-152 |
| normalSmoothLow / normalSmoothHigh | 0.2 / 0.8 | **0.05 / 0.3** | :153-154 |
| convexCutoff / creaseFeather / concaveCutoff / concaveZCutoff | 0.10 / 0 / 0.01 / 0.50 | 0.1 / 0.1 / 0.01 / 1（全为死参数） | :143-146 |

其余：SSAO feature 在列表内但 `m_Active: 0`（:99-114）；`m_RenderingMode: 2` = Forward+（:52；枚举
`URP17/Runtime/UniversalRenderer.cs:11-23`）；`m_UseNativeRenderPass: 1`（:31）。

### 5.5 URP/质量资产实测

- `R/Assets/Settings/PC_RPAsset.asset`：HDR **开**（:26）、MSAA 1=关（:28）、renderScale 1（:29）、
  `m_RequireDepthTexture: 1`（:22）、`m_RequireOpaqueTexture: 1`（:23）、主光阴影 2048（:46）、
  shadowDistance 50（:57）、4 级联（:58）、软阴影质量 3（:69）、Volume profile=SampleSceneProfile（:95）。
- `ProjectSettings/QualitySettings.asset`：当前档 = 1（"PC"，:7）；PC 档 vSync 0（:86）、lodBias 2（:97）、
  软阴影（:66）、customRenderPipeline=PC_RPAsset（:104）。
- `ProjectSettings/GraphicsSettings.asset:43`：全局管线 = PC_RPAsset。
- `ProjectSettings/ProjectSettings.asset:50`：**Linear 色彩空间**（m_ActiveColorSpace: 1）；
  URP 运行时开 `lightsUseColorTemperature`（`URP17/Runtime/UniversalRenderPipeline.cs:465-466`）。

---

## 6. 场景配置（`R/Assets/Scenes/Test/ShaderTest.unity`）

### 6.1 相机

结构：`Camera Pivot`（空物体，挂 IsometricCameraController，:1905-1944）→ 子 `Main Camera`（localPos (0,0,-30)，:491）。
pivot 与相机之间 30 m 距离配合正交无透视意义，只让"pivot 即视点"。

| 项 | 值 | 引用 |
|---|---|---|
| 正交 | 是，orthographic size **5**（可见高 10 m） | :465-466 |
| near/far | 0.3 / 1000 | :462-463 |
| HDR / MSAA | 开 / 允许（被 RP 资产关掉） | :475-476 |
| 控制器 fixedRotation | **(20, 45, 0)**——pitch 20°、yaw 45° | :1933 |
| smoothing（跟随/变焦 Lerp 系数） | 5（代码默认 30，`IsometricCameraController.cs:12`） | :1935 |
| orbit | sensitivity 0.3、pitch 夹 [19°, 60°]（代码默认 [5,60]） | :1936-1938 |
| zoom | speed 2、范围 [3,7]、默认 5（=ortho size，代码默认 [3,15]） | :1939-1942 |
| usePixelSnap | true | :1943 |
| **pixelRendererFeature 引用** | **{fileID: 0}（未赋值！）** → snap 走硬编码 360 高（`IsometricCameraController.cs:135`）——本场景正好等于实际值所以无异常 | :1944 |
| 相机 UA 数据 | Depth/Opaque=Required、后处理开、FXAA 关 | :497-540 |
| 背景色 | (0.192,0.302,0.475,0)（clearFlags Skybox :440，背景值兜底） | :441 |

### 6.2 光照

| 光 | 值 | 引用 |
|---|---|---|
| Sun | Directional；色 (1,0.949,0.8)；强度 1.2；**Soft 阴影**（strength 1、bias 0.05、normalBias 0.4）；**色温 5000K 开**（URP 冷暖补偿会把 5000K 校为中性白，近似 (1,0.894,0.808)【换算】）；UA soft quality 1（=Medium） | :559-656 |
| Sun 静态朝向 | Euler hint (-90.3, 0, 43.9)（运行时被 DayNight 接管） | :632-638 |
| Moon | Directional；强度 0；阴影 None；色 (0.083,0.096,0.142)；Euler hint (90,0,0) | :1605-1713 |
| 主光选择 | URP `GetMainLightIndex`：`RenderSettings.sun` 优先，否则取最亮平行光（`URP17/Runtime/UniversalRenderPipeline.cs:2113-2147`；URP14 `:1620-1646`）——昼/月交替时主光会切换 | |

### 6.3 Global Volume（profile = `R/Assets/Settings/SampleSceneProfile.asset`）

| 效果 | 状态 | 关键值 | 引用 |
|---|---|---|---|
| Tonemapping | 开 | **mode 1 = Neutral**（枚举 None/Neutral/ACES/Custom，`URP17/Runtime/Overrides/Tonemapping.cs:8-26`） | :105-133 |
| Bloom | 开 | threshold 1、intensity 0.25、scatter 0.5、高质量过滤 | :3-49 |
| Vignette | 开 | intensity 0.2 | :50-77 |
| MotionBlur | **关** | — | :135-159 |

后处理执行顺序：Bloom → Vignette → ColorGrading 在 UberPost 内部（`URP17/Shaders/PostProcessing/UberPost.shader:185,240,245`），
全部发生在**全分辨率**、早于 PixelRendererFeature 的 `AfterRenderingPostProcessing`——即像素化的是
**调色之后**的画面。Neutral tonemap 在线性域压高光：input 1.0 → 0.631、1.2 → 0.689【换算，曲线常数
`CORE17/ShaderLibrary/Color.hlsl:535-556`】——太阳直射面的过曝被压平，这对色带观感影响显著（§8.6）。

### 6.4 EnvironmentManager（CloudShadowManager 实测，:1437-1459）

`cloudNoise` 用的是 **CloudNoise2.png**（:1450）。逐项：scale **60**（:1451）、worldY 20（:1452）、
speed **10**（:1453）、contrast **20**（:1454）、threshold **0.47**（:1455）、direction (1,0)（:1456）、
shadowMin **0.05**（:1457）、divergeAngle 10（:1458）、power **3**（:1459）。
（代码默认值见 §7.2——场景全部偏离默认。）
云影实效【换算】：光值 ≈ `pow(clamp(20·(min(s1·s2+0.47,1)-0.5)+0.5+0.47, 0.05, 1), 3)`，
噪声乘积 ≥0.0315 即全亮，实测该域覆盖率 ≈69%（≈31% 面积处于云影，影底 ≈5% 亮度）。

### 6.5 GrassManager（GrassSpawner 实测，:1151-1173）

grassCount **35000**（:1163）；mesh = 内建 Quad（:1164）；材质 MAT_Grass（:1165）；
spawnArea 中心 (0,0,0)、extent (40,2,40)（:1166-1168）；terrainLayer bits=8（layer 3，Plane 所在层，:1171）；
minHeight/maxHeight **0.4/0.6**（:1172-1173，代码默认 0.8/1.2）。
实例变换：随机 Y 旋转、scale (0.35, h∈[0.4,0.6], 1)（`GrassSpawner.cs:73-76`）→ 草叶世界尺寸 0.35 m 宽 × 0.4–0.6 m 高。

### 6.6 场景物体与 RenderSettings

- 地面：Plane ×scale(10,1,10)（:837-838），材质 GreenGrass，layer 3，MeshCollider。
- 道具：9 个 Cube（0.6³–1.5³）、4 根 0.6×3×0.6 柱、icosphere ×50 放大（:851-909，Esmerald 材质 :903-906）、
  rock/rock_2 预制 ×3（rock 材质重映射到 Grey，`R/Assets/Objs/rock.obj.meta:6-11`）。
- 4 面隐形墙：BoxCollider（scale 110×5×1，MeshRenderer 关）围住 ±30 边界（:680、:1201、:1726、:2677）。
- RenderSettings：雾关（:17）、环境源 Skybox、Ambient Sky (0.149,0.149,0.2)（:23-25，与 DayNight gradient key1 一致）。
- PlayerPlaceholder：半径 0.5 球，`moveSpeed 5`、`jumpForce 5`、groundLayer bits 8（:2053、:2071-2077）——
  相机跟随目标（`IsometricCameraController` 的 target）。
- Build 场景唯一：ShaderTest（`R/ProjectSettings/EditorBuildSettings.asset:8-9`）。

### 6.7 噪声/精灵贴图导入参数（`R/Assets/Textures/*.meta`）

| 贴图 | 尺寸 | 导入 |
|---|---|---|
| Albedo2Noise / Albedo3Noise / CloudNoise / CloudNoise2 / WindNoise | 256×256 灰度 | **sRGB 开**、Bilinear、Wrap=Repeat、mip 开、压缩 Normal（各 meta `sRGBTexture: 1`） |
| grassleaf / accentleaf / foliage_sprite | 24×24 / 24×24 / 32×32 RGBA | **Point**（filterMode 0）、alphaIsTransparency、mip 开 |

⚠ 噪声图 sRGB 导入 + Linear 工程 → shader 采样得到**线性化**灰度，所有阈值（0.604/0.661/0.47/0.365 等）
都作用在线性域【实测：meta + `ProjectSettings.asset:50`】。自制贴图必须复刻这一导入组合，否则覆盖率完全不同
（例：Albedo2Noise 灰值 >0.3 的占比在 gamma 域是 98.3%，线性域只有 20.6%【换算】）。

---

## 7. 系统脚本

### 7.1 IsometricCameraController（`R/Assets/Scripts/Systems/IsometricCameraController.cs`）

公开参数：`fixedRotation`(6)、`target`(9)、`smoothing`(12)、`orbitSensitivity/minPitch/maxPitch`(15-17)、
`zoomSpeed/minZoom/maxZoom/defaultZoom`(20-23)、`usePixelSnap`(26)、`pixelRendererFeature`(27)。

LateUpdate 顺序：orbit 输入 → zoom 输入 → 跟随 target → ApplyTransformations（:76-84）。
ApplyTransformations 的 pixel-perfect 两步法（:121-164）：

```
texel = orthographicSize * 2 / pixelRenderHeight          # :136 —— 米/画布像素（高向）；feature 未赋值时高按 360 兜底（:135）
localPos = cam.InverseTransformPoint(truePosition)         # :139 视图空间
snappedXY = Round(localPos.xy / texel) * texel             # :142-143 视图空间 XY 吸附
snapError = localPos.xy - snappedXY                        # :146
uvOffset = (snapError.x / (ortho*2*aspect), snapError.y / (ortho*2))   # :149-150 误差转画布 UV
pivot.position = cam.TransformPoint(snappedXY, localPos.z) # :153-154 把吸附结果写回 Transform（!! 改的是 pivot 自身）
Shader.SetGlobalVector("_PixelPanOffset", (uvOffset, 0, 0)) # :157 上采样 shader 消费；关闭时清零（:162）
```

要点：snap 发生在**渲染外的 LateUpdate、直接改 Transform**（不碰渲染矩阵）——两台相机结构变化、引擎版本
变化都不影响；缩放（ortho size 连续变化）不参与 snap，只在 zoom 平滑过程中瞬时"漂"，落定后重 snap。

### 7.2 CloudShadowManager（`R/Assets/Scripts/Systems/CloudShadowManager.cs`）

公开参数（默认值，:13-21）：`directionalLight`(7)、`cloudNoise`(10)、`cloudScale` 50、`cloudWorldY` 20、
`cloudSpeed` 0.02、`cloudContrast` 2.0、`cloudThreshold` 0.3（-1..1）、`cloudDirection` (1,0)、
`cloudShadowMin` 0.3、`cloudDivergeAngle` 10、`cloudPower` 1（1..5）。

Update 每帧 `SetGlobalTexture/Float/Vector` 下发：`_CloudNoise/_CloudScale/_CloudWorldY/_CloudContrast/
_CloudThreshold/_CloudShadowMin/_CloudDivergeAngle/_CloudPower/_CloudDirection`（:60-79）——
**唯独不下发 `_CloudSpeed`**（:70 注释：该量归 DayNightCycleManager 所有，防两脚本竞态）。

### 7.3 DayNightCycleManager（`R/Assets/Scripts/Systems/DayNightCycleManager.cs`）

`[ExecuteInEditMode]`（:3）。公开参数：`timeOfDay` 0.5（0=午夜、0.25=日出、0.5=正午、0.75=日落，:8）、
`dayDurationInSeconds` 120（:9）、`isTimePassing`（:10）、`sunriseTime` 6 / `sunsetTime` 18（:14-16）、
`orbitAxisY` 45（:20）、`cloudShadowDirection` (0.2,-1,0.2)（:23）、`sunLight`/`moonLight`（:26-27）、
`maxSunIntensity` 1.2 / `maxMoonIntensity` 0.2（:30-31）、`lightIntensityCurve` Linear(0,0,1,1)（:33）、
三个 Gradient `sunColor/moonColor/ambientColor`（:37-43）、`cloudShadowManager` 引用（:46）。

每帧 `UpdateCycle`（:72-142）：

```
hour = time*24; isDay = sunrise <= hour < sunset                       # :74-77
sunAngle  = isDay ? Lerp(0,180, (hour-sunrise)/(sunset-sunrise))       # :144-153
                   : Lerp(180,360, 夜间百分比)                          # :154-170
moonAngle = sunAngle + 180                                              # :81
sun/moon.rotation = Euler(0, orbitAxisY, 0) * AngleAxis(angle, Vector3.right)   # :86,91 —— 绕世界 Y 转 45° 后绕本地 X 升落
sunDot = Clamp01(Dot(sun.forward, Vector3.down))                        # :100 —— 强度/颜色/环境全部由几何 dot 驱动，不由 timeOfDay 驱动
moonDot = 同理（moon）                                                   # :107
i = lightIntensityCurve.Evaluate(sunDot)
sun.intensity  = Lerp(0, maxSun, i);   sun.color  = sunColor.Evaluate(sunDot)
moon.intensity = Lerp(maxMoon, 0, i);  moon.color = moonColor.Evaluate(moonDot)   # 月亮自动反相（:115-125）
intensity < 0.01 → LightShadows.None，否则 Soft（:117,125）
RenderSettings.ambientLight = ambientColor.Evaluate(sunDot)
Shader.SetGlobalColor("_GlobalAmbientColor", 同值)                       # :129-131
Shader.SetGlobalVector("_CloudLightDirection", cloudShadowDirection.normalized)   # :134 —— 固定方向，不随太阳（README:144-146 的取舍）
scaledSpeed = isTimePassing ? cloudShadowManager.cloudSpeed / dayDurationInSeconds : 0
Shader.SetGlobalFloat("_CloudSpeed", scaledSpeed)                        # :139-140 —— 云速与昼夜时长自动同步
```

场景实测：`timeOfDay 0.5`、`dayDurationInSeconds **840**`、`orbitAxisY **43.9**`、`maxMoonIntensity **0.3**`
（:263-273）。Gradient 实测（两键线性）：sunColor `#FF6600`→`#FFF2CC`（key (1,0.4,0.098)→(1,0.949,0.8)，:300-301）；
moonColor (0.083,0.096,0.142)→(0.6,0.702,1)（:330-331）；ambientColor (0.020,0.020,0.078)→(0.149,0.149,0.2)
（:360-361，夜间≈黑、正午≈深灰蓝）。

### 7.4 GrassSpawner（`R/Assets/Scripts/Systems/GrassSpawner.cs`）

公开参数：`grassCount` 10000（:8）、`grassMesh`/`grassMaterial`（:9-10）、`spawnArea`（Bounds，默认 50×20×50，:14）、
`terrainLayer` ~0（:15）、`minHeight/maxHeight` 0.8/1.2（:18-19）。

机制：Start 一次性生成（:30-41）——在 spawnArea 顶面随机撒点、向下 Raycast 到 terrainLayer（:67），
命中点为实例位、记录地面法线；随机 Y 旋转 + scale (0.35, Random(minH,maxH), 1)（:73-76）；
按 `DrawMeshInstanced` 上限 **1023**/批分批存 `Matrix4x4[]` + `Vector4[]`（:22,49-52）；每帧
`Graphics.DrawMeshInstanced(..., ShadowCastingMode.Off, receiveShadows:false)` 重放全部批次，
`MaterialPropertyBlock.SetVectorArray("_TerrainNormal", ...)` 逐批注入法线（:98-111）。失败尝试上限
= 批大小×2 防死循环（:57）。

---

## 8. 移植到 2:1 的换算（画布 960×540 @1080p，k=2）

本仓口径：`pixelScale=2`（`PixelScaleState.cs:16`、`PixelartCameraRig.cs:66`）、正交 30° 俯角、可见 22.5 m
（ortho 11.25，`CameraFraming.cs:85`）、方位 45° 基准可自由旋转（`CameraFraming.cs:188-193`）。
参照库口径：画布 640×360、pitch 20°/yaw 45°、ortho 5（可见 10 m）、yaw 可拖拽 [19°,60°] 俯角。

### 8.1 与分辨率无关、原样搬运的量

- **Outline 全部阈值与权重**（`zDeltaCutoff/angleZCutoff/angleZScale/normalSmoothLow/High/creaseBrighten/
  lineDarken`）：比较发生在视图空间米（§2.2），与画布尺寸无关。但**世界每画布像素的米数（wpp）变了**，
  阈值的"像素尺度含义"随之变化（§8.3）。
- **SharpUpscale 公式与 `_PixelPanOffset`**：自适应 texel，无需改。
- **ToonLit/GrassBlade 全部材质参数与云影参数**：世界空间语义（噪声尺度=米、云平面高度=米）。
- **kernelRadius=1**：语义是"1 画布像素"，两库一致。

### 8.2 需要重标为"屏幕/画布像素"的量

| 量 | 参照库实况 | 2:1 移植动作 |
|---|---|---|
| 降采采样点 | Pass1 点采样取全分辨率块**角纹素**（§2.1；640×360 vs 1920×1080 非整数比 → 角纹素位置逐块漂移） | k=2 整数倍 → 角纹素恒为偶数行/列；描边在该网格上算与参照"内部直接渲染"同性质；若要块中心语义需在 blit scaleBias 上加半纹素偏移【提案：不加，保持角采样 + 让描边读全分辨率深度/法线，无块锚问题】 |
| Bayer 索引 | `SV_Position` 是**全分辨率**像素（ToonLit :199）→ 2:1 点降采后每个画布块只覆盖矩阵的偶数行/列 = 索引 {0,1,2,3}/16、均值 -0.406（系统性偏暗且图案从 4×4 退化为 2×2）【换算】 | 抖动坐标改为**画布像素**：`floor(positionHCS.xy / _PixelartSamplingScale)`（本仓已有该全局，`PixelartPath.cs:239-241`）或由后处理链下发 `_PixelartRTWidth/Height` 换算【提案】 |
| `_DitherStrength` 的 0.25 系数 | `(b-0.5)*strength*0.25`（ToonLit:201）：满幅=±0.125 光照 | 若对齐本仓"满幅 Bayer=±0.5 个档间距"口径（渲染篇 §6），系数改 `0.5/level`；若逐像素照搬参照观感则保留 0.25【提案：按本仓口径】 |
| 草叶/道具的世界尺寸 → 画布像素 | 草 0.35×0.4-0.6 m @ wpp 0.0278（ortho5/360）= 12.6×14.4-21.6 art px | 本仓 wpp = 22.5/540 = 0.04167 m/px → 同世界尺寸 = **8.4×9.6-14.4 art px**（细 2/3）。保持世界尺寸（艺术像素世界尺寸固定口径）则草更细；要像素观感一致需 scale ×1.5【提案：保持世界尺寸，草量与尺寸重调归场景设计】 |
| `_PixelPanOffset` 单位 | 画布 UV（0..1） | 不变；snap 的 texel 用本仓 `rig.UnitSize`（米/画布像素，`PixelartCameraRig.cs:308`）替换参照的 `ortho*2/height` |

### 8.3 深度阈值在 30° 俯角下的实效值【换算】

剪影最小可检深度跳变（平地旁、法线正确口径）：`h_min = z_thresh(ground)·sin(pitch)`，
`z_thresh(ground) = 0.21·(((1-sinθ)-0.3)/0.7·4+1)`：

| 口径 | pitch20（参照） | pitch30（本仓） |
|---|---|---|
| z_thresh(地面) | 0.64 m | 0.45 m |
| h_min（平地上物体低于此高度不出剪影线） | 0.219 m | **0.225 m** —— 基本不变，阈值族可原样搬运 |
| 正对相机的墙 z_thresh | 0.21 m | 0.21 m |
| 相邻像素的地面深度步（竖向邻）wpp·cotθ | 0.0763 m（wpp 0.0278） | **0.0722 m**（wpp 0.04167）——几乎相同 |
| 竖直墙面的深度步 wpp·tanθ | 0.0101 m | 0.0241 m（×2.4，细结构更易触发剪影，需实测验收） |

**若照抄参照的 DepthNormals 偏置写法**（§3.3），实效阈值随方位角漂移【换算】：pitch20/yaw45 全表面饱和
≈1.05 m（h_min≈0.36 m）、yaw225 恒 0.21 m（h_min≈0.072 m）；pitch30/yaw45 ∈[0.875,1.05]（h_min 0.44-0.53 m）、
yaw135/315 ∈[0.21,0.75]。表现为**环绕相机时描边忽粗忽无**——这是参照库自身潜伏 bug（§9 坑 3），
移植必须直写归一化法线规避。

### 8.4 相机与 snap

- 本仓已是双相机 + Transform/矩阵双层 snap（渲染篇 §4、`PixelartBeforeRenderFeature.cs:55-62`）。PostLine 链
  是**单相机全分辨率渲染**（`双风格切换.md:46`），snap 必须像参照那样**改 Transform**（LateUpdate，§7.1）——
  本仓 `cmd.SetViewProjectionMatrices` 方案依赖 BeforeRendering 块先于 `SetPerCameraProperties` 的 URP14
  执行序（`URP14/Runtime/ScriptableRenderer.cs:1189-1203`，相机矩阵在其后才写入 shader），单相机 + 标准
  `UniversalForward` 几何下不该复用【提案】。
- `_PixelPanOffset` 与本仓预留的 `SnapOffsetUvId`（`PixelartPath.cs:244-245`，注释即"上采样 UV 补偿量"）对接：
  PostLine 链首次让这个预留量有了消费者。
- 正交尺寸变化（本仓无滚轮缩放，恒 11.25）：参照的"缩放中 snap 漂移"问题不存在。

### 8.5 后处理域次序

参照库：URP 后处理（Neutral/Bloom/Vignette）在**前**（全分辨率），像素化在后——像素化的是调过色的画面。
本仓 URP14 单相机链若把 PostLine feature 放 `AfterRenderingPostProcessing` 可获得同序；但本仓 URP 资产
Bloom 关、HDR 关（`PC_Balanced_URPAsset.asset:29,31`、渲染篇 §2）→ 参照观感配方里的 Neutral tonemap +
Bloom 不存在。要对齐"参照观感"需二选一【提案】：① 保持 HDR 关，在 ToonLit 输出前内联 NeutralTonemap
（常数可照抄 `CORE17/ShaderLibrary/Color.hlsl:535-556`：a=0.2, b=0.29, c=0.24, d=0.272, e=0.02, f=0.3,
whiteLevel=5.3）；② 开 HDR + 本仓 URP 资产加 Neutral——与渲染篇 §2 "HDR 关"红线冲突，不推荐。

### 8.6 昼夜/云影（若接）

- 参照 `cloudSpeed` 语义 = "UV 位移/昼夜秒"（DayNight 再 ÷ dayDuration，§7.3）；场景 speed 10 + 840 s →
  全局 `_CloudSpeed ≈ 0.0119`。本仓游戏固定光照不接昼夜：直接 `SetGlobalFloat(_CloudSpeed, 常数)` 自定标
  （0.0119 ≈ 每 84 s 平移一个噪声周期）【提案】。
- `_CloudLightDirection` 固定 (0.2,-1,0.2).normalized，与太阳解耦——固定光照下直接常量下发。
- 环境/光色：本仓已有 rig 下发 `_PixelartLightDirWS/_PixelartLightColor` 的单源口径（渲染篇 §6），
  PostLine 链的 `_GlobalAmbientColor` 应并入同一处下发。

---

## 9. 已知限制与坑

README 自述（`R/README.md:412-424`）：

1. **物体根部棱线**：草不写深度/法线 → 物体与地形交界的 crease 判定裸奔，草密/物体细节多时不明显。
2. **云影压塌色带**：云影在色带量化**之前**取 min（ToonLit:194-195）——重云影下 diffuse 被钳到低值，
   量化后色带并档。
3. **草对描边隐形**（README:96、:418）：刻意无 DepthOnly/DepthNormals，草叶无剪影/棱线，与地面交界无线。
4. **不为 PBR 设计**：低分辨率下 PBR 渐变即噪声；配平色才是预期工作流。
5. **云影暂停错位**：shader 用 `_Time.y`，`isTimePassing=false` 时 C# 停止推进 timeOfDay 但 shader 时间照走
   → 恢复后云影位置跳变；修复需 C# 手动时间累加器。
6. **无按时间的调色板**：昼夜只变光色/环境，材质色不随时间变。

读码新发现（本文首次记录）：

7. **法线编码错配 → 阈值随方位角漂移**：ToonLit DepthNormals 写 `n*0.5+0.5` 进 SNorm 法线纹理（§3.3），
   Outline 的角度自适应阈值实效值随相机 yaw 在 0.21–1.05 m 之间摆动（§8.3 表）——参照库 demo 的环绕
   相机会看到描边灵敏度方向性变化。移植直写归一化法线即根治。
8. **HDR + Neutral/Bloom/Vignette 全部作用在像素化之前**（§6.3）：观感配方实际是"色调映射后的图被像素化"；
   本仓 HDR 关，直接搬参数会偏暗偏灰，需按 §8.5 二选一。
9. **相机控制器引用断裂**：场景里 `pixelRendererFeature = {fileID: 0}`（§6.1）——snap 高度靠硬编码 360 兜底
   （:135），改内部分辨率必须同步改这个兜底或补引用。
10. **`step_uv` 的 TexelSize 语义**：Outline 的 `step_uv = _BlitTexture_TexelSize.xy * _KernelRadius` 里
    `_BlitTexture` 是 Pass2 的 blit **源** `_ColorCopy`（全分辨率），TexelSize = 1/全分辨率——采样步进实际由
    UV 偏移 `off * (全分辨率纹素 × kernel)` 决定，与"内部分辨率 1px"的等价性来自 `off ∈ {-1,0,1}` 的 UV 差恰好
    等于画布 1px 的 UV 差这个巧合的坐标传递（input.texcoord 在两张不同尺寸 RT 间都是 0..1 归一坐标）。
    kernelRadius>1 时步长按**全分辨率纹素**放大而非画布纹素——参数语义与 README 描述（"each kernel step
    equals exactly 1 pixel"，README:59）在 kernel≠1 时不符。移植时若要"kernel 按画布像素计"，应改为
    `step = 画布TexelSize * kernel`【提案：固定 kernel=1 并按画布纹素写死，删掉该参数】。
11. **死参数面**：`_DepthDiffLow/High`（算了不用，§2.2③）、`_LineTint/_CreaseTint/_FlipPalettes/_LineOverlay/
    _CreaseOverlay/_ConvexCutoff/_CreaseFeather/_ConcaveCutoff/_ConcaveZCutoff`（声明不下发到计算，§2.2尾）、
    `_MidtoneColor`（palette 分支用 albedo 顶替）——移植时全部不搬，参数面收敛到实际生效的 9 个。
12. **Patch 默认值陷阱**：无噪声图材质恒取 `_Color3`（§5.1）——shader 默认 `_Color2/3` 是绿色，照搬 Properties
    默认值会让无图材质变绿；移植时把 `_Color2/3` 默认值改成 = `_BaseColor`。
13. **噪声图 sRGB 域**：所有阈值在线性域（§6.7），自制贴图必须 sRGB 导入，否则覆盖率差数倍。
14. **Pass3 的 Render Graph 全局纹理盲区**（§2.0 ⚠）：URP17 的隐患，URP14 无此问题，但两趟顺序必须硬约束。
15. **SetGlobalColor 无色彩空间转换**：`_GlobalAmbientColor` 的数值就是线性加数（§3.2），Gradient 里看着
    "很暗"的颜色在线性域更暗——配色对账必须按线性语义读值。
16. **Downsample 是点采样非 bilinear**（§1 出入表）：README 的降质描述不可作为观感依据。
17. **`m_InvalidKeywords: [_USEDITHER_ON]`**（GreenGrass :15-16、MAT_Grass :15-16）：`[Toggle]` 产生的关键字
    无 shader_feature 接收——shader 只读 float，开关关键字是死关键字；移植时材质不生成关键字。
18. **草叶在 2:1 下先天模糊**：草精灵 24×24 Point 采样、billboard 朝相机——参照 wpp 0.0278 时草叶宽 12.6 art px
    （精灵 24px → 缩到 0.52×，点采样即有丢列）；本仓 wpp 更粗（8.4 px，0.35×）丢得更狠。草精灵需要按
    目标 art px 数量重绘（而非缩放采样）【提案】。

---

## 10. 特性 → 落点清单（本仓）

命名遵循 `双风格切换.md` §1 命名纪律（新代码一律 `PostLine` 前缀）；shader 落 `Assets/Resources/PixelartShaders/`
（Resources 持有铁律，渲染篇 §8.1）；运行时取用走 `PixelartShaders` 同款入口。

### 新建

| 文件 | 内容 | 对应参照 |
|---|---|---|
| `pirate-crew/Assets/Scripts/PirateCrew/Rendering/PostLine/PostLineRendererFeature.cs` | 5 pass：CopyColor（可省——URP14 中间纹理模式下相机色目标已可读）/ Point 降采 / Outline / Composite / SharpUpscale；`RenderPassEvent.AfterRenderingPostProcessing`；RT 手管（`RenderingUtils.ReAllocateIfNeeded`，`URP14/Runtime/RenderingUtils.cs:596`）；outline/composite/upscale 材质序列化字段 | PRF 全文件 |
| `.../PostLine/PostLineOutline.shader`（Hidden） | §2.2 全公式；**直写归一化法线的配套**（采样端不变）；死参数不搬；step 固定画布纹素 | OutlineShader.shader |
| `.../PostLineComposite.shader`（Hidden） | `lerp(color, outline.rgb, outline.a)` | CompositeShader.shader |
| `.../PostLineSharpUpscale.shader`（Custom） | fwidth+smoothstep + `_PixelPanOffset`（接 `PixelartPath.SnapOffsetUvId` 已有名，避免双全局） | SharpUpscaleShader.shader |
| `.../PostLine/PostLinePath.cs` | shader 全局 ID 常量（`_PixelPanOffset`/outline 参数族/云影族），仿 `PixelartPath.cs` | — |
| `.../PostLine/PostLineCameraSnap.cs` | Transform 级 texel snap + UV 误差下发（LateUpdate；挂在战斗相机驱动侧或 rig 的 PostLine 分支） | IsometricCameraController.cs:121-164 |
| `pirate-crew/Assets/Art/Shaders/PostLine/PostLineToonLit.shader` | 4 pass（ForwardLit/ShadowCaster/DepthOnly/DepthNormals）；DepthNormals 直写归一化法线；Bayer 按画布像素；Patch 默认 `_Color2/3 = _BaseColor`；`_PixelartLightDirWS/_PixelartLightColor/_GlobalAmbientColor` 兼容 rig 下发 | ToonLit.shader |
| `pirate-crew/Assets/Art/Materials/PostLine/*.mat` | outline/composite/upscale 三张管线材质 + Toon 示例材质（参数按 §5.1 表） | Pipeline/*.mat、Toon/*.mat |
| `pirate-crew/Assets/Art/Textures/PostLine/` | 自制平铺噪声 ×3（云影/风/Patch）+ 草/点缀精灵（按 8.4 的 art px 目标尺寸重绘）；**导入参数照 §6.7**（噪声 sRGB+Bilinear+Repeat，精灵 Point） | Textures/*.png（参照 PNG 不入库） |
| `pirate-crew/Assets/Settings/URP/PostLine_Renderer.asset` | 单相机渲染器：仅 PostLine feature；注册进 `PC_Balanced_URPAsset.asset` 的 `m_RendererDataList` | PC_Renderer.asset |
| （可选，单列评估）`.../PostLineGrass/`：PostLineGrassBlade.shader + PostLineGrassSpawner.cs | §4 全套 + §7.4；`_TerrainNormal` 实例法线、1023/批 DrawMeshInstanced | GrassBlade.shader、GrassSpawner.cs |
| （可选，场景层）PostLineCloudShadow.cs / PostLineDayNight.cs | §7.2/§7.3；固定光照下只需 CloudShadow 下发 + `_CloudSpeed` 常数标定，昼夜不进本次范围（`双风格切换.md:75`） | CloudShadowManager.cs、DayNightCycleManager.cs |

### 修改

| 文件 | 动作 |
|---|---|
| `pirate-crew/Assets/Settings/URP/PC_Balanced_URPAsset.asset` | `m_RendererDataList` 追加 PostLine_Renderer（`PixelartPathInstaller.cs:472` 已有同款改列表先例） |
| `pirate-crew/Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartCameraRig.cs` | 按 `RenderStyleState.Current`（`RenderStyleState.cs:21-31`）分支：PostLine → 单相机 + snap 组件 + PostLine 渲染器 index；CellShade → 现状 |
| `pirate-crew/Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartPath.cs` | 若复用 `SnapOffsetUvId` 则无改；PostLine 专属 ID 放 `PostLinePath.cs` |
| 材质工厂（`PixelartMaterialFactory` 及战斗装配侧） | Toon 参数面（`_Cuts/_Steepness/_Wrap/_ThresholdGradientSize/_BaseColor/_Color2/3`…）进材质配置；`双风格切换.md:52-57` 的材质双 pass 口径（`PixelartOpaque` + `UniversalForward` 共存） |
| `pirate-crew/Assets/Tests/Rendering/` | 纯函数测试：色带量化（GLSLmod/梯度带）、z_thresh 公式、snap 换算、2:1 换算表（§8.3 数值可作断言） |
| 文档 | `渲染管线.md` §4 增补 PostLine 分支一行索引；本档从「提案」转正时在文首去标注 |

### 不搬清单（明确排除）

独立版 OutlineRendererFeature（§2.5）、全部死参数（§9 坑 11）、Mobile RP/质量档（参照库 Mobile_RPAsset
与本仓结构不同）、昼夜周期（本次范围外）、`Assets/Editor/V3DemoCapture.cs`（参照库工作区临时出图脚本）。

---

## 11. 未覆盖项

- 参照库 `Assets/Settings/Mobile_RPAsset.asset`/`Mobile_Renderer.asset` 与 Mobile 质量档未逐项读取（PC 档是
  当前生效档，`QualitySettings.asset:7`）。
- `Assets/_Recovery/0.unity`（场景备份）只对账了关键参数（差异：timeOfDay 0.07、cloudSpeed 0.01、
  maxMoonIntensity 默认——以 `Assets/Scenes/Test/ShaderTest.unity` 为准）。
- URP17 Render Graph 下 Pass3 的实际排序行为未在参照库实机验证（静态读码结论，§2.0 ⚠）。
- 无实机性能数据（参照库与 URP17 的帧率/profiling 均未采集；本仓移植后的帧耗需自行实测）。
- 参照库 itch.io demo 与 GitHub 视频素材未取（无法访问外链视频内容）。
