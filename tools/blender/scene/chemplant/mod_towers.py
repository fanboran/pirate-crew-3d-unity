# -*- coding: utf-8 -*-
"""chemplant/mod_towers.py —— 主装置区分件（towers 分区）纯程序化建模。

本地系：原点 = 分区盒（20×14×26）中心在地面投影，+X 东 / +Y 北 / +Z 上，地面 z=0。
内容：
    T-101 精馏塔    Ø2.6 × 22 m：6 段壳体 + 段间法兰环带 + 混凝土基础/钢裙座 +
                    两侧交替 4 层操作平台（猫道绕塔连通 + 笼式爬梯）+
                    顶置卧式冷凝器 + 放空管/安全阀 + 侧面密集接管
    T-102 吸收塔    Ø1.8 × 15 m：顶部弯头 + 通长笼式爬梯 + 加强环
    R-201/202       卧式反应器 ×2 Ø1.6 × 6 m：鞍座 + 椭圆封头 + 端法兰 + 侧接管
    ST-01 烟囱      26 m：Ø2.4 收 Ø1.2 + 底部加厚锥段 + 混凝土基础 +
                    顶部平台（栏杆，缺柱/垂轨）+ 通长爬梯 + 三道褪色警示环 + 顶部破口
    框架塔          8×8×14 m：8 工字钢柱 + 三层梁格 + 交叉斜撑 + 格栅楼面（带破洞）+
                    卧式换热器 + 一台不转的死风机（缺一叶）+ 外挂楼梯
    地面            基础墩 / 散管 / T 形支架 / 阀门 / 锈板补丁 / 油渍 / 断管口 / 天桥
"""

import math

import kit_common as K

TAU = K.TAU
R = math.radians

# ---------------------------------------------------------------------------
# 布局常量（本地系；分区盒 X[-10,10] Y[-7,7] Z[0,26]）
# ---------------------------------------------------------------------------

T101 = (-6.4, 3.0)          # 精馏塔轴心
T102 = (-5.8, -3.8)         # 吸收塔轴心
ST01 = (6.2, -4.0)          # 烟囱轴心
FRAME = (5.2, 2.6)          # 框架塔中心
R201_X, R202_X = -2.6, -0.6  # 卧式反应器轴向 Y，y ∈ [0.2, 6.2]
RY0, RY1 = 0.2, 6.2

#: 图元本地 +Z 轴映射到世界方向所需的 (rz, ry)
_AXIS_ROT = {
    "+x": (0.0, R(90.0)),
    "-x": (0.0, R(-90.0)),
    "+y": (R(90.0), R(90.0)),
    "-y": (R(-90.0), R(90.0)),
    "+z": (0.0, 0.0),
}
_AXIS_VEC = {
    "+x": (1.0, 0.0, 0.0), "-x": (-1.0, 0.0, 0.0),
    "+y": (0.0, 1.0, 0.0), "-y": (0.0, -1.0, 0.0),
    "+z": (0.0, 0.0, 1.0),
}


# ---------------------------------------------------------------------------
# 局部辅助图元（只动自己的文件：kit_common 缺的旋转自由度用 m.add 拼）
# ---------------------------------------------------------------------------

def _revolve(m, profile, at=(0, 0, 0), seg=24, mat="Kit_SteelPale", smooth=True,
             caps=True, rz=0.0, ry=0.0, rx=0.0):
    """旋转体（母线绕本地 Z），支持完整欧拉角——卧式封头/横置环带靠 ry/rx 摆位。"""
    v, f, sf = [], [], []
    rings = []
    for (r, z) in profile:
        if r <= 1e-6:
            rings.append(("pole", len(v)))
            v.append((0.0, 0.0, z))
        else:
            s = len(v)
            for i in range(seg):
                a = TAU * i / seg
                v.append((r * math.cos(a), r * math.sin(a), z))
            rings.append(("ring", s))
    for k in range(len(rings) - 1):
        t0, s0 = rings[k]
        t1, s1 = rings[k + 1]
        if t0 == "ring" and t1 == "ring":
            for i in range(seg):
                j = (i + 1) % seg
                f.append((s0 + i, s0 + j, s1 + j, s1 + i))
                sf.append(len(f) - 1)
        elif t0 == "pole" and t1 == "ring":
            for i in range(seg):
                j = (i + 1) % seg
                f.append((s0, s1 + j, s1 + i))
                sf.append(len(f) - 1)
        else:
            for i in range(seg):
                j = (i + 1) % seg
                f.append((s0 + i, s0 + j, s1))
                sf.append(len(f) - 1)
    if caps and profile[0][0] > 1e-6:
        f.append(tuple(range(seg - 1, -1, -1)))
    if caps and profile[-1][0] > 1e-6:
        last = rings[-1][1]
        f.append(tuple(range(last, last + seg)))
    m.add(v, f, at, rz, ry, rx, mat, smooth, smooth_faces=set(sf))


def _flange(m, center, axis, r, thick=0.06, bolts=6, mat="Kit_Rust",
            bolt_mat="Kit_Iron"):
    """任意朝向的法兰盘 + 螺栓圈（kit_common.flange 只朝天，这里补齐）。"""
    rz, ry = _AXIS_ROT[axis]
    n = _AXIS_VEC[axis]
    if axis in ("+x", "-x"):
        u, w = (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)
    elif axis in ("+y", "-y"):
        u, w = (1.0, 0.0, 0.0), (0.0, 0.0, 1.0)
    else:
        u, w = (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)
    rf = r * 1.45
    at = (center[0] - n[0] * thick / 2.0, center[1] - n[1] * thick / 2.0,
          center[2] - n[2] * thick / 2.0)
    m.cyl(rf, thick, at=at, seg=max(12, bolts * 2), mat=mat, rz=rz, ry=ry)
    bh = thick + 0.05
    for i in range(bolts):
        a = TAU * i / bolts
        ca, sa = math.cos(a) * rf * 0.75, math.sin(a) * rf * 0.75
        off = (u[0] * ca + w[0] * sa, u[1] * ca + w[1] * sa, u[2] * ca + w[2] * sa)
        m.cyl(0.04, bh,
              at=(center[0] + off[0] - n[0] * bh / 2.0,
                  center[1] + off[1] - n[1] * bh / 2.0,
                  center[2] + off[2] - n[2] * bh / 2.0),
              seg=6, mat=bolt_mat, rz=rz, ry=ry)
    return rf


def _nozzle(m, base, axis, r, length, mat="Kit_SteelPale", flange=True,
            fmat="Kit_Rust"):
    """短接管 + 端法兰（flange=False 出断口）。base = 管根（设备壁上）。"""
    n = _AXIS_VEC[axis]
    end = (base[0] + n[0] * length, base[1] + n[1] * length, base[2] + n[2] * length)
    m.tube([tuple(base), end], r, seg=10, mat=mat)
    if flange:
        _flange(m, end, axis, r, mat=fmat)
    return end


def _valve(m, pos, axis, r):
    """卧管上的截止阀：阀体 + 双小法兰 + 上引阀杆 + 手轮。"""
    n = _AXIS_VEC[axis]
    rz, ry = _AXIS_ROT[axis]
    m.cyl(r * 0.85, 0.22, at=(pos[0] - n[0] * 0.11, pos[1] - n[1] * 0.11,
                              pos[2] - n[2] * 0.11),
          seg=10, mat="Kit_Iron", rz=rz, ry=ry)
    for s in (-1, 1):
        c = (pos[0] + n[0] * 0.16 * s, pos[1] + n[1] * 0.16 * s,
             pos[2] + n[2] * 0.16 * s)
        _flange(m, c, axis, r * 0.85, thick=0.045, bolts=4)
    m.cyl(0.028, r * 0.85 + 0.18, at=(pos[0], pos[1], pos[2]), seg=6, mat="Kit_Iron")
    wz = pos[2] + r * 0.85 + 0.20
    _revolve(m, [(0.14, 0.0), (0.18, 0.0), (0.18, 0.04), (0.14, 0.04)],
             at=(pos[0], pos[1], wz), seg=12, mat="Kit_Rust", smooth=False)
    for k in range(3):
        a = TAU * k / 3.0
        m.member((pos[0], pos[1], wz + 0.02),
                 (pos[0] + 0.16 * math.cos(a), pos[1] + 0.16 * math.sin(a), wz + 0.02),
                 0.022, 0.022, mat="Kit_Rust")


def _ring_joint(m, cx, cy, z, r_shell, mat="Kit_Rust", half=0.10, bolts=16):
    """立式塔器段间法兰环带 + 一圈螺栓头。"""
    _revolve(m, [(r_shell, z - half), (r_shell + 0.16, z - half),
                 (r_shell + 0.16, z + half), (r_shell, z + half)],
             at=(cx, cy, 0.0), seg=24, mat=mat, smooth=True)
    for i in range(bolts):
        a = TAU * i / bolts
        m.cyl(0.038, half * 2 + 0.04,
              at=(cx + math.cos(a) * (r_shell + 0.09),
                  cy + math.sin(a) * (r_shell + 0.09), z - half - 0.02),
              seg=6, mat="Kit_Iron")


def _stiff_ring(m, cx, cy, z, r_shell, dr, half, mat):
    _revolve(m, [(r_shell, z - half), (r_shell + dr, z - half),
                 (r_shell + dr, z + half), (r_shell, z + half)],
             at=(cx, cy, 0.0), seg=24, mat=mat, smooth=True)


def _streak(m, cx, cy, az, r, z0, z1, w, mat="Kit_RustDark"):
    """贴壁锈流痕薄板（rz=az 使薄边指向径向；r 给板心半径，需明显凸出壳面）。"""
    m.box((0.09, w, z1 - z0), at=(cx + math.cos(az) * r, cy + math.sin(az) * r, z0),
          rz=az, mat=mat)


def _head_profile(r, depth):
    """2:1 椭圆封头母线：极点 (0,0) → (r, depth)。"""
    pts = [(0.0, 0.0)]
    for k in range(1, 5):
        t = math.pi / 2.0 * k / 4.0
        pts.append((r * math.sin(t), depth * (1.0 - math.cos(t))))
    return pts


def _drum(m, p0, p1, axis, r, mat="Kit_SteelPale", seg=20, hd=None, straps=(),
          strap_mat="Kit_RustDark"):
    """卧式筒体（沿 ±x/±y）+ 两端椭圆封头 + 可选环箍。p0→p1 为轴线两端。"""
    hd = r / 2.0 if hd is None else hd
    m.tube([tuple(p0), tuple(p1)], r, seg=seg, mat=mat)
    opp = {"+x": "-x", "-x": "+x", "+y": "-y", "-y": "+y"}[axis]
    prof = _head_profile(r, hd)
    rz0, ry0 = _AXIS_ROT[opp]
    _revolve(m, prof, at=p0, seg=seg, mat=mat, caps=False, rz=rz0, ry=ry0)
    rz1, ry1 = _AXIS_ROT[axis]
    _revolve(m, prof, at=p1, seg=seg, mat=mat, caps=False, rz=rz1, ry=ry1)
    for t in straps:
        c = (p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t,
             p0[2] + (p1[2] - p0[2]) * t)
        rzs, rys = _AXIS_ROT[axis]
        _revolve(m, [(r + 0.005, -0.06), (r + 0.045, -0.06),
                     (r + 0.045, 0.06), (r + 0.005, 0.06)],
                 at=c, seg=seg, mat=strap_mat, caps=False, rz=rzs, ry=rys)


def _saddle(m, c, axis, r, h, across, mat="Kit_RustDark"):
    """鞍座：卧式容器支撑（c = 鞍座顶面中心）。"""
    n = _AXIS_VEC[axis]
    if axis in ("+x", "-x"):
        size = (h, across, 0.5)
    else:
        size = (across, h, 0.5)
    # 简化为梯形感：底座宽板 + 两片肋板
    base = (c[0] - n[0] * h / 2.0, c[1] - n[1] * h / 2.0, c[2] - h)
    m.box(size[:2] + (h,), at=base, mat=mat)
    for s in (-1, 1):
        w = (c[0] + s * across * 0.3 - 0.03, c[1] + s * across * 0.3 - 0.03,
             c[2] - h)
        if axis in ("+x", "-x"):
            m.box((0.06, across * 0.8, h * 0.85), at=w, mat="Kit_Iron")
        else:
            m.box((across * 0.8, 0.06, h * 0.85), at=w, mat="Kit_Iron")


def _catwalk(m, cx, cy, z, az_a, az_b, r_a, r_b, ring, col_a=None, col_b=None,
             w=0.66, mat="Kit_RustDark"):
    """绕塔猫道：径向进出段 + 环向弧段（箱形踏板 + 单侧栏杆 + 塔壁斜撑）。"""
    def P(r, a):
        return (cx + r * math.cos(a), cy + r * math.sin(a))

    n = max(2, int(abs(az_b - az_a) / R(26.0)))
    azl = [az_a + (az_b - az_a) * i / n for i in range(n + 1)]
    pl = [P(r_a, az_a)] + [P(ring, a) for a in azl] + [P(r_b, az_b)]
    for i in range(len(pl) - 1):
        p, q = pl[i], pl[i + 1]
        dx, dy = q[0] - p[0], q[1] - p[1]
        ln = math.hypot(dx, dy)
        mx, my = (p[0] + q[0]) / 2.0, (p[1] + q[1]) / 2.0
        m.box((ln, w, 0.07), at=(mx, my, z - 0.07), rz=math.atan2(dy, dx), mat=mat)
    m.railing(pl, h=1.05, at=(0, 0, z), post_step=1.5, mat="Kit_Rust")
    for a in azl[1:-1]:
        s = P(1.32, a)
        p = P(ring, a)
        m.member((s[0], s[1], z - 0.62), (p[0], p[1], z - 0.10), 0.07, 0.07,
                 mat="Kit_Rust")
    for (r, a, cz) in ((r_a, az_a, col_a), (r_b, az_b, col_b)):
        if cz is None:
            continue
        p = P(r, a)
        pxv, pyv = -math.sin(a), math.cos(a)
        for s in (-1, 1):
            m.member((p[0] + pxv * 0.26 * s, p[1] + pyv * 0.26 * s, cz),
                     (p[0] + pxv * 0.26 * s, p[1] + pyv * 0.26 * s, z - 0.07),
                     0.09, 0.09, mat="Kit_Rust")


def _tower_platform(m, cx, cy, z, side, rail_sides=None, rail_h=1.1):
    """精馏塔悬挑操作平台（side=-1 西 / +1 东）。"""
    px = cx + side * 2.25
    if rail_sides is None:
        rail_sides = "nsw" if side < 0 else "nse"
    _plat(m, (1.9, 3.4), z, at=(px, cy), mat="Kit_Rust",
          rail_sides=rail_sides, rail_mat="Kit_Rust")
    for s in (-1, 1):
        m.member((cx + side * 1.28, cy + s * 1.2, z - 0.16),
                 (cx + side * 3.05, cy + s * 1.2, z - 0.16), 0.12, 0.07,
                 mat="Kit_RustDark")
        m.member((cx + side * 1.30, cy + s * 1.35, z - 0.75),
                 (cx + side * 2.95, cy + s * 1.55, z - 0.14), 0.07, 0.07,
                 mat="Kit_Rust")


def _broken_rail(m, pts, z, skip_spans, h=1.05, mat="Kit_Rust"):
    """带缺口的定制栏杆：skip_spans = 要跳过的段序号集合（立柱照立，横杆缺失）。"""
    for i in range(len(pts) - 1):
        if i in skip_spans:
            continue
        a, b = pts[i], pts[i + 1]
        m.pipe_member((a[0], a[1], z + h), (b[0], b[1], z + h), r=0.032, seg=6, mat=mat)
        m.pipe_member((a[0], a[1], z + h * 0.52), (b[0], b[1], z + h * 0.52),
                      r=0.024, seg=6, mat=mat)
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        seg = math.hypot(b[0] - a[0], b[1] - a[1])
        k = max(1, int(seg / 1.5))
        for j in range(k):
            t = j / float(k)
            m.member((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, z),
                     (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, z + h),
                     0.05, 0.05, mat=mat)


def _plat(m, size, z, at, rail_sides="nesw", mat="Kit_Rust", rail_mat=None,
          rail_h=1.1, thick=0.08):
    """操作平台修正版（kit_common.platform 的踏板与栏杆错位半格，见收工汇报）。
    at = 平台中心的地面投影；rail_sides ⊂ "nesw"。"""
    w, d = size
    rail_mat = rail_mat or mat
    m.box((w, d, thick), at=(at[0], at[1], z - thick), mat=mat)
    m.box((w, 0.05, 0.1), at=(at[0], at[1] - d / 2 + 0.025, z), mat=rail_mat)
    m.box((w, 0.05, 0.1), at=(at[0], at[1] + d / 2 - 0.025, z), mat=rail_mat)
    m.box((0.05, d, 0.1), at=(at[0] - w / 2 + 0.025, at[1], z), mat=rail_mat)
    m.box((0.05, d, 0.1), at=(at[0] + w / 2 - 0.025, at[1], z), mat=rail_mat)
    ox, oy = at[0] - w / 2.0, at[1] - d / 2.0
    if "n" in rail_sides:
        m.railing([(ox, oy + d), (ox + w, oy + d)], h=rail_h, at=(0, 0, z),
                  mat=rail_mat)
    if "s" in rail_sides:
        m.railing([(ox, oy), (ox + w, oy)], h=rail_h, at=(0, 0, z), mat=rail_mat)
    if "w" in rail_sides:
        m.railing([(ox, oy), (ox, oy + d)], h=rail_h, at=(0, 0, z), mat=rail_mat)
    if "e" in rail_sides:
        m.railing([(ox + w, oy), (ox + w, oy + d)], h=rail_h, at=(0, 0, z),
                  mat=rail_mat)


def _grat(m, size, at, thick=0.05, bar=0.05, mat="Kit_RustDark", direction="x",
          step=0.28):
    """格栅板自实现（kit_common.grating 的 at 是东北角语义，且筋条相对面板偏移半格）。
    at = 中心 xy + 底 z。"""
    w, d = size
    m.box((w, d, thick), at=(at[0], at[1], at[2]), mat=mat)
    if direction == "x":
        n = max(2, int(d / step))
        for i in range(1, n):
            m.box((w, bar, bar), at=(at[0], at[1] - d / 2.0 + d * i / n,
                                     at[2] + thick), mat=mat)
    else:
        n = max(2, int(w / step))
        for i in range(1, n):
            m.box((bar, d, bar), at=(at[0] - w / 2.0 + w * i / n, at[1],
                                     at[2] + thick), mat=mat)


def _stairs2(m, at, h, run, w=1.0, mat="Kit_RustDark", rail_mat="Kit_Rust"):
    """直跑楼梯修正版（kit_common.stairs 踏步偏移半格）。at = 梯段底端中心，向 +Y 上行。"""
    steps = max(3, int(round(h / 0.22)))
    rise = h / steps
    tread = run / steps
    for i in range(steps):
        m.box((w, tread, rise + 0.02),
              at=(at[0], at[1] + i * tread + tread / 2.0, i * rise), mat=mat)
    for side in (-1, 1):
        m.member((at[0] + side * w / 2.0, at[1], 0.0),
                 (at[0] + side * w / 2.0, at[1] + run, h), 0.12, 0.06, mat=mat)
        m.pipe_member((at[0] + side * (w / 2.0 + 0.06), at[1], 1.0),
                      (at[0] + side * (w / 2.0 + 0.06), at[1] + run, h + 1.0),
                      r=0.035, mat=rail_mat)
        for k in range(1, 4):
            t = k / 4.0
            m.member((at[0] + side * (w / 2.0 + 0.06), at[1] + run * t, h * t),
                     (at[0] + side * (w / 2.0 + 0.06), at[1] + run * t, h * t + 1.0),
                     0.05, 0.05, mat=rail_mat)


# ---------------------------------------------------------------------------
# T-101 精馏塔
# ---------------------------------------------------------------------------

def _t101(m):
    cx, cy = T101
    rt = 1.3

    # 基础：混凝土墩两级 + 地脚螺栓
    m.box((4.2, 4.2, 0.6), at=(cx, cy, 0.0), mat="Kit_ConcreteMid")
    m.box((3.4, 3.4, 0.5), at=(cx, cy, 0.6), mat="Kit_ConcreteLight")
    for i in range(8):
        a = TAU * i / 8.0
        m.cyl(0.05, 0.28, at=(cx + math.cos(a) * 1.5, cy + math.sin(a) * 1.5, 1.1),
              seg=6, mat="Kit_Iron")

    # 钢裙座 + 底环 + 竖向加劲肋 + 裙座检修口（暗色假洞）
    m.cyl(1.42, 1.7, at=(cx, cy, 1.1), seg=24, mat="Kit_RustDark")
    _revolve(m, [(1.42, 1.1), (1.55, 1.1), (1.55, 1.32), (1.42, 1.32)],
             at=(cx, cy, 0.0), seg=24, mat="Kit_Iron")
    for i in range(10):
        a = TAU * i / 10.0
        m.member((cx + math.cos(a) * 1.42, cy + math.sin(a) * 1.42, 1.15),
                 (cx + math.cos(a) * 1.42, cy + math.sin(a) * 1.42, 2.72),
                 0.09, 0.06, mat="Kit_RustDark")
    m.box((0.7, 0.1, 1.1), at=(cx + 0.6, cy - 1.32, 1.15), mat="Kit_GlassDark")

    # 壳体 6 段（2.8→22.0，每段 3.2）+ 段中焊缝细圈
    seg_h = 3.2
    z0 = 2.8
    for k in range(6):
        m.cyl(rt, seg_h, at=(cx, cy, z0 + k * seg_h), seg=24,
              mat="Kit_SteelPale")
        _stiff_ring(m, cx, cy, z0 + k * seg_h + seg_h * 0.55, rt - 0.005, 0.012,
                    0.025, "Kit_RustDark")
    for k in range(1, 6):
        _ring_joint(m, cx, cy, z0 + k * seg_h, rt)
    # 顶法兰
    _revolve(m, [(rt, 22.0), (rt + 0.16, 22.0), (rt + 0.16, 22.22), (rt, 22.22)],
             at=(cx, cy, 0.0), seg=24, mat="Kit_Rust")

    # 锈痕 / 锈斑（第 4 段最重；南东受光面加密）
    for (az_d, za, zb, w, mt) in ((15, 3.2, 6.2, 0.55, "Kit_RustDark"),
                                  (105, 6.6, 8.9, 0.7, "Kit_Rust"),
                                  (200, 9.6, 12.2, 0.6, "Kit_RustDark"),
                                  (250, 12.8, 15.4, 0.85, "Kit_Rust"),
                                  (305, 16.0, 17.6, 0.5, "Kit_RustDark"),
                                  (60, 18.9, 20.4, 0.6, "Kit_Rust"),
                                  (160, 20.0, 21.8, 0.7, "Kit_RustDark"),
                                  (135, 3.4, 7.6, 0.9, "Kit_Rust"),
                                  (120, 15.8, 19.4, 0.75, "Kit_RustDark"),
                                  (280, 16.2, 19.0, 0.6, "Kit_Rust")):
        _streak(m, cx, cy, R(az_d), rt + 0.04, za, zb, w, mt)
    _streak(m, cx, cy, R(35), rt + 0.01, 12.6, 15.6, 1.6, "Kit_Rust")
    _streak(m, cx, cy, R(285), rt + 0.01, 3.0, 6.0, 1.3, "Kit_RustDark")

    # 4 层交替操作平台（W/E/W/E）
    levels = (7.0, 11.4, 15.8, 20.2)
    sides = (-1, 1, -1, 1)
    for z, sd in zip(levels, sides):
        if z == 15.8:
            # 第三层：南栏残破（缺一段横杆）
            _tower_platform(m, cx, cy, z, sd, rail_sides="nw")
            px = cx + sd * 2.25
            _broken_rail(m, [(px - 0.95, cy - 1.7), (px + 0.15, cy - 1.7),
                             (px + 0.95, cy - 1.7)], z, skip_spans={0}, mat="Kit_Rust")
        else:
            _tower_platform(m, cx, cy, z, sd)

    # 爬梯：地面→L1（南面）+ 各层北端笼梯
    m.ladder((cx, cy - 1.56, 0.15), h=7.30, cage=True, mat="Kit_Rust")
    for z, sd in zip(levels[:-1], sides[:-1]):
        m.ladder((cx + sd * 2.95, cy + 1.15, z), h=4.75, cage=True,
                 rz=R(-90.0) if sd < 0 else R(90.0), mat="Kit_Rust")

    # 绕塔猫道（地面→L1 走西南，其余层北面折返）
    _catwalk(m, cx, cy, 7.0, R(268.0), R(217.0), 1.56, 2.86, 1.78,
             col_a=0.0, col_b=None)
    for z, cz in ((11.4, 7.0), (15.8, 11.4), (20.2, 15.8)):
        _catwalk(m, cx, cy, z, R(158.7), R(21.3), 3.16, 3.16, 1.80,
                 col_a=cz, col_b=None)

    # 侧面接管（含一只阀门、一只断口）
    _nozzle(m, (cx + 1.28, cy, 4.6), "+x", 0.20, 0.65)
    _nozzle(m, (cx - 1.28, cy, 9.0), "-x", 0.15, 0.55)
    nend = _nozzle(m, (cx + 1.28, cy, 13.0), "+x", 0.24, 0.70)
    _valve(m, (nend[0] + 0.28, nend[1], nend[2]), "+x", 0.24)
    _nozzle(m, (cx - 1.28, cy, 17.2), "-x", 0.13, 0.5, flange=False)   # 断口
    _nozzle(m, (cx, cy + 1.28, 19.6), "+y", 0.14, 0.5)
    _nozzle(m, (cx - 0.5, cy - 1.40, 1.6), "-y", 0.16, 0.4)
    _nozzle(m, (cx + 0.6, cy - 1.36, 1.6), "-y", 0.12, 0.4, flange=False)

    # 顶置卧式冷凝器（鞍座 + 环箍）+ 放空管 + 安全阀
    for sx_ in (cx - 1.0, cx + 1.0):
        m.box((0.55, 0.72, 0.45), at=(sx_, cy, 22.22), mat="Kit_RustDark")
    _drum(m, (cx - 1.5, cy, 23.15), (cx + 1.5, cy, 23.15), "+x", r=0.6,
          seg=20, mat="Kit_Rust", hd=0.3, straps=(0.28, 0.72))
    m.tube([(cx + 0.4, cy, 23.72), (cx + 0.4, cy, 24.72), (cx + 1.5, cy, 24.72)],
           r=0.13, seg=10, mat="Kit_SteelPale")
    _flange(m, (cx + 1.5, cy, 24.72), "+x", 0.13)
    _nozzle(m, (cx - 0.7, cy, 23.72), "+z", 0.10, 0.30, fmat="Kit_SteelPale")
    m.cyl(0.13, 0.22, at=(cx - 0.7, cy, 24.02), seg=10, mat="Kit_Iron")
    m.cyl(0.05, 0.16, at=(cx - 0.7, cy, 24.24), seg=6, mat="Kit_Iron")
    m.cyl(0.09, 0.07, at=(cx - 0.7, cy, 24.40), seg=10, mat="Kit_Rust")


# ---------------------------------------------------------------------------
# T-102 吸收塔
# ---------------------------------------------------------------------------

def _t102(m):
    cx, cy = T102
    rt = 0.9

    m.box((3.0, 3.0, 0.5), at=(cx, cy, 0.0), mat="Kit_ConcreteMid")
    m.cyl(0.98, 1.1, at=(cx, cy, 0.5), seg=20, mat="Kit_RustDark")

    # 壳体 3 段（1.6→15.0）
    zs = (1.6, 6.0, 10.5, 15.0)
    for k in range(3):
        m.cyl(rt, zs[k + 1] - zs[k], at=(cx, cy, zs[k]), seg=20,
              mat="Kit_SteelBlue")
    for z in (6.0, 10.5):
        _ring_joint(m, cx, cy, z, rt, half=0.08, bolts=12)
    # 加强环 ×3
    for z in (3.8, 7.8, 12.2):
        _stiff_ring(m, cx, cy, z, rt, 0.085, 0.07, "Kit_SteelBlue")
    # 顶法兰
    _revolve(m, [(rt, 15.0), (rt + 0.14, 15.0), (rt + 0.14, 15.18), (rt, 15.18)],
             at=(cx, cy, 0.0), seg=20, mat="Kit_Rust")

    # 顶部弯头 + 端法兰
    m.tube([(cx, cy, 14.9), (cx, cy, 16.15), (cx, cy - 1.15, 16.15)],
           r=0.2, seg=10, mat="Kit_Rust")
    _flange(m, (cx, cy - 1.15, 16.15), "-y", 0.2)

    # 通长笼式爬梯（东侧）
    m.ladder((cx + 1.06, cy, 0.35), h=14.95, cage=True, rz=R(90.0), mat="Kit_Rust")

    # 接管：进料 / 出料（带阀）/ 断口
    _nozzle(m, (cx, cy + 0.88, 13.6), "+y", 0.13, 0.45)
    nend = _nozzle(m, (cx, cy - 0.88, 2.5), "-y", 0.17, 0.55)
    _valve(m, (nend[0], nend[1] - 0.30, nend[2]), "-y", 0.17)
    m.tube([(cx + 0.86, cy, 8.6), (cx + 1.32, cy, 8.78), (cx + 1.58, cy + 0.12, 8.5)],
           r=0.11, seg=8, mat="Kit_Rust")

    # 锈痕 + 底部锈带 + 油渍（锈斑集中在南面可见半圆）
    for (az_d, za, zb, w, mt) in ((165, 2.4, 5.6, 1.0, "Kit_Rust"),
                                  (205, 6.2, 9.4, 0.9, "Kit_RustDark"),
                                  (235, 9.8, 13.4, 0.85, "Kit_Rust"),
                                  (185, 11.4, 14.6, 0.7, "Kit_RustDark"),
                                  (330, 4.0, 6.6, 0.7, "Kit_RustDark"),
                                  (250, 5.2, 8.0, 0.6, "Kit_Rust")):
        _streak(m, cx, cy, R(az_d), rt + 0.04, za, zb, w, mt)
    _revolve(m, [(rt, 1.7), (rt + 0.05, 1.7), (rt + 0.05, 3.1), (rt, 3.1)],
             at=(cx, cy, 0.0), seg=20, mat="Kit_RustDark")
    m.cyl(1.0, 0.016, at=(cx, cy, 0.002), seg=14, mat="Kit_RustDark",
          smooth=False)


# ---------------------------------------------------------------------------
# 卧式反应器 ×2
# ---------------------------------------------------------------------------

def _reactor(m, x, broken_top=False, valve_side=False):
    zc, r = 1.7, 0.8
    # 基础墩 + 鞍座
    for yc in (1.3, 5.1):
        m.box((1.2, 0.95, 0.5), at=(x, yc, 0.0), mat="Kit_ConcreteDark")
        m.box((1.5, 0.5, 0.42), at=(x, yc, 0.5), mat="Kit_RustDark")
        for s in (-1, 1):
            m.box((0.06, 0.42, 0.36), at=(x + s * 0.55, yc, 0.53),
                  mat="Kit_Iron")
    # 筒体 + 双椭圆封头 + 环箍
    _drum(m, (x, RY0, zc), (x, RY1, zc), "+y", r=r, seg=20, mat="Kit_SteelPale",
          straps=(0.25, 0.55, 0.85))
    # 端法兰接管
    _nozzle(m, (x, RY0 - 0.4, zc), "-y", 0.2, 0.25)
    _nozzle(m, (x, RY1 + 0.4, zc), "+y", 0.2, 0.25)
    # 顶部接管
    if broken_top:
        m.tube([(x, 1.9, zc + 0.78), (x, 1.95, zc + 1.15), (x + 0.28, 2.05, zc + 1.05)],
               r=0.12, seg=8, mat="Kit_Rust")
    else:
        _nozzle(m, (x, 1.9, zc + 0.78), "+z", 0.14, 0.35)
    _nozzle(m, (x, 4.6, zc + 0.78), "+z", 0.14, 0.35)
    # 侧接管（朝框架塔一侧）+ 液位计
    _nozzle(m, (x + 0.78, 3.3, zc - 0.25), "+x", 0.11, 0.3)
    if valve_side:
        _valve(m, (x - 1.15, RY0 + 0.9, zc + 0.1), "-x", 0.1)
    m.box((0.05, 0.06, 0.5), at=(x - 0.12, RY0 - 0.30, zc - 0.25),
          mat="Kit_GlassDark")
    m.cyl(0.045, 0.1, at=(x - 0.145, RY0 - 0.33, zc - 0.53), seg=6, mat="Kit_Iron")
    m.cyl(0.045, 0.1, at=(x - 0.145, RY0 - 0.33, zc + 0.25), seg=6, mat="Kit_Iron")
    # 底部锈带 + 锈痕
    _revolve(m, [(r - 0.015, 0.0), (r + 0.015, 0.0), (r + 0.015, 0.3),
                 (r - 0.015, 0.3)],
             at=(x, RY0 + 0.7, zc), seg=20, mat="Kit_RustDark", caps=False,
             rz=R(90.0), ry=R(90.0))
    for (az_d, ya, w) in ((65, 2.6, 0.5), (250, 4.3, 0.7)):
        pass  # 卧筒锈蚀用环带与贴板表达
    m.box((0.05, 1.1, 0.5), at=(x - r - 0.005, RY0 + 2.4, zc - 0.55),
          mat="Kit_Rust")
    m.box((0.05, 0.8, 0.35), at=(x + r + 0.005, RY0 + 4.0, zc - 0.5),
          mat="Kit_RustDark")


# ---------------------------------------------------------------------------
# ST-01 烟囱
# ---------------------------------------------------------------------------

def _stack_body_r(z):
    return 1.24 - (1.24 - 0.62) * (z - 3.5) / 22.1


def _stack(m):
    sx, sy = ST01

    # 混凝土基础两级 + 地脚螺栓
    m.box((4.4, 4.4, 0.8), at=(sx, sy, 0.0), mat="Kit_ConcreteMid")
    m.box((3.4, 3.4, 0.5), at=(sx, sy, 0.8), mat="Kit_ConcreteLight")
    for i in range(8):
        a = TAU * i / 8.0
        m.cyl(0.05, 0.3, at=(sx + math.cos(a) * 1.5, sy + math.sin(a) * 1.5, 1.3),
              seg=6, mat="Kit_Iron")

    # 底部加厚锥段 + 底环
    _revolve(m, [(1.55, 1.3), (1.55, 2.2), (1.26, 3.3), (1.24, 3.6)],
             at=(sx, sy, 0.0), seg=28, mat="Kit_RustDark")
    _revolve(m, [(1.55, 1.3), (1.67, 1.3), (1.67, 1.46), (1.55, 1.46)],
             at=(sx, sy, 0.0), seg=28, mat="Kit_Iron")

    # 筒身（3.5→25.6 收径）
    _revolve(m, [(1.24, 3.5), (0.62, 25.6)], at=(sx, sy, 0.0), seg=28,
             mat="Kit_Rust")

    # 三道褪色警示环
    for (z0, z1) in ((7.4, 8.5), (14.4, 15.5), (20.4, 21.5)):
        r0, r1 = _stack_body_r(z0), _stack_body_r(z1)
        _revolve(m, [(r0 - 0.06, z0), (r0 + 0.02, z0), (r1 + 0.02, z1),
                     (r1 - 0.06, z1)],
                 at=(sx, sy, 0.0), seg=28, mat="Kit_PaintYellow")

    # 锈流痕
    for (az_d, z0, h, w) in ((20, 4.2, 3.2, 0.5), (75, 9.0, 2.6, 0.6),
                             (160, 5.5, 4.0, 0.7), (230, 12.0, 3.0, 0.5),
                             (300, 17.0, 3.6, 0.65), (110, 18.5, 2.4, 0.45)):
        rm = _stack_body_r(z0 + h / 2.0) + 0.03
        m.box((0.09, w, h), at=(sx + math.cos(R(az_d)) * rm,
                                sy + math.sin(R(az_d)) * rm, z0),
              rz=R(az_d), mat="Kit_RustDark")

    # 顶部平台（环板 + 托架）
    _revolve(m, [(0.98, 23.75), (1.52, 23.75), (1.52, 23.92), (0.98, 23.92)],
             at=(sx, sy, 0.0), seg=28, mat="Kit_RustDark")
    for i in range(6):
        a = TAU * i / 6.0
        m.member((sx + math.cos(a) * 1.24, sy + math.sin(a) * 1.24, 23.2),
                 (sx + math.cos(a) * 1.45, sy + math.sin(a) * 1.45, 23.80),
                 0.07, 0.07, mat="Kit_Rust")

    # 顶部栏杆：12 柱缺 1、歪 1，一段横杆垂落
    top_z, mid_z = 25.02, 24.48
    pts_top = []
    for i in range(12):
        a = TAU * i / 12.0
        p = (sx + math.cos(a) * 1.44, sy + math.sin(a) * 1.44)
        if i == 5:
            continue                                   # 缺柱
        tip = (p[0], p[1])
        if i == 8:
            tip = (p[0] + 0.16, p[1] + 0.12)           # 歪柱
        m.member((p[0], p[1], 23.92), (tip[0], tip[1], top_z), 0.05, 0.05,
                 mat="Kit_Rust")
        pts_top.append((i, p, tip))
    for i in range(12):
        if i in (4, 5):
            continue                                   # 缺柱两侧不拉上杆
        j = (i + 1) % 12
        a = TAU * i / 12.0
        b = TAU * j / 12.0
        pa = (sx + math.cos(a) * 1.44, sy + math.sin(a) * 1.44)
        pb = (sx + math.cos(b) * 1.44, sy + math.sin(b) * 1.44)
        m.pipe_member((pa[0], pa[1], top_z), (pb[0], pb[1], top_z), r=0.03, seg=6,
                      mat="Kit_Rust")
        m.pipe_member((pa[0], pa[1], mid_z), (pb[0], pb[1], mid_z), r=0.022, seg=6,
                      mat="Kit_Rust")
    # 垂落的一段横杆（4→6 跨，绕过缺柱）
    a4 = TAU * 4 / 12.0
    a6 = TAU * 6 / 12.0
    p4 = (sx + math.cos(a4) * 1.44, sy + math.sin(a4) * 1.44)
    p6 = (sx + math.cos(a6) * 1.44, sy + math.sin(a6) * 1.44)
    pm = ((p4[0] + p6[0]) / 2.0, (p4[1] + p6[1]) / 2.0 - 0.3)
    m.tube([(p4[0], p4[1], top_z), (pm[0], pm[1], top_z - 0.42),
            (p6[0], p6[1], top_z)], r=0.026, seg=6, mat="Kit_Rust")

    # 顶部破口：锯齿缺口小板 + 内腔暗盘
    for i in range(9):
        a = TAU * i / 9.0 + 0.2
        h = 0.32 + ((i * 7) % 5) * 0.14
        m.box((0.09, 0.42, h),
              at=(sx + math.cos(a) * 0.56, sy + math.sin(a) * 0.56, 25.28),
              rz=a, ry=R(6.0 + (i % 3) * 4.0),
              mat="Kit_Rust" if i % 2 == 0 else "Kit_RustDark")
    m.cyl(0.58, 0.05, at=(sx, sy, 24.72), seg=24, mat="Kit_RustDark")

    # 通长爬梯（南面）+ 与壁连接支架 + 一处歇脚板
    m.ladder((sx, sy - 1.64, 1.45), h=22.35, cage=True, mat="Kit_Rust")
    zk = 3.2
    while zk < 23.0:
        rb = _stack_body_r(zk)
        for s in (-1, 1):
            m.member((sx + s * 0.25, sy - 1.64, zk),
                     (sx + s * 0.2, sy - rb - 0.03, zk), 0.05, 0.05, mat="Kit_Rust")
        zk += 3.3
    _grat(m, (0.85, 0.6), at=(sx, sy - 1.8, 15.4), thick=0.05,
          mat="Kit_RustDark", direction="x")
    m.member((sx - 0.2, sy - 1.28, 15.1), (sx - 0.35, sy - 1.75, 15.42), 0.06, 0.06,
             mat="Kit_Rust")

    # 塔底散落：剥落碎板 + 焦渍
    m.box((0.6, 0.5, 0.08), at=(sx + 2.0, sy - 1.9, 0.0), rz=R(35.0), ry=R(12.0),
          mat="Kit_Rust")
    m.box((0.45, 0.4, 0.07), at=(sx - 2.1, sy + 0.6, 0.0), rz=R(-20.0),
          mat="Kit_ConcreteDark")
    m.cyl(0.6, 0.014, at=(sx - 1.5, sy - 2.0, 0.002), seg=14, mat="Kit_RustDark",
          smooth=False)


# ---------------------------------------------------------------------------
# 钢结构框架塔
# ---------------------------------------------------------------------------

def _frame(m):
    fx, fy = FRAME
    x0, x1 = fx - 4.0, fx + 4.0
    y0, y1 = fy - 4.0, fy + 4.0
    zl1, zl2, zr = 4.8, 9.4, 14.0

    cols = [(x0, y0), (x1, y0), (x0, y1), (x1, y1),
            (fx, y0), (fx, y1), (x0, fy), (x1, fy)]
    for i, (cxx, cyy) in enumerate(cols):
        m.box((0.85, 0.85, 0.5), at=(cxx, min(cyy, y1 - 0.06), 0.0),
              mat="Kit_ConcreteDark")
        m.box((0.42, 0.42, 0.05), at=(cxx, min(cyy, y1 - 0.06), 0.5), mat="Kit_Iron")
        mat = "Kit_Rust" if i in (2, 5) else "Kit_SteelBlue"   # 两根后换的锈柱
        m.ibeam((cxx, cyy, 0.55), (cxx, cyy, zr), h=0.34, w=0.2, tf=0.045,
                tw=0.03, mat=mat)

    # 三层梁格
    for zl in (zl1, zl2, zr):
        zb = zl - 0.17
        m.ibeam((x0, y0, zb), (x1, y0, zb), h=0.3, w=0.16, mat="Kit_SteelBlue")
        m.ibeam((x0, y1, zb), (x1, y1, zb), h=0.3, w=0.16, mat="Kit_SteelBlue")
        m.ibeam((x0, y0, zb), (x0, y1, zb), h=0.3, w=0.16, mat="Kit_SteelBlue")
        m.ibeam((x1, y0, zb), (x1, y1, zb), h=0.3, w=0.16, mat="Kit_SteelBlue")
        m.ibeam((x0, fy, zb), (x1, fy, zb), h=0.26, w=0.14, mat="Kit_SteelBlue")
        m.ibeam((fx, y0, zb), (fx, y1, zb), h=0.26, w=0.14, mat="Kit_SteelBlue")
        if zl == zr:
            for yy in (y0 + 2.0, y1 - 2.0):
                m.ibeam((x0, yy, zb), (x1, yy, zb), h=0.24, w=0.12,
                        mat="Kit_SteelBlue")
            for xx in (x0 + 2.0, x1 - 2.0):
                m.ibeam((xx, y0, zb), (xx, y1, zb), h=0.24, w=0.12,
                        mat="Kit_SteelBlue")
        else:
            for yy in (y0 + 2.6, y1 - 2.6):
                m.ibeam((x0, yy, zb), (x1, yy, zb), h=0.22, w=0.12,
                        mat="Kit_SteelBlue")

    # 交叉斜撑（S/W 面双层 X 撑 + 顶层单斜；E/N 面简化；西南角缺一根撑）
    def bay(a, b, z0, z1, cross, mat="Kit_Rust"):
        A = (a[0], a[1], z0)
        B = (a[0], a[1], z1)
        C = (b[0], b[1], z0)
        D = (b[0], b[1], z1)
        m.member(A, D, 0.09, 0.09, mat=mat)
        if cross:
            m.member(C, B, 0.09, 0.09, mat=mat)

    for (a, b) in (((x0, y0 + 0.07), (fx, y0 + 0.07)),
                   ((fx, y0 + 0.07), (x1, y0 + 0.07))):
        bay(a, b, 0.55, zl1, True)
        bay(a, b, zl1, zl2, True)
        bay(a, b, zl2, zr, False)
    for (a, b) in (((x0 + 0.07, y0), (x0 + 0.07, fy)),
                   ((x0 + 0.07, fy), (x0 + 0.07, y1))):
        bay(a, b, 0.55, zl1, True)
        bay(a, b, zl1, zl2, True)
        bay(a, b, zl2, zr, False)
    # 西面底层北跨：斜撑锈断（只剩下半根 + 一个短桩）
    m.member((x0 + 0.07, fy, 2.6), (x0 + 0.07, y1, zl1), 0.09, 0.09, mat="Kit_Rust")
    m.member((x0 + 0.07, fy, 0.55), (x0 + 0.07, fy + 0.7, 1.7), 0.09, 0.09,
             mat="Kit_Rust")
    for (a, b) in (((x1 - 0.07, y0), (x1 - 0.07, fy)),
                   ((x1 - 0.07, fy), (x1 - 0.07, y1))):
        bay(a, b, 0.55, zl1, False, mat="Kit_SteelBlue")
        bay(a, b, zl1, zl2, True, mat="Kit_SteelBlue")
    for (a, b) in (((x0, y1 - 0.07), (fx, y1 - 0.07)),
                   ((fx, y1 - 0.07), (x1, y1 - 0.07))):
        bay(a, b, 0.55, zl1, False, mat="Kit_SteelBlue")
        bay(a, b, zl1, zl2, True, mat="Kit_SteelBlue")
    # 柱顶隅撑（朝跨内方向）
    for (cxx, cyy) in cols:
        dx = (0.85 if cxx < fx else -0.85) if abs(cxx - fx) > 0.01 else 0.0
        dy = (0.85 if cyy < fy else -0.85) if abs(cyy - fy) > 0.01 else 0.0
        m.member((cxx, cyy, zr - 0.9), (cxx + dx, cyy + dy, zr - 0.05), 0.08, 0.08,
                 mat="Kit_SteelBlue")

    # 格栅楼面（L1/L2）+ 破洞
    _grat(m, (7.5, 7.5), at=(fx, fy, zl1), thick=0.05, mat="Kit_RustDark",
          direction="x")
    _grat(m, (7.5, 7.5), at=(fx, fy, zl2), thick=0.05, mat="Kit_Rust",
          direction="y")
    m.box((0.95, 0.75, 0.03), at=(fx - 1.6, fy - 1.2, zl2 + 0.10),
          mat="Kit_GlassDark")
    m.box((0.7, 0.55, 0.03), at=(fx + 2.1, fy + 1.8, zl2 + 0.10),
          mat="Kit_GlassDark")
    m.box((0.8, 0.6, 0.03), at=(fx + 1.4, fy - 2.2, zl1 + 0.10),
          mat="Kit_GlassDark")

    # 一层卧式换热器 + 环箍 + 接管
    _saddle(m, (3.0, fy, zl1 + 0.85), "+x", 0.55, 0.7, 1.15)
    _saddle(m, (6.9, fy, zl1 + 0.85), "+x", 0.55, 0.7, 1.15)
    zc = zl1 + 1.35
    _drum(m, (2.3, fy, zc), (6.9, fy, zc), "+x", r=0.55, seg=18,
          mat="Kit_SteelPale", hd=0.28, straps=(0.2, 0.45, 0.7, 0.9))
    _nozzle(m, (3.6, fy, zc + 0.53), "+z", 0.12, 0.3)
    _nozzle(m, (5.8, fy, zc + 0.53), "+z", 0.12, 0.3, flange=False)   # 断口
    _nozzle(m, (6.9 + 0.28, fy, zc), "+x", 0.14, 0.3)
    m.box((0.05, 0.9, 0.4), at=(4.4, fy - 0.56, zc - 0.3), mat="Kit_Rust")

    # 屋顶死风机：筒体 + 喇叭口 + 轮毂 + 3 叶（缺 1）+ 护网
    m.cyl(0.95, 0.6, at=(7.6, 5.0, zr), seg=20, mat="Kit_SteelBlue")
    _revolve(m, [(0.80, zr + 0.60), (0.99, zr + 0.88), (0.97, zr + 0.90),
                 (0.78, zr + 0.62)],
             at=(7.6, 5.0, 0.0), seg=20, mat="Kit_SteelBlue")
    m.cyl(0.17, 0.35, at=(7.6, 5.0, zr + 0.55), seg=10, mat="Kit_Iron")
    bv = [(0.18, -0.10, 0.0), (0.88, -0.055, 0.0), (0.88, 0.055, 0.0),
          (0.18, 0.10, 0.0), (0.18, -0.10, 0.045), (0.88, -0.055, 0.045),
          (0.88, 0.055, 0.045), (0.18, 0.10, 0.045)]
    bf = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6),
          (3, 0, 4, 7)]
    for k in range(3):
        m.add(bv, bf, at=(7.6, 5.0, zr + 0.88), rz=R(20.0 + k * 120.0), ry=R(26.0),
              mat="Kit_SteelPale")
    ring = [(7.6 + math.cos(TAU * i / 14.0) * 0.98,
             5.0 + math.sin(TAU * i / 14.0) * 0.98, zr + 1.16) for i in range(14)]
    m.tube(ring, 0.03, seg=5, mat="Kit_Rust", caps=False)
    for k in range(4):
        a = R(45.0 * k)
        m.member((7.6 + math.cos(a) * 0.92, 5.0 + math.sin(a) * 0.92, zr + 1.14),
                 (7.6 - math.cos(a) * 0.92, 5.0 - math.sin(a) * 0.92, zr + 1.14),
                 0.04, 0.04, mat="Kit_Rust")
    for i in range(6):
        a = TAU * i / 6.0
        m.member((7.6 + math.cos(a) * 0.95, 5.0 + math.sin(a) * 0.95, zr + 0.9),
                 (7.6 + math.cos(a) * 0.95, 5.0 + math.sin(a) * 0.95, zr + 1.16),
                 0.035, 0.035, mat="Kit_Rust")

    # 层间爬梯（笼式）
    m.ladder((x1 - 0.25, fy, zl1), h=zl2 - zl1 + 0.35, cage=True, rz=R(-90.0),
             mat="Kit_Rust")
    m.ladder((x0 + 0.25, fy, zl2), h=zr - zl2 + 0.35, cage=True, rz=R(90.0),
             mat="Kit_Rust")

    # 屋顶格栅（风机口留空）+ 女儿墙栏杆
    _grat(m, (5.25, 7.5), at=(3.975, fy, zr), thick=0.05, mat="Kit_RustDark",
          direction="y")
    _grat(m, (0.5, 7.5), at=(8.8, fy, zr), thick=0.05, mat="Kit_RustDark",
          direction="y")
    m.railing([(x0 + 0.15, y0 + 0.15), (x1 - 0.15, y0 + 0.15),
               (x1 - 0.15, y1 - 0.15), (x0 + 0.15, y1 - 0.15),
               (x0 + 0.15, y0 + 0.15)], h=1.05, at=(0, 0, zr), post_step=2.0,
              mat="Kit_Rust")

    # 外挂楼梯（南面，避开烟囱基础）+ 落口补板
    _stairs2(m, at=(3.0, -4.85, 0.0), h=zl1, run=3.45, w=1.0, mat="Kit_RustDark")
    _grat(m, (1.2, 0.45), at=(2.4, y0 - 0.0, zl1 - 0.05), thick=0.05,
          mat="Kit_RustDark", direction="x")

    # 墙面板残骸（西面）+ 一块歪挂的
    m.box((0.05, 3.2, 2.4), at=(x0 - 0.06, 1.4, zl2 + 0.3), mat="Kit_RustDark")
    m.box((0.05, 2.6, 2.2), at=(x0 - 0.22, 4.6, zl2 + 0.2), ry=R(14.0),
          mat="Kit_Rust")

    # 立管 conduit（东侧柱翼缘）+ 分支
    m.pipe_member((x1 + 0.06, 0.6, 0.6), (x1 + 0.06, 0.6, 13.4), r=0.045, mat="Kit_Iron")
    for zk in (2.0, 5.0, 8.0, 11.0):
        m.pipe_member((x1 + 0.06, 0.6, zk), (x1 - 0.1, 0.6, zk), r=0.04,
                      mat="Kit_Iron")
    m.box((0.2, 0.16, 0.3), at=(x1 - 0.02, 0.52, 12.9), mat="Kit_Iron")


# ---------------------------------------------------------------------------
# 天桥（T-101 北侧 → 框架塔一层）
# ---------------------------------------------------------------------------

def _bridge(m):
    zb = 4.8
    yb_a, yb_b = 6.14, 6.86
    # B 段：东西向（x -6.85 → 1.2）
    for yy in (yb_a, yb_b):
        m.ibeam((-6.85, yy, zb - 0.18), (1.2, yy, zb - 0.18), h=0.24, w=0.14,
                mat="Kit_SteelBlue")
    _grat(m, (8.05, 0.85), at=(-2.825, 6.5, zb - 0.05), thick=0.05,
          mat="Kit_RustDark", direction="x")
    # A 段：南北向（接 T-101 壳体北面）
    _grat(m, (0.85, 2.35), at=(-6.425, 5.475, zb - 0.05), thick=0.05,
          mat="Kit_RustDark", direction="y")
    for xx in (-6.68, -6.16):
        m.member((xx, 5.5, 0.0), (xx, 5.5, zb - 0.2), 0.14, 0.14, mat="Kit_SteelBlue")
        m.box((0.5, 0.5, 0.35), at=(xx, 5.5, 0.0), mat="Kit_ConcreteDark")
    for xx in (-4.0, -1.6):
        for yy in (yb_a, yb_b):
            m.member((xx, yy, 0.0), (xx, yy, zb - 0.2), 0.14, 0.14,
                     mat="Kit_SteelBlue")
            m.box((0.5, 0.4, 0.35), at=(xx, min(yy, 6.8), 0.0),
                  mat="Kit_ConcreteDark")
        m.member((xx, yb_a, 0.55), (xx, yb_b, zb - 0.4), 0.08, 0.08,
                 mat="Kit_Rust")
        m.member((xx, yb_b, 0.55), (xx, yb_a, zb - 0.4), 0.08, 0.08,
                 mat="Kit_Rust")
    # 栏杆：北面整排；南面残缺（缺中段）
    m.railing([(-6.85, yb_b), (1.2, yb_b)], h=1.05, at=(0, 0, zb), post_step=1.7,
              mat="Kit_Rust")
    _broken_rail(m, [(-6.85, yb_a), (-5.6, yb_a), (-4.35, yb_a)], zb,
                 skip_spans=set(), mat="Kit_Rust")
    _broken_rail(m, [(-1.0, yb_a), (1.2, yb_a)], zb, skip_spans=set(),
                 mat="Kit_Rust")
    m.railing([(-6.85, 4.3), (-6.85, yb_a - 0.06)], h=1.05, at=(0, 0, zb),
              post_step=1.4, mat="Kit_Rust")
    m.railing([(-6.0, 4.3), (-6.0, yb_a - 0.06)], h=1.05, at=(0, 0, zb),
              post_step=1.6, mat="Kit_Rust")
    # 与 T-101 壳体的连接托架
    for xx in (-6.6, -6.15):
        m.member((xx, 4.02, zb - 0.45), (xx, 4.45, zb - 0.08), 0.08, 0.08,
                 mat="Kit_Rust")


# ---------------------------------------------------------------------------
# 地面散件
# ---------------------------------------------------------------------------

def _ground(m):
    cx, cy = T101

    # T-101 引出的两根地面工艺管（T 形支架）+ 阀 + 立式端法兰
    p1 = [(cx + 0.7, cy - 1.05, 0.55), (cx + 0.7, -0.4, 0.55),
          (-3.9, -0.4, 0.55), (-3.9, -6.55, 0.55)]
    p2 = [(-6.1, cy - 1.15, 0.42), (-6.1, -0.14, 0.42),
          (-4.16, -0.14, 0.42), (-4.16, -6.3, 0.42)]
    m.tube(p1, 0.12, seg=10, mat="Kit_SteelPale")
    m.tube(p2, 0.085, seg=8, mat="Kit_Rust")
    posts = ((cx + 0.7, 1.2, "y"), (cx + 0.7, -0.25, "y"), (-4.8, -0.4, "x"),
             (-3.9, -1.5, "y"), (-3.9, -3.3, "y"), (-3.9, -5.1, "y"))
    for (px_, py_, d) in posts:
        m.member((px_, py_, 0.0), (px_, py_, 0.36), 0.09, 0.09, mat="Kit_RustDark")
        if d == "y":
            m.box((0.62, 0.08, 0.05), at=(px_, py_, 0.33), mat="Kit_RustDark")
        else:
            m.box((0.08, 0.62, 0.05), at=(px_, py_, 0.33), mat="Kit_RustDark")
    _valve(m, (-3.9, -4.2, 0.55), "-y", 0.12)
    m.tube([(-3.9, -6.55, 0.55), (-3.9, -6.55, 1.5)], r=0.12, seg=10,
           mat="Kit_SteelPale")
    _flange(m, (-3.9, -6.55, 1.5), "+z", 0.12)
    m.tube([(-4.16, -6.3, 0.42), (-4.16, -6.3, 1.0)], r=0.085, seg=8,
           mat="Kit_Rust")
    _flange(m, (-4.16, -6.3, 1.0), "+z", 0.085)

    # T-102 → T-101 方向的跨接管理
    m.tube([(-5.35, -2.95, 1.25), (-5.35, 0.9, 1.25), (-4.0, 0.9, 1.25),
            (-3.9, 1.4, 1.25)], r=0.13, seg=10, mat="Kit_Rust")
    m.tube([(-3.9, 1.4, 1.25), (-3.9, 1.4, 2.1)], r=0.13, seg=10,
           mat="Kit_Rust")
    _flange(m, (-3.9, 1.4, 2.1), "+z", 0.13)
    m.member((-5.3, -0.6, 0.0), (-5.3, -0.6, 1.12), 0.1, 0.1, mat="Kit_RustDark")
    m.member((-4.5, 0.9, 0.0), (-4.5, 0.9, 1.12), 0.1, 0.1, mat="Kit_RustDark")

    # 两台反应器
    _reactor(m, R201_X)
    _reactor(m, R202_X, broken_top=True, valve_side=True)

    # 落地散管 + 支墩 + 端法兰
    m.tube([(0.6, -5.9, 0.24), (3.3, -5.75, 0.24)], r=0.14, seg=10, mat="Kit_Rust")
    _flange(m, (3.3, -5.75, 0.24), "+x", 0.14)
    for xx in (1.2, 2.6):
        m.box((0.35, 0.3, 0.24), at=(xx, -5.9, 0.0), mat="Kit_ConcreteDark")

    # 断掉的立管（扭折）
    m.tube([(2.6, -6.5, 0.0), (2.6, -6.5, 0.85), (3.15, -6.72, 1.12)],
           r=0.11, seg=8, mat="Kit_Rust")

    # 管料架（两根立柱 + 三根横管）
    m.member((0.0, -2.2, 0.0), (0.0, -2.2, 0.75), 0.1, 0.1, mat="Kit_RustDark")
    m.member((0.9, -2.2, 0.0), (0.9, -2.2, 0.75), 0.1, 0.1, mat="Kit_RustDark")
    m.member((0.0, -2.2, 0.5), (0.9, -2.2, 0.5), 0.07, 0.07, mat="Kit_RustDark")
    for zz in (0.62, 0.5):
        m.tube([(-0.25, -2.2, zz), (1.15, -2.2, zz)], r=0.09, seg=8, mat="Kit_Rust")

    # 油渍 / 锈渍
    for (sx_, sy_, rr) in ((-2.0, -4.5, 0.9), (2.2, -6.0, 0.6), (0.8, 1.2, 0.7),
                           (-6.5, -1.0, 0.5), (7.8, 0.8, 0.8), (-0.5, 5.2, 0.55)):
        m.cyl(rr, 0.014, at=(sx_, sy_, 0.002), seg=14,
              mat="Kit_RustDark", smooth=False)

    # 斜靠在反应器基础墩上的锈板
    m.box((0.9, 0.05, 0.8), at=(-3.58, 1.0, 0.0), ry=R(26.0), mat="Kit_Rust")
    m.box((1.2, 0.05, 0.9), at=(-1.9, -1.3, -0.02), rz=R(18.0), mat="Kit_RustDark")


# ---------------------------------------------------------------------------

def build(m):
    """主装置区分件入口：preview_module.py / assemble 脚本调用。"""
    _t101(m)
    _t102(m)
    _stack(m)
    _frame(m)
    _bridge(m)
    _ground(m)
