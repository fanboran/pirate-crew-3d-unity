# -*- coding: utf-8 -*-
"""archipelago_kit.py —— M4 地形件 kit（12 件大号地形）无头建模（Blender，纯程序化，零贴图）。

资产清单（FBX 名固定，WorldMapCatalog 按名引用；规格=足印×顶面高度档）：
    AtollArcA        环礁弧段：外 R44 / 内 R32、跨 90°，顶 +0.5，弧上 2 处 +1.0 礁台
    AtollCore        泻湖心岛 14×10 @ +0.5，礁台阶 +1.0 ×2
    TerraceIslandL   梯田岛 48×36，四层 +0.5/+1.5/+2.5/+3.5 错落，侧坡地层感
    TerraceIslandM   梯田岛 24×18，三层 +0.5/+1.5/+2.5
    SeaStackTall     海蚀柱：柱足 ~10，顶盘 8×8 @ +6.0，柱身节理
    SeaStackShort    海蚀柱：顶盘 7×7 @ +2.5
    SandBarL         沙洲 30×8 @ +0.5，贝壳/漂木散布（细节不超顶高）
    ReefStepsA       礁阶：3 块 6×5 板 @ +0.5/+1.0/+1.5，中心距 8（净隙 2u）
    MangroveHummock  红树墩 18×14 @ +1.0，底部根须裙（不站）
    VolcanoRimA      火山缘环：外径 64，缘顶 +3.5/+4.5 两档，~12u 缺口，内坪 +0.5
    TurtleShellIsle  龟甲岛 40×32：甲板 +1.0/+2.0/+3.0/+3.5（脊 12×4.4），四鳍 5×4 @ +1.0，
                     龟首 6×6 @ +2.0，尾 4×4 @ +1.0；六边形鳞纹几何
    SunkenPlaza      沉没广场 28×28 @ +0.5：边缘咬口、石缝分格、中央下沉圆池（池带无碰撞）

【坐标口径（硬指标，docs/M4 §1 / §4.2）】
    Blender 场景：+Z 上，1 单位 = 1 米；原点 = 足印中心、z=0 海平面；水下裙边到 z=-3。
    可站立顶面 Z 一律 0.5 档、面内高低差 ≤1e-3（顶板拼装全部同一 z 常数，天然平直）。
    站面 manifest：<名>.standable.json，box={"c":[x,y,z],"s":[sx,sy,sz],"yaw":度}（Blender 本地系、
    Z-up；yaw 为绕 +Z 逆时针角、可选，消费顺序「先 yaw 后平移 c」；Unity 侧（Z-up→Y-up）对应绕 +Y 同值角）。
    全高碰撞盒（sz = 顶面高 - 裙边底），防止从侧壁穿进岛体。

【风格纪律（docs/美术风格指南.md §2/§3 + style_tokens.py）】
    色值/粗糙度/预算/导出/预览全部取自 style_tokens（单一事实源，本脚本零散写色值）；
    三档明度纪律：每件顶面/侧壁至少出现同色系两档 + 暗衬档；shade_flat 全程（低多边形块面）。

复现（仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/archipelago/archipelago_kit.py -- [--only <名>|all] [--samples N] [--no-render]

产物：
    pirate-crew/Assets/Art/Models/WorldKit/Archipelago/<名>.fbx + <名>.standable.json
    export/worldkit-archipelago/<名>-{front34,side,back}.jpg （1024² q90，Standard 视图变换）
    external/worldkit-archipelago-work/archipelago_debug.blend （调参 GUI 缓存，gitignored）
"""

import math
import os
import random
import sys
import time
import zlib

import bpy
import bmesh
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))          # -> tools/blender/scene/
import style_tokens as ST                          # noqa: E402  单一事实源

# ============================================================================
# 参数区 —— 调参旋钮集中在此（README 按行号引用）
# ============================================================================

ONLY = 'all'            # 'all' | 资产名（如 'AtollArcA'）
SAMPLES = 96            # Cycles 采样数（预览图）
RENDER = True           # False = 只建模导出 FBX + manifest，不出预览图
RES = 1024              # 预览图边长
JPG_QUALITY = 90        # JPEG 质量

SEED0 = 20260917        # 全 kit 随机种子（逐件派生，--only 单件重跑与全量一致）

# ---- 全 kit 通用形态 ----
SKIRT_BOT_Z = -3.0      # 水下裙边底（所有大件统一）
TOP_T = 0.35            # 顶板厚（拼缝立面视深）
CAP_DROP = 0.12         # 衬底盖低于顶板的量（缝隙视深；共面会 z-fight，实测铁律）
STRIP_DROP = 0.02       # 贴地细节条（裂纹/湿斑/余烬纹）低于顶板的量
SIDE_BANDS = 3          # 层间侧壁地层带数
SEG_ARC = 40            # 中型件轮廓段数（闭合轮廓总点数基准）
SEG_ARC_BIG = 56        # 大件（≥40m）轮廓段数
JITTER = 0.22           # 侧壁地层径向抖动幅（米）
SKIRT_SPREAD = 1.08     # 裙边底部外扩系数

# ---- 全 kit 材质槽（顺序即 FBX 槽序候选；色值/粗糙度全部来自 ST.SLOTS）----
KIT_SLOTS = [
    'Kit_SandLight', 'Kit_SandMid', 'Kit_SandDark', 'Kit_WetSand',
    'Kit_GrassLight', 'Kit_GrassMid', 'Kit_GrassDark',
    'Kit_RockLight', 'Kit_RockMid', 'Kit_RockDark',
    'Kit_Coral', 'Kit_WoodDark', 'Kit_Ember', 'Kit_Sail',
]
(M_SANDL, M_SANDM, M_SANDD, M_WETSAND,
 M_GRASSL, M_GRASSM, M_GRASSD,
 M_ROCKL, M_ROCKM, M_ROCKD,
 M_CORAL, M_WOODD, M_EMBER, M_SAIL) = range(len(KIT_SLOTS))

# ---- 逐件参数（足印 × 顶面档；调"形"先动这里）----
ATOLL_ARC = dict(r_out=44.0, r_in=32.0, a0=-135.0, a1=-45.0,   # 凹面朝 -Y（front34 可见）
                 top=0.5, reef_top=1.0, reef_deg=(-118.0, -62.0), n_plates=3)

ATOLL_CORE = dict(hw=7.0, hd=5.0, n_pow=3.2, top=0.5,
                  steps=[(4.4, 3.4, 1.0), (-4.6, -2.8, 1.0)])  # (cx, cy, z_top)

TERRACE_L = dict(layers=[(0.5, 24.0, 18.0, 0.0, 0.0),         # (z_top, hw, hd, cx, cy)
                         (1.5, 16.0, 12.0, -5.0, 3.0),
                         (2.5, 10.0, 7.0, 3.0, -3.5),
                         (3.5, 5.5, 3.8, -1.5, 2.0)],
                 n_pow=3.0)

TERRACE_M = dict(layers=[(0.5, 12.0, 9.0, 0.0, 0.0),
                         (1.5, 7.5, 5.2, -2.8, 1.8),
                         (2.5, 4.0, 2.8, 1.6, -1.2)],
                 n_pow=3.0)

STACK_TALL = dict(r_foot=5.0, r_top=3.6, cap_z=6.0, cap_r=4.0, joints=5)
STACK_SHORT = dict(r_foot=4.2, r_top=3.2, cap_z=2.5, cap_r=3.5, joints=2)

SAND_BAR = dict(hw=15.0, hd=4.0, n_pow=2.4, top=0.5)

REEF_STEPS = dict(plate=(3.0, 2.5),                             # 半足印（6×5）
                  tops=[(-8.0, 0.5), (0.0, 1.0), (8.0, 1.5)])   # (cx, z_top)

MANGROVE = dict(hw=9.0, hd=7.0, n_pow=2.8, top=1.0, roots=22)

VOLCANO = dict(r_out=32.0, r_in=20.0, top_lo=3.5, top_hi=4.5,
               gap_c=-90.0, gap_half=13.3,                      # 缺口中心角/半角（~12u @r26）
               hi_spans=[(100.0, 168.0), (196.0, 252.0), (292.0, 348.0)],  # 避开缺口(256.7~283.3)
               floor_top=0.5, n_pillars=34)

TURTLE = dict(rings=[(1.0, 20.0, 16.0, 0.0, 0.0),               # (z_top, hw, hd, cx, cy)
                     (2.0, 14.0, 10.5, 0.0, -0.6),
                     (3.0, 8.0, 6.0, 0.0, -0.8)],
              spine=(3.5, 6.0, 2.2, 0.0, -0.8),                 # 脊台 12×4.4 @ +3.5
              fins=[(18.5, 8.5, 45.0), (-18.5, 8.5, 135.0),     # (cx, cy, yaw) 5×4 @ +1.0
                    (-18.5, -8.0, 225.0), (18.5, -8.0, 315.0)],
              head=(0.0, -19.5, 2.0),                           # 龟首 6×6 @ +2.0
              tail=(0.0, 18.0, 1.0),                            # 尾 4×4 @ +1.0
              n_pow=2.4)

PLAZA = dict(hw=14.0, hd=14.0, n_pow=4.0, top=0.5,              # 28×28 圆角方
             grid=7, pool_r=4.0, pool_z=-0.55)                  # 池底（不穿底，体到 -3）

# ---- 逐件预览机位微调（基础距离 = 包围盒对角线×1.30；d_mult 按件放大/拉近）----
VIEWS_CFG = {
    'AtollArcA':       dict(d_mult=1.0),
    'AtollCore':       dict(d_mult=1.1),
    'TerraceIslandL':  dict(d_mult=1.0),
    'TerraceIslandM':  dict(d_mult=1.1),
    'SeaStackTall':    dict(d_mult=1.15),
    'SeaStackShort':   dict(d_mult=1.2),
    'SandBarL':        dict(d_mult=1.0),
    'ReefStepsA':      dict(d_mult=1.1),
    'MangroveHummock': dict(d_mult=1.05),
    'VolcanoRimA':     dict(d_mult=1.0),
    'TurtleShellIsle': dict(d_mult=1.0),
    'SunkenPlaza':     dict(d_mult=1.05),
}

# ============================================================================
# 路径 / 日志 / 参数解析
# ============================================================================

ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))   # archipelago -> scene -> blender -> tools -> 仓库根
FBX_DIR = os.path.join(ROOT, 'pirate-crew', 'Assets', 'Art', 'Models', 'WorldKit', 'Archipelago')
PREVIEW_DIR = os.path.join(ROOT, 'export', 'worldkit-archipelago')
WORK_DIR = os.path.join(ROOT, 'external', 'worldkit-archipelago-work')

T0 = time.time()


def log(msg):
    print('[archipelago] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


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
# 几何累积器与图元（MeshAcc / ax_box / ax_tube / ax_grid 照 build_scene_kit.py 样板）
# ============================================================================

class MeshAcc:
    """单件资产的几何累积器。mat 序号 = make_kit_materials 列表下标。"""

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


def ax_box(acc, center, size, mat, smooth=False, rot_z=0.0):
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
    acc.add(pts, faces, mat, smooth)


def ax_tube(acc, p0, p1, r1, r2, sides, mat, smooth=False, caps=True):
    """圆台/圆杆：p0→p1 轴线（flat 为主：低多边形块面纪律）。"""
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
        ring0.append(tuple(c0 + w * r1))
        ring1.append(tuple(c1 + w * r2))
    pts = list(ring0) + list(ring1)
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
    if caps:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range(sides, 2 * sides)))
    acc.add(pts, faces, mat, smooth)


# ============================================================================
# 轮廓库（superellipse / 环弧）与 mound 主体生成器
# ============================================================================

def superellipse_pts(hw, hd, n_pow, count, cx=0.0, cy=0.0, phase=0.0):
    """超椭圆闭合轮廓（CCW）。n_pow=2 椭圆、4~6 圆角矩形、2.4~3 岛屿感。"""
    e = 2.0 / max(1.01, n_pow)
    pts = []
    for i in range(count):
        a = phase + 2.0 * math.pi * i / count
        ca, sa = math.cos(a), math.sin(a)
        pts.append((cx + hw * math.copysign(abs(ca) ** e, ca),
                    cy + hd * math.copysign(abs(sa) ** e, sa)))
    return pts


def annulus_outline(r_out, r_in, a0, a1, n_arc):
    """弧环带闭合轮廓（外弧 a0→a1 + 内弧折返），整体 CCW。角度为度。"""
    a0r, a1r = math.radians(a0), math.radians(a1)
    pts = []
    for i in range(n_arc):
        a = a0r + (a1r - a0r) * i / (n_arc - 1)
        pts.append((r_out * math.cos(a), r_out * math.sin(a)))
    for i in range(n_arc - 2, 0, -1):
        a = a0r + (a1r - a0r) * i / (n_arc - 1)
        pts.append((r_in * math.cos(a), r_in * math.sin(a)))
    return pts


def outline_with_normals(pts, rng=None):
    """闭合轮廓 → [(x, y, nx, ny, j)]：单位外法线 + 扰动幅（点距驱动，角部更大）。"""
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2 = pts[(i - 1) % n], pts[i], pts[(i + 1) % n]
        tx, ty = p2[0] - p0[0], p2[1] - p0[1]
        L = math.hypot(tx, ty) or 1.0
        nx, ny = ty / L, -tx / L                       # CCW 轮廓 → 外法线
        rr = rng.random() if rng is not None else 0.5
        j = JITTER * (0.7 + 0.6 * rr)
        out.append((p1[0], p1[1], nx, ny, j))
    return out


def _ring(outline, z, scale, cx, cy, jm):
    """层环：基准轮廓 × scale + 法向扰动 × jm。"""
    return [(cx + px * scale + nx * j * jm, cy + py * scale + ny * j * jm, z)
            for (px, py, nx, ny, j) in outline]


def terrace_body(acc, outline, layers, side_mat_fn, cap_mats, rng,
                 skirt=True, skirt_bands=2, bands=SIDE_BANDS):
    """层台 mound 主体：侧壁地层带（逐带扰动相移=岩层节理）+ 每层衬底盖 + 水下裙边。

    layers: [(z_top, scale, cx, cy), ...] 由外到内；
    side_mat_fn(z) -> 槽号；cap_mats: 每层缝底衬盖槽号列表（与 layers 等长）。
    """
    # 环序列：裙边 → 层0顶 → 层间带 → ... → 顶层顶
    jm_cycle = [1.0, 0.5, 1.2, 0.45, 1.05, 0.55, 1.15, 0.5]
    rings, ring_mat = [], []

    z_lo = layers[0][0]
    if skirt:
        base = layers[0]
        skirt_total = skirt_bands + bands                 # 裙边→顶面整段细分（单层件侧壁也够碎）
        for k in range(skirt_total):
            f = (k + 1) / skirt_total
            z = SKIRT_BOT_Z + (z_lo - SKIRT_BOT_Z) * f
            sc = base[1] * (SKIRT_SPREAD - 0.10 * f)      # 底部外扩、向上收
            jm = jm_cycle[k % len(jm_cycle)] * (1.35 - 0.3 * f)
            rings.append(_ring(outline, z, sc, base[2], base[3], jm))
            ring_mat.append(side_mat_fn(z))
    rings.append(_ring(outline, z_lo, layers[0][1], layers[0][2], layers[0][3], 1.0))
    ring_mat.append(side_mat_fn(z_lo))

    for li in range(len(layers) - 1):
        z0, s0, cx0, cy0 = layers[li]
        z1, s1, cx1, cy1 = layers[li + 1]
        # 梯田形制：陡坎（近竖直，不遮下层平台）+ 窄过渡斜坡（坎顶处快速收进）
        # 若从本层轮廓直接起斜坡，45° 视角下斜锥投影会盖住下层整个顶面（第 4 轮实证）
        z_shelf = max(z0 + 0.06, z1 - 0.18)
        for sub, jm in ((1, 1.1), (2, 0.55)):              # 陡坎
            z = z0 + (z_shelf - z0) * sub / 2.0
            rings.append(_ring(outline, z, s0 * 0.995, cx0, cy0, jm))
            ring_mat.append(side_mat_fn(z))
        for sub in (1, 2):                                 # 坎顶过渡（窄带内完成收分+平移）
            f = sub / 2.0
            z = z_shelf + (z1 - z_shelf) * f
            sc = s0 + (s1 - s0) * f
            cx = cx0 + (cx1 - cx0) * f
            cy = cy0 + (cy1 - cy0) * f
            rings.append(_ring(outline, z, sc, cx, cy, 0.5))
            ring_mat.append(side_mat_fn(z))
        rings.append(_ring(outline, z1, s1, cx1, cy1, 1.0))
        ring_mat.append(side_mat_fn(z1))

    # loft 侧壁
    for ri in range(len(rings) - 1):
        r0, r1 = rings[ri], rings[ri + 1]
        m = ring_mat[min(ri, len(ring_mat) - 1)]
        n = len(r0)
        for j in range(n):
            j2 = (j + 1) % n
            acc.add([r0[j], r0[j2], r1[j2], r1[j]], [(0, 1, 2, 3)], m, False)

    # 每层衬底盖（顶板拼缝的暗缝底；下沉 CAP_DROP 防共面 z-fighting）+ 底盖
    for li, (z, sc, cx, cy) in enumerate(layers):
        cap_fan(acc, _ring(outline, z, sc, cx, cy, 0.0), z - CAP_DROP, cap_mats[li])
    cap_fan(acc, _ring(outline, SKIRT_BOT_Z, layers[0][1] * SKIRT_SPREAD, layers[0][2], layers[0][3], 0.0),
            SKIRT_BOT_Z, cap_mats[0])


def cap_fan(acc, ring, z, mat):
    """环口三角扇封盖（顶面 z 严格 = 传入 ring 的 z）。"""
    n = len(ring)
    cx = sum(p[0] for p in ring) / n
    cy = sum(p[1] for p in ring) / n
    base = len(acc.verts)
    acc.verts.extend([(px, py, z) for (px, py, _z) in ring])
    acc.verts.append((cx, cy, z))
    ci = base + n
    faces = [(base + i, base + (i + 1) % n, ci) for i in range(n)]
    for _ in faces:
        acc.face_mat.append(mat)
        acc.face_smooth.append(False)
    acc.faces.extend(faces)


# ============================================================================
# 顶面拼板（站面顶面：所有板同一 z 常数 → 天然满足平直纪律；缝隙露衬底盖=暗缝）
# ============================================================================

def top_surface(acc, hw, hd, n_pow, z_top, mat_fn, n_seg=8, ring_scales=(0.62, 0.30),
                t=TOP_T, off=(0.0, 0.0), rot=0.0):
    """顶面三层拼装（超椭圆解析采样，精确贴合轮廓；离散点角度采样会产生畸形板，禁用）：
    外环板 n_seg 段 → 内环板 5 段 → 芯板。
    mat_fn(zone, seg_idx) -> 槽号（zone: 0=外环 1=内环 2=芯）。
    板间缝由段端向质心收缩 2.5% 形成；外缘带立面（檐口线，高 t）。off=中心平移、rot=朝向。"""
    radii = [1.0] + list(ring_scales)
    for zi in range(len(radii) - 1):
        s_out, s_in = radii[zi], radii[zi + 1]
        zone = zi
        seg = n_seg if zi == 0 else 5
        for k in range(seg):
            a0 = 2.0 * math.pi * k / seg
            a1 = 2.0 * math.pi * (k + 1) / seg
            quad = [_se_pt(hw, hd, n_pow, a0, s_out, off, rot),
                    _se_pt(hw, hd, n_pow, a1, s_out, off, rot),
                    _se_pt(hw, hd, n_pow, a1, s_in, off, rot),
                    _se_pt(hw, hd, n_pow, a0, s_in, off, rot)]
            mx = sum(p[0] for p in quad) / 4.0
            my = sum(p[1] for p in quad) / 4.0
            quad = [(mx + (p[0] - mx) * 0.975, my + (p[1] - my) * 0.975, z_top) for p in quad]
            mat = mat_fn(zone, k)
            acc.add(quad, [(0, 1, 2, 3)], mat, False)
            if zi == 0:                              # 外缘立面
                e0, e1 = quad[0], quad[1]
                acc.add([e0, e1, (e1[0], e1[1], z_top - t), (e0[0], e0[1], z_top - t)],
                        [(0, 1, 2, 3)], mat, False)
    # 芯板
    inner = [_se_pt(hw, hd, n_pow, 2.0 * math.pi * k / 8, radii[-1], off, rot) for k in range(8)]
    cap_fan(acc, [(px, py, z_top) for (px, py) in inner], z_top, mat_fn(2, 0))


def _se_pt(hw, hd, n_pow, ang, scale, off=(0.0, 0.0), rot=0.0):
    """超椭圆轮廓在角度 ang 处的解析点 × scale + off（再绕 off 旋 rot）。"""
    e = 2.0 / max(1.01, n_pow)
    a = ang - rot
    ca, sa = math.cos(a), math.sin(a)
    x = hw * math.copysign(abs(ca) ** e, ca)
    y = hd * math.copysign(abs(sa) ** e, sa)
    px, py = x * scale, y * scale
    if rot:
        cr, sr = math.cos(rot), math.sin(rot)
        px, py = px * cr - py * sr, px * sr + py * cr
    return (off[0] + px, off[1] + py)


# ============================================================================
# 细节件库（贝壳 / 草裙 / 漂木 / 岩块 / 海鸟 / 贴地条 / 浪蚀口袋 / 柱 / 鳞片）
# ============================================================================

def grass_tuft(acc, x, y, z, h, rng, mat_main, mat_dark):
    """草簇：3-4 片窄三角叶（贴地散开）。"""
    for k in range(rng.randint(3, 4)):
        a = rng.uniform(0, 2 * math.pi)
        lean = rng.uniform(0.25, 0.62) * h
        hh = h * rng.uniform(0.7, 1.15)
        w = 0.045
        dx, dy = math.cos(a), math.sin(a)
        acc.add([(x - dy * w, y + dx * w, z), (x + dy * w, y - dx * w, z),
                 (x + dx * lean, y + dy * lean, z + hh)],
                [(0, 1, 2)], mat_main if k % 2 == 0 else mat_dark, False)


def shell(acc, x, y, z, r, mat, rng, rot=0.0):
    """贝壳：低四棱锥（+30% 概率带一只小的）。"""
    def cone(cx, cy, rr, aa):
        pts = [(cx + rr * math.cos(aa + k * math.pi / 2 + 0.3),
                cy + rr * math.sin(aa + k * math.pi / 2 + 0.3), z) for k in range(4)]
        pts.append((cx, cy, z + rr * 0.55))
        b = len(acc.verts)
        acc.verts.extend(pts)
        faces = [(b + 0, b + 1, b + 4), (b + 1, b + 2, b + 4), (b + 2, b + 3, b + 4), (b + 3, b + 0, b + 4)]
        for _ in faces:
            acc.face_mat.append(mat)
            acc.face_smooth.append(False)
        acc.faces.extend(faces)
    cone(x, y, r, rot)
    if rng.random() < 0.3:
        cone(x + r * 1.6, y + 0.1, r * 0.55, rot + 0.8)


def rock_chunk(acc, cx, cy, cz, sx, sy, sz, rng, mat, n=7):
    """低面数随机岩块（顶收缩，flat）。"""
    top_s = rng.uniform(0.45, 0.7)
    lo = []
    hi = []
    for k in range(n):
        a = 2 * math.pi * k / n + rng.uniform(-0.12, 0.12)
        rl = 0.5 + rng.uniform(-0.14, 0.14)
        lo.append((cx + sx * rl * math.cos(a), cy + sy * rl * math.sin(a), cz))
        hi.append((cx + sx * top_s * rl * math.cos(a) + rng.uniform(-0.1, 0.1),
                   cy + sy * top_s * rl * math.sin(a) + rng.uniform(-0.1, 0.1),
                   cz + sz * rng.uniform(0.85, 1.1)))
    loft_closed(acc, lo, hi, mat)
    cap_fan(acc, hi, hi[0][2], mat)
    cap_fan(acc, lo, lo[0][2], mat)


def loft_closed(acc, r0, r1, mat, smooth=False):
    n = len(r0)
    for k in range(n):
        k2 = (k + 1) % n
        acc.add([r0[k], r0[k2], r1[k2], r1[k]], [(0, 1, 2, 3)], mat, smooth)


def driftwood(acc, x, y, z, L, yaw, rng, mat=M_WOODD):
    """漂木：微弯主杆（2 段）+ 1 短枝，半嵌地面（中心 z=地面上 ~0.1）。"""
    dx, dy = math.cos(yaw), math.sin(yaw)
    bend = rng.uniform(0.2, 0.45)
    p0 = (x - dx * L * 0.5, y - dy * L * 0.5, z + 0.02)
    p1 = (x + dy * bend * 0.3, y - dx * bend * 0.3, z + 0.14)
    p2 = (x + dx * L * 0.5, y + dy * L * 0.5, z + 0.2)
    ax_tube(acc, p0, p1, 0.13, 0.10, 6, mat)
    ax_tube(acc, p1, p2, 0.10, 0.06, 6, mat)
    ax_tube(acc, p1, (p1[0] + dx * 0.5 - dy * 0.5, p1[1] + dy * 0.5 + dx * 0.5, p1[2] + 0.45),
            0.05, 0.03, 5, mat)


def seagull(acc, x, y, z, yaw, s=1.0, mat=M_SAIL):
    """海鸟：细长身 + 双翼三角（剪影细节）。"""
    fx, fy = math.cos(yaw), math.sin(yaw)
    tail = (x - fx * 0.28 * s, y - fy * 0.28 * s, z)
    head = (x + fx * 0.28 * s, y + fy * 0.28 * s, z)
    lft = (x - fy * 0.09 * s, y + fx * 0.09 * s, z + 0.1 * s)
    rgt = (x + fy * 0.09 * s, y - fx * 0.09 * s, z + 0.1 * s)
    peak = (x - fx * 0.02 * s, y - fy * 0.02 * s, z + 0.16 * s)
    for a, b, c in ((tail, lft, peak), (lft, head, peak), (head, rgt, peak), (rgt, tail, peak)):
        acc.add([a, b, c], [(0, 1, 2)], mat, False)
    for sgn in (1, -1):                                        # 双翼
        acc.add([(x - fy * 0.05 * s * sgn, y + fx * 0.05 * s * sgn, z + 0.1 * s),
                 (x + fy * 0.5 * s * sgn, y - fx * 0.5 * s * sgn, z + 0.22 * s),
                 (x + fy * 0.55 * s * sgn, y - fx * 0.55 * s * sgn, z + 0.08 * s)],
                [(0, 1, 2)], mat, False)


def ground_strip(acc, pts, w, z_top, mat, drop=STRIP_DROP):
    """贴地细节条（裂纹 / 湿斑 / 余烬纹）：折线分段薄盒，顶面低于站面 drop。"""
    for a, b in zip(pts, pts[1:]):
        mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        if L < 1e-4:
            continue
        ang = math.atan2(b[1] - a[1], b[0] - a[0])
        ax_box(acc, (mx, my, z_top - drop - 0.03), (L + w * 0.6, w, 0.06), mat, rot_z=ang)


def wet_patch(acc, x, y, z_top, rx, ry, mat):
    """湿斑/潮池：贴地薄盘（顶低于站面，不参与碰撞）。"""
    pts = [(x + rx * math.cos(2 * math.pi * k / 10), y + ry * math.sin(2 * math.pi * k / 10), z_top)
           for k in range(10)]
    cap_fan(acc, pts, z_top, mat)
    rim = [(p[0], p[1], z_top - 0.05) for p in pts]
    loft_closed(acc, rim, pts, mat)


def erosion_pocket(acc, x, y, z, sx, sz, yaw, mat):
    """浪蚀口袋：嵌进侧壁的暗色凹板（外露面比壁面内缩，读作凹龛）。"""
    ax_box(acc, (x, y, z), (sx, 0.35, sz), mat, rot_z=yaw)


def pillar(acc, cx, cy, z0, z1, r, n_side, mat, rng):
    """玄武柱状节理单柱：微锥度 + 顶点扰动。"""
    lo = [(cx + r * rng.uniform(0.85, 1.12) * math.cos(2 * math.pi * k / n_side),
           cy + r * rng.uniform(0.85, 1.12) * math.sin(2 * math.pi * k / n_side), z0) for k in range(n_side)]
    hi = [(cx + r * rng.uniform(0.72, 1.0) * math.cos(2 * math.pi * k / n_side + 0.09),
           cy + r * rng.uniform(0.72, 1.0) * math.sin(2 * math.pi * k / n_side + 0.09), z1) for k in range(n_side)]
    loft_closed(acc, lo, hi, mat)
    cap_fan(acc, hi, z1, mat)


def hex_scale(acc, cx, cy, z_top, r_hex, h, mat, rot=0.0):
    """龟甲六边形鳞纹凸片：顶六边面 + 6 侧裙边（凸出顶面 h，不改变碰撞档）。"""
    top = [(cx + r_hex * math.cos(rot + math.pi / 3 * k + math.pi / 6),
            cy + r_hex * math.sin(rot + math.pi / 3 * k + math.pi / 6), z_top + h) for k in range(6)]
    bot = [(px, py, z_top) for (px, py, _z) in top]
    for k in range(6):
        k2 = (k + 1) % 6
        acc.add([bot[k], bot[k2], top[k2], top[k]], [(0, 1, 2, 3)], mat, False)
    cap_fan(acc, top, z_top + h, mat)


# ============================================================================
# 站面 manifest helper（c/s/yaw；sz 全高防侧穿）
# ============================================================================

def banded_mats(zone_colors, n_out=8):
    """低频分区配色（防棋盘乱色）：外环前半区 A 色/后半区 B 色，内环/芯各一色。
    zone_colors = [(外A, 外B), 内色, 芯色] —— 与 top_surface 的 (zone,k) 回调约定配套。"""
    def fn(zone, k):
        if zone == 0:
            a, b = zone_colors[0]
            return a if k < (n_out // 2) else b
        return zone_colors[1] if zone == 1 else zone_colors[2]
    return fn


def sbox(cx, cy, z_top, sx, sy, yaw_deg=0.0, sz=None):
    sz = (z_top - SKIRT_BOT_Z) if sz is None else sz
    return {"c": [round(cx, 3), round(cy, 3), round(z_top - sz / 2.0, 4)],
            "s": [round(sx, 3), round(sy, 3), round(sz, 3)],
            "yaw": round(yaw_deg, 2)}


# ============================================================================
# 材质（色值/粗糙度全部取自 style_tokens.SLOTS；sRGB→线性同样板）
# ============================================================================

def _srgb_to_linear(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def make_kit_materials():
    out = []
    for name in KIT_SLOTS:
        spec = ST.slot(name)                                   # 未登记槽名直接抛错
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes['Principled BSDF']
        r, g, b = ST.hex_to_rgb(spec['hex'])
        bsdf.inputs['Base Color'].default_value = (_srgb_to_linear(r), _srgb_to_linear(g), _srgb_to_linear(b), 1.0)
        bsdf.inputs['Roughness'].default_value = spec['roughness']
        bsdf.inputs['Metallic'].default_value = 0.0
        out.append((name, mat))
    return out


def join_to_object(acc, obj_name, mats):
    """MeshAcc → 单网格对象（只挂实际用到的槽）。照样板。"""
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
    # 注意：不做 recalc_face_normals——本 kit 所有面绕向已手工保证
    #（loft/cap_fan/顶板 quad 全部 CCW 约定）；recalc 对孤立面片（顶板拼板）的
    # 启发式翻转会让顶面随机朝下（渲染发黑），第 2 轮预览实证过。
    bm.to_mesh(me)
    bm.free()
    for old in used:
        me.materials.append(mats[old][1])
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ============================================================================
# 渲染（Cycles + Standard 视图变换 + ST 灰底三灯；机位照样板模式）
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
    scene.view_settings.view_transform = 'Standard'            # 铁律：AgX 洗色
    scene.view_settings.look = 'None'
    log('render engine=Cycles device=%s samples=%d' % (gpu or 'CPU', SAMPLES))


def aim_preview_lights(scene, target=(0.0, 0.0, 1.2)):
    """ST.setup_preview_world 的灯只有位置没有姿态（AREA 默认 -Z）——这里补 look-at。"""
    t = mathutils.Vector(target)
    for o in scene.objects:
        if o.type == 'LIGHT':
            d = t - o.location
            o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def setup_preview_world_compat(scene):
    """ST.setup_preview_world 的同参兼容版（Blender 5.2.1 适配）。

    ST 原函数内部有一行占位废代码 `bpy.data.curves.new(type="TEXT")` 在 5.2 崩溃
    （5.2 枚举为 "FONT"，且该曲线创建后立即删除、无任何作用）。
    本函数逐行复刻 ST 实现（背景/地板/三灯的色值·能量·位置与 ST 源码同款，
    只去掉那两行废代码）；style_tokens 修复后可整体切回。
    """
    world = scene.world
    if world is None:
        world = bpy.data.worlds.new("ST_PreviewWorld")
        scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (*ST.hex_to_rgb(ST.PREVIEW_BG_HEX), 1.0)
    bg.inputs[1].default_value = 1.0

    floor = bpy.data.meshes.new("ST_PreviewFloor")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400.0,
                          matrix=mathutils.Matrix.Translation((0.0, 0.0, -0.01)))
    bm.to_mesh(floor)
    bm.free()
    floor_obj = bpy.data.objects.new("ST_PreviewFloor", floor)
    scene.collection.objects.link(floor_obj)

    def _lamp(name, color, energy, location):
        data = bpy.data.lights.new(name, "AREA")
        data.color = ST.hex_to_rgb(color)
        data.energy = energy
        data.size = 60.0
        obj = bpy.data.objects.new(name, data)
        obj.location = location
        scene.collection.objects.link(obj)
        return obj

    # 三灯姿态沿用样板/ST：暖主光（右上前）、冷补光（左）、暖轮廓（后上）
    _lamp("ST_Key", "#FFE8C8", 90000, (160, -140, 220))
    _lamp("ST_Fill", "#C8DDF0", 36000, (-180, -60, 120))
    _lamp("ST_Rim", "#FFF0D8", 30000, (-40, 180, 160))
    return world, floor_obj


def render_views(scene, obj, name, out_dir):
    """三视角预览：机位按件 bbox 自适应（对角线定距、bbox 中心为目标，防大件出框）。"""
    import mathutils
    lo = [min(v[i] for v in obj.bound_box) for i in range(3)]
    hi = [max(v[i] for v in obj.bound_box) for i in range(3)]
    ext = [hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]]
    diag = math.sqrt(ext[0] ** 2 + ext[1] ** 2 + ext[2] ** 2)
    ctr = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2)
    d = diag * 1.30
    tz = ctr[2]
    base = VIEWS_CFG.get(name, {}).get('d_mult', 1.0)
    d *= base
    views = [
        ('front34', (ctr[0] - 0.72 * d, ctr[1] - 0.70 * d, ctr[2] + 0.42 * d), ctr),
        ('side', (ctr[0] - 1.02 * d, ctr[1] + 0.16 * d, ctr[2] + 0.20 * d), ctr),
        ('back', (ctr[0] + 0.60 * d, ctr[1] + 0.74 * d, ctr[2] + 0.36 * d), ctr),
    ]
    for vn, loc, target in views:
        cam_data = bpy.data.cameras.new('kit_cam')
        cam_data.lens = 50
        cam_data.clip_end = 800
        cam = bpy.data.objects.new('kit_cam', cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        di = mathutils.Vector(target) - mathutils.Vector(loc)
        cam.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        path = os.path.join(out_dir, '%s-%s.jpg' % (name, vn))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        log('rendered %s (%d bytes)' % (path, os.path.getsize(path)))
        bpy.data.objects.remove(cam, do_unlink=True)


# ============================================================================
# ① AtollArcA —— 环礁弧段（外 R44 / 内 R32、跨 90°、凹面朝 -Y；顶 +0.5；礁台 +1.0 ×2）
# ============================================================================

def build_atoll_arc(acc, rng):
    P = ATOLL_ARC
    n_arc = SEG_ARC_BIG
    outline = outline_with_normals(annulus_outline(P['r_out'], P['r_in'], P['a0'], P['a1'], n_arc), rng)
    layers = [(P['top'], 1.0, 0.0, 0.0)]

    def side_mat(z):
        if z < -0.7:
            return M_ROCKD
        if z < 0.42:
            return M_WETSAND
        return M_ROCKM

    terrace_body(acc, outline, layers, side_mat, [M_ROCKD], rng)

    # 顶面：弧环带拼板 n_plates 段（板顶同 +0.5；yaw=段中角，与 manifest 一致）
    # 低频分区：外缘带 SANDL（浪沙线）+ 主面 SANDM，段间同色只留缝
    seg_a = (P['a1'] - P['a0']) / P['n_plates']
    boxes = []
    for k in range(P['n_plates']):
        b0 = math.radians(P['a0'] + seg_a * k + 0.8)
        b1 = math.radians(P['a0'] + seg_a * (k + 1) - 0.8)
        mid = (b0 + b1) / 2
        r_o, r_i = P['r_out'] - 0.4, P['r_in'] + 0.4
        r_mid_in = P['r_in'] + 2.6                              # 内缘 SANDL 浪沙线分界
        # 主面（内区 SANDM）与外缘浪沙亮带（SANDL）分区拼接、不重叠（防共面 z-fighting）
        quad = [(r_mid_in * math.cos(b0), r_mid_in * math.sin(b0), P['top']),
                (r_mid_in * math.cos(b1), r_mid_in * math.sin(b1), P['top']),
                (r_i * math.cos(b1), r_i * math.sin(b1), P['top']),
                (r_i * math.cos(b0), r_i * math.sin(b0), P['top'])]
        acc.add(quad, [(0, 1, 2, 3)], M_SANDM, False)
        strip = [(r_o * math.cos(b0), r_o * math.sin(b0), P['top']),
                 (r_o * math.cos(b1), r_o * math.sin(b1), P['top']),
                 (r_mid_in * math.cos(b1), r_mid_in * math.sin(b1), P['top']),
                 (r_mid_in * math.cos(b0), r_mid_in * math.sin(b0), P['top'])]
        acc.add(strip, [(0, 1, 2, 3)], M_SANDL, False)
        for p0, p1 in ((quad[0], quad[1]), (quad[2], quad[3])):  # 内外缘立面
            acc.add([p0, p1, (p1[0], p1[1], P['top'] - TOP_T), (p0[0], p0[1], P['top'] - TOP_T)],
                    [(0, 1, 2, 3)], M_ROCKD, False)
        # 端部立面（弧段断口）
        for pa, pb in ((quad[0], quad[3]), (quad[1], quad[2])):
            acc.add([pa, pb, (pb[0], pb[1], P['top'] - TOP_T), (pa[0], pa[1], P['top'] - TOP_T)],
                    [(0, 1, 2, 3)], M_ROCKD, False)
        chord = 2 * 38.0 * math.sin(math.radians(seg_a / 2 - 0.8))
        boxes.append(sbox(38.0 * math.cos(mid), 38.0 * math.sin(mid), P['top'], chord, 11.5,
                          math.degrees(mid)))

    # +1.0 礁台 ×2（坐在弧顶上的小岩台：mini-mound + 拼板 2 + 裂纹 + 贝壳）
    for deg in P['reef_deg']:
        a = math.radians(deg)
        cx, cy = 38.0 * math.cos(a), 38.0 * math.sin(a)
        rot = deg + 90.0                                       # 台长轴顺弧向
        ol = outline_with_normals(_rot_pts(superellipse_pts(4.2, 2.7, 2.6, 18), math.radians(rot)), rng)
        terrace_body(acc, ol, [(P['reef_top'], 1.0, cx, cy)], lambda z: M_ROCKL, [M_ROCKD], rng,
                     skirt=False, bands=2)
        top_surface(acc, 4.2, 2.7, 2.6, P['reef_top'], banded_mats([(M_ROCKL, M_ROCKM), M_ROCKL, M_ROCKM], n_out=4),
                    n_seg=4, ring_scales=(0.55,), off=(cx, cy), rot=math.radians(rot))
        crack = [(cx - 2.6 * math.cos(a), cy - 2.6 * math.sin(a)), (cx, cy), (cx + 2.4 * math.cos(a + 0.5), cy + 2.4 * math.sin(a + 0.5))]
        ground_strip(acc, crack, 0.22, P['reef_top'], M_ROCKD)
        for _ in range(4):
            shell(acc, cx + rng.uniform(-2.8, 2.8), cy + rng.uniform(-1.8, 1.8),
                  P['reef_top'] - 0.01, rng.uniform(0.2, 0.32), M_CORAL, rng)
        chord_r = 8.0
        boxes.append(sbox(cx, cy, P['reef_top'], chord_r, 5.0, rot))

    # 外缘浪蚀口袋 + 内缘落水暗带 + 顶面贝壳/鸟
    for deg in (rng.uniform(P['a0'] + 8, P['a1'] - 8) for _ in range(6)):
        a = math.radians(deg)
        zz = rng.uniform(-0.4, 1.8)
        erosion_pocket(acc, (P['r_out'] - 0.05) * math.cos(a), (P['r_out'] - 0.05) * math.sin(a),
                       zz, rng.uniform(0.7, 1.4), rng.uniform(0.4, 0.9), a + math.pi / 2, M_ROCKD)
    for deg in (-120.0, -78.0, -55.0):
        a = math.radians(deg)
        shell(acc, 36.0 * math.cos(a), 36.0 * math.sin(a), P['top'] - 0.01,
              rng.uniform(0.15, 0.26), M_CORAL, rng, rot=a)
    seagull(acc, 38.0 * math.cos(math.radians(-95)), 38.0 * math.sin(math.radians(-95)),
            P['top'] + 0.02, math.radians(-30), 1.0)
    return boxes, 'stand=[+0.5x3, +1.0x2] 弧形分段yaw'


def _rot_pts(pts, ang):
    ca, sa = math.cos(ang), math.sin(ang)
    return [(px * ca - py * sa, px * sa + py * ca) for (px, py) in pts]


def _shift_pts(norm_pts, dx, dy):
    return [(px + dx, py + dy, nx, ny, j) for (px, py, nx, ny, j) in norm_pts]


# ============================================================================
# ② AtollCore —— 泻湖心岛 14×10 @ +0.5 + 礁台阶 +1.0 ×2
# ============================================================================

def build_atoll_core(acc, rng):
    P = ATOLL_CORE
    outline = outline_with_normals(superellipse_pts(P['hw'], P['hd'], P['n_pow'], SEG_ARC), rng)
    layers = [(P['top'], 1.0, 0.0, 0.0)]

    def side_mat(z):
        if z < -0.7:
            return M_ROCKD
        if z < 0.42:
            return M_SANDD                                  # 湿沙水线（槽位与衬盖并档）
        return M_SANDM

    terrace_body(acc, outline, layers, side_mat, [M_SANDD], rng)

    top_surface(acc, P['hw'], P['hd'], P['n_pow'], P['top'], banded_mats([(M_SANDM, M_SANDL), M_SANDM, M_SANDL]),
                n_seg=8, ring_scales=(0.6, 0.28))

    # +1.0 礁台阶 ×2（亮岩顶+暗缝，读作礁石台而非黑斑）
    boxes = [sbox(0, 0, P['top'], P['hw'] * 2 - 0.4, P['hd'] * 2 - 0.4)]
    for (cx, cy, zt) in P['steps']:
        ol = outline_with_normals(superellipse_pts(2.5, 2.0, 2.4, 14), rng)
        terrace_body(acc, ol, [(zt, 1.0, cx, cy)], lambda z: M_ROCKM, [M_ROCKD], rng, skirt=False, bands=2)
        top_surface(acc, 2.5, 2.0, 2.4, zt, banded_mats([(M_ROCKL, M_ROCKM), M_ROCKL, M_ROCKM], n_out=4),
                    n_seg=4, ring_scales=(0.5,), off=(cx, cy))
        ground_strip(acc, [(cx - 1.8, cy + 0.5), (cx, cy - 0.4), (cx + 1.8, cy + 0.3)], 0.2, zt, M_ROCKD)
        boxes.append(sbox(cx, cy, zt, 4.8, 4.0))

    # 细节：贝壳（两色、加大）+ 漂木；泻湖心岛无草（槽位让给木/贝）
    for i in range(0, SEG_ARC, 3):
        px, py, nx, ny, _j = outline[i]
        shell(acc, px * 0.97, py * 0.97, P['top'] - 0.01, rng.uniform(0.2, 0.32),
              M_CORAL if i % 2 else M_SANDL, rng)
    for _ in range(8):
        a = rng.uniform(0, 2 * math.pi)
        shell(acc, 4.5 * math.cos(a), 3.1 * math.sin(a), P['top'] - 0.01,
              rng.uniform(0.18, 0.3), M_CORAL if rng.random() < 0.5 else M_SANDL, rng)
    driftwood(acc, -2.0, 1.2, P['top'], 2.8, math.radians(70), rng)
    return boxes, 'stand=[+0.5, +1.0x2]'


# ============================================================================
# ③④ TerraceIslandL / M —— 梯田岛（四层/三层错落，侧坡地层感）
# ============================================================================

def _build_terrace(acc, rng, P, big):
    outline = outline_with_normals(superellipse_pts(P['layers'][0][1], P['layers'][0][2], P['n_pow'],
                                                    SEG_ARC_BIG if big else SEG_ARC), rng)
    layers = [(z, hw / P['layers'][0][1], cx, cy) for (z, hw, hd, cx, cy) in P['layers']]

    def side_mat(z):
        if z < -0.7:
            return M_ROCKD
        if z < 0.42:
            return M_SANDM                                  # 沙色水线带（槽位并档）
        return M_ROCKM if z < 1.0 else M_ROCKD

    caps = [M_ROCKD, M_ROCKD, M_ROCKD, M_GRASSD][:len(layers)]
    while len(caps) < len(layers):
        caps.append(M_ROCKD)
    terrace_body(acc, outline, layers, side_mat, caps, rng)

    # 各层顶面：外环拼板（草/沙按层）+ 内环 + 芯
    boxes = []
    zone_mats = [
        [(M_SANDM, M_SANDL), M_SANDM, M_SANDL],                # L1：沙（低频半区）
        [(M_GRASSM, M_GRASSD), M_GRASSM, M_GRASSL],            # L2：草
        [(M_GRASSL, M_GRASSM), M_GRASSL, M_GRASSM],            # L3：亮草
        [(M_GRASSM, M_GRASSL), M_GRASSL, M_GRASSD],            # L4：草
    ]
    for li, (z, sc, cx, cy) in enumerate(layers):
        if li == 0:
            ol = outline
        else:
            hw = P['layers'][li][1]
            ol = outline_with_normals(superellipse_pts(hw, P['layers'][li][2], P['n_pow'],
                                                       SEG_ARC if not big else SEG_ARC_BIG), rng)
        top_surface(acc, P['layers'][li][1], P['layers'][li][2], P['n_pow'], z,
                    banded_mats(zone_mats[min(li, len(zone_mats) - 1)], n_out=8 if li < 2 else 6),
                    n_seg=8 if li < 2 else 6, ring_scales=(0.58, 0.24), off=(cx, cy))
        hw, hd = P['layers'][li][1], P['layers'][li][2]
        boxes.append(sbox(cx, cy, z, hw * 2 - 0.4, hd * 2 - 0.4))

    # 层间阶梯（装饰，窄踏面不入 manifest）：东北侧每层 2 级
    for li in range(len(layers) - 1):
        z0, hw0 = layers[li][0], P['layers'][li][1]
        z1, hw1 = layers[li + 1][0], P['layers'][li + 1][1]
        step_cx = P['layers'][li][3] + hw0 * 0.55
        step_cy = P['layers'][li][4] + P['layers'][li][2] * 0.62
        for stp in range(int(round((z1 - z0) / 0.5))):
            ax_box(acc, (step_cx - hw1 * 0.4 - stp * 0.5, step_cy - stp * 0.4,
                         z0 + 0.25 + stp * 0.5 - 0.25),
                   (2.2, 1.3, 0.5), M_SANDM, rot_z=math.radians(-20))

    # 细节：草裙 / 岩露头 / L4 枯木
    top_z, top_hw = layers[-1][0], P['layers'][-1][1]
    cx4, cy4 = layers[-1][2], layers[-1][3]
    driftwood(acc, cx4 + 1.2, cy4 - 0.8, top_z, 2.6 if big else 2.0, math.radians(115), rng)
    for _ in range(9 if big else 6):
        grass_tuft(acc, cx4 + rng.uniform(-top_hw, top_hw) * 0.6, cy4 + rng.uniform(-0.9, 0.9),
                   top_z, rng.uniform(0.4, 0.75), rng, M_GRASSL, M_GRASSD)
    ol0 = outline
    for i in rng.sample(range(len(ol0)), 9):
        px, py, nx, ny, _j = ol0[i]
        if -0.5 < py * 0.2 < 20:
            rock_chunk(acc, px * 1.005, py * 1.005, 0.2, 0.7, 0.65, 0.9, rng, M_ROCKD)
    return boxes, 'stand=[%s] 层台错落' % ', '.join('+%.1f' % l[0] for l in layers)


def build_terrace_l(acc, rng):
    return _build_terrace(acc, rng, TERRACE_L, big=True)


def build_terrace_m(acc, rng):
    return _build_terrace(acc, rng, TERRACE_M, big=False)


# ============================================================================
# ⑤⑥ SeaStackTall / Short —— 海蚀柱（节理柱身 + 顶盘站面 + 海鸟）
# ============================================================================

def _build_stack(acc, rng, P, tall):
    # 柱身：r(z) 连续 profile + 环向节理凹陷（高环密度采样让节理读得出）+ 竖向棱褶
    z_bot, z_top = SKIRT_BOT_Z, P['cap_z'] - 0.5
    n_ring = 26
    joints = [(z_bot + (z_top - z_bot) * (i + 1) / (P['joints'] + 1), rng.uniform(0.15, 0.28))
              for i in range(P['joints'])]                     # 每道节理深度不同（破规律）

    def radius(z, ang):
        f = (z - z_bot) / (z_top - z_bot)
        r = P['r_foot'] + (P['r_top'] - P['r_foot']) * f
        r *= 1.0 + 0.09 * math.sin(ang * 3 + 1.2) + 0.065 * math.sin(ang * 5 + 0.4)   # 竖向棱褶
        for jz, dep in joints:
            r -= dep * math.exp(-((z - jz) / 0.30) ** 2)       # 节理环凹（加密采样后成形）
        if z < 1.1:
            r += (1.1 - z) * 0.55                              # 浪蚀裙外扩
        if 0.15 < z < 0.55:
            r -= 0.28                                          # 水线浪蚀腰
        return max(1.2, r)

    n_seg = 20

    def stack_mat(z):
        if z < 0.35:
            return M_WETSAND                                   # 水线湿带
        if z < 1.5:
            return M_SANDD                                     # 干沙溅染带
        return M_ROCKM                                         # 干区主岩

    rings = []
    for ri in range(n_ring + 1):
        z = z_bot + (z_top - z_bot) * ri / n_ring
        ring = []
        for k in range(n_seg):
            a = 2 * math.pi * k / n_seg
            ring.append((radius(z, a) * math.cos(a), radius(z, a) * math.sin(a), z))
        rings.append(ring)
    for ri in range(n_ring):
        loft_closed(acc, rings[ri], rings[ri + 1], stack_mat(rings[ri][0][2]))

    # 顶盘（站面 @cap_z）：出挑檐 + 低频分区拼板（3 亮 1 中）+ 衬盖
    cap_bot = z_top
    cz = P['cap_z']
    cr = P['cap_r']
    cap_out = outline_with_normals(superellipse_pts(cr, cr, 2.2, 20))
    cap_rings = [_ring(cap_out, cap_bot, 1.0, 0, 0, 0.6), _ring(cap_out, cz, 1.0, 0, 0, 0.25)]
    loft_closed(acc, cap_rings[0], cap_rings[1], M_ROCKL)
    cap_fan(acc, cap_rings[1], cz - CAP_DROP, M_ROCKD)         # 衬盖（缝底；下沉防共面）
    for k in range(4):                                         # 顶盘拼板：3×ROCKL + 1×ROCKM（潮池位）
        a0, a1 = math.pi / 2 * k + 0.06, math.pi / 2 * (k + 1) - 0.06
        mat = M_WETSAND if k == 2 else (M_ROCKL if k != 1 else M_ROCKM)
        quad = []
        for aa in (a0, a1):
            quad.append((cr * 0.92 * math.cos(aa), cr * 0.92 * math.sin(aa), cz))
        for aa in (a1, a0):
            quad.append((cr * 0.30 * math.cos(aa), cr * 0.30 * math.sin(aa), cz))
        acc.add(quad, [(0, 1, 2, 3)], mat, False)
    cap_fan(acc, [(cr * 0.3 * math.cos(math.pi / 2 * k / 4 + 0.4),
                   cr * 0.3 * math.sin(math.pi / 2 * k / 4 + 0.4), cz) for k in range(4)],
            cz, M_ROCKL)

    # 岩架（嵌壁岩块）+ 贝壳 + 海鸟
    for _ in range(3 if tall else 2):
        a = rng.uniform(0, 2 * math.pi)
        zz = rng.uniform(1.8, P['cap_z'] - 1.6)
        rr = radius(zz, a)
        rock_chunk(acc, rr * math.cos(a) * 1.02, rr * math.sin(a) * 1.02, zz,
                   rng.uniform(0.7, 1.2), rng.uniform(0.55, 0.9), rng.uniform(0.4, 0.7), rng, M_ROCKD)
    shell(acc, cr * 0.55, -cr * 0.3, cz - 0.01, 0.28, M_CORAL, rng)
    if tall:
        seagull(acc, cr * 0.62, cr * 0.45, cz + 0.01, math.radians(140), 1.5)
        seagull(acc, -cr * 0.5, cr * 0.55, cz + 0.01, math.radians(-100), 1.25)
    else:
        seagull(acc, cr * 0.5, -cr * 0.4, cz + 0.01, math.radians(60), 1.2)
    box_sz = cr * 2 - 0.4
    return [sbox(0, 0, P['cap_z'], box_sz, box_sz)], 'stand=[+%.1f] 节理%d道' % (P['cap_z'], P['joints'])


def build_stack_tall(acc, rng):
    return _build_stack(acc, rng, STACK_TALL, tall=True)


def build_stack_short(acc, rng):
    return _build_stack(acc, rng, STACK_SHORT, tall=False)


# ============================================================================
# ⑦ SandBarL —— 沙洲 30×8 @ +0.5（贝壳/漂木细节不超顶高）
# ============================================================================

def build_sand_bar(acc, rng):
    P = SAND_BAR
    outline = outline_with_normals(superellipse_pts(P['hw'], P['hd'], P['n_pow'], SEG_ARC + 8), rng)
    layers = [(P['top'], 1.0, 0.0, 0.0)]

    def side_mat(z):
        if z < -0.8:
            return M_SANDD
        if z < 0.4:
            return M_WETSAND
        return M_SANDM

    terrace_body(acc, outline, layers, side_mat, [M_SANDD], rng)

    # 顶面：跟随轮廓的 3 纵条分区（中脊 SANDL 亮带 + 两侧 SANDM），纵向分缝（沙脊观感）
    boxes = []

    def hw_at(x):
        u = min(1.0, abs(x) / P['hw'])
        return P['hd'] * max(0.10, (1.0 - u ** P['n_pow']) ** (1.0 / P['n_pow']))

    n_seg = 9
    bands = [(-0.98, -0.52, M_SANDM), (-0.52, 0.52, M_SANDL), (0.52, 0.98, M_SANDM)]
    for (v0, v1, mat) in bands:
        for i in range(n_seg):
            x0 = -P['hw'] + 2 * P['hw'] * i / n_seg + 0.07
            x1 = -P['hw'] + 2 * P['hw'] * (i + 1) / n_seg - 0.07
            quad = [(x0, v0 * hw_at(x0), P['top']), (x1, v0 * hw_at(x1), P['top']),
                    (x1, v1 * hw_at(x1), P['top']), (x0, v1 * hw_at(x0), P['top'])]
            acc.add(quad, [(0, 1, 2, 3)], mat, False)

    # 细节（全部嵌顶不超档）：贝壳 26 / 漂木 3 / 草 6 / 湿坑 2
    for _ in range(26):
        x = rng.uniform(-P['hw'] + 1.5, P['hw'] - 1.5)
        y = rng.uniform(-P['hd'] + 0.8, P['hd'] - 0.8) * 0.8
        shell(acc, x, y, P['top'] - 0.01, rng.uniform(0.16, 0.3),
              M_CORAL if rng.random() < 0.45 else M_SANDL, rng, rot=rng.uniform(0, 6))
    for i, xx in enumerate((-9.0, 2.5, 11.0)):
        driftwood(acc, xx, rng.uniform(-1.6, 1.6), P['top'], 2.2 + i * 0.9,
                  rng.uniform(-0.6, 0.6) + math.pi / 2, rng)
    for _ in range(6):
        grass_tuft(acc, rng.uniform(-P['hw'] * 0.7, P['hw'] * 0.7), rng.uniform(-1.8, 1.8),
                   P['top'], rng.uniform(0.35, 0.6), rng, M_GRASSM, M_GRASSD)
    wet_patch(acc, -5.5, -0.8, P['top'] - STRIP_DROP - 0.01, 1.8, 0.8, M_WETSAND)
    wet_patch(acc, 7.5, 0.9, P['top'] - STRIP_DROP - 0.01, 1.4, 0.65, M_WETSAND)
    boxes.append(sbox(0, 0, P['top'], P['hw'] * 2 - 0.4, P['hd'] * 2 - 0.4))
    return boxes, 'stand=[+0.5] 细节不超顶高'


# ============================================================================
# ⑧ ReefStepsA —— 礁阶 3 块 6×5 板 @ +0.5/+1.0/+1.5（中心距 8，净隙 2u）
# ============================================================================

def build_reef_steps(acc, rng):
    P = REEF_STEPS
    boxes = []
    for bi, (cx, zt) in enumerate(P['tops']):
        ol = outline_with_normals(superellipse_pts(P['plate'][0], P['plate'][1], 2.6, 18), rng)
        layers = [(zt, 1.0, cx, 0.0)]

        def side_mat(z, _zt=zt):
            if z < _zt - 0.9:
                return M_ROCKD
            if z < 0.42:
                return M_WETSAND
            return M_ROCKM

        terrace_body(acc, ol, layers, side_mat, [M_ROCKD], rng)
        # 顶板 3 拼块：低频分区（主面 ROCKM + 内芯 ROCKL；中块潮池 WETSAND；缺板=浪蚀缺口）
        missing = 1 if bi == 2 else (1 if bi == 0 and rng.random() < 0.6 else -1)
        for k in range(3):
            if k == missing:
                continue
            a0 = 2 * math.pi * k / 3 + 0.07
            a1 = 2 * math.pi * (k + 1) / 3 - 0.07
            mat = M_WETSAND if (bi == 1 and k == 2) else M_ROCKM
            po0, pi0 = _se_pt(P['plate'][0], P['plate'][1], 2.6, a0, 0.94, (cx, 0.0)), _se_pt(P['plate'][0], P['plate'][1], 2.6, a0, 0.30, (cx, 0.0))
            po1, pi1 = _se_pt(P['plate'][0], P['plate'][1], 2.6, a1, 0.94, (cx, 0.0)), _se_pt(P['plate'][0], P['plate'][1], 2.6, a1, 0.30, (cx, 0.0))
            quad = [(po0[0], po0[1], zt), (po1[0], po1[1], zt),
                    (pi1[0], pi1[1], zt), (pi0[0], pi0[1], zt)]
            acc.add(quad, [(0, 1, 2, 3)], mat, False)
        inner = [_se_pt(P['plate'][0], P['plate'][1], 2.6, 2 * math.pi * k / 8 + 0.35, 0.26, (cx, 0.0)) for k in range(8)]
        cap_fan(acc, [(px, py, zt) for (px, py) in inner], zt, M_ROCKL)   # 台面亮芯
        # 台面贝壳 + 草
        for _ in range(3):
            shell(acc, cx + rng.uniform(-2.2, 2.2), rng.uniform(-1.6, 1.6), zt - 0.01,
                  rng.uniform(0.18, 0.3), M_CORAL if rng.random() < 0.5 else M_ROCKL, rng)
        for _ in range(2):
            grass_tuft(acc, cx + rng.uniform(-2.0, 2.0), rng.uniform(-1.4, 1.4), zt,
                       rng.uniform(0.35, 0.6), rng, M_GRASSM, M_GRASSD)
        # 高块壁面浪蚀口袋
        if zt > 1.0:
            erosion_pocket(acc, cx - P['plate'][0] * 0.99, 0.4, zt - 0.6, 1.0, 0.5, math.pi / 2, M_ROCKD)
        boxes.append(sbox(cx, 0, zt, P['plate'][0] * 2 - 0.3, P['plate'][1] * 2 - 0.3))
    return boxes, 'stand=[+0.5,+1.0,+1.5] 中心距8净隙2'


# ============================================================================
# ⑨ MangroveHummock —— 红树墩 18×14 @ +1.0（根须裙不站）
# ============================================================================

def build_mangrove(acc, rng):
    P = MANGROVE
    outline = outline_with_normals(superellipse_pts(P['hw'], P['hd'], P['n_pow'], SEG_ARC), rng)
    layers = [(P['top'], 1.0, 0.0, 0.0)]

    def side_mat(z):
        if z < -0.5:
            return M_ROCKD
        return M_WETSAND

    terrace_body(acc, outline, layers, side_mat, [M_WETSAND], rng, bands=2)
    top_surface(acc, P['hw'], P['hd'], P['n_pow'], P['top'], banded_mats([(M_GRASSD, M_GRASSM), M_GRASSM, M_GRASSL]),
                n_seg=8, ring_scales=(0.58, 0.24))

    # 根须裙：周边拱根（顶缘外斜插进水，粗+深+拱）+ 短直根；根间泥芯已由侧壁 WetSand 承担
    n = len(outline)
    for i in range(0, n, max(1, n // P['roots'])):
        px, py, nx, ny, _j = outline[i]
        r0 = math.hypot(px, py)
        ux, uy = nx, ny
        top_p = (px + ux * 0.45, py + uy * 0.45, P['top'] - 0.2)
        mid_p = (px + ux * 1.55, py + uy * 1.55, 0.1)
        bot_p = (px + ux * 2.05, py + uy * 2.05, -1.1)
        ax_tube(acc, top_p, mid_p, 0.22, 0.15, 6, M_WOODD)
        ax_tube(acc, mid_p, bot_p, 0.15, 0.09, 6, M_WOODD)
        if i % 2 == 0:                                         # 短直根补充
            ax_tube(acc, (px + ux * 1.0, py + uy * 1.0, 0.3),
                    (px + ux * 1.4, py + uy * 1.4, -0.6), 0.09, 0.06, 5, M_WOODD)

    # 顶面密草 + 小枝 Y 叉 + 贝壳
    for _ in range(42):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0, 1) ** 0.6
        grass_tuft(acc, P['hw'] * 0.8 * rr * math.cos(a), P['hd'] * 0.8 * rr * math.sin(a),
                   P['top'], rng.uniform(0.4, 0.75), rng,
                   rng.choice([M_GRASSL, M_GRASSM, M_GRASSM]), M_GRASSD)
    for _ in range(4):
        x = rng.uniform(-P['hw'] * 0.5, P['hw'] * 0.5)
        y = rng.uniform(-P['hd'] * 0.5, P['hd'] * 0.5)
        ax_tube(acc, (x, y, P['top']), (x + 0.12, y + 0.06, P['top'] + 0.7), 0.05, 0.03, 5, M_WOODD)
        ax_tube(acc, (x + 0.12, y + 0.06, P['top'] + 0.7), (x + 0.42, y + 0.28, P['top'] + 1.15), 0.03, 0.02, 5, M_WOODD)
        ax_tube(acc, (x + 0.12, y + 0.06, P['top'] + 0.7), (x - 0.05, y + 0.4, P['top'] + 1.05), 0.03, 0.02, 5, M_WOODD)
    for _ in range(5):
        shell(acc, rng.uniform(-5, 5), rng.uniform(-4, 4), P['top'] - 0.01,
              rng.uniform(0.12, 0.2), M_CORAL, rng)
    return [sbox(0, 0, P['top'], P['hw'] * 2 - 0.4, P['hd'] * 2 - 0.4)], 'stand=[+1.0] 根须裙不站'


# ============================================================================
# ⑩ VolcanoRimA —— 火山缘环（外径 64，缘 +3.5/+4.5，~12u 缺口，内坪 +0.5）
# ============================================================================

def build_volcano(acc, rng):
    P = VOLCANO
    gap0, gap1 = P['gap_c'] - P['gap_half'], P['gap_c'] + P['gap_half']
    n_arc = SEG_ARC_BIG
    outline = outline_with_normals(annulus_outline(P['r_out'], P['r_in'], gap1, gap0 + 360.0, n_arc), rng)
    z_lo = P['top_lo']
    layers = [(z_lo, 1.0, 0.0, 0.0)]

    def side_mat(z):
        if z < -0.7:
            return M_ROCKD
        if z < 0.42:
            return M_WETSAND
        return M_ROCKM

    terrace_body(acc, outline, layers, side_mat, [M_ROCKD], rng)

    # hi 段：+4.5 再升一层（实体弧段体：侧壁 loft 到 lo 顶 + 顶盖，高低+明暗双编码）
    boxes = []
    mid_r = (P['r_out'] + P['r_in']) / 2.0
    for (h0, h1) in P['hi_spans']:
        n_seg = max(3, int((h1 - h0) / 22.0))
        for k in range(n_seg):
            b0 = math.radians(h0 + (h1 - h0) * k / n_seg + 0.7)
            b1 = math.radians(h0 + (h1 - h0) * (k + 1) / n_seg - 0.7)
            mid = (b0 + b1) / 2
            r_o, r_i = P['r_out'] - 0.4, P['r_in'] + 0.4
            quad = [(r_o * math.cos(b0), r_o * math.sin(b0), P['top_hi']),
                    (r_o * math.cos(b1), r_o * math.sin(b1), P['top_hi']),
                    (r_i * math.cos(b1), r_i * math.sin(b1), P['top_hi']),
                    (r_i * math.cos(b0), r_i * math.sin(b0), P['top_hi'])]
            acc.add(quad, [(0, 1, 2, 3)], M_ROCKL, False)
            lo_ring = [(r_o * math.cos(b0), r_o * math.sin(b0), P['top_lo']),
                       (r_o * math.cos(b1), r_o * math.sin(b1), P['top_lo']),
                       (r_i * math.cos(b1), r_i * math.sin(b1), P['top_lo']),
                       (r_i * math.cos(b0), r_i * math.sin(b0), P['top_lo'])]
            hi_ring = [(px, py, P['top_hi']) for (px, py, _z) in lo_ring]
            loft_closed(acc, lo_ring, hi_ring, M_ROCKM)        # 外壁/内壁/两端面一次成形
            chord = 2 * mid_r * math.sin((b1 - b0) / 2)
            boxes.append(sbox(mid_r * math.cos(mid), mid_r * math.sin(mid), P['top_hi'], chord, 11.5,
                              math.degrees(mid)))
            # Ember 余温裂纹（缘顶内侧双条）
            for off_r in (2.0, 3.4):
                aa = [((P['r_in'] + off_r) * math.cos(b0 + (b1 - b0) * f),
                       (P['r_in'] + off_r) * math.sin(b0 + (b1 - b0) * f), P['top_hi'])
                      for f in (0.05, 0.5, 0.95)]
                ground_strip(acc, aa, 0.3, P['top_hi'], M_EMBER)

    # lo 段顶板（+3.5）：全环减 hi 段与缺口（10° 步进扫描分段）
    spans = []
    a = gap1
    step = 10.0
    in_hi = None
    seg_start = a
    while a < gap0 + 360.0:
        is_hi = any(h0 - 2 <= (a % 360) <= h1 + 2 for (h0, h1) in P['hi_spans'])
        if in_hi is None:
            in_hi = is_hi
        elif is_hi != in_hi:
            spans.append((seg_start, a, in_hi))
            seg_start = a
            in_hi = is_hi
        a += step
    spans.append((seg_start, gap0 + 360.0, in_hi))
    for (s0, s1, is_hi) in spans:
        if is_hi:
            continue
        span = s1 - s0
        if span < 14.0:                                    # 短缝段（弦<5.5m）不可立足：不建板/box
            continue
        n_seg = max(1, int(span / 26.0))
        for k in range(n_seg):
            b0 = math.radians(s0 + span * k / n_seg + 0.7)
            b1 = math.radians(s0 + span * (k + 1) / n_seg - 0.7)
            mid = (b0 + b1) / 2
            r_o, r_i = P['r_out'] - 0.4, P['r_in'] + 0.4
            quad = [(r_o * math.cos(b0), r_o * math.sin(b0), P['top_lo']),
                    (r_o * math.cos(b1), r_o * math.sin(b1), P['top_lo']),
                    (r_i * math.cos(b1), r_i * math.sin(b1), P['top_lo']),
                    (r_i * math.cos(b0), r_i * math.sin(b0), P['top_lo'])]
            acc.add(quad, [(0, 1, 2, 3)], M_ROCKM, False)      # lo 段统一中岩色（ROCKL=高位编码）
            for p0, p1 in ((quad[0], quad[1]), (quad[2], quad[3]), (quad[0], quad[3]), (quad[1], quad[2])):
                acc.add([p0, p1, (p1[0], p1[1], P['top_lo'] - TOP_T), (p0[0], p0[1], P['top_lo'] - TOP_T)],
                        [(0, 1, 2, 3)], M_ROCKD, False)
            chord = 2 * mid_r * math.sin((b1 - b0) / 2)
            boxes.append(sbox(mid_r * math.cos(mid), mid_r * math.sin(mid), P['top_lo'], chord, 11.5,
                              math.degrees(mid)))

    # 外缘柱状节理（玄武柱群：外移半露、加高到近缘顶 → 锯齿壁读感）
    for k in range(P['n_pillars']):
        deg = gap1 + (gap0 + 360.0 - gap1) * (k + rng.uniform(0.25, 0.75)) / P['n_pillars']
        a = math.radians(deg)
        zz = rng.uniform(1.2, 3.2)
        pillar(acc, (P['r_out'] + 0.55) * math.cos(a), (P['r_out'] + 0.55) * math.sin(a),
               zz - rng.uniform(2.4, 3.6), zz, rng.uniform(0.5, 0.7), 6, M_ROCKD, rng)

    # 内坪（礁坪 +0.5）：圆 mound + 顶面环形拼板 + 潮池 + 裂纹
    fl = outline_with_normals(superellipse_pts(P['r_in'], P['r_in'], 2.0, 30))

    def floor_side(z):
        return M_ROCKD if z < -0.7 else M_WETSAND

    terrace_body(acc, fl, [(P['floor_top'], 1.0, 0.0, 0.0)], floor_side, [M_ROCKD], rng, bands=2)
    top_surface(acc, P['r_in'], P['r_in'], 2.0, P['floor_top'], banded_mats([(M_SANDD, M_ROCKM), M_SANDD, M_ROCKM]),
                n_seg=8, ring_scales=(0.6, 0.26))
    for wx, wy, wrx in ((-6.0, 3.0, 2.6), (5.5, -4.0, 2.2)):
        wet_patch(acc, wx, wy, P['floor_top'] - STRIP_DROP - 0.01, wrx, wrx * 0.62, M_WETSAND)
    for _ in range(3):
        a = rng.uniform(0, 2 * math.pi)
        aa = [((P['r_in'] - 3.5) * math.cos(a + f), (P['r_in'] - 3.5) * math.sin(a + f), P['floor_top'])
              for f in (0, 0.7, 1.4)]
        ground_strip(acc, aa, 0.13, P['floor_top'], M_ROCKD)
    for _ in range(4):
        shell(acc, rng.uniform(-8, 8), rng.uniform(-8, 8), P['floor_top'] - 0.01,
              rng.uniform(0.13, 0.22), M_CORAL, rng)
    # 浮石（缘顶亮色小块）
    for _ in range(6):
        deg = rng.uniform(gap1 + 6, gap0 + 354)
        a = math.radians(deg)
        zz = P['top_hi'] if any(h0 <= (deg % 360) <= h1 for (h0, h1) in P['hi_spans']) else P['top_lo']
        rock_chunk(acc, (mid_r + rng.uniform(-2, 2)) * math.cos(a), (mid_r + rng.uniform(-2, 2)) * math.sin(a),
                   zz - 0.02, 0.4, 0.35, 0.4, rng, M_ROCKL)
    # 内坪 manifest：R20 内接 box
    boxes.append(sbox(0, 0, P['floor_top'], 27.6, 27.6))
    return boxes, 'stand=[+3.5,+4.5 分段yaw, +0.5内坪]'


# ============================================================================
# ⑪ TurtleShellIsle —— 巨龟背甲岛（甲板 4 档 + 鳞纹 + 四鳍 + 首尾）
# ============================================================================

def build_turtle(acc, rng):
    P = TURTLE
    base = P['rings'][0]
    outline = outline_with_normals(superellipse_pts(base[1], base[2], P['n_pow'], SEG_ARC_BIG), rng)
    layers = [(z, hw / base[1], cx, cy) for (z, hw, hd, cx, cy) in P['rings']]

    def side_mat(z):
        if z < -0.7:
            return M_ROCKD
        if z < 0.42:
            return M_SANDD
        return M_ROCKM

    terrace_body(acc, outline, layers, side_mat, [M_ROCKD, M_ROCKD, M_ROCKL], rng)

    # 甲板顶面（三层环拼板，岩色三档交替 = 甲片底色）
    boxes = []
    for li, (z, sc, cx, cy) in enumerate(layers):
        hw, hd = P['rings'][li][1], P['rings'][li][2]
        ol = outline if li == 0 else outline_with_normals(
            superellipse_pts(hw, hd, P['n_pow'], SEG_ARC), rng)
        top_surface(acc, hw, hd, P['n_pow'], z,
                     lambda zone, k, _l=li: (M_ROCKM, M_ROCKD, M_ROCKL)[zone] if (k + _l) % 2 == 0
                     else (M_ROCKL, M_ROCKM, M_ROCKM)[zone],
                     n_seg=10 if li == 0 else 6, ring_scales=(0.6, 0.26), off=(cx, cy))
        # 鳞纹六边形凸片（两圈六方格排布，加大加密 = 龟甲身份符号）
        if li < 2:
            n_out = 8 if li == 0 else 6
            r_out, r_in = (0.74, 0.45) if li == 0 else (0.62, 0.34)
            for ring_i, (rr, n_h) in enumerate(((r_out, n_out), (r_in, max(4, n_out - 2)))):
                for k in range(n_h):
                    a = 2 * math.pi * k / n_h + 0.35 * li + 0.22 * ring_i
                    hx = cx + hw * rr * math.cos(a)
                    hy = cy + hd * rr * math.sin(a)
                    hex_scale(acc, hx, hy, z, 1.55 - 0.2 * li - 0.15 * ring_i, 0.15,
                              M_ROCKL if (k + ring_i) % 2 == 0 else M_ROCKM, rot=a)
        else:
            for k in range(5):                                 # 中央台大鳞
                a = 2 * math.pi * k / 5 + 0.5
                hex_scale(acc, cx + hw * 0.42 * math.cos(a), cy + hd * 0.42 * math.sin(a),
                          z, 1.9, 0.16, M_ROCKL if k % 2 == 0 else M_ROCKM, rot=a)

    # 脊台 12×4.4 @ +3.5（叠在中央台上）
    sz, s_hw, s_hd, s_cx, s_cy = P['spine']
    spine_ol = outline_with_normals(superellipse_pts(s_hw, s_hd, 2.2, 16), rng)
    terrace_body(acc, spine_ol, [(sz, 1.0, s_cx, s_cy)], lambda z: M_ROCKM, [M_ROCKD], rng,
                 skirt=False, bands=2)
    top_surface(acc, s_hw, s_hd, 2.2, sz, lambda zone, k: M_ROCKL if k % 2 == 0 else M_ROCKM,
                n_seg=5, ring_scales=(0.5,), off=(s_cx, s_cy))
    for k in range(4):                                         # 脊线鳞片
        hex_scale(acc, s_cx - 4.2 + k * 2.8, s_cy, sz, 1.05, 0.14, M_ROCKM, rot=math.pi / 2)

    # 四鳍岩（桨形 6.6×4.6 视觉 / manifest 5×4 @ +1.0，3 爪尖）
    for (fx, fy, fyaw) in P['fins']:
        a = math.radians(fyaw)
        ol = outline_with_normals(_rot_pts(superellipse_pts(3.3, 2.3, 2.2, 14), a), rng)
        terrace_body(acc, ol, [(1.0, 1.0, fx, fy)], lambda z: M_ROCKM, [M_ROCKD], rng,
                     skirt=False, bands=1)
        top_surface(acc, 3.3, 2.3, 2.2, 1.0, banded_mats([(M_ROCKM, M_ROCKL), M_ROCKL, M_ROCKM], n_out=4),
                    n_seg=4, ring_scales=(0.5,), off=(fx, fy), rot=a)
        for claw in (-1.2, 0.0, 1.2):                          # 爪尖（沿桨轴末端横向排布）
            ca, sa = math.cos(a), math.sin(a)
            rock_chunk(acc, fx + ca * 3.7 - sa * claw, fy + sa * 3.7 + ca * claw, 0.5,
                       0.5, 0.45, 1.0, rng, M_ROCKD, n=5)
        boxes.append(sbox(fx, fy, 1.0, 5.0, 4.0, fyaw))

    # 颈桥（连体低台，让首岩从"浮岛"变"探出的头"）
    ax_box(acc, (0.0, -17.6, 0.35), (2.6, 4.2, 1.3), M_ROCKM)

    # 龟首岩 6×6 @ +2.0（颈褶 + 眼窝）+ 尾 4×4 @ +1.0
    hx, hy, hz = P['head']
    head_ol = outline_with_normals(superellipse_pts(3.0, 3.0, 2.6, 16), rng)
    terrace_body(acc, head_ol, [(hz, 1.0, hx, hy)], lambda z: M_ROCKM, [M_ROCKD], rng, skirt=False, bands=2)
    top_surface(acc, 3.0, 3.0, 2.6, hz, banded_mats([(M_ROCKM, M_ROCKL), M_ROCKL, M_ROCKM], n_out=4),
                n_seg=4, ring_scales=(0.5,), off=(hx, hy))
    for ex in (-1.3, 1.3):                                     # 眼窝（暗凹块）
        ax_box(acc, (hx + ex, hy - 2.7, hz - 0.55), (1.05, 0.7, 0.95), M_ROCKD)
    ax_box(acc, (hx, hy - 3.35, hz - 1.05), (2.2, 0.95, 1.1), M_ROCKM)   # 吻部
    for k in range(2):                                         # 颈褶环
        ax_tube(acc, (hx, hy - 4.4 + k * 0.8, 0.9 - k * 0.15), (hx, hy - 3.9 + k * 0.8, 1.15 - k * 0.15),
                2.2 - k * 0.15, 2.1 - k * 0.15, 12, M_ROCKM)
    boxes.append(sbox(hx, hy, hz, 6.0, 6.0))
    tx, ty, tz = P['tail']
    tail_ol = outline_with_normals(superellipse_pts(2.0, 2.0, 2.4, 12), rng)
    terrace_body(acc, tail_ol, [(tz, 1.0, tx, ty)], lambda z: M_ROCKM, [M_ROCKD], rng, skirt=False, bands=1)
    top_surface(acc, 2.0, 2.0, 2.4, tz, lambda zone, k: M_ROCKM, n_seg=4, ring_scales=(0.5,), off=(tx, ty))
    boxes.append(sbox(tx, ty, tz, 4.0, 4.0))

    # 甲缘唇边已由外环立面承担；细节：草簇 / 贝壳 / 擦伤痕
    for _ in range(10):
        a = rng.uniform(0, 2 * math.pi)
        grass_tuft(acc, base[1] * 0.5 * math.cos(a), base[2] * 0.5 * math.sin(a),
                   layers[0][0], rng.uniform(0.26, 0.44), rng, M_GRASSM, M_GRASSD)
    for _ in range(6):
        shell(acc, rng.uniform(-10, 10), rng.uniform(-7, 7), layers[0][0] - 0.01,
              rng.uniform(0.13, 0.22), M_CORAL, rng)
    for (bx, by) in ((-6.0, 5.0), (7.5, -3.0)):
        ground_strip(acc, [(bx, by), (bx + 1.8, by + 0.7), (bx + 3.4, by + 0.2)], 0.5,
                     layers[0][0], M_SANDM)
    boxes.insert(0, sbox(0, 0, layers[0][0], base[1] * 2 - 0.4, base[2] * 2 - 0.4))
    mid = P['rings'][1]
    boxes.insert(1, sbox(mid[3], mid[4], mid[0], mid[1] * 2 - 0.4, mid[2] * 2 - 0.4))
    cen = P['rings'][2]
    boxes.insert(2, sbox(cen[3], cen[4], cen[0], cen[1] * 2 - 0.4, cen[2] * 2 - 0.4))
    boxes.append(sbox(s_cx, s_cy, sz, s_hw * 2, s_hd * 2))
    return boxes, 'stand=[+1.0,+2.0,+3.0,+3.5脊,+1.0鳍x4,+2.0首,+1.0尾]'


# ============================================================================
# ⑫ SunkenPlaza —— 沉没广场 28×28 @ +0.5（咬口 + 石缝分格 + 中央下沉圆池）
# ============================================================================

def build_plaza(acc, rng):
    P = PLAZA
    # 咬口轮廓：基本 superellipse + 3 处定点深咬
    pts = superellipse_pts(P['hw'], P['hd'], P['n_pow'], SEG_ARC)
    bites = [(25.0, 2.2), (140.0, 2.6), (255.0, 1.8)]          # (角度, 咬深)
    for i in range(len(pts)):
        a = math.degrees(math.atan2(pts[i][1], pts[i][0])) % 360.0
        for (ba, bd) in bites:
            d = abs((a - ba + 180) % 360 - 180)
            if d < 26:
                f = 1.0 - d / 26.0
                k = max(0.30, 1.0 - bd * (f ** 1.6) * (0.85 + 0.3 * math.sin(i * 2.7)))  # 防负缩放自交
                pts[i] = (pts[i][0] * k, pts[i][1] * k)
    outline = outline_with_normals(pts, rng)
    layers = [(P['top'], 1.0, 0.0, 0.0)]

    def side_mat(z):
        if z < -0.7:
            return M_ROCKD
        if z < 0.42:
            return M_WETSAND
        return M_ROCKM

    terrace_body(acc, outline, layers, side_mat, [M_ROCKD], rng)

    # 铺石：7×7 网格板（板间缝露衬底；池区/缺板让位；池边 2 块微倾板）
    cell = 2 * P['hw'] / P['grid']
    missing = {(1, 5), (5, 1), (0, 2)}                         # 缺板=坑
    tilted = {(3, 0), (5, 5)}                                  # 池边微倾（低于顶，不入碰撞语义）
    for gx in range(P['grid']):
        for gy in range(P['grid']):
            cx0 = -P['hw'] + cell * (gx + 0.5)
            cy0 = -P['hd'] + cell * (gy + 0.5)
            if math.hypot(cx0, cy0) < P['pool_r'] + cell * 0.42:
                continue                                       # 池区让位
            if (gx, gy) in missing:
                continue
            half = cell / 2 - 0.16
            corners = [(cx0 - half, cy0 - half), (cx0 + half, cy0 - half),
                       (cx0 + half, cy0 + half), (cx0 - half, cy0 + half)]
            if (gx, gy) in tilted:
                quad = [(x, y, P['top'] - 0.07) for (x, y) in corners]
                mat = M_ROCKD
            else:
                quad = [(x, y, P['top']) for (x, y) in corners]
                shade = (gx + gy) / (2.0 * (P['grid'] - 1))    # 对角明度渐变（低频有序）
                mat = M_ROCKL if shade < 0.34 else (M_ROCKM if shade < 0.68 else M_SANDD)
            acc.add(quad, [(0, 1, 2, 3)], mat, False)
            if (gx, gy) not in tilted:
                for k in range(4):                             # 板缘立面（缝视深）
                    p0, p1 = quad[k], quad[(k + 1) % 4]
                    acc.add([p0, p1, (p1[0], p1[1], P['top'] - TOP_T), (p0[0], p0[1], P['top'] - TOP_T)],
                            [(0, 1, 2, 3)], mat, False)

    # 中央下沉圆池：池口檐圈（12 段）+ 池壁下到 pool_z + 池底 + 渗水湿面
    pr = P['pool_r']
    ring_hi = [(pr * math.cos(2 * math.pi * k / 12), pr * math.sin(2 * math.pi * k / 12), P['top'])
               for k in range(12)]
    ring_lo = [(p[0] * 0.94, p[1] * 0.94, P['pool_z']) for p in ring_hi]
    loft_closed(acc, ring_hi, ring_lo, M_ROCKM)
    pool_bot = [(p[0] * 0.94, p[1] * 0.94, P['pool_z']) for p in ring_hi]
    cap_fan(acc, pool_bot, P['pool_z'], M_ROCKD)
    water = [(p[0] * 0.9, p[1] * 0.9, P['pool_z'] + 0.47) for p in ring_hi]
    cap_fan(acc, water, P['pool_z'] + 0.47, M_WETSAND)         # 渗水浅面（近池口，湿沙语汇）

    # 断柱 ×2（斜倚池边，装饰无碰撞）
    for (px, py, lean, rot) in ((5.8, -1.2, 0.22, 0.5), (-6.3, 2.0, -0.16, 2.2)):
        bx = px + math.sin(lean) * 0.8
        for seg in range(3):
            h0 = 0.45 + seg * 1.0
            ox = px + math.sin(lean) * h0
            oy = py - math.cos(lean) * 0.06 * h0
            ax_tube(acc, (ox, oy, h0), (ox + math.sin(lean) * 0.9, oy - 0.02, h0 + 0.96),
                    0.55 - seg * 0.05, 0.52 - seg * 0.05, 10, M_ROCKL if seg % 2 == 0 else M_ROCKM)
        ax_box(acc, (px, py, 0.16), (1.6, 1.6, 0.36), M_ROCKM, rot_z=rot)

    # 石缝草 / 贝壳 / 湿斑
    for (gx, gy) in ((2, 5), (0, 4), (6, 3)):
        cx0 = -P['hw'] + cell * (gx + 0.5)
        cy0 = -P['hd'] + cell * (gy + 0.5)
        grass_tuft(acc, cx0 + cell * 0.42, cy0 - cell * 0.42, P['top'], 0.45, rng, M_GRASSM, M_GRASSD)
    for _ in range(5):
        shell(acc, rng.uniform(-11, 11), rng.choice([-1, 1]) * rng.uniform(6, 11), P['top'] - 0.01,
              rng.uniform(0.18, 0.28), M_CORAL, rng)
    wet_patch(acc, 9.0, 6.5, P['top'] - STRIP_DROP - 0.01, 1.8, 1.1, M_WETSAND)
    wet_patch(acc, -9.5, -7.0, P['top'] - STRIP_DROP - 0.01, 1.4, 1.0, M_WETSAND)

    # manifest：池带（|y|<band）留空 → 南北两块（池=连海渗水池，落入按落水处理）
    band = P['pool_r'] + 0.4
    sy = (P['hd'] - band) - 0.4
    return [sbox(0, -(P['hd'] + band) / 2, P['top'], P['hw'] * 2 - 0.4, sy),
            sbox(0, (P['hd'] + band) / 2, P['top'], P['hw'] * 2 - 0.4, sy)], \
        'stand=[+0.5 南北两块，池带无碰撞]'


# ============================================================================
# 资产注册与主流程
# ============================================================================

ASSETS = [
    ('AtollArcA', build_atoll_arc),
    ('AtollCore', build_atoll_core),
    ('TerraceIslandL', build_terrace_l),
    ('TerraceIslandM', build_terrace_m),
    ('SeaStackTall', build_stack_tall),
    ('SeaStackShort', build_stack_short),
    ('SandBarL', build_sand_bar),
    ('ReefStepsA', build_reef_steps),
    ('MangroveHummock', build_mangrove),
    ('VolcanoRimA', build_volcano),
    ('TurtleShellIsle', build_turtle),
    ('SunkenPlaza', build_plaza),
]


def build_one(name, builder, mats, scene, do_render):
    rng = random.Random((SEED0 ^ zlib.crc32(name.encode())) & 0xFFFFFFFF)
    acc = MeshAcc()
    boxes, extra = builder(acc, rng)
    nv, np_, ntr = acc.counts()
    log('%s raw: verts=%d polys=%d tris=%d' % (name, nv, np_, ntr))
    obj = join_to_object(acc, name, mats)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ST.budget_guard(name, [obj], 'terrain')                    # STAT 行 + 15k 预算卡 + ≤8 槽卡
    fbx_path = os.path.join(FBX_DIR, name + '.fbx')
    ST.export_fbx([obj], fbx_path)
    log('exported %s (%d bytes)' % (fbx_path, os.path.getsize(fbx_path)))
    json_path = ST.write_standable_manifest(name, boxes, fbx_path)
    tops = sorted({round(b['c'][2] + b['s'][2] / 2.0, 2) for b in boxes})
    log('MANIFEST %s: %d boxes tops=%s yawed=%d -> %s'
        % (name, len(boxes), tops, sum(1 for b in boxes if b.get('yaw')), json_path))
    if do_render:
        render_views(scene, obj, name, PREVIEW_DIR)
    me = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.meshes.remove(me)


def main():
    parse_args()
    for d in (FBX_DIR, PREVIEW_DIR, WORK_DIR):
        os.makedirs(d, exist_ok=True)

    for obj in list(bpy.data.objects):                         # 清 factory 场景（幂等）
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    mats = make_kit_materials()
    if RENDER:
        setup_render(scene)
        setup_preview_world_compat(scene)                      # ST 灰底三灯（5.2.1 兼容版）
        aim_preview_lights(scene)

    built = 0
    for name, builder in ASSETS:
        if ONLY not in ('all', name):
            continue
        log('== %s ==' % name)
        build_one(name, builder, mats, scene, RENDER)
        built += 1
    if RENDER:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, 'archipelago_debug.blend'))
    log('done: %d assets in %.1fs' % (built, time.time() - T0))


if __name__ == '__main__':
    main()
