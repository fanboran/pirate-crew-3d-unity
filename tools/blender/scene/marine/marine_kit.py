# -*- coding: utf-8 -*-
"""marine_kit.py —— 船与码头 kit（WorldKit-Marine）无头建模（Blender 无头，纯程序化，零外部素材）。

与样板 tools/blender/scene/build_scene_kit.py（Flagship/Dock）同风格同质量线的"残破/功能性扩展"：
9 件资产，槽/预算/站面工具一律走 style_tokens（ST），配色零散写。规格见任务书（M4 §4.3 marine 域）。

资产清单（FBX 名固定；原点：大件=足印中心 z=0 海平面、水下做到 z≈-3；小件=落地接触面中心）：
    WreckBowHalf     搁浅巨舰艏半截 14×6，艏翘 6°，甲板 3 段平台阶 +0.5/+1.0/+1.5，
                     艏斜桅断裂下垂，舷侧破洞 2，断裂面撕板，焦黑区，缆绳盘            站面 3 box
    WreckSternHalf   艉半截 12×6，艉楼 +1.0 / 主甲板 +0.5，舵残件，艉窗 3 拱，船底搁浅   站面 2 box
    MastBridge       断桅桥：倒伏主桅当桥，走道 16×1.6 @ +1.5（中段断口可跳），
                     瞭望斗平台 3×3 @ +4.5，绳梯残段、帆布垂条                          站面 2 box
    LighthouseTower  灯塔：岛台 12×12 @ +0.5（礁石裙边），锥柱塔身高 7，环台 Ø4 @ +3.5，
                     灯室平台 Ø3.5 @ +7.5（Kit_Ember 玻璃微亮，Kit_Iron 顶锥帽）        站面 3 box
    PierLong         长栈桥 18×4 @ +1.0：错缝板、6 立柱入水、两侧系船柱、一段栏索断垂    站面 1 box
    PierHead         桥头平台 8×8 @ +1.0：转角段、双系船柱、堆缆圈                       站面 1 box
    BuoyRing         系船浮标：浮体 Ø1.2 + 铃架高 2.2（Kit_Iron+Kit_Rope），随波姿态     无
    RowboatBeached   搁浅小艇 4×1.4×0.8：艇身破缝、一桨插沙                              无
    AnchorMonument   锚碑：巨锚高 3.5 立于石墩 2×2×0.8，锚环缆绳一段                     无

站面 manifest（<名>.standable.json，与 ST.write_standable_manifest 格式完全一致）：
    全部 box 过 ST.check_standable_boxes 时直接走 ST 写出；含水平最小边 < MIN_STANDABLE_SIZE(4.0)
    的窄站面（桅桥走道 1.6 / 灯室平台 3.5 / 艏段第 3 级台阶）经用户裁决走本地放宽校验——仍强制
    顶面 0.5 档 / 平面度容差 / 正高度 / 有限数，仅 min-size 降级为告警（用户 2026-09-17 裁决）。
    每 box 支持可选 "yaw"（度，绕资产本地原点；协调者 2026-09-17 约定）；本套件几何全部正交，yaw 未用。

复现（仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/marine/marine_kit.py -- [--only all|<资产名>] [--samples N] [--no-render]

产物：
    pirate-crew/Assets/Art/Models/WorldKit/Marine/<名>.fbx + <名>.standable.json
    export/worldkit-marine/<名>-front34.jpg / <名>-side.jpg （1024² q90，Standard 视图变换）
    external/worldkit-marine-work/marine_kit_debug.blend （调参 GUI 缓存，gitignored）
"""

import math
import os
import random
import sys
import time

import bpy
import bmesh
import mathutils
from mathutils import Euler, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import style_tokens as ST

# ============================================================================
# 参数区 —— 调参旋钮集中在此（README 按行号引用）
# ============================================================================

ONLY = 'all'            # 'all' | 资产名（WreckBowHalf/WreckSternHalf/MastBridge/LighthouseTower/
                        #   PierLong/PierHead/BuoyRing/RowboatBeached/AnchorMonument）
SAMPLES = 96            # Cycles 采样数（预览图；迭代期可用 --samples 48 加速）
RENDER = True           # False = 只建模导出 FBX + manifest，不出预览图
RES = ST.PREVIEW_RES    # 预览图边长（1024）
JPG_QUALITY = ST.PREVIEW_QUALITY  # 预览图 JPEG 质量（90）
RNG_SEED = 20260917     # 破损细节伪随机种子（重跑结果可复现）

# ---- WreckBowHalf 搁浅艏半截（z=0 海平面；船长沿 Y，艏朝 -Y）----
BOW_LEN = 14.0          # 总长（y -7 断裂面 +7 …-7 艏柱；断面在 +7 侧）
BOW_HALF_BEAM = 3.0     # 半宽（船宽 6）
BOW_TILT_DEG = 6.0      # 艏部翘起视觉倾角（艏 -Y 端抬高）
BOW_KEEL_CUT = -2.2     # 龙骨 z@断面（未倾斜系）→ 倾斜后 ≈-2.93（水下 ≈-3 口径）
BOW_KEEL_STEM = -1.0    # 龙骨 z@艏柱（未倾斜系）→ 倾斜后 ≈-0.26（搁浅翘起）
BOW_DECK_CUT = 1.25     # 舷侧甲板线 z@断面（未倾斜系）
BOW_DECK_STEM = 1.55    # 舷侧甲板线 z@艏柱（未倾斜系）
BOW_RAIL = 0.48         # 舷墙高（残破低墙：保证 30-60m 剪影能读到甲板 3 阶）
BOW_STATIONS = 19       # 放样站数
# 平台阶（顶面严格 0.5 档；[y0, y1, 顶高]；倾斜只进船壳视觉，站面永远水平）
BOW_STEPS = [(2.2, 6.3, 0.5), (-2.4, 2.2, 1.0), (-6.5, -2.4, 1.5)]

# ---- WreckSternHalf 艉半截 ----
STN_LEN = 12.0          # y -6 断裂面 … +6 艉封板
STN_HALF_BEAM = 3.0
STN_TILT_DEG = 2.0      # 断裂端（-Y）微沉入水、艉端微翘（触地搁浅感）
STN_KEEL_CUT = -2.1     # 龙骨 z@断面 → 倾斜后 ≈-2.31（断口入水）
STN_KEEL_TRANSOM = -1.35  # 龙骨 z@艉封板 → 倾斜后 ≈-1.14（船底搁浅触地）
STN_DECK_CUT = 1.0      # 主甲板线 z@断面（未倾斜系）
STN_DECK_TRANSOM = 1.15
STN_RAIL = 0.45
STN_STATIONS = 17
STN_MAIN_STEP = (-5.5, 1.7, 0.5)    # 主甲板平台阶
STN_CASTLE_STEP = (1.8, 5.85, 1.0)  # 艉楼平台阶
STN_CASTLE_HALF = 2.2   # 艉楼半宽（楼体结构）

# ---- MastBridge 断桅桥 ----
MB_SPAN = 16.0          # 走道总长（y -8 … +8）
MB_WALK_W = 1.6         # 走道宽
MB_WALK_TOP = 1.5       # 走道顶面（0.5 档）
MB_GAP = 1.6            # 中段断口长（跳距教学点，wreck_hymn"教学翻滚与跳距"）
MB_TRUNK_R = 0.625      # 倒伏主桅半径（Ø1.25）
MB_NEST_TOP = 4.5       # 瞭望斗平台顶（0.5 档）
MB_NEST_SIZE = 3.0      # 瞭望斗平台 3×3
MB_NEST_POS = (1.35, 2.2)   # 瞭望斗中心 (x, y)：立在走道旁的断桅桩顶
MB_SUPPORT_BOT = -1.2   # 端部礁墩入水底

# ---- LighthouseTower 灯塔 ----
LH_ISLAND = 12.0        # 基座岛台边长（顶面 +0.5）
LH_ISLAND_TOP = 0.5
LH_ISLAND_BOT = -1.3    # 岛台裙边底
LH_TOWER_TOP = 7.5      # 塔身锥柱顶（高 7：0.5→7.5）
LH_TOWER_R0 = 1.65      # 塔底半径（Ø3.3）
LH_TOWER_R1 = 1.02      # 塔顶半径（Ø2.05）
LH_RING_TOP = 4.0       # 中段环台顶（Ø4；0.5→4.0→7.5 两跳各 3.5 = MaxUpStep 达标，M4 §2.2）
LH_RING_RO = 2.0
LH_LAMP_TOP = 7.5       # 灯室平台顶（Ø3.5）
LH_LAMP_RO = 1.75
LH_LAMP_GLASS_TOP = 8.95    # 玻璃条带顶
LH_CAP_TOP = 10.35      # 顶锥帽+避雷针尖（全件最高）

# ---- PierLong 长栈桥 ----
PL_LEN = 18.0
PL_WIDTH = 4.0
PL_TOP = 1.0            # 桥面顶（0.5 档）
PL_PILE_BOT = -2.6      # 立柱入水底
PL_PILE_R = 0.15
PL_BOLL_TOP = 0.5       # 系船柱高出桥面（顶 +1.5）

# ---- PierHead 桥头平台 ----
PH_SIZE = 8.0
PH_TOP = 1.0
PH_PILE_BOT = -2.6
PH_STUB = (-4.0, -1.0, 4.0, 6.8)   # 转角段 (x0, x1, y0, y1)：从 +Y 边伸出的 90° 转角引桥
PH_BOLL_TOP = 0.55

# ---- BuoyRing 系船浮标 ----
BU_BODY_R0 = 0.6        # 浮体底半径（Ø1.2）
BU_BODY_R1 = 0.31       # 浮体顶盘半径
BU_BODY_H = 0.95        # 浮体总高（原点 z=0 = 吃水接触面）
BU_FRAME_TOP = 2.2      # 铃架顶高
BU_ROLL = (9.0, 4.0)    # 随波姿态（绕 X / 绕 Y，度）

# ---- RowboatBeached 搁浅小艇 ----
RB_LEN = 4.0
RB_HALF_BEAM = 0.7
RB_DEPTH = 0.8
RB_ROLL = 7.0           # 搁浅侧倾（度）
RB_TRIM = 2.0           # 尾坐头翘微纵倾（度）

# ---- AnchorMonument 锚碑 ----
AM_PLINTH = (2.0, 2.0, 0.8)   # 石墩（x,y,z；原点=墩底接触面中心）
AM_ANCHOR_TOP = 3.5     # 锚环顶总高
AM_SHANK_TOP = 3.15     # 锚杆顶
AM_STOCK_Z = 2.85       # 横杆高度
AM_RING_C = 3.32        # 锚环中心 z

# ============================================================================
# 路径与控制台
# ============================================================================

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
FBX_DIR = os.path.join(ROOT, 'pirate-crew', 'Assets', 'Art', 'Models', 'WorldKit', 'Marine')
PREVIEW_DIR = os.path.join(ROOT, 'export', 'worldkit-marine')
WORK_DIR = os.path.join(ROOT, 'external', 'worldkit-marine-work')

T0 = time.time()


def log(msg):
    print('[marinekit] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


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


def srgb_hex(hex_str):
    """'#RRGGBB'（ST 调色板）→ Blender 线性 RGBA。hex 值只出自 ST.SLOTS，此处仅做色彩空间换算。"""
    def lin(v):
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = ST.hex_to_rgb(hex_str)
    return (lin(r), lin(g), lin(b), 1.0)


# ============================================================================
# 站面 manifest（ST 优先；窄站面走用户裁决的本地放宽校验；支持可选 yaw）
# ============================================================================

def standable_write(name, boxes, fbx_path):
    """优先 ST.write_standable_manifest（全过 ST 校验）；含 <4.0 窄站面时本地放宽校验后写出。

    放宽口径（用户 2026-09-17 裁决）：仍强制顶面 0.5 档 / MIN_TOP_Z / 正高度 / 有限数，
    仅 MIN_STANDABLE_SIZE 降级为告警；JSON 格式与 ST 完全一致，box 可带可选 'yaw'（度）。
    """
    try:
        ST.check_standable_boxes(boxes, name)
        path = ST.write_standable_manifest(name, boxes, fbx_path)
        log('manifest %s: %d boxes（ST 校验全过）-> %s' % (name, len(boxes), path))
        return path
    except ValueError as why:
        log('manifest %s: ST 严格校验未全过（%s）→ 放宽校验（仅豁免 min-size）' % (name, why))
    for i, b in enumerate(boxes):
        c, s = b['c'], b['s']
        if not all(math.isfinite(v) for v in list(c) + list(s)):
            raise ValueError('[%s] 放宽校验 box#%d 含非有限数' % (name, i))
        if s[2] <= 0:
            raise ValueError('[%s] 放宽校验 box#%d 高度非正' % (name, i))
        top = c[2] + s[2] / 2.0
        if abs(top / ST.TOP_STEP - round(top / ST.TOP_STEP)) > ST.PLANARITY_TOL / ST.TOP_STEP:
            raise ValueError('[%s] 放宽校验 box#%d 顶面 z=%.4f 不在 0.5 档' % (name, i, top))
        if top < ST.MIN_TOP_Z - ST.PLANARITY_TOL:
            raise ValueError('[%s] 放宽校验 box#%d 顶面 z=%.4f 低于最低档' % (name, i, top))
        if min(s[0], s[1]) < ST.MIN_STANDABLE_SIZE:
            log('  RELAXED box#%d: 水平最小边 %.2f < %.2f（窄站面，规格表保留）'
                % (i, min(s[0], s[1]), ST.MIN_STANDABLE_SIZE))
    out = os.path.join(os.path.dirname(os.path.abspath(fbx_path)), name + '.standable.json')
    import json
    with open(out, 'w', encoding='utf-8') as f:
        json.dump({'asset': name, 'boxes': boxes}, f, indent=1)
    log('manifest %s: %d boxes（RELAXED，含 yaw 支持）-> %s' % (name, len(boxes), out))
    return out


# ============================================================================
# 几何累积器与图元 helper（与样板 build_scene_kit.py 同款）
# ============================================================================

class MeshAcc:
    """单件资产的几何累积器。mat 序号对应 make_materials 返回列表的下标。"""

    def __init__(self):
        self.verts = []
        self.faces = []
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


def ax_box(acc, center, size, mat, smooth=False):
    """轴对齐盒。center=中心，size=(sx,sy,sz)。"""
    ax_box_r(acc, center, size, mat, rot=(0.0, 0.0, 0.0), smooth=smooth)


def ax_box_r(acc, center, size, mat, rot=(0.0, 0.0, 0.0), smooth=False):
    """可旋转盒（欧拉 XYZ，弧度）。撕裂板/断桨/搁浅姿态等斜置细节用。"""
    cx, cy, cz = center
    sx, sy, sz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    e = Euler(rot, 'XYZ').to_matrix()
    pts = []
    for dx in (-sx, sx):
        for dy in (-sy, sy):
            for dz in (-sz, sz):
                p = e @ Vector((dx, dy, dz))
                pts.append((cx + p.x, cy + p.y, cz + p.z))
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    acc.add(pts, faces, mat, smooth)


def ax_tube(acc, p0, p1, r1, r2, sides, mat, smooth=True, caps=True):
    """圆台/圆柱/圆杆：p0→p1 轴线，r1=起点半径，r2=终点半径。"""
    d = Vector(p1) - Vector(p0)
    length = d.length
    if length < 1e-6:
        return
    d = d.normalized()
    up = Vector((0, 0, 1))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0, 1, 0))
    u = d.cross(up).normalized()
    v = d.cross(u).normalized()
    c0, c1 = Vector(p0), Vector(p1)
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
        faces.append((k, k2, sides + k2, sides + k))
    if caps:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range(sides, 2 * sides)))
    acc.add(pts, faces, mat, smooth)


def ax_grid(acc, points, rows, cols, mat, smooth=True, flip=False):
    """rows×cols 点阵连四边形条带（帆布/玻璃条带/平台面）。points 行优先二维列表。"""
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


def rope_ring(acc, x, y, z, major, minor, mat, ma=16, mi=6, axis='z'):
    """绳圈（环面）。ma/mi 分段比样板(20×8)收一档控面数。axis='z' 水平圈 / 'x','y' 立圈。"""
    pts = []
    for i in range(ma):
        a = 2 * math.pi * i / ma
        for j in range(mi):
            b = 2 * math.pi * j / mi
            rr = major + minor * math.cos(b)
            h = minor * math.sin(b)
            if axis == 'z':
                pts.append((x + rr * math.cos(a), y + rr * math.sin(a), z + h))
            elif axis == 'y':
                pts.append((x + rr * math.cos(a), y + h, z + rr * math.sin(a)))
            else:
                pts.append((x + h, y + rr * math.cos(a), z + rr * math.sin(a)))
    faces = []
    for i in range(ma):
        i2 = (i + 1) % ma
        for j in range(mi):
            j2 = (j + 1) % mi
            faces.append((i * mi + j, i2 * mi + j, i2 * mi + j2, i * mi + j2))
    acc.add(pts, faces, mat, smooth=True)


def rope_coil(acc, x, y, z, major, mat, minor=0.045, layers=2):
    """盘绕缆绳堆：2-3 层绳圈叠成微穹顶（层距收紧使圈圈相触）。"""
    for k in range(layers):
        rope_ring(acc, x, y, z + minor * (0.8 + 0.95 * k), major - 0.13 * k, minor, mat)
    rope_ring(acc, x, y, z + minor * (0.8 + 0.95 * layers), major * 0.45, minor, mat)


def sag_rope(acc, p0, p1, sag, r, mat, segs=6, sides=6, end_fray=False):
    """下垂缆绳：二次贝塞尔下垂弧折线 → 短圆杆链。"""
    a, b = Vector(p0), Vector(p1)
    prev = a
    for k in range(1, segs + 1):
        t = k / segs
        p = a.lerp(b, t)
        p.z -= sag * 4.0 * t * (1.0 - t)
        rr = r * (1.0 - (0.55 * t if end_fray else 0.0))
        ax_tube(acc, tuple(prev), tuple(p), r if k > 1 else r, rr, sides, mat)
        prev = p


def rail_ring(acc, cx, cy, r, z, r_tube, mat, sides=12, tube_sides=6):
    """水平圆环栏杆（管链）。"""
    pts = [(cx + r * math.cos(2 * math.pi * k / sides), cy + r * math.sin(2 * math.pi * k / sides), z)
           for k in range(sides)]
    for k in range(sides):
        ax_tube(acc, pts[k], pts[(k + 1) % sides], r_tube, r_tube, tube_sides, mat)


def ring_slab(acc, cx, cy, z_top, r_in, r_out, thick, sides, mat, smooth=False):
    """水平环台（内外壁 + 顶底面）：灯塔环台/灯室平台/铁箍盘。"""
    top_o, top_i, bot_o, bot_i = [], [], [], []
    for k in range(sides):
        a = 2 * math.pi * k / sides
        ca, sa = math.cos(a), math.sin(a)
        top_o.append((cx + r_out * ca, cy + r_out * sa, z_top))
        top_i.append((cx + r_in * ca, cy + r_in * sa, z_top))
        bot_o.append((cx + r_out * ca, cy + r_out * sa, z_top - thick))
        bot_i.append((cx + r_in * ca, cy + r_in * sa, z_top - thick))
    def quads(ring_a, ring_b, flip=False):
        fs = []
        for k in range(sides):
            k2 = (k + 1) % sides
            fs.append((k, k2, sides + k2, sides + k) if not flip
                      else (k2, k, sides + k, sides + k2))
        return fs
    base = len(acc.verts)
    acc.verts.extend(tuple(p) for p in top_o + top_i + bot_o + bot_i)
    for f in quads(top_o, top_i):
        acc.faces.append(tuple(base + i for i in f)); acc.face_mat.append(mat); acc.face_smooth.append(smooth)
    for f in quads(top_i, bot_i, flip=True):
        acc.faces.append(tuple(base + i for i in f)); acc.face_mat.append(mat); acc.face_smooth.append(smooth)
    for f in quads(bot_o, bot_i, flip=True):
        acc.faces.append(tuple(base + i for i in f)); acc.face_mat.append(mat); acc.face_smooth.append(smooth)
    for f in quads(bot_o, top_o):
        acc.faces.append(tuple(base + i for i in f)); acc.face_mat.append(mat); acc.face_smooth.append(smooth)


def boulder(acc, center, rx, ry, rz, mat, rng, sides=7, rings=3, smooth=True):
    """礁石/砾石：低分瓣瘪球，顶点随机抖动（确定性 rng），上下极点封口。"""
    cx, cy, cz = center
    pts = [(cx, cy, cz + rz)]
    for j in range(1, rings):
        v = j / rings
        phi = v * math.pi
        zr = math.cos(phi)
        rr = math.sin(phi)
        for k in range(sides):
            a = 2 * math.pi * k / sides
            jit = 1.0 + rng.uniform(-0.16, 0.16)
            pts.append((cx + rx * rr * jit * math.cos(a),
                        cy + ry * rr * jit * math.sin(a),
                        cz + rz * zr * (1.0 + rng.uniform(-0.1, 0.1))))
    pts.append((cx, cy, cz - rz))
    top_i, bot_i = 0, len(pts) - 1
    faces = []
    for k in range(sides):                       # 顶极点扇面
        k2 = (k + 1) % sides
        faces.append((top_i, 1 + k, 1 + k2))
    for j in range(rings - 2):                   # 中间环带
        for k in range(sides):
            k2 = (k + 1) % sides
            a = j * sides + k
            b = j * sides + k2
            c = (j + 1) * sides + k2
            d = (j + 1) * sides + k
            faces.append((a, b, c, d))
    base = (rings - 2) * sides + 1
    for k in range(sides):                       # 底极点扇面
        k2 = (k + 1) % sides
        faces.append((base + k2, base + k, bot_i))
    acc.add(pts, faces, mat, smooth)


def hanging_cloth(acc, p0, p1, length, mat, rows=5, cols=4, sway=0.16, flare=0.10):
    """垂挂帆布条：顶边 p0→p1，向下摆垂，微 S 摆 + 外扩。"""
    a, b = Vector(p0), Vector(p1)
    perp = (b - a).cross(Vector((0, 0, 1)))
    if perp.length < 1e-5:
        perp = Vector((1, 0, 0))
    perp.normalize()
    pts = []
    for j in range(rows):
        v = j / (rows - 1)
        row = []
        for i in range(cols):
            u = i / (cols - 1)
            p = a.lerp(b, u)
            p.z -= length * v
            p = p + perp * (sway * v * math.sin(math.pi * u) + flare * v)
            row.append(tuple(p))
        pts.append(row)
    ax_grid(acc, pts, rows, cols, mat, smooth=True)


def rope_ladder(acc, top_c, bot_c, width, n_rungs, mat_rope, mat_wood, bow=0.12):
    """绳梯（残段）：2 根侧绳（微侧弓）+ 木踏棍。"""
    top_c, bot_c = Vector(top_c), Vector(bot_c)
    d = bot_c - top_c
    if d.length < 1e-5:
        return
    d.normalize()
    perp = d.cross(Vector((0, 0, 1)))
    if perp.length < 1e-5:
        perp = Vector((1, 0, 0))
    perp.normalize()
    def rail_pt(v, side):
        p = top_c.lerp(bot_c, v) + perp * (side * width * 0.5)
        p += perp * (bow * math.sin(math.pi * v))    # 残梯侧弓
        return tuple(p)
    for side in (-1, 1):
        prev = rail_pt(0.0, side)
        for k in range(1, 4):
            v = k / 3.0
            p = rail_pt(v, side)
            ax_tube(acc, prev, p, 0.022, 0.022, 5, mat_rope)
            prev = p
    for k in range(n_rungs):
        v = (k + 0.5) / n_rungs
        ax_tube(acc, rail_pt(v, -1), rail_pt(v, 1), 0.026, 0.026, 6, mat_wood)


def torn_edge(acc, pts, mat, rng, n, span=0.5, hmin=0.1, hmax=0.5, thick=0.1, mat2=None):
    """撕裂板缘：沿折线 pts 撒竖向撕断板头（高度随机、轻微歪斜）。pts 允许 2D (x,y)（z=0）。"""
    def v3(p):
        p = tuple(p)
        return Vector((p[0], p[1], p[2] if len(p) > 2 else 0.0))
    pts = [v3(p) for p in pts]
    for k in range(n):
        t = (k + rng.uniform(0.15, 0.85)) / n
        i = min(int(t * (len(pts) - 1)), len(pts) - 2)
        f = t * (len(pts) - 1) - i
        p = pts[i].lerp(pts[i + 1], f)
        h = rng.uniform(hmin, hmax)
        tilt = rng.uniform(-0.35, 0.35)
        ax_box_r(acc, (p.x, p.y, p.z + h * 0.5), (thick, span * rng.uniform(0.5, 1.0), h), mat,
                 rot=(0.0, tilt, rng.uniform(-0.2, 0.2)))


def flap_plank(acc, base, out_dir, length, mat, rng, tilt=None, w=0.22, thick=0.045):
    """外翻撕断板：底边贴 base，朝 out_dir 翻出。"""
    bx, by, bz = base
    dx, dy = out_dir
    if tilt is None:
        tilt = rng.uniform(0.5, 1.1)
    ang = tilt
    cx = bx + dx * math.cos(ang) * length * 0.5
    cy = by + dy * math.cos(ang) * length * 0.5
    cz = bz + math.sin(ang) * length * 0.5
    yaw = math.atan2(dy, dx) - math.pi * 0.5
    ax_box_r(acc, (cx, cy, cz), (w, thick, length), mat, rot=(0.0, 0.0, yaw))


# ============================================================================
# 船壳放样（WreckBowHalf / WreckSternHalf 共用；样板 HULL_SECTION 思路 + 破船倾斜）
# ============================================================================

HULL_SECTION = [
    (0.04, 0.00),   # 龙骨底
    (0.42, 0.16),   # 龙骨侧垫
    (0.72, 0.38),   # 舭部转弯
    (0.97, 0.62),   # 最宽
    (0.94, 0.88),   # 上舷内倾
    (0.82, 1.00),   # 栏杆顶
]


class WreckHull:
    """破船半截的外壳几何口径：半宽/龙骨/甲板线/倾斜（倾斜只进视觉壳，不进站面）。"""

    def __init__(self, y_lo, y_hi, half_beam, hw_fn, keel_fn, deck_fn, rail_h, tilt_fn):
        self.y_lo, self.y_hi = y_lo, y_hi
        self.half_beam = half_beam
        self.hw_fn, self.keel_fn, self.deck_fn = hw_fn, keel_fn, deck_fn
        self.rail_h, self.tilt_fn = rail_h, tilt_fn

    def t_at(self, y):
        return (y - self.y_lo) / (self.y_hi - self.y_lo)

    def rail_z(self, y):
        return self.deck_fn(y) + self.rail_h + self.tilt_fn(y)

    def section_pts(self, y):
        """横剖折线（含倾斜）：[(x, z), ...]，x 为半宽绝对值。"""
        hw = self.hw_fn(self.t_at(y)) * self.half_beam
        zk = self.keel_fn(y) + self.tilt_fn(y)
        zr = self.rail_z(y)
        return [(lat * hw, zk + up * (zr - zk)) for lat, up in HULL_SECTION]

    def half_at_world_z(self, y, z_world, inset=0.0):
        """倾斜后船壳在 (y, z_world) 处的半宽（站面/平台收边用）。"""
        pts = self.section_pts(y)
        zz = z_world
        if zz <= pts[0][1]:
            return pts[0][0] - inset
        for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
            if z0 <= zz <= z1:
                f = 0.0 if z1 - z0 < 1e-6 else (zz - z0) / (z1 - z0)
                return (x0 + (x1 - x0) * f) - inset
        return max(0.05, pts[-1][0] - inset)

    def loft(self, acc, stations, mat_dark, mat_mid, waterline=0.32):
        """外壳条带：水线附近暗木（浸水渍），其余中木。"""
        for s in range(stations - 1):
            y0 = self.y_lo + (self.y_hi - self.y_lo) * s / (stations - 1)
            y1 = self.y_lo + (self.y_hi - self.y_lo) * (s + 1) / (stations - 1)
            p0, p1 = self.section_pts(y0), self.section_pts(y1)
            for side in (1, -1):
                for c in range(len(HULL_SECTION) - 1):
                    quad = [(side * p0[c][0], y0, p0[c][1]),
                            (side * p0[c + 1][0], y0, p0[c + 1][1]),
                            (side * p1[c + 1][0], y1, p1[c + 1][1]),
                            (side * p1[c][0], y1, p1[c][1])]
                    mid_z = (p0[c][1] + p0[c + 1][1] + p1[c][1] + p1[c + 1][1]) * 0.25
                    acc.add(quad, [(0, 1, 2, 3)],
                            mat_dark if mid_z < waterline else mat_mid, smooth=True)

    def end_face(self, acc, y, rake, mat):
        """断口/艉封板：n 边形封面（rake=上段外倾）。"""
        pts = self.section_pts(y)
        poly = [(p[0], y + p[1] / max(0.3, self.rail_z(y)) * rake, p[1]) for p in pts]
        poly += [(-p[0], y + p[1] / max(0.3, self.rail_z(y)) * rake, p[1])
                 for _, p in list(reversed(list(enumerate(pts))))[1:-1]]
        acc.add(poly, [tuple(range(len(poly)))], mat)

    def bulwark_caps(self, acc, y_ranges, r, mat, sides=8):
        """舷墙顶帽杆（可给断口区间 → 断栏）。y_ranges: [(y0,y1), ...] 保留段。"""
        n = 14
        for side in (1, -1):
            for y0, y1 in y_ranges:
                prev = None
                for k in range(n + 1):
                    y = y0 + (y1 - y0) * k / n
                    pts = self.section_pts(y)
                    p = (side * pts[-1][0], y, pts[-1][1])
                    if prev is not None:
                        ax_tube(acc, prev, p, r, r, sides, mat)
                    prev = p

    def platform_slab(self, acc, y0, y1, z_top, thick, mat, inset=0.14, steps=6):
        """站面平台阶 slab：顶面严格 z_top 水平，两侧缘随壳内收（藏在舷墙后）。"""
        for k in range(steps):
            ya = y0 + (y1 - y0) * k / steps
            yb = y0 + (y1 - y0) * (k + 1) / steps
            wa = self.half_at_world_z((ya + yb) * 0.5, z_top, inset)
            acc.add([(-wa, ya, z_top), (wa, ya, z_top), (wa, yb, z_top), (-wa, yb, z_top)],
                    [(0, 1, 2, 3)], mat)
            acc.add([(-wa, ya, z_top - thick), (wa, ya, z_top - thick),
                     (wa, yb, z_top - thick), (-wa, yb, z_top - thick)], [(0, 1, 3, 2)], mat)
            for sx in (-1, 1):
                acc.add([(sx * wa, ya, z_top - thick), (sx * wa, yb, z_top - thick),
                         (sx * wa, yb, z_top), (sx * wa, ya, z_top)], [(0, 1, 2, 3)], mat)


def fit_standable_widest(y0, y1, half_fn, z_top, target_half=1.5, min_len=2.2, inset=0.12):
    """在平台跨内找能容纳 target_half 半宽的最宽连续 y 窗，生成内缩站面 box（顶面 0.5 档由 z_top 保证）。"""
    ys = [y0 + (y1 - y0) * k / 40 for k in range(41)]
    ok = [half_fn(y) - inset >= target_half for y in ys]
    best, cur = None, None
    for k, good in enumerate(ok + [False]):
        if good:
            cur = [k, k] if cur is None else [cur[0], k]
        else:
            if cur and (best is None or (cur[1] - cur[0]) > (best[1] - best[0])):
                best = list(cur)
            cur = None
    if best is None or (ys[best[1]] - ys[best[0]]) < min_len:
        best = [0, 40]                                 # 退回全跨（放宽档接管）
    wa, wb = ys[best[0]], ys[best[1]]
    hx = min(half_fn(ya) for ya in (wa + 0.05, wb - 0.05, (wa + wb) * 0.5)) - inset
    return {"c": [0.0, (wa + wb) * 0.5, z_top - 0.7],
            "s": [round(2 * max(hx, 0.6), 3), round(wb - wa - 0.1, 3), 1.4]}


def deck_planks_y(acc, half_fn, y_lo, y_hi, z_top, n_strips, mat_main, mat_wear, mat_dark,
                  rng, gap=0.05, t=0.09, inset=0.06, seg_target=2.4, wear=0.16, missing=0.06):
    """顺船板甲板：板条沿 Y、横向排布，外缘按 half_fn(y) 裁剪，每条独立分段相位=错缝；
    wear 板换浅色（曝晒褪色），missing 挖空露暗色腔底（破洞感）。"""
    ys = [y_lo + (y_hi - y_lo) * k / 48 for k in range(49)]
    x_max = max(half_fn(y) for y in ys)
    pw = (2 * x_max - gap * (n_strips - 1)) / n_strips
    for i in range(n_strips):
        xc = -x_max + gap * 0.5 + pw * 0.5 + i * (pw + gap)
        valid = [abs(xc) + pw * 0.5 <= half_fn(y) - inset for y in ys]
        best, cur = (0, 0), None
        for k, ok in enumerate(valid + [False]):
            if ok:
                cur = [k, k] if cur is None else [cur[0], k]
            else:
                if cur and (cur[1] - cur[0]) > (best[1] - best[0]):
                    best = tuple(cur)
                cur = None
        a, b = ys[best[0]], ys[best[1]]
        if b - a < 0.45:
            continue
        nseg = max(1, int(round((b - a) / seg_target)))
        seg = (b - a) / nseg
        phase = rng.uniform(0.0, seg)
        joints = [a]
        j = a + phase
        while j < b - 0.25:
            joints.append(j)
            j += seg
        joints.append(b)
        for s0, s1 in zip(joints, joints[1:]):
            if s1 - s0 < 0.18:
                continue
            r = rng.random()
            if r > 1.0 - missing:
                ax_box(acc, (xc, (s0 + s1) * 0.5, z_top - 0.24), (pw * 0.92, (s1 - s0) * 0.96, 0.06), mat_dark)
                continue
            ax_box(acc, (xc, (s0 + s1) * 0.5, z_top - t * 0.5), (pw, s1 - s0, t),
                   mat_wear if r < wear else mat_main)


def hull_strakes(acc, hull, zs, mat, stations=18, proud=0.03, half_t=0.05):
    """壳板缝条：沿船壳外表面若干高度的窄暗条带，打破大面空白（样板腰带条纹的低配残破版）。"""
    for zz in zs:
        prev = {1: None, -1: None}
        for s in range(stations):
            y = hull.y_lo + (hull.y_hi - hull.y_lo) * s / (stations - 1)
            hw = hull.half_at_world_z(y, zz) + proud
            for side in (1, -1):
                p = (side * hw, y, zz)
                if prev[side] is not None:
                    a, b = prev[side], p
                    acc.add([(a[0], a[1], zz - half_t), (a[0], a[1], zz + half_t),
                             (b[0], b[1], zz + half_t), (b[0], b[1], zz - half_t)],
                            [(0, 1, 2, 3)], mat, smooth=True)
                prev[side] = p


def hull_breach(acc, center, side, w, h, mat_dark, mat_mid, mat_iron, rng):
    """舷侧破洞：壁面凸出的焦黑破口框 + 深暗腔 + 周缘撕裂板 + 弯出铁肋（锈蚀 Kit_Iron）。
    center.x 给壳面内 0.25 处，框体半厚 0.36 → 外缘凸出壁面 ~0.1，任意角度可读。"""
    cx, cy, cz = center
    xo = side * 0.22
    ax_box(acc, (cx - xo, cy, cz), (0.5, w, h), mat_dark)                      # 深暗腔
    ax_box(acc, (cx + side * 0.11, cy, cz), (0.36, w + 0.28, h + 0.24), mat_dark)  # 凸出破口框
    edge = []
    for k in range(7):
        edge.append((cx + side * 0.28, cy - w * 0.55 + w * 1.1 * k / 6.0, cz + h * 0.5))
    torn_edge(acc, edge, mat_dark, rng, 8, span=w / 6.0, hmin=0.1, hmax=0.3, thick=0.07)
    for zz in (cz - h * 0.28, cz + h * 0.3):
        p0 = (cx - xo * 1.6, cy - w * 0.3, zz)
        p1 = (cx + side * 0.6, cy + w * 0.18, zz + 0.1 * (1 if zz > cz else -1))
        ax_tube(acc, p0, p1, 0.045, 0.032, 6, mat_iron)
    flap_plank(acc, (cx + side * 0.12, cy + rng.uniform(-w * 0.4, w * 0.4), cz + h * 0.46),
               (side, rng.uniform(-0.6, 0.6)), rng.uniform(0.25, 0.42), mat_dark, rng)


# ============================================================================
# 材质（槽名=Unity 换装键；hex/roughness 只出自 ST.SLOTS）
# ============================================================================

METALLIC_SLOTS = ('Kit_Iron', 'Kit_Brass')


def make_materials(names, emission=None):
    out = []
    for name in names:
        spec = ST.slot(name)
        mat = bpy.data.materials.get(name)     # 跨资产同名复用，防 Blender 加 .001 后缀破坏换装键
        if mat is None:
            mat = bpy.data.materials.new(name)
            mat.use_nodes = True
            bsdf = mat.node_tree.nodes['Principled BSDF']
            bsdf.inputs['Base Color'].default_value = srgb_hex(spec['hex'])
            bsdf.inputs['Roughness'].default_value = spec['roughness']
            bsdf.inputs['Metallic'].default_value = 1.0 if name in METALLIC_SLOTS else 0.0
            if emission and name in emission:
                try:   # 预览微亮（灯室玻璃）；Unity 侧按槽名重建材质，不受此影响
                    bsdf.inputs['Emission Color'].default_value = srgb_hex(spec['hex'])
                    bsdf.inputs['Emission Strength'].default_value = float(emission[name])
                except Exception:
                    pass
        out.append((name, mat))
    return out


# ============================================================================
# ① WreckBowHalf 搁浅艏半截
# ============================================================================

def build_wreck_bow(acc, M):
    rng = random.Random(RNG_SEED + 1)
    WL, WM, WD, RP, IR = (M['Kit_WoodLight'], M['Kit_WoodMid'], M['Kit_WoodDark'],
                          M['Kit_Rope'], M['Kit_Iron'])
    tilt = math.tan(math.radians(BOW_TILT_DEG))

    def hw_fn(t):     # t=0 艏柱(-Y) → t=1 断面(+Y)：艏柱端收尖，t>0.25 全宽（巨舰钝艏，保站面宽度）
        if t >= 0.25:
            return 1.0
        u = (0.25 - t) / 0.25
        return 1.0 - 0.95 * (u ** 2.0)

    def keel_fn(y):
        return BOW_KEEL_CUT + (BOW_KEEL_STEM - BOW_KEEL_CUT) * (BOW_LEN * 0.5 - y) / BOW_LEN

    def deck_fn(y):
        return BOW_DECK_CUT + (BOW_DECK_STEM - BOW_DECK_CUT) * (BOW_LEN * 0.5 - y) / BOW_LEN

    hull = WreckHull(-7.0, 7.0, BOW_HALF_BEAM, hw_fn, keel_fn, deck_fn, BOW_RAIL,
                     tilt_fn=lambda y: -y * tilt)
    hull.loft(acc, BOW_STATIONS, WD, WM)
    hull_strakes(acc, hull, (0.1, 0.5, 0.9), WD)           # 壳板缝条（破船水渍线）
    hull.end_face(acc, 6.95, 0.10, WD)                     # 断裂封面（微外倾）

    # 断裂面撕裂结构：外露肋骨 + 断口撕板 + 外翻板
    for rx in (-1.6, -0.8, 0.0, 0.8, 1.6):
        ax_box(acc, (rx, 6.88, 0.15), (0.16, 0.22, 3.1), WD)
    cut_pts = [(-2.6, 6.98), (-1.3, 6.98), (0.0, 6.98), (1.3, 6.98), (2.6, 6.98)]
    torn_edge(acc, [(x, y) for x, y in cut_pts], WD, rng, 12, span=0.8, hmin=0.2, hmax=0.85)
    flap_plank(acc, (-2.2, 6.9, 0.9), (-1, 0.25), 1.1, WM, rng)
    flap_plank(acc, (1.9, 6.9, 0.6), (1, -0.2), 0.9, WL, rng)
    flap_plank(acc, (0.3, 6.9, 1.7), (0, -1), 0.8, WM, rng)

    # 三段甲板平台阶（顶面 0.5 档；边缘藏进舷墙）
    slab_half = {}
    for (y0, y1, z_top) in BOW_STEPS:
        hull.platform_slab(acc, y0, y1, z_top, 0.4, WM)
        half_fn = lambda y, zt=z_top: hull.half_at_world_z(y, zt, inset=0.20)
        slab_half[(y0, y1, z_top)] = half_fn
        n_strips = 7 if (y1 - y0) > 4.0 else 6
        deck_planks_y(acc, half_fn, y0 + 0.08, y1 - 0.08, z_top, n_strips, WM, WL, WD, rng,
                      wear=0.18, missing=0.05)
    # 台阶立面（0.5 升差）+ 缘口撕板
    for (y_a, z_a, y_b, z_b) in ((2.2, 0.5, 2.2, 1.0), (-2.4, 1.0, -2.4, 1.5)):
        w = hull.half_at_world_z(y_a + 0.01, z_b, inset=0.2)
        ax_box(acc, (0, y_a, (z_a + z_b) * 0.5), (w * 2, 0.3, z_b - z_a), WM)
        torn_edge(acc, [(-w * 0.9, y_a - 0.16), (0.0, y_a - 0.16), (w * 0.9, y_a - 0.16)],
                  WD, rng, 5, span=0.5, hmin=0.1, hmax=0.3)

    # 艏柱 + 断裂下垂的艏斜桅
    stem = [(0, -6.4, keel_fn(-6.4) + tilt * 6.4 + 0.05), (0, -7.0, 1.6), (0, -7.05, 2.4)]
    for a, b in zip(stem, stem[1:]):
        ax_tube(acc, a, b, 0.17, 0.13, 8, WD)
    sp_base = (0, -6.2, deck_fn(-6.2) + tilt * 6.2 + 0.16)
    sp_mid = (0, sp_base[1] - 2.1 * math.cos(math.radians(22)),
              sp_base[2] + 2.1 * math.sin(math.radians(22)))
    ax_tube(acc, sp_base, sp_mid, 0.15, 0.11, 8, WM)
    for k in range(3):                                     # 断口撕裂箍
        ax_box_r(acc, (math.cos(k * 2.1) * 0.1, sp_mid[1] + 0.08 * k, sp_mid[2] + 0.05 * k),
                 (0.07, 0.26, 0.34), WD, rot=(0.3 * k, 0.2 * k, 0))
    sp_tip = (0, sp_mid[1] - 1.7 * math.cos(math.radians(34)),
              sp_mid[2] - 1.7 * math.sin(math.radians(34)))
    ax_tube(acc, sp_mid, sp_tip, 0.1, 0.05, 8, WM)         # 断桅下垂段
    sag_rope(acc, sp_tip, (0, -5.6, 1.52), 0.5, 0.024, RP, segs=5, end_fray=True)

    # 舷侧破洞 ×2（中心取壳面内 0.25，框体凸出可读）+ 舯部焦黑区（Kit_WoodDark 深化）
    hull_breach(acc, (-(hull.half_at_world_z(3.1, 0.25) - 0.25), 3.1, 0.25), -1, 1.5, 1.1, WD, WM, IR, rng)
    hull_breach(acc, (hull.half_at_world_z(-1.5, 0.1) - 0.25, -1.5, 0.1), 1, 1.7, 1.2, WD, WM, IR, rng)
    for k in range(7):                                     # 焦黑甲板块
        ax_box(acc, (1.05 + (k % 3) * 0.52 - 0.5, -2.0 + (k // 3) * 0.55, 0.955), (0.46, 0.48, 0.09), WD)
    ax_box(acc, (1.2, -1.7, 1.35), (0.3, 0.3, 0.7), WD)    # 烧断桩头
    ax_box(acc, (1.2, -1.7, 1.62), (0.42, 0.42, 0.1), IR)  # 焦铁箍

    # 舷墙帽（断栏：每舷留 2 段）
    hull.bulwark_caps(acc, [(-6.2, -3.4), (-1.6, 2.6)], 0.065, WM)

    # 艏楼残骸小件：系缆桩 + 盘缆
    for bx in (-1.55, 1.55):
        ax_tube(acc, (bx, 5.6, 0.5), (bx, 5.6, 1.02), 0.13, 0.115, 8, WD)
        ax_tube(acc, (bx, 5.6, 1.02), (bx, 5.6, 1.08), 0.15, 0.14, 8, WD)
    rope_ring(acc, -1.55, 5.6, 0.86, 0.17, 0.045, RP)
    rope_coil(acc, -1.35, 0.6, 1.0, 0.42, RP)
    rope_coil(acc, 1.1, 4.4, 0.5, 0.34, RP)

    # 站面 manifest（每级台阶取最宽可容窗；艏段窄台 → 放宽档）
    boxes = []
    for (y0, y1, z_top) in BOW_STEPS:
        half_fn = slab_half[(y0, y1, z_top)]
        boxes.append(fit_standable_widest(y0, y1, half_fn, z_top))
    return boxes


# ============================================================================
# ② WreckSternHalf 搁浅艉半截
# ============================================================================

def build_wreck_stern(acc, M):
    rng = random.Random(RNG_SEED + 2)
    WL, WM, WD, RP, IR, BR = (M['Kit_WoodLight'], M['Kit_WoodMid'], M['Kit_WoodDark'],
                              M['Kit_Rope'], M['Kit_Iron'], M['Kit_Brass'])
    tilt = math.tan(math.radians(STN_TILT_DEG))

    def hw_fn(t):      # 断面全宽 → 中段微鼓 → 艉封板收 0.78
        v = 1.0 + 0.05 * math.sin(math.pi * min(t * 1.25, 1.0))
        if t > 0.72:
            u = (t - 0.72) / 0.28
            v -= 0.27 * (u ** 1.7)
        return v

    def keel_fn(y):
        return STN_KEEL_TRANSOM + (STN_KEEL_CUT - STN_KEEL_TRANSOM) * (STN_LEN * 0.5 - y) / STN_LEN

    def deck_fn(y):
        return STN_DECK_CUT + (STN_DECK_TRANSOM - STN_DECK_CUT) * (y + STN_LEN * 0.5) / STN_LEN

    hull = WreckHull(-6.0, 6.0, STN_HALF_BEAM, hw_fn, keel_fn, deck_fn, STN_RAIL,
                     tilt_fn=lambda y: y * tilt)
    hull.loft(acc, STN_STATIONS, WD, WM)
    hull_strakes(acc, hull, (0.1, 0.55, 1.0), WD)          # 壳板缝条
    hull.end_face(acc, -5.95, -0.12, WD)                   # 断裂封面

    # 断裂面：外露肋骨 + 撕板 + 外翻板
    for rx in (-1.5, -0.75, 0.0, 0.75, 1.5):
        ax_box(acc, (rx, -5.88, 0.1), (0.15, 0.2, 2.8), WD)
    torn_edge(acc, [(-2.4, -5.98), (-1.2, -5.98), (0.0, -5.98), (1.2, -5.98), (2.4, -5.98)],
              WD, rng, 11, span=0.75, hmin=0.2, hmax=0.8)
    flap_plank(acc, (2.1, -5.9, 0.8), (1, 0.2), 1.0, WM, rng)
    flap_plank(acc, (-1.6, -5.9, 1.5), (-1, -0.3), 0.85, WL, rng)

    # 主甲板平台阶 +0.5
    y0, y1, z_top = STN_MAIN_STEP
    hull.platform_slab(acc, y0, y1, z_top, 0.4, WM)
    main_half = lambda y: hull.half_at_world_z(y, z_top, inset=0.20)
    deck_planks_y(acc, main_half, y0 + 0.08, y1 - 0.08, z_top, 7, WM, WL, WD, rng, wear=0.2, missing=0.07)

    # 艉楼平台阶 +1.0（楼体：侧壁 + 门洞 + 舷窗）
    cy0, cy1, cz_top = STN_CASTLE_STEP
    hull.platform_slab(acc, cy0, cy1, cz_top, 0.45, WM, inset=0.0, steps=4)
    for sx in (-1, 1):
        ax_box(acc, (sx * (STN_CASTLE_HALF - 0.06), (cy0 + cy1) * 0.5, cz_top + 0.26),
               (0.12, cy1 - cy0 - 0.3, 0.52), WM)
        for wy in (3.1, 4.3):                              # 楼体舷窗（暗洞+铁框）
            ax_box(acc, (sx * STN_CASTLE_HALF, wy, cz_top + 0.3), (0.1, 0.34, 0.3), WD)
            ax_box(acc, (sx * (STN_CASTLE_HALF - 0.02), wy, cz_top + 0.3), (0.08, 0.42, 0.38), IR)
    ax_box(acc, (0.55, cy0 + 0.06, cz_top + 0.27), (0.7, 0.14, 0.54), WD)   # 楼门暗框
    ax_box(acc, (0.55, cy0 + 0.06, cz_top + 0.27), (0.82, 0.1, 0.66), IR)
    deck_planks_y(acc, lambda y: STN_CASTLE_HALF - 0.14, cy0 + 0.1, cy1 - 0.1, cz_top, 5,
                  WM, WL, WD, rng, wear=0.15, missing=0.0)
    # 艉楼顶矮栏（断 1 段）
    for sx in (-1, 1):
        for by in (2.4, 3.4, 4.4, 5.3):
            ax_tube(acc, (sx * (STN_CASTLE_HALF - 0.16), by, cz_top), (sx * (STN_CASTLE_HALF - 0.16), by, cz_top + 0.3),
                    0.03, 0.026, 6, WM)
        ax_tube(acc, (sx * (STN_CASTLE_HALF - 0.16), 2.4, cz_top + 0.3),
                (sx * (STN_CASTLE_HALF - 0.16), 5.3, cz_top + 0.3), 0.032, 0.032, 6, WM)

    # 艉封板 + 三拱窗（暗底 + 黄铜拱框 + 窗台）+ 铜饰带 + 破艉灯
    hull.end_face(acc, 5.95, 0.18, WM)
    for wx in (-0.85, 0.0, 0.85):
        wz = 1.35
        ax_box(acc, (wx, 6.05, wz), (0.56, 0.08, 0.62), WD)
        for k in range(5):                                 # 拱框：半圆弧段
            a = math.pi * (k + 0.5) / 5.0
            ax_box_r(acc, (wx + 0.3 * math.cos(a), 6.1, wz + 0.31 + 0.3 * math.sin(a) - 0.31),
                     (0.09, 0.05, 0.09), BR, rot=(0, -a + math.pi * 0.5, 0))
        ax_box(acc, (wx, 6.1, wz - 0.38), (0.68, 0.06, 0.09), BR)
    ax_box(acc, (0, 6.08, 0.98), (3.1, 0.06, 0.1), BR)     # 窗下黄铜联系梁
    for zz in (0.7, 1.1):                                  # 铜饰带（收短内嵌，防在收分曲线上戳出）
        for sx in (-1, 1):
            x0 = hull.half_at_world_z(4.5, zz) - 0.04
            ax_box(acc, (sx * x0, 4.5, zz), (0.05, 1.4, 0.09), BR)
    ax_box(acc, (0, 6.14, 2.6), (0.5, 0.06, 0.5), WD)      # 破艉灯残座
    ax_box(acc, (0, 6.2, 2.72), (0.2, 0.14, 0.14), BR)

    # 舵残件（挂艉柱、断口斜裂、锈铁箍）
    ax_tube(acc, (0, 6.25, -1.5), (0, 6.25, 0.55), 0.11, 0.09, 8, WD)
    ax_box_r(acc, (0.12, 6.5, -0.95), (0.1, 0.46, 1.05), WD, rot=(0, math.radians(14), 0))
    ax_box_r(acc, (0.2, 6.62, -0.28), (0.08, 0.3, 0.3), WD, rot=(0, math.radians(26), 0))
    for sz in (-1.25, -0.7):
        ax_box_r(acc, (0.1, 6.48, sz), (0.16, 0.5, 0.07), IR, rot=(0, math.radians(14), 0))
    ax_box(acc, (0, 6.25, 0.3), (0.3, 0.24, 0.12), IR)     # 舵承锈箍

    # 破洞 ×2（凸出框版）+ 断口焦黑区 + 烧断桅桩
    hull_breach(acc, (-(hull.half_at_world_z(0.6, 0.15) - 0.25), 0.6, 0.15), -1, 1.4, 1.0, WD, WM, IR, rng)
    hull_breach(acc, (hull.half_at_world_z(-2.8, 0.0) - 0.25, -2.8, 0.0), 1, 1.8, 1.2, WD, WM, IR, rng)
    for k in range(6):
        ax_box(acc, (-1.15 + (k % 3) * 0.5 - 0.5, -4.6 + (k // 3) * 0.52, 0.455), (0.44, 0.46, 0.09), WD)
    ax_tube(acc, (0.4, -4.2, 0.5), (0.4, -4.2, 1.35), 0.17, 0.15, 8, WD)    # 烧断桅桩
    for k in range(3):
        ax_box_r(acc, (0.4 + math.cos(k * 2.0) * 0.16, -4.2 + math.sin(k * 2.0) * 0.16, 1.4),
                 (0.07, 0.2, 0.22), WD, rot=(0.4 * k, 0, 0.5 * k))
    ax_tube(acc, (0.4, -4.2, 0.95), (0.4, -4.2, 1.02), 0.2, 0.19, 8, IR)

    # 舷墙帽（断栏）
    hull.bulwark_caps(acc, [(-5.2, -2.6), (-0.8, 2.2), (3.2, 5.4)], 0.06, WM)

    # 盘缆 + 系缆桩
    rope_coil(acc, 1.6, 3.9, 1.0, 0.4, RP)
    for bx, by in ((-1.6, -4.6), (1.6, 0.6)):
        ax_tube(acc, (bx, by, 0.5), (bx, by, 1.0), 0.12, 0.11, 8, WD)
        ax_tube(acc, (bx, by, 1.0), (bx, by, 1.06), 0.14, 0.13, 8, WD)
    rope_ring(acc, 1.6, 0.6, 0.84, 0.16, 0.04, RP)

    # 站面 manifest（主甲板/艉楼各 1；艉楼半宽 2.2 → 箱 4.0 过 ST）
    m_y0, m_y1, m_z = STN_MAIN_STEP
    c_y0, c_y1, c_z = STN_CASTLE_STEP
    boxes = [
        {"c": [0.0, (m_y0 + m_y1) * 0.5, m_z - 0.7], "s": [4.4, round(m_y1 - m_y0 - 0.4, 3), 1.4]},
        {"c": [0.0, (c_y0 + c_y1) * 0.5, c_z - 0.7], "s": [4.0, round(c_y1 - c_y0 - 0.05, 3), 1.4]},
    ]
    return boxes


# ============================================================================
# ③ MastBridge 断桅桥
# ============================================================================

def build_mast_bridge(acc, M):
    rng = random.Random(RNG_SEED + 3)
    WL, WM, WD, SL, RP, IR = (M['Kit_WoodLight'], M['Kit_WoodMid'], M['Kit_WoodDark'],
                              M['Kit_Sail'], M['Kit_Rope'], M['Kit_Iron'])
    half = MB_SPAN * 0.5
    gap0, gap1 = -MB_GAP * 0.5, MB_GAP * 0.5
    trunk_top = MB_WALK_TOP - 0.085
    trunk_axis = trunk_top - MB_TRUNK_R

    # 端部礁墩支撑（入水）+ 垫木
    for sy in (-1, 1):
        by = sy * (half - 0.4)
        boulder(acc, (0.4 * sy, by, 0.15), 1.5, 1.1, 1.1, M['Kit_RockMid'], rng)
        boulder(acc, (-0.9 * sy, by - 0.8 * sy, -0.25), 0.9, 0.8, 0.8, M['Kit_RockDark'], rng)
        boulder(acc, (1.2 * sy, by + 0.9 * sy, -0.55), 0.8, 0.7, 0.7, M['Kit_RockDark'], rng)
        ax_box(acc, (0.0, by - sy * 0.9, 1.06), (1.3, 0.5, 0.24), WD)
        ax_box(acc, (0.3 * sy, by - sy * 0.2, 1.02), (1.0, 0.4, 0.2), WD)
        ax_tube(acc, (0.4 * sy, by, MB_SUPPORT_BOT), (0.4 * sy, by, -0.4), 0.16, 0.14, 8, WD)

    # 倒伏主桅（两段，中段断口；断端撕裂箍 + 远端撕头）
    ax_tube(acc, (0, -half - 0.5, trunk_axis), (0, gap0 - 0.25, trunk_axis), MB_TRUNK_R, MB_TRUNK_R * 0.97, 10, WM)
    ax_tube(acc, (0, gap1 + 0.25, trunk_axis), (0, half + 0.5, trunk_axis), MB_TRUNK_R * 0.96, MB_TRUNK_R * 0.9, 10, WM)
    for k in range(4):                                     # 远端断头撕茬
        a = k * 1.57 + 0.4
        ax_box_r(acc, (math.cos(a) * 0.4, -half - 0.42, trunk_axis + math.sin(a) * 0.4),
                 (0.08, 0.34, 0.3), WD, rot=(a * 0.6, 0, a))
    for gy in (gap0 - 0.25, gap1 + 0.25):
        for k in range(4):
            a = k * 1.57
            ax_box_r(acc, (math.cos(a) * 0.42, gy + (0.12 if gy > 0 else -0.12), trunk_axis + math.sin(a) * 0.42),
                     (0.09, 0.3, 0.34), WD, rot=(a * 0.5, 0, a))
    # 断口内撕出的桅芯
    ax_tube(acc, (0, gap0 - 0.3, trunk_axis), (0, gap0 + 0.15, trunk_axis - 0.1), 0.2, 0.1, 7, WD)
    ax_tube(acc, (0, gap1 + 0.3, trunk_axis), (0, gap1 - 0.15, trunk_axis + 0.08), 0.2, 0.12, 7, WD)
    # 走道板（顶 +1.5；两段，断口处缘板撕裂）
    walks = [(-half, gap0), (gap1, half)]
    for w0, w1 in walks:
        n = 4
        pw = (MB_WALK_W - 0.05 * (n - 1)) / n
        for i in range(n):
            xc = -MB_WALK_W * 0.5 + 0.025 + pw * 0.5 + i * (pw + 0.05)
            seg = (w1 - w0) / 3.0
            phase = (i * 0.61) % seg
            js = [w0 + phase + k * seg for k in range(4) if w0 + phase + k * seg < w1 - 0.2]
            js = [w0] + js + [w1]
            for s0, s1 in zip(js, js[1:]):
                if s1 - s0 < 0.15:
                    continue
                r = rng.random()
                ax_box(acc, (xc, (s0 + s1) * 0.5, MB_WALK_TOP - 0.045), (pw, s1 - s0, 0.09),
                       WL if r < 0.22 else WM)
    for gx in (gap0, gap1):                                # 断口缘撕板（踩断的板头立在内缘上）
        torn_edge(acc, [(-0.75, gx, MB_WALK_TOP - 0.09), (0.0, gx, MB_WALK_TOP - 0.09),
                        (0.75, gx, MB_WALK_TOP - 0.09)], WD, rng, 5, span=0.42,
                  hmin=0.08, hmax=0.24, thick=0.07)
    ax_box_r(acc, (0.1, gap0 - 0.9, MB_WALK_TOP - 0.35), (0.24, 0.8, 0.05), WD, rot=(0, 0.5, 0.15))  # 半垂断板

    # 桅身铁箍（锈，贴壁细箍）
    for gy in (-6.2, -3.1, 1.9, 5.2):
        ax_tube(acc, (0, gy, trunk_axis - 0.075), (0, gy, trunk_axis + 0.075),
                MB_TRUNK_R + 0.018, MB_TRUNK_R + 0.018, 10, IR, caps=False)

    # 瞭望斗：断桅桩（入水、微倾）+ 撕裂甲板残片平台 3×3 @ +4.5
    nx, ny = MB_NEST_POS
    lean = math.radians(4)
    stub_bot = (nx + 0.12, ny + 0.05, -1.0)
    stub_top = (nx, ny, MB_NEST_TOP - 0.3)
    ax_tube(acc, stub_bot, stub_top, 0.21, 0.16, 9, WD)
    for gz in (-0.2, 1.4, 2.9):
        zz = stub_bot[2] + (stub_top[2] - stub_bot[2]) * (gz + 1.0) / 5.8
        ax_tube(acc, (nx - 0.02, ny, zz - 0.04), (nx - 0.02, ny, zz + 0.04), 0.24, 0.24, 9, IR, caps=False)
    boulder(acc, (nx + 0.3, ny + 0.2, -0.6), 0.9, 0.9, 0.75, M['Kit_RockDark'], rng)   # 桩根礁石
    boulder(acc, (nx - 0.7, ny - 0.5, -0.9), 0.7, 0.6, 0.6, M['Kit_RockMid'], rng)

    nz0x, nz0y = nx - MB_NEST_SIZE * 0.5, ny - MB_NEST_SIZE * 0.5
    slab_t = 0.3
    acc_box_nest = []
    n = 5
    pw = (MB_NEST_SIZE - 0.04 * (n - 1)) / n
    for i in range(n):                                     # 残片甲板（撕裂边）
        yc = nz0y + 0.02 + pw * 0.5 + i * (pw + 0.04)
        seg = MB_NEST_SIZE / 2.0
        phase = (i * 0.83) % seg
        js = [nz0x + phase + k * seg for k in range(3) if nz0x + phase + k * seg < nz0x + MB_NEST_SIZE - 0.2]
        js = [nz0x] + js + [nz0x + MB_NEST_SIZE]
        for s0, s1 in zip(js, js[1:]):
            if s1 - s0 < 0.12:
                continue
            ax_box(acc, ((s0 + s1) * 0.5, yc, MB_NEST_TOP - slab_t * 0.5), (s1 - s0, pw, slab_t),
                   WL if rng.random() < 0.3 else WM)
    torn_edge(acc, [(nz0x, nz0y), (nz0x + MB_NEST_SIZE, nz0y)], WD, rng, 5, span=0.5, hmin=0.08, hmax=0.24, thick=0.07)
    torn_edge(acc, [(nz0x, nz0y + MB_NEST_SIZE), (nz0x + MB_NEST_SIZE, nz0y + MB_NEST_SIZE)],
              WD, rng, 4, span=0.5, hmin=0.06, hmax=0.2, thick=0.07)
    # 半圈瞭望斗残栏（原鸦巢残骸）
    cxc, cyc = nz0x + 0.6, nz0y + 0.6
    for k in range(9):
        a = math.pi * 0.15 + math.pi * 1.2 * k / 8.0
        p0 = (cxc + 0.5 * math.cos(a), cyc + 0.5 * math.sin(a), MB_NEST_TOP)
        p1 = (cxc + 0.5 * math.cos(a), cyc + 0.5 * math.sin(a), MB_NEST_TOP + 0.42)
        ax_tube(acc, p0, p1, 0.035, 0.03, 6, WM)
    rail_ring(acc, cxc, cyc, 0.5, MB_NEST_TOP + 0.42, 0.028, WM, sides=9)

    # 绳梯（走道→平台，底端落在走道面上）+ 垂残梯
    rope_ladder(acc, (0.42, 0.95, MB_WALK_TOP), (0.52, 0.78, MB_NEST_TOP - 0.06),
                0.52, 7, RP, WL)
    rope_ladder(acc, (nz0x + 0.05, ny + 0.4, MB_NEST_TOP - 0.5), (nz0x - 0.15, ny + 0.5, MB_WALK_TOP - 2.1),
                0.4, 3, RP, WD, bow=0.3)

    # 帆布垂条（平台缘 + 桅身缠挂团）
    hanging_cloth(acc, (nz0x + MB_NEST_SIZE, ny + 0.9, MB_NEST_TOP - 0.02),
                  (nz0x + MB_NEST_SIZE, ny + 0.1, MB_NEST_TOP - 0.02), 1.7, SL, sway=0.2)
    hanging_cloth(acc, (0.5, gap1 + 1.6, trunk_top + 0.02), (0.5, gap1 + 0.7, trunk_top + 0.02),
                  1.2, SL, sway=0.14)
    ax_box_r(acc, (0.14, -4.9, trunk_top + 0.03), (0.36, 0.52, 0.1), SL, rot=(0, 0.2, 0.4))
    ax_box_r(acc, (0.18, -4.72, trunk_top + 0.1), (0.22, 0.3, 0.09), SL, rot=(0.35, 0.1, 0.9))

    # 瞭望台→桅两端 张紧残索 + 断滑轮
    sag_rope(acc, (nx, ny, MB_NEST_TOP + 0.1), (0, -half + 0.6, trunk_top - 0.1), 0.8, 0.022, RP, segs=7)
    sag_rope(acc, (nz0x + MB_NEST_SIZE - 0.2, ny + MB_NEST_SIZE - 0.3, MB_NEST_TOP),
             (0.4, half - 0.8, trunk_top - 0.1), 0.9, 0.02, RP, segs=7, end_fray=True)
    ax_box(acc, (0.15, 3.6, trunk_top - 0.75), (0.12, 0.2, 0.26), IR)      # 断滑轮壳

    # 站面 manifest：走道两段（1.6 宽 → 放宽档；顶 +1.5）+ 瞭望斗平台 3×3（顶 +4.5，返工单补入）
    boxes = []
    for w0, w1 in walks:
        boxes.append({"c": [0.0, (w0 + w1) * 0.5, MB_WALK_TOP - 0.7],
                      "s": [MB_WALK_W, round(w1 - w0 - 0.1, 3), 1.4]})
    boxes.append({"c": [MB_NEST_POS[0], MB_NEST_POS[1], MB_NEST_TOP - 0.7],
                  "s": [MB_NEST_SIZE, MB_NEST_SIZE, 1.4]})
    return boxes


# ============================================================================
# ④ LighthouseTower 灯塔
# ============================================================================

def build_lighthouse(acc, M):
    rng = random.Random(RNG_SEED + 4)
    RL, RM, RD, SA, WS, IR, EM, WM = (M['Kit_RockLight'], M['Kit_RockMid'], M['Kit_RockDark'],
                                      M['Kit_SandMid'], M['Kit_WetSand'], M['Kit_Iron'],
                                      M['Kit_Ember'], M['Kit_WoodMid'])
    a_island = LH_ISLAND * 0.5

    # 基座岛台：圆角方形岩台（顶面严格 +0.5 平整），裙边入水
    sides = 26
    top_ring, bot_ring = [], []
    for k in range(sides):
        a = 2 * math.pi * k / sides
        ca, sa = math.cos(a), math.sin(a)
        n = 3.2                                            # 超椭圆方度
        px = math.copysign(abs(ca) ** (2.0 / n), ca) * a_island
        py = math.copysign(abs(sa) ** (2.0 / n), sa) * a_island
        top_ring.append((px, py, LH_ISLAND_TOP))
        bot_ring.append((px * 1.22, py * 1.22, LH_ISLAND_BOT))
    acc.add(top_ring, [tuple(range(sides))], RL)
    for k in range(sides):
        k2 = (k + 1) % sides
        acc.add([bot_ring[k], bot_ring[k2], top_ring[k2], top_ring[k]], [(0, 1, 2, 3)], RM, smooth=True)
    # 水线湿沙环带 + 沙嘴
    for k in range(sides):
        k2 = (k + 1) % sides
        b0, b1 = bot_ring[k], bot_ring[k2]
        mid = ((b0[0] + b1[0]) * 0.5 * 1.02, (b0[1] + b1[1]) * 0.5 * 1.02, 0.0)
        acc.add([b0, b1, mid], [(0, 1, 2)], WS)
    for k in range(6):                                     # 沙嘴（+X 侧）
        u = k / 5.0
        acc.add([(a_island * 0.92, -2.0 + u * 4.0, LH_ISLAND_TOP + 0.012),
                 (a_island * (1.34 - 0.4 * u), -1.5 + u * 3.0, 0.02 - 0.25 * math.sin(math.pi * u)),
                 (a_island * (1.34 - 0.4 * u), -0.4 + u * 3.0, 0.02 - 0.25 * math.sin(math.pi * u)),
                 (a_island * 0.92, -1.0 + u * 4.0, LH_ISLAND_TOP + 0.012)], [(0, 1, 2, 3)], SA)
    # 礁石裙边（半埋，只露顶）
    for k in range(14):
        a = 2 * math.pi * k / 14.0 + 0.2
        rr = a_island * (1.22 + 0.14 * (k % 3))
        boulder(acc, (rr * math.cos(a), rr * math.sin(a), -0.72 + (k % 3) * 0.14),
                0.85 + (k % 4) * 0.2, 0.8 + (k % 3) * 0.16, 0.75 + (k % 2) * 0.22,
                RD if k % 3 else RM, rng)
    for k in range(6):                                     # 台缘小砾
        a = 2 * math.pi * k / 6.0 + 0.7
        boulder(acc, (a_island * 0.86 * math.cos(a), a_island * 0.86 * math.sin(a), LH_ISLAND_TOP - 0.06),
                0.24, 0.22, 0.14, RL, rng, smooth=False)
    # 台面岩板 + 系船铁环（顶面细节，保平顶）
    for k in range(9):
        a = 2 * math.pi * k / 9.0 + 0.35
        rr = a_island * (0.34 + 0.12 * (k % 3))
        boulder(acc, (rr * math.cos(a), rr * math.sin(a) * 1.2, LH_ISLAND_TOP + 0.012),
                0.9 + (k % 3) * 0.25, 0.7 + (k % 2) * 0.3, 0.05, RM, rng, sides=6, rings=2,
                smooth=False)
    rope_ring(acc, a_island * 0.62, -a_island * 0.55, LH_ISLAND_TOP + 0.03, 0.16, 0.04, IR, ma=10, mi=5)
    rope_ring(acc, -a_island * 0.5, a_island * 0.62, LH_ISLAND_TOP + 0.03, 0.16, 0.04, IR, ma=10, mi=5)

    # 塔身锥柱 + 砌石环带 + 门 + 窗
    ax_tube(acc, (0, 0, LH_ISLAND_TOP - 0.06), (0, 0, LH_TOWER_TOP), LH_TOWER_R0, LH_TOWER_R1, 14, RL)
    for bz in (1.5, 2.6, 3.3, 5.0, 6.2):                   # 砌石环带（错层；3.3 避开 4.0 环台）
        f = (bz - LH_ISLAND_TOP) / (LH_TOWER_TOP - LH_ISLAND_TOP)
        rr = LH_TOWER_R0 + (LH_TOWER_R1 - LH_TOWER_R0) * f
        ax_tube(acc, (0, 0, bz - 0.055), (0, 0, bz + 0.055), rr + 0.05, rr + 0.045, 14, RM, caps=False)
    ax_box(acc, (0, -(LH_TOWER_R0 - 0.12), 1.12), (0.72, 0.3, 1.25), RD)     # 门洞
    for k in range(3):                                     # 门拱
        aa = math.pi * (k + 0.5) / 3.0
        ax_box_r(acc, (0.3 * math.cos(aa), -(LH_TOWER_R0 - 0.12), 1.72 + 0.3 * math.sin(aa) - 0.3),
                 (0.14, 0.28, 0.12), RD, rot=(0, -aa + math.pi * 0.5, 0))
    ax_box(acc, (0, -(LH_TOWER_R0 - 0.05), 0.62), (0.9, 0.16, 0.1), IR)      # 门槛铁
    for wz in (2.9, 5.5):                                  # 窗
        f = (wz - LH_ISLAND_TOP) / (LH_TOWER_TOP - LH_ISLAND_TOP)
        rrw = LH_TOWER_R0 + (LH_TOWER_R1 - LH_TOWER_R0) * f
        ax_box(acc, (0, -rrw + 0.06, wz), (0.3, 0.22, 0.44), RD)
        ax_box(acc, (0, -rrw + 0.1, wz), (0.38, 0.14, 0.52), IR)

    # 中段环台 Ø4 @ +3.5（木面 + 铁托 + 铁栏）
    f = (LH_RING_TOP - LH_ISLAND_TOP) / (LH_TOWER_TOP - LH_ISLAND_TOP)
    rt = LH_TOWER_R0 + (LH_TOWER_R1 - LH_TOWER_R0) * f
    ring_slab(acc, 0, 0, LH_RING_TOP, rt - 0.1, LH_RING_RO, 0.22, 14, WM)
    for k in range(8):
        aa = 2 * math.pi * k / 8.0
        ax_box_r(acc, (1.55 * math.cos(aa), 1.55 * math.sin(aa), LH_RING_TOP - 0.24),
                 (0.16, 0.1, 0.3), IR, rot=(0, 0.5, aa))
    for k in range(10):
        aa = 2 * math.pi * k / 10.0
        px, py = (LH_RING_RO - 0.14) * math.cos(aa), (LH_RING_RO - 0.14) * math.sin(aa)
        ax_tube(acc, (px, py, LH_RING_TOP), (px, py, LH_RING_TOP + 0.52), 0.032, 0.028, 6, IR)
    rail_ring(acc, 0, 0, LH_RING_RO - 0.14, LH_RING_TOP + 0.52, 0.03, IR, sides=10)
    rail_ring(acc, 0, 0, LH_RING_RO - 0.14, LH_RING_TOP + 0.3, 0.024, IR, sides=10)

    # 灯室平台 Ø3.5 @ +7.5 + 玻璃条带（Kit_Ember 微亮）+ 铁顶锥帽
    ring_slab(acc, 0, 0, LH_LAMP_TOP, LH_TOWER_R1 - 0.08, LH_LAMP_RO, 0.2, 12, WM)
    for k in range(8):
        aa = 2 * math.pi * k / 8.0
        ax_box_r(acc, (1.48 * math.cos(aa), 1.48 * math.sin(aa), LH_LAMP_TOP - 0.2),
                 (0.15, 0.09, 0.26), IR, rot=(0, 0.55, aa))
    for k in range(8):                                     # 玻璃条带（弧面细分）
        a0 = 2 * math.pi * k / 8.0
        a1 = 2 * math.pi * (k + 1) / 8.0
        pts = []
        for j in range(3):
            zz = LH_LAMP_TOP + (LH_LAMP_GLASS_TOP - LH_LAMP_TOP) * j / 2.0
            r_ = 1.06 + 0.02 * math.sin(math.pi * j / 2.0)
            pts.append([(r_ * math.cos(a0), r_ * math.sin(a0), zz),
                        (r_ * math.cos(a1), r_ * math.sin(a1), zz)])
        ax_grid(acc, pts, 3, 2, EM, smooth=True)
    for k in range(8):
        aa = 2 * math.pi * (k + 0.5) / 8.0
        ax_tube(acc, (1.08 * math.cos(aa), 1.08 * math.sin(aa), LH_LAMP_TOP),
                (1.05 * math.cos(aa), 1.05 * math.sin(aa), LH_LAMP_GLASS_TOP), 0.045, 0.04, 6, IR)
    ring_slab(acc, 0, 0, LH_LAMP_GLASS_TOP + 0.04, 0.4, 1.28, 0.1, 12, IR)
    ax_tube(acc, (0, 0, LH_LAMP_GLASS_TOP + 0.06), (0, 0, LH_CAP_TOP - 0.35), 1.3, 0.2, 12, IR)
    ax_tube(acc, (0, 0, LH_CAP_TOP - 0.35), (0, 0, LH_CAP_TOP - 0.1), 0.09, 0.05, 8, IR)
    ax_tube(acc, (0, 0, LH_LAMP_GLASS_TOP + 0.08), (0, 0, LH_LAMP_GLASS_TOP + 0.24), 0.16, 0.12, 8, IR)
    ax_tube(acc, (0, 0, LH_CAP_TOP - 0.1), (0, 0, LH_CAP_TOP), 0.035, 0.012, 6, IR)

    # 站面 manifest：岛台 / 环台 / 灯室平台（灯室 3.5 < 4 → 放宽档）
    return [
        {"c": [0.0, 0.0, LH_ISLAND_TOP - 0.7], "s": [LH_ISLAND - 0.6, LH_ISLAND - 0.6, 1.4]},
        {"c": [0.0, 0.0, LH_RING_TOP - 0.5], "s": [LH_RING_RO * 2, LH_RING_RO * 2, 1.0]},
        {"c": [0.0, 0.0, LH_LAMP_TOP - 0.5], "s": [LH_LAMP_RO * 2, LH_LAMP_RO * 2, 1.0]},
    ]


# ============================================================================
# ⑤ PierLong 长栈桥
# ============================================================================

def build_pier_long(acc, M):
    rng = random.Random(RNG_SEED + 5)
    WL, WM, WD, RP = M['Kit_WoodLight'], M['Kit_WoodMid'], M['Kit_WoodDark'], M['Kit_Rope']
    hl, hw2 = PL_LEN * 0.5, PL_WIDTH * 0.5
    T = 0.09

    # 错缝面板：宽向 7 板条，每条独立分段相位
    deck_planks_y(acc, lambda y: hw2, -hl + 0.05, hl - 0.05, PL_TOP, 7, WM, WL, WD, rng,
                  gap=0.05, t=T, seg_target=3.0, wear=0.16, missing=0.045)
    # 边梁 + 托梁
    for sx in (-1, 1):
        ax_box(acc, (sx * (hw2 - 0.08), 0, PL_TOP - T - 0.085), (0.16, PL_LEN, 0.17), WD)
    for by in (-hl + 0.15, -hl * 0.5, 0.0, hl * 0.5, hl - 0.15):
        ax_box(acc, (0, by, PL_TOP - T - 0.25), (PL_WIDTH - 0.12, 0.15, 0.15), WD)
    # 6 立柱入水（3 对）+ 柱间底撑 + 斜撑
    for py in (-6.0, 0.0, 6.0):
        for sx in (-1, 1):
            px = sx * (hw2 - 0.32)
            ax_tube(acc, (px, py, PL_PILE_BOT), (px, py, PL_TOP - T - 0.3), PL_PILE_R, PL_PILE_R * 0.9, 9, WD)
            ax_tube(acc, (px, py, PL_TOP - T - 0.3), (px, py, PL_TOP - T - 0.22), PL_PILE_R * 1.14, PL_PILE_R * 1.05, 9, WD)
        ax_box(acc, (0, py, -0.62), (hw2 * 2 - 0.7, 0.15, 0.18), WD)
        for sx in (-1, 1):
            px = sx * (hw2 - 0.32)
            ax_tube(acc, (px, py - 0.55, -0.5), (px * 0.62, py, PL_TOP - T - 0.3), 0.075, 0.06, 6, WD)
    # 两侧系船柱（交互布位）+ 绳圈
    for i, by in enumerate((-6.6, -3.0, 1.2, 4.6, 7.2)):
        sx = -1 if i % 2 == 0 else 1
        bx = sx * (hw2 - 0.28)
        ax_tube(acc, (bx, by, PL_TOP), (bx, by, PL_TOP + PL_BOLL_TOP), 0.12, 0.105, 8, WD)
        ax_tube(acc, (bx, by, PL_TOP + PL_BOLL_TOP), (bx, by, PL_TOP + PL_BOLL_TOP + 0.05),
                0.15, 0.14, 8, WD)
        if i in (0, 2, 3):
            rope_ring(acc, bx, by, PL_TOP + 0.28, 0.15, 0.042, RP)
    # 一侧栏索（南缘）+ 一段断垂
    rx = -hw2 + 0.14
    posts = [-7.7, -5.5, -3.3, -1.1, 1.1, 3.3, 5.5, 7.7]
    for i, py in enumerate(posts):
        broken = (1.1 <= py <= 3.3) and i % 2 == 1
        top = PL_TOP + (0.12 if broken else 0.55)
        ax_tube(acc, (rx, py, PL_TOP), (rx, py, top), 0.045, 0.04, 6, WD)
    for i in range(len(posts) - 1):
        a, b = posts[i], posts[i + 1]
        if -1.1 <= a <= 1.1:                               # 断垂段：索从断桩垂到系缆桩
            sag_rope(acc, (rx, -1.1, PL_TOP + 0.53), (rx, 1.1, PL_TOP + 0.1), 0.75, 0.026, RP,
                     segs=6, end_fray=True)
            sag_rope(acc, (rx, -1.1, PL_TOP + 0.34), (rx, 1.1, PL_TOP + 0.06), 0.55, 0.022, RP, segs=6)
            continue
        ax_tube(acc, (rx, a, PL_TOP + 0.52), (rx, b, PL_TOP + 0.52), 0.026, 0.026, 6, RP)
        ax_tube(acc, (rx, a, PL_TOP + 0.3), (rx, b, PL_TOP + 0.3), 0.022, 0.022, 6, RP)
    # 面上小件：盘缆 + 腐板暗斑
    rope_coil(acc, 1.15, -1.8, PL_TOP, 0.4, RP)
    rope_coil(acc, -1.2, 5.9, PL_TOP, 0.32, RP)
    for cy in (-4.2, 2.6):
        ax_box(acc, (-0.7, cy, PL_TOP - T * 0.5), (1.0, 1.4, T), WD)

    return [{"c": [0.0, 0.0, PL_TOP - 0.7], "s": [PL_WIDTH, PL_LEN - 0.4, 1.4]}]


# ============================================================================
# ⑥ PierHead 桥头平台
# ============================================================================

def build_pier_head(acc, M):
    rng = random.Random(RNG_SEED + 6)
    WL, WM, WD, RP = M['Kit_WoodLight'], M['Kit_WoodMid'], M['Kit_WoodDark'], M['Kit_Rope']
    hs = PH_SIZE * 0.5
    T = 0.09

    # 平台错缝板（宽向 11 条）
    deck_planks_y(acc, lambda y: hs, -hs + 0.05, hs - 0.05, PH_TOP, 11, WM, WL, WD, rng,
                  gap=0.05, t=T, seg_target=2.8, wear=0.18, missing=0.05)
    # 边梁 + 托梁
    for sx in (-1, 1):
        ax_box(acc, (sx * (hs - 0.08), 0, PH_TOP - T - 0.085), (0.16, PH_SIZE, 0.17), WD)
        ax_box(acc, (0, sx * (hs - 0.08), PH_TOP - T - 0.085), (PH_SIZE, 0.16, 0.17), WD)
    for bv in (-hs * 0.5, 0.0, hs * 0.5):
        ax_box(acc, (0, bv, PH_TOP - T - 0.25), (PH_SIZE - 0.12, 0.15, 0.15), WD)
        ax_box(acc, (bv, 0, PH_TOP - T - 0.25), (0.15, PH_SIZE - 0.12, 0.15), WD)
    # 转角段（+Y 侧伸出 3 宽引桥；板沿搭进主桥边梁下，交接处共梁）
    x0, x1, y0, y1 = PH_STUB
    deck_planks_y(acc, lambda y: (x1 - x0) * 0.5, y0 - 0.3, y1, PH_TOP, 5, WM, WL, WD, rng,
                  gap=0.05, t=T, seg_target=1.6, wear=0.15, missing=0.0)
    cxm = (x0 + x1) * 0.5
    ax_box(acc, (cxm, (y0 + y1) * 0.5, PH_TOP - T - 0.085), (x1 - x0, y1 - y0 + 0.3, 0.17), WD)
    for px in (x0 + 0.3, x1 - 0.3):
        ax_tube(acc, (px, y1 - 0.3, PH_PILE_BOT), (px, y1 - 0.3, PH_TOP - T - 0.3), 0.15, 0.13, 9, WD)
    ax_box(acc, (cxm, y1 - 0.6, -0.58), (x1 - x0 - 0.6, 0.14, 0.16), WD)
    # 平台立柱 8 根 + 底撑
    pile_pts = [(-hs + 0.4, -hs + 0.4), (0.0, -hs + 0.35), (hs - 0.4, -hs + 0.4),
                (-hs + 0.4, 0.0), (hs - 0.4, 0.0),
                (-hs + 0.4, hs - 0.4), (hs - 0.4, hs - 0.4), (0.9, hs - 0.4), (-2.5, hs - 0.4)]
    for px, py in pile_pts:
        ax_tube(acc, (px, py, PH_PILE_BOT), (px, py, PH_TOP - T - 0.3), 0.17, 0.155, 9, WD)
        ax_tube(acc, (px, py, PH_TOP - T - 0.3), (px, py, PH_TOP - T - 0.22), 0.19, 0.18, 9, WD)
    for py in (-hs * 0.5, hs * 0.5):
        ax_box(acc, (0, py, -0.62), (hs * 2 - 0.8, 0.14, 0.17), WD)
    for sx in (-1, 1):                                     # 角部斜撑（起点收进投影内）
        for sy in (-1, 1):
            px, py = sx * (hs - 0.4), sy * (hs - 0.4)
            ax_tube(acc, (px, py - sy * 0.45, -0.5), (px * 0.66, py, PH_TOP - T - 0.3), 0.075, 0.06, 6, WD)
    # 双系船柱（转角处一对）+ 大绳圈 + 引缆
    for dx in (-0.32, 0.32):
        bx = cxm + dx
        ax_tube(acc, (bx, y0 + 0.55, PH_TOP), (bx, y0 + 0.55, PH_TOP + PH_BOLL_TOP), 0.14, 0.12, 8, WD)
        ax_tube(acc, (bx, y0 + 0.55, PH_TOP + PH_BOLL_TOP), (bx, y0 + 0.55, PH_TOP + PH_BOLL_TOP + 0.06),
                0.17, 0.16, 8, WD)
    rope_ring(acc, cxm, y0 + 0.55, PH_TOP + 0.3, 0.3, 0.05, RP)
    rope_ring(acc, cxm, y0 + 0.55, PH_TOP + 0.42, 0.24, 0.045, RP)
    sag_rope(acc, (cxm + 0.32, y0 + 0.55, PH_TOP + 0.4), (cxm + 2.6, y0 + 1.7, PH_TOP + 0.04),
             0.1, 0.03, RP, segs=5)
    # 堆缆圈（3 叠 1 散）+ 散缆拖尾
    rope_coil(acc, 2.5, 2.9, PH_TOP, 0.52, RP, layers=3)
    rope_coil(acc, -2.6, -2.3, PH_TOP, 0.4, RP, layers=1)
    sag_rope(acc, (2.1, 2.3, PH_TOP + 0.1), (1.2, 1.5, PH_TOP + 0.06), 0.02, 0.035, RP, segs=3)
    sag_rope(acc, (1.2, 1.5, PH_TOP + 0.06), (0.4, 0.9, PH_TOP + 0.06), 0.02, 0.035, RP, segs=3)
    # 缺板暗腔 + 角栏残柱
    ax_box(acc, (1.6, -1.9, PH_TOP - 0.24), (1.3, 1.0, 0.06), WD)
    for px, py in ((-hs + 0.2, 1.8), (-hs + 0.2, 3.4)):
        ax_tube(acc, (px, py, PH_TOP), (px, py, PH_TOP + 0.5), 0.045, 0.04, 6, WD)
    ax_tube(acc, (-hs + 0.2, 1.8, PH_TOP + 0.48), (-hs + 0.2, 3.4, PH_TOP + 0.48), 0.026, 0.026, 6, RP)

    return [{"c": [0.0, 0.0, PH_TOP - 0.7], "s": [PH_SIZE - 0.3, PH_SIZE - 0.3, 1.4]}]


# ============================================================================
# ⑦ BuoyRing 系船浮标
# ============================================================================

def build_buoy(acc, M):
    """系船浮标：水线宽肩浮体（棱面锥台）+ 铁铃架 + 横杆挂铃 + 顶环；原点 z=0 = 吃水接触面。"""
    rng = random.Random(RNG_SEED + 7)
    IR, RP, RD = M['Kit_Iron'], M['Kit_Rope'], M['Kit_RockDark']
    e = Euler((math.radians(BU_ROLL[0]), math.radians(BU_ROLL[1]), 0.0), 'XYZ')

    def R(pt):
        p = e.to_matrix() @ Vector(pt)
        return tuple(p)

    # 浮体：水线鼓肩（0→0.42 微扩）+ 上锥台（0.42→1.0 收顶）+ 顶盘；flat 棱面
    ax_tube(acc, R((0, 0, -0.04)), R((0, 0, 0.42)), BU_BODY_R0, BU_BODY_R0 * 1.06, 12, IR, smooth=False)
    ax_tube(acc, R((0, 0, 0.42)), R((0, 0, 1.0)), BU_BODY_R0 * 1.04, BU_BODY_R1, 12, IR, smooth=False)
    ax_tube(acc, R((0, 0, 1.0)), R((0, 0, 1.07)), 0.3, 0.28, 10, IR)
    ax_tube(acc, R((0, 0, 0.44)), R((0, 0, 0.54)), BU_BODY_R0 * 1.07, BU_BODY_R0 * 1.05, 12, RD, caps=False)  # 锈带
    # 铃架：3 立柱自顶盘内倾 + 双横箍 + 顶板
    top = BU_FRAME_TOP
    for k in range(3):
        a = 2 * math.pi * k / 3.0 + 0.5
        p0 = R((0.26 * math.cos(a), 0.26 * math.sin(a), 1.05))
        p1 = R((0.12 * math.cos(a), 0.12 * math.sin(a), top))
        ax_tube(acc, p0, p1, 0.032, 0.026, 6, IR)
    for zz, rr in ((1.42, 0.225), (1.86, 0.175)):
        rail_ring(acc, 0, 0, rr, zz, 0.018, IR, sides=9)
    ax_tube(acc, R((0, 0, top - 0.04)), R((0, 0, top)), 0.15, 0.13, 8, IR)
    # 横杆 + 小钟（棱面，哑光）
    ax_tube(acc, R((-0.16, 0, 1.66)), R((0.16, 0, 1.66)), 0.022, 0.022, 6, IR)
    ax_tube(acc, R((0, 0, 1.6)), R((0, 0, 1.44)), 0.02, 0.09, 8, IR, smooth=False)   # 钟身
    ax_tube(acc, R((0, 0, 1.44)), R((0, 0, 1.47)), 0.095, 0.08, 8, IR, smooth=False)  # 钟唇
    ax_tube(acc, R((0, 0, 1.36)), R((0, 0, 1.42)), 0.014, 0.014, 5, IR)               # 钟舌
    # 顶环 + 外垂绳（绕架外缘）+ 腰绳
    rope_ring(acc, *R((0, 0, top + 0.06))[:2], R((0, 0, top + 0.06))[2], 0.085, 0.026, RP, ma=10, mi=5)
    sag_rope(acc, R((0.14, 0.05, top - 0.02)), R((0.52, 0.16, 0.34)), 0.24, 0.022, RP, segs=5, end_fray=True)
    rope_ring(acc, *R((0, 0, 0.3))[:2], R((0, 0, 0.3))[2], BU_BODY_R0 * 1.1, 0.03, RP, ma=14, mi=5)
    return []


# ============================================================================
# ⑧ RowboatBeached 搁浅小艇
# ============================================================================

RB_SECTION = [(0.00, 0.00), (0.55, 0.16), (0.94, 0.55), (1.00, 0.82)]


def build_rowboat(acc, M):
    rng = random.Random(RNG_SEED + 8)
    WL, WM, WD, RP, SA = (M['Kit_WoodLight'], M['Kit_WoodMid'], M['Kit_WoodDark'],
                          M['Kit_Rope'], M['Kit_SandMid'])
    roll = math.radians(RB_ROLL)
    trim = math.radians(RB_TRIM)
    hl = RB_LEN * 0.5

    def hw_fn(t):
        """绝对半宽（米）：艏尖 t=0 → 舯 0.7 → 艉封板 0.5（t=1 不归零，封板直立）。"""
        if t < 0.5:
            return 0.05 + (RB_HALF_BEAM - 0.05) * math.sin(math.pi * t) ** 0.8
        return RB_HALF_BEAM - (RB_HALF_BEAM - 0.5) * ((t - 0.5) / 0.5) ** 1.6

    def z_off(y):
        return (y + hl) * math.tan(-trim)                # 尾坐头翘微纵倾

    def list_x(x, y, z):
        e = Euler((roll, 0.0, 0.0), 'XYZ').to_matrix()
        p = e @ Vector((x, y, z + z_off(y)))
        return tuple(p)

    st = 9
    for s in range(st - 1):                              # 艏(-Y) → 艉(+Y)
        y0 = -hl + RB_LEN * s / (st - 1)
        y1 = -hl + RB_LEN * (s + 1) / (st - 1)
        t0, t1 = (y0 + hl) / RB_LEN, (y1 + hl) / RB_LEN
        for strake in range(len(RB_SECTION) - 1):
            la0, up0 = RB_SECTION[strake]
            la1, up1 = RB_SECTION[strake + 1]
            shrink = 0.985 if strake % 2 else 1.0        # 板缝阶梯感
            for side in (1, -1):
                quad = [list_x(side * la0 * hw_fn(t0) * shrink, y0, up0 * RB_DEPTH),
                        list_x(side * la1 * hw_fn(t0) * shrink, y0, up1 * RB_DEPTH),
                        list_x(side * la1 * hw_fn(t1) * shrink, y1, up1 * RB_DEPTH),
                        list_x(side * la0 * hw_fn(t1) * shrink, y1, up0 * RB_DEPTH)]
                acc.add(quad, [(0, 1, 2, 3)], WM, smooth=True)
    # 艉封板 + 缘板帽 + 两道横坐板 + 舱底板
    tE = 1.0
    pts = [(side * la * hw_fn(tE) * 0.98, up * RB_DEPTH) for la, up in RB_SECTION for side in ((1,) if la == 0 else (1, -1))]
    poly = [(p[0], hl, p[1] + z_off(hl)) for p in pts]
    acc.add(poly, [tuple(range(len(poly)))], WD)
    for side in (1, -1):
        prev = None
        for s in range(st):
            y = -hl + RB_LEN * s / (st - 1)
            t = (y + hl) / RB_LEN
            p = list_x(side * hw_fn(t) * RB_SECTION[-1][0], y, RB_SECTION[-1][1] * RB_DEPTH)
            if prev:
                ax_tube(acc, prev, p, 0.035, 0.035, 6, WL)
            prev = p
    for by in (-0.7, 0.6):
        t = (by + hl) / RB_LEN
        w = hw_fn(t) * 0.92
        ax_box(acc, list_x(0, by, 0.5), (w * 2, 0.22, 0.05), WL)
    ax_box(acc, list_x(0, 0.0, 0.1)[:2] + (0.1,), (0.5, RB_LEN * 0.72, 0.04), WD)
    # 艏缆绳环 + 拖缆
    rp = list_x(0, -hl + 0.12, 0.5)
    rope_ring(acc, rp[0], rp[1], rp[2], 0.08, 0.026, RP, ma=10, mi=5)
    sag_rope(acc, rp, (rp[0] + 0.7, rp[1] - 1.2, 0.03), 0.05, 0.024, RP, segs=4, end_fray=True)
    # 破缝：右舷（暗腔 + 短撕板翘出 + 缘口撕头，避免大块平滑"帆板"）
    cy0, cy1 = -0.35, 0.55
    ax_box(acc, list_x(0.46, (cy0 + cy1) * 0.5, 0.28), (0.14, cy1 - cy0, 0.4), WD)
    for k, (ya, yb) in enumerate(((cy0, (cy0 + cy1) * 0.5), ((cy0 + cy1) * 0.5, cy1))):
        tm = (ya + yb) * 0.5
        hw_m = hw_fn((tm + hl) / RB_LEN)
        out = 0.1 + 0.06 * k
        ax_box_r(acc, list_x(hw_m * 0.92 + out, tm, 0.42), (0.1, yb - ya, 0.3), WL,
                 rot=(roll * 0.4, 0, 0.25 * (1 if k else -1)))
    torn_edge(acc, [list_x(0.5, cy0, 0.6), list_x(0.56, 0.1, 0.62), list_x(0.5, cy1, 0.6)],
              WD, rng, 6, span=0.3, hmin=0.05, hmax=0.18, thick=0.05)
    # 艏部缺板（左舷，腔体藏在壳内）
    ax_box(acc, list_x(-0.26, -1.5, 0.28), (0.2, 0.44, 0.28), WD)
    torn_edge(acc, [list_x(-0.3, -1.72, 0.4), list_x(-0.32, -1.28, 0.44)], WD, rng, 4,
              span=0.2, hmin=0.04, hmax=0.13, thick=0.045)
    # 沙堆（坐底 + 破缝侧垫沙；圆钝不规则）
    boulder(acc, list_x(0.5, 0.2, -0.26), 0.78, 0.95, 0.34, SA, rng, sides=8, rings=3)
    boulder(acc, list_x(-0.45, -0.9, -0.3), 0.55, 0.65, 0.26, SA, rng, sides=7, rings=3)
    # 一桨插沙（桨顶搭在舷缘）
    ob = list_x(-0.98, 1.62, 0.0)
    ot = list_x(-0.5, 1.05, 0.62)
    ax_tube(acc, ob, ot, 0.032, 0.026, 6, WL)
    ax_box_r(acc, tuple((Vector(ob) + Vector(ot)) * 0.5 - Vector((0, 0, 0.52))), (0.14, 0.4, 0.03),
             WM, rot=(math.radians(24), math.radians(-14), 0))
    boulder(acc, (ob[0], ob[1], 0.02), 0.17, 0.17, 0.1, SA, rng, sides=6, rings=2, smooth=False)
    return []


# ============================================================================
# ⑨ AnchorMonument 锚碑
# ============================================================================

def build_anchor(acc, M):
    rng = random.Random(RNG_SEED + 9)
    IR, RM, RD, RL, BR, RP = (M['Kit_Iron'], M['Kit_RockMid'], M['Kit_RockDark'],
                              M['Kit_RockLight'], M['Kit_Brass'], M['Kit_Rope'])
    px, py, pz = AM_PLINTH

    # 石墩：底阶 + 主墩 + 顶沿 + 凿痕 + 铜牌
    ax_box(acc, (0, 0, pz * 0.09), (px + 0.3, py + 0.3, pz * 0.18), RD)
    ax_box(acc, (0, 0, pz * 0.55), (px, py, pz * 0.72), RM)
    ax_box(acc, (0, 0, pz - 0.035), (px - 0.24, py - 0.24, 0.07), RL)
    for k in range(4):                                     # 凿痕（凹条）
        ax_box(acc, (-px * 0.25 + k * px * 0.17, -py * 0.5 - 0.005, pz * 0.5), (0.05, 0.03, 0.3), RD)
    ax_box(acc, (0, -py * 0.5 - 0.01, pz * 0.66), (0.46, 0.02, 0.32), RD)    # 铜牌暗衬
    ax_box(acc, (0, -py * 0.5 - 0.028, pz * 0.66), (0.4, 0.024, 0.26), BR)   # 铜牌（凸出）
    # 巨锚（Iron）：冠坐墩顶，双臂上扬外张 + 错爪，锚杆 + 横杆 + 环
    ax_tube(acc, (0, 0, pz - 0.05), (0, 0, AM_SHANK_TOP), 0.115, 0.082, 8, IR)
    for side in (-1, 1):
        arm = [(side * 0.06, 0.0, pz + 0.02), (side * 0.52, side * 0.06, pz + 0.12),
               (side * 0.9, side * 0.1, pz + 0.55), (side * 1.05, side * 0.08, pz + 1.05)]
        for a, b in zip(arm, arm[1:]):
            ax_tube(acc, a, b, 0.085 if a is arm[0] else 0.07, 0.066, 7, IR)
        fx, fy, fz = arm[-1]
        ax_box_r(acc, (fx + side * 0.1, fy, fz + 0.14), (0.2, 0.09, 0.3), IR,
                 rot=(0, side * math.radians(42), side * math.radians(14)))   # 错爪（顺臂外撇）
        ax_box_r(acc, (fx + side * 0.18, fy, fz + 0.26), (0.13, 0.07, 0.16), IR,
                 rot=(0, side * math.radians(50), side * math.radians(20)))
    ax_box_r(acc, (0, 0, AM_STOCK_Z), (1.7, 0.09, 0.09), IR, rot=(0, 0, 0))
    for sx in (-1, 1):                                     # 横杆头短粗盘
        ax_tube(acc, (sx * 0.85, 0, AM_STOCK_Z), (sx * 0.93, 0, AM_STOCK_Z), 0.1, 0.08, 8, IR)
    rope_ring(acc, 0, 0, AM_RING_C, 0.19, 0.05, IR, ma=12, mi=6, axis='y')   # 锚环
    ax_tube(acc, (0, 0, AM_RING_C - 0.24), (0, 0, AM_RING_C - 0.02), 0.085, 0.082, 8, IR)  # 环颈
    # 缆绳：过环垂下，一段系墩、一段断头垂摆
    sag_rope(acc, (-0.16, 0, AM_RING_C + 0.08), (0.2, 0.02, AM_RING_C + 0.1), 0.16, 0.03, RP, segs=4)
    sag_rope(acc, (0.14, 0.02, AM_RING_C), (0.55, 0.05, pz + 0.02), 0.12, 0.028, RP, segs=5)
    ax_box(acc, (0.58, 0.05, pz + 0.05), (0.16, 0.14, 0.12), RP)             # 挽桩疙瘩
    sag_rope(acc, (-0.14, 0, AM_RING_C), (-0.7, 0.12, pz + 0.5), 0.2, 0.026, RP, segs=5, end_fray=True)
    # 墩脚砾石 + 盘缆
    for k in range(4):
        a = 2 * math.pi * k / 4.0 + 0.6
        boulder(acc, ((px * 0.72) * math.cos(a), (py * 0.72) * math.sin(a), 0.07),
                0.14, 0.13, 0.09, RL, rng, sides=6, rings=2, smooth=False)
    rope_coil(acc, 0.95, -0.75, 0.02, 0.3, RP, layers=2)
    return []


# ============================================================================
# 合网格 / 统计 / 导出 / 预览渲染
# ============================================================================

def join_to_object(acc, obj_name, mats):
    """MeshAcc → 单网格对象（只挂实际用到的槽）。"""
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
            pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    for old in used:
        me.materials.append(mats[old][1])
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def deselect_all():
    for o in bpy.context.scene.objects:
        o.select_set(False)


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
    scene.view_settings.view_transform = ST.PREVIEW_VIEW_TRANSFORM   # Standard，防 AgX 洗色
    scene.view_settings.look = 'None'
    log('render engine=Cycles device=%s samples=%d' % (gpu or 'CPU', SAMPLES))


def setup_preview_stage(scene, floor_z):
    """中性灰世界 + 灰地板 + 三灯（暖 key / 冷 fill / 暖 rim）——样板 build_scene_kit.py 同款。"""
    world = bpy.data.worlds.new('marine_world')
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs[0].default_value = srgb_hex(ST.PREVIEW_BG_HEX)
    bg.inputs[1].default_value = 1.0

    floor_mat = bpy.data.materials.new('marine_floor')
    floor_mat.use_nodes = True
    fb = floor_mat.node_tree.nodes['Principled BSDF']
    fb.inputs['Base Color'].default_value = srgb_hex(ST.PREVIEW_GROUND_HEX)
    fb.inputs['Roughness'].default_value = 0.9
    floor = bpy.data.objects.new('marine_floor', bpy.data.meshes.new('marine_floor'))
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
        di = Vector(target) - Vector(loc)
        o.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()

    area('key', 10.0, (-12, -14, 12), 2600, (1.0, 0.94, 0.84), (0, 0, 2))
    area('fill', 12.0, (14, -4, 7), 900, (0.62, 0.75, 1.0), (0, 0, 2))
    area('rim', 8.0, (3, 16, 10), 1800, (1.0, 0.9, 0.75), (0, 0, 2.5))


def render_views(scene, obj, name, out_dir, views):
    deselect_all()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for vname, loc, target in views:
        cam_data = bpy.data.cameras.new('marine_cam')
        cam_data.lens = 50
        cam_data.clip_end = 300
        cam = bpy.data.objects.new('marine_cam', cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        di = Vector(target) - Vector(loc)
        cam.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        path = os.path.join(out_dir, '%s-%s.jpg' % (name, vname))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        log('rendered %s (%d bytes)' % (path, os.path.getsize(path)))
        bpy.data.objects.remove(cam, do_unlink=True)


# ============================================================================
# 资产注册表 + 主流程
# ============================================================================

def idx_map(names):
    return {n: i for i, n in enumerate(names)}


ASSETS = [
    ('WreckBowHalf', build_wreck_bow,
     ['Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark', 'Kit_Rope', 'Kit_Iron'], None,
     [('front34', (-13.5, -15.0, 8.5), (0, -1.5, 1.2)), ('side', (-21.0, 0.5, 5.0), (0, 0, 1.0))]),
    ('WreckSternHalf', build_wreck_stern,
     ['Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark', 'Kit_Rope', 'Kit_Iron', 'Kit_Brass'], None,
     [('front34', (-12.0, -13.5, 7.5), (0, -1.0, 0.9)), ('side', (-19.0, 0.5, 4.6), (0, 0, 0.8))]),
    ('MastBridge', build_mast_bridge,
     ['Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark', 'Kit_Sail', 'Kit_Rope', 'Kit_Iron',
      'Kit_RockMid', 'Kit_RockDark'], None,
     [('front34', (-10.5, -13.5, 6.0), (0, -0.5, 1.8)), ('side', (-18.5, 0.5, 4.4), (0, 0, 2.0))]),
    ('LighthouseTower', build_lighthouse,
     ['Kit_RockLight', 'Kit_RockMid', 'Kit_RockDark', 'Kit_SandMid', 'Kit_WetSand', 'Kit_Iron',
      'Kit_Ember', 'Kit_WoodMid'], {'Kit_Ember': 1.8},
     [('front34', (-14.5, -16.0, 8.5), (0, 0, 3.4)), ('side', (-24.0, 1.0, 7.5), (0, 0, 3.6))]),
    ('PierLong', build_pier_long,
     ['Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark', 'Kit_Rope'], None,
     [('front34', (-11.0, -16.5, 5.8), (0, -0.5, 0.4)), ('side', (-23.0, 0.5, 3.4), (0, 0, 0.2))]),
    ('PierHead', build_pier_head,
     ['Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark', 'Kit_Rope'], None,
     [('front34', (-10.5, -12.5, 5.6), (0, 0.3, 0.4)), ('side', (-17.0, 0.5, 3.4), (0, 0, 0.3))]),
    ('BuoyRing', build_buoy,
     ['Kit_Iron', 'Kit_Rope', 'Kit_RockDark'], None,
     [('front34', (-2.2, -3.0, 2.4), (0, 0, 1.0)), ('side', (-4.0, 0.3, 1.6), (0, 0, 1.0))]),
    ('RowboatBeached', build_rowboat,
     ['Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark', 'Kit_Rope', 'Kit_SandMid'], None,
     [('front34', (-3.4, -4.2, 2.6), (0, -0.2, 0.25)), ('side', (-6.0, 0.2, 1.5), (0, 0, 0.3))]),
    ('AnchorMonument', build_anchor,
     ['Kit_Iron', 'Kit_RockMid', 'Kit_RockDark', 'Kit_RockLight', 'Kit_Brass', 'Kit_Rope'], None,
     [('front34', (-3.6, -4.4, 3.0), (0, 0, 1.7)), ('side', (-6.4, 0.3, 2.2), (0, 0, 1.7))]),
]


def main():
    parse_args()
    for d in (FBX_DIR, PREVIEW_DIR, WORK_DIR):
        os.makedirs(d, exist_ok=True)

    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    if RENDER:
        setup_render(scene)
        setup_preview_stage(scene, -0.06)

    built = [a for a in ASSETS if ONLY in ('all', a[0])]
    if not built:
        raise SystemExit('marine_kit: --only %r 不在资产清单' % ONLY)
    floor = bpy.data.objects.get('marine_floor')

    for name, build_fn, mat_names, emission, views in built:
        log('== %s ==' % name)
        acc = MeshAcc()
        mats = make_materials(mat_names, emission)
        boxes = build_fn(acc, idx_map(mat_names))
        n_verts, n_polys, n_tris = acc.counts()
        log('%s raw: verts=%d polys=%d tris=%d' % (name, n_verts, n_polys, n_tris))
        obj = join_to_object(acc, name, mats)
        deselect_all()
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        ST.budget_guard(name, [obj], 'marine')            # STAT 行 + marine ≤12000 tri 门禁
        fbx_path = os.path.join(FBX_DIR, name + '.fbx')
        ST.export_fbx([obj], fbx_path)
        standable_write(name, boxes, fbx_path)
        if RENDER and floor is not None:
            lo = min((obj.matrix_world @ Vector(c)).z for c in obj.bound_box)
            floor.location.z = lo - 0.06
            render_views(scene, obj, name, PREVIEW_DIR, views)
        bpy.data.objects.remove(obj, do_unlink=True)      # 清场，防跨资产残留混入 blend 缓存

    if RENDER:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, 'marine_kit_debug.blend'))
    log('done in %.1fs total' % (time.time() - T0))


if __name__ == '__main__':
    main()
