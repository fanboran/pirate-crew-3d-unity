using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实机四屏截图采集（命令桥出口，编辑器常开不退出）：主菜单 / 选关 / 船员管理 / 战斗 HUD。
    /// 开指定场景 → 进 Play → 钉 1920×1080 视口 → 第 95 帧后 ScreenCapture → 退 Play。
    /// 产物落 <c>export/ui-pixel-4c/&lt;场景名&gt;.png</c>（1:1 屏幕像素，供走查判字/件比例）。
    /// 机制同 <see cref="UiShowcaseSceneSetup"/> 的截图状态机（SessionState 跨域重载存活）。
    /// </summary>
    public static class UiPixelScreenCapture
    {
        const string CaptureDir = "export/ui-pixel-4c";

        const string PendingKey = "UiPixelScreenCapture.Pending";
        const string QueueKey = "UiPixelScreenCapture.Queue";
        const string QueueIndexKey = "UiPixelScreenCapture.QueueIndex";
        const string StateKey = "UiPixelScreenCapture.State";
        const string WaitKey = "UiPixelScreenCapture.Wait";
        const string SceneKey = "UiPixelScreenCapture.Scene";
        const string PathKey = "UiPixelScreenCapture.Path";
        const string SelectedKey = "UiPixelScreenCapture.Selected";
        const string OverlayKey = "UiPixelScreenCapture.Overlay";
        const string AutoQuitKey = "UiPixelScreenCapture.AutoQuit";

        /// <summary>主菜单。</summary>
        public static void CaptureMainMenu() => Start("MainMenu");
        /// <summary>选关。</summary>
        public static void CaptureLevelSelect() => Start("LevelSelect");
        /// <summary>船员管理。</summary>
        public static void CaptureCrewManagement() => Start("CrewManagement");
        /// <summary>战斗 HUD。</summary>
        public static void CaptureBattle() => Start("Battle");

        /// <summary>按场景名单采（遥控桥 capture:&lt;场景名&gt; 用；需要编辑器 GUI）。
        /// 场景名带 <c>+settings</c> / <c>+confirm</c> 后缀：进 Play 第 60 帧激活
        /// SettingsPanel / BackConfirmDialog 再截——标题带窗体/单选钮/滑条/窗控钮的走查入口。</summary>
        public static void CaptureScene(string sceneName) => Start(sceneName);

        /// <summary>四屏连采（一次编辑器启动全部拿到；Battle 放最后——需要选中角色入画）。
        /// 无头走查入口：<c>-executeMethod PirateCrew.EditorTools.UiPixelScreenCapture.CaptureAllFour</c>
        /// （**非 batchmode**：ScreenCapture 需要图形设备，挂 GUI 编辑器启动参数即可）。</summary>
        public static void CaptureAllFour()
        {
            SessionState.SetString(QueueKey, string.Join("|", new[] { "MainMenu", "LevelSelect", "CrewManagement", "Battle" }));
            SessionState.SetInt(QueueIndexKey, 0);
            Start("MainMenu");
        }

        /// <summary>调试面板四连采（遥控通道独立跑不动时挂 GUI 编辑器启动参数用）：
        /// 走主菜单「调试场景」→ 启动器按钮的真用户路径，逐面板进 Play 截图。
        /// 采完自动退出编辑器（<c>-executeMethod</c> 场景没有别的收尾机会）。</summary>
        public static void CaptureDebugPanels()
        {
            SessionState.SetString(QueueKey, string.Join("|", new[]
                { "MainMenu+dbg-widget", "MainMenu+dbg-newsprite", "MainMenu+dbg-menubar", "MainMenu+dbg-parts", "MainMenu+dbg-dup" }));
            SessionState.SetInt(QueueIndexKey, 0);
            SessionState.SetBool(AutoQuitKey, true);
            Start("MainMenu+dbg-widget");
        }

        static void Start(string sceneName)
        {
            // "MainMenu+settings" / "Battle+confirm"：后缀 = 进 Play 后激活对应隐藏弹窗
            string overlay = null;
            string baseScene = sceneName;
            int cut = sceneName.IndexOf('+');
            if (cut > 0)
            {
                overlay = sceneName.Substring(cut + 1);
                baseScene = sceneName.Substring(0, cut);
            }
            string dir = Path.GetFullPath(CaptureDir);
            SessionState.SetString(SceneKey, baseScene);
            SessionState.SetString(OverlayKey, overlay ?? "");
            SessionState.SetString(PathKey, Path.Combine(dir,
                (overlay != null ? OverlayName(overlay) : baseScene) + ".png"));
            SessionState.SetInt(StateKey, 0);
            SessionState.SetInt(WaitKey, 0);
            SessionState.SetBool(SelectedKey, false);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.update += CaptureStep;
            Debug.Log("[UiPixelScreenCapture] 开始采集 " + sceneName + " → " + CaptureDir);
        }

        static string OverlayName(string overlay) => overlay;

        /// <summary>按名找**已激活**对象（走用户路径点按钮用）。</summary>
        static Transform FindActive(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name == name)
                    return t;
            return null;
        }

        /// <summary>按名找**未激活**对象（弹窗默认隐藏，走不到就兜底直接激活）。</summary>
        static Transform FindInactive(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name && !t.gameObject.activeInHierarchy)
                    return t;
            return null;
        }

        /// <summary>场景里是否已有该名字的激活对象。</summary>
        static bool IsActiveNamed(string name) => FindActive(name) != null;

        /// <summary>已置 active（业务当前值）的选项块数量——诊断"控制器状态有没有落地"。</summary>
        static int CountActiveOptionSets()
        {
            int n = 0;
            foreach (var s in UnityEngine.Object.FindObjectsByType<PirateCrew.UI.Stick.SketchButtonSet>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.Active)
                    n++;
            return n;
        }

        static string OverlayTargetName(string overlay)
        {
            switch (overlay)
            {
                case "settings": return "SettingsPanel";
                case "confirm": return "ConfirmDialog";   // MainMenu 确认框根名（旧值 BackConfirmDialog 不存在）
                default: return null;
            }
        }

        /// <summary>dbg-* 调试面板 overlay → 启动器按钮名（DebugMenuHost.MakeLauncherButton 命名；
        /// 改启动器标签时这里必须同步——名不同步则取证静默扑空，实锄过一次）。</summary>
        static string DebugOpenButton(string key)
        {
            switch (key)
            {
                case "widget": return "Open_组件实摆";
                case "newsprite": return "Open_新建精灵（xml 装载）";
                case "menubar": return "Open_Aseprite 菜单栏";
                case "parts": return "Open_部件陈列廊";
                case "dup": return "Open_库对话框 Duplicate/Goto";
                default: return null;
            }
        }

        /// <summary>递归转储调试树（名/激活/画布几何）到 export/unity-command-result.txt 尾部。</summary>
        static void DumpTree(Transform node, int depth)
        {
            if (node == null || depth > 8)
                return;
            var rect = node as RectTransform;
            string geo = rect != null
                ? string.Format(" pos=({0:F0},{1:F0}) size=({2:F0}x{3:F0})",
                    rect.anchoredPosition.x, rect.anchoredPosition.y,
                    rect.rect.width, rect.rect.height)
                : "";
            System.IO.File.AppendAllText(
                Path.GetFullPath("export/unity-command-result.txt"),
                new string(' ', depth * 2) + node.name
                + (node.gameObject.activeSelf ? "" : " [inactive]")
                + geo + "\n",
                System.Text.Encoding.UTF8);
            for (int i = 0; i < node.childCount; i++)
                DumpTree(node.GetChild(i), depth + 1);
        }

        [InitializeOnLoadMethod]
        static void RehookAfterReload()
        {
            if (SessionState.GetBool(PendingKey, false))
                EditorApplication.update += CaptureStep;
        }

        static void CaptureStep()
        {
            string sceneName = SessionState.GetString(SceneKey, "");
            string path = SessionState.GetString(PathKey, "");
            switch (SessionState.GetInt(StateKey, 0))
            {
                case 0:
                    if (EditorApplication.isPlaying)
                        return;   // 等上一段 Play 完全退出再开场景
                    EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity",
                        OpenSceneMode.Single);
                    Screen.SetResolution(1920, 1080, false);
                    SessionState.SetInt(StateKey, 1);
                    break;
                case 1:
                    EditorApplication.isPlaying = true;
                    SessionState.SetInt(StateKey, 2);
                    break;
                case 2:
                    UiShowcaseSceneSetup.ForceGameViewSizePublic(1920, 1080);
                    if (EditorApplication.isPlaying && Time.frameCount == 60
                        && sceneName == "Battle" && !SessionState.GetBool(SelectedKey, false))
                    {
                        // 选中一名红队角色，让底部武器面板（含文字按钮）入画——走查对象。
                        SessionState.SetBool(SelectedKey, true);
                        var controller = UnityEngine.Object.FindObjectOfType<PirateCrew.Battle.BattleController>();
                        PirateCrew.Battle.PirateBase unit = null;
                        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<PirateCrew.Battle.PirateBase>(true))
                        {
                            if (candidate.TeamIndex == 0)
                            {
                                unit = candidate;
                                break;
                            }
                        }
                        if (controller != null && unit != null)
                            controller.SelectCharacter(unit);
                    }
                    if (EditorApplication.isPlaying && Time.frameCount == 60)
                    {
                        // 隐藏弹窗走查：直接激活（视觉验证不依赖控制器接线）。
                        // GameObject.Find 找不到未激活对象——含未激活全量搜名。
                        string overlayName = SessionState.GetString(OverlayKey, "");
                        string target = OverlayTargetName(overlayName);
                        if (!string.IsNullOrEmpty(target))
                        {
                            // 设置面板走**用户真路径**：点「设置」钮（控制器 OpenSettings →
                            // RefreshSettingsControls）——这样音量滑条显示真实百分比、选项块
                            // 显示真实当前值；直接 SetActive 会让这两类控制在图里全是未选中/0%。
                            Transform opener = overlayName == "settings" ? FindActive("SettingsButton") : null;
                            if (opener != null)
                            {
                                var openerButton = opener.GetComponent<UnityEngine.UI.Button>();
                                if (openerButton != null)
                                    openerButton.onClick.Invoke();
                                Debug.Log("[UiPixelScreenCapture] 设置覆盖层：opener=" + opener.name
                                    + " button=" + (openerButton != null)
                                    + " 面板已激活=" + IsActiveNamed(target)
                                    + " 选项块 active 数=" + CountActiveOptionSets());
                            }
                            else
                            {
                                Debug.LogWarning("[UiPixelScreenCapture] 设置覆盖层：找不到 SettingsButton，"
                                    + "只能直接激活面板（控制器状态不会应用）。");
                            }
                            if (!IsActiveNamed(target))
                            {
                                Transform t = FindInactive(target);
                                if (t != null)
                                    t.gameObject.SetActive(true);
                            }
                        }
                    }
                    if (EditorApplication.isPlaying && Time.frameCount == 60
                        && SessionState.GetString(OverlayKey, "").StartsWith("dbg-"))
                    {
                        string overlayName = SessionState.GetString(OverlayKey, "");
                        // 调试面板走查（真用户路径）：点主菜单「调试场景」开启动器，
                        // 再点对应启动钮——面板全为运行时构建，这是唯一不用模拟指针的入口。
                        Transform showcase = FindActive("ShowcaseButton");
                        Transform launcher = null;
                        if (showcase != null)
                        {
                            var showButton = showcase.GetComponent<UnityEngine.UI.Button>();
                            if (showButton != null)
                                showButton.onClick.Invoke();
                            string open = DebugOpenButton(overlayName.Substring(4));
                            if (open != null)
                                launcher = FindActive(open);
                        if (launcher != null)
                        {
                            var openButton = launcher.GetComponent<UnityEngine.UI.Button>();
                            if (openButton != null)
                                openButton.onClick.Invoke();
                        }
                        }
                        Debug.Log("[UiPixelScreenCapture] 调试面板覆盖层：" + overlayName
                            + " showcase=" + (showcase != null)
                            + " launcher=" + (launcher != null ? launcher.name : "<null>"));
                    }
                    if (EditorApplication.isPlaying && Time.frameCount > 95)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        // 调试面板树转储（dbg-* 时面板已建好——分帧构建是点击后下一帧才落）
                        if (SessionState.GetString(OverlayKey, "").StartsWith("dbg-"))
                        {
                            try { DumpTree(FindActive("AseDlg_new_sprite"), 0); }
                            catch (System.Exception e) { Debug.LogWarning("[UiPixelScreenCapture] 树转储失败：" + e.Message); }
                        }
                        // 截图同拍一份**实机文本度量**到命令桥结果文件（字体档/画布相位/渲染 shader）：
                        // 屏上"文字笔画时粗时细"的客观判据是「显示字号 ÷ 字体原生档 = 1」+
                        // 「画布空间相位 = 0」，这两项靠肉眼看不出来，落成数字才可复核。
                        try { FontProbeDumper.DumpSceneTexts(); FontProbeDumper.DumpSliders(); FontProbeDumper.DumpControlStates(); }
                        catch (Exception e) { Debug.LogError("[UiPixelScreenCapture] 文本度量转储失败：" + e.Message); }
                        ScreenCapture.CaptureScreenshot(path);
                        SessionState.SetInt(StateKey, 3);
                        SessionState.SetInt(WaitKey, 0);
                    }
                    break;
                case 3:
                    if (SessionState.GetInt(WaitKey, 0) > 20)
                    {
                        Debug.Log("[UiPixelScreenCapture] 截图完成：" + path);
                        EditorApplication.isPlaying = false;
                        SessionState.SetInt(StateKey, 4);
                        SessionState.SetInt(WaitKey, 0);
                    }
                    else
                    {
                        SessionState.SetInt(WaitKey, SessionState.GetInt(WaitKey, 0) + 1);
                    }
                    break;
                case 4:
                    if (!EditorApplication.isPlaying && SessionState.GetInt(WaitKey, 0) > 5)
                    {
                        Debug.Log("[UiPixelScreenCapture] 流程结束：" + path
                            + "（存在=" + File.Exists(path) + "）");
                        // 【四屏连采】队列还有下一场景就接着采（Start 会重置状态并重挂 update）。
                        string queue = SessionState.GetString(QueueKey, "");
                        int index = SessionState.GetInt(QueueIndexKey, 0);
                        var scenes = string.IsNullOrEmpty(queue) ? new string[0] : queue.Split('|');
                        int next = index + 1;
                        if (next < scenes.Length)
                        {
                            SessionState.SetInt(QueueIndexKey, next);
                            Start(scenes[next]);
                            return;
                        }
                        SessionState.SetBool(PendingKey, false);
                        EditorApplication.update -= CaptureStep;
                        // 自动退出（CaptureDebugPanels 的 -executeMethod 场景没有人工关窗机会）
                        if (SessionState.GetBool(AutoQuitKey, false))
                        {
                            SessionState.SetBool(AutoQuitKey, false);
                            EditorApplication.Exit(0);
                        }
                    }
                    else if (!EditorApplication.isPlaying)
                    {
                        SessionState.SetInt(WaitKey, SessionState.GetInt(WaitKey, 0) + 1);
                    }
                    break;
            }
        }
    }
}
