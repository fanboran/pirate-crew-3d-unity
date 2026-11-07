using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// L3 整帧逐像素差分（相位对齐、rt0 级）的 rt0 抓取闸门/状态（运行时侧）。
    ///
    /// 【口径】对齐原作 R 抓帧的 rt0（2560×1440 R16G16B16A16 线性 HDR，见
    /// external/sandcastle-ref/rt/reports/L2-run.md §1.2/§5）：在 URP 透明队列之后、
    /// 后处理（DoF/Vignette/SMAA）之前，把主相机的 cameraColorTarget（本仓 HDR=
    /// ARGBHalf，线性域）原样 blit 进 ARGBHalf dump RT，AsyncGPUReadback 落盘
    /// <c>ours-rt0-prepost.raw</c>（f16 位流，行序由比较器 l3_rt0_diff.py 对 PNG
    /// 自检）+ <c>.exr</c>（线性 half）+ <c>.png</c>（线性→sRGB 8bit 预览件，数值
    /// 比对一律以 raw 为准）+ <c>.meta.json</c>（抓取元数据），输出目录与截图器
    /// 同式（PC3D_CAPTURE_DIR）。
    ///
    /// 【闸门三重（严禁泄进主战役管线）】①env <c>PC3D_WATERLAB_RT0_DUMP=1</c>
    /// （进程常量，主战役/普通出图不设 = Feature 惰性零开销）；②<c>_WaterLabSimEnabled</c>
    /// 全局（WaterLabSimulation 发布，非实验场景恒 0——同 WaterLabRefractionFeature）；
    /// ③只对 capture 相机（Camera.main 或带 targetTexture 的 Game 相机）生效。
    /// 虽然 Feature 以子资产形式装在共享的 PC_Balanced_Renderer.asset 上（WaterLab_URPAsset
    /// 默认 index 0，与主战役 Balanced 共文件——先例同 WaterLabRefractionFeature/
    /// ReplicaGiFeature），运行行为被上面三道闸锁死在 WaterLab 出图流内。
    ///
    /// 【武装与卸载】Editor 侧 <c>WaterLabSetup.WaterLabL3CaptureHooks</c>（本文件域
    /// 的装配器加法）在截图器写出 battle-hud-1.png 后调 <see cref="RequestArm"/>，
    /// 下一个相机渲染即抓取（一次性）。静态 <see cref="Completed"/>/<see cref="Failed"/>
    /// 供 capture 流程/协调者轮询收口。**卸载条件**：L3 门禁收官归档后，从
    /// PC_Balanced_Renderer.asset 移除 "WaterLabColorDump" 子资产并同步
    /// m_RendererFeatures/m_RendererFeatureMap（逆操作见 WaterLabSetup.EnsureRt0DumpFeature），
    /// 本文件随之归档不再编译进出图链。
    /// </summary>
    public static class WaterLabColorDump
    {
        /// <summary>sim 使能全局（WaterLabSimulation 发布；Feature 侧第二重闸的键）。</summary>
        internal static readonly int SimEnabledId = Shader.PropertyToID("_WaterLabSimEnabled");

        /// <summary>env 进程常量：PC3D_WATERLAB_RT0_DUMP=1 才武装（主战役恒 false）。</summary>
        public static readonly bool EnabledByEnv =
            Environment.GetEnvironmentVariable("PC3D_WATERLAB_RT0_DUMP") == "1";

        /// <summary>Editor 侧时序钩子的武装信号（只应在 WaterLab 出图 Play 域置位）。</summary>
        public static bool ArmRequested { get; private set; }

        /// <summary>已发起抓取（一次性，防重复 blit）。</summary>
        public static bool Fired { get; private set; }

        /// <summary>raw+exr+png+meta 全部落盘完成。</summary>
        public static bool Completed { get; internal set; }

        /// <summary>抓取/落盘失败（超时或读回错误）。</summary>
        public static bool Failed { get; private set; }

        public static string LastError { get; private set; }

        /// <summary>武装时刻的模拟时钟（Editor 钩子注入；写进 meta.json 供相位对账
        /// ——目标 = 原作 cb0[113].y = 49.208416 s）。</summary>
        public static float CaptureSimTime { get; set; } = -1f;

        // ---- 抓取状态（Feature/Writer 同文件协作）----
        internal static RenderTexture DumpRT;
        internal static AsyncGPUReadbackRequest Pending;
        internal static bool HasPending;
        /// <summary>读回已到达（回调渲染线程置位；Pending 自此有效）。</summary>
        internal static volatile bool ReadbackArrived;

        /// <summary>cmd.RequestAsyncReadback 回调（渲染线程）：只存请求 + 置位，取数在 Writer 主线程。</summary>
        internal static void OnReadback(AsyncGPUReadbackRequest req) { Pending = req; ReadbackArrived = true; }
        internal static double StartedRealtime;
        internal static int Width, Height;

        /// <summary>Editor 钩子在 battle-hud-1.png 落盘后调用；下一个相机渲染抓取。</summary>
        public static void RequestArm() => ArmRequested = true;

        /// <summary>输出目录：与 BattleHudScreenshot.OutputDir 同式（env 绝对路径直达，
        /// 相对路径落仓库根；保证 ours-rt0-prepost.* 与 battle-hud-*.png 同目录）。</summary>
        public static string ResolveOutputDir()
        {
            var env = Environment.GetEnvironmentVariable("PC3D_CAPTURE_DIR");
            var tail = !string.IsNullOrEmpty(env) ? env : Path.Combine("temp", "hud-shots");
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", tail));
        }

        /// <summary>透明后/后处理前的相机色彩缓冲 blit + 读回（一次性）。Feature.Execute 调。</summary>
        internal static void BeginCapture(ScriptableRenderContext context, RenderingData renderingData)
        {
            if (Fired || Completed || Failed)
                return;
            Fired = true;
            ReadbackArrived = false;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            Width = desc.width;
            Height = desc.height;
            // 诊断（首跑全零排障加）：抓帧相机的 HDR/后处理/中间缓冲格式一次打全——
            // 若 desc=ARGB32/sRGB 或 isHdr=false，则 cameraColorTarget 是 LDR 链，差分口径作废
            Debug.Log($"[WaterLabColorDump] 诊断：isHdr={renderingData.cameraData.isHdrEnabled} " +
                $"post={renderingData.cameraData.postProcessEnabled} descFmt={desc.colorFormat} " +
                $"descSRGB={desc.sRGB} camAllowHDR={renderingData.cameraData.camera.allowHDR} " +
                $"w={desc.width} h={desc.height}");
            DumpRT = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "WaterLabRt0Dump",
                filterMode = FilterMode.Point,
            };
            DumpRT.Create();

            // 同尺寸同格式的 1:1 blit = 逐位拷贝（双线性在 texel 中心采样为恒等）；
            // NaN/Inf 位型在 blit 链路原样传播。持久 RT（非 GetTemporary）——读回可能
            // 晚于本帧，临时 RT 会被回收复用污染数据。
            var cmd = CommandBufferPool.Get("WaterLabRt0Dump");
            cmd.Blit(renderingData.cameraData.renderer.cameraColorTarget, DumpRT);
            // 读回必须走同一条命令缓冲排在 blit 之后：裸调 AsyncGPUReadback.Request
            // 会在 GPU 队列上立即执行拷贝，跑到尚未提交的 blit 前面——首跑全零的根因。
            // 回调在渲染线程触发，只置标志；Writer 主线程轮询取数。
            cmd.RequestAsyncReadback(DumpRT, 0, req => WaterLabColorDump.OnReadback(req));
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            HasPending = true;
            StartedRealtime = Time.realtimeSinceStartupAsDouble;

            WaterLabColorDumpWriter.Ensure();
            Debug.Log($"[WaterLabColorDump] 已发起 rt0 抓取：{Width}x{Height} ARGBHalf（线性域），" +
                $"simTime={CaptureSimTime:F4}s，输出目录 {ResolveOutputDir()}");
        }

        internal static void Fail(string why)
        {
            Failed = true;
            LastError = why;
            Debug.LogError("[WaterLabColorDump] 失败：" + why);
            ReleaseRT();
        }

        internal static void ReleaseRT()
        {
            if (DumpRT != null)
            {
                DumpRT.Release();
                UnityEngine.Object.DestroyImmediate(DumpRT);
                DumpRT = null;
            }
            HasPending = false;
        }
    }

    /// <summary>
    /// rt0 抓取的 URP 注入件（WaterLabSetup.EnsureRt0DumpFeature 幂等装入
    /// PC_Balanced_Renderer.asset——WaterLab_URPAsset 默认 renderer）。事件位
    /// BeforeRenderingPostProcessing = 透明队列之后、DoF/Vignette/SMAA 之前，
    /// 拿到的 cameraColorTarget 是后处理前的线性 HDR 相机色彩缓冲。闸门见
    /// <see cref="WaterLabColorDump"/> 类注释（env + simEnabled + capture 相机三重）。
    /// </summary>
    public sealed class WaterLabColorDumpFeature : ScriptableRendererFeature
    {
        sealed class DumpPass : ScriptableRenderPass
        {
            public DumpPass() => renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!WaterLabColorDump.ArmRequested || WaterLabColorDump.Fired)
                    return;
                WaterLabColorDump.BeginCapture(context, renderingData);
            }
        }

        DumpPass _pass;

        public override void Create() => _pass ??= new DumpPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!WaterLabColorDump.EnabledByEnv)
                return; // 主战役/普通出图：零开销直通（第一重闸）
            if (Shader.GetGlobalFloat(WaterLabColorDump.SimEnabledId) < 0.5f)
                return; // 非实验场景直通（第二重闸，同 WaterLabRefractionFeature）
            if (WaterLabColorDump.Fired || !WaterLabColorDump.ArmRequested)
                return; // 未武装 / 已抓取
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return; // SceneView/预览相机

            // 第三重闸：只认出图相机（LabCamera 挂 MainCamera tag；tag 缺失时回退
            // 首个带 targetTexture 的相机——截图器链路必带 targetTexture）。
            var cam = renderingData.cameraData.camera;
            var main = Camera.main;
            bool isCaptureCamera = main != null ? cam == main : cam.targetTexture != null;
            if (!isCaptureCamera)
                return;

            renderer.EnqueuePass(_pass);
        }
    }

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
