#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""judge_pixelart_pilot.py —— 像素化着色路径（v3 蓝本重写线）出图的程序化判据。

用法
----
    python tools/pixel-review/judge_pixelart_pilot.py <图片目录或单张 png>
    python tools/pixel-review/judge_pixelart_pilot.py export/pixelart-p1

依赖：numpy + Pillow。

背景（为什么是这四项）
----------------------
这条路径的形状是「物体 pass 把几何直接渲进低分辨率 G-buffer → 低分辨率域着色 → 点采样上屏」，
所以它的**正确性**几乎全是可测的几何/统计量，不需要"看着像"：

1. **块边长** = 屏幕宽 ÷ 低分辨率 RT 宽。这是"像素化到底有没有生效、RT 多大"的直接读数。
   本判据不从图上猜 RT 尺寸，而是先从像素跳变位置反推块边长，再报 `RT 宽 = 屏宽 ÷ 块边长`。
   - 默认档（RT 高 180、16:9）→ 320×180，1920 宽屏幕 → 块 6
   - 期望值表见 docs/技术/渲染/像素化着色路径.md §5
2. **跳变率** = 相邻低分辨率像素颜色不同的比例。**无抖动**的纯色带画面应接近 0
   （只有真实几何边缘才跳），这是"色带是平的、没有杂色"的判据。
3. **平坦占比** = 四邻皆同色的低分辨率像素比例。与跳变率互补，1 - 平坦占比 ≈ 有结构的像素。
   无抖动时应 > 0.9。
4. **低分辨率色数**：色带档位 × 材质数 + 少量边缘色。数量级检查用（异常大→出现渐变/抗锯齿污染，
   异常小→全屏一个色，通常意味着某条 pass 没生效）。
5. **亮暗跨度**（**这一条是"有没有光影"的回归判据**）：低分辨率颜色表里（只取占比 ≥0.5% 的
   大色块、并剔除近黑的墨线），<c>(最亮 luma − 最暗 luma) / 最亮 luma</c>。
   有光照时同一材质必然同时出现亮档（顶面）与暗档（背光面），跨度大；**光照被整屏旁路时
   画面只剩各材质的 albedo 原色，跨度塌到 0.45 上下**。
   这条判据是因为真出过事故才加的：`prop.a` 与 `_AAScale` 撞通道，
   每个不透明像素都被当成"墨线像素"原样输出 albedo，症状是"所有面同色、没有任何光影、
   且没有任何报错"——旧的四项判据全过（块边长/色数/平坦度都正常），只有这一条能抓住。
   两轮实测：**正常 0.78 / 事故 0.45**，阈值取 0.60。

抖动对照怎么读
--------------
判据本身不判断"哪种抖动好看"，但能把两范式的**性质**量出来，这正是裁决要的依据：
- **Bayer 4×4 有序抖动**（16 级渐变态）：只有色带阈值附近的像素翻档 → 跳变率**明显低于** 0.5
  （实测幅度 0.5 时 ≈ 0.25）。
- **v3 的 1-bit 密度图案**（两态）：图案是 0/1，落在阈值带内的像素**一半往上翻、一半往下翻**，
  于是相邻像素几乎必不同 → 跳变率**接近 0.9~1.0**（实测幅度 0.5 时 ≈ 0.91，1.0 时 ≈ 1.00）。
  这就是"撕边"而不是"渐变态"的量化含义。

退出码：全部核心判据通过 = 0，有 FAIL = 1。
"""

import os
import re
import sys

import numpy as np
from PIL import Image

# 文件名里带 rt<N> 的样本说明那张的 RT 高是 N（判据按此推算期望块边长）。
RT_HEIGHT_PATTERN = re.compile(r"rt(\d+)")

# 【RT 高的单一来源】不在这里写死：装配与出图两边的 RT 档来自
# Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartPilotScene.cs 的 RenderHeight。
# 曾经"场景 35.264° / 出图脚本 30°"各写一份，比对结论全错——同一个坑不再踩第二次。
SCENE_CONSTANTS_CS = os.path.join(
    "pirate-crew", "Assets", "Scripts", "PirateCrew", "Rendering", "Pixelart", "PixelartPilotScene.cs")
PIXEL_SCALE_PATTERN = re.compile(r"PixelScale\s*=\s*(\d+)")
FALLBACK_PIXEL_SCALE = 5
SCREEN_WIDTH = 1920

# 无抖动样本的期望区间（见模块 docstring 的判据二/三）
FLAT_MIN = 0.90          # 平坦占比下限（无抖动）
JUMP_MAX_NO_DITHER = 0.10   # 跳变率上限（无抖动）
DITHER_SPLIT = 0.5       # 跳变率分界线：低于它 = 渐变态，高于它 = 两态撕边

# 判据五：亮暗跨度
LIGHTING_MIN_SPAN = 0.60    # 跨度下限（正常 0.78 / 光照被旁路 0.45）
LIGHTING_MIN_SHARE = 0.005  # 参与比较的颜色至少占低分辨率域的 0.5%（滤掉零星边缘色）
LIGHTING_MIN_LUMA = 25.0    # 剔除近黑的墨线（墨线是"画上去的线"，不参与光照统计）


def read_pixel_scale():
    """从场景常量读像素档位（= 一个艺术像素占几个屏幕像素）。"""
    try:
        with open(SCENE_CONSTANTS_CS, "r", encoding="utf-8") as f:
            text = f.read()
    except OSError:
        return FALLBACK_PIXEL_SCALE, "（读不到 " + SCENE_CONSTANTS_CS + "，用兜底值）"
    match = PIXEL_SCALE_PATTERN.search(text)
    if not match:
        return FALLBACK_PIXEL_SCALE, "（" + SCENE_CONSTANTS_CS + " 里没解析到 PixelScale，用兜底值）"
    return int(match.group(1)), ""


def block_size(img):
    """
    从"同一行里连续同色像素的最短长度"反推块边长。

    【为什么不是"最小的 lag 使平移后相等"】那样在**大面积平涂**图上会误判成 2：
    色带平坦区里任意 lag 都相等，只有少数边缘像素不等，于是小 lag 也轻松过阈值。
    块内同色 ⇒ 任何一次颜色变化都发生在块边界上 ⇒ 行长必然是块边长的整数倍，
    因此"有边缘的行里最短的那一段"就是块边长（本路径的画面正是大面积平涂）。

    【为什么只扫到 88% 高】开发版播放器右下角有一行 "Development Build" 文字，
    它是**抗锯齿**的（逐像素变化）——扫到它就会得到 1 像素的行长。
    判据只看画面主体，故排除底部这条带。
    """
    a = np.asarray(img)
    h, w, _ = a.shape
    scan_bottom = max(4, int(h * 0.88))
    shortest = w
    for y in range(2, scan_bottom, 3):
        row = a[y]
        changes = np.where((np.diff(row, axis=0) != 0).any(axis=1))[0]
        if len(changes) == 0:
            continue    # 整行同色：这一行没有块边界信息
        boundaries = np.concatenate(([0], changes + 1, [w]))
        shortest = min(shortest, int(np.diff(boundaries).min()))
    return shortest if 1 < shortest < w else 0


def expected_block_size(image_width, image_height, rt_height):
    """按装配口径算期望块边长：低分辨率宽 = 偶数对齐的高×宽高比，块 = 屏宽 ÷ 低分辨率宽。"""
    aspect = image_width / image_height
    rt_width = max(2, int(round(rt_height * aspect / 2.0)) * 2)
    return max(1, int(round(image_width / rt_width)))


def low_res_view(a, b):
    """
    把屏幕图抽成低分辨率域（起点对齐到块边界，避免采到块与块的接缝）。
    与块检测同样排除底部那条带（开发版水印是抗锯齿文字，会污染色数与跳变率）。
    """
    h, w, _ = a.shape
    scan_bottom = (int(h * 0.88) // b) * b
    nx = (w // b) * b
    return a[0:scan_bottom, 0:nx].reshape(scan_bottom // b, b, nx // b, b, 3)[:, 0, :, 0]


def luma(c):
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def lighting_span(lr):
    """
    亮暗跨度：(最亮 luma − 最暗 luma) / 最亮 luma，只统计占比 ≥0.5% 的大色块、
    并剔除近黑的墨线。光照被整屏旁路时画面只剩各材质 albedo 原色 ⇒ 跨度塌到 0.45 上下。
    """
    flat = lr.reshape(-1, 3)
    colors, counts = np.unique(flat, axis=0, return_counts=True)
    share = counts / float(counts.sum())
    keep = colors[share >= LIGHTING_MIN_SHARE]
    lumas = [luma(c) for c in keep]
    lumas = [v for v in lumas if v >= LIGHTING_MIN_LUMA]
    if len(lumas) < 2:
        return 0.0
    hi, lo = max(lumas), min(lumas)
    return (hi - lo) / hi if hi > 0 else 0.0


def judge_one(path, pixel_scale):
    img = Image.open(path).convert("RGB")
    a = np.asarray(img).astype(int)
    b = block_size(img)

    name = os.path.basename(path)
    expected_block = pixel_scale    # 口径：锁"一个艺术像素占几个屏幕像素"

    if b < 2:
        return name, None, ["FAIL 块边长检测失败（画面没有被像素化，或整图同色）"]

    lr = low_res_view(a, b)
    rt_width = a.shape[1] // b
    colors = len(np.unique(lr.reshape(-1, 3), axis=0))
    jump = float((lr[:-1, :-1] != lr[:-1, 1:]).any(axis=2).mean())
    flat = float(((lr[:-1, :-1] == lr[:-1, 1:])
                  & (lr[:-1, :-1] == lr[1:, :-1])
                  & (lr[:-1, :-1] == lr[1:, 1:])).mean())
    span = lighting_span(lr)

    notes = []
    # 调试档（dbg-*）是**中间缓冲**的直接视图：albedo/参数缓冲按设计就是平涂、法线缓冲按设计满是跳变，
    # 所以除块边长外的四项判据在它们身上不成立，只判块边长（否则会把"设计如此"误报成故障）。
    is_debug_view = name.startswith("dbg-")
    if expected_block > 1 and b != expected_block:
        notes.append("FAIL 块边长 %d ≠ 期望 %d（像素档位，见 PixelartPilotScene.PixelScale）"
                     % (b, expected_block))
    if colors < 2:
        notes.append("FAIL 低分辨率域只有一个颜色（某条 pass 没生效）")
    if is_debug_view:
        stats = {"block": b, "rt_width": rt_width, "colors": colors,
                 "jump": jump, "flat": flat, "span": span}
        return name, stats, notes
    if span < LIGHTING_MIN_SPAN:
        notes.append("FAIL 亮暗跨度 %.2f < %.2f（画面只剩各材质 albedo 原色 ⇒ "
                     "光照/色带很可能被整屏旁路，检查 prop.a 之类通道语义是否冲突）"
                     % (span, LIGHTING_MIN_SPAN))

    # 抖动档由文件名标注：含 bayer/density/dither 的样本按"抖动应明显改变跳变率"判，
    # 其余按无抖动区间判。密度图案（两态）期望跳变率 > 0.5，Bayer（渐变态）期望 < 0.5。
    if "dither" in name or "bayer" in name or "density" in name:
        if "density" in name:
            if jump <= DITHER_SPLIT:
                notes.append("FAIL 密度图案档跳变率 %.2f 未过 0.5（1-bit 两态应接近 1.0）" % jump)
            else:
                notes.append("OK   跳变率 %.2f = 两态撕边（1-bit 密度图案）" % jump)
        elif "bayer" in name:
            if jump >= DITHER_SPLIT:
                notes.append("FAIL Bayer 档跳变率 %.2f 过了 0.5（有序抖动应是渐变态）" % jump)
            else:
                notes.append("OK   跳变率 %.2f = 渐变态抖动（Bayer 4×4）" % jump)
    else:
        if flat < FLAT_MIN:
            notes.append("FAIL 平坦占比 %.3f < %.2f（无抖动档色带应是平的，出现杂色/渐变）"
                         % (flat, FLAT_MIN))
        if jump > JUMP_MAX_NO_DITHER:
            notes.append("FAIL 跳变率 %.3f > %.2f（无抖动档相邻像素不应大量不同）"
                         % (jump, JUMP_MAX_NO_DITHER))

    stats = {"block": b, "rt_width": rt_width, "colors": colors,
             "jump": jump, "flat": flat, "span": span}
    return name, stats, notes


def collect(target):
    if os.path.isfile(target):
        return [target]
    out = []
    for root, _dirs, files in os.walk(target):
        for f in sorted(files):
            if f.lower().endswith((".png", ".jpg", ".jpeg")):
                out.append(os.path.join(root, f))
    return out


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    files = collect(argv[1])
    if not files:
        print("没有找到图片：" + argv[1])
        return 2

    scale, scale_note = read_pixel_scale()
    print("像素档位（场景常量）：%d× %s" % (scale, scale_note))

    failures = 0
    print("%-34s %5s %6s %7s %7s %8s %8s"
          % ("文件", "块边长", "RT宽", "色数", "跳变率", "平坦占比", "亮暗跨度"))
    for path in files:
        name, stats, notes = judge_one(path, scale)
        if stats is None:
            print("%-34s %s" % (name, notes[0]))
            failures += 1
            continue
        print("%-34s %5d %6d %7d %7.3f %8.3f %8.2f"
              % (name, stats["block"], stats["rt_width"], stats["colors"],
                 stats["jump"], stats["flat"], stats["span"]))
        for note in notes:
            print("    " + note)
            if note.startswith("FAIL"):
                failures += 1

    print("---")
    print("结论：" + ("全部核心判据通过" if failures == 0 else "%d 项 FAIL" % failures))
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
