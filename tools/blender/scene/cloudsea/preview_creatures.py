# -*- coding: utf-8 -*-
"""cloudsea/preview_creatures.py —— 生物造型自检：每个物种一件，排成网格出两张正交图。

用法（仓库根）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/cloudsea/preview_creatures.py
    # 可选：--res 1600  --samples 16

出图落 external/cloudsea-work/lineup/（不入库）：
    lineup-side.png   正侧视：看剪影与花纹（头朝右）
    lineup-game.png   默认取景机位（偏航 45° / 俯角 30°，CameraFraming 口径）：看进游戏后读成什么样
同时打印每个物种的面数与顶点动画数据概览（动画族是否齐、uv 值域是否对）。
"""

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import cloudsea_common as H  # noqa: E402
import creature_kit as CK    # noqa: E402
import kit_common as K       # noqa: E402
import mod_creatures as CR    # noqa: E402

OUT_DIR = os.path.join(H.WORK_DIR, "lineup")
CELL = 4.4

#: (物种名, 建模函数, 摆放高度)。四列一行：小鱼 / 大鱼 / 漂浮与固着 / 底栖。
LINEUP = [
    ("Clownfish", lambda: CR.clownfish(0.3), 1.2), ("Tang", lambda: CR.blue_tang(0.3), 1.2),
    ("YellowTang", lambda: CR.yellow_tang(0.3), 1.2), ("Bait", lambda: CR.bait_fish(0.3), 1.2),
    ("Sword", lambda: CR.swordfish(0.3), 1.2), ("Puffer", lambda: CR.pufferfish(0.3), 1.4),
    ("Turtle", lambda: CR.sea_turtle(0.3), 1.0), ("Seahorse", lambda: CR.seahorse(0.3), 0.4),
    ("Jelly", lambda: CR.jellyfish(0.3), 1.9), ("Octopus", lambda: CR.octopus(0.3), 0.0),
    ("Anemone", lambda: CR.anemone(0.3), 0.0), ("CrabWalk", lambda: CR.crab(0.3), 0.0),
    ("CrabWave", lambda: CR.crab(0.3, wave=True), 0.0), ("Star", lambda: CR.starfish(0.3), 0.0),
    ("StarGold", lambda: CR.starfish(0.3, gold=True), 0.0), ("Manta", lambda: CR.manta_ray(0.3), 1.4),
]

#: 取景机位（CameraFraming 口径）：相机在 +X/+Y 一侧、俯角 30°，看向 -X/-Y。
CAM_FWD = Vector((-0.606, -0.618, -0.5)).normalized()
CAM_RIGHT = Vector((-0.7071, 0.7071, 0.0))
CAM_UP_GROUND = Vector((-0.7071, -0.7071, 0.0))     # 地面上的「屏幕上方」


def report(name, c, obj):
    me = obj.data
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    modes = sorted({int(m) for m in c.anim_m})
    uvs = [layer.name for layer in me.uv_layers]
    smax = max(c.anim_s) if c.anim_s else 0.0
    wmax = max(c.anim_w) if c.anim_w else 0.0
    print("CREATURE %-10s verts=%5d tris=%5d modes=%s uv=%s s_max=%.2f w_max=%.3f slots=%s"
          % (name, len(me.vertices), tris, modes, uvs, smax, wmax, len(me.materials)),
          flush=True)


def build(view):
    """侧视：按 X / 高度排成整齐的 4×4（看剪影）；游戏机位：按屏幕的右 / 上两轴排（看进游戏的读法）。"""
    objects = []
    for idx, (name, maker, z) in enumerate(LINEUP):
        col, row = idx % 4, idx // 4
        c = maker()
        obj = c.to_object("Lineup_" + name)
        if view == "side":
            at = Vector(((col - 1.5) * CELL, 0.0, 0.0))
            z += (3 - row) * 2.9          # 行距按各行的最高 / 最低点错开（水母触手垂 1.35、剑鱼帆高 1.0）
        else:
            at = CAM_RIGHT * ((col - 1.5) * CELL) + CAM_UP_GROUND * ((1.5 - row) * CELL)
        obj.location = (at.x, at.y, z)
        obj.rotation_euler = (0.0, 0.0, math.radians(0.0 if view == "side" else 135.0))
        report(name, c, obj)
        objects.append(obj)
    return objects


def render(objects, out, view, res, samples):
    scene = bpy.context.scene
    K.setup_render(res=(res, int(res * 0.625)), samples=samples)
    K.setup_world("#CFE6F2")
    scene.render.image_settings.file_format = "PNG"
    K.add_sun(rot_deg=(50.0, 0.0, 35.0))
    ground = K.Mesher()
    half = 3.2 * CELL
    ground.box((2.0 * half, 2.0 * half, 0.02), at=(-half, -half, -0.02), mat="Cloud_Seabed")
    tmp = [ground.to_object("_lineup_ground")]
    cam = K.make_camera((0.0, 0.0, 0.0), (0.0, 0.0, 0.0))
    cam.data.type = "ORTHO"
    focus = Vector((0.0, 0.0, 0.8))
    if view == "side":
        cam.data.ortho_scale = 4.0 * CELL
        cam.location = (0.0, -40.0, 4.6)                         # 对焦在四行的中线附近
        cam.rotation_euler = (math.radians(90.0), 0.0, 0.0)      # 看向 +Y，头朝右
    else:
        cam.data.ortho_scale = 4.0 * CELL
        cam.location = focus - CAM_FWD * 40.0
        cam.rotation_euler = (focus - Vector(cam.location)).to_track_quat("-Z", "Y").to_euler()
    os.makedirs(OUT_DIR, exist_ok=True)
    scene.render.filepath = os.path.join(OUT_DIR, out)
    bpy.ops.render.render(write_still=True)
    for o in tmp + [cam]:
        bpy.data.objects.remove(o, do_unlink=True)
    print("LINEUP %s" % scene.render.filepath, flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--res", type=int, default=1600)
    ap.add_argument("--samples", type=int, default=16)
    args = ap.parse_args(argv)

    H.reset()
    for view in ("side", "game"):
        for obj in list(bpy.data.objects):
            if obj.name.startswith("Lineup_"):
                bpy.data.objects.remove(obj, do_unlink=True)
        render(build(view), "lineup-%s.png" % view, view, args.res, args.samples)
    print("LINEUP_OK", flush=True)


if __name__ == "__main__":
    main()
