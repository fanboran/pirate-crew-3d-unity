# -*- coding: utf-8 -*-
"""
pixel_export —— 等距像素卡通资产导出模板（资产篇 §6 的三件套，可被 kit 脚本 import）。

【它解决什么问题】
    Kit 脚本（tools/blender/scene/<kit>/*.py）导出的 FBX 在**新美术口径下缺三样东西**：
      ① 平滑法线没有烘进顶点色 ⇒ 反壳描边的外扩方向在硬边处开裂（渲染篇 §5）；
      ② 没有量化步骤 ⇒ 顶点色/贴图色漂在全局调色板之外，无法参与逐字节回归；
      ③ 没有纹素密度校验 ⇒ 32px/m 的密度纪律只能靠人眼看，量产后必然失控。
    本模块把这三件事做成三个纯函数 + 一个导出包装，kit 脚本 import 即可用。

【三个函数的契约】
    bake_smooth_normals_to_vertex_colors(obj)   平滑法线 → 顶点色 GBA（R 留阈值偏移）
    quantize_vertex_colors(obj, palette)        平涂顶点色 → OkLab 最近邻锁板
    check_texel_density(objects, ...)           密度 32px/m ±0.5% + 每面 Jacobian < 1.01

【顶点色的两个互斥用途（本模块的硬边界）】
    顶点色在这条管线里承载两种**完全不同**的数据：
      · 数据通道：<b>SmoothNormal</b> —— R = 色带阈值偏移（0.5 中性），GBA = 平滑法线（×0.5+0.5）
        （渲染篇 §5 / 裁决点 #8）；反壳描边 Pass 读它决定外扩方向。
      · 平涂色：<b>Col</b>（Blender 默认名）—— 美术刷的装饰颜色，是"颜色"，可以量化。
    把后者当前者量化（或反过来）会**静默毁掉描边**（法线被映射到板色上，外扩方向全错）。
    所以 quantize_vertex_colors 只接受 Col 一类的平涂属性名，遇到 SmoothNormal 直接抛错。

【确定性（资产篇 §4 末段）】
    本模块零随机数：平滑法线是角度加权平均（无采样）、量化是暴力 O(N×64) 最近邻
    （无数据结构差异）、密度是解析计算。同输入同输出，跨机器一致。

【与 Python 侧量化工具的一致性】
    OkLab 矩阵与加权距离在 tools/palette/palette_tool.py 里有一份等价实现。两份**故意不共享代码**
    （Blender 里没有 PIL，共享会把 blender 侧拖进无效依赖），改为各自用 Ottosson 官方测试表钉住 ——
    两个独立实现同时通过同一张官方表，比共享一份实现更能证明转换没写错。
    本模块每次 import 都会跑一次该回归（oklab_selftest），漂移在运行期就暴露。

用法（kit 脚本里）:
    import sys, os
    sys.path.insert(0, os.path.join(REPO_ROOT, "tools", "blender", "export"))
    import pixel_export as PX
    PX.oklab_selftest()
    PX.bake_smooth_normals_to_vertex_colors(obj)
    PX.quantize_vertex_colors(obj, PX.load_palette())
    PX.assert_texel_density([obj], texel_size_px=128)
    PX.export_fbx([obj], fbx_path)
"""

import json
import math
import os

# ---------------------------------------------------------------------------
# 常量与默认值
# ---------------------------------------------------------------------------

#: 全项目纹素密度目标（px/米，资产篇 §1）
TARGET_PX_PER_METER = 32.0

#: 密度容差（资产篇 §6；±0.5%）
DENSITY_TOLERANCE = 0.005

#: 每面 Jacobian 长短轴比上限（texel 方正性；超限比密度误差更显眼）
JACOBIAN_LIMIT = 1.01

#: 平滑法线顶点色的属性名（= Unity 侧 shader 读的通道名；改名必须同步 shader 与文档）
SMOOTH_NORMAL_ATTRIBUTE = "SmoothNormal"

#: 平涂色顶点色的属性名（Blender 默认名；只有这个名字的属性能被量化）
FLAT_COLOR_ATTRIBUTE = "Col"

#: 位置容差合并的容差（米）。1e-4 = 0.1mm，远小于任何美术尺度，只用来把"同一个点被拆成两个"
#: 的硬边顶点合回来（硬边顶点位置完全相同，只有法线不同）。
WELD_TOLERANCE = 1e-4

#: 8bit 顶点色的中性值（编码 v → v×0.5+0.5）
ENCODE_NEUTRAL = 0.5

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
DEFAULT_PALETTE_JSON = os.path.join(REPO_ROOT, "pirate-crew", "Assets", "Data", "Palette",
                                    "pirate_palette.json")


# ---------------------------------------------------------------------------
# OkLab（与 palette_tool.py 同一组 Ottosson 2021 修订版矩阵）
# ---------------------------------------------------------------------------

_XYZ_TO_LMS = (
    (0.8190224379967030, 0.3619062600528904, -0.1288737815209879),
    (0.0329836539323885, 0.9292868615863434, 0.0361446663506424),
    (0.0481771893596242, 0.2642395317527308, 0.6335478284694309),
)
_LMS_TO_LAB = (
    (0.2104542683093140, 0.7936177747023054, -0.0040720430116193),
    (1.9779985324311684, -2.4285922420485799, 0.4505937096174110),
    (0.0259040424655478, 0.7827717124575296, -0.8086757549230774),
)
_SRGB_TO_XYZ = (
    (0.4123907992659595, 0.3575843393838780, 0.1804807884018343),
    (0.2126390058715104, 0.7151686787677559, 0.0721923153607337),
    (0.0193308187155918, 0.1191947797946259, 0.9505321522496608),
)

#: Ottosson 原文公布的 XYZ→Oklab 参考值（容差 1e-3）
OKLAB_REFERENCE = (
    ((0.950, 1.000, 1.089), (1.000, 0.000, 0.000)),
    ((1.000, 0.000, 0.000), (0.450, 1.236, -0.019)),
    ((0.000, 1.000, 0.000), (0.922, -0.671, 0.263)),
    ((0.000, 0.000, 1.000), (0.153, -1.415, -0.449)),
)


def _mat3_mul(m, v):
    return tuple(m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] for i in range(3))


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def linear_srgb_to_oklab(rgb):
    """线性 sRGB（0~1，可 >1 / <0）→ OkLab。**不做非负钳位**（负 LMS 的实立方根是合法解）。"""
    xyz = _mat3_mul(_SRGB_TO_XYZ, rgb)
    lms = _mat3_mul(_XYZ_TO_LMS, xyz)
    return _mat3_mul(_LMS_TO_LAB, tuple(_cbrt(v) for v in lms))


def _cbrt(x):
    return math.copysign(abs(x) ** (1.0 / 3.0), x)


def srgb8_to_oklab(rgb8):
    return linear_srgb_to_oklab(tuple(srgb_to_linear(v / 255.0) for v in rgb8))


def oklab_selftest(tolerance=1e-3):
    """对 Ottosson 官方测试表做回归；失败直接抛错（不让漂移的转换继续跑量化）。"""
    worst = 0.0
    for xyz, ref in OKLAB_REFERENCE:
        lms = _mat3_mul(_XYZ_TO_LMS, xyz)
        got = _mat3_mul(_LMS_TO_LAB, tuple(_cbrt(v) for v in lms))
        worst = max(worst, max(abs(got[i] - ref[i]) for i in range(3)))
    if worst > tolerance:
        raise AssertionError("pixel_export.srgb8_to_oklab 与 Ottosson 官方测试表不符：最大偏差 %.6f > %g"
                             % (worst, tolerance))
    return worst


# ---------------------------------------------------------------------------
# 调色板
# ---------------------------------------------------------------------------

def load_palette(path=None):
    """读 <pid>palette_tool.py</pid> 写出的 JSON 真源，返回 [(id, (r,g,b)), ...]（sRGB 8bit）。"""
    path = path or DEFAULT_PALETTE_JSON
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    out = []
    for slot in data["slots"]:
        h = slot["hex"].lstrip("#")
        out.append((slot["id"], (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))))
    if not out:
        raise ValueError("调色板 %s 里没有槽位" % path)
    return out


def load_palette_lab(path=None, weights=1.0):
    """返回 (ids, lab 表, rgb 表)，量化时直接用。"""
    entries = load_palette(path)
    ids = [e[0] for e in entries]
    rgb = [e[1] for e in entries]
    lab = [srgb8_to_oklab(c) for c in rgb]
    return ids, lab, rgb


def nearest_palette_index(rgb01, lab_table, weight=1.0):
    """OkLab 加权最近邻（d = ΔL² + w·(Δa²+Δb²)）；rgb01 为 0~1 的 sRGB 三元组。"""
    target = linear_srgb_to_oklab(tuple(srgb_to_linear(max(0.0, min(1.0, c))) for c in rgb01))
    best_i, best_d = 0, None
    for i, p in enumerate(lab_table):
        dl = target[0] - p[0]
        da = target[1] - p[1]
        db = target[2] - p[2]
        d = dl * dl + weight * (da * da + db * db)
        if best_d is None or d < best_d:
            best_i, best_d = i, d
    return best_i, best_d


# ---------------------------------------------------------------------------
# ① 平滑法线 → 顶点色（反壳描边的外扩方向数据）
# ---------------------------------------------------------------------------

def smooth_normals_per_corner(mesh, angle_weighted=True, weld_tolerance=WELD_TOLERANCE):
    """算出每个 loop（corner）应该写的**平滑法线**，返回 [Vector]（长度 = len(mesh.loops)）。

    算法（资产篇 §6 第 1 条 / 调研-反向壳 §3）：
      1. **按位置合并**：位置在 weld_tolerance 内的顶点视为几何同一点（硬边顶点位置相同、
      只有法线不同，正是要合并的对象）；
      2. 对每个多边形朝该多边形的每个角累加 `面法线 × 权重`，权重 = 该角的角度
      （角度加权优于等权平均：细长三角形不会因为面积/角度失衡而拖偏平均方向）；
      3. 每个角取「它所属合并组」的平均方向，归一化。

    【为什么不用 mesh.corner_normals】corner_normals 就是**分裂后的**着色法线，硬边处本来就
    一分为二——拿它烘顶点色等于把开裂原样搬进数据里，反壳描边照旧会裂。
    """
    groups = _weld_groups(mesh, weld_tolerance)
    acc = {}
    for poly in mesh.polygons:
        if len(poly.vertices) < 3:
            continue
        fn = poly.normal.copy()
        loops = list(poly.loop_indices)
        n = len(loops)
        for k in range(n):
            loop_index = loops[k]
            vertex_index = mesh.loops[loop_index].vertex_index
            key = groups[vertex_index]
            if angle_weighted:
                prev_v = mesh.vertices[mesh.loops[loops[(k - 1) % n]].vertex_index].co
                next_v = mesh.vertices[mesh.loops[loops[(k + 1) % n]].vertex_index].co
                cur = mesh.vertices[vertex_index].co
                a = prev_v - cur
                b = next_v - cur
                weight = a.angle(b) if (a.length > 0 and b.length > 0) else 0.0
                if weight <= 1e-9:
                    weight = 1e-9
            else:
                weight = 1.0
            entry = acc.get(key)
            if entry is None:
                acc[key] = [fn * weight, None]
            else:
                entry[0] = entry[0] + fn * weight

    result = []
    for loop in mesh.loops:
        key = groups[loop.vertex_index]
        entry = acc.get(key)
        if entry is None:
            result.append(loop.normal.copy())
            continue
        v = entry[0]
        if v.length < 1e-9:
            result.append(loop.normal.copy())
        else:
            result.append(v.normalized())
    return result


def _weld_groups(mesh, tol):
    """位置容差合并：返回 [vertex_index -> group_key]（key 是空间格坐标的整数三元组）。"""
    inv = 1.0 / max(tol, 1e-9)
    groups = []
    for v in mesh.vertices:
        groups.append((int(math.floor(v.co.x * inv + 0.5)),
                       int(math.floor(v.co.y * inv + 0.5)),
                       int(math.floor(v.co.z * inv + 0.5))))
    return groups


def bake_smooth_normals_to_vertex_colors(obj, attribute=SMOOTH_NORMAL_ATTRIBUTE,
                                         threshold=ENCODE_NEUTRAL, angle_weighted=True,
                                         weld_tolerance=WELD_TOLERANCE, overwrite=True):
    """把平滑法线烘进顶点色：R = 阈值偏移（默认 0.5 中性），GBA = 法线（×0.5+0.5）。

    返回写入的 (min, max) 通道范围 dict（便于调用方判断是否真的写进去了）。
    """
    mesh = obj.data
    existing = mesh.color_attributes.get(attribute)
    if existing is not None:
        if not overwrite:
            raise RuntimeError("%s 已有顶点色属性 %r（overwrite=False）" % (obj.name, attribute))
        mesh.color_attributes.remove(existing)

    attr = mesh.color_attributes.new(name=attribute, type="BYTE_COLOR", domain="CORNER")
    normals = smooth_normals_per_corner(mesh, angle_weighted=angle_weighted,
                                        weld_tolerance=weld_tolerance)
    for i, loop in enumerate(mesh.loops):
        n = normals[i]
        attr.data[i].color = (
            _clamp01(threshold),
            _clamp01(n.x * 0.5 + 0.5),
            _clamp01(n.y * 0.5 + 0.5),
            _clamp01(n.z * 0.5 + 0.5),
        )
    mesh.update()
    return _channel_range(attr)


def _channel_range(attr):
    lo = [1.0] * 4
    hi = [0.0] * 4
    for d in attr.data:
        c = d.color
        for i in range(4):
            lo[i] = min(lo[i], c[i])
            hi[i] = max(hi[i], c[i])
    return {"min": tuple(round(v, 4) for v in lo), "max": tuple(round(v, 4) for v in hi)}


def _clamp01(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


# ---------------------------------------------------------------------------
# ② 顶点色量化（只作用于平涂色属性，绝不碰 SmoothNormal）
# ---------------------------------------------------------------------------

def quantize_vertex_colors(obj, palette_entries=None, attribute=FLAT_COLOR_ATTRIBUTE, weight=1.0):
    """把**平涂色**顶点色属性量化到全局板（OkLab 最近邻）；返回统计 dict。

    【硬边界】attribute 名字含 "SmoothNormal"（或等于 SMOOTH_NORMAL_ATTRIBUTE）时直接抛错：
    那是反壳描边的外扩方向数据，量化它 = 把法线映射到板色上 = 描边方向全错（且画面不一定立刻炸，
    属于最难查的一类静默故障）。要量化的是美术刷出来的平涂色属性（Blender 默认名 Col）。
    """
    if attribute == SMOOTH_NORMAL_ATTRIBUTE or "SmoothNormal" in attribute:
        raise ValueError("拒绝对 %r 量化：该属性承载反壳描边的平滑法线数据，不是颜色"
                         "（渲染篇 §5 / 裁决点 #8）。平涂色属性名应是 %r。"
                         % (attribute, FLAT_COLOR_ATTRIBUTE))

    mesh = obj.data
    attr = mesh.color_attributes.get(attribute)
    if attr is None:
        raise KeyError("%s 上没有顶点色属性 %r（可量化属性：%s）"
                       % (obj.name, attribute,
                          [a.name for a in mesh.color_attributes] or "无"))

    ids, lab_table, rgb_table = (palette_entries if palette_entries is not None
                                 else load_palette_lab())
    changed = 0
    used = set()
    worst_shift = 0.0
    for d in attr.data:
        c = d.color
        src = (c[0], c[1], c[2])
        idx, dist = nearest_palette_index(src, lab_table, weight)
        target = rgb_table[idx]
        dst = (target[0] / 255.0, target[1] / 255.0, target[2] / 255.0)
        if any(abs(src[i] - dst[i]) > 1e-6 for i in range(3)):
            changed += 1
            worst_shift = max(worst_shift, math.sqrt(dist))
        d.color = (dst[0], dst[1], dst[2], c[3])
        used.add(ids[idx])
    mesh.update()
    return {
        "attribute": attribute,
        "corners": len(attr.data),
        "changed": changed,
        "colors_used": len(used),
        "palette_colors": len(ids),
        "worst_oklab_shift": round(worst_shift, 5),
    }


# ---------------------------------------------------------------------------
# ③ 纹素密度校验（32px/m ±0.5% + 每面 Jacobian < 1.01）
# ---------------------------------------------------------------------------

class DensityResult(object):
    """一个资产的密度体检结果（可 JSON 化，进报告）。"""

    def __init__(self, name):
        self.name = name
        self.meshes = 0
        self.triangles = 0
        self.uvless_meshes = 0
        self.world_area = 0.0
        self.uv_area = 0.0
        self.density = 0.0
        self.density_ok = False
        self.jacobian_max = 0.0
        self.jacobian_ok = True
        self.face_density_outliers = 0
        self.problems = []

    def to_dict(self):
        return {
            "name": self.name,
            "meshes": self.meshes,
            "triangles": self.triangles,
            "uvlessMeshes": self.uvless_meshes,
            "worldArea": round(self.world_area, 6),
            "uvArea": round(self.uv_area, 6),
            "densityPxPerMeter": round(self.density, 4),
            "densityOk": self.density_ok,
            "jacobianMax": round(self.jacobian_max, 6),
            "jacobianOk": self.jacobian_ok,
            "faceDensityOutliers": self.face_density_outliers,
            "problems": list(self.problems),
        }


def check_texel_density(objects, texel_size_px, target=TARGET_PX_PER_METER,
                        tolerance=DENSITY_TOLERANCE, jacobian_limit=JACOBIAN_LIMIT,
                        name=None):
    """核算纹素密度与 texel 方正性；**不合规写进 problems，由调用方决定是否抛**。

    公式（资产篇 §6 第 3 条 / 调研 §4）：
        density = tex_size × √(uv_area ÷ world_area)      [px/米] —— **必须开方**
        （`uv_area × tex_size ÷ world_area` 的量纲是 px²/m²，只在 world_area=1 时数值碰巧相等）
        每面 Jacobian 长短轴比 = σ_max/σ_min（世界→UV 的 2×2 线性映射的奇异值比）

    区间口径（本项目裁决）：
      · **聚合**密度必须落在 target ±tolerance（±0.5%）—— 这是硬判据，不合规 = problems + 失败；
      · 每面 Jacobian 必须 < jacobian_limit（1.01）—— 硬判据（点采样下非方形 texel 比密度误差更显眼）；
      · 单面密度对聚合值的偏离**只计数不失败**：非均匀 UV 展开（浮雕/倒角面）天然有分布，
        把它当失败会让判据失去意义；计数进报告供人判断。

    无 UV 网格 = 直接记一条 problem（密度无从谈起，且这正是存量 WorldKit 47 件的现状）。
    """
    result = DensityResult(name or (objects[0].name if objects else "?"))
    for obj in objects:
        if obj.type != "MESH":
            continue
        result.meshes += 1
        mesh = obj.data
        uv_layer = mesh.uv_layers.active
        if uv_layer is None:
            result.uvless_meshes += 1
            result.problems.append("%s：没有 UV 层（密度无从核算 —— 先按纹素密度展开 UV）" % obj.name)
            continue

        mw = obj.matrix_world
        face_densities = []
        for poly in mesh.polygons:
            loops = list(poly.loop_indices)
            if len(loops) < 3:
                continue
            world = [mw @ mesh.vertices[mesh.loops[li].vertex_index].co for li in loops]
            uvs = [tuple(uv_layer.data[li].uv) for li in loops]

            # 扇形三角化（凸多边形精确；凹多边形在 kit 资产里不出现，出现也不影响密度聚合）
            for k in range(1, len(loops) - 1):
                tri_w = (world[0], world[k], world[k + 1])
                tri_uv = (uvs[0], uvs[k], uvs[k + 1])
                wa = _triangle_area_world(tri_w)
                ua = abs(_triangle_area_uv(tri_uv))
                result.triangles += 1
                result.world_area += wa
                result.uv_area += ua
                if wa > 1e-12 and ua > 1e-16:
                    face_densities.append(texel_size_px * math.sqrt(ua / wa))
                ratio = _jacobian_ratio(tri_w, tri_uv)
                if ratio is not None:
                    result.jacobian_max = max(result.jacobian_max, ratio)

    if result.world_area <= 0.0:
        if result.uvless_meshes:
            # 无 UV 时世界面积必然也是 0 —— 只报一次根因（"没有 UV"），不再补一条同源的
            # "世界面积非正"，免得报告里同一件事报两遍、把人往"网格退化"的错误方向带。
            pass
        else:
            result.problems.append("%s：世界面积非正（退化网格？）" % result.name)
        return result

    result.density = texel_size_px * math.sqrt(result.uv_area / result.world_area)
    lo = target * (1.0 - tolerance)
    hi = target * (1.0 + tolerance)
    result.density_ok = lo <= result.density <= hi
    if not result.density_ok:
        result.problems.append(
            "%s：聚合纹素密度 %.3f px/米 不在 %.3f~%.3f（目标 %.1f ±%.1f%%）"
            % (result.name, result.density, lo, hi, target, tolerance * 100.0))
    result.jacobian_ok = result.jacobian_max < jacobian_limit
    if not result.jacobian_ok:
        result.problems.append(
            "%s：Jacobian 长短轴比 %.4f ≥ %.4f（texel 非方形；点采样下比密度误差更显眼）"
            % (result.name, result.jacobian_max, jacobian_limit))

    if face_densities:
        for d in face_densities:
            if d < lo or d > hi:
                result.face_density_outliers += 1
    return result


def assert_texel_density(objects, texel_size_px, target=TARGET_PX_PER_METER,
                         tolerance=DENSITY_TOLERANCE, jacobian_limit=JACOBIAN_LIMIT,
                         name=None):
    """check 的硬版本：有任何 problem 就抛（"不达标要报错而不是警告"）。"""
    result = check_texel_density(objects, texel_size_px, target, tolerance, jacobian_limit, name)
    if result.problems:
        raise AssertionError("纹素密度校验未过（%s）：\n  - %s"
                             % (result.name, "\n  - ".join(result.problems)))
    return result


def _triangle_area_world(tri):
    a = tri[1] - tri[0]
    b = tri[2] - tri[0]
    return 0.5 * a.cross(b).length


def _triangle_area_uv(tri):
    return 0.5 * ((tri[1][0] - tri[0][0]) * (tri[2][1] - tri[0][1])
                  - (tri[2][0] - tri[0][0]) * (tri[1][1] - tri[0][1]))


def _jacobian_ratio(tri_w, tri_uv):
    """世界→UV 的 2×2 线性映射的奇异值比 σmax/σmin（None = 退化三角形）。"""
    e1 = tri_w[1] - tri_w[0]
    e2 = tri_w[2] - tri_w[0]
    l1 = e1.length
    if l1 < 1e-9:
        return None
    ax = e1 / l1
    # 世界三角形所在平面的正交基 (ax, ay)
    perp = e2 - ax * e2.dot(ax)
    l2 = perp.length
    if l2 < 1e-9:
        return None
    ay = perp / l2
    # 局部 2D 坐标：p0=(0,0), p1=(l1,0), p2=(e2·ax, l2)
    m11, m21 = l1, 0.0
    m12 = e2.dot(ax)
    m22 = l2
    det = m11 * m22 - m12 * m21
    if abs(det) < 1e-12:
        return None
    # W = [[m11, m12], [m21, m22]] 把局部 2D 映到世界；U 把局部 2D 映到 UV
    u1 = (tri_uv[1][0] - tri_uv[0][0], tri_uv[1][1] - tri_uv[0][1])
    u2 = (tri_uv[2][0] - tri_uv[0][0], tri_uv[2][1] - tri_uv[0][1])
    # J = U · W⁻¹，其中 U 的列 = u1,u2 ：局部的 (l1,0) → u1，局部的 (m12,l2) → u2
    inv = (1.0 / det)
    w_inv = ((m22 * inv, -m12 * inv), (-m21 * inv, m11 * inv))
    # U 的列向量已由 (l1,0)→u1、(m12,l2)→u2 给出，等价于 U = [u1/(l1) 组合]；直接解 U：
    # U · W = [u1 | u2]  ⇒  U = [u1|u2] · W⁻¹
    j00 = u1[0] * w_inv[0][0] + u2[0] * w_inv[1][0]
    j01 = u1[0] * w_inv[0][1] + u2[0] * w_inv[1][1]
    j10 = u1[1] * w_inv[0][0] + u2[1] * w_inv[1][0]
    j11 = u1[1] * w_inv[0][1] + u2[1] * w_inv[1][1]
    # 2×2 的奇异值：由 JᵀJ 的特征值开方
    a = j00 * j00 + j10 * j10
    b = j00 * j01 + j10 * j11
    c = j01 * j01 + j11 * j11
    tr = a + c
    dt = a * c - b * b
    disc = max(tr * tr / 4.0 - dt, 0.0)
    root = math.sqrt(disc)
    s_hi = math.sqrt(max(tr / 2.0 + root, 1e-16))
    s_lo = math.sqrt(max(tr / 2.0 - root, 1e-16))
    if s_lo < 1e-16:
        return None
    return s_hi / s_lo


# ---------------------------------------------------------------------------
# FBX 导出（沿用 scene/style_tokens.py 的铁律 + 顶点色/平滑法线的版本自适应）
# ---------------------------------------------------------------------------

def export_fbx(objects, fbx_path, use_selection=True, colors_type="SRGB",
               smooth_type="SMOOTH_GROUP"):
    """导出 FBX：单位/轴向沿用既有铁律，平滑法线与顶点色按本版 Blender 的能力传递。

    铁律（tools/blender/scene/README.md 与 style_tokens.export_fbx 同款，勿改）：
      · `apply_scale_options='FBX_SCALE_NONE'`：不在导出侧乘单位因子（useFileScale 会把 cm×0.01 乘进来）；
      · `axis_forward='-Z', axis_up='Y'`：Blender Z-up → FBX Y-up；
      · `path_mode='COPY'`、`bake_anim=False`。

    两项**版本/语义自适应**（Blender 5.2.2 实测，勿凭文档直觉改）：

      · `colors_type`：枚举只有 NONE/SRGB/LINEAR。**默认 SRGB —— 它是数据通道的恒等变换**。
        实测（写入 R=0.5/G=0.25/B=0.5/A=0.75 后导出再导入）：
          `SRGB`   → (0.5029, 0.2502, 0.5029, 0.749)  ← 与写入值一致（8bit 舍入），可用
          `LINEAR` → (0.2159, 0.0513, 0.2159, 0.749)  ← 被当作 sRGB 数值线性化，**数据被污染**
          `NONE`   → 顶点色整个丢失
        本管线的顶点色 GBA 承载平滑法线（数据，不是颜色），任何色彩空间转换都是错的；
        所以必须选 `SRGB`。名字听起来像"要加 gamma"，实为"文件里的数值就是作者数值"。

      · `mesh_smooth_type`：本版枚举只有 OFF/FACE/EDGE/SMOOTH_GROUP ——
        **没有旧文档（资产篇 §6 / 调研-反向壳 §6）写的 "Normals Only"**（那是 Blender 2.8x 的选项名）。
        自定分裂法线在本版经 SMOOTH_GROUP（锐边标记）传递，故默认用它；
        传 smooth_type=None 则不传该参数，完全交给 Blender 默认值。

    只传目标 Blender 真的有的参数（`get_rna_type().properties` 探测），
    避免跨版本升级时静默 TypeError。
    """
    import bpy

    os.makedirs(os.path.dirname(os.path.abspath(fbx_path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0] if objects else None

    available = {p.identifier for p in bpy.ops.export_scene.fbx.get_rna_type().properties}
    kwargs = {
        "filepath": str(fbx_path),
        "use_selection": use_selection,
        "apply_scale_options": "FBX_SCALE_NONE",
        "axis_forward": "-Z",
        "axis_up": "Y",
        "use_mesh_modifiers": True,
        "add_leaf_bones": False,
        "bake_anim": False,
        "path_mode": "COPY",
    }
    if smooth_type and "mesh_smooth_type" in available:
        kwargs["mesh_smooth_type"] = smooth_type
    if colors_type and "colors_type" in available:
        kwargs["colors_type"] = colors_type

    bpy.ops.export_scene.fbx(**kwargs)
    return fbx_path


def import_fbx(fbx_path):
    """导入 FBX 并返回新建的网格物体列表。"""
    import bpy
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    return [o for o in bpy.data.objects if o not in before and o.type == "MESH"]


# ---------------------------------------------------------------------------
# ④ UV 展开 + 密度锁定（存量资产的补齐步骤）
# ---------------------------------------------------------------------------

def unwrap_and_lock_uv_density(objects, texel_size_px, target=TARGET_PX_PER_METER,
                               angle_limit_deg=66.0, island_margin=0.02, max_passes=3):
    """给没有 UV 的网格做 Smart UV Project，并把 UV 整体缩放锁定到目标纹素密度。

    【为什么需要这一步】存量 47 件 WorldKit FBX **一个 UV 层都没有**（实测，见
    文档「Blender 导出模板使用说明」§4），密度校验对它们永远是 FAIL —— 不是"密度不对"，
    而是"没有 UV 可言"。本函数补上缺失的前置工序，让 kit 资产走完全链。

    【为什么用整体缩放锁密度】密度 ∝ 1/UV 尺度：把 UV 绕其包围盒中心整体乘 k，uv_area 乘 k²，
    于是 density 乘 k。一次线性缩放即可精确命中目标（k = target/current），且**不改变
    Jacobian 长短轴比**（等比缩放不改变奇异值之比）—— 密度与 texel 方正性因此互不牵制。

    【确定性】Smart UV Project 是纯几何算法（无随机数），同输入同输出；
    本函数不做任何迭代寻优（缩放是解析解，max_passes 只用于吸收浮点残差）。

    返回 {obj.name: {"k": k, "before": density_before, "after": density_after}}。
    """
    import bpy

    report = {}
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        if not mesh.uv_layers:
            _smart_project(obj, angle_limit_deg, island_margin)

    # 聚合算一次 k，所有网格用同一个 k（保住"全项目一个密度"的纪律）
    before = check_texel_density(objects, texel_size_px=texel_size_px, target=target,
                                 name="unwrap-lock")
    if before.uvless_meshes or before.density <= 0.0:
        raise RuntimeError("无法锁定密度：仍有网格没有 UV 或世界面积非正（uvless=%d density=%.4f）"
                           % (before.uvless_meshes, before.density))

    k = target / before.density
    after = before.density
    for _ in range(max(1, max_passes)):
        if abs(after - target) <= target * 1e-9:
            break
        for obj in objects:
            if obj.type != "MESH" or not obj.data.uv_layers:
                continue
            uv = obj.data.uv_layers.active
            us = [d.uv[0] for d in uv.data]
            vs = [d.uv[1] for d in uv.data]
            if not us:
                continue
            cu = (min(us) + max(us)) * 0.5
            cv = (min(vs) + max(vs)) * 0.5
            for d in uv.data:
                d.uv = (cu + (d.uv[0] - cu) * k, cv + (d.uv[1] - cv) * k)
            obj.data.update()
        # 复算（浮点残差用一次微调吸收）
        now = check_texel_density(objects, texel_size_px=texel_size_px, target=target,
                                  name="unwrap-lock")
        if now.density <= 0.0:
            break
        k = target / now.density
        after = now.density

    final = check_texel_density(objects, texel_size_px=texel_size_px, target=target,
                               name="unwrap-lock")
    for obj in objects:
        report[obj.name] = {"scale": round(k, 8), "before": round(before.density, 4),
                            "after": round(final.density, 4)}
    return report


def _smart_project(obj, angle_limit_deg, island_margin):
    import bpy
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    kwargs = {}
    props = {p.identifier for p in bpy.ops.uv.smart_project.get_rna_type().properties}
    if "angle_limit" in props:
        kwargs["angle_limit"] = math.radians(angle_limit_deg)
    if "island_margin" in props:
        kwargs["island_margin"] = island_margin
    bpy.ops.uv.smart_project(**kwargs)
    bpy.ops.object.mode_set(mode="OBJECT")
    if not obj.data.uv_layers:
        raise RuntimeError("Smart UV Project 之后 %s 仍然没有 UV 层" % obj.name)


# ---------------------------------------------------------------------------
# 汇总报告
# ---------------------------------------------------------------------------

def format_density_report(results, texel_size_px, target=TARGET_PX_PER_METER):
    """把密度结果排成人读表格（CLI 与文档示例共用同一份格式）。"""
    lines = ["%-34s %6s %8s %12s %10s %9s  %s"
             % ("asset", "tris", "uvless", "density", "jac_max", "outliers", "verdict")]
    bad = 0
    for r in results:
        verdict = "ok" if not r.problems else "FAIL(%d)" % len(r.problems)
        if r.problems:
            bad += 1
        lines.append("%-34s %6d %8d %12.3f %10.5f %9d  %s"
                     % (r.name, r.triangles, r.uvless_meshes, r.density, r.jacobian_max,
                        r.face_density_outliers, verdict))
    lines.append("目标 %.1f px/米（纹素 %dpx）· ±%.1f%% · Jacobian < %.2f · 不合规 %d/%d"
                 % (target, texel_size_px, DENSITY_TOLERANCE * 100.0, JACOBIAN_LIMIT, bad, len(results)))
    return "\n".join(lines), bad
