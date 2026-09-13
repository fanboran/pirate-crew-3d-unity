using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 【波次 I3】场景美术陈设构建器：把 <c>docs/场景设计-战斗竞技场.md</c> 的施工图落成场景内容。
    ///
    /// 【入口】<see cref="M2BattleSceneSetup"/> 在竞技场骨架（地面 / 水面 / 海床台阶 / 地形根 / 队伍根）
    /// 建好之后调用 <see cref="Apply"/>，并把空的 <c>SceneArt</c> 根节点传进来。所有生成物挂在它之下。
    ///
    /// 【本文件做什么】只做「场景编排 + 资产落盘」：
    ///   ① 潮间带湿沙坡、泡沫线、落水危险带（虚线 + 暗水带）、竞技场外海床沙脊；
    ///   ② 调 <see cref="ScenePropComposer"/>（纯 C#）把 <see cref="ScenePropLayout"/> 的摆位表
    ///      翻译成三角面：搁浅断船 / 栈桥 / 箱桶 / 旗 / 锚 / 棕榈 / 灌木 / 草丛 / 礁石 / 杂物 /
    ///      积云 / 远景剪影岛与帆船；
    ///   ③ 每个材质组把全部实例**合并成 1 个网格**（构建期静态合并，DrawCall 与实例数无关），
    ///      网格存 <c>Assets/Art/Models/Scene/</c>、材质存 <c>Assets/Art/Materials/Scene/</c>；
    ///   ④ 把湿沙材质接到 <see cref="BattleTerrainView"/> 的 <c>wetMaterial</c>（潮沟贴片用）。
    ///
    /// 【几何/规则在哪】三角面生成在 <c>Assets/Scripts/PirateCrew/SceneArt/</c>（纯 C#，无头可测）：
    ///   <see cref="IslandShellGeometry"/>（地形壳）、<see cref="ScenePropGeometry"/>（道具）、
    ///   <see cref="ScenePropLayout"/>（摆位）、<see cref="SceneLayoutRules"/>（规则）、
    ///   <see cref="SceneArtPalette"/> / <see cref="SkyTierCatalog"/>（色值与天空档）。本文件不含可测逻辑。
    ///
    /// 【合同纪律（M2BattleSceneSetup 注释里写明）】
    ///   · 只做纯表现：不引用/修改 BattleController / TurnManager 等规则组件；
    ///   · 不删除、不顶替已有节点（Ground / Water / Seabed_* / TeamRoot / Camera 一个都不动）；
    ///   · 允许重复调用（每次场景重建都会调一次），不依赖"上次留下的引用"；
    ///   · 道具一律**不加 Collider**（场景文档 §9.4：避免投掷物被弹开而破坏"预览 = 实弹"）。
    ///
    /// 【与渲染波次的分工】光照 / 天空盒 / 环境光 / 雾 / 后处理全部由
    /// <see cref="BattleSceneLighting"/> 负责，本文件**一个 RenderSettings 字段都不写**（避免两处覆盖）。
    /// 远景剪影的色值取自 <see cref="SkyTierCatalog"/>（档 1 正午），只作用于本文件自建的几何。
    /// </summary>
    public static class SceneArtBuilder
    {
        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        const string SceneMaterialFolder = "Assets/Art/Materials/Scene";
        const string SceneMeshFolder = "Assets/Art/Models/Scene";

        /// <summary>道具描边材质组用的 shader（与单位同一套 inverted hull，满足场景文档 M12）。</summary>
        const string OutlineShaderName = "PirateCrew/PirateOutline";

        /// <summary>环境材质 shader（渲染波次生成的环境材质库用同一套）。</summary>
        const string SurfaceShaderName = "PirateCrew/PirateSurface";

        const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        // ------------------------------------------------------------------
        // 尺寸常量（全部来自场景文档 §3.3/§5.3/§5.4/§6.4；逐条注明）
        // ------------------------------------------------------------------

        /// <summary>潮间带湿沙坡宽度（竞技场边界外 0→2.5 单位、y 从 0 缓降到 -0.6）。【依据 §3.3】</summary>
        const float TideSlopeWidth = 2.5f;

        /// <summary>水面立方体的顶面高度（M2BattleSceneSetup 把水面放在 y=-0.2、厚 0.1 → 顶面 y=-0.15）。</summary>
        const float WaterTopY = -0.15f;

        /// <summary>泡沫线环带内/外偏移。【AI 提案：贴在水线外侧 0.75-1.75，doc §5.3 只给"宽 0.4-1.2"】</summary>
        const float FoamInnerOffset = 0.75f;

        /// <summary>泡沫线环带外偏移。</summary>
        const float FoamOuterOffset = 1.75f;

        /// <summary>暗水带内/外偏移。【偏离 doc §5.4「边界外 0-1.0」的原因见类头/报告：0-2.5 已被潮间带占用】</summary>
        const float DangerBandInnerOffset = 2.6f;

        /// <summary>暗水带外偏移。</summary>
        const float DangerBandOuterOffset = 3.8f;

        /// <summary>危险虚线所在偏移。【AI 提案：取 3.15 → 落在暗水带正中】</summary>
        const float DangerLineOffset = 3.15f;

        /// <summary>危险虚线线宽。【doc §5.4 给 0.06，本实现取 0.12：1080p 下 0.06 仅约 2px，读不出"线"】</summary>
        const float DangerLineWidth = 0.12f;

        /// <summary>线环带的分段长度（世界单位）。【AI 提案】</summary>
        const float BandSegmentLength = 1f;

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>
        /// 在 <paramref name="root"/>（SceneArt 空节点）下陈设场景美术。幂等：重复调用会先清掉自己上次建的子节点。
        /// </summary>
        public static void Apply(GameObject root)
        {
            if (root == null)
            {
                Debug.LogWarning("[SceneArtBuilder] root 为空，跳过场景陈设。");
                return;
            }

            EnsureFolder(SceneMaterialFolder);
            EnsureFolder(SceneMeshFolder);

            ClearPrevious(root);

            // ---- 与地形/水有关的上下文 ----
            int levelNumber = 1;   // 与 M2BattleSceneSetup.LevelNumber 一致（该常量是 private，这里同值）
            LevelData level = LevelCatalog.Get(levelNumber);
            int arenaW = level.WidthTiles;
            int arenaD = level.HeightTiles;

            TileTerrainGrid grid = TerrainCatalog.Build(levelNumber, arenaW, arenaD);
            if (grid == null)
            {
                // 地形未转写（或尺寸不符）时退回平坦地面：只做潮间带/水/远景，不做贴地道具。
                Debug.LogWarning("[SceneArtBuilder] level_" + levelNumber
                    + " 无瓦片地形数据，场景陈设退化为「平坦沙洲」模式。");
            }

            List<Vector2Int> spawnCells = CollectSpawnCells(level);

            // ---- 材质库（同材质合并成一个网格 = 一个 DrawCall）----
            var materials = new SceneArtMaterials();

            // ---- 三角面缓冲：按材质分组 ----
            var buffers = new ScenePropBuffers();

            // 1. 潮间带湿沙坡（沙→湿沙→泡沫→水 四段过渡里的前两段）
            IslandShellGeometry.AddOffsetBand(buffers.SandWet, arenaW, arenaD,
                0f, TideSlopeWidth, LevelGeometry.GroundTopY, -0.6f, BandSegmentLength);

            // 2. 岸边泡沫线（贴水面之上，避开水立方体的顶面 -0.15）
            IslandShellGeometry.AddFlatRingBand(buffers.Foam, arenaW, arenaD,
                FoamInnerOffset, FoamOuterOffset, WaterTopY + 0.01f, BandSegmentLength);

            // 3. 落水危险带：暗水带 + #CC2222 虚线（都不进可玩区，线全场景宽 = 0.12 单位）
            IslandShellGeometry.AddFlatRingBand(buffers.WaterDark, arenaW, arenaD,
                DangerBandInnerOffset, DangerBandOuterOffset, WaterTopY + 0.002f, BandSegmentLength);
            IslandShellGeometry.AddDashedBorder(buffers.Danger, arenaW, arenaD,
                DangerLineOffset, WaterTopY + 0.012f, 0.9f, 0.55f, DangerLineWidth);

            // 4. 贴地/场外道具（确定性布局）
            int propSeed = levelNumber * 1013 + 7;
            SceneLayout layout = grid != null
                ? ScenePropLayout.Build(grid, spawnCells, propSeed)
                : EmptyLayout(arenaW, arenaD);

            // 调度（摆位 → 三角面）在纯 C# 的 ScenePropComposer 里，编辑器与性能用例共用同一份。
            ScenePropComposer.Compose(buffers, layout, propSeed, arenaD);

            // 5. 落盘：每个非空材质组 = 1 网格 + 1 材质 + 1 渲染器
            int groups = 0;
            groups += EmitGroup(root, "Wood", buffers.Wood, materials.Wood, true, true);
            groups += EmitGroup(root, "WoodDark", buffers.WoodDark, materials.WoodDark, true, true);
            groups += EmitGroup(root, "Rock", buffers.Rock, materials.Rock, true, true);
            groups += EmitGroup(root, "Metal", buffers.Metal, materials.Metal, true, true);
            groups += EmitGroup(root, "Foliage", buffers.Foliage, materials.Foliage, true, true);
            groups += EmitGroup(root, "Cloth", buffers.Cloth, materials.Cloth, true, true);
            groups += EmitGroup(root, "FlagRed", buffers.FlagRed, materials.FlagRed, true, true);
            groups += EmitGroup(root, "FlagBlue", buffers.FlagBlue, materials.FlagBlue, true, true);
            groups += EmitGroup(root, "SandWet", buffers.SandWet, materials.SandWet, true, true);
            groups += EmitGroup(root, "Foam", buffers.Foam, materials.Foam, false, false);
            groups += EmitGroup(root, "DangerLine", buffers.Danger, materials.Danger, false, false);
            groups += EmitGroup(root, "WaterDarkBand", buffers.WaterDark, materials.WaterDark, false, false);
            groups += EmitGroup(root, "FarSilhouette", buffers.Silhouette, materials.Silhouette, false, false);
            groups += EmitGroup(root, "Clouds", buffers.Cloud, materials.Cloud, false, false);

            // 6. 把湿沙材质接给地形壳（潮沟贴片）
            WireTerrainWetMaterial(root, materials.SandWet);

            AssetDatabase.SaveAssets();

            Debug.Log("[SceneArtBuilder] 场景美术陈设完成（level_" + levelNumber + "）。\n"
                + "  道具摆位: " + layout.Props.Count + " 条（掩体中石 " + layout.CoverRockCount
                + " / 棕榈 " + layout.CountOf(ScenePropKind.Palm)
                + " / 草丛 " + layout.CountOf(ScenePropKind.GrassTuft)
                + " / 礁石 " + (layout.CountOf(ScenePropKind.RidgeRock) + layout.CountOf(ScenePropKind.IntertidalRock))
                + "）\n"
                + "  合并网格组: " + groups + " 个（= 该组 DrawCall）\n"
                + "  三角面合计: " + buffers.TotalTriangles
                + "（木 " + buffers.Wood.TriangleCount
                + " / 暗木 " + buffers.WoodDark.TriangleCount
                + " / 岩 " + buffers.Rock.TriangleCount
                + " / 铁 " + buffers.Metal.TriangleCount
                + " / 植被 " + buffers.Foliage.TriangleCount
                + " / 布 " + buffers.Cloth.TriangleCount
                + " / 湿沙 " + buffers.SandWet.TriangleCount
                + " / 剪影 " + buffers.Silhouette.TriangleCount
                + " / 云 " + buffers.Cloud.TriangleCount + "）\n"
                + "  网格资产: " + SceneMeshFolder + " / 材质资产: " + SceneMaterialFolder);
        }

        // ------------------------------------------------------------------
        // 落盘：网格 + 材质 + 渲染器
        // ------------------------------------------------------------------

        /// <summary>
        /// 把一组缓冲落成「1 个网格资产 + 1 个渲染器」。空组直接跳过（不产生空对象）。
        /// </summary>
        /// <returns>实际生成的组数（0 或 1）。</returns>
        static int EmitGroup(GameObject root, string groupName, MeshBuffers buffers, Material material,
            bool castShadows, bool receiveShadows)
        {
            if (buffers == null || buffers.IsEmpty || material == null)
                return 0;

            string meshPath = SceneMeshFolder + "/SceneArt_" + groupName + ".asset";
            Mesh mesh = EnsureMeshAsset(meshPath, buffers);

            var go = new GameObject("SceneArt_" + groupName);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;

            return 1;
        }

        /// <summary>取/建网格资产并写入缓冲（幂等：已存在就地覆盖顶点）。</summary>
        static Mesh EnsureMeshAsset(string path, MeshBuffers buffers)
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

            var vertices = new List<Vector3>(buffers.VertexCount);
            var normals = new List<Vector3>(buffers.VertexCount);
            var triangles = new List<int>(buffers.IndexCount);
            buffers.CopyTo(vertices, normals, triangles);

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------
        // 地形壳接线
        // ------------------------------------------------------------------

        /// <summary>
        /// 把湿沙材质接到场景里 <see cref="BattleTerrainView"/> 的 <c>wetMaterial</c>（潮沟贴片用）。
        /// 只在**场景构建期**按层级找一次（不违反"运行时禁 GameObject.Find"——它不是每帧调用，
        /// 且 M2BattleSceneSetup 尚未给该字段接线）。
        /// </summary>
        static void WireTerrainWetMaterial(GameObject sceneArtRoot, Material wetMaterial)
        {
            if (sceneArtRoot == null || wetMaterial == null)
                return;

            Transform parent = sceneArtRoot.transform.parent;
            if (parent == null)
                return;

            BattleTerrainView view = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                view = parent.GetChild(i).GetComponent<BattleTerrainView>();
                if (view != null)
                    break;
            }

            if (view == null)
            {
                Debug.LogWarning("[SceneArtBuilder] 没找到 BattleTerrainView，潮沟湿沙材质未接线"
                    + "（地形壳会回落用 blockMaterial，不影响可玩性）。");
                return;
            }

            var so = new SerializedObject(view);
            SerializedProperty prop = so.FindProperty("wetMaterial");
            if (prop == null)
            {
                Debug.LogWarning("[SceneArtBuilder] BattleTerrainView 上找不到 wetMaterial 字段。");
                return;
            }

            prop.objectReferenceValue = wetMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
        }

        // ------------------------------------------------------------------
        // 上下文收集
        // ------------------------------------------------------------------

        /// <summary>收集出生格（gridX, gridY）。</summary>
        static List<Vector2Int> CollectSpawnCells(LevelData level)
        {
            var cells = new List<Vector2Int>();
            if (level.Units == null)
                return cells;

            for (int i = 0; i < level.Units.Count; i++)
            {
                LevelUnit unit = level.Units[i];
                cells.Add(new Vector2Int(unit.gridX, unit.gridY));
            }

            return cells;
        }

        static SceneLayout EmptyLayout(int arenaW, int arenaD)
        {
            // 地形未转写时的退化布局：只有潮间带/水/远景（道具摆位需要网格地表）。
            var layout = ScenePropLayout.Build(TileTerrainGrid.Flat(arenaW, arenaD), null, 0);
            return layout;
        }

        static void ClearPrevious(GameObject root)
        {
            var doomed = new List<GameObject>();
            for (int i = 0; i < root.transform.childCount; i++)
                doomed.Add(root.transform.GetChild(i).gameObject);

            for (int i = 0; i < doomed.Count; i++)
                Object.DestroyImmediate(doomed[i]);
        }

        // ------------------------------------------------------------------
        // 材质库（Assets/Art/Materials/Scene，全部程序化、0 贴图）
        // ------------------------------------------------------------------

        /// <summary>
        /// 场景材质库（>Assets/Art/Materials/Scene<）。**只在这里建自己的材质**：
        /// 环境材质（沙/湿沙/岩/地形）由渲染波次的 <see cref="BattleSceneLighting"/> 生成，
        /// 本类只**读取**它（<see cref="BattleSceneLighting.LoadEnvironmentMaterial"/>），读不到才自建兜底。
        ///
        /// 【为什么道具走 <c>PirateCrew/PirateOutline</c>】场景文档 M12 要求"道具带 #2A2A2A 风格描边、
        /// 与单位描边一致"。该 shader 本体 Pass 是简单 Lambert + SH、第 2 个 Pass 是 inverted hull 描边，
        /// 正好可以当一个"描边材质"整体用（这也让本波次不必新增任何 shader）。
        /// 代价：道具本体没有 URP/Lit 的粗糙度分区更细腻的高光（报告里作为取舍说明）。
        /// </summary>
        sealed class SceneArtMaterials
        {
            public readonly Material Wood;
            public readonly Material WoodDark;
            public readonly Material Rock;
            public readonly Material Metal;
            public readonly Material Foliage;
            public readonly Material Cloth;
            public readonly Material FlagRed;
            public readonly Material FlagBlue;
            public readonly Material SandWet;
            public readonly Material Foam;
            public readonly Material Danger;
            public readonly Material WaterDark;
            public readonly Material Silhouette;
            public readonly Material Cloud;

            public SceneArtMaterials()
            {
                // ---- 描边道具材质（本体色 + #2A2A2A 描边）----
                Wood = EnsureOutlineMaterial("Scene_Wood", SceneArtPalette.WoodMid);
                WoodDark = EnsureOutlineMaterial("Scene_WoodDark", SceneArtPalette.WoodDark);
                Rock = EnsureOutlineMaterial("Scene_Rock", SceneArtPalette.RockMid);
                Metal = EnsureOutlineMaterial("Scene_Metal", SceneArtPalette.Iron);
                Foliage = EnsureOutlineMaterial("Scene_Foliage", SceneArtPalette.GrassMid);
                Cloth = EnsureOutlineMaterial("Scene_Cloth", SceneArtPalette.WoodLight);
                FlagRed = EnsureOutlineMaterial("Scene_FlagRed", SceneArtPalette.TeamRed);
                FlagBlue = EnsureOutlineMaterial("Scene_FlagBlue", SceneArtPalette.TeamBlue);

                // ---- 湿沙：优先用渲染波次的环境湿沙材质，读不到才自建（保证水面附近颜色统一）----
                SandWet = BattleSceneLighting.LoadEnvironmentMaterial(BattleSceneLighting.WetSandMaterial);
                if (SandWet == null)
                {
                    Debug.LogWarning("[SceneArtBuilder] 环境材质 " + BattleSceneLighting.WetSandMaterial
                        + " 未找到（BattleSceneLighting.BuildAll 未跑或失败），潮间带退回自建 Scene_SandWet。");
                    SandWet = EnsureLitMaterial("Scene_SandWet", SceneArtPalette.SandDark);
                }

                // ---- 效果类（unlit + 半透明；不参与光照、不投影，场景文档 §5.3）----
                Foam = EnsureUnlitTransparent("Scene_Foam", SceneArtPalette.Foam, 0.55f);
                Danger = EnsureUnlitTransparent("Scene_Danger", SceneArtPalette.Danger, 0.5f);
                WaterDark = EnsureUnlitTransparent("Scene_WaterDark", SceneArtPalette.WaterDeep, 0.5f);

                // ---- 远景：剪影用不透明（雾负责变淡，避免半透明排序抖动）；云用半透明 ----
                // 剪影基色随天空档位取（场景文档 §6.3 的"按距离变浅"档；档 1 = 近档青灰）。
                SkyTierCatalog.SkyTier tier = SkyTierCatalog.ForLevel(1);
                Silhouette = EnsureUnlitOpaque("Scene_Silhouette",
                    tier.Index == 1 ? SceneArtPalette.FarSilhouetteNear : SceneArtPalette.FarSilhouetteFar);
                Cloud = EnsureUnlitTransparent("Scene_Cloud", SceneArtPalette.CloudWhite, 0.62f);
            }
        }

        /// <summary>道具描边材质：本体色由 <paramref name="bodyHex"/> 指定，描边色 = #2A2A2A（GDD §10.4）。</summary>
        static Material EnsureOutlineMaterial(string fileName, string bodyHex)
        {
            string path = SceneMaterialFolder + "/" + fileName + ".mat";
            Shader shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[SceneArtBuilder] 找不到 shader " + OutlineShaderName
                    + "，道具退回 URP/Lit 纯色（无描边）。请先 read_console 确认 shader 无编译错误。");
                return EnsureLitMaterial(fileName, bodyHex);
            }

            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = fileName };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            SetColor(m, "_BaseColor", SceneArtPalette.Hex(bodyHex));

            // 状态 0（无选中）走 _OutlineColor：给不透明的 #2A2A2A，道具因此始终有一圈深色描边。
            SetColor(m, "_OutlineColor", SceneArtPalette.Hex(SceneArtPalette.Outline, 1f));
            SetColor(m, "_OutlineColorHover", SceneArtPalette.Hex(SceneArtPalette.Outline, 1f));
            SetColor(m, "_OutlineColorSelected", SceneArtPalette.Hex(SceneArtPalette.Outline, 1f));

            // 描边宽度：单位是 0.006（M2BattleSceneSetup），道具略细（0.0045）以免压过角色。
            SetFloat(m, "_OutlineWidth", 0.0045f);
            SetFloat(m, "_OutlineWidthHover", 0.0045f);
            SetFloat(m, "_OutlineWidthSelected", 0.0045f);
            SetFloat(m, "_OutlineState", 0f);
            SetFloat(m, "_OutlineAlpha", 1f);
            SetFloat(m, "_OutlineExpandMode", 0f);              // 屏幕空间恒定粗细
            SetFloat(m, "_OutlineDistanceAttenuation", 0.4f);
            SetFloat(m, "_DebugMode", 0f);

            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>不透明的 URP/Lit 兜底材质（0 贴图）。</summary>
        static Material EnsureLitMaterial(string fileName, string hex)
        {
            string path = SceneMaterialFolder + "/" + fileName + ".mat";
            Shader shader = Shader.Find(SurfaceShaderName);
            Material m;

            if (shader != null)
            {
                m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader) { name = fileName };
                    AssetDatabase.CreateAsset(m, path);
                }
                else if (m.shader != shader)
                {
                    m.shader = shader;
                }

                // PirateSurface 走"暗/中/亮"三档色阶：这里给同一色的三档近似（文档只要求"像沙"）。
                Color c = SceneArtPalette.Hex(hex);
                SetColor(m, "_BaseColorA", c * 0.72f);
                SetColor(m, "_BaseColorB", c);
                SetColor(m, "_BaseColorC", c * 1.12f);
                SetFloat(m, "_Metallic", 0f);
                SetFloat(m, "_Smoothness", 0.35f);
                SetFloat(m, "_DebugMode", 0f);
                EditorUtility.SetDirty(m);
                return m;
            }

            shader = Shader.Find(UnlitShaderName);
            if (shader == null)
                shader = Shader.Find("Standard");

            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = fileName };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (shader != null && m.shader != shader)
            {
                m.shader = shader;
            }

            SetColor(m, "_BaseColor", SceneArtPalette.Hex(hex));
            SetColor(m, "_Color", SceneArtPalette.Hex(hex));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>不透明 unlit（远景剪影）。</summary>
        static Material EnsureUnlitOpaque(string fileName, string hex)
        {
            Material m = EnsureUnlit(fileName);
            SetColor(m, "_BaseColor", SceneArtPalette.Hex(hex, 1f));
            SetFloat(m, "_Surface", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetFloat(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetFloat(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            SetFloat(m, "_ZWrite", 1f);
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>半透明 unlit（泡沫/危险线/暗水带/云）。</summary>
        static Material EnsureUnlitTransparent(string fileName, string hex, float alpha)
        {
            Material m = EnsureUnlit(fileName);
            // URP/Unlit 的透明走 _Surface/_Blend + 混合字段 + 关键字，缺一不可（否则会掉回不透明）。
            SetColor(m, "_BaseColor", SceneArtPalette.Hex(hex, alpha));
            SetFloat(m, "_Surface", 1f);
            SetFloat(m, "_Blend", 0f);   // Alpha
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetFloat(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloat(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloat(m, "_ZWrite", 0f);
            SetFloat(m, "_AlphaClip", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material EnsureUnlit(string fileName)
        {
            string path = SceneMaterialFolder + "/" + fileName + ".mat";
            Shader shader = Shader.Find(UnlitShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[SceneArtBuilder] 找不到 shader " + UnlitShaderName + "，退回 Standard。");
                shader = Shader.Find("Standard");
            }

            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = fileName };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            return m;
        }

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

        // ------------------------------------------------------------------
        // 文件夹
        // ------------------------------------------------------------------

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
