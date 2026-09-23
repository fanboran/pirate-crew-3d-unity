using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 场景资产样板（SceneKit Pilot）的一键组装：导入 Blender 侧产出的两个 FBX
    /// （<see cref="FlagshipFbxPath"/> / <see cref="DockFbxPath"/>）、按 Kit_ 槽名换装、
    /// 搭 showcase 场景（水面 + 沙岸 + 两件资产按口径落位 + 暖阳 + 双机位）并保存。
    ///
    /// 用法（协调者执行，本脚本自己不跑）：
    /// <code>
    /// Unity.exe -batchmode -nographics -projectPath …
    ///     -executeMethod PirateCrew.EditorTools.SceneKitPilotSetup.BuildAll [-quit]
    /// </code>
    ///
    /// 【导入参数教训（勿回退）】
    ///   · <c>importMaterials</c> 属性在 2022.3 已移除，用 <c>materialImportMode = None</c>；
    ///   · <c>useFileScale = false</c>：设 true 会把 Blender 的 cm 因子 ×0.01 乘进来（1.9 米变 0.019）；
    ///   · <c>bakeAxisConversion = true</c>：不设模型在 Unity 里会躺倒 -90°；
    ///   · 配 <c>useFileUnits = true</c> + <c>globalScale = 1</c> 才是 1 FBX 单位 = 1 米 1:1。
    ///
    /// 【坐标口径（与 tools/blender/scene/build_scene_kit.py 头注同源）】
    ///   船原点 = 船长中点水线 → 水面 y=-0.4 时根节点放 y=-0.4；
    ///   桥原点 = 桥面顶面中心（桥面在其上方 0.65）→ 根节点放 y = -0.4 + 0.65 = 0.25。
    ///   导入后船艏朝 Unity +Z（FBX -Z 前，bakeAxisConversion 折算）。
    ///
    /// 【已知边界】本机 batchmode 下对 <c>EditorBuildSettings.scenes</c> 的 API 改动不落盘，
    /// 故本方法**不写** Build Settings 登记——SceneKitPilot 场景已由协调者手工登记进
    /// ProjectSettings/EditorBuildSettings.asset（YAML 直改）。
    /// </summary>
    public static class SceneKitPilotSetup
    {
        // ---- 路径 ----
        public const string FlagshipFbxPath = "Assets/Art/Models/SceneKit/Flagship.fbx";
        public const string DockFbxPath = "Assets/Art/Models/SceneKit/Dock.fbx";
        public const string MaterialDir = "Assets/Art/Models/SceneKit/Materials";
        public const string ScenePath = "Assets/Scenes/SceneKitPilot.unity";

        /// <summary>showcase 根节点名（幂等重建的扫描键，不用 GameObject.Find）。</summary>
        public const string ShowcaseRootName = "[SceneKitPilotShowcase]";

        // ---- 世界口径 ----
        const float WaterY = -0.4f;         // 水面（docs/3D空间模型对齐.md，格 1→2 单位后）
        const float DockRootY = 0.25f;      // = WaterY + 0.65（桥面高）
        const float DockDeckTop = 0.65f;    // 桥面在水面上方的高度（Blender 侧 DOCK 桥面 0.65）
        const float UrpLitSmoothWood = 0.28f;   // docs/美术风格指南.md §3.1 木

        // ---- Kit_ 换装常量表（色值与 Blender 侧 build_scene_kit.py 同源，sRGB 直存）----
        // 本工程是 Gamma 色彩空间（SceneArtPalette.cs 头注），hex 归一化即与色板一致。
        static readonly string[] KitSlotNames =
        {
            "Kit_WoodMid", "Kit_WoodDark", "Kit_Sail", "Kit_Brass", "Kit_Rope",
        };
        static readonly string[] KitSlotHex =
        {
            "#A67B42", "#6B4C28", "#F5E8C8", "#C9A227", "#8A6F4D",
        };
        static readonly float[] KitSlotMetallic = { 0f, 0f, 0f, 1f, 0f };
        static readonly float[] KitSlotSmoothness = { 0.28f, 0.26f, 0.12f, 0.50f, 0.22f };

        /// <summary>-executeMethod 入口。失败走 LogError + Exit(非0)。</summary>
        public static void BuildAll()
        {
            if (!ImportFbx(FlagshipFbxPath) | !ImportFbx(DockFbxPath))   // 单 |：两个都要重导入
            {
                Debug.LogError("[SceneKitPilotSetup] FBX 导入失败，中止。");
                EditorApplication.Exit(1);
                return;
            }

            var flagship = AssetDatabase.LoadAssetAtPath<GameObject>(FlagshipFbxPath);
            var dock = AssetDatabase.LoadAssetAtPath<GameObject>(DockFbxPath);
            if (flagship == null || dock == null)
            {
                Debug.LogError("[SceneKitPilotSetup] FBX 根对象取不到（flagship=" + (flagship == null)
                    + " dock=" + (dock == null) + "）——检查导入日志。");
                EditorApplication.Exit(1);
                return;
            }

            BuildShowcase(flagship, dock);

            foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
                ApplyKitMaterials(renderer);

            int tris = CountSceneTriangles();
            Debug.Log("[SceneKitPilotSetup] showcase 完成：" + ScenePath
                + "，场景三角面 ≈ " + tris + "。场景已由协调者手工登记进 Build Settings。");

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------
        // FBX 导入
        // ------------------------------------------------------------------

        static bool ImportFbx(string path)
        {
            if (!File.Exists(Path.GetFullPath(path)))
            {
                Debug.LogError("[SceneKitPilotSetup] 找不到 FBX：" + path);
                return false;
            }
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[SceneKitPilotSetup] 不是模型资产：" + path);
                return false;
            }
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // 2022.3 无 importMaterials
            importer.useFileUnits = true;
            importer.useFileScale = false;          // true 会把 cm×0.01 乘进来（实测踩坑）
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;     // 不设模型躺倒 -90°（实测踩坑）
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------
        // showcase 场景
        // ------------------------------------------------------------------

        static void BuildShowcase(Object flagshipPrefab, Object dockPrefab)
        {
            // 幂等：已登记场景则打开重建，否则新建；先按根名清掉旧 showcase 根。
            SceneAsset existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existing != null)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var staleRoot in UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().GetRootGameObjects())
            {
                if (staleRoot.name.StartsWith(ShowcaseRootName))
                    Object.DestroyImmediate(staleRoot);
            }

            var root = new GameObject(ShowcaseRootName);

            // ---- 水面大平面（海蓝 #2B7AB8，风格指南 §3.1 水）----
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.SetParent(root.transform, false);
            water.transform.position = new Vector3(0f, WaterY, 0f);
            water.transform.localScale = new Vector3(8f, 1f, 8f);       // Plane 图元 10×10 → 80×80
            water.GetComponent<Renderer>().sharedMaterial
                = MakeSimpleMaterial("KitPilot_Water", "#2B7AB8", 0f, 0.92f);

            // ---- 沙岸（栈桥从岸探入水；岸顶 y=0 与竞技场地面同口径）----
            var shore = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shore.name = "Shore";
            shore.transform.SetParent(root.transform, false);
            shore.transform.position = new Vector3(0f, -0.7f, 6.5f);
            shore.transform.localScale = new Vector3(9f, 1.4f, 5f);
            shore.GetComponent<Renderer>().sharedMaterial
                = MakeSimpleMaterial("KitPilot_Sand", "#C4A76A", 0f, 0.18f);

            // ---- 两件资产按口径落位（船艏朝 +Z、面向岸）----
            var dockRoot = (GameObject)PrefabUtility.InstantiatePrefab(dockPrefab);
            dockRoot.name = "Dock";
            dockRoot.transform.SetParent(root.transform, false);
            dockRoot.transform.position = new Vector3(0f, DockRootY, 2f);

            var shipRoot = (GameObject)PrefabUtility.InstantiatePrefab(flagshipPrefab);
            shipRoot.name = "Flagship";
            shipRoot.transform.SetParent(root.transform, false);
            shipRoot.transform.position = new Vector3(0f, WaterY, -6.5f);

            // ---- 暖阳平行光（口径同 BattleSceneLighting.CreateDirectionalLight）----
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(48f, 140f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("#FFF4E0");
            light.intensity = 1.55f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.86f;

            // ---- 相机两台（全景 + 船侧中景）；全景挂 MainCamera 标签 ----
            AddCamera(root.transform, "PanoCamera",
                new Vector3(15f, 8.5f, 13.5f), new Vector3(0f, 0.4f, -2.5f), 55f, main: true);
            AddCamera(root.transform, "ShipSideCamera",
                new Vector3(-9.0f, 2.6f, -5.8f), new Vector3(0f, 2.3f, -6.5f), 50f, main: false);

            // ---- 环境（正午冷环境光托底，无需天空盒资产）----
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            Color ambient = Hex("#C8DDF0");
            RenderSettings.ambientLight = new Color(ambient.r * 0.85f, ambient.g * 0.85f,
                ambient.b * 0.85f, 1f);

            // 显式给绝对路径保存（SaveScene 不给路径会存到当前场景临时名）
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        }

        static void AddCamera(Transform parent, string name, Vector3 position,
            Vector3 lookTarget, float fov, bool main)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(lookTarget - position, Vector3.up);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            if (main)
                go.tag = "MainCamera";
        }

        // ------------------------------------------------------------------
        // Kit_ 换装
        // ------------------------------------------------------------------

        /// <summary>按槽名逐 renderer 换装；数组里绝不允许留 null（空材质=播放器粉色炸弹）。</summary>
        static void ApplyKitMaterials(Renderer renderer)
        {
            var mats = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                string slot = mats[i] != null ? mats[i].name : null;
                if (!string.IsNullOrEmpty(slot) && slot.StartsWith("Kit_"))
                {
                    int index = System.Array.IndexOf(KitSlotNames, slot);
                    mats[i] = index >= 0
                        ? GetOrCreateKitMaterial(index)
                        : MakeSimpleMaterial(slot, "#FF00FF", 0f, 0.5f);    // 未知 Kit_ 槽：品红暴露
                }
                else if (mats[i] == null)
                {
                    mats[i] = MakeSimpleMaterial("Kit_Fallback", "#FF00FF", 0f, 0.5f);
                }
                changed = true;
            }
            if (changed)
                renderer.sharedMaterials = mats;
        }

        static Material GetOrCreateKitMaterial(int index)
        {
            EnsureFolder(MaterialDir);
            string path = MaterialDir + "/" + KitSlotNames[index] + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var mat = NewUrpLitMaterial(KitSlotNames[index]);
            mat.color = Hex(KitSlotHex[index]);
            mat.SetFloat("_Metallic", KitSlotMetallic[index]);
            mat.SetFloat("_Smoothness", KitSlotSmoothness[index]);
            if (KitSlotNames[index] == "Kit_Sail")
                mat.SetFloat("_Cull", 0f);          // Render Face=Both：帆布双面可见
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material MakeSimpleMaterial(string name, string hex, float metallic, float smoothness)
        {
            var mat = NewUrpLitMaterial(name);
            mat.color = Hex(hex);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;     // 场景内嵌材质（水/沙/兜底），随场景保存
        }

        static Material NewUrpLitMaterial(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[SceneKitPilotSetup] 找不到 URP/Lit shader——工程未装 URP？");
                shader = Shader.Find("Standard");
            }
            return new Material(shader) { name = name };
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return;
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        static int CountSceneTriangles()
        {
            int total = 0;
            foreach (var filter in Object.FindObjectsOfType<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh != null)
                    total += mesh.triangles.Length / 3;
            }
            return total;
        }

        /// <summary>sRGB hex → Color（Gamma 色彩空间直存，口径同 SceneArtPalette/BattleSceneLighting）。</summary>
        static Color Hex(string hex)
        {
            var match = Regex.Match(hex, "^#([0-9A-Fa-f]{6})$");
            if (!match.Success)
                return Color.magenta;
            return new Color(
                System.Convert.ToInt32(match.Groups[1].Value.Substring(0, 2), 16) / 255f,
                System.Convert.ToInt32(match.Groups[1].Value.Substring(2, 2), 16) / 255f,
                System.Convert.ToInt32(match.Groups[1].Value.Substring(4, 2), 16) / 255f,
                1f);
        }
    }
}
