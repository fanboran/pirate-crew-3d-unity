# -*- coding: utf-8 -*-
"""解码 WaterLab 水面 PS 的 3x3 诊断格（`PC3D_WATERLAB_OCEAN_DEBUG=1`）。

出图链写 PNG 时做了显示编码（r48-dbg 实测为标准 sRGB 曲线）。直接拿 PNG 字节/255
当线性值会得到「幻影相机」：LabCamera 真值 (10.019, 26.265, 21.019) 被读成
(21.2, 43.2, 27.0)，1920x1080 被读成 2923x2249——第十八轮「相机常量异常」的根因。
本工具先按 sRGB EOTF 反解，再按 shader 内的编码式还原物理量。

用法：
  python tools/waterlab/debug_tiles.py <png> [<png> ...]
  python tools/waterlab/debug_tiles.py <png> --encoding srgb2   # 出图链做了两次显示编码时
  python tools/waterlab/debug_tiles.py <png> --encoding pow22   # 若出图改走 pow(1/2.2)
  python tools/waterlab/debug_tiles.py <png> --encoding raw     # 线性直写（未编码）

格序（shader 内定义）：行 = floor(screenUV.y*3)，行 0 = 画面下排；列 = floor(x*3)。
"""
import argparse
import sys

import numpy as np
from PIL import Image

TILE_SLOTS = [(c, r) for r in range(3) for c in range(3)]


def srgb_to_linear(u):
    u = np.asarray(u, dtype=np.float64)
    return np.where(u <= 0.04045, u / 12.92, ((u + 0.055) / 1.055) ** 2.4)


def pow22_to_linear(u):
    return np.power(np.clip(np.asarray(u, dtype=np.float64), 0.0, 1.0), 2.2)


def decode(med_rgb, encoding):
    u = med_rgb / 255.0
    if encoding == "raw":
        return u
    if encoding == "pow22":
        return pow22_to_linear(u)
    if encoding == "srgb2":
        # r58-dbg 实测：PNG 字节是 sRGB(sRGB(shader 值))——单次反解会再次得到
        # 「幻影相机 (32,48,14) / 2902x2210」，两次反解才回到 (31.8,34.2,0)/1920x1080。
        return srgb_to_linear(srgb_to_linear(u))
    return srgb_to_linear(u)


def sample_tile(im, col, row, half=3):
    """格中心 7x7 中位数；返回 (线性 rgb, 逐通道极差, 像素坐标)。

    shader 的 row 0 = 画面下排（screenUV.y 向上），PNG 的行 0 在顶部——这里要翻转。
    """
    h, w, _ = im.shape
    x = int((col + 0.5) / 3.0 * w)
    y = int((2 - row + 0.5) / 3.0 * h)
    patch = im[max(0, y - half):y + half + 1, max(0, x - half):x + half + 1]
    patch = patch.reshape(-1, 3)
    return np.median(patch, axis=0), patch.max(axis=0) - patch.min(axis=0), (x, y)


def analyze(path, encoding):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float64)
    h, w, _ = im.shape
    print(f"\n=== {path}  {w}x{h}  encoding={encoding} ===")

    vals = {}
    for col, row in TILE_SLOTS:
        med, spread, (x, y) = sample_tile(im, col, row)
        v = decode(med, encoding)
        vals[(col, row)] = v
        print(f"  tile({col},{row}) @({x:4d},{y:4d})  rgb_lin=("
              f"{v[0]:.4f},{v[1]:.4f},{v[2]:.4f})  格内极差≤{spread.max():.0f}")

    print("  ---- 物理解读 ----")
    d, dist, clip = vals[(0, 0)]
    depth_m, dist_m = d * 2.0, dist * 10.0
    ratio = dist_m / max(depth_m, 1e-6)
    print(f"  (0,0) 水深 depth={depth_m:.3f} m  光程 dist={dist_m:.3f} m  "
          f"clip={clip > 0.5}  dist/depth={ratio:.3f}")

    t = vals[(1, 0)]
    print(f"  (1,0) T=({t[0]:.4f},{t[1]:.4f},{t[2]:.4f})  "
          f"R:G:B=1:{t[1] / max(t[0], 1e-9):.2f}:{t[2] / max(t[0], 1e-9):.2f}")

    bg = vals[(2, 0)]
    print(f"  (2,0) bgColor(linear)=({bg[0]:.3f},{bg[1]:.3f},{bg[2]:.3f})")

    wx, wz, _ = vals[(0, 1)]
    print(f"  (0,1) world XZ=({wx * 64.0 - 32.0:+.2f},{wz * 64.0 - 32.0:+.2f})")

    cx, cz, cy = vals[(1, 1)]
    cam = ((cx - 0.5) * 64.0, cy * 64.0, (cz - 0.5) * 64.0)
    print(f"  (1,1) camera pos=({cam[0]:.3f}, {cam[1]:.3f}, {cam[2]:.3f})  [x,y,z]")

    sx, sy, se = vals[(2, 1)]
    print(f"  (2,1) _ScreenParams=({sx * 4096.0:.0f}, {sy * 4096.0:.0f})  "
          f"simEnabled={se > 0.5}")

    nx, nz, _ = vals[(0, 2)]
    print(f"  (0,2) normalWS.xz=({nx * 2.0 - 1.0:+.3f},{nz * 2.0 - 1.0:+.3f})")

    spec, shadow, soft = vals[(1, 2)]
    print(f"  (1,2) spec={spec * 4.0:.4f}  sunShadow={shadow:.3f}  shadowSoft={soft:.3f}")

    col = vals[(2, 2)]
    disp = [int(round(min(1.0, max(0.0, c)) ** (1.0 / 2.2) * 255.0)) for c in col]
    print(f"  (2,2) final col(linear)=({col[0]:.3f},{col[1]:.3f},{col[2]:.3f})  "
          f"display≈({disp[0]},{disp[1]},{disp[2]})")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("images", nargs="+")
    ap.add_argument("--encoding", choices=["srgb", "srgb2", "pow22", "raw"], default="srgb",
                    help="PNG 的显示编码（默认 srgb，出图链实测值）")
    args = ap.parse_args()
    for p in args.images:
        analyze(p, args.encoding)


if __name__ == "__main__":
    sys.exit(main())
