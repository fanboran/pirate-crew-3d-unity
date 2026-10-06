using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Visual
{
    /// <summary>
    /// 角色零件网格库：把 docs/设计/美术/角色造型.md §6.2 的生成器清单固化成一份**确定性零件表**
    /// （键 → <see cref="MeshData"/>），并给出各职业的零件用量计划（用于三角面预算核算）。
    ///
    /// 【单一来源】编辑器脚本 <c>CrewVisualPrefabBuilder</c> 用它生成网格资产；
    /// 预算测试 <c>CrewMeshFactoryTests</c> 用同一份数据核算"每职业 ≤2500 tri"，
    /// 避免"文档估算"与"实测"两套数字漂移。
    ///
    /// 【注意】<see cref="CountPartInstances"/> 是与 <see cref="CrewVisualRig.Build"/> 并行的
    /// 零件用量镜像表——**改装配结构时必须同步改这里**，否则预算核算会失真。
    /// 纯 C#，可在无头验证台断言。
    /// </summary>
    public static class CrewMeshLibrary
    {
        // 键（稳定字符串，网格资产文件名 = 键）。
        public const string Sphere = "Sphere";
        public const string SphereSmall = "SphereSmall";
        public const string Cylinder = "Cylinder";
        public const string Capsule = "Capsule";
        public const string Box = "Box";
        public const string TorsoStandard = "TorsoStandard";
        public const string TorsoWide = "TorsoWide";
        public const string TorsoNarrow = "TorsoNarrow";
        public const string TorsoRib = "TorsoRib";
        public const string TorsoCaptain = "TorsoCaptain";
        public const string BandanaStandard = "BandanaStandard";
        public const string BandanaTight = "BandanaTight";
        public const string BandanaLow = "BandanaLow";
        public const string Tricorn = "Tricorn";
        public const string CoatSkirt = "CoatSkirt";
        public const string HookMesh = "Hook";
        public const string RibMesh = "Rib";
        public const string Beard = "Beard";
        public const string HairClump = "HairClump";

        /// <summary>两件式 Body 圆台柱（世界尺寸直造，键值与既有网格资产文件名一致）。</summary>
        public const string BodyFrustum = "CrewBodyFrustum";

        /// <summary>两件式 Head 圆球（世界半径直造）。</summary>
        public const string HeadSphere = "CrewHeadSphere";

        // ------------------------------------------------------------------
        // 两件式造型规格（世界单位；2026-09-29 创始人在角色镜头调试场定档。
        // 2026-10-05 随职业外观塌缩从 CrewVisualPrefabBuilder 下沉至此——单一真源、
        // 无头验证台直接可测，装配器只消费不私藏尺寸）。
        // ------------------------------------------------------------------

        /// <summary>Body 圆台柱顶半径 = 0.30（前值 0.35）。</summary>
        public const float BodyTopRadius = 0.3f;

        /// <summary>Body 圆台柱底半径 = 0.30（与顶径等宽的直筒；演进 0.4 → 0.4667 → 0.3）。
        /// 特写档直径 0.6 m 在可见 14 m@540 画布 = 23.1 px（1:2）。</summary>
        public const float BodyBottomRadius = 0.3f;

        /// <summary>Body 圆台柱高 = 1.3215，底面贴脚底（局部 y 0..1.3215）。</summary>
        public const float BodyHeight = 1.3215f;

        /// <summary>Head 球半径 = 0.30（前值 0.35）。</summary>
        public const float HeadSphereRadius = 0.3f;

        /// <summary>头颈间距 = 0.1606：头心上抬量，正 = 拉出脖颈间隙、0 = 球底与柱顶重叠 0.05。</summary>
        public const float HeadLift = 0.1606f;

        /// <summary>Head 球心高度 = 身高 − 头身重叠(0.05) + 头半径 + 头颈间距 = 1.7321。
        /// 校验：球底 1.4321 &lt; 柱顶 1.3215 不成立（头颈间距把头拉离柱顶，间隙 0.11 m）。</summary>
        public const float HeadSphereCenterY = BodyHeight - 0.05f + HeadSphereRadius + HeadLift;

        /// <summary>造型总高 = 球心 1.7321 + 头半径 0.30 = <c>2.0321</c>
        /// （相机侧镜像 <c>CameraFraming.UnitVisualHeight</c> 同源）。</summary>
        public const float TotalHeight = HeadSphereCenterY + HeadSphereRadius;

        /// <summary>圆台柱侧壁分段 = 16（三角面 = 侧壁 32 + 上下盖 32 = 64）。底缘弦长 6.9 px@1:2 特写。</summary>
        public const int BodySides = 16;

        /// <summary>球经向分段 = 12（像素感口径：16×12 面片在特写档只有 3.5×5.3 px，白费三角面；
        /// 12×8 为 3.5×5.3 px@1:2、≥1.45 px@全场档，三角面 352 → 168）。</summary>
        public const int HeadSegments = 12;

        /// <summary>球纬向分段 = 8（三角面 = 2×12×(8−1) = 168）。</summary>
        public const int HeadRings = 8;

        /// <summary>全部零件键（顺序稳定，用于生成资产与日志）。</summary>
        public static readonly string[] Keys =
        {
            Sphere, SphereSmall, Cylinder, Capsule, Box,
            TorsoStandard, TorsoWide, TorsoNarrow, TorsoRib, TorsoCaptain,
            BandanaStandard, BandanaTight, BandanaLow,
            Tricorn, CoatSkirt, HookMesh, RibMesh, Beard, HairClump,
            BodyFrustum, HeadSphere,
        };

        /// <summary>构建全部零件网格（纯 C#，确定性）。</summary>
        public static Dictionary<string, MeshData> BuildAll()
        {
            var map = new Dictionary<string, MeshData>(Keys.Length);

            map[Sphere] = CrewMeshFactory.LowPolySphere(1f, 10, 6);
            map[SphereSmall] = CrewMeshFactory.LowPolySphere(1f, 6, 4);
            map[Cylinder] = CrewMeshFactory.Cylinder(1f, 1f, 10);
            map[Capsule] = CrewMeshFactory.Capsule(0.5f, 1f, 8, 3);
            map[Box] = CrewMeshFactory.Box(Vector3.one);

            // 躯干圆台（核心"梯形"）：尺寸见规格 §1.2 / §3 各职业表。
            map[TorsoStandard] = CrewMeshFactory.Frustum(0.052f, 0.095f, 0.200f, 10);
            map[TorsoWide] = CrewMeshFactory.Frustum(0.052f, 0.115f, 0.200f, 10);      // 炮手
            map[TorsoNarrow] = CrewMeshFactory.Frustum(0.052f, 0.080f, 0.200f, 10);    // 狙击手
            map[TorsoRib] = CrewMeshFactory.Frustum(0.045f, 0.070f, 0.185f, 10);       // 骷髅胸腔
            map[TorsoCaptain] = CrewMeshFactory.Frustum(0.055f, 0.098f, 0.210f, 10);   // 船长加长

            // 头巾（顶封口、底开口的薄壳，规格 §3.2/§3.3/§3.4）。
            map[BandanaStandard] = CrewMeshFactory.Frustum(0.055f, 0.082f, 0.045f, 10, capTop: true, capBottom: false);
            map[BandanaTight] = CrewMeshFactory.Frustum(0.058f, 0.080f, 0.040f, 10, capTop: true, capBottom: false);
            map[BandanaLow] = CrewMeshFactory.Frustum(0.060f, 0.080f, 0.035f, 10, capTop: true, capBottom: false);

            // 三角帽（宽 0.13 = brimRadius 0.065，规格 §3.8）。
            map[Tricorn] = CrewMeshFactory.Tricorn(0.065f, 0.048f, 0.070f, 28f, 0.012f, 10);

            // 长大衣下摆（下摆外扩 r0.115 / h0.150，规格 §3.8）。
            map[CoatSkirt] = CrewMeshFactory.Frustum(0.095f, 0.115f, 0.150f, 10);

            // 铁钩 270° / 肋骨 180°（规格 §3.5/§3.7）。
            map[HookMesh] = CrewMeshFactory.ArcRib(0.0375f, 0.012f, 270f, 12, 6);
            map[RibMesh] = CrewMeshFactory.ArcRib(0.055f, 0.008f, 180f, 8, 6);

            // 大胡子球簇 / 乱发球簇（规格 §3.6/§3.8）。
            map[Beard] = CrewMeshFactory.BlobCluster(new[]
            {
                new CrewMeshFactory.Blob(new Vector3(0f, 0f, 0.006f), new Vector3(0.046f, 0.030f, 0.030f)),
                new CrewMeshFactory.Blob(new Vector3(0.026f, -0.010f, 0.002f), new Vector3(0.030f, 0.028f, 0.026f)),
                new CrewMeshFactory.Blob(new Vector3(-0.026f, -0.010f, 0.002f), new Vector3(0.030f, 0.028f, 0.026f)),
                new CrewMeshFactory.Blob(new Vector3(0f, -0.030f, 0.004f), new Vector3(0.038f, 0.026f, 0.028f)),
                new CrewMeshFactory.Blob(new Vector3(0.018f, 0.022f, -0.018f), new Vector3(0.022f, 0.018f, 0.020f)),
                new CrewMeshFactory.Blob(new Vector3(-0.018f, 0.022f, -0.018f), new Vector3(0.022f, 0.018f, 0.020f)),
            }, 8, 5);

            map[HairClump] = CrewMeshFactory.BlobCluster(new[]
            {
                new CrewMeshFactory.Blob(Vector3.zero, new Vector3(0.024f, 0.020f, 0.024f)),
                new CrewMeshFactory.Blob(new Vector3(0.020f, 0.006f, 0.008f), new Vector3(0.017f, 0.017f, 0.017f)),
                new CrewMeshFactory.Blob(new Vector3(-0.016f, 0.008f, -0.012f), new Vector3(0.016f, 0.016f, 0.016f)),
            }, 6, 4);

            // 两件式网格（世界尺寸直造，装配时缩放恒为 1 —— 尺寸即规格值）。
            map[BodyFrustum] = CrewMeshFactory.Frustum(BodyTopRadius, BodyBottomRadius, BodyHeight, BodySides);
            map[HeadSphere] = CrewMeshFactory.LowPolySphere(HeadSphereRadius, HeadSegments, HeadRings);

            return map;
        }

        /// <summary>
        /// 两件式造型的零件及实例数（镜像 ApplyTwoPieceSilhouette 的装配结构：
        /// 圆台柱 Body + 圆球 Head。2026-10-05 职业外观塌缩裁决，7 档分职业计划随之根除）。
        /// </summary>
        public static Dictionary<string, int> CountPartInstances()
        {
            var counts = new Dictionary<string, int>();
            Add(counts, BodyFrustum, 1);     // 身体圆台柱
            Add(counts, HeadSphere, 1);      // 头球
            return counts;
        }

        /// <summary>两件式造型三角面合计（用真实 MeshData 统计，非估算）。</summary>
        public static int TotalTriangles()
        {
            Dictionary<string, MeshData> meshes = BuildAll();
            Dictionary<string, int> counts = CountPartInstances();

            int total = 0;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (!meshes.TryGetValue(pair.Key, out MeshData mesh))
                    continue;
                total += mesh.TriangleCount * pair.Value;
            }
            return total;
        }

        /// <summary>键对应的网格（生成一次并缓存；无头环境只读数据，不建 Mesh）。</summary>
        public static MeshData Get(string key)
        {
            if (_cache == null)
                _cache = BuildAll();
            return _cache.TryGetValue(key, out MeshData mesh) ? mesh : MeshData.Empty;
        }

        static Dictionary<string, MeshData> _cache;

        static void Add(Dictionary<string, int> counts, string key, int count)
        {
            if (counts.TryGetValue(key, out int existing))
                counts[key] = existing + count;
            else
                counts[key] = count;
        }
    }
}
