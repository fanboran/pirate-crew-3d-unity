using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileSpawnPlanner"/> 测试（B9 审计回填，49dc3288 误删后按当前生产代码适配）。
    ///
    /// 【适配说明】单一弹体收敛后规划退化为「纯透传」：一次使用恒产 1 条、位置/速度原样透传、
    /// 恒非 kinematic——旧版的每武器分叉（放置多弹、anchor 空投、seagull/tidalWave 入场位、
    /// Flash 速度换算）已随武器系统空壳化删除，不再有对应生产语义可断言。
    /// 「预览 = 实弹」由调用方传同一初速保证（同源约束），本测试钉的就是透传不变形。
    /// </summary>
    [TestFixture]
    public class ProjectileSpawnPlannerTests
    {
        static readonly Vector3 Origin = new Vector3(1f, 2f, 3f);
        static readonly Vector3 Velocity = new Vector3(4f, 5f, 6f);

        [Test]
        public void Plan_ProducesExactlyOneSpawn()
        {
            // 「点哪把都是它」：任意武器的一次使用都只产 1 条通用抛掷弹。
            Assert.AreEqual(1, ProjectileSpawnPlanner.Plan(Origin, Velocity).Count);
        }

        [Test]
        public void Plan_PassesThroughOriginAndVelocityVerbatim()
        {
            ProjectileSpawn spawn = ProjectileSpawnPlanner.Plan(Origin, Velocity)[0];
            Assert.AreEqual(Origin, spawn.WorldPosition, "生成点应原样透传（不另加偏移）");
            Assert.AreEqual(Velocity, spawn.WorldVelocity, "初速应原样透传（预览=实弹同源）");
        }

        [Test]
        public void Plan_IsNeverKinematic()
        {
            // 单一弹体路径没有常驻静置弹（放置机制收敛为通用抛掷）。
            Assert.IsFalse(ProjectileSpawnPlanner.Plan(Origin, Velocity)[0].Kinematic);
        }

        [Test]
        public void Plan_IsIndependentOfWeaponStats()
        {
            // 旧签名里 stats 只是数据锚：不同武器、同一 origin/velocity → 计划逐位相同。
            ProjectileSpawn a = ProjectileSpawnPlanner.Plan(
                WeaponCatalog.Get(WeaponId.CherryBomb), Origin, Velocity)[0];
            ProjectileSpawn b = ProjectileSpawnPlanner.Plan(
                WeaponCatalog.Get(WeaponId.Cannonball), Origin, Velocity)[0];
            Assert.AreEqual(a.WorldPosition, b.WorldPosition);
            Assert.AreEqual(a.WorldVelocity, b.WorldVelocity);
            Assert.AreEqual(a.Kinematic, b.Kinematic);
        }

        [Test]
        public void Plan_ZeroVelocityPassesThrough()
        {
            // 零初速原样透传（不再有旧弹弓链的 twangMax 特判分支）。
            ProjectileSpawn spawn = ProjectileSpawnPlanner.Plan(Origin, Vector3.zero)[0];
            Assert.AreEqual(Vector3.zero, spawn.WorldVelocity);
            Assert.AreEqual(Origin, spawn.WorldPosition);
        }
    }
}
