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
    /// 【尺寸口径：场景要"扛得住一次跳跃"】角色高 1.82m（人的尺度），场地 160×160、中央台阶 18×18。
    /// 这条是创始人两次纠正的结果：先是"角色在画面里太小"，改大角色之后又变成
    /// "角色为什么这么大 / 场景为什么这么小"——**根因是把镜头推近了**（正交 size 一度只有 3.2，
    /// 可见高度 6.4m，一个 2m 的角色占了 31% 屏高）。现在镜头可见高度 28m，角色占 6.5%，
    /// 一次 2m 的跳跃在 18m 的台子上只挪一小段。
    ///
    /// 【内容全是图元】地面 + 三级台阶（三面朝向不同 → 色带切分一眼可辨）+ 两根立柱 + 三个木箱
    /// + 两名船员（**方块拼的台柱身 + 球形头**）。不引程序化网格生成器、不依赖任何既有装配脚本
    /// ——几何正确性由 Unity 图元保证。
    ///
    /// 【材质】全部新建（`Assets/Art/Materials/Pixelart/`），只吃本路径的物体 shader；
    /// 逐物体的差异都在"色带档数"上：地面 2 档、其余 3 档。
    ///
    /// 【取景口径不在这里】俯角/方位/机位距离/构图中心全在 <see cref="PixelartPilotScene"/>
    /// ——出图脚本也要读同一份（各写一份的后果：场景 35.264°、出图脚本 30°，比对结论全错）。
    ///
    /// 【本机 batchmode 的两条教训照旧适用】File IO 必须绝对路径；Build Settings 走
    /// ProjectSettings 资产而不是 <c>EditorBuildSettings.scenes</c>（后者在 batchmode 下可能不落盘）。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙像素化试点场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartPilotSetup.BuildAll</c>。
    /// </summary>
    public static class PixelartPilotSetup
    {
        const string ScenePath = "Assets/Scenes/" + PixelartPilotScene.SceneName + ".unity";
        const string MaterialFolder = "Assets/Art/Materials/Pixelart";
        const string DitherFolder = "Assets/Art/Textures/Fx/Dither";

        /// <summary>每级台阶的高度（三级台阶的总高 1.5m ≈ 角色高，走上去有"台地"的读法）。</summary>
        const float StepHeight = 0.5f;

        [MenuItem("PirateCrew/Pixelart/烘焙像素化试点场景")]
        public static void BuildAll()
        {
            // 渲染器与索引先备齐：场景要把两个索引写进 PixelartCameraRig，否则运行时相机挂错渲染器。
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError("[PixelartPilotSetup] 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            // 抖动图案（v3 的 1-bit 密度图案）先备齐，材质里挂好、_DitherMode 留在 0，
            // 想试 v3 口径就把材质上的 _DitherMode 拨到 1（不必重烘场景）。
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            EnsureFolder("Assets/Art/Materials");
            EnsureFolder(MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质（第二个参数 = 色带档数，第三个 = 描边线宽/低分辨率像素）----------------
            // 地面不描边（铺满画面的大平面的壳环会顶到边缘、无观感意义），且队列压到 1999 ——
            // 墨线壳是"外扩 + 沿视线拉近"，轮廓外侧那圈像素落在地面上；地面**必须先画完**，
            // 否则它 ZWrite On 的本体会把壳环整圈盖掉（渲染篇 §5 + 旧链"海面回 2000"的同一结论）。
            Material ground = EnsureMaterial("PixelartPilot_Ground", HexGamma("4A6E86"), 2f,
                outlinePixels: 0f, renderQueue: PixelartPath.BackgroundPlaneRenderQueue);
            Material rock = EnsureMaterial("PixelartPilot_Rock", HexGamma("D8BE8A"), 3f);
            Material pillar = EnsureMaterial("PixelartPilot_Pillar", HexGamma("C9A97A"), 3f);
            Material crate = EnsureMaterial("PixelartPilot_Crate", HexGamma("A8703F"), 3f);
            Material crewRed = EnsureMaterial("PixelartPilot_CrewRed", HexGamma("DE524D"), 3f);
            Material crewBlue = EnsureMaterial("PixelartPilot_CrewBlue", HexGamma("598CD9"), 3f);

            var root = new GameObject("PixelartPilot");

            // ---------------- 地面（160×160：一次跳跃在它上面微不足道）----------------
            GameObject groundMesh = NewPrimitive(PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = Vector3.zero;
            groundMesh.transform.localScale = new Vector3(16f, 1f, 16f);   // Plane 图元 10×10

            // ---------------- 中央三级台阶（足迹 18 / 13 / 8）----------------
            // 每级 0.5 高：三级总高 1.5m，和角色差不多高——远看能读出"这是个台地"。
            AddBox(root.transform, "Step1", new Vector3(0f, StepHeight * 0.5f, 0f),
                new Vector3(18f, StepHeight, 18f), rock);                        // 顶面 y=0.5，足迹 ±9.0
            AddBox(root.transform, "Step2", new Vector3(0f, StepHeight * 1.5f, 0f),
                new Vector3(13f, StepHeight, 13f), rock);                        // 顶面 y=1.0，足迹 ±6.5
            AddBox(root.transform, "Step3", new Vector3(0f, StepHeight * 2.5f, 0f),
                new Vector3(8f, StepHeight, 8f), rock);                          // 顶面 y=1.5，足迹 ±4.0

            // ---------------- 陈设（给"这是一张地图"的尺度参照）----------------
            // 立柱：站在地面上、完全在台阶足迹之外（|x| > 9）。
            AddBox(root.transform, "PillarA", new Vector3(11.5f, 3.0f, -4.5f), new Vector3(1.4f, 6.0f, 1.4f), pillar);
            AddBox(root.transform, "PillarB", new Vector3(-12.0f, 3.0f, 7.5f), new Vector3(1.4f, 6.0f, 1.4f), pillar);

            // 木箱：地面上的两个 + 二级台面外圈的一个（脚底各自贴在所在的面上）。
            AddBox(root.transform, "CrateGroundA", new Vector3(7.5f, 0.7f, -8.5f), new Vector3(1.4f, 1.4f, 1.4f), crate);
            AddBox(root.transform, "CrateGroundB", new Vector3(-6.5f, 0.6f, 9.5f), new Vector3(1.2f, 1.2f, 1.2f), crate);
            AddBox(root.transform, "CrateOnStep2", new Vector3(5.2f, StepHeight + 0.45f, -5.2f),
                new Vector3(0.9f, 0.9f, 0.9f), crate);                           // 二级顶面 y=1.0 + 半个箱高

            // ---------------- 角色（脚底高度按"站在哪个面"给，别一律给 0）----------------
            // - 红：二级台阶台面（y=1.0；x/z=4.6 在足迹 ±6.5 内、±4.0 外 ⇒ 不在三级体积里，也不会被箱子压到）
            // - 蓝：地面，台阶足迹之外（|x|=6.5 会落在足迹内，故给到 -7.5）
            AddCrew(root.transform, "CrewRed", new Vector3(4.6f, StepHeight * 2f, 4.6f), 200f, crewRed);
            AddCrew(root.transform, "CrewBlue", new Vector3(-7.5f, 0f, 6.0f), -30f, crewBlue);

            // ---------------- 光 ----------------
            // 【太阳高度 58° 是算出来的，不是随手给的】3 档色带下"顶面 / 朝光立面 / 背光立面"
            // 必须落进三个不同档，否则**台阶会读成一块平面**（实测：仰角 48° 时顶面与朝光立面
            // 的 ndotl 只差 0.23、量化后同档 ⇒ 所有台面糊成一整块菱形）。
            // 58° 时顶面/朝光立面/背光面 ≈ 0.85 / 0.41 / 0.0 三档。
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

            // 环境光 = 本路径的"暗部色"（暗面 = albedo × 环境色）。来源与取舍：
            //   · 口径：渲染篇 §4.6"单平行光 + 单环境光"，蓝本 §7.2 第 5 条"GI 用扁平环境光"
            //     ——v3 的暗面来自 SH（`_GlobalIlluminationBuffer` 的 lightmap 通道实际是空的，
            //     等于只吃 SH，蓝本 §2 注意 2），本路径按裁决简化为这一项。
            //   · **不能给太小**：旧链取 #1A1E28 是有配套的——它的暗部是材质上的
            //     **替换式暗部色**（`_ShadowColor`，渲染篇 §4.2），本路径还没有那条通道，
            //     暗面就是 albedo × 环境色；环境色压到 #1A1E28 时暗面落在 sRGB 0.08 上下，
            //     与墨线（#120C14）几乎同值，轮廓线和暗面糊成一片，等于没有描边。
            //   · **也不能给太大**：加法环境光会抬平明暗跨度（旧链实测结论）。
            //   本档 #3A4760 让暗面落在 sRGB 0.2 上下：明显暗于亮面、又明显亮于墨线。
            //   这一条是**美术旋钮**（改这里即整体调暗部亮度），不是机制。
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = HexGamma("3A4760");

            // ---------------- 相机（正交，俯角 30° = 规则像素阶梯） ----------------
            var camGo = new GameObject("PixelartPilotCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = PixelartPilotScene.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartPilotScene.AzimuthDegrees);
            camGo.transform.position = target + dir * PixelartPilotScene.CameraDistance;
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = PixelartPilotScene.OrthoSize;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
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
            rig.renderHeight = PixelartPilotScene.RenderHeight;
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
                + "；低分辨率 RT 高 " + PixelartPilotScene.RenderHeight
                + "；俯角 " + PixelartPilotScene.PitchDegrees + "°）。");
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

        static GameObject AddBoxObj(Transform parent, string name, Material material)
        {
            return NewPrimitive(PrimitiveType.Cube, name, parent, material);
        }

        static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = AddBoxObj(parent, name, material);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
        }

        /// <summary>
        /// 角色：**方块拼的台柱形 + 球头**（腿 + 躯干 + 球头 + 帽檐），全部同一材质。
        ///
        /// 【为什么躯干不用胶囊图元】胶囊是圆管：低分辨率下没有面与面的转折，色带切不出结构，
        /// 剪影读起来就是"一根柱子"而不是一个角色（创始人一眼指出：胶囊形不对，要台柱形）。
        /// 方块件每个面各自成档（正面/侧面/顶面三档），像素风要的正是这种"面 = 色块"的读法。
        ///
        /// 【头为什么是球】创始人指定："我不是让头部是个球吗"。球在低分辨率下由
        /// 剪影（覆盖度判据）勾出一圈轮廓、面上不生成内线（球面法线渐变，跨不过 55° 阈值）。
        ///
        /// 总高 1.82m（腿 0.60 / 躯干 0.60 / 球头 0.55 / 帽檐 0.07）。
        /// <paramref name="feetPosition"/> 是**脚底世界高度**：调用方按"站在哪个面上"给，
        /// 别一律给地面高度（上一版把角色放在台阶足迹内、脚底却是 y=0，半个身子埋进台阶）。
        /// </summary>
        static void AddCrew(Transform parent, string name, Vector3 feetPosition, float yawDegrees, Material material)
        {
            var crewRoot = new GameObject(name);
            crewRoot.transform.SetParent(parent);
            crewRoot.transform.localPosition = feetPosition;
            crewRoot.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            AddBox(crewRoot.transform, name + "_Legs", new Vector3(0f, 0.30f, 0f),
                new Vector3(0.60f, 0.60f, 0.50f), material);
            AddBox(crewRoot.transform, name + "_Torso", new Vector3(0f, 0.90f, 0f),
                new Vector3(0.78f, 0.60f, 0.56f), material);

            GameObject head = NewPrimitive(PrimitiveType.Sphere, name + "_Head", crewRoot.transform, material);
            head.transform.localPosition = new Vector3(0f, 1.475f, 0f);
            head.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);

            // 帽檐：比头宽一圈的薄板——低分辨率下"有顶帽子"全靠这一圈外扩。
            AddBox(crewRoot.transform, name + "_Hat", new Vector3(0f, 1.785f, 0f),
                new Vector3(0.82f, 0.07f, 0.82f), material);
        }

        /// <summary>
        /// 新建/就地更新物体材质（幂等：重跑覆盖，常量表是唯一调色入口）。
        /// 只设"这个物体该长什么样"的逐物体参数；着色数学全在走 shader 全局的那一趟里。
        ///
        /// 【描边线宽为什么是 1】渲染篇 §5 的起步值是"RT 空间 1px"，旧链实测要 2px 的原因是
        /// **旧架构**"全分辨率渲染 → 3× 点降采"会把 1px 线欠采掉；本路径几何**直接渲进
        /// 低分辨率 RT**，1 低分辨率像素 = 1920 宽屏上 5 屏幕像素，1px 是实打实可见的。
        /// 所以这里**不要**把旧链的 2px 当基准搬过来。
        /// </summary>
        static Material EnsureMaterial(string name, Color albedo, float bandCount,
            float outlinePixels = 1.0f, int renderQueue = -1)
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

            // 反向壳描边（渲染篇 §5）：线宽单位 = 低分辨率像素；0 = 本物体不描边。
            // 墨色与 UI 令牌 INK 同色（3D/2D 描边同色统一）。
            mat.SetColor("_InkColor", HexGamma("120C14"));   // 比纯黑带一点紫（阴影里不发死）
            mat.SetFloat("_OutlinePixels", outlinePixels);

            // -1 = 用 shader 里的 Queue（Geometry = 2000）；大平面由调用方压到 1999。
            mat.renderQueue = renderQueue >= 0 ? renderQueue : 2000;

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
