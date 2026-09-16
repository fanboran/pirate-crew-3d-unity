# -*- coding: utf-8 -*-
"""props_kit.py —— WorldKit 道具套件 18 件无头建模(Blender 5.2,纯 bpy/bmesh 程序化,零贴图,shade_flat)。

分工出处:docs/M4-大海域世界化.md §4.3(道具域 = tools/blender/scene/props/)。
风格参数唯一来源:style_tokens.py(ST.SLOTS / POLY_BUDGETS / export_fbx / PREVIEW_*)——
本脚本不散写任何色值/粗糙度/导出参数;金属度按 docs/美术风格指南.md §3.1 材质参数总表(铁 0.85 / 黄铜 1.0)。
管线样板:tools/blender/scene/build_scene_kit.py(MeshAcc / join_to_object / 三灯预览 / Standard 视图变换)。

【硬口径】
- 1 单位 = 1 米,Z 朝上;每件原点 = 落地接触面中心、底面 Z=0,整体向上生长,X/Y 居中;
- 全部面 shade_flat:细节全靠几何(木板错缝、绳圈盘绕、树干分节棱化、叶簇分层、篝火石圈、断裂斜口);
- 材质槽只用 ST.SLOTS 已登记槽,每件 ≤8(ST 断言);相邻异质部件粗糙度差按 §3.2 尽量取大,
  同族三档差由 §2.1 明度纪律(亮:中:暗 ≈ 1:0.75:0.5)承担——逐件配对表见 props/README.md;
- 面数预算:prop ≤3000 tri / prop_small ≤600 tri(ST.POLY_BUDGETS;RockM/RockFlat/Driftwood/CrateStack/
  RuinColumnBroken 按任务书"升 prop"档);
- 道具无站面 manifest、无碰撞(纯视觉,WorldMapComposer 统一摆)。

复现(仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行):
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/props/props_kit.py -- [--only PalmTall,RockS|all] [--samples N] [--no-render]

产物(重跑幂等覆盖):
    pirate-crew/Assets/Art/Models/WorldKit/Props/<名>.fbx    ×18
    export/worldkit-props/<名>-front34.jpg / <名>-side.jpg   (1024² q90,view_transform=Standard)
    external/worldkit-props-work/props_debug.blend           (调参 GUI 缓存,gitignored)

控制台每件打印一行 STAT(三角面数 / 材质槽 / 包围盒),README 引用。
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

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))                       # -> tools/blender/scene/
import style_tokens as ST                                       # noqa: E402

# ============================================================================
# 参数区 —— 调参旋钮集中在此(props/README.md 按行号引用)
# ============================================================================

ONLY = 'all'                 # 'all' | 逗号分隔的资产名(大小写不敏感),如 'PalmTall,RockS'
SAMPLES = 64                 # Cycles 采样数(CPU 回退时自动减半再减半,保出图速度)
RENDER = True                # False = 只建模导出 FBX,不出预览图
RES = ST.PREVIEW_RES         # 1024
JPG_QUALITY = ST.PREVIEW_QUALITY  # 90
LENS = 50                    # 预览相机焦距(mm)

# ---- 造型总旋钮(全局比例感)----
TRUNK_SIDES = 10             # 棕榈树干棱数(棱线密度)
FROND_SEGS = 7               # 棕榈叶折带段数
ROCK_SIDES = 11              # 大岩环向分段(节理棱面密度)
ROCK_RINGS = 7               # 大岩垂向环数

FBX_DIR = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..',
                                       'pirate-crew', 'Assets', 'Art', 'Models', 'WorldKit', 'Props'))
PREVIEW_DIR = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..',
                                           'export', 'worldkit-props'))
WORK_DIR = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..',
                                        'external', 'worldkit-props-work'))

T0 = time.time()


def log(msg):
    print('[propskit] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


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
# MeshAcc:几何累积器(样板同款;mat 直接用 ST 槽名,join 时压缩成实际用到的槽)
# ============================================================================

class MeshAcc:
    def __init__(self):
        self.verts = []
        self.faces = []        # 每项 = 顶点下标元组(3~n 边形)
        self.face_mat = []     # 每项 = ST 槽名
        self.face_smooth = []  # 本 kit 恒 False(shade_flat 铁律)

    def add(self, verts, faces, mat, smooth=False):
        base = len(self.verts)
        self.verts.extend(tuple(v) for v in verts)
        for f in faces:
            self.faces.append(tuple(base + i for i in f))
            self.face_mat.append(mat)
            self.face_smooth.append(smooth)

    def counts(self):
        tris = sum(len(f) - 2 for f in self.faces)
        return len(self.verts), len(self.faces), tris


def join_to_object(acc, obj_name, mats):
    """MeshAcc → 单网格对象;只挂实际用到的槽(不背空槽)。"""
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
            pass                                                    # 重复面(理论不出现)跳过
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])            # 法线朝外/岛内一致
    bm.to_mesh(me)
    bm.free()
    for name in used:
        me.materials.append(mats[name])
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ============================================================================
# 图元 helper(全部 flat;坐标已是资产局部系:底面 Z=0、X/Y 居中)
# ============================================================================

def ax_box(acc, center, size, mat, rot_z=0.0):
    """轴对齐盒(可绕 z 旋转)。center=中心,size=(sx,sy,sz)。"""
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


def ax_tube(acc, p0, p1, r1, r2, sides, mat, cap0=True, cap1=True):
    """圆台/圆柱杆:p0→p1 轴线,r1/r2 = 两端半径。flat 下低分段即棱面杆。"""
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
    pts = []
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        w = u * math.cos(a) + v * math.sin(a)
        pts.append(c0 + w * r1)
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        w = u * math.cos(a) + v * math.sin(a)
        pts.append(c1 + w * r2)
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
    if cap0:
        faces.append(tuple(range(sides - 1, -1, -1)))
    if cap1:
        faces.append(tuple(range(sides, 2 * sides)))
    acc.add(pts, faces, mat)


def _torus(acc, center, major, minor, ma, mi, mat, plane='xz'):
    """圆环面(舵轮外环/绳圈)。plane='xz' 立环(轮), 'xy' 平环(盘绕)。"""
    cx, cy, cz = center
    pts = []
    for i in range(ma):
        a = 2.0 * math.pi * i / ma
        for j in range(mi):
            b = 2.0 * math.pi * j / mi
            rr = major + minor * math.cos(b)
            if plane == 'xz':
                pts.append((cx + rr * math.cos(a), cy + minor * math.sin(b), cz + rr * math.sin(a)))
            else:
                pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a), cz + minor * math.sin(b)))
    faces = []
    for i in range(ma):
        i2 = (i + 1) % ma
        for j in range(mi):
            j2 = (j + 1) % mi
            faces.append((i * mi + j, i2 * mi + j, i2 * mi + j2, i * mi + j2))
    acc.add(pts, faces, mat)


def _berry(acc, center, r, mat, sides=6):
    """低模球(椰果/装饰果):中环 + 上下极点。"""
    cx, cy, cz = center
    pts = [(cx, cy, cz - r)]
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r, cz))
    pts.append((cx, cy, cz + r))
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((0, 1 + k, 1 + k2))
        faces.append((1 + sides, 1 + k2, 1 + k))
    acc.add(pts, faces, mat)


def _rock(acc, cx, cy, sx, sy, sz, seed, rings=5, sides=10, family='Kit_Rock',
          dome=0.07, base_grow=0.82, top_mat=None, top_wobble=0.0):
    """节理岩体:分层环放样 + 固定种子噪声位移;顶环压平成微凸穹(dome)。
    侧壁按面高自动落岩三档(底暗/中灰/顶亮,§2.1 明度纪律);无底面(贴地不可见)。
    top_wobble:顶环 z 抖动幅(宽扁岩顶面破碎感用)。"""
    rng = random.Random(seed)
    m_dark, m_mid, m_light = family + 'Dark', family + 'Mid', family + 'Light'
    ring_pts = []
    for k in range(rings):
        t = k / (rings - 1)
        coeff = 0.55 + 0.45 * math.sin(math.pi * (t ** 0.85))       # 底收-中鼓-顶收
        if k == 0:
            coeff *= base_grow
        if k == 0:
            z = 0.0
        elif k == rings - 1:
            z = sz * (1.0 - dome) + sz * top_wobble * rng.uniform(-1.0, 1.0)
        else:
            z = sz * t * (1.0 + rng.uniform(-0.05, 0.05))
        pts = []
        for i in range(sides):
            a = 2.0 * math.pi * (i + rng.uniform(-0.32, 0.32)) / sides
            rr = coeff * (1.0 + rng.uniform(-0.22, 0.22))
            pts.append((cx + math.cos(a) * rr * sx * 0.5,
                        cy + math.sin(a) * rr * sy * 0.5, z))
        ring_pts.append(pts)
    top_c = (cx + rng.uniform(-0.04, 0.04) * sx, cy + rng.uniform(-0.04, 0.04) * sy, sz)
    for k in range(rings - 1):
        for i in range(sides):
            i2 = (i + 1) % sides
            q = [ring_pts[k][i], ring_pts[k][i2], ring_pts[k + 1][i2], ring_pts[k + 1][i]]
            zr = (q[0][2] + q[1][2] + q[2][2] + q[3][2]) / (4.0 * sz)
            m = m_dark if zr < 0.45 else (m_mid if zr < 0.75 else m_light)
            acc.add(q, [(0, 1, 2, 3)], m)
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([ring_pts[-1][i], ring_pts[-1][i2], top_c], [(0, 1, 2)], top_mat or m_light)


def _palm_frond(acc, base, yaw_deg, elev0_deg, length, width, mat,
                segs=FROND_SEGS, droop=60.0, fold=0.30, taper=0.16,
                rib=False, rib_mat=None):
    """棕榈式叶片:V 形折带(先扬后垂的弯)+ 可选中肋杆;末端自然收尖。"""
    yr = math.radians(yaw_deg)
    er0 = math.radians(elev0_deg)
    drp = math.radians(droop)
    dh = Vector((math.cos(yr), math.sin(yr), 0.0))
    sd = Vector((-math.sin(yr), math.cos(yr), 0.0))
    up = Vector((0, 0, 1))
    secs = []
    p = Vector(base)
    for k in range(segs + 1):
        f = k / segs
        elev = er0 - drp * (f ** 1.3)
        d = dh * math.cos(elev) + up * math.sin(elev)
        if k > 0:
            p = p + d * (length / segs)
        w = width * (1.0 - (1.0 - taper) * (f ** 1.15))
        secs.append((p.copy(), max(w, width * taper)))
    for k in range(segs):
        (p0, w0), (p1, w1) = secs[k], secs[k + 1]
        l0 = p0 - sd * (w0 * 0.5); r0 = p0 + sd * (w0 * 0.5); m0 = p0 + up * (w0 * fold)
        l1 = p1 - sd * (w1 * 0.5); r1 = p1 + sd * (w1 * 0.5); m1 = p1 + up * (w1 * fold)
        acc.add([tuple(l0), tuple(m0), tuple(m1), tuple(l1)], [(0, 1, 2, 3)], mat)
        acc.add([tuple(m0), tuple(r0), tuple(r1), tuple(m1)], [(0, 1, 2, 3)], mat)
    if rib:
        rm = rib_mat or mat
        n_rib = max(2, segs // 3)
        for k in range(n_rib):
            i0 = int(k * segs / n_rib)
            i1 = min(int((k + 1) * segs / n_rib), segs)
            rr = 0.030 * (1.0 - 0.6 * k / n_rib)
            ax_tube(acc, tuple(secs[i0][0]), tuple(secs[i1][0]), rr, rr * 0.8, 4, rm,
                    cap0=(k == 0), cap1=(k == n_rib - 1))


def _sandbag(acc, center, yaw, length, rx, rz, mat, sides=7):
    """躺倒鼓沙袋:水平轴线沿 yaw,两端收口枕形。"""
    ca, sa = math.cos(yaw), math.sin(yaw)
    d = Vector((ca, sa, 0.0))
    sd = Vector((-sa, ca, 0.0))
    c = Vector(center)
    p0, p1 = c - d * (length * 0.5), c + d * (length * 0.5)
    ring0, ringm, ring1 = [], [], []
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        ox, oz = math.cos(a) * rx, math.sin(a) * rz
        ring0.append(tuple(p0 + sd * (ox * 0.62) + Vector((0, 0, oz * 0.62))))
        ringm.append(tuple(c + sd * ox + Vector((0, 0, oz))))
        ring1.append(tuple(p1 + sd * (ox * 0.62) + Vector((0, 0, oz * 0.62))))
    pts = ring0 + ringm + ring1
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
        faces.append((sides + k, sides + k2, 2 * sides + k2, 2 * sides + k))
    faces.append(tuple(range(sides - 1, -1, -1)))
    faces.append(tuple(range(2 * sides, 3 * sides)))
    acc.add(pts, faces, mat)


def _coin(acc, center, r, thick, mat, yaw=0.0, tilt=0.0, sides=6):
    """金币薄饼:n 棱饼,顶面 n 边形(省面),底面省(俯视战场看不到)。"""
    ca, sa = math.cos(yaw), math.sin(yaw)
    ct, st_ = math.cos(tilt), math.sin(tilt)
    top, bot = [], []
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        lx, ly = math.cos(a) * r, math.sin(a) * r
        wx = lx * ca - ly * sa
        wy = lx * sa + ly * ca
        top.append((center[0] + wx, center[1] + wy, center[2] + thick * 0.5 + lx * st_))
        bot.append((center[0] + wx, center[1] + wy, center[2] - thick * 0.5 + lx * st_))
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
    faces.append(tuple(range(sides - 1, -1, -1)))
    acc.add(top + bot, faces, mat)


def _gem(acc, center, r, h, mat):
    """八面体宝石。"""
    cx, cy, cz = center
    pts = [(cx, cy, cz + h * 0.5), (cx, cy, cz - h * 0.5)]
    for k in range(4):
        a = 2.0 * math.pi * k / 4 + math.pi * 0.25
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r, cz))
    faces = [(0, 2, 3), (0, 3, 4), (0, 4, 5), (0, 5, 2),
             (1, 3, 2), (1, 4, 3), (1, 5, 4), (1, 2, 5)]
    acc.add(pts, faces, mat)


def _flame(acc, base, r, h, mat, rng, sides=5, lean=0.25):
    """棱面火锥:底环 + 歪斜顶尖(火苗感),无底面。"""
    cx, cy, cz = base
    la = rng.uniform(0, 2 * math.pi)
    tip = (cx + math.cos(la) * r * lean, cy + math.sin(la) * r * lean, cz + h)
    pts = [tip]
    for k in range(sides):
        a = 2.0 * math.pi * k / sides + rng.uniform(-0.1, 0.1)
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r, cz))
    faces = [(0, 1 + k, 1 + (k + 1) % sides) for k in range(sides)]
    acc.add(pts, faces, mat)


def _coral(acc, base, rng, mat='Kit_Coral', length=0.3, r=0.035, depth=1):
    """珊瑚枝:主杆 + 顶端 2 分叉。"""
    top = (base[0] + rng.uniform(-0.04, 0.04), base[1] + rng.uniform(-0.04, 0.04),
           base[2] + length)
    ax_tube(acc, base, top, r, r * 0.7, 4, mat)
    if depth > 0:
        for _ in range(2):
            a = rng.uniform(0, 2 * math.pi)
            fbase = (top[0] + math.cos(a) * r * 0.6,
                     top[1] + math.sin(a) * r * 0.6, top[2] - length * 0.15)
            fend = (fbase[0] + math.cos(a) * length * 0.45,
                    fbase[1] + math.sin(a) * length * 0.45, fbase[2] + length * 0.40)
            ax_tube(acc, fbase, fend, r * 0.7, r * 0.45, 4, mat)


def _mound(acc, cx, cy, r0, r1, h, mat, sides=8):
    """圆台土丘(草丛/蕨的根部土芯):r0 底 → r1 顶,穹顶心。"""
    ring0 = [(cx + math.cos(2 * math.pi * k / sides) * r0,
              cy + math.sin(2 * math.pi * k / sides) * r0, 0.0) for k in range(sides)]
    ring1 = [(cx + math.cos(2 * math.pi * k / sides) * r1,
              cy + math.sin(2 * math.pi * k / sides) * r1, h * 0.8) for k in range(sides)]
    pts = ring0 + ring1 + [(cx, cy, h)]
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
        faces.append((sides + k, sides + k2, 2 * sides, 2 * sides))
    acc.add(pts, faces, mat)


# ============================================================================
# 材质(全部槽来自 ST.SLOTS;金属度出处=美术风格指南 §3.1 材质参数总表)
# ============================================================================

SLOT_METALLIC = {'Kit_Iron': 0.85, 'Kit_Brass': 1.0}

USED_SLOTS = [
    'Kit_WoodLight', 'Kit_WoodMid', 'Kit_WoodDark',
    'Kit_SandMid', 'Kit_SandDark',
    'Kit_GrassLight', 'Kit_GrassMid', 'Kit_GrassDark',
    'Kit_RockLight', 'Kit_RockMid', 'Kit_RockDark',
    'Kit_Iron', 'Kit_Brass', 'Kit_Rope', 'Kit_Ember', 'Kit_Coral',
]


def srgb_hex(hex_str):
    """ST.SLOTS 的 sRGB hex → 线性 RGBA(Blender 节点吃线性值)。"""
    def f(v):
        v = v / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = ST.hex_to_rgb(hex_str)
    return (f(r * 255), f(g * 255), f(b * 255), 1.0)


def make_materials():
    out = {}
    for name in USED_SLOTS:
        spec = ST.slot(name)                                    # 未登记槽名在此抛错
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = srgb_hex(spec['hex'])
        bsdf.inputs['Roughness'].default_value = spec['roughness']
        bsdf.inputs['Metallic'].default_value = SLOT_METALLIC.get(name, 0.0)
        out[name] = mat
    return out


# ============================================================================
# ① 棕榈三件(共用 _palm;直立 / 斜身 / 枯死)
# ============================================================================

def _palm(acc, trunk_h, lean_deg, lean_yaw_deg, n_seg, sides, n_leaves, frond_len,
          seed, dead=False, fruit_n=9, stub_n=4):
    """棕榈主体:分节棱化树干(段界面半径回弹=叶鞘环)+ 叶冠 / 断茬 + 残柄 + 果串。
    原点=树干落地点,基部竖直、沿 lean 方向渐倾(弦角=lean_deg)。"""
    rng = random.Random(seed)
    lean = math.radians(lean_deg)
    odx, ody = math.cos(math.radians(lean_yaw_deg)), math.sin(math.radians(lean_yaw_deg))
    r_base, r_top = 0.24, 0.13

    def axis(t):
        off = trunk_h * math.sin(lean) * (t ** 1.7)
        return Vector((off * odx, off * ody, trunk_h * t))

    ang0 = rng.uniform(0, math.pi)
    rings = []
    for k in range(n_seg + 1):
        t = k / n_seg
        c = axis(t)
        r = (r_base + (r_top - r_base) * t)
        if 0 < k < n_seg:
            r *= 1.32                                           # 段界面叶鞘环鼓起(分节感)
        rings.append((c, r, ang0 + 0.5 * (math.pi / sides) * (k % 2)))   # 隔环错缝棱线
    ring_pts = []
    for c, r, a0 in rings:
        pts = []
        for i in range(sides):
            a = a0 + 2.0 * math.pi * i / sides
            pts.append((c.x + math.cos(a) * r, c.y + math.sin(a) * r, c.z))
        ring_pts.append(pts)
    trunk_mat = 'Kit_WoodLight' if dead else 'Kit_WoodMid'
    for k in range(n_seg):
        for i in range(sides):
            i2 = (i + 1) % sides
            acc.add([ring_pts[k][i], ring_pts[k][i2], ring_pts[k + 1][i2], ring_pts[k + 1][i]],
                    [(0, 1, 2, 3)], trunk_mat)

    crown = axis(1.0)
    if dead:
        # 断顶斜茬:顶环各点高低错落(斜面法向朝 lean 背侧),断面 fan 到低位心点(暗色心材)
        top_pts = []
        for i in range(sides):
            p = ring_pts[-1][i]
            a = ang0 + 2.0 * math.pi * i / sides
            drop = 0.34 * (0.5 + 0.5 * math.cos(a - math.radians(lean_yaw_deg) - math.pi))
            top_pts.append((p[0], p[1], trunk_h - drop))
        heart = (crown.x, crown.y, trunk_h - 0.30)
        for i in range(sides):
            i2 = (i + 1) % sides
            acc.add([top_pts[i], top_pts[i2], heart], [(0, 1, 2)], 'Kit_WoodDark')
        # 断口分叉残枝 ×2(斜上伸出,顶端自带小端盖=断口)
        for j, (t0, yaw_j, ln_j) in enumerate(((0.78, 0.6, 0.95), (0.88, 2.6, 0.62))):
            b = axis(t0)
            r_b = r_base + (r_top - r_base) * t0
            dirv = Vector((math.cos(yaw_j), math.sin(yaw_j), 1.35)).normalized()
            tip = b + dirv * ln_j
            ax_tube(acc, tuple(b - Vector((0, 0, 0.1))), tuple(tip), r_b * 0.62, 0.028,
                    6, trunk_mat, cap0=False, cap1=True)
        # 残留叶柄 ×9:下垂枯叶楔(Rope 色,枯褐)——宽大下垂,剪影"裙状"
        for j in range(9):
            t0 = 0.20 + 0.62 * (j / 8.0)
            b = axis(t0)
            yaw_j = math.radians(38 + 137 * j + rng.uniform(-15, 15))
            _palm_frond(acc, b, math.degrees(yaw_j), 10, rng.uniform(0.7, 1.05), 0.33,
                        'Kit_Rope', segs=4, droop=118, fold=0.10, taper=0.05)
    else:
        # 活树顶帽 + 果串(椰果 WoodDark)
        cap_pts = ring_pts[-1]
        acc.add(cap_pts + [tuple(crown)], [tuple(range(sides + 1))], 'Kit_WoodMid')
        for j in range(fruit_n):
            a = 2.0 * math.pi * j / fruit_n + rng.uniform(-0.25, 0.25)
            rr = 0.16 + 0.11 * rng.random()
            pos = crown + Vector((math.cos(a) * rr, math.sin(a) * rr,
                                  -0.10 - 0.14 * rng.random()))
            _berry(acc, tuple(pos), 0.095 + 0.025 * rng.random(), 'Kit_WoodDark', sides=6)
        # 冠下垂残叶柄(枯褐,层次)
        for j in range(stub_n):
            b = axis(1.0) + Vector((0, 0, -0.12))
            yaw_j = math.radians(90 * j + rng.uniform(-20, 20))
            _palm_frond(acc, b, math.degrees(yaw_j), -6, 0.36, 0.14, 'Kit_Rope',
                        segs=3, droop=42, fold=0.10, taper=0.08)

    if n_leaves > 0:
        # 叶冠三排:上扬(亮)/ 平展(中)/ 下垂(暗)——同件三档明度(§2.1)
        tier_elev = (40.0, 14.0, -10.0)
        tier_mat = ('Kit_GrassLight', 'Kit_GrassMid', 'Kit_GrassDark')
        for j in range(n_leaves):
            tier = j % 3
            yaw_j = math.degrees(2.0 * math.pi * j / n_leaves) + rng.uniform(-8, 8)
            elev0 = tier_elev[tier] + rng.uniform(-4, 4)
            ln = frond_len * (1.0 if tier != 2 else 0.92)
            _palm_frond(acc, crown + Vector((0, 0, -0.04)), yaw_j, elev0, ln,
                        0.54 * (frond_len / 2.2), tier_mat[tier], segs=FROND_SEGS,
                        droop=(58 if tier != 1 else 40), fold=0.42, taper=0.15,
                        rib=True, rib_mat='Kit_WoodMid')


def build_palm_tall(acc):
    """PalmTall:直立棕榈,总高 ~7,冠幅 ~4。"""
    _palm(acc, trunk_h=6.2, lean_deg=2.0, lean_yaw_deg=20.0, n_seg=9, sides=TRUNK_SIDES,
          n_leaves=12, frond_len=2.2, seed=4101, fruit_n=12, stub_n=4)


def build_palm_lean(acc):
    """PalmLean:斜身棕榈,倾角 ~20°,总高 ~6。"""
    _palm(acc, trunk_h=5.3, lean_deg=20.0, lean_yaw_deg=35.0, n_seg=8, sides=TRUNK_SIDES,
          n_leaves=10, frond_len=1.95, seed=4102, fruit_n=8, stub_n=3)


def build_palm_dead(acc):
    """PalmDead:枯棕榈,无冠、断顶分叉、残留叶柄,总高 ~4.6。"""
    _palm(acc, trunk_h=4.5, lean_deg=7.0, lean_yaw_deg=140.0, n_seg=9, sides=TRUNK_SIDES,
          n_leaves=0, frond_len=0.0, seed=4103, dead=True)


# ============================================================================
# ② 岩石四件 + 漂木
# ============================================================================

def build_rock_l(acc):
    """RockL:大岩 4×3×2.5,节理多面体、顶部微平(可蹲视觉)+ 两块贴脚副岩。"""
    _rock(acc, 0, 0, 4.0, 3.0, 2.5, seed=4201, rings=ROCK_RINGS, sides=ROCK_SIDES, dome=0.04)
    _rock(acc, 1.28, -0.85, 1.5, 1.1, 0.95, seed=4202, rings=4, sides=8, dome=0.10)
    _rock(acc, -1.35, 0.75, 1.2, 1.0, 0.7, seed=4203, rings=4, sides=7, dome=0.10)


def build_rock_m(acc):
    """RockM:中岩 2×1.6×1.2(预算升 prop,任务附加 ≤1500)。"""
    _rock(acc, 0, 0, 2.0, 1.6, 1.2, seed=4204, rings=6, sides=9, dome=0.06)
    _rock(acc, 0.55, -0.45, 0.85, 0.7, 0.5, seed=4205, rings=4, sides=7, dome=0.12)


def build_rock_s(acc):
    """RockS:小岩 0.9×0.8×0.6。"""
    _rock(acc, 0, 0, 0.9, 0.8, 0.6, seed=4206, rings=4, sides=7, dome=0.09)


def build_rock_flat(acc):
    """RockFlat:宽扁岩 3×2.4×0.6,顶面大而平(矮凳视觉;预算升 prop)。"""
    _rock(acc, 0, 0, 3.0, 2.4, 0.6, seed=4207, rings=4, sides=11, dome=0.28, top_wobble=0.16)
    _rock(acc, -1.15, 0.55, 1.15, 0.8, 0.38, seed=4208, rings=3, sides=6, dome=0.2)


def build_driftwood(acc):
    """Driftwood:漂木 4×0.5×0.6,长轴沿 Y 躺地;剥蚀纹理=低分段棱面+沿程凹陷段(明暗交替)。"""
    rng = random.Random(4301)
    n_seg, sides, L = 11, 7, 4.0

    def axis(t):
        y = -L * 0.5 + L * t
        z = 0.30 + 0.10 * (t ** 2) + 0.10 * ((1.0 - t) ** 2)      # 两端微翘
        return Vector((0.05 * math.sin(t * 5.2), y, z))

    rings = []
    for k in range(n_seg + 1):
        t = k / n_seg
        c = axis(t)
        dent = 1.0 - 0.42 * math.exp(-(((t - 0.36) / 0.09) ** 2)) \
                    - 0.34 * math.exp(-(((t - 0.70) / 0.07) ** 2))   # 两处剥蚀凹陷
        pts = []
        for i in range(sides):
            a = 2.0 * math.pi * i / sides + (0.45 if k % 2 else 0.0)  # 隔环错缝棱
            wob = 1.0 + 0.22 * math.sin(a * 3.0 + 1.7) + rng.uniform(-0.06, 0.06)
            rz = 0.30 * wob * (0.55 + 0.45 * dent)                # 竖向半径(高度 0.6 档)
            rxx = 0.24 * wob * (0.6 + 0.4 * dent)                 # 水平半径(宽度 0.5 档)
            edge = abs(math.cos(a))                               # 端部斜断口:上下缘收
            zz = c.z + math.sin(a) * rz - (0.16 * edge * (0.0 if 0.0 < t < 1.0 else 0.25))
            pts.append((c.x + math.cos(a) * rxx, c.y, zz))
        rings.append((pts, dent))
    for k in range(n_seg):
        pts0, dent0 = rings[k]
        pts1, dent1 = rings[k + 1]
        m = 'Kit_WoodMid' if min(dent0, dent1) < 0.82 else 'Kit_WoodLight'   # 凹陷段暗(湿/磨)
        for i in range(sides):
            i2 = (i + 1) % sides
            acc.add([pts0[i], pts0[i2], pts1[i2], pts1[i]], [(0, 1, 2, 3)], m)
    # 端面斜断口(暗心材,心点微偏不穿出截面)
    for idx in (0, n_seg):
        pts, _ = rings[idx]
        c = axis(idx / n_seg)
        acc.add(pts + [tuple(c + Vector((0, 0.05 * (1 if idx == 0 else -1), 0.02)))],
                [tuple(range(sides + 1))], 'Kit_WoodMid')
    # 残枝 ×3(剥断短杈,两段折)
    for j, (t0, yaw_j, ln_j) in enumerate(((0.18, 1.1, 0.55), (0.55, 3.4, 0.42), (0.82, 5.2, 0.48))):
        b = axis(t0) + Vector((0, 0, 0.05))
        dirv = Vector((math.cos(yaw_j) * 0.8, math.sin(yaw_j) * 0.3, 1.0)).normalized()
        mid = b + dirv * (ln_j * 0.6) + Vector((0.05, -0.04, 0.06))
        tip = mid + dirv * (ln_j * 0.4) + Vector((0.03, 0.05, 0.02))
        ax_tube(acc, tuple(b), tuple(mid), 0.075, 0.05, 5, 'Kit_WoodLight', cap1=False)
        ax_tube(acc, tuple(mid), tuple(tip), 0.05, 0.025, 5, 'Kit_WoodLight')


# ============================================================================
# ③ 木桶 / 木箱堆
# ============================================================================

def build_barrel(acc):
    """BarrelWood:木桶 Ø0.9×1.1;竖向板缝=12 棱 flat 棱线,两道铁箍,顶面凹进+盖板。"""
    sides, H = 12, 1.1
    prof = [(0.0, 0.380), (0.16, 0.432), (0.50, 0.445), (0.84, 0.432), (1.0, 0.380)]
    rings = []
    for zt, r in prof:
        z = zt * H
        rings.append([(math.cos(2 * math.pi * i / sides) * r,
                       math.sin(2 * math.pi * i / sides) * r, z) for i in range(sides)])
    for k in range(len(prof) - 1):
        for i in range(sides):
            i2 = (i + 1) % sides
            m = 'Kit_WoodLight' if i % 2 == 0 else 'Kit_WoodMid'   # 竖板交替色(拼装感)
            acc.add([rings[k][i], rings[k][i2], rings[k + 1][i2], rings[k + 1][i]],
                    [(0, 1, 2, 3)], m)
    # 顶面:桶口沿(外环→内环)+ 凹进盖板(暗缝感)
    inner = 0.30
    ring_in = [(math.cos(2 * math.pi * i / sides) * inner,
                math.sin(2 * math.pi * i / sides) * inner, H - 0.07) for i in range(sides)]
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([rings[-1][i], rings[-1][i2], ring_in[i2], ring_in[i]], [(0, 1, 2, 3)], 'Kit_WoodMid')
    acc.add(ring_in + [(0, 0, H - 0.07)], [tuple(range(sides + 1))], 'Kit_WoodLight')
    # 铁箍 ×2(短圆筒,半径=该高度桶身半径+贴合量,棱与桶身对齐)
    for zt in (0.28, 0.76):
        z0, z1 = zt * H - 0.045, zt * H + 0.045
        r_at = 0.380 + (0.445 - 0.380) * min(1.0, abs(zt - 0.02) / 0.34)   # 桶身该处半径
        rb = r_at + 0.012
        pts = []
        for z in (z0, z1):
            for i in range(sides):
                a = 2 * math.pi * i / sides
                pts.append((math.cos(a) * rb, math.sin(a) * rb, z))
        faces = []
        for i in range(sides):
            i2 = (i + 1) % sides
            faces.append((i, i2, sides + i2, sides + i))
        acc.add(pts, faces, 'Kit_Iron')
    # 侧面桶塞(小圆台)
    ax_tube(acc, (0, -0.442, 0.52), (0, -0.475, 0.52), 0.055, 0.042, 6, 'Kit_WoodDark')


def _crate(acc, center, sx, sy, sz, rot_z, seed, plank_rows):
    """木箱:暗色内芯 + 4 角柱 + 顶底横框 + 每面横板错缝(缝=板间空隙露暗芯)。"""
    rng = random.Random(seed)
    cx, cy, cz = center
    core = 0.86
    ax_box(acc, center, (sx * core, sy * core, sz * core), 'Kit_WoodDark', rot_z)
    post = min(sx, sy) * 0.10
    for px in (-1, 1):
        for py in (-1, 1):
            lx = px * (sx * 0.5 - post * 0.5)
            ly = py * (sy * 0.5 - post * 0.5)
            wx = lx * math.cos(rot_z) - ly * math.sin(rot_z)
            wy = lx * math.sin(rot_z) + ly * math.cos(rot_z)
            ax_box(acc, (cx + wx, cy + wy, cz), (post, post, sz), 'Kit_WoodMid', rot_z)
    # 顶/底横框
    for czf in (cz - sz * 0.5 + post * 0.5, cz + sz * 0.5 - post * 0.5):
        for px in (-1, 1):
            ax_box(acc, (cx + px * (sx * 0.5 - post * 0.5) * math.cos(rot_z),
                         cy + px * (sx * 0.5 - post * 0.5) * math.sin(rot_z), czf),
                   (post, sy - post * 2, post), 'Kit_WoodMid', rot_z)
            ax_box(acc, (cx - px * (sy * 0.5 - post * 0.5) * math.sin(rot_z),
                         cy + px * (sy * 0.5 - post * 0.5) * math.cos(rot_z), czf),
                   (sx - post * 2, post, post), 'Kit_WoodMid', rot_z)
    # 六面横板(plank_rows 行,板间缝露暗芯)
    t = 0.028
    gap = 0.024
    faces = ((0, 1), (0, -1), (1, 1), (1, -1))                  # (轴, 方向):±y 面、±x 面
    for axis_i, sgn in faces:
        w = (sx if axis_i == 0 else sy)
        d = (sy if axis_i == 0 else sx)
        row_h = (sz - (plank_rows + 1) * gap) / plank_rows
        for r in range(plank_rows):
            lz = -sz * 0.5 + gap + row_h * 0.5 + r * (row_h + gap)
            off = (d * 0.5 - t * 0.5 + 0.012) * sgn
            pm = 'Kit_WoodLight' if r % 2 == 0 else 'Kit_WoodMid'   # 横板交替色(拼装感)
            if axis_i == 0:
                ox = 0.0
                oy = off
                bx, by = ox * math.cos(rot_z) - oy * math.sin(rot_z), ox * math.sin(rot_z) + oy * math.cos(rot_z)
                ax_box(acc, (cx + bx, cy + by, cz + lz), (w - post * 1.6, t, row_h), pm, rot_z)
            else:
                ox = off
                oy = 0.0
                bx, by = ox * math.cos(rot_z) - oy * math.sin(rot_z), ox * math.sin(rot_z) + oy * math.cos(rot_z)
                ax_box(acc, (cx + bx, cy + by, cz + lz), (t, w - post * 1.6, row_h), pm, rot_z)
    _ = rng


def build_crate_stack(acc):
    """CrateStack:2 大 1 小错叠(上层旋转错位,小箱搭角),1.8×1.4×1.6。"""
    _crate(acc, (-0.06, 0.05, 0.31), 1.50, 1.28, 0.62, math.radians(4), 4401, 3)
    _crate(acc, (0.10, -0.04, 0.90), 1.28, 1.12, 0.56, math.radians(18), 4402, 3)
    _crate(acc, (-0.32, 0.28, 1.39), 0.62, 0.55, 0.42, math.radians(38), 4403, 2)


# ============================================================================
# ④ 篝火 / 炮位
# ============================================================================

def build_campfire(acc):
    """Campfire:石圈 Ø1.6 + 交叉柴堆 + Ember 余烬丘 + 4 片棱面火锥(渐高色不变)。"""
    rng = random.Random(4501)
    # 石圈:10 块微型节理石(高度自动落岩三档),高低错落
    for i in range(10):
        a = 2.0 * math.pi * i / 10
        rr = 0.80 + rng.uniform(-0.03, 0.05)
        _rock(acc, math.cos(a) * rr, math.sin(a) * rr,
              rng.uniform(0.26, 0.32), rng.uniform(0.22, 0.28), rng.uniform(0.14, 0.30),
              seed=4500 + i, rings=3, sides=6, dome=0.22)
    # 柴堆:3 根斜靠帐篷 + 3 根底层平搭(WoodDark 焦柴,端头探出圈外)
    for j in range(3):
        a = 2.0 * math.pi * j / 3 + 0.5
        ax_tube(acc, (math.cos(a) * 0.42, math.sin(a) * 0.42, 0.05),
                (math.cos(a + math.pi) * 0.09, math.sin(a + math.pi) * 0.09, 0.40),
                0.055, 0.038, 5, 'Kit_WoodDark')
    for j in range(3):
        a = 2.0 * math.pi * j / 3 + 2.0
        ax_tube(acc, (math.cos(a) * 0.44, math.sin(a) * 0.44, 0.05),
                (math.cos(a + math.pi * 0.8) * 0.44, math.sin(a + math.pi * 0.8) * 0.44, 0.05),
                0.048, 0.048, 5, 'Kit_WoodDark', cap1=False)
    # 余烬丘(Ember)+ 圈内 3 块烧暗小石
    _mound(acc, 0, 0, 0.36, 0.17, 0.14, 'Kit_Ember', sides=7)
    for j in range(3):
        a = 2.0 * math.pi * j / 3 + 1.1
        _rock(acc, math.cos(a) * 0.30, math.sin(a) * 0.30, 0.15, 0.12, 0.09,
              seed=4520 + j, rings=3, sides=5, dome=0.3, family='Kit_Rock')
    # 火焰:4 片棱面锥(中心矮宽 + 三围绕低外斜),统一 Kit_Ember,渐高色不变
    _flame(acc, (0, 0, 0.10), 0.27, 0.52, 'Kit_Ember', rng, sides=5, lean=0.30)
    for j in range(3):
        a = 2.0 * math.pi * j / 3 + 0.9
        _flame(acc, (math.cos(a) * 0.24, math.sin(a) * 0.24, 0.05),
               0.15, rng.uniform(0.30, 0.44), 'Kit_Ember', rng, sides=4, lean=0.45)


def build_cannon(acc):
    """CannonEmplacement:Ø3 沙袋/石垛围圈(留炮口缺口朝 -Y)+ 旧舰炮 2.2(铁身/暗木架/铜毂轮)。"""
    rng = random.Random(4551)
    R = 1.5
    # 下层 12 袋 + 上层 9 袋错缝;缺口朝 -Y(炮口方向 yaw≈-90°±28° 不摆袋)
    def in_gap(a):
        d = abs((a + math.pi / 2 + math.pi) % (2 * math.pi) - math.pi)
        return d < math.radians(30)
    n_low = 0
    for i in range(14):
        a = 2.0 * math.pi * i / 14
        if in_gap(a):
            continue
        _sandbag(acc, (math.cos(a) * R, math.sin(a) * R, 0.115), a + math.pi / 2,
                 0.54, 0.185, 0.115, 'Kit_SandMid' if i % 2 == 0 else 'Kit_SandDark')
        n_low += 1
    for i in range(11):
        a = 2.0 * math.pi * (i + 0.5) / 11
        if in_gap(a):
            continue
        _sandbag(acc, (math.cos(a) * (R - 0.10), math.sin(a) * (R - 0.10), 0.325), a + math.pi / 2,
                 0.50, 0.170, 0.105, 'Kit_SandDark' if i % 2 == 0 else 'Kit_SandMid')
    # 缺口两侧石垛 + 圈后大石(节理岩三档;不喧宾夺主)
    for j, a in enumerate((math.radians(-52), math.radians(-128), math.radians(95))):
        _rock(acc, math.cos(a) * (R + 0.05), math.sin(a) * (R + 0.05),
              0.62, 0.52, 0.45 + 0.15 * (j == 2), seed=4560 + j, rings=4, sides=8, dome=0.12)
    # 炮身(铁):尾球→药室→身管渐细→两道口箍+一道中箍,轴线 z≈0.62、炮口微俯
    muz = Vector((0, -1.72, 0.56))
    _berry(acc, (0, 0.52, 0.66), 0.16, 'Kit_Iron', sides=6)      # 尾球
    ax_tube(acc, (0, 0.44, 0.655), (0, 0.18, 0.645), 0.145, 0.155, 8, 'Kit_Iron', cap0=False)
    ax_tube(acc, (0, 0.18, 0.645), muz.to_tuple(), 0.135, 0.095, 8, 'Kit_Iron', cap0=False)
    for ty, tr in ((-0.55, 0.152), (-1.40, 0.126), (-1.62, 0.110)):   # 中箍×1 + 口箍×2(贴合短环)
        f = (ty - 0.18) / (-1.72 - 0.18)
        zt = 0.645 + (0.56 - 0.645) * f
        ax_tube(acc, (0, ty, zt), (0, ty + 0.07, zt - 0.003), tr + 0.020, tr + 0.018, 8,
                'Kit_Iron', cap0=False, cap1=False)
    # 炮架(暗木):两块斜撑侧板 + 托枕
    for sx in (-1, 1):
        ax_box(acc, (sx * 0.17, 0.02, 0.30), (0.055, 0.92, 0.34), 'Kit_WoodDark', rot_z=0.0)
        ax_box(acc, (sx * 0.17, -0.30, 0.14), (0.055, 0.30, 0.28), 'Kit_WoodDark')
        ax_box(acc, (sx * 0.17, 0.42, 0.16), (0.055, 0.26, 0.32), 'Kit_WoodDark')
    ax_box(acc, (0, 0.10, 0.50), (0.46, 0.5, 0.09), 'Kit_WoodDark')            # 托炮枕
    # 轮 ×2:木盘(短筒侧壁 + 双面盘缘)+ 5 辐 + 黄铜毂
    for sx in (-1, 1):
        wx = sx * 0.30
        for y0, y1 in ((-0.26, -0.19), (0.23, 0.30)):            # 轮辘双盘(中空夹辐条)
            ax_tube(acc, (wx, y0, 0.30), (wx, y1, 0.30), 0.30, 0.30, 10, 'Kit_WoodDark',
                    cap0=True, cap1=True)
        for k in range(5):
            a = 2.0 * math.pi * k / 5
            ax_tube(acc, (wx, -0.19, 0.30),
                    (wx + math.cos(a) * 0.24, 0.23, 0.30 + math.sin(a) * 0.24),
                    0.034, 0.028, 4, 'Kit_WoodDark')
        ax_tube(acc, (wx, -0.29, 0.30), (wx, 0.31, 0.30), 0.075, 0.075, 6, 'Kit_Brass')


# ============================================================================
# ⑤ 废墟两件
# ============================================================================

def build_ruin_column(acc):
    """RuinColumnBroken:残柱 Ø1.1 高 ~2.4;竖向刻槽=棱半径交替(20 棱密槽),断裂斜口与柱身同环闭合。"""
    sides = 20
    # 柱础两层(暗石)+ 顶部碎石
    ax_box(acc, (0, 0, 0.06), (1.18, 1.18, 0.12), 'Kit_RockDark')
    ax_box(acc, (0, 0, 0.17), (1.02, 1.02, 0.10), 'Kit_RockDark')
    # 柱身:梅花截面(半径交替=竖刻槽),分节环;截面函数供断口复用
    def section(z):
        t = z / 2.2
        flare = 1.0 - 0.06 * t                                   # 柱头微收分
        pts = []
        for i in range(sides):
            a = 2.0 * math.pi * i / sides
            r = (0.46 if i % 2 == 0 else 0.31) * flare           # 半径差 0.15=刻槽深度
            pts.append((math.cos(a) * r, math.sin(a) * r, z))
        return pts
    zs = [0.22, 0.62, 1.02, 1.38, 1.72, 2.02]
    rings = [section(z) for z in zs]
    for k in range(len(zs) - 1):
        for i in range(sides):
            i2 = (i + 1) % sides
            acc.add([rings[k][i], rings[k][i2], rings[k + 1][i2], rings[k + 1][i]],
                    [(0, 1, 2, 3)], 'Kit_RockMid')
    # 中部残饰环(凸带)
    z0, z1 = 1.16, 1.32
    band = []
    for z in (z0, z1):
        for i in range(sides):
            a = 2.0 * math.pi * i / sides
            r = ((0.46 if i % 2 == 0 else 0.31) + 0.05) * (1.0 - 0.06 * z / 2.2)
            band.append((math.cos(a) * r, math.sin(a) * r, z))
    faces = []
    for i in range(sides):
        i2 = (i + 1) % sides
        faces.append((i, i2, sides + i2, sides + i))
    acc.add(band, faces, 'Kit_RockLight')
    # 断裂斜口:与柱身末环同截面(无浮环),各点 z 沿斜面+锯齿变化,断面 fan 到偏心暗面
    top_pts = []
    last = rings[-1]
    for i in range(sides):
        p = last[i]
        a = 2.0 * math.pi * i / sides
        zz = 2.02 + 0.26 * (0.5 + 0.5 * math.cos(a - 0.9)) + (0.03 if i % 3 == 0 else 0.0)
        top_pts.append((p[0], p[1], zz))
    heart = (-0.05, 0.04, 2.12)
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([top_pts[i], top_pts[i2], heart], [(0, 1, 2)], 'Kit_RockDark')
    # 顶部斜断口侧壁(末环→断口环,闭合无缝)
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([last[i], last[i2], top_pts[i2], top_pts[i]], [(0, 1, 2, 3)], 'Kit_RockMid')
    # 根部苔痕两片(GrassDark 楔片)
    for j, (bx, by, yaw_j) in enumerate(((0.38, -0.16, 0.4), (-0.28, 0.34, 2.2))):
        _palm_frond(acc, (bx, by, 0.02), math.degrees(yaw_j), 30, 0.46, 0.20,
                    'Kit_GrassDark', segs=2, droop=52, fold=0.25, taper=0.1)
    # 础顶碎石两块
    _rock(acc, 0.52, 0.42, 0.30, 0.24, 0.18, seed=4611, rings=3, sides=6, dome=0.2)
    _rock(acc, -0.48, -0.38, 0.22, 0.20, 0.13, seed=4612, rings=3, sides=5, dome=0.2)


def build_ruin_arch(acc):
    """RuinArch:石拱跨 4 高 3.2;两柱一拱 14 楔块,块间错位裂缝 + 一块缺角 + 落地碎石。"""
    # 两柱(锥度柱 + 础 + 头;柱头窄于拱脚,不喧宾)
    for sx in (-1, 1):
        cx = sx * 1.65
        ax_box(acc, (cx, 0, 0.11), (0.95, 0.95, 0.22), 'Kit_RockDark')
        sides = 12
        z0, z1 = 0.22, 2.02
        r0, r1 = 0.36, 0.32
        rings = []
        for z in (z0, (z0 + z1) * 0.5, z1):
            t = (z - z0) / (z1 - z0)
            rings.append([(cx + math.cos(2 * math.pi * i / sides) * (r0 + (r1 - r0) * t),
                           math.sin(2 * math.pi * i / sides) * (r0 + (r1 - r0) * t), z)
                          for i in range(sides)])
        for k in range(2):
            for i in range(sides):
                i2 = (i + 1) % sides
                acc.add([rings[k][i], rings[k][i2], rings[k + 1][i2], rings[k + 1][i]],
                        [(0, 1, 2, 3)], 'Kit_RockMid')
        acc.add(rings[-1] + [(cx, 0, z1)], [tuple(range(sides + 1))], 'Kit_RockLight')
        ax_box(acc, (cx, 0, z1 + 0.07), (0.72, 0.72, 0.14), 'Kit_RockLight')
    # 拱:14 楔块,圆心 (0,0,1.0),内 R1.42 / 外 R2.20,角度 33°→147°(拱脚落在柱头内)
    cxy, cz = 0.0, 1.0
    Rin, Rout = 1.42, 2.20
    n = 14
    a0, a1 = math.radians(33), math.radians(147)
    y0, y1 = -0.16, 0.16
    for k in range(n):
        b0 = a0 + (a1 - a0) * k / n
        b1 = a0 + (a1 - a0) * (k + 1) / n
        # 裂缝:两块错位(下沉/平移),一块缺角(外缘收),块缝天然由弦线台阶呈现
        dx, dz = 0.0, 0.0
        if k == 4:
            dx, dz = 0.05, -0.06
        elif k == 9:
            dx, dz = -0.04, -0.035
        shrink = 0.10 if k == 6 else 0.0                          # 缺角块:外缘下沉收进
        pts = {}
        for yy in (y0, y1):
            i0 = (cxy + math.cos(b0) * Rin + dx * 0.4, yy, cz + math.sin(b0) * Rin + dz * 0.4)
            i1 = (cxy + math.cos(b1) * Rin + dx * 0.4, yy, cz + math.sin(b1) * Rin + dz * 0.4)
            o0 = (cxy + math.cos(b0) * (Rout - shrink) + dx, yy,
                  cz + math.sin(b0) * (Rout - shrink) + dz - shrink)
            o1 = (cxy + math.cos(b1) * (Rout - shrink) + dx, yy,
                  cz + math.sin(b1) * (Rout - shrink) + dz - shrink)
            pts[yy] = (i0, i1, o0, o1)
        i0a, i1a, o0a, o1a = pts[y0]
        i0b, i1b, o0b, o1b = pts[y1]
        m_in, m_out = 'Kit_RockMid', 'Kit_RockLight'
        acc.add([i0a, i1a, i1b, i0b], [(0, 1, 2, 3)], m_in)       # 内弧面
        acc.add([o0a, o1a, o1b, o0b], [(0, 1, 2, 3)], m_out)      # 外弧面
        acc.add([i0a, o0a, o0b, i0b], [(0, 1, 2, 3)], 'Kit_RockDark')   # 端面 b0
        acc.add([i1a, o1a, o1b, i1b], [(0, 1, 2, 3)], 'Kit_RockDark')   # 端面 b1
        acc.add([i0a, i1a, o1a, o0a], [(0, 1, 2, 3)], 'Kit_RockDark')   # 侧面 y0
        acc.add([i0b, o0b, o1b, i1b], [(0, 1, 2, 3)], 'Kit_RockDark')   # 侧面 y1
    # 落地碎石(柱脚)
    _rock(acc, 2.35, 0.55, 0.55, 0.45, 0.35, seed=4621, rings=4, sides=7, dome=0.12)
    _rock(acc, -2.3, -0.5, 0.4, 0.35, 0.26, seed=4622, rings=3, sides=6, dome=0.15)


# ============================================================================
# ⑥ 舵轮 / 宝藏堆
# ============================================================================

def build_wheel_post(acc):
    """ShipWheelPost:柱高 2.2 + 舵轮 Ø1.4(木环 5 辐 4 把手 + 黄铜毂),prop_small 预算。
    轮面整体外移(y=0.34),与立柱错开不穿模。"""
    ax_box(acc, (0, 0, 0.07), (0.5, 0.5, 0.14), 'Kit_WoodDark')               # 柱础
    ax_tube(acc, (0, 0, 0.12), (0, 0, 2.2), 0.13, 0.105, 6, 'Kit_WoodMid')    # 立柱
    hy = 0.34
    ax_tube(acc, (0, 0.02, 2.02), (0, hy + 0.10, 2.02), 0.05, 0.05, 6, 'Kit_WoodDark')   # 横轴
    _torus(acc, (0.0, hy, 2.02), 0.66, 0.05, 12, 5, 'Kit_WoodMid', plane='xz')  # 轮环
    for k in range(5):
        a = 2.0 * math.pi * k / 5
        ax_tube(acc, (0.0, hy, 2.02),
                (math.cos(a) * 0.62, hy, 2.02 + math.sin(a) * 0.62),
                0.038, 0.028, 4, 'Kit_WoodMid', cap0=True, cap1=False)
    ax_tube(acc, (0.0, hy - 0.07, 2.02), (0.0, hy + 0.07, 2.02),
            0.085, 0.085, 6, 'Kit_Brass')                                    # 黄铜毂
    for k in range(4):
        a = 2.0 * math.pi * k / 4 + 0.4
        p_out = (math.cos(a) * 0.68, hy, 2.02 + math.sin(a) * 0.68)
        p_tip = (math.cos(a) * 0.88, hy + 0.03, 2.02 + math.sin(a) * 0.88 + 0.03)
        ax_tube(acc, p_out, p_tip, 0.030, 0.024, 4, 'Kit_WoodDark')           # 把手


def build_treasure(acc):
    """TreasureMound:宝藏堆 Ø2.2;暗木托盘 + Brass 金丘(起伏坡)+ 币贴坡散布 + Coral 宝物。"""
    rng = random.Random(4701)
    # 托盘(破木底盘)
    ax_tube(acc, (0, 0, 0.0), (0, 0, 0.10), 1.08, 0.98, 10, 'Kit_WoodDark', cap0=False, cap1=False)
    top = [(math.cos(2 * math.pi * i / 10) * 0.98, math.sin(2 * math.pi * i / 10) * 0.98, 0.10)
           for i in range(10)]
    acc.add(top + [(0, 0, 0.10)], [tuple(range(11))], 'Kit_WoodDark')
    # 金丘(Brass 圆台坡 + 穹心;环点抖动出"币堆起伏")
    prof = [(0.02, 1.00), (0.24, 0.88), (0.44, 0.62), (0.62, 0.30)]

    def slope_r(z):
        """坡面半径:按 prof 分段线性插值(金币贴坡用)。"""
        if z <= prof[0][0]:
            return prof[0][1]
        for (za, ra), (zb, rb) in zip(prof, prof[1:]):
            if za < z <= zb:
                f = (z - za) / (zb - za)
                return ra + (rb - ra) * f
        return prof[-1][1] * max(0.0, 1.0 - (z - prof[-1][0]) / 0.10)
    rings = []
    for zt, r in prof:
        rings.append([(math.cos(2 * math.pi * i / 10) * (r + rng.uniform(-0.05, 0.05)),
                       math.sin(2 * math.pi * i / 10) * (r + rng.uniform(-0.05, 0.05)),
                       zt + rng.uniform(-0.02, 0.02)) for i in range(10)])
    for k in range(len(prof) - 1):
        for i in range(10):
            i2 = (i + 1) % 10
            acc.add([rings[k][i], rings[k][i2], rings[k + 1][i2], rings[k + 1][i]],
                    [(0, 1, 2, 3)], 'Kit_Brass')
    acc.add(rings[-1] + [(0, 0, 0.72)], [tuple(range(11))], 'Kit_Brass')
    # 散金币:贴坡散布(半径按坡面公式,略嵌入,斜立更"金币"),再立 4 枚在托盘沿
    for j in range(14):
        t = rng.uniform(0.12, 0.92)
        a = rng.uniform(0, 2 * math.pi)
        zz = prof[0][0] + (0.62 - prof[0][0]) * t
        rr = min(slope_r(zz) + 0.015, 0.97)
        tilt = 1.15 * math.atan2(0.20, 0.19) * (0.4 + 0.6 * t)    # 近似坡面切线角,偏竖立
        _coin(acc, (math.cos(a) * rr, math.sin(a) * rr, zz + 0.015), 0.104, 0.026,
              'Kit_Brass', a + math.pi * 0.5, tilt)
    for j in range(4):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.78, 0.94)
        _coin(acc, (math.cos(a) * rr, math.sin(a) * rr, 0.135), 0.105, 0.026,
              'Kit_Brass', rng.uniform(0, math.pi), 0.0)
    # 宝物:3 颗八面体宝石(浮于坡面)+ 1 珊瑚枝(Coral)
    _gem(acc, (0.34, -0.24, 0.50), 0.16, 0.24, 'Kit_Coral')
    _gem(acc, (-0.36, 0.24, 0.36), 0.13, 0.20, 'Kit_Coral')
    _gem(acc, (-0.10, 0.42, 0.74), 0.09, 0.14, 'Kit_Coral')
    _coral(acc, (-0.20, -0.36, 0.30), rng, 'Kit_Coral', length=0.30, r=0.036)


# ============================================================================
# ⑦ 草丛 / 蕨丛
# ============================================================================

def build_grass(acc):
    """GrassTuft:0.8×0.8×0.7;14 片交叉弯叶(GrassLight/Mid 两档混)+ 2 抽穗茎。"""
    rng = random.Random(4801)
    _mound(acc, 0, 0, 0.17, 0.10, 0.10, 'Kit_GrassDark', sides=7)
    for j in range(14):
        yaw_j = math.degrees(2.0 * math.pi * j / 14) + rng.uniform(-9, 9)
        elev0 = rng.uniform(34, 62)
        ln = rng.uniform(0.42, 0.68)
        mat = 'Kit_GrassLight' if j % 2 == 0 else 'Kit_GrassMid'
        _palm_frond(acc, (rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), 0.04),
                    yaw_j, elev0, ln, 0.064, mat, segs=4, droop=34, fold=0.5, taper=0.04)
    for j in range(2):
        yaw_j = 80 + 140 * j
        bx = rng.uniform(-0.05, 0.05)
        by = rng.uniform(-0.05, 0.05)
        ax_tube(acc, (bx, by, 0.06), (bx + 0.10, by + 0.06, 0.56), 0.014, 0.010, 4,
                'Kit_GrassMid', cap1=False)
        for s in range(4):
            _berry(acc, (bx + 0.10 + 0.015 * s, by + 0.06 + 0.009 * s, 0.585 + 0.042 * s),
                   0.020, 'Kit_GrassLight', sides=4)


def build_fern(acc):
    """FernClump:1.0×1.0×0.9;7 根羽叶分层(内层直立/外层斜展),羽片宽大成对上翘。"""
    rng = random.Random(4851)
    _mound(acc, 0, 0, 0.20, 0.12, 0.12, 'Kit_GrassDark', sides=7)
    for j in range(7):
        inner = j < 3
        yaw_j = math.degrees(2.0 * math.pi * j / 7) + rng.uniform(-10, 10)
        elev0 = rng.uniform(70, 80) if inner else rng.uniform(24, 36)
        ln = rng.uniform(0.70, 0.85) if inner else rng.uniform(0.52, 0.66)
        _fern_frond(acc, (rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), 0.05),
                    yaw_j, elev0, ln, rng)


def _fern_frond(acc, base, yaw_deg, elev0_deg, length, rng):
    """蕨羽叶:V 折主带 5 段 + 每段两侧宽大羽片(菱形双三角,近水平展开、梢端上翘)。"""
    yr = math.radians(yaw_deg)
    er0 = math.radians(elev0_deg)
    drp = math.radians(30)
    dh = Vector((math.cos(yr), math.sin(yr), 0.0))
    sd = Vector((-math.sin(yr), math.cos(yr), 0.0))
    up = Vector((0, 0, 1))
    segs = 5
    width = 0.055
    secs = []
    p = Vector(base)
    for k in range(segs + 1):
        f = k / segs
        elev = er0 - drp * (f ** 1.2)
        d = dh * math.cos(elev) + up * math.sin(elev)
        if k > 0:
            p = p + d * (length / segs)
        w = width * (1.0 - 0.8 * (f ** 1.1))
        secs.append((p.copy(), max(w, 0.012)))
    mat_main = 'Kit_GrassMid'
    for k in range(segs):
        (p0, w0), (p1, w1) = secs[k], secs[k + 1]
        l0 = p0 - sd * (w0 * 0.5); r0 = p0 + sd * (w0 * 0.5); m0 = p0 + up * (w0 * 0.32)
        l1 = p1 - sd * (w1 * 0.5); r1 = p1 + sd * (w1 * 0.5); m1 = p1 + up * (w1 * 0.32)
        acc.add([tuple(l0), tuple(m0), tuple(m1), tuple(l1)], [(0, 1, 2, 3)], mat_main)
        acc.add([tuple(m0), tuple(r0), tuple(r1), tuple(m1)], [(0, 1, 2, 3)], mat_main)
        # 羽片:主带两侧各 1 片(宽菱形:近水平伸出、梢端微翘;交替明暗)
        pm = (p0 + p1) * 0.5
        wl = 0.23 * (1.0 - 0.5 * (k / segs))
        mat_p = 'Kit_GrassDark' if k % 2 == 0 else 'Kit_GrassLight'
        for sgn in (1, -1):
            tip = pm + sd * (wl * sgn) + up * (wl * 0.38)
            low = pm + sd * (wl * 0.62 * sgn) - up * (wl * 0.06)
            anchor = pm - sd * (w0 * 0.30) if sgn > 0 else pm + sd * (w0 * 0.30)
            acc.add([tuple(anchor), tuple(tip), tuple(low)], [(0, 1, 2)], mat_p)


# ============================================================================
# 资产清单(名 / builder / 预算档)
# ============================================================================

ASSETS = [
    ('PalmTall', build_palm_tall, 'prop'),
    ('PalmLean', build_palm_lean, 'prop'),
    ('PalmDead', build_palm_dead, 'prop'),
    ('RockL', build_rock_l, 'prop'),
    ('RockM', build_rock_m, 'prop'),            # 任务附加 ≤1500(实测见 README)
    ('RockS', build_rock_s, 'prop_small'),
    ('RockFlat', build_rock_flat, 'prop'),
    ('Driftwood', build_driftwood, 'prop'),
    ('BarrelWood', build_barrel, 'prop_small'),
    ('CrateStack', build_crate_stack, 'prop'),
    ('Campfire', build_campfire, 'prop_small'),
    ('CannonEmplacement', build_cannon, 'prop'),
    ('RuinColumnBroken', build_ruin_column, 'prop'),
    ('RuinArch', build_ruin_arch, 'prop'),
    ('ShipWheelPost', build_wheel_post, 'prop_small'),
    ('TreasureMound', build_treasure, 'prop_small'),
    ('GrassTuft', build_grass, 'prop_small'),
    ('FernClump', build_fern, 'prop_small'),
]


# ============================================================================
# 预览渲染(样板同款:Cycles GPU→CPU 回退、Standard 视图、中性灰三灯)
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
    samples = SAMPLES if gpu else max(16, SAMPLES // 2)
    scene.cycles.samples = samples
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
    scene.view_settings.view_transform = ST.PREVIEW_VIEW_TRANSFORM   # Standard(AgX 洗色禁用)
    scene.view_settings.look = 'None'
    log('render engine=Cycles device=%s samples=%d' % (gpu or 'CPU', samples))


def srgb255(hexv):
    return ST.hex_to_rgb(hexv) if isinstance(hexv, str) else hexv


def setup_stage(scene, dim, h):
    """中性灰世界 + 灰地板(z=-0.02)+ 三灯(暖 key / 冷 fill / 暖 rim),灯距按资产尺寸缩放。"""
    for oname in ('sk_key', 'sk_fill', 'sk_rim', 'sk_floor'):
        o = bpy.data.objects.get(oname)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
    world = scene.world
    if world is None:
        world = bpy.data.worlds.new('props_world')
        scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs[0].default_value = (*srgb255(ST.PREVIEW_BG_HEX), 1.0)
    bg.inputs[1].default_value = 1.0

    u = max(dim, 1.2) / 15.85                                       # 样板船 maxdim 归一
    floor_mat = bpy.data.materials.get('props_floor_mat')
    if floor_mat is None:
        floor_mat = bpy.data.materials.new('props_floor_mat')
        floor_mat.use_nodes = True
        fb = floor_mat.node_tree.nodes['Principled BSDF']
        fb.inputs['Base Color'].default_value = (*srgb255(ST.PREVIEW_GROUND_HEX), 1.0)
        fb.inputs['Roughness'].default_value = 0.9
    floor = bpy.data.objects.new('sk_floor', bpy.data.meshes.new('sk_floor'))
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=80)
    bm.to_mesh(floor.data)
    bm.free()
    floor.location = (0, 0, -0.02)
    floor.data.materials.append(floor_mat)
    scene.collection.objects.link(floor)

    def area(name, loc, energy, color, target):
        d = bpy.data.lights.new(name, 'AREA')
        d.size = 10.0 * u
        d.energy = energy * u * u
        d.color = color
        o = bpy.data.objects.new(name, d)
        scene.collection.objects.link(o)
        o.location = loc
        di = Vector(target) - Vector(loc)
        o.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()

    tz = (0, 0, max(0.5, h * 0.4))
    area('sk_key', (-12 * u, -14 * u, 12 * u), 2600, (1.0, 0.94, 0.84), tz)
    area('sk_fill', (14 * u, -4 * u, 7 * u), 900, (0.62, 0.75, 1.0), tz)
    area('sk_rim', (3 * u, 16 * u, 10 * u), 1800, (1.0, 0.9, 0.75), (0, 0, max(0.8, h * 0.6)))


def make_views(size):
    """两视角:front34(右前上 3/4)+ side(正侧)。距离系数 1.42:保证高件(棕榈 7m)不裁顶。"""
    D = max(size)
    h = size[2]
    d = D * 1.42 + 1.4
    ct = (0, 0, h * 0.50)
    return [
        ('front34', (d * 0.58, -d * 0.76, h * 0.56 + d * 0.14), ct),
        ('side', (-d * 0.98, d * 0.10, h * 0.52 + d * 0.08), ct),
    ]


def render_views(scene, prefix, views):
    for name, loc, target in views:
        cam_data = bpy.data.cameras.new('sk_cam')
        cam_data.lens = LENS
        cam_data.clip_end = 400
        cam = bpy.data.objects.new('sk_cam', cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        di = Vector(target) - Vector(loc)
        cam.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        path = os.path.join(PREVIEW_DIR, '%s-%s.jpg' % (prefix, name))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        log('rendered %s (%d bytes)' % (path, os.path.getsize(path)))
        bpy.data.objects.remove(cam, do_unlink=True)


# ============================================================================
# 主流程
# ============================================================================

def main():
    parse_args()
    for d in (FBX_DIR, PREVIEW_DIR, WORK_DIR):
        os.makedirs(d, exist_ok=True)

    # 清空 factory 场景(防御性,保证幂等)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0                          # 1 Blender 单位 = 1 米

    mats = make_materials()
    if RENDER:
        setup_render(scene)

    wanted = None
    if ONLY != 'all':
        wanted = {s.strip().lower() for s in ONLY.split(',') if s.strip()}
        known = {name.lower(): name for name, _, _ in ASSETS}
        for w in wanted:
            if w not in known:
                raise SystemExit('props_kit: 未知资产名 %r(合法:%s)' %
                                 (w, ', '.join(n for n, _, _ in ASSETS)))

    for name, fn, budget in ASSETS:
        if wanted is not None and name.lower() not in wanted:
            continue
        log('== %s ==' % name)
        acc = MeshAcc()
        fn(acc)
        nv, nf, ntri = acc.counts()
        log('%s raw: verts=%d polys=%d tris=%d' % (name, nv, nf, ntri))
        obj = join_to_object(acc, name, mats)
        stats = ST.print_stats(name, [obj])                         # STAT 行(README 引用)
        if stats['tris'] > ST.POLY_BUDGETS[budget]:                 # 等价 ST.budget_guard(避免重复 STAT)
            raise ValueError('[%s] 面数超预算 %s=%d' % (name, budget, ST.POLY_BUDGETS[budget]))
        ST.export_fbx([obj], os.path.join(FBX_DIR, name + '.fbx'))
        log('exported %s' % os.path.join(FBX_DIR, name + '.fbx'))
        if RENDER:
            sx, sy, sz = stats['size']
            setup_stage(scene, max(sx, sy, sz), sz)
            render_views(scene, name, make_views((sx, sy, sz)))
        bpy.data.objects.remove(obj, do_unlink=True)

    if RENDER:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, 'props_debug.blend'))
    log('done in %.1fs total' % (time.time() - T0))


if __name__ == '__main__':
    main()
