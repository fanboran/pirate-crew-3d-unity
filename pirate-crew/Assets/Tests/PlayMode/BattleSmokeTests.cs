using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// M2 战斗组装层 PlayMode 冒烟测试。
    ///
    /// 【运行环境】只能在 Unity 里跑（<c>new GameObject()</c> 与 <c>[UnityTest]</c> 在无头验证台会抛 ECall）。
    ///   纯逻辑覆盖在 <c>Assets/Tests/Battle/*.cs</c>（HarnessScope=Battle）。
    ///
    /// 【覆盖】PirateBase 的伤害/死亡/落水/保底武器/行动经济，TrajectoryPreview 与 Ballistics 同源，
    ///   以及（需场景接线后）Battle 场景双方生成数量与 TurnManager 回合推进。
    /// </summary>
    public class BattleSmokeTests
    {
        static PirateBase CreatePirate(int teamIndex, out GameObject go)
        {
            go = new GameObject("TestPirate", typeof(Rigidbody), typeof(BoxCollider), typeof(PirateBase));
            var pirate = go.GetComponent<PirateBase>();
            var entry = new SpawnPlanEntry(
                teamIndex, "redPirate", 5, 0, 0, Vector3.zero,
                new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) });
            pirate.Initialize(0, entry);
            return pirate;
        }

        [UnityTest]
        public IEnumerator PirateBase_TakesDamageAndDies()
        {
            PirateBase pirate = CreatePirate(0, out GameObject go);
            try
            {
                Assert.IsTrue(pirate.Alive);
                Assert.AreEqual(100, pirate.Health);

                Assert.IsFalse(pirate.SubtractHealth(40f));
                Assert.AreEqual(60, pirate.Health);

                Assert.IsTrue(pirate.SubtractHealth(60f));
                Assert.AreEqual(0, pirate.Health);
                Assert.IsFalse(pirate.Alive);
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PirateBase_DrownsBelowWater()
        {
            PirateBase pirate = CreatePirate(0, out GameObject go);
            try
            {
                // 水面世界 y = -14；角色放到 -20 → 落水即死（全局规则）。
                pirate.transform.position = new Vector3(0f, -20f, 0f);
                Assert.IsTrue(pirate.CheckWaterDeath(-14f));
                Assert.IsFalse(pirate.Alive);
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PirateBase_ResetGrantsFallbackWeaponAndClearsEvilness()
        {
            var go = new GameObject("TestPirate2", typeof(Rigidbody), typeof(BoxCollider), typeof(PirateBase));
            PirateBase pirate = go.GetComponent<PirateBase>();
            try
            {
                // 空背包 + 已有 evilness。
                var entry = new SpawnPlanEntry(0, "redPirate", 5, 0, 0, Vector3.zero, null);
                pirate.Initialize(0, entry);
                pirate.AddEvilness(3f);
                Assert.AreEqual(3, pirate.Evilness);

                pirate.ResetForTurnStart();

                Assert.AreEqual(0, pirate.Evilness);
                Assert.IsTrue(pirate.Inventory.HasAny, "§3.2 每回合开始若空则补 cannonball");
                Assert.IsTrue(pirate.Inventory.TryGetWeaponAt(0, out WeaponId id));
                Assert.AreEqual(WeaponId.Cannonball, id);
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TrajectoryPreview_UsesSameBallisticsSource()
        {
            var go = new GameObject("Trajectory", typeof(LineRenderer), typeof(TrajectoryPreview));
            try
            {
                var preview = go.GetComponent<TrajectoryPreview>();
                var line = go.GetComponent<LineRenderer>();

                preview.Show(100f, 200f, 3f, -12f, 1f);

                // 15 段采样 + 起点。
                Assert.AreEqual(Ballistics.DefaultPredictionSteps + 1, line.positionCount);

                Vector3 expectedOrigin = LevelGeometry.PixelToWorld(100f, 200f);
                Assert.AreEqual(expectedOrigin.x, line.GetPosition(0).x, 1e-4f);
                Assert.AreEqual(expectedOrigin.y, line.GetPosition(0).y, 1e-4f);

                // 第 1 个采样点必须等于 Ballistics 的输出经 LevelGeometry 换算后的世界坐标。
                var points = Ballistics.PredictTrajectory(100f, 200f, 3f, -12f, 1f, Ballistics.DefaultPredictionSteps);
                Vector3 expected1 = LevelGeometry.PixelToWorld(points[0].x, points[0].y);
                Assert.AreEqual(expected1.x, line.GetPosition(1).x, 1e-4f);
                Assert.AreEqual(expected1.y, line.GetPosition(1).y, 1e-4f);

                preview.Hide();
                Assert.AreEqual(0, line.positionCount);
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
        }

        /// <summary>
        /// 场景级冒烟：Battle 场景需由协调者接线（BattleController/TurnManager/PirateBase 预制体/水位）。
        /// 未接线时用 <see cref="Assert.Ignore"/> 跳过，避免误报；接线后本用例自动生效。
        /// </summary>
        [UnityTest]
        public IEnumerator BattleScene_SpawnsBothTeams_AndTurnAdvances()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Battle);
            yield return null;
            yield return null;

            var controller = Object.FindObjectOfType<BattleController>();
            if (controller == null)
            {
                Assert.Ignore("Battle 场景尚未接入 BattleController（组装层由协调者接线），跳过场景级冒烟。");
                yield break;
            }

            BattlePlan plan = controller.Plan;
            Assert.IsNotNull(plan, "BattleController 应生成出战计划");
            Assert.Greater(plan.Entries.Count, 0);

            // 每个出战条目都应实例化出对应 PirateBase。
            Assert.AreEqual(plan.Entries.Count, controller.AllPirates.Count);

            var turnManager = Object.FindObjectOfType<TurnManager>();
            Assert.IsNotNull(turnManager, "Battle 场景应有 TurnManager");
            Assert.IsTrue(turnManager.Started, "TurnManager 应已开始第一回合");

            BattleTeam team = turnManager.CurrentTeam;
            Assert.IsNotNull(team);

            // 让当前队完成行动：选中首个存活角色并 end go，然后等 inactivity > 10 帧。
            PirateBase actor = team.FirstAlive();
            Assert.IsNotNull(actor, "当前队应有存活角色");
            team.Select(actor);
            actor.MarkEndGo();

            int before = team.Number;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (turnManager.CurrentTeam != null && turnManager.CurrentTeam.Number == before
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreNotEqual(before, turnManager.CurrentTeam.Number, "inactivity > 10 后回合应推进到另一队");
        }
    }
}
