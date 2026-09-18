namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 由整数坐标派生的确定性哈希（给网格顶点抖动用，不依赖遍历顺序）。
    /// 原在 <c>SceneLayoutRules.cs</c> 内；一代布局器退场时提为独立文件——
    /// 留在运行时程序集的 <see cref="MeshBuffers"/> 顶点抖动与烘焙器几何层共用本实现。
    /// </summary>
    public static class SceneArtHash
    {
        /// <summary>把 (a, b, salt) 映射到 [0,1)。</summary>
        public static float Hash01(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>把 (a, b, salt) 映射到 [-1,1)。</summary>
        public static float SignedHash(int a, int b, int salt)
        {
            return Hash01(a, b, salt) * 2f - 1f;
        }
    }
}
