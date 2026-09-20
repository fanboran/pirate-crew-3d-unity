using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.Battle;
using PirateCrew.Combat;
using PirateCrew.Data;
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
                // 3D 模型：水面是世界高度常量 WaterSurfaceY = -0.2，与 Flash 的 waterTileY 无关；
                // 落水即死是全局规则、方向无关（从 X 或 Z 任一侧掉出地面都会落到水面以下）。
                // 角色沿 -Y 下落到水面之下 → 落水即死。
                pirate.transform.position = new Vector3(0f, LevelGeometry.WaterSurfaceY - 0.5f, 0f);
                Assert.IsTrue(pirate.CheckWaterDeath(LevelGeometry.WaterSurfaceY));
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
        public IEnumerator TrajectoryPreview_UsesSameThrowTrajectorySource()
        {
            var go = new GameObject("Trajectory", typeof(LineRenderer), typeof(TrajectoryPreview));
            try
            {
                var preview = go.GetComponent<TrajectoryPreview>();
                var line = go.GetComponent<LineRenderer>();

                // 3D 模型：起点是 XZ 竞技场上的世界点（格位 + 枢轴高度），方向是 XZ 水平方向，
                // 速度取 Flash 口径（px/帧）。预览内部走 LevelGeometry.ThrowVelocity + ThrowTrajectory，
                // 与实弹（PirateBase.ApplyLaunchVelocity / ProjectileSpawnPlanner）同源。
                Vector3 origin = LevelGeometry.GridToArena(3, 6);          // (3.5, 0.25, 6.5)
                var horizontal = new Vector3(1f, 0f, -0.5f);
                const float speedPixelsPerFrame = 12f;                     // px/帧（twang 限速后的模长）
                const float weight = 1f;

                preview.Show(origin, horizontal, speedPixelsPerFrame, weight);

                // 15 段采样 + 起点。
                Assert.AreEqual(ThrowTrajectory.DefaultSteps + 1, line.positionCount);

                // 起点 = 传入的世界原点，x/y/z 三分量原样（旧版曾在 XY 平面翻转 y）。
                Vector3 p0 = line.GetPosition(0);
                Assert.AreEqual(origin.x, p0.x, 1e-4f);
                Assert.AreEqual(origin.y, p0.y, 1e-4f);
                Assert.AreEqual(origin.z, p0.z, 1e-4f);

                // 每个采样点必须等于 ThrowTrajectory 的同源积分（预览 = 实弹的关键不变量）。
                var expected = new Vector3[ThrowTrajectory.DefaultSteps];
                ThrowTrajectory.PredictFromFlashSpeed(
                    origin, horizontal, speedPixelsPerFrame, weight, expected, ThrowTrajectory.DefaultSteps);

                for (int i = 0; i < expected.Length; i++)
                {
                    Vector3 got = line.GetPosition(i + 1);
                    Assert.AreEqual(expected[i].x, got.x, 1e-4f, "采样点 " + i + " 的 X 应同源");
                    Assert.AreEqual(expected[i].y, got.y, 1e-4f, "采样点 " + i + " 的 Y（高度）应同源");
                    Assert.AreEqual(expected[i].z, got.z, 1e-4f, "采样点 " + i + " 的 Z 应同源");
                }

                // 3D 抛物线：XZ 水平面内匀速（重力只沿 -Y，与水平面正交），Y 的速度逐段被重力削去。
                Vector3 step0To1 = expected[1] - expected[0];
                Vector3 step1To2 = expected[2] - expected[1];
                Assert.AreEqual(step0To1.x, step1To2.x, 1e-4f, "XZ 水平面内匀速：X 步长恒定");
                Assert.AreEqual(step0To1.z, step1To2.z, 1e-4f, "XZ 水平面内匀速：Z 步长恒定");
                Assert.Greater(step0To1.y, step1To2.y, "重力沿 -Y：竖直步长逐段变小");

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
