using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **八张大海域海图各自一个像素化试点场景**的装配（一次跑完；也是这八张图宣传素材的成图入口）。
    ///
    /// 【为什么一族八场景，而不是把海图塞进样板关那几个】每张图的**内容与跨度都不一样**
    /// （跨度 150–280 m、站面顶高 0.5–7.5 m、出生 6–12 人），用一个场景靠开关切换就是"折叠态契约"
    /// 最忌讳的按参数变化的可见性。一图一场景，每个场景都是**静态、可单独打开、可单独出图**的内容。
    ///
    /// 【内容逐条来自哪（一条都不自己编）】
    /// <list type="bullet">
    ///   <item>地图定义：`WorldMapCatalog`（数据真源 `Assets/Data/WorldMaps/*.asset` + `_golden/*.json`）；</item>
    ///   <item>地形/装饰/kit/道具/礁石：调**运行时同一个合成入口** `WorldMapComposer.Build`
    ///         （站面 box → BoxCollider + 分带材质、确定性散布装饰、手摆道具吸附地表、
    ///         `ReefFieldRules` 礁石场）。内容一项都不在本文件里手摆；</item>
    ///   <item>船员：出生表 `map.Spawns`（世界坐标），脚底高度用
    ///         `WorldMapRules.HeightAtWorld(AllStandBoxes(map), (x,z))` —— 与
    ///         `WorldMapRuntime.BuildBattlePlan` 同一个口径（那边再加 `LevelGeometry.UnitPivotHeight`，
    ///         本路径的 `PixelartStageKit.PlaceCrew` 内部自己加 `CrewRootToFeetOffset`）；</item>
    ///   <item>镜头口径在 <see cref="PixelartLevelScene"/>（本文件只读表，不写任何机位数字）。</item>
    /// </list>
    ///
    /// 【为什么内容要烘进场景（不走运行时合成）】本路径的出图在**播放器**里跑
    /// （`PlayerArtCapture` 加载场景直接拍），而 `WorldMapComposer` 是战斗装配期的东西
    /// （需要 `WorldMapAssetSet`、需要 Battle 侧通电）。试点场景把这次合成结果**固化一份快照**，
    /// 于是"打开即所见"，且出图链与战斗装配链彻底解耦。代价说清楚：
    /// `Object.Instantiate` 的 kit 件**不是预制体实例**，改了 kit FBX 或地图数据必须**重跑本装配器**，
    /// 场景不会自己跟着变。
    ///
    /// 【材质（这条路径的硬约束，理由见 PixelartStageKit 类头）】两路并行：
    /// <list type="bullet">
    ///   <item>kit/道具/装饰的 23 个 `Kit_*` 槽位是 URP/Lit、都不透明、都写 `_BaseColor` ⇒
    ///         走 `SwapMaterials(..., deriveMissing: true)` 的派生档（从原材质取色，逐条打日志）；</item>
    ///   <item>站面底材是 `WorldMapComposer` **运行时 new 出来的** `PirateCrew/PirateTerrain` 材质，
    ///         三色写在 `_SandColor/_GrassColor/_RockColor`（**没有 `_BaseColor`**）⇒ 派生档对它必然
    ///         取不到色（报错 + 返回 null），必须**显式映射**。按运行时材质名
    ///         `WorldMapStand_Band{0,1,2}_{Sand,Grass,Rock}` 挂三份本路径材质，取色 = 各档**顶面**
    ///         的主色（band0 纯沙 / band1 纯草 / band2 纯岩，由 `WorldMapComposer.HeightSandGrassOf`
    ///         的推导保证）。台面侧壁在原链是岩（坡度项），本路径单色 albedo 下侧壁与顶面同色——
    ///         试点看的是台面读法，这一条是明确的代价。</item>
    /// </list>
    ///
    /// 【海面是替身，如实标注】海图的活水面是 `PirateCrew/Ocean`（半透明 + 运行时圆盘 + 波浪），
    /// **不在本路径的材质口径里**（G-buffer 没有混合、也拉不动它的网格）；与样板关同样用一块
    /// **同高度（`LevelGeometry.WaterSurfaceY` = −0.4）的平色海面**占位，尺寸按 span 放大到能铺满
    /// 宽机位画面。它只影响观感的丰富度，不影响"场地 / 单位 / 构图"这三件要判的事。
    ///
    /// 【HorizonFeatureRules 远景环：**排除**，并说清为什么】`WorldMapComposer.Build` 会顺手调
    /// `HorizonFeatureRules.Place` 把本图专属远景件（鲸/触手/巨肋…）撒在**距图心 200–280 u 的环带**上。
    /// 本装配器在合成后把这一组（节点名 `HorizonFeature_*`）**删掉**：
    /// <list type="bullet">
    ///   <item>这组件的构图口径是给**55° 透视全景机位**调的（可见海面斜距 ~350 u，见
    ///         `HorizonFeatureRules` 类头）；本路径的宽机位是 30° 正交、可见高度 = 1.0 × span
    ///         （半宽 0.889·span、半高 0.5·span）。环带内界 200 u：span ≤ 220 的五张图（101/102/103/105/107）
    ///         环带**整圈在画面外**；span 240–280 的三张（104/106/108）半宽 213–249 &gt; 200，
    ///         只有左右边缘擦到一点（垂直方向 0.5·span = 120–140 &gt; 环带最大 100，仍在框内）——
    ///         是"边缘掠过几个剪影"，不是"看得见一圈天际线"；</item>
    ///   <item>留在场景里会把内容包围盒撑到 ±280，`AssertFraming` 的"镜头对着内容"就退化成恒真，
    ///         构图断言等于没查；还会为了画面外的几何白抬 `farClipPlane`。</item>
    /// </list>
    /// 判定：**排除**，让宽机位读的是"这一张图的战场"（这也正是本族场景要回答的问题）。
    /// 手摆的 `map.Horizon`（远岛/远帆/云带）**保留**——那是地图作者按本图布局摆在场心周围的，
    /// 与出生区/站面同属这张图的内容，不是规则层撒的环。
    ///
    /// 【FloatingPropView：剥掉】合成会把贴水的浮标/Boat/Ship 类道具挂上
    /// <see cref="FloatingPropView"/>（`LateUpdate` 取 `OceanRig` 波高做起伏）。本场景是"静态快照"、
    /// 且海面是替身平面（没有 `OceanRig`）——组件在播放器里会静默 no-op，但**留一个运行时会动
    /// transform 的组件在"静态场景"里是误导**（以后谁给试点加个 OceanRig，道具就会对着不动的
    /// 替身海面上下浮）。故合成后逐个 `DestroyImmediate`，并打出剥掉的件数。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙八张海图试点场景（101–108）</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartWorldMapPilotSetup.BuildAll</c>
    /// （单图调试用 <c>BuildLevel101</c>…<c>BuildLevel108</c> / <c>BuildSingle(level)</c>）。
    /// </summary>
    public static class PixelartWorldMapPilotSetup
    {
        const string LogTag = "[PixelartWorldMapPilotSetup]";

        /// <summary>kit 资产表（Battle 场景持有同一份；本路径直接读资产，不走场景接线）。</summary>
        const string AssetSetPath = "Assets/Art/Models/WorldKit/WorldMapAssetSet.asset";

        /// <summary>
        /// 运行时站面材质名（`WorldMapComposer.CreateTerrainMaterial` 的命名）：显式映射的键。
        /// **必须与那边逐字一致**——名字错一个字符，派生档就会来接管它们、而它们没有 `_BaseColor`，
        /// 于是三档站面全部落空（报错在前、站面在出图上变白/花屏在后）。
        /// </summary>
        const string BandSandName = "WorldMapStand_Band0_Sand";
        const string BandGrassName = "WorldMapStand_Band1_Grass";
        const string BandRockName = "WorldMapStand_Band2_Rock";

        const string SeaMaterialName = "PixelartMap_Sea";
        const string BandSandMaterialName = "PixelartMap_BandSand";
        const string BandGrassMaterialName = "PixelartMap_BandGrass";
        const string BandRockMaterialName = "PixelartMap_BandRock";

        /// <summary>远景环节点名前缀（`WorldMapComposer.BuildKitPlacement` 的 group 名 + "_"）。</summary>
        const string HorizonFeaturePrefix = "HorizonFeature_";

        /// <summary>合成结果根名前缀（`WorldMapComposer.Build` 的 "WorldMap_" + map.Id）。</summary>
        const string ComposedRootPrefix = "WorldMap_";

        /// <summary>
        /// 站位支撑面高度与逻辑高度场的允许偏差（米）。**不是随手给的容差**：
        /// 站面 BoxCollider 的顶面比视觉顶面高 `0.03 × box 高`（`WorldMapComposer.BuildStandBox` 把
        /// 视觉盒下沉 0.06 再给 collider 中心 +0.03×height）——band0 约 +0.2 m、高层站面约 +0.35 m。
        /// 0.5 把这段**已知的几何偏移**排除在报警外，只留真正的"数据/几何对不上"。
        /// </summary>
        const float SupportTolerance = 0.5f;

        /// <summary>构图中心的竖直偏差超过这个值就告警（与样板关装配器同一阈值）。</summary>
        const float VerticalFramingTolerance = 6f;

        [MenuItem("PirateCrew/Pixelart/烘焙八张海图试点场景（101–108）")]
        public static void BuildAll()
        {
            IReadOnlyList<WorldMapDefinition> maps = WorldMapCatalog.All;
            if (maps == null || maps.Count == 0)
            {
                Debug.LogError(LogTag + " WorldMapCatalog 里一张海图都没有（读不到 "
                    + "Assets/Data/WorldMaps/*.asset？）——没有可烘的内容。");
                return;
            }

            WorldMapAssetSet assetSet = AssetDatabase.LoadAssetAtPath<WorldMapAssetSet>(AssetSetPath);
            if (assetSet == null)
            {
                // 缺表不是"少几个装饰"：Build 会把每一个 Get(name) 都拿到 null 从而只留下站面灰盒，
                // 出图上"图是空的"却不报错。宁可整批不烘，也不要产出八张误导性的场景。
                Debug.LogError(LogTag + " 找不到 WorldKit 资产表 " + AssetSetPath
                    + "（先跑 PirateCrew.EditorTools.WorldMapAssetSetBuilder.BuildAll）——本批未烘焙。");
                return;
            }

            int ok = 0;
            int skipped = 0;
            for (int i = 0; i < maps.Count; i++)
            {
                WorldMapDefinition map = maps[i];
                if (!PixelartLevelScene.TryGet(map.LevelNumber, out PixelartLevelScene.View view))
                {
                    Debug.LogError(LogTag + " 取景表里没有海图关卡号 " + map.LevelNumber + "（" + map.Id
                        + "）——该图未烘焙。往 PixelartLevelScene._views 补一行再跑。");
                    skipped++;
                    continue;
                }

                if (BuildOne(map, view, assetSet))
                    ok++;
                else
                    skipped++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogTag + " 海图试点场景烘焙完成：" + ok + "/" + maps.Count + " 张"
                + (skipped > 0 ? "（" + skipped + " 张未完成，见上面的报错）" : "") + "。");
        }

        // ---- 单图入口（batchmode 调试用；菜单只留 BuildAll）----

        public static void BuildLevel101() => BuildSingle(101);
        public static void BuildLevel102() => BuildSingle(102);
        public static void BuildLevel103() => BuildSingle(103);
        public static void BuildLevel104() => BuildSingle(104);
        public static void BuildLevel105() => BuildSingle(105);
        public static void BuildLevel106() => BuildSingle(106);
        public static void BuildLevel107() => BuildSingle(107);
        public static void BuildLevel108() => BuildSingle(108);

        static void BuildSingle(int levelNumber)
        {
            if (!WorldMapCatalog.TryGetByLevelNumber(levelNumber, out WorldMapDefinition map))
            {
                Debug.LogError(LogTag + " 海图目录里没有关卡号 " + levelNumber + "。");
                return;
            }

            if (!PixelartLevelScene.TryGet(levelNumber, out PixelartLevelScene.View view))
            {
                Debug.LogError(LogTag + " 取景表里没有关卡号 " + levelNumber + "。");
                return;
            }

            WorldMapAssetSet assetSet = AssetDatabase.LoadAssetAtPath<WorldMapAssetSet>(AssetSetPath);
            if (assetSet == null)
            {
                Debug.LogError(LogTag + " 找不到 WorldKit 资产表 " + AssetSetPath + "——本图未烘焙。");
                return;
            }

            BuildOne(map, view, assetSet);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>烘一张图：内容 → 剥动态件 → 材质 → 船员 → 光 → 相机/rig → 断言 → 存盘登记。</summary>
        static bool BuildOne(WorldMapDefinition map, PixelartLevelScene.View view, WorldMapAssetSet assetSet)
        {
            int level = view.LevelNumber;

            // 渲染器与索引先备齐：场景要把两个索引写进 PixelartCameraRig，否则运行时相机挂错渲染器。
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，海图 " + level + " 未烘焙：" + error);
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(PixelartStageKit.DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder("Assets/Art/Materials");
            PixelartStageKit.EnsureFolder(PixelartStageKit.MaterialFolder);

            Debug.Log(LogTag + " 海图 " + level + "「" + map.DisplayName + "」(" + map.Id + ")：跨度 "
                + map.SpanX + "×" + map.SpanZ + "、地形件 " + map.Terrain.Count + "、远景件 " + map.Horizon.Count
                + "、道具 " + map.Props.Count + "、出生 " + map.Spawns.Count + " 人；构图中心 "
                + view.Target.ToString("0.##") + "、宽机位可见 " + view.WideVisibleMeters.ToString("0.#")
                + " m（" + view.SceneName + "）。");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质 ----------------
            // 海面替身：两档色带的平色（与样板关同一配方），不是活水面的配色。
            Material sea = PixelartStageKit.EnsureMaterial(SeaMaterialName,
                PixelartStageKit.Hex("2E5F84"), 2f, outlinePixels: 0f);

            // 三档站面：取色 = `WorldMapComposer.CreateTerrainMaterial` 写进 shader 的三个基色
            // （WorldMapComposer.cs:382-384 的 #C4A76A / #4A8C4A / #8C7B6A），各档的**顶面主色**。
            var materialMap = new Dictionary<string, Material>
            {
                [BandSandName] = PixelartStageKit.EnsureMaterial(BandSandMaterialName,
                    PixelartStageKit.Hex("C4A76A"), 3f),
                [BandGrassName] = PixelartStageKit.EnsureMaterial(BandGrassMaterialName,
                    PixelartStageKit.Hex("4A8C4A"), 3f),
                [BandRockName] = PixelartStageKit.EnsureMaterial(BandRockMaterialName,
                    PixelartStageKit.Hex("8C7B6A"), 3f),
            };

            Material crewRed = PixelartStageKit.CrewRed();
            Material crewBlue = PixelartStageKit.CrewBlue();
            Material crewHead = PixelartStageKit.CrewHead();

            var root = new GameObject("PixelartMap" + level);

            // ---------------- 海面（同高度替身，见类头）----------------
            // Plane 图元 10×10 × scale ⇒ 边长 10 × scale = 2 × span：宽机位可见宽 1.78 × span，
            // 画面里不会露出清屏色；海面按"铺满取景"给尺寸，与地图本身无关。
            GameObject seaMesh = PixelartStageKit.NewPrimitive(
                PrimitiveType.Plane, "Sea", root.transform, sea);
            seaMesh.transform.localPosition = new Vector3(
                map.SpanX * 0.5f, LevelGeometry.WaterSurfaceY, map.SpanZ * 0.5f);
            float seaScale = map.SpanX * 0.2f;
            seaMesh.transform.localScale = new Vector3(seaScale, 1f, seaScale);

            // ---------------- 地图内容（运行时同一个合成入口）----------------
            Transform composed = WorldMapComposer.Build(root.transform, map, assetSet);
            if (composed == null)
            {
                Debug.LogError(LogTag + " 海图 " + level + " 的 WorldMapComposer.Build 返回空——没有内容可烘。");
                return false;
            }

            GameObject content = composed.gameObject;

            // 远景环：排除（理由见类头）。先删再换材质，免得为永不入画的件派生一堆材质。
            int horizonFeatures = StripNodeGroup(composed, "Kit", HorizonFeaturePrefix);
            Debug.Log(LogTag + " 海图 " + level + "：远景环（HorizonFeatureRules）" + horizonFeatures
                + " 件已排除（环带 200–280 u，宽机位可见高度 " + view.WideVisibleMeters.ToString("0.#")
                + " m 下基本在画面外；理由见类头）。");

            // FloatingPropView：剥掉（理由见类头）。合成时挂上去的，只在有 OceanRig 时才会动。
            int floatingViews = StripComponents<FloatingPropView>(content);
            if (floatingViews > 0)
                Debug.Log(LogTag + " 海图 " + level + "：剥掉 " + floatingViews
                    + " 个 FloatingPropView（本场景是静态快照、海面是替身平面）。");

            // 材质换装：Kit_* 走派生档，站面三档走显式映射（见类头与 Band*Name 注释）。
            // 【先收再换】站面三档是 composer 运行时 new 出来的**非资产**材质，换装后 renderer 就不再引用它们；
            // 先把这批引用收下来，换完当场释放。
            var runtimeStandMaterials = new HashSet<Material>();
            foreach (MeshRenderer renderer in content.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material original = renderer.sharedMaterial;
                if (original != null && materialMap.ContainsKey(original.name))
                    runtimeStandMaterials.Add(original);
            }

            PixelartStageKit.SwapMaterials(content, materialMap, LogTag, deriveMissing: true);

            // 【为什么就地 DestroyImmediate，而不是调 WorldMapComposer.ReleaseBandMaterialCache】
            // 那边内部走 `Object.Destroy`，是运行时的释放语义；本路径在 edit mode 下装配，
            // 该批非资产材质会一直挂在 composer 的 static 缓存里连着八张图不释放。
            // 这里当场 DestroyImmediate；缓存里留下的悬空项在下一次 Build 开头的 Release 里
            // 会被它的 null 判断跳过（`if (pair.Value != null)`），不影响下一张图。
            foreach (Material standMaterial in runtimeStandMaterials)
                Object.DestroyImmediate(standMaterial);
            Debug.Log(LogTag + " 海图 " + level + "：释放运行时站面材质 "
                + runtimeStandMaterials.Count + " 个。");

            // ---------------- 船员（出生表 + 站面顶高）----------------
            // AllStandBoxes 返回新 List，只读用途，整个循环算一次复用（口径同 WorldMapRuntime.BuildBattlePlan）。
            List<WorldMapRules.WorldBox> standBoxes = WorldMapRules.AllStandBoxes(map);
            float centerX = map.SpanX * 0.5f;
            float centerZ = map.SpanZ * 0.5f;

            int placed = 0;
            for (int i = 0; i < map.Spawns.Count; i++)
            {
                WorldMapSpawn spawn = map.Spawns[i];
                float surfaceY = WorldMapRules.HeightAtWorld(standBoxes, new Vector2(spawn.X, spawn.Z));
                if (surfaceY <= LevelGeometry.WaterSurfaceY + 0.001f)
                {
                    Debug.LogError(LogTag + " 海图 " + level + " 出生点 " + i + " (" + spawn.X + ","
                        + spawn.Z + ") 落在没有站面的水面上——角色会站进海里，先修地图数据。");
                    continue;
                }

                var feet = new Vector3(spawn.X, surfaceY, spawn.Z);
                // 朝向场心（"对峙"读法；两件式造型本身回转对称，这一项只影响姿态语义）。
                Vector3 toCenter = new Vector3(centerX - spawn.X, 0f, centerZ - spawn.Z);
                float yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;

                string name = "Crew" + i + "_" + spawn.Archetype;
                Material body = spawn.TeamIndex == 0 ? crewRed : crewBlue;
                if (PixelartStageKit.PlaceCrew(root.transform, name, feet, yaw, body, crewHead, LogTag) != null)
                    placed++;
            }

            if (placed != map.Spawns.Count)
            {
                Debug.LogError(LogTag + " 海图 " + level + " 船员只摆上 " + placed + "/" + map.Spawns.Count
                    + " 个——出图上会缺人，先看上面逐条的报错。");
            }

            // ---------------- 光（数值理由见 PixelartStageKit.CreateSunAndAmbient）----------------
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

            // ---------------- 相机（正交，俯角 30° = 规则像素阶梯；口径见 PixelartLevelScene）----------------
            var camGo = new GameObject("PixelartMapCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = view.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartPilotScene.AzimuthDegrees);
            // 机位距离随跨度放大：60 m 的基准只罩得住 40×30 的样板关，海图的近角会落到相机背后被裁掉
            // （函数里的推导）。与出图脚本共用 PixelartLevelScene.CameraDistanceFor，不两边各写一份。
            camGo.transform.position = target + dir * PixelartLevelScene.CameraDistanceFor(view);
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            // 参考画布下的取景（运行时由 rig 按 worldPerPixel × 艺术像素数重算，分辨率越高范围越大）。
            camera.orthographicSize = view.WideVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.3f;
            // 远平面要罩住最远那张角的斜距 ≈ 机位距离 + 0.71 × span；1.2 × 可见高度（= span）给足余量。
            // 【真正光栅化几何的是 Cast 相机】它保持默认 far=1000，够用（最大 280 m 图的斜距 ~507 m），
            // 这里设的是上屏相机，为的是与"可见范围"口径一致、也是 rig 退出时恢复的那份值。
            camera.farClipPlane = Mathf.Max(300f,
                PixelartLevelScene.CameraDistanceFor(view) + view.WideVisibleMeters * 1.2f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PixelartStageKit.Hex("0A0F1C");
            camGo.AddComponent<AudioListener>();

            // Cast 相机：编辑器侧建好并写进场景（索引可序列化），运行时由 rig 同步视野/清屏等。
            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);   // local 恒等
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData();        // 确保存在（URP 才会认它的渲染器索引）
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = PixelartPilotScene.PixelScale;
            rig.worldPerPixel = PixelartLevelScene.WorldPerPixel(view.WideVisibleMeters);
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            AssertFraming(view, map, root.transform);
            AssertSpawnsSupported(view, map, standBoxes, root.transform);
            // 材质口径收口：物体 pass 按层拉全部不透明物体、不按 shader 过滤，混进一个旧 shader 的
            // renderer 就是"那片像素花屏、一行报错都没有"（细节见 PixelartStageKit 类头）。
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            string scenePath = "Assets/Scenes/" + view.SceneName + ".unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            PixelartStageKit.RegisterScene(scenePath, LogTag);

            Debug.Log(LogTag + " 海图 " + level + "「" + map.DisplayName + "」试点场景完成：" + scenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；放大倍数 " + PixelartPilotScene.PixelScale + "×"
                + "；俯角 " + PixelartPilotScene.PitchDegrees + "°"
                + "；机位距离 " + PixelartLevelScene.CameraDistanceFor(view).ToString("0.#") + " m）。");
            return true;
        }

        /// <summary>
        /// 删掉 <paramref name="parent"/> 下名为 <paramref name="group"/> 的组里、名字带
        /// <paramref name="namePrefix"/> 的子物体，返回删掉的件数。找不到组返回 0（不是错误：
        /// 内容结构变了要在这里看见"0 件"，而不是静默）。
        /// </summary>
        static int StripNodeGroup(Transform parent, string group, string namePrefix)
        {
            Transform groupRoot = parent.Find(group);
            if (groupRoot == null)
                return 0;

            int removed = 0;
            for (int i = groupRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = groupRoot.GetChild(i);
                if (!child.name.StartsWith(namePrefix, System.StringComparison.Ordinal))
                    continue;
                Object.DestroyImmediate(child.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>剥掉一整棵子树里的某类组件（返回件数）。用于"合成时挂上的运行时表现件"。</summary>
        static int StripComponents<T>(GameObject root) where T : Component
        {
            T[] found = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < found.Length; i++)
                Object.DestroyImmediate(found[i]);
            return found.Length;
        }

        /// <summary>
        /// 构图不变式：<see cref="PixelartLevelScene.View.Target"/> 的 XZ 必须等于地图定义推出的场心
        /// （span/2），且镜头**确实对着内容**（内容实测包围盒覆盖构图中心、竖直偏差在容差内）。
        ///
        /// 【为什么要写成断言】机位中心是"跨三处各写一份"的典型受害者（本仓踩过：场景写 35.264°、
        /// 出图脚本写 30°）。这里把"镜头对哪儿"钉在**地图定义与内容实测**上：改了 span、
        /// 或某件内容摆位漂了，装配当场报错，而不是出图后看着不对劲再回头查。
        /// 海图的 span 各不相同（150–280），取景表按 span 派生——这条断言是那个派生的唯一验收点。
        /// </summary>
        static void AssertFraming(PixelartLevelScene.View view, WorldMapDefinition map, Transform sceneRoot)
        {
            float centerX = map.SpanX * 0.5f;
            float centerZ = map.SpanZ * 0.5f;
            Vector3 target = view.Target;

            if (Mathf.Abs(target.x - centerX) > 0.01f || Mathf.Abs(target.z - centerZ) > 0.01f)
            {
                Debug.LogError(LogTag + " 海图 " + view.LevelNumber + " 构图中心与地图场心不一致："
                    + target.ToString("0.###") + " vs (" + centerX.ToString("0.###") + ", ·, "
                    + centerZ.ToString("0.###") + ")——改了 span 就要同步改取景表（wide/mid 也是按 span 派的）。");
            }

            // 取景表按"正方形地图"写 wide = span（两条边等长时"span"才无歧义）。
            // 真出现长方形，半宽/半高不同、对角线方向也变，这里点名而不是让它悄悄偏。
            if (Mathf.Abs(map.SpanX - map.SpanZ) > 0.01f)
            {
                Debug.LogWarning(LogTag + " 海图 " + view.LevelNumber + " 的跨度不是正方形（"
                    + map.SpanX + "×" + map.SpanZ + "）——取景表按 span 单值派生，"
                    + "长方形图要按对角线重新推 wide 并同步改表。");
            }

            // 内容包围盒：只统计**有 renderer 的子物体**（海面排除，它铺满画面、会把包围盒拉到无穷大）。
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (MeshRenderer renderer in sceneRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "Sea")
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                Debug.LogError(LogTag + " 海图 " + view.LevelNumber + " 场景里没有任何内容 renderer。");
                return;
            }

            Debug.Log(LogTag + " 海图 " + view.LevelNumber + " 内容包围盒 中心="
                + bounds.center.ToString("0.##") + " 尺寸=" + bounds.size.ToString("0.##")
                + "；构图中心 = " + target.ToString("0.##") + "。");

            if (!bounds.Contains(target))
            {
                Debug.LogError(LogTag + " 海图 " + view.LevelNumber + " 的构图中心在内容包围盒之外"
                    + "——镜头对着空地。核对取景表的 Target 与地图定义 span。");
                return;
            }

            float dy = Mathf.Abs(bounds.center.y - target.y);
            if (dy > VerticalFramingTolerance)
            {
                Debug.LogWarning(LogTag + " 海图 " + view.LevelNumber + " 构图中心的竖直位置与内容重心差 "
                    + dy.ToString("0.##") + "m（> " + VerticalFramingTolerance
                    + "m）——出图上内容会偏出画面，请核对取景表里的 Target.y。");
            }
            else
            {
                Debug.Log(LogTag + " 海图 " + view.LevelNumber
                    + " 构图不变式通过：场心 XZ 与内容重心竖直都对齐。");
            }
        }

        /// <summary>
        /// 站位不变式：每个出生点在**可见几何**上真的踩得到东西，且测得的支撑高度与用来摆放的
        /// 逻辑高度场差多少（逐条报数）。
        ///
        /// 【Δy 为什么是"查数"而不是"报警"】海图站面是 0.5 m 档的离散平台，逻辑高度场（box 表）与
        /// 可见几何（同一份 box 生成的 cube collider）本该一致；但 collider 顶面本身比视觉顶面高
        /// `0.03 × box 高`（见 <see cref="SupportTolerance"/>）。所以这里把两件事分开：
        /// **每条出生点都打出"逻辑高度 vs 射线测到的高度"**（数据/几何漂了当场看得见），
        /// 只有超过容差才升级成告警——那是真正要推回数据侧的偏差。
        /// 全 miss 则更像"编辑器批处理下物理查询不可用"，与"几何对不上"分开报。
        /// </summary>
        static void AssertSpawnsSupported(PixelartLevelScene.View view, WorldMapDefinition map,
            List<WorldMapRules.WorldBox> standBoxes, Transform sceneRoot)
        {
            int colliders = sceneRoot.GetComponentsInChildren<Collider>(true).Length;
            if (colliders == 0)
            {
                Debug.Log(LogTag + " 海图 " + view.LevelNumber + " 的内容不含任何碰撞体——"
                    + "站位只能用逻辑高度场核对，射线查支撑面这一项本图跳过。");
                return;
            }

            Physics.SyncTransforms();

            int supported = 0;
            int unsupported = 0;
            float worst = 0f;
            for (int i = 0; i < map.Spawns.Count; i++)
            {
                WorldMapSpawn spawn = map.Spawns[i];
                var xz = new Vector2(spawn.X, spawn.Z);
                float surfaceY = WorldMapRules.HeightAtWorld(standBoxes, xz);
                var origin = new Vector3(spawn.X, surfaceY + 0.5f, spawn.Z);

                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, 2f))
                {
                    supported++;
                    float delta = hit.point.y - surfaceY;
                    worst = Mathf.Max(worst, Mathf.Abs(delta));
                    if (Mathf.Abs(delta) > SupportTolerance)
                    {
                        Debug.LogWarning(LogTag + " 海图 " + view.LevelNumber + " 出生点 " + i + " ("
                            + spawn.X + "," + spawn.Z + ") 的场地表面在 y=" + hit.point.y.ToString("0.###")
                            + "，而逻辑高度场给的是 y=" + surfaceY.ToString("0.###") + "（差 "
                            + delta.ToString("0.###") + "m，容差 " + SupportTolerance + "）——"
                            + "这是**地图数据与几何的偏差**（站面 0.5 档的平台高度），不是渲染问题。");
                    }
                }
                else
                {
                    unsupported++;
                    Debug.LogWarning(LogTag + " 海图 " + view.LevelNumber + " 出生点 " + i + " ("
                        + spawn.X + "," + spawn.Z + ")（" + spawn.Archetype + "）在场地上打不到支撑面。");
                }
            }

            if (supported == 0)
            {
                Debug.LogError(LogTag + " 海图 " + view.LevelNumber + " 的所有向下射线都没打到东西——"
                    + "这更像「编辑器批处理下物理查询不可用」而不是内容缺陷（几何问题一般是零散几处）。");
            }
            else
            {
                Debug.Log(LogTag + " 海图 " + view.LevelNumber + " 站位不变式：" + supported + "/"
                    + map.Spawns.Count + " 个出生点打到支撑面，最大偏差 " + worst.ToString("0.###") + "m"
                    + "（容差 " + SupportTolerance + "m）"
                    + (unsupported > 0 ? "，" + unsupported + " 个没打到（见上面逐条告警）" : "") + "。");
            }
        }
    }
}
