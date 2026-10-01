using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.TestTools;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 关卡数据资产的**内容门禁**（无头可跑）：资产文本 ↔ golden JSON 的等价、
    /// 全部资产过校验器、以及"迁移没有改变任何数值"的冻结期望值。
    ///
    /// 【三层证据】
    ///   ① 载体一致性：`Assets/Data/**/*.asset` 的正文 == golden JSON 经同一写出器渲染的正文
    ///      （逐字节；行尾归一化）。它保证"资产是唯一真源"这句话可机器复核，而不是口号。
    ///   ② 数据自洽/玩法硬约束：<see cref="LevelAssetRules"/>（与编辑器校验器同一份实现）。
    ///   ③ **语义零漂移**：下面两张冻结期望值表是手抄自迁移前的 C# 目录表
    ///      （`WorldMapCatalog.cs` / `ShowcaseLevels.cs`，迁移后只存在于 git 历史）。
    ///      它们独立于资产与 golden JSON——即使有人用漂移过的数据重跑迁移器，
    ///      这里也会红。改关卡内容属于设计裁决，必须同时更新这张表和 golden JSON。
    /// </summary>
    public class LevelAssetTests
    {
        // ------------------------------------------------------------------
        // ① 载体一致性
        // ------------------------------------------------------------------

        [Test]
        public void GoldenJson_ExistsForEveryAsset()
        {
            string dataRoot = DataRootPath();
            for (int i = 0; i < WorldMapIds.Length; i++)
            {
                string path = WorldMapGolden(dataRoot, WorldMapIds[i]);
                Assert.That(File.Exists(path), Is.True, "缺 golden JSON：" + path);
            }

            for (int i = 0; i < LevelAssetNames.Length; i++)
            {
                string path = LevelGolden(dataRoot, LevelAssetNames[i]);
                Assert.That(File.Exists(path), Is.True, "缺 golden JSON：" + path);
            }
        }

        [Test]
        public void Assets_MatchGoldenJson_ByteForByte()
        {
            string dataRoot = DataRootPath();
            int checkedFiles = 0;

            for (int i = 0; i < WorldMapIds.Length; i++)
            {
                string id = WorldMapIds[i];
                WorldMapAssetPayload payload = ReadWorldMapPayload(dataRoot, id);
                string expected = LevelAssetYaml.WriteWorldMap(payload, id, WorldMapScriptGuid());
                AssertSameText(dataRoot + "/WorldMaps/" + id + ".asset", expected);
                checkedFiles++;
            }

            for (int i = 0; i < LevelAssetNames.Length; i++)
            {
                string name = LevelAssetNames[i];
                LevelAssetPayload payload = ReadLevelPayload(dataRoot, name);
                string expected = LevelAssetYaml.WriteLevel(payload, name, LevelScriptGuid());
                AssertSameText(dataRoot + "/Levels/" + name + ".asset", expected);
                checkedFiles++;
            }

            Assert.That(checkedFiles, Is.EqualTo(WorldMapIds.Length + LevelAssetNames.Length));
        }

        /// <summary>
        /// golden JSON 的确定性：载荷 → 文本 → 载荷 → 文本必须逐字节一致
        /// （重复跑迁移器不产生 diff）。
        /// </summary>
        [Test]
        public void GoldenJson_RoundTripIsStable()
        {
            for (int i = 0; i < WorldMapIds.Length; i++)
            {
                string first = LevelAssetJson.Write(ReadWorldMapPayload(DataRootPath(), WorldMapIds[i]));
                WorldMapAssetPayload again = LevelAssetJson.ReadWorldMap(first);
                Assert.That(again, Is.Not.Null, WorldMapIds[i] + " 回读失败");
                Assert.That(LevelAssetJson.Write(again), Is.EqualTo(first),
                    WorldMapIds[i] + " golden JSON 往返不稳定（重复迁移会产生 diff）");
            }

            for (int i = 0; i < LevelAssetNames.Length; i++)
            {
                string name = LevelAssetNames[i];
                string first = LevelAssetJson.Write(ReadLevelPayload(DataRootPath(), name));
                LevelAssetPayload again = LevelAssetJson.ReadLevel(first);
                Assert.That(again, Is.Not.Null, name + " 回读失败");
                Assert.That(LevelAssetJson.Write(again), Is.EqualTo(first),
                    name + " golden JSON 往返不稳定");
            }
        }

        // ------------------------------------------------------------------
        // ①' schema 校验：代差资产按坏数据拒绝（LevelAssetJson 读侧）
        // ------------------------------------------------------------------

        [Test]
        public void GoldenJson_SchemaMismatch_RejectedAsBadData()
        {
#if UNITY_EDITOR
            // 两条读入口各留痕一条 Error（LogAssert 按序消费），返回 null 走既有坏数据路径。
            // 无头域整体让位：schema 拒绝走 Log.Error 直通 Debug.LogError，ECall 脱离 Unity 运行时
            // 必抛 SecurityException（harness 边界），EditMode 为权威判定环境。
            LogAssert.Expect(LogType.Error, new Regex("\\[LevelAssetJson\\].*schema"));
            LogAssert.Expect(LogType.Error, new Regex("\\[LevelAssetJson\\].*schema"));

            Assert.That(LevelAssetJson.ReadLevel("{\"schema\": 999, \"kind\": \"level\"}"), Is.Null,
                "schema 高于当前版本的关卡资产必须拒绝，不能拿旧语义当新语义错读");
            Assert.That(LevelAssetJson.ReadWorldMap("{\"schema\": 999, \"kind\": \"world_map\"}"), Is.Null,
                "schema 高于当前版本的海图资产必须拒绝");
#else
            Assert.Ignore("schema 拒绝留痕走 Debug.LogError，无头域 ECall 必抛，EditMode 为权威判定");
#endif
        }

        [Test]
        public void GoldenJson_SchemaCurrent_MinimalPayloadAccepted()
        {
            // 对照组：schema 等于当前版本的最小载荷能通过校验——证明上面的拒绝只来自 schema
            // （版本号引用 LevelAssetSchema.Version，版本递增时本用例不红）
            string schema = LevelAssetSchema.Version.ToString();

            Assert.That(LevelAssetJson.ReadLevel("{\"schema\": " + schema + ", \"kind\": \"level\"}"),
                Is.Not.Null, "当前 schema 的最小载荷应通过读侧校验");
            Assert.That(LevelAssetJson.ReadWorldMap("{\"schema\": " + schema + ", \"kind\": \"world_map\"}"),
                Is.Not.Null, "当前 schema 的最小海图载荷应通过读侧校验");
        }

        // ------------------------------------------------------------------
        // ② 加载路径与内容门禁
        // ------------------------------------------------------------------

        /// <summary>
        /// 关卡数据的加载通道：**编辑器/播放器走 Unity 资产清单，无头验证台走 golden JSON**。
        ///
        /// 这两条通道由 <c>LevelAssetLibrary</c> 自己选（<c>Resources.Load</c> 是原生 ECall，
        /// 脱离 Unity 运行时必抛 <c>SecurityException</c>），所以期望值随环境而变，用
        /// <c>UNITY_EDITOR</c> 区分——EditMode 测试定义它，无头验证台不定义。
        /// 断言强度不变：两条通道都必须给出**与目录表一致**的海图/关卡数
        /// （八张世界海图已删除待重做 ⇒ 当前海图 0 张；关卡 2 已删除 2026-09-22 ⇒ 现存 1、3、4、5）。
        /// </summary>
        [Test]
        public void Library_LoadedFrom_MatchesEnvironment_WithCatalogCounts()
        {
            // 先触达数据（惰性加载在首次取用时发生），再断言来源——LoadedFrom 在加载前恒为 None。
            int maps = LevelAssetLibrary.WorldMaps.Count;
            int levels = LevelAssetLibrary.Levels.Count;

#if UNITY_EDITOR
            Assert.That(LevelAssetLibrary.LoadedFrom, Is.EqualTo(LevelAssetLibrary.Source.UnityAssets),
                "编辑器/播放器读的应是 Unity 资产清单：" + LevelAssetLibrary.Diagnostic);
#else
            Assert.That(LevelAssetLibrary.LoadedFrom, Is.EqualTo(LevelAssetLibrary.Source.GoldenJson),
                "无头验证台读的应是 golden JSON：" + LevelAssetLibrary.Diagnostic);
#endif
            Assert.That(maps, Is.EqualTo(WorldMapIds.Length));
            Assert.That(levels, Is.EqualTo(LevelAssetNames.Length));
            Assert.That(WorldMapCatalog.Count, Is.EqualTo(WorldMapIds.Length));
        }

        /// <summary>
        /// golden JSON 兜底通道**单独**钉一遍，两条环境都跑：强制跳过 Unity 资产清单后，
        /// 数据必须仍能凑齐与目录表一致的海图/关卡数，且与清单里的语义一致（条数对不上即红）。
        /// 没有这条，兜底通道就只在无头环境被间接覆盖，Unity 侧坏了没人知道。
        /// </summary>
        [Test]
        public void Library_ForcedGoldenJson_YieldsSameContentAsCatalog()
        {
            LevelAssetLibrary.ForceGoldenJsonOnly(true);
            try
            {
                int maps = LevelAssetLibrary.WorldMaps.Count;
                int levels = LevelAssetLibrary.Levels.Count;
                Assert.That(LevelAssetLibrary.LoadedFrom, Is.EqualTo(LevelAssetLibrary.Source.GoldenJson),
                    "强制 golden 通道后来源应变成 golden JSON：" + LevelAssetLibrary.Diagnostic);
                Assert.That(maps, Is.EqualTo(WorldMapIds.Length),
                    "golden JSON 的海图数应与资产清单一致");
                Assert.That(levels, Is.EqualTo(LevelAssetNames.Length),
                    "golden JSON 的关卡数应与资产清单一致");
            }
            finally
            {
                LevelAssetLibrary.ForceGoldenJsonOnly(false);
            }
        }

        [Test]
        public void Catalog_IsOrderedByLevelNumber()
        {
            IReadOnlyList<WorldMapAssetPayload> maps = LevelAssetLibrary.WorldMaps;
            for (int i = 1; i < maps.Count; i++)
                Assert.That(maps[i].levelNumber, Is.GreaterThan(maps[i - 1].levelNumber),
                    "海图顺序必须是关卡号升序（选关页/小地图按顺序取，顺序变了等于改界面）");

            IReadOnlyList<LevelAssetPayload> levels = LevelAssetLibrary.Levels;
            for (int i = 1; i < levels.Count; i++)
                Assert.That(levels[i].levelNumber, Is.GreaterThan(levels[i - 1].levelNumber));
        }

        [Test]
        public void AllWorldMaps_PassContentGate([ValueSource(nameof(WorldMapPayloads))] WorldMapAssetPayload payload)
        {
            Assume.That(LevelAssetLibrary.WorldMaps.Count, Is.GreaterThan(0),
                "当前工程零海图（八张世界海图已删除待重做），内容门禁待重做后自动生效");

            WorldMapDefinition runtime = WorldMapFromAsset.ToRuntime(payload);
            Assert.That(runtime, Is.Not.Null, payload.id + " 无法转成运行时定义");
            AssertProblems(string.Empty, payload.id, LevelAssetRules.Problems(runtime));
        }

        [Test]
        public void AllLevels_PassContentGate([ValueSource(nameof(LevelPayloads))] LevelAssetPayload payload)
        {
            AssertProblems(payload.assetName, payload.assetName, LevelAssetRules.Problems(payload));
        }

        // ------------------------------------------------------------------
        // ③ 语义零漂移：冻结期望值（抄自迁移前的 C# 表）
        // ------------------------------------------------------------------

        /// <summary>
        /// 海图骨架：id|关卡号|图幅|氛围档|远景种子|terrain|horizon|props|spawns|出生点逐条
        /// （team/符号/x/z/luck）‖ 军火（id:count）‖ 远景特征件。
        /// 数值出处：迁移前 `WorldMapCatalog.cs`（git 历史；各图的逐条注释即设计意图）。
        ///
        /// **当前为空**：八张海图已删除待重做 → <c>WorldMaps_MatchFrozenLegacySignature</c> 零用例；
        /// 海图重做时按上面的格式补回（新增海图必须同时补这里与 golden JSON）。
        /// </summary>
        static readonly string[] FrozenWorldMapSignatures = { };

        /// <summary>
        /// 关卡（现存样板关）骨架：关卡号|资产名|显示名|尺幅（米）|waterWorldY
        /// ‖编成（符号/队/世界 x/世界 z/luck）‖军火‖高度场（实心格数/块高总和/加权摘要）‖烘焙件。
        /// 数值出处：迁移前 `ShowcaseLevels.cs` 与各关设计文档 docs/设计/关卡/L0N-*.md。
        /// 关卡 2「碎岛雨」已删除（2026-09-22），号段有意不连续——故表里只有 1、3、4、5 行。
        ///
        /// 【为什么高度场仍报"格/块"】资产的栅格已改存**米高度**（`terrain.heights`），
        /// 这里按 `blockWorldHeight` 折回运行时块数再算摘要——目的正是让摘要值与换单位前**逐字相同**，
        /// 从而把"换单位没有动任何一处地形"变成可机器复核的证据（不是靠人肉看米数对不对）。
        /// </summary>
        static readonly string[] FrozenLevelSignatures =
        {
            "1|cloud_walk|云端漫步|40x30|-0.4||units=redPirate/0/17/13/5;redPirate/0/25/19/5;redPirate/0/21/9/5;redPirateCaptain/0/21/23/5;cabinBoy/1/25/13/1;cabinBoy/1/17/19/1;cabinBoyCaptain/1/27/15/1|air=Dynamite:10|raster=solid48/total422/digest68270|pieces=1/CloudField",
            "3|sky_island|天空之岛|40x30|-0.4||units=redPirate/0/23/11/5;redPirate/0/27/13/5;redPirate/0/23/17/5;redPirateCaptain/0/25/13/5;cabinBoy/1/13/11/5;cabinBoy/1/17/11/5;cabinBoy/1/11/15/5;cabinBoy/1/17/17/5;cabinBoyCaptain/1/13/17/5|air=TidalWave:10|Anchor:10|Seagull:10|raster=solid96/total2688/digest404544|pieces=",
            // L4 废弃化工厂（2026-09-29 入库，提案/待定）：64×44 m 大场地，平地可走 + 建筑足印抬高成掩体
            // （solid704 = 704 个 2m 采样格 − 建筑格）；整场件 pieceId 2 = ChemPlantYard（Blender 手作 FBX）。
            "4|chem_plant|废弃化工厂|64x44|-0.4||units=redPirate/0/11/17/5;redPirate/0/17/19/5;redPirate/0/15/23/5;redPirateCaptain/0/19/21/5;cabinBoy/1/33/9/5;cabinBoy/1/39/11/5;cabinBoy/1/35/13/5;cabinBoy/1/49/7/5;cabinBoyCaptain/1/57/9/5|air=Dynamite:10|raster=solid704/total5500/digest1783200|pieces=2/ChemPlantYard",
            // L5 废弃化工厂·六件并行版（2026-09-29 入库，提案/待定）：56×40 m = 本 kit 场地 1:1；
            // 平地 2 块（单位踩在地坪顶 y=1.0，总装件摆 y=1.0）+ 四个设备区抬高 14 块（+7 m 掩体）；
            // 整场件 pieceId 3 = ChemPlantTeamYard（SceneKit/ChemPlant.fbx，六件并行 kit）。
            "5|chem_plant_team|废弃化工厂·六件版|56x40|-0.4||units=redPirate/0/7/25/5;redPirate/0/13/25/5;redPirate/0/11/27/5;redPirateCaptain/0/9/27/5;cabinBoy/1/39/25/5;cabinBoy/1/45/25/5;cabinBoy/1/41/27/5;cabinBoy/1/49/27/5;cabinBoyCaptain/1/45/23/5|air=Dynamite:10|raster=solid560/total5464/digest1306248|pieces=3/ChemPlantTeamYard",
        };

        [Test]
        public void WorldMaps_MatchFrozenLegacySignature(
            [ValueSource(nameof(WorldMapPayloads))] WorldMapAssetPayload payload)
        {
            Assume.That(LevelAssetLibrary.WorldMaps.Count, Is.GreaterThan(0),
                "当前工程零海图（八张世界海图已删除待重做），冻结期望值表待重做后自动生效");

            string actual = WorldMapSignature(payload);
            string expected = FindSignature(FrozenWorldMapSignatures, payload.id + "|");
            Assert.That(expected, Is.Not.Null, payload.id + " 不在冻结期望值表里（新增海图要同时补表与 golden JSON）");
            Assert.That(actual, Is.EqualTo(expected),
                payload.id + " 的骨架与迁移前不一致（语义漂移）：\n实际 " + actual + "\n期望 " + expected);
        }

        [Test]
        public void Levels_MatchFrozenLegacySignature(
            [ValueSource(nameof(LevelPayloads))] LevelAssetPayload payload)
        {
            string actual = LevelSignature(payload);
            string expected = FindSignature(FrozenLevelSignatures, payload.levelNumber + "|");
            Assert.That(expected, Is.Not.Null, payload.levelNumber + " 不在冻结期望值表里");
            Assert.That(actual, Is.EqualTo(expected),
                payload.levelNumber + " 的骨架与迁移前不一致（语义漂移）：\n实际 " + actual + "\n期望 " + expected);
        }

        // ------------------------------------------------------------------
        // 关卡来源解析（B3 的收口点）
        // ------------------------------------------------------------------

        /// <summary>
        /// 解析优先级：① 出图覆盖 → ② 选关页点选的样板关 → ③ 待战海图 → ④ 兜底关 1。
        /// 本用例钉 ①③④（调用点显式传 <c>pendingShowcaseLevel: 0</c> 表明"无样板关待战"）；
        /// 第 ② 级与两个待战槽位的互斥/复位在 <c>ShowcaseLevelSelectionTests</c>。
        ///
        /// 【零海图口径】待战海图通道在数据上恒不可用（八张海图已删除、目录为空），
        /// 故第 ③ 级用**合成海图**注入（<see cref="SyntheticMap"/>）——本用例验的是解析器的分叉规则，
        /// 与"目录里有没有图"无关。
        /// </summary>
        [Test]
        public void LevelSourceResolver_PriorityIsUnchanged()
        {
            WorldMapDefinition synthetic = SyntheticMap();

            // ① -artReviewLevel 覆盖命中关卡资产 → 走关卡资产（哪怕同时有待战海图）
            //    用关卡 3 而非关卡 1：只有非兜底关号才能证明"覆盖优先于兜底"。
            //    （关卡 2 已删除（2026-09-22），号段有意不连续——覆盖到不存在的关号会落到
            //    兜底关 1 并留告警，不再能承担这条断言。）
            LevelSource overridden = LevelSourceResolver.Resolve(3, synthetic, 0);
            Assert.That(overridden, Is.Not.Null);
            Assert.That(overridden.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(overridden.LevelNumber, Is.EqualTo(3));
            Assert.That(overridden.Notice, Is.Null, "覆盖命中时不该有回落告警");

            // ② 无覆盖 + 有待战海图 → 走海图
            LevelSource worldMap = LevelSourceResolver.Resolve(0, synthetic, 0);
            Assert.That(worldMap.Kind, Is.EqualTo(LevelSourceKind.WorldMap));
            Assert.That(worldMap.WorldMap.Id, Is.EqualTo(synthetic.Id));
            Assert.That(worldMap.CameraWorldSpan, Is.GreaterThan(0f), "海图必须给出相机全景档的图幅");
            Assert.That(worldMap.AmbientTier, Is.Not.Null, "海图必须有氛围档");

            // ③ 无覆盖 + 无待战海图 → 兜底关卡 1（并提示）
            LevelSource fallback = LevelSourceResolver.Resolve(0, null, 0);
            Assert.That(fallback.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(fallback.LevelNumber, Is.EqualTo(LevelSourceResolver.FallbackLevelNumber));
            Assert.That(fallback.Notice, Is.Not.Null, "兜底必须留下提示（原来的 LogWarning 语义）");
            Assert.That(fallback.CameraWorldSpan, Is.EqualTo(0f), "关卡资产路径不设全景档（沿用场景烘焙边界）");
            Assert.That(fallback.AmbientTier, Is.Null, "关卡资产路径保持场景烘焙的氛围档");
        }

        [Test]
        public void LevelSourceResolver_TerrainAndWaterComeFromSource()
        {
            LevelSource worldMap = LevelSourceResolver.Resolve(0, SyntheticMap(), 0);
            Assert.That(worldMap.Terrain, Is.Not.Null);
            Assert.That(worldMap.Terrain.WidthTiles, Is.EqualTo(worldMap.Plan.WidthTiles));
            Assert.That(worldMap.WaterWorldY, Is.EqualTo(LevelGeometry.WaterSurfaceY));

            LevelSource level = LevelSourceResolver.Resolve(1, null, 0);
            Assert.That(level.Terrain, Is.Not.Null);
            Assert.That(level.Terrain.BlocksAt(8, 6), Is.GreaterThan(0), "L1 首个出生点那一格必须是实心");
            Assert.That(level.Terrain.WidthTiles, Is.EqualTo(ShowcaseLevels.WidthTiles));
            Assert.That(level.Terrain.DepthTiles, Is.EqualTo(ShowcaseLevels.DepthTiles));
        }

        /// <summary>
        /// 构造一张最小合成海图，供上面两条用例走"待战海图"分支（零海图下目录里没有真图）。
        /// 内容全空、只给 span 与氛围档——<c>LevelSourceResolver</c> 的纯函数重载只读这几项。
        /// </summary>
        static WorldMapDefinition SyntheticMap()
        {
            return new WorldMapDefinition(
                id: "synthetic_map", displayName: "合成图", levelNumber: WorldMapCatalog.FirstLevelNumber,
                spanX: 100f, spanZ: 100f, ambientTier: "Noon",
                crewWeapons: null, captainWeapons: null,
                terrain: new List<WorldKitPlacement>(),
                horizon: new List<WorldKitPlacement>(),
                props: new List<WorldPropPlacement>(),
                spawns: new List<WorldMapSpawn>(),
                airdropPool: new List<WeaponStack>(),
                horizonSeed: 0,
                horizonFeatures: new List<string>());
        }

        // ------------------------------------------------------------------
        // 签名与辅助
        // ------------------------------------------------------------------

        /// <summary>
        /// 现役海图 id 表。**当前为空**：八张世界海图（101–108）已删除待重做，
        /// 其 `.asset` / `_golden/*.json` / 场景一并从仓库移除；海图重做后把 id 与冻结签名一并补回
        /// （表为空时上面与海图相关的循环与 <c>[ValueSource]</c> 用例自动零用例/真空通过）。
        /// </summary>
        static readonly string[] WorldMapIds = { };

        static readonly string[] LevelAssetNames = { "cloud_walk", "sky_island", "chem_plant", "chem_plant_team" };

        static IEnumerable<WorldMapAssetPayload> WorldMapPayloads()
        {
            // 零海图（八张海图已删除待重做）时产出一个 null 哨兵：NUnit 对**空** [ValueSource] 的
            // 处理两套 runner 不一致（无头验证台静默零用例，Unity EditMode 记成失败），
            // 给一个占位用例、由用例开头的 Assume 跳成 Skipped，海图放回来后逐图用例自动恢复。
            if (LevelAssetLibrary.WorldMaps.Count == 0)
            {
                yield return null;
                yield break;
            }

            foreach (WorldMapAssetPayload payload in LevelAssetLibrary.WorldMaps)
                yield return payload;
        }

        static IEnumerable<LevelAssetPayload> LevelPayloads()
        {
            foreach (LevelAssetPayload payload in LevelAssetLibrary.Levels)
                yield return payload;
        }

        static string WorldMapSignature(WorldMapAssetPayload p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(p.id).Append('|').Append(p.levelNumber).Append('|')
              .Append(Number(p.spanX)).Append('x').Append(Number(p.spanZ)).Append('|')
              .Append(p.ambientTier).Append('|').Append(p.horizonSeed).Append('|')
              .Append(p.terrain.Count).Append('|').Append(p.horizon.Count).Append('|')
              .Append(p.props.Count).Append('|').Append(p.spawns.Count).Append('|');

            for (int i = 0; i < p.spawns.Count; i++)
            {
                SpawnEntry s = p.spawns[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(s.teamIndex).Append('/').Append(s.archetype).Append('/')
                  .Append(Number(s.x)).Append('/').Append(Number(s.z)).Append('/').Append(s.luck);
            }

            sb.Append("||crew=").Append(Stacks(p.crewWeapons))
              .Append("|cap=").Append(Stacks(p.captainWeapons))
              .Append("|air=").Append(Stacks(p.airdropPool))
              .Append("|feats=").Append(string.Join(",", p.horizonFeatures));
            return sb.ToString();
        }

        static string LevelSignature(LevelAssetPayload p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(p.levelNumber).Append('|').Append(p.assetName).Append('|').Append(p.displayName)
              .Append('|').Append(Number(p.sizeX)).Append('x').Append(Number(p.sizeZ)).Append('|')
              .Append(Number(p.waterWorldY)).Append("||units=");

            for (int i = 0; i < p.units.Count; i++)
            {
                LevelUnit u = p.units[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(u.typeName).Append('/').Append(u.teamIndex).Append('/')
                  .Append(Number(u.x)).Append('/').Append(Number(u.z)).Append('/').Append(u.luck);
            }

            // 米高度折回运行时块数（÷ blockWorldHeight）：摘要值与换单位前逐字相同 = 地形零漂移的证据。
            int solid = 0, total = 0;
            long digest = 0;
            List<float> heights = p.terrain.heights;
            for (int i = 0; i < heights.Count; i++)
            {
                int blocks = (int)System.Math.Round(heights[i] / p.terrain.blockWorldHeight);
                if (blocks > 0)
                    solid++;
                total += blocks;
                digest = (digest + (long)blocks * ((i + 1) % 4294967291L)) % 4294967291L;
            }

            sb.Append("|air=").Append(Stacks(p.airdropPool))
              .Append("|raster=solid").Append(solid)
              .Append("/total").Append(total)
              .Append("/digest").Append(digest)
              .Append("|pieces=");

            for (int i = 0; i < p.bakedPieces.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');
                sb.Append(p.bakedPieces[i].pieceId).Append('/').Append(p.bakedPieces[i].instanceName);
            }
            return sb.ToString();
        }

        static string Stacks(List<WeaponStack> stacks)
        {
            if (stacks == null)
                return string.Empty;
            var parts = new List<string>(stacks.Count);
            for (int i = 0; i < stacks.Count; i++)
                parts.Add(((WeaponId)stacks[i].id) + ":" + stacks[i].count);
            return string.Join("|", parts);
        }

        static string Number(float value) => LevelAssetJson.Number(value);

        static string FindSignature(string[] table, string prefix)
        {
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].StartsWith(prefix, System.StringComparison.Ordinal))
                    return table[i];
            }
            return null;
        }

        static void AssertProblems(string label, string what, List<string> problems)
        {
            var fatal = new List<string>();
            for (int i = 0; i < problems.Count; i++)
            {
                // 【告警】是【提案/待定】的尺度口径，只报数不判死（判死后每次调平衡都要改测试）。
                if (!problems[i].StartsWith("【告警】", System.StringComparison.Ordinal))
                    fatal.Add(problems[i]);
            }
            Assert.That(fatal, Is.Empty, what + " 未通过内容门禁：" + string.Join(" ; ", fatal));
        }

        static void AssertSameText(string path, string expected)
        {
            Assert.That(File.Exists(path), Is.True, "缺资产文件：" + path);
            string actual = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.That(actual, Is.EqualTo(expected.Replace("\r\n", "\n")),
                path + " 的正文与 golden JSON 的确定性渲染不一致（有人在 Inspector 里手改过资产？）");
        }

        static string DataRootPath()
        {
            string root = LevelAssetLibrary.DataRootPath;
            Assert.That(root, Is.Not.Null, "找不到 Assets/Data（无头测试需要 golden JSON 才能断言）");
            return root.Replace('\\', '/');
        }

        static string WorldMapGolden(string dataRoot, string id) => dataRoot + "/WorldMaps/_golden/" + id + ".json";

        static string LevelGolden(string dataRoot, string name) => dataRoot + "/Levels/_golden/" + name + ".json";

        static WorldMapAssetPayload ReadWorldMapPayload(string dataRoot, string id)
        {
            WorldMapAssetPayload payload = LevelAssetJson.ReadWorldMap(File.ReadAllText(WorldMapGolden(dataRoot, id)));
            Assert.That(payload, Is.Not.Null, id + " golden JSON 解析失败");
            return payload;
        }

        static LevelAssetPayload ReadLevelPayload(string dataRoot, string name)
        {
            LevelAssetPayload payload = LevelAssetJson.ReadLevel(File.ReadAllText(LevelGolden(dataRoot, name)));
            Assert.That(payload, Is.Not.Null, name + " golden JSON 解析失败");
            return payload;
        }

        static string WorldMapScriptGuid() =>
            LevelAssetYaml.GuidFor("Assets/Scripts/PirateCrew/Data/Levels/WorldMapDefinitionAsset.cs");

        static string LevelScriptGuid() =>
            LevelAssetYaml.GuidFor("Assets/Scripts/PirateCrew/Data/Levels/LevelDefinition.cs");
    }
}
