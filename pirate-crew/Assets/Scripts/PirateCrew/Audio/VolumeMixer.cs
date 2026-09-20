using System;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 分类音量混音器（纯 C#，无 UnityEngine，可无头测试）。
    ///
    /// 【增益结构】Master 是总闸，乘在其余三类之上：
    ///   EffectiveGain(Sfx)     = Master × Sfx
    ///   EffectiveGain(Ambient) = Master × Ambient
    ///   EffectiveGain(Music)   = Master × Music
    ///   EffectiveGain(Master)  = Master
    /// 这样「总音量拉到 0」能可靠静音全部，而各分类滑条之间互不干扰。
    ///
    /// 【为什么用乘法而不是分贝】玩家滑条语义是「0–100%」，乘法最直观；
    /// 分贝是工程口径，转成滑条要再套一层曲线，反而增加两个口径不一致的风险。
    /// 听感上乘法滑条在低端变化陡、高端变化钝，属于已知取舍（**提案/待定**：
    /// 后续若要做「感知均匀」的滑条，只改 <see cref="SetVolume"/> 的映射即可，
    /// 不影响调用方）。
    /// </summary>
    public sealed class VolumeMixer
    {
        readonly float[] _volumes;
        readonly bool[] _muted;

        /// <summary>默认全部 1.0（未静音）。</summary>
        public VolumeMixer()
        {
            _volumes = new float[AudioCategories.Count];
            _muted = new bool[AudioCategories.Count];
            for (int i = 0; i < _volumes.Length; i++)
                _volumes[i] = 1f;
        }

        /// <summary>读取某分类滑条值（不含 Master 与静音）。</summary>
        public float GetVolume(AudioCategory category)
        {
            int i = Index(category);
            return _volumes[i];
        }

        /// <summary>设置某分类滑条值（钳制到 [0,1]）。</summary>
        public void SetVolume(AudioCategory category, float volume)
        {
            int i = Index(category);
            if (volume < 0f) volume = 0f;
            if (volume > 1f) volume = 1f;
            _volumes[i] = volume;
        }

        /// <summary>该分类是否被单独静音。</summary>
        public bool IsMuted(AudioCategory category)
        {
            return _muted[Index(category)];
        }

        /// <summary>设置/取消某分类静音。</summary>
        public void SetMuted(AudioCategory category, bool muted)
        {
            _muted[Index(category)] = muted;
        }

        /// <summary>某分类的最终增益（含 Master 缩放与静音判定）。</summary>
        public float EffectiveGain(AudioCategory category)
        {
            int i = Index(category);
            if (_muted[i])
                return 0f;

            float volume = _volumes[i];
            if (!AudioCategories.IsScaledByMaster(category))
                return volume;

            // Master 自身被静音时，所有分类归零
            if (_muted[Index(AudioCategory.Master)])
                return 0f;

            return _volumes[Index(AudioCategory.Master)] * volume;
        }

        /// <summary>按索引取增益（AudioService 的播放热路径用，避免枚举装箱歧义）。</summary>
        public float EffectiveGainByIndex(int categoryIndex)
        {
            if (categoryIndex < 0 || categoryIndex >= AudioCategories.Count)
                return 0f;
            return EffectiveGain((AudioCategory)categoryIndex);
        }

        /// <summary>一次性写入四类音量（读档/设置面板初始化用）。</summary>
        public void SetAll(float master, float sfx, float ambient, float music)
        {
            SetVolume(AudioCategory.Master, master);
            SetVolume(AudioCategory.Sfx, sfx);
            SetVolume(AudioCategory.Ambient, ambient);
            SetVolume(AudioCategory.Music, music);
        }

        /// <summary>
        /// 某分类当前是否为「有效静音」（滑条为 0 或 Master 为 0 或本类静音）。
        /// 播放前用它提前短路，省掉一次合成/剪辑查找。
        /// </summary>
        public bool IsSilent(AudioCategory category)
        {
            return EffectiveGain(category) <= 0f;
        }

        /// <summary>数组索引（与枚举值同值；集中在 <see cref="AudioCategories.Index"/>）。</summary>
        static int Index(AudioCategory category)
        {
            int index = AudioCategories.Index(category);
            if (index < 0 || index >= AudioCategories.Count)
                throw new ArgumentOutOfRangeException(nameof(category));
            return index;
        }
    }
}
