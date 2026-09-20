using System.IO;
using PirateCrew.Battle;
using PirateCrew.SceneArt;
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
    ///
    /// 【一代退场后的岛层】本脚本不再按关卡瓦片烘焙岛层（一代数据源已删）。世界图运行时
    ///   走 <c>BattleMinimap.ConfigureWorldChartFromRuntime</c>（海图模式），样板三关走点阵层；
    ///   <c>BattleMinimap.terrain</c> 仍显式清空（瓦片点阵不参与）。
    ///     彻底修法（需改本波次文件域外的两个文件，见 docs/待办事项.md）：让 <c>MinimapRules.TileColor</c>
    ///     按格面语义取沙/草色、恢复 terrain 接线后删掉本烘焙层。
    /// </summary>
    public static class HudMinimapSceneSetup
    {
        const string BattleScenePath = "Assets/Scenes/Battle.unity";
        const string CanvasName = "BattleCanvas";
        const string MinimapPanelName = "MinimapPanel";
        const string DotLayerName = "DotLayer";
        const string TileLayerName = "TileLayer";

        /// <summary>小地图岛层（本轮新增：编辑器按 tile 数据烘焙的沙/草/海面，盖在瓦片点层之上）。</summary>
        const string IslandLayerName = "IslandLayer";

        const string Team0RootName = "Team0_Red";
        const string Team1RootName = "Team1_Blue";

        /// <summary>小地图在屏幕左上角的外边距（本波次统一口径 16px，与 BattleHudBuilder.Safe 一致；
        /// 原版 mapHolder 挂在 (20,20)，§2.3）。</summary>
        static readonly Vector2 PanelOffset = new Vector2(16f, -16f);

        /// <summary>点阵层退到面板边内（与 <c>BattleHudBuilder.PanelPadding</c> 同口径 14px；本波次由 24 收紧，
        /// 与亚克力玻璃面板的紧凑内边距保持一致，避免点阵离框太远显得"面板很大内容很小"）。</summary>
        const float PanelInnerPadding = 14f;

        /// <summary>标题条占位（与 <c>BattleHudBuilder.MinimapCaptionHeight</c> 同口径 22 + 与点阵 6px 间距）。</summary>
        const float CaptionStrip = 28f;

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

            // 一代退场后：小地图不再按关卡瓦片烘焙岛层（一代数据源已删）。
            // 世界图运行时走 BattleMinimap.ConfigureWorldChartFromRuntime（海图模式），
            // 样板三关走点阵层；旧场景里已烘焙的 IslandLayer 保持原样（不新增烘焙）。
            int widthTiles = ShowcaseLevels.WidthTiles;
            int depthTiles = ShowcaseLevels.DepthTiles;

            RectTransform panel = EnsurePanel(canvas.transform, widthTiles, depthTiles);
            RectTransform dotLayer = EnsureDotLayer(panel);
            RectTransform tileLayer = EnsureTileLayer(dotLayer);
            EnsureIslandLayer(dotLayer, tileLayer);

            var minimap = panel.GetComponent<BattleMinimap>();
            if (minimap == null)
                minimap = panel.gameObject.AddComponent<BattleMinimap>();

            WriteReferences(minimap, team0, team1, dotLayer, tileLayer, widthTiles, depthTiles);

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
                + "  面板口径: " + widthTiles + " × " + depthTiles + " 瓦片（样板第 1 关）\n"
                + "  岛层: " + IslandLayerName + "（不再烘焙；世界图走运行时海图模式）\n"
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
        /// <see cref="BattleUiTheme"/>/<see cref="BattleHudBuilder"/>（海图玻璃观感），
        /// 本脚本只负责补 <c>BattleMinimap</c> 组件与引用接线。
        /// 仅在面板缺失（没跑过 HUD 重建）时才按 <see cref="MinimapRules"/> 建一个同款兜底面。
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

            // ---- 以下仅在"兜底新建"时执行，避免覆盖 BattleHudBuilder 的玻璃皮 ----
            // 尺寸与竞技场同比例（不拉伸变形）：一颗瓦片 = pixelsPerTile 像素。
            Vector2 size = MinimapRules.PanelSizePixels(widthTiles, depthTiles, DefaultPixelsPerTile);
            panel.anchorMin = PanelAnchor;
            panel.anchorMax = PanelAnchor;
            panel.pivot = PanelAnchor;
            panel.anchoredPosition = PanelOffset;
            panel.sizeDelta = size;

            // 海图玻璃（深蓝绿亚克力，alpha 0.84）：与 BattleHudBuilder.ApplyMinimapSkin 同皮同参，
            // 兜底也不退回深棕木底。描边已烘进九宫格贴图（双色 1px，外扩 0），不再挂 UGUI Outline。
            var background = panel.gameObject.AddComponent<Image>();
            MenuUiBuilder.ApplyGlassSkin(background, GlassPanelSpriteBuilder.Tone.Sea);

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

            // 与 BattleHudBuilder.BuildMinimap 同口径：四周退 PanelInnerPadding、顶部再让出「海图」标题条，
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
        // 小地图岛层（本轮：按 tile 数据烘焙沙/草色，取代运行期的灰岩色点阵）
        // ------------------------------------------------------------------

        /// <summary>
        /// 取/建 <c>IslandLayer</c>：与点阵层同尺寸铺满，层级排在瓦片点层**之上**、
        /// 单位点（BattleMinimap 运行期追加到 DotLayer）之下。
        /// </summary>
        static RectTransform EnsureIslandLayer(RectTransform dotLayer, RectTransform tileLayer)
        {
            if (dotLayer == null)
                return null;

            Transform existing = FindChildByName(dotLayer, IslandLayerName);
            var layer = existing as RectTransform;

            if (layer == null)
            {
                var go = new GameObject(IslandLayerName, typeof(RectTransform));
                go.transform.SetParent(dotLayer, false);
                layer = go.GetComponent<RectTransform>();
            }

            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;

            // 绘制顺序（同层内先出现的先画）：瓦片点层(0) → 岛层(1) → 运行期单位点(最后)。
            if (tileLayer != null)
                tileLayer.SetAsFirstSibling();
            if (dotLayer.childCount > 1)
                layer.SetSiblingIndex(1);

            return layer;
        }

        // ------------------------------------------------------------------
        // 引用注入（SerializedObject，字段名与 BattleMinimap 一一对应）
        // ------------------------------------------------------------------

        static void WriteReferences(
            BattleMinimap minimap, Transform team0, Transform team1, RectTransform dotLayer,
            RectTransform tileLayer, int widthTiles, int depthTiles)
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

            // 瓦片点层引用照旧给上（层还在，只是不再由运行期画点阵）。
            SerializedProperty tileLayerProp = so.FindProperty("tileLayer");
            if (tileLayerProp != null)
                tileLayerProp.objectReferenceValue = tileLayer;

            // 【terrain 接回（r11 复盘）】旧取舍曾把 terrain 清空、靠烘焙岛层表现地形；
            // 一代退场后岛层不再烘焙，清空 terrain 导致样板关海图只剩单位点、观感近乎空板。
            // 现接 BattleTerrainView（运行时 RenderCollidersOnly 填 Grid）→ 样板关有灰岩
            // 点阵；世界图走运行时海图岛层（ConfigureWorldChartFromRuntime），两态都有内容。
            // 代价：width×depth 个一次性 Image（level_1 = 850），构建一次、刷新只写 alpha。
            BattleTerrainView terrainView = FindFirstComponent<BattleTerrainView>();
            SerializedProperty terrainProp = so.FindProperty("terrain");
            if (terrainProp != null)
                terrainProp.objectReferenceValue = terrainView;
            if (terrainView == null)
                Debug.LogWarning("[HudMinimapSceneSetup] 场景里未找到 BattleTerrainView，"
                    + "样板关小地图瓦片点阵不可用（单位点与世界图岛层不受影响）。");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>小地图面板每瓦片像素数（**提案/待定**：原版 dotSize=3，本工程放大以便辨认）。</summary>
        const float DefaultPixelsPerTile = 5f;

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
