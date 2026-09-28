# -*- coding: utf-8 -*-
"""chemplant/mod_site.py —— 场地分件（全场的底子）：地坪 / 道路 / 围墙大门 / 荒草 / 排水。

本地系 = 场地系（AREAS['site']：X[-28,28] Y[-20,20]，原点场地中心，+X 东 +Y 北 +Z 上）。
towers / tanks / pipes / building / props 五个分件都摆在本件之上，硬边界见 kit_common.AREAS。

【分层（自下而上）】
    1. 暗色垫层（Kit_ConcreteDark）：从一切板缝 / 坑洞 / 沟槽 / 路面里透出来的"底"。
    2. 混凝土板（~6 m 分格，0.03 实缝，缝里露出垫层 = 板缝）：主体 Kit_ConcreteMid，
       混入 Kit_ConcreteLight/Dark 模拟新旧浇筑；道路 / 支路 / 明沟整行开洞。
    3. 道路：主路 + 支路路面 = 垫层露头（比板低 ~2.2 cm，沥青感），
       路缘石（Kit_ConcreteLight，带断口）+ 褪色标线 + 车辙 + 坑洞 + 裂缝。
    4. 围墙：混凝土柱 @6 m + 锈铁栅（竖杆 @0.15 + 三道横杆）；2 处倒塌 + 1 处缺口。
    5. 西大门：门柱 + 半开推拉门（一角塌陷）+ 门卫室（单坡顶 1 门 1 窗）+ 断档杆 + 减速带。
    6. 排水：主路北侧排水明沟（低矮沟墙 + 格栅盖板 + 淤泥）+ 井盖 6 个。
    7. 荒草：~240 簇尖叶（只撒在分区包围盒之外：墙根 / 板缝 / 路肩 / 沟里 / 倒墙下）。

【竖向口径（本件特批）】kit_common 规定地面 z=0，但自检器 preview_module 的灰地板
占 z[-0.02, 0] 整层：路面若按"板顶 0、路面 -0.02"建模，自检图里路面会被灰地板整个盖住。
故本件整体把"名义地面"抬到 Z_P=0.026（垫层顶 0.001），装配后与其他分件的最大偏差
≤2.6 cm（都从 z=0 长起的塔/罐/楼基部没入板面，不可感知）。

【面数纪律】地坪 = 大垫层 + ~80 块分格板（而非小块铺满）；荒草 = 少量成簇尖叶；
总量目标 40k~120k 三角面。零贴图零金属度，质感全部来自几何密度 + 调色板对比。
"""

import math
import random

import kit_common as K

TAU = math.tau

# ---- 本地系硬边界（AREAS['site']，原点 = 场地中心，地面 z=0）----
X0, X1 = -28.0, 28.0
Y0, Y1 = -20.0, 20.0

# ---- 关键场地坐标（对齐 kit_common.AREAS 俯视图）----
ROAD_Y0, ROAD_Y1 = K.ROAD_MAIN_Y            # -8 / -4：东西主路（4 m 宽，西端接大门）
SIDE_X0, SIDE_X1 = K.ROAD_SIDE_X            # -6.5 / -3.5：南北支路（3 m 宽）
SIDE_Y1 = 18.0                              # 支路北端
GATE_X = K.GATE_X                           # -28：西围墙线
GATE_Y0, GATE_Y1 = -9.0, -3.0               # 大门开口（罩住主路宽）
FX, FY = 27.8, 19.8                         # 围墙线（场地边内收 0.2）

# ---- 竖向口径（见模块 docstring）----
Z_U = 0.001                                 # 垫层顶：缝 / 沟底 / 路面露出的暗色"底"
Z_P = 0.026                                 # 混凝土板顶 = 名义地面
Z_R = 0.004                                 # 路面标高基准（比板低 ~2.2 cm）
T_P = 0.024                                 # 板厚

DITCH_YC = -3.45                            # 排水明沟中心线（主路北侧走道内）
DITCH_WY = (-3.72, -3.18)                   # 沟墙外沿线（南，北）
GUARD = (-25.3, -3.0, -22.3, -0.2)          # 门卫室占地 x0,y0,x1,y1
SEED = 20260929


# ---------------------------------------------------------------------------
# 小工具
# ---------------------------------------------------------------------------

def _plate(m, x0, x1, y0, y1, mat, dz=0.0):
    """一块混凝土地板：四周留 0.015 缝（缝里露出垫层）。"""
    g = 0.015
    m.box((x1 - x0 - 2 * g, y1 - y0 - 2 * g, T_P + dz),
          at=((x0 + x1) / 2.0, (y0 + y1) / 2.0, Z_U + 0.001), mat=mat)


def _chips(m, rng, cx, cy, n, spread, z, mats=None, smin=0.05, smax=0.16):
    """碎渣一撮（倒塌围墙 / 坑洞边）。"""
    mats = mats or ("Kit_ConcreteMid", "Kit_ConcreteLight", "Kit_RockMid")
    for _ in range(n):
        x = cx + rng.uniform(-spread, spread)
        y = cy + rng.uniform(-spread * 0.7, spread * 0.7)
        s = rng.uniform(smin, smax)
        m.box((s, s * rng.uniform(0.5, 1.0), s * rng.uniform(0.3, 0.7)),
              at=(x, y, z), rz=rng.uniform(0, TAU), mat=rng.choice(mats))


def _rock(m, rng, x, y, s):
    """碎石堆：两块乱向石块。"""
    for _ in range(2):
        m.box((s, s * 0.7, s * 0.5), at=(x + rng.uniform(-0.12, 0.12),
                                         y + rng.uniform(-0.12, 0.12), Z_P - 0.01),
              rz=rng.uniform(0, 3.0), rx=rng.uniform(-0.15, 0.15),
              mat=rng.choice(("Kit_RockMid", "Kit_RockDark")))


def _dash_line(m, rng, p0, p1, w, dash, gap, z, h, mat, skip=0.18):
    """断续标线（褪色 = 随机缺段）。"""
    x0, y0 = p0
    x1, y1 = p1
    length = math.hypot(x1 - x0, y1 - y0)
    if length < 1e-6:
        return
    ux, uy = (x1 - x0) / length, (y1 - y0) / length
    rz = math.atan2(uy, ux)
    s = rng.uniform(0.0, gap)
    while s + dash < length:
        if rng.random() >= skip:
            c = s + dash / 2.0
            m.box((dash, w, h), at=(x0 + ux * c, y0 + uy * c, z), rz=rz, mat=mat)
        s += dash + gap


def _strip(m, rng, p0, p1, w, z, h, mat):
    """一段实线。"""
    x0, y0 = p0
    x1, y1 = p1
    length = math.hypot(x1 - x0, y1 - y0)
    if length < 1e-6:
        return
    m.box((length, w, h), at=((x0 + x1) / 2.0, (y0 + y1) / 2.0, z),
          rz=math.atan2(y1 - y0, x1 - x0), mat=mat)


def _crack(m, rng, pts, w, z, mat):
    """折线裂缝：短薄板条接成。"""
    for i in range(len(pts) - 1):
        (xa, ya), (xb, yb) = pts[i], pts[i + 1]
        seg = math.hypot(xb - xa, yb - ya)
        m.box((seg + 0.04, w * rng.uniform(0.7, 1.3), 0.005),
              at=((xa + xb) / 2.0, (ya + yb) / 2.0, z),
              rz=math.atan2(yb - ya, xb - xa), mat=mat)


def _blotch(m, rng, cx, cy, r, z, mat):
    """不规则污渍/坑洞面片（星形多边形挤出）。"""
    n = 7
    pts = []
    for i in range(n):
        a = TAU * i / n + rng.uniform(-0.22, 0.22)
        rr = r * rng.uniform(0.6, 1.0)
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr * 0.8))
    m.poly_extrude(pts, z, z + 0.005, mat=mat)


def _pothole(m, rng, cx, cy, r, z, hole_mat, pit_mat=None, chips=True):
    """坑洞：深色不规则面片 +（可选）内芯泥坑 + 边缘碎渣。"""
    _blotch(m, rng, cx, cy, r, z, hole_mat)
    if pit_mat:
        _blotch(m, rng, cx, cy, r * 0.55, z + 0.002, pit_mat)
    if chips:
        _chips(m, rng, cx, cy, max(4, int(r * 8)), r * 1.25, z + 0.004,
               smin=0.04, smax=0.13)


def _striped_arm(m, a, b, nseg, w, t):
    """黄白相间的档杆（断杆用）。"""
    ax, ay, az = a
    bx, by, bz = b
    for i in range(nseg):
        t0, t1 = i / float(nseg), (i + 1) / float(nseg)
        p0 = (ax + (bx - ax) * t0, ay + (by - ay) * t0, az + (bz - az) * t0)
        p1 = (ax + (bx - ax) * t1, ay + (by - ay) * t1, az + (bz - az) * t1)
        m.member(p0, p1, w, t,
                 mat="Kit_PaintYellow" if i % 2 == 0 else "Kit_ConcreteLight")


# ---------------------------------------------------------------------------
# 1. 地坪：垫层 + 分格板 + 修补 + 板面破损
# ---------------------------------------------------------------------------

def _ground(m, rng):
    # 暗色垫层：板缝 / 沟槽 / 坑洞 / 路面的"底"
    m.box((X1 - X0, Y1 - Y0, 0.15), at=(0.0, 0.0, Z_U - 0.149), mat="Kit_ConcreteDark")

    xb = [-28.0, -24.0, -18.0, -12.0, -6.5, -3.5, 0.0, 6.0, 12.0, 18.0, 24.0, 28.0]
    yb = [-20.0, -18.0, -12.0, -8.0, -4.0, 0.0, 6.0, 12.0, 18.0, 20.0]

    def pick_mat():
        r = rng.random()
        if r < 0.72:
            return "Kit_ConcreteMid"
        return "Kit_ConcreteLight" if r < 0.87 else "Kit_ConcreteDark"

    # 指定的"新旧浇筑"色差块（保证在开阔区看得见）
    force = {(7, 5): "Kit_ConcreteLight",    # 东带 X[12,18] Y[0,6]
             (2, 2): "Kit_ConcreteDark",     # 南院 X[-18,-12] Y[-12,-8]
             (0, 1): "Kit_ConcreteLight",    # 西南角 X[-28,-24] Y[-18,-12]
             (5, 6): "Kit_ConcreteDark",     # 支路东走廊 X[-3.5,0] Y[6,12]
             (9, 2): "Kit_ConcreteLight",    # 东南 X[24,28] Y[-12,-8]
             (1, 7): "Kit_ConcreteDark"}     # 西北 X[-24,-18] Y[12,18]
    raise_cells = set()
    for _ in range(6):
        raise_cells.add((rng.randrange(0, 11), rng.randrange(0, 9)))

    for i in range(len(xb) - 1):
        x0, x1 = xb[i], xb[i + 1]
        for j in range(len(yb) - 1):
            y0, y1 = yb[j], yb[j + 1]
            if j == 3:
                continue                     # 主路 Y[-8,-4] 整行开洞 = 路面
            if j == 4:
                _band_row(m, rng, x0, x1)    # Y[-4,0] 行：明沟开槽，单独处理
                continue
            if i == 4 and j in (5, 6, 7):
                continue                     # 支路 X[-6.5,-3.5] 开洞（Y -4..18）
            mat = force.get((i, j), pick_mat())
            dz = 0.012 if (i, j) in raise_cells else 0.0   # 错台
            _plate(m, x0, x1, y0, y1, mat, dz)

    _ground_damage(m, rng)


def _band_row(m, rng, x0, x1):
    """Y(-4,0) 行：主路北侧走道。排水明沟 Y[-3.72,-3.18] 开槽；门卫室跨不挖沟。
    沟分段：[-27.6,-25.7]（门卫室西）+ [-22.0,-7.0] / [-3.0,6.4]（支路口留涵洞口）。"""
    ys0, ys1 = -4.0, DITCH_WY[0]             # 沟南窄条
    yn0, yn1 = DITCH_WY[1], 0.0              # 沟北宽条
    segs = [(-28.0, -27.6, "full"), (-27.6, -25.7, "ditch"), (-25.7, -25.3, "full"),
            (-25.3, -22.3, "house"), (-22.3, -22.0, "full"), (-22.0, -7.0, "ditch"),
            (-7.0, -6.5, "full"), (-6.5, -3.5, "road"), (-3.5, -3.0, "full"),
            (-3.0, 6.4, "ditch"), (6.4, 12.0, "full")]
    for (a, b, kind) in segs:
        if b <= x0 + 1e-6 or a >= x1 - 1e-6:
            continue
        r = rng.random()
        mat = "Kit_ConcreteMid" if r < 0.75 else ("Kit_ConcreteLight" if r < 0.9
                                                  else "Kit_ConcreteDark")
        if kind == "full":
            _plate(m, a, b, -4.0, 0.0, mat)
        elif kind == "road":
            continue                          # 支路口：路面
        else:                                  # ditch / house：南北两条板
            _plate(m, a, b, ys0, ys1, mat)
            _plate(m, a, b, yn0, yn1, mat)


def _ground_damage(m, rng):
    """板面：修补补丁 + 裂缝 + 坑洞 + 油污。"""
    z = Z_P
    # 修补补丁（新浇筑 / 沥青补块）
    for (cx, cy, w, d, mat) in [(8.0, -1.2, 2.2, 1.4, "Kit_ConcreteLight"),
                                (-13.0, -1.8, 1.8, 1.2, "Kit_ConcreteDark"),
                                (-5.0, -16.5, 2.4, 1.6, "Kit_ConcreteLight"),
                                (26.0, -12.0, 1.6, 1.2, "Kit_ConcreteDark"),
                                (-19.0, -10.5, 1.6, 1.1, "Kit_ConcreteLight")]:
        m.box((w, d, 0.018), at=(cx, cy, z), mat=mat)
    # 板面裂缝（折线）
    _crack(m, rng, [(12.0, -1.5), (14.5, -0.8), (16.0, -1.9), (18.5, -1.4)], 0.04, z,
           "Kit_ConcreteDark")
    _crack(m, rng, [(-10.0, -1.2), (-12.0, -2.2), (-14.5, -1.6)], 0.035, z,
           "Kit_ConcreteDark")
    _crack(m, rng, [(-16.0, -11.0), (-14.0, -12.5), (-12.2, -12.1)], 0.04, z,
           "Kit_ConcreteDark")
    _crack(m, rng, [(-4.0, -16.0), (-6.0, -17.2), (-8.5, -16.8)], 0.035, z,
           "Kit_ConcreteDark")
    _crack(m, rng, [(-20.5, -1.0), (-22.0, -0.3)], 0.03, z, "Kit_ConcreteDark")
    # 板面坑洼（暗色凹陷错觉）+ 油污
    _pothole(m, rng, -10.5, -12.3, 0.7, z, "Kit_ConcreteDark", "Kit_RockDark")
    _pothole(m, rng, 12.0, -2.6, 0.5, z, "Kit_ConcreteDark", None)
    _blotch(m, rng, -9.0, -10.5, 0.6, z + 0.001, "Kit_RockDark")
    _blotch(m, rng, -15.0, -15.5, 0.8, z + 0.001, "Kit_RockDark")
    _blotch(m, rng, 22.0, -2.0, 0.5, z + 0.001, "Kit_RockDark")


# ---------------------------------------------------------------------------
# 2. 道路：路缘石 + 标线 + 车辙 + 坑洞（路面 = 垫层露头，见 _ground 开洞）
# ---------------------------------------------------------------------------

def _roads(m, rng):
    z = Z_R
    # 路缘石：0.15 宽 × 顶面高出板面 0.13，主路两沿 + 支路两沿，带断口
    curb_top = Z_P + 0.13
    curb_h = curb_top - (-0.02)

    def curb(x0, x1, ycen):
        m.box((x1 - x0, 0.15, curb_h), at=((x0 + x1) / 2.0, ycen, -0.02),
              mat="Kit_ConcreteLight")

    for (a, b) in [(-27.9, -10.2), (-9.6, 11.4), (12.0, 27.9)]:
        curb(a, b, ROAD_Y0 - 0.075)           # 主路南沿 Y=-8
    for (a, b) in [(-27.9, -6.8), (-3.2, 14.1), (14.8, 27.9)]:
        curb(a, b, ROAD_Y1 + 0.075)           # 主路北沿 Y=-4（避开支路口）
    for (a, b) in [(-7.9, 1.4), (2.1, 17.9)]:   # 支路西沿 X=-6.5
        m.box((0.15, b - a, curb_h), at=(SIDE_X0 - 0.075, (a + b) / 2.0, -0.02),
              mat="Kit_ConcreteLight")
    for (a, b) in [(-7.9, -2.2), (-1.5, 17.9)]:  # 支路东沿 X=-3.5
        m.box((0.15, b - a, curb_h), at=(SIDE_X1 + 0.075, (a + b) / 2.0, -0.02),
              mat="Kit_ConcreteLight")
    # 断掉的一截路缘（倒在断口边）
    m.box((0.9, 0.15, 0.14), at=(-9.9, ROAD_Y0 - 0.1, Z_P), rz=0.4,
          mat="Kit_ConcreteLight")

    # 褪色标线：主路中线黄虚线 + 两沿白实线（断续）+ 大门停车线
    _dash_line(m, rng, (-26.5, -6.0), (27.0, -6.0), 0.12, 2.2, 2.8, z, 0.007,
               "Kit_PaintYellow", skip=0.25)
    for (a, b) in [(-27.5, -14.0), (-12.0, 4.0), (8.0, 27.5)]:
        _strip(m, rng, (a, -7.7), (b, -7.7), 0.12, z, 0.006, "Kit_ConcreteLight")
    for (a, b) in [(-27.5, -7.0), (-3.0, 9.0), (11.0, 27.5)]:
        _strip(m, rng, (a, -4.3), (b, -4.3), 0.12, z, 0.006, "Kit_ConcreteLight")
    m.box((0.35, 3.4, 0.007), at=(-26.2, -6.0, z), mat="Kit_PaintYellow")
    # 支路中线白虚线
    _dash_line(m, rng, (-5.0, -6.5), (-5.0, 16.5), 0.10, 1.8, 2.4, z, 0.006,
               "Kit_ConcreteLight", skip=0.2)

    # 车辙痕（窄深色板条，双向各一，磨损断续）
    for yy in (-7.15, -4.85):
        for (a, b) in [(-24.0, -18.0), (-15.0, -6.0), (2.0, 9.0), (12.0, 20.0)]:
            m.box((b - a, 0.3, 0.005), at=((a + b) / 2.0, yy + rng.uniform(-0.06, 0.06),
                                           z), rz=rng.uniform(-0.012, 0.012),
                  mat="Kit_RockDark")

    # 路面坑洞（2 处，一处积水）+ 路面裂缝 + 油污
    _pothole(m, rng, 6.5, -6.6, 0.85, z, "Kit_RockDark", "Kit_WetSand")
    _pothole(m, rng, -13.5, -5.2, 0.55, z, "Kit_RockDark", None)
    _crack(m, rng, [(2.2, -5.2), (4.5, -6.3), (7.2, -5.7)], 0.04, z, "Kit_RockDark")
    _crack(m, rng, [(-16.0, -6.7), (-14.2, -5.6), (-12.4, -6.1)], 0.035, z,
           "Kit_RockDark")
    for (cx, cy, r) in [(14.0, -6.8, 0.7), (-20.0, -5.6, 0.5), (-6.0, -4.6, 0.4),
                        (8.0, -5.4, 0.45)]:
        _blotch(m, rng, cx, cy, r, z + 0.001, "Kit_RockDark")


# ---------------------------------------------------------------------------
# 3. 围墙：混凝土柱 @6 m + 铁栅板（含倒塌 / 缺口）
# ---------------------------------------------------------------------------

def _fence_post(m, x, y, tilt=0.0, nx=0.0, ny=0.0, h=2.2):
    m.box((0.4, 0.4, h), at=(x, y, -0.02), rx=-tilt * ny, ry=tilt * nx,
          mat="Kit_ConcreteMid")
    if abs(tilt) < 0.05:
        m.box((0.46, 0.46, 0.06), at=(x, y, h - 0.02), mat="Kit_ConcreteLight")


def _fence_panel(m, p0, p1, nx, ny, tilt=0.0, bars=True, mat="Kit_Rust",
                 z_bot=0.22, h=1.9, bar_step=0.15):
    """柱间一张铁栅：三道横杆 + 竖杆 @0.15。tilt = 绕底部杆线向内倒的角（0=立，~1.5=躺平）。"""
    (x0, y0), (x1, y1) = p0, p1
    length = math.hypot(x1 - x0, y1 - y0)
    if length < 0.2:
        return
    ux, uy = (x1 - x0) / length, (y1 - y0) / length
    tt = min(1.0, abs(tilt) / 1.2)
    zb = z_bot * (1.0 - tt) + 0.07 * tt       # 倒得越平越贴地
    st, ct = math.sin(tilt), math.cos(tilt)

    def p(s, dz):
        return (x0 + ux * s + nx * st * dz, y0 + uy * s + ny * st * dz, zb + ct * dz)

    for dz in (0.02, h * 0.5, h - 0.03):
        m.member(p(0.06, dz), p(length - 0.06, dz), 0.06, 0.05, mat=mat)
    if bars:
        n = max(2, int(length / bar_step))
        for i in range(1, n):
            s = length * i / n
            m.member(p(s, 0.02), p(s, h - 0.04), 0.035, 0.035, mat=mat)


def _fence(m, rng):
    lean1, lean2, lean3 = 0.2, 0.38, 0.55
    # （边, 内倒法线, 柱位, 各跨状态）——倒塌段的"倒伏走廊"避开了各区包围盒主体
    sides = [
        ("N", (0, -1),
         [(27.8, 19.8), (21.8, 19.8), (15.8, 19.8), (9.8, 19.8), (3.8, 19.8),
          (-2.2, 19.8), (-8.2, 19.8), (-14.2, 19.8), (-20.2, 19.8), (-26.2, 19.8),
          (-27.8, 19.8)],
         ["up", "up", "lean2", "up", "lean3", "down", "up", "up", "lean1", "up"]),
        ("S", (0, 1),
         [(27.8, -19.8), (21.8, -19.8), (15.8, -19.8), (9.8, -19.8), (3.8, -19.8),
          (-2.2, -19.8), (-8.2, -19.8), (-14.2, -19.8), (-20.2, -19.8), (-26.2, -19.8),
          (-27.8, -19.8)],
         ["up", "up", "up", "up", "down", "lean2", "gap", "lean1", "up", "up"]),
        ("E", (-1, 0),
         [(27.8, 19.8), (27.8, 13.8), (27.8, 7.8), (27.8, 1.8), (27.8, -4.2),
          (27.8, -10.2), (27.8, -16.2), (27.8, -19.8)],
         ["up", "lean1", "ruin", "down", "up", "lean2", "up"]),
        ("W", (1, 0),
         [(-27.8, 19.8), (-27.8, 13.8), (-27.8, 7.8), (-27.8, 1.8), (-27.8, -3.0),
          (-27.8, -9.0), (-27.8, -15.0), (-27.8, -19.8)],
         ["up", "lean1", "up", "up", None, "up", "lean2"]),
    ]
    lean_v = {"lean1": lean1, "lean2": lean2, "lean3": lean3}
    for (name, (nx, ny), posts, states) in sides:
        for k in range(len(posts) - 1):
            st = states[k]
            if st is None:
                continue                       # 大门口
            p0, p1 = posts[k], posts[k + 1]
            # 柱：倒塌 / 残破跨端柱向内歪
            tilt = (lean_v.get(st, 0.0) * 0.55) if st in ("down", "ruin") else 0.0
            if st == "gap":
                tilt = 0.12
            if k == 0 or states[k - 1] is None:
                _fence_post(m, p0[0], p0[1], tilt, nx, ny)
            _fence_post(m, p1[0], p1[1], tilt, nx, ny)
            if st == "gap":
                continue                       # 整跨消失：缺口可通行
            mat = "Kit_RustDark" if (k + hash(name)) % 4 == 0 else "Kit_Rust"
            if st == "ruin":
                # 只剩下轨 + 几根乱倒的杆
                _fence_panel(m, p0, p1, nx, ny, tilt=0.0, bars=False, mat=mat)
                for _ in range(7):
                    s = rng.uniform(0.3, math.hypot(p1[0] - p0[0], p1[1] - p0[1]) - 0.3)
                    bx = p0[0] + (p1[0] - p0[0]) * s / max(0.01, math.hypot(
                        p1[0] - p0[0], p1[1] - p0[1]))
                    by = p0[1] + (p1[1] - p0[1]) * s / max(0.01, math.hypot(
                        p1[0] - p0[0], p1[1] - p0[1]))
                    t = rng.uniform(0.9, 1.55)
                    hh = rng.uniform(0.9, 1.9)
                    m.member((bx, by, 0.1),
                             (bx + nx * math.sin(t) * hh, by + ny * math.sin(t) * hh,
                              0.1 + math.cos(t) * hh), 0.035, 0.035, mat=mat)
                _chips(m, rng, (p0[0] + p1[0]) / 2.0 + nx * 0.8,
                       (p0[1] + p1[1]) / 2.0 + ny * 0.8, 6, 1.2, Z_P - 0.01)
            else:
                _fence_panel(m, p0, p1, nx, ny, tilt=lean_v.get(st, 0.0), mat=mat)
            if st == "down":
                # 倒伏带：碎渣 + 石堆
                cx = (p0[0] + p1[0]) / 2.0 + nx * 1.0
                cy = (p0[1] + p1[1]) / 2.0 + ny * 1.0
                _chips(m, rng, cx, cy, 10, 1.6, Z_P - 0.01)
                _rock(m, rng, cx + rng.uniform(-0.8, 0.8),
                      cy + rng.uniform(-0.5, 0.5), rng.uniform(0.3, 0.5))
        # 断柱（南缺口边：只剩半截）
        if name == "S":
            m.box((0.4, 0.4, 0.95), at=(-14.2, -19.8, -0.02), mat="Kit_ConcreteMid")
            m.box((0.35, 0.3, 0.25), at=(-13.6, -19.4, Z_P - 0.01), rz=0.7,
                  mat="Kit_ConcreteMid")


# ---------------------------------------------------------------------------
# 4. 西大门：门柱 + 半开推拉门（一角塌陷）+ 门卫室 + 断档杆 + 减速带
# ---------------------------------------------------------------------------

def _gate_area(m, rng):
    gx = GATE_X
    # —— 门柱（0.6×0.6×3.0 + 柱头灯）——
    for gy in (GATE_Y0, GATE_Y1):
        m.box((0.6, 0.6, 3.0), at=(gx, gy, -0.02), mat="Kit_ConcreteMid")
        m.box((0.72, 0.72, 0.12), at=(gx, gy, 2.98), mat="Kit_ConcreteLight")
        m.cyl(0.05, 0.5, at=(gx, gy, 3.1), seg=8, mat="Kit_Rust")
        m.box((0.2, 0.2, 0.24), at=(gx, gy, 3.6), mat="Kit_GlassDark")
    # 门柱锈渍
    for gy in (GATE_Y0, GATE_Y1):
        m.box((0.03, 0.5, 1.4), at=(gx - 0.315, gy - 0.1, 0.8), mat="Kit_RustDark")

    # —— 推拉门（半开：门扇占 Y[-8.8,-5.6]，开口东半可过；东北角塌到地）——
    m.box((0.26, 6.2, 0.05), at=(gx - 0.13, -6.0, Z_R - 0.004), mat="Kit_RustDark")
    y0, y1 = -8.8, -5.6
    top, bot = 2.42, 0.30
    for yy in (y0 + 0.05, y1 - 0.05, -7.6, -6.7):
        m.member((gx, yy, bot), (gx, yy, top), 0.07, 0.07, mat="Kit_Rust")
    m.member((gx, y0, top), (gx, y1, top - 0.12), 0.08, 0.06, mat="Kit_Rust")
    m.member((gx, y0, bot), (gx, -6.55, bot), 0.08, 0.06, mat="Kit_Rust")
    m.member((gx, -6.55, bot), (gx, y1 - 0.05, 0.06), 0.07, 0.05, mat="Kit_RustDark")
    n = int((y1 - y0) / 0.15)
    for i in range(1, n):
        yy = y0 + (y1 - y0) * i / n
        if yy > -6.55:
            t = (yy + 6.55) / ((y1 - 0.05) + 6.55)
            bz = bot + (0.06 - bot) * t
            tz = (top - 0.12) - 0.5 * t * t
        else:
            bz, tz = bot, top
        m.member((gx, yy, bz), (gx, yy, tz), 0.035, 0.035, mat="Kit_Rust")
    # 门扇滚轮 ×2
    for wy in (-8.3, -7.0):
        m.cyl(0.09, 0.06, at=(gx, wy, 0.11), ry=math.pi / 2, seg=10, mat="Kit_RustDark")

    _guard_house(m, rng)

    # —— 断档杆（立柱 + 半截翘起的横杆 + 落地后半段）——
    px, py = -26.3, -2.6
    m.box((0.4, 0.4, 0.12), at=(px, py, Z_P - 0.02), mat="Kit_ConcreteMid")
    m.box((0.22, 0.22, 1.1), at=(px, py, Z_P + 0.06), mat="Kit_SteelPale")
    _striped_arm(m, (px, py, Z_P + 1.05), (-26.65, -4.35, Z_P + 1.5), 3, 0.09, 0.06)
    _striped_arm(m, (-26.9, -4.9, Z_P + 0.05), (-25.6, -7.6, Z_P + 0.05), 4, 0.08, 0.05)

    # —— 减速带（三段磨损的黄黑（黄/灰）带）——
    for (a, b) in [(-8.0, -6.6), (-6.2, -5.0), (-4.6, -4.0)]:
        m.box((0.35, b - a, 0.055), at=(-21.5, (a + b) / 2.0, Z_R - 0.004),
              mat="Kit_PaintYellow")


def _sloped_wall(m, cx, y0, y1, t, h_s, h_n, mat):
    """沿 Y 走的墙，南高北低（单坡侧墙）。"""
    v = [(cx - t / 2, y0, 0.0), (cx + t / 2, y0, 0.0), (cx + t / 2, y1, 0.0),
         (cx - t / 2, y1, 0.0), (cx - t / 2, y0, h_s), (cx + t / 2, y0, h_s),
         (cx + t / 2, y1, h_n), (cx - t / 2, y1, h_n)]
    f = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6),
         (3, 0, 4, 7)]
    m.add(v, f, mat=mat)


def _guard_house(m, rng):
    """门卫室：3×2.8×2.8 单坡顶（南高北低），1 门 1 窗，淡混凝土墙 + 深顶。"""
    x0, y0, x1, y1 = GUARD
    h_s, h_n, t = 2.8, 2.45, 0.15
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    # 散水勒脚
    m.box((x1 - x0 + 0.2, y1 - y0 + 0.2, 0.09), at=(cx, cy, Z_P - 0.04),
          mat="Kit_ConcreteDark")
    zb = Z_P + 0.05
    # 北墙（整）+ 东西坡墙
    m.box((x1 - x0, t, h_n), at=(cx, y1 - t / 2, zb), mat="Kit_ConcreteLight")
    _sloped_wall(m, x0 + t / 2, y0, y1, t, h_s - zb, h_n - zb, "Kit_ConcreteLight")
    _sloped_wall(m, x1 - t / 2, y0, y1, t, h_s - zb, h_n - zb, "Kit_ConcreteLight")
    # 南墙（开 1 门 1 窗）：底带 / 顶带 / 门右墩 / 窗上带 / 窗右墩
    yw = y0 + t / 2
    m.box((x1 - x0, t, 1.0), at=(cx, yw, zb), mat="Kit_ConcreteLight")
    m.box((x1 - x0, t, h_s - zb - 2.05), at=(cx, yw, zb + 2.05),
          mat="Kit_ConcreteLight")
    m.box((0.7, t, 1.05), at=(x0 + 0.35, yw, zb + 1.0), mat="Kit_ConcreteLight")
    m.box((0.3, t, 1.05), at=(-23.55, yw, zb + 1.0), mat="Kit_ConcreteLight")
    m.box((0.8, t, 0.2), at=(-23.0, yw, zb + 1.85), mat="Kit_ConcreteLight")
    m.box((0.3, t, 1.05), at=(x1 - 0.15, yw, zb + 1.0), mat="Kit_ConcreteLight")
    # 门（锈铁门，退墙半砖）+ 把手 + 门口台阶
    m.box((0.84, 0.05, 2.0), at=(-24.15, y0 + 0.06, zb + 0.02), mat="Kit_Rust")
    m.box((0.05, 0.04, 0.16), at=(-23.85, y0 + 0.02, zb + 1.0), mat="Kit_SteelPale")
    m.box((1.0, 0.32, 0.1), at=(-24.15, y0 - 0.14, Z_P), mat="Kit_ConcreteMid")
    # 窗（玻璃 + 锈窗棂 + 挑窗台）
    m.box((0.74, 0.04, 0.8), at=(-23.0, y0 + 0.07, zb + 1.03), mat="Kit_GlassDark")
    for wx in (-23.3, -22.7):
        m.member((wx, y0 + 0.06, zb + 1.05), (wx, y0 + 0.06, zb + 1.8), 0.035, 0.035,
                 mat="Kit_Rust")
    m.box((0.9, 0.1, 0.05), at=(-23.0, y0 + 0.06, zb + 0.99), mat="Kit_ConcreteLight")
    # 单坡顶（南高北低，四面挑出）+ 北坡小通风管
    slope = math.atan2(h_s - h_n, y1 - y0)
    m.box((x1 - x0 + 0.5, y1 - y0 + 0.5, 0.1), at=(cx, cy, zb + 2.5), rx=-slope,
          mat="Kit_ConcreteDark")
    m.cyl(0.09, 0.5, at=(-24.5, -0.6, zb + 2.2), seg=8, mat="Kit_RustDark")
    # 北墙挂一台旧空调 + 落地线管
    m.box((0.34, 0.26, 0.5), at=(-24.4, y1 + 0.13, zb + 1.3), mat="Kit_SteelPale")
    m.tube([(-24.4, y1 + 0.03, zb + 1.0), (-24.4, y1 + 0.03, 0.5),
            (-24.4, y1 + 0.18, 0.05)], r=0.03, seg=6, mat="Kit_RustDark")


# ---------------------------------------------------------------------------
# 5. 排水：明沟 + 格栅盖板 + 端部集水井 + 井盖
# ---------------------------------------------------------------------------

def _grate(m, cx, cy, w=0.72, d=0.62, z=0.066, mat="Kit_Rust"):
    """沟渠格栅：边框 + 横箅条（坐在沟墙顶上）。"""
    m.box((w, 0.06, 0.05), at=(cx, cy - d / 2 + 0.03, z), mat=mat)
    m.box((w, 0.06, 0.05), at=(cx, cy + d / 2 - 0.03, z), mat=mat)
    m.box((0.06, d - 0.12, 0.05), at=(cx - w / 2 + 0.03, cy, z), mat=mat)
    m.box((0.06, d - 0.12, 0.05), at=(cx + w / 2 - 0.03, cy, z), mat=mat)
    n = 6
    for i in range(1, n):
        yy = cy - d / 2 + 0.06 + (d - 0.12) * i / n
        m.box((w - 0.12, 0.04, 0.035), at=(cx, yy, z + 0.02), mat=mat)


def _ditch(m, rng):
    """排水明沟：低矮沟墙夹 0.45 m 沟槽（槽底 = 垫层露头），格栅盖板若干 + 淤泥 + 沟内荒草。"""
    segs = [(-27.6, -25.7), (-22.0, -7.0), (-3.0, 6.4)]
    wall_h, wall_z = 0.156, -0.09
    for (a, b) in segs:
        cx = (a + b) / 2.0
        length = b - a
        m.box((length, 0.05, wall_h), at=(cx, DITCH_WY[0] + 0.025, wall_z),
              mat="Kit_ConcreteMid")
        m.box((length, 0.05, wall_h), at=(cx, DITCH_WY[1] - 0.025, wall_z),
              mat="Kit_ConcreteMid")
        # 端部封墙（集水井口）
        for e in (a, b):
            m.box((0.08, DITCH_WY[1] - DITCH_WY[0], wall_h), at=(e, DITCH_YC, wall_z),
                  mat="Kit_ConcreteMid")
    # 淤泥 / 泥浆（沟底亮暗斑）
    for sx in (-26.4, -19.8, -15.5, -9.2, -1.5, 3.8):
        m.box((1.4, 0.36, 0.014), at=(sx, DITCH_YC, Z_U + 0.001), mat="Kit_WetSand")
    # 格栅盖板（含门卫室前那段的连续盖板沟）
    for gxx in (-27.0, -26.0, -24.7, -23.9, -23.1, -20.5, -17.0, -13.5, -10.0, -7.6,
                -2.3, 1.2, 4.7, 6.1):
        _grate(m, gxx, DITCH_YC)
    # 井盖 6 个（路 / 支路 / 走道 / 南院）
    def manhole(cx, cy, z):
        m.cyl(0.42, 0.06, at=(cx, cy, z), seg=16, mat="Kit_ConcreteDark")
        m.cyl(0.34, 0.05, at=(cx, cy, z + 0.02), seg=16, mat="Kit_Rust")

    manhole(2.0, -6.0, Z_R)                   # 主路
    manhole(-5.0, 10.0, Z_R)                  # 支路
    manhole(10.0, -1.8, Z_P)                  # 东带
    manhole(18.0, -2.4, Z_P)                  # 东带
    manhole(-12.0, -14.0, Z_P)                # 南院
    manhole(-20.0, -11.0, Z_P)                # 南院


# ---------------------------------------------------------------------------
# 6. 荒草：尖叶片 + 成簇撒（全部在分区包围盒之外）
# ---------------------------------------------------------------------------

def _blade(m, x, y, h, w, rz, lean, mat):
    """一片带弯腰的尖叶：三环收分棱柱（底环/中环/顶环，环上 4 角），10 面无退化。"""
    t2 = w * 0.30
    rings = [(w / 2.0, t2, 0.0, 0.0),            # (半宽, 半厚, 弯腰量, z)
             (w * 0.30, t2 * 0.7, h * 0.10, h * 0.55),
             (w * 0.10, t2 * 0.35, h * 0.26, h)]
    v = []
    for (a, b, c, z) in rings:
        v += [(-a, c + b, z), (a, c + b, z), (a, c - b, z), (-a, c - b, z)]
    f = [(0, 1, 2, 3),                            # 底
         (0, 1, 5, 4), (4, 5, 9, 8),              # 前
         (1, 2, 6, 5), (5, 6, 10, 9),             # 右
         (3, 2, 6, 7), (6, 7, 11, 10),            # 后
         (0, 3, 7, 4), (4, 7, 11, 8),             # 左
         (8, 9, 10, 11)]                          # 顶
    m.add(v, f, at=(x, y, -0.012), rz=rz, rx=lean, mat=mat)


def _tuft(m, rng, x, y, h0, h1, s=1.0):
    """一簇荒草：中心直立几片 + 外圈向外散倒（外八字），枯绿混色。"""
    n = rng.randint(4, 7)
    for _ in range(n):
        a = rng.uniform(0, TAU)
        r = rng.uniform(0.02, 0.26) * s
        h = rng.uniform(h0, h1) * rng.uniform(0.75, 1.2)
        if rng.random() < 0.6:
            # 叶弯朝簇外（外八字）+ 少量乱向
            rz = a - math.pi / 2.0 + rng.uniform(-0.5, 0.5)
        else:
            rz = rng.uniform(0, TAU)
        lean = rng.uniform(-0.12, 0.12) + (r / 0.26) * rng.uniform(0.1, 0.45)
        _blade(m, x + math.cos(a) * r, y + math.sin(a) * r, h,
               rng.uniform(0.06, 0.11) * s, rz, lean * rng.choice((-1.0, 1.0)),
               rng.choice(("Kit_GrassMid", "Kit_GrassDark", "Kit_GrassDark")))


def _grass(m, rng):
    # （x0, y0, x1, y1, 簇数, 高下限, 高上限）——全部避开 towers/tanks/pipes/building 主体
    rects = [
        (-27.5, 19.15, 27.5, 19.75, 28, 0.35, 0.9),   # 北墙根
        (26.2, 5.3, 27.5, 18.9, 10, 0.3, 0.8),        # 东墙根北段
        (26.2, -18.9, 27.5, -4.6, 11, 0.3, 0.8),      # 东墙根南段
        (-27.5, -19.75, 1.7, -19.15, 12, 0.3, 0.7),   # 南墙根西段
        (24.3, -19.75, 27.5, -9.0, 7, 0.3, 0.8),      # 南墙根东段 + 楼东
        (-27.6, 4.7, -27.1, 18.9, 6, 0.3, 0.8),       # 西墙根北段
        (-27.6, -7.8, -27.1, -0.3, 5, 0.3, 0.6),      # 西墙根南段
        (-21.5, -3.08, 26.8, -0.35, 32, 0.3, 0.8),    # 主路北带（沟以北）
        (-27.6, -3.08, -25.6, -0.35, 6, 0.3, 0.6),    # 主路北带西端
        (-27.5, -8.9, 1.8, -8.15, 13, 0.3, 0.7),      # 主路南肩西段
        (24.2, -8.9, 27.5, -8.15, 5, 0.3, 0.7),       # 主路南肩东段
        (-7.2, -7.8, -6.6, -0.4, 6, 0.3, 0.7),        # 支路西肩南段
        (-7.2, 4.7, -6.6, 17.7, 7, 0.35, 0.85),       # 支路西肩北段
        (-3.4, -7.8, -2.8, -0.4, 6, 0.3, 0.7),        # 支路东肩南段
        (-3.4, 4.7, -2.8, 17.7, 7, 0.35, 0.85),       # 支路东肩北段
        (-3.3, 5.0, -0.35, 18.8, 20, 0.35, 0.9),      # 支路东—罐区西走廊
        (-6.9, 18.1, -0.4, 19.1, 5, 0.3, 0.8),        # 支路北端头
        (-26.0, -18.6, -1.0, -9.3, 26, 0.28, 0.55),   # 南院（矮草）
        (-27.0, -18.8, -25.6, -9.5, 6, 0.3, 0.6),     # 南院西缘
        (-25.9, -2.85, -25.35, -0.45, 2, 0.3, 0.5),   # 门卫室西侧
        (-22.25, -2.85, -21.75, -0.45, 2, 0.3, 0.5),  # 门卫室东侧
        (-8.0, 18.2, -2.4, 19.6, 6, 0.35, 0.9),       # 北倒墙内侧
        (-3.5, -19.4, 3.5, -18.0, 5, 0.3, 0.7),       # 南倒墙内侧
        (25.9, -4.0, 27.6, 1.5, 4, 0.3, 0.7),         # 东倒墙内侧
    ]
    for (x0, y0, x1, y1, n, h0, h1) in rects:
        for _ in range(n):
            _tuft(m, rng, rng.uniform(x0, x1), rng.uniform(y0, y1), h0, h1)
    # 沟里 / 沟沿（堵了的排水沟）
    for sx in (-26.4, -19.8, -15.2, -9.2, -1.5, 3.8, 5.6):
        _tuft(m, rng, sx + rng.uniform(-0.3, 0.3), DITCH_YC + rng.uniform(-0.08, 0.08),
              0.22, 0.42, s=0.8)
    for _ in range(6):
        _tuft(m, rng, rng.uniform(-26.0, 5.5), -2.95 + rng.uniform(-0.1, 0.1),
              0.3, 0.6)
    # 裂缝端头 / 坑边零星
    for (px, py) in [(18.5, -1.4), (-8.5, -16.8), (-13.5, -5.2), (-14.5, -1.6)]:
        _tuft(m, rng, px + rng.uniform(-0.2, 0.2), py + rng.uniform(-0.2, 0.2),
              0.3, 0.6)
    # 板缝交点零星（避开路 / 沟 / 房 / 各区，取 14 处）
    forb = [(-27.3, 4.7, -6.7, 19.3), (-0.3, 4.7, 26.3, 19.3), (-27.9, -0.5, 27.9, 4.8),
            (1.7, -19.3, 24.3, -7.7), (-27.9, -8.4, 27.9, -3.0),
            (-7.3, -8.4, -2.7, 18.3), (GUARD[0] - 0.3, GUARD[1] - 0.3, GUARD[2] + 0.3,
                                       GUARD[3] + 0.3),
            (-27.9, -9.3, -26.5, -2.7)]

    def open_xy(x, y):
        return not any(a < x < c and b < y < d for (a, b, c, d) in forb)

    jx = (-24.0, -18.0, -12.0, -6.5, -3.5, 0.0, 6.0, 12.0, 18.0, 24.0)
    jy = (-18.0, -12.0, 0.0, 6.0, 12.0, 18.0)
    got = 0
    for _ in range(40):
        if got >= 14:
            break
        x = rng.choice(jx) + rng.uniform(-0.4, 0.4)
        y = rng.choice(jy) + rng.uniform(-0.4, 0.4)
        if open_xy(x, y):
            _tuft(m, rng, x, y, 0.28, 0.6)
            got += 1


# ---------------------------------------------------------------------------
# 7. 其他：废弃停车位 + 隔离墩 / 车挡 + 石堆
# ---------------------------------------------------------------------------

def _misc(m, rng):
    # 废弃停车位白线（南院，褪色断续）
    for lx in (-7.0, -5.4, -3.8, -2.2, -0.6):
        _strip(m, rng, (lx, -13.2), (lx, -11.6), 0.1, Z_P, 0.005, "Kit_ConcreteLight")
        if rng.random() < 0.7:
            _strip(m, rng, (lx, -10.9), (lx, -9.0), 0.1, Z_P, 0.005,
                   "Kit_ConcreteLight")
    # 车挡（停车位的混凝土轮挡）
    for wx in (-6.2, -4.6, -3.0, -1.4):
        m.box((1.5, 0.25, 0.14), at=(wx, -13.55, Z_P - 0.02),
              rz=rng.uniform(-0.05, 0.05), mat="Kit_ConcreteLight")
    # 隔离墩（低矮混凝土墩，跨断口 / 护墙根）：cross-section 沿线挤出
    prof = [(-0.33, 0.0), (0.33, 0.0), (0.15, 0.55), (0.09, 0.68), (-0.09, 0.68),
            (-0.15, 0.55)]
    m.extrude_along(prof, (-26.3, -8.62, Z_P - 0.02), (-24.4, -8.78, Z_P - 0.02),
                    mat="Kit_ConcreteLight")
    m.extrude_along(prof, (24.4, -18.6, Z_P - 0.02), (26.2, -18.42, Z_P - 0.02),
                    mat="Kit_ConcreteLight")
    # 场角 / 倒墙碎石堆
    for (rx, ry, rs) in [(6.2, 18.6, 0.45), (-5.2, 18.9, 0.4), (26.6, -1.2, 0.5),
                         (-2.8, -18.7, 0.42), (22.0, 19.2, 0.38), (-25.9, 19.2, 0.45)]:
        _rock(m, rng, rx, ry, rs)
        _chips(m, rng, rx, ry, 5, 0.9, Z_P - 0.01)
    # 院里零散废渣堆（砖石/垃圾几处）
    for (dx, dy, dn) in [(-18.0, -11.0, 9), (0.5, -12.0, 8), (16.0, -2.0, 7),
                         (-9.0, -15.5, 8), (20.0, -1.0, 6), (-3.0, 8.5, 6)]:
        _chips(m, rng, dx, dy, dn, 1.1, Z_P - 0.005,
               mats=("Kit_ConcreteMid", "Kit_ConcreteLight", "Kit_RockMid",
                     "Kit_RockDark"))
        if rng.random() < 0.6:
            _rock(m, rng, dx + rng.uniform(-0.6, 0.6), dy + rng.uniform(-0.5, 0.5),
                  rng.uniform(0.25, 0.4))


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def build(m):
    """场地分件：地坪 / 道路 / 围墙大门 / 荒草 / 排水（本地系 = 场地系）。"""
    rng = random.Random(SEED)
    _ground(m, rng)       # 垫层 + 分格板 + 修补 + 板面破损
    _roads(m, rng)        # 路缘 / 标线 / 车辙 / 坑洞
    _fence(m, rng)        # 围墙 + 倒塌段 + 缺口
    _gate_area(m, rng)    # 大门 + 门卫室 + 档杆 + 减速带
    _ditch(m, rng)        # 明沟 + 格栅 + 井盖
    _grass(m, rng)        # 荒草簇
    _misc(m, rng)         # 停车位 + 隔离墩 + 石堆
