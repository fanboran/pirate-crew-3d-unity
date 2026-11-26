# -*- coding: utf-8 -*-
"""云端漫步重做总装：五个分件（海水 / 云台 / 远景云 / 自然小品 / 活动生物）→ 一个 FBX + 三视图成品图。

用法（仓库根）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/cloudsea/assemble_cloudsea.py
    # 可选：--no-render  --samples 32  --res 1100
"""

import argparse
import importlib
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import cloudsea_common as H  # noqa: E402
import creature_kit as CK    # noqa: E402
import style_tokens as ST    # noqa: E402

MODULES = ("sea", "clouds", "sky", "life", "creatures")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--res", type=int, default=1100)
    args = ap.parse_args(argv)

    H.reset()
    objects = []
    joints = []
    for name in MODULES:
        mod = importlib.import_module("mod_" + name)
        built = mod.build()
        if not isinstance(built, (list, tuple)):
            built = [built]
        objects.extend(built)
        print("MODULE %-10s objects=%d" % (name, len(built)), flush=True)
        if name == "creatures":
            # 水下生物的关节：全场景统一编号、落成空物体（随 FBX 进 Unity），跟生物一起导出。
            empties, joint_count = CK.bind_joints(built)
            joints += empties
            print("JOINTS creatures joints=%d" % joint_count, flush=True)

    H.stats(objects)
    os.makedirs(H.FBX_DIR, exist_ok=True)
    fbx = os.path.join(H.FBX_DIR, "CloudWalk.fbx")
    # 轴向转换烘进顶点：水下生物的物体空间必须是 x 前 / y 上 / z 侧（蒙皮矩阵按它写）。
    ST.export_fbx(objects + joints, fbx, bake_space_transform=True)
    print("FBX %s" % fbx, flush=True)

    if not args.no_render:
        H.preview(objects, "cloudsea", views=("front34", "side", "top"),
                  res=(args.res, int(args.res * 0.62)), samples=args.samples)
    print("ASSEMBLE_OK", flush=True)


if __name__ == "__main__":
    main()
