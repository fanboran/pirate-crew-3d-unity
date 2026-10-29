using PirateCrew.CrewProbe;
using PirateCrew.GeoProbe;
using PirateCrew.Rendering.Pixelart;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **几何标定场**烘焙器：空白场地 + 一排几何基元（真实 Crew 预制体、球、立方体、
    /// 圆柱、六棱柱、正八面体，色相互异），转台整排旋转。给像素化路径做全相位标定：
    /// 剪影对称、逐物体描边闭环、带界抖动、1px 特征随相位的闪动。
    /// 装配结构与 <see cref="CrewProbeSceneSetup"/> 同构（渲染器安装 / 正午光 / 相机 rig /
    /// 控制器接线），常量与角色探针对齐。
    /// </summary>
    public static class GeometryProbeSceneSetup
    {
        const string ScenePath = "Assets/Scenes/Pixelart/GeometryProbe.unity";
        const string LogTag = "[GeometryProbeSceneSetup]";

        static readonly Color GreenScreen = new Color(0f, 1f, 0f, 1f);

        [MenuItem("PirateCrew/像素探针/烘焙几何标定场")]
        public static void BuildAll()
        {
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，场景未烘焙：" + error);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                PixelartStageKit.DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();
            PixelartStageKit.EnsureFolder(PixelartStageKit.MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 地面：单档、无抖动、无内线、不描边（背景判定与角色探针同口径：近白=背景）。
            Material ground = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Ground",
                PixelartStageKit.Hex("D8D8D8"), 1f, outlinePixels: 0f, aaScale: 0f);
            ground.SetFloat("_DitherStrength", 0f);
            ground.SetFloat("_NormalEdgeLevel", 0f);

            var root = new GameObject(GeometryProbeController.SceneName);

            GameObject groundGo = PixelartStageKit.NewPrimitive(PrimitiveType.Plane, "Ground",
                root.transform, ground);
            groundGo.transform.localPosition = Vector3.zero;
            groundGo.transform.localScale = new Vector3(4f, 1f, 4f);
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;

            // ---------------- 转台：一排标定体 ----------------
            // 色相互异且避开绿幕；尺寸让关键维度 ≈15–50 艺术像素（P=24 下 0.6–2m）。
            var turntable = new GameObject("Turntable");
            turntable.transform.SetParent(root.transform, false);

            Material matYellow = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Yellow",
                PixelartStageKit.Hex("FFD24A"), 4f);
            Material matWhite = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_White",
                PixelartStageKit.Hex("F0F0F0"), 4f);
            Material matRed = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Red",
                PixelartStageKit.Hex("FF3A29"), 4f);
            Material matMagenta = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Magenta",
                PixelartStageKit.Hex("C93A8F"), 4f);
            Material matOrange = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Orange",
                PixelartStageKit.Hex("E07020"), 4f);
            Material matBlue = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Blue",
                PixelartStageKit.Hex("3A6BFF"), 4f);
            Material matBody = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_BodyRed",
                PixelartStageKit.Hex("FF3A29"), 4f);
            Material matHead = PixelartStageKit.EnsureMaterial("PixelartGeoProbe_Head",
                PixelartStageKit.Hex("D4A76A"), 4f);

            // 1) 真实 Crew 预制体（标定主角：头/身 twin 就在它身上）
            GameObject crew = PixelartStageKit.PlaceCrew(turntable.transform, "GeoProbeCrew",
                new Vector3(-4.5f, 0f, 0f), 0f, matBody, matHead, LogTag);
            if (crew == null)
            {
                Debug.LogError(LogTag + " 船员预制体摆放失败，场景未烘焙。");
                return;
            }

            // 2) 高精度球（Unity 内建球，多段光滑）
            Place(turntable.transform, PrimitiveType.Sphere, "SphereFine", matYellow,
                new Vector3(-1.8f, 0.5f, 0f), Vector3.one * 1.0f);
            // 3) 立方体（平面 + 硬棱）
            Place(turntable.transform, PrimitiveType.Cube, "Cube", matWhite,
                new Vector3(0.2f, 0.5f, 0f), Vector3.one);
            // 4) Unity 圆柱（多段光滑侧面）
            Place(turntable.transform, PrimitiveType.Cylinder, "CylinderUnity", matRed,
                new Vector3(2.2f, 1.0f, 0f), new Vector3(1.0f, 1.0f, 1.0f));
            // 5) 六棱柱（面法线硬边：棱线是否触发法线内线，看这里）
            PlaceMesh(turntable.transform, NGonPrism("PrismHex", 0.5f, 1.4f, 6), matMagenta,
                new Vector3(4.4f, 0.7f, 0f));
            // 6) 正八面体（三角面拼的曲面剪影）
            PlaceMesh(turntable.transform, Octahedron("Octa", 0.7f), matOrange,
                new Vector3(6.4f, 0.7f, 0f));
            // 7) 蓝色小球（头部尺寸级：0.3m 半径 = 14.4px 直径，与头同档）
            Place(turntable.transform, PrimitiveType.Sphere, "SphereHeadSize", matBlue,
                new Vector3(8.2f, 0.3f, 0f), Vector3.one * 0.6f);

            foreach (MeshRenderer renderer in turntable.GetComponentsInChildren<MeshRenderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;

            // ---------------- 光照：与角色探针同值（Battle 正午档） ----------------
            var sunGo = new GameObject("ProbeSun");
            sunGo.transform.SetParent(root.transform);
            sunGo.transform.rotation = Quaternion.Euler(48f, 140f, 0f);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = PixelartStageKit.Hex("FFF4E0");
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.235f, 0.222f, 0.205f);
            RenderSettings.ambientEquatorColor = new Color(0.145f, 0.132f, 0.115f);
            RenderSettings.ambientGroundColor = new Color(0.075f, 0.065f, 0.050f);
            RenderSettings.ambientIntensity = 0.85f;

            // ---------------- 相机 + 像素化 rig ----------------
            Vector3 focus = new Vector3(1.85f, 1.0f, 0f);
            var camGo = new GameObject("PixelartGeoProbeCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            camGo.transform.position = focus + PixelartPilotScene.CameraDirection(
                GeometryProbeController.PitchDegrees, 0f)
                * GeometryProbeController.CameraDistance;
            camGo.transform.LookAt(focus);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = GeometryProbeController.BattleVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GreenScreen.linear;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camGo.AddComponent<AudioListener>();

            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            int baseRendererIndex = ReadRendererIndex(camera);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = GeometryProbeController.BattlePixelScale;
            rig.worldPerPixel = CrewProbeController.WorldPerPixel(
                GeometryProbeController.BattleVisibleMeters, GeometryProbeController.BattlePixelScale);
            rig.deriveOrthographicSize = true;
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;
            rig.runInEditMode = true;

            // ---------------- 控制器 ----------------
            var controllerGo = new GameObject("GeometryProbeController");
            controllerGo.transform.SetParent(root.transform);
            var controller = controllerGo.AddComponent<GeometryProbeController>();
            controller.rig = rig;
            controller.spinners = new Transform[] {
                crew.transform,
                turntable.transform.Find("SphereFine"),
                turntable.transform.Find("Cube"),
                turntable.transform.Find("CylinderUnity"),
                turntable.transform.Find("PrismHex"),
                turntable.transform.Find("Octa"),
                turntable.transform.Find("SphereHeadSize"),
            };

            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            rig.enabled = false;
            WriteRendererIndex(camera, baseRendererIndex);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(LogTag + " 几何标定场烘焙完成：" + ScenePath
                + "（转台 36 帧 × " + GeometryProbeController.AzimuthStep + "°，桥 call "
                + nameof(GeometryProbeController) + "." + nameof(GeometryProbeController.CaptureTurntableInScene) + "）");
        }

        [MenuItem("PirateCrew/像素探针/打开几何标定场（Play）")]
        public static void OpenPlay()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;   // 播放期由 CrewProbe 同款：控制器 Start 自启 rig
        }

        static void Place(Transform parent, PrimitiveType type, string name, Material material,
            Vector3 position, Vector3 scale)
        {
            GameObject go = PixelartStageKit.NewPrimitive(type, name, parent, material);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
        }

        static void PlaceMesh(Transform parent, Mesh mesh, Material material, Vector3 position)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// 正 n 棱柱：侧面**面法线硬边**（真棱柱，棱线由法线内线判定），顶底平盖。
        /// 环绕向 θ_i = 2πi/n，截面在 XZ、轴沿 Y。
        /// </summary>
        static Mesh NGonPrism(string name, float radius, float height, int sides)
        {
            float half = height * 0.5f;
            var verts = new Vector3[sides * 4 + 2];
            for (int i = 0; i < sides; i++)
            {
                float theta = 2f * Mathf.PI * i / sides;
                float x = radius * Mathf.Cos(theta);
                float z = radius * Mathf.Sin(theta);
                verts[i] = new Vector3(x, -half, z);              // 底环
                verts[sides + i] = new Vector3(x, half, z);       // 顶环
                verts[2 * sides + i] = new Vector3(x, half, z);   // 顶盖环（硬法线独立顶点）
                verts[3 * sides + i] = new Vector3(x, -half, z);  // 底盖环
            }
            verts[4 * sides] = new Vector3(0f, half, 0f);         // 顶盖心
            verts[4 * sides + 1] = new Vector3(0f, -half, 0f);    // 底盖心

            var tris = new int[sides * 12];
            int t = 0;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                int b0 = i, b1 = j, t0 = sides + i, t1 = sides + j;
                tris[t++] = b0; tris[t++] = t0; tris[t++] = t1;   // 侧面两三角（外向绕序，见文件头推导）
                tris[t++] = b0; tris[t++] = t1; tris[t++] = b1;
                int c0 = 2 * sides + i, c1 = 2 * sides + j;
                tris[t++] = 4 * sides; tris[t++] = c1; tris[t++] = c0;      // 顶盖
                int d0 = 3 * sides + i, d1 = 3 * sides + j;
                tris[t++] = 4 * sides + 1; tris[t++] = d0; tris[t++] = d1;  // 底盖
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris;
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < sides; i++)
            {
                float theta = 2f * Mathf.PI * (i + 0.5f) / sides;
                normals[i] = normals[sides + i] =
                    new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));   // 侧面法线（垂直外向）
                normals[2 * sides + i] = Vector3.up;
                normals[3 * sides + i] = Vector3.down;
            }
            normals[4 * sides] = Vector3.up;
            normals[4 * sides + 1] = Vector3.down;
            mesh.normals = normals;
            return mesh;
        }

        /// <summary>正八面体：6 顶点 8 三角面，面法线（平面拼剪影）。</summary>
        static Mesh Octahedron(string name, float radius)
        {
            Vector3 up = Vector3.up * radius, down = Vector3.down * radius;
            Vector3 xp = Vector3.right * radius, xn = Vector3.left * radius;
            Vector3 zp = Vector3.forward * radius, zn = Vector3.back * radius;

            var verts = new Vector3[6 * 8];   // 每面独立三顶点（面法线）
            var tris = new int[8 * 3];
            var normals = new Vector3[verts.Length];
            Vector3[][] faces =
            {
                new[] { up, zp, xp }, new[] { up, xp, zn }, new[] { up, zn, xn }, new[] { up, xn, zp },
                new[] { down, xp, zp }, new[] { down, zn, xp }, new[] { down, xn, zn }, new[] { down, zp, xn },
            };
            for (int f = 0; f < faces.Length; f++)
            {
                Vector3 n = Vector3.Normalize(Vector3.Cross(faces[f][1] - faces[f][0], faces[f][2] - faces[f][0]));
                for (int k = 0; k < 3; k++)
                {
                    verts[f * 3 + k] = faces[f][k];
                    normals[f * 3 + k] = n;
                }
                tris[f * 3] = f * 3; tris[f * 3 + 1] = f * 3 + 1; tris[f * 3 + 2] = f * 3 + 2;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = normals;
            return mesh;
        }

        // ---- 渲染器索引读写（与 CrewProbeSceneSetup 同法，保存前还原主相机） ----

        static int ReadRendererIndex(Camera camera)
        {
            var data = camera.GetUniversalAdditionalCameraData();
            var field = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (field?.GetValue(data)) is int index ? index : -1;
        }

        static void WriteRendererIndex(Camera camera, int index)
        {
            if (index < 0)
                return;
            var data = new SerializedObject(camera.GetUniversalAdditionalCameraData());
            data.FindProperty("m_RendererIndex").intValue = index;
            data.ApplyModifiedProperties();
        }
    }
}
