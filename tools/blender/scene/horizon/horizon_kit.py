# -*- coding: utf-8 -*-
"""horizon_kit.py —— 远景装饰 kit（HorizonKit，8 件）无头建模（Blender 纯程序化，零贴图）。

远景件 = 放在可玩区之外、纯剪影观感、无站面无碰撞（docs/大海域世界化.md §1/§2.1「舞台幕布」）。
设计契约：低多边形但轮廓精细——细节预算全部花在**轮廓层次**（山脊起伏 / 云团堆叠 / 鲸背弧线 /
螺旋盘绕 / 肋拱错落），不做表面小细节。体量巨大（40~200 单位宽），玩家在地图中心远眺时剪影成画。

八件资产（FBX 名固定）：
    DistantIsleS       远岛小   宽 40，2-3 山头，最高 8            Kit_FarNear
    DistantIsleM       远岛中   宽 90，脊线最高 16，1 处海蚀拱      Kit_FarNear + Kit_FarFar(远坡)
    DistantIsleL       远岛大   宽 180，层峦 3 深，最高 28          Kit_FarNear + Kit_FarFar
    CloudBankL         云带     80x20x8，7 团平底云               Kit_Cloud
    WhaleSurfacing     巨鲸露背 长 25，背弧出水 4，背鳍+尾鳍        Kit_FarNear
    LeviathanTentacle  利维坦触手 高 18，螺旋 2.5 圈，吸盘棱面环     Kit_FarNear
    GiantRibs          巨肋拱   7 根肋弧排 40 宽拱廊 + 断脊柱        Kit_FarNear
    FarFleet           远帆船队 3 艘小帆船（各 ~8 长）错落          Kit_FarSail(帆) + Kit_FarNear(船身)

【坐标口径（硬指标，Unity 侧 1 单位 = 1 米）】
    Blender 场景：+Z 为上，XY 为海平面（z=0 海平面）；原点 = 足印中心，底部插入水下。
    正面朝 -Y（玩家从地图中心向 -Y 远眺的主视方向），主展开轴沿 X。
    FBX 导出（axis_forward='-Z', axis_up='Y'）后 +Y 上 / 正面朝 -Z；
    Unity（bakeAxisConversion=true）后正面朝 Unity +Z，宽沿 Unity X。

【纪律】
    - 槽/色值/roughness/预算全部取自 style_tokens（禁止散写）；每件 ≤2 材质槽（任务硬约束）。
    - horizon 预算 ≤3000 tri/件（ST.POLY_BUDGETS['horizon']，ST.budget_guard 闸口）。
    - 全部面 shade_flat（smooth=False）——棱面低模剪影。
    - 无 standable manifest（远景不可达、不参与碰撞）。

复现（仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/horizon/horizon_kit.py -- [--only all|DistantIsleS|...] [--samples N] [--no-render]

产物：
    pirate-crew/Assets/Art/Models/WorldKit/Horizon/<名>.fbx        ×8
    export/worldkit-horizon/<名>-front34.jpg / <名>-side.jpg       ×16（1024² q90，相机拉远 150-400 模拟真实观景）
    external/worldkit-horizon-work/horizon_kit_debug.blend         （调参 GUI 缓存，gitignored）

幂等：重跑覆盖全部产物。控制台打印每件 STAT（tri/槽/包围盒），入 kit README。
"""

import math
import os
import random
import sys
import time

import bpy
import bmesh
import mathutils
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # -> tools/blender/scene/
import style_tokens as ST

# ============================================================================
# 参数区 —— 调参旋钮集中在此（README 按行号引用）
# ============================================================================

ONLY = 'all'            # 'all' | 8 个 FBX 名之一
SAMPLES = 64            # Cycles 采样数（预览图；远景剪影件面少，64 够净）
RENDER = True           # False = 只建模导出 FBX，不出预览图
RES = ST.PREVIEW_RES    # 1024
JPG_QUALITY = ST.PREVIEW_QUALITY  # 90
MAX_SLOTS = 2           # 任务硬约束：每件 ≤2 材质槽
LIGHT_BOOST = 2.5       # ST 预览三灯能量倍率（曝光微调旋钮，1.0=ST 原值；远景大件照度偏低实测调高）
CAM_LENS = 50           # 预览相机焦距（观景压缩感）

# ---- 观景相机（每件：dist=观景距离 150-400 模拟真实远眺；h=件代表高度，定相机/目标高度）----
CAM = {
    'DistantIsleS':      dict(dist=150, h=8.0),
    'DistantIsleM':      dict(dist=230, h=16.0),
    'DistantIsleL':      dict(dist=340, h=28.0),
    'CloudBankL':        dict(dist=175, h=7.0),
    'WhaleSurfacing':    dict(dist=150, h=5.0),
    'LeviathanTentacle': dict(dist=150, h=18.0),
    'GiantRibs':         dict(dist=165, h=10.0),
    'FarFleet':          dict(dist=150, h=8.0),
}

# ---- DistantIsleS 远岛小（t: 0..1 沿 X）----
ISLE_S = dict(
    x0=-20.0, x1=20.0, n=21, z_bot=-2.5,
    # 3 山头（cx,cw,amp）：峰窄而陡 + jag 高频锯齿 → 山脊线剪影起伏
    peaks=((0.24, 0.055, 6.2), (0.56, 0.075, 4.9), (0.84, 0.045, 3.4)),
    base=0.7,
    jag=((13.0, 1.7, 0.55), (5.3, 0.0, 0.45)),       # (freq, phase, amp) 正弦叠加小齿
    hw_base=1.6, hw_peaks=((0.24, 0.10, 3.4), (0.56, 0.13, 2.8), (0.84, 0.08, 1.8)), hw_min=1.2,
    y_amp=1.2, y_freq=1.2, y_phase=0.6,                # 平面走向微摆
)

# ---- DistantIsleM 远岛中（宽 90，脊线最高 16，海蚀拱跨山链豁口；FarNear 主体 + FarFar 远坡）----
ISLE_M = dict(
    # 主体拆两段放样，中间留豁口，海蚀拱跨在豁口上（洞口透光才可读）
    seg_a=dict(x0=-45.0, x1=5.0, n=19),
    seg_b=dict(x0=17.0, x1=45.0, n=15),
    z_bot=-3.0,
    peaks=((0.12, 0.060, 13.8), (0.30, 0.055, 9.6), (0.47, 0.035, 6.0), (0.80, 0.055, 11.2)),
    base=1.0,
    jag=((15.0, 0.9, 0.7), (6.1, 2.2, 0.5)),
    hw_base=2.6, hw_peaks=((0.12, 0.09, 6.4), (0.30, 0.10, 4.6), (0.80, 0.10, 5.8)), hw_min=2.2,
    y_amp=1.8, y_freq=1.0, y_phase=1.1,
    # 海蚀拱（世界坐标）：腿立在豁口两侧水中，弧顶探出山肩
    arch_lx=6.0, arch_rx=17.0, arch_leg_z=-2.5, arch_leg_top=4.6,
    arch_leg_r0=1.5, arch_leg_r1=1.15, arch_rise=5.2, arch_tube_r=1.15, arch_seg=12,
    # FarFar 远坡（横贯山链身后，豁口处透出远山）
    back_x0=-38.0, back_x1=53.0, back_n=19, back_y=18.0, back_z_bot=-2.5,
    back_peaks=((0.30, 0.085, 7.6), (0.55, 0.05, 4.8), (0.80, 0.07, 5.6)), back_base=1.2,
    back_jag=((11.0, 0.4, 0.35),),
    back_hw_base=3.0, back_hw_peaks=((0.30, 0.14, 6.0), (0.80, 0.12, 4.5)), back_hw_min=2.4,
)

# ---- DistantIsleL 远岛大（层峦 3 深：近 FarNear / 中、远 FarFar）----
ISLE_L = dict(
    near=dict(x0=-90.0, x1=90.0, n=43, z_bot=-3.0, y=0.0,
              peaks=((0.30, 0.055, 25.0), (0.50, 0.040, 15.8), (0.66, 0.055, 20.2), (0.87, 0.040, 9.5)),
              base=1.2, jag=((17.0, 1.2, 1.1), (7.3, 0.3, 0.7)),
              hw_base=3.5, hw_peaks=((0.30, 0.09, 9.0), (0.66, 0.10, 7.5)), hw_min=3.0,
              y_amp=2.2, y_freq=0.9, y_phase=0.5),
    mid=dict(x0=-70.0, x1=78.0, n=29, z_bot=-3.0, y=25.0,
             peaks=((0.24, 0.060, 14.0), (0.55, 0.045, 8.6), (0.80, 0.060, 10.4)),
             base=1.0, jag=((13.0, 0.6, 0.8), (5.7, 1.9, 0.5)),
             hw_base=5.0, hw_peaks=((0.24, 0.10, 7.0), (0.80, 0.09, 5.5)), hw_min=3.5,
             y_amp=1.5, y_freq=0.7, y_phase=2.0),
    far=dict(x0=-55.0, x1=85.0, n=21, z_bot=-3.0, y=50.0,
             peaks=((0.40, 0.100, 8.0), (0.74, 0.070, 5.2)),
             base=0.8, jag=((9.0, 0.2, 0.45),),
             hw_base=8.0, hw_peaks=((0.40, 0.15, 9.0), (0.74, 0.11, 6.0)), hw_min=5.0,
             y_amp=1.5, y_freq=0.5, y_phase=1.2),
)

# ---- CloudBankL 云带 ----
CLOUD = dict(
    n=7,
    xs=(-33.5, -22.5, -11.0, 0.5, 11.5, 22.5, 33.5),
    y_amp=4.5, y_freq=2.1,                       # 团心 y 摆动
    z_cut=0.6,                                   # 云底削平高度（低低压着海平线的积云带）
    z_top_max=8.4,                               # 顶钳制：bbox 高 ≈ 8 规格
    main_r=(6.0, 8.4),                           # 主球 rx 区间（ry≈0.7rx，rz≈0.45rx）
    sub_balls=(2, 3),                            # 每团副球数区间
    sub_scale=(0.42, 0.72),                      # 副球相对主球比例
    sub_dx=(2.5, 6.0), sub_dz=(0.0, 1.4),
    segs=9, rings=5,                             # 低多边形棱面球
    seed=7,
)

# ---- WhaleSurfacing 巨鲸露背 ----
WHALE = dict(
    x_tail=-12.5, x_head=12.5, n=17, sides=10,
    # 「背弧出水 4」指可见背弧（截面顶缘）最高 ≈4：轴峰 = 4 - 峰处截面半径
    # lift*(1-u)^4：尾端上翘项（巨鲸下潜前抬尾，尾鳍露出水面）
    axis_base=-2.9, axis_amp=4.6, axis_pow=0.9, axis_lift=2.3,
    r_max=2.35, r_pow_head=0.30, r_pow_tail=0.55,  # r(u) = r_max*u^a*(1-u)^b，归一
    rx_ratio=0.55,                                 # 截面椭圆扁率
    # 背鳍（XZ 轮廓，后弯镰形；竖直薄片）
    fin_y0=0.0, fin_t=0.5,
    fin_pts=((1.3, 3.0), (2.3, 3.35), (3.1, 5.05), (3.7, 5.2), (4.2, 3.45)),
    # 尾鳍（XY 轮廓水平两片，y 镜像；随抬尾立出水面上方——水线下会被海面挡住）
    fluke_z=0.55, fluke_t=0.45,
    fluke_pts=((0.0, 0.0), (1.4, 2.2), (3.3, 3.4), (2.3, 0.9), (0.7, 0.25)),
    fluke_x=-12.3,
)

# ---- LeviathanTentacle 利维坦触手 ----
TENTACLE = dict(
    turns=2.5, nseg=56, sides=8,
    r_base=6.8, r_shrink=3.6, xy_squash=0.82,     # 螺旋半径（y 向压扁）
    z_base=1.6, z_span=15.7, z_pow=1.08,          # z(q)=z_base+z_span*q^pow（顶 ≈17.3，+尖钩 ≈18；底圈骑在水面）
    tube_r0=2.35, tube_pow=0.8, tube_tip=0.30,    # 体半径沿 q 收细
    th0=0.9,                                      # 起始角（正面构图）
    suck_n=12, suck_q0=0.10, suck_dq=0.062,       # 吸盘：数量 / 起始 / 间隔（沿 q）
    suck_len=0.5, suck_r0=0.30, suck_r1=0.55, suck_sides=7, suck_up=0.55,  # 喇叭口朝内上
    hook_len=1.8, hook_r=0.26,                    # 尖端外弯小钩
)

# ---- GiantRibs 巨肋拱 ----
RIBS = dict(
    xs=(-12.6, -8.4, -4.2, 0.0, 4.2, 8.4, 12.6),
    radii=(6.4, 11.6, 9.2, 13.8, 9.6, 12.0, 7.0),     # 每根弧半径（拱廊起伏；弧顶 2.2~9.6 高耸）
    y_off_mul=1.6, y_off_freq=1.3,                    # 前后微错落
    cz=-4.2,                                          # 弧圆心 z（两端腿插水下）
    th0=-0.28, th1=math.pi + 0.30, narc=18,           # 弧角范围（>半圆，腿入水）与段数
    tube_r_mul=0.026, tube_r_add=0.75, sides=8,
    knob_r=1.30,                                      # 弧顶关节鼓包
    knob_segs=8, knob_rings=5,
    spine=dict(y=-3.4, z0=-1.2, z1=4.6, r0=1.7, r1=1.5,
               z2=6.6, r2=1.05, lean=0.3, sides=8),   # 断脊柱（两段错位=断口）
)

# ---- FarFleet 远帆船队 ----
FLEET = dict(
    ships=(  # (x, y, rot_deg, scale)
        (-13.0, 5.0, 12.0, 1.00),
        (1.5, -4.0, -6.0, 1.05),
        (14.0, 7.0, -24.0, 0.82),
    ),
    hull_len=8.0, hull_n=7,
    sec=(  # 船壳横剖面（y 相对半宽比例, z）
        (-0.42, -0.75), (0.0, -0.90), (0.42, -0.75),
        (0.62, 0.05), (0.45, 0.62), (-0.45, 0.62), (-0.62, 0.05),
    ),
    half_beam=1.15, sheer=0.30,
    mast_main=dict(x=-0.2, z0=0.5, z1=7.6, r0=0.15, r1=0.09),
    mast_aft=dict(x=-2.3, z0=0.5, z1=6.0, r0=0.12, r1=0.08),
    main_sail=dict(x=-0.2, top=7.1, w=3.4, h=2.9, bulge=0.5, rows=4, cols=5),
    aft_sail=dict(x=-2.3, top=5.6, w=2.7, h=2.2, bulge=0.4, rows=4, cols=4),
    jib=dict(tack=(3.8, 0, 2.5), head=(-0.2, 0, 6.9), clew=(0.9, 0, 1.5), rows=4, cols=4, bulge=0.3),
    sprit=dict(x0=3.4, z0=0.7, dx=1.9, dz=0.9, r0=0.09, r1=0.05),   # 艏斜桁
)

# ============================================================================
# 路径与控制台
# ============================================================================

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))  # horizon/ 比 scene/ 深一级
FBX_DIR = os.path.join(ROOT, 'pirate-crew', 'Assets', 'Art', 'Models', 'WorldKit', 'Horizon')
PREVIEW_DIR = os.path.join(ROOT, 'export', 'worldkit-horizon')
WORK_DIR = os.path.join(ROOT, 'external', 'worldkit-horizon-work')

T0 = time.time()


def log(msg):
    print('[horizonkit] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


def parse_args():
    global ONLY, SAMPLES, RENDER
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == '--only':
            ONLY = argv[i + 1]; i += 2
        elif a == '--samples':
            SAMPLES = int(argv[i + 1]); i += 2
        elif a == '--no-render':
            RENDER = False; i += 1
        else:
            i += 1


# ============================================================================
# 几何累积器与图元 helper（口径沿 tools/blender/scene/build_scene_kit.py 样板）
# ============================================================================

class MeshAcc:
    """单件资产几何累积器。mat 序号 = 该件槽名列表下标。"""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.face_mat = []
        self.face_smooth = []

    def add(self, verts, faces, mat, smooth=False):
        base = len(self.verts)
        self.verts.extend(tuple(v) for v in verts)
        for f in faces:
            self.faces.append(tuple(base + i for i in f))
            self.face_mat.append(mat)
            self.face_smooth.append(smooth)


def merge(dst, src, xf=None, mat_map=None):
    """把 src 的几何经变换 xf（可 None）并入 dst；mat_map 重映射槽序号。"""
    base = len(dst.verts)
    if xf is None:
        dst.verts.extend(tuple(v) for v in src.verts)
    else:
        dst.verts.extend(tuple(xf(v)) for v in src.verts)
    for f, m, s in zip(src.faces, src.face_mat, src.face_smooth):
        dst.faces.append(tuple(base + i for i in f))
        dst.face_mat.append(mat_map[m] if mat_map else m)
        dst.face_smooth.append(s)


def loft_closed(acc, rings, mat):
    """闭合环放样：rings 等长点列表，相邻环连四边形，首尾加 n-gon 端盖。全部 flat。"""
    n = len(rings[0])
    verts = []
    for ring in rings:
        verts.extend(ring)
    faces = []
    for s in range(len(rings) - 1):
        o, o2 = s * n, (s + 1) * n
        for i in range(n):
            i2 = (i + 1) % n
            faces.append((o + i, o + i2, o2 + i2, o2 + i))
    faces.append(tuple(range(n - 1, -1, -1)))                 # 起端盖
    faces.append(tuple(range((len(rings) - 1) * n, len(rings) * n)))  # 末端盖
    acc.add(verts, faces, mat)


def ridge_rings(x0, x1, n, h_fn, hw_fn, yc_fn, z_bot):
    """山脊放样环：沿 X 取 n 站，每站 6 点山形剖面（水线-肩-脊-肩-水线，flat 棱面感）。
    肩点高/横向比例偏陡（0.65 / 0.72）→ 山体读得出「耸立」而非土墩。"""
    rings = []
    for s in range(n):
        t = s / (n - 1)
        x = x0 + (x1 - x0) * t
        h = h_fn(t); hw = hw_fn(t); yc = yc_fn(t)
        span = h - z_bot
        rings.append([
            (x, yc - hw, z_bot),
            (x, yc - hw * 0.72, z_bot + span * 0.65),
            (x, yc - hw * 0.35, h),
            (x, yc + hw * 0.35, h),
            (x, yc + hw * 0.72, z_bot + span * 0.65),
            (x, yc + hw, z_bot),
        ])
    return rings


def ax_box(acc, center, size, mat, rot_z=0.0):
    """轴对齐盒（可绕 z 旋转）。"""
    cx, cy, cz = center
    sx, sy, sz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    ca, sa = math.cos(rot_z), math.sin(rot_z)
    pts = []
    for dx in (-sx, sx):
        for dy in (-sy, sy):
            for dz in (-sz, sz):
                pts.append((cx + dx * ca - dy * sa, cy + dx * sa + dy * ca, cz + dz))
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    acc.add(pts, faces, mat)


def ax_tube(acc, p0, p1, r1, r2, sides, mat, caps=True):
    """圆台/圆柱/圆杆：p0→p1 轴线，r1/r2 两端半径。flat。"""
    d = Vector(p1) - Vector(p0)
    if d.length < 1e-6:
        return
    d = d.normalized()
    up = Vector((0, 0, 1))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0, 1, 0))
    u = d.cross(up).normalized()
    v = d.cross(u).normalized()
    c0, c1 = Vector(p0), Vector(p1)
    ring0 = [c0 + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * r1
             for k in range(sides)]
    ring1 = [c1 + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * r2
             for k in range(sides)]
    pts = [tuple(p) for p in ring0 + ring1]
    faces = [(k, (k + 1) % sides, sides + (k + 1) % sides, sides + k) for k in range(sides)]
    if caps:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range(sides, 2 * sides)))
    acc.add(pts, faces, mat)


def tube_chain(acc, pts, radii, sides, mat, caps=True):
    """沿点列的连续管：相邻点成段、段间不重复封盖（面数友好），radii 与 pts 等长。"""
    n = len(pts)
    ring_list = []
    for i in range(n):
        pa = pts[max(0, i - 1)]
        pb = pts[min(n - 1, i + 1)]
        d = Vector(pb) - Vector(pa)
        d = d.normalized() if d.length > 1e-9 else Vector((0, 0, 1))
        up = Vector((0, 0, 1))
        if abs(d.dot(up)) > 0.95:
            up = Vector((0, 1, 0))
        u = d.cross(up).normalized()
        v = d.cross(u).normalized()
        c = Vector(pts[i])
        ring_list.append([tuple(c + (u * math.cos(2 * math.pi * k / sides) +
                                      v * math.sin(2 * math.pi * k / sides)) * radii[i])
                          for k in range(sides)])
    verts = []
    for ring in ring_list:
        verts.extend(ring)
    faces = []
    m = len(ring_list)
    for s in range(m - 1):
        o, o2 = s * sides, (s + 1) * sides
        for k in range(sides):
            k2 = (k + 1) % sides
            faces.append((o + k, o + k2, o2 + k2, o2 + k))
    if caps:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range((m - 1) * sides, m * sides)))
    acc.add(verts, faces, mat)


def ring_frame(p0, p1, sides, r0, r1):
    """两环点阵（p0 半径 r0 / p1 半径 r1），轴向 frame 由 p0→p1 推出。"""
    d = Vector(p1) - Vector(p0)
    if d.length < 1e-9:
        d = Vector((0, 0, 1))
    d = d.normalized()
    up = Vector((0, 0, 1))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0, 1, 0))
    u = d.cross(up).normalized()
    v = d.cross(u).normalized()
    out = []
    for p, r in ((p0, r0), (p1, r1)):
        c = Vector(p)
        out.append([tuple(c + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * r)
                    for k in range(sides)])
    return out


def ax_grid(acc, points, rows, cols, mat):
    """rows×cols 点阵连四边形条带（微鼓弧面帆）。points 是行优先二维列表。flat。"""
    verts = []
    for row in points:
        verts.extend(row)
    acc.add(verts,
            [(j * cols + i, j * cols + i + 1, (j + 1) * cols + i + 1, (j + 1) * cols + i)
             for j in range(rows - 1) for i in range(cols - 1)],
            mat)


def uv_sphere(acc, center, rx, ry, rz, segs, rings, mat, z_cut=None):
    """低多边形椭球（棱面）；z_cut 把底部削平（云底平切），底口加 n-gon 盖。"""
    cx, cy, cz = center
    # φ 从顶(0)到底(π)；z = cz + rz*cosφ
    phi_end = math.pi
    if z_cut is not None:
        c = (z_cut - cz) / rz
        if c >= 1.0:
            return                                   # 整球在切面之下：不可见，跳过
        if c > -1.0:
            phi_end = math.acos(c)                   # 切面穿过球体：只保留切面以上
        # c <= -1.0：切面在球底之下 → 完整球
    K = max(2, rings - 1)
    pt_rings = []
    for k in range(1, K + 1):
        phi = phi_end * k / K
        sp = math.sin(phi)
        pt_rings.append([(cx + rx * sp * math.cos(2 * math.pi * i / segs),
                          cy + ry * sp * math.sin(2 * math.pi * i / segs),
                          cz + rz * math.cos(phi)) for i in range(segs)])
    verts = [(cx, cy, cz + rz)]                      # 顶极点
    for ring in pt_rings:
        verts.extend(ring)
    faces = []
    for i in range(segs):                            # 顶扇
        faces.append((0, 1 + (i + 1) % segs, 1 + i))
    for k in range(K - 1):                           # 环间
        o, o2 = 1 + k * segs, 1 + (k + 1) * segs
        for i in range(segs):
            i2 = (i + 1) % segs
            faces.append((o + i, o + i2, o2 + i2, o2 + i))
    base_last = 1 + (K - 1) * segs                   # 底口 n-gon
    faces.append(tuple(range(base_last, base_last + segs)))
    acc.add(verts, faces, mat)


def prism_xz(acc, pts_xz, y0, thick, mat):
    """XZ 轮廓多边形沿 Y 挤薄片（背鳍等竖直片）。"""
    n = len(pts_xz)
    a = [(x, y0 - thick * 0.5, z) for x, z in pts_xz]
    b = [(x, y0 + thick * 0.5, z) for x, z in pts_xz]
    faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    faces.append(tuple(range(n - 1, -1, -1)))
    faces.append(tuple(range(n, 2 * n)))
    acc.add(a + b, faces, mat)


def prism_xy(acc, pts_xy, z0, thick, mat):
    """XY 轮廓多边形沿 Z 挤薄片（尾鳍等水平片）。"""
    n = len(pts_xy)
    a = [(x, y, z0 - thick * 0.5) for x, y in pts_xy]
    b = [(x, y, z0 + thick * 0.5) for x, y in pts_xy]
    faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    faces.append(tuple(range(n - 1, -1, -1)))
    faces.append(tuple(range(n, 2 * n)))
    acc.add(a + b, faces, mat)


# ============================================================================
# 数学小工具
# ============================================================================

def peaks(t, ps):
    """高斯峰叠加：(center, width, amp)*n → 山脊起伏轮廓。"""
    return sum(a * math.exp(-0.5 * ((t - c) / w) ** 2) for c, w, a in ps)


def jagged(t, jags):
    """高频正弦叠加：(freq, phase, amp)*n → 山脊线小锯齿（远景剪影的轮廓细节）。"""
    return sum(amp * math.sin(2 * math.pi * freq * t + ph) for freq, ph, amp in jags)


def srgb_to_linear(hex_str):
    """sRGB hex → 线性 RGBA（Blender Principled 吃线性值；色值本身取自 style_tokens）。"""
    def f(v):
        v = v / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = ST.hex_to_rgb(hex_str)
    return (f(r * 255), f(g * 255), f(b * 255), 1.0)


def make_materials(slot_names):
    out = []
    for name in slot_names:
        spec = ST.slot(name)                          # 未登记槽名直接抛错
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = srgb_to_linear(spec['hex'])
        bsdf.inputs['Roughness'].default_value = spec['roughness']
        out.append((name, mat))
    return out


# ============================================================================
# ① DistantIsleS 远岛小（宽 40，2-3 山头，最高 8）
# ============================================================================

def build_distant_isle_s(acc, mi):
    p = ISLE_S
    h_fn = lambda t: max(0.4, p['base'] + peaks(t, p['peaks']) + jagged(t, p['jag']))
    hw_fn = lambda t: max(p['hw_min'],
                          (p['hw_base'] + peaks(t, p['hw_peaks'])) * (math.sin(math.pi * t) ** 0.30))
    yc_fn = lambda t: p['y_amp'] * math.sin(2 * math.pi * p['y_freq'] * t + p['y_phase'])
    loft_closed(acc, ridge_rings(p['x0'], p['x1'], p['n'], h_fn, hw_fn, yc_fn, p['z_bot']),
                mi['Kit_FarNear'])


# ============================================================================
# ② DistantIsleM 远岛中（宽 90，脊线最高 16，1 处海蚀拱；FarNear 主体 + FarFar 远坡）
# ============================================================================

def build_distant_isle_m(acc, mi):
    p = ISLE_M
    mfn = mi['Kit_FarNear']

    def h_fn(t):
        return max(0.5, p['base'] + peaks(t, p['peaks']) + jagged(t, p['jag']))

    def hw_fn(t):
        return max(p['hw_min'],
                   (p['hw_base'] + peaks(t, p['hw_peaks'])) * (math.sin(math.pi * t) ** 0.30))

    def yc_fn(t):
        return p['y_amp'] * math.sin(2 * math.pi * p['y_freq'] * t + p['y_phase'])

    # 主体两段放样，中间豁口留给海蚀拱（洞口透光）
    for seg in (p['seg_a'], p['seg_b']):
        loft_closed(acc, ridge_rings(seg['x0'], seg['x1'], seg['n'], h_fn, hw_fn, yc_fn, p['z_bot']),
                    mfn)

    # ---- 海蚀拱：两腿立豁口两侧水中 + 半圆拱弧探出山肩 ----
    lx, rx_ = p['arch_lx'], p['arch_rx']
    ax_tube(acc, (lx, 0, p['arch_leg_z']), (lx, 0, p['arch_leg_top']),
            p['arch_leg_r0'], p['arch_leg_r1'], 8, mfn)
    ax_tube(acc, (rx_, 0, p['arch_leg_z']), (rx_, 0, p['arch_leg_top']),
            p['arch_leg_r0'], p['arch_leg_r1'], 8, mfn)
    cx, span = (lx + rx_) * 0.5, (rx_ - lx) * 0.5
    arc = []
    for s in range(p['arch_seg'] + 1):
        th = math.pi * (1.0 - s / p['arch_seg'])     # π→0：左腿顶到右腿顶
        arc.append((cx + span * math.cos(th), 0, p['arch_leg_top'] + p['arch_rise'] * math.sin(th)))
    tube_chain(acc, arc, [p['arch_tube_r']] * (p['arch_seg'] + 1), 8, mfn)

    # ---- FarFar 远坡（横贯山链身后，豁口处透出远山）----
    bh = lambda t: max(0.5, p['back_base'] + peaks(t, p['back_peaks']) + jagged(t, p['back_jag']))
    bw = lambda t: max(p['back_hw_min'],
                       (p['back_hw_base'] + peaks(t, p['back_hw_peaks'])) * (math.sin(math.pi * t) ** 0.30))
    loft_closed(acc, ridge_rings(p['back_x0'], p['back_x1'], p['back_n'], bh, bw,
                                 lambda t: p['back_y'], p['back_z_bot']),
                mi['Kit_FarFar'])


# ============================================================================
# ③ DistantIsleL 远岛大（宽 180，层峦 3 深，最高 28）
# ============================================================================

def build_distant_isle_l(acc, mi):
    p = ISLE_L
    for key, slot_name in (('near', 'Kit_FarNear'), ('mid', 'Kit_FarFar'), ('far', 'Kit_FarFar')):
        q = p[key]
        h_fn = lambda t, q=q: max(0.5, q['base'] + peaks(t, q['peaks']) + jagged(t, q['jag']))
        hw_fn = lambda t, q=q: max(q['hw_min'],
                                   (q['hw_base'] + peaks(t, q['hw_peaks'])) * (math.sin(math.pi * t) ** 0.28))
        yc_fn = lambda t, q=q: q['y'] + q['y_amp'] * math.sin(2 * math.pi * q['y_freq'] * t + q['y_phase'])
        loft_closed(acc, ridge_rings(q['x0'], q['x1'], q['n'], h_fn, hw_fn, yc_fn, q['z_bot']),
                    mi[slot_name])


# ============================================================================
# ④ CloudBankL 云带（80×20×8，7 团平底棱面云）
# ============================================================================

def build_cloud_bank(acc, mi):
    p = CLOUD
    rng = random.Random(p['seed'])
    m = mi['Kit_Cloud']
    for i, cx in enumerate(p['xs']):
        cy = p['y_amp'] * math.sin(p['y_freq'] * i + 0.7)
        rx = rng.uniform(*p['main_r'])
        ry = rx * 0.70
        rz = min(rx * 0.45, p['z_top_max'] - p['z_cut'] - 0.6)
        # 球心压低：球底伸到削平面之下 → 底部被 z_cut 削平成「云滩」；同时顶不超规格
        cz = min(p['z_cut'] + rng.uniform(0.35, 0.75) * rz + rng.uniform(0.8, 1.8),
                 p['z_top_max'] - rz - 0.05)
        uv_sphere(acc, (cx, cy, cz), rx, ry, rz, p['segs'], p['rings'], m, z_cut=p['z_cut'])
        for _ in range(rng.randint(*p['sub_balls'])):
            s = rng.uniform(*p['sub_scale'])
            dx = rng.uniform(*p['sub_dx']) * (1 if rng.random() > 0.5 else -1)
            dy = rng.uniform(-1.8, 1.8)
            dz = rng.uniform(*p['sub_dz'])
            rs = rz * s
            # 副球顶钳到规格高度内
            uv_sphere(acc, (cx + dx, cy + dy, min(cz + dz, p['z_top_max'] - rs)),
                      rx * s, ry * s, rs, max(7, p['segs'] - 2), max(4, p['rings'] - 1), m,
                      z_cut=p['z_cut'])


# ============================================================================
# ⑤ WhaleSurfacing 巨鲸露背（长 25，背弧出水 4，背鳍 + 尾鳍）
# ============================================================================

def build_whale(acc, mi):
    p = WHALE
    m = mi['Kit_FarNear']
    xl, xr = p['x_tail'], p['x_head']

    def axis(u):
        return (xl + (xr - xl) * u, 0.0,
                p['axis_base'] + p['axis_amp'] * math.sin(math.pi * (u ** p['axis_pow']))
                + p['axis_lift'] * ((1 - u) ** 4))

    raw = [(u ** p['r_pow_head']) * ((1 - u) ** p['r_pow_tail']) for u in
           [k / (p['n'] - 1) for k in range(p['n'])]]
    rmax = max(raw)

    def prof(u):
        k = round((u * (p['n'] - 1)))
        return p['r_max'] * raw[k] / rmax

    rings = []
    for k in range(p['n']):
        u = k / (p['n'] - 1)
        ax, ay, az = axis(u)
        r = prof(u)
        rx, rz = p['rx_ratio'] * r, r
        ring = []
        for s in range(p['sides']):
            a = 2 * math.pi * s / p['sides']
            ring.append((ax, ay + rx * math.cos(a), az + rz * math.sin(a)))
        rings.append(ring)
    loft_closed(acc, rings, m)

    prism_xz(acc, p['fin_pts'], p['fin_y0'], p['fin_t'], m)              # 背鳍
    fx = p['fluke_x']
    prism_xy(acc, [(fx + dx, dy) for dx, dy in p['fluke_pts']], p['fluke_z'], p['fluke_t'], m)
    prism_xy(acc, [(fx + dx, -dy) for dx, dy in p['fluke_pts']], p['fluke_z'], p['fluke_t'], m)


# ============================================================================
# ⑥ LeviathanTentacle 利维坦触手（高 18，螺旋 2.5 圈，吸盘棱面环）
# ============================================================================

def build_tentacle(acc, mi):
    p = TENTACLE
    m = mi['Kit_FarNear']

    def axis_pt(q):
        th = 2 * math.pi * p['turns'] * q + p['th0']
        R = p['r_base'] - p['r_shrink'] * q
        z = p['z_base'] + p['z_span'] * (q ** p['z_pow'])
        return (R * math.cos(th), R * math.sin(th) * p['xy_squash'], z), th

    def tube_r(q):
        return p['tube_r0'] * ((1 - q) ** p['tube_pow']) + p['tube_tip']

    eps = 0.5 / p['nseg']
    rings = []
    centers = []
    for k in range(p['nseg'] + 1):
        q = k / p['nseg']
        pt, th = axis_pt(q)
        centers.append((pt, th, q))
        pa, _ = axis_pt(min(1.0, q + eps))
        pb, _ = axis_pt(max(0.0, q - eps))
        tg = (Vector(pa) - Vector(pb))
        if tg.length < 1e-6:
            tg = Vector((0, 0, 1))
        tg = tg.normalized()
        u = tg.cross(Vector((0, 0, 1)))
        u = u.normalized() if u.length > 1e-6 else Vector((1, 0, 0))
        v = tg.cross(u).normalized()
        r = tube_r(q)
        rings.append([tuple(Vector(pt) + (u * math.cos(2 * math.pi * s / p['sides']) +
                                          v * math.sin(2 * math.pi * s / p['sides'])) * r)
                      for s in range(p['sides'])])
    loft_closed(acc, [list(map(tuple, ring)) for ring in rings], m)

    # 吸盘：沿内侧朝轴心、略上仰的棱面喇叭短管
    for i in range(p['suck_n']):
        q = min(0.94, p['suck_q0'] + i * p['suck_dq'])
        pt, th, _ = centers[int(q * p['nseg'])]
        r = tube_r(q)
        inward = Vector((-math.cos(th), -math.sin(th) * p['xy_squash'], 0)).normalized()
        d = (inward + Vector((0, 0, p['suck_up']))).normalized()
        p0 = Vector(pt) + d * (r * 0.35)
        p1 = Vector(pt) + d * (r + p['suck_len'])
        ax_tube(acc, tuple(p0), tuple(p1), p['suck_r0'], p['suck_r1'], p['suck_sides'], m)

    # 尖端外弯小钩
    p_end = Vector(centers[-1][0])
    outward = Vector((math.cos(centers[-1][1]), math.sin(centers[-1][1]) * p['xy_squash'], 0)).normalized()
    hook_mid = p_end + (outward + Vector((0, 0, 0.35))).normalized() * (p['hook_len'] * 0.6)
    hook_tip = p_end + outward * p['hook_len'] + Vector((0, 0, -p['hook_len'] * 0.35))
    tube_chain(acc,
               [tuple(p_end), tuple(hook_mid), tuple(hook_tip)],
               [p['hook_r'], p['hook_r'] * 0.85, p['hook_r'] * 0.25], 6, m)


# ============================================================================
# ⑦ GiantRibs 巨肋拱（7 根肋弧排 40 宽拱廊 + 断脊柱）
# ============================================================================

def build_giant_ribs(acc, mi):
    p = RIBS
    m = mi['Kit_FarNear']
    tube_r = [p['tube_r_add'] + R * p['tube_r_mul'] for R in p['radii']]
    for i, (x, R) in enumerate(zip(p['xs'], p['radii'])):
        y0 = p['y_off_mul'] * math.sin(i * p['y_off_freq'])
        arc = []
        rr = []
        half = p['narc'] * 0.5
        for s in range(p['narc'] + 1):
            th = p['th0'] + (p['th1'] - p['th0']) * s / p['narc']
            arc.append((x + R * math.cos(th), y0, p['cz'] + R * math.sin(th)))
            rr.append(tube_r[i] * (1.0 - 0.25 * abs(s - half) / half))   # 中段粗、两端细
        tube_chain(acc, arc, rr, p['sides'], m)
        # 弧顶关节鼓包
        uv_sphere(acc, (x, y0, p['cz'] + R), tube_r[i] * 1.18, tube_r[i] * 1.18, tube_r[i] * 1.05,
                  p['knob_segs'], p['knob_rings'], m)

    sp = p['spine']
    ax_tube(acc, (0, sp['y'], sp['z0']), (0, sp['y'], sp['z1']), sp['r0'], sp['r1'], sp['sides'], m)
    ax_tube(acc, (0, sp['y'], sp['z1']), (sp['lean'] * 2, sp['y'], sp['z2']), sp['r1'] * 0.9, sp['r2'], sp['sides'], m)


# ============================================================================
# ⑧ FarFleet 远帆船队（3 艘小帆船剪影错落一组）
# ============================================================================

def _build_ship(acc, mi_hull, mi_sail, fp):
    """局部系单艘小帆船：艏朝 +X，原点 = 水线船长中点。"""
    # 船壳放样（XZ 站 × 七点剖面环）
    rings = []
    for k in range(fp['hull_n']):
        u = k / (fp['hull_n'] - 1)
        x = -fp['hull_len'] * 0.5 + fp['hull_len'] * u
        w = fp['half_beam'] * (math.sin(math.pi * (0.06 + 0.88 * u)) ** 0.6)
        sheer = fp['sheer'] * math.sin(math.pi * u)
        ring = [(x, yy * w, zz + sheer) for yy, zz in fp['sec']]
        rings.append(ring)
    loft_closed(acc, rings, mi_hull)
    # 双桅
    for ms in (fp['mast_main'], fp['mast_aft']):
        ax_tube(acc, (ms['x'], 0, ms['z0']), (ms['x'], 0, ms['z1']), ms['r0'], ms['r1'], 6, mi_hull)
    # 艏斜桁
    sp = fp['sprit']
    ax_tube(acc, (sp['x0'], 0, sp['z0']), (sp['x0'] + sp['dx'], 0, sp['z0'] + sp['dz']),
            sp['r0'], sp['r1'], 6, mi_hull)

    # 横帆（微鼓风弧面，帆面在 YZ 平面、鼓向 -X）
    def square_sail(cx, top_z, w, h, bulge, rows, cols):
        pts = []
        for j in range(rows):
            v = j / (rows - 1)
            z = top_z - h * v
            row = []
            for i in range(cols):
                u2 = i / (cols - 1)
                y = -w * 0.5 + w * u2
                dx = -bulge * math.sin(math.pi * u2) * (0.25 + 0.75 * v)
                row.append((cx + dx, y, z))
            pts.append(row)
        ax_grid(acc, pts, rows, cols, mi_sail)

    square_sail(fp['main_sail']['x'], fp['main_sail']['top'], fp['main_sail']['w'],
                fp['main_sail']['h'], fp['main_sail']['bulge'], fp['main_sail']['rows'], fp['main_sail']['cols'])
    square_sail(fp['aft_sail']['x'], fp['aft_sail']['top'], fp['aft_sail']['w'],
                fp['aft_sail']['h'], fp['aft_sail']['bulge'], fp['aft_sail']['rows'], fp['aft_sail']['cols'])

    # 前三角帆（tack→head→clew 网格，鼓向 -X）
    j = fp['jib']
    tack, head, clew = (Vector(v) for v in (j['tack'], j['head'], j['clew']))
    jpts = []
    for r in range(j['rows']):
        v = r / (j['rows'] - 1) * 0.94
        e1 = tack + (head - tack) * v
        e2 = clew + (head - clew) * v
        row = []
        for c in range(j['cols']):
            u2 = c / (j['cols'] - 1)
            pv = e1.lerp(e2, u2)
            pv.x -= j['bulge'] * math.sin(math.pi * u2) * (0.3 + 0.7 * v)
            row.append(tuple(pv))
        jpts.append(row)
    ax_grid(acc, jpts, j['rows'], j['cols'], mi_sail)


def build_far_fleet(acc, mi):
    p = FLEET
    for x, y, rot_deg, sc in p['ships']:
        tmp = MeshAcc()
        _build_ship(tmp, 0, 1, p)
        ca, sa = math.cos(math.radians(rot_deg)), math.sin(math.radians(rot_deg))

        def xf(v, x=x, y=y, ca=ca, sa=sa, sc=sc):
            vx, vy, vz = v
            return (x + sc * (vx * ca - vy * sa), y + sc * (vx * sa + vy * ca), vz * sc)

        merge(acc, tmp, xf=xf, mat_map={0: mi['Kit_FarNear'], 1: mi['Kit_FarSail']})


# ============================================================================
# 资产登记（FBX 名, build, 槽名序）
# ============================================================================

ASSETS = [
    ('DistantIsleS', build_distant_isle_s, ('Kit_FarNear',)),
    ('DistantIsleM', build_distant_isle_m, ('Kit_FarNear', 'Kit_FarFar')),
    ('DistantIsleL', build_distant_isle_l, ('Kit_FarNear', 'Kit_FarFar')),
    ('CloudBankL', build_cloud_bank, ('Kit_Cloud',)),
    ('WhaleSurfacing', build_whale, ('Kit_FarNear',)),
    ('LeviathanTentacle', build_tentacle, ('Kit_FarNear',)),
    ('GiantRibs', build_giant_ribs, ('Kit_FarNear',)),
    ('FarFleet', build_far_fleet, ('Kit_FarNear', 'Kit_FarSail')),
]

# ============================================================================
# 合网格 / 场景 / 渲染
# ============================================================================

def join_to_object(acc, obj_name, mats):
    """MeshAcc → 单网格对象（多材质槽）。只挂实际用到的槽。"""
    used = sorted(set(acc.face_mat))
    index_map = {old: new for new, old in enumerate(used)}
    me = bpy.data.meshes.new(obj_name)
    bm = bmesh.new()
    bm_verts = [bm.verts.new(v) for v in acc.verts]
    bm.verts.ensure_lookup_table()
    for f, mi, sm in zip(acc.faces, acc.face_mat, acc.face_smooth):
        try:
            bf = bm.faces.new([bm_verts[i] for i in f])
            bf.material_index = index_map[mi]
            bf.smooth = sm                            # shade_flat（远景剪影全部平直着色）
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    for old in used:
        me.materials.append(mats[old][1])
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def reset_scene():
    """清空全部对象并 purge 孤儿数据（幂等 + 每件独立干净场景）。"""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)


def enable_gpu():
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for ctype in ('OPTIX', 'CUDA'):
            try:
                prefs.compute_device_type = ctype
                prefs.get_devices()
                found = False
                for dev in prefs.devices:
                    dev.use = dev.type != 'CPU'
                    found = found or dev.use
                if found:
                    return ctype
            except Exception:
                continue
    except Exception:
        pass
    return None


def setup_render(scene):
    scene.render.engine = 'CYCLES'
    gpu = enable_gpu()
    scene.cycles.device = 'GPU' if gpu else 'CPU'
    scene.cycles.samples = SAMPLES
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = RES
    scene.render.resolution_percentage = 100
    scene.render.use_file_extension = False
    ims = scene.render.image_settings
    ims.file_format = 'JPEG'
    ims.quality = JPG_QUALITY
    ims.color_mode = 'RGB'
    # 铁律（style_tokens.PREVIEW_VIEW_TRANSFORM）：Standard，AgX 会洗掉调色板色值
    scene.view_settings.view_transform = ST.PREVIEW_VIEW_TRANSFORM
    scene.view_settings.look = 'None'
    log('render engine=Cycles device=%s samples=%d' % (gpu or 'CPU', SAMPLES))


def setup_preview_stage(scene, aim):
    """灰底 + 灰地板 + 三灯（暖 key / 冷 fill / 暖 rim），灯瞄准 aim。

    与 style_tokens.setup_preview_world 函数体同源（同灯位/能量/色值/地板尺寸）；
    之所以本地复刻：该函数在 Blender 5.2 里 `bpy.data.curves.new(type="TEXT")`
    枚举不存在（已改名 FONT）直接崩溃——本 kit 不因此改 tokens 文件（域外文件），
    待协调者修 tokens 后可切回。背景/地板色值仍取 ST 常量，不散写。
    """
    world = scene.world
    if world is None:
        world = bpy.data.worlds.new('HK_PreviewWorld')
        scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get('Background')
    bg.inputs[0].default_value = srgb_to_linear(ST.PREVIEW_BG_HEX)
    bg.inputs[1].default_value = 1.0

    floor_mat = bpy.data.materials.new('HK_PreviewFloor')
    floor_mat.use_nodes = True
    fb = floor_mat.node_tree.nodes['Principled BSDF']
    fb.inputs['Base Color'].default_value = srgb_to_linear(ST.PREVIEW_GROUND_HEX)
    fb.inputs['Roughness'].default_value = 0.9
    floor = bpy.data.objects.new('HK_PreviewFloor', bpy.data.meshes.new('HK_PreviewFloor'))
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400.0,
                          matrix=mathutils.Matrix.Translation((0.0, 0.0, -0.01)))
    bm.to_mesh(floor.data)
    bm.free()
    floor.location = (0, 0, 0)
    floor.data.materials.append(floor_mat)
    scene.collection.objects.link(floor)

    lamps = [
        ('ST_Key', ST.hex_to_rgb('#FFE8C8'), 90000 * LIGHT_BOOST, (160, -140, 220)),   # 暖主光（右上前）
        ('ST_Fill', ST.hex_to_rgb('#C8DDF0'), 36000 * LIGHT_BOOST, (-180, -60, 120)),  # 冷补光（左）
        ('ST_Rim', ST.hex_to_rgb('#FFF0D8'), 30000 * LIGHT_BOOST, (-40, 180, 160)),    # 暖轮廓（后上）
    ]
    for lname, color, energy, loc in lamps:
        data = bpy.data.lights.new(lname, 'AREA')
        data.color = color
        data.energy = energy
        data.size = 60.0
        o = bpy.data.objects.new(lname, data)
        scene.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (Vector(aim) - o.location).to_track_quat('-Z', 'Y').to_euler()


def render_views(scene, obj, name, out_dir, views):
    for vname, loc, target in views:
        cam_data = bpy.data.cameras.new('hk_cam')
        cam_data.lens = CAM_LENS
        cam_data.clip_end = 2500.0                     # 远观 400 距离不裁剪
        cam = bpy.data.objects.new('hk_cam', cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        path = os.path.join(out_dir, '%s-%s.jpg' % (name, vname))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        log('rendered %s (%d bytes)' % (path, os.path.getsize(path)))
        bpy.data.objects.remove(cam, do_unlink=True)


def horizon_views(dist, h):
    """观景双视角：front34 = 正面 3/4 主剪影；side = 侧后看纵深层次。相机拉远模拟真实远眺。"""
    tx, ty, tz = 0.0, 0.0, h * 0.38
    return [
        ('front34', (dist * 0.60, -dist * 0.80, h * 0.55 + 2.0), (tx, ty, tz)),
        ('side', (-dist, -dist * 0.18, h * 0.55 + 2.0), (tx, ty, tz)),
    ]


# ============================================================================
# 主流程
# ============================================================================

def main():
    parse_args()
    for d in (FBX_DIR, PREVIEW_DIR, WORK_DIR):
        os.makedirs(d, exist_ok=True)

    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0             # 1 Blender 单位 = 1 米
    setup_render(scene)

    done = 0
    for name, build_fn, slot_names in ASSETS:
        if ONLY not in ('all', name):
            continue
        log('== %s ==' % name)
        reset_scene()
        mats = make_materials(slot_names)
        mi = {n: i for i, (n, _m) in enumerate(mats)}

        acc = MeshAcc()
        build_fn(acc, mi)
        obj = join_to_object(acc, name, mats)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

        ST.budget_guard(name, [obj], 'horizon')        # STAT 行 + horizon ≤3000 tri 闸
        used_slots = [s.name for s in obj.data.materials if s is not None]
        if len(used_slots) > MAX_SLOTS:
            raise ValueError('[%s] 材质槽 %d 个超远景硬约束 %d：%s'
                             % (name, len(used_slots), MAX_SLOTS, used_slots))

        fbx = os.path.join(FBX_DIR, name + '.fbx')
        ST.export_fbx([obj], fbx)
        log('exported %s (%d bytes)' % (fbx, os.path.getsize(fbx)))

        if RENDER:
            cam = CAM[name]
            setup_preview_stage(scene, (0, 0, cam['h'] * 0.4))
            render_views(scene, obj, name, PREVIEW_DIR, horizon_views(cam['dist'], cam['h']))
        done += 1

    if RENDER and done:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, 'horizon_kit_debug.blend'))
    log('done: %d asset(s) in %.1fs total' % (done, time.time() - T0))


if __name__ == '__main__':
    main()
