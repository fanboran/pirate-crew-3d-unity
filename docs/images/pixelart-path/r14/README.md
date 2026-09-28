# r14 · 第三手搓样板关「废弃化工厂」实机出图（关卡号 4，提案/待定）

> **这一轮回答什么**：前几轮（r9–r12）出的是样板关与海图；本轮是**第一次把 Blender 手作的一套
> 关卡美术件接进像素化着色路径出图**——问的是"这套观感用在这座化工厂上是什么样"。
> 内容**尚未接玩法数据**（没有高度场 / 编成 / 武器池 / 摆位表，见
> [L04-废弃化工厂.md](../../../设计/关卡/L04-废弃化工厂.md)，**提案/待定**）：
> 场景由 `PixelartChemPlantSetup`（`pirate-crew/Assets/Pixelart/Editor/`）把**一件总装件**
> `ChemPlant_Level.fbx` 直接摆进 `PixelartLevelScene` 的取景口径里，不是可玩关卡。
> 建模口径 / STAT 实测表 / 摆位表见 [tools/blender/scene/chemplant/README.md](../../../../tools/blender/scene/chemplant/README.md)。

> **⚠ 本轮图带已修复的缺陷**：塔身/罐体/球罐上的"暗斑"是**剔背面洞**（recalc 对开口面翻向
> + 实机物体 pass 剔背面），r16 已根修并工具化（kit 预览默认按实机口径单面渲染）——
> 判读观感请以 [r16](../r16/README.md) 为准，本轮留档仅作前后对照。

## 复现（三条命令，一次只跑一个 Unity 进程）

```bash
# ① 烘试点场景（导入 FBX + Kit_ 槽换装成像素路径材质 + 日/环境光 + 正交相机与 Cast 相机 + 存场景）
tools/headless/run.sh build PirateCrew.EditorTools.PixelartChemPlantSetup.BuildAll
# ② 出开发包（像素路径出图只能走播放器：编辑器侧出图在 batchmode/无图形设备时被设计上拒绝）
tools/headless/run.sh build PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs \
    -buildFlavor development -buildScenes development
# ③ 播放器出图 + 判据
external/build/<版本>/win64/PirateCrew3D.exe -pixelartOut <本目录绝对路径> -pixelartLevel 4
python tools/pixel-review/judge_pixelart_pilot.py docs/images/pixelart-path/r14
```

## 图目

| 档 | 可见高 | 说明 |
| --- | --- | --- |
| `pl4-wide.png` | 32 m | 宽机位（十关统一口径，人物为锚）——本关场地 68×48 m，此档只盖住约三分之一 |
| `pl4-mid.png` | 14 m | 中机位（= 游戏内正交档 7）；场地在场判据取这张 |
| `pl4-close.png` | 7 m | 近机位：塔/罐/平台/断管等细节 |
| `pl4-overview.png` | 58 m | **本关专属**：整场总览（≈0.85 × 场地长边 68 m，整场地进画面） |
| `pl4-mid-density.png` | 14 m | 1-bit 密度抖动对照档 |
| `pl4-dbg-albedo.png` / `pl4-dbg-outline.png` | 14 m | 调试缓冲：albedo / 描边（"光照未旁路""墨线在写缓冲"两条判据的证据图） |

## 判据读数（2026-09-29 运行，`judge_pixelart_pilot.py`）

| 图 | 块边长 | RT宽 | 色数 | 跳变率 | 平坦占比 | 亮暗跨度 |
| --- | --- | --- | --- | --- | --- | --- |
| `pl4-close` | 2 | 960 | 32 | 0.059 | 0.907 | 0.79 |
| `pl4-overview` | 2 | 960 | 54 | 0.098 | 0.848 | 0.82 |
| `pl4-mid` | 2 | 960 | 50 | 0.104 | 0.815 | 0.82 |
| `pl4-wide` | 2 | 960 | — | — | — | — |
| `pl4-dbg-albedo` | 2 | 960 | 18 | 0.045 | 0.918 | 0.72 |
| `pl4-mid-density` | 2 | 960 | 52 | 0.971 | — | 0.82 |

硬门禁（关卡档）全过：**块边长 2**（= 现役 `PixelScale`；RT 960 = 1080p 的艺术画布）、
**墨线在写缓冲**（127 584 像素）、**光照未旁路**（`pl4-mid` 与同机位 albedo 图差 100%）、
**场地在场**（`pl4-mid` 83.1% / `pl4-wide` 59.7%，阈值 10%）。
平坦占比与跳变率在关卡档按约定只打印读数（本关结构密：塔罐管廊平台混在一张画面里）。

## 这轮修掉的一个真问题：换装串槽（口径教训，可复用）

`PixelartChemPlantSetup` 首轮把 FBX 导成 `materialImportMode = None`（照 WorldKit 口径），
结果日志报 **"按名匹配 0 槽 / 按槽序兜底 16 槽"**——`None` 档下 renderer 的材质槽读不到名字，
只能按 FBX 槽序兜底；而 FBX 的实际槽序**不等于**我假设的槽名字典序 ⇒ **串色**：
albedo 调试图里能一眼看出「场地地坪 = 褪色漆黄、罐体/办公楼墙面 = 草绿」。
改成 `ImportStandard` 导入（FBX 材质建成子资产、名字就是 Blender 侧的 `Kit_` 槽名）后
**按名匹配 16 槽 / 兜底 0 槽**，配色归位。场景引用的是本路径自己的 `.mat`
（`Assets/Pixelart/Materials/PixelartChemPlant_Kit_*.mat`），FBX 里的子资产不被引用（惰性）。

**待核实（未查，别当结论）**：`WorldMapAssetSetBuilder.ApplyImportSettings` 同样用 `None`
再按名匹配（`ApplyKitMaterials`），是否有同一问题需单独验——本轮只管本关，没动它。

## 这轮修掉的第二类问题：实机色带下的"几何装饰"（两轮实测，可复用）

**在近似竖直的曲面上做周向棱槽 = 必翻档。** 冷却塔原先用"隔扇区半径 ±5.5%"做棱槽（Blender 的
连续渲染下只是轻微的槽线），进实机后相邻扇区的法线差几度，而漫反射只有三档 ⇒ 相邻扇区互相翻档，
读成**棋盘格**；把幅度压到 ±1.5% 后更糟——读成**镂空壳**（暗档被看成洞）。
**改法**：壳体半径一律光滑（去掉周向棱槽），装饰改成**外凸细肋条**（沿半径剖面的折线细管 ×16）——
肋条自身单一面朝向、只落一档色，不参与翻档。这与厂房压型墙（平墙 + 外凸竖棱）的成功做法同源。

同类修正一处：**围栏横杆/竖条由 `Kit_Rust` 压到 `Kit_RustDark`**——实机里亮锈橙在长杆上太抢眼，
成排围栏把整片场地染橙；暗锈更贴"多年失修"。

**通用教训**：贴图的"细节靠几何"在实机里要按**色带档数**复核——平面凸起（肋条/棱条）安全，
**曲面上的凹陷（槽/凹痕）在竖直面上不安全**；凹陷类装饰只适合水平面（顶面）或与光照方向一致的面上。

## 材质与色卡口径

- **逐物体只吃 albedo**：像素路径的物体材质由 `PixelartMaterialFactory.Create(name, albedo)` 造
  （`pirate-crew/Assets/Scripts/PirateCrew/Rendering/Pixelart/PixelartMaterialFactory.cs`），
  色带/描边/调色板都在屏幕空间那几趟里，粗糙度与金属度不参与。⇒ 新增的 9 个工业槽位在 Unity 侧
  真正要同源的只有**槽名 + hex**（`style_tokens.SLOTS` ↔ `WorldMapAssetSetBuilder.Slots` ↔
  `PixelartChemPlantSetup.SlotHex`，三处同源，改一处必改另两处）。
- **有限色卡（全局 32~64 色锁板、纹理量化入板）已由创始人裁决取消**（2026-09-29
  「先把有限色卡取消吧这个项目」），记档在 [美术风格指南](../../../设计/美术风格指南.md) §3.2。
  本轮因此**不做色卡对照图**：色彩依据就是这些实机图本身。既有"帧级调色板 LUT"本就默认关闭
  （[像素化着色路径/README.md](../../../技术/渲染/像素化着色路径/README.md) §4 已知关闭项②），
  与本裁决同向。
- Blender 侧的预览图（`docs/images/chemplant-kit/`）是**代理观察**，不是实机口径：
  那边是 Cycles + 哑光 + 像素块边长 2；实机的色带是**逐物体 3 档**、再加屏幕空间描边与合成那几趟，
  所以同一份 albedo 在实机里更冷、更平、更"块"。判观感一律以本目录的实机图为准。

## 已知的观感与日志观察项

1. **宽机位盖不住整场**（32 m 可见高 / 68 m 场地）——本关专属的 `pl4-overview` 补这一档；
   其余关卡无此档，因为它们是 40×30 的竞技场或按人物为锚可横向比的图。
2. 出图日志会写一句船员 renderer 条数"期望 -2 个"：本关**没有编成数据**，
   `PlayerArtCapture` 两侧（关卡资产 / 海图出生表）都读不到人数才落到 -1 ⇒ 是预期的读取缺口，
   不是场景缺件。场内实际摆了 3 个船员当尺度参照（人高 1.85 m ≈ 厂房檐 7.2 m 的 1/4）。
3. 场内那 3 个船员**不参与任何玩法**（本关没有编成），只做尺度锚。
