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
    /// 【职责边界】本脚本**只接线**（BattleMinimap 的 unitRoots/dotLayer/tileLayer/terrain 等字段），
    ///   面板的位置/尺寸/背景/描边归 <see cref="BattleUiTheme"/> / <see cref="BattleHudBuilder"/>
    ///   （木质框观感）；只有面板缺失时才兜底建一个朴素面板，避免覆盖 UI 波次的样式。
    /// </summary>
    public static class HudMinimapSceneSetup
    {
        const string BattleScenePath = "Assets/Scenes/Battle.unity";
        const string CanvasName = "BattleCanvas";
        const string MinimapPanelName = "MinimapPanel";
        const string DotLayerName = "DotLayer";
        const string TileLayerName = "TileLayer";
        const string Team0RootName = "Team0_Red";
        const string Team1RootName = "Team1_Blue";
        const int FallbackLevelNumber = 1;

        /// <summary>小地图在屏幕左上角的外边距（统一口径 24px；原版 mapHolder 挂在 (20,20)，§2.3）。</summary>
        static readonly Vector2 PanelOffset = new Vector2(24f, -24f);

        /// <summary>点阵层退到面板边 24px 内（与 BattleHudBuilder.PanelPadding 同口径，避免点阵贴面板边框）。</summary>
        const float PanelInnerPadding = 24f;

        /// <summary>标题条占位 = 标题高 22 + 与点阵 8px 间距（顶部为「海图」标题留白）。</summary>
        const float CaptionStrip = 30f;

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
            RectTransform tileLayer = EnsureTileLayer(dotLayer);

            var minimap = panel.GetComponent<BattleMinimap>();
            if (minimap == null)
                minimap = panel.gameObject.AddComponent<BattleMinimap>();

            // 瓦片地形点阵的数据源：同场景的 BattleTerrainView（[SerializeField] 直连，禁 Find）。
            BattleTerrainView terrainView = FindFirstComponent<BattleTerrainView>();

            WriteReferences(minimap, team0, team1, dotLayer, tileLayer, terrainView, widthTiles, depthTiles);

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

        /// <summary>
        /// 取/建 <c>MinimapPanel</c>。
        ///
        /// 【样式归属】已存在的面板**只复用、不覆写**：位置/尺寸/背景/描边归
        /// <see cref="BattleUiTheme"/>/<see cref="BattleHudBuilder"/>（木质框观感），
        /// 本脚本只负责补 <c>BattleMinimap</c> 组件与引用接线。
        /// 仅在面板缺失（没跑过 HUD 重建）时才按 <see cref="MinimapRules"/> 建一个朴素兜底面。
        /// </summary>
        static RectTransform EnsurePanel(Transform canvas, int widthTiles, int depthTiles)
        {
            Transform existing = FindChildByName(canvas, MinimapPanelName);
            var panel = existing as RectTransform;

            if (panel != null)
                return panel;

            var go = new GameObject(MinimapPanelName, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            panel = go.GetComponent<RectTransform>();

            // ---- 以下仅在"兜底新建"时执行，避免覆盖 BattleHudBuilder 的木框样式 ----
            // 尺寸与竞技场同比例（不拉伸变形）：一颗瓦片 = pixelsPerTile 像素。
            Vector2 size = MinimapRules.PanelSizePixels(widthTiles, depthTiles, DefaultPixelsPerTile);
            panel.anchorMin = PanelAnchor;
            panel.anchorMax = PanelAnchor;
            panel.pivot = PanelAnchor;
            panel.anchoredPosition = PanelOffset;
            panel.sizeDelta = size;

            var background = panel.gameObject.AddComponent<Image>();
            // 海图面板水蓝系底（§1.3 UI_SEA）；兜底也不退回深棕，与 BattleHudBuilder 的皮一致。
            background.color = UiTheme.Sea;
            background.raycastTarget = false;

            var border = panel.gameObject.AddComponent<Outline>();
            border.effectColor = UiTheme.Brass;
            border.effectDistance = new Vector2(3f, -3f);

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

            // 与 BattleHudBuilder.BuildMinimap 同口径：四周退 24px、顶部再让出「海图」标题条，
            // 这样本脚本在 HUD 重建之后执行也不会把点阵层的安全边距重置回贴边框。
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = new Vector2(PanelInnerPadding, PanelInnerPadding);
            layer.offsetMax = new Vector2(-PanelInnerPadding, -(PanelInnerPadding + CaptionStrip));
            return layer;
        }

        /// <summary>瓦片点层（TileLayer）：铺在点阵层之下，一格一颗点（§8.1 两档 alpha）。</summary>
        static RectTransform EnsureTileLayer(RectTransform dotLayer)
        {
            if (dotLayer == null)
                return null;

            Transform existing = FindChildByName(dotLayer, TileLayerName);
            var layer = existing as RectTransform;

            if (layer == null)
            {
                var go = new GameObject(TileLayerName, typeof(RectTransform));
                go.transform.SetParent(dotLayer, false);
                layer = go.GetComponent<RectTransform>();
            }

            layer.SetAsFirstSibling();   // 瓦片点在单位点之下
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
            RectTransform tileLayer, BattleTerrainView terrainView, int widthTiles, int depthTiles)
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

            // 瓦片地形点阵：数据源 + 点层（字段在 BattleMinimap 上；找不到就给 null，BattleMinimap 会退回不画点阵）。
            SerializedProperty terrainProp = so.FindProperty("terrain");
            if (terrainProp != null)
                terrainProp.objectReferenceValue = terrainView;

            SerializedProperty tileLayerProp = so.FindProperty("tileLayer");
            if (tileLayerProp != null)
                tileLayerProp.objectReferenceValue = tileLayer;

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
