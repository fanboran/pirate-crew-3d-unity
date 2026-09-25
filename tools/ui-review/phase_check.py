#!/usr/bin/env python3
"""像素相位复核（Aseprite 观感对齐：判定 UI 元素是否落在整数画布格上）。

【为什么需要】画布 = 屏 / 2（恒定像素密度栈，见 UiSkin.Px 口径）。1 画布像素 = 2 屏幕
像素，所以**整数画布格上的线条，屏幕坐标必为偶数**；起点落在奇数 = 那条线停在半画布
格上——`PixelSnapText`（文字侧）与「中心锚奇尺寸偶数化」（面板件侧）两条纪律治的都是
这件事。本脚本把「看着有点糊 / 对不齐」变成可复算的奇偶判定。

【判据】沿扫描线切成等值游程：
  · 长度 2 的游程 = 1 画布像素的线/描边 → 看它的**起点**奇偶：偶 ✓ / 奇 ✗
  · 长度 1 或 ≥3 = 内容（文字笔画、面）——相位由同线的 2 长游程体现，不单独判定
  · 相邻两格被摊薄成同值（如 `40 40` 而本该 `68 68`）说明线跨了格，属真糊

【用法】
  # 单图：横切一行 / 竖切一列（窗口务必收窄到目标元素，否则背景件与文字会一起进来）
  python phase_check.py shot.png row 700 500 1500 --thr 20
  python phase_check.py shot.png col 700 150 950

  # 双图对照（改前 / 改后）：同一扫描线各打一份，看 ✗ 是否转 ✓
  python phase_check.py new.png row 350 500 1400 --thr 20 --pair old.png

参数：<图> <row|col> <扫描线坐标> <起> <止> [--thr N] [--pair 对照图]
  row = 固定 y、沿 x 扫；col = 固定 x、沿 y 扫。
"""
import argparse
import os

from PIL import Image


def runs(img, kind, index, lo, hi):
    """扫描线上的等值游程：[(起点, 终点, 值, 长度), ...]。"""
    px = img.load()
    get = (lambda i: px[i, index]) if kind == "row" else (lambda i: px[index, i])
    out = []
    start = lo
    prev = get(lo)
    for i in range(lo + 1, hi):
        v = get(i)
        if v != prev:
            out.append((start, i - 1, prev, i - start))
            start, prev = i, v
    out.append((start, hi - 1, prev, hi - start))
    return out


def report(path, kind, index, lo, hi, thr):
    label = f"{os.path.basename(path)} {kind}={index} 窗口[{lo},{hi}) thr={thr}"
    print(f"[{label}]")
    if not os.path.exists(path):
        print("    图不存在")
        return
    img = Image.open(path).convert("L")
    lines = []
    for s, e, v, n in runs(img, kind, index, lo, hi):
        if n == 2 and v > thr:
            lines.append((s, e, v, s % 2 == 0))
    if not lines:
        print("    窗口内没有长度 2 的亮线（窗口取偏了？或该元素不是 1 画布像素线）")
        return
    bad = [x for x in lines if not x[3]]
    for s, e, v, ok in lines:
        print(f"    线 [{s},{e}] 值={v} 起点{s} {'✓ 整格' if ok else '✗ 半格'}")
    print(f"    —— 2 长亮线 {len(lines)} 条，其中半格 {len(bad)} 条"
          + ("（全整格 ✓）" if not bad else "（✗ 需归整）"))
    return len(bad)


def main():
    ap = argparse.ArgumentParser(description="UI 像素相位复核（2 长游程起点奇偶 = 半画布格判定）")
    ap.add_argument("image")
    ap.add_argument("kind", choices=["row", "col"])
    ap.add_argument("index", type=int, help="row 给 y / col 给 x")
    ap.add_argument("lo", type=int)
    ap.add_argument("hi", type=int)
    ap.add_argument("--thr", type=int, default=12, help="亮度阈值（低于此值的游程不判定，默认 12）")
    ap.add_argument("--pair", help="对照图（改前），同扫描线各打一份")
    args = ap.parse_args()

    if args.pair:
        print("--- 对照（改前）---")
        report(args.pair, args.kind, args.index, args.lo, args.hi, args.thr)
        print("--- 现图（改后）---")
    report(args.image, args.kind, args.index, args.lo, args.hi, args.thr)


if __name__ == "__main__":
    main()
