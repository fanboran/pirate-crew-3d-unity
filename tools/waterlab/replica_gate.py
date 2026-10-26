# -*- coding: utf-8 -*-
"""复刻验证门禁：对 WaterLab 出图跑固定数值探针，对照目标锚点出 pass/fail。

用法:
  python replica_gate.py <capture.png> [--targets targets.json] [--json out.json]

目标锚点默认取主菜单参考口径（浅水水泽）；I8 相机矩阵级对齐后会用
「原作截图 vs 我方出图」的同机位探针重新生成 targets.json 替换默认值。
退出码 0=全过 1=有 fail（可挂 CI/集成脚本）。
"""
import sys, io, json, argparse
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
import numpy as np
from PIL import Image

# 默认目标：值域锚点（历史稳定口径；I8 后由同机位探针重定）
DEFAULT_TARGETS = {
    "sand_mean": {"target": [252, 226, 167], "tol": 12},
    "water_frac": {"min": 0.35, "max": 0.90},
    "deep_band_Y": {"min": 55, "max": 95},     # B-R>105 带亮度（GI/tonemap 落地后收窄）
    "glint_gt60": {"max": 0.030},              # 局部高差>60 占比上限（碎金）
    "band_continuity": {"min_rows": 4},        # 逐行剖面至少 4 个可分辨水色行
}


def lum(px):
    return 0.2126 * px[..., 0] + 0.7152 * px[..., 1] + 0.0722 * px[..., 2]


def analyze(path):
    px = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64)
    H, W, _ = px.shape
    R, G, B = px[..., 0], px[..., 1], px[..., 2]

    water = (B - R > 4) | ((G - R > 4) & (B > 110))
    sand = (R - B > 30) & (R > 170)

    res = {}
    if sand.sum() > 200:
        res["sand_mean"] = [round(v, 1) for v in px[sand].mean(axis=0)]
    res["water_frac"] = round(float(water.mean()), 4)

    if water.sum() > 200:
        d = B - R
        deep = water & (d > 105)
        res["deep_band_Y"] = round(float(lum(px[deep]).mean()), 1) if deep.sum() > 200 else None

        # 局部高差（9x9 中位）——碎金密度
        from PIL import ImageFilter
        L8 = np.clip(lum(px), 0, 255).astype(np.uint8)
        base = np.asarray(Image.fromarray(L8).filter(ImageFilter.MedianFilter(9)), dtype=np.float64)
        res["glint_gt60"] = round(float(((lum(px) - base) > 60)[water].mean()), 4)

        rows = 0
        for r in range(10):
            y0, y1 = int(r * H / 10), int((r + 1) * H / 10)
            m = water[y0:y1]
            if m.mean() > 0.5:
                rows += 1
        res["band_continuity"] = rows
    return res


def gate(meas, targets):
    results = {}
    for key, spec in targets.items():
        v = meas.get(key)
        item = {"measured": v, "spec": spec, "pass": None}
        if v is None:
            item["pass"] = False
            item["why"] = "样本不足"
        elif key == "sand_mean":
            item["pass"] = all(abs(v[i] - spec["target"][i]) <= spec["tol"] for i in range(3))
        elif key == "water_frac":
            item["pass"] = spec["min"] <= v <= spec["max"]
        elif key == "deep_band_Y":
            item["pass"] = spec["min"] <= v <= spec["max"]
        elif key == "glint_gt60":
            item["pass"] = v <= spec["max"]
        elif key == "band_continuity":
            item["pass"] = v >= spec["min_rows"]
        results[key] = item
    return results


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture")
    ap.add_argument("--targets")
    ap.add_argument("--json")
    args = ap.parse_args()

    targets = DEFAULT_TARGETS
    if args.targets:
        targets = json.load(open(args.targets, encoding="utf-8"))

    meas = analyze(args.capture)
    results = gate(meas, targets)
    all_pass = all(r["pass"] for r in results.values())

    for k, r in results.items():
        mark = "PASS" if r["pass"] else "FAIL"
        print(f"  [{mark}] {k:18s} measured={r['measured']}")
    print("GATE:", "PASS" if all_pass else "FAIL")

    if args.json:
        json.dump({"measured": meas, "results": results, "pass": all_pass},
                  open(args.json, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    sys.exit(0 if all_pass else 1)


if __name__ == "__main__":
    main()
