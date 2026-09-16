using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>大海域海面的域配置（纯数据）。见 <see cref="OceanRules"/> 的半径规则。</summary>
    public struct OceanConfig
    {
        /// <summary>竞技场中心（世界 XZ）。长涌包络以此为中心（<see cref="OceanGlobals.ArenaCenter"/>）。</summary>
        public Vector2 ArenaCenter;

        /// <summary>竞技场半径（中心到最远岛角的水平距离，半对角）。决定近岸保护圈大小。</summary>
        public float ArenaRadius;

        /// <summary>默认配置：中心原点、半径取 <see cref="OceanRules.DefaultArenaRadius"/>（覆盖 M4 最大 280u 跨度）。</summary>
        public static OceanConfig Default => new OceanConfig
        {
            ArenaCenter = Vector2.zero,
            ArenaRadius = OceanRules.DefaultArenaRadius,
        };

        /// <summary>按竞技场矩形外扩求配置（radius = 半对角）。</summary>
        public static OceanConfig ForArena(Vector2 center, float halfExtentX, float halfExtentZ)
        {
            halfExtentX = Mathf.Abs(halfExtentX);
            halfExtentZ = Mathf.Abs(halfExtentZ);
            return new OceanConfig
            {
                ArenaCenter = center,
                ArenaRadius = Mathf.Sqrt(halfExtentX * halfExtentX + halfExtentZ * halfExtentZ),
            };
        }
    }

    /// <summary>
    /// 大海域海面的运行时载体（M4 §5.2）：自包含地创建"近场细分 + 中场环带 + 远场裙边"的
    /// 单张径向圆盘网格（<see cref="OceanGridRules"/>），跟随主相机、按格步进对齐，
    /// 并把长涌包络/网格中心发布成全局变量供 <c>PirateOcean.shader</c> 消费。
    ///
    /// 【自包含接线（协调者只需一行）】
    /// <code>
    /// var ocean = PirateCrew.PirateCrew.Water.OceanRig.Create(
    ///     PirateCrew.PirateCrew.Water.OceanConfig.ForArena(new Vector2(w * 0.5f, d * 0.5f), w * 0.5f, d * 0.5f));
    /// </code>
    /// 不依赖场景里已有的 Water Cube（旧 <see cref="WaterTessellator"/> 水面可整体退役）；
    /// 与 <see cref="WaterSimulationDriver"/> 的涟漪注入天然兼容——驱动发布的是**全局**纹理/向量，
    /// 新 shader 按世界 XZ 采样，与水面网格的域无关，无需任何坐标映射适配。
    ///
    /// 【材质】<paramref name="material"/> 缺省时按 <see cref="OceanShaderName"/> 查找并 new 一个
    /// 运行时材质（编辑器/开发期足够；出正式资产用 Editor/WaterAssetBuilder.ApplyOceanMaterialDefaults
    /// 落盘的 Ocean_Water.mat，再显式传入）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OceanRig : MonoBehaviour
    {
        /// <summary>海面 shader 名（与 Assets/Art/Shaders/Ocean/PirateOcean.shader 的声明一致）。</summary>
        public const string OceanShaderName = "PirateCrew/Ocean";

        static readonly int ArenaCenterId = Shader.PropertyToID(OceanGlobals.ArenaCenter);
        static readonly int GridCenterId = Shader.PropertyToID(OceanGlobals.GridCenter);

        OceanConfig _config;
        Mesh _mesh;
        Material _material;
        bool _ownsMaterial;
        Transform _followTarget;

        /// <summary>当前配置（只读快照）。</summary>
        public OceanConfig Config => _config;

        /// <summary>当前网格（调试/报告用）。</summary>
        public Mesh OceanMesh => _mesh;

        /// <summary>实际绑定的材质。</summary>
        public Material ResolvedMaterial => _material;

        /// <summary>网格三角面数（报告用）。</summary>
        public int TriangleCount { get; private set; }

        /// <summary>
        /// 静态构建入口：创建 "OceanRig" 物体（网格 + 渲染器 + 本组件），定位到竞技场中心并发布全局。
        /// </summary>
        /// <param name="config">域配置（竞技场中心/半径）。</param>
        /// <param name="material">海面材质；null = 按 <see cref="OceanShaderName"/> 现场创建。</param>
        /// <param name="parent">可选父节点。</param>
        /// <param name="followCamera">可选跟随相机；null = 每帧用 Camera.main。</param>
        public static OceanRig Create(OceanConfig config, Material material = null,
            Transform parent = null, Camera followCamera = null)
        {
            var go = new GameObject("OceanRig");
            if (parent != null)
                go.transform.SetParent(parent, false);

            var rig = go.AddComponent<OceanRig>();
            rig._config = config;
            rig._followTarget = followCamera != null ? followCamera.transform : null;
            rig.BuildContent(material);
            rig.ApplyPosition(true);
            rig.PublishGlobals();
            return rig;
        }

        /// <summary>更新域配置（换地图时调用；只影响包络半径与网格定位，不重建网格）。</summary>
        public void Configure(OceanConfig config)
        {
            _config = config;
            PublishGlobals();
        }

        void BuildContent(Material material)
        {
            _material = material != null ? material : ResolveOrCreateMaterial();

            _mesh = BuildDiscMesh(OceanGridRules.RingRadii(), OceanGridRules.Segments);
            TriangleCount = _mesh.triangles.Length / 3;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            // 透明水面：不投影（与旧 PirateWater 同口径）； receives shadows 走 shader 的主光阴影项。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Material ResolveOrCreateMaterial()
        {
            Shader shader = Shader.Find(OceanShaderName);
            if (shader == null)
            {
                Debug.LogError($"[OceanRig] 找不到 shader {OceanShaderName}（工程未导入或被剔除），海面将显示为品红。");
                shader = Shader.Find("Sprites/Default"); // 兜底：至少不是隐藏异常
            }

            _ownsMaterial = true;
            return new Material(shader) { name = "Ocean_Water_Runtime" };
        }

        /// <summary>
        /// 生成径向圆盘网格：中心 1 顶点 + 同心环（每环 <see cref="OceanGridRules.Segments"/>+1 顶点），
        /// 顶点全在本地 y=0 平面、法线朝上；物体本体被 <see cref="ApplyPosition"/> 放到水面高度。
        /// 环间四边形拆分与旧 <see cref="WaterTessellator"/> 同一绕序约定（顶面朝 +Y）。
        /// </summary>
        static Mesh BuildDiscMesh(float[] ringRadii, int segments)
        {
            int ringCount = ringRadii.Length - 1; // 不含中心
            int vertexCount = OceanGridRules.VertexCount(ringCount);

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var up = Vector3.up;

            // 顶点 0 = 圆盘中心；环 i（1 起）的顶点从 index 1 + (i-1)*(segments+1) 开始。
            int v = 1;
            for (int i = 1; i < ringRadii.Length; i++)
            {
                float r = ringRadii[i];
                for (int s = 0; s <= segments; s++)
                {
                    float theta = (float)s / segments * Mathf.PI * 2f;
                    vertices[v] = new Vector3(Mathf.Cos(theta) * r, 0f, Mathf.Sin(theta) * r);
                    normals[v] = up;
                    v++;
                }
            }

            // 索引：16 位够默认规模（~28k 顶点）；超限自动切 32 位。
            var mesh = new Mesh { name = "OceanDisc" };
            if (OceanGridRules.Needs32BitIndices(ringCount))
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            int fanTris = segments;                          // 中心扇
            int quadTris = (ringCount - 1) * segments * 2;   // 环间四边形
            var triangles = new int[(fanTris + quadTris) * 3];
            int t = 0;

            // 中心扇：(center, s, s+1)。
            int ringStart(int ring) => 1 + (ring - 1) * (segments + 1); // ring 从 1 起

            for (int s = 0; s < segments; s++)
            {
                triangles[t++] = 0;
                triangles[t++] = ringStart(1) + s;
                triangles[t++] = ringStart(1) + s + 1;
            }

            // 环间四边形 a=(i,s) b=(i,s+1) c=(i+1,s) d=(i+1,s+1)，拆 (a,c,d) / (a,d,b)。
            for (int i = 1; i < ringCount; i++)
            {
                int inner = ringStart(i);
                int outer = ringStart(i + 1);
                for (int s = 0; s < segments; s++)
                {
                    int a = inner + s;
                    int b = inner + s + 1;
                    int c = outer + s;
                    int d = outer + s + 1;

                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(
                ringRadii[ringRadii.Length - 1] * 2f, 1f, ringRadii[ringRadii.Length - 1] * 2f));
            return mesh;
        }

        void LateUpdate()
        {
            ApplyPosition(false);
            PublishGlobals();
        }

        void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_ownsMaterial && _material != null)
                Destroy(_material);
        }

        /// <summary>
        /// 跟随相机水平移动网格（Y 恒为 <see cref="LevelGeometry.WaterSurfaceY"/>），
        /// XZ 按 <see cref="OceanGridRules.SnapStep"/> 取整对齐——步进之间网格原地不动，采样点不漂（防泳动）。
        /// </summary>
        void ApplyPosition(bool force)
        {
            Transform target = _followTarget != null && _followTarget.gameObject.activeInHierarchy
                ? _followTarget
                : ResolveMainCamera();

            float step = OceanGridRules.SnapStep;
            float cx, cz;
            if (target != null)
            {
                cx = Mathf.Round(target.position.x / step) * step;
                cz = Mathf.Round(target.position.z / step) * step;
            }
            else
            {
                // 没有相机（编辑器装配期/特殊测试）：停在竞技场中心。
                cx = Mathf.Round(_config.ArenaCenter.x / step) * step;
                cz = Mathf.Round(_config.ArenaCenter.y / step) * step;
            }

            Vector3 desired = new Vector3(cx, LevelGeometry.WaterSurfaceY, cz);
            if (force || transform.position != desired)
                transform.position = desired;
        }

        Transform ResolveMainCamera()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        /// <summary>发布包络与网格中心全局（shader 的长涌衰减/几何梯子都依赖）。</summary>
        void PublishGlobals()
        {
            float protect = OceanRules.ProtectRadius(_config.ArenaRadius);
            float fullSwell = OceanRules.FullSwellRadius(_config.ArenaRadius);
            Shader.SetGlobalVector(ArenaCenterId, new Vector4(
                _config.ArenaCenter.x, _config.ArenaCenter.y, protect, fullSwell));

            Shader.SetGlobalVector(GridCenterId, new Vector4(
                transform.position.x, transform.position.z, 0f, 0f));
        }
    }
}
