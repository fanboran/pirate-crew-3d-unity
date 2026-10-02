# -*- coding: utf-8 -*-
"""chemplant_kit.py —— 第三手搓样板关「废弃化工厂」无头建模（Blender 5.2，纯 bpy/bmesh 程序化，零贴图）。

风格/参数唯一来源：style_tokens.py（ST.SLOTS / print_stats / export_fbx / PREVIEW_*）——
本脚本不散写色值、粗糙度、导出参数；工业槽位（Kit_Concrete*/Kit_Rust*/Kit_Steel*/Kit_GlassDark/
Kit_PaintYellow）见 ST.SLOTS 的「工业/废弃【提】」段，Unity 侧同源表 = WorldMapAssetSetBuilder.Slots。

【硬口径】
- 1 单位 = 1 米，Z 朝上；每件原点 = 落地接触面中心、底面 Z=0，整体向上生长，X/Y 居中；
- 全部面 shade_flat：细节全靠几何（压型板竖棱、锈痕按扇区落面、破窗=真空洞、塌顶=缺面板露桁架）；
- 分件材质槽 ≤8（ST 断言）；总装件 ChemPlant_Level 是一整关（非 Unity 换装单元），槽数 = 各分件并集，
  经 ST.print_stats(slot_limit=...) 显式放宽，实到槽数见 README；
- 废弃质感的做法（不贴图）：锈痕 = 竖向流痕按 扇区×环带 落 Kit_Rust/Kit_RustDark 面；
  破窗 = 窗带只留暗背板 + 框，缺玻璃的洞是真空洞；塌顶/断墙 = 几何直接缺件 + 挂板 + 露桁架；
  荒草 = 墙面根部细瘦歪斜草簇，裂缝/积水 = 贴地薄片（Kit_ConcreteDark / Kit_WetSand）。

复现（仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/chemplant/chemplant_kit.py -- [--only Level|Hall,...] [--samples N] [--no-render]

产物（重跑幂等覆盖）：
    pirate-crew/Assets/Art/Models/WorldKit/ChemPlant/<名>.fbx        ×11 分件 + ChemPlant_Level.fbx
    export/chemplant-kit/<名>-<视角>.jpg                              (1024²/1280² q90，view_transform=Standard)
    external/chemplant-kit-work/chemplant_debug.blend                 (调参 GUI 缓存，gitignored)
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
# 参数区
# ============================================================================

ONLY = 'all'                 # 'all' | 逗号分隔资产名（大小写不敏感），'Level' 可简写
SAMPLES = 48                 # Cycles 采样数（CPU 回退自动减半）
RENDER = True                # False = 只建模导出 FBX，不出预览图
KIT_W, KIT_H = 1024, 576     # 分件预览 16:9
LEVEL_W, LEVEL_H = 1920, 1080  # 场地成品图（块边长 2 → 艺术画布 960×540 = 1080p 实机同口径）
PIXEL_BLOCK = 2              # 像素块边长（屏幕像素；与 PixelartCameraRig.PixelScaleDefault 同值）
CULL_BACKFACES = True        # 预览是否按实机口径剔背面（像素路径物体 pass 单面染染）
                             # 【为什么默认开】Cycles 默认双面染染：任何**朝向朝内/开壳**的面在预览里
                             # 照常可见，进实机却因背面剔除变成"透视洞"（冷却塔 2026-09-29 实测：
                             # 预览是实心塔、实机是镂空壳）。开这一档，预览才会提前暴露这类缺陷。
PIXEL_STEPS = 0              # 全图色阶量化档数（0 = 只像素化不做量化；>1 才挂 Posterize 节点）
                             # 【为什么默认关】游戏里的色带作用在**光照**上、逐物体量化；
                             # 对成图整幅逐通道量化会把 #8C4A28 这类低饱和锈色推成饱和红、
                             # 把灰底推成粉彩环带（实测）。要对照色带纹理时再开。
JPG_QUALITY = ST.PREVIEW_QUALITY
LENS = 50                    # mm
FIT_PAD = 1.18               # 分件取景余量（按投影外框宽/高取大算距离）
GAME_WIDE_M = 28.0           # 像素化路径"广角"可见米数锚（PixelartPilotScene.cs:62-68）
EXPOSURE_LEVEL = -0.45       # 场地成图曝光补偿（浅色砼在暖 key 下会糊到近白，压一档）
EXPOSURE_KIT = -0.10         # 分件预览（影棚中性档，与其它 kit 预览一致）

# 槽上限：分件 12（本 kit 件是**关卡美术**，不是 WorldKit 换装单元——8 槽纪律针对随海图换装的
# kit；本 kit 里锈/砼/钢多档共存正是"废弃质感"的载体，故显式放宽并记录实际值）；总装件 20（一整关）
LEVEL_SLOT_LIMIT = 20
PIECE_SLOT_LIMIT = 12

FBX_DIR = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..',
                                       'pirate-crew', 'Assets', 'Art', 'Models', 'WorldKit', 'ChemPlant'))
PREVIEW_DIR = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', 'docs', 'images', 'chemplant-kit'))
WORK_DIR = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', 'external', 'chemplant-kit-work'))

T0 = time.time()


def log(msg):
    print('[chemplant] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


def parse_args():
    global ONLY, SAMPLES, RENDER, CULL_BACKFACES
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
        elif a == '--double-sided':
            CULL_BACKFACES = False; i += 1
        else:
            i += 1


# ============================================================================
# MeshAcc：几何累积器（样板同款 + 分件摆位 append）
# ============================================================================

class MeshAcc:
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

    def append(self, other, offset=(0.0, 0.0, 0.0), yaw_deg=0.0, tilt_deg=0.0):
        """把另一累积器（局部系）绕 X 倾斜、绕 Z 旋转后平移到本器：摆位/倾倒件用。"""
        ca, sa = math.cos(math.radians(yaw_deg)), math.sin(math.radians(yaw_deg))
        ct, st = math.cos(math.radians(tilt_deg)), math.sin(math.radians(tilt_deg))
        ox, oy, oz = offset
        base = len(self.verts)
        for (x, y, z) in other.verts:
            y, z = y * ct - z * st, y * st + z * ct
            self.verts.append((x * ca - y * sa + ox, x * sa + y * ca + oy, z + oz))
        for f, m, sm in zip(other.faces, other.face_mat, other.face_smooth):
            self.faces.append(tuple(base + i for i in f))
            self.face_mat.append(m)
            self.face_smooth.append(sm)

    def counts(self):
        tris = sum(len(f) - 2 for f in self.faces)
        return len(self.verts), len(self.faces), tris


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
    # 【不跑 recalc_face_normals，2026-09-29 实机实测】它对本 kit 大量存在的**开口面**（壳体/盘/条）
    # 是启发式的：会把整片翻成朝内 ⇒ 实机（像素路径物体 pass，剔背面）直接变成"透视洞"，
    # 而 Blender 双面染染与 recalc 前的作者朝向都看不出来（塔壳/罐体/球罐/烟囱/地面水池全中过）。
    # 改为**按构造保证外法线**：loft / fan / annulus / cyl / sphere_shell / patch_poly / wall_seg
    # 的绕序已逐条核对；sbox 的面表已按右手定则改写。新增图元请照此自查（预览用
    # `--double-sided` 关掉剔除可看双面效果，默认按实机口径剔背面）。
    bm.to_mesh(me)
    bm.free()
    for name in used:
        me.materials.append(mats[name])
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def srgb_hex(hex_str):
    def f(v):
        v = v / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = ST.hex_to_rgb(hex_str)
    return (f(r * 255), f(g * 255), f(b * 255), 1.0)


def make_materials():
    """预览材质：只吃 ST 槽色 + 哑光（Specular=0 / Roughness=1）。

    像素化着色路径（docs/技术/染染/管线/染染管线.md §1.1）的物体材质由
    `PixelartMaterialFactory.Create(name, albedo)` 造——**逐物体只认 albedo 一个量**，
    粗糙度/金属度不参与着色（色带 + 描边在屏幕空间那几趟里）。所以预览用哑光漫反射
    + 像素化合成，才是对得上实机观感的近似；ST.SLOTS 的 roughness 仅供别的路径/记录。
    """
    out = {}
    for name in ALL_SLOTS:
        spec = ST.slot(name)
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = srgb_hex(spec['hex'])
        bsdf.inputs['Roughness'].default_value = 1.0
        bsdf.inputs['Metallic'].default_value = 0.0
        for key in ('Specular IOR Level', 'Specular'):
            if key in bsdf.inputs:
                bsdf.inputs[key].default_value = 0.0
                break
        if CULL_BACKFACES:
            nt = mat.node_tree
            out_node = nt.nodes['Material Output']
            transp = nt.nodes.new('ShaderNodeBsdfTransparent')
            mix = nt.nodes.new('ShaderNodeMixShader')
            geo = nt.nodes.new('ShaderNodeNewGeometry')
            nt.links.new(geo.outputs['Backfacing'], mix.inputs[0])
            nt.links.new(bsdf.outputs['BSDF'], mix.inputs[1])
            nt.links.new(transp.outputs['BSDF'], mix.inputs[2])
            nt.links.new(mix.outputs['Shader'], out_node.inputs['Surface'])
        out[name] = mat
    return out


# ============================================================================
# 图元 helper（全部 flat；坐标 = 资产局部系：底面 Z=0、X/Y 居中）
# ============================================================================

def sbox(acc, c, s, mat, rot=None):
    """盒体（rot = XYZ 欧拉弧度，可倾斜；闭合体，法线由 recalc 收口）。"""
    hx, hy, hz = s[0] * .5, s[1] * .5, s[2] * .5
    m = mathutils.Euler(rot if rot else (0.0, 0.0, 0.0), 'XYZ').to_matrix()
    pts = []
    for dx in (-hx, hx):
        for dy in (-hy, hy):
            for dz in (-hz, hz):
                v = m @ Vector((dx, dy, dz))
                pts.append((c[0] + v.x, c[1] + v.y, c[2] + v.z))
    # 面表按**右手定则**写对（外法线）：本 kit 不再靠 recalc_face_normals 兜朝向，见 join_to_object 注释
    acc.add(pts, [(1, 3, 2, 0), (6, 7, 5, 4), (4, 5, 1, 0), (3, 7, 6, 2), (2, 6, 4, 0), (5, 7, 3, 1)], mat)


def cyl(acc, p0, p1, r0, r1, sides, mat, cap0=True, cap1=True, smooth=False):
    """圆台/圆柱杆（低分段 = 棱面杆）。"""
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
        pts.append(tuple(c0 + w * r0))
    for k in range(sides):
        a = 2.0 * math.pi * k / sides
        w = u * math.cos(a) + v * math.sin(a)
        pts.append(tuple(c1 + w * r1))
    faces = []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
    if cap0:
        faces.append(tuple(range(sides - 1, -1, -1)))
    if cap1:
        faces.append(tuple(range(sides, 2 * sides)))
    acc.add(pts, faces, mat, smooth=smooth)


def ring_pts(cx, cy, r, z, sides, phase=0.0, roff=None):
    """一圈点（俯视逆时针；roff = 逐扇区半径倍率，做凹痕/棱槽）。"""
    out = []
    for i in range(sides):
        a = phase + 2.0 * math.pi * i / sides
        rr = r if roff is None else r * roff[i]
        out.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr, z))
    return out


def loft(acc, rings, matf, cap_bot=None, cap_top=None, smooth=False):
    """环放样：rings 自下而上、俯视逆时针；matf(band, sector) → 槽名。
    smooth=True = 平滑着色（弧面用）：实机三档色带下，平滑法线让色带沿曲面**连续弯**，
    逐面 flat 则每面各落一档（竖直曲面上就是逐面跳档）。创始人 2026-09-29：
    「圆柱形建筑要不在弧形面上精准使用一下细分曲面」。"""
    n = len(rings[0])
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(n):
            i2 = (i + 1) % n
            acc.add([a[i], a[i2], b[i2], b[i]], [(0, 1, 2, 3)], matf(k, i), smooth=smooth)
    if cap_bot:
        acc.add(list(reversed(rings[0])), [tuple(range(n))], cap_bot)
    if cap_top:
        acc.add(list(rings[-1]), [tuple(range(n))], cap_top)


def fan(acc, apex, ring, mat, invert=False):
    """环 → 顶点扇形（锥顶/球极）。"""
    n = len(ring)
    for i in range(n):
        i2 = (i + 1) % n
        tri = (apex, ring[i2], ring[i]) if invert else (apex, ring[i], ring[i2])
        acc.add(list(tri), [(0, 1, 2)], mat)


def annulus(acc, cx, cy, r_in, r_out, z, sides, mat, thick=0.10, mat_edge=None):
    """平面圆环（平台面/法兰盘）。"""
    ti = ring_pts(cx, cy, r_in, z + thick * .5, sides)
    to = ring_pts(cx, cy, r_out, z + thick * .5, sides)
    bi = ring_pts(cx, cy, r_in, z - thick * .5, sides)
    bo = ring_pts(cx, cy, r_out, z - thick * .5, sides)
    e = mat_edge or mat
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([to[i], to[i2], ti[i2], ti[i]], [(0, 1, 2, 3)], mat)
        acc.add([bo[i2], bo[i], bi[i], bi[i2]], [(0, 1, 2, 3)], mat)
        acc.add([bo[i], bo[i2], to[i2], to[i]], [(0, 1, 2, 3)], e)
        acc.add([ti[i], ti[i2], bi[i2], bi[i]], [(0, 1, 2, 3)], e)


def arc_tube(acc, cx, cy, z, r, a0, a1, sides, tube_r, mat, t_sides=4):
    """水平圆弧杆（护栏/护笼）；a0/a1 弧度。"""
    for i in range(sides):
        t0 = a0 + (a1 - a0) * i / sides
        t1 = a0 + (a1 - a0) * (i + 1) / sides
        cyl(acc, (cx + math.cos(t0) * r, cy + math.sin(t0) * r, z),
            (cx + math.cos(t1) * r, cy + math.sin(t1) * r, z), tube_r, tube_r, t_sides, mat)


def sphere_shell(acc, cx, cy, cz, r, sides, nrings, matf, smooth=False):
    """球壳（自下而上环放样 + 两极扇面）。"""
    rings = []
    for k in range(1, nrings):
        phi = math.pi * (1.0 - k / float(nrings))          # 底 → 顶
        rings.append(ring_pts(cx, cy, r * math.sin(phi), cz + r * math.cos(phi), sides))
    loft(acc, rings, matf, smooth=smooth)
    bot = (cx, cy, cz - r)
    top = (cx, cy, cz + r)
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([bot, rings[0][i2], rings[0][i]], [(0, 1, 2)], matf(0, i))
        acc.add([top, rings[-1][i], rings[-1][i2]], [(0, 1, 2)], matf(len(rings) - 2, i))


def tri_prism(acc, axis, c, a0, a1, z0, z_apex, thick, mat, apex_a=None):
    """三角墙尖（山墙）：底边 a0..a1、顶点 z_apex，厚度 thick。"""
    aa = apex_a if apex_a is not None else (a0 + a1) * .5
    ht = thick * .5
    if axis == 'x':
        pts = [(c - ht, a0, z0), (c - ht, a1, z0), (c - ht, aa, z_apex),
               (c + ht, a0, z0), (c + ht, a1, z0), (c + ht, aa, z_apex)]
    else:
        pts = [(a0, c - ht, z0), (a1, c - ht, z0), (aa, c - ht, z_apex),
               (a0, c + ht, z0), (a1, c + ht, z0), (aa, c + ht, z_apex)]
    acc.add(pts, [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], mat)


# ----------------------------------------------------------------------------
# 材质落面策略：锈痕 / 风化斑
# ----------------------------------------------------------------------------

def streak_matf(base, seed, sides, rust='Kit_Rust', alt='Kit_RustDark', cover=0.35, alt_every=11):
    """竖向锈流痕：部分扇区自第二环带起整条落锈色；另有点状锈斑。"""
    rng = random.Random(seed)
    start = [rng.random() for _ in range(sides + 4)]
    n = sides + 4

    def f(band, sector):
        s = sector % n
        if band >= 1 and start[s] < cover:
            return rust
        if (s * 7 + band * 5 + seed) % alt_every == 0:
            return alt if alt else rust
        return base
    return f


def patch_matf(base, seed, dark, rate=7, light=None):
    """风化斑（混凝土剥落/水渍）：确定性散斑。"""
    def f(band, sector):
        h = (sector * 31 + band * 17 + seed * 13) % 89
        if h % rate == 0:
            return dark
        if light is not None and h % (rate * 3) == 1:
            return light
        return base
    return f


# ----------------------------------------------------------------------------
# 通用结构件：墙段 / 窗带 / 爬梯 / 护栏 / 楼梯 / 架子 / 土建小节
# ----------------------------------------------------------------------------

def wall_seg(acc, axis, a0, a1, c, z0, z1, mat, thick=0.16, ribs=0, rib_mat=None,
             rib_w=0.11, rib_d=0.09, inward=1.0):
    """一段墙（axis='x' 沿 X 走向, c = 墙中心 y；ribs>0 时外侧加竖棱 = 压型钢板）。"""
    L = a1 - a0
    if L <= 1e-6 or z1 - z0 <= 1e-6:
        return
    cz = (z0 + z1) * .5
    off = inward * (thick * .5 + rib_d * .5)
    if axis == 'x':
        sbox(acc, ((a0 + a1) * .5, c, cz), (L, thick, z1 - z0), mat)
        for k in range(ribs):
            m = mat
            if rib_mat is not None and (k * 5 + int(abs(L))) % 4 == 0:
                m = rib_mat
            sbox(acc, (a0 + L * (k + 0.5) / ribs, c + off, cz), (rib_w, rib_d, z1 - z0), m)
    else:
        sbox(acc, (c, (a0 + a1) * .5, cz), (thick, L, z1 - z0), mat)
        for k in range(ribs):
            m = mat
            if rib_mat is not None and (k * 5 + int(abs(L))) % 4 == 0:
                m = rib_mat
            sbox(acc, (c + off, a0 + L * (k + 0.5) / ribs, cz), (rib_d, rib_w, z1 - z0), m)


def window_band(acc, axis, a0, a1, c, z0, z1, bays, seed, inward=1.0,
                frame='Kit_SteelPale', glass='Kit_GlassDark', back='Kit_GlassDark',
                mull_w=0.30, depth=0.30, boarded='Kit_WoodDark', transom=True):
    """窗带：内暗背板 + 竖梃 + 玻璃片；按确定性图案留空洞 / 钉木板 / 碎玻璃。

    「破窗」的洞是真空洞——该 bay 不放玻璃片，透到暗背板（读作黑窗）。
    """
    L = a1 - a0
    if L <= 1e-6 or z1 - z0 <= 1e-6:
        return
    cz = (z0 + z1) * .5
    boff = c + inward * (depth + 0.05)
    # 暗背板（整条）
    if axis == 'x':
        sbox(acc, ((a0 + a1) * .5, boff, cz), (L, 0.10, z1 - z0), back)
    else:
        sbox(acc, (boff, (a0 + a1) * .5, cz), (0.10, L, z1 - z0), back)
    # 上下框（窗台/过梁）+ 竖梃
    for zz in (z0 - 0.09, z1 + 0.09):
        if axis == 'x':
            sbox(acc, ((a0 + a1) * .5, c, zz), (L + 0.1, depth * 0.7, 0.20), frame)
        else:
            sbox(acc, (c, (a0 + a1) * .5, zz), (depth * 0.7, L + 0.1, 0.20), frame)
    for k in range(bays + 1):
        p = a0 + L * k / float(bays)
        if axis == 'x':
            sbox(acc, (p, c, cz), (mull_w, depth * 0.8, z1 - z0 + 0.2), frame)
        else:
            sbox(acc, (c, p, cz), (depth * 0.8, mull_w, z1 - z0 + 0.2), frame)
    # 玻璃片 / 空洞 / 破窗
    pitch = L / float(bays)
    for k in range(bays):
        pc = a0 + pitch * (k + 0.5)
        h = (k * 13 + int(abs(a0)) * 7 + seed) % 5
        if axis == 'x':
            gc, gsz = (pc, c, cz), (pitch - mull_w - 0.10, depth * 0.5, z1 - z0 - 0.1)
        else:
            gc, gsz = (c, pc, cz), (depth * 0.5, pitch - mull_w - 0.10, z1 - z0 - 0.1)
        if h == 0:                                              # 空洞（玻璃全掉）
            continue
        if h == 1:                                              # 钉了块烂木板
            if axis == 'x':
                sbox(acc, (pc, c + inward * 0.10, cz), (pitch * 0.8, 0.07, 0.45), boarded,
                     rot=(0.06, 0.0, 0.05))
            else:
                sbox(acc, (c + inward * 0.10, pc, cz), (0.07, pitch * 0.8, 0.45), boarded,
                     rot=(-0.06, 0.0, 0.05))
            continue
        sbox(acc, gc, gsz, glass)
        if h == 2:                                              # 残玻璃（一角留了片）
            if axis == 'x':
                sbox(acc, (pc + pitch * 0.28, c + inward * 0.02, z0 + 0.30),
                     (pitch * 0.34, 0.05, 0.55), glass, rot=(0.0, 0.0, 0.26))
            else:
                sbox(acc, (c + inward * 0.02, pc + pitch * 0.28, z0 + 0.30),
                     (0.05, pitch * 0.34, 0.55), glass, rot=(0.0, 0.0, -0.26))
    if transom:                                                 # 中横档
        if axis == 'x':
            sbox(acc, ((a0 + a1) * .5, c, cz), (L, depth * 0.6, 0.10), frame)
        else:
            sbox(acc, (c, (a0 + a1) * .5, cz), (depth * 0.6, L, 0.10), frame)


def ladder(acc, x, y, z0, z1, mat, out_dir=(1.0, 0.0), rung_step=0.34, w=0.52,
           rail_r=0.05, rung_r=0.035, cage=False):
    """直爬梯（x,y = 梯底中心，out_dir = 梯外侧方向）；cage=True 加护笼。"""
    px, py = -out_dir[1], out_dir[0]
    ax, ay = x + px * w * .5, y + py * w * .5
    bx, by = x - px * w * .5, y - py * w * .5
    cyl(acc, (ax, ay, z0), (ax, ay, z1), rail_r, rail_r, 4, mat)
    cyl(acc, (bx, by, z0), (bx, by, z1), rail_r, rail_r, 4, mat)
    n = max(1, int((z1 - z0) / rung_step))
    for k in range(n + 1):
        z = z0 + (z1 - z0) * k / float(n)
        cyl(acc, (ax, ay, z), (bx, by, z), rung_r, rung_r, 4, mat)
    if cage:
        ang = math.atan2(out_dir[1], out_dir[0])
        cx, cy = x + out_dir[0] * 0.30, y + out_dir[1] * 0.30
        z = z0 + 2.3
        while z < z1 - 0.2:
            arc_tube(acc, cx, cy, z, 0.72, ang - 1.85, ang + 1.85, 6, 0.035, mat)
            z += 0.62
        for s in (-0.7, 0.0, 0.7):
            bx1 = cx + math.cos(ang + s) * 0.72
            by1 = cy + math.sin(ang + s) * 0.72
            cyl(acc, (bx1, by1, z0 + 2.3), (bx1, by1, z1 - 0.2), 0.032, 0.032, 4, mat)


def railing(acc, p0, p1, z, h=1.05, mat='Kit_Iron', posts=4, tube_r=0.045):
    """直线护栏（两横杆 + 立柱）。"""
    x0, y0 = p0
    x1, y1 = p1
    cyl(acc, (x0, y0, z + h), (x1, y1, z + h), tube_r, tube_r, 4, mat)
    cyl(acc, (x0, y0, z + h * .55), (x1, y1, z + h * .55), tube_r * .8, tube_r * .8, 4, mat)
    for k in range(posts + 1):
        t = k / float(posts)
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        cyl(acc, (x, y, z), (x, y, z + h), tube_r * 1.1, tube_r * 1.1, 4, mat)


def stair_flight(acc, x0, z0, x1, z1, y, w, mat='Kit_Iron', rail=True, steps=None):
    """一段直跑钢梯（沿 X 走向，y = 梯中心线）；steps 缺省按踏高 0.20 推。"""
    run = x1 - x0
    rise = z1 - z0
    if steps is None:
        steps = max(4, int(abs(rise) / 0.21))
    pitch = math.atan2(abs(rise), abs(run))
    rot_y = -pitch if run > 0 else pitch               # 斜梁：随走向升/降
    L = math.hypot(run, rise)
    cx, cz = (x0 + x1) * .5, (z0 + z1) * .5
    for s in (-1.0, 1.0):
        sbox(acc, (cx, y + s * (w * .5 - 0.04), cz), (L, 0.09, 0.30), mat, rot=(0.0, rot_y, 0.0))
    # 踏步
    for k in range(steps):
        t0 = k / float(steps)
        t1 = (k + 1) / float(steps)
        sx = x0 + run * (t0 + t1) * .5
        sz = z0 + rise * t0 + 0.05
        sbox(acc, (sx, y, sz), (abs(run) / steps, w - 0.06, 0.06), mat)
    if rail:
        for s in (-1.0, 1.0):
            yy = y + s * (w * .5 + 0.05)
            cyl(acc, (x0, yy, z0 + 1.05), (x1, yy, z1 + 1.05), 0.04, 0.04, 4, mat)
            for k in range(steps // 3 + 1):
                t = k / float(max(1, steps // 3))
                x = x0 + run * t
                z = z0 + rise * t
                cyl(acc, (x, yy, z), (x, yy, z + 1.05), 0.035, 0.035, 4, mat)


# ----------------------------------------------------------------------------
# 废弃道具：锈桶 / 荒草簇 / 碎石堆 / 土坑水渍 / 裂缝
# ----------------------------------------------------------------------------

def drum(acc, cx, cy, z=0.0, r=0.30, h=0.92, yaw=0.0, tilt=0.0, mat='Kit_Rust',
         band='Kit_RustDark', lid='Kit_Iron'):
    """废油桶（竖立 / 倾倒）：两道箍 + 桶盖；tilt = 绕 X 倾倒角（弧度）。"""
    sub = MeshAcc()
    zs = [0.0, 0.10, 0.32, 0.36, 0.60, 0.64, h]
    rings = [ring_pts(0.0, 0.0, r * (1.02 if i in (2, 5) else 1.0), zz, 12)
             for i, zz in enumerate(zs)]
    loft(sub, rings, lambda b, s: band if b in (2, 4) else mat)
    fan(sub, (0.0, 0.0, h - 0.06), rings[-1], lid)
    fan(sub, (0.0, 0.0, 0.02), list(reversed(rings[0])), band, invert=True)
    acc.append(sub, (cx, cy, z), yaw_deg=yaw, tilt_deg=math.degrees(tilt))


def weeds(acc, cx, cy, n, seed, spread=1.2, mat_a='Kit_GrassDark', mat_b='Kit_GrassMid',
          hmin=0.25, hmax=0.75):
    """荒草簇：每簇 4 根细瘦歪斜草叶（薄板三棱柱）。"""
    rng = random.Random(seed)
    for i in range(n):
        bx = cx + rng.uniform(-spread, spread)
        by = cy + rng.uniform(-spread, spread)
        for k in range(4):
            hh = rng.uniform(hmin, hmax)
            m = mat_a if rng.random() < 0.7 else mat_b
            sbox(acc, (bx + rng.uniform(-0.12, 0.12), by + rng.uniform(-0.12, 0.12), hh * .5),
                 (0.05, 0.03, hh), m,
                 rot=(rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), rng.uniform(0, 3.14)))


def rubble(acc, cx, cy, n, seed, spread=1.8, mat='Kit_ConcreteMid', mat_dark='Kit_ConcreteDark',
           size=0.9, rust=None):
    """碎石/砼块堆。"""
    rng = random.Random(seed)
    for i in range(n):
        s = size * rng.uniform(0.25, 1.0)
        m = mat if rng.random() < 0.62 else (mat_dark if rng.random() < 0.75 else (rust or mat_dark))
        sbox(acc, (cx + rng.uniform(-spread, spread), cy + rng.uniform(-spread, spread),
                   s * rng.uniform(0.18, 0.42)),
             (s * rng.uniform(0.7, 1.5), s * rng.uniform(0.7, 1.5), s * rng.uniform(0.5, 1.1)),
             m, rot=(rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), rng.uniform(0, 3.14)))


def patch_poly(acc, cx, cy, rx, ry, z, mat, sides=9, seed=0, thick=0.06):
    """贴地不规则薄片（泥地/积水/剥落水泥面）。"""
    rng = random.Random(seed)
    roff = [rng.uniform(0.72, 1.28) for _ in range(sides)]
    pts = ring_pts(cx, cy, 1.0, z, sides, roff=roff)
    pts = [(cx + (p[0] - cx) * rx, cy + (p[1] - cy) * ry, p[2]) for p in pts]
    lo = [(p[0], p[1], p[2] - thick) for p in pts]
    acc.add(pts, [tuple(range(sides))], mat)
    acc.add(list(reversed(lo)), [tuple(range(sides))], mat)
    for i in range(sides):
        i2 = (i + 1) % sides
        acc.add([lo[i], lo[i2], pts[i2], pts[i]], [(0, 1, 2, 3)], mat)


def crack(acc, x0, y0, x1, y1, seed, z=0.05, w=0.16, mat='Kit_ConcreteDark', segs=5):
    """地面裂缝：折线薄片。"""
    rng = random.Random(seed)
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    if L < 1e-6:
        return
    ang = math.atan2(dy, dx)
    for k in range(segs):
        t = (k + 0.5) / segs
        px = x0 + dx * t + rng.uniform(-0.25, 0.25)
        py = y0 + dy * t + rng.uniform(-0.25, 0.25)
        sbox(acc, (px, py, z), (L / segs * 1.15, w * rng.uniform(0.7, 1.3), 0.05), mat,
             rot=(0.0, 0.0, ang + rng.uniform(-0.35, 0.35)))


# ============================================================================
# 分件 ①：主厂房（压型钢墙 + 塌顶 + 破窗 + 卷帘门）
# ============================================================================

HALL_L, HALL_W = 16.0, 10.0          # 长(X) × 宽(Y)
HALL_Z0, HALL_EAVE, HALL_RIDGE = 0.5, 7.2, 9.0


def build_hall(acc, dress=True):
    """废弃反应车间：局塌屋面露桁架、破窗、半开卷帘门、管道出墙。"""
    hx, hy = HALL_L * .5, HALL_W * .5
    pitch = math.atan2(HALL_RIDGE - HALL_EAVE, hy)
    wall = 'Kit_SteelBlue'
    sbox(acc, (0, 0, 0.20), (HALL_L + 0.8, HALL_W + 0.8, 0.40), 'Kit_ConcreteMid')
    sbox(acc, (0, 0, 0.30), (HALL_L - 0.3, HALL_W - 0.3, 0.60), 'Kit_ConcreteMid')
    # ---- 长墙：下段 / 窗带 / 上段（窗带处不放墙 = 真窗洞）
    for inward, y in ((-1.0, -hy), (1.0, hy)):                   # -y 面朝院子（正面）
        for x0, x1 in ((-hx, -2.6), (2.6, hx)) if inward < 0 else ((-hx, hx),):
            wall_seg(acc, 'x', x0, x1, y, HALL_Z0, 3.85, wall, ribs=int((x1 - x0) / 0.8),
                     rib_mat='Kit_Rust', inward=inward)
            wall_seg(acc, 'x', x0, x1, y, 5.25, HALL_EAVE, wall, ribs=int((x1 - x0) / 0.8),
                     rib_mat='Kit_Rust', inward=inward)
        bays = 6 if inward > 0 else 5
        window_band(acc, 'x', -hx + 0.3, hx - 0.3, y, 3.85, 5.25, bays, seed=7 if inward > 0 else 3,
                    inward=inward, frame='Kit_Iron', boarded='Kit_Rust')
    # ---- 山墙（含山尖）
    for x in (-hx, hx):
        wall_seg(acc, 'y', -hy + 0.2, hy - 0.2, x, HALL_Z0, HALL_EAVE, wall,
                 ribs=6, rib_mat='Kit_Rust', inward=1.0 if x > 0 else -1.0)
        tri_prism(acc, 'x', x, -hy, hy, HALL_EAVE, HALL_RIDGE, 0.16, wall)
    window_band(acc, 'y', -hy + 1.4, hy - 1.4, -hx, 3.85, 5.25, 2, seed=5, inward=-1.0,
                frame='Kit_Iron', boarded='Kit_Rust')
    # ---- 屋面（双坡压型板）：正面（-y）坡在中段塌落露桁架
    def roof_slope(sign, x0, x1, mat='Kit_SteelPale'):
        L = x1 - x0
        if L <= 0.05:
            return
        ymid = sign * hy * .5
        zmid = (HALL_EAVE + HALL_RIDGE) * .5
        slope_len = math.hypot(hy, HALL_RIDGE - HALL_EAVE)
        sbox(acc, ((x0 + x1) * .5, ymid, zmid + 0.06), (L, slope_len, 0.14), mat,
             rot=(-sign * pitch, 0.0, 0.0))
        ribs = max(2, int(L / 0.85))
        for k in range(ribs):
            rx = x0 + L * (k + 0.5) / ribs
            m = 'Kit_Rust' if (k * 5 + int(abs(x0))) % 5 == 0 else mat
            sbox(acc, (rx, ymid, zmid + 0.17), (0.13, slope_len, 0.10), m,
                 rot=(-sign * pitch, 0.0, 0.0))
    roof_slope(-1.0, -hx - 0.3, -0.4)                            # 正面坡（塌口左侧）
    roof_slope(-1.0, 3.2, hx + 0.3)                              # 正面坡（塌口右侧）
    roof_slope(1.0, -hx - 0.3, hx + 0.3)                         # 背面坡（完整）
    # 塌口：歪斜下垂的挂板 + 断檩 + 露出的桁架
    sbox(acc, (1.4, -3.1, 6.4), (3.6, 3.4, 0.14), 'Kit_SteelPale', rot=(0.95, 0.25, 0.30))
    sbox(acc, (0.6, -2.2, 7.9), (3.2, 0.13, 0.13), 'Kit_Rust', rot=(0.0, 0.0, 0.22))
    for x in (-1.2, 1.0, 3.0, 5.0):                              # 断檩（悬空短段）
        sbox(acc, (x, -3.9, 7.55 - abs(x) * 0.05), (1.1, 0.12, 0.12), 'Kit_Rust', rot=(0.3, 0.0, 0.0))
    # ---- 桁架 ×5
    for tx in (-6.4, -3.2, 0.0, 3.2, 6.4):
        sbox(acc, (tx, 0.0, HALL_EAVE - 0.35), (0.14, HALL_W - 0.5, 0.16), 'Kit_Rust')
        for s in (-1.0, 1.0):
            sbox(acc, (tx, s * hy * .5, (HALL_EAVE + HALL_RIDGE) * .5 - 0.30),
                 (0.13, math.hypot(hy, HALL_RIDGE - HALL_EAVE), 0.14), 'Kit_Rust',
                 rot=(-s * pitch, 0.0, 0.0))
        sbox(acc, (tx, 0.0, (HALL_EAVE + HALL_RIDGE) * .5 - 0.30), (0.12, 0.12, 1.7), 'Kit_Rust')
        sbox(acc, (tx, -2.4, HALL_EAVE + 0.25), (0.11, 2.9, 0.11), 'Kit_Rust', rot=(0.32, 0.0, 0.0))
        sbox(acc, (tx, 2.4, HALL_EAVE + 0.25), (0.11, 2.9, 0.11), 'Kit_Rust', rot=(-0.32, 0.0, 0.0))
    # ---- 正面卷帘门（半开、扭曲）+ 导轨 + 卷筒
    dark = 'Kit_GlassDark'
    sbox(acc, (0.0, -hy + 0.30, 2.70), (5.2, 0.10, 4.40), dark)
    for s in (-2.6, 2.6):
        sbox(acc, (s, -hy + 0.22, 2.90), (0.16, 0.30, 4.8), 'Kit_Iron')
    sbox(acc, (0.0, -hy + 0.22, 5.30), (5.5, 0.30, 0.22), 'Kit_Iron')
    cyl(acc, (-2.5, -hy + 0.22, 5.55), (2.5, -hy + 0.22, 5.55), 0.28, 0.28, 8, 'Kit_Rust')
    sbox(acc, (1.1, -hy + 0.34, 4.35), (4.0, 0.10, 2.6), 'Kit_SteelPale', rot=(0.30, 0.0, 0.16))
    for k in range(5):
        sbox(acc, (1.1 - 1.8 + k * 0.9, -hy + 0.42, 4.35), (0.13, 0.08, 2.6), 'Kit_Rust',
             rot=(0.30, 0.0, 0.16))
    # ---- 背面人员门（门扇歪挂）
    sbox(acc, (4.6, hy - 0.30, 1.65), (1.20, 0.10, 2.30), dark)
    sbox(acc, (5.35, hy - 0.45, 1.62), (1.15, 0.07, 2.20), 'Kit_Rust', rot=(0.0, 0.0, -0.95))
    for s in (3.95, 5.25):
        sbox(acc, (s, hy - 0.26, 1.70), (0.12, 0.22, 2.5), 'Kit_Iron')
    # ---- 内部机器剪影（透过门窗读作车间深处）
    for cx, cy, sz, m in ((-3.0, 1.6, (2.6, 1.8, 3.4), dark), (2.4, -1.2, (2.0, 2.4, 2.8), dark),
                          (5.2, 2.6, (1.6, 1.6, 4.2), dark)):
        sbox(acc, (cx, cy, HALL_Z0 + sz[2] * .5), sz, m)
    cyl(acc, (-5.0, -2.0, HALL_Z0), (-5.0, -2.0, 5.0), 0.8, 0.8, 10, dark)
    cyl(acc, (-3.0, 3.4, 3.2), (4.0, 3.4, 3.2), 0.42, 0.42, 8, dark)
    # ---- 屋面杂件：通风器 / 屋顶机组
    for vx in (-5.2, 1.8):
        cyl(acc, (vx, 1.6, HALL_RIDGE - 0.2), (vx, 1.6, HALL_RIDGE + 0.5), 0.26, 0.26, 8, 'Kit_Rust')
        cyl(acc, (vx, 1.6, HALL_RIDGE + 0.5), (vx, 1.6, HALL_RIDGE + 0.78), 0.42, 0.16, 8, 'Kit_SteelPale')
    sbox(acc, (-2.2, 2.2, HALL_EAVE + 1.05), (1.7, 1.2, 1.1), 'Kit_SteelBlue', rot=(-pitch, 0.0, 0.0))
    cyl(acc, (-2.2, 2.2, HALL_EAVE + 1.65), (-2.2, 2.2, HALL_EAVE + 1.75), 0.42, 0.42, 10, 'Kit_Rust')
    # ---- 落水管 + 墙面杂件 + 招牌
    for dx in (-6.2, 6.2):
        cyl(acc, (dx, -hy - 0.16, 0.30), (dx, -hy - 0.16, 6.9), 0.11, 0.11, 6, 'Kit_Rust')
        for bz in (1.6, 3.6, 5.6):
            sbox(acc, (dx, -hy - 0.10, bz), (0.10, 0.20, 0.08), 'Kit_Iron')
    sbox(acc, (0.0, hy + 0.14, 6.3), (4.4, 0.12, 1.0), 'Kit_ConcreteMid')
    for k in range(5):
        sbox(acc, (-1.7 + k * 0.85, hy + 0.22, 6.3), (0.5, 0.06, 0.55), dark)
    sbox(acc, (-7.6, hy + 0.13, 4.4), (0.9, 0.22, 1.1), 'Kit_Iron')
    # ---- 出墙管桥（接场地管廊）
    for z, r in ((5.6, 0.30), (6.3, 0.22), (4.9, 0.16)):
        cyl(acc, (hx, 2.4, z), (hx + 3.4, 2.4, z), r, r, 8, 'Kit_Rust')
        annulus(acc, hx + 3.4, 2.4, r, r * 1.35, z, 8, 'Kit_Iron', thick=0.10)
    # ---- 山墙爬梯（歪） + 破损墙段碎石
    ladder(acc, hx + 0.22, -3.2, 0.30, HALL_EAVE + 0.2, 'Kit_Rust', out_dir=(1.0, 0.0),
           cage=False)
    sbox(acc, (hx - 0.1, -3.2, 3.6), (0.2, 0.5, 0.5), 'Kit_Iron', rot=(0.0, 0.0, 0.2))
    if dress:
        weeds(acc, -hx + 1.0, -hy - 0.6, 3, 11, spread=1.2)
        weeds(acc, hx - 1.5, hy + 0.7, 2, 12, spread=1.0)
        weeds(acc, -2.0, -hy - 0.8, 2, 13, spread=1.1)
        rubble(acc, 5.4, -hy - 1.0, 6, 21, spread=1.0, size=0.7, rust='Kit_Rust')
        rubble(acc, -4.0, -hy - 0.9, 4, 22, spread=0.8, size=0.6)
        drum(acc, -7.2, -hy - 1.3, yaw=0.4, mat='Kit_Rust')
        drum(acc, -6.5, -hy - 1.9, yaw=1.1, tilt=0.5, mat='Kit_RustDark', band='Kit_Rust')


# ============================================================================
# 分件 ②：蒸馏塔（18.4 m，带三层平台 + 锈痕 + 折弯放空管）
# ============================================================================

def build_column(acc, tall=True, dress=True):
    """精馏塔：锥台塔身 + 三层环形平台 + 爬梯护笼 + 接管/人孔 + 顶部折弯放空。"""
    S = 20
    if tall:
        zs = [0.60, 1.40, 2.40, 4.20, 6.20, 8.40, 10.60, 12.80, 15.00, 16.60, 17.40, 18.10]
        rs = [2.45, 2.25, 2.18, 2.14, 2.10, 2.06, 2.02, 1.97, 1.90, 1.82, 1.72, 1.40]
        plats = (4.20, 9.60, 15.00)
    else:
        zs = [0.50, 1.20, 2.20, 3.60, 5.20, 6.80, 8.20, 9.30, 10.00, 10.50]
        rs = [1.95, 1.80, 1.74, 1.70, 1.66, 1.62, 1.58, 1.52, 1.44, 1.10]
        plats = (3.60, 7.40)
    top = zs[-1]

    def matf(band, sector):
        z = zs[band]
        if 6.30 <= z < 7.30 or 12.9 <= z < 13.9:
            return 'Kit_PaintYellow'
        f = streak_matf('Kit_SteelBlue', 101 if tall else 102, S, cover=0.30)
        return f(band, sector)

    sbox(acc, (0, 0, 0.30), (5.6, 5.6, 0.60), 'Kit_ConcreteMid')
    sbox(acc, (0, 0, 0.72), (4.9, 4.9, 0.36), 'Kit_ConcreteMid')
    rings = [ring_pts(0, 0, r, z, S) for r, z in zip(rs, zs)]
    loft(acc, rings, matf, cap_bot='Kit_SteelBlue', smooth=True)
    if tall:
        dr = [1.40, 1.28, 1.00, 0.55]
        dz = [top, top + 0.26, top + 0.50, top + 0.68]
        dro = [ring_pts(0, 0, r, z, S) for r, z in zip(dr, dz)]
        loft(acc, [rings[-1]] + dro, lambda b, s: 'Kit_SteelPale', smooth=True)
        fan(acc, (0, 0, top + 0.76), dro[-1], 'Kit_SteelPale')
        cyl(acc, (0.10, 0.0, top + 0.70), (0.95, 0.25, top + 1.35), 0.20, 0.20, 8, 'Kit_Rust')
        annulus(acc, 0.95, 0.25, 0.20, 0.32, top + 1.35, 8, 'Kit_Iron', thick=0.09)
    else:
        loft(acc, [rings[-1], ring_pts(0, 0, 0.95, top + 0.35, S)],
             lambda b, s: 'Kit_SteelPale')
        fan(acc, (0, 0, top + 0.52), ring_pts(0, 0, 0.95, top + 0.35, S), 'Kit_SteelPale')
    # ---- 环形平台 + 护栏 + 爬梯
    for i, pz in enumerate(plats):
        rr = rs[min(range(len(zs)), key=lambda k: abs(zs[k] - pz))] + 0.04
        annulus(acc, 0, 0, rr, rr + 1.30, pz, S, 'Kit_Iron', thick=0.11, mat_edge='Kit_Rust')
        for s, zz in ((-0.22, 0.55), (0.10, 1.08)):
            arc_tube(acc, 0, 0, pz + zz, rr + 1.24, 0.35, 2.0 * math.pi - 0.05, 14, 0.045, 'Kit_Iron')
        for k in range(S):
            a = 2.0 * math.pi * k / S
            cyl(acc, (math.cos(a) * (rr + 1.24), math.sin(a) * (rr + 1.24), pz + 0.10),
                (math.cos(a) * (rr + 1.24), math.sin(a) * (rr + 1.24), pz + 1.08), 0.04, 0.04, 4,
                'Kit_Iron')
        z0 = plats[i - 1] if i > 0 else 0.60
        ladder(acc, rr + 0.24, 0.0, z0, pz + 1.0, 'Kit_Iron', out_dir=(1.0, 0.0),
               cage=(i == len(plats) - 1))
    for i, pz in enumerate(plats[:-1]):
        zz = plats[i + 1]
        ladder(acc, -(rs[0] + 0.24), 0.0, pz + 1.0, zz + 1.0, 'Kit_Iron', out_dir=(-1.0, 0.0),
               cage=(i == 0))
    # ---- 接管（带法兰）+ 人孔
    for z, ya, r in ((3.0, math.radians(200), 0.22), (7.8, math.radians(20), 0.26),
                     (11.6, math.radians(300), 0.20), (14.6, math.radians(150), 0.18)):
        rr = rs[min(range(len(zs)), key=lambda k: abs(zs[k] - z))]
        cx, cy = math.cos(ya) * rr, math.sin(ya) * rr
        ex, ey = math.cos(ya) * (rr + 0.95), math.sin(ya) * (rr + 0.95)
        cyl(acc, (cx, cy, z), (ex, ey, z), r, r, 8, 'Kit_Rust')
        annulus(acc, ex, ey, r, r * 1.5, z, 8, 'Kit_Iron', thick=0.09)
    for z, ya in ((8.4, math.pi), (14.0, math.pi * 0.55)):
        rr = rs[min(range(len(zs)), key=lambda k: abs(zs[k] - z))]
        cx, cy = math.cos(ya) * rr, math.sin(ya) * rr
        cyl(acc, (cx * 0.95, cy * 0.95, z), (cx * 1.10, cy * 1.10, z), 0.42, 0.42, 10, 'Kit_Iron')
        for k in range(8):
            a = 2.0 * math.pi * k / 8.0
            sbox(acc, (cx * 1.12, cy * 1.12, z + math.sin(a) * 0.34), (0.08, 0.08, 0.08), 'Kit_Rust')
    # ---- 塔底接管 + 阀轮
    cyl(acc, (1.6, 0.0, 1.55), (5.0, 0.0, 1.55), 0.34, 0.34, 10, 'Kit_Rust')
    annulus(acc, 5.0, 0.0, 0.34, 0.50, 1.55, 10, 'Kit_Iron', thick=0.10)
    cyl(acc, (0.0, -1.6, 1.15), (0.0, -4.4, 1.15), 0.26, 0.26, 8, 'Kit_Rust')
    cyl(acc, (0.0, -4.4, 1.15), (0.0, -4.4, 2.40), 0.26, 0.26, 8, 'Kit_Rust')
    arc_tube(acc, 1.1, -4.4, 1.90, 0.30, 0.0, 2.0 * math.pi - 0.05, 10, 0.05, 'Kit_Brass')
    for k in range(4):
        a = math.pi * k / 2.0
        cyl(acc, (1.1, -4.4, 1.90), (1.1 + math.cos(a) * 0.30, -4.4 + math.sin(a) * 0.30, 1.90),
            0.04, 0.04, 4, 'Kit_Brass')
    if dress:
        weeds(acc, 2.9, -2.6, 3, 31, spread=1.4)
        weeds(acc, -2.6, 2.6, 2, 32, spread=1.2)
        rubble(acc, -2.2, -3.0, 4, 33, spread=1.1, size=0.7)


# ============================================================================
# 分件 ③：管廊（钢架 + 多层管道 + 断管垂吊）
# ============================================================================

def build_pipe_rack(acc, dress=True):
    """管廊：X 向 16 m、两层横梁，管道错落（含断口、塌腰、垂吊断管、阀组）。"""
    x0, x1 = -8.0, 8.0
    tier = (4.40, 6.60)
    stations = (-8.0, -4.0, 0.0, 4.0, 8.0)
    for sx in stations:
        bent = (sx == 4.0)
        for sy in (-1.15, 1.15):
            sbox(acc, (sx, sy, 0.25), (1.3, 1.3, 0.50), 'Kit_ConcreteMid')
            if bent and sy < 0:
                sbox(acc, (sx, sy - 0.30, 3.6), (0.30, 0.30, 6.4), 'Kit_SteelBlue',
                     rot=(-0.13, 0.0, 0.0))
            else:
                sbox(acc, (sx, sy, 1.05), (0.30, 0.30, 1.1), 'Kit_Rust')
                sbox(acc, (sx, sy, 4.05), (0.30, 0.30, 4.9), 'Kit_SteelBlue')
        for tz in tier:
            sbox(acc, (sx, 0.0, tz - 0.28), (0.26, 2.9, 0.38), 'Kit_SteelBlue')
    for tz in tier:
        for sy in (-1.15, 1.15):
            sbox(acc, (0.0, sy, tz - 0.02), (x1 - x0 + 0.9, 0.22, 0.40), 'Kit_SteelBlue')
    # ---- 一层管道（错落半径/材质；一根短断、一根塌腰）
    row1 = [(-0.85, 0.34, 'Kit_Rust'), (-0.45, 0.26, 'Kit_SteelPale'),
            (-0.05, 0.20, 'Kit_Rust'), (0.35, 0.16, 'Kit_SteelPale'), (0.85, 0.26, 'Kit_Rust')]
    for oy, r, m in row1:
        if abs(oy + 0.85) < 1e-6:
            cyl(acc, (x0, oy, tier[0] + r + 0.16), (1.2, oy, tier[0] + r + 0.16), r, r, 8, m)
            annulus(acc, 1.2, oy, r, r * 1.4, tier[0] + r + 0.16, 8, 'Kit_RustDark', thick=0.08)
            continue
        if abs(oy - 0.35) < 1e-6:                                 # 塌腰段
            pz = tier[0] + r + 0.16
            cyl(acc, (x0, oy, pz), (-2.0, oy, pz), r, r, 8, m)
            cyl(acc, (-2.0, oy, pz), (0.0, oy, pz - 0.42), r, r, 8, m)
            cyl(acc, (0.0, oy, pz - 0.42), (2.0, oy, pz), r, r, 8, m)
            cyl(acc, (2.0, oy, pz), (x1, oy, pz), r, r, 8, m)
            sbox(acc, (0.0, oy, pz - 0.70), (0.3, 0.3, 0.55), 'Kit_Rust')
            continue
        cyl(acc, (x0, oy, tier[0] + r + 0.16), (x1 + 0.6, oy, tier[0] + r + 0.16), r, r, 10, m, smooth=True)
    # ---- 二层管道（含垂吊断管）
    row2 = [(-0.80, 0.28, 'Kit_SteelPale'), (-0.30, 0.22, 'Kit_Rust'),
            (0.25, 0.18, 'Kit_SteelPale'), (0.80, 0.09, 'Kit_Brass')]
    for oy, r, m in row2:
        pz = tier[1] + r + 0.16
        cyl(acc, (x0, oy, pz), (x1 + 0.6, oy, pz), r, r, 10, m, smooth=True)
    pz = tier[1] + 0.44
    cyl(acc, (2.0, -0.30, pz), (2.0, -0.30, pz - 0.9), 0.22, 0.22, 8, 'Kit_Rust')
    cyl(acc, (2.0, -0.30, pz - 0.9), (2.6, -0.10, pz - 2.4), 0.22, 0.21, 8, 'Kit_Rust')
    cyl(acc, (2.6, -0.10, pz - 2.4), (2.7, -0.05, pz - 3.9), 0.21, 0.20, 8, 'Kit_Rust')
    annulus(acc, 2.7, -0.05, 0.20, 0.30, pz - 3.9, 8, 'Kit_RustDark', thick=0.08)
    # ---- 立管 + 阀组（落到地面）
    for dx, oy in ((-4.0, -1.35), (6.0, 1.35)):
        cyl(acc, (dx, oy, 0.55), (dx, oy, tier[0]), 0.24, 0.24, 8, 'Kit_Rust')
        cyl(acc, (dx, oy, 0.55), (dx, oy + (2.0 if oy > 0 else -2.0), 0.55), 0.24, 0.24, 8, 'Kit_Rust')
        arc_tube(acc, dx, oy + (1.0 if oy > 0 else -1.0), 1.25, 0.30, 0.0, 2.0 * math.pi - 0.05,
                 10, 0.05, 'Kit_Brass')
    sbox(acc, (-4.0, -1.35, 0.20), (0.9, 0.9, 0.40), 'Kit_ConcreteMid')
    if dress:
        weeds(acc, -1.5, -2.6, 3, 41, spread=1.6)
        weeds(acc, 5.0, 0.0, 2, 42, spread=1.4)
        rubble(acc, -6.5, -1.8, 4, 43, spread=1.2, size=0.7, rust='Kit_Rust')
        drum(acc, 7.0, -2.2, yaw=0.9, mat='Kit_Rust')


# ============================================================================
# 分件 ④：球形储罐（6 腿 + 赤道平台）
# ============================================================================

def build_sphere_tank(acc, dress=True):
    """球罐：r=4.0 支于 6 腿，赤道环形走道 + 爬梯 + 底部阀组接管。"""
    S = 28
    R = 4.0
    cz = 8.30
    legs = 6
    for k in range(legs):
        a = 2.0 * math.pi * k / legs + math.radians(30)
        lx, ly = math.cos(a) * 3.0, math.sin(a) * 3.0
        sbox(acc, (lx, ly, 0.25), (1.2, 1.2, 0.50), 'Kit_ConcreteMid')
        cyl(acc, (lx, ly, 0.50), (lx, ly, 4.45), 0.30, 0.28, 8, 'Kit_SteelBlue')
        a2 = 2.0 * math.pi * ((k + 1) % legs) / legs + math.radians(30)
        mx = (lx + math.cos(a2) * 3.0) * .5
        my = (ly + math.sin(a2) * 3.0) * .5
        dl = math.hypot(math.cos(a2) * 3.0 - lx, math.sin(a2) * 3.0 - ly)
        cyl(acc, (lx, ly, 1.6), (mx, my, 3.4), 0.10, 0.10, 5, 'Kit_Iron')
        cyl(acc, (mx, my, 3.4), (math.cos(a2) * 3.0, math.sin(a2) * 3.0, 1.6), 0.10, 0.10, 5,
            'Kit_Iron')
    for k in range(S):
        a = 2.0 * math.pi * k / S
        cyl(acc, (math.cos(a) * 3.1, math.sin(a) * 3.1, 4.45),
            (math.cos(a) * 3.35, math.sin(a) * 3.35, 5.10), 0.09, 0.09, 5, 'Kit_Iron')
    sphere_shell(acc, 0.0, 0.0, cz, R, S, 9,
                 streak_matf('Kit_SteelPale', 51, S, cover=0.32, alt_every=9), smooth=True)
    annulus(acc, 0, 0, 3.7, 3.98, cz - 3.6, S, 'Kit_SteelPale', thick=0.5)
    # 赤道走道 + 护栏
    annulus(acc, 0, 0, R - 0.15, R + 1.15, cz + 0.05, S, 'Kit_Iron', thick=0.12,
            mat_edge='Kit_Rust')
    for zz in (0.62, 1.10):
        arc_tube(acc, 0, 0, cz + zz, R + 1.10, 0.0, 2.0 * math.pi - 0.05, 20, 0.045, 'Kit_Iron')
    for k in range(S):
        a = 2.0 * math.pi * k / S
        cyl(acc, (math.cos(a) * (R + 1.10), math.sin(a) * (R + 1.10), cz + 0.10),
            (math.cos(a) * (R + 1.10), math.sin(a) * (R + 1.10), cz + 1.10), 0.04, 0.04, 4,
            'Kit_Iron')
    ladder(acc, R + 0.45, 0.0, 0.55, cz + 1.0, 'Kit_Iron', out_dir=(1.0, 0.0), cage=True)
    # 顶部平台 + 底部阀组
    annulus(acc, 0, 0, 0.0, 1.30, cz + R - 0.10, 10, 'Kit_Iron', thick=0.12)
    railing(acc, (-1.2, -1.2), (1.2, -1.2), cz + R - 0.05, mat='Kit_Iron', posts=3)
    railing(acc, (-1.2, 1.2), (1.2, 1.2), cz + R - 0.05, mat='Kit_Iron', posts=3)
    cyl(acc, (0.0, 0.0, cz - R + 0.2), (0.0, 0.0, 1.20), 0.36, 0.36, 10, 'Kit_Rust')
    cyl(acc, (0.0, 0.0, 1.20), (3.6, -1.0, 1.20), 0.36, 0.36, 10, 'Kit_Rust')
    annulus(acc, 3.6, -1.0, 0.36, 0.52, 1.20, 10, 'Kit_RustDark', thick=0.09)
    arc_tube(acc, 1.2, -0.4, 2.10, 0.28, 0.0, 2.0 * math.pi - 0.05, 10, 0.05, 'Kit_Brass')
    if dress:
        weeds(acc, 3.6, 3.4, 3, 61, spread=1.6)
        rubble(acc, -4.2, -2.4, 5, 62, spread=1.4, size=0.8, rust='Kit_Rust')
        drum(acc, 4.6, 0.6, yaw=0.3, mat='Kit_Rust')
        drum(acc, 5.3, 1.1, yaw=1.4, tilt=1.45, mat='Kit_RustDark', band='Kit_Rust')


# ============================================================================
# 分件 ⑤：罐区（三立式罐 + 围堰 + 联通管）
# ============================================================================

def _v_tank(acc, cx, cy, r, h, seed, dent=False, band='Kit_PaintYellow'):
    """立式储罐：基础环 + 罐身（锈痕/接缝带/可选凹瘪）+ 锥顶 + 爬梯 + 顶部护栏。"""
    S = 20
    sbox(acc, (cx, cy, 0.22), (r * 2.3, r * 2.3, 0.44), 'Kit_ConcreteMid')
    zs = [0.44, h * 0.22, h * 0.46, h * 0.70, h * 0.94, h]
    rs = [r, r * 0.995, r * 0.99, r * 0.985, r * 0.98, r * 0.975]
    roff = [1.0] * S
    if dent:
        for i in (3, 4, 5, 6):
            roff[i] = 0.84
    rings = []
    for k, (z, rr) in enumerate(zip(zs, rs)):
        ro = [roff[i] if k >= 2 else 1.0 for i in range(S)]
        rings.append(ring_pts(cx, cy, rr, z, S, roff=ro))
    loft(acc, rings, streak_matf('Kit_SteelPale', seed, S, cover=0.30, alt_every=8),
         cap_bot='Kit_SteelPale', smooth=True)
    for z in (h * 0.33, h * 0.66):                               # 接缝箍带
        loft(acc, [ring_pts(cx, cy, r * 1.012, z - 0.10, S), ring_pts(cx, cy, r * 1.012, z + 0.10, S)],
             lambda b, s: band if (s * 3 + seed) % 5 == 0 else 'Kit_Rust')
    if dent:                                                     # 破口下的塌边
        for i in (3, 4, 5, 6):
            a0 = 2.0 * math.pi * i / S
            a1 = 2.0 * math.pi * (i + 1) / S
            p0 = (cx + math.cos(a0) * r * 0.9, cy + math.sin(a0) * r * 0.9, h)
            p1 = (cx + math.cos(a1) * r * 0.9, cy + math.sin(a1) * r * 0.9, h)
            fan(acc, (cx, cy, h - 0.55), [p0, p1], 'Kit_RustDark')
    else:
        fan(acc, (cx, cy, h + 0.62), rings[-1], 'Kit_SteelPale')
    top_r = r * 0.9 if dent else r * 0.975
    annulus(acc, cx, cy, top_r - 0.16, top_r, h + 0.30 if not dent else h - 0.10, S, 'Kit_Iron',
            thick=0.10, mat_edge='Kit_Rust')
    for k in range(10):
        a = 2.0 * math.pi * k / 10.0
        cyl(acc, (cx + math.cos(a) * (top_r - 0.10), cy + math.sin(a) * (top_r - 0.10),
                  h + 0.35 if not dent else h - 0.05),
            (cx + math.cos(a) * (top_r - 0.10), cy + math.sin(a) * (top_r - 0.10),
             (h + 1.25) if not dent else (h + 0.75)), 0.04, 0.04, 4, 'Kit_Iron')
    cyl(acc, (cx + 0.4, cy, h + 0.55), (cx + 0.4, cy, h + 1.45), 0.15, 0.15, 8, 'Kit_Rust')
    ladder(acc, cx + r + 0.16, cy, 0.44, h + 0.35, 'Kit_Iron', out_dir=(1.0, 0.0),
           cage=(r > 2.5))
    cyl(acc, (cx + r, cy + 0.6, 1.05), (cx + r + 1.6, cy + 0.6, 1.05), 0.20, 0.20, 8, 'Kit_Rust')
    annulus(acc, cx + r + 1.6, cy + 0.6, 0.20, 0.32, 1.05, 8, 'Kit_RustDark', thick=0.08)


def build_tank_farm(acc, dress=True):
    """罐区：三罐一线 + 混凝土围堰 + 罐间联通管（一处断开）+ 罐顶平台。"""
    _v_tank(acc, -6.0, 0.0, 3.20, 8.50, 71, dent=True)
    _v_tank(acc, 0.5, 0.0, 2.60, 7.50, 72)
    _v_tank(acc, 6.4, 0.0, 2.20, 6.50, 73)
    # 围堰（三面墙，南侧开口）
    for x0, x1, y in ((-10.4, 10.4, 4.6), (-10.4, 10.4, -4.6)):
        sbox(acc, ((x0 + x1) * .5, y, 0.70), (x1 - x0, 0.42, 1.40), 'Kit_ConcreteMid')
        sbox(acc, ((x0 + x1) * .5, y + (0.30 if y > 0 else -0.30), 0.20), (x1 - x0 + 0.5, 1.1, 0.40),
             'Kit_ConcreteDark')
    sbox(acc, (10.4, 0.0, 0.70), (0.42, 9.2, 1.40), 'Kit_ConcreteMid')
    # 联通管（地面矮支架）+ 一根断口
    for dx in (-6.0, 0.5, 6.4):
        cyl(acc, (dx, -2.2, 0.95), (dx, 2.2, 0.95), 0.20, 0.20, 8, 'Kit_Rust')
    cyl(acc, (-2.8, -3.0, 1.15), (3.5, -3.0, 1.15), 0.26, 0.26, 8, 'Kit_Rust')
    cyl(acc, (3.5, -3.0, 1.15), (5.6, -3.0, 0.35), 0.26, 0.24, 8, 'Kit_Rust')
    annulus(acc, 5.6, -3.0, 0.24, 0.36, 0.35, 8, 'Kit_RustDark', thick=0.08)
    for dx in (-2.8, 0.4, 3.5):
        sbox(acc, (dx, -3.0, 0.35), (0.35, 0.4, 0.70), 'Kit_ConcreteMid')
    if dress:
        weeds(acc, -8.5, -3.6, 4, 81, spread=1.6)
        weeds(acc, 8.0, 3.2, 3, 82, spread=1.4)
        rubble(acc, -9.4, 3.4, 6, 83, spread=1.6, size=0.9, rust='Kit_Rust')
        drum(acc, 9.2, -3.6, yaw=0.7, mat='Kit_Rust')
        drum(acc, 8.6, -4.2, yaw=2.1, tilt=1.5, mat='Kit_RustDark', band='Kit_Rust')
        drum(acc, -1.2, 3.4, yaw=0.2, mat='Kit_Rust')


# ============================================================================
# 分件 ⑥：烟囱（26 m，残缺顶口 + 爬梯护笼）
# ============================================================================

def build_stack(acc, dress=True):
    """砖砼烟囱：锥台筒身 + 铁箍 + 褪色漆带 + 顶口残缺 + 护笼爬梯 + 底部烟道。"""
    S = 24
    zs = [0.70, 4.00, 8.00, 12.00, 16.00, 20.00, 23.00, 25.20, 26.00]
    rs = [2.30, 2.16, 2.05, 1.94, 1.84, 1.74, 1.66, 1.60, 1.54]
    sbox(acc, (0, 0, 0.35), (5.0, 5.0, 0.70), 'Kit_ConcreteDark')
    sbox(acc, (0, 0, 0.82), (4.2, 4.2, 0.40), 'Kit_ConcreteMid')

    def matf(band, sector):
        z = zs[band]
        if z >= 20.0:
            return 'Kit_PaintYellow' if (int(z) // 3) % 2 == 0 else 'Kit_ConcreteLight'
        f = streak_matf('Kit_ConcreteLight', 91, S, rust='Kit_Rust', alt='Kit_ConcreteDark',
                        cover=0.28, alt_every=6)
        return f(band, sector)

    rings = [ring_pts(0, 0, r, z, S) for r, z in zip(rs, zs)]
    # 顶口残缺：最后一环两个扇区掉高
    top = list(rings[-1])
    for i in (7, 8):
        a = 2.0 * math.pi * i / S
        top[i] = (math.cos(a) * rs[-1], math.sin(a) * rs[-1], 24.55)
    rings[-1] = top
    loft(acc, rings, matf, cap_bot='Kit_ConcreteMid', smooth=True)
    fan(acc, (0, 0, 24.30), ring_pts(0, 0, rs[-1] * 0.86, 24.30, S), 'Kit_ConcreteDark', invert=True)
    for z in (4.0, 8.0, 12.0, 16.0, 20.0):                       # 铁箍
        rr = rs[min(range(len(zs)), key=lambda k: abs(zs[k] - z))]
        loft(acc, [ring_pts(0, 0, rr + 0.05, z - 0.12, S), ring_pts(0, 0, rr + 0.05, z + 0.12, S)],
             lambda b, s: 'Kit_Rust')
    ladder(acc, rs[0] + 0.06, 0.0, 0.82, 20.5, 'Kit_Rust', out_dir=(1.0, 0.0), cage=True)
    for z in (6.0, 12.0, 18.0):                                  # 爬梯歇脚平台
        annulus(acc, 0, 0, rs[min(range(len(zs)), key=lambda k: abs(zs[k] - z))] - 0.1,
                rs[min(range(len(zs)), key=lambda k: abs(zs[k] - z))] + 0.85, z, S, 'Kit_Iron',
                thick=0.10)
    for yaw in (math.radians(150), math.radians(330)):           # 底部烟道
        ca, sa = math.cos(yaw), math.sin(yaw)
        cyl(acc, (ca * 1.8, sa * 1.8, 1.60), (ca * 6.0, sa * 6.0, 1.60), 0.62, 0.62, 10, 'Kit_Rust')
        annulus(acc, ca * 6.0, sa * 6.0, 0.62, 0.82, 1.60, 10, 'Kit_Iron', thick=0.12)
        sbox(acc, (ca * 4.2, sa * 4.2, 0.40), (1.0, 1.0, 0.80), 'Kit_ConcreteMid')
    if dress:
        weeds(acc, 3.4, 2.6, 3, 95, spread=1.6)
        rubble(acc, -3.6, 3.0, 5, 96, spread=1.6, size=1.0, rust='Kit_Rust')
        drum(acc, 4.4, -2.8, yaw=0.5, mat='Kit_Rust')


# ============================================================================
# 分件 ⑦：冷却塔（双曲线壳 + 底部架空支腿 + 水渍苔痕）
# ============================================================================

def build_cooling_tower(acc, dress=True):
    """冷却塔：双曲面壳（棱槽）h=22、r 底 8.0→喉 4.8→顶 5.6；底部 12 腿架空、壳内暗色。"""
    S = 40                                   # 弧面细分（创始人 2026-09-29：弧面要圆）
    h, r0, rt, rtop = 22.0, 8.0, 4.8, 5.60
    zs = [2.60, 4.0, 6.0, 9.0, 12.0, 15.0, 18.0, 20.5, h]

    def _rr(z):
        if z <= 15.0:
            t = (z - 2.60) / (15.0 - 2.60)
            return r0 + (rt - r0) * (t ** 0.62)
        u = (z - 15.0) / (h - 15.0)
        return rt + (rtop - rt) * (u ** 1.6)

    rs = [_rr(z) for z in zs]
    # 【实机口径修正 2026-09-29，两轮实测】壳体**不做周向棱槽**：在近似竖直的曲面上，
    # 任何幅度的交替半径都会让相邻扇区的法线差几度，而像素路径的漫反射只有三档 ⇒
    # 相邻扇区互相翻档，读成"棋盘格"（±5.5%）乃至"镂空壳"（±1.5%）；Blender 的连续染染看不出这个。
    # 改法照厂房压型墙的成功做法：壳体光滑，装饰改成**外凸细肋条**（见下方 ribs），
    # 肋条自身是单一面朝向、只落一档色，不参与翻档。
    roff = None

    def matf(band, sector):
        if zs[band] < 6.0:
            return 'Kit_ConcreteDark' if (sector * 5 + band) % 4 else 'Kit_ConcreteMid'
        if zs[band] < 9.0:
            return 'Kit_ConcreteMid' if (sector * 3 + band) % 3 else 'Kit_Rust'
        return patch_matf('Kit_ConcreteLight', 111, 'Kit_ConcreteMid', rate=6,
                          light='Kit_ConcreteLight')(band, sector)

    # 【闭合实体，2026-09-29 实测】壳体必须**双壁 + 顶口环盖 + 底环盖**闭合成实体：
    # 单面开壳（只有外壁）在 `recalc_face_normals` 下判不准朝向——实机剔背面后塔身成"镂空壳"
    # （Blender 双面染染完全看不出来）。闭合体的法线收口是确定的，这才是可判的。
    rings = [ring_pts(0, 0, r, z, S, roff=roff) for r, z in zip(rs, zs)]
    rings_in = [ring_pts(0, 0, max(r - 0.30, 0.5), z, S, roff=roff) for r, z in zip(rs, zs)]
    loft(acc, list(reversed(rings_in)), lambda b, s: 'Kit_ConcreteDark', smooth=True)   # 内壁（朝轴）
    loft(acc, rings, matf, smooth=True)                                                # 外壁
    for zb in (7.5, 12.5, 18.0):                                 # 施工缝箍带（打断大白面）
        rb = _rr(zb) * 1.02
        loft(acc, [ring_pts(0, 0, rb, zb - 0.22, S, roff=roff),
                   ring_pts(0, 0, rb, zb + 0.22, S, roff=roff)],
             lambda b, s: 'Kit_ConcreteMid' if (s * 3 + int(zb)) % 4 else 'Kit_Rust',
             cap_bot='Kit_ConcreteMid', cap_top='Kit_ConcreteMid')
    # 外凸竖肋 ×16（沿半径剖面折线，5 棱细管；代替会被色带翻档的周向棱槽）
    for k in range(16):
        a = 2.0 * math.pi * (k + 0.5) / 16.0
        ca, sa2 = math.cos(a), math.sin(a)
        zz = 2.70
        prev = None
        while zz <= h - 0.30:
            rr = _rr(zz) + 0.085
            pt = (ca * rr, sa2 * rr, zz)
            if prev is not None:
                cyl(acc, prev, pt, 0.075, 0.075, 8, 'Kit_ConcreteLight', smooth=True)
            prev = pt
            zz += 3.0
    # 顶口残缺两扇区
    top = list(rings[-1])
    for i in (9, 10):
        a = 2.0 * math.pi * i / S
        top[i] = (math.cos(a) * rs[-1], math.sin(a) * rs[-1], h - 0.85)
    rings[-1] = top
    rin_top = max(rings_in[-1][0][0] ** 2 + rings_in[-1][0][1] ** 2, 0.25) ** 0.5
    annulus(acc, 0, 0, rin_top, rs[-1], h - 0.9, S, 'Kit_ConcreteLight', thick=0.22)   # 顶口环盖
    annulus(acc, 0, 0, rs[0] - 0.30, rs[0], zs[0], S, 'Kit_ConcreteDark', thick=0.24)  # 底环盖
    annulus(acc, 0, 0, 0.05, rs[0] - 0.30, 2.40, S, 'Kit_ConcreteDark', thick=0.30)     # 壳内地面
    # 底部 12 条斜腿（架空感）+ 地面环形水池
    S_LEG = 12
    for k in range(S_LEG):
        a = 2.0 * math.pi * k / S_LEG
        lx, ly = math.cos(a) * r0 * 0.94, math.sin(a) * r0 * 0.94
        cyl(acc, (lx * 0.98, ly * 0.98, 0.0), (lx * 0.86, ly * 0.86, 2.70), 0.55, 0.42, 6,
            'Kit_ConcreteMid')
        sbox(acc, (lx * 1.02, ly * 1.02, 0.30), (1.5, 1.5, 0.60), 'Kit_ConcreteDark',
             rot=(0.0, 0.0, a))
    patch_poly(acc, 0, 0, r0 * 1.22, r0 * 1.22, 0.10, 'Kit_WetSand', sides=12, seed=113, thick=0.10)
    patch_poly(acc, 1.2, -1.6, r0 * 0.5, r0 * 0.45, 0.16, 'Kit_RustDark', sides=9, seed=114, thick=0.06)
    # 塌落的水管与残渣
    cyl(acc, (r0 * 0.7, -1.2, 0.35), (r0 * 1.7, -1.2, 0.35), 0.34, 0.34, 8, 'Kit_Rust')
    cyl(acc, (r0 * 1.7, -1.2, 0.35), (r0 * 1.7, -2.6, 0.35), 0.34, 0.34, 8, 'Kit_Rust')
    annulus(acc, r0 * 1.7, -2.6, 0.34, 0.48, 0.35, 8, 'Kit_RustDark', thick=0.09)
    if dress:
        weeds(acc, -r0 * 1.1, -2.0, 4, 115, spread=2.0)
        weeds(acc, r0 * 1.05, 3.0, 3, 116, spread=1.8)
        rubble(acc, -4.0, r0 * 0.9, 6, 117, spread=2.0, size=1.1, rust='Kit_Rust')
        drum(acc, r0 * 0.5, 3.6, yaw=1.2, mat='Kit_Rust')


# ============================================================================
# 分件 ⑧：办公楼（旁边的楼：4 层 + 破窗 + 外挂消防梯塌段 + 屋顶水箱）
# ============================================================================

def build_office(acc, dress=True):
    """厂区办公楼：4 层 14×9、层层窗带（破窗/钉板/残玻璃）、入口雨棚、
    南侧外挂消防梯（顶层梯段塌落）、屋顶水箱 + 楼梯间 + 女儿墙缺口。"""
    L, W = 14.0, 9.0
    FH = 3.4
    FLOORS = 4
    z_plinth = 0.60
    hx, hy = L * .5, W * .5
    wallc, band, dark = 'Kit_ConcreteLight', 'Kit_ConcreteMid', 'Kit_GlassDark'
    sbox(acc, (0, 0, 0.30), (L + 0.8, W + 0.8, 0.60), band)
    for k in range(FLOORS + 1):
        zb = z_plinth + FH * k
        for y in (-hy, hy):
            sbox(acc, (0, y, zb), (L, 0.16, 0.34), band)
        for x in (-hx, hx):
            sbox(acc, (x, 0, zb), (0.16, W, 0.34), band)
    for k in range(FLOORS):
        zb = z_plinth + FH * k
        for inward, y in ((-1.0, -hy), (1.0, hy)):
            wall_seg(acc, 'x', -hx, hx, y, zb + 0.16, zb + 0.95, wallc, thick=0.30,
                     ribs=0, inward=inward)
            wall_seg(acc, 'x', -hx, hx, y, zb + 2.65, zb + FH, wallc, thick=0.30,
                     ribs=0, inward=inward)
            window_band(acc, 'x', -hx + 0.15, hx - 0.15, y, zb + 0.95, zb + 2.65, 5,
                        seed=17 + k, inward=inward)
            if k == 0:                                           # 首层外墙剥落/水渍
                for sx in (-4.4, 1.8):
                    sbox(acc, (sx, y + inward * 0.17, zb + 0.45), (1.6, 0.03, 0.62), band)
        for inward, x in ((-1.0, -hx), (1.0, hx)):
            if inward > 0:                                       # 东面（朝厂区）= 入口面
                wall_seg(acc, 'y', -hy, -1.6, x, zb + 0.16, zb + 0.95, wallc, thick=0.30, inward=inward)
                wall_seg(acc, 'y', 1.6, hy, x, zb + 0.16, zb + 0.95, wallc, thick=0.30, inward=inward)
                wall_seg(acc, 'y', -hy, hy, x, zb + 2.65, zb + FH, wallc, thick=0.30, inward=inward)
                if k == 0:
                    sbox(acc, (x + 0.10, 0.0, zb + 1.60), (0.10, 3.2, 3.20), dark)
                    sbox(acc, (x - 0.05, 0.6, zb + 1.55), (0.10, 1.5, 3.0), 'Kit_Rust',
                         rot=(0.0, 0.0, -0.85))
                    sbox(acc, (x + 0.25, 0.0, zb + 3.35), (0.55, 3.6, 0.18), band)
                    for s in (-1.5, 1.5):
                        cyl(acc, (x + 0.45, s, zb + 0.16), (x + 0.45, s, zb + 3.30), 0.09, 0.09, 6,
                            'Kit_Iron')
                else:
                    window_band(acc, 'y', -1.6, 1.6, x, zb + 0.95, zb + 2.65, 1,
                                seed=23 + k, inward=inward)
            else:
                wall_seg(acc, 'y', -hy, hy, x, zb + 0.16, zb + 0.95, wallc, thick=0.30, inward=inward)
                wall_seg(acc, 'y', -hy, hy, x, zb + 2.65, zb + FH, wallc, thick=0.30, inward=inward)
                window_band(acc, 'y', -hy + 0.15, hy - 0.15, x, zb + 0.95, zb + 2.65, 3,
                            seed=29 + k, inward=inward)
    # ---- 入口台阶 + 歪斜雨棚
    for i in range(3):
        sbox(acc, (hx + 0.6 + i * 0.45, 0.0, 0.60 - i * 0.18), (0.45, 4.4, 0.36 - i * 0.12), band)
    sbox(acc, (hx + 0.75, 0.0, 3.55), (1.5, 4.0, 0.24), band, rot=(0.0, -0.16, 0.0))
    cyl(acc, (hx + 1.25, -1.7, 0.10), (hx + 1.25, -1.7, 3.40), 0.10, 0.10, 6, 'Kit_Iron')
    cyl(acc, (hx + 1.25, 1.7, 0.10), (hx + 1.25, 1.7, 2.10), 0.10, 0.10, 6, 'Kit_Iron')
    # ---- 招牌（褪色漆板 + 字块）
    sbox(acc, (hx + 0.30, 0.0, 5.10), (0.22, 4.2, 1.10), 'Kit_PaintYellow')
    for k in range(5):
        sbox(acc, (hx + 0.44, -1.5 + k * 0.75, 5.10), (0.06, 0.45, 0.52), dark)
    # ---- 屋顶板 + 女儿墙 + 塌角
    sbox(acc, (0, 0, z_plinth + FH * FLOORS + 0.10), (L + 0.5, W + 0.5, 0.20), band)
    for y in (-hy - 0.2, hy + 0.2):
        sbox(acc, (0, y, z_plinth + FH * FLOORS + 0.65), (L + 0.5, 0.30, 0.90 if y > 0 else 0.90),
             wallc)
    sbox(acc, (hx + 0.2, 1.4, z_plinth + FH * FLOORS + 0.65), (0.30, W - 2.6, 0.90), wallc)
    sbox(acc, (-hx - 0.2, -2.6, z_plinth + FH * FLOORS + 0.65), (0.30, W - 5.0, 0.90), wallc)
    for k in range(4):                                            # 塌角露钢筋
        sbox(acc, (-hx - 0.3 + k * 0.35, hy - 0.9 + k * 0.30, z_plinth + FH * FLOORS + 0.55),
             (0.06, 0.06, 1.0), 'Kit_Rust', rot=(0.25, 0.20, 0.0))
    sbox(acc, (-hx + 1.0, hy - 1.0, z_plinth + FH * FLOORS + 0.30), (1.4, 1.0, 0.16), band,
         rot=(0.42, 0.0, 0.35))
    # ---- 屋顶水箱（4 腿）+ 爬梯 + 楼梯间 + 通风机组
    for lx, ly in ((-1.9, -1.2), (1.9, -1.2), (-1.9, 1.2), (1.9, 1.2)):
        cyl(acc, (2.6 + lx, ly, z_plinth + FH * FLOORS + 0.20), (2.6 + lx, ly,
            z_plinth + FH * FLOORS + 1.55), 0.10, 0.10, 6, 'Kit_Iron')
    zt = z_plinth + FH * FLOORS + 1.55
    loft(acc, [ring_pts(2.6, 0.0, 1.45, zt, 14), ring_pts(2.6, 0.0, 1.45, zt + 2.10, 14)],
         streak_matf('Kit_SteelPale', 131, 14, cover=0.3), cap_bot='Kit_SteelPale')
    fan(acc, (2.6, 0.0, zt + 2.62), ring_pts(2.6, 0.0, 1.45, zt + 2.10, 14), 'Kit_SteelPale')
    cyl(acc, (2.9, 0.0, zt + 2.55), (2.9, 0.0, zt + 3.30), 0.13, 0.13, 6, 'Kit_Rust')
    ladder(acc, 2.6 + 1.55, 0.0, z_plinth + FH * FLOORS + 0.25, zt + 2.30, 'Kit_Iron',
           out_dir=(1.0, 0.0))
    sbox(acc, (-3.4, -1.6, z_plinth + FH * FLOORS + 1.50), (3.0, 2.6, 2.60), wallc)
    sbox(acc, (-3.4, -2.95, z_plinth + FH * FLOORS + 1.35), (1.1, 0.10, 2.10), dark)
    sbox(acc, (-3.4, -1.6, z_plinth + FH * FLOORS + 2.95), (3.3, 2.9, 0.30), band)
    sbox(acc, (-3.2, -2.70, z_plinth + FH * FLOORS + 2.10), (1.0, 0.08, 1.9), 'Kit_Rust',
         rot=(0.0, 0.0, -1.15))
    for bx, by in ((0.2, 2.2), (-1.2, 2.6)):
        sbox(acc, (bx, by, z_plinth + FH * FLOORS + 0.72), (1.4, 1.1, 1.0), 'Kit_SteelPale')
        cyl(acc, (bx, by, z_plinth + FH * FLOORS + 1.25), (bx, by, z_plinth + FH * FLOORS + 1.35),
            0.34, 0.34, 8, 'Kit_Rust')
    # ---- 南侧外挂消防梯（顶层梯段塌落）
    fy = -hy - 0.85
    landings = [z_plinth + FH * k for k in (1, 2, 3, 4)]
    for i, lz in enumerate(landings):
        sbox(acc, (0.0, fy, lz + 0.06), (3.0, 1.5, 0.12), 'Kit_Iron')
        sbox(acc, (0.0, fy - 0.75, lz + 0.55), (3.0, 0.06, 0.10), 'Kit_Iron')
        railing(acc, (-1.45, fy - 0.72), (1.45, fy - 0.72), lz + 0.12, mat='Kit_Iron', posts=4)
        railing(acc, (1.45, fy - 0.72), (1.45, fy), lz + 0.12, mat='Kit_Iron', posts=2)
        railing(acc, (-1.45, fy - 0.72), (-1.45, fy), lz + 0.12, mat='Kit_Iron', posts=2)
    stair_flight(acc, 1.5, 0.20, -1.5, landings[0] + 0.06, fy, 1.0, 'Kit_Iron')
    stair_flight(acc, -1.5, landings[0] + 0.06, 1.5, landings[1] + 0.06, fy, 1.0, 'Kit_Iron')
    stair_flight(acc, 1.5, landings[1] + 0.06, -1.5, landings[2] + 0.06, fy, 1.0, 'Kit_Iron')
    # 顶层梯段（10.8 → 14.2）整体塌落：只剩两端断梁 / 弯折斜梁 / 悬垂栏杆
    sbox(acc, (-1.35, fy, landings[2] + 0.16), (0.6, 0.14, 0.32), 'Kit_Rust',
         rot=(0.0, -0.42, 0.0))
    sbox(acc, (0.2, fy + 0.10, landings[2] + 1.55), (3.4, 0.11, 0.16), 'Kit_Rust',
         rot=(0.0, -1.05, 0.0))
    sbox(acc, (1.1, fy - 0.30, landings[2] + 0.95), (1.2, 0.10, 0.12), 'Kit_Rust',
         rot=(0.0, 0.55, 0.20))
    for k in range(3):
        cyl(acc, (-1.0 + k * 0.9, fy, landings[3] + 0.06),
            (-1.0 + k * 0.9, fy, landings[3] - 1.3 - k * 0.35), 0.06, 0.06, 4, 'Kit_Rust')
    # ---- 墙面锈痕/裂缝/苔痕
    rng = random.Random(141)
    for k in range(10):
        wy = -hy - 0.16 if k % 2 == 0 else hy + 0.16
        wx = rng.uniform(-hx + 0.8, hx - 0.8)
        wz = rng.choice([2.0, 5.4, 8.8, 12.2]) + rng.uniform(-0.4, 0.4)
        sbox(acc, (wx, wy, wz), (0.10, 0.03, rng.uniform(0.8, 2.2)), 'Kit_Rust')
    for k in range(3):
        sbox(acc, (rng.uniform(-hx + 1, hx - 1), -hy - 0.17, rng.uniform(7.0, 14.0)),
             (0.06, 0.03, rng.uniform(2.0, 4.5)), dark, rot=(0.0, 0.0, rng.uniform(-0.1, 0.1)))
    for y in (-hy - 0.16, hy + 0.16):
        sbox(acc, (0.0, y, 0.66), (L - 0.4, 0.05, 0.24), 'Kit_GrassDark')
    if dress:
        weeds(acc, hx + 0.8, 3.6, 3, 151, spread=1.4)
        weeds(acc, -hx - 0.9, -3.0, 2, 152, spread=1.2)
        weeds(acc, 2.0, -hy - 1.5, 2, 153, spread=1.2)
        rubble(acc, -hx - 1.2, hy + 1.4, 6, 154, spread=1.4, size=0.8)
        rubble(acc, 4.2, -hy - 1.4, 4, 155, spread=1.0, size=0.6, rust='Kit_Rust')


# ============================================================================
# 分件 ⑨⑩⑪：场地小件（废料堆 / 围栏 / 泵组基座）
# ============================================================================

def build_debris(acc):
    """废料堆（场地散布件）：砼块 + 断管 + 塌板 + 弯曲钢筋 + 破木托。"""
    rng = random.Random(161)
    sbox(acc, (0, 0, 0.35), (2.6, 2.0, 0.70), 'Kit_ConcreteMid', rot=(0.30, 0.10, 0.4))
    sbox(acc, (-1.4, 1.1, 0.22), (1.6, 1.2, 0.44), 'Kit_ConcreteDark', rot=(-0.2, 0.3, 0.9))
    cyl(acc, (1.8, -0.9, 0.30), (3.2, -1.5, 0.30), 0.28, 0.28, 8, 'Kit_Rust')
    cyl(acc, (3.2, -1.5, 0.30), (3.2, -2.6, 0.30), 0.28, 0.26, 8, 'Kit_Rust')
    sbox(acc, (-0.6, -1.6, 0.55), (2.4, 1.6, 0.12), 'Kit_SteelPale', rot=(0.55, 0.20, 0.9))
    for k in range(5):
        sbox(acc, (0.4 + k * 0.22, 1.8, 0.30 + k * 0.05), (0.05, 0.05, 1.9), 'Kit_Rust',
             rot=(rng.uniform(-0.5, 0.5), rng.uniform(-0.4, 0.4), 0.0))
    sbox(acc, (2.4, 1.4, 0.18), (1.4, 0.9, 0.18), 'Kit_WoodDark', rot=(0.0, 0.0, 0.6))
    sbox(acc, (2.2, 1.9, 0.30), (1.3, 0.8, 0.16), 'Kit_WoodDark', rot=(0.0, 0.0, 0.2))
    rubble(acc, -1.8, -0.6, 5, 162, spread=1.2, size=0.7, rust='Kit_Rust')
    weeds(acc, -0.8, 0.4, 2, 163, spread=0.9)


def build_fence(acc, toppled=False):
    """围栏段（6 m）：立柱 + 上下横杆 + 密排竖条；toppled=True = 整段倒伏在地。"""
    L = 6.0
    n = 14
    if toppled:
        for k in range(3):
            sbox(acc, (-L * .5 + k * L * .5, 0.0, 0.14), (0.12, 0.12, 2.4), 'Kit_Rust',
                 rot=(0.0, math.pi * .5 - 0.08, 0.0))
        for k in range(n):
            sbox(acc, (-L * .5 + L * (k + 0.5) / n, 0.0, 0.10), (0.05, 2.0, 0.06), 'Kit_Rust',
                 rot=(0.0, 0.0, 0.04))
        sbox(acc, (0.0, 0.0, 0.06), (L, 0.09, 0.09), 'Kit_Rust')
        weeds(acc, 1.0, 0.5, 2, 171, spread=1.2)
        return
    for k in range(4):
        px = -L * .5 + L * k / 3.0
        sbox(acc, (px, 0.0, 0.20), (0.5, 0.5, 0.40), 'Kit_ConcreteMid')
        lean = -0.10 if k == 2 else 0.0
        sbox(acc, (px, 0.0, 1.30), (0.10, 0.10, 2.20), 'Kit_Rust', rot=(lean, 0.0, 0.0))
    # 【实机口径修正 2026-09-29】横杆/竖条由 Rust 压到 RustDark：实机里长杆上的亮锈橙太抢眼
    # （连成排的围栏把整片场地染成橙色），暗锈更贴"多年失修"。
    for z in (0.55, 2.20):
        sbox(acc, (0.0, 0.0, z), (L, 0.07, 0.07), 'Kit_RustDark')
    for k in range(n):
        px = -L * .5 + L * (k + 0.5) / n
        sag = 0.12 if 1.6 < px < 2.2 else 0.0
        sbox(acc, (px, 0.0, 1.38 - sag), (0.05, 0.05, 1.7), 'Kit_RustDark',
             rot=(0.0, 0.06 if sag else 0.0, 0.0))
    weeds(acc, -2.0, 0.4, 2, 172, spread=1.0)
    weeds(acc, 2.2, -0.4, 2, 173, spread=1.0)


def build_pump_skid(acc):
    """泵组基座：砼基础 + 两台泵体 + 电机 + 阀门 + 短管。"""
    sbox(acc, (0, 0, 0.20), (5.0, 3.2, 0.40), 'Kit_ConcreteMid')
    for dx in (-1.4, 1.4):
        sbox(acc, (dx, 0.2, 0.62), (1.7, 1.1, 0.44), 'Kit_SteelBlue')
        cyl(acc, (dx - 0.4, 0.2, 0.85), (dx + 0.9, 0.2, 0.85), 0.46, 0.46, 10, 'Kit_Rust')
        sbox(acc, (dx + 1.5, 0.2, 0.80), (1.0, 0.9, 0.80), 'Kit_SteelPale')
        cyl(acc, (dx + 0.9, -0.6, 0.85), (dx + 0.9, -1.8, 0.85), 0.20, 0.20, 8, 'Kit_Rust')
        arc_tube(acc, dx + 0.5, -1.2, 1.35, 0.26, 0.0, 2.0 * math.pi - 0.05, 10, 0.05, 'Kit_Brass')
    cyl(acc, (-2.0, -1.6, 0.85), (2.4, -1.6, 0.85), 0.24, 0.24, 8, 'Kit_Rust')
    cyl(acc, (2.4, -1.6, 0.85), (2.4, -2.6, 0.85), 0.24, 0.24, 8, 'Kit_RustDark')
    weeds(acc, -2.0, 1.0, 2, 181, spread=0.8)


# ============================================================================
# 总装：第三样板关「废弃化工厂」场地
# ============================================================================

YARD_X, YARD_Y = 68.0, 48.0


def build_level(acc):
    """场地总装：砼地坪 + 道路 + 裂缝/积水/荒草 + 全部构件摆位 + 场内废件散布。"""
    # ---- 地坪 + 道路 + 停车场
    sbox(acc, (0, 0, -0.15), (YARD_X, YARD_Y, 0.30), 'Kit_ConcreteMid')
    for cx, cy, sx, sy, seed in ((0, -19.5, 66, 7.0, 201), (30.0, -4.0, 6.0, 38.0, 202),
                                 (-12.5, 2.0, 9.0, 6.0, 203)):
        patch_poly(acc, cx, cy, sx * .5, sy * .5, 0.04, 'Kit_ConcreteDark', sides=11, seed=seed,
                   thick=0.05)
    # ---- 泥地 / 积水 / 裂缝
    for cx, cy, rx, ry, seed in ((-26.0, -18.0, 6.0, 4.0, 211), (18.0, 18.0, 7.0, 4.5, 212),
                                 (22.0, -16.0, 5.0, 3.5, 213), (-8.0, 16.5, 5.5, 3.0, 214),
                                 (2.0, -6.0, 4.0, 2.6, 215), (-31.0, 8.0, 4.0, 6.0, 216)):
        patch_poly(acc, cx, cy, rx, ry, 0.05, 'Kit_SandDark', sides=10, seed=seed, thick=0.04)
    for cx, cy, rx, ry, seed, m in ((10.0, -12.0, 3.4, 2.2, 221, 'Kit_WetSand'),
                                    (-14.0, -8.5, 2.6, 1.8, 222, 'Kit_WetSand'),
                                    (24.0, -18.0, 3.0, 2.0, 223, 'Kit_RustDark'),
                                    (5.0, 10.0, 2.2, 1.5, 224, 'Kit_WetSand')):
        patch_poly(acc, cx, cy, rx, ry, 0.07, m, sides=9, seed=seed, thick=0.05)
    for k, (x0, y0, x1, y1) in enumerate(((-30, -2, 2, -6), (4, 6, 26, 2), (-18, 12, -4, 6),
                                          (12, -14, 28, -8), (-6, -20, 8, -14), (16, 12, 30, 16))):
        crack(acc, x0, y0, x1, y1, 231 + k, z=0.05, segs=6)
    # ---- 构件摆位（yaw 均为 0：场地正交布局）
    place(acc, build_office, -19.0, 6.0)
    place(acc, build_hall, -1.0, 14.0)
    place(acc, build_pipe_rack, 13.0, 14.0)
    place(acc, build_column, 13.0, 7.5, tall=True)
    place(acc, build_column, 18.5, 1.0, tall=False)
    place(acc, build_sphere_tank, 24.0, 3.5)
    place(acc, build_tank_farm, 0.0, -9.0)
    place(acc, build_stack, 30.0, 15.0)
    place(acc, build_cooling_tower, -17.0, -13.0)
    place(acc, build_pump_skid, 8.0, 2.0)
    place(acc, build_pump_skid, 22.0, -8.5, yaw=90.0)
    # ---- 倒伏大管（横穿场地）
    cyl(acc, (-2.0, -3.0, 0.46), (9.0, -4.4, 0.46), 0.42, 0.42, 10, 'Kit_Rust')
    cyl(acc, (9.0, -4.4, 0.46), (9.0, -4.4, 1.60), 0.42, 0.40, 10, 'Kit_Rust')
    annulus(acc, 9.0, -4.4, 0.40, 0.56, 1.60, 10, 'Kit_RustDark', thick=0.10)
    sbox(acc, (2.0, -3.4, 0.28), (1.1, 1.0, 0.55), 'Kit_ConcreteDark')
    sbox(acc, (6.4, -3.9, 0.26), (1.0, 0.9, 0.50), 'Kit_ConcreteDark', rot=(0.0, 0.0, 0.3))
    # ---- 围栏（南侧成列 + 东/西侧残段 + 一段倒伏）
    for cx in (-24.0, -12.0, 0.0, 12.0, 24.0):
        place(acc, build_fence, cx, -22.4)
    for cy in (-14.0, -2.0, 10.0):
        place(acc, build_fence, 32.4, cy, yaw=90.0)
    for cy in (2.0, 14.0):
        place(acc, build_fence, -32.4, cy, yaw=90.0)
    place(acc, build_fence, 8.0, -20.6, toppled=True)             # 倒伏段
    place(acc, build_fence, 20.0, -20.0, yaw=0.0)
    # ---- 场内废件散布
    rng = random.Random(301)
    for cx, cy in ((-8.0, -2.0), (14.0, -1.0), (-24.0, 16.0), (26.0, -12.0), (3.0, 18.0),
                   (-30.0, -6.0), (20.0, 16.0), (-14.0, 19.0), (33.0, 3.0)):
        place(acc, build_debris, cx, cy, yaw=rng.uniform(0, 360))
    for cx, cy in ((-6.5, -3.5), (16.5, 9.5), (-21.0, -8.0), (28.0, -3.0), (6.5, 5.5),
                   (-27.0, 13.0), (12.0, -17.0), (23.0, 12.0)):
        n = rng.randint(3, 6)
        for k in range(n):
            drum(acc, cx + rng.uniform(-1.6, 1.6), cy + rng.uniform(-1.6, 1.6),
                 yaw=rng.uniform(0, 360), tilt=rng.choice((0.0, 0.0, 0.0, 1.45, 1.57)),
                 mat=rng.choice(('Kit_Rust', 'Kit_Rust', 'Kit_RustDark')))
    for cx, cy, seed in ((-7.0, 3.0, 401), (9.5, 12.5, 402), (-20.0, 18.0, 403), (26.0, 7.0, 404),
                         (-28.0, -14.0, 405), (4.0, -17.0, 406), (33.0, -18.0, 407),
                         (-11.0, -4.0, 408), (19.0, -13.0, 409), (-3.0, 22.0, 410)):
        rubble(acc, cx, cy, rng.randint(5, 11), seed, spread=rng.uniform(1.0, 2.2),
               size=rng.uniform(0.7, 1.3), rust='Kit_Rust')
    # ---- 荒草（贴墙根/围栏根/裂缝带）
    for cx, cy, n, seed, sp in ((-25.0, 10.8, 5, 501, 1.4), (-12.6, 6.0, 4, 502, 1.3),
                                (-1.0, 8.8, 6, 503, 2.2), (7.2, 14.0, 4, 504, 1.4),
                                (-10.0, -2.6, 5, 505, 1.6), (10.6, -9.0, 4, 506, 1.6),
                                (-17.0, -5.6, 6, 507, 2.4), (30.0, 10.0, 4, 508, 1.6),
                                (-24.0, -21.5, 5, 509, 1.5), (0.0, -21.6, 5, 510, 1.5),
                                (12.0, -21.5, 4, 511, 1.5), (24.0, -21.4, 4, 512, 1.5),
                                (32.0, -14.0, 3, 513, 1.4), (-32.0, 2.0, 3, 514, 1.4),
                                (-8.0, 16.0, 4, 515, 1.8), (20.0, 16.0, 4, 516, 1.8)):
        weeds(acc, cx, cy, n, seed, spread=sp)
    for k in range(16):                                          # 满场零散草
        weeds(acc, rng.uniform(-32, 33), rng.uniform(-21, 22), 1, 600 + k, spread=0.8)


def place(acc, fn, x, y, yaw=0.0, **kw):
    """把分件（局部系）摆到场地坐标 (x, y)，可绕 Z 旋转。"""
    sub = MeshAcc()
    fn(sub, **kw)
    acc.append(sub, (x, y, 0.0), yaw)


# ============================================================================
# 预览染染（样板同款：Cycles + 标准视图变换 + 中性灰底 + 三灯）
# ============================================================================

def setup_pixel_pass(scene, block=2, steps=6):
    """合成器：像素化（块边长 = block 屏幕像素）+ 色阶量化（steps 档）。

    对齐像素化着色路径的成图口径（docs/技术/染染/管线/染染管线.md §1.1）：
    几何染进屏幕档 → 着色跑在艺术画布（屏幕 ÷ pixelScale，现役 2）→ 点采样放大上屏。
    预览图以 block=2 出（与 1080p 现役 `pixelScale=2` 同口径），色阶量化近似其逐物体色带。
    Blender 5.2 的合成器挂在 `scene.compositing_node_group`（Scene.node_tree 已移除）。
    """
    try:
        scene.use_nodes = True
        ng = scene.compositing_node_group
        if ng is None:
            ng = bpy.data.node_groups.new('chem_pixel', 'CompositorNodeTree')
            scene.compositing_node_group = ng
        ng.nodes.clear()
        for it in list(ng.interface.items_tree):
            ng.interface.remove(it)
        ng.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')
        rl = ng.nodes.new('CompositorNodeRLayers')
        px = ng.nodes.new('CompositorNodePixelate')
        px.inputs['Size'].default_value = int(block)
        out = ng.nodes.new('NodeGroupOutput')
        ng.links.new(rl.outputs['Image'], px.inputs['Color'])
        last = px
        if steps and steps >= 2:
            po = ng.nodes.new('CompositorNodePosterize')
            po.inputs['Steps'].default_value = float(steps)
            ng.links.new(px.outputs['Color'], po.inputs['Image'])
            last = po
        ng.links.new(last.outputs['Color'], out.inputs[0])
        log('pixel pass: block=%d steps=%d' % (block, steps))
        return True
    except Exception as exc:                                     # 合成器不可用则退化为净染染
        log('pixel pass 不可用（%s），本轮到净染染' % exc)
        return False


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


def setup_render(scene, res_w, res_h, exposure=0.0):
    scene.render.engine = 'CYCLES'
    gpu = enable_gpu()
    scene.cycles.device = 'GPU' if gpu else 'CPU'
    samples = SAMPLES if gpu else max(12, SAMPLES // 3)
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.render.resolution_x = res_w
    scene.render.resolution_y = res_h
    scene.render.resolution_percentage = 100
    scene.render.use_file_extension = False
    ims = scene.render.image_settings
    ims.file_format = 'JPEG'
    ims.quality = JPG_QUALITY
    ims.color_mode = 'RGB'
    scene.view_settings.view_transform = ST.PREVIEW_VIEW_TRANSFORM
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = exposure
    log('render engine=Cycles device=%s samples=%d res=%dx%d' % (gpu or 'CPU', samples, res_w, res_h))
    return bool(gpu)


def setup_stage(scene, dim, h, floor_size=80.0, key=2600.0, fill=900.0, rim=1800.0,
                sky=700.0, world_strength=1.35, key_h=12.0, ground_hex=None):
    """中性灰世界（带环境光强）+ 灰地板 + 四灯（暖 key / 冷 fill / 暖 rim / 顶部天光）。

    哑光材质 + 纯 diffuse 后，背光面只靠环境光——环境光弱就塌成死黑（像素路径里那道
    GI 趟正是干这个的），故补一盏顶光并把 world 强度提到 >1。
    """
    for oname in ('sk_key', 'sk_fill', 'sk_rim', 'sk_sky', 'sk_floor'):
        o = bpy.data.objects.get(oname)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
    world = scene.world
    if world is None:
        world = bpy.data.worlds.new('chem_world')
        scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs[0].default_value = (*ST.hex_to_rgb(ST.PREVIEW_BG_HEX), 1.0)
    bg.inputs[1].default_value = world_strength

    u = max(dim, 1.2) / 15.85
    fmat = bpy.data.materials.get('chem_floor_mat')
    if fmat is None:
        fmat = bpy.data.materials.new('chem_floor_mat')
        fmat.use_nodes = True
    fb = fmat.node_tree.nodes['Principled BSDF']
    # 每次都写：场地档与分件档地板色不同，材质是共享的（只在新建时写会留上一档的色）
    fb.inputs['Base Color'].default_value = (*ST.hex_to_rgb(ground_hex or ST.PREVIEW_GROUND_HEX), 1.0)
    fb.inputs['Roughness'].default_value = 0.9
    floor = bpy.data.objects.new('sk_floor', bpy.data.meshes.new('sk_floor'))
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=floor_size)
    bm.to_mesh(floor.data)
    bm.free()
    floor.location = (0, 0, -0.03)
    floor.data.materials.append(fmat)
    scene.collection.objects.link(floor)

    def area(name, loc, energy, color, target, size):
        d = bpy.data.lights.new(name, 'AREA')
        d.size = size
        d.energy = energy * u * u
        d.color = color
        o = bpy.data.objects.new(name, d)
        scene.collection.objects.link(o)
        o.location = loc
        di = Vector(target) - Vector(loc)
        o.rotation_euler = di.to_track_quat('-Z', 'Y').to_euler()

    tz = (0, 0, max(0.5, h * 0.4))
    area('sk_key', (-12 * u, -14 * u, key_h * u), key, (1.0, 0.94, 0.84), tz, 10 * u)
    area('sk_fill', (14 * u, -4 * u, 7 * u), fill, (0.62, 0.75, 1.0), tz, 10 * u)
    area('sk_rim', (3 * u, 16 * u, 10 * u), rim, (1.0, 0.9, 0.75), (0, 0, max(0.8, h * 0.6)), 10 * u)
    area('sk_sky', (6 * u, -4 * u, 22 * u), sky, (0.92, 0.95, 1.0), (0, 0, max(1.0, h * 0.5)), 18 * u)


def frame_view(target, dist, az_deg, el_deg):
    """按方位/仰角/距离摆机位；返回 (loc, target)。"""
    az, el = math.radians(az_deg), math.radians(el_deg)
    return ((target[0] + dist * math.cos(el) * math.cos(az),
             target[1] + dist * math.cos(el) * math.sin(az),
             target[2] + dist * math.sin(el)), target)


def min_half_tan():
    """画幅较短边的半视场 tan（取景距离用；sensor_fit=AUTO 取长边 36mm）。"""
    half = math.atan(18.0 / LENS)                                # 长边半角
    if KIT_W >= KIT_H:
        half_v = math.atan((18.0 * KIT_H / KIT_W) / LENS)
    else:
        half_v = half
    return math.tan(min(half, half_v))


def make_views(size):
    """分件两视角：front34（右前上 3/4）+ side（正侧）。

    距离按**投影外框**推（宽/高各算一次取大），不是包围球——包围球对扁而宽的大件
    （冷却塔 25.9×20.0）会把机位推到 100 m 外，件在画面里只剩四成（实测）。
    """
    tan_v = min_half_tan()
    tan_h = math.tan(math.atan(18.0 / LENS)) if KIT_W >= KIT_H else tan_v
    d = max(2.5, max(size[2] * 0.5 / tan_v, max(size[0], size[1]) * 0.5 / tan_h) * FIT_PAD)
    ct = (0.0, 0.0, size[2] * 0.48)
    return [('front34',) + frame_view(ct, d, -38.0, 22.0),
            ('side',) + frame_view(ct, d, 92.0, 12.0)]


def make_level_views():
    """场地成品图七机位：广角 3/4 / 正面 3/4 / 工艺区 / 办公楼 / 冷却塔低角 / 正立面 / 实机同框。"""
    return [
        ('wide34',) + frame_view((0, 0, 6), 105.0, -40.0, 32.0),
        ('front34',) + frame_view((-2, 0, 6), 62.0, -35.0, 18.0),
        ('process',) + frame_view((14, 5, 7), 46.0, -30.0, 17.0),
        ('office',) + frame_view((-19, 5, 7), 34.0, -105.0, 16.0),
        ('tower',) + frame_view((-13, -11, 6), 58.0, -125.0, 13.0),
        ('side',) + frame_view((0, -2, 8), 90.0, -88.0, 10.0),
        # 实机同框：等距 35.264° + 45° 方位、画面横跨 GAME_WIDE_M —— 玩家实际看到的构图
        ('ingame',) + frame_view((2, 4, 2), (GAME_WIDE_M * 0.5) / min_half_tan(), -45.0, 35.264),
    ]


def render_views(scene, prefix, views):
    for name, loc, target in views:
        cam_data = bpy.data.cameras.new('chem_cam')
        cam_data.lens = LENS
        cam_data.clip_end = 600
        cam = bpy.data.objects.new('chem_cam', cam_data)
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
# 资产表 + 主流程
# ============================================================================

ASSETS = [
    ('ChemPlant_Hall', build_hall, PIECE_SLOT_LIMIT),
    ('ChemPlant_ColumnTall', lambda a: build_column(a, tall=True), PIECE_SLOT_LIMIT),
    ('ChemPlant_ColumnShort', lambda a: build_column(a, tall=False), PIECE_SLOT_LIMIT),
    ('ChemPlant_PipeRack', build_pipe_rack, PIECE_SLOT_LIMIT),
    ('ChemPlant_SphereTank', build_sphere_tank, PIECE_SLOT_LIMIT),
    ('ChemPlant_TankFarm', build_tank_farm, PIECE_SLOT_LIMIT),
    ('ChemPlant_Stack', build_stack, PIECE_SLOT_LIMIT),
    ('ChemPlant_CoolingTower', build_cooling_tower, PIECE_SLOT_LIMIT),
    ('ChemPlant_Office', build_office, PIECE_SLOT_LIMIT),
    ('ChemPlant_Debris', build_debris, PIECE_SLOT_LIMIT),
    ('ChemPlant_Fence', build_fence, PIECE_SLOT_LIMIT),
    ('ChemPlant_PumpSkid', build_pump_skid, PIECE_SLOT_LIMIT),
    ('ChemPlant_Level', build_level, LEVEL_SLOT_LIMIT),
]

ALL_SLOTS = sorted(set(
    ['Kit_ConcreteLight', 'Kit_ConcreteMid', 'Kit_ConcreteDark', 'Kit_Rust', 'Kit_RustDark',
     'Kit_SteelBlue', 'Kit_SteelPale', 'Kit_GlassDark', 'Kit_PaintYellow', 'Kit_Iron',
     'Kit_Brass', 'Kit_GrassDark', 'Kit_GrassMid', 'Kit_SandDark', 'Kit_WetSand', 'Kit_WoodDark']
))


def main():
    parse_args()
    for d in (FBX_DIR, PREVIEW_DIR, WORK_DIR):
        os.makedirs(d, exist_ok=True)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    mats = make_materials()
    gpu = False
    if RENDER:
        gpu = setup_render(scene, KIT_W, KIT_H, EXPOSURE_KIT)
        setup_pixel_pass(scene, PIXEL_BLOCK, PIXEL_STEPS)

    wanted = None
    if ONLY != 'all':
        # 名字可简写：'level' / 'hall' / 'ChemPlant_Stack' 都收（统一归一到 ChemPlant_<名>）
        raw = {s.strip().lower() for s in ONLY.split(',') if s.strip()}
        wanted = {r if r.startswith('chemplant_') else 'chemplant_' + r for r in raw}
        known = {name.lower() for name, _, _ in ASSETS}
        for w in sorted(wanted):
            if w not in known:
                raise SystemExit('chemplant_kit: 未知资产名 %r（合法：%s）'
                                 % (w, ', '.join(n for n, _, _ in ASSETS)))

    for name, fn, slot_limit in ASSETS:
        if wanted is not None and name.lower() not in wanted:
            continue
        log('== %s ==' % name)
        acc = MeshAcc()
        fn(acc)
        nv, nf, ntri = acc.counts()
        log('%s raw: verts=%d polys=%d tris=%d' % (name, nv, nf, ntri))
        obj = join_to_object(acc, name, mats)
        stats = ST.print_stats(name, [obj], slot_limit=slot_limit)
        ST.export_fbx([obj], os.path.join(FBX_DIR, name + '.fbx'))
        log('exported %s' % os.path.join(FBX_DIR, name + '.fbx'))
        if RENDER:
            sx, sy, sz = stats['size']
            if name == 'ChemPlant_Level':
                setup_stage(scene, max(sx, sy), sz, floor_size=400.0, key=3600.0, fill=1100.0,
                            rim=1900.0, sky=520.0, world_strength=1.0, key_h=8.0,
                            ground_hex='#6A6A6A')
                scene.view_settings.exposure = EXPOSURE_LEVEL
                scene.render.resolution_x = LEVEL_W
                scene.render.resolution_y = LEVEL_H
                render_views(scene, name, make_level_views())
                scene.view_settings.exposure = EXPOSURE_KIT
                scene.render.resolution_x = KIT_W
                scene.render.resolution_y = KIT_H
            else:
                setup_stage(scene, max(sx, sy, sz), sz)
                render_views(scene, name, make_views((sx, sy, sz)))
        bpy.data.objects.remove(obj, do_unlink=True)

    if RENDER:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, 'chemplant_debug.blend'))
        log('gpu=%s' % (gpu or 'CPU'))
    log('done in %.1fs total' % (time.time() - T0))


if __name__ == '__main__':
    main()
