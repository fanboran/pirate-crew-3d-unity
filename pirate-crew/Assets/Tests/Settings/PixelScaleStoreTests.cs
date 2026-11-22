using PirateCrew.Core;
using PirateCrew.Settings;
using NUnit.Framework;

namespace PirateCrew.Tests.Settings
{
    /// <summary>
    /// <see cref="PixelScaleStore"/>（像素比例档纯键值层）的契约测试：
    /// 往返一致、非法值忽略、自动档换算、与视频设置同槽共存。
    /// </summary>
    public sealed class PixelScaleStoreTests
    {
        [Test]
        public void SettingsSlot_SharesSlot9_WithVideoSettings()
        {
            Assert.AreEqual(VideoSettingsStore.SettingsSlot, PixelScaleStore.SettingsSlot,
                "像素比例与视频设置同槽读改写（同一份设置数据），分槽会把'关面板统一落盘'写散。");
        }

        [Test]
        public void WriteThenRead_RoundTrips_SelectableScales()
        {
            foreach (int scale in new[] { 2, PixelScaleStore.ScaleAuto })
            {
                var data = new SaveData();
                PixelScaleStore.WriteTo(data, scale);

                int index = PixelScaleStore.ScaleDefault;
                Assert.IsTrue(PixelScaleStore.TryReadFrom(data, ref index));
                Assert.AreEqual(scale, index, "可选档 {0} 往返应一致", scale);
            }
        }

        [Test]
        public void LegacyFixedScales_NormalizeToDefault()
        {
            // 手动 3×/4× 已退役：旧档读入归一为 2×，写回也写 2×。
            foreach (int legacy in new[] { 3, 4 })
            {
                var data = new SaveData();
                data.SetData(PixelScaleStore.ScaleKey, legacy.ToString());

                int index = PixelScaleStore.ScaleDefault;
                Assert.IsTrue(PixelScaleStore.TryReadFrom(data, ref index));
                Assert.AreEqual(PixelScaleStore.ScaleDefault, index, "旧档 {0}× 应归一为 2×", legacy);

                var rewritten = new SaveData();
                PixelScaleStore.WriteTo(rewritten, legacy);
                int readBack = PixelScaleStore.ScaleDefault;
                Assert.IsTrue(PixelScaleStore.TryReadFrom(rewritten, ref readBack));
                Assert.AreEqual(PixelScaleStore.ScaleDefault, readBack);
            }
        }

        [Test]
        public void IsSelectable_OnlyAutoAndDefault()
        {
            Assert.IsTrue(PixelScaleStore.IsSelectable(PixelScaleStore.ScaleAuto));
            Assert.IsTrue(PixelScaleStore.IsSelectable(PixelScaleStore.ScaleDefault));
            Assert.IsFalse(PixelScaleStore.IsSelectable(3));
            Assert.IsFalse(PixelScaleStore.IsSelectable(4));
            Assert.IsFalse(PixelScaleStore.IsSelectable(1));
        }

        [Test]
        public void WriteThenRead_RoundTrips_Auto()
        {
            var data = new SaveData();
            PixelScaleStore.WriteTo(data, PixelScaleStore.ScaleAuto);

            int index = PixelScaleStore.ScaleDefault;
            Assert.IsTrue(PixelScaleStore.TryReadFrom(data, ref index));
            Assert.AreEqual(PixelScaleStore.ScaleAuto, index);
        }

        [Test]
        public void Read_EmptyData_ReturnsFalseAndKeepsOriginal()
        {
            int index = PixelScaleStore.ScaleDefault;
            Assert.IsFalse(PixelScaleStore.TryReadFrom(new SaveData(), ref index));
            Assert.AreEqual(PixelScaleStore.ScaleDefault, index, "旧档/首启没有比例键，应保持原值。");
        }

        [Test]
        public void Read_InvalidValues_AreIgnored()
        {
            foreach (string bad in new[] { "1", "5", "7", "fast", "-2" })
            {
                var data = new SaveData();
                data.SetData(PixelScaleStore.ScaleKey, bad);

                int index = PixelScaleStore.ScaleDefault;
                bool any = PixelScaleStore.TryReadFrom(data, ref index);

                Assert.IsFalse(any, "非法值 {0} 不应被当作有档", bad);
                Assert.AreEqual(PixelScaleStore.ScaleDefault, index, "非法值 {0} 必须被忽略", bad);
            }
        }

        [Test]
        public void AutoUnit_ScalesWithScreenHeight_ClampedToContract()
        {
            // 参考画布高 540：整倍数换算 + 契约域 [2,4] 钳制。
            Assert.AreEqual(2, PixelScaleStore.AutoUnit(1080), "1080p → 2×（现行观感不变）");
            Assert.AreEqual(3, PixelScaleStore.AutoUnit(1620), "1620p → 3×");
            Assert.AreEqual(4, PixelScaleStore.AutoUnit(2160), "2160p → 4×");
            Assert.AreEqual(2, PixelScaleStore.AutoUnit(1440), "1440p floor=2 → 下界 2");
            Assert.AreEqual(2, PixelScaleStore.AutoUnit(540), "540 floor=1 → 钳到下界（关像素化不归本档）");
            Assert.AreEqual(4, PixelScaleStore.AutoUnit(4320), "4320p → 上界 4");
            Assert.AreEqual(2, PixelScaleStore.AutoUnit(0), "无头/异常环境（高 0）回落默认档");
            Assert.AreEqual(2, PixelScaleStore.AutoUnit(-100));
        }

        [Test]
        public void ResolveUnit_SelectablePassesThrough_LegacyFallsBack_AutoComputes()
        {
            Assert.AreEqual(2, PixelScaleStore.ResolveUnit(2, 1080));
            Assert.AreEqual(2, PixelScaleStore.ResolveUnit(3, 1080), "退役的固定 3× 回落 2×");
            Assert.AreEqual(2, PixelScaleStore.ResolveUnit(4, 1080), "退役的固定 4× 回落 2×");
            Assert.AreEqual(2, PixelScaleStore.ResolveUnit(PixelScaleStore.ScaleAuto, 1080));
            Assert.AreEqual(4, PixelScaleStore.ResolveUnit(PixelScaleStore.ScaleAuto, 2160));
        }

        [Test]
        public void PixelScale_AndVideoSettings_OnSameSaveData_DoNotOverwriteEachOther()
        {
            var shared = new SaveData();
            VideoSettingsStore.WriteTo(shared, fullscreen: true, qualityIndex: VideoSettingsStore.QualitySmooth);
            PixelScaleStore.WriteTo(shared, 3);

            bool fullscreen = false;
            int quality = VideoSettingsStore.QualityHigh;
            Assert.IsTrue(VideoSettingsStore.TryReadFrom(shared, ref fullscreen, ref quality));
            Assert.IsTrue(fullscreen);
            Assert.AreEqual(VideoSettingsStore.QualitySmooth, quality, "比例键写入不得抹掉视频键");

            int index = PixelScaleStore.ScaleDefault;
            Assert.IsTrue(PixelScaleStore.TryReadFrom(shared, ref index));
            Assert.AreEqual(PixelScaleStore.ScaleDefault, index, "视频键写入不得抹掉比例键；旧档 3× 归一 2×");
        }
    }
}
