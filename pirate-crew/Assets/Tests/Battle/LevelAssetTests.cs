using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using PirateCrew.SceneArt;
using UnityEngine;

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
        // ② 加载路径与内容门禁
        // ------------------------------------------------------------------

        /// <summary>
        /// 关卡数据的加载通道：**编辑器/播放器走 Unity 资产清单，无头验证台走 golden JSON**。
        ///
        /// 这两条通道由 <c>LevelAssetLibrary</c> 自己选（<c>Resources.Load</c> 是原生 ECall，
        /// 脱离 Unity 运行时必抛 <c>SecurityException</c>），所以期望值随环境而变，用
        /// <c>UNITY_EDITOR</c> 区分——EditMode 测试定义它，无头验证台不定义。
        /// 断言强度不变：两条通道都必须给出 8 张海图 / 3 张关卡。
        /// </summary>
        [Test]
        public void Library_LoadedFrom_MatchesEnvironment_WithAllEightMapsAndThreeLevels()
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
        /// 数据必须仍能凑齐 8 张海图 / 3 张关卡，且与清单里的语义一致（条数对不上即红）。
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
        /// </summary>
        static readonly string[] FrozenWorldMapSignatures =
        {
            "wreck_hymn|101|150x150|Noon|101|14|4|23|6|0/redPirate/50/72/5;0/redPirate/56.6/76.2/5;0/redPirateCaptain/54/79/5;1/bluePirate/92/74/2;1/bluePirate/98/76/2;1/bluePirateCaptain/104/78/2||crew=CherryBomb:10|Dynamite:2|cap=CherryBomb:10|Dynamite:5|Dynamite:5|Anchor:1|air=CherryBomb:10|Dynamite:10|Banana:10|RumBottle:10|Seagull:10|feats=WhaleSurfacing",
            "atoll_ring|102|190x190|Noon|102|29|4|14|8|0/redPirate/57/93/5;0/redPirate/62/97/5;0/redPirate/59.5/98/5;0/redPirateCaptain/62/92/5;1/bluePirate/127/93/2;1/bluePirate/132/97/2;1/bluePirate/129.5/98/2;1/bluePirateCaptain/132/92/2||crew=CherryBomb:10|Mine:1|cap=CherryBomb:10|Dynamite:5|Mine:4|TidalWave:1|air=CherryBomb:10|ParachuteBomb:10|Anchor:1|Seagull:1|feats=LeviathanTentacle,WhaleSurfacing",
            "ghost_harbor|103|220x220|Dusk|103|34|5|31|8|0/redPirate/95/66/5;0/redPirate/97/71/5;0/redPirate/101/52/5;0/redPirateCaptain/108/66/5;1/bluePirate/144/94/2;1/bluePirate/148/103/2;1/bluePirate/158/93/2;1/bluePirateCaptain/173.5/90/2||crew=CherryBomb:10|Dynamite:2|cap=CherryBomb:10|Dynamite:5|Dynamite:5|VoodooDoll:1|air=CherryBomb:10|RumBottle:10|VoodooDoll:1|ParachuteBomb:2|feats=GiantRibs,WhaleSurfacing",
            "turtle_back|104|240x240|Noon|104|25|6|30|10|0/redPirate/85/117/5;0/redPirate/91/123/5;0/redPirate/85/121.5/5;0/redPirate/91/117/5;0/redPirateCaptain/88/120/5;1/bluePirate/153/117/2;1/bluePirate/159/123/2;1/bluePirate/153/121.5/2;1/bluePirate/159/117/2;1/bluePirateCaptain/156/120/2||crew=CherryBomb:10|Mine:1|cap=CherryBomb:10|Dynamite:5|Mine:4|Cannon:1|air=CherryBomb:10|Banana:10|Dynamite:10|Boulder:2|Seagull:2|TidalWave:1|feats=WhaleSurfacing,LeviathanTentacle",
            "mangrove_veil|105|180x180|Dusk|105|26|6|22|8|0/redPirate/33/82/5;0/redPirate/39/87/5;0/redPirate/33/85.5/5;0/redPirateCaptain/39/81/5;1/bluePirate/141/82/2;1/bluePirate/147/87/2;1/bluePirate/141/85.5/2;1/bluePirateCaptain/147/81/2||crew=CherryBomb:10|Mine:2|cap=CherryBomb:10|Dynamite:5|Mine:5|Seagull:1|air=CherryBomb:10|Banana:10|Mine:2|RumBottle:2|feats=LeviathanTentacle",
            "spiral_throne|106|260x260|Noon|106|23|5|27|10|0/redPirate/159/160/5;0/redPirate/167/168/5;0/redPirate/159/168/5;0/redPirate/167/160/5;0/redPirateCaptain/163/170/5;1/bluePirate/181/122/2;1/bluePirate/189/128/2;1/bluePirate/181/127/2;1/bluePirate/189/122/2;1/bluePirateCaptain/185/130/2||crew=CherryBomb:10|Dynamite:1|cap=CherryBomb:10|Dynamite:5|Dynamite:4|TidalWave:1|air=CherryBomb:10|Dynamite:10|Cannon:3|Anchor:2|ParachuteBomb:2|TidalWave:1|feats=GiantRibs,LeviathanTentacle",
            "storm_cape|107|200x200|Storm|107|28|5|28|8|0/redPirate/50/96/5;0/redPirate/55/103/5;0/redPirate/60/96/5;0/redPirateCaptain/58/104/5;1/bluePirate/153/96/2;1/bluePirate/158/103/2;1/bluePirate/163/96/2;1/bluePirateCaptain/161/104/2||crew=CherryBomb:10|Dynamite:2|cap=CherryBomb:10|Dynamite:5|Dynamite:5|Cannon:1|air=CherryBomb:10|TidalWave:2|Anchor:2|Seagull:2|Dynamite:2|ParachuteBomb:10|feats=LeviathanTentacle,GiantRibs",
            "sunken_gate|108|280x280|Dusk|108|46|7|40|12|0/redPirate/96/134/5;0/redPirate/103/142/5;0/redPirate/96/144/5;0/redPirate/104/133/5;0/redPirate/97/148.5/5;0/redPirateCaptain/104/148/5;1/bluePirate/201/171/2;1/bluePirate/209/179/2;1/bluePirate/217/171/2;1/bluePirate/203/187/2;1/bluePirate/213/188/2;1/bluePirateCaptain/221/180/2||crew=CherryBomb:10|Dynamite:2|cap=CherryBomb:10|Dynamite:5|Dynamite:5|VoodooDoll:1|Anchor:1|air=CherryBomb:10|Dynamite:1|Boulder:1|Banana:1|Mine:1|ParachuteBomb:1|RumBottle:1|PiecesOfEight:1|GunpowderBarrel:1|WoodenCrate:1|Anchor:1|Seagull:1|TidalWave:1|VoodooDoll:1|Cannon:1|feats=GiantRibs,LeviathanTentacle,WhaleSurfacing",
        };

        /// <summary>
        /// 关卡（样板三关）骨架：关卡号|资产名|显示名|格子|waterTileY|maxChests|sourceXmlMaxChests
        /// ‖编成（符号/队/格/luck）‖军火‖高度场（实心格数/块高总和/加权摘要）‖烘焙件。
        /// 数值出处：迁移前 `ShowcaseLevels.cs` 与各关设计文档 docs/设计/关卡/L0N-*.md。
        /// </summary>
        static readonly string[] FrozenLevelSignatures =
        {
            "1|cloud_walk|云端漫步|20x15|14|3|3||units=redPirate/0/8/6/5;redPirate/0/12/9/5;redPirate/0/10/4/5;redPirateCaptain/0/10/11/5;cabinBoy/1/12/6/1;cabinBoy/1/8/9/1;cabinBoyCaptain/1/13/7/1|air=Dynamite:10|raster=solid48/total422/digest68270|pieces=0/DangerBorder;1/CloudField",
            "2|islet_rain|碎岛雨|20x15|14|3|3||units=redPirate/0/3/3/5;redPirate/0/6/3/5;redPirate/0/3/5/5;redPirateCaptain/0/6/5/5;cabinBoy/1/16/11/2;cabinBoy/1/13/11/2;cabinBoy/1/16/9/2;cabinBoyCaptain/1/13/9/2|air=Mine:10|RumBottle:10|Banana:10|raster=solid69/total69/digest10269|pieces=0/DangerBorder;2/Islets_L02",
            "3|sky_island|天空之岛|20x15|14|3|3||units=redPirate/0/11/5/5;redPirate/0/13/6/5;redPirate/0/11/8/5;redPirateCaptain/0/12/6/5;cabinBoy/1/6/5/5;cabinBoy/1/8/5/5;cabinBoy/1/5/7/5;cabinBoy/1/8/8/5;cabinBoyCaptain/1/6/8/5|air=TidalWave:10|Anchor:10|Seagull:10|raster=solid96/total2688/digest404544|pieces=0/DangerBorder",
        };

        [Test]
        public void WorldMaps_MatchFrozenLegacySignature(
            [ValueSource(nameof(WorldMapPayloads))] WorldMapAssetPayload payload)
        {
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

        [Test]
        public void LevelSourceResolver_PriorityIsUnchanged()
        {
            // ① -artReviewLevel 覆盖命中关卡资产 → 走关卡资产（哪怕同时有待战海图）
            LevelSource overridden = LevelSourceResolver.Resolve(2, WorldMapCatalog.All[0]);
            Assert.That(overridden, Is.Not.Null);
            Assert.That(overridden.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(overridden.LevelNumber, Is.EqualTo(2));
            Assert.That(overridden.Notice, Is.Null, "覆盖命中时不该有回落告警");

            // ② 无覆盖 + 有待战海图 → 走海图
            LevelSource worldMap = LevelSourceResolver.Resolve(0, WorldMapCatalog.All[0]);
            Assert.That(worldMap.Kind, Is.EqualTo(LevelSourceKind.WorldMap));
            Assert.That(worldMap.WorldMap.Id, Is.EqualTo(WorldMapCatalog.All[0].Id));
            Assert.That(worldMap.CameraWorldSpan, Is.GreaterThan(0f), "海图必须给出相机全景档的图幅");
            Assert.That(worldMap.AmbientTier, Is.Not.Null, "海图必须有氛围档");

            // ③ 无覆盖 + 无待战海图 → 兜底关卡 1（并提示）
            LevelSource fallback = LevelSourceResolver.Resolve(0, null);
            Assert.That(fallback.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(fallback.LevelNumber, Is.EqualTo(LevelSourceResolver.FallbackLevelNumber));
            Assert.That(fallback.Notice, Is.Not.Null, "兜底必须留下提示（原来的 LogWarning 语义）");
            Assert.That(fallback.CameraWorldSpan, Is.EqualTo(0f), "关卡资产路径不设全景档（沿用场景烘焙边界）");
            Assert.That(fallback.AmbientTier, Is.Null, "关卡资产路径保持场景烘焙的氛围档");
        }

        [Test]
        public void LevelSourceResolver_TerrainAndWaterComeFromSource()
        {
            LevelSource worldMap = LevelSourceResolver.Resolve(0, WorldMapCatalog.All[0]);
            Assert.That(worldMap.Terrain, Is.Not.Null);
            Assert.That(worldMap.Terrain.WidthTiles, Is.EqualTo(worldMap.Plan.WidthTiles));
            Assert.That(worldMap.WaterWorldY, Is.EqualTo(LevelGeometry.WaterSurfaceY));

            LevelSource level = LevelSourceResolver.Resolve(1, null);
            Assert.That(level.Terrain, Is.Not.Null);
            Assert.That(level.Terrain.BlocksAt(8, 6), Is.GreaterThan(0), "L1 首个出生点那一格必须是实心");
            Assert.That(level.Terrain.WidthTiles, Is.EqualTo(ShowcaseLevels.WidthTiles));
            Assert.That(level.Terrain.DepthTiles, Is.EqualTo(ShowcaseLevels.DepthTiles));
        }

        // ------------------------------------------------------------------
        // 签名与辅助
        // ------------------------------------------------------------------

        static readonly string[] WorldMapIds =
        {
            "wreck_hymn", "atoll_ring", "ghost_harbor", "turtle_back",
            "mangrove_veil", "spiral_throne", "storm_cape", "sunken_gate",
        };

        static readonly string[] LevelAssetNames = { "cloud_walk", "islet_rain", "sky_island" };

        static IEnumerable<WorldMapAssetPayload> WorldMapPayloads()
        {
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
              .Append('|').Append(p.widthTiles).Append('x').Append(p.depthTiles).Append('|')
              .Append(Number(p.waterTileY)).Append('|').Append(p.maxChests).Append('|')
              .Append(p.sourceXmlMaxChests).Append("||units=");

            for (int i = 0; i < p.units.Count; i++)
            {
                LevelUnit u = p.units[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(u.typeName).Append('/').Append(u.teamIndex).Append('/')
                  .Append(u.gridX).Append('/').Append(u.gridY).Append('/').Append(u.luck);
            }

            int solid = 0, total = 0;
            long digest = 0;
            List<int> blocks = p.terrain.blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] > 0)
                    solid++;
                total += blocks[i];
                digest = (digest + (long)blocks[i] * ((i + 1) % 4294967291L)) % 4294967291L;
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
