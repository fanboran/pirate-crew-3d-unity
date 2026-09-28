# -*- coding: utf-8 -*-
"""chemplant/mod_pipes.py —— 管廊带分件（分区 AREAS['pipes']，54×4.6×9 m，PIPE-03）。

纯程序化建模：东西横贯主管桥 + 阀门站 ×2 + 缓冲罐管汇 + 泵组 ×3 + 断裂坠落管段。
零贴图、零金属度；质感全部由几何密度（焊缝环/鞍座/法兰/锯齿断口/保温棉）与
调色板对比（SteelPale/SteelBlue/Rust/RustDark/ConcreteDark/Iron/PaintYellow）承担。

【布局总览（本地系：原点=分区中心地面投影，+X 东，+Y 北，地面 z=0）】
  · 柱网：两列工字钢柱（y=±1.5），柱位 X=[-26,-20,-14,-8,2,8,14,20,26]（柱距 6 m），
    柱底混凝土短基墩；净跨 X[-8,2]（10 m）让南北支路（ROAD_SIDE_X=-6.5..-3.5）
    从廊下穿过，底层管束在此爬升 1.2 m 抬空（管底净空 ≥5.5 m）。
  · 两层承重横梁（z=4.2 / z=6.4，梁顶 4.36 / 6.56）；门架平面交叉撑 +
    纵向交替斜撑 + 净跨两榀加强桁架。
  · 管束：底层 6 根 + 顶层 5 根（Ø0.15~0.50），槽色混拼；柱位一道鞍座（6 m 间距）、
    焊缝环按 6 m 错开柱位。
  · 两端走向转折：X≈±24.5 管束北折出界（到本地 Y=3.4，越界 1.1 m ≤ 1.5）通向
    塔区（西）/罐区（东），北折段下混凝土管墩 + 托梁；顶层管两端出界 + 盲板法兰。
  · 阀站#1（底层管汇，X≈-18）：管 A/B/C 各引支管下接汇管，3 阀 + 支架 + 法兰。
  · 泵组 ×3（壳心 X=4.5/8.5/12.5，南缘 y≈-1.9）：蜗壳泵体 + 卧式电机 + 翅片 +
    联轴器护罩 + 混凝土底座 + 进出支管（出口上接管 A）。
  · 阀站#2（顶层管 G，X≈11）+ 南挑顶层操作平台 + 带笼爬梯；
    缓冲罐管汇（X≈17，卧式罐 + 3 支管接顶层下方的管 D）。
  · 废弃感：顶层管 I 断裂坠落（断口 X≈-3.4，锯齿翻边，坠管横落地面）；
    底层管 E 断裂（X≈-12，西端上翘、中段坠地）；顶层小管 K V 形悬垂（X≈21.5）；
    锈蚀补板、悬挂残破保温棉、倒伏门架、地面散管堆。
"""

import math
import random

import kit_common as K

#: 固定随机种子：断口锯齿 / 坠落姿态可复现（改数字 = 改破口长相）
RNG = random.Random(20303)

# ---------------------------------------------------------------------------
# 结构标高与柱网（唯一事实源，往下所有函数引用这里）
# ---------------------------------------------------------------------------
COL_X = (-26.0, -20.0, -14.0, -8.0, 2.0, 8.0, 14.0, 20.0, 26.0)
COL_Y = (-1.5, 1.5)
FOOT_H = 0.35                       # 基墩高
COL_TOP = 6.56                      # 柱顶（= 顶层梁顶）
BEAM_BOT_Z = 4.2                    # 底层横梁中心
BEAM_TOP_Z = 6.4                    # 顶层横梁中心
BEAM_BOT_TOP = 4.36                 # 底层梁顶（鞍座/走台基面）
BEAM_TOP_TOP = 6.56                 # 顶层梁顶
GAP_X0, GAP_X1 = -8.0, 2.0          # 净跨（南北支路上方，不放柱）
CLIMB = 1.2                         # 净跨处底层管抬升量
BEND_XW, BEND_XE = -24.5, 24.5      # 两端北折线
BEND_Y_END = 3.4                    # 北折出口（越界 1.1 m ≤ 1.5 允许值）

#: 管束表：底层 (y, 半径, 槽名)，顶层多一列 dz 错落（斜排，避免低视角叠成一根）。
#: 管心标高 = 梁顶 + r + 0.10（+ dz）。最大管让给锈红 Ø0.42，浅色管做点缀。
PIPES_BOT = (
    (-1.15, 0.21, "Kit_Rust"),
    (-0.62, 0.18, "Kit_SteelBlue"),
    (-0.05, 0.15, "Kit_SteelPale"),
    (0.50, 0.12, "Kit_RustDark"),
    (0.92, 0.095, "Kit_Rust"),
    (1.22, 0.07, "Kit_SteelPale"),
    (1.45, 0.055, "Kit_RustDark"),
)
PIPES_TOP = (
    (-0.75, 0.16, "Kit_Rust", 0.0),
    (-0.2, 0.13, "Kit_SteelPale", 0.08),
    (0.35, 0.11, "Kit_SteelBlue", 0.16),   # 断裂坠落 #1 的管
    (0.85, 0.09, "Kit_Rust", 0.24),
    (1.25, 0.065, "Kit_RustDark", 0.32),   # V 形悬垂的管
)
PIPE_BROKEN_TOP = 2                 # PIPES_TOP 里断管的序号
PIPE_BROKEN_BOT = 4                 # PIPES_BOT 里断管的序号
PIPE_SAG_TOP = 4                    # PIPES_TOP 里悬垂管的序号


def _z_bot(r):
    return BEAM_BOT_TOP + r + 0.10


def _z_top(r, dz=0.0):
    return BEAM_TOP_TOP + r + 0.10 + dz


def _seg_for(r):
    return 18 if r >= 0.18 else (14 if r >= 0.11 else 12)


def _ye(y):
    """北折后的收拢位（扇形收拢到 y*0.5+0.2 ∈ [-0.33, 0.83]）。"""
    return y * 0.5 + 0.2


# ---------------------------------------------------------------------------
# 通用小件（缺的图元就在本文件里拼：法兰要水平/侧向，kit_common.flange 只给竖向）
# ---------------------------------------------------------------------------

def _flange(m, r, at, axis="x", bolts=8, mat="Kit_Rust", bolt_mat="Kit_Iron",
            thick=0.055):
    """任意朝向的法兰对（盘 + 螺栓圈）。axis = 盘面法线方向。at = 盘中心。"""
    x, y, z = at
    rf = r * 1.45
    if axis == "x":
        m.cyl(rf, thick, at=(x - thick / 2, y, z), ry=math.pi / 2,
              seg=max(12, bolts * 2), mat=mat)
        for i in range(bolts):
            a = K.TAU * i / bolts
            m.cyl(0.042, thick + 0.028, at=(x - (thick + 0.028) / 2, y + math.sin(a) * rf * 0.78,
                                            z + math.cos(a) * rf * 0.78),
                  ry=math.pi / 2, seg=6, mat=bolt_mat)
    elif axis == "-x":
        m.cyl(rf, thick, at=(x + thick / 2, y, z), ry=-math.pi / 2,
              seg=max(12, bolts * 2), mat=mat)
        for i in range(bolts):
            a = K.TAU * i / bolts
            m.cyl(0.042, thick + 0.028, at=(x + (thick + 0.028) / 2, y + math.sin(a) * rf * 0.78,
                                            z + math.cos(a) * rf * 0.78),
                  ry=math.pi / 2, seg=6, mat=bolt_mat)
    elif axis == "y":
        m.cyl(rf, thick, at=(x, y - thick / 2, z), rx=-math.pi / 2,
              seg=max(12, bolts * 2), mat=mat)
        for i in range(bolts):
            a = K.TAU * i / bolts
            m.cyl(0.042, thick + 0.028, at=(x + math.cos(a) * rf * 0.78, y - (thick + 0.028) / 2,
                                            z + math.sin(a) * rf * 0.78),
                  rx=-math.pi / 2, seg=6, mat=bolt_mat)
    else:  # "z" 竖直朝上
        m.cyl(rf, thick, at=(x, y, z - thick / 2), seg=max(12, bolts * 2), mat=mat)
        for i in range(bolts):
            a = K.TAU * i / bolts
            m.cyl(0.042, thick + 0.028, at=(x + math.cos(a) * rf * 0.78, y + math.sin(a) * rf * 0.78,
                                            z - (thick + 0.028) / 2),
                  seg=6, mat=bolt_mat)


def _weld_ring(m, r, at, axis="x", mat="Kit_Iron"):
    """焊缝环：管身上一圈细凸环。"""
    x, y, z = at
    if axis == "x":
        m.cyl(r + 0.018, 0.05, at=(x - 0.025, y, z), ry=math.pi / 2, seg=12, mat=mat)
    else:  # 竖管
        m.cyl(r + 0.018, 0.05, at=(x, y, z - 0.025), seg=12, mat=mat)


def _handwheel(m, at, r=0.16, mat="Kit_Rust"):
    """水平手轮（阀杆竖直朝上）：外圈管环 + 3 辐条 + 毂。at = 手轮中心。"""
    cx, cy, cz = at
    ring = []
    n = 12
    for i in range(n):
        a = K.TAU * i / n
        ring.append((cx + math.cos(a) * r, cy + math.sin(a) * r, cz))
    ring.append(ring[0])
    m.tube(ring, 0.017, seg=6, mat=mat, caps=False)
    for i in range(3):
        a = K.TAU * i / 3 + 0.5
        m.member((cx, cy, cz), (cx + math.cos(a) * r, cy + math.sin(a) * r, cz),
                 0.032, mat="Kit_Iron")
    m.cyl(0.045, 0.1, at=(cx - 0.045, cy - 0.045, cz - 0.06), seg=8, mat="Kit_Iron")


def _valve_vert(m, x, y, z, s=1.0, body_mat="Kit_SteelBlue"):
    """竖管上的闸阀：阀体方壳 + 阀盖 + 阀杆 + 上位水平手轮。at = 阀体中心。"""
    w = 0.30 * s
    m.box((w, 0.26 * s, w), at=(x, y, z - w / 2), mat=body_mat)
    m.cyl(0.09 * s, 0.09 * s, at=(x - 0.09 * s, y - 0.09 * s, z + w / 2), seg=10,
          mat="Kit_Iron")
    m.cyl(0.028 * s, 0.16 * s, at=(x - 0.028 * s, y - 0.028 * s, z + w / 2 + 0.09 * s),
          seg=8, mat="Kit_Iron")
    _handwheel(m, (x, y, z + w / 2 + 0.09 * s + 0.16 * s), r=0.15 * s)


def _valve_horiz(m, x, y, z, s=1.0, body_mat="Kit_SteelBlue"):
    """水平管（沿 X）上的闸阀：阀体骑管 + 竖阀杆 + 手轮。at = 阀体中心。"""
    w = 0.32 * s
    m.box((w, 0.28 * s, 0.30 * s), at=(x, y, z - 0.15 * s), mat=body_mat)
    m.cyl(0.028 * s, 0.18 * s, at=(x - 0.028 * s, y - 0.028 * s, z + 0.15 * s),
          seg=8, mat="Kit_Iron")
    _handwheel(m, (x, y, z + 0.15 * s + 0.18 * s), r=0.16 * s)


def _shoe(m, x, y_pipe, z_beam_top, z_pipe_c, r, mat="Kit_Iron"):
    """管托鞍座：立板 + 顶横托板，从梁顶垫到管底。"""
    gap = z_pipe_c - r - z_beam_top
    if gap < 0.03:
        return
    m.box((0.26, 0.20, gap), at=(x, y_pipe, z_beam_top), mat=mat)
    m.box((0.34, 0.26, 0.045), at=(x, y_pipe, z_beam_top + gap), mat=mat)


def _jag(m, at, r, n=7, mat="Kit_RustDark"):
    """管断口的撕裂翻边：断口端面一圈乱翘薄板。"""
    cx, cy, cz = at
    for i in range(n):
        a = K.TAU * i / n + RNG.uniform(-0.35, 0.35)
        rr = r * RNG.uniform(0.5, 1.05)
        sz = RNG.uniform(0.05, 0.13)
        m.box((sz, 0.03, RNG.uniform(0.08, 0.22)),
              at=(cx + math.cos(a) * rr - sz / 2, cy + math.sin(a) * rr - 0.015,
                  cz - RNG.uniform(0.02, 0.14)),
              rz=a, ry=RNG.uniform(-0.7, 0.7), rx=RNG.uniform(-0.45, 0.45), mat=mat)


def _patch(m, at, size, rz=0.0, ry=0.0, rx=0.0, mat="Kit_RustDark"):
    """锈蚀补板：一块歪贴的薄板。"""
    m.box(size, at=at, rz=rz, ry=ry, rx=rx, mat=mat)


def _lagging(m, x0, y, z_bottom, n, mats=("Kit_ConcreteLight", "Kit_RockMid")):
    """悬挂的残破保温棉：一排下垂薄板（微歪、长短不齐）。"""
    for i in range(n):
        x = x0 + i * 0.55 + RNG.uniform(-0.08, 0.08)
        w = RNG.uniform(0.38, 0.58)
        m.box((0.035, w, RNG.uniform(0.45, 0.8)),
              at=(x - 0.02, y - w / 2 + RNG.uniform(-0.05, 0.05),
                  z_bottom - RNG.uniform(0.42, 0.72)),
              rz=RNG.uniform(-0.25, 0.25), rx=RNG.uniform(-0.15, 0.15),
              mat=mats[i % len(mats)])


# ---------------------------------------------------------------------------
# 结构
# ---------------------------------------------------------------------------

def _foundations_and_frames(m):
    """基墩 + 工字钢柱 + 两层横梁 + 门架交叉撑。"""
    for i, x in enumerate(COL_X):
        col_mat = "Kit_SteelBlue" if i % 2 == 0 else "Kit_Rust"
        for y in COL_Y:
            m.box((0.75, 0.75, FOOT_H), at=(x, y, 0), mat="Kit_ConcreteDark")
            # 基墩顶小垫板
            m.box((0.34, 0.34, 0.03), at=(x, y, FOOT_H), mat="Kit_Iron")
            m.ibeam((x, y, FOOT_H), (x, y, COL_TOP), h=0.34, w=0.24, mat=col_mat)
        # 两层横梁（沿 Y，工字钢腹板竖直承管）
        m.ibeam((x, -1.5, BEAM_BOT_Z), (x, 1.5, BEAM_BOT_Z), h=0.32, w=0.18,
                mat="Kit_SteelBlue")
        m.ibeam((x, -1.5, BEAM_TOP_Z), (x, 1.5, BEAM_TOP_Z), h=0.32, w=0.18,
                mat="Kit_SteelBlue")
        # 门架平面（Y-Z）交叉撑：低层每个柱位（锈/蓝灰隔位），高层隔位且蓝灰为主
        brace = "Kit_Rust" if i % 2 == 0 else "Kit_SteelBlue"
        m.member((x, -1.5, 0.45), (x, 1.5, 3.95), 0.09, mat=brace)
        m.member((x, 1.5, 0.45), (x, -1.5, 3.95), 0.09, mat=brace)
        if i % 2 == 0:
            m.member((x, -1.5, 4.45), (x, 1.5, 6.18), 0.08, mat="Kit_SteelBlue")
            m.member((x, 1.5, 4.45), (x, -1.5, 6.18), 0.08, mat="Kit_SteelBlue")


def _longitudinals(m):
    """沿 X 纵梁（两层 × 两列，逐跨分段）+ 纵向交替斜撑 + 净跨加强桁架。"""
    for y in COL_Y:
        for i in range(len(COL_X) - 1):
            a, b = COL_X[i], COL_X[i + 1]
            for z in (BEAM_BOT_Z, BEAM_TOP_Z):
                m.ibeam((a + 0.1, y, z), (b - 0.1, y, z), h=0.28, w=0.16,
                        mat="Kit_SteelBlue")
            if GAP_X0 <= a and b <= GAP_X1 + 0.1:
                continue  # 净跨不放纵向斜撑（桁架另做）
            # 低层交替斜撑（锈/蓝灰相间，避免锈橙一片）
            diag = "Kit_Rust" if i % 2 == 0 else "Kit_SteelBlue"
            if i % 2 == 0:
                m.member((a + 0.12, y, 0.45), (b - 0.12, y, 3.95), 0.08, mat=diag)
            else:
                m.member((a + 0.12, y, 3.95), (b - 0.12, y, 0.45), 0.08, mat=diag)
            # 高层隔跨斜撑
            if i % 2 == 1:
                m.member((a + 0.12, y, 4.45), (b - 0.12, y, 6.18), 0.07, mat="Kit_Rust")
    # 净跨（-8..2）两榀加强桁架：竖杆 3 + 斜杆 4，借用两层纵梁做弦杆
    for y in COL_Y:
        z0, z1 = BEAM_BOT_Z + 0.12, BEAM_TOP_Z - 0.12
        for xv in (-5.5, -3.0, -0.5):
            m.member((xv, y, z0), (xv, y, z1), 0.11, mat="Kit_SteelBlue")
        m.member((-7.8, y, z0), (-5.62, y, z1), 0.09, mat="Kit_Rust")
        m.member((-5.38, y, z1), (-3.12, y, z0), 0.09, mat="Kit_Rust")
        m.member((-2.88, y, z0), (-0.62, y, z1), 0.09, mat="Kit_Rust")
        m.member((-0.38, y, z1), (1.88, y, z0), 0.09, mat="Kit_Rust")


def _bend_anchors(m):
    """两端北折段的支承：混凝土管墩 ×2 + 托梁 ×2（西端 / 东端对称）。"""
    for bx in (BEND_XW, BEND_XE):
        for dy in (0.2, 2.55):
            m.box((0.6, 0.6, 4.13), at=(bx - 0.3, dy - 0.3, 0), mat="Kit_ConcreteDark")
            for z in (0.9, 2.4):
                m.member((bx - 0.28, dy - 0.28, z), (bx + 0.28, dy + 0.28, z),
                         0.07, mat="Kit_Iron")  # 墩身抱箍
        for ly in (-0.62, 2.05):
            m.member((bx - 0.75, ly, 4.13), (bx + 0.75, ly, 4.13), 0.14, 0.12,
                     mat="Kit_SteelBlue")


# ---------------------------------------------------------------------------
# 管束
# ---------------------------------------------------------------------------

def _run_pts(y, z, climb, ye_w, ye_e):
    """通长管折线：西端北折 →（可选净跨爬升）→ 通长 → 东端北折。"""
    pts = [(BEND_XW, BEND_Y_END, z), (BEND_XW, ye_w, z), (-23.6, y, z)]
    if climb:
        pts += [(-9.0, y, z), (-7.8, y, z + 0.75), (-6.4, y, z + CLIMB),
                (-1.6, y, z + CLIMB), (0.2, y, z + 0.8), (1.4, y, z)]
    pts += [(23.6, y, z), (BEND_XE, ye_e, z), (BEND_XE, BEND_Y_END, z)]
    return pts


def _pipe_lines(m):
    """主管束铺装 + 鞍座 + 焊缝环 + 端部法兰；断管（顶层 I / 底层 E）交给 _decay。"""
    for k, (y, r, mat) in enumerate(PIPES_BOT):
        if k == PIPE_BROKEN_BOT:
            continue
        z = _z_bot(r)
        m.tube(_run_pts(y, z, True, _ye(y), _ye(y)), r, seg=_seg_for(r), mat=mat)
    for k, (y, r, mat, dz) in enumerate(PIPES_TOP):
        if k in (PIPE_BROKEN_TOP, PIPE_SAG_TOP):
            continue
        z = _z_top(r, dz)
        m.tube(_run_pts(y, z, False, _ye(y), _ye(y)), r, seg=_seg_for(r), mat=mat)
    # 悬垂管（顶层 K）：两端仍挂托上，中段 V 形下坠（跨中 x≈21.5，避开柱位横梁）
    y, r, mat, dz = PIPES_TOP[PIPE_SAG_TOP]
    z = _z_top(r, dz)
    base = [(BEND_XW, BEND_Y_END, z), (BEND_XW, _ye(y), z), (-23.6, y, z),
            (19.7, y, z), (21.5, y + 0.07, z - 0.5), (23.3, y + 0.03, z),
            (23.6, y, z), (BEND_XE, _ye(y), z), (BEND_XE, BEND_Y_END, z)]
    m.tube(base, r, seg=_seg_for(r), mat=mat)
    _pipe_fittings(m)


def _pipe_fittings(m):
    """鞍座（柱位一道）+ 焊缝环（6 m 错柱位）+ 顶层管两端盲板法兰。"""
    for k, (y, r, mat) in enumerate(PIPES_BOT):
        if k == PIPE_BROKEN_BOT:
            continue
        z = _z_bot(r)
        for x in COL_X:
            if x == -8.0:
                continue  # 爬升起坡柱位不放（鞍座会插进爬升段管身）
            _shoe(m, x, y, BEAM_BOT_TOP, z, r)
    for k, (y, r, mat, dz) in enumerate(PIPES_TOP):
        if k == PIPE_BROKEN_TOP:
            continue
        z = _z_top(r, dz)
        for x in COL_X:
            _shoe(m, x, y, BEAM_TOP_TOP, z, r)
    # 断管的存活段照常放鞍座
    yt, rt, mt, dzt = PIPES_TOP[PIPE_BROKEN_TOP]
    for x in (-20.0, -14.0, -8.0, 2.0, 8.0, 14.0, 20.0, 26.0):
        _shoe(m, x, yt, BEAM_TOP_TOP, _z_top(rt, dzt), rt)
    ye_b, re_b, me_b = PIPES_BOT[PIPE_BROKEN_BOT]
    for x in (-20.0, -14.0, -8.0, 2.0, 8.0, 14.0, 20.0):
        _shoe(m, x, ye_b, BEAM_BOT_TOP, _z_bot(re_b), re_b)
    # 焊缝环（水平段，避开爬升区与断口）
    ring_xs = (-22.0, -16.0, -10.0, 5.0, 9.0, 15.0, 21.0)
    for ki, (y, r, mat) in enumerate(PIPES_BOT):
        for x in ring_xs:
            if ki == PIPE_BROKEN_BOT and -13.0 < x < -10.0:
                continue  # 断口段不放环
            _weld_ring(m, r, (x, y, _z_bot(r)), axis="x", mat="Kit_Iron")
    for ki, (y, r, mat, dz) in enumerate(PIPES_TOP):
        for x in ring_xs:
            if ki == PIPE_BROKEN_TOP and -4.4 < x < -0.4:
                continue  # 断口段不放环
            if ki == PIPE_SAG_TOP and 20.0 < x < 23.0:
                continue  # 悬垂段不放环
            _weld_ring(m, r, (x, y, _z_top(r, dz)), axis="x", mat="Kit_Iron")
    # 顶层管两端（出界处）盲板法兰
    for ki, (y, r, mat, dz) in enumerate(PIPES_TOP):
        if ki == PIPE_BROKEN_TOP:
            continue
        _flange(m, r, (-26.98, y, _z_top(r, dz)), axis="x")
        _flange(m, r, (26.98, y, _z_top(r, dz)), axis="x")


# ---------------------------------------------------------------------------
# 检修走台（北挑）+ 爬梯
# ---------------------------------------------------------------------------

def _low_lines(m):
    """低层小管排（z≈0.5~0.8，净跨处断开 + 盲板法兰）+ 落地 H 支墩 —— 填充梁下。"""
    runs = ((-24.0, -9.5), (3.0, 24.0))
    for (y, r, z0, mat) in ((-0.9, 0.09, 0.55, "Kit_Rust"),
                            (0.3, 0.12, 0.82, "Kit_SteelBlue")):
        for (a, b) in runs:
            m.tube([(a, y, z0), (b, y, z0)], r, seg=12, mat=mat)
            _flange(m, r, (a + 0.02, y, z0), axis="-x", bolts=6)
            _flange(m, r, (b - 0.02, y, z0), axis="x", bolts=6)
            for xr in (a + 3.0, (a + b) / 2.0, b - 3.0):
                _weld_ring(m, r, (xr, y, z0), axis="x", mat="Kit_Iron")
    # H 支墩 ×6（西段 3 / 东段 3，避开泵组/阀站/管墩）
    for x in (-22.0, -16.0, -10.0, 6.2, 15.3, 22.0):
        for yy in (-1.1, 0.5):
            m.box((0.2, 0.2, 0.05), at=(x, yy, 0), mat="Kit_ConcreteDark")
            m.member((x, yy, 0.05), (x, yy, 0.32), 0.08, mat="Kit_Iron")
        m.member((x, -1.15, 0.32), (x, 0.55, 0.32), 0.1, 0.08, mat="Kit_Iron")
        for (py, pz, pr) in ((-0.9, 0.55, 0.09), (0.3, 0.82, 0.12)):
            _shoe(m, x, py, 0.37, pz, pr)


def _cable_tray(m):
    """电缆桥架（y=0，底层梁下 3.8，净跨断开）+ 柱位吊杆 —— 廊下中线层次。"""
    z = 3.8
    for (a, b) in ((-25.7, -8.3), (2.3, 25.7)):
        mid = (a + b) / 2.0
        ln = b - a
        m.box((ln, 0.28, 0.06), at=(mid, -0.14, z), mat="Kit_Iron")
        m.box((ln, 0.03, 0.1), at=(mid, -0.155, z + 0.06), mat="Kit_Iron")
        m.box((ln, 0.03, 0.1), at=(mid, 0.125, z + 0.06), mat="Kit_Iron")
        # 端板（净跨断口 / 出界端）
        for xe in (a + 0.03, b - 0.03):
            m.box((0.04, 0.28, 0.12), at=(xe, -0.14, z - 0.01), mat="Kit_RustDark")
    for x in COL_X:
        if x == -8.0:
            continue
        m.member((x, 0.0, z - 0.02), (x, 0.0, BEAM_BOT_Z - 0.16), 0.05,
                 mat="Kit_RustDark")


def _walkway(m):
    z = BEAM_BOT_TOP - 0.05
    spans = [(-25.7, -20.3), (-19.7, -14.3), (-13.7, -8.3), (2.3, 7.7),
             (8.3, 13.7), (14.3, 19.7), (20.3, 25.7)]
    for (x0, x1) in spans:
        mid = (x0 + x1) / 2.0
        m.grating((x1 - x0, 0.7), at=(mid, 1.95, z), thick=0.05, bar=0.05,
                  mat="Kit_Iron", direction="x")
        # 支承挑梁：从底层纵梁挑出
        for xs in (x0 + 0.3, mid, x1 - 0.3):
            m.member((xs, 1.56, z - 0.03), (xs, 2.3, z - 0.03), 0.08, 0.05,
                     mat="Kit_Rust")
        # 外缘栏杆
        m.railing([(x0, 2.26), (x1, 2.26)], h=1.05, at=(0, 0, z), post_step=2.8,
                  mat="Kit_Rust")
    # 净跨两侧板端封杆
    for xs in (-8.25, 2.25):
        m.railing([(xs, 1.62), (xs, 2.26)], h=1.05, at=(0, 0, z), post_step=3.0,
                  mat="Kit_Rust")
    # 地面 → 走台爬梯（净跨以东）
    m.ladder(at=(6.5, 2.33, 0), h=BEAM_BOT_TOP, w=0.5, rz=0.0, mat="Kit_Rust")


# ---------------------------------------------------------------------------
# 阀站 #1（底层管汇，X≈-18）
# ---------------------------------------------------------------------------

def _valve_manifold_low(m):
    # 三根下引支管 + 阀 + 接主管处法兰
    for x, (y0, r0) in ((-18.6, (PIPES_BOT[0][0], PIPES_BOT[0][1])),
                        (-18.0, (PIPES_BOT[1][0], PIPES_BOT[1][1])),
                        (-17.4, (PIPES_BOT[2][0], PIPES_BOT[2][1]))):
        pts = [(x, y0, _z_bot(r0)), (x, y0 - 0.3, 3.9), (x, -1.7, 2.6),
               (x, -1.7, 1.14)]
        m.tube(pts, 0.075, seg=10, mat="Kit_SteelBlue")
        _flange(m, 0.075, (x, y0 - r0 - 0.03, _z_bot(r0)), axis="y")
        _valve_vert(m, x, -1.7, 2.15, s=0.9, body_mat="Kit_SteelPale")
    # 汇管 + 端法兰 + 支架
    m.tube([(-19.4, -1.7, 1.0), (-16.6, -1.7, 1.0)], 0.16, seg=14,
           mat="Kit_SteelBlue")
    _flange(m, 0.16, (-19.35, -1.7, 1.0), axis="-x")
    _flange(m, 0.16, (-16.65, -1.7, 1.0), axis="x")
    for xs in (-19.1, -16.9):
        m.member((xs - 0.05, -1.75, 0), (xs + 0.05, -1.65, 0.86), 0.09,
                 mat="Kit_Rust")
        m.member((xs, -1.7, 0.5), (xs + 0.45, -1.7, 0.06), 0.06, mat="Kit_Rust")
        m.box((0.5, 0.22, 0.06), at=(xs, -1.7, 0.86), mat="Kit_Iron")
    # 阀站旁仪表：立柱 + 表盘（嵌玻璃小窗）
    m.member((-19.7, -1.45, 0), (-19.7, -1.45, 1.75), 0.05, mat="Kit_Iron")
    m.box((0.36, 0.12, 0.44), at=(-19.88, -1.51, 1.75), mat="Kit_SteelPale")
    m.box((0.22, 0.02, 0.15), at=(-19.81, -1.58, 1.87), mat="Kit_GlassDark")


# ---------------------------------------------------------------------------
# 泵组 ×3
# ---------------------------------------------------------------------------

def _pump(m, x_p):
    yb = -1.9
    # 混凝土底座 + 钢底架（覆盖泵壳到电机）
    m.box((2.2, 1.0, 0.24), at=(x_p - 0.8, yb, 0), mat="Kit_ConcreteDark")
    m.box((2.0, 0.8, 0.07), at=(x_p - 0.8, yb, 0.24), mat="Kit_SteelBlue")
    # 泵壳（立式蜗壳鼓形）
    x_s = x_p
    prof = [(0.10, 0.0), (0.24, 0.02), (0.30, 0.12), (0.30, 0.26), (0.22, 0.36),
            (0.10, 0.40), (0.0, 0.42)]
    m.revolve(prof, at=(x_s, yb, 0.31), seg=18, mat="Kit_SteelBlue")
    m.box((0.16, 0.2, 0.14), at=(x_s + 0.26, yb, 0.55), mat="Kit_Iron")  # 蜗舌座
    # 出口管：壳顶正上竖引 → 折向北接管 A（加粗、蓝灰，与主管桥连成体系）
    zA = _z_bot(PIPES_BOT[0][1])
    yA = PIPES_BOT[0][0]
    rA = PIPES_BOT[0][1]
    m.tube([(x_s, yb, 0.70), (x_s, yb, 3.6), (x_s, -1.55, 4.1),
            (x_s, yA - rA - 0.04, zA)], 0.12, seg=14, mat="Kit_SteelBlue")
    _flange(m, 0.12, (x_s, yA - rA - 0.02, zA), axis="y")
    _weld_ring(m, 0.12, (x_s, yb, 2.2), axis="z", mat="Kit_Iron")
    _weld_ring(m, 0.12, (x_s, yb, 3.3), axis="z", mat="Kit_Iron")
    # 吸入管：管 C 下引 → 折平 → 接泵壳东侧入口
    yC, rC = PIPES_BOT[2][0], PIPES_BOT[2][1]
    zC = _z_bot(rC)
    xe = x_s + 0.75
    m.tube([(xe, yC, zC - rC + 0.02), (xe, yC, 4.2), (xe, yb, 3.3),
            (xe, yb, 0.5), (x_s + 0.26, yb, 0.5)], 0.1, seg=12, mat="Kit_Rust")
    _flange(m, 0.1, (xe, yC, zC - rC - 0.01), axis="z")
    _weld_ring(m, 0.1, (xe, yb, 1.9), axis="z", mat="Kit_Iron")
    # 电机（卧式圆柱 + 翅片环 + 端盖 + 接线盒）
    mx0 = x_s - 1.32
    m.cyl(0.17, 0.5, at=(mx0, yb, 0.34), ry=math.pi / 2, seg=14, mat="Kit_Rust")
    for i in range(5):
        m.cyl(0.19, 0.016, at=(mx0 + 0.08 + i * 0.08, yb, 0.34), ry=math.pi / 2,
              seg=14, mat="Kit_Rust")
    m.cyl(0.19, 0.05, at=(mx0 - 0.05, yb, 0.34), ry=math.pi / 2, seg=14,
          mat="Kit_Iron")
    m.box((0.16, 0.16, 0.1), at=(mx0 + 0.16, yb, 0.55), mat="Kit_Iron")
    # 联轴器护罩（黄漆）
    m.cyl(0.155, 0.3, at=(x_s - 0.79, yb, 0.37), ry=math.pi / 2, seg=12,
          mat="Kit_PaintYellow")
    # 泵脚撑
    m.member((x_s - 0.18, yb - 0.14, 0.31), (x_s - 0.18, yb - 0.14, 0.1), 0.08,
             mat="Kit_Iron")
    m.member((x_s + 0.14, yb - 0.14, 0.31), (x_s + 0.14, yb - 0.14, 0.1), 0.08,
             mat="Kit_Iron")


def _pump_train(m):
    for x_p in (4.5, 8.5, 12.5):
        _pump(m, x_p)


# ---------------------------------------------------------------------------
# 阀站 #2（顶层 + 操作平台）与缓冲罐管汇
# ---------------------------------------------------------------------------

def _valve_platform_top(m):
    yG, rG = PIPES_TOP[0][0], PIPES_TOP[0][1]
    zG = _z_top(rG)
    # 阀 + 法兰对（管 G 通长，法兰直接贴管身）
    _valve_horiz(m, 11.0, yG, zG, s=1.0, body_mat="Kit_SteelBlue")
    _flange(m, rG, (10.68, yG, zG), axis="-x")
    _flange(m, rG, (11.32, yG, zG), axis="x")
    # 南挑操作平台（跨在南列顶层梁上）
    m.platform((2.4, 1.6), BEAM_TOP_TOP, at=(11.2, -1.4), thick=0.08,
               mat="Kit_Iron", rail_h=1.1, rail_sides="nsw", rail_mat="Kit_Rust")
    # 平台 → 顶层梁的支撑斜杆
    m.member((10.1, -2.0, BEAM_TOP_TOP - 0.1), (10.1, -1.55, BEAM_BOT_Z + 0.1),
             0.07, mat="Kit_Rust")
    m.member((12.3, -2.0, BEAM_TOP_TOP - 0.1), (12.3, -1.55, BEAM_BOT_Z + 0.1),
             0.07, mat="Kit_Rust")
    # 地面 → 平台带笼爬梯
    m.ladder(at=(12.55, -1.4, 0), h=BEAM_TOP_TOP, w=0.5, rz=math.pi / 2,
             mat="Kit_Rust", cage=True)
    # 平台上的小仪表
    m.member((10.2, -1.55, BEAM_TOP_TOP), (10.2, -1.55, BEAM_TOP_TOP + 0.5), 0.04,
             mat="Kit_Iron")
    m.box((0.32, 0.12, 0.4), at=(10.04, -1.61, BEAM_TOP_TOP + 0.5),
          mat="Kit_SteelPale")
    m.box((0.2, 0.02, 0.13), at=(10.1, -1.68, BEAM_TOP_TOP + 0.62),
          mat="Kit_GlassDark")


def _surge_manifold(m):
    """缓冲罐管汇：卧式罐 + 鞍座 + 3 根支管上接管 D（各带阀）。"""
    yD, rD = PIPES_BOT[3][0], PIPES_BOT[3][1]
    zD = _z_bot(rD)
    # 卧式罐（沿 X）
    m.cyl(0.35, 1.4, at=(16.3, 0.6, 1.2), ry=math.pi / 2, seg=16, mat="Kit_SteelPale")
    m.revolve([(0.0, 0.0), (0.2, 0.02), (0.33, 0.14), (0.35, 0.26), (0.0, 0.38)],
              at=(16.3 - 0.35, 0.6, 0.94), seg=16, mat="Kit_SteelPale", rz=0.0)
    # 罐身加强环 ×2
    for xr in (16.75, 17.25):
        m.cyl(0.37, 0.05, at=(xr, 0.6, 1.175), ry=math.pi / 2, seg=16, mat="Kit_Iron")
    _flange(m, 0.3, (16.26, 0.6, 1.2), axis="-x")
    _flange(m, 0.3, (17.74, 0.6, 1.2), axis="x")
    # 鞍座 + 混凝土垫
    for xs in (16.55, 17.45):
        m.box((0.16, 0.5, 0.5), at=(xs, 0.6, 0.7), mat="Kit_Iron")
        m.box((0.3, 0.62, 0.2), at=(xs, 0.6, 0.5), mat="Kit_ConcreteDark")
    # 3 根支管：罐顶 → 管 D 底，各带阀
    for x in (16.6, 17.0, 17.4):
        m.tube([(x, 0.6, 1.5), (x, 0.53, 2.4), (x, yD, zD - rD + 0.02)], 0.065,
               seg=10, mat="Kit_SteelPale")
        _flange(m, 0.065, (x, 0.6, 1.53), axis="z")
        _valve_vert(m, x, 0.5, 3.0, s=0.8)
        _flange(m, 0.065, (x, yD, zD - rD - 0.01), axis="z")


# ---------------------------------------------------------------------------
# 废弃感：断裂 / 坠落 / 补板 / 保温棉 / 倒架 / 散管
# ---------------------------------------------------------------------------

def _decay(m):
    yI, rI = PIPES_TOP[PIPE_BROKEN_TOP][0], PIPES_TOP[PIPE_BROKEN_TOP][1]
    zI = _z_top(rI)
    # --- 断裂 #1：顶层管 I，断口 X≈-3.4 / -1.9，中段坠地 -------------------
    west = [(BEND_XW, BEND_Y_END, zI), (BEND_XW, _ye(yI), zI), (-23.6, yI, zI),
            (-3.4, yI, zI)]
    east = [(-1.9, yI + 0.02, zI), (23.6, yI, zI), (BEND_XE, _ye(yI), zI),
            (BEND_XE, BEND_Y_END, zI)]
    m.tube(west, rI, seg=_seg_for(rI), mat=PIPES_TOP[PIPE_BROKEN_TOP][2])
    m.tube(east, rI, seg=_seg_for(rI), mat=PIPES_TOP[PIPE_BROKEN_TOP][2])
    _jag(m, (-3.4, yI, zI), rI, n=8)
    _jag(m, (-1.9, yI + 0.02, zI), rI, n=7)
    # 坠落段：斜落砸地 + 拖行 + 尾部微翘（亮锈色，落点甩向南侧近镜头处）
    fallen = [(-3.4, yI, zI), (-3.7, yI + 0.1, 4.6), (-4.2, 0.3, 2.2),
              (-4.6, 0.6, 0.5), (-4.7, 0.75, 0.18), (-2.6, 0.95, 0.15),
              (-1.0, 0.8, 0.3)]
    m.tube(fallen, rI, seg=_seg_for(rI), mat="Kit_Rust")
    _jag(m, fallen[-1], rI, n=6, mat="Kit_Rust")
    _jag(m, (0.3, 1.4, 0.17), rI, n=5, mat="Kit_Rust")
    # --- 断裂 #2：底层管 E，断口 X≈-12 --------------------------------------
    k = PIPE_BROKEN_BOT
    yE, rE, mE = PIPES_BOT[k]
    zE = _z_bot(rE)
    w2 = [(BEND_XW, BEND_Y_END, zE), (BEND_XW, _ye(yE), zE), (-23.6, yE, zE),
          (-13.0, yE, zE), (-12.2, yE + 0.03, zE + 0.38)]
    e2 = [(-11.55, yE - 0.02, zE - 0.22), (-11.0, yE, zE),
          (-9.0, yE, zE), (-7.8, yE, zE + 0.75), (-6.4, yE, zE + CLIMB),
          (-1.6, yE, zE + CLIMB), (0.2, yE, zE + 0.8), (1.4, yE, zE),
          (23.6, yE, zE), (BEND_XE, _ye(yE), zE), (BEND_XE, BEND_Y_END, zE)]
    m.tube(w2, rE, seg=_seg_for(rE), mat=mE)
    m.tube(e2, rE, seg=_seg_for(rE), mat=mE)
    _jag(m, w2[-1], rE, n=6)
    _jag(m, e2[0], rE, n=6)
    # 坠地中段：横躺在断口下方
    mid2 = [(-12.6, yE + 0.15, 3.4), (-12.2, yE + 0.4, 1.7), (-11.7, yE + 0.6, 0.24),
            (-10.2, yE + 0.8, 0.14)]
    m.tube(mid2, rE, seg=_seg_for(rE), mat="Kit_RustDark")
    _jag(m, mid2[0], rE, n=5, mat="Kit_Rust")
    _jag(m, mid2[-1], rE, n=5, mat="Kit_Rust")
    # --- 锈蚀补板 ------------------------------------------------------------
    _patch(m, (-14.13, -1.74, 1.6), (0.02, 0.5, 0.95), rz=0.06, mat="Kit_RustDark")
    _patch(m, (-14.13, -1.72, 2.3), (0.02, 0.42, 0.6), rz=-0.1, mat="Kit_Rust")
    _patch(m, (6.0, PIPES_BOT[0][0] - 0.22, _z_bot(PIPES_BOT[0][1]) + 0.12),
           (0.9, 0.02, 0.5), rx=0.3, mat="Kit_Rust")
    _patch(m, (20.0, PIPES_TOP[0][0] - 0.05, _z_top(PIPES_TOP[0][1]) + 0.16),
           (0.8, 0.02, 0.42), rx=0.16, mat="Kit_RustDark")
    _patch(m, (-14.14, -1.5, 1.62), (0.03, 0.2, 0.2), mat="Kit_RustDark")
    # --- 悬挂残破保温棉 ------------------------------------------------------
    _lagging(m, -20.6, PIPES_BOT[1][0], _z_bot(PIPES_BOT[1][1]) - PIPES_BOT[1][1],
             4, mats=("Kit_ConcreteLight", "Kit_RockMid"))
    _lagging(m, 14.6, PIPES_TOP[1][0], _z_top(PIPES_TOP[1][1]) - PIPES_TOP[1][1],
             3, mats=("Kit_ConcreteLight", "Kit_RockMid"))
    # --- 倒伏门架 ------------------------------------------------------------
    x0 = -17.5
    m.member((x0, 1.1, 0.1), (x0 + 1.7, 1.16, 0.16), 0.16, mat="Kit_Rust")
    m.member((x0, 1.1, 0.14), (x0 - 0.2, 0.3, 0.2), 0.12, mat="Kit_Rust")
    m.member((x0 + 1.7, 1.16, 0.18), (x0 + 1.95, 2.0, 0.55), 0.12, mat="Kit_Rust")
    m.member((x0 + 0.6, 1.35, 0.12), (x0 + 1.5, 2.1, 0.35), 0.07, mat="Kit_RustDark")
    # --- 地面散管堆 ----------------------------------------------------------
    for (sx, sy, sr, sl, mat, tilt) in ((-16.2, 1.65, 0.16, 2.6, "Kit_Rust", 0.05),
                                        (-15.9, 1.62, 0.13, 2.2, "Kit_RustDark", -0.08),
                                        (-16.05, 1.95, 0.1, 1.8, "Kit_SteelPale", 0.12)):
        z0 = 0.13 if sr < 0.15 else 0.3
        m.tube([(sx - sl / 2, sy, z0), (sx + sl / 2, sy + tilt * 2, z0 + abs(tilt) * 3)],
               sr, seg=12, mat=mat)


# ---------------------------------------------------------------------------

def build(m):
    """管廊带入口：preview_module.py 调这里。"""
    _foundations_and_frames(m)
    _longitudinals(m)
    _bend_anchors(m)
    _pipe_lines(m)
    _low_lines(m)
    _cable_tray(m)
    _walkway(m)
    _valve_manifold_low(m)
    _pump_train(m)
    _valve_platform_top(m)
    _surge_manifold(m)
    _decay(m)
