using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 抛初速「按 Weight 分流」的回归测试（<see cref="LevelGeometry.ThrowVelocityForWeight"/>）。
    ///
    /// 【背景】cannonball weight=0（§5.2「无重力」，逆向口径）此前也被 ThrowLift 强抬约 35° 仰角，
    /// 直线爬升越过出界清理线后被静默销毁（不爆炸、无事件）。修复口径：UsesGravity=false 的弹体
    /// 不加仰角、水平直线飞行，忠于 Flash「无重力直线弹道」语义。
    ///
    /// 【三条链路同源】玩家投掷与 AI 的实弹都经 <see cref="ProjectileSpawnPlanner"/> →
    /// <see cref="LevelGeometry.FlashLaunchVelocityToWorld(float, float, float)"/>，
    /// 预览经 <see cref="ThrowTrajectory.PredictFromFlashSpeed"/>，AI 预测经
    /// <see cref="AiEvaluation.SimulateShot"/>——三者全部落在同一个分流函数
    /// <see cref="LevelGeometry.ThrowVelocityForWeight"/> 上，本文件逐链路断言其初速一致。
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
            // weight>0：逐位等于旧的 ThrowVelocity（固定仰角抬升），既有弹道不受分流影响。
            const float speedPx = 12f;
            Vector3 split = LevelGeometry.ThrowVelocityForWeight(Forward, speedPx, 1f);
            Vector3 legacy = LevelGeometry.ThrowVelocity(Forward, speedPx);

            Assert.AreEqual(legacy.x, split.x, 1e-6f);
            Assert.AreEqual(legacy.y, split.y, 1e-6f);
            Assert.AreEqual(legacy.z, split.z, 1e-6f);
            Assert.Greater(split.y, 0f, "有重力弹体仍由 ThrowLift 抬出仰角");
        }

        // ------------------------------------------------------------------
        // 实弹链路（ProjectileSpawnPlanner，玩家投掷与加农炮共用）
        // ------------------------------------------------------------------

        [Test]
        public void Planner_Cannonball_WeightZero_SpawnsStraightVelocity()
        {
            WeaponStats stats = WeaponCatalog.Get(WeaponId.Cannonball);
            Assert.AreEqual(0f, stats.Weight, 1e-6f, "cannonball weight=0 是逆向口径（§5.2），数值表未变");
            Assert.IsFalse(ProjectileProfile.FromStats(stats).UsesGravity);

            IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                stats, new Vector3(1f, 0.5f, 1f), new Vector3(5f, 0f, 5f), vxFlash: 8f, vyFlash: 0f);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(0f, plan[0].WorldVelocity.y, 0f,
                "加农炮弹（FireCannonToward 也走本链路）不得再带 ThrowLift 仰角");
            Assert.AreEqual(8f * LevelGeometry.FlashSpeedScale,
                new Vector2(plan[0].WorldVelocity.x, plan[0].WorldVelocity.z).magnitude, 1e-5f);
        }

        [Test]
        public void Planner_GravityWeapon_KeepsLegacyLift()
        {
            // cherryBomb weight=1：与旧口径 FlashLaunchVelocityToWorld(vx, vy) 逐位一致。
            WeaponStats stats = WeaponCatalog.Get(WeaponId.CherryBomb);
            Assert.Greater(stats.Weight, 0f);

            IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                stats, new Vector3(1f, 0.5f, 1f), new Vector3(5f, 0f, 5f), vxFlash: 4f, vyFlash: 0f);

            Vector3 legacy = LevelGeometry.FlashLaunchVelocityToWorld(4f, 0f);
            Assert.AreEqual(legacy.x, plan[0].WorldVelocity.x, 1e-6f);
            Assert.AreEqual(legacy.y, plan[0].WorldVelocity.y, 1e-6f);
            Assert.AreEqual(legacy.z, plan[0].WorldVelocity.z, 1e-6f);
            Assert.Greater(plan[0].WorldVelocity.y, 0f);
        }

        // ------------------------------------------------------------------
        // 预览链路（ThrowTrajectory.PredictFromFlashSpeed）= 实弹链路
        // ------------------------------------------------------------------

        [Test]
        public void Preview_FirstSample_MatchesPlannerInitialVelocity_BothWeights()
        {
            // 预览第 1 个采样点 = 起点 +（实弹初速 + 首步重力）× 物理步——Predict 与 PhysX
            // 同为半隐式欧拉（先更新速度再位移），weight=0 时重力项为零退化为纯匀速，
            // 两种重量下预览与实弹取的都是同一个分流函数给出的初速。
            const float vx = 6f, vy = 2.5f;
            float speed = Mathf.Sqrt(vx * vx + vy * vy);
            Vector3 direction = new Vector3(vx, 0f, vy);
            Vector3 origin = new Vector3(3f, 0.5f, 4f);
            var buffer = new Vector3[2];

            foreach (float weight in new[] { 0f, 1f })
            {
                IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                    WeaponCatalog.Get(weight == 0f ? WeaponId.Cannonball : WeaponId.CherryBomb),
                    origin, origin, vx, vy);

                ThrowTrajectory.PredictFromFlashSpeed(
                    origin, direction, speed, weight, buffer, 2, LevelGeometry.FrameSeconds);

                Vector3 firstStepVelocity = plan[0].WorldVelocity
                    + new Vector3(0f, LevelGeometry.WorldGravityY(weight) * LevelGeometry.FrameSeconds, 0f);
                Vector3 expectedFirst = origin + firstStepVelocity * LevelGeometry.FrameSeconds;
                Assert.AreEqual(expectedFirst.x, buffer[0].x, 1e-5f, "weight=" + weight);
                Assert.AreEqual(expectedFirst.y, buffer[0].y, 1e-5f, "weight=" + weight);
                Assert.AreEqual(expectedFirst.z, buffer[0].z, 1e-5f, "weight=" + weight);
            }

            // weight=0 时整条预览线是水平直线（y 恒等于起点高度）。
            ThrowTrajectory.PredictFromFlashSpeed(
                origin, direction, speed, 0f, buffer, 2, LevelGeometry.FrameSeconds);
            Assert.AreEqual(origin.y, buffer[0].y, 1e-5f);
            Assert.AreEqual(origin.y, buffer[1].y, 1e-5f);
        }

        // ------------------------------------------------------------------
        // AI 预测链路（AiEvaluation.SimulateShot）
        // ------------------------------------------------------------------

        [Test]
        public void SimulateShot_ZeroWeight_TravelsStraightAtFlashPixelsPerStep()
        {
            // 无重力弹体的 AI 预测 = 直线：Flash 平面速度 vx px/帧，每物理步恰好前进 vx px
            // （1 步 = 1 帧 @25fps），5 步 × vx=10 → 落点 = 起点 + 50px，纵深不动。
            // 旧口径（强抬仰角 + 零重力）会沿 35° 上斜线漂走，此断言把它钉死在直线口径上。
            var terrain = new AiTerrain(0f, 2000f, 0f, 2000f);
            AiThrowSample sample = AiEvaluation.SimulateShot(
                startX: 100f, startY: 400f, vx: 10f, vy: 0f,
                weight: 0f, terrain: terrain, maxSteps: 5);

            Assert.AreEqual(150f, sample.Ex, 1e-3f, "每步直线前进 vx px");
            Assert.AreEqual(400f, sample.Ey, 1e-3f, "纵深不受重力/抬升影响");
            Assert.IsFalse(sample.Drowned);
        }

        [Test]
        public void SimulateShot_PositiveWeight_Unchanged_StillArcsAndLands()
        {
            // weight=1（角色自抛口径）：仍走抛物线并落到地面平面——分流不改变有重力弹道的落点。
            var terrain = new AiTerrain(0f, 2000f, 0f, 2000f);
            AiThrowSample sample = AiEvaluation.SimulateShot(
                startX: 100f, startY: 400f, vx: 6f, vy: 3f,
                weight: CrewCatalog.Weight, terrain: terrain);

            Assert.IsFalse(sample.Drowned, "2000×200px 竞技场内满步数内应落地而非落水");
            Vector3 world = LevelGeometry.PixelToArena(sample.Ex, sample.Ey);
            Assert.AreEqual(LevelGeometry.GroundTopY, world.y, 1e-4f, "落点必须在地面上");
        }
    }
}
