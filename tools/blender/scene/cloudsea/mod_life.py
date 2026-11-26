# -*- coding: utf-8 -*-
"""cloudsea/mod_life.py —— 自然生命分件：停栖海鸥 / 鸟巢 / 羽毛 / 贝壳 / 跃水鱼。

【内容（对象清单）】
    · Cloud_GullPerch_0..7   停栖海鸥 ×8：BIRD_PERCHES 5 个停栖点扩成 8 只
                          （Hero 东缘 / East 北缘 / South 北缘各 2 只，同缘错开
                          0.95-1.12 m）；收翅站立低模鸥（双锥蛋身 + 两片收拢翼 +
                          球头 + 深色眼点 + 黄喙 + 橙色细腿 + 短尾），每只
                          80-140 tris；**原点在脚底**，脚底 z 精确落在云台顶面；
                          朝向朝云台外侧/朝水，姿态有差异（stand/look/tilt/
                          preen/alert，含理羽与歪头）。
    · Cloud_Nest_0..1        鸟巢 ×2：云台边缘小树枝巢（外圈粗枝 + 内圈细枝 +
                          Cloud_Rope 衬里）+ 2-3 枚淡色蛋（Cloud_Bone/Paper），
                          不挡站面中心。
    · Cloud_Feather_0..11    小羽毛 ×12：薄片五边叶形，散在云台顶（避开出生点
                          正中心与停栖点），随机小角度。
    · Cloud_Shell_0..6       贝壳 ×7：小扇片（顶扇面 + 裙边 + 底，闭实体），
                          Cloud_Bone/Coral 交替，散在云台顶。
    · Cloud_FishJump_0       跃水鱼 ×1：一条银鱼（mod_creatures.bait_fish，放大到 ≈1 单位）
                          仰头跃出水面的瞬间；Cloud_FishSplash_0 = 3 颗水花小球。
                          原点 = 入水点（水面 -0.4）。

【纪律】锚点/水位/停栖点单源取 cloudsea_common；材质只用 Cloud_* 自然物槽
（零贴图零碰撞）；总三角面 ≤ 8k（以 preview_module 的 STAT 实测为准）；
固定种子，重跑同图。
主题红线：只有海鸥/羽毛/鸟巢/蛋/贝壳/鱼等自然物，**无人造物**。
"""

import math
import random

import cloudsea_common as H
import kit_common as K
import mod_creatures as CR           # 跃水鱼用生物库的银鱼模型（总装里 life 不依赖 sea 的加载顺序）

TAU = math.tau
PI = math.pi
SEED = 20261111

# ---- 材质槽（全部 Cloud_* 自然物）----
WHITE = "Cloud_White"
SHADE = "Cloud_Shade"
YELLOW = "Cloud_Yellow"
CORAL = "Cloud_Coral"
BLACK = "Cloud_Black"
BONE = "Cloud_Bone"
PAPER = "Cloud_Paper"
WOOD = "Cloud_WoodDark"
ROPE = "Cloud_Rope"
BUBBLE = "Cloud_Bubble"

#: 停栖鸥 ×8：(x, y, 云台顶 z, 基础朝向(弧度, 0=+X), 姿态, 翼色)。
#: 朝向 = 从所属云台中心指向停栖点的外向方位加手调；同一边缘第 2 只沿缘错开。
GULLS = [
    (6.20, 0.00, 4.5, 0.00, "look", SHADE),          # Hero 东缘，朝东（朝水）
    (6.15, 0.95, 4.5, -0.55, "stand", SHADE),        # 同缘第 2 只（错开 0.95 m）
    (-6.00, 2.50, 4.5, PI, "preen", SHADE),          # Hero 西缘，朝西
    (-3.00, 7.20, 6.5, -PI / 2, "alert", SHADE),     # North 南缘，朝南（朝水）
    (12.00, 4.00, 7.5, 1.35, "stand", WHITE),        # East 北缘，朝东北
    (10.90, 4.30, 7.5, 1.95, "look", SHADE),         # 同缘第 2 只（错开 1.12 m）
    (4.00, -8.00, 5.5, 1.50, "preen", SHADE),        # South 北缘，朝北
    (4.95, -7.55, 5.5, 0.60, "tilt", SHADE),         # 同缘第 2 只（错开 0.99 m）
]

#: 姿态表：头偏航 / 头滚转(歪头) / 头位置(本地系, z=脚底起) / 喙下压(弧度)。
POSES = {
    "stand": (0.18, 0.00, (0.150, 0.000, 0.318), 0.10),
    "look":  (0.95, 0.15, (0.148, 0.015, 0.316), 0.16),
    "tilt":  (0.42, 0.55, (0.146, 0.020, 0.318), 0.12),
    "preen": (2.30, -0.30, (0.060, 0.088, 0.300), 0.62),
    "alert": (-0.15, 0.00, (0.152, 0.000, 0.348), 0.04),
}

#: 鸟巢 ×2：(x, y, 云台顶 z, 朝向, 蛋色组合) —— 都贴台缘，不挡站面中心。
NESTS = [
    (-1.70, -6.90, 6.5, -0.5, (BONE, PAPER, BONE)),  # North 台北缘（东距停栖鸥 1.3 m）
    (14.30, 11.30, 6.0, 0.8, (PAPER, BONE)),         # SE_Islet 东南缘
]

#: 羽毛 ×12：(x, y, 云台顶 z, 材质) —— 避开出生点中心 (0,0) 半径 2 m 与停栖点。
FEATHERS = [
    (3.40, -1.90, 4.5, WHITE), (-2.80, -3.60, 4.5, SHADE), (1.80, 3.40, 4.5, WHITE),
    (-4.60, 1.20, 4.5, WHITE), (4.90, 2.30, 4.5, SHADE), (-1.90, 4.10, 4.5, WHITE),
    (2.70, -4.10, 4.5, WHITE), (-5.20, -8.10, 6.5, SHADE), (-0.90, -8.60, 6.5, WHITE),
    (-4.30, -11.60, 6.5, WHITE), (2.60, 8.90, 5.5, WHITE), (5.90, 11.30, 5.5, SHADE),
]

#: 贝壳 ×7：(x, y, 云台顶 z, 材质)。
SHELLS = [
    (5.10, -3.20, 4.5, BONE), (-3.70, 3.90, 4.5, CORAL), (0.90, -4.60, 4.5, BONE),
    (-5.40, -0.70, 4.5, CORAL), (-14.60, -1.80, 3.0, BONE), (-11.40, 3.60, 3.0, CORAL),
    (-12.60, -8.70, 2.5, BONE),
]

#: 跃水鱼：(x, y, 入水点 z, 朝向) —— Hero 与 East 之间的开阔水面。
FISH_JUMP = (9.0, 3.5, H.SEA_TOP_Z, -0.9)


# ---------------------------------------------------------------------------
# 停栖海鸥（每只独立对象，脚底 = 本地原点，收翅站立，面数 80-140）
# ---------------------------------------------------------------------------

#: 收拢翼楔形（左翼 5 站点：肩内/肩外/中内/中外/尖；右翼 y 取镜像，闭实体 16 tris）。
#: 顶面随背脊下倾，外缘（前缘）挂出体侧轮廓，内缘没入背脊，翼尖收过尾根。
_WING_V = [
    (0.070, 0.030, 0.278),    # 0 肩内（贴背脊，让开体后隆背）
    (0.070, 0.125, 0.252),    # 1 肩外（微出侧影）
    (-0.100, 0.035, 0.262),   # 2 中内
    (-0.100, 0.125, 0.228),   # 3 中外
    (-0.245, 0.080, 0.215),   # 4 翼尖（收过尾根）
]
_WING_F = [
    (0, 2, 3), (0, 3, 1), (2, 4, 3),           # 顶面
    (5, 8, 7), (5, 6, 8), (7, 8, 9),           # 底面
    (0, 5, 7, 2), (2, 7, 9, 4),                # 内缘（埋入背脊）
    (1, 3, 8, 6), (3, 4, 9, 8),                # 外缘（折叠前缘）
    (0, 1, 6, 5),                              # 根帽
]
#: 短尾三角轮廓。
_TAIL_PTS = [(0.0, 0.036), (-0.115, 0.006), (-0.018, -0.040)]
#: 喙圆锥（局部 +Z = 喙尖方向，收工时 ry 转到水平前向）。
_BEAK_V = [(0.020, 0.0, 0.0), (0.0, 0.020, 0.0), (-0.020, 0.0, 0.0),
           (0.0, -0.020, 0.0), (0.0, 0.0, 0.095)]
_BEAK_F = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (3, 2, 1, 0)]


def _egg_ring(z, rf, dx=0.0):
    """鸥体横截面环：8 边椭圆（偏瘦，好让收拢翼探出体侧），后半圈拉长成尾。"""
    pts = []
    for i in range(8):
        a = TAU * i / 8.0
        f = rf * (1.0 + 0.30 * max(0.0, -math.cos(a)))
        pts.append((0.155 * f * math.cos(a) + dx, 0.102 * f * math.sin(a), z))
    return pts


def _rot_head(dx, dy, dz, yaw, roll):
    """头部局部偏移：先绕 Z 偏航（转头），再绕前进轴 X 滚转（歪头）。"""
    cy, sy = math.cos(yaw), math.sin(yaw)
    x = dx * cy - dy * sy
    y = dx * sy + dy * cy
    cr, sr = math.cos(roll), math.sin(roll)
    return (x, y * cr - dz * sr, y * sr + dz * cr)


def _build_gull(m, rng, pose_name, wing_mat):
    """一只收翅站立的低模鸥：脚底 = 原点，面朝 +X。32+12+12+8+32+12+32 ≈ 138 tris。"""
    s = rng.uniform(0.95, 1.05)                       # 个体大小微差
    yaw, roll, head_pos, beak_pitch = POSES[pose_name]

    # 身体：底极 + 两道环 + 顶极（顶极后移上翘 = 收翅隆背），32 tris
    verts = [(0.0, 0.0, 0.105)] + _egg_ring(0.135, 0.90) \
        + _egg_ring(0.298, 0.80, 0.012) + [(-0.030, 0.0, 0.348)]
    faces = [(0, 1 + (i + 1) % 8, 1 + i) for i in range(8)]
    faces += [(1 + i, 1 + (i + 1) % 8, 9 + (i + 1) % 8, 9 + i) for i in range(8)]
    faces += [(17, 9 + i, 9 + (i + 1) % 8) for i in range(8)]
    m.add(verts, faces, scale=(s, s, s), mat=WHITE, smooth=True)

    # 收拢翼 ×2（楔形实体：顶面 5 站点 + 底面下垂 0.048，外缘挂出体侧）
    for side in (1, -1):
        v = [(p[0] * s, side * p[1] * s, p[2] * s) for p in _WING_V]
        v += [(p[0] * s, side * p[1] * s, (p[2] - 0.048) * s) for p in _WING_V]
        m.add(v, _WING_F, mat=wing_mat, smooth=False)

    # 短尾（三角薄板，微翘）
    m.poly_extrude([(p[0] * s, p[1] * s) for p in _TAIL_PTS],
                   0.212 * s, 0.228 * s, at=(0.0, 0.0, 0.0), mat=WHITE, smooth=False)

    # 头 + 眼点 + 喙（随姿态偏航/滚转）
    hx, hy, hz = (c * s for c in head_pos)
    m.sphere(0.070 * s, at=(hx, hy, hz), seg=6, rings=2, mat=WHITE, smooth=True)
    for side in (1, -1):
        ex, ey, ez = _rot_head(0.044, 0.050 * side, 0.008, yaw, roll)
        m.sphere(0.0115 * s, at=(hx + ex * s, hy + ey * s, hz + ez * s),
                 seg=3, rings=2, mat=BLACK, smooth=False)
    bx, by, bz = _rot_head(0.058, 0.0, -0.006, yaw, roll)
    m.add(_BEAK_V, _BEAK_F, at=(hx + bx * s, hy + by * s, hz + bz * s),
          rz=yaw, ry=PI / 2 + beak_pitch, scale=(s, s, s), mat=YELLOW, smooth=False)

    # 橙色细腿 ×2（底端 z=0 = 脚底，顶端没入身体）
    for side in (1, -1):
        m.cyl(0.011 * s, 0.135 * s, at=(0.030 * s, side * 0.032 * s, 0.0),
              seg=4, mat=CORAL, smooth=True)


# ---------------------------------------------------------------------------
# 鸟巢（树枝环 + 衬里 + 淡色蛋）
# ---------------------------------------------------------------------------

def _egg(m, at, rz, rx, mat, s=1.0):
    """一枚低模蛋：底极 + 两道环 + 顶极，闭实体，24 tris。"""
    r1, r2 = 0.026 * s, 0.033 * s
    v = [(0.0, 0.0, 0.0)]
    for i in range(6):
        a = TAU * i / 6.0
        v.append((r1 * math.cos(a), r1 * math.sin(a), 0.020 * s))
    for i in range(6):
        a = TAU * i / 6.0
        v.append((r2 * math.cos(a), r2 * math.sin(a), 0.052 * s))
    v.append((0.0, 0.0, 0.082 * s))
    f = [(0, 1 + (i + 1) % 6, 1 + i) for i in range(6)]
    f += [(1 + i, 1 + (i + 1) % 6, 7 + (i + 1) % 6, 7 + i) for i in range(6)]
    f += [(13, 7 + i, 7 + (i + 1) % 6) for i in range(6)]
    m.add(v, f, at=at, rz=rz, rx=rx, mat=mat, smooth=True)


def _build_nest(m, rng, eggs):
    """边缘树枝巢：外圈粗枝 6 + 内圈细枝 4 + Rope 衬里 + 蛋。本地原点 = 巢底中心。"""
    for i in range(6):
        a = TAU * i / 6.0 + rng.uniform(-0.2, 0.2)
        r = 0.105 + rng.uniform(-0.008, 0.008)
        m.box((rng.uniform(0.16, 0.22), 0.013, 0.012),
              at=(r * math.cos(a), r * math.sin(a), 0.008),
              rz=a + PI / 2 + rng.uniform(-0.45, 0.45),
              rx=rng.uniform(-0.18, 0.18), mat=WOOD)
    for i in range(4):
        a = TAU * i / 4.0 + 0.5 + rng.uniform(-0.3, 0.3)
        r = 0.072 + rng.uniform(-0.010, 0.010)
        m.box((rng.uniform(0.12, 0.16), 0.011, 0.010),
              at=(r * math.cos(a), r * math.sin(a), 0.032),
              rz=a + PI / 2 + rng.uniform(-0.5, 0.5),
              rx=rng.uniform(-0.25, 0.25), mat=WOOD)
    m.revolve([(0.106, 0.0), (0.086, 0.046)], seg=8, mat=ROPE)
    for i, mat in enumerate(eggs):
        a = TAU * i / len(eggs) + 0.7
        _egg(m, (0.024 * math.cos(a), 0.024 * math.sin(a), 0.020),
             rz=a + PI, rx=0.55, mat=mat)


# ---------------------------------------------------------------------------
# 羽毛 / 贝壳 / 跃水鱼
# ---------------------------------------------------------------------------

#: 羽毛平面轮廓（五边叶形，+X = 羽尖）。
_FEATHER_PTS = [(0.0, 0.016), (0.055, 0.014), (0.105, 0.0),
                (0.050, -0.017), (0.006, -0.015)]


def _shell(m, rng, mat):
    """小扇片贝壳（闭实体）：顶扇面 + 裙边 + 底扇面，20 tris。本地原点 = 扇心底。"""
    r = rng.uniform(0.026, 0.040)
    a0 = rng.uniform(0.0, TAU)
    span = 2.35
    top, bot, f = [], [], []
    for i in range(6):
        a = a0 + span * i / 5.0
        top.append((r * math.cos(a), r * math.sin(a), 0.0045))
        bot.append((r * 0.96 * math.cos(a), r * 0.96 * math.sin(a), 0.0))
    v = [(0.0, 0.0, 0.013)] + top + bot
    for i in range(5):
        f.append((0, 1 + i, 2 + i))                  # 顶扇面
        f.append((1 + i, 2 + i, 8 + i, 7 + i))       # 裙边
        f.append((0, 8 + i, 7 + i))                  # 底扇面（反向朝下）
    m.add(v, f, mat=mat, smooth=False)


#: 跃水鱼的体型：银鱼建模体长 0.62，放大到 ≈1 单位——和水下鱼群同一档卡通夸张。
FISH_JUMP_SCALE = 1.6


def _build_fish_jump():
    """跃水鱼：一条银鱼仰头 43° 跃出水面 + 3 颗水花小球（各一个对象）。本地原点 = 入水点。"""
    x, y, z, heading = FISH_JUMP
    fish = CR.bait_fish().to_object("Cloud_FishJump_0")
    fish.location = (x + 0.16, y + 0.05, z + 0.45)
    fish.rotation_euler = (0.0, -0.75, heading)      # 先绕 Y 仰头，再绕 Z 转到游向
    fish.scale = (FISH_JUMP_SCALE,) * 3
    splash = K.Mesher()
    for dx, dy, dz, r in ((0.05, 0.16, 0.10, 0.030), (-0.08, 0.02, 0.16, 0.022),
                          (0.16, -0.04, 0.05, 0.026)):
        splash.sphere(r * 1.4, at=(dx, dy, dz), seg=4, rings=2, mat=BUBBLE, smooth=True)
    drop = splash.to_object("Cloud_FishSplash_0")
    drop.location = (x, y, z)
    return [fish, drop]


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def build():
    rng = random.Random(SEED)
    objects = []

    # 停栖海鸥 ×8：脚底 = 原点，z = 云台顶；朝向 = 外向方位 + 微抖
    for i, (x, y, z, heading, pose, wing_mat) in enumerate(GULLS):
        m = K.Mesher()
        _build_gull(m, rng, pose, wing_mat)
        obj = m.to_object("Cloud_GullPerch_%d" % i)
        obj.location = (x, y, z)
        obj.rotation_euler = (0.0, 0.0, heading + rng.uniform(-0.12, 0.12))
        objects.append(obj)

    # 鸟巢 ×2
    for i, (x, y, z, rz, eggs) in enumerate(NESTS):
        m = K.Mesher()
        _build_nest(m, rng, eggs)
        obj = m.to_object("Cloud_Nest_%d" % i)
        obj.location = (x, y, z)
        obj.rotation_euler = (0.0, 0.0, rz)
        objects.append(obj)

    # 羽毛 ×12（随机朝向 + 大小微差）
    for i, (x, y, z, mat) in enumerate(FEATHERS):
        m = K.Mesher()
        fl = rng.uniform(0.80, 1.25)
        pts = [(p[0] * fl, p[1] * fl) for p in _FEATHER_PTS]
        m.poly_extrude(pts, 0.0, 0.007, at=(x, y, z),
                       rz=rng.uniform(0.0, TAU), mat=mat, smooth=False)
        objects.append(m.to_object("Cloud_Feather_%d" % i))

    # 贝壳 ×7
    for i, (x, y, z, mat) in enumerate(SHELLS):
        m = K.Mesher()
        _shell(m, rng, mat)
        obj = m.to_object("Cloud_Shell_%d" % i)
        obj.location = (x, y, z)
        obj.rotation_euler = (0.0, 0.0, rng.uniform(0.0, TAU))
        objects.append(obj)

    # 跃水鱼 ×1 + 水花
    objects.extend(_build_fish_jump())

    return objects
