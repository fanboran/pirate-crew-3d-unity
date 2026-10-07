# -*- coding: utf-8 -*-
"""sync_island_terrain —— Blender 岛体站面快照 → golden json terrain 段(单向同步)。

关卡数据三层(golden JSON ↔ SO asset ↔ runtime)中,terrain 段的真源升级为 Blender 岛体件:
build 脚本建模时随件导出 external/islands-work/<名>.terrain.json(islands 表),
本工具把它回填进 golden 的 terrain 段——**出生摆位/武器/空投池等其余字段原样保留,不经本管线**。

首跑(Blender 建模输入即 golden 轮廓)应报 IN SYNC、零改动——这是「视觉与数据钉在同一根轴」
的判据。此后 Blender 件形变(岛缘修饰改了可站边界)时重跑本工具 → 重烘 asset
(batchmode MigrateAll)→ WriteGoldenFromAssets 由 C# 把 golden 格式拉直。

用法(仓库根):
    python tools/level-design/sync_island_terrain.py cloud_walk sky_island [--apply]
默认只校验(IN SYNC / DRIFT);--apply 才回填。
"""

import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WORK_DIR = os.path.join(ROOT, "external", "islands-work")
GOLDEN_DIR = os.path.join(ROOT, "pirate-crew", "Assets", "Data", "Levels", "_golden")
TOL = 1e-6


def num(x):
    """C# Number 风格:整数值不带小数点。"""
    x = round(float(x), 3)
    return int(x) if abs(x - round(x)) < 1e-9 else x


def islands_equal(a, b):
    if len(a["islands"]) != len(b["islands"]):
        return False
    for ia, ib in zip(a["islands"], b["islands"]):
        if abs(ia["topY"] - ib["topY"]) > TOL:
            return False
        for key in ("outline", "holes"):
            ra = ia.get(key) or []
            rb = ib.get(key) or []
            if len(ra) != len(rb) or any(abs(x - y) > TOL for x, y in zip(ra, rb)):
                return False
    return True


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    apply = "--apply" in sys.argv
    ok = True
    for name in args:
        golden_path = os.path.join(GOLDEN_DIR, name + ".json")
        snap_path = os.path.join(WORK_DIR, name + ".terrain.json")
        with open(golden_path, encoding="utf-8") as f:
            golden = json.load(f)
        with open(snap_path, encoding="utf-8") as f:
            snap = json.load(f)

        if islands_equal(golden["terrain"], snap):
            print("IN SYNC %s" % name)
            continue

        ok = False
        print("DRIFT %s（golden terrain ≠ Blender 快照）" % name)
        if not apply:
            continue
        golden["terrain"]["islands"] = [
            {
                "topY": num(isl["topY"]),
                "outline": [num(v) for v in isl["outline"]],
                "holes": [[num(v) for v in h] for h in isl.get("holes") or []],
            }
            for isl in snap["islands"]
        ]
        with open(golden_path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(golden, f, ensure_ascii=False, indent=1)
            f.write("\n")
        print("APPLIED %s（记得 batchmode 重烘 asset + WriteGoldenFromAssets 拉直格式）" % name)

    sys.exit(0 if ok or apply else 1)


if __name__ == "__main__":
    main()
