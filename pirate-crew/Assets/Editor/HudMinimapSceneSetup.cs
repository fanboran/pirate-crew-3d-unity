using System.IO;
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

        // ------------------------------------------------------------------
        // P2-2 岸线侵蚀参数（把「轴对齐矩形岛」打散成不规则岸线）
        // ------------------------------------------------------------------

        /// <summary>边缘格朝向水一侧的缩进下限（单位 = 格宽；r4 工单 0.2-0.4 格）。</summary>
        const float ErosionInsetMin = 0.20f;

        /// <summary>边缘格朝向水一侧的缩进上限（单位 = 格宽）。</summary>
        const float ErosionInsetMax = 0.40f;

        /// <summary>凸角圆角 Sprite 的持久资产路径（单角圆角：缺角在贴图左上）。</summary>
        const string CoastCornerSpritePath = "Assets/Art/Textures/UI/MinimapCoastCorner.png";

        /// <summary>凸角 Sprite 的烘焙尺寸（像素）。</summary>
        const int CoastCornerSpriteSize = 32;

        /// <summary>凸角圆弧半径占边长的比例（0.42 → 圆角明显但仍是方块主体）。</summary>
        const float CoastCornerRadiusRatio = 0.42f;

        static Sprite _coastCornerSprite;

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
        ///
        /// 【P2-2 岸线侵蚀（r4：三块岛是轴对齐矩形，加一个漂浮小矩形）】
        /// 光按 tile 逐格画，矩形数据仍会画出矩形轮廓（平台簇本身是若干嵌套矩形 Step）。
        /// 本轮在逐格基础上做两层处理，把直角岸线打散：
        ///   ① <b>半格侵蚀</b>：朝向水的边按确定性哈希缩进 <see cref="ErosionInsetMin"/>–<see cref="ErosionInsetMax"/> 格宽
        ///      （同一 (格,边) 每次都得同一值，可复现、可截图对比，不用 Random）；
        ///   ② <b>凸角圆角化</b>：相邻两条水边的凸角格改用单角圆角 Sprite，并用 <c>localScale</c> 镜像把缺角
        ///      旋到正确那一角（贴图缺角在左上；镜像映射见 <see cref="BakeIslandTiles"/> 内注释）；
        ///   ③ <b>剔除孤立漂浮块</b>：四邻皆水的单格与任何岛都不连通，在小地图上读作"UI 残留"（r4 那个漂浮小矩形
        ///      经查是关卡的「北侧小空岛」簇 ④，共 8 格、并非单格残留；单格残留在这里被剔除，簇 ④ 则被同一套
        ///      侵蚀+圆角重画成不规则小岛，不再是一个漂浮方块）。
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

            Sprite cornerSprite = GetCoastCornerSprite();   // 生成失败回落方角（不阻塞接线）
            float tileW = 1f / widthTiles;
            float tileH = 1f / depthTiles;

            int tiles = 0;
            int isolated = 0;
            for (int gy = 0; gy < depthTiles; gy++)
            {
                for (int gx = 0; gx < widthTiles; gx++)
                {
                    if (!IsGroundAt(grid, gx, gy))
                        continue;

                    // 四邻是否水（越界按水算）。gy-1 = 屏幕上方（z 小）、gy+1 = 屏幕下方。
                    bool waterLeft = !IsGroundAt(grid, gx - 1, gy);
                    bool waterRight = !IsGroundAt(grid, gx + 1, gy);
                    bool waterUp = !IsGroundAt(grid, gx, gy - 1);
                    bool waterDown = !IsGroundAt(grid, gx, gy + 1);
                    int waterSides = (waterLeft ? 1 : 0) + (waterRight ? 1 : 0)
                                     + (waterUp ? 1 : 0) + (waterDown ? 1 : 0);

                    // ③ 孤立漂浮块（与任何岛格不连通）：剔除，避免小地图出现漂浮小方块。
                    if (waterSides == 4)
                    {
                        isolated++;
                        continue;
                    }

                    var go = new GameObject("IslandTile_" + gx + "_" + gy,
                        typeof(RectTransform), typeof(Image));
                    var rect = go.GetComponent<RectTransform>();
                    rect.SetParent(layer, false);

                    var image = go.GetComponent<Image>();
                    image.color = IslandTileColor(grid, gx, gy);
                    image.raycastTarget = false;

                    bool convexCorner = waterSides == 2
                        && (waterLeft && waterUp || waterLeft && waterDown
                            || waterRight && waterUp || waterRight && waterDown);

                    if (convexCorner && cornerSprite != null)
                    {
                        // ② 凸角格：整格铺满 + 单角圆角 Sprite，靠镜像缩放把缺角旋到水里那一侧。
                        // 贴图缺角在「屏幕左上」；localScale 镜像映射：
                        //   缺角目标 = 左上 → (1,1) / 右上 → (-1,1) / 左下 → (1,-1) / 右下 → (-1,-1)。
                        image.sprite = cornerSprite;
                        image.type = Image.Type.Simple;
                        rect.anchorMin = new Vector2(gx * tileW, 1f - (gy + 1) * tileH);
                        rect.anchorMax = new Vector2((gx + 1) * tileW, 1f - gy * tileH);
                        rect.offsetMin = Vector2.zero;
                        rect.offsetMax = Vector2.zero;
                        rect.localScale = new Vector3(waterRight ? -1f : 1f, waterDown ? -1f : 1f, 1f);
                    }
                    else
                    {
                        // ① 直边/尖端：朝向水的边做确定性半格侵蚀（0.20–0.40 格宽），其余边贴满。
                        float insetLeft = waterLeft ? ErosionInset(gx, gy, 0) : 0f;
                        float insetRight = waterRight ? ErosionInset(gx, gy, 1) : 0f;
                        float insetDown = waterDown ? ErosionInset(gx, gy, 2) : 0f;
                        float insetUp = waterUp ? ErosionInset(gx, gy, 3) : 0f;

                        // 直接用归一化 anchor 表达缩进（offset 恒 0）：与层级像素尺寸解耦，
                        // 不依赖 RectTransform 布局是否已刷新。
                        rect.anchorMin = new Vector2(
                            gx * tileW + insetLeft * tileW,
                            1f - (gy + 1) * tileH + insetDown * tileH);
                        rect.anchorMax = new Vector2(
                            (gx + 1) * tileW - insetRight * tileW,
                            1f - gy * tileH - insetUp * tileH);
                        rect.offsetMin = Vector2.zero;
                        rect.offsetMax = Vector2.zero;
                    }

                    tiles++;
                }
            }

            LogIslandComposition(grid, widthTiles, depthTiles, tiles, isolated);
            return tiles;
        }

        /// <summary>取格子是否地面；越界按水（false）。</summary>
        static bool IsGroundAt(TileTerrainGrid grid, int gx, int gy)
        {
            if (grid == null)
                return false;
            if (gx < 0 || gy < 0 || gx >= grid.WidthTiles || gy >= grid.DepthTiles)
                return false;
            return grid.IsGroundAt(gx, gy);
        }

        /// <summary>
        /// 确定性哈希（Wang hash 变体）→ [0,1)，用作某格某边的侵蚀缩进量。
        /// 【为什么不用 UnityEngine.Random】接线可反复执行，必须每次得到同一条岸线，
        /// 否则每次重建 HUD 小地图形状都会变，无法做截图前后对比。
        /// </summary>
        static float ErosionInset(int gx, int gy, int side)
        {
            uint h = (uint)(gx * 73856093 ^ gy * 19349663 ^ side * 83492791);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            float t = (h & 0xFFFFFFu) / (float)0xFFFFFFu;
            return Mathf.Lerp(ErosionInsetMin, ErosionInsetMax, t);
        }

        /// <summary>
        /// 输出岛/簇组成日志：既便于报告核对「漂浮小矩形」是谁，也便于测试断言。
        /// 小簇（≤12 格）单独点名——r4 的漂浮小矩形即 <c>sky_islet_north</c>（8 格）这个真簇。
        /// </summary>
        static void LogIslandComposition(TileTerrainGrid grid, int widthTiles, int depthTiles,
            int tiles, int isolated)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[HudMinimapSceneSetup] 岛层组成：").Append(tiles).Append(" 格（剔除孤立漂浮块 ")
              .Append(isolated).Append(" 个）。");
            int clusterCount = grid.ClusterCount;
            for (int c = 0; c < clusterCount; c++)
            {
                PlatformClusterInfo info = grid.ClusterAt(c);
                int cells = 0;
                for (int gy = info.Z0; gy <= info.Z1 && gy < depthTiles; gy++)
                {
                    for (int gx = info.X0; gx <= info.X1 && gx < widthTiles; gx++)
                    {
                        if (gx >= 0 && gy >= 0 && grid.ClusterIndexOf(gx, gy) == c)
                            cells++;
                    }
                }

                sb.Append("\n  簇 ").Append(c).Append(" ").Append(info.Name)
                  .Append("：").Append(cells).Append(" 格，包络 x").Append(info.X0).Append("-").Append(info.X1)
                  .Append(" / z").Append(info.Z0).Append("-").Append(info.Z1);
                if (cells <= 12)
                    sb.Append("（小簇：小地图上易被读作漂浮小矩形）");
            }

            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        // 凸角圆角 Sprite（生成一次落盘，随场景复用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 取/生成「单角圆角」贴图（缺角在贴图左上，其余三角为实心方角）。
        /// 持久资产：负 localScale 镜像即可复用成四个角的圆角，无需四个 Sprite。
        /// 生成失败返回 null（调用方回落到方角侵蚀，不阻塞接线）。
        /// </summary>
        static Sprite GetCoastCornerSprite()
        {
            if (_coastCornerSprite != null)
                return _coastCornerSprite;

            _coastCornerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoastCornerSpritePath);
            if (_coastCornerSprite != null)
                return _coastCornerSprite;

            try
            {
                EnsureFolder("Assets/Art");
                EnsureFolder("Assets/Art/Textures");
                EnsureFolder("Assets/Art/Textures/UI");

                const int n = CoastCornerSpriteSize;
                float radius = n * CoastCornerRadiusRatio;
                var pixels = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float fx = x + 0.5f;
                        float fy = y + 0.5f;   // 纹理坐标 y 向上：左上 = x 小、y 大
                        bool inside = true;
                        if (fx < radius && fy > n - radius)
                        {
                            float dx = fx - radius;
                            float dy = fy - (n - radius);
                            inside = dx * dx + dy * dy <= radius * radius;
                        }

                        pixels[y * n + x] = inside
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(255, 255, 255, 0);
                    }
                }

                var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                byte[] png = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);

                string absolute = Path.Combine(Application.dataPath,
                    CoastCornerSpritePath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(absolute, png);
                AssetDatabase.ImportAsset(CoastCornerSpritePath, ImportAssetOptions.ForceSynchronousImport);

                var importer = AssetImporter.GetAtPath(CoastCornerSpritePath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.alphaIsTransparency = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                _coastCornerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoastCornerSpritePath);
                return _coastCornerSprite;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[HudMinimapSceneSetup] 生成凸角圆角 Sprite 失败，岛角回落方角："
                    + e.Message);
                return null;
            }
        }

        /// <summary>递归确保资产目录存在（AssetDatabase.CreateFolder 不建中间层级）。</summary>
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
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
