using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.UI.Tests
{
    /// <summary>
    /// 俯视海图（M4 世界地图模式）的装配行为测试——<see cref="BattleMinimap.ConfigureWorldChartFromRuntime"/>
    /// 的双模式切换（UI 审计 P0-2 / 地图审计 §二.10）。
    /// 纯函数口径（WorldBoxToChartRect / 越界不变量）见 <see cref="MinimapChartRectTests"/>（无头可跑）；
    /// 本文件实例化 MonoBehaviour，只能跑 Unity EditMode。
    ///
    /// 【零海图口径】八张世界海图已删除待重做、目录为空 ⇒ <c>WorldMapRuntime.SetPending</c> 恒失败，
    /// 海图模式的**待战入口不再可达**（<c>ConfigureWorldChartFromRuntime</c> 只认待战通道，
    /// 没有注入缝）——故用例改为钉住"入口被拒 ⇒ 保持烘焙模式"，海图重做后恢复
    /// "待战海图 → 图幅重定 + 岛层重建"的原始断言。
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
        public void SetPendingWithoutWorldMapCatalog_IsRejected_StaysBakedMode()
        {
            Assert.IsFalse(WorldMapRuntime.SetPending("wreck_hymn"),
                "零海图（目录为空）下 SetPending 必须被拒——海图模式不可达");

            BattleMinimap minimap = CreateMinimap();
            try
            {
                minimap.ConfigureWorldChartFromRuntime();

                Assert.IsFalse(minimap.IsWorldChartMode);
                Assert.IsNull(minimap.WorldChartLayer);
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
