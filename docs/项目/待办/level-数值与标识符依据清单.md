# 数值与标识符「依据可疑」清单（待创始人圈定）

> 一句话现状：代码/文档里有一批数值与标识符，其注释里写的存在理由只有「对齐 Godot 基准 / 忠实转写、照搬 Flash 逆向 / 逐值对齐 / 逆向得到」——这些依据按创始人裁决**已作废**（参照游戏与 Godot 版都只是早期来源，不是设计基准），本清单把这些项逐条圈出，供创始人决定「删 / 改依据 / 留」。

> **状态：提案 / 待定。** 本文件是**只读审计的产物**，不含任何执行动作；`建议` 列的取舍权在创始人。
> 本清单**只判断「依据是否作废」**，**不判断「数值好不好」**——数值本身可能是好用的，问题只在于它的理由。

## 汇总

**共 86 项。** 建议：**删 9 项 / 改依据 68 项 / 留 9 项。**

- **删（9）**：无任何设计文档背书、且已被明示为「历史字段 / 仅存档备查 / 已废归一基准」，或已被 2026-09-30 裁决整批删除（8 张海图）——属「该扔」。
- **改依据（68）**：数值/标识符本体仍在用、可能仍然好用，但注释给的理由（Godot/逆向/忠实转写/逐值）作废，需重写理由并重新裁决。
- **留（9）**：有已裁决的设计文档（契约 / 创始人裁决）为它定口径——只需保留，无需动。

> 分布：A 战斗数值 19 项（改 16 / 留 3）；B 数据表 18 项（改 12 / 删 3 / 留 3）；C 战斗·AI·表现 12 项（改 11 / 留 1）；D 美术·场景 13 项（改 9 / 删 3 / 留 1）；E WorldMaps 2 项（删 2）；F 测试 7 项（改 5 / 删 1 / 留 1）；G 文档 15 项（改 15）。

**最值得先看的 5 项**（依据最彻底作废 + 依赖面最广）：

1. `WeaponId` 17 武器枚举 + `WeaponCatalog` 17 行数值（`pirate-crew/Assets/Scripts/PirateCrew/Data/WeaponId.cs`、`WeaponCatalog.cs`）——唯一依据是「逆向 §5.2 逐行逐值」，却是全工程武器/关卡 XML/测试的公共地基。
2. `BalanceConfig` 全部常量（`pirate-crew/Assets/Scripts/PirateCrew/Data/BalanceConfig.cs`）——出处整句写「静态逆向文档 §5.1/§5.3/§5.2/§3.1/§1/§7.3/§4.1」。
3. `CrewVisualPrefabBuilder` 里以 `Godot*` 命名的标识符（`pirate-crew/Assets/Editor/CrewVisualPrefabBuilder.cs`）——标识符名与依据都写死「Godot 基准」。
4. `docs/项目/交接与恢复指南.md` 的「**Godot 出空间结构 + Flash 出数值**」总口径（`:21/:40/:69`）——这是全项目文档的母口径，它不改，下游文档都会继续复读。
5. `MinimapRules` / `SceneArtPalette` 的队色与点阵常量（`MinimapRules.cs`、`SceneArtPalette.cs`）——红蓝 `#FF3A29`/`#3366FF` 只有「逆向 §8.1」一条出处。

---

## 判据与口径（说明）

- **「依据可疑」判据**：注释/文档把它的存在理由写成「对齐 Godot 基准 / 忠实转写或照搬（Godot 或原版 Flash）/ 逐值对齐 / 逆向得到」，**且找不到** 一份已裁决的设计文档为它背书。
- **「有背书」判据**：能指出具体文件（如 `docs/技术/投掷行为契约.md`、`docs/技术/3D空间模型对齐.md`）为它定了口径。
- **三档建议**：`删`（无背书、且不被别处依赖，或已被裁决删除）/ `改依据`（值可能在用，仅理由作废）/ `留`（有设计文档背书）。
- **本清单认定的「背书白名单」**（只认这几份，其余一律视为无背书）：
  - `docs/技术/投掷行为契约.md`（投掷行为唯一规格）
  - `docs/技术/相机行为契约.md`（相机行为唯一规格）
  - `docs/技术/3D空间模型对齐.md`（3D 空间契约；其 §5 爆炸口径、§1/§3 空间与运动学口径）
  - `docs/设计/美术.md`（§9 角色造型口径：圆球+圆柱、阵营色/木色分工；尺寸/分段常量的数值真源在 `CrewVisualPrefabBuilder`，**创始人裁决 2026-09-29**）
  - `docs/技术/渲染/管线/渲染管线.md`（**创始人裁决 2026-09-22**：俯角 30°）
  - `docs/技术/架构/EventBus事件契约.md`（事件契约）
- **不作为背书**：`docs/项目/归档/参考逆向/**`（已归档，裁定「不再作设计依据」）；一切「提案/待定」段落；`3D空间模型对齐.md` §7 里「Flash 数值**不变**」这类**整批保留条款**——它只声明「不要改」，不构成正面设计理由，不足以给单个数值背书。

---

## 详情

### A. 战斗数值 —— `Combat/*`

| 项 | 现值/名称 | 出处（文件:行） | 注释里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| A1 关卡得分公式 | `floor(avgHealth*20 − totalTurnsTaken*25)`，下限 `levelIndex*10` | `pirate-crew/Assets/Scripts/PirateCrew/Combat/ScoreRules.cs:7` | 「对应逆向文档 §3.3 / §7.3」 | ❌ | 改依据 |
| A2 得分·血量权重 | `20` | `Combat/ScoreRules.cs:13` | 「每点平均生命值折算的分数（原版 20）」 | ❌ | 改依据 |
| A3 得分·回合惩罚 | `25` | `Combat/ScoreRules.cs:16` | 「每消耗一个回合扣除的分数（原版 25）」 | ❌ | 改依据 |
| A4 得分·关卡下限系数 | `10` | `Combat/ScoreRules.cs:19` | 「原版下限 = selected_level * 10」 | ❌ | 改依据 |
| A5 空闲帧推进阈值 | `InactivityFramesToAdvance = 10` | `Combat/TurnRules.cs:67` | 「原版空闲帧阈值：inactivity > 10（严格大于）才推进回合（§3.1）」 | ❌ | 改依据 |
| A6 回合状态字段 | `thrown` / `fired` / `canThrow` / `canShoot` | `Combat/TurnRules.cs:9,12,15,18` | 「原版 Character.thrown / Weapon.fired / Character.canThrow / canShoot」 | ❌ | 改依据 |
| A7 地雷引信与蜂鸣 | `60` 帧引信、`beepTimes=[0,15,30,38,45,49,53,55,57,59]` | `Combat/WeaponTriggerRules.cs`、`Data/WeaponCatalog.cs:160` | 「规则出自逆向文档 §5.2 触发/引爆条件列，以及 mine 的 60 帧引信与 beepTimes」 | ❌ | 改依据 |
| A8 船锚投放点 y | `SpawnFlashY = −200` | `Combat/AnchorRules.cs:25` | 「§5.2 anchor 行」 | ❌ | 改依据 |
| A9 船锚下落速度 | `FallSpeed(v_y) = 40` | `Combat/AnchorRules.cs:27` | 「§5.1「anchor 恒 vy=40」」 | ❌ | 改依据 |
| A10 船锚 AABB | `HalfWidth=48` / `Top=96` / `Bottom=0` / `anchorY−64` | `Combat/AnchorRules.cs:30,33,36,39` | 「§5.2 AABB「l/r 48」「top 96」「bottom 0」」 | ❌ | 改依据 |
| A11 海鸥入场/速度 | `SpawnFlashX = −300`、`vx = 10`、`levelWidth*32+275` | `Combat/SeagullRules.cs:26,28,31` | 「§5.2 seagull 行 / 「以 vx=10 向右飞」/「levelWidth*32+275」」 | ❌ | 改依据 |
| A12 海鸥 AI 评分/选高 | `1 − d/40`、`enemyMaxY − 100 − random*100` | `Combat/SeagullRules.cs:40,43,46` | 「§6.3 seagull 行 /「高度 = 敌方最高 y − 100 − random*100」」 | ❌ | 改依据 |
| A13 潮汐浪常量 | `SpawnFlashX = −550`、`vx = 20`、`±150px`、`y ≥ waterY−300`、每帧 `5` 伤 | `Combat/TidalWaveRules.cs:27,29,35,38` | 「§5.2「vx=20 横扫到最右」「±150px 内」」 | ❌ | 改依据 |
| A14 蔓延火焰常量 | 每段 `8px`、命中 `dist < 8px`、每段 `30` 伤、击退 `(rand−0.5)*8` / `−(rand*2+6)` | `Combat/SweepingFlameRules.cs:24,27,36` | 「§5.2「沿地面每 8px 蔓延一段」「命中条件 dist < 8px」§6.3」 | ❌ | 改依据 |
| A15 加农炮蓄力阈值 | `fireStrength ≤ 30` 上限、`≥30` 才发射、`≤4` 不发射 | `Combat/CannonRules.cs:32,53` | 「§5.1「fireStrength（≤30）」」 | ❌ | 改依据 |
| A16 弹弓力度系数 | `TwangForceScale = 0.25`（初速 = 0.25 × 拖拽距离） | `Combat/Ballistics.cs:20` | 「原版弹弓的固定力度系数：初速 = 0.25 × 拖拽距离」 | ✅ `docs/技术/投掷行为契约.md:23,31`（列 0.25 为规格） | 留 |
| A17 摩擦/弹跳默认 | `friction / bounce`（角色与武器默认 2 / 0.2） | `Combat/Ballistics.cs:89` | 「弹体弹跳/摩擦 Flash 口径接线」 | ❌ | 改依据 |
| A18 爆炸公式与常数 | `radius = size/2 + 20`、`k = 0.06·falloff·maxDamage`、`5k` 径向、`6k` 抬升 | `Combat/ExplosionResolver.cs:123,126,129,132` | 「原版固定 +20px / 原版 0.06 / 原版 5 / 原版 6」 | ✅ `docs/技术/3D空间模型对齐.md` §5（定义 3D 合成口径；但条款内写「公式与常数仍取 Flash」，**此句需一并重写**） | 留 |
| A19 爆炸 3D 化「逐值不变」 | 2D 用例（z=0）结果逐值不变 | `Combat/ExplosionResolver.cs:11,118` | 「只传 (x,y) 的既有 2D 用例结果逐值相同」「公式与常数全部取自 Flash」 | ✅ 同上（3D空间 §5） | 留 |

### B. 数据表 —— `Data/*`

| 项 | 现值/名称 | 出处（文件:行） | 注释里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| B1 武器总表（17 行全字段） | AABB/摩擦/重力/弹跳/twangMax/爆炸 size·伤害/触发/dragRange/复用次数/固定伤害 | `Data/WeaponCatalog.cs:114,124` | 「【出处】静态逆向文档 §5.2「武器总表」逐行逐值」「17 行逐行参考」——**依赖面极广：武器 SO / 战斗规则 / AI / 关卡 XML 键 / 测试全部吃它** | ❌（`3D空间模型对齐.md` §7 只说「武器触发规则不变」，非正面理由） | 改依据 |
| B2 武器 id 枚举 | `WeaponId`（cannonball…sweepingFlame 共 17） | `Data/WeaponId.cs:4-7` | 「原版《海盗军团抢宝藏》(Mutiny) 的 17 种武器 id」「成员名直接采用文档表格里的英文 id（= 原版 ActionScript 类名的小驼峰形式）」——**依赖面极广** | ❌ | 改依据 |
| B3 武器触发 Flags | `WeaponTrigger` | `Data/WeaponTrigger.cs:6,8` | 「对应静态逆向文档 §5.2「触发/引爆条件」列」 | ❌ | 改依据 |
| B4 武器定义 SO 显示名 | `WeaponDefinition.DisplayName` | `Data/WeaponDefinition.cs:8,26` | 「沿用原版英文类名 / 关卡 XML 属性键（如 cherryBomb）」 | ❌ | 改依据 |
| B5 投掷系数/满力上限 | `TwangForceScale 0.25` / `DefaultTwangMax 20` / `HighTwangMax 30` | `Data/BalanceConfig.cs:22,25,28` | 「§5.1：初速 = 0.25 × 拖拽距离」「默认 twangMaxForce = 20」「高弹弓上限 30」 | ✅ `docs/技术/投掷行为契约.md:23,31` | 留 |
| B6 爆炸平衡常数 | `ExplosionRadiusPadding 20` / `KnockbackCoefficient 0.06` / `KnockbackHorizontal 5` / `KnockbackVertical 6` | `Data/BalanceConfig.cs:31,34,37,40` | 「§5.3：爆炸半径 radius = size/2 + 20」「k = 0.06·falloff·maxDamage」「dir·5k」「dir·5k − 6k」 | ✅ `docs/技术/3D空间模型对齐.md` §5 | 留 |
| B7 物理默认摩擦/弹跳 | `DefaultFriction 2` / `DefaultBounce 0.2` | `Data/BalanceConfig.cs:43,46` | 「§4.1 / §5.2：默认摩擦 2」「默认弹跳 0.2」 | ❌ | 改依据 |
| B8 空闲帧阈值 | `InactivityFramesToAdvance 10` | `Data/BalanceConfig.cs:49` | 「§3.1：全局 inactivity 超过 10 帧（≈0.4s 无活动）推进回合」 | ❌ | 改依据 |
| B9 帧率 | `OriginalFps 25` | `Data/BalanceConfig.cs:52` | 「§1：原版帧率 25 fps（所有「每帧」量纲都基于它）」 | ✅ `docs/技术/3D空间模型对齐.md` §3（钉 `Time.fixedDeltaTime = 1/25`） | 留 |
| B10 得分平衡常数 | `ScoreHealthWeight 20` / `ScoreTurnPenalty 25` / `ScoreFloorPerLevel 10` | `Data/BalanceConfig.cs:55,58,61` | 「§7.3：floor(平均血量*20 − 回合数*25)」 | ❌ | 改依据 |
| B11 角色 AABB | `CharHalfWidth 6` / `CharHalfHeight 8` | `Data/BalanceConfig.cs:64,67` | 「§4.1：角色 AABB 半宽 6 / 半高 8」 | ❌ | 改依据 |
| B12 船员共享属性/队伍归属/∞ 约定 | 全船员零属性差、`luck`、非 redPirate 归 team2、`count==10`=无限 | `Data/CrewCatalog.cs:7,9,147,162,237` | 「出处：静态逆向文档 §4.1」「原版所有海盗没有任何属性差异」「原版武器栈里值 10 表示「无限」（§5.5）」 | ❌ | 改依据 |
| B13 船员定义 | `CrewDefinition`（`count==10`=无限等） | `Data/CrewDefinition.cs:10,11,36,38` | 「【出处】静态逆向文档 §5.5 Character.setWeapons(attrs)」「§4.1（共享属性）、§4.2、§4.3」 | ❌ | 改依据 |
| B14 关卡 XML·players | `players` 字段（1/2） | `Data/LevelData.cs:30` | 「原版 XML players 属性（1/2；仅样板数据保留此口径，世界图为 1）」 | ❌ | 改依据 |
| B15 关卡 XML·maxChests | `maxChests` 原值 | `Data/LevelData.cs:42` | 「原版 XML 的 maxChests 属性原值（仅存档备查）」 | ❌ | **删**（仅存档备查，无运行时消费） |
| B16 关卡数据·导出符号名 | 角色导出符号名（redPirate / bossGuy） | `Data/LevelData.cs:85` | 「原版导出符号名（§4.2，如 redPirate / bossGuy）」 | ❌ | 改依据 |
| B17 关卡资产·players / maxChests | 同 B14/B15 | `Data/Levels/LevelAssetTypes.cs:127,136` | 「原版 XML players 属性（1/2；仅存档备查，现行恒 1）」「仅存档备查」 | ❌ | **删** |
| B18 关卡数据·waterTileY | `waterTileY` | `docs/技术/3D空间模型对齐.md:87`（口径）+ 关卡数据 | 「自此仅作历史字段保留，不参与运行时」 | ❌（文档明示为历史字段） | **删** |

### C. 战斗 / AI / 表现 —— `Battle/*`、`UI/*`

| 项 | 现值/名称 | 出处（文件:行） | 注释里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| C1 单位视觉总高 | `CameraFraming.UnitVisualHeight = 1.85` | `Battle/CameraFraming.cs:82,85` | 「单位视觉总高（世界单位）= 1.85，与 CrewVisualPrefabBuilder.TargetUnitHeight 同源」 | ❌（角色造型规范现役总高为 **2.0321**，1.85 只是已废的归一基准） | 改依据（且数值与现役造型已不一致，需重新标定） |
| C2 lookAt 抬高比例 | `LookAtHeightRatio`（0.65 档） | `Battle/CameraFraming.cs:90` | 「lookAt 抬高 = 1.85 × 0.65」 | ❌ | 改依据 |
| C3 AI 打分公式与常量 | §6.2/§6.3 位置启发式全套常量（`SelfLandingDistanceWeight 0.003`、放置圈、随机采样…） | `Battle/AiEvaluation.cs:654,682,767,993,997` | 「原版常量（§6）」「原 Flash §6.2 是 s += (t.ey − this.y) * −0.003」「系数标定：沿用原式的 0.003/px 斜率」 | 部分 ✅：`3D空间模型对齐.md` §8 为 `SelfLandingDistanceWeight` 一项定了 3D 口径；其余无 | 改依据（可拆：着陆项 留，其余 改依据） |
| C4 AI 字段名含 Flash | `FlashSuccess` 等 | `Battle/AiEvaluation.cs:471,498,979` | 「= 逆向文档 §6.2/§6.3 的原始 success（逐行参考）」 | ❌ | 改依据（标识符改名） |
| C5 世界重力/物理帧率 | `Physics.gravity = −19.53125`、`fixedDeltaTime = 1/25` | `Battle/BattleController.cs:394` | 「把 PhysX 全局重力设为 Flash weight=1 的等价重力（−19.53125）」「物理帧率设为原版 25fps」 | ✅ `docs/技术/3D空间模型对齐.md` §3（钉此逐步等价关系） | 留 |
| C6 相机聚焦平滑 | 聚焦速度 `6`（90% 约 0.384s） | `Battle/BattleCameraDriver.cs:63` | 「贴合原版 10 帧 @25fps 的回合节奏（§3.1）」 | ❌ | 改依据 |
| C7 震屏/顿帧 | `[Header("震屏（提案/待定，原版无此机制）")]` 等参数 | `Battle/BattleCameraDriver.cs:66,85` | 「提案/待定，原版无此机制」 | ❌（本身自陈提案；但理由句含「原版」对照） | 改依据 |
| C8 相机手感时间参数 | 跟随/震屏/顿帧时长 | `Battle/CameraFeelRules.cs:45,103-106` | 「全部为提案/待定（原版无镜头跟随机制）」「依据是「对齐原版回合节奏」」 | ❌ | 改依据 |
| C9 HUD 血量比例 | 「28 帧」血量比例口径 | `UI/BattleHud.TeamBars.cs:21,367` | 「28 帧血量比例口径（原版 §4.1）」 | ❌ | 改依据 |
| C10 小地图（视图） | `dotSize=3`、红 `0xFF3A29`/蓝 `0x3366FF`、`mapVisibility`、`mapHolder (20,20)` | `UI/BattleMinimap.cs:12-15` | 「【原版依据】Map.as（§2.4/§8.1）」 | ❌ | 改依据 |
| C11 小地图（换算法） | `FlashDotSizePixels 3` / `FlashSolidTileAlpha 50` / `FlashEmptyTileAlpha 20` / `RedTeamColor` / `BlueTeamColor` | `UI/MinimapRules.cs:33,37,40,78,81` | 「原版小地图点尺寸 dotSize=3（§8.1）」「原版红队色 0xFF3A29（§8.1）」 | ❌ | 改依据 |
| C12 落地翻滚全套 | `Spin 48°/s` / `GroundDecel 78.125` / `DampFactor 0.5` / `Bounce 0.2` / `WaterSpin 64` | `Battle/RollRules.cs:34,41,50,59` | 「每帧 rotation += vx*3（Character.as:151-154）」「落地 vy *= −0.2（Solid.as:263-276）」——**背书理由是「忠实转写 Flash 逆向」** | ⚠ 仅 `docs/项目/归档/M4-世界化/大海域世界化.md:101`「忠实转写 Flash 逆向，【裁决：按逆向实现】」——该**裁决的内容本身即已作废的依据** | 改依据（需创始人重新确认该「按逆向」裁决是否随之作废） |

### D. 美术 / 场景 —— 角色命名、取色、天空档

| 项 | 现值/名称 | 出处（文件:行） | 注释里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| D1 归一基准总高 | `GodotReferenceHeight = 1.85f` | `pirate-crew/Assets/Editor/CrewVisualPrefabBuilder.cs:110-112` | 「归一基准总高 = 1.85」「只作 GodotScale 的归一基准；**现役总高以两件式常量推导为准（≈2.03）**」 | ❌（文档自陈已不再锚 1.85） | **删**（已废归一基准） |
| D2 格↔世界单位系数 | `GodotUnitsPerTile = 1f` | `CrewVisualPrefabBuilder.cs:118` | 「兜底关地面 50 格 ↔ 50 世界单位」「1 格 = 1 世界单位（GodotUnitsPerTile）」 | ❌（与现行 `LevelGeometry.TileWorldSize = 2` 冲突） | **删** |
| D3 归一缩放 | `TargetUnitHeight` / `GodotScale`（恒 1） | `CrewVisualPrefabBuilder.cs:134,137` | 「k = 1.85 / 1.85 = 1（归一单位 → 本工程世界单位）」「总高不再锚 1.85」 | ❌ | **删**（恒 1，无意义） |
| D4 Body 分段 | `GodotBodySides = 16` | `CrewVisualPrefabBuilder.cs:173` | 「圆台柱侧壁分段（16…）」——值有裁决，名字溯 Godot | ✅ 值：`CrewVisualPrefabBuilder.cs:173`（**创始人裁决 2026-09-29** 已落实，柱 16 段；设计口径见 `docs/设计/美术.md` §9） | 改依据（改名去 Godot） |
| D5 Head 分段 | `GodotHeadSegments = 12` / `GodotHeadRings = 8` | `CrewVisualPrefabBuilder.cs:182,184` | 「球分段 12×8」——值有裁决，名字溯 Godot | ✅ 值：`CrewVisualPrefabBuilder.cs:182,184`（**创始人裁决 2026-09-29** 已落实，球分段 12×8） | 改依据（改名去 Godot） |
| D6 两件式现役尺寸 | `BodyTopRadius 0.30` / `BodyBottomRadius 0.30` / `BodyHeight 1.3215` / `HeadSphereRadius 0.30` | `CrewVisualPrefabBuilder.cs:143,150,153,156` | 「顶 r 0.30 / 底 r 0.30（直筒）/ 柱高 1.3215 / 头 r 0.30」 | ✅ `CrewVisualPrefabBuilder.cs:143-156`（**创始人裁决 2026-09-29** 已落实；设计口径见 `docs/设计/美术.md` §9） | 留（仅需清理其乘子 `* GodotScale`） |
| D7 装配方法名 | `ApplyGodotTwoPieceSilhouette` / 日志「用户裁决：Godot 两件式」 | `CrewVisualPrefabBuilder.cs:243,451,534` | 「用户裁决：Godot 两件式 = 圆球 + 圆台柱」 | ⚠ 两件式有裁决，但标识符名与理由溯 Godot | 改依据（改名去 Godot） |
| D8 角色调试场 | `CharCamDebugController` 的「Godot 基准」tooltip/镜像常量 | `Scripts/PirateCrew/CharCamDebug/CharCamDebugController.cs:19,59,62,65,134,457,527` | 「0 = Godot 基准（球底与柱顶重叠 0.05）」「镜像 CrewVisualPrefabBuilder.GodotBodySides」 | ❌ | 改依据 |
| D9 文本样张对比基准 | 「与 Godot 基准图并排对比」 | `Editor/TextSampleBuilder.cs:244` | 「请与 Godot 基准图并排对比；描边宽度初值 OutlineWidthInitial 待目测校准」 | ❌ | 改依据（对比基准改本仓锁定样张） |
| D10 阵营色 | 红 `#FF3A29` / 蓝 `#3366FF` | `Scripts/PirateCrew/SceneArt/SceneArtPalette.cs:13,72` | 「阵营红/蓝 = 逆向文档小地图配色（参考游戏逆向…静态.md:721）」 | ❌ | 改依据 |
| D11 天空档位分配 | 档 1/2/3（正午/黄昏/阴云） | `Scripts/PirateCrew/SceneArt/SkyTierCatalog.cs:8,26,71` | 「三档的档位分配是【依据】——原版按 skyColour=1/2/3 切贴图」 | ❌ | 改依据 |
| D12 天空盒档映射 | `AmbientSkyboxCatalog.SkyTierIndex` | `Scripts/PirateCrew/Ambient/AmbientSkyboxCatalog.cs:18,233` | 「对应 SkyTierCatalog 的档号（1/2/3，原版 skyColour 口径）」「三档本就是照原版 skyColour 1/2/3 分的」 | ❌ | 改依据 |
| D13 活物禁飞天花板 | `AmbientRules.DefaultCeilingY = 9` | `Scripts/PirateCrew/Ambient/AmbientRules.cs:214` | 「【提案】：投掷抛物线顶点通常 < 6 单位（**Flash 满力抛射高度量级**），取 9 留出余量」 | ❌（理由取 Flash 量级） | 改依据 |

### E. WorldMaps（M4 已裁决：8 张海图全删）

| 项 | 现值/名称 | 出处（文件:行） | 注释里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| E1 世界图军火分层 | 每图船员初配近程档、船长含旗舰、军火分层 | `Scripts/PirateCrew/Battle/WorldMaps/WorldMapKit.cs:108,113` | 「分层依据：逆向 §5.1/§5.2」「逆袭 §5.2——这五件射程 = 地图本身」 | ❌ | **删**（`docs/设计/场景/环境表现.md:187`：**创始人 2026-09-30 裁决 8 张海图全部删除**） |
| E2 世界图配置/测试 | `WorldMapLoadout` 八图配置 | `Tests/WorldMaps/WorldMapLoadoutTests.cs:11` | 「分层依据：逆向 §5.1/§5.2——twangMax 20 近程 / 30 中程 / 五件全图级」 | ❌ | **删**（同上裁决） |

### F. 测试断言

| 项 | 现值/名称 | 出处（文件:行） | 注释里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| F1 名册招募去重/空阵容 | 重复招募无效、空阵容允许 | `Tests/CrewManagement/RosterTests.cs:40,91` | 「重复招募应无效（**Godot unlock_crew 的 has 去重**）」「Godot 版允许空阵容」 | ❌ | 改依据 |
| F2 单位高随 Godot | `LookAtHeight_FollowsGodotUnitHeightAndRatio`、断言 `1.85` | `Tests/Battle/CameraFeelRulesTests.cs:502,505,506` | 「单位视觉总高应 = **Godot 1.85**（1 格 = 1 Godot 单位 = 1 本工程单位）」 | ❌ | 改依据 |
| F3 世界图分层 | 同 E2 | `Tests/WorldMaps/WorldMapLoadoutTests.cs:11` | 「分层依据：逆向 §5.1/§5.2」 | ❌ | **删** |
| F4 武器触发 | mine 60 帧引信 / beepTimes | `Tests/Combat/WeaponTriggerRulesTests.cs:6` | 「规则出自逆向文档 §5.2 触发/引爆条件列，以及 mine 的 60 帧引信与 beepTimes」 | ❌ | 改依据 |
| F5 回合规则 | inactivity>10 / isTurnComplete | `Tests/Combat/TurnRulesTests.cs:6` | 「规则出自逆向文档 §3.1/§3.2/§3.3/§3.4」 | ❌ | 改依据 |
| F6 计分公式 | `floor(avgHealth*20 − totalTurnsTaken*25)` | `Tests/Combat/ScoreRulesTests.cs:6` | 「公式出自逆向文档 §3.3 / §7.3」 | ❌ | 改依据 |
| F7 爆炸 3D 泛化 | 「2D 旧实现逐值相同」等断言 | `Tests/Combat/ExplosionResolverTests.cs:6,11,151` | 「期望值全部可由逆向文档 §5.3 的公式手算复核，口径见 docs/3D空间模型对齐.md §5」 | ✅ `docs/技术/3D空间模型对齐.md` §5 | 留 |

### G. 文档（`docs/**`，已排除 归档 / 审计 / images / 交接）

| 项 | 现值/名称 | 出处（文件:行） | 文档里写的依据（原文摘录） | 背书 | 建议 |
| --- | --- | --- | --- | --- | --- |
| G1 全项目母口径 | 「**Godot 出空间结构 + Flash 出数值**」 | `docs/项目/交接与恢复指南.md:21,40,69` | 「现按 Godot 出空间结构 + Flash 出数值 重做」「Godot 4 版…**翻译基准与设计参照**」「Godot 出空间结构、Flash 逆向文档出数值」 | ❌ | 改依据（**这是下游文档的引用源，最该先改**） |
| G2 立约动机/分工表 | 「对齐 Godot 版真 3D」「Flash 逆向文档 → 数值、公式、回合规则、武器表」 | `docs/技术/3D空间模型对齐.md:2-5,9,43,57` | 「本契约的立约动机：第一版曾把 2D 侧视坐标…产出「披着 3D 引擎的 2D 游戏」」「数值取 Flash」 | ⚠ 文档本体是**已裁决契约**（本清单白名单） | 改依据（**保留文档**，只重写「Godot/Flash 出××」的依据措辞） |
| G3 整批保留条款 | §7「数值三层…与全部 Flash 数值**不变**」「Combat/ 回合/计分/武器触发规则不变」 | `docs/技术/3D空间模型对齐.md:117-118` | 「数值三层…与全部 Flash 数值不变」「Combat/ 的回合/计分/武器触发规则不变」 | ❌（只声明「不改」，非正面理由，也是 §A/§B 各项「无背书」的成因） | 改依据（应被针对性裁决取代） |
| G4 建关基准文档 | 「参照游戏《Mutiny》全 33 关场景分析…**建关的构造依据**」 | ~~`docs/设计/关卡/设计语言.md:1,3`~~ **已消解**：设计语言.md 已并入 `docs/设计/关卡/00-管线与设计语言.md` 并重写，开头明确「只作灵感库，不作为建关的构造依据」 | — | ✅ | 已消解 |
| G5 翻滚裁决 | 「落地翻滚（**忠实转写 Flash 逆向**，【裁决：按逆向实现】）」 | `docs/项目/归档/M4-世界化/大海域世界化.md:100-114` | 「落地翻滚（忠实转写 Flash 逆向，【裁决：按逆向实现】）」「原版行为 / AS2 出处 / Unity 转写」 | ⚠ 有裁决，但裁决内容本身即已废依据 | 改依据（对应 C12） |
| G6 手感章节 | 「手感：**忠实逆向** + 现代层」 | `docs/项目/归档/M4-世界化/设计理念与实现档案.md:67-74` | 「忠实转写成 RollRules 纯 C#」「逆向常量的单位换算全部集中在 RollRules 一处」 | ❌ | 改依据 |
| G7 保护清单 | 「不动（受保护资产）：…17 武器数值」 | ~~`docs/技术/美术翻新-等距像素卡通立项任务书.md:17`~~ **已根除**（git 历史可溯） | 「不动…战斗纯逻辑与 17 武器数值（1161 EditMode + 7 PlayMode 测试）」 | ❌ | 改依据（同口径现役依据 = `Data/WeaponCatalog.cs` 契约 + AGENTS 铁律） |
| G8 竞技场文档 | 「玩法数值以…逆向文档为准」 | ~~`docs/设计/场景/战斗竞技场.md:8,10`~~ **已消解**：文档按等距像素卡通口径重写（现为 `docs/设计/场景.md`），逆向材料已统一标注「早期灵感来源，非设计基准」 | — | ✅ | 已消解 |
| G9 UI 规范·依据列 | 「【依据】逆向 §8.1」队色、28 帧血条、文案 | ~~`docs/设计/UI-UX与中文本地化规范.md:15,81,82,228`~~ **已消解**：旧篇删除，活内容迁入 `docs/设计/UI设计语言.md`；新篇文案出处不再以逆向作队色/血条依据（§6.5 出色列已重写） | — | ✅ | 已消解 |
| G10 投掷提案·依据 | 「逆向 §5.1/§5.2/§5.4/§6.2」逐条 | `docs/技术/投掷机制.md:8,17,24,47,49` | 「数值/公式/回合规则 → 逆向文档」「打分：逆向 §6.2/§6.3 逐行转写」 | ❌（提案文档） | 改依据 |
| G11 参照库调研 | 「已读逆向…确认 M2 需要四类能力」 | `docs/技术/架构/UnityAPI陷阱与参照库公约.md`（原 Unity参照库调研.md，M2 选型过程章节已删） | 「先读逆向文档再定选型」「逆向文档依据」「逆向文档坐标为 Flash 像素」 | ❌ | 改依据 |
| G12 架构总览 | 加武器落点表「数值出处（Flash 逆向文档）」 | `docs/技术/架构/架构总览.md:419` | 「数值出处（Flash 逆向文档）」 | ❌ | 改依据 |
| G13 开发者指南 | 「数值与玩法权威（原版逆向）」 | `docs/项目/开发者指南.md:270,364` | 「逐字段对齐逆向文档 §5.2「武器总表」」「数值与玩法权威（原版逆向）」 | ❌ | 改依据 |
| G14 待办·遗留 | `AtRestSqrMagnitudeEpsilon` 无出处 / 点击引爆语义 | `docs/项目/待办/battle-审计批次遗留.md:12,29` | 「逆向无 0.01 出处，原版静止 = vx==0 且 |vy|<0.2（逆向 §5.2）」 | ❌ | 改依据 |
| G15 交接档·Godot 偏离 | 「与 Godot 基准的唯一一处有意偏离（pirate.tscn bottom_radius 0.4）」 | `docs/项目/交接与恢复指南.md:2103` | 「这是与 Godot 基准的唯一一处有意偏离（pirate.tscn bottom_radius 仍是 0.4）」 | ❌ | 改依据 |

---

## 明确排除（不算「依据可疑」）

- **自陈「提案/待定」、无伪造依据的**：`Campaign/StarRules.cs:8-11`（星评，自陈「本项目设计提案，非 Flash 逆向结论」）、`CrewManagement/CrewProgression.cs:9-12`（成长曲线，自陈「Flash 逆向文档里没有任何…规则」「整体为提案/待定」）、`CrewManagement/CrewRosterCatalog.cs:20-24,52-54`（招募表，自陈「原版 Flash 没有船员系统」）。这些的依据本就是「本项目自创」，不是 Godot/逆向——**不在本清单圈定范围**，但它们同样需要创始人一次性确认为正式设定。
- **自陈「无逆向出处/实现细节」的**：`Battle/AiController.cs:45`（id 质数种子，「数值沿用初版（实现细节，无逆向出处）」）、`Battle/BattleController.cs:353`（`OceanFarClipMin`，「3D 侧口径无逆向出处」）。
- **同名不同义、仅因用词命中的**：`docs/技术/渲染/*` 里对 t3ssel8r / Godot 教程作为**外部技术参照**的引用（非玩法基准）；`docs/项目/交接/**`（按范围排除）；`docs/项目/归档/**`、`docs/审计/**`、`docs/images/**`（按范围排除）。

## 判据上的不确定点（供创始人裁决时留意）

1. **「有契约背书」与「契约内写着取 Flash」的冲突**：`docs/技术/投掷行为契约.md:23,31` 一边把 `0.25 / twangMax` 列为规格（故本清单判「留」），一边写「数值取 Flash…改动须走逆向文档口径」；`docs/技术/3D空间模型对齐.md` §3/§5 同样「**方向/结构**定契约、**数值**取 Flash」。**结构有背书、数值理由作废**——这两类条目（A16/B5/B6、爆炸常数）本清单按「保留文档但重写数值理由」处理，判「留」，但严格说其**数值理由仍需重申**。
2. **「按逆向实现」这种裁决算不算背书**：`docs/项目/归档/M4-世界化/大海域世界化.md:101` 明文「【裁决：按逆向实现】」。形式上有裁决，但裁决内容正是被作废的依据。本清单对 C12/G5 判「改依据」，**取舍权在创始人**。
3. **`3D空间模型对齐.md` §7「Flash 数值不变」是否算背书**：本清单**不认**它（只声明不改、无正面理由）；若创始人认为「不变」即为有效口径，则 §A/§B 多数条目会翻转成「留」。
4. **依赖面**：B1/B2（武器表/枚举）、B5/B10（BalanceConfig）被武器 SO、战斗规则、AI、关卡 XML 键与大批测试直接消费；D6 被预制体生成与调试场消费——**改依据可低风险，删则牵一发动全身**。E1/E2 已被裁决整批删除，属低风险。
5. **覆盖粒度**：本清单以「一个可独立裁决的数值/标识符」为一行；`WeaponCatalog` 17 种武器、`AiEvaluation` 数十个 §6 常量按「同一依据的成组项」合并为一行，未拆到单个字面量级。

## 执行结果（协调者按创始人裁决落地）

- **「改依据」68 项已全部执行**（50 文件，纯注释/md，数值与逻辑未动）。
- **「删」9 项的实际处置**——协调者逐条查引用面后**只删 3 项**：
  - **已删**：一代遗留化石字段 `originalXmlPlayers` / `sourceXmlMaxChests` / `maxChests`
    （自注「仅存档备查」「宝箱未实装占位」），连根拔了 资产载荷 / `LevelData` / `BattlePlan` /
    资产 IO / golden JSON / `.asset` / 冻结签名测试共 17 文件（提交 `0e87ea6f`）。
  - **未删（保留，理由如下）**：
    - `ReferenceHeight` / `UnitsPerTile` / `TargetUnitHeight` / `ReferenceToWorldScale`——
      它们是**角色视觉尺度的定义**（1.85 单位高、每格单位数），现役 `CameraFraming` 等在消费；
      删掉等于删角色尺度，撞 AGENTS「角色模型不许改」。上一轮已改成中性名，依据问题已消。
    - `waterTileY` ——归属**「地图资产统一（全米）」**那条任务（要连格容器一起清），此处单独删会留半截状态。
    - 世界图分层军火（`crewWeapons` / `captainWeapons`）——海图虽已删，但**管线刻意保留待重做**，
      `WorldMapLoadoutTests` 仍在消费；先删与那个决定打架。
- **仍待处理**：`docs/项目/交接与恢复指南.md` 与 `Assets/Editor/TextSampleBuilder.cs`（创始人在建，未动）。
