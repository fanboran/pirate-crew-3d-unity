using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>
    /// PlayerSettings 工业化：把「发行需要的 PlayerSettings」变成**构建脚本的显式动作**，
    /// 而不是靠人记得在 GUI 里点。
    ///
    /// 【为什么脚本化而不是手改 ProjectSettings.asset】
    /// <list type="bullet">
    ///   <item>YAML 是**产物**：手改的东西没有校验、没有 diff 语义、Unity 一保存就可能被改写。</item>
    ///   <item>脚本是**真源**：每个字段为什么取这个值，理由写在字段旁的注释里，可以 review。</item>
    ///   <item>CI 需要它：干净机器上的 ProjectSettings 必须由脚本收敛到同一状态，否则「CI 绿」不代表本地行为。</item>
    /// </list>
    /// 唯一需要人手的例外：<c>bundleVersion</c> 已经与 <see cref="BuildVersion.Current"/> 对齐
    /// （<c>ProjectSettings/ProjectSettings.asset</c> 里手改成 0.1.0），本类只负责把它**写回**同一值并校验，
    /// 不指望人来维持。
    ///
    /// 【与 ReleaseGate 的关系】<c>Assets/Editor/ReleaseGate.cs</c> 也写图标与版本，两者重叠但语义不同：
    /// 它写的是「一次性发布资产」（程序化画图标 PNG），本类写的是「每次构建都要成立的配置」。
    /// 两者图标指向同一张 <c>Assets/Art/Textures/AppIcon.png</c>，所以先后顺序不影响结果；
    /// **版本号是冲突点**（ReleaseGate 的常量是旧值 1.0.0，重跑会把 bundleVersion 回写）——已登记给主控，本轨道不改它。
    /// </summary>
    public static class PlayerPreset
    {
        /// <summary>公司名（PlayerSettings.companyName）。</summary>
        public const string CompanyName = "BoranFan";

        /// <summary>产品名（PlayerSettings.productName，显示在窗口标题与系统里）。</summary>
        public const string ProductName = "Pirate Crew 3D";

        /// <summary>
        /// 应用图标 PNG（1024×1024，Blender 管线渲染版）。
        /// 与 <c>ReleaseGate.IconAssetPath</c> 同源——同一个文件路径两处持有常量是有意的：
        /// 构建脚本不该因为美术向脚本被并行改写而编译不过。
        /// </summary>
        public const string IconAssetPath = "Assets/Art/Textures/AppIcon.png";

        /// <summary>默认窗口分辨率（16:9；与 README 系统需求页的「DirectX 11 兼容 / 1920×1080」一致）。</summary>
        public const int DefaultScreenWidth = 1920;

        /// <summary>默认窗口高度。</summary>
        public const int DefaultScreenHeight = 1080;

        /// <summary>
        /// 默认全屏模式。<c>FullScreenWindow</c>（无边框全屏）取的是项目既有行为
        /// （<c>fullScreenMode: 1</c>），改成独占全屏会改变已有玩家的切屏体验，不在本次范围内。
        /// </summary>
        public const FullScreenMode ScreenMode = FullScreenMode.FullScreenWindow;

        /// <summary>
        /// 图形 API 顺序：只保留 Direct3D 11。依据是 README 声明的系统需求「DirectX 11 兼容」
        /// ——把它写成配置而不是靠引擎默认，可以让「声明的最低配置」与「实际作出的包」对得上。
        /// 未来想开 D3D12，在数组前面插一项即可（顺序 = 优先级）。
        /// </summary>
        public static readonly GraphicsDeviceType[] GraphicsApis =
        {
            GraphicsDeviceType.Direct3D11,
        };

        /// <summary>
        /// 托管剥离等级。发行档 <c>Low</c>：剥掉未使用的引擎代码但不碰托管类型。
        /// **故意不用 Medium/High**——本项目有反射依赖（<c>SceneNames</c> 常量反射校验、EventCatalog 反射测试），
        /// 提高剥离等级需要先补 <c>link.xml</c> 保留清单，否则是「构建成功、运行期找不到类型」的静默故障。
        /// </summary>
        public const ManagedStrippingLevel ReleaseStripping = ManagedStrippingLevel.Low;

        /// <summary>开发档剥离等级：全关，保证栈帧里能看到真实方法名。</summary>
        public const ManagedStrippingLevel DevelopmentStripping = ManagedStrippingLevel.Disabled;

        /// <summary>
        /// 脚本 API 兼容级别。<c>NET_Standard_2_0</c> 与工程现值一致（<c>apiCompatibilityLevel: 6</c>）；
        /// 升到 .NET Framework 没有收益且会改变可用的 BCL 面（可能让「本机编译过、CI 不过」）。
        /// </summary>
        public const ApiCompatibilityLevel Compatibility = ApiCompatibilityLevel.NET_Standard_2_0;

        /// <summary>本平台目标组。</summary>
        public static NamedBuildTarget TargetGroup
        {
            get { return NamedBuildTarget.Standalone; }
        }

        /// <summary>构建目标。</summary>
        public static BuildTarget Target
        {
            get { return BuildTarget.StandaloneWindows64; }
        }

        /// <summary>
        /// 预检：目标平台的脚本后端是否真的装好。IL2CPP 是 Unity Hub 的独立模块，
        /// 没装时构建会走到一半才失败（甚至只报一句 IL2CPP 编译错误），所以提前拦下来并给安装指引。
        /// </summary>
        public static bool CheckScriptingBackend(string backend, out string detail)
        {
            detail = null;

            if (string.Equals(backend, "mono", System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.Equals(backend, "il2cpp", System.StringComparison.OrdinalIgnoreCase))
            {
                detail = "未知脚本后端 \"" + backend + "\"；可用 mono | il2cpp。";
                return false;
            }

            // IL2CPP 的 Windows 播放器变体目录名随版本略有差异，三种都认。
            string variations = EditorApplication.applicationContentsPath
                + "/PlaybackEngines/WindowsStandaloneSupport/Variations";
            string[] candidates =
            {
                variations + "/win64_il2cpp",
                variations + "/win64_player_development_il2cpp",
                variations + "/win64_player_nondevelopment_il2cpp",
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i]))
                    return true;
            }

            detail = "本机 Unity 未安装 Windows IL2CPP 变体（查过 " + variations + "）。"
                + "请在 Unity Hub 给 2022.3.62f1c1 追加安装「Windows Build Support (IL2CPP)」，"
                + "或改用 -buildScriptingBackend mono（本机已装）。";
            return false;
        }

        /// <summary>
        /// 切换活动构建目标。**必须在写平台相关的 PlayerSettings 之前调用**：
        /// 目标没切过去时 <c>SetApplicationIdentifier</c> 之类的写入会落到别的平台组上。
        /// </summary>
        public static bool EnsureActiveTarget(List<string> problems)
        {
            if (EditorUserBuildSettings.activeBuildTarget == Target)
                return true;

            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, Target);
            if (!switched)
                problems.Add("切换到 " + Target + " 失败（可能正在编译或该平台未安装）。");
            return switched;
        }

        /// <summary>
        /// 写入全部配置并读回校验。返回「已生效条目」（写进构建报告），<paramref name="problems"/> 收错误。
        /// </summary>
        public static List<string> Apply(BuildConfig config, List<string> problems)
        {
            var applied = new List<string>();

            EnsureActiveTarget(problems);
            if (problems.Count > 0)
                return applied;

            PlayerSettings.companyName = CompanyName;
            applied.Add("companyName=" + CompanyName);

            PlayerSettings.productName = ProductName;
            applied.Add("productName=" + ProductName);

            PlayerSettings.bundleVersion = config.Version;
            applied.Add("bundleVersion=" + config.Version);

            // 【提案/待定】应用标识由用户确认后才是最终值，缺省见 BuildConfig.AppId。
            PlayerSettings.SetApplicationIdentifier(TargetGroup, config.AppId);
            applied.Add("applicationIdentifier=" + config.AppId);

            PlayerSettings.defaultScreenWidth = DefaultScreenWidth;
            PlayerSettings.defaultScreenHeight = DefaultScreenHeight;
            applied.Add("defaultScreen=" + DefaultScreenWidth + "x" + DefaultScreenHeight);

            PlayerSettings.fullScreenMode = ScreenMode;
            applied.Add("fullScreenMode=" + ScreenMode);

            PlayerSettings.resizableWindow = false;
            applied.Add("resizableWindow=false");

            PlayerSettings.runInBackground = true;
            applied.Add("runInBackground=true");

            PlayerSettings.SetUseDefaultGraphicsAPIs(Target, false);
            PlayerSettings.SetGraphicsAPIs(Target, GraphicsApis);
            applied.Add("graphicsAPIs=" + GraphicsApis[0]);

            ScriptingImplementation backend = string.Equals(config.ScriptingBackend, "il2cpp",
                System.StringComparison.OrdinalIgnoreCase)
                ? ScriptingImplementation.IL2CPP
                : ScriptingImplementation.Mono2x;
            PlayerSettings.SetScriptingBackend(TargetGroup, backend);
            applied.Add("scriptingBackend=" + backend);

            PlayerSettings.SetApiCompatibilityLevel(TargetGroup, Compatibility);
            applied.Add("apiCompatibilityLevel=" + Compatibility);

            ManagedStrippingLevel stripping = config.Flavor == BuildFlavor.Development
                ? DevelopmentStripping
                : ReleaseStripping;
            PlayerSettings.SetManagedStrippingLevel(TargetGroup, stripping);
            applied.Add("managedStrippingLevel=" + stripping);

            List<string> icons = ApplyIcon(problems);
            applied.AddRange(icons);

            Verify(config, problems);
            return applied;
        }

        /// <summary>
        /// 图标：同一张 1024 PNG 填满 Standalone 的全部 <see cref="IconKind.Application"/> 尺寸槽
        /// （Unity 构建期按槽缩放）。资源缺失时明确报错而不是留一个空图标表。
        /// </summary>
        static List<string> ApplyIcon(List<string> problems)
        {
            var applied = new List<string>();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
            if (icon == null)
            {
                problems.Add("应用图标资源缺失: " + IconAssetPath
                    + "（可用菜单 PirateCrew/Release/应用图标与版本 重新生成）。");
                return applied;
            }

            // 用 GetIconSizes/SetIcons 的 NamedBuildTarget 三元组，而不是标记「将来弃用」的
            // *ForTargetGroup(BuildTargetGroup) 系列（见 UnityEditor.xml 的 <summary>：BuildTargetGroup
            // is marked for deprecation in the future. Use PlayerSettings.GetIconSizes instead）。
            int[] sizes = PlayerSettings.GetIconSizes(TargetGroup, IconKind.Application);
            if (sizes == null || sizes.Length == 0)
            {
                problems.Add("Standalone 图标槽尺寸表为空，无法写入图标。");
                return applied;
            }

            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++)
                icons[i] = icon;

            PlayerSettings.SetIcons(TargetGroup, icons, IconKind.Application);

            // 读回校验：槽位数量与首槽图标都对得上才算写入成功。
            Texture2D[] readBack = PlayerSettings.GetIcons(TargetGroup, IconKind.Application);
            if (readBack == null || readBack.Length != icons.Length || readBack[0] != icon)
            {
                problems.Add("图标读回不一致：写入 " + icons.Length + " 槽，读回 "
                    + (readBack == null ? "null" : readBack.Length.ToString()) + " 槽。");
                return applied;
            }

            applied.Add("icons=" + IconAssetPath + " × " + icons.Length + " 槽");
            return applied;
        }

        /// <summary>
        /// 读回校验：把写过的东西再读一遍，与期望值不一致就报错。
        /// 意义在于「脚本写了」不等于「引擎接受了」——例如目标平台没切过去、或某些字段受平台限制被忽略。
        /// </summary>
        public static void Verify(BuildConfig config, List<string> problems)
        {
            if (PlayerSettings.companyName != CompanyName)
                problems.Add("companyName 读回不一致: " + PlayerSettings.companyName);
            if (PlayerSettings.productName != ProductName)
                problems.Add("productName 读回不一致: " + PlayerSettings.productName);
            if (PlayerSettings.bundleVersion != config.Version)
                problems.Add("bundleVersion 读回不一致: " + PlayerSettings.bundleVersion);

            string appId = PlayerSettings.GetApplicationIdentifier(TargetGroup);
            if (appId != config.AppId)
                problems.Add("applicationIdentifier 读回不一致: " + appId);
        }
    }
}
