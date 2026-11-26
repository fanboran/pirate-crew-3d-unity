# -*- coding: utf-8 -*-
"""cloudsea/creature_kit.py —— 水下生物建模库：低模图元 + 骨架与蒙皮权重。

【骨架】每只生物（Creature）登记一组关节：角色名 + 静止位置（Blender 物体空间，x 前、y 侧、z 上）
+ 父关节。全部生物建完后 bind_joints 统一编号，每个关节落成一个空物体 J<编号>_<角色>（挂在生物
对象下、位置 = 静止位置；父关节号写在名字里 J<号>_P<父号>_<角色>），随 FBX 进 Unity。运行时
（CloudSeaRig）从这里读静止位置与父子关系，
运动学控制器（CloudSeaBrains / CloudSeaCrawlers）按角色名找到关节去解——角色名是两边共用的契约。

【蒙皮数据在顶点上】水下材质按颜色共享（橙色既是小丑鱼也是海星），蒙皮数据不能挂在材质上。
每个顶点用三套 UV 携带，Unity 侧 CloudSeaUnderwater 的顶点着色器按同一契约自己蒙皮：
    uv0 = (关节号 A, 权重 A)
    uv1 = (关节号 B, 权重 B)      没有第二个影响时权重 0
    uv2 = (1, 个体种子)            1 = 「这个顶点要蒙皮」的标记；种子 0..1，同群的鱼各不相同
关节号在 bind_joints 之前是生物内的本地序号，之后换成全场景的全局号。

【链】一条链 = 一串节点关节；第 k 节骨骼从节点 k 指向节点 k+1，绕节点 k 转。
    · 放样身体（鱼身、魔鬼鱼翼面）：顶点沿链的参数连续，相邻两节骨骼线性插值 → 平滑弯曲（along_chain）；
    · 管（腿、触手、腕）：按截面站给，中间的站对前后两节各 0.5（tube 的 joints 参数）；
    · 整件刚性的小件（眼、鳍、刺）：rigid。

【面数与法线】身体一律平滑着色（两档卡通色的明暗线才是流畅曲线，平面着色会让色带逐面闪烁）；
鳍这类薄片平直着色。条纹 / 腹面靠逐面换槽来画，顶点在各槽之间共用——按槽拆开复制顶点会让
平滑法线在条纹边上断开。每个图元都是闭合体，收工时统一重算法线朝外。
"""

import math

from mathutils import Matrix, Vector
from mathutils.geometry import tessellate_polygon

import kit_common as K

TAU = math.tau

# 对象名 → 建它的 Creature（Blender 的对象挂不了自定义属性，骨架数据走这张表；bind_joints 来取）。
_CREATURES = {}

# 材质槽（style_tokens 的 Cloud_* 生物色卡；名字前缀 Cloud_Fin / Cloud_Eye / Cloud_Ink 在
# Unity 侧烘焙器里按纯平件处理）。
EYE = "Cloud_Eye"
INK = "Cloud_Ink"
BAND = "Cloud_Band"          # 白色花纹 / 浅色腹面


# ---------------------------------------------------------------------------
# 插值
# ---------------------------------------------------------------------------

def curve(keys, t):
    """非均匀节点的 Catmull-Rom 标量插值。keys = [(t, 值), ...] 升序；t 夹在两端之内。"""
    n = len(keys)
    if n == 1 or t <= keys[0][0]:
        return keys[0][1]
    if t >= keys[-1][0]:
        return keys[-1][1]
    i = 0
    while i < n - 2 and t > keys[i + 1][0]:
        i += 1
    t0, v0 = keys[i]
    t1, v1 = keys[i + 1]
    tm0, vm0 = keys[i - 1] if i > 0 else keys[i]
    tm2, vm2 = keys[i + 2] if i + 2 < n else keys[i + 1]
    dt = (t1 - t0) or 1e-9
    m0 = (v1 - vm0) / ((t1 - tm0) or 1e-9)
    m1 = (vm2 - v0) / ((tm2 - t0) or 1e-9)
    u = (t - t0) / dt
    u2, u3 = u * u, u * u * u
    return ((2.0 * u3 - 3.0 * u2 + 1.0) * v0 + (u3 - 2.0 * u2 + u) * dt * m0
            + (-2.0 * u3 + 3.0 * u2) * v1 + (u3 - u2) * dt * m1)


def shape(keys, ts):
    """keys = [(t, 半高, 半宽), ...] → 在 ts 上插值出的 [(t, 半高, 半宽)]。"""
    hk = [(k[0], k[1]) for k in keys]
    wk = [(k[0], k[2]) for k in keys]
    return [(t, max(curve(hk, t), 0.0), max(curve(wk, t), 0.0)) for t in ts]


def smooth_samples(count, lo=0.0, hi=1.0):
    """两端加密的采样点（余弦间距）：头尾曲率大，中间平直。"""
    return [lo + (hi - lo) * (1.0 - math.cos(math.pi * i / (count - 1))) * 0.5 for i in range(count)]


def merge_sorted(anchors, others, keep_out=0.0):
    """合并两组升序采样点：anchors 全保留（条纹边界、极点），others 里离任何已保留点不足
    keep_out 的丢弃（同一条纹带里不留多余的环）。返回升序列表。"""
    out = list(anchors)
    for v in others:
        if all(abs(v - a) >= keep_out for a in out):
            out.append(v)
    return sorted(out)


def clamp01(v):
    return min(1.0, max(0.0, v))


def smoothstep(a, b, v):
    u = clamp01((v - a) / (b - a)) if b > a else (1.0 if v >= b else 0.0)
    return u * u * (3.0 - 2.0 * u)


# ---------------------------------------------------------------------------
# 蒙皮：每个顶点 = (关节 A, 权重 A, 关节 B, 权重 B)
# ---------------------------------------------------------------------------

def rigid(index):
    """整件刚性绑在一个关节上。"""
    return lambda v: (index, 1.0, index, 0.0)


def along_chain(indexes, t_of):
    """沿链的连续参数 t_of(v) ∈ [0, 节点数 - 1]：落在第 k 到 k + 1 个节点之间时，对第 k、k + 1 节
    骨骼线性插值（平滑弯曲）。超过最后一个节点算在最后一节骨骼上。"""
    n = len(indexes)

    def skin(v):
        t = min(max(float(t_of(v)), 0.0), float(n - 1))
        k = min(int(math.floor(t)), n - 2)
        frac = 0.0 if k >= n - 2 else clamp01(t - k)
        return (indexes[k], 1.0 - frac, indexes[k + 1], frac)
    return skin


def by_height(levels, z_of):
    """按高度在相邻两个关节之间插值。levels = [(高度, 关节), ...] 升序；低于第一档绑第一档、
    高于最后一档绑最后一档（水母铃：钟口 / 中段 / 钟顶三层关节按高度插值）。"""
    def skin(v):
        z = z_of(v)
        if z <= levels[0][0]:
            return (levels[0][1], 1.0, levels[0][1], 0.0)
        for (z0, a), (z1, b) in zip(levels, levels[1:]):
            if z <= z1:
                f = (z - z0) / (z1 - z0)
                return (a, 1.0 - f, b, f)
        return (levels[-1][1], 1.0, levels[-1][1], 0.0)
    return skin


def station_skins(indexes):
    """管的一圈顶点按所在站绑（管 = 每个节点处一圈）：首站 = 第 0 节骨骼，末站 = 最后一节，
    中间的站对前后两节各 0.5。返回与站点等长的蒙皮表。"""
    n = len(indexes)
    out = []
    for j in range(n):
        if j == 0:
            out.append((indexes[0], 1.0, indexes[0], 0.0))
        elif j == n - 1:
            out.append((indexes[n - 2], 1.0, indexes[n - 2], 0.0))
        else:
            out.append((indexes[j - 1], 0.5, indexes[j], 0.5))
    return out


def _skin4(value):
    return (int(value[0]), float(value[1]), int(value[2]), float(value[3]))


# ---------------------------------------------------------------------------
# 累加器
# ---------------------------------------------------------------------------

class Creature(K.Mesher):
    """一只生物的建模累加器：图元直接写在物体空间，关节登记在骨架表里，顶点各自带蒙皮权重。

    seed = 个体种子（0..1），写进每个顶点的第三套 UV；鱼群里的个体在 append 时各换各的。
    """

    def __init__(self, seed=0.0):
        K.Mesher.__init__(self)
        self.seed = float(seed)
        self.joint_roles = []       # 关节角色名（登记顺序 = 本地序号）
        self.joint_pos = []         # 关节静止位置
        self.joint_parent = []      # 父关节的本地序号；-1 = 没有（由求解器直接驱动）
        self._joint_index = {}
        self.skin = []              # 与 verts 等长：(关节 A, 权重 A, 关节 B, 权重 B)
        self.vert_seed = []         # 与 verts 等长：个体种子

    def joint(self, role, position, parent=None):
        """登记一个关节。parent = 父关节的角色名或本地序号，必须在它之前登记；None = 没有父关节。"""
        if role in self._joint_index:
            raise ValueError("关节重复登记：" + role)
        index = len(self.joint_roles)
        parent_index = -1
        if parent is not None:
            parent_index = self._joint_index[parent] if isinstance(parent, str) else int(parent)
            if not 0 <= parent_index < index:
                raise ValueError("关节 %s 的父关节必须先登记" % role)
        self.joint_roles.append(role)
        self.joint_pos.append(tuple(float(c) for c in position))
        self.joint_parent.append(parent_index)
        self._joint_index[role] = index
        return index

    def chain(self, prefix, points):
        """登记一条由求解器驱动的链：节点 prefix0、prefix1…，每个节点都是根一级的关节。
        返回节点序号列表（第 k 节骨骼 = 节点 k → 节点 k + 1）。"""
        return [self.joint(prefix + str(k), p) for k, p in enumerate(points)]

    def add(self, verts, faces, mat, smooth=True, skin=None):
        """并入一段顶点 / 面。mat 是槽名，或与 faces 等长的槽名表（逐面换色，顶点共用）。
        skin = 顶点坐标 → (关节 A, 权重 A, 关节 B, 权重 B) 的函数、一个固定的四元组，
        或与 verts 等长的四元组表。每个顶点都必须绑到关节上。"""
        n = len(verts)
        if skin is None:
            raise ValueError("图元没有蒙皮：每个顶点都要绑到关节上")
        if callable(skin):
            skins = [_skin4(skin(v)) for v in verts]
        elif len(skin) == 4 and not hasattr(skin[0], "__len__"):
            skins = [_skin4(skin)] * n
        else:
            skins = [_skin4(s) for s in skin]
            if len(skins) != n:
                raise ValueError("蒙皮表长度 %d ≠ 顶点数 %d" % (len(skins), n))
        count = len(self.joint_roles)
        for (a, wa, b, wb) in skins:
            if not (0 <= a < count and 0 <= b < count):
                raise ValueError("蒙皮引用了没登记的关节：%r" % ((a, wa, b, wb),))
            if abs(wa + wb - 1.0) > 1e-3:
                raise ValueError("蒙皮权重和不为 1：%r" % ((a, wa, b, wb),))

        slots = [mat] * len(faces) if isinstance(mat, str) else list(mat)
        if len(slots) != len(faces):
            raise ValueError("逐面槽名表长度 %d ≠ 面数 %d" % (len(slots), len(faces)))
        base = len(self.verts)
        for slot in slots:
            self._slot(slot)
        self.verts += [tuple(float(c) for c in v) for v in verts]
        self.vert_aux += [0.0] * n
        for f in faces:
            self.faces.append(tuple(base + i for i in f))
        self.face_mat += slots
        self.face_smooth += [bool(smooth)] * len(faces)
        self.skin += skins
        self.vert_seed += [self.seed] * n
        return base

    def append(self, other, at=(0.0, 0.0, 0.0), yaw=0.0, pitch=0.0, prefix="", seed=None):
        """把另一只（在局部建好、面朝 +X 的）生物整体搬进来：绕 Z 偏航、绕 Y 俯仰后平移。
        鱼群用——群里的鱼各有位置与朝向；它的关节角色名前面加 prefix（f0s0、f1finL……），
        骨架与蒙皮随顶点一起搬。"""
        mtx = (Matrix.Translation(Vector(at)) @ Matrix.Rotation(yaw, 4, "Z")
               @ Matrix.Rotation(pitch, 4, "Y"))
        base = len(self.verts)
        joint_base = len(self.joint_roles)
        for role, pos, parent in zip(other.joint_roles, other.joint_pos, other.joint_parent):
            p = mtx @ Vector(pos)
            self.joint(prefix + role, (p.x, p.y, p.z),
                       None if parent < 0 else joint_base + parent)
        for slot in other.slot_order:
            self._slot(slot)
        self.verts += [tuple(mtx @ Vector(v)) for v in other.verts]
        self.vert_aux += [0.0] * len(other.verts)
        for f in other.faces:
            self.faces.append(tuple(base + i for i in f))
        self.face_mat += list(other.face_mat)
        self.face_smooth += list(other.face_smooth)
        self.skin += [(a + joint_base, wa, b + joint_base, wb) for (a, wa, b, wb) in other.skin]
        self.vert_seed += [other.seed if seed is None else float(seed)] * len(other.verts)

    def scale(self, factor):
        """整件等比缩放：顶点与关节静止位置一起乘。"""
        self.verts = [tuple(factor * c for c in v) for v in self.verts]
        self.joint_pos = [tuple(factor * c for c in p) for p in self.joint_pos]
        return self

    def to_object(self, name):
        """合成网格：三套 UV 层按固定顺序导出（FBX 的 UVMap / UVMap.001 / UVMap.002）。"""
        if any(self.vert_aux):
            raise ValueError("[%s] Creature 不用 Mesher 的单辅助量通道（蒙皮走三套 UV）" % name)
        if len(self.skin) != len(self.verts):
            raise ValueError("[%s] 蒙皮表 %d 条 ≠ 顶点数 %d" % (name, len(self.skin), len(self.verts)))
        obj = K.Mesher.to_object(self, name, recalc_normals=True)
        me = obj.data
        if len(me.vertices) != len(self.verts):
            raise ValueError("[%s] 网格校验后顶点数 %d ≠ %d，逐顶点蒙皮会错位"
                             % (name, len(me.vertices), len(self.verts)))

        def layer(layer_name, pick):
            data = me.uv_layers.new(name=layer_name).data
            for poly in me.polygons:
                for li in poly.loop_indices:
                    data[li].uv = pick(me.loops[li].vertex_index)

        layer("UVMap", lambda i: (float(self.skin[i][0]), self.skin[i][1]))
        layer("UVMap.001", lambda i: (float(self.skin[i][2]), self.skin[i][3]))
        layer("UVMap.002", lambda i: (1.0, self.vert_seed[i]))
        _CREATURES[obj.name] = self
        return obj


def bind_joints(objects):
    """给全部生物的关节统一编号（按对象名排序，重跑同号）：每个关节落成空物体 J<编号>_<角色>
    （一律平挂在生物对象下、位置 = 静止位置；有父关节的名字是 J<号>_P<父号>_<角色>），并把网格
    uv0 / uv1 里的本地关节号换成全局号。
    返回空物体列表——要跟生物对象一起导出。"""
    import bpy
    empties = []
    counter = 0
    for obj in sorted(objects, key=lambda o: o.name):
        creature = _CREATURES.get(obj.name)
        if creature is None:
            raise ValueError("对象 %s 不是 Creature.to_object 建的，没有骨架可绑" % obj.name)
        base = counter
        collection = obj.users_collection[0]
        for k, (role, pos, parent) in enumerate(zip(creature.joint_roles, creature.joint_pos,
                                                    creature.joint_parent)):
            # 父关节写在名字里（J<号>_P<父号>_<角色>），不靠层级：Blender 导出嵌套空物体时换一套约定
            # （位置用米、带 90° 旋转与 0.01 缩放），和平挂的空物体对不上。空物体一律平挂在生物对象下。
            name = "J%d_%s" % (base + k, ("P%d_" % (base + parent) + role) if parent >= 0 else role)
            empty = bpy.data.objects.new(name, None)
            empty.empty_display_type = 'PLAIN_AXES'
            empty.empty_display_size = 0.04
            collection.objects.link(empty)
            empty.parent = obj
            empty.matrix_parent_inverse = Matrix.Identity(4)
            empty.location = pos
            empties.append(empty)
        me = obj.data
        for layer_name, which in (("UVMap", 0), ("UVMap.001", 2)):
            data = me.uv_layers[layer_name].data
            for poly in me.polygons:
                for li in poly.loop_indices:
                    data[li].uv[0] = base + creature.skin[me.loops[li].vertex_index][which]
        counter += len(creature.joint_roles)
    return empties, counter


# ---------------------------------------------------------------------------
# 图元
# ---------------------------------------------------------------------------

def _stitched(verts, starts, n, count, mat, face_mat, cap):
    """把一串环 / 极点缝成面。返回 (faces, 槽名表)。starts = 各环首顶点下标（末尾带哨兵）。"""
    faces, mats = [], []
    for k in range(count - 1):
        a, b = starts[k], starts[k + 1]
        a_pole = starts[k + 1] - a == 1
        b_pole = starts[k + 2] - b == 1
        for j in range(n):
            j1 = (j + 1) % n
            if a_pole:
                f = (a, b + j1, b + j)
            elif b_pole:
                f = (a + j, a + j1, b)
            else:
                f = (a + j, a + j1, b + j1, b + j)
            faces.append(f)
            mats.append(mat if face_mat is None else face_mat(k, j))
    for use, idx in ((cap[0], 0), (cap[1], count - 1)):
        if use and starts[idx + 1] - starts[idx] > 1:
            faces.append(tuple(starts[idx] + j for j in range(n)))
            mats.append(mat)
    return faces, mats


def loft(c, rings, mat, n, smooth=True, skin=None, face_mat=None, cap=(True, True)):
    """一串横截面环缝成闭合管体（鱼身、尾柄、吻……）。
    rings = [(cx, cy, cz, 半宽_y, 半高_z), ...] 沿 X 给；半宽 = 半高 = 0 的环收成极点。
    face_mat(k, j) → 第 k 段第 j 面的槽名，用来画条纹与腹面（j = 0 在背上，向 +Y 侧数）。
    cap = (首环, 末环) 不是极点时是否用 n 边形封口。
    """
    verts, starts = [], []
    for (cx, cy, cz, ry, rz) in rings:
        starts.append(len(verts))
        if ry <= 1e-6 and rz <= 1e-6:
            verts.append((cx, cy, cz))
        else:
            for j in range(n):
                a = math.pi / 2.0 + TAU * j / n
                verts.append((cx, cy + ry * math.cos(a), cz + rz * math.sin(a)))
    starts.append(len(verts))
    faces, mats = _stitched(verts, starts, n, len(rings), mat, face_mat, cap)
    c.add(verts, faces, mats, smooth=smooth, skin=skin)


def revolve(c, profile, mat, n, centre=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0), smooth=True,
            skin=None, face_mat=None, cap=(True, True)):
    """绕上轴 Z 的旋转体（蟹壳、水母铃、章鱼头……）。profile = [(半径, z), ...] 自下而上；
    半径 0 的环收成极点。face_mat(k, j) → 第 k 段第 j 面的槽名。"""
    cx, cy, cz = centre
    verts, starts = [], []
    for (r, z) in profile:
        starts.append(len(verts))
        if r <= 1e-6:
            verts.append((cx, cy, cz + z * scale[2]))
        else:
            for j in range(n):
                a = TAU * j / n
                verts.append((cx + r * scale[0] * math.cos(a), cy + r * scale[1] * math.sin(a),
                              cz + z * scale[2]))
    starts.append(len(verts))
    faces, mats = _stitched(verts, starts, n, len(profile), mat, face_mat, cap)
    c.add(verts, faces, mats, smooth=smooth, skin=skin)


def blob(c, centre, radii, mat, n=8, rings=4, smooth=True, skin=None, z_lo=-1.0, z_hi=1.0,
         face_mat=None):
    """椭球（眼睛、头、鼓包）。radii = (rx, ry, rz)；z_lo / z_hi = 上下限（相对半径，可切球冠）。"""
    a0, a1 = math.asin(max(-1.0, z_lo)), math.asin(min(1.0, z_hi))
    profile = []
    for i in range(rings + 1):
        a = a0 + (a1 - a0) * i / rings
        profile.append((math.cos(a), math.sin(a)))
    if z_lo <= -0.999:
        profile[0] = (0.0, profile[0][1])
    if z_hi >= 0.999:
        profile[-1] = (0.0, profile[-1][1])
    revolve(c, profile, mat, n, centre=centre, scale=radii, smooth=smooth, skin=skin,
            face_mat=face_mat, cap=(z_lo > -0.999, z_hi < 0.999))


def frame(origin, ex, ey):
    """局部平面标架：返回 Frame；place(u, v, n) → 物体空间坐标，n 沿 ex × ey。"""
    return Frame(origin, ex, ey)


class Frame(object):
    """局部平面标架（u 沿 ex、v 沿 ey、法向 n = ex × ey），并能把物体空间点反解回局部坐标。"""

    def __init__(self, origin, ex, ey):
        self.o = Vector(origin)
        self.a = Vector(ex).normalized()
        b = Vector(ey)
        self.b = (b - self.a * b.dot(self.a)).normalized()
        self.n = self.a.cross(self.b).normalized()

    def place(self, u, v, d=0.0):
        p = self.o + self.a * u + self.b * v + self.n * d
        return (p.x, p.y, p.z)

    def u(self, point):
        return (Vector(point) - self.o).dot(self.a)

    def v(self, point):
        return (Vector(point) - self.o).dot(self.b)


def sheet(c, pts, thick, place, mat, smooth=False, skin=None):
    """薄片鳍 / 翼 / 鳍肢：pts = 局部平面上的闭合多边形（任意绕序），沿法向居中挤出 thick 的闭合棱柱。
    place = Frame.place（(u, v, n) → 物体空间）。"""
    tris = tessellate_polygon([[Vector((p[0], p[1], 0.0)) for p in pts]])   # 返回下标三元组
    n = len(pts)
    verts = [place(p[0], p[1], thick * 0.5) for p in pts] + [place(p[0], p[1], -thick * 0.5) for p in pts]
    faces = []
    for tri in tris:
        faces.append((tri[0], tri[1], tri[2]))
        faces.append(tuple(n + i for i in reversed(tri)))
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    c.add(verts, faces, mat, smooth=smooth, skin=skin)


def tube(c, points, radii, mat, n=6, smooth=True, skin=None, joints=None, face_mat=None,
         cap=(True, True)):
    """沿折线的管（腕、触手、腿、眼柄、吻）。截面用平行移动标架，不沿轴向扭转。
    points = [(x, y, z), ...]；radii 与 points 等长（或给首尾两个自动线性过渡）。
    joints = 与 points 等长的关节序号表（每个点是一个链节点）：按截面站绑（station_skins）。
    face_mat(k, j) → 第 k 段第 j 面的槽名。"""
    pts = [Vector(p) for p in points]
    if len(radii) != len(pts):
        r0, r1 = radii[0], radii[-1]
        radii = [r0 + (r1 - r0) * i / (len(pts) - 1) for i in range(len(pts))]
    tangents = []
    for i in range(len(pts)):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == len(pts) - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tangents.append(t.normalized())
    ref = Vector((0.0, 0.0, 1.0)) if abs(tangents[0].z) < 0.9 else Vector((1.0, 0.0, 0.0))
    normal = (ref - tangents[0] * ref.dot(tangents[0])).normalized()
    verts, starts = [], []
    for i, p in enumerate(pts):
        t = tangents[i]
        if i > 0:
            normal = (normal - t * normal.dot(t)).normalized()   # 平行移动
        binrm = t.cross(normal).normalized()
        starts.append(len(verts))
        for j in range(n):
            a = TAU * j / n
            q = p + radii[i] * (normal * math.cos(a) + binrm * math.sin(a))
            verts.append((q.x, q.y, q.z))
    starts.append(len(verts))
    faces, mats = _stitched(verts, starts, n, len(pts), mat, face_mat, cap)
    if joints is not None:
        if len(joints) != len(pts):
            raise ValueError("管的关节数 %d ≠ 站点数 %d" % (len(joints), len(pts)))
        per_station = station_skins(joints)
        skin = [per_station[i] for i in range(len(pts)) for _ in range(n)]
    c.add(verts, faces, mats, smooth=smooth, skin=skin)


def eye(c, centre, out, r, pupil=0.58, glint=False, skin=None, up=(0.0, 0.0, 1.0),
        fwd=(1.0, 0.0, 0.0)):
    """卡通眼睛：白眼球嵌在体表、深色瞳仁盖在上面，外圈留一圈眼白；glint 加一颗高光点。
    centre = 眼睛在体表上的位置，out = 体表外法向（不必归一）。"""
    o = Vector(out).normalized()
    white = Vector(centre) - o * (r * 0.45)
    pr = r * pupil
    pc = Vector(centre) + o * (r * 0.22)
    blob(c, tuple(white), (r, r, r), EYE, n=6, rings=3, skin=skin)
    blob(c, tuple(pc), (pr, pr, pr), INK, n=6, rings=3, skin=skin)
    if glint:
        g = pc + o * (pr * 0.55) + (Vector(up) * 0.55 + Vector(fwd) * 0.35).normalized() * (pr * 0.45)
        blob(c, tuple(g), (pr * 0.26,) * 3, EYE, n=4, rings=2, skin=skin)
