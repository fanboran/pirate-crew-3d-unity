# -*- coding: utf-8 -*-
"""assemble_chemplant.py —— 第三样板关「废弃化工厂」总装 + FBX 导出 + 成品渲染。

分件脚本（`mod_*.py`，各由一名 Agent 独立产出）只管在自己的分区里建模；本脚本负责：
    ① 逐件 build → 单件网格对象（本地系）→ 按 `K.AREAS[名]['center']` 平移就位；
    ② 打印每件 + 全场的 STAT（三角面 / 材质槽 / 包围盒）；
    ③ 导出 FBX（`Assets/Art/Models/SceneKit/ChemPlant.fbx`，一场景多节点）；
    ④ 出成品图（Nishita 天空 + 日光 + 补光，零贴图平色材质，Standard 视图变换）；
    ⑤（可选）出「低分辨率档」参考图：整图按**块中心**降采到 1/N 再用最近邻放大回来 ——
       块边长 N 取 `PixelartCameraRig.PixelScaleDefault` 的值（现役 2），说明"块边长锁整数倍、
       块中心采样"这两件事。**它只是近似**：游戏内实拍另有屏幕空间描边、逐物体光带量化与
       帧级调色板（见 docs/技术/渲染/像素化着色路径/实现口径.md），本图不做色阶量化。

复现（仓库根执行，约 3~8 分钟，视采样数与机位数量）：
    B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
    "$B" -b --factory-startup -P tools/blender/scene/chemplant/assemble_chemplant.py
    # 可选参数（`--` 之后）：
    #   --only towers,tanks     只装指定分件
    #   --no-render             只装配 + 导出 FBX
    #   --no-export             只出图
    #   --samples N             采样数（默认 48）
    #   --res W                 出图宽（默认 1440，高 = W×0.625）
    #   --views hero,street     只出指定机位
    #   --pixel 3               额外出 1/3 分辨率档参考图（默认关）

产物：
    pirate-crew/Assets/Art/Models/SceneKit/ChemPlant.fbx
    docs/images/chemplant-scene/<机位>.jpg
    external/chemplant-work/chemplant_debug.blend（调参用，gitignored）
"""

import importlib
import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kit_common as K                                          # noqa: E402

T0 = time.time()

#: 曝光补偿（EV）。Standard 视图变换没有高光滚降，直射阳光下的调色板平色很容易冲成白片；
#: 用固定负曝光把"混凝土/浅灰钢"压回 #9E988A/#98A2A6 该有的读数（实测标定，勿随手改）。
EXPOSURE = -0.35

#: 分件顺序 = 装配顺序（上游先建）。名 → 模块文件 mod_<名>.py。
PARTS = ["site", "towers", "tanks", "pipes", "building", "props"]

#: 成品机位：[名, 机位, 视点, 焦距]。view 名会进文件名。
VIEWS = [
    ("hero",     (-58, -50, 34), (-2, 0, 9), 35),       # 全场 3/4 鸟瞰（西南）—— 主图
    ("overview", (46, -46, 30), (2, 2, 9), 35),         # 全场 3/4 鸟瞰（东南）
    ("street",   (-15, -6.2, 2.6), (20, -3.5, 5.0), 35),  # 主路街面（站路上向东看管廊/旁楼）
    ("north",    (-34, 52, 28), (2, 2, 9), 32),         # 厂内中层（自北向南横看管廊与罐区）
    ("towers",   (-52, -14, 26), (-15, 11, 13), 32),    # 主装置区（塔 + 烟囱 + 框架塔）
    ("tanks",    (46, 36, 24), (10, 11, 9), 40),        # 罐区（储罐 + 球罐 + 冷却塔）
    ("building", (0, -45, 11), (14, -13, 8), 32),       # 旁楼主立面（4 层楼 + 仓库）
    ("top",      (0, -6, 92), (0, 0, 0), 35),           # 俯视平面
]



def log(msg):
    print("[chemplant] %6.1fs %s" % (time.time() - T0, msg), flush=True)


def parse_args():
    opts = {"only": None, "render": True, "export": True, "samples": 48, "res": 1440,
            "views": None, "pixel": 0}
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--only":
            opts["only"] = argv[i + 1].split(","); i += 2
        elif a == "--no-render":
            opts["render"] = False; i += 1
        elif a == "--no-export":
            opts["export"] = False; i += 1
        elif a == "--samples":
            opts["samples"] = int(argv[i + 1]); i += 2
        elif a == "--res":
            opts["res"] = int(argv[i + 1]); i += 2
        elif a == "--views":
            opts["views"] = argv[i + 1].split(","); i += 2
        elif a == "--pixel":
            opts["pixel"] = int(argv[i + 1]); i += 2
        else:
            i += 1
    return opts


# ---------------------------------------------------------------------------
# ① 装配
# ---------------------------------------------------------------------------

def build_parts(only=None):
    """逐件建模 → 对象列表（对象已按分区中心平移到场地坐标）。缺件的分件会被跳过并报出。"""
    objs = []
    for name in PARTS:
        if only and name not in only:
            continue
        try:
            mod = importlib.import_module("mod_" + name)
        except ImportError as exc:
            log("!! 分件 %s 缺失（%s）——跳过" % (name, exc))
            continue
        m = K.Mesher()
        mod.build(m)
        nv, nf, nt = m.counts()
        if nt == 0:
            log("!! 分件 %s 空网格——跳过" % name)
            continue
        obj = m.to_object("ChemPlant_" + name.capitalize())
        cx, cy, cz = K.AREAS[name]["center"]
        obj.location = (cx, cy, cz)
        objs.append(obj)
        log("分件 %-9s tris=%-7d verts=%-7d slots=%d" % (name, nt, nv, len(m.slot_order)))
    return objs


# ---------------------------------------------------------------------------
# ② 成品环境（天空 + 日光 + 补光）+ 渲染
# ---------------------------------------------------------------------------

def setup_sky(scene, strength=0.30):
    """手调三段渐变天穹（地平线暖雾 → 中天灰蓝 → 天顶冷蓝），零贴图。

    为什么不用 ShaderNodeTexSky（Nishita/Hosek）：那套物理天空在 `Standard` 视图变换下
    近地平线亮度轻易越过 1.0（没有高光滚降 ⇒ 直接冲成白片），且不同 Blender 版本枚举名还会变
    （5.x 把 NISHITA 改成了 MULTIPLE_SCATTERING）。渐变天穹可预测、出图稳定，观感也够用。
    太阳角度与 `add_sun` 同源（方位 155° / 高度 34°）。
    """
    world = scene.world or bpy.data.worlds.new("chem_sky")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    mapr = nt.nodes.new("ShaderNodeMapRange")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    coord = nt.nodes.new("ShaderNodeTexCoord")

    mapr.inputs["From Min"].default_value = -0.15        # 视线方向的 Z 分量（地平线略往下起）
    mapr.inputs["From Max"].default_value = 0.75
    ramp.color_ramp.interpolation = "LINEAR"
    el0 = ramp.color_ramp.elements[0]
    el1 = ramp.color_ramp.elements[1]
    el0.position = 0.0
    el0.color = (*[K.srgb_to_linear(c) for c in ST_hex("#C4C3B6")], 1.0)   # 地平线暖雾
    el1.position = 1.0
    el1.color = (*[K.srgb_to_linear(c) for c in ST_hex("#5E7386")], 1.0)   # 天顶冷蓝
    mid = ramp.color_ramp.elements.new(0.42)
    mid.color = (*[K.srgb_to_linear(c) for c in ST_hex("#93A3AE")], 1.0)   # 中天灰蓝
    bg.inputs[1].default_value = strength
    nt.links.new(coord.outputs["Generated"], sep.inputs["Vector"])
    nt.links.new(sep.outputs["Z"], mapr.inputs["Value"])
    nt.links.new(mapr.outputs["Result"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
    return world


def ST_hex(hex_str):
    """#RRGGBB → (r, g, b) 0~1（sRGB 分量，本模块内小工具）。"""
    h = hex_str.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def add_sun(scene, energy=2.6, elev_deg=34.0, rot_deg=155.0, color=(1.0, 0.93, 0.82)):
    """与天空同源的日光：方位角取"从西南上方打过来"，让主立面与塔群有侧向长影。"""
    d = bpy.data.lights.new("chem_sun", "SUN")
    d.energy = energy
    d.angle = math.radians(2.5)
    d.color = color
    obj = bpy.data.objects.new("chem_sun", d)
    scene.collection.objects.link(obj)
    el, az = math.radians(elev_deg), math.radians(rot_deg)
    # 光来向 = 从 (sin az, cos az) 水平方向、elev 高度角射向场景
    direction = Vector((math.cos(el) * math.sin(az), math.cos(el) * math.cos(az), math.sin(el)))
    obj.location = direction * 200.0
    obj.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    return obj


def add_fill(scene, loc=(-70, -70, 60), energy=60000, size=90):
    d = bpy.data.lights.new("chem_fill", "AREA")
    d.energy = energy
    d.size = size
    d.color = (0.72, 0.80, 1.0)
    obj = bpy.data.objects.new("chem_fill", d)
    obj.location = loc
    obj.rotation_euler = (Vector((0, 0, 6)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(obj)
    return obj


def setup_render(scene, res, samples, quality=90):
    scene.render.engine = "CYCLES"
    dev = K._enable_gpu(scene)
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.caustics_reflective = False
    scene.cycles.caustics_refractive = False
    scene.render.resolution_x = res
    scene.render.resolution_y = int(res * 0.625)
    scene.render.resolution_percentage = 100
    scene.render.use_file_extension = False
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = quality
    scene.render.image_settings.color_mode = "RGB"
    # 铁律（同 build_scene_kit.py）：Standard 视图变换 —— AgX 会洗掉调色板色值
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = EXPOSURE     # Standard 无高光滚降：靠曝光把调色板压回可读区
    log("render engine=Cycles device=%s samples=%d res=%dx%d"
        % (dev, samples, scene.render.resolution_x, scene.render.resolution_y))
    return dev


def add_backdrop(scene, size=700.0, z=-0.12):
    """场外土地面（**只进渲染，不进 FBX**）：厂区是一块混凝土场地，场外是荒地。

    没有它，成品图里厂区会像一块悬空沙盘（图为用途，与资产口径无关）。
    """
    m = K.Mesher()
    m.box((size, size, 0.12), at=(-size / 2, -size / 2, z), mat="Kit_RockDark")
    m.box((size * 0.98, size * 0.98, 0.06), at=(-size * 0.49, -size * 0.49, z + 0.1),
          mat="Kit_RockMid")
    obj = m.to_object("_chem_backdrop")
    log("场外土地面 %.0f×%.0f m（仅渲染用，不导出）" % (size, size))
    return obj


def render_views(scene, out_dir, views, res, samples):
    os.makedirs(out_dir, exist_ok=True)
    for name, cam, target, lens in views:
        cam_data = bpy.data.cameras.new("chem_cam")
        cam_data.lens = lens
        cam_data.clip_start = 0.3
        cam_data.clip_end = 1200
        c = bpy.data.objects.new("chem_cam", cam_data)
        scene.collection.objects.link(c)
        c.location = Vector(cam)
        c.rotation_euler = (Vector(target) - Vector(cam)).to_track_quat("-Z", "Y").to_euler()
        scene.camera = c
        path = os.path.join(out_dir, "%s.jpg" % name)
        scene.render.filepath = os.path.abspath(path)
        bpy.ops.render.render(write_still=True)
        log("出图 %s (%d bytes)" % (path, os.path.getsize(path)))
        bpy.data.objects.remove(c, do_unlink=True)


def pixel_reference(view, out_dir, res, block):
    """低分辨率档参考图：把成品图降采到 1/block，再用最近邻放大回来（块边长 = block 屏幕像素）。

    **近似**：只说"块边长锁整数倍"这一件事；游戏内实拍另有描边/光带量化/帧级调色板。
    """
    src = os.path.join(out_dir, "%s.jpg" % view[0])
    if not os.path.exists(src):
        return None
    img = bpy.data.images.load(src)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    sw, sh = max(1, w // block), max(1, h // block)
    # 块中心采样（与像素管线的"降采 = 取块中心"口径同源）
    ys = (np.arange(sh) * block + block // 2).clip(0, h - 1)
    xs = (np.arange(sw) * block + block // 2).clip(0, w - 1)
    small = px[ys][:, xs]
    big = np.repeat(np.repeat(small, block, axis=0), block, axis=1)[:h, :w]
    out = bpy.data.images.new("pixel_ref", width=w, height=h, alpha=False)
    out.pixels.foreach_set(big.reshape(-1))
    out_path = os.path.join(out_dir, "%s-pixel%d.jpg" % (view[0], block))
    out.filepath_raw = out_path
    out.file_format = "JPEG"
    out.save()
    bpy.data.images.remove(img)
    bpy.data.images.remove(out)
    log("像素档参考 %s" % out_path)
    return out_path


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------

def verify_bounds(objs, tolerance=1.5):
    """越界校验：每件的包围盒必须落在自己分区盒 + tolerance 内（悬挑/管口允许的越界量）。

    六件由六名队员**并行**建模（互相看不见对方），越界/错位只有在这一步才会现形——
    这是分区契约的验收判据，不是可选项。
    """
    bad = 0
    # 铁律：刚设过 obj.location 之后 matrix_world 还是旧的（Blender 惰性求值），
    # 不刷新就量，量到的是本地坐标 —— 会得到满天飞的假越界。
    bpy.context.view_layer.update()
    for obj in objs:
        name = obj.name.replace("ChemPlant_", "").lower()
        area = K.AREAS.get(name)
        if area is None:
            continue
        cx, cy, cz = area["center"]
        sx, sy, sz = area["size"]
        lo = (cx - sx / 2 - tolerance, cy - sy / 2 - tolerance, cz - tolerance)
        hi = (cx + sx / 2 + tolerance, cy + sy / 2 + tolerance, cz + sz + tolerance)
        bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        mn = (min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb))
        mx = (max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb))
        over = []
        for i, axis in enumerate("XYZ"):
            if mn[i] < lo[i]:
                over.append("%s 下越界 %.2f m" % (axis, lo[i] - mn[i]))
            if mx[i] > hi[i]:
                over.append("%s 上越界 %.2f m" % (axis, mx[i] - hi[i]))
        if over:
            bad += 1
            log("!! 越界 %-9s %s（分区盒 X[%.1f,%.1f] Y[%.1f,%.1f] Z[0,%.1f]，容差 %.1f）"
                % (name, "；".join(over), lo[0], hi[0], lo[1], hi[1], sz, tolerance))
        else:
            log("越界校验 %-9s OK（bbox %.1f × %.1f × %.1f）"
                % (name, mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]))
    return bad


def main():
    o = parse_args()
    scene = K.reset_scene()
    for d in (K.WORK_DIR, K.SHOT_DIR, K.FBX_DIR):
        os.makedirs(d, exist_ok=True)

    objs = build_parts(o["only"])
    if not objs:
        raise SystemExit("没有任何分件可装 —— 先跑各 mod_*.py 的建模（preview_module.py）")
    K.stats(objs, "ChemPlant(全场)", slot_limit=99)
    total_tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in objs)
    log("总装完成：%d 个节点 / %d 三角面" % (len(objs), total_tris))
    verify_bounds(objs)

    if o["export"]:
        fbx = os.path.join(K.FBX_DIR, "ChemPlant.fbx")
        K.apply_all_transforms()
        ST_export = __import__("style_tokens").export_fbx
        ST_export(objs, fbx)
        log("FBX 导出 %s (%d bytes)" % (fbx, os.path.getsize(fbx)))

    if o["render"]:
        views = VIEWS if not o["views"] else [v for v in VIEWS if v[0] in o["views"]]
        setup_render(scene, o["res"], o["samples"])
        setup_sky(scene)
        add_sun(scene)
        add_fill(scene)
        add_backdrop(scene)
        render_views(scene, K.SHOT_DIR, views, o["res"], o["samples"])
        if o["pixel"]:
            for v in views:
                pixel_reference(v, K.SHOT_DIR, o["res"], o["pixel"])

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.WORK_DIR, "chemplant_debug.blend"))
    log("全部完成，用时 %.1fs" % (time.time() - T0))


if __name__ == "__main__":
    main()
