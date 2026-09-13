using System.Collections.Generic;
using System.IO;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Visual;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 【波次 I2 填充】角色视觉预制体生成器：程序化产出 7 个职业预制体（六职业 + 船长），
    /// 配套网格资产（<c>Assets/Art/Models/Generated/</c>）与共享材质（<c>Assets/Art/Materials/Crew/</c>）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/角色/生成职业视觉预制体
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.CrewVisualPrefabBuilder.BuildAll
    ///
    /// 【预制体结构】（docs/角色造型规范.md §5 接线要求）
    /// <code>
    /// Crew_&lt;Profession&gt;（根）
    /// ├ Transform.scale = (0.375, 0.5, 0.375)   ← 与 PirateBase.prefab 一致
    /// ├ BoxCollider size=(1,1,1)                  ← 世界 AABB 0.375×0.5×0.375（碰撞契约不变）
    /// ├ Rigidbody mass=1 drag=0 angularDrag=0.05 useGravity=on
    /// ├ PirateBase / UnitOutlineBinder / CrewVisualAnimator
    /// └ Visual（CrewVisualRig + 各部件 renderer）
    /// </code>
    ///
    /// 【为什么不替换 PirateBase.prefab 的 Cube 外观】<c>M2BattleSceneSetup.BuildAll</c> 会重建
    /// PirateBase.prefab 为 Cube；把职业外观塞进去会与该重建互相覆盖。这里改为**独立职业预制体**，
    /// 由 <c>BattleController.crewVisualPrefabs</c> 按 <c>CrewVisualCatalog</c> 映射选择，
    /// 未命中/未接线时回落 PirateBase.prefab（对方块外观做兜底，不破坏既有场景）。
    ///
    /// 【幂等】
    ///   · 网格资产存在则**就地刷新**（<c>ApplyToMesh</c>，不换 GUID、不丢引用）；
    ///   · 材质存在则就地更新参数（不新建重名副本）；
    ///   · 预制体按固定路径覆盖保存。
    ///
    /// 【发光说明】火把/火焰用 <c>Flame</c> 材质的高亮 base color（#FF7A1A）借 HDR + Bloom 出光。
    ///   PirateOutline shader 没有 Emission 通道，且本波次禁止改 shader（另一 agent 在改 ShadowCaster），
    ///   故不引入 URP/Lit 发光材质，避免"火焰没有描边"破坏 R-5。
    /// </summary>
    public static class CrewVisualPrefabBuilder
    {
        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        const string MeshFolder = "Assets/Art/Models/Generated";
        const string CrewMaterialFolder = "Assets/Art/Materials/Crew";
        const string CrewPrefabFolder = "Assets/Prefabs/PirateCrew/Crew";

        const string OutlineShaderName = "PirateCrew/PirateOutline";

        /// <summary>单位根缩放：与 PirateBase.prefab 一致，使 BoxCollider(1,1,1) 的世界 AABB = 0.375×0.5×0.375。</summary>
        static readonly Vector3 UnitRootScale = new Vector3(12f / 32f, 16f / 32f, 12f / 32f);

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>生成全部网格 / 材质 / 职业预制体（幂等，可重复调用）。</summary>
        [MenuItem("PirateCrew/角色/生成职业视觉预制体")]
        public static void BuildAll()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Models");
            EnsureFolder(MeshFolder);
            EnsureFolder("Assets/Art/Materials");
            EnsureFolder(CrewMaterialFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/PirateCrew");
            EnsureFolder(CrewPrefabFolder);

            Dictionary<string, MeshData> meshData = CrewMeshLibrary.BuildAll();
            Dictionary<string, Mesh> meshes = BuildMeshAssets(meshData);
            Material[] materials = BuildMaterialAssets();
            CrewVisualAssetSet assetSet = BuildAssetSet(meshes, materials);

            var report = new System.Text.StringBuilder();
            report.AppendLine("[CrewVisualPrefabBuilder] 职业视觉预制体生成完成：");
            report.AppendLine("  网格资产: " + MeshFolder + "（" + meshes.Count + " 个）");
            report.AppendLine("  材质资产: " + CrewMaterialFolder + "（" + materials.Length + " 个）");

            int failures = 0;
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                CrewProfession profession = CrewVisualCatalog.AllProfessions[i];
                GameObject prefab = BuildProfessionPrefab(profession, assetSet, out int renderers, out int triangles);
                if (prefab == null)
                {
                    failures++;
                    report.AppendLine("  [" + CrewVisualCatalog.DisplayName(profession) + "] 生成失败");
                    continue;
                }

                report.AppendLine("  [" + CrewVisualCatalog.DisplayName(profession) + " / " + profession + "] "
                    + "renderer=" + renderers
                    + " tri=" + triangles
                    + "（预算 " + CrewMeshFactory.MaxTrianglesPerUnit + "）"
                    + (triangles <= CrewMeshFactory.MaxTrianglesPerUnit ? " OK" : " **超预算**"));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failures > 0)
                Debug.LogError(report.ToString());
            else
                Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------
        // 网格资产
        // ------------------------------------------------------------------

        static Dictionary<string, Mesh> BuildMeshAssets(Dictionary<string, MeshData> meshData)
        {
            var result = new Dictionary<string, Mesh>(meshData.Count);
            foreach (KeyValuePair<string, MeshData> pair in meshData)
            {
                string path = MeshFolder + "/" + pair.Key + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    CrewMeshFactory.ApplyToMesh(existing, pair.Value);
                    EditorUtility.SetDirty(existing);
                    result[pair.Key] = existing;
                    continue;
                }

                Mesh mesh = CrewMeshFactory.CreateMesh(pair.Key, pair.Value);
                AssetDatabase.CreateAsset(mesh, path);
                result[pair.Key] = mesh;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 材质资产
        // ------------------------------------------------------------------

        static Material[] BuildMaterialAssets()
        {
            var roles = System.Enum.GetValues(typeof(CrewMaterialRole));
            var materials = new Material[roles.Length];

            Shader outlineShader = Shader.Find(OutlineShaderName);
            if (outlineShader == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 未找到 shader " + OutlineShaderName
                    + "，退回 URP/Lit（角色将没有描边，描边验收会失败）。");
                outlineShader = Shader.Find("Universal Render Pipeline/Lit");
            }

            foreach (CrewMaterialRole role in roles)
            {
                string path = CrewMaterialFolder + "/" + CrewVisualCatalog.MaterialFileName(role) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(outlineShader) { name = CrewVisualCatalog.MaterialFileName(role) };
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (outlineShader != null && material.shader != outlineShader)
                {
                    material.shader = outlineShader;
                }

                ApplyOutlineUnitMaterial(material, CrewVisualCatalog.RoleColor(role));
                EditorUtility.SetDirty(material);
                materials[(int)role] = material;
            }

            return materials;
        }

        /// <summary>
        /// 配置"单位描边材质"参数：本体色 + 描边三档（state0 不可见 / hover 白细 / selected 青粗虚线）。
        /// 参数值与 <c>M2BattleSceneSetup.EnsureOutlineMaterial</c> 保持同源（该方法是私有的，故此处镜像；
        /// 若那边调参，这里必须同步，口径见 docs/描边Shader调试.md）。
        /// </summary>
        static void ApplyOutlineUnitMaterial(Material material, Color baseColor)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);

            // state=0 用兜底色，alpha=0 → 片元 discard，未选中的单位不顶青边。
            if (material.HasProperty("_OutlineColor"))
                material.SetColor("_OutlineColor", new Color(0.286f, 0.851f, 0.839f, 0f));
            if (material.HasProperty("_OutlineColorHover"))
                material.SetColor("_OutlineColorHover", new Color(1f, 1f, 1f, 0.22f));
            if (material.HasProperty("_OutlineColorSelected"))
                material.SetColor("_OutlineColorSelected", new Color(0.286f, 0.851f, 0.839f, 0.949f));

            if (material.HasProperty("_OutlineWidth"))
                material.SetFloat("_OutlineWidth", 0.006f);
            if (material.HasProperty("_OutlineWidthHover"))
                material.SetFloat("_OutlineWidthHover", 0.0025f);
            if (material.HasProperty("_OutlineWidthSelected"))
                material.SetFloat("_OutlineWidthSelected", 0.006f);
            if (material.HasProperty("_OutlineState"))
                material.SetFloat("_OutlineState", 0f);
            if (material.HasProperty("_OutlineAlpha"))
                material.SetFloat("_OutlineAlpha", 1f);
            if (material.HasProperty("_OutlineExpandMode"))
                material.SetFloat("_OutlineExpandMode", 0f);
            if (material.HasProperty("_OutlineDistanceAttenuation"))
                material.SetFloat("_OutlineDistanceAttenuation", 0.4f);
            if (material.HasProperty("_DashSpeed"))
                material.SetFloat("_DashSpeed", 5f);
            if (material.HasProperty("_DashFrequency"))
                material.SetFloat("_DashFrequency", 50f);
            if (material.HasProperty("_DebugMode"))
                material.SetFloat("_DebugMode", 0f);
        }

        static CrewVisualAssetSet BuildAssetSet(Dictionary<string, Mesh> meshes, Material[] materials)
        {
            return new CrewVisualAssetSet
            {
                Sphere = meshes[CrewMeshLibrary.Sphere],
                SphereSmall = meshes[CrewMeshLibrary.SphereSmall],
                Cylinder = meshes[CrewMeshLibrary.Cylinder],
                Capsule = meshes[CrewMeshLibrary.Capsule],
                Box = meshes[CrewMeshLibrary.Box],
                TorsoStandard = meshes[CrewMeshLibrary.TorsoStandard],
                TorsoWide = meshes[CrewMeshLibrary.TorsoWide],
                TorsoNarrow = meshes[CrewMeshLibrary.TorsoNarrow],
                TorsoRib = meshes[CrewMeshLibrary.TorsoRib],
                TorsoCaptain = meshes[CrewMeshLibrary.TorsoCaptain],
                BandanaStandard = meshes[CrewMeshLibrary.BandanaStandard],
                BandanaTight = meshes[CrewMeshLibrary.BandanaTight],
                BandanaLow = meshes[CrewMeshLibrary.BandanaLow],
                Tricorn = meshes[CrewMeshLibrary.Tricorn],
                CoatSkirt = meshes[CrewMeshLibrary.CoatSkirt],
                Hook = meshes[CrewMeshLibrary.HookMesh],
                Rib = meshes[CrewMeshLibrary.RibMesh],
                Beard = meshes[CrewMeshLibrary.Beard],
                HairClump = meshes[CrewMeshLibrary.HairClump],
                Materials = materials,
            };
        }

        // ------------------------------------------------------------------
        // 预制体
        // ------------------------------------------------------------------

        static GameObject BuildProfessionPrefab(CrewProfession profession, CrewVisualAssetSet assetSet,
            out int rendererCount, out int triangles)
        {
            rendererCount = 0;
            triangles = 0;

            string fileName = CrewVisualCatalog.PrefabFileName(profession);
            string path = CrewPrefabFolder + "/" + fileName + ".prefab";

            var root = new GameObject("Crew_" + fileName);
            root.transform.localScale = UnitRootScale;

            var collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one;      // §4.1：AABB 12×16px → 世界 0.375×0.5×0.375

            var body = root.AddComponent<Rigidbody>();
            body.mass = 1f;                   // §4.1 weight = 1
            body.drag = 0f;
            body.angularDrag = 0.05f;
            body.useGravity = true;

            var pirate = root.AddComponent<PirateBase>();
            root.AddComponent<UnitOutlineBinder>();
            var animator = root.AddComponent<CrewVisualAnimator>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            CrewVisualRig rig = CrewVisualRig.Build(visual.transform, profession, assetSet);

            // 统计实测三角面（用网格资产数据算，不靠估算）。
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null)
                    triangles += mesh.triangles.Length / 3;
            }
            rendererCount = root.GetComponentsInChildren<MeshRenderer>(true).Length;

            // 接线（PirateBase.body / bodyCollider / 表现层引用）。
            var so = new SerializedObject(pirate);
            SetRef(so, "body", body);
            SetRef(so, "bodyCollider", collider);
            SetRef(so, "visualAnimator", animator);
            SetString(so, "crewType", RepresentativeSymbol(profession));
            so.ApplyModifiedPropertiesWithoutUndo();

            var animatorSo = new SerializedObject(animator);
            SetRef(animatorSo, "rig", rig);
            SetRef(animatorSo, "pirate", pirate);
            animatorSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 保存预制体失败: " + path);
                return null;
            }
            return prefab;
        }

        /// <summary>职业的默认战斗导出符号（运行时由 PirateBase.Initialize 覆盖，仅作 prefab 默认值）。</summary>
        static string RepresentativeSymbol(CrewProfession profession)
        {
            switch (profession)
            {
                case CrewProfession.Sailor: return "redPirate";
                case CrewProfession.Bombardier: return "soldier";
                case CrewProfession.Sniper: return "femalePirate";
                case CrewProfession.Hook: return "blindPirate";
                case CrewProfession.Arsonist: return "oldPirate";
                case CrewProfession.Skeleton: return "skeletonPirate";
                case CrewProfession.Captain: return "redPirateCaptain";
                default: return "redPirate";
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 找不到序列化字段: " + name);
                return;
            }
            prop.objectReferenceValue = value;
        }

        static void SetString(SerializedObject so, string name, string value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
                prop.stringValue = value;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
