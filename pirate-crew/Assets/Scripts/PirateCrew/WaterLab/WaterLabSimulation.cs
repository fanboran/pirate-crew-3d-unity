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

        /// <summary>
        /// 耦合权重场（W1，B2 定案）：MenuWeights512.bytes，512² float32 = 原作 4bit
        /// 权重/15 的运行时实测场（只桨带 row 496..511 有 ramp）。注入且校验通过时
        /// SurfaceUpdate 逐格读真值；缺失或校验失败回退「初始淹没满耦」代理场并报错
        ///（<see cref="WaterLabRules.CouplingWeightAt"/>，仅数据源回退，语义见 Rules 注释）。
        /// </summary>
        public TextAsset replicaWeights;

        /// <summary>
        /// 纯沙场快照（D 流件2）：MenuSand256.bytes，256² float32 = 原作 sandAmount。
        /// 与 groundHeight 快照成对推出障碍场（obstacleDelta = groundHeight − sandAmount
        /// 阈值化，B2 统计锁见 <see cref="WaterLabReplicaObstacles"/>）；两快照齐且校验通过
        /// 时 obstaclesMap 泵启用，否则泵禁用并报错（其余模拟不受影响）。
        /// </summary>
        public TextAsset replicaSandSnapshot;

        /// <summary>
        /// 夜光浮游生物 compute（D 流件4，WaterLabPhytoplankton.compute）。装配器不注入
        /// （自包含创建：Awake 里挂在同一 GameObject）；缺失时浮游生物系统不启动并报错
        /// 一次——主菜单 Night=0 下原作本就不绘制，缺失只影响结构完整度。
        /// </summary>
        public ComputeShader phytoplanktonShader;

        /// <summary>浮游生物系统（Awake 自建，见 <see cref="phytoplanktonShader"/>）。</summary>
        ReplicaPhytoplanktonSystem _phytoSystem;

        /// <summary>主菜单快照复刻是否生效（= 快照已加载，TerrainHeight/初始水位已切数据源）。</summary>
        public bool ReplicaMode => WaterLabReplicaSnapshot.HasData;

        // kernel 句柄
        int _kSurface, _kHeight, _kVelocity, _kAdvectFoam, _kWetness, _kOutput;
        int _kWWBegin, _kWWSpawn, _kWWUpdate, _kWWCompact, _kWWEnd;
        int _kObstacleInit, _kGroundVelocity, _kGroundHeight, _kGroundOutput;

        ComputeBuffer _waterCur, _waterNext; // float4: h,vx,vz,foam × WaterCells²
        ComputeBuffer _wetCur, _wetNext;     // float  × GroundCells²
        ComputeBuffer _ground;               // float  × GroundCells²（静态 t0，潮位/干湿判据用）
        ComputeBuffer _weights;              // float  × WaterCells²（耦合权重/15，静态；B2 真值或回退代理）
        ComputeBuffer _floor;                // float  × WaterCells²（BlurFloor 平滑场，静态）
        ComputeBuffer _wwPos, _wwVel;        // float4 × 容量（白水粒子池，缓冲环）
        ComputeBuffer _wwDraw;               // uint   × 容量（活粒子 draw list）
        ComputeBuffer _wwAlive, _wwArgs;     // uint ×2（计数/游标）、uint ×4（indirect args）
        ComputeBuffer _groundCur, _groundNext; // float4: h,vx,vz,sediment × GroundCells²（活沙，D 流件1）
        ComputeBuffer _obstacleInit;         // float × 256²（障碍增量，ObstacleInit 一次性搬进纹理）
        ComputeBuffer _airCells;             // float4 × AirCells²（大气水汽，恒初值，D 流件3）

        RenderTexture _heightTex;  // WaterCells²（ARGBHalf，见 NewDataTexture）
        RenderTexture _wetTex;     // GroundCells²（ARGBHalf，R 有效）
        RenderTexture _causticTex; // WaterCells² ARGBHalf（R=焦散拉普拉斯，带符号；OutputField 顺带烘焙）
        RenderTexture _groundTex;  // GroundCells² ARGBHalf（R=(h+1)/5 编码；D 流件1 起改由
                                   // GroundOutput 每帧从活沙场写，快照 t0 为初值——原为静态 Texture2D）
        RenderTexture _obstacleTex; // 256² RHalf（障碍增量，Point 采样对齐原作 obstaclesMap）

        float _accumulator;
        float _groundAccumulator;
        float _simTime;

        /// <summary>当前模拟时间（本地时钟；出图门控/诊断锚点用它）。</summary>
        public float SimTime => _simTime;

        /// <summary>
        /// playTime 起始偏移（W1）：下发给 compute 的 `_Time` = 该值 + <see cref="_simTime"/>。
        /// ReplicaMode 下默认锚 B2 同相 dump 组 t0 的 Game.playTime 运行时真值
        ///（<see cref="WaterLabReplicaWeights.PlayTimeOrigin"/> = 622.4888 s）；
        /// <c>PC3D_WATERLAB_PLAYTIME0</c>（秒，≥0）可覆写（对拍不同潮相用）；
        /// 非 Replica 回退 = 0（旧口径，playTime ≡ simTime）。
        ///
        /// 【为什么锚绝对值而不是 mod-2π 相位 φ₀=0.4534 rad】潮位项 amp·sin(playTime)
        /// 只吃相位，但振幅渐起 saturate(playTime×0.05) 要 20 s 才饱和（B2 §6：菜单已
        /// 饱和）——锚 φ₀ 会让振幅跌回 0.045，只有绝对 playTime 同时复现两者。
        ///
        /// 【快进时钟语义核对（B2 §4 漂移 4–7% 的处置）】原作 playTime 逐帧
        /// `+= Time.deltaTime`（模拟时钟），与壁钟的漂移发生在原作壁钟一侧；本仓
        /// TIME_SCALE 快进同样按固定子步累计模拟时钟——两条时间轴同构，漂移项不进入
        /// 我方。对相因此 = 锚初值 + 同倍率累计，无须（也不能）对壁钟。
        /// </summary>
        float _playTimeBase;

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
            _kObstacleInit = computeShader.FindKernel("ObstacleInit");
            _kGroundVelocity = computeShader.FindKernel("GroundVelocity");
            _kGroundHeight = computeShader.FindKernel("GroundHeight");
            _kGroundOutput = computeShader.FindKernel("GroundOutput");

            TryLoadReplicaSnapshot();
            ResolvePlayTimeBase();
            CreateBuffers();
            CreateTextures();
            PublishGlobals();
            BootstrapPhytoplankton();
        }

        /// <summary>
        /// 浮游生物系统自包含创建（D 流件4）：同一 GameObject 挂
        /// <see cref="ReplicaPhytoplanktonSystem"/>，compute 缺失时只报错不启动（原作
        /// 主菜单 Night=0 恒不绘制，缺失不影响可见结果）；水场缓冲每帧由 Update 重绑
        /// （ping-pong 换位后最新场对象会变）。
        /// </summary>
        void BootstrapPhytoplankton()
        {
            if (phytoplanktonShader == null)
            {
                Debug.LogError("[WaterLabSimulation] 浮游生物 compute 未接线（WaterLabPhytoplankton.compute），"
                    + "Phytoplankton 系统未启动（主菜单 Night=0 恒不绘制，不影响可见结果）。");
                return;
            }
            _phytoSystem = gameObject.AddComponent<ReplicaPhytoplanktonSystem>();
            var mat = new Material(Shader.Find("WaterLab/ReplicaPhytoplankton"))
            {
                name = "WaterLabPhytoplankton",
                hideFlags = HideFlags.DontSave,
            };
            var sys = _phytoSystem;
            sys.computeShader = phytoplanktonShader;
            sys.material = mat;
            sys.renderCamera = renderCamera;
            sys.Bootstrap(phytoplanktonShader, _waterCur, renderCamera);
        }

        /// <summary>
        /// playTime 起始偏移解析（在快照加载之后——ReplicaMode 判定要已就位）。
        /// </summary>
        void ResolvePlayTimeBase()
        {
            _playTimeBase = ReplicaMode ? WaterLabReplicaWeights.PlayTimeOrigin : 0f;
            string raw = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_PLAYTIME0");
            if (float.TryParse(raw, out float v) && v >= 0f)
                _playTimeBase = v;
        }

        /// <summary>
        /// 快照接线（在 CreateBuffers 之前——地形/水位/权重初值都要读它）。校验失败
        /// （长度/四角翻转/负水量，见 WaterLabReplicaSnapshot.Load；权重场同）按差分
        /// 门禁精神走「保数值语义」的回退：卸载快照、退回程序剖面并显式报错，不静默
        /// 带病运行。权重场单独 try：地形/水位快照有效而权重缺失时 ReplicaMode 仍开
        ///（回退代理场 + 显式报错），权重不是 ReplicaMode 的判定项。
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
                WaterLabReplicaWeights.Unload();
                Debug.LogError($"[WaterLabSimulation] 主菜单快照加载失败，回退程序剖面：{e.Message}");
                return;
            }
            try
            {
                if (replicaWeights != null)
                {
                    WaterLabReplicaWeights.Load(replicaWeights.bytes);
                    Debug.LogWarning($"[WaterLabSimulation] {WaterLabReplicaWeights.Describe()}");
                }
                else
                {
                    Debug.LogError("[WaterLabSimulation] 耦合权重场资产未接线（MenuWeights512.bytes），"
                        + "SurfaceUpdate 回退「初始淹没满耦」代理场——复刻语义降级，检查装配器注入。");
                }
            }
            catch (System.Exception e)
            {
                WaterLabReplicaWeights.Unload();
                Debug.LogError($"[WaterLabSimulation] 耦合权重场加载失败，回退代理场：{e.Message}");
            }
            try
            {
                if (replicaSandSnapshot != null)
                {
                    WaterLabReplicaObstacles.Load(replicaGroundSnapshot.bytes, replicaSandSnapshot.bytes);
                    Debug.LogWarning($"[WaterLabSimulation] {WaterLabReplicaObstacles.Describe()}");
                }
                else
                {
                    Debug.LogError("[WaterLabSimulation] 纯沙场快照未接线（MenuSand256.bytes），"
                        + "obstaclesMap 泵禁用（D 流件2 数据缺失，其余模拟不受影响）。");
                }
            }
            catch (System.Exception e)
            {
                WaterLabReplicaObstacles.Unload();
                Debug.LogError($"[WaterLabSimulation] 障碍场推导失败，泵禁用：{e.Message}");
            }
        }

        void OnDestroy()
        {
            WaterLabReplicaSnapshot.Unload(); // 静态采样器不跨 Play 会话残留
            WaterLabReplicaWeights.Unload();
            WaterLabReplicaObstacles.Unload();
            _waterCur?.Dispose(); _waterNext?.Dispose();
            _wetCur?.Dispose(); _wetNext?.Dispose();
            _ground?.Dispose();
            _weights?.Dispose(); _floor?.Dispose();
            _wwPos?.Dispose(); _wwVel?.Dispose();
            _wwDraw?.Dispose(); _wwAlive?.Dispose(); _wwArgs?.Dispose();
            _groundCur?.Dispose(); _groundNext?.Dispose();
            _obstacleInit?.Dispose();
            _airCells?.Dispose();
            WaterLabWhitewaterBridge.Clear();
            DestroyTexture(ref _heightTex);
            DestroyTexture(ref _wetTex);
            DestroyTexture(ref _causticTex);
            DestroyTexture(ref _groundTex);
            DestroyTexture(ref _obstacleTex);
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
            {
                UpdateSurface(frameDt);
                StepGround(frameDt);
                // 浮游生物读最新水场（换位后对象已互换，重绑；系统 Update 在本组件之后）
                _phytoSystem?.RebindWater(_waterCur);
            }

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

        /// <summary>
        /// 活沙 Ground 帧调度（D 流件1）：独立子步累加器按 <see cref="WaterLabRules.GroundFixedStep"/>
        /// （0.5·dx/√g ≈ 46.3 ms，平均约 3 帧一步，原作 Ground.Run 同式）步进
        /// GroundVelocity(A→B) → GroundHeight(B→A)；帧末一次 GroundOutput 把活沙高度写
        /// _WaterLabGroundTex（上屏最新场，快照 t0 为初值）。最新活沙场在 A（与水场同款换位约定）。
        /// </summary>
        void StepGround(float frameDt)
        {
            _groundAccumulator += frameDt;
            int maxSteps = Mathf.CeilToInt(2f * TimeScale) + 2;
            int steps = 0;
            while (_groundAccumulator >= WaterLabRules.GroundFixedStep && steps < maxSteps)
            {
                GroundStepOnce();
                _groundAccumulator -= WaterLabRules.GroundFixedStep;
                steps++;
            }
            if (steps == maxSteps)
                _groundAccumulator = 0f; // 追帧雪崩保护（与水子步同款）

            // 帧末发布：哪怕本帧没有子步（46.3 ms 步长 > 帧长时），输出也不花大钱（1024² 写一次）
            PushGroundConstants();
            BindAll();
            int groundGroups = (WaterLabRules.GroundCells + 7) / 8;
            computeShader.Dispatch(_kGroundOutput, groundGroups, groundGroups, 1);
        }

        void GroundStepOnce()
        {
            PushGroundConstants();
            BindAll();
            int groundGroups = (WaterLabRules.GroundCells + 7) / 8;
            computeShader.Dispatch(_kGroundVelocity, groundGroups, groundGroups, 1);
            computeShader.Dispatch(_kGroundHeight, groundGroups, groundGroups, 1);
            (_groundCur, _groundNext) = (_groundNext, _groundCur);
            BindAll();
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
                     _kWWBegin, _kWWSpawn, _kWWUpdate, _kWWCompact, _kWWEnd,
                     _kObstacleInit, _kGroundVelocity, _kGroundHeight, _kGroundOutput })
            {
                computeShader.SetBuffer(k, ShaderIds.WaterA, _waterCur);
                computeShader.SetBuffer(k, ShaderIds.WaterB, _waterNext);
                computeShader.SetBuffer(k, ShaderIds.Ground, _ground);
                computeShader.SetBuffer(k, ShaderIds.WetA, _wetCur);
                computeShader.SetBuffer(k, ShaderIds.WetB, _wetNext);
                computeShader.SetBuffer(k, ShaderIds.CouplingWeights, _weights);
                computeShader.SetBuffer(k, ShaderIds.Floor, _floor);
                computeShader.SetTexture(k, ShaderIds.WetTex, _wetTex);
                computeShader.SetTexture(k, ShaderIds.HeightTex, _heightTex);
                computeShader.SetTexture(k, ShaderIds.CausticTex, _causticTex);
                computeShader.SetBuffer(k, ShaderIds.WWPos, _wwPos);
                computeShader.SetBuffer(k, ShaderIds.WWVel, _wwVel);
                computeShader.SetBuffer(k, ShaderIds.WWDraw, _wwDraw);
                computeShader.SetBuffer(k, ShaderIds.WWAlive, _wwAlive);
                computeShader.SetBuffer(k, ShaderIds.WWArgs, _wwArgs);
                // 活沙（D 流件1/2/3）：GroundOutput 的输出纹理在 CreateBuffers 里逐 kernel
                // 单独绑（RW 纹理绑定不允许进这里的全 buffer 循环也不冲突，统一绑齐防漏）
                computeShader.SetTexture(k, ShaderIds.GroundTex, _groundTex);
                computeShader.SetTexture(k, ShaderIds.ObstacleMap, _obstacleTex);
                computeShader.SetBuffer(k, ShaderIds.GroundA, _groundCur);
                computeShader.SetBuffer(k, ShaderIds.GroundB, _groundNext);
                computeShader.SetBuffer(k, ShaderIds.AirCells, _airCells);
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
            _weights = new ComputeBuffer(waterCount, 4);
            _floor = new ComputeBuffer(waterCount, 4);
            _wwPos = new ComputeBuffer(WaterLabRules.WhitewaterCapacity, 16);
            _wwVel = new ComputeBuffer(WaterLabRules.WhitewaterCapacity, 16);
            _wwDraw = new ComputeBuffer(WaterLabRules.WhitewaterCapacity, 4);
            _wwAlive = new ComputeBuffer(2, 4);
            _wwArgs = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
            _groundCur = new ComputeBuffer(groundCount, 16);
            _groundNext = new ComputeBuffer(groundCount, 16);

            // 初始场（B-terrain §6，裁决 5）：
            //   _ground ← TerrainHeight（ReplicaMode 下即快照 groundHeight 双线性上采样到
            //   GroundCells 网格）；_waterCur/_waterNext 的 h ← ReplicaMode 时快照
            //   waterAmount 采样（语义 = 含结合水的水柱量，直接作 h），否则静水面
            //   h = max(SeaLevel − ground, 0)；v/foam=0、湿沙 0 两种模式相同。
            //   _weights ← W1：ReplicaMode 且权重资产有效 = B2 真值场（桨带 ramp，其余 0）；
            //   否则「初始淹没满耦」回退代理场（Rules.CouplingWeightAt，已退役语义见该函数）。
            //   _floor ← 潮位目标的 BlurFloor 项：水格格心采地形后 3 次 4 邻盒式模糊
            //   （Rules.BlurFloor 同式；静态场一次预计算，SurfaceUpdate 直接读）。
            var waterInit = new Vector4[waterCount];
            var groundData = new float[groundCount];
            var weightsData = new float[waterCount];
            var floorData = new float[waterCount];
            bool replica = WaterLabReplicaSnapshot.HasData;
            bool weightsTruth = WaterLabReplicaWeights.HasData;
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
                    int i = cx + cz * WaterLabRules.WaterCells;
                    float wx = -WaterLabRules.DomainSize * 0.5f + (cx + 0.5f) * WaterLabRules.WaterCellSize;
                    float wz = -WaterLabRules.DomainSize * 0.5f + (cz + 0.5f) * WaterLabRules.WaterCellSize;
                    float g = WaterLabRules.TerrainHeight(wx, wz);
                    float h = replica
                        ? WaterLabReplicaSnapshot.SampleWater(wx, wz)
                        : Mathf.Max(WaterLabRules.SeaLevel - g, 0f);
                    waterInit[i] = new Vector4(h, 0f, 0f, 0f);
                    floorData[i] = g;
                    weightsData[i] = weightsTruth
                        ? WaterLabReplicaWeights.Weights[i]
                        : WaterLabRules.CouplingWeightAt(g) / WaterLabRules.CouplingWeightMax;
                }
            }
            WaterLabRules.BlurFloor(floorData, WaterLabRules.WaterCells, WaterLabRules.FloorBlurIterations);
            _waterCur.SetData(waterInit);
            _waterNext.SetData(waterInit);
            _ground.SetData(groundData);
            _weights.SetData(weightsData);
            _floor.SetData(floorData);
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

            // ---- 活沙场（D 流件1）：t0 = TerrainHeight（ReplicaMode 即 groundHeight 快照，
            // 含烘焙障碍——渲染语义，裁决②），vx/vz/sediment 零起；双缓冲两份同值。
            var groundCur = new Vector4[groundCount];
            var groundNext = new Vector4[groundCount];
            for (int i = 0; i < groundCount; i++)
            {
                groundCur[i] = new Vector4(groundData[i], 0f, 0f, 0f);
                groundNext[i] = groundCur[i];
            }
            _groundCur.SetData(groundCur);
            _groundNext.SetData(groundNext);

            // ---- 障碍场（D 流件2）：groundHeight − sandAmount 阈值化（B2 统计锁），
            // ObstacleInit kernel 一次性搬进 _obstacleTex（point 采样）。
            bool obstaclesReady = WaterLabReplicaObstacles.HasData;
            var obstacleData = new float[WaterLabReplicaObstacles.Resolution * WaterLabReplicaObstacles.Resolution];
            if (obstaclesReady)
                System.Buffer.BlockCopy(WaterLabReplicaObstacles.Delta, 0, obstacleData, 0, obstacleData.Length * 4);
            _obstacleInit = new ComputeBuffer(obstacleData.Length, 4);
            _obstacleInit.SetData(obstacleData);

            // ---- Air 大气场（D 流件3）：Air Initialization 恒初值（水汽 0；原作 Air 层
            // 只 Init 不步进，Air.Run 为空——初值即全解）。
            int airCount = WaterLabRules.AirCells * WaterLabRules.AirCells;
            var airInit = new Vector4[airCount];
            for (int i = 0; i < airCount; i++)
                airInit[i] = new Vector4(WaterLabRules.AirHumidity, 0f, 0f, 0f);
            _airCells = new ComputeBuffer(airCount, 16);
            _airCells.SetData(airInit);

            // 地形输出纹理（渲染 shader 采样：R=高程编码 (h+1)/5，与 OutputField 同式）。
            // 【D 流件1】从静态 Texture2D 改 RenderTexture：每帧 GroundOutput 从活沙场写
            // （快照 t0 为初值、活沙演化后上屏）。渲染侧解码 DecodeGround(e)=e×5−1 不动。
            // 【I7 已知边界】快照地形最高 2.616 m 超出 [−1,4] 编码量程上端的 4.0，编码在
            // 4.0 饱和。影响面只有干沙丘顶（saturate 截断）：渲染侧 visibleDepth = η−ground
            // 两端同饱和 → 深度 0 → 干区裁剪，水到不了那里，语义无损；水下沙全部在量程内。
            _groundTex = NewDataTexture(WaterLabRules.GroundCells, "WaterLabGround");
            Shader.SetGlobalTexture(ShaderIds.GroundTex, _groundTex);

            // 障碍纹理：一次性初始化 dispatch（obstaclesReady 才有意义；缺数据时保持全零，
            // 泵的 delta>阈值 门控恒假 = 泵禁用）。
            var obstacleDesc = new RenderTextureDescriptor(
                WaterLabReplicaObstacles.Resolution, WaterLabReplicaObstacles.Resolution,
                RenderTextureFormat.RHalf, 0)
            {
                enableRandomWrite = true,
                sRGB = false,
            };
            _obstacleTex = new RenderTexture(obstacleDesc)
            {
                filterMode = FilterMode.Point, // 原作 obstaclesMap 为 point 采样（§9）
                wrapMode = TextureWrapMode.Clamp,
                name = "WaterLabObstacle",
            };
            _obstacleTex.Create();
            computeShader.SetTexture(_kObstacleInit, ShaderIds.ObstacleMap, _obstacleTex);
            computeShader.SetBuffer(_kObstacleInit, ShaderIds.ObstacleInit, _obstacleInit);
            computeShader.Dispatch(_kObstacleInit,
                (WaterLabReplicaObstacles.Resolution + 7) / 8,
                (WaterLabReplicaObstacles.Resolution + 7) / 8, 1);
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
            // _Time = playTime（原作 Game.playTime 模拟时钟）：ReplicaMode 锚 B2 真值偏移，
            // 快进同倍率累计（推导见 _playTimeBase 注释）。
            cs.SetFloat(ShaderIds.Time, _playTimeBase + _simTime);
            cs.SetFloat(ShaderIds.SeaLevel, WaterLabRules.SeaLevel);
            cs.SetFloat(ShaderIds.AdvectionFactor, WaterLabRules.AdvectionFactor);
            cs.SetFloat(ShaderIds.Kappa, WaterLabRules.Kappa);
            // 原作 UpdateAmount：waveAmplitude 每帧按 `saturate(playTime×0.05)×max(四列)` 重算；
            // 潮位目标/5 cm 下限/蒸发从 WaterLabRules 单源下发。耦合权重不再走标量 uniform——
            // W1 起逐格读 _CouplingWeights 静态缓冲（B2 真值场/回退代理场，CreateBuffers 灌）。
            cs.SetFloat(ShaderIds.WaveAmplitude, WaterLabRules.WaveAmplitude(_playTimeBase + _simTime));
            cs.SetFloat(ShaderIds.TideClampMin, WaterLabRules.TideClampMin);
            cs.SetFloat(ShaderIds.TideMinAmount, WaterLabRules.TideMinAmount);
            PushGroundConstants();
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
                    + $"playTime0={_playTimeBase:F4} {WaterLabReplicaWeights.Describe()} "
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
        /// 活沙/泵/Air 常量下发（D 流件1/2/3）：13 个沉积参数（D-sand.md §4.2 Carbonate
        /// 真值）+ 泵公式 + GroundFixedStep + 障碍阈值 + 蒸发基率（湿度因子在 kernel 内
        /// 逐格 exp2——Air 场驱动，X 流修正语义）。
        /// </summary>
        void PushGroundConstants()
        {
            var cs = computeShader;
            cs.SetFloat(ShaderIds.GroundDt, WaterLabRules.GroundFixedStep);
            cs.SetFloat(ShaderIds.GroundMaxVel, WaterLabRules.GroundMaxVelocity);
            cs.SetFloat(ShaderIds.SandDamping, WaterLabRules.SandDamping);
            cs.SetFloat(ShaderIds.SandAmountKappa, WaterLabRules.SandAmountKappa);
            cs.SetFloat(ShaderIds.SandVelocityKappa, WaterLabRules.SandVelocityKappa);
            cs.SetFloat(ShaderIds.SandErosionWet, WaterLabRules.SandWaterErosionFactor);
            cs.SetFloat(ShaderIds.SandErosionGamma, WaterLabRules.SandWaterErosionAmountGamma);
            cs.SetFloat(ShaderIds.SandCapacity, WaterLabRules.SandSedimentCapacity);
            cs.SetFloat(ShaderIds.SandSpeedGamma, WaterLabRules.SandSedimentWaterSpeedGamma);
            cs.SetFloat(ShaderIds.SandAmountGamma, WaterLabRules.SandSedimentWaterAmountGamma);
            cs.SetFloat(ShaderIds.SandSuspension, WaterLabRules.SandSedimentSuspension);
            cs.SetFloat(ShaderIds.SandDeposition, WaterLabRules.SandSedimentDeposition);
            cs.SetFloat(ShaderIds.SandAdvection, WaterLabRules.SandSedimentAdvection);
            cs.SetFloat(ShaderIds.SandPumpAmp, WaterLabRules.PumpAmplitude);
            cs.SetFloat(ShaderIds.SandPumpFreq, WaterLabRules.PumpTimeFrequency);
            cs.SetFloat(ShaderIds.SandPumpPhaseV, WaterLabRules.PumpPhasePerV);
            cs.SetFloat(ShaderIds.SandPumpWindow, WaterLabRules.PumpEtaWindow);
            cs.SetFloat(ShaderIds.ObstacleThreshold, WaterLabReplicaObstacles.DeltaThreshold);
            cs.SetFloat(ShaderIds.EvaporationBase, WaterLabRules.EvaporationBaseRate());
            cs.SetInt(ShaderIds.AirCellsCount, WaterLabRules.AirCells);
        }

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
            public static readonly int CouplingWeights = Shader.PropertyToID("_CouplingWeights");
            public static readonly int Floor = Shader.PropertyToID("_Floor");
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

            // ---- 活沙 Ground / 泵 / Air（D 流件1/2/3）----
            public static readonly int GroundA = Shader.PropertyToID("_GroundA");
            public static readonly int GroundB = Shader.PropertyToID("_GroundB");
            public static readonly int ObstacleMap = Shader.PropertyToID("_ObstacleMap");
            public static readonly int ObstacleInit = Shader.PropertyToID("_ObstacleInit");
            public static readonly int AirCells = Shader.PropertyToID("_AirCells");
            public static readonly int GroundDt = Shader.PropertyToID("_GroundDt");
            public static readonly int GroundMaxVel = Shader.PropertyToID("_GroundMaxVel");
            public static readonly int SandDamping = Shader.PropertyToID("_SandDamping");
            public static readonly int SandAmountKappa = Shader.PropertyToID("_SandAmountKappa");
            public static readonly int SandVelocityKappa = Shader.PropertyToID("_SandVelocityKappa");
            public static readonly int SandErosionWet = Shader.PropertyToID("_SandErosionWet");
            public static readonly int SandErosionGamma = Shader.PropertyToID("_SandErosionGamma");
            public static readonly int SandCapacity = Shader.PropertyToID("_SandCapacity");
            public static readonly int SandSpeedGamma = Shader.PropertyToID("_SandSpeedGamma");
            public static readonly int SandAmountGamma = Shader.PropertyToID("_SandAmountGamma");
            public static readonly int SandSuspension = Shader.PropertyToID("_SandSuspension");
            public static readonly int SandDeposition = Shader.PropertyToID("_SandDeposition");
            public static readonly int SandAdvection = Shader.PropertyToID("_SandAdvection");
            public static readonly int SandPumpAmp = Shader.PropertyToID("_SandPumpAmp");
            public static readonly int SandPumpFreq = Shader.PropertyToID("_SandPumpFreq");
            public static readonly int SandPumpPhaseV = Shader.PropertyToID("_SandPumpPhaseV");
            public static readonly int SandPumpWindow = Shader.PropertyToID("_SandPumpWindow");
            public static readonly int ObstacleThreshold = Shader.PropertyToID("_ObstacleThreshold");
            public static readonly int EvaporationBase = Shader.PropertyToID("_EvaporationBase");
            public static readonly int AirCellsCount = Shader.PropertyToID("_AirCellsCount");
        }
    }
}
