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
        const string StateKey = "UiPixelScreenCapture.State";
        const string WaitKey = "UiPixelScreenCapture.Wait";
        const string SceneKey = "UiPixelScreenCapture.Scene";
        const string PathKey = "UiPixelScreenCapture.Path";
        const string SelectedKey = "UiPixelScreenCapture.Selected";

        /// <summary>主菜单。</summary>
        public static void CaptureMainMenu() => Start("MainMenu");
        /// <summary>选关。</summary>
        public static void CaptureLevelSelect() => Start("LevelSelect");
        /// <summary>船员管理。</summary>
        public static void CaptureCrewManagement() => Start("CrewManagement");
        /// <summary>战斗 HUD。</summary>
        public static void CaptureBattle() => Start("Battle");

        static void Start(string sceneName)
        {
            string dir = Path.GetFullPath(CaptureDir);
            SessionState.SetString(SceneKey, sceneName);
            SessionState.SetString(PathKey, Path.Combine(dir, sceneName + ".png"));
            SessionState.SetInt(StateKey, 0);
            SessionState.SetInt(WaitKey, 0);
            SessionState.SetBool(SelectedKey, false);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.update += CaptureStep;
            Debug.Log("[UiPixelScreenCapture] 开始采集 " + sceneName + " → " + CaptureDir);
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
                        var controller = Object.FindObjectOfType<PirateCrew.Battle.BattleController>();
                        PirateCrew.Battle.PirateBase unit = null;
                        foreach (var candidate in Object.FindObjectsOfType<PirateCrew.Battle.PirateBase>(true))
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
                    if (EditorApplication.isPlaying && Time.frameCount > 95)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
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
                        SessionState.SetBool(PendingKey, false);
                        EditorApplication.update -= CaptureStep;
                        Debug.Log("[UiPixelScreenCapture] 流程结束：" + path
                            + "（存在=" + File.Exists(path) + "）");
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
