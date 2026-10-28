using System;
using System.Collections.Generic;
using PirateCrew.WaterLab;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    // =========================================================================
    // WaterLabScatterSetup —— C 流海底散布恢复装配件（§4b 批次）。
    //
    // 职责（B 流在 BuildScene 末段经反射调用本类 public static EnsureScatter()）：
    //   1. MeshData .bytes（提取自 sharedassets0.assets，extracted/meshes/
    //      extract_scatter_meshes.py 产物，30 网格）→ Unity Mesh 资产（原作顶点/
    //      法线/切线/UV/顶点色直灌；幂等：资产在即加载）。
    //   2. 材质：复用 WaterLabProp.shader（只读引用，不改它），_BaseColor =
    //      该网格顶点色均值（§10.6 色板口径的第一手替代——顶点色即原作调色板，
    //      见 C-scatter.md §2；Vector 下发避 Linear 材质色 gamma 陷阱）。
    //   3. loose 群簇分布（§10.6：单簇 0.3–0.8 m、簇距 1–3 m；带按水深），
    //      确定性种子，实例以场景 GameObject 序列化。
    //   4. Gravel 粒子：挂 ReplicaGravelDriver（自举，见系统侧注释）。
    //
    // 分布为统计复刻（非逐株恒等）：原作菜单运行时只摆 Rock 实体（LayoutMainMenu，
    // 外环 dist>10、scale 0.5–1.5），贝壳/卵石/海星/砾石不可静态逐粒恢复——
    // 按 §10.6 群簇参数与快照带面积定标，全部登记 C-scatter.md §3。
    // =========================================================================
    public static class WaterLabScatterSetup
    {
        const string ScatterFolder = "Assets/Art/WaterLabReplica/Scatter";
        const string MeshDataFolder = ScatterFolder + "/MeshData";
        const string MeshFolder = ScatterFolder + "/Meshes";
        const string MaterialFolderScatter = ScatterFolder + "/Materials";
        const string ScatterRootName = "Scatter";
        const string PropShaderPath = "Assets/Art/Shaders/WaterLab/WaterLabProp.shader";
        const string GravelShaderPath = "Assets/Art/Shaders/WaterLab/ReplicaGravel.shader";
        const string ReplicaGroundPath = "Assets/Art/WaterLabReplica/MenuGround256.bytes";
        const string ReplicaWaterPath = "Assets/Art/WaterLabReplica/MenuWater256.bytes";

        // ---------------------------------------------------------------------
        // 分布常量（出处标注；全部进 C-scatter.md §3 统计口径）
        // ---------------------------------------------------------------------

        /// <summary>确定性散布种子（我方定标；原作逐株不可恢复）。</summary>
        public const uint Seed = 0x5CA77u;

        const float DomainSize = WaterLabRules.DomainSize;   // 30（B 流 bounds）
        const float SeaLevel = WaterLabRules.SeaLevel;       // 1.0（level 真值）

        /// <summary>礁石浅带（水深 0.2–1.0 m；r82 带参数「礁石 0–1.0 m 水深」下限抬到
        /// 0.2 避免贴岸线）。快照实测面积 383.0 m²。</summary>
        const float RockShallowDepthMin = 0.2f, RockShallowDepthMax = 1.0f;
        const int RockShallowCount = 12;

        /// <summary>外环剪影（原作 LayoutMainMenu:444-463：dist>10 才摆 Rock）。
        /// 快照实测 r>10 ∧ depth>0 面积 301.9 m²。</summary>
        const float RockRingRadiusMin = 10f, RockRingRadiusSpan = 4.5f;
        const int RockRingCount = 6;
        const float RockDepthMin = 0.05f;

        /// <summary>Rock scale 0.5–1.5（原作 LayoutMainMenu:466 Random.Range(0.5,1.5)）。</summary>
        const float RockScaleMin = 0.5f, RockScaleSpan = 1.0f;

        /// <summary>贝壳带 ground∈[0.55,1.4]（Spawn.Sand 滩面 + swash 湿带；
        /// 快照面积 403.6 m²）。卵石带 depth∈[0.1,1.0]（面积 427.4 m²）。
        /// 海星带 ground∈[0.8,1.45]（近水线，面积≈378 m²）。</summary>
        const float ShellGroundMin = 0.55f, ShellGroundMax = 1.4f;
        const float PebbleDepthMin = 0.1f, PebbleDepthMax = 1.0f;
        const float StarGroundMin = 0.8f, StarGroundMax = 1.45f;

        /// <summary>簇距 2 m（§10.6 间距 1–3 m 中值）±0.5 m 抖动（间距保底 1 m）。</summary>
        const float ClusterSpacing = 2f, ClusterJitter = 0.5f;

        /// <summary>簇半径 0.15–0.4 m（§10.6 单簇直径 0.3–0.8 m）。</summary>
        const float ClusterRadiusMin = 0.15f, ClusterRadiusSpan = 0.25f;

        /// <summary>带内簇保留率（类目窗口，量级定标）：贝壳 0.30 / 卵石 0.22 /
        /// 海星 0.05；同格互斥按 roll 切分。</summary>
        const float ShellKeep = 0.30f, PebbleKeep = 0.22f, StarKeep = 0.05f;

        const int ItemsMin = 2, ItemsSpan = 5;               // 每簇 2–6 件
        const float ShellScaleMin = 0.8f, ShellScaleSpan = 0.6f;
        const float PebbleScaleMin = 0.8f, PebbleScaleSpan = 0.8f;
        const float StarScaleMin = 0.9f, StarScaleSpan = 0.4f;
        const float TiltMaxDeg = 10f;                        // 滩面小件随机倾覆
        const float RockEmbed = 0.05f;                       // 礁石浅埋（我方口径）

        // 物种池（提取清单全集；Gravel_01 归砾石系统，不入散布池）
        static readonly string[] RockPool =
            { "Rock_01", "Rock_02", "Rock_03", "Rock_04", "Rock_05", "Rock_06" };
        static readonly string[] PebblePool =
            { "Pebble_01", "Pebble_02", "Pebble_03", "Pebble_04", "Pebble_05", "Pebble_06" };
        static readonly string[] ShellPool =
        {
            "CalicoScallopShell_01", "CalicoScallopShell_02", "CalicoScallopShell_03",
            "ClamShell_01", "ClamShell_02", "ClamShell_03",
            "CommonMusselShell_01", "CommonMusselShell_02", "CommonMusselShell_03",
            "IncongruousArkShell_01", "IncongruousArkShell_02", "IncongruousArkShell_03",
            "MiterShell_01", "MoonSnailShell_01", "SundialShell_01", "HermitCrabShell_01",
        };
        const string StarfishName = "Starfish_01";

        // ---------------------------------------------------------------------
        // MeshData .bytes 布局（extract_scatter_meshes.py write_bytes 对应）
        // ---------------------------------------------------------------------
        sealed class MeshPayload
        {
            public string Name;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector4[] Tangents;
            public Vector2[] Uv;
            public Color32[] Colors;
            public int[] SubFirst;      // 子网格起始索引
            public int[] SubCount;
            public int[] Indices;
            public Vector3 ColorMean;   // 顶点色均值（材质 _BaseColor 用）
        }

        /// <summary>C 流入口（B 流 WaterLabSetup.EnsureScatter 反射按名调用）：
        /// 幂等——场景已有 Scatter 根则跳过。</summary>
        public static void EnsureScatter()
        {
            if (GameObject.Find(ScatterRootName) != null)
            {
                Debug.Log($"[WaterLabScatterSetup] 场景已有 {ScatterRootName} 根，跳过（幂等）。");
                return;
            }
            if (!WaterLabReplicaSnapshot.HasData)
            {
                Debug.LogError("[WaterLabScatterSetup] 主菜单快照缺失（散布需要地面高度场），跳过 EnsureScatter。");
                return;
            }

            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolderScatter);

            var payloads = LoadMeshData();
            if (payloads.Count == 0)
            {
                Debug.LogError($"[WaterLabScatterSetup] {MeshDataFolder} 无 MeshData（提取脚本产物未入库？），跳过。");
                return;
            }
            var meshes = EnsureMeshAssets(payloads);
            var materials = EnsureMaterials(payloads, meshes);

            var root = new GameObject(ScatterRootName);
            int rocks = PlaceRocks(root, payloads, meshes, materials);
            int clusters;
            int placed = PlaceClusters(root, payloads, meshes, materials, out clusters, out int shells, out int pebbles, out int stars);
            EnsureGravelDriver(root, meshes, materials);

            AssetDatabase.SaveAssets();
            Debug.Log("[WaterLabScatterSetup] 散布就绪：礁石 " + rocks +
                "（浅带 " + RockShallowCount + " + 外环 " + RockRingCount + " 目标）、" +
                "群簇 " + clusters + "（贝壳 " + shells + " / 卵石 " + pebbles + " / 海星 " + stars +
                "，共 " + placed + " 件）+ ReplicaGravelDriver（分布=§10.6 群簇统计复刻，非逐株恒等，见 C-scatter.md §3）");
        }

        // =====================================================================
        // 资产生成（幂等：在即加载）
        // =====================================================================
        static List<MeshPayload> LoadMeshData()
        {
            var result = new List<MeshPayload>();
            if (!AssetDatabase.IsValidFolder(MeshDataFolder))
                return result;
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { MeshDataFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset == null)
                    continue;
                try
                {
                    result.Add(ParseMeshData(asset.name, asset.bytes));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[WaterLabScatterSetup] MeshData 解析失败：{path} → {e.Message}");
                }
            }
            return result;
        }

        static MeshPayload ParseMeshData(string name, byte[] data)
        {
            var p = new MeshPayload { Name = name };
            int off = 0;
            Func<int> u32 = () => { int v = BitConverter.ToInt32(data, off); off += 4; return v; };
            Func<float> f32 = () => { float v = BitConverter.ToSingle(data, off); off += 4; return v; };

            if (data[0] != (byte)'W' || data[1] != (byte)'L' || data[2] != (byte)'S' || data[3] != (byte)'M')
                throw new FormatException("magic 非 WLSM");
            off = 4;
            int version = u32();
            if (version != 1)
                throw new FormatException($"version {version} 不支持");
            int nameLen = u32();
            off += nameLen;                          // 文件内名与资产名一致，跳过
            int vc = u32();
            int subCount = u32();
            p.SubFirst = new int[subCount];
            p.SubCount = new int[subCount];
            for (int i = 0; i < subCount; i++)
            {
                p.SubFirst[i] = u32();
                p.SubCount[i] = u32();
            }
            int flags = u32();
            bool hasNormals = (flags & 1) != 0;
            bool hasTangents = (flags & 2) != 0;
            bool hasColors = (flags & 4) != 0;
            bool hasUv = (flags & 8) != 0;
            p.ColorMean = new Vector3(f32(), f32(), f32());
            off += 4;                                // 均值 alpha（材质不用）

            p.Positions = new Vector3[vc];
            for (int i = 0; i < vc; i++)
                p.Positions[i] = new Vector3(f32(), f32(), f32());
            if (hasNormals)
            {
                p.Normals = new Vector3[vc];
                for (int i = 0; i < vc; i++)
                {
                    var n = new Vector3(f32(), f32(), f32());
                    p.Normals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                }
            }
            if (hasTangents)
            {
                p.Tangents = new Vector4[vc];
                for (int i = 0; i < vc; i++)
                    p.Tangents[i] = new Vector4(f32(), f32(), f32(), f32());
            }
            if (hasColors)
            {
                p.Colors = new Color32[vc];
                for (int i = 0; i < vc; i++)
                    p.Colors[i] = new Color32(data[off], data[off + 1], data[off + 2], data[off + 3]);
                off += 4 * vc;
            }
            if (hasUv)
            {
                p.Uv = new Vector2[vc];
                for (int i = 0; i < vc; i++)
                    p.Uv[i] = new Vector2(f32(), f32());
            }
            int total = 0;
            for (int i = 0; i < subCount; i++) total += p.SubCount[i];
            p.Indices = new int[total];
            for (int i = 0; i < total; i++)
                p.Indices[i] = u32();
            return p;
        }

        static Mesh[] EnsureMeshAssets(List<MeshPayload> payloads)
        {
            var result = new Mesh[payloads.Count];
            for (int i = 0; i < payloads.Count; i++)
            {
                var p = payloads[i];
                string path = $"{MeshFolder}/{p.Name}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    mesh = new Mesh { name = p.Name };
                    mesh.vertices = p.Positions;
                    if (p.Normals != null) mesh.normals = p.Normals;      // 原作法线通道直灌
                    if (p.Tangents != null) mesh.tangents = p.Tangents;   // 原作切线通道直灌
                    if (p.Uv != null) mesh.uv = p.Uv;
                    if (p.Colors != null) mesh.colors32 = p.Colors;       // 顶点色=原作调色板，随资产保全
                    mesh.subMeshCount = p.SubFirst.Length;
                    for (int s = 0; s < p.SubFirst.Length; s++)
                    {
                        var tri = new int[p.SubCount[s]];
                        Array.Copy(p.Indices, p.SubFirst[s], tri, 0, p.SubCount[s]);
                        mesh.SetTriangles(tri, s, false);
                    }
                    if (p.Normals == null) mesh.RecalculateNormals();
                    if (p.Tangents == null) mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, path);
                }
                result[i] = mesh;
            }
            return result;
        }

        static Material[] EnsureMaterials(List<MeshPayload> payloads, Mesh[] meshes)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(PropShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[WaterLabScatterSetup] 找不到 prop shader：{PropShaderPath}");
                return new Material[payloads.Count];
            }
            var result = new Material[payloads.Count];
            for (int i = 0; i < payloads.Count; i++)
            {
                string path = $"{MaterialFolderScatter}/{payloads[i].Name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader) { name = payloads[i].Name };
                    // 顶点色均值 → 均色口径（§10.6 色板）；Vector 下发（Linear 材质色 gamma 陷阱）
                    mat.SetVector("_BaseColor", payloads[i].ColorMean);
                    AssetDatabase.CreateAsset(mat, path);
                }
                result[i] = mat;
            }
            return result;
        }

        // =====================================================================
        // 分布
        // =====================================================================
        static int PlaceRocks(GameObject root, List<MeshPayload> payloads, Mesh[] meshes, Material[] materials)
        {
            int placed = 0;
            placed += PlaceRockSet(root, payloads, meshes, materials, RockShallowCount, PlaceRockShallow);
            placed += PlaceRockSet(root, payloads, meshes, materials, RockRingCount, PlaceRockRing);
            return placed;
        }

        delegate bool RockSampler(ref uint state, out float x, out float z);

        static int PlaceRockSet(GameObject root, List<MeshPayload> payloads, Mesh[] meshes,
                                Material[] materials, int count, RockSampler sample)
        {
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                uint state = Seed ^ (0x2000u + (uint)i * 0x9E3779B1u);
                // 拒绝采样上限 64 次，失败则少摆（统计口径如实入日志）
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    if (!sample(ref state, out float x, out float z))
                        continue;
                    float ground = WaterLabReplicaSnapshot.SampleGround(x, z);
                    if (ground <= 0f)
                        continue;
                    float scale = RockScaleMin + Next01(ref state) * RockScaleSpan;
                    float yaw = Next01(ref state) * 360f;
                    float tiltX = (Next01(ref state) * 2f - 1f) * TiltMaxDeg;
                    float tiltZ = (Next01(ref state) * 2f - 1f) * TiltMaxDeg;
                    int pick = (int)(Next01(ref state) * RockPool.Length) % RockPool.Length;
                    int idx = IndexOf(payloads, RockPool[pick]);
                    if (idx < 0 || meshes[idx] == null || materials[idx] == null)
                        break;
                    var go = new GameObject($"{RockPool[pick]}_{placed}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(x, ground - RockEmbed * scale, z);
                    go.transform.rotation = Quaternion.Euler(tiltX, yaw, tiltZ);
                    go.transform.localScale = Vector3.one * scale;
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[idx];
                    go.AddComponent<MeshRenderer>().sharedMaterial = materials[idx];
                    placed++;
                    break;
                }
            }
            return placed;
        }

        static bool PlaceRockShallow(ref uint state, out float x, out float z)
        {
            x = (Next01(ref state) * 2f - 1f) * DomainSize * 0.5f;
            z = (Next01(ref state) * 2f - 1f) * DomainSize * 0.5f;
            float depth = SeaLevel - WaterLabReplicaSnapshot.SampleGround(x, z);
            return depth >= RockShallowDepthMin && depth <= RockShallowDepthMax;
        }

        static bool PlaceRockRing(ref uint state, out float x, out float z)
        {
            float r = RockRingRadiusMin + Next01(ref state) * RockRingRadiusSpan;
            float a = Next01(ref state) * Mathf.PI * 2f;
            x = Mathf.Cos(a) * r;
            z = Mathf.Sin(a) * r;
            if (Mathf.Abs(x) > DomainSize * 0.5f || Mathf.Abs(z) > DomainSize * 0.5f)
                return false;
            return SeaLevel - WaterLabReplicaSnapshot.SampleGround(x, z) > RockDepthMin;
        }

        static int PlaceClusters(GameObject root, List<MeshPayload> payloads, Mesh[] meshes,
                                 Material[] materials, out int clusterCount,
                                 out int shellItems, out int pebbleItems, out int starItems)
        {
            clusterCount = 0;
            shellItems = pebbleItems = starItems = 0;
            int placed = 0;
            int half = Mathf.FloorToInt(DomainSize / ClusterSpacing * 0.5f); // ±7 格（2 m 格距）
            uint cell = 0;
            for (int gz = -half; gz <= half; gz++)
            {
                for (int gx = -half; gx <= half; gx++, cell++)
                {
                    uint state = Seed ^ (0x4000u + cell * 0x9E3779B1u);
                    float cx = gx * ClusterSpacing + (Next01(ref state) * 2f - 1f) * ClusterJitter;
                    float cz = gz * ClusterSpacing + (Next01(ref state) * 2f - 1f) * ClusterJitter;
                    float ground = WaterLabReplicaSnapshot.SampleGround(cx, cz);
                    float depth = SeaLevel - ground;
                    float roll = Next01(ref state);

                    IReadOnlyList<string> pool;
                    float scaleMin, scaleSpan;
                    int items;
                    if (ground >= ShellGroundMin && ground < ShellGroundMax && roll < ShellKeep)
                    {
                        pool = ShellPool; scaleMin = ShellScaleMin; scaleSpan = ShellScaleSpan;
                        items = ItemsMin + (int)(Next01(ref state) * ItemsSpan);
                    }
                    else if (depth >= PebbleDepthMin && depth <= PebbleDepthMax && roll < ShellKeep + PebbleKeep)
                    {
                        pool = PebblePool; scaleMin = PebbleScaleMin; scaleSpan = PebbleScaleSpan;
                        items = ItemsMin + (int)(Next01(ref state) * ItemsSpan);
                    }
                    else if (ground >= StarGroundMin && ground < StarGroundMax && roll < ShellKeep + PebbleKeep + StarKeep)
                    {
                        pool = new[] { StarfishName }; scaleMin = StarScaleMin; scaleSpan = StarScaleSpan;
                        items = 1 + (int)(Next01(ref state) * 2f);
                    }
                    else
                    {
                        continue;
                    }

                    float radius = ClusterRadiusMin + Next01(ref state) * ClusterRadiusSpan;
                    for (int k = 0; k < items; k++)
                    {
                        float ang = Next01(ref state) * Mathf.PI * 2f;
                        float dist = Mathf.Sqrt(Next01(ref state)) * radius;   // 盘内均匀
                        float x = cx + Mathf.Cos(ang) * dist;
                        float z = cz + Mathf.Sin(ang) * dist;
                        float g = WaterLabReplicaSnapshot.SampleGround(x, z);
                        // 成员复检（防簇缘出带）；海星簇贴水线，允许半没
                        float d = SeaLevel - g;
                        if (pool == PebblePool && (d < PebbleDepthMin * 0.5f || d > PebbleDepthMax)) continue;

                        int pick = (int)(Next01(ref state) * pool.Count) % pool.Count;
                        int idx = IndexOf(payloads, pool[pick]);
                        if (idx < 0 || meshes[idx] == null || materials[idx] == null)
                            continue;

                        float scale = scaleMin + Next01(ref state) * scaleSpan;
                        float yaw = Next01(ref state) * 360f;
                        float tiltX = (Next01(ref state) * 2f - 1f) * TiltMaxDeg;
                        float tiltZ = (Next01(ref state) * 2f - 1f) * TiltMaxDeg;
                        var normal = GroundNormal(x, z);
                        var go = new GameObject($"{pool[pick]}_{placed}");
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = new Vector3(x, g, z);
                        // 贴地：法线对齐（原作 Findable.Alignment.Normal 语义）× 随机朝向 × 倾覆
                        go.transform.rotation =
                            Quaternion.FromToRotation(Vector3.up, normal) *
                            Quaternion.Euler(tiltX, yaw, tiltZ);
                        go.transform.localScale = Vector3.one * scale;
                        go.AddComponent<MeshFilter>().sharedMesh = meshes[idx];
                        go.AddComponent<MeshRenderer>().sharedMaterial = materials[idx];
                        placed++;
                        if (pool == ShellPool) shellItems++;
                        else if (pool == PebblePool) pebbleItems++;
                        else starItems++;
                    }
                    clusterCount++;
                }
            }
            return placed;
        }

        static void EnsureGravelDriver(GameObject root, Mesh[] meshes, Material[] materials)
        {
            int idx = IndexOf(meshes, "Gravel_01");
            if (idx < 0)
            {
                Debug.LogError("[WaterLabScatterSetup] 缺 Gravel_01 资产，砾石层跳过。");
                return;
            }
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GravelShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[WaterLabScatterSetup] 找不到砾石 shader：{GravelShaderPath}");
                return;
            }
            string matPath = $"{MaterialFolderScatter}/ReplicaGravel.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "ReplicaGravel" };
                mat.SetVector("_BaseColor1", ReplicaGravelSystem.CarbonateColor1);
                mat.SetVector("_BaseColor2", ReplicaGravelSystem.CarbonateColor2);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var go = new GameObject("ReplicaGravelDriver");
            go.transform.SetParent(root.transform, false);
            var driver = go.AddComponent<ReplicaGravelDriver>();
            driver.GravelMesh = meshes[idx];
            driver.GravelMaterial = mat;
            driver.GroundSnapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaGroundPath);
            driver.WaterSnapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaWaterPath);
        }

        // ---- 地形采样辅助（快照中央差分）----
        static Vector3 GroundNormal(float x, float z)
        {
            float e = WaterLabReplicaSnapshot.CellSize;
            float xl = WaterLabReplicaSnapshot.SampleGround(x - e, z);
            float xr = WaterLabReplicaSnapshot.SampleGround(x + e, z);
            float zl = WaterLabReplicaSnapshot.SampleGround(x, z - e);
            float zr = WaterLabReplicaSnapshot.SampleGround(x, z + e);
            return new Vector3(-(xr - xl) / (2f * e), 1f, -(zr - zl) / (2f * e)).normalized;
        }

        static int IndexOf(List<MeshPayload> payloads, string name)
        {
            for (int i = 0; i < payloads.Count; i++)
                if (payloads[i].Name == name)
                    return i;
            return -1;
        }

        static int IndexOf(Mesh[] meshes, string name)
        {
            for (int i = 0; i < meshes.Length; i++)
                if (meshes[i] != null && meshes[i].name == name)
                    return i;
            return -1;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            int slash = folder.LastIndexOf('/');
            EnsureFolder(folder.Substring(0, slash));
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }

        // ---- 确定性哈希（PCG，与 ReplicaPlantSystem/ReplicaGravelSystem 同链）----
        static uint PcgHash(uint v)
        {
            uint t = (v ^ 61u) ^ (v >> 16);
            t *= 9u;
            t ^= t >> 4;
            t *= 0x27d4eb2du;
            t ^= t >> 15;
            return t;
        }

        static float Next01(ref uint state)
        {
            state = PcgHash(state + 0x9E3779B1u);
            return BitConverter.Int32BitsToSingle((int)(0x3f800000u | (state & 0x7fffffu))) - 1f;
        }
    }
}
