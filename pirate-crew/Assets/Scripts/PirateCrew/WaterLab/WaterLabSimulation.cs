using UnityEngine;
using PirateCrew.Rendering;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// 实验水体的模拟驱动（W1）：分配缓冲、按固定步长调度 <c>WaterLabSim.compute</c> 的
    /// kernel 序、把结果发布为全局纹理供渲染 shader 采样。
    ///
    /// 【调度序与缓冲语义】若干子步 Height(A→B) → Velocity(B→A) → AdvectFoam(A→B)
    /// → Wetness(C→D) → 子步末换位 → **帧末一次** SurfaceUpdate（原作 UpdateAmount：
    /// 蒸发 + 潮位松弛，原地写 A）→ Output(读 A)。顺序与原作
    /// `…Advection → UpdateAmount → UpdateTangent` 一致：先松弛再发布，避免把
    /// Advection 带进 5 cm 结合水带的 h 直接上屏（岸线游离水膜）。
    /// 常量全部按 <see cref="WaterLabRules"/> 下发（compute 无字面量）。
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

        /// <summary>
        /// 白水逐粒视锥剔除所用相机（装配器接线；为空时回落 <see cref="Camera.main"/>，
        /// 再为空则不做剔除——恒定绘出全部活粒子）。
        /// </summary>
        public Camera renderCamera;

        /// <summary>
        /// 主菜单快照（I7 ReplicaMode）：groundHeight/waterAmount 各 256² float32。
        /// 装配器（WaterLabSetup.CreateSimulation）按资产路径注入；两者都非空且校验通过
        /// 时 <see cref="ReplicaMode"/> 开——地形/水位走快照；任一缺失或加载失败回退
        /// 程序剖面（ReplicaMode 判定 = 快照资产可加载，协调者裁决 5）。
        /// </summary>
        public TextAsset replicaGroundSnapshot;
        public TextAsset replicaWaterSnapshot;

        /// <summary>主菜单快照复刻是否生效（= 快照已加载，TerrainHeight/初始水位已切数据源）。</summary>
        public bool ReplicaMode => WaterLabReplicaSnapshot.HasData;

        // kernel 句柄
        int _kSurface, _kHeight, _kVelocity, _kAdvectFoam, _kWetness, _kOutput;
        int _kWWBegin, _kWWSpawn, _kWWUpdate, _kWWCompact, _kWWEnd;

        ComputeBuffer _waterCur, _waterNext; // float4: h,vx,vz,foam × WaterCells²
        ComputeBuffer _wetCur, _wetNext;     // float  × GroundCells²
        ComputeBuffer _ground;               // float  × GroundCells²（静态）
        ComputeBuffer _wwPos, _wwVel;        // float4 × 容量（白水粒子池，缓冲环）
        ComputeBuffer _wwDraw;               // uint   × 容量（活粒子 draw list）
        ComputeBuffer _wwAlive, _wwArgs;     // uint ×2（计数/游标）、uint ×4（indirect args）

        RenderTexture _heightTex;  // WaterCells²（ARGBHalf，见 NewDataTexture）
        RenderTexture _wetTex;     // GroundCells²（ARGBHalf，R 有效）
        RenderTexture _causticTex; // WaterCells² ARGBHalf（R=焦散拉普拉斯，带符号；OutputField 顺带烘焙）
        Texture2D _groundTex;      // GroundCells² RGBAFloat（R 编码高程）

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

        /// <summary>
        /// 出图用相位冻结（<c>PC3D_WATERLAB_FIXED_SIM_TIME</c>，秒，默认 &lt;0 不冻结）。
        /// 潮位周期 2π s，TIME_SCALE 下每帧跳 0.5 s，三枪相位随机；给定目标时刻后
        /// 模拟到达即停住，用于低潮/高潮的确定性对比。只影响实验出图，不进模拟公式。
        /// </summary>
        static readonly float FixedSimTime = ResolveFixedSimTime();

        static float ResolveFixedSimTime()
        {
            string raw = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_FIXED_SIM_TIME");
            return float.TryParse(raw, out float v) && v >= 0f ? v : -1f;
        }

        /// <summary>
        /// 水体渲染诊断档（<c>PC3D_WATERLAB_OCEAN_DEBUG</c>，默认 0）。非 0 时水面 PS
        /// 不裁剪并按 2x2 分格输出中间量（depth/dist/T/底色/取样值），用于区分
        /// 「水没画出来」「裁掉了」「取样读到 0」「吸收系数没到」四类只在实跑才暴露的
        /// 绑定问题——必须在 Play 域读（编辑器域置全局会被域重载吞掉）。
        /// </summary>
        static float ResolveOceanDebug()
        {
            return System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_OCEAN_DEBUG") is string raw
                && float.TryParse(raw, out float v) && v > 0f ? v : 0f;
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

            _kSurface = computeShader.FindKernel("SurfaceUpdate");
            _kHeight = computeShader.FindKernel("HeightIntegration");
            _kVelocity = computeShader.FindKernel("VelocityIntegration");
            _kAdvectFoam = computeShader.FindKernel("AdvectFoam");
            _kWetness = computeShader.FindKernel("WetnessStep");
            _kOutput = computeShader.FindKernel("OutputField");
            _kWWBegin = computeShader.FindKernel("WhitewaterBegin");
            _kWWSpawn = computeShader.FindKernel("WhitewaterSpawn");
            _kWWUpdate = computeShader.FindKernel("WhitewaterUpdate");
            _kWWCompact = computeShader.FindKernel("WhitewaterCompact");
            _kWWEnd = computeShader.FindKernel("WhitewaterEnd");

            TryLoadReplicaSnapshot();
            CreateBuffers();
            CreateTextures();
            PublishGlobals();
        }

        /// <summary>
        /// 快照接线（在 CreateBuffers 之前——地形/水位初值都要读它）。校验失败
        /// （长度/四角翻转/负水量，见 WaterLabReplicaSnapshot.Load）按差分门禁精神
        /// 走「保数值语义」的回退：卸载快照、退回程序剖面并显式报错，不静默带病运行。
        /// </summary>
        void TryLoadReplicaSnapshot()
        {
            if (replicaGroundSnapshot == null || replicaWaterSnapshot == null)
                return; // 未接线 = ReplicaMode 关（程序剖面回退）
            try
            {
                WaterLabReplicaSnapshot.Load(replicaGroundSnapshot.bytes, replicaWaterSnapshot.bytes);
                Debug.LogWarning($"[WaterLabSimulation] ReplicaMode=开（主菜单快照）{WaterLabReplicaSnapshot.Describe()}");
            }
            catch (System.Exception e)
            {
                WaterLabReplicaSnapshot.Unload();
                Debug.LogError($"[WaterLabSimulation] 主菜单快照加载失败，回退程序剖面：{e.Message}");
            }
        }

        void OnDestroy()
        {
            WaterLabReplicaSnapshot.Unload(); // 静态采样器不跨 Play 会话残留
            _waterCur?.Dispose(); _waterNext?.Dispose();
            _wetCur?.Dispose(); _wetNext?.Dispose();
            _ground?.Dispose();
            _wwPos?.Dispose(); _wwVel?.Dispose();
            _wwDraw?.Dispose(); _wwAlive?.Dispose(); _wwArgs?.Dispose();
            WaterLabWhitewaterBridge.Clear();
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
            float frameDt = Mathf.Min(Time.deltaTime, 0.1f) * TimeScale;
            if (FixedSimTime >= 0f)
            {
                if (_simTime >= FixedSimTime)
                {
                    _simTime = FixedSimTime;
                    frameDt = 0f;
                    _accumulator = 0f;
                }
                else if (_simTime + frameDt > FixedSimTime)
                {
                    frameDt = FixedSimTime - _simTime;
                }
            }
            _accumulator += frameDt;

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
            if (FixedSimTime >= 0f && _simTime > FixedSimTime)
                _simTime = FixedSimTime;

            // 原作 Water.Run 的 UpdateAmount 在**所有子步之后**每帧派发一次，紧接着
            // UpdateTangent 发布高度/法线。顺序不能反：Advection 会把 h 平流进 5 cm
            // 结合水带，若先松弛后平流，渲染看到的是平流后的 h（岸线出现游离水膜）；
            // 先子步、后松弛+发布，上屏的才是原作“松弛后的表面”。
            if (frameDt > 0f)
                UpdateSurface(frameDt);

        // 诊断落盘（环境变量开才生效）：把高度/湿沙纹理存 PNG，供无头链排查模拟纹理内容。
        // 【模拟时间锚定】按 simTime 取样（36 s 起每 1.2 s 一张×8，覆盖一个主波周期）——
        // 旧实现按真实时间在开机 1 秒内连拍 3 张，拍到的是开场而非稳态；且与截图链的
        // PC3D_CAPTURE_MIN_SIM_TIME 稳态门同口径（先转储后开拍）。注意 $TEMP 里的同名
        // 旧文件会冒充本轮产物——capture 前先清空。
        if (System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_DUMP") == "1")
            DumpDiagnostics();
        if (System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_WW_DUMP") == "1")
            DumpWhitewaterCount();
        DumpCameraOnce();
    }

    /// <summary>
    /// 一次性相机点名（<c>PC3D_WATERLAB_CAM_DUMP=1</c>）。出图链的相机是 Camera.main，
    /// 而本场景的 LabCamera 未打 MainCamera tag——若场景里另有一台被标了 tag 的相机
    /// （出图准备的 UI 相机等），抓帧用的就不是装配器那台，取景/投影全对不上。
    /// 站位、投影、渲染目标一次打全，供无头链判「拍的是哪台」。
    /// </summary>
    void DumpCameraOnce()
    {
        if (System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_CAM_DUMP") != "1")
            return;
        // 两个检查点：开场 2s 与出图前的 36s（第一枪前会 PrepareCanvasForCapture，
        // 可能新添相机——抓帧用的 FindObjectOfType<Camera>() 会改为命中它）
        int want = _simTime >= 36f ? 2 : (_simTime >= 2f ? 1 : 0);
        if (want == 0 || _camDumped >= want)
            return;
        _camDumped = want;
        var main = Camera.main;
        Debug.LogWarning($"[WaterLab] 检查点{_camDumped} simTime={_simTime:F1}s Camera.main = "
            + $"{(main == null ? "空" : main.name + " tag=" + main.tag)}"
            + $"；渲染相机引用 = {(renderCamera == null ? "空" : renderCamera.name)}");
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var t = c.transform;
            Debug.LogWarning($"[WaterLab] 相机 {c.name} tag={c.tag} 使能={c.enabled} 激活={c.gameObject.activeInHierarchy}"
                + $" ortho={c.orthographic} size={c.orthographicSize:F2} fov={c.fieldOfView:F1}"
                + $" depth={c.depth:F1} 层={c.cullingMask} RT={(c.targetTexture == null ? "空" : c.targetTexture.name)}"
                + $" pos={t.position} rot={t.rotation.eulerAngles}");
        }

        // 水面材质的运行期真值：shader 名 + 吸收/水色/折射/干区阈值/网格顶点数。
        // 「吸收不生效」若源于材质属性没进 shader，这里读出来就是 0 或旧值。
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m == null || m.shader == null || !m.shader.name.Contains("WaterLab"))
                continue;
            Debug.LogWarning($"[WaterLab] 水面材质 {m.name} shader={m.shader.name}"
                + $" _Absorption={m.GetVector("_Absorption")} _WaterTint={m.GetVector("_WaterTint")}"
                + $" _RefractionFactor={m.GetFloat("_RefractionFactor"):F4} _DryClip={m.GetFloat("_DryClip"):F3}"
                + $" 网格={(r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0} 顶点");
        }
    }

    int _camDumped;
    bool _constantsLogged;

    int _dumpIndex;
    int _wwDumpIndex;
    bool _wwDumpPending;

    /// <summary>
    /// 白水链路诊断（环境变量 <c>PC3D_WATERLAB_WW_DUMP=1</c>）：按 simTime 锚点回读
    /// <c>_WWAlive</c>——<c>[0]</c>=本步 draw list 长度（**已过视锥剔除**、即真正出画的粒子数）、
    /// <c>[1]</c>=缓冲环写游标（累计生成数）。用于把「白水偏含蓄」拆成
    /// 「没生成 / 生成了被剔除 / 生成了但看不见」三种可能（异步回读，不阻塞模拟）。
    /// </summary>
    void DumpWhitewaterCount()
    {
        if (_simTime < 36f || _wwDumpIndex >= 8 || _wwDumpPending)
            return;
        if (_simTime < 36f + _wwDumpIndex * 1.2f)
            return;
        _wwDumpPending = true;
        int idx = _wwDumpIndex++;
        UnityEngine.Rendering.AsyncGPUReadback.Request(_wwAlive, r =>
        {
            _wwDumpPending = false;
            if (r.hasError)
            {
                Debug.LogWarning($"[WaterLab] 白水回读 {idx} 失败");
                return;
            }
            var d = r.GetData<uint>();
            Debug.LogWarning($"[WaterLab] 白水 {idx}: drawList={d[0]} 累计生成={d[1]}"
                + $" 容量={WaterLabRules.WhitewaterCapacity} simTime={_simTime:F1}s");
        });
    }

    void DumpDiagnostics()
    {
        // 诊断窗口：默认 36 s 起（稳态）；环境变量 PC3D_WATERLAB_DUMP_EARLY=1 时
        // 改为 0.5 s 起、每 0.5 s 一张，用于定位“水体在最初几秒被抽干”这类问题。
        bool early = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_DUMP_EARLY") == "1";
        float start = early ? 0.5f : 36f;
        float interval = early ? 0.5f : 1.2f;
        if (_simTime < start || _dumpIndex >= 8)
            return;
        if (_simTime < start + _dumpIndex * interval)
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
        /// <summary>
        /// 原作 UpdateAmount 的帧级对应：水场蒸发 + 全场潮位松弛（每帧一次，在全部子步
        /// 之后）。写入当前水场 A，随后立刻派发 OutputField 把松弛后的表面发布上屏——
        /// 原作 `UpdateAmount → UpdateTangent` 同序；不能把松弛放在子步之前，否则
        /// Advection 会把 h 平流进 5 cm 结合水带、渲染出游离水膜。
        /// </summary>
        void UpdateSurface(float frameDt)
        {
            PushConstants(frameDt);
            BindAll();
            int groups = (WaterLabRules.WaterCells + 7) / 8;
            computeShader.Dispatch(_kSurface, groups, groups, 1);
            computeShader.Dispatch(_kOutput, groups, groups, 1);
        }

        void StepOnce(float dt)
        {
            _simTime += dt;
            PushConstants(dt);
            BindAll();

            int waterGroups = (WaterLabRules.WaterCells + 7) / 8;
            int groundGroups = (WaterLabRules.GroundCells + 7) / 8;

            // 子步：Height(A→B) → Velocity(B→A) → AdvectFoam(A→B)
            // → 白水（Begin→Spawn(读B泡沫)→Update→Compact→End）
            // → Wetness(C→D，读最新水场 B)。Output 不在这里——帧末 UpdateSurface
            // 先把 h 松弛回原作目标，再由它统一派发 OutputField（读 A），保证
            // 上屏的是松弛后的表面而不是 Advection 后的游离水膜。
            computeShader.Dispatch(_kHeight, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kVelocity, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kAdvectFoam, waterGroups, waterGroups, 1);
            int wwGroups = WaterLabRules.WhitewaterCapacity / 64;
            computeShader.Dispatch(_kWWBegin, 1, 1, 1);
            computeShader.Dispatch(_kWWSpawn, waterGroups, waterGroups, 1);
            computeShader.Dispatch(_kWWUpdate, wwGroups, 1, 1);
            computeShader.Dispatch(_kWWCompact, wwGroups, 1, 1);
            computeShader.Dispatch(_kWWEnd, 1, 1, 1);
            computeShader.Dispatch(_kWetness, groundGroups, groundGroups, 1);

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
            foreach (int k in new[] { _kSurface, _kHeight, _kVelocity, _kAdvectFoam, _kWetness, _kOutput,
                     _kWWBegin, _kWWSpawn, _kWWUpdate, _kWWCompact, _kWWEnd })
            {
                computeShader.SetBuffer(k, ShaderIds.WaterA, _waterCur);
                computeShader.SetBuffer(k, ShaderIds.WaterB, _waterNext);
                computeShader.SetBuffer(k, ShaderIds.Ground, _ground);
                computeShader.SetBuffer(k, ShaderIds.WetA, _wetCur);
                computeShader.SetBuffer(k, ShaderIds.WetB, _wetNext);
                computeShader.SetTexture(k, ShaderIds.WetTex, _wetTex);
                computeShader.SetTexture(k, ShaderIds.HeightTex, _heightTex);
                computeShader.SetTexture(k, ShaderIds.CausticTex, _causticTex);
                computeShader.SetBuffer(k, ShaderIds.WWPos, _wwPos);
                computeShader.SetBuffer(k, ShaderIds.WWVel, _wwVel);
                computeShader.SetBuffer(k, ShaderIds.WWDraw, _wwDraw);
                computeShader.SetBuffer(k, ShaderIds.WWAlive, _wwAlive);
                computeShader.SetBuffer(k, ShaderIds.WWArgs, _wwArgs);
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
            _wwPos = new ComputeBuffer(WaterLabRules.WhitewaterCapacity, 16);
            _wwVel = new ComputeBuffer(WaterLabRules.WhitewaterCapacity, 16);
            _wwDraw = new ComputeBuffer(WaterLabRules.WhitewaterCapacity, 4);
            _wwAlive = new ComputeBuffer(2, 4);
            _wwArgs = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);

            // 初始场（B-terrain §6，裁决 5）：
            //   _ground ← TerrainHeight（ReplicaMode 下即快照 groundHeight 双线性上采样到
            //   GroundCells 网格）；_waterCur/_waterNext 的 h ← ReplicaMode 时快照
            //   waterAmount 采样（语义 = 含结合水的水柱量，直接作 h），否则静水面
            //   h = max(SeaLevel − ground, 0)；v/foam=0、湿沙 0 两种模式相同。
            var waterInit = new Vector4[waterCount];
            var groundData = new float[groundCount];
            bool replica = WaterLabReplicaSnapshot.HasData;
            for (int cz = 0; cz < WaterLabRules.GroundCells; cz++)
            {
                for (int cx = 0; cx < WaterLabRules.GroundCells; cx++)
                {
                    float wx = WaterLabRules.GroundCellCenter(cx);
                    float wz = WaterLabRules.GroundCellCenter(cz);
                    groundData[cx + cz * WaterLabRules.GroundCells] = WaterLabRules.TerrainHeight(wx, wz);
                }
            }
            for (int cz = 0; cz < WaterLabRules.WaterCells; cz++)
            {
                for (int cx = 0; cx < WaterLabRules.WaterCells; cx++)
                {
                    float wx = -WaterLabRules.DomainSize * 0.5f + (cx + 0.5f) * WaterLabRules.WaterCellSize;
                    float wz = -WaterLabRules.DomainSize * 0.5f + (cz + 0.5f) * WaterLabRules.WaterCellSize;
                    float h = replica
                        ? WaterLabReplicaSnapshot.SampleWater(wx, wz)
                        : Mathf.Max(WaterLabRules.SeaLevel - WaterLabRules.TerrainHeight(wx, wz), 0f);
                    waterInit[cx + cz * WaterLabRules.WaterCells] = new Vector4(h, 0f, 0f, 0f);
                }
            }
            _waterCur.SetData(waterInit);
            _waterNext.SetData(waterInit);
            _ground.SetData(groundData);
            // 湿沙双缓冲零初始化（ComputeBuffer 内容不保证清零）：首个 WetnessStep 直接读
            // _WetA、读邻均值做扩散，不补零首帧湿沙场是显存垃圾；换位后 _WetNext 轮到当
            // _WetA 读，两份都必须干净，出图才可复现。
            _wetCur.SetData(new float[groundCount]);
            _wetNext.SetData(new float[groundCount]);
            // 粒子池零初始化（寿命 0 = 全死；Update kernel 对 p.w<=0 提前返回，速度槽不会被读到）
            _wwPos.SetData(new Vector4[WaterLabRules.WhitewaterCapacity]);
            // 白水计数/游标零初始化：[0] 每步被 WhitewaterBegin 清零，[1] 是跨步连续的
            // 缓冲环写游标（WhitewaterSpawn 里 InterlockedAdd 自增），不从 0 起则粒子槽
            // 分配顺序随显存残留值漂移，回放不可复现。
            _wwAlive.SetData(new uint[2]);

            // 地形静态纹理（渲染 shader 采样：R=高程编码 [-3,2]→[0,1]，与 OutputField 同式）。
            // 【I7 已知边界】快照地形最高 2.616 m 超出 [−3,2] 量程上端，编码在 2.0 饱和。
            // 影响面只有干沙丘顶：渲染侧 visibleDepth = η−ground 两端同饱和 → 深度 0 →
            // 干区裁剪，水到不了那里，语义无损；水下沙（≤1.29 m）全部在量程内。
            // 量程扩展（如 [−3,3] 的 e*6−3）是 shader 侧改动（DecodeEta 在 Ocean/Sand/
            // Prop/Whitewater 四处成对），归 I1/I2 shader 真值轮同步处理，届时只改这里
            // 的常数与四处解码。
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
            cs.SetFloat(ShaderIds.AdvectionFactor, WaterLabRules.AdvectionFactor);
            cs.SetFloat(ShaderIds.Kappa, WaterLabRules.Kappa);
            // 原作 UpdateAmount：waveAmplitude 每帧按 `saturate(playTime×0.05)×max(四列)` 重算；
            // 潮位目标/5 cm 下限/权重/蒸发全部从 WaterLabRules 单源下发。
            cs.SetFloat(ShaderIds.WaveAmplitude, WaterLabRules.WaveAmplitude(_simTime));
            cs.SetFloat(ShaderIds.TideClampMin, WaterLabRules.TideClampMin);
            cs.SetFloat(ShaderIds.TideMinAmount, WaterLabRules.TideMinAmount);
            cs.SetFloat(ShaderIds.CouplingWeight,
                WaterLabRules.CouplingWeightFull / WaterLabRules.CouplingWeightMax);
            cs.SetFloat(ShaderIds.EvaporationRate, WaterLabRules.EvaporationRate());
            cs.SetFloat(ShaderIds.BeachInfiltrationDepth, WaterLabRules.BeachInfiltrationDepth);
            cs.SetFloat(ShaderIds.BeachInfiltrationRate, WaterLabRules.BeachInfiltrationRate);
            if (!_constantsLogged)
            {
                _constantsLogged = true;
                Debug.LogWarning($"[WaterLab] 常量：cells={WaterLabRules.WaterCells} ground={WaterLabRules.GroundCells} "
                    + $"dx={WaterLabRules.WaterCellSize:F5} dt={dt:F5} g={WaterLabRules.Gravity} "
                    + $"vmax={WaterLabRules.MaxVelocity:F3} damping={WaterLabRules.VelocityDamping} "
                    + $"adv={WaterLabRules.AdvectionFactor} kappa={WaterLabRules.Kappa} "
                    + $"dry={WaterLabRules.DryDepth} sea={WaterLabRules.SeaLevel} "
                    + $"waveMax={WaterLabRules.WaveAmplitudeMax} evap={WaterLabRules.EvaporationRate():E2} "
                    + $"replica={(ReplicaMode ? "开" + WaterLabReplicaSnapshot.Describe() : "关（程序剖面）")}");
            }
            cs.SetFloat(ShaderIds.BreakingSteepness, WaterLabRules.BreakingSteepness);
            cs.SetFloat(ShaderIds.BreakingFroude, WaterLabRules.BreakingFroude);
            cs.SetFloat(ShaderIds.FoamGain, WaterLabRules.FoamGain);
            cs.SetFloat(ShaderIds.FoamDecay, WaterLabRules.FoamDecay);
            cs.SetFloat(ShaderIds.FoamDiffuse, WaterLabRules.FoamDiffuse);
            cs.SetFloat(ShaderIds.WetSoakRate, WaterLabRules.WetnessSoakRate);
            cs.SetFloat(ShaderIds.WetEvaporateRate, WaterLabRules.WetnessEvaporateRate);
            cs.SetFloat(ShaderIds.WetDiffuse, WaterLabRules.WetnessDiffuse);
            cs.SetInt(ShaderIds.SpongeCells, WaterLabRules.SpongeCells);
            cs.SetFloat(ShaderIds.SpongeMaxDamping, WaterLabRules.SpongeMaxDamping);
            cs.SetInt(ShaderIds.WWCapacity, WaterLabRules.WhitewaterCapacity);
            cs.SetFloat(ShaderIds.WWLifetime, WaterLabRules.WhitewaterLifetime);
            cs.SetFloat(ShaderIds.WWSpawnThreshold, WaterLabRules.WhitewaterSpawnThreshold);
            cs.SetFloat(ShaderIds.WWSpawnRate, WaterLabRules.WhitewaterSpawnRate);
            cs.SetFloat(ShaderIds.WWSize, WaterLabRules.WhitewaterSize);
            cs.SetFloat(ShaderIds.WWSurfaceOffset, WaterLabRules.WhitewaterSurfaceOffset);
            cs.SetFloat(ShaderIds.WWMinDepth, WaterLabRules.WhitewaterMinVisibleDepth);
            PushWhitewaterFrustum();
        }

        // 视锥平面缓存（每步复用，避免逐帧分配；Plane→Vector4 隐式转换带 (n.xyz, d)）
        readonly Plane[] _frustumPlanes = new Plane[6];
        readonly Vector4[] _frustumPlaneV4 = new Vector4[6];

        /// <summary>
        /// 白水视锥剔除的相机平面下发（原作 Culling kernel 的相机那一路；
        /// 阴影视锥那一路原作有 drawShadowList/argsShadow，我方白水不投影，无对应物）。
        /// 无相机可解析时下发退化平面（w=+∞ 语义由 overscan 承担）——恒内侧，等于不剔除。
        /// </summary>
        void PushWhitewaterFrustum()
        {
            var cam = renderCamera != null ? renderCamera : Camera.main;
            if (cam == null)
            {
                // 无相机：六面全部退化为「法线 0、距离 +1」，任意点都判为内侧 → 不剔除
                for (int i = 0; i < 6; i++) _frustumPlaneV4[i] = new Vector4(0f, 0f, 0f, 1f);
            }
            else
            {
                GeometryUtility.CalculateFrustumPlanes(cam, _frustumPlanes);
                for (int i = 0; i < 6; i++)
                {
                    var p = _frustumPlanes[i];
                    _frustumPlaneV4[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
                }
            }
            computeShader.SetVectorArray(ShaderIds.WWFrustumPlanes, _frustumPlaneV4);
            computeShader.SetFloat(ShaderIds.WWFrustumOverscan, WaterLabRules.WhitewaterFrustumOverscan);
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
            Shader.SetGlobalFloat(ShaderIds.OceanDebug, ResolveOceanDebug());
            WaterLabWhitewaterBridge.Publish(_wwPos, _wwVel, _wwDraw, _wwArgs);
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
            public static readonly int OceanDebug = Shader.PropertyToID("_WaterLabOceanDebug");

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
            public static readonly int AdvectionFactor = Shader.PropertyToID("_AdvectionFactor");
            public static readonly int Kappa = Shader.PropertyToID("_Kappa");
            public static readonly int WaveAmplitude = Shader.PropertyToID("_WaveAmplitude");
            public static readonly int TideClampMin = Shader.PropertyToID("_TideClampMin");
            public static readonly int TideMinAmount = Shader.PropertyToID("_TideMinAmount");
            public static readonly int CouplingWeight = Shader.PropertyToID("_CouplingWeight");
            public static readonly int EvaporationRate = Shader.PropertyToID("_EvaporationRate");
            public static readonly int BeachInfiltrationDepth = Shader.PropertyToID("_BeachInfiltrationDepth");
            public static readonly int BeachInfiltrationRate = Shader.PropertyToID("_BeachInfiltrationRate");
            public static readonly int BreakingSteepness = Shader.PropertyToID("_BreakingSteepness");
            public static readonly int BreakingFroude = Shader.PropertyToID("_BreakingFroude");
            public static readonly int FoamGain = Shader.PropertyToID("_FoamGain");
            public static readonly int FoamDecay = Shader.PropertyToID("_FoamDecay");
            public static readonly int FoamDiffuse = Shader.PropertyToID("_FoamDiffuse");
            public static readonly int WetSoakRate = Shader.PropertyToID("_WetSoakRate");
            public static readonly int WetEvaporateRate = Shader.PropertyToID("_WetEvaporateRate");
            public static readonly int WetDiffuse = Shader.PropertyToID("_WetDiffuse");
            public static readonly int SpongeCells = Shader.PropertyToID("_SpongeCells");
            public static readonly int SpongeMaxDamping = Shader.PropertyToID("_SpongeMaxDamping");

            public static readonly int WWPos = Shader.PropertyToID("_WWPos");
            public static readonly int WWVel = Shader.PropertyToID("_WWVel");
            public static readonly int WWDraw = Shader.PropertyToID("_WWDraw");
            public static readonly int WWAlive = Shader.PropertyToID("_WWAlive");
            public static readonly int WWArgs = Shader.PropertyToID("_WWArgs");
            public static readonly int WWCapacity = Shader.PropertyToID("_WwCapacity");
            public static readonly int WWLifetime = Shader.PropertyToID("_WwLifetime");
            public static readonly int WWSpawnThreshold = Shader.PropertyToID("_WwSpawnThreshold");
            public static readonly int WWSpawnRate = Shader.PropertyToID("_WwSpawnRate");
            public static readonly int WWSize = Shader.PropertyToID("_WwSize");
            public static readonly int WWSurfaceOffset = Shader.PropertyToID("_WwSurfaceOffset");
            public static readonly int WWMinDepth = Shader.PropertyToID("_WwMinDepth");
            public static readonly int WWFrustumPlanes = Shader.PropertyToID("_WwFrustumPlanes");
            public static readonly int WWFrustumOverscan = Shader.PropertyToID("_WwFrustumOverscan");
        }
    }
}
