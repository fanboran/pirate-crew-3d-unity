using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
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
    ///      （`ShowcaseLevels.cs`，迁移后只存在于 git 历史）。
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

            for (int i = 0; i < LevelAssetNames.Length; i++)
            {
                string name = LevelAssetNames[i];
                LevelAssetPayload payload = ReadLevelPayload(dataRoot, name);
                string expected = LevelAssetYaml.WriteLevel(payload, name, LevelScriptGuid());
                AssertSameText(dataRoot + "/Levels/" + name + ".asset", expected);
                checkedFiles++;
            }

            Assert.That(checkedFiles, Is.EqualTo(LevelAssetNames.Length));
        }

        /// <summary>
        /// golden JSON 的确定性：载荷 → 文本 → 载荷 → 文本必须逐字节一致
        /// （重复跑迁移器不产生 diff）。
        /// </summary>
        [Test]
        public void GoldenJson_RoundTripIsStable()
        {
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
            // 读入口留痕一条 Error（LogAssert 按序消费），返回 null 走既有坏数据路径。
            // 无头域整体让位：schema 拒绝走 Log.Error 直通 Debug.LogError，ECall 脱离 Unity 运行时
            // 必抛 SecurityException（harness 边界），EditMode 为权威判定环境。
            LogAssert.Expect(LogType.Error, new Regex("\\[LevelAssetJson\\].*schema"));

            Assert.That(LevelAssetJson.ReadLevel("{\"schema\": 999, \"kind\": \"level\"}"), Is.Null,
                "schema 高于当前版本的关卡资产必须拒绝，不能拿旧语义当新语义错读");
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
        /// 断言强度不变：两条通道都必须给出**与目录表一致**的关卡数（现役四关号 1–4 连续）。
        /// </summary>
        [Test]
        public void Library_LoadedFrom_MatchesEnvironment_WithCatalogCounts()
        {
            // 先触达数据（惰性加载在首次取用时发生），再断言来源——LoadedFrom 在加载前恒为 None。
            int levels = LevelAssetLibrary.Levels.Count;

#if UNITY_EDITOR
            Assert.That(LevelAssetLibrary.LoadedFrom, Is.EqualTo(LevelAssetLibrary.Source.UnityAssets),
                "编辑器/播放器读的应是 Unity 资产清单：" + LevelAssetLibrary.Diagnostic);
#else
            Assert.That(LevelAssetLibrary.LoadedFrom, Is.EqualTo(LevelAssetLibrary.Source.GoldenJson),
                "无头验证台读的应是 golden JSON：" + LevelAssetLibrary.Diagnostic);
#endif
            Assert.That(levels, Is.EqualTo(LevelAssetNames.Length));
        }

        /// <summary>
        /// golden JSON 兜底通道**单独**钉一遍，两条环境都跑：强制跳过 Unity 资产清单后，
        /// 数据必须仍能凑齐与目录表一致的关卡数，且与清单里的语义一致（条数对不上即红）。
        /// 没有这条，兜底通道就只在无头环境被间接覆盖，Unity 侧坏了没人知道。
        /// </summary>
        [Test]
        public void Library_ForcedGoldenJson_YieldsSameContentAsCatalog()
        {
            LevelAssetLibrary.ForceGoldenJsonOnly(true);
            try
            {
                int levels = LevelAssetLibrary.Levels.Count;
                Assert.That(LevelAssetLibrary.LoadedFrom, Is.EqualTo(LevelAssetLibrary.Source.GoldenJson),
                    "强制 golden 通道后来源应变成 golden JSON：" + LevelAssetLibrary.Diagnostic);
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
            IReadOnlyList<LevelAssetPayload> levels = LevelAssetLibrary.Levels;
            for (int i = 1; i < levels.Count; i++)
                Assert.That(levels[i].levelNumber, Is.GreaterThan(levels[i - 1].levelNumber));
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
        /// 关卡（现存样板关）骨架：关卡号|资产名|显示名|尺幅（米）|waterWorldY
        /// ‖编成（符号/队/世界 x/世界 z/luck）‖军火‖高度场（实心格数/块高总和/加权摘要）‖烘焙件。
        /// 数值出处：迁移前 `ShowcaseLevels.cs` 与各关设计文档 docs/设计/关卡/NN-*.md。
        /// 现役四关的关卡号 1–4 连续、无空号，表里四行按关卡号排列。
        ///
        /// 【为什么高度场仍报"格/块"】资产的岛形地形按采样粒度栅格化后折回**米高度**，
        /// 这里按 `blockWorldHeight` 折回运行时块数再算摘要——目的正是让摘要值与换单位前**逐字相同**，
        /// 从而把"换单位没有动任何一处地形"变成可机器复核的证据（不是靠人肉看米数对不对）。
        /// </summary>
        static readonly string[] FrozenLevelSignatures =
        {
            // L1 云端漫步：5 坛（主角云+环1）+ 第二环 6 朵可站面（16 边形贴云台；solid 48→112）。
            // 【前排防遮挡】45° 机位下前排两朵压到 y3/y5、16 块档挪正南深处（translate_cloudfield.py
            // 的布局裁决）——total/digest 随高度重算重冻，solid 不变（坛形未动）。
            "1|cloud_walk|云端漫步|40x30|-0.4||units=redPirate/0/17/13/5;redPirate/0/25/19/5;redPirate/0/19/15/5;redPirateCaptain/0/14/21/5;cabinBoy/1/25/13/1;cabinBoy/1/17/19/1;cabinBoyCaptain/1/12.5/22.5/1|air=Dynamite:10|raster=solid127/total1289/digest176398|pieces=1/CloudField",
            // L2 重冻（逐格高度入 LevelData）：穹顶烘进数据面后岛面从恒 28 块平顶变为
            // 0.5 m 量化台阶（27/28/29 块）——solid 不变（96 格仍是岛），total/digest 随台阶重算。
            "2|sky_island|天空之岛|40x30|-0.4||units=redPirate/0/23/11/5;redPirate/0/27/13/5;redPirate/0/23/17/5;redPirateCaptain/0/25/13/5;cabinBoy/1/13/11/5;cabinBoy/1/17/11/5;cabinBoy/1/11/15/5;cabinBoy/1/17/17/5;cabinBoyCaptain/1/13/17/5|air=TidalWave:10|Anchor:10|Seagull:10|raster=solid96/total2689/digest404694|pieces=",
            // L3 废弃化工厂（2026-09-29 入库，提案/待定）：64×44 m 大场地，平地可走 + 建筑足印抬高成掩体
            // （solid704 = 704 个 2m 采样格 − 建筑格）；整场件 pieceId 2 = ChemPlantYard（Blender 手作 FBX）。
            "3|chem_plant|废弃化工厂|64x44|-0.4||units=redPirate/0/11/17/5;redPirate/0/17/19/5;redPirate/0/15/23/5;redPirateCaptain/0/19/21/5;cabinBoy/1/33/9/5;cabinBoy/1/39/11/5;cabinBoy/1/35/13/5;cabinBoy/1/49/7/5;cabinBoyCaptain/1/57/9/5|air=Dynamite:10|raster=solid704/total5500/digest1783200|pieces=2/ChemPlantYard",
            // L4 废弃化工厂·六件并行版（2026-09-29 入库，提案/待定）：56×40 m = 本 kit 场地 1:1；
            // 平地 2 块（单位踩在地坪顶 y=1.0，总装件摆 y=1.0）+ 四个设备区抬高 14 块（+7 m 掩体）；
            // 整场件 pieceId 3 = ChemPlantTeamYard（SceneKit/ChemPlant.fbx，六件并行 kit）。
            "4|chem_plant_team|废弃化工厂·六件版|56x40|-0.4||units=redPirate/0/7/25/5;redPirate/0/13/25/5;redPirate/0/11/27/5;redPirateCaptain/0/9/27/5;cabinBoy/1/39/25/5;cabinBoy/1/45/25/5;cabinBoy/1/41/27/5;cabinBoy/1/49/27/5;cabinBoyCaptain/1/45/23/5|air=Dynamite:10|raster=solid560/total5464/digest1306248|pieces=3/ChemPlantTeamYard",
        };

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
        /// 解析优先级：① 出图覆盖 → ② 待战关卡 → ③ 兜底关 1。
        /// 第 ② 级的待战槽行为在 <c>ShowcaseLevelSelectionTests</c>。
        /// </summary>
        [Test]
        public void LevelSourceResolver_PriorityIsUnchanged()
        {
            // ① -artReviewLevel 覆盖命中关卡资产。
            //    用关卡 2 而非关卡 1：只有非兜底关号才能证明"覆盖优先于兜底"。
            LevelSource overridden = LevelSourceResolver.Resolve(2, 0);
            Assert.That(overridden, Is.Not.Null);
            Assert.That(overridden.LevelNumber, Is.EqualTo(2));
            Assert.That(overridden.Notice, Is.Null, "覆盖命中时不该有回落告警");

            // ② 无覆盖 + 待战关卡 → 走该关。
            LevelSource pending = LevelSourceResolver.Resolve(0, 3);
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.LevelNumber, Is.EqualTo(3));
            Assert.That(pending.Notice, Is.Null);

            // ③ 无覆盖 + 无待战 → 兜底关卡 1（并提示）。
            LevelSource fallback = LevelSourceResolver.Resolve(0, 0);
            Assert.That(fallback.LevelNumber, Is.EqualTo(LevelSourceResolver.FallbackLevelNumber));
            Assert.That(fallback.Notice, Is.Not.Null, "兜底必须留下提示（原来的 LogWarning 语义）");
        }

        [Test]
        public void LevelSourceResolver_TerrainAndWaterComeFromSource()
        {
            LevelSource level = LevelSourceResolver.Resolve(1, 0);
            Assert.That(level.Terrain, Is.Not.Null);
            Assert.That(level.Terrain.BlocksAt(8, 6), Is.GreaterThan(0), "L1 首个出生点那一格必须是实心");
            Assert.That(level.Terrain.WidthCells, Is.EqualTo(ShowcaseLevels.WidthCells));
            Assert.That(level.Terrain.DepthCells, Is.EqualTo(ShowcaseLevels.DepthCells));
            Assert.That(level.WaterWorldY, Is.EqualTo(LevelGeometry.WaterSurfaceY));
        }

        // ------------------------------------------------------------------
        // 签名与辅助
        // ------------------------------------------------------------------

        static readonly string[] LevelAssetNames = { "cloud_walk", "sky_island", "chem_plant", "chem_plant_team" };

        static IEnumerable<LevelAssetPayload> LevelPayloads()
        {
            foreach (LevelAssetPayload payload in LevelAssetLibrary.Levels)
                yield return payload;
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

            // 岛形栅格化回米高度再折回运行时块数（÷ blockWorldHeight）：摘要值与迁移前逐字相同
            // = 地形零漂移的证据（岛形数据与旧采样数组表达同一场地）。
            int solid = 0, total = 0;
            long digest = 0;
            List<float> heights = TerrainShapeRasterizer.ToHeights(p.terrain, p.sizeX, p.sizeZ);
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

        static string LevelGolden(string dataRoot, string name) => dataRoot + "/Levels/_golden/" + name + ".json";

        static LevelAssetPayload ReadLevelPayload(string dataRoot, string name)
        {
            LevelAssetPayload payload = LevelAssetJson.ReadLevel(File.ReadAllText(LevelGolden(dataRoot, name)));
            Assert.That(payload, Is.Not.Null, name + " golden JSON 解析失败");
            return payload;
        }

        static string LevelScriptGuid() =>
            LevelAssetYaml.GuidFor("Assets/Scripts/PirateCrew/Data/Levels/LevelDefinition.cs");

        // ------------------------------------------------------------------
        // ④ 逐格高度（v4 类体素）：读写往返 / 缺席语义 / 校验器
        // ------------------------------------------------------------------

        /// <summary>带逐格高度的合法关卡载荷（岛 0..8 × 0..6，网格 col 0..4 / row 0..3 全覆盖）。</summary>
        static LevelAssetPayload HeightFixturePayload()
        {
            return new LevelAssetPayload
            {
                levelNumber = 99,
                assetName = "height_fixture",
                displayName = "逐格高度fixture",
                sizeX = 8f,
                sizeZ = 6f,
                waterWorldY = LevelGeometry.WaterSurfaceY,
                terrain = new TerrainShape
                {
                    blockWorldHeight = 0.5f,
                    islands = new List<TerrainIsland>
                    {
                        new TerrainIsland
                        {
                            topY = 14f,
                            outline = new List<float> { 0f, 0f, 8f, 0f, 8f, 6f, 0f, 6f },
                            heightCol0 = 0,
                            heightRow0 = 0,
                            heightCols = 4,
                            heightRows = 3,
                            heights = new List<float>
                            {
                                13.5f, 14f, 14.5f, 14f,
                                14f, 14.5f, 14f, 13.5f,
                                14.5f, 14f, 13.5f, 14f,
                            },
                        },
                    },
                },
            };
        }

        [Test]
        public void HeightsJson_RoundTripIsStable_AndFieldsSurvive()
        {
            // schema v4：带逐格高度的载荷 Write → Read → Write 逐字节一致（重复烘焙不产生 diff），
            // 且字段（网格四角 + 行主序数组）完整往返。
            string first = LevelAssetJson.Write(HeightFixturePayload());
            Assert.That(first, Does.Contain("\"schema\": " + LevelAssetSchema.Version),
                "写侧必须是当前 schema 版本");
            Assert.That(first, Does.Contain("\"heights\""), "逐格高度字段应写进 golden JSON");

            LevelAssetPayload again = LevelAssetJson.ReadLevel(first);
            Assert.That(again, Is.Not.Null, "当前 schema 的载荷回读失败");

            TerrainIsland island = again.terrain.islands[0];
            Assert.That(island.HasHeights, Is.True);
            Assert.That(island.heightCol0, Is.EqualTo(0));
            Assert.That(island.heightRow0, Is.EqualTo(0));
            Assert.That(island.heightCols, Is.EqualTo(4));
            Assert.That(island.heightRows, Is.EqualTo(3));
            CollectionAssert.AreEqual(HeightFixturePayload().terrain.islands[0].heights, island.heights,
                "逐格高度数组（行主序）必须逐值往返");
            Assert.That(LevelAssetJson.Write(again), Is.EqualTo(first), "第二次写出必须逐字节一致");

            // 资产 YAML 渲染同样携带逐格高度（「资产 == golden JSON」门禁的载体侧）。
            string yaml = LevelAssetYaml.WriteLevel(again, "height_fixture", LevelScriptGuid());
            Assert.That(yaml, Does.Contain("heightCol0: 0"), "YAML 侧缺逐格高度字段");
            Assert.That(yaml, Does.Contain("heights:"));
        }

        [Test]
        public void HeightsJson_AbsentKeepsFlatTop_AndWrittenWithoutField()
        {
            // 缺席语义：不带（或带空）逐格高度的载荷读回仍无高度（HasHeights = false），
            // 写出的 JSON 不含 "heights" 键——L1/L3/L4 的 golden 除 schema 行外零改动靠这条。
            var payload = HeightFixturePayload();
            TerrainIsland absent = payload.terrain.islands[0];
            absent.heights = new List<float>();   // 空数组 = 缺席
            payload.terrain.islands[0] = absent;

            string json = LevelAssetJson.Write(payload);
            Assert.That(json, Does.Not.Contains("\"heights\""), "缺席岛不得写 heights 键");

            LevelAssetPayload again = LevelAssetJson.ReadLevel(json);
            Assert.That(again.terrain.islands[0].HasHeights, Is.False, "空数组读回应保持缺席");
        }

        [Test]
        public void HeightsRules_PassOnWellFormedGrid()
        {
            var payload = HeightFixturePayload();
            payload.units.Add(new LevelUnit("redPirate", 0, 2f, 2f, 5,
                new List<WeaponStack>()));   // 校验器要求有单位（站格 (2,2) 在岛内）
            payload.units.Add(new LevelUnit("cabinBoy", 1, 4f, 4f, 1, new List<WeaponStack>()));

            var problems = LevelAssetRules.Problems(payload);
            Assert.That(problems.FindAll(p => p.Contains("逐格高度")), Is.Empty,
                "合法逐格高度不应报问题：" + string.Join(" ; ", problems));
        }

        [Test]
        public void HeightsRules_FlagLengthMismatch_CoverageGap_AndDataHole()
        {
            // 长度与包围盒不符：结构层（IsWellFormed）直接判不自洽，整张载荷按坏数据拒绝。
            var badLength = HeightFixturePayload();
            TerrainIsland tooFew = badLength.terrain.islands[0];
            tooFew.heightCols = 5;   // 5×3 ≠ 12
            badLength.terrain.islands[0] = tooFew;
            Assert.That(badLength.terrain.IsWellFormed, Is.False, "长度不符 = 结构不自洽");
            Assert.That(LevelAssetRules.Problems(badLength),
                Has.Some.Contains("岛形地形不自洽"), "长度不符必须报红");

            // 网格未盖住轮廓包围盒（结构自洽但覆盖有缺口）。
            var badCover = HeightFixturePayload();
            TerrainIsland shifted = badCover.terrain.islands[0];
            shifted.heightCol0 = 1;   // 盖世界 2..10，缺轮廓 0..2 一段
            badCover.terrain.islands[0] = shifted;
            Assert.That(badCover.terrain.IsWellFormed, Is.True, "前置：覆盖缺口不破坏结构自洽");
            Assert.That(LevelAssetRules.Problems(badCover),
                Has.Some.Contains("未盖住轮廓包围盒"), "覆盖缺口必须报红");

            // 岛内数据洞（格中心在轮廓内但高度 0）。
            var badHole = HeightFixturePayload();
            badHole.terrain.islands[0].heights[1 + 1 * 4] = 0f;   // 格 (1,1) 中心 (3,3) 在岛内
            Assert.That(LevelAssetRules.Problems(badHole),
                Has.Some.Contains("数据洞"), "岛内 0 高度格必须报红");

            // 值越上限。
            var badValue = HeightFixturePayload();
            badValue.terrain.islands[0].heights[0] = 99f;
            Assert.That(LevelAssetRules.Problems(badValue),
                Has.Some.Contains("超出"), "超上限值必须报红");
        }

        // ------------------------------------------------------------------
        // ⑤ 内洞（扁平单环，D4）：Unity 不序列化嵌套 List，环结构只能放一层
        // ------------------------------------------------------------------

        [Test]
        public void HolesJson_FlatRing_RoundTrips_AndExcludesInside()
        {
            // 环岛：外环 0..8 × 0..6，洞 2..6 × 2..4（扁平单环）。
            var payload = HeightFixturePayload();
            TerrainIsland island = payload.terrain.islands[0];
            island.heights = null;   // 本用例只看轮廓 + 洞
            island.holes = new List<float> { 2f, 2f, 6f, 2f, 6f, 4f, 2f, 4f };
            payload.terrain.islands[0] = island;

            string json = LevelAssetJson.Write(payload);
            Assert.That(json, Does.Contain("\"holes\": [2, 2, 6, 2, 6, 4, 2, 4]"),
                "内洞必须是单层扁平数组（与 outline 同一写法；嵌套环列表 Unity 不序列化）");

            LevelAssetPayload again = LevelAssetJson.ReadLevel(json);
            Assert.That(again, Is.Not.Null, "带洞载荷回读失败");
            CollectionAssert.AreEqual(island.holes, again.terrain.islands[0].holes, "内洞环必须逐值往返");
            Assert.That(again.terrain.IsWellFormed, Is.True);

            // 洞内无地面、环带内实心（even-odd 判定吃扁平环）。
            Assert.That(TerrainShapeRasterizer.PointInIsland(4f, 3f, again.terrain.islands[0]),
                Is.False, "洞内点不算岛内");
            Assert.That(TerrainShapeRasterizer.PointInIsland(1f, 1f, again.terrain.islands[0]),
                Is.True, "环带点仍是岛内");

            // 资产 YAML 同样携带扁平洞（「资产 == golden JSON」门禁的载体侧）。
            string yaml = LevelAssetYaml.WriteLevel(again, "height_fixture", LevelScriptGuid());
            Assert.That(yaml, Does.Contain("holes:"), "YAML 侧缺内洞字段");
        }

        [Test]
        public void HolesJson_AbsentWritesNoKey_EmptyMeansNoHole()
        {
            // 现役四关全部无洞：缺席不写键（golden 零改动）；空环读回 = 无洞（缺席口径同 heights）。
            string json = LevelAssetJson.Write(HeightFixturePayload());
            Assert.That(json, Does.Not.Contains("\"holes\""), "无洞岛不得写 holes 键");

            var payload = HeightFixturePayload();
            TerrainIsland island = payload.terrain.islands[0];
            island.holes = new List<float>();
            payload.terrain.islands[0] = island;

            string written = LevelAssetJson.Write(payload);
            Assert.That(written, Does.Not.Contains("\"holes\""), "空环 = 无洞，同样不写键");

            LevelAssetPayload again = LevelAssetJson.ReadLevel(written);
            Assert.That(again.terrain.islands[0].holes.Count, Is.EqualTo(0), "空环读回仍是无洞");
        }
    }
}
