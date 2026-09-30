using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 抛初速「按 Weight 分流」的回归测试（<see cref="LevelGeometry.ThrowVelocityForWeight"/>）。
    ///
    /// 【口径】weight &gt; 0 走固定仰角抬升（抛物线）；weight == 0 不加仰角、水平直线。
    /// 标准炸弹恒 weight &gt; 0（<see cref="StandardBombRules.Weight"/>），走抛物线弹道。
    ///
    /// 【两条链路同源】实弹生成经 <see cref="ProjectileSpawnPlanner"/> →
    /// <see cref="LevelGeometry.FlashLaunchVelocityToWorld(float, float, float)"/>，
    /// 预览经 <see cref="ThrowTrajectory.PredictFromFlashSpeed"/>——两条链路落在同一个
    /// 分流函数上，本文件断言其初速一致（预览 = 实弹）。
    /// </summary>
    [TestFixture]
    public class ThrowWeightSplitTests
    {
        static readonly Vector3 Forward = Vector3.forward;

        // ------------------------------------------------------------------
        // 分流函数本体（LevelGeometry.ThrowVelocityForWeight）
        // ------------------------------------------------------------------

        [Test]
        public void ZeroWeight_NoLift_VelocityIsFlatHorizontal()
        {
            // weight=0（UsesGravity=false）：y 分量严格为 0，水平直线；模长仍按 FlashSpeedScale。
            const float speedPx = 30f;
            Vector3 v = LevelGeometry.ThrowVelocityForWeight(Forward, speedPx, 0f);

            Assert.AreEqual(0f, v.y, 0f, "无重力弹体的初速 y 分量必须严格为 0（直线弹道）");
            Assert.AreEqual(speedPx * LevelGeometry.FlashSpeedScale, v.magnitude, 1e-5f);
            Assert.Greater(v.z, 0f, "水平方向语义不变（Flash py → 世界 Z）");
        }

        [Test]
        public void ZeroDirection_ReturnsZero_NoNaN()
        {
            Assert.AreEqual(0f, LevelGeometry.ThrowVelocityForWeight(Vector3.zero, 20f, 0f).sqrMagnitude, 1e-8f);
            Assert.AreEqual(0f, LevelGeometry.ThrowVelocityForWeight(Vector3.zero, 20f, 1f).sqrMagnitude, 1e-8f);
        }

        [Test]
        public void PositiveWeight_Unchanged_EqualsThrowVelocityWithLift()
        {
            // weight>0：逐位等于 ThrowVelocity（固定仰角抬升），抛物线弹道不受分流影响。
            const float speedPx = 12f;
            Vector3 split = LevelGeometry.ThrowVelocityForWeight(Forward, speedPx, 1f);
            Vector3 lifted = LevelGeometry.ThrowVelocity(Forward, speedPx);

            Assert.AreEqual(lifted.x, split.x, 1e-6f);
            Assert.AreEqual(lifted.y, split.y, 1e-6f);
            Assert.AreEqual(lifted.z, split.z, 1e-6f);
            Assert.Greater(split.y, 0f, "有重力弹体由 ThrowLift 抬出仰角");
        }

        // ------------------------------------------------------------------
        // 实弹链路（ProjectileSpawnPlanner）：任意武器 → 同一条标准抛掷
        // ------------------------------------------------------------------

        [Test]
        public void Planner_AnyWeapon_SpawnsStandardThrowFromOwner()
        {
            // 标准炸弹 weight>0：抬升仰角的抛物线初速；生成点 = 投掷者位置、非 kinematic。
            Assert.Greater(StandardBombRules.Weight, 0f, "标准炸弹恒吃重力（抛物线弹道的前提）");

            foreach (WeaponId id in new[] { WeaponId.CherryBomb, WeaponId.Anchor, WeaponId.Cannon })
            {
                IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                    WeaponCatalog.Get(id), new Vector3(1f, 0.5f, 1f), new Vector3(5f, 0f, 5f),
                    vxFlash: 4f, vyFlash: 0f);

                Assert.AreEqual(1, plan.Count, id.ToString());
                Assert.IsFalse(plan[0].Kinematic, id.ToString());
                Assert.AreEqual(new Vector3(1f, 0.5f, 1f), plan[0].WorldPosition, id.ToString());

                Vector3 expected = LevelGeometry.FlashLaunchVelocityToWorld(
                    4f, 0f, StandardBombRules.Weight);
                Assert.AreEqual(expected.x, plan[0].WorldVelocity.x, 1e-6f, id.ToString());
                Assert.AreEqual(expected.y, plan[0].WorldVelocity.y, 1e-6f, id.ToString());
                Assert.AreEqual(expected.z, plan[0].WorldVelocity.z, 1e-6f, id.ToString());
                Assert.Greater(plan[0].WorldVelocity.y, 0f, id + " 应带 ThrowLift 仰角");
            }
        }

        // ------------------------------------------------------------------
        // 预览链路（ThrowTrajectory.PredictFromFlashSpeed）= 实弹链路
        // ------------------------------------------------------------------

        [Test]
        public void Preview_FirstSample_MatchesPlannerInitialVelocity()
        {
            // 预览第 1 个采样点 = 起点 +（实弹初速 + 首步重力）× 物理步——Predict 与 PhysX
            // 同为半隐式欧拉（先更新速度再位移），两者取的都是同一个分流函数给出的初速。
            const float vx = 6f, vy = 2.5f;
            float speed = Mathf.Sqrt(vx * vx + vy * vy);
            Vector3 direction = new Vector3(vx, 0f, vy);
            Vector3 origin = new Vector3(3f, 0.5f, 4f);
            float weight = StandardBombRules.Weight;
            var buffer = new Vector3[2];

            IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                WeaponCatalog.Get(WeaponId.CherryBomb), origin, origin, vx, vy);

            ThrowTrajectory.PredictFromFlashSpeed(
                origin, direction, speed, weight, buffer, 2, LevelGeometry.FrameSeconds);

            Vector3 firstStepVelocity = plan[0].WorldVelocity
                + new Vector3(0f, LevelGeometry.WorldGravityY(weight) * LevelGeometry.FrameSeconds, 0f);
            Vector3 expectedFirst = origin + firstStepVelocity * LevelGeometry.FrameSeconds;
            Assert.AreEqual(expectedFirst.x, buffer[0].x, 1e-5f);
            Assert.AreEqual(expectedFirst.y, buffer[0].y, 1e-5f);
            Assert.AreEqual(expectedFirst.z, buffer[0].z, 1e-5f);
        }
    }
}
