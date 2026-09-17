using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="FloatingPropView.EligibleForFloatingView"/> 资格判定测试（批次 F）：
    /// 保守白名单（Buoy / Boat|Ship 且非 Beached）+ 贴水检查（|y − WaterSurfaceY| ≤ 0.5）。
    /// 判定纯函数、无实例化（MonoBehaviour 类型只调静态方法，无头安全）。
    /// </summary>
    [TestFixture]
    public class FloatingPropEligibilityTests
    {
        static float WaterY => LevelGeometry.WaterSurfaceY; // -0.4

        // ------------------------------------------------------------------
        // 名字白名单
        // ------------------------------------------------------------------

        [Test]
        public void BuoyRing_AtWaterLevel_Hits()
        {
            // 现役唯一漂浮件（sunken_gate props，吸附后 y = WaterSurfaceY）；
            // 装配层传入的是 go.name（带组前缀）。
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("Prop_BuoyRing", WaterY));
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("Terrain_BuoyRing", WaterY));
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("BuoyRing", 0f)); // 目录 y=0 → 距水面 0.4 ≤ 0.5
        }

        [Test]
        public void RowboatBeached_NeverHits()
        {
            // 搁浅船件：含 "Boat" 但含 "Beached" → 即使 y 恰在水面也不命中。
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Prop_RowboatBeached", WaterY));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("RowboatBeached", 0f));
        }

        [Test]
        public void ShipWheelPost_InWater_HitsByWhitelist()
        {
            // 含 "Ship" 字面命中。开阔水面的吸附件（atoll_ring props，吸附后 y=WaterSurfaceY）
            // 随波起伏语义正确；站面顶的同名件由贴水阈值排除（见下）。
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("Prop_ShipWheelPost", WaterY));
        }

        [Test]
        public void NonKeywordAssets_NeverHit()
        {
            // 现役 terrain/horizon 全部不含关键词 → BuildKitPlacement 路径当前零命中（事实钉值）。
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Terrain_WreckBowHalf", 0f));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Horizon_FarFleet", 0f));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Terrain_PierLong", 0f));
        }

        [Test]
        public void MatchingIsCaseSensitiveOrdinal()
        {
            // 保守序数匹配：资产 id 一律 PascalCase，小写变体不命中（防误扩）。
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("buoyring", WaterY));
        }

        [Test]
        public void NullOrEmpty_NeverHits()
        {
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView(null, WaterY));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("", WaterY));
        }

        // ------------------------------------------------------------------
        // 贴水检查（|y − WaterSurfaceY| ≤ 0.5，含边界）
        // ------------------------------------------------------------------

        [Test]
        public void YDeltaWithinLimit_Hits_EdgeInclusive()
        {
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("Prop_LifeBoat", WaterY + 0.5f),
                "恰在 +0.5 上界应命中（≤ 语义）");
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("Prop_LifeBoat", WaterY - 0.5f),
                "恰在 -0.5 下界应命中（≤ 语义）");
            Assert.IsTrue(FloatingPropView.EligibleForFloatingView("Prop_LifeBoat", WaterY + 0.4f));
        }

        [Test]
        public void YDeltaExceedsLimit_Fails()
        {
            // 高空/深水放置都要挡掉。
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Prop_LifeBoat", WaterY + 0.51f));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Prop_LifeBoat", WaterY - 0.51f));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Prop_SailShip", 3.0f));
        }

        [Test]
        public void ShipWheelPost_OnStand_Excluded()
        {
            // 回归钉：sunken_gate 的 ShipWheelPost 落在 0.5 档站面顶（y=0.5 → 距水面 0.9），
            // 在站面上起伏会"悬空"穿帮——0.5 阈值必须把它排除（开阔水面的同名件仍命中）。
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Prop_ShipWheelPost", 0.5f));
            Assert.IsFalse(FloatingPropView.EligibleForFloatingView("Prop_ShipWheelPost", 1.0f));
        }
    }
}
