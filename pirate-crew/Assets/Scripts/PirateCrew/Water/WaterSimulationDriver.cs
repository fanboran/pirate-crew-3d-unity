using PirateCrew.Core;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 二维波动方程水面的运行驱动：持有 <see cref="WaterWaveField2D"/>、每帧固定步长推进、
    /// 把高度/法线/泡沫打包进一张 128×128 的 <see cref="Texture2D"/> 上传给水面 shader，
    /// 并注入三类扰动（域边缘涌浪 / 爆炸事件 / 落水事件）。
    ///
    /// 【域契约：驱动常驻、旧水面渲染定向退役】本驱动是海洋 shader 高度场全局变量
    /// （<c>_WaterHeightField/_WaterObstacleMap/_WaterSimOrigin/_WaterSimEnabled</c>）的唯一发布者，
    /// 因此**必须常驻运行**：物体失活会停掉 Update 并触发 <see cref="OnDisable"/> 把
    /// <c>_WaterSimEnabled</c> 置 0，涟漪/泡沫累积/障碍绕射路径随之整体静默失效。
    /// 退役同物体上的旧水面（MeshRenderer + <see cref="WaterTessellator"/>）只能做**组件级禁用**，
    /// 不能对 GameObject SetActive(false)；世界地图装配期由 <see cref="BattleController"/>
    /// 调 <see cref="ConfigureWorldDomain"/> 把模拟域搬到图心（默认域由关卡目录推出，
    /// 只对旧竞技场成立）。
    ///
    /// 【分工，避免双重计高】
    ///   · 宏观形状（浪的几何轮廓、菲涅尔/镜面的低频倾斜）：<c>PirateWater.shader</c> 的
    ///     Gerstner 顶点位移 + 解析法线，**不经过本模拟**；
    ///   · 局部扰动（反射、绕射、爆炸涟漪）与泡沫累积：本模拟的高度场，
    ///     shader 只把它当**法线扰动 + 泡沫源**用，**不做顶点位移**。
    ///   两条路径叠加的是"不同频段"，不是同一高度加两遍。
    ///
    /// 【只驱动观感】模拟结果不参与任何玩法判定（落水仍是 <c>LevelGeometry.WaterWorldY</c> 标量阈值；
    /// AI 预演不读这里）。总开关 <see cref="enableSimulation"/> 关掉后 shader 走纯 Gerstner + 深度泡沫。
    ///
    /// 【接线】挂到场景里即可（建议挂在 <c>Water</c> 物体上）。全局 shader 变量由本驱动设置；
    /// 驱动不存在时这些全局为 0/未绑定 → shader 自动跳过高度场项（有 <c>_WaterSimEnabled</c> 兜底）。
    ///
    /// 【无 GC】RGB32 像素缓冲与障碍图缓冲都在 <c>Awake</c> 预分配，运行时只做复用写入 +
    /// <c>SetPixels32 + Apply(false)</c>，每帧 0 次托管分配。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterSimulationDriver : MonoBehaviour
    {
        /// <summary>全局开关（0/1），shader 用 <c>_WaterSimEnabled</c> 读取。</summary>
        public const string GlobalEnabled = "_WaterSimEnabled";

        /// <summary>高度场纹理（R=高度 G=法线x B=法线z A=泡沫）。</summary>
        public const string GlobalHeightField = "_WaterHeightField";

        /// <summary>烘焙的障碍图（R=障碍）。</summary>
        public const string GlobalObstacleMap = "_WaterObstacleMap";

        /// <summary>(中心X, 中心Z, 域边长, 保留)。</summary>
        public const string GlobalOrigin = "_WaterSimOrigin";

        /// <summary>
        /// 太阳方向（世界空间，**从水面指向光源** L = -sun.forward，xyz，w=0）。
        /// 供水面 shader 的镜面光路用；语义与 URP <c>GetMainLight().direction</c> 同向（不是光传播方向）。
        /// </summary>
        public const string GlobalSunDir = "_WaterSunDir";

        /// <summary>驱动单例（供 <see cref="InjectSplash"/> 使用；可为 null）。</summary>
        public static WaterSimulationDriver Instance { get; private set; }

        [Header("总开关")]
        [Tooltip("关掉 = 回到纯 Gerstner + 深度泡沫，不跑模拟、不上传纹理。")]
        [SerializeField] bool enableSimulation = true;

        [Header("模拟域（世界 XZ，正方形，以竞技场中心为中心）")]
        [Tooltip("域边长（世界单位）。128 覆盖 100×34 竞技场与两侧海床台阶（格 1→2 单位 ×2）。")]
        [SerializeField] float domainSize = WaterSimRules.DefaultDomainSize;

        [Tooltip("每轴格数。128² ≈ 1.6 万格，C# 每步约 0.4ms（Release 实测）。dx = 域边长/本值 = 1.0 世界单位 = 半格（格数不动，格距随域 ×2）。")]
        [SerializeField] int cellsPerAxis = WaterSimRules.DefaultCellsPerAxis;

        [Tooltip("用于取竞技场中心的关卡号（域中心 = W/2, D/2）。改关卡时要与 BattleController 一致。")]
        [SerializeField] int domainLevelNumber = 1;

        [Tooltip("勾上则忽略上面的关卡号，直接用下面的自定义中心。")]
        [SerializeField] bool useCustomDomainCenter = false;

        [Tooltip("自定义域中心（世界 XZ）。")]
        [SerializeField] Vector2 customDomainCenter = new Vector2(50f, 17f);

        [Tooltip("波速（世界单位/秒）。CFL 上限会自动约束 dt。格 1→2 单位后 ×2（9→18），保持每格每秒的行进观感与 CFL 余量。")]
        [SerializeField] float waveSpeed = 18f;

        [Tooltip("模拟固定步长（秒）。1/60 时 CFL = 0.30，余量 39%。")]
        [SerializeField] float fixedStep = 1f / 60f;

        [Header("域边缘涌浪源（克制起见默认很轻）")]
        [SerializeField] bool enableEdgeSwell = true;
        [Tooltip("涌浪振幅（世界单位）。默认 0.04（格 1→2 单位 ×2）。")]
        [SerializeField] float edgeSwellAmplitude = 0.04f;
        [Tooltip("涌浪周期（秒）。默认 6s，慢涌。")]
        [SerializeField] float edgeSwellPeriod = 6f;
        [Tooltip("注入边：0=−Z 1=+Z 2=−X 3=+X。")]
        [SerializeField, Range(0, 3)] int edgeSwellEdge = 0;

        [Header("事件注入")]
        [Tooltip("爆炸涟漪半径（世界单位）。格 1→2 单位 ×2（3.5→7）。")]
        [SerializeField] float splashRadiusWorld = 7f;
        [Tooltip("爆炸涟漪峰值高度（世界单位）。格 1→2 单位 ×2（0.06→0.12）。")]
        [SerializeField] float splashAmplitude = 0.12f;
        [Tooltip("落水/爆炸同时在水面留下的泡沫量（0-1）。")]
        [SerializeField, Range(0f, 1f)] float splashFoam = 0.35f;

        [Header("纹理编码")]
        [Tooltip("高度编码满量程（世界单位）：±该值映射到 [0,1]。格 1→2 单位 ×2（0.25→0.5，编码对比度口径不变）。")]
        [SerializeField] float heightEncodeScale = 0.5f;

        [Header("烘焙资产（Editor/WaterAssetBuilder 生成，可为空=不做障碍反射）")]
        [SerializeField] Texture2D obstacleMap;

        WaterWaveField2D _field;
        Texture2D _heightTexture;
        Color32[] _pixels;
        bool[] _obstacleMask;
        float _accumulator;
        float _simTime;
        Texture2D _fallbackObstacle;
        Light _sunLight;
        bool _subscribed;

        /// <summary>模拟是否在运行（供测试/报告读取）。</summary>
        public bool IsRunning => enableSimulation && _field != null;

        /// <summary>当前模拟域边长（世界单位）。</summary>
        public float DomainSize => domainSize;

        /// <summary>模拟域中心 XZ（世界坐标）。Awake 按关卡/自定义中心推出；世界地图模式由
        /// <see cref="ConfigureWorldDomain"/> 显式指定为图心。</summary>
        public Vector2 DomainCenter { get; private set; }

        void Awake()
        {
            Instance = this;
            DomainCenter = ResolveDomainCenter();

            var cfg = WaterFieldConfig.Default;
            cfg.CellsX = Mathf.Max(8, cellsPerAxis);
            cfg.CellsZ = Mathf.Max(8, cellsPerAxis);
            cfg.Dx = domainSize / cfg.CellsX;
            cfg.WaveSpeed = Mathf.Max(waveSpeed, 0.1f);

            _field = new WaterWaveField2D(cfg);

            // 单步过长时自动夹到 CFL 上限，避免用户把波速调爆。
            fixedStep = Mathf.Clamp(fixedStep, 1f / 240f, _field.MaxStableDt);

            BuildObstacleMask();
            _field.SetObstacleFromMask(_obstacleMask);

            _heightTexture = new Texture2D(cfg.CellsX, cfg.CellsZ, TextureFormat.RGBA32, false, true)
            {
                name = "WaterHeightField_RT",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            _pixels = new Color32[cfg.CellsX * cfg.CellsZ];

            PublishGlobals();
            UploadTexture();
            BattleEventsSubscribe();
        }

        /// <summary>
        /// 把模拟域重配到世界地图（场景装配期调用一次，无每帧开销）：域心 = 图心 <paramref name="center"/>，
        /// 域边长 = <see cref="WaterSimRules.WorldDomainSizeForSpan"/>（随跨度伸缩，clamp [128, 256]）。
        /// 随后安全重建：新 <see cref="WaterFieldConfig"/>（Dx = 域边长/格数）→ 重建波动场与障碍掩码 →
        /// 重建像素缓冲/高度纹理 → 重发布全部全局变量（<see cref="PublishGlobals"/> 尾部已含
        /// <see cref="PublishOrigin"/>）→ 立即 <see cref="UploadTexture"/> 填一帧有效像素。
        /// 事件注入（<see cref="InjectSplashInternal"/> 的 uv 判定）读的正是本类的 center/size，随新域自动生效。
        ///
        /// 【为什么需要】Awake 推出的默认域由关卡目录/自定义中心决定（旧竞技场口径），
        /// 与世界地图 150–260u 的跨度对不上；装配方在世界地图模式调用本方法完成搬迁。
        /// 防御分支：Awake 尚未执行时只把新值落进序列化字段（自定义中心绕开关卡推导），初始化交给 Awake。
        /// </summary>
        public void ConfigureWorldDomain(Vector2 center, float spanUnits)
        {
            float newSize = WaterSimRules.WorldDomainSizeForSpan(spanUnits);

            if (_field == null)
            {
                domainSize = newSize;
                useCustomDomainCenter = true;
                customDomainCenter = center;
                return;
            }

            domainSize = newSize;
            DomainCenter = center;

            // 重建波动场：纯 C# 分配，装配期一次性（旧场无原生资源，交给 GC）。
            var cfg = WaterFieldConfig.Default;
            cfg.CellsX = Mathf.Max(8, cellsPerAxis);
            cfg.CellsZ = Mathf.Max(8, cellsPerAxis);
            cfg.Dx = domainSize / cfg.CellsX;
            cfg.WaveSpeed = Mathf.Max(waveSpeed, 0.1f);

            _field = new WaterWaveField2D(cfg);

            // 单步过长时自动夹到 CFL 上限（与 Awake 同口径；域扩大只会放宽该上限）。
            fixedStep = Mathf.Clamp(fixedStep, 1f / 240f, _field.MaxStableDt);

            BuildObstacleMask();
            _field.SetObstacleFromMask(_obstacleMask);

            // 像素缓冲随格数重建；纹理尺寸不变（格数不动）时复用，变了才重建。
            _pixels = new Color32[cfg.CellsX * cfg.CellsZ];
            if (_heightTexture == null || _heightTexture.width != cfg.CellsX
                || _heightTexture.height != cfg.CellsZ)
            {
                if (_heightTexture != null)
                    Destroy(_heightTexture);
                _heightTexture = new Texture2D(cfg.CellsX, cfg.CellsZ, TextureFormat.RGBA32, false, true)
                {
                    name = "WaterHeightField_RT",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 0,
                };
            }

            // 旧域的步长累积与涌浪相位对新城无意义，清零让涌浪从新边界重新起波。
            _accumulator = 0f;
            _simTime = 0f;

            UploadTexture();
            PublishGlobals();
        }

        void OnDisable()
        {
            BattleEventsUnsubscribe();
            if (Instance == this)
            {
                // 组件被禁用/物体失活后 Update 不再跑：显式关掉全局，避免 shader 采到冻结的旧纹理。
                Shader.SetGlobalFloat(GlobalEnabled, 0f);
                Instance = null;
            }
        }

        void OnDestroy()
        {
            BattleEventsUnsubscribe();
            if (Instance == this)
                Instance = null;
            if (Shader.GetGlobalFloat(GlobalEnabled) != 0f)
                Shader.SetGlobalFloat(GlobalEnabled, 0f);
        }

        /// <summary>模拟域中心：竞技场中心（W/2, D/2）。拿不到关卡数据时退回自定义值。</summary>
        Vector2 ResolveDomainCenter()
        {
            if (useCustomDomainCenter)
                return customDomainCenter;

            LevelData level = LevelCatalog.Get(domainLevelNumber);
            if (level.WidthTiles > 0 && level.HeightTiles > 0)
                // 竞技场中心 = 格数 × TileWorldSize / 2（格 1→2 单位后 W/2 → W·1）。
                return new Vector2(LevelGeometry.TileToWorld(level.WidthTiles * 0.5f),
                    LevelGeometry.TileToWorld(level.HeightTiles * 0.5f));

            return customDomainCenter;
        }

        void Update()
        {
            PublishSunDirection();

            if (!enableSimulation || _field == null)
            {
                Shader.SetGlobalFloat(GlobalEnabled, 0f);
                return;
            }

            Shader.SetGlobalFloat(GlobalEnabled, 1f);

            _accumulator += Time.deltaTime;
            // 最多补 4 步，防止卡顿后追帧雪崩。
            int steps = 0;
            while (_accumulator >= fixedStep && steps < 4)
            {
                _accumulator -= fixedStep;
                _simTime += fixedStep;
                steps++;

                if (enableEdgeSwell && edgeSwellPeriod > 1e-3f)
                {
                    float phase = Mathf.PI * 2f * _simTime / edgeSwellPeriod;
                    _field.InjectEdgeSwell(edgeSwellEdge, edgeSwellAmplitude, phase);
                }

                _field.Step(fixedStep);
            }

            if (steps > 0)
            {
                UploadTexture();
                PublishOrigin();
            }
        }

        // ------------------------------------------------------------------
        // 纹理上传（无 GC：复用 _pixels / _heightTexture）
        // ------------------------------------------------------------------

        void UploadTexture()
        {
            int cxCount = _field.CellsX;
            int czCount = _field.CellsZ;
            float invScale = 1f / Mathf.Max(heightEncodeScale, 1e-3f);
            float invTwoDx = 0.5f / Mathf.Max(_field.Dx, 1e-4f);

            for (int cz = 0; cz < czCount; cz++)
            {
                for (int cx = 0; cx < cxCount; cx++)
                {
                    int idx = cz * cxCount + cx;

                    if (_field.IsObstacle(cx, cz))
                    {
                        // 障碍格：高度 0.5、法线向上、无泡沫。
                        _pixels[idx] = new Color32(128, 128, 128, 0);
                        continue;
                    }

                    float h = _field.HeightAtCell(cx, cz);

                    // 法线：中心差分（边缘单边），n = normalize((−∂h/∂x, 1, −∂h/∂z))。
                    int xm = cx > 0 ? cx - 1 : cx;
                    int xp = cx < cxCount - 1 ? cx + 1 : cx;
                    int zm = cz > 0 ? cz - 1 : cz;
                    int zp = cz < czCount - 1 ? cz + 1 : cz;
                    float dhdx = (_field.HeightAtCell(xp, cz) - _field.HeightAtCell(xm, cz)) * invTwoDx;
                    float dhdz = (_field.HeightAtCell(cx, zp) - _field.HeightAtCell(cx, zm)) * invTwoDx;
                    Vector3 n = new Vector3(-dhdx, 1f, -dhdz).normalized;

                    float foam = Mathf.Clamp01(_field.FoamAtCell(cx, cz));

                    _pixels[idx] = new Color32(
                        EncodeSigned(h * invScale),
                        EncodeSigned(n.x),
                        EncodeSigned(n.z),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(foam * 255f), 0, 255));
                }
            }

            _heightTexture.SetPixels32(_pixels);
            _heightTexture.Apply(false, false);
        }

        static byte EncodeSigned(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt((Mathf.Clamp(v, -1f, 1f) * 0.5f + 0.5f) * 255f), 0, 255);
        }

        void PublishGlobals()
        {
            Shader.SetGlobalTexture(GlobalHeightField, _heightTexture);

            if (obstacleMap != null)
            {
                Shader.SetGlobalTexture(GlobalObstacleMap, obstacleMap);
            }
            else
            {
                if (_fallbackObstacle == null)
                {
                    _fallbackObstacle = new Texture2D(1, 1, TextureFormat.R8, false, true) { name = "WaterObstacleNull" };
                    _fallbackObstacle.SetPixel(0, 0, Color.black);
                    _fallbackObstacle.Apply(false, false);
                }
                Shader.SetGlobalTexture(GlobalObstacleMap, _fallbackObstacle);
            }

            Shader.SetGlobalFloat(GlobalEnabled, enableSimulation ? 1f : 0f);
            PublishSunDirection();
            PublishOrigin();
        }

        void PublishOrigin()
        {
            // w = 每轴格数（shader 用它算纹理 texel 做曲率 2 阶差分）。
            Shader.SetGlobalVector(GlobalOrigin,
                new Vector4(DomainCenter.x, DomainCenter.y, domainSize, _field != null ? _field.CellsX : 0f));
        }

        /// <summary>
        /// 把太阳方向发布成全局 uniform（供 <c>PirateWater.shader</c> 的太阳光路用）。
        /// 语义 = **从水面指向光源**的世界方向 L = <c>-sun.transform.forward</c>，
        /// 与 URP 主光 <c>GetMainLight().direction</c>（= <c>_MainLightPosition.xyz</c> = <c>-light.forward</c>）同向，
        /// shader 里直接用它的 dot 做镜面，不能再取负（r3 的符号错误正是"画面里完全没有光路"的元凶之一）。
        ///
        /// 【为什么 sun 需要专门传】水面是 Transparent、SRP Batcher 走的材质 uniform 里没有太阳方向；
        /// 而 <c>GetMainLight()</c> 在 ForwardLit 里可用，但场景主光由 <see cref="RenderSettings.sun"/>
        /// 权威给出，这里显式对齐实际主光姿态。
        ///
        /// 【为什么不能只发静态正午姿态】本场景的 <c>Battle.unity</c> 没有把主光登记为 sun
        /// （<c>m_Sun: {fileID: 0}</c>），但运行时的 <c>AmbientDirector</c> 会旋转它指向的光源
        /// （昼夜档位）。若 sun 为空就永远发固定 Euler(48,140) 的 L，切到其它时段后水面光路方向
        /// 会与真实主光相反/错位。故解析顺序为
        /// <c>RenderSettings.sun</c> → 场景里最亮的启用平行光（缓存）→ 静态正午兜底。
        /// </summary>
        void PublishSunDirection()
        {
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : _sunLight;
            if (sun == null)
            {
                // 缓存失效（光源被销毁/换场景）时重解析一次。
                _sunLight = FindBrightestDirectionalLight();
                sun = _sunLight;
            }

            Vector3 toLight = sun != null ? -sun.transform.forward : FallbackSunToLight;
            Shader.SetGlobalVector(GlobalSunDir, new Vector4(toLight.x, toLight.y, toLight.z, 0f));
        }

        /// <summary>
        /// 找场景里最亮的启用平行光（sun 未登记时的兜底；只在缓存失效时调用，非每帧）。
        /// 只按"平行光 + 启用 + 强度最高"挑选，不跨模块引用 AmbientDirector，保持水体模块独立。
        /// </summary>
        static Light FindBrightestDirectionalLight()
        {
            Light[] lights = Object.FindObjectsOfType<Light>();
            Light best = null;
            float bestIntensity = -1f;
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null || l.type != LightType.Directional || !l.enabled)
                    continue;
                if (l.intensity > bestIntensity)
                {
                    bestIntensity = l.intensity;
                    best = l;
                }
            }
            return best;
        }

        /// <summary>
        /// sun 未接线且找不到平行光时的兜底「指向光源」方向：由主光固定姿态 <c>Euler(48,140,0)</c> 反推
        /// （<c>Vector3.back</c> 旋转后即指向光源的 L ≈ (-0.43,0.74,0.51)）。静态只算一次。
        /// </summary>
        static readonly Vector3 FallbackSunToLight = Quaternion.Euler(48f, 140f, 0f) * Vector3.back;

        // ------------------------------------------------------------------
        // 障碍图（烘焙资产 → 模拟格）
        // ------------------------------------------------------------------

        void BuildObstacleMask()
        {
            int cxCount = _field.CellsX;
            int czCount = _field.CellsZ;
            _obstacleMask = new bool[cxCount * czCount];

            if (obstacleMap == null)
                return;

            Color32[] src;
            try
            {
                src = obstacleMap.GetPixels32();
            }
            catch (System.Exception e)
            {
                global::PirateCrew.Core.Log.Warn("[WaterSimulationDriver] 障碍图不可读（TextureImporter 需 Is Readable）："
                                 + e.Message + "，本次模拟不做障碍反射。");
                return;
            }

            int sw = obstacleMap.width;
            int sh = obstacleMap.height;
            if (sw <= 0 || sh <= 0)
                return;

            for (int cz = 0; cz < czCount; cz++)
            {
                for (int cx = 0; cx < cxCount; cx++)
                {
                    // 最近邻：格心 uv → 障碍图像素
                    float u = (cx + 0.5f) / cxCount;
                    float v = (cz + 0.5f) / czCount;
                    int px = Mathf.Clamp((int)(u * sw), 0, sw - 1);
                    int pz = Mathf.Clamp((int)(v * sh), 0, sh - 1);
                    _obstacleMask[cz * cxCount + cx] = src[pz * sw + px].r > 127;
                }
            }
        }

        // ------------------------------------------------------------------
        // 事件注入
        // ------------------------------------------------------------------

        void BattleEventsSubscribe()
        {
            if (_subscribed)
                return;
            EventBus.Subscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            _subscribed = true;
        }

        void BattleEventsUnsubscribe()
        {
            if (!_subscribed)
                return;
            EventBus.Unsubscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            _subscribed = false;
        }

        void OnProjectileDetonated(object payload)
        {
            if (!(payload is ProjectileDetonatedPayload detonated))
                return;
            // 距水面 1.0 世界单位以上算"空中爆炸"（格 1→2 单位后 0.5 → 1.0）。
            if (detonated.Position.y > LevelGeometry.WaterSurfaceY + 1.0f)
                return; // 空中爆炸不搅水

            InjectSplash(detonated.Position, 1f);
        }

        /// <summary>
        /// 在某世界坐标注入落水/爆炸涟漪（对外 API）。
        /// 落水事件 <c>crew_died</c> 的载荷不含世界坐标（<see cref="CrewDiedPayload"/> 只有 id/队伍/种类），
        /// 故 <c>FxRoot.OnCrewDied</c> 里拿到 <c>pirate.transform.position</c> 后调用本方法即可
        /// （一行接线，见报告）。驱动不存在时本方法安全空转。
        /// </summary>
        public static void InjectSplash(Vector3 worldPosition, float strength01)
        {
            WaterSimulationDriver driver = Instance;
            if (driver == null || !driver.IsRunning)
                return;
            driver.InjectSplashInternal(worldPosition, Mathf.Clamp01(strength01));
        }

        void InjectSplashInternal(Vector3 worldPosition, float strength01)
        {
            if (strength01 <= 0f)
                return;

            Vector2 uv = WaterSimRules.WorldToDomainUv(
                new Vector2(worldPosition.x, worldPosition.z), DomainCenter, domainSize);

            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                return; // 域外忽略（水位域只覆盖竞技场及近岸）

            _field.InjectGaussian(uv.x, uv.y, splashRadiusWorld / domainSize, splashAmplitude * strength01);

            if (splashFoam > 0f)
            {
                int cx = Mathf.Clamp((int)(uv.x * _field.CellsX), 0, _field.CellsX - 1);
                int cz = Mathf.Clamp((int)(uv.y * _field.CellsZ), 0, _field.CellsZ - 1);
                int r = Mathf.Max(1, Mathf.RoundToInt(splashRadiusWorld / domainSize * _field.CellsX * 0.5f));
                for (int z = cz - r; z <= cz + r; z++)
                {
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        float dd = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                        if (dd > r * r)
                            continue;
                        _field.AddFoam(x, z, splashFoam * strength01 * (1f - Mathf.Sqrt(dd) / Mathf.Max(r, 1)));
                    }
                }
            }
        }
    }
}
