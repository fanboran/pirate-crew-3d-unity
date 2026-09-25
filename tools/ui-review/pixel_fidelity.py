#!/usr/bin/env python3
"""像素保真探针：判定实拍图里哪些像素**不是平涂色**（即被抗锯齿/重采样糊过）。

判据（对每个像素）
------------------
取全图出现次数最多的一批颜色作为「权威平涂色板」（默认阈值：出现 ≥ 0.01% 的像素，
即一张 1920×1080 图里 ≥ 200 px 的颜色；平涂件、纯色行板、描边线都落在这批里）。
一个像素若**恰好等于**某个平涂色 → 干净；否则它在两色之间做了插值 → 「糊」。
对糊像素再反解其最可能的两个端点色与混合系数 t（沿 RGB 线段投影），
t ∈ (0.15, 0.85) 计为**中间灰阶**（真正的抗锯齿污点，纯位移不该产生）。

输出
----
1. 汇总：糊像素总数、中间灰阶数、占比；
2. 按连通块列出糊像素的包围盒与主要端点色（=> 直接点名是哪个控件糊了）；
3. --region 时只统计该窗口，并逐行打印扫描线 run-length（人工复核笔画粗细是否恒定）。

用法
----
  python tools/ui-review/pixel_fidelity.py <png> [--region x0,y0,x1,y1] [--min-blob 24]
"""
from __future__ import annotations

import argparse
import sys
from collections import Counter

from PIL import Image


def palette_of(im: Image.Image, min_frac: float = 1e-4) -> set[tuple[int, int, int]]:
    counts = Counter(im.getdata())
    floor = max(4, int(im.width * im.height * min_frac))
    return {c for c, n in counts.items() if n >= floor}


def pairs_of(pal):
    cands = sorted(pal)
    out = []
    for i, a in enumerate(cands):
        for b in cands[i + 1:]:
            out.append((a, b, (b[0] - a[0], b[1] - a[1], b[2] - a[2])))
    return out


def nearest_axis_mix(px, pairs):
    """把像素表示为色板上一条 RGB 线段的插值点，返回 (端点A, 端点B, t, 残差)。

    在色板里找与 px 构成最小残差的色对；残差是 px 到线段的垂距（0 = 正好在两色连线上）。
    """
    best = None
    for a, b, ab in pairs:
        if True:
            den = ab[0] ** 2 + ab[1] ** 2 + ab[2] ** 2
            if den == 0:
                continue
            t = ((px[0] - a[0]) * ab[0] + (px[1] - a[1]) * ab[1] + (px[2] - a[2]) * ab[2]) / den
            if t < -1e-6 or t > 1 + 1e-6:
                continue
            t = min(1.0, max(0.0, t))
            proj = (a[0] + t * ab[0], a[1] + t * ab[1], a[2] + t * ab[2])
            res = sum((px[k] - proj[k]) ** 2 for k in range(3)) ** 0.5
            if best is None or res < best[3]:
                best = (a, b, t, res)
    return best


def blobs(pts: set[tuple[int, int]]):
    """4 邻接连通块。"""
    seen = set()
    out = []
    for p in pts:
        if p in seen:
            continue
        stack = [p]
        seen.add(p)
        comp = []
        while stack:
            x, y = stack.pop()
            comp.append((x, y))
            for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if q in pts and q not in seen:
                    seen.add(q)
                    stack.append(q)
        out.append(comp)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("png")
    ap.add_argument("--region", help="x0,y0,x1,y1")
    ap.add_argument("--min-blob", type=int, default=16)
    ap.add_argument("--top", type=int, default=12)
    ap.add_argument("--scanlines", action="store_true", help="打印扫描线 RLE")
    args = ap.parse_args()

    im = Image.open(args.png).convert("RGB")
    pal = palette_of(im)
    pairs = pairs_of(pal)
    box = (0, 0, im.width, im.height)
    if args.region:
        box = tuple(int(v) for v in args.region.split(","))
    x0, y0, x1, y1 = box

    px = im.load()
    dirty: dict[tuple[int, int], tuple] = {}
    for y in range(y0, y1):
        for x in range(x0, x1):
            c = px[x, y]
            if c in pal:
                continue
            m = nearest_axis_mix(c, pairs)
            if m is None:
                continue
            dirty[(x, y)] = (c, m[0], m[1], m[2], m[3])

    total = (x1 - x0) * (y1 - y0)
    mid = {p: v for p, v in dirty.items() if 0.15 < v[3] < 0.85}

    print(f"== {args.png}  区域 {box}  像素 {total}")
    print(f"平涂色板 {len(pal)} 色")
    print(f"非平涂像素 {len(dirty)} ({len(dirty) / total:.4%})  其中中间灰阶 {len(mid)} ({len(mid) / total:.4%})")

    if mid:
        comps = sorted(blobs(set(mid)), key=len, reverse=True)
        print(f"中间灰阶连通块 {len(comps)} 个，最大 {len(comps[0])} px：")
        for comp in comps[: args.top]:
            xs = [p[0] for p in comp]
            ys = [p[1] for p in comp]
            palette_used = Counter((v[1], v[2]) for p, v in mid.items() if p in set(comp))
            (a, b), _ = palette_used.most_common(1)[0]
            print(f"  {len(comp):6d} px  bbox=({min(xs)},{min(ys)})-({max(xs)},{max(ys)})"
                  f"  端点 {a}↔{b}")
    else:
        print("中间灰阶 0 个 —— 该区域已彻底二值化")

    if args.scanlines:
        for y in range(y0, y1):
            runs = []
            prev = None
            start = x0
            for x in range(x0, x1):
                c = px[x, y]
                if c != prev:
                    if prev is not None:
                        runs.append((x - start, prev))
                    prev = c
                    start = x
            runs.append((x1 - start, prev))
            print(f"y={y:4d} " + " ".join(f"{n}×{c}" for n, c in runs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
