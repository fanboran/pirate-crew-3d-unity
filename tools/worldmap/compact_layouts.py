# -*- coding: utf-8 -*-
"""compact_layouts —— M4 布局可玩性修正（一次性迁移脚本，跑完归档）。

设计原则：双方主战场贴身（出生质心 ≤110u，1-3 跳接敌）；平台合并成连续陆块+内部高差；
装饰群岛推到外海当幕布。只动 terrain/spawns/props 三段，其余（空投/氛围/远景）保留。
"""
import io
import re

P = "pirate-crew/Assets/Scripts/PirateCrew/Battle/WorldMaps/WorldMapCatalog.cs"
s = io.open(P, encoding="utf-8").read()


def block(lines):
    return "\n".join("                " + l for l in lines)


def repl(map_id, section, new_lines, comment=None):
    global s
    pat = re.compile(r'(id: "' + map_id + r'".*?)(' + section + r': new\[\]\s*\{.*?\},)', re.S)
    header = section + ": new[]\n            {\n" + ("" if not comment else "                // " + comment + "\n")
    new = header + block(new_lines) + "\n            },"
    new2 = new.replace("\\n", "\n")
    s2, n = pat.subn(lambda m: m.group(1) + header + block(new_lines) + "\n            },", s, count=1)
    assert n == 1, map_id + " " + section
    s = s2


# 1. wreck_hymn —— 主战线连成一体（M 岛与断船/桅桥重叠成连续陆块），南线沙洲环做侧翼
repl("wreck_hymn", "terrain", [
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 55f, 75f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 95f, 75f, 0f),',
    'new WorldKitPlacement("Marine", "WreckBowHalf", 40f, 75f, 90f),',
    'new WorldKitPlacement("Marine", "WreckSternHalf", 110f, 75f, 90f),',
    'new WorldKitPlacement("Marine", "MastBridge", 75f, 75f, 90f),',
    'new WorldKitPlacement("Archipelago", "SandBarL", 55f, 62f, 315f),',
    'new WorldKitPlacement("Archipelago", "SandBarL", 95f, 62f, 45f),',
    'new WorldKitPlacement("Archipelago", "AtollCore", 75f, 45f, 0f),',
], "主战线连成一体（M 岛与断船/桅桥重叠），南线沙洲环做侧翼")
repl("wreck_hymn", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 52f, 73f, 5),',
    'new WorldMapSpawn(0, "redPirate", 58f, 77f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 55f, 79f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 92f, 73f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 98f, 77f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 95f, 79f, 2),',
])
repl("wreck_hymn", "props", [
    'new WorldPropPlacement("Campfire", 55f, 0.5f, 78f, 0f),',
    'new WorldPropPlacement("Campfire", 95f, 0.5f, 72f, 0f),',
    'new WorldPropPlacement("TreasureMound", 77.2f, 4.5f, 73.7f, 0f),',
    'new WorldPropPlacement("CannonEmplacement", 48f, 0.5f, 71f, 200f),',
    'new WorldPropPlacement("CannonEmplacement", 102f, 0.5f, 79f, 160f),',
    'new WorldPropPlacement("PalmTall", 70f, 1f, 42f, 15f),',
    'new WorldPropPlacement("Driftwood", 60f, 0.5f, 52f, 60f),',
    'new WorldPropPlacement("GrassTuft", 78f, 0.5f, 47f, 0f),',
])

# 2. atoll_ring —— 本阵（M 岛）贴上环礁带，接敌 1-2 跳；旧外侧本阵撤掉
repl("atoll_ring", "terrain", [
    'new WorldKitPlacement("Archipelago", "AtollArcA", 95f, 95f, 270f),',
    'new WorldKitPlacement("Archipelago", "AtollArcA", 95f, 95f, 0f),',
    'new WorldKitPlacement("Archipelago", "AtollArcA", 95f, 95f, 180f),',
    'new WorldKitPlacement("Archipelago", "AtollCore", 95f, 95f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 78f, 108f, 20f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 112f, 82f, 200f),',
    'new WorldKitPlacement("Archipelago", "SeaStackShort", 135f, 70f, 0f),',
    'new WorldKitPlacement("Archipelago", "SeaStackShort", 135f, 120f, 0f),',
    'new WorldKitPlacement("Archipelago", "ReefStepsA", 133f, 95f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 60f, 95f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 130f, 95f, 0f),',
], "本阵 M 岛贴环礁带（西压弧带/东压门口栈道），接敌 1-2 跳")
repl("atoll_ring", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 57f, 93f, 5),',
    'new WorldMapSpawn(0, "redPirate", 62f, 97f, 5),',
    'new WorldMapSpawn(0, "redPirate", 58f, 98f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 62f, 92f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 127f, 93f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 132f, 97f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 128f, 98f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 132f, 92f, 2),',
])

# 4. turtle_back —— 出生挪到内翼台地（88/156），外翼 L 岛成扩张地
repl("turtle_back", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 85f, 117f, 5),',
    'new WorldMapSpawn(0, "redPirate", 91f, 123f, 5),',
    'new WorldMapSpawn(0, "redPirate", 85f, 123f, 5),',
    'new WorldMapSpawn(0, "redPirate", 91f, 117f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 88f, 120f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 153f, 117f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 159f, 123f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 153f, 123f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 159f, 117f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 156f, 120f, 2),',
])

# 5. mangrove_veil —— 本阵贴迷宫西/东中段（横向接敌）
repl("mangrove_veil", "terrain", [
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 48f, 45f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 76f, 45f, 25f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 104f, 45f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 132f, 45f, 70f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 62f, 71f, 40f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 90f, 71f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 118f, 71f, 210f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 146f, 71f, 15f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 48f, 97f, 80f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 76f, 97f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 104f, 97f, 300f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 132f, 97f, 45f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 62f, 123f, 10f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 90f, 123f, 190f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 118f, 123f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 146f, 123f, 60f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 36f, 84f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 144f, 84f, 0f),',
], "本阵贴迷宫西/东中段，横向接敌")
repl("mangrove_veil", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 33f, 82f, 5),',
    'new WorldMapSpawn(0, "redPirate", 39f, 87f, 5),',
    'new WorldMapSpawn(0, "redPirate", 33f, 87f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 39f, 81f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 141f, 82f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 147f, 87f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 141f, 87f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 147f, 81f, 2),',
])

# 6. spiral_throne —— 本阵沿螺旋内收一格（保留远征身份，接敌距离减半）
repl("spiral_throne", "terrain", [
    'new WorldKitPlacement("Archipelago", "SandBarL", 95f, 200f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 118f, 193f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 133f, 190f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 163f, 164f, 0f),',
    'new WorldKitPlacement("Archipelago", "ReefStepsA", 185f, 140f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 185f, 125f, 0f),',
    'new WorldKitPlacement("Archipelago", "TurtleShellIsle", 215f, 110f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 250f, 82f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 228f, 58f, 0f),',
    'new WorldKitPlacement("Archipelago", "SandBarL", 160f, 115f, 0f),',
    'new WorldKitPlacement("Archipelago", "VolcanoRimA", 150f, 40f, 90f),',
], "本阵沿螺旋内收一格（远征图，接敌距离减半）")
repl("spiral_throne", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 115f, 191f, 5),',
    'new WorldMapSpawn(0, "redPirate", 121f, 195f, 5),',
    'new WorldMapSpawn(0, "redPirate", 115f, 196f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 121f, 190f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 225f, 56f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 231f, 60f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 225f, 61f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 231f, 55f, 2),',
])

# 7. storm_cape —— 本阵内移贴柱链两端（103u 接敌）
repl("storm_cape", "terrain", [
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 55f, 100f, 0f),',
    'new WorldKitPlacement("Archipelago", "ReefStepsA", 60f, 100f, 0f),',
    'new WorldKitPlacement("Archipelago", "SeaStackShort", 75f, 100f, 0f),',
    'new WorldKitPlacement("Marine", "PierLong", 95f, 100f, 90f),',
    'new WorldKitPlacement("Archipelago", "SeaStackShort", 110f, 100f, 0f),',
    'new WorldKitPlacement("Archipelago", "SeaStackTall", 130f, 100f, 0f),',
    'new WorldKitPlacement("Archipelago", "SeaStackShort", 140f, 100f, 0f),',
    'new WorldKitPlacement("Marine", "PierLong", 158f, 100f, 90f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 158f, 100f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 100f, 122f, 0f),',
    'new WorldKitPlacement("Marine", "LighthouseTower", 100f, 145f, 0f),',
], "本阵内移贴柱链两端（103u 接敌）")
repl("storm_cape", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 50f, 96f, 5),',
    'new WorldMapSpawn(0, "redPirate", 55f, 103f, 5),',
    'new WorldMapSpawn(0, "redPirate", 60f, 96f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 58f, 104f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 153f, 96f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 158f, 103f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 163f, 96f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 161f, 104f, 2),',
])

# 8. sunken_gate —— 本阵贴主轴两端（西岛压沙洲/礁阶/广场；东岛压心岛链）
repl("sunken_gate", "terrain", [
    'new WorldKitPlacement("Archipelago", "TerraceIslandL", 100f, 140f, 0f),',
    'new WorldKitPlacement("Archipelago", "SandBarL", 105f, 140f, 0f),',
    'new WorldKitPlacement("Archipelago", "ReefStepsA", 135f, 140f, 0f),',
    'new WorldKitPlacement("Archipelago", "SunkenPlaza", 155f, 140f, 0f),',
    'new WorldKitPlacement("Marine", "PierLong", 182f, 140f, 90f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 200f, 140f, 0f),',
    'new WorldKitPlacement("Marine", "PierHead", 225f, 140f, 0f),',
    'new WorldKitPlacement("Archipelago", "AtollCore", 240f, 140f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandM", 230f, 158f, 0f),',
    'new WorldKitPlacement("Archipelago", "TerraceIslandL", 205f, 175f, 0f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 155f, 85f, 20f),',
    'new WorldKitPlacement("Archipelago", "ReefStepsA", 155f, 108f, 90f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 200f, 75f, 0f),',
    'new WorldKitPlacement("Archipelago", "SandBarL", 180f, 80f, 90f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 155f, 195f, 200f),',
    'new WorldKitPlacement("Archipelago", "ReefStepsA", 155f, 172f, 90f),',
    'new WorldKitPlacement("Archipelago", "MangroveHummock", 200f, 205f, 40f),',
    'new WorldKitPlacement("Archipelago", "SandBarL", 180f, 200f, 90f),',
    'new WorldKitPlacement("Archipelago", "SeaStackTall", 200f, 105f, 0f),',
    'new WorldKitPlacement("Archipelago", "SeaStackTall", 200f, 175f, 0f),',
], "本阵贴主轴两端（西岛压沙洲/礁阶/广场；东岛压心岛链）")
repl("sunken_gate", "spawns", [
    'new WorldMapSpawn(0, "redPirate", 96f, 134f, 5),',
    'new WorldMapSpawn(0, "redPirate", 103f, 142f, 5),',
    'new WorldMapSpawn(0, "redPirate", 96f, 144f, 5),',
    'new WorldMapSpawn(0, "redPirate", 104f, 133f, 5),',
    'new WorldMapSpawn(0, "redPirate", 97f, 150f, 5),',
    'new WorldMapSpawn(0, "redPirateCaptain", 104f, 148f, 5),',
    'new WorldMapSpawn(1, "bluePirate", 201f, 171f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 209f, 179f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 217f, 171f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 203f, 187f, 2),',
    'new WorldMapSpawn(1, "bluePirate", 213f, 188f, 2),',
    'new WorldMapSpawn(1, "bluePirateCaptain", 221f, 180f, 2),',
])

io.open(P, "w", encoding="utf-8", newline="\n").write(s)
print("catalog compacted")
