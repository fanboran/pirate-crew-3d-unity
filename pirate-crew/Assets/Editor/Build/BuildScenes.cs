using System.Collections.Generic;
using System.Reflection;
using PirateCrew.Core;
using UnityEditor;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>
    /// 场景清单的**单一真源**：哪些场景进发行包、哪些只属于开发/测试。
    ///
    /// 【为什么是静态表而不是 <c>Assets/Scenes/ScenesManifest.asset</c>】
    /// 选静态表是因为这三条要求它必须满足：
    /// <list type="number">
    ///   <item><b>能被编译期校验</b>：<see cref="Validate"/> 要拿 <see cref="SceneNames"/> 的常量做交叉比对，
    ///         而 <see cref="SceneNames"/> 是 C# 常量——静态表是唯一能直接引用它的形式。</item>
    ///   <item><b>不含 GUID</b>：SO 资产要引用 <c>SceneAsset</c>，场景改名/移动就断引用且断链是静默的；
    ///         静态表用字符串路径，改名会在 <see cref="Validate"/> 里变成一条明确的报错。</item>
    ///   <item><b>可 diff、可 review</b>：清单是发行范围的一部分，应该出现在代码评审的 diff 里，
    ///         而不是藏在一个二进制式 YAML 资产里。</item>
    /// </list>
    /// 代价是清单改动要重编译——对「一版加几个场景」的频率来说可以忽略。
    ///
    /// 【与 Build Settings 的关系】<b>构建时不再读写 <c>EditorBuildSettings</c></b>：场景列表经
    /// <c>BuildPlayerOptions.scenes</c> 显式传给 <c>BuildPipeline.BuildPlayer</c>。这样做的两个理由：
    /// ① 本机 batchmode 下 <c>EditorBuildSettings.scenes</c> 的 API 写入不落盘（已有事故记录，
    /// 见 <c>Assets/Editor/ToonPilotSetup.cs</c> 的注释），显式传参天然绕开这个坑；
    /// ② Build Settings 是「编辑器里按 Play 想看什么」的列表，必须留着 ToonPilot（等距像素卡通试点场景的
    /// 播放器出图入口依赖它在列表里），而发行包不该含它——两者诉求相反，所以不该共用一个列表。
    /// 想让 Build Settings 与这里一致时用菜单 <c>PirateCrew/Build/同步 Build Settings…</c>（显式动作，不是副作用）。
    ///
    /// 【未收口项（登记给主控，本轨道不改）】<c>Assets/Editor/M3SceneSetup.cs:449 RegisterBuildSettings()</c>
    /// 仍是幂等全量写入 6 场景（含 ToonPilot）、自带一份硬编码数组。它每次跑装配链都会覆盖 Build Settings。
    /// 那是「开发集」，与这里的 <see cref="DevelopmentSet"/> 语义相同但两处维护——建议后续把该方法的数组
    /// 换成 <c>BuildScenes.DevelopmentSet()</c>，冲突与处置见 docs/项目/构建与发布手册.md 的遗留节。
    /// </summary>
    public static class BuildScenes
    {
        /// <summary>场景资产所在目录（AssetDatabase 相对路径）。</summary>
        public const string Folder = "Assets/Scenes";

        /// <summary>场景资产扩展名。</summary>
        public const string Extension = ".unity";

        /// <summary>
        /// 等距像素卡通试点场景：开发/测试专用，**不进发行包**。
        /// 它是 <c>PirateCrew/ToonPilot</c> 出图入口的载体，玩家不应看到。
        /// </summary>
        public const string ToonPilot = "ToonPilot";

        /// <summary>
        /// 像素化着色路径（v3 蓝本重写线）试点场景：开发/测试专用，**不进发行包**。
        /// 它是 <c>-pixelartOut</c> 出图入口的载体（物体 pass 写 G-buffer → 低分辨率域着色 → 上屏）。
        /// </summary>
        public const string PixelartPilot = "PixelartPilot";

        /// <summary>
        /// 关卡像素化试点场景（三个样板关的真实内容走本路径）：开发/测试专用，**不进发行包**。
        /// 它们是 <c>-pixelartOut -pixelartLevel &lt;N&gt;</c> 出图入口的载体，装配器见
        /// <c>PixelartLevelPilotSetup</c>，取景口径见 <c>PixelartLevelScene</c>；
        /// 只影响观感裁决与宣传图，不影响主战斗场景。
        /// </summary>
        public static readonly string[] PixelartLevelScenes =
        {
            "PixelartCloud",        // 关卡 1 云端漫步
            "PixelartIslets",       // 关卡 2 碎岛雨
            "PixelartSkyIsland",    // 关卡 3 天空之岛
        };

        /// <summary>
        /// 发行场景集（顺序即包内 index，必须 <c>[0] = Bootstrapper</c>）。
        /// 名字一律取自 <see cref="SceneNames"/>——那些常量是运行时 <c>SceneLoader.ChangeScene</c> 的入参，
        /// 只有共用同一批常量，<see cref="Validate"/> 才能证明「运行时会切到的场景都在包里」。
        /// </summary>
        static readonly string[] _releaseSceneNames =
        {
            SceneNames.Bootstrapper,   // index 0：播放器第一个场景，必须存在
            SceneNames.MainMenu,
            SceneNames.Battle,
            SceneNames.CrewManagement,
            SceneNames.LevelSelect,
        };

        /// <summary>仅开发/测试集：发行包不含，Build Settings 需要含（播放器出图入口依赖）。</summary>
        static readonly string[] _developmentOnlySceneNames = BuildDevelopmentOnlySet();

        /// <summary>仅开发场景集 = 固定两项 + <see cref="PixelartLevelScenes"/>（表在别处，避免两处维护）。</summary>
        static string[] BuildDevelopmentOnlySet()
        {
            var set = new string[2 + PixelartLevelScenes.Length];
            set[0] = ToonPilot;
            set[1] = PixelartPilot;
            PixelartLevelScenes.CopyTo(set, 2);
            return set;
        }

        /// <summary>发行场景集的场景名（只读视图，顺序即包内 index）。</summary>
        public static IReadOnlyList<string> ReleaseSceneSet
        {
            get { return _releaseSceneNames; }
        }

        /// <summary>仅开发/测试的场景名。</summary>
        public static IReadOnlyList<string> DevelopmentOnlySceneSet
        {
            get { return _developmentOnlySceneNames; }
        }

        /// <summary>发行场景集的新副本（调用方可安全改动）。</summary>
        public static string[] ReleaseSet()
        {
            return (string[])_releaseSceneNames.Clone();
        }

        /// <summary>开发场景集的新副本 = 发行集 + 仅开发集（Build Settings 的期望内容）。</summary>
        public static string[] DevelopmentSet()
        {
            var all = new string[_releaseSceneNames.Length + _developmentOnlySceneNames.Length];
            _releaseSceneNames.CopyTo(all, 0);
            _developmentOnlySceneNames.CopyTo(all, _releaseSceneNames.Length);
            return all;
        }

        /// <summary>场景名 → 场景资产路径（<c>Battle</c> → <c>Assets/Scenes/Battle.unity</c>）。</summary>
        public static string PathOf(string sceneName)
        {
            return Folder + "/" + sceneName + Extension;
        }

        /// <summary>
        /// 按清单名解析场景集：<c>release</c> / <c>development</c> / <c>a,b,c</c>（逗号分隔的自定义场景名）。
        /// 自定义列表供一次性排查用，仍会走 <see cref="Validate"/>。
        /// </summary>
        public static bool TryResolveSet(string setName, out string[] scenes, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(setName))
            {
                scenes = ReleaseSet();
                return true;
            }

            if (string.Equals(setName, "release", System.StringComparison.OrdinalIgnoreCase))
            {
                scenes = ReleaseSet();
                return true;
            }

            if (string.Equals(setName, "development", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(setName, "dev", System.StringComparison.OrdinalIgnoreCase))
            {
                scenes = DevelopmentSet();
                return true;
            }

            string[] custom = setName.Split(',');
            var trimmed = new List<string>(custom.Length);
            for (int i = 0; i < custom.Length; i++)
            {
                string name = custom[i].Trim();
                if (name.Length > 0)
                    trimmed.Add(name);
            }

            if (trimmed.Count == 0)
            {
                scenes = null;
                error = "无法解析场景集 \"" + setName + "\"。可用: release | development | 逗号分隔的场景名";
                return false;
            }

            scenes = trimmed.ToArray();
            return true;
        }

        /// <summary>
        /// 校验一份场景集。返回问题清单（空 = 通过）。检查项：
        /// <list type="number">
        ///   <item>非空，元素非空，且是「场景名」而不是路径（禁止传 <c>Assets/Scenes/Battle.unity</c>）</item>
        ///   <item>无重复项（重复会让包内出现同名场景，加载行为依赖顺序，属静默故障）</item>
        ///   <item>场景资产真实存在（<c>LoadAssetAtPath&lt;SceneAsset&gt;</c> 非空）</item>
        ///   <item><c>[0]</c> 必须是 <see cref="SceneNames.Bootstrapper"/>——播放器启动场景</item>
        ///   <item>覆盖 <see cref="SceneNames"/> 声明的**全部**场景：运行时会 ChangeScene 到的场景若不在包里，
        ///         会在玩家手里 <c>LoadScene</c> 失败；这是本表最有价值的一条断言</item>
        ///   <item><paramref name="requireReleaseSafe"/> 为 true 时，禁止出现仅开发场景（ToonPilot）</item>
        /// </list>
        /// </summary>
        public static List<string> Validate(string[] scenes, bool requireReleaseSafe)
        {
            var problems = new List<string>();

            if (scenes == null || scenes.Length == 0)
            {
                problems.Add("场景集为空。");
                return problems;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < scenes.Length; i++)
            {
                string name = scenes[i];

                if (string.IsNullOrWhiteSpace(name))
                {
                    problems.Add("第 " + i + " 项场景名为空。");
                    continue;
                }

                if (name.IndexOf('/') >= 0 || name.EndsWith(Extension))
                {
                    problems.Add("第 " + i + " 项 \"" + name + "\" 是路径或含扩展名；场景集只写场景名（例: Battle）。");
                    continue;
                }

                if (!seen.Add(name))
                {
                    problems.Add("场景重复出现: " + name);
                    continue;
                }

                string path = PathOf(name);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    problems.Add("场景资产不存在: " + path);
            }

            if (scenes[0] != SceneNames.Bootstrapper)
            {
                problems.Add("第 0 项必须是 " + SceneNames.Bootstrapper
                    + "（播放器启动场景），实际是 \"" + scenes[0] + "\"。");
            }

            foreach (string declared in DeclaredRuntimeSceneNames())
            {
                if (!seen.Contains(declared))
                {
                    problems.Add("运行时 SceneNames." + declared + " 已声明，但不在场景集里——"
                        + "打包后 LoadScene 会失败，请把它加进 BuildScenes。");
                }
            }

            if (requireReleaseSafe)
            {
                for (int i = 0; i < _developmentOnlySceneNames.Length; i++)
                {
                    string devOnly = _developmentOnlySceneNames[i];
                    if (seen.Contains(devOnly))
                        problems.Add("发行集里出现仅开发场景 " + devOnly + "（它属于 Build Settings 的开发集）。");
                }
            }

            return problems;
        }

        /// <summary>
        /// 反射列出 <see cref="SceneNames"/> 里声明的全部 <c>public const string</c>。
        /// 用反射而不是手抄一遍，是为了让「新增场景常量忘了进发行集」自动变成一条报错——
        /// 手抄的清单会随常量一起漂移，等于没校验。
        /// </summary>
        public static List<string> DeclaredRuntimeSceneNames()
        {
            var names = new List<string>();

            FieldInfo[] fields = typeof(SceneNames).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsLiteral || field.FieldType != typeof(string))
                    continue;

                var value = field.GetRawConstantValue() as string;
                if (!string.IsNullOrEmpty(value) && !names.Contains(value))
                    names.Add(value);
            }

            names.Sort(System.StringComparer.Ordinal);
            return names;
        }
    }
}
