using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// **脚本可序列化契约**：每个 MonoBehaviour 派生类型都必须能映射到一个「类名 = 文件名」的
    /// MonoScript 资产。
    ///
    /// 【它在防什么】Unity 的脚本-资产映射规则是硬的：**一个 .cs 一个 MonoScript，类名必须等于文件名，
    /// 且类不能嵌套**。违反它不会编译报错，代价是引擎在**序列化环节**才翻脸：
    ///   · 存 Prefab 直接失败——`You are trying to save a Prefab that contains the script
    ///     'SketchSeparator/WavyLineGraphic', which does not derive from MonoBehaviour. This is not allowed.`
    ///   · 场景/构建产物重新加载时该组件还原不回来——**分隔线静默消失，不报错、不留日志**。
    /// 本项目真实踩过：`WavyLineGraphic` 嵌在 `SketchSeparator` 里，直到做场景 Prefab 化、
    /// 引擎拒绝给它存 Prefab 才暴露；而那时的 MainMenu / LevelSelect 里已经躺着 3 个
    /// 还原不回来的失效组件。另一处 `SketchSliderGraphic` 与宿主同文件（名字不匹配），
    /// 属同一类问题，只是还没被存进场景。
    ///
    /// 【为什么用反射 + AssetDatabase 而不是正则扫源码】"类名 = 文件名"这件事的权威定义在
    /// MonoScript 的映射里，不在文本里——正则可以找出"嵌套类"，但找不出"同文件不同名"，
    /// 也判不准嵌套（命名空间缩进与类缩进长得一样）。这里直接问引擎：这个类型有没有
    /// MonoScript、MonoScript 属于哪个文件。
    ///
    /// 【范围】只扫本工程程序集（`PirateCrew.*` 与 `Assembly-CSharp*`，排除测试程序集）
    /// 与 `Assets/` 下的脚本资产，不碰引擎与第三方包。
    /// </summary>
    [TestFixture]
    public class ScriptAssetSerializabilityTests
    {
        static readonly string[] ScriptRoots = { "Assets/Scripts", "Assets/Editor" };

        /// <summary>
        /// **棘轮白名单：已知存量违规，冻结不再增长。**
        ///
        /// 这不是"允许"，是"登记在案的债"：列表里的类型确实不满足 Unity 的脚本-资产映射规则，
        /// 只是在当前工程里它们要么不进场景、要么只在运行期挂（详见逐条理由）。
        /// 一次性清掉它们的代价与收益不成比例（其中两个 <c>ToastFader</c> 同名重复，谁留谁删是设计决定），
        /// 故登记为待办（`docs/项目/待办事项.md`），本测试只负责**不再多一条**。
        ///
        /// 【怎么用】清掉一条就从这里删一行；新增一条必须先修代码，不要往这里加——
        /// 加一行等于把"组件在构建产物里还原不回来"的故障永久合法化。
        /// </summary>
        static readonly Dictionary<string, string> KnownOffenders = new Dictionary<string, string>
        {
            { "PirateCrew.Visual.CrewTeamTintPart",
              "构建期临时标记 + 运行期兜底，**永不进 Prefab**：CrewVisualPrefabBuilder 建完部件就把它删掉、改用显式引用（CrewVisualPrefabBuilder.cs:932）" },
            { "PirateCrew.UI.Stick.StickLayoutElement", "同文件第二个 MonoBehaviour（StickLayout.cs 里的布局件）" },
            { "PirateCrew.UI.Stick.StickLayoutGroup", "同文件第二个 MonoBehaviour（同上）" },
            { "PirateCrew.UI.Stick.StickContextAnchor", "同文件第二个 MonoBehaviour（StickUIRoot.cs）" },
            { "PirateCrew.UI.Stick.WobbledDotGraphic", "同文件第二个 MonoBehaviour（StickWorldHealthBar.cs 的自绘件）" },
            { "PirateCrew.UI.SketchWidgets+ToastFader", "嵌套类；与 StickKit+ToastFader **同名重复**，去留待 UI 换装批次裁决" },
            { "PirateCrew.UI.Stick.StickKit+ToastFader", "嵌套类；同上" },
            { "PirateCrew.UI.Stick.SketchSwitch+SketchSwitchGraphic", "嵌套自绘件（与刚修掉的 WavyLineGraphic 同类）" },
            { "PirateCrew.UI.Stick.SketchToggle+SketchToggleGraphic", "嵌套自绘件（同上）" },
            { "PirateCrew.UI.Stick.StickKit+StickHoverScale", "嵌套组件（同上）" },
            { "PirateCrew.UI.Stick.StickWindow+WindowDragHandle", "嵌套组件（同上）" },
        };

        static bool IsProjectAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            if (name.IndexOf("Tests", StringComparison.Ordinal) >= 0)
                return false;
            return name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)
                   || name.StartsWith("PirateCrew.", StringComparison.Ordinal);
        }

        static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return Array.FindAll(e.Types, t => t != null);
            }
        }

        /// <summary>MonoScript 资产里的"类型 → 脚本路径"映射（引擎认的那一份）。</summary>
        static Dictionary<Type, string> BuildMonoScriptMap()
        {
            var map = new Dictionary<Type, string>();
            string[] guids = AssetDatabase.FindAssets("t:MonoScript", ScriptRoots);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                    continue;

                Type type = script.GetClass();
                if (type != null && !map.ContainsKey(type))
                    map[type] = path;
            }

            return map;
        }

        [Test]
        public void EveryMonoBehaviourType_IsBackedByASameNamedScriptFile()
        {
            Dictionary<Type, string> map = BuildMonoScriptMap();
            // 违规项按「类型全名 → 说明」成对收集：棘轮按全名过滤，
            // 不要事后从说明文本里反解全名（两种说明格式的首个括号位置不同，反解会漏）。
            var offenders = new List<KeyValuePair<string, string>>();
            int checkedTypes = 0;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                if (!IsProjectAssembly(assemblies[a]))
                    continue;

                foreach (Type type in SafeGetTypes(assemblies[a]))
                {
                    if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                        continue;
                    if (type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;   // 抽象/泛型基类不挂到对象上，Unity 也不需要它的 MonoScript

                    checkedTypes++;

                    string path;
                    if (!map.TryGetValue(type, out path))
                    {
                        offenders.Add(new KeyValuePair<string, string>(type.FullName,
                            type.FullName
                            + "（" + (type.IsNested ? "嵌套在 " + type.DeclaringType.FullName + " 内" : "未与任何脚本资产匹配")
                            + "）：Unity 找不到它的 MonoScript → 存 Prefab 会失败、场景重载后组件还原不回来。"
                            + "修法：提为顶级类并放进同名文件 " + type.Name + ".cs。"));
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (fileName != type.Name)
                    {
                        offenders.Add(new KeyValuePair<string, string>(type.FullName,
                            type.FullName + " 落在 " + path
                            + "（文件名 " + fileName + " ≠ 类名）→ Unity 只认文件里的同名类，"
                            + "这个组件一样存不进 Prefab。修法：拆到 " + type.Name + ".cs。"));
                    }
                }
            }

            Assert.Greater(checkedTypes, 0,
                "没扫到任何 MonoBehaviour 派生类型——程序集名过滤条件过期了？本测试会因此变成永远绿的空测试。");

            // 棘轮：已知存量违规过滤掉，只对**新增**违规报警。
            var fresh = new List<string>();
            for (int i = 0; i < offenders.Count; i++)
            {
                if (KnownOffenders.ContainsKey(offenders[i].Key))
                    continue;
                fresh.Add(offenders[i].Value);
            }

            Assert.IsEmpty(fresh,
                "以下 MonoBehaviour 类型不满足 Unity 的「类名 = 文件名、不嵌套」规则（共 " + fresh.Count
                + " 个新增违规）：\n  · " + string.Join("\n  · ", fresh)
                + "\n（已知存量违规 " + KnownOffenders.Count + " 条登记在 KnownOffenders 白名单里，"
                + "本测试只保证不再新增；清掉一条请同步删白名单一行。）");
        }

        /// <summary>
        /// 白名单不许留僵尸项：一个已修好的类型还留在 <see cref="KnownOffenders"/> 里，
        /// 等于给下一个同类违规留了后门（棘轮会悄悄松一格）。
        /// </summary>
        [Test]
        public void KnownOffendersAllowlist_HasNoStaleEntries()
        {
            var seen = new HashSet<string>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                if (!IsProjectAssembly(assemblies[a]))
                    continue;
                foreach (Type type in SafeGetTypes(assemblies[a]))
                {
                    if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                        continue;
                    seen.Add(type.FullName);
                }
            }

            var stale = new List<string>();
            foreach (string key in KnownOffenders.Keys)
            {
                if (!seen.Contains(key))
                    stale.Add(key);
            }

            Assert.IsEmpty(stale,
                "KnownOffenders 里的这些类型已经不存在了（被删/改名/已修）——请删掉对应的白名单行：\n  · "
                + string.Join("\n  · ", stale));
        }
    }
}
