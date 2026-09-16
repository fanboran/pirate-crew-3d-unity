# -*- coding: utf-8 -*-
"""render_icon.py —— 《海盗军团夺宝 3D》应用图标无头渲染（Blender 5.2，纯程序化，零外部素材）。

主体：低模骷髅 + 交叉骨 + 黄铜圆环徽章构图（与旧程序化图标同一主体，16px 下可读性最优）；
背景：深海蓝径向渐变背板（自发光材质，色值不受灯光污染，#14344E 中心 → #0A1D33 边缘）。
全部用 Blender 图元 + Principled/Emission 纯色节点材质，无任何贴图文件、无网络素材。

复现（仓库根 F:/VSCode/pirate-crew-3d-unity/ 执行）：
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/icon/render_icon.py

产物：
    pirate-crew/Assets/Art/Textures/AppIcon.png   1024x1024 主图标（覆盖；.meta 不动）
    export/icon-blender/previous_procedural.png    原程序化图标备份（仅首次，存在即跳过）
    export/icon-blender/preview_512.jpg            512 宽预览
    export/icon-blender/icon_16.png / icon_32.png / icon_48.png   小尺寸可读性验收图
    external/icon-blender-work/icon_debug.blend    场景缓存（gitignored，调参时用它开 GUI）

幂等：重跑直接覆盖全部产物；备份只做一次。运行约 1~3 分钟（CPU 也够）。
"""

import math
import os
import shutil
import sys
import time

import bpy
import mathutils

# ============================================================================
# 参数区 —— 协调者最可能要调的三个旋钮都在这（其余微调参数也集中在此）
# ============================================================================

RES = 1024              # 主图标边长（px）
SAMPLES = 96            # Cycles 采样数（嫌噪/嫌慢改这里）
ORTHO_SCALE = 5.2       # 正交相机取景宽度（主体显大改小、留边改大）

# 配色（sRGB hex，按项目美术语言：深海蓝 / 骨白羊皮 / 黄铜）
BONE = 0xEDE0BE         # 颅骨
BONE_AGED = 0xD8C699    # 交叉骨（比颅骨暗一档，区分前后）
SOCKET = 0x081527       # 眼窝/鼻孔/牙缝暗部
BRASS = 0xC9A227        # 圆环
BG_CENTER = 0x14344E    # 背景渐变亮端（画面上方）
BG_EDGE = 0x0A1D33      # 背景渐变暗端（画面下方）

# 构图（世界单位；相机看 -Z，屏幕右=+X，屏幕上=+Y）
RING_MAJ = 2.32         # 黄铜圆环主半径
RING_MIN = 0.085        # 圆环管径
BONE_ANGLE = 38.0       # 交叉骨与水平线夹角（度）
BONE_HALF = 2.05        # 交叉骨半长

# ============================================================================
# 路径
# ============================================================================

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
APP_ICON = os.path.join(ROOT, 'pirate-crew', 'Assets', 'Art', 'Textures', 'AppIcon.png')
EXPORT = os.path.join(ROOT, 'export', 'icon-blender')
WORK = os.path.join(ROOT, 'external', 'icon-blender-work')

T0 = time.time()


def log(msg):
    print('[icon] %-6.1fs %s' % (time.time() - T0, msg), flush=True)


# ============================================================================
# 小工具
# ============================================================================

def srgb(hexv):
    """0xRRGGBB → 线性 RGBA（Blender 节点吃线性值，不转会被冲淡）。"""
    def f(v):
        v = v / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (hexv >> 16) & 0xFF, (hexv >> 8) & 0xFF, hexv & 0xFF
    return (f(r), f(g), f(b), 1.0)


def deselect_all():
    for o in bpy.context.scene.objects:
        o.select_set(False)


def add_primitive(op, name, **kw):
    """加一个图元并返回其对象（保持场景选择干净）。"""
    deselect_all()
    op(**kw)
    obj = bpy.context.active_object
    obj.name = name
    return obj


def sphere(name, radius, loc, segments=64, rings=32):
    return add_primitive(bpy.ops.mesh.primitive_uv_sphere_add, name,
                         radius=radius, segments=segments, ring_count=rings, location=loc)


def box(name, half_ext, loc, bevel=0.0, bevel_seg=3):
    obj = add_primitive(bpy.ops.mesh.primitive_cube_add, name, size=2.0, location=loc)
    obj.scale = half_ext
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        m = obj.modifiers.new('bevel', 'BEVEL')
        m.width = bevel
        m.segments = bevel_seg
        m.limit_method = 'ANGLE'
        smooth_by_angle(obj, math.radians(60))
    return obj


def cone(name, radius, depth, loc, verts=3, apex_down=True):
    """细锥（默认顶点朝下，做鼻孔布尔刀/暗部填充）。"""
    obj = add_primitive(bpy.ops.mesh.primitive_cone_add, name,
                        vertices=verts, radius1=radius, radius2=0.0, depth=depth, location=loc)
    if apex_down:
        obj.rotation_euler = (math.pi, 0, 0)
    return obj


def cylinder(name, radius, depth, loc, direction=None):
    """圆柱；direction 给定则 +Z 轴对齐该方向。"""
    obj = add_primitive(bpy.ops.mesh.primitive_cylinder_add, name,
                        vertices=48, radius=radius, depth=depth, location=loc)
    if direction:
        d = mathutils.Vector(direction)
        if d.length > 1e-6:
            obj.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    return obj


def smooth_by_angle(obj, angle):
    """4.1+ 的按角平滑：能用 op 就用，失败则退化为全平滑（低模下可接受）。"""
    for p in obj.data.polygons:
        p.use_smooth = True
    for opname, kw in (('object.shade_auto_smooth', {'angle': angle}),
                       ('object.shade_smooth_by_angle', {'angle': angle})):
        try:
            op = getattr(bpy.ops.object, opname.split('.')[1])
            deselect_all()
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            op(**kw)
            return
        except Exception:
            continue


def bool_cut(target, cutters):
    """对 target 做布尔差集（精确解）并删掉刀体。"""
    for i, c in enumerate(cutters):
        m = target.modifiers.new('bcut%d' % i, 'BOOLEAN')
        m.operation = 'DIFFERENCE'
        m.solver = 'EXACT'
        m.object = c
    deselect_all()
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    for m in list(target.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)


def flat_mat(name, color, rough=0.5, metal=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = srgb(color)
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Metallic'].default_value = metal
    return mat


def aim(obj, target):
    d = mathutils.Vector(target) - obj.location
    obj.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


# ============================================================================
# 材质
# ============================================================================

MAT_SKULL = None
MAT_BONE = None
MAT_DARK = None
MAT_BRASS = None


def make_materials():
    global MAT_SKULL, MAT_BONE, MAT_DARK, MAT_BRASS
    MAT_SKULL = flat_mat('icon_skull', BONE, rough=0.50)
    MAT_BONE = flat_mat('icon_bone', BONE_AGED, rough=0.55)
    MAT_DARK = flat_mat('icon_socket', SOCKET, rough=0.9)
    MAT_BRASS = flat_mat('icon_brass', BRASS, rough=0.32, metal=1.0)


def make_backdrop():
    """背景大板：自发光竖直线性渐变（上亮 #14344E → 下暗 #0A1D33，同平面版图标）。
    注意别改回径向渐变：亮心会被骷髅+圆环完全挡住，可见背景环带只剩渐变末端，等于纯色。"""
    mat = bpy.data.materials.new('icon_bg')
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')

    tc = nt.nodes.new('ShaderNodeTexCoord')                     # Generated: 0..1
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    mr = nt.nodes.new('ShaderNodeMapRange')                     # Y(0..1) → Fac
    mr.inputs['From Min'].default_value = 0.0
    mr.inputs['From Max'].default_value = 1.0
    mr.clamp = True
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].color = srgb(BG_EDGE)           # Y=0 底部：暗
    ramp.color_ramp.elements[1].color = srgb(BG_CENTER)         # Y=1 顶部：亮
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Strength'].default_value = 1.0

    nt.links.new(tc.outputs['Generated'], sep.inputs[0])
    nt.links.new(sep.outputs['Y'], mr.inputs['Value'])
    nt.links.new(mr.outputs[0], ramp.inputs['Fac'])
    nt.links.new(ramp.outputs['Color'], em.inputs['Color'])
    nt.links.new(em.outputs[0], out.inputs['Surface'])

    plane = add_primitive(bpy.ops.mesh.primitive_plane_add, 'backdrop',
                          size=1.0, location=(0, 0, -3.2))
    plane.scale = (7.0, 7.0, 1.0)
    plane.data.materials.append(mat)
    return plane


# ============================================================================
# 主体建模
# ============================================================================

def make_ring():
    """黄铜圆环（徽章描边），在主体后方。"""
    ring = add_primitive(bpy.ops.mesh.primitive_torus_add, 'brass_ring',
                         major_radius=RING_MAJ, minor_radius=RING_MIN,
                         major_segments=96, minor_segments=24,
                         location=(0, -0.10, -1.6))
    ring.data.materials.append(MAT_BRASS)
    smooth_by_angle(ring, math.radians(60))
    return ring


def make_bone(name, angle_deg, center, z):
    """一根骨头：圆柱骨干 + 每端双球凸起（经典卡通骨），绕 Z 轴转 angle_deg。"""
    a = math.radians(angle_deg)
    d = mathutils.Vector((math.cos(a), math.sin(a), 0.0))
    p = mathutils.Vector((-math.sin(a), math.cos(a), 0.0))
    parts = []
    shaft = cylinder(name + '_shaft', radius=0.125, depth=BONE_HALF * 2,
                     loc=(center[0], center[1], z), direction=d)
    parts.append(shaft)
    for sgn in (1, -1):
        for koff in (0.135, -0.135):
            tip = (center[0] + d.x * sgn * (BONE_HALF - 0.12) + p.x * koff,
                   center[1] + d.y * sgn * (BONE_HALF - 0.12) + p.y * koff,
                   z)
            parts.append(sphere('%s_knob%d%d' % (name, sgn, koff > 0), 0.165, tip))
    for obj in parts:
        obj.data.materials.append(MAT_BONE)
        smooth_by_angle(obj, math.radians(60))
    return parts


def make_skull():
    """颅骨（球体布尔挖眼窝/鼻孔）+ 下颌 + 嘴腔暗板 + 四颗门牙。"""
    # 颅骨本体：X 宽 1.0 / Y 高 0.92 / Z 厚 0.82 的椭球，中心略抬高
    cr = sphere('skull_cranium', 1.0, (0, 0.15, 0))
    cr.scale = (1.0, 0.92, 0.82)
    bpy.ops.object.transform_apply(scale=True)
    # 眼窝刀（两个球）
    cutters = [sphere('cutter_eye%d' % sgn, 0.29, (0.37 * sgn, 0.40, 0.55)) for sgn in (1, -1)]
    # 鼻孔刀（三角锥，顶点朝下）
    cutters.append(cone('cutter_nose', 0.17, 0.36, (0, 0.02, 0.68)))
    bool_cut(cr, cutters)
    cr.data.materials.append(MAT_SKULL)
    smooth_by_angle(cr, math.radians(60))

    # 眼窝/鼻孔暗部填充（凹进去一点，保深度感）
    for sgn in (1, -1):
        f = sphere('socket_fill%d' % sgn, 0.235, (0.37 * sgn, 0.40, 0.42))
        f.data.materials.append(MAT_DARK)
    nf = cone('nose_fill', 0.125, 0.30, (0, 0.02, 0.60))
    nf.data.materials.append(MAT_DARK)

    # 下颌（圆角盒，藏进颅骨下沿、向前突出）
    jaw = box('skull_jaw', (0.50, 0.30, 0.28), (0, -0.72, 0.18), bevel=0.10)
    jaw.data.materials.append(MAT_SKULL)

    # 牙缝：三条暗色细条贴在下颌前面（骨色下颌 + 暗缝 = 四颗门牙，同平面版画法）
    for i, x in enumerate((-0.18, 0.0, 0.18)):
        g = box('tooth_gap%d' % i, (0.022, 0.17, 0.016), (x, -0.66, 0.47))
        g.data.materials.append(MAT_DARK)


def make_lights_and_camera(scene):
    """三点布光：暖主光(左上) + 冷辅光(右) + 逆光轮廓(后上)。"""
    def area(name, size, loc, energy, color, target):
        d = bpy.data.lights.new(name, 'AREA')
        d.size = size
        d.energy = energy
        d.color = color
        o = bpy.data.objects.new(name, d)
        scene.collection.objects.link(o)
        o.location = loc
        aim(o, target)
        return o

    # 能量按 PIL 判色校准：主光过强会把颅骨大面推到纯白（丢骨白偏奶油的色调）
    area('key', 5.0, (-3.6, 2.6, 4.6), 430, (1.0, 0.95, 0.86), (0, 0.1, 0))
    area('fill', 6.0, (4.6, -0.8, 2.6), 220, (0.55, 0.72, 1.0), (0, 0, 0))
    area('rim', 3.5, (0.6, 3.4, -4.4), 1250, (1.0, 0.88, 0.70), (0, 0.2, 0))
    # 低位暖补光（模拟甲板反光），托起圆环底部与下颌阴影，避免环底隐入背景
    area('bounce', 4.0, (0.5, -4.2, 1.0), 60, (1.0, 0.88, 0.66), (0, -0.2, 0))

    cam_data = bpy.data.cameras.new('icon_cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ORTHO_SCALE
    cam = bpy.data.objects.new('icon_cam', cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0, -0.10, 7.0)
    cam.rotation_euler = (0, 0, 0)
    scene.camera = cam


# ============================================================================
# 渲染与后处理
# ============================================================================

def enable_gpu():
    """尝试 OPTIX/CUDA，失败回退 CPU（无头环境保证能跑）。"""
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for ctype in ('OPTIX', 'CUDA'):
            try:
                prefs.compute_device_type = ctype
                prefs.get_devices()
                found = False
                for dev in prefs.devices:
                    dev.use = dev.type != 'CPU'
                    found = found or dev.use
                if found:
                    return ctype
            except Exception:
                continue
    except Exception:
        pass
    return None


def setup_render(scene):
    scene.render.engine = 'CYCLES'
    gpu = enable_gpu()
    scene.cycles.device = 'GPU' if gpu else 'CPU'
    scene.cycles.samples = SAMPLES
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RES
    scene.render.resolution_y = RES
    scene.render.resolution_percentage = 100
    scene.render.filepath = APP_ICON
    scene.render.use_file_extension = False
    ims = scene.render.image_settings
    ims.file_format = 'PNG'
    ims.color_mode = 'RGB'
    ims.color_depth = '8'
    ims.compression = 15
    # 铁律：Standard 视图变换，否则 AgX 会把 #0A1D33/#C9A227 这些色值洗掉
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    log('engine=Cycles device=%s samples=%d' % (gpu or 'CPU', SAMPLES))


def derive_image(src, dst, w, h, fmt):
    """从 src 图缩放出一张 w*h 的副本（每次重新 load，scale 是破坏性的）。"""
    img = bpy.data.images.load(src)
    img.scale(w, h)
    img.filepath_raw = dst
    img.file_format = fmt
    img.save()
    bpy.data.images.remove(img)


# ============================================================================
# 主流程
# ============================================================================

def main():
    os.makedirs(EXPORT, exist_ok=True)
    os.makedirs(WORK, exist_ok=True)

    # 1) 备份原程序化图标（只在首次；重跑不覆盖备份）
    if os.path.exists(APP_ICON) and not os.path.exists(os.path.join(EXPORT, 'previous_procedural.png')):
        shutil.copy2(APP_ICON, os.path.join(EXPORT, 'previous_procedural.png'))
        log('backed up original icon -> export/icon-blender/previous_procedural.png')

    # 2) 清空 factory 场景（-b 启动自带一个 Cube 等，防御性清一遍保证幂等）
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene

    # 3) 世界环境（给金属一点环境反射底色）
    world = bpy.data.worlds.new('icon_world')
    scene.world = world
    world.use_nodes = True
    wbg = world.node_tree.nodes['Background']
    wbg.inputs[0].default_value = srgb(BG_EDGE)
    wbg.inputs[1].default_value = 0.35

    # 4) 建场景
    make_materials()
    make_backdrop()
    make_ring()
    make_bone('bone_a', BONE_ANGLE, (0, -0.30), -0.5)
    make_bone('bone_b', -BONE_ANGLE, (0, -0.30), -0.5)
    make_skull()
    make_lights_and_camera(scene)
    log('scene built')

    # 5) 渲染（直接写 Assets 目标路径）
    setup_render(scene)
    bpy.ops.render.render(write_still=True)
    if not (os.path.exists(APP_ICON) and os.path.getsize(APP_ICON) > 0):
        log('FATAL: render output missing: %s' % APP_ICON)
        sys.exit(1)
    log('rendered %dx%d -> %s (%d bytes)' % (RES, RES, APP_ICON, os.path.getsize(APP_ICON)))

    # 6) 派生预览与缩略图
    derive_image(APP_ICON, os.path.join(EXPORT, 'preview_512.jpg'), 512, 512, 'JPEG')
    for px in (16, 32, 48):
        derive_image(APP_ICON, os.path.join(EXPORT, 'icon_%d.png' % px), px, px, 'PNG')
    log('preview + thumbnails written to export/icon-blender/')

    # 7) 场景缓存（调参时可开 GUI 直接看）
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, 'icon_debug.blend'))
    log('done in %.1fs total' % (time.time() - T0))


if __name__ == '__main__':
    main()
