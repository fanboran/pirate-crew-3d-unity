using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 操作模组骨架测试：路由表（空表回退标准投掷）+ 放置/直线发射两槽的参数规则
    /// + OperationChanged 载荷的模组字段兼容性。MonoBehaviour 侧的分派
    /// （BattleInteractionController）是薄胶水，链路由占位立方人工/PlayMode 验证。
    /// </summary>
    [TestFixture]
    public class OperationModuleTests
    {
        [SetUp]
        public void SetUp()
        {
            OperationModuleRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            OperationModuleRegistry.Clear();
        }

        // ------------------------------------------------------------------
        // 路由表：空表回退标准投掷（与路由表引入前一致）
        // ------------------------------------------------------------------

        [Test]
        public void EmptyRegistry_AllWeaponsRouteToStandardThrow()
        {
            Assert.AreEqual(0, OperationModuleRegistry.Count);
            foreach (WeaponStats stats in WeaponCatalog.All)
                Assert.AreEqual(OperationModuleKind.StandardThrow,
                    OperationModuleRegistry.ModuleFor(stats.Id), stats.Id.ToString());
        }

        [Test]
        public void Registered_RoutesThatWeapon_KeepsOthersDefault()
        {
            OperationModuleRegistry.Register(WeaponId.Cannon, OperationModuleKind.LineLaunch);
            OperationModuleRegistry.Register(WeaponId.GunpowderBarrel, OperationModuleKind.Place);
            Assert.AreEqual(2, OperationModuleRegistry.Count);

            Assert.AreEqual(OperationModuleKind.LineLaunch,
                OperationModuleRegistry.ModuleFor(WeaponId.Cannon));
            Assert.AreEqual(OperationModuleKind.Place,
                OperationModuleRegistry.ModuleFor(WeaponId.GunpowderBarrel));
            Assert.AreEqual(OperationModuleKind.StandardThrow,
                OperationModuleRegistry.ModuleFor(WeaponId.CherryBomb));
        }

        [Test]
        public void Cleared_FallsBackToStandardThrow()
        {
            OperationModuleRegistry.Register(WeaponId.WoodenCrate, OperationModuleKind.Place);
            OperationModuleRegistry.Clear();
            Assert.AreEqual(OperationModuleKind.StandardThrow,
                OperationModuleRegistry.ModuleFor(WeaponId.WoodenCrate));
        }

        // ------------------------------------------------------------------
        // 放置模组参数（骨架桩：初始占位点）
        // ------------------------------------------------------------------

        [Test]
        public void PlaceModule_Initial_IsInFrontOfUnitAlongYaw()
        {
            // yaw=0 → +Z 方向前方：z + 距离。
            PlaceParams p = PlaceModuleRules.Initial(new Vector3(1f, 0f, 2f), 0f);
            Assert.AreEqual(new Vector3(1f, 0f, 2f + PlaceModuleRules.PlaceholderSpawnDistance),
                p.TargetPosition);

            // yaw=90 → +X 方向前方：x + 距离。
            PlaceParams q = PlaceModuleRules.Initial(new Vector3(1f, 0f, 2f), 90f);
            float eps = 1e-4f;
            Assert.AreEqual(1f + PlaceModuleRules.PlaceholderSpawnDistance, q.TargetPosition.x, eps);
            Assert.AreEqual(2f, q.TargetPosition.z, eps);
        }

        // ------------------------------------------------------------------
        // 直线发射模组参数（占位键位推进 + 恒水平初速）
        // ------------------------------------------------------------------

        [Test]
        public void LineLaunch_Initial_TakesYawAndDefaultPower()
        {
            LineLaunchParams p = LineLaunchModuleRules.Initial(37f);
            Assert.AreEqual(37f, p.YawDegrees, 1e-4f);
            Assert.AreEqual(LineLaunchModuleRules.DefaultPower, p.Power, 1e-4f);
        }

        [Test]
        public void LineLaunch_Advance_ClampsPower_AndLeavesYawUnbounded()
        {
            LineLaunchParams p = LineLaunchModuleRules.Initial(0f);

            // 蓄力推进 1 秒（0.45/s）：0.60 → 1.0（夹到上限）。
            p = LineLaunchModuleRules.Advance(p, yawInput: 0, powerInput: +1, dt: 1f);
            Assert.AreEqual(LineLaunchModuleRules.MaxPower, p.Power, 1e-4f);

            // 降蓄力 10 秒：夹到下限（不产生零发射）。
            p = LineLaunchModuleRules.Advance(p, yawInput: 0, powerInput: -1, dt: 10f);
            Assert.AreEqual(LineLaunchModuleRules.MinPower, p.Power, 1e-4f);

            // 方向角全周无界推进。
            p = LineLaunchModuleRules.Advance(p, yawInput: +1, powerInput: 0, dt: 2f);
            Assert.AreEqual(2f * LineLaunchModuleRules.YawRateDegreesPerSecond, p.YawDegrees, 1e-3f);
        }

        [Test]
        public void LineLaunch_LaunchDirection_IsHorizontal_AndMatchesYawConvention()
        {
            // yaw 约定与 StandardThrowRules 相同：0 → +Z、90 → +X；直线发射恒水平（y=0）。
            Vector3 forward = LineLaunchModuleRules.LaunchDirection(0f);
            Assert.AreEqual(0f, forward.x, 1e-4f);
            Assert.AreEqual(0f, forward.y, 1e-4f);
            Assert.AreEqual(1f, forward.z, 1e-4f);

            Vector3 right = LineLaunchModuleRules.LaunchDirection(90f);
            Assert.AreEqual(1f, right.x, 1e-4f);
            Assert.AreEqual(0f, right.y, 1e-4f);
            Assert.AreEqual(0f, right.z, 1e-4f);
        }

        [Test]
        public void LineLaunch_LaunchVelocity_IsDirectionTimesClampedPowerTimesSpeed()
        {
            LineLaunchParams p = LineLaunchModuleRules.Initial(0f);
            Vector3 v = LineLaunchModuleRules.LaunchVelocity(p);
            Assert.AreEqual(0f, v.x, 1e-3f);
            Assert.AreEqual(0f, v.y, 1e-3f);
            Assert.AreEqual(LineLaunchModuleRules.DefaultPower * LineLaunchModuleRules.PlaceholderLaunchSpeed,
                v.z, 1e-3f);
        }

        // ------------------------------------------------------------------
        // 载荷兼容：模组字段默认标准投掷（旧构造点不破）
        // ------------------------------------------------------------------

        [Test]
        public void OperationChangedPayload_ModuleDefaultsToStandardThrow()
        {
            var legacy = new OperationChangedPayload(1, true, WeaponId.CherryBomb, true);
            Assert.AreEqual(OperationModuleKind.StandardThrow, legacy.Module);

            var explicitModule = new OperationChangedPayload(
                1, true, WeaponId.Cannon, true, OperationModuleKind.LineLaunch);
            Assert.AreEqual(OperationModuleKind.LineLaunch, explicitModule.Module);
        }
    }
}
