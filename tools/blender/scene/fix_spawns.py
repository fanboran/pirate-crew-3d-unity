# -*- coding: utf-8 -*-
"""
fix_spawns —— 按站面 manifest 自动修正 WorldMapCatalog.cs 里的出生点坐标。

逻辑：展开每张图的世界系站面 box（Unity (X,Z)=(Blender X, −Blender Y)，yaw 同值）；
出生点若已在「最高覆盖 box」内且离边 ≥ SpawnEdgeMargin 则保留；否则在半径 8u 内
找「顶高 ≤ 4.0（不上灯塔顶/瞭望斗）」的 box，把点钳进其内缩 1.5u 的矩形，取最近者。

用法（仓库根）：python tools/blender/scene/fix_spawns.py
输出：每张图修正后的 new WorldMapSpawn(...) 行（直接替换目录里的对应块）。
"""

import json
import math
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
CATALOG = os.path.join(ROOT, "pirate-crew", "Assets", "Scripts", "PirateCrew", "Battle",
                       "WorldMaps", "WorldMapCatalog.cs")
WORLDKIT = os.path.join(ROOT, "pirate-crew", "Assets", "Art", "Models", "WorldKit")

MARGIN = 1.2
SHRINK = 1.5
MAX_TOP = 4.0
SEARCH_RADIUS = 8.0


def load_manifests():
    manifests = {}
    for path in glob_all():
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        boxes = []
        for b in d["boxes"]:
            c, s = b["c"], b["s"]
            boxes.append((c[0], -c[1], s[0], s[1], c[2] + s[2] / 2.0, float(b.get("yaw", 0.0))))
        manifests[d["asset"]] = boxes
    return manifests


def glob_all():
    out = []
    for kit in os.listdir(WORLDKIT):
        kdir = os.path.join(WORLDKIT, kit)
        if os.path.isdir(kdir):
            import glob as g
            out.extend(g.glob(os.path.join(kdir, "*.standable.json")))
    return out


def expand(placement, manifests):
    _, asset, px, pz, pyaw = placement
    world = []
    for (cx, cz, sx, sz, top, yaw) in manifests[asset]:
        rad = math.radians(pyaw)
        wx = px + cx * math.cos(rad) + cz * math.sin(rad)
        wz = pz + -cx * math.sin(rad) + cz * math.cos(rad)
        world.append((wx, wz, sx, sz, top, pyaw + yaw))
    return world


def box_local(box, x, z):
    """点到 box 局部系（Unity 正变换 (x,z)→(x·cos+z·sin, −x·sin+z·cos) 的逆）。"""
    _, _, sx, sz, _, yaw = box
    rad = math.radians(yaw)
    dx, dz = x - box[0], z - box[1]
    lx = dx * math.cos(rad) - dz * math.sin(rad)
    lz = dx * math.sin(rad) + dz * math.cos(rad)
    return lx, lz


def edge_clearance(box, x, z):
    """box 内部净空（到最近边的距离；box 外为负）。"""
    lx, lz = box_local(box, x, z)
    return min(box[2] / 2.0 - abs(lx), box[3] / 2.0 - abs(lz))


def distance_to_box(box, x, z):
    lx, lz = box_local(box, x, z)
    dx = max(abs(lx) - box[2] / 2.0, 0.0)
    dz = max(abs(lz) - box[3] / 2.0, 0.0)
    return math.hypot(dx, dz)


def clamp_into_box(box, x, z, shrink):
    lx, lz = box_local(box, x, z)
    hx, hz = box[2] / 2.0 - shrink, box[3] / 2.0 - shrink
    lx = max(-hx, min(hx, lx))
    lz = max(-hz, min(hz, lz))
    rad = math.radians(box[5])
    wx = box[0] + lx * math.cos(rad) + lz * math.sin(rad)
    wz = box[1] - lx * math.sin(rad) + lz * math.cos(rad)
    return wx, wz


def main():
    manifests = load_manifests()
    with open(CATALOG, encoding="utf-8") as f:
        text = f.read()

    map_re = re.compile(r'id: "([a-z_]+)", displayName')
    placement_re = re.compile(
        r'new WorldKitPlacement\("(\w+)", "(\w+)", ([\d.]+)f?, ([\d.]+)f?, ([\d.-]+)f?\)')
    spawn_re = re.compile(
        r'new WorldMapSpawn\((\d), "(\w+)", ([\d.]+)f?, ([\d.]+)f?, (\d+)\)')

    # 按地图切块
    ids = [(m.start(), m.group(1)) for m in map_re.finditer(text)]
    ids.append((len(text), None))

    for k in range(len(ids) - 1):
        start, map_id = ids[k]
        end = ids[k + 1][0]
        block = text[start:end]
        placements = [(m.group(1), m.group(2), float(m.group(3)), float(m.group(4)), float(m.group(5)))
                      for m in placement_re.finditer(block)]
        spawns = [(int(m.group(1)), m.group(2), float(m.group(3)), float(m.group(4)), int(m.group(5)), m)
                  for m in spawn_re.finditer(block)]
        if not spawns:
            continue

        boxes = []
        for p in placements:
            if p[1] not in manifests:
                continue  # 远景/纯视觉件无站面
            boxes.extend(expand(p, manifests))

        print("### %s" % map_id)
        for (team, arch, x, z, luck, m) in spawns:
            # 最高覆盖 box
            best = None
            for b in boxes:
                if b[4] <= MAX_TOP + 3.5:  # 全部 box 都参与“承载”判定（含高台），但修正候选限 MAX_TOP
                    lx, lz = box_local(b, x, z)
                    if abs(lx) <= b[2] / 2.0 and abs(lz) <= b[3] / 2.0:
                        if best is None or b[4] > best[4]:
                            best = b
            ok = best is not None and edge_clearance(best, x, z) >= MARGIN
            if ok:
                print("  keep %s (%.1f, %.1f) top=%.2f" % (arch, x, z, best[4]))
                continue
            # 修正：半径内最近的合法 box（顶 ≤ MAX_TOP）
            cand = None
            for b in boxes:
                if b[4] > MAX_TOP or min(b[2], b[3]) / 2.0 < MARGIN + 0.3:
                    continue
                lx, lz = box_local(b, x, z)
                # 点到内缩矩形的近似距离
                dx = max(abs(lx) - (b[2] / 2.0 - SHRINK), 0.0)
                dz = max(abs(lz) - (b[3] / 2.0 - SHRINK), 0.0)
                dist = math.hypot(dx, dz)
                if dist < SEARCH_RADIUS and (cand is None or dist < cand[0] or
                                             (abs(dist - cand[0]) < 1e-6 and b[4] > cand[1][4])):
                    cand = (dist, b)
            if cand is None:
                print("  FAIL %s (%.1f, %.1f): 半径内无合法站面" % (arch, x, z))
                continue
            nx, nz = clamp_into_box(cand[1], x, z, SHRINK)
            print('  fix  %s (%.1f, %.1f) -> (%.2f, %.2f) top=%.2f'
                  % (arch, x, z, nx, nz, cand[1][4]))


if __name__ == "__main__":
    main()
