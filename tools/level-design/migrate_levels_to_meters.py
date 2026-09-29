#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""一次性迁移：关卡资产「格 → 米」（地图资产模型统一 / 全米口径）。

背景与裁决见 docs/项目/待办/level-地图资产模型统一.md。

本脚本做两件事，逐字段核对换算（不是全局 ×2）：
  ① 把 `Assets/Data/Levels/_golden/*.json` 的格值换算成米，字段改名；
  ② 按 LevelAssetYaml.WriteLevel 的确定性规则重写 `Assets/Data/Levels/<assetName>.asset`。

换算表（1 格 = 2 米；块高 0.5 米）：
  widthTiles / depthTiles              → sizeX / sizeZ            = 格 × 2        （米）
  LevelUnit.gridX / gridY              → x / z                   = (格 + 0.5) × 2（米，格心）
  waterTileY（存档化石）                → waterWorldY             = -0.4          （米，= LevelGeometry.WaterSurfaceY）
  TerrainRaster.widthTiles / depthTiles → sizeX / sizeZ           = 格 × 2        （米）
  TerrainRaster.blocks[i]（堆叠块数）    → heights[i]             = 块 × 0.5      （米）
  TerrainRaster.blockWorldHeight       不变（本来就是米 0.5）

`_golden/*.json` 是文本锚点（评审面），`<name>.asset` 才是提交进仓库的资产；
两者的等价判据在 Assets/Tests/Battle/LevelAssetTests.cs。

用法（仓库根）：
  python tools/level-design/migrate_levels_to_meters.py
"""

import json
import math
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LEVELS_DIR = os.path.join(REPO, "pirate-crew", "Assets", "Data", "Levels")
GOLDEN_DIR = os.path.join(LEVELS_DIR, "_golden")

TILE_WORLD_SIZE = 2.0    # 1 格 = 2 米（LevelGeometry.TileWorldSize）
WATER_WORLD_Y = -0.4     # LevelGeometry.WaterSurfaceY


# ----------------------------------------------------------------------
# ① 载荷换算
# ----------------------------------------------------------------------

def migrate_payload(payload):
    """旧（格）载荷 dict → 新（米）载荷 dict（字段顺序 = C# 写出器顺序）。"""
    width_tiles = payload["widthTiles"]
    depth_tiles = payload["depthTiles"]
    raster = payload["terrain"]

    out = {}
    out["schema"] = 2
    out["kind"] = payload["kind"]
    out["levelNumber"] = payload["levelNumber"]
    out["assetName"] = payload["assetName"]
    out["displayName"] = payload["displayName"]
    out["sizeX"] = width_tiles * TILE_WORLD_SIZE
    out["sizeZ"] = depth_tiles * TILE_WORLD_SIZE
    out["waterWorldY"] = WATER_WORLD_Y

    out["airdropPool"] = payload["airdropPool"]

    units = []
    for u in payload["units"]:
        units.append({
            "typeName": u["typeName"],
            "teamIndex": u["teamIndex"],
            "x": (u["gridX"] + 0.5) * TILE_WORLD_SIZE,
            "z": (u["gridY"] + 0.5) * TILE_WORLD_SIZE,
            "luck": u["luck"],
            "initialWeapons": u["initialWeapons"],
        })
    out["units"] = units

    heights = []
    for row in raster["blocks"]:
        heights.append([b * raster["blockWorldHeight"] for b in row])

    out["terrain"] = {
        "sizeX": raster["widthTiles"] * TILE_WORLD_SIZE,
        "sizeZ": raster["depthTiles"] * TILE_WORLD_SIZE,
        "blockWorldHeight": raster["blockWorldHeight"],
        "heights": heights,
    }

    out["bakedPieces"] = payload["bakedPieces"]
    return out


# ----------------------------------------------------------------------
# ② golden JSON 写出（复刻 LevelAssetJson.Write 的排版：2 空格缩进、栅格每行一行）
# ----------------------------------------------------------------------

class Raw(object):
    """数组里整行原样输出的元素（栅格按行写出用）。"""

    def __init__(self, text):
        self.text = text


def number(value):
    """复刻 LevelAssetJson.Number：整数型不写小数点，其余最短往返形式。"""
    f = float(value)
    if f == math.floor(f) and abs(f) < 1e7:
        return str(int(f))
    return repr(f)


def json_scalar(value):
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return number(value)
    raise TypeError("不支持的标量：" + repr(value))


def json_dump(value, depth=0):
    if isinstance(value, Raw):
        return value.text
    if isinstance(value, dict):
        if not value:
            return "{}"
        parts = []
        for k, v in value.items():
            parts.append("\n" + "  " * (depth + 1) + json.dumps(k, ensure_ascii=False)
                         + ": " + json_dump(v, depth + 1))
        return "{" + ",".join(parts) + "\n" + "  " * depth + "}"
    if isinstance(value, list):
        if not value:
            return "[]"
        parts = []
        for v in value:
            parts.append("\n" + "  " * (depth + 1) + json_dump(v, depth + 1))
        return "[" + ",".join(parts) + "\n" + "  " * depth + "]"
    return json_scalar(value)


def render_golden(payload):
    """栅格 heights（每行一个数组）→ 单行原样；其余交给通用写出器。"""
    heights = payload["terrain"]["heights"]
    payload = dict(payload)
    terrain = dict(payload["terrain"])
    terrain["heights"] = [
        Raw("[" + ", ".join(number(h) for h in row) + "]") for row in heights
    ]
    payload["terrain"] = terrain
    return json_dump(payload) + "\n"


# ----------------------------------------------------------------------
# ③ .asset YAML 写出（逐条对齐 LevelAssetYaml.WriteLevel）
# ----------------------------------------------------------------------

ASSET_FILE_ID = 11400000
SCRIPT_FILE_ID = 11500000

_YAML_SENSITIVE_FIRST = set(" \t-?:[]{}#&*!|>'\"%@`")


def yaml_string(value):
    """复刻 LevelAssetYaml.FormatString。"""
    if value is None or value == "":
        return ""
    needs_quote = value[0] in _YAML_SENSITIVE_FIRST
    for c in value:
        if c in ":#\n\r\t'":
            needs_quote = True
        elif ord(c) < 0x20:
            needs_quote = True
    if not needs_quote:
        saw_digit = False
        numeric = True
        for c in value:
            if c.isdigit():
                saw_digit = True
            elif c not in "-+.eE":
                numeric = False
                break
        if numeric and saw_digit:
            needs_quote = True
    if not needs_quote:
        return value
    return "'" + value.replace("'", "''") + "'"


def yaml_scalar(value):
    if isinstance(value, str):
        return yaml_string(value)
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    return number(value)


class Map(object):
    def __init__(self, fields):
        self.fields = fields   # list[(name, value)]


class Seq(object):
    def __init__(self, items):
        self.items = items     # list[scalar | Map]


def emit_value(lines, name, value, indent):
    """写「键名: 之后」的内容；indent = 键所在缩进。"""
    head = " " * indent + name + ":"
    if isinstance(value, Map):
        lines.append(head)
        emit_map(lines, value, indent + 2)
        return
    if isinstance(value, Seq):
        if not value.items:
            lines.append(head + " []")
            return
        lines.append(head)
        emit_seq(lines, value, indent)
        return
    lines.append(head + " " + yaml_scalar(value))


def emit_map(lines, node, indent):
    for name, value in node.fields:
        emit_value(lines, name, value, indent)


def emit_seq(lines, node, indent):
    for item in node.items:
        if isinstance(item, Map):
            # 第一行贴在破折号后，其余字段缩进 +2。
            first = True
            for name, value in item.fields:
                line_indent = indent + 2
                prefix = " " * indent + "- " if first else " " * line_indent
                first = False
                if isinstance(value, Map):
                    lines.append(prefix + name + ":")
                    emit_map(lines, value, line_indent + 2)
                elif isinstance(value, Seq):
                    if not value.items:
                        lines.append(prefix + name + ": []")
                    else:
                        lines.append(prefix + name + ":")
                        emit_seq(lines, value, line_indent)
                else:
                    lines.append(prefix + name + ": " + yaml_scalar(value))
            continue
        lines.append(" " * indent + "- " + yaml_scalar(item))


def render_asset(payload, script_guid):
    lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!114 &%d" % ASSET_FILE_ID,
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: 0}",
        "  m_Enabled: 1",
        "  m_EditorHideFlags: 0",
        "  m_Script: {fileID: %d, guid: %s, type: 3}" % (SCRIPT_FILE_ID, script_guid),
        "  m_Name: " + yaml_string(payload["assetName"]),
        "  m_EditorClassIdentifier: ",
    ]

    data = Map([
        ("levelNumber", payload["levelNumber"]),
        ("assetName", payload["assetName"]),
        ("displayName", payload["displayName"]),
        ("sizeX", payload["sizeX"]),
        ("sizeZ", payload["sizeZ"]),
        ("waterWorldY", payload["waterWorldY"]),
        ("airdropPool", Seq([weapon_node(s) for s in payload["airdropPool"]])),
        ("units", Seq([unit_node(u) for u in payload["units"]])),
        ("terrain", terrain_node(payload["terrain"])),
        ("bakedPieces", Seq([
            Map([
                ("pieceId", p["pieceId"]),
                ("instanceName", p["instanceName"]),
                ("x", p["x"]),
                ("y", p["y"]),
                ("z", p["z"]),
                ("yawDeg", p["yawDeg"]),
            ]) for p in payload["bakedPieces"]
        ])),
    ])

    lines.append("  data:")
    emit_map(lines, data, 4)
    return "\n".join(lines) + "\n"


def weapon_node(stack):
    return Map([("id", stack["id"]), ("count", stack["count"])])


def unit_node(unit):
    return Map([
        ("typeName", unit["typeName"]),
        ("teamIndex", unit["teamIndex"]),
        ("x", unit["x"]),
        ("z", unit["z"]),
        ("luck", unit["luck"]),
        ("initialWeapons", Seq([weapon_node(s) for s in unit["initialWeapons"]])),
    ])


def terrain_node(raster):
    field = Map([
        ("sizeX", raster["sizeX"]),
        ("sizeZ", raster["sizeZ"]),
        ("blockWorldHeight", raster["blockWorldHeight"]),
    ])
    field.fields.append(("heights", Seq([h for row in raster["heights"] for h in row])))
    return field


# ----------------------------------------------------------------------
# ④ 驱动
# ----------------------------------------------------------------------

def read_script_guid(asset_path):
    with open(asset_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    match = re.search(r"m_Script: \{fileID: \d+, guid: ([0-9a-f]+), type: 3\}", text)
    if not match:
        raise RuntimeError("取不到 m_Script guid：" + asset_path)
    return match.group(1)


def main():
    names = sorted(
        f[:-len(".json")] for f in os.listdir(GOLDEN_DIR) if f.endswith(".json")
    )
    if not names:
        print("_golden 下没有关卡 JSON，什么都没做。")
        return 1

    for name in names:
        golden_path = os.path.join(GOLDEN_DIR, name + ".json")
        asset_path = os.path.join(LEVELS_DIR, name + ".asset")

        with open(golden_path, "r", encoding="utf-8") as handle:
            old = json.load(handle)
        if old.get("kind") != "level":
            print("跳过非 level 载荷：" + golden_path)
            continue
        if "sizeX" in old:
            print("已是米口径，跳过：" + golden_path)
            continue

        payload = migrate_payload(old)
        script_guid = read_script_guid(asset_path)

        with open(golden_path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(render_golden(payload))
        with open(asset_path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(render_asset(payload, script_guid))

        print("%-16s %sx%s 格 → %sm x %sm；units %d；heights %d"
              % (name, old["widthTiles"], old["depthTiles"],
                 payload["sizeX"], payload["sizeZ"],
                 len(payload["units"]),
                 len(payload["terrain"]["heights"]) * len(payload["terrain"]["heights"][0])))

    return 0


if __name__ == "__main__":
    sys.exit(main())
