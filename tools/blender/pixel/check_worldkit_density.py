# -*- coding: utf-8 -*-
"""
check_worldkit_density —— 对整个 WorldKit（或任意 FBX 目录）批量跑纹素密度体检并出报告。

【它回答什么问题】"这批粗模的 UV 展开到底达不达标？"——资产篇 §6 第 3 条把 32px/m ±0.5%
与每面 Jacobian < 1.01 定为硬判据；本 CLI 是它的**批量执行体**，出人读表格 + 机器可读 JSON。

【复现命令（仓库根执行）】
    "F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
        -P tools/blender/pixel/check_worldkit_density.py -- \
        --root pirate-crew/Assets/Art/Models/WorldKit --tex-size 64 \
        --out "$TEMP/pc3d-worldkit-density.json"

    （--self-test 先跑正反对照，证明判据不是空转；不带 --root 时默认用上面的 root）

【退出码】0 = 全部达标；1 = 有资产未达标（或没有任何资产可查）；2 = 用法错误

【报告口径】
    · 一个资产可能含多个网格：逐个网格算，再按面积加权聚合出该资产的密度；
    · "uvless" 列 = 没有 UV 层的网格数 —— 无 UV 时密度无从核算，直接记 problem；
    · outliers 列 = 单面密度偏离聚合值超过 ±0.5% 的面数（**只计数不判失败**：
      非均匀 UV 展开天然有分布，把它当失败会让判据失去意义；数字供人判断展开质量）。
"""

import glob
import json
import os
import sys
import tempfile
import time

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import pixel_export as PX  # noqa: E402

DEFAULT_ROOT = os.path.join("pirate-crew", "Assets", "Art", "Models", "WorldKit")


def parse_args(argv):
    import argparse
    p = argparse.ArgumentParser(prog="check_worldkit_density",
                                description="批量纹素密度体检（32px/m ±0.5% + Jacobian < 1.01）")
    p.add_argument("--root", default=DEFAULT_ROOT, help="FBX 根目录（工程内相对或绝对）")
    p.add_argument("--tex-size", type=int, default=64,
                   help="这批资产的目标贴图边长（px，POT）；密度随它换算")
    p.add_argument("--texel-target", type=float, default=PX.TARGET_PX_PER_METER)
    p.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "pc3d-worldkit-density.json"),
                   help="JSON 报告输出路径（默认系统临时目录，跑测产物不落 external/）")
    p.add_argument("--limit", type=int, default=0, help="只查前 N 件（排障用）")
    p.add_argument("--self-test", action="store_true", help="先跑正反对照自证判据")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return p.parse_args(args)


def abs_path(path):
    return path if os.path.isabs(path) else os.path.join(PX.REPO_ROOT, path)


def rel(path):
    try:
        return os.path.relpath(path, PX.REPO_ROOT)
    except ValueError:
        return path


def check_one(fbx_path, texel_size_px, target):
    """导入一件 FBX 并逐网格体检；返回 (asset_result, mesh_results)。"""
    import bpy
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    asset_name = os.path.splitext(os.path.basename(fbx_path))[0]
    objects = PX.import_fbx(fbx_path)
    if not objects:
        result = PX.DensityResult(asset_name)
        result.problems.append("%s：FBX 里没有网格" % asset_name)
        return result, []

    meshes = []
    for obj in objects:
        r = PX.check_texel_density([obj], texel_size_px=texel_size_px, target=target, name=obj.name)
        meshes.append(r)

    aggregate = PX.DensityResult(asset_name)
    aggregate.meshes = len(meshes)
    aggregate.triangles = sum(r.triangles for r in meshes)
    aggregate.uvless_meshes = sum(r.uvless_meshes for r in meshes)
    aggregate.world_area = sum(r.world_area for r in meshes)
    aggregate.uv_area = sum(r.uv_area for r in meshes)
    aggregate.jacobian_max = max([r.jacobian_max for r in meshes] or [0.0])
    aggregate.face_density_outliers = sum(r.face_density_outliers for r in meshes)

    lo = target * (1.0 - PX.DENSITY_TOLERANCE)
    hi = target * (1.0 + PX.DENSITY_TOLERANCE)
    if aggregate.world_area > 0.0 and aggregate.uvless_meshes == 0:
        aggregate.density = texel_size_px * (aggregate.uv_area / aggregate.world_area) ** 0.5
        aggregate.density_ok = lo <= aggregate.density <= hi
        if not aggregate.density_ok:
            aggregate.problems.append(
                "%s：聚合纹素密度 %.3f px/米 不在 %.3f~%.3f（目标 %.1f ±%.1f%%）"
                % (asset_name, aggregate.density, lo, hi, target, PX.DENSITY_TOLERANCE * 100.0))
    aggregate.jacobian_ok = aggregate.jacobian_max < PX.JACOBIAN_LIMIT
    if not aggregate.jacobian_ok:
        aggregate.problems.append("%s：Jacobian 长短轴比 %.4f ≥ %.4f（texel 非方形）"
                                 % (asset_name, aggregate.jacobian_max, PX.JACOBIAN_LIMIT))
    if aggregate.uvless_meshes:
        aggregate.problems.append("%s：%d/%d 个网格没有 UV 层（密度无从核算 —— 先按纹素密度展开 UV）"
                                 % (asset_name, aggregate.uvless_meshes, aggregate.meshes))
    return aggregate, meshes


def main():
    args = parse_args(sys.argv)

    if args.self_test:
        import step_asset
        code = step_asset.run_self_test()
        if code != 0:
            print("\n自证失败：判据不可信，批量体检结果不予采信。")
            return code

    root = abs_path(args.root)
    if not os.path.isdir(root):
        print("ERROR 找不到目录：%s" % root)
        return 2

    fbx_files = sorted(glob.glob(os.path.join(root, "**", "*.fbx"), recursive=True))
    if args.limit:
        fbx_files = fbx_files[:args.limit]
    if not fbx_files:
        print("ERROR %s 下没有 FBX（判据没有靶子 = 不是通过）" % rel(root))
        return 1

    print("检查 %s 下 %d 件 FBX，贴图 %dpx，目标 %.1f px/米 ±%.1f%%"
          % (rel(root), len(fbx_files), args.tex_size, args.texel_target,
             PX.DENSITY_TOLERANCE * 100.0))

    started = time.time()
    results = []
    per_mesh = {}
    for path in fbx_files:
        asset, meshes = check_one(path, args.tex_size, args.texel_target)
        results.append(asset)
        per_mesh[asset.name] = [m.to_dict() for m in meshes]

    table, bad = PX.format_density_report(results, args.tex_size, args.texel_target)
    print(table)
    uvless_assets = [r.name for r in results if r.uvless_meshes]
    print("无 UV 资产 %d/%d：%s" % (len(uvless_assets), len(results),
                                   ", ".join(uvless_assets[:8]) + ("…" if len(uvless_assets) > 8 else "")))

    report = {
        "root": rel(root),
        "generatedBy": "tools/blender/pixel/check_worldkit_density.py",
        "texelSizePx": args.tex_size,
        "targetPxPerMeter": args.texel_target,
        "tolerance": PX.DENSITY_TOLERANCE,
        "jacobianLimit": PX.JACOBIAN_LIMIT,
        "assetCount": len(results),
        "nonCompliant": bad,
        "assets": [r.to_dict() for r in results],
        "meshes": per_mesh,
    }
    out = abs_path(args.out) if not os.path.isabs(args.out) else args.out
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1, sort_keys=True)
        f.write("\n")
    print("报告 %s（%d 字节，%.1fs）" % (out, os.path.getsize(out), time.time() - started))

    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
