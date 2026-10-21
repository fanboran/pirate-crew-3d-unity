# -*- coding: utf-8 -*-
"""干沙定点探针：用相机几何 + 地形函数筛出「真干沙」像素（不受沙/水掩码混淆）。

`compare.py` 的沙掩码会把水下沙混进来，单点读数在 (196,206,149)…(249,222,197)
之间跳（审计 §9.3②）。这里按几何筛：射线命中地形且高度 > `--min-height`（默认
0.25 m，高于静水面），即真干沙；再统计颜色、色度与亮度。

参考受光干沙：`(252,226,167)`，色度 85（Steam 高清头图）。

用法：
  python tools/waterlab/sand_probe.py export/waterlab-debug/r54/battle-hud-2.png
"""
import argparse
import math

import numpy as np
from PIL import Image

CENTER = np.array([-11.0, -0.5, 0.0])
PITCH = math.radians(48.0)
YAW = math.radians(45.0)
DIST = 40.0
FOVY = math.radians(32.0)
DOMAIN = 64.0

OFFSHORE_DEPTH = -2.4
SHELF_START_X, SHELF_END_X = -26.0, 6.0
SHELF_END_HEIGHT = -0.15
FACE_START_X, FACE_END_X = 6.0, 14.0
BEACH_HEIGHT = 0.5
SAND_RIPPLE = 0.012


def smooth01(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def terrain(x, z):
    h = OFFSHORE_DEPTH
    h += (SHELF_END_HEIGHT - h) * smooth01((x - SHELF_START_X) / (SHELF_END_X - SHELF_START_X))
    h += (BEACH_HEIGHT - h) * smooth01((x - FACE_START_X) / (FACE_END_X - FACE_START_X))
    h += SAND_RIPPLE * (math.sin(x * 1.7 + z * 0.6) * 0.75
                        + math.sin(z * 2.3 - x * 0.4) * 0.25)
    return h


def chroma(rgb):
    return float(rgb.max() - rgb.min())


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("image")
    ap.add_argument("--min-height", type=float, default=0.25,
                    help="判定干沙的地形高度下限（m，静水面=0）")
    ap.add_argument("--step", type=int, default=4)
    args = ap.parse_args()

    im = np.asarray(Image.open(args.image).convert("RGB")).astype(np.float64)
    h, w, _ = im.shape
    dir_c = np.array([math.sin(PITCH) * math.sin(YAW), math.cos(PITCH),
                      math.sin(PITCH) * math.cos(YAW)])
    pos = CENTER + dir_c * DIST
    fwd = -dir_c
    right = np.cross([0, 1, 0], fwd)
    right /= np.linalg.norm(right)
    up = np.cross(fwd, right)
    half_v = math.tan(FOVY / 2)
    half_h = half_v * (w / h)

    samples = []
    for py in range(0, h, args.step):
        for px in range(0, w, args.step):
            nx = (px + 0.5) / w * 2 - 1
            ny = 1 - (py + 0.5) / h * 2
            d = fwd + right * (nx * half_h) + up * (ny * half_v)
            d /= np.linalg.norm(d)
            lo, hi = 0.1, 300.0

            def f(t):
                p = pos + d * t
                return p[1] - terrain(p[0], p[2])

            if f(lo) < 0:
                continue
            for _ in range(45):
                mid = 0.5 * (lo + hi)
                if f(mid) > 0:
                    lo = mid
                else:
                    hi = mid
            p = pos + d * ((lo + hi) * 0.5)
            if abs(p[0]) > DOMAIN / 2 or abs(p[2]) > DOMAIN / 2:
                continue
            if terrain(p[0], p[2]) < args.min_height:
                continue
            samples.append(im[py, px])

    if not samples:
        raise SystemExit("没有命中干沙像素——检查机位/阈值")

    a = np.array(samples)
    med = np.median(a, axis=0)
    mean = a.mean(axis=0)
    p10, p90 = np.percentile(a, [10, 90], axis=0)
    print(f"{args.image}  干沙像素 n={len(a)}  (地形高度 ≥ {args.min_height} m)")
    print(f"  中位=({med[0]:.0f},{med[1]:.0f},{med[2]:.0f})  色度={chroma(med):.0f}  Y={med.mean():.0f}")
    print(f"  均值=({mean[0]:.0f},{mean[1]:.0f},{mean[2]:.0f})  色度={chroma(mean):.0f}  Y={mean.mean():.0f}")
    print(f"  p10=({p10[0]:.0f},{p10[1]:.0f},{p10[2]:.0f})  p90=({p90[0]:.0f},{p90[1]:.0f},{p90[2]:.0f})")
    print(f"  参考受光干沙=(252,226,167)  色度=85  Y=227")


if __name__ == "__main__":
    main()
