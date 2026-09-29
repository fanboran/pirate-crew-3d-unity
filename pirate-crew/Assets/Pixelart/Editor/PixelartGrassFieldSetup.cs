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
        /// <summary>草簇间距（米）：0.32——**草皮尺度**：参考帧 035 里草簇只有石块的 1/4 高
        /// （≈0.15-0.3 m 的矮草，密到融成一张面），r20/r21 的 0.9 m 是 3-6 倍失真（麦田）。
        /// 约 1.1 万簇 × 8 三角面 ≈ 9 万面，桌面量级。</summary>
        const float Spacing = 0.32f;

        /// <summary>草簇种子（确定性：同参数重跑逐顶点一致）。</summary>
        const int Seed = 20260930;

        /// <summary>sprite 四边形边长（米）：0.3——矮草皮（16 纹素 × 1.9cm）。
        /// 【判据修订】「1 纹素 ≈ 1 艺术像素」只在**单一机位**成立；多机位下矮草在宽机位
        /// 本来就该读成斑驳表面（参考帧 035 正是如此——单簇不可辨、只剩色调纹理），
        /// r17 的"V 字碎片"病根是**高对比描边**不是密度。</summary>
        const float QuadSize = 0.3f;

        /// <summary>稀有高株 accent 概率：参考里路径边零星的高草（3%）。</summary>
        const float AccentFrequency = 0.03f;

        const string MeshFolder = "Assets/Art/Models/Scene/GrassField";
        const string SpriteFolder = "Assets/Pixelart/Textures/GrassTuft";

        /// <summary>常规簇变体数 / 高株 accent 变体数（图集横排，每格 16×16）。</summary>
        const int Variants = 4;
        const int AccentVariants = 3;

        /// <summary>遮罩图集：64 宽 × 32 高。下行（uv.y 0-0.5）= 4 种常规簇；
        /// 上行（0.5-1）= 3 种高株 accent。逐簇随机选格 + 随机水平镜像。</summary>
        const int AtlasW = 64;
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

            // ---------------- 材质（遮罩只管形状，颜色 = 草皮三档 × 逐簇微 tint）----------------
            // 【对齐参考 r23】创始人判 r22「太显眼」：簇与地、簇与簇的色差全部压进 ±8% 邻近域——
            // 参考的观感是"融进地面的细腻色纹"，不是"簇形拼贴"。多样性由**逐簇随机微 tint**
            // （t3ssel8r 原话 "a randomized color"：×0.92 / ×1.0 / ×1.08，哈希逐簇随机）
            // 叠加空间斑块（GrassPatchRules）共同承担。3 档 × 3 tint = 9 材质 / 9 DrawCall。
            // 【提案】色值系 #6FB86A 邻近域派生，进岛前过创始人配色裁决。
            Material ground = PixelartStageKit.EnsureMaterial("PixelartGrassField_Ground",
                PixelartStageKit.Hex("5C9556"), 3f, outlinePixels: 0f);

            var bandBases = new[]
            {
                (IslandMaterial.GrassMid, PixelartStageKit.Hex("6FB86A")),
                (IslandMaterial.GrassLight, PixelartStageKit.Hex("77BB70")),
                (IslandMaterial.GrassDark, PixelartStageKit.Hex("639E5F")),
            };
            var materials = new Dictionary<(IslandMaterial, int), Material>();
            bool missing = ground == null;
            foreach (var (band, baseColor) in bandBases)
            {
                for (int tint = 0; tint < TintLevels.Length; tint++)
                {
                    var m = EnsureSpriteMaterial(
                        "PixelartGrassField_" + band + "_T" + tint,
                        Tint(baseColor, TintLevels[tint]), atlas);
                    materials[(band, tint)] = m;
                    missing |= m == null;
                }
            }
            if (missing)
            {
                Debug.LogError(LogTag + " 草皮材质没造出来（shader 缺失？）——出图上会是品红。");
                return;
            }

            // ---------------- 底板：草色邻近域的大平面（草的"土"，只承接不投影）----------------
            GameObject groundMesh = PixelartStageKit.NewPrimitive(
                PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = new Vector3(view.Target.x, 0f, view.Target.z);
            groundMesh.transform.localScale = new Vector3(8f, 1f, 8f);   // Plane 10 m × 8 = 80×80

            // ---------------- 草簇满铺（sprite 四边形，档×tint 各 1 网格 1 材质）----------------
            Vector3 camForward = -PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartLevelScene.AzimuthFor(view)).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, camForward).normalized;
            Vector3 camUp = Vector3.Cross(camForward, camRight).normalized;

            var batches = FillGrass(view.Target, camRight, camUp);
            foreach (var kvp in batches)
                EmitBand(root.transform,
                    "Grass_" + kvp.Key.Item1 + "_T" + kvp.Key.Item2, kvp.Value, materials[kvp.Key]);

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

            // 常规簇 4 变体（下行，每格 16×16）：叶数/高矮/撇向各异——逐簇随机选格，
            // 叠加随机水平镜像 ⇒ 4 变体 × 2 镜像 × 3 档色 = 24 种组合，治"个个一模一样"。
            DrawTuft(px, 0, 0, new[] { (7, 13, 0, 3), (4, 9, -3, 2), (10, 10, 3, 2), (6, 7, -1, 1), (9, 8, 1, 1) });
            DrawTuft(px, 16, 0, new[] { (8, 11, 0, 2), (5, 8, -2, 2), (11, 9, 2, 2) });
            DrawTuft(px, 32, 0, new[] { (6, 12, -1, 2), (9, 12, 1, 2), (3, 7, -3, 2), (12, 8, 3, 2), (8, 6, 0, 1) });
            DrawTuft(px, 48, 0, new[] { (8, 14, 0, 2), (5, 10, -2, 1), (11, 11, 2, 1), (8, 7, 0, 1) });

            // 高株 accent 3 变体（上行 y=16，更高更瘦，占前 3 格）。
            DrawTuft(px, 0, 16, new[] { (8, 15, 0, 2), (5, 12, -3, 1), (10, 12, 3, 1) });
            DrawTuft(px, 16, 16, new[] { (7, 15, -1, 2), (10, 13, 2, 1) });
            DrawTuft(px, 32, 16, new[] { (8, 15, 0, 1), (6, 13, -2, 1), (11, 13, 2, 1), (8, 10, 0, 1) });

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

        /// <summary>逐簇微 tint 档（×0.92 / ×1.0 / ×1.08）——t3ssel8r "a randomized color"
        /// 的落点：多样性主要来自逐簇随机色，不是 sprite 数量。</summary>
        static readonly float[] TintLevels = { 0.92f, 1f, 1.08f };

        /// <summary>邻域 tint：RGB 同乘（γ 空间近似，±8% 内的邻近派生色）。</summary>
        static Color Tint(Color c, float k)
        {
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k),
                Mathf.Clamp01(c.b * k), c.a);
        }

        /// <summary>一簇 = 若干叶（局部格内 16×16 点阵）：(baseX, height, lean, width) 列表。</summary>
        static void DrawTuft(Color32[] px, int ox, int oy, (int baseX, int height, int lean, int width)[] blades)
        {
            foreach (var b in blades)
                DrawBlade(px, ox, oy, b.baseX, b.height, b.lean, b.width);
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
        /// 返回 (档位, tint 档) → 批次。accent 簇用图集上行（uv.y 0.5-1）。</summary>
        static Dictionary<(IslandMaterial, int), SpriteQuadBatch> FillGrass(Vector3 center,
            Vector3 camRight, Vector3 camUp)
        {
            var batches = new Dictionary<(IslandMaterial, int), SpriteQuadBatch>();

            int i = 0;
            for (float x = center.x - FieldHalfX; x <= center.x + FieldHalfX; x += Spacing)
            {
                for (float z = center.z - FieldHalfZ; z <= center.z + FieldHalfZ; z += Spacing)
                {
                    float jx = (SceneArtHash.Hash01(Seed, i, 3) - 0.5f) * 0.6f;
                    float jz = (SceneArtHash.Hash01(Seed, i, 5) - 0.5f) * 0.6f;
                    var p = new Vector3(x + jx, 0f, z + jz);

                    bool accent = SceneArtHash.Hash01(Seed, i, 1021) < AccentFrequency;
                    int variants = accent ? AccentVariants : Variants;
                    int variant = Mathf.Min((int)(SceneArtHash.Hash01(Seed, i, 101) * variants), variants - 1);
                    bool mirror = SceneArtHash.Hash01(Seed, i, 103) < 0.5f;
                    int tint = Mathf.Min((int)(SceneArtHash.Hash01(Seed, i, 107) * TintLevels.Length),
                        TintLevels.Length - 1);
                    float cell = 16f / AtlasW;            // 一格宽（uv.x，图集横排每格 16 纹素）
                    float uvX0 = variant * cell;
                    float uvX1 = uvX0 + cell;
                    float uvY0 = accent ? 0.5f : 0f;
                    float uvY1 = accent ? 1f : 0.5f;
                    float size = QuadSize * (accent ? 2f : 1f);

                    var key = (IslandBuffersBand(p), tint);
                    if (!batches.TryGetValue(key, out var batch))
                        batches[key] = batch = new SpriteQuadBatch();

                    AddQuad(batch, p, size, uvX0, uvX1, uvY0, uvY1, mirror, camRight, camUp);
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
        /// UV 取图集一格（<paramref name="uvX0"/>-<paramref name="uvX1"/> ×
        /// <paramref name="uvY0"/>-<paramref name="uvY1"/>），<paramref name="mirror"/> = 水平镜像；
        /// 双面（正反绕序各一遍——剪影叶不该被背面剔除吃掉）。</summary>
        static void AddQuad(SpriteQuadBatch batch, Vector3 basePos, float size,
            float uvX0, float uvX1, float uvY0, float uvY1, bool mirror,
            Vector3 camRight, Vector3 camUp)
        {
            float half = size * 0.5f;
            Vector3 bl = basePos - camRight * half;
            Vector3 br = basePos + camRight * half;
            Vector3 tl = bl + camUp * size;
            Vector3 tr = br + camUp * size;
            var up = Vector3.up;
            float uL = mirror ? uvX1 : uvX0;
            float uR = mirror ? uvX0 : uvX1;

            int v = batch.Vertices.Count;
            batch.Vertices.Add(bl);
            batch.Vertices.Add(br);
            batch.Vertices.Add(tl);
            batch.Vertices.Add(tr);
            batch.UVs.Add(new Vector2(uL, uvY0));
            batch.UVs.Add(new Vector2(uR, uvY0));
            batch.UVs.Add(new Vector2(uL, uvY1));
            batch.UVs.Add(new Vector2(uR, uvY1));
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
