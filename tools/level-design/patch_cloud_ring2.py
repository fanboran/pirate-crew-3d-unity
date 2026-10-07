# -*- coding: utf-8 -*-
"""patch_cloud_ring2 —— 云场第二环 6 朵补进关卡 1 的可站面（terrain 段）。

背景：旧云田视觉（CloudFieldGeometry.Default）是 11 朵环形 blob 云，地形数据只覆盖
5 坛（主角云 + 第一环 4），第二环 6 朵「看着能站、实际会掉」。用户定夺：把第二环
做成可站立。本脚本照云田布局参数（与 translate_cloudfield.py 同一 Layout/哈希源）
为每朵第二环云补一块可站面：16 边形轮廓（半径 = 云台面半径，边界处收缩不出场地）、
顶面高度 = 云顶。

流程：本脚本改 golden → batchmode MigrateAll（golden→asset）→ WriteGoldenFromAssets
（asset→golden，C# 权威格式拉直）。出生摆位 / 武器 / 空投池等其余字段不动。
幂等：已是 11 坛时跳过。
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GOLDEN = os.path.join(ROOT, "pirate-crew", "Assets", "Data", "Levels", "_golden",
                      "cloud_walk.json")

CX, CZ = 20.0, 15.0          # 云场中心（世界）
NGON = 16                    # 轮廓边数（近似圆台面）


def hash01(seed, index, salt):
    h = (seed & 0xFFFFFFFF) * 747796405 \
        + (index & 0xFFFFFFFF) * 2891336453 \
        + (salt & 0xFFFFFFFF) * 1911520717 \
        + 1442695041
    h &= 0xFFFFFFFF
    h ^= h >> 15
    h = (h * 2246822519) & 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 3266489917) & 0xFFFFFFFF
    h ^= h >> 16
    return h * (1.0 / 4294967296.0)


def num(x):
    x = round(float(x), 3)
    return int(x) if abs(x - round(x)) < 1e-9 else x


def main():
    with open(GOLDEN, encoding="utf-8") as f:
        data = json.load(f)
    sx, sz = float(data["sizeX"]), float(data["sizeZ"])
    existing = len(data["terrain"]["islands"])
    if existing >= 11:
        print("SKIP 已是 %d 坛（第二环已可站）" % existing)
        return

    # 第二环布局（照 CloudFieldGeometry.Layout：ring2Order / ring2Top / 半宽哈希同源）
    ring2_order = [0, 3, 1, 4, 2, 5]
    ring2_top = [5, 8, 4, 7, 3, 6]
    added = []
    for k in range(6):
        slot = ring2_order[k]
        index = 5 + k            # layout 列表下标（主角 0 + ring1 1..4）
        ang = math.radians(30.0 + slot * 60.0)
        half = 3.4 + (4.3 - 3.4) * hash01(0, index, 71)
        cx = CX + math.cos(ang) * 11.5
        cz = CZ + math.sin(ang) * 11.5
        # 半径：云台面半径 ≈ half；场内收缩（轮廓顶点不得出场地）
        r = min(half, cx - 0.0, sx - cx, cz - 0.0, sz - cz) - 0.02
        outline = []
        for i in range(NGON):
            a = math.pi * 2.0 * i / NGON
            outline.append(num(cx + math.cos(a) * r))
            outline.append(num(cz + math.sin(a) * r))
        data["terrain"]["islands"].append({"topY": num(ring2_top[slot]),
                                           "outline": outline, "holes": []})
        added.append("slot%d top=%s r=%.2f c=(%.2f,%.2f)" % (slot, ring2_top[slot], r, cx, cz))

    with open(GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print("PATCHED %d → %d 坛：%s" % (existing, existing + 6, "; ".join(added)))
    print("NEXT batchmode：LevelDataMigrator.MigrateAll → WriteGoldenFromAssets 拉直格式")


if __name__ == "__main__":
    main()
