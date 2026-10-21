using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// S2 弹跳/摩擦参数化的数据面与 3D 积分封装测试。
    ///
    /// 【回归钉】覆写表现役为空 → 全部武器回退全局默认（BalanceConfig.Defaults），
    /// 与参数化前逐位一致；登记后才按武器分叉。
    /// 【积分数学】断言只覆盖 <see cref="Ballistics"/> 口径在世界向量上的拆装
    /// （方向保持/下限保护/竖直独立），公式本体由 BallisticsTests 钉住。
    /// </summary>
    [TestFixture]
    public class ProjectileContactRulesTests
    {
        const float Eps = 1e-4f;

        [SetUp]
        public void SetUp()
        {
            ProjectileContactOverrides.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // 静态表跨用例共享：每条用例后清空，避免污染其他测试文件的默认回退断言。
            ProjectileContactOverrides.Clear();
        }

        // ------------------------------------------------------------------
        // 覆写表：空表回退全局默认（现状逐位一致）
        // ------------------------------------------------------------------

        [Test]
        public void EmptyTable_AllWeaponsResolveToGlobalDefaults()
        {
            Assert.AreEqual(0, ProjectileContactOverrides.Count);

            foreach (WeaponStats stats in WeaponCatalog.All)
            {
                ProjectileContactRules.Resolve(stats.Id, out float friction, out float bounce);
                Assert.AreEqual(BalanceConfig.Defaults.DefaultFriction, friction, Eps, stats.Id.ToString());
                Assert.AreEqual(BalanceConfig.Defaults.DefaultBounce, bounce, Eps, stats.Id.ToString());
            }
        }

        [Test]
        public void Registered_OverrideChangesOnlyThatWeapon()
        {
            ProjectileContactOverrides.Register(
                WeaponId.Banana, new ProjectileContactOverride(friction: 0.5f, bounce: 0.8f));
            Assert.AreEqual(1, ProjectileContactOverrides.Count);
            Assert.IsTrue(ProjectileContactRules.HasManualIntegration(WeaponId.Banana));
            Assert.IsFalse(ProjectileContactRules.HasManualIntegration(WeaponId.CherryBomb));

            ProjectileContactRules.Resolve(WeaponId.Banana, out float friction, out float bounce);
            Assert.AreEqual(0.5f, friction, Eps);
            Assert.AreEqual(0.8f, bounce, Eps);

            // 其余武器仍回退默认。
            ProjectileContactRules.Resolve(WeaponId.CherryBomb, out float otherFriction, out float otherBounce);
            Assert.AreEqual(BalanceConfig.Defaults.DefaultFriction, otherFriction, Eps);
            Assert.AreEqual(BalanceConfig.Defaults.DefaultBounce, otherBounce, Eps);
        }

        [Test]
        public void Cleared_FallsBackToDefaultsAgain()
        {
            ProjectileContactOverrides.Register(
                WeaponId.Boulder, new ProjectileContactOverride(friction: 0.25f, bounce: 0.2f));
            ProjectileContactOverrides.Clear();

            Assert.IsFalse(ProjectileContactRules.HasManualIntegration(WeaponId.Boulder));
            ProjectileContactRules.Resolve(WeaponId.Boulder, out float friction, out float bounce);
            Assert.AreEqual(BalanceConfig.Defaults.DefaultFriction, friction, Eps);
            Assert.AreEqual(BalanceConfig.Defaults.DefaultBounce, bounce, Eps);
        }

        // ------------------------------------------------------------------
        // 撞地积分（3D 拆装）：方向保持 / 摩擦下限 / 竖直独立
        // ------------------------------------------------------------------

        [Test]
        public void IntegrateGroundVelocity_ReducesHorizontalMagnitude_KeepsDirection_BouncesVertical()
        {
            // 水平 (3,4) 模长 5，friction=2 → 3，方向保持 (1.8, 2.4)；vy=5、bounce=0.2 → -1。
            Vector3 result = ProjectileContactRules.IntegrateGroundVelocity(
                new Vector3(3f, 5f, 4f), friction: 2f, bounce: 0.2f);
            Assert.AreEqual(1.8f, result.x, Eps);
            Assert.AreEqual(-1f, result.y, Eps);
            Assert.AreEqual(2.4f, result.z, Eps);
        }

        [Test]
        public void IntegrateGroundVelocity_FrictionDoesNotReverseOrNullDirection()
        {
            // 模长 1 < friction 2 → 水平归零（不是反向），竖直照常反弹。
            Vector3 result = ProjectileContactRules.IntegrateGroundVelocity(
                new Vector3(1f, 0f, 0f), friction: 2f, bounce: 0.5f);
            Assert.AreEqual(0f, result.x, Eps);
            Assert.AreEqual(0f, result.z, Eps);
            Assert.AreEqual(0f, result.y, Eps);

            // 零水平速度：不产生方向（除零保护），竖直照常。
            Vector3 verticalOnly = ProjectileContactRules.IntegrateGroundVelocity(
                new Vector3(0f, -4f, 0f), friction: 2f, bounce: 0.25f);
            Assert.AreEqual(0f, verticalOnly.x, Eps);
            Assert.AreEqual(0f, verticalOnly.z, Eps);
            Assert.AreEqual(1f, verticalOnly.y, Eps);
        }

        [Test]
        public void IntegrateGroundVelocity_ZeroBounce_KillsVerticalOnly()
        {
            Vector3 result = ProjectileContactRules.IntegrateGroundVelocity(
                new Vector3(3f, -5f, 0f), friction: 1f, bounce: 0f);
            Assert.AreEqual(2f, result.x, Eps);
            Assert.AreEqual(0f, result.y, Eps);
        }

        // ------------------------------------------------------------------
        // 撞墙积分（3D 拆装）：水平反向衰减、竖直不变
        // ------------------------------------------------------------------

        [Test]
        public void IntegrateWallVelocity_ReversesBothHorizontalAxes_KeepsVertical()
        {
            // x=3 → 3 × -0.4 = -1.2；z=-2 → 0.8；y=5 原样。
            Vector3 result = ProjectileContactRules.IntegrateWallVelocity(new Vector3(3f, 5f, -2f));
            Assert.AreEqual(3f * Ballistics.WallBounceScale, result.x, Eps);
            Assert.AreEqual(5f, result.y, Eps);
            Assert.AreEqual(-2f * Ballistics.WallBounceScale, result.z, Eps);
        }

        [Test]
        public void IntegrateWallVelocity_ZeroX_StillReversesZ()
        {
            // 水平速度全在 Z 轴上时同样按墙系数反向（Ballistics 平面没有 Z 轴，此处补齐）。
            Vector3 result = ProjectileContactRules.IntegrateWallVelocity(new Vector3(0f, 2f, -5f));
            Assert.AreEqual(0f, result.x, Eps);
            Assert.AreEqual(2f, result.y, Eps);
            Assert.AreEqual(-5f * Ballistics.WallBounceScale, result.z, Eps);
        }
    }
}
