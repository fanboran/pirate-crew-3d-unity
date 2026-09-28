using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // GetUniversalAdditionalCameraData 是这里的扩展方法

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 第三手搓样板关「废弃化工厂」的**像素化路径试点场景**装配：
    /// 把 Blender 侧手作的总装件（`ChemPlant_Level.fbx`，见 `tools/blender/scene/chemplant/`）
    /// 摆进 `PixelartLevelScene` 的取景口径里，烘出 `Assets/Scenes/PixelartChemPlant.unity`，
    /// 供播放器出图链（`-pixelartOut -pixelartLevel 4`）产出**实机管线成图**。
    ///
    /// 【为什么需要这个装配器】`PixelartPilot` 是图元几何（验机制）、其余行是真实玩法内容；
    /// 本次要回答的是"这套观感用在这座化工厂上是什么样"，而本关**尚未接玩法数据**
    /// （没有高度场/编成/摆位表，见 `docs/设计/关卡/L04-废弃化工厂.md`）⇒ 内容不进 `ShowcaseLevels`，
    /// 只能像 `PixelartLevelPilotSetup` 那样直接摆一件总装件。取景仍走 `PixelartLevelScene`
    /// 的同一张表（本文件不写任何机位数字）。
    ///
    /// 【材质的唯一配方】逐个 `Kit_` 槽调 <see cref="PixelartStageKit.EnsureMaterial"/>——
    /// 与游戏内物体材质同一条配方（`PixelartObject` shader + 色带档数 + 描边），色值镜像
    /// `tools/blender/scene/style_tokens.py` 的 SLOTS（两处同源，改一处必改另一处；
    /// 另一份镜像在 `WorldMapAssetSetBuilder.Slots`）。
    ///
    /// 【不做的事】不碰 `WorldMapAssetSet` 资产表、不碰 `Battle.unity`（那是
    /// `WorldMapAssetSetBuilder.BuildAll` 的职责，属"接进玩法"那一步，本关还没到）。
    ///
    /// 用法（仓库根执行，一次只跑一个 Unity 进程）：
    ///   Unity.exe -batchmode -nographics -quit -projectPath pirate-crew \
    ///     -executeMethod PirateCrew.EditorTools.PixelartChemPlantSetup.BuildAll -logFile -
    /// </summary>
    public static class PixelartChemPlantSetup
    {
        public const string LogTag = "[PixelartChemPlant]";

        /// <summary>场景名（与 `PixelartLevelScene` 表的第 4 行、`BuildScenes` 的开发场景集同名）。</summary>
        public const string SceneName = "PixelartChemPlant";

        const string ModelPath = "Assets/Art/Models/WorldKit/ChemPlant/ChemPlant_Level.fbx";
        const int LevelNumber = 4;

        /// <summary>场地样板用的海面替身（口径抄 `PixelartLevelPilotSetup`：平面 10 m × 缩放 16）。</summary>
        const float SeaPlaneScale = 16f;
        const string SeaMaterialName = "PixelartLevel_Sea";

        /// <summary>
        /// 总装件的 16 个槽，**按名字典序**——Blender 侧 `join_to_object` 按槽名排序 append
        /// （`tools/blender/scene/props/props_kit.py` 同款），故 FBX 的材质槽序 == 字典序。
        /// 槽名不可用时按本序兜底赋值；可用时按名字匹配（两条路都在日志里报数）。
        /// </summary>
        static readonly string[] SlotNames =
        {
            "Kit_Brass", "Kit_ConcreteDark", "Kit_ConcreteLight", "Kit_ConcreteMid",
            "Kit_GlassDark", "Kit_GrassDark", "Kit_GrassMid", "Kit_Iron",
            "Kit_PaintYellow", "Kit_Rust", "Kit_RustDark", "Kit_SandDark",
            "Kit_SteelBlue", "Kit_SteelPale", "Kit_WetSand", "Kit_WoodDark",
        };

        /// <summary>槽色值：镜像 `style_tokens.SLOTS`（sRGB hex；本路径逐物体只吃 albedo）。</summary>
        static readonly Dictionary<string, string> SlotHex = new Dictionary<string, string>
        {
            { "Kit_Brass", "C9A227" },
            { "Kit_ConcreteDark", "6B665C" },
            { "Kit_ConcreteLight", "C6C1B4" },
            { "Kit_ConcreteMid", "9E988A" },
            { "Kit_GlassDark", "2E3A3E" },
            { "Kit_GrassDark", "2D5A2D" },
            { "Kit_GrassMid", "4A8C4A" },
            { "Kit_Iron", "6E6A63" },
            { "Kit_PaintYellow", "C9A63C" },
            { "Kit_Rust", "8C4A28" },
            { "Kit_RustDark", "5A2F1A" },
            { "Kit_SandDark", "8B7355" },
            { "Kit_SteelBlue", "4E6270" },
            { "Kit_SteelPale", "98A2A6" },
            { "Kit_WetSand", "5C4A34" },
            { "Kit_WoodDark", "6B4C28" },
        };

        /// <summary>场内摆 3 个船员当尺度参照（人的高度就是这关的尺度锚：厂房檐 7.2 m ≈ 4 人高）。</summary>
        static readonly Vector3[] CrewSpots =
        {
            new Vector3(4f, 0f, 6f),
            new Vector3(-9f, 0f, 2f),
            new Vector3(17f, 0f, -2f),
        };

        [MenuItem("PirateCrew/Pixelart/烘焙废弃化工厂试点场景")]
        public static void BuildAll()
        {
            if (!PixelartLevelScene.TryGet(LevelNumber, out PixelartLevelScene.View view))
            {
                Debug.LogError(LogTag + " 取景表里没有关卡 " + LevelNumber + " 的行，先补 "
                    + "PixelartLevelScene。");
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

            // ---------------- 模型：导入口径与 WorldKit 全套一致（WorldMapAssetSetBuilder.ApplyImportSettings）----------------
            if (!ApplyImportSettings())
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PixelartChemPlant");

            // ---------------- 材质（像素路径唯一配方）----------------
            var materials = new Material[SlotNames.Length];
            for (int i = 0; i < SlotNames.Length; i++)
            {
                string slot = SlotNames[i];
                materials[i] = PixelartStageKit.EnsureMaterial("PixelartChemPlant_" + slot,
                    PixelartStageKit.Hex(SlotHex[slot]), 3f);
                if (materials[i] == null)
                {
                    Debug.LogError(LogTag + " 槽 " + slot + " 的材质没造出来（shader 缺失？）——"
                        + "出图上那片会是品红/兜底色。");
                    return;
                }
            }

            // ---------------- 场地总装件 ----------------
            GameObject content = PlaceModel(root.transform);
            if (content == null)
                return;

            // ---------------- 海面替身（同高度；口径抄 PixelartLevelPilotSetup）----------------
            Material sea = PixelartStageKit.EnsureMaterial(SeaMaterialName,
                PixelartStageKit.Hex("2E5F84"), 2f, outlinePixels: 0f);
            if (sea != null)
            {
                GameObject seaMesh = PixelartStageKit.NewPrimitive(
                    PrimitiveType.Plane, "Sea", root.transform, sea);
                seaMesh.transform.localPosition = new Vector3(
                    view.Target.x, PirateCrew.Battle.LevelGeometry.WaterSurfaceY, view.Target.z);
                seaMesh.transform.localScale = new Vector3(SeaPlaneScale, 1f, SeaPlaneScale);
            }

            // ---------------- 尺度参照：3 个船员 ----------------
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

            // ---------------- 光 / 相机（俯角 30° = 规则像素阶梯，口径见 PixelartLevelScene）----------------
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

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

            LogFraming(view, content, root.transform);
            // 物体 pass 按层拉全部不透明物体、不按 shader 过滤：混进一个旧 shader 的 renderer
            // 就是"那片像素花屏、一行报错都没有"（口径见 PixelartStageKit 类头）。
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            string scenePath = "Assets/Scenes/" + SceneName + ".unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            PixelartStageKit.RegisterScene(scenePath, LogTag);

            Debug.Log(LogTag + " 场景完成：" + scenePath + "（Cast 渲染器 " + castIndex
                + " / Screen 渲染器 " + screenIndex + "；放大倍数 " + PixelartPilotScene.PixelScale
                + "×；俯角 " + PixelartPilotScene.PitchDegrees + "°；构图中心 "
                + view.Target.ToString("0.##") + "；宽机位可见 " + view.WideVisibleMeters
                + " m）。出图：播放器 -pixelartOut <目录> -pixelartLevel " + LevelNumber);
        }

        /// <summary>导入设置：与 WorldMapAssetSetBuilder.ApplyImportSettings 逐项同值（含那两条实测注释的坑）。</summary>
        static bool ApplyImportSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError(LogTag + " 找不到模型 " + ModelPath + "（先跑 "
                    + "tools/blender/scene/chemplant/chemplant_kit.py 出 FBX）。");
                return false;
            }

            // 【不是 None】槽名是换装的唯一依据：None 档下 renderer 的槽位读不到材质名（首次实测
            // "按名匹配 0 槽 / 按槽序兜底 16 槽"），按槽序兜底就会串色（实测把地坪套上褪色漆黄、
            // 罐体套上草绿）。ImportStandard 会把 FBX 的材质建成**子资产**（名字就是 Blender 侧的
            // Kit_ 槽名），据此按名匹配；场景里引用的是本路径自己的 .mat，这些子资产不被引用（惰性）。
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
            go.name = "ChemPlantLevel";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            int byName = 0;
            int byOrder = 0;
            int slots = 0;
            foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    slots++;
                    string slot = mats[i] != null ? mats[i].name : null;
                    if (slots < 3)
                        Debug.Log(LogTag + " 槽位样本#" + i + " = " + (slot ?? "<null>"));
                    int index = SlotIndex(slot);
                    if (index >= 0)
                    {
                        mats[i] = MaterialFor(index);
                        byName++;
                    }
                    else if (i < SlotNames.Length)
                    {
                        // materialImportMode=None 时槽位可能名字不可读——按 FBX 槽序（= 槽名字典序）兜底
                        mats[i] = MaterialFor(i);
                        byOrder++;
                    }
                    else
                    {
                        Debug.LogError(LogTag + " renderer " + renderer.name + " 有第 " + i
                            + " 个槽既无名字也超出槽表——材质会缺失。");
                    }
                }

                renderer.sharedMaterials = mats;
            }

            if (slots != SlotNames.Length)
            {
                Debug.LogError(LogTag + " 模型材质槽 " + slots + " 个，槽表 " + SlotNames.Length
                    + " 个——对不上说明 FBX 与槽表不同源，先核对 Blender 侧 STAT 行。");
            }

            Debug.Log(LogTag + " 换装完成：按名匹配 " + byName + " 槽 / 按槽序兜底 " + byOrder
                + " 槽（共 " + slots + " 槽）；模型包围盒 " + Bounds(go).ToString("0.##"));
            return go;
        }

        static readonly Dictionary<string, int> s_SlotIndex = BuildSlotIndex();

        static Dictionary<string, int> BuildSlotIndex()
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < SlotNames.Length; i++)
                map[SlotNames[i]] = i;
            return map;
        }

        static int SlotIndex(string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
                return -1;
            int index;
            return s_SlotIndex.TryGetValue(slotName, out index) ? index : -1;
        }

        static Material MaterialFor(int index)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(
                PixelartStageKit.MaterialFolder + "/PixelartChemPlant_" + SlotNames[index] + ".mat");
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// <summary>
        /// 构图核对：镜头确实对着内容（口径同 PixelartWorldMapPilotSetup.AssertFraming 的读法，
        /// 但这是美术件试点、内容是一件总装件 ⇒ 只打印读数 + 越界才报错，不硬断言）。
        /// </summary>
        static void LogFraming(PixelartLevelScene.View view, GameObject content, Transform root)
        {
            Bounds b = Bounds(content);
            Vector3 target = view.Target;
            bool insideXZ = b.Contains(new Vector3(target.x, b.center.y, target.z));
            Debug.Log(LogTag + " 构图核对：内容包围盒 center=" + b.center.ToString("0.##")
                + " size=" + b.size.ToString("0.##") + "；构图中心 " + target.ToString("0.##")
                + " 在内容 XZ 内 = " + insideXZ + "；宽机位可见高 " + view.WideVisibleMeters
                + " m ⇒ 画幅横跨 " + (view.WideVisibleMeters * 16f / 9f).ToString("0.#") + " m"
                + "（场地 68×48 m）；根下有 " + root.GetComponentsInChildren<Renderer>(true).Length
                + " 个 renderer。");
            if (!insideXZ)
                Debug.LogError(LogTag + " 构图中心不在内容上——出图会对着空处，先修取景表。");
        }
    }
}
