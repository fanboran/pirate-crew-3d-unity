using PirateCrew.Battle;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PirateCrew.EditorTools.BuildSystem;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// Battle 场景的 HUD 小地图**增量接线**（不重建场景，只补小地图部分，可反复执行）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/HUD/接线 Battle 小地图
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.HudMinimapSceneSetup.WireMinimap
    ///
    /// 【前置】先跑 <see cref="BattleSceneSetup.BuildAll"/> 生成 Battle.unity；
    ///         本脚本只做增量接线，若场景/装配根缺失会明确报错而不是静默造半个场景。
    ///
    /// 【空窗暂态（创始人 2026-09-28 裁决）】海图只留 Aseprite 窗体外框、**内容清空**：
    ///   本脚本不再建 DotLayer / TileLayer / 岛层，dotLayer / tileLayer / terrain 显式写 null
    ///   （清掉旧场景残留引用），<see cref="BattleMinimap"/> 见 dotLayer 为空即整组件休眠。
    ///   恢复内容时三处同改：<see cref="BattleHudBuilder"/>.BuildMinimap 重建 DotLayer 子树、
    ///   本脚本恢复层接线、BattleMinimap 撤休眠守卫。
    ///
    /// 【为什么单独一个脚本】小地图是 M2 之后追加的功能，而「其它 Editor 脚本」由协调者/其它 agent
    /// 管理，不能改 <c>BattleSceneSetup.cs</c>；本脚本复用它的代码模式
    /// （SerializedObject 写私有 <c>[SerializeField]</c>、不手写 .unity YAML）。
    ///
    /// 【幂等】面板按名复用；已存在的只补缺失组件并重写引用，不重复创建。
    /// 【职责边界】本脚本**只接线**（BattleMinimap 的 unitRoots 等字段），
    ///   面板的位置/尺寸/外框归 <see cref="BattleUiTheme"/> / <see cref="BattleHudBuilder"/>；
    ///   只有面板缺失时才兜底建一个同款窗体，避免覆盖 UI 波次的样式。
    /// </summary>
    public static class HudMinimapSceneSetup
    {
        static readonly string BattleScenePath = BuildScenes.PathOf("Battle");
        const string CanvasName = "BattleCanvas";
        const string MinimapPanelName = "MinimapPanel";
        const string Team0RootName = "Team0_Red";
        const string Team1RootName = "Team1_Blue";

        /// <summary>小地图窗体在屏幕左上角的外边距（画布像素；与 BattleHudBuilder.Safe 同源=4。
        /// 兜底新建路径才用得到——正常路径的几何归 BattleHudBuilder）。</summary>
        static readonly Vector2 PanelOffset = new Vector2(4f, -4f);

        /// <summary>面板相对屏幕左上角的锚点（ugui：锚点 (0,1) = 左上）。</summary>
        static readonly Vector2 PanelAnchor = new Vector2(0f, 1f);

        [MenuItem("PirateCrew/HUD/接线 Battle 小地图")]
        public static void WireMinimap()
        {
            // 用 AssetDatabase 判存在，避免 batchmode 下依赖当前工作目录。
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BattleScenePath) == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] 找不到 " + BattleScenePath
                    + "，请先运行 PirateCrew.EditorTools.BattleSceneSetup.BuildAll。");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[HudMinimapSceneSetup] 打开场景失败: " + BattleScenePath);
                return;
            }

            BattleController battle = FindFirstComponent<BattleController>();
            if (battle == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] Battle 场景里没有 BattleController；"
                    + "请先运行 BattleSceneSetup.BuildAll 重建场景。");
                return;
            }

            Transform team0 = FindTransformByName(Team0RootName);
            Transform team1 = FindTransformByName(Team1RootName);
            if (team0 == null && team1 == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] 找不到单位根节点 " + Team0RootName + " / " + Team1RootName
                    + "；请先运行 BattleSceneSetup.BuildAll。");
                return;
            }

            Canvas canvas = FindCanvas();
            if (canvas == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] 找不到 HUD Canvas（" + CanvasName + "）；"
                    + "请先运行 BattleSceneSetup.BuildAll。");
                return;
            }

            RectTransform panel = EnsurePanel(canvas.transform);

            var minimap = panel.GetComponent<BattleMinimap>();
            if (minimap == null)
                minimap = panel.gameObject.AddComponent<BattleMinimap>();

            WriteReferences(minimap, team0, team1);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BattleScenePath))
            {
                Debug.LogError("[HudMinimapSceneSetup] 保存场景失败: " + BattleScenePath);
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[HudMinimapSceneSetup] 小地图接线完成（空窗暂态：外框窗体 + 内容清空）。\n"
                + "  场景: " + BattleScenePath + "\n"
                + "  面板: " + MinimapPanelName + "（左上角 theme 窗体，标题带「海图」）\n"
                + "  内容: dotLayer / tileLayer / terrain 显式置空（BattleMinimap 休眠）\n"
                + "  单位根: " + (team0 != null ? team0.name : "null") + " / "
                + (team1 != null ? team1.name : "null") + "（恢复内容时即用）");
        }

        // ------------------------------------------------------------------
        // 层级
        // ------------------------------------------------------------------

        /// <summary>
        /// 取/建 <c>MinimapPanel</c>。
        ///
        /// 【样式归属】已存在的面板**只复用、不覆写**：位置/尺寸/外框归
        /// <see cref="BattleUiTheme"/>/<see cref="BattleHudBuilder"/>（theme 窗体观感），
        /// 本脚本只负责补 <see cref="BattleMinimap"/> 组件与引用接线。
        /// 仅在面板缺失（没跑过 HUD 重建）时才按同款窗体兜底新建。
        /// </summary>
        static RectTransform EnsurePanel(Transform canvas)
        {
            Transform existing = FindChildByName(canvas, MinimapPanelName);
            var panel = existing as RectTransform;

            if (panel != null)
                return panel;

            var go = new GameObject(MinimapPanelName, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            panel = go.GetComponent<RectTransform>();

            // ---- 以下仅在"兜底新建"时执行，避免覆盖 BattleHudBuilder 的窗体皮 ----
            panel.anchorMin = PanelAnchor;
            panel.anchorMax = PanelAnchor;
            panel.pivot = PanelAnchor;
            panel.anchoredPosition = PanelOffset;
            panel.sizeDelta = new Vector2(64f, 44f);   // 与 BattleHudBuilder 同源足迹

            UiKit.EnsureWindow(panel, PixelTone.Frame, UiStrings.BattleMinimapTitle,
                font: null, UiSkin.Font.Body, helpButton: false, closeButton: false);
            return panel;
        }

        // ------------------------------------------------------------------
        // 引用注入（SerializedObject，字段名与 BattleMinimap 一一对应）
        // ------------------------------------------------------------------

        static void WriteReferences(BattleMinimap minimap, Transform team0, Transform team1)
        {
            var so = new SerializedObject(minimap);

            // 单位根照旧写上（休眠组件不消费；恢复内容时无需重跑接线）。
            SerializedProperty roots = so.FindProperty("unitRoots");
            if (roots == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] BattleMinimap.unitRoots 字段未找到（序列化字段名漂移？）");
                return;
            }

            var list = new System.Collections.Generic.List<Object>(2);
            if (team0 != null)
                list.Add(team0);
            if (team1 != null)
                list.Add(team1);

            roots.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
                roots.GetArrayElementAtIndex(i).objectReferenceValue = list[i];

            // 【空窗暂态】内容层显式置 null（清掉旧场景/旧 Prefab 残留引用）——
            // BattleMinimap 见 dotLayer 为空即休眠。
            so.FindProperty("dotLayer").objectReferenceValue = null;

            SerializedProperty tileLayer = so.FindProperty("tileLayer");
            if (tileLayer != null)
                tileLayer.objectReferenceValue = null;

            SerializedProperty terrain = so.FindProperty("terrain");
            if (terrain != null)
                terrain.objectReferenceValue = null;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static T FindFirstComponent<T>() where T : Component
        {
            T[] all = Object.FindObjectsOfType<T>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    return all[i];
            }
            return null;
        }

        static Transform FindTransformByName(string name)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform found = FindChildRecursive(roots[i].transform, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        static Canvas FindCanvas()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform found = FindChildRecursive(roots[i].transform, CanvasName);
                if (found != null)
                {
                    Canvas canvas = found.GetComponent<Canvas>();
                    if (canvas != null)
                        return canvas;
                }
            }

            return FindFirstComponent<Canvas>();
        }

        static Transform FindChildByName(Transform parent, string name)
        {
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }
            return null;
        }

        static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null)
                return null;
            if (parent.name == name)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
