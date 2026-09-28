# -*- coding: utf-8 -*-
"""chemplant/kit_common.py —— 第三样板关「废弃化工厂」公共几何 + 材质库（Blender 5.2 无头）。

【这一层解决什么】各分件模块脚本（`mod_*.py`）不许自己写顶点、不许散写色值：
一律只用本文件的 `Mesher` 图元与 `ST.SLOTS` 槽名。分件之间唯一的耦合是**分区包围盒**
（`AREAS` 表）+ **本地坐标系口径**（见下），装配脚本 `assemble_chemplant.py` 按 `AREAS`
把分件平移到位、导出 FBX、渲染成品图。

【本地坐标系口径（硬指标）】
    · Blender Z-up，1 单位 = 1 米；**场地地面 z = 0**，一切物件从 z=0 往上长。
    · 分件在**自己的本地系**里建模：原点 = 该分区包围盒的**中心**在地面的投影（即 z=0）。
      `AREAS[名]['center']` 只是装配时的平移量，分件脚本**不关心**自己在场地哪儿。
    · 朝向：+Y = 场地「纵深向北」，-Y = 「朝向镜头/大门」那一侧；+X = 东。
    · 分区包围盒 = 硬边界：分件不许把主体长出 `AREAS[名]['size']`（管道口、悬挑雨棚可越界 ≤1.5 m）。

【面数与材质纪律】
    · 无面数上限（创始人 2026-09-29 口谕：疯狂搞）；但**每件建议 30k~150k 三角面**，
      细节堆在轮廓与近景可读处（栏杆/爬梯/管件/锈蚀分割线），别用高分段圆去堆。
    · 材质只能用 `ST.SLOTS` 里已登记的槽名（拼错当场 KeyError）。
      工业槽：Kit_ConcreteLight/Mid/Dark、Kit_Rust、Kit_RustDark、Kit_SteelBlue、
              Kit_SteelPale、Kit_GlassDark、Kit_PaintYellow；
      借用的通用槽：Kit_Iron、Kit_RockMid、Kit_RockDark、Kit_SandDark、Kit_GrassMid、
              Kit_GrassDark、Kit_WoodMid、Kit_WoodDark、Kit_WetSand。

【自检出图】`preview(...)` 一键出 640px / 24 采样的自检图（分件脚本收工前必须看一眼）。
成品图（写实贴图 + HDRI 环境光）由装配脚本统一出，走 `textures.py`。

用法：
    import kit_common as K
    m = K.Mesher()
    m.box((4, 4, 0.4), at=(0, 0, 0), mat='Kit_ConcreteMid')
    m.cyl(1.6, 12.0, at=(0, 0, 0.4), seg=24, mat='Kit_Rust')
    obj = m.to_object('ChemPlant_Towers')
    K.preview([obj], 'external/chemplant-work/towers_check.jpg', cam=(28, -28, 18), target=(0, 0, 6))
"""

import math
import os
import sys

import bpy
import bmesh
import mathutils
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
SCENE_DIR = os.path.dirname(HERE)
if SCENE_DIR not in sys.path:
    sys.path.insert(0, SCENE_DIR)
import style_tokens as ST                                     # noqa: E402

ROOT = os.path.abspath(os.path.join(SCENE_DIR, "..", "..", ".."))
WORK_DIR = os.path.join(ROOT, "external", "chemplant-work")
FBX_DIR = os.path.join(ROOT, "pirate-crew", "Assets", "Art", "Models", "SceneKit")
SHOT_DIR = os.path.join(ROOT, "docs", "images", "chemplant-scene")
TEX_DIR = os.path.join(WORK_DIR, "tex")

TAU = math.pi * 2.0

# ---------------------------------------------------------------------------
# 0. 场地分区表 —— 分件脚本与装配脚本共同的唯一事实源
# ---------------------------------------------------------------------------

#: 场地尺寸（米）：56（东西 X）× 40（南北 Y）。原点 = 场地中心，地面 z=0。
SITE_SIZE = (56.0, 40.0)

#: 分区表：'size' = 围绕盒（米，本地系），'center' = 该盒中心在场地里的平移量。
#: 装配脚本按 center 平移，分件脚本按 size 约束自己。
#:
#:   俯视（+Y 向北 / -Y 朝大门与镜头，+X 向东）：
#:   ┌──────────────────────────────────────────────────────┐ Y=+20
#:   │  towers  X[-27,-7] Y[5,19]    tanks X[0,26] Y[5,19]   │
#:   │  ─── 南北支路 X[-6.5,-3.5]（Y=-8 直上 Y=18）───        │
#:   │  pipes 管廊 X[-27,27] Y[-0.1,4.5]   ← 抬高跨过支路     │
#:   │═══ 东西主路 Y[-8,-4]（西端 X=-28 接大门）═════════════│
#:   │  props 南院 X[-27,0] Y[-19,-8.5]   building X[2,24]   │
#:   └──────────────────────────────────────────────────────┘ Y=-20
#:  X=-28                                                  X=+28
AREAS = {
    # 主装置区（精馏塔/反应器/烟囱/框架塔）——西北
    "towers":   {"size": (20.0, 14.0, 26.0), "center": (-17.0, 12.0, 0.0)},
    # 罐区（立式罐/球罐/冷却塔/围堰）——东北
    "tanks":    {"size": (26.0, 14.0, 18.0), "center": (13.0, 12.0, 0.0)},
    # 管廊（东西横贯主管桥 + 泵组 + 阀门站）——场地中部
    "pipes":    {"size": (54.0, 4.6, 9.0), "center": (0.0, 2.2, 0.0)},
    # 旁楼：办公/实验楼 + 仓库附属——东南（正对大路，镜头主看面）
    "building": {"size": (22.0, 11.0, 15.0), "center": (13.0, -13.5, 0.0)},
    # 场地（地坪/道路/围墙/大门/荒草）——铺满全场
    "site":     {"size": (56.0, 40.0, 6.0), "center": (0.0, 0.0, 0.0)},
    # 散落杂物（油桶/集装箱/废料/废车/电线杆）——西南院（避开上面所有分区）
    "props":    {"size": (33.0, 18.0, 7.0), "center": (-13.5, -13.0, 0.0)},
}

#: 道路（全场坐标）：东西主路 + 南北支路（支路抬高管廊从其上方跨过）。
ROAD_MAIN_Y = (-8.0, -4.0)      # 东西主路（4 m 宽），西端接大门
ROAD_SIDE_X = (-6.5, -3.5)      # 南北支路（3 m 宽），从主路北上
GATE_X = -28.0                  # 西侧大门所在 X（围墙线上）

# ---------------------------------------------------------------------------
# 1. 材质：只从 ST.SLOTS 取，渲染侧金属度按槽名归类（Unity 侧由 C# 表重建，不受影响）
# ---------------------------------------------------------------------------

#: 【零贴图铁律】本 kit 不引任何贴图、不设金属度：像素化着色路径的物体 pass 只收
#: `Albedo = 亮部色` + `Physical(光滑度, 金属度)` + `Palette(主光档数/抖动/边光/描边开关)`，
#: 颜色还要过帧级调色板；PBR 贴图与金属度的细节在低分辨率画布上根本落不住（创始人 2026-09-29 提醒）。
#: ⇒ 「写实质感」只能由**几何密度**（板缝/铆钉排/爬梯/栏杆/管件/法兰）与**调色板对比**（锈/混凝土/漆色）承担。
#: metallic 一律 0，仅粗糙度按 ST.SLOTS 取（Unity 侧 roughness = 1 - smoothness，同源）。


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def slot_color(slot_name):
    """槽名 → 线性 RGB（Principled Base Color 要线性值）。"""
    hex_str = ST.slot(slot_name)["hex"].lstrip("#")
    rgb = [int(hex_str[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return tuple(srgb_to_linear(c) for c in rgb)


def make_material(slot_name):
    """按槽名建 Principled 材质（已存在则复用）。颜色/粗糙度唯一取自 ST.SLOTS。"""
    mat = bpy.data.materials.get(slot_name)
    if mat is not None:
        return mat
    spec = ST.slot(slot_name)
    mat = bpy.data.materials.new(slot_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*slot_color(slot_name), 1.0)
    bsdf.inputs["Roughness"].default_value = float(spec.get("roughness", 0.8))
    bsdf.inputs["Metallic"].default_value = 0.0                  # 像素管线不吃金属度，见上
    mat["chemplant_slot"] = slot_name
    return mat


def material_of(slot_name):
    return make_material(slot_name)


# ---------------------------------------------------------------------------
# 2. Mesher —— 纯 Python 顶点/面累加器（不走 bmesh 图元，快且可控）
# ---------------------------------------------------------------------------

class Mesher(object):
    """分件建模累加器：图元写进 verts/faces，收工时 `to_object()` 合成一个网格。

    图元统一签名约定：
        at   = 图元**基点**在该分件本地系的位置（多数图元 = 底面中心；见各方法说明）
        rz/ry/rx = 绕本地轴的旋转（弧度），按 Z→Y→X 顺序合成
        mat  = ST.SLOTS 槽名（拼错当场 KeyError）
    """

    def __init__(self):
        self.verts = []
        self.faces = []          # 索引元组
        self.face_mat = []       # 与 faces 等长的槽名
        self.face_smooth = []    # 与 faces 等长的 bool
        self._slot_index = {}    # 槽名 -> 槽序号（用于材质槽排序）
        self.slot_order = []

    # -- 内部 ---------------------------------------------------------------
    def _slot(self, slot_name):
        ST.slot(slot_name)                                       # 未登记槽名当场抛错
        if slot_name not in self._slot_index:
            self._slot_index[slot_name] = len(self.slot_order)
            self.slot_order.append(slot_name)
        return self._slot_index[slot_name]

    def _matrix(self, at, rz=0.0, ry=0.0, rx=0.0, scale=(1.0, 1.0, 1.0)):
        rot = (Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(ry, 4, "Y")
               @ Matrix.Rotation(rx, 4, "X"))
        return Matrix.Translation(Vector(at)) @ rot @ Matrix.Diagonal(Vector(scale).to_4d())

    def add(self, local_verts, local_faces, at=(0, 0, 0), rz=0.0, ry=0.0, rx=0.0,
            mat="Kit_ConcreteMid", smooth=False, scale=(1.0, 1.0, 1.0),
            smooth_faces=None):
        """把一段局部顶点/面按变换并进累加器。smooth_faces = 需要平滑的局部面序号集合。"""
        self._slot(mat)
        base = len(self.verts)
        mtx = self._matrix(at, rz, ry, rx, scale)
        for v in local_verts:
            self.verts.append(tuple(mtx @ Vector(v)))
        for fi, f in enumerate(local_faces):
            self.faces.append(tuple(base + i for i in f))
            self.face_mat.append(mat)
            self.face_smooth.append(bool(smooth) if smooth_faces is None
                                    else (fi in smooth_faces))
        return base

    def counts(self):
        tris = sum(len(f) - 2 for f in self.faces)
        return len(self.verts), len(self.faces), tris

    # -- 基本体 -------------------------------------------------------------
    def box(self, size, at=(0, 0, 0), rz=0.0, ry=0.0, rx=0.0, mat="Kit_ConcreteMid",
            smooth=False):
        """长方体。at = **底面中心**（z 向上长 size[2]）。"""
        sx, sy, sz = size
        hx, hy = sx / 2.0, sy / 2.0
        v = [(-hx, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0),
             (-hx, -hy, sz), (hx, -hy, sz), (hx, hy, sz), (-hx, hy, sz)]
        f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        return self.add(v, f, at, rz, ry, rx, mat, smooth)

    def box_center(self, size, at=(0, 0, 0), **kw):
        """at = 几何中心的长方体（栏杆、构件等按中心摆放时更顺手）。"""
        at2 = (at[0], at[1], at[2] - size[2] / 2.0)
        return self.box(size, at2, **kw)

    def cyl(self, r, h, at=(0, 0, 0), seg=16, r_top=None, rz=0.0, ry=0.0, rx=0.0,
            mat="Kit_SteelPale", smooth=True, base_r=None):
        """圆柱/圆台。at = **底面中心**，向 +Z 长 h；r_top 给圆台（顶半径）。

        smooth 只作用于侧面（端盖保持平直，法线才对）。
        """
        r_top = r if r_top is None else r_top
        base_r = r if base_r is None else base_r
        v, f, sf = [], [], []
        for i in range(seg):
            a = TAU * i / seg
            v.append((base_r * math.cos(a), base_r * math.sin(a), 0.0))
        for i in range(seg):
            a = TAU * i / seg
            v.append((r_top * math.cos(a), r_top * math.sin(a), h))
        for i in range(seg):
            j = (i + 1) % seg
            f.append((i, j, seg + j, seg + i))
            sf.append(len(f) - 1)
        f.append(tuple(range(seg - 1, -1, -1)))
        f.append(tuple(range(seg, seg * 2)))
        return self.add(v, f, at, rz, ry, rx, mat, smooth, smooth_faces=set(sf))

    def revolve(self, profile, at=(0, 0, 0), seg=24, rz=0.0, mat="Kit_SteelPale",
                smooth=True, caps=True):
        """旋转体（母线绕本地 Z 轴）。profile = [(r, z), ...] 从下往上给。

        r=0 的端点自动收成极点（球罐、封头、锥顶就靠它）；r>0 的端点用端盖封口。
        """
        v, f, sf = [], [], []
        rings = []
        for (r, z) in profile:
            if r <= 1e-6:
                rings.append(("pole", len(v)))
                v.append((0.0, 0.0, z))
            else:
                start = len(v)
                for i in range(seg):
                    a = TAU * i / seg
                    v.append((r * math.cos(a), r * math.sin(a), z))
                rings.append(("ring", start))
        for k in range(len(rings) - 1):
            t0, s0 = rings[k]
            t1, s1 = rings[k + 1]
            if t0 == "ring" and t1 == "ring":
                for i in range(seg):
                    j = (i + 1) % seg
                    f.append((s0 + i, s0 + j, s1 + j, s1 + i))
                    sf.append(len(f) - 1)
            elif t0 == "pole" and t1 == "ring":
                for i in range(seg):
                    j = (i + 1) % seg
                    f.append((s0, s1 + j, s1 + i))
                    sf.append(len(f) - 1)
            elif t0 == "ring" and t1 == "pole":
                for i in range(seg):
                    j = (i + 1) % seg
                    f.append((s0 + i, s0 + j, s1))
                    sf.append(len(f) - 1)
        if caps and profile[0][0] > 1e-6:
            f.append(tuple(range(seg - 1, -1, -1)))
        if caps and profile[-1][0] > 1e-6:
            last = rings[-1][1]
            f.append(tuple(range(last, last + seg)))
        return self.add(v, f, at, 0.0, 0.0, 0.0, mat, smooth, smooth_faces=set(sf))

    def sphere(self, r, at=(0, 0, 0), seg=24, rings=12, z_lo=-1.0, z_hi=1.0,
               mat="Kit_SteelPale", smooth=True):
        """球/球带。at = **球心**；z_lo/z_hi = 相对半径的上下限（-1~1，可切球冠）。"""
        v, f, sf = [], [], []
        z_lo, z_hi = max(-1.0, z_lo), min(1.0, z_hi)
        lat0 = math.asin(z_lo)
        lat1 = math.asin(z_hi)
        ring_idx = []
        for i in range(rings + 1):
            lat = lat0 + (lat1 - lat0) * i / rings
            zz, rr = math.sin(lat), math.cos(lat)
            if rr <= 1e-6:
                ring_idx.append(("pole", len(v)))
                v.append((0.0, 0.0, r * zz))
            else:
                start = len(v)
                for k in range(seg):
                    a = TAU * k / seg
                    v.append((r * rr * math.cos(a), r * rr * math.sin(a), r * zz))
                ring_idx.append(("ring", start))
        for k in range(len(ring_idx) - 1):
            t0, s0 = ring_idx[k]
            t1, s1 = ring_idx[k + 1]
            if t0 == "ring" and t1 == "ring":
                for i in range(seg):
                    j = (i + 1) % seg
                    f.append((s0 + i, s0 + j, s1 + j, s1 + i))
                    sf.append(len(f) - 1)
            elif t0 == "pole":
                for i in range(seg):
                    j = (i + 1) % seg
                    f.append((s0, s1 + j, s1 + i))
                    sf.append(len(f) - 1)
            else:
                for i in range(seg):
                    j = (i + 1) % seg
                    f.append((s0 + i, s0 + j, s1))
                    sf.append(len(f) - 1)
        return self.add(v, f, at, 0.0, 0.0, 0.0, mat, smooth, smooth_faces=set(sf))

    def poly_extrude(self, pts2d, z0, z1, at=(0, 0, 0), rz=0.0, mat="Kit_Rust", smooth=False):
        """把 XY 平面上的**凸/凹多边形**沿 Z 挤出（z0→z1）：工字钢、角钢、异形板都用它。

        pts2d 建议逆时针给（法线朝向由收工时的 recalc 统一修，不用自己管）。
        """
        n = len(pts2d)
        v = [(p[0], p[1], z0) for p in pts2d] + [(p[0], p[1], z1) for p in pts2d]
        f = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n
            f.append((i, j, n + j, n + i))
        return self.add(v, f, at, rz, 0.0, 0.0, mat, smooth)

    def extrude_along(self, pts2d, a, b, mat="Kit_Rust", smooth=False, roll=0.0):
        """把 XY 剖面沿 a→b 方向挤出（任意朝向的梁/管件）：局部 Z 轴对齐 a→b。"""
        a, b = Vector(a), Vector(b)
        d = (b - a)
        length = d.length
        if length < 1e-9:
            return
        quat = d.to_track_quat("Z", "Y")
        mtx = Matrix.Translation(a) @ quat.to_matrix().to_4x4() @ Matrix.Rotation(roll, 4, "Z")
        n = len(pts2d)
        v = [(p[0], p[1], 0.0) for p in pts2d] + [(p[0], p[1], length) for p in pts2d]
        v = [tuple(mtx @ Vector(p)) for p in v]
        f = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n
            f.append((i, j, n + j, n + i))
        base = len(self.verts)
        self._slot(mat)
        self.verts.extend(v)
        for face in f:
            self.faces.append(tuple(base + i for i in face))
            self.face_mat.append(mat)
            self.face_smooth.append(smooth)

    def tube(self, points, r, at=(0, 0, 0), seg=10, r_end=None, mat="Kit_SteelPale",
             caps=True, smooth=True, up=(0, 0, 1)):
        """折线管道：points = [(x,y,z), ...]（本地系）；转角按相邻段方向**斜接**。

        r_end 给末端半径（异径管）；caps=True 用平面端盖封口。
        """
        pts = [Vector(p) for p in points]
        r_end = r if r_end is None else r_end
        n = len(pts)
        ring_idx = []
        v, f, sf = [], [], []
        for i, p in enumerate(pts):
            if i == 0:
                d = (pts[1] - pts[0])
            elif i == n - 1:
                d = (pts[-1] - pts[-2])
            else:
                d = (pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()
            d = d.normalized()
            u = Vector(up)
            if abs(d.dot(u)) > 0.999:
                u = Vector((1.0, 0.0, 0.0))
            xax = d.cross(u).normalized()
            yax = d.cross(xax).normalized()
            rr = r + (r_end - r) * (i / max(1, n - 1))
            start = len(v)
            for k in range(seg):
                a = TAU * k / seg
                v.append(tuple(p + xax * (math.cos(a) * rr) + yax * (math.sin(a) * rr)))
            ring_idx.append(start)
        for i in range(n - 1):
            s0, s1 = ring_idx[i], ring_idx[i + 1]
            for k in range(seg):
                j = (k + 1) % seg
                f.append((s0 + k, s0 + j, s1 + j, s1 + k))
                sf.append(len(f) - 1)
        if caps:
            f.append(tuple(range(ring_idx[0] + seg - 1, ring_idx[0] - 1, -1)))
            last = ring_idx[-1]
            f.append(tuple(range(last, last + seg)))
        return self.add(v, f, at, 0.0, 0.0, 0.0, mat, smooth, smooth_faces=set(sf))

    # -- 工业件（高频组合，省得各分件重复造轮子）-----------------------------
    def member(self, a, b, w, t=None, mat="Kit_Rust", smooth=False, roll=0.0):
        """方钢构件：a→b 之间一根矩形截面型钢（w=宽，t=厚，缺省 t=w）。"""
        t = w if t is None else t
        pts = [(-w / 2, -t / 2), (w / 2, -t / 2), (w / 2, t / 2), (-w / 2, t / 2)]
        self.extrude_along(pts, a, b, mat=mat, smooth=smooth, roll=roll)

    def ibeam(self, a, b, h=0.4, w=0.22, tf=0.05, tw=0.035, mat="Kit_SteelBlue",
              smooth=False, roll=0.0):
        """工字钢：a→b 一根（框架塔/管廊的主角）。"""
        hh, hw = h / 2.0, w / 2.0
        pts = [(-hw, -hh), (hw, -hh), (hw, -hh + tf), (tw / 2, -hh + tf), (tw / 2, hh - tf),
               (hw, hh - tf), (hw, hh), (-hw, hh), (-hw, hh - tf), (-tw / 2, hh - tf),
               (-tw / 2, -hh + tf), (-hw, -hh + tf)]
        self.extrude_along(pts, a, b, mat=mat, smooth=smooth, roll=roll)

    def pipe_member(self, a, b, r=0.08, seg=10, mat="Kit_SteelPale"):
        """两点之间的圆管（斜撑/拉杆/细管）。"""
        self.tube([a, b], r, seg=seg, mat=mat)

    def ladder(self, at=(0, 0, 0), h=6.0, w=0.5, r=0.03, rung_step=0.32, rz=0.0,
               mat="Kit_Rust", cage=False, cage_r=0.34):
        """竖直爬梯（工业塔器必备）：两根边梁 + 踏棍，cage=True 加安全笼圈。"""
        mtx = Matrix.Rotation(rz, 4, "Z")
        for side in (-1, 1):
            a = mtx @ Vector((side * w / 2, 0.0, 0.0)) + Vector(at)
            b = mtx @ Vector((side * w / 2, 0.0, h)) + Vector(at)
            self.tube([a, b], r, seg=6, mat=mat)
        n = max(1, int(h / rung_step))
        for i in range(1, n + 1):
            z = min(h - 0.05, i * rung_step)
            a = mtx @ Vector((-w / 2, 0.0, z)) + Vector(at)
            b = mtx @ Vector((w / 2, 0.0, z)) + Vector(at)
            self.tube([a, b], r * 0.8, seg=6, mat=mat)
        if cage:
            step = 0.7
            z = 1.4
            while z < h - 0.4:
                ring = []
                for k in range(13):
                    ang = math.pi * (k / 12.0) - math.pi / 2.0
                    p = Vector((math.cos(ang) * cage_r, -math.sin(ang) * cage_r * 0.55 - 0.05, z))
                    ring.append(tuple(mtx @ p + Vector(at)))
                self.tube(ring, r * 0.7, seg=5, mat=mat, caps=False, smooth=True)
                z += step

    def railing(self, points, h=1.1, at=(0, 0, 0), rz=0.0, post_step=2.0, r_top=0.035,
                r_mid=0.025, mat="Kit_Rust", mid_rail=True):
        """沿折线的一排栏杆：立柱 + 上下横杆（平台/走道/屋顶女儿墙上的主角）。"""
        mtx = Matrix.Rotation(rz, 4, "Z")
        pts = [Vector((p[0], p[1], 0.0)) for p in points]        # 只需 XY，高度由 h 定
        corners = list(pts)
        # 按弧长补立柱
        total = 0.0
        for i in range(len(pts) - 1):
            seg = (pts[i + 1] - pts[i]).length
            k = max(1, int(seg / post_step))
            for j in range(k):
                t = j / float(k)
                corners.append(pts[i] + (pts[i + 1] - pts[i]) * t)
            total += seg
        for c in corners:
            self.member((mtx @ c) + Vector(at), (mtx @ (c + Vector((0, 0, h)))) + Vector(at),
                        0.06, 0.06, mat=mat)
        rails = [h] if not mid_rail else [h, h * 0.52]
        for rr in rails:
            for i in range(len(pts) - 1):
                a = Vector((pts[i].x, pts[i].y, rr))
                b = Vector((pts[i + 1].x, pts[i + 1].y, rr))
                self.pipe_member((mtx @ a) + Vector(at), (mtx @ b) + Vector(at),
                                 r=r_top if rr == h else r_mid, seg=6, mat=mat)

    def platform(self, size, z, at=(0, 0, 0), thick=0.08, mat="Kit_Rust",
                 rail_h=1.1, rail_sides="nesw", rail_mat=None, toeboard=True):
        """操作平台：格栅板 + 四面（可选）栏杆 + 踢脚板。at = 平台中心的地面投影。"""
        w, d = size
        self.box((w, d, thick), (at[0] - w / 2, at[1] - d / 2, z - thick), mat=mat)
        rail_mat = rail_mat or mat
        if toeboard:
            self.box((w, 0.06, 0.12), (at[0] - w / 2, at[1] - d / 2, z), mat=rail_mat)
            self.box((w, 0.06, 0.12), (at[0] - w / 2, at[1] + d / 2 - 0.06, z), mat=rail_mat)
            self.box((0.06, d, 0.12), (at[0] - w / 2, at[1] - d / 2, z), mat=rail_mat)
            self.box((0.06, d, 0.12), (at[0] + w / 2 - 0.06, at[1] - d / 2, z), mat=rail_mat)
        ox, oy = at[0] - w / 2, at[1] - d / 2
        if "n" in rail_sides:
            self.railing([(ox, oy + d), (ox + w, oy + d)], h=rail_h, at=(0, 0, z), mat=rail_mat)
        if "s" in rail_sides:
            self.railing([(ox, oy), (ox + w, oy)], h=rail_h, at=(0, 0, z), mat=rail_mat)
        if "w" in rail_sides:
            self.railing([(ox, oy), (ox, oy + d)], h=rail_h, at=(0, 0, z), mat=rail_mat)
        if "e" in rail_sides:
            self.railing([(ox + w, oy), (ox + w, oy + d)], h=rail_h, at=(0, 0, z), mat=rail_mat)

    def flange(self, r, at=(0, 0, 0), thick=0.06, bolts=8, mat="Kit_Rust",
               bolt_mat=None, rz=0.0):
        """法兰盘（r=管外径，盘半径取 1.45r）+ 螺栓头一圈。"""
        bolt_mat = bolt_mat or mat
        rf = r * 1.45
        self.cyl(rf, thick, at=at, seg=max(12, bolts * 2), mat=mat, rz=rz)
        for i in range(bolts):
            a = TAU * i / bolts
            self.cyl(0.045, thick + 0.03, at=(at[0] + math.cos(a + rz) * rf * 0.78,
                                              at[1] + math.sin(a + rz) * rf * 0.78, at[2]),
                     seg=6, mat=bolt_mat)
        return rf

    def stairs(self, at=(0, 0, 0), h=3.0, w=1.0, run=3.2, rz=0.0, mat="Kit_Rust",
               rail_mat="Kit_Rust", rail=True, steps=None):
        """直跑楼梯：踏步 + 两侧斜梁 +（可选）扶手。at = 梯段**底端**地面中心，向 +Y 上行。"""
        steps = steps or max(3, int(round(h / 0.22)))
        mtx = Matrix.Rotation(rz, 4, "Z")
        rise = h / steps
        tread = run / steps
        lng = math.hypot(h, run)                                 # 梯段斜长（给调用者做雨棚/斜撑用）
        for i in range(steps):
            z = i * rise
            y = i * tread
            p = mtx @ Vector((0.0, y + tread / 2, z))
            self.box((w, tread, rise + 0.02), (at[0] + p.x - w / 2, at[1] + p.y - tread / 2,
                                               z), mat=mat, rz=rz)
        ang = math.atan2(h, run)
        for side in (-1, 1):
            a = mtx @ Vector((side * w / 2, 0.0, 0.0))
            b = mtx @ Vector((side * w / 2, run, h))
            self.member((at[0] + a.x, at[1] + a.y, a.z), (at[0] + b.x, at[1] + b.y, b.z),
                        0.12, 0.06, mat=mat, roll=0.0)
        if rail:
            lng = math.hypot(h, run)
            for side in (-1, 1):
                a = mtx @ Vector((side * (w / 2 + 0.06), 0.0, 0.0))
                b = mtx @ Vector((side * (w / 2 + 0.06), run, h))
                self.pipe_member((at[0] + a.x, at[1] + a.y, a.z + 1.0),
                                 (at[0] + b.x, at[1] + b.y, b.z + 1.0), r=0.035, mat=rail_mat)
                for k in range(1, 4):
                    t = k / 4.0
                    p = mtx @ Vector((side * (w / 2 + 0.06), run * t, h * t))
                    self.member((at[0] + p.x, at[1] + p.y, p.z),
                                (at[0] + p.x, at[1] + p.y, p.z + 1.0), 0.06, 0.06, mat=rail_mat)
        return lng if rail else None

    def grating(self, size, at=(0, 0, 0), thick=0.05, bar=0.05, mat="Kit_Rust",
                direction="x"):
        """格栅平台板：面板 + 单向密铺筋条（视觉近似，便宜且读得出"格栅"）。"""
        w, d = size
        self.box((w, d, thick), (at[0] - w / 2, at[1] - d / 2, at[2]), mat=mat)
        if direction == "x":
            n = max(2, int(d / 0.25))
            for i in range(1, n):
                self.box((w, bar, bar), (at[0] - w / 2, at[1] - d / 2 + d * i / n, at[2] + thick),
                         mat=mat)
        else:
            n = max(2, int(w / 0.25))
            for i in range(1, n):
                self.box((bar, d, bar), (at[0] - w / 2 + w * i / n, at[1] - d / 2, at[2] + thick),
                         mat=mat)

    def tank_shell(self, r, h, at=(0, 0, 0), seg=32, mat="Kit_SteelPale", rings=4,
                   ditch=True, dome="both", mat_band=None, band_h=0.35, smooth=True):
        """立式储罐壳体：带加强环的圆筒 + 上下封头（ellipsoid 近似）。"""
        prof = []
        if dome in ("both", "bottom"):
            prof.append((0.0, -0.02 * h))
            for i in range(1, 4):
                a = math.pi / 2 * i / 4.0
                prof.append((r * math.sin(a), -0.02 * h - 0.06 * h * math.cos(a) + 0.06 * h))
        prof.append((r, 0.0))
        prof.append((r, h))
        if dome in ("both", "top"):
            for i in range(1, 4):
                a = math.pi / 2 * (1 - i / 4.0)
                prof.append((r * math.sin(a), h + 0.06 * h * math.cos(a)))
            prof.append((0.0, h + 0.06 * h))
        self.revolve(prof, at=at, seg=seg, mat=mat, smooth=smooth)
        if ditch:
            mat_band = mat_band or "Kit_RustDark"
            for i in range(1, rings + 1):
                z = h * i / float(rings + 1)
                self.revolve([(r * 0.995, z - band_h / 2), (r * 1.01, z - band_h / 2),
                              (r * 1.01, z + band_h / 2), (r * 0.995, z + band_h / 2)],
                             at=at, seg=seg, mat=mat_band, smooth=True)
        return prof

    # -- 收工 ---------------------------------------------------------------
    def to_object(self, name):
        """合成网格对象：材质槽按首次出现顺序，法线统一朝外，图元平滑标记落地。

        【材质对齐铁律】材质/平滑标记是**按面下标**平行数组给的，而 `me.validate()` 会
        **删掉退化面**（顶点重复 / 完全重合的面）—— 删面后若还按下标 `zip`，其后所有面的
        材质会整体错位（真事故：地王件的草叶重复面把草绿穿到了隔离墩上）。
        所以这里先**自己**把退化面连同它的槽名/平滑标记一起剔掉（三列同删，下标对齐不变），
        再在 validate 之后**断言面数没变**：变了就是库这一层没覆盖的退化形态，当场报错，
        不允许静默错位。
        """
        verts = self.verts
        faces, mats, smooths = [], [], []
        dropped = 0
        seen = set()
        for f, slot_name, sm in zip(self.faces, self.face_mat, self.face_smooth):
            if len(set(f)) != len(f):                                # 顶点重复 → 退化面
                dropped += 1
                continue
            key = tuple(sorted(f))
            if key in seen:                                          # 完全重合的面
                dropped += 1
                continue
            seen.add(key)
            faces.append(f)
            mats.append(slot_name)
            smooths.append(sm)
        me = bpy.data.meshes.new(name)
        me.from_pydata(verts, [], faces)
        me.validate(verbose=False)
        if len(me.polygons) != len(faces):
            raise ValueError(
                "[%s] 网格校验删面 %d 个（库侧已剔 %d 个退化面）—— 材质/平滑按下标对齐，"
                "此处必须相等；请查建模里是否有自交/零面积/重复顶点的图元"
                % (name, len(faces) - len(me.polygons), dropped))
        for slot_name in self.slot_order:
            me.materials.append(make_material(slot_name))
        index = {s: i for i, s in enumerate(self.slot_order)}
        for poly, slot_name, sm in zip(me.polygons, mats, smooths):
            poly.material_index = index[slot_name]
            poly.use_smooth = sm
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        me.update()
        if dropped:
            print("[chemplant] %s: 剔除退化面 %d 个（顶点重复/完全重合）" % (name, dropped),
                  flush=True)
        return obj


# ---------------------------------------------------------------------------
# 3. 场景/装配小工具
# ---------------------------------------------------------------------------

def reset_scene():
    """清空 factory 场景并设公制单位（幂等重跑的前提）。"""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    return scene

def apply_all_transforms():
    """把分件的位移/旋转落到数据上：装配后对象原点 = 世界原点（贴图/坐标一律世界口径）。"""
    bpy.ops.object.select_all(action="DESELECT")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for obj in meshes:
        obj.select_set(True)
    if meshes:
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.select_all(action="DESELECT")


def stats(objects, label, slot_limit=99):
    """STAT 行（面数/槽/包围盒）。整场总装件槽数天生超 8，用 slot_limit 显式放宽。"""
    return ST.print_stats(label, objects, slot_limit=slot_limit)


# ---------------------------------------------------------------------------
# 4. 自检渲染（分件脚本收工前必须看一眼；成品图在装配脚本里出）
# ---------------------------------------------------------------------------

def _enable_gpu(scene):
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for ctype in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = ctype
                prefs.get_devices()
                if any(d.type != "CPU" for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = d.type != "CPU"
                    scene.cycles.device = "GPU"
                    return ctype
            except Exception:
                continue
    except Exception:
        pass
    scene.cycles.device = "CPU"
    return "CPU"


def setup_render(res=(640, 400), samples=24, quality=88):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    dev = _enable_gpu(scene)
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.use_file_extension = False
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = quality
    scene.render.image_settings.color_mode = "RGB"
    # 铁律（同 build_scene_kit.py / render_icon.py）：Standard 视图变换，AgX 会洗掉调色板色值
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    return dev


def setup_world(bg_hex="#9AA0A6", strength=1.0):
    """中性灰世界（观感基线，同 build_scene_kit.py 的 pilot_world）。"""
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("chem_world")
    scene.world = world
    world.use_nodes = True
    node = world.node_tree.nodes.get("Background")
    rgb = ST.hex_to_rgb(bg_hex)
    node.inputs[0].default_value = (srgb_to_linear(rgb[0]), srgb_to_linear(rgb[1]),
                                    srgb_to_linear(rgb[2]), 1.0)
    node.inputs[1].default_value = strength
    return world


def add_sun(energy=3.2, rot_deg=(52.0, 0.0, 38.0), color=(1.0, 0.95, 0.86)):
    d = bpy.data.lights.new("_kit_sun", "SUN")
    d.energy = energy
    d.angle = math.radians(3.0)
    d.color = color
    o = bpy.data.objects.new("_kit_sun", d)
    o.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.scene.collection.objects.link(o)
    return o


def make_camera(loc, target, lens=50):
    cam_data = bpy.data.cameras.new("_kit_cam")
    cam_data.lens = lens
    cam_data.clip_end = 900
    cam = bpy.data.objects.new("_kit_cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def preview(objects, out_path, cam=(34, -34, 18), target=(0, 0, 5), res=(640, 400),
            samples=24, lens=50, floor=True, sun=True, bg="#9AA0A6"):
    """分件自检出图：灰底 + 日光 + 灰地板（零贴图，只看体块/轮廓/比例）。返回出图路径。"""
    scene = bpy.context.scene
    setup_render(res=res, samples=samples)
    setup_world(bg)
    tmp = []
    if floor:
        m = Mesher()
        m.box((400, 400, 0.02), at=(-200, -200, -0.02), mat="Kit_ConcreteMid")
        tmp.append(m.to_object("_kit_floor"))
    if sun:
        tmp.append(add_sun())
    cam_obj = make_camera(cam, target, lens)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    scene.render.filepath = os.path.abspath(out_path)            # use_file_extension=False：扩展名自带
    bpy.ops.render.render(write_still=True)
    for o in tmp + [cam_obj]:
        bpy.data.objects.remove(o, do_unlink=True)
    print("[chemplant] preview -> %s" % out_path, flush=True)
    return out_path
