"""一次性诊断：墨线（反向壳）在剪影哪个方向缺环。

判定口径（决定性）：对内容掩码的**每一个 4 邻域边界像素**，检查它**外侧的 8 邻域**里
有没有墨线像素。没有 ⇒ 该边界像素"外侧无墨线"= 缺环。把缺的位置画成洋红叠加图。

用法（仓库根）：
    python tools/pixel-review/ink_gap_probe.py [图路径]
输出：
  1. 深色像素颜色直方图（分离「墨线色」与「无光暗面色」）
  2. 轮廓外侧墨线覆盖率，按八方向分解
  3. 洋红叠加图 + 疑点区域放大裁图 → export/ink-gap/
"""

import os
import sys
from collections import Counter

import numpy as np
from PIL import Image

BG = np.array([74, 110, 134])       # 4A6E86 背景
OUT_DIR = os.path.join("export", "ink-gap")

NEIGH = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]
DIRS = {
    "上": (-1, 0), "下": (1, 0), "左": (0, -1), "右": (0, 1),
    "左上": (-1, -1), "右上": (-1, 1), "左下": (1, -1), "右下": (1, 1),
}


def at(mask, dy, dx):
    """out[p] = mask[p + (dy, dx)]；越界补 False。"""
    out = np.roll(mask, (-dy, -dx), axis=(0, 1))
    h, w = mask.shape
    if dy > 0:
        out[h - dy:] = False
    elif dy < 0:
        out[: -dy] = False
    if dx > 0:
        out[:, w - dx:] = False
    elif dx < 0:
        out[:, : -dx] = False
    return out


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "docs/images/pixelart-path/r6/pa-mid.png"
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    print("图：", path, rgb.shape[1], "x", rgb.shape[0])

    print("\n== 深色像素颜色直方图（sum(rgb) < 200）TOP12 ==")
    s = rgb.sum(axis=2)
    for color, n in Counter(map(tuple, rgb[s < 200])).most_common(12):
        print("   #%02X%02X%02X  %6d" % (color[0], color[1], color[2], n))

    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    ink = (r < 32) & (g < 32) & (b < 48)

    content = np.abs(rgb - BG).sum(axis=2) > 40
    h, w = content.shape
    content[int(h * 0.95):, int(w * 0.93):] = False   # 右下角水印
    content[int(h * 0.96):, : int(w * 0.08)] = False

    # 边界 = 自己是内容、且 4 邻域里有非内容
    outside4 = np.zeros_like(content)
    for dy, dx in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
        outside4 |= ~at(content, dy, dx)
    boundary = content & outside4

    # 覆盖 = 外侧 8 邻域里有墨线
    covered = np.zeros_like(content)
    for dy, dx in NEIGH:
        covered |= at(ink, dy, dx)
    miss = boundary & ~covered

    print("\n内容 %d，墨线 %d，剪影边界 %d，外侧无墨线的边界 %d（%.1f%%）"
          % (content.sum(), ink.sum(), boundary.sum(), miss.sum(),
             100.0 * miss.sum() / max(1, boundary.sum())))

    print("\n== 缺环按方向分解（该方向外侧的边界像素里，缺的占比）==")
    for name, (dy, dx) in DIRS.items():
        outer = boundary & ~at(content, dy, dx)
        if outer.sum() == 0:
            continue
        m = (miss & outer).sum()
        print("   %-3s 边界 %6d  缺 %6d  %5.1f%%" % (name, outer.sum(), m, 100.0 * m / outer.sum()))

    over = rgb.astype(np.uint8).copy()
    over[miss] = (255, 0, 255)
    Image.fromarray(over).save(os.path.join(OUT_DIR, "overlay-miss.png"))
    print("\n叠加图：", os.path.join(OUT_DIR, "overlay-miss.png"))

    crops = [
        ((250, 400, 700, 700), 3, "01-platform-lowerleft.png"),
        ((950, 540, 1450, 830), 3, "02-platform-lowerright.png"),
        ((100, 420, 380, 720), 3, "03-left-pillar.png"),
        ((1400, 320, 1570, 480), 4, "04-blue-crew.png"),
        ((780, 520, 920, 680), 4, "05-red-crew.png"),
    ]
    print("\n== 裁图（洋红叠加 + 原图，NEAREST ×scale）==")
    for box, scale, name in crops:
        x0, y0, x1, y1 = box
        for tag, src in (("miss", over), ("raw", rgb.astype(np.uint8))):
            out = os.path.join(OUT_DIR, tag + "-" + name)
            Image.fromarray(src[y0:y1, x0:x1]).resize(
                ((x1 - x0) * scale, (y1 - y0) * scale), Image.NEAREST).save(out)
            print("  ", out)


if __name__ == "__main__":
    main()
