# -*- coding: utf-8 -*-
"""assemble_chemplant_merged.py —— 废弃化工厂「两关合并」总装 + FBX 导出 + 成品图。

合并口径（创始人 2026-11-20 裁决）：
    · 原第 3 关（单件版 kit，`chemplant_kit.py`）与第 4 关（六件并行版，`mod_*.py`）
      合并为一张关卡；**只保留两边的建筑零件**，场地布局彻底重构（不是两个整场件并排）。
    · 可玩区 96 × 68 m；围墙为不规则折线 + 多处坍塌缺口。
    · 围墙外做**视觉连续、逻辑不可达**的延伸区（墙外道路/远景厂房/树线/土坡/废料山），
      与内部连成一片，不出现「一圈空海」。
    · 废弃质感加强：荒草、枯树、藤蔓、积水、裂缝、瓦砾、翘起板、倒伏电杆与围栏。

产物（重跑幂等覆盖）：
    pirate-crew/Assets/Art/Models/SceneKit/ChemPlantMerged.fbx
    docs/images/chemplant-merged/<机位>.jpg
    external/chemplant-merged-work/merged_debug.blend（gitignored）

复现（仓库根执行，分钟级）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/chemplant/assemble_chemplant_merged.py \
        -- [--no-render] [--no-export] [--samples 32] [--res 1440] [--views hero,overview]

坐标口径：Blender Z-up、1 单位 = 1 米、场地中心 = 原点；+Y 北。
关卡数据坐标 = Blender (x, y) + (48, 34)（可玩区 96×68）。
"""

import importlib
import math
import os
import random
import sys
import time

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kit_common as K                                          # noqa: E402
import chemplant_kit as CK                                      # noqa: E402
import assemble_chemplant as A                                  # noqa: E402
import style_tokens as ST                                       # noqa: E402

T0 = time.time()

# ---------------------------------------------------------------------------
# 参数
# ---------------------------------------------------------------------------

YARD_X, YARD_Y = 96.0, 68.0          # 可玩区（关卡地形尺寸）
GROUND_Z = 0.0                        # 地坪顶面（Unity 摆 y=1.0 后与逻辑地形顶面 1.0 对齐）
OUTSKIRT_Z = -0.08                    # 墙外地面顶面（只比场内低 8 cm，削弱内外分界）

OUT_FBX = os.path.abspath(os.path.join(
    HERE, "..", "..", "..", "..", "pirate-crew", "Assets", "Art", "Models", "SceneKit",
    "ChemPlantMerged.fbx"))
OUT_IMG = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", "docs", "images",
                                       "chemplant-merged"))
WORK_DIR = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", "external",
                                        "chemplant-merged-work"))

SEED = 20261120

#: 单件版构件：名 → (build 调用, 摆位 (x, y), yaw°)
SINGLE = [
    ("Office",        lambda a: CK.build_office(a),                (-32.0,   2.0),   0.0),
    ("Hall",          lambda a: CK.build_hall(a),                  (-12.0,  -3.0),   0.0),
    ("CoolingTower",  lambda a: CK.build_cooling_tower(a),         (-15.0, -20.0),   0.0),
    ("TankFarm",      lambda a: CK.build_tank_farm(a),             (-39.0, -19.0),  90.0),
    ("Stack",         lambda a: CK.build_stack(a),                 (-37.0,  14.0),   0.0),
    ("ColumnTall",    lambda a: CK.build_column(a, tall=True),     ( 38.0,  -6.0),   0.0),
    ("ColumnShort",   lambda a: CK.build_column(a, tall=False),    ( 38.0, -14.0),   0.0),
    ("PipeRack",      lambda a: CK.build_pipe_rack(a),             ( 17.0,  -1.0),   0.0),
    ("SphereTank",    lambda a: CK.build_sphere_tank(a),           ( 40.0,   6.0),   0.0),
    ("PumpSkidA",     lambda a: CK.build_pump_skid(a),             ( 30.0,  -4.0),   0.0),
    ("PumpSkidB",     lambda a: CK.build_pump_skid(a),             (-30.0, -17.0),  90.0),
]

#: 六件版分区件：名 → 摆位 (x, y), yaw°（site 不用：新场地地坪自建）
SIX = [
    ("towers",   (-20.0,  20.0), 0.0),
    ("tanks",    (  6.0,  20.0), 0.0),
    ("building", ( 34.0,  18.0), 0.0),
    ("pipes",    (  6.0,   6.5), 0.0),
]

#: 成品机位：[名, 机位, 视点, 焦距]
VIEWS = [
    ("hero",      ( 62, -74, 58), ( -2,   0,  7), 35),
    ("overview",  (-74, -58, 52), (  2,   0,  8), 35),
    ("north",     (  0,  76, 46), (  0,   0, 10), 32),
    ("street",    ( 10, -14, 2.8), (-30,   8,  6), 30),
    ("south",     ( 40,  70, 40), ( 10,  -4,  8), 35),
    ("top",       (  0,  -8, 128), ( 0, 0, 0), 35),
]


def log(msg):
    print("[merged] %6.1fs %s" % (time.time() - T0, msg), flush=True)


def parse_args():
    opts = {"render": True, "export": True, "samples": 32, "res": 1440, "views": None}
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--no-render":
            opts["render"] = False; i += 1
        elif a == "--no-export":
            opts["export"] = False; i += 1
        elif a == "--samples":
            opts["samples"] = int(argv[i + 1]); i += 2
        elif a == "--res":
            opts["res"] = int(argv[i + 1]); i += 2
        elif a == "--views":
            opts["views"] = argv[i + 1].split(","); i += 2
        else:
            i += 1
    return opts


# ---------------------------------------------------------------------------
# 新几何：地坪 / 道路 / 围墙 / 墙外延伸 / 植被
# ---------------------------------------------------------------------------

#: 可玩区地坪轮廓（不规则 16 边，米；场地中心原点）。凹口是坍塌/缺口处。
YARD_OUTLINE = [
    (-46, -31), (-47, -10), (-44,  8), (-46,  26), (-38,  33),
    (-20,  33), (  2,  32), ( 20,  33), ( 38,  31), ( 47,  22),
    ( 46,   4), ( 48, -12), ( 42, -28), ( 30, -33), (  8, -32),
    (-16, -33), (-34, -31),
]

#: 围墙折线（可玩区边缘内缩；每段独立生成，缺口即无墙段）。
WALL_LINE = [
    (-44.5, -29.5), (-45.5,  -9.0), (-42.5,   8.0), (-44.5,  25.0),
    (-37.0,  31.0), (-20.0,  31.0), (  2.0,  30.0), ( 20.0,  31.0),
    ( 36.5,  29.0), ( 45.0,  21.0), ( 44.0,   4.0), ( 46.0, -11.0),
    ( 40.5, -26.5), ( 29.0, -31.0), (  8.0, -30.0), (-15.0, -31.0),
    (-33.0, -29.5),
]

#: 围墙缺口（段起-段止，段号按 WALL_LINE 相邻两点；缺口处用土坡/废料堆封住视线）。
WALL_GAPS = {(2, 3), (7, 8), (12, 13), (15, 16)}

#: 场内道路中心线 + 宽度（build_ground 的草/斑避让与 build_roads 共用同一份）。
YARD_ROADS = [
    ([(-45.0, -16.0), (-20.0, -15.0), (4.0, -14.0), (26.0, -15.0), (44.0, -14.0)], 6.5),
    ([(-20.0, -15.0), (-10.0, -6.0), (-2.0, 2.0), (6.0, 9.0), (14.0, 14.0)], 5.0),
    ([(30.0, -30.0), (30.0, -16.0), (29.0, -2.0), (28.0, 8.0)], 4.5),
]

#: 完整水泥板矩形 (x0, z0, x1, z1, 顶面 z)；build_ground 写入，草/斑/裂缝按它判“能不能长”。
SLAB_RECTS = []
#: 缺板矩形（土面露出的格子）。
MISSING_RECTS = []


def _slab_top(x, y):
    for x0, z0, x1, z1, top in SLAB_RECTS:
        if x0 <= x <= x1 and z0 <= y <= z1:
            return top
    return None


def _near_slab_edge(x, y, margin=0.28):
    for x0, z0, x1, z1, _ in SLAB_RECTS:
        if x0 - margin <= x <= x1 + margin and z0 - margin <= y <= z1 + margin:
            if not (x0 + margin <= x <= x1 - margin and z0 + margin <= y <= z1 - margin):
                return True
    return False


def _dist_point_seg(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    if L2 < 1e-9:
        return math.hypot(px - ax, py - ay)
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - (ax + dx * t), py - (ay + dy * t))


def _road_dist(x, y):
    """到最近场内道路中心线的距离 - 半宽；<=0 表示在路面上。"""
    best = 1e9
    for pts, width in YARD_ROADS:
        for i in range(len(pts) - 1):
            d = _dist_point_seg(x, y, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1])
            best = min(best, d - width * 0.5)
    return best


def _on_road(x, y, margin=0.0):
    return _road_dist(x, y) <= margin


def _paved_top(x, y):
    """板面/沥青面顶；土面返回 None。"""
    t = _slab_top(x, y)
    if t is not None:
        return t
    if _on_road(x, y, 0.0):
        return GROUND_Z + 0.02
    return None


def _ground_z(x, y):
    """可放置物件的落脚面：完整板顶，或土面（缺板/板缝）。"""
    t = _slab_top(x, y)
    return t if t is not None else GROUND_Z - 0.06


def _grass_z(x, y):
    """草簇的落脚面：板/沥青面 +2 mm，或土面 +5 mm。"""
    t = _paved_top(x, y)
    if t is not None:
        return t + 0.002
    return GROUND_Z - 0.055


def _cuts(lo, hi, rng, step_lo, step_hi):
    """不等距切割线（水泥板分块）：从 lo 到 hi，随机步长。"""
    cuts = [lo]
    x = lo
    while x < hi - step_lo:
        x += rng.uniform(step_lo, step_hi)
        cuts.append(min(x, hi))
    if cuts[-1] < hi:
        cuts.append(hi)
    return cuts


def build_hall_bridge(m):
    """主厂房东山墙三根出墙管 → 单件管廊之间的短管桥。

    原单件 kit 里 `build_hall` 的三根出墙管就是接在场地管廊上的；合并布局把
    管廊移到了东侧（相距约 9 m），不补这一段，三根管就是悬空断头。这里补
    同径三管 + 法兰 + 两座门架支撑，让「管道出墙」这个锚点重新成立。
    """
    y = -3.0 + 2.4                       # 出墙管的世界 y（本地 2.4 + 主厂房 -3.0）
    x0 = -12.0 + CK.HALL_L * 0.5 + 3.4   # -0.6：原出墙管端面
    x1 = 8.8                             # 伸进单件管廊西端（CP_PipeRack x=8.35）
    for z, r in ((5.6, 0.30), (6.3, 0.22), (4.9, 0.16)):
        m.extrude_along(_circle_profile(r, 8), (x0, y, z), (x1, y, z),
                        mat="Kit_Rust", smooth=True)
        for xx in (x0 + 0.12, 3.0, 5.9, x1 - 0.12):
            m.extrude_along(_circle_profile(r * 1.35, 8),
                            (xx - 0.05, y, z), (xx + 0.05, y, z),
                            mat="Kit_Iron")
    # 两座门架：立柱 + 顶梁 + 斜撑（站在场内空地上，管底 4.7 m 不影响通行）
    for sx in (2.2, 6.4):
        for side in (-1.0, 1.0):
            lx, ly = sx, y + side * 0.95
            gz = _ground_z(lx, ly)
            m.box((0.22, 0.22, 7.0), at=(lx, ly, gz), mat="Kit_SteelBlue")
            m.box((0.55, 0.55, 0.30), at=(lx, ly, gz), mat="Kit_ConcreteMid")
            p0 = Vector((lx, ly, gz + 0.35))
            p1 = Vector((sx, y, gz + 6.65))
            _branch(m, p0, p1, 0.055, "Kit_SteelBlue")
        m.box((0.24, 2.14, 0.22), at=(sx, y, _ground_z(sx, y) + 6.75),
              mat="Kit_SteelBlue")


def build_ground(m):
    """可玩区地坪：泥土基面 + 残破水泥板阵列（板缝全长草）+ 大面积草土斑 + 积水。

    氛围口径 = 现实里荒废多年的厂区：水泥不是"一整片干净灰地坪"，而是
    ① 板块之间留缝、缝里全长草；② 18% 的板整块缺失/被土吞；③ **完整板面不撒草**，
    只留薄泥/苔渍与板边杂草（草只从缝、缺板、裂缝、板边、墙脚长）；④ 墙根一线草甸
    与墙外连成同一片荒地。树/灌木脚下另补破板露土的树坑。
    """
    rng = random.Random(SEED + 1)
    SLAB_RECTS.clear()
    MISSING_RECTS.clear()

    # ---- 1) 泥土基面：整块轮廓，顶面比板面低 6 cm（板缝/缺板处露出土）----
    m.poly_extrude(YARD_OUTLINE, GROUND_Z - 0.35, GROUND_Z - 0.06, mat="Kit_SandDark")

    # ---- 2) 水泥板阵列：不等距切割，板间留 0.25~0.75 m 缝 ----
    xs = _cuts(-48.0, 48.0, rng, 3.6, 6.6)
    zs = _cuts(-34.0, 34.0, rng, 3.2, 6.0)
    for i in range(len(xs) - 1):
        for j in range(len(zs) - 1):
            x0, x1 = xs[i], xs[i + 1]
            z0, z1 = zs[j], zs[j + 1]
            cx, cy = (x0 + x1) / 2, (z0 + z1) / 2
            if not point_in_poly(cx, cy, YARD_OUTLINE):
                continue
            gap = rng.uniform(0.35, 1.0)
            w = (x1 - x0) - gap
            d = (z1 - z0) - gap
            if w < 0.6 or d < 0.6:
                MISSING_RECTS.append((x0, z0, x1, z1))
                continue
            if rng.random() < 0.18:          # 整块板缺失（被土/草吞掉）
                MISSING_RECTS.append((x0, z0, x1, z1))
                continue
            mat = rng.choice(("Kit_ConcreteMid", "Kit_ConcreteMid", "Kit_ConcreteMid",
                              "Kit_ConcreteLight", "Kit_ConcreteDark"))
            drop = 0.0
            tilt = 0.0
            if rng.random() < 0.10:          # 下沉/翘板
                drop = rng.uniform(0.01, 0.06)
                tilt = math.radians(rng.uniform(1.0, 4.0))
            m.box((w, d, 0.12), at=(cx, cy, GROUND_Z - 0.12 - drop),
                  rz=rng.uniform(-0.015, 0.015), rx=tilt, mat=mat)
            SLAB_RECTS.append((cx - w * 0.5, cy - d * 0.5,
                               cx + w * 0.5, cy + d * 0.5, GROUND_Z - drop))
            # 板面残破：小锈斑 / 泥斑 / 草斑
            if rng.random() < 0.22:
                rx_ = rng.uniform(0.4, max(0.5, min(2.2, w * 0.32)))
                ry_ = rng.uniform(0.3, max(0.4, min(1.6, d * 0.32)))
                m.poly_extrude(_blob(rng, rx_, ry_, 6), GROUND_Z + 0.004, GROUND_Z + 0.03,
                               at=(cx + rng.uniform(-w * 0.3, w * 0.3),
                                   cy + rng.uniform(-d * 0.3, d * 0.3), 0),
                               mat=_dec(rng.choice(("Kit_SandDark", "Kit_RustDark",
                                                    "Kit_ConcreteDark", "Kit_WetSand",
                                                    "Kit_SandDark"))))

    # ---- 2.5) 板缝深色土条：缝底压暗，突出水泥板分块 ----
    for x in xs[1:-1]:
        z = -33.0
        while z < 33.0:
            seg = 4.0
            if point_in_poly(x, z + seg * 0.5, YARD_OUTLINE):
                m.box((0.85, seg + 0.15, 0.02), at=(x, z + seg * 0.5, GROUND_Z - 0.085),
                      mat="Kit_WetSand")
            z += seg
    for z in zs[1:-1]:
        x = -47.0
        while x < 47.0:
            seg = 4.0
            if point_in_poly(x + seg * 0.5, z, YARD_OUTLINE):
                m.box((seg + 0.15, 0.85, 0.02), at=(x + seg * 0.5, z, GROUND_Z - 0.085),
                      mat="Kit_WetSand")
            x += seg

    # ---- 3) 板缝草带：沿每条切割线撒矮草簇（只长在缝/缺板里；路面上不长）----
    for x in xs[1:-1]:
        z = -33.0
        while z < 33.0:
            z += rng.uniform(0.65, 1.25)
            gx, gy = x + rng.uniform(-0.3, 0.3), z
            if not point_in_poly(gx, gy, YARD_OUTLINE) or _on_road(gx, gy, 0.25):
                continue
            _grass_tuft(m, gx, gy, rng.uniform(0.32, 0.62), rng,
                        n=rng.randint(3, 5), z=_grass_z(gx, gy))
    for z in zs[1:-1]:
        x = -47.0
        while x < 47.0:
            x += rng.uniform(0.65, 1.25)
            gx, gy = x, z + rng.uniform(-0.3, 0.3)
            if not point_in_poly(gx, gy, YARD_OUTLINE) or _on_road(gx, gy, 0.25):
                continue
            _grass_tuft(m, gx, gy, rng.uniform(0.32, 0.62), rng,
                        n=rng.randint(3, 5), z=_grass_z(gx, gy))

    # ---- 3.5) 缺板处：土面裸露 + 密草（草只从没有水泥的地方长出来）----
    for (x0, z0, x1, z1) in MISSING_RECTS:
        cx, cy = (x0 + x1) * 0.5, (z0 + z1) * 0.5
        if not point_in_poly(cx, cy, YARD_OUTLINE):
            continue
        m.poly_extrude(_blob(rng, (x1 - x0) * 0.45, (z1 - z0) * 0.45, 9),
                       GROUND_Z - 0.06, GROUND_Z - 0.03,
                       at=(cx, cy, 0),
                       mat=_dec(rng.choice(("Kit_SandDark", "Kit_GrassDark", "Kit_SandDark",
                                            "Kit_GrassDark", "Kit_WetSand"))))
        n = max(2, int((x1 - x0) * (z1 - z0) / 2.2))
        for k in range(n):
            gx = rng.uniform(x0, x1)
            gy = rng.uniform(z0, z1)
            if point_in_poly(gx, gy, YARD_OUTLINE) and not _on_road(gx, gy, 0.1):
                _grass_tuft(m, gx, gy, rng.uniform(0.35, 0.85), rng,
                            n=rng.randint(2, 4), z=GROUND_Z - 0.03)

    # ---- 4) 大面积草土斑（成片、边缘不规则；草色以暗绿+土褐为主，不用鲜绿）----
    patches = []

    def add_patch(cx, cy, rx, ry, mat=None, force_real=False):
        """草土斑：完整板上只留薄污渍（不撒草）；缺板/土面上才是真草甸。"""
        if not point_in_poly(cx, cy, YARD_OUTLINE) or _on_road(cx, cy, 0.2):
            return
        # 采样判断落点：≥3/5 在完整板上 ⇒ 板面污渍
        samples = [(cx, cy)] + [
            (cx + math.cos(a) * rx * 0.55, cy + math.sin(a) * ry * 0.55)
            for a in (0.0, math.pi * 0.5, math.pi, math.pi * 1.5)]
        slab_hits = sum(1 for (sx, sy) in samples if _slab_top(sx, sy) is not None)
        on_slab = slab_hits >= 3

        if on_slab and not force_real:
            # 完整水泥板：只留 1 cm 内的泥/苔薄渍，且只保留一部分，不整片刷绿
            if rng.random() > 0.35:
                return
            top = _slab_top(cx, cy)
            if top is None:
                return
            mat2 = _dec(rng.choice(("Kit_SandDark", "Kit_GrassDark", "Kit_WetSand")))
            for k in range(rng.randint(1, 3)):
                ox = rng.uniform(-0.45, 0.45) * rx
                oy = rng.uniform(-0.45, 0.45) * ry
                m.poly_extrude(_blob(rng, rx * rng.uniform(0.5, 0.8),
                                     ry * rng.uniform(0.5, 0.8), rng.randint(8, 12)),
                               top + 0.004, top + 0.012,
                               at=(cx + ox, cy + oy, 0), mat=mat2)
            return

        # 土面/缺板（或强制墙根草甸）：真草土斑，顶面低于板顶；只有这类才撒草
        z0, z1 = GROUND_Z - 0.06, GROUND_Z - 0.02
        if force_real and _slab_top(cx, cy) is not None:
            top = _slab_top(cx, cy)
            z0, z1 = top + 0.004, top + 0.018    # 墙根薄土带（贴着墙脚，逻辑上成立）
        mat = _dec(mat or rng.choice(("Kit_GrassDark", "Kit_GrassDark", "Kit_SandDark",
                                      "Kit_SandDark", "Kit_GrassDark")))
        # 由 2~3 个错位小斑拼成，边缘更碎、不读成圆贴纸
        for k in range(rng.randint(2, 3)):
            ox = rng.uniform(-0.45, 0.45) * rx
            oy = rng.uniform(-0.45, 0.45) * ry
            m.poly_extrude(_blob(rng, rx * rng.uniform(0.55, 0.85),
                                 ry * rng.uniform(0.55, 0.85), rng.randint(9, 14)),
                           z0 + k * 0.003, z1,
                           at=(cx + ox, cy + oy, 0),
                           mat=mat if k == 0 else _dec(rng.choice(
                               ("Kit_GrassDark", "Kit_SandDark", "Kit_GrassDark"))))
        patches.append((cx, cy, rx, ry, z1))

    # ① 墙根环带：沿围墙内侧 1~5 m 连成草甸（草从墙脚破口长出来，强制真草斑）
    for i in range(len(WALL_LINE)):
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[(i + 1) % len(WALL_LINE)]
        for t in (0.12, 0.38, 0.62, 0.88):
            wax = x0 + (x1 - x0) * t
            way = y0 + (y1 - y0) * t
            nlen = math.hypot(wax, way) or 1.0
            # 指向场心的法向：草带从墙根往场内铺 3~6 m
            inward = rng.uniform(2.0, 5.5)
            cx = wax - wax / nlen * inward
            cy = way - way / nlen * inward
            add_patch(cx, cy, rng.uniform(2.0, 4.5), rng.uniform(1.6, 3.6),
                      rng.choice(("Kit_GrassDark", "Kit_SandDark", "Kit_GrassDark")),
                      force_real=True)

    # ② 场内成片草甸/土斑（完整板上退化为薄污渍，缺板/土面才是真斑）
    for k in range(90):
        cx = rng.uniform(-45.0, 45.0)
        cy = rng.uniform(-31.0, 31.0)
        add_patch(cx, cy, rng.uniform(1.0, 4.6), rng.uniform(0.8, 3.4))

    # ③ 真草斑上撒草簇 + 野花（板面污渍不撒草、不开花）
    for (cx, cy, rx, ry, pz) in patches:
        area = 3.1416 * rx * ry
        tufts = max(4, int(area / 0.95))
        for k in range(tufts):
            a = rng.uniform(0, math.tau)
            r = math.sqrt(rng.random())
            px = cx + math.cos(a) * rx * r * 0.9
            py = cy + math.sin(a) * ry * r * 0.9
            _grass_tuft(m, px, py, rng.uniform(0.38, 0.85), rng,
                        n=rng.randint(3, 5), z=pz)
        if rng.random() < 0.30:
            for k in range(rng.randint(1, 3)):
                a = rng.uniform(0, math.tau)
                r = math.sqrt(rng.random())
                fx = cx + math.cos(a) * rx * r * 0.8
                fy = cy + math.sin(a) * ry * r * 0.8
                _flower(m, fx, fy, rng, z=pz)

    # ---- 5) 裂缝（宽缝 + 两侧草簇）----
    for k, (x0, y0, x1, y1) in enumerate((
        (-40, -6, -20, -12), (2, 20, 22, 16), (-14, 8, 8, 4),
        (10, -14, 30, -12), (-36, 18, -18, 22), (28, -24, 40, -18),
        (-8, -22, 14, -26), (18, 4, 32, 8),
    )):
        _crack(m, x0, y0, x1, y1, SEED + 30 + k, width=0.28)
        for t in (0.15, 0.35, 0.55, 0.75, 0.95):
            gx = x0 + (x1 - x0) * t + rng.uniform(-0.4, 0.4)
            gy = y0 + (y1 - y0) * t + rng.uniform(-0.4, 0.4)
            top = _paved_top(gx, gy)
            if top is None:
                continue          # 土面上没有“水泥裂缝”，不长缝边草
            _grass_tuft(m, gx, gy, rng.uniform(0.3, 0.6), rng, n=2,
                        z=top + 0.003)

    # ---- 6) 积水洼（更大更浅，参考现实废墟的低洼积水）----
    for (x, y, rx, ry) in ((-14, -9, 4.6, 2.6), (16, 9, 4.0, 2.2),
                           (-30, -20, 4.4, 2.4), (32, -7, 3.6, 2.0),
                           (-40, -1, 3.8, 2.2), (7, -25, 4.2, 2.3),
                           (24, 24, 3.4, 1.9), (-6, 16, 3.2, 1.8)):
        _puddle(m, x, y, rx, ry, rng)

    # ---- 7) 翘起/碎裂水泥板与边缘坍塌（碎块落在实际表面上，不悬空）----
    for k in range(16):
        x = rng.uniform(-44, 44)
        y = rng.uniform(-30, 30)
        m.box((rng.uniform(1.0, 2.4), rng.uniform(0.7, 1.6), rng.uniform(0.06, 0.16)),
              at=(x, y, _ground_z(x, y) + 0.004), rz=rng.uniform(0, math.pi),
              mat=rng.choice(("Kit_ConcreteDark", "Kit_ConcreteLight")))
    for k, (x, y, yaw) in enumerate(((-46, -12, 8), (45, -14, -12), (20, 32.5, 5),
                                    (-36, 32, -8), (8, -32, 4), (-20, -32, -6))):
        m.box((3.2, 1.6, 0.22), at=(x, y, GROUND_Z - 0.06),
              rz=math.radians(yaw), rx=math.radians(6), mat="Kit_ConcreteDark")

    # ---- 8) 场内散草（只长在缝/缺板/板边；完整板心不长草，路面不长草）----
    for k in range(1000):
        x = rng.uniform(-45, 45)
        y = rng.uniform(-31, 31)
        if not point_in_poly(x, y, YARD_OUTLINE) or _on_road(x, y, 0.25):
            continue
        top = _slab_top(x, y)
        if top is not None:
            if not _near_slab_edge(x, y, 0.30):   # 完整板面内部：跳过
                continue
            z = top + 0.002
        else:
            z = GROUND_Z - 0.055
        _grass_tuft(m, x, y, rng.uniform(0.35, 0.85), rng, n=rng.randint(2, 4), z=z)


def build_roads(m):
    """场地道路：一条东西主路 + 一条斜插支路 + 墙外延伸（视觉引导）。"""
    rng = random.Random(SEED + 2)

    # 场内道路（中心线与宽度和 build_ground 的避让共用 YARD_ROADS）
    for pts, width in YARD_ROADS:
        _road(m, pts, width=width, z=GROUND_Z + 0.02)
        # 路面坑洞（土/草从破沥青里长出来）+ 路缘草
        for i in range(len(pts) - 1):
            ax, ay = pts[i]
            bx, by = pts[i + 1]
            L = math.hypot(bx - ax, by - ay)
            yaw = math.atan2(by - ay, bx - ax)
            nx, ny = -math.sin(yaw), math.cos(yaw)
            n = max(1, int(L / 6.0))
            for k in range(n):
                t = (k + 0.5) / n
                mx, my = ax + (bx - ax) * t, ay + (by - ay) * t
                for side in (-1.0, 1.0):
                    if rng.random() < 0.45:
                        off = width * 0.5 + rng.uniform(0.12, 0.7)
                        gx, gy = mx + nx * off * side, my + ny * off * side
                        if point_in_poly(gx, gy, YARD_OUTLINE) and not _on_road(gx, gy, 0.05):
                            _grass_tuft(m, gx, gy, rng.uniform(0.3, 0.7), rng,
                                        n=rng.randint(2, 4), z=_grass_z(gx, gy))
                if rng.random() < 0.22:
                    px = mx + rng.uniform(-width * 0.3, width * 0.3)
                    py = my + rng.uniform(-width * 0.3, width * 0.3)
                    m.poly_extrude(_blob(rng, rng.uniform(0.3, 0.9),
                                         rng.uniform(0.25, 0.7), 7),
                                   GROUND_Z + 0.024, GROUND_Z + 0.036,
                                   at=(px, py, 0),
                                   mat=_dec(rng.choice(("Kit_SandDark", "Kit_GrassDark",
                                                        "Kit_WetSand", "Kit_RustDark"))))
                    _grass_tuft(m, px, py, rng.uniform(0.2, 0.45), rng,
                                n=2, z=GROUND_Z + 0.036)

    # 墙外道路延伸：北、西、东三个方向（视觉上通往墙外世界）
    _road(m, [(-2, 32), (-4, 44), (-6, 58)], width=6.0, z=OUTSKIRT_Z + 0.02)
    _road(m, [(-46, -9), (-58, -10), (-72, -12)], width=6.0, z=OUTSKIRT_Z + 0.02)
    _road(m, [(44, 4), (56, 3), (68, 1)], width=5.5, z=OUTSKIRT_Z + 0.02)
    # 路缘碎片/泥带
    rng = random.Random(SEED + 2)
    for k in range(26):
        t = rng.random()
        x = -45 + 90 * t
        y = -15 + rng.uniform(-2.2, 2.2)
        m.box((rng.uniform(0.4, 1.1), rng.uniform(0.3, 0.8), 0.04),
              at=(x, y, GROUND_Z + 0.06), rz=rng.uniform(0, 3.14), mat="Kit_SandDark")


def build_walls(m):
    """不规则围墙：逐段生成，随机高度/倾斜/材质；WALL_GAPS 处留缺口。"""
    rng = random.Random(SEED + 3)
    for i in range(len(WALL_LINE) - 1):
        if (i, i + 1) in WALL_GAPS:
            _rubble_barrier(m, WALL_LINE[i], WALL_LINE[i + 1], rng)
            continue
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[i + 1]
        seg_len = math.hypot(x1 - x0, y1 - y0)
        n = max(1, int(seg_len / 4.4))
        for k in range(n):
            t0, t1 = k / n, (k + 1) / n
            ax, ay = x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0
            bx, by = x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1
            mx, my = (ax + bx) / 2, (ay + by) / 2
            yaw = math.atan2(by - ay, bx - ax)
            L = math.hypot(bx - ax, by - ay) + 0.25
            stub = rng.random() < 0.30
            h = rng.uniform(0.6, 1.35) if stub else rng.uniform(2.0, 3.4)
            mat = rng.choice(("Kit_ConcreteMid", "Kit_ConcreteMid", "Kit_ConcreteDark"))
            # 墙体；完整段带压顶，残基座只剩墙根
            gz = _ground_z(mx, my)
            m.box((L, 0.34, h), at=(mx, my, gz), rz=yaw, mat=mat)
            if not stub:
                m.box((L + 0.1, 0.46, 0.12), at=(mx, my, gz + h), rz=yaw,
                      mat="Kit_ConcreteDark")
            # 锈迹流痕（竖向薄板）
            if rng.random() < 0.45:
                ox, oy = -math.sin(yaw) * 0.18, math.cos(yaw) * 0.18
                m.box((rng.uniform(0.25, 0.6), 0.02, rng.uniform(0.5, min(1.6, h - 0.3))),
                      at=(mx + ox, my + oy, gz + 0.2), rz=yaw, mat="Kit_RustDark")
        # 段末立柱
        for (px, py) in ((x0, y0),):
            gz = _ground_z(px, py)
            m.box((0.55, 0.55, 3.7), at=(px, py, gz), rz=0, mat="Kit_ConcreteDark")
            m.box((0.7, 0.7, 0.14), at=(px, py, gz + 3.7), rz=0, mat="Kit_ConcreteLight")

    # 大门柱（西南主路穿墙处）+ 倒伏门扇
    _gate(m, (-45.5, -16.0), math.radians(90))
    # 墙根荒草/藤蔓
    for i in range(len(WALL_LINE) - 1):
        if (i, i + 1) in WALL_GAPS:
            continue
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[i + 1]
        for t in (0.08, 0.26, 0.44, 0.62, 0.80, 0.94):
            x = x0 + (x1 - x0) * t
            y = y0 + (y1 - y0) * t
            gx = x + rng.uniform(-0.7, 0.7)
            gy = y + rng.uniform(-0.7, 0.7)
            _grass_tuft(m, gx, gy, rng.uniform(0.7, 1.5), rng, n=5,
                        z=_grass_z(gx, gy))


def build_outskirts(m):
    """墙外视觉延伸：草地、远景厂房、堆场、土坡、密植树线、电杆。逻辑不可达。"""
    rng = random.Random(SEED + 4)

    # 墙外地面：比可玩区大一圈的不规则多边形（低于地坪，盖住水面）。
    # 底色用暗草（不是沙）：墙外读成荒废多年的厂区外绿地，与厂内灰地坪自然过渡。
    outer = [
        (-78, -58), (-82,  -6), (-76,  34), (-58,  56), (-12,  62),
        ( 36,  60), ( 66,  50), ( 80,  26), ( 82, -10), ( 74, -46),
        ( 50, -60), ( 10, -64), (-32, -62), (-60, -60),
    ]
    m.poly_extrude(outer, OUTSKIRT_Z - 0.4, OUTSKIRT_Z, mat="Kit_SandDark")

    # 泥地/碎石斑（打散整片草绿；墙外也是"草-土-碎石"混合，不是均匀草坪）
    for k in range(140):
        x = rng.uniform(-76, 76)
        y = rng.uniform(-56, 56)
        if abs(x) < YARD_X / 2 - 2 and abs(y) < YARD_Y / 2 - 2:
            continue
        rx = rng.uniform(2.5, 9.0) * (1.0 if abs(x) > 50 or abs(y) > 40 else 0.8)
        ry = rx * rng.uniform(0.55, 0.9)
        mat = _dec(rng.choice(("Kit_SandDark", "Kit_SandDark", "Kit_SandDark",
                               "Kit_GrassDark", "Kit_WetSand", "Kit_RockDark",
                               "Kit_ConcreteDark", "Kit_GrassDark")))
        m.poly_extrude(_blob(rng, rx, ry, 9), OUTSKIRT_Z + 0.002, OUTSKIRT_Z + 0.02,
                       at=(x, y, 0), mat=mat)

    # 墙外近带（0~7 m）草甸/碎石：与墙内同一密度，内外接缝不靠"两种地面"区分
    for i in range(len(WALL_LINE)):
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[(i + 1) % len(WALL_LINE)]
        for t in (0.08, 0.26, 0.44, 0.62, 0.80, 0.94):
            wx = x0 + (x1 - x0) * t
            wy = y0 + (y1 - y0) * t
            nl = math.hypot(wx, wy) or 1.0
            out = rng.uniform(1.0, 6.5)
            cx = wx + wx / nl * out
            cy = wy + wy / nl * out
            rx = rng.uniform(1.4, 3.6)
            ry = rng.uniform(1.0, 2.8)
            m.poly_extrude(_blob(rng, rx, ry, 8), OUTSKIRT_Z + 0.002, OUTSKIRT_Z + 0.025,
                           at=(cx, cy, 0),
                           mat=_dec(rng.choice(("Kit_GrassDark", "Kit_SandDark", "Kit_GrassDark"))))
            for k in range(rng.randint(2, 5)):
                a = rng.uniform(0, math.tau)
                r = math.sqrt(rng.random())
                _grass_tuft(m, cx + math.cos(a) * rx * r * 0.9,
                            cy + math.sin(a) * ry * r * 0.9,
                            rng.uniform(0.3, 0.8), rng, n=rng.randint(2, 4),
                            z=OUTSKIRT_Z + 0.025)

    # 远景厂房群（低细节体块 + 窗带 + 破屋顶）：贴着墙外 12~26 m，加高加数。
    for cx, cy, w, d, h, yaw in (
        (-58, 26, 18, 9, 9.5, 8), (-63, 4, 15, 9, 7.0, -6), (-54, -46, 17, 9, 8.0, 4),
        (58, 34, 20, 10, 10.0, -4), (64, 8, 15, 8, 7.5, 3), (55, -40, 19, 9, 8.5, 6),
        (-18, 56, 22, 8, 8.0, 2), (20, 55, 16, 8, 6.5, -3),
        (-66, 38, 14, 8, 6.0, -8), (40, 50, 15, 8, 7.0, 5),
        (70, -18, 13, 8, 6.0, -3), (-72, -22, 12, 8, 5.5, 7),
    ):
        _distant_hall(m, cx, cy, w, d, h, math.radians(yaw), rng)

    # 远景罐体/料仓（剪影）
    for cx, cy, r, h in ((-72, 20, 4.2, 13), (74, 22, 5.0, 15), (40, 52, 3.6, 11),
                         (-66, -8, 3.4, 10), (62, 42, 3.2, 9)):
        m.cyl(r, h, at=(cx, cy, OUTSKIRT_Z), seg=12, mat="Kit_SteelPale")
        m.revolve([(r, 0), (r * 1.15, h * 0.85), (0, h * 1.05)],
                  at=(cx, cy, OUTSKIRT_Z), seg=12, mat="Kit_SteelPale")

    # 土坡 / 废料山（低多边形隆起）
    for cx, cy, rad, h, mat in (
        (-46, 41, 10, 3.2, "Kit_GrassDark"), (43, 42, 8, 2.4, "Kit_GrassDark"),
        (-66, -18, 9, 2.6, "Kit_SandDark"), (62, -18, 7, 2.0, "Kit_GrassDark"),
        (0, -52, 11, 2.8, "Kit_GrassDark"), (-76, 10, 8, 2.2, "Kit_GrassDark"),
        (68, -46, 9, 2.4, "Kit_SandDark"),
    ):
        _mound(m, cx, cy, rad, h, mat, rng)

    # 密植树线：绕厂区外缘 2~3 排（枯树 + 阔叶 + 针叶混植），近墙处最密。
    def tree_row(x0, y0, x1, y1, spacing, depth):
        n = max(2, int(math.hypot(x1 - x0, y1 - y0) / spacing))
        for i in range(n):
            t = (i + 0.5) / n
            bx = x0 + (x1 - x0) * t
            by = y0 + (y1 - y0) * t
            for r in range(depth):
                x = bx + rng.uniform(-3.0, 3.0)
                y = by + rng.uniform(-3.0, 3.0)
                if abs(x) < YARD_X / 2 + 3 and abs(y) < YARD_Y / 2 + 3:
                    continue
                if rng.random() < 0.45:
                    _dead_tree(m, x, y, rng.uniform(3.5, 7.5), rng, z=OUTSKIRT_Z)
                else:
                    _broadleaf(m, x, y, rng.uniform(4.0, 7.5), rng, z=OUTSKIRT_Z)

    tree_row(-82, 52, 80, 52, 5.5, 3)      # 北
    tree_row(-72, -54, 66, -54, 6.0, 3)    # 南
    tree_row(-80, -46, -80, 50, 5.5, 3)    # 西
    tree_row(78, -44, 78, 52, 5.5, 3)      # 东
    # 墙外近带再加一圈针叶/枯树（把墙头以上的天际线填满）
    for k in range(46):
        side = k % 4
        if side == 0:
            x, y = rng.uniform(-62, 62), rng.uniform(40, 50)
        elif side == 1:
            x, y = rng.uniform(-62, 62), rng.uniform(-50, -40)
        elif side == 2:
            x, y = rng.uniform(-64, -52), rng.uniform(-36, 34)
        else:
            x, y = rng.uniform(52, 64), rng.uniform(-36, 34)
        if abs(x) < YARD_X / 2 + 4 and abs(y) < YARD_Y / 2 + 4:
            continue
        if rng.random() < 0.5:
            _conifer(m, x, y, rng.uniform(5.0, 9.0), rng, z=OUTSKIRT_Z)
        else:
            _dead_tree(m, x, y, rng.uniform(4.0, 7.0), rng, z=OUTSKIRT_Z)

    # 灌木带（贴墙外 2~5 m，密）
    for i in range(len(WALL_LINE)):
        x0, y0 = WALL_LINE[i]
        for t in range(6):
            tt = (t + 0.5) / 6
            if i + 1 < len(WALL_LINE):
                x1, y1 = WALL_LINE[i + 1]
                x = x0 + (x1 - x0) * tt
                y = y0 + (y1 - y0) * tt
            else:
                x, y = x0, y0
            _shrub(m, x + rng.uniform(-3.0, 3.0), y + rng.uniform(-3.0, 3.0),
                   rng.uniform(1.0, 2.0), rng, z=OUTSKIRT_Z)

    # 墙外中景带（8~24 m）：草甸/土斑 + 草簇，填满从场内看出去的视野
    # （宽景里西北角那片"空土色平地"就是这条带原来没有覆盖到的区域）
    for i in range(len(WALL_LINE)):
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[(i + 1) % len(WALL_LINE)]
        for t in (0.15, 0.4, 0.65, 0.9):
            wx = x0 + (x1 - x0) * t
            wy = y0 + (y1 - y0) * t
            nl = math.hypot(wx, wy) or 1.0
            out = rng.uniform(8.0, 24.0)
            cx = wx + wx / nl * out
            cy = wy + wy / nl * out
            rx = rng.uniform(3.0, 7.0)
            ry = rng.uniform(2.0, 5.0)
            m.poly_extrude(_blob(rng, rx, ry, 9), OUTSKIRT_Z + 0.002, OUTSKIRT_Z + 0.022,
                           at=(cx, cy, 0),
                           mat=_dec(rng.choice(("Kit_SandDark", "Kit_GrassDark",
                                                "Kit_SandDark", "Kit_GrassDark",
                                                "Kit_WetSand", "Kit_RockDark"))))
            for k in range(rng.randint(3, 7)):
                a = rng.uniform(0, math.tau)
                r = math.sqrt(rng.random())
                _grass_tuft(m, cx + math.cos(a) * rx * r * 0.9,
                            cy + math.sin(a) * ry * r * 0.9,
                            rng.uniform(0.4, 1.1), rng, n=rng.randint(2, 4),
                            z=OUTSKIRT_Z + 0.022)

    # 电线杆 + 电缆（墙外道路旁）
    _pole_run(m, [(-2, 34), (-4, 46), (-6, 58)], rng)
    _pole_run(m, [(46, 5), (58, 4), (70, 2)], rng)
    _pole_run(m, [(-48, -9), (-60, -10), (-72, -12)], rng)

    # 墙外堆场：集装箱/废料/托盘/油桶群
    for cx, cy, yaw in ((-54, -26, 12), (52, -30, -8), (48, 30, 20), (-52, 33, -15),
                        (58, -8, 4), (-60, 14, -9)):
        _scrap_yard(m, cx, cy, math.radians(yaw), rng)

    # 外围散草/灌木（密度按像素宽景校准：1 艺术像素 ≈ 5~6 cm，稀了就是一片空土色）
    for k in range(1800):
        x = rng.uniform(-80, 80)
        y = rng.uniform(-62, 62)
        if abs(x) < YARD_X / 2 + 1 and abs(y) < YARD_Y / 2 + 1:
            continue
        _grass_tuft(m, x, y, rng.uniform(0.5, 1.5), rng, n=rng.randint(3, 5),
                    z=OUTSKIRT_Z + 0.01)


def build_vegetation(m):
    """场内植被与荒废细节：草簇、灌木、树、藤蔓、瓦砾、托盘木箱、水洼、铁轨残段。"""
    rng = random.Random(SEED + 5)
    # 墙根/构件根部草簇（沿所有建筑脚、围墙内侧、道路边）
    spots = [
        (-42, 26), (-40, -2), (-36, 8), (-30, 26), (-20, 28), (-8, 26),
        (8, 28), (24, 27), (38, 24), (42, 12), (42, -4), (38, -22),
        (10, -29), (-2, -29), (-16, -30), (-36, -28), (-44, -18),
        (-26, -2), (-24, -16), (6, -12), (22, -18), (14, 2), (2, 2),
        (28, 2), (32, -12), (-2, 16), (-14, 16), (18, 22),
        (-44, 4), (-44, 16), (-43, 22), (-30, 31), (-12, 31), (4, 30),
        (22, 31), (34, 30), (44, 18), (45, 8), (45, -4), (43, -16),
        (34, -30), (18, -31), (4, -31), (-12, -32), (-26, -30), (-38, -30),
        (-20, -14), (-6, -18), (2, -18), (16, -20), (26, -16), (20, -8),
        (-28, -2), (-30, 6), (-16, 4), (0, 8), (10, 8), (24, 12),
    ]
    for (x, y) in spots:
        for k in range(rng.randint(2, 5)):
            px = x + rng.uniform(-1.6, 1.6)
            py = y + rng.uniform(-1.6, 1.6)
            if not point_in_poly(px, py, YARD_OUTLINE) or _on_road(px, py, 0.1):
                continue
            _grass_tuft(m, px, py, rng.uniform(0.6, 1.3), rng,
                        n=rng.randint(4, 7), z=_grass_z(px, py))
    # 整场随机散草（只长在缝/缺板/板边，不在完整板心与路面上）
    for k in range(150):
        x = rng.uniform(-45, 45)
        y = rng.uniform(-31, 31)
        if not point_in_poly(x, y, YARD_OUTLINE) or _on_road(x, y, 0.25):
            continue
        top = _slab_top(x, y)
        if top is not None and not _near_slab_edge(x, y, 0.30):
            continue
        _grass_tuft(m, x, y, rng.uniform(0.4, 1.0), rng, n=rng.randint(2, 4),
                    z=_grass_z(x, y))

    # 部分构件脚下的灌木/藤蔓
    for (x, y, r) in ((-33, -8, 2.2), (-16, -9, 2.0), (34, 12, 2.4),
                      (6, 27, 2.2), (-26, 25, 2.0), (17, -8, 2.6), (-11, -18, 2.0),
                      (-23, 11, 1.6), (32, 2, 1.8), (0, -6, 1.5), (20, 12, 1.4),
                      (-32, -6, 1.6), (42, -24, 1.5)):
        gz = _ground_z(x, y)
        _soil_pit(m, x, y, r * 0.7, rng, z=gz)
        _shrub(m, x, y, r, rng, z=gz)

    # 场内几棵枯树/阔叶树（避开构件与主路；树坑解释破板露土）
    for (x, y, h) in ((-44, 14, 5.5), (22, 24, 4.8), (44, -20, 5.2), (-24, -6, 4.2),
                      (10, 26, 4.6), (26, 14, 4.0), (-31, -7, 4.4), (36, -28, 4.6),
                      (-4, 11, 3.8), (-30, 9, 3.6)):
        gz = _ground_z(x, y)
        _soil_pit(m, x, y, 1.5, rng, z=gz)
        _dead_tree(m, x, y, h, rng, z=gz)
    for (x, y, h) in ((-33, 24, 5.0), (28, 28, 4.4), (44, 28, 4.8), (-9, 24, 4.0),
                      (14, -8, 4.2), (-24, -9, 3.8)):
        gz = _ground_z(x, y)
        _soil_pit(m, x, y, 1.4, rng, z=gz)
        _broadleaf(m, x, y, h, rng, z=gz)

    # 瓦砾堆
    for k, (x, y) in enumerate(((-38, -4), (0, -22), (24, -10), (8, 12), (-18, 4),
                                (36, 2), (-28, 30), (18, -2), (-16, -22), (6, -14),
                                (-40, 10), (32, -18), (12, 22), (-24, 8))):
        _rubble(m, x, y, rng.uniform(1.0, 2.4), rng, n=rng.randint(7, 16))

    # 油桶群/碎件
    for k, (x, y) in enumerate(((-20, -2), (4, -4), (30, 8), (-36, 18), (14, 18),
                                (40, -10), (-4, 22), (22, -26), (0, -8), (-32, -20),
                                (36, -2), (10, -18))):
        _drums(m, x, y, rng, n=rng.randint(3, 7))

    # 托盘 / 木箱 / 混凝土块堆
    for (x, y, yaw) in ((2, -8, 14), (28, 0, -22), (-8, 11, 8), (36, -22, 30),
                        (2, 1, -6), (-24, 11, 17)):
        _pallets_crates(m, x, y, math.radians(yaw), rng)

    # 废弃铁路支线（北侧贴墙，枕木埋草、钢轨一段翘起）
    _rail_spur(m, -30, 28.6, 26, 28.2, rng)

    # 积水洼（低洼处薄片 + 深色湿边）
    for (x, y, rx, ry) in ((0, -10, 2.6, 1.4), (18, 11, 2.2, 1.2), (-26, -18, 2.8, 1.5),
                           (43, -10, 2.0, 1.1), (-34, -7, 2.2, 1.2), (36, -28, 2.4, 1.3)):
        _puddle(m, x, y, rx, ry, rng)

    # 场内废弃电杆（倒伏）
    _fallen_pole(m, 12, -11, math.radians(28), rng)
    _fallen_pole(m, -30, 29, math.radians(-52), rng)

    # 藤蔓：贴主厂房 / 办公楼 / 六件旁楼的实际墙脚（起点在墙外 6~10 cm，沿墙攀爬）
    for (x, y, yaw, height) in (
        (-16.0, -8.08, 0, 4.2), (-8.5, -8.08, 0, 3.6),     # 主厂房南墙
        (-36.0, -2.58, 0, 6.5), (-29.0, -2.58, 0, 5.8),    # 办公楼南墙
        (26.0, 11.02, 0, 4.6), (32.0, 11.02, 0, 5.2),      # 六件旁楼南墙
    ):
        _vine(m, x, y, math.radians(yaw), height, rng)


# ---------------------------------------------------------------------------
# 图元 helper
# ---------------------------------------------------------------------------

def point_in_poly(x, z, poly):
    """even-odd 点包含（poly = [(x, z), ...]）。"""
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        xi, zi = poly[i]
        xj, zj = poly[j]
        if ((zi > z) != (zj > z)) and (x < (xj - xi) * (z - zi) / (zj - zi) + xi):
            inside = not inside
        j = i
    return inside


def _blob(rng, rx, ry, sides):
    """不规则椭圆轮廓（多边形顶点）。"""
    pts = []
    for i in range(sides):
        a = 2 * math.pi * i / sides
        k = rng.uniform(0.82, 1.12)
        pts.append((math.cos(a) * rx * k, math.sin(a) * ry * k))
    return pts


def _crack(m, x0, y0, x1, y1, seed, segs=6, width=0.18):
    rng = random.Random(seed)
    pts = [(x0, y0)]
    for i in range(1, segs):
        t = i / segs
        pts.append((x0 + (x1 - x0) * t + rng.uniform(-0.5, 0.5),
                    y0 + (y1 - y0) * t + rng.uniform(-0.5, 0.5)))
    pts.append((x1, y1))
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
        top = _paved_top(mx, my)
        if top is None:
            continue          # 裂缝只存在于水泥板/沥青上，土面上不画
        m.box((L + 0.08, width, 0.008), at=(mx, my, top + 0.002),
              rz=yaw, mat="Kit_DecWet")


def _road(m, pts, width, z):
    """残破道路：短段拼接 + 段间缺口 + 草土侵占 + 路缘草。"""
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        n = max(1, int(L / 3.8))
        for k in range(n):
            rr = random.Random(SEED + 700 + i * 97 + k)
            if rr.random() < 0.15:                      # 整段缺失（被草吞掉）
                continue
            t0, t1 = k / n, (k + 1) / n
            mx = ax + (bx - ax) * (t0 + t1) * 0.5
            my = ay + (by - ay) * (t0 + t1) * 0.5
            seg = L / n
            m.box((seg - rr.uniform(0.05, 0.35), width, 0.04),
                  at=(mx, my, z - 0.02), rz=yaw, mat="Kit_ConcreteDark")
            # 路面草土侵占
            if rr.random() < 0.50:
                m.poly_extrude(_blob(rr, seg * rr.uniform(0.25, 0.48), width * rr.uniform(0.25, 0.45), 7),
                               z + 0.005, z + 0.035,
                               at=(mx + rr.uniform(-0.8, 0.8), my + rr.uniform(-0.8, 0.8), 0),
                               mat=_dec(rr.choice(("Kit_GrassDark", "Kit_SandDark",
                                                   "Kit_GrassDark", "Kit_RustDark"))))
            # 路缘草
            for side in (-1, 1):
                ox = -math.sin(yaw) * width * 0.5 * side
                oy = math.cos(yaw) * width * 0.5 * side
                if rr.random() < 0.85:
                    gx = mx + ox + rr.uniform(-0.35, 0.35)
                    gy = my + oy + rr.uniform(-0.35, 0.35)
                    _grass_tuft(m, gx, gy, rr.uniform(0.3, 0.65), rr, n=2,
                                z=_grass_z(gx, gy))


def _gate(m, at, yaw):
    x, y = at
    cx, cy = math.cos(yaw), math.sin(yaw)
    gz = _ground_z(x, y)
    # 两根门柱
    for s in (-1, 1):
        px = x - cy * 3.6 * s
        py = y + cx * 3.6 * s
        m.box((0.8, 0.8, 3.6), at=(px, py, _ground_z(px, py)), rz=yaw,
              mat="Kit_ConcreteDark")
        m.box((1.0, 1.0, 0.16), at=(px, py, _ground_z(px, py) + 3.6), rz=yaw,
              mat="Kit_ConcreteLight")
    # 半塌的推拉门扇
    m.box((3.4, 0.12, 2.2), at=(x - cy * 1.2, y + cx * 1.2, gz + 0.15),
          rz=yaw + math.radians(18), rx=math.radians(14), mat="Kit_Rust")
    # 门牌
    m.box((1.6, 0.08, 0.5), at=(x, y, gz + 2.9), rz=yaw, mat="Kit_PaintYellow")


def _rubble_barrier(m, a, b, rng):
    """围墙缺口处的废料堆/土坡/树丛（视觉封口，不是墙）。"""
    ax, ay = a
    bx, by = b
    n = 6
    for k in range(n):
        t = (k + 0.5) / n
        x = ax + (bx - ax) * t
        y = ay + (by - ay) * t
        _mound(m, x, y, rng.uniform(1.8, 3.0), rng.uniform(1.4, 2.6), "Kit_GrassDark", rng)
        _rubble(m, x, y, rng.uniform(1.4, 2.4), rng, n=rng.randint(8, 14))
        if rng.random() < 0.7:
            tx = x + rng.uniform(-1.2, 1.2)
            ty = y + rng.uniform(-1.2, 1.2)
            _dead_tree(m, tx, ty, rng.uniform(3.5, 6.0), rng, z=_ground_z(tx, ty))
        if rng.random() < 0.6:
            sx = x + rng.uniform(-1.5, 1.5)
            sy = y + rng.uniform(-1.5, 1.5)
            _shrub(m, sx, sy, rng.uniform(1.0, 1.8), rng, z=_ground_z(sx, sy))


def _mound(m, cx, cy, rad, h, mat, rng):
    sides = 10
    inside = abs(cx) < YARD_X * 0.5 and abs(cy) < YARD_Y * 0.5
    base = _ground_z(cx, cy) if inside else OUTSKIRT_Z
    m.revolve([(0.0, 0.0), (rad * 0.7, h * 0.45), (rad * 0.38, h * 0.82), (0.0, h)],
              at=(cx, cy, base), seg=sides, mat=mat)


def _distant_hall(m, cx, cy, w, d, h, yaw, rng):
    """远景厂房：大体块 + 窗带 + 破屋顶（低细节剪影向）。"""
    m.box((w, d, h), at=(cx, cy, OUTSKIRT_Z), rz=yaw, mat="Kit_ConcreteDark")
    # 屋顶（半塌）
    m.box((w + 0.4, d + 0.4, 0.25), at=(cx, cy, OUTSKIRT_Z + h), rz=yaw,
          mat="Kit_RustDark")
    # 窗带（两面）
    for side in (-1, 1):
        ox = math.sin(yaw) * (d / 2 + 0.03) * side
        oy = math.cos(yaw) * (d / 2 + 0.03) * side
        n = max(2, int(w / 3.2))
        for k in range(n):
            t = (k + 0.5) / n - 0.5
            px = cx + math.cos(yaw) * w * t + ox
            py = cy + math.sin(yaw) * w * t + oy
            m.box((1.5, 0.06, 1.05), at=(px, py, OUTSKIRT_Z + h * 0.55),
                  rz=yaw, mat="Kit_GlassDark")
    # 附属小体块
    m.box((w * 0.35, d * 0.5, h * 0.45),
          at=(cx + math.cos(yaw + 1.57) * (d * 0.7), cy + math.sin(yaw + 1.57) * (d * 0.7),
              OUTSKIRT_Z),
          rz=yaw + math.radians(8), mat="Kit_ConcreteMid")


def _dead_tree(m, x, y, h, rng, z=None):
    z = GROUND_Z if z is None else z
    trunk_r = max(0.12, h * 0.035)
    m.cyl(trunk_r * 1.35, h, at=(x, y, z), seg=6, r_top=trunk_r * 0.55,
          mat="Kit_WoodDark", smooth=False)
    # 主枝
    for k in range(3):
        a = rng.uniform(0, math.pi * 2)
        z0 = h * rng.uniform(0.45, 0.78)
        reach = h * rng.uniform(0.22, 0.38)
        tilt = rng.uniform(0.5, 1.1)
        p0 = Vector((x, y, z + z0))
        p1 = p0 + Vector((math.cos(a) * reach, math.sin(a) * reach, reach * tilt))
        _branch(m, p0, p1, trunk_r * 0.55, "Kit_WoodDark")
        # 次级枝
        if rng.random() < 0.7:
            p2 = p1 + Vector((math.cos(a + rng.uniform(-0.9, 0.9)) * reach * 0.55,
                              math.sin(a + rng.uniform(-0.9, 0.9)) * reach * 0.55,
                              reach * rng.uniform(0.2, 0.5)))
            _branch(m, p1, p2, trunk_r * 0.28, "Kit_WoodDark")
    # 残叶/枯枝团（稀疏、低饱和）
    if rng.random() < 0.55:
        m.sphere(h * 0.18, at=(x, y, z + h * 0.9), seg=8, rings=5,
                 mat="Kit_GrassDark", smooth=False)


def _branch(m, p0, p1, r, mat):
    d = p1 - p0
    L = d.length
    if L < 0.05:
        return
    m.extrude_along(_circle_profile(r, 6), p0, p1, mat=mat, smooth=False)


def _broadleaf(m, x, y, h, rng, z=None):
    """阔叶树：短干 + 2~3 团低多边形树冠（荒废厂区外的绿带主力）。"""
    z = GROUND_Z if z is None else z
    trunk_h = h * rng.uniform(0.35, 0.55)
    m.cyl(h * 0.045, trunk_h, at=(x, y, z), seg=12, r_top=h * 0.035,
          mat="Kit_WoodDark", smooth=True)
    crown_r = h * rng.uniform(0.28, 0.38)
    for k in range(rng.randint(2, 3)):
        ox = rng.uniform(-0.35, 0.35) * crown_r
        oy = rng.uniform(-0.35, 0.35) * crown_r
        rr = crown_r * rng.uniform(0.7, 1.05)
        m.sphere(rr, at=(x + ox, y + oy, z + trunk_h + crown_r * 0.55 + k * crown_r * 0.35),
                 seg=16, rings=10,
                 mat=rng.choice(("Kit_CpFoliageMid", "Kit_CpFoliageLight", "Kit_CpFoliageMid")),
                 smooth=True)


def _conifer(m, x, y, h, rng, z=None):
    """针叶树：短干 + 三层锥冠。"""
    z = GROUND_Z if z is None else z
    m.cyl(h * 0.04, h * 0.35, at=(x, y, z), seg=10, r_top=h * 0.03,
          mat="Kit_WoodDark", smooth=True)
    for k, (z0, r, hh) in enumerate(((h * 0.18, h * 0.20, h * 0.42),
                                     (h * 0.38, h * 0.16, h * 0.38),
                                     (h * 0.56, h * 0.11, h * 0.34))):
        m.revolve([(r, 0.0), (r * 0.55, hh * 0.55), (0.0, hh)],
                  at=(x, y, z + z0), seg=14,
                  mat=rng.choice(("Kit_CpFoliageDark", "Kit_CpFoliageMid")), smooth=True)


def _pallets_crates(m, x, y, yaw, rng):
    """托盘木箱堆（废料/待运货物）。"""
    # 托盘底
    for k in range(rng.randint(1, 3)):
        ox = rng.uniform(-1.2, 1.2)
        oy = rng.uniform(-1.2, 1.2)
        gz = _ground_z(x + ox, y + oy)
        m.box((1.2, 0.8, 0.14), at=(x + ox, y + oy, gz), rz=yaw + rng.uniform(-0.3, 0.3),
              mat="Kit_WoodDark")
        if rng.random() < 0.65:
            w = rng.uniform(0.6, 1.0)
            m.box((w, w * 0.8, w * 0.7), at=(x + ox, y + oy, gz + 0.14),
                  rz=yaw + rng.uniform(-0.4, 0.4), mat="Kit_WoodDark")
    # 散落木条
    for k in range(rng.randint(2, 5)):
        lx = x + rng.uniform(-1.8, 1.8)
        ly = y + rng.uniform(-1.8, 1.8)
        m.box((rng.uniform(1.0, 2.2), 0.12, 0.08),
              at=(lx, ly, _ground_z(lx, ly) + 0.005),
              rz=rng.uniform(0, 3.14), mat="Kit_WoodDark")


def _rail_spur(m, x0, y0, x1, y1, rng):
    """废弃铁路支线：碎石道床 + 枕木 + 两条钢轨（局部翘起/断头）。"""
    L = math.hypot(x1 - x0, y1 - y0)
    yaw = math.atan2(y1 - y0, x1 - x0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    gz = _ground_z(cx, cy)
    # 道床
    m.box((L, 2.6, 0.10), at=(cx, cy, gz), rz=yaw, mat="Kit_SandDark")
    # 枕木
    n = int(L / 0.9)
    for i in range(n):
        t = (i + 0.5) / n
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        m.box((0.32, 2.0, 0.10), at=(x, y, _ground_z(x, y) + 0.10), rz=yaw,
              mat="Kit_WoodDark")
    # 钢轨（两条）
    for side in (-0.62, 0.62):
        ox = -math.sin(yaw) * side
        oy = math.cos(yaw) * side
        for seg in range(3):
            t0 = seg / 3.0
            t1 = (seg + 1) / 3.0
            ax = x0 + (x1 - x0) * t0 + ox
            ay = y0 + (y1 - y0) * t0 + oy
            bx = x0 + (x1 - x0) * t1 + ox
            by = y0 + (y1 - y0) * t1 + oy
            m.box((math.hypot(bx - ax, by - ay), 0.14, 0.16),
                  at=((ax + bx) / 2, (ay + by) / 2, _ground_z((ax + bx) / 2, (ay + by) / 2) + 0.20),
                  rz=yaw, mat="Kit_Iron")
    # 断头翘起的钢轨
    m.box((3.0, 0.14, 0.16), at=(x1 - 1.2, y1 + 0.6, _ground_z(x1 - 1.2, y1 + 0.6) + 0.24),
          rz=yaw + 0.3, rx=math.radians(10), mat="Kit_Rust")


def _puddle(m, x, y, rx, ry, rng):
    """积水洼：湿边 + 薄水片。板/沥青上是 1 cm 内的薄水膜；土面上才是洼地水。"""
    top = _paved_top(x, y)
    if top is not None:
        zr0, zr1 = top + 0.002, top + 0.006
        zw0, zw1 = top + 0.007, top + 0.010
    else:
        zr0, zr1 = GROUND_Z - 0.055, GROUND_Z - 0.035
        zw0, zw1 = GROUND_Z - 0.034, GROUND_Z - 0.028
    m.poly_extrude(_blob(rng, rx, ry, 9), zr0, zr1, at=(x, y, 0), mat="Kit_DecWet")
    m.poly_extrude(_blob(rng, rx * 0.72, ry * 0.72, 8), zw0, zw1,
                   at=(x + 0.2, y - 0.1, 0), mat="Kit_DecWater")


def _circle_profile(r, sides):
    return [(math.cos(2 * math.pi * i / sides) * r,
             math.sin(2 * math.pi * i / sides) * r) for i in range(sides)]


def _dec(name):
    """把地面平贴片的颜色槽映射到无描边的贴片槽（泥/苔/湿/水）。"""
    return {
        "Kit_SandDark": "Kit_DecSoil",
        "Kit_GrassDark": "Kit_DecGrass",
        "Kit_WetSand": "Kit_DecWet",
        "Kit_GlassDark": "Kit_DecWater",
        "Kit_ConcreteDark": "Kit_DecWet",
        "Kit_RustDark": "Kit_DecWet",
        "Kit_RockDark": "Kit_DecSoil",
    }.get(name, name)


def _grass_fan(m, x, y, size, rng, z, yaw):
    """单张扇状草叶卡：**一整张共面多边形**，锯齿只在外轮廓上。

    关键在共面：如果扇面三角各自不共面，屏幕空间法线边会把内部辐条也描出来，
    草就变成“棕榈叶”；这里所有顶点都在同一个倾斜平面里，描边只会沿外轮廓走。
    叶片卡朝外、上仰 35~55°，正面的法线朝相机/天空，避免竖向叶片吃到最暗光档。
    """
    h = 0.45 + rng.uniform(0.25, 0.65) * size
    w = 0.34 + rng.uniform(0.12, 0.24) * size
    tilt = math.radians(rng.uniform(30.0, 50.0))
    n = 9                        # 5 个主尖 + 4 个缺口：比 13 尖少一半“梳齿”
    verts = [(0.0, 0.0, 0.0)]
    for i in range(n):
        t = i / (n - 1)
        u = -w * 0.5 + w * t + rng.uniform(-0.025, 0.025) * w
        v = h * (0.55 + 0.45 * math.sin(math.pi * t))
        if i in (0, n - 1):
            v *= 0.55
        elif i % 2 == 1:
            v *= rng.uniform(0.35, 0.58)     # 缺口不完全对称，避免机械梳齿
        else:
            v *= rng.uniform(0.82, 1.10)
        verts.append((u, v * math.sin(tilt), v * math.cos(tilt)))
    # (0, i+1, i) → 法线朝外/上（正面朝南，像素相机看得到）
    faces = [(0, i + 1, i) for i in range(1, n)]
    m.add(verts, faces, at=(x, y, z), rz=yaw, mat="Kit_GrassMid")


def _grass_tuft(m, x, y, size, rng, n=5, z=None):
    """草簇：3 张朝向不同的扇状草叶卡，形成有体积的草丛。

    n 保留在签名里（旧调用按叶片数传），实际用 3 张卡：像素管线是单面几何，
    单张卡背向相机就会被背剔；3 个方位保证任意机位至少有一张正面可见，
    同时描边只沿每张卡的外轮廓，不再把每根细叶描成黑线。
    """
    z = GROUND_Z if z is None else z
    yaw0 = rng.uniform(0, math.tau)
    for k in range(3):
        yaw = yaw0 + k * math.tau / 3.0 + rng.uniform(-0.22, 0.22)
        jx = x + rng.uniform(-0.10, 0.10) * size
        jy = y + rng.uniform(-0.10, 0.10) * size
        _grass_fan(m, jx, jy, size, rng, z, yaw)


def _flower(m, x, y, rng, z=None):
    """野花点缀（小白/淡黄/淡蓝），茎 + 花头各一个薄片。z = 草斑顶面。"""
    z = GROUND_Z if z is None else z
    h = rng.uniform(0.10, 0.24)
    m.box((0.04, 0.04, h), at=(x, y, z + 0.001), rz=rng.uniform(0, 3.14),
          mat="Kit_DecGrass")
    m.poly_extrude(_blob(rng, 0.06, 0.06, 5), z + h,
                   z + h + 0.015, at=(x, y, 0),
                   mat=rng.choice(("Kit_ConcreteLight", "Kit_PaintYellow", "Kit_SteelBlue")))


def _shrub(m, x, y, r, rng, z=None):
    z = GROUND_Z if z is None else z
    for k in range(rng.randint(4, 7)):
        ox = rng.uniform(-r, r) * 0.7
        oy = rng.uniform(-r, r) * 0.7
        rr = rng.uniform(0.35, 0.8) * r * 0.6
        m.sphere(rr, at=(x + ox, y + oy, z + rr * 0.65), seg=12, rings=8,
                 mat=rng.choice(("Kit_CpMoss", "Kit_CpFoliageMid")), smooth=True)


def _soil_pit(m, x, y, r, rng, z=None):
    """树坑/灌木坑：破板露土 + 坑边碎板 + 草簇——解释树为什么能从水泥地里长出来。"""
    z = _ground_z(x, y) if z is None else z
    top = _slab_top(x, y)
    zt = (top + 0.02) if top is not None else (z + 0.02)
    m.poly_extrude(_blob(rng, r, r * 0.82, 9), zt - 0.025, zt, at=(x, y, 0),
                   mat=_dec(rng.choice(("Kit_SandDark", "Kit_SandDark", "Kit_GrassDark"))))
    for k in range(rng.randint(3, 5)):
        a = rng.uniform(0, math.tau)
        rad = r * rng.uniform(0.75, 1.1)
        px, py = x + math.cos(a) * rad, y + math.sin(a) * rad
        m.box((rng.uniform(0.35, 0.8), rng.uniform(0.25, 0.55), 0.06),
              at=(px, py, zt - 0.012), rz=rng.uniform(0, 3.14), rx=rng.uniform(-0.2, 0.2),
              mat=rng.choice(("Kit_ConcreteDark", "Kit_ConcreteMid")))
    for k in range(rng.randint(4, 7)):
        a = rng.uniform(0, math.tau)
        rad = r * rng.uniform(0.55, 1.0)
        _grass_tuft(m, x + math.cos(a) * rad, y + math.sin(a) * rad,
                    rng.uniform(0.35, 0.7), rng, n=3, z=zt)


def _rubble(m, x, y, spread, rng, n=10):
    for k in range(n):
        px = x + rng.uniform(-spread, spread)
        py = y + rng.uniform(-spread, spread)
        s = rng.uniform(0.18, 0.55)
        m.box((s, s * rng.uniform(0.6, 1.4), s * rng.uniform(0.3, 0.8)),
              at=(px, py, _ground_z(px, py)), rz=rng.uniform(0, 3.14),
              mat=rng.choice(("Kit_ConcreteDark", "Kit_ConcreteMid", "Kit_RustDark")))


def _drums(m, x, y, rng, n=5):
    for k in range(n):
        px = x + rng.uniform(-1.3, 1.3)
        py = y + rng.uniform(-1.3, 1.3)
        gz = _ground_z(px, py)
        standing = rng.random() < 0.6
        r = 0.3
        h = 0.92
        if standing:
            m.cyl(r, h, at=(px, py, gz), seg=8, mat="Kit_Rust")
            m.cyl(r * 1.03, 0.05, at=(px, py, gz + 0.28), seg=8, mat="Kit_RustDark")
            m.cyl(r * 1.03, 0.05, at=(px, py, gz + 0.62), seg=8, mat="Kit_RustDark")
        else:
            _branch(m, Vector((px - 0.5, py, gz + r)), Vector((px + 0.5, py, gz + r)),
                    r, "Kit_RustDark")


def _fallen_pole(m, x, y, yaw, rng):
    L = 7.5
    gz = _ground_z(x, y)
    p0 = Vector((x, y, gz + 0.18))
    p1 = p0 + Vector((math.cos(yaw) * L, math.sin(yaw) * L, 0))
    _branch(m, p0, p1, 0.13, "Kit_WoodDark")
    # 横担
    c = (p0 + p1) / 2
    m.box((0.1, 3.4, 0.1), at=(c.x, c.y, c.z + 0.05), rz=yaw + math.pi / 2,
          mat="Kit_WoodDark")


def _pole_run(m, pts, rng):
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        for t in (0.0, 0.5):
            x = ax + (bx - ax) * t
            y = ay + (by - ay) * t
            h = rng.uniform(6.0, 7.0)
            m.cyl(0.11, h, at=(x, y, OUTSKIRT_Z), seg=6, mat="Kit_WoodDark")
            m.box((0.08, 2.6, 0.08), at=(x, y, OUTSKIRT_Z + h - 0.5), rz=rng.uniform(0, 3.14),
                  mat="Kit_WoodDark")
        # 电缆（两杆之间的下垂折线）
        _cable(m, (ax, ay, OUTSKIRT_Z + 6.2), (bx, by, OUTSKIRT_Z + 6.2), rng)


def _cable(m, a, b, rng):
    ax, ay, az = a
    bx, by, bz = b
    seg = 5
    prev = Vector((ax, ay, az))
    for i in range(1, seg + 1):
        t = i / seg
        x = ax + (bx - ax) * t
        y = ay + (by - ay) * t
        z = az + (bz - az) * t - math.sin(math.pi * t) * 0.7
        cur = Vector((x, y, z))
        _branch(m, prev, cur, 0.025, "Kit_Iron")
        prev = cur


def _scrap_yard(m, cx, cy, yaw, rng):
    for k in range(4):
        ox = rng.uniform(-4.5, 4.5)
        oy = rng.uniform(-3.5, 3.5)
        w = rng.uniform(2.2, 4.4)
        d = rng.uniform(1.6, 2.8)
        m.box((w, d, 2.6), at=(cx + ox, cy + oy, OUTSKIRT_Z), rz=yaw + rng.uniform(-0.4, 0.4),
              mat=rng.choice(("Kit_SteelBlue", "Kit_Rust", "Kit_ConcreteDark")))
    _mound(m, cx, cy, 5.5, 2.2, "Kit_RustDark", rng)


def _vine(m, x, y, yaw, height, rng):
    """墙根藤蔓：贴墙向上攀爬 + 叶簇（不是一条躺在地上的绿线）。

    x/y = 墙脚外 5~10 cm 的起点；yaw = 沿墙方向；height = 攀爬高度。
    """
    dx, dy = math.cos(yaw), math.sin(yaw)
    for k in range(5):
        off = rng.uniform(-0.45, 0.45)
        climb = height * rng.uniform(0.45, 1.0)
        p0 = Vector((x + dx * off, y + dy * off, GROUND_Z + 0.04))
        p1 = p0 + Vector((dx * rng.uniform(0.15, 0.70),
                          dy * rng.uniform(0.15, 0.70), climb))
        _branch(m, p0, p1, 0.03, "Kit_CpMoss")
        # 叶簇沿茎 2~3 处 + 顶梢，读作爬藤而不是一根杆
        for t in (0.42, 0.72, 1.0):
            p = p0.lerp(p1, t)
            rr = 0.20 * rng.uniform(0.75, 1.25)
            m.sphere(rr, at=(p.x, p.y, p.z), seg=6, rings=3,
                     mat=rng.choice(("Kit_CpMoss", "Kit_CpFoliageMid")), smooth=False)


# ---------------------------------------------------------------------------
# 休闲化重做（2026-11-22）：奶油大板广场 + 三处圆形苔藓花园 + 圆润植被
# ---------------------------------------------------------------------------

#: 三处圆形苔藓花园（避开主装置/道路的空地）：西北角、南侧中段、东南角。
MOSS_ZONES = [(-38.0, 24.0, 7.0), (10.0, -24.0, 8.0), (32.0, -24.0, 6.0)]

GATE_POS = (-45.5, -16.0)


def _in_moss_zone(x, y):
    for zx, zy, zr in MOSS_ZONES:
        if (x - zx) ** 2 + (y - zy) ** 2 <= zr * zr:
            return True
    return False


def _grass_dome(m, x, y, size, rng, z):
    """圆润草丘：2~3 个半埋低多边形圆顶，亮/中/暗三档草色。"""
    mats = ("Kit_CpGrass", "Kit_CpMoss", "Kit_CpFoliageLight")
    for k in range(rng.randint(2, 3)):
        rr = (0.24 + 0.18 * size) * rng.uniform(0.82, 1.12)
        hh = (0.20 + 0.16 * size) * rng.uniform(0.85, 1.20)
        ox = rng.uniform(-0.55, 0.55) * rr
        oy = rng.uniform(-0.55, 0.55) * rr
        m.revolve([(0.0, 0.0), (rr * 0.55, 0.015), (rr * 0.84, hh * 0.14),
                   (rr, hh * 0.36), (rr * 0.88, hh * 0.62),
                   (rr * 0.55, hh * 0.85), (0.0, hh)],
                  at=(x + ox, y + oy, z), seg=16, mat=rng.choice(mats), smooth=True)


def _planter_tree(m, x, y, h, rng):
    """圆形花坛树：低矮圆池 + 土面 + 圆冠树，给大广场一点休闲游戏的花园感。"""
    gz = _ground_z(x, y)
    m.cyl(1.45, 0.35, at=(x, y, gz), seg=24, mat="Kit_ConcreteLight")
    m.cyl(1.30, 0.05, at=(x, y, gz + 0.35), seg=24, mat="Kit_DecSoil")
    _broadleaf(m, x, y, h, rng, z=gz + 0.40)


def _clean_road(m, pts, width, z):
    """连续干净的沥青路：无断段/坑洞/草侵，只有两条浅色路缘线。"""
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
        m.box((L + 0.02, width, 0.06), at=(mx, my, z - 0.03), rz=yaw,
              mat="Kit_CpAsphalt")
        for side in (-1.0, 1.0):
            off = width * 0.5 - 0.10
            ox, oy = -math.sin(yaw) * off * side, math.cos(yaw) * off * side
            m.box((L + 0.02, 0.12, 0.07), at=(mx + ox, my + oy, z - 0.035),
                  rz=yaw, mat="Kit_ConcreteLight")


def build_ground_casual(m):
    """休闲化地坪：奶油大板 + 细苔缝；没有随机缺板和碎细节，只有三处圆形花园。"""
    rng = random.Random(SEED + 1)
    SLAB_RECTS.clear()
    MISSING_RECTS.clear()

    # 土基：浅沙（只在板缝/花园/缺板处露出）
    m.poly_extrude(YARD_OUTLINE, GROUND_Z - 0.30, GROUND_Z - 0.04, mat="Kit_CpSand")

    # 大板：6~8.5 m 一块，缝 18~30 cm；只有花园范围整块缺板
    xs = _cuts(-48.0, 48.0, rng, 6.0, 8.5)
    zs = _cuts(-34.0, 34.0, rng, 5.5, 7.0)
    for i in range(len(xs) - 1):
        for j in range(len(zs) - 1):
            x0, x1 = xs[i], xs[i + 1]
            z0, z1 = zs[j], zs[j + 1]
            cx, cy = (x0 + x1) * 0.5, (z0 + z1) * 0.5
            if not point_in_poly(cx, cy, YARD_OUTLINE):
                continue
            gap = rng.uniform(0.18, 0.30)
            w = (x1 - x0) - gap
            d = (z1 - z0) - gap
            if w < 1.0 or d < 1.0 or _in_moss_zone(cx, cy):
                MISSING_RECTS.append((x0, z0, x1, z1))
                continue
            mat = rng.choice(("Kit_ConcreteLight", "Kit_ConcreteLight",
                              "Kit_ConcreteLight", "Kit_ConcreteMid"))
            m.box((w, d, 0.12), at=(cx, cy, GROUND_Z - 0.12), mat=mat)
            SLAB_RECTS.append((cx - w * 0.5, cy - d * 0.5,
                               cx + w * 0.5, cy + d * 0.5, GROUND_Z))

    # 板缝苔藓：细绿色缝线（补贴片，不描边）
    for x in xs[1:-1]:
        z = -33.0
        while z < 33.0:
            seg = 3.6
            if point_in_poly(x, z + seg * 0.5, YARD_OUTLINE):
                m.box((0.30, seg + 0.06, 0.02), at=(x, z + seg * 0.5, GROUND_Z - 0.035),
                      mat="Kit_DecMoss")
            z += seg
    for z in zs[1:-1]:
        x = -47.0
        while x < 47.0:
            seg = 3.6
            if point_in_poly(x + seg * 0.5, z, YARD_OUTLINE):
                m.box((seg + 0.06, 0.30, 0.02), at=(x + seg * 0.5, z, GROUND_Z - 0.035),
                      mat="Kit_DecMoss")
            x += seg

    # 三处圆形苔藓花园：浅沙圈 + 苔藓主体 + 圆水洼
    for (zx, zy, zr) in MOSS_ZONES:
        m.poly_extrude(_blob(rng, zr * 0.98, zr * 0.94, 16),
                       GROUND_Z - 0.055, GROUND_Z - 0.026,
                       at=(zx, zy, 0), mat="Kit_DecSoil")
        for k in range(2):
            ox = rng.uniform(-0.15, 0.15) * zr
            oy = rng.uniform(-0.15, 0.15) * zr
            m.poly_extrude(_blob(rng, zr * (0.72 - 0.10 * k), zr * (0.66 - 0.09 * k), 14),
                           GROUND_Z - 0.030, GROUND_Z - 0.012,
                           at=(zx + ox, zy + oy, 0), mat="Kit_DecMoss")

    # 花园里的浅水洼（水色是浅蓝，不是深墨）
    for (px, py, rx, ry) in ((10.0, -24.0, 3.6, 2.7), (32.0, -24.0, 2.6, 2.0),
                             (-38.0, 24.0, 2.4, 1.8)):
        m.poly_extrude(_blob(rng, rx * 1.10, ry * 1.10, 12),
                       GROUND_Z - 0.014, GROUND_Z - 0.008, at=(px, py, 0), mat="Kit_DecWet")
        m.poly_extrude(_blob(rng, rx * 0.82, ry * 0.82, 10),
                       GROUND_Z - 0.007, GROUND_Z - 0.002,
                       at=(px + 0.2, py - 0.1, 0), mat="Kit_DecWater")


def build_roads_casual(m):
    """两条干净沥青路 + 一条从大门向西的场外土路。"""
    _clean_road(m, YARD_ROADS[0][0], 6.0, GROUND_Z + 0.02)
    _clean_road(m, YARD_ROADS[1][0], 4.5, GROUND_Z + 0.02)
    # 场外土路：大门 → 西侧旷野
    m.box((34.0, 5.0, 0.06), at=(-63.0, -17.0, OUTSKIRT_Z - 0.01), mat="Kit_DecSoil")


def build_walls_casual(m):
    """连续矮墙 + 圆润墙柱 + 干净大门；墙根一条苔藓带，不再有断壁/锈痕/残基。"""
    rng = random.Random(SEED + 103)
    gx, gy = GATE_POS

    def near_gate(x, y, d=2.9):
        return (x - gx) ** 2 + (y - gy) ** 2 < d * d

    for i in range(len(WALL_LINE) - 1):
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[i + 1]
        seg = math.hypot(x1 - x0, y1 - y0)
        yaw = math.atan2(y1 - y0, x1 - x0)
        n = max(1, int(seg / 4.0))
        for k in range(n):
            t0, t1 = k / n, (k + 1) / n
            ax, ay = x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0
            bx, by = x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1
            mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
            if near_gate(mx, my):
                continue
            L = math.hypot(bx - ax, by - ay) + 0.10
            gz = _ground_z(mx, my)
            m.box((L, 0.36, 2.2), at=(mx, my, gz), rz=yaw, mat="Kit_ConcreteMid")
            m.box((L + 0.10, 0.52, 0.12), at=(mx, my, gz + 2.2), rz=yaw,
                  mat="Kit_ConcreteLight")
            # 墙根苔藓带（补贴片，不描边）
            nlen = math.hypot(mx, my) or 1.0
            ix, iy = -mx / nlen, -my / nlen
            m.box((L, 1.5, 0.02), at=(mx + ix * 1.0, my + iy * 1.0, gz + 0.004),
                  rz=yaw, mat="Kit_DecMoss")
            if rng.random() < 0.8:
                _grass_dome(m, mx + ix * rng.uniform(1.4, 2.6),
                            my + iy * rng.uniform(1.4, 2.6),
                            rng.uniform(0.7, 1.2), rng, _grass_z(mx + ix, my + iy))
        # 段末圆墙柱
        if not near_gate(x0, y0):
            gz = _ground_z(x0, y0)
            m.cyl(0.38, 2.5, at=(x0, y0, gz), seg=16, mat="Kit_ConcreteLight")
            m.cyl(0.46, 0.10, at=(x0, y0, gz + 2.5), seg=16, mat="Kit_ConcreteLight")

    # 干净大门：两根圆门柱 + 横梁 + 黄漆牌
    gz = _ground_z(gx, gy)
    for s in (-1.0, 1.0):
        m.cyl(0.55, 3.2, at=(gx, gy + s * 3.4, gz), seg=20, mat="Kit_ConcreteLight")
        m.cyl(0.68, 0.16, at=(gx, gy + s * 3.4, gz + 3.2), seg=20, mat="Kit_PaintYellow")
    m.box((0.70, 7.6, 0.45), at=(gx, gy, gz + 3.2), mat="Kit_ConcreteLight")
    m.box((0.18, 2.8, 0.55), at=(gx, gy, gz + 3.65), mat="Kit_PaintYellow")


def build_hall_roof_clean(m):
    """主厂房干净双坡屋面：盖住原 kit 的塌口，并用檐口/山墙封住屋顶下的黑缝。"""
    L, W = 16.0, 10.0
    eave, ridge = CK.HALL_EAVE, CK.HALL_RIDGE
    pitch = math.atan2(ridge - eave, W * 0.5)
    for sign in (-1.0, 1.0):
        m.box((L + 0.7, math.hypot(W * 0.5, ridge - eave) + 0.10, 0.12),
              at=(-12.0, -3.0 + sign * W * 0.25, (eave + ridge) * 0.5 + 0.10),
              rx=-sign * pitch, mat="Kit_SteelPale")
    m.box((L + 0.8, 0.28, 0.10), at=(-12.0, -3.0, ridge + 0.16), mat="Kit_SteelPale")
    # 屋面细肋：18 道/坡，低分辨率下也有细密光带，不是一块光板
    slope_len = math.hypot(W * 0.5, ridge - eave)
    for sign in (-1.0, 1.0):
        for k in range(18):
            rx = -12.0 - (L + 0.5) * 0.5 + (k + 0.5) / 18.0 * (L + 0.5)
            m.box((0.08, slope_len, 0.045),
                  at=(rx, -3.0 + sign * W * 0.25, (eave + ridge) * 0.5 + 0.17),
                  rx=-sign * pitch, mat="Kit_SteelPale")
    # 檐口封板：堵住新屋顶与原屋顶/塌口之间的黑缝
    for sy in (-1.0, 1.0):
        m.box((L + 0.8, 0.22, 0.55), at=(-12.0, -3.0 + sy * W * 0.5, eave - 0.18),
              mat="Kit_ConcreteLight")
    # 山墙封板（三角）：两端补齐，正面不再是一只黑洞
    for gx in (-20.2, -3.8):
        pts = [(gx, -8.0, eave), (gx, 2.0, eave), (gx, -3.0, ridge),
               (gx + 0.16, -8.0, eave), (gx + 0.16, 2.0, eave), (gx + 0.16, -3.0, ridge)]
        m.add(pts, [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)],
              at=(0, 0, 0), mat="Kit_ConcreteLight")


def _distant_hall_light(m, cx, cy, w, d, h, yaw, rng):
    """远景厂房：浅色体块 + 软蓝屋顶 + 深蓝灰窗带（不做锈/破屋顶）。"""
    m.box((w, d, h), at=(cx, cy, OUTSKIRT_Z), rz=yaw, mat="Kit_ConcreteLight")
    m.box((w + 0.5, d + 0.5, 0.30), at=(cx, cy, OUTSKIRT_Z + h), rz=yaw,
          mat="Kit_SteelPale")
    for side in (-1.0, 1.0):
        ox = math.sin(yaw) * (d * 0.5 + 0.03) * side
        oy = math.cos(yaw) * (d * 0.5 + 0.03) * side
        n = max(2, int(w / 4.0))
        for k in range(n):
            t = (k + 0.5) / n - 0.5
            px = cx + math.cos(yaw) * w * t + ox
            py = cy + math.sin(yaw) * w * t + oy
            m.box((1.6, 0.06, 1.0), at=(px, py, OUTSKIRT_Z + h * 0.55),
                  rz=yaw, mat="Kit_GlassDark")


def build_outskirts_casual(m):
    """墙外：整片浅绿草地 + 两排圆树 + 四面远景厂房 + 三座料仓；不再铺废料/电杆。"""
    rng = random.Random(SEED + 104)
    outer = [
        (-110, -85), (-116, -10), (-106, 55), (-80, 82), (-20, 92),
        (50, 86), (96, 64), (112, 28), (116, -16), (100, -68),
        (64, -88), (8, -94), (-46, -92), (-82, -86),
    ]
    m.poly_extrude(outer, OUTSKIRT_Z - 0.40, OUTSKIRT_Z, mat="Kit_CpGrass")

    # 大地色斑（平贴，不描边）：只做几十块，打破整片绿
    for k in range(24):
        x = rng.uniform(-100, 100)
        y = rng.uniform(-78, 78)
        if abs(x) < YARD_X / 2 + 2 and abs(y) < YARD_Y / 2 + 2:
            continue
        rx = rng.uniform(4.0, 11.0)
        ry = rx * rng.uniform(0.55, 0.90)
        mat = rng.choice(("Kit_DecSoil", "Kit_DecMoss", "Kit_DecGrass", "Kit_DecSoil"))
        m.poly_extrude(_blob(rng, rx, ry, 10), OUTSKIRT_Z + 0.002, OUTSKIRT_Z + 0.018,
                       at=(x, y, 0), mat=mat)

    # 缓草坡
    for (x, y, r, h) in ((-55, 50, 10.0, 2.2), (58, 44, 9.0, 1.8),
                         (-60, -52, 9.0, 2.0), (0, -65, 11.0, 2.4)):
        _mound(m, x, y, r, h, "Kit_CpGrass", rng)

    # 两排圆树（只种阔叶，不要枯枝）
    def row(x0, y0, x1, y1, spacing):
        n = max(2, int(math.hypot(x1 - x0, y1 - y0) / spacing))
        for i in range(n):
            t = (i + 0.5) / n
            x = x0 + (x1 - x0) * t + rng.uniform(-2.5, 2.5)
            y = y0 + (y1 - y0) * t + rng.uniform(-2.5, 2.5)
            if abs(x) < YARD_X / 2 + 5 and abs(y) < YARD_Y / 2 + 5:
                continue
            _broadleaf(m, x, y, rng.uniform(4.5, 7.5), rng, z=OUTSKIRT_Z)

    row(-72, -52, 72, -52, 9.0)
    row(-72, 46, 72, 46, 9.0)
    row(-68, -48, -68, 44, 9.0)
    row(68, -48, 68, 44, 9.0)

    # 墙外近带：圆草丘
    for i in range(len(WALL_LINE)):
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[(i + 1) % len(WALL_LINE)]
        for t in (0.1, 0.3, 0.5, 0.7, 0.9):
            wx = x0 + (x1 - x0) * t
            wy = y0 + (y1 - y0) * t
            nl = math.hypot(wx, wy) or 1.0
            out = rng.uniform(2.0, 6.0)
            cx = wx + wx / nl * out
            cy = wy + wy / nl * out
            if rng.random() < 0.8:
                _grass_dome(m, cx, cy, rng.uniform(0.8, 1.3), rng, OUTSKIRT_Z + 0.02)

    # 远景厂房 / 料仓
    for (cx, cy, w, d, h, yaw) in ((-70, 36, 20, 10, 8.0, 5), (66, 26, 18, 9, 7.0, -4),
                                   (-64, -60, 18, 10, 7.5, 8), (60, -62, 16, 9, 7.0, 3)):
        _distant_hall_light(m, cx, cy, w, d, h, math.radians(yaw), rng)
    for (cx, cy, r, h) in ((-84, 4, 3.5, 11.0), (82, -8, 3.2, 10.0), (4, -80, 3.0, 9.0)):
        m.cyl(r, h, at=(cx, cy, OUTSKIRT_Z), seg=24, mat="Kit_SteelPale")
        m.revolve([(r, 0.0), (r * 1.05, h * 0.9), (0.0, h * 1.05)],
                  at=(cx, cy, OUTSKIRT_Z), seg=24, mat="Kit_SteelPale")

    # 树线/田野里的圆草丘
    for k in range(80):
        x = rng.uniform(-100, 100)
        y = rng.uniform(-80, 80)
        if abs(x) < YARD_X / 2 + 3 and abs(y) < YARD_Y / 2 + 3:
            continue
        _grass_dome(m, x, y, rng.uniform(0.8, 1.5), rng, OUTSKIRT_Z + 0.02)


def build_vegetation_casual(m):
    """圆润植被：墙根草丘、花园草丘、花坛树、少量灌木/藤蔓/圆桶。"""
    rng = random.Random(SEED + 105)

    # 花园草丘
    for (zx, zy, zr) in MOSS_ZONES:
        for k in range(14):
            a = rng.uniform(0, math.tau)
            r = math.sqrt(rng.random()) * zr * 0.78
            _grass_dome(m, zx + math.cos(a) * r, zy + math.sin(a) * r,
                        rng.uniform(0.8, 1.4), rng, GROUND_Z - 0.012)

    # 场内的花坛树（圆形花池）
    for (x, y, h) in ((-24.0, -7.5, 5.2), (6.0, -8.0, 5.0), (22.0, -14.0, 4.8)):
        _planter_tree(m, x, y, h, rng)
    # 花园里直接种的树
    _broadleaf(m, 32.0, -24.0, 5.2, rng, z=GROUND_Z - 0.012)

    # 圆灌木
    for (x, y, r) in ((-22, -9, 1.4), (8, -5, 1.3), (30, -12, 1.4),
                      (-44, 10, 1.3), (44, -22, 1.3), (-14, 20, 1.2)):
        _shrub(m, x, y, r, rng, z=_grass_z(x, y))

    # 藤蔓（只留两处，贴墙圆叶）
    for (x, y, yaw, h) in ((-16.0, -8.08, 0, 4.2), (-36.0, -2.58, 0, 6.0)):
        _vine(m, x, y, math.radians(yaw), h, rng)

    # 花园小花簇（圆点，不是薄片）
    for (cx, cy) in ((10.0, -24.0), (32.0, -24.0), (-38.0, 24.0)):
        for k in range(5):
            a = rng.uniform(0, math.tau)
            r = math.sqrt(rng.random()) * 2.0
            m.sphere(rng.uniform(0.06, 0.10),
                     at=(cx + math.cos(a) * r, cy + math.sin(a) * r, GROUND_Z + 0.06),
                     seg=6, rings=3,
                     mat=rng.choice(("Kit_PaintYellow", "Kit_CpFoliageLight", "Kit_Coral")),
                     smooth=False)

    # 唯一一处圆桶 + 木箱，放在主厂房东侧空地
    for k in range(3):
        x = 3.5 + rng.uniform(-0.5, 0.5)
        y = -5.0 + rng.uniform(-0.5, 0.5)
        gz = _ground_z(x, y)
        m.cyl(0.32, 0.86, at=(x, y, gz), seg=8, mat="Kit_Rust")
        m.cyl(0.34, 0.05, at=(x, y, gz + 0.30), seg=8, mat="Kit_RustDark")
    for k in range(2):
        x = 5.0 + k * 0.9
        y = -5.5
        m.box((0.8, 0.7, 0.6), at=(x, y, _ground_z(x, y)), rz=0.3, mat="Kit_WoodDark")


# ---------------------------------------------------------------------------
# 装配
# ---------------------------------------------------------------------------

def build_all():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    # 材质统一：单件版先建（含背剔预览节点），六件版与新几何复用同名材质。
    mats = CK.make_materials()
    K.material_of = lambda slot: mats[slot]

    objs = []

    # 单件版构件
    for name, fn, (x, y), yaw in SINGLE:
        acc = CK.MeshAcc()
        fn(acc)
        obj = CK.join_to_object(acc, "CP_" + name, mats)
        obj.location = (x, y, 0.0)
        obj.rotation_euler = (0.0, 0.0, math.radians(yaw))
        objs.append(obj)

    # 六件版分区件
    for part, (x, y), yaw in SIX:
        mod = importlib.import_module("mod_" + part)
        m = K.Mesher()
        mod.build(m)
        obj = m.to_object("CP6_" + part.capitalize())
        obj.location = (x, y, 0.0)
        obj.rotation_euler = (0.0, 0.0, math.radians(yaw))
        objs.append(obj)

    # 新几何（拆成几个对象，方便单件排查）
    # 【休闲化重做】旧的地表/道路/围墙/植被函数保留作对照，现役走 *_casual 这套：
    # 大板干净地坪 + 三处圆形苔藓花园 + 连续矮墙 + 圆润草丘/树，杂物大幅减量。
    for name, fn in (("Ground", build_ground_casual), ("Roads", build_roads_casual),
                     ("Walls", build_walls_casual), ("HallBridge", build_hall_bridge),
                     ("HallRoof", build_hall_roof_clean),
                     ("Outskirts", build_outskirts_casual),
                     ("Vegetation", build_vegetation_casual)):
        m = K.Mesher()
        fn(m)
        obj = m.to_object("CP_" + name)
        objs.append(obj)
        nv, nf, nt = m.counts()
        log("新几何 %-10s tris=%-7d slots=%d" % (name, nt, len(m.slot_order)))

    return objs


def report(objs):
    total = 0
    for o in objs:
        nt = sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        total += nt
        mn = [min((o.matrix_world @ v.co)[i] for v in o.data.vertices) for i in range(3)]
        mx = [max((o.matrix_world @ v.co)[i] for v in o.data.vertices) for i in range(3)]
        log("%-16s tris=%-7d size=%.1f x %.1f x %.1f  pos=(%.1f, %.1f)" % (
            o.name, nt, mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2],
            o.location.x, o.location.y))
    log("TOTAL tris=%d objects=%d range=%.1f x %.1f" % (
        total, len(objs),
        max((o.matrix_world @ v.co).x for o in objs for v in o.data.vertices)
        - min((o.matrix_world @ v.co).x for o in objs for v in o.data.vertices),
        max((o.matrix_world @ v.co).y for o in objs for v in o.data.vertices)
        - min((o.matrix_world @ v.co).y for o in objs for v in o.data.vertices)))


def write_layout(objs):
    """世界包围盒清单（供关卡地形足印生成脚本读取；单一真源，不手抄尺寸）。"""
    import json
    # Blender 惰性求值：设完 location/rotation 后 matrix_world 仍是旧值，必须先 update
    # （kit README「建模踩坑 #4」原话：不更新就量，量到的是本地坐标）。
    bpy.context.view_layer.update()
    data = []
    for o in objs:
        pts = [o.matrix_world @ v.co for v in o.data.vertices]
        if not pts:
            continue
        data.append({
            "name": o.name,
            "min": [round(min(p[i] for p in pts), 3) for i in range(3)],
            "max": [round(max(p[i] for p in pts), 3) for i in range(3)],
        })
    path = os.path.join(WORK_DIR, "merged_layout.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"yard": [YARD_X, YARD_Y], "objects": data,
                   "outline": YARD_OUTLINE, "wallLine": WALL_LINE,
                   "wallGaps": sorted(WALL_GAPS)},
                  f, ensure_ascii=False, indent=1)
    log("布局包围盒 %s" % path)


def render(views, res, samples):
    scene = bpy.context.scene
    # 休闲化出图：亮一档曝光、浅蓝天穹、高角度暖阳、整片浅绿 backdrop。
    A.EXPOSURE = -0.10
    A.setup_render(scene, res, samples)
    world = A.setup_sky(scene, strength=0.55)
    ramp = next(n for n in world.node_tree.nodes if n.type == "VALTORGB")
    cols = ("E9F2E4", "C6E6F0", "8FC8E8")          # 地平线暖白 → 中天浅蓝 → 天顶蓝
    for e, hx in zip(sorted(ramp.color_ramp.elements, key=lambda e: e.position), cols):
        e.color = (*[K.srgb_to_linear(c) for c in A.ST_hex(hx)], 1.0)
    A.add_sun(scene, energy=3.0, elev_deg=58.0, rot_deg=30.0, color=(1.0, 0.97, 0.90))
    A.add_fill(scene)
    bm = K.Mesher()
    bm.box((700.0, 700.0, 0.12), at=(-350.0, -350.0, -0.5), mat="Kit_CpGrass")
    bm.to_object("_chem_backdrop")
    os.makedirs(OUT_IMG, exist_ok=True)
    A.render_views(scene, OUT_IMG, views, res, samples)


def main():
    opts = parse_args()
    os.makedirs(OUT_IMG, exist_ok=True)
    os.makedirs(WORK_DIR, exist_ok=True)

    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    objs = build_all()
    report(objs)
    write_layout(objs)

    if opts["export"]:
        os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
        ST.export_fbx(objs, OUT_FBX)
        log("FBX 导出 %s (%d bytes)" % (OUT_FBX, os.path.getsize(OUT_FBX)))

    if opts["render"]:
        views = VIEWS if not opts["views"] else [v for v in VIEWS if v[0] in opts["views"]]
        render(views, opts["res"], opts["samples"])

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK_DIR, "merged_debug.blend"))
    log("完成，用时 %.1fs" % (time.time() - T0))


if __name__ == "__main__":
    main()
