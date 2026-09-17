using NUnit.Framework;
using PirateCrew.PirateCrew.Battle.WorldMaps;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.PirateCrew.UI.Tests
{
    /// <summary>
    /// <see cref="MinimapRules.WorldBoxToChartRect"/> 纯函数口径（无头可跑：
    /// 只依赖 WorldMapRules 的嵌套 struct 与目录数据，不实例化任何 MonoBehaviour；
    /// BattleMinimap 的装配行为测试见 MinimapWorldChartTests，需 Unity EditMode）。
    /// </summary>
    [TestFixture]
    public class MinimapChartRectTests
    {
        const float Span = 150f;

        [Test]
        public void CenteredAxisAlignedBox_IsCenteredWithRelativeSize()
        {
            Rect r = MinimapRules.WorldBoxToChartRect(
                new Vector2(75f, 75f), new Vector2(30f, 20f), 0f, Span, Span);

            Assert.AreEqual(0.5f, r.center.x, 1e-4f);
            Assert.AreEqual(0.5f, r.center.y, 1e-4f);
            Assert.AreEqual(30f / Span, r.width, 1e-4f);
            Assert.AreEqual(20f / Span, r.height, 1e-4f);
        }

        [Test]
        public void QuarterTurnRotation_SwapsAabbExtents()
        {
            Rect r = MinimapRules.WorldBoxToChartRect(
                new Vector2(75f, 75f), new Vector2(30f, 10f), 90f, Span, Span);

            // 旋转 90° 后 AABB 宽深互换。
            Assert.AreEqual(10f / Span, r.width, 1e-4f);
            Assert.AreEqual(30f / Span, r.height, 1e-4f);
        }

        [Test]
        public void VAxisFlips_ZNearZeroLandsAtChartTop()
        {
            // u/v 口径与 ArenaToNormalized 一致：z 小（远端）→ 图上方（v 大）。
            Rect nearZeroZ = MinimapRules.WorldBoxToChartRect(
                new Vector2(75f, 10f), new Vector2(20f, 10f), 0f, Span, Span);
            Rect nearMaxZ = MinimapRules.WorldBoxToChartRect(
                new Vector2(75f, 140f), new Vector2(20f, 10f), 0f, Span, Span);

            Assert.Greater(nearZeroZ.yMax, 0.85f);
            Assert.Less(nearMaxZ.yMin, 0.15f);
        }

        [Test]
        public void DiagonalYaw_InflatesAabbTowardDiagonal()
        {
            float side = 20f;
            Rect axis = MinimapRules.WorldBoxToChartRect(
                new Vector2(75f, 75f), new Vector2(side, side), 0f, Span, Span);
            Rect diag = MinimapRules.WorldBoxToChartRect(
                new Vector2(75f, 75f), new Vector2(side, side), 45f, Span, Span);

            // 45° 正方形的 AABB 边长 = side·√2。
            Assert.AreEqual(side * Mathf.Sqrt(2f) / Span, diag.width, 1e-4f);
            Assert.Greater(diag.width, axis.width);
        }

        [Test]
        public void RectMayExceedUnitSquare_BeforeClamp()
        {
            // 越界裁剪是显示层（Clamp01）的职责；纯函数如实给出越界矩形。
            Rect r = MinimapRules.WorldBoxToChartRect(
                new Vector2(-10f, 75f), new Vector2(30f, 20f), 0f, Span, Span);
            Assert.Less(r.xMin, 0f);
        }

        [Test]
        public void AllMaps_AllStandBoxRects_StayWithinChart()
        {
            // 八图全部站面 box 的海图矩形应落在面板内（裁剪前）——
            // 布局对话已把内容跨度压到 76-94%，海图岛层不该被面板边裁掉。
            for (int i = 0; i < WorldMapCatalog.Count; i++)
            {
                WorldMapDefinition map = WorldMapCatalog.All[i];
                var boxes = WorldMapRules.AllStandBoxes(map);
                Assert.Greater(boxes.Count, 0, map.Id + " 应有站面");

                foreach (WorldMapRules.WorldBox box in boxes)
                {
                    Rect r = MinimapRules.WorldBoxToChartRect(
                        box.Center, box.Size, box.YawDeg, map.SpanX, map.SpanZ);
                    Assert.GreaterOrEqual(r.xMin, -0.01f, $"{map.Id} box {box.Center} 越出图西界");
                    Assert.LessOrEqual(r.xMax, 1.01f, $"{map.Id} box {box.Center} 越出图东界");
                    Assert.GreaterOrEqual(r.yMin, -0.01f, $"{map.Id} box {box.Center} 越出图南界");
                    Assert.LessOrEqual(r.yMax, 1.01f, $"{map.Id} box {box.Center} 越出图北界");
                }
            }
        }
    }
}
