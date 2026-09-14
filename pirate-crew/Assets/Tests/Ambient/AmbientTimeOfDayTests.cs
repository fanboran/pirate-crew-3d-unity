using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient.Tests
{
    /// <summary>
    /// 昼夜/天气档位的纯 C# 用例。
    ///
    /// 【最关键的一条】<c>Noon_MatchesBattleSceneLightingCurrentValues</c>：
    /// 任务书要求"默认正午，不要改变默认可玩状态"。正午档必须与
    /// <c>Assets/Editor/BattleSceneLighting.cs</c> 写死的现状**逐值一致**（主光 1.55 / Euler(48,140,0)，
    /// 出处：docs/阳光感打光调研.md §4 调法 2 / 雾 #B0D4F1 · 50→280（格 1→2 单位后由 25→140 ×2）
    /// / 环境光 0.85），否则"默认档"一应用就把画面改了。
    /// </summary>
    [TestFixture]
    public class AmbientTimeOfDayTests
    {
        [Test]
        public void Default_IsNoon()
        {
            Assert.AreEqual(AmbientTimeOfDay.Noon, AmbientTimeOfDayCatalog.Default);
            Assert.AreEqual(AmbientTimeOfDay.Noon, AmbientTimeOfDayCatalog.FromInt(0));
        }

        [Test]
        public void Noon_MatchesBattleSceneLightingCurrentValues()
        {
            AmbientLightingPreset noon = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Noon);

            // 出处：Assets/Editor/BattleSceneLighting.cs CreateDirectionalLight / ApplySceneAtmosphere
            //（主光 1.55 / 环境光 0.85 = 直射:天光 ≈4:1，docs/阳光感打光调研.md §4 调法 2；
            // 姿态 Euler(48,140,0) 维持场景设计 §6.5 原裁决）。
            // 雾距离 50→280：格 1→2 单位后整段 ×2（旧 25→140；BattleSceneLighting.cs:1038-1039 与
            // AmbientTimeOfDayCatalog.cs:143 的正午档逐值相同）。
            Assert.AreEqual(1.55f, noon.SunIntensity, 1e-4f);
            Assert.AreEqual(48f, noon.SunEuler.x, 1e-4f);
            Assert.AreEqual(140f, noon.SunEuler.y, 1e-4f);
            Assert.AreEqual(50f, noon.FogStart, 1e-4f);
            Assert.AreEqual(280f, noon.FogEnd, 1e-4f);
            Assert.AreEqual(0.85f, noon.AmbientIntensity, 1e-4f);

            Color fog = AmbientTimeOfDayCatalog.Hex("#B0D4F1");
            Assert.AreEqual(fog.r, noon.FogColor.r, 1e-4f);
            Assert.AreEqual(fog.g, noon.FogColor.g, 1e-4f);
            Assert.AreEqual(fog.b, noon.FogColor.b, 1e-4f);
        }

        [Test]
        public void Presets_SunIntensityIsStrictlyMonotonicDecreasing()
        {
            float noon = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Noon).SunIntensity;
            float dusk = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Dusk).SunIntensity;
            float overcast = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Overcast).SunIntensity;

            Assert.Greater(noon, dusk, "正午必须比黄昏亮");
            Assert.Greater(dusk, overcast, "黄昏必须比阴云亮");
        }

        [Test]
        public void Presets_FogEndAndAmbientAreMonotonicDecreasing()
        {
            AmbientLightingPreset noon = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Noon);
            AmbientLightingPreset dusk = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Dusk);
            AmbientLightingPreset overcast = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Overcast);

            Assert.Greater(noon.FogEnd, dusk.FogEnd);
            Assert.Greater(dusk.FogEnd, overcast.FogEnd);
            Assert.Greater(noon.FogStart, dusk.FogStart);
            Assert.Greater(dusk.FogStart, overcast.FogStart);

            Assert.Greater(noon.AmbientIntensity, dusk.AmbientIntensity);
            Assert.Greater(dusk.AmbientIntensity, overcast.AmbientIntensity);
        }

        [Test]
        public void Presets_SunElevation_DuskIsLowerThanNoon()
        {
            // 美术风格指南 §4.6「黄昏暖调」：太阳更低、更长阴影。
            float noon = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Noon).SunEuler.x;
            float dusk = AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Dusk).SunEuler.x;

            Assert.Less(dusk, noon, "黄昏太阳高度必须低于正午");
            Assert.That(dusk, Is.InRange(15f, 35f), "黄昏俯仰角应在低斜射区间（提案 24°）");
        }

        [Test]
        public void Presets_AllColorsParseAndAreNotFallbackMagenta()
        {
            for (int i = 0; i < AmbientTimeOfDayCatalog.Count; i++)
            {
                AmbientLightingPreset preset = AmbientTimeOfDayCatalog.For(AmbientTimeOfDayCatalog.FromInt(i));

                Assert.AreNotEqual(Color.magenta, preset.SunColor, "档 " + i + " 主光色解析失败");
                Assert.AreNotEqual(Color.magenta, preset.FogColor, "档 " + i + " 雾色解析失败");
                Assert.AreNotEqual(Color.magenta, preset.AmbientColor, "档 " + i + " 环境光色解析失败");
                Assert.AreNotEqual(Color.magenta, preset.SkyTint, "档 " + i + " 天空 tint 解析失败");
                Assert.AreEqual(1f, preset.SunColor.a, 1e-4f);
            }
        }

        [Test]
        public void Next_CyclesThroughAllTiers()
        {
            AmbientTimeOfDay t = AmbientTimeOfDay.Noon;

            t = AmbientTimeOfDayCatalog.Next(t);
            Assert.AreEqual(AmbientTimeOfDay.Dusk, t);
            t = AmbientTimeOfDayCatalog.Next(t);
            Assert.AreEqual(AmbientTimeOfDay.Overcast, t);
            t = AmbientTimeOfDayCatalog.Next(t);
            Assert.AreEqual(AmbientTimeOfDay.Noon, t, "三轮后回到正午");
        }

        [Test]
        public void FromInt_ClampsOutOfRangeToDefault()
        {
            Assert.AreEqual(AmbientTimeOfDay.Noon, AmbientTimeOfDayCatalog.FromInt(-1));
            Assert.AreEqual(AmbientTimeOfDay.Noon, AmbientTimeOfDayCatalog.FromInt(99));
            Assert.AreEqual(AmbientTimeOfDay.Overcast, AmbientTimeOfDayCatalog.FromInt(2));
        }

        [Test]
        public void DisplayName_IsChinese()
        {
            Assert.AreEqual("正午", AmbientTimeOfDayCatalog.DisplayName(AmbientTimeOfDay.Noon));
            Assert.AreEqual("黄昏", AmbientTimeOfDayCatalog.DisplayName(AmbientTimeOfDay.Dusk));
            Assert.AreEqual("阴云", AmbientTimeOfDayCatalog.DisplayName(AmbientTimeOfDay.Overcast));
        }

        [Test]
        public void Hex_ParsesSixAndEightDigits()
        {
            Assert.IsTrue(AmbientTimeOfDayCatalog.TryParseHex("#FF3A29", out Color red));
            Assert.AreEqual(1f, red.r, 1e-4f);
            Assert.AreEqual(0x3A / 255f, red.g, 1e-3f);
            Assert.AreEqual(1f, red.a, 1e-4f);

            Assert.IsTrue(AmbientTimeOfDayCatalog.TryParseHex("3366FF80", out Color blue));
            Assert.AreEqual(0x80 / 255f, blue.a, 1e-3f);

            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseHex("nope", out _));
            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseHex(null, out _));
            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseHex("#12345", out _));
        }
    }
}
