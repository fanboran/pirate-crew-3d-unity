using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="LevelTileMaps"/> 里 33 关行串原文与**源数据**的逐行比对。
    ///
    /// 【数据源】两个都读：
    ///   · <c>external/swf-decompile/levels_all.json</c> —— 33 关关卡 XML 全量导出（权威）；
    ///   · <c>external/swf-decompile/levels_raw.txt</c> —— 早期手工 dump（只含前若干关，
    ///     作为**第二来源**交叉验证：里面出现的每一关行串都必须能在入库数据里原样找到）。
    ///
    /// 【读不到源数据时】<c>external/</c> 不入库（见 AGENTS.md），CI / 纯净检出下文件不存在，
    /// 此时 <see cref="Assert.Ignore"/> 跳过（与 <c>LevelCatalogJsonParityTests</c> 同规矩）。
    ///
    /// 【为什么必须比对】把行串搬进 C# 时任何手滑（少一行、漏一个 <c>:4</c>）都不会破坏编译，
    /// 只会让关卡地形悄悄变形 —— 只有与源数据逐字符比对才抓得住。
    /// </summary>
    [TestFixture]
    public class LevelTileMapsJsonParityTests
    {
        const string JsonRelativePath = "external/swf-decompile/levels_all.json";
        const string RawRelativePath = "external/swf-decompile/levels_raw.txt";

        static readonly Regex LevelTag = new Regex("<level\\s+([^>]*?)>", RegexOptions.Compiled);
        static readonly Regex RowTag = new Regex("<row>(.*?)</row>", RegexOptions.Compiled);
        static readonly Regex AttributeTag = new Regex("([A-Za-z][A-Za-z0-9]*)=\"([^\"]*)\"", RegexOptions.Compiled);

        static Dictionary<string, string> _xmlByLevel;

        static Dictionary<string, string> XmlByLevel
        {
            get
            {
                if (_xmlByLevel == null)
                {
                    string path = FindInRepo(JsonRelativePath);
                    if (path == null)
                    {
                        Assert.Ignore("读不到 " + JsonRelativePath
                            + "（external/ 不入库，纯净检出/CI 下正常缺失）。"
                            + "恢复方式：把 SWF 逆向产物放回仓库根的 external/swf-decompile/。"
                            + "已跳过行串逐关比对。");
                    }

                    _xmlByLevel = ParseFlatStringMap(File.ReadAllText(path, Encoding.UTF8));
                }

                return _xmlByLevel;
            }
        }

        // ------------------------------------------------------------------

        [Test]
        public void AllLevels_RowStrings_MatchSourceJson()
        {
            Dictionary<string, string> xml = XmlByLevel;

            for (int n = 1; n <= LevelTileMaps.LevelCount; n++)
            {
                string key = "level_" + n;
                Assert.IsTrue(xml.ContainsKey(key), "源数据缺少 " + key);

                string source = xml[key];
                Match level = LevelTag.Match(source);
                Assert.IsTrue(level.Success, key + " 的 <level> 标签缺失");

                var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match m in AttributeTag.Matches(level.Groups[1].Value))
                    attrs[m.Groups[1].Value] = m.Groups[2].Value;

                // 尺寸也要对上（宽度/高度的权威来源是 LevelCatalog，这里与源 XML 同时核对）。
                Assert.AreEqual(int.Parse(attrs["width"]), LevelTileMaps.WidthOf(n),
                    key + " 宽度应与源 XML 一致");
                Assert.AreEqual(int.Parse(attrs["height"]), LevelTileMaps.HeightOf(n),
                    key + " 行数应与源 XML 一致");

                MatchCollection rows = RowTag.Matches(source);
                string[] embedded = LevelTileMaps.RawRows(n);
                Assert.AreEqual(rows.Count, embedded.Length, key + " 行数应与源 XML 一致");

                for (int i = 0; i < embedded.Length; i++)
                {
                    Assert.AreEqual(rows[i].Groups[1].Value, embedded[i],
                        key + " 第 " + i + " 行行串与源 XML 不一致（入库数据被手改过？）");
                }
            }
        }

        [Test]
        public void RawDumpRows_AreAllPresentInEmbeddedData()
        {
            string path = FindInRepo(RawRelativePath);
            if (path == null)
                Assert.Ignore("读不到 " + RawRelativePath + "（external/ 不入库），跳过第二来源交叉验证。");

            // levels_raw.txt 是 Python repr 风格的行串 dump：'<level ...>...</level>'\n\n...
            // 直接抽出所有 <level ...>...</level> 片段，逐段与入库行串比对。
            string text = File.ReadAllText(path, Encoding.UTF8);
            MatchCollection levels = Regex.Matches(text, "<level\\s[^>]*>(?s:.*?)</level>",
                RegexOptions.Compiled);
            Assert.Greater(levels.Count, 0, "levels_raw.txt 里应至少有一段关卡 XML");

            int matched = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                MatchCollection rows = RowTag.Matches(levels[i].Value);
                var rowStrings = new List<string>(rows.Count);
                for (int r = 0; r < rows.Count; r++)
                    rowStrings.Add(rows[r].Groups[1].Value);

                bool found = false;
                for (int n = 1; n <= LevelTileMaps.LevelCount && !found; n++)
                {
                    string[] embedded = LevelTileMaps.RawRows(n);
                    if (embedded.Length != rowStrings.Count)
                        continue;

                    bool same = true;
                    for (int r = 0; r < embedded.Length; r++)
                    {
                        if (embedded[r] != rowStrings[r])
                        {
                            same = false;
                            break;
                        }
                    }

                    found = same;
                }

                Assert.IsTrue(found,
                    "levels_raw.txt 第 " + (i + 1) + " 段（" + rowStrings.Count
                    + " 行）在入库行串里找不到完全一致的一关 —— 第二来源不一致");
                matched++;
            }

            TestContext.Progress.WriteLine("[LevelTileMaps] levels_raw.txt 交叉验证通过，" + matched + " 段");
        }

        // ------------------------------------------------------------------
        // 源数据装载（与 LevelCatalogJsonParityTests 同一套上溯查找）
        // ------------------------------------------------------------------

        /// <summary>从工作目录 / BaseDirectory 出发逐级上溯，找仓库根下的相对路径。</summary>
        static string FindInRepo(string relativePath)
        {
            var starts = new List<string>(2);
            TryAdd(starts, () => Environment.CurrentDirectory);
            TryAdd(starts, () => AppContext.BaseDirectory);

            foreach (string start in starts)
            {
                if (string.IsNullOrEmpty(start))
                    continue;

                DirectoryInfo dir;
                try { dir = new DirectoryInfo(start); }
                catch (ArgumentException) { continue; }

                for (int up = 0; dir != null && up < 12; up++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        static void TryAdd(List<string> list, Func<string> getter)
        {
            try
            {
                string value = getter();
                if (!string.IsNullOrEmpty(value))
                    list.Add(value);
            }
            catch (Exception)
            {
                // 取不到候选起点就算了
            }
        }

        /// <summary>levels_all.json 是 {"level_1": "&lt;level ...&gt;...", ...} 的扁平字符串表。</summary>
        static Dictionary<string, string> ParseFlatStringMap(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            MatchCollection pairs = Regex.Matches(json, "\"(level_\\d+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Compiled);

            for (int i = 0; i < pairs.Count; i++)
            {
                string key = pairs[i].Groups[1].Value;
                string value = Regex.Unescape(pairs[i].Groups[2].Value);
                result[key] = value;
            }

            return result;
        }
    }
}
