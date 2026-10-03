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
        public IEnumerator TrajectoryPreview_ImpactTerminated_AndNeverBelowGround()
        {
            var go = new GameObject("Trajectory", typeof(TrajectoryPreview));
            try
            {
                var preview = go.GetComponent<TrajectoryPreview>();

                // 米制口径：起点 = 格位 + 投掷手高度；初速 = 标准投掷模组三参数合成。
                Vector3 origin = LevelGeometry.GridToArena(3, 6)
                    + Vector3.up * StandardThrowRules.ThrowOriginHeight;
                Vector3 velocity = StandardThrowRules.LaunchVelocity(StandardThrowRules.Initial(45f));

                preview.Show(origin, velocity);

                // 珠点池自建且逐点不低于地面（投掷行为契约 #9：不入地）。
                Transform dotsRoot = go.transform.Find("TrajectoryDots");
                Assert.IsNotNull(dotsRoot, "预览应自建珠点池");
                int activeDots = 0;
                foreach (Transform dot in dotsRoot)
                {
                    if (!dot.gameObject.activeSelf)
                        continue;
                    activeDots++;
                    Assert.GreaterOrEqual(dot.position.y, LevelGeometry.GroundTopY - 1e-3f,
                        "珠点不得穿到地面之下");
                }
                Assert.Greater(activeDots, 0, "展示中的预览应有可见珠点");

                // 与同源积分一致（预览 = 实弹的关键不变量）：首珠点 = 第一个采样步。
                var expected = new Vector3[StandardThrowRules.PreviewMaxSteps];
                bool landed = ThrowTrajectory.TryPredictUntilImpact(
                    origin, velocity, StandardThrowRules.LaunchGravityY,
                    expected, out int count, out Vector3 impact);
                Assert.IsTrue(landed, "满力 45° 仰角必在兜底步数内穿地");
                Transform firstDot = null;
                foreach (Transform dot in dotsRoot)
                    if (dot.gameObject.activeSelf)
                    {
                        firstDot = dot;
                        break;
                    }
                Assert.AreEqual(expected[0].x, firstDot.position.x, 1e-3f, "首珠点应 = 同源积分第一步");
                Assert.AreEqual(expected[0].y, firstDot.position.y, 1e-3f);
                Assert.AreEqual(expected[0].z, firstDot.position.z, 1e-3f);

                // 落点标记恒显且恰在地面（环抬升 ImpactMarkerLift 防 z-fight）。
                var marker = go.transform.Find("ImpactMarker");
                Assert.IsNotNull(marker, "落点标记应存在（落点终止预览）");
                var markerLine = marker.GetComponent<LineRenderer>();
                Assert.IsTrue(markerLine.enabled, "落点标记应显示");
                Vector3 ringPoint = markerLine.GetPosition(0);
                Assert.AreEqual(impact.y + TrajectoryPreview.ImpactMarkerLift, ringPoint.y, 1e-3f,
                    "落点环应抬在穿地点上方");

                preview.Hide();
                foreach (Transform dot in dotsRoot)
                    Assert.IsFalse(dot.gameObject.activeSelf, "Hide 后珠点应收起");
                Assert.IsFalse(markerLine.enabled, "Hide 后落点标记应隐藏");
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
