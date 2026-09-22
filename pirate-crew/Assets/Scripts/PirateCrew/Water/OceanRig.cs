using PirateCrew.Battle;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Water
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
    /// 并把长涌包络/网格中心发布成全局变量供 <c>PirateOcean.shader</c> 消费
    /// （仅当调用方显式传入旧 <c>PirateCrew/Ocean</c> 材质时有人消费；本类的替身海面不读它们）。
    ///
    /// 【自包含接线（协调者只需一行）】
    /// <code>
    /// var ocean = PirateCrew.Water.OceanRig.Create(
    ///     PirateCrew.Water.OceanConfig.ForArena(new Vector2(w * 0.5f, d * 0.5f), w * 0.5f, d * 0.5f));
    /// </code>
    /// 不依赖场景里已有的 Water Cube（旧 <see cref="WaterTessellator"/> 水面可整体退役）；
    /// 与 <see cref="WaterSimulationDriver"/> 的涟漪注入天然兼容——驱动发布的是**全局**纹理/向量，
    /// 新 shader 按世界 XZ 采样，与水面网格的域无关，无需任何坐标映射适配。
    ///
    /// 【材质（像素化路径的替身海面，如实标注）】活海面 <c>PirateCrew/Ocean</c>（半透明 + 波浪顶点动画）
    /// **进不了本路径**：G-buffer 没有混合，也拉不动它的网格。故 <paramref name="material"/> 缺省时
    /// 用 <see cref="PixelartMaterialFactory"/> 造一块**不透明平色 + 色带**的替身海面，
    /// 与海图试点场景的替身海面**同色同档**（<see cref="PixelartMaterialFactory.Sea"/> /
    /// <see cref="PixelartMaterialFactory.SeaBandCount"/>）、**0 描边**（海是背景，出线会把画面切碎）。
    /// 真正的 3D 波浪观感留给"像素海面方案"那条待定项。
    ///
    /// 【当前实拍链的注意点】Battle 场景经由 <c>BattleController.worldOceanMaterial</c> 显式传入
    /// 落盘资产 <c>Ocean_Water.mat</c>（旧 shader），本处兜底只在那个字段为空时才生效——
    /// 要让实拍走替身海面，须把该字段清空/替换或改场景接线，见交接说明。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OceanRig : MonoBehaviour
    {
        static readonly int ArenaCenterId = Shader.PropertyToID(OceanGlobals.ArenaCenter);
        static readonly int GridCenterId = Shader.PropertyToID(OceanGlobals.GridCenter);

        /// <summary>
        /// 当前活跃的海面（批次 F 增设）：<see cref="Create"/> 赋值，OnDestroy 清空（只清自己，
        /// 避免换图时旧 rig 的销毁误清新 rig）。为 null = 旧竞技场模式（无大海域海面），
        /// 表现层消费方（<see cref="FloatingPropView"/> 等）据此逐帧早退，不动 transform。
        /// </summary>
        public static OceanRig Instance { get; private set; }

        OceanConfig _config;
        Mesh _mesh;
        Material _material;
        bool _ownsMaterial;
        Transform _followTarget;

        /// <summary>当前配置（只读快照）。</summary>
        public OceanConfig Config => _config;

        /// <summary>
        /// 网格中心的世界 XZ（批次 F 增设，只读）：transform.position 即网格中心、
        /// Y 恒为 <see cref="LevelGeometry.WaterSurfaceY"/>（<see cref="ApplyPosition"/> 维护），
        /// 供水面高度采样器对齐 shader 的 <c>_OceanGridCenter</c>（环宽梯子以此为圆心）。
        /// </summary>
        public Vector2 GridCenterXZ => new Vector2(transform.position.x, transform.position.z);

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
        /// <param name="material">海面材质；null = 用 <see cref="PixelartMaterialFactory"/> 现场造替身海面（见类头）。</param>
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
            Instance = rig; // 批次 F：表现层（FloatingPropView 等）的静态访问入口
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
            // 【为什么"给了材质也可能不用"】本路径放不下半透明几何，半透明内容一律被交到叠加档
            // 画在成图之上——一个旧链的半透明海洋（`Ocean_Water.mat`，Queue=Transparent）接进来，
            // 结果就是**一层全屏旧水盖住整个新管线画面**（实测：场地与单位全看不见）。所以即使
            // 调用方显式传了材质，只要它不是本路径的物体 shader，就拒绝并改用替身海面（并且吵闹地报）。
            if (material != null && material.shader != null
                && material.shader.name != PixelartPath.ObjectShaderName)
            {
                global::PirateCrew.Core.Log.Error("[OceanRig] 传入的海面材质 \"" + material.name
                    + "\" 用的是 " + material.shader.name + "，不是本路径的物体 shader（"
                    + PixelartPath.ObjectShaderName + "）：半透明海洋会被叠加档整屏画在像素化成图之上、"
                    + "把画面全盖住。已改用运行期造的不透明替身海面。"
                    + "修复：清空 BattleController.worldOceanMaterial（装配链第 ⑦ 步别再写它）。");
                material = null;
            }

            _material = material != null ? material : ResolveOrCreateMaterial();
            ApplyDebugOverride(_material);

            _mesh = BuildDiscMesh(OceanGridRules.RingRadii(), OceanGridRules.Segments);
            TriangleCount = _mesh.triangles.Length / 3;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            // 替身海面是不透明平色（本路径的 G-buffer 没有混合），不投影、靠深度参与遮挡；
            // 光照在低分辨率域那几趟里按全局主光/环境色算，不看 URP 的 receiveShadows 开关。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// 调试覆盖：命令行 <c>-oceanDebug &lt;0-13&gt;</c> 会去写海面材质的 <c>_DebugMode</c>
        /// （旧 <c>PirateCrew/Ocean</c> 的档位：1=纯水色 4=全泡沫 11=白帽 12=包络…）。
        ///
        /// 【像素化路径没有这个档】替身海面用的是本路径物体 shader，**没有 `_DebugMode` 属性**，
        /// 写进去是静默无效——所以这里**明确报错**而不是悄悄忽略；只有调用方显式传入的旧海面材质
        /// （如 <c>Ocean_Water.mat</c>）才有该属性，那种情况仍照旧写入。
        /// </summary>
        void ApplyDebugOverride(Material material)
        {
            // argv 由 Core.CommandLineOptions 统一解析（唯一入口解析一次），本类只取值。
            if (!global::PirateCrew.Core.CommandLineOptions.TryGetFloat(
                    global::PirateCrew.Core.ToolFlags.OceanDebug, out float mode))
                return;

            if (material != null && material.HasProperty("_DebugMode"))
            {
                material.SetFloat("_DebugMode", mode);
                Debug.Log("[OceanRig] 调试档 _DebugMode=" + mode);
                return;
            }

            Debug.LogError("[OceanRig] -oceanDebug=" + mode + " 未生效：当前是像素化路径的**替身海面**"
                + "（物体 shader " + PixelartPath.ObjectShaderName + " 没有 _DebugMode 属性）。"
                + "这个调试档只属于旧 PirateCrew/Ocean 材质——要逐档诊断旧海面，请显式传入 Ocean_Water.mat。");
        }

        /// <summary>
        /// 造替身海面材质（见类头：不透明平色 + 色带、0 描边，与海图试点替身同色同档）。
        /// 物体 shader 缺失时退回 <c>Sprites/Default</c>——这是**构建包丢 shader**，不是逻辑错；
        /// 宁可看见一块能识别的水色，也不要品红/不可见（旧实现的兜底语义保留）。
        /// </summary>
        Material ResolveOrCreateMaterial()
        {
            _ownsMaterial = true;

            Material mat = PixelartMaterialFactory.Create(
                "PixelartOcean_Sea",
                PixelartMaterialFactory.Sea,
                PixelartMaterialFactory.SeaBandCount,
                outlinePixels: 0f);
            if (mat != null)
                return mat;

            Debug.LogError("[OceanRig] 替身海面材质未创建（物体 shader \"" + PixelartPath.ObjectShaderName
                + "\" 不在包里），已回退 Sprites/Default。这是**构建包丢 shader 资产**，不是逻辑错误。");
            Shader fallback = Shader.Find("Sprites/Default"); // 兜底：至少不是隐藏异常
            return new Material(fallback) { name = "Ocean_Water_Runtime_Fallback" };
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
            if (Instance == this)
                Instance = null; // 只清自己：换图后新 rig 已就位时，旧 rig 销毁不得清掉它

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
