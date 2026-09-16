# -*- coding: utf-8 -*-
"""build_scene_kit.py —— 《海盗军团夺宝 3D》场景资产样板无头建模（Blender 5.2，纯程序化，零外部素材）。

两件资产（美术验收清单 r8+ 登记的缺口：大帆船含白帆、木栈桥入水立柱）：
    ① Flagship.fbx  大帆船：放样船体 + 平坦开阔甲板 + 艏楼/艉楼 + 双桅双横帆 + 三角帆
                    + 缆绳 + 黄铜炮/艉灯/舷窗 + 锚 + 舵；
    ② Dock.fbx      木栈桥：错缝甲板（几何缝=凹槽感）+ 6 根入水立柱（插到水面以下）
                    + 系船柱绳圈 + 斜撑 + 尾端下水梯。

【坐标口径（硬指标，Unity 侧 1 单位 = 1 米）】
    Blender 场景：+Z 为上、船长沿 Y（船艏朝 -Y）、船宽沿 X。
    FBX 导出（axis_forward='-Z', axis_up='Y'）后：**FBX 空间 +Y 为上、-Z 为前**——
    船艏在 FBX 的 -Z 方向；Unity 导入（bakeAxisConversion=true）后船艏朝 Unity +Z。
    · 大帆船原点 = 船长中点水线处（z=0 即水线；Unity 里根节点放 y=-0.4 的水面高度）；
      总长 15.6（ hull 13.0 + 艏斜桁悬出 2.6 ）、船宽 4.5、甲板走道高 1.4、主桅顶 z=6.5。
    · 木栈桥原点 = 桥面顶面中心（z=0 即桥面顶；Unity 里根节点放 y = -0.4 + 0.65 = 0.25，
      桥面顶即落在水面上方 0.65）；长 6 × 宽 2，立柱插到 z=-1.2（水面在 -0.65）。

【材质纪律】零贴图，Principled BSDF 纯色；槽名 Kit_ 前缀（Unity 侧换装键，C# 常量表同源色值）：
    Kit_WoodMid #A67B42 / Kit_WoodDark #6B4C28 / Kit_Sail #F5E8C8 /
    Kit_Brass #C9A227 / Kit_Rope #8A6F4D（项目调色板，docs/美术风格指南.md §2.1）。

复现（仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/build_scene_kit.py -- [--only flagship|dock|all] [--samples N] [--no-render]

产物：
    pirate-crew/Assets/Art/Models/SceneKit/Flagship.fbx / Dock.fbx
    export/scene-kit-pilot/flagship-{front34,side,back}.jpg / dock-{front34,side,back}.jpg （1024²）
    external/scene-kit-work/scene_kit_debug.blend （调参用 GUI 缓存，gitignored）

幂等：重跑直接覆盖全部产物。控制台会打印每件资产的三角面数 / 材质槽 / 包围盒（README 引用）。
"""

import math
import os
import sys
import time

import bpy
import bmesh
import mathutils

# ============================================================================
# 参数区 —— 调参旋钮集中在此（README 按行号引用）
# ============================================================================

ONLY = 'all'            # 'all' | 'flagship' | 'dock'
SAMPLES = 96            # Cycles 采样数（预览图；嫌慢改小）
RENDER = True           # False = 只建模导出 FBX，不出预览图
RES = 1024              # 预览图边长（px）
JPG_QUALITY = 90        # 预览图 JPEG 质量（export/ 产物入库需压在几百 KB 内）

# ---- 配色（sRGB hex；与 Unity 侧 SceneKitPilotSetup.cs 常量表同源）----
C_WOOD_MID = 0xA67B42   # 木棕中档：甲板/桅杆/艏艉楼/桥面板
C_WOOD_DARK = 0x6B4C28  # 木棕暗档：水线以下船壳/龙骨/舵/桥体构件/立柱
C_SAIL = 0xF5E8C8       # 帆布暖白
C_BRASS = 0xC9A227      # 黄铜：炮/艉灯/舷窗框/锚环
C_ROPE = 0x8A6F4D       # 缆绳

# ---- 大帆船主尺度（世界单位=米；z=0 为水线）----
SHIP_LEN = 13.0         # 船体总长（艏柱到艉板；不含艏斜桁悬出）
SHIP_HALF_BEAM = 2.25   # 最大半宽（船宽 4.5）
SHIP_DECK_Z = 1.4       # 主甲板走道高（水线上方；任务口径 1.2~1.6）
SHIP_KEEL_Z = -0.9      # 龙骨底（水线下方）
SHIP_BULWARK = 0.7      # 舷墙高（甲板面以上）
SHIP_SHEER_AMP = 0.6    # 舷弧幅：栏杆线向艏上扬幅（艉取 0.55 倍）
SHIP_STATIONS = 23      # 放样横剖站数（越大船壳越顺滑、面数越多）
SHIP_MAST_TOP_MAIN = 6.5    # 主桅顶高度（水线上方；任务口径 5~7）
SHIP_MAST_TOP_FORE = 5.9    # 前桅顶高度
SAIL_MAIN_W = 5.4       # 主横帆宽
SAIL_MAIN_H = 2.45      # 主横帆高
SAIL_FORE_W = 4.4       # 前横帆宽
SAIL_FORE_H = 2.05      # 前横帆高
SAIL_BULGE = 0.75       # 帆鼓风前凸深度（微微鼓风）
ROW_CAP_R = 0.085       # 栏杆帽圆杆半径
ROPE_R = 0.028          # 缆绳半径

# ---- 木栈桥主尺度（z=0 为桥面顶面）----
DOCK_LEN = 6.0          # 长（3 瓦片）
DOCK_WIDTH = 2.0        # 宽（1 瓦片）
DOCK_PLANK_T = 0.09     # 面板厚
DOCK_PILING_R = 0.115   # 立柱半径
DOCK_PILING_BOT = -1.2  # 立柱底（水面在 -0.65，插入水下 0.55）
DOCK_MOOR_TOP = 0.34    # 系船柱顶（高出桥面）
DOCK_GAP = 0.055        # 板缝宽（几何凹槽，不贴图）

# ============================================================================
# 路径与控制台
# ============================================================================

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
FBX_DIR = os.path.join(ROOT, 'pirate-crew', 'Assets', 'Art', 'Models', 'SceneKit')
PREVIEW_DIR = os.path.join(ROOT, 'export', 'scene-kit-pilot')
WORK_DIR = os.path.join(ROOT, 'external', 'scene-kit-work')

T0 = time.time()


def log(msg):
    print('[scenekit] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


def parse_args():
    """blender -b -P 脚本 -- 之后才是自己的参数。"""
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
# 几何累积器：所有部件按 (顶点, 面, 材质序号, 是否平滑) 追加，最后合成单网格
# ============================================================================

class MeshAcc:
    """单件资产的几何累积器。mat 序号对应 make_materials 返回列表的下标。"""

    def __init__(self):
        self.verts = []
        self.faces = []        # 每项 = 顶点下标元组（3~n 边形）
        self.face_mat = []
        self.face_smooth = []

    def add(self, verts, faces, mat, smooth=False):
        base = len(self.verts)
        self.verts.extend(verts)
        for f in faces:
            self.faces.append(tuple(base + i for i in f))
            self.face_mat.append(mat)
            self.face_smooth.append(smooth)

    def counts(self):
        tris = sum(len(f) - 2 for f in self.faces)
        return len(self.verts), len(self.faces), tris


# ---------- 图元 helper（全部落进 MeshAcc；坐标已是资产局部系） ----------

def ax_box(acc, center, size, mat, smooth=False, rot_z=0.0):
    """轴对齐盒（可绕 z 旋转）。center=中心，size=(sx,sy,sz)。"""
    cx, cy, cz = center
    sx, sy, sz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    ca, sa = math.cos(rot_z), math.sin(rot_z)
    pts = []
    for dx in (-sx, sx):
        for dy in (-sy, sy):
            for dz in (-sz, sz):
                pts.append((cx + dx * ca - dy * sa, cy + dx * sa + dy * ca, cz + dz))
    # 顶点序：0(-x,-y,-z) 1(-x,+y,-z) 2(+x,-y,-z) 3(+x,+y,-z) 4..7 同序 +z
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    acc.add(pts, faces, mat, smooth)


def ax_tube(acc, p0, p1, r1, r2, sides, mat, smooth=True, caps=True):
    """圆台/圆柱/圆杆：p0→p1 轴线，r1=起点半径，r2=终点半径。"""
    d = mathutils.Vector(p1) - mathutils.Vector(p0)
    length = d.length
    if length < 1e-6:
        return
    d = d.normalized()
    up = mathutils.Vector((0, 0, 1))
    if abs(d.dot(up)) > 0.95:
        up = mathutils.Vector((0, 1, 0))
    u = d.cross(up).normalized()
    v = d.cross(u).normalized()
    c0, c1 = mathutils.Vector(p0), mathutils.Vector(p1)
    ring0, ring1 = [], []
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        w = u * math.cos(a) + v * math.sin(a)
        ring0.append(c0 + w * r1)
        ring1.append(c1 + w * r2)
    pts = [tuple(p) for p in ring0 + ring1]
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))          # 侧面
    if caps:
        faces.append(tuple(range(sides - 1, -1, -1)))          # p0 端盖（朝外）
        faces.append(tuple(range(sides, 2 * sides)))           # p1 端盖
    acc.add(pts, faces, mat, smooth)


def ax_grid(acc, points, rows, cols, mat, smooth=True, flip=False):
    """rows×cols 点阵连四边形条带（sails / 平台面）。points 是行优先二维列表。"""
    base = len(acc.verts)
    for row in points:
        acc.verts.extend(tuple(p) for p in row)
    faces = []
    for j in range(rows - 1):
        for i in range(cols - 1):
            a = base + j * cols + i
            b = a + 1
            c = a + cols + 1
            d = a + cols
            faces.append((a, d, c, b) if flip else (a, b, c, d))
    for _ in faces:
        acc.face_mat.append(mat)
        acc.face_smooth.append(smooth)
    acc.faces.extend(faces)


# ============================================================================
# 材质（槽名 = Unity 侧换装键，顺序即 FBX 槽序）
# ============================================================================

def srgb(hexv):
    """0xRRGGBB → 线性 RGBA（Blender 节点吃线性值）。"""
    def f(v):
        v = v / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (hexv >> 16) & 0xFF, (hexv >> 8) & 0xFF, hexv & 0xFF
    return (f(r), f(g), f(b), 1.0)


def make_materials():
    """按固定顺序创建 5 个 Kit_ 材质；返回 [(槽名, 材质), ...]。roughness≈1-Unity smoothness。"""
    spec = [
        ('Kit_WoodMid', C_WOOD_MID, 0.72, 0.0),
        ('Kit_WoodDark', C_WOOD_DARK, 0.78, 0.0),
        ('Kit_Sail', C_SAIL, 0.88, 0.0),
        ('Kit_Brass', C_BRASS, 0.50, 1.0),
        ('Kit_Rope', C_ROPE, 0.80, 0.0),
    ]
    out = []
    for name, hexv, rough, metal in spec:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = srgb(hexv)
        bsdf.inputs['Roughness'].default_value = rough
        bsdf.inputs['Metallic'].default_value = metal
        out.append((name, mat))
    return out


def name_to_index(mats):
    return {name: i for i, (name, _mat) in enumerate(mats)}


M_WOODMID, M_WOODDARK, M_SAIL, M_BRASS, M_ROPE = 0, 1, 2, 3, 4

# ============================================================================
# ① 大帆船
# ============================================================================

def hull_half_width(t):
    """半宽剖面（t: 0=艉 1=艏）：艉板 0.62 → 舯 1.0 → 艏柱 0.05（幂曲线收尖）。
    同口径移植 Assets/Scripts/PirateCrew/SceneArt/ShipHullGeometry.cs 的 HalfWidthAt。"""
    if t < 0.5:
        u = t / 0.5
        s = u * u * (3.0 - 2.0 * u)                      # smoothstep
        return 0.62 + (1.0 - 0.62) * s
    v = (t - 0.5) / 0.5
    return 1.0 + (0.05 - 1.0) * (v ** 2.8)


def hull_rail_z(t):
    """栏杆顶高度：甲板 + 舷墙 + 舷弧（艏上扬多、艉上扬少；t=1 是艏）。"""
    amp = SHIP_SHEER_AMP * (t ** 2.6 + 0.55 * (1.0 - t) ** 2.2)
    return SHIP_DECK_Z + SHIP_BULWARK + amp


# 横剖面控制点（横向比例, 垂向比例 0=龙骨 1=栏杆顶）——同 ShipHullGeometry 的 Section 思路
HULL_SECTION = [
    (0.04, 0.00),   # 龙骨底
    (0.42, 0.16),   # 龙骨侧垫板
    (0.72, 0.38),   # 舭部转弯
    (0.97, 0.62),   # 最宽点
    (0.94, 0.88),   # 上舷内倾
    (0.82, 1.00),   # 栏杆顶
]


def hull_station_points(t):
    """一个横剖站的剖面折线（不含甲板缘插入点）：返回 [(x, z), ...]，x 为半宽绝对值。"""
    hw = hull_half_width(t) * SHIP_HALF_BEAM
    rz = hull_rail_z(t)
    pts = []
    for lat, up in HULL_SECTION:
        pts.append((lat * hw, SHIP_KEEL_Z + up * (rz - SHIP_KEEL_Z)))
    return pts


def hull_lat_at_z(t, z):
    """在横剖站 t 上、高度 z 处的半宽（用于甲板面/艏艉楼平台收边）。"""
    pts = hull_station_points(t)
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if z0 <= z <= z1:
            f = 0.0 if z1 - z0 < 1e-6 else (z - z0) / (z1 - z0)
            return x0 + (x1 - x0) * f
    return pts[-1][0] if z > pts[-1][1] else pts[0][0]


def station_y(t):
    """站号 → 纵向 y（t=0 艉板 y=+6.5，t=1 艏柱 y=-6.5；艏朝 -Y）。"""
    return SHIP_LEN * 0.5 - SHIP_LEN * t


def build_flagship(acc):
    """大帆船：船壳放样 + 甲板 + 艏艉楼 + 横桅帆 + 缆绳 + 黄铜件。"""
    st = SHIP_STATIONS
    ts = [i / (st - 1) for i in range(st)]

    # ---- 船壳条带：相邻站 × 相邻剖面点 × 左右两舷；水线附近暗木、其余中木 ----
    for s in range(st - 1):
        t0, t1 = ts[s], ts[s + 1]
        y0, y1 = station_y(t0), station_y(t1)
        p0, p1 = hull_station_points(t0), hull_station_points(t1)
        for side in (1, -1):
            for c in range(len(HULL_SECTION) - 1):
                quad = [(side * p0[c][0], y0, p0[c][1]),
                        (side * p0[c + 1][0], y0, p0[c + 1][1]),
                        (side * p1[c + 1][0], y1, p1[c + 1][1]),
                        (side * p1[c][0], y1, p1[c][1])]
                mid_z = (p0[c][1] + p0[c + 1][1] + p1[c][1] + p1[c + 1][1]) * 0.25
                mat = M_WOODDARK if mid_z < 0.35 else M_WOODMID
                acc.add(quad, [(0, 1, 2, 3)], mat, smooth=True)

    # ---- 甲板缘插入点（甲板面 z 处船壳半宽）与平坦主甲板（走道保持开阔）----
    deck_edge = []   # 每站 (左x, 右x)
    for t in ts:
        hw = hull_half_width(t) * SHIP_HALF_BEAM
        rz = hull_rail_z(t)
        up_d = (SHIP_DECK_Z - SHIP_KEEL_Z) / (rz - SHIP_KEEL_Z)
        lat0, up0 = HULL_SECTION[3]      # 最宽点
        lat1, up1 = HULL_SECTION[4]      # 上舷内倾
        f = (up_d - up0) / (up1 - up0)
        lat_d = lat0 + (lat1 - lat0) * max(0.0, min(1.0, f))
        deck_edge.append(lat_d * hw)

    def deck_strip(t_from, t_to, z, mat, width_fn=None):
        """平坦甲板/平台面：从 t_from 到 t_to 每站连左右收边点的水平条带。
        width_fn(t) 给出该站在高度 z 处的半宽（缺省=主甲板缘 deck_edge）。"""
        i0 = int(round(t_from * (st - 1)))
        i1 = int(round(t_to * (st - 1)))
        for s in range(i0, i1):
            y0, y1 = station_y(ts[s]), station_y(ts[s + 1])
            w0 = (width_fn(ts[s]) if width_fn else deck_edge[s])
            w1 = (width_fn(ts[s + 1]) if width_fn else deck_edge[s + 1])
            acc.add([(w0, y0, z), (-w0, y0, z), (-w1, y1, z), (w1, y1, z)],
                    [(0, 1, 2, 3)], mat, smooth=False)

    deck_strip(0.20, 0.80, SHIP_DECK_Z, M_WOODMID)          # 主甲板（平坦开阔，无凸起舱盖）

    # ---- 艏楼 / 艉楼平台 + 隔壁墙（走在两端的升高甲板，高出舷墙形成阶梯剪影，不挡中段走道）----
    # 平台半宽取"平台高度处的船壳半宽"（高出舷墙时收窄到栏杆宽），贴着船舷不留缝
    FORE_CASTLE_Z = 2.75
    AFT_CASTLE_Z = 2.95
    deck_strip(0.80, 0.965, FORE_CASTLE_Z, M_WOODMID,
               width_fn=lambda t: hull_lat_at_z(t, FORE_CASTLE_Z))      # 艏楼
    deck_strip(0.045, 0.20, AFT_CASTLE_Z, M_WOODMID,
               width_fn=lambda t: hull_lat_at_z(t, AFT_CASTLE_Z))       # 艉楼
    for (t_wall, z_top) in ((0.80, FORE_CASTLE_Z), (0.20, AFT_CASTLE_Z)):
        yw = station_y(t_wall)
        w = hull_lat_at_z(t_wall, z_top)
        acc.add([(-w, yw, SHIP_DECK_Z), (w, yw, SHIP_DECK_Z),
                 (w, yw, z_top), (-w, yw, z_top)], [(0, 1, 2, 3)], M_WOODMID)
        ax_box(acc, (0, yw, SHIP_DECK_Z + 0.55), (0.9, 0.10, 1.10), M_WOODDARK)   # 舱门暗框
    # 艉楼尾缘栏杆（细柱一排，艉部剪影的层次）
    aft_y = station_y(0.06)
    aft_w = hull_lat_at_z(0.06, AFT_CASTLE_Z) - 0.08
    for bx in (-aft_w, -aft_w * 0.5, 0.0, aft_w * 0.5, aft_w):
        ax_tube(acc, (bx, aft_y, AFT_CASTLE_Z), (bx, aft_y, AFT_CASTLE_Z + 0.34), 0.03, 0.03, 6, M_WOODMID)
    ax_box(acc, (0, aft_y, AFT_CASTLE_Z + 0.34), (aft_w * 2 + 0.1, 0.06, 0.07), M_WOODMID)

    # ---- 舷墙帽（栏杆顶圆杆，左右各一条）----
    for side in (1, -1):
        for s in range(st - 1):
            p0 = hull_station_points(ts[s])[-1]
            p1 = hull_station_points(ts[s + 1])[-1]
            ax_tube(acc, (side * p0[0], station_y(ts[s]), p0[1]),
                        (side * p1[0], station_y(ts[s + 1]), p1[1]),
                    ROW_CAP_R, ROW_CAP_R, 10, M_WOODMID)

    # ---- 舯部腰带条纹（galleon 标志性舷侧饰带；几何凸条，不贴图）----
    for z_band in (0.55, 0.85):
        for side in (1, -1):
            for s in range(st - 1):
                x0 = hull_lat_at_z(ts[s], z_band) + 0.035
                x1 = hull_lat_at_z(ts[s + 1], z_band) + 0.035
                acc.add([(side * x0, station_y(ts[s]), z_band - 0.055),
                         (side * x0, station_y(ts[s]), z_band + 0.055),
                         (side * x1, station_y(ts[s + 1]), z_band + 0.055),
                         (side * x1, station_y(ts[s + 1]), z_band - 0.055)],
                        [(0, 1, 2, 3)], M_WOODDARK, smooth=True)

    # ---- 艉板（transom，略后倾）+ 艉窗 + 艉廊栏杆 + 艉灯 ----
    stern_pts = hull_station_points(ts[0])
    rake = [p[1] / hull_rail_z(0.0) * 0.38 for p in stern_pts]     # 上段后倾更多
    ys = station_y(0.0)
    poly = [(p[0], ys + rake[i], p[1]) for i, p in enumerate(stern_pts)]
    poly += [(-p[0], ys + rake[i], p[1]) for i, p in list(reversed(list(enumerate(stern_pts))))[1:-1]]
    acc.add(poly, [tuple(range(len(poly)))], M_WOODMID)             # 封板（n 边形）
    for wx in (-0.85, 0.0, 0.85):
        ax_box(acc, (wx, ys + 0.16, 1.85), (0.5, 0.06, 0.55), M_WOODDARK)   # 窗洞暗底
        ax_box(acc, (wx, ys + 0.20, 1.85), (0.56, 0.05, 0.61), M_BRASS)     # 黄铜窗框
    ax_box(acc, (0, ys + 0.45, AFT_CASTLE_Z + 0.05), (2.6, 0.5, 0.12), M_WOODMID)   # 艉廊底板
    for bx in (-1.1, -0.55, 0.0, 0.55, 1.1):
        ax_tube(acc, (bx, ys + 0.62, AFT_CASTLE_Z + 0.11), (bx, ys + 0.62, AFT_CASTLE_Z + 0.45), 0.028, 0.028, 6, M_WOODMID)
    ax_tube(acc, (0, ys + 0.55, AFT_CASTLE_Z + 0.12), (0, ys + 0.55, AFT_CASTLE_Z + 0.85), 0.05, 0.05, 8, M_WOODDARK)  # 灯杆
    lamp = [(0.16 * math.cos(2 * math.pi * k / 8), ys + 0.55 + 0.16 * math.sin(2 * math.pi * k / 8),
             AFT_CASTLE_Z + 0.98 + (0.10 if k % 2 else 0.16)) for k in range(8)]
    acc.add([(0, ys + 0.55, AFT_CASTLE_Z + 0.98)] + lamp, [tuple(range(9))], M_BRASS)   # 艉灯（八棱小穹顶）

    # ---- 龙骨 + 舵 + 艏柱 ----
    ax_box(acc, (0, 0.3, SHIP_KEEL_Z + 0.10), (0.34, SHIP_LEN * 0.72, 0.42), M_WOODDARK)
    ax_box(acc, (0, ys + 0.42, -0.45), (0.12, 0.55, 1.15), M_WOODDARK)      # 舵板（不垂过龙骨太多）
    stem = [(0, station_y(0.985), SHIP_KEEL_Z + 0.05), (0, station_y(1.0) - 0.05, 0.8),
            (0, station_y(1.0) - 0.02, 1.9), (0, station_y(1.0), 2.6)]
    for a, b in zip(stem, stem[1:]):
        ax_tube(acc, a, b, 0.12, 0.10, 8, M_WOODDARK)

    # ---- 艏斜桁（从艏楼顶前伸上扬；含悬出总长 ≈16.0，卡在 12~16 口径内）----
    sprit_base = (0, station_y(0.94), FORE_CASTLE_Z + 0.02)
    sprit_tip = (sprit_base[0], sprit_base[1] - 3.2 * math.cos(math.radians(25)),
                 sprit_base[2] + 3.2 * math.sin(math.radians(25)))
    ax_tube(acc, sprit_base, sprit_tip, 0.13, 0.055, 10, M_WOODMID)

    # ---- 双桅（前桅在艏楼、主桅在主甲板）+ 桅顶 + 鸦巢 + 横桁 ----
    fore_base = (0, station_y(0.70), 2.15)
    main_base = (0, station_y(0.32), SHIP_DECK_Z)
    fore_top = (0, fore_base[1], SHIP_MAST_TOP_FORE)
    main_top = (0, main_base[1], SHIP_MAST_TOP_MAIN)
    ax_tube(acc, fore_base, fore_top, 0.17, 0.105, 12, M_WOODMID)
    ax_tube(acc, main_base, main_top, 0.21, 0.125, 12, M_WOODMID)
    for top, r in ((fore_top, 0.10), (main_top, 0.12)):
        ax_tube(acc, (top[0], top[1], top[2]), (top[0], top[1], top[2] + 0.22), r, 0.02, 8, M_WOODDARK)
    nest_y, nest_z = main_base[1], 5.15
    ax_tube(acc, (0, nest_y, nest_z), (0, nest_y, nest_z + 0.34), 0.50, 0.46, 12, M_WOODMID, caps=False)
    for yard_y, yard_z, yard_w in ((fore_base[1], 5.30, SAIL_FORE_W + 0.6),
                                   (main_base[1], 5.90, SAIL_MAIN_W + 0.6)):
        ax_tube(acc, (-yard_w * 0.5, yard_y, yard_z), (yard_w * 0.5, yard_y, yard_z), 0.085, 0.085, 8, M_WOODMID)

    # ---- 帆：双横帆（微微鼓风弧面）+ 艏三角帆 ----
    def square_sail(y, top_z, bot_z, width, bulge, rows, cols):
        pts = []
        for j in range(rows):
            v = j / (rows - 1)
            z = top_z - (top_z - bot_z) * v
            row = []
            for i in range(cols):
                u = i / (cols - 1)
                x = -width * 0.5 + width * u
                dy = -bulge * math.sin(math.pi * u) * (0.25 + 0.75 * v)   # 底边鼓得更多
                row.append((x, y + dy, z))
            pts.append(row)
        ax_grid(acc, pts, rows, cols, M_SAIL, smooth=True)

    square_sail(fore_base[1], 5.22, 5.22 - SAIL_FORE_H, SAIL_FORE_W, SAIL_BULGE * 0.8, 7, 10)
    square_sail(main_base[1], 5.82, 5.82 - SAIL_MAIN_H, SAIL_MAIN_W, SAIL_BULGE, 8, 12)

    tack = (0, sprit_base[1] - 1.55, 3.55)                  # 三角帆：斜桁前段 → 前桅中上 → 帆脚
    head = (0, fore_top[1], 4.85)
    clew = (0, fore_base[1] + 0.75, 3.15)
    jrows, jcols = 6, 5
    jpts = []
    for j in range(jrows):
        v = j / (jrows - 1) * 0.94
        e1 = mathutils.Vector(tack) + (mathutils.Vector(head) - mathutils.Vector(tack)) * v
        e2 = mathutils.Vector(clew) + (mathutils.Vector(head) - mathutils.Vector(clew)) * v
        row = []
        for i in range(jcols):
            u = i / (jcols - 1)
            p = e1.lerp(e2, u)
            p.y -= 0.18 * math.sin(math.pi * u) * (0.3 + 0.7 * v)   # 朝艏方向微鼓
            row.append(tuple(p))
        jpts.append(row)
    ax_grid(acc, jpts, jrows, jcols, M_SAIL, smooth=True)

    # ---- 缆绳（细圆柱，数量克制）：2 静索 + 4 桅侧支索 + 2 帆脚索 + 1 三角帆索 ----
    def rod(p0, p1, r=ROPE_R, mat=M_ROPE):
        ax_tube(acc, p0, p1, r, r, 6, mat)

    rod(fore_top, sprit_tip)                                             # 前支索（沿斜桁）
    rod(main_top, (0, station_y(0.06), AFT_CASTLE_Z))                    # 主支索（到艉楼）
    for top, z_from in ((fore_top, 4.3), (main_top, 4.6)):
        for side in (1, -1):
            rail_x = hull_lat_at_z(0.5, SHIP_DECK_Z + 0.3) * 0.98
            rod((top[0], top[1], z_from), (side * rail_x, top[1] + 0.55, SHIP_DECK_Z + 0.55))
    for side in (1, -1):
        rail_x = hull_lat_at_z(0.42, SHIP_DECK_Z + 0.25) * 0.98
        rod((side * SAIL_MAIN_W * 0.42, main_base[1] - SAIL_MAIN_H * 0.62, 5.82 - SAIL_MAIN_H),
            (side * rail_x, main_base[1] + 0.4, SHIP_DECK_Z + 0.5))
    rod(clew, (hull_lat_at_z(0.62, SHIP_DECK_Z + 0.2), fore_base[1] + 1.5, SHIP_DECK_Z + 0.35))

    # ---- 黄铜炮（每舷 2 门，从舷墙探出；炮管外露 0.65 保剪影可读）----
    for gy in (2.0, 0.4):
        for side in (1, -1):
            gx = hull_lat_at_z(0.42, 1.78)
            ax_tube(acc, (side * (gx - 0.9), gy, 1.78), (side * (gx + 0.65), gy, 1.78),
                    0.09, 0.075, 10, M_BRASS)
    cat_x = hull_lat_at_z(0.90, 2.05)
    ax_box(acc, (cat_x + 0.2, station_y(0.88), 2.05), (0.7, 0.14, 0.14), M_WOODDARK)   # 吊锚杆
    ax_tube(acc, (cat_x + 0.45, station_y(0.88), 2.0), (cat_x + 0.45, station_y(0.88), 1.1),
            0.03, 0.03, 6, M_ROPE)
    ax_tube(acc, (cat_x + 0.45, station_y(0.88), 1.12), (cat_x + 0.45, station_y(0.88), 0.35),
            0.055, 0.045, 8, M_WOODDARK)                                               # 锚杆
    for sgn in (1, -1):
        ax_tube(acc, (cat_x + 0.45 + sgn * 0.05, station_y(0.88), 0.38),
                (cat_x + 0.45 + sgn * 0.30, station_y(0.88) + sgn * 0.22, 0.30), 0.03, 0.012, 6, M_WOODDARK)
    acc_t = None


# ============================================================================
# ② 木栈桥
# ============================================================================

def build_dock(acc):
    """木栈桥：错缝面板（几何缝）+ 边梁托梁 + 6 入水立柱 + 系船柱绳圈 + 斜撑 + 下水梯。"""
    L, W, T, GAP = DOCK_LEN, DOCK_WIDTH, DOCK_PLANK_T, DOCK_GAP

    # ---- 甲板面板：宽向 5 块、错缝拼装（偶数行 3 段 / 奇数行 2 段），缝=几何凹槽 ----
    n_plank, pw = 5, (W - 4 * GAP) / 5
    seg_even = (L - 2 * GAP) / 3
    seg_odd = (L - GAP) / 2
    for i in range(n_plank):
        cx = -W * 0.5 + GAP + pw * 0.5 + i * (pw + GAP)
        if i % 2 == 0:
            joints = [-L * 0.5, -L * 0.5 + GAP + seg_even, -L * 0.5 + 2 * (GAP + seg_even), L * 0.5]
        else:
            joints = [-L * 0.5, -L * 0.5 + GAP + seg_odd, L * 0.5]
        for a, b in zip(joints, joints[1:]):
            ax_box(acc, (cx, (a + b) * 0.5, -T * 0.5), (pw, b - a, T), M_WOODMID)

    # ---- 边梁（暗木）+ 横托梁 ----
    for sx in (-1, 1):
        ax_box(acc, (sx * (W * 0.5 - 0.075), 0, -T - 0.085), (0.15, L, 0.17), M_WOODDARK)
    for by in (-L * 0.5 + 0.13, -L * 0.25, 0.0, L * 0.25, L * 0.5 - 0.13):
        ax_box(acc, (0, by, -T - 0.24), (W - 0.1, 0.14, 0.14), M_WOODDARK)

    # ---- 入水立柱：3 对（含 4 根高出桥面的系船柱），底 -1.2 插到水面（-0.65）以下 ----
    pilings = [(-0.75, -2.4, True), (0.75, -2.4, True),
               (-0.75, 0.0, False), (0.75, 0.0, False),
               (-0.75, 2.4, True), (0.75, 2.4, True)]
    for px, py, mooring in pilings:
        top = DOCK_MOOR_TOP if mooring else -0.02
        ax_tube(acc, (px, py, DOCK_PILING_BOT), (px, py, top), DOCK_PILING_R, DOCK_PILING_R * 0.92,
                10, M_WOODDARK)
        ax_tube(acc, (px, py, top), (px, py, top + 0.055), DOCK_PILING_R * 1.12,
                DOCK_PILING_R * 1.05, 10, M_WOODDARK)                     # 柱头加箍
        if mooring:
            for cz in (top - 0.18, top - 0.27):                           # 系缆绳圈
                rope_ring(acc, px, py, cz, 0.155, 0.045)

    # ---- 横向底撑（三档，撑在左右立柱之间；桥下仰视可见结构感）----
    for py in (-2.4, 0.0, 2.4):
        ax_box(acc, (0, py, -0.72), (1.38, 0.12, 0.15), M_WOODDARK)

    # ---- 尾端下水梯（水面 -0.65 在梯子中段，读得出"入水"）----
    for lx in (-0.28, 0.28):
        ax_box(acc, (lx, -L * 0.5 + 0.1, -0.60), (0.07, 0.07, 1.15), M_WOODDARK)
    for rz in (-0.16, -0.42, -0.68, -0.94):
        ax_tube(acc, (-0.28, -L * 0.5 + 0.1, rz), (0.28, -L * 0.5 + 0.1, rz), 0.028, 0.028, 8, M_WOODMID)


def rope_ring(acc, x, y, z, major, minor):
    """水平绳圈（绕立柱的缆绳环），绕 z 轴的圆环面。分段数是栈桥面数的主要来源（20×8）。"""
    ma, mi = 20, 8
    pts = []
    for i in range(ma):
        a = 2 * math.pi * i / ma
        ca, sa = math.cos(a), math.sin(a)
        for j in range(mi):
            b = 2 * math.pi * j / mi
            rr = major + minor * math.cos(b)
            pts.append((x + rr * ca, y + rr * sa, z + minor * math.sin(b)))
    faces = []
    for i in range(ma):
        i2 = (i + 1) % ma
        for j in range(mi):
            j2 = (j + 1) % mi
            faces.append((i * mi + j, i2 * mi + j, i2 * mi + j2, i * mi + j2))
    acc.add(pts, faces, M_ROPE, smooth=True)


# ============================================================================
# 合网格 / 统计 / 导出
# ============================================================================

def join_to_object(acc, obj_name, mats):
    """MeshAcc → 单网格对象（多材质槽，Unity 侧一个 renderer 拿全部 Kit_ 槽）。
    只挂实际用到的材质槽（空槽不导出：Dock 不会背上帆布/黄铜空槽）。"""
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
            bf.smooth = sm
        except ValueError:
            pass                                                    # 重复面（理论不出现）跳过
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])            # 法线朝外/岛内一致
    bm.to_mesh(me)
    bm.free()
    for old in used:
        me.materials.append(mats[old][1])
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def report(obj, label):
    """打印 README 要引用的统计：面数 / 材质槽 / 包围盒。"""
    me = obj.data
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    slots = [s.name for s in me.materials if s is not None]
    dims = obj.dimensions
    log('STAT %s tris=%d verts=%d polys=%d slots=%s bbox=%.2fx%.2fx%.2f'
        % (label, tris, len(me.vertices), len(me.polygons),
           '|'.join(slots), dims.x, dims.y, dims.z))


def export_fbx(obj, filepath):
    """FBX 导出（口径）：+Y 上 / -Z 前（axis_forward='-Z', axis_up='Y' 默认值即此，
    这里显式写出防止默认变更）；缩放按 FBX_SCALE_NONE + useFileUnits 在 Unity 侧 1:1。"""
    deselect_all()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        apply_scale_options='FBX_SCALE_NONE',   # 不在导出侧乘单位因子（Unity useFileUnits=true 接）
        path_mode='COPY',                       # 不嵌贴图（本资产零贴图）
        axis_forward='-Z', axis_up='Y',         # → FBX 空间 +Y 上 / -Z 前（船艏朝 -Z）
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        bake_anim=False,
    )
    log('exported %s (%d bytes)' % (filepath, os.path.getsize(filepath)))


def deselect_all():
    for o in bpy.context.scene.objects:
        o.select_set(False)


# ============================================================================
# 预览渲染（中性灰背景 + 三灯；1024²；Cycles GPU→CPU 回退）
# ============================================================================

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
    # 铁律（同 render_icon.py）：Standard 视图变换，AgX 会洗掉调色板色值
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    log('render engine=Cycles device=%s samples=%d' % (gpu or 'CPU', SAMPLES))


def setup_preview_stage(scene, floor_z):
    """中性灰世界 + 灰地板（板在资产最低点之下，能看到完整剪影与接触阴影）+ 三灯。"""
    world = bpy.data.worlds.new('pilot_world')
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs[0].default_value = srgb(0x7F7F7F)                     # 中性灰背景
    bg.inputs[1].default_value = 1.0

    floor_mat = bpy.data.materials.new('pilot_floor')
    floor_mat.use_nodes = True
    fb = floor_mat.node_tree.nodes['Principled BSDF']
    fb.inputs['Base Color'].default_value = srgb(0x8C8C8C)
    fb.inputs['Roughness'].default_value = 0.9
    floor = bpy.data.objects.new('pilot_floor', bpy.data.meshes.new('pilot_floor'))
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60)
    bm.to_mesh(floor.data)
    bm.free()
    floor.location = (0, 0, floor_z)
    floor.data.materials.append(floor_mat)
    scene.collection.objects.link(floor)

    def area(name, size, loc, energy, color, target):
        d = bpy.data.lights.new(name, 'AREA')
        d.size = size
        d.energy = energy
        d.color = color
        o = bpy.data.objects.new(name, d)
        scene.collection.objects.link(o)
        o.location = loc
        di = mathutils.Vector(target) - mathutils.Vector(loc)
        o.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()

    # 三灯：暖主光（左前上）+ 冷辅光（右侧）+ 暖逆光（后上勾轮廓）
    area('key', 10.0, (-12, -14, 12), 2600, (1.0, 0.94, 0.84), (0, 0, 2))
    area('fill', 12.0, (14, -4, 7), 900, (0.62, 0.75, 1.0), (0, 0, 2))
    area('rim', 8.0, (3, 16, 10), 1800, (1.0, 0.9, 0.75), (0, 0, 2.5))


def render_views(scene, obj, prefix, out_dir, views):
    deselect_all()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for name, loc, target in views:
        cam_data = bpy.data.cameras.new('pilot_cam')
        cam_data.lens = 50
        cam_data.clip_end = 300
        cam = bpy.data.objects.new('pilot_cam', cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        di = mathutils.Vector(target) - mathutils.Vector(loc)
        cam.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        path = os.path.join(out_dir, '%s-%s.jpg' % (prefix, name))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        log('rendered %s (%d bytes)' % (path, os.path.getsize(path)))
        bpy.data.objects.remove(cam, do_unlink=True)


# ============================================================================
# 主流程
# ============================================================================

def build_and_export(label, build_fn, mats, fbx_path, views, floor_z):
    acc = MeshAcc()
    build_fn(acc)
    n_verts, n_polys, n_tris = acc.counts()
    log('%s raw: verts=%d polys=%d tris=%d' % (label, n_verts, n_polys, n_tris))
    obj = join_to_object(acc, label, mats)
    deselect_all()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    report(obj, label)
    export_fbx(obj, fbx_path)
    if RENDER:
        render_views(bpy.context.scene, obj, label.lower(), PREVIEW_DIR, views)
    return obj


def main():
    parse_args()
    for d in (FBX_DIR, PREVIEW_DIR, WORK_DIR):
        os.makedirs(d, exist_ok=True)

    # 清空 factory 场景（防御性，保证幂等）
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0                          # 1 Blender 单位 = 1 米

    mats = make_materials()
    if RENDER:
        setup_render(scene)

    ship_views = [
        ('front34', (-11.5, -12.5, 6.5), (0, -1.0, 2.2)),   # 艏 3/4（艏朝 -Y）
        ('side', (-16.5, 0.5, 3.6), (0, 0, 2.4)),           # 正侧
        ('back', (7.5, 12.0, 5.5), (0, 0, 2.2)),            # 艉 3/4
    ]
    dock_views = [
        ('front34', (4.6, -6.2, 3.0), (0, -0.4, -0.35)),
        ('side', (-8.0, 0.3, 1.2), (0, 0, -0.45)),
        ('back', (3.6, 6.6, 2.6), (0, 0.3, -0.4)),
    ]

    if ONLY in ('all', 'flagship'):
        log('== flagship ==')
        setup_preview_stage(scene, SHIP_KEEL_Z - 0.06) if RENDER else None
        build_and_export('Flagship', build_flagship, mats,
                         os.path.join(FBX_DIR, 'Flagship.fbx'), ship_views, None)
        for o in list(bpy.data.objects):
            if o.name not in ('key', 'fill', 'rim') and o.name != 'pilot_floor':
                bpy.data.objects.remove(o, do_unlink=True)

    if ONLY in ('all', 'dock'):
        log('== dock ==')
        if RENDER:
            floor = bpy.data.objects.get('pilot_floor')
            if floor:
                floor.location.z = DOCK_PILING_BOT - 0.06
        build_and_export('Dock', build_dock, mats,
                         os.path.join(FBX_DIR, 'Dock.fbx'), dock_views, None)

    if RENDER:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, 'scene_kit_debug.blend'))
    log('done in %.1fs total' % (time.time() - T0))


if __name__ == '__main__':
    main()
