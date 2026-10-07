# -*- coding: utf-8 -*-
"""respread_cloud_islands —— 云田散开布局（2026-10-07 裁决）同步关卡 1 可站面。

云田视觉布局已由 translate_cloudfield.py 重排（环1 转 45° 斜方位、半径 6.5→10.5、
环2 椭圆 x16/z11.5），本脚本把 golden 的 10 块可站坛跟到新云位上：
  · 坛 0-3（环1 四朵）= 4 边形方形贴新云心（±3；坛 0 沿用 ±2 旧口径）；
    高度重排：东北 2.5 / 西北 7.5（制高点，蓝队长移驻）/ 西南 6.5 / 东南 2.5。
  · 坛 2（主角云 8 边形）不动——主角云与全部摆位（除蓝队长）不变。
  · 坛 5-10（环2 六朵）= 16 边形贴新云台（公式同 patch_cloud_ring2.py：
    半径 = min(half, 场内收缩) - 0.02），高度 [2.5,5,3,7,8,6]（slot 序）不变。
  · 蓝队长摆位 (21,23) → (12.5,22.5)（西北制高点坛心）。

流程：本脚本改 golden → batchmode MigrateAll（golden→asset）→ 拍图验证。
"""

import json
import math
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GOLDEN = os.path.join(ROOT, "pirate-crew", "Assets", "Data", "Levels", "_golden",
                      "cloud_walk.json")

CX, CZ = 20.0, 15.0
NGON = 16


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


def cloud_center(index, ang_deg, radius_x, radius_z):
    ang = math.radians(ang_deg)
    return (CX + math.cos(ang) * radius_x, CZ + math.sin(ang) * radius_z)


def main():
    with open(GOLDEN, encoding="utf-8") as f:
        data = json.load(f)
    sx, sz = float(data["sizeX"]), float(data["sizeZ"])
    iss = data["terrain"]["islands"]
    assert len(iss) == 11, "期望 11 坛（主角+环1×4+环2×6），实际 %d" % len(iss)

    # ---- 环1 四朵（layout 序：主角 0 + ring1 1..4 → 坛 0,1,3,4；坛 2 = 主角不动）----
    # layout 下标 1..4 对应角度 45/135/225/315；golden 坛槽位沿用旧语义槽（0/1/3/4）。
    ring1 = [
        # (坛槽, layout下标, 角度, topY, 方形半宽)
        (0, 1, 45.0, 2.5, 2.0),   # 东北 低位（原坛0 ±2 口径沿用）
        (1, 4, 315.0, 2.5, 3.0),  # 东南 低位
        (3, 3, 225.0, 6.5, 3.0),  # 西南 中继
        (4, 2, 135.0, 7.5, 3.0),  # 西北 制高点（蓝队长驻）
    ]
    for slot, index, ang_deg, top_y, half_box in ring1:
        index = int(index)  # noqa: F841  layout 下标只参与哈希可读性
        cx, cz = cloud_center(0, ang_deg, 10.5, 10.5)
        iss[slot]["topY"] = num(top_y)
        iss[slot]["outline"] = [num(cx - half_box), num(cz - half_box),
                                num(cx - half_box), num(cz + half_box),
                                num(cx + half_box), num(cz + half_box),
                                num(cx + half_box), num(cz - half_box)]
        print("坛%d → %s 4边形 ±%s topY=%s 心(%.2f,%.2f)"
              % (slot, ["东北", "东南", "西南", "西北"][ [0,1,3,4].index(slot) ], half_box, top_y, cx, cz))

    # ---- 环2 六朵（坛 5-10，slot 序 [0,3,1,4,2,5]，椭圆 x16/z11.5，高度不变）----
    ring2_order = [0, 3, 1, 4, 2, 5]
    ring2_top = [2.5, 5, 3, 7, 8, 6]
    for k in range(6):
        slot = ring2_order[k]
        index = 5 + k
        ang_deg = 30.0 + slot * 60.0
        half = 3.4 + (4.3 - 3.4) * hash01(0, index, 71)
        cx, cz = cloud_center(0, ang_deg, 16.0, 11.5)
        r = min(half, cx - 0.0, sx - cx, cz - 0.0, sz - cz) - 0.02
        outline = []
        for i in range(NGON):
            a = math.pi * 2.0 * i / NGON
            outline.append(num(cx + math.cos(a) * r))
            outline.append(num(cz + math.sin(a) * r))
        iss[5 + k]["topY"] = num(ring2_top[slot])
        iss[5 + k]["outline"] = outline
        print("坛%d → slot%d 16边形 r=%.2f 心(%.2f,%.2f) topY=%s"
              % (5 + k, slot, r, cx, cz, ring2_top[slot]))

    # ---- 蓝队长移驻西北制高点 ----
    for u in data["units"]:
        if u["typeName"] == "cabinBoyCaptain" and u["teamIndex"] == 1:
            print("蓝队长 (%s,%s) → (12.5,22.5)" % (u["x"], u["z"]))
            u["x"] = 12.5
            u["z"] = 22.5

    with open(GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print("DONE 迁移：batchmode LevelDataMigrator.MigrateAll")


if __name__ == "__main__":
    main()
