# -*- coding: utf-8 -*-
"""逐像素光线投射：把 WaterLab 成片每个采样点的世界坐标/水深算出来，
再用着色器同式模型预测颜色，与实测颜色对拍。

相机参数与地形公式从装配链抄死：
- `WaterLabSetup.CreateCamera`：center=(-11,-0.5,0)、俯角 48°（距天顶）、方位 45°、
  dist=40、fovY=32°。
- `WaterLabRules.TerrainHeight`：OffshoreDepth=-2.4、Shelf -26→6、Face 6→14、Beach 0.5。
- 水体吸收：材质 tint 域 (0.6468,0.2241,0.0404) × 8.65617 = 有效 (5.60,1.94,0.35)/m。

成片 PNG 是显示编码（标准 sRGB），本脚本按 sRGB 读入/输出，与参考图同口径。
"""
import math

import numpy as np
from PIL import Image

# ---- 相机（WaterLabSetup.CreateCamera） ----
CENTER = np.array([-11.0, -0.5, 0.0])
PITCH = math.radians(48.0)   # 距天顶
YAW = math.radians(45.0)
DIST = 40.0
FOVY = math.radians(32.0)
DOMAIN = 64.0

# ---- 水体（WaterLab_Ocean.mat） ----
SIGMA = np.array([0.6468, 0.2241, 0.0404]) * 8.65617   # 有效每米
WATER_TINT = np.array([0.9603, 0.9858, 0.9995])
REFRACT = 0.0121

# ---- 地形（WaterLabRules.TerrainHeight） ----
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


def srgb2lin(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin2srgb(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.0031308, c * 12.92,
                    1.055 * np.clip(c, 0, None) ** (1 / 2.4) - 0.055)


# ---- 相机基 ----
dir_c = np.array([math.sin(PITCH) * math.sin(YAW), math.cos(PITCH),
                  math.sin(PITCH) * math.cos(YAW)])
POS = CENTER + dir_c * DIST
FWD = -dir_c
RIGHT = np.cross([0, 1, 0], FWD)
RIGHT /= np.linalg.norm(RIGHT)
UP = np.cross(FWD, RIGHT)


def ray_for(px, py, w, h):
    half_v = math.tan(FOVY / 2)
    half_h = half_v * (w / h)
    nx = (px + 0.5) / w * 2 - 1
    ny = 1 - (py + 0.5) / h * 2
    d = FWD + RIGHT * (nx * half_h) + UP * (ny * half_v)
    return d / np.linalg.norm(d)


def hit_terrain(d):
    """沿 d 从 POS 步进找地形交点，返回 (t, x, y, z) 或 None。"""
    lo, hi = 0.1, 300.0

    def f(t):
        p = POS + d * t
        return p[1] - terrain(p[0], p[2])

    if f(lo) < 0:
        return None
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if f(mid) > 0:
            lo = mid
        else:
            hi = mid
    t = 0.5 * (lo + hi)
    p = POS + d * t
    return t, p[0], p[1], p[2]


def analyze(img_path):
    px = np.asarray(Image.open(img_path).convert("RGB"), dtype=np.float64) / 255.0
    h, w, _ = px.shape
    print(f"\n=== {img_path}  {w}x{h} ===")
    print(f"  相机位={np.round(POS, 2)} 前向={np.round(FWD, 3)} "
          f"右={np.round(RIGHT, 3)} 上={np.round(UP, 3)}")

    hit = hit_terrain(FWD)
    if hit:
        t, x, y, z = hit
        print(f"  画面中心射线: t={t:.1f} 世界=({x:+.2f},{y:+.2f},{z:+.2f}) "
              f"地形={terrain(x, z):+.3f} 水深={-terrain(x, z):+.3f}")

    print("\n  逐行（12 行）: 域内占比 / 命中点x范围 / 解析水深 / 实测色 / 模型预测色")
    for r in range(12):
        py = int((r + 0.5) * h / 12)
        xs, inside = [], 0
        for c in range(24):
            pxc = int((c + 0.5) * w / 24)
            hh = hit_terrain(ray_for(pxc, py, w, h))
            if hh is None:
                continue
            _, x, _, z = hh
            if abs(x) < DOMAIN / 2 and abs(z) < DOMAIN / 2:
                inside += 1
                xs.append(x)

        dc = ray_for(w // 2, py, w, h)
        hc = hit_terrain(dc)
        if hc is None:
            continue
        _, xc, _, zc = hc
        depth = max(-terrain(xc, zc), 0.0)
        dist = depth / max(-dc[1], 0.25)
        trans = np.exp(-SIGMA * dist) * WATER_TINT
        # 底色按参考受光干沙近似；系数 0.9+0.2·NdotV⁶ 的中心视线近似取 1.1。
        bg = srgb2lin(np.array([252, 226, 167]) / 255.0) * 0.9
        pred = np.round(lin2srgb(bg * trans * 1.1) * 255).astype(int)
        meas = np.round(px[py, w // 2] * 255).astype(int)
        print(f"    y={py:4d} 域内{inside / 24:4.0%} "
              f"x∈[{min(xs) if xs else float('nan'):+.1f},"
              f"{max(xs) if xs else float('nan'):+.1f}] 中心x={xc:+.1f} "
              f"水深={depth:.2f}m 实测=({meas[0]:3d},{meas[1]:3d},{meas[2]:3d}) "
              f"模型=({pred[0]:3d},{pred[1]:3d},{pred[2]:3d})")


if __name__ == "__main__":
    import sys

    for p in sys.argv[1:]:
        analyze(p)
