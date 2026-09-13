using System;

namespace PirateCrew.PirateCrew.Audio.Synth
{
    /// <summary>
    /// 单极点一阶滤波器（纯值类型，逐个样本调用，可测）。
    ///
    /// 一阶低通：y[n] = y[n-1] + a·(x[n] - y[n-1])，a = 1 - e^(-2π·fc/fs)。
    /// 频响 -6 dB/oct，过渡带平缓、无谐振，适合做噪声的「粗糙度」塑形
    /// （爆炸尾音的闷响、海风的宽带噪声），不会像二阶滤波那样在截止点附近产生啸叫。
    ///
    /// 【与二阶的分工】需要明确的频段隔离/共振时用 <see cref="Biquad"/>；
    /// 需要「把白噪声变暗」这种整体倾斜时用本类，代价低且状态简单。
    /// </summary>
    public struct OnePoleLowPass
    {
        float _y;
        float _a;

        /// <summary>按截止频率创建（<paramref name="cutoffHz"/> ≥ Nyquist 时近似直通）。</summary>
        public static OnePoleLowPass Create(double cutoffHz, int sampleRate)
        {
            var filter = new OnePoleLowPass();
            filter.ResetTo(cutoffHz, sampleRate);
            return filter;
        }

        /// <summary>重新设定截止频率（不重置状态，用于随时间扫频）。</summary>
        public void ResetTo(double cutoffHz, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                _a = 1f;
                return;
            }

            double nyquist = sampleRate * 0.5d;
            if (cutoffHz > nyquist)
                cutoffHz = nyquist;
            if (cutoffHz < 1d)
                cutoffHz = 1d;

            _a = (float)(1d - Math.Exp(-2d * Math.PI * cutoffHz / sampleRate));
        }

        /// <summary>处理一个样本。</summary>
        public float Process(float x)
        {
            _y += _a * (x - _y);
            return _y;
        }

        /// <summary>清空内部状态（重渲染时防止上一段残留）。</summary>
        public void Reset()
        {
            _y = 0f;
        }
    }

    /// <summary>单极点一阶高通：x - lowpass(x)。用高通滤掉落水/风声里的低频隆隆。</summary>
    public struct OnePoleHighPass
    {
        OnePoleLowPass _lp;

        public static OnePoleHighPass Create(double cutoffHz, int sampleRate)
        {
            var filter = new OnePoleHighPass();
            filter._lp = OnePoleLowPass.Create(cutoffHz, sampleRate);
            return filter;
        }

        public void ResetTo(double cutoffHz, int sampleRate)
        {
            _lp.ResetTo(cutoffHz, sampleRate);
        }

        public float Process(float x)
        {
            return x - _lp.Process(x);
        }

        public void Reset()
        {
            _lp.Reset();
        }
    }

    /// <summary>
    /// 双二阶（RBJ Audio EQ Cookbook）滤波器：低通 / 高通 / 带通，带 Q 值谐振。
    ///
    /// 相比一阶：-12 dB/oct 更陡、Q > 0.707 时在截止点附近形成共振峰——
    /// 这正是「木桶碎裂的腔体感」「海鸥鸣叫的共振峰（formant）」「whoosh 的呼啸」
    /// 所需要的音色来源。系数更新用三角函数，离线渲染可接受；实时逐样本扫频时
    /// 建议按块（如每 64 样本）更新系数，见 CombatSynth 的用法。
    ///
    /// 【出处】公式取自 RBJ Audio EQ Cookbook（Web 上长期稳定的公开公式）；
    /// 本项目只使用其标准低通/高通/带通三式。
    /// </summary>
    public struct Biquad
    {
        float _b0, _b1, _b2, _a1, _a2;
        float _x1, _x2, _y1, _y2;

        /// <summary>低通（<paramref name="q"/> = 0.707 为 Butterworth，无谐振）。</summary>
        public static Biquad LowPass(double cutoffHz, double q, int sampleRate)
        {
            var b = new Biquad();
            b.SetLowPass(cutoffHz, q, sampleRate);
            return b;
        }

        /// <summary>高通。</summary>
        public static Biquad HighPass(double cutoffHz, double q, int sampleRate)
        {
            var b = new Biquad();
            b.SetHighPass(cutoffHz, q, sampleRate);
            return b;
        }

        /// <summary>常数峰值增益带通（中心频率处增益为 1）。</summary>
        public static Biquad BandPass(double centerHz, double q, int sampleRate)
        {
            var b = new Biquad();
            b.SetBandPass(centerHz, q, sampleRate);
            return b;
        }

        public void SetLowPass(double cutoffHz, double q, int sampleRate)
        {
            double w0 = Omega(cutoffHz, sampleRate);
            double alpha = Alpha(w0, q);
            double cos = Math.Cos(w0);
            double a0 = 1d + alpha;

            _b0 = (float)((1d - cos) * 0.5d / a0);
            _b1 = (float)((1d - cos) / a0);
            _b2 = _b0;
            _a1 = (float)(-2d * cos / a0);
            _a2 = (float)((1d - alpha) / a0);
        }

        public void SetHighPass(double cutoffHz, double q, int sampleRate)
        {
            double w0 = Omega(cutoffHz, sampleRate);
            double alpha = Alpha(w0, q);
            double cos = Math.Cos(w0);
            double a0 = 1d + alpha;

            _b0 = (float)((1d + cos) * 0.5d / a0);
            _b1 = (float)(-(1d + cos) / a0);
            _b2 = _b0;
            _a1 = (float)(-2d * cos / a0);
            _a2 = (float)((1d - alpha) / a0);
        }

        public void SetBandPass(double centerHz, double q, int sampleRate)
        {
            double w0 = Omega(centerHz, sampleRate);
            double alpha = Alpha(w0, q);
            double cos = Math.Cos(w0);
            double a0 = 1d + alpha;

            _b0 = (float)(alpha / a0);
            _b1 = 0f;
            _b2 = (float)(-alpha / a0);
            _a1 = (float)(-2d * cos / a0);
            _a2 = (float)((1d - alpha) / a0);
        }

        /// <summary>处理一个样本。</summary>
        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0f;
        }

        static double Omega(double freqHz, int sampleRate)
        {
            if (sampleRate <= 0)
                return 0d;
            double nyquist = sampleRate * 0.5d;
            if (freqHz > nyquist * 0.999d)
                freqHz = nyquist * 0.999d;
            if (freqHz < 1d)
                freqHz = 1d;
            return 2d * Math.PI * freqHz / sampleRate;
        }

        static double Alpha(double w0, double q)
        {
            if (q <= 0d)
                q = 0.707d;
            return Math.Sin(w0) / (2d * q);
        }
    }
}
