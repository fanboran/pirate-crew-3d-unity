using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 小地图换算/表现规则的纯逻辑测试（无头可跑；不碰 GameObject/MonoBehaviour）。
    ///
    /// 【对应依据】<c>docs/参考游戏逆向-海盗军团抢宝藏-静态.md</c> §8.1（<c>Map.as</c>：
    /// dotSize=3 / 红 0xFF3A29 / 蓝 0x3366FF / alpha=mapVisibility*100 / 死亡 -0.1 帧）
    /// 与 §2.3（mapHolder 在 (20,20)）；以及 <c>docs/3D空间模型对齐.md</c> §1 的 XZ 重投影。
    /// 原版未给出的映射（XZ → 归一化坐标、面板比例）标为**提案/待定**，本测试锁定其行为不再漂移。
    /// </summary>
    [TestFixture]
    public class MinimapRulesTests
    {
        const float Tolerance = 1e-5f;

        // ------------------------------------------------------------------
        // XZ → 归一化坐标
        // ------------------------------------------------------------------

        [Test]
        public void ArenaToNormalized_MapsCorners_WithZDown()
        {
            // 左上角 = 世界 (0, z=0)（-Z 远离相机 → 小地图上方）；
            // 右下角 = 世界 (width, depth)（+Z 朝相机 → 小地图下方）。
            Vector2 topLeft = MinimapRules.ArenaToNormalized(0f, 0f, 50f, 17f);
            Assert.AreEqual(0f, topLeft.x, Tolerance);
            Assert.AreEqual(1f, topLeft.y, Tolerance, "+Z 朝相机，z=0 应在小地图上方（v=1）");

            Vector2 bottomRight = MinimapRules.ArenaToNormalized(50f, 17f, 50f, 17f);
            Assert.AreEqual(1f, bottomRight.x, Tolerance);
            Assert.AreEqual(0f, bottomRight.y, Tolerance, "z=depth 应在小地图下方（v=0）");
        }

        [Test]
        public void ArenaToNormalized_MapsCenterToCenter()
        {
            Vector2 center = MinimapRules.ArenaToNormalized(25f, 8.5f, 50f, 17f);
            Assert.AreEqual(0.5f, center.x, Tolerance);
            Assert.AreEqual(0.5f, center.y, Tolerance);
        }

        [Test]
        public void ArenaToNormalized_DegenerateSize_DoesNotDivideByZero()
        {
            Vector2 p = MinimapRules.ArenaToNormalized(3f, 4f, 0f, 0f);
            Assert.AreEqual(0f, p.x);
            Assert.AreEqual(0f, p.y);
        }

        [Test]
        public void ClampNormalized_ClampsOutOfArenaPoints()
        {
            Vector2 clamped = MinimapRules.ClampNormalized(new Vector2(-0.2f, 1.4f));
            Assert.AreEqual(0f, clamped.x, Tolerance);
            Assert.AreEqual(1f, clamped.y, Tolerance);
        }

        [Test]
        public void Level1_RedTeamSitsLeftOfBlueTeam()
        {
            // level_1：红队 gridX=17 / 蓝队 gridX=48，世界 X = gridX + 0.5（§4.3 LevelGeometry.GridToArena）。
            float red = MinimapRules.ArenaToNormalized(17.5f, 10.5f, 50f, 17f).x;
            float blue = MinimapRules.ArenaToNormalized(48.5f, 10.5f, 50f, 17f).x;
            Assert.Less(red, blue, "红队在竞技场左侧（u 更小），小地图上应仍在左边");
            Assert.AreEqual(0.35f, red, 1e-4f);
            Assert.AreEqual(0.97f, blue, 1e-4f);
        }

        // ------------------------------------------------------------------
        // 面板比例与点尺寸
        // ------------------------------------------------------------------

        [Test]
        public void PanelSizePixels_KeepsArenaAspectRatio()
        {
            Vector2 size = MinimapRules.PanelSizePixels(50, 17, 5f);
            Assert.AreEqual(250f, size.x, Tolerance);
            Assert.AreEqual(85f, size.y, Tolerance);
            Assert.AreEqual(50f / 17f, size.x / size.y, 1e-4f, "面板应与竞技场同比例，不拉伸变形");
        }

        [Test]
        public void PanelSizePixels_NonPositivePixelsPerTile_FallsBackToOne()
        {
            Vector2 size = MinimapRules.PanelSizePixels(50, 17, 0f);
            Assert.AreEqual(50f, size.x, Tolerance);
            Assert.AreEqual(17f, size.y, Tolerance);
        }

        [Test]
        public void DotSizePixels_HasFloorAndScalesWithTile()
        {
            Assert.AreEqual(6.25f, MinimapRules.DotSizePixels(5f), Tolerance);
            Assert.AreEqual(4f, MinimapRules.DotSizePixels(1f), Tolerance, "下限 4px，保证远小点位也能看见");
        }

        // ------------------------------------------------------------------
        // 死亡淡出（原版 mapVisibility -= 0.1/帧）
        // ------------------------------------------------------------------

        [Test]
        public void DeadFadePerSecond_MatchesFlashPerFrameAt25Fps()
        {
            Assert.AreEqual(3f, MinimapRules.FlashDotSizePixels, Tolerance);
            Assert.AreEqual(25f, LevelGeometry.FrameRate, Tolerance);
            Assert.AreEqual(2.5f, MinimapRules.DeadFadePerSecond, Tolerance);
        }

        [Test]
        public void AdvanceVisibility_FadesLinearlyAndClamps()
        {
            Assert.AreEqual(0.75f, MinimapRules.AdvanceVisibility(1f, 0.1f), Tolerance);
            Assert.AreEqual(0f, MinimapRules.AdvanceVisibility(1f, 0.4f), Tolerance, "0.4s 应恰好淡到 0");
            Assert.AreEqual(0f, MinimapRules.AdvanceVisibility(0.5f, 10f), Tolerance, "不应低于 0");
            Assert.AreEqual(0.5f, MinimapRules.AdvanceVisibility(0.5f, 0f), Tolerance, "dt=0 不变");
        }

        // ------------------------------------------------------------------
        // 颜色（原版取值）
        // ------------------------------------------------------------------

        [Test]
        public void TeamColors_MatchFlashHex()
        {
            Color red = MinimapRules.RedTeamColor;
            Assert.AreEqual(1f, red.r, Tolerance);
            Assert.AreEqual(0x3A / 255f, red.g, Tolerance);   // 0xFF3A29
            Assert.AreEqual(0x29 / 255f, red.b, Tolerance);

            Color blue = MinimapRules.BlueTeamColor;
            Assert.AreEqual(0x33 / 255f, blue.r, Tolerance);  // 0x3366FF
            Assert.AreEqual(0x66 / 255f, blue.g, Tolerance);
            Assert.AreEqual(1f, blue.b, Tolerance);
        }

        [Test]
        public void DotColor_AppliesVisibilityAsAlpha()
        {
            Color faded = MinimapRules.DotColor(0, 0.3f);
            Assert.AreEqual(MinimapRules.RedTeamColor.r, faded.r, Tolerance);
            Assert.AreEqual(0.3f, faded.a, Tolerance);

            Color blue = MinimapRules.DotColor(1, 1f);
            Assert.AreEqual(MinimapRules.BlueTeamColor.b, blue.b, Tolerance);
            Assert.AreEqual(1f, blue.a, Tolerance);

            Color clamped = MinimapRules.DotColor(1, 5f);
            Assert.AreEqual(1f, clamped.a, Tolerance, "alpha 应夹在 [0,1]");
        }
    }
}
