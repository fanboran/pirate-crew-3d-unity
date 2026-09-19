# 天空盒环境光三档实拍（视觉遗留 #6 翻转验收）

> **本目录是什么**：环境光从 Trilight 三色切换为天空盒驱动（`AmbientSkyboxCatalog.DefaultAmbientSource
> = Skybox`，2026-09-17 用户拍板翻转）后的三档氛围实拍对比图，供用户终审。
> 数值口径见 [`../../docs/技术/环境光天空盒化-预研与接线清单.md`(../../../docs/技术/环境光天空盒化-预研与接线清单.md) §四
> （全部【提案/待定】）；程序化判据见 `tools/ambient/judge_ambient_captures.py`（初筛），人眼看图为终审。
>
> 原始 PNG 不入库（.gitignore），入库的是 1280 宽 JPEG（同 art-review / worldmap-captures-r2 惯例）。

## 拍摄条件

- 播放器：`external/build/PirateCrew3D.exe`（`-buildWindows64Player` 重建，构建日志 grep
  "shader error" 必须 0 条）。
- 地图：`wreck_hymn`（搁浅圣母号，布局 r2 最新）；机位组 = `PlayerArtCapture.BuildWorldShots`
  （全景斜 45° / 正俯瞰 / 海平线远眺 / 双方出生近景）。海平线远眺即"逆光位"（朝太阳方向看）。
- 三档强制：命令行 `-ambientTimeOfDay Noon|Dusk|Overcast`（`AmbientDirector` 的覆盖开关，优先级
  高于地图档；Storm 为 Overcast 别名）。

## 每张图应看到什么（预期）

| 档 | 天顶 | 地平线 | 太阳盘 | 判据要点 |
| --- | --- | --- | --- | --- |
| noon（正午） | 干净蓝 `#4DA6D9` | 亮白蓝 `#B0D4F1`（=雾色） | 有（角半径 0.045 rad，强度 2.0 喂 Bloom） | 天顶偏冷（B≥R）、亮度三档最高、上缘不见绿色带 |
| dusk（黄昏） | 深蓝 `#3E7FB5` | 暖橙 `#F2B27A` | 有、更大更柔（0.065 rad / 2.6） | 地平线带偏暖（R>B）、日盘比正午大 |
| overcast（阴云） | 铅灰蓝 `#244A72` | 灰 `#9A8E86` | **无**（乌云蔽日） | 通道极差明显小于正午（低饱和）、亮度三档最低、无成片过曝亮斑 |

三档共同：海平线处天与雾无缝（地平线色=雾色契约）；远景岛淡出的终点与天空衔接；
水面反射色随天空盒变化（`PirateOcean` 的 `SampleSH(reflectDirWS)`）。

## 程序化判据（初筛，阈值【提案/待定】）

```
python tools/ambient/judge_ambient_captures.py export/ambient-skybox-debug
```

① 洋红 ≤0.05%（硬门禁）② horizon 图天空上带亮度 >0.15（抓"天空盒 Pass 被静默丢弃"→黑背景）
③ 三档全图亮度严格单调 noon > dusk > overcast ④ dusk 地平线带 R>B、noon 天顶 B≥R（渐变方向）
⑤ noon/dusk 有太阳盘亮斑、overcast 无 ⑥ overcast 天顶带通道极差 < noon（铅灰 vs 干净蓝）。

## 复现

```bash
# 1) 资产与场景（编辑器必须关闭；四步链顺序见 docs/项目/交接与恢复指南.md §23）
Unity.exe -batchmode -nographics -quit -projectPath .../pirate-crew \
  -executeMethod PirateCrew.EditorTools.SkyAssetBuilder.BuildAll -logFile -
# 2) 四步装配链（M2BattleSceneSetup.BuildAll → WireMinimap → M3SceneSetup.BuildAll
#    → WorldMapAssetSetBuilder.BuildAll）
# 3) 重建播放器（grep "shader error" 必须 0）
# 4) 三档出图（各跑一次播放器）
external/build/PirateCrew3D.exe -worldMap wreck_hymn -ambientTimeOfDay Noon     -artReviewOut docs/images/ambient-skybox-debug/noon
external/build/PirateCrew3D.exe -worldMap wreck_hymn -ambientTimeOfDay Dusk     -artReviewOut docs/images/ambient-skybox-debug/dusk
external/build/PirateCrew3D.exe -worldMap wreck_hymn -ambientTimeOfDay Overcast -artReviewOut docs/images/ambient-skybox-debug/overcast
# 5) 判据初筛 → 压缩入库 → 人眼终审
python tools/ambient/judge_ambient_captures.py export/ambient-skybox-debug
```

## 实测结果（2026-09-18，`wreck_hymn`，播放器 2022.3.62f1c1 / d3d11）

**门禁**：构建日志 shader error 0 条（首建曾抓到 `PirateGradientSky` 的
`_MainLightPosition` 重定义——该错误只在真实图形 API 的变体编译暴露，-nographics 导入不报，
已修：URP `Input.hlsl:101` 已声明、本 shader 不再重复声明）；EditMode 全量 1307/0/1。

**程序化判据（`tools/ambient/judge_ambient_captures.py`）——全部通过**：

| 判据 | 结果 |
| --- | --- |
| 洋红占比（硬门禁） | 全图 ≤0.0037%（阈值 0.05%）✓ |
| 天空非黑（抓 Pass 被丢弃） | horizon 天带上带亮度 0.30–0.62（阈值 0.15）✓ |
| 三档亮度严格单调 | noon 0.648 > dusk 0.571 > overcast 0.398 ✓ |
| 渐变方向 | noon 天顶偏冷（B>R，R-B=-0.40）、dusk 地平线带偏暖（R-B=+0.054）✓ |
| 阴云低饱和 | 天顶带通道极差 overcast 0.23 < noon 0.57 ✓ |
| 阴云无太阳盘 | overcast 全图无成片过曝亮斑 ✓ |

**人眼可见结论**（初筛之外）：三档分得开且方向正确——正午干净蓝、黄昏"深蓝天顶→暖橙地平线"
（岛体木纹吃暖光，观感最好）、阴云整体铅灰压暗；下半球暖反弹在场（岛底/背光面不死黑）；
海平线处天与雾无接缝（地平线=雾色契约生效）。

**两项提示（不阻断）**：
1. **太阳盘未入画**：现 world-horizon 机位不朝太阳方位（-40°），noon/dusk 的日盘不在画面内，
   判据降级为提示；要终审日盘需给 `PlayerArtCapture.BuildWorldShots` 加一个朝阳机位（实拍轮待办）。
2. **⚠ 海面不可见（跨域发现，A–F 实拍的阻塞项）**：三档图里海面均未绘制，天空盒下半球直接露出
   （俯视图里岛屿悬在均匀的地面回照色上）。海床板消失是 `542d71c` 的**有意退役**，但海面网格
   （OceanRig，代码路径无静默跳过）也不可见，且 Player.log 0 错误、-oceanDebug 档无变化。
   r2（`9ab372e`）之后无人出过图，候选引入提交：`542d71c` / `11b3920` / `fcda283`；
   与环境光翻转的关系未排除（未找到机制），归因方法=把 `DefaultAmbientSource` 临时改回
   Trilight 重建对照（一次构建可裁）。**该问题须由水域域认领修复后，本目录的海平线衔接判据才算数。**
