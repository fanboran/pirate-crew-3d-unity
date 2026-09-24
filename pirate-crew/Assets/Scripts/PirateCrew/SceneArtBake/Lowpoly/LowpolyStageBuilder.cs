using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.SceneArt.Lowpoly
{
    /// <summary>一次 <see cref="LowpolyStageBuilder"/> 构建的产出报告（供关卡装配器与日志用）。</summary>
    public sealed class LowpolyStageReport
    {
        /// <summary>生成的场景根节点（已挂到调用方给的 parent 下）。</summary>
        public GameObject Root;

        /// <summary>三角面总数（全部材质槽合计）。</summary>
        public int Triangles;

        /// <summary>实际落地的网格数（= 非空材质槽数，DrawCall 上限）。</summary>
        public int MeshCount;

        /// <summary>站位建议点（脚底贴地；落角色时另加 UnitPivotHeight）。</summary>
        public readonly List<LowpolySpawnPoint> SpawnPoints = new List<LowpolySpawnPoint>();

        /// <summary>云朵关：各云台面数据（山包关为 null）。</summary>
        public List<CloudPlatformData> CloudPlatforms;

        /// <summary>山包关：山顶台面 Y（云朵关为 float.MinValue）。</summary>
        public float HillTopY = float.MinValue;
    }

    /// <summary>
    /// 低模关卡的 **Unity 装配入口**（几何层 → GameObject）：把
    /// <see cref="CloudFieldGeometry"/> 写好的
    /// <see cref="LowpolyBuffers"/> 落成 MeshFilter + MeshRenderer + Collider。
    ///
    /// 【渲染铁律】每个 MeshRenderer 创建后**显式绑定材质**：像素化路径物体材质
    /// （<see cref="GetOrCreateMaterial"/>，配方唯一来源 = <see cref="PixelartMaterialFactory"/>，
    /// 按槽位缓存），绝不留空槽 —— 空材质在播放器构建里是粉色炸弹。
    ///
    /// 【碰撞策略（选稳的）】
    ///   · 云朵：每朵云 **双 BoxCollider**（台面平盒顶面 = TopY，角色必站平面；
    ///     云身粗盒接住侧面打来的弹体）。静态、无 Rigidbody —— 盒子组合最稳最便宜。
    ///
    /// 【接线归协调者】本类不进任何场景烘焙流程；由关卡装配器在需要时调用 Build 入口。
    /// </summary>
    public static class LowpolyStageBuilder
    {
        // ------------------------------------------------------------------
        // 材质（阳光明媚低模调色：饱和度中高、暖调）
        // ------------------------------------------------------------------

        /// <summary>各槽位基色（sRGB）。云=暖白/淡金按高度分带；山=暖岩/草绿/亮草/沙。</summary>
        public static Color ColorOf(LowpolyMaterialSlot slot)
        {
            switch (slot)
            {
                case LowpolyMaterialSlot.CloudPaleGold: return new Color(1f, 0.85f, 0.56f);  // #FFD98F
                case LowpolyMaterialSlot.HillRockWarm: return new Color(0.69f, 0.54f, 0.36f); // #B08A5C
                case LowpolyMaterialSlot.HillGrassMid: return new Color(0.38f, 0.72f, 0.31f); // #61B84F
                case LowpolyMaterialSlot.HillGrassTop: return new Color(0.55f, 0.83f, 0.37f); // #8CD45E
                case LowpolyMaterialSlot.HillSand: return new Color(0.94f, 0.84f, 0.58f);     // #EFD694
                default: return new Color(1f, 0.95f, 0.86f);                                   // #FFF2DB 暖白
            }
        }

        static readonly Dictionary<LowpolyMaterialSlot, Material> _materialCache =
            new Dictionary<LowpolyMaterialSlot, Material>();

        /// <summary>
        /// 取（或创建并缓存）槽位材质：本路径物体 shader + 槽位基色（配方唯一来源 =
        /// <see cref="PixelartMaterialFactory"/>，色带档数/描边都走工厂默认值）。
        /// shader 缺失时不缓存、返回 null（调用方按"空材质红警"纪律暴露，不静默换族）。
        /// </summary>
        public static Material GetOrCreateMaterial(LowpolyMaterialSlot slot)
        {
            Material cached;
            if (_materialCache.TryGetValue(slot, out cached) && cached != null)
                return cached;

            Material material = PixelartMaterialFactory.Create("Lowpoly_" + slot, ColorOf(slot));
            if (material == null)
                return null;

            _materialCache[slot] = material;
            return material;
        }

        // ------------------------------------------------------------------
        // 构建入口（协调者从关卡装配器调用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 构建云朵平台场（幂等：同 parent 下同名旧根先删后建）。
        /// 返回报告含 8 个出生点建议与各云台面数据。
        /// </summary>
        public static LowpolyStageReport BuildCloudField(Transform parent, CloudFieldSpec spec)
        {
            if (spec == null)
                spec = CloudFieldSpec.Default;

            var buffers = new LowpolyBuffers();
            CloudFieldGeometry.Compose(buffers, spec);

            GameObject root = FreshChild(parent, "LowpolyCloudField");
            List<CloudPlatformData> platforms = CloudFieldGeometry.Layout(spec);

            // 渲染：2 个云材质槽（非空才落地）。
            int meshes = EmitBuffers(buffers, root, LowpolySlotSet.Cloud, addMeshCollider: false);

            // 碰撞：每朵云双 BoxCollider（台面平盒 + 云身粗盒），静态无 Rigidbody。
            Transform colliderRoot = root.transform.Find("Colliders");
            if (colliderRoot == null)
            {
                var colliderGo = new GameObject("Colliders");
                colliderGo.transform.SetParent(root.transform, false);
                colliderRoot = colliderGo.transform;
            }

            for (int i = 0; i < platforms.Count; i++)
            {
                CloudPlatformData p = platforms[i];
                var cloudGo = new GameObject("Cloud" + i + (p.IsHero ? "_hero" : ""));
                cloudGo.transform.SetParent(colliderRoot, false);
                cloudGo.transform.localPosition = Vector3.zero;

                // 台面盒：顶面精确 = TopY（角色站这），半宽各收 0.05 防边缘悬空脚。
                var platform = cloudGo.AddComponent<BoxCollider>();
                platform.center = new Vector3(p.CenterXZ.x, p.TopY - 0.25f, p.CenterXZ.z);
                platform.size = new Vector3(
                    Mathf.Max(1f, p.TopHalfWidth * 2f - 0.1f), 0.5f,
                    Mathf.Max(1f, p.TopHalfDepth * 2f - 0.1f));

                // 云身盒：接住侧向弹体与擦边掉落（比台面窄一圈、从底面到台面下沿）。
                var body = cloudGo.AddComponent<BoxCollider>();
                float bodyHalf = Mathf.Min(p.TopHalfWidth, p.TopHalfDepth) * 0.8f;
                body.center = new Vector3(p.CenterXZ.x, (p.BaseY + p.TopY - 0.5f) * 0.5f, p.CenterXZ.z);
                body.size = new Vector3(bodyHalf * 2f, Mathf.Max(0.4f, p.TopY - 0.5f - p.BaseY), bodyHalf * 2f);
            }

            var report = new LowpolyStageReport
            {
                Root = root,
                Triangles = buffers.TotalTriangles,
                MeshCount = meshes,
                CloudPlatforms = platforms,
            };
            report.SpawnPoints.AddRange(CloudFieldGeometry.SpawnPoints(spec));
            return report;
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        /// <summary>槽位子集（云 / 山各自只落地自己的槽）。</summary>
        enum LowpolySlotSet
        {
            Cloud,
            Hill,
        }

        /// <summary>把一组非空缓冲各落成一个网格 + 渲染器（山体槽附 MeshCollider）。</summary>
        static int EmitBuffers(LowpolyBuffers buffers, GameObject root, LowpolySlotSet set,
            bool addMeshCollider)
        {
            int count = 0;
            for (int i = 0; i <= 5; i++)
            {
                var slot = (LowpolyMaterialSlot)i;
                bool inSet = set == LowpolySlotSet.Cloud
                    ? (slot == LowpolyMaterialSlot.CloudWarmWhite || slot == LowpolyMaterialSlot.CloudPaleGold)
                    : (slot != LowpolyMaterialSlot.CloudWarmWhite && slot != LowpolyMaterialSlot.CloudPaleGold);
                if (!inSet)
                    continue;

                MeshBuffers source = buffers.Slot(slot);
                if (source.IsEmpty)
                    continue;

                EmitMesh(root.transform, "Lowpoly_" + slot, source, GetOrCreateMaterial(slot), addMeshCollider);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 缓冲 → GameObject：Mesh（UInt32 索引 + SetVertices/SetNormals/SetTriangles）
        /// + MeshFilter + MeshRenderer（**显式绑材质**，投影全开）+ 可选静态 MeshCollider。
        /// </summary>
        static void EmitMesh(Transform parent, string objectName, MeshBuffers source,
            Material material, bool addMeshCollider)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.isStatic = true;

            var vertices = new List<Vector3>(source.VertexCount);
            var normals = new List<Vector3>(source.VertexCount);
            var triangles = new List<int>(source.IndexCount);
            source.CopyTo(vertices, normals, triangles);

            // 【本实例的宿主与销毁责任】这里 new 出来的 Mesh 是运行时原生对象，**没有资产宿主**：
            //   · 运行时重建路径由 <see cref="FreshChild"/> → ReleaseStageMeshes 先回收旧网格；
            //   · 编辑器烘焙路径（SceneArtBaker.BakeCloudField）会把 filter.sharedMesh 换成
            //     .asset 资产引用——换完必须由烘焙器销毁本实例（持局部引用 DestroyImmediate，
            //     且只能在换引用处销；换完再按 MeshFilter 收集会误销刚挂上的资产网格），
            //     否则每次烘焙泄 2 个匿名 Mesh（DestroyImmediate(root) 只销 GameObject）。
            var mesh = new Mesh { name = objectName };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            // 渲染铁律：材质必须显式绑定（material 为 null 时宁可红警也不静默出粉）。
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (addMeshCollider)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;   // 静态非凸：棱台壳所见即所撞
            }
        }

        /// <summary>
        /// 幂等子节点：同 parent 下同名旧根（连同其网格）先删后建。
        /// 【资源泄漏防线】<see cref="EmitMesh"/> 每次构建都 new Mesh 挂 MeshFilter，只 Destroy
        /// GameObject 不会销毁 Mesh 本体（独立原生对象）——每次进关重建就泄全部槽位网格，
        /// 故删旧根前先回收其下全部运行时网格（模式照抄 Ambient/AmbientLibrary.Dispose；
        /// 材质走 <see cref="GetOrCreateMaterial"/> 静态缓存，**绝不**在此销毁）。
        /// </summary>
        static GameObject FreshChild(Transform parent, string name)
        {
            if (parent != null)
            {
                Transform existing = parent.Find(name);
                if (existing != null)
                {
                    ReleaseStageMeshes(existing);
                    GameObject old = existing.gameObject;
                    if (Application.isPlaying)
                        Object.Destroy(old);
                    else
                        Object.DestroyImmediate(old);
                }
            }

            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.isStatic = true;
            return go;
        }

        /// <summary>销毁旧舞台根下全部 MeshFilter 引用的运行时网格（本类产的 Mesh 全部运行时 new，可安全销毁）。</summary>
        static void ReleaseStageMeshes(Transform stageRoot)
        {
            MeshFilter[] filters = stageRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i] != null ? filters[i].sharedMesh : null;
                if (mesh == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(mesh);
                else
                    Object.DestroyImmediate(mesh);
            }
        }
    }
}
