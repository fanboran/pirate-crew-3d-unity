using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PirateCrew.SceneArt;
using PirateCrew.UI.Stick;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 等距像素卡通·步骤 2 试点装配（渲染篇 §8-2；立项任务书 M1 工作项 1a/1b 的载体）。
    ///
    /// 【试点是什么】一个独立小场景 ToonPilot.unity：海面 quad + 试点岛台 + 两名船员，
    /// 全部用 <c>PirateCrew/PirateToon</c>（赛璐璐本体 + 常驻 INK 反壳描边）+ 左上单光。
    /// 验收出口 = 播放器 <c>-toonPilotOut &lt;目录&gt;</c>（PlayerArtCapture 分支），
    /// 不污染 Battle 场景与任何玩法 prefab——船员是**实例 override**（复制网格烘平滑法线后换材质），
    /// 7 个职业预制体本体零改动。
    ///
    /// 【平滑法线】岛台网格（IslandShellGeometry 程序化硬边）与船员部件网格都在装配时经
    /// <see cref="SmoothNormalsBaker"/>（角度加权 + 位置容差）烘进顶点色 GBA（R=0.5 阈值中性）。
    ///
    /// 【调色数值全【AI 提案】】亮/暗部色、光色、天空色都是 M1 样张裁决 #1-#5 的起点值，
    /// 实拍过审后在材质资产上调（本装配幂等重跑会覆盖——调参轮里改这里的常量表）。
    ///
    /// 【Build Settings】本机 batchmode 下 EditorBuildSettings.scenes API 不落盘（§18 教训），
    /// 故注册走 EditorBuildSettings.asset 的 YAML 直改（幂等：已在列表则跳过）。
    /// </summary>
    public static class ToonPilotSetup
    {
        const string ScenePath = "Assets/Scenes/ToonPilot.unity";
        const string MeshFolder = "Assets/Art/Models/SceneKit/Baked";
        const string MaterialFolder = "Assets/Art/Materials/Toon";
        const string BuildSettingsAsset = "ProjectSettings/EditorBuildSettings.asset";

        [MenuItem("PirateCrew/ToonPilot/烘焙试点场景")]
        public static void BuildAll()
        {
            EnsureFolder(MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            float deckY = BakePilotIslet();
            Material sea = EnsureToonMaterial("ToonPilot_Sea", HexGamma("2E5F80"), HexGamma("1C3A52"), 0f); // 大平面不描边；深调海面
            Material ground = sea; // 试点阶段海面即"地面"；真像素海面是 M2f 裁决 #7
            Material crewRed = EnsureToonMaterial("ToonPilot_CrewRed", HexGamma("DE524D"), HexGamma("6B3352"));
            Material crewBlue = EnsureToonMaterial("ToonPilot_CrewBlue", HexGamma("598CD9"), HexGamma("345085"));

            // ---- 场景内容 ----
            var root = new GameObject("ToonPilot");

            // 海面 quad（30×30 @ 水面高度 -0.4）。
            // 【旋转方向实测教训】Unity Quad 默认法线朝 +Z，Euler(90,0,0) 才把它转到 +Y 朝上；
            // 首版写成 Euler(-90,0,0)（法线朝下）→ ToonBase 的 Cull Back 把海面本体整个剔除、
            // 而 ToonInk 的 Cull Front 反而整面画出（正交 45° 下大平面铺满视野）→ 全屏墨色。
            GameObject seaQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            seaQuad.name = "ToonPilot_Sea";
            Object.DestroyImmediate(seaQuad.GetComponent<Collider>());
            seaQuad.transform.SetParent(root.transform);
            seaQuad.transform.position = new Vector3(0f, -0.4f, 0f);
            seaQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            seaQuad.transform.localScale = new Vector3(30f, 30f, 1f);
            seaQuad.GetComponent<MeshRenderer>().sharedMaterial = ground;

            // 试点岛台（prefab 实例）。【网格原点在角落】TileTerrainGrid 的格子从 (0,0) 铺起，
            // 6×6×2u 的岛壳几何中心在世界 (6,·,6)——实例平移 (-6,0,-6) 把岛台中心挪到原点，
            // 相机（看向原点）与船员摆放才成立。首版漏了这步：岛台整体偏在画面右半、
            // 放大中心落在空海面上（构图与描边判读全歪）。
            GameObject islet = PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(MeshFolder + "/ToonPilot_Islet.prefab")) as GameObject;
            islet.transform.SetParent(root.transform);
            islet.transform.position = new Vector3(-6f, 0f, -6f);

            // 船员 ×2（实例 override：复制网格烘平滑法线 + 换 Toon 材质；跳过 ContactShadow 贴片）。
            // 落位按台面世界高度算：中央 3 块 × BlockWorldHeight 0.5 = 1.5u 顶面；
            // 船员根 ≈ 脚底 + 0.25（ContactShadowDecal 类头推导），故 rootY = 顶面 + 0.25。
            SpawnToonCrew("Assets/Prefabs/PirateCrew/Crew/Sailor.prefab", "ToonPilot_CrewRed", crewRed,
                new Vector3(-1.2f, deckY + 0.25f, 0.6f), 150f, root.transform);
            SpawnToonCrew("Assets/Prefabs/PirateCrew/Crew/Sniper.prefab", "ToonPilot_CrewBlue", crewBlue,
                new Vector3(1.6f, deckY + 0.25f, -1.0f), -35f, root.transform);

            // 左上单光（方向同 Battle 主光口径 Euler(48,140)，无阴影——色带表面接实时投影必脏）。
            var sunGo = new GameObject("ToonSun");
            sunGo.transform.SetParent(root.transform);
            sunGo.transform.rotation = Quaternion.Euler(48f, 140f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = HexGamma("FFF5E0");
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;

            // 扁平环境光（ToonLightDriver 会把它下发为 _ToonAmbientColor）。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = HexGamma("1A1E28"); // 收小环境光：加法环境光会抬平明暗跨度（参照图最暗像素是真暗）

            var driverGo = new GameObject("ToonLightDriver");
            driverGo.transform.SetParent(root.transform);
            driverGo.AddComponent<global::PirateCrew.Rendering.ToonLightDriver>();

            // 主相机：正交真等距（俯角 35.264°、方位 45°）看向岛台（渲染篇 §2/§2.1）。
            var camGo = new GameObject("ToonPilotCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = new Vector3(0f, 1.0f, 0f);
            // 【真等距口径（渲染篇 §2.1 裁决）】俯角 35.264° + 方位 45° ⇔ 视线沿立方对角线：
            // offset = 30×(1,1,1)/√3 ≈ (17.32,17.32,17.32)，用 LookAt 定朝向（免手推 Euler）。
            camGo.transform.position = target + new Vector3(17.32f, 17.32f, 17.32f);
            camGo.transform.LookAt(target);
            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = HexGamma("7E9BB4"); // 天空色板起步值（裁决 #5）；深调以留明暗跨度
            camGo.AddComponent<AudioListener>();

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            RegisterInBuildSettingsYaml();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ToonPilotSetup] 试点场景烘焙完成：" + ScenePath
                + "（岛台 + 双船员 + 海面，PirateToon 材质 + 平滑法线顶点色；调色常量在本文件头）。");
        }

        // ------------------------------------------------------------------
        // 试点岛台：手写 6×6 对称山丘高度场 → 岛壳 → 平滑法线 → 网格资产 + prefab
        // ------------------------------------------------------------------
        /// <summary>烘焙岛台并返回台面顶世界高度（腿部落位用；-1 = 失败）。</summary>
        static float BakePilotIslet()
        {
            // 高度场（行主序，块数；1 档裙边、2 档腰、3 档顶）：同 L2 碎岛的列式模式。
            int[] blocks =
            {
                1, 1, 1, 1, 1, 1,
                1, 2, 2, 2, 2, 1,
                1, 2, 3, 3, 2, 1,
                1, 2, 3, 3, 2, 1,
                1, 2, 2, 2, 2, 1,
                1, 1, 1, 1, 1, 1,
            };
            var grid = new PirateCrew.Battle.TileTerrainGrid(6, 6, blocks, 0f);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);
            if (shell.IsEmpty)
            {
                Debug.LogError("[ToonPilotSetup] 试点岛壳烘出空网格（高度场/设置有误），中止。");
                return -1f;
            }

            Vector3[] vertices = shell.ToVertices();
            int[] triangles = shell.ToTriangles();
            Vector3[] normals = shell.ToNormals();
            Color[] smoothColors = SmoothNormalsBaker.Bake(vertices, triangles, normals);

            Mesh mesh = new Mesh { name = "ToonPilot_Islet" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetColors(smoothColors);

            Mesh meshAsset = EnsureMeshAsset(MeshFolder + "/ToonPilot_Islet.asset", mesh);
            Material rock = EnsureToonMaterial("ToonPilot_Islet", HexGamma("E3C795"), HexGamma("4A4266")); // 暗部 lum≈70 ≈ 亮部 31%（参照暗/亮比 27-40%）

            var root = new GameObject("ToonPilot_Islet");
            var child = new GameObject("SceneArt_ToonIslet");
            child.transform.SetParent(root.transform);
            child.AddComponent<MeshFilter>().sharedMesh = meshAsset;
            child.AddComponent<MeshRenderer>().sharedMaterial = rock;

            string prefabPath = MeshFolder + "/ToonPilot_Islet.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return grid.SurfaceWorldY(2, 2);
        }

        /// <summary>
        /// 船员试点实例：实例化职业 prefab → 逐 MeshFilter 复制网格烘平滑法线（落资产）→ 换 Toon 材质。
        /// 【不碰 prefab 本体】sharedMesh/材质的修改全部落在场景里的 prefab **实例 override** 上；
        /// ContactShadow 子物体（透明贴片）保持原样——Toon 不透明描边材质会毁掉它。
        /// </summary>
        static void SpawnToonCrew(string prefabPath, string meshPrefix, Material material,
            Vector3 position, float yawDegrees, Transform parent)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null)
            {
                Debug.LogError("[ToonPilotSetup] 找不到职业 prefab：" + prefabPath);
                return;
            }

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (inst == null)
            {
                Debug.LogError("[ToonPilotSetup] 实例化失败：" + prefabPath);
                return;
            }
            inst.name = meshPrefix;
            inst.transform.SetParent(parent);
            inst.transform.position = position;
            inst.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            foreach (MeshFilter filter in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                bool isShadowDecal = filter.GetComponent<PirateCrew.Battle.ContactShadowDecal>() != null
                    || filter.name == "ContactShadow";
                if (isShadowDecal || filter.sharedMesh == null)
                    continue;

                Mesh copy = Object.Instantiate(filter.sharedMesh);
                copy.name = meshPrefix + "_" + filter.gameObject.name;
                Color[] smooth = SmoothNormalsBaker.Bake(copy.vertices, copy.triangles, copy.normals);
                copy.SetColors(smooth);
                filter.sharedMesh = EnsureMeshAsset(MeshFolder + "/" + copy.name + ".asset", copy);

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        /// <summary>落盘网格资产（幂等：已存在则就地覆写保 GUID，同 SceneArtBaker 口径）。</summary>
        static Mesh EnsureMeshAsset(string path, Mesh source)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetDatabase.CreateAsset(Object.Instantiate(source), path);
                return AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }

            mesh.Clear();
            mesh.name = source.name;
            mesh.SetVertices(source.vertices);
            mesh.SetTriangles(source.triangles, 0);
            mesh.SetNormals(source.normals);
            if (source.colors != null && source.colors.Length > 0)
                mesh.SetColors(source.colors);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>创建/更新 PirateToon 试点材质（幂等；重跑覆盖 = 常量表是唯一调色入口）。</summary>
        static Material EnsureToonMaterial(string name, Color bright, Color shadow, float outlinePixels = 2f)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader toon = Shader.Find("PirateCrew/PirateToon");
            if (toon == null)
            {
                Debug.LogError("[ToonPilotSetup] 找不到 PirateCrew/PirateToon（shader 未导入？），中止。");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(toon) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = toon;
            mat.SetColor("_BaseColor", bright);
            mat.SetColor("_ShadowColor", shadow);
            mat.SetColor("_InkColor", StickTokens.INK); // 3D 描边与 UI 令牌 INK 同色（渲染篇 §5）
            mat.SetFloat("_ShadowThreshold", 0.5f);
            mat.SetFloat("_ShadowFeather", 0f);   // 硬切主路径（裁决 #2 对照开关，默认硬切）
            mat.SetFloat("_DitherStrength", 0f);  // 默认关：参照图无有序抖动，开 1.0 会织出 12px 网眼
            mat.SetFloat("_OutlinePixels", outlinePixels); // 岛台/船员 2px：实测 1px 经降采样不可见（裁决#3 对照数据）
            // 海面关描边时同步把队列覆写回 2000（Geometry）：描边物 SubShader 是 Geometry+50
            // 整体晚画，海面若也 +50 会排到描边物之后、fill 全屏盖掉壳环（六轮实测的根因之一）。
            mat.renderQueue = outlinePixels < 0.01f ? 2000 : 2050;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 把 ToonPilot 场景追加进 Build Settings（YAML 直改，幂等）。
        /// 播放器侧 PlayerArtCapture 用 SceneManager.LoadScene("ToonPilot") 按名加载，
        /// 必须在 Build 列表里；本机 batchmode 下 scenes API 不落盘（§18），故直改文件。
        /// </summary>
        static void RegisterInBuildSettingsYaml()
        {
            const string sceneEntry = "Assets/Scenes/ToonPilot.unity";
            AssetDatabase.ImportAsset(ScenePath); // 确保 .meta（guid）已生成

            // File IO 必须绝对路径：batchmode 下相对路径按进程 CWD 解析（§18 教训），
            // 而 AssetDatabase API 恒以项目根为基准（上面 ScenePath 保持相对路径）。
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string metaPath = Path.Combine(projectRoot, sceneEntry + ".meta").Replace('\\', '/');
            string buildSettingsPath = Path.Combine(projectRoot, BuildSettingsAsset).Replace('\\', '/');

            string meta = File.ReadAllText(metaPath);
            Match guidMatch = Regex.Match(meta, @"guid: ([0-9a-f]{32})");
            if (!guidMatch.Success)
            {
                Debug.LogError("[ToonPilotSetup] ToonPilot.unity.meta 没有 guid——先保存场景再注册。");
                return;
            }

            string yaml = File.ReadAllText(buildSettingsPath);
            if (yaml.Contains(sceneEntry))
                return; // 幂等：已注册

            string insertion = "  - enabled: 1\n    path: " + sceneEntry
                + "\n    guid: " + guidMatch.Groups[1].Value + "\n";
            yaml = yaml.Replace("  m_configObjects:", insertion + "  m_configObjects:");
            File.WriteAllText(buildSettingsPath, yaml);
            AssetDatabase.Refresh();
            Debug.Log("[ToonPilotSetup] ToonPilot 已追加进 Build Settings（YAML 直改）。");
        }

        /// <summary>sRGB hex（Gamma 空间直存）→ Color，口径同 SceneKitPilotSetup/SceneArtPalette。</summary>
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
