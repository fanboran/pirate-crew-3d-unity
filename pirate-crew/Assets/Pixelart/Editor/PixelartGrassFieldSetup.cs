using System.Collections.Generic;
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
    /// 「纯草坪验收场」装配器——**t3ssel8r 草坪的全量模仿**（第三轮，r19）：
    /// 草不再是几何叶片，而是**像素画剪影 sprite 贴在相机朝向的四边形上**
    /// （`PixelartMaterialFactory.CreateSprite`：`_BaseMap` 只管形状、`_BaseColor` 调色板只管颜色、
    /// `_SPRITE` 关键字 alpha 裁切——与参考库 GrassBlade.shader 同构；本仓相机固定等距，
    /// 四边形在**烘焙期**就朝向相机，不需要运行时 billboard）。
    /// 三档斑块（<see cref="GrassPatchRules"/>）与 6% 高株 accent（图集上半窗）沿用。
    ///
    /// 【为什么叶形必须是画的】几何叶片（AddLeaf 单段折面）在 2-3 艺术像素的高度下只能读成
    /// "长方形板"（r17/r18 实拍裁决）；sprite 的锯齿尖叶是逐纹素画出来的，密度对齐艺术像素。
    ///
    /// 【遮罩图集】烘焙期程序化点阵绘制（确定性）：16×32，下半 = 常规簇（三尖叶束），
    /// 上半 = 高株 accent（更高更瘦）。导入口径 Point/Clamp/无 mip/不压缩。
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

        /// <summary>草皮满铺区（米）：以取景表 Target 为中心的 XZ 范围。</summary>
        const float FieldHalfX = 20f;
        const float FieldHalfZ = 14f;

        /// <summary>草簇间距（米）：0.75——quad 加大到 0.9 后放疏（约 2000 簇，近满铺不互相盖死）。</summary>
        const float Spacing = 0.75f;

        /// <summary>草簇种子（确定性：同参数重跑逐顶点一致）。</summary>
        const int Seed = 20260930;

        /// <summary>sprite 四边形边长（米）：0.9——**纹素密度判据**「1 纹素 ≈ 1 艺术像素」：
        /// 16 纹素 × 5.6cm ≈ 宽机位 11 艺术像素高（r19 教训：0.45m 时 1 艺术像素盖 3 纹素，
        /// 点采样把细叶尖全跳掉，sprite 被降采样成 V 字碎片）。</summary>
        const float QuadSize = 0.9f;

        /// <summary>稀有高株 accent 概率（原版 _AccentFrequency 口径）。</summary>
        const float AccentFrequency = 0.06f;

        const string MeshFolder = "Assets/Art/Models/Scene/GrassField";
        const string SpriteFolder = "Assets/Pixelart/Textures/GrassTuft";

        /// <summary>遮罩图集：16 宽 × 32 高。下半（uv.y 0-0.5）= 常规簇；上半（0.5-1）= 高株 accent。</summary>
        const int AtlasW = 16;
        const int AtlasH = 32;

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
            PixelartStageKit.EnsureFolder(SpriteFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PixelartGrassField");

            // ---------------- 遮罩图集（程序化点阵，确定性）----------------
            Texture2D atlas = EnsureTuftAtlas();

            // ---------------- 材质（遮罩只管形状，颜色 = 草皮三档调色板）----------------
            Material ground = PixelartStageKit.EnsureMaterial("PixelartGrassField_Ground",
                PixelartStageKit.Hex(SceneArtPalette.GrassDark), 3f, outlinePixels: 0f);
            Material mid = EnsureSpriteMaterial("PixelartGrassField_Mid",
                PixelartStageKit.Hex(SceneArtPalette.GrassMid), atlas);
            Material light = EnsureSpriteMaterial("PixelartGrassField_Light",
                PixelartStageKit.Hex(SceneArtPalette.GrassLight), atlas);
            Material dark = EnsureSpriteMaterial("PixelartGrassField_Dark",
                PixelartStageKit.Hex(SceneArtPalette.GrassDark), atlas);
            if (ground == null || mid == null || light == null || dark == null)
            {
                Debug.LogError(LogTag + " 草皮材质没造出来（shader 缺失？）——出图上会是品红。");
                return;
            }

            // ---------------- 底板：暗绿大平面（草的"土"，只承接不投影）----------------
            GameObject groundMesh = PixelartStageKit.NewPrimitive(
                PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = new Vector3(view.Target.x, 0f, view.Target.z);
            groundMesh.transform.localScale = new Vector3(8f, 1f, 8f);   // Plane 10 m × 8 = 80×80

            // ---------------- 草簇满铺（sprite 四边形，三档缓冲 → 各 1 网格 1 材质）----------------
            Vector3 camForward = -PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartLevelScene.AzimuthFor(view)).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, camForward).normalized;
            Vector3 camUp = Vector3.Cross(camForward, camRight).normalized;

            Dictionary<IslandMaterial, SpriteQuadBatch> batches = FillGrass(view.Target, camRight, camUp);
            EmitBand(root.transform, "Grass_Mid", batches[IslandMaterial.GrassMid], mid);
            EmitBand(root.transform, "Grass_Light", batches[IslandMaterial.GrassLight], light);
            EmitBand(root.transform, "Grass_Dark", batches[IslandMaterial.GrassDark], dark);

            // ---------------- 尺度参照：3 个船员 ----------------
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

            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            string scenePath = "Assets/Scenes/" + SceneName + ".unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            PixelartStageKit.RegisterScene(scenePath, LogTag);

            Debug.Log(LogTag + " 场景完成（sprite 版）：Assets/Scenes/" + SceneName + ".unity。出图：播放器 "
                + "-pixelartOut <目录> -pixelartLevel " + LevelNumber);
        }

        // ------------------------------------------------------------------
        // 遮罩图集（程序化点阵：尖叶逐纹素走列，t3ssel8r grassleaf 的读法）
        // ------------------------------------------------------------------

        /// <summary>图集就地覆写（GUID 稳定）：16×32，下半常规簇、上半高株 accent。</summary>
        static Texture2D EnsureTuftAtlas()
        {
            string path = SpriteFolder + "/GrassTuftMask.png";
            var px = new Color32[AtlasW * AtlasH];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(0, 0, 0, 0);

            // 常规簇（下半，原点 y=0）：中央高叶 + 两侧外撇 + 两根补空，5 叶束。
            // 叶基 3 纹素（≈2 艺术像素宽的笔触，quad 0.9m 口径下点采样不丢列）。
            DrawBlade(px, 0, 0, baseX: 7, height: 13, lean: 0, width: 3);
            DrawBlade(px, 0, 0, baseX: 4, height: 9, lean: -3, width: 3);
            DrawBlade(px, 0, 0, baseX: 10, height: 10, lean: 3, width: 3);
            DrawBlade(px, 0, 0, baseX: 6, height: 7, lean: -1, width: 2);
            DrawBlade(px, 0, 0, baseX: 9, height: 8, lean: 1, width: 2);

            // 高株 accent（上半，原点 y=16）：更高更瘦的三叶。
            DrawBlade(px, 0, 16, baseX: 8, height: 15, lean: 0, width: 3);
            DrawBlade(px, 0, 16, baseX: 5, height: 12, lean: -3, width: 2);
            DrawBlade(px, 0, 16, baseX: 10, height: 12, lean: 3, width: 2);

            var texture = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, false)
            {
                name = "GrassTuftMask",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(px);
            texture.Apply();

            // PNG 文件覆写 + 重新导入（**不走** DeleteAsset→CreateAsset：两者相邻调用在
            // batchmode 的导入队列里有竞态——Delete 未落地 Create 就报"路径占用"抛
            // UnityException，实测 PixelartScenesRebuild 首跑即中断草场装配；文件覆写
            // 没有这步竞态，点阵确定性重生成语义不变）。File IO 走绝对路径（batchmode 铁律）。
            string absPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", path));
            System.IO.File.WriteAllBytes(absPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                // 像素 mask 任何过滤/压缩都会糊边：Point / Clamp / 无 mip / 不压缩。
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>一根尖叶：从底往上逐行走，二次曲线外撇（叶中段打折的读法），半高以上收成 1 纹素尖。</summary>
        static void DrawBlade(Color32[] px, int ox, int oy, int baseX, int height, int lean, int width)
        {
            for (int i = 0; i < height; i++)
            {
                float t = i / (float)height;
                int x = baseX + Mathf.RoundToInt(lean * t * t);
                int w = i < height * 0.5f ? width : 1;
                for (int dx = 0; dx < w; dx++)
                    SetPx(px, ox + x + dx, oy + i);
            }
        }

        static void SetPx(Color32[] px, int x, int y)
        {
            if (x < 0 || x >= AtlasW || y < 0 || y >= AtlasH)
                return;
            px[y * AtlasW + x] = new Color32(255, 255, 255, 255);
        }

        // ------------------------------------------------------------------
        // sprite 材质（资产就地覆写：r17/r18 已有同名材质，重烘补上关键字与遮罩）
        // ------------------------------------------------------------------

        static Material EnsureSpriteMaterial(string name, Color albedo, Texture2D atlas)
        {
            string path = PixelartStageKit.MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = PixelartMaterialFactory.CreateSprite(name, albedo, atlas);
                if (material != null)
                    AssetDatabase.CreateAsset(material, path);
                return material;
            }

            // 已存在（前几轮的纯色版）：就地补 sprite 配方（幂等）。
            PixelartMaterialFactory.Configure(material, albedo, 3f, outlinePixels: 0f);
            material.EnableKeyword("_SPRITE");
            material.SetTexture("_BaseMap", atlas);
            material.SetFloat("_Cutoff", 0.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------
        // 草簇满铺（sprite 四边形；档位/密度/accent 与 r18 同参）
        // ------------------------------------------------------------------

        /// <summary>一档草皮的全部四边形（世界空间已展开；法线一律朝上——
        /// quad 是单一平面 ⇒ 整簇一个面法线 ⇒ 整簇同光照档，"地形法线着色"自动成立）。</summary>
        class SpriteQuadBatch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> UVs = new List<Vector2>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
        }

        /// <summary>满铺：每簇一个相机朝向四边形（烘焙期定向——本仓相机固定等距，无需运行时 billboard）。
        /// 返回 档位 → 批次。accent 簇用图集上半窗（uv.y 0.5-1）。</summary>
        static Dictionary<IslandMaterial, SpriteQuadBatch> FillGrass(Vector3 center,
            Vector3 camRight, Vector3 camUp)
        {
            var batches = new Dictionary<IslandMaterial, SpriteQuadBatch>
            {
                [IslandMaterial.GrassMid] = new SpriteQuadBatch(),
                [IslandMaterial.GrassLight] = new SpriteQuadBatch(),
                [IslandMaterial.GrassDark] = new SpriteQuadBatch(),
            };

            int i = 0;
            for (float x = center.x - FieldHalfX; x <= center.x + FieldHalfX; x += Spacing)
            {
                for (float z = center.z - FieldHalfZ; z <= center.z + FieldHalfZ; z += Spacing)
                {
                    float jx = (SceneArtHash.Hash01(Seed, i, 3) - 0.5f) * 0.6f;
                    float jz = (SceneArtHash.Hash01(Seed, i, 5) - 0.5f) * 0.6f;
                    var p = new Vector3(x + jx, 0f, z + jz);

                    bool accent = SceneArtHash.Hash01(Seed, i, 1021) < AccentFrequency;
                    float uvY0 = accent ? 0.5f : 0f;
                    float uvY1 = accent ? 1f : 0.5f;
                    float size = QuadSize * (accent ? 1.15f : 1f);

                    AddQuad(batches[IslandBuffersBand(p)], p, size, uvY0, uvY1, camRight, camUp);
                    i++;
                }
            }
            return batches;
        }

        static IslandMaterial IslandBuffersBand(Vector3 p)
        {
            switch (GrassPatchRules.SelectBand(p))
            {
                case 1: return IslandMaterial.GrassLight;
                case 2: return IslandMaterial.GrassDark;
                default: return IslandMaterial.GrassMid;
            }
        }

        /// <summary>一个四边形：底边中点 = <paramref name="basePos"/>，沿相机右/上轴展开；
        /// 双面（正反绕序各一遍——剪影叶不该被背面剔除吃掉）。</summary>
        static void AddQuad(SpriteQuadBatch batch, Vector3 basePos, float size,
            float uvY0, float uvY1, Vector3 camRight, Vector3 camUp)
        {
            float half = size * 0.5f;
            Vector3 bl = basePos - camRight * half;
            Vector3 br = basePos + camRight * half;
            Vector3 tl = bl + camUp * size;
            Vector3 tr = br + camUp * size;
            var up = Vector3.up;

            int v = batch.Vertices.Count;
            batch.Vertices.Add(bl);
            batch.Vertices.Add(br);
            batch.Vertices.Add(tl);
            batch.Vertices.Add(tr);
            batch.UVs.Add(new Vector2(0f, uvY0));
            batch.UVs.Add(new Vector2(1f, uvY0));
            batch.UVs.Add(new Vector2(0f, uvY1));
            batch.UVs.Add(new Vector2(1f, uvY1));
            for (int k = 0; k < 4; k++)
                batch.Normals.Add(up);

            batch.Triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });       // 正面
            batch.Triangles.AddRange(new[] { v + 2, v + 1, v, v + 2, v + 3, v + 1 });       // 反面
        }

        /// <summary>一批四边形 → 网格资产（含 UV 通道）→ 命名子物体 + 渲染器。</summary>
        static void EmitBand(Transform parent, string objectName, SpriteQuadBatch batch, Material material)
        {
            if (batch == null || batch.Triangles.Count == 0)
            {
                Debug.LogWarning(LogTag + " 档位 " + objectName + " 批次为空（该档斑块没铺到？核对阈值）。");
                return;
            }

            string path = MeshFolder + "/" + objectName + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = objectName };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.Clear(false);
            }

            mesh.SetVertices(batch.Vertices);
            mesh.SetUVs(0, batch.UVs);
            mesh.SetNormals(batch.Normals);
            mesh.SetTriangles(batch.Triangles, 0, true);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
