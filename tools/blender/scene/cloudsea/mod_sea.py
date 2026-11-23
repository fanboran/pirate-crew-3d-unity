# -*- coding: utf-8 -*-
"""cloudsea/mod_sea.py —— 海水/海床/海面大平面分件（中央精细浅海 + 延伸到画面外的大海）。

【内容（对象清单）】
    · Cloud_Sea_Bed       海床：中央 X[-19,19] Y[-14,14] 放海草/岩/珊瑚/鱼群，海床细节
                          网格扩到 X[-80,80] Y[-80,80]（脊/洼交替沙丘，脊 -2.28、洼 -3.16，
                          外缘 1.2m 收到水底 -3.2）；细节网格外再铺一圈 Cloud_SeabedDeep
                          深海底板到 ±240 —— 镜头可见地面足迹半径 ≈43m，画面里看不到
                          海床/水面的矩形切边。无四面水壁、无底封口。
    · Cloud_Sea_Surface   水面：中央同范围细节起伏（z≈-0.4 死亡线），外圈平水面到 ±240；
                          两层在细节网格边界同高（-0.4），接缝在画面外。
    · Cloud_Sea_Weeds     海草床：10 锚点 × 6-10 株（每株 4-6 片带状叶共根，顺水流方向弯，
                          双面薄片）；前排（+X/-Y 附近）另有 3 丛 1.2-1.8m 海草树
                          （9-12 片宽叶），前景读得到；根部小石堆。
    · Cloud_Sea_Rocks     岩簇：8 处不规则岩块组（半埋）+ 周边碎石；前排 2 组 2-2.5m
                          岩拱/叠岩（石柱+横梁+叠石），前场读得出。
    · Cloud_Sea_Coral     珊瑚 12 处（每处 2 株枝状 + 扇/管/脑随机 1 株 + 1 团块，
                          粉/紫/橙/青/骨白/原橙六色档）+ 贝壳 / 14 只海星点缀。
    · Cloud_Sea_Debris    水底细节：散布小石块、碎石滩、贝壳、鱼骨（无人造垃圾）。
    · Cloud_FishSchool_0..9   鱼群 10 群（公共库 5 锚点 + 本件新增 5 群，分布到主云台下方
                          与两侧前场）：每群独立对象、原点 = 群中心（Unity 整体游动），
                          12-16 条/群、鱼体 0.53-0.87m，群内松紧各不同（紧=梭形纵列，
                          松=云团）；16 三角面/条（椭球身 + 双面尾/背/腹鳍）。
    · Cloud_Bubbles_0..2      气泡柱：海床升到水面附近的成串小球，原点 = 柱底。

【朝向纪律】像素着色路径是 Cull Back：海床/水面是开放薄面，绕序按 +Z 朝上建模，
  出口禁 recalc（见 kit_common.Mesher.to_object 的 recalc_normals 说明）；侧壁只修
  轴对齐竖面。海草叶与鱼鳍直接建双面（Unity 侧不用改任何东西）。

【纪律】材质只用 Cloud_* 自然物槽（零贴图零碰撞）；固定种子重跑同图；主题红线：
  只有海水/海床/岩石/海草/珊瑚/鱼群/气泡/贝壳等自然物，无人造物。
  海床/水面细节网格按 1.2-1.5m 格距取舍，外圈只铺四条大带，不追高模。
"""

import math
import random

import cloudsea_common as H
import kit_common as K

TAU = math.tau
SEED = 20261109

# ---- 水域与水位（Blender 本地系，对齐 cloudsea_common 口径）----
#: 精细内容（海草/岩/珊瑚/鱼群/碎石）的摆放范围。
X0, X1 = -19.0, 19.0
Y0, Y1 = -14.0, 14.0
TOP = H.SEA_TOP_Z        # -0.4 水面 = 现役死亡线
BOT = H.SEA_BOTTOM_Z     # -3.2 水底

#: 海床/水面**细节网格**的覆盖范围。镜头可见地面足迹（ortho 11.25 / 30° 俯角）约
#: 40×45m；相机聚焦高处平台时，画面中心落水点还会向远处偏 ~13m，最远可见海面点到
#: 场地焦点约 43m。扩到 ±80 后，无论聚焦哪座云台、转到哪个方位，切片边界（含外缘
#: 沉到水底的收边）都在画面外，海面在画面里是连续的海，不再出现矩形切边。
#: 精细布景仍只在 X0..X1 / Y0..Y1 内，外圈只保留沙丘。
BX0, BX1 = -80.0, 80.0
BY0, BY1 = -80.0, 80.0

#: 外围海面：细节网格外再铺一圈大平面到 ±240（远裁剪 + 距离雾在后面兜住），
#: 保证自由镜头转到任意方位都看不到海的物理边。
OCEAN_HALF = 240.0

# ---- 材质槽（全部 Cloud_* 自然物）----
SAND = "Cloud_Sand"
BED = "Cloud_Seabed"
DEEP_BED = "Cloud_SeabedDeep"
ROCK = "Cloud_Rock"
WATER = "Cloud_Water"
DEEP = "Cloud_WaterDeep"
WEED = "Cloud_Seaweed"
PLANT = "Cloud_Plant"
CORAL = "Cloud_Coral"
PINK = "Cloud_CoralPink"
PURPLE = "Cloud_CoralPurple"
ORANGE = "Cloud_CoralOrange"
TEAL = "Cloud_CoralTeal"
BONE = "Cloud_Bone"
BUBBLE = "Cloud_Bubble"
FISH_ORANGE = "Cloud_FishOrange"   # 鱼专属色卡（配背光边缘光，见 style_tokens 注）
FISH_YELLOW = "Cloud_FishYellow"
WHITE = "Cloud_White"
BLUE = "Cloud_Blue"

#: 本件鱼群锚点（前 5 = cloudsea_common.FISH_SCHOOLS；+5 群分布到主云台两侧与前场）
#: 2026-11-21 观感反馈：群数 8→10、前场再补两群，鱼群要「一眼看得见」。
MOD_FISH_SCHOOLS = list(H.FISH_SCHOOLS) + [
    (14.0, -8.5, -1.5, 2.6),     # 主云台东南（近 +X 侧）
    (16.0, 9.0, -2.0, 2.4),      # 东南前角（front34 主看位）
    (-6.5, 8.0, -1.4, 2.2),      # 主云台西南
    (9.0, -12.0, -1.6, 2.4),     # 南前场
    (-12.0, 9.5, -1.7, 2.2),     # 西北前场
    (12.0, 12.0, -0.9, 1.5),     # 前景主礁上方
    (18.5, 5.0, -0.9, 1.5),      # 右侧主礁上方
]
#: 群主色（银蓝/浅蓝/珊瑚橙）/ 游向（弧度）/ 群内松紧（0 紧=纵列 → 1 松=云团）
#: 鱼群主色：暖色为主（CORAL/YELLOW 与青色水强对比；BLUE/WHITE 只留少量杂色个体——
#: 首轮实拍蓝鱼与水色融为一体，创始人裁定换对比色卡）
# 2026-11-22 浅海 σ 吸收（红通道被吃）：主色改黄/白/少量橙——黄白在青水里读得出来，
# 橙只做点缀（近水面群）。
SCHOOL_DOM = [FISH_YELLOW, BONE, FISH_YELLOW, FISH_ORANGE, BONE, FISH_YELLOW,
              BONE, FISH_YELLOW, FISH_ORANGE, BONE, FISH_YELLOW, BONE]
SCHOOL_HEAD = [0.6, 2.4, 4.2, 1.3, 5.1, 0.2, 3.6, 4.9, 2.9, 1.1, 0.9, 3.9]
SCHOOL_TIGHT = [0.55, 0.4, 0.5, 0.8, 0.35, 0.6, 0.3, 0.75, 0.55, 0.45, 0.5, 0.45]

#: 海草床锚点（前 6 = cloudsea_common.SEAWEED_BEDS；+4 处靠 +X/+Y 前场）
MOD_SEAWEED_BEDS = list(H.SEAWEED_BEDS) + [
    (16.2, 8.4, 1.8),            # +X 侧前排
    (17.9, 0.9, 1.5),            # +X 侧最前（front34 直视）
    (8.6, 12.2, 1.6),            # +Y 侧前排
    (2.8, 13.0, 1.5),            # +Y 侧最前
    (-10.0, -11.0, 2.6),         # 西北远侧水带（默认镜头左上）
    (6.0, -11.0, 2.6),           # 北侧远水带（默认镜头右上）
    (14.0, -9.0, 2.2),           # 东北远侧水带
]
#: 各海草床的水流方向（弧度；同床叶片大致顺流倾斜）
BED_CURRENT = [0.5, 2.0, 5.6, 3.3, 1.2, 0.2, 2.9, 3.1, 3.4, 3.6,
               1.7, 4.4, 2.6]

#: 海草树（前场 tall 丛 + 中部/远侧各一处）
KELP_SPOTS = [(15.6, 4.6), (12.6, 11.8), (16.9, -4.2),
              (3.2, 6.8), (-7.8, 5.8), (-4.0, -6.2)]

#: 岩簇锚点（前 5 = cloudsea_common.ROCK_CLUSTERS；+3 处靠前场）
MOD_ROCK_CLUSTERS = list(H.ROCK_CLUSTERS) + [
    (13.2, 11.4, 1.3),           # +Y 侧前排
    (16.4, 3.0, 1.1),            # +X 侧前排
    (17.3, -9.0, 1.2),           # +X 侧后段
]

#: 岩拱/叠岩（2-2.5m 高大件：x, y, 顶高——不出水面）
ARCH_SPOTS = [(17.7, 8.2, 2.2), (12.6, 13.2, 1.95)]

#: 珊瑚丘锚点（x, y, 主材质）——主礁 6 组分布到**默认镜头可见的中部水湾**与前景水带，
#: 卫星礁沿各条水带铺开（不再全挤 +Y 边）。C# `CloudSeaWaterLab.CoralMounds` 与本表
#: 逐项同步（丘包 +0.16 m）；改表必须两边一起改。
CORAL_SPOTS = [
    # 主礁 6 组：中部水湾 3 组（主云台南/西南/东侧）+ 前景水带 3 组
    (0.0, 6.8, PINK), (-6.0, 5.5, PURPLE), (7.5, 4.0, ORANGE),
    (2.0, 13.0, CORAL), (12.0, 12.0, PINK), (18.5, 5.0, TEAL),
    # 卫星礁：主礁周围 + 东西/北侧水带（把颜色铺满可见水面）
    (-1.6, 7.3, CORAL), (-3.0, 4.5, BONE), (-7.5, 7.0, TEAL),
    (-1.5, 4.8, ORANGE), (5.5, 3.0, PURPLE), (9.0, 5.5, BONE),
    (4.0, 7.0, PINK), (2.0, 14.0, ORANGE), (14.0, 12.5, TEAL),
    (18.0, 9.0, PURPLE), (-9.0, -4.0, CORAL), (4.0, -8.0, BONE),
    (10.0, 8.0, PINK), (-10.0, -6.5, TEAL), (8.0, -6.5, ORANGE),
    (-2.0, -6.0, BONE),
]

#: 海星（x, y, 材质）——数量 8→14、四色、放大 1.6×（近景读得到五角轮廓）
STAR_SPOTS = [(5.5, -1.5, CORAL), (-8.5, 6.0, BONE), (14.8, 12.4, PINK),
              (17.9, -1.6, BONE), (-3.5, 12.8, ORANGE), (16.9, 13.0, BONE),
              (18.0, 5.4, CORAL), (10.5, 13.1, BONE), (13.0, 1.2, PINK),
              (16.8, -5.2, PURPLE), (7.2, 11.8, ORANGE), (1.8, -9.5, PINK),
              (11.8, -11.2, TEAL), (-10.5, 12.0, ORANGE)]

#: 前场碎石滩（+X / +Y 两侧透过水面读得到）
GRAVEL_PATCHES = [(17.6, 2.0), (17.4, -10.5), (6.0, 12.6), (-6.5, 12.5),
                  (17.6, 11.6), (12.2, 12.9), (3.2, 12.7)]

#: 气泡柱锚点（贴近 +X / +Y 两个前场位置，水面上读得到）
BUBBLE_COLUMNS = [(16.2, 5.2), (11.6, 12.0), (17.2, -6.8)]


# ---------------------------------------------------------------------------
# 高度场
# ---------------------------------------------------------------------------

def _rim_f(x, y, margin=1.2):
    """边缘下沉因子：距细节网格外缘 margin 内平滑收到 0（smoothstep）。"""
    ex = min((BX1 - x) / margin, (x - BX0) / margin, 1.0)
    ey = min((BY1 - y) / margin, (y - BY0) / margin, 1.0)
    f = max(0.0, ex) * max(0.0, ey)
    return f * f * (3.0 - 2.0 * f)


def _ridge_valley(x, y):
    """沙丘形态场：h(t) 软夹三档（脊 / 坡 / 洼）——脊顶平台化、洼底持平，坡面陡。

    2026-11-21 观感反馈「水底弄得再不平整一些」：落差 0.88 → 0.94 m、整体抬高
    0.2 m（水深 1.55-2.5 m），另加一项高频起伏（波长 ~2.4 m）——透水读得出丘壑。
    值域 [-2.95, -2.01]：脊 -2.01、洼 -2.95。Unity 侧 `CloudSeaWaterLab` 逐行同步。
    """
    t = 0.5 + 0.26 * math.sin(x * 0.17 + 0.4) * math.cos(y * 0.23 - 0.8) \
        + 0.16 * math.sin(x * 0.37 - y * 0.31 + 2.0) \
        + 0.10 * math.sin(x * 0.74 + y * 0.58 + 0.9) \
        + 0.07 * math.sin(x * 1.15 - y * 0.9 + 1.6) \
        + 0.05 * math.sin(x * 2.30 + y * 1.90 + 0.3)
    t = min(1.0, max(0.0, t))
    # 2026-11-22 浅海化：水面 −0.4，海床抬到 −1.15..−0.75（水深 0.35–0.75 m）。
    # 原作 σ 是按 0.1–0.6 m 浅滩标定的；2 m 水柱会把红通道吸没（r20 实拍深海军蓝）。
    # 要拿 Demo 的水色，必须让水变浅——不是改 shader。
    if t < 0.36:                                # 洼地（深色海床）
        u = t / 0.36
        return -1.15 + 0.08 * (u * u * (3.0 - 2.0 * u))
    if t < 0.62:                                # 陡坡
        u = (t - 0.36) / 0.26
        return -1.07 + 0.26 * (u * u * (3.0 - 2.0 * u))
    u = min(1.0, (t - 0.62) / 0.38)             # 沙脊（白沙，顶部平台化）
    return -0.81 + 0.06 * math.sin(u * math.pi / 2)


#: 沙纹振幅/波长（沙面细纹：3-4 m 波长的规则起伏，透水读成沙面纹路）
SAND_RIPPLE_AMP = 0.025


def bed_z(x, y):
    """海床高度：沙丘场 + 沙纹 + 岩簇丘包 + 珊瑚丘，细节网格四边 1.2m 平滑收到水底。

    珊瑚丘（2026-11-21 新增）：每处珊瑚锚点脚下抬 0.28 m、半径 0.85 m 的小丘包——
    珊瑚坐丘不插平沙，丘影/丘脊让水底读起来有高低。Unity 侧逐行同步。
    """
    h = _ridge_valley(x, y)
    h += SAND_RIPPLE_AMP * math.sin(x * 1.65 + 0.9 * math.sin(y * 0.35)) * math.cos(y * 1.35 + 0.4)
    for cx, cy, s in MOD_ROCK_CLUSTERS:
        d2 = ((x - cx) ** 2 + (y - cy) ** 2) / max(0.5, s * s * 1.6)
        h += 0.18 * math.exp(-d2)
    for cx, cy, _mat in CORAL_SPOTS:
        d2 = ((x - cx) ** 2 + (y - cy) ** 2) / (0.85 * 0.85)
        h += 0.16 * math.exp(-d2)
    return BOT + (h - BOT) * _rim_f(x, y)


def _surface_z(x, y):
    """水面起伏：三组小幅行波，细节网格四边 1.2m 内收到 -0.4（接外围平水面）。"""
    z = TOP
    z += 0.035 * math.sin(x * 0.55 + y * 0.35)
    z += 0.018 * math.sin(x * 1.4 - y * 1.1 + 1.0)
    z += 0.012 * math.sin(y * 2.2 + x * 0.8 + 2.6)
    return TOP + (z - TOP) * _rim_f(x, y, 1.2)


def _clamp_xy(x, y, pad=0.8):
    return (min(X1 - pad, max(X0 + pad, x)),
            min(Y1 - pad, max(Y0 + pad, y)))


def _above_floor(z, drop):
    """摆放高度夹紧：几何下沿（z - drop）不穿出水底底板 -3.2。"""
    return max(z, BOT + drop + 0.003)


# ---------------------------------------------------------------------------
# 朝向校正（出口统一过一遍；像素路径 Cull Back，法线朝向即可见性）
# ---------------------------------------------------------------------------

def _orient_faces(obj):
    """网格出口校正：只把轴对齐**侧壁**按 ±X/±Y 所在侧吸附朝外；水平面一律不动。

    【为什么不再按 c.z 判顶/底（2026-11-18 修）】旧实现写
    `want = +Z if center.z > 0 else -Z`——本件整个海都在 z<0，海床/水面/岩石顶面
    全被当成“底面”压成朝下；像素路径 Cull Back 一裁，海床整层消失、水下岩石与鱼
    顶面变黑块。水平面的朝向由建模绕序负责（海床/水面 +Z 朝上），这里只修侧壁。
    同位置出现 ± 成对法线的面是刻意双面薄片，跳过不校；斜面（坡/球）不动。
    """
    me = obj.data
    groups = {}
    for p in me.polygons:
        n, c = p.normal, p.center
        axis = max(range(3), key=lambda k: abs(n[k]))
        if abs(n[axis]) < 0.98 or axis == 2:
            continue                                    # 非轴对齐/水平面：不吸附
        key = (round(c.x, 3), round(c.y, 3), round(c.z, 3))
        groups.setdefault(key, set()).add((axis, 1 if n[axis] > 0 else -1))
    fixed = 0
    for p in me.polygons:
        n, c = p.normal, p.center
        axis = max(range(3), key=lambda k: abs(n[k]))
        if abs(n[axis]) < 0.98 or axis == 2:
            continue
        key = (round(c.x, 3), round(c.y, 3), round(c.z, 3))
        signs = {s for (a, s) in groups.get(key, ()) if a == axis}
        if len(signs) >= 2:
            continue                                    # ± 成对 = 双面薄片，保留
        want = 1.0 if c[axis] > 0.0 else -1.0
        if n[axis] * want < 0.0:
            p.flip()
            fixed += 1
    if fixed:
        me.update()
        print("[mod_sea] %s: 朝向校正 %d 面" % (obj.name, fixed), flush=True)
    return fixed


# ---------------------------------------------------------------------------
# 通用自然件：不规则石块（全三角面）
# ---------------------------------------------------------------------------

def _boulder(m, rng, center, sx, sy, sz, mat, seg=6, rings=3, jit=0.2, rz=None):
    """不规则低模石块：经纬球（层间错位）顶点按半径抖动，再按 sx/sy/sz 压扁。

    center = 几何中心；seg=6/rings=3 → 24 三角面，seg=5/rings=2 → 10 三角面（卵石）。
    """
    verts = [(0.0, 0.0, -1.0 + rng.uniform(0.0, jit))]
    starts = []
    for k in range(1, rings):
        lat = -math.pi / 2 + math.pi * k / rings
        starts.append(len(verts))
        for i in range(seg):
            a = TAU * i / seg + (k % 2) * math.pi / seg
            r = math.cos(lat) * (1.0 + rng.uniform(-jit, jit))
            z = math.sin(lat) * (1.0 + rng.uniform(-jit, jit))
            verts.append((r * math.cos(a), r * math.sin(a), z))
    top = len(verts)
    verts.append((0.0, 0.0, 1.0 - rng.uniform(0.0, jit)))
    faces = []
    s0 = starts[0]
    for i in range(seg):
        faces.append((0, s0 + (i + 1) % seg, s0 + i))
    for k in range(len(starts) - 1):
        a0, a1 = starts[k], starts[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            faces.append((a0 + i, a0 + j, a1 + i))
            faces.append((a0 + j, a1 + j, a1 + i))
    s1 = starts[-1]
    for i in range(seg):
        faces.append((top, s1 + i, s1 + (i + 1) % seg))
    m.add(verts, faces, at=center,
          rz=rng.uniform(0.0, TAU) if rz is None else rz,
          rx=rng.uniform(-0.15, 0.15), ry=rng.uniform(-0.15, 0.15),
          scale=(sx * 0.5, sy * 0.5, sz * 0.5), mat=mat)


def _pebble(m, rng, x, y, s, mats=(ROCK, BED)):
    """一颗卵石（10 三角面），半埋进海床。"""
    h = s * rng.uniform(0.45, 0.7)
    _boulder(m, rng, (x, y, _above_floor(bed_z(x, y) + h * 0.22, h * 0.62)), s,
             s * rng.uniform(0.65, 0.95), h, rng.choice(mats), seg=5, rings=2, jit=0.18)


# ---------------------------------------------------------------------------
# 海床 / 水面 / 外围海平面
# ---------------------------------------------------------------------------

def _flat_frame(m, z, mat, seg_x=16, seg_y=8):
    """细节网格外的一圈平水面/平底板：内框 (BX0..BX1, BY0..BY1) → 外框 ±OCEAN_HALF。

    四条带拼成回字形：南/北条横向铺满外框宽度，西/东条只覆盖内框纵向范围，
    角区由南/北条独占（不重叠）。内边界与中央细节网格同高（水面 TOP / 海床 BOT），
    外边界在远裁剪 + shader 距离雾之外；只铺大带，不追高模。
    """
    verts = []
    faces = []

    def strip(xs, ys):
        rows = []
        for y in ys:
            row = []
            for x in xs:
                verts.append((x, y, z))
                row.append(len(verts) - 1)
            rows.append(row)
        for j in range(len(rows) - 1):
            lo, hi = rows[j], rows[j + 1]
            for i in range(len(xs) - 1):
                faces.append((lo[i], lo[i + 1], hi[i + 1], hi[i]))

    ox0 = oy0 = -OCEAN_HALF
    ox1 = oy1 = OCEAN_HALF
    xs = [ox0 + (ox1 - ox0) * i / seg_x for i in range(seg_x + 1)]
    iys = [BY0 + (BY1 - BY0) * j / seg_y for j in range(seg_y + 1)]
    strip(xs, [oy0, BY0])                               # 南（含两个外角）
    strip(xs, [BY1, oy1])                               # 北（含两个外角）
    strip([ox0, BX0], iys)                              # 西
    strip([BX1, ox1], iys)                              # 东
    m.add(verts, faces, mat=mat, smooth=False)


def _build_bed(m):
    # 格距 1.6m：沙丘主频（5.5-8.5m 波长）仍有 3-5 个采样；外缘收边在画面外。
    nx, ny = 100, 100
    verts = []
    for j in range(ny + 1):
        y = BY0 + (BY1 - BY0) * j / ny
        for i in range(nx + 1):
            x = BX0 + (BX1 - BX0) * i / nx
            verts.append((x, y, bed_z(x, y)))
    vid = lambda i, j: j * (nx + 1) + i
    faces = []

    def _orient(tri):
        """顶面片统一 CCW（投影像素法线 +Z）——出口校正器的地基。"""
        a, b, c = verts[tri[0]], verts[tri[1]], verts[tri[2]]
        crz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        return tri if crz >= 0.0 else (tri[0], tri[2], tri[1])

    # 【海床只用一种沙色（2026-11-18）】旧实现按格高/坡把洼底涂成深色海床；透过
    # 0.6 alpha 的水面，这些色块读成一块块硬边多边形（像水底破洞/碎玻璃），比沙丘
    # 本身抢眼。现在整片海床统一 Cloud_Sand，起伏全靠高度场的平滑光照；深色只留给
    # 岩石/海草/鱼群等实物。
    for j in range(ny):
        for i in range(nx):
            a, b, c, d = vid(i, j), vid(i + 1, j), vid(i + 1, j + 1), vid(i, j + 1)
            diag = (i + j) % 2 == 0
            tris = ((a, b, c), (a, c, d)) if diag else ((a, b, d), (b, c, d))
            for tri in tris:
                faces.append(_orient(tri))
    m.add(verts, faces, mat=SAND, smooth=True)
    _flat_frame(m, BOT, DEEP_BED)                        # 细节网格外的深海底板


def _build_surface(m):
    nx, ny = 90, 90
    verts = []
    for j in range(ny + 1):
        y = BY0 + (BY1 - BY0) * j / ny
        for i in range(nx + 1):
            x = BX0 + (BX1 - BX0) * i / nx
            verts.append((x, y, _surface_z(x, y)))
    faces = []
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    m.add(verts, faces, mat=WATER, smooth=True)
    _flat_frame(m, TOP, WATER)                           # 细节网格外的平水面


# ---------------------------------------------------------------------------
# 海草床
# ---------------------------------------------------------------------------

def _frond(m, rng, x, y, base_z, top_z, lean, w0, mat, double_sided=True):
    """一片海草叶：5 截面收窄 + 顶梢；顺 lean 方向主弯 + 轻微 S 摆，叶面随高度微扭。

    double_sided=True 时叠一份反向绕序（像素路径 Cull Back，薄片两面都要可见）。9 或 18 三角面。
    """
    L = top_z - base_z
    amp = rng.uniform(0.10, 0.22) * L
    wig = rng.uniform(0.04, 0.09) * L
    ph = rng.uniform(0.0, TAU)
    pa0 = lean + math.pi / 2 + rng.uniform(-0.5, 0.5)
    twist = rng.uniform(-0.6, 0.6)
    cl, sl = math.cos(lean), math.sin(lean)
    verts = []
    for t, w in zip((0.0, 0.22, 0.44, 0.64, 0.84), (1.0, 0.92, 0.74, 0.48, 0.22)):
        off = amp * t * t + wig * math.sin(t * 5.0 + ph) * t
        sx, sy, sz = x + cl * off, y + sl * off, base_z + L * t
        pa = pa0 + twist * t
        hw = 0.5 * w0 * w
        dx, dy = math.cos(pa) * hw, math.sin(pa) * hw
        verts.append((sx - dx, sy - dy, sz))
        verts.append((sx + dx, sy + dy, sz))
    off = amp + wig * math.sin(5.0 + ph)
    verts.append((x + cl * off, y + sl * off, top_z))
    faces = [(2 * k, 2 * k + 1, 2 * k + 3, 2 * k + 2) for k in range(4)]
    faces.append((8, 9, 10))
    if double_sided:
        # 真双面：复制一份顶点块再叠反向绕序（validate 按"同顶点集合"剔面，
        # 复用同组下标的反面会被当退化面删掉——必须用独立顶点）
        base2 = len(verts)
        verts = verts + verts[:]
        faces += [tuple(base2 + i for i in f[::-1]) for f in faces]
    m.add(verts, faces, mat=mat)


def _plant(m, rng, x, y, current):
    """一株海草：4-6 片叶共根，**整株没入水下**（顶 ≤ 水面 −0.12）；根部 2-3 颗小石。

    2026-11-23 修正：旧式 `max(base + 0.7, …)` 会把草顶推到水面以上（基床 −0.8 时顶
    到 −0.1），带水成片里只剩露出水面的草尖——创始人「水草都只露个头」的实因。
    现在顶高同时受「株高 ≤0.55 m」与「水面帽 −0.52」夹住，0.25–0.55 m 全株在水下。
    """
    base = bed_z(x, y)
    top_max = min(base + 0.55, -0.52)
    main = WEED if rng.random() < 0.72 else PLANT
    alt = PLANT if main == WEED else WEED
    for _ in range(rng.randint(4, 6)):
        lean = current + rng.uniform(-0.9, 0.9)
        top = top_max if rng.random() < 0.4 else base + (top_max - base) * rng.uniform(0.55, 0.9)
        _frond(m, rng, x + rng.uniform(-0.06, 0.06), y + rng.uniform(-0.06, 0.06),
               base - 0.03, top, lean, rng.uniform(0.13, 0.20),
               main if rng.random() < 0.8 else alt)
    for _ in range(rng.randint(2, 3)):
        _pebble(m, rng, x + rng.uniform(-0.22, 0.22), y + rng.uniform(-0.22, 0.22),
                rng.uniform(0.10, 0.20))


def _kelp(m, rng, x, y):
    """海草树：9-12 片宽叶共根，冠高 0.3–0.62 m 且**整株没入水下**（顶 ≤ −0.52）。

    浅海化后水柱只有 0.35–0.75 m，旧式 1.2–1.8 m 冠高全被 `min(-0.62, …)` 压成
    0.18–0.3 m 的小丛；现在按水柱给足 0.3–0.62 m，宽叶 0.20–0.30 m，读成水下灌木。
    """
    base = bed_z(x, y)
    top_max = min(base + 0.62, -0.52)
    current = rng.choice(BED_CURRENT)
    main = WEED if rng.random() < 0.7 else PLANT
    for _ in range(rng.randint(9, 12)):
        lean = current + rng.uniform(-0.5, 0.5)
        top = top_max if rng.random() < 0.5 else base + (top_max - base) * rng.uniform(0.6, 0.92)
        _frond(m, rng, x + rng.uniform(-0.14, 0.14), y + rng.uniform(-0.14, 0.14),
               base - 0.03, top, lean, rng.uniform(0.20, 0.30),
               main if rng.random() < 0.8 else PLANT)
    for _ in range(3):
        _pebble(m, rng, x + rng.uniform(-0.4, 0.4), y + rng.uniform(-0.4, 0.4),
                rng.uniform(0.14, 0.26))


def _build_weeds(m, rng):
    for (bx, by, br), current in zip(MOD_SEAWEED_BEDS, BED_CURRENT):
        for _ in range(rng.randint(6, 10)):
            ang = rng.uniform(0.0, TAU)
            rr = br * 0.55 * math.sqrt(rng.uniform(0.05, 1.0))
            x, y = _clamp_xy(bx + math.cos(ang) * rr, by + math.sin(ang) * rr)
            _plant(m, rng, x, y, current)
    for kx, ky in KELP_SPOTS:
        _kelp(m, rng, kx, ky)


# ---------------------------------------------------------------------------
# 岩簇 / 岩拱 / 珊瑚 / 贝壳海星
# ---------------------------------------------------------------------------

def _rock_arch(m, rng, cx, cy, top):
    """岩拱/叠岩：两根石柱 + 横梁 + 柱侧叠石，顶不出水面。"""
    zb = bed_z(cx, cy)
    h = min(top, TOP - 0.3) - zb                     # 实际可站高度（留 0.3 出水余量）
    if h < 0.9:
        h = min(0.9, TOP - 0.3 - zb)
    w = h * rng.uniform(0.62, 0.8)                   # 跨度
    pw = 0.42                                        # 柱径
    lean = rng.uniform(0.0, TAU)
    dx, dy = math.cos(lean) * w / 2, math.sin(lean) * w / 2
    for sx in (-1.0, 1.0):                           # 石柱
        px, py = cx + dx * sx, cy + dy * sx
        ph = h - 0.16
        _boulder(m, rng, (px, py, _above_floor(zb + ph * 0.5, ph * 0.5)),
                 pw * rng.uniform(1.0, 1.25), pw * rng.uniform(0.85, 1.1), ph, ROCK)
    lintel = _above_floor(zb + h - 0.14, 0.24)
    m.box((w + pw * 1.3, pw * 0.9, 0.26), at=(cx, cy, lintel),
          rz=math.atan2(dy, dx), ry=0.06, mat=ROCK)  # 横梁
    _boulder(m, rng, (cx + dx * 1.5, cy + dy * 1.5,
                      _above_floor(bed_z(cx + dx * 1.5, cy + dy * 1.5) + 0.1, 0.2)),
             0.5, 0.42, 0.3, BED)                    # 柱侧叠石


def _build_rocks(m, rng):
    for cx, cy, s in MOD_ROCK_CLUSTERS:
        for k in range(rng.randint(4, 6)):
            if k == 0:                          # 主石
                w = s * rng.uniform(0.55, 0.65)
                x, y = cx, cy
            else:
                w = s * rng.uniform(0.22, 0.42)
                ang = rng.uniform(0.0, TAU)
                rr = s * rng.uniform(0.30, 0.60)
                x, y = cx + math.cos(ang) * rr, cy + math.sin(ang) * rr
            h = w * rng.uniform(0.55, 0.8) * 0.55
            h = min(h, max(0.10, -0.55 - bed_z(x, y)))   # 浅海：岩顶不出水
            _boulder(m, rng, (x, y, _above_floor(bed_z(x, y) + h * 0.22, h * 0.65)),
                     w * rng.uniform(0.95, 1.3) * 0.8, w * rng.uniform(0.7, 0.95) * 0.8, h,
                     ROCK if rng.random() < 0.7 else BED)
        for _ in range(rng.randint(4, 6)):      # 周边碎石
            ang = rng.uniform(0.0, TAU)
            rr = s * rng.uniform(0.6, 1.1)
            _pebble(m, rng, cx + math.cos(ang) * rr, cy + math.sin(ang) * rr,
                    rng.uniform(0.12, 0.24))
    for ax, ay, top in ARCH_SPOTS:
        _rock_arch(m, rng, ax, ay, top)


def _branch_coral(m, rng, cx, cy, mat, scale):
    """枝状珊瑚：弯茎 + 四根上翘侧枝 + 顶珠。"""
    zb = bed_z(cx, cy)
    stem_h = rng.uniform(0.5, 0.8) * scale
    lean = rng.uniform(0.0, TAU)
    cl, sl = math.cos(lean), math.sin(lean)
    mid = (cx + cl * 0.05, cy + sl * 0.05, zb + stem_h * 0.5)
    top = (cx + cl * 0.14, cy + sl * 0.14, zb + stem_h)
    m.tube([(cx, cy, max(BOT + 0.005, zb - 0.05)), mid, top], 0.07 * scale, seg=5,
           r_end=0.025 * scale, mat=mat)
    tips = [top]
    for k in range(4):
        ang = lean + TAU * k / 4.0 + rng.uniform(-0.35, 0.35)
        f = 0.35 if k % 2 == 0 else 0.62
        p0 = (cx + cl * 0.05 * f * 2, cy + sl * 0.05 * f * 2, zb + stem_h * f)
        reach = rng.uniform(0.22, 0.36) * scale
        p1 = (p0[0] + math.cos(ang) * reach * 0.7, p0[1] + math.sin(ang) * reach * 0.7,
              p0[2] + reach * 0.35)
        p2 = (p0[0] + math.cos(ang) * reach, p0[1] + math.sin(ang) * reach,
              p0[2] + reach * rng.uniform(0.9, 1.3))
        m.tube([p0, p1, p2], 0.04 * scale, seg=5, r_end=0.014 * scale, mat=mat)
        tips.append(p2)
    for p in tips:
        m.sphere(rng.uniform(0.035, 0.06) * scale, at=p, seg=5, rings=2, mat=mat)


def _mound_coral(m, rng, cx, cy, mat, r):
    """团块珊瑚：压扁的不规则半球团。"""
    _boulder(m, rng, (cx, cy, _above_floor(bed_z(cx, cy) + r * 0.25, r * 0.68)),
             2.0 * r, 1.8 * r, 1.2 * r, mat, seg=7, rings=3, jit=0.12)


def _shell(m, rng, x, y):
    r = rng.uniform(0.05, 0.09)
    tilt = rng.uniform(0.5, 1.1)                # 斜躺：下沿比摆放点低 r·sin(tilt)
    m.cyl(r, rng.uniform(0.05, 0.09),
          at=(x, y, _above_floor(bed_z(x, y) + 0.005, r * math.sin(tilt + 0.3))),
          seg=6, r_top=0.008, mat=rng.choice((BONE, BONE, CORAL)),
          rz=rng.uniform(0.0, TAU), rx=tilt, ry=rng.uniform(-0.3, 0.3))


def _starfish(m, rng, x, y, mat):
    z = bed_z(x, y) + 0.02
    R = rng.uniform(0.10, 0.14)                 # 2026-11-21 放大 1.6×：五角轮廓近景可读
    a0 = rng.uniform(0.0, TAU)
    pts = []
    for k in range(10):
        ang = a0 + TAU * k / 10.0
        rr = R if k % 2 == 0 else R * 0.42
        pts.append((x + math.cos(ang) * rr, y + math.sin(ang) * rr))
    m.poly_extrude(pts, z, z + 0.055, mat=mat)


def _fan_coral(m, rng, cx, cy, mat, scale):
    """扇珊瑚：竖直薄扇面（双面），放射状波浪扇缘 + 短根，朝向随机。"""
    zb = bed_z(cx, cy)
    facing = rng.uniform(0.0, TAU)
    R = rng.uniform(0.42, 0.62) * scale
    h0 = rng.uniform(0.10, 0.20) * scale
    n = 7
    verts = [(0.0, 0.0, 0.0)]
    for k in range(n + 1):
        t = -1.15 + 2.30 * k / n
        rr = R * rng.uniform(0.88, 1.08)         # 扇缘不规则（波浪缺口）
        verts.append((math.sin(t) * rr, 0.0, h0 + math.cos(t) * rr))
    faces = [(0, k, k + 1) for k in range(1, n + 1)]
    at = (cx, cy, zb + 0.02)
    m.add(verts, faces, at=at, rz=facing, mat=mat)
    m.add(verts, [f[::-1] for f in faces], at=at, rz=facing, mat=mat)   # 真双面


def _tube_coral(m, rng, cx, cy, mat, scale):
    """管珊瑚：一簇竖管（4-7 根），管径/高度各异，顶口微张。"""
    zb = bed_z(cx, cy)
    for _ in range(rng.randint(4, 7)):
        ang = rng.uniform(0.0, TAU)
        rr = rng.uniform(0.0, 0.22) * scale
        x, y = cx + math.cos(ang) * rr, cy + math.sin(ang) * rr
        h = rng.uniform(0.28, 0.55) * scale
        r0 = rng.uniform(0.045, 0.07) * scale
        m.cyl(r0, h, at=(x, y, zb - 0.02), seg=6,
              r_top=r0 * rng.uniform(0.90, 1.18), mat=mat)


def _brain_coral(m, rng, cx, cy, mat, r):
    """脑纹珊瑚：低伏半球团（大抖动 → 表面沟回感），比团块珊瑚更扁更宽。"""
    _boulder(m, rng, (cx, cy, _above_floor(bed_z(cx, cy) + r * 0.18, r * 0.55)),
             1.95 * r, 1.80 * r, 1.00 * r, mat, seg=9, rings=4, jit=0.16)


def _table_coral(m, rng, cx, cy, mat, scale):
    """桌珊瑚：短柄 + 宽扁波浪盘（俯视读成大圆盘，礁区主景）。"""
    zb = bed_z(cx, cy)
    r = rng.uniform(0.26, 0.38) * scale
    h = rng.uniform(0.10, 0.15) * scale
    m.cyl(0.05 * scale, h * 0.9, at=(cx, cy, zb - 0.02), seg=6,
          r_top=0.055 * scale, mat=mat)
    n = 12
    top = zb + h
    ring = []
    for k in range(n):
        a = TAU * k / n
        rr = r * (1.0 + 0.12 * math.sin(a * 3.0 + rng.uniform(-0.25, 0.25)))
        ring.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr,
                     top + 0.02 * rng.uniform(-1.0, 1.0)))
    center = (cx, cy, top + 0.035)
    verts = [center] + ring + [(x, y, z - 0.035) for x, y, z in ring]
    faces = [(0, 1 + k, 1 + (k + 1) % n) for k in range(n)]
    b2 = 1 + n
    faces += [(0, b2 + (k + 1) % n, b2 + k) for k in range(n)]
    faces += [(1 + k, b2 + k, b2 + (k + 1) % n, 1 + (k + 1) % n) for k in range(n)]
    # 薄盘双面：必须用**独立顶点块**叠反向面——复用同一顶点集的反向面会被
    # Mesher.to_object 的“完全重合面”过滤当重复删掉（2026-11-22 桌珊瑚隐身的真因）。
    m.add(verts, faces, mat=mat, smooth=False)
    base = len(verts)
    m.add(verts + verts,
          [tuple(base + i for i in f[::-1]) for f in faces],
          mat=mat, smooth=False)


def _anemone(m, rng, cx, cy, mat, scale):
    """海葵：低伏基座 + 一圈上翘触手（像素尺度读作彩色绒球）。"""
    zb = bed_z(cx, cy)
    r = rng.uniform(0.09, 0.14) * scale
    m.sphere(r * 0.8, at=(cx, cy, zb + r * 0.35), seg=6, rings=3, mat=mat)
    n = rng.randint(10, 14)
    for k in range(n):
        a = TAU * k / n + rng.uniform(-0.15, 0.15)
        rr = r * rng.uniform(0.45, 0.85)
        hh = r * rng.uniform(1.4, 2.2)
        p0 = (cx + math.cos(a) * rr, cy + math.sin(a) * rr, zb + r * 0.5)
        p1 = (cx + math.cos(a) * (rr + r * 0.35), cy + math.sin(a) * (rr + r * 0.35),
              zb + r * 0.5 + hh)
        m.tube([p0, p1], r * 0.10, seg=4, r_end=r * 0.04, mat=mat)


#: 主礁锚点（中部水湾 + 前景水带；每处是一组高密度珊瑚花园，默认镜头就能读到）
HERO_REEFS = [(0.0, 6.8), (-6.0, 5.5), (7.5, 4.0),
              (2.0, 13.0), (12.0, 12.0), (18.5, 5.0)]
#: 卫星礁（主礁周围 + 东西/北侧水带，1-2 株点缀，避免满场碎点）
SAT_REEFS = [(-1.6, 7.3), (-3.0, 4.5), (-7.5, 7.0), (-1.5, 4.8),
             (5.5, 3.0), (9.0, 5.5), (4.0, 7.0), (2.0, 14.0),
             (14.0, 12.5), (18.0, 9.0), (-9.0, -4.0), (4.0, -8.0), (10.0, 8.0),
             (-10.0, -6.5), (8.0, -6.5), (-2.0, -6.0)]


def _reef_scale(cx, cy, scale, height_per_scale):
    """按当地水深夹珊瑚尺度：珊瑚顶离水面 ≥0.05 m（顶点不破水，波峰也不穿帮）。"""
    base = bed_z(cx, cy)
    max_h = max(0.10, -0.45 - base)
    return min(scale, max_h / max(0.2, height_per_scale))


def _build_hero_reef(m, rng, cx, cy):
    """一组设计过的主礁：桌 + 扇 + 枝 + 管 + 脑 + 海葵 + 碎石 + 海星。"""
    warm = rng.choice((PINK, ORANGE, CORAL))
    cool = rng.choice((PURPLE, TEAL, PINK))
    ox = lambda: cx + rng.uniform(-0.55, 0.55)
    oy = lambda: cy + rng.uniform(-0.55, 0.55)

    # 大剪影优先：像素档下先读出 ~1.2 m 桌 / 0.6 m 扇 / 高枝丛，再谈细节
    _table_coral(m, rng, ox(), oy(), warm, _reef_scale(cx, cy, 2.8, 0.20))
    _fan_coral(m, rng, ox(), oy(), cool, _reef_scale(cx, cy, 2.2, 0.60))
    for _ in range(3):
        _branch_coral(m, rng, ox(), oy(), rng.choice((TEAL, CORAL, ORANGE)),
                      _reef_scale(cx, cy, 1.8, 0.70))
    for _ in range(2):
        _tube_coral(m, rng, ox(), oy(), rng.choice((ORANGE, PINK, TEAL)),
                    _reef_scale(cx, cy, 2.0, 0.55))
    _brain_coral(m, rng, ox(), oy(), rng.choice((BONE, CORAL)),
                 _reef_scale(cx, cy, 0.50, 1.2))
    _anemone(m, rng, ox(), oy(), rng.choice((PURPLE, PINK, TEAL)), 2.2)
    for _ in range(5):
        _pebble(m, rng, ox(), oy(), rng.uniform(0.12, 0.24))
    for _ in range(5):
        _starfish(m, rng, ox(), oy(), rng.choice((PINK, ORANGE, TEAL, BONE)))


def _build_coral(m, rng):
    for cx, cy in HERO_REEFS:
        _build_hero_reef(m, rng, cx, cy)
    for cx, cy in SAT_REEFS:
        mat = rng.choice((PINK, PURPLE, ORANGE, TEAL, CORAL, BONE))
        _branch_coral(m, rng, cx, cy, mat, _reef_scale(cx, cy, 1.3, 0.70))
        if rng.random() < 0.6:
            _fan_coral(m, rng, cx + rng.uniform(-0.4, 0.4), cy + rng.uniform(-0.4, 0.4),
                       rng.choice((PINK, PURPLE, TEAL)),
                       _reef_scale(cx, cy, 1.2, 0.60))
    for bx, by, br in MOD_SEAWEED_BEDS:         # 草床边贝壳
        for _ in range(2):
            ang = rng.uniform(0.0, TAU)
            rr = br * rng.uniform(0.2, 0.9)
            x, y = _clamp_xy(bx + math.cos(ang) * rr, by + math.sin(ang) * rr)
            _shell(m, rng, x, y)

# ---------------------------------------------------------------------------
# 水底细节（小石块 / 剖面碎石滩 / 贝壳 / 鱼骨）
# ---------------------------------------------------------------------------

def _fish_bones(m, rng, x, y):
    zb = bed_z(x, y) + 0.02
    heading = rng.uniform(0.0, TAU)
    L = rng.uniform(0.5, 0.7)
    ax, ay = x - math.cos(heading) * L * 0.5, y - math.sin(heading) * L * 0.5
    bx, by = x + math.cos(heading) * L * 0.5, y + math.sin(heading) * L * 0.5
    m.member((ax, ay, zb), (bx, by, zb), 0.035, 0.02, mat=BONE)     # 脊柱
    for k in range(4):                                              # 鱼肋
        t = 0.18 + 0.6 * k / 3.0
        px, py = ax + (bx - ax) * t, ay + (by - ay) * t
        side = 1 if k % 2 == 0 else -1
        ang = heading + side * rng.uniform(1.2, 1.6)
        m.member((px, py, zb),
                 (px + math.cos(ang) * 0.14, py + math.sin(ang) * 0.14, zb + 0.015),
                 0.016, 0.01, mat=BONE)


def _build_debris(m, rng):
    for _ in range(22):                         # 散布小石块
        x = rng.uniform(X0 + 1.4, X1 - 1.4)
        y = rng.uniform(Y0 + 1.4, Y1 - 1.4)
        _pebble(m, rng, x, y, rng.uniform(0.12, 0.26), (ROCK, BED, SAND))
    for gx, gy in GRAVEL_PATCHES:               # 剖面内侧碎石滩
        for _ in range(rng.randint(6, 9)):
            x, y = _clamp_xy(gx + rng.uniform(-1.1, 1.1), gy + rng.uniform(-0.8, 0.8), 0.3)
            _pebble(m, rng, x, y, rng.uniform(0.10, 0.24))
    for _ in range(14):                         # 沉底贝壳（偏前场）
        side = rng.random()
        if side < 0.45:
            x, y = rng.uniform(14.0, 18.0), rng.uniform(-13.0, 13.0)   # +X 侧前场
        elif side < 0.8:
            x, y = rng.uniform(-16.0, 16.0), rng.uniform(10.6, 13.2)   # +Y 侧前场
        else:
            x, y = rng.uniform(X0 + 1.5, X1 - 1.5), rng.uniform(Y0 + 1.5, Y1 - 1.5)
        _shell(m, rng, x, y)
    for _ in range(4):                          # 鱼骨
        _fish_bones(m, rng, rng.uniform(X0 + 2.0, X1 - 2.0), rng.uniform(Y0 + 2.0, Y1 - 2.0))


# ---------------------------------------------------------------------------
# 鱼群（独立对象，原点 = 群中心）
# ---------------------------------------------------------------------------

def _fish(m, fx, fy, fz, heading, pitch, s, body_mat, fin_mat):
    """一条小鱼：6 顶点椭球身（8 面）+ 双面尾鳍/背鳍/腹鳍（8 面）= 16 三角面。朝向本地 +X。"""
    BL, BH, BW = 0.34 * s, 0.125 * s, 0.062 * s
    body_v = [(0.50 * BL, 0.0, 0.02 * BH),      # 吻
              (0.06 * BL, 0.0, 0.50 * BH),      # 背
              (0.06 * BL, 0.0, -0.44 * BH),     # 腹
              (0.10 * BL, -0.5 * BW, -0.04 * BH),
              (0.10 * BL, 0.5 * BW, -0.04 * BH),
              (-0.44 * BL, 0.0, 0.0)]           # 尾根
    body_f = [(0, 1, 3), (0, 3, 2), (0, 2, 4), (0, 4, 1),
              (5, 1, 4), (5, 4, 2), (5, 2, 3), (5, 3, 1)]
    m.add(body_v, body_f, at=(fx, fy, fz), rz=heading, ry=pitch, mat=body_mat)
    fin_v = [(-0.36 * BL, 0.0, 0.0), (-0.62 * BL, 0.0, 0.34 * BH),   # 尾鳍菱形
             (-0.48 * BL, 0.0, 0.0), (-0.62 * BL, 0.0, -0.30 * BH),
             (0.16 * BL, 0.0, 0.42 * BH), (-0.12 * BL, 0.0, 0.30 * BH),  # 背鳍
             (-0.02 * BL, 0.0, 0.60 * BH),
             (0.04 * BL, 0.0, -0.42 * BH), (-0.10 * BL, 0.0, -0.30 * BH),  # 腹鳍
             (-0.02 * BL, 0.0, -0.52 * BH)]
    fin_f = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (7, 8, 9)]
    fin_v = fin_v + fin_v[:]                    # 真双面：独立顶点块 + 反向绕序
    fin_f += [tuple(len(fin_v) // 2 + i for i in f[::-1]) for f in fin_f[:4]]
    m.add(fin_v, fin_f, at=(fx, fy, fz), rz=heading, ry=pitch, mat=fin_mat)


def _build_school(idx, rng):
    cx, cy, cz, spread = MOD_FISH_SCHOOLS[idx]
    m = K.Mesher()
    base_h = SCHOOL_HEAD[idx]
    dom = SCHOOL_DOM[idx]
    tight = SCHOOL_TIGHT[idx]                   # 0 紧=梭形纵列 → 1 松=云团
    z_lo, z_hi = -1.05 - cz, -0.70 - cz          # 个体中心相对群中心的 z 行程（夹进 -2.4..-0.85）
    placed = []
    s_sum = 0.0
    for _ in range(rng.randint(12, 16)):
        s = rng.uniform(0.85, 1.25) * 1.6       # 鱼体长 0.46–0.68 m 体干（含尾 ~0.8–1.1 m）
        s_sum += s
        gap2 = (0.32 * s) ** 2                  # 同校鱼体间距 ≥ 0.32·身长（不叠鱼）
        for _try in range(18):                  # 拒采样
            u = rng.uniform(-1.0, 1.0) * spread * (0.75 + 0.45 * tight)   # 沿游向
            v = rng.gauss(0.0, 0.24 * spread * tight)                     # 横向散布
            fx = math.cos(base_h) * u - math.sin(base_h) * v
            fy = math.sin(base_h) * u + math.cos(base_h) * v
            fz = rng.uniform(max(z_lo, -0.4), min(z_hi, 0.4))
            # 海床净空必须放在带高夹**之后**：先 min(z_hi) 会把珊瑚丘顶的鱼压回沙里
            #（2026-11-22 实拍：主礁群半埋、可见鱼离床只有 0.1 m，水体保护判不到）。
            # 净空 0.22 m（鱼体下沿 ≈0.14 m 离床）；再夹水面帽，鱼背不出水（水面 −0.4）。
            fz = max(fz, bed_z(cx + fx, cy + fy) + 0.22 - cz)
            fz = min(fz, -0.58 - cz)
            if all((fx - px) ** 2 + (fy - py) ** 2 + (fz - pz) ** 2 > gap2
                   for px, py, pz in placed):
                break
        placed.append((fx, fy, fz))
        heading = base_h + rng.gauss(0.0, 0.32)
        pitch = max(-0.25, min(0.25, rng.gauss(0.0, 0.10)))
        if rng.random() < 0.2:                  # 少量杂色个体
            body = rng.choice([c for c in (BONE, FISH_YELLOW, FISH_ORANGE) if c != dom])
        else:
            body = dom
        _fish(m, fx, fy, fz, heading, pitch, s,
              body, FISH_ORANGE if body in (BONE, FISH_YELLOW) else body)
    # 鱼身是闭合椭球、鳍是双面薄片：整体带边界，recalc 可能把鱼身翻成内朝外
    # （实拍 6/7 号群体积为负 = 鱼身被翻）。绕序建模时已按外朝写，禁 recalc。
    obj = m.to_object("Cloud_FishSchool_%d" % idx, recalc_normals=False)
    obj.location = (cx, cy, cz)
    print("[mod_sea] FishSchool_%d: %d 条 均体长 %.2fm 松紧 %.2f"
          % (idx, len(placed), 0.34 * s_sum / len(placed), tight), flush=True)
    return obj


# ---------------------------------------------------------------------------
# 气泡柱（独立对象，原点 = 柱底）
# ---------------------------------------------------------------------------

def _build_bubbles(idx, rng, bx, by):
    base = bed_z(bx, by)
    m = K.Mesher()
    n = rng.randint(8, 10)
    z_top = TOP - 0.3                           # 顶泡中心 -0.7：贴近水面、不出水
    for i in range(n):
        t = i / float(n - 1)
        z = base + 0.18 + (z_top - base - 0.18) * t + rng.gauss(0.0, 0.04)
        r = 0.06 + 0.06 * t + rng.uniform(0.0, 0.02)
        x = 0.12 * math.sin(t * 7.3 + idx * 2.1) + rng.gauss(0.0, 0.02)
        y = 0.09 * math.sin(t * 5.1 + idx * 1.3) + rng.gauss(0.0, 0.02)
        m.sphere(r, at=(x, y, z - base), seg=6, rings=2, mat=BUBBLE)
    obj = m.to_object("Cloud_Bubbles_%d" % idx)
    obj.location = (bx, by, base)
    return obj


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def build():
    rng = random.Random(SEED)
    objects = []

    # 【开放薄面禁 recalc（2026-11-18）】海床/水面/海草是单面高度场/双面薄片，
    # recalc_face_normals 会把整张朝上的面翻成朝下（Cull Back 后海床整层消失）。
    # 绕序在建模里已按 +Z 朝上写好，出口只做侧壁校正。
    bed = K.Mesher()
    _build_bed(bed)
    objects.append(bed.to_object("Cloud_Sea_Bed", recalc_normals=False))

    surf = K.Mesher()
    _build_surface(surf)
    objects.append(surf.to_object("Cloud_Sea_Surface", recalc_normals=False))

    weeds = K.Mesher()
    _build_weeds(weeds, rng)
    objects.append(weeds.to_object("Cloud_Sea_Weeds", recalc_normals=False))

    rocks = K.Mesher()
    _build_rocks(rocks, rng)
    objects.append(rocks.to_object("Cloud_Sea_Rocks"))

    flora = K.Mesher()
    _build_coral(flora, rng)
    objects.append(flora.to_object("Cloud_Sea_Coral"))

    debris = K.Mesher()
    _build_debris(debris, rng)
    objects.append(debris.to_object("Cloud_Sea_Debris"))

    for i in range(len(MOD_FISH_SCHOOLS)):
        objects.append(_build_school(i, rng))

    for i, (bx, by) in enumerate(BUBBLE_COLUMNS):
        objects.append(_build_bubbles(i, rng, bx, by))

    for obj in objects:                         # 出口统一朝向校正（Cull Back 可见性）
        _orient_faces(obj)

    return objects
