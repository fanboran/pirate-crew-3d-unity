using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PirateCrew.Core;
using PirateCrew.WaterLab;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实验水体场景（WaterLab）的装配器：代码建场景（沿 Battle/Pixelart 试点惯例，重跑即重建），
    /// 菜单 <c>Tools/PirateCrew/WaterLab/Build Scene</c>。
    ///
    /// 【组成】太阳平行光 + 透视相机 + 程序化沙滩地形网格（顶点直接放 TerrainHeight 高度）
    /// + 水面平面（顶点位移由 WaterLabOcean.shader 采样模拟高度场）+ WaterLabSimulation 驱动物体。
    ///
    /// 【资产】材质落盘 <c>Assets/Art/Materials/WaterLab/</c>（场景不内嵌材质，必须存资产）；
    /// compute 引用按路径加载注入（场景序列化 PPtr）。实验场景不进 Build Settings（编辑器验证用）。
    /// 场景路径经 <see cref="SceneNames.PathOf"/> 推导（Lab 二级目录的唯一真源）。
    /// </summary>
    public static class WaterLabSetup
    {
        const string ShaderPathOcean = "Assets/Art/Shaders/WaterLab/WaterLabOcean.shader";
        const string ShaderPathSand = "Assets/Art/Shaders/WaterLab/WaterLabSand.shader";
        const string ComputePath = "Assets/Art/Shaders/WaterLab/WaterLabSim.compute";
        const string MaterialFolder = "Assets/Art/Materials/WaterLab";

        /// <summary>地形网格每轴段数（0.5 m/格，与地形模拟分辨率一致）。</summary>
        const int TerrainSegments = WaterLabRules.GroundCells;

        /// <summary>水面网格每轴段数（0.5 m/顶点；顶点位移采样 0.25 m 高度场，双线性平滑）。</summary>
        const int SurfaceSegments = 128;

        [MenuItem("Tools/PirateCrew/WaterLab/Build Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 材质目录先删后建：shader 默认值改动只影响新建材质，旧 .mat 会锁死旧参数
            //（序列化陷阱）。重建语义 = 全量重装配。
            AssetDatabase.DeleteAsset(MaterialFolder);
            EnsureFolder(MaterialFolder); // 材质目录先建（装配过程中就要写材质资产）

            CreateLighting();
            var camera = CreateCamera();
            CreateTerrain();
            CreateWaterSurface();
            CreateSimulation();
            _ = camera; // 取景只影响保存时的默认视角，Play 沿用

            Directory.CreateDirectory("Assets/Scenes/Lab");
            var path = SceneNames.PathOf(SceneNames.WaterLab);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[WaterLabSetup] 场景已装配并保存：{path}");
        }

        /// <summary>无头出图组合入口：重建场景 → 武装截图器（它进 Play 抓完自己 Exit 收口）。
        /// 必须带图形跑（capture 铁律禁 -nographics）；输出目录 <c>PC3D_CAPTURE_DIR</c>。
        /// 不经 HeadlessChain：截图器是异步武装流程，链式器的同步收口会掐死它。</summary>
        public static void BuildSceneAndCapture()
        {
            BuildScene();
            BattleHudScreenshot.CaptureHeadless();
        }

        static void CreateLighting()
        {
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.0f;
            light.color = new Color(1.0f, 0.95f, 0.85f);
            // 太阳方位放相机对面（指向太阳 L=(-0.525,0.669,-0.525)，方位 225°/仰角 42°）：
            // 水面反射闪耀路径 dot(reflect(-V,N), L)≈1 才会横穿取景——同侧时整条 glitter 路在画外
            //（实拍第二轮海面无闪点的根因）。forward = -L 必须朝下照地。
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.525f, -0.669f, 0.525f));
            sun.transform.position = new Vector3(-24f, 30f, -36f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.50f, 0.58f);
            RenderSettings.sun = light; // WaterLabSimulation.PublishSunDir 解析源
        }

        static Camera CreateCamera()
        {
            var go = new GameObject("LabCamera");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = false;
            // 俯视沙盘取景（向 Battle 的俯角等距观感靠拢）：长焦 + 高机位俯角 40°、方位 45°。
            // 岸线约在 x≈14（剖面 0 高程点），取景中心放 (2, 0)，海面与干沙滩同框。
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.88f);
            // 取景：48° 俯角，中心再向海压（水面为主体，沙滩退居画面下缘）
            var center = new Vector3(-4f, -0.5f, 0f);
            float pitch = 48f * Mathf.Deg2Rad;
            float yaw = 45f * Mathf.Deg2Rad;
            float dist = 40f;
            var dir = new Vector3(
                Mathf.Sin(pitch) * Mathf.Sin(yaw),
                Mathf.Cos(pitch),
                Mathf.Sin(pitch) * Mathf.Cos(yaw)); // 从中心指向机位
            go.transform.position = center + dir * dist;
            go.transform.rotation = Quaternion.LookRotation(-dir);
            return cam;
        }

        static void CreateTerrain()
        {
            var mesh = new Mesh { name = "WaterLabTerrain" };
            int verts = TerrainSegments + 1;
            float step = WaterLabRules.DomainSize / TerrainSegments;

            var vertices = new Vector3[verts * verts];
            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    float wx = -WaterLabRules.DomainSize * 0.5f + x * step;
                    float wz = -WaterLabRules.DomainSize * 0.5f + z * step;
                    vertices[x + z * verts] = new Vector3(wx, WaterLabRules.TerrainHeight(wx, wz), wz);
                }
            }

            var triangles = new int[TerrainSegments * TerrainSegments * 6];
            int t = 0;
            for (int z = 0; z < TerrainSegments; z++)
            {
                for (int x = 0; x < TerrainSegments; x++)
                {
                    int i0 = x + z * verts;
                    int i1 = i0 + 1;
                    int i2 = i0 + verts;
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = LoadOrCreateMaterial(
                "WaterLab_Sand", ShaderPathSand);
        }

        static void CreateWaterSurface()
        {
            var mesh = new Mesh { name = "WaterLabSurface" };
            int verts = SurfaceSegments + 1;
            float step = WaterLabRules.DomainSize / SurfaceSegments;

            var vertices = new Vector3[verts * verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    vertices[x + z * verts] = new Vector3(
                        -WaterLabRules.DomainSize * 0.5f + x * step, 0f,
                        -WaterLabRules.DomainSize * 0.5f + z * step);

            var triangles = new int[SurfaceSegments * SurfaceSegments * 6];
            int t = 0;
            for (int z = 0; z < SurfaceSegments; z++)
            {
                for (int x = 0; x < SurfaceSegments; x++)
                {
                    int i0 = x + z * verts;
                    int i1 = i0 + 1;
                    int i2 = i0 + verts;
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            var go = new GameObject("WaterSurface");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = LoadOrCreateMaterial("WaterLab_Ocean", ShaderPathOcean);
            // 原作水面不投影（Lit_Water blob 实证无 ShadowCaster pass）；水底波影
            // 由沙侧「阴影 × exp(-0.721×水深)」承担——开投影反而整网格盖灰影（实拍实证）
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 细节法线贴图（WaterNormal2D 程序化等价物）：碎金 glitter 的高频法线源，
            // 生成一次落资产（材质引用它；重建时已存在则复用）
            var detailPath = $"{MaterialFolder}/WaterLabDetailNormal.asset";
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(detailPath);
            if (detail == null)
            {
                detail = WaterLabDetailNormal.Create();
                AssetDatabase.CreateAsset(detail, detailPath);
            }
            renderer.sharedMaterial.SetTexture("_WaterLabDetailNormal", detail);
            // 亮脊网 = 宽瓣镜面扫亮细节法线的棱线（逆向档案 §4.1：原作 GGX 高光与
            // pow1500 尖瓣并存——尖瓣只出孤立碎点，宽瓣才出连绵网；p80/p400 同贴图
            // 对照实证）。pow48 旧值 96、强度旧值 0.02：脊上只能加 1-4% 亮度，
            // 网「有形无光」；0.6×pow48 ≈ GGX 中粗糙度，脊线 +0.2 亮度（r17→r18）
            renderer.sharedMaterial.SetFloat("_SpecularBroad", 0.6f);
        }

        static void CreateSimulation()
        {
            var go = new GameObject("WaterLabSimulation");
            var sim = go.AddComponent<WaterLabSimulation>();
            sim.computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            if (sim.computeShader == null)
                Debug.LogError($"[WaterLabSetup] 找不到 compute：{ComputePath}（检查文件是否入库/被导入）");
        }

        static Material LoadOrCreateMaterial(string name, string shaderPath)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                Debug.LogError($"[WaterLabSetup] 找不到 shader：{shaderPath}");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader != shader)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            // 新文件夹要先被 AssetDatabase 认领，紧接着的 CreateAsset 才不会失败
            AssetDatabase.Refresh();
        }
    }
}
