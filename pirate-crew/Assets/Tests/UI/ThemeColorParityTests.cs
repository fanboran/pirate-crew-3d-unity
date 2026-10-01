using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests.UI
{
    /// <summary>
    /// theme.xml 色值**手抄漂移防线**：theme.xml（<c>Assets/Art/Sprites/UI/Aseprite/theme.xml</c>，
    /// colors 段 = theme.xml:35-116）是 UI 配色唯一权威；代码里逐处手抄的 Color32 字面量在此
    /// 逐条对拍——手抄值 == theme.xml 解析值（按 color id）。对拍表（22 处）：
    /// <list type="bullet">
    /// <item>PixelSkin.Theme 13 处（PixelSkin.cs:255-279，public 直读）</item>
    /// <item>AseMenuKit 8 处（AseMenuKit.cs:83-90，internal，无 InternalsVisibleTo → 反射）</item>
    /// <item>AseEntry 1 处（AseEntry.cs:82 ColSuffix，private → 反射）</item>
    /// </list>
    ///
    /// 【手抄值从源码常量读出】表里的期望值通过直读/反射取自被测常量本身，测试不二次手抄——
    /// 否则防线自身就成了又一份会漂的手抄。常量被移动/改名时表跟着改（读不到即红）。
    ///
    /// 【双域可跑】工程根三级回退定位（环境变量 PIRATECREW_PROJECT_ROOT → 反射
    /// Application.dataPath → BaseDirectory 上溯，手法照抄 Tests/Audio/Game2AudioPortTests.cs:342-384；
    /// 反射包 icall：无头域直接调 icall 会在 JIT 编译期炸出 try/catch 拦不住的 SecurityException）+
    /// System.IO 读文件 + <see cref="AseThemeLayers.ParseColorsFromText"/> 解析——全程纯 C#，
    /// harness 纯 dotnet 与 Unity EditMode 跑同一份代码。
    ///
    /// 【不钉没手抄的】colors 段 80+ 个 token 只对拍被手抄的 22 个 id——没被引用的 token
    /// 漂了不影响任何像素，全量对拍无意义。
    ///
    /// 【漂移处置】对拍红 = 手抄与权威脱钩：测试只把差异亮出来（两边十六进制都在失败消息里，
    /// 手抄出处带行号），**改哪边由协调者按「theme.xml 是唯一权威」裁决**，测试不代行、不改手抄值。
    /// </summary>
    public sealed class ThemeColorParityTests
    {
        /// <summary>对拍表行数（PixelSkin.Theme 13 + AseMenuKit 8 + AseEntry 1）。</summary>
        const int HandCopiedCount = 22;

        /// <summary>一条手抄对拍：源码常量（含出处行号）→ theme.xml color id。</summary>
        private sealed class HandCopied
        {
            public string Owner;      // 常量所在类型（失败定位用）
            public string Field;      // 常量名
            public string TokenId;    // theme.xml colors 段 id
            public string SourceRef;  // 手抄出处（文件:行号）
            public Color32 Value;     // 手抄值（运行时从源码常量读出，不二次手抄）
        }

        // ------------------------------------------------------------------
        // 对拍主断言
        // ------------------------------------------------------------------

        [Test]
        public void ThemeXml_ColorsTable_ParsesViaTextSeam()
        {
            // 冒烟：权威文件定位得到、可解析、规模对得上（对拍至少要 22 个 id）。
            IReadOnlyDictionary<string, Color32> colors = LoadThemeColors();
            Assert.GreaterOrEqual(colors.Count, HandCopiedCount,
                "theme.xml colors 段只解析出 {0} 个 token——文件或解析缝损坏。", colors.Count);
        }

        [Test]
        public void HandCopiedColors_All22_MatchThemeXmlTokens()
        {
            IReadOnlyDictionary<string, Color32> colors = LoadThemeColors();
            List<HandCopied> table = BuildHandCopiedTable();
            Assert.AreEqual(HandCopiedCount, table.Count,
                "对拍表应钉住全部 22 处手抄（PixelSkin.Theme 13 + AseMenuKit 8 + AseEntry 1）；"
                + "增删手抄常量时同步本表，别让防线出现盲区。");

            // 逐条累积失败再一次报：一次看清所有漂移点。
            // （不用 Assert.Multiple——Unity 内置 NUnit 版本无该 API，兼容写法。）
            var drifts = new List<string>();
            foreach (HandCopied row in table)
            {
                if (!colors.TryGetValue(row.TokenId, out Color32 authoritative))
                {
                    drifts.Add(string.Format(
                        "{0}.{1}（出处 {2}）对拍不到 theme.xml colors[\"{3}\"]"
                        + "——id 拼错，或权威文件删了该 token 而手抄侧没跟进。",
                        row.Owner, row.Field, row.SourceRef, row.TokenId));
                    continue;
                }
                if (authoritative.r != row.Value.r || authoritative.g != row.Value.g
                    || authoritative.b != row.Value.b || authoritative.a != row.Value.a)
                {
                    drifts.Add(string.Format(
                        "手抄漂移：{0}.{1}（{2}）手抄 {3} ≠ theme.xml[\"{4}\"] 权威 {5}。"
                        + "改哪边由协调者按「theme.xml 是唯一权威」裁决，本测试只亮差异。",
                        row.Owner, row.Field, row.SourceRef, Hex(row.Value), row.TokenId, Hex(authoritative)));
                }
            }

            Assert.IsEmpty(drifts, "theme.xml 对拍差异（共 " + drifts.Count + " 处）：\n" + string.Join("\n", drifts));
        }

        /// <summary>
        /// Unity 域加验：<see cref="AseThemeLayers.TryGetColor"/>（Resources 同步副本路径）与
        /// 文本解析缝（盘上权威 theme.xml）对同一 id 给出同值——两源任一过期/漂移即红
        /// （副本过期按提示跑「菜单 PirateCrew/同步 Aseprite widgets 资产」）。
        /// harness 纯 dotnet 域自动 Ignore（EnsureParsed 的 Resources.Load 脱离 Unity 运行时不可调）。
        /// </summary>
        [Test]
        public void TryGetColor_ByTokenId_MatchesTextSeam_InUnityDomain()
        {
            if (!IsUnityRuntimeDomain())
                Assert.Ignore("非 Unity 运行时（无头验证台纯 dotnet）：TryGetColor 走 Resources.Load 不可调，"
                    + "权威对拍由文本解析缝分担（HandCopiedColors_All22_MatchThemeXmlTokens）。");

            IReadOnlyDictionary<string, Color32> colors = LoadThemeColors();
            var mismatches = new List<string>();
            foreach (HandCopied row in BuildHandCopiedTable())
            {
                bool hit = AseThemeLayers.TryGetColor(row.TokenId, out Color32 viaApi);
                if (!hit)
                {
                    mismatches.Add("TryGetColor(\"" + row.TokenId + "\") 未命中——Resources/AseWidgets/theme.xml.txt "
                        + "同步副本缺失，跑菜单 PirateCrew/同步 Aseprite widgets 资产后重试。");
                    continue;
                }
                if (colors.TryGetValue(row.TokenId, out Color32 authoritative))
                {
                    if (authoritative.r != viaApi.r || authoritative.g != viaApi.g
                        || authoritative.b != viaApi.b || authoritative.a != viaApi.a)
                    {
                        mismatches.Add(string.Format(
                            "TryGetColor(\"{0}\") vs 盘上权威 theme.xml 两源不一致：{1} ≠ {2}。"
                            + "Resources 同步副本与权威 theme.xml 漂移，重跑同步菜单后仍红则升级协调者。",
                            row.TokenId, Hex(viaApi), Hex(authoritative)));
                    }
                }
            }

            Assert.IsEmpty(mismatches,
                "TryGetColor 双源差异（共 " + mismatches.Count + " 处）：\n" + string.Join("\n", mismatches));
        }

        // ------------------------------------------------------------------
        // 对拍表（22 处；值从源码常量读出，出处行号写死便于定位）
        // ------------------------------------------------------------------
       
        static List<HandCopied> BuildHandCopiedTable()
        {
            var table = new List<HandCopied>
            {
                // ---- PixelSkin.Theme（PixelSkin.cs:252-280；public static readonly，直读）----
                Row("PixelSkin.Theme", "Text",           "text",                "PixelSkin.cs:255", PixelSkin.Theme.Text),
                Row("PixelSkin.Theme", "TextSelected",   "button_selected_text","PixelSkin.cs:257", PixelSkin.Theme.TextSelected),
                Row("PixelSkin.Theme", "Face",           "face",                "PixelSkin.cs:259", PixelSkin.Theme.Face),
                Row("PixelSkin.Theme", "Background",     "background",          "PixelSkin.cs:261", PixelSkin.Theme.Background),
                Row("PixelSkin.Theme", "Disabled",       "disabled",            "PixelSkin.cs:263", PixelSkin.Theme.Disabled),
                Row("PixelSkin.Theme", "HotFace",        "check_hot_face",      "PixelSkin.cs:265", PixelSkin.Theme.HotFace),
                Row("PixelSkin.Theme", "Selected",       "selected",            "PixelSkin.cs:267", PixelSkin.Theme.Selected),
                Row("PixelSkin.Theme", "SelectedText",   "selected_text",       "PixelSkin.cs:269", PixelSkin.Theme.SelectedText),
                Row("PixelSkin.Theme", "SeparatorLabel", "separator_label",     "PixelSkin.cs:271", PixelSkin.Theme.SeparatorLabel),
                Row("PixelSkin.Theme", "TooltipFace",    "tooltip_face",        "PixelSkin.cs:273", PixelSkin.Theme.TooltipFace),
                Row("PixelSkin.Theme", "TabNormalText",  "tab_normal_text",     "PixelSkin.cs:275", PixelSkin.Theme.TabNormalText),
                Row("PixelSkin.Theme", "StatusFace",     "status_bar_face",     "PixelSkin.cs:277", PixelSkin.Theme.StatusFace),
                Row("PixelSkin.Theme", "StatusText",     "status_bar_text",     "PixelSkin.cs:279", PixelSkin.Theme.StatusText),
            };

            // ---- AseMenuKit（AseMenuKit.cs:83-90；internal，无 InternalsVisibleTo → 反射）----
            // 注：:89 手抄注释记的是 `disabled`（theme.xml:38 #202125），而非同值的死 token
            // menuitem_disabled_text（theme.xml:59，无任何样式引用）——按手抄口径钉 disabled。
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "FaceNormal",        "menuitem_normal_face",    "AseMenuKit.cs:83"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "FaceHot",           "menuitem_hot_face",       "AseMenuKit.cs:84"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "FaceHighlight",     "menuitem_highlight_face", "AseMenuKit.cs:85"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "TextNormal",        "menuitem_normal_text",    "AseMenuKit.cs:86"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "TextHot",           "menuitem_hot_text",       "AseMenuKit.cs:87"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "TextHighlight",     "menuitem_highlight_text", "AseMenuKit.cs:88"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "TextDisabled",      "disabled",                "AseMenuKit.cs:89"));
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseMenuKit", "TextDisabledShadow","background",              "AseMenuKit.cs:90"));

            // ---- AseEntry（AseEntry.cs:82；private → 反射。ColText/ColSelected/ColSelectedText
            //      引用的是 PixelSkin.Theme，已由上表 1/7/8 行钉住，不重复钉）----
            table.Add(ReflectRow("PirateCrew.UI.DebugUi.AseEntry", "ColSuffix", "entry_suffix", "AseEntry.cs:82"));

            return table;
        }

        static HandCopied Row(string owner, string field, string tokenId, string sourceRef, Color32 value)
        {
            return new HandCopied
            {
                Owner = owner,
                Field = field,
                TokenId = tokenId,
                SourceRef = sourceRef,
                Value = value,
            };
        }

        /// <summary>反射读一条静态 Color32 常量（internal/private 字段两域统一的读取口）。</summary>
        static HandCopied ReflectRow(string ownerType, string field, string tokenId, string sourceRef)
        {
            Type type = ResolveScriptType(ownerType);
            if (type == null)
                throw new InvalidOperationException(
                    string.Format("找不到手抄源类型 {0}——常量被移动/改名后须同步 ThemeColorParityTests 对拍表。", ownerType));
            FieldInfo fi = type.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fi == null)
                throw new InvalidOperationException(
                    string.Format("手抄源 {0}.{1} 不存在——常量被移动/改名后须同步 ThemeColorParityTests 对拍表。",
                        ownerType, field));
            return Row(ownerType, field, tokenId, sourceRef, (Color32)fi.GetValue(null));
        }

        /// <summary>
        /// 按全名解析脚本类型：先按程序集名 PirateCrew.UI（Unity EditMode 独立程序集），
        /// 再试测试程序集自身（无头验证台把 Scripts+Tests 编进同一 DLL），最后全域扫描兜底。
        /// </summary>
        static Type ResolveScriptType(string fullName)
        {
            Type t = Type.GetType(fullName + ", PirateCrew.UI");
            if (t != null)
                return t;
            t = Assembly.GetExecutingAssembly().GetType(fullName);
            if (t != null)
                return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null)
                    return t;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 权威文件定位 + 解析（双域统一）
        // ------------------------------------------------------------------

        /// <summary>盘上权威 theme.xml → colors 表（System.IO + 文本解析缝，纯 C#）。</summary>
        static IReadOnlyDictionary<string, Color32> LoadThemeColors()
        {
            string root = FindProjectRoot();
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException(
                    "定位不到 Unity 工程根（pirate-crew/）：PIRATECREW_PROJECT_ROOT / Application.dataPath"
                    + " / BaseDirectory 上溯三级全落空。无头验证台快照跑法请设 PIRATECREW_PROJECT_ROOT"
                    + " 指向快照内的工程根。");
            string path = Path.Combine(root, "Assets", "Art", "Sprites", "UI", "Aseprite", "theme.xml");
            if (!File.Exists(path))
                throw new FileNotFoundException("权威配色文件缺失，UI 配色对拍无法进行：", path);
            IReadOnlyDictionary<string, Color32> colors = AseThemeLayers.ParseColorsFromText(File.ReadAllText(path));
            Assert.GreaterOrEqual(colors.Count, HandCopiedCount,
                "theme.xml（{0}）colors 段只解析出 {1} 个 token——文件损坏或解析缝回退。", path, colors.Count);
            return colors;
        }

        /// <summary>
        /// 找 Unity 工程根（pirate-crew/，其 Assets/Art/Sprites/UI/Aseprite/theme.xml 即权威配色）。
        /// 三级回退手法照抄 Tests/Audio/Game2AudioPortTests.cs:342-384（判据换成 theme.xml）：
        /// ① 环境变量 PIRATECREW_PROJECT_ROOT（无头验证台快照跑法显式指定）；
        /// ② Unity 侧 Application.dataPath（EditMode 下 = &lt;工程&gt;/Assets，取其父目录）——
        ///    必须【反射】调用：无头域直接调 icall 会在 JIT 编译期炸出 SecurityException
        ///    （try/catch 拦不住），反射把同一错误包成可捕获的 TargetInvocationException；
        /// ③ 测试程序集 BaseDirectory 逐级上溯（含仓库根下 pirate-crew/ 嵌套探测）。
        /// </summary>
        static string FindProjectRoot()
        {
            string env = Environment.GetEnvironmentVariable("PIRATECREW_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(env) && IsProjectRoot(env))
                return env;

            try
            {
                Type appType = Type.GetType("UnityEngine.Application, UnityEngine.CoreModule");
                PropertyInfo dataPath = appType?.GetProperty("dataPath", BindingFlags.Public | BindingFlags.Static);
                string assets = dataPath?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(assets))
                {
                    string parent = Path.GetDirectoryName(assets.Replace('/', Path.DirectorySeparatorChar));
                    if (IsProjectRoot(parent))
                        return parent;
                }
            }
            catch (Exception)
            {
                // 无 Unity 运行时：忽略，走 BaseDirectory 上溯
            }

            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (IsProjectRoot(dir.FullName))
                    return dir.FullName;

                string nested = Path.Combine(dir.FullName, "pirate-crew");
                if (IsProjectRoot(nested))
                    return nested;

                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>工程根判据：权威 theme.xml 恰在该根的 Assets/Art/Sprites/UI/Aseprite/ 下。</summary>
        static bool IsProjectRoot(string path)
        {
            return !string.IsNullOrEmpty(path)
                   && File.Exists(Path.Combine(path, "Assets", "Art", "Sprites", "UI", "Aseprite", "theme.xml"));
        }

        /// <summary>
        /// 是否跑在 Unity 运行时里（EditMode/PlayMode）：反射探 Application.dataPath——
        /// 拿得到 = Unity 域；无头验证台纯 dotnet 域下同一调用抛异常或拿空 = false。
        /// （手法规格同 Game2AudioPortTests.FindProjectRoot 的反射包 icall 注释。）
        /// </summary>
        static bool IsUnityRuntimeDomain()
        {
            try
            {
                Type appType = Type.GetType("UnityEngine.Application, UnityEngine.CoreModule");
                PropertyInfo dataPath = appType?.GetProperty("dataPath", BindingFlags.Public | BindingFlags.Static);
                return !string.IsNullOrEmpty(dataPath?.GetValue(null) as string);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Hex(Color32 c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.r, c.g, c.b, c.a);
        }
    }
}
