using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// §6.3 cannon（加农炮）AI 评估的纯 C# 测试（无头可跑）。
    ///
    /// 【依据】静态逆向文档 §6.3 cannon 行：<c>Cannon.randomThrows(count)</c> 用
    /// 「随机角度 0–360 + 随机拖拽距离（≤ dragRange/2）决定炮位」，<c>aiFireTime=25</c> 后发射。
    /// 本工程把可执行部分实现为 <see cref="AiEvaluation.PlanCannon"/>：只评估炮位
    /// （运行时对 AI 队会自动朝最近敌人发射，见 WeaponProjectile.UpdateCannon）。
    /// </summary>
    [TestFixture]
    public class AiCannonTests
    {
        const float Lane = 400f;
        const float Water = 600f;

        static AiBattlefield FieldWithCannon()
        {
            var units = new List<AiUnit>
            {
                AiUnit.Simple(1, 0, 100f, Lane, luck: 5),
                AiUnit.Simple(2, 1, 300f, Lane),
            };
            var weapons = new List<AiWeaponSlot> { new AiWeaponSlot(0, WeaponId.Cannon) };
            return new AiBattlefield(
                units, 1, canThrow: false, canShoot: true, weapons,
                new AiTerrain(0f, 2000f, 0f, 2000f), Water);
        }

        static AiEvaluationSession RunToEnd(AiBattlefield field, int seed)
        {
            AiEvaluationSession session = AiEvaluation.CreateSession(field, new AiRandom(seed));
            while (!session.IsFinished)
            {
                if (!session.StepOnce())
                    break;
            }
            return session;
        }

        static List<AiMoveCandidate> CannonCandidates(AiEvaluationSession session)
        {
            var list = new List<AiMoveCandidate>();
            for (int i = 0; i < session.Candidates.Count; i++)
            {
                if (session.Candidates[i].WeaponId == WeaponId.Cannon)
                    list.Add(session.Candidates[i]);
            }
            return list;
        }

        [Test]
        public void PlanCannon_ProducesCandidateForCannonSlot()
        {
            AiEvaluationSession session = RunToEnd(FieldWithCannon(), seed: 12345);

            List<AiMoveCandidate> cannons = CannonCandidates(session);
            Assert.Greater(cannons.Count, 0, "§6.3 cannon 分支应产出炮位候选");
            Assert.AreEqual(0, cannons[0].WeaponSlotIndex, "候选应指向 cannon 槽位");
        }

        [Test]
        public void PlanCannon_PlacementWithinDragRangeHalfOfActor()
        {
            // §6.3：炮位 = 角色位置 + 极坐标（随机角度 × 距离 ≤ dragRange/2 = 60px）。
            AiEvaluationSession session = RunToEnd(FieldWithCannon(), seed: 999);
            List<AiMoveCandidate> cannons = CannonCandidates(session);

            for (int i = 0; i < cannons.Count; i++)
            {
                float dx = cannons[i].AimX - 100f;   // actor.X
                float dy = cannons[i].AimY - Lane;   // actor.Y
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                Assert.LessOrEqual(distance, AiEvaluation.CannonPlacementMaxOffsetPx + 1e-3f,
                    "炮位偏移不应超过 dragRange/2");
            }
        }

        [Test]
        public void PlanCannon_SameSeedProducesIdenticalPlacements()
        {
            AiEvaluationSession a = RunToEnd(FieldWithCannon(), seed: 4242);
            AiEvaluationSession b = RunToEnd(FieldWithCannon(), seed: 4242);

            List<AiMoveCandidate> ca = CannonCandidates(a);
            List<AiMoveCandidate> cb = CannonCandidates(b);

            Assert.AreEqual(ca.Count, cb.Count, "同一 seed 的评估应完全可复现");
            for (int i = 0; i < ca.Count; i++)
            {
                Assert.AreEqual(ca[i].AimX, cb[i].AimX, 1e-6f);
                Assert.AreEqual(ca[i].AimY, cb[i].AimY, 1e-6f);
            }
        }

        [Test]
        public void CannonPlacementConstants_MatchDerivedDragRange()
        {
            // 炮位圈 = CannonRules.FullChargeDragPx（30/0.25 = 120），偏移上限 = 其一半。
            Assert.AreEqual(120f, AiEvaluation.CannonDragRangePx, 1e-4f);
            Assert.AreEqual(60f, AiEvaluation.CannonPlacementMaxOffsetPx, 1e-4f);
        }
    }
}
