using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.UI.Tests
{
    /// <summary>
    /// 俯视海图（M4 世界地图模式）的装配行为测试——<see cref="BattleMinimap.ConfigureWorldChartFromRuntime"/>
    /// 的双模式切换（UI 审计 P0-2 / 地图审计 §二.10）。
    /// 纯函数口径（WorldBoxToChartRect / 八图越界不变量）见 <see cref="MinimapChartRectTests"/>（无头可跑）；
    /// 本文件实例化 MonoBehaviour，只能跑 Unity EditMode。
    /// </summary>
    [TestFixture]
    public class MinimapWorldChartTests
    {
        BattleMinimap CreateMinimap()
        {
            var go = new GameObject("MinimapUnderTest");
            var minimap = go.AddComponent<BattleMinimap>();
            var dotLayer = new GameObject("DotLayer", typeof(RectTransform));
            dotLayer.transform.SetParent(go.transform, false);
            SetField(minimap, "dotLayer", (RectTransform)dotLayer.transform);
            return minimap;
        }

        static void SetField(BattleMinimap minimap, string name, object value)
        {
            var field = typeof(BattleMinimap).GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "序列化字段存在: " + name);
            field.SetValue(minimap, value);
        }

        [TearDown]
        public void TearDown()
        {
            WorldMapRuntime.ClearPending();
        }

        [Test]
        public void WorldMapPending_RetargetsArenaSpanAndBuildsChart()
        {
            Assert.IsTrue(WorldMapRuntime.SetPending("wreck_hymn"));
            WorldMapRuntime.TryGetPending(out WorldMapDefinition map);

            BattleMinimap minimap = CreateMinimap();
            try
            {
                minimap.ConfigureWorldChartFromRuntime();

                Assert.IsTrue(minimap.IsWorldChartMode);
                Assert.AreEqual(map.SpanX, minimap.ArenaWidth, 1e-3f);
                Assert.AreEqual(map.SpanZ, minimap.ArenaDepth, 1e-3f);
                Assert.IsNotNull(minimap.WorldChartLayer);
                // 岛层数 = 站面 box 数（>=1；wreck_hymn 有多件）。
                Assert.GreaterOrEqual(minimap.WorldChartLayer.childCount, 1);
            }
            finally
            {
                Object.DestroyImmediate(minimap.gameObject);
            }
        }

        [Test]
        public void NoWorldMapPending_StayBakedMode()
        {
            WorldMapRuntime.ClearPending();

            BattleMinimap minimap = CreateMinimap();
            try
            {
                minimap.ConfigureWorldChartFromRuntime();

                Assert.IsFalse(minimap.IsWorldChartMode);
                Assert.IsNull(minimap.WorldChartLayer);
                Assert.AreEqual(50f, minimap.ArenaWidth, 1e-3f, "旧关卡口径 50×17 保持不变");
                Assert.AreEqual(17f, minimap.ArenaDepth, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(minimap.gameObject);
            }
        }
    }
}
