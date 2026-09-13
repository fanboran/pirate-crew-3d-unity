using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// LevelCatalog 与<b>原始关卡 XML 转写数据</b>的逐字段比对。
    ///
    /// 【数据源】<c>external/swf-decompile/levels_all.json</c>——33 关关卡 XML 的全量逆向导出，
    /// 即静态逆向文档 §7.2 指明的布阵数据源（§7.2 表格本身只有人数与武器池概览）。
    ///
    /// 【为什么要比对】LevelCatalog 的值是从该 JSON 人工/半自动转写的，任何手滑（漏一个单位、
    /// 抄错一个坐标、把 count=1 写成 10）都不会破坏"编译"或"通用不变量"，只能靠与源数据逐字段比对抓出来。
    ///
    /// 【读不到源数据时】<c>external/</c> 不入库（见 AGENTS.md），CI / 纯净检出环境下文件不存在，
    /// 此时用 <see cref="Assert.Ignore"/> 跳过（而不是失败），并在提示里写明如何恢复该数据。
    ///
    /// 【无头验证台】harness 的 ProjectRoot 指向 <c>pirate-crew/</c>，测试工作目录在
    /// <c>external/harness-*/bin/Debug/net8.0</c>，因此这里从工作目录与
    /// <see cref="AppContext.BaseDirectory"/> 两处出发逐级上溯找 <c>external/swf-decompile/levels_all.json</c>。
    /// </summary>
    public class LevelCatalogJsonParityTests
    {
        const string JsonRelativePath = "external/swf-decompile/levels_all.json";

        /// <summary>§5.5 setWeapons 明确排除的属性键（其余属性键都是武器 id）。</summary>
        static readonly HashSet<string> NonWeaponAttrs =
            new HashSet<string>(StringComparer.Ordinal) { "type", "x", "y", "luck", "maxChests" };

        static readonly Regex LevelTag = new Regex("<level\\s+([^>]*?)>", RegexOptions.Compiled);
        static readonly Regex ObjTag = new Regex("<obj\\s+([^>]*?)/>", RegexOptions.Compiled);
        static readonly Regex AttributeRegex = new Regex("([A-Za-z][A-Za-z0-9]*)=\"([^\"]*)\"", RegexOptions.Compiled);

        static Dictionary<string, string> _xmlByLevel;
        static Dictionary<string, WeaponId> _nameToId;

        // ------------------------------------------------------------------
        // 源数据装载
        // ------------------------------------------------------------------

        /// <summary>关卡键名（level_1 …）→ 该关的原始 XML 串。</summary>
        static Dictionary<string, string> XmlByLevel
        {
            get
            {
                if (_xmlByLevel == null)
                {
                    string path = FindLevelsJson();
                    if (path == null)
                    {
                        Assert.Ignore("读不到 " + JsonRelativePath
                            + "（external/ 不入库，纯净检出/CI 下正常缺失）。"
                            + "恢复方式：把 SWF 逆向产物放回仓库根的 external/swf-decompile/。已跳过 JSON 逐关比对。");
                    }

                    _xmlByLevel = ParseFlatStringMap(File.ReadAllText(path, Encoding.UTF8));
                }

                return _xmlByLevel;
            }
        }

        static Dictionary<string, WeaponId> NameToId
        {
            get
            {
                if (_nameToId == null)
                {
                    _nameToId = new Dictionary<string, WeaponId>(StringComparer.Ordinal);
                    foreach (WeaponStats stats in WeaponCatalog.All)
                        _nameToId[stats.DisplayName] = stats.Id;
                }

                return _nameToId;
            }
        }

        /// <summary>从工作目录 / BaseDirectory 出发逐级上溯，找仓库根下的 levels_all.json。</summary>
        static string FindLevelsJson()
        {
            var starts = new List<string>(3);
            TryAdd(starts, SafeGetCurrentDirectory);
            TryAdd(starts, SafeGetBaseDirectory);

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
                        JsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
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
                // 取不到候选起点就算了，交给下一个候选
            }
        }

        static string SafeGetCurrentDirectory()
        {
            return Environment.CurrentDirectory;
        }

        static string SafeGetBaseDirectory()
        {
            return AppContext.BaseDirectory;
        }

        // ------------------------------------------------------------------
        // 极简 JSON 解析（levels_all.json 是 {"level_1": "<xml/>", …} 的扁平字符串表）
        // ------------------------------------------------------------------

        static Dictionary<string, string> ParseFlatStringMap(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            SkipWhitespace(json, ref i);
            Expect(json, ref i, '{');
            SkipWhitespace(json, ref i);

            if (i < json.Length && json[i] == '}')
                return map;

            while (true)
            {
                SkipWhitespace(json, ref i);
                string key = ReadJsonString(json, ref i);
                SkipWhitespace(json, ref i);
                Expect(json, ref i, ':');
                SkipWhitespace(json, ref i);
                map[key] = ReadJsonString(json, ref i);

                SkipWhitespace(json, ref i);
                if (i >= json.Length)
                    throw new FormatException("levels_all.json 意外结束（位置 " + i + "）");
                if (json[i] == ',') { i++; continue; }
                if (json[i] == '}') { i++; break; }
                throw new FormatException("levels_all.json 位置 " + i + " 出现意外字符 '" + json[i] + "'");
            }

            return map;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c)
                throw new FormatException("levels_all.json 期望 '" + c + "'，实际位置 " + i);
            i++;
        }

        static string ReadJsonString(string s, ref int i)
        {
            Expect(s, ref i, '"');

            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length)
                    throw new FormatException("levels_all.json 字符串未闭合");

                char c = s[i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length)
                    throw new FormatException("levels_all.json 转义序列不完整");

                char escape = s[i++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                            throw new FormatException("levels_all.json \\u 转义不完整");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default:
                        throw new FormatException("levels_all.json 出现未知转义 \\" + escape);
                }
            }
        }

        // ------------------------------------------------------------------
        // XML 侧取值
        // ------------------------------------------------------------------

        static string Xml(int levelNumber)
        {
            string xml;
            Assert.That(XmlByLevel.TryGetValue("level_" + levelNumber, out xml), Is.True,
                "levels_all.json 里没有 level_" + levelNumber);
            return xml;
        }

        static Dictionary<string, string> AttributesOf(string tagBody)
        {
            var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in AttributeRegex.Matches(tagBody))
                attrs[m.Groups[1].Value] = m.Groups[2].Value;
            return attrs;
        }

        static Dictionary<string, string> XmlLevelAttrs(int levelNumber)
        {
            Match m = LevelTag.Match(Xml(levelNumber));
            Assert.That(m.Success, Is.True, "level_" + levelNumber + " 的 XML 里没有 <level> 标签");
            return AttributesOf(m.Groups[1].Value);
        }

        static List<Dictionary<string, string>> XmlObjs(int levelNumber)
        {
            var objs = new List<Dictionary<string, string>>();
            foreach (Match m in ObjTag.Matches(Xml(levelNumber)))
                objs.Add(AttributesOf(m.Groups[1].Value));
            return objs;
        }

        static bool IsUnitObj(Dictionary<string, string> obj)
        {
            string type;
            if (!obj.TryGetValue("type", out type))
                return false;
            return type != "potentialWeapons" && type != "water";
        }

        static List<Dictionary<string, string>> XmlUnitObjs(int levelNumber)
        {
            var units = new List<Dictionary<string, string>>();
            foreach (Dictionary<string, string> obj in XmlObjs(levelNumber))
            {
                if (IsUnitObj(obj))
                    units.Add(obj);
            }

            return units;
        }

        static List<Dictionary<string, string>> XmlObjsOfType(int levelNumber, string type)
        {
            var found = new List<Dictionary<string, string>>();
            foreach (Dictionary<string, string> obj in XmlObjs(levelNumber))
            {
                string t;
                if (obj.TryGetValue("type", out t) && t == type)
                    found.Add(obj);
            }

            return found;
        }

        static float XmlWaterTileY(int levelNumber)
        {
            List<Dictionary<string, string>> waters = XmlObjsOfType(levelNumber, "water");
            Assert.That(waters.Count, Is.GreaterThan(0), "level_" + levelNumber + " 的 XML 里没有 water 对象");
            return float.Parse(waters[waters.Count - 1]["y"], CultureInfo.InvariantCulture);
        }

        /// <summary>potentialWeapons 对象；同关多个时取最后一个（§5.5 是赋值语义，后写覆盖前写）。</summary>
        static Dictionary<string, string> XmlPotentialWeapons(int levelNumber)
        {
            List<Dictionary<string, string>> pools = XmlObjsOfType(levelNumber, "potentialWeapons");
            Assert.That(pools.Count, Is.GreaterThan(0),
                "level_" + levelNumber + " 的 XML 里没有 potentialWeapons 对象");
            return pools[pools.Count - 1];
        }

        static WeaponId IdOf(string xmlWeaponKey)
        {
            Assert.That(NameToId.ContainsKey(xmlWeaponKey), Is.True,
                "关卡 XML 出现 WeaponCatalog 未收录的武器属性键: " + xmlWeaponKey);
            return NameToId[xmlWeaponKey];
        }

        static int CountOf(string xmlValue)
        {
            return int.Parse(xmlValue, CultureInfo.InvariantCulture);
        }

        /// <summary>XML 对象上的武器键值对签名（排除 x/y/type/luck/maxChests）。</summary>
        static List<string> XmlWeaponSignatures(Dictionary<string, string> obj)
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, string> kv in obj)
            {
                if (NonWeaponAttrs.Contains(kv.Key))
                    continue;
                list.Add(IdOf(kv.Key) + ":" + CountOf(kv.Value));
            }

            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ------------------------------------------------------------------
        // 目录侧取值
        // ------------------------------------------------------------------

        static List<string> CatalogWeaponSignatures(IReadOnlyList<WeaponStack> stacks)
        {
            var list = new List<string>(stacks.Count);
            foreach (WeaponStack stack in stacks)
                list.Add(stack.id + ":" + stack.count);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>单位的可比较签名（多集合语义：调用方自行排序）。</summary>
        static string UnitSignature(string type, int team, int gridX, int gridY, int luck, List<string> weapons)
        {
            weapons.Sort(StringComparer.Ordinal);
            return type + " | team=" + team + " | (" + gridX + "," + gridY + ") | luck=" + luck
                + " | " + string.Join(", ", weapons.ToArray());
        }

        static List<string> CatalogUnitSignatures(LevelData level)
        {
            var list = new List<string>(level.Units.Count);
            foreach (LevelUnit unit in level.Units)
            {
                list.Add(UnitSignature(unit.typeName, unit.teamIndex, unit.gridX, unit.gridY, unit.luck,
                    CatalogWeaponSignatures(unit.initialWeapons)));
            }

            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static List<string> XmlUnitSignatures(int levelNumber)
        {
            var list = new List<string>();
            foreach (Dictionary<string, string> obj in XmlUnitObjs(levelNumber))
            {
                string type = obj["type"];
                list.Add(UnitSignature(type, CrewCatalog.TeamIndexOf(type),
                    int.Parse(obj["x"], CultureInfo.InvariantCulture),
                    int.Parse(obj["y"], CultureInfo.InvariantCulture),
                    int.Parse(obj["luck"], CultureInfo.InvariantCulture),
                    XmlWeaponSignatures(obj)));
            }

            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static int CountTeam(LevelData level, int teamIndex)
        {
            int n = 0;
            foreach (LevelUnit unit in level.Units)
            {
                if (unit.teamIndex == teamIndex)
                    n++;
            }

            return n;
        }

        /// <summary>把某队单位按种类聚合为 "type:count" 升序清单，便于与文档 §7.2 表格核对。</summary>
        static List<string> TypeCounts(LevelData level, int teamIndex)
        {
            var order = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (LevelUnit unit in level.Units)
            {
                if (unit.teamIndex != teamIndex)
                    continue;
                if (!counts.ContainsKey(unit.typeName))
                {
                    counts[unit.typeName] = 0;
                    order.Add(unit.typeName);
                }

                counts[unit.typeName]++;
            }

            order.Sort(StringComparer.Ordinal);
            var list = new List<string>(order.Count);
            foreach (string type in order)
                list.Add(type + ":" + counts[type]);
            return list;
        }

        // ------------------------------------------------------------------
        // 测试
        // ------------------------------------------------------------------

        [Test]
        public void JsonSource_Exists_AndCoversAll33Levels()
        {
            Assert.That(XmlByLevel.Count, Is.EqualTo(33), "levels_all.json 应包含 33 关");
            for (int n = 1; n <= 33; n++)
                Assert.That(XmlByLevel.ContainsKey("level_" + n), Is.True, "levels_all.json 缺少 level_" + n);
        }

        [Test]
        public void EachLevel_UnitCount_MatchesXmlObjCount()
        {
            for (int n = 1; n <= 33; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                List<Dictionary<string, string>> objects = XmlObjs(n);
                List<Dictionary<string, string>> units = XmlUnitObjs(n);

                Assert.That(level.Units.Count, Is.EqualTo(units.Count),
                    "关卡 " + n + "：目录里 " + level.Units.Count + " 个单位，XML 里 " + units.Count
                    + " 个 <obj>（XML 共 " + objects.Count + " 个 obj，含 potentialWeapons/water）");

                int red = 0;
                int blue = 0;
                foreach (Dictionary<string, string> obj in units)
                {
                    if (CrewCatalog.TeamIndexOf(obj["type"]) == CrewCatalog.RedTeamIndex) red++;
                    else blue++;
                }

                Assert.That(CountTeam(level, CrewCatalog.RedTeamIndex), Is.EqualTo(red),
                    "关卡 " + n + " 红队人数与 XML 不符");
                Assert.That(CountTeam(level, CrewCatalog.BlueTeamIndex), Is.EqualTo(blue),
                    "关卡 " + n + " 蓝队人数与 XML 不符");
            }
        }

        [Test]
        public void EachLevel_Metadata_MatchesXml()
        {
            for (int n = 1; n <= 33; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                Dictionary<string, string> attrs = XmlLevelAttrs(n);

                Assert.That(level.Name, Is.EqualTo("level_" + n), "关卡 " + n + " 的 name");
                Assert.That(level.WidthTiles, Is.EqualTo(int.Parse(attrs["width"], CultureInfo.InvariantCulture)),
                    "关卡 " + n + " 的 widthTiles");
                Assert.That(level.HeightTiles, Is.EqualTo(int.Parse(attrs["height"], CultureInfo.InvariantCulture)),
                    "关卡 " + n + " 的 heightTiles");
                Assert.That(level.OriginalXmlPlayers, Is.EqualTo(int.Parse(attrs["players"], CultureInfo.InvariantCulture)),
                    "关卡 " + n + " 的 originalXmlPlayers");
                Assert.That(level.WaterTileY, Is.EqualTo(XmlWaterTileY(n)), "关卡 " + n + " 的 waterTileY");
                Assert.That(level.WaterY, Is.EqualTo(level.WaterTileY * 32f), "关卡 " + n + " 的 WaterY 换算");

                // sourceXmlMaxChests：XML 有该属性就用原值，缺省记 1（沿用 level_27 的缺省口径）
                Dictionary<string, string> pool = XmlPotentialWeapons(n);
                string xmlMax;
                int expectedMax = pool.TryGetValue("maxChests", out xmlMax) ? CountOf(xmlMax) : 1;
                Assert.That(level.SourceXmlMaxChests, Is.EqualTo(expectedMax),
                    "关卡 " + n + " 的 sourceXmlMaxChests（XML 值 " + (xmlMax ?? "缺省") + "）");
            }
        }

        [Test]
        public void EachLevel_PotentialWeapons_MatchXml()
        {
            for (int n = 1; n <= 33; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                List<string> expected = XmlWeaponSignatures(XmlPotentialWeapons(n));
                List<string> actual = CatalogWeaponSignatures(level.PotentialWeapons);

                Assert.That(actual, Is.EqualTo(expected),
                    "关卡 " + n + " 的空投武器池与 XML potentialWeapons 不符");
            }
        }

        [Test]
        public void EachLevel_Units_MatchXml_UnitByUnit()
        {
            for (int n = 1; n <= 33; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                List<string> expected = XmlUnitSignatures(n);
                List<string> actual = CatalogUnitSignatures(level);

                Assert.That(actual, Is.EqualTo(expected),
                    "关卡 " + n + " 的单位列表（种类/队伍/坐标/luck/初始武器）与 XML 逐单位比对不符");
            }
        }

        [Test]
        public void EachUnit_GridCoordinates_WithinLevelBounds()
        {
            for (int n = 1; n <= 33; n++)
            {
                LevelData level = LevelCatalog.Get(n);

                foreach (LevelUnit unit in level.Units)
                {
                    Assert.That(unit.gridX, Is.InRange(0, level.WidthTiles - 1),
                        "关卡 " + n + " 单位 " + unit.typeName + " 的 gridX 越界");
                    Assert.That(unit.gridY, Is.InRange(0, level.HeightTiles - 1),
                        "关卡 " + n + " 单位 " + unit.typeName + " 的 gridY 越界");
                }

                // 原始 XML 自身也应在界内（防止坐标来源就错了）
                foreach (Dictionary<string, string> obj in XmlUnitObjs(n))
                {
                    Assert.That(int.Parse(obj["x"], CultureInfo.InvariantCulture),
                        Is.InRange(0, level.WidthTiles - 1), "level_" + n + " XML 里 " + obj["type"] + " 的 x 越界");
                    Assert.That(int.Parse(obj["y"], CultureInfo.InvariantCulture),
                        Is.InRange(0, level.HeightTiles - 1), "level_" + n + " XML 里 " + obj["type"] + " 的 y 越界");
                }
            }
        }

        [Test]
        public void AllWeaponIds_AreLegal()
        {
            for (int n = 1; n <= 33; n++)
            {
                LevelData level = LevelCatalog.Get(n);

                foreach (WeaponStack stack in level.PotentialWeapons)
                {
                    Assert.That(WeaponCatalog.TryGet(stack.id, out _), Is.True,
                        "关卡 " + n + " 空投池引用了非法武器 id: " + stack.id);
                    Assert.That(stack.count, Is.GreaterThan(0), "关卡 " + n + " 空投池出现非正件数");
                }

                foreach (LevelUnit unit in level.Units)
                {
                    foreach (WeaponStack stack in unit.initialWeapons)
                    {
                        Assert.That(WeaponCatalog.TryGet(stack.id, out _), Is.True,
                            "关卡 " + n + " 单位 " + unit.typeName + " 引用了非法武器 id: " + stack.id);
                        Assert.That(stack.count, Is.GreaterThan(0),
                            "关卡 " + n + " 单位 " + unit.typeName + " 出现非正件数");
                    }
                }

                // XML 侧也只允许出现 WeaponCatalog 收录的武器键（IdOf 内部会断言）
                XmlWeaponSignatures(XmlPotentialWeapons(n));
                foreach (Dictionary<string, string> obj in XmlUnitObjs(n))
                    XmlWeaponSignatures(obj);
            }
        }

        /// <summary>
        /// 静态逆向文档 §7.2「若干代表关」表逐行核对（红队/蓝队人数与兵种构成、空投池、水面 tile y）。
        /// 该表是文档里唯一写死的关卡配置基准，作为 JSON 之外的第二个独立参照。
        /// </summary>
        [Test]
        public void RepresentativeLevels_MatchDoc72Table()
        {
            // 关卡, 红队人数, 蓝队人数, 红队构成, 蓝队构成, 空投池, 水面 tile y
            AssertRepresentative(1, 5, 3,
                new[] { "redPirate:4", "redPirateCaptain:1" },
                new[] { "cabinBoy:2", "cabinBoyCaptain:1" },
                new[] { "Dynamite:10" }, 14f);

            AssertRepresentative(2, 7, 7,
                new[] { "redPirate:6", "redPirateCaptain:1" },
                new[] { "squid:7" },
                new[] { "Anchor:1", "CherryBomb:10", "ParachuteBomb:10" }, 26f);

            AssertRepresentative(4, 6, 6,
                new[] { "redPirate:5", "redPirateCaptain:1" },
                new[] { "soldier:5", "soldierCaptain:1" },
                new[]
                {
                    "Dynamite:5", "GunpowderBarrel:2", "ParachuteBomb:2",
                    "PiecesOfEight:5", "Seagull:1", "WoodenCrate:2",
                }, 17f);

            AssertRepresentative(7, 2, 1,
                new[] { "redPirate:1", "redPirateCaptain:1" },
                new[] { "bossGuy:1" },
                new[] { "Banana:1", "CherryBomb:9" }, 9f);

            AssertRepresentative(8, 5, 5,
                new[] { "redPirate:4", "redPirateCaptain:1" },
                new[] { "parrot:5" },
                AllFifteenWeaponsOnce(), 31f);

            AssertRepresentative(15, 7, 1,
                new[] { "redPirate:6", "redPirateCaptain:1" },
                new[] { "bossGuyZombie:1" },
                new[] { "CherryBomb:10", "Dynamite:1", "RumBottle:1" }, 17f);

            AssertRepresentative(21, 6, 6,
                new[] { "redPirate:5", "redPirateCaptain:1" },
                new[] { "bluePirate:5", "bluePirateCaptain:1" },
                new[]
                {
                    "Anchor:10", "Banana:10", "Boulder:10", "ParachuteBomb:10",
                    "Seagull:10", "TidalWave:10", "VoodooDoll:10",
                }, 32f);

            AssertRepresentative(27, 1, 1,
                new[] { "redPirateCaptain:1" },
                new[] { "bluePirateCaptain:1" },
                new[]
                {
                    "Banana:5", "GunpowderBarrel:5", "Mine:5",
                    "ParachuteBomb:5", "RumBottle:5", "WoodenCrate:5",
                }, 19f);

            AssertRepresentative(33, 8, 8,
                new[] { "redPirate:7", "redPirateCaptain:1" },
                new[] { "bluePirate:7", "bluePirateCaptain:1" },
                AllFifteenWeaponsOnce(), 30f);
        }

        /// <summary>§7.2 表格里「全部 15 种武器各 1」的等价展开（15 种 = 17 种去掉 cannonball 与 sweepingFlame）。</summary>
        static string[] AllFifteenWeaponsOnce()
        {
            return new[]
            {
                "Anchor:1", "Banana:1", "Boulder:1", "Cannon:1", "CherryBomb:1", "Dynamite:1",
                "GunpowderBarrel:1", "Mine:1", "ParachuteBomb:1", "PiecesOfEight:1", "RumBottle:1",
                "Seagull:1", "TidalWave:1", "VoodooDoll:1", "WoodenCrate:1",
            };
        }

        static void AssertRepresentative(int levelNumber, int redCount, int blueCount,
            string[] redTypes, string[] blueTypes, string[] pool, float waterTileY)
        {
            LevelData level = LevelCatalog.Get(levelNumber);

            Assert.That(CountTeam(level, CrewCatalog.RedTeamIndex), Is.EqualTo(redCount),
                "关卡 " + levelNumber + " 红队人数与 §7.2 代表表不符");
            Assert.That(CountTeam(level, CrewCatalog.BlueTeamIndex), Is.EqualTo(blueCount),
                "关卡 " + levelNumber + " 蓝队人数与 §7.2 代表表不符");
            Assert.That(TypeCounts(level, CrewCatalog.RedTeamIndex), Is.EqualTo(redTypes),
                "关卡 " + levelNumber + " 红队兵种构成与 §7.2 代表表不符");
            Assert.That(TypeCounts(level, CrewCatalog.BlueTeamIndex), Is.EqualTo(blueTypes),
                "关卡 " + levelNumber + " 蓝队兵种构成与 §7.2 代表表不符");

            var expectedPool = new List<string>(pool);
            expectedPool.Sort(StringComparer.Ordinal);
            Assert.That(CatalogWeaponSignatures(level.PotentialWeapons), Is.EqualTo(expectedPool),
                "关卡 " + levelNumber + " 空投武器池与 §7.2 代表表不符");

            Assert.That(level.WaterTileY, Is.EqualTo(waterTileY), "关卡 " + levelNumber + " 水面 tile y 与 §7.2 代表表不符");
        }
    }
}
