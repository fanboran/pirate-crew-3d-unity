using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// 实验水体的模拟驱动（W1）：分配缓冲、按固定步长调度 <c>WaterLabSim.compute</c> 的
    /// kernel 序、把结果发布为全局纹理供渲染 shader 采样。
    ///
    /// 【调度序与缓冲语义】见 compute 文件头：WaveSource(原地) → Height(A→B) →
    /// Velocity(B→A) → AdvectFoam(A→B) → Wetness(C→D，读最新水场 B) → Output(读 B)；
    /// 帧末 cur/next 换位。常量全部按 <see cref="WaterLabRules"/> 下发（compute 无字面量）。
    ///
    /// 【全局发布】本类是这些全局的**唯一发布者**（沿 WaterSimulationDriver 先例）：
    /// <c>_WaterLabHeightTex</c>（R=η G,B=法线xz A=泡沫）、<c>_WaterLabWetTex</c>（R=湿沙）、
    /// <c>_WaterLabCausticTex</c>（R=焦散拉普拉斯，OutputField 顺带烘焙，渲染侧乘
    /// _CausticGain——对标原作「焦散 RT 由引擎侧产出、材质只采 causticsMap」的外包分工）、
    /// <c>_WaterLabGroundTex</c>（R=地形高程编码，静态一次上传）、<c>_WaterLabOrigin</c>
    /// （x,z=域中心 y=域边长 w=水格数）、<c>_WaterLabSimEnabled</c>。禁用时清 0 防止渲染
    /// shader 采到冻结纹理。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterLabSimulation : MonoBehaviour
    {
        /// <summary>模拟内核（装配器注入；缺省按路径 Resources 加载）。</summary>
        public ComputeShader computeShader;

        // kernel 句柄
        int _kWaveSource, _kHeight, _kVelocity, _kAdvectFoam, _kWetness, _kOutput;

        ComputeBuffer _waterCur, _waterNext; // float4: h,vx,vz,foam × 256²
        ComputeBuffer _wetCur, _wetNext;     // float  × 128²
        ComputeBuffer _ground;               // float  × 128²（静态）

        RenderTexture _heightTex;  // 256²（ARGBHalf，见 NewDataTexture）
        RenderTexture _wetTex;     // 128² ARGB32（R 有效）
        RenderTexture _causticTex; // 256² ARGBHalf（R=焦散拉普拉斯，带符号；OutputField 顺带烘焙）
        Texture2D _groundTex;      // 128² R8 编码（静态）

        float _accumulator;
        float _simTime;

        /// <summary>当前模拟时间（波源相位用）。</summary>
        public float SimTime => _simTime;

        /// <summary>
        /// 模拟时间倍率（<c>PC3D_WATERLAB_TIME_SCALE</c>，默认 1）。涌浪填满 64 m 域需
        /// 30 s+ 实时，而无头截图链进场约 2 s 就开拍（拍到的永远是静水开场）——出图时
        /// 把模拟时间倍率调大先到稳态，渲染所见即稳态场。步数上限同倍放大，追帧
        /// 雪崩保护语义不变。
        /// </summary>
        static readonly float TimeScale = ResolveTimeScale();

        static float ResolveTimeScale()
        {
            string raw = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_TIME_SCALE");
            return float.TryParse(raw, out float v) && v > 0f ? Mathf.Min(v, 60f) : 1f;
        }

        void Awake()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                enabled = false;
                Debug.LogError("[WaterLabSimulation] 本机不支持 compute shader，实验水体模拟未启动。");
                return;
            }

            if (computeShader == null)
            {
                enabled = false;
                Debug.LogError("[WaterLabSimulation] computeShader 未注入（场景装配器负责引用 WaterLabSim.compute），模拟未启动。");
                return;
            }

            _kWaveSource = computeShader.FindKernel("WaveSource");
            _kHeight = computeShader.FindKernel("HeightIntegration");
            _kVelocity = computeShader.FindKernel("VelocityIntegration");
            _kAdvectFoam = computeShader.FindKernel("AdvectFoam");
            _kWetness = computeShader.FindKernel("WetnessStep");
            _kOutput = computeShader.FindKernel("OutputField");

            CreateBuffers();
            CreateTextures();
            PublishGlobals();
        }

        void OnDestroy()
        {
            _waterCur?.Dispose(); _waterNext?.Dispose();
            _wetCur?.Dispose(); _wetNext?.Dispose();
            _ground?.Dispose();
            DestroyTexture(ref _heightTex);
            DestroyTexture(ref _wetTex);
            DestroyTexture(ref _causticTex);
            if (_groundTex != null)
            {
                Destroy(_groundTex);
                _groundTex = null;
            }
            Shader.SetGlobalFloat(ShaderIds.SimEnabled, 0f);
        }

        void OnDisable() => Shader.SetGlobalFloat(ShaderIds.SimEnabled, 0f);
        void OnEnable() => Shader.SetGlobalFloat(ShaderIds.SimEnabled, 1f);

        void Update()
        {
            _accumulator += Mathf.Min(Time.deltaTime, 0.1f) * TimeScale;
            int maxSteps = Mathf.CeilToInt(4f * TimeScale);
            int steps = 0;
            while (_accumulator >= WaterLabRules.FixedStep && steps < maxSteps)
            {
                StepOnce(WaterLabRules.FixedStep);
                _accumulator -= WaterLabRules.FixedStep;
                steps++;
            }
            if (steps == maxSteps)
                _accumulator = 0f; // 追帧雪崩保护（沿 WaterSimulationDriver 先例）

        // 诊断落盘（环境变量开才生效）：把高度/湿沙纹理存 PNG，供无头链排查模拟纹理内容。
        // 【模拟时间锚定】按 simTime 取样（36 s 起每 1.2 s 一张×8，覆盖一个主波周期）——
        // 旧实现按真实时间在开机 1 秒内连拍 3 张，拍到的是开场而非稳态；且与截图链的
        // PC3D_CAPTURE_MIN_SIM_TIME 稳态门同口径（先转储后开拍）。注意 $TEMP 里的同名
        // 旧文件会冒充本轮产物——capture 前先清空。
        if (System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_DUMP") == "1")
            DumpDiagnostics();
    }

    int _dumpIndex;

    void DumpDiagnostics()
    {
        if (_simTime < 36f || _dumpIndex >= 8)
            return;
        if (_simTime < 36f + _dumpIndex * 1.2f)
            return;
        DumpTexture(_heightTex, $"waterlab-height-{_dumpIndex}");
        DumpTexture(_wetTex, $"waterlab-wet-{_dumpIndex}");
        _dumpIndex++;
    }

        static void DumpTexture(RenderTexture rt, string name)
        {
            if (rt == null)
                return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var reader = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            reader.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            reader.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), name + ".png"), reader.EncodeToPNG());
            Destroy(reader);
            Debug.LogWarning($"[WaterLab] 诊断落盘 {name}.png");
        }

        /// <summary>一步完整 kernel 序（compute 文件头的调度约定）。绑定走 compute 级统一入口
        /// （不按 kernel 分开绑），帧首一次绑齐、换位后重绑——避免「Property not set」类
        /// kernel 级绑定缝隙。</summary>
        void StepOnce(float dt)
        {
            _simTime += dt;
            PushConstants(dt);
            BindAll();

            int waterGroups = WaterLabRules.WaterCells / 8;
            int groundGroups = WaterLabRules.GroundCells / 8;

            // WaveSource(原地写 A) → Height(A→B) → Velocity(B→A) → AdvectFoam(A→B)
            // → Wetness(C→D，读最新水场 B) → Output(读 B)
            computeShader.Dispatch(_kWaveSource, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kHeight, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kVelocity, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kAdvectFoam, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kWetness, groundGroups, groundGroups, 1);
            computeShader.Dispatch(_kOutput, waterGroups, waterGroups, 1);

            // 帧末换位：最新水场在 next、最新湿沙在 nextW
            (_waterCur, _waterNext) = (_waterNext, _waterCur);
            (_wetCur, _wetNext) = (_wetNext, _wetCur);
            BindAll();
        }

        /// <summary>compute 绑定（A=cur、B=next 对齐 compute 文件头的调度约定）。
        /// 本版 ComputeShader 的 SetBuffer/SetTexture 只收 kernelIndex 形式——对全部 kernel
        /// 逐个绑齐，消除 kernel 级「Property not set」警告的全部空间。</summary>
        void BindAll()
        {
            foreach (int k in new[] { _kWaveSource, _kHeight, _kVelocity, _kAdvectFoam, _kWetness, _kOutput })
            {
                computeShader.SetBuffer(k, ShaderIds.WaterA, _waterCur);
                computeShader.SetBuffer(k, ShaderIds.WaterB, _waterNext);
                computeShader.SetBuffer(k, ShaderIds.Ground, _ground);
                computeShader.SetBuffer(k, ShaderIds.WetA, _wetCur);
                computeShader.SetBuffer(k, ShaderIds.WetB, _wetNext);
                computeShader.SetTexture(k, ShaderIds.WetTex, _wetTex);
                computeShader.SetTexture(k, ShaderIds.HeightTex, _heightTex);
                computeShader.SetTexture(k, ShaderIds.CausticTex, _causticTex);
            }
        }

        void CreateBuffers()
        {
            int waterCount = WaterLabRules.WaterCells * WaterLabRules.WaterCells;
            int groundCount = WaterLabRules.GroundCells * WaterLabRules.GroundCells;

            _waterCur = new ComputeBuffer(waterCount, 16);
            _waterNext = new ComputeBuffer(waterCount, 16);
            _wetCur = new ComputeBuffer(groundCount, 4);
            _wetNext = new ComputeBuffer(groundCount, 4);
            _ground = new ComputeBuffer(groundCount, 4);

            // 初始水场：静水面（h = SeaLevel - ground，干处 0）；泡沫 0
            var waterInit = new Vector4[waterCount];
            var groundData = new float[groundCount];
            for (int cz = 0; cz < WaterLabRules.GroundCells; cz++)
            {
                for (int cx = 0; cx < WaterLabRules.GroundCells; cx++)
                {
                    float wx = WaterLabRules.GroundCellCenter(cx);
                    float wz = WaterLabRules.GroundCellCenter(cz);
                    float g = WaterLabRules.TerrainHeight(wx, wz);
                    groundData[cx + cz * WaterLabRules.GroundCells] = g;
                }
            }
            for (int cz = 0; cz < WaterLabRules.WaterCells; cz++)
            {
                for (int cx = 0; cx < WaterLabRules.WaterCells; cx++)
                {
                    float wx = -WaterLabRules.DomainSize * 0.5f + (cx + 0.5f) * WaterLabRules.WaterCellSize;
                    float wz = -WaterLabRules.DomainSize * 0.5f + (cz + 0.5f) * WaterLabRules.WaterCellSize;
                    float g = WaterLabRules.TerrainHeight(wx, wz);
                    float h = Mathf.Max(WaterLabRules.SeaLevel - g, 0f);
                    waterInit[cx + cz * WaterLabRules.WaterCells] = new Vector4(h, 0f, 0f, 0f);
                }
            }
            _waterCur.SetData(waterInit);
            _waterNext.SetData(waterInit);
            _ground.SetData(groundData);

            // 地形静态纹理（渲染 shader 采样：R=高程编码 [-3,2]→[0,1]，与 OutputField 同式）
            _groundTex = new Texture2D(WaterLabRules.GroundCells, WaterLabRules.GroundCells,
                TextureFormat.RGBAFloat, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WaterLabGround" };
            var groundPixels = new Color[groundCount];
            for (int i = 0; i < groundCount; i++)
                groundPixels[i] = new Color(Mathf.Clamp01((groundData[i] + 3f) / 5f), 0f, 0f, 1f);
            _groundTex.SetPixels(groundPixels);
            _groundTex.Apply(false, false);
        }

        void CreateTextures()
        {
            _heightTex = NewDataTexture(WaterLabRules.WaterCells, "WaterLabHeight");
            _wetTex = NewDataTexture(WaterLabRules.GroundCells, "WaterLabWet");
            _causticTex = NewDataTexture(WaterLabRules.WaterCells, "WaterLabCaustic");
        }

        static RenderTexture NewDataTexture(int size, string name)
        {
            // ARGBHalf：8bit 的 ARGB32 存 η（5m 量程）每级 2cm，顶点位移与 alpha 全是台阶噪声
            //（实测浅水带细点残余的根因）；16F 后 smooth。
            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBHalf, 0)
            {
                enableRandomWrite = true,
                sRGB = false,
            };
            var rt = new RenderTexture(desc)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        static void DestroyTexture(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
                rt = null;
            }
        }

        void PushConstants(float dt)
        {
            var cs = computeShader;
            cs.SetInt(ShaderIds.Cells, WaterLabRules.WaterCells);
            cs.SetFloat(ShaderIds.CellSize, WaterLabRules.WaterCellSize);
            cs.SetInt(ShaderIds.GroundCells, WaterLabRules.GroundCells);
            cs.SetFloat(ShaderIds.GroundCellSize, WaterLabRules.GroundCellSize);
            cs.SetFloat(ShaderIds.HalfDomain, WaterLabRules.DomainSize * 0.5f);
            cs.SetFloat(ShaderIds.Dt, dt);
            cs.SetFloat(ShaderIds.Gravity, WaterLabRules.Gravity);
            cs.SetFloat(ShaderIds.Damping, WaterLabRules.VelocityDamping);
            cs.SetFloat(ShaderIds.MaxVelocity, WaterLabRules.MaxVelocity);
            cs.SetFloat(ShaderIds.DryDepth, WaterLabRules.DryDepth);
            cs.SetFloat(ShaderIds.Time, _simTime);
            cs.SetFloat(ShaderIds.SeaLevel, WaterLabRules.SeaLevel);
            cs.SetFloat(ShaderIds.WaveBandWidth, WaterLabRules.WaveBandWidth);
            cs.SetFloat(ShaderIds.GridFilterEps, WaterLabRules.GridFilterEps);
            PushWaveTrains();
            cs.SetFloat(ShaderIds.TideAmp, WaterLabRules.TideAmplitude);
            cs.SetFloat(ShaderIds.TideOmega, 2f * Mathf.PI / WaterLabRules.TidePeriod);
            cs.SetFloat(ShaderIds.TideRelaxRate, WaterLabRules.TideRelaxDepthRate);
            cs.SetFloat(ShaderIds.WaveRelaxRate, WaterLabRules.WaveRelaxRate);
            cs.SetFloat(ShaderIds.BreakingSteepness, WaterLabRules.BreakingSteepness);
            cs.SetFloat(ShaderIds.FoamGain, WaterLabRules.FoamGain);
            cs.SetFloat(ShaderIds.FoamDecay, WaterLabRules.FoamDecay);
            cs.SetFloat(ShaderIds.FoamDiffuse, WaterLabRules.FoamDiffuse);
            cs.SetFloat(ShaderIds.WetSoakRate, WaterLabRules.WetnessSoakRate);
            cs.SetFloat(ShaderIds.WetEvaporateRate, WaterLabRules.WetnessEvaporateRate);
            cs.SetFloat(ShaderIds.WetDiffuse, WaterLabRules.WetnessDiffuse);
            cs.SetInt(ShaderIds.SpongeCells, WaterLabRules.SpongeCells);
            cs.SetFloat(ShaderIds.SpongeMaxDamping, WaterLabRules.SpongeMaxDamping);
        }

        // 波列常量数组（PushConstants 每步复用，避免逐帧分配）
        readonly Vector4[] _trainsA = new Vector4[4];
        readonly Vector4[] _trainsB = new Vector4[4];

        /// <summary>波列常量下发（A: 方向/k/ω，B: 振幅/初相；与 compute 的 _WaveTrainsA/B 对齐）。</summary>
        void PushWaveTrains()
        {
            var trains = WaterLabRules.WaveTrains;
            for (int i = 0; i < trains.Length && i < 4; i++)
            {
                _trainsA[i] = new Vector4(trains[i].DirX, trains[i].DirZ, trains[i].K, trains[i].Omega);
                _trainsB[i] = new Vector4(trains[i].Amplitude, trains[i].Phase, 0f, 0f);
            }
            computeShader.SetVectorArray(ShaderIds.WaveTrainsA, _trainsA);
            computeShader.SetVectorArray(ShaderIds.WaveTrainsB, _trainsB);
        }

        void PublishGlobals()
        {
            Shader.SetGlobalTexture(ShaderIds.HeightTex, _heightTex);
            Shader.SetGlobalTexture(ShaderIds.WetTex, _wetTex);
            Shader.SetGlobalTexture(ShaderIds.CausticTex, _causticTex);
            Shader.SetGlobalTexture(ShaderIds.GroundTex, _groundTex);
            // 域中心固定在原点（场景装配约定），发布一次即可。
            // 【约定】shader 端 WorldToDomainUv 读 .xz 当中心、.y 当域边长——
            // 参数序必须是 ( centerX, size, centerZ, cells )。曾误写 (0,0,64,256)，
            // y=0 除零 → UV=INF → 采样钳到边缘列 eta≈+0.5 → 全场「水深1.7m」、
            // 水蒙整张地（2026-10-15 诊断帧实锤，28 轮隐形水的根因）。
            Shader.SetGlobalVector(ShaderIds.Origin, new Vector4(
                0f, WaterLabRules.DomainSize, 0f, WaterLabRules.WaterCells));
            Shader.SetGlobalFloat(ShaderIds.SimEnabled, 1f);
            PublishSunDir();
        }

        /// <summary>
        /// 解析太阳方向发布 <c>_WaterLabSunDir</c>（世界空间**指向太阳**，L = -light.forward；
        /// 解析顺序 RenderSettings.sun → 静态兜底【与装配器默认太阳同向】——沿
        /// WaterSimulationDriver.PublishSunDir 同一口径。缺它时渲染 shader 里
        /// normalize(零向量) 出 NaN，全部太阳项（Lambert/GGX/glitter）一起灭。
        /// </summary>
        void PublishSunDir()
        {
            Light sun = RenderSettings.sun;
            Vector3 dir = sun != null ? -sun.transform.forward
                                      : new Vector3(-0.525f, 0.669f, -0.525f);
            Shader.SetGlobalVector(ShaderIds.SunDir, dir);
        }

        /// <summary>shader 属性名单源（compute 绑定与全局发布共用）。</summary>
        static class ShaderIds
        {
            public static readonly int WaterA = Shader.PropertyToID("_WaterA");
            public static readonly int WaterB = Shader.PropertyToID("_WaterB");
            public static readonly int WetA = Shader.PropertyToID("_WetA");
            public static readonly int WetB = Shader.PropertyToID("_WetB");
            public static readonly int Ground = Shader.PropertyToID("_Ground");
            public static readonly int HeightTex = Shader.PropertyToID("_WaterLabHeightTex");
            public static readonly int WetTex = Shader.PropertyToID("_WaterLabWetTex");
            public static readonly int CausticTex = Shader.PropertyToID("_WaterLabCausticTex");
            public static readonly int GroundTex = Shader.PropertyToID("_WaterLabGroundTex");
            public static readonly int Origin = Shader.PropertyToID("_WaterLabOrigin");
            public static readonly int SunDir = Shader.PropertyToID("_WaterLabSunDir");
            public static readonly int SimEnabled = Shader.PropertyToID("_WaterLabSimEnabled");

            public static readonly int Cells = Shader.PropertyToID("_Cells");
            public static readonly int CellSize = Shader.PropertyToID("_CellSize");
            public static readonly int GroundCells = Shader.PropertyToID("_GroundCells");
            public static readonly int GroundCellSize = Shader.PropertyToID("_GroundCellSize");
            public static readonly int HalfDomain = Shader.PropertyToID("_HalfDomain");
            public static readonly int Dt = Shader.PropertyToID("_Dt");
            public static readonly int Gravity = Shader.PropertyToID("_Gravity");
            public static readonly int Damping = Shader.PropertyToID("_Damping");
            public static readonly int MaxVelocity = Shader.PropertyToID("_MaxVelocity");
            public static readonly int DryDepth = Shader.PropertyToID("_DryDepth");
            public static readonly int Time = Shader.PropertyToID("_Time");
            public static readonly int SeaLevel = Shader.PropertyToID("_SeaLevel");
            public static readonly int WaveBandWidth = Shader.PropertyToID("_WaveBandWidth");
            public static readonly int GridFilterEps = Shader.PropertyToID("_GridFilterEps");
            public static readonly int WaveTrainsA = Shader.PropertyToID("_WaveTrainsA");
            public static readonly int WaveTrainsB = Shader.PropertyToID("_WaveTrainsB");
            public static readonly int TideAmp = Shader.PropertyToID("_TideAmp");
            public static readonly int TideOmega = Shader.PropertyToID("_TideOmega");
            public static readonly int TideRelaxRate = Shader.PropertyToID("_TideRelaxRate");
            public static readonly int WaveRelaxRate = Shader.PropertyToID("_WaveRelaxRate");
            public static readonly int BreakingSteepness = Shader.PropertyToID("_BreakingSteepness");
            public static readonly int FoamGain = Shader.PropertyToID("_FoamGain");
            public static readonly int FoamDecay = Shader.PropertyToID("_FoamDecay");
            public static readonly int FoamDiffuse = Shader.PropertyToID("_FoamDiffuse");
            public static readonly int WetSoakRate = Shader.PropertyToID("_WetSoakRate");
            public static readonly int WetEvaporateRate = Shader.PropertyToID("_WetEvaporateRate");
            public static readonly int WetDiffuse = Shader.PropertyToID("_WetDiffuse");
            public static readonly int SpongeCells = Shader.PropertyToID("_SpongeCells");
            public static readonly int SpongeMaxDamping = Shader.PropertyToID("_SpongeMaxDamping");
        }
    }
}
