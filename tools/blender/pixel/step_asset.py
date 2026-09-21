# -*- coding: utf-8 -*-
"""
step_asset —— 单件资产的**像素化导出模板** CLI（资产篇 §6 三件套的端到端入口）。

【它做什么】把一件 FBX 过一遍新口径的三道工序再导回去：
    1. 烘平滑法线到顶点色（GBA=法线，R=阈值中性 0.5）           ← 反壳描边需要
    2. 量化平涂顶点色到全局调色板（OkLab 最近邻，可选）           ← 锁板需要
    3. 纹素密度校验（32px/m ±0.5% + 每面 Jacobian < 1.01）        ← 密度纪律需要
    4. 按新口径重新导出 FBX（并可选做 round-trip 实测）

【复现命令（仓库根执行）】
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/pixel/step_asset.py -- \
        --fbx pirate-crew/Assets/Art/Models/WorldKit/Props/Barrel.fbx \
        --out "$TEMP/pixel-export/Barrel_pixel.fbx" --tex-size 64 --roundtrip

    自证（不依赖任何真实资产，正反对照跑一遍判据）：
        ... -P tools/blender/pixel/step_asset.py -- --self-test

【退出码】0 = 全过；1 = 用法/资产错误；2 = 密度校验未过（--allow-density-fail 可降级为 0）
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import pixel_export as PX  # noqa: E402


def parse_args(argv):
    import argparse
    p = argparse.ArgumentParser(prog="step_asset", description="像素化导出模板（单件资产）")
    p.add_argument("--fbx", help="输入 FBX（工程内相对路径或绝对路径）")
    p.add_argument("--out", help="输出 FBX")
    p.add_argument("--tex-size", type=int, default=64, help="该资产贴图边长（px，POT）")
    p.add_argument("--texel-target", type=float, default=PX.TARGET_PX_PER_METER)
    p.add_argument("--palette", default=PX.DEFAULT_PALETTE_JSON)
    p.add_argument("--no-smooth-normals", action="store_true", help="跳过平滑法线烘焙")
    p.add_argument("--no-quantize", action="store_true", help="跳过顶点色量化")
    p.add_argument("--unwrap-uv", action="store_true",
                   help="对没有 UV 的网格做 Smart UV Project 并把密度锁到目标（存量资产的补齐步骤）")
    p.add_argument("--roundtrip", action="store_true", help="导出后重新导入，实测顶点色通道是否原样回来")
    p.add_argument("--allow-density-fail", action="store_true", help="密度不合规只报告不退非 0")
    p.add_argument("--self-test", action="store_true", help="正反对照自证判据（不需要真实资产）")
    # blender 会把 '--' 之后的参数原样传进来
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return p.parse_args(args)


def abs_path(path):
    return path if os.path.isabs(path) else os.path.join(PX.REPO_ROOT, path)


def rel(path):
    try:
        return os.path.relpath(path, PX.REPO_ROOT)
    except ValueError:
        return path


# ---------------------------------------------------------------------------
# 自证：正反对照（判据三律第 1 条）
# ---------------------------------------------------------------------------

def build_control_cube(uv_scale_u=1.0, uv_scale_v=1.0, face_meters=1.0):
    """造一个 1 米立方体，六个面各占一个 UV 方块；按 tex_size=64 反解出让密度恰好 32px/m 的方块尺寸。

    density = tex_size × √(uv_area / world_area) = 32
            ⇒ uv_area = (32/64)² × 1 = 0.25 ⇒ 方块边长 0.5（uv_scale 用来做负对照）
    """
    import bpy
    bpy.ops.mesh.primitive_cube_add(size=face_meters)
    obj = bpy.context.active_object
    obj.name = "SelfTestCube"
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    uv = mesh.uv_layers.active
    side = 0.5 * uv_scale_u
    side_v = 0.5 * uv_scale_v
    square = ((0.0, 0.0), (side, 0.0), (side, side_v), (0.0, side_v))
    for poly in mesh.polygons:
        for k, li in enumerate(poly.loop_indices):
            uv.data[li].uv = square[k % 4]
    mesh.update()
    return obj


def run_self_test():
    """正对照（密度恰好达标）必须通过；两个负对照（密度 ×3 / texel 非方形）必须被抓出来。"""
    import bpy
    failures = []

    def fresh():
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)

    # --- 正对照 ---
    fresh()
    pos = build_control_cube()
    r_pos = PX.check_texel_density([pos], texel_size_px=64)
    print("POS  density=%.4f jac_max=%.5f problems=%s" % (r_pos.density, r_pos.jacobian_max, r_pos.problems))
    if abs(r_pos.density - 32.0) > 1e-6:
        failures.append("正对照密度应恰为 32.0000，实测 %.6f" % r_pos.density)
    if r_pos.problems:
        failures.append("正对照不该有 problem：%s" % r_pos.problems)
    if not (0.999 <= r_pos.jacobian_max <= 1.01):
        failures.append("正对照 Jacobian 应≈1.0，实测 %.6f" % r_pos.jacobian_max)

    # --- 负对照 1：UV 放大 3 倍 ⇒ 密度 ×3 ---
    fresh()
    neg1 = build_control_cube(uv_scale_u=3.0, uv_scale_v=3.0)
    r_neg1 = PX.check_texel_density([neg1], texel_size_px=64)
    print("NEG1 density=%.4f jac_max=%.5f problems=%s" % (r_neg1.density, r_neg1.jacobian_max, r_neg1.problems))
    if not r_neg1.problems:
        failures.append("负对照 1（密度 ×3）没被抓出来 —— 判据无法区分，等于没判据")

    # --- 负对照 2：U 方向拉 3 倍、V 不变 ⇒ texel 非方形（Jacobian ≈3） ---
    fresh()
    neg2 = build_control_cube(uv_scale_u=3.0, uv_scale_v=1.0)
    r_neg2 = PX.check_texel_density([neg2], texel_size_px=64)
    print("NEG2 density=%.4f jac_max=%.5f problems=%s" % (r_neg2.density, r_neg2.jacobian_max, r_neg2.problems))
    if r_neg2.jacobian_max < 1.01:
        failures.append("负对照 2（texel 非方形）Jacobian 应 ≥1.01，实测 %.5f" % r_neg2.jacobian_max)
    if not any("Jacobian" in p for p in r_neg2.problems) and r_neg2.density_ok:
        failures.append("负对照 2 既没判 Jacobian 也没判密度：%s" % r_neg2.problems)

    # --- 平滑法线：硬边立方体的角顶点应得到**连续**法线（不再一分为三） ---
    fresh()
    cube = build_control_cube()
    cube.data.polygons.foreach_set("use_smooth", [False] * len(cube.data.polygons))
    rng = PX.bake_smooth_normals_to_vertex_colors(cube)
    attr = cube.data.color_attributes[PX.SMOOTH_NORMAL_ATTRIBUTE]
    # 立方体 8 个角，每个角在 3 个面上各有一个 corner ⇒ 3 个 corner 的 GBA 应完全相同
    groups = {}
    for poly in cube.data.polygons:
        for li in poly.loop_indices:
            vi = cube.data.loops[li].vertex_index
            groups.setdefault(vi, []).append(tuple(round(c, 6) for c in attr.data[li].color))
    inconsistent = [vi for vi, cols in groups.items()
                    if max(c[1:4] for c in cols) != min(c[1:4] for c in cols)]
    print("SMOOTH corner_groups=%d inconsistent=%d channel_range=%s"
          % (len(groups), len(inconsistent), rng))
    if inconsistent:
        failures.append("硬边立方体有 %d 个角顶点的 GBA 不一致（平滑法线没合并，反壳描边仍会裂）"
                        % len(inconsistent))
    if any(abs(c[0] - 0.5) > 1.5 / 255.0 for cols in groups.values() for c in cols):
        # 顶点色是 BYTE_COLOR：0.5 只能落在 127/255=0.498 或 128/255=0.502 上，
        # 偏差 ≤ 半个 8bit 步长属正常（shader 里 (c-0.5)×2 的偏移约 ±0.004，可忽略）。
        failures.append("R 通道（阈值偏移）偏离中性 0.5 超过一个 8bit 步长")

    # --- OkLab 回归 ---
    worst = PX.oklab_selftest()
    print("OKLAB worst_dev=%.6f" % worst)

    # --- 量化边界：拒绝量化 SmoothNormal ---
    try:
        PX.quantize_vertex_colors(cube, attribute=PX.SMOOTH_NORMAL_ATTRIBUTE)
        failures.append("量化器竟然接受了 SmoothNormal 属性（会把描边法线映射到板色上）")
    except ValueError as exc:
        print("GUARD ok: %s" % exc)

    # --- 量化真跑：平涂色属性 Col 送上板，输出必须 100% ∈ 板 ---
    mesh = cube.data
    flat = mesh.color_attributes.new(name=PX.FLAT_COLOR_ATTRIBUTE, type="BYTE_COLOR", domain="CORNER")
    seeds = [(0.53, 0.31, 0.19), (0.11, 0.87, 0.42), (0.62, 0.62, 0.61), (0.02, 0.05, 0.11)]
    for i, d in enumerate(flat.data):
        s = seeds[i % len(seeds)]
        d.color = (s[0], s[1], s[2], 1.0)
    stat = PX.quantize_vertex_colors(cube, PX.load_palette_lab())
    _, _, palette_rgb = PX.load_palette_lab()
    board = {tuple(c) for c in palette_rgb}
    # BYTE_COLOR 顶点色是 8bit：写 123/255 读回来可能落在 124/255（实测差 ≤1 个步长）。
    # 判据因此给一个步长的余量，否则会把"量化正确"误判成失败 —— 但也只给一个步长。
    worst_byte_delta = 0
    for d in mesh.color_attributes[PX.FLAT_COLOR_ATTRIBUTE].data:
        byte = tuple(int(round(c * 255.0)) for c in d.color[:3])
        delta = min(max(abs(byte[k] - b[k]) for k in range(3)) for b in board)
        worst_byte_delta = max(worst_byte_delta, delta)
    print("QUANT corners=%d 改动=%d 用到板色=%d 最大 OkLab 偏移=%.4f 离板最大 8bit 偏差=%d"
          % (stat["corners"], stat["changed"], stat["colors_used"], stat["worst_oklab_shift"],
             worst_byte_delta))
    if worst_byte_delta > 1:
        failures.append("量化后有 corner 的颜色离板超过 1 个 8bit 步长（最大偏差 %d）" % worst_byte_delta)
    if stat["changed"] == 0:
        failures.append("量化一个都没改动 —— 喂进去的全是板内色，用例没有区分力")

    if failures:
        print("\nSELFTEST FAILED:")
        for f in failures:
            print("  - " + f)
        return 1
    print("\nSELFTEST PASSED（正对照过 / 两个负对照被抓 / 平滑法线在硬边处连续 / 量化边界守住）")
    return 0


# ---------------------------------------------------------------------------
# 单件资产主流程
# ---------------------------------------------------------------------------

def run_step(args):
    failures_soft = 0
    if not args.fbx or not args.out:
        print("用法：--fbx <in.fbx> --out <out.fbx> [--tex-size 64] [--roundtrip]（或 --self-test）")
        return 1

    src = abs_path(args.fbx)
    if not os.path.isfile(src):
        print("ERROR 找不到输入 FBX：%s" % src)
        return 1

    worst = PX.oklab_selftest()
    print("STEP ok: Oklab 官方测试表回归 最大偏差 %.6f" % worst)

    objects = PX.import_fbx(src)
    if not objects:
        print("ERROR 导入 %s 没有得到任何网格" % rel(src))
        return 1
    print("STEP 导入 %s：%d 个网格" % (rel(src), len(objects)))

    if not args.no_smooth_normals:
        for obj in objects:
            rng = PX.bake_smooth_normals_to_vertex_colors(obj)
            print("STEP 平滑法线→顶点色 %-24s R=%.3f G=%.3f B=%.3f A=%.3f"
                  % (obj.name, rng["min"][0], rng["min"][1], rng["min"][2], rng["min"][3]))

    if not args.no_quantize:
        try:
            palette = PX.load_palette_lab(args.palette)
        except Exception as exc:                       # noqa: BLE001
            print("WARN 读不到调色板，跳过量化：%s" % exc)
            palette = None
        if palette is not None:
            for obj in objects:
                try:
                    stat = PX.quantize_vertex_colors(obj, palette)
                    print("STEP 量化顶点色 %-24s corners=%d 改动=%d 用到板色=%d 最大 OkLab 偏移=%.4f"
                          % (obj.name, stat["corners"], stat["changed"], stat["colors_used"],
                             stat["worst_oklab_shift"]))
                except KeyError as exc:
                    print("STEP 跳过量化 %-24s（%s）" % (obj.name, exc))

    results = []
    if args.unwrap_uv:
        lock = PX.unwrap_and_lock_uv_density(objects, texel_size_px=args.tex_size,
                                            target=args.texel_target)
        for name, info in sorted(lock.items()):
            print("STEP UV 展开+密度锁定 %-24s 密度 %.3f → %.3f（缩放 ×%.6f）"
                  % (name, info["before"], info["after"], info["scale"]))

    for obj in objects:
        r = PX.check_texel_density([obj], texel_size_px=args.tex_size, target=args.texel_target,
                                   name=obj.name)
        results.append(r)
    table, bad = PX.format_density_report(results, args.tex_size, args.texel_target)
    print(table)
    if bad:
        for r in results:
            for problem in r.problems:
                print("DENSITY-FAIL " + problem)
        failures_soft = 1

    out = abs_path(args.out)
    PX.export_fbx(objects, out)
    print("STEP 导出 FBX %s（%d 个对象，%d 字节）" % (rel(out), len(objects), os.path.getsize(out)))

    if args.roundtrip:
        print(roundtrip_report(out))

    if failures_soft and not args.allow_density_fail:
        print("\nRESULT 密度未达标（退出码 2）。修 UV 展开密度，或用 --allow-density-fail 只报告。")
        return 2
    print("\nRESULT 通过（平滑法线 + 量化 + 密度 + 导出）")
    return 0


def roundtrip_report(fbx_path):
    """导出→重新导入，实测顶点色通道是否原样回来（颜色空间变换会在这里露馅）。"""
    import bpy
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    objs = PX.import_fbx(fbx_path)
    lines = ["ROUNDTRIP 重新导入 %s：%d 个网格" % (rel(fbx_path), len(objs))]
    for obj in objs:
        names = [a.name for a in obj.data.color_attributes]
        lines.append("  %s color_attributes=%s" % (obj.name, names))
        for a in obj.data.color_attributes:
            lo = [1.0] * 4
            hi = [0.0] * 4
            for d in a.data:
                for i in range(4):
                    lo[i] = min(lo[i], d.color[i])
                    hi[i] = max(hi[i], d.color[i])
            lines.append("    %-16s min=%s max=%s" % (a.name,
                          tuple(round(v, 4) for v in lo), tuple(round(v, 4) for v in hi)))
    return "\n".join(lines)


def main():
    args = parse_args(sys.argv)
    if args.self_test:
        return run_self_test()
    return run_step(args)


if __name__ == "__main__":
    sys.exit(main())
