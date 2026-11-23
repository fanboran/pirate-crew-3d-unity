using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.Settings;

namespace PirateCrew.Tests.Settings
{
    /// <summary>
    /// <see cref="RenderStyleStore"/>（渲染风格持久化纯逻辑）与同槽其他设置共存的契约测试。
    /// 值域 = cellshade / postline；键缺失或非法时保持原值。
    /// </summary>
    public sealed class RenderStyleStoreTests
    {
        [Test]
        public void SettingsSlot_SharedWithOtherSettings_AtSlot9()
        {
            Assert.AreEqual(9, RenderStyleStore.SettingsSlot,
                "渲染风格与音频/视频/像素比例共用设置槽 9，否则关面板统一落盘会写散两份档。");
            Assert.AreEqual(VideoSettingsStore.SettingsSlot, RenderStyleStore.SettingsSlot);
        }

        [Test]
        public void WriteThenRead_RoundTrips_PostLine()
        {
            var data = new SaveData();

            RenderStyleStore.WriteTo(data, RenderStyle.PostLine);
            RenderStyle style = RenderStyle.CellShade;
            bool any = RenderStyleStore.TryReadFrom(data, ref style);

            Assert.IsTrue(any);
            Assert.AreEqual(RenderStyle.PostLine, style);
        }

        [Test]
        public void WriteThenRead_RoundTrips_CellShade()
        {
            var data = new SaveData();

            RenderStyleStore.WriteTo(data, RenderStyle.CellShade);
            RenderStyle style = RenderStyle.PostLine;
            bool any = RenderStyleStore.TryReadFrom(data, ref style);

            Assert.IsTrue(any);
            Assert.AreEqual(RenderStyle.CellShade, style);
        }

        [Test]
        public void Read_EmptyData_ReturnsFalseAndKeepsValue()
        {
            var data = new SaveData();

            RenderStyle style = RenderStyle.PostLine;
            bool any = RenderStyleStore.TryReadFrom(data, ref style);

            Assert.IsFalse(any, "旧档/首启没有风格键，应保持传入值而不是误报已读。");
            Assert.AreEqual(RenderStyle.PostLine, style, "读失败不得修改原值。");
        }

        [Test]
        public void Read_InvalidValue_ReturnsFalseAndKeepsValue()
        {
            var data = new SaveData();
            data.SetData(RenderStyleStore.StyleKey, "v3");

            RenderStyle style = RenderStyle.CellShade;
            bool any = RenderStyleStore.TryReadFrom(data, ref style);

            Assert.IsFalse(any, "非法值应忽略，不得静默落到某个风格。");
            Assert.AreEqual(RenderStyle.CellShade, style);
        }

        [Test]
        public void Write_DoesNotClobber_OtherSettingsKeys()
        {
            var data = new SaveData();
            VideoSettingsStore.WriteTo(data, fullscreen: false,
                qualityIndex: VideoSettingsStore.QualitySmooth);

            RenderStyleStore.WriteTo(data, RenderStyle.PostLine);

            bool fullscreen = VideoSettingsStore.DefaultFullscreen;
            int quality = VideoSettingsStore.DefaultQuality;
            Assert.IsTrue(VideoSettingsStore.TryReadFrom(data, ref fullscreen, ref quality),
                "风格写入必须走读改写，不得抹掉同槽的视频设置键。");
            Assert.IsFalse(fullscreen);
            Assert.AreEqual(VideoSettingsStore.QualitySmooth, quality);
        }
    }
}
