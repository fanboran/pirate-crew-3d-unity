using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="LevelGeometry"/> 测试（3D 重投影版，契约见 docs/M2-3D空间模型对齐.md）。
    /// 覆盖：px→单位换算、px/py→XZ 水平面、水位常量、速度/重力换算、投掷抬升、
    /// 相机基向量拖拽映射，以及"预览=实弹"的 3D 半隐式欧拉等价性（§3/§5.4 的 3D 化决策）。
    /// </summary>
    [TestFixture]
    public class LevelGeometryTests
    {
        // ------------------------------------------------------------------
        // px → 单位 / px、py → XZ 平面
        // ------------------------------------------------------------------

        [Test]
        public void PixelsToUnits_ThirtyTwoPixels_IsOneUnit()
        {
            // 决策 1：1 瓦片 = 32px = 1 单位。
            Assert.AreEqual(1f, LevelGeometry.PixelsToUnits(32f), 1e-6f);
        }

        [Test]
        public void PixelToArena_MapsPxToWorldXZ()
        {
            // 3D 重投影：Flash 的 px → 世界 X，py → 世界 Z（纵深，**不取负**），高度恒为地面顶面 0。
            // (64px, 96px) → (64/32, 0, 96/32) = (2, 0, 3)
            Vector3 world = LevelGeometry.PixelToArena(64f, 96f);
            Assert.AreEqual(2f, world.x, 1e-6f);
            Assert.AreEqual(0f, world.y, 1e-6f);
            Assert.AreEqual(3f, world.z, 1e-6f);
        }

        [Test]
        public void GridToArena_MatchesSection4_3Formula()
        {
            // §4.3：世界 X = gridX + 0.5、Z = gridY + 0.5、Y = 地面 + UnitPivotHeight（脚底贴地）。
            // gridX=17 → 17.5；gridY=10 → 10.5；Y = 0 + 0.25 = 0.25。
            Vector3 world = LevelGeometry.GridToArena(17, 10);
            Assert.AreEqual(17.5f, world.x, 1e-5f);
            Assert.AreEqual(0.25f, world.y, 1e-5f);
            Assert.AreEqual(10.5f, world.z, 1e-5f);

            // UnitPivotHeight = PixelsToUnits(16 - BottomExtent=8) = 8/32 = 0.25。
            Assert.AreEqual(0.25f, LevelGeometry.UnitPivotHeight, 1e-5f);
        }

        [Test]
        public void ArenaToPixel_IsInverseOfPixelToArena_AndIgnoresHeight()
        {
            // (560px, 344px) → (17.5, 0, 10.75) → 回读仍是 (560, 344)。
            Vector3 world = LevelGeometry.PixelToArena(560f, 344f);
            Vector2 px = LevelGeometry.ArenaToPixel(world);
            Assert.AreEqual(560f, px.x, 1e-4f);
            Assert.AreEqual(344f, px.y, 1e-4f);

            // ArenaToPixel 只看平面分量：高度 y 不参与（爆炸的 3D 距离另行按高度处理）。
            Vector2 planar = LevelGeometry.ArenaToPixel(new Vector3(2f, 123f, 3f));
            Assert.AreEqual(64f, planar.x, 1e-4f);
            Assert.AreEqual(96f, planar.y, 1e-4f);
        }

        // ------------------------------------------------------------------
        // 水位（§4.4 / §5.5）
        // ------------------------------------------------------------------

        [Test]
        public void WaterSurfaceY_IsGlobalMinusPointTwo()
        {
            // §4.4 3D 化：水面改为全局常量 WaterSurfaceY = -0.2，不再由关卡 waterTileY 推出。
            Assert.AreEqual(-0.2f, LevelGeometry.WaterSurfaceY, 1e-5f);
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
        public void FlashSpeedScale_IsZeroPoint78125()
        {
            // 1 / (32 * 0.04) = 1 / 1.28 = 0.78125
            Assert.AreEqual(0.78125f, LevelGeometry.FlashSpeedScale, 1e-7f);
        }

        [Test]
        public void WorldGravityY_WeightOne_IsMinus19Point53125()
        {
            // -1 / (32 * 0.04^2) = -1 / 0.0512 = -19.53125；重力沿 -Y，与水平面正交。
            Assert.AreEqual(-19.53125f, LevelGeometry.WorldGravityY(1f), 1e-5f);
        }

        [Test]
        public void FlashVelocityToArena_MapsPlanarVelocityToXZ()
        {
            // 3D 重投影：Flash 平面速度 (vx, vy) → 世界 (X, Z)，高度分量恒为 0
            // （仰角由 ApplyThrowLift 提供，不由 Flash 平面速度提供）。
            // vx=4 → 4*0.78125 = 3.125；vy=10 → 10*0.78125 = 7.8125。
            Vector3 v = LevelGeometry.FlashVelocityToArena(4f, 10f);
            Assert.AreEqual(3.125f, v.x, 1e-5f);
            Assert.AreEqual(0f, v.y, 1e-5f);
            Assert.AreEqual(7.8125f, v.z, 1e-5f);
        }

        [Test]
        public void FlashVelocityDeltaToArena_MapsToPlaneComponents()
        {
            // 击退/爆炸的速度增量同样落在平面：Flash (dvx, dvy) → 世界 (X, Z)，y = 0。
            // §5 的 3D 化：原先塞在 vy 里的 -6k 抬升项改由 ExplosionResolver 的三维泛化
            // 作为独立的 +Y 输出（见 docs/M2-3D空间模型对齐.md §5），本函数只管平面两分量。
            // dvx=2 → 1.5625；dvy=-6 → -4.6875。
            Vector3 dv = LevelGeometry.FlashVelocityDeltaToArena(2f, -6f);
            Assert.AreEqual(1.5625f, dv.x, 1e-5f);
            Assert.AreEqual(0f, dv.y, 1e-6f);
            Assert.AreEqual(-4.6875f, dv.z, 1e-5f);
        }

        // ------------------------------------------------------------------
        // 投掷抬升（§3 决策 3）
        // ------------------------------------------------------------------

        [Test]
        public void ApplyThrowLift_AddsFixedElevation_AndKeepsUnitLength()
        {
            // throwDir = normalize(flatDir + UP * ThrowLift)，ThrowLift = 0.7。
            // 仰角 = atan2(0.7, 1) = atan(0.7) ≈ 35°，方向仍为单位向量。
            Vector3 dir = LevelGeometry.ApplyThrowLift(Vector3.forward);

            Assert.AreEqual(1f, dir.magnitude, 1e-5f, "抬升只改方向，结果必须已归一化");
            Assert.AreEqual(Mathf.Atan(LevelGeometry.ThrowLift),
                Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude), 1e-5f);
            Assert.AreEqual(0f, dir.x, 1e-6f);
            Assert.Greater(dir.y, 0f, "抬升后竖直分量必须向上（+Y）");

            // 斜向水平输入：水平朝向不变（x:z 比例保持），仰角不变。
            Vector3 diagonal = LevelGeometry.ApplyThrowLift(new Vector3(1f, 0f, 1f));
            Assert.AreEqual(1f, diagonal.magnitude, 1e-5f);
            Assert.AreEqual(diagonal.x, diagonal.z, 1e-6f);
            Assert.AreEqual(dir.y / Mathf.Sqrt(1f - dir.y * dir.y),
                diagonal.y / Mathf.Sqrt(1f - diagonal.y * diagonal.y), 1e-5f);

            // 零输入返回零，不产生 NaN。
            Assert.AreEqual(0f, LevelGeometry.ApplyThrowLift(Vector3.zero).sqrMagnitude, 1e-6f);
        }

        [Test]
        public void ThrowVelocity_LiftDoesNotChangeSpeedMagnitude()
        {
            // 速度**大小**由 Flash 的 twang 结果决定；抬升只改仰角 → 限速语义不被破坏。
            const float speedPx = 12f;
            float expected = speedPx * LevelGeometry.FlashSpeedScale;

            Vector3 forward = LevelGeometry.ThrowVelocity(Vector3.forward, speedPx);
            Vector3 diagonal = LevelGeometry.ThrowVelocity(new Vector3(1f, 0f, 1f), speedPx);

            Assert.AreEqual(expected, forward.magnitude, 1e-5f);
            Assert.AreEqual(forward.magnitude, diagonal.magnitude, 1e-5f,
                "不同水平朝向的抬升投掷必须模长相同");
            Assert.AreEqual(expected, LevelGeometry.FlashVelocityToArena(speedPx, 0f).magnitude, 1e-5f,
                "抬升不应改变与无抬升平面速度相同的模长");
        }

        [Test]
        public void FlashLaunchVelocityToWorld_MagnitudeIsFlashSpeedScaled()
        {
            // 统一入口：Flash 平面初速 (vx, vy) → 3D 世界初速（含抬升）。
            // 模长 = √(vx²+vy²) × FlashSpeedScale = √(vx²+vy²) / 1.28（与 ThrowVelocity 同源）。
            const float vx = 4f, vy = 10f;
            float speed = Mathf.Sqrt(vx * vx + vy * vy);

            Vector3 v = LevelGeometry.FlashLaunchVelocityToWorld(vx, vy);

            Assert.AreEqual(speed * LevelGeometry.FlashSpeedScale, v.magnitude, 1e-5f);
            Assert.AreEqual(speed / 1.28f, v.magnitude, 1e-4f);
            Assert.Greater(v.y, 0f, "含抬升的初速必须有向上的竖直分量");

            // 水平朝向 = Flash 平面速度 (vx, vy) 的方向；仰角 = atan(ThrowLift)。
            float horizontal = new Vector2(v.x, v.z).magnitude;
            Assert.AreEqual(vx / speed, v.x / horizontal, 1e-5f);
            Assert.AreEqual(vy / speed, v.z / horizontal, 1e-5f);
            Assert.AreEqual(Mathf.Atan(LevelGeometry.ThrowLift),
                Mathf.Atan2(v.y, horizontal), 1e-5f);

            // 零速度返回零，不产生 NaN。
            Assert.AreEqual(0f, LevelGeometry.FlashLaunchVelocityToWorld(0f, 0f).sqrMagnitude, 1e-6f);
        }

        // ------------------------------------------------------------------
        // 屏幕拖拽 → 世界水平方向（§3 决策：用相机基向量投影）
        // ------------------------------------------------------------------

        [Test]
        public void ScreenDragToArenaDirection_YawZero_EqualsMinusDrag()
        {
            // yaw=0 且相机水平朝向 +Z 时：right=(1,0,0)、forward=(0,0,1)，
            // 直接瞄准语义：horiz = right*dx + forward*dy → (0.6, 0, 0.8)（拖向哪扔向哪，r12 用户裁决）。
            Vector3 dir = LevelGeometry.ScreenDragToArenaDirection(
                new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), 3f, 4f);

            Assert.AreEqual(0.6f, dir.x, 1e-5f);
            Assert.AreEqual(0f, dir.y, 1e-6f);
            Assert.AreEqual(0.8f, dir.z, 1e-5f);

            // 零拖拽返回零，不产生 NaN。
            Assert.AreEqual(0f, LevelGeometry.ScreenDragToArenaDirection(
                new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), 0f, 0f).sqrMagnitude, 1e-6f);
        }

        [Test]
        public void ScreenDragToArenaDirection_YawNinety_RotatesWithCamera()
        {
            // yaw=90°：right=(0,0,-1)、forward=(1,0,0)（绕 Y 旋转 90°）。
            // 同一拖拽 (3,4) → horiz = right*3 + forward*4 = (4, 0, -3) → (0.8, 0, -0.6)。
            Vector3 dir = LevelGeometry.ScreenDragToArenaDirection(
                new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 0f), 3f, 4f);

            Assert.AreEqual(0.8f, dir.x, 1e-5f);
            Assert.AreEqual(0f, dir.y, 1e-6f);
            Assert.AreEqual(-0.6f, dir.z, 1e-5f);

            // 与 yaw=0 的同一拖拽方向不同 → 映射确实随相机环绕而旋转（而非硬编码）。
            Vector3 yaw0 = LevelGeometry.ScreenDragToArenaDirection(
                new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), 3f, 4f);
            Assert.AreNotEqual(yaw0, dir, "相机 yaw 变化后映射必须随之旋转");
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
            // 用真实物理常数（dt = FrameSeconds = 1/25、g = WorldGravityY(1) = -19.53125）
            // 验证"预览 = 实弹"：重力只作用在 Y，水平面（XZ）匀速。
            // g*dt = -0.78125 → 手算：
            //   第 1 步：vy=-0.78125 → y=-0.03125；第 2 步：vy=-1.5625 → y=-0.09375；
            //   第 3 步：vy=-2.34375 → y=-0.1875。x 每步 +10*0.04=0.4，z 恒为 0。
            var buffer = new Vector3[3];
            ThrowTrajectory.Predict(
                Vector3.zero, new Vector3(10f, 0f, 0f), LevelGeometry.WorldGravityY(1f),
                buffer, 3, LevelGeometry.FrameSeconds);

            float[] expectedY = { -0.03125f, -0.09375f, -0.1875f };
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
            // 30 / 32 = 0.9375
            Assert.AreEqual(0.9375f, LevelGeometry.SelectionRadiusWorld, 1e-6f);
        }
    }
}
