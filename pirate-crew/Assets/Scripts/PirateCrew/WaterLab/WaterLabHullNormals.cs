using UnityEngine;

namespace PirateCrew.WaterLab
{
    // =========================================================================
    // WaterLabHullNormals —— 植物凸包代理网格的**原作半精度法线表**（F3）。
    //
    // 来源（全部第一手）：sharedassets0.assets 13 个 <植物>_01_Collider Mesh 的
    // m_VertexData（经 m_StreamData → sharedassets0.assets.resS，每格 stride 36B），
    // channels_stream0 offset 12..19 = half4（IEEE Float16 ×4，dimension=52 的 Typedef
    // 歧义以字节宽 8 实读为准）：xyz = 法线（实测全部 |n|=1.000），w = 0。
    // 提取脚本 rt/extract_hull_normals.py（UnityPy read_typetree + resS 切片），
    // 字节级副本 rt/hull-normals.json（normals_half4_hex 逐顶点）。
    //
    // 【为什么不能静态重推】凸包是图集剪影的代理几何（G-plants.md §0.1），法线是
    // 烘焙期资产字节，不是高度场可解析重构的公式值（F1-plant.md §4.1 移交结论）。
    //
    // 【PlantBake 接线（I6b 域，3 行）】WaterLabPlantBake.BuildMesh 现有
    // `mesh.RecalculateNormals()`（:84 附近）替换为：
    //     if (!WaterLabHullNormals.Apply(hullMeshName, mesh))
    //         mesh.RecalculateNormals();   // 表未覆盖时回退近似（13/13 已覆盖）
    // Apply 内部 = 解码 half4 → mesh.SetNormals(Vector3[])（顶点数与 mesh 一致才写）。
    // shader 消费侧（ReplicaPlant VS 的 TEXCOORD1 假光照 NdotL）随 mesh.normals 生效。
    // =========================================================================
    public static class WaterLabHullNormals
    {
        // 每格 8 字节 half4 的十六进制串（半精度直存，运行时解码——比 float 表小一半）
        static readonly string[][] Tables =
        {

            /* Kelp_01_Collider (40 verts) */ new[]
            {
                "123ab3a6353900009439063288390000eb34b82e913b00003e333330b93b0000a2b904aea139000001ba33302f3900009d39ab335e3900000a372c39fd380000d434d6326f3b00001c33eb31a93b0000f1b9ec31273900005ebb323669aa0000f0b9ec3127b90000bcbaec310f38000084bb4f356fad0000b53bba324bb10000",
                "36398731e9b90000e138d3381d380000503a163396380000d43aaca62a380000e73b33304fad00003039ac2f04ba0000d93ba72e3eb1000031380ca6d0ba00003b38c831a2ba000000b9c83113ba000030384a394c38000019bba7366bb2000016b9123017ba000039381230b8ba0000273602a662bb00002fb908a618ba0000",
                "773ab3aab2380000a9b93caaa3b900003a373caa20bb00000dbabeaf2439000001ba33302fb9000071ba10aeae380000c9ba33301c38000012ba02a635b90000"
            },
            /* Seagrass_01_Collider (11 verts) */ new[]
            {
                "863a083892b40000d638523906370000ce30c7b3ab3b0000c032b93be8b00000952bd2397839000063b35239afb900002cb4b93b3b810000813935b800b8000081b935380038000081b9353800b8000099bb5cb4f5b00000"
            },
            /* RedAlgae_01_Collider (15 verts) */ new[]
            {
                "913b65a42e350000663913afd6b90000a83370398cb9000046387cbaa8b30000deb047b574bb0000bdba3233d5b700001ab76dba5cb600009ab465bb0234000084bbe5b4f9300000e634beb90039000080b9cc39452a000069b7b6b1f23a00009c2df535643b0000c4a3a73ba7340000f0395d39191e0000"
            },
            /* LettuceCoral_01_Collider (17 verts) */ new[]
            {
                "d73876372a390000632c0cb6633b0000fa3845b9c5360000aab4e9350f3b000088a5ff3b50a70000f2b71db9b3380000e73b9f3057ab0000c9ba3635ae360000c13886b999b6000012399d32f6b90000f4b15eb9beb90000382afdbbae23000035b1e5bbc59e000056badfb865a90000f4ba2a34bbb60000a031b63620bb0000",
                "cfb61d34f1ba0000"
            },
            /* GroovedBrainCoral_01_Collider (32 verts) */ new[]
            {
                "523bd1b3223500003a3b81b42cb50000503b4034e8b40000ec38adb83cb800001138d3ba922f0000803a7d38123100002b399233ceb900008435153bffb400000638a438213900001b32963b11340000643995b6e9380000cd3670ae323b0000c333d1b9243900004bb4323a9538000008ac5234b03b0000b6b554b8183a0000",
                "17b19cbb3a340000642bb7bb20b400003cb65e3bf8a30000d4b818304c3a000080b9ccb93d2a00004dba813801340000efb8183734b9000045bb60b11b360000e6bb092d5eb00000b8b2e03a77b70000842c5b3715bb00003f2dabb1d9bb000005ad50bae0b800007fb89ab90db70000bbba83b02db8000090b560b501bb0000"
            },
            /* CarnationCoral_01_Collider (13 verts) */ new[]
            {
                "2e3276afcbbb000087b885b432ba0000e2ac8c39bcb9000059aceebb56af0000d5bb95319fae0000673a363507b800006d3befb51e2500002739c939fe3300007e3ad6aca33800001fb6b9b8af3900001fb3cc3bc3a2000098b4dd38ec3900005f331f30b73b0000"
            },
            /* ElkhornCoral_01_Collider (9 verts) */ new[]
            {
                "af390b3764b80000e935f13978380000893a43b41738000043b9fa3936ae0000e8b6b22c313b000092b74e2d04bb0000ff3431b857ba0000c22af2bba22e0000d0ba08b8a3300000"
            },
            /* GreyTussock_01_Collider (13 verts) */ new[]
            {
                "ce3a3538bd820000313808387f390000a935d239b4b80000ce36ce3af2340000922bd2397839000017a50838e9ba0000ce36ce3af2b400000eb9d2394834000033b1ce3a0038000028adb93befb30000922bd23978b90000ddb70838afb90000e4b5203b48b40000"
            },
            /* Yarrow_01_Collider (13 verts) */ new[]
            {
                "5939d239f53000008030203bed360000942bd2397839000032b7d23925380000b803b180003c000029adb93bf033000011b7c733e9ba00007b39c7337fb900000eb9d23947b40000962bd23978b90000d538523906b70000ce36ce3af2b4000033b1ce3a00b80000"
            },
            /* Switchgrass_01_Collider (11 verts) */ new[]
            {
                "2cb4b93bdf03000029adb93bef33000029adb93bef330000c032b93be8300000c032b93be7300000b502003cfa8300002cb4b93b1c86000029adb93befb300004837203bb4000000c032b93be8b0000029adb93befb30000"
            },
            /* Goldenrod_01_Collider (16 verts) */ new[]
            {
                "4629febb361d00000cabd8b85aba0000693911b60eb90000d0b983b845b6000058b3563551bb00000f3ab4b88d3400003c3921386cb80000abb7003b42ac000055bb8f347db400003b3af638d42d0000b8360e38063a00001b3212b7033b000091b8a6b8a438000088b1fd381a3a00002dbad233b1380000dbbb15b074300000"
            },
            /* BuffaloGrass_01_Collider (13 verts) */ new[]
            {
                "2cb4b93b0000000029adb93befb30000e4b5203b48b400002cb4b93b2703000028adb93bf0b30000e4b5203b4834000034b1ce3a0038000028adb93bf0330000ce36ce3af2b40000c032b93be73000008030203bed360000ce36ce3af2340000c032b93be8b00000"
            },
            /* WheatGrass_01_Collider (14 verts) */ new[]
            {
                "c032b93be730000028adb93bf0330000c032b93be73000002cb4b93b3b02000029adb93bef3300009e81003cad0000002cb4b93b7c810000c032b93be7b000002aadb93befb3000028adb93bf0b30000c032b93be73000008030203bedb60000da82003cde810000c032b93be7b00000"
            },
        };

        static readonly string[] Names =
        {
            "Kelp_01_Collider",
            "Seagrass_01_Collider",
            "RedAlgae_01_Collider",
            "LettuceCoral_01_Collider",
            "GroovedBrainCoral_01_Collider",
            "CarnationCoral_01_Collider",
            "ElkhornCoral_01_Collider",
            "GreyTussock_01_Collider",
            "Yarrow_01_Collider",
            "Switchgrass_01_Collider",
            "Goldenrod_01_Collider",
            "BuffaloGrass_01_Collider",
            "WheatGrass_01_Collider",
        };

        /// <summary>表覆盖的凸包名（= <see cref="ReplicaPlantParams.HullMeshName"/>，13/13）。</summary>
        public static int CoveredCount => Names.Length;

        /// <summary>IEEE half → float（逐字节表解码用；Unity 2022.3 无 System.Half）。</summary>
        static float HalfToFloat(ushort h)
        {
            int sign = (h >> 15) & 1;
            int exp = (h >> 10) & 0x1F;
            int man = h & 0x3FF;
            int bits;
            if (exp == 0) bits = man != 0 ? (sign << 31) | 0x38000000 | (man << 13) : sign << 31;
            else if (exp == 31) bits = (sign << 31) | 0x7F800000 | (man << 13);
            else bits = (sign << 31) | ((exp - 15 + 127) << 23) | (man << 13);
            return System.BitConverter.Int32BitsToSingle(bits);
        }

        /// <summary>
        /// 取一组凸包法线（half4→Vector3，w 丢弃=0）。顶点数以表为准，
        /// 与 placement JSON mesh_full.vertexCount 逐组一致。
        /// </summary>
        public static bool TryGet(string hullMeshName, Vector3[] normals)
        {
            for (int t = 0; t < Names.Length; t++)
            {
                if (Names[t] != hullMeshName)
                    continue;
                string packed = string.Concat(Tables[t]);
                int count = packed.Length / 16;   // 16 hex chars = 8 bytes = half4
                if (normals == null || normals.Length != count)
                    return false;
                for (int i = 0; i < count; i++)
                {
                    int o = i * 16;
                    // 表内是**字节序** hex（resS 原样拷贝）：每半精度 2 字节 LE，
                    // 高位字节在前拼回 ushort（如 "123a" → 0x3a12 = 0.7588）。
                    ushort x = System.Convert.ToUInt16(packed.Substring(o + 2, 2) + packed.Substring(o, 2), 16);
                    ushort y = System.Convert.ToUInt16(packed.Substring(o + 6, 2) + packed.Substring(o + 4, 2), 16);
                    ushort z = System.Convert.ToUInt16(packed.Substring(o + 10, 2) + packed.Substring(o + 8, 2), 16);
                    normals[i] = new Vector3(HalfToFloat(x), HalfToFloat(y), HalfToFloat(z));
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// PlantBake 接线助手：表内有该凸包时写入 mesh 法线并返回 true；
        /// 未覆盖（或顶点数不符）返回 false，调用方回退 RecalculateNormals。
        /// </summary>
        public static bool Apply(string hullMeshName, Mesh mesh)
        {
            if (mesh == null)
                return false;
            var normals = new Vector3[mesh.vertexCount];
            if (!TryGet(hullMeshName, normals))
                return false;
            mesh.SetNormals(normals);
            return true;
        }
    }
}
