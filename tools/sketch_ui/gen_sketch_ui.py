# -*- coding: utf-8 -*-
"""手绘 UI 贴图生成器——自 game-2/stick-world temp/gen_sketch_ui_a10.py 原样移植
（用户 2026-09-20 裁决：隔壁手绘涂鸦素材与生成流水线原封不动搬来，只换配色）。

几何与烘焙纪律全部沿用上游 a10_clean：
  · 单一几何参数（TEX=96 / SS=4 超采样 / MARGIN=10 / INSET=0.6 / cos 整数频率噪声）；
  · wobbly 圆角矩形路径 + 三帧沸腾（每帧独立 seed 重掷边缘扰动）；
  · 面板 wobble 1.8 / 小件 1.1（静态手绘感不依赖沸腾成立）；
  · 面板黑场 2~3% 纸感噪点（中心块周期平铺无缝）；
  · 产物逐槽过「源图四边存在性 + 九宫格平铺接缝跳变」自检，任何槽位失败即停产。

本项目改动（仅两处，几何零改动）：
  1. 配色常量区换成 UiSkin.cs 色板（夜海靛蓝深底 + 宝石彩 + 金强调；白系三级描边与
     纸感 btn_ink 槽沿用上游——墨线纪律与色相无关）；
  2. 新增三个 tint 槽（pip / fill / ring）：白色实底、运行时 Image.color 乘色——
     队色血条段 / 职业色头像点 / 回合徽章圆片需要"同一形状任意色"，烘焙期烘白最省；
     ring 槽把圆角参数推到近圆（corner_r 参数化是唯一几何签名改动，默认值不变）。

用法：python tools/sketch_ui/gen_sketch_ui.py
产物：pirate-crew/Assets/Art/UI/Sketch/{slot}_f{i}.png（96px，九宫格 border=10）
     + export/sketch-ui/_preview_9slice.png（人工验收拼图）
"""

from __future__ import annotations

import math
import os
import random


from PIL import Image, ImageDraw

SS = 4
TEX = 96
MARGIN = 10
INSET = 0.6
CORNER_R = 6.0
WOBBLE = 1.1
LINE_W = 2.6
FRAMES = 3
SEP_LEN = 64
SEP_TH = 12
TAU = math.tau

REPO = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
# 产物放 Resources：Editor 装配的场景序列化引用持久资产，播放器 Resources.Load 同源取帧。
OUT_DIR = os.path.normpath(os.path.join(REPO, "pirate-crew", "Assets", "Resources", "UI", "Sketch"))
PREVIEW_DIR = os.path.normpath(os.path.join(REPO, "export", "sketch-ui"))

def C(r, g, b, a=1.0):
    return (round(r * 255), round(g * 255), round(b * 255), round(a * 255))

# ---- 配色区（唯一改动①：换成 pirate-crew UiSkin.cs 色板；透明度结构沿用上游） ----
WINDOW_BG = C(0.110, 0.137, 0.200, 0.88)      # UiSkin.InkDeep 夜海靛蓝（半透明玻璃窗）
WINDOW_BG_LIGHT = C(0.165, 0.200, 0.314, 0.72)  # UiSkin.InkSoft（HUD 横条更透一档）
GROOVE_BG = C(0.063, 0.078, 0.118, 0.45)      # UiSkin.BarTrackInk 凹槽底
BORDER = C(1.0, 1.0, 1.0, 0.16)               # 白系三级描边（上游纪律：墨线与色相无关）
BORDER_PANEL = C(1.0, 1.0, 1.0, 0.30)
BORDER_STRONG = C(1.0, 1.0, 1.0, 0.38)
ACCENT = C(0.949, 0.757, 0.306)               # UiSkin.Gold 强调金
ACCENT_BG = C(0.949, 0.757, 0.306, 0.14)
DANGER = C(1.0, 0.227, 0.161)                 # UiSkin.TeamRed（语义红做边框/叠加要够亮）
DANGER_BG = C(1.0, 0.227, 0.161, 0.14)
BTN_BG = C(1.0, 1.0, 1.0, 0.07)
BTN_BG_HOVER = C(1.0, 1.0, 1.0, 0.10)
BTN_BG_PRESSED = C(1.0, 1.0, 1.0, 0.04)
BTN_BG_DISABLED = C(1.0, 1.0, 1.0, 0.03)
INK = C(0.165, 0.114, 0.055, 1.0)             # UiSkin.InkOnGold 深墨棕
NONE = (0, 0, 0, 0)

## 面板 wobble 档（上游审计：面板级 1.5~2.2，小件保持默认 1.1）
WOBBLE_PANEL = 1.8

## tint 槽近圆圆角（唯一几何签名改动②：wobbly_path 增加 corner_r 参数，默认 CORNER_R 不变）
CORNER_R_ROUND = 44.0

RECIPES = {
    "panel":          {"bg": WINDOW_BG, "border": BORDER_PANEL, "wobble": WOBBLE_PANEL, "grain": True},
    "panel_light":    {"bg": WINDOW_BG_LIGHT, "border": BORDER_PANEL, "wobble": WOBBLE_PANEL, "grain": True},
    "groove":         {"bg": GROOVE_BG, "border": C(1, 1, 1, 0.05)},
    "groove_focus":   {"bg": GROOVE_BG, "border": ACCENT},
    "btn_normal":     {"bg": BTN_BG, "border": BORDER},
    "btn_hover":      {"bg": BTN_BG_HOVER, "border": BORDER_STRONG},
    "btn_pressed":    {"bg": BTN_BG_PRESSED, "border": ACCENT},
    "btn_disabled":   {"bg": BTN_BG_DISABLED, "border": C(1, 1, 1, 0.04)},
    "accent_normal":  {"bg": ACCENT_BG, "border": C(ACCENT[0], ACCENT[1], ACCENT[2], 190)},
    "accent_hover":   {"bg": C(ACCENT[0], ACCENT[1], ACCENT[2], 46), "border": ACCENT},
    "accent_pressed": {"bg": C(ACCENT[0], ACCENT[1], ACCENT[2], 20), "border": C(ACCENT[0], ACCENT[1], ACCENT[2], 150)},
    "btn_primary_normal":   {"bg": ACCENT, "border": INK},
    "btn_primary_hover":    {"bg": C(1.0, 0.85, 0.50), "border": INK},
    "btn_primary_pressed":  {"bg": C(0.78, 0.60, 0.22), "border": INK},
    "btn_primary_disabled": {"bg": C(0.949, 0.757, 0.306, 0.25), "border": C(0.165, 0.114, 0.055, 0.35)},
    "btn_ink_normal":   {"bg": C(0.95, 0.92, 0.84, 0.92), "border": INK},
    "btn_ink_hover":    {"bg": C(1.0, 0.97, 0.90, 0.95), "border": INK},
    "btn_ink_pressed":  {"bg": C(0.86, 0.82, 0.74, 0.92), "border": INK},
    "btn_ink_disabled": {"bg": C(0.90, 0.88, 0.82, 0.50), "border": C(0.165, 0.114, 0.055, 0.30)},
    "danger_normal":  {"bg": DANGER_BG, "border": C(DANGER[0], DANGER[1], DANGER[2], 180)},
    "danger_hover":   {"bg": C(DANGER[0], DANGER[1], DANGER[2], 46), "border": DANGER},
    "danger_pressed": {"bg": C(DANGER[0], DANGER[1], DANGER[2], 20), "border": C(DANGER[0], DANGER[1], DANGER[2], 150)},
    "tab_selected":   {"bg": ACCENT_BG, "border": NONE, "underline": ACCENT},
    "tab_hover":      {"bg": BTN_BG, "border": NONE},
    "progress_bg":    {"bg": GROOVE_BG, "border": BORDER},
    "progress_fill":  {"bg": ACCENT, "border": C(0.0, 0.0, 0.0, 60)},
    # ---- 本项目 tint 槽（白实底，运行时 Image.color 乘色；墨边乘色后仍近黑可辨——
    #      白系边乘深色会隐形，所以彩底件一律走墨边）----
    "pip":            {"bg": C(1, 1, 1), "border": INK, "corner": CORNER_R_ROUND},
    "ring":           {"bg": C(1, 1, 1), "border": INK, "corner": CORNER_R_ROUND, "wobble": WOBBLE_PANEL},
    "fill":           {"bg": C(1, 1, 1), "border": C(0.0, 0.0, 0.0, 60)},
    "cell":           {"bg": C(1, 1, 1), "border": INK},
}

def cnoise(t: float, sd: float) -> float:
    return (math.cos(TAU * t + sd * 0.7) * 0.55
            + math.cos(TAU * 3.0 * t + sd * 1.9) * 0.30
            + math.cos(TAU * 7.0 * t + sd * 3.7) * 0.15)

def _rand(sd: float) -> float:
    v = math.sin(sd * 127.1 + 311.7) * 43758.5453
    return (v - math.floor(v)) - 0.5

def wobbly_path(size: float, seed: int, amp_px: float = WOBBLE, corner_r: float = CORNER_R) -> list:
    """圆角矩形 + boiling 扰动（上游 a10_clean 原样，仅 corner_r 参数化供 tint 圆槽）。"""
    inset = INSET * SS
    r = corner_r * SS
    M = MARGIN * SS
    E = size - MARGIN * SS
    amp = amp_px * SS
    pts = []

    def h_edge(x0: float, x1: float, y_fixed: float, side: int) -> None:
        sd = seed * 0.3117 + side * 13.7
        n = 30
        for i in range(n + 1):
            t = i / n
            x = x0 + (x1 - x0) * t
            tn = (x - M) / (E - M)
            pts.append((x, y_fixed + cnoise(tn, sd) * amp))

    def v_edge(y0: float, y1: float, x_fixed: float, side: int) -> None:
        sd = seed * 0.7 + side * 7.3
        n = 26
        for i in range(n + 1):
            t = i / n
            y = y0 + (y1 - y0) * t
            tn = (y - M) / (E - M)
            pts.append((x_fixed + cnoise(tn, sd) * amp, y))

    def arc(cx: float, cy: float, a0: float, a1: float, side: int) -> None:
        sd = seed * 0.7 + side * 7.3
        for i in range(7):
            t = i / 7.0
            a = a0 + (a1 - a0) * t
            rr = r + _rand(sd + i * 3.1) * amp * 1.2
            pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))

    A = inset + r
    B = size - inset - r
    h_edge(A, B, inset, 2)
    arc(B, A, math.pi * 1.5, math.tau, 3)
    v_edge(A, B, size - inset, 4)
    arc(B, B, 0.0, math.pi * 0.5, 5)
    h_edge(B, A, size - inset, 6)
    arc(A, B, math.pi * 0.5, math.pi, 7)
    v_edge(B, A, inset, 0)
    arc(A, A, math.pi, math.pi * 1.5, 1)
    return pts

def _grain(img: Image.Image, frame: int) -> None:
    """面板黑场纸感噪点（上游原样：中心块内平铺 0~12 级白噪）。"""
    span = (TEX - 2 * MARGIN) * SS
    rng = random.Random(9000 + frame * 131)
    tile = Image.new("L", (span, span))
    tile.putdata([rng.randint(0, 12) for _ in range(span * span)])
    mask = Image.new("L", img.size, 0)
    mask.paste(tile, (MARGIN * SS, MARGIN * SS))
    white = Image.new("RGBA", img.size, (255, 255, 255, 255))
    white.putalpha(mask)
    img.alpha_composite(white)

def render_slot(recipe: dict, frame: int) -> Image.Image:
    size = TEX * SS
    img = Image.new("RGBA", (size, size), NONE)
    dr = ImageDraw.Draw(img)
    seed = 1000 + frame * 7717
    path = wobbly_path(size, seed, recipe.get("wobble", WOBBLE),
                       recipe.get("corner", CORNER_R))
    bg = recipe.get("bg", NONE)
    if bg[3] > 0:
        dr.polygon(path, fill=bg)
    if recipe.get("grain"):
        _grain(img, frame)
    border = recipe.get("border", NONE)
    if border[3] > 0:
        dr.line(path + [path[0]], fill=border, width=round(LINE_W * SS), joint="curve")
    uline = recipe.get("underline")
    if uline is not None:
        M = MARGIN * SS
        y = size - M + 0.5 * SS
        sd = seed * 0.53
        line_pts = []
        for i in range(24):
            t = i / 24
            x = M + (size - M * 2) * t
            yy = y + cnoise(t, sd) * WOBBLE * SS * 1.4
            line_pts.append((x, yy))
        dr.line(line_pts, fill=uline, width=round(LINE_W * SS * 1.7), joint="curve")
    return img.resize((TEX, TEX), Image.LANCZOS)

def render_sep(vertical: bool, frame: int) -> Image.Image:
    w, h = (SEP_TH * SS, SEP_LEN * SS) if vertical else (SEP_LEN * SS, SEP_TH * SS)
    img = Image.new("RGBA", (w, h), NONE)
    dr = ImageDraw.Draw(img)
    seed = 2200 + frame * 911
    sd = seed * 0.77
    amp = WOBBLE * SS * 1.8
    pts = []
    for i in range(20):
        t = i / 20
        o = cnoise(t, sd) * amp
        if vertical:
            pts.append((w * 0.5 + o, h * (0.12 + 0.76 * t)))
        else:
            pts.append((w * (0.12 + 0.76 * t), h * 0.5 + o))
    dr.line(pts, fill=BORDER, width=round(LINE_W * SS * 0.85), joint="curve")
    return img.resize((SEP_TH if vertical else SEP_LEN,
                       SEP_LEN if vertical else SEP_TH), Image.LANCZOS)

def nine_slice_sim(src: Image.Image, out_w=280, out_h=64) -> Image.Image:
    """按 MARGIN 切九块拼装（边平铺、角固定、中心平铺）。"""
    s = src.load()
    out = Image.new("RGBA", (out_w, out_h), (18, 18, 24, 255))
    o = out.load()
    span = TEX - 2 * MARGIN
    for y in range(out_h):
        for x in range(out_w):
            if x < MARGIN:
                sx = x
            elif x >= out_w - MARGIN:
                sx = TEX - (out_w - x)
            elif y < MARGIN or y >= out_h - MARGIN:
                sx = MARGIN + (x - MARGIN) % span
            else:
                sx = MARGIN + (x - MARGIN) % span
            if y < MARGIN:
                sy = y
            elif y >= out_h - MARGIN:
                sy = TEX - (out_h - y)
            elif x < MARGIN or x >= out_w - MARGIN:
                sy = MARGIN + (y - MARGIN) % span
            else:
                sy = MARGIN + (y - MARGIN) % span
            o[x, y] = s[sx, sy]
    return out

def check_source_shape(img: Image.Image) -> int:
    """源图四边存在性（上游自检一：拦截三角形类几何错）。"""
    p = img.load()
    errs = 0
    for label, y_win in [("top", range(0, 12)), ("bottom", range(84, 96))]:
        found = 0
        for x in range(20, 76, 4):
            for y in y_win:
                r, g, b, a = p[x, y]
                if (r + g + b) / 3.0 * (a / 255.0) > 8:
                    found += 1
                    break
        if found < 10:
            errs += 1
    return errs

def check_tiling(img: Image.Image) -> int:
    """跨接缝不连续检测（上游自检二：只查平铺接缝坐标处 y 跳变）。"""
    p = img.load()
    w = img.size[0]
    span = TEX - 2 * MARGIN
    seams = []
    x = MARGIN + span
    while x < w - MARGIN - 2:
        seams.append(x)
        x += span
    jumps = 0
    for sx in seams:
        ly, lv = _peak(p, sx - 3, sx - 1)
        ry, rv = _peak(p, sx + 1, sx + 3)
        if lv > 24 and rv > 24 and abs(ly - ry) > 4:
            jumps += 1
    return jumps

def _peak(p, x0, x1):
    best_y, best_v = -1, 0.0
    for x in range(x0, x1 + 1):
        for y in range(0, 18):
            r, g, b, a = p[x, y]
            lum = (r + g + b) / 3.0 * (a / 255.0)
            if lum > best_v:
                best_v, best_y = lum, y
    return best_y, best_v

def build_preview(slots: list) -> None:
    try:
        from PIL import ImageFont
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)
    except Exception:
        from PIL import ImageFont
        font = ImageFont.load_default()
    cell_w, cell_h, label_h = 280, 64, 20
    cols = 3
    rows = math.ceil(len(slots) / cols)
    out = Image.new("RGB", (cols * (cell_w + 12) + 12,
                            rows * (cell_h + label_h + 12) + 12), (24, 24, 30))
    dr = ImageDraw.Draw(out)
    for i, name in enumerate(slots):
        cx = 12 + (i % cols) * (cell_w + 12)
        cy = 12 + (i // cols) * (cell_h + label_h + 12)
        sim = nine_slice_sim(Image.open(os.path.join(OUT_DIR, name + "_f0.png")).convert("RGBA"))
        out.paste(sim.convert("RGB"), (cx, cy + label_h))
        dr.text((cx, cy + 2), name + "  9-slice 280x64", fill=(160, 160, 170), font=font)
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    out.save(os.path.join(PREVIEW_DIR, "_preview_9slice.png"))
    print("[sketch] preview -> export/sketch-ui/_preview_9slice.png")

## 大件槽：额外切 9 砖（panel_tl/bc/... 命名）供 Unity Tiled 平铺组装——
## Godot StyleBoxTexture 的边带是 TILE 平铺，Unity Sliced 是边带拉伸；
## 大面板（600px+）上 Sliced 会把 76px 的墨线小波浪拉成 1000px 缓波，
## 手绘感的"密度"就是这边丢的。砖块=边带整平铺周期段，Tiled 重复即复刻 Godot。
TILE_SLOTS = ("panel", "panel_light", "progress_bg", "btn_normal",
              "btn_primary_normal", "danger_normal")

def slice_tiles(slot: str) -> None:
    for f in range(FRAMES):
        src = Image.open(os.path.join(OUT_DIR, "%s_f%d.png" % (slot, f))).convert("RGBA")
        m, e = MARGIN, TEX - MARGIN
        parts = {
            "tl": (0, 0, m, m),           "tc": (m, 0, e, m),           "tr": (e, 0, TEX, m),
            "ml": (0, m, m, e),           "mc": (m, m, e, e),           "mr": (e, m, TEX, e),
            "bl": (0, e, m, TEX),         "bc": (m, e, e, TEX),         "br": (e, e, TEX, TEX),
        }
        for part, box in parts.items():
            src.crop(box).save(os.path.join(OUT_DIR, "%s_%s_f%d.png" % (slot, part, f)))

def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    count = 0
    for slot, recipe in RECIPES.items():
        for f in range(FRAMES):
            render_slot(recipe, f).save(os.path.join(OUT_DIR, "%s_f%d.png" % (slot, f)))
            count += 1
    for f in range(FRAMES):
        render_sep(False, f).save(os.path.join(OUT_DIR, "sep_h_f%d.png" % f))
        render_sep(True, f).save(os.path.join(OUT_DIR, "sep_v_f%d.png" % f))
        count += 2
    print("[sketch] %d textures -> %s" % (count, OUT_DIR))
    for slot in TILE_SLOTS:
        slice_tiles(slot)
    print("[sketch] 9-slice tiles for %d slots" % len(TILE_SLOTS))
    failures = []
    for slot in RECIPES:
        img = Image.open(os.path.join(OUT_DIR, slot + "_f0.png")).convert("RGBA")
        shape_errs = check_source_shape(img)
        jumps = check_tiling(nine_slice_sim(img))
        status = "OK" if jumps == 0 and shape_errs == 0 else "BAD(%d jumps,%d shape)" % (jumps, shape_errs)
        if jumps > 0 or shape_errs > 0:
            failures.append(slot)
        print("  [%6s] %s" % (status, slot))
    build_preview(list(RECIPES.keys()))
    if failures:
        raise SystemExit("[sketch] FAILED slots: %s" % failures)
    print("[sketch] ALL SLOTS TILING-CLEAN")

if __name__ == "__main__":
    main()
