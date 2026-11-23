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
    ("hero",      ( 58, -70, 54), (  0,   0,  7), 35),
    ("overview",  (-70, -56, 46), (  2,   0,  8), 35),
    ("north",     (  0,  72, 42), (  0,   0, 10), 32),
    ("street",    ( 12, -20, 3.2), (-24,   5, 5.8), 32),
    ("gate",      (-60, -28, 7.5),(-44, -14, 2.2), 32),
    ("south",     ( 38,  66, 38), ( 10,  -4,  8), 35),
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
    # 枝干/斜撑都是圆柱：10 段 + 平滑，避免在像素描边下读出六棱柱。
    m.extrude_along(_circle_profile(r, 10), p0, p1, mat=mat, smooth=True)


def _ellipsoid(m, rx, ry, rz, at, mat, seg=18, rings=12, smooth=True,
               rz_rot=0.0, ry_rot=0.0, rx_rot=0.0):
    """任意轴长的椭球：花瓣/树冠瓣/灌木团/草叶的基本单元（相当于云场的 blob 瓣）。

    与 `Mesher.sphere` 同一套 UV 球拓扑（极点收敛、四边面），只是三轴独立缩放；
    `at` = 球心。段数默认 18/12（像素描边下低于这个数容易读出折线）；
    小叶片可显式给 12/8。`rz_rot/ry_rot/rx_rot` 让长轴可以朝外倾斜（草叶/花瓣用）。
    """
    seg = max(10, int(seg))
    rings = max(6, int(rings))
    v, f, sf = [], [], set()
    ring_idx = []
    for i in range(rings + 1):
        lat = -math.pi * 0.5 + math.pi * i / rings
        zz, rr = math.sin(lat), math.cos(lat)
        if rr <= 1e-6:
            ring_idx.append(("pole", len(v)))
            v.append((0.0, 0.0, zz))
        else:
            start = len(v)
            for k in range(seg):
                a = math.tau * k / seg
                v.append((rr * math.cos(a), rr * math.sin(a), zz))
            ring_idx.append(("ring", start))
    for k in range(len(ring_idx) - 1):
        t0, s0 = ring_idx[k]
        t1, s1 = ring_idx[k + 1]
        if t0 == "ring" and t1 == "ring":
            for i in range(seg):
                j = (i + 1) % seg
                f.append((s0 + i, s0 + j, s1 + j, s1 + i))
                sf.add(len(f) - 1)
        elif t0 == "pole":
            for i in range(seg):
                j = (i + 1) % seg
                f.append((s0, s1 + j, s1 + i))
                sf.add(len(f) - 1)
        else:
            for i in range(seg):
                j = (i + 1) % seg
                f.append((s0 + i, s0 + j, s1))
                sf.add(len(f) - 1)
    m.add(v, f, at=at, rz=rz_rot, ry=ry_rot, rx=rx_rot, mat=mat,
          smooth=smooth, smooth_faces=sf, scale=(rx, ry, rz))


def _canopy(m, x, y, z, crown_r, rng, n=9, spread=0.72, mats=None):
    """多瓣树冠：中心瓣 + 8~10 个环绕瓣 + 6 个小亮瓣。

    云场的做法是"多个 blob 深度互穿"——单球或少量大球都会读成气球。这里把
    数量提上来、单瓣缩小到冠半径的 0.4~0.62，并**按高度分色**：下沿暗、腰中调、
    上沿亮，剪影才有叶团的层层起伏。返回各瓣参数（供枝干对接）。
    """
    lobes = []
    # 中心瓣：略扁，作为"冠芯"
    lobes.append((x, y, z + crown_r * 0.12, crown_r * 0.92,
                  crown_r * 0.86, crown_r * 0.72, "Kit_CpFoliageMid"))
    for k in range(n):
        a = math.tau * k / n + rng.uniform(-0.40, 0.40)
        dist = crown_r * rng.uniform(0.34, spread)
        rr = crown_r * rng.uniform(0.36, 0.58)
        lx = x + math.cos(a) * dist
        ly = y + math.sin(a) * dist
        lz = z + rng.uniform(-0.34, 0.46) * crown_r
        if lz < z - 0.08 * crown_r:
            mt = rng.choice(("Kit_CpFoliageDark", "Kit_CpFoliageDark",
                             "Kit_CpFoliageMid"))
        elif lz > z + 0.24 * crown_r:
            mt = rng.choice(("Kit_CpFoliageLight", "Kit_CpFoliageMid",
                             "Kit_CpFoliageLight"))
        else:
            mt = rng.choice(("Kit_CpFoliageMid", "Kit_CpFoliageMid",
                             "Kit_CpFoliageLight", "Kit_CpFoliageDark"))
        lobes.append((lx, ly, lz, rr, rr * rng.uniform(0.82, 1.10),
                      rr * rng.uniform(0.72, 0.95), mt))
    # 冠面小亮瓣：像云朵表面的鼓包，专门破大轮廓
    for k in range(6):
        a = rng.uniform(0, math.tau)
        rr = crown_r * rng.uniform(0.16, 0.30)
        lobes.append((x + math.cos(a) * crown_r * rng.uniform(0.28, 0.68),
                      y + math.sin(a) * crown_r * rng.uniform(0.28, 0.68),
                      z + crown_r * rng.uniform(0.28, 0.66), rr, rr,
                      rr * rng.uniform(0.78, 0.96),
                      rng.choice(("Kit_CpFoliageLight", "Kit_CpFoliageMid",
                                  "Kit_CpFoliageLight"))))
    for (lx, ly, lz, rx, ry, rz, mt) in lobes:
        _ellipsoid(m, rx, ry, rz, (lx, ly, lz), mt, seg=18, rings=12)
    return [(lx, ly, lz, rx, ry, rz) for (lx, ly, lz, rx, ry, rz, _mt) in lobes]


def _broadleaf(m, x, y, h, rng, z=None):
    """阔叶树：根盘 + 锥形主干 + 3~4 根可见主枝 + 6~9 瓣圆冠。

    【为什么树要这么做】上一版是"一根杆顶一个球"，在像素描边下就是一个气球。
    这一版把树拆成**可读的三层**：根盘（落地）、枝干（冠下负空间）、多瓣冠
    （剪影起伏）。冠底抬到 1.8~2.6 m，路人能从树下看见后面的东西，场景才有纵深。
    """
    z = _grass_z(x, y) if z is None else z
    trunk_h = h * rng.uniform(0.36, 0.46)
    base_r = max(0.11, h * 0.042)
    # 根盘 + 主干（下粗上细）
    m.cyl(base_r * 1.55, min(0.60, trunk_h * 0.28), at=(x, y, z), seg=18,
          r_top=base_r * 1.06, mat="Kit_WoodDark", smooth=True)
    m.cyl(base_r, trunk_h, at=(x, y, z), seg=18, r_top=base_r * 0.52,
          mat="Kit_WoodDark", smooth=True)
    crown_r = h * rng.uniform(0.28, 0.36)
    crown_z = z + trunk_h + crown_r * 0.58
    lobes = _canopy(m, x, y, crown_z, crown_r, rng,
                    n=rng.randint(8, 10), spread=0.66)
    # 主枝：从干顶伸进冠内，三根即可读出结构（太多会变蜘蛛）
    for (lx, ly, lz, rx, ry, rz) in lobes[1:4]:
        start = Vector((x, y, z + trunk_h * rng.uniform(0.62, 0.86)))
        end = Vector((lx, ly, lz - rz * 0.45))
        d = end - start
        if d.length < 0.2:
            continue
        _branch(m, start, end, base_r * rng.uniform(0.34, 0.46), "Kit_WoodDark")
        if rng.random() < 0.65:
            mid = start + d * 0.55
            fork = end + Vector((rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6),
                                 rng.uniform(0.1, 0.5))) * crown_r * 0.4
            _branch(m, mid, fork, base_r * 0.22, "Kit_WoodDark")


def _conifer(m, x, y, h, rng, z=None):
    """针叶树：主干 + 6 层圆唇锥冠（层间留缝，轮廓有节奏）。"""
    z = _grass_z(x, y) if z is None else z
    m.cyl(max(0.10, h * 0.035), h * 0.34, at=(x, y, z), seg=16,
          r_top=h * 0.024, mat="Kit_WoodDark", smooth=True)
    tiers = 6
    for k in range(tiers):
        t = k / (tiers - 1.0)
        z0 = z + h * (0.16 + 0.68 * t)
        r = h * (0.205 - 0.125 * t) * rng.uniform(0.94, 1.06)
        hh = h * (0.30 - 0.07 * t)
        profile = [(r * 0.92, 0.0), (r, hh * 0.10), (r * 0.80, hh * 0.42),
                   (r * 0.38, hh * 0.82), (0.0, hh)]
        m.revolve(profile, at=(x + rng.uniform(-0.08, 0.08) * r,
                               y + rng.uniform(-0.08, 0.08) * r, z0),
                  seg=20, rz=rng.uniform(0, math.tau),
                  mat=rng.choice(("Kit_CpFoliageDark", "Kit_CpFoliageMid",
                                  "Kit_CpFoliageDark")), smooth=True)


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
    """圆灌木：7~11 个互穿椭球团 + 2~3 粒小花；轮廓像一小朵绿云。"""
    z = _grass_z(x, y) if z is None else z
    n = rng.randint(7, 11)
    for k in range(n):
        a = math.tau * k / n + rng.uniform(-0.4, 0.4)
        dist = r * rng.uniform(0.0, 0.72)
        rr = r * rng.uniform(0.34, 0.60)
        _ellipsoid(m, rr, rr * rng.uniform(0.85, 1.15), rr * rng.uniform(0.72, 1.0),
                   (x + math.cos(a) * dist, y + math.sin(a) * dist,
                    z + rr * rng.uniform(0.55, 0.95)),
                   rng.choice(("Kit_CpFoliageMid", "Kit_CpMoss",
                               "Kit_CpFoliageDark", "Kit_CpFoliageLight")),
                   seg=18, rings=12)
    for k in range(rng.randint(2, 3)):
        a = rng.uniform(0, math.tau)
        rr = r * rng.uniform(0.35, 0.65)
        m.sphere(0.055, at=(x + math.cos(a) * rr, y + math.sin(a) * rr,
                            z + r * rng.uniform(0.55, 0.85)),
                 seg=16, rings=10, smooth=True,
                 mat=rng.choice(("Kit_PaintYellow", "Kit_ConcreteLight",
                                 "Kit_CpFoliageLight")))


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
    """墙根藤蔓：3 根攀爬主茎 + 5 层贴墙叶簇（扁圆叶片，不是一串黑珠子）。

    x/y = 墙脚外 5~10 cm 的起点；yaw = 沿墙方向；height = 攀爬高度。
    叶片用扁椭球（16 段），大小/角度错开；上一版 6 段小球在像素描边下
    读成一排黑阶梯，这一版从拓扑上换掉。
    """
    dx, dy = math.cos(yaw), math.sin(yaw)
    nl = math.hypot(x, y) or 1.0
    nx, ny = -x / nl, -y / nl                    # 贴墙外法线（朝院内）
    for k in range(3):
        off = rng.uniform(-0.55, 0.55)
        climb = height * rng.uniform(0.55, 1.0)
        p0 = Vector((x + dx * off, y + dy * off, GROUND_Z + 0.04))
        p1 = p0 + Vector((dx * rng.uniform(0.10, 0.55),
                          dy * rng.uniform(0.10, 0.55), climb))
        _branch(m, p0, p1, 0.025, "Kit_CpFoliageDark")
        for t in (0.32, 0.52, 0.72, 0.90, 1.0):
            p = p0.lerp(p1, t)
            for j in range(rng.randint(4, 6)):
                a = rng.uniform(0, math.tau)
                rr = rng.uniform(0.10, 0.20)
                _ellipsoid(m, rr * 1.25, rr, rr * 0.55,
                           (p.x + math.cos(a) * rr * 1.15 + nx * 0.04,
                            p.y + math.sin(a) * rr * 1.15 + ny * 0.04,
                            p.z + rng.uniform(-0.12, 0.12)),
                           rng.choice(("Kit_CpFoliageDark", "Kit_CpFoliageMid",
                                       "Kit_CpFoliageLight")),
                           seg=16, rings=10)


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


def _grass_clump(m, x, y, size, rng, z=None, flowers=False):
    """成簇草：基部 2~3 个圆草团 + 6~10 根锥形草叶（向外倾），可选花粒。

    【为什么不是"小绿豆"】上一版每簇只有一个 `revolve` 小圆顶，远看就是散落的绿点。
    改成"团 + 叶"两层后：贴地有体量、上有参差的高度与方向，像素描边能勾出草丛剪影；
    叶根 3~6 cm、梢部收尖，是封闭实体（不是单面草卡），不会再出现黑片。
    """
    z = _grass_z(x, y) if z is None else z
    size = max(0.45, float(size))
    base_mats = ("Kit_CpGrass", "Kit_CpMoss", "Kit_CpFoliageLight")
    for k in range(rng.randint(2, 3)):
        rr = 0.26 * size * rng.uniform(0.85, 1.15)
        _ellipsoid(m, rr, rr * rng.uniform(0.84, 1.12), rr * rng.uniform(0.58, 0.82),
                   (x + rng.uniform(-0.22, 0.22) * size,
                    y + rng.uniform(-0.22, 0.22) * size,
                    z + rr * 0.38),
                   rng.choice(base_mats), seg=16, rings=10)
    blades = []
    # 【像素可读性】草叶不再是细锥（细锥在描边下会连成一片黑刺），改成 3~5 片
    # **扁圆叶**：一根长轴椭球朝外倾，叶宽 8~14 cm，屏幕空间能读出圆头轮廓。
    n = rng.randint(3, 5)
    a0 = rng.uniform(0, math.tau)
    for k in range(n):
        a = a0 + math.tau * k / n + rng.uniform(-0.30, 0.30)
        lean = rng.uniform(0.18, 0.52)
        hh = size * rng.uniform(0.38, 0.72)
        w = size * rng.uniform(0.085, 0.135)
        bx = x + math.cos(a) * 0.11 * size
        by = y + math.sin(a) * 0.11 * size
        cx = bx + math.sin(lean * math.cos(a)) * hh * 0.50
        cy = by + math.sin(lean * math.sin(a)) * hh * 0.50
        cz = z + 0.04 + math.cos(lean) * hh * 0.50
        _ellipsoid(m, w, w * rng.uniform(0.72, 1.0), hh * 0.52,
                   (cx, cy, cz),
                   rng.choice(("Kit_CpGrass", "Kit_CpGrass", "Kit_CpMoss",
                               "Kit_CpFoliageLight")),
                   seg=12, rings=8, ry_rot=lean * math.cos(a),
                   rx_rot=-lean * math.sin(a))
        blades.append((a, lean, hh, bx, by))
    if flowers:
        for k in range(rng.randint(2, 4)):
            a, lean, hh, bx, by = rng.choice(blades)
            px = bx + math.sin(lean * math.cos(a)) * hh
            py = by + math.sin(lean * math.sin(a)) * hh
            pz = z + 0.04 + math.cos(lean) * hh
            m.sphere(rng.uniform(0.045, 0.065), at=(px, py, pz), seg=16, rings=10,
                     smooth=True, mat=rng.choice(("Kit_PaintYellow", "Kit_ConcreteLight",
                                                  "Kit_CpFoliageLight")))


def _boulder(m, x, y, r, rng, z=None, mat="Kit_ConcreteDark"):
    """园景石：2~3 个互穿扁椭球 + 顶面苔帽。"""
    z = _grass_z(x, y) if z is None else z
    for k in range(rng.randint(2, 3)):
        rr = r * rng.uniform(0.55, 0.9)
        _ellipsoid(m, rr, rr * rng.uniform(0.75, 1.05), rr * rng.uniform(0.50, 0.75),
                   (x + rng.uniform(-0.35, 0.35) * r,
                    y + rng.uniform(-0.35, 0.35) * r, z + rr * 0.30),
                   mat, seg=18, rings=12)
    _ellipsoid(m, r * 0.62, r * 0.55, r * 0.16,
               (x, y, z + r * 0.55), "Kit_CpMoss", seg=18, rings=10)


def _grate(m, x, y, yaw, z):
    """排水篦子：外框 + 凹槽 + 6 根格栅条（像素下是清楚的工业小件）。"""
    m.box((0.92, 0.62, 0.06), at=(x, y, z - 0.03), rz=yaw, mat="Kit_ConcreteDark")
    m.box((0.74, 0.46, 0.05), at=(x, y, z + 0.005), rz=yaw, mat="Kit_Iron")
    for k in range(6):
        off = -0.30 + k * 0.12
        m.box((0.08, 0.44, 0.035), at=(x + math.sin(yaw) * off,
                                       y + math.cos(yaw) * off, z + 0.045),
              rz=yaw, mat="Kit_GlassDark")


def _manhole(m, x, y, z, r=0.44):
    """圆井盖：座圈 + 盖板 + 十字筋，20 段旋转体。"""
    m.revolve([(r * 1.12, 0.0), (r * 1.12, 0.05), (r, 0.07), (r * 0.92, 0.09)],
              at=(x, y, z), seg=20, mat="Kit_ConcreteDark", smooth=True)
    m.cyl(r * 0.90, 0.075, at=(x, y, z + 0.015), seg=20, mat="Kit_Iron")
    for k in range(2):
        m.box((r * 1.45, 0.07, 0.025), at=(x, y, z + 0.09), rz=k * math.pi * 0.5,
              mat="Kit_ConcreteDark")


def _planter_tree(m, x, y, h, rng):
    """圆形花坛树：石砌池壁 + 压顶 + 土面 + 花 + 多瓣冠树。"""
    gz = _ground_z(x, y)
    m.cyl(1.52, 0.16, at=(x, y, gz), seg=24, mat="Kit_ConcreteDark")
    m.cyl(1.44, 0.40, at=(x, y, gz), seg=24, mat="Kit_ConcreteMid")
    # 石砌竖缝（12 道），把圆柱读成砌块池
    for k in range(12):
        a = math.tau * k / 12.0
        m.box((0.05, 0.10, 0.38), at=(x + math.cos(a) * 1.44, y + math.sin(a) * 1.44,
                                     gz + 0.01), rz=a, mat="Kit_ConcreteDark")
    m.revolve([(1.52, 0.0), (1.52, 0.06), (1.40, 0.14), (1.30, 0.18)],
              at=(x, y, gz + 0.40), seg=24, mat="Kit_ConcreteLight")
    m.cyl(1.28, 0.06, at=(x, y, gz + 0.55), seg=24, mat="Kit_DecSoil")
    for k in range(7):
        a = rng.uniform(0, math.tau)
        rr = rng.uniform(0.45, 1.12)
        m.sphere(rng.uniform(0.05, 0.09),
                 at=(x + math.cos(a) * rr, y + math.sin(a) * rr, gz + 0.64),
                 seg=16, rings=10, smooth=True,
                 mat=rng.choice(("Kit_PaintYellow", "Kit_ConcreteLight",
                                 "Kit_CpFoliageLight")))
    _broadleaf(m, x, y, h, rng, z=gz + 0.58)


def _clean_road(m, pts, width, z, center_line=False):
    """连续干净的沥青路：沥青面 + 两侧排水沟 + 带倒角的路缘石（+ 可选中心虚线）。"""
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
        ux, uy = math.cos(yaw), math.sin(yaw)
        nx, ny = -math.sin(yaw), math.cos(yaw)
        m.box((L + 0.04, width, 0.06), at=(mx, my, z - 0.03), rz=yaw,
              mat="Kit_CpAsphalt")
        for side in (-1.0, 1.0):
            # 排水沟（暗缝）
            off = width * 0.5 + 0.12
            m.box((L + 0.04, 0.26, 0.045), at=(mx + nx * off * side,
                                               my + ny * off * side, z - 0.035),
                  rz=yaw, mat="Kit_ConcreteDark")
            # 路缘石：下座 + 压顶倒角
            off = width * 0.5 + 0.35
            m.box((L + 0.04, 0.26, 0.14), at=(mx + nx * off * side,
                                              my + ny * off * side, z - 0.075),
                  rz=yaw, mat="Kit_ConcreteMid")
            m.box((L + 0.04, 0.18, 0.055), at=(mx + nx * off * side,
                                               my + ny * off * side, z + 0.065),
                  rz=yaw, mat="Kit_ConcreteLight")
        if center_line:
            step = 4.0
            n = max(1, int(L / step))
            for k in range(n):
                t = (k + 0.5) / n - 0.5
                m.box((1.8, 0.10, 0.025), at=(mx + ux * L * t, my + uy * L * t,
                                              z + 0.026), rz=yaw, mat="Kit_PaintYellow")
        # 排水篦子：沿沟每 14 m 一枚，左右交替
        n = max(1, int(L / 14.0))
        for k in range(n):
            t = (k + 0.5) / n - 0.5
            side = -1.0 if k % 2 == 0 else 1.0
            off = width * 0.5 + 0.12
            _grate(m, mx + ux * L * t + nx * off * side,
                   my + uy * L * t + ny * off * side, yaw, z + 0.01)


def build_ground_casual(m):
    """休闲化地坪：带倒角的奶油大板 + 断续苔缝 + 圆花园 + 排水井盖。

    【板为什么要"两层"】一层平板在 30° 俯视 + 像素描边下是一条硬白边，像纸片。
    每块板做"缩进的面层 + 外扩 5 cm 的暗色垫层"，板边就有了 5 cm 的**倒角台阶**；
    板缝里的苔藓是断续的细段（不是通长绿线），只在缝里长，不爬板面。
    """
    rng = random.Random(SEED + 1)
    SLAB_RECTS.clear()
    MISSING_RECTS.clear()

    # 土基：浅沙（只在板缝/花园/缺板处露出）
    m.poly_extrude(YARD_OUTLINE, GROUND_Z - 0.34, GROUND_Z - 0.06, mat="Kit_CpSand")

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
            top_z = GROUND_Z + rng.uniform(-0.006, 0.004)
            # 垫层外扩 5 cm（读作倒角/滴水线），面层板缩进
            m.box((w + 0.10, d + 0.10, 0.10), at=(cx, cy, top_z - 0.16),
                  mat="Kit_ConcreteDark")
            mat = rng.choices(("Kit_ConcreteLight", "Kit_ConcreteMid",
                               "Kit_ConcreteDark"), weights=(68, 27, 5))[0]
            m.box((w, d, 0.055), at=(cx, cy, top_z - 0.055), mat=mat)
            SLAB_RECTS.append((cx - w * 0.5, cy - d * 0.5,
                               cx + w * 0.5, cy + d * 0.5, top_z))

    # 板缝苔藓：断续细段（不是通长绿线），宽度/间隔随机
    def seam_x(x):
        z = -33.0
        while z < 33.0:
            seg = rng.uniform(0.8, 2.0)
            gap = rng.uniform(0.7, 2.2)
            if point_in_poly(x, z + seg * 0.5, YARD_OUTLINE) and rng.random() > 0.18:
                wdt = rng.uniform(0.08, 0.18)
                m.box((wdt, seg, 0.018), at=(x, z + seg * 0.5, GROUND_Z - 0.030),
                      mat="Kit_DecMoss")
                if rng.random() < 0.15:
                    _grass_clump(m, x + rng.uniform(-0.1, 0.1), z + seg * 0.5,
                                 rng.uniform(0.5, 0.9), rng, GROUND_Z - 0.012)
            z += seg + gap

    def seam_z(z):
        x = -47.0
        while x < 47.0:
            seg = rng.uniform(0.8, 2.0)
            gap = rng.uniform(0.7, 2.2)
            if point_in_poly(x + seg * 0.5, z, YARD_OUTLINE) and rng.random() > 0.18:
                wdt = rng.uniform(0.08, 0.18)
                m.box((seg, wdt, 0.018), at=(x + seg * 0.5, z, GROUND_Z - 0.030),
                      mat="Kit_DecMoss")
                if rng.random() < 0.15:
                    _grass_clump(m, x + seg * 0.5, z + rng.uniform(-0.1, 0.1),
                                 rng.uniform(0.5, 0.9), rng, GROUND_Z - 0.012)
            x += seg + gap

    for x in xs[1:-1]:
        seam_x(x)
    for z in zs[1:-1]:
        seam_z(z)

    # 三处圆形苔藓花园：石砌边圈 + 沙床 + 苔藓丘 + 圆水洼 + 园景石
    puddles = ((10.0, -24.0, 3.4, 2.5), (32.0, -24.0, 2.4, 1.8),
               (-38.0, 24.0, 2.2, 1.6))
    for (zx, zy, zr), (px, py, prx, pry) in zip(MOSS_ZONES, puddles):
        # 石砌边圈（18 块，切线摆放，深浅交替）
        n = 18
        for k in range(n):
            a = math.tau * (k + 0.5) / n
            bx = zx + math.cos(a) * zr * 0.99
            by = zy + math.sin(a) * zr * 0.99
            bw = rng.uniform(0.62, 0.78)
            bd = rng.uniform(0.30, 0.38)
            bh = rng.uniform(0.14, 0.20)
            m.box((bw, bd, bh), at=(bx, by, GROUND_Z - 0.06), rz=a + math.pi * 0.5,
                  mat=rng.choice(("Kit_ConcreteLight", "Kit_ConcreteMid")))
        # 沙床
        m.poly_extrude(_blob(rng, zr * 0.94, zr * 0.90, 20),
                       GROUND_Z - 0.085, GROUND_Z - 0.050,
                       at=(zx, zy, 0), mat="Kit_DecSoil")
        # 苔藓丘：3 个互穿扁椭球（有厚度的草毯，不是贴纸）
        for k in range(3):
            ox = rng.uniform(-0.22, 0.22) * zr
            oy = rng.uniform(-0.22, 0.22) * zr
            _ellipsoid(m, zr * rng.uniform(0.52, 0.68), zr * rng.uniform(0.46, 0.60),
                       rng.uniform(0.10, 0.16),
                       (zx + ox, zy + oy, GROUND_Z - 0.020),
                       "Kit_DecMoss", seg=20, rings=12)
        # 水洼：湿土唇 + 水面 + 边缘石
        m.poly_extrude(_blob(rng, prx * 1.16, pry * 1.16, 18),
                       GROUND_Z - 0.026, GROUND_Z - 0.014,
                       at=(px, py, 0), mat="Kit_DecWet")
        m.poly_extrude(_blob(rng, prx, pry, 18),
                       GROUND_Z - 0.012, GROUND_Z - 0.006,
                       at=(px + 0.15, py - 0.10, 0), mat="Kit_DecWater")
        for k in range(3):
            a = rng.uniform(0, math.tau)
            _boulder(m, px + math.cos(a) * prx * rng.uniform(0.8, 1.05),
                     py + math.sin(a) * pry * rng.uniform(0.8, 1.05),
                     rng.uniform(0.22, 0.36), rng, GROUND_Z - 0.03,
                     mat=rng.choice(("Kit_ConcreteDark", "Kit_RockDark")))

    # 广场井盖：4 处，位置避开花园/主设备
    for (mx, my) in ((-20.0, 12.0), (12.0, 6.0), (30.5, -8.0), (-34.0, -17.0)):
        if point_in_poly(mx, my, YARD_OUTLINE) and not _in_moss_zone(mx, my):
            _manhole(m, mx, my, _paved_top(mx, my) + 0.002)


def build_roads_casual(m):
    """两条干净沥青路（主路带中心虚线）+ 一条从大门向西的场外土路（带车辙）。"""
    rng = random.Random(SEED + 106)
    _clean_road(m, YARD_ROADS[0][0], 6.0, GROUND_Z + 0.02, center_line=True)
    _clean_road(m, YARD_ROADS[1][0], 4.5, GROUND_Z + 0.02)
    # 场外土路：大门 → 西侧旷野；两条压实的车辙 + 路肩草缘
    m.box((34.0, 5.4, 0.06), at=(-64.0, -17.0, OUTSKIRT_Z - 0.01), mat="Kit_DecSoil")
    for side in (-1.0, 1.0):
        m.box((34.0, 0.46, 0.022), at=(-64.0, -17.0 + side * 1.05,
                                       OUTSKIRT_Z + 0.045), mat="Kit_DecWet")
    for k in range(9):
        _grass_clump(m, -79.0 + k * 3.8, -14.0 + rng.uniform(-0.5, 0.5),
                     rng.uniform(0.6, 1.0), rng, OUTSKIRT_Z + 0.02)


def build_walls_casual(m):
    """连续矮墙的**构造级**版本：勒脚 / 墙身 / 壁柱 / 三道线脚压顶 / 壁灯 / 门柱与门扇。

    上一版只给了"一块 2.2 m 的板 + 一条压顶"，转角没有收头、墙身没有分格、门区只有一个
    横梁——在 30° 俯视里就是一圈白条。这一版把每 3.2 m 做成一个**墙跨**：勒脚外扩、
    两端壁柱压边、压顶三层出挑，墙身每 1.6 m 一道暗缝；门区补门柱、金属门扇和门卫亭。
    """
    rng = random.Random(SEED + 103)
    gx, gy = GATE_POS

    def near_gate(x, y, d=3.3):
        return (x - gx) ** 2 + (y - gy) ** 2 < d * d

    def bay(mx, my, yaw, L, gz, idx):
        # 勒脚（外扩 12 cm，暗色）
        m.box((L + 0.04, 0.64, 0.34), at=(mx, my, gz), rz=yaw,
              mat="Kit_ConcreteDark")
        # 墙身
        m.box((L + 0.02, 0.40, 1.72), at=(mx, my, gz + 0.34), rz=yaw,
              mat="Kit_ConcreteMid")
        # 墙身分格暗缝（每 1.6 m 一道，略微出头，读出预制板缝）
        njoints = max(1, int(L / 1.6))
        for j in range(njoints + 1):
            off = -L * 0.5 + L * j / njoints
            m.box((0.035, 0.44, 1.68), at=(mx + math.cos(yaw) * off,
                                           my + math.sin(yaw) * off, gz + 0.36),
                  rz=yaw, mat="Kit_ConcreteDark")
        # 压顶：暗缝 + 下压条 + 出挑檐口 + 圆边收顶（三层线脚）
        m.box((L + 0.02, 0.44, 0.045), at=(mx, my, gz + 2.05), rz=yaw,
              mat="Kit_ConcreteDark")
        m.box((L + 0.06, 0.54, 0.10), at=(mx, my, gz + 2.09), rz=yaw,
              mat="Kit_ConcreteLight")
        m.box((L + 0.02, 0.68, 0.09), at=(mx, my, gz + 2.19), rz=yaw,
              mat="Kit_ConcreteLight")
        m.box((L - 0.02, 0.54, 0.045), at=(mx, my, gz + 2.28), rz=yaw,
              mat="Kit_ConcreteMid")
        # 墙根苔藓：两侧各一条 16 cm 的断续带，只贴地
        for side in (-1.0, 1.0):
            off = 0.28 * side
            m.box((L * 0.92, 0.16, 0.016),
                  at=(mx + (-math.sin(yaw)) * off, my + math.cos(yaw) * off,
                      gz + 0.006), rz=yaw, mat="Kit_DecMoss")
        # 内侧墙根草簇（每跨 1~2 簇）
        nlen = math.hypot(mx, my) or 1.0
        ix, iy = -mx / nlen, -my / nlen
        for k in range(rng.randint(1, 2)):
            _grass_clump(m, mx + ix * rng.uniform(0.55, 1.05) +
                         math.cos(yaw) * rng.uniform(-L * 0.35, L * 0.35),
                         my + iy * rng.uniform(0.55, 1.05) +
                         math.sin(yaw) * rng.uniform(-L * 0.35, L * 0.35),
                         rng.uniform(0.55, 0.95), rng, gz + 0.004)
        # 壁灯（每 3 跨一盏，装内侧，臂 + 锥形灯罩 + 暖黄灯泡）
        if idx % 3 == 0:
            lx = mx + ix * 0.26
            ly = my + iy * 0.26
            tip = Vector((mx + ix * 0.78, my + iy * 0.78, gz + 2.02))
            _branch(m, Vector((lx, ly, gz + 2.02)), tip, 0.045, "Kit_Iron")
            m.revolve([(0.30, 0.0), (0.30, 0.05), (0.20, 0.13), (0.08, 0.17)],
                      at=(tip.x, tip.y, tip.z - 0.04), seg=16,
                      mat="Kit_ConcreteLight", smooth=True)
            m.sphere(0.11, at=(tip.x, tip.y, tip.z - 0.06), seg=16, rings=10,
                     mat="Kit_PaintYellow", smooth=True)

    for i in range(len(WALL_LINE) - 1):
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[i + 1]
        seg = math.hypot(x1 - x0, y1 - y0)
        yaw = math.atan2(y1 - y0, x1 - x0)
        n = max(1, int(round(seg / 3.2)))
        bay_len = seg / n
        for k in range(n):
            t0, t1 = k / n, (k + 1) / n
            ax, ay = x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0
            bx, by = x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1
            mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
            if near_gate(mx, my):
                continue
            bay(mx, my, yaw, bay_len, _ground_z(mx, my), i * 7 + k)
        # 跨端壁柱（含转角收头）
        for k in range(n + 1):
            t = k / n
            px, py = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            if near_gate(px, py):
                continue
            gz = _ground_z(px, py)
            m.box((0.52, 0.56, 2.12), at=(px, py, gz), rz=yaw, mat="Kit_ConcreteLight")
            m.box((0.64, 0.70, 0.085), at=(px, py, gz + 2.12), rz=yaw,
                  mat="Kit_ConcreteLight")
            m.box((0.56, 0.60, 0.05), at=(px, py, gz + 2.205), rz=yaw,
                  mat="Kit_ConcreteMid")

    # ---- 大门：方门柱 + 三层柱帽 + 金属门扇 + 门楣牌 + 门卫亭 ----
    gz = _ground_z(gx, gy)
    for s in (-1.0, 1.0):
        py = gy + s * 3.55
        m.box((1.10, 1.10, 0.40), at=(gx, py, gz), mat="Kit_ConcreteDark")
        m.box((0.86, 0.86, 2.75), at=(gx, py, gz + 0.40), mat="Kit_ConcreteLight")
        m.box((1.04, 1.04, 0.14), at=(gx, py, gz + 3.15), mat="Kit_ConcreteLight")
        m.box((0.90, 0.90, 0.10), at=(gx, py, gz + 3.29), mat="Kit_ConcreteMid")
        m.revolve([(0.34, 0.0), (0.34, 0.06), (0.22, 0.18), (0.0, 0.26)],
                  at=(gx, py, gz + 3.39), seg=20, mat="Kit_PaintYellow", smooth=True)
    # 门楣：三块浅色板 + 黄漆招牌，横跨两根门柱
    m.box((0.36, 7.4, 0.62), at=(gx, gy, gz + 3.05), mat="Kit_ConcreteLight")
    m.box((0.16, 3.2, 0.46), at=(gx + 0.22, gy, gz + 3.13), mat="Kit_PaintYellow")
    for k in range(3):
        m.box((0.06, 0.72, 0.24), at=(gx + 0.31, gy - 1.0 + k * 1.0, gz + 3.15),
              mat="Kit_GlassDark")
    # 两扇门叶：向院内开启 ~62°，框 + 竖栅 + 斜撑
    for s in (-1.0, 1.0):
        hinge_y = gy + s * 2.95
        open_yaw = math.radians(62.0) * (-s)
        # 门叶从铰点向院内侧展开：局部宽度方向 u = (cos, sin)
        ux, uy = math.cos(open_yaw), math.sin(open_yaw)
        for (lo, hi, zz, sz) in ((0.08, 2.62, 2.30, 0.10), (0.08, 2.62, 0.62, 0.10)):
            m.box((2.6, 0.09, sz), at=(gx + ux * 1.3, hinge_y + uy * 1.3, gz + zz),
                  rz=open_yaw, mat="Kit_Iron")
        for k in range(9):
            t = 0.14 + 2.34 * k / 8.0
            m.box((0.055, 0.055, 1.72), at=(gx + ux * t, hinge_y + uy * t, gz + 0.66),
                  rz=open_yaw, mat="Kit_Iron")
        # 斜撑（框内对角）
        m.box((2.5, 0.055, 0.055), at=(gx + ux * 1.3, hinge_y + uy * 1.3, gz + 1.55),
              rz=open_yaw, ry=math.radians(32.0), mat="Kit_Iron")
    # 门卫亭：2.4×2.2×2.8，窗带 + 门 + 出檐平顶
    bx, by = gx + 4.6, gy + 5.4
    bgz = _ground_z(bx, by)
    m.box((2.8, 2.6, 0.25), at=(bx, by, bgz), mat="Kit_ConcreteDark")
    m.box((2.4, 2.2, 2.55), at=(bx, by, bgz + 0.25), mat="Kit_ConcreteLight")
    m.box((2.6, 2.4, 0.14), at=(bx, by, bgz + 2.80), mat="Kit_ConcreteMid")
    m.box((2.9, 2.7, 0.10), at=(bx, by, bgz + 2.86), mat="Kit_SteelPale")
    for sx in (-0.75, 0.0, 0.75):
        m.box((0.62, 0.06, 0.95), at=(bx + sx, by - 1.12, bgz + 1.35),
              mat="Kit_GlassDark")
        m.box((0.72, 0.10, 0.10), at=(bx + sx, by - 1.14, bgz + 0.82),
              mat="Kit_ConcreteMid")
    m.box((0.72, 0.06, 1.85), at=(bx + 1.22, by + 0.2, bgz + 0.30),
          mat="Kit_GlassDark")
    m.box((0.10, 0.08, 0.18), at=(bx + 1.30, by + 0.55, bgz + 1.15),
          mat="Kit_PaintYellow")


def build_hall_clean(m):
    """主厂房的**干净重制版**：型钢门式刚架 + 压型墙板 + 真实窗带 + 双坡金属屋面。

    取代旧 `CK.build_hall`（那件是为"废弃/塌顶/破窗"写的，叠一个干净屋顶只会在黑窗洞
    与塌口之间打架）。这里从零建一栋完整的单层厂房，尺寸/位置/出墙管坐标保持原值，
    所以 `build_hall_bridge` 与场地关系不用动。
    """
    HX, HY = -12.0, -3.0
    L, W = 16.0, 10.0
    EAVE, RIDGE = 7.2, 9.0
    gz = _ground_z(HX, HY)
    hx, hy = L * 0.5, W * 0.5
    pitch = math.atan2(RIDGE - EAVE, hy)
    wall_mat = "Kit_SteelBlue"

    def B(size, at, **kw):
        m.box(size, at=(at[0] + HX, at[1] + HY, at[2] + gz), **kw)

    def C(r, h, at, **kw):
        m.cyl(r, h, at=(at[0] + HX, at[1] + HY, at[2] + gz), **kw)

    def R(profile, at, **kw):
        m.revolve(profile, at=(at[0] + HX, at[1] + HY, at[2] + gz), **kw)

    # ---- 基础：勒脚 + 地梁 + 室内地坪 ----
    B((L + 1.0, W + 1.0, 0.26), (0, 0, 0.02), mat="Kit_ConcreteDark")
    B((L + 0.5, W + 0.5, 0.22), (0, 0, 0.26), mat="Kit_ConcreteMid")
    B((L - 0.1, W - 0.1, 0.06), (0, 0, 0.42), mat="Kit_ConcreteLight")
    # 四角基础墩（出挑 8 cm）
    for sx in (-1, 1):
        for sy in (-1, 1):
            B((0.9, 0.9, 0.55), (sx * (hx - 0.15), sy * (hy - 0.15), 0.0),
              mat="Kit_ConcreteDark")

    # ---- 四面墙：压型钢板 + 底部 1.0 m 混凝土墙裙 ----
    # 墙板（实心，窗是外挂的框/玻璃，不挖洞——避免从窗洞看到室内黑箱）
    for sy in (-1.0, 1.0):
        B((L + 0.02, 0.22, EAVE - 0.46), (0, sy * hy, 0.44), mat=wall_mat)
    for sx in (-1.0, 1.0):
        B((0.22, W + 0.02, EAVE - 0.46), (sx * hx, 0, 0.44), mat=wall_mat)
    # 墙裙（外凸 6 cm，浅混凝土）
    for sy in (-1.0, 1.0):
        B((L + 0.20, 0.10, 0.98), (0, sy * (hy + 0.05), 0.44),
          mat="Kit_ConcreteMid")
    for sx in (-1.0, 1.0):
        B((0.10, W + 0.20, 0.98), (sx * (hx + 0.05), 0, 0.44),
          mat="Kit_ConcreteMid")
    # 竖向压型肋：0.7 m 一道，只落在窗带以外的墙板上
    for sy in (-1.0, 1.0):
        n = int(L / 0.70)
        for k in range(n):
            x = -hx + 0.35 + k * 0.70
            # 南墙门洞 + 窗带、北墙窗带都跳过（肋会插进玻璃）
            if sy < 0 and (abs(x) < 3.05 or (3.7 < abs(x) < 6.9)):
                continue
            if sy > 0 and abs(x) < 7.0:
                continue
            B((0.055, 0.06, EAVE - 1.45), (x, sy * (hy + 0.14), 1.42),
              mat="Kit_SteelPale")
    for sx in (-1.0, 1.0):
        n = int(W / 0.70)
        for k in range(n):
            y = -hy + 0.35 + k * 0.70
            if abs(y) < 3.6:
                continue
            B((0.06, 0.055, EAVE - 1.45), (sx * (hx + 0.14), y, 1.42),
              mat="Kit_SteelPale")
    # 横向腰线/檐口线：把整面大蓝墙切成"墙裙—窗带—檐下"三段
    for sy in (-1.0, 1.0):
        B((L + 0.26, 0.12, 0.16), (0, sy * (hy + 0.04), 2.62),
          mat="Kit_SteelPale")
        B((L + 0.20, 0.08, 0.07), (0, sy * (hy + 0.10), 6.62),
          mat="Kit_SteelPale")
    for sx in (-1.0, 1.0):
        B((0.12, W + 0.26, 0.16), (sx * (hx + 0.04), 0, 2.62),
          mat="Kit_SteelPale")
        B((0.08, W + 0.20, 0.07), (sx * (hx + 0.10), 0, 6.62),
          mat="Kit_SteelPale")
    # 南墙两件小设备：百叶风口 + 配电箱（填掉大面空白，也给"厂房在用"的叙事）
    B((1.00, 0.16, 0.80), (-7.15, -hy - 0.10, 1.72), mat="Kit_ConcreteDark")
    for k in range(5):
        B((0.90, 0.06, 0.075), (-7.15, -hy - 0.19, 1.82 + k * 0.14),
          mat="Kit_SteelPale")
    B((0.86, 0.18, 1.25), (7.05, -hy - 0.11, 1.45), mat="Kit_SteelPale")
    B((0.68, 0.05, 1.02), (7.05, -hy - 0.22, 1.56), mat="Kit_SteelBlue")
    B((0.08, 0.06, 0.16), (6.76, -hy - 0.26, 2.02), mat="Kit_PaintYellow")

    # ---- 窗带：框 + 玻璃 + 中挺 + 横档 + 窗台 + 过梁 ----
    def window_x(cx, y, z0, w=1.45, h=1.35):
        outward = -1.0 if y < 0 else 1.0
        yy = y + outward * 0.14
        # 深色洞口内衬（比玻璃大一圈）——窗才像"凹进去"，不是贴纸
        B((w + 0.18, 0.05, h + 0.18), (cx, y + outward * 0.09, z0 - 0.09),
          mat="Kit_ConcreteDark")
        B((w, 0.08, h), (cx, yy, z0), mat="Kit_GlassDark")
        B((w + 0.14, 0.12, 0.09), (cx, yy + outward * 0.02, z0 - 0.05),
          mat="Kit_ConcreteLight")
        B((w + 0.10, 0.10, 0.09), (cx, yy + outward * 0.02, z0 + h),
          mat="Kit_ConcreteLight")
        for sx in (-1.0, 1.0):
            B((0.075, 0.11, h + 0.04), (cx + sx * w * 0.5, yy, z0 - 0.02),
              mat="Kit_SteelPale")
        B((0.065, 0.10, h), (cx, yy, z0), mat="Kit_SteelPale")
        B((w, 0.10, 0.06), (cx, yy, z0 + h * 0.60), mat="Kit_SteelPale")
        # 窗台挑出
        B((w + 0.32, 0.26, 0.075), (cx, y + outward * 0.18, z0 - 0.12),
          mat="Kit_ConcreteLight")

    def window_y(cy, x, z0, w=1.45, h=1.35):
        outward = -1.0 if x < 0 else 1.0
        xx = x + outward * 0.14
        B((0.05, w + 0.18, h + 0.18), (x + outward * 0.09, cy, z0 - 0.09),
          mat="Kit_ConcreteDark")
        B((0.08, w, h), (xx, cy, z0), mat="Kit_GlassDark")
        B((0.12, w + 0.14, 0.09), (xx + outward * 0.02, cy, z0 - 0.05),
          mat="Kit_ConcreteLight")
        B((0.10, w + 0.10, 0.09), (xx + outward * 0.02, cy, z0 + h),
          mat="Kit_ConcreteLight")
        for sy in (-1.0, 1.0):
            B((0.11, 0.075, h + 0.04), (xx, cy + sy * w * 0.5, z0 - 0.02),
              mat="Kit_SteelPale")
        B((0.10, 0.065, h), (xx, cy, z0), mat="Kit_SteelPale")
        B((0.10, w, 0.06), (xx, cy, z0 + h * 0.60), mat="Kit_SteelPale")
        B((0.26, w + 0.32, 0.075), (x + outward * 0.18, cy, z0 - 0.12),
          mat="Kit_ConcreteLight")

    # 南立面：门两侧各 2 樘；北立面 5 樘；西山墙 4 樘；东山墙 2 樘（避开出墙管）
    for cx in (-5.55, -4.05, 4.05, 5.55):
        window_x(cx, -hy, 3.95, w=1.32)
    for cx in (-6.4, -3.2, 0.0, 3.2, 6.4):
        window_x(cx, hy, 3.95, w=1.45)
    for cy in (-3.4, -1.15, 1.15, 3.4):
        window_y(cy, -hx, 3.95, w=1.32)
    for cy in (-2.4, -0.4):
        window_y(cy, hx, 3.95, w=1.15)

    # ---- 南立面：大卷帘门 + 门框/导轨/门楣盒 + 底部警示带 + 人员门 ----
    B((5.9, 0.12, 4.75), (0, -hy - 0.12, 0.48), mat="Kit_SteelPale")
    for k in range(9):
        B((5.8, 0.05, 0.035), (0, -hy - 0.20, 0.72 + k * 0.52),
          mat="Kit_SteelBlue")
    for sx in (-1.0, 1.0):
        B((0.22, 0.26, 5.05), (sx * 3.02, -hy - 0.10, 0.44), mat="Kit_Iron")
    B((6.4, 0.34, 0.30), (0, -hy - 0.14, 5.28), mat="Kit_Iron")
    B((5.6, 0.06, 0.34), (0, -hy - 0.21, 0.52), mat="Kit_PaintYellow")
    for k in range(8):
        B((0.34, 0.05, 0.34), (-2.45 + k * 0.70, -hy - 0.245, 0.52),
          mat="Kit_GlassDark")
    # 门两侧的混凝土门垛 + 雨棚
    for sx in (-1.0, 1.0):
        B((0.55, 0.55, 5.35), (sx * 3.35, -hy - 0.12, 0.42), mat="Kit_ConcreteLight")
    B((7.4, 1.55, 0.20), (0, -hy - 0.85, 5.55), mat="Kit_ConcreteLight")
    B((7.4, 0.12, 0.34), (0, -hy - 1.58, 5.42), mat="Kit_ConcreteLight")
    B((7.0, 0.05, 0.10), (0, -hy - 1.65, 5.50), mat="Kit_PaintYellow")
    for sx in (-1.0, 1.0):
        B((0.12, 0.12, 5.05), (sx * 3.35, -hy - 1.40, 0.42), mat="Kit_Iron")
        # 雨棚斜拉杆：从墙面上方拉到棚口，把薄板变成结构
        _branch(m, Vector((HX + sx * 3.1, HY - hy - 0.03, gz + 6.35)),
                Vector((HX + sx * 3.1, HY - hy - 1.52, gz + 5.58)), 0.04,
                "Kit_Iron")
    # 人员门（门洞贴面 + 框 + 把手 + 台阶）
    B((1.15, 0.10, 2.35), (-5.9, -hy - 0.15, 0.46), mat="Kit_WoodDark")
    for sx in (-1.0, 1.0):
        B((0.10, 0.14, 2.45), (-5.9 + sx * 0.62, -hy - 0.17, 0.42),
          mat="Kit_ConcreteLight")
    B((1.34, 0.16, 0.10), (-5.9, -hy - 0.17, 2.80), mat="Kit_ConcreteLight")
    B((0.08, 0.10, 0.10), (-5.45, -hy - 0.24, 1.55), mat="Kit_PaintYellow")
    for k in range(3):
        B((1.9 - k * 0.10, 0.45, 0.16), (-5.9, -hy - 0.45 - k * 0.35, 0.10 + k * 0.16),
          mat="Kit_ConcreteMid")

    # ---- 东山墙三根出墙管（接场地管桥；坐标与原 kit 对齐） ----
    for z, r in ((4.9, 0.16), (5.6, 0.30), (6.3, 0.22)):
        m.extrude_along(_circle_profile(r, 16),
                        (HX + hx, HY + 2.4, gz + z),
                        (HX + hx + 3.4, HY + 2.4, gz + z),
                        mat="Kit_Rust", smooth=True)
        for xx in (hx + 0.18, hx + 3.22):
            m.extrude_along(_circle_profile(r * 1.35, 16),
                            (HX + xx - 0.055, HY + 2.4, gz + z),
                            (HX + xx + 0.055, HY + 2.4, gz + z),
                            mat="Kit_Iron", smooth=True)

    # ---- 双坡屋面：檐口封板 + 24 道细肋/坡 + 脊盖 + 天窗 + 通风器 ----
    slope_len = math.hypot(hy + 0.75, RIDGE - EAVE)
    for sign in (-1.0, 1.0):
        B((L + 1.5, slope_len, 0.14), (0, sign * (hy * 0.5 + 0.35),
                                       (EAVE + RIDGE) * 0.5 + 0.10),
          rx=-sign * pitch, mat="Kit_SteelPale")
        for k in range(24):
            x = -hx - 0.55 + (k + 0.5) / 24.0 * (L + 1.1)
            B((0.075, slope_len, 0.060), (x, sign * (hy * 0.5 + 0.35),
                                          (EAVE + RIDGE) * 0.5 + 0.19),
              rx=-sign * pitch, mat="Kit_SteelPale")
        # 檐口封板 + 檐沟
        B((L + 1.6, 0.20, 0.38), (0, sign * (hy + 0.68), EAVE - 0.16),
          mat="Kit_ConcreteLight")
        m.extrude_along(_circle_profile(0.105, 14),
                        (HX - hx - 0.72, HY + sign * (hy + 0.70), gz + EAVE - 0.30),
                        (HX + hx + 0.72, HY + sign * (hy + 0.70), gz + EAVE - 0.30),
                        mat="Kit_SteelPale", smooth=True)
    B((L + 1.6, 0.34, 0.14), (0, 0, RIDGE + 0.16), mat="Kit_SteelPale")
    B((L + 1.4, 0.20, 0.10), (0, 0, RIDGE + 0.29), mat="Kit_SteelPale")
    # 北坡两条天窗带（贴合坡面：z = 脊高 − |y|/半宽 × 坡高 + 面层厚度）
    for cx in (-4.6, 4.6):
        sky_z = RIDGE - (2.9 / hy) * (RIDGE - EAVE) + 0.12
        B((3.0, 1.55, 0.10), (cx, 2.9, sky_z), rx=-pitch,
          mat="Kit_GlassDark")
        for sy in (-1.0, 1.0):
            B((3.15, 0.10, 0.12), (cx, 2.9 + sy * 0.80, sky_z + 0.02),
              rx=-pitch, mat="Kit_SteelPale")
    # 屋脊通风器 ×3
    for cx in (-5.2, 0.0, 5.2):
        C(0.26, 0.55, (cx, 0.0, RIDGE + 0.05), seg=18, mat="Kit_Rust")
        R([(0.26, 0.0), (0.34, 0.06), (0.16, 0.28), (0.0, 0.40)],
          (cx, 0.0, RIDGE + 0.58), seg=18, mat="Kit_SteelPale", smooth=True)
    # 落水管：四角竖管 + 弯头
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            px, py = sx * (hx + 0.02), sy * (hy + 0.72)
            m.extrude_along(_circle_profile(0.085, 14),
                            (HX + px, HY + py, gz + 0.55),
                            (HX + px, HY + py, gz + EAVE - 0.35),
                            mat="Kit_SteelPale", smooth=True)
            C(0.095, 0.18, (hx + sx * 0.02, sy * (hy + 0.58), EAVE - 0.55),
              seg=16, mat="Kit_SteelPale")

    # ---- 山墙尖 + 百叶通风口 + 檐口收边 ----
    for sx in (-1.0, 1.0):
        x0 = sx * hx
        xin = sx * (hx - 0.22)
        pts = [(x0, -hy, EAVE + gz), (x0, hy, EAVE + gz), (x0, 0, RIDGE + gz),
               (xin, -hy, EAVE + gz), (xin, hy, EAVE + gz), (xin, 0, RIDGE + gz)]
        m.add(pts, [(0, 1, 2), (3, 5, 4),
                    (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)],
              at=(HX, HY, 0.0), mat=wall_mat)
        # 百叶：5 道水平叶片 + 边框
        for k in range(5):
            z = EAVE + 0.30 + k * 0.26
            B((0.10, 1.35 - abs(k - 2) * 0.16, 0.10),
              (sx * (hx + 0.06), 0, z - gz), mat="Kit_SteelPale")
        # 山墙檐口包边（斜板）
        for sign in (-1.0, 1.0):
            B((0.10, slope_len, 0.18), (sx * (hx + 0.10), sign * (hy * 0.5 + 0.35),
                                        (EAVE + RIDGE) * 0.5 - gz + 0.16),
              rx=-sign * pitch, mat="Kit_ConcreteLight")

    # ---- 招牌 + 壁灯 + 落水管卡箍 ----
    B((4.6, 0.12, 1.10), (0, -hy - 0.22, 5.95), mat="Kit_ConcreteLight")
    B((4.2, 0.06, 0.80), (0, -hy - 0.30, 6.10), mat="Kit_PaintYellow")
    for k in range(4):
        B((0.62, 0.05, 0.40), (-1.55 + k * 1.03, -hy - 0.34, 6.30),
          mat="Kit_GlassDark")
    for sx in (-1.0, 1.0):
        lx, ly = sx * 6.9, -hy - 0.20
        tip = Vector((HX + lx, HY + ly - 0.55, gz + 4.95))
        _branch(m, Vector((HX + lx, HY + ly, gz + 4.95)), tip, 0.045, "Kit_Iron")
        R([(0.28, 0.0), (0.28, 0.05), (0.18, 0.12), (0.07, 0.16)],
          (lx, ly - 0.55, 4.86), seg=16, mat="Kit_ConcreteLight", smooth=True)
        m.sphere(0.10, at=(tip.x, tip.y, tip.z - 0.05), seg=16, rings=10,
                 mat="Kit_PaintYellow", smooth=True)

    # ---- 东山墙检修爬梯（干净双轨 + 横档 + 顶部小平台；不做密笼，免得读成黑栅栏） ----
    lx = hx + 0.35
    for sy in (-1.0, 1.0):
        m.extrude_along(_circle_profile(0.045, 12),
                        (HX + lx, HY - 3.0 + sy * 0.32, gz + 0.55),
                        (HX + lx, HY - 3.0 + sy * 0.32, gz + EAVE + 1.15),
                        mat="Kit_SteelPale", smooth=True)
    for k in range(15):
        z = 0.85 + k * 0.52
        B((0.07, 0.70, 0.040), (lx, -3.0, z), mat="Kit_SteelPale")
    B((1.30, 0.90, 0.10), (lx + 0.35, -3.0, EAVE + 1.18), mat="Kit_Iron")
    for sy in (-1.0, 1.0):
        B((0.75, 0.05, 0.05), (lx + 0.35, -3.0 + sy * 0.42, EAVE + 1.60),
          mat="Kit_SteelPale")


def _field_h(x, y):
    """墙外起伏地形的高度（与 grass/roads/trees 同一真源，避免物件悬空/入土）。"""
    dx = max(0.0, abs(x) - 52.0)
    dy = max(0.0, abs(y) - 38.0)
    d = math.hypot(dx, dy)
    t = min(1.0, d / 55.0)
    t = t * t * (3.0 - 2.0 * t)
    h = OUTSKIRT_Z + t * (1.05 + 0.85 * math.sin(x * 0.031 + 1.3) *
                          math.cos(y * 0.027 - 0.7))
    h += 2.4 * t * t * max(0.0, math.sin(x * 0.017 + 0.6) *
                           math.cos(y * 0.014 - 0.4))
    h += 0.10 * math.sin(x * 0.23) * math.sin(y * 0.19) * t
    return h


def _far_hall(m, cx, cy, w, d, h, yaw, rng, haze=True):
    """远景厂房：浅灰绿体块 + 锯齿/双坡屋面 + 窗带 + 烟囱；用远景色，不抢近景。"""
    gz = _field_h(cx, cy)
    wall = "Kit_CpHaze" if haze else "Kit_ConcreteLight"
    roof = "Kit_CpGrassFar" if haze else "Kit_SteelPale"
    m.box((w, d, h), at=(cx, cy, gz), rz=yaw, mat=wall)
    # 锯齿屋脊：3~5 个小坡
    n = max(3, int(w / 6.0))
    for k in range(n):
        t = (k + 0.5) / n - 0.5
        m.box((w / n * 1.05, d + 0.5, 0.28),
              at=(cx + math.cos(yaw) * w * t, cy + math.sin(yaw) * w * t,
                  gz + h + 0.14), rz=yaw, mat=roof)
    # 窗带（两长边）
    for side in (-1.0, 1.0):
        ox = math.sin(yaw) * (d * 0.5 + 0.04) * side
        oy = math.cos(yaw) * (d * 0.5 + 0.04) * side
        nw = max(3, int(w / 3.2))
        for k in range(nw):
            t = (k + 0.5) / nw - 0.5
            m.box((1.35, 0.07, 0.95),
                  at=(cx + math.cos(yaw) * w * t + ox,
                      cy + math.sin(yaw) * w * t + oy, gz + h * 0.54),
                  rz=yaw, mat="Kit_GlassDark")
    # 烟囱/通风管
    for k in range(2):
        px = cx + rng.uniform(-0.32, 0.32) * w
        py = cy + rng.uniform(-0.25, 0.25) * d
        rr = rng.uniform(0.28, 0.55)
        ch = h * rng.uniform(1.25, 1.75)
        m.cyl(rr, ch, at=(px, py, gz + h), seg=18,
              mat="Kit_ConcreteMid" if not haze else "Kit_CpHaze")
        m.cyl(rr * 1.15, 0.18, at=(px, py, gz + h + ch), seg=18, mat="Kit_RustDark")


def _hedge_clump(m, x, y, r, rng, z=None):
    """田埂灌木：3~4 个大瓣，专供远景树篱（比庭院灌木便宜，轮廓仍圆）。"""
    z = _field_h(x, y) if z is None else z
    for k in range(rng.randint(3, 4)):
        a = rng.uniform(0, math.tau)
        dist = r * rng.uniform(0.0, 0.55)
        rr = r * rng.uniform(0.42, 0.68)
        _ellipsoid(m, rr, rr * rng.uniform(0.82, 1.12), rr * rng.uniform(0.66, 0.92),
                   (x + math.cos(a) * dist, y + math.sin(a) * dist,
                    z + rr * rng.uniform(0.48, 0.78)),
                   rng.choice(("Kit_CpFoliageMid", "Kit_CpFoliageDark",
                               "Kit_CpFoliageFar")),
                   seg=16, rings=10)


def _far_tree(m, x, y, h, rng):
    """远树：不建模枝叶，只用 4~6 个大椭球瓣堆出剪影（空气透视色）。"""
    z = _field_h(x, y)
    for k in range(rng.randint(4, 6)):
        a = rng.uniform(0, math.tau)
        rr = h * rng.uniform(0.22, 0.34)
        _ellipsoid(m, rr, rr * rng.uniform(0.8, 1.1), rr * rng.uniform(0.7, 0.95),
                   (x + math.cos(a) * h * rng.uniform(0.0, 0.22),
                    y + math.sin(a) * h * rng.uniform(0.0, 0.22),
                    z + h * rng.uniform(0.5, 0.85)),
                   rng.choice(("Kit_CpFoliageFar", "Kit_CpHaze", "Kit_CpFoliageFar")),
                   seg=18, rings=12)


def _hay_bale(m, x, y, rng):
    """田里的小圆草捆（横躺圆柱 + 两道捆带），给远景一点人味。"""
    z = _field_h(x, y)
    rr = rng.uniform(0.65, 0.95)
    L = rng.uniform(1.0, 1.4)
    m.cyl(rr, L, at=(x - L * 0.5, y, z + rr), seg=20,
          ry=math.pi * 0.5, mat="Kit_PaintYellow")
    for off in (-L * 0.28, L * 0.28):
        m.cyl(rr * 1.015, 0.10, at=(x + off - 0.05, y, z + rr), seg=20,
              ry=math.pi * 0.5, mat="Kit_ConcreteMid")


def build_outskirts_casual(m):
    """墙外：起伏田野（连续高度场）+ 田垄 + 树线/灌木篱 + 远景厂区 + 远山。

    上一版是"一整块平绿 extrude + 几个圆包 + 两排球树"，远看就是一张绿纸。
    这一版把墙外当天经地义的**乡野**来做：地形有缓坡（4 m 网格、平滑法线）、
    近草/中草/远雾三层色带；田垄、灌木篱、树线、土路、草捆、围栏把 232×186 m 填满；
    最外圈用霾灰绿的山丘与厂房剪影收边，和天空接上。
    """
    rng = random.Random(SEED + 104)

    # 墙外田块（多边形 + 色）：地形按"面心落在哪块田"换色，形成真实的田畴拼布。
    far_fields = [
        ([(-108, -70), (-58, -70), (-52, -40), (-92, -30), (-112, -45)],
         "Kit_CpGrassFar"),
        ([(-112, 10), (-62, 10), (-54, 42), (-96, 58), (-110, 38)],
         "Kit_CpGrassFar"),
        ([(54, -68), (106, -66), (112, -36), (58, -22)],
         "Kit_CpGrassFar"),
        ([(56, 14), (108, 14), (112, 44), (60, 56)],
         "Kit_CpGrass"),
        ([(-48, 64), (18, 62), (30, 100), (-54, 106)],
         "Kit_CpHaze"),
        ([(-52, -72), (18, -70), (26, -104), (-56, -106)],
         "Kit_CpGrassFar"),
    ]

    # ---- 1) 起伏地形：共享顶点的 4 m 网格（平滑法线），逐面按距离选色 ----
    step = 4.0
    x0, x1 = -170.0, 170.0
    y0, y1 = -150.0, 150.0
    nx = int((x1 - x0) / step)
    ny = int((y1 - y0) / step)
    verts = []
    for j in range(ny + 1):
        yy = y0 + j * step
        for i in range(nx + 1):
            xx = x0 + i * step
            verts.append((xx, yy, _field_h(xx, yy)))
    faces = []
    mats = []
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            b = a + 1
            c = a + nx + 2
            d = a + nx + 1
            faces.append((a, b, c, d))
            cx = x0 + (i + 0.5) * step
            cy = y0 + (j + 0.5) * step
            dx = max(0.0, abs(cx) - 52.0)
            dy = max(0.0, abs(cy) - 38.0)
            dist = math.hypot(dx, dy)
            band = dist + 11.0 * math.sin(cx * 0.053) * math.cos(cy * 0.041)
            field_mat = None
            for poly, mt in far_fields:
                if point_in_poly(cx, cy, poly):
                    field_mat = mt
                    break
            if dist < 9.0:
                mats.append("Kit_CpGrass")
            elif field_mat is not None:
                mats.append(field_mat)
            elif band < 40.0:
                mats.append("Kit_CpGrass")
            elif band < 82.0:
                mats.append("Kit_CpGrassFar")
            else:
                mats.append("Kit_CpHaze")
    base_f = len(m.faces)
    m.add(verts, faces, mat="Kit_CpGrass", smooth=True)
    for k, mt in enumerate(mats):
        m._slot(mt)                       # 逐面换色也要登记槽，否则 to_object 的 index 查不到
        m.face_mat[base_f + k] = mt

    # ---- 2) 田垄：5 片田，短段随地形高度铺，读作犁沟 ----
    fields = ((-92, -46, -56, -18), (54, 8, 94, 44),
              (-42, 56, 6, 92), (58, -64, 96, -34), (-112, 24, -78, 58))
    for (ax, ay, bx, by) in fields:
        rows = int(abs(by - ay) / 2.4)
        cols = int(abs(bx - ax) / 2.4)
        for r in range(rows):
            yy = ay + (r + 0.5) * (by - ay) / rows
            for c in range(cols):
                xx = ax + (c + 0.5) * (bx - ax) / cols
                zz = _field_h(xx, yy)
                m.box((2.0, 0.20, 0.035), at=(xx, yy, zz + 0.015),
                      rz=math.radians(4.0), mat="Kit_DecSoil")

    # ---- 3) 草色斑块：只做近带（10~45 m），大块、无描边 ----
    for k in range(26):
        a = rng.uniform(0, math.tau)
        dist = rng.uniform(14.0, 46.0)
        cx = math.cos(a) * (52.0 + dist)
        cy = math.sin(a) * (38.0 + dist)
        rx = rng.uniform(3.0, 7.0)
        ry = rx * rng.uniform(0.55, 0.90)
        m.poly_extrude(_blob(rng, rx, ry, 12),
                       _field_h(cx, cy) + 0.005, _field_h(cx, cy) + 0.020,
                       at=(cx, cy, 0),
                       mat=rng.choice(("Kit_DecMoss", "Kit_DecGrass", "Kit_DecSoil")))

    # ---- 4) 灌木篱：三横两纵，断断续续；是墙外"不可达"的软边界 ----
    def hedge(ax, ay, bx, by, spacing=2.3):
        L = math.hypot(bx - ax, by - ay)
        n = max(2, int(L / spacing))
        for k in range(n):
            t = (k + 0.5) / n
            x = ax + (bx - ax) * t + rng.uniform(-0.7, 0.7)
            y = ay + (by - ay) * t + rng.uniform(-0.7, 0.7)
            if abs(x) < 54 and abs(y) < 40:
                continue
            if rng.random() < 0.12:
                continue
            _hedge_clump(m, x, y, rng.uniform(0.9, 1.4), rng, _field_h(x, y))

    # 树篱贴着田块边界走——田埂在画面里是可读的线，不再是随机散点
    for poly, _mt in far_fields[:4]:
        for k in range(len(poly)):
            ax, ay = poly[k]
            bx, by = poly[(k + 1) % len(poly)]
            hedge(ax, ay, bx, by, spacing=3.4)

    # ---- 5) 树线：四边两层，阔叶/针叶混种；第二层退后 12 m ----
    def row(ax, ay, bx, by, spacing, far=False):
        L = math.hypot(bx - ax, by - ay)
        n = max(2, int(L / spacing))
        for k in range(n):
            t = (k + 0.5) / n
            x = ax + (bx - ax) * t + rng.uniform(-4.0, 4.0)
            y = ay + (by - ay) * t + rng.uniform(-4.0, 4.0)
            if abs(x) < 55 and abs(y) < 41:
                continue
            if rng.random() < 0.12:
                continue
            if far:
                _far_tree(m, x, y, rng.uniform(6.0, 9.0), rng)
            elif rng.random() < 0.22:
                _conifer(m, x, y, rng.uniform(6.0, 9.5), rng, z=_field_h(x, y))
            else:
                _broadleaf(m, x, y, rng.uniform(5.0, 8.5), rng, z=_field_h(x, y))

    row(-108, -56, 108, -56, 8.5)
    row(-108, 54, 108, 54, 8.5)
    row(-72, -46, -72, 44, 8.0)
    row(72, -46, 72, 44, 8.0)
    row(-128, -70, 128, -70, 12.0, far=True)
    row(-128, 68, 128, 68, 12.0, far=True)
    row(-96, -80, -96, 58, 12.0, far=True)
    row(96, -80, 96, 58, 12.0, far=True)

    # ---- 6) 近墙外侧的灌木/草簇（把墙基"种"进地里） ----
    for i in range(len(WALL_LINE)):
        x0w, y0w = WALL_LINE[i]
        x1w, y1w = WALL_LINE[(i + 1) % len(WALL_LINE)]
        for t in (0.08, 0.22, 0.38, 0.55, 0.72, 0.88):
            wx = x0w + (x1w - x0w) * t
            wy = y0w + (y1w - y0w) * t
            nl = math.hypot(wx, wy) or 1.0
            out = rng.uniform(1.1, 3.4)
            cx = wx + wx / nl * out
            cy = wy + wy / nl * out
            if rng.random() < 0.45:
                _shrub(m, cx, cy, rng.uniform(0.7, 1.2), rng, _field_h(cx, cy))
            else:
                _grass_clump(m, cx, cy, rng.uniform(0.7, 1.25), rng,
                             _field_h(cx, cy), flowers=rng.random() < 0.10)

    # ---- 6b) 近带散树/灌木：墙外 6~18 m 的空地也要有东西，不能只有墙根一排草 ----
    for (x, y, h) in ((-30, -47, 6.2), (-9, -45, 5.4), (12, -48, 6.8),
                      (34, -45, 5.8), (49, -37, 5.2), (51, -8, 5.6),
                      (48, 12, 5.0), (-52, 24, 5.4), (-50, -33, 5.0)):
        _broadleaf(m, x, y, h, rng, z=_field_h(x, y))
    for k in range(26):
        a = rng.uniform(0, math.tau)
        dist = rng.uniform(5.0, 19.0)
        cx = math.cos(a) * (52.0 + dist)
        cy = math.sin(a) * (38.0 + dist)
        if rng.random() < 0.55:
            _shrub(m, cx, cy, rng.uniform(0.6, 1.1), rng, _field_h(cx, cy))
        else:
            _grass_clump(m, cx, cy, rng.uniform(0.7, 1.3), rng,
                         _field_h(cx, cy), flowers=rng.random() < 0.18)

    # ---- 7) 远景厂区：霾灰绿的厂房 / 冷却塔 / 料仓 / 烟囱 ----
    for (cx, cy, w, d, h, yaw) in ((-104, 36, 26, 12, 9.0, 8), (104, 34, 24, 11, 8.5, -6),
                                   (-100, -58, 22, 11, 8.0, 5), (98, -60, 20, 10, 8.0, 4),
                                   (-24, 116, 30, 13, 9.5, 2), (56, 112, 24, 11, 8.5, -3)):
        _far_hall(m, cx, cy, w, d, h, math.radians(yaw), rng, haze=True)
    # 冷却塔（双曲面，24 段）
    for (cx, cy, r, h) in ((122, 82, 8.0, 24.0), (-126, 76, 7.0, 21.0)):
        gz = _field_h(cx, cy)
        m.revolve([(r, 0.0), (r * 0.78, h * 0.28), (r * 0.48, h * 0.58),
                   (r * 0.50, h * 0.82), (r * 0.58, h)],
                  at=(cx, cy, gz), seg=24, mat="Kit_CpHaze", smooth=True)
    # 料仓
    for (cx, cy, r, h) in ((116, -14, 3.6, 13.0), (-118, -8, 3.2, 12.0),
                           (82, 96, 3.0, 11.0)):
        gz = _field_h(cx, cy)
        m.cyl(r, h, at=(cx, cy, gz), seg=24, mat="Kit_CpGrassFar")
        m.revolve([(r * 1.04, 0.0), (r * 1.04, 0.6), (r * 0.55, h * 0.22),
                   (0.0, h * 0.32)], at=(cx, cy, gz + h), seg=24,
                  mat="Kit_CpHaze", smooth=True)
    # 烟囱（细高 + 两道环带）
    for (cx, cy, r, h) in ((-112, 92, 2.0, 28.0), (108, 100, 1.8, 25.0)):
        gz = _field_h(cx, cy)
        m.cyl(r, h, at=(cx, cy, gz), seg=20, mat="Kit_CpHaze", r_top=r * 0.72)
        for zz in (h * 0.45, h * 0.75):
            m.cyl(r * (1.02 - 0.2 * zz / h), 0.9, at=(cx, cy, gz + zz), seg=20,
                  mat="Kit_RustDark")

    # ---- 8) 远山（低缓，灰绿；只压天际线，不做"灰色大饼"） ----
    for (cx, cy, rx, ry, h) in ((-162, 20, 46, 28, 4.2), (162, -10, 42, 26, 3.8),
                                (-130, -132, 40, 24, 4.0), (130, -122, 38, 24, 3.6),
                                (0, 162, 50, 28, 4.5)):
        _ellipsoid(m, rx, ry, h, (cx, cy, _field_h(cx, cy) - h * 0.55),
                   "Kit_CpGrassFar", seg=24, rings=14)

    # ---- 9) 田里草捆 + 木围栏 ----
    for (cx, cy) in ((-78, -30), (-76, -33), (70, 22), (74, 26),
                     (-30, 70), (62, -48)):
        _hay_bale(m, cx, cy, rng)
    for (ax, ay, bx, by) in ((-100, -62, -54, -62), (54, 58, 102, 58)):
        L = math.hypot(bx - ax, by - ay)
        n = max(2, int(L / 3.0))
        for k in range(n):
            t = (k + 0.5) / n
            x = ax + (bx - ax) * t
            y = ay + (by - ay) * t
            gz = _field_h(x, y)
            m.box((0.14, 0.14, 1.35), at=(x, y, gz), mat="Kit_WoodDark")
            if k < n - 1:
                t2 = (k + 1.5) / n
                x2 = ax + (bx - ax) * t2
                y2 = ay + (by - ay) * t2
                for zz in (0.45, 0.95):
                    _branch(m, Vector((x, y, gz + zz)), Vector((x2, y2, gz + zz)),
                            0.035, "Kit_WoodDark")

    # ---- 10) 场外土路：大门 → 西侧，跟着地形分段 + 两道车辙 ----
    road = [(-44.0, -17.0), (-58.0, -19.0), (-76.0, -16.0), (-96.0, -20.0),
            (-118.0, -15.0), (-140.0, -18.0)]
    for i in range(len(road) - 1):
        ax, ay = road[i]
        bx, by = road[i + 1]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        mx, my = (ax + bx) * 0.5, (ay + by) * 0.5
        gz = _field_h(mx, my)
        m.box((L + 0.5, 4.8, 0.06), at=(mx, my, gz - 0.015), rz=yaw,
              mat="Kit_DecSoil")
        for side in (-1.0, 1.0):
            m.box((L + 0.5, 0.40, 0.022),
                  at=(mx - math.sin(yaw) * 0.95 * side,
                      my + math.cos(yaw) * 0.95 * side, gz + 0.030),
                  rz=yaw, mat="Kit_DecWet")


def build_vegetation_casual(m):
    """场内植被：花园草簇/花、花池树、基础灌木、藤蔓、花境与少量道具。"""
    rng = random.Random(SEED + 105)

    # ---- 1) 三处花园：草簇（带花）+ 园景石 + 水边芦苇 ----
    for (zx, zy, zr) in MOSS_ZONES:
        for k in range(18):
            a = rng.uniform(0, math.tau)
            r = math.sqrt(rng.random()) * zr * 0.80
            _grass_clump(m, zx + math.cos(a) * r, zy + math.sin(a) * r,
                         rng.uniform(0.65, 1.10), rng, GROUND_Z - 0.005,
                         flowers=rng.random() < 0.35)
        # 水边一圈更高的芦苇/草
        px, py, prx, pry = ((10.0, -24.0, 3.4, 2.5) if (zx, zy) == (10.0, -24.0)
                            else ((32.0, -24.0, 2.4, 1.8) if zx > 20
                                  else (-38.0, 24.0, 2.2, 1.6)))
        for k in range(8):
            a = math.tau * k / 8.0 + rng.uniform(-0.15, 0.15)
            _grass_clump(m, px + math.cos(a) * prx * rng.uniform(1.02, 1.16),
                         py + math.sin(a) * pry * rng.uniform(1.02, 1.16),
                         rng.uniform(0.9, 1.35), rng, GROUND_Z - 0.004,
                         flowers=False)
        for k in range(4):
            a = rng.uniform(0, math.tau)
            rr = zr * rng.uniform(0.45, 0.80)
            _boulder(m, zx + math.cos(a) * rr, zy + math.sin(a) * rr,
                     rng.uniform(0.28, 0.48), rng, GROUND_Z - 0.02)

    # ---- 2) 花池树（3 个）+ 花园大树 ----
    for (x, y, h) in ((-24.0, -7.5, 5.4), (6.0, -8.0, 5.2), (22.0, -8.5, 5.0)):
        _planter_tree(m, x, y, h, rng)
    _broadleaf(m, 32.0, -24.0, 5.6, rng, z=GROUND_Z - 0.005)
    _broadleaf(m, -38.0, 22.0, 5.0, rng, z=GROUND_Z - 0.005)

    # ---- 3) 基础灌木：只贴墙根/花池边，不落在广场中间 ----
    for (x, y, r) in ((-22, -9.2, 1.25), (8, -9.4, 1.15), (30, -12.2, 1.2),
                      (-44, 10, 1.15), (44, -22, 1.2), (-14, 20, 1.1),
                      (-26.5, 6.8, 1.0), (-38.0, -1.8, 1.0),
                      (-19.0, -8.9, 0.95), (-4.5, -8.9, 0.95)):
        _shrub(m, x, y, r, rng, z=_grass_z(x, y))

    # 花境：三处抬高石槽（石边 + 土面 + 花 + 一丛灌木/草）
    for (bx, by, w, d) in ((-24.0, -10.6, 3.2, 1.4), (0.0, -10.6, 3.2, 1.4),
                           (-40.0, -6.5, 2.8, 1.3)):
        gz = _ground_z(bx, by)
        m.box((w + 0.30, d + 0.30, 0.30), at=(bx, by, gz), mat="Kit_ConcreteMid")
        m.box((w, d, 0.08), at=(bx, by, gz + 0.30), mat="Kit_DecSoil")
        for k in range(4):
            m.box((w * 0.20, 0.06, 0.10),
                  at=(bx - w * 0.35 + k * w * 0.23, by - d * 0.5 - 0.15,
                      gz + 0.32), mat="Kit_ConcreteLight")
        for k in range(7):
            a = rng.uniform(0, math.tau)
            rr = rng.uniform(0.2, 0.45)
            m.sphere(rng.uniform(0.055, 0.085),
                     at=(bx + math.cos(a) * w * rr, by + math.sin(a) * d * rr,
                         gz + 0.44), seg=16, rings=10, smooth=True,
                     mat=rng.choice(("Kit_PaintYellow", "Kit_ConcreteLight",
                                     "Kit_CpFoliageLight")))
        _grass_clump(m, bx, by, 1.0, rng, gz + 0.36, flowers=True)

    # 藤蔓暂不出：爬藤在像素描边里仍容易读成"串珠/梯子"，先用墙根草簇与苔痕代替。
    # for (x, y, yaw, h) in ((-16.0, -8.10, 0, 4.4), (-36.0, -2.56, 0, 5.8)):
    #     _vine(m, x, y, math.radians(yaw), h, rng)

    # ---- 5) 花园花簇（圆粒，不是薄片） ----
    for (cx, cy) in ((10.0, -24.0), (32.0, -24.0), (-38.0, 24.0)):
        for k in range(7):
            a = rng.uniform(0, math.tau)
            r = math.sqrt(rng.random()) * 2.2
            m.sphere(rng.uniform(0.05, 0.09),
                     at=(cx + math.cos(a) * r, cy + math.sin(a) * r,
                         GROUND_Z + 0.05), seg=16, rings=10, smooth=True,
                     mat=rng.choice(("Kit_PaintYellow", "Kit_ConcreteLight",
                                     "Kit_CpFoliageLight")))

    # ---- 6) 道具：圆桶 + 木箱（比原来多一圈桶箍/箱沿，读得出是工业桶） ----
    for k in range(3):
        x = 3.5 + rng.uniform(-0.55, 0.55)
        y = -5.0 + rng.uniform(-0.55, 0.55)
        gz = _ground_z(x, y)
        m.cyl(0.34, 0.88, at=(x, y, gz), seg=18, mat="Kit_Rust")
        for zz in (0.16, 0.46, 0.76):
            m.cyl(0.355, 0.045, at=(x, y, gz + zz), seg=18, mat="Kit_RustDark")
        m.cyl(0.33, 0.04, at=(x, y, gz + 0.88), seg=18, mat="Kit_SteelPale")
    for k in range(2):
        x = 5.0 + k * 0.95
        y = -5.5
        gz = _ground_z(x, y)
        m.box((0.84, 0.74, 0.62), at=(x, y, gz), rz=0.3, mat="Kit_WoodMid")
        m.box((0.90, 0.80, 0.06), at=(x, y, gz + 0.62), rz=0.3, mat="Kit_WoodDark")
        m.box((0.06, 0.76, 0.06), at=(x, y, gz + 0.30), rz=0.3, mat="Kit_WoodDark")


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
        # 【休闲化口径】六件版旁楼本体照用（窗框/发水/入口细节很足），但它是为"废弃关"写的：
        # 破窗/封板/掉角在亮色场景里会读成一排黑洞。合并版清空这三张损毁登记表，
        # 只保留干净的窗、门、雨棚、屋顶设备与管线——零件不变，损毁变体关掉。
        if part == "building":
            mod.BROKEN.clear()
            mod.BOARDED.clear()
            mod.CHIP.clear()
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
                     ("Walls", build_walls_casual), ("Hall", build_hall_clean),
                     ("HallBridge", build_hall_bridge),
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


def _cloud_material():
    """出图专用云材质：白底 + 自发光，逆光时不会变成灰石头。"""
    mat = bpy.data.materials.get("_chem_cloud_mat")
    if mat is not None:
        return mat
    mat = bpy.data.materials.new("_chem_cloud_mat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.92, 0.95, 0.98, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    if "Emission Color" in bsdf.inputs:
        bsdf.inputs["Emission Color"].default_value = (0.62, 0.72, 0.82, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 0.55
    return mat


def _render_clouds():
    """只进出图的云带：6 团多瓣白云，放在远景天幕上补足天空。

    **不进 FBX**（在 render() 里临时建）——像素路径俯视 30°，看不到天空；它的作用只有
    Blender 成品图的氛围与纵深。瓣数/重叠方式照云场（多球叠团），不是单个球；
    材质带自发光，避免逆光时读成灰色石头。
    """
    rng = random.Random(SEED + 301)
    m = K.Mesher()
    spots = ((-120, 150, 82, 16), (-45, 170, 92, 13), (40, 165, 86, 17),
             (125, 130, 80, 14), (160, 30, 88, 12), (-165, 20, 84, 13))
    for (cx, cy, cz, r) in spots:
        for k in range(7):
            ox = rng.uniform(-1.05, 1.05) * r
            oy = rng.uniform(-0.55, 0.55) * r
            oz = rng.uniform(-0.15, 0.22) * r
            rr = r * rng.uniform(0.34, 0.60)
            m.sphere(rr, at=(cx + ox, cy + oy, cz + oz), seg=20, rings=12,
                     mat="Kit_Cloud", smooth=True)
    obj = m.to_object("_chem_clouds")
    obj.data.materials.clear()
    obj.data.materials.append(_cloud_material())
    for poly in obj.data.polygons:
        poly.material_index = 0
    return obj


def _add_rim(scene, energy=16000.0):
    """冷白轮廓光：从镜头右后方压一道边，把树冠/塔顶从天幕里剥出来。"""
    d = bpy.data.lights.new("chem_rim", "AREA")
    d.energy = energy
    d.size = 120.0
    d.color = (0.86, 0.93, 1.0)
    obj = bpy.data.objects.new("chem_rim", d)
    obj.location = (90.0, 95.0, 75.0)
    obj.rotation_euler = (Vector((0.0, 0.0, 8.0)) - Vector(obj.location)).to_track_quat(
        "-Z", "Y").to_euler()
    scene.collection.objects.link(obj)
    return obj


def render(views, res, samples):
    scene = bpy.context.scene
    # 【重定标】上一版 -0.10 曝光 + 0.55 近白天穹 + 60000 补光把整场冲成粉彩平片。
    # 现在：天空压深、太阳压低到 42° 拉长影子、补光砍到 1/7、曝光 -0.55，
    # 让受光面保住奶油色、暗面落到暖灰蓝，投影成为画面的结构。
    A.EXPOSURE = -0.38
    A.setup_render(scene, res, samples)
    world = A.setup_sky(scene, strength=0.44)
    ramp = next(n for n in world.node_tree.nodes if n.type == "VALTORGB")
    cols = ("E7F0F2", "B7D8E9", "7FB2D3")          # 地平线雾白 → 中天浅蓝 → 天顶蓝
    for e, hx in zip(sorted(ramp.color_ramp.elements, key=lambda e: e.position), cols):
        e.color = (*[K.srgb_to_linear(c) for c in A.ST_hex(hx)], 1.0)
    A.add_sun(scene, energy=4.5, elev_deg=42.0, rot_deg=145.0, color=(1.0, 0.94, 0.84))
    A.add_fill(scene, energy=11000)
    _add_rim(scene)
    bm = K.Mesher()
    bm.box((700.0, 700.0, 0.12), at=(-350.0, -350.0, -0.5), mat="Kit_CpGrassFar")
    bm.to_object("_chem_backdrop")
    # 云只作为可选氛围；当前版本的球形云在 35 mm 机位下读成白气球，先不出。
    # _render_clouds()
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
