using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Combat;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="WeaponProjectile"/> 纯函数口径测试（B9 审计回填，49dc3288 误删后按当前生产代码适配）。
    ///
    /// 【适配说明】原文件的「火焰击退方位轴投影」（FlameKnockbackWorldDelta）已随武器系统
    /// 空壳化删除（sweepingFlame 机制退役），无对应生产代码可断言；现生产代码的 Flash 帧口径
    /// 纯函数是 <see cref="WeaponProjectile.FlashRestComponents"/>（世界速度 → Flash (vx, vy)），
    /// 本文件承载它的全部口径断言（原寄生在 ProjectileTriggerRulesTests 里，按文件名语义归位）。
    /// 逐帧计数的 FixedUpdate 化属结构性修复（Update/FixedUpdate 的迁移无法在 EditMode 断言），
    /// 其数值口径由各 *Rules 测试钉住。
    /// </summary>
    [TestFixture]
    public class WeaponProjectileFlashFrameTests
    {
        // ------------------------------------------------------------------
        // 世界速度 → Flash (vx, vy) 口径：
        //   vx = XZ 平面速度模长（Flash 的横向"是否在动"，3D 两个水平轴任一有速度都算在动）
        //   vy = 世界 Y 速度 / FlashSpeedScale（Flash 的 vy 是重力轴分量，3D 重力沿 -Y）
        // ------------------------------------------------------------------

        [TestCase(0f, -5f, 0f)]   // 竖直下落（重力轴）
        [TestCase(0f, 5f, 0f)]    // 竖直上抛
        public void RestComponents_3D_VerticalMotionIsNotAtRest(float x, float y, float z)
        {
            // 竖直方向的运动必须由 vy 体现：若把平面重投影的 Z 当 vy，竖直下落会被读成 (0,0) →
            // 弹体在半空误判静止引爆。这也是文档"落水下沉不静止"的同一条口径。
            WeaponProjectile.FlashRestComponents(new Vector3(x, y, z), out float vx, out float vy);
            Assert.AreEqual(0f, vx, 1e-6f);
            Assert.AreEqual(y / LevelGeometry.FlashSpeedScale, vy, 1e-5f);
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(vx, vy));
        }

        [Test]
        public void RestComponents_3D_HorizontalDriftOnEitherAxisIsNotAtRest()
        {
            // X 或 Z 任一方向在动 → 平面模长非零 → 不静止（只读 world.x 会漏掉沿 Z 的滑行）。
            WeaponProjectile.FlashRestComponents(new Vector3(0f, 0f, 0.5f), out float vxZ, out float vyZ);
            Assert.Greater(vxZ, 0f);
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(vxZ, vyZ));

            WeaponProjectile.FlashRestComponents(new Vector3(0.5f, 0f, 0f), out float vxX, out float vyX);
            Assert.Greater(vxX, 0f);
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(vxX, vyX));
        }

        [Test]
        public void RestComponents_3D_PlaneMagnitudeUsesBothHorizontalAxes()
        {
            // 世界 (3, 0, 4) 单位/秒 → XZ 模长 5 → Flash 5 / FlashSpeedScale px/帧；Y 不参与平面模长。
            WeaponProjectile.FlashRestComponents(new Vector3(3f, 0f, 4f), out float vx, out float vy);
            Assert.AreEqual(5f / LevelGeometry.FlashSpeedScale, vx, 1e-5f);
            Assert.AreEqual(0f, vy, 1e-6f);
        }

        [Test]
        public void RestComponents_3D_RestingOnGroundIsAtRest()
        {
            WeaponProjectile.FlashRestComponents(Vector3.zero, out float vx, out float vy);
            Assert.AreEqual(0f, vx, 1e-6f);
            Assert.AreEqual(0f, vy, 1e-6f);
            Assert.IsTrue(WeaponTriggerRules.IsAtRest(vx, vy));
        }

        [Test]
        public void RestComponents_ScaleIsLinear()
        {
            // 平面模长对世界速度线性（同向放大 k 倍 → vx 放大 k 倍）、vy 与 world.y 线性。
            WeaponProjectile.FlashRestComponents(new Vector3(2f, -8f, 1f), out float vx1, out float vy1);
            WeaponProjectile.FlashRestComponents(new Vector3(6f, -24f, 3f), out float vx3, out float vy3);
            Assert.AreEqual(3f * vx1, vx3, 1e-4f);
            Assert.AreEqual(3f * vy1, vy3, 1e-4f);
        }

        [Test]
        public void RestComponents_NegativeVerticalSpeed_KeepsSign()
        {
            // 下落（vy<0）与上抛（vy>0）符号保留：静止判据只看 |vy|<0.2，符号不丢才能区分触地瞬间。
            WeaponProjectile.FlashRestComponents(new Vector3(0f, -1f, 0f), out float vxDn, out float vyDn);
            WeaponProjectile.FlashRestComponents(new Vector3(0f, 1f, 0f), out float vxUp, out float vyUp);
            Assert.Less(vyDn, 0f);
            Assert.Greater(vyUp, 0f);
            Assert.AreEqual(0f, vxDn, 1e-6f);
            Assert.AreEqual(0f, vxUp, 1e-6f);
        }
    }
}
