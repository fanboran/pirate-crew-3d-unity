using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.SceneArt;
using PirateCrew.SceneArt.Showcase;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 空岛展示件的**编辑器装配 + 菜单**：把 <see cref="FloatingIslandComposer.Compose"/>
    /// 产出的纯 C# 三角面缓冲（<see cref="IslandBuffers"/>，15 个材质槽）落成场景里的 GameObject
    /// —— 每槽 1 网格资产 + 1 材质资产 + 1 MeshRenderer = 1 DrawCall，整座岛约 2 万面只占 15 个 DrawCall。
    ///
    /// 【做什么 / 不做什么】本文件只做「装配 + 资产落盘」（同 <see cref="SceneArtBuilder"/> 的分工）：
    ///   · 几何来自纯 C# 的 <see cref="FloatingIslandComposer"/>（无头可测，这里只消费）；
    ///   · 路径 / 命名 / 摆放位 / 投影口径来自 <see cref="FloatingIslandScenePlan"/>（无头可测的唯一真源）；
    ///   · 颜色 / 参数来自 <see cref="IslandMaterialCatalog"/> 的配方表；
    ///   · 本文件不含可测逻辑，也不碰 RenderSettings / 光照（那是 BattleSceneLighting 的域）。
    ///
    /// 【质量红线（工程血泪教训）】**每个 MeshRenderer 必须显式绑定材质**——
    /// 材质为空的 renderer 在播放器构建里是洋红炸弹。本类的 EnsureMaterial 走三级回落链
    /// （PirateSurface → URP/Lit → Standard），任何一档都保证返回非空材质；回落时记 Warning 不静默。
    /// 另一条隐形红线：<c>PirateCrew/Fx/Additive</c> 的片元是 <c>tex × _Color × 顶点色</c>，
    /// 而合并网格默认没有 COLOR 通道（D3D11 上默认值不可依赖）——因此晶体/暖光两槽的网格
    /// **必须显式写入顶点色**（<see cref="IslandMaterialRecipe.VertexTint"/>），否则发光件整体变黑
    /// 或直接不可见（加法混合下黑色 = 无贡献）。
    ///
    /// 【可静态复用】<see cref="PlaceDefault"/> / <see cref="Place"/> / <see cref="Remove"/>
    /// / <see cref="FindIslandRoot"/> 是公共静态 API，供 ArtGate 等烘焙流程直接调用
    /// （接线说明：ArtGate 在第 ⑦ 步 M2BattleSceneSetup.BuildAll **之后**插一步
    /// 「FloatingIslandShowcaseMenu.PlaceDefault()」即可把空岛烘进 Battle.unity）。
    /// </summary>
    public static class FloatingIslandShowcaseMenu
    {
        // ------------------------------------------------------------------
        // shader 名（与 SceneArtBuilder 同源；回落链见 EnsureMaterial）
        // ------------------------------------------------------------------

        const string SurfaceShaderName = "PirateCrew/PirateSurface";
        const string AdditiveShaderName = "PirateCrew/Fx/Additive";
        const string UnlitShaderName = "Universal Render Pipeline/Unlit";
        const string LitFallbackShaderName = "Universal Render Pipeline/Lit";

        // ------------------------------------------------------------------
        // 菜单
        // ------------------------------------------------------------------

        const string PlaceMenuPath = "PirateCrew/Showcase/摆放超美空岛";
        const string RemoveMenuPath = "PirateCrew/Showcase/移除空岛";
        const string FrameMenuPath = "PirateCrew/Showcase/对准空岛取景";

        /// <summary>在当前打开的场景里摆放空岛（自动选位 + 自动高度），并把 Scene 视图对准它。</summary>
        [MenuItem(PlaceMenuPath, false, 0)]
        public static void PlaceMenuAction()
        {
            PlaceDefault();
            FrameSceneViewOnIsland();
        }

        /// <summary>移除当前场景里的空岛（只删展示件本体，不删网格/材质资产）。</summary>
        [MenuItem(RemoveMenuPath, false, 20)]
        public static void RemoveMenuAction()
        {
            if (Remove())
                Debug.Log("[FloatingIslandShowcase] 已移除空岛（网格/材质资产保留，重摆时幂等复用）。");
            else
                Debug.Log("[FloatingIslandShowcase] 当前场景没有空岛。");
        }

        /// <summary>把 Scene 视图对准空岛（评审出图前的取景辅助）。</summary>
        [MenuItem(FrameMenuPath, false, 21)]
        public static void FrameMenuAction()
        {
            FrameSceneViewOnIsland();
        }

        // ------------------------------------------------------------------
        // 公共静态 API（供其它 Editor 流程 / ArtGate 调用）
        // ------------------------------------------------------------------

        /// <summary>按交付构图（<see cref="FloatingIslandSpec.Default"/>）在当前场景摆放空岛。</summary>
        public static GameObject PlaceDefault()
        {
            return Place(FloatingIslandSpec.Default);
        }

        /// <summary>
        /// 【第 3 关可玩地面】把空岛摆进 Battle 场景**场心居中位**并存盘（batchmode 可调：
        /// <c>-executeMethod PirateCrew.EditorTools.FloatingIslandShowcaseMenu.PlaceIntoBattleCenter</c>）。
        /// 草皮站位面 ≈ y14，与 ShowcaseLevels 关卡 3 的逻辑高度场（28 块 × 0.5）对齐；
        /// 运行时由 RuntimeSceneArt 按关卡号开关（只有第 3 关激活）。
        /// 必须在 ArtGate ⑦（M2BattleSceneSetup 重建 Battle 场景）**之后**跑，否则会被洗掉。
        /// </summary>
        [MenuItem("PirateCrew/Showcase/摆进战斗场景居中（第3关地面）", false, 5)]
        public static void PlaceIntoBattleCenter()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/Battle.unity", OpenSceneMode.Single);

            GameObject root = Place(FloatingIslandSpec.Default);
            root.transform.position = new Vector3(
                LevelGeometry.TileToWorld(10f),
                13.3f,
                LevelGeometry.TileToWorld(7.5f));

            // 连线：Battle 场景的 RuntimeSceneArt 持有空岛根引用（运行时按关卡号开关可见性）。
            var sceneArt = Object.FindObjectOfType<RuntimeSceneArt>();
            if (sceneArt != null)
            {
                var so = new SerializedObject(sceneArt);
                so.FindProperty("skyIslandRoot").objectReferenceValue = root;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(sceneArt);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[FloatingIslandShowcase] 已摆进 Battle 场景场心并保存：" + root.transform.position);
        }

        /// <summary>
        /// 在当前打开的场景里摆放一座空岛（幂等：先清掉上次摆放，资产就地覆写）。
        /// 返回空岛根节点（调用方可继续接线 / 取包围盒）。
        /// </summary>
        public static GameObject Place(FloatingIslandSpec spec)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            // 1. 纯 C# 构图 → 分材质缓冲（同 seed 必得同一座岛；这一步无 Unity 对象）。
            var buffers = new IslandBuffers();
            FloatingIslandStats stats = FloatingIslandComposer.Compose(buffers, spec);

            EnsureFolder(FloatingIslandScenePlan.MaterialFolder);
            EnsureFolder(FloatingIslandScenePlan.MeshFolder);

            // 2. 幂等重建：先删上一座（资产不删，下方 Ensure 系列就地覆写）。
            Remove();

            // 3. 组装层级：根节点按计划位（远缘外侧背景位 + PlacementHeight 净空）。
            Vector3 rootPosition = FloatingIslandScenePlan.DefaultBackdropRootPosition(spec);
            var root = new GameObject(FloatingIslandScenePlan.RootName);
            Undo.RegisterCreatedObjectUndo(root, "摆放超美空岛");
            root.transform.position = rootPosition;
            root.isStatic = true;   // 纯静态展示件：静态批合 + 可参与光照烘焙。

            int groups = 0;
            var all = IslandMaterialCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                IslandMaterial slot = all[i];
                MeshBuffers source = buffers.Get(slot);
                if (source.IsEmpty)
                    continue;

                Material material = EnsureMaterial(slot);
                Mesh mesh = EnsureMeshAsset(slot, source, IslandMaterialCatalog.For(slot));

                var child = new GameObject(FloatingIslandScenePlan.GroupName(slot));
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;
                child.isStatic = true;

                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                // 【红线】renderer 必须拿到非空材质：EnsureMaterial 三级回落链保证非空，
                // 这里再断言一次，真为 null 时宁可跳过该槽也不留洋红 renderer。
                if (material == null)
                {
                    Debug.LogError("[FloatingIslandShowcase] 材质 " + slot + " 三级回落链全部落空"
                        + "（连 Standard 都找不到，正常编辑器会话不可能发生），跳过该槽。");
                    Object.DestroyImmediate(child);
                    continue;
                }

                var meshRenderer = child.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                bool castShadows = IslandMaterialCatalog.For(slot).CastShadows;
                meshRenderer.shadowCastingMode = castShadows
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
                meshRenderer.receiveShadows = FloatingIslandScenePlan.ReceivesShadows(slot);
                meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;

                groups++;
            }

            // ---- 可玩地面：顶面碰撞代理（无 renderer，只有 MeshCollider；凹面/静态）----
            var collisionSource = new MeshBuffers();
            FloatingIslandComposer.BuildCollisionSurface(collisionSource, spec);
            Mesh collisionMesh = EnsureMeshAssetAtPath(
                FloatingIslandScenePlan.CollisionMeshPath(), collisionSource, null);

            var collisionChild = new GameObject(FloatingIslandScenePlan.CollisionChildName);
            collisionChild.transform.SetParent(root.transform, false);
            collisionChild.isStatic = true;
            collisionChild.AddComponent<MeshFilter>().sharedMesh = collisionMesh;
            MeshCollider collision = collisionChild.AddComponent<MeshCollider>();
            collision.sharedMesh = collisionMesh;
            collision.convex = false;   // 凹面静态网格：站得住、弹体任意方向撞壳都起爆

            // ---- 出生点表（第 3 关装配器接队伍用）：局部 → 世界坐标记进日志 ----
            IslandSpawnPoint[] spawns = FloatingIslandSpawnTable.Build(spec);
            var spawnLog = new System.Text.StringBuilder();
            spawnLog.Append("[FloatingIslandShowcase] 出生点表（世界坐标，y=脚底草皮面；"
                + "接 PirateBase 时加 LevelGeometry.UnitPivotHeight）：\n");
            for (int i = 0; i < spawns.Length; i++)
            {
                Vector3 world = root.transform.TransformPoint(spawns[i].Position);
                spawnLog.AppendFormat("  {0}{1} ({2:0.00}, {3:0.00}, {4:0.00})\n",
                    spawns[i].TeamIndex == 0 ? "R" : "B", spawns[i].Slot,
                    world.x, world.y, world.z);
            }
            Debug.Log(spawnLog.ToString());

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);

            Debug.Log("[FloatingIslandShowcase] 空岛摆放完成（seed=" + spec.Seed + "）。\n"
                + "  根节点位置: " + rootPosition.ToString("0.0")
                + "（岛尖净空 " + FloatingIslandComposer.ArenaClearance + "，见 PlacementHeight）\n"
                + "  三角面合计: " + stats.Triangles + " / 材质槽: " + groups
                + "（预算约 2 万面 / 15 DrawCall）\n"
                + "  包围盒(局部): min=" + stats.BoundsMin.ToString("0.0")
                + " max=" + stats.BoundsMax.ToString("0.0") + "\n"
                + "  网格资产: " + FloatingIslandScenePlan.MeshFolder
                + " / 材质资产: " + FloatingIslandScenePlan.MaterialFolder);

            Selection.activeGameObject = root;
            return root;
        }

        /// <summary>
        /// 移除当前场景里的空岛根节点（按 <see cref="FloatingIslandScenePlan.RootName"/> 扫场景根部，
        /// 不用 GameObject.Find——它找不到 inactive 且违反工程纪律）。资产保留（幂等复用）。
        /// </summary>
        /// <returns>是否移除了至少一个根节点。</returns>
        public static bool Remove()
        {
            bool removedAny = false;
            Scene activeScene = SceneManager.GetActiveScene();
            var doomed = new List<GameObject>();
            var roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == FloatingIslandScenePlan.RootName)
                    doomed.Add(roots[i]);
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                Object.DestroyImmediate(doomed[i]);
                removedAny = true;
            }

            if (removedAny)
                EditorSceneManager.MarkSceneDirty(activeScene);

            return removedAny;
        }

        /// <summary>取当前场景里的空岛根节点（没有则 null）。</summary>
        public static GameObject FindIslandRoot()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == FloatingIslandScenePlan.RootName)
                    return roots[i];
            }

            return null;
        }

        /// <summary>把 Scene 视图对准空岛（选中 + 下一帧 FrameSelected；评审出图用）。</summary>
        public static void FrameSceneViewOnIsland()
        {
            GameObject root = FindIslandRoot();
            if (root == null)
            {
                Debug.LogWarning("[FloatingIslandShowcase] 当前场景没有空岛（先跑 摆放超美空岛）。");
                return;
            }

            Selection.activeGameObject = root;
            EditorApplication.delayCall += () =>
            {
                SceneView view = SceneView.lastActiveSceneView;
                if (view != null)
                    view.FrameSelected();
            };
        }

        // ------------------------------------------------------------------
        // 资产落盘：网格
        // ------------------------------------------------------------------

        /// <summary>
        /// 取/建网格资产并写入缓冲（幂等：已存在就地覆写顶点；与 SceneArtBuilder.EnsureMeshAsset 同构）。
        /// <paramref name="recipe"/> 为加法发光族时**显式写入顶点色**——该 shader 的片元乘顶点色，
        /// 合并网格没有 COLOR 通道时默认值不可依赖（见类头【质量红线】）；传 null = 纯几何网格（碰撞代理用）。
        /// </summary>
        static Mesh EnsureMeshAsset(IslandMaterial slot, MeshBuffers source, IslandMaterialRecipe recipe)
        {
            return EnsureMeshAssetAtPath(FloatingIslandScenePlan.MeshAssetPath(slot), source, recipe);
        }

        static Mesh EnsureMeshAssetAtPath(string path, MeshBuffers source, IslandMaterialRecipe recipe)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                mesh.indexFormat = IndexFormat.UInt32;
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.Clear(false);
            }

            var vertices = new List<Vector3>(source.VertexCount);
            var normals = new List<Vector3>(source.VertexCount);
            var triangles = new List<int>(source.IndexCount);
            source.CopyTo(vertices, normals, triangles);

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);

            if (recipe != null && recipe.Kind == IslandShaderKind.FxAdditive)
            {
                var colors = new List<Color>(vertices.Count);
                for (int i = 0; i < vertices.Count; i++)
                    colors.Add(recipe.VertexTint);
                mesh.SetColors(colors);
            }

            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------
        // 资产落盘：材质（配方 → Unity 材质；三级回落链保证非空）
        // ------------------------------------------------------------------

        /// <summary>取/建槽位材质（幂等；按 <see cref="IslandShaderKind"/> 分四族配置）。</summary>
        static Material EnsureMaterial(IslandMaterial slot)
        {
            string path = FloatingIslandScenePlan.MaterialAssetPath(slot);
            IslandMaterialRecipe recipe = IslandMaterialCatalog.For(slot);
            if (recipe == null)
            {
                Debug.LogError("[FloatingIslandShowcase] 槽位 " + slot + " 没有材质配方（目录表漏登记），跳过。");
                return null;
            }

            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = ResolveShader(recipe.Kind, slot);
            if (m == null)
            {
                m = new Material(shader) { name = IslandMaterialCatalog.AssetName(slot) };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            switch (recipe.Kind)
            {
                case IslandShaderKind.SurfaceSolid:
                    ConfigureSurface(m, recipe);
                    break;
                case IslandShaderKind.UnlitOpaque:
                    ConfigureUnlitOpaque(m, recipe);
                    break;
                case IslandShaderKind.UnlitTransparent:
                    ConfigureUnlitTransparent(m, recipe);
                    break;
                default:
                    ConfigureFxAdditive(m, recipe);
                    break;
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// shader 解析 + 三级回落链（PirateSurface/FxAdditive/URP-Unlit → URP/Lit → Standard）。
        /// **保证返回非空**：回落只记 Warning 不抛——空岛缺主 shader 时宁可降级成纯色也不洋红。
        /// </summary>
        static Shader ResolveShader(IslandShaderKind kind, IslandMaterial slot)
        {
            string primary;
            switch (kind)
            {
                case IslandShaderKind.SurfaceSolid: primary = SurfaceShaderName; break;
                case IslandShaderKind.FxAdditive: primary = AdditiveShaderName; break;
                default: primary = UnlitShaderName; break;
            }

            Shader shader = Shader.Find(primary);
            if (shader != null)
                return shader;

            Debug.LogWarning("[FloatingIslandShowcase] 槽位 " + slot + " 找不到 shader " + primary
                + "（shader 编译失败？先 read_console 查 shader error），回落 URP/Lit。");

            shader = Shader.Find(LitFallbackShaderName);
            if (shader != null)
                return shader;

            Debug.LogWarning("[FloatingIslandShowcase] URP/Lit 也找不到，槽位 " + slot + " 回落 Standard。");
            return Shader.Find("Standard");
        }

        /// <summary>
        /// PBR 表面族（岩/草/泥/石工/木）：PirateSurface 的三档色阶 + 程序化噪声参数，
        /// 并按族接上程序化细节贴图（albedo 色斑 + 法线，同 SceneArtBuilder 的贴图来源）。
        /// 贴图缺失时把强度写 0（退回纯色本体 + 主体噪声），不中断、不随机变色。
        /// </summary>
        static void ConfigureSurface(Material m, IslandMaterialRecipe recipe)
        {
            SetColor(m, "_BaseColorA", SceneArtPalette.Hex(recipe.HexDark));
            SetColor(m, "_BaseColorB", SceneArtPalette.Hex(recipe.HexMid));
            SetColor(m, "_BaseColorC", SceneArtPalette.Hex(recipe.HexLight));
            // 【AI 提案】色阶对比取配方值（岩层靠它读"层理"）；分布偏移保持 0.5 居中。
            SetFloat(m, "_ColorRampContrast", recipe.RampContrast);
            SetFloat(m, "_ColorRampBias", 0.5f);

            // 主体噪声：尺度取全岛统一值（岛体半径 ~14.5，比竞技场道具大一个量级，
            // 噪声从默认 3.5 放宽到 2.2 让色斑与岛体体量匹配）；强度/拉伸取配方值。
            SetFloat(m, "_NoiseScale", 2.2f);
            SetFloat(m, "_NoiseStrength", recipe.NoiseStrength);
            SetVector(m, "_NoiseStretch",
                new Vector4(recipe.NoiseStretch.x, recipe.NoiseStretch.y, 0f, 0f));

            SetFloat(m, "_Metallic", recipe.Metallic);
            SetFloat(m, "_Smoothness", recipe.Smoothness);
            SetFloat(m, "_DebugMode", 0f);

            // ---- 细节贴图（程序化资产，与 Scene_* 道具同源）：先关强度再按需打开（幂等纪律）----
            SetFloat(m, "_DetailAlbedoStrength", 0f);
            SetFloat(m, "_BumpScale", 0f);

            NoiseFamily family = FamilyOf(m.name);

            SetFloat(m, "_NoiseWorldScale", family.WorldScale);
            Texture2D albedo = MaterialNoiseBuilder.Load(family.Albedo);
            Texture2D normal = MaterialNoiseBuilder.Load(family.Normal);

            bool albedoOk = albedo != null && m.HasProperty("_DetailNoiseMap");
            bool normalOk = normal != null && m.HasProperty("_BumpMap");

            if (albedoOk)
            {
                m.SetTexture("_DetailNoiseMap", albedo);
                SetFloat(m, "_DetailAlbedoStrength", family.AlbedoStrength);
            }

            if (normalOk)
            {
                m.SetTexture("_BumpMap", normal);
                SetFloat(m, "_BumpScale", family.NormalStrength);
            }

            if (!albedoOk || !normalOk)
            {
                Debug.LogWarning("[FloatingIslandShowcase] 材质 " + m.name + " 的细节贴图缺失："
                    + (albedoOk ? "" : "albedo ") + (normalOk ? "" : "normal ")
                    + "→ 对应强度已置 0（退回纯色本体 + 主体噪声）。"
                    + "先跑 ArtGate 第 ⓪.5 步（程序化材质噪声贴图）。");
            }
        }

        /// <summary>细节贴图配方族（映射抄 SceneArtBuilder 的 NoiseRecipe 分族口径）。</summary>
        struct NoiseFamily
        {
            public MaterialNoiseBuilder.NoiseKind Albedo;
            public MaterialNoiseBuilder.NoiseKind Normal;
            public float WorldScale;
            public float AlbedoStrength;
            public float NormalStrength;
        }

        /// <summary>按材质名把槽位分进细节贴图配方族（岩/草/木/泥四族，石工并进岩的低强度档）。</summary>
        static NoiseFamily FamilyOf(string materialName)
        {
            // 默认族 = 岩（匹配失败时最安全的回落：岩是空岛的视觉主体）。
            var family = new NoiseFamily
            {
                Albedo = MaterialNoiseBuilder.NoiseKind.RockAlbedo,
                Normal = MaterialNoiseBuilder.NoiseKind.RockNormal,
                WorldScale = 0.45f,
                AlbedoStrength = 0.35f,
                NormalStrength = 0.60f,
            };

            if (string.IsNullOrEmpty(materialName))
                return family;

            if (materialName.Contains("Grass"))
            {
                family.Albedo = MaterialNoiseBuilder.NoiseKind.GrassAlbedo;
                family.Normal = MaterialNoiseBuilder.NoiseKind.GrassNormal;
                family.WorldScale = 0.70f;
                family.AlbedoStrength = 0.35f;
                family.NormalStrength = 0.50f;
            }
            else if (materialName.Contains("Wood"))
            {
                // 木：暖色斑（沙族 albedo）+ 岩族法线（节疤起伏）——与 Scene_Wood 同口径。
                family.Albedo = MaterialNoiseBuilder.NoiseKind.SandAlbedo;
                family.Normal = MaterialNoiseBuilder.NoiseKind.RockNormal;
                family.WorldScale = 0.55f;
                family.AlbedoStrength = 0.18f;
                family.NormalStrength = 0.30f;
            }
            else if (materialName.Contains("Dirt"))
            {
                // 泥土：暖褐底上撒沙族色斑 + 岩族法线（土坷垃的碎感）。
                family.Albedo = MaterialNoiseBuilder.NoiseKind.SandAlbedo;
                family.Normal = MaterialNoiseBuilder.NoiseKind.RockNormal;
                family.WorldScale = 0.50f;
                family.AlbedoStrength = 0.30f;
                family.NormalStrength = 0.40f;
            }
            else if (materialName.Contains("Stone"))
            {
                // 石工：比天然岩更规整（低强度），保留一点凿痕斑驳。
                family.Albedo = MaterialNoiseBuilder.NoiseKind.RockAlbedo;
                family.Normal = MaterialNoiseBuilder.NoiseKind.RockNormal;
                family.WorldScale = 0.45f;
                family.AlbedoStrength = 0.22f;
                family.NormalStrength = 0.35f;
            }

            return family;
        }

        /// <summary>不透明 unlit（旗帜）。</summary>
        static void ConfigureUnlitOpaque(Material m, IslandMaterialRecipe recipe)
        {
            SetColor(m, "_BaseColor", SceneArtPalette.Hex(recipe.Hex, 1f));
            SetFloat(m, "_Surface", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetFloat(m, "_SrcBlend", (float)BlendMode.One);
            SetFloat(m, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(m, "_ZWrite", 1f);
            m.renderQueue = (int)RenderQueue.Geometry;
        }

        /// <summary>半透明 unlit（水帘/浪花/云雾）。</summary>
        static void ConfigureUnlitTransparent(Material m, IslandMaterialRecipe recipe)
        {
            // URP/Unlit 的透明走 _Surface/_Blend + 混合字段 + 关键字，缺一不可（SceneArtBuilder 同款）。
            SetColor(m, "_BaseColor", SceneArtPalette.Hex(recipe.Hex, recipe.Alpha));
            SetFloat(m, "_Surface", 1f);
            SetFloat(m, "_Blend", 0f);   // Alpha
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetFloat(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(m, "_ZWrite", 0f);
            SetFloat(m, "_AlphaClip", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>加法发光（晶体/暖光点）：<c>PirateCrew/Fx/Additive</c> 的 _Color × _Intensity。</summary>
        static void ConfigureFxAdditive(Material m, IslandMaterialRecipe recipe)
        {
            SetColor(m, "_Color", SceneArtPalette.Hex(recipe.Hex));
            SetFloat(m, "_Intensity", recipe.Intensity);
            SetFloat(m, "_DebugMode", 0f);
            // 渲染队列由 shader 自带（Transparent + ZWrite Off）；加法混合不需要改排序。
        }

        // ------------------------------------------------------------------
        // 杂项
        // ------------------------------------------------------------------

        static void SetColor(Material m, string property, Color value)
        {
            if (m != null && m.HasProperty(property))
                m.SetColor(property, value);
        }

        static void SetFloat(Material m, string property, float value)
        {
            if (m != null && m.HasProperty(property))
                m.SetFloat(property, value);
        }

        static void SetVector(Material m, string property, Vector4 value)
        {
            if (m != null && m.HasProperty(property))
                m.SetVector(property, value);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
