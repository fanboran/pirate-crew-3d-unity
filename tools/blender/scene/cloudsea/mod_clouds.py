# -*- coding: utf-8 -*-
"""mod_clouds —— 「云端漫步」7 座可玩云台 + 雨水洼 + 云瀑 + 边缘云絮。

【主题红线】只有云与自然物（云台/云瓣/云絮/云瀑/雨水洼），没有任何人造物。

【口径】
    · 布局真源 = cloudsea_common.PLATFORMS（7 座：中心/顶高/半宽）。台面外廓与
      half_x/half_y 对齐；顶面 z **精确等于** top_z（中央 80% 绝对平，到台缘只缓降
      几厘米，可站立）；任一台外廓内，除雨水洼（≤ +0.02 m）外不许有顶点高出台面
      （build() 末尾逐台自检，打印 TOPZ / TOPCLEAR）。
    · 云台 = 倒圆顶云腹胚（台缘外鼓成鼓瓣带，往下收成腹底）+ 台缘云瓣 6-10 个
      + 腹壁云瓣 + 台缘外分离云絮 2-4 个 + 腹底下垂云絮（主絮 3 团越往下越小、
      副絮 0-2 团，顺风偏移，最低离水面约半米）。
    · 瓣/絮一律在台面以下，且不挤进邻台云体、不悬到邻台顶面上方（_clear 避让）。
    · 材质只用 Cloud_* 槽（零贴图零碰撞），按「相对深度 + 面朝向」分层涂：
      站立面 Cloud_White；台缘/云瓣朝上受光面 Cloud_White、东南向阳面 Cloud_Warm、
      背光面 Cloud_Shade；台缘外分离云絮上半白/向阳微暖、下半阴；腹中 Cloud_Shade →
      Cloud_Deep → 腹底 Cloud_Blue；下垂云絮根部 Cloud_Blue、往下 Cloud_Deep、末端化开成
      Cloud_Shade；朝下的面再压暗一档，整块不是纯白。
    · 雨水洼：主台 ×2 + 东台/北台各 ×1，Cloud_Water 薄透镜（顶 +0.02 m）+ Cloud_Shade 湿圈。
    · 云瀑：East 台缘在泻口方位留出云瓣缺口，云絮从唇口外翻，顺 Catmull-Rom 曲线落到
      cloudsea_common.WATERFALL 的海面落点，一路渐细渐散，落点一圈 Cloud_Mist 水雾。
    · 静态站面件：7 台合成一个对象 Cloud_Platforms（不摇晃）；云瀑单独 Cloud_Waterfall。
      总三角面 ≤ 24k，近景云瓣/云腹优先。
"""

import math
import os
import sys

import bpy

_HERE = os.path.dirname(os.path.abspath(__file__))
_SCENE_DIR = os.path.dirname(_HERE)
for _p in (_SCENE_DIR, os.path.join(_SCENE_DIR, "chemplant")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import kit_common as K          # noqa: E402  Mesher 图元 + 材质/收工
import cloudsea_common as H     # noqa: E402  PLATFORMS / WATERFALL 布局真源

TAU = math.pi * 2.0

#: 主光水平投影（太阳在东南上方）：向阳面涂 Cloud_Warm。
#: 【2026-11-20 翻号】本地 y 现在直通 Unity z，风向/光向量跟着翻号。
SUN_XY = (0.61, 0.79)

#: 全场风向（水平单位向量）：云腹后倾、下垂云絮斜垂都顺着它，整场读作同一阵风
WIND_XY = (0.92, -0.38)

#: 合并对象统一槽序（只收实际用到的槽）
SLOT_ORDER = ("Cloud_White", "Cloud_Warm", "Cloud_Shade", "Cloud_Deep",
              "Cloud_Blue", "Cloud_Mist", "Cloud_Sea", "Cloud_SeaDeep", "Cloud_Water")

#: 三角面预算（自检口径）：台缘上沿蓬松瓣与饱满云腹的合法开销
TRI_BUDGET = 25000

# ---------------------------------------------------------------------------
# 各台建型参数（近景台加密）：胚体分段 / 台缘云瓣 / 腹壁云瓣 / 分离云絮 / 下垂云絮
# ---------------------------------------------------------------------------

_SEGS = {"Hero": 36, "North": 26, "West": 26, "East": 26, "South": 26,
         "NW_Islet": 22, "SE_Islet": 22}
_RIM = {"Hero": 10, "North": 9, "West": 9, "East": 9, "South": 9,
        "NW_Islet": 7, "SE_Islet": 7}
_BELLY = {"Hero": 7, "North": 5, "West": 4, "East": 5, "South": 5,
          "NW_Islet": 3, "SE_Islet": 3}
_PUFS = {"Hero": 4, "North": 3, "West": 3, "East": 3, "South": 3,
         "NW_Islet": 2, "SE_Islet": 2}
#: 腹底下垂云絮（主絮团数, 副絮团数）
_WISPS = {"Hero": (3, 2), "North": (3, 1), "West": (3, 1), "East": (3, 2),
          "South": (3, 1), "NW_Islet": (3, 0), "SE_Islet": (3, 0)}

#: 雨水洼（平台名, 偏移 ox, oy, 半长 rx, 半短 ry）——都在中央平区内
_PUDDLES = [
    ("Hero", -2.3, -1.5, 1.55, 1.05),
    ("Hero", 1.9, 1.9, 1.20, 0.85),
    ("East", 0.5, 0.4, 1.00, 0.75),
    ("North", -0.6, 0.8, 0.95, 0.70),
]

#: 云瀑泻口在 East 台缘留出的云瓣缺口半角（该方位不放瓣/絮，云瀑才看得见）
_SPILL_GAP = math.radians(30.0)

# ---------------------------------------------------------------------------
# 云台胚剖面（径向比例, 相对深度 dz, 谐波鼓瓣幅度）
#   顶心 → 平带（0.80 内绝对平）→ 缓圆 → 台缘（≈hx/hy）→ 鼓瓣带 → 倒圆顶收到腹底
# ---------------------------------------------------------------------------

_PROFILE = [
    (0.00,  0.000, 0.00),   # 顶心
    (0.45,  0.000, 0.00),
    (0.92,  0.000, 0.00),   # 平带覆盖逻辑可站轮廓（16 边形半径 = half × 0.92）
    (0.96, -0.006, 0.10),   # 缓圆起点（几厘米）
    (1.03, -0.035, 0.35),   # 台缘（≈half_x/half_y）
    (1.09, -0.110, 1.00),   # 鼓瓣带（顶缘外扩）
    (1.06, -0.200, 1.00),
    (1.03, -0.300, 0.95),
    (0.96, -0.410, 0.90),
    (0.86, -0.530, 0.85),
    (0.72, -0.650, 0.80),
    (0.56, -0.770, 0.75),
    (0.38, -0.875, 0.65),
    (0.18, -0.955, 0.50),
    (0.00, -1.000, 0.00),   # 腹底心
]


def _h01(seed, idx, salt):
    """uint32 混淆哈希 → [0,1)（确定性，无环境依赖；思路同旧云场生成器）。"""
    h = (seed & 0xFFFFFFFF) * 747796405 \
        + (idx & 0xFFFFFFFF) * 2891336453 \
        + (salt & 0xFFFFFFFF) * 1911520717 + 1442695041
    h &= 0xFFFFFFFF
    h ^= h >> 15
    h = (h * 2246822519) & 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 3266489917) & 0xFFFFFFFF
    h ^= h >> 16
    return h * (1.0 / 4294967296.0)


def _sgn(seed, idx, salt):
    return _h01(seed, idx, salt) * 2.0 - 1.0


def _ang_dist(a, b):
    d = (a - b) % TAU
    return min(d, TAU - d)


def _profile_rad(frac):
    """按相对深度 frac（0=台顶 →1=腹底）线性插值剖面半径（跳过零跨度的平带段）。"""
    f = max(0.0, min(frac, 1.0))
    n = len(_PROFILE)
    for i in range(n - 1):
        f0, f1 = -_PROFILE[i][1], -_PROFILE[i + 1][1]
        if f1 - f0 <= 1e-9:
            continue
        if f <= f1 or i == n - 2:
            t = min(1.0, max(0.0, (f - f0) / (f1 - f0)))
            return _PROFILE[i][0] + (_PROFILE[i + 1][0] - _PROFILE[i][0]) * t
    return 0.0


def _depth(top, hx, hy):
    """云腹深度：随台面尺寸与高度加深；腹底离水面 ≥ 1.2 m（z ≥ 0.8）。"""
    return min(0.38 * (hx + hy) + 0.10 * (top - 2.5), top - 0.8)


# ---------------------------------------------------------------------------
# 几何图元（全部走 Mesher.add；占位槽 Cloud_White，收工后按所属台分层重涂）
# ---------------------------------------------------------------------------

def _unit_sphere(seg, rings):
    """单位 UV 球（Z 极轴，rings=纬度带数）→ (verts, faces)，绕向一律朝外。"""
    seg = max(6, seg)
    rings = max(3, rings)
    verts = [(0.0, 0.0, 1.0)]
    for ri in range(1, rings):
        lat = math.pi * ri / rings
        zz, rr = math.cos(lat), math.sin(lat)
        for k in range(seg):
            a = TAU * k / seg
            verts.append((rr * math.cos(a), rr * math.sin(a), zz))
    bot = len(verts)
    verts.append((0.0, 0.0, -1.0))
    faces = []
    for k in range(seg):                                    # 顶极点扇
        faces.append((0, 1 + k, 1 + (k + 1) % seg))
    for ri in range(rings - 2):                             # 纬带四边环
        up0, lo0 = 1 + ri * seg, 1 + (ri + 1) * seg
        for k in range(seg):
            j = (k + 1) % seg
            faces.append((lo0 + k, lo0 + j, up0 + j, up0 + k))
    last = 1 + (rings - 2) * seg
    for k in range(seg):                                    # 底极点扇
        faces.append((bot, last + (k + 1) % seg, last + k))
    return verts, faces


def _puff(m, center, rx, ry, rz, seg, rings, mat="Cloud_White", rot=0.0):
    """云球/云瓣：单位球经非均匀缩放成椭球；rot = 绕 Z 旋转（让云瓣沿台缘切向拉长）。"""
    verts, faces = _unit_sphere(seg, rings)
    m.add(verts, faces, at=center, rz=rot, scale=(rx, ry, rz), mat=mat, smooth=True)


def _mound(m, seed, cx, cy, top, hx, hy, depth, seg):
    """一座云台的倒圆顶胚（单封闭网格）。返回实测最高点 z（应精确 == top）。

    顶面三圈绝对平（无抖动/无鼓瓣）；鼓瓣谐波与径向抖动只作用在台缘以下；
    腹部顺风向轻微后倾。
    """
    a1 = 0.050 + 0.025 * _h01(seed, 1, 21)
    a2 = 0.028 + 0.014 * _h01(seed, 2, 22)
    n1 = 4 + int(_h01(seed, 3, 23) * 2.99)
    n2 = 8 + int(_h01(seed, 4, 24) * 2.99)
    p1 = _h01(seed, 5, 25) * TAU
    p2 = _h01(seed, 6, 26) * TAU
    lean = 0.15 + 0.25 * _h01(seed, 7, 27)
    la = math.atan2(WIND_XY[1], WIND_XY[0]) + 0.5 * _sgn(seed, 8, 28)
    lx, ly = lean * math.cos(la), lean * math.sin(la)

    verts, rings = [], []
    for ri, (rad, dz, amp) in enumerate(_PROFILE):
        fn = -dz                                            # 相对深度 0..1
        if rad <= 1e-6:                                     # 极点（顶心/腹底）
            rings.append([len(verts)])
            verts.append((cx + lx * fn * fn, cy + ly * fn * fn, top + dz * depth))
            continue
        ring = []
        for k in range(seg):
            a = TAU * k / seg
            bump = 1.0 + amp * (a1 * math.cos(n1 * a + p1) + a2 * math.cos(n2 * a + p2))
            if amp > 0.0:                                   # 平带不抖，顶面保平
                bump *= 1.0 + 0.024 * _sgn(seed, ri * 53 + k, 29)
            ring.append(len(verts))
            verts.append((cx + lx * fn * fn + math.cos(a) * hx * rad * bump,
                          cy + ly * fn * fn + math.sin(a) * hy * rad * bump,
                          top + dz * depth))
        rings.append(ring)

    faces = []
    for ri in range(len(rings) - 1):
        up, lo = rings[ri], rings[ri + 1]
        if len(up) == 1:                                    # 上极点扇（+Z）
            for k in range(seg):
                faces.append((up[0], lo[k], lo[(k + 1) % seg]))
        elif len(lo) == 1:                                  # 下极点扇（−Z）
            for k in range(seg):
                faces.append((lo[0], up[(k + 1) % seg], up[k]))
        else:                                               # 侧壁四边环（朝外）
            for k in range(seg):
                j = (k + 1) % seg
                faces.append((lo[k], lo[j], up[j], up[k]))
    m.add(verts, faces, mat="Cloud_White", smooth=True)
    return max(v[2] for v in verts)


def _revolve(m, rows, seg, at, sxy, mat, smooth, rot=0.0, poles=(False, False)):
    """小旋转体（雨水洼湿圈/水透镜用）：rows=[(r_unit, z_m)]，poles=下/上极点 (r, z)。"""
    verts, rings, faces = [], [], []
    if poles[0]:
        rings.append([len(verts)])
        verts.append((poles[0][0], 0.0, poles[0][1]))
    for (r, z) in rows:
        start = len(verts)
        for k in range(seg):
            a = TAU * k / seg
            verts.append((r * math.cos(a), r * math.sin(a), z))
        rings.append(list(range(start, start + seg)))
    if poles[1]:
        rings.append([len(verts)])
        verts.append((poles[1][0], 0.0, poles[1][1]))
    for ri in range(len(rings) - 1):
        up, lo = rings[ri], rings[ri + 1]
        if len(up) == 1:                                    # 极点扇（按上下定绕向）
            below = verts[up[0]][2] < verts[lo[0]][2]
            for k in range(seg):
                q, w = lo[k], lo[(k + 1) % seg]
                faces.append((up[0], w, q) if below else (up[0], q, w))
        elif len(lo) == 1:
            for k in range(seg):
                faces.append((lo[0], up[(k + 1) % seg], up[k]))
        else:
            for k in range(seg):
                j = (k + 1) % seg
                faces.append((lo[k], lo[j], up[j], up[k]))
    m.add(verts, faces, at=at, rz=rot, scale=(sxy[0], sxy[1], 1.0),
          mat=mat, smooth=smooth)


def _puddle(m, seed, px, py, top, rx, ry):
    """雨水洼：Cloud_Mist 湿圈（浅色薄环带，不做深蓝眼圈）+ Cloud_Water 薄透镜（顶 +0.02 m）。"""
    seg = 12
    rot = 0.7 * _sgn(seed, 1, 43)
    _revolve(m, [(0.98, 0.003), (1.16, 0.005), (1.16, 0.009), (0.98, 0.011)],
             seg, (px, py, top), (rx, ry), "Cloud_Mist", smooth=False, rot=rot)
    _revolve(m, [(0.50, 0.006), (0.80, 0.009), (0.95, 0.014)],
             seg, (px, py, top), (rx, ry), "Cloud_Water", smooth=False, rot=rot,
             poles=((0.0, 0.004), (0.0, 0.020)))


def _catmull(pts, u):
    """Catmull-Rom 样条（端点外推），u ∈ [0, len(pts)-1]。"""
    n = len(pts)
    u = max(0.0, min(u, n - 1 - 1e-9))
    s = int(u)
    t = u - s
    p1, p2 = pts[s], pts[s + 1]
    p0 = pts[s - 1] if s >= 1 else tuple(2 * a - b for a, b in zip(pts[0], pts[1]))
    p3 = pts[s + 2] if s + 2 < n else tuple(2 * a - b for a, b in zip(pts[-1], pts[-2]))
    t2, t3 = t * t, t * t * t
    return tuple(0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2
                        + (-a + 3 * b - 3 * c + d) * t3)
                 for a, b, c, d in zip(p0, p1, p2, p3))


# ---------------------------------------------------------------------------
# 摆放：瓣 / 絮（逐槽位均匀分布，遇邻台让位）
# ---------------------------------------------------------------------------

def _body_k(o, z):
    """邻台 o 在高度 z 处的外廓径向比例（含鼓瓣）：高于台面按台面外廓，低于腹底 None。"""
    top, depth = o[3], o[6]
    if z >= top:
        return 1.10
    frac = (top - z) / depth
    if frac > 1.0:
        return None
    return _profile_rad(frac) * 1.10


def _clear(self_name, px, py, z_lo, z_hi, r, plats, pad=0.30):
    """瓣/絮净空：不挤进邻台云体（含其云瓣外伸），更不悬到邻台顶面上方挡视线。"""
    for o in plats:
        if o[0] == self_name:
            continue
        _nm, cx, cy, top, hx, hy, _d = o
        for z in (z_lo, 0.5 * (z_lo + z_hi), z_hi):
            k = _body_k(o, z)
            if k is None:
                continue
            ext = (0.0 if z >= top else 0.9) + r + pad
            ex, ey = hx * k + ext, hy * k + ext
            if ((px - cx) / ex) ** 2 + ((py - cy) / ey) ** 2 < 1.0:
                return False
    return True


def _rim_lobes(m, seed, ent, plats, n_target, gap_az=None):
    """台缘云瓣：骑在鼓瓣带上的肥椭球（瓣顶 ≤ 台面 −0.08），环向均匀分槽。"""
    nm, cx, cy, top, hx, hy, _depth_ = ent
    base_r = min(max(min(hx, hy) * 0.30, 0.42), 1.05)
    placed = 0
    for j in range(n_target):
        h = _h01(seed, j, 61)
        a_nom = TAU * (j + 0.30 * _h01(seed, j, 62)) / n_target
        for off, shrink in ((0.0, 1.0), (0.30, 1.0), (-0.30, 1.0), (0.0, 0.62)):
            a = a_nom + off * TAU / n_target
            if gap_az is not None and _ang_dist(a, gap_az) < _SPILL_GAP:
                continue
            r = base_r * (0.85 + 0.30 * h) * shrink
            rz = r * 0.72
            cz = top - rz - 0.08 - 0.12 * _h01(seed, j, 64) - (1.0 - shrink) * 0.6
            k = 1.06 + 0.34 * r / min(hx, hy)
            px = cx + math.cos(a) * hx * k
            py = cy + math.sin(a) * hy * k
            if not _clear(nm, px, py, cz - rz, cz + rz, r, plats):
                continue
            # 切向拉长：rot=a 后局部 y 沿台缘环向，云瓣读作被风吹开的层片而不是圆球。
            _puff(m, (px, py, cz), r * 0.82, r * 1.16, rz, 12, 6, rot=a)
            placed += 1
            break
    return placed


def _top_rim_lobes(m, seed, ent, plats, gap_az=None):
    """台缘上沿的蓬松云瓣：在台缘外侧立起一圈小云团，打破“平白盘 + 球串”的剪影。

    瓣心压到 1.30× 台缘以外，内缘恒在 1.05× 可站立外廓之外——不挡站面、不触发
    越顶自检；瓣顶可略高于台面，云台边缘因此有起伏的白边。
    """
    nm, cx, cy, top, hx, hy, _depth_ = ent
    base_r = min(max(min(hx, hy) * 0.20, 0.30), 0.55)
    placed = 0
    for j in range(7):
        h = _h01(seed, j, 78)
        a = TAU * (j + 0.4 * _h01(seed, j, 79)) / 7.0
        if gap_az is not None and _ang_dist(a, gap_az) < _SPILL_GAP:
            continue
        r = base_r * (0.75 + 0.45 * h)
        rz = r * (0.62 + 0.16 * h)
        k = 1.30 + 0.10 * _h01(seed, j, 80)
        px = cx + math.cos(a) * hx * k
        py = cy + math.sin(a) * hy * k
        cz = top - 0.03 + rz * 0.5
        if not _clear(nm, px, py, cz - rz, cz + rz, r, plats):
            continue
        _puff(m, (px, py, cz), r * 0.78, r * 1.18, rz, 10, 5, rot=a)
        placed += 1
    return placed


def _belly_lobes(m, seed, ent, plats, n_target, gap_az=None):
    """腹壁云瓣：半嵌在云腹侧壁的圆鼓，让云腹读作层叠云团而不是光滑锥面。"""
    nm, cx, cy, top, hx, hy, depth = ent
    base_r = min(max(min(hx, hy) * 0.24, 0.40), 0.95)
    placed = 0
    for j in range(n_target):
        h = _h01(seed, j, 51)
        a_nom = TAU * (j + 0.5 + 0.35 * _h01(seed, j, 52)) / n_target
        r = base_r * (0.80 + 0.40 * h)
        rz = r * 0.80
        zc = min(top - (0.36 + 0.30 * _h01(seed, j, 53)) * depth, top - 0.15 - rz)
        body = _profile_rad((top - zc) / depth)
        for off in (0.0, 0.30, -0.30):
            a = a_nom + off * TAU / n_target
            if gap_az is not None and _ang_dist(a, gap_az) < _SPILL_GAP:
                continue
            px = cx + math.cos(a) * (hx * body + 0.22 * r)
            py = cy + math.sin(a) * (hy * body + 0.22 * r)
            if not _clear(nm, px, py, zc - rz, zc + rz, r, plats):
                continue
            _puff(m, (px, py, zc), r, r * (0.85 + 0.25 * _h01(seed, j, 54)), rz, 10, 5)
            placed += 1
            break
    return placed


def _puffs(m, seed, ent, plats, n_target, gap_az=None):
    """台缘外分离云絮：塞在台缘云瓣缝外侧、略低一层的独立小云球（絮顶 ≤ 台面 −0.40）。"""
    nm, cx, cy, top, hx, hy, _depth_ = ent
    base_r = min(max(min(hx, hy) * 0.20, 0.30), 0.50)
    placed = 0
    for j in range(n_target):
        h = _h01(seed, j, 71)
        a_nom = TAU * (j + 0.25 + 0.50 * _h01(seed, j, 72)) / n_target
        r = base_r * (0.80 + 0.40 * h)
        rz = r * 0.78
        cz = top - 0.40 - rz - 0.30 * _h01(seed, j, 73)
        d_out = 0.45 + 0.60 * r + 0.30 * _h01(seed, j, 75)
        for off in (0.0, 0.35, -0.35):
            a = a_nom + off * TAU / n_target
            if gap_az is not None and _ang_dist(a, gap_az) < _SPILL_GAP:
                continue
            px = cx + math.cos(a) * (hx * 1.09 + d_out)
            py = cy + math.sin(a) * (hy * 1.09 + d_out)
            if not _clear(nm, px, py, cz - rz, cz + rz, r, plats):
                continue
            _puff(m, (px, py, cz), r, r * (0.85 + 0.25 * _h01(seed, j, 74)), rz, 10, 5)
            placed += 1
            break
    return placed


def _tail(m, seed, ent, a, k_n, r0, z0, floor, rad_frac):
    """一条下垂云絮尾：k_n 团横胖云团深叠压、越往下越小、顺风偏移；末端不低于 floor。

    返回 (面起, 面止, 出腹 z, 末端 z)，供「沿絮长度」分层重涂。
    """
    _nm, cx, cy, top, hx, hy, depth = ent
    x0 = cx + math.cos(a) * hx * rad_frac
    y0 = cy + math.sin(a) * hy * rad_frac
    radii = [r0 * (1.0, 0.72, 0.50, 0.36)[k] for k in range(k_n)]
    rzs = [r * 0.85 for r in radii]
    zs = [z0]
    for k in range(1, k_n):                                 # 相邻两团叠压 45%
        zs.append(zs[-1] - 0.55 * (rzs[k - 1] + rzs[k]))
    if zs[-1] - rzs[-1] < floor:                            # 空间不够就压缩间距
        full = z0 - zs[-1]
        scale = max(0.15, (z0 - floor - rzs[-1]) / full) if full > 1e-6 else 1.0
        zs = [z0 - (z0 - z) * scale for z in zs]
    f0 = len(m.faces)
    z_lo = z0
    for k in range(k_n):
        drift = 0.28 * k * k * r0                           # 越往下越顺风偏
        px = x0 + WIND_XY[0] * drift + 0.12 * r0 * k * _sgn(seed, k, 84)
        py = y0 + WIND_XY[1] * drift + 0.12 * r0 * k * _sgn(seed, k, 85)
        _puff(m, (px, py, zs[k]), radii[k], radii[k] * (0.85 + 0.25 * _h01(seed, k, 86)),
              rzs[k], 10, 5)
        z_lo = zs[k] - rzs[k]
    return (f0, len(m.faces), top - 0.97 * depth, z_lo)


def _wisps(m, seed, ent, plan):
    """腹底下垂云絮：主絮 3 团（朝镜头侧，云腹往下收成越来越小的团块）+ 副絮 0-2 团。"""
    k_main, k_side = plan
    _nm, cx, cy, top, hx, hy, depth = ent
    floor = max(0.10, top - depth - 2.6)                    # 最低离水面约半米
    r0 = min(max(min(hx, hy) * 0.26, 0.45), 1.20)
    a_cam = math.atan2(SUN_XY[1], SUN_XY[0])                # 东南（默认镜头侧）
    spans = [_tail(m, seed, ent, a_cam + 0.4 * _sgn(seed, 0, 87), k_main, r0,
                   top - 0.92 * depth, floor, 0.12)]
    if k_side:
        a2 = a_cam + math.pi * (0.75 + 0.5 * _h01(seed, 1, 88))
        spans.append(_tail(m, seed + 5, ent, a2, k_side, r0 * 0.68,
                           top - 0.90 * depth, floor, 0.26))
    return spans


def _waterfall(m, ent):
    """云瀑：East 台缘泻口（云瓣缺口）外翻 → 顺曲线落到 WATERFALL 海面落点，渐细渐散。"""
    _nm, ecx, ecy, etop, ehx, ehy, _d = ent
    ex, ey, ez = H.WATERFALL[1]
    a0 = math.atan2(ey - ecy, ex - ecx)                     # 泻口方位：朝落点
    ca, sa = math.cos(a0), math.sin(a0)

    def rim(k, z):
        return (ecx + ca * ehx * k, ecy + sa * ehy * k, z)

    c2 = rim(1.24, etop - 1.50)
    ctrl = [rim(1.04, etop - 0.30),                         # 唇口（压在台缘上）
            rim(1.17, etop - 0.62),                         # 越过鼓瓣带外翻
            c2,
            (0.5 * (c2[0] + ex) + 0.15 * ca, 0.5 * (c2[1] + ey) + 0.15 * sa, 2.6),
            (ex, ey, ez + 0.12)]                            # 海面落点
    n = 30
    for i in range(n):
        t = (i / (n - 1.0)) ** 1.15                         # 上段加密（唇口更连贯）
        bx, by, bz = _catmull(ctrl, t * (len(ctrl) - 1))
        r = 0.50 - 0.22 * t
        rz = r * (1.0 + 1.2 * min(1.0, t * 3.0))            # 越落越拉长，串成细流
        rz = min(rz, max(0.10, etop - 0.08 - bz))           # 唇口段球顶不越台面
        sc = 0.04 + 0.22 * t
        px = bx + _sgn(990011, i, 91) * sc
        py = by + _sgn(990011, i, 92) * sc
        _puff(m, (px, py, bz), r, r * 0.9, rz, 8, 5)
        if t > 0.55 and i % 3 == 0:                         # 下段分股，越落越散
            a = TAU * _h01(990011, i, 94)
            _puff(m, (px + math.cos(a) * 0.38, py + math.sin(a) * 0.38, bz + 0.15),
                  r * 0.60, r * 0.55, rz * 0.60, 8, 4)
    for j in range(6):                                      # 飘散的细絮
        h = _h01(990011, j, 95)
        bx, by, bz = _catmull(ctrl, (0.45 + 0.45 * h) * (len(ctrl) - 1))
        a = TAU * _h01(990011, j, 96)
        d = 0.40 + 0.40 * _h01(990011, j, 97)
        r = 0.10 + 0.08 * h
        _puff(m, (bx + math.cos(a) * d, by + math.sin(a) * d, bz), r, r, r * 1.3, 6, 4)
    for j in range(9):                                      # 落点水雾一圈
        h = _h01(990011, j, 93)
        if j == 0:
            px, py, r = ex, ey, 0.85
        else:
            a = TAU * (j - 1) / 8.0 + 0.4 * h
            d = 0.85 + 0.45 * h
            px, py, r = ex + math.cos(a) * d, ey + math.sin(a) * d * 0.85, 0.36 + 0.30 * h
        _puff(m, (px, py, -0.30 + 0.12 * h), r, r * 0.9, r * 0.36, 8, 4)


# ---------------------------------------------------------------------------
# 分层重涂 / 合并 / 自检
# ---------------------------------------------------------------------------

_DARKER = {"Cloud_White": "Cloud_Shade", "Cloud_Warm": "Cloud_Shade",
           "Cloud_Shade": "Cloud_Deep", "Cloud_Deep": "Cloud_Blue",
           "Cloud_Blue": "Cloud_Blue", "Cloud_Sea": "Cloud_Shade",
           "Cloud_SeaDeep": "Cloud_Deep", "Cloud_Mist": "Cloud_Shade"}


def _band(frac, nz, nh, az):
    """相对深度 + 面朝向 → (槽, 平滑)。nz=法线 z；nh=法线·阳光水平向；az=方位·阳光。"""
    if frac < 0.008:
        return "Cloud_White", False                         # 站立面：绝对平，平直着色
    if frac < 0.06:                                         # 台缘缓圆：受光白，向阳微暖
        mat = "Cloud_Warm" if (nh > 0.30 or az > 0.55) else "Cloud_White"
    elif frac < 0.20:                                       # 鼓瓣带 / 台缘云瓣
        if nz > 0.60:
            mat = "Cloud_White"
        elif nh > 0.30:
            mat = "Cloud_Warm"
        else:
            mat = "Cloud_Shade"
    elif frac < 0.40:                                       # 腹上：冷阴面
        mat = "Cloud_Shade"
    elif frac < 0.62:                                       # 腹中：蓝灰云影
        mat = "Cloud_Deep"
    else:                                                   # 腹底 / 下垂云絮
        mat = "Cloud_Blue"
    # 【为什么不用 Cloud_Sea/SeaDeep 涂腹（2026-11-20）】这两个槽是海色（青灰），
    # 涂在云台侧裙上会和海面同色，云台边缘整个溶进海里——角色看起来像悬在水上。
    # 云腹要的是“蓝天反射的云影”（蓝紫灰），与海的青绿拉开色相。
    if nz < -0.45:                                          # 朝下的面压暗一档
        mat = _DARKER[mat]
    return mat, True


def _ensure_slots(me, names):
    for nm in names:
        if not any(mm is not None and mm.name == nm for mm in me.materials):
            me.materials.append(K.make_material(nm))
    return {mm.name: i for i, mm in enumerate(me.materials)}


def _paint_platforms(obj, groups):
    """云台件重涂。groups 两类：
        ("plat", 面起, 面止, 平台条目) —— 胚/瓣：按所属台相对深度 + 面朝向分层；
        ("puff", 面起, 面止) —— 台缘外分离云絮：受光小絮，上半白/向阳微暖、下半阴；
        ("wisp", 面起, 面止, 出腹 z, 末端 z) —— 下垂云絮：沿絮长度 Blue → Deep → Shade
        （根部接腹底冷蓝，末端化开变浅，不读作深色水滴）。
    """
    me = obj.data
    idx = _ensure_slots(me, ("Cloud_White", "Cloud_Warm", "Cloud_Shade",
                             "Cloud_Deep", "Cloud_Blue", "Cloud_Mist",
                             "Cloud_Sea", "Cloud_SeaDeep"))
    polys = me.polygons
    for g in groups:
        if g[0] == "puff":
            for fi in range(g[1], g[2]):
                poly = polys[fi]
                n = poly.normal
                nh = n.x * SUN_XY[0] + n.y * SUN_XY[1]
                if n.z > 0.15:
                    mat = "Cloud_Warm" if nh > 0.35 else "Cloud_White"
                elif n.z > -0.45:
                    mat = "Cloud_Warm" if nh > 0.55 else "Cloud_Shade"
                else:
                    mat = "Cloud_Shade"
                poly.material_index = idx[mat]
                poly.use_smooth = True
            continue
        if g[0] == "wisp":
            _k, f0, f1, z_hi, z_lo = g
            span = max(1e-6, z_hi - z_lo)
            for fi in range(f0, f1):
                poly = polys[fi]
                s = (z_hi - poly.center.z) / span
                mat = "Cloud_Blue" if s < 0.45 else ("Cloud_Deep" if s < 0.80 else "Cloud_Shade")
                poly.material_index = idx[mat]
                poly.use_smooth = True
            continue
        _k, f0, f1, ent = g
        _nm, cx, cy, top, hx, hy, depth = ent
        for fi in range(f0, f1):
            poly = polys[fi]
            c, n = poly.center, poly.normal
            dx, dy = (c.x - cx) / hx, (c.y - cy) / hy
            dl = math.hypot(dx, dy) or 1.0
            az = (dx * SUN_XY[0] + dy * SUN_XY[1]) / dl
            nh = n.x * SUN_XY[0] + n.y * SUN_XY[1]
            if (top - c.z) / depth < 0.008:
                # 台面：低frequency云斑（暖白/冷白/浅蓝），让大片平顶读作柔软云面
                # 而不是一整块死白；色块边界利落，符合像素化平涂。
                u = (c.x - cx) / max(hx, 1e-3)
                v = (c.y - cy) / max(hy, 1e-3)
                w = (math.sin(u * 3.7 + cx * 0.9) * math.cos(v * 3.1 - cy * 0.7)
                     + 0.55 * math.sin((u + v) * 5.3 + cx * 0.3))
                if w > 0.45:
                    mat = "Cloud_Warm"
                elif w < -0.45:
                    mat = "Cloud_Mist"
                else:
                    mat = "Cloud_White"
                poly.material_index = idx[mat]
                poly.use_smooth = False
                continue
            mat, sm = _band((top - c.z) / depth, n.z, nh, az)
            poly.material_index = idx[mat]
            poly.use_smooth = sm


def _paint_fall(obj):
    """云瀑重涂：受光面白、背光面阴，近水段化成水雾。"""
    me = obj.data
    idx = _ensure_slots(me, ("Cloud_White", "Cloud_Shade", "Cloud_Mist"))
    for poly in me.polygons:
        c, n = poly.center, poly.normal
        nh = n.x * SUN_XY[0] + n.y * SUN_XY[1]
        if c.z < 0.9:
            mat = "Cloud_White" if n.z > 0.55 else "Cloud_Mist"
        elif n.z < -0.45 or nh < -0.25:
            mat = "Cloud_Shade"
        else:
            mat = "Cloud_White"
        poly.material_index = idx[mat]


def _merge(obj_a, obj_b, name):
    """两网格手工合并为一个对象（面级槽名/平滑标记重映射，绕向已各自收工校直）。"""
    verts, faces, fmats, fsmooth = [], [], [], []
    for obj in (obj_a, obj_b):
        me = obj.data
        names = [mm.name if mm else "" for mm in me.materials]
        base = len(verts)
        verts.extend(tuple(v.co) for v in me.vertices)
        for poly in me.polygons:
            faces.append(tuple(base + i for i in poly.vertices))
            fmats.append(names[poly.material_index])
            fsmooth.append(poly.use_smooth)
    used = [nm for nm in SLOT_ORDER if nm in set(fmats)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    for nm in used:
        me.materials.append(K.make_material(nm))
    idx = {nm: i for i, nm in enumerate(used)}
    for poly, nm, sm in zip(me.polygons, fmats, fsmooth):
        poly.material_index = idx[nm]
        poly.use_smooth = sm
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def _check_tops(objs, plats):
    """越顶自检：任一台外廓（×1.05）内，除雨水洼（≤ +0.021 m）外不得有顶点高出台面。"""
    bad_total = 0
    for (nm, cx, cy, top, hx, hy, _d) in plats:
        ex, ey = hx * 1.05, hy * 1.05
        bad = 0
        for obj in objs:
            for v in obj.data.vertices:
                x, y, z = v.co
                if z > top + 0.021 and ((x - cx) / ex) ** 2 + ((y - cy) / ey) ** 2 <= 1.0:
                    bad += 1
        bad_total += bad
        print("TOPCLEAR %-9s 越顶顶点=%d %s" % (nm, bad, "OK" if bad == 0 else "违规!"),
              flush=True)
    return bad_total


def build():
    """建 7 座云台（+雨水洼）→ Cloud_Platforms；云瀑 → Cloud_Waterfall。"""
    plats = [(nm, cx, cy, top, hx, hy, _depth(top, hx, hy))
             for (nm, cx, cy, top, hx, hy, _role) in H.PLATFORMS]
    by_name = {p[0]: p for p in plats}
    east = by_name["East"]
    spill_az = math.atan2(H.WATERFALL[1][1] - east[2], H.WATERFALL[1][0] - east[1])

    m_base = K.Mesher()                                     # 胚 + 瓣 + 絮（收工后分层重涂）
    m_pud = K.Mesher()                                      # 雨水洼（自带 Water/Shade 槽）
    groups, topz = [], []
    for i, ent in enumerate(plats):
        nm, cx, cy, top, hx, hy, depth = ent
        seed = 770123 + i * 97
        gap = spill_az if nm == "East" else None
        f0 = len(m_base.faces)
        zmax = _mound(m_base, seed, cx, cy, top, hx, hy, depth, _SEGS[nm])
        n_rim = _rim_lobes(m_base, seed + 1, ent, plats, _RIM[nm], gap)
        f_top = len(m_base.faces)
        n_top = _top_rim_lobes(m_base, seed + 5, ent, plats, gap)
        groups.append(("puff", f_top, len(m_base.faces)))
        n_bel = _belly_lobes(m_base, seed + 2, ent, plats, _BELLY[nm], gap)
        groups.append(("plat", f0, len(m_base.faces), ent))
        f_puf = len(m_base.faces)
        n_puf = _puffs(m_base, seed + 3, ent, plats, _PUFS[nm], gap)
        groups.append(("puff", f_puf, len(m_base.faces)))
        for span in _wisps(m_base, seed + 4, ent, _WISPS[nm]):
            groups.append(("wisp",) + span)
        topz.append((nm, top, zmax))
        print("PIECE %-9s depth=%.2f rim=%d belly=%d puffs=%d tails=%d+%d"
              % ((nm, depth, n_rim, n_bel, n_puf) + _WISPS[nm]), flush=True)
    for j, (pn, ox, oy, rx, ry) in enumerate(_PUDDLES):
        ent = by_name[pn]
        _puddle(m_pud, 880077 + j * 13, ent[1] + ox, ent[2] + oy, ent[3], rx, ry)

    n_faces = len(m_base.faces)
    obj_base = m_base.to_object("Cloud_Platforms_base")
    if len(obj_base.data.polygons) != n_faces:
        raise ValueError("云台件收工删了面（%d → %d）：分层重涂按面序对齐，两者必须相等"
                         % (n_faces, len(obj_base.data.polygons)))
    _paint_platforms(obj_base, groups)
    obj_pud = m_pud.to_object("Cloud_Platforms_puddles")
    plats_obj = _merge(obj_base, obj_pud, "Cloud_Platforms")
    for o in (obj_base, obj_pud):
        bpy.data.objects.remove(o, do_unlink=True)

    m_wf = K.Mesher()
    _waterfall(m_wf, east)
    fall_obj = m_wf.to_object("Cloud_Waterfall")
    _paint_fall(fall_obj)

    for nm, top, zmax in topz:                              # 顶高精确对齐 PLATFORMS
        ok = "OK" if abs(zmax - top) < 1e-4 else "偏差!"
        print("TOPZ %-9s 期望=%.2f 实测=%.4f %s" % (nm, top, zmax, ok), flush=True)
    _check_tops([plats_obj, fall_obj], plats)
    total = sum(sum(len(p.vertices) - 2 for p in o.data.polygons)
                for o in (plats_obj, fall_obj))
    print("BUDGET Cloud_* 总三角面=%d（上限 %d）%s"
          % (total, TRI_BUDGET, "OK" if total <= TRI_BUDGET else "超限!"), flush=True)
    return [plats_obj, fall_obj]
