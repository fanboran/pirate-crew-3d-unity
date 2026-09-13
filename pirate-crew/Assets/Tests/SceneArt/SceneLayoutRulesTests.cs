using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 布局规则、确定性随机、天空档与调色板的纯 C# 用例（无头可跑）。
    ///
    /// 【为什么单独测规则类】规则类（<see cref="SceneLayoutRules"/>）是"可玩性约束"的唯一落点：
    /// 纵深分层（§7.3）与出生位净空（§9.5）一旦被改坏，会以"某些道具莫名消失/莫名挡视野"的形式
    /// 只出现在观感里。这里把每条阈值单独钉一遍，回归时能立刻定位到是哪一条。
    /// </summary>
    [TestFixture]
    public class SceneLayoutRulesTests
    {
        // ------------------------------------------------------------------
        // 纵深分层（场景文档 §2.4 / §7.3）
        // ------------------------------------------------------------------

        [Test]
        public void BandOf_SplitsAtFourAndThirteen()
        {
            Assert.AreEqual(SceneDepthBand.Far, SceneLayoutRules.BandOf(0f));
            Assert.AreEqual(SceneDepthBand.Far, SceneLayoutRules.BandOf(4f));
            Assert.AreEqual(SceneDepthBand.Mid, SceneLayoutRules.BandOf(4.01f));
            Assert.AreEqual(SceneDepthBand.Mid, SceneLayoutRules.BandOf(12.99f));
            Assert.AreEqual(SceneDepthBand.Near, SceneLayoutRules.BandOf(13f));
            Assert.AreEqual(SceneDepthBand.Near, SceneLayoutRules.BandOf(16.9f));
        }

        [Test]
        public void HeightRules_MatchSceneDocOcclusionTable()
        {
            // 规则 1：≤0.6 任意位置可放。
            Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(0.6f, 16f, true));
            Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(0.2f, 8f, true));

            // 规则 2：0.6-1.5 不得放 Z≥13。
            Assert.IsFalse(SceneLayoutRules.IsHeightAllowedAt(0.61f, 13f, true));
            Assert.IsFalse(SceneLayoutRules.IsHeightAllowedAt(1.5f, 15f, true));
            Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(1.5f, 12.99f, true));

            // 规则 3：>1.5 只允许 Z≤4 或竞技场外。
            Assert.IsFalse(SceneLayoutRules.IsHeightAllowedAt(1.51f, 4.01f, true));
            Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(1.51f, 4f, true));
            Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(6.9f, 2.6f, true));
            Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(6.9f, 16f, false));
        }

        [Test]
        public void SpawnClearance_IsOnePointFiveUnitsAroundCellCenter()
        {
            var spawns = new[]
            {
                new Vector2Int(7, 11),
                new Vector2Int(43, 10),
            };

            // 格心 = (gridX+0.5, gridY+0.5)（与 LevelGeometry.GridToArena 同口径）。
            Assert.IsTrue(SceneLayoutRules.IsInsideSpawnClearance(7.5f, 11.5f, spawns), "正好在格心 → 命中");
            Assert.IsTrue(SceneLayoutRules.IsInsideSpawnClearance(7.5f + 1.4f, 11.5f, spawns), "1.4 < 1.5 → 命中");
            Assert.IsFalse(SceneLayoutRules.IsInsideSpawnClearance(7.5f + 1.6f, 11.5f, spawns), "1.6 > 1.5 → 安全");
            Assert.IsFalse(SceneLayoutRules.IsInsideSpawnClearance(7.5f, 11.5f + 1.6f, spawns));
            Assert.IsTrue(SceneLayoutRules.IsInsideSpawnClearance(43.5f - 1f, 10.5f, spawns));
        }

        // ------------------------------------------------------------------
        // 出生位安全性（§9.5 / §4.4）
        // ------------------------------------------------------------------

        [Test]
        public void SpawnCellSafety_RequiresRaisedLandAboveWater()
        {
            var cells = new int[4 * 4];
            cells[1 + 1 * 4] = 3;   // (1,1) 有抬升
            var grid = new TileTerrainGrid(4, 4, cells, 0.25f);

            Assert.IsTrue(SceneLayoutRules.IsSpawnCellSafe(grid, 1, 1), "有抬升且在基础地面之上 → 安全");
            Assert.IsFalse(SceneLayoutRules.IsSpawnCellSafe(grid, 0, 0), "0 块格不满足 §9.5 的『滩头列 ≥1 块』");
            Assert.IsFalse(SceneLayoutRules.IsSpawnCellSafe(null, 0, 0), "无网格 → 不安全");

            // 水面在 y=-0.2，基础地面 y=0；即使 0 块格也不落水，但那条规则更严（要求 ≥1 块）。
            Assert.IsFalse(LevelGeometry.IsBelowWater(LevelGeometry.GroundTopY, LevelGeometry.WaterSurfaceY));
        }

        [Test]
        public void SpawnHeightOffset_TracksColumnBlocks()
        {
            var cells = new int[4 * 4];
            cells[2 + 2 * 4] = 5;
            var grid = new TileTerrainGrid(4, 4, cells, 0.25f);

            // 平坦计划（GridToArena）恒为 y=0.25；运行时口径 = 地表 + 0.25。
            Assert.AreEqual(LevelGeometry.UnitPivotHeight, LevelGeometry.GridToArena(2, 2).y, 1e-6f);
            Assert.AreEqual(5 * 0.25f, SceneLayoutRules.SpawnHeightOffsetFromFlatPlan(grid, 2, 2), 1e-6f);
            Assert.AreEqual(0f, SceneLayoutRules.SpawnHeightOffsetFromFlatPlan(grid, 0, 0), 1e-6f);

            // 脚底贴合：由规则类推出的脚底 Y 就是该格地表。
            Assert.AreEqual(grid.SurfaceWorldY(2, 2),
                SceneLayoutRules.SpawnFootWorldY(grid, 2, 2, LevelGeometry.UnitPivotHeight), 1e-5f);
            Assert.AreEqual(grid.SurfaceWorldY(0, 0),
                SceneLayoutRules.SpawnFootWorldY(grid, 0, 0, LevelGeometry.UnitPivotHeight), 1e-5f);
        }

        // ------------------------------------------------------------------
        // 密度与矩形约束
        // ------------------------------------------------------------------

        [Test]
        public void CoverCountRange_IsSixToTen()
        {
            Assert.IsFalse(SceneLayoutRules.IsCoverCountInRange(5));
            Assert.IsTrue(SceneLayoutRules.IsCoverCountInRange(6));
            Assert.IsTrue(SceneLayoutRules.IsCoverCountInRange(10));
            Assert.IsFalse(SceneLayoutRules.IsCoverCountInRange(11));
        }

        [Test]
        public void ArenaRect_HelpersBehaveOnBoundary()
        {
            Assert.IsTrue(SceneLayoutRules.FitsInsideArena(0.5f, 0.5f, 0.4f, 50f, 17f));
            Assert.IsFalse(SceneLayoutRules.FitsInsideArena(0.3f, 0.5f, 0.4f, 50f, 17f), "越出西边界");
            Assert.IsFalse(SceneLayoutRules.FitsInsideArena(25f, 16.8f, 0.4f, 50f, 17f), "越出南边界");
            Assert.IsTrue(SceneLayoutRules.IsInsideArenaRect(-3f, 5f, 50f, 17f, 4f), "外扩 4 单位内算「场内带」");
            Assert.IsFalse(SceneLayoutRules.IsInsideArenaRect(-5f, 5f, 50f, 17f, 4f));
        }

        [Test]
        public void RidgeInsideArena_RejectsOverhang()
        {
            Assert.IsTrue(SceneLayoutRules.RidgeInsideArena(23f, 38f, 50f));
            Assert.IsFalse(SceneLayoutRules.RidgeInsideArena(23f, 51f, 50f), "中脊不得越出东边界");
            Assert.IsFalse(SceneLayoutRules.RidgeInsideArena(-1f, 38f, 50f));
            Assert.IsFalse(SceneLayoutRules.RidgeInsideArena(30f, 30f, 50f), "退化区间应判 false");
        }

        // ------------------------------------------------------------------
        // 确定性随机 / 哈希
        // ------------------------------------------------------------------

        [Test]
        public void SceneArtRandom_IsDeterministicAndInRange()
        {
            var a = new SceneArtRandom(12345);
            var b = new SceneArtRandom(12345);

            for (int i = 0; i < 64; i++)
            {
                float x = a.Next01();
                float y = b.Next01();
                Assert.AreEqual(x, y, 0f, "同种子必须给出同一序列（跨 Mono/.NET 一致）");
                Assert.That(x, Is.InRange(0f, 1f));
            }

            var c = new SceneArtRandom(54321);
            Assert.AreNotEqual(a.Next01(), c.Next01(), "不同种子不应给出同一序列");
        }

        [Test]
        public void SceneArtRandom_RangeInt_StaysInHalfOpenInterval()
        {
            var rng = new SceneArtRandom(7);
            for (int i = 0; i < 200; i++)
            {
                int v = rng.RangeInt(3, 9);
                Assert.That(v, Is.InRange(3, 8));
            }

            Assert.AreEqual(4, rng.RangeInt(4, 4), "空区间返回下界");
            Assert.AreEqual(4, rng.RangeInt(4, 1));
        }

        [Test]
        public void SceneArtHash_IsDeterministicAndSignedHashIsSymmetric()
        {
            Assert.AreEqual(SceneArtHash.Hash01(3, 5, 7), SceneArtHash.Hash01(3, 5, 7), 0f);
            Assert.That(SceneArtHash.Hash01(3, 5, 7), Is.InRange(0f, 1f));
            Assert.AreNotEqual(SceneArtHash.Hash01(3, 5, 7), SceneArtHash.Hash01(5, 3, 7));

            float s = SceneArtHash.SignedHash(11, 13, 17);
            Assert.That(s, Is.InRange(-1f, 1f));
            Assert.AreEqual(SceneArtHash.SignedHash(11, 13, 17), s, 0f);
        }

        // ------------------------------------------------------------------
        // 天空三档（原版档位分配 + AI 提案色值）
        // ------------------------------------------------------------------

        [Test]
        public void SkyTier_LevelMappingMatchesOriginalGroups()
        {
            // 出处：docs/参考游戏逆向-海盗军团抢宝藏-静态.md:676（1-5/16-21 → 1，6-10/22-27 → 2，11-15/28-33 → 3）。
            Assert.AreEqual(1, SkyTierCatalog.TierIndexForLevel(1));
            Assert.AreEqual(1, SkyTierCatalog.TierIndexForLevel(5));
            Assert.AreEqual(2, SkyTierCatalog.TierIndexForLevel(6));
            Assert.AreEqual(2, SkyTierCatalog.TierIndexForLevel(10));
            Assert.AreEqual(3, SkyTierCatalog.TierIndexForLevel(11));
            Assert.AreEqual(3, SkyTierCatalog.TierIndexForLevel(15));
            Assert.AreEqual(1, SkyTierCatalog.TierIndexForLevel(16));
            Assert.AreEqual(1, SkyTierCatalog.TierIndexForLevel(21));
            Assert.AreEqual(2, SkyTierCatalog.TierIndexForLevel(22));
            Assert.AreEqual(3, SkyTierCatalog.TierIndexForLevel(33));
            Assert.AreEqual(1, SkyTierCatalog.TierIndexForLevel(0), "越界回落档 1");
            Assert.AreEqual(1, SkyTierCatalog.TierIndexForLevel(99));
        }

        [Test]
        public void SkyTier_Level1UsesNoonTierWithSceneDocValues()
        {
            SkyTierCatalog.SkyTier tier = SkyTierCatalog.ForLevel(1);

            Assert.AreEqual(1, tier.Index);
            Assert.AreEqual("#4DA6D9", tier.ZenithHex, "档 1 天顶色（场景文档 §6.1 表，AI 提案）");
            Assert.AreEqual("#C8DDF0", tier.HorizonHex, "档 1 地平线色 = 雾色基准");
            Assert.AreEqual("1-5 / 16-21", tier.LevelGroups);

            // 色值必须能解析（不能是 Hex() 的品红兜底）。
            Assert.AreNotEqual(Color.magenta, SceneArtPalette.Hex(tier.ZenithHex));
            Assert.AreNotEqual(Color.magenta, SceneArtPalette.Hex(tier.HorizonHex));
            Assert.AreNotEqual(Color.magenta, SceneArtPalette.Hex(tier.GroundBounceHex));
        }

        [Test]
        public void Palette_HexParsesGddValues_AndAlphaOverloadWorks()
        {
            Color danger = SceneArtPalette.Hex(SceneArtPalette.Danger);
            Assert.AreEqual("#CC2222", SceneArtPalette.Danger, "危险色出处：GDD §10.4（gdd.md:833）");
            Assert.AreNotEqual(Color.magenta, danger);
            Assert.AreEqual(1f, danger.a, 1e-5f);

            Color faded = SceneArtPalette.Hex(SceneArtPalette.Danger, 0.5f);
            Assert.AreEqual(0.5f, faded.a, 1e-5f);
            Assert.AreEqual(danger.r, faded.r, 1e-5f);

            Assert.AreNotEqual(Color.magenta, SceneArtPalette.Hex(SceneArtPalette.Outline));
            Assert.AreEqual("#2A2A2A", SceneArtPalette.Outline, "描边色出处：GDD §10.4（gdd.md:831）");
        }
    }
}
