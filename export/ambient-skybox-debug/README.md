# 天空盒环境光三档实拍（视觉遗留 #6 翻转验收）

> **本目录是什么**：环境光从 Trilight 三色切换为天空盒驱动（`AmbientSkyboxCatalog.DefaultAmbientSource
> = Skybox`，2026-09-17 用户拍板翻转）后的三档氛围实拍对比图，供用户终审。
> 数值口径见 [`../../docs/环境光天空盒化-预研与接线清单.md`](../../docs/环境光天空盒化-预研与接线清单.md) §四
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
# 1) 资产与场景（编辑器必须关闭；四步链顺序见 docs/交接与恢复指南.md §23）
Unity.exe -batchmode -nographics -quit -projectPath .../pirate-crew \
  -executeMethod PirateCrew.EditorTools.SkyAssetBuilder.BuildAll -logFile -
# 2) 四步装配链（M2BattleSceneSetup.BuildAll → WireMinimap → M3SceneSetup.BuildAll
#    → WorldMapAssetSetBuilder.BuildAll）
# 3) 重建播放器（grep "shader error" 必须 0）
# 4) 三档出图（各跑一次播放器）
external/build/PirateCrew3D.exe -worldMap wreck_hymn -ambientTimeOfDay Noon     -artReviewOut export/ambient-skybox-debug/noon
external/build/PirateCrew3D.exe -worldMap wreck_hymn -ambientTimeOfDay Dusk     -artReviewOut export/ambient-skybox-debug/dusk
external/build/PirateCrew3D.exe -worldMap wreck_hymn -ambientTimeOfDay Overcast -artReviewOut export/ambient-skybox-debug/overcast
# 5) 判据初筛 → 压缩入库 → 人眼终审
python tools/ambient/judge_ambient_captures.py export/ambient-skybox-debug
```

## 实测结果

（待出图后回填：判据输出摘要 + 每档一张缩略对比 + 人眼终审结论。）
