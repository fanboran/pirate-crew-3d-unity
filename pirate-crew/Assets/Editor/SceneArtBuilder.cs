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
    ///   ① 落水危险带（#CC2222 虚线，仍绕竞技场矩形一圈）；
    ///   ② 调 <see cref="ScenePropComposer"/>（纯 C#）把 <see cref="ScenePropLayout"/> 的摆位表
    ///      翻译成三角面：搁浅断船 / 栈桥 / 箱桶 / 旗 / 锚 / 棕榈 / 灌木 / 草丛 / 礁石 / 杂物 /
    ///      积云 / 远景剪影岛与帆船；
    ///   ③ 每个材质组把全部实例**合并成 1 个网格**（构建期静态合并，DrawCall 与实例数无关），
    ///      网格存 <c>Assets/Art/Models/Scene/</c>、材质存 <c>Assets/Art/Materials/Scene/</c>；
    ///   ④ 把湿沙材质接到 <see cref="BattleTerrainView"/> 的 <c>wetMaterial</c>（潮沟贴片用）。
    ///
    /// 【r6：竞技场矩形级的三条环带退役】湿沙坡 / 岸边泡沫线 / 暗水带曾绕整块竞技场矩形铺一圈，
    /// 平台簇化后它们悬在开阔水面上、且与岸隔水 —— r5 实测成"大片冷白纸板"（泡沫单连通
    /// 32,844px @sea-shore）与中性灰带。现删除，岸线四段过渡（沙 → 暗湿沙 → 泡沫 → 水）改由
    /// **簇级水线系统**承担（<see cref="IslandShellGeometry.AddPlatformUndersides"/> 的
    /// AddWaterlineWetSand / AddWaterlineFoam / AddWaterlineBand，写入同一个 SandWet / Foam 组）。
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

        /// <summary>
        /// 道具材质组用的 shader。写实化后仍用 <c>PirateCrew/PirateOutline</c>，但**描边参数全部置零**
        /// （见 <see cref="EnsureOutlineMaterial"/>）—— 只借用它的 **PBR 本体 Pass**
        /// （写实化后 Base Pass = URP PBR），不再要 inverted hull 轮廓（场景文档 M12 的描边要求已随
        /// "写实无描边"退役；单位选中/hover 描边不受影响，仍由 PirateOutline 的描边 Pass 提供）。
        /// </summary>
        const string OutlineShaderName = "PirateCrew/PirateOutline";

        /// <summary>环境材质 shader（渲染波次生成的环境材质库用同一套）。</summary>
        const string SurfaceShaderName = "PirateCrew/PirateSurface";

        const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        // ------------------------------------------------------------------
        // 尺寸常量（全部来自场景文档 §3.3/§5.3/§5.4/§6.4；逐条注明）
        // ------------------------------------------------------------------

        // 【r6 退役】竞技场矩形级的三条环带（潮间带湿沙坡 / 岸边泡沫线 / 暗水带）已删除，
        // 故不再需要它们的宽度/内偏移/外偏移常量；相关岸线改由簇级水线系统生成。
        // 保留的只有绕竞技场矩形的 #CC2222 危险虚线（下面三个常量）。

        /// <summary>水面立方体的顶面高度（M2BattleSceneSetup 把水面放在 y=-0.2、厚 0.1 → 顶面 y=-0.15）。</summary>
        const float WaterTopY = -0.15f;

        /// <summary>危险虚线所在偏移。【AI 提案：取 3.15】</summary>
        const float DangerLineOffset = 3.15f;

        /// <summary>危险虚线线宽。【doc §5.4 给 0.06，本实现取 0.12：1080p 下 0.06 仅约 2px，读不出"线"】</summary>
        const float DangerLineWidth = 0.12f;

        /// <summary>
        /// 云核基色（【AI 提案】<c>#E4EAF0</c>，最大通道 240）。r2 出图中云是"硬边纯白 255"，
        /// 故**亮度封顶 240、禁用纯白**；内核用高 alpha，边缘由 <see cref="CloudFringeHex"/> 低 alpha 近似衰减。
        /// </summary>
        const string CloudCoreHex = "#E4EAF0";

        /// <summary>云缘基色（【AI 提案】<c>#D9E2EA</c>，最大通道 234）：更暗更透，读作云的虚边。</summary>
        const string CloudFringeHex = "#D9E2EA";

        /// <summary>
        /// 草梢亮档（【AI 提案】<c>#63A964</c>）：介于 <see cref="SceneArtPalette.GrassMid"/>（#4A8C4A）
        /// 与 <see cref="SceneArtPalette.GrassLight"/>（#7BC67E）之间，与保留中绿档的草丛形成两色变化。
        /// </summary>
        const string GrassLightHex = "#63A964";

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

            // 远景/植被**分层附加材质组**（【提案】）：远岛近/中/远 3 层、云核/云缘、远帆、草根/草梢。
            // 一个材质组只能一个基色，而验收要求"远岛逐层提亮+雾衰减""云中心实边缘虚""草两档绿+根渐暗"，
            // 故拆成多组；几何调度仍共用 ScenePropComposer（编辑器与无头测试同一份逻辑）。
            var tiers = new ScenePropTierBuffers(
                new MeshBuffers(), new MeshBuffers(), new MeshBuffers(),
                new MeshBuffers(), new MeshBuffers(), new MeshBuffers(),
                new MeshBuffers(), new MeshBuffers());

            // 1. 落水危险带：只保留 #CC2222 虚线（不进可玩区，线宽 0.12 单位 = 全场景宽）。
            // 【r6 删除竞技场矩形级的三条环带】湿沙坡 / 岸边泡沫线 / 暗水带曾绕整块竞技场矩形铺一圈，
            // 平台簇化后它们悬在开阔水面上、且与岸隔水 → r5 实测成"大片冷白纸板"
            // （泡沫单连通 32,844px @sea-shore、色 (191,199,207) 单色硬边）与中性灰带。
            // 岸线的四段过渡（沙 → 暗湿沙 → 泡沫 → 水）现在完全由**簇级水线系统**提供：
            // 见下方 4c 的 IslandShellGeometry.AddPlatformUndersides()，它把
            // AddWaterlineWetSand / AddWaterlineFoam 写进同一个 SandWet / Foam 组（逐簇、贴包络）。
            IslandShellGeometry.AddDashedBorder(buffers.Danger, arenaW, arenaD,
                DangerLineOffset, WaterTopY + 0.012f, 0.9f, 0.55f, DangerLineWidth);

            // 4. 贴地/场外道具（确定性布局）
            int propSeed = levelNumber * 1013 + 7;
            SceneLayout layout = grid != null
                ? ScenePropLayout.Build(grid, spawnCells, propSeed)
                : EmptyLayout(arenaW, arenaD);

            // 调度（摆位 → 三角面）在纯 C# 的 ScenePropComposer 里，编辑器与性能用例共用同一份。
            ScenePropComposer.Compose(buffers, layout, propSeed, arenaD, tiers);

            // 4b. 模块化构件（kit）：把平台簇伪装成大船 / 空岛 / 梯田小岛。
            //     构件注册表 + 配方在 SceneKitCatalog（纯 C#），几何在 SceneKitGeometry，
            //     调度在 SceneKitComposer；同材质并入下方既有材质组（构建期合批）。
            int kitSeed = propSeed + 500;
            SceneKitLayout kit = SceneKitCatalog.BuildLevel1(kitSeed);
            SceneKitComposer.Compose(buffers, kit, kitSeed);

            // 4c. 悬空平台底部（船体侧板+龙骨 / 岩锥收尖 / 梯田岩层）：按材质并入 暗木 / 岩。
            if (grid != null)
                IslandShellGeometry.AddPlatformUndersides(buffers, grid, IslandShellSettings.Default);

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
            // SandWet / Foam 两组现在只由**簇级水线系统**填充（竞技场环带已退役，见上方 1.）：
            // 地形未转写（grid == null）时 AddPlatformUndersides 不跑、这两组为空 → EmitGroup 自动跳过。
            groups += EmitGroup(root, "SandWet", buffers.SandWet, materials.SandWet, true, true);
            groups += EmitGroup(root, "Foam", buffers.Foam, materials.Foam, false, false);
            groups += EmitGroup(root, "DangerLine", buffers.Danger, materials.Danger, false, false);
            // WaterDarkBand（暗水带）随竞技场环带一并退役：不再生成该组（buffers.WaterDark 恒空）。
            groups += EmitGroup(root, "FarSilhouette", buffers.Silhouette, materials.Silhouette, false, false);
            groups += EmitGroup(root, "Clouds", buffers.Cloud, materials.Cloud, false, false);

            // 远景/植被分层组（提案）：远岛 3 层、云核/缘、远帆、草根/梢。
            groups += EmitGroup(root, "FarSilNear", tiers.SilhouetteNear, materials.SilhouetteNear, false, false);
            groups += EmitGroup(root, "FarSilMid", tiers.SilhouetteMid, materials.SilhouetteMid, false, false);
            groups += EmitGroup(root, "FarSilFar", tiers.SilhouetteFar, materials.SilhouetteFar, false, false);
            groups += EmitGroup(root, "CloudCore", tiers.CloudCore, materials.CloudCore, false, false);
            groups += EmitGroup(root, "CloudFringe", tiers.CloudFringe, materials.CloudFringe, false, false);
            groups += EmitGroup(root, "SailFar", tiers.SailFar, materials.SailFar, false, false);
            groups += EmitGroup(root, "GrassRoot", tiers.GrassRoot, materials.GrassRoot, true, true);
            groups += EmitGroup(root, "GrassLight", tiers.GrassLight, materials.GrassLight, true, true);

            // 6. 把湿沙材质接给地形壳（潮沟贴片）
            WireTerrainWetMaterial(root, materials.SandWet);

            AssetDatabase.SaveAssets();

            Debug.Log("[SceneArtBuilder] 场景美术陈设完成（level_" + levelNumber + "）。\n"
                + "  道具摆位: " + layout.Props.Count + " 条（掩体中石 " + layout.CoverRockCount
                + " / 棕榈 " + layout.CountOf(ScenePropKind.Palm)
                + " / 草丛 " + layout.CountOf(ScenePropKind.GrassTuft)
                + " / 礁石 " + (layout.CountOf(ScenePropKind.RidgeRock) + layout.CountOf(ScenePropKind.IntertidalRock))
                + "）\n"
                + "  构件摆位(kit): " + kit.Parts.Count + " 件"
                + "（船体段 " + kit.CountOf(SceneKitPiece.HullMid) + " / 甲板 " + kit.CountOf(SceneKitPiece.DeckPlank)
                + " / 桅 " + kit.CountOf(SceneKitPiece.Mast) + " / 岛顶 " + kit.CountOf(SceneKitPiece.IslandTop) + "）\n"
                + "  合并网格组: " + groups + " 个（= 该组 DrawCall）\n"
                + "  三角面合计: " + buffers.TotalTriangles
                + "（木 " + buffers.Wood.TriangleCount
                + " / 暗木 " + buffers.WoodDark.TriangleCount
                + " / 岩 " + buffers.Rock.TriangleCount
                + " / 铁 " + buffers.Metal.TriangleCount
                + " / 植被 " + buffers.Foliage.TriangleCount
                + " / 布 " + buffers.Cloth.TriangleCount
                + " / 湿沙 " + buffers.SandWet.TriangleCount
                + " / 剪影 " + (tiers.SilhouetteNear.TriangleCount + tiers.SilhouetteMid.TriangleCount
                    + tiers.SilhouetteFar.TriangleCount)
                + " / 云 " + (tiers.CloudCore.TriangleCount + tiers.CloudFringe.TriangleCount)
                + " / 草梢亮 " + tiers.GrassLight.TriangleCount
                + " / 草根暗 " + tiers.GrassRoot.TriangleCount + "）\n"
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
            if (wetMaterial == null)
                return;

            // SceneArt 根是 new GameObject 直接挂场景根（无父节点，M2BattleSceneSetup 装配顺序），
            // 旧版在 parent==null 时静默 return → 湿沙材质永不接线、buildUnderside 永不关 →
            // 运行时又生成一份单材质平台底部（r3 岸线洋红带的第二成因）。
            // 现在按 兄弟扫描 → 场景全局 兜底，找不到必须告警，不允许静默跳过。
            BattleTerrainView view = null;
            Transform parent = sceneArtRoot != null ? sceneArtRoot.transform.parent : null;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    view = parent.GetChild(i).GetComponent<BattleTerrainView>();
                    if (view != null)
                        break;
                }
            }

            if (view == null)
                view = Object.FindObjectOfType<BattleTerrainView>();

            if (view == null)
            {
                Debug.LogWarning("[SceneArtBuilder] 没找到 BattleTerrainView，潮沟湿沙材质未接线、"
                    + "buildUnderside 未关闭（运行时会额外生成一份平台底部，且用 blockMaterial）。");
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

            // 构建期已按材质生成平台底部（SceneKit + AddPlatformUndersides），
            // 关掉运行时的单材质底部，避免同一平台出现两份重叠几何。
            SerializedProperty underside = so.FindProperty("buildUnderside");
            if (underside != null)
            {
                underside.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

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
        // 材质库（Assets/Art/Materials/Scene；本体色为纯色 + 程序化细节噪声贴图，0 外部贴图）
        // ------------------------------------------------------------------

        /// <summary>
        /// 场景材质库（>Assets/Art/Materials/Scene<）。**只在这里建自己的材质**：
        /// 环境材质（沙/湿沙/岩/地形）由渲染波次的 <see cref="BattleSceneLighting"/> 生成，
        /// 本类只**读取**它（<see cref="BattleSceneLighting.LoadEnvironmentMaterial"/>），读不到才自建兜底。
        ///
        /// 【为什么道具仍走 <c>PirateCrew/PirateOutline</c>（而不是新增一个道具 shader）】
        /// 写实化后该 shader 的**本体 Pass 已是 URP PBR**（BRDF + 阴影 + SH + 雾），
        /// 正好可当"写实材质"用；描边参数在本类里全部置零（见 <see cref="EnsureOutlineMaterial"/>）。
        /// 这样本波次不必新增 shader，也不改道具的网格合并/材质分组结构（最小改动）。
        /// 【"大色块平面"工单】道具本体只有一个 _BaseColor，若不加噪声，每个材质组就是一整片单色平面。
        /// 现已由 <see cref="PirateOutline"/> 新增的可选 <c>_DetailNoiseMap</c>/<c>_DetailBumpMap</c>
        /// （默认关闭）接上 <see cref="MaterialNoiseBuilder"/> 的程序化贴图 —— 见 <see cref="NoiseRecipe"/>。
        /// 仍未做：三档色阶（PirateSurface 的 _BaseColorA/B/C 机制）—— 本 shader 只有一个 _BaseColor，
        /// 若要"同一材质组内有色相分档"需要给 PirateOutline 再加一组色阶属性（列入遗留优化）。
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

            // ---- 分层组（提案）：远岛三层 / 云核·缘 / 远帆 / 草根·梢 ----
            public readonly Material SilhouetteNear;
            public readonly Material SilhouetteMid;
            public readonly Material SilhouetteFar;
            public readonly Material CloudCore;
            public readonly Material CloudFringe;
            public readonly Material SailFar;
            public readonly Material GrassRoot;
            public readonly Material GrassLight;

            public SceneArtMaterials()
            {
                // ---- 道具材质（PBR 本体 + 描边置零 + 程序化细节噪声；见 EnsureOutlineMaterial / NoiseRecipe）----
                // 配方（一族一行，理由见 NoiseRecipe 类注释）：木/深木 = 沙族暖色斑 + 岩族法线；
                // 岩 = 本族双图；植被/草根/草梢 = 草族双图；布 = 极低强度暖噪 + 弱岩法线；
                // 金属/旗帜 = null（刻意保持纯净）。
                Wood = EnsureOutlineMaterial("Scene_Wood", SceneArtPalette.WoodMid, NoiseWood);
                WoodDark = EnsureOutlineMaterial("Scene_WoodDark", SceneArtPalette.WoodDark, NoiseWoodDark);
                Rock = EnsureOutlineMaterial("Scene_Rock", SceneArtPalette.RockMid, NoiseRock);
                Metal = EnsureOutlineMaterial("Scene_Metal", SceneArtPalette.Iron, null);
                Foliage = EnsureOutlineMaterial("Scene_Foliage", SceneArtPalette.GrassMid, NoiseFoliage);
                Cloth = EnsureOutlineMaterial("Scene_Cloth", SceneArtPalette.WoodLight, NoiseCloth);
                FlagRed = EnsureOutlineMaterial("Scene_FlagRed", SceneArtPalette.TeamRed, null);
                FlagBlue = EnsureOutlineMaterial("Scene_FlagBlue", SceneArtPalette.TeamBlue, null);

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
                Cloud = EnsureUnlitTransparent("Scene_Cloud", CloudCoreHex, 0.62f);

                // ---- 远景剪影三层：近/中/远逐层提亮 + 雾衰减（【提案】档 1 地平线雾色）----
                SilhouetteNear = EnsureUnlitOpaque("Scene_SilhouetteNear", SkyTierCatalog.FarSilhouetteHex(0, 1));
                SilhouetteMid = EnsureUnlitOpaque("Scene_SilhouetteMid", SkyTierCatalog.FarSilhouetteHex(1, 1));
                SilhouetteFar = EnsureUnlitOpaque("Scene_SilhouetteFar", SkyTierCatalog.FarSilhouetteHex(2, 1));

                // ---- 云：核（高 alpha）/ 缘（低 alpha）两层近似"中心 1 → 边缘 0"的顶点 alpha 渐变；
                //      基色亮度封顶 240（#E4EAF0 / #D9E2EA），禁止纯 255 ----
                CloudCore = EnsureUnlitTransparent("Scene_CloudCore", CloudCoreHex, 0.92f);
                CloudFringe = EnsureUnlitTransparent("Scene_CloudFringe", CloudFringeHex, 0.30f);

                // ---- 远帆：单独走 SailFarWhite #E8E8E0（最大通道 232，非过曝纯白）----
                SailFar = EnsureUnlitOpaque("Scene_SailFar", SceneArtPalette.SailFarWhite);

                // ---- 草：根暗档（GrassDark #2D5A2D）/ 梢亮档（介于 GrassMid↔GrassLight）----
                GrassRoot = EnsureOutlineMaterial("Scene_GrassRoot", SceneArtPalette.GrassDark, NoiseGrassRoot);
                GrassLight = EnsureOutlineMaterial("Scene_GrassLight", GrassLightHex, NoiseGrassLight);
            }
        }

        // ------------------------------------------------------------------
        // Scene_* 族的细节噪声配方（程序化资产 → PirateOutline 的可选 _Detail* 通道）
        // ------------------------------------------------------------------

        /// <summary>
        /// Scene_* 族（<c>PirateOutline</c> 本体）的**细节噪声配方**：一族的 albedo 色斑图 + 法线图
        /// + 二者**共用的世界尺度**。
        ///
        /// 【解决什么】道具是按材质组合并成的大网格（1 组 = 1 DrawCall），网格无 UV、本体只有一个
        ///   <c>_BaseColor</c> → 每个材质组是一整片单色平面（"贴图质量低"的主要观感来源之一）。
        ///   贴上程序化色斑 + 细节法线后，木有木纹暖斑、岩有斑块裂隙、草有双色 patch。
        ///
        /// 【为什么 albedo 与法线共用一个 WorldScale】shader 里只有一组 detailUV；两套尺度会得到
        ///   "法线浮在色斑之外"的错位观感。
        ///
        /// 【为什么不用模型 UV】这些合并网格根本没有 UV 通道（SceneArtBuilder.EmitGroup 只写
        ///   position+normal），世界 XZ 投影是唯一可用口径（且让同一构件的纹理跨面连续）。
        ///
        /// 【选型理由（逐族）】
        ///   · <b>木 / 深木</b>：沙族 albedo（暖色色斑）做低强度"暖噪" + 岩族法线给节疤/木纹起伏；
        ///   · <b>岩</b>：本族双图（斑块 + 裂缝暗线 + 强断裂感）；
        ///   · <b>植被 / 草根 / 草梢</b>：草族双图（双色 patch + 绒毛感）；
        ///   · <b>布</b>：极低强度沙族暖噪 + 弱岩法线（读作织物起伏，不抢队色）；
        ///   · <b>金属 / 旗帜</b>：<c>null</c> 配方，**刻意保持纯净** —— 金属的不均匀来自边缘磨损而非
        ///     介质色斑（与 <see cref="BattleSceneLighting"/> 对黄铜/铁的处理同一条纪律）；旗帜是大色块
        ///     队色标识，加噪声会削弱"一眼分红蓝"的可读性；
        ///   · <b>远影族</b>（Silhouette / Cloud / SailFar / CloudCore…）：走 URP/Unlit，**根本不经本通道**
        ///     —— 雾里不需要细节（任务书明确要求保持纯净）。
        ///
        /// 【WorldScale = 1/平铺米数】木 0.55（约 1.8m）/ 岩 0.45（2.2m）/ 草 0.70（1.4m）——
        ///   道具尺度是 0.3-3 m 的构件，贴图按 1-2 m 平铺才能让中频八度（period 4-16）落在
        ///   "一个构件上能看出 2-5 个斑块"的可读区间。
        ///
        /// 【强度口径】贴图是**均值保持的乘性图**（线性均值恰 0.5 → ×2 后恰 1.0），
        ///   故任何强度都不改材质已调好的平均色（<see cref="SceneArtPalette"/> 的色值零漂移）；
        ///   强度只是"起伏占比"：0.45-0.5 = 用满贴图起伏（岩/草），0.16-0.22 = 轻微（木/布）。
        /// </summary>
        sealed class NoiseRecipe
        {
            public readonly MaterialNoiseBuilder.NoiseKind Albedo;
            public readonly MaterialNoiseBuilder.NoiseKind Normal;
            public readonly float WorldScale;
            public readonly float AlbedoStrength;
            public readonly float NormalStrength;

            public NoiseRecipe(MaterialNoiseBuilder.NoiseKind albedo, MaterialNoiseBuilder.NoiseKind normal,
                float worldScale, float albedoStrength, float normalStrength)
            {
                Albedo = albedo;
                Normal = normal;
                WorldScale = worldScale;
                AlbedoStrength = albedoStrength;
                NormalStrength = normalStrength;
            }
        }

        static readonly NoiseRecipe NoiseWood = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
            0.55f, 0.22f, 0.35f);

        static readonly NoiseRecipe NoiseWoodDark = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
            0.55f, 0.18f, 0.30f);

        static readonly NoiseRecipe NoiseRock = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.RockAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
            0.45f, 0.50f, 0.85f);

        static readonly NoiseRecipe NoiseFoliage = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.GrassAlbedo, MaterialNoiseBuilder.NoiseKind.GrassNormal,
            0.70f, 0.45f, 0.60f);

        static readonly NoiseRecipe NoiseCloth = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
            0.50f, 0.16f, 0.25f);

        static readonly NoiseRecipe NoiseGrassRoot = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.GrassAlbedo, MaterialNoiseBuilder.NoiseKind.GrassNormal,
            0.70f, 0.40f, 0.50f);

        static readonly NoiseRecipe NoiseGrassLight = new NoiseRecipe(
            MaterialNoiseBuilder.NoiseKind.GrassAlbedo, MaterialNoiseBuilder.NoiseKind.GrassNormal,
            0.70f, 0.45f, 0.55f);

        /// <summary>
        /// 给 Scene_* 道具材质接上程序化细节噪声（<paramref name="recipe"/> 为 <c>null</c> = 该族刻意
        /// 保持纯净）。贴图来自 <see cref="MaterialNoiseBuilder"/>（512² 域名扭曲 fBm 的程序化资产）；
        /// **贴图缺失时把两个强度都写 0**（画面退回"纯色本体"、绝不随机变色）并记一条告警。
        /// 幂等：每次场景重建都会重跑一遍，先关强度再按需打开。
        /// </summary>
        static void ApplyOutlineDetailNoise(Material m, NoiseRecipe recipe)
        {
            if (m == null)
                return;

            // 先关强度：保证"上次跑过、这次贴图没了"不会留下旧的开启状态（与 BattleSceneLighting 同款纪律）。
            SetFloat(m, "_DetailNoiseStrength", 0f);
            SetFloat(m, "_DetailBumpScale", 0f);

            if (recipe == null)
                return;

            SetFloat(m, "_DetailNoiseScale", recipe.WorldScale);

            Texture2D albedo = MaterialNoiseBuilder.Load(recipe.Albedo);
            Texture2D normal = MaterialNoiseBuilder.Load(recipe.Normal);

            bool albedoOk = albedo != null && m.HasProperty("_DetailNoiseMap");
            bool normalOk = normal != null && m.HasProperty("_DetailBumpMap");

            if (albedoOk)
            {
                m.SetTexture("_DetailNoiseMap", albedo);
                SetFloat(m, "_DetailNoiseStrength", recipe.AlbedoStrength);
            }

            if (normalOk)
            {
                m.SetTexture("_DetailBumpMap", normal);
                SetFloat(m, "_DetailBumpScale", recipe.NormalStrength);
            }

            if (!albedoOk || !normalOk)
            {
                Debug.LogWarning("[SceneArtBuilder] 材质 " + m.name + " 的细节噪声贴图缺失："
                    + (albedoOk ? "" : "albedo ")
                    + (normalOk ? "" : "normal ")
                    + "→ 对应强度已置 0（道具退回纯色本体）。"
                    + "先跑 PirateCrew/渲染/生成程序化材质噪声贴图（或 ArtGate 的 ⓪.5 步）。");
            }
        }

        /// <summary>
        /// 道具材质：本体色由 <paramref name="bodyHex"/> 指定；**描边已退役**（写实方向无描边）。
        /// 实现方式 = 把三套描边色 alpha 与三套宽度全部置 0：
        ///   · 宽度 0 → inverted hull 外扩为 0，壳体与本体同深，被 ZTest LEqual 剔除；
        ///   · alpha 0 → OutlineFragment 里 `alpha &lt; 0.002` 直接 discard（双保险）。
        /// 道具本体仍由 PirateOutline 的 Base Pass 渲染（写实化后为 URP PBR）。
        /// 单位的选中/hover 描边走**另一份材质**（PirateOutlineUnit.mat，由 M2BattleSceneSetup 生成），
        /// 不受本方法影响 —— 功能反馈完整保留。
        ///
        /// 【细节噪声】<paramref name="noise"/> 为 null 的族（金属/旗帜）显式把两个强度写 0，
        /// 于是它们的材质与"加贴图之前"逐像素一致；有配方的族接上程序化色斑 + 细节法线
        /// （见 <see cref="NoiseRecipe"/> 与 <see cref="ApplyOutlineDetailNoise"/>）。
        /// </summary>
        static Material EnsureOutlineMaterial(string fileName, string bodyHex, NoiseRecipe noise)
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

            // ---- 细节噪声（程序化资产；null 配方 = 把两个强度写 0，材质与加贴图前逐像素一致）----
            ApplyOutlineDetailNoise(m, noise);

            // ---- 描边退役：三套色 alpha=0、三套宽=0 ----
            SetColor(m, "_OutlineColor", SceneArtPalette.Hex(SceneArtPalette.Outline, 0f));
            SetColor(m, "_OutlineColorHover", SceneArtPalette.Hex(SceneArtPalette.Outline, 0f));
            SetColor(m, "_OutlineColorSelected", SceneArtPalette.Hex(SceneArtPalette.Outline, 0f));

            SetFloat(m, "_OutlineWidth", 0f);
            SetFloat(m, "_OutlineWidthHover", 0f);
            SetFloat(m, "_OutlineWidthSelected", 0f);
            SetFloat(m, "_OutlineState", 0f);
            SetFloat(m, "_OutlineAlpha", 1f);                   // 逐状态 alpha 已为 0；此总乘子保持 1 以便单独回退
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
