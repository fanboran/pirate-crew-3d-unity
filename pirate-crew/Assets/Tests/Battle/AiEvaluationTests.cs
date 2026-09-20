using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="AiEvaluation"/> / <see cref="AiEvaluationSession"/> 的纯 C# 测试。
    ///
    /// 【纪律】本文件不 <c>new GameObject()</c>、不继承 MonoBehaviour、不用 <c>[UnityTest]</c>，
    ///         因此可在无头验证台（dotnet）运行；所有随机性通过固定 seed 的
    ///         <see cref="AiRandom"/> 注入，断言完全确定。
    ///
    /// 【3D 坐标模型】AI 的打分仍在 Flash 平面像素域（x = 世界 X、y = 世界 Z 纵深），
    /// 但轨迹模拟走 3D 世界域：初速 <see cref="LevelGeometry.FlashLaunchVelocityToWorld"/>（含抬升）、
    /// 积分 <see cref="ThrowTrajectory.Predict"/>、落点 = 与水平面 <c>y=GroundTopY</c> 的首次相交。
    /// 因此本文件里的 "Ground" 语义已从「屏幕地面像素 y」改为「典型纵深像素坐标」。
    ///
    /// 【覆盖】§6.2 自抛、§6.3 通用/特殊武器、§6.1 汇总与 bailout、§6.4 随机性、
    ///         §4.1 evilness/luck、§5.2 表末 7 种特殊武器、3D 落点内核不变量、以及题目要求的全部边界。
    /// </summary>
    [TestFixture]
    public class AiEvaluationTests
    {
        // ==================================================================
        // 测试脚手架
        // ==================================================================

        /// <summary>典型纵深像素坐标（原 2D 的「地面像素 y」在平面重投影后 = 世界 Z 像素）。</summary>
        const float Lane = 400f;

        /// <summary>tidalWave「近水带」判据使用的平面像素水面标量（阈值 = Water − 300 = 300）。</summary>
        const float Water = 600f;

        static AiTerrain Terrain(float minX = 0f, float maxX = 2000f, float minY = 0f, float maxY = 2000f)
            => new AiTerrain(minX, maxX, minY, maxY);

        static AiBattlefield Field(
            IReadOnlyList<AiUnit> units, int actorId, bool canThrow, bool canShoot,
            float water = Water, AiTerrain terrain = null, params WeaponId[] weapons)
        {
            var slots = new List<AiWeaponSlot>();
            for (int i = 0; i < weapons.Length; i++)
                slots.Add(new AiWeaponSlot(i, weapons[i]));

            return new AiBattlefield(
                units, actorId, canThrow, canShoot, slots,
                terrain ?? Terrain(), water);
        }

        static AiEvaluationSession RunToEnd(AiBattlefield field, int seed, AiEvaluationOptions options = null)
        {
            var session = AiEvaluation.CreateSession(field, new AiRandom(seed), options);
            while (!session.IsFinished)
            {
                if (!session.StepOnce())
                    break;
            }
            return session;
        }

        static bool HasCandidate(AiEvaluationSession session, int slot, WeaponId id)
        {
            for (int i = 0; i < session.Candidates.Count; i++)
            {
                AiMoveCandidate c = session.Candidates[i];
                if (c.WeaponSlotIndex == slot && c.WeaponId == id)
                    return true;
            }
            return false;
        }

        /// <summary>标准双人场景：红队 actor(id1) 对蓝队 enemy(id2)，纵深同为 <see cref="Lane"/>。</summary>
        static AiBattlefield Duel(WeaponId[] weapons, int actorLuck = 5, int enemyEvilness = 0)
        {
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, Lane, luck: actorLuck),
                AiUnit.Simple(2, 1, 300f, Lane, evilness: enemyEvilness),
            };
            return Field(units, 1, canThrow: true, canShoot: true, weapons: weapons);
        }

        /// <summary>
        /// 测试侧的独立参照实现：用 <see cref="ThrowTrajectory.Predict"/> 逐步走，
        /// 找最先与水平面 y=GroundTopY 相交且落在矩形内的点 —— 与 <see cref="AiEvaluation.SimulateShot"/> 同口径。
        /// 用于交叉验证被测内核确实走「同一套 3D 运动学 + 同一落点判据」。
        /// </summary>
        static (float ex, float ey, bool drowned) ReferenceLanding(
            float startX, float startY, float vx, float vy, float weight, AiTerrain terrain, int maxSteps)
        {
            Vector3 origin = LevelGeometry.PixelToArena(startX, startY);
            origin.y = LevelGeometry.GroundTopY + LevelGeometry.UnitPivotHeight;
            Vector3 v = LevelGeometry.FlashLaunchVelocityToWorld(vx, vy);
            float g = LevelGeometry.WorldGravityY(weight);

            var points = new Vector3[maxSteps];
            ThrowTrajectory.Predict(origin, v, g, points, maxSteps, LevelGeometry.FrameSeconds);

            Vector3 end = origin;
            bool drowned = false;
            for (int i = 0; i < maxSteps; i++)
            {
                end = points[i];
                if (end.y <= LevelGeometry.GroundTopY)
                {
                    Vector2 planar = LevelGeometry.ArenaToPixel(end);
                    if (terrain == null || terrain.IsInside(planar.x, planar.y))
                        break;
                }

                if (end.y <= LevelGeometry.WaterSurfaceY)
                {
                    drowned = true;
                    break;
                }
            }

            Vector2 landing = LevelGeometry.ArenaToPixel(end);
            return (landing.x, landing.y, drowned);
        }

        // ==================================================================
        // 3D 落点内核不变量（与实弹/预览同源）
        // ==================================================================

        [Test]
        public void SimulateShot_MatchesThrowTrajectory_And_LandsOnGroundPlane()
        {
            // 被测内核必须与「ThrowTrajectory 逐步 + y=GroundTopY 首次相交」逐值一致——
            // 这是 AI 预测与 3D 实弹不分叉的关键不变量。
            var terrain = Terrain();
            AiThrowSample actual = AiEvaluation.SimulateShot(
                startX: 100f, startY: Lane, vx: 6f, vy: 3f,
                weight: CrewCatalog.Weight, terrain: terrain);

            var expected = ReferenceLanding(
                100f, Lane, 6f, 3f, CrewCatalog.Weight, terrain, AiEvaluation.MaxSimulationSteps);

            Assert.AreEqual(expected.ex, actual.Ex, 1e-3f, "落点 X（平面像素）必须与参照实现一致");
            Assert.AreEqual(expected.ey, actual.Ey, 1e-3f, "落点纵深（平面像素）必须与参照实现一致");
            Assert.IsFalse(actual.Drowned);

            // 落点反投影回世界 = 地面平面上的一点（y = GroundTopY）。
            Vector3 world = LevelGeometry.PixelToArena(actual.Ex, actual.Ey);
            Assert.AreEqual(LevelGeometry.GroundTopY, world.y, 1e-5f, "落点必须在地面水平面上");
            Assert.IsTrue(terrain.IsInside(actual.Ex, actual.Ey), "落点应在竞技场矩形内");
        }

        [Test]
        public void SimulateShot_PreservesFlashPlanarLaunchVelocity()
        {
            // Vx/Vy 是直接交回 ApplyLaunchVelocity / 弹体生成器的 Flash 平面初速，必须原样保留
            // （3D 抬升由 LevelGeometry.FlashLaunchVelocityToWorld 在执行端施加，不在 AI 里改速度）。
            AiThrowSample s = AiEvaluation.SimulateShot(
                100f, Lane, 7.5f, -2.25f, CrewCatalog.Weight, Terrain());

            Assert.AreEqual(7.5f, s.Vx, 1e-6f);
            Assert.AreEqual(-2.25f, s.Vy, 1e-6f);
        }

        [Test]
        public void SimulateShot_OutsideArena_Drowns()
        {
            // 竞技场矩形缩到原点附近，投掷者在其外 → 地面平面之外无地可落，继续下落到水面以下。
            var tiny = new AiTerrain(0f, 1f, 0f, 1f);
            AiThrowSample s = AiEvaluation.SimulateShot(100f, Lane, 5f, 5f, CrewCatalog.Weight, tiny);

            Assert.IsTrue(s.Drowned, "掉出地面矩形后必须判定落水");
        }

        [Test]
        public void RandomThrow_ForceIsWithinTwangMax()
        {
            // angle = 180 + rand*180、force = 5 + rand*(twangMax−5) → 平面速度模长 ∈ [5, twangMax]。
            for (int seed = 0; seed < 20; seed++)
            {
                AiThrowSample s = AiEvaluation.RandomThrow(
                    100f, Lane, CrewCatalog.TwangMaxForce, CrewCatalog.Weight,
                    Terrain(), new AiRandom(seed));

                float speed = Mathf.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
                Assert.GreaterOrEqual(speed, AiEvaluation.ThrowForceMin - 1e-4f);
                Assert.LessOrEqual(speed, CrewCatalog.TwangMaxForce + 1e-4f);
            }
        }

        // ==================================================================
        // 确定性 / 随机性（§6.4）
        // ==================================================================

        [Test]
        public void Evaluate_SameSeed_IsFullyDeterministic()
        {
            // 同一 seed 两次评估：所有可观测输出必须逐位一致（AI 的随机性必须可复现）。
            AiBattlefield field = Duel(new[] { WeaponId.CherryBomb, WeaponId.Dynamite });

            AiDecision a = AiEvaluation.Evaluate(field, new AiRandom(12345));
            AiDecision b = AiEvaluation.Evaluate(field, new AiRandom(12345));

            Assert.AreEqual(a.Kind, b.Kind);
            Assert.AreEqual(a.ActorUnitId, b.ActorUnitId);
            Assert.AreEqual(a.WeaponSlotIndex, b.WeaponSlotIndex);
            Assert.AreEqual(a.WeaponId, b.WeaponId);
            Assert.AreEqual(a.TargetUnitId, b.TargetUnitId);
            Assert.AreEqual(a.Vx, b.Vx, "同 seed 的发射速度必须一致");
            Assert.AreEqual(a.Vy, b.Vy);
            Assert.AreEqual(a.Success, b.Success);
            Assert.AreEqual(a.FlashSuccess, b.FlashSuccess);
            Assert.AreEqual(a.ShouldBailOut, b.ShouldBailOut);
            Assert.AreEqual(a.EvaluationCount, b.EvaluationCount);
        }

        [Test]
        public void Evaluate_DifferentSeeds_ProduceDifferentDecisions()
        {
            // 随机性确实生效：跨多个 seed 必须出现多于 1 种决策签名（不是常量返回）。
            AiBattlefield field = Duel(new[] { WeaponId.CherryBomb, WeaponId.Dynamite });

            var signatures = new HashSet<string>();
            for (int seed = 1; seed <= 12; seed++)
            {
                AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(seed));
                signatures.Add($"{d.Kind}|{d.WeaponSlotIndex}|{d.Success:F6}|{d.Vx:F4}");
            }

            Assert.Greater(signatures.Count, 1, "不同 seed 应产生可观测的不同决策（随机性生效）");
        }

        [Test]
        public void Session_StepOnce_And_Step_ReachSameResultAsEvaluate()
        {
            // 分帧（StepOnce 单步 / Step 批量）与一次跑完（Evaluate）必须同结果——
            // 否则 30ms 时间片驱动会改变 AI 行为。
            AiBattlefield field = Duel(new[] { WeaponId.Dynamite, WeaponId.PiecesOfEight });

            AiDecision direct = AiEvaluation.Evaluate(field, new AiRandom(777));

            var session = AiEvaluation.CreateSession(field, new AiRandom(777));
            session.Step(int.MaxValue);
            AiDecision stepped = session.BuildDecision(canBailOut: false);

            Assert.AreEqual(direct.Kind, stepped.Kind);
            Assert.AreEqual(direct.WeaponSlotIndex, stepped.WeaponSlotIndex);
            Assert.AreEqual(direct.Success, stepped.Success);
            Assert.AreEqual(direct.EvaluationCount, stepped.EvaluationCount);
        }

        // ==================================================================
        // 伤害优先（题目要求：能命中时优先更高伤害）
        // ==================================================================

        [Test]
        public void ExpectedDamage_HigherMaxDamage_WeaponScoresHigher()
        {
            // 同一落点（敌人中心）下，dynamite(250,70) 归一化伤害 70/100 = 0.7 >
            // cherryBomb(80,40) 的 40/100 = 0.4。伤害公式复用 ExplosionResolver（§5.3，平面 XZ 距离）。
            AiBattlefield field = Duel(new WeaponId[0]);
            float ex = 300f;
            float ey = Lane;

            float cherry = AiEvaluation.ExpectedDamage(WeaponId.CherryBomb, ex, ey, field, 1);
            float dynamite = AiEvaluation.ExpectedDamage(WeaponId.Dynamite, ex, ey, field, 1);

            Assert.AreEqual(0.4f, cherry, 1e-4f);       // 40 / 100
            Assert.AreEqual(0.7f, dynamite, 1e-4f);     // 70 / 100
            Assert.Greater(dynamite, cherry);
        }

        [Test]
        public void PickBest_WhenBothHit_PrefersHigherDamage()
        {
            // 两个候选原始 §6 打分相同（1.0）都命中；排序分含伤害项 → 高伤害者胜。
            var low = new AiMoveCandidate(1, 0, WeaponId.CherryBomb, 0f, 0f, -1, 0f, 0f, 1.0f, 0.4f);
            var high = new AiMoveCandidate(1, 1, WeaponId.Dynamite, 0f, 0f, -1, 0f, 0f, 1.0f, 0.7f);

            AiMoveCandidate? best = AiEvaluation.PickBest(new[] { low, high }, damageScoreWeight: 0.5f);

            Assert.IsTrue(best.HasValue);
            Assert.AreEqual(WeaponId.Dynamite, best.Value.WeaponId);
            // 排序分验算：1.0 + 0.5×0.4 = 1.2 vs 1.0 + 0.5×0.7 = 1.35。
            Assert.AreEqual(1.20f, low.TotalScore(0.5f), 1e-4f);
            Assert.AreEqual(1.35f, high.TotalScore(0.5f), 1e-4f);
        }

        [Test]
        public void PickBest_DamageWeightZero_RestoresPureFlashOrdering()
        {
            // DamageScoreWeight = 0 时恢复纯原版行为：原始 success 并列 → 保留先出现者。
            var first = new AiMoveCandidate(1, 0, WeaponId.CherryBomb, 0f, 0f, -1, 0f, 0f, 1.0f, 0.4f);
            var second = new AiMoveCandidate(1, 1, WeaponId.Dynamite, 0f, 0f, -1, 0f, 0f, 1.0f, 0.7f);

            AiMoveCandidate? best = AiEvaluation.PickBest(new[] { first, second }, damageScoreWeight: 0f);
            Assert.AreEqual(WeaponId.CherryBomb, best.Value.WeaponId, "权重为 0 时纯 §6 打分并列 → 先出现者胜");
        }

        // ==================================================================
        // evilness（§4.1 / §6.2 / §6.3）
        // ==================================================================

        [Test]
        public void ScoreSelfThrowSample_HigherEvilness_EnemyIsMoreAttractive()
        {
            // 场景：actor(100,400)、敌人(500,400)；落点(400,400) 距敌人质心 100px。
            // 【3D 落点质量项】−0.003 × 100 = −0.3（见 AiEvaluation.SelfLandingDistanceWeight）。
            // 质心项：横向 (|400−500| − |100−500|)/−500 = +0.6；纵深项 0。
            // 敌人循环：落点距敌人 100px → d²=10000，在 200² 内、不小于 100²（无贴脸惩罚），
            // 故 k = 0.2×(1−100/200) = 0.1。乘子前的 base = −0.3 + 0.6 + 0.1 = 0.4，
            // 再走 §6.2 的 s *= (1+evilness)：evilness=0 → 0.4；evilness=5 → 2.4（乘子放大收益）。
            // 最后减「离自己太远」的 0.3（sd = 300，d² 不小于 90000）→ 0.1 / 2.1。
            // 注意：3D 落点质量项是「离目标越近越好」的罚项，故本用例刻意取质心项收益能压过
            // 它的几何，以继续验证「高 evilness 更受青睐」这条 Flash 乘子语义。
            float sampleX = 400f;
            float sampleY = 400f;
            var sample = new AiThrowSample(0f, 0f, sampleX, sampleY, false);

            AiBattlefield low = Field(new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 500f, 400f, evilness: 0),
            }, 1, true, true);

            AiBattlefield high = Field(new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 500f, 400f, evilness: 5),
            }, 1, true, true);

            float sLow = AiEvaluation.ScoreSelfThrowSample(sample, low, 1);
            float sHigh = AiEvaluation.ScoreSelfThrowSample(sample, high, 1);

            Assert.AreEqual(0.10f, sLow, 1e-4f);
            Assert.AreEqual(2.10f, sHigh, 1e-4f);
            Assert.Greater(sHigh, sLow);
        }

        [Test]
        public void ScoreSelfThrowSample_DrownedSample_GetsTwoPointPenalty()
        {
            // 3D 落水由样本的 Drowned 事实承载（不再是平面 y 与水位比较）：Drowned=true 额外 −2。
            var field = Field(new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 300f, 400f),
            }, 1, true, true);

            var dry = new AiThrowSample(0f, 0f, 300f, 400f, false);
            var wet = new AiThrowSample(0f, 0f, 300f, 400f, true);

            float dryScore = AiEvaluation.ScoreSelfThrowSample(dry, field, 1);
            float wetScore = AiEvaluation.ScoreSelfThrowSample(wet, field, 1);

            Assert.AreEqual(AiEvaluation.DrownPenalty, dryScore - wetScore, 1e-4f);
        }

        [Test]
        public void PickPriorityTarget_HigherEvilness_BeatsCloserEnemy()
        {
            // 目标选择 = 距离项 × evilness 项：敌人 A 距 50px、evilness 0 → (1.5−50/70)×1 = 0.786；
            // 敌人 B 距 60px、evilness 5 → (1.5−60/70)×6 = 3.857 > A。故更远但更「邪恶」的 B 被优先。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 150f, 400f, evilness: 0),
                AiUnit.Simple(3, 1, 160f, 400f, evilness: 5),
            };
            AiBattlefield field = Field(units, 1, true, true);

            Assert.AreEqual(3, AiEvaluation.PickPriorityTarget(field, 1));

            float a = AiEvaluation.ScoreEnemyAttractiveness(field, 1, 2);
            float b = AiEvaluation.ScoreEnemyAttractiveness(field, 1, 3);
            Assert.AreEqual(0.785714f, a, 1e-4f);
            Assert.AreEqual(3.857143f, b, 1e-4f);
        }

        // ==================================================================
        // luck（§4.1 / §6.3：评估次数随 luck 变化）
        // ==================================================================

        [Test]
        public void EvaluationCount_GrowsWithLuck()
        {
            // §6.3：count = floor(luck × 队伍人数 / 存活人数)。
            // 队伍 2 人全活 → count = luck；再各自加自抛固定 50 次采样。
            // 故 EvaluationCount = 50 + luck（dynamite 无 cherryBomb 复核）。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f, luck: 1),
                AiUnit.Simple(3, 0, 120f, 400f, luck: 1),
                AiUnit.Simple(2, 1, 300f, 400f),
            };

            AiBattlefield low = Field(units, 1, true, true, weapons: new[] { WeaponId.Dynamite });
            var unitsHigh = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f, luck: 10),
                AiUnit.Simple(3, 0, 120f, 400f, luck: 10),
                AiUnit.Simple(2, 1, 300f, 400f),
            };
            AiBattlefield high = Field(unitsHigh, 1, true, true, weapons: new[] { WeaponId.Dynamite });

            AiDecision dLow = AiEvaluation.Evaluate(low, new AiRandom(3));
            AiDecision dHigh = AiEvaluation.Evaluate(high, new AiRandom(3));

            Assert.AreEqual(51, dLow.EvaluationCount, "50 次自抛 + luck=1 的 1 次武器采样");
            Assert.AreEqual(60, dHigh.EvaluationCount, "50 次自抛 + luck=10 的 10 次武器采样");
            Assert.Greater(dHigh.EvaluationCount, dLow.EvaluationCount);
        }

        [Test]
        public void LuckZero_DoesNotEvaluateWeapons_ButStaysLegal()
        {
            // luck=0 → count=0 → 武器阶段不产出候选；仍有 50 次自抛采样，决定必须合法。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f, luck: 0),
                AiUnit.Simple(2, 1, 300f, 400f),
            };
            AiBattlefield field = Field(units, 1, true, true, weapons: new[] { WeaponId.Dynamite });

            AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(5));

            Assert.AreEqual(50, d.EvaluationCount, "luck=0 时只有 50 次自抛采样");
            Assert.IsTrue(d.Kind == AiActionKind.ThrowSelf || d.Kind == AiActionKind.EndGo);
            Assert.AreEqual(-1, d.WeaponSlotIndex, "luck=0 不得产出武器动作");
        }

        // ==================================================================
        // 无武器 / 无候选回退（不返回非法动作）
        // ==================================================================

        [Test]
        public void NoWeapons_CanThrow_FallsBackToThrowSelf()
        {
            AiBattlefield field = Duel(new WeaponId[0]);
            AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(11));

            Assert.AreEqual(AiActionKind.ThrowSelf, d.Kind);
            Assert.AreEqual(-1, d.WeaponSlotIndex);
        }

        [Test]
        public void NoWeapons_NoThrow_ReturnsEndGo_NotIllegalWeapon()
        {
            // continueTurn 且无武器：canThrow=false、canShoot=true 但背包为空 → 无候选 → EndGo。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 300f, 400f),
            };
            AiBattlefield field = Field(units, 1, canThrow: false, canShoot: true);

            AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(11), canBailOut: true);

            Assert.AreEqual(AiActionKind.EndGo, d.Kind);
            Assert.AreEqual(-1, d.WeaponSlotIndex, "非法动作防护：不能在无武器时返回 UseWeapon");
            Assert.AreEqual(0, d.EvaluationCount);
            Assert.IsTrue(d.ShouldBailOut, "无候选 + 允许放弃 → 应建议跳过回合");
        }

        [Test]
        public void TargetedButOutOfRange_StaysLegalAndConsistent()
        {
            // 目标全在极远处（5100px）的边界：不能崩溃，必须返回合法动作。
            // 注意 §6.2 的质心项是无界的「更靠近敌方质心就加分」，所以哪怕敌人在 5000px 外，
            // 朝它方向落点仍可能得正分——这是原版公式的事实行为，本测试只保证合法性与
            // ShouldBailOut 的一致性，不臆断一定为负。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 5100f, 400f),
            };
            AiBattlefield field = Field(units, 1, true, true, weapons: new[] { WeaponId.Dynamite });

            AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(21), canBailOut: true);

            Assert.IsTrue(
                d.Kind == AiActionKind.ThrowSelf
                || d.Kind == AiActionKind.UseWeapon
                || d.Kind == AiActionKind.EndGo);
            Assert.IsTrue(d.WeaponSlotIndex == -1 || d.WeaponSlotIndex == 0, "武器槽位必须合法");
            Assert.IsFalse(float.IsNaN(d.Success) || float.IsInfinity(d.Success), "评分必须有限");
            Assert.AreEqual(d.FlashSuccess <= 0f, d.ShouldBailOut, "bailout 应与原始 success ≤ 0 一致");
        }

        // ==================================================================
        // 边界：落水 / 单一存活
        // ==================================================================

        [Test]
        public void ThrownOutsideArena_AllThrowsDrown_ScoresNegative()
        {
            // 竞技场矩形缩到原点附近，投掷者在其外 → 所有投掷都掉出地面 → 落水（§4.4），
            // 自抛每个样本 −2，整体 success ≤ 0；允许放弃时建议跳过。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 300f, 400f),
            };
            var tiny = new AiTerrain(0f, 1f, 0f, 1f);
            AiBattlefield field = Field(units, 1, true, true, terrain: tiny);

            AiEvaluationSession session = RunToEnd(field, 33);

            Assert.Greater(session.Candidates.Count, 0);
            for (int i = 0; i < session.Candidates.Count; i++)
            {
                Assert.LessOrEqual(session.Candidates[i].FlashSuccess, 0f,
                    "整片水域时应无正收益（含落水 −2 惩罚）");
            }

            AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(33), canBailOut: true);
            Assert.IsTrue(d.Kind == AiActionKind.ThrowSelf || d.Kind == AiActionKind.EndGo);
            Assert.IsTrue(d.ShouldBailOut);
        }

        [Test]
        public void SingleSurvivingActor_NoEnemy_StaysLegal()
        {
            // 只有 1 个存活角色（无敌人、无队友）：不能因除零/空集合崩溃，决定必须合法，
            // 且 ShouldBailOut 必须与 §6.1 的 best.success > 0 判据一致。
            //
            // 【3D 落点质量项已实现】无存活敌人时 AiBattlefield.EnemyCentroid 回落到 actor 自身位置，
            // 落点质量项 = −0.003 ×（落点到自身的 XZ 水平面像素距离）≤ 0，故自抛候选不会因纵深漂移得正分。
            // 这里仍只校验合法性与 bailout 一致性，不对具体 success 符号下断言（本用例还带一个 dynamite 槽，
            // 其收益走 §6.3 独立路径）。
            var units = new List<AiUnit> { AiUnit.Simple(1, 0, 100f, 400f) };
            AiBattlefield field = Field(units, 1, true, true, weapons: new[] { WeaponId.Dynamite });

            AiDecision d = AiEvaluation.Evaluate(field, new AiRandom(42), canBailOut: true);

            Assert.IsTrue(
                d.Kind == AiActionKind.ThrowSelf
                || d.Kind == AiActionKind.UseWeapon
                || d.Kind == AiActionKind.EndGo);
            Assert.IsTrue(d.WeaponSlotIndex == -1 || d.WeaponSlotIndex == 0, "武器槽位必须合法");
            Assert.AreEqual(1, d.ActorUnitId);
            Assert.IsFalse(float.IsNaN(d.Success) || float.IsInfinity(d.Success), "评分必须有限");
            Assert.AreEqual(d.FlashSuccess <= 0f, d.ShouldBailOut, "bailout 应与原始 success ≤ 0 一致");
        }

        // ==================================================================
        // §5.2 表末 7 种特殊武器的专门评分路径
        // ==================================================================

        [Test]
        public void Special_TidalWave_ScoresEnemiesNearWater_MinusAllies()
        {
            // §6.3：s = Σ_受浪敌人 health/maxHealth×0.5 − 1.5×受浪队友数；受浪条件 y ≥ waterY−300。
            // 平面 y 即纵深：waterY=600 → 阈值 300。敌人纵深 400 在带内；actor 放在纵深 200
            // （带外）以免把「自己是否被浪打到」也算进队友惩罚。
            var enemyOnly = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 200f),
                AiUnit.Simple(2, 1, 300f, 400f),
            };
            var withAlly = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 200f),
                AiUnit.Simple(3, 0, 120f, 400f),
                AiUnit.Simple(2, 1, 300f, 400f),
            };

            float enemyScore = AiEvaluation.ScoreTidalWave(Field(enemyOnly, 1, true, true), 1);
            float allyScore = AiEvaluation.ScoreTidalWave(Field(withAlly, 1, true, true), 1);

            Assert.AreEqual(0.5f, enemyScore, 1e-4f, "1 个满血敌人 × 0.5");
            Assert.AreEqual(-1.0f, allyScore, 1e-4f, "0.5 − 1.5（1 个在线内队友）");

            // 专门路径必须被评估器真正走到：只带 tidalWave 时产出该武器的候选。
            AiEvaluationSession session = RunToEnd(
                Field(enemyOnly, 1, true, true, weapons: new[] { WeaponId.TidalWave }), 1);
            Assert.IsTrue(HasCandidate(session, 0, WeaponId.TidalWave));
        }

        [Test]
        public void Special_Anchor_HalvesScore_AndHasDedicatedPath()
        {
            // §6.3：anchor 收益减半 s *= 0.5。
            Assert.AreEqual(1.5f, AiEvaluation.ScoreAnchor(3f), 1e-4f);
            Assert.AreEqual(0.4f, AiEvaluation.ApplyWeaponModifier(WeaponId.Anchor, 0.8f), 1e-4f);

            // 专门路径：点击直落没有弹弓初速，仍应产出 anchor 候选（按敌方附近列 + 命中带）。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, Lane),
                AiUnit.Simple(2, 1, 300f, Lane),
            };
            AiEvaluationSession session = RunToEnd(
                Field(units, 1, true, true, weapons: new[] { WeaponId.Anchor }), 1);

            Assert.IsTrue(HasCandidate(session, 0, WeaponId.Anchor));
            // 命中带 |x−anchorX| < 48 且 anchorY−64 < y < anchorY：候选落点 y = 敌人纵深 + 32 = 432，
            // 敌人纵深 400 落在 (368, 432) → 有伤害。
            float anchorY = Lane + AiEvaluation.AnchorVerticalBand * 0.5f;
            Assert.Greater(
                AiEvaluation.ExpectedDamage(WeaponId.Anchor, 300f, anchorY, Field(units, 1, true, true), 1),
                0f);
        }

        [Test]
        public void Special_PiecesOfEight_UsesAggressiveTransform()
        {
            // §6.3：s = (s − 0.5) × 1.2。验算：s=2 → 1.8；s=0.5 → 0；s=0 → −0.6。
            Assert.AreEqual(1.8f, AiEvaluation.ScorePiecesOfEight(2f), 1e-4f);
            Assert.AreEqual(0f, AiEvaluation.ScorePiecesOfEight(0.5f), 1e-4f);
            Assert.AreEqual(-0.6f, AiEvaluation.ScorePiecesOfEight(0f), 1e-4f);
            Assert.AreEqual(1.8f, AiEvaluation.ApplyWeaponModifier(WeaponId.PiecesOfEight, 2f), 1e-4f);

            AiEvaluationSession session = RunToEnd(
                Duel(new[] { WeaponId.PiecesOfEight }), 1);
            Assert.IsTrue(HasCandidate(session, 0, WeaponId.PiecesOfEight));
        }

        [Test]
        public void Special_VoodooDoll_DrowningTargetIsTopGain_WithTargetId()
        {
            // §6.3：对每个存活敌人随机投 2 次；落水 → s = 1+rand×0.2 ∈ [1,1.2)，否则 rand×0.2−0.5 ∈ [−0.5,−0.3)。
            // 3D 下用「竞技场矩形不含敌人」强制任何投掷都掉出地面 → 落水。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 380f),
                AiUnit.Simple(2, 1, 300f, 380f),
            };
            var tiny = new AiTerrain(0f, 50f, 0f, 50f);
            AiBattlefield field = Field(units, 1, true, true, terrain: tiny);

            var candidates = new List<AiMoveCandidate>();
            int evalCount = 0;
            AiEvaluation.PlanVoodoo(field, 1, 0, new AiRandom(9), candidates, ref evalCount);

            Assert.AreEqual(2, candidates.Count, "1 个敌人 × 2 次模拟");
            Assert.AreEqual(2, evalCount);
            for (int i = 0; i < candidates.Count; i++)
            {
                Assert.AreEqual(2, candidates[i].TargetUnitId, "voodoo 候选必须带锁定目标");
                Assert.GreaterOrEqual(candidates[i].FlashSuccess, 1f, "落水目标 = 最高收益");
                Assert.Less(candidates[i].FlashSuccess, 1.2f);
            }

            AiEvaluationSession session = RunToEnd(
                Field(units, 1, true, true, terrain: tiny,
                    weapons: new[] { WeaponId.VoodooDoll }), 9);
            Assert.IsTrue(HasCandidate(session, 0, WeaponId.VoodooDoll));
        }

        [Test]
        public void Special_Seagull_PlansHeightAndPositiveShotPoints()
        {
            // §6.3：高度 = 单位顶端 + 100 + rand×100（3D 世界高度，相对地面，单位 px）；
            // 随机 10 个 x 落点，只保留正收益落点并要求 shots>1。
            // 地形收窄到敌人 x∈[295,305] 附近，保证每个随机落点都落在敌人 40px 内。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 50f, Lane),
                AiUnit.Simple(2, 1, 300f, Lane),
            };
            AiBattlefield field = Field(
                units, 1, true, true, terrain: Terrain(minX: 295f, maxX: 305f));

            bool ok = AiEvaluation.TryPlanSeagull(
                field, 1, new AiRandom(4), out float success, out float height, out IReadOnlyList<float> shots);

            Assert.IsTrue(ok, "10 个落点都在敌人 40px 内 → 应成立");
            float minH = LevelGeometry.UnitsToPixels(LevelGeometry.UnitPivotHeight)
                + AiEvaluation.SeagullHeightAboveUnitTop;
            Assert.GreaterOrEqual(height, minH - 1e-3f, "高度不得低于 单位顶端 + 100px");
            Assert.LessOrEqual(height, minH + AiEvaluation.SeagullHeightRandomDrop + 1e-3f,
                "高度不得超过 单位顶端 + 200px");
            Assert.Greater(shots.Count, 1);
            Assert.Greater(success, 0f);

            AiEvaluationSession session = RunToEnd(
                Field(units, 1, true, true, terrain: Terrain(minX: 295f, maxX: 305f),
                    weapons: new[] { WeaponId.Seagull }), 4);
            Assert.IsTrue(HasCandidate(session, 0, WeaponId.Seagull));
        }

        [Test]
        public void Special_WoodenCrate_And_GunpowderBarrel_UseDedicatedPlacementPath()
        {
            // §6.3 BoxWeapon：10 个候选点、要求 ≥3 个可行点、success = random（纯随机）。
            // 固定 seed 下该路径成立（地面放置判定见 AiTerrain.CanPlace）。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, Lane),
                AiUnit.Simple(2, 1, 300f, Lane),
            };
            AiBattlefield field = Field(units, 1, true, true);

            // TryPlanBoxPlacement 的可行性由随机纵深偏移决定（10 个点里需 ≥3 个满足足迹在矩形内）。
            // 在确定性的 0..30 种子空间里找出可用种子（PRNG 固定 → 该搜索本身也是确定性的）。
            int crateSeed = -1;
            int barrelSeed = -1;
            for (int seed = 0; seed <= 30; seed++)
            {
                if (crateSeed < 0 && AiEvaluation.TryPlanBoxPlacement(
                        field, 1, new AiRandom(seed), out _, out _))
                    crateSeed = seed;

                if (barrelSeed < 0 && AiEvaluation.TryPlanBoxPlacement(
                        field, 1, new AiRandom(seed), out _, out _))
                    barrelSeed = seed;
            }

            Assert.GreaterOrEqual(crateSeed, 0, "在 0..30 中应存在能找到 ≥3 个可行放置点的种子");

            bool crateOk = AiEvaluation.TryPlanBoxPlacement(
                field, 1, new AiRandom(crateSeed), out float crateSuccess, out IReadOnlyList<(float x, float y)> cratePoints);
            bool barrelOk = AiEvaluation.TryPlanBoxPlacement(
                field, 1, new AiRandom(barrelSeed), out float barrelSuccess, out _);

            Assert.IsTrue(crateOk, "在 0..30 中应存在能找到 ≥3 个可行放置点的种子");
            Assert.IsTrue(barrelOk);
            Assert.GreaterOrEqual(cratePoints.Count, AiEvaluation.BoxMinFeasiblePoints);
            Assert.GreaterOrEqual(crateSuccess, 0f);
            Assert.Less(crateSuccess, 1f, "success = random ∈ [0,1)");

            AiEvaluationSession crateSession = RunToEnd(
                Duel(new[] { WeaponId.WoodenCrate }), crateSeed);
            Assert.IsTrue(HasCandidate(crateSession, 0, WeaponId.WoodenCrate));

            AiEvaluationSession barrelSession = RunToEnd(
                Duel(new[] { WeaponId.GunpowderBarrel }), barrelSeed);
            Assert.IsTrue(HasCandidate(barrelSession, 0, WeaponId.GunpowderBarrel));

            // gunpowderBarrel(150,30) 在放置点若能覆盖敌人，应计入预期伤害。
            float expected = AiEvaluation.ExpectedDamage(
                WeaponId.GunpowderBarrel, 300f, Lane, field, 1);
            Assert.Greater(expected, 0f);
        }

        // ==================================================================
        // §6.3 通用武器打分公式核对
        // ==================================================================

        [Test]
        public void ScoreWeaponSample_EnemyInRange_MatchesFormula()
        {
            // 落点正好在敌人身上（d=0）→ s = −0.01 + (1.5 − 0) = 1.49，无队友/无 evilness。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(2, 1, 300f, 400f),
            };
            AiBattlefield field = Field(units, 1, true, true);
            var sample = new AiThrowSample(0f, 0f, 300f, 400f, false);

            float s = AiEvaluation.ScoreWeaponSample(WeaponId.CherryBomb, sample, field, 1);
            Assert.AreEqual(1.49f, s, 1e-4f);
        }

        [Test]
        public void ScoreWeaponSample_AllyInRange_IsPenalised()
        {
            // 队友在落点 ±40px 内 → s -= 1.5 − d/40。取纵深方向 20px 处：1.5 − 0.5 = 1.0 惩罚。
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, 400f),
                AiUnit.Simple(3, 0, 300f, 380f),
            };
            AiBattlefield field = Field(units, 1, true, true);
            var sample = new AiThrowSample(0f, 0f, 300f, 400f, false);

            float s = AiEvaluation.ScoreWeaponSample(WeaponId.CherryBomb, sample, field, 1);
            Assert.AreEqual(-0.01f - 1.0f, s, 1e-4f);
        }
    }
}
