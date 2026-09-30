using NUnit.Framework;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 锚落地线 <see cref="WeaponProjectile.AnchorLandingY"/> 的口径测试
    /// （纯 C#，不 new GameObject / MonoBehaviour，无头验证台可断言）。
    /// 覆盖：平地、抬升高度场、水格哨兵不落定、半高对落地线的一比一影响。
    /// 背景口径：锚的落地阈值与出生摆放 / AI 模拟共用同一份静态高度场
    /// （<see cref="TileTerrainGrid.SurfaceWorldYAtWorld"/>），水格哨兵
    /// <see cref="TileTerrainGrid.WaterVoidY"/> 的语义见 TileTerrainGrid 类头。
    /// </summary>
    [TestFixture]
    public class AnchorLandingTests
    {
        const float HalfHeight = 0.5f;

        [Test]
        public void FlatGround_LandingLineIsGroundTopPlusHalfHeight()
        {
            // 无抬升格（blocks=0）：地表 = GroundTopY，锚心落地线 = 地表 + 半高。
            float landingY = WeaponProjectile.AnchorLandingY(LevelGeometry.GroundTopY, HalfHeight);
            Assert.AreEqual(LevelGeometry.GroundTopY + HalfHeight, landingY);
        }

        [Test]
        public void RaisedHeightField_LandingLineFollowsSurface()
        {
            // 抬升 7 块（现役关卡高度场的量级）：地表 = GroundTopY + 7 × BlockWorldHeight，
            // 落地线随地表抬升同量上移——锚落到高台顶而不是穿台落到基础地面。
            float surfaceY = LevelGeometry.GroundTopY + 7f * LevelGeometry.BlockWorldHeight;
            float landingY = WeaponProjectile.AnchorLandingY(surfaceY, HalfHeight);
            Assert.AreEqual(surfaceY + HalfHeight, landingY);
            Assert.Greater(landingY, LevelGeometry.GroundTopY + HalfHeight, "落地线必须高于基础地面口径");
        }

        [Test]
        public void WaterCellSentinel_NeverProducesLandingLine()
        {
            // 平台模式水格：SurfaceWorldYAtWorld 返回 WaterVoidY（水面下方哨兵）→ NaN，
            // 调用方不落地、继续下坠，交给 HandleSpecialBounds 的落水清退（§4.4 落水即死）。
            Assert.IsTrue(float.IsNaN(
                WeaponProjectile.AnchorLandingY(TileTerrainGrid.WaterVoidY, HalfHeight)));

            // 水面线本身及任何水下返回值一律视为无地表：判据写「不高于水面」而非 == 哨兵。
            Assert.IsTrue(float.IsNaN(
                WeaponProjectile.AnchorLandingY(LevelGeometry.WaterSurfaceY, HalfHeight)));
        }

        [Test]
        public void HalfHeight_ShiftsLandingLineOneToOne()
        {
            // 半高只做刚体平移：地表相同时，半高差多少落地线就差多少
            // （锚心 y ≤ 落地线即落定的「容差」由半高承担，见 AdvanceAnchor 的判据）。
            float landingY = WeaponProjectile.AnchorLandingY(2f, HalfHeight);
            Assert.AreEqual(HalfHeight,
                WeaponProjectile.AnchorLandingY(2f, 1f) - landingY, 1e-5f);
        }
    }
}
