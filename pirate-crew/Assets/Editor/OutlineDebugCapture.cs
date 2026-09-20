using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 逐档采集 PirateOutline.shader / PirateOutlinePost.shader 的 _DebugMode 调试截图。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Rendering/采集描边调试截图
    ///   调用: PirateCrew.EditorTools.OutlineDebugCapture.CaptureAll()
    ///
    /// 【产物】&lt;仓库根&gt;/export/outline-debug/debug-mode-{n}-{slug}.png（共 5 张）
    ///   仓库根由 Application.dataPath 上溯一级得到（dataPath 是 pirate-crew/Assets）。
    ///   export/ 在 .gitignore 之外、位于 Unity 工程外，故用 System.IO 直接落盘，
    ///   不能走 AssetDatabase。
    ///
    /// 【为什么文件名用英文 slug 而不是中文】
    ///   ScreenCapture.CaptureScreenshot 在部分平台/驱动组合下对非 ASCII 路径写入会失败，
    ///   故文件名用短 slug；中文含义写在下方 Levels 表与日志里，docs/描边Shader调试.md
    ///   也列了文件名↔含义对照。
    ///
    /// 【重要：本环境无法产出真实截图】
    ///   本仓库的开发环境用 -batchmode -nographics 验证工程，没有可用渲染路径：
    ///   graphicsDeviceType == Null，ScreenCapture / Camera.Render 拿不到真实像素。
    ///   因此 CaptureAll() 在检测到无渲染路径时会 Debug.LogError 明确报错并直接返回，
    ///   **不会静默失败、也不会生成占位图**。
    ///   真实截图必须在**有图形界面的 Unity 编辑器会话**里手动跑本菜单项（见 docs）。
    ///
    /// 【采集方式】
    ///   ScreenCapture.CaptureScreenshot 抓的是 Game View 当前帧，因此：
    ///     - 需要打开 Game View，且 Game View 尺寸=期望截图分辨率；
    ///     - 需要场景里有一台生效的相机（本工程 Battle 场景有 Main Camera）；
    ///     - 逐档之间需要让编辑器真实走几帧，否则抓到的是上一档的画面，
    ///       故用 EditorApplication.update 驱动的状态机，每档"改档→等 N 帧→截图→等 N 帧"。
    /// </summary>
    public static class OutlineDebugCapture
    {
        const string OutlineShaderName = "PirateCrew/PirateOutline";
        const string OutlinePostShaderName = "PirateCrew/PirateOutlinePost";
        const string OutputFolderRelative = "export/outline-debug";

        static readonly int s_DebugModeId = Shader.PropertyToID("_DebugMode");

        /// <summary>每档"改档后"与"截图后"各等待的编辑器帧数，给渲染与写盘留时间。</summary>
        const int FramesPerStep = 4;

        struct DebugLevel
        {
            public int Mode;
            public string Slug;    // 文件名用
            public string Meaning; // 日志/文档用
        }

        // 逐档含义必须与 docs/描边Shader调试.md 的"应看到什么"一致。
        static readonly DebugLevel[] Levels =
        {
            new DebugLevel { Mode = 0, Slug = "normal",           Meaning = "正常合成（本体+描边）" },
            new DebugLevel { Mode = 1, Slug = "base-only",        Meaning = "只显示本体不描边" },
            new DebugLevel { Mode = 2, Slug = "expanded-hull",    Meaning = "只显示法线外扩结果" },
            new DebugLevel { Mode = 3, Slug = "outline-mask",     Meaning = "只显示描边掩码（单色）" },
            new DebugLevel { Mode = 4, Slug = "depth-normal-raw", Meaning = "深度/法线原始数据" },
        };

        // ---- 状态机（EditorApplication.update 驱动）----
        static List<Material> s_Targets;
        static List<UnitOutlineBinder> s_ForcedBinders;
        static string s_OutputFolder;
        static int s_LevelIndex;
        static int s_Phase;   // 0=改档后等待, 1=等待抓帧, 2=收尾等待（让最后一张真抓完再复位）
        static int s_Ticks;
        static int s_TargetFrame;
        static bool s_Running;

        [MenuItem("PirateCrew/Rendering/采集描边调试截图")]
        public static void CaptureAll()
        {
            if (s_Running)
            {
                Debug.LogWarning("[OutlineDebugCapture] 上一轮采集还没结束，忽略本次调用。");
                return;
            }

            // ---- 1. 渲染路径前置检查：没有就明确报错，绝不静默失败 ----
            string unavailableReason = DetectUnavailableRenderPath();
            if (unavailableReason != null)
            {
                Debug.LogError(
                    "[OutlineDebugCapture] 当前会话没有可用渲染路径，无法采集真实截图。原因：" + unavailableReason + "\n"
                    + "  本脚本不会生成占位图或伪造截图。\n"
                    + "  请改用**有图形界面的 Unity 编辑器会话**运行本菜单项：\n"
                    + "    PirateCrew/Rendering/采集描边调试截图\n"
                    + "  注意：不要用 `-batchmode -nographics` 跑截图 —— 本工程在该模式下会卡在 GfxDevice 创建，\n"
                    + "        且 ScreenCapture / Camera.Render 本就拿不到真实像素。\n"
                    + "  截图产物应落在 " + GetOutputFolder() + "，逐档预期画面见 docs/描边Shader调试.md。");
                return;
            }

            Camera camera = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
            if (camera == null)
            {
                Debug.LogError("[OutlineDebugCapture] 当前场景里找不到任何 Camera（Camera.main 与 FindObjectOfType<Camera> 均为空），"
                    + "无法渲染。请打开 Assets/Scenes/Battle.unity 或其它含相机的场景后重试。");
                return;
            }

            // play mode 下若编辑器处于暂停，player loop 不推进 → 改档后画面不会重绘，
            // 采集会一直等不到新帧（曾实测卡住）。这里明确报错而不是挂死。
            if (Application.isPlaying && EditorApplication.isPaused)
            {
                Debug.LogError("[OutlineDebugCapture] 编辑器处于 play mode **暂停**状态，画面不会随 _DebugMode 重绘，"
                    + "无法逐档采集。请先取消暂停（工具栏 Pause 按钮 / EditorApplication.isPaused = false）后重试。");
                return;
            }

            s_Targets = FindOutlineMaterials();
            if (s_Targets.Count == 0)
            {
                Debug.LogError("[OutlineDebugCapture] 没找到任何使用 \"" + OutlineShaderName + "\" 或 \""
                    + OutlinePostShaderName + "\" 的材质。\n"
                    + "  请先给场景里的单位挂上 PirateOutline 材质（或在 URP Renderer 上加 OutlineRendererFeature 并指定后处理 shader），"
                    + "再运行本菜单项。");
                return;
            }

            s_OutputFolder = GetOutputFolder();
            Directory.CreateDirectory(s_OutputFolder);

            // 单位上的 _OutlineState 由 UnitOutlineBinder 用 MPB 逐单位覆盖（MPB 优先于材质资产），
            // 所以只改材质资产的 _OutlineState 对已生成单位无效。这里统一把场所内有 binder 的单位
            // 临时强制为「选中态(2)」，保证档 0 也能看到描边；采集结束复位为 -1（交回状态位驱动）。
            ForceBinderOutlineState(OutlineStateRules.Selected);

            s_LevelIndex = 0;
            s_Phase = 0;
            s_Ticks = 0;
            s_TargetFrame = 0;
            s_Running = true;
            EditorApplication.update += Tick;

            Debug.Log("[OutlineDebugCapture] 开始逐档采集 " + Levels.Length + " 张截图，命中材质 "
                + s_Targets.Count + " 个（" + DescribeTargets() + "），输出目录 " + s_OutputFolder
                + "。请保持 Game View 可见且不要操作编辑器，采集完成后会自动把 _DebugMode 复位为 0。\n"
                + "  已把 " + s_ForcedBinders.Count + " 个 UnitOutlineBinder 临时强制为选中态(_OutlineState=2)，"
                + "使档 0 也能看到描边；采集结束复位为 -1（交回 Selected/Hovered 状态位驱动）。\n"
                + "  ⚠ 若本脚本中断（编辑器崩溃/强杀），材质资产的 _DebugMode 与 binder 的 DebugForcedState 可能残留，"
                + "重跑一次本菜单项即可复位。\n"
                + "  注意：文件名档位含义按 PirateOutline.shader（单体描边）定义；\n"
                + "        若命中材质里含 PirateOutlinePost.shader，其 _DebugMode 档位语义不同\n"
                + "        （0 正常/1 原始mask/2 二值mask/3 Sobel/4 纯边缘），见 docs/描边Shader调试.md 第四节。\n"
                + "        后处理 shader 的材质由 RendererFeature 运行时创建（非资产），通常不会被本脚本命中，\n"
                + "        需要验证后处理分档时请在 URP Renderer 的 OutlineRendererFeature 上改 Debug Mode 后重跑。");
        }

        /// <summary>列出命中材质按 shader 分组，便于确认采到的是哪套 shader。</summary>
        static string DescribeTargets()
        {
            if (s_Targets == null)
                return string.Empty;

            var counts = new Dictionary<string, int>();
            foreach (Material mat in s_Targets)
            {
                if (mat == null || mat.shader == null)
                    continue;
                counts.TryGetValue(mat.shader.name, out int n);
                counts[mat.shader.name] = n + 1;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts)
                parts.Add(kv.Key + " x" + kv.Value);
            return string.Join("，", parts);
        }

        /// <summary>
        /// 返回 null 表示有可用渲染路径；否则返回不可用原因。
        /// </summary>
        static string DetectUnavailableRenderPath()
        {
            if (Application.isBatchMode)
                return "Application.isBatchMode == true（批处理模式）";

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return "SystemInfo.graphicsDeviceType == Null（无图形设备，通常是 -nographics）";

            if (string.IsNullOrEmpty(SystemInfo.graphicsDeviceVersion))
                return "SystemInfo.graphicsDeviceVersion 为空（图形驱动未初始化）";

            return null;
        }

        static string GetOutputFolder()
        {
            // Application.dataPath = <仓库根>/pirate-crew/Assets，上溯两级到仓库根。
            string projectRoot = Path.GetDirectoryName(Application.dataPath); // pirate-crew
            string repoRoot = Path.GetDirectoryName(projectRoot);              // 仓库根
            return Path.Combine(repoRoot, OutputFolderRelative).Replace('\\', '/');
        }

        /// <summary>收集所有含 _DebugMode 的目标材质：已在场景里的实例 + 工程内的材质资产。</summary>
        static List<Material> FindOutlineMaterials()
        {
            var found = new List<Material>();
            var seen = new HashSet<Material>();

            // 场景实例优先（单位上临时挂的材质实例也能采到）。
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                foreach (Material mat in renderer.sharedMaterials)
                    TryAdd(mat, found, seen);
            }

            // 材质资产（比如还没实例化到场景的模板材质）。
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                TryAdd(mat, found, seen);
            }

            return found;
        }

        static void TryAdd(Material mat, List<Material> found, HashSet<Material> seen)
        {
            if (mat == null || mat.shader == null || !seen.Add(mat))
                return;

            string shaderName = mat.shader.name;
            bool isOutline = shaderName == OutlineShaderName || shaderName == OutlinePostShaderName;
            if (isOutline && mat.HasProperty(s_DebugModeId))
                found.Add(mat);
        }

        /// <summary>
        /// 把场所内所有 <see cref="UnitOutlineBinder"/> 强制到指定描边档（-1 = 交回状态位驱动）。
        /// </summary>
        static void ForceBinderOutlineState(int state)
        {
            s_ForcedBinders = new List<UnitOutlineBinder>(Object.FindObjectsOfType<UnitOutlineBinder>());
            foreach (UnitOutlineBinder binder in s_ForcedBinders)
            {
                if (binder != null)
                    binder.DebugForcedState = state;
            }

            if (state >= 0 && s_ForcedBinders.Count == 0)
            {
                Debug.LogWarning("[OutlineDebugCapture] 场景里没有 UnitOutlineBinder（单位是在 play mode 的 "
                    + "BattleController.Awake 里生成的）。档 0 可能看不到描边——"
                    + "建议在 **play mode** 下运行本菜单项。");
            }
        }

        /// <summary>
        /// 等待一轮：play mode 下必须等**真实渲染帧**推进（EditorApplication.update 每帧会触发多次，
        /// 只数 update 次数可能在画面重绘之前就截图）；edit mode 无 player loop，退回数 update 次数。
        /// </summary>
        static void BeginWait()
        {
            s_Ticks = FramesPerStep;
            s_TargetFrame = Time.frameCount + FramesPerStep;
        }

        static bool IsWaiting()
        {
            if (Application.isPlaying)
                return Time.frameCount < s_TargetFrame;

            if (s_Ticks > 0)
            {
                s_Ticks--;
                return true;
            }
            return false;
        }

        /// <summary>EditorApplication.update 状态机：每档 改档 → 等帧 → 截图 → 等帧。</summary>
        static void Tick()
        {
            if (IsWaiting())
                return;

            if (s_Phase == 2)
            {
                // 收尾（延迟到最后一帧真被抓走之后，见下方 s_Phase=2 的说明）。
                ApplyDebugMode(0);
                ForceBinderOutlineState(-1);
                EditorApplication.update -= Tick;
                s_Running = false;
                s_Targets = null;

                Debug.Log("[OutlineDebugCapture] 采集流程结束（_DebugMode 已复位为 0，单位描边已交回状态位驱动）。\n"
                    + "  截图由 ScreenCapture 异步写盘，可能比本日志晚几帧才出现；\n"
                    + "  请到 " + s_OutputFolder + " 确认 5 张 PNG，并逐张对照 docs/描边Shader调试.md 的"
                    + "\"每档应看到什么\"。");
                return;
            }

            if (s_Phase == 0)
            {
                DebugLevel level = Levels[s_LevelIndex];
                ApplyDebugMode(level.Mode);

                // edit mode 下主动推一帧；play mode 下 player loop 本来就在跑。
                InternalEditorUtility.RepaintAllViews();
                EditorApplication.QueuePlayerLoopUpdate();
                BeginWait();
                s_Phase = 1;
                return;
            }

            // s_Phase == 1：此刻画面已是本档效果，抓图。
            DebugLevel current = Levels[s_LevelIndex];
            string fileName = "debug-mode-" + current.Mode + "-" + current.Slug + ".png";
            string fullPath = Path.Combine(s_OutputFolder, fileName);

            ScreenCapture.CaptureScreenshot(fullPath);
            Debug.Log("[OutlineDebugCapture] 已请求截图（档 " + current.Mode + " " + current.Meaning
                + "）-> " + fullPath);

            s_LevelIndex++;
            BeginWait();

            // 最后一档必须延迟复位：ScreenCapture 是异步的（在当前帧末尾真正取像素），
            // 若在同一 Tick 里紧接着 ApplyDebugMode(0)，最后一张就会拍到复位后的样子。
            // 实测（2026-09-13）：档 4 曾因此拍成档 0（红色本体 + 稀疏青描边）而不是法线/深度数据。
            s_Phase = s_LevelIndex >= Levels.Length ? 2 : 0;
        }

        static void ApplyDebugMode(int mode)
        {
            if (s_Targets == null)
                return;

            foreach (Material mat in s_Targets)
            {
                if (mat == null || !mat.HasProperty(s_DebugModeId))
                    continue;

                mat.SetFloat(s_DebugModeId, mode);
                // 材质资产标脏，避免打开材质面板时看到旧值；场景实例无需回写。
                EditorUtility.SetDirty(mat);
            }
        }
    }
}
