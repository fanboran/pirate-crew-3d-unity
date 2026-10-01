using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 场景美术调色板（<see cref="SceneArtPalette"/>）的解析契约：
    /// ① 全部十六进制常量合法——任何一处拼错，<see cref="SceneArtPalette.Hex"/> 都会**静默**
    ///    兜底成品红（失败不抛、只留色），场景上就是一块突兀的品红，很难追到源头；
    /// ② 解析器分支矩阵（<c>#RRGGBB</c> / <c>#RRGGBBAA</c> / 可省 <c>#</c> / 大小写）。
    /// 出典：pirate-crew/Assets/Scripts/PirateCrew/SceneArt/SceneArtPalette.cs（解析器为
    /// 自实现纯 C#，"既保住色值可无头测试，也少一个 Unity 依赖"，无 GameObject 可直测）。
    /// </summary>
    public class SceneArtPaletteTests
    {
        [Test]
        public void Palette_AllConstHex_ParseCleanly()
        {
            string[] hexes = typeof(SceneArtPalette)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null))
                .ToArray();

            Assert.That(hexes, Is.Not.Empty, "调色板不应为空");

            foreach (string hex in hexes)
            {
                Assert.That(SceneArtPalette.TryParseHex(hex, out Color color), Is.True,
                    $"色值 '{hex}' 解析失败——拼错会静默兜底成品红污染场景");
                Assert.That(color.r, Is.InRange(0f, 1f), $"色值 '{hex}' R 通道越界");
                Assert.That(color.g, Is.InRange(0f, 1f), $"色值 '{hex}' G 通道越界");
                Assert.That(color.b, Is.InRange(0f, 1f), $"色值 '{hex}' B 通道越界");
                Assert.That(color.a, Is.EqualTo(1f).Within(0.0001f), $"色值 '{hex}' 六位制 alpha 应为 1");
            }
        }

        [Test]
        public void TryParseHex_FormatMatrix()
        {
            // 合法：6 位 / 8 位（带 alpha）/ '#' 可省 / 大小写不限。
            Assert.That(SceneArtPalette.TryParseHex("#FF3A29", out _), Is.True);
            Assert.That(SceneArtPalette.TryParseHex("FF3A29", out _), Is.True);
            Assert.That(SceneArtPalette.TryParseHex("#ff3a29", out _), Is.True);
            Assert.That(SceneArtPalette.TryParseHex("#FF3A2980", out _), Is.True, "8 位制应解析出 alpha");

            Assert.That(SceneArtPalette.TryParseHex("#FF3A2980", out Color withAlpha), Is.True);
            Assert.That(withAlpha.a, Is.EqualTo(0x80 / 255f).Within(0.0001f));

            // 非法：null / 空串 / 位长错 / 非 hex 字符。
            Assert.That(SceneArtPalette.TryParseHex(null, out _), Is.False);
            Assert.That(SceneArtPalette.TryParseHex("", out _), Is.False);
            Assert.That(SceneArtPalette.TryParseHex("FF3A2", out _), Is.False, "5 位应拒绝");
            Assert.That(SceneArtPalette.TryParseHex("FF3A292", out _), Is.False, "7 位应拒绝");
            Assert.That(SceneArtPalette.TryParseHex("FF3A2Z", out _), Is.False, "非 hex 字符应拒绝");
        }

        [Test]
        public void Hex_ParseFailure_FallsBackToMagenta()
        {
            // 品红兜底是**既定契约**（SceneArtPalette.Hex 注释："便于肉眼发现问题，
            // 与 BattleSceneLighting.Hex 同口径"）——固化它，防止有人改成抛异常
            // 让编辑器期构建流程直接崩掉。
            Assert.That(SceneArtPalette.Hex("ZZZZZZ"), Is.EqualTo(Color.magenta));
        }
    }
}
