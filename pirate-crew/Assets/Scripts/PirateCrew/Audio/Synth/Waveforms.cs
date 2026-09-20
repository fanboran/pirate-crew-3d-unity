using System;

namespace PirateCrew.Audio.Synth
{
    /// <summary>
    /// 基础波形表（纯函数；相位口径为 [ 0,1 ) 的归一化相位，返回 [-1,1]）。
    ///
    /// 【为什么用相位而不是时间】振荡器的频率通常随时间滑变（爆炸的 95→32 Hz、海鸥的
    /// 鸣叫滑音），每帧直接算 <c>sin(2π f t)</c> 会因相位不连续产生咔哒声。调用方维护
    /// 累积相位 <c>phase += freq / sampleRate</c>，本类只负责「相位 → 幅值」的纯映射，
    /// 频率滑变天然连续。
    ///
    /// 【为什么 SINE/三角优先于锯齿/方波】本项目调性（docs/美术风格指南.md:12-16
    /// 加勒比正午海岛、温暖写实风格）偏柔和；锯齿/方波含大量奇次谐波，直接使用电子味重，
    /// 因此仅在 UI 错误音、危险提示等「需要刺耳」的场合使用，其余场合用
    /// 叠加少量谐波的三角形波或正弦，并配合 <see cref="Detune"/> 的失谐叠加。
    /// 这些取舍属于 **AI 提案/待定**，最终听感需人耳验收（见交付报告的验收清单）。
    /// </summary>
    public static class Waveforms
    {
        const double TwoPi = Math.PI * 2d;

        /// <summary>正弦：sin(2π·phase)。</summary>
        public static double Sine(double phase)
        {
            return Math.Sin(TwoPi * phase);
        }

        /// <summary>
        /// 三角波：相位 0 → 0，0.25 → 1，0.5 → 0，0.75 → -1（与正弦同相位对齐，
        /// 便于把三角波与正弦直接叠加时不会产生相位错位）。
        /// </summary>
        public static double Triangle(double phase)
        {
            double p = phase - Math.Floor(phase);
            return 1d - 4d * Math.Abs(p + 0.25d - Math.Floor(p + 0.25d) - 0.5d);
        }

        /// <summary>锯齿波（-1 → 1 上行）：2(p - floor(p+0.5))。</summary>
        public static double Saw(double phase)
        {
            return 2d * (phase - Math.Floor(phase + 0.5d));
        }

        /// <summary>方波（带占空比；占空比 0.5 时最经典，0.2 时更接近鼻音/木质）。</summary>
        public static double Square(double phase, double duty = 0.5d)
        {
            if (duty <= 0d || duty >= 1d)
                duty = 0.5d;
            double p = phase - Math.Floor(phase);
            return p < duty ? 1d : -1d;
        }

        /// <summary>
        /// 加谐波的正弦叠加（1..harmonics 次谐波，幅度 1/h^rolloff）：
        /// 音色介于正弦与锯齿之间，<paramref name="rolloff"/> 越大越柔和。
        /// 用于「不电子味」的号角/弦垫音色，见 <see cref="Detune.AddToneStack"/>。
        /// </summary>
        public static double Harmonics(double phase, int harmonics, double rolloff, double oddOnly = 0d)
        {
            if (harmonics < 1)
                harmonics = 1;

            double sum = 0d;
            double norm = 0d;
            for (int h = 1; h <= harmonics; h++)
            {
                // oddOnly > 0 时压制偶次谐波（单簧管/方波系音色）
                double weight = 1d / Math.Pow(h, rolloff);
                if (oddOnly > 0d && (h & 1) == 0)
                    weight *= (1d - oddOnly);
                sum += Sine(phase * h) * weight;
                norm += weight;
            }

            return norm <= 0d ? 0d : sum / norm;
        }

        /// <summary>把频率换算为「每采样相位增量」。</summary>
        public static double PhaseIncrement(double frequencyHz, int sampleRate)
        {
            if (sampleRate <= 0)
                return 0d;
            return frequencyHz / sampleRate;
        }

        /// <summary>指数频率滑变：从 <paramref name="fromHz"/> 指数过渡到 <paramref name="toHz"/>。</summary>
        public static double Glide(double fromHz, double toHz, double t, double tauSeconds)
        {
            if (tauSeconds <= 0d)
                return toHz;
            double k = Math.Exp(-t / tauSeconds);
            return toHz + (fromHz - toHz) * k;
        }

        /// <summary>线性频率滑变（带钳制）。</summary>
        public static double Lerp(double fromHz, double toHz, double t, double duration)
        {
            if (duration <= 0d)
                return toHz;
            double k = t / duration;
            if (k < 0d) k = 0d;
            if (k > 1d) k = 1d;
            return fromHz + (toHz - fromHz) * k;
        }

        /// <summary>把相位包裹回 [0,1)，避免长时间累积导致 double 精度损失。</summary>
        public static double Wrap(double phase)
        {
            return phase - Math.Floor(phase);
        }

        /// <summary>指数衰减包络值 exp(-t/tau)。</summary>
        public static double ExpDecay(double t, double tauSeconds)
        {
            if (t <= 0d)
                return 1d;
            if (tauSeconds <= 0d)
                return 0d;
            return Math.Exp(-t / tauSeconds);
        }

        /// <summary>把 x 从 [inMin,inMax] 线性映射到 [outMin,outMax]（带钳制）。</summary>
        public static double MapClamped(double x, double inMin, double inMax, double outMin, double outMax)
        {
            if (inMax <= inMin)
                return outMin;
            double k = (x - inMin) / (inMax - inMin);
            if (k < 0d) k = 0d;
            if (k > 1d) k = 1d;
            return outMin + (outMax - outMin) * k;
        }
    }
}
