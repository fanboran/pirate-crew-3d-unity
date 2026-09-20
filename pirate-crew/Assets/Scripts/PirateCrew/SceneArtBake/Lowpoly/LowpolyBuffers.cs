using System.Collections.Generic;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.SceneArt.Lowpoly
{
    /// <summary>
    /// 低多边形关卡几何（Lowpoly/SceneArt）：云朵平台场生成器。
    ///
    /// 【分工】与 SceneArt 既有管线同构：
    ///   · 纯 C# 几何层（<see cref="CloudFieldGeometry"/>）
    ///     只往 <see cref="MeshBuffers"/> 写顶点/三角面——无头可测、确定性（同 seed 同几何）；
    ///   · Unity 装配层（<see cref="LowpolyStageBuilder"/>）把缓冲落成 MeshFilter + MeshRenderer
    ///     + Collider，并显式绑定程序化 URP Lit 材质（渲染铁律：不绑 = 构建里粉色炸弹）。
    ///
    /// 【flat shading】<see cref="MeshBuffers"/> 本身就是"每面独立 3 顶点 + 面法线"的平面着色
    /// 缓冲（见其类头），所以本目录全部几何天生是硬边低模色块，无需再做顶点平均。
    /// </summary>

    /// <summary>材质槽位（1 槽 = 1 合并网格 = 1 DrawCall）。颜色定义在 <see cref="LowpolyStageBuilder.ColorOf"/>。</summary>
    public enum LowpolyMaterialSlot
    {
        /// <summary>云朵暗部：暖白（云体下半）。</summary>
        CloudWarmWhite = 0,

        /// <summary>云朵亮部：淡金（按高度高于阈值的部分——"阳光晒到的地方"）。</summary>
        CloudPaleGold = 1,

        /// <summary>山体底层：暖岩色（水下基座 + 入水线一带）。</summary>
        HillRockWarm = 2,

        /// <summary>山体中层：草绿。</summary>
        HillGrassMid = 3,

        /// <summary>山体顶层：草绿偏亮。</summary>
        HillGrassTop = 4,

        /// <summary>山顶台面：沙色平缓面（可站位）。</summary>
        HillSand = 5,
    }

    /// <summary>
    /// 一次低模关卡合成的全部三角面缓冲（按材质槽分组）。纯 C#，无头可测。
    /// </summary>
    public sealed class LowpolyBuffers
    {
        public readonly MeshBuffers CloudWarmWhite = new MeshBuffers();
        public readonly MeshBuffers CloudPaleGold = new MeshBuffers();
        public readonly MeshBuffers HillRockWarm = new MeshBuffers();
        public readonly MeshBuffers HillGrassMid = new MeshBuffers();
        public readonly MeshBuffers HillGrassTop = new MeshBuffers();
        public readonly MeshBuffers HillSand = new MeshBuffers();

        /// <summary>按槽位取目标缓冲。</summary>
        public MeshBuffers Slot(LowpolyMaterialSlot slot)
        {
            switch (slot)
            {
                case LowpolyMaterialSlot.CloudPaleGold: return CloudPaleGold;
                case LowpolyMaterialSlot.HillRockWarm: return HillRockWarm;
                case LowpolyMaterialSlot.HillGrassMid: return HillGrassMid;
                case LowpolyMaterialSlot.HillGrassTop: return HillGrassTop;
                case LowpolyMaterialSlot.HillSand: return HillSand;
                default: return CloudWarmWhite;
            }
        }

        /// <summary>三角面总数（全部槽位合计，预算用）。</summary>
        public int TotalTriangles
        {
            get
            {
                int sum = 0;
                for (int i = 0; i <= 5; i++)
                    sum += Slot((LowpolyMaterialSlot)i).TriangleCount;
                return sum;
            }
        }

        /// <summary>非空槽数（= 实际 DrawCall 上限）。</summary>
        public int NonEmptySlots
        {
            get
            {
                int n = 0;
                for (int i = 0; i <= 5; i++)
                {
                    if (!Slot((LowpolyMaterialSlot)i).IsEmpty)
                        n++;
                }
                return n;
            }
        }
    }

    /// <summary>
    /// 一个站位建议点（脚底贴地坐标）。**调用方落角色时需再加
    /// <c>LevelGeometry.UnitPivotHeight</c>**（角色枢轴抬高），本结构只给"脚踩的平面"。
    /// </summary>
    public readonly struct LowpolySpawnPoint
    {
        /// <summary>脚底贴地的世界坐标（y = 平台顶面）。</summary>
        public readonly Vector3 FootPosition;

        /// <summary>人类可读标签（哪朵云 / 山顶哪个方位）。</summary>
        public readonly string Label;

        public LowpolySpawnPoint(Vector3 footPosition, string label)
        {
            FootPosition = footPosition;
            Label = label ?? "";
        }
    }

    /// <summary>
    /// 确定性哈希工具（无 System.Random：由 seed/index/salt 直接得值，与遍历顺序无关，
    /// 同 seed 必得同一几何——与 SceneArtHash 同思路的本地实现，避免跨层依赖）。
    /// </summary>
    public static class LowpolyHash
    {
        /// <summary>伪随机浮点 [0,1)，确定性。</summary>
        public static float Hash01(int seed, int index, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 747796405u
                    + (uint)index * 2891336453u
                    + (uint)salt * 1911520717u
                    + 1442695041u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h * (1f / 4294967296f);
            }
        }

        /// <summary>有符号伪随机 [-1,1)。</summary>
        public static float SignedHash(int seed, int index, int salt)
        {
            return Hash01(seed, index, salt) * 2f - 1f;
        }
    }
}
