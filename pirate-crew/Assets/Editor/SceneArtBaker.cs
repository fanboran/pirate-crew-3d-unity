using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.SceneArt;
using PirateCrew.PirateCrew.SceneArt.Lowpoly;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 样板场景件**编辑器烘焙器**（糖豆人式资产架构，任务书阶段 A/B）：配方 + 种子 → 按材质组
    /// 合并网格 → 落 prefab 资产。几何生成从此退出运行时——战斗 Awake 只做实例化组装。
    ///
    /// 【产物】（全部在 <see cref="BakeFolder"/>，与 Blender 手作 FBX 同目录同清单）
    ///   · Ship_Galleon.prefab   —— <see cref="SceneKitCatalog.LargeShipRecipe"/>（配方+种子=固定输出）
    ///   · Ship_Longboat.prefab  —— <see cref="SceneKitCatalog.SmallBoatRecipe"/>
    ///   · ShowcaseDangerBorder.prefab —— 落水危险虚线（样板三关共用）
    /// 每个材质组一个合并子网格（命名/投影口径照抄 RuntimeSceneArt.EmitGroup：
    /// "SceneArt_&lt;组名&gt;"——AmbientWindBinder 靠该名做风摆绑定，不能改）。
    ///
    /// 【确定性】同配方 + 同种子必得逐顶点一致的输出（EditMode 断言见
    /// Tests/SceneArt/SceneArtBakeDeterminismTests.cs）。多样性靠"更多预制变体"而非运行时随机：
    /// 需要第 N 种船时加一条配方烘焙第 N 个 prefab，运行时零成本换装。
    ///
    /// 【幂等】网格资产就地覆写（GUID 稳定，prefab 引用不漂）、prefab 覆盖保存、场景接线重写。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/烘焙/样板场景件（船/危险线）
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.SceneArtBaker.BuildAll
    /// </summary>
    public static class SceneArtBaker
    {
        // ------------------------------------------------------------------
        // 路径常量
        // ------------------------------------------------------------------

        /// <summary>烘焙产物目录（与 Blender 手作件同目录——阶段 C「命名统一」的落点）。</summary>
        const string BakeFolder = "Assets/Art/Models/SceneKit";

        /// <summary>烘焙网格资产目录（prefab 引用的独立 .asset，不内嵌）。</summary>
        const string MeshFolder = BakeFolder + "/Baked";

        /// <summary>场景美术材质（Scene_* 族，原 SceneArtBuilder 生成、RuntimeSceneArt 同源）。</summary>
        const string SceneMaterialFolder = "Assets/Art/Materials/Scene";

        const string BattleScenePath = "Assets/Scenes/Battle.unity";

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>低模云槽位材质目录（运行时 GetOrCreateMaterial 的烘焙落盘版）。</summary>
        const string LowpolyMaterialFolder = "Assets/Art/Materials/Lowpoly";

        [MenuItem("PirateCrew/烘焙/样板场景件（船/云场/危险线）")]
        public static void BuildAll()
        {
            EnsureFolder(BakeFolder);
            EnsureFolder(MeshFolder);
            EnsureFolder(LowpolyMaterialFolder);

            BakeShip("Ship_Galleon", SceneKitCatalog.LargeShipRecipe);
            BakeShip("Ship_Longboat", SceneKitCatalog.SmallBoatRecipe);
            BakeCloudField();
            BakeDangerBorder();
            WireBattleScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SceneArtBaker] 样板场景件烘焙完成：" + BakeFolder
                + "（船 ×2 + 云场 + 危险线；同配方重跑逐顶点一致，见确定性测试）。");
        }

        // ------------------------------------------------------------------
        // 船（配方 → prefab）
        // ------------------------------------------------------------------

        /// <summary>
        /// 烘一艘船：配方展开（<see cref="SceneKitCatalog.BuildCompleteShip"/>，甲板中心=原点、yaw=0、
        /// 种子与样板第 2 关 <c>ShowcaseLevels.ComposeInto</c> 的 Compose 种子同值 20）→
        /// <see cref="SceneKitComposer.Compose"/> 按材质写缓冲 → 非空组各落一个合并子网格。
        /// 实例摆位（格坐标/yaw）在 <c>ShowcaseLevels.BakedPlacements</c>——运行时只 Instantiate。
        /// </summary>
        static void BakeShip(string prefabName, in ShipRecipe recipe)
        {
            const int composeSeed = 20;   // 与样板第 2 关 ShowcaseLevels.ComposeInto 的 Compose 种子同值

            var buffers = new ScenePropBuffers();
            var layout = new SceneKitLayout();
            List<KitPart> parts = SceneKitCatalog.BuildCompleteShip(recipe, Vector3.zero, 0f, composeSeed,
                recipe.HullLength, recipe.BowLength, recipe.SternLength, recipe.HullBeam,
                mastAlongOffsets: null);
            for (int i = 0; i < parts.Count; i++)
                layout.Add(parts[i]);
            SceneKitComposer.Compose(buffers, layout, composeSeed);

            var root = new GameObject(prefabName);
            EmitGroupMesh(root, "SceneArt_Wood", buffers.Wood, "Scene_Wood", castShadows: true);
            EmitGroupMesh(root, "SceneArt_WoodDark", buffers.WoodDark, "Scene_WoodDark", castShadows: true);
            EmitGroupMesh(root, "SceneArt_Metal", buffers.Metal, "Scene_Metal", castShadows: true);
            EmitGroupMesh(root, "SceneArt_Cloth", buffers.Cloth, "Scene_Cloth", castShadows: true);

            SavePrefab(root, BakeFolder + "/" + prefabName + ".prefab");
        }

        // ------------------------------------------------------------------
        // 云场（阶段 B：几何纯 C#，装配壳落成 prefab）
        // ------------------------------------------------------------------

        /// <summary>
        /// 烘云场：低模云几何（<see cref="CloudFieldGeometry"/>，同种子必得同一片云）由
        /// <see cref="LowpolyStageBuilder.BuildCloudField"/> 装配（2 个材质槽网格 + 每云双 BoxCollider），
        /// 烘焙 = 把运行时网格/材质**落成资产**后存 prefab——运行时只实例化，不再逐帧 new Mesh。
        /// 实例摆位（场心格 10, 7.5）在 <c>ShowcaseLevels.BakedPlacements</c>。
        /// </summary>
        static void BakeCloudField()
        {
            LowpolyStageReport report = LowpolyStageBuilder.BuildCloudField(null, CloudFieldSpec.Default);
            GameObject root = report.Root;   // "LowpolyCloudField"：网格子物体 + Colliders/（每云双 BoxCollider）
            root.name = "CloudField";

            // 网格 → 资产（就地覆写保 GUID），渲染器改引资产网格。
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh runtimeMesh = filter.sharedMesh;
                if (runtimeMesh == null)
                    continue;
                Mesh asset = EnsureMeshAssetFromRuntime(MeshFolder + "/" + runtimeMesh.name + ".asset", runtimeMesh);
                filter.sharedMesh = asset;
            }

            // 材质 → 资产（Lowpoly 槽位材质原本只在运行时缓存，烘焙侧落盘同参数 .mat）。
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material source = renderer.sharedMaterial;
                if (source == null)
                    continue;
                renderer.sharedMaterial = EnsureCloudMaterialAsset(source.name);
            }

            SavePrefab(root, BakeFolder + "/CloudField.prefab");
        }

        /// <summary>按运行时材质名（"Lowpoly_&lt;槽位&gt;"）落盘同参数材质资产（幂等）。</summary>
        static Material EnsureCloudMaterialAsset(string runtimeMaterialName)
        {
            LowpolyMaterialSlot slot;
            switch (runtimeMaterialName)
            {
                case "Lowpoly_CloudWarmWhite": slot = LowpolyMaterialSlot.CloudWarmWhite; break;
                case "Lowpoly_CloudPaleGold": slot = LowpolyMaterialSlot.CloudPaleGold; break;
                default:
                    Debug.LogWarning("[SceneArtBaker] 未登记的低模槽位材质 " + runtimeMaterialName
                        + "——按 URP/Lit 白色落盘（检查 LowpolyStageBuilder.GetOrCreateMaterial 的槽位表）。");
                    return EnsureMaterialAsset(
                        LowpolyMaterialFolder + "/" + runtimeMaterialName + ".mat", runtimeMaterialName,
                        "Universal Render Pipeline/Lit", Color.white);
            }

            // 参数照抄 LowpolyStageBuilder.GetOrCreateMaterial：PirateSurface 平色（A/B/C 同色）、
            // _Smoothness 0.06、_Metallic 0——播放器里被资产引用的活 shader 是 PirateSurface（r11 教训）。
            Material material = EnsureMaterialAsset(
                LowpolyMaterialFolder + "/" + runtimeMaterialName + ".mat", runtimeMaterialName,
                "PirateCrew/PirateSurface", LowpolyStageBuilder.ColorOf(slot));
            if (material.HasProperty("_BaseColorA"))
            {
                material.SetColor("_BaseColorA", LowpolyStageBuilder.ColorOf(slot));
                material.SetColor("_BaseColorB", LowpolyStageBuilder.ColorOf(slot));
                material.SetColor("_BaseColorC", LowpolyStageBuilder.ColorOf(slot));
            }
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.06f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>材质资产就地覆写（存在则改色/改 shader，不存在则 CreateAsset）。</summary>
        static Material EnsureMaterialAsset(string path, string name, string shaderName, Color color)
        {
            Shader shader = Shader.Find(shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader != null ? shader : Shader.Find("Standard")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 把运行时 Mesh 的数据写进资产网格（就地覆写保 GUID；与 EnsureMeshAsset 的缓冲版同口径）。
        /// </summary>
        static Mesh EnsureMeshAssetFromRuntime(string path, Mesh runtimeMesh)
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

            mesh.SetVertices(new List<Vector3>(runtimeMesh.vertices));
            mesh.SetNormals(new List<Vector3>(runtimeMesh.normals));
            mesh.SetTriangles(new List<int>(runtimeMesh.triangles), 0, true);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------
        // 危险虚线（样板三关共用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 落水危险虚线：参数照抄 <c>ShowcaseLevels.ComposeInto</code> 的既有调用
        /// （偏移 3.15 / 线宽 0.9-0.18；绕 20×15 格竞技场一圈），烘焙成单网格 prefab。
        /// </summary>
        static void BakeDangerBorder()
        {
            var buffers = new ScenePropBuffers();
            IslandShellGeometry.AddDashedBorder(buffers.Danger,
                ShowcaseLevels.WidthTiles, ShowcaseLevels.DepthTiles,
                3.15f, LevelGeometry.WaterSurfaceY + 0.012f, 0.9f, 0.55f, 0.18f);

            var root = new GameObject("ShowcaseDangerBorder");
            EmitGroupMesh(root, "SceneArt_DangerLine", buffers.Danger, "Scene_Danger", castShadows: false);

            SavePrefab(root, BakeFolder + "/ShowcaseDangerBorder.prefab");
        }

        // ------------------------------------------------------------------
        // 组 → 合并网格（口径照抄 RuntimeSceneArt.EmitGroup）
        // ------------------------------------------------------------------

        /// <summary>
        /// 非空缓冲 → "SceneArt_&lt;组名&gt;" 子物体（合并网格 + 材质 + 投影口径）。
        /// 【命名契约】AmbientWindBinder 按名绑定风摆（Cloth=帆/索具），改名 = 风摆静默失效。
        /// </summary>
        static void EmitGroupMesh(GameObject root, string objectName, MeshBuffers source,
            string materialName, bool castShadows)
        {
            if (source == null || source.IsEmpty)
                return;

            var child = new GameObject(objectName);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            Mesh mesh = EnsureMeshAsset(MeshFolder + "/" + objectName + ".asset", source);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                SceneMaterialFolder + "/" + materialName + ".mat");
            if (material == null)
            {
                Debug.LogError("[SceneArtBaker] 缺材质 " + SceneMaterialFolder + "/" + materialName
                    + ".mat——组 " + objectName + " 无材质不入 prefab（渲染铁律：宁缺不粉）。");
                Object.DestroyImmediate(child);
                return;
            }

            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        }

        /// <summary>
        /// 网格资产就地覆写（模式照抄 FloatingIslandShowcaseMenu.EnsureMeshAssetAtPath）：
        /// 已存在则重写顶点保持 GUID 稳定（prefab/场景引用不漂），否则 CreateAsset。
        /// </summary>
        static Mesh EnsureMeshAsset(string path, MeshBuffers source)
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
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success || prefab == null)
                Debug.LogError("[SceneArtBaker] 保存 prefab 失败: " + path);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------
        // 场景接线（把烘焙件写进 Battle.unity 的 RuntimeSceneArt）
        // ------------------------------------------------------------------

        /// <summary>
        /// 打开 Battle 场景，把烘焙 prefab 写进 <see cref="RuntimeSceneArt"/> 的序列化引用后存盘。
        /// <c>M2BattleSceneSetup.WireRuntimeSceneArt</c> 也会按同路径装载（无烘焙资产时留 null），
        /// 故本步与场景重建的先后次序无关紧要；这里是显式收口。
        /// </summary>
        static void WireBattleScene()
        {
            var scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            RuntimeSceneArt sceneArt = Object.FindObjectOfType<RuntimeSceneArt>();
            if (sceneArt == null)
            {
                Debug.LogError("[SceneArtBaker] Battle 场景里没有 RuntimeSceneArt（先跑 M2BattleSceneSetup.BuildAll）。");
                return;
            }

            var so = new SerializedObject(sceneArt);
            SetPrefabRef(so, "galleonPrefab", BakeFolder + "/Ship_Galleon.prefab");
            SetPrefabRef(so, "longboatPrefab", BakeFolder + "/Ship_Longboat.prefab");
            SetPrefabRef(so, "cloudFieldPrefab", BakeFolder + "/CloudField.prefab");
            SetPrefabRef(so, "dangerBorderPrefab", BakeFolder + "/ShowcaseDangerBorder.prefab");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void SetPrefabRef(SerializedObject so, string fieldName, string path)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError("[SceneArtBaker] RuntimeSceneArt 找不到序列化字段 " + fieldName + "（字段名漂移？）");
                return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogError("[SceneArtBaker] 烘焙产物缺失: " + path);
            prop.objectReferenceValue = prefab;
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
