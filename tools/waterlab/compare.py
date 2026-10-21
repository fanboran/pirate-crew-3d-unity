# -*- coding: utf-8 -*-
"""Sandcastle 参考头图 vs 本仓 WaterLab 成片 —— 分区比色。

用法: python compare.py [我们的图...]
输出: 参考图分区均值 / 我们的图分区均值 / 参考近岸->离岸颜色渐变曲线。
所有输出为 sRGB 0-255，便于直接对照。
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

REF = Path(r"E:\Windows_steam\appcache\librarycache\4894830\441b340643660c543c77b25ec188e0dc8eb42b6e\library_hero.jpg")


def rgb_of(img):
    return np.asarray(img.convert("RGB"), dtype=np.float32)


def stats(px, mask, name):
    if mask.sum() < 50:
        print(f"  {name:<22} (样本不足)")
        return
    sel = px[mask]
    m = sel.mean(axis=0)
    mx = sel.max(axis=1)          # 逐像素
    mn = sel.min(axis=1)
    chroma = (mx - mn).mean()
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0).mean()
    Y = (0.2126 * sel[:, 0] + 0.7152 * sel[:, 1] + 0.0722 * sel[:, 2])
    print(f"  {name:<22} mean=({m[0]:6.1f},{m[1]:6.1f},{m[2]:6.1f})  B-R={m[2]-m[0]:+6.1f}  "
          f"chroma={chroma:5.1f}  sat={sat:4.2f}  Y={Y.mean():5.1f}  n={mask.sum()}")


def report_ref():
    px = rgb_of(Image.open(REF))
    H, W, _ = px.shape
    print(f"\n=== 参考头图 {W}x{H} ===")
    R, G, B = px[..., 0], px[..., 1], px[..., 2]
    Y = 0.2126 * R + 0.7152 * G + 0.0722 * B

    # 沙：暖色且亮；水：蓝多
    sand = (R - B > 35) & (R > 170)
    water = (B - R > 8)
    print(f"  区域占比: 沙 {sand.mean():.1%}  水 {water.mean():.1%}")

    stats(px, sand, "干沙(全部暖色区)")
    # 水按蓝红差分层（越大越深/越蓝）
    d = B - R
    stats(px, water & (d < 30), "水: 最浅 (B-R<30)")
    stats(px, water & (d >= 30) & (d < 70), "水: 中 (30-70)")
    stats(px, water & (d >= 70) & (d < 110), "水: 深 (70-110)")
    stats(px, water & (d >= 110), "水: 最深 (>110)")

    # 从上(离岸)到下(岸线) 的每 40 行条带均值 —— 只有水像素
    print("\n  --- 参考 行条带（仅水像素）离岸->岸线 颜色渐变 ---")
    for y0 in range(0, H, 60):
        band = water[y0:y0 + 60]
        if band.sum() < 200:
            continue
        sub = px[y0:y0 + 60][band]
        m = sub.mean(axis=0)
        print(f"    y={y0:4d}-{y0+60:4d}  ({m[0]:6.1f},{m[1]:6.1f},{m[2]:6.1f})  B-R={m[2]-m[0]:6.1f}")

    # 定点探针
    probes = {
        "远海(深)": (960, 30), "远海(深)2": (300, 40),
        "中水": (960, 150), "中水2": (1200, 170),
        "浅水(绿松石)": (1150, 300), "浅水2": (700, 330),
        "极浅(近岸)": (700, 400),
        "干沙(左下)": (150, 520), "干沙(右下)": (1750, 480),
        "湿沙带": (500, 430),
    }
    print("\n  --- 定点探针 (7x7 均值) ---")
    for name, (x, y) in probes.items():
        patch = px[max(0, y - 3):y + 4, max(0, x - 3):x + 4]
        m = patch.reshape(-1, 3).mean(axis=0)
        print(f"    {name:<14} @({x:4d},{y:3d})  ({m[0]:6.1f},{m[1]:6.1f},{m[2]:6.1f})")


def report_ours(path):
    px = rgb_of(Image.open(path))
    H, W, _ = px.shape
    print(f"\n=== 我们的成片 {path.name} {W}x{H} ===")
    R, G, B = px[..., 0], px[..., 1], px[..., 2]
    d = B - R
    water = (d > 4) | ((G - R > 4) & (B > 120))     # 水/青绿
    sand = (R - B > 30) & (R > 170)
    print(f"  区域占比: 沙 {sand.mean():.1%}  水 {water.mean():.1%}")
    stats(px, sand, "沙(暖色区)")
    stats(px, water, "水(全部)")
    # 近白统计：判「水是否成了白雾」
    nearwhite = (R > 225) & (G > 225) & (B > 225)
    print(f"  近白像素(RGB>225)占比: {nearwhite.mean():.1%}")
    stats(px, water & nearwhite, "水中的近白(雾)")
    # 局部梯度（细节可见度）
    gy = np.abs(np.diff(px.mean(axis=2), axis=0)).mean()
    gx = np.abs(np.diff(px.mean(axis=2), axis=1)).mean()
    print(f"  亮度局部梯度: gx={gx:.2f} gy={gy:.2f}")
    # 亮度直方图摘要
    Y = 0.2126 * R + 0.7152 * G + 0.0722 * B
    print(f"  亮度分位: p5={np.percentile(Y,5):.0f} p50={np.percentile(Y,50):.0f} "
          f"p95={np.percentile(Y,95):.0f} p99={np.percentile(Y,99):.0f}")
    # 色度摘要
    mx = px.max(axis=2); mn = px.min(axis=2)
    ch = mx - mn
    print(f"  色度分位: p50={np.percentile(ch,50):.0f} p90={np.percentile(ch,90):.0f} "
          f"p99={np.percentile(ch,99):.0f}")


if __name__ == "__main__":
    report_ref()
    for a in sys.argv[1:]:
        report_ours(Path(a))
