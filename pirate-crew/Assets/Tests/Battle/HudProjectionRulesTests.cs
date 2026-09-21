using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Data;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// HUD 透视尺度规则的纯逻辑测试（无头可跑）。
    ///
    /// 【背景】HUD 本体是 UGUI ScreenSpaceOverlay（不随透视变形）；但拾取半径是**屏幕像素口径**
    /// （Flash §3.4 的 30px，见 <see cref="PirateCrew.Battle.LevelGeometry.SelectionRadiusPixels"/>），
    /// 透视下同一世界高度在不同距离上的屏幕尺寸不同。<see cref="HudProjectionRules"/> 给出
    /// **提案/待定**的缩放公式；本测试锁定公式行为，供后续验收决定是否接入 AimThrowController。
    /// </summary>
    [TestFixture]
    public class HudProjectionRulesTests
    {
        const float Fov = 60f;          // M2BattleSceneSetup.CameraFieldOfView（§2 相机）
        const float ScreenHeight = 1080f;
        const float UnitHeight = 0.5f;  // 16px / 32px 每单位（§4.1 top/bottomExtent = 8）

        // ------------------------------------------------------------------
        // 透视投影屏幕高度
        // ------------------------------------------------------------------

        [Test]
        public void ProjectedScreenHeight_MatchesPerspectiveFormula_AtReferenceDistance()
        {
            // px = h * screenH / (2 * tan(fov/2) * d)
            // 注意：18 是 HudProjectionRules 自身的**缩放基准距离**（Godot 基准），仍用于半径缩放公式；
            // 用户裁决 2026-09-14 后**默认机位**已改为角色特写（见下面的特写档测试）。
            float expected = UnitHeight * ScreenHeight / (2f * Mathf.Tan(Fov * Mathf.Deg2Rad * 0.5f) * 18f);
            float actual = HudProjectionRules.ProjectedScreenHeightPixels(UnitHeight, 18f, Fov, ScreenHeight);
            Assert.AreEqual(expected, actual, 1e-3f);
            Assert.AreEqual(25.98f, actual, 0.05f, "距离 18 / FOV 60 / 1080p 下单位屏幕高约 26px");
        }

        /// <summary>
        /// 等距像素卡通（创始人裁决 2026-09-21）：正交口径的可读性判据。
        /// 判据 A-2 / R-6 的"1080p 下单位竖高 ≥25px"按**屏幕像素**口径核算——
        /// 正交 + 全屏像素化（RT 高 360）下，屏幕高 1080 = RT 高 ×3，单位屏幕高 =
        /// 视觉高 ÷ (2 × OrthoSize) × 1080。特写档 size
        /// <see cref="BattleCameraController.CloseUpOrthoSize"/> 与全场档 size
        /// <see cref="BattleCameraController.FullFieldOrthoSize"/> 都必须守住下限。
        /// 高度取**视觉总高 1.85**（Godot 对齐后的角色高），不再用旧口径的 0.5。
        /// </summary>
        [Test]
        public void OrthographicUnitHeight_AtPresetSizes_Exceeds25PxReadabilityFloor()
        {
            const float ScreenPixelsPerRtPixel = 3f; // RT 360 → 屏幕 1080（渲染篇 §3）
            float visualHeight = BattleCameraController.UnitVisualHeight;   // 1.85，与 CrewVisualPrefabBuilder 同源
            float rtHeight = ScreenHeight / ScreenPixelsPerRtPixel;

            // RT 像素口径 → 屏幕像素口径（每块 = 3×3 屏幕像素）。特写 1.85/(2×5)×360 ≈ 67 RT px ≈ 200 屏幕px。
            float closeUpPx = visualHeight
                / (2f * BattleCameraController.CloseUpOrthoSize) * rtHeight * ScreenPixelsPerRtPixel;
            Assert.GreaterOrEqual(closeUpPx, 25f,
                "特写档（size " + BattleCameraController.CloseUpOrthoSize + "）下单位屏幕竖高应 ≥25px（判据 A-2/R-6）");

            float fullFieldPx = visualHeight
                / (2f * BattleCameraController.FullFieldOrthoSize) * rtHeight * ScreenPixelsPerRtPixel;
            Assert.GreaterOrEqual(fullFieldPx, 25f,
                "全场档（size " + BattleCameraController.FullFieldOrthoSize + "）下单位屏幕竖高仍应 ≥25px");
        }

        [Test]
        public void ProjectedScreenHeight_IsInverselyProportionalToDistance()
        {
            float near = HudProjectionRules.ProjectedScreenHeightPixels(UnitHeight, 9f, Fov, ScreenHeight);
            float far = HudProjectionRules.ProjectedScreenHeightPixels(UnitHeight, 18f, Fov, ScreenHeight);
            Assert.AreEqual(2f, near / far, 1e-4f, "距离减半 → 屏幕高度翻倍");
        }

        [Test]
        public void ProjectedScreenHeight_DegenerateInputs_ReturnZero()
        {
            Assert.AreEqual(0f, HudProjectionRules.ProjectedScreenHeightPixels(0f, 18f, Fov, ScreenHeight));
            Assert.AreEqual(0f, HudProjectionRules.ProjectedScreenHeightPixels(UnitHeight, 0f, Fov, ScreenHeight));
            Assert.AreEqual(0f, HudProjectionRules.ProjectedScreenHeightPixels(UnitHeight, 18f, 0f, ScreenHeight));
            Assert.AreEqual(0f, HudProjectionRules.ProjectedScreenHeightPixels(UnitHeight, 18f, Fov, 0f));
        }

        // ------------------------------------------------------------------
        // 半径缩放 + 夹取（提案/待定）
        // ------------------------------------------------------------------

        [Test]
        public void PerspectiveRadius_AtReference_IsExactlyFlashThirty()
        {
            float r = HudProjectionRules.PerspectiveSelectionRadiusPixels(30f, 26f, 26f, 18f, 64f);
            Assert.AreEqual(30f, r, 1e-4f);
        }

        [Test]
        public void PerspectiveRadius_ScalesLinearlyThenClamps()
        {
            // 2× 屏幕尺寸 → 60px（未触上限 64）
            Assert.AreEqual(60f, HudProjectionRules.PerspectiveSelectionRadiusPixels(30f, 26f, 52f, 18f, 64f), 1e-4f);
            // 10× → 300 被夹到上限 64
            Assert.AreEqual(64f, HudProjectionRules.PerspectiveSelectionRadiusPixels(30f, 26f, 260f, 18f, 64f), 1e-4f);
            // 0.1× → 3 被夹到下限 18
            Assert.AreEqual(18f, HudProjectionRules.PerspectiveSelectionRadiusPixels(30f, 26f, 2.6f, 18f, 64f), 1e-4f);
        }

        [Test]
        public void PerspectiveRadius_ZeroReferenceHeight_FallsBackToBase()
        {
            float r = HudProjectionRules.PerspectiveSelectionRadiusPixels(30f, 0f, 52f, 18f, 64f);
            Assert.AreEqual(30f, r, 1e-4f, "基准为 0 时退回固定 30px，不产生 NaN");
        }

        [Test]
        public void PerspectiveRadius_SwappedBounds_StillClampsCorrectly()
        {
            float r = HudProjectionRules.PerspectiveSelectionRadiusPixels(30f, 26f, 260f, 64f, 18f);
            Assert.AreEqual(64f, r, 1e-4f);
        }

        // ------------------------------------------------------------------
        // 一步到位的辅助（按世界位置）
        // ------------------------------------------------------------------

        [Test]
        public void SelectionRadiusFor_AtReferenceDistance_IsThirty()
        {
            var camera = new Vector3(0f, 0f, 0f);
            var unit = new Vector3(0f, 0f, 18f);
            float r = HudProjectionRules.SelectionRadiusPixelsFor(unit, camera, Fov, ScreenHeight);
            Assert.AreEqual(30f, r, 1e-3f);
        }

        [Test]
        public void SelectionRadiusFor_NearUnitsGetLargerRadius_FarUnitsClamp()
        {
            var camera = new Vector3(0f, 0f, 0f);

            float near = HudProjectionRules.SelectionRadiusPixelsFor(
                new Vector3(0f, 0f, 9f), camera, Fov, ScreenHeight);
            Assert.AreEqual(60f, near, 0.1f, "近处单位屏幕更大 → 半径按比例放大（提案）");

            float veryFar = HudProjectionRules.SelectionRadiusPixelsFor(
                new Vector3(0f, 0f, 200f), camera, Fov, ScreenHeight);
            Assert.AreEqual(HudProjectionRules.MinRadiusPixels, veryFar, 1e-3f, "远处夹到下限，避免点不中");
        }

        [Test]
        public void Constants_AreConsistentWithProject()
        {
            // 基准半径 / 基准距离必须与 Flash 30px 与相机默认距离 18 对齐。
            Assert.AreEqual(30f, HudProjectionRules.ReferenceRadiusPixels, 1e-4f);
            Assert.AreEqual(18f, HudProjectionRules.ReferenceDistance, 1e-4f);
            // 【2026-09-14 修复】单位世界高度 = Flash 竖高 16px ÷ 每单位像素数。
            // 格 1→2 世界单位后 PixelsPerUnit = TilePixels(32) / TileWorldSize(2) = 16
            // （LevelGeometry.cs:175-181），故现值 = 16/16 = 1.0；旧期望 16/32 = 0.5 是
            // 32px/单位时代的口径（HudProjectionRules.UnitWorldHeight 的公式本身没变）。
            Assert.AreEqual(16f / LevelGeometry.PixelsPerUnit, HudProjectionRules.UnitWorldHeight, 1e-4f);
            Assert.AreEqual(1f, HudProjectionRules.UnitWorldHeight, 1e-4f, "现值 = 16px ÷ 16px/单位 = 1 世界单位");
            Assert.AreEqual(16f, CrewCatalog.TopExtent * 2f, 1e-4f);
        }
    }
}
