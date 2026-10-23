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
    /// 【覆盖】PirateBase 的伤害/死亡/落水/保底武器/行动经济，TrajectoryPreview 节点显隐
    ///   （弹道数学与「预览 = 实弹」的同源性由 ThrowTrajectory 的纯逻辑域钉），以及
    ///   Battle 场景双方生成数量与 TurnManager 回合推进。
    /// </summary>
    public class BattleSmokeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            HeadlessFrustumNoise.Suppress();
            yield return null;
        }

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

        /// <summary>
        /// R4（节点重做后口径）：弹道预览的视觉节点随「磁轨光带」变体重做已换名——
        /// 现役节点 = <c>TrajectoryArc</c>（光带网格）/ <c>TrajectoryOrigin</c>（根部锚点+引线）/
        /// <c>TrajectoryReticle</c>（落点准星）/ <c>TrajectoryPulse{i}</c>（流动脉冲）；
        /// 旧节点 TrajectoryDots / ImpactMarker / ImpactArrow / ImpactCenter 已根除，不再有对应物。
        /// 断言口径 = 当前真实行为：Awake 自建齐全且收起；瞄准（Show，满力 45° 必穿地）激活；
        /// 失焦（Hide）整组收起。逐点位置语义（不入地/落点终止/与实弹同源）已下沉到
        /// ThrowTrajectory 纯逻辑域（EditMode/harness 覆盖），视觉网格顶点经像素格网
        /// 光栅化（SnapToPixelGrid），不再逐位钉世界坐标。
        /// </summary>
        [UnityTest]
        public IEnumerator TrajectoryPreview_NodesActivateOnShow_AndCollapseOnHide()
        {
            // Show 依赖 Camera.main（TrajectoryPreview.Awake/ResolveCamera 取渲染相机）——
            // 独立组件测试自建一台确定性主相机（透视，无图形环境可建、不触发渲染）。
            GameObject camGo = null;
            GameObject go = null;
            try
            {
                camGo = new GameObject("PreviewCam", typeof(Camera));
                camGo.GetComponent<Camera>().tag = "MainCamera";

                go = new GameObject("Trajectory", typeof(TrajectoryPreview));
                var preview = go.GetComponent<TrajectoryPreview>();

                // 像素光带节点自建齐全，Awake 后默认收起。
                Transform arc = go.transform.Find("TrajectoryArc");
                Transform origin = go.transform.Find("TrajectoryOrigin");
                Transform reticle = go.transform.Find("TrajectoryReticle");
                Transform pulse0 = go.transform.Find("TrajectoryPulse0");
                Assert.IsNotNull(arc, "应有光带网格节点 TrajectoryArc（旧 TrajectoryDots 已根除）");
                Assert.IsNotNull(origin, "应有根部锚点/引线节点 TrajectoryOrigin");
                Assert.IsNotNull(reticle, "应有落点准星节点 TrajectoryReticle（旧 ImpactMarker/ImpactArrow/ImpactCenter 已根除）");
                Assert.IsNotNull(pulse0, "应有流动脉冲节点 TrajectoryPulse0");
                Assert.IsFalse(arc.gameObject.activeSelf, "Awake 后光带应收起");
                Assert.IsFalse(origin.gameObject.activeSelf, "Awake 后根部锚点应收起");
                Assert.IsFalse(reticle.gameObject.activeSelf, "Awake 后准星应收起");
                Assert.IsFalse(pulse0.gameObject.activeSelf, "Awake 后脉冲应收起");

                // 米制口径：起点 = 格位 + 投掷手高度；初速 = 标准投掷模组三参数合成。
                Vector3 throwOrigin = LevelGeometry.GridToArena(3, 6)
                    + Vector3.up * StandardThrowRules.ThrowOriginHeight;
                Vector3 velocity = StandardThrowRules.LaunchVelocity(StandardThrowRules.Initial(45f));

                // 瞄准展示：整组激活（满力 45° 仰角必在兜底步数内穿地，Show 的兜底隐藏分支不触发）。
                preview.Show(throwOrigin, velocity);
                Assert.IsTrue(arc.gameObject.activeSelf, "展示中光带应激活");
                Assert.IsTrue(origin.gameObject.activeSelf, "展示中根部锚点应激活");
                Assert.IsTrue(reticle.gameObject.activeSelf,
                    "展示中准星应激活（showImpactMarker 默认 true）");
                Assert.IsTrue(pulse0.gameObject.activeSelf, "展示中脉冲应激活");

                // 失焦收起：Hide 整组 SetActive(false)。
                preview.Hide();
                Assert.IsFalse(arc.gameObject.activeSelf, "Hide 后光带应收起");
                Assert.IsFalse(origin.gameObject.activeSelf, "Hide 后根部锚点应收起");
                Assert.IsFalse(reticle.gameObject.activeSelf, "Hide 后准星应收起");
                Assert.IsFalse(pulse0.gameObject.activeSelf, "Hide 后脉冲应收起");
            }
            finally
            {
                if (go != null)
                    Object.Destroy(go);
                if (camGo != null)
                    Object.Destroy(camGo);
            }

            yield return null;
        }

        /// <summary>
        /// 场景级冒烟：Battle 场景装配后必须能开战（BattleController/TurnManager/双方生成/回合推进）。
        /// 【T4 修正】原版在缺 BattleController 时 <see cref="Assert.Ignore"/> 静默跳过——那是装配期
        /// 历史遗留；现在场景接线早已收口，PlayMode 加载场景后缺装配**就是回归**，必须响亮红
        /// 而不是静默绿（跳过会伪装成「没测」，让装配断裂从冒烟层漏网）。本用例无合法跳过场景。
        /// </summary>
        [UnityTest]
        public IEnumerator BattleScene_SpawnsBothTeams_AndTurnAdvances()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Battle);
            yield return null;
            yield return null;

            var controller = Object.FindObjectOfType<BattleController>();
            Assert.IsNotNull(controller,
                "Battle 场景缺 BattleController——场景加载后缺装配就是回归（原版此处 Assert.Ignore 静默跳过，已改响亮失败）");

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
