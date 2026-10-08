using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
                // 第二版取证：**真实点击链**模拟——射线打点 + executeHierarchy（不直调处理器）。
                // 第一版直调 OnShowcaseClicked 已证明 Toggle/Build 本体是通的；本版回答
                // 「指针事件到底有没有/被谁吃掉」。射线结果写进转储（_raycastDump）。
                var controller = Object.FindFirstObjectByType<global::PirateCrew.UI.MainMenuController>();
                var go = controller != null
                    ? FindButton(controller.transform.parent)
                    : null;
                Debug.Log("[DebugMenuProbe] 控制器=" + (controller != null ? controller.name : "<无>")
                    + "，按钮=" + (go != null ? go.name : "<未找到>"));
                if (go != null)
                {
                    Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, go.transform.position);
                    var ped = new PointerEventData(EventSystem.current) { position = screenPos };
                    var hits = new System.Collections.Generic.List<RaycastResult>();
                    EventSystem.current.RaycastAll(ped, hits);
                    var sbHits = new StringBuilder();
                    sbHits.AppendLine("== 射线命中（自上而下，第一个吃点击）==");
                    foreach (var hit in hits)
                        sbHits.AppendLine("  " + hit.gameObject.name
                            + " (depth=" + hit.sortingOrder + ")");

                    // 监听器计数 + 可交互态 + 事件链分段取证：
                    // ①onClick 挂没挂监听（UnityEvent m_Calls 反射）；②直接 Invoke 与
                    // ③ExecuteEvents 点击链的结果分开报——哪一段断的一目了然。
                    Button button = go.GetComponent<Button>();
                    FieldInfo callsField = typeof(UnityEngine.Events.UnityEventBase).GetField(
                        "m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
                    object calls = callsField != null ? callsField.GetValue(button.onClick) : null;
                    FieldInfo countField = calls != null ? calls.GetType().GetField(
                        "m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic) : null;
                    object runtimeCalls = countField != null ? countField.GetValue(calls) : null;
                    var runtimeList = runtimeCalls as System.Collections.ICollection;
                    sbHits.AppendLine("onClick 运行期监听数=" + (runtimeList != null ? runtimeList.Count : -1)
                        + "，interactable=" + button.interactable
                        + "，activeInHierarchy=" + go.activeInHierarchy
                        + "，组件启用=" + button.enabled);

                    ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
                    sbHits.AppendLine("ExecuteEvents 点击链后 _root 建否=" + (HostRoot() != null));
                    button.onClick.Invoke();
                    sbHits.AppendLine("onClick.Invoke() 后 _root 建否=" + (HostRoot() != null));
                    _raycastDump = sbHits.ToString();
                }
            }
            if (Frames >= DumpFrame)
            {
                Dump();
                SessionState.SetBool(KeyArmed, false);
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }

        static string _raycastDump = "<未采集>";

        /// <summary>DebugMenuHost 私有静态 _root 的当前值（建窗判据）。</summary>
        static object HostRoot()
        {
            return typeof(global::PirateCrew.UI.DebugUi.DebugMenuHost)
                .GetField("_root", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null);
        }

        static UnityEngine.GameObject FindButton(Transform canvas)
        {
            if (canvas == null)
                return null;
            Transform button = canvas.Find("MenuWindow/ShowcaseButton");
            return button != null ? button.gameObject : null;
        }

        static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== DebugMenuProbe 转储（帧 " + Frames + "，点击发生在 " + ClickFrame + "）==");
            sb.Append(_raycastDump);

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
