namespace PirateCrew.PirateCrew.Audio.Synth
{
    /// <summary>
    /// 合成专用的确定性伪随机数发生器（xorshift32；纯 C#、无 UnityEngine.Random）。
    ///
    /// 【为什么要自己写】<c>UnityEngine.Random</c> 是全局状态且带 ECall，离线批量渲染时
    /// 既不可复现也会污染运行时随机序列。音频噪声要求「同 seed 必然同波形」——
    /// 否则每次生成 wav 的哈希都不同，幂等资产重建与逐样本测试都无从谈起。
    ///
    /// 【为什么不用 System.Random】System.Random 的算法随 .NET 版本变化（.NET Core 起
    /// 与 .NET Framework 不同），跨编辑器/无头验证台可能得到不同波形；xorshift32 的
    /// 位运算在任何运行时的结果都一致。
    ///
    /// 用途：白噪声、颗粒抖动、多振荡器失谐量、木箱碎裂的碎块时间抖动。
    /// </summary>
    public struct SynthRandom
    {
        uint _state;

        /// <summary>用种子创建；种子为 0 时替换为非零常量（xorshift 不能全零）。</summary>
        public SynthRandom(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        /// <summary>下一个 32 位无符号随机数。</summary>
        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0,1) 均匀分布。</summary>
        public float NextFloat()
        {
            // 取高 24 位 → [0, 2^24) → 除以 2^24，精度足够且无符号问题
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>[-1,1) 均匀分布（双极性，噪声源用）。</summary>
        public float NextBipolar()
        {
            return NextFloat() * 2f - 1f;
        }

        /// <summary>[min,max) 均匀分布。</summary>
        public float NextRange(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>
        /// 无状态哈希 → [0,1)：由整数索引与种子直接求值，不推进内部状态。
        /// 用于「同一噪声场可被多次采样」（环境音的调制相位等）。
        /// </summary>
        public static float Hash01(int index, uint seed)
        {
            uint h = (uint)index * 0x9E3779B1u ^ seed * 0x85EBCA6Bu;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h >> 8) * (1f / 16777216f);
        }

        /// <summary>无状态哈希 → [-1,1)。</summary>
        public static float HashBipolar(int index, uint seed)
        {
            return Hash01(index, seed) * 2f - 1f;
        }
    }
}
