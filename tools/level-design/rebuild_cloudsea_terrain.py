# -*- coding: utf-8 -*-
"""rebuild_cloudsea_terrain —— 「云端漫步」7 座云台 → 关卡 1 的可站面与出生点（只改 golden JSON）。

真源是 tools/blender/scene/cloudsea/cloudsea_common.py 的 PLATFORMS（Blender 本地系）。
本脚本把它投影成 cloud_walk 的 terrain.islands，并把 7 名单位重摆到 South / North 云台：

  · 坐标：Blender(x, y) → Unity 世界 (20 + x, 15 + y)；top_z 即世界顶高 Y，必须是块高的整数倍。
  · 轮廓：16 边形，半径 = min(half_x, half_y) × 0.92——视觉云台边缘向下收 6-13 cm，
    逻辑格不能铺到最外缘。
  · 出生点两条口径都要过：所在 2 m 格实心（BuildBattlePlan 按 floor(x/2) 取格，定站面高度）
    + 点在轮廓内（LevelAssetRules 内容门禁）。栅格语义逐句移植 TerrainShapeRasterizer：
    格中心 even-odd 点包含，多岛取最高。任一不满足即报错退出、不写文件。

PLATFORMS 按字面量解析而非 import：cloudsea_common 顶层 import bpy，系统 Python 下不可导入；
解析字面量同样只有一份数字来源。

流程：本脚本改 golden → batchmode MigrateAll（golden→asset）→ WriteGoldenFromAssets
（asset→golden，C# 权威格式拉直）。空投池 / 武器 / bakedPieces 等其余字段不动。
写前备份到 external/cloudsea-work/：首次 cloud_walk.before.json（此后不再覆盖），之后每次
cloud_walk.prev.json。幂等：目标内容与现状相同时不写、不备份。

用法（仓库根）：python tools/level-design/rebuild_cloudsea_terrain.py [--dry-run]
"""

import argparse
import ast
import collections
import copy
import itertools
import json
import math
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COMMON = os.path.join(ROOT, "tools", "blender", "scene", "cloudsea", "cloudsea_common.py")
GOLDEN = os.path.join(ROOT, "pirate-crew", "Assets", "Data", "Levels", "_golden",
                      "cloud_walk.json")
WORK_DIR = os.path.join(ROOT, "external", "cloudsea-work")
BACKUP_FIRST = os.path.join(WORK_DIR, "cloud_walk.before.json")
BACKUP_LATER = os.path.join(WORK_DIR, "cloud_walk.prev.json")
MIGRATOR = "PirateCrew.EditorTools.LevelDataMigrator"

EXPECTED_PLATFORMS = 7
CX, CZ = 20.0, 15.0          # Blender 本地原点在 Unity 世界的 XZ（竞技场中心）
NGON = 16                    # 轮廓边数
INSET = 0.92                 # 逻辑轮廓半径 / 视觉半宽
CELL = 2.0                   # 采样格边长（= LevelAssetSchema.RasterCellSize）
BLOCK = 0.5                  # 块高（= LevelGeometry.BlockWorldHeight）
EPS = 1e-6
OUTLINE_TOL = 2e-3           # 轮廓中心/半径复核容差（顶点四舍五入到 3 位小数）

SPAWN_RADIUS_MAX = 2.2                # 出生点离云台中心的上限（米）
SPAWN_SPACING_MIN = math.sqrt(2.0)    # 1×1 m 碰撞箱任意朝向都不相交的最小中心距（米）

#: teamIndex → (云台名, 世界偏移 (dx, dz))，偏移顺序对应该队在 units 里的出场顺序。
#: 前排朝向对方（红在南朝 -Z，蓝在北朝 +Z），队长（各队末位）站后排。
#: 偏移取值让每人独占一个实心格，且不落在格边界上。
SPAWN_LAYOUT = {
    0: ("South", [(-1.0, -1.5), (1.0, -1.5), (-1.0, 1.5), (1.0, 1.5)]),
    1: ("North", [(-1.5, 1.5), (1.5, 1.5), (0.0, -2.0)]),
}

Platform = collections.namedtuple("Platform", "name cx cy top_z half_x half_y role")


def fail(message):
    raise SystemExit("ERROR " + message)


def check(problems):
    if problems:
        for problem in problems:
            print("ERROR " + problem, file=sys.stderr)
        sys.exit(1)


def num(x):
    """C# Number 风格：保留 3 位小数，整数值不带小数点。"""
    x = round(float(x), 3)
    return int(x) if abs(x - round(x)) < 1e-9 else x


# ---------------------------------------------------------------------------
# 云台 → 岛形
# ---------------------------------------------------------------------------

def load_platforms():
    with open(COMMON, encoding="utf-8") as f:
        tree = ast.parse(f.read(), COMMON)
    for node in tree.body:
        if isinstance(node, ast.Assign) and any(getattr(t, "id", "") == "PLATFORMS"
                                                for t in node.targets):
            platforms = [Platform(*row) for row in ast.literal_eval(node.value)]
            if len(platforms) != EXPECTED_PLATFORMS:
                fail("PLATFORMS 有 %d 座，应为 %d 座" % (len(platforms), EXPECTED_PLATFORMS))
            return platforms
    fail("%s 里找不到 PLATFORMS" % COMMON)


def world_xz(p):
    # 【2026-11-20 翻号】视觉管线（bakeAxisConversion）Blender y 直通 Unity z，
    # 本表 cy 即 Unity z 偏移——与 mod_clouds/mod_sea 摆位同约定。
    return CX + p.cx, CZ + p.cy


def logic_radius(p):
    return min(p.half_x, p.half_y) * INSET


def build_island(p):
    if abs(p.top_z / BLOCK - round(p.top_z / BLOCK)) > EPS:
        fail("%s 的 top_z=%s 不是块高 %s 的整数倍" % (p.name, p.top_z, BLOCK))
    wx, wz = world_xz(p)
    r = logic_radius(p)
    outline = []
    for i in range(NGON):
        a = math.pi * 2.0 * i / NGON
        outline.append(num(wx + math.cos(a) * r))
        outline.append(num(wz + math.sin(a) * r))
    return {"topY": num(p.top_z), "outline": outline, "holes": []}


def place_units(units, by_name):
    """只改 x/z；typeName/teamIndex/luck/initialWeapons 原样保留。"""
    wanted = sum(len(offsets) for _, offsets in SPAWN_LAYOUT.values())
    for team, (_, offsets) in SPAWN_LAYOUT.items():
        count = sum(1 for u in units if u["teamIndex"] == team)
        if count != len(offsets):
            fail("队伍 %d 有 %d 人，摆位表只备了 %d 个位置" % (team, count, len(offsets)))
    if len(units) != wanted:
        fail("units 有 %d 人，摆位表共 %d 个位置" % (len(units), wanted))

    used = collections.Counter()
    for unit in units:
        team = unit["teamIndex"]
        name, offsets = SPAWN_LAYOUT[team]
        dx, dz = offsets[used[team]]
        used[team] += 1
        wx, wz = world_xz(by_name[name])
        unit["x"] = num(wx + dx)
        unit["z"] = num(wz + dz)


# ---------------------------------------------------------------------------
# 栅格语义（TerrainShapeRasterizer 的移植）
# ---------------------------------------------------------------------------

def point_in_ring(x, z, ring):
    inside = False
    count = len(ring) // 2
    j = count - 1
    for i in range(count):
        xi, zi = ring[i * 2], ring[i * 2 + 1]
        xj, zj = ring[j * 2], ring[j * 2 + 1]
        if (zi > z) != (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi) + xi:
            inside = not inside
        j = i
    return inside


def point_in_island(x, z, island):
    if not point_in_ring(x, z, island["outline"]):
        return False
    holes = island.get("holes") or []
    return not (len(holes) >= 6 and point_in_ring(x, z, holes))


def raster_cells(meters):
    return int(math.floor(meters / CELL + 0.5))


def cell_of(meters):
    return int(math.floor(meters / CELL))


def to_heights(islands, size_x, size_z):
    """行主序高度数组 heights[col + row · cols]（0 = 无地面）。"""
    cols, rows = raster_cells(size_x), raster_cells(size_z)
    heights = []
    for row in range(rows):
        cz = (row + 0.5) * CELL
        for col in range(cols):
            cx = (col + 0.5) * CELL
            best = 0.0
            for island in islands:
                top = float(island["topY"])
                if top > best and point_in_island(cx, cz, island):
                    best = top
            heights.append(best)
    return heights, cols, rows


def solid_cells(island, cols, rows):
    return sum(1 for row in range(rows) for col in range(cols)
               if point_in_island((col + 0.5) * CELL, (row + 0.5) * CELL, island))


def raster_signature(heights):
    """LevelAssetTests.LevelSignature 的 raster 段（改完 golden 后冻结值要跟着重算）。"""
    solid = total = digest = 0
    for i, height in enumerate(heights):
        blocks = round(height / BLOCK)
        if blocks > 0:
            solid += 1
        total += blocks
        digest = (digest + blocks * ((i + 1) % 4294967291)) % 4294967291
    return "solid%d/total%d/digest%d" % (solid, total, digest)


def ring_center_radius(ring):
    xs, zs = ring[0::2], ring[1::2]
    ox, oz = sum(xs) / len(xs), sum(zs) / len(zs)
    return ox, oz, sum(math.hypot(x - ox, z - oz) for x, z in zip(xs, zs)) / len(xs)


# ---------------------------------------------------------------------------
# 校验 / 打印
# ---------------------------------------------------------------------------

def frozen_view(data):
    """除 terrain.islands 与单位 x/z 外的全部字段：这些必须一字不动。"""
    view = copy.deepcopy(data)
    del view["terrain"]["islands"]
    for unit in view["units"]:
        del unit["x"], unit["z"]
    return view


def content_view(data):
    """本脚本负责的内容（C# 写回时省略空 holes，按语义比较）。"""
    return ([(i["topY"], i["outline"], i.get("holes") or []) for i in data["terrain"]["islands"]],
            [(u["x"], u["z"]) for u in data["units"]])


def verify(data, original, platforms):
    """返回问题清单（空 = 通过）。写前查内存数据，写后查从磁盘读回的数据。"""
    problems = []
    if frozen_view(data) != frozen_view(original):
        problems.append("terrain.islands 与单位 x/z 之外的字段被改动了")

    islands = data["terrain"]["islands"]
    if len(islands) != EXPECTED_PLATFORMS:
        problems.append("terrain.islands 数量 %d ≠ %d" % (len(islands), EXPECTED_PLATFORMS))
        return problems

    size_x, size_z = float(data["sizeX"]), float(data["sizeZ"])
    heights, cols, rows = to_heights(islands, size_x, size_z)
    by_name = {p.name: p for p in platforms}

    for p, island in zip(platforms, islands):
        ring = island["outline"]
        wx, wz = world_xz(p)
        if abs(float(island["topY"]) - p.top_z) > EPS:
            problems.append("%s 顶高 %s ≠ PLATFORMS 的 %s" % (p.name, island["topY"], p.top_z))
        if len(ring) != NGON * 2:
            problems.append("%s 轮廓顶点数 %d ≠ %d" % (p.name, len(ring) // 2, NGON))
            continue
        ox, oz, radius = ring_center_radius(ring)
        if math.hypot(ox - wx, oz - wz) > OUTLINE_TOL or abs(radius - logic_radius(p)) > OUTLINE_TOL:
            problems.append("%s 轮廓中心/半径与设计不符：(%.3f,%.3f) r=%.3f" % (p.name, ox, oz, radius))
        if any(v < 0.0 or v > size_x for v in ring[0::2]) \
                or any(v < 0.0 or v > size_z for v in ring[1::2]):
            problems.append("%s 有轮廓顶点落在场地外（LevelAssetRules 会报红）" % p.name)
        if solid_cells(island, cols, rows) == 0:
            problems.append("%s 一个实心格都没有（格中心全落在轮廓外）" % p.name)

    spots = []
    for i, unit in enumerate(data["units"]):
        x, z = float(unit["x"]), float(unit["z"])
        spots.append((x, z))
        tag = "单位 #%d %s (%s, %s)" % (i, unit["typeName"], unit["x"], unit["z"])
        p = by_name[SPAWN_LAYOUT[unit["teamIndex"]][0]]
        wx, wz = world_xz(p)
        if math.hypot(x - wx, z - wz) > SPAWN_RADIUS_MAX + EPS:
            problems.append("%s 离 %s 云台中心超过 %s m" % (tag, p.name, SPAWN_RADIUS_MAX))
        col, row = cell_of(x), cell_of(z)
        if not (0 <= col < cols and 0 <= row < rows):
            problems.append(tag + " 落在场外")
            continue
        top = heights[col + row * cols]
        if top <= 0.0:
            problems.append("%s 所在格 (%d,%d) 顶面为 0，不是实心格" % (tag, col, row))
        elif abs(top - p.top_z) > EPS:
            problems.append("%s 所在格顶高 %s ≠ %s 云台的 %s" % (tag, top, p.name, p.top_z))
        if not any(float(isl["topY"]) > 0.0 and point_in_island(x, z, isl) for isl in islands):
            problems.append(tag + " 不在任何轮廓内（过不了 LevelAssetRules 的 PointOnGround）")

    for (i, a), (j, b) in itertools.combinations(enumerate(spots), 2):
        if math.dist(a, b) < SPAWN_SPACING_MIN:
            problems.append("单位 #%d 与 #%d 中心距 %.3f m < %.3f m，会重叠"
                            % (i, j, math.dist(a, b), SPAWN_SPACING_MIN))
    return problems


def report(data, original, platforms):
    islands = data["terrain"]["islands"]
    size_x, size_z = float(data["sizeX"]), float(data["sizeZ"])
    heights, cols, rows = to_heights(islands, size_x, size_z)
    by_name = {p.name: p for p in platforms}

    print("云台 → terrain.islands（%d 座）" % len(islands))
    for p, island in zip(platforms, islands):
        ox, oz, radius = ring_center_radius(island["outline"])
        print("  %-9s Blender(%6.1f,%6.1f) → 世界中心(%6.2f,%6.2f)  半径 %.3f  顶高 %.1f  实心格 %d"
              % (p.name, p.cx, p.cy, ox, oz, radius, float(island["topY"]),
                 solid_cells(island, cols, rows)))

    print("出生点 → units（%d 人）" % len(data["units"]))
    spots = []
    for i, unit in enumerate(data["units"]):
        x, z = float(unit["x"]), float(unit["z"])
        spots.append((x, z))
        p = by_name[SPAWN_LAYOUT[unit["teamIndex"]][0]]
        wx, wz = world_xz(p)
        col, row = cell_of(x), cell_of(z)
        print("  #%d %-16s team%d %-5s 世界(%6.2f,%6.2f)  距云台心 %.3f  格(%d,%d) 格顶 %s"
              % (i, unit["typeName"], unit["teamIndex"], p.name, x, z,
                 math.hypot(x - wx, z - wz), col, row, heights[col + row * cols]))
    print("最小中心距 %.3f m（下限 %.3f m）；出生点全部在实心格且在轮廓内"
          % (min(math.dist(a, b) for a, b in itertools.combinations(spots, 2)), SPAWN_SPACING_MIN))

    before, _, _ = to_heights(original["terrain"]["islands"], float(original["sizeX"]),
                              float(original["sizeZ"]))
    print("栅格摘要（LevelAssetTests 口径）")
    print("  改前 raster=%s" % raster_signature(before))
    print("  改后 raster=%s" % raster_signature(heights))
    print("  改后 units=%s" % ";".join(
        "%s/%d/%s/%s/%d" % (u["typeName"], u["teamIndex"], num(u["x"]), num(u["z"]), u["luck"])
        for u in data["units"]))


# ---------------------------------------------------------------------------

def backup():
    os.makedirs(WORK_DIR, exist_ok=True)
    target = BACKUP_LATER if os.path.exists(BACKUP_FIRST) else BACKUP_FIRST
    shutil.copyfile(GOLDEN, target)
    return target


def main():
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser(description="7 座云台 → cloud_walk golden 的可站面与出生点")
    parser.add_argument("--dry-run", action="store_true", help="只校验并打印，不写任何文件")
    args = parser.parse_args()

    platforms = load_platforms()
    with open(GOLDEN, encoding="utf-8") as f:
        original = json.load(f)

    if float(original["sizeX"]) / 2.0 != CX or float(original["sizeZ"]) / 2.0 != CZ:
        fail("场地尺幅 %s×%s 的中心不是 (%s, %s)" % (original["sizeX"], original["sizeZ"], CX, CZ))
    if float(original["terrain"]["blockWorldHeight"]) != BLOCK:
        fail("blockWorldHeight=%s，本脚本按 %s 校验" % (original["terrain"]["blockWorldHeight"], BLOCK))

    data = copy.deepcopy(original)
    data["terrain"]["islands"] = [build_island(p) for p in platforms]
    place_units(data["units"], {p.name: p for p in platforms})

    check(verify(data, original, platforms))
    report(data, original, platforms)

    if args.dry_run:
        print("DRY-RUN：未写入任何文件")
    elif content_view(data) == content_view(original):
        print("IN SYNC：golden 已是目标内容，未写入、未备份")
    else:
        backup_path = backup()
        with open(GOLDEN, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
            f.write("\n")
        with open(GOLDEN, encoding="utf-8") as f:
            written = json.load(f)
        check(verify(written, original, platforms))
        print("WROTE %s（terrain.islands = %d）" % (GOLDEN, len(written["terrain"]["islands"])))
        print("BACKUP %s" % backup_path)

    print("NEXT（本脚本不跑 Unity；一次冷启动串完 MigrateAll + WriteGoldenFromAssets）：")
    print('  tools/headless/run.sh chain "%s.MigrateAll;%s.WriteGoldenFromAssets"'
          % (MIGRATOR, MIGRATOR))


if __name__ == "__main__":
    main()
