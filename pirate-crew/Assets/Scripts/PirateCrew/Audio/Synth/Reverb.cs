using System;

namespace PirateCrew.PirateCrew.Audio.Synth
{
    /// <summary>
    /// 固定长度循环延迟线（单声道；回声/混响/梳状滤波的公共积木）。
    /// 纯 C#，环形缓冲实现，读写 O(1)。
    ///
    /// 提供两套读写口径：
    ///   · <see cref="Process"/>：一次性「读出旧值 + 写入新值 + 前进」，适合简单回声。
    ///   · <see cref="Read"/>/<see cref="Write"/>/<see cref="Advance"/>：分步版，
    ///     适合反馈路径需要在同一格上先读后写、并插入滤波器的结构（梳状/全通）。
    /// </summary>
    public sealed class DelayLine
    {
        readonly float[] _buffer;
        int _writeIndex;

        public DelayLine(int delaySamples)
        {
            if (delaySamples < 1)
                delaySamples = 1;
            _buffer = new float[delaySamples];
        }

        /// <summary>延迟长度（采样数）。</summary>
        public int Length => _buffer.Length;

        /// <summary>读取当前格（尚未写入的旧样本）。</summary>
        public float Read()
        {
            return _buffer[_writeIndex];
        }

        /// <summary>写入当前格。</summary>
        public void Write(float value)
        {
            _buffer[_writeIndex] = value;
        }

        /// <summary>前进到下一格。</summary>
        public void Advance()
        {
            _writeIndex++;
            if (_writeIndex >= _buffer.Length)
                _writeIndex = 0;
        }

        /// <summary>简单延迟：返回延迟长度前的样本，并写入当前输入。</summary>
        public float Process(float input)
        {
            float delayed = _buffer[_writeIndex];
            _buffer[_writeIndex] = input;
            Advance();
            return delayed;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _writeIndex = 0;
        }
    }

    /// <summary>
    /// 简化 Freeverb 混响（4 个并联阻尼梳状滤波器 + 2 个串联全通），单声道。
    ///
    /// 【为什么需要它】干声直接结束会显得「贴脸、塑料」；爆炸尾音、UI 展开、结果乐句
    /// 都需要一段空间残响把声场拉开。程序化合成里最廉价可靠的离线做法就是
    /// Freeverb 简化结构：4 个互质延迟长度的梳状滤波器制造回声密度，2 个全通打散相位。
    ///
    /// 【阻尼】每个梳状滤波器的反馈路径上串一阶低通（<c>filterstore</c>），
    /// 高频衰减快于低频——真实空间听感，也是避免金属味啸叫的关键。
    ///
    /// 【出处】Freeverb（Jezar 公开实现，公有领域口径的经典结构）；延迟长度取常用的
    /// 梳状 1116/1188/1277/1356 采样（44.1 kHz）、全通 556/441 采样，按采样率换算。
    /// roomSize/damping/wet 的具体取值为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public sealed class SimpleReverb
    {
        // Freeverb 在 44.1 kHz 下的经典延迟长度（采样数）
        static readonly int[] CombSamples44k = { 1116, 1188, 1277, 1356 };
        static readonly int[] AllPassSamples44k = { 556, 441 };
        const float AllPassFeedback = 0.5f;

        readonly DelayLine[] _combs;
        readonly float[] _combFeedback;
        readonly float[] _combFilterStore;
        readonly float _damping1;
        readonly float _damping2;
        readonly DelayLine[] _allPass;

        /// <summary>
        /// 创建混响。<paramref name="roomSize"/> 越大回声反馈越强（0–0.95，越大尾巴越长）；
        /// <paramref name="damping"/> 越大高频衰减越快（0–1，越大越「闷」）。
        /// </summary>
        public SimpleReverb(int sampleRate, float roomSize = 0.72f, float damping = 0.42f)
        {
            if (sampleRate <= 0)
                sampleRate = AudioBuffer.DefaultSampleRate;

            roomSize = Clamp01(roomSize, 0.95f);
            damping = Clamp01(damping, 1f);

            _damping1 = damping;
            _damping2 = 1f - damping;

            _combs = new DelayLine[CombSamples44k.Length];
            _combFeedback = new float[CombSamples44k.Length];
            _combFilterStore = new float[CombSamples44k.Length];

            for (int i = 0; i < CombSamples44k.Length; i++)
            {
                _combs[i] = new DelayLine(Scale(CombSamples44k[i], sampleRate));
                // 各梳状滤波器反馈略有差异，避免整齐的回声串（听感上更像扩散）
                _combFeedback[i] = roomSize * (1f - i * 0.035f);
            }

            _allPass = new DelayLine[AllPassSamples44k.Length];
            for (int i = 0; i < AllPassSamples44k.Length; i++)
                _allPass[i] = new DelayLine(Scale(AllPassSamples44k[i], sampleRate));
        }

        /// <summary>处理一个样本，返回湿信号（不含干声）。</summary>
        public float Process(float input)
        {
            float combSum = 0f;

            for (int i = 0; i < _combs.Length; i++)
            {
                float output = _combs[i].Read();
                // 反馈路径一阶低通（Freeverb 的 filterstore）
                _combFilterStore[i] = output * _damping2 + _combFilterStore[i] * _damping1;
                _combs[i].Write(input + _combFilterStore[i] * _combFeedback[i]);
                _combs[i].Advance();
                combSum += output;
            }

            float wet = combSum * (1f / _combs.Length);

            for (int i = 0; i < _allPass.Length; i++)
            {
                float buffered = _allPass[i].Read();
                // 用本级输出（wet）作为下一级的输入，形成串联全通
                float output = buffered - wet;
                _allPass[i].Write(wet + buffered * AllPassFeedback);
                _allPass[i].Advance();
                wet = output;
            }

            return wet;
        }

        /// <summary>
        /// 计算整段缓冲的湿信号并与干声按 <paramref name="wet"/> 混合（0 = 全干，1 = 全湿）。
        /// </summary>
        public void ProcessInPlace(AudioBuffer buffer, double wet)
        {
            if (buffer == null || wet <= 0d)
                return;
            if (wet > 1d)
                wet = 1d;

            float w = (float)wet;
            int frames = buffer.FrameCount;
            for (int f = 0; f < frames; f++)
            {
                int idx = f * buffer.Channels;
                float dry = buffer.Samples[idx];
                float rev = Process(dry);
                buffer.Samples[idx] = dry * (1f - w) + rev * w;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _combs.Length; i++)
            {
                _combs[i].Clear();
                _combFilterStore[i] = 0f;
            }
            for (int i = 0; i < _allPass.Length; i++)
                _allPass[i].Clear();
        }

        static int Scale(int samples44k, int sampleRate)
        {
            int scaled = (int)Math.Round(samples44k * (double)sampleRate / AudioBuffer.DefaultSampleRate);
            return scaled < 1 ? 1 : scaled;
        }

        static float Clamp01(float value, float max)
        {
            if (value < 0f) return 0f;
            if (value > max) return max;
            return value;
        }
    }
}
