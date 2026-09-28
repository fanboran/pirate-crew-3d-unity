# -*- coding: utf-8 -*-
"""chemplant/mod_building.py —— BLOCK-04 厂区旁楼：4 层办公楼 + 单层仓库附属（废弃化工厂）。

【是什么】东南分区 `AREAS['building']`（本地盒 22×11×15）的镜头主看面建筑：
    · 主楼   4 层，面宽 15 m（X -11..+4）× 进深 10 m（Y -5..+5）× 高 4×3.5=14 m。
             混凝土预制板立面（ConcreteLight）+ 层间腰线带 + 竖向板缝条；
             逐层窗带走**真实洞口**（内凹 0.25 m：ConcreteDark 内衬 + GlassDark 玻璃 +
             SteelPale 上下窗框/中挺/亮子横档），每层南向 7 樘；
             损毁变体：玻璃缺失（歪中挺 + 窗台玻璃碴）、木板封窗（斜缝木板 + 木档带）、
             洞口边缘掉角（洞旁真实缺口 + 剥露暗面）。
    · 入口   -Y 主立面凹龛（进深 0.7 m）：WoodDark 双开门（嵌板/推板/踢脚/把手）+
             门楣 + 亮子玻璃 + 3 级台阶（外扩放宽）+ 门廊地台 + 悬挑雨棚
             （4 根细柱 + 板 + 褪色 PaintYellow 边条，外缘越界 1.4 m ≤ 1.5 上限）+ 门牌板。
    · 屋顶   女儿墙一圈（西南角坍塌成锯齿矮墩 + 屋面散落碎块，西檐留爬梯出口）+
             轴流风机×2（护圈 + 叶轮，一台风蚀锈重掉叶）+ 卧式水箱（支腿 + 锈箍 + 检修口）+
             排气鹅颈管 + 出屋面检修间（低矮化：分区顶 15 m 硬限，屋顶设备全部压在
             14~15 m 之间）+ 西墙带笼爬梯（cage）直上屋顶 + 梯顶过渡抓栏 + 屋面格栅板。
    · 仓库   贴主楼东侧（X +4.3..+11，接缝 0.3 m 用钢板披水搭接），单层大跨 6.7×10 m，
             檐口 6 m，SteelBlue 波纹墙板 + SteelPale/Rust 交替竖缝条，人字顶（坡度 ~21°）+
             锈脊盖 + 坡面补板，3 樘褪色 PaintYellow 卷帘门（门轨/门楣盒/锈蚀分化），
             北墙高窗木封板，东墙破洞（暗内壁 + 翻边板），山墙百叶通风口。
    · 管线   西墙工艺立管（穿墙法兰 + 阀门手轮 + 保温段，朝 -X/-Y）+
             南檐落水管（披水铁 + 箍带 + 鞋头）+ 东墙出管（朝 +X 下翻）+ 地面倒伏排水管。
    · 风化   窗台锈水痕 / 女儿墙长锈痕 / 门牌与配电箱下滴痕 / 水泥补板（部分带螺栓排）/
             深色污渍带，全走薄板贴面（0.02~0.03 m 出头），零贴图零金属度。

【坐标系】原点 = 分区中心地面投影，地面 z=0，+Z 上，+Y 北（背面），-Y 朝大路与镜头，+X 东。
主体全部收在本地盒 X[-11,+11] Y[-5.5,+5.5] Z[0,15] 内；越界件（雨棚 1.4 / 台阶 0.64 /
倒管 1.05 / 东西立管 1.1）均 ≤ 1.5 m。

自检：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/chemplant/preview_module.py -- building
"""

import math
import random

import kit_common as K

# ---------------------------------------------------------------------------
# 布局常量（本地系）
# ---------------------------------------------------------------------------
MX0, MX1 = -11.0, 4.0                    # 主楼东西外墙线
MY0, MY1 = -5.0, 5.0                     # 主楼南北外墙线
WT = 0.4                                 # 主楼墙厚
FL_H = 3.5
N_FLOORS = 4
H_ROOF = FL_H * N_FLOORS                 # 14.0

BX0, BX1 = -5.4, -1.6                    # 入口凹龛（主立面）沿 X
BY_REC = -4.3                            # 凹龛后壁外墙面
DX_C = -3.5                              # 门洞中心

# 仓库附属
WX0, WX1 = 4.3, 11.0                     # 仓库东西外墙线
WY0, WY1 = -5.5, 4.5                     # 仓库南北外墙线
W_EAVE = 6.0                             # 檐口
W_RIDGE_X = (WX0 + WX1) / 2.0            # 7.65
W_RIDGE_Z = 7.3
W_DOOR_W = 1.7
W_DOOR_XS = (5.7, 7.65, 9.6)             # 卷帘门中心

# 窗带
WIN_W, WIN_SILL, WIN_H = 1.1, 0.95, 1.7
S_XS = [-9.85, -7.92, -5.98, -4.05, -2.12, -0.18, 1.75]   # 南/北每层 7 樘
W_YS = [-3.8, -1.63, 0.53, 2.7]                            # 西墙每层 4 樘
E_YS = [-3.0, 0.0, 3.0]                                    # 东墙 2~3 层各 3 樘

# 损毁登记：(朝向, 层, 樘号)；掉角 = (朝向, 层, 樘号) -> 'l'/'r'
BROKEN = {("s", 1, 2), ("s", 2, 5), ("n", 1, 3), ("w", 2, 2), ("e", 2, 1)}
BOARDED = {("s", 3, 0), ("n", 3, 2), ("w", 1, 1)}
CHIP = {("s", 2, 3): "r", ("n", 3, 6): "l"}


# ---------------------------------------------------------------------------
# 通用小件
# ---------------------------------------------------------------------------

def _wall_x(m, x0, x1, y_out, y_in, z0, z1, holes, mat):
    """沿 X 展开的墙（真实洞口）。外墙面 y_out，墙体内长到 y_in。holes=[(cx,w,zs,ze)]。"""
    t = abs(y_in - y_out)
    yc = (y_out + y_in) / 2.0
    edges = sorted(set([x0, x1] + [e for cx, w, zs, ze in holes
                                   for e in (cx - w / 2.0, cx + w / 2.0)]))
    for a, b in zip(edges[:-1], edges[1:]):
        if b - a < 1e-4:
            continue
        mid = (a + b) / 2.0
        if any(cx - w / 2.0 + 1e-4 < mid < cx + w / 2.0 - 1e-4
               for cx, w, zs, ze in holes):
            continue
        m.box((b - a, t, z1 - z0), (mid, yc, z0), mat=mat)
    for cx, w, zs, ze in holes:
        if zs - z0 > 1e-4:
            m.box((w, t, zs - z0), (cx, yc, z0), mat=mat)
        if z1 - ze > 1e-4:
            m.box((w, t, z1 - ze), (cx, yc, ze), mat=mat)


def _wall_y(m, y0, y1, x_out, x_in, z0, z1, holes, mat):
    """沿 Y 展开的墙（真实洞口）。外墙面 x_out，墙体内长到 x_in。holes=[(cy,w,zs,ze)]。"""
    t = abs(x_in - x_out)
    xc = (x_out + x_in) / 2.0
    edges = sorted(set([y0, y1] + [e for cy, w, zs, ze in holes
                                   for e in (cy - w / 2.0, cy + w / 2.0)]))
    for a, b in zip(edges[:-1], edges[1:]):
        if b - a < 1e-4:
            continue
        mid = (a + b) / 2.0
        if any(cy - w / 2.0 + 1e-4 < mid < cy + w / 2.0 - 1e-4
               for cy, w, zs, ze in holes):
            continue
        m.box((t, b - a, z1 - z0), (xc, mid, z0), mat=mat)
    for cy, w, zs, ze in holes:
        if zs - z0 > 1e-4:
            m.box((t, w, zs - z0), (xc, cy, z0), mat=mat)
        if z1 - ze > 1e-4:
            m.box((t, w, z1 - ze), (xc, cy, ze), mat=mat)


def _window(m, face, c, z0, style, rng):
    """一樘窗（配墙已开好的洞口）：内衬 + 背板 + 玻璃/木板 + 钢框 + 外挑窗台/过梁。
    face ∈ 's n e w'，c = 沿面坐标。style ∈ 'glass broken boarded'。"""
    w, h = WIN_W, WIN_H
    zs = z0 + WIN_SILL
    if face in ("s", "n"):
        yf = MY0 if face == "s" else MY1
        si = 1.0 if face == "s" else -1.0          # +墙内方向

        def B(sa, sd, sz, pa, pd, pz, mt, **kw):
            m.box((sa, sd, sz), (pa, yf + si * pd, pz), mat=mt, **kw)
    else:
        xf = MX0 if face == "w" else MX1
        si = 1.0 if face == "w" else -1.0

        def B(sa, sd, sz, pa, pd, pz, mt, **kw):
            m.box((sd, sa, sz), (xf + si * pd, pa, pz), mat=mt, **kw)

    d = 0.25                                        # 洞口内凹深
    B(0.05, d, h, c - w / 2.0 + 0.025, d / 2.0, zs, "Kit_ConcreteDark")
    B(0.05, d, h, c + w / 2.0 - 0.025, d / 2.0, zs, "Kit_ConcreteDark")
    B(w, d, 0.05, c, d / 2.0, zs + h - 0.05, "Kit_ConcreteDark")
    B(w, d, 0.05, c, d / 2.0, zs, "Kit_ConcreteDark")
    B(w - 0.06, 0.05, h - 0.06, c, 0.45, zs + 0.03, "Kit_ConcreteDark")   # 洞内背板
    B(w - 0.02, 0.07, 0.09, c, 0.10, zs + 0.02, "Kit_SteelPale")          # 下窗框
    B(w - 0.02, 0.07, 0.09, c, 0.10, zs + h - 0.11, "Kit_SteelPale")      # 上窗框
    B(w - 0.02, 0.05, 0.05, c, 0.10, zs + h * 0.62, "Kit_SteelPale")      # 亮子横档
    if style == "glass":
        B(0.05, 0.05, h - 0.24, c, 0.10, zs + 0.12, "Kit_SteelPale")      # 中挺
        B(w - 0.10, 0.04, h - 0.14, c, 0.27, zs + 0.07, "Kit_GlassDark")
        for sx in (-1.0, 1.0):                                            # 亮子芯条
            B(0.04, 0.05, h * 0.30, c + sx * w / 6.0, 0.10, zs + h * 0.62 + 0.02,
              "Kit_SteelPale")
    elif style == "broken":
        B(0.05, 0.05, h - 0.30, c + 0.08, 0.10, zs + 0.10, "Kit_SteelPale", rz=0.12)
        for _ in range(3):                                             # 窗台残玻璃碴
            B(0.10, 0.02, 0.15 + 0.1 * rng.random(), c + rng.uniform(-0.4, 0.4),
              0.24, zs + 0.01, "Kit_GlassDark", rz=rng.uniform(-0.6, 0.6))
    elif style == "boarded":
        pw = (w - 0.04) / 4.0
        for k in range(4):
            B(pw - 0.025, 0.05, h - 0.04, c - w / 2.0 + 0.02 + pw * (k + 0.5),
              0.09, zs + 0.02, "Kit_WoodMid", rz=rng.uniform(-0.05, 0.05))
        B(w - 0.02, 0.05, 0.10, c, 0.13, zs + 0.5, "Kit_WoodDark")
        B(w - 0.02, 0.05, 0.10, c, 0.13, zs + h - 0.6, "Kit_WoodDark")
    so = -si                                           # 朝外
    if face in ("s", "n"):
        m.box((w + 0.26, 0.16, 0.09), (c, yf + so * 0.05, zs - 0.10), mat="Kit_ConcreteMid")
        m.box((w + 0.20, 0.10, 0.14), (c, yf + so * 0.03, zs + h), mat="Kit_ConcreteMid")
    else:
        m.box((0.16, w + 0.26, 0.09), (xf + so * 0.05, c, zs - 0.10), mat="Kit_ConcreteMid")
        m.box((0.10, w + 0.20, 0.14), (xf + so * 0.03, c, zs + h), mat="Kit_ConcreteMid")


def _streak_y(m, y_face, side, x, z_top, length, w, mat):
    """竖向锈水/污渍痕（沿 Y 法线的墙面薄板）。side=-1 朝 -Y 出头。"""
    m.box((w, 0.016, length), (x, y_face + side * 0.008, z_top - length), mat=mat)


def _streak_x(m, x_face, side, y, z_top, length, w, mat):
    """竖向锈水/污渍痕（沿 X 法线的墙面薄板）。side=+1 朝 +X 出头。"""
    m.box((0.016, w, length), (x_face + side * 0.008, y, z_top - length), mat=mat)


def _patch_y(m, y_face, side, x, z, w, h, mat, bolts=False):
    """沿 Y 法线墙面的补板（薄板出头）。"""
    m.box((w, 0.03, h), (x, y_face + side * 0.015, z), mat=mat)
    if bolts:
        for dx in (-w / 2.0 + 0.09, w / 2.0 - 0.09):
            for dz in (0.09, h - 0.09):
                m.cyl(0.016, 0.035, at=(x + dx, y_face + side * 0.028, z + dz),
                      seg=6, mat="Kit_Iron")


def _patch_x(m, x_face, side, y, z, w, h, mat, bolts=False):
    """沿 X 法线墙面的补板（薄板出头）。"""
    m.box((0.03, w, h), (x_face + side * 0.015, y, z), mat=mat)
    if bolts:
        for dy in (-w / 2.0 + 0.09, w / 2.0 - 0.09):
            for dz in (0.09, h - 0.09):
                m.cyl(0.016, 0.035, at=(x_face + side * 0.028, y + dy, z + dz),
                      seg=6, mat="Kit_Iron")


def _hoop(m, cx, cy, z, r, r_bar, mat, spokes=3):
    """水平圆环（风机护圈/阀门手轮）：闭合管圈 + 辐条。"""
    pts = [(cx + r * math.cos(K.TAU * k / 12.0), cy + r * math.sin(K.TAU * k / 12.0), z)
           for k in range(13)]
    pts.append(pts[0])
    m.tube(pts, r_bar, seg=5, caps=False, mat=mat)
    for k in range(spokes):
        a = math.pi * k / spokes
        m.pipe_member((cx + r * math.cos(a), cy + r * math.sin(a), z),
                      (cx - r * math.cos(a), cy - r * math.sin(a), z),
                      r=r_bar * 0.8, mat=mat)


# ---------------------------------------------------------------------------
# 主楼壳体 + 窗带
# ---------------------------------------------------------------------------

def _main_shell(m, rng):
    light = "Kit_ConcreteLight"
    dark = "Kit_ConcreteDark"
    mid = "Kit_ConcreteMid"

    # -- 南墙（主立面）：1 层含入口凹龛大洞，2~4 层每层 7 樘；2 层 3 号窗右上掉角 --
    for fl in range(N_FLOORS):
        z0 = fl * FL_H
        holes = []
        if fl == 0:
            holes = [(x, WIN_W, WIN_SILL, WIN_SILL + WIN_H)
                     for x in S_XS[:3] + S_XS[4:]]
            holes.append((DX_C, 3.8, 0.0, 3.3))            # 凹龛
        else:
            holes = [(x, WIN_W, z0 + WIN_SILL, z0 + WIN_SILL + WIN_H) for x in S_XS]
        if ("s", fl, 3) in CHIP:                            # 掉角：右肩真实缺口
            holes.append((S_XS[3] + WIN_W / 2.0 + 0.16, 0.32,
                          z0 + WIN_SILL + WIN_H, z0 + WIN_SILL + WIN_H + 0.42))
        _wall_x(m, MX0, MX1, MY0, MY0 + WT, z0, z0 + FL_H, holes, light)
        for i, x in enumerate(S_XS):
            if fl == 0 and i == 3:
                continue                                    # 凹龛位无窗
            key = ("s", fl, i)
            style = ("broken" if key in BROKEN else
                     "boarded" if key in BOARDED else "glass")
            _window(m, "s", x, z0, style, rng)
    m.box((0.26, 0.05, 0.36), (S_XS[3] + WIN_W / 2.0 + 0.16, MY0 + 0.10,
                               2 * FL_H + 2.69), mat=dark)  # 掉角剥露暗面

    # -- 北墙（背面）：每层 7 樘；3 层 6 号窗左肩掉角 --
    for fl in range(N_FLOORS):
        z0 = fl * FL_H
        holes = [(x, WIN_W, z0 + WIN_SILL, z0 + WIN_SILL + WIN_H) for x in S_XS]
        if ("n", fl, 6) in CHIP:
            holes.append((S_XS[6] - WIN_W / 2.0 - 0.16, 0.32,
                          z0 + WIN_SILL + WIN_H, z0 + WIN_SILL + WIN_H + 0.42))
        _wall_x(m, MX0, MX1, MY1, MY1 - WT, z0, z0 + FL_H, holes, light)
        for i, x in enumerate(S_XS):
            key = ("n", fl, i)
            style = ("broken" if key in BROKEN else
                     "boarded" if key in BOARDED else "glass")
            _window(m, "n", x, z0, style, rng)
    m.box((0.26, 0.05, 0.36), (S_XS[6] - WIN_W / 2.0 - 0.16, MY1 - 0.10,
                               3 * FL_H + 3.19), mat=dark)

    # -- 西墙：每层 4 樘 --
    for fl in range(N_FLOORS):
        z0 = fl * FL_H
        holes = [(y, WIN_W, z0 + WIN_SILL, z0 + WIN_SILL + WIN_H) for y in W_YS]
        _wall_y(m, MY0 + WT, MY1 - WT, MX0, MX0 + WT, z0, z0 + FL_H, holes, light)
        for i, y in enumerate(W_YS):
            key = ("w", fl, i)
            style = ("broken" if key in BROKEN else
                     "boarded" if key in BOARDED else "glass")
            _window(m, "w", y, z0, style, rng)

    # -- 东墙（下部被仓库贴住，只做 2~3 层窗） --
    holes = []
    for fl in (2, 3):
        z0 = fl * FL_H
        holes += [(y, WIN_W, z0 + WIN_SILL, z0 + WIN_SILL + WIN_H) for y in E_YS]
    _wall_y(m, MY0 + WT, MY1 - WT, MX1, MX1 - WT, 0.0, H_ROOF, holes, light)
    for fl in (2, 3):
        for i, y in enumerate(E_YS):
            key = ("e", fl, i)
            style = "broken" if key in BROKEN else "glass"
            _window(m, "e", y, fl * FL_H, style, rng)

    # -- 屋面板（封顶，女儿墙坐其上） --
    m.box((MX1 - MX0 - 0.2, MY1 - MY0 - 0.2, 0.15), (-3.5, 0.0, 13.85), mat=dark)

    # -- 层间腰线带 + 预制板横缝 + 竖向板缝条 + 勒脚 --
    for z in (3.4, 6.9, 10.4):
        m.box((MX1 - MX0 + 0.24, 0.08, 0.2), (-3.5, MY0 - 0.04, z), mat=mid)
        m.box((MX1 - MX0 + 0.24, 0.08, 0.2), (-3.5, MY1 + 0.04, z), mat=mid)
        m.box((0.08, MY1 - MY0 + 0.24, 0.2), (MX0 - 0.04, 0.0, z), mat=mid)
        m.box((0.08, MY1 - MY0 + 0.24, 0.2), (MX1 + 0.04, 0.0, z), mat=mid)
    for fl in range(N_FLOORS):
        zj = fl * FL_H + 0.42
        m.box((MX1 - MX0 - 0.2, 0.03, 0.08), (-3.5, MY0 - 0.015, zj), mat=mid)
        m.box((0.03, MY1 - MY0 - 0.9, 0.08), (MX0 - 0.015, 0.0, zj), mat=mid)
    for x, mat in ((-8.9, mid), (-6.95, mid), (-1.15, mid), (0.78, mid)):
        m.box((0.1, 0.05, 13.9), (x, MY0 - 0.025, 0.05), mat=mat)
    for y in (-2.7, 3.5):
        m.box((0.05, 0.1, 13.9), (MX0 - 0.025, y, 0.05), mat=mid)
    # 北/东背面板缝（背面也有预制感；对齐窗间墙，别压窗）
    for x in (-8.9, -5.0, -1.15, 0.78, 3.0):
        m.box((0.1, 0.05, 13.9), (x, MY1 + 0.025, 0.05), mat=mid)
    for y in (-1.5, 1.5):
        m.box((0.05, 0.1, 6.9), (MX1 + 0.025, y, 7.05), mat=mid)
    # 上层窗下预制板（凹面板节奏，S/W/N 三面）
    for fl in (1, 2, 3):
        z0 = fl * FL_H
        for x in S_XS:
            m.box((1.3, 0.025, 0.4), (x, MY0 - 0.0125, z0 + 0.5), mat=mid)
            m.box((1.3, 0.025, 0.4), (x, MY1 + 0.0125, z0 + 0.5), mat=mid)
        for y in W_YS:
            m.box((0.025, 1.3, 0.4), (MX0 - 0.0125, y, z0 + 0.5), mat=mid)
    m.box((MX1 - MX0 + 0.2, 0.1, 0.35), (-3.5, MY0 - 0.05, 0.0), mat=mid)
    m.box((MX1 - MX0 + 0.2, 0.1, 0.35), (-3.5, MY1 + 0.05, 0.0), mat=mid)
    m.box((0.1, MY1 - MY0 + 0.2, 0.35), (MX0 - 0.05, 0.0, 0.0), mat=mid)
    m.box((0.1, MY1 - MY0 + 0.2, 0.35), (MX1 + 0.05, 0.0, 0.0), mat=mid)


# ---------------------------------------------------------------------------
# 入口：凹龛 + 双开门 + 台阶 + 雨棚 + 门牌
# ---------------------------------------------------------------------------

def _entrance(m, rng):
    light = "Kit_ConcreteLight"
    mid = "Kit_ConcreteMid"
    dark = "Kit_ConcreteDark"
    # 凹龛侧壁（返回墙）
    m.box((0.15, 0.7, 3.3), (BX0 - 0.075, -4.65, 0.0), mat=light)
    m.box((0.15, 0.7, 3.3), (BX1 + 0.075, -4.65, 0.0), mat=light)
    # 凹龛后壁（真实门洞 + 亮子洞）
    _wall_x(m, BX0 - 0.15, BX1 + 0.15, BY_REC, BY_REC + 0.3, 0.0, 3.3,
            [(DX_C, 1.9, 0.42, 2.97), (DX_C, 1.9, 3.12, 3.3)], light)
    # 门廊地台 + 3 级台阶（外扩放宽）
    m.box((4.0, 0.72, 0.42), (DX_C, -4.64, 0.0), mat=mid)
    m.box((3.6, 0.38, 0.30), (DX_C, -5.19, 0.0), mat=mid)
    m.box((4.0, 0.38, 0.15), (DX_C, -5.57, 0.0), mat=mid)
    m.box((0.22, 0.76, 0.34), (BX0 - 0.12, -5.38, 0.0), mat=mid)
    m.box((0.22, 0.76, 0.34), (BX1 + 0.12, -5.38, 0.0), mat=mid)
    # 门框
    m.box((0.14, 0.3, 2.55), (DX_C - 0.98, -4.26, 0.42), mat=mid)
    m.box((0.14, 0.3, 2.55), (DX_C + 0.98, -4.26, 0.42), mat=mid)
    m.box((2.24, 0.3, 0.15), (DX_C, -4.26, 2.97), mat=mid)
    # 双开门扇 + 嵌板 + 推板把手 + 踢脚板（嵌板朝观者 -Y 凸出）
    for sx in (-1.0, 1.0):
        cx = DX_C + sx * 0.48
        m.box((0.9, 0.055, 2.5), (cx, -4.245, 0.45), mat="Kit_WoodDark")
        for pz in (0.62, 1.72):
            m.box((0.62, 0.018, 0.72), (cx, -4.29, pz), mat="Kit_WoodMid")
        m.box((0.4, 0.02, 0.55), (cx, -4.28, 1.55), mat="Kit_GlassDark")      # 门玻
        m.box((0.46, 0.015, 0.61), (cx, -4.272, 1.52), mat="Kit_SteelPale")
        m.cyl(0.018, 0.32, at=(DX_C + sx * 0.13, -4.20, 1.02), seg=8,
              mat="Kit_SteelPale")
        m.box((0.09, 0.015, 0.28), (DX_C + sx * 0.13, -4.205, 1.12), mat="Kit_SteelPale")
        m.box((0.84, 0.015, 0.22), (cx, -4.208, 0.48), mat="Kit_SteelPale")
    # 亮子（玻璃 + 桁条）
    m.box((1.74, 0.04, 0.15), (DX_C, -4.24, 3.13), mat="Kit_GlassDark")
    m.box((1.9, 0.05, 0.05), (DX_C, -4.25, 3.10), mat="Kit_SteelPale")
    for sx in (-1.0, 1.0):
        m.box((0.04, 0.04, 0.16), (DX_C + sx * 0.8, -4.25, 3.13), mat="Kit_SteelPale")
    # 雨棚：板 + 三面褪色黄边条 + 4 细柱（前对落地 / 后对落地台）
    m.box((5.0, 2.6, 0.15), (DX_C, -5.6, 3.12), mat=dark)
    m.box((5.02, 0.06, 0.30), (DX_C, -6.87, 2.97), mat="Kit_PaintYellow")
    m.box((0.06, 2.6, 0.30), (DX_C - 2.48, -5.6, 2.97), mat="Kit_PaintYellow")
    m.box((0.06, 2.6, 0.30), (DX_C + 2.48, -5.6, 2.97), mat="Kit_PaintYellow")
    for cx, cy, z0 in ((DX_C - 1.85, -6.6, 0.0), (DX_C + 1.85, -6.6, 0.0),
                       (DX_C - 1.65, -4.55, 0.42), (DX_C + 1.65, -4.55, 0.42)):
        m.cyl(0.055, 3.12 - z0, at=(cx, cy, z0), seg=10, mat="Kit_Iron")
        m.box((0.16, 0.16, 0.04), (cx, cy, z0 - 0.04), mat="Kit_Iron")
    # 门牌板（凹龛侧墙）+ 螺栓 + 黄条
    m.box((0.78, 0.045, 0.4), (-2.35, -4.26, 1.75), mat="Kit_SteelPale")
    m.box((0.66, 0.02, 0.09), (-2.35, -4.235, 1.98), mat="Kit_PaintYellow")
    for dx in (-0.3, 0.3):
        for dz in (0.08, 0.32):
            m.cyl(0.015, 0.025, at=(-2.35 + dx, -4.222, 1.75 + dz), seg=6,
                  mat="Kit_Iron")
    # 雨棚剪刀撑（前柱）+ 凹龛吸顶灯
    for cx in (DX_C - 1.85, DX_C + 1.85):
        m.member((cx, -6.6, 2.0), (cx, -5.95, 3.04), 0.05, 0.04, mat="Kit_Iron")
    m.box((0.18, 0.09, 0.1), (DX_C, -4.36, 3.02), mat="Kit_Iron")
    m.tube([(DX_C, -4.36, 3.12), (DX_C, -4.36, 3.28)], 0.02, seg=6, mat="Kit_Iron")


# ---------------------------------------------------------------------------
# 女儿墙 + 屋顶设备
# ---------------------------------------------------------------------------

def _parapet_roof(m, rng):
    mid = "Kit_ConcreteMid"
    light = "Kit_ConcreteLight"
    dark = "Kit_ConcreteDark"
    # 女儿墙（t=0.22，h=0.5）；西南角坍塌成三段锯齿矮墩；西檐留爬梯口 y[-3.1,-2.0]
    for x0, x1, h in ((-10.9, -10.1, 0.3), (-10.1, -9.3, 0.22), (-9.3, -8.4, 0.38)):
        m.box((x1 - x0, 0.22, h), ((x0 + x1) / 2.0, -4.89, 14.0), mat=mid)
    m.box((12.3, 0.22, 0.5), (-2.25, -4.89, 14.0), mat=mid)              # 南主段
    m.box((14.8, 0.22, 0.5), (-3.5, 4.89, 14.0), mat=mid)                # 北
    m.box((0.22, 1.9, 0.5), (-10.89, -4.05, 14.0), mat=mid)              # 西南段
    m.box((0.22, 7.0, 0.5), (-10.89, 1.5, 14.0), mat=mid)                # 西北段（让爬梯口）
    m.box((0.22, 10.0, 0.5), (3.89, 0.0, 14.0), mat=mid)                 # 东
    # 压顶（坍塌段不给）
    m.box((12.46, 0.36, 0.07), (-2.25, -4.89, 14.5), mat=light)
    m.box((14.96, 0.36, 0.07), (-3.5, 4.89, 14.5), mat=light)
    m.box((0.36, 2.06, 0.07), (-10.89, -4.05, 14.5), mat=light)
    m.box((0.36, 7.16, 0.07), (-10.89, 1.5, 14.5), mat=light)
    m.box((0.36, 10.16, 0.07), (3.89, 0.0, 14.5), mat=light)
    # 坍塌段屋面散落碎块
    for cx, cy, s, rz in ((-9.8, -4.2, 0.5, 0.4), (-9.2, -3.7, 0.3, 1.1),
                          (-10.3, -4.45, 0.34, 2.0)):
        m.box((s, s * 0.8, 0.16), (cx, cy, 14.0), rz=rz, mat=mid)

    # 出屋面检修间（低矮化，顶 ≤14.97）
    m.box((2.8, 2.4, 0.75), (-3.5, 3.5, 14.0), mat=mid)
    m.box((3.0, 2.6, 0.12), (-3.5, 3.55, 14.72), rx=0.09, mat=light)
    m.box((0.8, 0.05, 0.6), (-3.5, 2.28, 14.0), mat=dark)                # 检修门暗龛
    m.box((0.1, 0.06, 0.1), (-3.05, 2.27, 14.3), mat="Kit_Iron")         # 门闩座
    # 轴流风机 ×2（一台风蚀掉叶）
    for fx, fy, shroud, guard, n_blades in ((-7.6, 2.6, "Kit_SteelPale", "Kit_Iron", 4),
                                            (-5.5, 0.5, "Kit_Rust", "Kit_RustDark", 2)):
        m.box((1.1, 1.1, 0.28), (fx, fy, 14.0), mat=mid)
        m.cyl(0.42, 0.34, at=(fx, fy, 14.28), seg=16, mat=shroud)
        m.cyl(0.07, 0.12, at=(fx, fy, 14.40), seg=8, mat=guard)
        for k in range(n_blades):
            m.box((0.55, 0.1, 0.02), (fx, fy, 14.45), rz=K.TAU * k / 4.0,
                  mat="Kit_SteelPale")
        _hoop(m, fx, fy, 14.66, 0.46, 0.018, guard)
    # 卧式水箱（支腿 + 锈箍 + 检修口 + 进管）
    for dx in (-0.55, 0.55):
        for dy in (-0.55, 0.55):
            m.cyl(0.05, 0.3, at=(1.4 + dx, 2.9 + dy, 14.0), seg=8, mat="Kit_Iron")
    m.revolve([(0.0, -0.02), (0.95, 0.0), (1.02, 0.10), (1.02, 0.42),
               (0.88, 0.55), (0.30, 0.62), (0.0, 0.64)],
              at=(1.4, 2.9, 14.30), seg=20, mat="Kit_SteelPale")
    m.revolve([(0.99, 14.62), (1.035, 14.62), (1.035, 14.74), (0.99, 14.74)],
              at=(1.4, 2.9, 0.0), seg=20, mat="Kit_RustDark")
    m.cyl(0.13, 0.07, at=(1.15, 3.2, 14.90), seg=10, mat="Kit_Iron")
    m.tube([(2.35, 2.9, 14.55), (2.35, 2.9, 14.02)], 0.05, seg=8, mat="Kit_SteelPale")
    # 排气鹅颈管
    m.flange(0.1, at=(-9.3, -3.2, 14.0), mat="Kit_Rust", bolt_mat="Kit_Iron")
    m.tube([(-9.3, -3.2, 14.05), (-9.3, -3.2, 14.7), (-9.3, -2.8, 14.7),
            (-9.3, -2.8, 14.5)], 0.085, seg=10, mat="Kit_SteelPale")
    m.cyl(0.1, 0.04, at=(-9.3, -2.8, 14.46), seg=10, mat="Kit_Iron")
    # 屋面排水口小盖
    m.cyl(0.1, 0.03, at=(-6.0, 1.0, 14.0), seg=10, mat="Kit_Iron")


def _west_ladder(m):
    """西墙带笼爬梯直上屋顶 + 梯顶抓栏 + 屋面格栅过渡板。"""
    m.ladder(at=(-11.12, -2.55, 0.0), h=14.9, w=0.5, rz=-math.pi / 2.0,
             mat="Kit_Rust", cage=True, cage_r=0.36)
    for y in (-2.8, -2.3):
        m.tube([(-11.12, y, 14.5), (-11.25, y, 14.96), (-10.65, y, 14.96),
                (-10.6, y, 14.1)], 0.024, seg=6, mat="Kit_Iron")
    m.grating((0.85, 1.15), at=(-10.55, -3.15, 14.0), mat="Kit_Rust", direction="x")


def _roof_extra(m):
    """屋顶管走线 + 检修走道 + 屋面机组（全部压在 14~15 m 之间）。"""
    pale = "Kit_SteelPale"
    iron = "Kit_Iron"
    mid = "Kit_ConcreteMid"
    # 屋面管走线：水箱 → 北 → 西 → 检修间（支墩 + 接头箍）
    m.tube([(2.35, 2.9, 14.22), (2.35, 4.1, 14.22), (-2.0, 4.1, 14.22),
            (-2.05, 3.6, 14.22)], 0.06, seg=8, mat=pale)
    for px, py in ((2.35, 3.6), (0.5, 4.1), (-1.4, 4.1)):
        m.member((px, py, 14.0), (px, py, 14.16), 0.05, 0.05, mat=iron)
    for cx in (1.0, -0.8):
        m.cyl(0.075, 0.05, at=(cx, 4.1, 14.2), seg=10, ry=math.pi / 2.0, mat=iron)
    # 检修走道格栅：爬梯口 → 检修间门口
    m.grating((0.7, 5.0), at=(-9.3, 0.3, 14.0), mat="Kit_Rust", direction="x")
    # 屋面机组（空调外机形态）+ 连管
    m.box((0.9, 0.45, 0.6), (2.8, -3.8, 14.0), mat=pale)
    m.box((0.78, 0.03, 0.5), (2.8, -4.04, 14.05), mat="Kit_RustDark")
    m.tube([(2.55, -3.8, 14.6), (2.55, -3.8, 14.05)], 0.035, seg=6, mat=iron)
    m.member((2.35, -3.8, 14.0), (2.55, -3.8, 14.0), 0.06, 0.06, mat=iron)


# ---------------------------------------------------------------------------
# 仓库附属
# ---------------------------------------------------------------------------

def _roll_door(m, cx, heavy_rust):
    """一樘卷帘门（墙已开门洞 1.7×3.0）：门板 + 缝肋 + 底轨 + 双轨 + 门楣盒 + 锈蚀分化。"""
    m.box((W_DOOR_W - 0.04, 0.07, 2.96), (cx, -5.44, 0.02), mat="Kit_PaintYellow")
    rib = "Kit_SteelPale" if heavy_rust else "Kit_Rust"
    for z in (0.55, 1.1, 1.65, 2.2):
        m.box((W_DOOR_W - 0.12, 0.025, 0.06), (cx, -5.487, z), mat=rib)
    if heavy_rust:
        m.box((0.7, 0.02, 0.9), (cx - 0.4, -5.49, 0.25), mat="Kit_Rust", rz=0.06)
        m.box((0.55, 0.02, 0.7), (cx + 0.45, -5.49, 1.5), mat="Kit_RustDark")
        m.box((0.6, 0.02, 1.2), (cx - 0.2, -5.49, 2.1), mat="Kit_Rust")
        m.box((0.9, 0.07, 0.1), (cx - 0.3, -5.475, 0.02), mat="Kit_RustDark")
    else:
        m.box((0.6, 0.02, 0.5), (cx - 0.45, -5.49, 0.2), mat="Kit_Rust")
        m.box((1.5, 0.02, 0.3), (cx, -5.49, 2.62), mat="Kit_ConcreteDark")
        m.box((W_DOOR_W, 0.09, 0.12), (cx, -5.475, 0.02), mat="Kit_SteelPale")
    for sx in (-1.0, 1.0):
        m.member((cx + sx * (W_DOOR_W / 2.0 + 0.06), -5.46, 0.0),
                 (cx + sx * (W_DOOR_W / 2.0 + 0.06), -5.46, 3.0), 0.09, 0.06,
                 mat="Kit_SteelPale")
    m.box((2.1, 0.5, 0.4), (cx, -5.32, 3.0), mat="Kit_SteelPale")


def _warehouse(m, rng):
    blue = "Kit_SteelBlue"
    pale = "Kit_SteelPale"
    # 四面墙（真实洞口：3 樘卷帘门 + 东墙破洞 + 北墙高窗）
    _wall_x(m, WX0, WX1, WY0, WY0 + WT, 0.0, W_EAVE,
            [(x, W_DOOR_W, 0.0, 3.0) for x in W_DOOR_XS], blue)
    _wall_x(m, WX0, WX1, WY1, WY1 - WT, 0.0, W_EAVE,
            [(W_RIDGE_X, 1.2, 4.2, 5.0)], blue)
    _wall_y(m, WY0 + WT, WY1 - WT, WX1, WX1 - WT, 0.0, W_EAVE,
            [(0.8, 1.3, 1.9, 3.0)], blue)
    _wall_y(m, WY0 + WT, WY1 - WT, WX0, WX0 + WT, 0.0, W_EAVE, [], blue)
    # 破洞内壁 + 翻边板
    m.box((1.24, 0.06, 1.04), (10.52, 0.8, 1.93), mat="Kit_ConcreteDark")
    m.box((0.5, 0.45, 0.03), (11.06, 0.35, 3.12), ry=0.45, rx=-0.15, mat=blue)
    # 人字山墙（三角棱柱，两端面各缩 0.04 防共面闪烁）
    v = [(WX0, WY0 + 0.04, W_EAVE), (W_RIDGE_X, WY0 + 0.04, W_RIDGE_Z),
         (WX1, WY0 + 0.04, W_EAVE), (WX0, WY1 - 0.04, W_EAVE),
         (W_RIDGE_X, WY1 - 0.04, W_RIDGE_Z), (WX1, WY1 - 0.04, W_EAVE)]
    f = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (0, 3, 5, 2)]
    m.add(v, f, mat=blue)
    # 屋面两坡 + 锈脊盖 + 坡面补板 + 屋面通气管
    ang = math.atan2(W_RIDGE_Z - W_EAVE, WX1 - W_RIDGE_X)
    slope = math.hypot(WX1 - W_RIDGE_X, W_RIDGE_Z - W_EAVE)
    m.box((slope + 0.25, 10.5, 0.14), (W_RIDGE_X, -0.5, W_RIDGE_Z), ry=ang, mat=blue)
    m.box((slope + 0.3, 10.5, 0.14), (WX0 - 0.25, -0.5, W_EAVE - 0.10),
          ry=-ang, mat=blue)
    m.box((0.34, 10.6, 0.07), (W_RIDGE_X, -0.5, W_RIDGE_Z), mat="Kit_Rust")
    m.box((0.9, 1.4, 0.03), (9.3, 1.5, 6.68), ry=ang, mat="Kit_Rust")
    m.box((0.8, 1.1, 0.03), (9.9, -2.6, 6.45), ry=ang, mat="Kit_RustDark")
    m.cyl(0.12, 0.35, at=(5.6, 2.5, 6.5), seg=10, mat="Kit_Iron")
    m.cyl(0.16, 0.05, at=(5.6, 2.5, 6.83), seg=10, mat="Kit_Rust")
    # 东坡天窗 ×2（斜脊底框 + 玻璃盖）
    for sx, sy in ((9.0, -1.0), (8.55, 2.3)):
        zsl = W_RIDGE_Z - (sx - W_RIDGE_X) * (W_RIDGE_Z - W_EAVE) / (WX1 - W_RIDGE_X)
        m.box((1.3, 1.0, 0.12), (sx, sy, zsl + 0.02), ry=ang, mat=pale)
        m.box((1.1, 0.8, 0.05), (sx, sy, zsl + 0.14), ry=ang, mat="Kit_GlassDark")
    # 东檐落水管（檐口 → 卸货台）
    m.tube([(11.2, 4.2, 5.85), (11.2, 4.35, 5.7), (11.2, 4.35, 0.55)], 0.06,
           seg=8, mat="Kit_Rust")
    for z in (1.2, 3.0, 4.8):
        m.member((11.02, 4.35, z), (11.2, 4.35, z), 0.05, 0.04, mat="Kit_Iron")
    m.box((0.2, 0.2, 0.08), (11.2, 4.35, 0.47), mat="Kit_RustDark")
    # 东侧卸货台（混凝土台 + 钢包边 + 台上缓冲柱 + 登台台阶）
    m.box((1.2, 4.5, 0.5), (11.6, 0.75, 0.0), mat="Kit_ConcreteMid")
    m.box((0.08, 4.5, 0.1), (12.16, 0.75, 0.4), mat=pale)
    m.box((1.2, 0.5, 0.25), (11.6, -1.05, 0.0), mat="Kit_ConcreteMid")
    m.box((1.2, 0.5, 0.12), (11.6, -1.5, 0.0), mat="Kit_ConcreteMid")
    for by in (-0.6, 2.4):
        m.cyl(0.06, 0.75, at=(12.0, by, 0.5), seg=8, mat="Kit_PaintYellow")
        m.cyl(0.065, 0.04, at=(12.0, by, 1.21), seg=8, mat="Kit_RustDark")
    # 卷帘门前排水沟带 + 铁缓冲柱（防撞）
    m.box((6.3, 0.5, 0.06), (W_RIDGE_X, -5.95, 0.0), mat="Kit_ConcreteDark")
    for bx in (4.9, 10.4):
        m.cyl(0.055, 0.8, at=(bx, -6.0, 0.0), seg=8, mat="Kit_Iron")
        m.cyl(0.06, 0.05, at=(bx, -6.0, 0.76), seg=8, mat="Kit_RustDark")
    # 破洞内景：货架板 + 板条箱剪影（透过洞读到"里面堆着货"）
    m.box((0.9, 0.3, 0.05), (10.35, 0.8, 1.2), mat="Kit_WoodDark")
    m.box((0.45, 0.45, 0.4), (10.3, 0.6, 0.0), rz=0.2, mat="Kit_ConcreteDark")
    m.box((0.4, 0.4, 0.35), (10.42, 1.05, 1.25), rz=-0.3, mat="Kit_WoodDark")
    # 3 樘卷帘门（锈蚀分化：中间那樘最惨）
    _roll_door(m, W_DOOR_XS[0], False)
    _roll_door(m, W_DOOR_XS[1], True)
    _roll_door(m, W_DOOR_XS[2], False)
    # 北墙高窗钢框 + 木封板
    for z, hh in ((4.17, 0.06), (4.94, 0.06)):
        m.box((1.24, 0.06, hh), (W_RIDGE_X, WY1, z), mat=pale)
    for sx in (-1.0, 1.0):
        m.box((0.06, 0.06, 0.86), (W_RIDGE_X + sx * 0.62, WY1, 4.2), mat=pale)
    for k in range(3):
        m.box((1.14, 0.04, 0.22), (W_RIDGE_X, WY1 + 0.04, 4.28 + 0.27 * k),
              rz=rng.uniform(-0.03, 0.03), mat="Kit_WoodMid")
    # 山墙通风口 + 百叶
    m.box((0.5, 0.12, 0.36), (W_RIDGE_X, -5.54, 6.42), mat=pale)
    for k in range(3):
        m.box((0.42, 0.03, 0.05), (W_RIDGE_X, -5.61, 6.5 + 0.1 * k), mat="Kit_RustDark")
    # 竖缝条（SteelPale/Rust 交替；门洞以上满布，门间壁上落地）
    pats = ["Kit_Rust", "Kit_SteelPale", "Kit_Rust", "Kit_RustDark",
            "Kit_SteelPale", "Kit_Rust"]
    i = 0
    for x in (4.42, 4.7, 6.68, 8.62, 10.55, 10.85):          # 南门间壁/端壁：落地
        m.box((0.09, 0.03, 5.9), (x, WY0 - 0.015, 0.03), mat=pats[i % len(pats)])
        i += 1
    for x in (5.3, 6.05, 7.25, 8.05, 9.2, 10.0):             # 门楣以上
        m.box((0.09, 0.03, 2.45), (x, WY0 - 0.015, 3.5), mat=pats[i % len(pats)])
        i += 1
    for y in (-4.8, -3.95, -3.1, -2.25, -1.4, 2.0, 2.85, 3.7):   # 东墙（避开破洞）
        m.box((0.03, 0.09, 5.9), (WX1 + 0.015, y, 0.03), mat=pats[i % len(pats)])
        i += 1
    for x in (4.6, 5.5, 6.4, 8.2, 9.1, 10.0, 10.9):          # 北墙
        m.box((0.09, 0.03, 5.9), (x, WY1 + 0.015, 0.03), mat=pats[i % len(pats)])
        i += 1
    # 勒脚（卷帘门口分段断开）
    m.box((0.12, 9.6, 0.4), (WX0 + 0.06, -0.5, 0.0), mat="Kit_ConcreteMid")
    m.box((6.82, 0.12, 0.4), (W_RIDGE_X, WY1 + 0.06, 0.0), mat="Kit_ConcreteMid")
    m.box((0.12, 9.6, 0.4), (WX1 - 0.06, -0.5, 0.0), mat="Kit_ConcreteMid")
    for x0, w in ((4.3, 0.55), (6.55, 0.25), (8.5, 0.25), (10.45, 0.55)):
        m.box((w, 0.12, 0.4), (x0 + w / 2.0, WY0 - 0.06, 0.0), mat="Kit_ConcreteMid")
    # 与主楼的接缝披水钢板
    m.box((0.55, 10.4, 0.06), (4.15, -0.5, 6.02), mat=pale)


# ---------------------------------------------------------------------------
# 管线 + 小件点缀
# ---------------------------------------------------------------------------

def _pipes_misc(m, rng):
    pale = "Kit_SteelPale"
    rust = "Kit_Rust"
    # -- 西墙工艺立管：穿墙法兰 → 西伸 → 落地（阀门手轮 + 保温段）--
    m.cyl(0.17, 0.12, at=(-11.02, -1.2, 5.6), seg=12, ry=math.pi / 2.0, mat="Kit_Iron")
    for k in range(4):
        a = math.pi / 4.0 + K.TAU * k / 4.0
        m.cyl(0.02, 0.06, at=(-11.06, -1.2 + 0.13 * math.cos(a),
                              5.6 + 0.13 * math.sin(a)),
              seg=6, ry=math.pi / 2.0, mat="Kit_Iron")
    m.tube([(-10.92, -1.2, 5.6), (-11.78, -1.2, 5.6), (-11.78, -1.2, 0.42),
            (-11.5, -1.2, 0.2)], 0.11, seg=10, mat=pale)
    m.cyl(0.17, 1.1, at=(-11.78, -1.2, 2.6), seg=12, mat="Kit_ConcreteMid")   # 保温段
    for z in (1.6, 4.2):
        m.cyl(0.125, 0.05, at=(-11.78, -1.2, z), seg=10, mat="Kit_RustDark")
    m.flange(0.12, at=(-11.5, -1.2, 0.02), mat=rust)
    m.box((0.2, 0.2, 0.26), (-11.78, -1.2, 1.35), mat="Kit_Iron")             # 阀体
    _hoop(m, -11.78, -1.2, 1.62, 0.13, 0.018, "Kit_Iron")                     # 手轮
    m.cyl(0.03, 0.1, at=(-11.78, -1.2, 1.57), seg=8, mat="Kit_Iron")
    # -- 东墙出管（朝 +X）：出墙 → 东伸 → 落地开口 --
    m.cyl(0.15, 0.1, at=(10.97, 3.6, 4.3), seg=10, ry=math.pi / 2.0, mat="Kit_Iron")
    m.tube([(10.9, 3.6, 4.3), (12.05, 3.6, 4.3), (12.05, 3.6, 0.5),
            (12.28, 3.6, 0.32)], 0.09, seg=10, mat=pale)
    m.cyl(0.14, 0.9, at=(12.05, 3.6, 2.2), seg=10, mat="Kit_ConcreteMid")
    m.flange(0.11, at=(12.24, 3.6, 0.14), mat=rust)
    # -- 南檐落水管：披水铁 + 竖管 + 箍带 + 鞋头 --
    m.box((0.28, 0.55, 0.13), (3.3, -4.9, 13.8), mat="Kit_Iron")
    m.tube([(3.42, -5.12, 13.78), (3.42, -5.12, 0.35), (3.42, -5.36, 0.1)],
           0.075, seg=10, mat=rust)
    for z in (1.5, 4.3, 7.1, 9.9, 12.7):
        m.member((3.42, -5.02, z), (3.42, -5.2, z), 0.05, 0.04, mat="Kit_Iron")
    for z in (5.0, 10.0):
        m.cyl(0.085, 0.06, at=(3.42, -5.12, z), seg=10, mat="Kit_RustDark")
    m.box((0.22, 0.22, 0.1), (3.42, -5.42, 0.0), mat="Kit_RustDark")
    # -- 地面倒伏排水管（朝 -Y 院里倒）--
    m.tube([(0.6, -6.35, 0.13), (1.8, -6.12, 0.14), (2.95, -6.55, 0.13)],
           0.12, seg=10, mat="Kit_RustDark")
    m.cyl(0.17, 0.06, at=(0.5, -6.35, 0.1), seg=12, ry=math.pi / 2.0, mat=rust)
    for k in range(4):
        a = K.TAU * k / 4.0
        m.cyl(0.02, 0.05, at=(0.47, -6.35 + 0.13 * math.cos(a),
                              0.1 + 0.13 * math.sin(a)),
              seg=6, ry=math.pi / 2.0, mat="Kit_Iron")
    m.cyl(0.12, 0.25, at=(2.85, -6.52, 0.01), seg=10, r_top=0.095,
          ry=1.45, mat="Kit_RustDark")
    # -- 小件：配电箱 / 外挂空调×2 / 废弃推车 / 缓冲柱 / 油桶 / 木托盘 --
    m.box((0.14, 0.62, 0.82), (-11.07, 1.6, 1.1), mat=pale)                   # 配电箱
    m.box((0.02, 0.5, 0.7), (-11.145, 1.6, 1.16), mat="Kit_ConcreteDark")
    m.tube([(-11.07, 1.6, 1.92), (-11.07, 1.6, 3.4)], 0.03, seg=6, mat="Kit_Iron")
    # 西墙线管延伸：配电箱 → 上翻 → 横走 → 一层窗侧接线盒
    m.tube([(-11.07, 1.6, 3.4), (-11.07, 0.53, 3.4), (-11.07, 0.53, 4.5)],
           0.025, seg=6, mat="Kit_Iron")
    for z in (1.6, 2.6, 3.4):
        m.box((0.02, 0.06, 0.06), (-11.09, 1.6, z), mat="Kit_Iron")
    m.box((0.1, 0.14, 0.18), (-11.1, 0.53, 4.55), mat="Kit_Iron")
    # 女儿墙坍塌角墙根散落碎块
    for rx0, ry0, s, rz, mt in ((-10.5, -5.25, 0.4, 0.5, "Kit_ConcreteMid"),
                                (-9.9, -5.4, 0.3, 1.3, "Kit_ConcreteDark"),
                                (-10.15, -5.05, 0.24, 2.2, "Kit_ConcreteMid"),
                                (-9.55, -5.15, 0.5, 0.2, "Kit_WoodDark")):
        m.box((s, s * 0.7, 0.14), (rx0, ry0, 0.0), rz=rz, mat=mt)
    for ax, az, tilt in ((-9.85, 3.8, 0.0), (1.75, 7.28, 0.12)):              # 空调外机
        m.box((0.78, 0.42, 0.6), (ax, -5.21, az), rx=tilt, mat=pale)
        m.box((0.7, 0.02, 0.5), (ax, -5.425, az + 0.05), mat="Kit_RustDark")
        for dx in (-0.28, 0.28):
            m.member((ax + dx, -5.0, az - 0.02), (ax + dx, -5.32, az - 0.1),
                     0.05, 0.05, mat="Kit_Iron")
    m.box((0.78, 0.52, 0.05), (-0.9, -6.1, 0.30), mat=rust)                   # 推车
    for dx, dy in ((-0.32, -0.2), (0.32, -0.2), (-0.32, 0.2), (0.32, 0.2)):
        m.member((-0.9 + dx, -6.1 + dy, 0.0), (-0.9 + dx, -6.1 + dy, 0.3),
                 0.04, 0.04, mat="Kit_Iron")
    for wx in (-1.18, -0.62):
        m.cyl(0.14, 0.06, at=(wx, -6.1, 0.14), seg=12, ry=math.pi / 2.0, mat=rust)
    m.tube([(-0.55, -6.1, 0.35), (-0.55, -6.35, 0.95), (-0.35, -6.35, 1.0)],
           0.025, seg=6, mat="Kit_Iron")
    m.box((0.3, 0.2, 0.12), (-0.95, -6.1, 0.38), rz=0.3, mat="Kit_WoodMid")
    # 入口东墙根板条箱堆（两只半 + 倒扣一只）
    m.box((0.55, 0.45, 0.4), (0.9, -5.42, 0.0), rz=0.12, mat="Kit_WoodMid")
    m.box((0.5, 0.42, 0.36), (0.95, -5.4, 0.4), rz=-0.08, mat="Kit_WoodDark")
    m.box((0.48, 0.44, 0.3), (1.62, -5.35, 0.0), rz=0.45, mat="Kit_WoodMid")
    # 西墙消火栓（墙装卷盘 + 竖管 + 阀轮，位于一层窗间空档）
    m.cyl(0.28, 0.13, at=(-11.07, -0.55, 1.3), seg=14, ry=math.pi / 2.0,
          mat="Kit_RustDark")
    m.cyl(0.1, 0.22, at=(-11.14, -0.55, 1.3), seg=10, ry=math.pi / 2.0, mat="Kit_Iron")
    _hoop(m, -11.26, -0.55, 1.3, 0.1, 0.014, "Kit_Iron")
    m.tube([(-11.07, -0.55, 1.3), (-11.07, -0.55, 0.35)], 0.03, seg=6, mat="Kit_Iron")
    for bx, by in ((-6.35, -5.9), (-7.1, -5.65)):                             # 缓冲柱
        m.cyl(0.1, 0.05, at=(bx, by, 0.0), seg=10, mat="Kit_ConcreteMid")
        m.cyl(0.07, 0.7, at=(bx, by, 0.05), seg=10, mat="Kit_PaintYellow")
        m.cyl(0.075, 0.04, at=(bx, by, 0.71), seg=10, mat="Kit_RustDark")
    m.cyl(0.29, 0.85, at=(4.75, -5.9, 0.0), seg=14, mat="Kit_PaintYellow")    # 废油桶
    m.cyl(0.3, 0.05, at=(4.75, -5.9, 0.3), seg=14, mat="Kit_RustDark")
    m.cyl(0.3, 0.05, at=(4.75, -5.9, 0.58), seg=14, mat="Kit_RustDark")
    m.cyl(0.295, 0.03, at=(4.75, -5.9, 0.85), seg=14, mat=rust)
    for k in range(5):                                                        # 木托盘斜靠
        m.box((1.05, 0.045, 0.13), (10.5, -5.68, 0.12 + 0.16 * k),
              rx=-0.16, rz=rng.uniform(-0.02, 0.02), mat="Kit_WoodMid")
    m.box((0.12, 0.05, 0.9), (10.1, -5.72, 0.15), mat="Kit_WoodDark")
    m.box((0.12, 0.05, 0.9), (10.9, -5.72, 0.15), mat="Kit_WoodDark")


# ---------------------------------------------------------------------------
# 风化层：锈水痕 / 污渍 / 水泥补板
# ---------------------------------------------------------------------------

def _weathering(m):
    rust = "Kit_Rust"
    rust_d = "Kit_RustDark"
    grime = "Kit_ConcreteDark"
    # 南立面（y=-5，朝 -Y）
    for x, zt, ln, w, mt in (
            (-8.88, 13.9, 4.2, 0.15, rust), (0.5, 13.9, 2.8, 0.11, rust_d),
            (-10.7, 2.6, 1.5, 0.10, rust), (-7.92, 4.35, 0.9, 0.13, rust_d),
            (-5.98, 7.85, 1.2, 0.09, rust), (-4.05, 9.55, 2.2, 0.14, rust_d),
            (-0.18, 4.35, 0.7, 0.16, rust), (1.75, 11.35, 1.6, 0.12, rust_d),
            (3.42, 13.3, 3.6, 0.09, rust), (-6.9, 3.0, 1.1, 0.2, rust_d),
            (-8.9, 8.0, 2.0, 0.08, grime)):
        _streak_y(m, MY0, -1.0, x, zt, ln, w, mt)
    # 西立面（x=-11，朝 -X）
    for y, zt, ln, w, mt in (
            (-2.55, 13.9, 3.5, 0.14, rust), (1.6, 2.0, 0.8, 0.15, rust_d),
            (-3.8, 4.3, 1.0, 0.1, rust), (-0.55, 1.25, 1.0, 0.14, rust_d)):
        _streak_x(m, MX0, -1.0, y, zt, ln, w, mt)
    _streak_x(m, MX0, -1.0, 3.9, 5.5, 2.4, 0.18, rust)
    # 北立面（朝 +Y，背面偶尔扫到）
    for x, zt, ln, w, mt in ((-8.88, 13.9, 3.0, 0.12, rust),
                             (0.78, 8.0, 2.2, 0.1, rust_d),
                             (-6.95, 10.2, 2.4, 0.1, rust),
                             (-2.12, 4.35, 1.1, 0.12, grime)):
        _streak_y(m, MY1, 1.0, x, zt, ln, w, mt)
    # 仓库东墙（x=11，朝 +X）：破洞下大锈痕 + 竖缝锈 + 卸货台沿锈
    for y, zt, ln, w, mt in ((0.8, 1.85, 1.7, 0.3, rust_d), (3.5, 5.9, 2.2, 0.12, rust),
                             (-4.0, 5.9, 1.5, 0.1, rust_d)):
        _streak_x(m, WX1, 1.0, y, zt, ln, w, mt)
    _streak_x(m, 12.2, 1.0, 0.75, 0.42, 0.3, 0.5, rust_d)   # 卸货台包边垂锈
    # 仓库南墙（y=-5.5，朝 -Y）：门间锈水 + 山墙尖下
    for x, zt, ln, w, mt in ((6.7, 2.9, 2.4, 0.12, rust), (8.6, 2.95, 1.8, 0.14, rust_d),
                             (10.45, 5.9, 2.0, 0.1, rust), (4.6, 5.9, 1.6, 0.1, rust_d)):
        _streak_y(m, WY0, -1.0, x, zt, ln, w, mt)
    # 水泥补板（部分带螺栓排）+ 换板
    _patch_y(m, MY0, -1.0, -10.2, 5.2, 1.6, 1.2, "Kit_ConcreteMid", bolts=True)
    _patch_y(m, MY0, -1.0, -0.9, 10.8, 2.2, 1.0, "Kit_ConcreteMid")
    _patch_y(m, MY0, -1.0, 3.1, 1.2, 1.2, 1.8, "Kit_ConcreteMid", bolts=True)
    _patch_x(m, MX0, -1.0, -2.2, 8.6, 1.8, 1.2, "Kit_ConcreteMid")
    _patch_x(m, MX0, -1.0, 0.9, 11.8, 1.4, 0.9, "Kit_ConcreteMid", bolts=True)
    _patch_y(m, WY0, -1.0, 10.2, 4.5, 1.2, 1.4, "Kit_SteelPale")
    _patch_y(m, WY0, -1.0, 4.75, 4.6, 1.1, 1.0, "Kit_SteelPale")
    # 抹灰脱落露砖补丁（RockMid 作砖色，避开层间腰线）
    _patch_y(m, MY0, -1.0, -8.9, 1.4, 1.2, 1.6, "Kit_RockMid", bolts=False)
    _patch_x(m, MX0, -1.0, 0.5, 5.6, 1.4, 1.2, "Kit_RockMid")
    _patch_x(m, MX0, -1.0, -3.6, 12.4, 1.0, 1.0, "Kit_RockMid")
    # 西南角女儿墙根墙面剥落暗斑
    _patch_y(m, MY0, -1.0, -10.6, 13.6, 0.9, 0.7, "Kit_ConcreteDark")
    _patch_x(m, MX0, -1.0, -4.6, 13.4, 0.8, 0.9, "Kit_ConcreteDark")


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def build(m):
    """BLOCK-04 厂区旁楼（预览器入口：preview_module.py -- building）。"""
    rng = random.Random(407)
    _main_shell(m, rng)
    _entrance(m, rng)
    _parapet_roof(m, rng)
    _west_ladder(m)
    _roof_extra(m)
    _warehouse(m, rng)
    _pipes_misc(m, rng)
    _weathering(m)
    return m
