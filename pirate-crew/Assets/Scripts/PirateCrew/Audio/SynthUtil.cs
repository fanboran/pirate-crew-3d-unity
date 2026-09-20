using System;
using PirateCrew.Audio.Synth;

namespace PirateCrew.Audio
{
    /// <summary>噪声塑形用的滤波器类型。</summary>
    public enum NoiseFilterKind
    {
        /// <summary>低通：闷响/隆隆（爆炸尾音、翻滚石块）。</summary>
        LowPass = 0,

        /// <summary>高通：嘶嘶/水花（浪花泡沫、落水）。</summary>
        HighPass = 1,

        /// <summary>带通：有明确「腔体」的音色（木箱碎裂、海鸥嘶哑、whoosh 呼啸）。</summary>
        BandPass = 2,
    }

    /// <summary>
    /// 音效合成的公共积木（纯 C#，全部离屏、确定性）。
    ///
    /// 提供三类高频复用的动作：
    ///   ① <see cref="AddFilteredNoise"/>：一段带包络的噪声，滤波参数可按块扫变
    ///      （爆炸、水花、whoosh、风声、浪声全靠它）；
    ///   ② <see cref="AddGlideTone"/>：频率指数/线性滑变的正弦（低频冲击、弹跳、海鸥滑音）；
    ///   ③ <see cref="AddDecayingPartials"/>：一组衰减正弦分音（木质腔体、金属感）。
    ///
    /// 【为什么按块更新滤波系数】逐样本调 <see cref="Biquad.SetLowPass"/> 要算 cos/sin，
    /// 离线渲染虽可接受，但一段 8 秒环境音就是 35 万次三角函数；按 64 样本一块更新，
    /// 听觉上无差别、耗时降到 1/64。块边界不连续性远小于噪声本身的随机波动，听不出来。
    /// </summary>
    public static class SynthUtil
    {
        /// <summary>首尾淡入淡出时长（秒），防止起止爆音。</summary>
        public const double DeclickSeconds = 0.004;

        /// <summary>滤波系数更新块长度（样本）。</summary>
        public const int FilterBlockSamples = 64;

        /// <summary>按秒创建零填充缓冲。</summary>
        public static AudioBuffer Create(double seconds, int sampleRate)
        {
            return new AudioBuffer(AudioBuffer.FramesForSeconds(seconds, sampleRate), sampleRate, AudioBuffer.Mono);
        }

        /// <summary>
        /// 叠加一段噪声（白噪声 → 滤波器 → 包络）。
        /// <paramref name="attackSeconds"/> 为线性起音；<paramref name="decayTauSeconds"/> &gt; 0
        /// 时按 exp(-t/tau) 衰减，≤ 0 表示不衰减（持续型纹理，收尾靠外层淡出）。
        /// 截止频率从 <paramref name="cutoffFromHz"/> 指数滑到 <paramref name="cutoffToHz"/>。
        /// </summary>
        public static void AddFilteredNoise(
            AudioBuffer target,
            ref SynthRandom rng,
            double startSeconds,
            double durationSeconds,
            float gain,
            double attackSeconds,
            double decayTauSeconds,
            NoiseFilterKind kind,
            double cutoffFromHz,
            double cutoffToHz,
            double q = 0.8d)
        {
            if (target == null || durationSeconds <= 0d || gain == 0f)
                return;

            // 防御：扫频起止必须为正，否则 Math.Pow(比值) 会得到 NaN/Inf
            if (cutoffFromHz < 1d)
                cutoffFromHz = 1d;
            if (cutoffToHz < 1d)
                cutoffToHz = 1d;

            int sampleRate = target.SampleRate;
            int startFrame = (int)Math.Round(startSeconds * sampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, sampleRate);
            if (frames <= 0)
                return;

            Biquad filter = default;
            int blockLeft = 0;

            for (int f = 0; f < frames; f++)
            {
                int dst = startFrame + f;
                if (dst >= target.FrameCount)
                    break;

                double t = (double)f / sampleRate;
                if (blockLeft <= 0)
                {
                    double k = frames <= 1 ? 0d : (double)f / (frames - 1);
                    double cutoff = cutoffFromHz * Math.Pow(cutoffToHz / cutoffFromHz, k);
                    switch (kind)
                    {
                        case NoiseFilterKind.HighPass:
                            filter.SetHighPass(cutoff, q, sampleRate);
                            break;
                        case NoiseFilterKind.BandPass:
                            filter.SetBandPass(cutoff, q, sampleRate);
                            break;
                        default:
                            filter.SetLowPass(cutoff, q, sampleRate);
                            break;
                    }

                    blockLeft = FilterBlockSamples;
                }

                blockLeft--;

                double amplitude = 1d;
                if (attackSeconds > 0d && t < attackSeconds)
                    amplitude = t / attackSeconds;
                if (decayTauSeconds > 0d)
                    amplitude *= Waveforms.ExpDecay(t, decayTauSeconds);
                if (amplitude <= 0d)
                    continue;

                float wet = filter.Process(rng.NextBipolar());
                float value = (float)(wet * amplitude * gain);

                if (dst >= 0)
                {
                    for (int c = 0; c < target.Channels; c++)
                        target.Samples[dst * target.Channels + c] += value;
                }
            }
        }

        /// <summary>
        /// 叠加一段频率滑变的正弦：<paramref name="exponentialGlide"/> = true 时指数滑变
        /// （听感上是「音高均匀下降」），false 时线性滑变。
        /// </summary>
        public static void AddGlideTone(
            AudioBuffer target,
            double startSeconds,
            double durationSeconds,
            double fromHz,
            double toHz,
            double glideTauSeconds,
            double decayTauSeconds,
            float gain,
            bool exponentialGlide = true,
            double phase0 = 0d)
        {
            if (target == null || durationSeconds <= 0d || gain == 0f)
                return;

            int sampleRate = target.SampleRate;
            int startFrame = (int)Math.Round(startSeconds * sampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, sampleRate);

            double phase = phase0;
            for (int f = 0; f < frames; f++)
            {
                int dst = startFrame + f;
                if (dst >= target.FrameCount)
                    break;

                double t = (double)f / sampleRate;
                double freq = exponentialGlide
                    ? Waveforms.Glide(fromHz, toHz, t, glideTauSeconds)
                    : Waveforms.Lerp(fromHz, toHz, t, durationSeconds);

                phase += freq / sampleRate;
                if (phase >= 1d)
                    phase -= Math.Floor(phase);

                double amplitude = decayTauSeconds > 0d ? Waveforms.ExpDecay(t, decayTauSeconds) : 1d;
                float value = (float)(Waveforms.Sine(phase) * amplitude * gain);

                if (dst >= 0)
                {
                    for (int c = 0; c < target.Channels; c++)
                        target.Samples[dst * target.Channels + c] += value;
                }
            }
        }

        /// <summary>
        /// 叠加一组衰减正弦分音（谐振腔体）。<paramref name="partialsHz"/> 与
        /// <paramref name="gains"/> 等长；每个分音独立衰减常数（秒）。
        /// </summary>
        public static void AddDecayingPartials(
            AudioBuffer target,
            double startSeconds,
            double durationSeconds,
            double[] partialsHz,
            float[] gains,
            double[] decayTaus,
            double phase0 = 0.37d)
        {
            if (target == null || partialsHz == null || durationSeconds <= 0d)
                return;

            int sampleRate = target.SampleRate;
            int startFrame = (int)Math.Round(startSeconds * sampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, sampleRate);

            for (int p = 0; p < partialsHz.Length; p++)
            {
                double gain = gains != null && p < gains.Length ? gains[p] : 1d / (p + 1d);
                double tau = decayTaus != null && p < decayTaus.Length
                    ? decayTaus[p]
                    : durationSeconds * 0.4d;

                double phase = phase0 * (p + 1);
                for (int f = 0; f < frames; f++)
                {
                    int dst = startFrame + f;
                    if (dst >= target.FrameCount)
                        break;

                    double t = (double)f / sampleRate;
                    phase += partialsHz[p] / sampleRate;
                    if (phase >= 1d)
                        phase -= Math.Floor(phase);

                    double amplitude = Waveforms.ExpDecay(t, tau);
                    float value = (float)(Waveforms.Sine(phase) * amplitude * gain);

                    if (dst >= 0)
                    {
                        for (int c = 0; c < target.Channels; c++)
                            target.Samples[dst * target.Channels + c] += value;
                    }
                }
            }
        }

        /// <summary>
        /// 施加与频率相关的整体滤波（对已有缓冲再塑形），按块更新系数。
        /// 用于给整段噪声/音色做统一的频段倾斜。
        /// </summary>
        public static void ShapeSpectrum(
            AudioBuffer buffer,
            NoiseFilterKind kind,
            double cutoffFromHz,
            double cutoffToHz,
            double q = 0.8d)
        {
            if (buffer == null)
                return;

            if (cutoffFromHz < 1d)
                cutoffFromHz = 1d;
            if (cutoffToHz < 1d)
                cutoffToHz = 1d;

            int sampleRate = buffer.SampleRate;
            int frames = buffer.FrameCount;
            Biquad filter = default;
            int blockLeft = 0;

            for (int f = 0; f < frames; f++)
            {
                if (blockLeft <= 0)
                {
                    double k = frames <= 1 ? 0d : (double)f / (frames - 1);
                    double cutoff = cutoffFromHz * Math.Pow(cutoffToHz / cutoffFromHz, k);
                    switch (kind)
                    {
                        case NoiseFilterKind.HighPass:
                            filter.SetHighPass(cutoff, q, sampleRate);
                            break;
                        case NoiseFilterKind.BandPass:
                            filter.SetBandPass(cutoff, q, sampleRate);
                            break;
                        default:
                            filter.SetLowPass(cutoff, q, sampleRate);
                            break;
                    }

                    blockLeft = FilterBlockSamples;
                }

                blockLeft--;
                for (int c = 0; c < buffer.Channels; c++)
                {
                    int idx = f * buffer.Channels + c;
                    buffer.Samples[idx] = filter.Process(buffer.Samples[idx]);
                }
            }
        }
    }
}
