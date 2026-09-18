using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient.Tests
{
    /// <summary>
    /// 活物行为规则的纯 C# 用例（海鸥路径 / 螃蟹状态 / 鱼群 boids）。
    ///
    /// 【最重要的一条】<c>GullPlans_NeverEnterNoFlyZone</c>：
    /// 对**导演实际使用的那份轨道布局**（<see cref="GullFlightRules.BuildPlans"/>）
    /// 连续采样 8000 帧，断言任何一帧都不在投掷视线中央区域内。
    /// 这条把"可读性红线"从口头约定变成可回归的几何约束。
    /// </summary>
    [TestFixture]
    public class AmbientCreatureRulesTests
    {
        static AmbientArena Level1Arena()
        {
            return AmbientArena.FromTiles(50f, 17f);
        }

        // ------------------------------------------------------------------
        // 海鸥
        // ------------------------------------------------------------------

        [Test]
        public void GullOrbit_IsContinuousAndBounded()
        {
            var orbit = new GullOrbit(new Vector3(-6f, 8.6f, -7f), 6f, 3.5f,
                0.22f, 0.31f, 0.15f, 0.4f, 1.1f, 2.3f, 1.4f);

            Vector3 previous = orbit.Evaluate(0f);
            for (int i = 1; i <= 4000; i++)
            {
                Vector3 p = orbit.Evaluate(i * 0.01f);
                Assert.Less(Vector3.Distance(p, previous), 0.12f, "40 秒内步进必须平滑");
                Assert.That(p.y, Is.InRange(GullFlightRules.MinCruiseAltitude, GullFlightRules.MaxCruiseAltitude));
                previous = p;
            }
        }

        [Test]
        public void GullPlans_NeverEnterNoFlyZone()
        {
            AmbientArena arena = Level1Arena();
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(arena);
            GullPlan[] plans = GullFlightRules.BuildPlans(arena, new AmbientRandom(20260913),
                AmbientBudget.MaxGulls);

            Assert.AreEqual(AmbientBudget.MaxGulls, plans.Length);

            for (int i = 0; i < plans.Length; i++)
            {
                for (int step = 0; step <= 8000; step++)
                {
                    Vector3 p = plans[i].Orbit.Evaluate(step * 0.05f);
                    Assert.IsFalse(zone.Contains(p),
                        "海鸥 " + i + " 在第 " + step + " 帧（t=" + (step * 0.05f) + "）闯入禁飞区：" + p);
                }
            }
        }

        [Test]
        public void GullPlans_StayInAltitudeBandAndAboveWater()
        {
            AmbientArena arena = Level1Arena();
            GullPlan[] plans = GullFlightRules.BuildPlans(arena, new AmbientRandom(11),
                AmbientBudget.MaxGulls);

            for (int i = 0; i < plans.Length; i++)
            {
                for (int step = 0; step <= 4000; step++)
                {
                    Vector3 p = plans[i].Orbit.Evaluate(step * 0.05f);
                    Assert.That(p.y, Is.InRange(GullFlightRules.MinCruiseAltitude, GullFlightRules.MaxCruiseAltitude));
                    Assert.Greater(p.y, arena.WaterY + 5f, "海鸥不应贴水飞行");
                }
            }
        }

        [Test]
        public void GullPlans_DiveTargetsAndDivePaths_AreOutsideNoFlyZone()
        {
            AmbientArena arena = Level1Arena();
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(arena);
            GullPlan[] plans = GullFlightRules.BuildPlans(arena, new AmbientRandom(5),
                AmbientBudget.MaxGulls);

            for (int i = 0; i < plans.Length; i++)
            {
                Assert.IsFalse(zone.Contains(plans[i].DiveTarget),
                    "海鸥 " + i + " 的俯冲入水点在禁飞区内");

                if (!plans[i].Dives)
                    continue;

                Vector3 from = plans[i].Orbit.Evaluate(0f);
                for (float t = 0f; t <= 2f; t += 0.02f)
                {
                    Vector3 p = GullFlightRules.DivePoint(from, plans[i].DiveTarget, t);
                    Assert.IsFalse(zone.Contains(p),
                        "海鸥 " + i + " 俯冲路径在 t=" + t + " 闯入禁飞区：" + p);
                }
            }
        }

        [Test]
        public void GullPlans_AtLeastOneHighFlyerDoesNotDive()
        {
            AmbientArena arena = Level1Arena();
            GullPlan[] plans = GullFlightRules.BuildPlans(arena, new AmbientRandom(3),
                AmbientBudget.MaxGulls);

            // 4 只里恰有 1 只（高空飞越那只）不俯冲。
            int divers = 0;
            for (int i = 0; i < plans.Length; i++)
            {
                if (plans[i].Dives)
                    divers++;
            }

            Assert.AreEqual(plans.Length - 1, divers, "飞越竞技场上方的那只必须禁止俯冲");
        }

        [Test]
        public void WingAngle_IsAsymmetricAndBounded()
        {
            const float amplitude = 34f;

            for (float phase = 0f; phase < 6.283f; phase += 0.05f)
            {
                float angle = GullFlightRules.WingAngleDegrees(phase, amplitude);
                Assert.That(angle, Is.InRange(-amplitude, amplitude));
            }

            Assert.AreEqual(amplitude, GullFlightRules.WingAngleDegrees(Mathf.PI * 0.5f, amplitude), 1e-4f);
            // 下拍被压缩到 55%，读作"下拍有阻力"。
            Assert.AreEqual(-amplitude * 0.55f,
                GullFlightRules.WingAngleDegrees(Mathf.PI * 1.5f, amplitude), 1e-3f);
        }

        [Test]
        public void Panic_DecaysToZeroAndProducesUpwardAwayOffset()
        {
            float panic = 1f;
            for (int i = 0; i < 500; i++)
                panic = GullFlightRules.DecayPanic(panic, 0.016f);

            Assert.AreEqual(0f, panic, 1e-6f, "惊飞必须在 PanicDuration 内归零");

            Vector3 gull = new Vector3(10f, 8f, 5f);
            Vector3 blast = new Vector3(8f, 0f, 5f);
            Vector3 offset = GullFlightRules.PanicOffset(gull, blast, 1f);

            Assert.Greater(offset.y, 0f, "惊飞要向上抬");
            Assert.Greater(offset.x, 0f, "惊飞要背离爆心（爆心在左侧 → 向右逃）");

            Assert.AreEqual(Vector3.zero, GullFlightRules.PanicOffset(gull, blast, 0f));
        }

        [Test]
        public void Heading_FollowsXZVelocity()
        {
            Assert.AreEqual(0f, GullFlightRules.HeadingDegrees(Vector3.forward), 1e-3f);
            Assert.AreEqual(90f, GullFlightRules.HeadingDegrees(Vector3.right), 1e-3f);
            Assert.AreEqual(180f, Mathf.Abs(GullFlightRules.HeadingDegrees(Vector3.back)), 1e-3f);
            Assert.AreEqual(42f, GullFlightRules.HeadingDegrees(Vector3.zero, 42f), 1e-3f, "零速度回落");
        }

        // ------------------------------------------------------------------
        // 螃蟹
        // ------------------------------------------------------------------

        [Test]
        public void Crab_Next_HasHysteresis()
        {
            // 触发 2.2、解除 3.2：中间区间保持原状，避免"探头-缩回"抖动。
            Assert.AreEqual(CrabState.Patrol, CrabBehaviorRules.Next(CrabState.Patrol, 5f));
            Assert.AreEqual(CrabState.Buried, CrabBehaviorRules.Next(CrabState.Patrol, 2.0f));

            Assert.AreEqual(CrabState.Buried, CrabBehaviorRules.Next(CrabState.Buried, 2.8f),
                "在滞回区间内保持埋没");
            Assert.AreEqual(CrabState.Buried, CrabBehaviorRules.Next(CrabState.Buried, 3.1f));
            Assert.AreEqual(CrabState.Patrol, CrabBehaviorRules.Next(CrabState.Buried, 3.5f));
        }

        [Test]
        public void Crab_PingPong_StaysInUnitRangeAndIsContinuous()
        {
            float previous = CrabBehaviorRules.PingPong(0f, 4f);

            for (int i = 1; i <= 4000; i++)
            {
                float v = CrabBehaviorRules.PingPong(i * 0.01f, 4f);
                Assert.That(v, Is.InRange(0f, 1f));
                Assert.Less(Mathf.Abs(v - previous), 0.01f, "除折返点外必须连续");
                previous = v;
            }

            Assert.AreEqual(0f, CrabBehaviorRules.PingPong(0f, 4f), 1e-5f);
            Assert.AreEqual(1f, CrabBehaviorRules.PingPong(4f, 4f), 1e-5f);
        }

        [Test]
        public void Crab_PatrolPoint_ClampsToSegment()
        {
            var a = new Vector3(1f, 0f, 2f);
            var b = new Vector3(5f, 0f, 2f);

            Assert.AreEqual(a, CrabBehaviorRules.PatrolPoint(a, b, -1f));
            Assert.AreEqual(b, CrabBehaviorRules.PatrolPoint(a, b, 2f));
            Assert.AreEqual(new Vector3(3f, 0f, 2f), CrabBehaviorRules.PatrolPoint(a, b, 0.5f));
        }

        [Test]
        public void Shore_TideSlope_MatchesSceneBakeContract()
        {
            Assert.AreEqual(0f, AmbientShore.TideSlopeY(0f), 1e-5f, "边界处 = 地面顶面");
            Assert.AreEqual(-0.6f, AmbientShore.TideSlopeY(AmbientShore.TideSlopeWidth), 1e-5f,
                "坡外缘 = -0.6（场景烘焙的潮间带底高）");
            Assert.AreEqual(-0.6f, AmbientShore.TideSlopeY(99f), 1e-5f, "超出坡宽后夹住");
            Assert.AreEqual(-0.192f, AmbientShore.TideSlopeY(AmbientShore.CrabShoreOffset), 0.01f);
        }

        [Test]
        public void Shore_CrabBodyY_IsBetweenSeabedAndWaterSurface()
        {
            float y = AmbientShore.CrabBodyY(-0.2f);

            // 浅海床顶面 = 水面 - 0.4 = -0.6；螃蟹不能被算到海床以下。
            Assert.Greater(y, -0.6f);
            Assert.Less(y, 0.2f, "螃蟹仍在水线附近");
        }

        [Test]
        public void BuriedVisibility_IsOneMinusProgress()
        {
            Assert.AreEqual(1f, CrabBehaviorRules.BuriedVisibility(0f), 1e-5f);
            Assert.AreEqual(0f, CrabBehaviorRules.BuriedVisibility(1f), 1e-5f);
            Assert.AreEqual(0.5f, CrabBehaviorRules.BuriedVisibility(0.5f), 1e-5f);
        }

        // ------------------------------------------------------------------
        // 鱼群 boids
        // ------------------------------------------------------------------

        static void StepSchool(Vector3[] positions, Vector3[] velocities, BoidsSettings settings,
            Vector3 anchor, int frames, float dt)
        {
            for (int i = 0; i < frames; i++)
                BoidsRules.Step(positions, velocities, positions.Length, settings, anchor, dt);
        }

        [Test]
        public void Boids_DoNotCollapse_AndStayInWaterVolume()
        {
            AmbientArena arena = Level1Arena();
            BoidsSettings settings = BoidsSettings.Default(arena);
            int count = AmbientBudget.FishPerSchool;
            var positions = new Vector3[count];
            var velocities = new Vector3[count];

            var rng = new AmbientRandom(20260913);
            BoidsRules.Seed(rng, positions, velocities, count, settings, settings.Center);
            StepSchool(positions, velocities, settings, settings.Center, 900, 1f / 60f);

            float minDistance = BoidsRules.MinPairwiseDistance(positions, count);
            Assert.Greater(minDistance, settings.SeparationRadius * 0.4f,
                "鱼群塌缩成一点说明分离力失效（最小间距 " + minDistance + "）");

            for (int i = 0; i < count; i++)
            {
                Vector3 p = positions[i];
                Assert.That(p.x, Is.InRange(settings.Center.x - settings.Extents.x - 1e-3f,
                    settings.Center.x + settings.Extents.x + 1e-3f));
                Assert.That(p.y, Is.InRange(settings.Center.y - settings.Extents.y - 1e-3f,
                    settings.Center.y + settings.Extents.y + 1e-3f));
                Assert.That(p.z, Is.InRange(settings.Center.z - settings.Extents.z - 1e-3f,
                    settings.Center.z + settings.Extents.z + 1e-3f));

                // 仅在水面以下可见：上界必须低于水面。
                Assert.Less(p.y, arena.WaterY - 0.05f, "鱼必须在水面以下（任务书硬要求）");
                Assert.Greater(p.y, arena.WaterY - 0.4f, "鱼不能穿海床（浅台顶面 = 水面 − 0.4）");
            }
        }

        [Test]
        public void Boids_SpeedsStayBounded()
        {
            AmbientArena arena = Level1Arena();
            BoidsSettings settings = BoidsSettings.Default(arena);
            int count = 12;
            var positions = new Vector3[count];
            var velocities = new Vector3[count];

            BoidsRules.Seed(new AmbientRandom(99), positions, velocities, count, settings, settings.Center);
            StepSchool(positions, velocities, settings, settings.Center, 600, 1f / 60f);

            for (int i = 0; i < count; i++)
            {
                float speed = velocities[i].magnitude;
                Assert.That(speed, Is.InRange(settings.MinSpeed * 0.5f, settings.MaxSpeed * 1.05f),
                    "速度必须被夹在 [MinSpeed, MaxSpeed] 一带（第 " + i + " 条 = " + speed + "）");
            }
        }

        [Test]
        public void Boids_SchoolStaysNearAnchor()
        {
            AmbientArena arena = Level1Arena();
            BoidsSettings settings = BoidsSettings.Default(arena);
            int count = AmbientBudget.FishPerSchool;
            var positions = new Vector3[count];
            var velocities = new Vector3[count];

            var rng = new AmbientRandom(4242);
            BoidsRules.Seed(rng, positions, velocities, count, settings, settings.Center);
            StepSchool(positions, velocities, settings, settings.Center, 900, 1f / 60f);

            Vector3 centroid = BoidsRules.Centroid(positions, count);
            float drift = Vector2.Distance(new Vector2(centroid.x, centroid.z),
                new Vector2(settings.Center.x, settings.Center.z));

            Assert.Less(drift, settings.Extents.x + settings.Extents.z,
                "整群不应游出锚点附近（漂移 " + drift + "）");
        }

        [Test]
        public void Boids_IsDeterministicForSameSeed()
        {
            AmbientArena arena = Level1Arena();
            BoidsSettings settings = BoidsSettings.Default(arena);
            int count = 10;
            var p1 = new Vector3[count];
            var v1 = new Vector3[count];
            var p2 = new Vector3[count];
            var v2 = new Vector3[count];

            BoidsRules.Seed(new AmbientRandom(7), p1, v1, count, settings, settings.Center);
            BoidsRules.Seed(new AmbientRandom(7), p2, v2, count, settings, settings.Center);

            StepSchool(p1, v1, settings, settings.Center, 120, 1f / 60f);
            StepSchool(p2, v2, settings, settings.Center, 120, 1f / 60f);

            for (int i = 0; i < count; i++)
                Assert.AreEqual(p1[i], p2[i], "同种子 + 同步数 → 同一鱼群形态");
        }

        [Test]
        public void Boids_ClampToVolume_IsHardBound()
        {
            AmbientArena arena = Level1Arena();
            BoidsSettings settings = BoidsSettings.Default(arena);

            Vector3 wild = new Vector3(999f, 999f, -999f);
            Vector3 clamped = BoidsRules.ClampToVolume(wild, settings);

            Assert.AreEqual(settings.Center.x + settings.Extents.x, clamped.x, 1e-4f);
            Assert.AreEqual(settings.Center.y + settings.Extents.y, clamped.y, 1e-4f);
            Assert.AreEqual(settings.Center.z - settings.Extents.z, clamped.z, 1e-4f);
        }

        [Test]
        public void BoidsSettings_Default_PutsWaterBoxBelowSurfaceAndAboveSeabed()
        {
            AmbientArena arena = Level1Arena();
            BoidsSettings settings = BoidsSettings.Default(arena);

            float top = settings.Center.y + settings.Extents.y;
            float bottom = settings.Center.y - settings.Extents.y;

            Assert.Less(top, arena.WaterY - 0.05f, "盒子上界必须在水面以下");
            Assert.Greater(bottom, arena.WaterY - 0.4f, "盒子下界必须在浅海床之上");
            Assert.Greater(settings.Center.z, arena.Depth, "鱼群放在竞技场外的近侧水域（相机前景）");
        }
    }
}
