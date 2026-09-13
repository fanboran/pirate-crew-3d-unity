using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient.Tests
{
    /// <summary>
    /// 环境模块"边界与不变量"的纯 C# 用例（无头可跑，不碰 GameObject）。
    ///
    /// 【为什么先钉这几条】禁飞区、竞技场边界与风相位是本模块最容易"看起来没问题、
    /// 实际偶发穿帮"的三处：鸟偶尔飞过瞄准带、浮标飘上岸、植被根部被吹离地面。
    /// 这里把每条阈值与不变量单独断言，回归时能立刻定位。
    /// </summary>
    [TestFixture]
    public class AmbientRulesTests
    {
        static AmbientArena Level1Arena()
        {
            // 与 LevelCatalog._level1 一致：50 × 17 瓦片。
            return AmbientArena.FromTiles(50f, 17f);
        }

        // ------------------------------------------------------------------
        // 竞技场边界
        // ------------------------------------------------------------------

        [Test]
        public void Arena_FromTiles_UsesProjectHeightContract()
        {
            AmbientArena arena = Level1Arena();

            Assert.AreEqual(50f, arena.Width, 1e-5f);
            Assert.AreEqual(17f, arena.Depth, 1e-5f);
            Assert.AreEqual(-0.2f, arena.WaterY, 1e-5f, "水面 y 来自 LevelGeometry.WaterSurfaceY");
            Assert.AreEqual(0f, arena.GroundY, 1e-5f, "地面顶面 y 来自 LevelGeometry.GroundTopY");
            Assert.AreEqual(25f, arena.CenterX, 1e-5f);
            Assert.AreEqual(8.5f, arena.CenterZ, 1e-5f);
        }

        [Test]
        public void Arena_ContainsXZ_HandlesMarginSign()
        {
            AmbientArena arena = Level1Arena();

            Assert.IsTrue(arena.ContainsXZ(0f, 0f, 0f));
            Assert.IsTrue(arena.ContainsXZ(50f, 17f, 0f));
            Assert.IsFalse(arena.ContainsXZ(-0.1f, 5f, 0f));
            Assert.IsTrue(arena.ContainsXZ(-0.1f, 5f, 0.5f), "外扩 0.5 后算场内带");
            Assert.IsFalse(arena.ContainsXZ(-0.6f, 5f, 0.5f));
        }

        [Test]
        public void Arena_PushOutsideXZ_MovesInsidePointOut_AndKeepsOutsidePoint()
        {
            AmbientArena arena = Level1Arena();

            Vector3 outside = new Vector3(51.5f, -0.3f, 4f);
            Assert.AreEqual(outside, arena.PushOutsideXZ(outside, 0.5f), "已在外面的点不动");

            Vector3 inside = new Vector3(25f, -0.4f, 8f);
            Vector3 pushed = arena.PushOutsideXZ(inside, 0.5f);
            Assert.IsFalse(arena.ContainsXZ(pushed.x, pushed.z, 0.5f), "推出去后必须不在矩形（含边距）内");
            Assert.AreEqual(inside.y, pushed.y, 1e-5f, "只动 XZ，高度不变");
        }

        // ------------------------------------------------------------------
        // 禁飞区（可读性红线）
        // ------------------------------------------------------------------

        [Test]
        public void NoFlyZone_ForArena_HasExpectedBounds()
        {
            AmbientArena arena = Level1Arena();
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(arena);

            Assert.AreEqual(19f, zone.MinX, 1e-5f);
            Assert.AreEqual(31f, zone.MaxX, 1e-5f);
            Assert.AreEqual(-1f, zone.MinZ, 1e-5f);
            Assert.AreEqual(18f, zone.MaxZ, 1e-5f);
            Assert.AreEqual(9f, zone.CeilingY, 1e-5f);
        }

        [Test]
        public void NoFlyZone_Contains_IsLowAltitudeCentralColumnOnly()
        {
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(Level1Arena());

            Assert.IsTrue(zone.Contains(25f, 0f, 5f), "中央 · 低空 → 禁飞");
            Assert.IsTrue(zone.Contains(25f, 9f, 5f), "天花板边界包含");
            Assert.IsFalse(zone.Contains(25f, 9.01f, 5f), "天花板之上 → 放行");
            Assert.IsFalse(zone.Contains(5f, 0f, 5f), "西侧（走廊外）→ 放行");
            Assert.IsFalse(zone.Contains(45f, 0f, 5f), "东侧（走廊外）→ 放行");
            Assert.IsFalse(zone.Contains(25f, 0f, -5f), "纵深在禁区之外 → 放行");
            Assert.IsFalse(zone.Contains(25f, 0f, 20f), "近侧在禁区之外 → 放行");
        }

        [Test]
        public void NoFlyZone_ClampOut_AlwaysProducesLegalPoint()
        {
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(Level1Arena());

            for (float x = -10f; x <= 60f; x += 1.7f)
            {
                for (float y = -2f; y <= 14f; y += 1.3f)
                {
                    for (float z = -12f; z <= 30f; z += 2.1f)
                    {
                        var p = new Vector3(x, y, z);
                        Vector3 clamped = zone.ClampOut(p);
                        Assert.IsFalse(zone.Contains(clamped),
                            "钳制结果必须合法，输入 " + p + " 输出 " + clamped);
                    }
                }
            }
        }

        [Test]
        public void NoFlyZone_ClampOut_LeavesLegalPointUntouched()
        {
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(Level1Arena());
            var legal = new Vector3(-3f, 4f, 12f);

            Assert.AreEqual(legal, zone.ClampOut(legal));
        }

        [Test]
        public void NoFlyZone_ClampOut_PicksCheapestEscape()
        {
            AmbientNoFlyZone zone = AmbientNoFlyZone.ForArena(Level1Arena());

            // 贴近右边界 → 往右推最省（水平推出，不抬高）。
            Vector3 nearRight = new Vector3(zone.MaxX - 0.2f, 3f, 5f);
            Vector3 pushedRight = zone.ClampOut(nearRight);
            Assert.Greater(pushedRight.x, zone.MaxX);
            Assert.AreEqual(3f, pushedRight.y, 1e-4f, "贴边时应水平推出而不是抬到天上");

            // 已经贴近天花板 → 向上抬最省。
            Vector3 nearCeiling = new Vector3(zone.MinX + 2f, 8.9f, 5f);
            Vector3 pushedUp = zone.ClampOut(nearCeiling);
            Assert.Greater(pushedUp.y, zone.CeilingY);
            Assert.AreEqual(nearCeiling.x, pushedUp.x, 1e-4f, "抬升时不动水平位置");
        }

        // ------------------------------------------------------------------
        // 风
        // ------------------------------------------------------------------

        [Test]
        public void Wind_PhaseIsContinuous_UnderTinySteps()
        {
            float previous = WindRules.Phase(0f, WindRules.BaseSpeed, 0f);

            for (int i = 1; i <= 4000; i++)
            {
                float t = i * 0.001f;
                float phase = WindRules.Phase(t, WindRules.BaseSpeed, 0f);
                float delta = phase - previous;
                Assert.Less(Mathf.Abs(delta), 0.002f, "相邻 1ms 的相位增量必须趋近 0（无跳变）");
                previous = phase;
            }
        }

        [Test]
        public void Wind_SpatialPhaseOffsetsDifferButAreDeterministic()
        {
            float a = WindRules.SpatialPhase(3f, 4f, 0.35f);
            float b = WindRules.SpatialPhase(3f, 4f, 0.35f);
            float c = WindRules.SpatialPhase(9f, 4f, 0.35f);

            Assert.AreEqual(a, b, 0f, "同坐标必须同相位");
            Assert.AreNotEqual(a, c, "不同坐标应错相（否则整片植被同步抽搐）");
        }

        [Test]
        public void Wind_GustEnvelope_IsBoundedContinuousAndPositive()
        {
            float previous = WindRules.GustEnvelope(0f);

            for (int i = 1; i <= 2000; i++)
            {
                float phase = i * 0.01f;
                float gust = WindRules.GustEnvelope(phase);
                Assert.That(gust, Is.InRange(WindRules.GustMin - 1e-4f, WindRules.GustMax + 1e-4f),
                    "阵风包络必须落在 [GustMin, GustMax] 且恒正（不做反向吹）");
                Assert.Less(Mathf.Abs(gust - previous), 0.02f, "包络必须连续");
                previous = gust;
            }
        }

        [Test]
        public void Wind_SwayWeight_AnchoredAtRootAndMonotonicUpward()
        {
            // 树/草：根 y=0、权重高度 2 → 0 处权重 0、2 以上权重 1。
            Assert.AreEqual(0f, WindRules.SwayWeight(0f, 0f, 2f, 1f), 1e-5f);
            Assert.AreEqual(1f, WindRules.SwayWeight(2f, 0f, 2f, 1f), 1e-5f);
            Assert.AreEqual(1f, WindRules.SwayWeight(6f, 0f, 2f, 1f), 1e-5f);

            float previous = -1f;
            for (float y = 0f; y <= 3f; y += 0.1f)
            {
                float w = WindRules.SwayWeight(y, 0f, 2f, 1f);
                Assert.GreaterOrEqual(w, previous - 1e-6f, "向上权重必须单调不减");
                previous = w;
            }
        }

        [Test]
        public void Wind_SwayWeight_FlagHangsDownward()
        {
            // 旗/帆：悬挂点 y=3、权重高度 1 → 悬挂点权重 0、向下 1 处权重 1。
            Assert.AreEqual(0f, WindRules.SwayWeight(3f, 3f, 1f, -1f), 1e-5f);
            Assert.AreEqual(1f, WindRules.SwayWeight(2f, 3f, 1f, -1f), 1e-5f);
            Assert.Greater(WindRules.SwayWeight(2.5f, 3f, 1f, -1f),
                WindRules.SwayWeight(2.9f, 3f, 1f, -1f), "越往下权重越大");
        }

        [Test]
        public void Wind_SwayWeightWithFloor_NeverGoesBelowFloor()
        {
            float floor = WindRules.DefaultSwayFloor;

            Assert.AreEqual(floor, WindRules.SwayWeightWithFloor(0f, 0f, 1.6f, 1f, floor), 1e-5f,
                "合并网格里矮草丛也必须有摆幅（否则一片静止）");
            Assert.AreEqual(1f, WindRules.SwayWeightWithFloor(5f, 0f, 1.6f, 1f, floor), 1e-5f);
        }

        [Test]
        public void Wind_Displacement_IsBounded()
        {
            const float strength = 0.09f;
            const float flutter = 0.3f;
            float bound = WindRules.MaxDisplacement(strength, flutter);

            for (float x = -5f; x <= 55f; x += 3.3f)
            {
                for (float y = 0f; y <= 6f; y += 0.7f)
                {
                    for (float t = 0f; t <= 40f; t += 1.1f)
                    {
                        Vector3 d = WindRules.Displacement(
                            new Vector3(x, y, 3f), anchorY: 0f, weightScale: 1.6f,
                            weightDirection: 1f, swayFloor: WindRules.DefaultSwayFloor,
                            strength: strength, flutter: flutter,
                            angularSpeed: WindRules.BaseSpeed, density: 0.35f,
                            time: t, windDirectionXZ: new Vector2(1f, 0f));

                        Assert.LessOrEqual(new Vector2(d.x, d.z).magnitude, bound + 1e-4f,
                            "位移必须有界（防止改参数把植被吹飞）");
                    }
                }
            }
        }

        [Test]
        public void Wind_Displacement_ZeroStrengthMeansNoMotion()
        {
            Vector3 d = WindRules.Displacement(Vector3.one, 0f, 1f, 1f, 0f, 0f, 0.5f,
                WindRules.BaseSpeed, 0.35f, 12.3f, Vector2.right);

            Assert.AreEqual(0f, d.magnitude, 1e-6f);
        }

        // ------------------------------------------------------------------
        // 确定性随机与预算
        // ------------------------------------------------------------------

        [Test]
        public void AmbientRandom_IsDeterministicAcrossInstances()
        {
            var a = new AmbientRandom(20260913);
            var b = new AmbientRandom(20260913);

            for (int i = 0; i < 128; i++)
            {
                Assert.AreEqual(a.Next01(), b.Next01(), 0f, "同种子必须给出同一序列");
            }

            var c = new AmbientRandom(7);
            Assert.AreNotEqual(a.Next01(), c.Next01());
        }

        [Test]
        public void Budget_DefaultsAreInsideMinMax()
        {
            Assert.That(AmbientBudget.DefaultGulls,
                Is.InRange(AmbientBudget.MinGulls, AmbientBudget.MaxGulls));
            Assert.That(AmbientBudget.DefaultCrabs,
                Is.InRange(AmbientBudget.MinCrabs, AmbientBudget.MaxCrabs));
            Assert.That(AmbientBudget.FishPerSchool, Is.InRange(2, AmbientBudget.MaxFishPerSchool));
            Assert.Greater(AmbientBudget.MaxAmbientTriangles, 0);
            Assert.Greater(AmbientBudget.MaxAmbientDrawCalls, 0);
        }
    }
}
