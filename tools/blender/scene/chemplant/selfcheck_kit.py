# -*- coding: utf-8 -*-
"""selfcheck_kit.py —— kit_common 图元冒烟自检（不开工前先跑它）。

用法（仓库根执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/scene/chemplant/selfcheck_kit.py -- [--render]

每个图元都建一遍（有异常当场炸），打印 STAT；--render 再出一张 640px 自检图。
"""

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit_common as K                                          # noqa: E402


def build():
    m = K.Mesher()
    # 基本体
    m.box((3, 2, 1), at=(-12, 0, 0), mat="Kit_ConcreteMid")
    m.box_center((2, 2, 2), at=(-8, 0, 3), mat="Kit_ConcreteDark")
    m.cyl(1.0, 6.0, at=(-4, 0, 0), seg=24, r_top=0.6, mat="Kit_SteelPale")
    m.revolve([(0.0, 0), (1.2, 0.2), (1.2, 3.0), (0.8, 3.6), (0.0, 3.9)], at=(0, 0, 0),
              seg=24, mat="Kit_SteelBlue")
    m.sphere(1.6, at=(4, 0, 3.0), z_lo=-0.75, z_hi=1.0, mat="Kit_SteelPale")
    m.poly_extrude([(-1, -1), (1, -1), (1, 1), (-1, 1)], 0.0, 0.5, at=(8, 0, 0),
                   mat="Kit_Rust")
    # 工业件
    m.extrude_along([(-0.2, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2)],
                    (12, -2, 0), (12, 2, 4), mat="Kit_SteelBlue")
    m.ibeam((14, -2, 0), (14, 2, 0), mat="Kit_SteelBlue")
    m.member((16, -2, 0), (16, 2, 3), 0.2, 0.12, mat="Kit_Rust")
    m.tube([(18, -2, 1), (18, 0, 1), (18, 0, 4), (18, 2, 4)], 0.16, seg=12,
           mat="Kit_SteelPale")
    m.pipe_member((20, -2, 0), (20, 2, 3), r=0.1, mat="Kit_Rust")
    m.ladder(at=(22, 0, 0), h=6.0, cage=True, mat="Kit_Rust")
    m.railing([(24, -2), (24, 2), (26, 2)], h=1.1, mat="Kit_Rust")
    m.platform((4, 4), 6.0, at=(-4, 6), mat="Kit_Rust", rail_sides="ns")
    m.stairs(at=(0, 6), h=3.0, w=1.0, run=3.0, mat="Kit_Rust")
    m.flange(0.6, at=(6, 6, 0), mat="Kit_Rust")
    m.grating((4, 2), at=(10, 6, 5.0), mat="Kit_Rust")
    m.tank_shell(3.0, 8.0, at=(16, 6, 0), seg=32, mat="Kit_SteelPale")
    return m


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    K.reset_scene()
    m = build()
    nv, nf, nt = m.counts()
    print("[selfcheck] raw verts=%d faces=%d tris=%d" % (nv, nf, nt), flush=True)
    obj = m.to_object("_selfcheck")
    K.stats([obj], "selfcheck", slot_limit=99)
    out = os.path.join(K.WORK_DIR, "selfcheck_kit.jpg")
    if "--render" in argv:
        K.preview([obj], out, cam=(46, -46, 26), target=(6, 2, 4), res=(900, 560),
                  samples=24)
        print("[selfcheck] image: %s (%d bytes)" % (out, os.path.getsize(out)), flush=True)
    print("[selfcheck] OK", flush=True)


if __name__ == "__main__":
    main()
