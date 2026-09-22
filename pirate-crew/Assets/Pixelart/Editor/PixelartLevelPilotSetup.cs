using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Data;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.SceneArt;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **三个关卡各自一个像素化试点场景**的装配（一次跑完；也是 README 宣传图的成图入口）。
    ///
    /// 【为什么一族三场景而不是一个】每个场景的**内容与构图都不一样**（云场在天上、碎岛贴海面、
    /// 空岛是一座 13 米高的岛），把它们塞进一个场景就得靠开关切换——那是"折叠态契约"最忌讳的状态
    /// （场景里出现按关卡参数变化的可见性）。改成"一关一场景"，每个场景都是一份**静态、可单独打开、
    /// 可单独出图**的内容，装配脚本是唯一的作者。
    ///
    /// 【内容逐条来自哪（一条都不自己编）】
    /// <list type="bullet">
    ///   <item>烘焙陈设（云场 / 碎岛壳 / 落水危险虚线）：摆位读关卡资产的摆位表
    ///         （<see cref="ShowcaseLevels.BakedPlacements"/>），与主战斗场景 <c>RuntimeSceneArt</c>
    ///         读的是同一张表；</item>
    ///   <item>第 3 关的空岛**没有烘焙件**（它由 `FloatingIslandShowcaseMenu.Place` 程序化合成进场景），
    ///         故这里调用同一个入口合成，摆位沿用 `PlaceIntoBattleCenter` 的 (20, 13.3, 15)；</item>
    ///   <item>船员：格坐标与阵营读关卡资产的出生表（`units`），站位高度用逻辑高度场的
    ///         <c>SurfaceWorldY</c>（与 <c>BattleTerrainView</c> 同一口径）；</item>
    ///   <item>镜头口径在 <see cref="PixelartLevelScene"/>（本文件不写任何机位数字）。</item>
    /// </list>
    ///
    /// 【海面是替身，如实标注】关卡的活水面（OceanRig）是自带 shader 的运行时系统，不在本路径的
    /// 材质口径里；三个场景都用一块**同高度（<c>LevelGeometry.WaterSurfaceY</c> = −0.4）的平色海面**占位。
    /// 它只影响观感的丰富度，不影响"场地 / 单位 / 构图"这三件要判的事。
    ///
    /// 【材质】第 1 关手写映射（云的两档色取自关卡槽位色）；第 2/3 关的岛体有十几个材质槽，
    /// 走**派生档**（`PixelartStageKit.EnsureDerivedMaterial`：从关卡自己的材质取色），
    /// 每个材质一行日志说清派生关系，收尾由 `AssertObjectShaderOnly` 兜底。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙三个关卡试点场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartLevelPilotSetup.BuildAll</c>
    /// （单关调试用 <c>BuildLevel1/2/3</c>）。
    /// </summary>
    public static class PixelartLevelPilotSetup
    {
        const string LogTag = "[PixelartLevelPilotSetup]";

        // ---- 内容件（与 SceneArtBaker 的产物路径一致）----
        const string CloudFieldPrefabPath = "Assets/Art/Models/SceneKit/CloudField.prefab";
        const string IsletsPrefabPath = "Assets/Art/Models/SceneKit/Islets_L02.prefab";
        const string DangerBorderPrefabPath = "Assets/Art/Models/SceneKit/ShowcaseDangerBorder.prefab";

        /// <summary>海面替身尺寸：Plane 图元 10×10 × 16 = 160×160（与试点场景同量级）。</summary>
        const float SeaPlaneScale = 16f;

        /// <summary>第 3 关空岛的根位置（= `FloatingIslandShowcaseMenu.PlaceIntoBattleCenter` 的取值）。</summary>
        static readonly Vector3 IslandRootPosition = new Vector3(20f, 13.3f, 15f);

        /// <summary>第 3 关空岛根名（`FloatingIslandScenePlan.RootName` 的镜像，仅用于日志核对）。</summary>
        const string IslandRootName = "FloatingIslandShowcase";

        const string SeaMaterialName = "PixelartLevel_Sea";
        const string DangerMaterialName = "PixelartLevel_DangerLine";

        [MenuItem("PirateCrew/Pixelart/烘焙三个关卡试点场景（云场/碎岛/空岛）")]
        public static void BuildAll()
        {
            PixelartLevelScene.View[] views = PixelartLevelScene.All;
            int ok = 0;
            for (int i = 0; i < views.Length; i++)
            {
                if (BuildOne(views[i]))
                    ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogTag + " 关卡试点场景烘焙完成：" + ok + "/" + views.Length + " 关。");
        }

        // ---- 单关入口（batchmode 调试用；菜单只留 BuildAll）----

        public static void BuildLevel1() => BuildSingle(1);
        public static void BuildLevel2() => BuildSingle(2);
        public static void BuildLevel3() => BuildSingle(3);

        static void BuildSingle(int levelNumber)
        {
            if (!PixelartLevelScene.TryGet(levelNumber, out PixelartLevelScene.View view))
            {
                Debug.LogError(LogTag + " 取景表里没有关卡 " + levelNumber + "。");
                return;
            }

            BuildOne(view);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>烘一个关卡：内容 → 材质 → 船员 → 光 → 相机/rig → 断言 → 存盘登记。</summary>
        static bool BuildOne(PixelartLevelScene.View view)
        {
            int level = view.LevelNumber;

            // 渲染器与索引先备齐：场景要把两个索引写进 PixelartCameraRig，否则运行时相机挂错渲染器。
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，关卡 " + level + " 未烘焙：" + error);
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(PixelartStageKit.DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder("Assets/Art/Materials");
            PixelartStageKit.EnsureFolder(PixelartStageKit.MaterialFolder);

            // ---------------- 关卡数据（内容与摆位的唯一来源）----------------
            if (!LevelAssetLibrary.TryGetLevel(level, out LevelAssetPayload payload))
            {
                Debug.LogError(LogTag + " 关卡 " + level + " 的资产读不到（" + LevelAssetLibrary.Diagnostic
                    + "）——内容无从摆起，本关未烘焙。");
                return false;
            }

            TileTerrainGrid grid = LevelRasterFromAsset.Build(payload);
            List<ShowcasePiecePlacement> placements = ShowcaseLevels.BakedPlacements(level);
            Debug.Log(LogTag + " 关卡 " + level + "「" + payload.displayName + "」："
                + payload.widthTiles + "×" + payload.depthTiles + " 格、块高 " + grid.BlockWorldHeight
                + "、出生 " + payload.units.Count + " 人、烘焙件 " + placements.Count + " 件；"
                + "构图中心 " + view.Target.ToString("0.##") + "（" + view.SceneName + "）。");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质 ----------------
            Material sea = PixelartStageKit.EnsureMaterial(SeaMaterialName,
                PixelartStageKit.Hex("2E5F84"), 2f, outlinePixels: 0f);
            // 危险虚线在原链是半透明红（`Scene_Danger`：a=0.5、队列 3000）——本路径的 G-buffer
            // 没有混合、不透明批次也不会拉它，故改成不透明红（同一色相，去掉透明度）。
            Material danger = PixelartStageKit.EnsureMaterial(DangerMaterialName,
                PixelartStageKit.Hex("CC2222"), 2f, outlinePixels: 0f);

            var materialMap = new Dictionary<string, Material>();
            bool deriveMissing = false;

            if (level == 1)
            {
                // 云场只有两档槽位色（`LowpolyStageBuilder.ColorOf`：暖白 #FFF2DB / 淡金 #FFD98F），
                // 手写映射保证"云还是那朵云的颜色"。
                materialMap["Lowpoly_CloudWarmWhite"] = PixelartStageKit.EnsureMaterial(
                    "PixelartLevel_CloudWarmWhite", PixelartStageKit.Hex("FFF2DB"), 3f);
                materialMap["Lowpoly_CloudPaleGold"] = PixelartStageKit.EnsureMaterial(
                    "PixelartLevel_CloudPaleGold", PixelartStageKit.Hex("FFD98F"), 3f);
            }
            else
            {
                // 第 2/3 关的岛体有十几个材质槽（岩三档/草三档/土/石/木/水/沫/云/晶/光/旗），
                // 逐个手挑色号既慢又会让"岛还是那座岛的颜色"失去保证 ⇒ 走派生档（见类头）。
                deriveMissing = true;
            }

            Material crewRed = PixelartStageKit.CrewRed();
            Material crewBlue = PixelartStageKit.CrewBlue();
            Material crewHead = PixelartStageKit.CrewHead();

            var root = new GameObject("PixelartLevel" + level);

            // 本关的**主内容件**（云场 / 碎岛壳 / 空岛），供构图断言核对"镜头确实对着内容"。
            GameObject content = null;

            // ---------------- 海面（同高度替身，见类头）----------------
            GameObject seaMesh = PixelartStageKit.NewPrimitive(
                PrimitiveType.Plane, "Sea", root.transform, sea);
            seaMesh.transform.localPosition = new Vector3(
                view.Target.x, LevelGeometry.WaterSurfaceY, view.Target.z);
            seaMesh.transform.localScale = new Vector3(SeaPlaneScale, 1f, SeaPlaneScale);

            // ---------------- 关卡内容 ----------------
            if (level == 3)
            {
                // 空岛没有烘焙件：调同一个合成入口（`Place` 幂等、资产就地覆写），
                // 摆位照抄 `PlaceIntoBattleCenter`。
                GameObject island = FloatingIslandShowcaseMenu.Place(null);
                if (island == null)
                {
                    Debug.LogError(LogTag + " 空岛合成失败（FloatingIslandShowcaseMenu.Place 返回空）——"
                        + "关卡 3 场景没有地面。");
                    return false;
                }

                island.name = IslandRootName;
                // 【必须挂进本场景的根】：不挂的话它是一条**独立根**——构图断言里的内容包围盒
                // 是按 `root` 子树统计的，独立根会被漏掉，于是"镜头对着内容吗"这条断言等于没查。
                island.transform.SetParent(root.transform, true);
                island.transform.position = IslandRootPosition;
                PixelartStageKit.SwapMaterials(island, materialMap, LogTag, deriveMissing: true);
                content = island;
            }

            for (int i = 0; i < placements.Count; i++)
            {
                ShowcasePiecePlacement placement = placements[i];
                string prefabPath = PrefabPathFor(placement.Piece);
                if (prefabPath == null)
                {
                    Debug.LogWarning(LogTag + " 关卡 " + level + " 未登记烘焙件 " + placement.Piece
                        + "（" + placement.InstanceName + "）——该件不摆。");
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError(LogTag + " 找不到烘焙件 prefab " + prefabPath
                        + "（跑一次 PirateCrew/烘焙/样板场景件 吗？）——" + placement.InstanceName + " 未摆入。");
                    continue;
                }

                // 保留成预制体实例：几何重烘后本场景自动跟着更新（材质是实例级覆盖）。
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = placement.InstanceName;
                instance.transform.SetParent(root.transform);
                instance.transform.position = placement.Position;
                instance.transform.rotation = Quaternion.Euler(0f, placement.YawDegrees, 0f);

                PixelartStageKit.SwapMaterials(instance, materialMap, LogTag, deriveMissing);

                if (placement.Piece != ShowcasePieceId.DangerBorder && content == null)
                    content = instance;
            }

            // ---------------- 船员（关卡出生表 + 逻辑高度场）----------------
            int placed = 0;
            for (int i = 0; i < payload.units.Count; i++)
            {
                LevelUnit unit = payload.units[i];
                int blocks = grid.BlocksAt(unit.gridX, unit.gridY);
                if (blocks <= 0)
                {
                    Debug.LogError(LogTag + " 出生点 (" + unit.gridX + "," + unit.gridY + ") 是空格"
                        + "（" + unit.typeName + "）——高度场里没有可站的面，角色会悬在空中。");
                    continue;
                }

                Vector2 xz = LevelGeometry.TileCenterWorld(unit.gridX, unit.gridY);
                float feetY = grid.SurfaceWorldY(unit.gridX, unit.gridY);
                var feet = new Vector3(xz.x, feetY, xz.y);

                // 朝向场地心（"对峙"读法；两件式造型本身回转对称，这一项只影响姿态语义）。
                Vector3 toCenter = view.Target - feet;
                toCenter.y = 0f;
                float yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;

                string name = "Crew" + i + "_" + unit.typeName;
                Material body = unit.teamIndex == 0 ? crewRed : crewBlue;
                if (PixelartStageKit.PlaceCrew(root.transform, name, feet, yaw, body, crewHead, LogTag) != null)
                    placed++;
            }

            if (placed != payload.units.Count)
            {
                Debug.LogError(LogTag + " 关卡 " + level + " 船员只摆上 " + placed + "/" + payload.units.Count
                    + " 个——出图上会缺人，先看上面逐条的报错。");
            }

            // ---------------- 光（数值理由见 PixelartStageKit.CreateSunAndAmbient）----------------
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

            // ---------------- 相机（正交，俯角 30° = 规则像素阶梯；口径见 PixelartLevelScene）----------------
            var camGo = new GameObject("PixelartLevelCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = view.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartPilotScene.AzimuthDegrees);
            camGo.transform.position = target + dir * PixelartPilotScene.CameraDistance;
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            // 参考画布下的取景（运行时由 rig 按 worldPerPixel × 艺术像素数重算，分辨率越高范围越大）。
            camera.orthographicSize = view.WideVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
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

            AssertFraming(view, payload, content, root.transform);
            AssertCrewsStandOnGround(view, payload, grid, root.transform);
            // 材质口径收口：物体 pass 按层拉全部不透明物体、不按 shader 过滤，混进一个旧 shader 的
            // renderer 就是"那片像素花屏、一行报错都没有"（细节见 PixelartStageKit 类头）。
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            string scenePath = "Assets/Scenes/" + view.SceneName + ".unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            PixelartStageKit.RegisterScene(scenePath, LogTag);

            Debug.Log(LogTag + " 关卡 " + level + "「" + payload.displayName + "」试点场景完成：" + scenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；放大倍数 " + PixelartPilotScene.PixelScale + "×"
                + "；俯角 " + PixelartPilotScene.PitchDegrees + "°）。");
            return true;
        }

        static string PrefabPathFor(ShowcasePieceId piece)
        {
            switch (piece)
            {
                case ShowcasePieceId.CloudField: return CloudFieldPrefabPath;
                case ShowcasePieceId.Islets: return IsletsPrefabPath;
                case ShowcasePieceId.DangerBorder: return DangerBorderPrefabPath;
                default: return null;
            }
        }

        /// <summary>
        /// 构图不变式：<see cref="PixelartLevelScene.View.Target"/> 的 XZ 必须等于关卡数据推出的场心，
        /// 且镜头**确实对着内容**（内容实测包围盒与构图中心的偏差在容差内）。
        ///
        /// 【为什么要写成断言】机位中心是"跨三处各写一份"的典型受害者（本仓踩过：场景写 35.264°、
        /// 出图脚本写 30°）。这里把"镜头对哪儿"钉在数据与内容实测上：改了格世界尺寸、改了关卡尺寸、
        /// 或某件内容摆位漂了，装配当场报错，而不是出图后看着不对劲再回头查。
        /// </summary>
        static void AssertFraming(PixelartLevelScene.View view, LevelAssetPayload payload,
            GameObject content, Transform sceneRoot)
        {
            float centerX = LevelGeometry.TileToWorld(payload.widthTiles * 0.5f);
            float centerZ = LevelGeometry.TileToWorld(payload.depthTiles * 0.5f);
            Vector3 target = view.Target;

            if (Mathf.Abs(target.x - centerX) > 0.01f || Mathf.Abs(target.z - centerZ) > 0.01f)
            {
                Debug.LogError(LogTag + " 关卡 " + view.LevelNumber + " 构图中心与关卡场心不一致："
                    + target.ToString("0.###") + " vs (" + centerX.ToString("0.###") + ", ·, "
                    + centerZ.ToString("0.###") + ")——改了格世界尺寸或关卡尺寸就要同步改取景表。");
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
                Debug.LogError(LogTag + " 关卡 " + view.LevelNumber + " 场景里没有任何内容 renderer。");
                return;
            }

            var boundsCenter = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
            float dy = Mathf.Abs(boundsCenter.y - target.y);
            Debug.Log(LogTag + " 关卡 " + view.LevelNumber + " 内容包围盒 中心="
                + boundsCenter.ToString("0.##") + " 尺寸=" + bounds.size.ToString("0.##")
                + "；构图中心 = " + target.ToString("0.##") + "（竖直偏差 " + dy.ToString("0.##") + "m）。");

            if (content == null)
            {
                Debug.LogWarning(LogTag + " 关卡 " + view.LevelNumber
                    + " 没有主内容件（只有危险线？）——镜头可能对着空地。");
            }

            if (dy > 6f)
            {
                Debug.LogWarning(LogTag + " 关卡 " + view.LevelNumber + " 构图中心的竖直位置与内容重心差 "
                    + dy.ToString("0.##") + "m（> 6m）——出图上内容会偏出画面，请核对取景表里的 Target.y。");
            }
            else
            {
                Debug.Log(LogTag + " 关卡 " + view.LevelNumber + " 构图不变式通过：场心 XZ 与内容重心竖直都对齐。");
            }
        }

        /// <summary>
        /// 站位不变式：每个出生点在**可见几何**上真的踩得到东西。
        ///
        /// 【为什么用射线查而不是只查高度场】高度场是逻辑栅格，场地是另一份几何（云场/岛壳/空岛各由
        /// 不同管线产出）。"逻辑上实心、可见几何上没有那一块"出图后的表现是"角色悬空站在场地外"，
        /// 很容易被当成渲染问题查半天；这里直接对碰撞体打一条向下的射线验一遍。
        ///
        /// 【Δy 报警不是装饰】高度场给的高度与几何面不一致（例如第 3 关的岛面与 28×0.5 的栅格差
        /// 零点几米）时，角色会悬空或陷进去——那是**关卡数据问题**，该报出来而不是悄悄贴到几何上。
        /// 全 miss 则更像"编辑器批处理下物理查询不可用"，与"几何对不上"分开报。
        /// </summary>
        static void AssertCrewsStandOnGround(PixelartLevelScene.View view, LevelAssetPayload payload,
            TileTerrainGrid grid, Transform sceneRoot)
        {
            // 【先看这一关的内容有没有碰撞体】云场件每朵云带双 BoxCollider（可查），
            // 碎岛的合并壳网格**没有任何碰撞体**（纯渲染壳，碰撞由逻辑高度场/隐形方块负责）。
            // 没有碰撞体时射线必然全 miss——那是"本件查不了"，不是"内容有问题"，
            // 混在一起报会把一次查询能力的限制伪装成内容缺陷（上一轮就这么误报过一关）。
            int colliders = sceneRoot.GetComponentsInChildren<Collider>(true).Length;
            if (colliders == 0)
            {
                Debug.Log(LogTag + " 关卡 " + view.LevelNumber + " 的场地件不含碰撞体（纯渲染壳）——"
                    + "站位只能用逻辑高度场核对，射线查支撑面这一项本关跳过。");
                return;
            }

            Physics.SyncTransforms();

            int supported = 0;
            int unsupported = 0;
            float worst = 0f;
            for (int i = 0; i < payload.units.Count; i++)
            {
                LevelUnit unit = payload.units[i];
                Vector2 xz = LevelGeometry.TileCenterWorld(unit.gridX, unit.gridY);
                float feetY = grid.SurfaceWorldY(unit.gridX, unit.gridY);
                var origin = new Vector3(xz.x, feetY + 0.5f, xz.y);

                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, 2f))
                {
                    supported++;
                    float delta = hit.point.y - feetY;
                    worst = Mathf.Max(worst, Mathf.Abs(delta));
                    if (Mathf.Abs(delta) > 0.35f)
                    {
                        Debug.LogWarning(LogTag + " 关卡 " + view.LevelNumber + " 出生点 ("
                            + unit.gridX + "," + unit.gridY + ") 的场地表面在 y="
                            + hit.point.y.ToString("0.###") + "，而逻辑高度场给的是 y="
                            + feetY.ToString("0.###") + "（差 " + delta.ToString("0.###") + "m）——"
                            + "角色会悬空或陷进去；这是**关卡数据与几何的偏差**，不是渲染问题。");
                    }
                }
                else
                {
                    unsupported++;
                    Debug.LogWarning(LogTag + " 关卡 " + view.LevelNumber + " 出生点 (" + unit.gridX
                        + "," + unit.gridY + ")（" + unit.typeName + "）在场地上打不到支撑面。");
                }
            }

            if (supported == 0)
            {
                Debug.LogError(LogTag + " 关卡 " + view.LevelNumber + " 的所有向下射线都没打到东西——"
                    + "这更像「编辑器批处理下物理查询不可用」而不是内容缺陷（几何问题一般是零散几处）。");
            }
            else
            {
                Debug.Log(LogTag + " 关卡 " + view.LevelNumber + " 站位不变式：" + supported + "/"
                    + payload.units.Count + " 个出生点打到支撑面，最大偏差 " + worst.ToString("0.###") + "m"
                    + (unsupported > 0 ? "，" + unsupported + " 个没打到（见上面逐条告警）" : "") + "。");
            }
        }
    }
}
