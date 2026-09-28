# -*- coding: utf-8 -*-
"""mod_tanks.py —— 罐区分件（立式储罐×4 / 球罐×2 / 双曲线冷却塔 / 围堰 / 泵棚 / 管汇）。

本地系：原点 = AREAS['tanks'] 盒中心地面投影，地面 z=0，+X 东 +Y 北 +Z 上；
盒 26×14×18，本件全部主体收在 X[-13,13] Y[-7,7] Z[0,15] 内。

布局（俯视，-Y 朝镜头）：
    · 围堰 + Ø7/Ø5.5 两座大立罐 —— 西半区（围堰南墙留一处缺口 + 钢板坡），
      Ø7 罐（前排）罐顶塌陷（扭曲薄板 + 撕裂罐沿 + 断管挂入）。
    · 双曲线冷却塔（底 Ø9 / 喉部 / 顶 Ø5 / 高 14）—— 东北，12 根人字柱 +
      塔顶破口补板 + 褪色环带 + 塔基锈流，全场最强剪影。
    · 球罐 ×2 —— 前排（-Y 侧），6 支腿 + 赤道弧形操作平台 + 侧爬梯 + 顶部接管。
    · Ø4/Ø3 小罐 —— 前排西侧；泵组小棚（单坡顶 + 4 柱 + 泵/电机）夹在中间。
    · 管汇 —— 地眠管一长一短 + 睡枕 + 阀站 + 法兰；泵出料管翻围堰进大罐。

材质纪律：零贴图零金属度，只用 ST.SLOTS 槽名；锈蚀 = 贴面薄板/环带（Kit_Rust /
Kit_RustDark）叠在 Kit_SteelPale 罐身上，靠调色板对比出质感。
"""

import math

import kit_common as K

TAU = math.pi * 2.0
DEG = math.pi / 180.0

# 槽名速记（全部在 style_tokens.SLOTS 已登记，拼错当场 KeyError）
C_L = "Kit_ConcreteLight"
C_M = "Kit_ConcreteMid"
C_D = "Kit_ConcreteDark"
RUST = "Kit_Rust"
RUSTD = "Kit_RustDark"
STL_B = "Kit_SteelBlue"
STL_P = "Kit_SteelPale"
GLS = "Kit_GlassDark"
YEL = "Kit_PaintYellow"
IRON = "Kit_Iron"
ROCK = "Kit_RockDark"


# ---------------------------------------------------------------------------
# 局部小件（kit_common 没有的，全部用 Mesher 图元拼，不动公共文件）
# ---------------------------------------------------------------------------

def _wall_patch(m, cx, cy, r_in, r_out, a0, a1, z0, z1, mat, seg_deg=6.0, smooth=True):
    """贴壁薄板（锈痕/补带/塔身锈流）：a0→a1 可跨 2π（a1>a0 即可），4 条环带围成薄板。"""
    n = max(2, int(round(abs(a1 - a0) / math.radians(seg_deg))))
    rows = []
    for (r, z) in ((r_in, z0), (r_out, z0), (r_out, z1), (r_in, z1)):
        row = []
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            row.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
        rows.append(row)
    verts = rows[0] + rows[1] + rows[2] + rows[3]
    faces = []
    for i in range(n):
        faces.append((i, i + 1, n + 1 + i + 1, n + 1 + i))                # 底环带
        faces.append((n + 1 + i, n + 1 + i + 1, 2 * n + 2 + i + 1, 2 * n + 2 + i))  # 外壁
        faces.append((2 * n + 2 + i, 2 * n + 2 + i + 1, 3 * n + 3 + i + 1, 3 * n + 3 + i))  # 顶环带
        faces.append((3 * n + 3 + i, 3 * n + 3 + i + 1, i, i + 1))        # 内壁
    return m.add(verts, faces, mat=mat, smooth=smooth)


def _flange_h(m, at, r, rz=0.0, mat=RUST, bolt_mat=None, bolts=6):
    """水平管端法兰（盘面竖直，法线方向 = az(rz+90°)）。at = 盘中心。"""
    bolt_mat = bolt_mat or mat
    rf = r * 1.45
    ca, sa = math.cos(rz), math.sin(rz)
    m.cyl(rf, 0.055, at=(at[0], at[1], at[2] - 0.0275), seg=14, rx=math.pi / 2,
          rz=rz, mat=mat, smooth=True)
    for i in range(bolts):
        a = TAU * i / bolts
        lx, lz = math.cos(a) * rf * 0.75, math.sin(a) * rf * 0.75
        m.cyl(0.032, 0.07, at=(at[0] + lx * ca, at[1] + lx * sa, at[2] + lz - 0.035),
              seg=6, rx=math.pi / 2, rz=rz, mat=bolt_mat)


def _valve(m, at, r_pipe=0.09, mat=IRON, wheel_mat=RUST):
    """阀站：立管 + 阀体 + 手轮（盘 + 十字柄）。at = 立管底（管汇顶面上）。"""
    x, y, z = at
    m.cyl(r_pipe * 0.62, 0.5, at=(x, y, z), seg=8, mat=mat)
    m.box((0.26, 0.2, 0.28), at=(x, y, z + 0.5), mat=mat)
    m.cyl(0.03, 0.22, at=(x, y, z + 0.78), seg=6, mat=mat)
    m.cyl(0.17, 0.035, at=(x, y, z + 0.98), seg=14, rx=math.pi / 2, mat=wheel_mat)
    for sgn in (-1, 1):
        m.member((x - 0.15, y, z + 0.99), (x + 0.15, y, z + 0.99), 0.024, 0.024, mat=wheel_mat)
        m.member((x, y - 0.15, z + 0.99), (x, y + 0.15, z + 0.99), 0.024, 0.024, mat=wheel_mat)


def _sleeper(m, x, y, mat=RUSTD):
    """管匝睡枕：短柱 + 横托。"""
    m.box((0.16, 0.16, 0.34), at=(x, y + 0.18, 0), mat=mat)
    m.box((0.2, 0.55, 0.07), at=(x, y, 0.34), mat=mat)


def _tank_landing(m, cx, cy, r, az, ztop, mat=RUST):
    """爬梯顶小平台：骑在罐沿上的格栅板 + 外缘栏杆 + 支架。（box at = 底面中心）"""
    a = az * DEG
    px = cx + (r + 0.62) * math.cos(a)
    py = cy + (r + 0.62) * math.sin(a)
    rz = az * DEG
    ca, sa = math.cos(rz), math.sin(rz)

    def l2g(lx, ly):
        return (px + lx * ca - ly * sa, py + lx * sa + ly * ca)

    m.box((1.3, 1.0, 0.055), at=(px, py, ztop - 0.02), rz=rz, mat=mat)
    m.box((1.3, 0.1, 0.06), at=(*l2g(0.0, -0.14), ztop + 0.035), rz=rz, mat=mat)
    m.box((1.3, 0.1, 0.06), at=(*l2g(0.0, 0.14), ztop + 0.035), rz=rz, mat=mat)
    # 外缘栏杆（沿板外缘切向 ±0.6，两端 0.3 内收）
    ox, oy = math.cos(a + math.pi / 2), math.sin(a + math.pi / 2)
    rad = (math.cos(a), math.sin(a))
    E = (px + 0.45 * rad[0], py + 0.45 * rad[1])
    seq = [tuple(E[i] - 0.6 * (ox, oy)[i] - 0.3 * rad[i] for i in (0, 1)),
           tuple(E[i] - 0.6 * (ox, oy)[i] for i in (0, 1)),
           tuple(E[i] for i in (0, 1)),
           tuple(E[i] + 0.6 * (ox, oy)[i] for i in (0, 1))]
    posts = seq + [tuple(E[i] + 0.6 * (ox, oy)[i] - 0.3 * rad[i] for i in (0, 1))]
    for (qx, qy) in posts:
        m.member((qx, qy, ztop), (qx, qy, ztop + 1.05), 0.05, 0.05, mat=mat)
    for i in range(3):
        m.pipe_member((seq[i][0], seq[i][1], ztop + 1.05),
                      (seq[i + 1][0], seq[i + 1][1], ztop + 1.05), r=0.028, mat=mat)
        m.pipe_member((seq[i][0], seq[i][1], ztop + 0.55),
                      (seq[i + 1][0], seq[i + 1][1], ztop + 0.55), r=0.022, mat=mat)
    # 支架：从罐壁斜撑到平台底
    for s in (-1, 0, 1):
        sx = cx + (r + 0.03) * math.cos(a + s * 14 * DEG)
        sy = cy + (r + 0.03) * math.sin(a + s * 14 * DEG)
        ex = px + s * 0.55 * ox
        ey = py + s * 0.55 * oy
        m.member((sx, sy, ztop - 0.42), (ex, ey, ztop - 0.05), 0.09, 0.07, mat=mat)


# ---------------------------------------------------------------------------
# 1. 立式储罐（×4）
# ---------------------------------------------------------------------------

def _vertical_tank(m, cx, cy, r, h, seg, shell_mat, az_ladder, streaks,
                   dome="both", yellow_band=None, breather=True, downcomer=None,
                   ladder_az_override=None):
    """立式圆筒储罐：混凝土环座 + tank_shell + 锈痕 + 爬梯 + 罐顶栏杆/附件 + 底部管嘴。"""
    base_z = 0.2
    top_z = base_z + h
    # 混凝土环座 + 垫层（收窄，别顶出分区盒/围堰）
    m.cyl(r + 0.35, 0.13, at=(cx, cy, 0), seg=seg, mat=C_M)
    m.revolve([(r + 0.3, 0), (r + 0.5, 0.02), (r + 0.5, 0.24), (r + 0.3, 0.24)],
              at=(cx, cy, 0), seg=seg, mat=C_M, caps=False)
    # 罐壳（自带加强环 ditch）
    m.tank_shell(r, h, at=(cx, cy, base_z), seg=seg, mat=shell_mat, rings=4,
                 ditch=True, dome=dome, mat_band=RUSTD)
    # 底圈 rigor 锈带（最常见的老化特征）
    m.revolve([(r + 0.015, base_z + 0.06), (r + 0.05, base_z + 0.06),
               (r + 0.05, base_z + 0.8), (r + 0.015, base_z + 0.8)],
              at=(cx, cy, 0), seg=seg, mat=RUSTD, caps=False)
    # 竖向锈流
    for (az, z_hi, w, mat) in streaks:
        _wall_patch(m, cx, cy, r + 0.018, r + 0.05,
                    (az - w) * DEG, (az + w) * DEG, 0.35, z_hi, mat)
    if yellow_band:
        zb0, zb1 = yellow_band
        m.revolve([(r + 0.012, zb0), (r + 0.05, zb0), (r + 0.05, zb1), (r + 0.012, zb1)],
                  at=(cx, cy, 0), seg=seg, mat=YEL, caps=False)
    # 外爬梯（带护笼），rz=az：护笼鼓包朝外、开口朝罐
    la = (ladder_az_override if ladder_az_override is not None else az_ladder) * DEG
    lx = cx + (r + 0.18) * math.cos(la)
    ly = cy + (r + 0.18) * math.sin(la)
    m.ladder(at=(lx, ly, 0.12), h=top_z - 0.02, w=0.56, cage=True, rz=la, mat=RUST)
    _tank_landing(m, cx, cy, r, az_ladder, top_z)
    # 罐顶栏杆一圈（railing 契约：点相对 at）
    pts = [((r - 0.08) * math.cos(TAU * i / 12.0),
            (r - 0.08) * math.sin(TAU * i / 12.0)) for i in range(12)]
    m.railing(pts, h=1.1, at=(cx, cy, top_z), post_step=1.7, mat=RUST)

    def dome_z(rho):
        return top_z + 0.06 * h * math.sqrt(max(0.0, 1.0 - (rho / r) ** 2))

    if dome in ("both", "top"):
        # 人孔 / 呼吸阀 / 量油口（ρ 按罐半径取比例，小罐不至于悬空）
        a = 30 * DEG
        rho = 0.37 * r
        mx, my = cx + rho * math.cos(a), cy + rho * math.sin(a)
        m.cyl(0.32 * r / 3.5, 0.3, at=(mx, my, dome_z(rho) - 0.05), seg=14, mat=RUST)
        m.cyl(0.38 * r / 3.5, 0.07, at=(mx, my, dome_z(rho) + 0.24), seg=14, mat=RUSTD)
        # 呼吸阀
        if breather:
            a = 140 * DEG
            rho = 0.63 * r
            bx, by = cx + rho * math.cos(a), cy + rho * math.sin(a)
            m.cyl(0.13, 0.55, at=(bx, by, dome_z(rho) - 0.05), seg=10, mat=IRON)
            m.cyl(0.22, 0.06, at=(bx, by, dome_z(rho) + 0.48), seg=10, mat=IRON)
            m.box((0.2, 0.2, 0.18), at=(bx, by, dome_z(rho) + 0.1), mat=IRON)
        # 量油口
        a = 200 * DEG
        rho = 0.74 * r
        gx, gy = cx + rho * math.cos(a), cy + rho * math.sin(a)
        m.cyl(0.18, 0.22, at=(gx, gy, dome_z(rho) - 0.04), seg=10, mat=RUSTD)
        # 顶部接管 → 翻罐沿的落料管（好剪影）
        if downcomer:
            a = downcomer * DEG
            rho = 0.49 * r
            px, py = cx + rho * math.cos(a), cy + rho * math.sin(a)
            ex = cx + (r + 0.14) * math.cos(a)
            ey = cy + (r + 0.14) * math.sin(a)
            m.tube([(px, py, dome_z(rho) - 0.1), (px, py, top_z + 1.55),
                    (ex, ey, top_z + 1.55), (ex, ey, 2.4)], r=0.09, seg=8, mat=STL_P)
            m.flange(0.09, at=(ex, ey, 2.32), mat=RUST)
            for z in (4.2, 6.6):
                m.member((cx + (r + 0.02) * math.cos(a), cy + (r + 0.02) * math.sin(a), z),
                         (ex, ey, z), 0.07, 0.05, mat=RUSTD)
    # 底部管嘴（水平 + 法兰）
    for (az, z, ln) in _BOTTOM_NOZZLES.get((round(cx, 1), round(cy, 1)), []):
        a = az * DEG
        x0 = cx + (r - 0.12) * math.cos(a)
        y0 = cy + (r - 0.12) * math.sin(a)
        x1 = cx + (r + ln) * math.cos(a)
        y1 = cy + (r + ln) * math.sin(a)
        m.tube([(x0, y0, z), (x1, y1, z)], r=0.11, seg=8, mat=STL_P)
        _flange_h(m, (x1, y1, z), 0.11, rz=a + math.pi / 2, mat=RUST)


#: 各罐底部管嘴表（按罐心坐标查）：(方位角°, 高度, 伸出长)
_BOTTOM_NOZZLES = {
    (-8.8, 2.2):  [(250, 0.62, 0.75), (295, 0.5, 0.6)],
    (-2.3, 3.8): [(230, 0.55, 0.7), (310, 0.48, 0.6)],
    (-3.4, -3.7): [(255, 0.5, 0.9)],
    (-10.6, -4.9): [(260, 0.5, 0.42)],
}


def build(m):
    # =======================================================================
    # 1) 围堰（绕两座大罐）+ 内面深色地坪 + 缺口/坡板/转角柱
    # =======================================================================
    bx0, bx1, by0, by1 = -12.7, 0.8, -2.2, 6.78
    wt, wh = 0.25, 0.8
    # 南墙（留缺口 X[-10.6,-9.2]，box at = 底面中心）；西段短、东段长，缺口两端各一块半高残墙
    m.box((-10.6 - bx0, wt, wh), at=((bx0 - 10.6) / 2, by0 - wt / 2, 0), mat=C_M)
    m.box((bx1 - (-9.2), wt, wh), at=((-9.2 + bx1) / 2, by0 - wt / 2, 0), mat=C_M)
    m.box((0.9, wt, 0.5), at=(-11.05, by0 - wt / 2, 0), mat=C_D)     # 残墙（矮一截）
    m.box((0.8, wt, 0.62), at=(-9.55, by0 - wt / 2, 0), mat=C_D)     # 缺口东残墙
    m.box((by1 - by0, wt, wh), at=(bx0 - wt / 2, by0, 0), rz=math.pi / 2, mat=C_M)   # 西墙
    m.box((by1 - by0, wt, wh), at=(bx1 + wt / 2, by0, 0), rz=math.pi / 2, mat=C_M)   # 东墙
    m.box((bx1 - bx0, wt, wh), at=((bx0 + bx1) / 2, by1 - wt / 2, 0), mat=C_M)       # 北墙
    # 转角柱 + 中间柱（柱外面与墙外面平齐，收在分区盒内）
    wx0, wx1 = bx0 - wt / 2 + 0.21, bx1 + wt / 2 - 0.21     # 西/东墙线上柱心
    wy0, wy1 = by0 - wt / 2 + 0.21, by1 - wt / 2 - 0.21     # 南/北墙线上柱心
    for (px, py) in ((wx0, wy0), (wx1, wy0), (wx0, wy1), (wx1, wy1),
                     (-5.95, wy0), (-5.95, wy1)):
        m.box((0.42, 0.42, 1.02), at=(px, py, 0), mat=C_L)
    # 围堰内深一档的地坪（at = 中心）
    m.box((bx1 - bx0 - 0.25, by1 - by0 - 0.25, 0.06),
          at=((bx0 + bx1) / 2, (by0 + by1) / 2, 0), mat=C_D)
    # 缺口：钢板坡 + 散落碎块
    m.box((1.3, 1.25, 0.06), at=(-9.9, -2.8, 0.32), rx=40 * DEG, mat=RUST)
    m.box((0.55, 0.4, 0.28), at=(-10.9, -3.3, 0), rz=20 * DEG, mat=C_M)
    m.box((0.42, 0.3, 0.22), at=(-9.0, -3.15, 0), rz=65 * DEG, rx=15 * DEG, mat=C_D)
    m.box((0.4, 0.3, 0.24), at=(-11.1, -1.6, wh), rz=35 * DEG, mat=C_L)
    m.box((0.35, 0.28, 0.2), at=(-12.2, -1.2, wh), rz=-15 * DEG, mat=C_M)

    # =======================================================================
    # 2) 立式罐 A（Ø7×10，罐顶塌陷——镜头正面的主角）—— 围堰内西侧
    # =======================================================================
    acx, acy, ar, ah = -8.8, 2.2, 3.5, 10.0
    abase, atop = 0.2, 10.2
    m.cyl(ar + 0.35, 0.13, at=(acx, acy, 0), seg=36, mat=C_M)
    m.revolve([(ar + 0.3, 0), (ar + 0.5, 0.02), (ar + 0.5, 0.24), (ar + 0.3, 0.24)],
              at=(acx, acy, 0), seg=36, mat=C_M, caps=False)
    m.tank_shell(ar, ah, at=(acx, acy, abase), seg=36, mat=STL_P, rings=4,
                 ditch=True, dome="bottom", mat_band=RUSTD)
    m.revolve([(ar + 0.015, abase + 0.06), (ar + 0.05, abase + 0.06),
               (ar + 0.05, abase + 0.8), (ar + 0.015, abase + 0.8)],
              at=(acx, acy, 0), seg=36, mat=RUSTD, caps=False)
    for (az, z_hi, w, mat) in [(205, 5.5, 5, RUST), (248, 3.8, 4, RUSTD), (332, 6.4, 6, RUST),
                               (18, 4.6, 4, RUSTD), (60, 3.2, 3, RUST)]:
        _wall_patch(m, acx, acy, ar + 0.018, ar + 0.05,
                    (az - w) * DEG, (az + w) * DEG, 0.35, z_hi, mat)
    # 罐内：黑液面 + 淤堆
    m.cyl(3.3, 0.16, at=(acx, acy, 0.38), seg=36, mat=GLS)
    m.revolve([(0.0, 0.7), (1.3, 0.95), (2.4, 1.5), (3.1, 2.3)],
              at=(acx, acy, 0), seg=24, mat=ROCK, caps=True)
    # 塌顶薄板：两块搭在罐沿、三块陷进罐里
    m.box((3.0, 1.9, 0.06), at=(acx - 0.15, acy + 2.4, 9.85), rx=24 * DEG, rz=15 * DEG, mat=RUST)
    m.box((2.7, 1.8, 0.06), at=(acx + 2.7, acy + 0.5, 9.6), ry=-20 * DEG, rz=-10 * DEG, mat=RUSTD)
    m.box((2.6, 1.8, 0.05), at=(acx - 0.9, acy - 0.7, 8.3), rx=18 * DEG, rz=40 * DEG, mat=RUSTD)
    m.box((2.2, 1.5, 0.05), at=(acx + 0.8, acy + 0.8, 7.6), rx=-14 * DEG, rz=-25 * DEG, mat=RUST)
    m.box((1.9, 1.4, 0.05), at=(acx - 0.1, acy + 0.2, 8.9), ry=12 * DEG, rz=70 * DEG, mat=RUSTD)
    # 撕裂的罐沿（一排歪斜的尖板，留两处豁口）
    for i in range(12):
        if i in (3, 8):
            continue
        a = TAU * i / 12.0 + 0.15
        m.box((0.6, 0.28, 0.5 + 0.14 * ((i * 37) % 3)),
              at=(acx + 3.42 * math.cos(a) - 0.14, acy + 3.42 * math.sin(a) - 0.07, 10.05),
              rz=a + 25 * DEG * ((i % 3) - 1), rx=12 * DEG * ((i % 2) * 2 - 1), mat=RUSTD)
    # 断柱 + 断落的顶部接管挂进罐里
    m.member((acx + 3.5, acy + 1.1, 10.1), (acx + 2.9, acy + 2.1, 11.0), 0.05, 0.05, mat=RUST)
    m.tube([(acx - 0.8, acy + 2.1, 10.05), (acx - 1.2, acy + 1.5, 9.0),
            (acx - 0.9, acy + 1.0, 8.1)], r=0.09, seg=6, mat=RUST)
    # 爬梯 + 平台 + 幸存的罐顶栏杆
    la = 270 * DEG
    m.ladder(at=(acx + (ar + 0.18) * math.cos(la), acy + (ar + 0.18) * math.sin(la), 0.12),
             h=atop - 0.02, w=0.56, cage=True, rz=la, mat=RUST)
    _tank_landing(m, acx, acy, ar, 270, atop)
    pts = [((ar - 0.05) * math.cos(TAU * i / 12.0),
            (ar - 0.05) * math.sin(TAU * i / 12.0)) for i in range(12)]
    m.railing(pts, h=1.05, at=(acx, acy, atop), post_step=1.7, mat=RUST)
    for (az, z, ln) in _BOTTOM_NOZZLES[(round(acx, 1), round(acy, 1))]:
        a = az * DEG
        m.tube([(acx + (ar - 0.12) * math.cos(a), acy + (ar - 0.12) * math.sin(a), z),
                (acx + (ar + ln) * math.cos(a), acy + (ar + ln) * math.sin(a), z)],
               r=0.11, seg=8, mat=STL_P)
        _flange_h(m, (acx + (ar + ln) * math.cos(a), acy + (ar + ln) * math.sin(a), z),
                  0.11, rz=a + math.pi / 2, mat=RUST)

    # =======================================================================
    # 3) 立式罐 B（Ø5.5×8，完好，带人孔/呼吸阀/落料管）—— 围堰内东侧
    # =======================================================================
    _vertical_tank(
        m, -2.3, 3.75, 2.75, 8.0, 28, STL_P, az_ladder=320,
        streaks=[(210, 4.2, 5, RUST), (300, 5.6, 4, RUSTD), (30, 3.4, 4, RUST)],
        downcomer=300)

    # =======================================================================
    # 4) 小罐 C（Ø4×6，褪色黄环带）/ D（Ø3×5）—— 前排西侧
    # =======================================================================
    _vertical_tank(
        m, -3.4, -3.7, 2.0, 6.0, 24, STL_P, az_ladder=270,
        streaks=[(190, 3.6, 5, RUST), (315, 2.8, 4, RUSTD), (55, 4.4, 4, RUST)],
        yellow_band=(4.3, 4.78), breather=False, downcomer=None)
    _vertical_tank(
        m, -10.6, -4.9, 1.5, 5.0, 20, STL_P, az_ladder=340,
        streaks=[(150, 2.6, 6, RUSTD), (75, 3.4, 4, RUST)],
        breather=False, downcomer=None)

    # =======================================================================
    # 5) 球罐 ×2（Ø5，6 支腿 + 赤道弧形平台 + 侧爬梯 + 顶部接管）
    # =======================================================================
    def sphere_tank(sx, sy, shell_mat, arc=(195.0, 243.0), rust_bands=(("eq", RUST),)):
        cz, R = 4.2, 2.5
        m.sphere(R, at=(sx, sy, cz), seg=28, rings=14, mat=shell_mat)
        # 焊缝锈带（纬向环带）
        for (kind, mat) in rust_bands:
            if kind == "eq":
                m.sphere(R + 0.03, at=(sx, sy, cz), seg=28, rings=2,
                         z_lo=-0.014, z_hi=0.014, mat=mat)
            else:
                m.sphere(R + 0.03, at=(sx, sy, cz), seg=28, rings=2,
                         z_lo=kind - 0.022, z_hi=kind + 0.022, mat=mat)
        # 支腿 ×6 + 混凝土墩
        for k in range(6):
            a = (15 + 60 * k) * DEG
            bx, by = sx + 2.2 * math.cos(a), sy + 2.2 * math.sin(a)
            tx, ty = sx + 1.78 * math.cos(a), sy + 1.78 * math.sin(a)
            m.pipe_member((bx, by, 0.28), (tx, ty, 4.3), r=0.095, seg=8, mat=RUST)
            m.box((0.5, 0.5, 0.3), at=(bx - 0.25, by - 0.25, 0), mat=C_M)
            m.member((bx, by, 2.4), (tx, ty, 3.4), 0.07, 0.05, mat=RUSTD)  # 腿间撑
        # 赤道弧形平台 + 栏杆
        a0, a1 = arc
        nseg = 5
        posts = []
        for i in range(nseg):
            am = a0 + (a1 - a0) * (i + 0.5) / nseg
            px = sx + 3.15 * math.cos(am * DEG)
            py = sy + 3.15 * math.sin(am * DEG)
            m.box((0.82, 0.85, 0.07), at=(px, py, 3.87),
                  rz=(am + 90) * DEG, mat=RUST)
            m.box((0.7, 0.08, 0.06), at=(px, py, 3.83),
                  rz=(am + 90) * DEG, mat=RUSTD)
            if i % 2 == 0:
                aa = (a0 + (a1 - a0) * i / nseg) * DEG
                posts.append((sx + 3.15 * math.cos(aa), sy + 3.15 * math.sin(aa)))
        aa = a1 * DEG
        posts.append((sx + 3.15 * math.cos(aa), sy + 3.15 * math.sin(aa)))
        for (qx, qy) in posts:
            m.member((qx, qy, 3.94), (qx, qy, 4.99), 0.05, 0.05, mat=RUST)
        for i in range(len(posts) - 1):
            m.pipe_member((posts[i][0], posts[i][1], 4.99),
                          (posts[i + 1][0], posts[i + 1][1], 4.99), r=0.028, mat=RUST)
            m.pipe_member((posts[i][0], posts[i][1], 4.45),
                          (posts[i + 1][0], posts[i + 1][1], 4.45), r=0.022, mat=RUST)
        # 侧爬梯（西面，rz=az：护笼朝外）
        la = 195 * DEG
        m.ladder(at=(sx + 3.05 * math.cos(la), sy + 3.05 * math.sin(la), 0.0),
                 h=4.1, w=0.5, cage=True, rz=la, mat=RUST)
        # 顶部接管 + 弯头 + 法兰 + 通气帽
        m.tube([(sx, sy, 6.35), (sx, sy, 7.45), (sx, sy + 0.85, 7.45)], r=0.1, seg=8, mat=STL_P)
        _flange_h(m, (sx, sy + 0.95, 7.45), 0.1, rz=0.0, mat=RUST)
        m.cyl(0.09, 0.32, at=(sx + 0.55, sy - 0.3, 6.5), seg=8, mat=IRON)
        # 底部出料管（废弃：出料→截止阀→盲法兰）
        m.tube([(sx, sy, cz - R + 0.2), (sx, sy, 0.62), (sx, sy - 1.6, 0.62)], r=0.09,
               seg=8, mat=STL_P)
        _valve(m, (sx, sy - 0.85, 0.62))
        _flange_h(m, (sx, sy - 1.72, 0.62), 0.09, rz=0.0, mat=RUST)

    sphere_tank(4.45, -3.95, STL_P, arc=(186.0, 236.0),
                rust_bands=(("eq", RUST), (0.44, RUSTD), (-0.5, RUST)))
    sphere_tank(10.4, -3.8, STL_B, arc=(196.0, 240.5),
                rust_bands=(("eq", RUSTD), (0.46, RUST)))

    # =======================================================================
    # 6) 双曲线冷却塔（底 Ø9 / 顶 Ø5 / 高 14）—— 全场最强剪影
    # =======================================================================
    tx, ty = 8.15, 2.3
    zb, zthr, ztop = 2.2, 8.8, 14.0
    r_thr, r_base, r_top = 2.3, 4.5, 2.5
    amp = (zthr - zb) / math.sqrt((r_base / r_thr) ** 2 - 1.0)

    def rt(z):
        if z <= zthr:
            return r_thr * math.sqrt(1.0 + ((z - zthr) / amp) ** 2)
        t = (z - zthr) / (ztop - zthr)
        return r_thr + (r_top - r_thr) * (t ** 1.7)

    zs = [2.2, 2.7, 3.3, 4.0, 4.8, 5.7, 6.6, 7.5, 8.3, 8.8, 9.4, 10.1, 10.9, 11.8,
          12.7, 13.4, 13.72]
    prof = [(rt(z), z) for z in zs]
    m.revolve(prof, at=(tx, ty, 0), seg=32, mat=C_L, smooth=True, caps=True)
    # 内壁（开口向上的暗腔暗示）
    m.revolve([(rt(13.7) - 0.16, 13.7), (rt(12.2) - 0.16, 12.2),
               (rt(9.6) - 0.16, 9.6), (rt(8.9) - 0.16, 8.9)],
              at=(tx, ty, 0), seg=32, mat=C_D, smooth=True, caps=False)
    # 褪色环带 ×3
    for (z0, z1, mat) in ((5.2, 5.78, C_M), (8.35, 8.9, ROCK if False else C_M),
                          (11.3, 11.82, C_M)):
        m.revolve([(rt(z0) + 0.015, z0), (rt(z0) + 0.05, z0),
                   (rt(z1) + 0.05, z1), (rt(z1) + 0.015, z1)],
                  at=(tx, ty, 0), seg=32, mat=mat, caps=False)
    # 塔基锈流（水垢/锈水从人字柱根往上爬）
    for (a0, a1, z0, z1, mat) in ((190, 215, 0.45, 3.2, RUST), (250, 268, 0.4, 2.4, RUSTD),
                                  (305, 322, 0.45, 3.6, RUST), (20, 45, 0.4, 2.8, RUSTD)):
        _wall_patch(m, tx, ty, rt((z0 + z1) / 2) + 0.02, rt((z0 + z1) / 2) + 0.05,
                    a0 * DEG, a1 * DEG, z0, z1, mat, seg_deg=5.0)
    # 塔顶环梁（留两处破口）+ 破口毛边 + 补板
    for (a0, a1) in ((175, 292), (318, 508)):
        _wall_patch(m, tx, ty, rt(13.72) + 0.01, rt(13.72) + 0.17,
                    a0 * DEG, a1 * DEG, 13.72, 14.0, C_M, seg_deg=5.0)
    for (a, tilt) in ((292, 30), (318, -25), (148, 40), (175, -35)):
        aa = a * DEG
        rr = rt(13.8) + 0.05
        m.box((0.55, 0.3, 0.4), at=(tx + rr * math.cos(aa) - 0.15,
                                    ty + rr * math.sin(aa) - 0.07, 13.7),
              rz=aa, rx=tilt * DEG, mat=C_D)
    for (a, z, rz2, rx2) in ((300, 13.1, 40, 18), (160, 12.6, -30, -15),
                             (295, 12.3, 15, -22), (155, 13.3, -45, 12)):
        aa = a * DEG
        rr = rt(z) + 0.06
        m.box((1.5, 1.1, 0.06), at=(tx + rr * math.cos(aa) - 0.5,
                                    ty + rr * math.sin(aa) - 0.35, z),
              rz=aa + rz2 * DEG, rx=rx2 * DEG, ry=rz2 * 0.5 * DEG, mat=RUST if a > 200 else RUSTD)
    # 人字柱 ×12 + 基础墩 + 集水环墙
    for k in range(12):
        a = TAU * k / 12.0 + 8 * DEG
        bx, by = tx + 4.02 * math.cos(a), ty + 4.02 * math.sin(a)
        ex, ey = tx + 4.46 * math.cos(a), ty + 4.46 * math.sin(a)
        m.pipe_member((bx, by, 0.35), (ex, ey, 2.2), r=0.17, seg=8, mat=C_M)
        m.box((0.55, 0.55, 0.35), at=(bx - 0.275, by - 0.275, 0), mat=C_M)
    m.revolve([(4.25, 0), (4.68, 0), (4.68, 0.5), (4.25, 0.5)],
              at=(tx, ty, 0), seg=32, mat=C_M, caps=False)
    # 废弃冷却水引入管 ×2（从东面进塔，带法兰截止）
    for (sy0, z, az_f) in ((-0.5, 0.45, 51.0), (0.1, 0.72, 47.0)):
        m.tube([(12.3, sy0, z), (10.6, sy0 + 1.36, z)], r=0.12, seg=8, mat=STL_P)
        _flange_h(m, (10.6, sy0 + 1.36, z), 0.12, rz=az_f * DEG, mat=RUST)

    # =======================================================================
    # 7) 泵组小棚（单坡顶 + 4 柱）+ 泵/电机 + 出料管翻围堰
    # =======================================================================
    scx, scy = -7.0, -5.2
    m.box((3.6, 2.6, 0.08), at=(scx, scy, 0), mat=C_D)
    for (cx2, cy2, ch) in ((-1.55, -1.05, 2.35), (1.55, -1.05, 2.35),
                           (-1.55, 1.05, 2.95), (1.55, 1.05, 2.95)):
        m.box((0.15, 0.15, ch), at=(scx + cx2 - 0.075, scy + cy2 - 0.075, 0), mat=RUSTD)
    m.box((3.95, 3.0, 0.09), at=(scx, scy, 2.85), rx=9 * DEG, mat=RUST)
    m.box((1.6, 1.0, 0.05), at=(scx - 0.8, scy - 0.5, 3.02), rx=9 * DEG, rz=8 * DEG, mat=STL_B)
    for yy in (-0.85, 0.85):
        m.box((3.9, 0.12, 0.14), at=(scx, scy + yy, 2.72), rx=9 * DEG, mat=RUSTD)
    # 泵 ×2（蜗壳+电机）+ 基座
    for k, px in ((0, scx - 0.75), (1, scx + 0.7)):
        m.box((1.45, 0.55, 0.14), at=(px, scy, 0.08), mat=RUSTD)
        m.cyl(0.27, 0.55, at=(px - 0.3, scy, 0.45), seg=14, ry=math.pi / 2, mat=STL_B)
        m.sphere(0.3, at=(px + 0.28, scy, 0.45), seg=12, rings=8, mat=STL_B)
        m.cyl(0.2, 0.5, at=(px + 0.32, scy, 0.45), seg=12, ry=math.pi / 2, mat=YEL)
        m.box((0.16, 0.16, 0.12), at=(px + 0.5, scy - 0.08, 0.62), mat=IRON)
        # 出料：立管 → 北上 → 翻围堰 → 进 A 罐
        dpx = px - 0.1
        shell_y = 2.2 - math.sqrt(max(0.1, 12.25 - (dpx + 8.8) ** 2))
        m.tube([(dpx, scy, 0.68), (dpx, scy, 1.05), (dpx, shell_y + 0.04, 1.05)],
               r=0.08, seg=8, mat=STL_B)
        _flange_h(m, (dpx, shell_y + 0.04, 1.05), 0.08, rz=0.0, mat=RUST)
        m.member((dpx, -2.2, 0.78), (dpx, -2.2, 1.12), 0.08, 0.08, mat=RUSTD)
        # 吸入：从管汇北上进泵
        m.tube([(px + 0.35, -6.68, 0.45), (px + 0.35, scy - 0.62, 0.45),
                (px + 0.35, scy - 0.42, 0.32)], r=0.07, seg=8, mat=RUST)

    # =======================================================================
    # 8) 管汇：地眠管一长一短 + 睡枕 + 阀站 + 法兰
    # =======================================================================
    my = -6.7
    for x in (-11.2, -9.6, -8.0, -6.4, -4.8, -3.2, -1.6, 0.0, 1.6):
        _sleeper(m, x, my)
    m.tube([(-11.4, my, 0.5), (2.2, my, 0.5)], r=0.11, seg=10, mat=STL_P)
    m.tube([(-11.4, my - 0.16, 0.5), (-4.0, my - 0.16, 0.5)], r=0.08, seg=8, mat=RUST)
    m.tube([(-6.0, my + 0.28, 0.5), (2.2, my + 0.28, 0.5)], r=0.055, seg=8, mat=STL_B)
    for x in (-10.0, -5.2, 0.6):
        _flange_h(m, (x, my, 0.5), 0.11, rz=90 * DEG, mat=RUST)
    _flange_h(m, (-4.0, my - 0.16, 0.5), 0.08, rz=90 * DEG, mat=RUST)
    _flange_h(m, (2.2, my, 0.5), 0.11, rz=90 * DEG, mat=RUSTD)
    _valve(m, (-7.6, my, 0.61))
    _valve(m, (1.2, my, 0.61))
    # 东段废弃盲管（球罐 B 出料方向）+ 立管盲法兰
    for x in (10.9, 11.8):
        _sleeper(m, x, -6.55)
    m.tube([(9.7, -6.55, 0.5), (12.5, -6.55, 0.5)], r=0.1, seg=10, mat=RUST)
    _flange_h(m, (9.7, -6.55, 0.5), 0.1, rz=90 * DEG, mat=RUSTD)
    _valve(m, (11.3, -6.55, 0.61))
    m.cyl(0.1, 0.7, at=(12.5, -6.55, 0.5), seg=8, mat=RUST)
    m.flange(0.1, at=(12.5, -6.55, 1.2), mat=RUSTD)
    # 地上散落旧管（西空地）
    m.tube([(-12.2, -6.15, 0.15), (-10.55, -6.5, 0.15)], r=0.14, seg=8, mat=RUST)
    m.tube([(-12.4, -5.55, 0.13), (-11.15, -6.1, 0.13)], r=0.1, seg=8, mat=STL_P)
    _flange_h(m, (-12.25, -5.81, 0.14), 0.1, rz=60 * DEG, mat=RUSTD)
