using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// Battle 场景装配完整性 PlayMode 测试（可选交付物，只能由协调者在 Unity 里跑）。
    ///
    /// 【验证闭环】对应 <c>M2BattleSceneSetup</c> 的装配结果：
    ///   · 关键 <c>[SerializeField]</c> 引用非空（BattleController / TurnManager /
    ///     AimThrowController / TrajectoryPreview / BattleCameraController / BattleHud）；
    ///   · 双方船员数量 = <see cref="LevelCatalog"/> 关卡数据（level_1：红 5 / 蓝 3）；
    ///   · 水面世界 Y = <c>BattleController.WaterWorldY</c>（BuildPlan 结果）；
    ///   · TurnManager 已开始回合，且 end go 后能推进到另一队。
    ///
    /// 【说明】私有序列化字段用反射读取，避免为了测试扩大运行时 API；字段名与装配脚本
    ///         <c>SerializedObject.FindProperty(...)</c> 使用的名字一一对应。
    /// </summary>
    public class BattleSceneWiringTests
    {
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static object Field(object target, string name)
        {
            Assert.IsNotNull(target, "读取字段 " + name + " 时目标对象为空");
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, target.GetType().Name + "." + name + " 字段不存在（装配脚本字段名漂移？）");
            return field.GetValue(target);
        }

        [UnityTest]
        public IEnumerator BattleScene_IsFullyWired()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Battle);
            yield return null;
            yield return null;

            BattleController controller = Object.FindObjectOfType<BattleController>();
            Assert.IsNotNull(controller, "Battle 场景应有 BattleController");

            // ---- 关键序列化引用非空 ----
            Assert.IsNotNull(Field(controller, "piratePrefab"), "BattleController.piratePrefab 未接线");
            Assert.IsNotNull(Field(controller, "team0Root"), "BattleController.team0Root 未接线");
            Assert.IsNotNull(Field(controller, "team1Root"), "BattleController.team1Root 未接线");
            Assert.IsNotNull(Field(controller, "waterPlane"), "BattleController.waterPlane 未接线");
            Assert.IsNotNull(Field(controller, "turnManager"), "BattleController.turnManager 未接线");
            Assert.IsNotNull(Field(controller, "aimController"), "BattleController.aimController 未接线");
            Assert.IsNotNull(Field(controller, "battleCamera"), "BattleController.battleCamera 未接线");

            var turnManager = Object.FindObjectOfType<TurnManager>();
            Assert.IsNotNull(turnManager, "Battle 场景应有 TurnManager");
            Assert.IsNotNull(Field(turnManager, "battle"), "TurnManager.battle 未接线");

            var aim = Object.FindObjectOfType<AimThrowController>();
            Assert.IsNotNull(aim, "Battle 场景应有 AimThrowController");
            Assert.IsNotNull(Field(aim, "battleCamera"), "AimThrowController.battleCamera 未接线");
            Assert.IsNotNull(Field(aim, "battle"), "AimThrowController.battle 未接线");
            Assert.IsNotNull(Field(aim, "trajectory"), "AimThrowController.trajectory 未接线");

            var camController = Object.FindObjectOfType<BattleCameraController>();
            Assert.IsNotNull(camController, "Battle 场景应有 BattleCameraController");
            Assert.IsNotNull(Field(camController, "virtualCamera"), "BattleCameraController.virtualCamera 未接线");
            Assert.IsNotNull(Field(camController, "cameraTarget"), "BattleCameraController.cameraTarget 未接线");

            Assert.IsNotNull(Object.FindObjectOfType<TrajectoryPreview>(), "Battle 场景应有 TrajectoryPreview");

            // ---- 关卡数据驱动的出征数量一致 ----
            BattlePlan plan = controller.Plan;
            Assert.IsNotNull(plan, "BattleController 应生成出战计划");
            Assert.Greater(plan.Entries.Count, 0, "出战计划不应为空");
            Assert.AreEqual(plan.Entries.Count, controller.AllPirates.Count, "实例化角色数应等于出战条目数");
            Assert.AreEqual(plan.CountForTeam(0), controller.GetTeam(0).Characters.Count, "红队人数应与关卡数据一致");
            Assert.AreEqual(plan.CountForTeam(1), controller.GetTeam(1).Characters.Count, "蓝队人数应与关卡数据一致");
            Assert.Greater(plan.CountForTeam(0), 0, "红队应有成员");
            Assert.Greater(plan.CountForTeam(1), 0, "蓝队应有成员");

            // ---- 水位（BattleController.Start 已把水面对象移到 WaterWorldY）----
            var waterPlane = (Transform)Field(controller, "waterPlane");
            Assert.AreEqual(controller.WaterWorldY, waterPlane.position.y, 1e-3f, "水面 y 应等于计划水位");

            // ---- HUD 接线 ----
            var hud = Object.FindObjectOfType<BattleHud>();
            Assert.IsNotNull(hud, "Battle 场景应有 BattleHud");
            Assert.IsTrue(hud.HasCoreReferences, "BattleHud 核心引用未接线");
            Assert.AreEqual(17, hud.WeaponSlotCount, "HUD 应有 17 个武器槽（§5.2）");
            Assert.IsTrue(hud.HasWeaponWiring, "HUD 武器按钮/文本未全部接线");
            Assert.AreEqual(12, hud.RosterRowCount, "HUD 名册应有 12 行");
            Assert.IsTrue(hud.HasRosterWiring, "HUD 名册控件未全部接线");

            // ---- 回合开始与推进 ----
            Assert.IsTrue(turnManager.Started, "TurnManager 应已开始第一回合");
            Assert.IsNotNull(turnManager.CurrentTeam, "当前队不应为空");

            BattleTeam team = turnManager.CurrentTeam;
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
