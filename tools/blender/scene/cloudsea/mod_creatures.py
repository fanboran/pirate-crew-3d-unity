# -*- coding: utf-8 -*-
"""cloudsea/mod_creatures.py —— 水下生物分件：鱼群 / 大型游泳者 / 水母 / 底栖动物。

【内容（对象清单；名字 = Cloud_Creature_<物种>_<序号>，Unity 侧按物种名分派控制器）】
    · 鱼群（一群一个对象，群内个体各有朝向与种子）：小丑鱼 Clownfish / 蓝吊 Tang /
      黄吊 YellowTang / 银鱼 Bait
    · 单体游泳者：剑鱼 Sword / 河豚 Puffer / 魔鬼鱼 Manta / 海龟 Turtle / 海马 Seahorse
    · 漂浮：水母 Jelly
    · 底栖：章鱼 Octopus / 海葵 Anemone / 螃蟹 CrabWalk（横爬）与 CrabWave（挥钳）/
      海星 Star 与 StarGold

【骨架在这里建，动画不在这里烘】每个物种登记自己的关节并把顶点绑上去（creature_kit.py）。
关节的**角色名是 Blender 与 Unity 两边共用的契约**——运行时 CloudSeaRig 从关节空物体读静止位置与
父子关系，运动学控制器（CloudSeaBrains / CloudSeaCrawlers）按角色名找关节去解，改了这边的名字
就要同步改那边（验证台的 CloudSeaKinTests 按同一份角色名搭合成骨架）。总装时 bind_joints 给全场景
的关节统一编号、落成空物体 J<编号>_<角色>，随 FBX 一起导出。
    鱼类        s0…（头 → 尾的脊链，节点按沿体参数落位）· finL / finR（胸鳍根，挂在 s0 上）
    河豚        body · spike（刺，挂在 body 上）· finL / finR · tail
    魔鬼鱼      body · wL0…3 / wR0…3（翼根 → 翼尖，逐级父子）· tail0…4（细尾，驱动链）
    海龟        body · n0…2（颈：根 / 头基 / 吻）· fL0…2 / fR0…2（前鳍，驱动链）·
                bL0-1 / bR0-1（后鳍，挂在 body 上）
    海马        p0…9（卷尾 → 颈的脊链）· head（挂 p8）· fin（背鳍，挂 p5）
    水母        rim / mid / apex（钟口 / 中段 / 钟顶）· t<i>_0…（触手）· a<i>_0…（口腕）
    章鱼        body · mantle（挂 body）· w<a>_0…（八条腕，驱动链）
    海葵        base · f<i>_0…（触手，驱动链）
    螃蟹        body · eyeL / eyeR（眼柄根）· aL_0…2 / aR_0…2（肩 / 肘 / 螯）·
                lL<i>_0…3 / lR<i>_0…3（髋 / 膝 / 踝 / 尖）
    海星        centre · w<a>_0…2（中心 / 腕中 / 腕尖）

【比例尺】卡通夸张：小鱼体长 ≈ 1 单位（= 0.86 m，取景里 24 艺术像素起），剑鱼 3 单位、
魔鬼鱼翼展 4 单位，螃蟹壳宽 0.6 单位、海星直径 0.9 单位——隔着水色与折射都读得出轮廓。

【摆放】锚点全部落在默认取景（CameraFraming：偏航 45° / 俯角 30°）读得到的水面上；
云台的裙摆会挡住它后下方的水，可见性用 preview_gameview.py 按同一机位核对。
固定种子，重跑同图。
"""

import math
import random

import cloudsea_common as H
import creature_kit as CK
import mod_sea as SEA              # 海床高度场单源（总装里 sea 先于 creatures 装载）

PREFIX = "Cloud_Creature_"
TAU = math.tau
SEED = 20261125

# ---- 材质槽（style_tokens 的 Cloud_* 生物色卡）----
CLOWN = "Cloud_Clown"
FIN_CLOWN = "Cloud_Fin_Clown"
TANG = "Cloud_Tang"
TANG_YELLOW = "Cloud_TangYellow"
FIN_YELLOW = "Cloud_Fin_Yellow"
SILVER = "Cloud_Silver"
FIN_PALE = "Cloud_Fin_Pale"
SWORD = "Cloud_Sword"
SWORD_BELLY = "Cloud_SwordBelly"
FIN_SWORD = "Cloud_Fin_Sword"
PUFFER = "Cloud_Puffer"
PUFFER_SPOT = "Cloud_PufferSpot"
FIN_PUFFER = "Cloud_Fin_Puffer"
JELLY = "Cloud_Jelly"
JELLY_TOP = "Cloud_JellyTop"
FIN_JELLY = "Cloud_Fin_Jelly"
MANTA = "Cloud_Manta"
SHELL = "Cloud_Shell"
SHELL_DARK = "Cloud_ShellDark"
TURTLE_SKIN = "Cloud_TurtleSkin"
OCTOPUS = "Cloud_Octopus"
CRAB = "Cloud_Crab"
#: 蟹的整件缩放。按原尺寸建（壳宽 0.62、壳顶离地 0.28）后整体乘它：创始人要求至少缩到一半。
#: Unity 侧的步态常数按身体高度等比换算（CloudSeaCrawlers.CrabBrain），物种表里的半径同步。
CRAB_SCALE = 0.5
STARFISH = "Cloud_Starfish"
ANEMONE = "Cloud_Anemone"
ANEMONE_BASE = "Cloud_AnemoneBase"
LILAC = "Cloud_CoralPurple"
GOLD = "Cloud_Yellow"
BONE = "Cloud_Bone"

EYE, INK, BAND = CK.EYE, CK.INK, CK.BAND


# ---------------------------------------------------------------------------
# 共用小件
# ---------------------------------------------------------------------------

#: 鱼类脊链的节点落位（沿体参数：吻 0 → 尾柄 1；最后一节是尾鳍）。
SPINE_SMALL = (0.0, 0.3, 0.62, 1.0, 1.55)
SPINE_BAIT = (0.0, 0.5, 1.0, 1.7)
SPINE_SWORD = (0.0, 0.14, 0.28, 0.42, 0.56, 0.70, 0.84, 1.0, 1.2)


def spine(c, ts, x_head, body_len):
    """鱼类的脊链：节点按沿体参数 ts 落在中轴上（Blender x 前）。返回 (节点序号, 蒙皮函数)。"""
    nodes = c.chain("s", [(x_head - t * body_len, 0.0, 0.0) for t in ts])

    def t_of(v):
        s = (x_head - v[0]) / body_len
        if s <= ts[0]:
            return 0.0
        for k in range(len(ts) - 1):
            if s < ts[k + 1]:
                return k + (s - ts[k]) / (ts[k + 1] - ts[k])
        return float(len(ts) - 1)

    return nodes, CK.along_chain(nodes, t_of)


def fish_body(c, x_head, body_len, keys, n, body, ts, bands=(), paint=None, rings=20):
    """鱼身放样 + 脊链。keys = [(t, 背线 z, 腹线 z, 半宽)]（t = 沿轴参数，吻 0 → 尾柄 1）。
    bands = [(t0, t1, 槽)] 沿轴换色的条纹（边界处各加一道环）；paint(k, j, t中点) → 槽名，
    优先于 bands。身体与竖直鳍都按 x 落在链上连续插值。返回 (节点序号, 蒙皮函数)。"""
    edges = sorted({e for (t0, t1, _) in bands for e in (t0, t1)})
    ts_ = CK.merge_sorted(edges, CK.smooth_samples(rings), keep_out=0.014)
    rings_ = []
    for t in ts_:
        top = CK.curve([(k[0], k[1]) for k in keys], t)
        bot = CK.curve([(k[0], k[2]) for k in keys], t)
        hw = CK.curve([(k[0], k[3]) for k in keys], t)
        rings_.append((x_head - t * body_len, 0.0, (top + bot) * 0.5, max(hw, 0.0),
                       max((top - bot) * 0.5, 0.0)))
    nodes, skin = spine(c, ts, x_head, body_len)

    def face_mat(k, j):
        tm = (ts_[k] + ts_[k + 1]) * 0.5
        if paint is not None:
            slot = paint(k, j, tm)
            if slot is not None:
                return slot
        for t0, t1, slot in bands:
            if t0 <= tm <= t1:
                return slot
        return body

    CK.loft(c, rings_, body, n, skin=skin, face_mat=face_mat)
    return nodes, skin


def vfin(c, pts, mat, skin, thick=0.022):
    """竖直鳍（尾 / 背 / 臀）：pts = (x, z) 多边形，鳍面在 x–z 平面上，随身体一起弯。"""
    CK.sheet(c, pts, thick, CK.frame((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0)).place,
             mat, skin=skin)


def pectoral(c, side, root, direction, pts, mat, joint):
    """胸鳍（成对，side = ±1 = ±Y 侧）。root = 鳍根（= 关节静止位置），direction = 鳍展方向
    （水平面内，按 +Y 侧给，另一侧镜像）。鳍面朝背上方倾斜；整片刚性挂在鳍根关节上。"""
    ex = (direction[0], direction[1] * side, 0.0)
    fr = CK.frame(root, ex, (0.0, -0.35 * side, 0.94))
    CK.sheet(c, pts, 0.02, fr.place, mat, skin=CK.rigid(joint))


def bait_eye(c, centre, out, r, skin):
    """小鱼的点眼：只一颗深色珠（银鱼 20 像素长，分不出眼白与瞳仁）。"""
    o = [out[i] / math.sqrt(sum(x * x for x in out)) for i in range(3)]
    CK.blob(c, (centre[0] + o[0] * r * 0.3, centre[1] + o[1] * r * 0.3, centre[2] + o[2] * r * 0.3),
            (r, r, r), INK, n=4, rings=2, skin=skin)


# ---------------------------------------------------------------------------
# 小鱼（体长 ≈ 1 单位）
# ---------------------------------------------------------------------------

def clownfish(seed=0.0):
    """小丑鱼：橙身、三道白条纹黑镶边、圆尾。体长 1.0（吻 +0.37 → 尾鳍尖 -0.60）。"""
    c = CK.Creature(seed)
    body, tail = 0.72, 0.374
    keys = [(0.00, 0.000, 0.000, 0.000), (0.06, 0.110, -0.130, 0.062), (0.16, 0.185, -0.225, 0.090),
            (0.32, 0.215, -0.245, 0.100), (0.50, 0.205, -0.215, 0.094), (0.68, 0.165, -0.160, 0.074),
            (0.84, 0.100, -0.085, 0.046), (0.94, 0.052, -0.044, 0.026), (1.00, 0.040, -0.036, 0.018)]
    bands = [(0.115, 0.150, INK), (0.150, 0.250, BAND), (0.250, 0.285, INK),
             (0.400, 0.435, INK), (0.435, 0.560, BAND), (0.560, 0.595, INK),
             (0.780, 0.810, INK), (0.810, 0.885, BAND), (0.885, 0.915, INK)]
    spine_nodes, skin = fish_body(c, tail, body, keys, 12, CLOWN, SPINE_SMALL, bands=bands)
    vfin(c, [(-0.310, 0.026), (-0.400, 0.100), (-0.500, 0.145), (-0.585, 0.120), (-0.605, 0.0),
             (-0.585, -0.120), (-0.500, -0.140), (-0.400, -0.095), (-0.310, -0.026)], FIN_CLOWN, skin)
    vfin(c, [(0.035, 0.170), (-0.010, 0.290), (-0.090, 0.335), (-0.190, 0.300), (-0.285, 0.160),
             (-0.300, 0.020), (-0.220, 0.070), (-0.120, 0.125), (-0.030, 0.160)], FIN_CLOWN, skin)
    vfin(c, [(-0.100, -0.110), (-0.150, -0.270), (-0.230, -0.280), (-0.280, -0.160), (-0.265, -0.060),
             (-0.190, -0.110)], FIN_CLOWN, skin)
    head = CK.rigid(spine_nodes[0])
    for side in (1, -1):
        root = (0.129, 0.075 * side, -0.020)
        fin = c.joint("finL" if side > 0 else "finR", root, spine_nodes[0])
        pectoral(c, side, root, (-0.55, 0.835),
                 [(-0.02, -0.035), (-0.02, 0.050), (0.09, 0.075), (0.19, 0.035), (0.20, -0.020),
                  (0.10, -0.055)], FIN_CLOWN, fin)
        CK.eye(c, (0.295, 0.070 * side, 0.025), (0.25, side, 0.15), 0.048, skin=head)
    return c


def blue_tang(seed=0.0):
    """蓝吊：宝蓝椭圆身、体侧一道黑「画板」纹、黄尾。体长 1.15（吻 +0.41 → 尾尖 -0.67）。"""
    c = CK.Creature(seed)
    body, head_x = 0.86, 0.413
    keys = [(0.00, 0.000, 0.000, 0.000), (0.06, 0.070, -0.080, 0.040), (0.16, 0.150, -0.170, 0.070),
            (0.32, 0.225, -0.235, 0.086), (0.48, 0.250, -0.235, 0.086), (0.64, 0.220, -0.190, 0.072),
            (0.80, 0.140, -0.120, 0.046), (0.93, 0.065, -0.055, 0.026), (1.00, 0.050, -0.042, 0.018)]

    def paint(k, j, tm):
        side = j in (2, 3, 8, 9)                   # 侧面中带
        upper = j in (1, 10)                       # 上侧（纹在前半更宽）
        lower = j in (4, 7)                        # 下侧（纹在后半更宽）
        if 0.30 <= tm <= 0.93 and (side or (upper and tm <= 0.62) or (lower and tm >= 0.56)):
            return INK
        return None

    spine_nodes, skin = fish_body(c, head_x, body, keys, 12, TANG, SPINE_SMALL, paint=paint, rings=18)
    vfin(c, [(-0.400, 0.045), (-0.470, 0.160), (-0.600, 0.190), (-0.665, 0.0), (-0.600, -0.190),
             (-0.470, -0.160), (-0.400, -0.045)], FIN_YELLOW, skin)
    vfin(c, [(0.240, 0.150), (0.120, 0.330), (-0.100, 0.330), (-0.420, 0.070), (-0.420, 0.020),
             (-0.200, 0.130), (0.050, 0.170)], TANG, skin)
    vfin(c, [(-0.050, -0.185), (-0.150, -0.330), (-0.380, -0.260), (-0.420, -0.020), (-0.230, -0.150)],
         TANG, skin)
    head = CK.rigid(spine_nodes[0])
    for side in (1, -1):
        root = (0.150, 0.082 * side, -0.020)
        fin = c.joint("finL" if side > 0 else "finR", root, spine_nodes[0])
        pectoral(c, side, root, (-0.55, 0.835),
                 [(-0.02, -0.035), (-0.02, 0.050), (0.10, 0.080), (0.21, 0.030), (0.19, -0.030),
                  (0.09, -0.060)], FIN_YELLOW, fin)
        CK.eye(c, (0.320, 0.066 * side, 0.030), (0.25, side, 0.15), 0.045, skin=head)
    return c


def yellow_tang(seed=0.0):
    """黄吊：柠檬黄高身、背鳍基部一道白棱。体长 1.0（吻 +0.34 → 尾尖 -0.60）。"""
    c = CK.Creature(seed)
    body, head_x = 0.74, 0.340
    keys = [(0.00, 0.000, 0.000, 0.000), (0.05, 0.075, -0.090, 0.034), (0.14, 0.215, -0.235, 0.060),
            (0.28, 0.330, -0.300, 0.074), (0.44, 0.365, -0.310, 0.076), (0.60, 0.330, -0.270, 0.062),
            (0.76, 0.230, -0.185, 0.044), (0.90, 0.110, -0.095, 0.026), (1.00, 0.075, -0.062, 0.016)]

    def paint(k, j, tm):
        if j in (0, 11) and 0.16 <= tm <= 0.78:
            return BAND
        return None

    spine_nodes, skin = fish_body(c, head_x, body, keys, 12, TANG_YELLOW, SPINE_SMALL, paint=paint)
    vfin(c, [(-0.380, 0.070), (-0.440, 0.210), (-0.560, 0.250), (-0.600, 0.0), (-0.560, -0.250),
             (-0.440, -0.210), (-0.380, -0.070)], FIN_YELLOW, skin)
    head = CK.rigid(spine_nodes[0])
    for side in (1, -1):
        root = (0.110, 0.072 * side, -0.010)
        fin = c.joint("finL" if side > 0 else "finR", root, spine_nodes[0])
        pectoral(c, side, root, (-0.55, 0.835),
                 [(-0.02, -0.035), (-0.02, 0.050), (0.10, 0.080), (0.21, 0.030), (0.19, -0.030),
                  (0.09, -0.060)], FIN_YELLOW, fin)
        CK.eye(c, (0.250, 0.058 * side, 0.075), (0.25, side, 0.20), 0.045, skin=head)
    return c


def bait_fish(seed=0.0):
    """银鱼：细梭身、宝蓝背脊、叉尾。体长 0.62（吻 +0.23 → 尾尖 -0.39）。"""
    c = CK.Creature(seed)
    body, head_x = 0.48, 0.226
    keys = [(0.00, 0.000, 0.000, 0.000), (0.08, 0.040, -0.044, 0.024), (0.22, 0.070, -0.074, 0.038),
            (0.40, 0.080, -0.080, 0.042), (0.58, 0.070, -0.072, 0.036), (0.76, 0.046, -0.050, 0.026),
            (0.92, 0.022, -0.022, 0.014), (1.00, 0.017, -0.017, 0.010)]

    def paint(k, j, tm):
        return TANG if j in (0, 7) else None

    spine_nodes, skin = fish_body(c, head_x, body, keys, 8, SILVER, SPINE_BAIT, paint=paint, rings=12)
    vfin(c, [(-0.250, 0.018), (-0.330, 0.100), (-0.310, 0.0), (-0.330, -0.100), (-0.250, -0.018)],
         FIN_PALE, skin, thick=0.014)
    vfin(c, [(-0.010, 0.070), (-0.080, 0.150), (-0.120, 0.035)], FIN_PALE, skin, thick=0.014)
    for side in (1, -1):
        bait_eye(c, (0.150, 0.034 * side, 0.012), (0.3, side, 0.2), 0.030, CK.rigid(spine_nodes[0]))
    return c


# ---------------------------------------------------------------------------
# 大型游泳者
# ---------------------------------------------------------------------------

#: 剑鱼建模体长 3.4，整件缩到 0.85：脊顶处水面到沙面只有 1.85 m，帆尖（原点上方 0.83）与
#: 尾鳍下叶（下方 0.53）之间要留得出上下浮动的余量。
SWORD_SCALE = 0.85


def swordfish(seed=0.0):
    """剑鱼：长吻、高帆背鳍、月牙尾、钢蓝背浅腹。建模体长 3.4（吻尖 +1.72 → 尾尖 -1.40），
    整件乘 SWORD_SCALE。吻与眼刚性跟头部，身后八节脊链。"""
    c = CK.Creature(seed)
    body, head_x = 2.0, 1.0
    keys = [(0.00, 0.000, 0.000, 0.000), (0.04, 0.095, -0.105, 0.075), (0.12, 0.230, -0.250, 0.165),
            (0.24, 0.330, -0.330, 0.215), (0.38, 0.360, -0.340, 0.220), (0.52, 0.330, -0.300, 0.195),
            (0.66, 0.255, -0.235, 0.150), (0.80, 0.165, -0.150, 0.095), (0.92, 0.090, -0.082, 0.050),
            (1.00, 0.062, -0.058, 0.034)]

    def paint(k, j, tm):
        if 5 <= j <= 10:
            return SWORD_BELLY
        return None

    spine_nodes, skin = fish_body(c, head_x, body, keys, 16, SWORD, SPINE_SWORD, paint=paint, rings=24)
    head = CK.rigid(spine_nodes[0])
    CK.tube(c, [(0.98, 0.0, 0.015), (1.25, 0.0, 0.04), (1.50, 0.0, 0.06), (1.72, 0.0, 0.075)],
            [0.080, 0.055, 0.030, 0.010], INK, n=6, skin=head)                # 剑吻（不随身体弯）
    vfin(c, [(0.740, 0.200), (0.620, 0.780), (0.400, 0.980), (0.120, 0.960), (-0.180, 0.780),
             (-0.460, 0.420), (-0.500, 0.140), (-0.200, 0.240), (0.100, 0.265), (0.420, 0.250),
             (0.620, 0.215)], FIN_SWORD, skin, thick=0.034)
    vfin(c, [(-0.970, 0.050), (-1.120, 0.420), (-1.400, 0.620), (-1.330, 0.200), (-1.280, 0.0),
             (-1.330, -0.200), (-1.400, -0.620), (-1.120, -0.420), (-0.970, -0.050)], FIN_SWORD, skin,
         thick=0.034)
    vfin(c, [(-0.350, -0.190), (-0.550, -0.520), (-0.700, -0.420), (-0.660, -0.090)], FIN_SWORD, skin,
         thick=0.03)
    for side in (1, -1):
        root = (0.300, 0.190 * side, -0.050)
        fin = c.joint("finL" if side > 0 else "finR", root, spine_nodes[0])
        pectoral(c, side, root, (-0.75, 0.66),
                 [(-0.03, -0.040), (-0.03, 0.040), (0.350, 0.070), (0.620, 0.0), (0.300, -0.050)],
                 FIN_SWORD, fin)
        CK.eye(c, (0.820, 0.128 * side, 0.060), (0.3, side, 0.3), 0.085, glint=True, skin=head)
    return c.scale(SWORD_SCALE)


def pufferfish(seed=0.0):
    """河豚：金黄圆身、橄榄绿斑与刺。原点 = 球心，直径 0.9。刺单独挂在 spike 关节上，
    鼓起来时它比身体放大得更多（刺先冒头）。"""
    c = CK.Creature(seed)
    body = c.joint("body", (0.0, 0.0, 0.0))
    spike = c.joint("spike", (0.0, 0.0, 0.0), body)
    on_body = CK.rigid(body)

    def spots(k, j):
        return PUFFER_SPOT if (k * 7 + j * 3 + (j * k) % 5) % 9 < 2 else (BAND if k < 3 else PUFFER)

    ball = [(math.cos(-math.pi / 2.0 + math.pi * i / 7.0), math.sin(-math.pi / 2.0 + math.pi * i / 7.0))
            for i in range(8)]                     # 自下极点到上极点的半圆母线
    CK.revolve(c, ball, PUFFER, 12, scale=(0.44, 0.38, 0.40), skin=on_body, face_mat=spots)
    for i in range(26):                                          # 刺：斐波那契球面布点
        z = 1.0 - 2.0 * (i + 0.5) / 26.0
        a = i * 2.399963
        d = (math.sqrt(1.0 - z * z) * math.cos(a), math.sqrt(1.0 - z * z) * math.sin(a), z)
        base = (d[0] * 0.44 * 0.93, d[1] * 0.38 * 0.93, d[2] * 0.40 * 0.93)
        tip = (d[0] * 0.44 * 1.10, d[1] * 0.38 * 1.10, d[2] * 0.40 * 1.10)
        _spike(c, base, tip, 0.045, PUFFER_SPOT, CK.rigid(spike))
    tail = c.joint("tail", (-0.380, 0.0, 0.0), body)
    on_tail = CK.rigid(tail)
    vfin(c, [(-0.380, 0.050), (-0.550, 0.150), (-0.580, 0.0), (-0.550, -0.150), (-0.380, -0.050)],
         FIN_PUFFER, on_tail)
    vfin(c, [(-0.020, 0.370), (-0.110, 0.520), (-0.220, 0.300)], FIN_PUFFER, on_body)
    vfin(c, [(-0.050, -0.350), (-0.130, -0.500), (-0.240, -0.290)], FIN_PUFFER, on_body)
    for side in (1, -1):
        root = (0.100, 0.360 * side, 0.020)
        fin = c.joint("finL" if side > 0 else "finR", root, body)
        pectoral(c, side, root, (-0.45, 0.90),
                 [(-0.02, -0.05), (-0.02, 0.07), (0.12, 0.10), (0.22, 0.0), (0.10, -0.07)],
                 FIN_PUFFER, fin)
        CK.eye(c, (0.300, 0.270 * side, 0.170), (0.5, side, 0.3), 0.088, glint=True, skin=on_body)
    CK.blob(c, (0.435, 0.0, -0.040), (0.045, 0.050, 0.040), INK, n=6, rings=2, skin=on_body)   # 喙
    return c


def _spike(c, base, tip, r, mat, skin):
    """四棱锥刺：底面埋在体内（闭合体，法线重算才稳）。"""
    bx, by, bz = base
    dx, dy, dz = (tip[i] - base[i] for i in range(3))
    ln = math.sqrt(dx * dx + dy * dy + dz * dz) or 1e-9
    d = (dx / ln, dy / ln, dz / ln)
    ref = (0.0, 0.0, 1.0) if abs(d[2]) < 0.9 else (1.0, 0.0, 0.0)
    a = (ref[1] * d[2] - ref[2] * d[1], ref[2] * d[0] - ref[0] * d[2], ref[0] * d[1] - ref[1] * d[0])
    al = math.sqrt(sum(x * x for x in a))
    a = (a[0] / al, a[1] / al, a[2] / al)
    b = (d[1] * a[2] - d[2] * a[1], d[2] * a[0] - d[0] * a[2], d[0] * a[1] - d[1] * a[0])
    verts = [tip]
    for k in range(4):
        ang = TAU * k / 4.0 + math.pi / 4.0
        verts.append((bx + r * (a[0] * math.cos(ang) + b[0] * math.sin(ang)),
                      by + r * (a[1] * math.cos(ang) + b[1] * math.sin(ang)),
                      bz + r * (a[2] * math.cos(ang) + b[2] * math.sin(ang))))
    c.add(verts, [(0, 1 + k, 1 + (k + 1) % 4) for k in range(4)] + [(4, 3, 2, 1)], mat, smooth=False,
          skin=skin)


#: 魔鬼鱼建模翼展 4.0，整件缩到 0.8：翼展 3.2 才能在侧湾里绕得开巡游圈。
MANTA_SCALE = 0.8
MANTA_SPAN = 2.0


def manta_ray(seed=0.0):
    """魔鬼鱼：菱形身、头前两只头鳍、细尾。建模翼展 4.0，整件乘 MANTA_SCALE。
    原点 = 身体中心（翼根）。翼面按离翼根的距离绑到翼链上（翼链逐级父子挂在 body 上）；
    细尾是驱动链。"""
    c = CK.Creature(seed)
    body = c.joint("body", (0.0, 0.0, 0.02))
    on_body = CK.rigid(body)
    span, n_u, n_v = MANTA_SPAN, 9, 6
    us = [math.sin(math.pi * (i / (n_u - 1.0) - 0.5)) for i in range(n_u)]     # 两端（翼尖）加密
    vs = [i / (n_v - 1.0) for i in range(n_v)]
    top, bot = {}, {}

    def point(u, v, upper):
        front, back = 0.95 - 1.55 * u * u, -1.05 + 0.75 * u * u
        thick = (0.34 * (1.0 - u * u) ** 0.8 + 0.02) * math.sin(math.pi * v) ** 0.7
        return (front + (back - front) * v, u * span,
                0.02 + thick * (0.62 if upper else -0.38))

    for i, u in enumerate(us):
        for j, v in enumerate(vs):
            top[(i, j)] = point(u, v, True)
            bot[(i, j)] = point(u, v, False)
    verts, index = [], {}

    def vid(p):
        if p not in index:
            index[p] = len(verts)
            verts.append(p)
        return index[p]

    faces, mats = [], []
    for upper, grid, slot in ((True, top, "Cloud_Manta"), (False, bot, BAND)):
        for i in range(n_u - 1):
            for j in range(n_v - 1):
                q = [grid[(i, j)], grid[(i + 1, j)], grid[(i + 1, j + 1)], grid[(i, j + 1)]]
                ids = [vid(p) for p in (q if upper else q[::-1])]
                faces.append(tuple(ids))
                mats.append(slot)
    for i in range(n_u - 1):                     # 前缘 / 后缘封边
        for j in (0, n_v - 1):
            q = [top[(i, j)], top[(i + 1, j)], bot[(i + 1, j)], bot[(i, j)]]
            faces.append(tuple(vid(p) for p in q))
            mats.append("Cloud_Manta")
    for j in range(n_v - 1):                     # 左右翼尖封边
        for i in (0, n_u - 1):
            q = [top[(i, j)], top[(i, j + 1)], bot[(i, j + 1)], bot[(i, j)]]
            faces.append(tuple(vid(p) for p in q))
            mats.append("Cloud_Manta")

    wing_nodes = {}
    for side, name in ((1, "wL"), (-1, "wR")):
        parent = body
        chain = []
        for k, y in enumerate((0.25, 0.85, 1.45, span)):
            u = y / span
            parent = c.joint(name + str(k), (-0.05 - 0.4 * u * u, side * y, 0.02), parent)
            chain.append(parent)
        wing_nodes[side] = chain
    skin_left = CK.along_chain(wing_nodes[1], lambda p: abs(p[1]) / span * 3.0)
    skin_right = CK.along_chain(wing_nodes[-1], lambda p: abs(p[1]) / span * 3.0)
    c.add(verts, faces, mats, smooth=True,
          skin=lambda v: (skin_left if v[1] >= 0.0 else skin_right)(v))

    CK.tube(c, [(0.86, 0.17, 0.12), (0.98, 0.20, 0.06), (1.04, 0.21, 0.02)], [0.062, 0.045, 0.030],
            MANTA, n=5, skin=on_body)
    CK.tube(c, [(0.86, -0.17, 0.12), (0.98, -0.20, 0.06), (1.04, -0.21, 0.02)],
            [0.062, 0.045, 0.030], MANTA, n=5, skin=on_body)
    tail_pts = [(-1.0, 0.0, 0.02), (-1.35, 0.0, 0.02), (-1.7, 0.0, 0.0), (-2.05, 0.0, -0.02),
                (-2.3, 0.0, -0.04)]
    tail = c.chain("tail", tail_pts)
    CK.tube(c, tail_pts, [0.036, 0.028, 0.021, 0.014, 0.008], MANTA, n=4, joints=tail)
    for side in (1, -1):
        CK.eye(c, (0.700, 0.300 * side, 0.170), (0.3, side, 0.5), 0.072, glint=True, skin=on_body)
    return c.scale(MANTA_SCALE)


def sea_turtle(seed=0.0):
    """海龟：绿壳深色盾片、黄绿皮肤、长前鳍短后鳍。原点 = 壳底中心（腹甲），壳长 1.4。
    壳 / 后鳍挂在 body 上；颈与前鳍是驱动链（IK）。"""
    c = CK.Creature(seed)
    body = c.joint("body", (0.0, 0.0, 0.10))
    on_body = CK.rigid(body)

    def scutes(k, j):
        return SHELL_DARK if (k == 4 or (k == 3 and j % 2 == 0) or (k == 1 and j % 2 == 1)) else SHELL

    CK.revolve(c, [(0.62, 0.05), (0.63, 0.12), (0.56, 0.24), (0.40, 0.34), (0.20, 0.40), (0.0, 0.42)],
                SHELL, 10, scale=(1.15, 1.0, 1.0), skin=on_body, face_mat=scutes)
    CK.revolve(c, [(0.60, 0.0), (0.60, 0.07)], BONE, 10, scale=(1.15, 1.0, 1.0), skin=on_body)
    neck = c.chain("n", [(0.50, 0.0, 0.13), (0.72, 0.0, 0.16), (0.95, 0.0, 0.17)])
    on_head = CK.rigid(neck[1])
    CK.blob(c, (0.80, 0.0, 0.17), (0.24, 0.18, 0.17), TURTLE_SKIN, n=8, rings=4, skin=on_head)
    CK.tube(c, [(0.50, 0.0, 0.13), (0.68, 0.0, 0.15)], [0.125, 0.135], TURTLE_SKIN, n=6,
            skin=CK.rigid(neck[0]))
    CK.tube(c, [(-0.64, 0.0, 0.11), (-0.82, 0.0, 0.07)], [0.065, 0.020], TURTLE_SKIN, n=4, skin=on_body)
    for side in (1, -1):
        tag = "L" if side > 0 else "R"
        for (root, direction, pts, thick, prefix) in (
                ((0.30, 0.50, 0.14), (-0.55, 0.835),
                 [(-0.05, -0.09), (-0.05, 0.10), (0.30, 0.17), (0.62, 0.12), (0.80, 0.0),
                  (0.58, -0.10), (0.25, -0.13)], 0.07, "f"),
                ((-0.55, 0.38, 0.08), (-0.35, 0.94),
                 [(-0.04, -0.07), (-0.04, 0.07), (0.22, 0.13), (0.40, 0.0), (0.20, -0.12)], 0.05, "b")):
            origin = (root[0], root[1] * side, root[2])
            ex = (direction[0], direction[1] * side, 0.0)
            fr = CK.frame(origin, ex, (0.0, -0.25 * side, 0.97))
            step = 0.40
            nodes = [origin,
                     (origin[0] + ex[0] * step, origin[1] + ex[1] * step, origin[2]),
                     (origin[0] + ex[0] * step * 2.0, origin[1] + ex[1] * step * 2.0, origin[2])]
            if prefix == "f":
                chain = c.chain(prefix + tag, nodes)
                CK.sheet(c, pts, thick, fr.place, TURTLE_SKIN, smooth=True,
                         skin=CK.along_chain(chain, lambda p, fr=fr: fr.u(p) / step))
            else:
                rear = c.joint(prefix + tag + "0", nodes[0], body)
                c.joint(prefix + tag + "1", nodes[1], rear)
                CK.sheet(c, pts, thick, fr.place, TURTLE_SKIN, smooth=True, skin=CK.rigid(rear))
        CK.eye(c, (0.900, 0.135 * side, 0.210), (0.4, side, 0.4), 0.066, glint=True, skin=on_head)
    return c


def seahorse(seed=0.0):
    """海马：直立、卷尾、管吻、背鳍颤动。原点 = 卷尾底部，高 1.05。
    脊链从卷尾（p0，锚定）排到颈（p9）；头挂在 p8 上，背鳍挂在 p5 上。"""
    c = CK.Creature(seed)
    path = [(-0.05, 0.06), (0.08, 0.03), (0.17, 0.10), (0.19, 0.20), (0.13, 0.30), (0.02, 0.42),
            (-0.04, 0.55), (-0.05, 0.68), (-0.02, 0.79), (0.03, 0.87)]
    radii = [0.030, 0.045, 0.060, 0.075, 0.090, 0.105, 0.110, 0.100, 0.085, 0.075]
    spine_nodes = c.chain("p", [(x, 0.0, z) for x, z in path])
    CK.tube(c, [(x, 0.0, z) for x, z in path], radii, LILAC, n=8, joints=spine_nodes)
    head = c.joint("head", (0.03, 0.0, 0.87), spine_nodes[8])
    on_head = CK.rigid(head)
    CK.blob(c, (0.10, 0.0, 0.92), (0.12, 0.10, 0.11), LILAC, n=8, rings=4, skin=on_head)
    CK.tube(c, [(0.17, 0.0, 0.90), (0.27, 0.0, 0.875), (0.35, 0.0, 0.85)], [0.058, 0.045, 0.034],
            LILAC, n=6, skin=on_head)
    fin = c.joint("fin", (-0.125, 0.0, 0.52), spine_nodes[5])
    fr = CK.frame((-0.125, 0.0, 0.52), (0.0, 0.0, 1.0), (-1.0, 0.0, 0.0))
    CK.sheet(c, [(-0.10, 0.02), (0.10, 0.02), (0.07, 0.10), (-0.04, 0.13)], 0.016, fr.place, FIN_PALE,
             skin=CK.rigid(fin))
    for side in (1, -1):
        CK.eye(c, (0.120, 0.086 * side, 0.950), (0.3, side, 0.3), 0.050, glint=True, skin=on_head)
    return c


# ---------------------------------------------------------------------------
# 漂浮与固着
# ---------------------------------------------------------------------------

#: 水母建模钟顶 +0.52、触手到 -1.35，整件缩到 0.78：全高 1.5 m，沙脊上钟顶到水面还有余量。
JELLY_SCALE = 0.78
JELLY_BELL = (0.0, 0.26, 0.52)            # 钟口 / 中段 / 钟顶的高度


def jellyfish(seed=0.0):
    """水母：粉钟浅顶、九根触手四条口腕。建模钟顶 +0.52、触手到 -1.35，整件乘 JELLY_SCALE。
    原点 = 钟口中心。铃按高度在钟口 / 中段 / 钟顶三个关节之间插值；触手与口腕是 Verlet 链。"""
    c = CK.Creature(seed)
    rim = c.joint("rim", (0.0, 0.0, JELLY_BELL[0]))
    mid = c.joint("mid", (0.0, 0.0, JELLY_BELL[1]))
    apex = c.joint("apex", (0.0, 0.0, JELLY_BELL[2]))
    CK.revolve(c, [(0.50, 0.00), (0.50, 0.06), (0.455, 0.20), (0.36, 0.34), (0.22, 0.45), (0.09, 0.51),
                   (0.0, 0.52)], JELLY, 12,
               skin=CK.by_height([(JELLY_BELL[0], rim), (JELLY_BELL[1], mid), (JELLY_BELL[2], apex)],
                                lambda v: v[2]),
               face_mat=lambda k, j: JELLY_TOP if k >= 3 else JELLY)
    rng = random.Random(int(seed * 100003) + 7)

    def strand(prefix, points, radii, n, mat):
        nodes = c.chain(prefix, points)
        CK.tube(c, points, radii, mat, n=n, joints=nodes)

    for i in range(9):
        a = TAU * i / 9.0 + 0.2
        r = 0.40
        sway = 0.07 * math.sin(i * 2.1)
        pts = [(r * math.cos(a), r * math.sin(a), z) for z in (0.01, -0.25, -0.50)]
        pts += [((r + 0.08) * math.cos(a) - sway * math.sin(a),
                 (r + 0.08) * math.sin(a) + sway * math.cos(a), -0.80),
                ((r + 0.04) * math.cos(a) + sway * math.sin(a),
                 (r + 0.04) * math.sin(a) - sway * math.cos(a), -1.08),
                ((r + 0.11) * math.cos(a), (r + 0.11) * math.sin(a), -1.35)]
        strand("t%d_" % i, pts, [0.030, 0.026, 0.022, 0.018, 0.015, 0.010], 4, FIN_JELLY)
    for i in range(4):
        a = TAU * i / 4.0 + 0.9
        pts = [(0.12 * math.cos(a), 0.12 * math.sin(a), z) for z in (0.02, -0.30, -0.60, -0.90)]
        strand("a%d_" % i, pts, [0.062, 0.055, 0.045, 0.028], 5, JELLY)
    return c.scale(JELLY_SCALE)


def octopus(seed=0.0):
    """章鱼：珊瑚红圆头、大眼、八条腕向外铺在沙上爬行。原点 = 腕底的地面接触中心，高 0.74。
    头（套膜）挂在 body 上随呼吸鼓缩；八条腕是 FABRIK 链。"""
    c = CK.Creature(seed)
    body = c.joint("body", (0.0, 0.0, 0.16))
    mantle = c.joint("mantle", (0.0, 0.0, 0.40), body)
    on_mantle = CK.rigid(mantle)
    CK.revolve(c, [(0.26, 0.08), (0.38, 0.16), (0.43, 0.30), (0.41, 0.46), (0.32, 0.60), (0.18, 0.70),
                   (0.0, 0.74)], OCTOPUS, 12, skin=on_mantle)
    for a in range(8):
        th = TAU * a / 8.0 + TAU / 16.0
        radii, pts = [0.105, 0.095, 0.080, 0.062, 0.042, 0.018], []
        for (r, curl, z) in ((0.24, 0.0, 0.16), (0.45, 0.05, 0.10), (0.68, 0.15, 0.07), (0.92, 0.32, 0.055),
                             (1.10, 0.55, 0.05), (1.22, 0.85, 0.06)):
            pts.append((r * math.cos(th + curl), r * math.sin(th + curl), z))
        nodes = c.chain("w%d_" % a, pts)
        CK.tube(c, pts, radii, OCTOPUS, n=5, joints=nodes,
                face_mat=lambda k, j: BAND if k >= 4 else OCTOPUS)
    for side in (1, -1):
        CK.eye(c, (0.300, 0.300 * side, 0.220), (0.45, side, 0.25), 0.098, glint=True, skin=on_mantle)
    return c


def anemone(seed=0.0):
    """海葵：紫红基座上一圈粉触手、白梢，随水流慢摆。原点 = 基座底，宽 0.8、高 0.6。
    每根触手是一条 Verlet 链。"""
    c = CK.Creature(seed)
    base = c.joint("base", (0.0, 0.0, 0.05))
    CK.revolve(c, [(0.26, 0.0), (0.30, 0.10), (0.26, 0.17)], ANEMONE_BASE, 10, skin=CK.rigid(base))
    rng = random.Random(int(seed * 100003) + 11)
    bases = [(0.0, 0.0)] + [(0.09 * math.cos(TAU * k / 6.0), 0.09 * math.sin(TAU * k / 6.0))
                            for k in range(6)]
    bases += [(0.17 * math.cos(TAU * k / 9.0 + 0.3), 0.17 * math.sin(TAU * k / 9.0 + 0.3))
              for k in range(9)]
    bases += [(0.24 * math.cos(TAU * k / 10.0 + 0.6), 0.24 * math.sin(TAU * k / 10.0 + 0.6))
              for k in range(10)]
    for i, (bx, by) in enumerate(bases):
        out = math.atan2(by, bx) if (bx or by) else rng.uniform(0.0, TAU)
        ox, oy = math.cos(out), math.sin(out)
        lean = 0.0 if not (bx or by) else 0.16
        pts = [(bx, by, 0.15), (bx + ox * 0.05, by + oy * 0.05, 0.31),
               (bx + ox * (0.10 + lean), by + oy * (0.10 + lean), 0.47),
               (bx + ox * (0.12 + 2.0 * lean), by + oy * (0.12 + 2.0 * lean), 0.60)]
        nodes = c.chain("f%d_" % i, pts)
        CK.tube(c, pts, [0.045, 0.040, 0.034, 0.026], ANEMONE, n=4, joints=nodes,
                face_mat=lambda k, j: BAND if k == 2 else ANEMONE)
    return c


# ---------------------------------------------------------------------------
# 底栖动物（腿 / 腕踩地）
# ---------------------------------------------------------------------------

def crab(seed=0.0, wave=False):
    """螃蟹：红壳、眼柄、双螯、每侧四条腿。wave = False 横爬（腿走步态）；True 原地举钳挥动。
    原点 = 地面接触中心。下面的尺寸是缩放前的（CRAB_SCALE = 0.5 后壳宽 0.31、壳顶离地 0.14）。
    蟹面朝本地 +X，横爬方向是本地 -Y（Blender；导入后是 Unity 的 -Z，
    即它的右侧）。两种蟹骨架相同：body · eyeL / eyeR · aL_0…2 / aR_0…2 · lL<i>_0…3 / lR<i>_0…3。"""
    c = CK.Creature(seed)
    body = c.joint("body", (0.0, 0.0, 0.16))
    on_body = CK.rigid(body)
    CK.revolve(c, [(0.27, 0.10), (0.31, 0.15), (0.29, 0.22), (0.20, 0.27), (0.0, 0.28)], CRAB, 12,
                scale=(0.92, 1.10, 1.0), skin=on_body)
    CK.revolve(c, [(0.25, 0.05), (0.27, 0.11)], BONE, 8, scale=(0.9, 1.05, 1.0), skin=on_body)
    for side, tag in ((1, "L"), (-1, "R")):
        stalk = (0.24, 0.09 * side, 0.22)
        top = (0.34, 0.11 * side, 0.40)
        eye = c.joint("eye" + tag, stalk, body)
        on_eye = CK.rigid(eye)
        CK.tube(c, [stalk, top], [0.024, 0.022], CRAB, n=4, skin=on_eye)
        CK.eye(c, (0.35, 0.11 * side, 0.42), (0.5, 0.4 * side, 0.75), 0.062, glint=True, skin=on_eye)

        shoulder = (0.16, 0.30 * side, 0.14)
        claw = [shoulder, (0.34, 0.44 * side, 0.12), (0.50, 0.40 * side, 0.10)]
        arm = c.chain("a%s_" % tag, claw)
        on_claw = CK.rigid(arm[1])
        CK.tube(c, claw, [0.050, 0.046, 0.040], CRAB, n=5, joints=arm)
        CK.blob(c, (0.60, 0.37 * side, 0.12), (0.125, 0.088, 0.075), CRAB, n=8, rings=3, z_lo=-0.2,
                skin=on_claw)
        CK.tube(c, [(0.66, 0.40 * side, 0.12), (0.82, 0.41 * side, 0.08)], [0.036, 0.012], CRAB, n=4,
                skin=on_claw)
        CK.tube(c, [(0.64, 0.34 * side, 0.06), (0.80, 0.35 * side, 0.03)], [0.032, 0.010], BONE, n=4,
                skin=on_claw)

        for i in range(4):                        # 四条腿：髋 → 膝（外上）→ 踝（外下）→ 足尖
            hip = (0.17 - 0.11 * i, (0.30 - 0.02 * abs(i - 1.5)) * side, 0.13)
            phi = (0.62 + 0.38 * i) * side         # 腿的伸向：前腿朝前外，后腿朝后外
            d = (math.cos(phi), math.sin(phi))
            knee = (hip[0] + d[0] * 0.19, hip[1] + d[1] * 0.19, 0.21)
            ankle = (hip[0] + d[0] * 0.36, hip[1] + d[1] * 0.36, 0.07)
            tip = (hip[0] + d[0] * 0.47, hip[1] + d[1] * 0.47, 0.0)
            pts = [hip, knee, ankle, tip]
            leg = c.chain("l%s%d_" % (tag, i), pts)
            CK.tube(c, pts, [0.028, 0.022, 0.017, 0.011], CRAB, n=4, joints=leg)
    return c.scale(CRAB_SCALE)


STAR_R_TIP, STAR_R_MID, STAR_R_NOTCH = 0.46, 0.23, 0.21


def starfish(seed=0.0, gold=False):
    """海星：五腕、腕背一列浅色疣点。原点 = 中心底面，直径 0.92、高 0.14。
    原地朝向 = 第一条腕指向本地 +X。每条腕两节骨骼（中心 → 腕中 → 腕尖）；中心盘的顶点按
    「离腕轴多近、离中心多远」在中心关节与最近的腕之间插值——腕抬起来时盘面跟着微微翘。"""
    c = CK.Creature(seed)
    body, dots = (GOLD, BAND) if gold else (STARFISH, GOLD)
    centre = c.joint("centre", (0.0, 0.0, 0.05))
    arms = []
    for a in range(5):
        th = TAU * a / 5.0
        arms.append(c.chain("w%d_" % a, [(0.0, 0.0, 0.02),
                                         (STAR_R_MID * math.cos(th), STAR_R_MID * math.sin(th), 0.03),
                                         (STAR_R_TIP * math.cos(th), STAR_R_TIP * math.sin(th), 0.03)]))
    step = TAU / 5.0

    def disc_skin(v):
        r = math.hypot(v[0], v[1])
        th = math.atan2(v[1], v[0])
        k = round(th / step)
        delta = th - k * step                       # 离最近一条腕的中轴的角差（±36°）
        nodes = arms[int(k) % 5]
        toward = (0.5 + 0.5 * math.cos(5.0 * delta)) ** 2 * CK.smoothstep(0.06, STAR_R_NOTCH, r)
        f = CK.clamp01((r - 0.20) / 0.20)           # 腕中 → 腕尖那段从腕中骨骼过渡到腕尖骨骼
        if toward < 0.98:
            return (centre, 1.0 - toward, nodes[0] if f < 0.5 else nodes[1], toward)
        return (nodes[0], 1.0 - f, nodes[1], f)

    per, n = 6, 30
    outline = []
    for k in range(n):
        th = TAU * k / n
        f = abs(math.cos(2.5 * th)) ** 0.9
        outline.append((th, STAR_R_NOTCH + (STAR_R_TIP - STAR_R_NOTCH) * f, f))
    verts = [(0.0, 0.0, 0.14)]
    for (scale, upper) in ((0.5, True), (1.0, True), (0.5, False), (1.0, False)):
        for (th, r, f) in outline:
            z = 0.14 * (1.0 - scale * scale) * (0.62 + 0.38 * f) if upper else 0.0
            verts.append((r * scale * math.cos(th), r * scale * math.sin(th), z))

    top_in, top_out, bot_in, bot_out = 1, 1 + n, 1 + 2 * n, 1 + 3 * n
    faces, mats = [], []
    for k in range(n):
        k1 = (k + 1) % n
        f = outline[k][2]
        top_slot = dots if f > 0.93 else body
        faces += [(0, top_in + k, top_in + k1), (top_in + k, top_out + k, top_out + k1, top_in + k1),
                  (top_out + k, bot_out + k, bot_out + k1, top_out + k1),
                  (bot_in + k, bot_in + k1, bot_out + k1, bot_out + k), (0, bot_in + k1, bot_in + k)]
        mats += [body, top_slot, body, body, body]
    c.add(verts, faces, mats, smooth=True, skin=disc_skin)
    for a in range(5):                            # 疣点：每条腕背两颗，跟着所在那节腕骨
        th = TAU * a / 5.0
        for r in (0.27, 0.36):
            z = 0.14 * (1.0 - (r / STAR_R_TIP) ** 2)
            CK.blob(c, (r * math.cos(th), r * math.sin(th), z - 0.012), (0.034, 0.034, 0.030), dots,
                    n=4, rings=2, skin=CK.along_chain(arms[a], lambda p: math.hypot(p[0], p[1])
                                                      / STAR_R_TIP * 2.0))
    return c


# ---------------------------------------------------------------------------
# 鱼群（一群一个对象）
# ---------------------------------------------------------------------------

def school(kind, count, spread, tight, rng):
    """建一群鱼：沿本地 +X 游向拉成梭形（tight = 0 紧凑纵列 → 1 松散云团），个体朝向各有微差、
    种子各不相同。返回 Creature（原点 = 群中心）。群里的每条鱼带自己的脊链与胸鳍关节，
    角色名前面加 f<i>（f0s0、f1finL……）。"""
    makers = {"Clownfish": clownfish, "Tang": blue_tang, "YellowTang": yellow_tang, "Bait": bait_fish}
    lengths = {"Clownfish": 1.0, "Tang": 1.15, "YellowTang": 1.0, "Bait": 0.62}
    maker, length = makers[kind], lengths[kind]
    c = CK.Creature(0.0)
    placed = []
    for i in range(count):
        gap = 0.34 * length
        for _try in range(24):
            u = rng.uniform(-1.0, 1.0) * spread * (0.8 + 0.5 * tight)
            v = rng.gauss(0.0, 0.22 * spread * (0.3 + tight))
            z = rng.gauss(0.0, 0.16)                 # 个体在群中心上下 ±0.3 内散布
            if all((u - p[0]) ** 2 + (v - p[1]) ** 2 + (z - p[2]) ** 2 > gap * gap for p in placed):
                break
        placed.append((u, v, z))
        one = maker(seed=rng.random())
        c.append(one, at=(u, v, z), yaw=rng.gauss(0.0, 0.30), pitch=rng.gauss(0.0, 0.08),
                 prefix="f%d" % i, seed=rng.random())
    return c


# ---------------------------------------------------------------------------
# 摆放（Blender 本地系；z 是 Blender 高度）
# ---------------------------------------------------------------------------

#: 鱼群：(物种, 条数, 锚点 x, y, z, 散布半径, 松紧)。锚点 = 巡游圈心，群内沿本地 +X 拉开。
#: 【摆在哪】云台的裙摆会挡住它后下方的水，默认取景下读得到的有三片（preview_gameview.py 的
#: 机位图与 --probe 可见性图）：**西南前湾**（X -12～-4、Y 6～13；相机偏向西南时在画面右侧）、
#: **东南湾**（X 8～15、Y -4～6；相机在场地中心时在画面左下）、**南侧前带**（X -2～8、Y 8～12；
#: 两种机位下都贴着画面底边）。相机跟着单位走，中心就在这两个机位之间——三片各放一套，
#: 南侧前带放两种机位都读得到的重点。西侧、北侧与各云台的后下方在所有典型机位下都被挡住，不放。
SCHOOLS = [
    ("Clownfish", 7, 1.2, 11.0, -1.20, 1.3, 0.50),      # 南侧前带（小丑鱼的家，旁边两株海葵）
    ("Clownfish", 6, 9.2, 5.0, -1.15, 1.1, 0.45),       # 东南湾
    ("Tang", 6, 9.8, 2.8, -1.45, 1.2, 0.50),            # 东南湾
    ("YellowTang", 7, -7.0, 10.6, -1.25, 1.4, 0.50),    # 西南前湾
    ("YellowTang", 6, 10.6, 0.6, -1.20, 1.2, 0.50),     # 东南湾
    ("Bait", 14, 4.0, 8.6, -1.05, 1.3, 0.90),           # 南侧前带
    ("Bait", 14, -3.4, 12.6, -1.35, 1.5, 0.90),         # 西南前湾 / 南侧前带交界
    ("Bait", 12, 12.4, -1.8, -1.15, 1.3, 0.90),         # 东南湾外缘
]

#: 单体游泳者：(物种, x, y, z)。运行时绕锚点巡游 / 漂浮。
SINGLES = [
    ("Sword", 0.0, 9.0, -1.60), ("Sword", 9.4, 1.4, -1.60),
    ("Manta", -5.6, 10.2, -1.60),
    ("Turtle", 3.2, 8.0, -1.50), ("Turtle", 11.6, 4.4, -1.50),
    ("Puffer", -1.6, 7.0, -1.70), ("Puffer", 11.4, 2.0, -1.70),
    ("Jelly", -3.2, 8.2, -1.20), ("Jelly", 2.2, 13.0, -1.20), ("Jelly", -8.6, 8.4, -1.25),
    ("Jelly", 5.2, 11.4, -1.20), ("Jelly", 9.0, -1.6, -1.25),
]

#: 贴底 / 固着：(物种, x, y)。z 由海床高度场给（SEA.bed_z）；朝向随机，俯视下挑朝向的除外（见 FACING_YAW）。
#: 贴底的在画面里比游泳层更低一截，南侧前带只放少数几只，主力放两个侧湾。
BOTTOM = [
    ("Octopus", -11.0, 3.2), ("Octopus", 10.8, -2.0),
    ("Anemone", 0.0, 12.2), ("Anemone", 1.4, 11.6), ("Anemone", 12.6, 2.2), ("Anemone", -9.4, 6.8),
    ("Seahorse", 12.0, 0.6), ("Seahorse", 12.8, -0.6), ("Seahorse", -7.4, 7.0),
    ("CrabWalk", -10.4, 7.4), ("CrabWalk", -8.0, 9.6), ("CrabWalk", -5.0, 10.2),
    ("CrabWalk", 3.4, 6.8), ("CrabWalk", 10.4, 3.4), ("CrabWalk", 13.0, 1.6),
    ("CrabWave", -9.0, 8.0), ("CrabWave", -3.0, 9.4), ("CrabWave", 1.6, 8.2), ("CrabWave", 5.0, 6.4),
    ("Star", -7.0, 7.6), ("StarGold", -7.6, 10.0), ("Star", 0.8, 10.0), ("StarGold", 3.4, 9.4),
    ("Star", 11.6, 1.0), ("StarGold", 9.6, -2.8), ("Star", -4.0, 8.0), ("StarGold", 2.0, 5.6),
]

#: 贴底 / 固着物的离床高度（米）：原点相对沙面的抬升。
BOTTOM_LIFT = {"Octopus": 0.0, "Anemone": 0.0, "Seahorse": 0.35, "CrabWalk": 0.0, "CrabWave": 0.0,
               "Star": 0.01, "StarGold": 0.01}

#: 朝向固定的贴底生物：俯视下只有特定朝向才读得出，所以不随机（各只带一点随机偏差）；没列的朝向随机。
#:   海马——侧扁，背对镜头就是一根竖条，必须侧面朝机位：0 / π = 头朝画面右 / 左（见 preview_creatures.py 的机位图）；
#:   海星——一条腕沿视线方向（正对或背离镜头）会被透视压成一根竖条，整只读成三叉戟。本文件的 (x, y)
#:     就是 Unity 的 (x, z)，方位角数值相同：默认机位朝向相机是 45°、画面右方是 135°。腕间 72°、
#:     第一条腕指向本地 +X，所以朝向取 135° + k·72° 时恰有一条腕水平横在画面上，离视线最近的腕也偏 18°。
#:     五个取值视觉等价，这里列两个只为保持随机数消耗不变；偏差要小，大了最坏的腕又会转回视线方向。
STAR_YAW = math.radians(135.0)
FACING_YAW = {"Seahorse": (0.0, math.pi), "Star": (STAR_YAW, STAR_YAW + TAU / 5.0),
              "StarGold": (STAR_YAW, STAR_YAW + TAU / 5.0)}
FACING_JITTER = {"Seahorse": 0.3, "Star": 0.05, "StarGold": 0.05}

#: 屏幕占位半径（米）：机位预览按它估「这只生物有多大一块落在可见水面上」（鱼群按实际散布）。
RADIUS = {"Clownfish": 1.6, "Tang": 1.7, "YellowTang": 1.6, "Bait": 1.7, "Sword": 1.7, "Manta": 2.3,
          "Turtle": 1.1, "Puffer": 0.6, "Jelly": 0.6, "Octopus": 1.2, "Anemone": 0.4,
          "Seahorse": 0.4, "CrabWalk": 0.3, "CrabWave": 0.35, "Star": 0.5, "StarGold": 0.5}

_MAKERS = {
    "Sword": swordfish, "Manta": manta_ray, "Turtle": sea_turtle, "Puffer": pufferfish, "Jelly": jellyfish,
    "Octopus": octopus, "Anemone": anemone, "Seahorse": seahorse,
    "CrabWalk": lambda seed: crab(seed), "CrabWave": lambda seed: crab(seed, wave=True),
    "Star": lambda seed: starfish(seed), "StarGold": lambda seed: starfish(seed, gold=True),
}


def build():
    rng = random.Random(SEED)
    objects = []
    counters = {}

    def named(species):
        counters[species] = counters.get(species, 0) + 1
        return "%s%s_%d" % (PREFIX, species, counters[species] - 1)

    for (kind, count, x, y, z, spread, tight) in SCHOOLS:
        obj = school(kind, count, spread, tight, rng).to_object(named(kind))
        obj.location = (x, y, z)
        obj["radius"] = RADIUS[kind]
        objects.append(obj)

    for (kind, x, y, z) in SINGLES:
        obj = _MAKERS[kind](rng.random()).to_object(named(kind))
        obj.location = (x, y, z)
        obj.rotation_euler = (0.0, 0.0, rng.uniform(0.0, TAU))
        obj["radius"] = RADIUS[kind]
        objects.append(obj)

    for (kind, x, y) in BOTTOM:
        obj = _MAKERS[kind](rng.random()).to_object(named(kind))
        obj.location = (x, y, SEA.bed_z(x, y) + BOTTOM_LIFT[kind])
        if kind in FACING_YAW:
            obj.rotation_euler = (0.0, 0.0, rng.choice(FACING_YAW[kind])
                                  + rng.uniform(-1.0, 1.0) * FACING_JITTER[kind])
        else:
            obj.rotation_euler = (0.0, 0.0, rng.uniform(0.0, TAU))
        obj["radius"] = RADIUS[kind]
        objects.append(obj)
    return objects
