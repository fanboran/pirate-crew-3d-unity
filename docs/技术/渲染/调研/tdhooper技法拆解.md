# tdhooper 六作技法拆解

> 对象：external/shadertoy/ 下 6 个 tdhooper 作品存档（存档不入库，配套视觉证据在各存档目录 `preview.jpg` 与 `render.png`）。
> 关联文档：[Shadertoy分级特征.md](Shadertoy分级特征.md)（分级口径与 S 档壁纸公式）。

## 〇、作者主线

6 个作品出自同一作者 tdhooper，主线高度一致：**用对称数学把简单图形铺满球面/空间，再用非传统渲染（辉光、色散、景深）而非标准光照呈现**。效果惊艳的根子不在模型复杂度，而在「空间变换 + 一步进一发光」的渲染思路。

| 作品 | 存档目录（external/shadertoy/） | 行数 | pass | 一句话 |
| --- | --- | --- | --- | --- |
| Bubble rings | `WdB3Dw_bubble-rings/` | 158 | image | 四维环面的三维投影泡泡 |
| Geodesic tiling | `llVXRd_geodesic-tiling/` | 706 | image | 测地穹顶瓦片（S 档） |
| Shaderdough fairy | `4lGyW1_shaderdough-fairy/` | 338 | image | 十二轴扭曲彩虹面团 |
| Shuffle box | `7t3SW8_shuffle-box/` | 462 | image | 光谱色散玻璃魔方 |
| Fractured Orb | `ttycWW_fractured-orb/` | 606 | image+buffer | 足球逐面爆裂 |
| HardSurface Sphere [2] | `fdGfW1_hardsurface-sphere-2/` | 1064 | image+buffer+common | 2D 图案→3D 硬表面管线 |

## 一、Bubble rings —— 四维几何的三维投影

**效果**：悬浮的彩虹泡泡环，中段扭曲成喇叭口，边缘金色轮廓光，肥皂泡膜质感。

**核心技法**（`external/shadertoy/WdB3Dw_bubble-rings/a_Image.glsl`）：

1. **反向球极投影**：`k = 2/(1+dot(p,p)); vec4(k*p, k-1)` 把三维空间每点映射到四维球面；`fTorus(p4)` 在四维空间算 Clifford 环面距离。画面里的"扭曲圆环"是四维几何体的投影，模型不存在于三维空间。
2. **距离场修正** `fixDistance`：投影缩放率 k 随位置变化，SDF 除回 k 再做非线性修正，防步进穿模。
3. **辉光步进替代表面光照**：全 shader 无法线、无光照模型——每步进一次加一点光（贴面蓝绿光 + 每步紫色环境光 + 距离雾），迭代故意压低（82 步）让光线"穿不透"模型，形成毛玻璃感。
4. 调色用 iq 的 `spectrum()` 余弦调色板，tonemap 用非常规 pow 链（^1.8 → ^2×3 → ^1/2.2）拉中间调。

**可迁移**：辉光步进公式 `c += max(0, .01-abs(d)) * tint` 直接可用于火把/魔法/幽光体渲染，十几行出氛围。

## 二、Geodesic tiling —— 球面铺瓦的标准答案

**效果**：圆角六边形瓦片组成的测地穹顶，瓦面波浪起伏、缝隙呼吸，三套动画交叉淡化。

**核心技法**（`external/shadertoy/llVXRd_geodesic-tiling/a_Image.glsl`）：

1. **二十面体域折叠**（源自 knighty）：`pModIcosahedron` 用 abs + 三次 `pReflect` 把整个球面折叠到二十面体一个基本三角形——球面任意点 O(1) 判定所属瓦片，无需网格结构。
2. **基本面三角形铺瓦**（源自 mattz）：六边形坐标系 `cart2hex` 做 floor/fract 找最近三角形，得三顶点+三边中点；`faceToSphere` 把 2D 坐标投回球面，测地穹顶顶点即生成。
3. **瓦片 SDF**：两片平面夹出边缘（带 gap）+ 内外球壳挖空 + `smax` 圆角成厚瓦；参数集中在 `HexSpec`（高度/厚度/缝隙/圆角）。
4. **参数动画编排**：三套动画（细分度波动 / 高度波浪 / 缝隙呼吸）都是 `HexSpec` 的时间函数，`transitionValues` 用 cosine 缓动交叉淡化——动画写在 SDF 参数上，不写在模型上。

**可迁移**：「域折叠让球面铺满图案」与「动画写在参数上」两条通则适用于海浪起伏、船体装饰纹。

## 三、Shaderdough fairy —— 十二轴扭曲的面团

**效果**：彩虹玻璃质感的扭曲面团/蝴蝶结，霓虹描边。

**核心技法**（`external/shadertoy/4lGyW1_shaderdough-fairy/a_Image.glsl`）：

1. **二十面体顶点轴扭曲**：`icosahedronVertex` 找最近顶点（12 个），`icosahedronAxisDistance` 算点到轴的归一化距离（棱处用 `blend = pow(1-boundryDist, 6)` 平滑混合两种度量），`pTwistIcosahedron` 绕轴按距离比例旋转空间——12 个方向同时搓卷，空间成面团。
2. **逐面着色**：`icosahedronIndex` 给出面编号（1~20），按编号取 spectrum 色；`COLOUR_CYCLE` 使色环随时间轮转，`SEAMLESS_LOOP` 用 `pModPolar` 三重对称实现无缝循环。
3. **霓虹描边辉光核**：`color += m.colour * pow(max(0,(.02-h))*19.5, 10) * 150` —— `pow(k·(ε-d), n)` 形成紧贴轮廓的描边光；fudge factor 压到 0.2 保证扭曲域内步进稳定。

**可迁移**：绕「最近对称轴」按距离扭曲空间是通用变形器（旗/水流/果冻）；`pow(k·(ε-d), n)` 是描边光效万能公式。

## 四、Shuffle box —— 单 pass 光谱色散追踪

**效果**：会"洗牌"呼吸的玻璃魔方，边缘折射出真彩虹条纹，背景六边形点阵漂移。

**核心技法**（`external/shadertoy/7t3SW8_shuffle-box/a_Image.glsl`）：

1. **域扭曲动画**：`p += sin(p*8 + time·vec3(1,3,2)·2π)·0.1` 让盒子空间果冻化（"洗牌"感的全部来源）；再叠 `sin3(p*80)·0.0015` 微噪声防条带。
2. **光谱色散**：每像素发 10 条光线，各带波长 `λ`，折射率 `ior = mix(1.2, 1.8, λ)`（λ 用 iChannel0 噪声抖动）；玻璃内外反复折射最多 5 次弹射（含全内反射），最终按 `spectrum(-λ+.25)` 加权求和——彩虹是真算出来的。
3. **内外步进切换**：`invert` 翻转 SDF 符号实现在玻璃内部继续步进；作者自注 `invert *= -1` 物理不正确但效果更有趣。
4. **性能三板斧**：首 hit 在所有色散光线间复用；解析软箱灯（`intersectPlane` + smoothstep 盒）当环境光；屏幕空间六边形网格（Fabrice 的 hex tiling + `fwidth` 抗锯齿）做背景装饰。

**可迁移**：色散棱镜适合宝藏/水晶高光；域扭曲是「活着的物体」（水/旗/果冻）最廉价实现。

## 五、Fractured Orb —— 足球面的逐片爆裂

**效果**：白瓷足球解体，六边形/五边形面板逐个弹出、果冻回弹、归位；玻璃质感 + 浅景深。

**核心技法**（`external/shadertoy/ttycWW_fractured-orb/b_Buffer_A.glsl` + `a_Image.glsl`）：

1. **截角二十面体（足球）面分解**：`icosahedronVertex` + `dodecahedronVertex`（iq 无分支版）找最近两种顶点，`erot` 绕轴 ±72° 旋转生成邻居，凑出一个五边形面的 4 个角点；面 = 4 个半空间 `max` 出的边界与球壳 `smax` 相交。
2. **只算最近 4 个面**：`for i<4` 每轮把四顶点轮转一位覆盖相邻面——DE 干净且只花 4 份算力。
3. **逐面爆裂动画**：每面相位 `t = mod(time·2/3 + dot(va.xy,(1,-1))/30, 1)`；爆裂包络「快出慢回」`1-pow(1-t2,10)` × `(1-pow(t2,5))`；回弹用 `expImpulse` 衰减正弦做果冻抖动。相位由位置决定，爆炸如波绕球传播。
4. **双 pass 架构**：Buffer A 把深度写进 alpha；Image pass 做黄金角散景景深（单 pass DoF，tuxedolabs 算法）+ filmic tonemap——浅景深是壁纸感的最后一块拼图。
5. 色散折射引擎与 Shuffle box 同源（作者注明为两个旧作 mashup）。

**可迁移**：「同一段动画 + 逐部件相位偏移」是群体动画最省公式（船板碎裂、金币群、船员动作波）；深度写 alpha + 后期景深是无引擎项目加电影感的标准打法。

## 六、HardSurface Sphere [2] —— 2D 图案到 3D 球面的完整管线

**效果**：白色硬表面机甲球，三角形面板拼接、凹槽绿光、面板按时钟弹跳换位，背景发光曼陀罗。

**核心技法**（`external/shadertoy/fdGfW1_hardsurface-sphere-2/` 的 `c_Common.glsl` + `b_Buffer_A.glsl` + `a_Image.glsl`）：

1. **双 pass G-buffer 管线**：Buffer A 不渲染 3D——在面板 UV 空间做**三角形 Voronoi**（距离度量为等边三角形半平面 `sdTriEdges`，边界距离 `sdBorder` 精确算角点），两层网格（不同缩放 + 转 120°）交错裁剪，输出 `[边界距离, 网格id, 瓦片类型, mask]` 四通道。
2. **球面反投影采样**：Image pass 用 `fold(p)`（knighty 折叠）把球面点折到基本面，`pp = p/dot(p,face)` 心射投影得面内 UV——用该 UV 采样 Buffer A 图案，2D 图案无缝贴上球面；再按边界距离挖凹槽、`cmin/cmax` 倒角成硬表面浮雕。
3. **步进时钟编排**：`tFloor/tFract` 把连续时间切成整数拍，`easeSnap/easeOutBack`（回弹缓动）驱动网格在整拍换位——「编舞」式动画系统，与 Common 里的时间工具（`initTime`/`gDuration`/循环模式）配套。
4. 标准件：双光源 Blinn-Phong + 菲涅尔高光、`iSphere` 包围球提前裁剪、iq 2×2 抖动 AA、lift/gamma/gain 调色（tropical trevor）、NIGHT_MODE/LOOP 等编译期开关做预览变体。

**可迁移**：「2D 图案 UV → 球体硬表面」管线可给球状物（宝珠/炮弹/星球）免建模贴程序化浮雕；步进时钟 + 回弹缓动是 UI 与场景动画的通用编舞。

## 七、tdhooper 工具箱（六作共享的可复用件）

| 部件 | 来源 | 用途 |
| --- | --- | --- |
| HG_SDF（pR / smax / pReflect / pModPolar…） | HG_SDF (Xs3GRB) | SDF 标准工具库，六作全用 |
| `spectrum()` 余弦调色板 | iq (ll2GD3) | 单参数出彩虹；色环 / 描边 / 色散加权 |
| 二十面体域折叠 | knighty (MsKGzw) | 球面图案 O(1) 索引，三作用 |
| 辉光步进 `Σ max(0,ε-\|d\|)·tint` | 自有 | 无光照体渲染，暗场发光的根 |
| 光谱色散折射循环 | 自有 | 真彩虹折射，玻璃 / 水晶专属 |
| 步进时钟 + 回弹缓动（easeOutBack / easeSnap） | 自有 / skaplun | 动画编舞；参数动画不动画模型 |
| 深度写 alpha + 黄金角景深 | tuxedolabs | 后期电影感 |
| `erot` 紧凑轴旋转 | blackle | 多面体邻居顶点生成 |

## 八、项目落点（等距像素海盗游戏）

1. **最该抄的三件**：辉光步进（火把 / 炮口焰 / 幽灵，十几行出效果）、逐面相位爆裂（船板碎裂 / 金币群）、域扭曲（海浪 / 旗 / 果冻 UI）。
2. **海盗题材对症**：Bubble rings 的泡泡膜感适合水下气泡；Fractured Orb 的面板爆裂即船体受击碎裂方案；Geodesic tiling 的参数动画思路可做船帆鼓风。
3. **风格提醒**（接分级特征分析的 S 档壁纸公式）：这批的高完成度 = 净背景 + 单主体 + 后期景深，主菜单背景球路线可行。
