#!/usr/bin/env python3
"""天空盒环境光三档实拍图的程序化判据（人眼终审前的初筛）。

用法:
    python tools/ambient/judge_ambient_captures.py export/ambient-skybox-debug
    python tools/ambient/judge_ambient_captures.py export/ambient-skybox-debug/noon

目录约定（与 tools/worldmap/judge_worldmap_captures.py 同风格）：
    export/ambient-skybox-debug/<tier>/world-*.png|jpg   tier ∈ {noon, dusk, overcast}

口径来源：docs/环境光天空盒化-预研与接线清单.md §四（三档参数表【提案/待定】）与
docs/审计/视觉审计报告.md §二.4（环境光是"画面假"根因之一）。判据回答的是
"三档是不是真的分得开、方向对不对"，不回答"好不好看"——后者必须人眼看图：

  ① 洋红占比      —— 缺 shader/材质硬门禁，任何一张 >0.05% 即阻断（同 worldmap 判据）。
  ② 天空非黑      —— horizon 机位画面上带（上 35%）平均亮度必须 >0.15：天空盒 Pass 若被
                     静默丢弃（描边 Pass 有过同族事故），背景会是纯色/黑，这条最先抓到。
  ③ 三档分得开    —— 全图平均亮度必须严格单调 noon > dusk > overcast（预设曝光/天顶亮度
                     的单调性在实拍里的读数）。
  ④ 暖冷方向      —— dusk 的地平线带必须偏暖（R 通道均值 > B 通道均值）；noon 的天顶带
                     必须偏冷（B ≥ R）。渐变方向装反（倒扣碗）会在这里现形。
  ⑤ 阴云无太阳盘  —— overcast 不允许出现成片过曝亮斑（>245 灰度且成簇）；noon/dusk 的
                     horizon 图必须有太阳盘亮斑（强度 >1 喂 Bloom 的设计预期）。
  ⑥ 阴云低饱和    —— overcast 天顶带 RGB 通道极差必须明显小于 noon（铅灰 vs 干净蓝）。

阈值为【提案/待定】：首轮实拍后按人眼结论校准，别把初筛阈值当美术标准。
"""
import sys
import glob
import os

import numpy as np
from PIL import Image

# --- 阈值（【提案/待定】）-----------------------------------------------------
MAGENTA_MAX = 0.0005       # ① 洋红占比上限
SKY_LUM_MIN = 0.15         # ② 天空上带平均亮度下限（黑背景探测）
SUN_BRIGHT_MIN = 245       # ⑤ 太阳盘/过曝亮斑的灰度下限
SUN_PIXELS_MIN = 30        # ⑤ 成簇亮斑的最少像素（1280 宽图）
SUN_PIXELS_MAX_OVERCAST = 30  # ⑤ overcast 允许的亮斑像素上限
SKY_BAND = 0.35            # "天空上带" = 画面顶部这一比例（horizon 机位天空在上缘）
HORIZON_WARM_BAND = (0.30, 0.50)  # ④ 地平线带 = 高度方向的这个比例区间（上带之内靠下）

SHOTS = ("world-pano", "world-overhead", "world-horizon",
         "world-team0-spawn", "world-team1-spawn")


def load(path):
    img = Image.open(path).convert("RGB")
    return np.asarray(img).astype(np.float32) / 255.0


def magenta_ratio(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return float(np.mean((r > 0.55) & (b > 0.55) & (g < 0.25)))


def band(a, top, bottom):
    h = a.shape[0]
    return a[int(h * top):int(h * bottom)]


def analyze(tier, path):
    a = load(path)
    h, w, _ = a.shape
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * b

    sky = band(a, 0.0, SKY_BAND)
    sky_lum = float((0.2126 * sky[..., 0] + 0.7152 * sky[..., 1]
                     + 0.0722 * sky[..., 2]).mean())
    sky_spread = float((sky.max(axis=-1) - sky.min(axis=-1)).mean())

    warm = band(a, *HORIZON_WARM_BAND)

    bright = int(np.mean(a, axis=-1).__gt__(SUN_BRIGHT_MIN / 255.0).sum())

    return {
        "tier": tier,
        "file": os.path.basename(path),
        "magenta": magenta_ratio(a),
        "mean_lum": float(lum.mean()),
        "sky_lum": sky_lum,
        "sky_spread": sky_spread,
        "sky_r": float(sky[..., 0].mean()),
        "sky_b": float(sky[..., 2].mean()),
        "horizon_warm_r_minus_b": float(warm[..., 0].mean() - warm[..., 2].mean()),
        "sun_pixels": bright,
    }


def checks(m):
    """返回 (该图的 [失败项]，空列表 = 过)。太阳盘单列 advisories（见调用处）。"""
    fails = []
    tag = f"{m['tier']}/{m['file']}"
    if m["magenta"] > MAGENTA_MAX:
        fails.append(f"{tag} 洋红 {m['magenta']:.4%} > {MAGENTA_MAX:.2%}")
    if m["file"].startswith("world-horizon"):
        if m["sky_lum"] <= SKY_LUM_MIN:
            fails.append(f"{tag} 天空上带亮度 {m['sky_lum']:.3f} ≤ {SKY_LUM_MIN}"
                         "（天空盒 Pass 可能被静默丢弃）")
        if m["tier"] == "overcast" and m["sun_pixels"] > SUN_PIXELS_MAX_OVERCAST:
            fails.append(f"{tag} 阴云档出现成片过曝亮斑（{m['sun_pixels']} px）——乌云蔽日不应有盘")
        if m["tier"] == "dusk" and m["horizon_warm_r_minus_b"] <= 0:
            fails.append(f"{tag} 黄昏地平线带不偏暖（R-B={m['horizon_warm_r_minus_b']:.3f}）")
        if m["tier"] == "noon" and m["sky_b"] < m["sky_r"]:
            fails.append(f"{tag} 正午天顶不偏冷（B={m['sky_b']:.3f} < R={m['sky_r']:.3f}）")
    return fails


def advisories(m):
    """提示级（不阻断）：太阳盘是否可见取决于机位是否朝向太阳方位——
    现有 world-horizon 机位不朝太阳（2026-09-18 实测 noon 7px / dusk 1px、盘不在画面内），
    把"没盘"判失败会误报；要判日盘需给 BuildWorldShots 加一个朝阳机位（实拍轮待办）。"""
    notes = []
    if m["file"].startswith("world-horizon") and m["tier"] in ("noon", "dusk"):
        if m["sun_pixels"] < SUN_PIXELS_MIN:
            notes.append(f"{m['tier']}/world-horizon 未见太阳盘亮斑（{m['sun_pixels']} px）——"
                         "机位不朝太阳方位时属预期；判日盘需加朝阳机位")
    return notes


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "export/ambient-skybox-debug"
    tiers = ("noon", "dusk", "overcast")

    rows = []
    failures = []
    notes = []
    for tier in tiers:
        for pattern in ("*.png", "*.jpg"):
            for path in sorted(glob.glob(os.path.join(root, tier, pattern))):
                m = analyze(tier, path)
                rows.append(m)
                failures += checks(m)
                notes += advisories(m)

    if not rows:
        print(f"未找到图片：{root}/<tier>/world-*.png|jpg")
        return 2

    print(f"{'档':10s} {'图':26s} {'洋红':>9s} {'均亮':>6s} {'天带上亮':>8s} "
          f"{'通道极差':>8s} {'R-B地平':>8s} {'亮斑px':>7s}")
    for m in rows:
        print(f"{m['tier']:10s} {m['file']:26s} {m['magenta']:9.4%} {m['mean_lum']:6.3f} "
              f"{m['sky_lum']:8.3f} {m['sky_spread']:8.3f} "
              f"{m['horizon_warm_r_minus_b']:+8.3f} {m['sun_pixels']:7d}")

    # ③ 全图平均亮度严格单调（用三档各自的 horizon 图比——同机位才可比）。
    horizon = {m["tier"]: m["mean_lum"] for m in rows if m["file"].startswith("world-horizon")}
    if len(horizon) == 3:
        if not (horizon["noon"] > horizon["dusk"] > horizon["overcast"]):
            failures.append(
                f"三档亮度不单调：noon={horizon['noon']:.3f} dusk={horizon['dusk']:.3f} "
                f"overcast={horizon['overcast']:.3f}（horizon 机位）")
        else:
            print(f"\n三档亮度单调 ✓  noon={horizon['noon']:.3f} > dusk={horizon['dusk']:.3f} "
                  f"> overcast={horizon['overcast']:.3f}")

    # ⑥ 阴云低饱和：overcast 天顶带通道极差必须明显小于 noon。
    spread = {m["tier"]: m["sky_spread"] for m in rows if m["file"].startswith("world-horizon")}
    if "noon" in spread and "overcast" in spread and not spread["noon"] > spread["overcast"]:
        failures.append(
            f"阴云天顶带饱和度未低于正午（spread noon={spread['noon']:.3f} "
            f"overcast={spread['overcast']:.3f}）——阴云应是铅灰不是干净蓝")

    print()
    for n in notes:
        print("⚠ " + n)
    if notes:
        print()
    if failures:
        print(f"初筛不通过（{len(failures)} 项）：")
        for f in failures:
            print("  ✗ " + f)
        return 1
    print("初筛全部通过 ✓（阈值【提案/待定】，人眼终审另行进行）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
