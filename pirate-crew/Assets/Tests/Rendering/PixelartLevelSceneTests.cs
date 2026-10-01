using System.Linq;
using NUnit.Framework;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 像素化"真实内容"试点场景**取景表**（<see cref="PixelartLevelScene"/>）的契约断言
    /// （纯 C#——表与查询全是静态数据/静态方法，无 GameObject）。
    ///
    /// 【为什么钉这张表】它是装配器、出图脚本与 Build Settings 登记共用的唯一数据源
    /// （PixelartLevelScene.cs 类头："两处各写一份的坑本仓踩过"）；号段语义（样板关 1/3/4/5/6，
    /// 海图 101–108 已删除待重做）也登记在此——解析错号必须显式失败，不许静默兜底到别的关。
    /// </summary>
    public class PixelartLevelSceneTests
    {
        // ------------------------------------------------------------------
        // TryGet：号段语义
        // ------------------------------------------------------------------

        [Test]
        public void TryGet_InRegistryLevels_ReturnsRowWithMatchingIdentity()
        {
            int[] inRegistry = { 1, 3, 4, 5, 6 };

            foreach (int level in inRegistry)
            {
                Assert.That(PixelartLevelScene.TryGet(level, out PixelartLevelScene.View view), Is.True,
                    "关卡 " + level + " 应在取景表里");
                Assert.That(view.LevelNumber, Is.EqualTo(level));
                Assert.That(view.SceneName, Is.Not.Empty);
                Assert.That(view.ShotPrefix, Is.EqualTo("pl" + level), "出图前缀应由关卡号派生");
            }
        }

        [Test]
        public void TryGet_OutOfRegistryLevel_ReturnsFalse()
        {
            // 2 = 样板关号段空洞；101 = 已删除待重做的海图号段（PixelartLevelScene.cs:149 注释）——
            // 查不到就返回 false（"未知关卡号返回 false（不静默兜底到别的关）"），
            // PlayerArtCapture.Install 依赖它走回落分支并打 error 日志。
            foreach (int level in new[] { 0, 2, 7, 100, 101, -1 })
            {
                Assert.That(PixelartLevelScene.TryGet(level, out _), Is.False,
                    "关卡 " + level + " 不应在取景表里");
            }
        }

        // ------------------------------------------------------------------
        // All：表不变量 + 防御拷贝
        // ------------------------------------------------------------------

        [Test]
        public void All_TableInvariants_LevelNumbersUniqueAndFramingOrdered()
        {
            PixelartLevelScene.View[] all = PixelartLevelScene.All;

            Assert.That(all, Is.Not.Empty, "取景表不应为空");
            Assert.That(all.Select(v => v.LevelNumber).Distinct().Count(), Is.EqualTo(all.Length),
                "关卡号不应重复");

            foreach (PixelartLevelScene.View view in all)
            {
                Assert.That(view.SceneName, Is.Not.Empty, "关卡 " + view.LevelNumber + " 场景名不应为空");
                Assert.Greater(view.WideVisibleMeters, 0f, "三档可见米数必须为正（宽/中/近）");
                Assert.Greater(view.MidVisibleMeters, 0f);
                Assert.Greater(view.CloseVisibleMeters, 0f);
                Assert.GreaterOrEqual(view.WideVisibleMeters, view.MidVisibleMeters, "宽机位不应比中机位更近");
                Assert.GreaterOrEqual(view.MidVisibleMeters, view.CloseVisibleMeters, "中机位不应比近机位更近");
            }
        }

        [Test]
        public void All_ReturnsDefensiveCopy_WritingIntoItDoesNotCorruptRegistry()
        {
            // All 明写 "(View[])_views.Clone()"：调用方改返回数组不许污染注册表——
            // 否则装配器/出图脚本谁先跑谁改表，后跑的拿到被改过的取景口径。
            PixelartLevelScene.View[] copy = PixelartLevelScene.All;
            copy[0] = default;

            Assert.That(PixelartLevelScene.TryGet(1, out PixelartLevelScene.View view), Is.True,
                "外部改 All() 返回数组不应影响注册表");
            Assert.That(view.SceneName, Is.EqualTo("PixelartCloud"));
        }

        // ------------------------------------------------------------------
        // AzimuthFor / CameraDistanceFor：装配器与出图脚本共用的派生规则
        // ------------------------------------------------------------------

        [Test]
        public void AzimuthFor_RowOverrideWins_ZeroFallsBackToGlobalDefault()
        {
            // 覆盖分支：行里给了方位角（> 0）就用行值；
            // 兜底分支：0 = 未覆盖，回落全局默认 45°（PixelartPilotScene.AzimuthDegrees，对称菱形）。
            var overridden = new PixelartLevelScene.View(1, "S", new Vector3(20f, 4.5f, 15f), 32f, 13.7f, 7f, 60f);
            var notOverridden = new PixelartLevelScene.View(1, "S", new Vector3(20f, 4.5f, 15f), 32f, 13.7f, 7f);

            Assert.That(PixelartLevelScene.AzimuthFor(overridden), Is.EqualTo(60f).Within(0.0001f));
            Assert.That(PixelartLevelScene.AzimuthFor(notOverridden), Is.EqualTo(45f).Within(0.0001f),
                "未覆盖方位角应兜底全局默认 45°");
        }

        [Test]
        public void CameraDistanceFor_KeepsBaselineWhenContentNarrow_ExtendsWhenContentDeep()
        {
            // 规则（PixelartLevelScene.CameraDistanceFor 注释）：max(基准 60, wide × 1.1)——
            // 基准保证样板关老场景逐字节不变；深跨内容（如假想的 280 m 海图）必须把机位
            // 推到内容半跨度之外，否则近侧半张图被正交近平面裁掉且不报错。
            var narrow = new PixelartLevelScene.View(1, "S", Vector3.zero, 32f, 13.7f, 7f);
            var deep = new PixelartLevelScene.View(101, "S", Vector3.zero, 80f, 13.7f, 7f);

            Assert.That(PixelartLevelScene.CameraDistanceFor(narrow), Is.EqualTo(60f).Within(0.0001f),
                "样板关量级应保持 60 m 基准");
            Assert.That(PixelartLevelScene.CameraDistanceFor(deep), Is.EqualTo(88f).Within(0.0001f),
                "深跨内容应按 1.1 × 宽机位可见高度外推");
        }

        // ------------------------------------------------------------------
        // 与装配契约的联动
        // ------------------------------------------------------------------

        [Test]
        public void Contract_PixelScale_MatchesPilotSceneConstant()
        {
            // PixelartSceneContract.PixelScale 是装配契约的转发口（PixelartSceneContract.cs:18）——
            // 转发源与 rigs 的默认/端点常量必须同值：契约说 2，滚轮却从别的档起步，就是单源失守。
            Assert.That(PixelartSceneContract.PixelScale, Is.EqualTo(PixelartPilotScene.PixelScale));
            Assert.That(PixelartPilotScene.PixelScale, Is.EqualTo(PixelartCameraRig.PixelScaleDefault));
            Assert.That(PixelartCameraRig.PixelScaleDefault,
                Is.InRange(PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax));
        }
    }
}
