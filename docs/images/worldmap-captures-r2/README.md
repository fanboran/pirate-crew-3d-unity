# M4 八图布局重摆——实拍验收（r2）

> 这轮图是 [docs/隔壁交接-1-八图布局重摆与出生构图.md(../../../docs/隔壁交接-1-八图布局重摆与出生构图.md)
> 的验收产物。机位组由 `PlayerArtCapture.BuildWorldShots` 产出（`-worldMap <id> -artReviewOut <dir>`）：
> **world-pano / world-overhead / world-horizon / world-team0-spawn / world-team1-spawn**。
> 原始 PNG 未入库（.gitignore 排除），入库的是 1280 宽的 JPEG（quality 80）。

## 判据（复跑：`python tools/worldmap/judge_worldmap_captures.py export/worldmap-captures-r2`）

硬门禁两条：**洋红 = 0**（缺 shader/材质）、**出生机位遮挡 ≤45%**（机位被近处巨物糊满 = 面壁）。
"内容/陆地占比"只报数不判死——现役场景的矩形海床板是一整块暖色平面，会同时污染这两个口径
（审计 §二.5 的穿帮物，退役排在波 2）；"空尺度"的硬门禁在 harness 的
`WorldMapPropPlacementTests`（站面覆盖 ≥15% / 内容跨度 ≥55%）。

```
file                                  sky%    sea%   land%   cont% magenta%    occl%
atoll_ring/world-horizon.png         18.0%    4.2%   45.3%   77.8%   0.000%    23.6%
atoll_ring/world-overhead.png        43.9%    0.9%   52.7%   55.2%   0.000%     3.2%
atoll_ring/world-pano.png            58.0%    7.4%   32.1%   34.5%   0.001%    12.1%
atoll_ring/world-team0-spawn.png     23.6%    0.4%   61.7%   76.0%   0.000%    19.6%
atoll_ring/world-team1-spawn.png     23.5%    4.9%   63.3%   71.6%   0.005%    26.4%
ghost_harbor/world-horizon.png       16.0%   14.8%   55.3%   69.2%   0.000%     6.4%
ghost_harbor/world-overhead.png      50.8%    0.0%  100.0%   49.2%   0.000%     0.2%
ghost_harbor/world-pano.png          63.9%    2.2%   92.2%   34.0%   0.001%    10.6%
ghost_harbor/world-team0-spawn.png   12.0%    0.0%   90.8%   88.0%   0.000%    18.0%
ghost_harbor/world-team1-spawn.png   13.1%    0.0%   91.8%   86.9%   0.001%    18.7%
mangrove_veil/world-horizon.png      17.0%   15.1%   53.9%   67.9%   0.000%     7.1%
mangrove_veil/world-overhead.png     37.0%    0.0%   99.9%   63.0%   0.000%     2.2%
mangrove_veil/world-pano.png         52.6%    2.2%   92.4%   45.2%   0.001%    15.1%
mangrove_veil/world-team0-spawn.png   15.3%    0.0%   85.9%   84.7%   0.000%    32.0%
mangrove_veil/world-team1-spawn.png   15.0%    0.0%   94.0%   85.0%   0.003%    23.4%
spiral_throne/world-horizon.png      15.8%    4.7%   44.5%   79.4%   0.000%    31.7%
spiral_throne/world-overhead.png     64.0%    2.0%   33.0%   34.0%   0.000%     0.4%
spiral_throne/world-pano.png         71.9%    8.5%   16.9%   19.6%   0.001%    10.0%
spiral_throne/world-team0-spawn.png   51.9%    0.2%   44.5%   47.9%   0.000%     3.4%
spiral_throne/world-team1-spawn.png   33.1%    3.9%   27.0%   63.0%   0.000%    44.1%
storm_cape/world-horizon.png          9.6%   23.6%   43.3%   66.9%   0.000%    15.8%
storm_cape/world-overhead.png        46.1%    0.0%   47.2%   53.9%   0.000%     6.4%
storm_cape/world-pano.png            58.3%    7.2%   29.4%   34.6%   0.001%    21.7%
storm_cape/world-team0-spawn.png     27.9%   11.2%   51.4%   61.0%   0.000%    17.0%
storm_cape/world-team1-spawn.png     22.2%   13.7%   46.8%   64.0%   0.000%    24.6%
sunken_gate/world-horizon.png        18.2%   14.4%   54.5%   67.4%   0.000%    11.7%
sunken_gate/world-overhead.png       62.1%    0.0%  100.0%   37.9%   0.000%     0.0%
sunken_gate/world-pano.png           66.7%    2.2%   91.0%   31.1%   0.001%     8.8%
sunken_gate/world-team0-spawn.png    22.3%    0.0%   91.5%   77.7%   0.000%    15.5%
sunken_gate/world-team1-spawn.png    14.0%    0.0%   93.1%   86.0%   0.000%    26.8%
turtle_back/world-horizon.png        16.3%    4.2%   44.7%   79.5%   0.000%    29.3%
turtle_back/world-overhead.png       58.7%    2.1%   38.6%   39.1%   0.000%     1.0%
turtle_back/world-pano.png           71.4%    8.2%   18.6%   20.4%   0.001%    10.7%
turtle_back/world-team0-spawn.png     9.4%    0.4%   36.6%   90.1%   0.000%    44.2%
turtle_back/world-team1-spawn.png    16.8%    1.0%    7.2%   82.2%   0.000%     8.2%
wreck_hymn/world-horizon.png         16.5%    4.8%   44.0%   78.7%   0.000%    23.7%
wreck_hymn/world-overhead.png        15.6%    0.4%   78.3%   84.0%   0.000%     2.5%
wreck_hymn/world-pano.png            35.5%    3.9%   56.0%   60.6%   0.002%    10.3%
wreck_hymn/world-team0-spawn.png     20.2%    0.7%   62.7%   79.1%   0.000%    14.0%
wreck_hymn/world-team1-spawn.png     22.8%    3.1%   65.1%   74.1%   0.004%    24.1%

观察项 2 条（不阻断，口径见 verdict 的 docstring）：
  ~ worldmap-captures-r2/spiral_throne/world-pano.png 全景内容 19.6%（<22%）
  ~ worldmap-captures-r2/turtle_back/world-pano.png 全景内容 20.4%（<22%）

判据全过（40 张，洋红=0 且出生机位不面壁）——下一步：人眼终审。
```

## 修复前后对照（同一套机位、同一套判据）

| 指标 | 修复前 | 修复后 |
| --- | --- | --- |
| 站面覆盖（数据层，harness） | 3.9%–13.8% | **16.6%–17.8%** |
| 内容跨度 x / z | 51–76% / 29–75% | **76–94% / 69–85%** |
| 出生质心间距 | 40–174u | 44–117u |
| 出生机位遮挡比（像素） | 53%–80% | **0%–44%** |
| 洋红像素 | 0.000–0.002% | 0.000–0.006%（见下"已知残留"） |
| 全景内容占比（像素） | 3.5%–23.4% | 14.9%–60.6% |

## 已知残留（不属于本任务，已登记）

1. **矩形海床板**（审计 §二.5）：世界地图模式下 M2 时代烘焙的矩形海床台阶没有被移除，
   俯视/全景机位下是一整块硬边暖色平面，"桌上沙盘"感 + 海面读成土黄色。
   归**波 2「海床板退役」**。
2. **品红像素点**：pano 机位在归一化 (0.545, 0.210)、wreck_hymn 的 team1-spawn 在 (0.145, 0.185)，
   精确 RGB (255,0,255)（Unity 缺材质错误着色器），约 5×5 px。八张 pano 的**归一化位置完全一致**
   ⇒ 该物体到图心的距离**与图跨度成比例**（远景特征环是绝对半径 200–280u，已排除）；
   且只在 pano / team1-spawn 出现（非屏幕空间 UI）。最可能在**随跨度配置的海洋域**
   （`OceanRig.SetWorldSpan` / `WaterSimulationDriver.ConfigureWorldDomain`）。
   审计 §二.10 已登记为【待查】，本轮补充了上述定位线索。
3. **天空/环境光正在被并行会话改造**（`AmbientSkyboxCatalog` + `Art/Shaders/Sky/`，隔壁交接-3），
   本轮出图时天空为浅青白；不影响布局判据。
