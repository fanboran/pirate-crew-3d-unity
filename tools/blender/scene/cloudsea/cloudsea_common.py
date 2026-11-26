# -*- coding: utf-8 -*-
"""云端漫步 · 场景重做 —— 公共几何/坐标/预览层。

【主题】「云端漫步」：7 座蓬松云台浮在浅海上，下方不远处就是水面；水下有海床、
海草、鱼群，天空有大量海鸥。**无人建物**（没有哨站/灯塔/绳桥/船/旗帜）。

【坐标（Blender Z-up，1 单位 = 1 m）】
    · 所有模块共用**本地原点 = 竞技场中心**：Unity 世界摆放 (20, 0, 15)。
    · 【2026-11-20 翻号】FBX bakeAxisConversion 把 Blender y 直通 Unity z（不取负），
      本地 Blender(x, y) = Unity(x, z) 偏移；Unity +Z（南/镜头侧）= Blender +Y。
      旧约定（Unity z = −Blender y）会在 Unity 里把整场镜像，角色全部悬在海面上。
    · 水面 z=-0.4（= 现役死亡线），水底 z=-3.2；可玩云台顶 z=2.5–7.5。
    · 默认相机从东南方看（Blender +X/+Y）→ 正对主云台与海面。

【模块】mod_sea（海水切片/海床/海草/气泡）/ mod_clouds（7 座云台 + 云瀑）/
        mod_sky（远景云岛/薄云）/ mod_life（停栖海鸥/羽毛等自然小品）/
        mod_creatures（鱼群/大鱼/水母/蟹/海星等活动物）。
"""

import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
SCENE_DIR = os.path.dirname(HERE)
if SCENE_DIR not in sys.path:
    sys.path.insert(0, SCENE_DIR)
sys.path.insert(0, os.path.join(SCENE_DIR, "chemplant"))
import kit_common as K          # noqa: E402
import style_tokens as ST       # noqa: E402

ROOT = os.path.abspath(os.path.join(SCENE_DIR, "..", "..", ".."))
FBX_DIR = os.path.join(ROOT, "pirate-crew", "Assets", "Art", "Models", "SceneKit")
SHOT_DIR = os.path.join(ROOT, "docs", "images", "cloudsea")
WORK_DIR = os.path.join(ROOT, "external", "cloudsea-work")

# ---------------------------------------------------------------------------
# 场地常量（Blender 本地系）
# ---------------------------------------------------------------------------

ARENA_HALF_X = 20.0
ARENA_HALF_Y = 15.0

#: 海水切片：水面 = 现役水位 -0.4（落水即死线），水底 -3.2。
SEA_TOP_Z = -0.4
SEA_BOTTOM_Z = -3.2

#: 7 座可玩云台：name, cx, cy, top_z, half_x, half_y, role
#: 【by = Unity z 偏移（2026-11-20 翻号）】FBX bakeAxisConversion 把 Blender y 直通 Unity z
#: （不取负），本表 by 必须直接写 Unity z 偏移，视觉才与逻辑地形/设计表对位。
PLATFORMS = [
    ("Hero",      0.0,   0.0, 4.5, 6.5, 5.5, "主战场"),
    ("North",    -3.0, -10.0, 6.5, 4.0, 3.5, "蓝队出生/制高"),
    ("West",    -13.0,   1.0, 3.0, 3.5, 4.0, "西侧跳板"),
    ("East",     12.0,  -2.0, 7.5, 3.5, 3.5, "东侧制高"),
    ("South",     4.0,  10.0, 5.5, 3.5, 3.0, "红队出生"),
    ("NW_Islet", -14.0, -10.0, 2.5, 2.5, 2.5, "外环小台"),
    ("SE_Islet",  13.0,  10.0, 6.0, 2.5, 2.5, "外环小台"),
]

#: 云瀑：从 East 云台边缘泻下、落入海面（自然物）
WATERFALL = ((13.8, -2.0, 7.2), (15.0, -1.0, SEA_TOP_Z))

#: 远景小云岛（自然云岛，无建筑）。【2026-11-20 翻号 + 重锚】翻号后四角让位给翻过来的
#: 可玩平台，三座远景岛重锚到不与平台/云带相撞的远端（西/东/北各一）。
DISTANT_ISLETS = [
    ("Far_W", -16.0,  13.0, 1.5, 4.0),
    ("Far_E",  16.0, -13.0, 2.0, 5.0),
    ("Far_N", -10.0, -15.0, 0.8, 3.5),
]

#: 海草床锚点（水下；每处 4-8 株海草）
SEAWEED_BEDS = [
    (-6.0, 3.0, 5.0), (7.0, -4.0, 4.0), (-12.0, -8.0, 3.5),
    (12.0, 8.0, 4.5), (0.0, 10.0, 3.0), (-16.0, 0.0, 3.0),
]

#: 水下岩簇（海床起伏）
ROCK_CLUSTERS = [
    (-3.0, -2.0, 1.4), (8.0, 4.0, 1.8), (-11.0, 3.0, 1.2),
    (14.0, -4.0, 1.6), (2.0, 8.0, 1.1),
]

#: 海鸥停栖点（云台边缘；静态停栖鸥由 mod_life 建，飞行鸥由运行时 flock 生成）
BIRD_PERCHES = [
    (6.2, 0.0, 4.5), (-6.0, -2.5, 4.5), (-3.0, -7.2, 6.5),
    (12.0, -4.0, 7.5), (4.0, 8.0, 5.5),
]


def u2b(x, y, z):
    """Unity 偏移 → Blender 本地：新约定下 Blender x/y 直接等于 Unity x/z（z 是高度）。"""
    return (x, z, y)


def reset():
    return K.reset_scene()


# ---------------------------------------------------------------------------
# 预览
# ---------------------------------------------------------------------------

VIEWS = {
    "front34": ((34.0, 38.0, 24.0), (0.0, 0.0, 2.0)),
    "side": ((44.0, 0.0, 10.0), (0.0, 0.0, 2.0)),
    "top": ((0.001, 0.0, 54.0), (0.0, 0.0, 0.0)),
    "closeup": ((12.0, 15.0, 11.0), (0.0, 0.0, 1.5)),
}


def preview(objects, asset_name, views=("front34",), res=(900, 560), samples=24):
    os.makedirs(SHOT_DIR, exist_ok=True)
    paths = []
    for view in views:
        if view not in VIEWS:
            raise KeyError("未知预览视角 %r（可选 %s）" % (view, list(VIEWS)))
        cam, target = VIEWS[view]
        out = os.path.join(SHOT_DIR, "%s-%s.png" % (asset_name, view))
        # floor=False：云海底在 z=-3.2，灰地板会挡住剖面。
        K.preview(objects, out, cam=cam, target=target, res=res, samples=samples,
                  floor=False, bg="#CFE6F2")
        paths.append(out)
    return paths


def stats(objects):
    total = 0
    for obj in objects:
        me = obj.data
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        total += tris
        mats = [m.name if m else "<none>" for m in me.materials]
        print("STAT %-24s tris=%6d slots=%d %s"
              % (obj.name, tris, len(mats), ",".join(mats)), flush=True)
    print("STAT TOTAL tris=%d objects=%d" % (total, len(objects)), flush=True)
    return total
