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
OUTSKIRT_Z = -0.28                    # 墙外地面顶面（略低，藏住水面）

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
    ("FenceA",        lambda a: CK.build_fence(a),                 (  2.0,  -9.0),   0.0),
    ("FenceB",        lambda a: CK.build_fence(a),                 ( 24.0, -10.0),  90.0),
    ("FenceToppled",  lambda a: CK.build_fence(a, toppled=True),   ( -6.0,   0.5),  12.0),
    ("DebrisA",       lambda a: CK.build_debris(a),                (  6.0,  -8.0),  37.0),
    ("DebrisB",       lambda a: CK.build_debris(a),                ( 30.0,   0.0), 133.0),
    ("DebrisC",       lambda a: CK.build_debris(a),                (-26.0,   8.0), 251.0),
    ("DebrisD",       lambda a: CK.build_debris(a),                (  0.0,  24.0),  74.0),
    ("DebrisE",       lambda a: CK.build_debris(a),                ( 30.0,  -8.0), 318.0),
    ("DebrisF",       lambda a: CK.build_debris(a),                ( 28.0,   8.0), 198.0),
]

#: 六件版分区件：名 → 摆位 (x, y), yaw°（site 不用：新场地地坪自建）
SIX = [
    ("towers",   (-20.0,  20.0), 0.0),
    ("tanks",    (  6.0,  20.0), 0.0),
    ("building", ( 34.0,  18.0), 0.0),
    ("pipes",    (  6.0,   6.5), 0.0),
    ("props",    ( 17.0, -21.0), 0.0),
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
WALL_GAPS = {(2, 3), (7, 8), (12, 13)}


def build_ground(m):
    """可玩区地坪：不规则轮廓 + 分块板缝 + 破损补丁 + 裂缝 + 积水。"""
    m.poly_extrude(YARD_OUTLINE, GROUND_Z - 0.30, GROUND_Z, mat="Kit_ConcreteMid")
    rng = random.Random(SEED + 1)

    # 大块铺装补丁（深浅交替，读成水泥分块）——碎块化，避免整片灰地坪。
    for cx, cy, sx, sy, rz, mat in (
        (-30, 12, 11, 8, 4, "Kit_ConcreteLight"),
        (  2, 16, 10, 7, -3, "Kit_ConcreteDark"),
        ( 28, 10, 9, 8, 7, "Kit_ConcreteLight"),
        ( -8, -2, 10, 6, -6, "Kit_ConcreteDark"),
        ( 24, -8, 10, 6, 3, "Kit_ConcreteLight"),
        (-32,-14, 9, 5, 2, "Kit_ConcreteDark"),
        ( 12, -20, 12, 5, 0, "Kit_ConcreteLight"),
        (-20,  2, 8, 5, -4, "Kit_ConcreteLight"),
        ( 18,  2, 8, 5, 5, "Kit_ConcreteDark"),
        (  6, -8, 8, 5, -2, "Kit_ConcreteLight"),
        (-40, -8, 7, 6, 3, "Kit_ConcreteDark"),
        ( 38, -18, 7, 6, -5, "Kit_ConcreteLight"),
        (-12, 24, 8, 5, 6, "Kit_ConcreteDark"),
        ( 30, 24, 7, 5, -3, "Kit_ConcreteLight"),
    ):
        m.box((sx, sy, 0.04), at=(cx, cy, GROUND_Z - 0.02), rz=math.radians(rz), mat=mat)
    # 小碎补丁（泥/草/锈斑）打散水泥面
    for k in range(60):
        x = rng.uniform(-44, 44)
        y = rng.uniform(-31, 31)
        rx = rng.uniform(0.8, 3.2)
        ry = rx * rng.uniform(0.5, 1.0)
        mat = rng.choice(("Kit_ConcreteDark", "Kit_SandDark", "Kit_RustDark",
                          "Kit_GrassDark", "Kit_ConcreteLight"))
        m.poly_extrude(_blob(rng, rx, ry, 7), GROUND_Z + 0.005, GROUND_Z + 0.035,
                       at=(x, y, 0), mat=mat)

    # 草缝 / 泥地 / 积水
    for cx, cy, rx, ry, mat, seed in (
        (-40,  2, 4.0, 5.0, "Kit_GrassDark", 11),
        (-18, 22, 5.0, 3.0, "Kit_GrassDark", 12),
        ( 14, 24, 4.0, 3.0, "Kit_GrassDark", 13),
        ( 30, -26, 5.0, 3.5, "Kit_GrassDark", 14),
        (  0, -28, 4.5, 3.0, "Kit_SandDark", 15),
        (-30, -20, 4.0, 3.0, "Kit_SandDark", 16),
        ( 20,  1, 3.5, 2.5, "Kit_WetSand", 17),
        ( -4, 10, 3.0, 2.0, "Kit_WetSand", 18),
        ( 34,  -6, 3.0, 2.0, "Kit_RustDark", 19),
        (-24, -30, 4.0, 2.2, "Kit_WetSand", 20),
    ):
        m.poly_extrude(_blob(rng, rx, ry, 9), GROUND_Z + 0.005, GROUND_Z + 0.05,
                       at=(cx, cy, 0), mat=mat)

    # 裂缝（细长折条）
    for k, (x0, y0, x1, y1) in enumerate((
        (-40, -6, -20, -12), (2, 20, 22, 16), (-14, 8, 8, 4),
        (10, -14, 30, -12), (-36, 18, -18, 22), (28, -24, 40, -18),
    )):
        _crack(m, x0, y0, x1, y1, SEED + 30 + k)

    # 翘起/碎裂水泥板
    for k in range(10):
        x = rng.uniform(-44, 44)
        y = rng.uniform(-30, 30)
        m.box((rng.uniform(1.2, 2.6), rng.uniform(0.8, 1.8), rng.uniform(0.06, 0.16)),
              at=(x, y, GROUND_Z), rz=rng.uniform(0, math.pi),
              mat=rng.choice(("Kit_ConcreteDark", "Kit_ConcreteLight")))

    # 地坪边缘坍塌：几块斜插的碎板
    for k, (x, y, yaw) in enumerate(((-46, -12, 8), (45, -14, -12), (20, 32.5, 5),
                                    (-36, 32, -8), (8, -32, 4))):
        m.box((3.2, 1.6, 0.22), at=(x, y, GROUND_Z - 0.06),
              rz=math.radians(yaw), rx=math.radians(6), mat="Kit_ConcreteDark")


def build_roads(m):
    """场地道路：一条东西主路 + 一条斜插支路 + 墙外延伸（视觉引导）。"""
    # 场内东西主路（略斜，避开中央构件）
    _road(m, [(-45, -16), (-20, -15), (4, -14), (26, -15), (44, -14)], width=6.5, z=GROUND_Z + 0.02)
    # 斜插支路：从西南主路拐向东北罐区
    _road(m, [(-20, -15), (-10, -6), (-2, 2), (6, 9), (14, 14)], width=5.0, z=GROUND_Z + 0.02)
    # 东侧纵向支路
    _road(m, [(30, -30), (30, -16), (29, -2), (28, 8)], width=4.5, z=GROUND_Z + 0.02)
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
            h = rng.uniform(2.3, 3.6)
            mat = rng.choice(("Kit_ConcreteMid", "Kit_ConcreteMid", "Kit_ConcreteDark"))
            # 墙体（带压顶）
            m.box((L, 0.34, h), at=(mx, my, GROUND_Z), rz=yaw, mat=mat)
            m.box((L + 0.1, 0.46, 0.12), at=(mx, my, GROUND_Z + h), rz=yaw,
                  mat="Kit_ConcreteDark")
            # 锈迹流痕（竖向薄板）
            if rng.random() < 0.45:
                ox, oy = -math.sin(yaw) * 0.18, math.cos(yaw) * 0.18
                m.box((rng.uniform(0.25, 0.6), 0.02, rng.uniform(0.5, min(1.6, h - 0.3))),
                      at=(mx + ox, my + oy, GROUND_Z + 0.2), rz=yaw, mat="Kit_RustDark")
        # 段末立柱
        for (px, py) in ((x0, y0),):
            m.box((0.55, 0.55, 3.7), at=(px, py, GROUND_Z), rz=0, mat="Kit_ConcreteDark")
            m.box((0.7, 0.7, 0.14), at=(px, py, GROUND_Z + 3.7), rz=0, mat="Kit_ConcreteLight")

    # 大门柱（西南主路穿墙处）+ 倒伏门扇
    _gate(m, (-45.5, -16.0), math.radians(90))
    # 墙根荒草/藤蔓
    for i in range(len(WALL_LINE) - 1):
        if (i, i + 1) in WALL_GAPS:
            continue
        x0, y0 = WALL_LINE[i]
        x1, y1 = WALL_LINE[i + 1]
        for t in (0.15, 0.5, 0.85):
            x = x0 + (x1 - x0) * t
            y = y0 + (y1 - y0) * t
            _grass_tuft(m, x + rng.uniform(-0.6, 0.6), y + rng.uniform(-0.6, 0.6),
                        rng.uniform(0.6, 1.2), rng, n=5)


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
    m.poly_extrude(outer, OUTSKIRT_Z - 0.4, OUTSKIRT_Z, mat="Kit_GrassDark")

    # 泥地/碎石斑（打散整片草绿；靠墙处多、远处少）
    for k in range(34):
        x = rng.uniform(-76, 76)
        y = rng.uniform(-56, 56)
        if abs(x) < YARD_X / 2 - 2 and abs(y) < YARD_Y / 2 - 2:
            continue
        rx = rng.uniform(2.5, 8.0) * (1.0 if abs(x) > 50 or abs(y) > 40 else 0.7)
        ry = rx * rng.uniform(0.55, 0.9)
        mat = rng.choice(("Kit_SandDark", "Kit_GrassMid", "Kit_SandDark", "Kit_WetSand"))
        m.poly_extrude(_blob(rng, rx, ry, 9), OUTSKIRT_Z + 0.005, OUTSKIRT_Z + 0.04,
                       at=(x, y, 0), mat=mat)

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
                    _dead_tree(m, x, y, rng.uniform(3.5, 7.5), rng)
                else:
                    _broadleaf(m, x, y, rng.uniform(4.0, 7.5), rng)

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
            _conifer(m, x, y, rng.uniform(5.0, 9.0), rng)
        else:
            _dead_tree(m, x, y, rng.uniform(4.0, 7.0), rng)

    # 灌木带（贴墙外 2~5 m，密）
    for i in range(len(WALL_LINE)):
        x0, y0 = WALL_LINE[i]
        for t in range(4):
            tt = (t + 0.5) / 4
            if i + 1 < len(WALL_LINE):
                x1, y1 = WALL_LINE[i + 1]
                x = x0 + (x1 - x0) * tt
                y = y0 + (y1 - y0) * tt
            else:
                x, y = x0, y0
            _shrub(m, x + rng.uniform(-2.2, 2.2), y + rng.uniform(-2.2, 2.2),
                   rng.uniform(1.0, 2.0), rng)

    # 电线杆 + 电缆（墙外道路旁）
    _pole_run(m, [(-2, 34), (-4, 46), (-6, 58)], rng)
    _pole_run(m, [(46, 5), (58, 4), (70, 2)], rng)
    _pole_run(m, [(-48, -9), (-60, -10), (-72, -12)], rng)

    # 墙外堆场：集装箱/废料/托盘/油桶群
    for cx, cy, yaw in ((-54, -26, 12), (52, -30, -8), (48, 30, 20), (-52, 33, -15),
                        (58, -8, 4), (-60, 14, -9)):
        _scrap_yard(m, cx, cy, math.radians(yaw), rng)

    # 外围散草/灌木
    for k in range(260):
        x = rng.uniform(-78, 78)
        y = rng.uniform(-60, 60)
        if abs(x) < YARD_X / 2 + 1 and abs(y) < YARD_Y / 2 + 1:
            continue
        _grass_tuft(m, x, y, rng.uniform(0.5, 1.5), rng, n=4)


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
            _grass_tuft(m, x + rng.uniform(-1.6, 1.6), y + rng.uniform(-1.6, 1.6),
                        rng.uniform(0.6, 1.3), rng, n=rng.randint(4, 7))
    # 整场随机散草（把空地打碎）
    for k in range(150):
        x = rng.uniform(-45, 45)
        y = rng.uniform(-31, 31)
        _grass_tuft(m, x, y, rng.uniform(0.4, 1.0), rng, n=rng.randint(2, 4))

    # 部分构件脚下的灌木/藤蔓
    for (x, y, r) in ((-33, -13, 2.2), (-12, -13, 2.0), (34, 12, 2.4),
                      (6, 27, 2.2), (-26, 25, 2.0), (17, -15, 2.6), (-11, -18, 2.0),
                      (-23, 11, 1.6), (32, 2, 1.8), (0, -6, 1.5), (20, 12, 1.4),
                      (-32, -6, 1.6), (42, -24, 1.5)):
        _shrub(m, x, y, r, rng)

    # 场内几棵枯树/阔叶树（避开构件与主路）
    for (x, y, h) in ((-44, 14, 5.5), (22, 24, 4.8), (44, -20, 5.2), (-6, -12, 4.2),
                      (10, 26, 4.6), (26, 14, 4.0), (-31, -12, 4.4), (36, -28, 4.6),
                      (-4, 11, 3.8), (-30, 9, 3.6)):
        _dead_tree(m, x, y, h, rng)
    for (x, y, h) in ((-33, 24, 5.0), (28, 28, 4.4), (44, 28, 4.8), (-9, 24, 4.0),
                      (14, -8, 4.2), (-30, -12, 3.8)):
        _broadleaf(m, x, y, h, rng)

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

    # 藤蔓：贴办公楼/旁楼墙根
    for (x, y, yaw, length) in ((-23, -6, 90, 7), (21, 14, 90, 6), (-2, -12, 0, 5),
                                (-30, 12, 90, 6), (30, 7, 90, 5)):
        _vine(m, x, y, math.radians(yaw), length, rng)


# ---------------------------------------------------------------------------
# 图元 helper
# ---------------------------------------------------------------------------

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
        m.box((L + 0.08, width, 0.02), at=((ax + bx) / 2, (ay + by) / 2, GROUND_Z + 0.045),
              rz=yaw, mat="Kit_ConcreteDark")


def _road(m, pts, width, z):
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        m.box((L + 0.2, width, 0.08), at=((ax + bx) / 2, (ay + by) / 2, z - 0.04),
              rz=yaw, mat="Kit_ConcreteDark")
        # 路中虚线/裂缝带
        m.box((L * 0.9, 0.12, 0.01), at=((ax + bx) / 2, (ay + by) / 2, z + 0.005),
              rz=yaw, mat="Kit_ConcreteLight")


def _gate(m, at, yaw):
    x, y = at
    cx, cy = math.cos(yaw), math.sin(yaw)
    # 两根门柱
    for s in (-1, 1):
        px = x - cy * 3.6 * s
        py = y + cx * 3.6 * s
        m.box((0.8, 0.8, 3.6), at=(px, py, GROUND_Z), rz=yaw, mat="Kit_ConcreteDark")
        m.box((1.0, 1.0, 0.16), at=(px, py, GROUND_Z + 3.6), rz=yaw, mat="Kit_ConcreteLight")
    # 半塌的推拉门扇
    m.box((3.4, 0.12, 2.2), at=(x - cy * 1.2, y + cx * 1.2, GROUND_Z + 0.15),
          rz=yaw + math.radians(18), rx=math.radians(14), mat="Kit_Rust")
    # 门牌
    m.box((1.6, 0.08, 0.5), at=(x, y, GROUND_Z + 2.9), rz=yaw, mat="Kit_PaintYellow")


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
            _dead_tree(m, x + rng.uniform(-1.2, 1.2), y + rng.uniform(-1.2, 1.2),
                       rng.uniform(3.5, 6.0), rng)
        if rng.random() < 0.6:
            _shrub(m, x + rng.uniform(-1.5, 1.5), y + rng.uniform(-1.5, 1.5),
                   rng.uniform(1.0, 1.8), rng)


def _mound(m, cx, cy, rad, h, mat, rng):
    sides = 10
    pts = _blob(rng, rad, rad * rng.uniform(0.7, 0.95), sides)
    m.revolve([(0.0, 0.0), (rad * 0.7, h * 0.45), (rad * 0.38, h * 0.82), (0.0, h)],
              at=(cx, cy, OUTSKIRT_Z if abs(cx) > YARD_X / 2 or abs(cy) > YARD_Y / 2 else GROUND_Z),
              seg=sides, mat=mat)


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
    m.cyl(h * 0.045, trunk_h, at=(x, y, z), seg=6, r_top=h * 0.035,
          mat="Kit_WoodDark", smooth=False)
    crown_r = h * rng.uniform(0.28, 0.38)
    for k in range(rng.randint(2, 3)):
        ox = rng.uniform(-0.35, 0.35) * crown_r
        oy = rng.uniform(-0.35, 0.35) * crown_r
        rr = crown_r * rng.uniform(0.7, 1.05)
        m.sphere(rr, at=(x + ox, y + oy, z + trunk_h + crown_r * 0.55 + k * crown_r * 0.35),
                 seg=8, rings=5,
                 mat=rng.choice(("Kit_GrassDark", "Kit_GrassMid", "Kit_GrassDark")),
                 smooth=False)


def _conifer(m, x, y, h, rng, z=None):
    """针叶树：短干 + 三层锥冠。"""
    z = GROUND_Z if z is None else z
    m.cyl(h * 0.04, h * 0.35, at=(x, y, z), seg=5, r_top=h * 0.03,
          mat="Kit_WoodDark", smooth=False)
    for k, (z0, r, hh) in enumerate(((h * 0.18, h * 0.20, h * 0.42),
                                     (h * 0.38, h * 0.16, h * 0.38),
                                     (h * 0.56, h * 0.11, h * 0.34))):
        m.revolve([(r, 0.0), (r * 0.55, hh * 0.55), (0.0, hh)],
                  at=(x, y, z + z0), seg=7, mat="Kit_GrassDark")


def _pallets_crates(m, x, y, yaw, rng):
    """托盘木箱堆（废料/待运货物）。"""
    # 托盘底
    for k in range(rng.randint(1, 3)):
        ox = rng.uniform(-1.2, 1.2)
        oy = rng.uniform(-1.2, 1.2)
        m.box((1.2, 0.8, 0.14), at=(x + ox, y + oy, GROUND_Z), rz=yaw + rng.uniform(-0.3, 0.3),
              mat="Kit_WoodDark")
        if rng.random() < 0.65:
            w = rng.uniform(0.6, 1.0)
            m.box((w, w * 0.8, w * 0.7), at=(x + ox, y + oy, GROUND_Z + 0.14),
                  rz=yaw + rng.uniform(-0.4, 0.4), mat="Kit_WoodDark")
    # 散落木条
    for k in range(rng.randint(2, 5)):
        m.box((rng.uniform(1.0, 2.2), 0.12, 0.08),
              at=(x + rng.uniform(-1.8, 1.8), y + rng.uniform(-1.8, 1.8), GROUND_Z + 0.02),
              rz=rng.uniform(0, 3.14), mat="Kit_WoodDark")


def _rail_spur(m, x0, y0, x1, y1, rng):
    """废弃铁路支线：碎石道床 + 枕木 + 两条钢轨（局部翘起/断头）。"""
    L = math.hypot(x1 - x0, y1 - y0)
    yaw = math.atan2(y1 - y0, x1 - x0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    # 道床
    m.box((L, 2.6, 0.10), at=(cx, cy, GROUND_Z), rz=yaw, mat="Kit_SandDark")
    # 枕木
    n = int(L / 0.9)
    for i in range(n):
        t = (i + 0.5) / n
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        m.box((0.32, 2.0, 0.10), at=(x, y, GROUND_Z + 0.10), rz=yaw,
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
                  at=((ax + bx) / 2, (ay + by) / 2, GROUND_Z + 0.20), rz=yaw,
                  mat="Kit_Iron")
    # 断头翘起的钢轨
    m.box((3.0, 0.14, 0.16), at=(x1 - 1.2, y1 + 0.6, GROUND_Z + 0.24), rz=yaw + 0.3,
          rx=math.radians(10), mat="Kit_Rust")


def _puddle(m, x, y, rx, ry, rng):
    """积水洼：深色湿边 + 浅水薄片（荒废场地低点）。"""
    m.poly_extrude(_blob(rng, rx, ry, 9), GROUND_Z + 0.02, GROUND_Z + 0.035,
                   at=(x, y, 0), mat="Kit_WetSand")
    m.poly_extrude(_blob(rng, rx * 0.72, ry * 0.72, 8), GROUND_Z + 0.035, GROUND_Z + 0.045,
                   at=(x + 0.2, y - 0.1, 0), mat="Kit_SteelBlue")


def _circle_profile(r, sides):
    return [(math.cos(2 * math.pi * i / sides) * r,
             math.sin(2 * math.pi * i / sides) * r) for i in range(sides)]


def _grass_tuft(m, x, y, size, rng, n=5, z=None):
    z = GROUND_Z if z is None else z
    mat_a, mat_b = "Kit_GrassDark", "Kit_GrassMid"
    for k in range(n):
        a = rng.uniform(0, math.pi * 2)
        lean = rng.uniform(0.15, 0.45) * size
        h = rng.uniform(0.35, 0.75) * size
        bx = x + rng.uniform(-0.22, 0.22) * size
        by = y + rng.uniform(-0.22, 0.22) * size
        tipx = bx + math.cos(a) * lean
        tipy = by + math.sin(a) * lean
        # 三片窄叶
        for t in (-0.6, 0.0, 0.6):
            w = 0.045 * size
            px, py = -math.sin(a + t) * w, math.cos(a + t) * w
            m.add(
                [(bx + px, by + py, z), (bx - px, by - py, z),
                 (tipx + px * 0.3, tipy + py * 0.3, z + h)],
                [(0, 1, 2)], at=(0, 0, 0), mat=mat_a if (k + t) % 2 else mat_b)


def _shrub(m, x, y, r, rng):
    for k in range(rng.randint(4, 7)):
        ox = rng.uniform(-r, r) * 0.7
        oy = rng.uniform(-r, r) * 0.7
        rr = rng.uniform(0.35, 0.8) * r * 0.6
        m.sphere(rr, at=(x + ox, y + oy, GROUND_Z + rr * 0.65), seg=7, rings=4,
                 mat=rng.choice(("Kit_GrassDark", "Kit_GrassMid")), smooth=False)


def _rubble(m, x, y, spread, rng, n=10):
    for k in range(n):
        px = x + rng.uniform(-spread, spread)
        py = y + rng.uniform(-spread, spread)
        s = rng.uniform(0.18, 0.55)
        m.box((s, s * rng.uniform(0.6, 1.4), s * rng.uniform(0.3, 0.8)),
              at=(px, py, GROUND_Z), rz=rng.uniform(0, 3.14),
              mat=rng.choice(("Kit_ConcreteDark", "Kit_ConcreteMid", "Kit_RustDark")))


def _drums(m, x, y, rng, n=5):
    for k in range(n):
        px = x + rng.uniform(-1.3, 1.3)
        py = y + rng.uniform(-1.3, 1.3)
        standing = rng.random() < 0.6
        r = 0.3
        h = 0.92
        if standing:
            m.cyl(r, h, at=(px, py, GROUND_Z), seg=8, mat="Kit_Rust")
            m.cyl(r * 1.03, 0.05, at=(px, py, GROUND_Z + 0.28), seg=8, mat="Kit_RustDark")
            m.cyl(r * 1.03, 0.05, at=(px, py, GROUND_Z + 0.62), seg=8, mat="Kit_RustDark")
        else:
            _branch(m, Vector((px - 0.5, py, GROUND_Z + r)), Vector((px + 0.5, py, GROUND_Z + r)),
                    r, "Kit_RustDark")


def _fallen_pole(m, x, y, yaw, rng):
    L = 7.5
    p0 = Vector((x, y, GROUND_Z + 0.18))
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


def _vine(m, x, y, yaw, length, rng):
    """墙根藤蔓：细条 + 叶簇。"""
    for k in range(6):
        ox = math.cos(yaw) * rng.uniform(-0.4, 0.4)
        oy = math.sin(yaw) * rng.uniform(-0.4, 0.4)
        h = rng.uniform(0.6, 1.8)
        p0 = Vector((x + ox, y + oy, GROUND_Z + 0.05))
        p1 = p0 + Vector((math.cos(yaw) * rng.uniform(0.3, length), math.sin(yaw) * rng.uniform(0.3, length),
                          h))
        _branch(m, p0, p1, 0.03, "Kit_GrassDark")
        m.sphere(0.22, at=(p1.x, p1.y, p1.z), seg=6, rings=3, mat="Kit_GrassDark", smooth=False)


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
    for name, fn in (("Ground", build_ground), ("Roads", build_roads),
                     ("Walls", build_walls), ("Outskirts", build_outskirts),
                     ("Vegetation", build_vegetation)):
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
    A.setup_render(scene, res, samples)
    A.setup_sky(scene)
    A.add_sun(scene)
    A.add_fill(scene)
    A.add_backdrop(scene, size=700.0, z=-0.6)
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
