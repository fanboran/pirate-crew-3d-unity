# -*- coding: utf-8 -*-
"""chemplant/mod_props.py —— 南院散落杂物（SCRAP-06，分区 props）。

【职责】密度：让西南院不空。油桶 / 集装箱 / 废料堆 / 废弃卡车 / 叉车 /
电线杆与垂落电缆 / 散落管段 / 木托盘 / 警示牌 / 路障锥 / 半塌料棚。
全部按「锈蚀 / 褪色 / 半倒」处理：Kit_Rust 系 + 褪色黄做对比，质感全靠
几何密度（波纹壁 / 滚箍 / 门铰链 / 板条），零贴图零金属度。

【本地系与硬边界】原点 = 分区 props 包围盒中心地面投影；地面 z=0，+Z 上、
+Y 北、+X 东。盒 X[-16.5,16.5]、Y[-9,9]、Z<=7（+0.5 容差）。
场地东西主路（site Y[-8,-4] = 本地 Y[5,9]）斜穿本盒北带：大件全部压在
Y<=4.5 的院内，路边（Y>5）只放「倾倒感」小件（托盘/锥/牌/管段/断缆），
电线杆沿路缘线 Y≈4.7~5.2 一字排开。

【与规格的偏差（记入汇报）】电线杆规格写 8 m，盒子高 7 + 0.5 容差装不下，
杆高取 7.2 m（断缆拖地的戏剧感由 P2 前倾 4 度 + 断落电缆补足）。

用法（仓库根执行，先建模后出图由 preview_module 收口）：
    "$BLENDER" -b --factory-startup -P tools/blender/scene/chemplant/preview_module.py -- props
"""

import math
import random

import kit_common as K

# 本地硬边界（props 盒，<=0.5 m 容差）
X_MIN, X_MAX = -16.5, 16.5
Y_MIN, Y_MAX = -9.0, 9.0
Z_MAX = 7.5

TAU = math.pi * 2.0


# ---------------------------------------------------------------------------
# 脚手架：平面旋转框（局部 u=纵轴 / v=横轴 -> 分件本地系）
# ---------------------------------------------------------------------------

class Frame(object):
    """以 (cx,cy) 为原点、绕 Z 转 deg 的平面脚手架。p(u,v,z) -> 本地坐标。"""

    def __init__(self, cx, cy, deg=0.0):
        self.cx, self.cy = cx, cy
        self.deg = deg
        self.rad = math.radians(deg)
        self.c = math.cos(self.rad)
        self.s = math.sin(self.rad)

    def p(self, u, v, z=0.0):
        return (self.cx + u * self.c - v * self.s,
                self.cy + u * self.s + v * self.c, z)

    def yaw(self, extra=0.0):
        """图元自身的世界 yaw（弧度）= 框朝向 + 附加角。"""
        return self.rad + math.radians(extra)


def _rot2(x, y, ang):
    ca, sa = math.cos(ang), math.sin(ang)
    return (x * ca - y * sa, x * sa + y * ca)


def _octagon(r):
    return [(r * math.cos(TAU * k / 8.0), r * math.sin(TAU * k / 8.0)) for k in range(8)]


# ---------------------------------------------------------------------------
# 油桶（12 只：立放 / 叠放 / 侧倒 / 锈穿）
# ---------------------------------------------------------------------------

def _drum(m, x, y, z=0.0, rz=0.0, lying=False, body="Kit_Rust", hoop="Kit_RustDark",
          seg=26, broken=False, sink=0.0):
    """油桶 Ø0.6 × 0.9：两道滚箍 + 顶缘凸缘 + 两只桶塞。

    lying=True 侧倒（轴向水平，rz = 轴向方位角，桶心高 = 半径-下沉量）；
    broken=True 加锈穿破口（暗孔盘 + 外翻薄板，只对侧倒桶实现）。
    """
    rb, hh = 0.30, 0.90
    if not lying:
        m.cyl(rb, hh, at=(x, y, z), seg=seg, mat=body)
        for zi in (0.24, 0.60):                                   # 两道滚箍
            m.cyl(0.313, 0.045, at=(x, y, z + zi), seg=seg, mat=hoop)
        m.cyl(0.304, 0.05, at=(x, y, z + hh - 0.05), seg=seg, mat=hoop)   # 顶缘
        for dx, dy in ((0.10, 0.06), (-0.09, -0.08)):             # 桶塞
            m.cyl(0.052, 0.034, at=(x + dx, y + dy, z + hh - 0.004), seg=10, mat=hoop)
        return
    # 侧倒：轴心 c，轴向 d，径向 r0
    d = (math.cos(rz), math.sin(rz), 0.0)
    r0 = (-math.sin(rz), math.cos(rz), 0.0)
    c = (x, y, rb - sink)
    base = (c[0] - d[0] * hh / 2, c[1] - d[1] * hh / 2, c[2])
    m.cyl(rb, hh, at=base, ry=math.pi / 2, rz=rz, seg=seg, mat=body)

    def _collar(t):
        p = (c[0] + d[0] * t, c[1] + d[1] * t, c[2])
        m.cyl(0.313, 0.045, at=(p[0] - d[0] * 0.0225, p[1] - d[1] * 0.0225, p[2]),
              ry=math.pi / 2, rz=rz, seg=seg, mat=hoop)

    _collar(-0.21)
    _collar(0.15)
    _collar(0.373)                                                # 顶缘（+d 端）
    for sgn, off in ((1.0, 0.10), (-1.0, -0.09)):                 # 桶塞在 +d 端盖上
        p = (c[0] + d[0] * (hh / 2 - 0.016) + r0[0] * off,
             c[1] + d[1] * (hh / 2 - 0.016) + r0[1] * off, c[2] + r0[2] * off)
        m.cyl(0.052, 0.032, at=(p[0] - d[0] * 0.016, p[1] - d[1] * 0.016, p[2]),
              ry=math.pi / 2, rz=rz, seg=10, mat=hoop)
    if broken:
        # 锈穿：朝上侧破口（暗孔盘）+ 外翻薄板
        hp = (c[0] + r0[0] * rb * 0.97 + d[0] * 0.05,
              c[1] + r0[1] * rb * 0.97 + d[1] * 0.05, c[2] + r0[2] * rb * 0.97)
        m.cyl(0.13, 0.014, at=(hp[0] - r0[0] * 0.007, hp[1] - r0[1] * 0.007,
                               hp[2] - r0[2] * 0.007),
              ry=math.pi / 2, rz=rz + math.pi / 2, seg=12, mat="Kit_RockDark")
        fp = (c[0] + d[0] * 0.05 + r0[0] * (rb + 0.05),
              c[1] + d[1] * 0.05 + r0[1] * (rb + 0.05), c[2] + r0[2] * (rb + 0.05) + 0.09)
        m.box((0.34, 0.016, 0.18), at=fp, rz=rz, rx=-1.05, mat="Kit_RustDark")


def _drums(m):
    """12 只油桶，成组散落（叠放/侧倒/锈穿各有所在）。"""
    # 组1：集装箱 B 东端外——两只叠放
    _drum(m, -1.86, 4.06, 0.0, rz=0.3, body="Kit_Rust")
    _drum(m, -1.74, 4.16, 0.9, rz=1.1, body="Kit_SteelBlue")
    # 组2：院南——两立 + 一只侧倒滚开
    _drum(m, -4.0, -2.6, 0.0, rz=0.2, body="Kit_Rust")
    _drum(m, -4.8, -3.3, 0.0, rz=1.4, body="Kit_PaintYellow")
    _drum(m, -8.0, -6.6, 0.02, rz=math.radians(64), lying=True, body="Kit_Rust", sink=0.02)
    # 组3：卡车南侧——三只立放
    _drum(m, 3.2, 0.2, 0.0, rz=0.7, body="Kit_Rust")
    _drum(m, 4.0, -0.3, 0.0, rz=2.0, body="Kit_SteelBlue")
    _drum(m, 3.6, 0.9, 0.0, rz=0.1, body="Kit_Rust")
    # 组4：西北角独苗 / 西缘独苗
    _drum(m, -16.1, -4.4, 0.0, rz=0.5, body="Kit_Rust")
    _drum(m, -11.4, -8.0, 0.0, rz=1.9, body="Kit_RustDark")
    # 组5：棚前——立放 + 一只锈穿侧倒（漏油渍）
    _drum(m, 10.2, 3.9, 0.0, rz=0.9, body="Kit_PaintYellow")
    _drum(m, 10.2, 2.0, 0.02, rz=math.radians(15), lying=True, body="Kit_Rust",
          broken=True, sink=0.02)
    m.cyl(0.85, 0.006, at=(10.9, 1.6, 0.003), seg=16, mat="Kit_RockDark")   # 油渍


# ---------------------------------------------------------------------------
# 集装箱（3 只：波纹壁 = 竖向梯形板条密铺；门端两扇门 + 铰链 + 门锁杆 + 8 角件）
# ---------------------------------------------------------------------------

# 波纹条剖面（+y = 朝墙外）：窄条 + 深槽，凸筋之间露出 0.13 宽的阴影缝才读得出波纹
_CORR = [(-0.085, 0.0), (0.085, 0.0), (0.085, 0.030), (0.035, 0.060),
         (-0.035, 0.060), (-0.085, 0.030)]


def _wall_strips(m, f, axis, fixed, z0, z1, n, mat, scale=1.0):
    """沿箱壁密铺竖向波纹条。axis='u'：条排布沿 u、墙面在 v=fixed；反之亦然。"""
    lo, hi = fixed[0], fixed[1]
    for k in range(n):
        t = lo + (hi - lo) * (k + 0.5) / n
        pts = [(p[0] * scale, p[1]) for p in _CORR]
        if axis == "u":
            a, b = f.p(t, lo * 0 + fixed[2], z0), f.p(t, fixed[2], z1)
            roll = f.yaw() if fixed[2] > 0 else f.yaw(180.0)
        else:
            a, b = f.p(fixed[2], t, z0), f.p(fixed[2], t, z1)
            roll = f.yaw(-90.0) if fixed[2] > 0 else f.yaw(90.0)
        m.extrude_along(pts, a, b, mat=mat, roll=roll)


def _door_leaf(m, f, hv, open_deg, mat, iron, H, W):
    """集装箱门端一扇门。hv = 铰链边横向坐标（框 v 值，取 ±(W/2-0.07)）；
    open_deg=0 关闭贴门端，>0 绕铰链外摆。含门板/三条竖筋/锁杆/门把手。"""
    L = 12.2
    uw, dh = W / 2 - 0.10, H - 0.50
    tv = (math.cos(f.yaw(90.0)), math.sin(f.yaw(90.0)))
    tu = (math.cos(f.rad), math.sin(f.rad))
    hinge = f.p(L / 2 - 0.06, hv, 0.0)
    s_c = tv if hv < 0 else (-tv[0], -tv[1])                      # 关门时门宽方向
    sgn = 1.0 if hv < 0 else -1.0
    s = _rot2(*s_c, -sgn * math.radians(open_deg)) if open_deg > 0.5 else s_c
    n = (s[1], -s[0])                                             # 门面外法向
    yaw_s = math.atan2(s[1], s[0])
    # 门板
    m.box((0.06, uw, dh), at=(hinge[0] + s[0] * uw / 2, hinge[1] + s[1] * uw / 2, 0.25),
          rz=yaw_s - math.pi / 2, mat=mat)
    # 三条竖向加强筋
    for k in (1, 2, 3):
        px = hinge[0] + s[0] * uw * k / 4.0 + n[0] * 0.048
        py = hinge[1] + s[1] * uw * k / 4.0 + n[1] * 0.048
        m.box((0.038, 0.09, dh - 0.10), at=(px, py, 0.30), rz=yaw_s - math.pi / 2, mat=mat)
    # 锁杆 + 把手
    rx_ = hinge[0] + s[0] * uw * 0.72 + n[0] * 0.075
    ry_ = hinge[1] + s[1] * uw * 0.72 + n[1] * 0.075
    m.cyl(0.020, dh - 0.10, at=(rx_, ry_, 0.30), seg=8, mat=iron)
    hy, hz = 1.05, 0.0
    m.cyl(0.017, 0.26, at=(rx_ + n[0] * (hy - 0.02), ry_ + n[1] * (hy - 0.02), 1.02),
          ry=math.pi / 2, rz=math.atan2(n[1], n[0]), seg=8, mat=iron)
    # 铰链座三只（留在门框上，开门也不动）
    tu3 = (tu[0], tu[1])
    for zj in (0.38, 1.38, 2.22):
        m.box((0.06, 0.09, 0.11),
              at=(hinge[0] - tu3[0] * 0.02 + n[0] * 0.02,
                  hinge[1] - tu3[1] * 0.02 + n[1] * 0.02, zj),
              rz=f.yaw(), mat=iron)


def _container(m, f, body, iron="Kit_Iron", door_open=False):
    """40 ft 集装箱 12.2×2.44×2.6：波纹壁密铺 + 门端五金 + 8 角件；
    door_open=True 时一扇门外摆 ~115 度，露出 Kit_ConcreteDark 内壁。"""
    L, W, H = 12.2, 2.44, 2.6
    # 底盘 + 角柱 + 顶板
    m.box((L, W, 0.14), at=f.p(0, 0, 0.02), rz=f.yaw(), mat=body)
    for su in (-1, 1):
        for sv in (-1, 1):
            m.box((0.15, 0.15, H - 0.14), at=f.p(su * (L / 2 - 0.075), sv * (W / 2 - 0.075),
                                                 0.14), rz=f.yaw(), mat=body)
    m.box((L, W, 0.10), at=f.p(0, 0, H - 0.10), rz=f.yaw(), mat=body)
    # 纵墙：底带 + 波纹条密铺
    for sv in (-1, 1):
        vv = sv * (W / 2 - 0.028)
        m.box((L - 0.30, 0.05, H - 0.30), at=f.p(0, vv, 0.12), rz=f.yaw(), mat=body)
        _wall_strips(m, f, "u", (-(L / 2 - 0.35), L / 2 - 0.35, vv), 0.14, H - 0.14,
                     52, body)
    # 空端（u=-L/2）：底带 + 矮波纹条
    uu = -(L / 2 - 0.028)
    m.box((0.05, W - 0.30, H - 0.30), at=f.p(uu, 0, 0.12), rz=f.yaw(), mat=body)
    _wall_strips(m, f, "v", (-(W / 2 - 0.35), W / 2 - 0.35, uu), 0.14, H - 0.14, 9,
                 body, scale=0.7)
    # 门端门槛
    m.box((0.10, W - 0.20, 0.14), at=f.p(L / 2 - 0.06, 0, 0.14), rz=f.yaw(),
          mat="Kit_RustDark")
    # 门端框（u=+L/2）：楣 + 内衬暗墙
    m.box((0.10, W, 0.30), at=f.p(L / 2 - 0.05, 0, H - 0.30), rz=f.yaw(), mat=body)
    # 锈蚀层：下沿一圈锈带 + 竖向锈泪痕（废感全靠这个）
    for su in (-1, 1):
        m.box((L - 0.24, 0.02, 0.34), at=f.p(0, su * (W / 2 + 0.005), 0.14),
              rz=f.yaw(), mat="Kit_RustDark")
    for sv in (-1, 1):
        m.box((0.02, W - 0.24, 0.34), at=f.p(sv * (L / 2 + 0.005), 0, 0.14),
              rz=f.yaw(), mat="Kit_RustDark")
    rnd = random.Random(int(f.cx * 10) + int(f.cy * 10))
    for k in range(7):                                            # 泪痕：纵墙竖锈条
        t = -L / 2 + 0.9 + k * (L - 1.8) / 6.0 + rnd.uniform(-0.2, 0.2)
        sv = 1.0 if k % 2 == 0 else -1.0
        m.box((0.05 + rnd.uniform(0, 0.04), 0.015, rnd.uniform(0.5, 1.4)),
              at=f.p(t, sv * (W / 2 + 0.008), rnd.uniform(0.4, 0.9)),
              rz=f.yaw(), mat="Kit_RustDark")
    # 8 角件
    for zu in (0.0, H - 0.30):
        for su in (-1, 1):
            for sv in (-1, 1):
                m.box((0.30, 0.20, 0.17),
                      at=f.p(su * (L / 2 - 0.16), sv * (W / 2 - 0.11), zu),
                      rz=f.yaw(), mat=iron)
    # 门扇
    if door_open:
        _door_leaf(m, f, -(W / 2 - 0.07), 0.0, body, iron, H, W)      # 关着的一扇
        _door_leaf(m, f, W / 2 - 0.07, 115.0, body, iron, H, W)       # 外摆的一扇
        # 内壁 + 内地板 + 内景（敞门可见）
        m.box((L - 0.36, 0.04, H - 0.70), at=f.p(0, -(W / 2 - 0.16), 0.16),
              rz=f.yaw(), mat="Kit_ConcreteDark")
        m.box((L - 0.36, W - 0.36, 0.05), at=f.p(0, 0, 0.16), rz=f.yaw(),
              mat="Kit_ConcreteDark")
        for xt in (-3.2, 0.0, 3.2):                               # 内顶横梁（敞门可见）
            m.member(f.p(xt, -(W / 2 - 0.20), H - 0.32), f.p(xt, W / 2 - 0.20, H - 0.32),
                     0.10, 0.06, mat="Kit_RustDark")
        m.box((0.55, 0.50, 0.45), at=f.p(-1.4, -0.30, 0.21), rz=0.4,
              mat="Kit_WoodDark")                                  # 箱内杂物
        m.box((0.45, 0.40, 0.35), at=f.p(-1.35, -0.28, 0.61), rz=0.1,
              mat="Kit_WoodMid")
        _drum(m, *(f.p(-2.6, 0.35)[:2]), 0.21, rz=0.6, body="Kit_RustDark")
    else:
        _door_leaf(m, f, -(W / 2 - 0.07), 0.0, body, iron, H, W)
        _door_leaf(m, f, W / 2 - 0.07, 0.0, body, iron, H, W)


def _containers(m):
    _container(m, Frame(-9.5, 2.3, 4.0), "Kit_SteelBlue")                 # 褪色蓝
    _container(m, Frame(-9.3, -1.3, -3.0), "Kit_Rust")                    # 全锈
    _container(m, Frame(8.5, -2.5, 100.0), "Kit_PaintYellow", door_open=True)  # 褪色黄·敞门


# ---------------------------------------------------------------------------
# 废弃卡车（驾驶室 + 风挡 + 货斗竖条 + 尾板放下 + 4 轮 + 底盘）
# ---------------------------------------------------------------------------

def _wheel(m, f, u, v, r, w, tire="Kit_ConcreteDark", hub="Kit_Iron", seg=30):
    """车轮：扁平 cyl 轮胎 + 轮毂盘（轴沿车横 v 向）。"""
    ax = f.yaw(90.0)
    d = (math.cos(ax), math.sin(ax))
    c = f.p(u, v, r)
    m.cyl(r, w, at=(c[0] - d[0] * w / 2, c[1] - d[1] * w / 2, c[2]),
          ry=math.pi / 2, rz=ax, seg=seg, mat=tire)
    m.cyl(r * 0.46, w + 0.05, at=(c[0] - d[0] * (w + 0.05) / 2,
                                  c[1] - d[1] * (w + 0.05) / 2, c[2]),
          ry=math.pi / 2, rz=ax, seg=16, mat=hub)


def _truck(m):
    f = Frame(1.0, 2.6, 10.0)
    rust, dark = "Kit_Rust", "Kit_RustDark"
    # 底盘：两根纵梁 + 三道横梁
    for sv in (-1, 1):
        m.member(f.p(-3.3, sv * 0.55, 0.62), f.p(3.45, sv * 0.55, 0.62), 0.14, 0.10, mat=dark)
    for xu in (-2.6, -1.4, -0.2, 1.2, 2.6):
        m.member(f.p(xu, -0.55, 0.60), f.p(xu, 0.55, 0.60), 1.10, 0.09, mat=dark)
    m.member(f.p(-2.15, 0, 0.66), f.p(2.55, 0, 0.66), 0.09, 0.09, mat=dark)  # 传动轴
    # 四轮
    for sv in (-1, 1):
        _wheel(m, f, 2.55, sv * 0.98, 0.52, 0.32)
        _wheel(m, f, -2.15, sv * 0.98, 0.52, 0.32)
    # 驾驶室：主箱 + 顶盖 + 引擎罩 + 格栅 + 保险杠
    m.box((1.6, 2.2, 2.25), at=f.p(1.9, 0, 0.85), rz=f.yaw(), mat=rust)
    m.box((1.72, 2.3, 0.09), at=f.p(1.9, 0, 3.10), rz=f.yaw(), mat=dark)
    m.box((0.75, 1.75, 0.85), at=f.p(3.07, 0, 0.90), rz=f.yaw(), mat=rust)
    m.box((0.06, 1.5, 0.5), at=f.p(3.46, 0, 1.12), rz=f.yaw(), mat=dark)
    m.member(f.p(3.55, -1.0, 0.52), f.p(3.55, 1.0, 0.52), 0.14, 0.18, mat="Kit_Iron")
    # 风挡（洞框 + 玻璃内嵌）+ 雨刷 + 侧窗 + 门缝线 + 门把手
    m.box((0.06, 1.5, 0.68), at=f.p(2.72, 0, 2.02), rz=f.yaw(), rx=-0.14, mat="Kit_GlassDark")
    for sv in (-1, 1):
        m.member(f.p(2.76, sv * 0.25, 2.62), f.p(2.78, sv * 0.75, 2.28), 0.03, 0.03,
                 mat="Kit_Iron")
        m.box((0.18, 0.14, 0.14), at=f.p(3.44, sv * 0.62, 1.62), rz=f.yaw(), mat=dark)
    for zu, hh in ((2.66, 0.08), (1.98, 0.08)):
        m.box((0.08, 1.62, hh), at=f.p(2.72, 0, zu), rz=f.yaw(), mat=dark)
    for sv in (-1, 1):
        m.box((0.08, 0.07, 0.62), at=f.p(2.70, sv * 0.78, 2.32), rz=f.yaw(), mat=dark)
        m.box((0.95, 0.05, 0.42), at=f.p(1.95, sv * 1.115, 2.10), rz=f.yaw(),
              mat="Kit_GlassDark")
        m.box((0.018, 0.02, 1.05), at=f.p(1.68, sv * 1.115, 1.05), rz=f.yaw(), mat=dark)
        m.box((0.16, 0.05, 0.05), at=f.p(1.55, sv * 1.13, 1.62), rz=f.yaw(), mat="Kit_Iron")
    # 锈蚀痕：驾驶室竖泪痕 + 引擎盖锈斑 + 货斗下沿锈带
    for sv in (-1, 1):
        m.box((0.30, 0.015, 1.10), at=f.p(2.15, sv * 1.118, 1.15), rz=f.yaw(),
              mat=dark)
        m.box((0.45, 0.015, 0.30), at=f.p(1.35, sv * 1.118, 0.95), rz=f.yaw(),
              mat=dark)
    m.box((0.60, 1.30, 0.02), at=f.p(3.10, 0.18, 1.76), rz=f.yaw(), mat=dark)
    m.box((4.05, 0.02, 0.22), at=f.p(-1.15, 1.105, 1.13), rz=f.yaw(), mat=dark)
    # 排气筒 + 后视镜
    m.cyl(0.055, 2.3, at=f.p(1.02, -0.85, 0.90), seg=10, mat="Kit_Iron")
    m.cyl(0.075, 0.05, at=f.p(1.02, -0.85, 3.18), seg=10, mat=dark)
    for sv in (-1, 1):
        m.member(f.p(2.62, sv * 1.16, 2.55), f.p(2.55, sv * 1.38, 2.55), 0.05, 0.05,
                 mat="Kit_Iron")
        m.box((0.06, 0.16, 0.30), at=f.p(2.55, sv * 1.42, 2.52), rz=f.yaw(), mat=dark)
    # 前轮挡泥板
    for sv in (-1, 1):
        m.box((1.25, 0.34, 0.08), at=f.p(2.55, sv * 1.02, 1.14), rz=f.yaw(), rx=0.10,
              mat=dark)
    # 货斗：底板 + 前挡 + 两侧竖条侧板 + 上沿 + 尾板放下
    m.box((4.15, 2.2, 0.09), at=f.p(-1.15, 0, 1.02), rz=f.yaw(), mat=rust)
    m.box((0.07, 2.2, 0.80), at=f.p(0.96, 0, 1.11), rz=f.yaw(), mat=rust)
    for sv in (-1, 1):
        m.box((4.15, 0.07, 0.72), at=f.p(-1.15, sv * 1.065, 1.11), rz=f.yaw(), mat=rust)
        m.box((4.15, 0.10, 0.06), at=f.p(-1.15, sv * 1.065, 1.83), rz=f.yaw(), mat=dark)
        for k in range(9):
            xu = -3.05 + k * 0.51
            m.box((0.07, 0.05, 0.64), at=f.p(xu, sv * 1.105, 1.14), rz=f.yaw(), mat=dark)
            m.box((0.07, 0.05, 0.60), at=f.p(xu, sv * 1.02, 1.15), rz=f.yaw(),
                  mat=dark)                                                 # 内衬筋
    # 侧挂油箱 + 工具箱 + 踏板
    m.cyl(0.30, 1.05, at=f.p(-0.55, -1.30, 0.72), rz=f.yaw(), seg=16, mat="Kit_Iron")
    m.cyl(0.30, 0.04, at=f.p(0.50, -1.30, 0.72), rz=f.yaw(), seg=16, mat=dark)
    m.box((0.60, 0.35, 0.40), at=f.p(0.30, 1.26, 0.78), rz=f.yaw(), mat=dark)
    m.member(f.p(1.62, -0.95, 0.42), f.p(1.62, 0.95, 0.42), 0.26, 0.05, mat=dark)
    m.box((0.70, 2.06, 0.06), at=f.p(-3.58, 0, 0.10), rz=f.yaw(), rx=0.05, mat=rust)
    for sv in (-1, 1):
        m.box((0.62, 0.05, 0.05), at=f.p(-3.55, sv * 0.68, 0.16), rz=f.yaw(), mat=dark)
    m.member(f.p(-3.32, -1.0, 0.52), f.p(-3.32, 1.0, 0.52), 0.12, 0.16, mat=dark)
    for sv in (-1, 1):
        m.box((0.03, 0.40, 0.45), at=f.p(-2.72, sv * 0.98, 0.42), rz=f.yaw(), mat=dark)


# ---------------------------------------------------------------------------
# 叉车（车体 + 门架 + L 形货叉 + 4 小轮 + 座 + 顶棚）
# ---------------------------------------------------------------------------

def _forklift(m):
    f = Frame(9.8, -1.2, 117.0)
    body, iron, dark = "Kit_PaintYellow", "Kit_Iron", "Kit_RustDark"
    # 车体 + 配重（带散热鳍片）+ 机盖
    m.box((1.8, 0.9, 0.45), at=f.p(-0.15, 0, 0.28), rz=f.yaw(), mat=body)
    m.box((0.45, 0.95, 0.42), at=f.p(-1.05, 0, 0.24), rz=f.yaw(), mat=body)
    for k in range(5):                                            # 配重散热片
        xu = -1.22 + k * 0.075
        m.box((0.03, 0.80, 0.30), at=f.p(xu, 0, 0.30), rz=f.yaw(), mat=dark)
    m.box((0.72, 0.85, 0.30), at=f.p(0.18, 0, 0.73), rz=f.yaw(), mat=body)
    # 四轮（前大后小）
    for sv in (-1, 1):
        _wheel(m, f, 0.62, sv * 0.44, 0.24, 0.16, seg=14)
        _wheel(m, f, -0.85, sv * 0.42, 0.20, 0.14, seg=14)
    # 门架：两立柱 + 三道横梁
    for sv in (-1, 1):
        m.member(f.p(0.95, sv * 0.32, 0.20), f.p(0.95, sv * 0.32, 2.05), 0.09, 0.07,
                 mat=iron)
    for zj in (0.80, 1.45, 2.00):
        m.member(f.p(0.95, -0.32, zj), f.p(0.95, 0.32, zj), 0.08, 0.06, mat=iron)
    # 货叉：滑架 + 两片 L 形薄板（竖跟 + 平叉尖）
    m.box((0.06, 0.70, 0.38), at=f.p(1.00, 0, 0.55), rz=f.yaw(), mat=iron)
    for sv in (-1, 1):
        m.box((0.10, 0.14, 0.50), at=f.p(1.03, sv * 0.19, 0.08), rz=f.yaw(), mat=iron)
        m.box((0.80, 0.14, 0.055), at=f.p(1.45, sv * 0.19, 0.085), rz=f.yaw(), mat=iron)
    # 座 + 靠背 + 方向盘
    m.box((0.42, 0.46, 0.12), at=f.p(-0.42, 0, 0.75), rz=f.yaw(), mat=dark)
    m.box((0.10, 0.46, 0.42), at=f.p(-0.64, 0, 0.80), rz=f.yaw(), mat=dark)
    m.cyl(0.16, 0.04, at=f.p(0.28, 0, 1.06), rx=math.radians(55), seg=12, mat=iron)
    # 顶棚：四柱 + 棚板
    for su in (-1, 1):
        for sv in (-1, 1):
            m.member(f.p(-0.62 + su * 0.85, sv * 0.40, 1.10),
                     f.p(-0.62 + su * 0.85, sv * 0.40, 2.02), 0.06, 0.06, mat=iron)
    m.box((1.20, 1.00, 0.05), at=f.p(-0.20, 0, 2.02), rz=f.yaw(), mat="Kit_Rust")
    # 前灯
    for sv in (-1, 1):
        m.box((0.06, 0.12, 0.12), at=f.p(0.56, sv * 0.30, 0.62), rz=f.yaw(), mat=dark)


# ---------------------------------------------------------------------------
# 废料堆 ×2（土堆基 + 乱插管段 + 工字钢 + 木板交叉 + 中间破桶）
# ---------------------------------------------------------------------------

def _scrap_pile(m, cx, cy, seed, radius=2.0):
    rnd = random.Random(seed)
    m.sphere(radius, at=(cx, cy, 0.0), seg=24, rings=10, z_lo=-0.15, z_hi=0.12,
             mat="Kit_RockDark")
    mats = ["Kit_Rust", "Kit_SteelPale", "Kit_RustDark", "Kit_SteelBlue"]
    # 乱插/横陈的管段（不同 Ø 与长度，一部分近乎立插）
    for i in range(17):
        ang = rnd.uniform(0, TAU)
        tilt = rnd.uniform(1.05, 1.35) if i % 3 == 0 else rnd.uniform(0.06, 0.45)
        r = rnd.uniform(0.05, 0.16)
        hl = rnd.uniform(0.8, 1.7)
        d = (math.cos(ang) * math.cos(tilt), math.sin(ang) * math.cos(tilt), math.sin(tilt))
        off = rnd.uniform(0.2, radius * 0.62)
        c = (cx + math.cos(ang + 1.3) * off, cy + math.sin(ang + 1.3) * off,
             0.18 + rnd.uniform(0.0, 0.55))
        a = (c[0] - d[0] * hl, c[1] - d[1] * hl, max(0.04, c[2] - d[2] * hl))
        b = (c[0] + d[0] * hl, c[1] + d[1] * hl, c[2] + d[2] * hl)
        m.tube([a, b], r, seg=16, mat=mats[i % 4])
        if i % 2 == 0:                                            # 端头法兰领
            fx, fy, fz = b
            m.cyl(r * 1.5, 0.05, at=(fx - d[0] * 0.025, fy - d[1] * 0.025,
                                     max(0.02, fz - d[2] * 0.025)),
                  ry=math.pi / 2, rz=ang, seg=12, mat="Kit_RustDark")
    # 工字钢 ×3
    m.ibeam((cx - 1.4, cy - 0.6, 0.55), (cx + 1.5, cy + 0.9, 0.30), h=0.26,
            mat="Kit_Rust", roll=rnd.uniform(0, TAU))
    m.ibeam((cx - 0.7, cy + 1.2, 0.90), (cx + 1.2, cy - 1.3, 0.62), h=0.20,
            mat="Kit_SteelBlue", roll=rnd.uniform(0, TAU))
    m.ibeam((cx - 1.7, cy + 0.3, 0.35), (cx + 0.4, cy - 1.6, 0.16), h=0.18,
            mat="Kit_RustDark", roll=rnd.uniform(0, TAU))
    # 木板交叉堆叠
    wood = ["Kit_WoodMid", "Kit_WoodDark"]
    for i in range(12):
        ang = rnd.uniform(0, TAU)
        tilt = rnd.uniform(-0.28, 0.28)
        hl = rnd.uniform(0.75, 1.15)
        w = rnd.uniform(0.14, 0.22)
        z = 0.25 + rnd.uniform(0.0, 0.75)
        d = (math.cos(ang) * math.cos(tilt), math.sin(ang) * math.cos(tilt), math.sin(tilt))
        c = (cx + rnd.uniform(-0.6, 0.6), cy + rnd.uniform(-0.6, 0.6), z)
        m.member((c[0] - d[0] * hl, c[1] - d[1] * hl, max(0.06, c[2] - d[2] * hl)),
                 (c[0] + d[0] * hl, c[1] + d[1] * hl, c[2] + d[2] * hl),
                 w, 0.028, mat=wood[i % 2])
    # 中间破桶（半埋敞口）+ 破口翻边
    bz = 0.30
    m.cyl(0.30, 0.55, at=(cx - 0.15, cy + 0.10, -0.14), seg=16, mat="Kit_RustDark")
    m.cyl(0.26, 0.02, at=(cx - 0.15, cy + 0.10, bz - 0.16), seg=16, mat="Kit_RockDark")
    for k in range(3):
        ang = TAU * k / 3.0 + 0.4
        px, py = cx - 0.15 + math.cos(ang) * 0.30, cy + 0.10 + math.sin(ang) * 0.30
        m.box((0.15, 0.014, 0.13), at=(px, py, bz - 0.06), rz=ang, rx=-1.1,
              mat="Kit_RustDark")
    # 碎块
    for i in range(7):
        sx = cx + rnd.uniform(-1.5, 1.5)
        sy = cy + rnd.uniform(-1.5, 1.5)
        s = rnd.uniform(0.15, 0.38)
        m.box((s, s * rnd.uniform(0.6, 1.0), s * 0.7), at=(sx, sy, rnd.uniform(0.0, 0.10)),
              rz=rnd.uniform(0, TAU), mat=["Kit_RockMid", "Kit_ConcreteDark"][i % 2])


def _scrap_piles(m):
    _scrap_pile(m, -5.5, -5.8, seed=11)
    _scrap_pile(m, -14.2, -6.5, seed=23)


# ---------------------------------------------------------------------------
# 电线杆 ×4 + 悬链电缆（一段断落拖地）
# ---------------------------------------------------------------------------

def _pole(m, x, y, lean_rx=0.0, brace=False):
    """木杆：锥度杆身 + 横担 + 斜撑 + 3 绝缘子 + 根部土台 + 帮桩撑杆。
    lean_rx 绕 X 前倾（弧度，正 = 倒向 -Y）。返回电缆挂点 (x, y', z)。"""
    h = 7.2
    m.cyl(0.10, h, at=(x, y, -0.05), base_r=0.135, seg=12, rx=lean_rx,
          mat="Kit_WoodDark")
    m.cyl(0.17, 0.18, at=(x, y, -0.02), seg=10, mat="Kit_SandDark")   # 根部土台
    shift = math.sin(lean_rx)                                          # 每米前移量
    ay = y - shift * 6.28
    m.member((x, ay - 1.15, 6.28), (x, ay + 1.15, 6.28), 0.09, 0.07, mat="Kit_WoodDark")
    for sv in (-1, 1):
        m.member((x, ay + sv * 0.58, 6.26), (x, y - shift * 5.72, 5.72), 0.05, 0.04,
                 mat="Kit_WoodDark")
    if brace:                                                          # 帮桩斜撑
        m.member((x, y - 1.5, 0.02), (x, y - 0.06, 3.1), 0.14, 0.11, mat="Kit_WoodDark")
        m.member((x, y - 1.52, 0.02), (x, y - 1.02, 0.06), 0.18, 0.12, mat="Kit_RockDark")
    for vy in (-0.85, 0.0, 0.85):
        m.cyl(0.045, 0.13, at=(x, ay + vy, 6.315), seg=8, mat="Kit_GlassDark")
        m.cyl(0.018, 0.06, at=(x, ay + vy, 6.30), seg=6, mat="Kit_Iron")
    return (x, ay, 6.46)


def _catenary(m, p0, p1, z_att, a, seg, r, mat="Kit_Iron"):
    """悬链电缆：z(s) = z_att - a(cosh(L/2a) - cosh((s-1/2)L/a))，中点垂度最大。"""
    lx, ly = p1[0] - p0[0], p1[1] - p0[1]
    L = math.hypot(lx, ly)
    pts = []
    for i in range(seg + 1):
        s = i / float(seg)
        z = z_att - a * (math.cosh(L / (2 * a)) - math.cosh((s - 0.5) * L / a))
        pts.append((p0[0] + lx * s, p0[1] + ly * s, z))
    m.tube(pts, r, seg=6, mat=mat)


def _pole_line(m):
    P1 = _pole(m, -14.5, 5.0, brace=True)
    P2 = _pole(m, -4.5, 4.7, lean_rx=0.0698)                      # 前倾 4 度
    P3 = _pole(m, 5.2, 4.9, brace=True)
    P4 = _pole(m, 14.9, 5.0)                                      # 靠棚，不加帮桩
    _catenary(m, P1[:2], P2[:2], 6.46, a=16.0, seg=30, r=0.028)
    _catenary(m, P2[:2], P3[:2], 6.46, a=16.0, seg=30, r=0.028)
    # P3->P4 段断落：从 P3 挂点坠向地面并拖地一段
    drop = [P3, (5.75, 4.98, 5.55), (6.3, 5.05, 4.5), (6.8, 5.12, 3.3),
            (7.25, 5.2, 2.1), (7.65, 5.28, 1.05), (7.98, 5.34, 0.35),
            (8.3, 5.4, 0.07), (8.8, 5.45, 0.06), (9.5, 5.35, 0.10),
            (10.2, 5.2, 0.06), (10.9, 5.05, 0.12), (11.5, 4.95, 0.06)]
    m.tube(drop, 0.028, seg=10, mat="Kit_Iron")
    # P4 残桩（断口垂下一截）
    m.tube([P4, (14.93, 5.02, 5.6), (14.96, 5.05, 4.9)], 0.028, seg=10, mat="Kit_Iron")


# ---------------------------------------------------------------------------
# 散落管段 ×8 / 木托盘 ×4 / 警示牌 ×2 / 路障锥 ×5
# ---------------------------------------------------------------------------

def _scattered_pipes(m):
    """8 根散管：不同 Ø/长/朝向，半陷进地坪，部分带法兰领。"""
    spec = [
        (-1.5, -6.8, 20.0, 0.11, 2.4), (0.9, -5.4, 70.0, 0.16, 2.0),
        (5.8, -6.8, 80.0, 0.13, 2.6), (11.8, -5.6, 40.0, 0.09, 2.2),
        (14.6, -2.4, 85.0, 0.13, 2.0), (-10.8, -4.2, -25.0, 0.18, 2.4),
        (-15.0, 6.9, 20.0, 0.10, 2.6), (6.2, 7.6, 60.0, 0.15, 2.2),
    ]
    mats = ["Kit_Rust", "Kit_SteelPale", "Kit_RustDark", "Kit_SteelBlue",
            "Kit_Rust", "Kit_SteelPale", "Kit_RustDark", "Kit_Rust"]
    for i, (x, y, deg, r, ln) in enumerate(spec):
        ang = math.radians(deg)
        d = (math.cos(ang), math.sin(ang))
        z = r * 0.78                                              # 半陷
        m.cyl(r, ln, at=(x - d[0] * ln / 2, y - d[1] * ln / 2, z),
              ry=math.pi / 2, rz=ang, seg=18, mat=mats[i])
        for sgn in (1, -1):                                       # 双头法兰领
            m.cyl(r * 1.45, 0.05, at=(x + d[0] * sgn * (ln / 2 - 0.05),
                                      y + d[1] * sgn * (ln / 2 - 0.05), z - 0.025),
                  ry=math.pi / 2, rz=ang, seg=12, mat="Kit_RustDark")


def _pallet(m, x, y, rz=0.0, stacked=False):
    """木托盘 1.2×1.0：底铺板 / 布墩 / 面板 三层竖条。stacked 再叠一只错位盘。"""
    f = Frame(x, y, rz)
    for vy in (-0.45, -0.15, 0.15, 0.45):                         # 底层
        m.box((1.2, 0.10, 0.02), at=f.p(0, vy, 0.0), rz=f.yaw(), mat="Kit_WoodDark")
    for ux in (-0.52, 0.0, 0.52):                                 # 中层布墩
        m.box((0.09, 1.0, 0.075), at=f.p(ux, 0, 0.02), rz=f.yaw(), mat="Kit_WoodMid")
    for k in range(7):                                            # 面层
        vy = -0.45 + k * 0.15
        m.box((1.2, 0.13, 0.022), at=f.p(0, vy, 0.095), rz=f.yaw(), mat="Kit_WoodMid")
    if stacked:
        f2 = Frame(x + 0.06, y + 0.05, rz + 7.0)
        for vy in (-0.45, -0.15, 0.15, 0.45):
            m.box((1.2, 0.10, 0.02), at=f2.p(0, vy, 0.12), rz=f2.yaw(), mat="Kit_WoodDark")
        for ux in (-0.52, 0.0, 0.52):
            m.box((0.09, 1.0, 0.075), at=f2.p(ux, 0, 0.14), rz=f2.yaw(), mat="Kit_WoodMid")
        for k in range(7):
            vy = -0.45 + k * 0.15
            m.box((1.2, 0.13, 0.022), at=f2.p(0, vy, 0.215), rz=f2.yaw(), mat="Kit_WoodMid")


def _pallets(m):
    _pallet(m, -2.5, 6.2, rz=15.0)
    _pallet(m, 2.9, 5.4, rz=-10.0, stacked=True)
    _pallet(m, 12.4, 6.6, rz=80.0)
    _pallet(m, 7.6, 5.8, rz=-30.0)


def _sign(m, x, y, yaw_deg):
    """警示牌：立杆 + 背板 + 黄黑斜条带（平行四边形带沿板面法向挤出）。"""
    f = Frame(x, y, yaw_deg)
    n3 = (-f.s, f.c)                                              # 板面外法向
    m.cyl(0.035, 1.62, at=(x, y, 0.0), seg=10, mat="Kit_Iron")
    m.cyl(0.10, 0.06, at=(x, y, 0.0), seg=12, mat="Kit_Iron")     # 底座
    m.cyl(0.05, 0.05, at=(x, y, 1.60), seg=10, mat="Kit_Iron")
    m.box((1.06, 0.02, 0.76), at=f.p(0, -0.012, 1.52), rz=f.yaw(), mat="Kit_RustDark")
    for k in range(8):                                            # 斜条带
        zc = 1.60 + k * 0.082
        xl = -0.47 + k * 0.118
        pts = [(xl - 0.075, -0.104), (xl + 0.08, -0.104),
               (xl + 0.155 + 0.075, 0.104), (xl + 0.155 - 0.08, 0.104)]
        a = (x + n3[0] * -0.018, y + n3[1] * -0.018, zc)
        b = (x + n3[0] * 0.018, y + n3[1] * 0.018, zc)
        m.extrude_along(pts, a, b, mat="Kit_PaintYellow" if k % 2 == 0 else "Kit_RustDark")
    for zu in (1.515, 2.245):                                     # 边框
        m.box((1.10, 0.035, 0.05), at=f.p(0, 0.024, zu), rz=f.yaw(), mat="Kit_RustDark")
    for ux in (-0.53, 0.53):
        m.box((0.05, 0.035, 0.78), at=f.p(ux, 0.024, 1.88), rz=f.yaw(), mat="Kit_RustDark")


def _signs(m):
    _sign(m, 1.2, -7.6, 180.0)
    _sign(m, -8.6, 6.4, 160.0)


def _cone(m, x, y):
    """路障锥：黄锥身 + 浅色环带 + 方座。"""
    m.box((0.34, 0.34, 0.028), at=(x - 0.17, y - 0.17, 0.0), mat="Kit_PaintYellow")
    m.revolve([(0.155, 0.028), (0.150, 0.050), (0.052, 0.50), (0.046, 0.545)],
              at=(x, y, 0.0), seg=24, mat="Kit_PaintYellow")
    m.revolve([(0.098, 0.255), (0.118, 0.255), (0.100, 0.345), (0.082, 0.345)],
              at=(x, y, 0.0), seg=24, mat="Kit_ConcreteLight")


def _cones(m):
    for x, y in ((3.7, 5.9), (4.6, 6.5), (5.6, 5.8), (10.2, 6.6), (-0.6, -4.9)):
        _cone(m, x, y)


# ---------------------------------------------------------------------------
# 半塌料棚（4 柱 + 残梁 + 破顶板向南斜垂 + 木箱堆 + 塌落板）
# ---------------------------------------------------------------------------

def _shed(m):
    rust, dark = "Kit_Rust", "Kit_RustDark"
    cols = [(11.0, 4.4), (15.4, 4.4), (11.0, 0.2)]                # 东南柱断成短桩
    for x, y in cols:
        m.box((0.28, 0.28, 3.4), at=(x - 0.14, y - 0.14, 0.0), mat="Kit_ConcreteLight")
        m.box((0.36, 0.36, 0.10), at=(x - 0.18, y - 0.18, 0.0), mat="Kit_ConcreteMid")
    m.box((0.28, 0.28, 1.05), at=(15.4 - 0.14, 0.2 - 0.14, 0.0), rx=0.22,
          mat="Kit_ConcreteLight")                                 # 断桩（斜口）
    m.box((0.34, 0.30, 0.55), at=(15.75, -0.35, 0.0), rz=0.5, mat="Kit_ConcreteLight")
    # 残梁：北梁整、西梁半截、东梁折垂
    m.member((11.0, 4.4, 3.34), (15.4, 4.4, 3.34), 0.15, 0.15, mat=rust)
    m.member((11.0, 4.4, 3.30), (11.0, 3.25, 3.26), 0.13, 0.13, mat=rust)
    m.member((15.4, 4.4, 3.30), (15.32, 2.45, 1.70), 0.13, 0.13, mat=rust)
    # 山墙残墙：东墙半截（斜断口），西墙只剩底带
    m.box((0.05, 3.9, 1.00), at=(15.4, 2.3, 0.0), mat="Kit_SteelBlue")
    for yy, hh in ((0.75, 0.9), (1.35, 1.7), (2.05, 1.15), (2.75, 2.0), (3.45, 1.45)):
        m.extrude_along(_CORR, (15.4, yy, 1.0), (15.4, yy, hh), mat="Kit_SteelBlue",
                        roll=-math.pi / 2)                                    # 撕剩的墙筋
    m.box((0.05, 3.6, 0.30), at=(11.0, 2.2, 0.0), mat="Kit_SteelBlue")
    for yy in (1.2, 2.3, 3.3):                                    # 西墙残条
        m.extrude_along(_CORR, (11.0, yy, 0.0), (11.0, yy, 0.9 + yy * 0.25),
                        mat="Kit_SteelBlue", roll=math.pi / 2)
    for bx, by in ((12.4, 1.6), (13.6, 1.3), (11.9, 0.9), (14.2, 2.1), (13.0, 2.7)):
        m.box((0.22, 0.16, 0.12), at=(bx, by, 0.0), rz=(bx + by) * 0.7,
              mat="Kit_ConcreteDark")
    # 柱间残支撑
    m.member((11.0, 4.4, 3.20), (13.0, 4.4, 1.85), 0.10, 0.08, mat=dark)
    # 破顶板：4 块向南逐级斜垂（末块搭地）——同批褪色彩钢，仅第 3 块锈穿；
    # 每块轻微错缝（x 偏移）避免读成整齐阶梯
    slabs = [(4.30, 3.34, 3.10, 2.60, 0.00), (3.05, 2.55, 1.95, 1.65, 0.10),
             (1.90, 1.60, 0.85, 0.75, -0.14), (0.80, 0.70, -0.15, 0.06, 0.08)]
    for i, (y0, z0, y1, z1, xo) in enumerate(slabs):
        m.extrude_along([(-2.2, -0.03), (2.2, -0.03), (2.2, 0.03), (-2.2, 0.03)],
                        (13.2 + xo, y0, z0), (13.2 + xo, y1, z1),
                        mat="Kit_Rust" if i == 2 else "Kit_SteelBlue")
        for xt in (-1.4, 0.0, 1.4):                               # 板底檩条
            m.member((13.2 + xo + xt, y0 - 0.08, z0 - 0.07),
                     (13.2 + xo + xt, y1 + 0.08, z1 - 0.07), 0.09, 0.05, mat=dark)
    # 塌落散板：一块平贴地 + 一块斜搭在散板上
    m.extrude_along([(-2.2, -0.03), (2.2, -0.03), (2.2, 0.03), (-2.2, 0.03)],
                    (12.9, -0.2, 0.12), (12.9, -1.35, 0.05), mat="Kit_Rust")
    m.extrude_along([(-1.3, -0.025), (1.3, -0.025), (1.3, 0.025), (-1.3, 0.025)],
                    (11.4, -1.6, 0.55), (14.9, -1.0, 0.06), mat="Kit_ConcreteMid")
    # 木箱堆（棚下完好区 + 棚南缘露脸的一只）+ 倾倒箱
    m.box((0.85, 0.75, 0.62), at=(12.3 - 0.425, 3.6 - 0.375, 0.02), rz=0.08,
          mat="Kit_WoodMid")
    m.box((0.70, 0.65, 0.55), at=(13.4 - 0.35, 3.7 - 0.325, 0.02), rz=-0.12,
          mat="Kit_WoodDark")
    m.box((0.60, 0.58, 0.55), at=(12.35 - 0.30, 3.62 - 0.29, 0.66), rz=0.20,
          mat="Kit_WoodDark")
    m.box((0.75, 0.70, 0.50), at=(14.55 - 0.375, 0.9 - 0.35, 0.02), rz=0.05,
          mat="Kit_WoodMid")
    m.box((0.55, 0.50, 0.45), at=(10.6 - 0.275, 1.3 - 0.25, 0.02), rz=-0.5,
          mat="Kit_WoodDark")
    m.box((0.65, 0.55, 0.60), at=(11.85 - 0.325, 2.2 - 0.275, 0.02), rz=0.8, rx=0.18,
          mat="Kit_WoodDark")
    for px, py in ((12.02, 3.98), (13.06, 3.66)):                 # 箱盖压条
        m.box((0.66, 0.06, 0.04), at=(px - 0.33, py - 0.03, 0.86 if py < 3.7 else 0.66),
              rz=0.1, mat="Kit_WoodDark")


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def build(m):
    """SCRAP-06 南院杂物：全部图元进同一个 Mesher（装配脚本按 AREAS['props'] 平移）。"""
    _containers(m)
    _truck(m)
    _forklift(m)
    _drums(m)
    _scrap_piles(m)
    _pole_line(m)
    _scattered_pipes(m)
    _pallets(m)
    _signs(m)
    _cones(m)
    _shed(m)
