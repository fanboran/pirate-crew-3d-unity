using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="LevelGeometry"/> 测试。
    /// 覆盖：px→单位换算、y 轴翻转、出水计划坐标、水位判定、速度/重力换算，
    /// 以及"预览=实弹"的离散积分等价性（§5.1/§5.4 的 3D 化决策）。
    /// </summary>
    [TestFixture]
    public class LevelGeometryTests
    {
        // ------------------------------------------------------------------
        // px → 单位 / y 轴翻转
        // ------------------------------------------------------------------

        [Test]
        public void PixelsToUnits_ThirtyTwoPixels_IsOneUnit()
        {
            // 决策 1：1 瓦片 = 32px = 1 单位。
            Assert.AreEqual(1f, LevelGeometry.PixelsToUnits(32f), 1e-6f);
        }

        [Test]
        public void PixelToWorld_FlipsYAxis()
        {
            // (64px, 96px) → (64/32, -96/32) = (2, -3, 0)
            Vector3 world = LevelGeometry.PixelToWorld(64f, 96f);
            Assert.AreEqual(2f, world.x, 1e-6f);
            Assert.AreEqual(-3f, world.y, 1e-6f);
            Assert.AreEqual(0f, world.z, 1e-6f);
        }

        [Test]
        public void GridToWorld_MatchesSection4_3Formula()
        {
            // gridX=17 → px=(17+0.5)*32=560 → worldX=560/32=17.5
            // gridY=10 → py=(10+0.5)*32+16-8=344 → worldY=-344/32=-10.75
            Vector3 world = LevelGeometry.GridToWorld(17, 10);
            Assert.AreEqual(17.5f, world.x, 1e-5f);
            Assert.AreEqual(-10.75f, world.y, 1e-5f);
        }

        [Test]
        public void WorldToPixel_IsInverseOfPixelToWorld()
        {
            Vector3 world = LevelGeometry.PixelToWorld(560f, 344f);
            Vector2 px = LevelGeometry.WorldToPixel(world);
            Assert.AreEqual(560f, px.x, 1e-4f);
            Assert.AreEqual(344f, px.y, 1e-4f);
        }

        // ------------------------------------------------------------------
        // 水位（§4.4 / §5.5）
        // ------------------------------------------------------------------

        [Test]
        public void WaterWorldY_Level1WaterTile14_IsMinusFourteen()
        {
            // §5.5: waterY_px = 14*32 = 448 → worldY = -448/32 = -14
            Assert.AreEqual(-14f, LevelGeometry.WaterWorldY(448f), 1e-5f);
        }

        [Test]
        public void IsBelowWater_SmallerWorldY_IsTrue()
        {
            // Unity y 向上：世界 y 比水面小 = 在水下。
            Assert.IsTrue(LevelGeometry.IsBelowWater(-14.5f, -14f));
            Assert.IsFalse(LevelGeometry.IsBelowWater(-13.9f, -14f));
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
            // -1 / (32 * 0.04^2) = -1 / 0.0512 = -19.53125
            Assert.AreEqual(-19.53125f, LevelGeometry.WorldGravityY(1f), 1e-5f);
        }

        [Test]
        public void FlashVelocityToWorld_ScalesAndFlipsY()
        {
            // vx=4 → 4*0.78125 = 3.125；vy=10 → -10*0.78125 = -7.8125
            Vector3 v = LevelGeometry.FlashVelocityToWorld(4f, 10f);
            Assert.AreEqual(3.125f, v.x, 1e-5f);
            Assert.AreEqual(-7.8125f, v.y, 1e-5f);
        }

        [Test]
        public void FlashVelocityDeltaToWorld_MinusSixK_BecomesUpward()
        {
            // Flash 约定 deltaVy = -6k（负 y = 向上）→ Unity y 取负 = +4.6875（向上）。
            Vector3 dv = LevelGeometry.FlashVelocityDeltaToWorld(0f, -6f);
            Assert.AreEqual(0f, dv.x, 1e-6f);
            Assert.AreEqual(4.6875f, dv.y, 1e-5f);
            Assert.Greater(dv.y, 0f, "Flash 的向上增量在 Unity 里必须仍是向上（+Y）。");
        }

        // ------------------------------------------------------------------
        // 预览 = 实弹（离散积分等价性）
        // ------------------------------------------------------------------

        [Test]
        public void Ballistics_And_WorldSemiImplicit_Match_Exactly()
        {
            // 命题：把 Unity 物理帧率设为 25fps（FrameSeconds），
            //   初速 = FlashVelocityToWorld(vx, vy)，重力 = WorldGravity(weight)，
            //   PhysX 的半隐式欧拉（v += g*dt; p += v*dt）逐步等于
            //   Ballistics 的 (vy += w; p += v)。
            // 手算第 1 步（x0=0,y0=0,vx=1,vy=0,w=1）：
            //   Flash: vy=1 → y=1；Unity: v_y=0+(-19.53125*0.04)=-0.78125 → y=-0.78125*0.04=-0.03125 = -1/32。
            const float x0 = 100f, y0 = 200f, vx = 3f, vy = -12f, w = 1f;
            const int steps = 8;

            (float x, float y)[] flash = Ballistics.PredictTrajectory(x0, y0, vx, vy, w, steps);

            Vector3 p = LevelGeometry.PixelToWorld(x0, y0);
            Vector3 v = LevelGeometry.FlashVelocityToWorld(vx, vy);
            Vector3 g = LevelGeometry.WorldGravity(w);
            float dt = LevelGeometry.FrameSeconds;

            for (int i = 0; i < steps; i++)
            {
                v += g * dt;
                p += v * dt;
                Vector3 expected = LevelGeometry.PixelToWorld(flash[i].x, flash[i].y);
                Assert.AreEqual(expected.x, p.x, 1e-4f, "第 " + (i + 1) + " 步 x 不一致");
                Assert.AreEqual(expected.y, p.y, 1e-4f, "第 " + (i + 1) + " 步 y 不一致");
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
