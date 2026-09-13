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
    ///
    /// 【本轮新增：小地图岛烘焙】按 tile 数据（<see cref="TerrainCatalog"/> / <see cref="PlatformClusterLayout"/>）
    ///   把「沙台 / 草格 / 海面」画进 <c>IslandLayer</c>：
    ///   · 旧版小地图岛是运行期 <c>BattleMinimap.BuildTiles</c> 用 <c>MinimapRules.TileColor</c> 画的灰岩色矩形
    ///     （r3 实测 #898D7E，与实岛沙 #F7D384 不符），而 <c>MinimapRules.cs</c> / <c>BattleMinimap.cs</c>
    ///     不在本波次文件域内；
    ///   · 本脚本按同一份瓦片数据逐格上色（浅台 = 沙 #F0D48A 系、抬起的台面 = 草 #6FA86F 系、
    ///     水 = <see cref="UiTheme.Sea"/>），岛轮廓 = 平台簇的真实逐格形状（不再是矩形兜底）；
    ///   · 因此运行期的瓦片点阵不再需要（会被烘焙层整片盖住，却仍要建 width×depth 个 Image），
    ///     本轮起 <c>BattleMinimap.terrain</c> 显式清空 —— 代价是地形被炸后小地图不再变化（静态岛）。
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
        const int FallbackLevelNumber = 1;

        /// <summary>
        /// 岛格沙色（浅台 / 滩）。【提案/待定】色值取 r3 美术复验工单给的 <c>#F0D48A 系</c>
        /// （实测实岛沙为 <c>#F7D384</c>；这里稍压一点亮度，避免小地图比实景还亮）。
        /// </summary>
        static readonly Color SandTileColor = new Color(0xF0 / 255f, 0xD4 / 255f, 0x8A / 255f, 1f);

        /// <summary>岛格草色（抬起的台面）。【提案/待定】色值取工单给的 <c>#6FA86F 系</c>。</summary>
        static readonly Color GrassTileColor = new Color(0x6F / 255f, 0xA8 / 255f, 0x6F / 255f, 1f);

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
            int levelNumber = ResolveLevelNumber(battle);

            RectTransform panel = EnsurePanel(canvas.transform, widthTiles, depthTiles);
            RectTransform dotLayer = EnsureDotLayer(panel);
            RectTransform tileLayer = EnsureTileLayer(dotLayer);
            RectTransform islandLayer = EnsureIslandLayer(dotLayer, tileLayer);
            int islandTiles = BakeIslandTiles(islandLayer, levelNumber, widthTiles, depthTiles);

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
                + "  竞技场: " + widthTiles + " × " + depthTiles + " 瓦片（关卡 " + levelNumber + "）\n"
                + "  岛层: " + IslandLayerName + "（沙/草逐格烘焙，共 " + islandTiles + " 格；水 = UI_SEA）\n"
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

        /// <summary>
        /// 按关卡瓦片数据烘焙小地图岛（幂等：每次接线先清空重建）。
        ///
        /// 【配色】浅台 / 滩 = <see cref="SandTileColor"/>，抬起来的台面（块高 &gt; 本簇最低块高）= <see cref="GrassTileColor"/>；
        /// 水格不画（点阵区底色 = 面板的「羊皮纸 × UI_SEA」，即 <see cref="UiTheme.Sea"/> 系）。
        /// 锚点口径与 <c>BattleMinimap.BuildTiles</c> 逐格一致，
        /// 保证单位点（按 <c>MinimapRules.ArenaToNormalized</c> 归一化定位）与岛格严格对齐。
        /// </summary>
        /// <returns>烘焙出的岛格数（0 = 该关未转写地形 / 非平台关）。</returns>
        static int BakeIslandTiles(RectTransform layer, int levelNumber, int widthTiles, int depthTiles)
        {
            if (layer == null)
                return 0;

            for (int i = layer.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(layer.GetChild(i).gameObject);

            // 只做平台化关卡：列式旧地形（level_4/27）整图都是「有地面」，逐格上色会糊成一块沙色大矩形，
            // 故非平台关直接关掉本层，把绘制让回运行期的灰岩色点阵。
            if (!TerrainCatalog.IsPlatformLevel(levelNumber))
            {
                layer.gameObject.SetActive(false);
                return 0;
            }

            layer.gameObject.SetActive(true);

            // 水不用画：运行期瓦片点阵已停画（见 WriteReferences 的 terrain 清空），
            // 点阵区的底色就是面板自己的「羊皮纸 × UI_SEA」底色（= UiTheme.Sea 系），
            // 再铺一层反而会在面板里出现一块色调不同的矩形。这里只画岛格。
            TileTerrainGrid grid = TerrainCatalog.Build(levelNumber, widthTiles, depthTiles);
            if (grid == null)
                return 0;

            int tiles = 0;
            for (int gy = 0; gy < depthTiles; gy++)
            {
                for (int gx = 0; gx < widthTiles; gx++)
                {
                    if (!grid.IsGroundAt(gx, gy))
                        continue;

                    var go = new GameObject("IslandTile_" + gx + "_" + gy,
                        typeof(RectTransform), typeof(Image));
                    var rect = go.GetComponent<RectTransform>();
                    rect.SetParent(layer, false);
                    rect.anchorMin = new Vector2((float)gx / widthTiles, 1f - (float)(gy + 1) / depthTiles);
                    rect.anchorMax = new Vector2((float)(gx + 1) / widthTiles, 1f - (float)gy / depthTiles);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;

                    var image = go.GetComponent<Image>();
                    image.color = IslandTileColor(grid, gx, gy);
                    image.raycastTarget = false;
                    tiles++;
                }
            }

            return tiles;
        }

        /// <summary>
        /// 岛格取色：块高高于所属平台簇的最低块高 = 抬起来的台面（草），否则 = 浅台/滩（沙）。
        /// 即「沙台打底、其上草格」，与 3D 场景里台地顶面长草、裙边是沙的观感一致。
        /// </summary>
        static Color IslandTileColor(TileTerrainGrid grid, int gx, int gy)
        {
            int cluster = grid.ClusterIndexOf(gx, gy);
            int minBlocks = cluster >= 0 ? grid.ClusterAt(cluster).MinBlocks : 0;
            return grid.BlocksAt(gx, gy) > minBlocks ? GrassTileColor : SandTileColor;
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

            // 【本轮取舍】terrain 显式清空：小地图岛改由 IslandLayer 按同一份 tile 数据烘焙成沙/草色，
            // 运行期再画一遍灰岩色点阵既会被整片盖住、又要白建 width×depth（level_1 = 850）个 Image。
            // 代价：地形被炸后小地图不再变化（静态岛）——恢复动态需先改 MinimapRules.cs / BattleMinimap.cs。
            SerializedProperty terrainProp = so.FindProperty("terrain");
            if (terrainProp != null)
                terrainProp.objectReferenceValue = null;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>小地图面板每瓦片像素数（**提案/待定**：原版 dotSize=3，本工程放大以便辨认）。</summary>
        const float DefaultPixelsPerTile = 5f;

        // ------------------------------------------------------------------
        // 场景查询工具（Editor 期；不使用 GameObject.Find）
        // ------------------------------------------------------------------

        /// <summary>读 BattleController 上的关卡资产（未指定时返回 null）。</summary>
        static LevelDefinition ReadLevel(BattleController battle)
        {
            var so = new SerializedObject(battle);
            SerializedProperty prop = so.FindProperty("level");
            return prop != null ? prop.objectReferenceValue as LevelDefinition : null;
        }

        /// <summary>解析当前场景实际会加载的关卡号（BattleController.level 为空时回落 level_1）。</summary>
        static int ResolveLevelNumber(BattleController battle)
        {
            LevelDefinition level = ReadLevel(battle);
            return level != null ? level.LevelNumber : FallbackLevelNumber;
        }

        static void ResolveArenaTiles(BattleController battle, out int widthTiles, out int depthTiles)
        {
            LevelDefinition level = ReadLevel(battle);

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
