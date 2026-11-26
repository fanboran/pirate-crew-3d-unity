# -*- coding: utf-8 -*-
"""cloudsea/preview_gameview.py —— 按出图机位看整场，并量活动物落在可见水面上的比例。

用法（仓库根）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/cloudsea/preview_gameview.py
    # 可选：--center -3.4,2.8   --res 960   --samples 16   --no-beauty

【机位】与 CameraFraming 同口径：偏航 45°（相机在 +X/+Y 一侧）、俯角 30°、正交，
    画布宽 26.67 单位（= 取景 ortho 7.5，36 艺术像素 / 单位）。--center 给画面中心落在
    地面（z=0）的本地坐标；缺省是场地中心。
    【怎么对上一次实拍】BattleHudScreenshot 的「勘」日志列出每个单位的世界坐标与视口 (u, v)。
    在 Unity 世界系里它们满足 u = a·x + b·z + c、v = d·x + e·z + cos30°·y/15 + f；
    拿 ≥3 个单位做最小二乘解出 a..f，令 u = v = 0.5、y = 0 解出的 (x, z) 就是画面中心的
    世界坐标，减去场景根 (20, 15) 得本地坐标喂给 --center。

【为什么要它】云台的裙摆会挡住它后下方的水：摆在云台正下方的鱼、贴在云台背后的蟹，
    在俯视镜头里根本看不见。摆放表改完先跑这里，别等进引擎出图才发现。

【产物】external/cloudsea-work/gameview/（不入库）：
    gameview-<center>.png        整场机位图（与 Unity 出图的艺术画布同尺寸，可逐像素对照）
    gameview-<center>-mask.png   可见水面掩膜：水面绿、其余黑
终端逐只打印 VIEW 行：对象名、画布像素（左上为原点）、可见比例、判定。
"""

import argparse
import importlib
import math
import os
import sys
import time

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import cloudsea_common as H  # noqa: E402
import kit_common as K       # noqa: E402
import mod_creatures as CR   # noqa: E402

MODULES = ("sea", "clouds", "sky", "life", "creatures")
OUT_DIR = os.path.join(H.WORK_DIR, "gameview")

#: CameraFraming 口径（见模块头）。
PITCH = math.radians(30.0)
CANVAS_UNITS_WIDE = 26.67
CANVAS_W, CANVAS_H = 960, 540
CAM_FWD = Vector((-math.cos(PITCH) / math.sqrt(2.0), -math.cos(PITCH) / math.sqrt(2.0), -math.sin(PITCH)))
PX_PER_UNIT = CANVAS_W / CANVAS_UNITS_WIDE


def emission(name, rgb):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    for n in list(nodes):
        nodes.remove(n)
    out = nodes.new("ShaderNodeOutputMaterial")
    emi = nodes.new("ShaderNodeEmission")
    emi.inputs["Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    emi.inputs["Strength"].default_value = 1.0
    mat.node_tree.links.new(emi.outputs["Emission"], out.inputs["Surface"])
    return mat


def make_camera(center, z_focus=0.0):
    focus = Vector((center[0], center[1], z_focus))
    cam = K.make_camera(tuple(focus - CAM_FWD * 60.0), tuple(focus))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = CANVAS_UNITS_WIDE
    cam.data.clip_end = 900.0
    return cam


def visible_fraction(mask, w, h, px, py, r_px):
    """以 (px, py)（左下为原点）为心、r_px 为半径的圆盘里，绿色像素的占比。"""
    green = 0
    total = 0
    steps = 9
    for i in range(steps):
        for j in range(steps):
            dx = (i + 0.5) / steps * 2.0 - 1.0
            dy = (j + 0.5) / steps * 2.0 - 1.0
            if dx * dx + dy * dy > 1.0:
                continue
            x = int(px + dx * r_px)
            y = int(py + dy * r_px)
            if not (0 <= x < w and 0 <= y < h):
                total += 1
                continue
            total += 1
            k = (y * w + x) * 4
            if mask[k + 1] > 0.5 and mask[k] < 0.25:
                green += 1
    return green / max(total, 1)


def render_mask(scene, cam, w, h, black, green, objects, path):
    """可见水面掩膜（水面绿、其余黑）→ 像素数组（左下为原点的 RGBA 浮点）。"""
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.materials.clear()
        obj.data.materials.append(green if obj.name == "Cloud_Sea_Surface" else black)
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.0, 0.0, 0.0, 1.0)
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return bpy.data.images.load(path).pixels[:]


def probe(scene, objects, centres, w, h, black, green):
    """网格探测：对每个机位算掩膜，汇总成 ASCII 图。

    两种高度各出一张：Z = -1.3（游泳水层）与 Z = -2.5（贴底）。判定取该点画布像素四周一圈
    全是水面——留一像素余量，贴着云台裙摆的点不算。图例：数字 = 几个机位下可见（5 = 全部），
    . = 都不可见。坐标轴标的是 Blender 本地系。
    """
    cams = [make_camera(centre) for centre in centres]
    masks = [render_mask(scene, cam, w, h, black, green, objects,
                         os.path.join(OUT_DIR, "probe-%s,%s-mask.png" % centre))
             for cam, centre in zip(cams, centres)]
    for label, z in (("游泳水层 Z=-1.3", -1.3), ("贴底 Z=-2.5", -2.5)):
        lines = ["PROBE %s  机位中心：%s" % (label, "  ".join("%s,%s" % c for c in centres))]
        for yi in range(14, -15, -1):                       # Y 从 +14 到 -14（屏幕上方向下）
            row = "%+4d " % yi
            for xi in range(-19, 20):
                on = 0
                for cam, mask in zip(cams, masks):
                    uv = world_to_camera_view(scene, cam, Vector((xi, yi, z)))
                    px, py = int(uv.x * w), int(uv.y * h)
                    clear = True
                    for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (3, 0), (-3, 0), (0, 3), (0, -3)):
                        x, y = px + dx, py + dy
                        if not (0 <= x < w and 0 <= y < h):
                            clear = False
                            break
                        k = (y * w + x) * 4
                        if not (mask[k + 1] > 0.5 and mask[k] < 0.25):
                            clear = False
                            break
                    on += 1 if clear else 0
                row += str(on) if on else "."
            lines.append(row)
        lines.append("      " + "".join("%+4d" % xi if xi % 4 == 0 else "    " for xi in range(-19, 20)))
        print("\n".join(lines), flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--center", default="0,0")
    ap.add_argument("--res", type=int, default=CANVAS_W)
    ap.add_argument("--samples", type=int, default=16)
    ap.add_argument("--no-beauty", action="store_true")
    ap.add_argument("--no-creatures", action="store_true")
    ap.add_argument("--probe", action="store_true",
                    help="只在场地网格上探测可见性并打印 ASCII 图（多机位），不出机位图")
    args = ap.parse_args(argv)
    center = tuple(float(v) for v in args.center.split(","))
    w, h = args.res, int(round(args.res * CANVAS_H / CANVAS_W))
    scale = w / float(CANVAS_W)
    tag = "%s,%s" % (center[0], center[1])

    H.reset()
    objects = []
    for name in MODULES:
        if name == "creatures" and (args.no_creatures or args.probe):
            continue
        t0 = time.time()
        mod = importlib.import_module("mod_" + name)
        built = mod.build()
        objects.extend(built if isinstance(built, (list, tuple)) else [built])
        print("MODULE %-9s objects=%d  %.1fs" % (name, len(objects), time.time() - t0), flush=True)

    os.makedirs(OUT_DIR, exist_ok=True)
    scene = bpy.context.scene
    cam = make_camera(center)
    K.setup_render(res=(w, h), samples=args.samples)
    K.setup_world("#CFE6F2")
    scene.render.image_settings.file_format = "PNG"
    black = emission("_mask_black", (0.0, 0.0, 0.0))
    green = emission("_mask_green", (0.0, 1.0, 0.0))

    if args.probe:
        # 典型机位：场地中心 + 出图实拍中心（向西南偏）+ 三个对称偏移。相机跟着单位走，
        # 中心大体在这几个之间。
        probe(scene, objects, [(0.0, 0.0), (-3.4, 2.8), (3.4, -2.8), (-3.4, -2.8), (3.4, 2.8)],
              w, h, black, green)
        print("PROBE_OK", flush=True)
        return

    beauty = os.path.join(OUT_DIR, "gameview-%s.png" % tag)
    if not args.no_beauty:
        scene.render.filepath = beauty
        bpy.ops.render.render(write_still=True)
        print("GAMEVIEW %s" % beauty, flush=True)

    # ---- 可见水面掩膜：水面绿，其余（含天空）黑 ----
    mask_path = os.path.join(OUT_DIR, "gameview-%s-mask.png" % tag)
    mask = render_mask(scene, cam, w, h, black, green, objects, mask_path)
    water = sum(1 for k in range(0, len(mask), 4) if mask[k + 1] > 0.5 and mask[k] < 0.25) / float(w * h)
    print("GAMEVIEW mask=%s  可见水面占画布 %.0f%%" % (mask_path, water * 100.0), flush=True)

    hidden = 0
    for obj in objects:
        if not obj.name.startswith(CR.PREFIX):
            continue
        uv = world_to_camera_view(scene, cam, obj.location)
        px, py = uv.x * w, uv.y * h
        frac = visible_fraction(mask, w, h, px, py, float(obj["radius"]) * PX_PER_UNIT * scale)
        on = 0.0 <= uv.x <= 1.0 and 0.0 <= uv.y <= 1.0
        verdict = "可见" if frac >= 0.9 else ("半遮" if frac >= 0.4 else ("出画" if not on else "被挡"))
        if frac < 0.9:
            hidden += 1
        print("VIEW %-26s 画布=(%4d,%4d)  占位半径=%4.1fpx  可见=%3.0f%%  %s"
              % (obj.name, uv.x * CANVAS_W, (1.0 - uv.y) * CANVAS_H,
                 float(obj["radius"]) * PX_PER_UNIT, frac * 100.0, verdict), flush=True)
    print("GAMEVIEW_OK 完全可见之外的对象 %d 个" % hidden, flush=True)


if __name__ == "__main__":
    main()
