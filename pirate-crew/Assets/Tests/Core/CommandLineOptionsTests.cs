using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 命令行解析（<see cref="CommandLineOptions"/>）测试：开关/取值/类型化取值/重复取最后/复位。
    /// 用显式 argv 断言（不依赖测试宿主自己的命令行，结果确定）。
    /// </summary>
    public class CommandLineOptionsTests
    {
        [SetUp]
        public void SetUp()
        {
            CommandLineOptions.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CommandLineOptions.Reset();
        }

        [Test]
        public void Parse_FlagWithValue_IsReadable()
        {
            CommandLineOptions.Parse(new[] { "PirateCrew.exe", "-worldMap", "wreck_hymn" });

            Assert.That(CommandLineOptions.Has(ToolFlags.WorldMap), Is.True);
            Assert.That(CommandLineOptions.GetValue(ToolFlags.WorldMap), Is.EqualTo("wreck_hymn"));
        }

        [Test]
        public void Parse_FlagWithoutValue_IsPresentWithNullValue()
        {
            CommandLineOptions.Parse(new[] { "PirateCrew.exe", "-artReviewOut" });

            Assert.That(CommandLineOptions.Has(ToolFlags.ArtReviewOut), Is.True);
            Assert.That(CommandLineOptions.GetValue(ToolFlags.ArtReviewOut), Is.Null,
                "开关存在但没带值时必须能区分于『开关没出现』——不能拿空串糊过去");
        }

        [Test]
        public void Parse_MissingFlag_IsAbsent()
        {
            CommandLineOptions.Parse(new[] { "PirateCrew.exe", "-worldMap", "wreck_hymn" });

            Assert.That(CommandLineOptions.Has(ToolFlags.SceneKitOut), Is.False);
            Assert.That(CommandLineOptions.GetValue(ToolFlags.SceneKitOut), Is.Null);
        }

        [Test]
        public void Parse_TwoAdjacentFlags_NeitherConsumesTheOther()
        {
            CommandLineOptions.Parse(new[] { "app", "-worldMap", "-artReviewOut", "D:/out" });

            Assert.That(CommandLineOptions.GetValue(ToolFlags.WorldMap), Is.Null, "值不能吞掉下一个开关");
            Assert.That(CommandLineOptions.GetValue(ToolFlags.ArtReviewOut), Is.EqualTo("D:/out"));
        }

        [Test]
        public void Parse_NegativeNumericValue_IsAcceptedAsValue()
        {
            // 负数是合法取值（如法线边加成档 -1 = 面转折处压暗），不能被那条"以 - 开头即新开关"
            // 的规则吞掉——否则调用方只看到"参数没生效"，没有任何报错。
            CommandLineOptions.Parse(new[]
            {
                "app", ToolFlags.PixelartNormalEdgeLevel, "-1", ToolFlags.PixelartMainLightLevel, "4",
            });

            Assert.That(CommandLineOptions.TryGetFloat(ToolFlags.PixelartNormalEdgeLevel, out float level), Is.True);
            Assert.That(level, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(CommandLineOptions.TryGetFloat(ToolFlags.PixelartMainLightLevel, out float bands), Is.True);
            Assert.That(bands, Is.EqualTo(4f).Within(0.0001f), "负数值不能吞掉它后面的开关");
        }

        [Test]
        public void Parse_LoneDash_IsStillAMissingValue()
        {
            // 放宽负号规则后，单个 `-`（Unity 的 `-logFile -` = 写 stdout）必须仍是"无值"。
            CommandLineOptions.Parse(new[] { "app", "-logFile", "-" });

            Assert.That(CommandLineOptions.Has("-logFile"), Is.True);
            Assert.That(CommandLineOptions.GetValue("-logFile"), Is.Null);
        }

        [Test]
        public void Parse_RepeatedFlag_LastWins()
        {
            CommandLineOptions.Parse(new[] { "app", "-worldMap", "a_map", "-worldMap", "sunken_gate" });

            Assert.That(CommandLineOptions.GetValue(ToolFlags.WorldMap), Is.EqualTo("sunken_gate"));
        }

        [Test]
        public void Parse_NonFlagTokens_AreIgnored()
        {
            // 松散 token 忽略；旗标（含未登记进 ToolFlags 的）照常落表。
            CommandLineOptions.Parse(new[] { "app.exe", "loose-token", "-someUnknownFlag", "D:/x" });

            Assert.That(CommandLineOptions.Count, Is.EqualTo(1));
            Assert.That(CommandLineOptions.GetValue("-someUnknownFlag"), Is.EqualTo("D:/x"));
        }

        [Test]
        public void Parse_UnityAndEditorNoiseFlags_AreHarmless()
        {
            // 真实播放器命令行里混着 Unity 自己的开关（-logFile / -projectPath 等）：
            // 它们照常落表，但不影响我们关心的那几个开关。
            CommandLineOptions.Parse(new[]
            {
                "app.exe", "-logFile", "player.log", "-batchmode", "-nographics",
                "-artReviewLevel", "2",
            });

            Assert.That(CommandLineOptions.TryGetInt(ToolFlags.ArtReviewLevel, out int level), Is.True);
            Assert.That(level, Is.EqualTo(2));
            Assert.That(CommandLineOptions.GetValue("-logFile"), Is.EqualTo("player.log"));
        }

        [Test]
        public void TryGetInt_InvalidOrMissing_ReturnsFalse()
        {
            CommandLineOptions.Parse(new[] { "app", "-artReviewLevel", "abc", "-oceanDebug", "7" });

            Assert.That(CommandLineOptions.TryGetInt(ToolFlags.ArtReviewLevel, out _), Is.False);
            Assert.That(CommandLineOptions.TryGetInt(ToolFlags.OceanDebug, out int mode), Is.True);
            Assert.That(mode, Is.EqualTo(7));
            Assert.That(CommandLineOptions.TryGetInt("-nope", out _), Is.False);
        }

        [Test]
        public void TryGetFloat_InvalidOrMissing_ReturnsFalse()
        {
            CommandLineOptions.Parse(new[] { "app", "-oceanDebug", "12.5" });

            Assert.That(CommandLineOptions.TryGetFloat(ToolFlags.OceanDebug, out float mode), Is.True);
            Assert.That(mode, Is.EqualTo(12.5f).Within(0.0001f));
            Assert.That(CommandLineOptions.TryGetFloat("-nope", out _), Is.False);
        }

        [Test]
        public void RawArgs_ExposesWholeCommandLine_ForModuleOwnedSwitches()
        {
            // 模块自有开关（如 -ambientTimeOfDay）用自己的目录类解释取值，但输入取这里的原始 argv——
            // 全仓只有本类读 Environment.GetCommandLineArgs。
            string[] args = { "app", "-ambientTimeOfDay", "Dusk" };
            CommandLineOptions.Parse(args);

            Assert.That(CommandLineOptions.RawArgs, Is.EqualTo(args));
        }

        [Test]
        public void Parse_NullOrEmpty_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => CommandLineOptions.Parse(null));
            Assert.That(CommandLineOptions.Count, Is.EqualTo(0));

            Assert.DoesNotThrow(() => CommandLineOptions.Parse(new string[0]));
            Assert.That(CommandLineOptions.Has(ToolFlags.WorldMap), Is.False);
        }

        [Test]
        public void Reset_ClearsPreviousParse()
        {
            CommandLineOptions.Parse(new[] { "app", "-worldMap", "wreck_hymn" });
            Assert.That(CommandLineOptions.Has(ToolFlags.WorldMap), Is.True);

            CommandLineOptions.Reset();
            CommandLineOptions.Parse(new[] { "app" });

            Assert.That(CommandLineOptions.Has(ToolFlags.WorldMap), Is.False);
            Assert.That(CommandLineOptions.Count, Is.EqualTo(0));
        }

        [Test]
        public void Reparse_ReplacesPreviousResult()
        {
            CommandLineOptions.Parse(new[] { "app", "-worldMap", "wreck_hymn" });
            CommandLineOptions.Parse(new[] { "app", "-sceneKitOut", "D:/kit" });

            Assert.That(CommandLineOptions.Has(ToolFlags.WorldMap), Is.False, "重解析必须整表替换");
            Assert.That(CommandLineOptions.Has(ToolFlags.SceneKitOut), Is.True);
        }

        // ------------------------------------------------------------------
        // 登记制与防御性边界（下列用例为补课批新增）
        // ------------------------------------------------------------------

        [Test]
        public void ToolFlags_Registry_FlagsAreUniqueAndWellFormed()
        {
            // 登记制不变量（ToolFlags 类头："单一事实源：不许在业务代码里散落 -flag 字面量"）：
            // 全部常量非空、以 '-' 开头、互不重复——复制粘贴拼错一个开关名，两个 flag
            // 就会各自带着错名字静默落表，消费方永远等不到参数。
            string[] flags = typeof(ToolFlags)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null))
                .ToArray();

            Assert.That(flags, Is.Not.Empty, "登记表不应为空");

            foreach (string flag in flags)
            {
                Assert.That(flag, Is.Not.Empty, "开关名不应为空串");
                Assert.That(flag[0], Is.EqualTo('-'), $"'{flag}' 应以 '-' 开头");
            }

            Assert.That(flags.Distinct().Count(), Is.EqualTo(flags.Length),
                "开关名出现重复（复制粘贴未改值）：" + string.Join(", ", flags));
        }

        [Test]
        public void Parse_NullOrEmptyTokens_AreSkipped()
        {
            // 防御性边界（CommandLineOptions.Parse 的 IsNullOrEmpty 分支）：
            // token 位置与值位置的 null / 空串都不能把解析器带崩——
            // 值位 null 等价"无值"，不能 NRE 也不能把 null 记成值。
            CommandLineOptions.Parse(new[] { null, "", "-artReviewOut", null, "-worldMap", "wreck_hymn" });

            Assert.That(CommandLineOptions.Has(ToolFlags.ArtReviewOut), Is.True);
            Assert.That(CommandLineOptions.GetValue(ToolFlags.ArtReviewOut), Is.Null,
                "值位 null 应按『出现但无值』处理");
            Assert.That(CommandLineOptions.GetValue(ToolFlags.WorldMap), Is.EqualTo("wreck_hymn"));
        }

        [Test]
        public void HasAndGetValue_NullOrEmptyFlag_AreSafe()
        {
            // 防御性边界（CommandLineOptions.cs:58 / 68 的 IsNullOrEmpty 分支）：
            // 调用方传 null / 空串查询必须得到 false / null，而不是内部字典异常。
            CommandLineOptions.Parse(new[] { "app", "-worldMap", "wreck_hymn" });

            Assert.That(CommandLineOptions.Has(null), Is.False);
            Assert.That(CommandLineOptions.Has(""), Is.False);
            Assert.That(CommandLineOptions.GetValue(null), Is.Null);
            Assert.That(CommandLineOptions.GetValue(""), Is.Null);
        }

        [Test]
        public void TryGetInt_NegativeNumericValue_ParsesAsInt()
        {
            // 负数判据（IsNumericLiteral）对整数取值同样生效——除了 float 侧
            // （Parse_NegativeNumericValue_IsAcceptedAsValue），int 侧消费方也要吃得下负值。
            // 消费口径例：ChemPlantOrbitCapture.Install 以 TryGetInt(ToolFlags.OrbitLevel)
            // + level <= 0 拒绝非法关号（pirate-crew/Assets/Scripts/PirateCrew/ArtReview/ChemPlantOrbitCapture.cs:22-25）——
            // 解析层只负责把 "-3" 原样交到调用方手里，裁值权在消费方。
            CommandLineOptions.Parse(new[] { "app", ToolFlags.PixelartNormalEdgeLevel, "-1" });

            Assert.That(CommandLineOptions.TryGetInt(ToolFlags.PixelartNormalEdgeLevel, out int level), Is.True);
            Assert.That(level, Is.EqualTo(-1));
        }
    }
}
