# -*- coding: utf-8 -*-
"""
style_tokens —— 世界套件（WorldKit）统一风格参数，单一事实源。

所有 kit 脚本（tools/blender/scene/<kit>/*.py）必须：
    import sys, os
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # -> tools/blender/scene/
    import style_tokens as ST
然后只用 ST 里的常量/工具，**禁止在脚本内散写色值、粗糙度、导出参数**。

契约文档：docs/M4-大海域世界化.md §4（尺度契约见 §1，站面平直机制见 §4.2）。
调色板出处：docs/美术风格指南.md §2.1 / SceneArtPalette.cs（双侧同源纪律）。
预览渲染与 FBX 参数出处：tools/blender/scene/build_scene_kit.py（样板）。
"""

import json
import math
import os

# ---------------------------------------------------------------------------
# 0. 尺度与网格纪律（docs/M4 §1）
# ---------------------------------------------------------------------------

#: 1 Blender 单位 = 1 米 = 1 Unity unit。
#: Blender Z-up；导出经 axis 转换后 Unity Y-up（顶面高度 = Blender Z 高度）。
UNIT = 1.0

#: 站面高度档：所有可站立顶面的 Z 必须是 0.5 的整数倍。
TOP_STEP = 0.5

#: 顶面平面度容差（偏离 0.5 档 / 面内高低差超过即报错退出）。
PLANARITY_TOL = 1e-3

#: 站面最小尺寸（角色身高 1.85、占地约 0.5，站面小于此值无法立足）。
MIN_STANDABLE_SIZE = 4.0

#: 站面最低档（水面 y=-0.4，湿沙带 -0.5~-0.2；+0.25 起保证干站面）→ 首档 +0.5 起步。
MIN_TOP_Z = 0.5

#: 每件资产材质槽上限（Unity 换装纪律，Kit_ 前缀 = 换装键）。
SLOT_LIMIT_PER_ASSET = 8

#: 面数预算（三角形）。总预算：全场景 ≤150k、材质 ≤30（美术风格指南 §8）。
POLY_BUDGETS = {
    "terrain": 15000,   # 大号地形件
    "marine": 12000,    # 船与码头大件
    "prop": 3000,       # 中型道具
    "prop_small": 600,  # 小道具
    "horizon": 3000,    # 远景装饰（无站面无碰撞）
}

# ---------------------------------------------------------------------------
# 1. 材质槽表（Kit_ 前缀；Unity 侧 SceneKitPilotSetup 同步同源）
#    roughness = 1 - Unity smoothness（美术风格指南 §3.1）。
#    标【提】的槽为 M4 新增提案槽，Unity 侧表由协调者同步扩展。
# ---------------------------------------------------------------------------

SLOTS = {
    # 木（三档）
    "Kit_WoodLight":  {"hex": "#D4A76A", "roughness": 0.72},
    "Kit_WoodMid":    {"hex": "#A67B42", "roughness": 0.72},
    "Kit_WoodDark":   {"hex": "#6B4C28", "roughness": 0.74},
    # 沙（三档）+ 湿沙
    "Kit_SandLight":  {"hex": "#E8D5A3", "roughness": 0.82},
    "Kit_SandMid":    {"hex": "#C4A76A", "roughness": 0.82},
    "Kit_SandDark":   {"hex": "#8B7355", "roughness": 0.82},
    "Kit_WetSand":    {"hex": "#5C4A34", "roughness": 0.60},
    # 草/植被（三档；树叶复用草地档——美术风格指南 §2.1）
    "Kit_GrassLight": {"hex": "#7BC67E", "roughness": 0.86},
    "Kit_GrassMid":   {"hex": "#4A8C4A", "roughness": 0.86},
    "Kit_GrassDark":  {"hex": "#2D5A2D", "roughness": 0.86},
    # 岩（三档）
    "Kit_RockLight":  {"hex": "#B8A99A", "roughness": 0.78},
    "Kit_RockMid":    {"hex": "#8C7B6A", "roughness": 0.78},
    "Kit_RockDark":   {"hex": "#5C4F42", "roughness": 0.78},
    # 金属
    "Kit_Iron":       {"hex": "#6E6A63", "roughness": 0.58},
    "Kit_Brass":      {"hex": "#C9A227", "roughness": 0.50},
    # 布缆
    "Kit_Sail":       {"hex": "#F5E8C8", "roughness": 0.88},
    "Kit_Rope":       {"hex": "#8A6F4D", "roughness": 0.78},
    # 特殊【提】
    "Kit_Ember":      {"hex": "#FFB347", "roughness": 0.90},   # 篝火余烬亮色
    "Kit_Coral":      {"hex": "#E8845A", "roughness": 0.70},   # 珊瑚/贝彩点缀
    # 远景剪影（SceneArtPalette.cs:85-91；近→远两级 + 云/远帆）
    "Kit_FarNear":    {"hex": "#7E93A8", "roughness": 1.00},
    "Kit_FarFar":     {"hex": "#AFC2D4", "roughness": 1.00},
    "Kit_Cloud":      {"hex": "#FFFFFF", "roughness": 1.00},
    "Kit_FarSail":    {"hex": "#E8E8E0", "roughness": 1.00},
}


def hex_to_rgb(hex_str):
    h = hex_str.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def slot(name):
    """取槽定义；未登记槽名直接抛错（防散写/拼错）。"""
    if name not in SLOTS:
        raise KeyError("style_tokens: 未登记的材质槽 %r（合法槽见 SLOTS 表）" % name)
    return SLOTS[name]


# ---------------------------------------------------------------------------
# 2. 站面平直机制（docs/M4 §4.2）
# ---------------------------------------------------------------------------

def check_standable_boxes(boxes, asset_name=""):
    """校验站面 manifest：顶面中心 Z + 半高必须是 0.5 档、尺寸达标、坐标为有限数。

    boxes: [{"c": [cx, cy, cz], "s": [sx, sy, sz]}, ...]  # Blender 本地系（Z-up，米）
    抛 ValueError 即建模不合规——kit 脚本在导出前必须调用本函数。
    """
    for i, b in enumerate(boxes):
        c, s = b["c"], b["s"]
        top_z = c[2] + s[2] / 2.0
        if abs(top_z / TOP_STEP - round(top_z / TOP_STEP)) > PLANARITY_TOL / TOP_STEP:
            raise ValueError(
                "[%s] 站面 box#%d 顶面 z=%.4f 不是 %.1f 档的整数倍" % (asset_name, i, top_z, TOP_STEP))
        if top_z < MIN_TOP_Z - PLANARITY_TOL:
            raise ValueError(
                "[%s] 站面 box#%d 顶面 z=%.4f 低于最低档 %.2f" % (asset_name, i, top_z, MIN_TOP_Z))
        if min(s[0], s[1]) < MIN_STANDABLE_SIZE:
            raise ValueError(
                "[%s] 站面 box#%d 水平最小边 %.2f < %.2f（立足不下）" % (asset_name, i, min(s[0], s[1]), MIN_STANDABLE_SIZE))
        if s[2] <= 0:
            raise ValueError("[%s] 站面 box#%d 高度非正" % (asset_name, i))
    return True


def write_standable_manifest(asset_name, boxes, fbx_path):
    """把站面 box 表写为 <FBX同目录>/<资产名>.standable.json。返回 JSON 路径。

    Unity 侧 WorldMapAssetBuilder 按 manifest 生成 BoxCollider（视觉自由、碰撞平直两解耦）。
    """
    check_standable_boxes(boxes, asset_name)
    out = os.path.join(os.path.dirname(os.path.abspath(fbx_path)), asset_name + ".standable.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"asset": asset_name, "boxes": boxes}, f, indent=1)
    return out


# ---------------------------------------------------------------------------
# 3. FBX 导出（参数与 build_scene_kit.py:629-646 完全一致；勿改，踩过坑）
# ---------------------------------------------------------------------------

def export_fbx(export_objects, fbx_path):
    """把 export_objects（对象列表）导出为单个 FBX。

    铁律（tools/blender/scene/README.md:56-62）：
    - apply_scale_options='FBX_SCALE_NONE'：不在导出侧乘单位因子（useFileScale 会把 cm×0.01 乘进来）；
    - axis_forward='-Z', axis_up='Y'：Blender Z-up → FBX Y-up；
    - path_mode='COPY' 零贴图；mesh_smooth_type='FACE' 保 flat shading。
    """
    import bpy
    os.makedirs(os.path.dirname(os.path.abspath(fbx_path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in export_objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        apply_scale_options="FBX_SCALE_NONE",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="COPY",
    )
    return fbx_path


# ---------------------------------------------------------------------------
# 4. 预览渲染（观感基线；build_scene_kit.py:694,734-736 同款）
# ---------------------------------------------------------------------------

PREVIEW_VIEW_TRANSFORM = "Standard"  # Blender 5.x 默认 AgX 会洗掉调色板色值，勿删
PREVIEW_BG_HEX = "#7F7F7F"
PREVIEW_GROUND_HEX = "#8C8C8C"
PREVIEW_RES = 1024
PREVIEW_QUALITY = 90


def setup_preview_world(scene):
    """灰底 + 灰地板 + 三灯（暖 key / 冷 fill / 暖 rim）。返回 (world, floor)。"""
    import bpy
    world = scene.world
    if world is None:
        world = bpy.data.worlds.new("ST_PreviewWorld")
        scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (*hex_to_rgb(PREVIEW_BG_HEX), 1.0)
    bg.inputs[1].default_value = 1.0

    floor = bpy.data.meshes.new("ST_PreviewFloor")
    from mathutils import Matrix
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400.0,
                          matrix=Matrix.Translation((0.0, 0.0, -0.01)))
    bm.to_mesh(floor)
    bm.free()
    floor_obj = bpy.data.objects.new("ST_PreviewFloor", floor)
    scene.collection.objects.link(floor_obj)

    def _lamp(name, color, energy, location, target=(0, 0, 1.5)):
        data = bpy.data.lights.new(name, "AREA")
        data.color = hex_to_rgb(color)
        data.energy = energy
        data.size = 60.0
        obj = bpy.data.objects.new(name, data)
        obj.location = location
        d = bpy.data.curves.new(name + "_t", type="TEXT")  # 占位，用 track-to 简化为 look_direction
        bpy.data.curves.remove(d)
        scene.collection.objects.link(obj)
        return obj

    # 三灯姿态沿用样板：暖主光（右上前）、冷补光（左）、暖轮廓（后上）
    _lamp("ST_Key", "#FFE8C8", 90000, (160, -140, 220))
    _lamp("ST_Fill", "#C8DDF0", 36000, (-180, -60, 120))
    _lamp("ST_Rim", "#FFF0D8", 30000, (-40, 180, 160))
    return world, floor_obj


# ---------------------------------------------------------------------------
# 5. STAT 报告（每件资产导出后必须打印；入 kit README）
# ---------------------------------------------------------------------------

def print_stats(asset_name, objects, extra=None):
    tri = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        for poly in obj.data.polygons:
            tri += len(poly.vertices) - 2
    slots = sorted({ms.material.name for obj in objects if obj.type == "MESH"
                    for ms in obj.material_slots if ms.material is not None})
    if len(slots) > SLOT_LIMIT_PER_ASSET:
        raise ValueError("[%s] 材质槽 %d 个超上限 %d：%s" % (asset_name, len(slots), SLOT_LIMIT_PER_ASSET, slots))
    bbox_lo, bbox_hi = None, None
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            w = obj.matrix_world @ __import__("mathutils").Vector(corner)
            bbox_lo = w.copy() if bbox_lo is None else __import__("mathutils").Vector(
                (min(bbox_lo.x, w.x), min(bbox_lo.y, w.y), min(bbox_lo.z, w.z)))
            bbox_hi = w.copy() if bbox_hi is None else __import__("mathutils").Vector(
                (max(bbox_hi.x, w.x), max(bbox_hi.y, w.y), max(bbox_hi.z, w.z)))
    size = (bbox_hi - bbox_lo) if bbox_lo else (0, 0, 0)
    print("STAT %s: tris=%d slots=%s size=%.2fx%.2fx%.2f (X,Y,Z blender)%s"
          % (asset_name, tri, slots, size.x, size.y, size.z,
             (" " + str(extra)) if extra else ""))
    return {"tris": tri, "slots": slots, "size": (size.x, size.y, size.z)}


def budget_guard(asset_name, objects, budget_key):
    if print_stats(asset_name, objects)["tris"] > POLY_BUDGETS[budget_key]:
        raise ValueError("[%s] 面数超预算 %s=%d" % (asset_name, budget_key, POLY_BUDGETS[budget_key]))
