using System;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 一次性音效的「变奏」规则（纯 C#，可无头测试）。
    ///
    /// 【要解决什么】同一段素材反复播放（每回合的提示音、连续的命中/爆炸、
    /// 同一种武器多次引爆）会立刻被耳朵识别为「机械重复」。给每次播放加一点
    /// 音高与音量抖动，用极小的代价消解重复感——这是本模块的第一优先级目标。
    ///
    /// 【为什么音高只抖 ±8%】±8% 约等于 ±1.4 个半音内的微差：已经足够让两次播放
    /// 听出「不是同一份拷贝」，但不会让音效跑调或与音效本身的音高设计冲突。
    /// 音量 ±10% 同理，只做轻微强弱变化，不影响「远处更轻」的语义。
    ///
    /// 【作用范围】只用于**一次性（非循环）音效**：循环音抖动会在循环点产生音高跳变，
    /// 音乐抖动会让乐句走音，两者都明确排除（见 <see cref="AppliesTo"/>）。
    ///
    /// 数值为 **AI 提案/待定**，需人耳验收（<see cref="PitchRange"/> / <see cref="VolumeRange"/>）。
    /// </summary>
    public static class AudioVariation
    {
        /// <summary>总开关：置 false 则所有一次性音效按原音量/原音高播放（便于 A/B 对比）。</summary>
        public const bool Enabled = true;

        /// <summary>音高抖动幅度（±比例，0.08 = ±8%）。</summary>
        public const float PitchRange = 0.08f;

        /// <summary>音量抖动幅度（±比例，0.10 = ±10%）。</summary>
        public const float VolumeRange = 0.10f;

        /// <summary>音高下限保护（避免极端抖动把音效拉到不可用的音区）。</summary>
        public const float MinPitch = 0.25f;

        /// <summary>音高上限保护。</summary>
        public const float MaxPitch = 3f;

        /// <summary>
        /// 该音效是否参与抖动。排除两类：
        /// ① <b>循环音</b>（海浪/风声/垫底）——抖音高会在循环拼接点产生跳变；
        /// ② <b>音乐</b>（胜负乐句）——抖音高会让乐句走音。
        /// 环境总线里的**一次性**点缀（鸟鸣）照抖：它也是反复触发的音，同样有重复感问题。
        /// </summary>
        public static bool AppliesTo(AudioCategory category, bool loop)
        {
            return !loop && category != AudioCategory.Music;
        }

        /// <summary>把 [0,1) 的随机数映射到 [-range, +range] 的乘性偏移（0.5 → 不变）。</summary>
        public static float Offset(float roll, float range)
        {
            if (range <= 0f)
                return 0f;
            if (roll < 0f) roll = 0f;
            if (roll > 1f) roll = 1f;
            return (roll * 2f - 1f) * range;
        }

        /// <summary>音高抖动：返回 <paramref name="basePitch"/> 抖动后的值（受上下限保护）。</summary>
        public static float JitterPitch(float roll, float basePitch, float range = PitchRange)
        {
            if (basePitch <= 0f)
                basePitch = 1f;
            float pitch = basePitch * (1f + Offset(roll, range));
            if (pitch < MinPitch) pitch = MinPitch;
            if (pitch > MaxPitch) pitch = MaxPitch;
            return pitch;
        }

        /// <summary>音量抖动：返回 <paramref name="baseVolume"/> 抖动后的值（不为负）。</summary>
        public static float JitterVolume(float roll, float baseVolume, float range = VolumeRange)
        {
            float volume = baseVolume * (1f + Offset(roll, range));
            return volume < 0f ? 0f : volume;
        }

        /// <summary>
        /// 一次算出音高与音量（AudioService 每次播放调一次）；
        /// <paramref name="enabled"/> 为 false 时原样返回，便于测试直接对比。
        /// </summary>
        public static void Apply(
            bool enabled,
            float pitchRoll,
            float volumeRoll,
            float basePitch,
            float baseVolume,
            out float pitch,
            out float volume)
        {
            if (!enabled)
            {
                pitch = basePitch <= 0f ? 1f : basePitch;
                volume = baseVolume < 0f ? 0f : baseVolume;
                return;
            }

            pitch = JitterPitch(pitchRoll, basePitch);
            volume = JitterVolume(volumeRoll, baseVolume);
        }
    }
}
