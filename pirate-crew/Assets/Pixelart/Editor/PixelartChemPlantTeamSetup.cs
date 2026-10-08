using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // GetUniversalAdditionalCameraData 是这里的扩展方法
using PixelartLevelView = PirateCrew.Rendering.Pixelart.PixelartLevelScene.View;
using PirateCrew.EditorTools.BuildSystem;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 「废弃化工厂 · 六件并行版」的**像素化路径试点场景**装配：把
    /// `Assets/Art/Models/SceneKit/ChemPlant.fbx`（六件并行分件式 kit，
    /// `tools/blender/scene/chemplant/` 的 `assemble_chemplant.py` 产出）摆进
    /// <see cref="PixelartLevelScene"/> 的取景口径里，烘出 `Assets/Scenes/Pixelart/PixelartChemPlantTeam.unity`，
    /// 供播放器出图链（`-pixelartOut -pixelartLevel 5`）产**实机管线成图**。
    ///
    /// 【与 `PixelartChemPlantSetup`（单文件版）的关系】两套是同一主题的两条并行路线，共用：
    /// 像素路径装配机制（<see cref="PixelartStageKit"/>）、取景表（<see cref="PixelartLevelScene"/>）、
    /// 出图链（`PlayerArtCapture` 的 `-pixelartOut -pixelartLevel N`）。差别只有**模型来源与场景名**：
    /// 本文件吃 `SceneKit/ChemPlant.fbx`（6 节点 / 18 槽 / 223,240 三角面 / 56×40 m 场地），
    /// 场景名 `PixelartChemPlantTeam`（关卡号 5 = 取景表里本文件新增的那一行）。
    ///
    /// 【材质的唯一配方】逐个 `Kit_` 槽调 <see cref="PixelartStageKit.EnsureMaterial"/>——与游戏内
    /// 物体材质同一条配方（`PixelartObject` shader + 3 档色带 + 描边），色值镜像
    /// `tools/blender/scene/style_tokens.py` 的 SLOTS（改一处必改另一处）。
    ///
    /// 【不做的事】不碰 `WorldMapAssetSet`、不碰 `Battle.unity`、不改 `PixelartChemPlant`（另一条的场景）。
    ///
    /// 用法（仓库根执行，一次只跑一个 Unity 进程）：
    ///   Unity.exe -batchmode -nographics -quit -projectPath pirate-crew \
    ///     -executeMethod PirateCrew.EditorTools.PixelartChemPlantTeamSetup.BuildAll -logFile -
    /// </summary>
    public static class PixelartChemPlantTeamSetup
    {
        public const string LogTag = "[PixelartChemPlantTeam]";

        /// <summary>场景名（取景表与开发场景集里的名字）。</summary>
        public const string SceneName = "PixelartChemPlantTeam";

        const string ModelPath = "Assets/Art/Models/SceneKit/ChemPlant.fbx";

        /// <summary>取景表里的关卡号（本文件往 `PixelartLevelScene` 新增的那一行）。</summary>
        const int LevelNumber = 5;

        /// <summary>场外地面替身的缩放（Plane 是 10 m 见方 ⇒ 20 = 200 m 见方）。</summary>
        const float GroundPlaneScale = 20f;

        /// <summary>
        /// 六件版的 18 个槽（**按名字典序** —— `Mesher.to_object` 按首次出现顺序 append，
        /// 但跨件并集与 FBX 子节点的槽序不保证一致，故一律按名匹配，名字读不到才按序兜底）。
        /// </summary>
        static readonly string[] SlotNames =
        {
            "Kit_ConcreteDark", "Kit_ConcreteLight", "Kit_ConcreteMid", "Kit_GlassDark",
            "Kit_GrassDark", "Kit_GrassMid", "Kit_Iron", "Kit_PaintYellow", "Kit_RockDark",
            "Kit_RockMid", "Kit_Rust", "Kit_RustDark", "Kit_SandDark", "Kit_SteelBlue",
            "Kit_SteelPale", "Kit_WetSand", "Kit_WoodDark", "Kit_WoodMid",
        };

        /// <summary>槽色值：镜像 `style_tokens.SLOTS`（sRGB hex；本路径逐物体只吃 albedo）。</summary>
        static readonly Dictionary<string, string> SlotHex = new Dictionary<string, string>
        {
            { "Kit_ConcreteDark", "6B665C" },
            { "Kit_ConcreteLight", "C6C1B4" },
            { "Kit_ConcreteMid", "9E988A" },
            { "Kit_GlassDark", "2E3A3E" },
            { "Kit_GrassDark", "2D5A2D" },
            { "Kit_GrassMid", "4A8C4A" },
            { "Kit_Iron", "6E6A63" },
            { "Kit_PaintYellow", "C9A63C" },
            { "Kit_RockDark", "5C4F42" },
            { "Kit_RockMid", "8C7B6A" },
            { "Kit_Rust", "8C4A28" },
            { "Kit_RustDark", "5A2F1A" },
            { "Kit_SandDark", "8B7355" },
            { "Kit_SteelBlue", "4E6270" },
            { "Kit_SteelPale", "98A2A6" },
            { "Kit_WetSand", "5C4A34" },
            { "Kit_WoodDark", "6B4C28" },
            { "Kit_WoodMid", "A67B42" },
        };

        /// <summary>
        /// 场内摆 3 个船员当**尺度参照**（人的高度就是这关的尺度锚）。
        /// 坐标 = Unity 系（= Blender 的 X / Z / -Y）：全落在场地地坪（y=0）与主路上，
        /// 不与设备基础打架——Blender 侧主路在 Y∈[-8,-4] ⇒ Unity Z∈[4,8]。
        /// </summary>
        static readonly Vector3[] CrewSpots =
        {
            new Vector3(0f, 0f, 6f),      // 主路正中（Blender (0,-6)）
            new Vector3(-14f, 0f, 6f),    // 主路西段（管廊下）
            new Vector3(9f, 0f, -5f),     // 旁楼前院（Blender (9,5) 一侧）
        };

        [MenuItem("PirateCrew/Pixelart/烘焙废弃化工厂试点场景（六件并行版）")]
        public static void BuildAllMenu() => BuildAll();

        /// <summary>无头入口：导模型 → 换材质 → 搭像素 rig → 存场景 → 登记进开发场景集。</summary>
        public static void BuildAll()
        {
            if (!PixelartLevelScene.TryGet(LevelNumber, out PixelartLevelView view))
            {
                Debug.LogError(LogTag + " 取景表里没有关卡 " + LevelNumber + " 的行，先补 PixelartLevelScene。");
                return;
            }

            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(PixelartStageKit.DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder(PixelartStageKit.MaterialFolder);

            if (!ApplyImportSettings())
                return;

            // 材质重映射（2026-10-08「关5 偏暗、红太深」修复）：同关4 的修法——把 18 个 Kit_ 槽
            // 外链到本路径着色材质。FBX 内嵌材质携带的是**线性值**（Kit_Rust=(0.262,0.068,0.021)），
            // 而像素材质的 _BaseColor 口径是 **sRGB 原值**（PixelartChemPlantTeam_Kit_Rust.mat =
            // (0.549,0.290,0.157)）——战斗路径（RuntimeSceneArt 直接实例化、不换装）拿到线性值
            // 就整关掉一档 gamma：实测亮度 V 0.49→0.28、锈红 (144,77,42)→(83,24,2)。
            BindImporterMaterialRemap();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PixelartChemPlantTeam");

            var materials = new Material[SlotNames.Length];
            for (int i = 0; i < SlotNames.Length; i++)
            {
                string slot = SlotNames[i];
                materials[i] = PixelartStageKit.EnsureMaterial("PixelartChemPlantTeam_" + slot,
                    PixelartStageKit.Hex(SlotHex[slot]), 3f);
                if (materials[i] == null)
                {
                    Debug.LogError(LogTag + " 槽 " + slot + " 的材质没造出来（shader 缺失？）——出图上会是品红。");
                    return;
                }
            }

            GameObject content = PlaceModel(root.transform);
            if (content == null)
                return;

            // 场外地面（**替身**，只为出图不显"悬空沙盘"）：本关是内陆厂区，没有海面可借，
            // 用一大块土色平面兜住场地之外——口径同「海面替身」（大平面 + 关描边），
            // 真上玩法时这块会被关卡地形取代。
            Material ground = PixelartStageKit.EnsureMaterial("PixelartChemPlantTeam_GroundFar",
                PixelartStageKit.Hex("6B665C"), 3f, outlinePixels: 0f);
            if (ground != null)
            {
                GameObject groundMesh = PixelartStageKit.NewPrimitive(
                    PrimitiveType.Plane, "GroundFar", root.transform, ground);
                groundMesh.transform.localPosition = new Vector3(view.Target.x, -0.16f, view.Target.z);
                groundMesh.transform.localScale = new Vector3(GroundPlaneScale, 1f, GroundPlaneScale);
            }

            // 尺度参照：3 个船员（红/蓝交替）
            Material crewRed = PixelartStageKit.CrewRed();
            Material crewBlue = PixelartStageKit.CrewBlue();
            Material crewHead = PixelartStageKit.CrewHead();
            for (int i = 0; i < CrewSpots.Length; i++)
            {
                Material body = i % 2 == 0 ? crewBlue : crewRed;
                Vector3 to = view.Target - CrewSpots[i];
                to.y = 0f;
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                PixelartStageKit.PlaceCrew(root.transform, "Crew" + i, CrewSpots[i], yaw,
                    body, crewHead, LogTag);
            }

            // 光 / 相机（俯角 30° = 规则像素阶梯，口径见 PixelartLevelScene）
            // 【castShadows = true】化工厂这种大件场地**必须开实时投影**，否则整场没有光影
            // （并行线 r16 定的口径与根因：投影默认关是"完全没有光影"的来源；见 d5e7aec3）。
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform, castShadows: true);

            var camGo = new GameObject("PixelartLevelCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = view.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartPilotScene.AzimuthDegrees);
            camGo.transform.position = target + dir * PixelartLevelScene.CameraDistanceFor(view);
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = view.WideVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PixelartStageKit.Hex("0A0F1C");
            camGo.AddComponent<AudioListener>();

            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData();
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = PixelartPilotScene.PixelScale;
            rig.worldPerPixel = PixelartLevelScene.WorldPerPixel(view.WideVisibleMeters);
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            string scenePath = BuildScenes.PathOf(SceneName);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            PixelartStageKit.RegisterScene(scenePath, LogTag);

            Bounds b = ContentBounds(content);
            Debug.Log(LogTag + " 场景完成：" + scenePath + "（Cast 渲染器 " + castIndex
                + " / Screen 渲染器 " + screenIndex + "；放大 " + PixelartPilotScene.PixelScale
                + "×；俯角 " + PixelartPilotScene.PitchDegrees + "°；构图中心 "
                + view.Target.ToString("0.##") + "；宽机位可见 " + view.WideVisibleMeters
                + " m；内容包围盒 " + b.size.ToString("0.0")
                + " 中心 " + b.center.ToString("0.0") + "）。出图："
                + "播放器 -pixelartOut <目录> -pixelartLevel " + LevelNumber);
        }

        /// <summary>导入设置：与 `PixelartChemPlantSetup` / `WorldMapAssetSetBuilder.ApplyImportSettings`
        /// 逐项同值（含两条实测坑：ImportStandard 才读得到槽名、useFileScale=true 吃掉根上的 100×）。</summary>
        /// <summary>
        /// 【2026-10-08「关5 偏暗、红太深」修复】把 FBX 导入器的材质重映射表（meta 的
        /// externalObjects）的 18 个 <c>Kit_</c> 槽外链到本路径的着色像素材质
        /// （<c>PixelartChemPlantTeam_Kit_*.mat</c>）。
        ///
        /// 【为什么】FBX 内嵌材质携带**线性值**（Kit_Rust=(0.262,0.068,0.021)），而像素材质
        /// _BaseColor 的口径是 **sRGB 原值**（Team_Kit_Rust=(0.549,0.290,0.157)）——战斗路径
        /// （RuntimeSceneArt 直接实例化模型预制、不换装）随之整关偏暗一档 gamma
        /// （实测 V 0.49→0.28、锈红 (144,77,42)→(83,24,2)），试点路径靠按槽名换装从未受影响。
        /// 与关4 的 PixelartChemPlantSetup.BindImporterMaterialRemap 同机制、同修法。
        /// </summary>
        [MenuItem("PirateCrew/Pixelart/关5 FBX 材质重映射绑定")]
        public static void BindImporterMaterialRemap()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError(LogTag + " 找不到模型 " + ModelPath + "，重映射未执行。");
                return;
            }

            int bound = 0;
            for (int i = 0; i < SlotNames.Length; i++)
            {
                string matPath = PixelartStageKit.MaterialFolder + "/PixelartChemPlantTeam_"
                    + SlotNames[i] + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    Debug.LogError(LogTag + " 着色材质缺件：" + matPath);
                    continue;
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), SlotNames[i]), mat);
                bound++;
            }

            importer.SaveAndReimport();
            Debug.Log(LogTag + " 材质重映射完成：" + bound + "/" + SlotNames.Length
                + " 槽 → PixelartChemPlantTeam_Kit_*.mat（战斗路径实例化即着色）。");
        }

        static bool ApplyImportSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError(LogTag + " 找不到模型 " + ModelPath
                    + "（先跑 tools/blender/scene/chemplant/assemble_chemplant.py 出 FBX）。");
                return false;
            }

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.useFileUnits = true;
            importer.useFileScale = true;      // FBX_SCALE_NONE 会把 (100,100,100) 挂在根上，这里吃掉
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;   // 不设模型躺倒 -90°
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            return true;
        }

        static GameObject PlaceModel(Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null)
            {
                Debug.LogError(LogTag + " 模型读不到：" + ModelPath);
                return null;
            }

            GameObject go = PrefabUtility.InstantiatePrefab(asset) as GameObject;
            if (go == null)
                go = Object.Instantiate(asset);
            go.name = "ChemPlantTeam";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            int byName = 0, byOrder = 0, slots = 0;
            foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    slots++;
                    string slot = mats[i] != null ? mats[i].name : null;
                    int index = SlotIndex(slot);
                    if (index >= 0)
                    {
                        mats[i] = MaterialFor(index);
                        byName++;
                    }
                    else if (i < SlotNames.Length)
                    {
                        mats[i] = MaterialFor(i);          // 名字读不到时按槽序兜底
                        byOrder++;
                    }
                    else
                    {
                        Debug.LogError(LogTag + " renderer " + renderer.name + " 第 " + i
                            + " 个槽既无名字也超出槽表——材质会缺失。");
                    }
                }

                renderer.sharedMaterials = mats;
            }

            Debug.Log(LogTag + " 模型就位：" + go.name + "（renderer 槽位 " + slots
                + " 个；按名匹配 " + byName + " / 按序兜底 " + byOrder
                + "；子节点 " + go.transform.childCount + " 个）");
            return go;
        }

        static int SlotIndex(string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
                return -1;
            for (int i = 0; i < SlotNames.Length; i++)
            {
                // FBX 子资产名可能带 " (Instance)" 之类后缀，用 Contains 兜住
                if (SlotNames[i] == slotName || slotName.Contains(SlotNames[i]))
                    return i;
            }
            return -1;
        }

        static Material MaterialFor(int index)
            => AssetDatabase.LoadAssetAtPath<Material>(
                PixelartStageKit.MaterialFolder + "/PixelartChemPlantTeam_" + SlotNames[index] + ".mat");

        static Bounds ContentBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
