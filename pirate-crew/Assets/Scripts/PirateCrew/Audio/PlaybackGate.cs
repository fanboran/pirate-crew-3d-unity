using System;
using System.Collections.Generic;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 播放闸门（纯 C#，可无头测试）：同帧去抖 + 分类播放上限。
    ///
    /// 【为什么需要去抖】一次爆炸可能在同一帧触发多个来源（弹体引爆 + 连锁 +
    /// 表现层回调），不去抖会叠成「啪」的一声爆音；UI 连点同理。
    /// 规则：**同一 key（音效 id）在 50 ms 内只放行一次**。
    ///
    /// 【为什么需要播放上限】竞技场一次爆炸可能同时衰减到多个单位、
    /// 多个弹体同时命中，瞬间开 20 路 AudioSource 会挤占音频线程。
    /// 规则：每个分类最多 <see cref="DefaultMaxVoicesPerCategory"/> 路并发，
    /// 超出则丢弃新请求（**不做抢占**——抢占会切掉正在响的爆炸，听感更差）。
    ///
    /// 【时钟口径】外部传入秒数，不依赖 <c>Time.time</c>：一来可在无头验证台
    /// 逐时刻断言，二来 AudioService 可以用 <c>Time.unscaledTimeAsDouble</c>
    /// （游戏暂停时音频仍应可播）。
    ///
    /// 数值（50 ms / 12 路）为 **AI 提案/待定**，需实机手感与人耳验收。
    /// </summary>
    public sealed class PlaybackGate
    {
        /// <summary>同 key 去抖窗口（秒）。</summary>
        public const double DefaultDebounceSeconds = 0.05d;

        /// <summary>每分类并发播放上限。</summary>
        public const int DefaultMaxVoicesPerCategory = 12;

        /// <summary>去抖窗口内被丢弃的请求次数（调试/报告用）。</summary>
        public int DebouncedCount { get; private set; }

        /// <summary>因并发上限被丢弃的请求次数。</summary>
        public int VoiceCappedCount { get; private set; }

        readonly double _debounceSeconds;
        readonly int[] _maxVoices;
        readonly Dictionary<int, double> _lastStartSeconds = new Dictionary<int, double>();
        readonly List<Voice> _active = new List<Voice>();

        struct Voice
        {
            public int CategoryIndex;
            public double EndSeconds;
        }

        public PlaybackGate(
            double debounceSeconds = DefaultDebounceSeconds,
            int maxVoicesPerCategory = DefaultMaxVoicesPerCategory)
        {
            _debounceSeconds = debounceSeconds < 0d ? 0d : debounceSeconds;
            if (maxVoicesPerCategory < 1)
                maxVoicesPerCategory = 1;

            _maxVoices = new int[AudioCategories.Count];
            for (int i = 0; i < _maxVoices.Length; i++)
                _maxVoices[i] = maxVoicesPerCategory;
        }

        /// <summary>当前时刻该分类的活跃路数（会顺带清理已结束的占位）。</summary>
        public int ActiveCount(AudioCategory category, double nowSeconds)
        {
            Trim(nowSeconds);
            int index = (int)category;
            int count = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].CategoryIndex == index)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 只做去抖判定：同 key 在窗口内重复调用返回 false。
        /// key 为音效 id 的整型值（调用方传 <c>(int)SfxId</c>）：整型直存字典，
        /// 播放热路径零装箱、零字符串分配（旧版 string key 每次播放都要 <c>id.ToString()</c>，
        /// 受击/地雷蜂鸣这类高频音效吃 GC）。key 唯一性由调用方保证（同一音效恒用同一 id）。
        /// 放行时会记录本次时间，被拒绝时不记录（保证窗口内第一次请求总能过）。
        /// </summary>
        public bool ShouldPlay(int key, double nowSeconds)
        {
            if (_lastStartSeconds.TryGetValue(key, out double last))
            {
                if (nowSeconds - last < _debounceSeconds)
                    return false;
            }

            _lastStartSeconds[key] = nowSeconds;
            return true;
        }

        /// <summary>
        /// 综合判定并占位：去抖 + 分类并发上限都通过才返回 true，
        /// 并在 <paramref name="durationSeconds"/> 内占用一个并发名额。
        ///
        /// 【顺序】先查并发上限再去抖：并发满时不应消耗去抖窗口，
        /// 否则「名额刚满→名额释放」之间 50 ms 内的请求会被误杀。
        /// </summary>
        public bool TryAcquire(int key, AudioCategory category, double nowSeconds, double durationSeconds)
        {
            Trim(nowSeconds);

            int index = (int)category;
            if (index < 0 || index >= _maxVoices.Length)
                return false;

            if (CountActive(index) >= _maxVoices[index])
            {
                VoiceCappedCount++;
                return false;
            }

            if (!ShouldPlay(key, nowSeconds))
            {
                DebouncedCount++;
                return false;
            }

            _active.Add(new Voice
            {
                CategoryIndex = index,
                EndSeconds = nowSeconds + (durationSeconds > 0d ? durationSeconds : 0d),
            });
            return true;
        }

        /// <summary>清空所有状态（进入新一局/新场景时调用，避免旧局占位残留）。</summary>
        public void Reset()
        {
            _lastStartSeconds.Clear();
            _active.Clear();
            DebouncedCount = 0;
            VoiceCappedCount = 0;
        }

        /// <summary>清理已结束的占位。</summary>
        public void Trim(double nowSeconds)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].EndSeconds <= nowSeconds)
                    _active.RemoveAt(i);
            }
        }

        int CountActive(int categoryIndex)
        {
            int count = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].CategoryIndex == categoryIndex)
                    count++;
            }

            return count;
        }
    }
}
