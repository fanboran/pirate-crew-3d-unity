# -*- coding: utf-8 -*-
"""单件自检：建一个 cloudsea 分件 → STAT → 预览图。

用法（仓库根）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/cloudsea/preview_module.py -- base
    # 可选：--views front34,side,top  --res 900  --samples 24  --no-render

分件脚本 `mod_<name>.py` 必须暴露 `def build(): return [bpy objects]`。
"""

import argparse
import importlib
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import cloudsea_common as H  # noqa: E402

MODULES = ("sea", "clouds", "sky", "life", "creatures")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("module", choices=MODULES)
    ap.add_argument("--views", default="front34,closeup")
    ap.add_argument("--res", type=int, default=900)
    ap.add_argument("--samples", type=int, default=24)
    ap.add_argument("--no-render", action="store_true")
    args = ap.parse_args(argv)

    H.reset()
    mod = importlib.import_module("mod_" + args.module)
    objs = mod.build()
    if not isinstance(objs, (list, tuple)):
        objs = [objs]

    H.stats(objs)
    if not args.no_render:
        views = tuple(v.strip() for v in args.views.split(",") if v.strip())
        H.preview(objs, "cloudsea_" + args.module, views=views,
                  res=(args.res, int(args.res * 0.62)), samples=args.samples)
    print("PREVIEW_OK %s" % args.module, flush=True)


if __name__ == "__main__":
    main()
