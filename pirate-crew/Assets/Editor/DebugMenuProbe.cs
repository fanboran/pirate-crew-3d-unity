using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 「调试场景」按钮失灵取证（创始人 2026-10-08 报障：有按压反馈、无红字、启动器窗不出现）。
    /// 按压反馈可见 = 处理器 <c>OnShowcaseClicked</c> 已执行（ButtonFeedback 是其第一行），
    /// 无红字 = Toggle/Build 未抛异常——本探针回答剩下的唯一问题：<b>Build 到底建了什么、
    /// 建在哪、活没活</b>。
    ///
    /// 无头 -executeMethod：进 Play（MainMenu）→ 第 120 帧反射调用私有 OnShowcaseClicked
    /// （与真实点击同一入口）→ 第 160 帧把 DebugMenuHost 静态态 + 画布子树全量转储到
    /// <c>export/debugmenu-probe.txt</c> 后 Exit(0)。域重载存活模式抄 BattleHudScreenshot
    /// （SessionState 计数 + InitializeOnLoad 重挂）。
    /// </summary>
    [InitializeOnLoad]
    public static class DebugMenuProbe
    {
        const string KeyArmed = "DebugMenuProbe.Armed";
        const string KeyFrames = "DebugMenuProbe.Frames";
        const string ScenePath = "Assets/Scenes/Game/MainMenu.unity";
        const int ClickFrame = 120;   // 菜单装配/布局稳定余量
        const int DumpFrame = 160;    // 点击后再等 40 帧（首开构建 + 一帧延帧队列）

        static DebugMenuProbe()
        {
            if (Application.isBatchMode && !SessionState.GetBool(KeyArmed, false))
                return;
            if (SessionState.GetBool(KeyArmed, false))
                EditorApplication.update += Tick;   // 进 Play 域重载后重挂
        }

        public static void RunHeadless()
        {
            if (SessionState.GetBool(KeyArmed, false))
                return;
            SessionState.SetBool(KeyArmed, true);
            SessionState.SetInt(KeyFrames, 0);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
            EditorApplication.update += Tick;
            Debug.Log("[DebugMenuProbe] 已武装：第 " + ClickFrame + " 帧程序化点击，第 "
                + DumpFrame + " 帧转储。");
        }

        static int Frames
        {
            get => SessionState.GetInt(KeyFrames, 0);
            set => SessionState.SetInt(KeyFrames, value);
        }

        static void Tick()
        {
            if (!SessionState.GetBool(KeyArmed, false) || !EditorApplication.isPlaying)
                return;
            Frames++;
            if (Frames == ClickFrame)
            {
                var controller = Object.FindFirstObjectByType<global::PirateCrew.UI.MainMenuController>();
                MethodInfo click = controller != null
                    ? typeof(global::PirateCrew.UI.MainMenuController).GetMethod(
                        "OnShowcaseClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                    : null;
                Debug.Log("[DebugMenuProbe] 控制器=" + (controller != null ? controller.name : "<无>")
                    + "，处理器=" + (click != null ? "在" : "<缺失>"));
                click?.Invoke(controller, null);
            }
            if (Frames >= DumpFrame)
            {
                Dump();
                SessionState.SetBool(KeyArmed, false);
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }

        static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== DebugMenuProbe 转储（帧 " + Frames + "，点击发生在 " + ClickFrame + "）==");

            // DebugMenuHost 静态态（static class，反射取私有字段）
            System.Type host = typeof(global::PirateCrew.UI.DebugUi.DebugMenuHost);
            foreach (string fieldName in new[] { "_root", "_launcher", "_widgetGallery", "_partsGallery" })
            {
                FieldInfo f = host.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
                if (f == null)
                {
                    sb.AppendLine(fieldName + "：<反射失败>");
                    continue;
                }
                var rt = f.GetValue(null) as RectTransform;
                if (rt == null)
                {
                    sb.AppendLine(fieldName + "：null/未建");
                    continue;
                }
                sb.AppendLine(fieldName + "：" + HierarchyPath(rt.transform)
                    + " activeSelf=" + rt.gameObject.activeSelf
                    + " activeInHierarchy=" + rt.gameObject.activeInHierarchy
                    + " pos=" + rt.anchoredPosition.ToString("F1")
                    + " size=" + rt.sizeDelta.ToString("F1")
                    + " 子数=" + rt.childCount);
            }

            // 主菜单画布全子树（名字/激活/层级深度）
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                sb.AppendLine("画布：<无>");
            }
            else
            {
                sb.AppendLine("== 画布 " + canvas.name + " 子树 ==");
                DumpChildren(sb, canvas.transform, 0);
            }

            string path = Path.Combine(Directory.GetCurrentDirectory(), "..", "export", "debugmenu-probe.txt");
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DebugMenuProbe] 转储已写 " + path);
        }

        static void DumpChildren(StringBuilder sb, Transform t, int depth)
        {
            foreach (Transform child in t)
            {
                sb.AppendLine(new string(' ', depth * 2) + child.name
                    + (child.gameObject.activeSelf ? "" : " [inactive]")
                    + " 子数=" + child.childCount);
                if (depth < 4)
                    DumpChildren(sb, child, depth + 1);
            }
        }

        static string HierarchyPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }
    }
}
