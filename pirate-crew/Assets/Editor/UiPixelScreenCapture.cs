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

        static string OverlayTargetName(string overlay)
        {
            switch (overlay)
            {
                case "settings": return "SettingsPanel";
                case "confirm": return "ConfirmDialog";   // MainMenu 确认框根名（旧值 BackConfirmDialog 不存在）
                default: return null;
            }
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
                        string target = OverlayTargetName(SessionState.GetString(OverlayKey, ""));
                        if (!string.IsNullOrEmpty(target))
                        {
                            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(
                                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                            {
                                if (t.name == target)
                                {
                                    t.gameObject.SetActive(true);
                                    break;
                                }
                            }
                        }
                    }
                    if (EditorApplication.isPlaying && Time.frameCount > 95)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        // 截图同拍一份**实机文本度量**到命令桥结果文件（字体档/画布相位/渲染 shader）：
                        // 屏上"文字笔画时粗时细"的客观判据是「显示字号 ÷ 字体原生档 = 1」+
                        // 「画布空间相位 = 0」，这两项靠肉眼看不出来，落成数字才可复核。
                        try { FontProbeDumper.DumpSceneTexts(); FontProbeDumper.DumpSliders(); }
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
