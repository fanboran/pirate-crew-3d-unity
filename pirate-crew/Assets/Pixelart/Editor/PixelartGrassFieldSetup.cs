using PirateCrew.Rendering.Pixelart;
using PirateCrew.SceneArt;
using PirateCrew.SceneArt.Showcase;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // GetUniversalAdditionalCameraData 是这里的扩展方法

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 「纯草坪验收场」装配器：一块**无地形的平地**满铺 t3ssel8r 口径草簇
    /// （<see cref="GrassPatchRules"/> 三档斑块 + 草叶法线强制朝上 + 稀有高株 accent），
    /// 烘出 `Assets/Scenes/PixelartGrassField.unity`，供播放器出图链
    /// （`-pixelartOut -pixelartLevel 6`）产出**实机管线成图**——专门回答
    /// "新草丛观感在像素路径下是什么样"，把空岛/化工厂的其它内容全部排除在外。
    ///
    /// 【为什么独立成场景】草丛的三档斑块是**世界坐标噪声**，只有在"除了草没别的"的场地上，
    /// 斑块的形状/尺度/连贯性才能被单独读出来；空岛里它们混在岩皮穹顶之间，验收会互相污染。
    ///
    /// 【口径与复用】草几何与分档全部复用空岛同款（<see cref="IslandPrimitives.AddGrassTuft"/> /
    /// <see cref="GrassPatchRules.SelectBuffer"/>，满铺循环照抄 `FloatingIslandComposer` 的草簇段）；
    /// 材质/光/相机/门禁照 `PixelartChemPlantSetup` 先例。**不接玩法数据**（无高度场/编成），
    /// 取景走 `PixelartLevelScene` 关卡 6 行。
    ///
    /// 用法（仓库根执行，一次只跑一个 Unity 进程）：
    ///   Unity.exe -batchmode -nographics -quit -projectPath pirate-crew \
    ///     -executeMethod PirateCrew.EditorTools.PixelartGrassFieldSetup.BuildAll -logFile -
    /// </summary>
    public static class PixelartGrassFieldSetup
    {
        public const string LogTag = "[PixelartGrassField]";

        /// <summary>场景名（与 `PixelartLevelScene` 表的第 6 行、`BuildScenes` 开发集同名）。</summary>
        public const string SceneName = "PixelartGrassField";

        const int LevelNumber = 6;

        /// <summary>草皮满铺区（米）：以取景表 Target 为中心的 XZ 范围——宽机位 32 m 可见高
        /// （横跨 ≈57 m）能吃到整个铺草区加边缘余量。</summary>
        const float FieldHalfX = 20f;
        const float FieldHalfZ = 14f;

        /// <summary>草簇间距（米）：0.55——r17 实拍 0.85 太稀（地面全露），加密到近满铺
        /// （约 3700 簇 / 18 万三角面，桌面量级无压力）。</summary>
        const float Spacing = 0.55f;

        /// <summary>草簇种子（确定性：同参数重跑逐顶点一致）。</summary>
        const int Seed = 20260930;

        /// <summary>网格资产目录（独立子目录，不与空岛网格混放）。</summary>
        const string MeshFolder = "Assets/Art/Models/Scene/GrassField";

        [MenuItem("PirateCrew/Pixelart/烘焙纯草坪验收场景")]
        public static void BuildAll()
        {
            if (!PixelartLevelScene.TryGet(LevelNumber, out PixelartLevelScene.View view))
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
            PixelartStageKit.EnsureFolder(MeshFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PixelartGrassField");

            // ---------------- 材质（像素路径唯一配方；色值 = 草皮三档调色板）----------------
            Material ground = PixelartStageKit.EnsureMaterial("PixelartGrassField_Ground",
                PixelartStageKit.Hex(SceneArtPalette.GrassDark), 3f, outlinePixels: 0f);
            // 草皮材质**关逐叶描边**（r17 实拍教训：0.3 m 的簇只有 2-3 艺术像素高，
            // 1 像素描边吃掉近半——近读成贴纸、远读成黑斑点噪声；t3ssel8r 原版草没有逐叶描边）。
            Material mid = PixelartStageKit.EnsureMaterial("PixelartGrassField_Mid",
                PixelartStageKit.Hex(SceneArtPalette.GrassMid), 3f, outlinePixels: 0f);
            Material light = PixelartStageKit.EnsureMaterial("PixelartGrassField_Light",
                PixelartStageKit.Hex(SceneArtPalette.GrassLight), 3f, outlinePixels: 0f);
            Material dark = PixelartStageKit.EnsureMaterial("PixelartGrassField_Dark",
                PixelartStageKit.Hex(SceneArtPalette.GrassDark), 3f, outlinePixels: 0f);
            if (ground == null || mid == null || light == null || dark == null)
            {
                Debug.LogError(LogTag + " 草皮材质没造出来（shader 缺失？）——出图上会是品红。");
                return;
            }

            // ---------------- 底板：暗绿大平面（草簇的"土"，只承接不投影）----------------
            GameObject groundMesh = PixelartStageKit.NewPrimitive(
                PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = new Vector3(view.Target.x, 0f, view.Target.z);
            groundMesh.transform.localScale = new Vector3(8f, 1f, 8f);   // Plane 10 m × 8 = 80×80

            // ---------------- 草簇满铺（三档缓冲 → 各自 1 网格 1 材质 1 DrawCall）----------------
            var buffers = new IslandBuffers();
            int tufts = FillGrass(buffers, view.Target);

            EmitBand(root.transform, "Grass_Mid", buffers.GrassMid, mid);
            EmitBand(root.transform, "Grass_Light", buffers.GrassLight, light);
            EmitBand(root.transform, "Grass_Dark", buffers.GrassDark, dark);

            // ---------------- 尺度参照：3 个船员（人的高度就是草坪的尺度锚）----------------
            Material crewRed = PixelartStageKit.CrewRed();
            Material crewBlue = PixelartStageKit.CrewBlue();
            Material crewHead = PixelartStageKit.CrewHead();
            var crewSpots = new[]
            {
                new Vector3(view.Target.x + 3f, 0f, view.Target.z + 5f),
                new Vector3(view.Target.x - 8f, 0f, view.Target.z - 2f),
                new Vector3(view.Target.x + 14f, 0f, view.Target.z - 6f),
            };
            for (int i = 0; i < crewSpots.Length; i++)
            {
                Material body = i % 2 == 0 ? crewBlue : crewRed;
                PixelartStageKit.PlaceCrew(root.transform, "Crew" + i, crewSpots[i], 40f * i + 20f,
                    body, crewHead, LogTag);
            }

            // ---------------- 光 / 相机（俯角 30° = 规则像素阶梯，口径见 PixelartLevelScene）----------------
            // 投影开（r16 口径）：草簇贴地投影是"草长在地上"的一部分。
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform, castShadows: true);

            var camGo = new GameObject("PixelartLevelCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = view.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartLevelScene.AzimuthFor(view));
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

            // 物体 pass 按层拉全部不透明物体、不按 shader 过滤：混进一个旧 shader 的 renderer
            // 就是"那片像素花屏、一行报错都没有"（口径见 PixelartStageKit 类头）。
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            string scenePath = "Assets/Scenes/" + SceneName + ".unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            PixelartStageKit.RegisterScene(scenePath, LogTag);

            Debug.Log(LogTag + " 场景完成：" + scenePath + "（草簇 " + tufts + " 簇 / 间距 " + Spacing
                + " m；Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；放大倍数 " + PixelartPilotScene.PixelScale + "×；俯角 "
                + PixelartPilotScene.PitchDegrees + "°）。出图：播放器 -pixelartOut <目录> -pixelartLevel "
                + LevelNumber);
        }

        /// <summary>
        /// 满铺草簇（照抄 `FloatingIslandComposer` 草簇段的三件套：
        /// <see cref="GrassPatchRules.SelectBuffer"/> 选档 + 尺寸哈希 + 6% 高株 accent）。
        /// 返回簇数。网格抖动 ±0.3 m 打散排布感。
        /// </summary>
        static int FillGrass(IslandBuffers buffers, Vector3 center)
        {
            int count = 0;
            int i = 0;
            for (float x = center.x - FieldHalfX; x <= center.x + FieldHalfX; x += Spacing)
            {
                for (float z = center.z - FieldHalfZ; z <= center.z + FieldHalfZ; z += Spacing)
                {
                    float jx = (SceneArtHash.Hash01(Seed, i, 3) - 0.5f) * 0.6f;
                    float jz = (SceneArtHash.Hash01(Seed, i, 5) - 0.5f) * 0.6f;
                    var p = new Vector3(x + jx, 0f, z + jz);

                    float scale = 0.26f + 0.22f * SceneArtHash.Hash01(Seed, i, 7);
                    // 稀有高株 accent（原版 _AccentFrequency/_AccentHeight 口径）：6% 概率高出一截。
                    if (SceneArtHash.Hash01(Seed, i, 1021) < 0.06f)
                        scale *= 1.45f;

                    IslandPrimitives.AddGrassTuft(GrassPatchRules.SelectBuffer(buffers, p), p, scale,
                        Seed + i * 13, 4 + (int)(SceneArtHash.Hash01(Seed, i, 1009) * 3f));
                    count++;
                    i++;
                }
            }
            return count;
        }

        /// <summary>一档缓冲 → 网格资产（就地覆写，GUID 稳定）→ 命名子物体 + 渲染器。</summary>
        static void EmitBand(Transform parent, string objectName, MeshBuffers source, Material material)
        {
            if (source == null || source.IsEmpty)
            {
                Debug.LogWarning(LogTag + " 档位 " + objectName + " 缓冲为空（该档斑块没铺到？核对阈值）。");
                return;
            }

            Mesh mesh = EnsureMeshAsset(MeshFolder + "/" + objectName + ".asset", source);

            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>网格资产就地覆写（模式照抄 SceneArtBaker.EnsureMeshAsset：重写顶点保持 GUID 稳定）。</summary>
        static Mesh EnsureMeshAsset(string path, MeshBuffers source)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.Clear(false);
            }

            mesh.SetVertices(new System.Collections.Generic.List<Vector3>(source.ToVertices()));
            mesh.SetNormals(new System.Collections.Generic.List<Vector3>(source.ToNormals()));
            mesh.SetTriangles(new System.Collections.Generic.List<int>(source.ToTriangles()), 0, true);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
