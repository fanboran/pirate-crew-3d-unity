# -*- coding: utf-8 -*-
"""preview_module.py —— 单分件自检器：建一件 → STAT → 出图（不碰其他分件、不开 Unity）。

分件脚本（mod_*.py）收工前必须跑它看一眼自己的东西。

用法（仓库根执行）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/chemplant/preview_module.py -- towers
    # 可选：
    #   --res 900            出图宽度（高 = 宽 × 0.62）
    #   --samples 24         Cycles 采样
    #   --no-render          只建模 + STAT
    #   --cam x,y,z          机位（缺省按分区包围盒自动取 3/4 鸟瞰）
    #   --target x,y,z       视点（缺省 = 分区中心抬高 1/3 高度）
    #   --out <路径>         出图落点（缺省 external/chemplant-work/<件名>_check.jpg）

分区名 → 模块文件：towers/mod_towers.py、tanks/mod_tanks.py、pipes/mod_pipes.py、
building/mod_building.py、site/mod_site.py、props/mod_props.py。
"""

import importlib
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kit_common as K                                          # noqa: E402


def parse(argv):
    opts = {"name": argv[0] if argv and not argv[0].startswith("--") else "towers",
            "res": 900, "samples": 24, "render": True, "cam": None, "target": None,
            "out": None}
    i = 1 if argv and not argv[0].startswith("--") else 0
    while i < len(argv):
        a = argv[i]
        if a == "--res":
            opts["res"] = int(argv[i + 1]); i += 2
        elif a == "--samples":
            opts["samples"] = int(argv[i + 1]); i += 2
        elif a == "--no-render":
            opts["render"] = False; i += 1
        elif a == "--cam":
            opts["cam"] = tuple(float(v) for v in argv[i + 1].split(",")); i += 2
        elif a == "--target":
            opts["target"] = tuple(float(v) for v in argv[i + 1].split(",")); i += 2
        elif a == "--out":
            opts["out"] = argv[i + 1]; i += 2
        else:
            i += 1
    return opts


def auto_camera(name):
    """按分区包围盒自动取 3/4 鸟瞰机位（分件脚本不用自己算机位）。"""
    area = K.AREAS[name]
    sx, sy, sz = area["size"]
    span = max(sx, sy, sz * 1.3)
    dist = span * 1.45
    az = math.radians(-125.0)                                    # 西南方向看过来
    el = math.radians(24.0)
    cx, cy, cz = 0.0, 0.0, sz * 0.42
    cam = (cx + dist * math.cos(el) * math.cos(az),
           cy + dist * math.cos(el) * math.sin(az),
           cz + dist * math.sin(el))
    return cam, (cx, cy, cz)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = parse(argv)
    name = o["name"]
    if name not in K.AREAS:
        raise SystemExit("未知分件 %r（合法：%s）" % (name, sorted(K.AREAS)))

    K.reset_scene()
    mod = importlib.import_module("mod_" + name)
    m = K.Mesher()
    mod.build(m)
    nv, nf, nt = m.counts()
    print("[chemplant] %s raw: verts=%d faces=%d tris=%d" % (name, nv, nf, nt), flush=True)
    obj = m.to_object("ChemPlant_" + name.capitalize())
    K.stats([obj], name, slot_limit=99)

    if not o["render"]:
        print("[chemplant] %s OK (no render)" % name, flush=True)
        return
    cam, target = auto_camera(name)
    if o["cam"]:
        cam = o["cam"]
    if o["target"]:
        target = o["target"]
    out = o["out"] or os.path.join(K.WORK_DIR, "%s_check.jpg" % name)
    K.preview([obj], out, cam=cam, target=target, res=(o["res"], int(o["res"] * 0.62)),
              samples=o["samples"], lens=42)
    print("[chemplant] %s OK -> %s" % (name, out), flush=True)


if __name__ == "__main__":
    main()
