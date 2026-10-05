using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="LevelGeometry"/> 测试（3D 重投影版，契约见 docs/技术/3D空间模型对齐.md）。
    /// 覆盖：px→单位换算、px/py→XZ 水平面、水位常量、武器/爆炸域的 px 速度增量换算，
    /// 以及"预览=实弹"的 3D 半隐式欧拉等价性。投掷链已米制化（初速/重力单源
    /// StandardThrowRules，见 StandardThrowRulesTests），旧投掷换算测试随 API 退役删除。
    /// </summary>
    [TestFixture]
    public class LevelGeometryTests
    {
        // ------------------------------------------------------------------
        // px → 单位 / px、py → XZ 平面
        // ------------------------------------------------------------------

        [Test]
        public void PixelsToUnits_ThirtyTwoPixels_IsTwoUnits()
        {
            // 决策 1：1 瓦片 = 32px；格 1→2 单位后 1 瓦片 = 2 世界单位（LevelGeometry.TileWorldSize=2，
            // PixelsPerUnit = 32/2 = 16）。故 32px = 32/16 = 2 单位。
            Assert.AreEqual(2f, LevelGeometry.PixelsToUnits(32f), 1e-6f);
        }

        [Test]
        public void PixelToArena_MapsPxToWorldXZ()
        {
            // 3D 重投影：Flash 的 px → 世界 X，py → 世界 Z（纵深，**不取负**），高度恒为地面顶面 0。
            // (64px, 96px) → (64/16, 0, 96/16) = (4, 0, 6)。
            Vector3 world = LevelGeometry.PixelToArena(64f, 96f);
            Assert.AreEqual(4f, world.x, 1e-6f);
            Assert.AreEqual(0f, world.y, 1e-6f);
            Assert.AreEqual(6f, world.z, 1e-6f);
        }

        [Test]
        public void GridToArena_MatchesSection4_3Formula()
        {
            // §4.3：世界 X = (gridX + 0.5) × TileWorldSize(2)、Z 同理、Y = 地面 + UnitPivotHeight（脚底贴地）。
            // gridX=17 → 17.5×2 = 35；gridY=10 → 10.5×2 = 21；Y = 0 + 0.5 = 0.5。
            Vector3 world = LevelGeometry.GridToArena(17, 10);
            Assert.AreEqual(35f, world.x, 1e-5f);
            Assert.AreEqual(0.5f, world.y, 1e-5f);
            Assert.AreEqual(21f, world.z, 1e-5f);

            // UnitPivotHeight = PixelsToUnits(16 - BottomExtent=8) = 8/16 = 0.5（半格内偏移随格世界尺寸放大）。
            Assert.AreEqual(0.5f, LevelGeometry.UnitPivotHeight, 1e-5f);
        }

        [Test]
        public void ArenaToPixel_IsInverseOfPixelToArena_AndIgnoresHeight()
        {
            // (560px, 344px) → (35, 0, 21.5) → 回读仍是 (560, 344)。
            Vector3 world = LevelGeometry.PixelToArena(560f, 344f);
            Vector2 px = LevelGeometry.ArenaToPixel(world);
            Assert.AreEqual(560f, px.x, 1e-4f);
            Assert.AreEqual(344f, px.y, 1e-4f);

            // ArenaToPixel 只看平面分量：高度 y 不参与（爆炸的 3D 距离另行按高度处理）。
            // (2, 123, 3) → (2×16, 3×16) = (32, 48)。
            Vector2 planar = LevelGeometry.ArenaToPixel(new Vector3(2f, 123f, 3f));
            Assert.AreEqual(32f, planar.x, 1e-4f);
            Assert.AreEqual(48f, planar.y, 1e-4f);
        }

        // ------------------------------------------------------------------
        // 水位（§4.4 / §5.5）
        // ------------------------------------------------------------------

        [Test]
        public void WaterSurfaceY_IsGlobalMinusPointFour()
        {
            // §4.4 3D 化：水面改为全局常量 WaterSurfaceY = -0.4（比地面低 0.4 单位 = 6.4px，
            // 格 1→2 单位后由 -0.2 乘 2；LevelGeometry.cs:197），不再由关卡 waterTileY 推出。
            Assert.AreEqual(-0.4f, LevelGeometry.WaterSurfaceY, 1e-5f);
        }

        [Test]
        public void IsBelowWater_SmallerWorldY_IsTrue()
        {
            // Unity y 向上：世界 y 比水面小 = 在水下（严格小于；等高视为未落水）。
            float water = LevelGeometry.WaterSurfaceY;
            Assert.IsTrue(LevelGeometry.IsBelowWater(water - 0.1f, water));
            Assert.IsFalse(LevelGeometry.IsBelowWater(water + 0.1f, water));
            Assert.IsFalse(LevelGeometry.IsBelowWater(water, water));
        }

        // ------------------------------------------------------------------
        // 速度 / 重力换算（§5.1 / §5.4）
        // ------------------------------------------------------------------

        [Test]
        public void FlashSpeedScale_IsOnePoint5625()
        {
            // 1 / (16 * 0.04) = 1 / 0.64 = 1.5625（格 1→2 单位后 PixelsPerUnit 32→16，比例由 0.78125 翻倍）。
            Assert.AreEqual(1.5625f, LevelGeometry.FlashSpeedScale, 1e-7f);
        }

        [Test]
        public void FlashVelocityDeltaToArena_MapsToPlaneComponents()
        {
            // 击退/爆炸的速度增量同样落在平面：Flash (dvx, dvy) → 世界 (X, Z)，y = 0。
            // §5 的 3D 化：原先塞在 vy 里的 -6k 抬升项改由 ExplosionResolver 的三维泛化
            // 作为独立的 +Y 输出（见 docs/3D空间模型对齐.md §5），本函数只管平面两分量。
            // dvx=2 → 3.125；dvy=-6 → -9.375。
            Vector3 dv = LevelGeometry.FlashVelocityDeltaToArena(2f, -6f);
            Assert.AreEqual(3.125f, dv.x, 1e-5f);
            Assert.AreEqual(0f, dv.y, 1e-6f);
            Assert.AreEqual(-9.375f, dv.z, 1e-5f);
        }

        // ------------------------------------------------------------------
        // 预览 = 实弹（3D 半隐式欧拉等价性）
        // ------------------------------------------------------------------

        [Test]
        public void ThrowTrajectory_Predict_MatchesHandComputedSemiImplicitEuler()
        {
            // 命题：ThrowTrajectory.Predict 与 PhysX 的半隐式欧拉（v += g·dt; p += v·dt）
            // 逐步严格相等。用整数便于手算：g = -10、dt = 0.5、v0 = (4, 5, 6)、p0 = (1, 2, 3)。
            // 第 1 步：v=(4, 0, 6)    → p=(3, 2, 6)
            // 第 2 步：v=(4,-5, 6)    → p=(5,-0.5, 9)
            // 第 3 步：v=(4,-10,6)    → p=(7,-5.5, 12)
            var buffer = new Vector3[3];
            ThrowTrajectory.Predict(
                new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), -10f,
                buffer, 3, 0.5f);

            Assert.AreEqual(3f, buffer[0].x, 1e-5f);
            Assert.AreEqual(2f, buffer[0].y, 1e-5f);
            Assert.AreEqual(6f, buffer[0].z, 1e-5f);

            Assert.AreEqual(5f, buffer[1].x, 1e-5f);
            Assert.AreEqual(-0.5f, buffer[1].y, 1e-5f);
            Assert.AreEqual(9f, buffer[1].z, 1e-5f);

            Assert.AreEqual(7f, buffer[2].x, 1e-5f);
            Assert.AreEqual(-5.5f, buffer[2].y, 1e-5f);
            Assert.AreEqual(12f, buffer[2].z, 1e-5f);
        }

        [Test]
        public void ThrowTrajectory_Predict_WithPhysXFrame_OnlyGravityActsOnY()
        {
            // 用真实物理常数（dt = FrameSeconds = 1/25、g = StandardThrowRules.LaunchGravityY = -30）
            // 验证"预览 = 实弹"：重力只作用在 Y，水平面（XZ）匀速。
            // g*dt = -1.2 → 手算：
            //   第 1 步：vy=-1.2  → y=-0.048；第 2 步：vy=-2.4 → y=-0.144；
            //   第 3 步：vy=-3.6 → y=-0.288。x 每步 +10*0.04=0.4，z 恒为 0。
            var buffer = new Vector3[3];
            ThrowTrajectory.Predict(
                Vector3.zero, new Vector3(10f, 0f, 0f), StandardThrowRules.LaunchGravityY,
                buffer, 3, LevelGeometry.FrameSeconds);

            float[] expectedY = { -0.048f, -0.144f, -0.288f };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(10f * LevelGeometry.FrameSeconds * (i + 1), buffer[i].x, 1e-5f,
                    "第 " + (i + 1) + " 步 x 应为水平匀速");
                Assert.AreEqual(expectedY[i], buffer[i].y, 1e-5f,
                    "第 " + (i + 1) + " 步 y 应与手算半隐式欧拉一致");
                Assert.AreEqual(0f, buffer[i].z, 1e-5f,
                    "第 " + (i + 1) + " 步 z 不受重力影响（重力沿 -Y，与水平面正交）");
            }
        }

        [Test]
        public void SelectionRadiusWorld_IsThirtyPixelsInUnits()
        {
            // 30 / 16 = 1.875（屏幕空间 30px 阈值不变，换算除数随 PixelsPerUnit 32→16 改）。
            Assert.AreEqual(1.875f, LevelGeometry.SelectionRadiusWorld, 1e-6f);
        }
    }
}
