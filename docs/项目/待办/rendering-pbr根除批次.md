# PBR 根除批次·待实机走查

> 三波已落 `refactor/industrial-grade`、harness 零回归且像素管线零触碰；遗留 `BattleSceneLighting` 8 材质段 Surface 口径与 `Water_Ocean.mat` 悬空引用，卡在待编辑器空闲窗口跑 batchmode open 档走查。

## 详情

**【PBR 根除批次·待实机走查】**（2026-09-24 三波落 `refactor/industrial-grade`，harness All 零回归
1243/11/1 与当期基线逐条一致；**像素管线零触碰**——`Assets/Pixelart/**` 整树、URP 链
（PC_*/Pixelart* 五渲染器）、PixelationFeature、ToonLightDriver（与像素链共用 `_PixelRT*` 全局）
全保留）——
① **C# 换血**：删全屏 Sobel 三件套（`OutlineRendererFeature`/`OutlineDebugCapture`/
`UrpRendererFeatureRetire`）+ ArtGate 白名单缩减；烘焙链与装配器（SceneArtBaker/LowpolyStageBuilder/
WorldMapComposer/WorldMapAssetSetBuilder/FloatingIslandShowcaseMenu/AmbientMaterialSet/
CrewVisualPrefabBuilder）全部改产 PixelartObject 像素材质，Lit/Standard 兜底清零；
WaterAssetBuilder 拆材质工厂保 `BakeObstacleMap`（水模拟障碍图与 PBR 无关）；ToonPilotSetup/
ToonMaterialFactory 删除。
② **场景手术**：BattleRig 浮岛×15 → `PixelartDerived_FloatingIsland_*`；海面引用清空
（OceanRig 运行时造 `PixelartOcean_Sea` 替身）；CloudField → `PixelartLevel_Cloud*`；
ToonPilot.unity+Islet+7 材质+2 贴图共 33 资产清除；装配链场景清单 6→5、开发集 17→16
（`ManagementSceneSetup`/`SceneSetup` 幂等写入同步，防重跑写回）。**船员反壳纠缠未动**：
描边是 PirateOutline shader 内置 Pass 非独立材质槽，迁移会破 `UnitOutlineBinder._OutlineState`
MPB 契约，且立项任务书 §3 保留「反壳骨架（基座）」；运行期 PixelartContentConverter 派生兜底。
③ **清扫**：删 6 个零消费者 shader（PirateSurface/Water/Toon/Terrain/Wind/Glow）+ 28 个孤儿材质
（Surface×8/瓦片地形/海水/旧浮岛×9/风摆灯笼/低模云，每件 guid 反查零命中）+ ToonPilot 残留
（`-toonPilotOut` 参数、PlayerArtCapture 出图流程、scene-assets.json 死条目、
DitherPatternBaker 菜单路径归 Pixelart）。
④ **保留件（裁决）**：PirateGradientSky 三档（像素天空尚不存在，M2d 收编时换）；Fx/Additive+Alpha
与 URP/Unlit 系（非光照实用件非 PBR）；PirateOutline+Crew 族（反壳基座）；MaterialNoise 贴图
（BattleSceneLighting/ArtGate 仍有活消费）。「URP 下位替代」口径已从 AGENTS 移除（用户裁决同日）。
⑤ **遗留**：`BattleSceneLighting.BuildEnvironmentMaterials` 8 材质段仍 Surface 口径（重跑 BuildAll
会报错跳过不崩）；`Water_Ocean.mat` 仍被 Battle.unity 引用而 shader 已删（该 renderer 烘焙期
enabled=false 不参与渲染，随下波场景手术清引用或删材质）；`Assets/Pixelart/Editor/` 内 4 处
ToonPilot 注释字样（禁区未动）；docs/ 约 10 文件 ToonPilot 引用待顺带更新。
⑥ **验收**：待编辑器空闲窗口跑 batchmode open 档 + 必查清单（浮岛无洋红、海面显示
PixelartOcean_Sea、ToonPilot 无 missing 引用、删除资产导入无红）。