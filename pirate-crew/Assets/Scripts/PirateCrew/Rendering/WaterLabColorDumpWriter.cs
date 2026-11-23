using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// rt0 落盘写手（隐藏 GameObject，LateUpdate 轮询读回；生命周期=抓取起讫）。
    /// <c>AsyncGPUReadback</c> 完成后一帧内写 4 件：f16 raw（权威比对件）、EXR（线性
    /// half）、PNG（sRGB 预览）、meta.json（相位对账元数据）。10s 实时超时兜底：
    /// 超时置 <c>WaterLabColorDump.Failed</c> 响哨，不阻塞 capture 流程退出。
    /// </summary>
    internal sealed class WaterLabColorDumpWriter : MonoBehaviour
    {
        const float TimeoutSeconds = 10f;

        static WaterLabColorDumpWriter _instance;

        public static void Ensure()
        {
            if (_instance != null)
                return;
            var go = new GameObject("WaterLabColorDumpWriter") { hideFlags = HideFlags.HideAndDontSave };
            _instance = go.AddComponent<WaterLabColorDumpWriter>();
        }

        void LateUpdate()
        {
            if (WaterLabColorDump.Completed || WaterLabColorDump.Failed)
            {
                Destroy(gameObject);
                return;
            }
            if (!WaterLabColorDump.HasPending)
                return;

            if (Time.realtimeSinceStartupAsDouble - WaterLabColorDump.StartedRealtime > TimeoutSeconds)
            {
                WaterLabColorDump.Fail($"AsyncGPUReadback {TimeoutSeconds}s 超时（读回未完成）");
                Destroy(gameObject);
                return;
            }
            if (!WaterLabColorDump.ReadbackArrived)
                return;   // 回调未到（Pending 尚为默认结构，不许读）
            if (WaterLabColorDump.Pending.hasError)
            {
                WaterLabColorDump.Fail("AsyncGPUReadbackRequest.HasError");
                Destroy(gameObject);
                return;
            }

            try
            {
                WriteOutputs(WaterLabColorDump.Pending);
                WaterLabColorDump.Completed = true;
                WaterLabColorDump.ReleaseRT();
                Debug.Log($"[WaterLabColorDump] 完成：ours-rt0-prepost.raw/exr/png/meta.json → " +
                    $"{WaterLabColorDump.ResolveOutputDir()}（simTime={WaterLabColorDump.CaptureSimTime:F4}s）");
            }
            catch (Exception e)
            {
                WaterLabColorDump.Fail(e.ToString());
            }
            Destroy(gameObject);
        }

        static void WriteOutputs(AsyncGPUReadbackRequest request)
        {
            int w = WaterLabColorDump.Width;
            int h = WaterLabColorDump.Height;
            var halfs = request.GetData<ushort>(); // ARGBHalf 原生读回 = R16G16B16A16 f16
            ushort[] managed = halfs.ToArray();    // NativeArray 随帧失效，先托管拷贝
            if (managed.Length != w * h * 4)
                throw new InvalidDataException(
                    $"读回长度 {managed.Length} ≠ 预期 {w * h * 4}（{w}x{h}x4）");

            string dir = WaterLabColorDump.ResolveOutputDir();
            Directory.CreateDirectory(dir);

            // ① raw = f16 位流原样（权威比对件；行序 = 读回顺序，比较器对 PNG 自检）。
            var rawBytes = new byte[managed.Length * 2];
            Buffer.BlockCopy(managed, 0, rawBytes, 0, rawBytes.Length);
            File.WriteAllBytes(Path.Combine(dir, "ours-rt0-prepost.raw"), rawBytes);

            // ② EXR：RGBAHalf 纹理直接编码（线性 half，供通用工具肉眼/取值）。
            try
            {
                var exrTex = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
                exrTex.SetPixelData(managed, 0);
                exrTex.Apply(false);
                File.WriteAllBytes(Path.Combine(dir, "ours-rt0-prepost.exr"), exrTex.EncodeToEXR());
                DestroyImmediate(exrTex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WaterLabColorDump] EXR 编码失败（raw 权威件不受影响）：" + e.Message);
            }

            // ③ PNG：线性→sRGB 8bit 预览件（HDR>1 截断；数值比对一律用 raw）。
            try
            {
                var pngTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                pngTex.SetPixelData(BuildSrgbPreviewBytes(managed), 0);
                pngTex.Apply(false);
                File.WriteAllBytes(Path.Combine(dir, "ours-rt0-prepost.png"), pngTex.EncodeToPNG());
                DestroyImmediate(pngTex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WaterLabColorDump] PNG 编码失败（raw 权威件不受影响）：" + e.Message);
            }

            // ④ meta.json：相位对账元数据（目标 = 原作抓帧 playTime 49.208416）。
            var meta = new MetaJson
            {
                width = w,
                height = h,
                format = "R16G16B16A16 f16 位流（线性域；行序由 l3_rt0_diff.py 对 png 自检）",
                captureSimTime = WaterLabColorDump.CaptureSimTime,
                targetPlayTime = 49.208416f,
                playtime0Env = Environment.GetEnvironmentVariable("PC3D_WATERLAB_PLAYTIME0") ?? "",
                fixedSimTimeEnv = Environment.GetEnvironmentVariable("PC3D_WATERLAB_FIXED_SIM_TIME") ?? "",
                timeScaleEnv = Environment.GetEnvironmentVariable("PC3D_WATERLAB_TIME_SCALE") ?? "",
                pngNote = "sRGB 编码预览（HDR 截断），数值比对勿用",
            };
            File.WriteAllText(Path.Combine(dir, "ours-rt0-prepost.meta.json"),
                JsonUtility.ToJson(meta, true));
        }

        [Serializable]
        class MetaJson
        {
            public int width;
            public int height;
            public string format;
            public float captureSimTime;
            public float targetPlayTime;
            public string playtime0Env;
            public string fixedSimTimeEnv;
            public string timeScaleEnv;
            public string pngNote;
        }

        /// <summary>linear f16 → sRGB 8bit 预览字节（RGBA 序）。NaN→0，HDR>1 截白。</summary>
        static byte[] BuildSrgbPreviewBytes(ushort[] halfs)
        {
            var bytes = new byte[halfs.Length];
            for (int i = 0; i < halfs.Length; i++)
                bytes[i] = LinearToSrgbByte(HalfBitsToFloat(halfs[i]));
            return bytes;
        }

        /// <summary>half 位型 → float 值（含子正规/NaN/Inf；无 Unity.Mathematics 依赖）。</summary>
        static float HalfBitsToFloat(ushort halfBits)
        {
            uint h = halfBits;
            uint sign = (h & 0x8000u) << 16;
            uint exp = (h >> 10) & 0x1Fu;
            uint man = h & 0x03FFu;
            uint bits;
            if (exp == 0)
            {
                if (man == 0)
                {
                    bits = sign; // ±0
                }
                else
                {
                    int e = -1; // 子正规 → 归一化
                    while ((man & 0x0400u) == 0)
                    {
                        man <<= 1;
                        e++;
                    }
                    man &= 0x03FFu;
                    bits = sign | (uint)((127 - 15 - e) << 23) | (man << 13);
                }
            }
            else if (exp == 0x1F)
            {
                bits = sign | 0x7F800000u | (man << 13); // Inf/NaN
            }
            else
            {
                bits = sign | ((exp + 112u) << 23) | (man << 13);
            }
            return BitsToFloat(bits);
        }

        static readonly byte[] s_BitsBuffer = new byte[4]; // 复用缓冲防 14.7M 次小对象分配

        static float BitsToFloat(uint bits)
        {
            s_BitsBuffer[0] = (byte)bits;
            s_BitsBuffer[1] = (byte)(bits >> 8);
            s_BitsBuffer[2] = (byte)(bits >> 16);
            s_BitsBuffer[3] = (byte)(bits >> 24);
            return BitConverter.ToSingle(s_BitsBuffer, 0);
        }

        static byte LinearToSrgbByte(float c)
        {
            if (float.IsNaN(c) || c <= 0f)
                return 0;
            if (c >= 1f)
                return 255; // HDR>1 预览截断
            float s = c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
            int v = Mathf.RoundToInt(s * 255f);
            return (byte)Mathf.Clamp(v, 0, 255);
        }
    }
}
