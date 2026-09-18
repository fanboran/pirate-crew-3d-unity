# -*- coding: utf-8 -*-
"""关卡布局图渲染器（关卡制作管线 · 纸面设计阶段的产物工具）。

从 docs/设计/关卡/L0*.md 的 ```layout 围栏块渲染俯视布局 PNG 到 docs/images/level-design/。
设计文档是布局的唯一真源——改布局先改文档的 ASCII 块，再重跑本脚本；本脚本不持有任何布局数据。

围栏块格式（首行 = 头部，含调色板提示）：
    ```layout L02 碎岛雨 palette=island
    <20 字符 × 15 行；字符 = 块数（36 进制 0-9a-z），. = 无平台（水/空）>
    spawns: R(3,3)(6,3)(3,5)(6,5) B(13,9)(16,9)(13,12)(16,12)
    ```

用法：
    python tools/level-design/render_layouts.py            # 渲染全部 L0*.md
    python tools/level-design/render_layouts.py L02        # 只渲染某关
"""
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[2]
DOC_DIR = REPO / "docs" / "设计" / "关卡"
OUT_DIR = REPO / "docs" / "images" / "level-design"

CELL = 56          # 单格像素
MARGIN = 72        # 图幅边距（放坐标轴刻度）
TITLE_H = 64       # 标题带高
LEGEND_H = 96      # 图例带高
GRID_W, GRID_H = 20, 15

# 调色板：块数 → 填色（近似项目低模色语言；仅示意，不代表最终材质）
WATER = (46, 94, 140)
PALETTES = {
    # 云场：高度越高越暖（白 → 金）
    "cloud": [(248, 248, 244), (243, 236, 214), (238, 222, 176), (232, 205, 138), (226, 188, 110)],
    # 岛屿：低=沙，中=草，高=岩
    "island": [(217, 192, 138), (184, 197, 138), (143, 172, 116), (106, 143, 92), (122, 101, 77),
               (100, 82, 62), (82, 67, 51)],
}
RED = (198, 62, 54)
BLUE = (58, 108, 178)

BLOCK_RE = re.compile(r"```layout[ \t]+(L\d+)[ \t]+([^\n]*?)(?:[ \t]+palette=(\w+))?[ \t]*\n(.*?)```", re.S)
ROW_RE = re.compile(r"^[0-9a-z.]{20}$", re.M)
# "spawns: R(a,b)(c,d) B(e,f)" —— 队字母后跟一串坐标对
SPAWN_TEAM_RE = re.compile(r"([RB])((?:\(\d+,\d+\))+)")
SPAWN_PAIR_RE = re.compile(r"\((\d+),(\d+)\)")


def height_color(blocks: int, palette: str):
    ramp = PALETTES[palette]
    if blocks <= 0:
        return WATER
    idx = min(len(ramp) - 1, max(0, (blocks - 1) * len(ramp) // 16))
    return ramp[idx]


def font(size: int):
    for name in ("msyh.ttc", "simhei.ttf", "segoeui.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def render(doc: Path):
    text = doc.read_text(encoding="utf-8")
    out = []
    for m in BLOCK_RE.finditer(text):
        level, title, palette, body = m.group(1), m.group(2).strip(), m.group(3) or "island", m.group(4)
        rows = [r for r in body.splitlines() if ROW_RE.match(r)]
        if len(rows) != GRID_H:
            raise SystemExit(f"{doc.name}: layout 块应为 {GRID_H} 行，实得 {len(rows)} 行")
        grid = [[int(ch, 36) if ch != "." else 0 for ch in row] for row in rows]
        spawns = []
        for team_m in SPAWN_TEAM_RE.finditer(body):
            for pair in SPAWN_PAIR_RE.finditer(team_m.group(2)):
                spawns.append((team_m.group(1), int(pair.group(1)), int(pair.group(2))))

        w = MARGIN * 2 + GRID_W * CELL
        h = TITLE_H + MARGIN + GRID_H * CELL + LEGEND_H
        img = Image.new("RGB", (w, h), (250, 248, 244))
        d = ImageDraw.Draw(img)

        d.text((MARGIN, 18), f"{level} {title}（20×15 格，1 格 = 2 单位）", font=font(30), fill=(40, 40, 40))

        oy = TITLE_H + MARGIN
        for gy in range(GRID_H):
            for gx in range(GRID_W):
                x0, y0 = MARGIN + gx * CELL, oy + gy * CELL
                d.rectangle([x0 + 1, y0 + 1, x0 + CELL - 2, y0 + CELL - 2],
                            fill=height_color(grid[gy][gx], palette),
                            outline=(255, 255, 255) if grid[gy][gx] else (24, 60, 92))
            d.text((MARGIN - 30, oy + gy * CELL + CELL // 2 - 10), str(gy), font=font(20), fill=(120, 120, 120))
        for gx in range(GRID_W):
            d.text((MARGIN + gx * CELL + CELL // 2 - 12, oy + GRID_H * CELL + 8), str(gx),
                   font=font(20), fill=(120, 120, 120))

        for team, gx, gy in spawns:
            cx, cy = MARGIN + gx * CELL + CELL // 2, oy + gy * CELL + CELL // 2
            r = 17
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=RED if team == "R" else BLUE,
                      outline=(255, 255, 255), width=3)

        ly = oy + GRID_H * CELL + 40
        lx = MARGIN
        d.ellipse([lx, ly - 12, lx + 24, ly + 12], fill=RED, outline=(255, 255, 255), width=2)
        d.text((lx + 32, ly - 12), "红队（玩家）", font=font(22), fill=(40, 40, 40))
        lx += 190
        d.ellipse([lx, ly - 12, lx + 24, ly + 12], fill=BLUE, outline=(255, 255, 255), width=2)
        d.text((lx + 32, ly - 12), "蓝队（敌方）", font=font(22), fill=(40, 40, 40))
        lx += 190
        d.rectangle([lx, ly - 12, lx + 24, ly + 12], fill=WATER)
        d.text((lx + 32, ly - 12), "水 / 空（落水即死）", font=font(22), fill=(40, 40, 40))
        lx += 300
        d.rectangle([lx, ly - 12, lx + 24, ly + 12], fill=height_color(1, palette))
        d.text((lx + 32, ly - 12), "字符 = 块数（36 进制），颜色随高度加深", font=font(22), fill=(40, 40, 40))

        dest = OUT_DIR / f"{level}-layout.png"
        img.save(dest)
        out.append(dest)
        print(f"渲染 {dest.relative_to(REPO)}（出生点 {len(spawns)} 个）")
    return out


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    level_filter = sys.argv[1] if len(sys.argv) > 1 else ""
    docs = sorted(DOC_DIR.glob("L0*.md"))
    if not docs:
        raise SystemExit(f"找不到设计文档：{DOC_DIR}/L0*.md")
    total = 0
    for doc in docs:
        if level_filter and level_filter.lower() not in doc.name:
            continue
        total += len(render(doc))
    print(f"共渲染 {total} 张布局图。")


if __name__ == "__main__":
    main()
