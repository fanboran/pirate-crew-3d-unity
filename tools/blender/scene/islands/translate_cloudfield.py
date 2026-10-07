# -*- coding: utf-8 -*-
"""translate_cloudfield —— 云场（第 1 关主景）C# 程序化几何的 Blender 忠实翻译。

【这是什么】`CloudFieldGeometry`（Assets/Scripts/PirateCrew/SceneArtBake/Lowpoly/
CloudFieldGeometry.cs）已过审的 11 朵环形 blob 云田（主角云 + 第一环 4 + 第二环 6，
暖白 / 淡金按高度分带），本脚本把它**逐行移植**到 Blender：伪随机哈希逐位复刻
（LowpolyHash 是纯 uint32 位运算）、布局 / blob 结构 / 削平 / 抖动 / 分带全部照抄
C# 实现，几何与旧视觉一致（顶点误差仅来自 float32→double 的三角函数精度，< 1e-4 m）。

【碰撞不在本件】可站 / 落水判定走逻辑层地形数据（BattleTerrainView 运行时按
terrain 逐格生成 BoxCollider），视觉件零碰撞职责；第二环 6 朵的可站面随本批次
补进 golden 的 terrain 段（tools/level-design/patch_cloud_ring2.py）。

【坐标口径】C# 几何在 Unity 世界系（云场中心 = 原点）；映射 Unity(x,y,z) →
Blender(x,−z,y)（与 export_fbx + bakeAxisConversion 链一致，保定向，面绕序原样保留）。
Unity 侧实例摆 (20, 0, 15)（bakedPieces 现值，不动）。

【产物】（重跑幂等覆盖）
    Assets/Art/Models/SceneKit/CloudWalk.fbx    2 节点（WarmWhite / PaleGold 两槽）
    docs/images/level-islands/cloud_walk-{top,front34,side}
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_SCENE_DIR = os.path.dirname(_HERE)
_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(_SCENE_DIR)))
sys.path.insert(0, _SCENE_DIR)
sys.path.insert(0, os.path.join(_SCENE_DIR, "archipelago"))

import bpy
import archipelago_kit as AK
import style_tokens as ST

ASSET = "CloudWalk"
FBX_DIR = os.path.join(_ROOT, "pirate-crew", "Assets", "Art", "Models", "SceneKit")
IMG_DIR = os.path.join(_ROOT, "docs", "images", "level-islands")

# 材质 = 旧运行时槽的同名同色翻译（槽名出处：LowpolyStageBuilder.cs 的 SlotColor）。
# 【色值已退役】槽名保留（split_by_height 的两条分带路径不变），但基色以
# Assets/Art/Materials/Lowpoly/ 下的 .mat 为准——2026-10-07 创始人裁决"云彩应该是白色"：
# WarmWhite = 纯白 #FFFFFF、PaleGold = #F2F2F2 微灰白（分带靠明度，不发黄），色带档 4。
SLOTS = [
    ("Lowpoly_CloudWarmWhite", "#FFFFFF"),
    ("Lowpoly_CloudPaleGold", "#F2F2F2"),
]
M_WARM, M_GOLD = 0, 1

# ---------------------------------------------------------------------------
# LowpolyHash 逐位复刻（C# LowpolyBuffers.cs:119-143，uint32 mix，无 float 环境依赖）
# ---------------------------------------------------------------------------

def hash01(seed, index, salt):
    h = (seed & 0xFFFFFFFF) * 747796405 \
        + (index & 0xFFFFFFFF) * 2891336453 \
        + (salt & 0xFFFFFFFF) * 1911520717 \
        + 1442695041
    h &= 0xFFFFFFFF
    h ^= h >> 15
    h = (h * 2246822519) & 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 3266489917) & 0xFFFFFFFF
    h ^= h >> 16
    return h * (1.0 / 4294967296.0)


def signed_hash(seed, index, salt):
    return hash01(seed, index, salt) * 2.0 - 1.0


# ---------------------------------------------------------------------------
# MeshBuffers 忠实移植（SceneArt/MeshBuffers.cs：每三角独立顶点 + 面法线；
# AddQuad 按外法线提示自动翻绕序）。坐标仍为 Unity 世界系，转 Blender 在 join 前做。
# ---------------------------------------------------------------------------

class MeshBuffersPy:
    def __init__(self):
        self.verts = []
        self.normals = []
        self.tris = []

    def add_triangle(self, a, b, c):
        n = cross(sub(b, a), sub(c, a))
        if dot(n, n) < 1e-16:
            return
        n = normalize(n)
        base = len(self.verts)
        self.verts.extend([a, b, c])
        self.normals.extend([n, n, n])
        self.tris.extend([base, base + 1, base + 2])

    def add_quad(self, a, b, c, d, outward_hint):
        n = normalize(cross(sub(b, a), sub(d, a)))
        if dot(n, outward_hint) < 0:
            self.add_triangle(a, d, c)
            self.add_triangle(a, c, b)
        else:
            self.add_triangle(a, b, c)
            self.add_triangle(a, c, d)


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def normalize(v):
    l = math.sqrt(dot(v, v)) or 1.0
    return (v[0] / l, v[1] / l, v[2] / l)


def clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


# ---------------------------------------------------------------------------
# CloudFieldGeometry.Layout / ComposeCloud / AddCloudBlob / SplitByHeight 照抄
# （CloudFieldGeometry.cs:94-295；坐标系 Unity，云场中心 = 原点）
# ---------------------------------------------------------------------------

def make_platform(index, center, ang, radius, top_y, min_half, max_half, is_hero):
    half = min_half + (max_half - min_half) * hash01(0, index, 71)
    height = 1.9 + 1.1 * hash01(0, index, 73)
    return {
        "cx": center[0] + math.cos(ang) * radius,
        "cz": center[2] + math.sin(ang) * radius,
        "top": top_y,
        "base": top_y - height,
        "hw": half,
        "hd": half * (0.82 + 0.30 * hash01(0, index, 79)),
        "hero": is_hero,
    }


def make_platform_ellipse(index, center, ang, radius_x, radius_z, top_y, min_half, max_half, is_hero):
    """椭圆环变体：x/z 半径独立（场地 40×30 非正方，z 向没余量时东西向散开用）。"""
    p = make_platform(index, center, ang, radius_x, top_y, min_half, max_half, is_hero)
    ang = ang
    p["cz"] = center[2] + math.sin(ang) * radius_z
    return p


def layout(seed_ignored=None):
    """布局：确定性云台面表（主角云 + 环1 ×4 + 环2 ×6，共 11 朵）。

    【散开布局（创始人 2026-10-07 裁决"云朵均匀分布、散开、尽量不相互遮挡"）】
    环1 从正方位（0/90/180/270°）转到斜方位（45/135/225/315°）且半径 6.5→10.5——
    旧布局环1 云嵌进主角云 3.5m、与环2 间隙不足；环2 改椭圆（x 半径 16 / z 半径 11.5，
    场地 40×30 的 z 向没余量）向东西散开。高度随方位重排：45° 机位视线朝 +x+z，
    **视线走廊侧（东北/东南）一律低位 2.5**，制高点 7.5 放西北 135°（视线侧后方，永不盖人）、
    西南 225° 放 6.5 中继（在视线近侧但不切主角云，投影遮挡判定全项预验 7 单位 0% 遮挡）。
    """
    center = (0.0, 0.0, 0.0)
    plats = [dict(cx=0.0, cz=0.0, top=4.5, base=4.5 - 2.6, hw=6.0, hd=6.0, hero=True)]

    ring1_top = [2.5, 7.5, 6.5, 2.5]   # 45(东北) / 135(西北) / 225(西南) / 315(东南)
    for i in range(4):
        ang = math.radians(45.0 + i * 90.0)
        plats.append(make_platform(len(plats), center, ang, 10.5, ring1_top[i], 3.2, 4.0, False))

    ring2_order = [0, 3, 1, 4, 2, 5]
    # 【前排防遮挡】slot1(90°) 5、slot0(30°) 2.5——走廊侧低位；slot4(270°) 8 = 16 块最高档
    # 放正南深处（视线后方，永不盖人）。高度档集 8 档。
    ring2_top = [2.5, 5, 3, 7, 8, 6]
    for k in range(6):
        slot = ring2_order[k]
        ang = math.radians(30.0 + slot * 60.0)
        plats.append(make_platform_ellipse(len(plats), center, ang, 16.0, 11.5,
                                           ring2_top[slot], 3.4, 4.3, False))
    return plats


def add_cloud_blob(target, center, radius, squash, lat_rings, lon_segments,
                   seed, salt, flat_bottom_y, flat_top_y):
    """低模云团 blob：UV 球 + Y 压扁 + 顶 / 底削平 + 逐顶点径向 ±8% 抖动（照抄 C#）。"""
    lat_rings = max(3, lat_rings)
    lon_segments = max(5, lon_segments)

    pts = []
    for r in range(lat_rings):
        theta = math.pi * (r + 0.5) / lat_rings
        y = clamp(center[1] + math.cos(theta) * radius * squash, flat_bottom_y, flat_top_y)
        rr = math.sin(theta)
        ring = []
        for s in range(lon_segments):
            a = math.pi * 2.0 * s / lon_segments
            # 【抖动 ±4%】原 C# 件 ±8%（翻译期逐位复刻）；2026-10-07 创始人裁决"云朵不均
            # 更均匀一点"——金白分带边界与轮廓的犬牙来自径向抖动，减半后斑块与剪影都齐整。
            jitter = 1.0 + 0.04 * signed_hash(seed, r * 37 + s, salt)
            ring.append((center[0] + math.cos(a) * radius * rr * jitter, y,
                         center[2] + math.sin(a) * radius * rr * jitter))
        pts.append(ring)

    pole_top = (center[0], clamp(center[1] + radius * squash, flat_bottom_y, flat_top_y), center[2])
    pole_bottom = (center[0], clamp(center[1] - radius * squash, flat_bottom_y, flat_top_y), center[2])

    last = lat_rings - 1
    for s in range(lon_segments):
        t = (s + 1) % lon_segments
        target.add_triangle(pole_top, pts[0][t], pts[0][s])
        target.add_triangle(pole_bottom, pts[last][s], pts[last][t])

    for r in range(last):
        for s in range(lon_segments):
            t = (s + 1) % lon_segments
            lower_s, upper_s = pts[r + 1][s], pts[r][s]
            upper_t, lower_t = pts[r][t], pts[r + 1][t]
            mid = ((lower_s[0] + upper_s[0] + upper_t[0] + lower_t[0]) * 0.25,
                   (lower_s[1] + upper_s[1] + upper_t[1] + lower_t[1]) * 0.25,
                   (lower_s[2] + upper_s[2] + upper_t[2] + lower_t[2]) * 0.25)
            hint = sub(mid, center)
            target.add_quad(lower_s, upper_s, upper_t, lower_t,
                            normalize(hint) if dot(hint, hint) > 1e-10 else (0.0, 1.0, 0.0))


def compose_cloud(warm_buf, gold_buf, p, seed):
    """一朵云（照抄 ComposeCloud：顶盘 + 环绕中球 + 底盘，按高度阈值分带两槽）。"""
    top_y, base_y = p["top"], p["base"]
    half = max(p["hw"], p["hd"])
    r0 = half / 0.9

    gold_threshold = base_y + (top_y - base_y) * 0.55
    blob_count = 5 if p["hero"] else (4 if half > 3.6 else 3)
    temp = MeshBuffersPy()

    add_cloud_blob(temp, (p["cx"], top_y - r0 * 0.32, p["cz"]),
                   r0, 0.55, 4, 9 if p["hero"] else 7, seed, 1, base_y, top_y)

    for k in range(blob_count - 2):
        h1 = hash01(seed, k, 101)
        h2 = hash01(seed, k, 103)
        ang = k * 2.399963 + h1 * 1.2
        dist = r0 * 0.42
        radius = r0 * (0.44 + 0.18 * h2)
        c = (p["cx"] + math.cos(ang) * dist,
             base_y + 0.5 + (top_y - 0.6 - (base_y + 0.5)) * h2,
             p["cz"] + math.sin(ang) * dist)
        add_cloud_blob(temp, c, radius, 0.6, 3, 7, seed, 11 + k, base_y, top_y)

    add_cloud_blob(temp, (p["cx"], base_y + 0.7, p["cz"]),
                   r0 * 0.82, 0.5, 3, 7, seed, 19, base_y, top_y)

    split_by_height(temp, warm_buf, gold_buf, gold_threshold)


def split_by_height(source, below, above, threshold_y):
    """按面心高度把三角面拆进两槽（保留绕序，法线不变；照抄 C#）。"""
    v = source.verts
    for i in range(0, len(source.tris), 3):
        a, b, c = v[source.tris[i]], v[source.tris[i + 1]], v[source.tris[i + 2]]
        y = (a[1] + b[1] + c[1]) / 3.0
        (below if y < threshold_y else above).add_triangle(a, b, c)


# ---------------------------------------------------------------------------
# 装配 / 导出
# ---------------------------------------------------------------------------

def build():
    plats = layout()
    warm = MeshBuffersPy()
    gold = MeshBuffersPy()
    for i, p in enumerate(plats):
        compose_cloud(warm, gold, p, 26091401 + i * 131)

    print("STAT blobs=%d warm_tris=%d gold_tris=%d" % (
        len(plats), len(warm.tris) // 3, len(gold.tris) // 3))
    return warm, gold, plats


def make_materials():
    mats = []
    for name, hexcode in SLOTS:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        b = mat.node_tree.nodes["Principled BSDF"]
        r, g, bl = ST.hex_to_rgb(hexcode)
        b.inputs["Base Color"].default_value = (_srgb(r), _srgb(g), _srgb(bl), 1.0)
        b.inputs["Roughness"].default_value = 1.0
        b.inputs["Metallic"].default_value = 0.0
        mats.append((name, mat))
    return mats


def _srgb(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def join_cloud_object(buf, obj_name, mat):
    """MeshBuffersPy（Unity 系）→ Blender 对象：Unity(x,y,z) → Blender(x,−z,y)。

    映射保定向（行列式 +1），三角形绕序与法线原样保留。
    """
    me = bpy.data.meshes.new(obj_name)
    verts = [(p[0], -p[2], p[1]) for p in buf.verts]
    me.from_pydata(verts, [], [tuple(buf.tris[i:i + 3]) for i in range(0, len(buf.tris), 3)])
    me.materials.append(mat)
    obj = bpy.data.objects.new(obj_name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def render_views(objs, scene, samples, res):
    import mathutils

    scene.render.engine = "CYCLES"
    gpu = AK.enable_gpu()
    scene.cycles.device = "GPU" if gpu else "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = res
    scene.render.resolution_y = res * 3 // 4
    scene.render.use_file_extension = False
    ims = scene.render.image_settings
    ims.file_format = "JPEG"
    ims.quality = 90
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    AK.setup_preview_world_compat(scene)

    lo, hi = mathutils.Vector((1e9,) * 3), mathutils.Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(c)
            lo = mathutils.Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = mathutils.Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    center = (lo + hi) / 2.0
    diag = (hi - lo).length
    span = max(hi.x - lo.x, hi.y - lo.y)
    AK.aim_preview_lights(scene, target=center)

    views = [
        ("top", mathutils.Vector((0.0, 0.0, 1.0)), 1.2),
        ("front34", mathutils.Vector((0.55, -0.75, 0.45)), 1.4),
        ("side", mathutils.Vector((1.0, -0.15, 0.25)), 1.4),
    ]
    for label, direction, fit in views:
        cam_data = bpy.data.cameras.new("%s_cam_%s" % (ASSET, label))
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = span * fit
        cam_data.clip_end = diag * 8
        cam = bpy.data.objects.new("%s_cam_%s" % (ASSET, label), cam_data)
        scene.collection.objects.link(cam)
        cam.location = center + direction.normalized() * diag * 1.6
        cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.camera = cam
        scene.render.filepath = os.path.join(IMG_DIR, "%s-%s" % ("cloud_field", label))
        bpy.ops.render.render(write_still=True)
        print("VIEW %s" % scene.render.filepath)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    no_render = "--no-render" in argv
    samples = int(argv[argv.index("--samples") + 1]) if "--samples" in argv else 48
    res = int(argv[argv.index("--res") + 1]) if "--res" in argv else 1024

    os.makedirs(IMG_DIR, exist_ok=True)
    scene = bpy.context.scene
    for o in list(scene.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    warm, gold, plats = build()
    mats = make_materials()

    lo = [1e9] * 3
    hi = [-1e9] * 3
    for buf in (warm, gold):
        for p in buf.verts:
            for d in range(3):
                lo[d] = min(lo[d], p[d])
                hi[d] = max(hi[d], p[d])
    print("BBOX unity X[%.2f,%.2f] Y[%.2f,%.2f] Z[%.2f,%.2f]" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))

    objs = [
        join_cloud_object(warm, "%s_WarmWhite" % ASSET, mats[M_WARM][1]),
        join_cloud_object(gold, "%s_PaleGold" % ASSET, mats[M_GOLD][1]),
    ]
    ST.print_stats(ASSET, objs, slot_limit=99)
    ST.export_fbx(objs, os.path.join(FBX_DIR, ASSET + ".fbx"))
    print("FBX %s" % os.path.join(FBX_DIR, ASSET + ".fbx"))

    if not no_render:
        render_views(objs, scene, samples, res)


if __name__ == "__main__":
    main()
