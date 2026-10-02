# 参照 · t3ssel8r 的 3D 像素引擎口径（作者自述）

> **这份文档是什么**：**t3ssel8r**（[频道](https://www.youtube.com/channel/UCIjUIjWig0r5DIixQrt6A3A)，Unity 3D 像素引擎「[参照六视频](调研-3D像素渲染视频整合.md) v5」的作者）**逐条自述的引擎算法口径**。它是本次调研里**唯一由作者本人写清算法**的来源——比任何第三方复刻都可靠。
> **为什么单独成篇**：那六支视频里，v5 只讲建模不讲渲染，导致 v5 的算法在本仓一度只能靠画面推测。查他的投稿后发现：**他把每个系统的算法都写在对应视频的官方简介里**，27 条投稿合起来就是一份引擎规格书。本仓 §3 的两处缺口（帧级量化、内线）与三处现行裁决都能在这里找到作者口径。
> **调研时间**：2026-09-22。
> **素材与校验**：27 条投稿的标题/时长/发布日期/官方简介全文（第一手拉取），存于 `external/pixel-render-ref/t3ssel8r-desc.txt`；两条视频的官方英文字幕（`yt-subs/`）；v5 的抽帧与高倍裁切（`frames/v5/`、`crops/`）。**未下载全部视频**——本文所有算法描述均出自**官方简介原文**，不是从画面推测的。
> **标注约定**：沿用 [美术风格指南](../../设计/美术风格指南.md) §0。**引号内是作者原文（英文）**，后附中译与出处；方括号内是本仓的解读，属【AI 提案】。

---

## 1. 引擎口径逐条（作者原文 → 本仓意义）

### 1.1 分辨率与相机：640×360 + snap + 偏移补偿【与本仓两步法同构，且更早】

> `Subpixel Camera for a 3D Pixel Art Game Engine`（`NutO1jzuVXU`，2020-10-10）：
> "we render our scene at a low pixel art resolution (**640x360**) and upscale the result to the screen resolution for presentation. At render-time, the camera is **snapped to the nearest "pixel"**, and when blitting to screen, the **snap offset is corrected** so that the camera is permitted to smoothly glide over the pixels of the screen at the display's native resolution. Some extra projections need to be computed in order to ensure that this operation works precisely even when the camera is rotated **45 degrees**."

⇒ **与本仓 [调研-全屏像素化](调研-全屏像素化与像素完美.md) §2 的"视空间 snap + UV 反向补偿两步法"完全同构**，连内部分辨率都是 **640×360**，且同样处理 45° 方位角下的投影。时间上是 **2020-10**，早于本仓原引的 David Holland——**溯源可补：两步法的最早公开出处应记 t3ssel8r**。
⇒ 同一支还写了一条阴影侧的做法：**"The pixel art shaders were also updated to add contrast in shadowed regions by shading the shadowed regions using a simple **monochromatic Lambertian BRDF**, resulting in a soft fake ambient lighting effect."** —— 暗部用**单色 Lambert BRDF** 补对比，得到柔和的假环境光。对应本仓 §4.2"暗部不做纯乘暗、用替换式预制暗部色"，**是同一问题的另一条解法**。

### 1.2 相机机位：30° 俯角 + 方位角 snap 到 45°【与本仓 35.264° 的差异，值得回看】

> `Isometric Camera`（`ij555s4mAuI`，2020-10-05）：
> "a **30 degree pitch** isometric camera that **snaps the yaw to 45 degree increments** is used. This can perfectly reproduce the **2-pixels-across-1-pixel-down look** of rectangular pixel art blocks, while still allowing smooth camera motion between snapping regions."

⇒ 他取 **30° 俯角**，理由正是"2 像素横 : 1 像素竖"的**经典手绘像素等距网格**；本仓 [渲染管线-等距像素卡通](渲染管线-等距像素卡通.md) §2.1 的表格里，30° 一行写的也正是"经典像素等距（手绘像素画沿用至今的网格）"，但**现行裁决取的是 35.264°（真等距）**。
⇒ **这不是要推翻裁决**，而是补一条外部证据：**该方向公认参照作者选了 30°**。若 M1 样张在"像不像手绘像素等距"上不满意，30° 是第一个该回头看的档位。【AI 提案】
⇒ 另外他明确用了 **yaw snap 到 45° 增量**——与本仓"方位角固定 45°"同类，证据方向一致。

### 1.3 着色：色带 = 标准 BRDF 上套 ramp 过滤

> `Pixel Art Block Shader`（`Lm5tRVNQxFs`，2020-10-05）：
> "For typical isometric pixel art blocks, shading is performed with a **color ramp filter on a standard BRDF**."

⇒ 与本仓 §4.1"2~3 档色带 / 1D ramp 纹理（点采样）"**同构**。

### 1.4 边缘高光与阴影：**深度缓冲取邻域比较**，按比邻域远近提亮/压暗 ←（本仓"内线"缺口的直接参考）

> 同一支 `Lm5tRVNQxFs`：
> "To get the **edge highlight and shadow effects, the depth buffer is sampled for adjacent points, and the pixel is brightened or dimmed based on whether it is closer or further to the camera than its neighbors**."

⇒ **这是 v5 边缘效果的核心口径**：不画线，而是**在深度缓冲上取邻域、比较远近，然后对该像素的着色值提亮或压暗**。同时解释了画面里"棱线上是浅色亮边"（比邻域近 → 提亮）。
⇒ 对本仓 [渲染管线-等距像素卡通](渲染管线-等距像素卡通.md) §7"内线"与 [待办事项](../../项目/待办事项.md) 步骤 2 遗留⑥ 的**直接可抄路线**：它比 v2 的七步更简（不做阈值化 + 遮罩合成，直接改着色值），比 v3 的连通域更省（不做 compute 连通域）。

### 1.5 边缘检测器：**改用 normal pass** ←（v3 的可靠性问题的正解）

> `Demo Scene: Water Shrine at Night`（`ln6Rpdoc2PQ`，2020-10-26）：
> "The **pixel art edge detector was completely rewritten to use a normal pass**, which is **much more reliable at producing clean results**. I think it's a big step toward looking like hand-drawn isometric pixel art."

⇒ 这是一条**明确的迭代结论**：他先做的是**深度**方案（§1.4），随后**重写为法线通道**，理由是"结果干净得多、更像手绘等距像素画"。
⇒ 对本仓的意义：若走屏幕空间内线，**法线通道优先于深度通道**；深度方案适合做"边缘高光/阴影的提亮压暗"（§1.4）、法线方案适合做"边缘检测"。本仓 [调研-反向壳描边](调研-反向壳描边.md) 与 v3 的连通域路线都只用到深度+法线的**混合判据**，这条给出了优先级。

### 1.6 边缘色随光变化（light-aware edge coloration）←（v5 画面"细线随昼夜变色"的答案）

> `Pixel Art Shader Updates`（`ZsMHY4LDyRE`，2021-01-22）：
> "Here we are showing off the improvement to the main pixel art shader including **light-aware edge coloration**, and some procedural moss."

⇒ **v5 的边线颜色随昼夜变（夜里淡紫、白天近白）就是这一条**：边缘着色**对光照有感知**，不是固定墨色。
⇒ 与本仓 [渲染管线-等距像素卡通](渲染管线-等距像素卡通.md) §5"颜色统一墨色（与 UI 令牌 INK 同色）"**是一处口径分歧**：本仓要的是 3D/2D 统一的固定墨线，他要的是随环境光变化的边缘色。**两条都要留**（固定墨色更"平面设计"、随光变色更"融入场景"），M1 样张可对比。【AI 提案】

### 1.7 动态光：加法 pass 忽略 ramp + **低帧率抖动光位** + 光照档位量化

> `Nighttime Lighting in a Pixel Art Scene`（`VEP4INri_1U`，2020-10-05）：
> "Dynamic lights are simulated using an **additive lighting pass on the base shading, ignoring the color ramp**. To help sell the aesthetic, the light **position and intensity is wiggled at a very low framerate**, and the **lighting level is quantized to a preset number of levels**, to provide the intentional banding effect typical in limited-palette pixel art."

⇒ 三条可迁移：【**加法式附加光，不参与色带**】【**光源位置/强度按极低帧率抖动**（手动风格的闪烁感）】【**光照档位量化**】。
⇒ 与本仓 §6"Additional Lights = 0（仅主平行光）"是**冲突项**：本仓为保色带干净关掉了附加光，而他的做法证明**附加光可以存在**——只要它走加法、绕开 ramp、并把结果量化回档位。这正好回答本仓 §7 记的**"局部暖光池【缺口】：火把/宝箱那种局部暖光"**——**不必走烘焙贴花，可以按他这套加法+量化实现**。【AI 提案】

### 1.8 昼夜：材质变体按太阳角插值

> `Procedural Day/Night Cycle`（`YJSal0UocLY`，2020-11-01）：
> "The material colors are **blended by interpolating between different variants of the materials** for day, night, golden hour, and twilight **depending on the angle of the sun**. This gives the artist full control over the color palette for specific sun angles, while making the day/night system itself entirely procedural."

⇒ 与 v5 抽帧所见一致（`Assets/Materials/DayNightCycle` 里 4 种表面 × 4 个时段 = 16 个材质）。**插值**在变体之间做，不是硬切。
⇒ 提醒本仓：若要做时段变化，**在锁板内插值变体**，而不是复制 N 套资产（呼应 [六视频文档](调研-3D像素渲染视频整合.md) §3.3 的冲突提醒）。

### 1.9 水面：边缘高光用**深度 pass 判"背景深 1 像素"**，折射用屏幕空间像素完美位移图

> `3D Pixel Art Water Shader`（`-E0QaU70twc`，2020-10-18）：
> "The most striking part of pixel art water is the **sharp highlight at the edge of the water**. I am accomplishing this again using the **depth pass**, and drawing the highlight only if **the depth of the background is 1 pixel behind the depth of the water texel being shaded**. This method isn't perfect, but does result in nice looking edges most of the time. The rest of the effect is sold by a **screenspace pixel-perfect horizontal displacement map** for refraction wiggles, and some wiggling particles on the surface."

⇒ 与本仓 §7 的备用件"正交深度线性化"以及 v6 的岸线做法**互相印证**；"背景比水面深 **1 像素**"这个判据比 v6 的"扭采样坐标"更精确、更可控。
⇒ `Realtime Reflections`（`_0PpBepvjFk`）补充：水面反射用**自定义正交光探针**（从观察者视角捕获反射），再与折射混合——**正交相机下的 PBR 反射替身**，比 Planar Reflection 便宜。

### 1.10 树与草：代理体 + sprite；**草直传世界位置/法线防 popping**；GPU instancing；blue noise 撒点

> `Procedural Pixel Art Trees`（`pslJg8vpeHo`，2020-11-08）："Some **jagged spheres** are used as **shadow-casters** and a bunch of **randomly-placed leaf sprites** are drawn over them, **lit using the sphere normal**. … a more evenly-distributed sprite placement, perhaps using **blue noise**, like I did with the grass."
> `Pixel Art Grass Shader V2`（`thUXblUXKeU`，2020-10-04）："To avoid **popping** issues, grass is **shaded directly, by transferring the world position and normal of the base terrain in 3D**, which does not change frame-to-frame. **GPU instancing** is used for performance reasons. A random subset of the grass is given a separate sprite and a randomized color."
> `Wind Animations`（`JMhDEPIJths`，2020-11-09）："turned the grass and leaf sprite sheets into **animations, and animated in the shader via **UV-offset based on position and time**"

⇒ 四条可直接搬到本仓的资产侧（呼应 v5 §3.3 的"代理体 + 撒点"）：**代理体当阴影投射者**、**用代理体法线照亮 sprite**、**sprite 的世界位置/法线从地形直传（防逐帧变化导致的闪烁）**、**蓝色噪声撒点**、**风用 UV 偏移在 shader 里动画**。

### 1.11 地形：**自建 marching-squares 地形网格生成器**（Unity 默认地形拿不到 tile 感）

> `3D Pixel-Art Terrain Authoring`（`mZjSmJ3dxHw`，2021-02-21）：
> "The tile-based aesthetic of pixel art games is difficult to capture using **Unity's default terrain engine, which linearly interpolates a heightmap texture over a variably-subdivided quad**. Instead, we opt to build a **custom terrain mesh generator** that treats the input heightmap as a **low-resolution tile map**, and translates it into a mesh using a **marching-squares-based algorithm** designed to generate pixel-art-like structures including **terraces and slopes**. A custom editor UI is made to author edits to the heightmap…"

⇒ **一条明确的否定结论**：Unity 默认地形（高度图线性插值 + 变细分四边形）**做不出 tile 感**，他改用自建网格生成器（低分辨率高度图当 tile map + marching squares 出台地与斜坡）。
⇒ 对本仓的意义：**若地面要走"台阶/台地"的像素地形，不该基于 Unity Terrain**（v5 的成品里他早期确实用了 Terrain，2021-02 才换成自建生成器——**这是他自己的迭代结论**）。本仓属"岛台/栈桥"这类小面积场景，Blender 手作 + 自定义网格更契合，Unity Terrain 只适合做远景。

### 1.12 视差：近处正交 + 远处透视的混合

> `Parallax Effect for 3D Pixel Art Engine`（`cCUCMBmc9yQ`，2020-12-11）：
> "**cleverly blending between a real perspective camera for distant geometry and the orthographic camera for nearby geometry**. When tuned appropriately, this automatically creates the multilayered parallax effect for distant objects, **while maintaining crisp pixel positioning in the foreground**. A depth-based fog helps tie the scene together."

⇒ 大景深场景（山顶俯瞰）里保住前景像素干净、同时远景有层次的办法。本仓"全场/全景档（正交 size 3~60）"跨 20 倍密度，**这条可作为远景档的候选**（[渲染管线-等距像素卡通](渲染管线-等距像素卡通.md) §3.1 推论 3 的"对远景档降级像素化口径"）。【AI 提案】

### 1.13 火 / 雨 / 云 / 上帝光：四条要点

| 系统 | 作者原文要点 | 可迁移处 |
|---|---|---|
| 火 `erQ8PvxIjS8` | 粒子火渲进 buffer，再**用颜色量化 shader 后处理到视口**；核心/火焰/余烬**跑不同帧率**以模仿手绘动画；仅 100 粒子 | 本仓 §7"特效：粒子 + 全屏像素化自动风格化"**同构**；"不同部件不同帧率"是廉价的手绘感来源 |
| 雨 `ony4o3E0J20` | **八层合成**：雨丝 / 缓坡溅射 / **物体顶缘 spatter** / 水面涟漪 / 雾 / **朝上叶片随机摆动** / 闪电（硬阴影）/ 调色；多数用 GPU + **单次后处理 draw call** | "物体顶缘 spatter"与"朝上叶片摆动"是两个易被忽略的细节层；CPU 零参与 |
| 云影 `_uxV9R3JrXo` | 纯屏幕空间**随相机变化不一致**、纯世界空间**在物体边缘翻滚很出戏**；解法是**混合**：世界空间里假设"世界是平缓浮雕"的近似式与物理正确式混合，思路类似卡通渲染里给角色脸部做**法线平滑** | 与本仓"关实时阴影、用 blob shadow"是同一类取舍的**更精细版本**；云本身用 **3D FBM 空间滚动 + 时间演化** |
| 上帝光 `fSNdZ82I-eQ` | 用太阳的**光源空间阴影图**投影若干 quad 算光柱，"无需任何 raycast/raytrace"；**光柱保持硬边**以保住像素观感 | "硬边"这条与本仓"描边/色带硬切"同一审美纪律 |

### 1.14 粒子光：复用 depthnormals pass 做近似延迟光；屏幕空间阴影用深度图 raymarch

> `Dynamic 3D Pixel Art Particle Lighting`（`0xJqzUHJ2fI`，2021-01-30）：
> "A quick and dirty **light volume shader** uses the **existing depthnormals pass** I'm rendering to compute some pixel-art appropriate approximation of **deferred lighting without having to re-render any geometry** … allowing for a lot more lights in scene without too much overhead. Just for fun, I also implemented a **screenspace shadow option which raymarches the depth map**. It may be useful for creating **pixel-accurate contact shadows** at higher light eccentricities."

⇒ **"屏幕空间 raymarch 深度图做像素级接触阴影"**——本仓 §6 用"blob shadow（脚下贴花椭圆）"替代实时阴影，这条是**更准的替代方案**（且与本仓已关实时阴影相容）。【AI 提案】

### 1.15 纹理侧抗锯齿：deformation gradient + box filter（本仓"伪带限过滤"那格的深度版）

> `Crafting a Better Shader for Pixel Art Upscaling`（`d6tp43wZqps`，2023-05-26，**759s 长片**，20 万播放；投稿到 #SoME3）：讲"rendering pixel art with anti-aliasing"，应用场景是"Minecraft 这类低模游戏里的像素纹理抗锯齿"与"Octopath Traveler 这类 3D 环境里放像素 sprite 的游戏"。字幕要点：
> - 把**像素在纹理空间里的形状**求出来：像素空间里的单位正方形经"从像素空间到纹理空间的 **2×2 deformation gradient**（四阶偏导 ∂u/∂x、∂u/∂y、∂v/∂x、∂v/∂y）"映射 → 取其**轴对齐包围盒（AABB）**当 box filter 尺寸；简化后就是 `|dudx| + |dudy|`（v 同理），**GPU 上可直接用 `ddx`/`ddy`，即内建 `fwidth`**；
> - box 尺寸**钳到至多 1 纹素**（因为"拉伸 box"技巧假设不跨纹素边界）、且**不能为 0**；超出 1 纹素的过滤量靠 **mip map 与各向异性过滤**补，且因为 UV 被手动改过，**必须显式传入原始 UV 的梯度**（改采样函数）才能让 mip/aniso 正确；
> - AABB 会**高估**所需模糊量 → 用略小的 box，或**让 box 中心权重高于边缘**；取**二次型平均窗**时积分出来是三次过渡，**可直接用 `smoothstep` 算**，很便宜；
> - 末尾处理**直通 alpha（straight alpha）**的透明 sprite：透明纹素的颜色未定义，需要单独处理。

⇒ 与本仓 [调研-全屏像素化](调研-全屏像素化与像素完美.md) §5 备用的"**伪带限过滤**（themaister，属纹理侧：2x2 伪带限、不重开 mip）"是**同一个问题的两档解法**：themaister 是"一次 bilinear fetch 的廉价版"，t3ssel8r 这篇是"用 `fwidth` 算足迹 + box 平均 + 显式梯度带 mip/aniso 的完整版"。
⇒ **本仓是否要用**：本仓是**固定等距机位 + 正交**，纹理在屏幕上的压缩是全局常数（§2.1 的 1:0.577），**没有透视导致的纹理游动**，所以风险低；一旦 M-B 样张出现"细纹理在斜面上闪"，这篇就是首选参考（**但那是全分辨率域的操作，注意与"上采样后零颜色操作"红线的关系——它属纹理采样，不属颜色处理**）。【AI 提案】

---

## 2. 与本仓的关系汇总

| 分类 | 条目 |
|---|---|
| **互证本仓已有（不动）** | ① 640×360 + snap + 偏移补偿两步法（§1.1，且**比本仓原引出处更早**）；② 色带 = ramp 过滤标准 BRDF（§1.3）；③ 特效走"粒子 + 全屏像素化自动风格化"（§1.13 火）；④ 方位角 45°（§1.2） |
| **补本仓缺口（新增，优先级高）** | ⑤ **内线 = 深度缓冲邻域比较后提亮/压暗**（§1.4，比 v2/v3 都省）；⑥ **边缘色可随光变化**（§1.6，与"固定墨色"并列做样张）；⑦ **附加光可保留**：加法 + 绕开 ramp + 量化回档位（§1.7，直接解本仓"局部暖光池"缺口）；⑧ **屏幕空间深度图 raymarch 做像素级接触阴影**（§1.14，替代/补充 blob shadow） |
| **需样张裁决（外部证据进裁决点）** | ⑨ **俯角 30° vs 35.264°**（§1.2：该方向公认作者为"2 像素横 1 像素竖"选了 30°）；⑩ 边缘色固定墨 vs 随光（§1.6） |
| **否定性结论（避免走错路）** | ⑪ **Unity 默认地形做不出像素 tile 感**，他改用自建 marching-squares 网格生成器（§1.11）；⑫ 云影**纯屏幕空间与纯世界空间都不行**，必须混合（§1.13） |
| **备用（按需取用）** | ⑬ 视差 = 近正交 + 远透视混合（§1.12）；⑭ 树草的代理体 + sprite 四条纪律（§1.10）；⑭ 水面边缘高光判据"背景深 1 像素"（§1.9）；⑮ 纹理侧 AA 的完整版（§1.15） |

**为什么这份文档值得单独存在**：§1.4 与 §1.5 合起来给出了一条**本仓尚未尝试、但作者亲自迭代过**的内线路线（深度邻域 → 改法线通道），比六支视频里的 v2（七步阈值合成）和 v3（compute 连通域）都更轻；§1.7 则**直接回答**了本仓 §7 记为"缺口"的局部暖光池。两条都不需要新增渲染管线基础设施。

---

## 3. 来源清单（27 条投稿，第一手拉取官方简介）

| ID | 标题 | 时长 | 发布 |
|---|---|---|---|
| `NutO1jzuVXU` | Subpixel Camera for a 3D Pixel Art Game Engine | 12s | 2020-10-20 |
| `ij555s4mAuI` | Isometric Camera | 29s | 2020-10-20 |
| `Lm5tRVNQxFs` | Pixel Art Block Shader | 22s | 2020-10-20 |
| `XPA4kKOnIt8` | Pixel Art Grass Shader | 41s | 2020-10-20 |
| `thUXblUXKeU` | Pixel Art Grass Shader V2 | 51s | 2020-10-20 |
| `VEP4INri_1U` | Nighttime Lighting in a Pixel Art Scene | 23s | 2020-10-20 |
| `-E0QaU70twc` | 3D Pixel Art Water Shader | 10s | 2020-10-20 |
| `_0PpBepvjFk` | Realtime Reflections in a 3D Pixel Art Scene | 13s | 2020-10-20 |
| `a0Sv5xqvmzU` | Nighttime Dynamic Pixel Art Water Level | 15s | 2020-10-20 |
| `_uxV9R3JrXo` | Cloud Shader with a Pixel Art Aesthetic | 18s | 2020-10-20 |
| `ln6Rpdoc2PQ` | Demo Scene: Water Shrine at Night | 23s | 2020-10-26 |
| `YJSal0UocLY` | Procedural Day/Night Cycle | 114s | 2020-11-01 |
| `pslJg8vpeHo` | Procedural Pixel Art Trees | 25s | 2020-11-08 |
| `JMhDEPIJths` | Wind Animations | 27s | 2020-11-09 |
| `ERA7-I5nPAU` | **Creating a Scene for my 3D Pixel Art Game**（= 六视频里的 v5） | 300s | 2020-11-15 |
| `erQ8PvxIjS8` | Procedural Pixel Art Fire | 20s | 2020-11-23 |
| `ony4o3E0J20` | Pixel Art Rain Shader | 23s | 2020-11-30 |
| `cCUCMBmc9yQ` | Parallax Effect for 3D Pixel Art Engine | 22s | 2020-12-11 |
| `ZsMHY4LDyRE` | Pixel Art Shader Updates | 57s | 2021-01-22 |
| `fSNdZ82I-eQ` | God Rays in 3D Pixel Art Game Engine | 60s | 2021-01-24 |
| `0xJqzUHJ2fI` | Dynamic 3D Pixel Art Particle Lighting | 84s | 2021-01-30 |
| `mZjSmJ3dxHw` | 3D Pixel-Art Terrain Authoring | 75s | 2021-02-22 |
| `AcVDCCNqvzg` | Fire/Rain/Grass Interaction System | 24s | 2021-04-02 |
| `39A0n24PX8g` | Creating Particle Effects Without Particles | 210s | 2021-04-29 |
| `1FrIBkuq0ZI` | Making an Animation for my 3D Pixel Art Game | 440s | 2021-06-12 |
| `yGci-Lb87zs` | Designing a Better Aim Assist for 2D Games | 644s | 2021-11-28 |
| `KPoeNZZ6H4s` | Giving Personality to Procedural Animations using Math | 929s | 2022-06-29 |
| `d6tp43wZqps` | **Crafting a Better Shader for Pixel Art Upscaling** | 759s | 2023-05-26 |

**简介原文汇总**（本文全部引用的出处）：`external/pixel-render-ref/t3ssel8r-desc.txt`

---

## 4. 存疑与边界

1. **本文只读了他的官方简介与两条字幕，没有逐条看视频**。凡简介未写的实现细节（例如"深度邻域比较"具体取几个邻点、提亮/压暗幅度怎么定、"normal pass 边缘检测"的阈值形式），**本文没有也无法给出**——要拿这些必须逐个下载视频看画面（时间戳线索：作者多数简介只给一句话，画面里才有参数）。
2. **他 2020-10 的深度边缘方案与 2020-10-26 的"重写法线通道"之间的演替关系**：本文按时间线推断"深度 → 法线"是迭代（先 `Lm5tRVNQxFs` 深度、后 `ln6Rpdoc2PQ` 法线重写），**但两条简介没有明说前者被后者取代**——也可能两者并存（深度管提亮/压暗、法线管检测）。引用时请按"并存"保守理解。
3. **他的引擎不是"等距像素卡通"风格**：他走的是"手绘等距像素画"（30° + 2:1 + 随光边缘色），本仓的 Art Bible 走的是"赛璐璐 + 反向壳墨线 + 真等距 35.264°"。**两者是近亲但不同族**，凡引用他的口径都要过一遍本仓的裁决表，不能整包照搬。
4. **`Crafting a Better Shader for Pixel Art Upscaling` 的 759s 字幕我只读了后半段**（"the solution / final shader"部分），前半段的"问题陈述 / 第一个 shader / 失真 / box filter"只按简介的时间戳与后半段的回溯推断，未逐句核对。
5. **他的引擎后期转向完全延迟管线**（`0xJqzUHJ2fI` 自述"In the future, I will likely move the entire engine over to a fully deferred pipeline"）——若引用其光照做法，注意那是**前延迟时代**的方案。
