using System;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>环境底床的一层（纯值类型）。</summary>
    public readonly struct AmbientBedLayer
    {
        /// <summary>该层用哪个音效（必须是 <see cref="SfxRecipe.Loop"/> 为 true 的循环音）。</summary>
        public readonly SfxId Id;

        /// <summary>默认相对权重（0–1，乘在配方的 DefaultVolume 之上，用来平衡三层）。</summary>
        public readonly float DefaultWeight;

        /// <summary>人可读层名（报告/README 用）。</summary>
        public readonly string Label;

        public AmbientBedLayer(SfxId id, float defaultWeight, string label)
        {
            Id = id;
            DefaultWeight = defaultWeight;
            Label = label;
        }
    }

    /// <summary>
    /// 环境底床的分层混音与「镜头离场中心越远越轻」的衰减规则（纯 C#，可无头测试）。
    ///
    /// 【为什么要分层】场景氛围不是一条循环能撑起来的：海浪给低频体量、风声给中频空气感、
    /// 垫底 pad 给持续的和声背景。三者各自可调音量，才能在不改素材的前提下配平听感。
    ///
    /// 【为什么随镜头距离衰减】玩家把镜头推向场边时，环境底床应该跟着「退远」一点，
    /// 否则会盖住近处的战斗音效。只做**轻微**衰减（最多到 <see cref="MinDistanceGain"/>），
    /// 不做静音：环境音是氛围地基，静音会让场景「发空」。
    ///
    /// 数值（层权重 / 40–140 单位 / 0.78 下限）为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public static class AmbientBedRules
    {
        /// <summary>底床分层（顺序即播放顺序；全部必须是循环音）。</summary>
        public static readonly AmbientBedLayer[] Layers =
        {
            new AmbientBedLayer(SfxId.WavesLoop, 1.00f, "海浪"),
            new AmbientBedLayer(SfxId.WindLoop, 0.85f, "海风"),
            new AmbientBedLayer(SfxId.BedPad, 0.90f, "垫底"),
        };

        /// <summary>镜头离场中心这个距离之内不衰减（世界单位）。</summary>
        public const float NearDistance = 40f;

        /// <summary>镜头离场中心超过这个距离后不再继续衰减（世界单位）。</summary>
        public const float FarDistance = 140f;

        /// <summary>距离衰减的下限增益（轻微衰减而非静音）。</summary>
        public const float MinDistanceGain = 0.78f;

        /// <summary>鸟鸣点缀默认间隔（秒）。</summary>
        public const double BirdMinIntervalSeconds = 6d;
        public const double BirdMaxIntervalSeconds = 14d;

        /// <summary>鸟鸣音量相对配方的缩放。</summary>
        public const float BirdVolumeScale = 1f;

        /// <summary>层数。</summary>
        public static int LayerCount => Layers.Length;

        /// <summary>某层的索引；不是底床层返回 -1。</summary>
        public static int IndexOf(SfxId id)
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                if (Layers[i].Id == id)
                    return i;
            }

            return -1;
        }

        /// <summary>是否属于底床层。</summary>
        public static bool IsBedLayer(SfxId id)
        {
            return IndexOf(id) >= 0;
        }

        /// <summary>
        /// 距离衰减增益：<paramref name="distance"/> ≤ near → 1；
        /// near→far 线性降到 <paramref name="minGain"/>；超过 far 保持 minGain。
        /// 对距离单调不增，且恒 &gt; 0（环境音不静音）。
        /// </summary>
        public static float DistanceGain(
            float distance,
            float nearDistance = NearDistance,
            float farDistance = FarDistance,
            float minGain = MinDistanceGain)
        {
            if (minGain < 0f) minGain = 0f;
            if (minGain > 1f) minGain = 1f;
            if (farDistance <= nearDistance)
                return distance <= nearDistance ? 1f : minGain;
            if (distance <= nearDistance)
                return 1f;
            if (distance >= farDistance)
                return minGain;

            double k = (distance - nearDistance) / (double)(farDistance - nearDistance);
            return (float)(1d + (minGain - 1d) * k);
        }

        /// <summary>某层的最终音量：配方默认音量 × 总线增益 × 层权重 × 距离增益（钳到 0–1）。</summary>
        public static float LayerVolume(float recipeDefaultVolume, float busGain, float weight, float distanceGain)
        {
            if (recipeDefaultVolume < 0f) recipeDefaultVolume = 0f;
            if (busGain < 0f) busGain = 0f;
            if (weight < 0f) weight = 0f;
            if (distanceGain < 0f) distanceGain = 0f;

            float volume = recipeDefaultVolume * busGain * weight * distanceGain;
            return volume > 1f ? 1f : volume;
        }

        /// <summary>由两点世界坐标求镜头到场景中心的水平距离（Y 不参与，避免俯仰抬高造成误衰减）。</summary>
        public static float CameraDistance(float cameraX, float cameraZ, float centerX, float centerZ)
        {
            double dx = cameraX - centerX;
            double dz = cameraZ - centerZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// 环境底床的可调混音（每层音量 + 鸟鸣触发间隔）。纯 C#，可在无头验证台断言。
    ///
    /// 【为什么单独一个类】三层的权重与鸟鸣频率是「手感参数」，会随听觉验收反复改；
    /// 放在可测的值对象里，AudioService 只负责读它、不再散落常量。
    /// </summary>
    public sealed class AmbientBedMix
    {
        readonly float[] _weights;
        double _birdMin = AmbientBedRules.BirdMinIntervalSeconds;
        double _birdMax = AmbientBedRules.BirdMaxIntervalSeconds;
        float _birdVolumeScale = AmbientBedRules.BirdVolumeScale;

        public AmbientBedMix()
        {
            _weights = new float[AmbientBedRules.LayerCount];
            Reset();
        }

        /// <summary>恢复默认权重与鸟鸣参数。</summary>
        public void Reset()
        {
            for (int i = 0; i < _weights.Length; i++)
                _weights[i] = AmbientBedRules.Layers[i].DefaultWeight;

            _birdMin = AmbientBedRules.BirdMinIntervalSeconds;
            _birdMax = AmbientBedRules.BirdMaxIntervalSeconds;
            _birdVolumeScale = AmbientBedRules.BirdVolumeScale;
        }

        /// <summary>层数。</summary>
        public int LayerCount => _weights.Length;

        /// <summary>第 i 层的音效 id。</summary>
        public SfxId LayerId(int index)
        {
            return AmbientBedRules.Layers[index].Id;
        }

        /// <summary>第 i 层的权重。</summary>
        public float GetWeightAt(int index)
        {
            return _weights[index];
        }

        /// <summary>设置第 i 层的权重（负值钳到 0）。</summary>
        public void SetWeightAt(int index, float weight)
        {
            _weights[index] = weight < 0f ? 0f : weight;
        }

        /// <summary>取某层的权重；不是底床层返回 0。</summary>
        public float GetWeight(SfxId id)
        {
            int index = AmbientBedRules.IndexOf(id);
            return index < 0 ? 0f : _weights[index];
        }

        /// <summary>设置某层权重；不是底床层时忽略。</summary>
        public void SetWeight(SfxId id, float weight)
        {
            int index = AmbientBedRules.IndexOf(id);
            if (index >= 0)
                SetWeightAt(index, weight);
        }

        /// <summary>鸟鸣最短间隔（秒）。</summary>
        public double BirdMinIntervalSeconds => _birdMin;

        /// <summary>鸟鸣最长间隔（秒）。</summary>
        public double BirdMaxIntervalSeconds => _birdMax;

        /// <summary>鸟鸣音量缩放。</summary>
        public float BirdVolumeScale => _birdVolumeScale;

        /// <summary>设置鸟鸣触发间隔；保证 0 &lt; min ≤ max。</summary>
        public void SetBirdInterval(double minSeconds, double maxSeconds)
        {
            if (minSeconds < 0.05d) minSeconds = 0.05d;
            if (maxSeconds < minSeconds) maxSeconds = minSeconds;
            _birdMin = minSeconds;
            _birdMax = maxSeconds;
        }

        /// <summary>设置鸟鸣音量缩放（负值钳到 0）。</summary>
        public void SetBirdVolumeScale(float scale)
        {
            _birdVolumeScale = scale < 0f ? 0f : scale;
        }

        /// <summary>取下一次鸟鸣的间隔秒数（<paramref name="roll"/> ∈ [0,1)）。</summary>
        public double NextBirdInterval(double roll)
        {
            if (roll < 0d) roll = 0d;
            if (roll > 1d) roll = 1d;
            return _birdMin + roll * (_birdMax - _birdMin);
        }
    }
}
