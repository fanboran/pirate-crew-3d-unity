# -*- coding: utf-8 -*-
"""把本仓成片按「逐行水色剖面」与 Sandcastle 参考头图并排比色。

参考（library_hero.jpg）的水色带（离岸→岸线，仅水像素）：
  ( 16, 70,119) ( 19, 83,126) ( 28,103,136) ( 46,126,146)
  ( 74,153,161) (113,177,167) (146,195,173) (157,200,171)
  B-R: +103 +107 +107 +100 +86 +54 +27 +14
参考干沙：(252,226,167) 色度 85   Y=227
参考浅水块局部梯度中位 5.07；浅水内极亮像素(lum>235) 0.03%

注：`lum>235` 是**绝对阈值，随整体曝光漂移**，只适合同曝光 A/B；碎金密度看
「局部高差(水,9x9 中位)」——r54/r55 实测 >60 仅 0.12%，参考 0.45%（绝对近白反而
1.12%）。第十八轮把它当碎金指标，是被整体亮度抬升误导。

用法: python judge_vs_ref.py <本仓png> [...]
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

REF_BANDS = [(16, 70, 119), (19, 83, 126), (28, 103, 136), (46, 126, 146),
             (74, 153, 161), (113, 177, 167), (146, 195, 173), (157, 200, 171)]


def lum(px):
    return 0.2126 * px[..., 0] + 0.7152 * px[..., 1] + 0.0722 * px[..., 2]


def report(path):
    px = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64)
    H, W, _ = px.shape
    R, G, B = px[..., 0], px[..., 1], px[..., 2]
    ch = px.max(axis=2) - px.min(axis=2)
    L = lum(px)
    print(f"\n=== {path}  {W}x{H} ===")

    water = (B - R > 4) | ((G - R > 4) & (B > 110))
    sand = (R - B > 30) & (R > 170)
    print(f"  区域占比 水 {water.mean():.1%}  沙 {sand.mean():.1%}")

    # 沙
    if sand.sum() > 200:
        s = px[sand]
        sm = s.mean(axis=0)
        sc = (s.max(axis=1) - s.min(axis=1)).mean()
        print(f"  沙    mean=({sm[0]:5.0f},{sm[1]:5.0f},{sm[2]:5.0f})  色度={sc:5.1f}  Y={lum(s).mean():5.1f}")
        print(f"  参考  mean=(  252,  226,  167)  色度= 85.0  Y=227.0")
    else:
        print("  沙：样本不足（画面里干沙太少）")

    # 水：按 B-R 分带
    if water.sum() > 200:
        d = B - R
        edges = [(-999, 15), (15, 45), (45, 75), (75, 105), (105, 999)]
        print("  水按蓝红差分带：")
        for lo, hi in edges:
            m = water & (d >= lo) & (d < hi)
            if m.sum() < 200:
                continue
            s = px[m]
            sm = s.mean(axis=0)
            sc = (s.max(axis=1) - s.min(axis=1)).mean()
            print(f"    B-R [{lo:4d},{hi:4d})  mean=({sm[0]:5.0f},{sm[1]:5.0f},{sm[2]:5.0f})"
                  f"  色度={sc:5.1f}  Y={lum(s).mean():5.1f}  n={m.sum()}")
        w = px[water]
        wc = (w.max(axis=1) - w.min(axis=1)).mean()
        print(f"  水整体  B-R={w[:,2].mean()-w[:,0].mean():+.1f}  色度={wc:5.1f}  Y={lum(w).mean():5.1f}")
        print(f"  参考浅水 色度 36.9 / 深水 125.5；B-R 从 +19 到 +126")

    # 近白 / 亮度 / 梯度
    nw = (R > 225) & (G > 225) & (B > 225)
    print(f"  近白(RGB>225, 曝光相关) {nw.mean():.2%}   亮度 p5={np.percentile(L,5):5.0f} p50={np.percentile(L,50):5.0f} p95={np.percentile(L,95):5.0f}")
    # 曝光无关的碎金密度：亮度减去 9x9 局部中位，只在水掩码内统计
    base = np.asarray(
        Image.fromarray(np.clip(L, 0, 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(9))
    ).astype(np.float64)
    d = (L - base)[water]
    if d.size:
        print(f"  局部高差(水,9x9 中位) >20 {(d > 20).mean():.2%}  >40 {(d > 40).mean():.2%}"
              f"  >60 {(d > 60).mean():.2%}  p99={np.percentile(d, 99):.1f}"
              f"  p99.9={np.percentile(d, 99.9):.1f}")
    gx = np.abs(np.diff(L, axis=1)).mean()
    gy = np.abs(np.diff(L, axis=0)).mean()
    print(f"  局部梯度 gx={gx:.2f} gy={gy:.2f}   （参考浅水块 5.07）")

    # 逐行剖面（画面下→上 = 近→远）
    print("  逐行水色剖面（画面下→上，仅水像素）：")
    for r in range(9, -1, -1):
        y0, y1 = int(r * H / 10), int((r + 1) * H / 10)
        m = water[y0:y1]
        if m.sum() < 300:
            continue
        s = px[y0:y1][m]
        sm = s.mean(axis=0)
        sc = (s.max(axis=1) - s.min(axis=1)).mean()
        print(f"    y{r} [{y0:4d},{y1:4d}]  ({sm[0]:5.0f},{sm[1]:5.0f},{sm[2]:5.0f})"
              f"  B-R={sm[2]-sm[0]:+6.1f}  色度={sc:5.1f}  Y={lum(s).mean():5.1f}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        report(p)
