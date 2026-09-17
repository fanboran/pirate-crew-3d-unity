#!/usr/bin/env python3
"""世界地图实拍图的程序化判据（判图第一步，人眼终审之前的硬门禁）。

用法:
    python tools/worldmap/judge_worldmap_captures.py export/worldmap-captures
    python tools/worldmap/judge_worldmap_captures.py export/worldmap-captures/wreck_hymn

口径来源：docs/审计/地图设计审计报告.md §二（视觉与表现层）与 §四.3
（"布局改动必须按现行参数重拍 + 程序化判据（海窗蓝占比、灰盒可见比、出生机位遮挡比）
+ 人眼验收，不允许改了没看图"）。三个判据在此落地为可复算的像素口径：

  ① 洋红占比   —— 缺 shader/材质（Unity 品红）的硬门禁，任何一张 >0.05% 即阻断。
  ② 内容占比   —— 画面里"既不是天、也不是海"的像素比例。审计实测修复前约 25%，
                  这是"空尺度"（图面大部分是死水）的直接读数。
  ③ 出生机位遮挡比 —— team*-spawn 图**画面中央横带**里被暗色/低饱和大块几何占据的比例。
                  审计 §二.7 的"2/3 被 kit 弧段内侧巨墙占据"就是这个数。
  ④ 海窗蓝占比 —— 海面像素占非天空部分的比例，用来核"海像不像海"。

判据只回答"画面里有多少什么"，不回答"好不好看"——后者必须人眼看图。
"""
import sys
import glob
import os

import numpy as np
from PIL import Image

# --- 判据阈值（【提案/待定】：实拍验收后按用户裁决定稿）------------------------
MAGENTA_MAX = 0.0005          # 洋红占比上限
LAND_MIN_OVERHEAD = 0.08      # 俯视图"陆地色"占比下限（修复前实测 0.0-18%，多图 <8%）
CONTENT_MIN_PANO = 0.22       # 全景图内容占比下限（修复前实测 3.5-23.4%）
OCCLUSION_MAX_SPAWN = 0.45    # 出生机位中央横带遮挡比上限（修复前实测 53-80%）


def _masks(rgb):
    """把画面切成天空 / 海 / 陆地三类（HSV 口径，纯 numpy 实现）。"""
    a = rgb.astype(np.float32) / 255.0
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    val = mx

    # 洋红：R、B 高、G 低（Unity 缺材质色）
    magenta = (r > 0.75) & (b > 0.75) & (g < 0.35)

    # 天空：偏蓝青、亮、低饱和到中饱和，且 B ≥ R（本项目正午天空是青蓝渐变）
    sky = (val > 0.55) & (b >= r - 0.02) & (sat < 0.55) & ~magenta

    # 海：蓝青主导、亮度中等（水面比天空暗，且常带绿松石/深蓝）
    sea = (b > r + 0.04) & (val <= 0.72) & (sat > 0.10) & ~magenta & ~sky

    # 陆地：站面三档色板（沙 #C4A76A / 草 #4A8C4A / 岩 #8C7B6A）与 kit 上的植被——
    # 共性是 **R ≥ B 且带饱和**（暖色）或 **G 明显高于 B**（植被）。这条口径刻意排除
    # "灰蓝且低饱和"的矩形海床板（R≈B、sat 很低），否则正俯视会把整块底板记成陆地
    # （审计 §二.5 的穿帮物，不是内容）。
    warm = (r >= b + 0.04) & (sat > 0.12) & (val > 0.22)
    veg = (g > b + 0.03) & (sat > 0.12)
    land = (warm | veg) & ~magenta

    return sky, sea, magenta, land, val, sat


def judge(path):
    img = Image.open(path).convert("RGB")
    rgb = np.asarray(img)
    h, w, _ = rgb.shape
    sky, sea, magenta, land, val, sat = _masks(rgb)

    name = os.path.basename(path)
    # 出生机位遮挡比：画面中央横带（高 40%-75%）里"暗且低饱和"的像素（巨墙/岩壁/船体）
    band = slice(int(h * 0.40), int(h * 0.75))
    dark = (val[band] < 0.42) & (sat[band] < 0.35)
    occl = float(dark.mean())

    return {
        "file": name,
        "w": w, "h": h,
        "sky": float(sky.mean()),
        "sea": float(sea.mean()),
        "land": float(land.mean()),
        "content": 1.0 - float(sky.mean()) - float(sea.mean()) - float(magenta.mean()),
        "magenta": float(magenta.mean()),
        "occlusion": occl,
        "mean_val": float(val.mean()), "mean_sat": float(sat.mean()),
    }


def verdict(m):
    """返回 (是否通过, 失败原因列表)。

    【为什么只拿两条当硬门禁】洋红与出生机位遮挡比是**自证**的判据（缺材质必然刺眼、
    机位被巨物糊满必然看不清）；而"内容占比/陆地占比"这两条在现役场景里被**矩形海床板**
    污染——底板是一整块暖色平面，既被记成"内容"又被记成"陆地"（审计 §二.5 的穿帮物，
    退役排在波 2）。所以它们只报数不判死；"空尺度"的硬门禁放在 harness 的
    WorldMapPropPlacementTests（站面覆盖 ≥15% / 内容跨度 ≥55%），那是数据层的真值，
    不受渲染污染。
    """
    bad = []
    name = m["file"]
    if m["magenta"] > MAGENTA_MAX:
        bad.append("洋红 %.3f%%（缺材质）" % (m["magenta"] * 100))
    if "spawn" in name and m["occlusion"] > OCCLUSION_MAX_SPAWN:
        bad.append("出生机位遮挡 %.1f%% > %.0f%%（面壁）"
                   % (m["occlusion"] * 100, OCCLUSION_MAX_SPAWN * 100))
    return (not bad), bad


def notes(m):
    """非阻断的观察项（报数 + 提示，不影响退出码）。"""
    out = []
    name = m["file"]
    if "overhead" in name and m["land"] < LAND_MIN_OVERHEAD:
        out.append("俯视陆地 %.1f%%（<%.0f%%）" % (m["land"] * 100, LAND_MIN_OVERHEAD * 100))
    if "pano" in name and m["content"] < CONTENT_MIN_PANO:
        out.append("全景内容 %.1f%%（<%.0f%%）" % (m["content"] * 100, CONTENT_MIN_PANO * 100))
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    root = sys.argv[1]
    if os.path.isdir(root):
        pngs = sorted(glob.glob(os.path.join(root, "**", "*.png"), recursive=True))
    else:
        pngs = [root]
    if not pngs:
        print("没找到 PNG：", root)
        return 2

    print("%-34s %7s %7s %7s %7s %8s %8s" %
          ("file", "sky%", "sea%", "land%", "cont%", "magenta%", "occl%"))
    failures = []
    notes_all = []
    for p in pngs:
        m = judge(p)
        tag = os.path.relpath(p, os.path.dirname(root) if os.path.isdir(root) else ".")
        print("%-34s %6.1f%% %6.1f%% %6.1f%% %6.1f%% %7.3f%% %7.1f%%" % (
            os.path.basename(os.path.dirname(p)) + "/" + m["file"],
            m["sky"] * 100, m["sea"] * 100, m["land"] * 100, m["content"] * 100,
            m["magenta"] * 100, m["occlusion"] * 100))
        ok, bad = verdict(m)
        if not ok:
            failures.append((tag, bad))
        n = notes(m)
        if n:
            notes_all.append((tag, n))

    print()
    if notes_all:
        print("观察项 %d 条（不阻断，口径见 verdict 的 docstring）：" % len(notes_all))
        for tag, n in notes_all:
            print("  ~", tag, "；".join(n))
        print()
    if failures:
        print("判据未过 %d 张：" % len(failures))
        for tag, bad in failures:
            print("  -", tag, "；".join(bad))
        return 1
    print("判据全过（%d 张，洋红=0 且出生机位不面壁）——下一步：人眼终审。" % len(pngs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
