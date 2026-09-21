using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **像素化着色路径试点场景**装配（`Assets/Scenes/PixelartPilot.unity`）。
    ///
    /// 【这个场景是干什么的】给新路径（物体 pass 写 G-buffer → 低分辨率域着色 → 点采样上屏）一个
    /// **几何正确、内容最小**的载体，用来回答"v3 的观感在**本仓**长什么样"。
    /// 它刻意不复用既有试点场景的内容：那条链（`ToonPilot` 的岛壳/船员/描边装配）自身有未决缺陷，
    /// 拿它当基准会把"新路径的问题"和"旧装配的问题"混在一起。
    ///
    /// 【内容全是图元】地面 + 三级台阶（三面朝向不同 → 色带切分一眼可辨）+ 一根立柱 + 两名船员
    /// （胶囊 + 球头）。不引程序化网格生成器、不依赖任何既有装配脚本——几何正确性由 Unity 图元保证。
    ///
    /// 【材质】全部新建（`Assets/Art/Materials/Pixelart/`），只吃本路径的物体 shader；
    /// 逐物体的差异都在"色带档数"上：地面 2 档、岩石/立柱 3 档、船员 3 档。
    ///
    /// 【本机 batchmode 的两条教训照旧适用】File IO 必须绝对路径；Build Settings 走
    /// ProjectSettings 资产而不是 <c>EditorBuildSettings.scenes</c>（后者在 batchmode 下可能不落盘）。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙像素化试点场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartPilotSetup.BuildAll</c>。
    /// </summary>
    public static class PixelartPilotSetup
    {
        public const string SceneName = "PixelartPilot";
        const string ScenePath = "Assets/Scenes/" + SceneName + ".unity";
        const string MaterialFolder = "Assets/Art/Materials/Pixelart";
        const string DitherFolder = "Assets/Art/Textures/Fx/Dither";

        /// <summary>低分辨率 RT 高（v3 自身档：高 180 → 16:9 即 320×180）。</summary>
        const int RenderHeight = 180;

        [MenuItem("PirateCrew/Pixelart/烘焙像素化试点场景")]
        public static void BuildAll()
        {
            // 渲染器与索引先备齐：场景要把两个索引写进 PixelartCameraRig，否则运行时相机挂错渲染器。
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError("[PixelartPilotSetup] 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            // 抖动图案（P0-2 要用的 v3 1-bit 密度图案）先备齐，材质里挂好、_DitherMode 留在 0，
            // 想试 v3 口径就把材质上的 _DitherMode 拨到 1（不必重烘场景）。
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            EnsureFolder("Assets/Art/Materials");
            EnsureFolder(MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质 ----------------
            Material ground = EnsureMaterial("PixelartPilot_Ground", HexGamma("4A6E86"), 2f);
            Material rock = EnsureMaterial("PixelartPilot_Rock", HexGamma("D8BE8A"), 3f);
            Material pillar = EnsureMaterial("PixelartPilot_Pillar", HexGamma("C9A97A"), 3f);
            Material crewRed = EnsureMaterial("PixelartPilot_CrewRed", HexGamma("DE524D"), 3f);
            Material crewBlue = EnsureMaterial("PixelartPilot_CrewBlue", HexGamma("598CD9"), 3f);

            var root = new GameObject("PixelartPilot");

            // ---------------- 几何（全部图元，几何正确性由引擎保证） ----------------
            GameObject groundMesh = NewPrimitive(PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = new Vector3(0f, 0f, 0f);
            groundMesh.transform.localScale = new Vector3(6f, 1f, 6f);   // 60×60（Plane 图元 10×10）

            // 三级台阶：每级都是一个立方体，朝向相同但高度不同——色带会按面法线切出不同的档。
            AddBox(root.transform, "Step1", new Vector3(0f, 0.3f, 0f), new Vector3(6f, 0.6f, 6f), rock);
            AddBox(root.transform, "Step2", new Vector3(0f, 0.9f, 0f), new Vector3(4f, 0.6f, 4f), rock);
            AddBox(root.transform, "Step3", new Vector3(0f, 1.5f, 0f), new Vector3(2.4f, 0.6f, 2.4f), rock);

            // 立柱：竖直面 + 顶面，用来看"同一朝向在不同光角下是不是同一档"。
            AddBox(root.transform, "Pillar", new Vector3(-3.6f, 1.3f, -2.4f), new Vector3(1.2f, 2.6f, 1.2f), pillar);

            // 船员：胶囊 + 球头（省掉既有船员 prefab 的全部依赖）。
            AddCrew(root.transform, "CrewRed", new Vector3(2.6f, 0f, 2.4f), 150f, crewRed);
            AddCrew(root.transform, "CrewBlue", new Vector3(-2.2f, 0f, 3.2f), -35f, crewBlue);

            // ---------------- 光 ----------------
            var sunGo = new GameObject("PixelartSun");
            sunGo.transform.SetParent(root.transform);
            sunGo.transform.rotation = Quaternion.Euler(48f, 140f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = HexGamma("FFF5E0");
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;    // 色带表面接实时投影必脏（渲染篇 §4.5）
            RenderSettings.sun = sun;

            // 环境光 = 本路径的"暗部色"（暗面 = albedo × 环境色）。给一个偏冷的深色，
            // 让暗面明显暗但不死黑（v3 的暗面来自 SH，本路径按渲染篇 §4.6 简化为单色）。
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = HexGamma("14182A");

            // ---------------- 相机（正交真等距：俯角 35.264°、方位 45°） ----------------
            var camGo = new GameObject("PixelartPilotCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = new Vector3(0f, 1.0f, 0f);
            camGo.transform.position = target + new Vector3(17.32f, 17.32f, 17.32f);   // 视线沿立方对角线
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = HexGamma("0A0F1C");
            camGo.AddComponent<AudioListener>();

            // Cast 相机：编辑器侧建好并写进场景（索引可序列化），运行时由 rig 同步视野/清屏等。
            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);   // local 恒等
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData();        // 确保存在（URP 才会认它的渲染器索引）
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.renderHeight = RenderHeight;
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            RegisterInBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PixelartPilotSetup] 试点场景烘焙完成：" + ScenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；低分辨率 RT 高 " + RenderHeight + "）。");
        }

        static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = NewPrimitive(PrimitiveType.Cube, name, parent, material);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
        }

        /// <summary>船员：胶囊身体（图元高 2，scale 0.6 → 身高 1.2）+ 球头。脚底贴 y=0。</summary>
        static void AddCrew(Transform parent, string name, Vector3 groundPosition, float yawDegrees, Material material)
        {
            var crewRoot = new GameObject(name);
            crewRoot.transform.SetParent(parent);
            crewRoot.transform.localPosition = groundPosition;
            crewRoot.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            GameObject body = NewPrimitive(PrimitiveType.Capsule, name + "_Body", crewRoot.transform, material);
            body.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);

            GameObject head = NewPrimitive(PrimitiveType.Sphere, name + "_Head", crewRoot.transform, material);
            head.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            head.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
        }

        /// <summary>
        /// 新建/就地更新物体材质（幂等：重跑覆盖，常量表是唯一调色入口）。
        /// 只设"这个物体该长什么样"的逐物体参数；着色数学全在走 shader 全局的那一趟里。
        /// </summary>
        static Material EnsureMaterial(string name, Color albedo, float bandCount)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(PixelartPath.ObjectShaderName);
            if (shader == null)
            {
                Debug.LogError("[PixelartPilotSetup] 找不到 shader \"" + PixelartPath.ObjectShaderName
                    + "\"（编译失败？），材质未创建。");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetColor("_BaseColor", albedo);
            mat.SetFloat("_MainLightLevel", bandCount);
            mat.SetFloat("_DitherMode", 0f);       // 0 = Bayer 4×4 矩阵；1 = v3 的 1-bit 密度图案
            mat.SetFloat("_DitherStrength", 0f);   // 默认关（v3 自己的材质默认也是 0）；出图时用 MPB 拨开对照
            mat.SetFloat("_NormalEdgeLevel", 0f);  // P4 接连通域后才有效

            // 挂上 v3 口径的密度图案（_DitherMode 拨到 1 即生效，不必重烘场景）。
            var pattern = AssetDatabase.LoadAssetAtPath<Texture2D>(DitherFolder + "/ToonDither_0.png");
            if (pattern != null)
            {
                mat.SetTexture("_DitherPattern", pattern);
                mat.SetFloat("_DitherPatternSize", pattern.width);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 把场景写进 Build Settings（编辑器里 Play / 按名 LoadScene 用）。
        ///
        /// 【实现口径】先读出现有清单，就地拼成"去重后的目标清单"，再整表写回——
        /// 不用 <c>InsertArrayElementAtIndex(arraySize)</c> 追加：那个 API 在末尾插入时
        /// **新元素是末元素的副本**，随后若字段写入没落盘，就会留下一条重复场景
        /// （实测：本脚本第一版把 ToonPilot 写成了两条、新场景反而没进去）。
        /// 写回用 <c>AssetDatabase.SaveAssetIfDirty</c>（ProjectSettings 资产按 <c>SaveAssets</c> 不落盘），
        /// 最后**读文件回验**并把结果打进日志——注册这类"默默不生效"的活，不读回等于没做。
        /// 播放器出包不受本文件影响：`BuildScript` 是显式把场景集传给 <c>BuildPlayerOptions</c> 的。
        /// </summary>
        static void RegisterInBuildSettings(string scenePath)
        {
            AssetDatabase.ImportAsset(scenePath);   // 确保 .meta（guid）已生成

            Object settings = LoadProjectSettingsObject("ProjectSettings/EditorBuildSettings.asset");
            if (settings == null)
            {
                Debug.LogError("[PixelartPilotSetup] 读不到 EditorBuildSettings.asset，场景未注册。");
                return;
            }

            var so = new SerializedObject(settings);
            SerializedProperty scenes = so.FindProperty("m_Scenes");
            if (scenes == null)
            {
                Debug.LogError("[PixelartPilotSetup] EditorBuildSettings 没有 m_Scenes 字段，未注册场景。");
                return;
            }

            // 1) 收现状（按路径去重，保留原顺序；顺带清掉历史重复项）。
            var paths = new System.Collections.Generic.List<string>();
            var guids = new System.Collections.Generic.List<string>();
            for (int i = 0; i < scenes.arraySize; i++)
            {
                SerializedProperty element = scenes.GetArrayElementAtIndex(i);
                string path = element.FindPropertyRelative("path").stringValue;
                if (string.IsNullOrEmpty(path) || paths.Contains(path))
                    continue;

                paths.Add(path);
                guids.Add(element.FindPropertyRelative("guid").stringValue);
            }

            // 2) 目标清单 = 现状 + 本场景（已在则原样）。
            if (!paths.Contains(scenePath))
            {
                string guid = AssetDatabase.AssetPathToGUID(scenePath);
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError("[PixelartPilotSetup] 场景 guid 解析失败：" + scenePath);
                    return;
                }
                paths.Add(scenePath);
                guids.Add(guid);
            }

            // 3) 整表写回。
            scenes.ClearArray();
            for (int i = 0; i < paths.Count; i++)
            {
                scenes.InsertArrayElementAtIndex(i);
                SerializedProperty element = scenes.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("enabled").boolValue = true;
                element.FindPropertyRelative("path").stringValue = paths[i];
                element.FindPropertyRelative("guid").stringValue = guids[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings);

            // 4) 读回验证（不读回就不知道到底落盘没有）。
            string[] verify = System.IO.File.ReadAllLines(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..",
                    "ProjectSettings/EditorBuildSettings.asset")));
            int registered = 0;
            bool found = false;
            foreach (string line in verify)
            {
                if (!line.TrimStart().StartsWith("path:", System.StringComparison.Ordinal))
                    continue;
                registered++;
                if (line.Contains(scenePath))
                    found = true;
            }

            if (!found)
                Debug.LogError("[PixelartPilotSetup] 注册回验失败：Build Settings 里没有 " + scenePath
                    + "（现有 " + registered + " 条）。请手工核对 ProjectSettings/EditorBuildSettings.asset。");
            else
                Debug.Log("[PixelartPilotSetup] Build Settings 注册回验通过：" + scenePath
                    + "，共 " + registered + " 条场景。");

            AssetDatabase.SaveAssets();
        }

        /// <summary>ProjectSettings 下的资产要用 LoadAllAssetsAtPath 才拿得到（同 UrpSetup 口径）。</summary>
        static Object LoadProjectSettingsObject(string path)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all != null && all.Length > 0)
                return all[0];
            return AssetDatabase.LoadAssetAtPath<Object>(path);
        }

        /// <summary>sRGB hex（Gamma 空间直存）→ Color，口径同 SceneArtPalette / ToonPilotSetup。</summary>
        static Color HexGamma(string hex)
        {
            return new Color(
                int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                1f);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
