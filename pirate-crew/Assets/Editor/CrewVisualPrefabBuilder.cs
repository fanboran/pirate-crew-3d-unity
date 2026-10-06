using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Visual;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 角色视觉预制体生成器：程序化产出 7 个职业预制体（六职业 + 船长），
    /// 配套网格资产（<c>Assets/Art/Models/Generated/</c>）与共享材质（<c>Assets/Art/Materials/Crew/</c>）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/角色/生成船员视觉预制体
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.CrewVisualPrefabBuilder.BuildAll
    ///
    /// 【造型口径 — 用户裁决 2026-09-14；尺度口径 2026-09-14 修正（用户裁决②"角色为什么比木筏子小这么多"）】
    ///   **单一外观档**（2026-10-05 创始人裁决：职业外观塌缩，7 档目录根除）：
    ///   一个圆台柱 Body + 一个圆球 Head。
    ///
    ///   **造型规格单一真源 = <see cref="CrewMeshLibrary"/>**（顶/底半径 0.30、身高 1.3215、
    ///   头球 r 0.30、总高 2.0321 与分段数，2026-09-29 调试场定档）——规格常量与两件式网格
    ///   生成已下沉到该库（无头验证台直接可测），本文件只消费，不私藏尺寸。
    ///   （历史推导链：归一基准 1.85 与换算系数 k=1 的 Machinery 已随下沉删除，
    ///   演进史见 <c>docs/设计/美术/角色造型.md</c> 与 CrewMeshLibrary 各常量注释。）
    ///   材质：Head = 木色 <c>#D4A76A</c>（CrewWood），
    ///   Body = 阵营色（运行时由 UnitOutlineBinder 逐队写 <c>_BaseColor</c>）；
    ///   全部 Crew 材质走**像素化路径**物体 shader（<see cref="PixelartPath.ObjectShaderName"/>，
    ///   配方唯一来源 = <see cref="PixelartMaterialFactory"/>）——反壳描边（PirateOutline）已退役，
    ///   选中/悬停反馈由像素路径的屏幕空间描边承担。
    ///   职业差异只在数据（攻/防/技能）与 HUD，**不体现在造型上**（见 docs/设计/美术/角色造型.md）。
    ///
    /// 【为什么删掉了腿/靴/臂/掌/三角帽/头巾/发/鼻/眼/手持武器】那些零件是此前多轮复验里
    ///   **AI 自主加件**的产物（"评委"式跑偏），用户从未要求；用户原话是"一个球加一个梯形"，
    ///   并明确"我要圆球+圆台柱那种"。故装配代码整段移除
    ///   （见 <see cref="ApplyTwoPieceSilhouette"/>）；网格资产与 <c>CrewMeshLibrary</c> 的键
    ///   **不删库**（生成链路与三角面预算镜像表仍在），只是预制体里不再装配它们。
    ///
    /// 【预制体结构】
    /// <code>
    /// Crew（根）
    /// ├ Transform.scale = (0.375, 0.5, 0.375)   ← 与 PirateBase.prefab 一致
    /// ├ BoxCollider size=(1,1,1)                  ← 世界 AABB 0.375×0.5×0.375（碰撞契约不变）
    /// ├ Rigidbody mass=1 drag=0 angularDrag=0.05 useGravity=on
    /// ├ PirateBase / UnitOutlineBinder / CrewVisualAnimator
    /// └ Visual（CrewVisualRig）
    ///    └ BodyPivot（脚底枢轴：整身 bob / 倒地 / 落水下沉）
    ///       └ TorsoPivot（与 BodyPivot 同在脚底：呼吸缩放 + 前倾）
    ///          ├ Body（圆台柱 renderer，阵营色）
    ///          └ HeadPivot（球心 y=1.50：点头 / 呼吸浮动）
    ///             └ Head（圆球 renderer，木色）
    /// </code>
    ///   BodyPivot / TorsoPivot / HeadPivot 三个动画枢轴是 <c>CrewVisualAnimator</c> 的唯一接口
    ///   （它只读写这三个枢轴并对臂/腿/手持物挂点做 null 判断），故两件式下动画链路零改动。
    ///
    /// 【为什么不替换 PirateBase.prefab 的 Cube 外观】<c>BattleSceneSetup.BuildAll</c> 会重建
    ///   PirateBase.prefab 为 Cube；把职业外观塞进去会与该重建互相覆盖。这里改为**独立职业预制体**，
    ///   由 <c>BattleController.crewVisualPrefab</c> 单字段接线选择，
    ///   未命中/未接线时回落 PirateBase.prefab（对方块外观做兜底，不破坏既有场景）。
    ///
    /// 【坐标口径】单位根是 0.375/0.5/0.375 的非均匀缩放，Visual 用 (1/0.375, 1/0.5, 1/0.375) 抵消，
    ///   于是 **Visual 局部 1 单位 = 世界 1 单位、XZ 不畸变**；又因 Visual.localPosition.y = −0.5 而
    ///   rootScale.y = 0.5，得 `worldY = visualLocalY − 0.25` → 以"脚底 = 0、总高 = 1.85"的口径看，
    ///   **Visual 局部 y 就等于脚底起算的世界 y**，故本文件里的尺寸常量可直接用世界值。
    ///
    /// 【幂等】
    ///   · 网格资产存在则**就地刷新**（<c>ApplyToMesh</c>，不换 GUID、不丢引用）；
    ///   · 材质存在则就地更新 shader 与配方参数（不新建重名副本）；
    ///   · 预制体按固定路径覆盖保存。
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

        /// <summary>单位根缩放：与 PirateBase.prefab 一致，使 BoxCollider(1,1,1) 的世界 AABB = 0.375×0.5×0.375。</summary>
        static readonly Vector3 UnitRootScale = new Vector3(12f / 32f, 16f / 32f, 12f / 32f);


        // ------------------------------------------------------------------
        // 本文件追加的资产键（**不登记进 CrewMeshLibrary**，
        // 以免动到预算镜像表 CrewMeshLibrary.CountPartInstances 的既有断言）
        // ------------------------------------------------------------------

        // 两件式网格键改挂 CrewMeshLibrary 公共常量（值 = 网格资产文件名，与磁盘资产一致）：
        // Body = "CrewBodyFrustum"（顶 r 0.30 / 底 r 0.30 / h 1.3215，16 段）、Head = "CrewHeadSphere"（r 0.30，12×8）。

        // ------------------------------------------------------------------
        // 选中反馈（像素路径）：Crew 材质不再携带描边壳参数
        // 反壳描边（PirateOutline + _OutlineState/_DashFrequency/_OutlineWidth* 一族）
        // 已随 PBR 根除退役，反馈由像素路径的屏幕空间描边承担；材质侧只保留
        // 物体配方（PixelartMaterialFactory.Configure）。
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 追加资产打包
        // ------------------------------------------------------------------

        /// <summary>本文件生成的两件式网格件（生成后注入 <see cref="BuildCrewPrefab"/>）。</summary>
        struct VisualAddOns
        {
            public Mesh BodyFrustum;
            public Mesh HeadSphere;
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
            Dictionary<string, Mesh> meshes = BuildMeshAssets(meshData);
            Material[] materials = BuildMaterialAssets();
            VisualAddOns addOns = BuildAddOns(meshes);
            CrewVisualAssetSet assetSet = BuildAssetSet(meshes, materials);

            var report = new System.Text.StringBuilder();
            report.AppendLine("[CrewVisualPrefabBuilder] 船员视觉预制体生成完成（单一外观档；用户裁决：两件式 = 圆球 + 圆台柱）：");
            report.AppendLine("  网格资产: " + MeshFolder + "（" + meshes.Count + " 个）");
            report.AppendLine("  材质资产: " + CrewMaterialFolder + "（" + materials.Length + " 个 + 接触阴影 1 个）");
            report.AppendLine("  Body 圆台柱: 顶 r " + CrewMeshLibrary.BodyTopRadius.ToString("0.00000")
                + " / 底 r " + CrewMeshLibrary.BodyBottomRadius.ToString("0.00000")
                + " / h " + CrewMeshLibrary.BodyHeight.ToString("0.00000") + "（" + CrewMeshLibrary.BodySides + " 段）");
            report.AppendLine("  Head 圆球: r " + CrewMeshLibrary.HeadSphereRadius.ToString("0.00000")
                + " / 球心 y " + CrewMeshLibrary.HeadSphereCenterY.ToString("0.00000")
                + "（" + CrewMeshLibrary.HeadSegments + "×" + CrewMeshLibrary.HeadRings + "）");

            GameObject crew = BuildCrewPrefab(assetSet, addOns, out int renderers, out int triangles);
            int failures = 0;
            if (crew == null)
            {
                failures++;
                report.AppendLine("  [Crew] 生成失败");
            }
            else
            {
                report.AppendLine("  [Crew] "
                    + "部件 renderer=" + renderers + "（Body+Head）"
                    + " tri=" + triangles + "（含接触阴影 2）"
                    + " 预算 " + CrewMeshFactory.MaxTrianglesPerUnit
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

            foreach (CrewMaterialRole role in roles)
            {
                string fileName = CrewVisualCatalog.MaterialFileName(role);
                string path = CrewMaterialFolder + "/" + fileName + ".mat";

                // 像素化路径：材质本体换到物体 shader、配方写工厂默认值（色带 3 档 / 描边 1 艺术像素）。
                // 阵营色仍由 UnitOutlineBinder 运行时以 MPB 写 _BaseColor（像素 shader 同名属性，链路不变）。
                Shader pixelShader = ResolvePixelShader();
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    if (pixelShader == null)
                        continue;   // 物体 shader 缺失：该角色材质不落资产（工厂已报错），装配侧按 null 兜底
                    material = new Material(pixelShader) { name = fileName };
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (pixelShader != null)
                {
                    material.shader = pixelShader;   // 幂等重跑：把旧反壳描边材质换到本路径
                }

                PixelartMaterialFactory.Configure(material, CrewVisualCatalog.RoleColor(role));
                EditorUtility.SetDirty(material);
                materials[(int)role] = material;
            }

            return materials;
        }

        /// <summary>本路径物体 shader（缺失时报错返回 null，不静默拿别族 shader 顶上）。</summary>
        static Shader ResolvePixelShader()
        {
            Shader shader = Shader.Find(PixelartPath.ObjectShaderName);
            if (shader == null)
                Debug.LogError("[CrewVisualPrefabBuilder] 未找到 shader " + PixelartPath.ObjectShaderName
                    + "（被剔除/编译失败？），Crew 材质不换装。");
            return shader;
        }

        static VisualAddOns BuildAddOns(Dictionary<string, Mesh> meshes)
        {
            return new VisualAddOns
            {
                BodyFrustum = meshes[CrewMeshLibrary.BodyFrustum],
                HeadSphere = meshes[CrewMeshLibrary.HeadSphere],
            };
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

        /// <summary>船员视觉预制体文件名（不含扩展名）。单一外观档（2026-10-05 职业外观塌缩裁决）。</summary>
        const string CrewPrefabName = "Crew";

        static GameObject BuildCrewPrefab(CrewVisualAssetSet assetSet,
            VisualAddOns addOns, out int rendererCount, out int triangles)
        {
            rendererCount = 0;
            triangles = 0;

            string path = CrewPrefabFolder + "/" + CrewPrefabName + ".prefab";

            var root = new GameObject(CrewPrefabName);
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
            CrewVisualRig rig = CrewVisualRig.Build(visual.transform, assetSet);
            if (rig == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] CrewVisualRig.Build 失败: " + CrewPrefabName);
                Object.DestroyImmediate(root);
                return null;
            }

            // ---- 用户裁决 2026-09-14：塌缩成两件式（圆球 + 圆台柱）----
            // 保留 rig.Build 的枢轴接线（Body/TorsoPivot/HeadPivot 与动画器共用），
            // 只把"零件"部分整体换掉：删掉 legs/boots/arms/hands/hats/face/held 后重建 Body + Head。
            ApplyTwoPieceSilhouette(rig, assetSet, addOns);

            // 阵营色部件：显式写进 binder，并拆掉必然变成 missing script 的运行时标记组件。
            Renderer[] tintRenderers = CollectAndStripTintMarkers(root);
            if (tintRenderers.Length != 1)
                Debug.LogError("[CrewVisualPrefabBuilder] " + CrewPrefabName + " 的阵营色部件数 = "
                    + tintRenderers.Length + "（两件式口径下恒为 1 = Body），请检查 "
                    + "ApplyTwoPieceSilhouette 是否被改动。");

            // 统计实测三角面（用网格资产数据算，不靠估算）。
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null)
                    triangles += mesh.triangles.Length / 3;
            }

            // 部件 renderer 数：两件式下恒为 2（Body + Head），供判据 R-1 核对。
            var partRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < partRenderers.Length; i++)
            {
                if (partRenderers[i] != null)
                    rendererCount++;
            }
            if (rendererCount != 2)
                Debug.LogError("[CrewVisualPrefabBuilder] " + CrewPrefabName + " 的角色部件 renderer 数 = "
                    + rendererCount + "（两件式口径下恒为 2 = Body + Head），请检查 "
                    + "ApplyTwoPieceSilhouette 是否被改动。");


            // 接线（PirateBase.body / bodyCollider / 表现层引用）。
            var so = new SerializedObject(pirate);
            SetRef(so, "body", body);
            SetRef(so, "bodyCollider", collider);
            SetRef(so, "visualAnimator", animator);
            // 预制体默认符号（运行时由 PirateBase.Initialize 按出战数据覆盖；单一外观档恒红方水手）。
            SetString(so, "crewType", "redPirate");
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
        // 用户裁决 2026-09-14：两件式造型（圆台柱 Body + 圆球 Head）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把 <see cref="CrewVisualRig.Build"/> 装配出的整套零件**塌缩成两件式**：
        /// 一个圆台柱（Body，阵营色）+ 一个圆球（Head，木色），其余零件与臂/腿枢轴全部删除。
        ///
        /// 【为什么保留 rig.Build 再删】三个动画枢轴（BodyPivot / TorsoPivot / HeadPivot）的层级与序列化
        ///   接线由 rig 提供且被 <c>CrewVisualAnimator</c> 使用，重建一套层级反而容易漏接线；
        ///   这里沿用"rig 装配 + 后处理"的既有做法，只把零件层整体替换。
        ///
        /// 【尺寸与位置】
        ///   · Body：<see cref="CrewMeshLibrary.BodyFrustum"/> 网格按世界尺寸建模
        ///     （顶 r 0.30 / 底 r 0.30 / h 1.3215），缩放恒为 1，中心放在柱高的中点 →
        ///     底面恰在脚底 y=0（Visual 局部 y=0）。
        ///   · Head：<see cref="CrewMeshLibrary.HeadSphere"/> 网格半径 0.30，挂在 HeadPivot 下的原点 →
        ///     球心 y 1.7321（身体 1.3215 − 重叠 0.05 + 头半径 0.30 + 头颈间距 0.1606），
        ///     总高 = 1.7321 + 0.30 = 2.0321（= <see cref="CrewMeshLibrary.HeadSphereCenterY"/> + <see cref="CrewMeshLibrary.HeadSphereRadius"/>）。
        ///   · 枢轴归位：TorsoPivot 移到脚底（呼吸缩放/前倾都绕脚底，语义与原来一致），
        ///     HeadPivot 移到球心高度；BodyPivot 保持脚底（与 rig 建的一致）。
        ///
        /// 【动画影响】<c>CrewVisualAnimator</c> 用 <c>rig.ArmLPivot</c> 等做手臂/腿摆动，两件式下这些
        ///   引用为 null → 该组件已有 null 判断，行为退化为"无摆臂/无迈腿"，整身 bob / 前倾 / 倒地 /
        ///   落水下沉 / 呼吸全部照常（它们只依赖 Body/TorsoPivot/HeadPivot）。
        /// </summary>
        static void ApplyTwoPieceSilhouette(CrewVisualRig rig, CrewVisualAssetSet assets, VisualAddOns addOns)
        {
            Transform visual = rig.transform;
            Transform bodyPivot = rig.Body;
            Transform torsoPivot = rig.TorsoPivot;
            Transform headPivot = rig.HeadPivot;

            if (visual == null || bodyPivot == null || torsoPivot == null || headPivot == null
                || addOns.BodyFrustum == null || addOns.HeadSphere == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 两件式装配缺枢轴或网格"
                    + "（Body/TorsoPivot/HeadPivot 或 " + CrewMeshLibrary.BodyFrustum + "/" + CrewMeshLibrary.HeadSphere
                    + "），本预制体的零件层保持 rig 原样。");
                return;
            }

            // ---- 1) 只留动画需要的三个枢轴，其余零件与枢轴全删（幂等重跑也安全）----
            // 顺序：先删最深层的 HeadPivot 子件，再删 TorsoPivot 下除 HeadPivot 的件，
            // 再删 BodyPivot 下除 TorsoPivot 的件，最后清 Visual 下除 BodyPivot 的件。
            ClearChildren(headPivot);                    // 头球 / 头巾 / 三角帽 / 鼻 / 眼 / 须 / 发
            StripChildrenExcept(torsoPivot, headPivot);  // 躯干 / 腰带 / 双臂 / 围裙 / 肩章 / 肋骨 / 大衣下摆…
            StripChildrenExcept(bodyPivot, torsoPivot);  // 双腿 / 靴 / 木腿 / 抱弹 / 下摆
            StripChildrenExcept(visual, bodyPivot);      // Visual 下只留 BodyPivot

            // 枢轴改名：让"Body"这个名字专属于圆台柱 renderer（teamTintRenderers = [Body]）。
            bodyPivot.name = "BodyPivot";

            // ---- 2) 枢轴归位（Visual 局部 y = 脚底起算的世界 y，见类头坐标口径）----
            torsoPivot.localPosition = Vector3.zero;
            torsoPivot.localRotation = Quaternion.identity;
            torsoPivot.localScale = Vector3.one;

            headPivot.localPosition = new Vector3(0f, CrewMeshLibrary.HeadSphereCenterY, 0f);
            headPivot.localRotation = Quaternion.identity;
            headPivot.localScale = Vector3.one;

            // ---- 3) Body 圆台柱（阵营色：建时挂 CrewTeamTintPart 标记，随后被写进 binder 并删掉）----
            AddSimplePart(torsoPivot, "Body", addOns.BodyFrustum,
                assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, CrewMeshLibrary.BodyHeight * 0.5f, 0f), Vector3.one, teamTint: true);

            // ---- 4) Head 圆球（木色 #D4A76A）----
            AddSimplePart(headPivot, "Head", addOns.HeadSphere,
                assets.For(CrewMaterialRole.Wood),
                Vector3.zero, Vector3.one);
        }

        /// <summary>按组装侧 <c>CrewVisualRig.AddPart</c> 的同口径补一个零件。</summary>
        static MeshRenderer AddSimplePart(Transform parent, string name, Mesh mesh, Material material,
            Vector3 localPosition, Vector3 localScale, bool teamTint = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (teamTint)
                go.AddComponent<CrewTeamTintPart>();

            return renderer;
        }

        /// <summary>删掉 <paramref name="parent"/> 下除 <paramref name="keep"/> 以外的所有直接子物体。</summary>
        static void StripChildrenExcept(Transform parent, Transform keep)
        {
            var doomed = new List<GameObject>();
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != keep)
                    doomed.Add(child.gameObject);
            }

            for (int i = 0; i < doomed.Count; i++)
                Object.DestroyImmediate(doomed[i]);
        }

        /// <summary>清空直接子物体。</summary>
        static void ClearChildren(Transform parent)
        {
            StripChildrenExcept(parent, null);
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
        ///
        /// 【两件式下的期望】恰好 1 个（Body）；<see cref="BuildCrewPrefab"/> 会断言这一点。
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

        /// <summary>RepresentativeSymbol 已随职业外观塌缩删除——预制体默认符号恒 "redPirate"（运行时覆盖）。</summary>

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
