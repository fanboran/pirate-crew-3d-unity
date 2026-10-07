# -*- coding: utf-8 -*-
"""一次性迁移:关卡 golden schema 2(矩形采样数组)→ schema 3(岛形轮廓)。

- 高度分档 → 格边线边界跟随 → 环简化(共线合并)→ 外环/洞分组 → TerrainIsland。
- 自检:even-odd 点包含(与 C# TerrainShapeRasterizer 同语义)栅格化回填,与原 heights 逐格相等才写盘。
- 顶点 2m 整数倍 → 写 int;topY 按值写(整数→int,否则 float)——对齐 C# Number() 的 "整数无小数点" 格式。
"""
import json, sys, io, os

CELL = 2.0
ROOT = 'pirate-crew/Assets/Data/Levels/_golden'
FILES = ['cloud_walk', 'sky_island', 'chem_plant', 'chem_plant_team']


def raster_cells(meters):
    return int(round(meters / CELL))


def point_in_ring(x, z, pts):
    """pts = 顶点列表 [(x, z), ...] 米;even-odd(与 C# PointInPolygon 同语义)。"""
    inside = False
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, zi = pts[i]
        xj, zj = pts[j]
        if (zi > z) != (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi) + xi:
            inside = not inside
        j = i
    return inside


def point_in_island(x, z, island):
    outline = [(island['outline'][i], island['outline'][i + 1])
               for i in range(0, len(island['outline']), 2)]
    if not point_in_ring(x, z, outline):
        return False
    for hole in island.get('holes', []):
        ring = [(hole[i], hole[i + 1]) for i in range(0, len(hole), 2)]
        if point_in_ring(x, z, ring):
            return False
    return True


def simplify(pts):
    """去掉共线中间顶点;入/出都是顶点列表 [(x, z), ...]。"""
    out = []
    n = len(pts)
    for i in range(n):
        a, b, c = pts[i - 1], pts[i], pts[(i + 1) % n]
        cross = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        if cross != 0:
            out.append(b)
    return out


def trace_rings(mask, cell):
    """格边线边界跟随:返回环列表(每环 = 顶点米制列表,方向自洽)。"""
    edges = []  # 有向边 (A, B)
    for (c, r) in mask:
        x0, z0 = c * cell, r * cell
        x1, z1 = x0 + cell, z0 + cell
        if (c, r - 1) not in mask:
            edges.append(((x0, z0), (x1, z0)))
        if (c + 1, r) not in mask:
            edges.append(((x1, z0), (x1, z1)))
        if (c, r + 1) not in mask:
            edges.append(((x1, z1), (x0, z1)))
        if (c - 1, r) not in mask:
            edges.append(((x0, z1), (x0, z0)))

    start_map = {}
    for idx, (a, b) in enumerate(edges):
        start_map.setdefault(a, []).append(idx)

    used = [False] * len(edges)
    rings = []
    for start_idx in range(len(edges)):
        if used[start_idx]:
            continue
        a, b = edges[start_idx]
        used[start_idx] = True
        ring = [a, b]
        cur = b
        prev_dir = (b[0] - a[0], b[1] - a[1])
        while cur != ring[0]:
            candidates = [i for i in start_map.get(cur, []) if not used[i]]
            if not candidates:
                sys.exit('FAIL: 边界跟随中断(环不闭合)')
            if len(candidates) == 1:
                nxt = candidates[0]
            else:
                def turn_key(i):
                    _, nb = edges[i]
                    d = (nb[0] - cur[0], nb[1] - cur[1])
                    cross = prev_dir[0] * d[1] - prev_dir[1] * d[0]
                    dot = prev_dir[0] * d[0] + prev_dir[1] * d[1]
                    # 左转(叉积负,本定向约定下)优先,其次直行,再次右转
                    return (0 if cross < 0 else 1 if dot > 0 else 2)
                nxt = sorted(candidates, key=turn_key)[0]
            _, nb = edges[nxt]
            used[nxt] = True
            prev_dir = (nb[0] - cur[0], nb[1] - cur[1])
            ring.append(nb)
            cur = nb
        ring.pop()  # 闭合点重复
        rings.append(simplify(ring))
    return rings


def group_outer_and_holes(rings):
    """按包含深度分组:深度偶 = 外环,奇 = 洞(挂在包含它的最小外环)。"""
    def contains(outer, inner_pt):
        return point_in_ring(inner_pt[0], inner_pt[1], outer)

    depths = []
    for i, r in enumerate(rings):
        pt = r[0]
        depth = sum(1 for j, o in enumerate(rings) if j != i and contains(o, pt))
        depths.append(depth)

    islands = []
    for i, r in enumerate(rings):
        if depths[i] % 2 == 0:
            islands.append({'outline': r, 'holes': []})
    for i, r in enumerate(rings):
        if depths[i] % 2 == 1:
            pt = r[0]
            host, host_area = None, None
            for j, o in enumerate(rings):
                if depths[j] % 2 == 0 and depths[j] < depths[i] and contains(o, pt):
                    area = abs(sum((o[k][0] * o[k + 1][1] - o[k + 1][0] * o[k][1])
                                   for k in range(-1, len(o) - 1)))
                    if host is None or area < host_area:
                        host, host_area = islands[[idx for idx, ii in enumerate(islands)
                                                   if ii['outline'] is o][0]]['holes'], None
            if host is not None:
                host.append(r)
            else:
                sys.exit('FAIL: 洞环找不到宿主外环')
    return islands


def num(v):
    """对齐 C# Number():整数值无小数点,浮点 repr 最短。"""
    if v == int(v):
        return int(v)
    return v


def migrate(name):
    path = os.path.join(ROOT, name + '.json')
    doc = json.load(io.open(path, encoding='utf-8'))
    assert doc['schema'] == 2, name

    size_x, size_z = doc['terrain']['sizeX'], doc['terrain']['sizeZ']
    cols, rows = raster_cells(size_x), raster_cells(size_z)
    heights = [v for row in doc['terrain']['heights'] for v in row]
    assert len(heights) == cols * rows, name

    islands = []
    for v in sorted(set(h for h in heights if h > 0)):
        mask = {(c, r) for r in range(rows) for c in range(cols) if heights[r * cols + c] == v}
        rings = trace_rings(mask, CELL)
        for grp in group_outer_and_holes(rings):
            islands.append({
                'topY': num(v),
                'outline': [num(x) for pt in grp['outline'] for x in pt],
                'holes': [[num(x) for pt in hole for x in hole] for hole in grp['holes']],
            })

    # 自检:岛形栅格化 == 原 heights(逐格)
    new_heights = []
    for r in range(rows):
        for c in range(cols):
            cx, cz = (c + 0.5) * CELL, (r + 0.5) * CELL
            best = 0.0
            for island in islands:
                if island['topY'] > best and point_in_island(cx, cz, island):
                    best = island['topY']
            new_heights.append(best)
    for i, (a, b) in enumerate(zip(heights, new_heights)):
        if abs(a - b) > 1e-6:
            sys.exit('FAIL %s: 栅格化回填第 %d 格不等(%s vs %s)' % (name, i, a, b))

    new_terrain = {'blockWorldHeight': doc['terrain']['blockWorldHeight'], 'islands': islands}
    out = dict(doc)
    out['schema'] = 3
    # 键序保持:schema..units 不动,terrain 原地替换,bakedPieces 尾随(dict 有序)
    items = []
    for k, v in out.items():
        items.append((k, new_terrain if k == 'terrain' else v))
    out = dict(items)

    text = json.dumps(out, ensure_ascii=False, indent=2) + '\n'
    io.open(path, 'w', encoding='utf-8', newline='').write(text)
    solid = sum(1 for h in heights if h > 0)
    print('%s: %d 岛 / %d 采样格(%d 实心)/ roundtrip 一致' % (name, len(islands), cols * rows, solid))


for f in FILES:
    migrate(f)
print('MIGRATION OK')
