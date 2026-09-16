# -*- coding: utf-8 -*-
"""
sync_standables —— 从 <Asset>.standable.json 生成 WorldMapStandables.cs（唯一事实源同步工具）。

用法（仓库根）：
    python tools/blender/scene/sync_standables.py

输入：pirate-crew/Assets/Art/Models/WorldKit/<Kit>/<Asset>.standable.json
输出：pirate-crew/Assets/Scripts/PirateCrew/Battle/WorldMaps/WorldMapStandables.cs

坐标转换（Blender Z-up → Unity Y-up，与 ST.export_fbx 的 axis_forward='-Z'/axis_up='Y'
+ Unity bakeAxisConversion 一致）：
    Unity X = Blender X
    Unity Z = -Blender Y        （镜像轴；船艏朝 Blender -Y → Unity +Z，见 build_scene_kit README）
    TopY   = Blender Z（c[2] + s[2]/2）
    Yaw    = Blender yaw 同值    （镜像 M·R(θ)·M⁻¹ = R(θ)，镜像恰好抵消旋转方向翻转）
    box 尺寸不变（s[0]→Size.x，s[1]→Size.z）。

纪律：本脚本产物为 GENERATED 文件，手改会被下次同步覆盖；改数据请改 manifest/建模脚本。
"""

import glob
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
WORLDKIT_DIR = os.path.join(ROOT, "pirate-crew", "Assets", "Art", "Models", "WorldKit")
OUT_PATH = os.path.join(
    ROOT, "pirate-crew", "Assets", "Scripts", "PirateCrew", "Battle",
    "WorldMaps", "WorldMapStandables.cs")

HEADER = """\
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界套件（WorldKit）可站立面转写表 —— GENERATED 文件，勿手改。
    ///
    /// 由 <c>tools/blender/scene/sync_standables.py</c> 从
    /// <c>Assets/Art/Models/WorldKit/&lt;Kit&gt;/&lt;Asset&gt;.standable.json</c> 生成
    /// （manifest 是建模脚本的直接产物，本表与它逐字同步）。
    ///
    /// 【box 语义】资产本地系（Y-up，米）：矩形中心 = <see cref="WorldStandBox.Center"/>（资产系），
    /// 矩形自身绕其中心转 <see cref="WorldStandBox.YawDeg"/>；顶面高度 = <see cref="WorldStandBox.TopY"/>
    /// （0.5 档）。摆放进地图时：世界矩形中心 = placement.Position + R(placement.Yaw)·Center，
    /// 总转角 = placement.Yaw + box.Yaw。
    /// 【坐标转换】Unity (X,Z) = (Blender X, −Blender Y)，TopY = Blender Z，Yaw 同值。
    /// </summary>
    public static class WorldMapStandables
    {
        /// <summary>该资产是否有站面数据（false = 纯视觉件）。</summary>
        public static bool Has(string asset)
        {
            return Table.ContainsKey(asset);
        }

        /// <summary>取资产站面 box 表；未收录返回 null（调用方按纯视觉处理）。</summary>
        public static IReadOnlyList<WorldStandBox> BoxesOf(string asset)
        {
            return Table.TryGetValue(asset, out var boxes) ? boxes : null;
        }

        // ------------------------------------------------------------------
        // 各资产站面（按 kit 目录分组，组内按资产名排序）
        // ------------------------------------------------------------------
"""

FOOTER = """    }
}
"""


def fmt(x):
    if abs(x) < 5e-9:
        x = 0.0  # 归一 -0
    s = "%.2f" % x
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    return s + "f"


def main():
    kits = sorted(d for d in os.listdir(WORLDKIT_DIR)
                  if os.path.isdir(os.path.join(WORLDKIT_DIR, d)))
    body = []
    table_entries = []
    for kit in kits:
        jsons = sorted(glob.glob(os.path.join(WORLDKIT_DIR, kit, "*.standable.json")))
        if not jsons:
            continue
        body.append("        // %s（tools/blender/scene/%s/）\n" % (kit.lower(), kit.lower()))
        for jp in jsons:
            with open(jp, encoding="utf-8") as f:
                data = json.load(f)
            asset = data["asset"]
            boxes = []
            for b in data["boxes"]:
                c, s = b["c"], b["s"]
                top = c[2] + s[2] / 2.0
                yaw = float(b.get("yaw", 0.0))
                boxes.append((c[0], -c[1], s[0], s[1], top, yaw))
            if not boxes:
                continue  # 纯视觉件（manifest 为空表）
            field = "_%s" % asset
            body.append("        static readonly WorldStandBox[] %s =\n        {\n" % field)
            for (cx, cz, sx, sz, top, yaw) in boxes:
                yaw_part = ", %s" % fmt(yaw) if abs(yaw) > 1e-9 else ""
                body.append("            new WorldStandBox(%s, %s, %s, %s, %s%s),\n"
                            % (fmt(cx), fmt(cz), fmt(sx), fmt(sz), fmt(top), yaw_part))
            body.append("        };\n\n")
            table_entries.append('                { "%s", %s },' % (asset, field))
        body.append("\n")

    table = (
        "        static readonly Dictionary<string, WorldStandBox[]> Table =\n"
        "            new Dictionary<string, WorldStandBox[]>\n            {\n"
        + "\n".join(table_entries)
        + "\n            };\n"
    )

    content = HEADER + "".join(body) + table + "\n" + FOOTER
    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(content)
    print("OK %s: %d assets, %d boxes"
          % (os.path.relpath(OUT_PATH, ROOT), len(table_entries),
             sum(1 for line in content.splitlines() if "new WorldStandBox(" in line)))


if __name__ == "__main__":
    main()
