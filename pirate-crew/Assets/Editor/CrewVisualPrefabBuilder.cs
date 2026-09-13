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
    /// ├ ContactShadow（接触阴影面片，y=−0.46 → 脚底上方 0.02；见 §N7）
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
    /// 【本脚本承担的三处 r3 复验整改】
    ///   N7 接触阴影：脚底半球透明径向渐变面片（贴图 64² 程序化生成，中心 α=0.45），
    ///      挂在**单位根**下名为 <c>ContactShadow</c> → 跟随位移，零运行时代码（判据 A-6）。
    ///   N19 四肢写实化：腿由"等径圆柱"改**上粗下细圆台**（8 段）+ 加厚前出头靴块；
    ///      臂同样锥化，手由小球改**手掌块**；靴子另给深棕材质 <c>CrewBoot</c>（骷髅的骨色靴不动）。
    ///      做法是装配后只换 <c>MeshFilter.sharedMesh</c> 与零件缩放，**不动枢轴层级**
    ///      → <c>CrewVisualAnimator</c> 的动画链路零影响（R-3 三角面反而下降：圆台 32 tri < 圆柱 40 tri）。
    ///   阵营色接线：<c>CrewTeamTintPart</c> 与 <c>CrewVisualRig</c> 同处一个 .cs，Unity 只给
    ///      "类名 == 文件名"的类生成 MonoScript → 该标记进预制体必然变成 **missing script**（实测 7 个
    ///      预制体共 26 处），运行时一个都收不到，蓝队会顶着红队底色。故建预制体时把标记对应的 renderer
    ///      列表**显式写进 <c>UnitOutlineBinder.teamTintRenderers</c>**，并把标记组件删掉（不再留 missing script）。
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
        const string CrewTextureFolder = "Assets/Art/Textures/Crew";
        const string CrewPrefabFolder = "Assets/Prefabs/PirateCrew/Crew";

        const string OutlineShaderName = "PirateCrew/PirateOutline";
        const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        /// <summary>单位根缩放：与 PirateBase.prefab 一致，使 BoxCollider(1,1,1) 的世界 AABB = 0.375×0.5×0.375。</summary>
        static readonly Vector3 UnitRootScale = new Vector3(12f / 32f, 16f / 32f, 12f / 32f);

        // ------------------------------------------------------------------
        // 本波次追加资产（键名只在本脚本内使用；**不登记进 CrewMeshLibrary**，
        // 以免动到预算镜像表 CrewMeshLibrary.CountPartInstances 的既有断言）
        // ------------------------------------------------------------------

        const string LegTaperKey = "CrewLegTaper";
        const string ArmTaperKey = "CrewArmTaper";
        const string ContactShadowQuadKey = "CrewContactShadowQuad";

        const string BootMaterialFileName = "CrewBoot";
        const string ContactShadowMaterialFileName = "CrewContactShadow";
        const string ContactShadowTextureFileName = "CrewContactShadow.png";

        /// <summary>四肢锥化分段（8 段足够低模，且比原圆柱的 10 段更省面）。</summary>
        const int LimbTaperSides = 8;

        /// <summary>腿锥化：上（胯）半径系数，配装配侧 (r, h, r) 缩放 → 上半径 = r。</summary>
        const float LegTaperTopRadius = 1.0f;

        /// <summary>腿锥化：下（脚踝）半径系数 → 下半径 = 0.72r（"上粗下细"，替换原等径圆柱的"细棍"观感）。</summary>
        const float LegTaperBottomRadius = 0.72f;

        /// <summary>臂锥化：下（腕）半径系数 → 腕半径 = 0.78r。</summary>
        const float ArmTaperBottomRadius = 0.78f;

        /// <summary>靴块高度（世界单位；原 0.028）。</summary>
        const float BootHeight = 0.034f;

        /// <summary>靴块宽 = 腿半径 × 该系数（原 2.4）。</summary>
        const float BootWidthMul = 2.9f;

        /// <summary>靴块深 = 腿半径 × 该系数（原 3.4）——加深是"靴"而非"脚垫"的关键。</summary>
        const float BootDepthMul = 4.1f;

        /// <summary>靴尖前伸（世界单位，+Z 为角色正面）。</summary>
        const float BootForward = 0.014f;

        const float HandWidthMul = 2.2f;
        const float HandHeightMul = 1.7f;
        const float HandDepthMul = 1.85f;

        /// <summary>深棕靴色 #4A3021【提案】（N19："深棕靴"；比 Leather #8B5E3C 更暗，与沙地拉开明度）。</summary>
        static readonly Color BootColor = new Color(0.290f, 0.188f, 0.129f, 1f);

        /// <summary>接触阴影面片色：近黑 + 中心 α 0.45（边缘 α 由贴图径向渐变收到 0）。</summary>
        static readonly Color ContactShadowColor = new Color(0.015f, 0.015f, 0.020f,
            ContactShadowDecal.DefaultCenterAlpha);

        // ------------------------------------------------------------------
        // 追加资产打包
        // ------------------------------------------------------------------

        /// <summary>本波次新增的网格/材质句柄（生成后注入 <see cref="BuildProfessionPrefab"/>）。</summary>
        struct VisualAddOns
        {
            public Mesh LegTaper;
            public Mesh ArmTaper;
            public Mesh ContactShadowQuad;
            public Material BootMaterial;
            public Material ContactShadowMaterial;
        }

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
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder(CrewTextureFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/PirateCrew");
            EnsureFolder(CrewPrefabFolder);

            Dictionary<string, MeshData> meshData = CrewMeshLibrary.BuildAll();
            InjectAddOnMeshes(meshData);
            Dictionary<string, Mesh> meshes = BuildMeshAssets(meshData);
            Material[] materials = BuildMaterialAssets();
            VisualAddOns addOns = BuildAddOns(meshes);
            CrewVisualAssetSet assetSet = BuildAssetSet(meshes, materials);

            var report = new System.Text.StringBuilder();
            report.AppendLine("[CrewVisualPrefabBuilder] 职业视觉预制体生成完成：");
            report.AppendLine("  网格资产: " + MeshFolder + "（" + meshes.Count + " 个）");
            report.AppendLine("  材质资产: " + CrewMaterialFolder + "（" + materials.Length + " 个 + 靴/接触阴影 2 个）");

            int failures = 0;
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                CrewProfession profession = CrewVisualCatalog.AllProfessions[i];
                GameObject prefab = BuildProfessionPrefab(profession, assetSet, addOns, out int renderers, out int triangles);
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
        // 追加网格（N7 / N19）
        // ------------------------------------------------------------------

        /// <summary>
        /// 往网格表里追加本波次新增的零件：
        /// · 腿/臂"上粗下细"圆台——与单位 <c>Cylinder</c> 同口径（半径 1 / 高 1、沿 Y 居中），
        ///   所以装配侧既有的 (r, h, r) 缩放、枢轴位置、动画插值全部无需改动，只换 sharedMesh。
        /// · 接触阴影面片——1×1 的 XY 面（法线 +Z），预制体里绕 X 转 −90° 平铺，再缩放到 0.6 单位。
        /// </summary>
        static void InjectAddOnMeshes(Dictionary<string, MeshData> meshData)
        {
            meshData[LegTaperKey] = CrewMeshFactory.Frustum(
                LegTaperTopRadius, LegTaperBottomRadius, 1f, LimbTaperSides);
            meshData[ArmTaperKey] = CrewMeshFactory.Frustum(
                LegTaperTopRadius, ArmTaperBottomRadius, 1f, LimbTaperSides);
            meshData[ContactShadowQuadKey] = BuildQuadMeshData();
        }

        static MeshData BuildQuadMeshData()
        {
            var vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            var uvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            var triangles = new[] { 0, 1, 2, 0, 2, 3 };   // 逆时针 → 正面朝 +Z
            return new MeshData(vertices, normals, uvs, triangles);
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

            Shader outlineShader = ResolveOutlineShader();

            foreach (CrewMaterialRole role in roles)
            {
                string path = CrewMaterialFolder + "/" + CrewVisualCatalog.MaterialFileName(role) + ".mat";
                Material material = LoadOrCreateMaterial(path, CrewVisualCatalog.MaterialFileName(role), outlineShader);
                ApplyOutlineUnitMaterial(material, CrewVisualCatalog.RoleColor(role));
                EditorUtility.SetDirty(material);
                materials[(int)role] = material;
            }

            return materials;
        }

        static VisualAddOns BuildAddOns(Dictionary<string, Mesh> meshes)
        {
            return new VisualAddOns
            {
                LegTaper = meshes[LegTaperKey],
                ArmTaper = meshes[ArmTaperKey],
                ContactShadowQuad = meshes[ContactShadowQuadKey],
                BootMaterial = BuildBootMaterial(),
                ContactShadowMaterial = BuildContactShadowMaterial(),
            };
        }

        /// <summary>深棕靴材质（N19）：与其它角色材质同走 PirateOutline（否则靴子没有描边，破 R-9）。</summary>
        static Material BuildBootMaterial()
        {
            string path = CrewMaterialFolder + "/" + BootMaterialFileName + ".mat";
            Material material = LoadOrCreateMaterial(path, BootMaterialFileName, ResolveOutlineShader());
            ApplyOutlineUnitMaterial(material, BootColor);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 接触阴影材质（N7）：URP/Unlit 透明，贴图 = 程序化径向渐变，颜色 = 近黑（α 由贴图 × _BaseColor.a）。
        /// 用 Unlit 而非 Lit，因为阴影面片不该再被主光照亮一次（否则逆光时会变成一块亮斑）。
        /// </summary>
        static Material BuildContactShadowMaterial()
        {
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder(CrewTextureFolder);
            Texture2D radial = BuildContactShadowTexture();

            Shader unlit = Shader.Find(UnlitShaderName);
            if (unlit == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 未找到 shader " + UnlitShaderName
                    + "，接触阴影退回 Unlit/Transparent（贴图/颜色仍生效）。");
                unlit = Shader.Find("Unlit/Transparent");
            }

            string path = CrewMaterialFolder + "/" + ContactShadowMaterialFileName + ".mat";
            Material material = LoadOrCreateMaterial(path, ContactShadowMaterialFileName, unlit);

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", radial);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", radial);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", ContactShadowColor);

            // URP/Unlit 的透明档（等价于 Inspector 里把 Surface Type 切成 Transparent、Blending 选 Alpha）。
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 程序化生成 64² 径向渐变贴图（白 RGB + 中心 α=1 → 边缘 α=0 的 smoothstep），落成 PNG 资产。
        ///
        /// 【为什么用贴图而不是顶点色】免贴图方案要靠 shader 读顶点色，而 URP/Unlit 不读顶点色、
        /// 自写 shader 又超出本波次允许改动的文件范围；64² 单通道遮罩成本可忽略，且贴图可手改替换。
        /// </summary>
        static Texture2D BuildContactShadowTexture()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size - 0.5f;
                    float d = Mathf.Clamp01(new Vector2(u, v).magnitude / 0.5f);  // 0=中心, 1=外接圆
                    float t = 1f - d;
                    float alpha = t * t * (3f - 2f * t);                          // smoothstep 径向渐变
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CrewContactShadow" };
            texture.SetPixels32(pixels);
            texture.Apply();

            string path = CrewTextureFolder + "/" + ContactShadowTextureFileName;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;          // 贴地小面片，不需要 mip
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = false;            // 它是 α 遮罩：不做 sRGB→linear，否则渐变边缘会变硬
                importer.maxTextureSize = size;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Shader ResolveOutlineShader()
        {
            Shader outlineShader = Shader.Find(OutlineShaderName);
            if (outlineShader == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 未找到 shader " + OutlineShaderName
                    + "，退回 URP/Lit（角色将没有描边，描边验收会失败）。");
                outlineShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            return outlineShader;
        }

        static Material LoadOrCreateMaterial(string path, string materialName, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
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
            VisualAddOns addOns, out int rendererCount, out int triangles)
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
            var binder = root.AddComponent<UnitOutlineBinder>();
            var animator = root.AddComponent<CrewVisualAnimator>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            CrewVisualRig rig = CrewVisualRig.Build(visual.transform, profession, assetSet);

            // N19：腿/臂锥化 + 靴块/手掌块（只换 mesh 与零件缩放，不动枢轴 → 动画链路零影响）。
            ApplyRealisticLimbs(visual.transform, assetSet, addOns);

            // N7：脚底接触阴影面片（单位根的子物体 → 跟随位移，零运行时代码）。
            AddContactShadow(root.transform, addOns);

            // 阵营色部件：显式写进 binder，并拆掉必然变成 missing script 的运行时标记组件。
            Renderer[] tintRenderers = CollectAndStripTintMarkers(root);

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

            // 描边 binder：显式阵营色部件表（见类头"阵营色接线"）。
            var binderSo = new SerializedObject(binder);
            SetRendererArray(binderSo, "teamTintRenderers", tintRenderers);
            binderSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 保存预制体失败: " + path);
                return null;
            }
            return prefab;
        }

        // ------------------------------------------------------------------
        // N19：四肢写实化（装配后换网格/缩放，不改枢轴）
        // ------------------------------------------------------------------

        /// <summary>
        /// 腿/臂由等径圆柱改"上粗下细"圆台，靴块加厚前出头，手由小球改手掌块。
        /// 只写 <c>MeshFilter.sharedMesh</c> 与零件 <c>localScale/localPosition</c>：
        /// 枢轴层级、动画插值的基准值（<c>CrewVisualAnimator.Awake</c> 记的是枢轴，不是零件）都不受影响。
        /// </summary>
        static void ApplyRealisticLimbs(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns)
        {
            ApplyLeg(visual, assets, addOns, "LegL", "BootL");
            ApplyLeg(visual, assets, addOns, "LegR", "BootR");
            ApplyArm(visual, assets, addOns, "ArmL", "HandL");
            ApplyArm(visual, assets, addOns, "ArmR", "HandR");
        }

        static void ApplyLeg(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns,
            string legName, string bootName)
        {
            Transform leg = FindDeep(visual, legName);
            if (leg == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 找不到腿部零件 " + legName + "，N19 锥化跳过该部件。");
                return;
            }

            float radius = leg.localScale.x;
            float height = leg.localScale.y;
            var legFilter = leg.GetComponent<MeshFilter>();
            if (legFilter != null)
                legFilter.sharedMesh = addOns.LegTaper;     // 与 Cylinder 同口径（半径 1/高 1），缩放语义不变
            leg.localScale = new Vector3(radius, height, radius);

            Transform boot = FindDeep(visual, bootName);
            if (boot == null)
                return;

            // 靴块：高 0.034、宽 2.9r、深 4.1r、向 +Z 前伸 —— 从"脚垫"变成能读出朝向的靴子。
            boot.localScale = new Vector3(radius * BootWidthMul, BootHeight, radius * BootDepthMul);
            boot.localPosition = new Vector3(0f, BootHeight * 0.5f, BootForward);

            var bootRenderer = boot.GetComponent<MeshRenderer>();
            if (bootRenderer != null && bootRenderer.sharedMaterial == assets.For(CrewMaterialRole.Leather))
            {
                // 只有默认皮革靴换深棕；骷髅的靴子是骨色（R-11 无皮肤色纪律之外的骨色体系）保持不动。
                bootRenderer.sharedMaterial = addOns.BootMaterial;
            }
        }

        static void ApplyArm(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns,
            string armName, string handName)
        {
            Transform arm = FindDeep(visual, armName);
            if (arm == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 找不到手臂零件 " + armName + "，N19 锥化跳过该部件。");
                return;
            }

            var armFilter = arm.GetComponent<MeshFilter>();
            float height = arm.localScale.y;
            // 胶囊网格（狙击手细臂）的半径约定是 0.5，圆柱是 1.0：换网格前先把半径换算回世界值，
            // 否则狙击手的臂会粗一倍。
            bool capsuleMesh = armFilter != null && armFilter.sharedMesh == assets.Capsule;
            float radius = capsuleMesh ? arm.localScale.x * 0.5f : arm.localScale.x;
            if (armFilter != null)
                armFilter.sharedMesh = addOns.ArmTaper;
            arm.localScale = new Vector3(radius, height, radius);

            Transform hand = FindDeep(visual, handName);
            if (hand == null)
                return;

            float handRadius = hand.localScale.x;      // SphereSmall 是半径 1 的球，scale.x 即手半径
            var handFilter = hand.GetComponent<MeshFilter>();
            if (handFilter != null)
                handFilter.sharedMesh = assets.Box;    // 球手 → 手掌块（写实向的低模拳头）
            hand.localScale = new Vector3(handRadius * HandWidthMul,
                handRadius * HandHeightMul, handRadius * HandDepthMul);
        }

        // ------------------------------------------------------------------
        // N7：脚底接触阴影面片
        // ------------------------------------------------------------------

        /// <summary>
        /// 在单位根下挂"ContactShadow"面片（判据 A-6 / Art Bible §4.1 `:239`）。
        ///
        /// 【贴地口径】单位根的 local y = −0.5 就是脚底平面：脚底 world y = rootY + rootScale.y × Visual.localY
        /// = rootY − 0.25，而 rootScale.y = 0.5 → 局部 −0.5。出生逻辑把单位摆在平台顶面，故这里固定
        /// −0.5 + 0.02/0.5（抬高 0.02 世界单位防 z-fighting），不需要每帧查询地形高度。
        ///
        /// 【缩放口径】根是非均匀缩放（0.375/0.5/0.375）；面片绕 X 转 −90° 平铺后，面内 X/Z 都被 0.375 缩放，
        /// 故 X/Y 用同一系数 → 仍是正圆，且不产生剪切（旋转只把局部 Y 轴映射到世界 Z 轴）。
        /// </summary>
        static MeshRenderer AddContactShadow(Transform root, VisualAddOns addOns)
        {
            var go = new GameObject("ContactShadow");
            go.transform.SetParent(root, false);

            float planarScale = ContactShadowDecal.DefaultDiameter / UnitRootScale.x;
            go.transform.localPosition = new Vector3(0f,
                -0.5f + ContactShadowDecal.DefaultGroundOffset / UnitRootScale.y, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(planarScale, planarScale, 1f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = addOns.ContactShadowQuad;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = addOns.ContactShadowMaterial;
            // 面片自身不投影、不接收阴影/探针：它只是一层"压暗"叠加，不参与光照链路。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var decal = go.AddComponent<ContactShadowDecal>();
            var decalSo = new SerializedObject(decal);
            SetFloat(decalSo, "diameter", ContactShadowDecal.DefaultDiameter);
            SetFloat(decalSo, "groundOffset", ContactShadowDecal.DefaultGroundOffset);
            SetFloat(decalSo, "centerAlpha", ContactShadowColor.a);
            decalSo.ApplyModifiedPropertiesWithoutUndo();

            return renderer;
        }

        // ------------------------------------------------------------------
        // 阵营色接线（修 missing script 根因）
        // ------------------------------------------------------------------

        /// <summary>
        /// 收集"阵营色部件"的 renderer 并**删除标记组件**，返回的列表写进
        /// <c>UnitOutlineBinder.teamTintRenderers</c>。
        ///
        /// 【为什么要删标记】<see cref="CrewTeamTintPart"/> 定义在 <c>CrewVisualRig.cs</c> 里，
        /// 而 Unity 的脚本导入器只给"类名 == 文件名"的类生成 MonoScript 子资产；标记类拿不到
        /// MonoScript，序列化时 <c>m_Script</c> 只能写 fileID 0 —— 进预制体就是 missing script
        /// （控制台报 "referenced script is missing"、运行时 <c>GetComponentsInChildren</c> 收不到）。
        /// 实测 7 个预制体 26 处，是 r3「蓝队顶着红队底色」的根因。
        /// 删掉它、改走显式引用后，预制体不再有 missing script，也不再依赖运行时类型。
        /// </summary>
        static Renderer[] CollectAndStripTintMarkers(GameObject root)
        {
            var markers = root.GetComponentsInChildren<CrewTeamTintPart>(true);
            var tintRenderers = new List<Renderer>(markers.Length);
            for (int i = 0; i < markers.Length; i++)
            {
                var renderer = markers[i].GetComponent<Renderer>();
                if (renderer != null)
                    tintRenderers.Add(renderer);
                Object.DestroyImmediate(markers[i]);
            }
            return tintRenderers.ToArray();
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

        static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
                prop.floatValue = value;
        }

        static void SetRendererArray(SerializedObject so, string name, Renderer[] value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 找不到序列化数组字段: " + name);
                return;
            }
            prop.arraySize = value.Length;
            for (int i = 0; i < value.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = value[i];
        }

        /// <summary>按名字深度优先找子物体（装配层没有全局索引，零件名在职业间是稳定的）。</summary>
        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
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
