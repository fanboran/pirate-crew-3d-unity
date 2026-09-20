using System;

namespace PirateCrew.Audio.Synth
{
    /// <summary>
    /// 程序化合成的中间缓冲（纯 C#，不引用 UnityEngine，可在无头验证台实例化与测试）。
    ///
    /// 【采样率与声道约定（全音频模块唯一口径）】
    ///   · <see cref="DefaultSampleRate"/> = 44100 Hz（CD 标准；也是 SfxCatalog 全部配方的采样率）。
    ///   · 采样数据按 **交错（interleaved）** 存放：frame i 的第 c 声道在
    ///     <c>Samples[i * Channels + c]</c>。
    ///   · 本模块当前所有资产均为 **单声道（Mono）**——这不是省事：Unity 要求 3D 空间音
    ///     使用单声道片段，立体声片段会被引擎忽略 spatialBlend 直接平铺（Unity 官方限制）。
    ///     单声道同时让离线 wav 体积最小、合成器实现最直接。立体声宽度交给 AudioSource
    ///     （panStereo / spatialBlend）与混响在播放侧表达，见 AudioService。
    ///   · 采样值域约定为 [-1, 1]；渲染出口统一归一化到 <see cref="SfxCatalog.PeakTarget"/>，
    ///     不在中间步骤硬削波（削波会产生刺耳的谐波，需在合成阶段而非播放阶段避免）。
    ///
    /// 【设计取舍】用 class 而非 struct：合成过程中反复整体读写，堆上单实例更省心；
    /// 且本类型只承载 float[]，不触发 Unity 的 <c>ECall</c> 边界。
    /// </summary>
    public sealed class AudioBuffer
    {
        /// <summary>本模块统一采样率（Hz）。</summary>
        public const int DefaultSampleRate = 44100;

        /// <summary>单声道声道数常量。</summary>
        public const int Mono = 1;

        /// <summary>交错采样数据；长度 = FrameCount * Channels。</summary>
        public float[] Samples;

        /// <summary>声道数（本模块恒为 1）。</summary>
        public int Channels;

        /// <summary>采样率（Hz）。</summary>
        public int SampleRate;

        /// <summary>按帧数创建零填充缓冲。</summary>
        public AudioBuffer(int frameCount, int sampleRate = DefaultSampleRate, int channels = Mono)
        {
            if (frameCount < 0)
                frameCount = 0;
            if (sampleRate <= 0)
                sampleRate = DefaultSampleRate;
            if (channels <= 0)
                channels = Mono;

            Channels = channels;
            SampleRate = sampleRate;
            Samples = new float[frameCount * channels];
        }

        /// <summary>帧数（每声道采样个数）。</summary>
        public int FrameCount => Channels <= 0 ? 0 : Samples.Length / Channels;

        /// <summary>时长（秒）。</summary>
        public double Duration => SampleRate <= 0 ? 0d : (double)FrameCount / SampleRate;

        /// <summary>按秒数换算帧数（四舍五入，保证离线渲染时长与配方声明一致）。</summary>
        public static int FramesForSeconds(double seconds, int sampleRate = DefaultSampleRate)
        {
            if (seconds <= 0d)
                return 0;
            return (int)Math.Round(seconds * sampleRate, MidpointRounding.AwayFromZero);
        }

        /// <summary>创建一段静音缓冲。</summary>
        public static AudioBuffer Silence(double seconds, int sampleRate = DefaultSampleRate, int channels = Mono)
        {
            return new AudioBuffer(FramesForSeconds(seconds, sampleRate), sampleRate, channels);
        }

        /// <summary>取第 frame 帧第 channel 声道的样本（越界返回 0）。</summary>
        public float GetSample(int frame, int channel = 0)
        {
            if (frame < 0 || frame >= FrameCount || channel < 0 || channel >= Channels)
                return 0f;
            return Samples[frame * Channels + channel];
        }

        /// <summary>写第 frame 帧第 channel 声道的样本（越界忽略）。</summary>
        public void SetSample(int frame, float value, int channel = 0)
        {
            if (frame < 0 || frame >= FrameCount || channel < 0 || channel >= Channels)
                return;
            Samples[frame * Channels + channel] = value;
        }

        /// <summary>叠加样本（越界忽略）。</summary>
        public void AddSample(int frame, float value, int channel = 0)
        {
            if (frame < 0 || frame >= FrameCount || channel < 0 || channel >= Channels)
                return;
            Samples[frame * Channels + channel] += value;
        }

        /// <summary>峰值绝对值（空缓冲返回 0）。</summary>
        public float Peak()
        {
            float peak = 0f;
            for (int i = 0; i < Samples.Length; i++)
            {
                float v = Samples[i] < 0f ? -Samples[i] : Samples[i];
                if (v > peak)
                    peak = v;
            }

            return peak;
        }

        /// <summary>均方根（用于滤波器衰减的量化判据，测试用）。</summary>
        public float Rms()
        {
            if (Samples.Length == 0)
                return 0f;

            double sum = 0d;
            for (int i = 0; i < Samples.Length; i++)
                sum += (double)Samples[i] * Samples[i];

            return (float)Math.Sqrt(sum / Samples.Length);
        }

        /// <summary>整体乘以增益（返回自身便于链式调用）。</summary>
        public AudioBuffer Scale(float gain)
        {
            for (int i = 0; i < Samples.Length; i++)
                Samples[i] *= gain;

            return this;
        }

        /// <summary>
        /// 归一化到目标峰值。<paramref name="peakTarget"/> ≤ 0 或当前峰值为 0 时不动。
        /// 返回自身便于链式调用。
        /// </summary>
        public AudioBuffer NormalizeTo(float peakTarget)
        {
            float peak = Peak();
            if (peak <= 1e-6f || peakTarget <= 0f)
                return this;

            return Scale(peakTarget / peak);
        }

        /// <summary>
        /// 对缓冲前 <paramref name="seconds"/> 秒做线性淡入（防爆音 declick）。
        /// </summary>
        public void ApplyFadeIn(double seconds)
        {
            int frames = Math.Min(FramesForSeconds(seconds, SampleRate), FrameCount);
            if (frames <= 1)
                return;

            for (int f = 0; f < frames; f++)
            {
                float w = (float)f / (frames - 1);
                for (int c = 0; c < Channels; c++)
                    Samples[f * Channels + c] *= w;
            }
        }

        /// <summary>对缓冲尾部 <paramref name="seconds"/> 秒做线性淡出。</summary>
        public void ApplyFadeOut(double seconds)
        {
            int frames = Math.Min(FramesForSeconds(seconds, SampleRate), FrameCount);
            if (frames <= 1)
                return;

            int start = FrameCount - frames;
            for (int f = 0; f < frames; f++)
            {
                float w = 1f - (float)f / (frames - 1);
                int frame = start + f;
                for (int c = 0; c < Channels; c++)
                    Samples[frame * Channels + c] *= w;
            }
        }

        /// <summary>首尾各做 <paramref name="seconds"/> 秒淡入淡出（一次性 declick）。</summary>
        public void ApplyDeclick(double seconds)
        {
            ApplyFadeIn(seconds);
            ApplyFadeOut(seconds);
        }

        /// <summary>
        /// 把 <paramref name="source"/> 以 <paramref name="gain"/> 混入本缓冲（从 <paramref name="offsetFrames"/> 帧起）。
        /// 声道数取两者较小值；越界部分截断。
        /// </summary>
        public void MixIn(AudioBuffer source, int offsetFrames = 0, float gain = 1f)
        {
            if (source == null)
                return;

            int ch = Math.Min(Channels, source.Channels);
            int srcFrames = source.FrameCount;
            for (int f = 0; f < srcFrames; f++)
            {
                int dstFrame = offsetFrames + f;
                if (dstFrame < 0)
                    continue;
                if (dstFrame >= FrameCount)
                    break;

                for (int c = 0; c < ch; c++)
                    Samples[dstFrame * Channels + c] += source.Samples[f * source.Channels + c] * gain;
            }
        }

        /// <summary>深拷贝。</summary>
        public AudioBuffer Clone()
        {
            var copy = new AudioBuffer(FrameCount, SampleRate, Channels);
            Array.Copy(Samples, copy.Samples, Samples.Length);
            return copy;
        }

        /// <summary>
        /// 把「连续生成、含 <paramref name="tailFrames"/> 帧冗余尾巴」的缓冲折叠成
        /// 首尾无缝的循环体（长度 = 原长 - tailFrames）。
        ///
        /// 【原理】循环拼接处的不连续来自信号本身与滤波器状态的残余。做法：多生成一段尾巴，
        /// 把尾巴与头部做等功率线性交叉淡化，使新首帧 ≈ 原第 N 帧、新末帧 = 原第 N-1 帧，
        /// 而原第 N-1 与第 N 帧在同一段连续信号里相邻，故拼接处天然连续。
        /// 这是循环环境音（海浪/风声）离线渲染的标准做法，见 AmbientSynth 的用法。
        /// </summary>
        public AudioBuffer FoldSeamlessLoop(int tailFrames)
        {
            if (tailFrames <= 0 || tailFrames >= FrameCount)
                return Clone();

            int outFrames = FrameCount - tailFrames;
            var outBuf = new AudioBuffer(outFrames, SampleRate, Channels);

            // 尾部交叉淡化区：out[i] = buf[i] * (i/tail) + buf[N+i] * (1 - i/tail)
            for (int f = 0; f < tailFrames; f++)
            {
                float w = (float)f / tailFrames;          // 0 → 1
                for (int c = 0; c < Channels; c++)
                {
                    float head = Samples[f * Channels + c];
                    float tail = Samples[(outFrames + f) * Channels + c];
                    outBuf.Samples[f * Channels + c] = head * w + tail * (1f - w);
                }
            }

            for (int f = tailFrames; f < outFrames; f++)
                for (int c = 0; c < Channels; c++)
                    outBuf.Samples[f * Channels + c] = Samples[f * Channels + c];

            return outBuf;
        }
    }
}
