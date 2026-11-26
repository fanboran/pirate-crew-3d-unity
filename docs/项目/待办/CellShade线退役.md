# CellShade 线物理退役

> 一句话现状：渲染风格已裁决只留 PostLine，运行期已切不回 CellShade、口径文档已转正；CellShade 的代码还在库里（约两百个材质以它的物体 shader 为本体，靠运行时换装才画得出来），等云端漫步海面重做合入后单开分支删除。

## 详情

### 为什么要排到海面之后

- 删除会牵动全部关卡的画面，和海面重做混在一个分支里出问题难定位。
- 海面的新 shader 全是自带标准前向通道的独立件，不依赖 CellShade 底座，不必等删除完成。
- 在途分支（伤害数字特效、关卡扩充）可能还在新增以 `PixelartObject` 为本体的材质，等它们合入后再删可少一轮冲突。

### 删除清单（开工时先盘点一遍再动手，以盘点为准）

| 类别 | 内容 |
| --- | --- |
| shader | `Resources/PixelartShaders/` 下的 PixelartObject / PixelartOutline / PixelartRimLight / PixelartShading / PixelartTransparent，及 `Assets/Pixelart/Shaders/Includes/RimLight.hlsl` |
| compute | `Assets/Pixelart/Compute/` 整目录（连通域 Check / Flood / Result 与 Connectivity.hlsl、BlockPatch、RimLightCorrection） |
| 渲染特性 | PixelartBeforeRender / Object / Connectivity / Outline / RimLight / Shading / Transparent / WaterLabScene / ScreenCopy 九个 Feature 及其装配器 `PixelartPathInstaller` |
| 渲染器资产 | PixelartCast_Renderer / PixelartScreen_Renderer / PixelartOverlay_Renderer，及它们在两档 URP 资产列表里的条目 |
| 相机装配器 | `PixelartCameraRig` 里的 Cast / 叠加透明相机与风格分支；`RenderStyleState` / `RenderStyleStore` / `RenderStyleService` |
| 材质换装 | `PixelartContentConverter`、`PostLineMaterialSwapper`——派生规则（色带档数、抖动、云体与海底自然物的 Palette 影调与噪声）改进材质工厂，由烘焙器直接落成 `PostLineToonLit` 材质 |
| 云海家族 | Water / Rain 两支 shader 里的 `PixelartTransparent` 通道与域闸门全局；`CloudSeaWater.PushPixelDomain`、`CloudSeaWeather.PushChainGate` 两个下发点；`PostLineMaterialSwapper`（海面族已自带前向 pass，换装器只剩水上件在用） |
| 测试 | PixelartSceneContractTests、PixelartLevelSceneTests、PixelartPureFunctionsTests、RenderStyleStoreTests 等随代码删除的用例 |
| 文档 | 接口契约.md、调研/v3蓝本.md、调研/CellShade缺口规格.md；`docs/images/pixelart-path/` 的历史实拍留作沿革 |
| 代码注释 | 仍按"两套风格"口径写的头注：PostLineInstaller、PostLineToonLit.shader、CloudSeaRain.shader、PixelartCameraRig（两处）、PixelartPath——其中几处还指向已删除的双风格切换文档 |

### 开工前要先裁的三件事

1. **像素贴片的透明裁切**：CellShade 的物体 shader 有 `_BaseMap` + `_Cutoff` 的剪影遮罩通道，ToonLit 没有（纯色）。先确认现在有没有材质在用它；有的话要么给 ToonLit 补上，要么随草叶线另行处理。
2. **CellShade 独有的观感项**：边缘光（云海鱼材质配了背光边缘光）、镜面高光与金属度、逐物体描边开关、密度图案抖动——这些在 PostLine 下本来就不生效，删除即正式放弃，需创始人知情。
3. **试点 / 探针 / 实验场景的去留**：`Assets/Scenes/Pixelart/` 下的试点、探针、样板场景与 `WaterLab` 实验场景里有依赖 CellShade 特性的件（画布快照、透明叠加相机、异物 shader 断言），逐个定保留改造还是删除。

### 验收

- 无头验证台全量通过；EditMode 全量通过（随删除同步清理用例）。
- 删除前后全关卡 PostLine 同机位出图逐像素比对：除已裁定的观感项外不应有差异，每条差异都要能解释。
- Battle 装配链转储前后比对（见 `pirate-crew/Assets/Scenes/README.md` §五）。

### 已知的坑

- 重跑 Battle 装配链第 ① 步是破坏性重建，相关接线要落在生成器里而不是手改预制体。
- PostLine 装配器会清空 PostLine 渲染器的全部特性再装一个——往该渲染器加特性必须改装配器。
- WaterLab 复刻线本身不删：实验室场景在 PostLine 下走标准前向趟，删 CellShade 特性前先在 PostLine 下拍一张确认无损。
