using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// Battle 场景的 HUD 小地图**增量接线**（不重建场景，只补小地图部分，可反复执行）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/HUD/接线 Battle 小地图
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.HudMinimapSceneSetup.WireMinimap
    ///
    /// 【前置】先跑 <see cref="M2BattleSceneSetup.BuildAll"/> 生成 Battle.unity；
    ///         本脚本只做增量接线，若场景/装配根缺失会明确报错而不是静默造半个场景。
    ///
    /// 【为什么单独一个脚本】小地图是 M2 之后追加的功能，而「其它 Editor 脚本」由协调者/其它 agent
    /// 管理，不能改 <c>M2BattleSceneSetup.cs</c>；本脚本复用它的代码模式
    /// （SerializedObject 写私有 <c>[SerializeField]</c>、不手写 .unity YAML）。
    ///
    /// 【幂等】面板按名复用；已存在的层级只补缺失子物体并重写引用，不重复创建。
    /// </summary>
    public static class HudMinimapSceneSetup
    {
        const string BattleScenePath = "Assets/Scenes/Battle.unity";
        const string CanvasName = "BattleCanvas";
        const string MinimapPanelName = "MinimapPanel";
        const string DotLayerName = "DotLayer";
        const string Team0RootName = "Team0_Red";
        const string Team1RootName = "Team1_Blue";
        const int FallbackLevelNumber = 1;

        /// <summary>小地图在屏幕左上角的内边距（原版 mapHolder 挂在 (20,20)，§2.3）。</summary>
        static readonly Vector2 PanelOffset = new Vector2(16f, -48f);

        /// <summary>面板相对屏幕左上角的锚点（ugui：锚点 (0,1) = 左上）。</summary>
        static readonly Vector2 PanelAnchor = new Vector2(0f, 1f);

        [MenuItem("PirateCrew/HUD/接线 Battle 小地图")]
        public static void WireMinimap()
        {
            // 用 AssetDatabase 判存在，避免 batchmode 下依赖当前工作目录。
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BattleScenePath) == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] 找不到 " + BattleScenePath
                    + "，请先运行 PirateCrew.EditorTools.M2BattleSceneSetup.BuildAll。");
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
                    + "请先运行 M2BattleSceneSetup.BuildAll 重建场景。");
                return;
            }

            Transform team0 = FindTransformByName(Team0RootName);
            Transform team1 = FindTransformByName(Team1RootName);
            if (team0 == null && team1 == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] 找不到单位根节点 " + Team0RootName + " / " + Team1RootName
                    + "；请先运行 M2BattleSceneSetup.BuildAll。");
                return;
            }

            Canvas canvas = FindCanvas();
            if (canvas == null)
            {
                Debug.LogError("[HudMinimapSceneSetup] 找不到 HUD Canvas（" + CanvasName + "）；"
                    + "请先运行 M2BattleSceneSetup.BuildAll。");
                return;
            }

            ResolveArenaTiles(battle, out int widthTiles, out int depthTiles);

            RectTransform panel = EnsurePanel(canvas.transform, widthTiles, depthTiles);
            RectTransform dotLayer = EnsureDotLayer(panel);

            var minimap = panel.GetComponent<BattleMinimap>();
            if (minimap == null)
                minimap = panel.gameObject.AddComponent<BattleMinimap>();

            WriteReferences(minimap, team0, team1, dotLayer, widthTiles, depthTiles);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BattleScenePath))
            {
                Debug.LogError("[HudMinimapSceneSetup] 保存场景失败: " + BattleScenePath);
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[HudMinimapSceneSetup] 小地图接线完成。\n"
                + "  场景: " + BattleScenePath + "\n"
                + "  面板: " + MinimapPanelName + "（左上角，原版 mapHolder 在 (20,20)，§2.3）\n"
                + "  竞技场: " + widthTiles + " × " + depthTiles + " 瓦片\n"
                + "  单位根: " + (team0 != null ? team0.name : "null") + " / "
                + (team1 != null ? team1.name : "null"));
        }

        // ------------------------------------------------------------------
        // 层级
        // ------------------------------------------------------------------

        static RectTransform EnsurePanel(Transform canvas, int widthTiles, int depthTiles)
        {
            Transform existing = FindChildByName(canvas, MinimapPanelName);
            var panel = existing as RectTransform;

            if (panel == null)
            {
                var go = new GameObject(MinimapPanelName, typeof(RectTransform));
                go.transform.SetParent(canvas, false);
                panel = go.GetComponent<RectTransform>();
            }

            // 尺寸与竞技场同比例（不拉伸变形）：一颗瓦片 = pixelsPerTile 像素。
            Vector2 size = MinimapRules.PanelSizePixels(widthTiles, depthTiles, DefaultPixelsPerTile);
            panel.anchorMin = PanelAnchor;
            panel.anchorMax = PanelAnchor;
            panel.pivot = PanelAnchor;
            panel.anchoredPosition = PanelOffset;
            panel.sizeDelta = size;

            var background = panel.GetComponent<Image>();
            if (background == null)
                background = panel.gameObject.AddComponent<Image>();
            background.color = MinimapRules.BackgroundColor;
            background.raycastTarget = false;

            var border = panel.GetComponent<Outline>();
            if (border == null)
                border = panel.gameObject.AddComponent<Outline>();
            border.effectColor = MinimapRules.BorderColor;
            border.effectDistance = new Vector2(1f, -1f);

            return panel;
        }

        static RectTransform EnsureDotLayer(RectTransform panel)
        {
            Transform existing = FindChildByName(panel, DotLayerName);
            var layer = existing as RectTransform;

            if (layer == null)
            {
                var go = new GameObject(DotLayerName, typeof(RectTransform));
                go.transform.SetParent(panel, false);
                layer = go.GetComponent<RectTransform>();
            }

            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;
            return layer;
        }

        // ------------------------------------------------------------------
        // 引用注入（SerializedObject，字段名与 BattleMinimap 一一对应）
        // ------------------------------------------------------------------

        static void WriteReferences(
            BattleMinimap minimap, Transform team0, Transform team1, RectTransform dotLayer,
            int widthTiles, int depthTiles)
        {
            var so = new SerializedObject(minimap);

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

            so.FindProperty("dotLayer").objectReferenceValue = dotLayer;
            so.FindProperty("arenaWidth").floatValue = widthTiles;
            so.FindProperty("arenaDepth").floatValue = depthTiles;
            so.FindProperty("pixelsPerTile").floatValue = DefaultPixelsPerTile;
            // 0 = 由 MinimapRules.DotSizePixels(pixelsPerTile) 推导（单一来源）。
            so.FindProperty("dotSizePixels").floatValue = 0f;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>小地图面板每瓦片像素数（**提案/待定**：原版 dotSize=3，本工程放大以便辨认）。</summary>
        const float DefaultPixelsPerTile = 5f;

        // ------------------------------------------------------------------
        // 场景查询工具（Editor 期；不使用 GameObject.Find）
        // ------------------------------------------------------------------

        static void ResolveArenaTiles(BattleController battle, out int widthTiles, out int depthTiles)
        {
            LevelDefinition level = null;
            var so = new SerializedObject(battle);
            SerializedProperty prop = so.FindProperty("level");
            if (prop != null)
                level = prop.objectReferenceValue as LevelDefinition;

            if (level != null)
            {
                widthTiles = level.WidthTiles;
                depthTiles = level.HeightTiles;
                return;
            }

            LevelData fallback = LevelCatalog.Get(FallbackLevelNumber);
            widthTiles = fallback.WidthTiles;
            depthTiles = fallback.HeightTiles;
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
