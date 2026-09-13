using System;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 空间音频的纯规则（可无头测试）：2D/3D 混合、距离衰减、拖拽力度映射、地雷蜂鸣强度。
    ///
    /// 【与 Unity AudioSource 的对应关系（重要）】
    ///   · <see cref="SpatialBlend"/> → <c>AudioSource.spatialBlend</c>（0 = 2D，1 = 3D）；
    ///   · <see cref="VolumeAtDistance"/> 精确复刻 Unity 的 <b>Linear</b> 衰减曲线
    ///     （<c>rolloffMode = AudioRolloffMode.Linear</c>）：minDistance 内不衰减，
    ///     minDistance→maxDistance 线性降到 0，超过 maxDistance 静音。
    ///     AudioService 就按这套值设置 AudioSource，因此本函数不是「示意」而是**实际口径**，
    ///     测试对它的单调性断言等于对实机衰减的断言。
    ///   · 空间音必须用单声道片段（见 <see cref="Synth.AudioBuffer"/> 注释）。
    ///
    /// 【为什么用 Linear 而不是 Logarithmic】Logarithmic（Unity 默认）的音量公式
    /// 没有公开精确表达式，无法用纯函数精确建模；Linear 公式确定、可测、可预期，
    /// 且对「竞技场 20–60 单位的小尺度场景」听感差异很小。属 **提案/待定**。
    /// </summary>
    public static class SpatialAudioRules
    {
        /// <summary>空间模式 → spatialBlend（0 = 2D，1 = 3D）。</summary>
        public static float SpatialBlend(SpatialMode mode)
        {
            return mode == SpatialMode.ThreeD ? 1f : 0f;
        }

        /// <summary>
        /// 距离衰减（Unity Linear rolloff 口径）：distance ≤ min → 1；
        /// min &lt; distance &lt; max → 线性下降；distance ≥ max → 0。
        /// 对 distance 单调不增。
        /// </summary>
        public static float VolumeAtDistance(float distance, float minDistance, float maxDistance)
        {
            if (maxDistance <= minDistance)
                return distance <= minDistance ? 1f : 0f;
            if (distance <= minDistance)
                return 1f;
            if (distance >= maxDistance)
                return 0f;

            return (maxDistance - distance) / (maxDistance - minDistance);
        }

        /// <summary>
        /// 投掷 whoosh 的音高映射：拖拽距离越大 → 出手越快 → 音高越高。
        /// 映射到 [<paramref name="minPitch"/>, <paramref name="maxPitch"/>]，
        /// 并对 <paramref name="maxDrag"/> ≤ 0 做兜底（返回 1.0，不改变音高）。
        /// </summary>
        public static float PitchForDrag(float drag, float maxDrag, float minPitch = 0.86f, float maxPitch = 1.18f)
        {
            if (maxDrag <= 0f)
                return 1f;

            double k = drag / (double)maxDrag;
            if (k < 0d) k = 0d;
            if (k > 1d) k = 1d;
            return (float)(minPitch + (maxPitch - minPitch) * k);
        }

        /// <summary>投掷 whoosh 的音量映射：远抛更响（0.6–1.0）。</summary>
        public static float VolumeForDrag(float drag, float maxDrag, float minVolume = 0.60f, float maxVolume = 1f)
        {
            if (maxDrag <= 0f)
                return maxVolume;

            double k = drag / (double)maxDrag;
            if (k < 0d) k = 0d;
            if (k > 1d) k = 1d;
            return (float)(minVolume + (maxVolume - minVolume) * k);
        }

        /// <summary>
        /// 地雷蜂鸣强度：引信越接近引爆（elapsedFrames 越大）越响、越急促，
        /// 给玩家「快炸了」的紧迫感。区间取 <c>WeaponTriggerRules.MineBeepTimes</c>
        /// 的最后一个值（§5.2 beepTimes 末项 = 59 帧）。
        /// 返回 [<paramref name="minGain"/>, 1]。
        /// </summary>
        public static float MineBeepGain(int elapsedFrames, int lastBeepFrame = 59, float minGain = 0.45f)
        {
            if (lastBeepFrame <= 0)
                return 1f;

            double k = elapsedFrames / (double)lastBeepFrame;
            if (k < 0d) k = 0d;
            if (k > 1d) k = 1d;
            return (float)(minGain + (1d - minGain) * k);
        }

        /// <summary>判断距离是否在可听范围内（用于提前短路一次播放请求）。</summary>
        public static bool IsAudible(float distance, float maxDistance)
        {
            return distance < maxDistance;
        }
    }
}
