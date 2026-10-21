# -*- coding: utf-8 -*-
"""
style_tokens —— 世界套件（WorldKit）统一风格参数，单一事实源。

所有 kit 脚本（tools/blender/scene/<kit>/*.py）必须：
    import sys, os
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # -> tools/blender/scene/
    import style_tokens as ST
然后只用 ST 里的常量/工具，**禁止在脚本内散写色值、粗糙度、导出参数**。

契约文档（已归档）：docs/项目/归档/M4-世界化/大海域世界化.md §4（尺度契约见 §1，站面平直机制见 §4.2）。
调色板真源：pirate-crew/Assets/Data/Palette/（共享核心板 pirate_palette.json + 域板 palette_<域>.json，
分域两层体系见 docs/技术/资产管线/调色板.md）——本文件不再散写板内色值，槽位表按（域, 槽 id）引用。
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
#    【色值不在本文件】sRGB hex 唯一取自调色板真源（共享核心板 + 域板，分域两层；
#    板管色、本表管 roughness 等材质参数）。表项按（域, 槽 id）声明，导入时解析成
#    原始 {"hex", "roughness"} 形状 —— kit 脚本照旧 ST.slot(name)["hex"]，不感知拆分。
#    · domain "core"                 = 共享核心板（跨关共用：墨线/金属/布缆/点缀/远景…）
#    · domain "caribbean"/"chemplant" = 域板（palette_<域>.json，每关/每生物群系的地形风格档）
#    标【板外】的槽尚未入板，暂留字面值；要收编先在共享核心板登记槽位。
#    标【提】的槽为 M4 新增提案槽，Unity 侧表由协调者同步扩展。
# ---------------------------------------------------------------------------

_PALETTE_DIR = os.path.join(
    # 本文件在 <仓库根>/tools/blender/scene/ 下：文件目录向上三级 = 仓库根
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))),
    "pirate-crew", "Assets", "Data", "Palette")
_CORE_FILE = "pirate_palette.json"
_DOMAIN_FILE = "palette_%s.json"
_HEX_CACHE = {}


def palette_hex(domain, slot_id):
    """从调色板真源取槽位 hex（无 #；域板与共享核心板同读法，结果按（域, id）缓存）。"""
    key = (domain, slot_id)
    if key not in _HEX_CACHE:
        path = os.path.join(_PALETTE_DIR, _CORE_FILE if domain == "core" else _DOMAIN_FILE % domain)
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        for s in data["slots"]:
            _HEX_CACHE[(domain, s["id"])] = str(s["hex"]).lstrip("#")
        if key not in _HEX_CACHE:
            raise KeyError("style_tokens: 调色板（域 %s）没有槽位 %r（合法槽见 %s）" % (domain, slot_id, path))
    return _HEX_CACHE[key]


_SLOT_DECLS = {
    # 木（三档）—— 加勒比域板 WOOD 族
    "Kit_WoodLight":  {"domain": "caribbean", "slot": "WOOD_LIGHT", "roughness": 0.72},
    "Kit_WoodMid":    {"domain": "caribbean", "slot": "WOOD_MID", "roughness": 0.72},
    "Kit_WoodDark":   {"domain": "caribbean", "slot": "WOOD_DARK", "roughness": 0.74},
    # 沙（三档）+ 湿沙 —— 加勒比域板 SAND 族
    "Kit_SandLight":  {"domain": "caribbean", "slot": "SAND_LIGHT", "roughness": 0.82},
    "Kit_SandMid":    {"domain": "caribbean", "slot": "SAND_MID", "roughness": 0.82},
    "Kit_SandDark":   {"domain": "caribbean", "slot": "SAND_DARK", "roughness": 0.82},
    "Kit_WetSand":    {"domain": "caribbean", "slot": "SAND_WET", "roughness": 0.60},
    # 草/植被（三档；树叶复用草地档——美术风格指南 §2.1）—— 加勒比域板 GRASS 族
    "Kit_GrassLight": {"domain": "caribbean", "slot": "GRASS_LIGHT", "roughness": 0.86},
    "Kit_GrassMid":   {"domain": "caribbean", "slot": "GRASS_MID", "roughness": 0.86},
    "Kit_GrassDark":  {"domain": "caribbean", "slot": "GRASS_DARK", "roughness": 0.86},
    # 岩（三档）—— 加勒比域板 ROCK 族
    "Kit_RockLight":  {"domain": "caribbean", "slot": "ROCK_LIGHT", "roughness": 0.78},
    "Kit_RockMid":    {"domain": "caribbean", "slot": "ROCK_MID", "roughness": 0.78},
    "Kit_RockDark":   {"domain": "caribbean", "slot": "ROCK_DARK", "roughness": 0.78},
    # 金属 —— 共享核心板 METAL 族
    "Kit_Iron":       {"domain": "core", "slot": "IRON", "roughness": 0.58},
    "Kit_Brass":      {"domain": "core", "slot": "BRASS", "roughness": 0.50},
    # 布缆 —— 共享核心板 CLOTH 族
    "Kit_Sail":       {"domain": "core", "slot": "SAIL_CANVAS", "roughness": 0.88},
    "Kit_Rope":       {"domain": "core", "slot": "ROPE", "roughness": 0.78},
    # 特殊【提】—— 共享核心板 ACCENT 族
    "Kit_Ember":      {"domain": "core", "slot": "EMBER", "roughness": 0.90},   # 篝火余烬亮色
    "Kit_Coral":      {"domain": "core", "slot": "CORAL", "roughness": 0.70},   # 珊瑚/贝彩点缀
    # 工业/废弃【提】——第三样板关「废弃化工厂」用（kit: tools/blender/scene/chemplant/）
    # —— 化工厂域板 CONCRETE/RUST/STEEL/GLASS/PAINT 族；Unity 侧同源表：
    #    pirate-crew/Assets/Editor/WorldMapAssetSetBuilder.cs（Slots）
    "Kit_ConcreteLight": {"domain": "chemplant", "slot": "CONCRETE_LIGHT", "roughness": 0.84},
    "Kit_ConcreteMid":   {"domain": "chemplant", "slot": "CONCRETE_MID", "roughness": 0.84},
    "Kit_ConcreteDark":  {"domain": "chemplant", "slot": "CONCRETE_DARK", "roughness": 0.84},
    "Kit_Rust":          {"domain": "chemplant", "slot": "RUST_LIGHT", "roughness": 0.86},
    "Kit_RustDark":      {"domain": "chemplant", "slot": "RUST_DARK", "roughness": 0.88},
    "Kit_SteelBlue":     {"domain": "chemplant", "slot": "STEEL_DARK", "roughness": 0.62},
    "Kit_SteelPale":     {"domain": "chemplant", "slot": "STEEL_LIGHT", "roughness": 0.60},
    "Kit_GlassDark":     {"domain": "chemplant", "slot": "GLASS_DARK", "roughness": 0.35},
    "Kit_PaintYellow":   {"domain": "chemplant", "slot": "PAINT_YELLOW", "roughness": 0.66},
    # 远景剪影（SceneArtPalette.cs:85-91；近→远两级 + 云/远帆）—— 共享核心板 FARFIELD 族
    "Kit_FarNear":    {"domain": "core", "slot": "FAR_NEAR", "roughness": 1.00},
    "Kit_FarFar":     {"domain": "core", "slot": "FAR_FAR", "roughness": 1.00},
    "Kit_Cloud":      {"domain": "core", "slot": "CLOUD", "roughness": 1.00},
    # 【板外】云/远帆辅档，尚未入共享核心板（要收编先在板上登记槽位）
    "Kit_CloudShade": {"hex": "#DCE6EE", "roughness": 1.00},   # 云背光/云底（CloudWarmWhite 同系冷档）
    "Kit_CloudGold":  {"hex": "#F2E4C4", "roughness": 1.00},   # 云晒面淡金斑（CloudPaleGold 同源）
    "Kit_FarSail":    {"hex": "#E8E8E0", "roughness": 1.00},
}


def _build_slots(decls):
    """把槽声明解析成 kit 消费的原始形状 {"hex", "roughness"}（hex 带 #，导入时取板）。"""
    slots = {}
    for name, decl in decls.items():
        if "hex" in decl:      # 【板外】槽：字面值
            slots[name] = {"hex": decl["hex"], "roughness": decl["roughness"]}
        else:                  # 板内槽：色值唯一取自调色板真源
            slots[name] = {"hex": "#" + palette_hex(decl["domain"], decl["slot"]),
                           "roughness": decl["roughness"]}
    return slots


SLOTS = _build_slots(_SLOT_DECLS)


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

def print_stats(asset_name, objects, extra=None, slot_limit=None):
    """打印 STAT 行并返回统计。slot_limit 缺省 = SLOT_LIMIT_PER_ASSET（套件件纪律）。

    整场总装件（一件 = 一整关，非 Unity 换装单元）可显式放宽：见 chemplant/README.md
    「总装件槽上限」——总装 FBX 的槽数 = 各分件槽并集，天生超过 8。
    """
    tri = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        for poly in obj.data.polygons:
            tri += len(poly.vertices) - 2
    slots = sorted({ms.material.name for obj in objects if obj.type == "MESH"
                    for ms in obj.material_slots if ms.material is not None})
    limit = SLOT_LIMIT_PER_ASSET if slot_limit is None else slot_limit
    if len(slots) > limit:
        raise ValueError("[%s] 材质槽 %d 个超上限 %d：%s" % (asset_name, len(slots), limit, slots))
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
