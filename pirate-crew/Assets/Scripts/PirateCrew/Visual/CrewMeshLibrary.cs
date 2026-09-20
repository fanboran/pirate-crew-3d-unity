using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Visual
{
    /// <summary>
    /// 角色零件网格库：把 docs/角色造型规范.md §6.2 的生成器清单固化成一份**确定性零件表**
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

        /// <summary>全部零件键（顺序稳定，用于生成资产与日志）。</summary>
        public static readonly string[] Keys =
        {
            Sphere, SphereSmall, Cylinder, Capsule, Box,
            TorsoStandard, TorsoWide, TorsoNarrow, TorsoRib, TorsoCaptain,
            BandanaStandard, BandanaTight, BandanaLow,
            Tricorn, CoatSkirt, HookMesh, RibMesh, Beard, HairClump,
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

            return map;
        }

        /// <summary>
        /// 一个职业用到的零件及实例数（镜像 <see cref="CrewVisualRig.Build"/> 的装配结构）。
        /// </summary>
        public static Dictionary<string, int> CountPartInstances(CrewProfession profession)
        {
            var counts = new Dictionary<string, int>();

            // 通用骨架（BuildCore）：腿 ×2、靴 ×2、躯干、腰带、头、手 ×2，臂由各职业指定。
            Add(counts, Cylinder, 2);        // 腿
            Add(counts, Box, 2);             // 靴
            Add(counts, Sphere, 1);          // 头
            Add(counts, SphereSmall, 2);     // 手
            Add(counts, Cylinder, 1);        // 腰带
            Add(counts, TorsoStandard, 1);   // 躯干（各职业可能替换）

            switch (profession)
            {
                case CrewProfession.Sailor:
                    Add(counts, Cylinder, 2);        // 臂 ×2
                    Add(counts, BandanaStandard, 1);
                    Add(counts, SphereSmall, 3);     // 眼 ×2 + 头巾结
                    Add(counts, Cylinder, 1);        // 刀柄
                    Add(counts, Box, 1);             // 刀身
                    break;

                case CrewProfession.Bombardier:
                    Add(counts, Cylinder, 2);        // 臂 ×2
                    Replace(counts, TorsoStandard, TorsoWide);
                    Add(counts, BandanaTight, 1);
                    Add(counts, SphereSmall, 6);     // 眼 ×2 + 肩甲 ×2 + 火药袋 + 炸弹
                    Add(counts, Cylinder, 2);        // 火药袋口 + 引线
                    Add(counts, Box, 1);             // 围裙
                    break;

                case CrewProfession.Sniper:
                    Add(counts, Capsule, 2);         // 细胶囊臂 ×2
                    Replace(counts, TorsoStandard, TorsoNarrow);
                    Add(counts, BandanaLow, 1);
                    Add(counts, SphereSmall, 1);     // 眼罩
                    Add(counts, Box, 2);             // 眼罩带 + 木托
                    Add(counts, Cylinder, 1);        // 长枪管
                    break;

                case CrewProfession.Hook:
                    Add(counts, Cylinder, 2);        // 臂 ×2
                    Add(counts, BandanaStandard, 1);
                    Add(counts, SphereSmall, 3);     // 眼 ×2 + 绳结
                    Add(counts, Cylinder, 3);        // 木腿 + 铁箍 + 绳腰带
                    Add(counts, Cylinder, 1);        // 刀柄
                    Add(counts, Box, 1);             // 刀身
                    Add(counts, HookMesh, 1);
                    break;

                case CrewProfession.Arsonist:
                    Add(counts, Cylinder, 2);        // 臂 ×2
                    Add(counts, BandanaStandard, 1); // 头发
                    Add(counts, HairClump, 3);
                    Add(counts, Beard, 1);
                    Add(counts, Cylinder, 3);        // 火把柄 + 燃烧瓶身 + 瓶颈
                    Add(counts, SphereSmall, 2);     // 火球 + 布塞
                    Add(counts, Cylinder, 1);        // 肩挂油壶
                    break;

                case CrewProfession.Skeleton:
                    Add(counts, Cylinder, 2);        // 骨臂 ×2
                    Replace(counts, TorsoStandard, TorsoRib);
                    Add(counts, SphereSmall, 7);     // 眼窝 ×2 + 脊椎 ×4 + 骨棒球
                    Add(counts, Box, 2);             // 下颌 + 破头巾
                    Add(counts, RibMesh, 3);         // 肋骨 ×3
                    Add(counts, Cylinder, 1);        // 骨头棒
                    break;

                case CrewProfession.Captain:
                    Add(counts, Cylinder, 4);        // 臂 ×2 + 袖口 ×2
                    Replace(counts, TorsoStandard, TorsoCaptain);
                    Add(counts, Tricorn, 1);
                    Add(counts, Beard, 1);
                    Add(counts, CoatSkirt, 1);
                    Add(counts, SphereSmall, 2);     // 肩章 ×2
                    Add(counts, Box, 2);             // 流苏 ×2
                    Add(counts, Cylinder, 2);        // 护手 + 剑柄
                    Add(counts, Box, 1);             // 剑身
                    break;
            }

            return counts;
        }

        /// <summary>单职业三角面合计（用真实 MeshData 统计，非估算）。</summary>
        public static int TotalTriangles(CrewProfession profession)
        {
            Dictionary<string, MeshData> meshes = BuildAll();
            Dictionary<string, int> counts = CountPartInstances(profession);

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

        static void Replace(Dictionary<string, int> counts, string from, string to)
        {
            int count = counts.TryGetValue(from, out int c) ? c : 0;
            counts.Remove(from);
            if (count > 0)
                Add(counts, to, count);
        }
    }
}
