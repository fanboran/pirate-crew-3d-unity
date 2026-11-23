# -*- coding: utf-8 -*-
"""cloudsea/mod_sky.py —— 远景层分件：远景小云岛 + 积云塔（纯背景，静态零碰撞）。

【内容（对象清单）】
    · Cloud_Far_SW / _SE / _NW / _NE  远景小云岛：DISTANT_ISLETS 四锚点各一座，3-4 团云球
          （Cloud_Shade 平底云腹 + Cloud_White 圆顶 + Cloud_Mist 侧瓣，三个鼓包高低错开）。
          顶 = 锚点顶高，底统一 z=0（离水面 0.4 m）——读作浮在远处的云团，不是平台。
    · Cloud_Far_Tower        西北角场外的积云塔：平底浮在 z≈1.25，花椰菜顶 ≤ 5 m。
    云球一律 = Mesher 单位球 → 下半球压扁（平底积云）→ 径向轻抖 → 椭球缩放并入。

    【已删除（2026-11-21，创始人裁决）】薄云带（N1-N3/E1-E2）与云拱——俯视角下它们
    在画面里读作"连成一条线的短云条/异物"，一条不留；体积云团（岛/塔）保留。

【机位口径】默认机位在东南（Blender +X/−Y；俯角 30°、方位 45° 正交，同 CameraFraming）：
    北缘 = 屏幕右上沿、西缘 = 左上沿，是远端，全在主云台身后。海面已连续（无水剖面墙）。

【摆放规则（build() 收尾逐团断言，违反即抛错，不静默）】
    1. 主云台避让（按高度分段，对齐 mod_clouds 的建型）：
         · 瓣带 = 足迹 + min(2.3, 0.6·短半轴)（台缘云瓣外伸）+ 留缝，z ≥ 台顶 − 2.2 直到无穷高
           ——远景既不与台缘云瓣粘连，也不可能悬到主云台上方；
         · 云腹柱 = 0.6·足迹 + 0.9 m + 留缝，z 从水面到台顶（下垂云絮）。
       云瀑落点保留圈全高度生效。锚点冲突时 `_fit_islet` 就近挪到最近的合规中心（日志打印）。
    2. 场外延 ≤ OUTSKIRT；云底 ≥ 水面 + 0.3；顶 ≤ 5 m；远景岛顶 ≤ 锚点顶高。
    3. 总三角面 ≤ TRI_BUDGET；材质只用 Cloud_*。
"""

import math
import random
from collections import namedtuple

import cloudsea_common as H
import kit_common as K

TAU = math.tau
INF = math.inf
SEED = 20261109                 # 固定种子：重跑同图

# ---- 材质槽（只用 Cloud_* 云色档）----
WHITE = "Cloud_White"           # 迎光圆顶
MIST = "Cloud_Mist"             # 侧瓣 / 薄云浮面
SHADE = "Cloud_Shade"           # 平底云腹（冷灰蓝）

# ---- 场地与摆放口径（Blender 本地系；场地 = mod_sea 的连续海面）----
FIELD_X, FIELD_Y = 19.0, 14.0   # 细节海面网格 X[-19,19] Y[-14,14]（外围另有 ±80/±240 圈层）
OUTSKIRT = 3.0                  # 远景允许伸出场地的距离
KEEPOUT_GAP = 0.4               # 远景与主云台视觉占位之间的留缝
LOBE_BAND = 2.2                 # 台缘云瓣 + 鼓带占台顶以下的高度
SECTION_TOL = 0.2               # 远景岛越界容差（_section_overshoot 已废弃，恒 0）
WATERFALL_KEEP_R = 2.0          # 云瀑落点（水雾圈）保留半径
ISLET_BOTTOM_Z = H.SEA_TOP_Z + 0.4   # 远景岛统一底高：离水面 0.4 m
MIN_BOTTOM_Z = H.SEA_TOP_Z + 0.3     # 任何云体的底不低于此（不贴水）
MAX_TOP_Z = 5.0
TRI_BUDGET = 10000
JITTER = 0.035                  # 云球径向抖动：轮廓不呆板，又不起犬牙

# ---- 远景小云岛 ----
#: 拟合压扁（rx 比, ry 比）：按需给挤在平台/云带夹缝里的岛压扁。
ISLET_SQUASH = {"Far_E": (0.7, 1.0)}

#: 云球编排，归一化到单位盘（|中心| + 半轴比 ≤ 0.95 → 整座岛落在拟合椭圆足迹内）：
#: (u, v, 横半轴比, 纵半轴比, 半高比(×H), 顶距岛顶比(×H；None = 底座贴底), 压底, 材质)
ISLET_LOBES = [
    (0.00, 0.00, 0.90, 0.80, 0.30, None, 0.70, SHADE),   # 云腹底座：宽扁平底
    (0.06, 0.04, 0.56, 0.52, 0.56, 0.00, 0.30, WHITE),   # 圆顶：顶到锚点顶高
    (-0.46, 0.18, 0.45, 0.42, 0.46, 0.12, 0.35, MIST),   # 侧瓣 A：略低于圆顶
    (0.44, -0.26, 0.40, 0.38, 0.40, 0.22, 0.40, MIST),   # 侧瓣 B：再低一档（小岛省掉）
]
SMALL_ISLET_D = 3.8             # 直径小于此的岛只用前三团

# ---- 积云塔（西北角场外）：塔芯撑轮廓 + 贴面侧瓣 + 花椰菜顶 ----
#: 相对塔心的 (dx, dy, z, 半轴 x, y, z, 压底, 材质)
TOWER_XY = (-20.6, -16.0)
TOWER_PUFFS = [
    (0.00, 0.00, 1.40, 1.15, 0.95, 0.45, 0.70, SHADE),   # 平底云腹，浮在 z≈1.25
    (0.00, 0.00, 2.75, 0.95, 0.84, 1.45, 0.30, MIST),    # 塔芯
    (0.66, 0.40, 2.10, 0.62, 0.56, 0.58, 0.30, MIST),    # 塔身侧瓣（朝默认机位）
    (-0.70, -0.18, 2.40, 0.58, 0.54, 0.56, 0.25, MIST),
    (0.48, -0.28, 3.05, 0.56, 0.52, 0.54, 0.20, WHITE),
    (-0.50, 0.32, 3.30, 0.56, 0.52, 0.54, 0.20, WHITE),
    (0.30, 0.12, 3.95, 0.52, 0.48, 0.50, 0.15, WHITE),   # 花椰菜冠：两瓣并排
    (-0.32, -0.14, 4.08, 0.50, 0.48, 0.50, 0.15, WHITE),
]


Puff = namedtuple("Puff", "x y z a b c rz flat")
Zone = namedtuple("Zone", "name x y ex ey z_lo z_hi")


# ---------------------------------------------------------------------------
# 云球图元
# ---------------------------------------------------------------------------

_SPHERES = {}


def _unit_sphere(seg, rings):
    """Mesher 单位球模板（按分段缓存）。"""
    key = (seg, rings)
    if key not in _SPHERES:
        tmp = K.Mesher()
        tmp.sphere(1.0, seg=seg, rings=rings, mat=WHITE)
        _SPHERES[key] = (list(tmp.verts), list(tmp.faces))
    return _SPHERES[key]


def _puff(m, rng, at, radii, mat, rz=0.0, flat=0.0, seg=12, rings=6):
    """一团云球：单位球 → 下半球压扁 flat（0 = 正球）→ 径向轻抖 → 椭球缩放 / 绕 Z 转 → 并入 m。"""
    verts, faces = _unit_sphere(seg, rings)
    out = []
    for x, y, z in verts:
        k = 1.0 + JITTER * (2.0 * rng.random() - 1.0)
        if z < 0.0:
            z *= 1.0 - flat
        out.append((x * k, y * k, z * k))
    m.add(out, faces, at=at, rz=rz, scale=radii, mat=mat, smooth=True)
    return Puff(at[0], at[1], at[2], radii[0], radii[1], radii[2], rz, flat)


def _extent(p):
    """计入抖动上限的包围盒 (x0, x1, y0, y1, z0, z1)。"""
    k = 1.0 + JITTER
    big_a, big_b = p.a * k, p.b * k
    c, s = math.cos(p.rz), math.sin(p.rz)
    hx = math.hypot(big_a * c, big_b * s)
    hy = math.hypot(big_a * s, big_b * c)
    return (p.x - hx, p.x + hx, p.y - hy, p.y + hy,
            p.z - p.c * (1.0 - p.flat) * k, p.z + p.c * k)


# ---------------------------------------------------------------------------
# 摆放判据
# ---------------------------------------------------------------------------

def _platform_zones():
    """主云台视觉占位（每座两段椭圆柱）+ 云瀑落点圈，见模块头规则 1。"""
    zones = []
    for name, cx, cy, top, hx, hy, _role in H.PLATFORMS:
        reach = min(2.3, 0.6 * min(hx, hy)) + KEEPOUT_GAP
        zones.append(Zone(name, cx, cy, hx + reach, hy + reach, top - LOBE_BAND, INF))
        zones.append(Zone(name, cx, cy, 0.6 * hx + 0.9 + KEEPOUT_GAP,
                          0.6 * hy + 0.9 + KEEPOUT_GAP, H.SEA_TOP_Z, top))
    wx, wy = H.WATERFALL[1][0], H.WATERFALL[1][1]
    zones.append(Zone("Waterfall", wx, wy, WATERFALL_KEEP_R, WATERFALL_KEEP_R, -INF, INF))
    return zones


ZONES = _platform_zones()

#: 远景岛拟合时额外避开积云塔（远景之间留缝，不粘成一团）
FAR_ZONES = [
    Zone("Cloud_Far_Tower", TOWER_XY[0], TOWER_XY[1], 1.6, 1.5, -INF, INF),
]

_RING32 = [(math.cos(TAU * k / 32), math.sin(TAU * k / 32)) for k in range(32)]
_RING16 = [(math.cos(TAU * k / 16), math.sin(TAU * k / 16)) for k in range(16)]


def _hits(x, y, a, b, rz, z0, z1, zones):
    """水平椭圆（中心 x,y / 半轴 a,b / 转角 rz）× 高度 [z0,z1] 碰到的第一个占位名；不碰返回 None。"""
    c, s = math.cos(rz), math.sin(rz)
    ring = [(x + a * u * c - b * v * s, y + a * u * s + b * v * c) for u, v in _RING32]
    for zone in zones:
        if z1 <= zone.z_lo or z0 >= zone.z_hi:
            continue
        zx, zy, ex, ey = zone.x, zone.y, zone.ex, zone.ey
        if ((x - zx) / ex) ** 2 + ((y - zy) / ey) ** 2 < 1.0:
            return zone.name
        if any(((px - zx) / ex) ** 2 + ((py - zy) / ey) ** 2 < 1.0 for px, py in ring):
            return zone.name
        for u, v in _RING16:                    # 占位整个被云球包住的情形
            dx, dy = zx + ex * u - x, zy + ey * v - y
            if ((dx * c + dy * s) / a) ** 2 + ((-dx * s + dy * c) / b) ** 2 < 1.0:
                return zone.name
    return None


def _section_overshoot(x0, x1, y0, y1):
    """【已废弃（2026-11-18）】+X/−Y 水剖面墙随切片侧壁一起移除，远景不再受墙限。
    平台避让走 _hits；保留桩位返回 0，调用方口径不变。"""
    return 0.0


def _section_min_bottom(e):
    """伸出水剖面 e 米时云底的最低高度（30° 俯角 / 45° 方位下投影落在墙顶线之上，+0.1 余量）。"""
    return 0.816 * e - 0.4 + 0.1


def _fit_islet(name, ax, ay, rx, ry, top):
    """锚点 → 最近的合规中心：避让主云台 / 云瀑 / 云塔云拱、不越 +X / −Y 水剖面、不出场外延。

    由近到远螺旋搜索（步长 0.05 m × 48 方位，最远 3 m），返回 (cx, cy, 挪动量)。
    """
    zones = ZONES + FAR_ZONES
    for k in range(61):
        d = 0.05 * k
        n = 1 if k == 0 else 48
        for j in range(n):
            ang = TAU * j / n
            cx, cy = ax + d * math.cos(ang), ay + d * math.sin(ang)
            x0, x1, y0, y1 = cx - rx, cx + rx, cy - ry, cy + ry
            if _section_overshoot(x0, x1, y0, y1) > SECTION_TOL:
                continue
            if (max(abs(x0), abs(x1)) > FIELD_X + OUTSKIRT
                    or max(abs(y0), abs(y1)) > FIELD_Y + OUTSKIRT):
                continue
            if _hits(cx, cy, rx, ry, 0.0, ISLET_BOTTOM_Z, top, zones):
                continue
            return cx, cy, d
    raise ValueError("[mod_sky] 远景岛 %s 在锚点 3 m 内找不到合规位置"
                     "（主云台 / 水剖面 / 场外延），请重定 DISTANT_ISLETS 锚点" % name)


# ---------------------------------------------------------------------------
# 远景小云岛
# ---------------------------------------------------------------------------

def _build_islet(rng, name, ax, ay, top, diameter):
    sx, sy = ISLET_SQUASH.get(name, (1.0, 1.0))
    rx, ry = 0.5 * diameter * sx, 0.5 * diameter * sy
    cx, cy, moved = _fit_islet(name, ax, ay, rx, ry, top)
    h = top - ISLET_BOTTOM_Z
    # 侧瓣沿场地切向排开：从场心看过去，岛的剖影是"一高两低"的横向云团
    turn = math.atan2(cy, cx) + 0.5 * math.pi + rng.uniform(-0.35, 0.35)
    ct, st = math.cos(turn), math.sin(turn)
    lobes = ISLET_LOBES if diameter >= SMALL_ISLET_D else ISLET_LOBES[:3]
    m = K.Mesher()
    puffs = []
    for u, v, pa, pb, hz, drop, flat, mat in lobes:
        px = cx + (u * ct - v * st) * rx
        py = cy + (u * st + v * ct) * ry
        c = hz * h
        pz = ISLET_BOTTOM_Z + c * (1.0 - flat) if drop is None else top - drop * h - c
        puffs.append(_puff(m, rng, (px, py, pz), (pa * rx, pb * ry, c), mat, flat=flat))
    obj = m.to_object("Cloud_" + name)
    note = "（锚点 (%.1f, %.1f) 挪 %.2f m）" % (ax, ay, moved) if moved else ""
    print("SKY %-18s 中心 (%.2f, %.2f)%s 顶 %.2f 足迹 %.1f×%.1f m 云球 %d"
          % (obj.name, cx, cy, note, top, 2 * rx, 2 * ry, len(puffs)), flush=True)
    return obj, puffs


# ---------------------------------------------------------------------------
# 积云塔
# ---------------------------------------------------------------------------

def _build_tower(rng):
    tx, ty = TOWER_XY
    m = K.Mesher()
    puffs = [_puff(m, rng, (tx + dx, ty + dy, z), (a, b, c), mat, flat=flat)
             for dx, dy, z, a, b, c, flat, mat in TOWER_PUFFS]
    return m.to_object("Cloud_Far_Tower"), puffs


# ---------------------------------------------------------------------------
# 收尾断言
# ---------------------------------------------------------------------------

def _check(objects, report):
    errs = []
    lim_x, lim_y = FIELD_X + OUTSKIRT + 1e-6, FIELD_Y + OUTSKIRT + 1e-6
    for obj_name, kind, cap, puffs in report:
        for i, p in enumerate(puffs):
            x0, x1, y0, y1, z0, z1 = _extent(p)
            tag = "%s#%d" % (obj_name, i)
            hit = _hits(p.x, p.y, p.a * (1.0 + JITTER), p.b * (1.0 + JITTER), p.rz,
                        z0, z1, ZONES)
            if hit:
                errs.append("%s 压进 %s 的视觉占位" % (tag, hit))
            e = _section_overshoot(x0, x1, y0, y1)
            if e > 0.0 and z0 < _section_min_bottom(e):
                errs.append("%s 伸出水剖面 %.2f m、底高 %.2f < %.2f，会盖住剖面"
                            % (tag, e, z0, _section_min_bottom(e)))
            if max(abs(x0), abs(x1)) > lim_x or max(abs(y0), abs(y1)) > lim_y:
                errs.append("%s 超出场外延 %.1f m：x[%.2f,%.2f] y[%.2f,%.2f]"
                            % (tag, OUTSKIRT, x0, x1, y0, y1))
            if z0 < MIN_BOTTOM_Z:
                errs.append("%s 云底 %.2f 贴水（下限 %.2f）" % (tag, z0, MIN_BOTTOM_Z))
            if z1 > MAX_TOP_Z:
                errs.append("%s 云顶 %.2f 超 %.1f m" % (tag, z1, MAX_TOP_Z))
            if kind == "islet" and z1 > cap + 0.06:
                errs.append("%s 远景岛顶 %.2f 高过锚点顶 %.2f" % (tag, z1, cap))
    tris = sum(len(poly.vertices) - 2 for obj in objects for poly in obj.data.polygons)
    if tris > TRI_BUDGET:
        errs.append("总三角面 %d 超预算 %d" % (tris, TRI_BUDGET))
    for obj in objects:
        for mat in obj.data.materials:
            if mat is None or not mat.name.startswith("Cloud_"):
                errs.append("%s 用了非 Cloud_* 材质 %r" % (obj.name, mat and mat.name))
    if errs:
        raise ValueError("[mod_sky] 摆放断言失败：\n  " + "\n  ".join(errs))
    print("SKY 断言通过：%d 对象 / %d 云球 / %d 三角面（预算 %d）"
          % (len(objects), sum(len(r[3]) for r in report), tris, TRI_BUDGET), flush=True)


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def build():
    rng = random.Random(SEED)
    objects, report = [], []

    def keep(obj, kind, cap, puffs):
        objects.append(obj)
        report.append((obj.name, kind, cap, puffs))

    for name, ax, ay, top, diameter in H.DISTANT_ISLETS:
        obj, puffs = _build_islet(rng, name, ax, ay, top, diameter)
        keep(obj, "islet", top, puffs)
    obj, puffs = _build_tower(rng)
    keep(obj, "tower", None, puffs)

    _check(objects, report)
    return objects
