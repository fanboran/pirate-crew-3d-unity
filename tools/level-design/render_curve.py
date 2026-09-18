# -*- coding: utf-8 -*-
"""关间难度曲线渲染器（关卡制作管线 · 纸面设计阶段的产物工具）。

从 docs/设计/关卡/README.md 的 ```curve 围栏块渲染关间难度曲线 PNG。
README 的 curve 块是唯一真源——改曲线先改表，再重跑本脚本。

块格式（首行为表头，首列 = 关号，其余列 = 数值序列）：
    ```curve 关间难度曲线
    level,蓝方单位,蓝方luck,初始武器种数,空投池种数
    L01,3,1,1,1
    ```
末列名为「蓝方luck」时以折线 + 右轴渲染，其余为柱状。

用法：python tools/level-design/render_curve.py
"""
import csv
import io
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[2]
README = REPO / "docs" / "设计" / "关卡" / "README.md"
OUT = REPO / "docs" / "images" / "level-design" / "difficulty-curve.png"

W, H = 1280, 720
PLOT = (120, 90, W - 80, H - 120)   # l, t, r, b
BAR_COLORS = [(96, 130, 182), (143, 172, 116), (217, 179, 106), (182, 130, 96), (150, 120, 160)]
LINE_COLOR = (198, 62, 54)

CURVE_RE = re.compile(r"```curve[ \t]+([^\n]*)\n(.*?)```", re.S)


def font(size: int):
    for name in ("msyh.ttc", "simhei.ttf", "segoeui.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def main():
    text = README.read_text(encoding="utf-8")
    m = CURVE_RE.search(text)
    if not m:
        raise SystemExit(f"{README} 里没有 ```curve 块")
    title, body = m.group(1).strip(), m.group(2)
    rows = list(csv.reader(io.StringIO(body.strip())))
    header, data = rows[0], rows[1:]
    levels = [r[0] for r in data]
    series = [[int(v) for v in r[1:]] for r in data]
    n_cols = len(header) - 1

    img = Image.new("RGB", (W, H), (250, 248, 244))
    d = ImageDraw.Draw(img)
    d.text((PLOT[0], 28), title, font=font(32), fill=(40, 40, 40))

    line_col = header.index("蓝方luck") - 1 if "蓝方luck" in header else -1
    bar_max = max((series[i][c] for i in range(len(data)) for c in range(n_cols) if c != line_col), default=1)
    line_max = max((series[i][line_col] for i in range(len(data))), default=1) if line_col >= 0 else 1

    l, t, r, b = PLOT
    d.line([l, t - 10, l, b, r, b], fill=(120, 120, 120), width=2)
    for v in range(0, bar_max + 1):
        y = b - (b - t) * v / (bar_max + 1)
        d.text((l - 44, y - 10), str(v), font=font(18), fill=(120, 120, 120))

    group_w = (r - l) / len(data)
    bar_w = group_w * 0.62 / (n_cols - (1 if line_col >= 0 else 0))
    for gi, level in enumerate(levels):
        gx = l + group_w * gi + group_w * 0.19
        bi = 0
        for c in range(n_cols):
            if c == line_col:
                continue
            v = series[gi][c]
            h = (b - t) * v / (bar_max + 1)
            x0 = gx + bi * bar_w
            d.rectangle([x0, b - h, x0 + bar_w - 4, b - 2], fill=BAR_COLORS[bi % len(BAR_COLORS)])
            d.text((x0, b - h - 26), str(v), font=font(17), fill=(70, 70, 70))
            bi += 1
        d.text((l + group_w * gi + group_w / 2 - 22, b + 14), level, font=font(24), fill=(40, 40, 40))

    if line_col >= 0:
        pts = []
        for gi in range(len(data)):
            cx = l + group_w * gi + group_w / 2
            cy = b - (b - t) * series[gi][line_col] / (line_max + 1)
            pts += [cx, cy]
            d.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=LINE_COLOR)
        d.line(pts, fill=LINE_COLOR, width=3)
        for v in range(0, line_max + 1):
            y = b - (b - t) * v / (line_max + 1)
            d.text((r + 12, y - 10), str(v), font=font(18), fill=LINE_COLOR)

    lx = l
    for c in range(n_cols):
        color = LINE_COLOR if c == line_col else BAR_COLORS[c % len(BAR_COLORS)]
        if c == line_col:
            d.line([lx, H - 44, lx + 30, H - 44], fill=color, width=3)
        else:
            d.rectangle([lx, H - 52, lx + 22, H - 36], fill=color)
        d.text((lx + 36, H - 52), header[c + 1], font=font(20), fill=(40, 40, 40))
        lx += 60 + d.textlength(header[c + 1], font=font(20))

    img.save(OUT)
    print(f"渲染 {OUT.relative_to(REPO)}")


if __name__ == "__main__":
    main()
