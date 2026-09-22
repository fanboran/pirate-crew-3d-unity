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

        /// <summary>低分辨率 RT 高。360 = 1920 宽屏上 1 像素 = 3 屏幕像素（原 180 = 6 屏幕像素，颗粒过粗）。</summary>
        const int RenderHeight = 360;

        /// <summary>
        /// 俯角：**30°（经典像素等距 2:1）**，不是真等距 35.264°。
        ///
        /// 【为什么】地面轴的屏幕斜率 = sinθ：30° 恰为 **0.5 = 2 像素横移 / 1 像素下降**，
        /// 像素阶梯因此是**规则的**（每 2 格一段）；35.264° 是 0.5773，与像素网格无整数比，
        /// 栅格化出来的阶梯长度必然忽长忽短（手绘像素等距沿用至今的都是 2:1 这一档）。
        /// 这一条是渲染篇 §2.1 表里"30° = 经典像素等距"那一行的实拍依据。
        /// </summary>
        const float CameraPitchDegrees = 30f;

        /// <summary>方位角：固定 45°（对角线，只有它给出对称菱形）。</summary>
        const float CameraAzimuthDegrees = 45f;

        const float CameraDistance = 30f;
        const float CameraOrthoSize = 7f;

        /// <summary>角色身高（Unity 胶囊图元高 2 × scale；scale 1 = 2.0m）。</summary>
        const float CrewHeight = 2.0f;

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

            // ---------------- 材质（最后一个参数 = 描边线宽，低分辨率像素）----------------
            // 地面不描边（铺满画面的大平面的壳环会顶到边缘，且无观感意义）。
            Material ground = EnsureMaterial("PixelartPilot_Ground", HexGamma("4A6E86"), 2f, outlinePixels: 0f);
            Material rock = EnsureMaterial("PixelartPilot_Rock", HexGamma("D8BE8A"), 3f);
            Material pillar = EnsureMaterial("PixelartPilot_Pillar", HexGamma("C9A97A"), 3f);
            Material crewRed = EnsureMaterial("PixelartPilot_CrewRed", HexGamma("DE524D"), 3f);
            Material crewBlue = EnsureMaterial("PixelartPilot_CrewBlue", HexGamma("598CD9"), 3f);

            var root = new GameObject("PixelartPilot");

            // ---------------- 几何 ----------------
            // 【尺寸口径】台阶整体 5×5、三级各高 0.45（总高 1.35）；角色 2.0m 高 ——
            // 上一版台阶 6×6、角色 1.2m，角色在画面里太小（宽机位只占屏高 8.6%），
            // 且角色落点是拍脑袋给的坐标，正好落在台阶体积内（脚底埋进 0.6）。
            // 现在角色落点按"台面高度 + 在足迹内/外"算清楚，见 AddCrew 的调用处。
            GameObject groundMesh = NewPrimitive(PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = Vector3.zero;
            groundMesh.transform.localScale = new Vector3(6f, 1f, 6f);   // 60×60（Plane 图元 10×10）

            const float stepHeight = 0.45f;
            AddBox(root.transform, "Step1", new Vector3(0f, stepHeight * 0.5f, 0f),
                new Vector3(5.0f, stepHeight, 5.0f), rock);                       // 顶面 y=0.45，足迹 ±2.5
            AddBox(root.transform, "Step2", new Vector3(0f, stepHeight * 1.5f, 0f),
                new Vector3(3.5f, stepHeight, 3.5f), rock);                       // 顶面 y=0.90，足迹 ±1.75
            AddBox(root.transform, "Step3", new Vector3(0f, stepHeight * 2.5f, 0f),
                new Vector3(2.2f, stepHeight, 2.2f), rock);                       // 顶面 y=1.35，足迹 ±1.10

            // 立柱：站在地面上、完全在台阶足迹之外（x=-3.4 < -2.5）。
            AddBox(root.transform, "Pillar", new Vector3(-3.4f, 1.1f, -1.6f), new Vector3(1.0f, 2.2f, 1.2f), pillar);

            // 角色：
            // - 红：站在**二级台阶台面**上（y=0.90，x/z=1.45 在 ±1.75 内、±1.10 外 ⇒ 不在三级体积里）
            // - 蓝：站在**地面**上、台阶足迹之外（x=-3.3 < -2.5）
            AddCrew(root.transform, "CrewRed", new Vector3(1.45f, stepHeight * 2f, 1.45f), 200f, crewRed);
            AddCrew(root.transform, "CrewBlue", new Vector3(-3.3f, 0f, 2.6f), -30f, crewBlue);

            // ---------------- 光 ----------------
            // 【太阳高度 58° 是算出来的，不是随手给的】3 档色带下"顶面 / 朝光立面 / 背光立面"
            // 必须落进三个不同档，否则**台阶会读成一块平面**（实测：仰角 48° 时顶面与朝光立面
            // 的 ndotl 只差 0.23、量化后同档 ⇒ 三级台阶糊成一整块菱形）。
            // 抬高仰角会拉开"竖直面 vs 水平面"的 ndotl 差：58° 时顶面/朝光立面/背光面 ≈ 1.0 / 0.5 / 0.0 三档。
            // 方位角沿用本仓"左上光"惯例 140°。
            var sunGo = new GameObject("PixelartSun");
            sunGo.transform.SetParent(root.transform);
            sunGo.transform.rotation = Quaternion.Euler(58f, 140f, 0f);
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

            // ---------------- 相机（正交；俯角 30° = 经典像素等距 2:1） ----------------
            var camGo = new GameObject("PixelartPilotCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = new Vector3(0f, 0.9f, 0f);       // 构图中心：台阶腰高
            float pitch = CameraPitchDegrees * Mathf.Deg2Rad;
            float azim = CameraAzimuthDegrees * Mathf.Deg2Rad;
            // 俯角 θ、方位 φ 时相机在目标上方方向 = (cosθ·sinφ, sinθ, cosθ·cosφ)
            // （θ=30°、φ=45° ⇒ (0.6124, 0.5, 0.6124)；真等距 35.264° 那一档就是等分 (1,1,1)/√3）。
            Vector3 dir = new Vector3(Mathf.Cos(pitch) * Mathf.Sin(azim), Mathf.Sin(pitch),
                Mathf.Cos(pitch) * Mathf.Cos(azim));
            camGo.transform.position = target + dir * CameraDistance;
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraOrthoSize;
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

        /// <summary>
        /// 角色：胶囊身体 + 球头（图元自带，省掉既有船员 prefab 的全部依赖）。
        /// <paramref name="feetWorldY"/> 是**脚底所在高度**：调用方必须按"站在哪个面上"给，
        /// 别给地面高度了事——上一版把角色放在台阶足迹内、脚底却是 y=0，于是半个身子埋进台阶里。
        /// </summary>
        static void AddCrew(Transform parent, string name, Vector3 feetPosition, float yawDegrees, Material material)
        {
            var crewRoot = new GameObject(name);
            crewRoot.transform.SetParent(parent);
            crewRoot.transform.localPosition = feetPosition;
            crewRoot.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            float scale = CrewHeight * 0.5f;          // 胶囊图元高 2 ⇒ scale = 身高/2
            float headScale = CrewHeight * 0.4f;      // 头径 ≈ 0.4 × 身高（低模大头，读得清）
            float bodyCenter = CrewHeight * 0.5f;     // 胶囊中心在身高一半处

            GameObject body = NewPrimitive(PrimitiveType.Capsule, name + "_Body", crewRoot.transform, material);
            body.transform.localPosition = new Vector3(0f, bodyCenter, 0f);
            body.transform.localScale = new Vector3(scale, scale, scale);

            GameObject head = NewPrimitive(PrimitiveType.Sphere, name + "_Head", crewRoot.transform, material);
            head.transform.localPosition = new Vector3(0f, CrewHeight * 1.05f, 0f);
            head.transform.localScale = new Vector3(headScale, headScale, headScale);
        }

        /// <summary>
        /// 新建/就地更新物体材质（幂等：重跑覆盖，常量表是唯一调色入口）。
        /// 只设"这个物体该长什么样"的逐物体参数；着色数学全在走 shader 全局的那一趟里。
        /// </summary>
        static Material EnsureMaterial(string name, Color albedo, float bandCount, float outlinePixels = 1.2f)
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

            // 反向壳描边：线宽单位 = 低分辨率像素（1.2 px ≈ 360 档下 3~4 屏幕像素）。
            mat.SetColor("_InkColor", HexGamma("120C14"));   // 墨色：比纯黑带一点紫（阴影里不发死）
            mat.SetFloat("_OutlinePixels", outlinePixels);

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
        /// 【为什么走文本直改而不是 SerializedObject】`EditorBuildSettingsScene.guid` 在 2022.3 是
        /// <c>GUID</c> **结构体**而不是字符串——用 <c>SerializedProperty.stringValue</c> 读它直接抛
        /// "type is not a supported string value"，而异常发生在写完 path 之前，于是**插进去了半条脏记录**
        /// （实测：本脚本第一版把 ToonPilot 变成两条、新场景反而没进去，且只在日志里留一行异常）。
        /// 本仓既有装配脚本（`ToonPilotSetup`）对同一问题也是走 YAML 直改，这里沿用同一条路。
        ///
        /// 【实现口径】先找**最后一条 guid 行**（每个场景条目的末行），在它之后插入完整的三行条目；
        /// 幂等靠"文本里已有该 path 就跳过"；写完**读文件回验**并把条数打进日志——
        /// 注册这类"默默不生效"的活，不读回等于没做。
        /// 播放器出包不受本文件影响：`BuildScript` 是显式把场景集传给 <c>BuildPlayerOptions</c> 的。
        /// </summary>
        static void RegisterInBuildSettings(string scenePath)
        {
            AssetDatabase.ImportAsset(scenePath);   // 确保 .meta（guid）已生成

            string guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("[PixelartPilotSetup] 场景 guid 解析失败：" + scenePath);
                return;
            }

            // File IO 必须绝对路径（batchmode 下相对路径按进程 CWD 解析）。
            string abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "..", "ProjectSettings/EditorBuildSettings.asset")).Replace('\\', '/');

            string text;
            try
            {
                text = System.IO.File.ReadAllText(abs);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PixelartPilotSetup] 读 EditorBuildSettings.asset 失败：" + e.Message);
                return;
            }

            if (!text.Contains("path: " + scenePath))
            {
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                System.Text.RegularExpressions.MatchCollection guids =
                    System.Text.RegularExpressions.Regex.Matches(text, @"guid: [0-9a-f]{32}");
                if (guids.Count == 0)
                {
                    Debug.LogError("[PixelartPilotSetup] EditorBuildSettings 里找不到任何场景条目，未注册。");
                    return;
                }

                System.Text.RegularExpressions.Match last = guids[guids.Count - 1];
                string entry = newline + "  - enabled: 1" + newline + "    path: " + scenePath
                    + newline + "    guid: " + guid;
                text = text.Insert(last.Index + last.Length, entry);
                System.IO.File.WriteAllText(abs, text);
            }

            // 读回验证。
            string[] verify = System.IO.File.ReadAllLines(abs);
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

            AssetDatabase.Refresh();
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
