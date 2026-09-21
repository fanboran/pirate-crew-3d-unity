#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""judge_toon_pilot.py —— 赛璐璐 + 反壳描边试点验收的程序化判据脚本。

用法
----
    python tools/pixel-review/judge_toon_pilot.py <图片目录或单张 png/jpg>
    python tools/pixel-review/judge_toon_pilot.py docs/images/pixel-review/r1
    python tools/pixel-review/judge_toon_pilot.py docs/images/pixel-review/r1/battle-45.jpg
    python tools/pixel-review/judge_toon_pilot.py <目录> --block 3      # 手工指定块边长

目录会递归收集 .png/.jpg/.jpeg（大小写不敏感），每张图输出一行判据，
最后对整批输出汇总。退出码：全部核心判据通过 = 0，有 FAIL = 1，只有 WARN = 0。

依赖
----
    numpy + Pillow（二者本机已装：numpy 2.3.5 / Pillow 12.0.0）。无 numpy 的纯 PIL
    回退未实现——本判据的块统计用 numpy 才算得动 1920×1080。

背景（为什么是这四项）
----------------------
全屏像素化 RendererFeature 把 3D 画面降采样到 640×360 的 Point RT 再 nearest 放大回屏幕
（1920×1080 时块 = 3×3 屏幕像素；RT 宽恒 640，块边长 = 屏宽/640）。UI（Overlay Canvas）
不像素化。本脚本是这套管线 + 赛璐璐材质 + 反向壳描边的验收判据，替代此前只有会话内联
`python -c` 的临时口径（见 docs/images/pixel-review/r1/README.md 第 29-34 行）。


判据一：块化判据（r1 口径 + JPEG 鲁棒化）
------------------------------------------
口径（复刻 r1）：把整图切成 b×b 块，算每块「块内像素灰度 − 块均值」的绝对值平均，
再对全图所有像素取均值 —— 即列 `块方差`。r1 的 0.00 就是这么量出来的（对 1920×1080 PNG，
b=3）。b 按输入分辨率自适应：`b = max(1, round(width/640))`，即 1920 宽 → 3、1280 宽 → 2；
这正是「1 RT 像素 = b×b 屏幕像素」的定义，所以对 1280 宽的入库缩略图 b=2 才是对的块网格
（拿 3 去切 1280 图 = 量错网格，实测两组图会混在一起、完全不可分）。

⚠ 但 r1 的 0.00 只对**无损 1920 PNG** 成立。入库图是 1280 宽 JPEG（AGENTS.md 规定截图压
1280 JPEG），JPEG 有损 + 2/3 缩放把块锐边抹成了噪声，`块方差` 再也压不到 0 —— 实测
r1 的 JPEG 正例 0.52~2.46、旧图反例 2.26~3.32，**两者重叠、单独用 `块方差` 不可分**。
所以并列一个对 JPEG 免疫的判据：**块化比**。

块化比 = 对齐网格的 `块方差` ÷ 平移网格的 `块方差` 均值（网格原点取 (0,0)…(b-1,b-1) 全部
b² 个位置，分子取 (0,0)）。直觉：真块化时像素只在块内一致、跨块边界必跳变，于是「切在
块边界上」量到的方差远小于「切在块内部」；非块化时两种切法统计等价、比值 → 1。
- 真块化（理论极限）：比 = 0.00；
- 未块化（平滑图）：比 ≈ 1.00；
- 合成验证（1920×1080，块均值降采样→nearest 放大）：比 = 0.0000、块方差 = 0.0000，
  与 r1 的 0.00 完全吻合；同图未块化时比 = 1.000。

**实测校准（两组正反例，1280×720 JPEG，b=2）**
| 图组 | 文件 | 块方差 | 块化比 |
| --- | --- | --- | --- |
| 正例（像素化生效） | r1/unit-closeup.jpg | 0.52 | 0.230 |
| 正例 | r1/sea-shore.jpg | 0.77 | 0.252 |
| 正例 | r1/terrain-high.jpg | 1.31 | 0.257 |
| 正例 | r1/arena-overview.jpg | 1.02 | 0.356 |
| 正例 | r1/battle-45.jpg | 2.03 | 0.411 |
| 正例（含 HUD，UI 不块化故最高） | r1/hud-weaponpanel.jpg | 2.46 | 0.476 |
| 反例（未像素化） | scene-art-bake/l1-cloudfield-baked.jpg | 2.26 | 0.973 |
| 反例 | scene-art-bake/l2-ships-procedural.jpg | 3.08 | 0.973 |
| 反例 | scene-art-bake/l2-ships-baked.jpg | 3.32 | 0.979 |

→ **阈值（用块化比判定，两边留 ≥20% 余量）**：
- `块化比 < 0.60` ⇒ PASS（像素化生效）——正例最高 0.476，余量 21%；
- `块化比 > 0.85` ⇒ FAIL（未像素化）——反例最低 0.973，余量 13%；
- 0.60~0.85 ⇒ UNCERTAIN，需换无损 PNG 复测或人眼复核。
`块方差` 只作参考列，不单独判定。含 UI 的图块化比天然偏高（HUD 覆盖区不块化，
r1 的 HUD 图 0.476 vs 纯 3D 图 0.230~0.411）——这是红线 2 的预期表现，不是缺陷；
但若某图块化比冲进 0.6 以上，先确认是不是 HUD 占比过大再下结论。

⚠ `--block` 是给**非标准分辨率**用的逃生口，别拿它去「凑」判据：对 1280 宽 JPEG 强指
`--block 3`（拿 1920 的块网格切 1280 的图）会让 r1 六张正例块化比全部落到 0.99~1.02、
齐刷刷 FAIL —— 这不是「像素化失效」，是**量错了网格**，脚本对自适应口径的说明会同步打印
（`块边长：自适应 宽/640（1280w→b2）`），看到这行不一致就先怀疑自己的 `--block`。
（合成反证：同一张 1920×1080 图，块均值降采样→nearest 放大后 b=3 测得块方差 0.0000 /
块化比 0.0000，未块化的同一张测得块化比 1.000 —— 自适应口径在正确网格上是准的。）

图宽 < RT 宽（约 <960，块退化到 b=1）时块网格不存在，`块化比` 打 `n/a`、整图判定 `n/a`
而非 PASS —— 判不了就不给通过，避免小图/图标混进来虚报。

辅助列「全零块占比」= RGB 三通道在块内**严格全等**（每通道极差 0）的块占比。对无损图
它是像素化的强证据；对 JPEG 无用（实测正例 0.03~0.64、反例 0.22~0.30，重叠），
仅作趋势留存。


判据二：洋红检测（缺材质/坏 shader 的标志色）
----------------------------------------------
口径：`R>230 且 B>230 且 G<80` 的像素数。预期 **0**。
实测校准：r1 六张 + scene-art-bake 三张**全部为 0**——这两组都是「无缺材质」的既有基线，
所以 0 是本项目的常态值，任何非 0 都是新引入的坏材质信号。
阈值：0 = PASS；1~99 = WARN（容许 JPEG 边缘振铃误报）；≥100 = FAIL（成片出现 = 材质丢失）。
（此判据在整图上算，UI 区也计入——UI 用洋红通常也是坏图。）


判据三：唯一色数（色带表面颜色收敛追踪）
------------------------------------------
口径：全图 RGB 各通道 8bit 直接去重后的唯一颜色数（含 UI，不强行分区域）。
用途：赛璐璐材质把明暗量化成少数色阶，3D 区域颜色数应显著少于写实材质。**只做追踪，
不做单图 PASS/FAIL**——它衡量的是「材质是否收敛」，需要同一机位的 A/B（试点前 vs 试点后）
对比才有意义，且 JPEG 会因块噪声把色数抬高。
实测校准基线（JPEG，含 JPEG 噪声抬升）：
- r1 正例：28672（sea-shore）/ 40320（arena-overview）/ 53578（unit-closeup）/
  60101（battle-45）/ 64078（terrain-high）/ 74638（hud-weaponpanel）；
- 旧图反例：44878 / 48238 / 74208。
两组重叠 —— 说明「唯一色数」**不判像素化**（像素化只降到 640×360 再放大，理论色数上限
640×360=230400，远高于实测，故色数由材质而非像素化决定）。试点验收时的期望方向是：
对同一机位，赛璐璐化后 3D 区颜色数应明显下降；拿到 **无损 PNG** 才可作为硬指标
（JPEG 的正负噪声可达数千色）。
参考带（JPEG 口径）：< 20000 = 颜色高度收敛（很可能是大色块天空/海面）；
20000~80000 = 本批常态；> 100000 = 纹理丰富/未量化。


判据四：描边墨色覆盖（反向壳描边是否生效的追踪量）
--------------------------------------------------
口径：RGB 三通道均 < 60 且两两差 < 15（即近中性深色 ≈ 墨色）的像素占全图百分比。
反向壳描边生效时该值应明显 > 0：1 RT 像素的描边线 × b 倍放大 ≈ b 屏幕像素宽的线，
高对比轮廓会成片贡献深色像素。
实测校准（⚠ 本批都没开描边，下面是「无描边/旧资产」基线，不是目标值）：
- r1：0.305（arena-overview）/ 3.261（hud-weaponpanel）/ 4.089（battle-45）/
  4.819（terrain-high）/ 9.115（sea-shore）/ 17.712（unit-closeup）；
- 旧图：0.290（cloudfield）/ 2.762（ships-baked）/ 3.645（ships-procedural）。
→ 结论：**旧 PBR 资产自身就带大量深色**（海面暗部、船体阴影、特写剪影），单图阈值
不可靠。本判据的正确用法是 **A/B 对照**：同一机位「描边开」vs「描边关」两次出图，
比值 ≥1.2× 或绝对提升 ≥1 个百分点才算描边可见；试点图可对 r1 同机位图算增量。
注意 1280 宽 JPEG 上 1 RT 像素的描边只有 2 屏像素宽，比 1920 原图（3 屏像素）更易被
JPEG 抹掉——**描边验收务必用 1920 无损 PNG**，本列只作追踪。
本脚本对本判据只给 INFO（数值 + 与基线的差），不下 PASS/FAIL。
"""

import argparse
import os
import re
import sys
import unicodedata
from pathlib import Path

try:
    import numpy as np
except ImportError:  # pragma: no cover
    sys.stderr.write("需要 numpy：pip install numpy\n")
    raise SystemExit(2)

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.stderr.write("需要 Pillow：pip install Pillow\n")
    raise SystemExit(2)


# ---------------------------------------------------------------------------
# 口径常量
# ---------------------------------------------------------------------------

# 像素化 RT 宽度（RendererFeature 定死高 360，宽随宽高比；16:9 → 640）
RT_WIDTH = 640

# 判据一阈值（见文件头校准表）
BLOCK_RATIO_PASS = 0.60   # 块化比 <= 此值 ⇒ 像素化生效
BLOCK_RATIO_FAIL = 0.85   # 块化比 >= 此值 ⇒ 未像素化

# 判据二阈值
MAGENTA_WARN = 1
MAGENTA_FAIL = 100

IMAGE_EXTS = (".png", ".jpg", ".jpeg")

VERDICT_PASS = "PASS"
VERDICT_WARN = "WARN"
VERDICT_FAIL = "FAIL"
VERDICT_INFO = "-"


# ---------------------------------------------------------------------------
# 基础统计
# ---------------------------------------------------------------------------

def block_mad(gray, b, oy=0, ox=0):
    """块内像素与块均值差的绝对值平均（对全图所有像素取均值）——r1 口径。

    gray: 2D float 数组；b: 块边长；oy/ox: 网格原点偏移。
    """
    g = gray[oy:, ox:]
    h, w = g.shape
    hc = h - h % b
    wc = w - w % b
    if hc == 0 or wc == 0:
        return float("nan")
    g = g[:hc, :wc]
    blk = g.reshape(hc // b, b, wc // b, b).transpose(0, 2, 1, 3).reshape(-1, b * b)
    return float(np.abs(blk - blk.mean(axis=1, keepdims=True)).mean())


def block_size_for(width, override=None):
    """像素化块边长：1 RT 像素 = b×b 屏幕像素，故 b = round(宽 / 640)。"""
    if override:
        return int(override)
    return max(1, int(round(width / float(RT_WIDTH))))


def flat_block_fraction(rgb, b, oy=0, ox=0):
    """RGB 三通道在块内严格全等（每通道极差为 0）的块占比。"""
    a = rgb[oy:, ox:]
    h, w, _ = a.shape
    hc = h - h % b
    wc = w - w % b
    if hc == 0 or wc == 0:
        return float("nan")
    a = a[:hc, :wc]
    blk = a.reshape(hc // b, b, wc // b, b, 3).transpose(0, 2, 1, 3, 4).reshape(-1, b * b, 3)
    rng = blk.max(axis=1) - blk.min(axis=1)          # (nblk, 3) 每通道极差
    flat = (rng.max(axis=1) == 0)
    return float(flat.mean())


def analyze(path, block_override=None):
    """对单张图跑四项判据，返回 dict。"""
    with Image.open(path) as im:
        im = im.convert("RGB")
        rgb = np.asarray(im, dtype=np.uint8)

    h, w, _ = rgb.shape
    b = block_size_for(w, block_override)
    gray = rgb.astype(np.float32).mean(axis=2)

    # --- 判据一：块化 ---
    if b <= 1:
        # 图宽不足 RT 宽（<960 大致），块网格退化，无法判块化
        aligned = block_mad(gray, 1)
        ratio = float("nan")
        flat_frac = flat_block_fraction(rgb, 1)
        block_note = "块退化(图宽<RT宽)"
    else:
        grid = [[block_mad(gray, b, oy, ox) for ox in range(b)] for oy in range(b)]
        aligned = grid[0][0]
        others = [grid[y][x] for y in range(b) for x in range(b) if not (x == 0 and y == 0)]
        offset_mean = float(np.mean(others))
        ratio = float(aligned / offset_mean) if offset_mean > 0 else float("nan")
        flat_frac = flat_block_fraction(rgb, b)
        block_note = ""

    # --- 判据二：洋红 ---
    r = rgb[:, :, 0].astype(np.int16)
    g = rgb[:, :, 1].astype(np.int16)
    bl = rgb[:, :, 2].astype(np.int16)
    magenta = int(((r > 230) & (bl > 230) & (g < 80)).sum())

    # --- 判据三：唯一色数（打包 uint32 去重，比 axis=0 unique 快得多）---
    keys = (r.astype(np.uint32) << 16) | (g.astype(np.uint32) << 8) | bl.astype(np.uint32)
    uniq = int(np.unique(keys).size)

    # --- 判据四：墨色覆盖 ---
    mx = np.maximum(np.maximum(r, g), bl)
    mn = np.minimum(np.minimum(r, g), bl)
    ink_frac = float(((mx < 60) & ((mx - mn) < 15)).mean() * 100.0)

    return {
        "path": path,
        "w": w,
        "h": h,
        "b": b,
        "block_mad": aligned,
        "block_ratio": ratio,
        "flat_frac": flat_frac,
        "magenta": magenta,
        "uniq_colors": uniq,
        "ink_pct": ink_frac,
        "block_note": block_note,
    }


# ---------------------------------------------------------------------------
# 判定
# ---------------------------------------------------------------------------

def verdict_block(ratio, b):
    if b <= 1 or ratio != ratio:  # nan
        return VERDICT_INFO
    if ratio <= BLOCK_RATIO_PASS:
        return VERDICT_PASS
    if ratio >= BLOCK_RATIO_FAIL:
        return VERDICT_FAIL
    return VERDICT_WARN


def verdict_magenta(count):
    if count >= MAGENTA_FAIL:
        return VERDICT_FAIL
    if count >= MAGENTA_WARN:
        return VERDICT_WARN
    return VERDICT_PASS


# ---------------------------------------------------------------------------
# 输出
# ---------------------------------------------------------------------------

def _disp_width(s):
    """按终端显示宽度计（东亚宽字符算 2 列），保证中文路径对得齐。"""
    return sum(2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1 for ch in s)


def _pad(s, width, align="<"):
    pad = max(0, width - _disp_width(s))
    if align == ">":
        return " " * pad + s
    return s + " " * pad


def _truncate(s, width):
    """按显示宽度截断（超长路径不撑爆表格）。"""
    if _disp_width(s) <= width:
        return s
    out = ""
    for ch in s:
        w = 2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1
        if _disp_width(out) + w > width - 1:
            break
        out += ch
    return out + "…"


COLS = [
    ("图名", 34, "<"),
    ("b", 2, ">"),
    ("块方差", 8, ">"),
    ("块化比", 7, ">"),
    ("全零块比", 8, ">"),
    ("洋红px", 7, ">"),
    ("唯一色数", 9, ">"),
    ("墨色%", 7, ">"),
    ("判定", 4, "<"),
]


def print_table(rows):
    header = " | ".join(_pad(name, wdt, al) for name, wdt, al in COLS)
    print(header)
    print("-" * _disp_width(header))
    for r in rows:
        blocks_v = r["block_ratio"]
        blocks_s = "  n/a" if blocks_v != blocks_v else "%.3f" % blocks_v
        v_block = verdict_block(blocks_v, r["b"])
        v_mag = verdict_magenta(r["magenta"])
        # 单图总判定：核心硬判据（块化 + 洋红）全过才算 PASS；
        # 块网格退化（图宽 < RT 宽）无法判像素化，整图判 n/a 而非 PASS。
        if VERDICT_FAIL in (v_block, v_mag):
            overall = VERDICT_FAIL
        elif VERDICT_WARN in (v_block, v_mag):
            overall = VERDICT_WARN
        elif v_block == VERDICT_INFO:
            overall = "n/a"
        else:
            overall = VERDICT_PASS

        cells = [
            _truncate(r["disp"], 34),
            str(r["b"]),
            "%.2f" % r["block_mad"],
            blocks_s,
            "%.3f" % r["flat_frac"],
            str(r["magenta"]),
            str(r["uniq_colors"]),
            "%.3f" % r["ink_pct"],
            overall,
        ]
        print(" | ".join(_pad(c, COLS[i][1], COLS[i][2]) for i, c in enumerate(cells)))
    print()
    print("注：`块化比` = 对齐网格块方差 / 平移网格块方差均值（1.0=未块化，0.0=完美块化）；"
          "判定阈值 <%.2f 通过、>%.2f 失败。" % (BLOCK_RATIO_PASS, BLOCK_RATIO_FAIL))
    print("    `块方差`/`全零块比` 为 r1 口径参考列（对 JPEG 不具判别力，见脚本头）；"
          "`唯一色数` 与 `墨色%` 为追踪列，不下单图结论。")


def print_summary(rows, root_label):
    if not rows:
        return 0
    print("=" * 72)
    print("汇总：%s（%d 张）" % (root_label, len(rows)))
    print("=" * 72)

    def col(key, fmt="%.3f"):
        vals = [r[key] for r in rows if r[key] == r[key]]
        if not vals:
            return "n/a"
        return "min %s / mean %s / max %s" % (
            fmt % min(vals), fmt % float(np.mean(vals)), fmt % max(vals))

    n_block_pass = sum(1 for r in rows if verdict_block(r["block_ratio"], r["b"]) == VERDICT_PASS)
    n_block_fail = sum(1 for r in rows if verdict_block(r["block_ratio"], r["b"]) == VERDICT_FAIL)
    n_block_warn = sum(1 for r in rows if verdict_block(r["block_ratio"], r["b"]) == VERDICT_WARN)
    n_block_na = sum(1 for r in rows if verdict_block(r["block_ratio"], r["b"]) == VERDICT_INFO)
    n_mag_fail = sum(1 for r in rows if verdict_magenta(r["magenta"]) == VERDICT_FAIL)
    n_mag_warn = sum(1 for r in rows if verdict_magenta(r["magenta"]) == VERDICT_WARN)
    total_mag = sum(r["magenta"] for r in rows)

    print("  块化比        : %s" % col("block_ratio"))
    print("  块方差(r1口径): %s" % col("block_mad", "%.2f"))
    print("  全零块比      : %s" % col("flat_frac"))
    print("  唯一色数      : %s" % col("uniq_colors", "%.0f"))
    print("  墨色%%         : %s" % col("ink_pct"))
    print("  块化判定      : PASS %d / UNCERTAIN %d / FAIL %d / 不可判 %d"
          % (n_block_pass, n_block_warn, n_block_fail, n_block_na))
    print("  洋红判定      : 干净 %d / WARN %d / FAIL %d（总计 %d px）"
          % (len(rows) - n_mag_warn - n_mag_fail, n_mag_warn, n_mag_fail, total_mag))
    print()

    hard_fail = n_block_fail + n_mag_fail
    if total_mag > 0:
        print("  ⚠ 出现洋红像素：先查材质缺失 / shader 报错（read_console）。")
    if n_block_fail:
        bad = [r["disp"] for r in rows if verdict_block(r["block_ratio"], r["b"]) == VERDICT_FAIL]
        print("  ⚠ 未像素化：%s" % ", ".join(_truncate(x, 40) for x in bad))
        print("    若这是反例组（旧图/未开 Feature），符合预期；若是试点正例组则是缺陷。")
    if n_block_pass == len(rows):
        print("  ✓ 全部图片块化判据通过：像素化 RendererFeature 生效，且 UI 未把块化比推过阈值。")
    print("  → 整批结论：%s" % ("FAIL（%d 张硬失败）" % hard_fail if hard_fail else "PASS"))
    return 1 if hard_fail else 0


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def collect_images(target):
    p = Path(target)
    if p.is_file():
        if not p.name.lower().endswith(IMAGE_EXTS):
            raise SystemExit("不是支持的图片格式（需 png/jpg/jpeg）：%s" % target)
        return [p], p.parent
    if p.is_dir():
        found = []
        for dirpath, dirnames, filenames in os.walk(p):
            dirnames.sort()
            for fn in sorted(filenames):
                if fn.lower().endswith(IMAGE_EXTS):
                    found.append(Path(dirpath) / fn)
        return sorted(found), p
    raise SystemExit("路径不存在：%s" % target)


def main(argv=None):
    # 中文路径/文件名在 Windows 控制台默认编码下会翻车，强制 utf-8 输出
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    ap = argparse.ArgumentParser(
        description="赛璐璐 + 反壳描边试点验收判据（块化 / 洋红 / 唯一色数 / 墨色覆盖）",
        formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("target", help="图片目录或单张 png/jpg")
    ap.add_argument("--block", type=int, default=None,
                    help="手工指定块边长（默认按 宽/640 自适应：1920→3、1280→2）")
    args = ap.parse_args(argv)

    images, root = collect_images(args.target)
    if not images:
        raise SystemExit("未找到 png/jpg：%s" % args.target)

    rows = []
    for img in images:
        try:
            info = analyze(img, args.block)
        except Exception as exc:  # 坏图不中断整批
            print("!! 读取失败 %s：%s" % (img, exc))
            continue
        try:
            disp = str(img.relative_to(root))
        except ValueError:
            disp = img.name
        info["disp"] = disp
        rows.append(info)

    if not rows:
        raise SystemExit("无可用图片")

    print()
    print("图片根：%s" % root)
    sizes = sorted({(r["w"], r["b"]) for r in rows})
    print("块边长：%s" % ("手工 %d" % args.block if args.block
                          else "自适应 宽/640（%s）" % ", ".join(
                              "%dw→b%d" % (w_, b_) for w_, b_ in sizes)))
    print()
    print_table(rows)
    print()
    rc = print_summary(rows, str(root))
    return rc


if __name__ == "__main__":
    sys.exit(main())
