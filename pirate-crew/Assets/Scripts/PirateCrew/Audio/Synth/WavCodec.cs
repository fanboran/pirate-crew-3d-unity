using System;

namespace PirateCrew.PirateCrew.Audio.Synth
{
    /// <summary>
    /// 16 位 PCM WAV 编解码（纯 C#，无 UnityEngine，可在无头验证台逐字节测试）。
    ///
    /// 【为什么把编解码放在运行时程序集而不是编辑器脚本】离线落盘确实只在编辑器里做，
    /// 但 WAV 头是「一旦写错就静默产出坏资产」的典型位置（Unity 导入报错往往看不出是
    /// 头字段不对）。放在纯 C# 里就能在无头验证台上直接断言 RIFF/fmt/data 字段与采样值，
    /// 编辑器脚本只负责调用 + 写盘 + 设导入参数。
    ///
    /// 【格式】RIFF/WAVE，PCM，小端：
    ///   偏移 0   "RIFF" | 4  ChunkSize = 36 + dataBytes
    ///   偏移 8   "WAVE"
    ///   偏移 12  "fmt " | 16 | AudioFormat=1 | Channels | SampleRate | ByteRate | BlockAlign | BitsPerSample=16
    ///   偏移 36  "data" | dataBytes
    ///   偏移 44  采样数据（交错，int16 小端）
    ///
    /// 【音量保护】编码前按 <paramref name="gain"/> 缩放并钳制到 [-1, 1-1/32768]，
    /// 避免 float→int16 溢出回绕产生刺耳的爆音。
    /// </summary>
    public static class WavCodec
    {
        /// <summary>标准 PCM WAV 头长度（字节）。</summary>
        public const int HeaderBytes = 44;

        const int BitsPerSample = 16;

        /// <summary>把缓冲编码为 16 位 PCM WAV 字节流。</summary>
        public static byte[] EncodePcm16(AudioBuffer buffer, float gain = 1f)
        {
            if (buffer == null)
                return new byte[HeaderBytes];

            int channels = buffer.Channels < 1 ? 1 : buffer.Channels;
            int sampleRate = buffer.SampleRate < 1 ? AudioBuffer.DefaultSampleRate : buffer.SampleRate;
            int sampleCount = buffer.Samples.Length;
            int dataBytes = sampleCount * (BitsPerSample / 8);

            var wav = new byte[HeaderBytes + dataBytes];

            // RIFF 头
            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, 36 + dataBytes);
            WriteAscii(wav, 8, "WAVE");

            // fmt 子块
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);                                     // 子块长度
            WriteInt16(wav, 20, 1);                                      // PCM
            WriteInt16(wav, 22, channels);
            WriteInt32(wav, 24, sampleRate);
            WriteInt32(wav, 28, sampleRate * channels * (BitsPerSample / 8)); // ByteRate
            WriteInt16(wav, 32, channels * (BitsPerSample / 8));         // BlockAlign
            WriteInt16(wav, 34, BitsPerSample);

            // data 子块
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, dataBytes);

            int offset = HeaderBytes;
            for (int i = 0; i < sampleCount; i++)
            {
                float scaled = buffer.Samples[i] * gain;
                if (scaled > 1f) scaled = 1f;
                if (scaled < -1f) scaled = -1f;

                // 负半轴与正半轴的非对称映射（-1.0 → -32768，+1.0 → +32767）
                int value = scaled >= 0f
                    ? (int)Math.Round(scaled * 32767f)
                    : (int)Math.Round(scaled * 32768f);

                if (value > 32767) value = 32767;
                if (value < -32768) value = -32768;

                wav[offset++] = (byte)(value & 0xFF);
                wav[offset++] = (byte)((value >> 8) & 0xFF);
            }

            return wav;
        }

        /// <summary>
        /// 解析 WAV 头（测试与导入校验用）。只支持本项目产出的标准 44 字节头。
        /// </summary>
        public static bool TryReadHeader(
            byte[] wav, out int sampleRate, out int channels, out int bitsPerSample, out int dataBytes)
        {
            sampleRate = 0;
            channels = 0;
            bitsPerSample = 0;
            dataBytes = 0;

            if (wav == null || wav.Length < HeaderBytes)
                return false;

            if (!MatchesAscii(wav, 0, "RIFF") || !MatchesAscii(wav, 8, "WAVE"))
                return false;
            if (!MatchesAscii(wav, 12, "fmt ") || !MatchesAscii(wav, 36, "data"))
                return false;
            if (ReadInt16(wav, 20) != 1)
                return false;

            channels = ReadInt16(wav, 22);
            sampleRate = ReadInt32(wav, 24);
            bitsPerSample = ReadInt16(wav, 34);
            dataBytes = ReadInt32(wav, 40);

            return channels > 0 && sampleRate > 0 && dataBytes >= 0 && wav.Length >= HeaderBytes + dataBytes;
        }

        /// <summary>读取 WAV 中第 <paramref name="sampleIndex"/> 个采样（归一化到 [-1,1]）。</summary>
        public static float ReadPcm16Sample(byte[] wav, int sampleIndex)
        {
            int offset = HeaderBytes + sampleIndex * 2;
            if (wav == null || offset + 1 >= wav.Length)
                return 0f;

            int value = (short)(wav[offset] | (wav[offset + 1] << 8));
            return value < 0 ? value / 32768f : value / 32767f;
        }

        static void WriteAscii(byte[] target, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++)
                target[offset + i] = (byte)text[i];
        }

        static bool MatchesAscii(byte[] target, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (target[offset + i] != (byte)text[i])
                    return false;
            }

            return true;
        }

        static void WriteInt16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value & 0xFF);
            target[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value & 0xFF);
            target[offset + 1] = (byte)((value >> 8) & 0xFF);
            target[offset + 2] = (byte)((value >> 16) & 0xFF);
            target[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static int ReadInt16(byte[] source, int offset)
        {
            return source[offset] | (source[offset + 1] << 8);
        }

        static int ReadInt32(byte[] source, int offset)
        {
            return source[offset]
                   | (source[offset + 1] << 8)
                   | (source[offset + 2] << 16)
                   | (source[offset + 3] << 24);
        }
    }
}
