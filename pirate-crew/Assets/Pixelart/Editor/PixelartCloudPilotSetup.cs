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
    /// **云彩关像素化试点场景**装配（`Assets/Scenes/PixelartCloud.unity`）。
    ///
    /// 【这个场景为什么存在】`PixelartPilot` 那套图元（地面 + 三级台阶 + 木箱）证明的是"机制对不对"；
    /// 创始人 2026-09-22 要求"把云朵那关应用这一套渲染管线"——本场景就是那句要求的落点：
    /// **内容全部来自云彩关的真实数据**，只有渲染路径换成像素化路径。
    ///
    /// 【内容逐条来自哪（一条都不自己编）】
    /// <list type="bullet">
    ///   <item>云场 <c>CloudField.prefab</c> + 落水危险虚线 <c>ShowcaseDangerBorder.prefab</c>：
    ///         摆位读关卡资产（<see cref="ShowcaseLevels.BakedPlacements"/>），
    ///         与主战斗场景 <c>RuntimeSceneArt</c> 实例化时读的是同一张表；</item>
    ///   <item>7 个船员的格坐标与阵营读关卡资产的出生表（`cloud_walk` 的 <c>units</c>），
    ///         站位高度用逻辑高度场的 <c>SurfaceWorldY</c>（与 <c>BattleTerrainView</c> 同一口径）；</item>
    ///   <item>镜头口径在 <see cref="PixelartCloudScene"/>（本文件不写任何机位数字）。</item>
    /// </list>
    ///
    /// 【与主战斗场景的关系：本场景**不碰** Battle.unity】把这条路径接进真正的 Battle 关卡是另一件事——
    /// 本路径的物体 pass **按层拉全部不透明物体**，也就是说场景里每一个 renderer 都必须挂本路径的物体
    /// shader（否则那张像素的 7 张 MRT 里有未定义内容）。Battle 场景带着海面 rig、FX、HUD、单位装配器，
    /// 逐个换成像素化材质是"接入"那一轮的活；本场景先把**关卡内容 + 本路径**的组合摆出来给人看，
    /// 并且用同一份关卡数据保证"看的就是云彩关，不是另编一个云场"。
    ///
    /// 【海面是替身，如实标注】关卡的活水面（OceanRig）是自带 shader 的运行时系统，不在本路径的
    /// 材质口径里；这里用一块**同高度（<c>LevelGeometry.WaterSurfaceY</c> = −0.4）的平色海面**占位——
    /// 它只影响观感的丰富度，不影响"云台/船员/构图"这三件要判的事。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙云彩关像素化试点场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartCloudPilotSetup.BuildAll</c>。
    /// </summary>
    public static class PixelartCloudPilotSetup
    {
        const string LogTag = "[PixelartCloudPilotSetup]";

        const string ScenePath = "Assets/Scenes/" + PixelartCloudScene.SceneName + ".unity";
        const string CloudFieldPrefabPath = "Assets/Art/Models/SceneKit/CloudField.prefab";
        const string DangerBorderPrefabPath = "Assets/Art/Models/SceneKit/ShowcaseDangerBorder.prefab";

        /// <summary>海面半边长（160×160 的平面；与试点场景同量级，够铺满任何机位）。</summary>
        const float SeaHalfExtent = 8f;   // Plane 图元 10×10 × 8 = 80×80 半边

        [MenuItem("PirateCrew/Pixelart/烘焙云彩关像素化试点场景")]
        public static void BuildAll()
        {
            // 渲染器与索引先备齐：场景要把两个索引写进 PixelartCameraRig，否则运行时相机挂错渲染器。
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(PixelartStageKit.DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder("Assets/Art/Materials");
            PixelartStageKit.EnsureFolder(PixelartStageKit.MaterialFolder);

            // ---------------- 关卡数据（内容与摆位的唯一来源）----------------
            if (!LevelAssetLibrary.TryGetLevel(PixelartCloudScene.LevelNumber, out LevelAssetPayload payload))
            {
                Debug.LogError(LogTag + " 读不到关卡资产 " + PixelartCloudScene.LevelNumber
                    + "（" + LevelAssetLibrary.Diagnostic + "）——云彩关内容无从摆起，场景未烘焙。");
                return;
            }

            TileTerrainGrid grid = LevelRasterFromAsset.Build(payload);
            List<ShowcasePiecePlacement> placements =
                ShowcaseLevels.BakedPlacements(PixelartCloudScene.LevelNumber);
            Debug.Log(LogTag + " 关卡 " + payload.levelNumber + "「" + payload.displayName + "」："
                + payload.widthTiles + "×" + payload.depthTiles + " 格、块高 " + grid.BlockWorldHeight
                + "、出生 " + payload.units.Count + " 人、烘焙件 " + placements.Count + " 件。");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质（第二个参数 = 色带档数，第三个 = 是否参与描边）----------------
            // 云台要描边（它们是"岛"，每个都要有自己的剪影环）；海面与危险线不描边
            // （铺满画面/细长条，加环只会把画面糊上墨色，与试点场景的地面同理）。
            Material sea = PixelartStageKit.EnsureMaterial("PixelartCloud_Sea",
                PixelartStageKit.Hex("2E5F84"), 2f, outlinePixels: 0f);
            // 云的白/金两色取自关卡云场自己的槽位材质（`LowpolyStageBuilder.ColorOf`：
            // CloudWarmWhite #FFF2DB / CloudPaleGold #FFD98F），保证"云还是那朵云的颜色"。
            Material cloudWarm = PixelartStageKit.EnsureMaterial("PixelartCloud_CloudWarmWhite",
                PixelartStageKit.Hex("FFF2DB"), 3f);
            Material cloudGold = PixelartStageKit.EnsureMaterial("PixelartCloud_CloudPaleGold",
                PixelartStageKit.Hex("FFD98F"), 3f);
            // 危险虚线在原链是**半透明**红（`Scene_Danger`：a=0.5、队列 3000）——本路径的 G-buffer
            // 没有混合，且不透明批次不会拉它，故改成不透明红（同一色相，去掉透明度）。
            Material danger = PixelartStageKit.EnsureMaterial("PixelartCloud_DangerLine",
                PixelartStageKit.Hex("CC2222"), 2f, outlinePixels: 0f);
            Material crewRed = PixelartStageKit.CrewRed();
            Material crewBlue = PixelartStageKit.CrewBlue();
            Material crewHead = PixelartStageKit.CrewHead();

            var root = new GameObject("PixelartCloud");

            // ---------------- 海面（同高度替身，见类头）----------------
            GameObject seaMesh = PixelartStageKit.NewPrimitive(
                PrimitiveType.Plane, "Sea", root.transform, sea);
            seaMesh.transform.localPosition = new Vector3(
                PixelartCloudScene.Target.x, LevelGeometry.WaterSurfaceY, PixelartCloudScene.Target.z);
            seaMesh.transform.localScale = new Vector3(SeaHalfExtent, 1f, SeaHalfExtent) * 2f;

            // ---------------- 关卡的烘焙件（云场 + 危险线）----------------
            // 材质换装按**原材质名**映射（预制体是共享资产，绝不能直接改它——那会把主战斗场景一起改掉）。
            var materialMap = new Dictionary<string, Material>
            {
                { "Lowpoly_CloudWarmWhite", cloudWarm },
                { "Lowpoly_CloudPaleGold", cloudGold },
                { "Scene_Danger", danger },
            };

            Vector3 cloudFieldCenter = Vector3.zero;
            bool cloudFieldFound = false;
            for (int i = 0; i < placements.Count; i++)
            {
                ShowcasePiecePlacement placement = placements[i];
                string prefabPath = PrefabPathFor(placement.Piece);
                if (prefabPath == null)
                {
                    // 第 2 关的碎岛不属本场景；本关若有别的件没登记，点名比静默漏件强。
                    Debug.LogWarning(LogTag + " 本场景未登记烘焙件 " + placement.Piece
                        + "（" + placement.InstanceName + "）——该件不摆。");
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError(LogTag + " 找不到烘焙件 prefab " + prefabPath
                        + "（跑一次 PirateCrew/烘焙/样板场景件 吗？）——"
                        + placement.InstanceName + " 未摆入。");
                    continue;
                }

                // 保留成预制体实例：几何重烘后本场景自动跟着更新（材质是实例级覆盖，见下）。
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = placement.InstanceName;
                instance.transform.SetParent(root.transform);
                instance.transform.position = placement.Position;
                instance.transform.rotation = Quaternion.Euler(0f, placement.YawDegrees, 0f);

                PixelartStageKit.SwapMaterials(instance, materialMap, LogTag);

                if (placement.Piece == ShowcasePieceId.CloudField)
                {
                    cloudFieldCenter = placement.Position;
                    cloudFieldFound = true;
                }
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
                        + "（" + unit.typeName + "）——云台高度场里没有可站的面，角色会悬在云海上空。");
                    continue;
                }

                Vector2 xz = LevelGeometry.TileCenterWorld(unit.gridX, unit.gridY);
                float feetY = grid.SurfaceWorldY(unit.gridX, unit.gridY);
                var feet = new Vector3(xz.x, feetY, xz.y);

                // 朝向竞技场心（"对峙"读法；两件式造型本身回转对称，这一项只影响姿态语义）。
                Vector3 toCenter = PixelartCloudScene.Target - feet;
                toCenter.y = 0f;
                float yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;

                string name = "Crew" + i + "_" + unit.typeName;
                Material body = unit.teamIndex == 0 ? crewRed : crewBlue;
                if (PixelartStageKit.PlaceCrew(root.transform, name, feet, yaw, body, crewHead, LogTag) != null)
                    placed++;
            }

            if (placed != payload.units.Count)
            {
                Debug.LogError(LogTag + " 船员只摆上 " + placed + "/" + payload.units.Count
                    + " 个——出图上会缺人，先看上面逐条的报错。");
            }

            // ---------------- 光（太阳/环境光的数值理由见 PixelartStageKit.CreateSunAndAmbient）----------------
            // 【本场景为什么不放附加光】试点场景那盏暖光是为了证明"附加光这条通路在跑"；
            // 云场是给人看观感的，再加一盏只会多一个"打光怪不怪"的变量。附加光的验证仍归试点场景。
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

            // ---------------- 相机（正交，俯角 30° = 规则像素阶梯；口径见 PixelartCloudScene）----------------
            var camGo = new GameObject("PixelartCloudCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = PixelartCloudScene.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartPilotScene.AzimuthDegrees);
            camGo.transform.position = target + dir * PixelartPilotScene.CameraDistance;
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            // 参考画布下的取景（运行时由 rig 按 worldPerPixel × 艺术像素数重算，分辨率越高范围越大）。
            camera.orthographicSize = PixelartCloudScene.WideVisibleMeters * 0.5f;
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
            rig.worldPerPixel = PixelartCloudScene.WorldPerPixel(PixelartCloudScene.WideVisibleMeters);
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            AssertFramingAgainstLevelGeometry(payload, cloudFieldFound, cloudFieldCenter);
            AssertCrewsStandOnClouds(payload, grid);
            // 材质口径收口：物体 pass 按层拉全部不透明物体、不按 shader 过滤，混进一个旧 shader 的
            // renderer 就是"那片像素花屏、一行报错都没有"（细节见 PixelartStageKit 类头）。
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            PixelartStageKit.RegisterScene(ScenePath, LogTag);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogTag + " 云彩关像素化试点烘焙完成：" + ScenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；放大倍数 " + PixelartPilotScene.PixelScale + "×"
                + "；俯角 " + PixelartPilotScene.PitchDegrees + "°"
                + "；构图中心 " + target.ToString("0.##") + "）。");
        }

        static string PrefabPathFor(ShowcasePieceId piece)
        {
            switch (piece)
            {
                case ShowcasePieceId.CloudField: return CloudFieldPrefabPath;
                case ShowcasePieceId.DangerBorder: return DangerBorderPrefabPath;
                default: return null;
            }
        }

        /// <summary>
        /// 构图不变式：<see cref="PixelartCloudScene.Target"/> 的三个数必须与
        /// ①关卡数据推出的竞技场心、②云场实例的实际摆位 都对得上。
        ///
        /// 【为什么要写成断言】机位中心是"跨三处各写一份"的典型受害者（本仓踩过：场景写 35.264°、
        /// 出图脚本写 30°，那张"对照图"根本不是同一机位）。这里把"镜头对哪儿"钉在数据上：
        /// 改了格世界尺寸、改了关卡尺寸、或云场摆位漂了，装配当场报错，而不是出图后看着不对劲再回头查。
        /// </summary>
        static void AssertFramingAgainstLevelGeometry(LevelAssetPayload payload,
            bool cloudFieldFound, Vector3 cloudFieldCenter)
        {
            float centerX = LevelGeometry.TileToWorld(payload.widthTiles * 0.5f);
            float centerZ = LevelGeometry.TileToWorld(payload.depthTiles * 0.5f);
            Vector3 target = PixelartCloudScene.Target;

            if (Mathf.Abs(target.x - centerX) > 0.01f || Mathf.Abs(target.z - centerZ) > 0.01f)
            {
                Debug.LogError(LogTag + " 构图中心与关卡场心不一致：PixelartCloudScene.Target = "
                    + target.ToString("0.###") + "，关卡 " + payload.widthTiles + "×" + payload.depthTiles
                    + " 格推出的场心 = (" + centerX.ToString("0.###") + ", ·, " + centerZ.ToString("0.###")
                    + ")——改了格世界尺寸或关卡尺寸就要同步改那个常量。");
            }

            if (!cloudFieldFound)
            {
                Debug.LogError(LogTag + " 关卡摆位表里没有云场件——镜头对的是空地，出图会是「云海」而不是云彩关。");
                return;
            }

            if (Mathf.Abs(cloudFieldCenter.x - target.x) > 0.01f
                || Mathf.Abs(cloudFieldCenter.z - target.z) > 0.01f)
            {
                Debug.LogError(LogTag + " 云场实例摆位 (" + cloudFieldCenter.ToString("0.###")
                    + ") 与构图中心 (" + target.ToString("0.###") + ") 的 XZ 不一致——"
                    + "镜头没对在云场中心上（云场 prefab 的原点就是构图中心，两处必须同值）。");
            }
            else
            {
                Debug.Log(LogTag + " 构图不变式通过：镜头中心 = 关卡场心 = 云场摆位 ("
                    + target.ToString("0.###") + ")。");
            }
        }

        /// <summary>
        /// 站位不变式：每个出生点在**可见几何**上真的踩得到东西。
        ///
        /// 【为什么用射线查而不是只查高度场】高度场是逻辑栅格，云场是另一份烘焙几何——两者由不同的
        /// 作者/种子产出（云场是手工骨架 + 逐顶点抖动）。"逻辑上实心、可见云台上没有那一块"这件事
        /// 出图后的表现是"角色悬空站在云外"，很容易被当成渲染问题查半天；这里直接对云场的碰撞盒
        /// 打一条向下的射线验一遍（云场 prefab 每朵云带双 BoxCollider，正是为落水/站位判定烘的）。
        ///
        /// 【全 miss 的单独报法】编辑器脚本里物理查询若不可用（批处理下的物理场景未刷新），
        /// 七条全 miss；那与"几何对不上"是两回事，故分开报——不让一次环境问题伪装成内容缺陷。
        /// </summary>
        static void AssertCrewsStandOnClouds(LevelAssetPayload payload, TileTerrainGrid grid)
        {
            Physics.SyncTransforms();

            int supported = 0;
            int unsupported = 0;
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
                    if (Mathf.Abs(delta) > 0.35f)
                    {
                        Debug.LogWarning(LogTag + " 出生点 (" + unit.gridX + "," + unit.gridY + ") 的"
                            + "云台面在 y=" + hit.point.y.ToString("0.###")
                            + "，而逻辑高度场给的是 y=" + feetY.ToString("0.###")
                            + "（差 " + delta.ToString("0.###") + "m）——角色会悬空或陷进云里，"
                            + "判据是「云场几何与高度场的偏差」，不是渲染问题。");
                    }
                }
                else
                {
                    unsupported++;
                    Debug.LogWarning(LogTag + " 出生点 (" + unit.gridX + "," + unit.gridY + ")"
                        + "（" + unit.typeName + "）在云场几何上打不到支撑面。");
                }
            }

            if (supported == 0)
            {
                Debug.LogError(LogTag + " 七条向下射线**全都没打到东西**——这更像"
                    + "「编辑器批处理下物理查询不可用」而不是七处内容缺陷（几何问题一般是零散几处）。"
                    + "站位是否成立以出图为准；真要判定请在本场景里手工核一遍。");
            }
            else
            {
                Debug.Log(LogTag + " 站位不变式：" + supported + "/" + payload.units.Count
                    + " 个出生点在云台上打到支撑面"
                    + (unsupported > 0 ? "，" + unsupported + " 个没打到（见上面逐条告警）" : "") + "。");
            }
        }
    }
}
