using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Rendering.PostLine;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步「太阳雨」天气件（挂在 CloudWalk 预制体根，世界 (20,0,15)，本地原点 = 竞技场中心）：
    /// 金色雨丝 / 云隙光柱 / 偶发淡彩虹 / 水洼与海面涟漪 / PostLine 云影轻调制。
    /// 所有子对象位置都按**本物体局部坐标**表达（根在世界 (20,0,15)，局部原点即竞技场中心）。
    ///
    /// 【链契约】shader 用 <c>PirateCrew/CloudSea/CloudSeaRain</c> 的**双趟透明**
    /// （同 <see cref="CloudSeaPath.WaterShaderName"/> 的水体契约）：CellShade 走
    /// PixelartTransparent 趟（画进艺术画布、同颗粒、被地形正确遮挡），PostLine / 无 rig 的
    /// 普通相机走标准前向透明趟。两趟互斥由本组件每帧下发的闸门全局
    /// <c>_CloudSeaRainPixelDomain</c> 保证（判据 = RenderStyleState 是 CellShade 且 rig 在场）；
    /// 不判的话 CellShade 的叠加相机会把同一件按全分辨率再画一遍（重影 + 颗粒错档）。
    /// 涟漪不共用雨 shader，走 <see cref="CloudSeaPath.RippleShaderName"/>（像素同心环，单趟
    /// 前向透明）。
    ///
    /// 【成本契约】所有对象运行时创建、无 Collider、不写场景序列化；雨粒子 ≤1500；
    /// Update 每帧零堆分配（结构体数学 + 缓存的属性 ID，颜色是 struct 不装箱）；
    /// 运行时 new 的 Mesh / Material 在 <see cref="OnDestroy"/> 逐件释放
    /// （<see cref="UnityObjectCleanup"/> 口径）。材质按名取用走 <see cref="PixelartShaders"/>
    /// 取用口（全仓裸 Shader.Find 只许存在于该类内部）。
    /// </summary>
    public sealed class CloudSeaWeather : MonoBehaviour
    {
        // ==================== shader / 属性名契约（只有这一处写字面量） ====================

        // 单源：转换器排除 / 天气组件取用都走 CloudSeaPath 的同一常量。
        const string RainShaderName = CloudSeaPath.RainShaderName;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int TipFadeId = Shader.PropertyToID("_TipFade");
        static readonly int EdgeFadeId = Shader.PropertyToID("_EdgeFade");
        static readonly int PixelDomainGateId = Shader.PropertyToID("_CloudSeaRainPixelDomain");

        /// <summary>材质队列 = Transparent + 1：URP 标准透明档实测下界 3001（见 shader 文件头）。</summary>
        static readonly int TransparentQueueStandard = (int)RenderQueue.Transparent + 1;

        const float Tau = 6.2831853f;

        // ==================== 雨 ====================

        [Header("雨（金色太阳雨丝）")]
        [SerializeField] bool rainEnabled = true;
        [Tooltip("同时存活的雨丝预算（≤1500 是硬上限）；实际发射率 = 预算 / 生命期。")]
        [SerializeField, Range(0, 1500)] int rainParticleBudget = 1200;
        [Tooltip("可视雨幕范围：x 宽 / z 深 / y 不用（柱顶与死亡线单独给）。")]
        [SerializeField] float rainCoverageWidth = 40f;
        [SerializeField] float rainCoverageDepth = 30f;
        [Tooltip("发射带顶（局部 y）：雨丝从这里出生，可视柱 = 顶 − 死亡线 ≈ 22 m。")]
        [SerializeField] float rainTopY = 17.5f;
        [Tooltip("死亡线（局部 y）：低于水面 −0.4，配生命期末段渐隐，雨丝全程贯穿可视柱。")]
        [SerializeField] float rainDeathY = -6f;
        [SerializeField] float rainSpeedMin = 12f;
        [SerializeField] float rainSpeedMax = 18f;
        [SerializeField] Color rainColor = new Color(1f, 0.949f, 0.816f);   // #FFF2D0 暖金白
        [SerializeField, Range(0f, 1f)] float rainAlpha = 0.42f;
        [SerializeField, Range(0f, 4f)] float rainIntensity = 1f;
        [Tooltip("雨丝宽（米）。拉伸公告板下长度 = 长度倍率 × 宽，长度按雨丝长单独给。")]
        [SerializeField] float rainStreakWidth = 0.09f;
        [Tooltip("雨丝长（米）。换算成渲染器长度倍率 = 长 / 宽，速度不参与（保证长度确定）。")]
        [SerializeField] float rainStreakLength = 1.3f;

        // ==================== 云隙光柱 ====================

        [Header("云隙光柱（加法、斜射到云台/海面）")]
        [SerializeField] bool beamEnabled = true;
        [Tooltip("每项一根光柱：云隙出射点（局部坐标）。默认 4 根。")]
        [SerializeField] Vector3[] beamApexPoints =
        {
            new Vector3(-8f, 14.5f, -3f),
            new Vector3(4f, 16f, 6f),
            new Vector3(10f, 15f, -6f),
            new Vector3(-2f, 16.5f, 2f),
        };
        [Tooltip("光柱落点平面（局部 y；海面 = -0.4）。")]
        [SerializeField] float beamGroundY = -0.4f;
        [SerializeField] float beamApexWidth = 0.5f;
        [SerializeField] float beamBaseWidth = 2.4f;
        [SerializeField] Color beamColor = new Color(1f, 0.914f, 0.722f);   // #FFE9B8 淡金
        [SerializeField, Range(0f, 4f)] float beamIntensity = 1f;
        [Tooltip("alpha 在 [min, max] 间按 8–20 s 的正弦缓变（每根错相）。")]
        [SerializeField, Range(0f, 0.5f)] float beamAlphaMin = 0.06f;
        [SerializeField, Range(0f, 0.5f)] float beamAlphaMax = 0.15f;
        [SerializeField] float beamPeriodMin = 8f;
        [SerializeField] float beamPeriodMax = 20f;
        [Tooltip("斜射方向（世界语义同云影光向族，指向下方），Awake 里归一化。")]
        [SerializeField] Vector3 beamSlant = new Vector3(0.25f, -1f, 0.18f);

        // ==================== 彩虹 ====================

        [Header("彩虹（极淡半环，偶发淡入淡出）")]
        [SerializeField] bool rainbowEnabled = true;
        [SerializeField] Vector3 rainbowCenter = new Vector3(2f, 0f, -11f);
        [SerializeField] float rainbowRadius = 11f;
        [SerializeField] float rainbowBandWidth = 1.3f;
        [Tooltip("两次出现之间的随机间隔范围（秒）。")]
        [SerializeField] float rainbowMinInterval = 90f;
        [SerializeField] float rainbowMaxInterval = 180f;
        [Tooltip("一次可见的随机时长范围（秒），首尾各约 1/6 时长淡入淡出。")]
        [SerializeField] float rainbowMinDuration = 4f;
        [SerializeField] float rainbowMaxDuration = 8f;
        [SerializeField] Color rainbowColor = Color.white;
        [SerializeField, Range(0f, 1f)] float rainbowAlpha = 0.16f;
        [SerializeField, Range(0f, 4f)] float rainbowIntensity = 1f;
        [SerializeField, Range(8, 96)] int rainbowSegments = 48;

        // ==================== 涟漪 ====================

        [Header("涟漪（水洼 / 海面扩散的像素同心环）")]
        [SerializeField] bool rippleEnabled = true;
        [Tooltip("每个点一个涟漪：默认云台水洼 2 处 + 海面 3 处 + 云瀑落水点 1 处（y = 表面 + 0.03 防 z-fight）。装配期按实际云台顶面微调。")]
        [SerializeField] Vector3[] ripplePoints =
        {
            new Vector3(0f, 7.53f, 0f),
            new Vector3(4f, 3.03f, -2f),
            new Vector3(5f, -0.37f, 4f),
            new Vector3(-6f, -0.37f, -3f),
            new Vector3(2f, -0.37f, -7f),
            new Vector3(15f, -0.37f, -1f),
        };
        [Tooltip("环扩散到的最大半径（米）。")]
        [SerializeField] float rippleRadius = 0.70f;
        [Tooltip("环宽（艺术像素）。")]
        [SerializeField, Range(1f, 4f)] float rippleRingWidth = 1.5f;
        [Tooltip("一圈从出生到扩散满的时长随机范围（秒），之后隔一段空档再起。")]
        [SerializeField] float rippleLifeMin = 0.6f;
        [SerializeField] float rippleLifeMax = 1.5f;
        [Tooltip("环消失后的空档（占一圈时长的比例）。")]
        [SerializeField, Range(0f, 2f)] float rippleGap = 0.7f;
        [SerializeField] Color rippleColor = new Color(0.78f, 0.95f, 0.95f);
        [Tooltip("环的不透明度（扩散中按档位离散渐隐）。")]
        [SerializeField, Range(0f, 1f)] float rippleAlpha = 0.7f;

        // ==================== 云影调制（仅 PostLine 链有云影） ====================

        [Header("云影轻调制（PostLine 云影全局；CellShade 无云影，自动不生效）")]
        [SerializeField] bool cloudShadowModulation = true;
        [Tooltip("整体幅度旋钮，1 = 阈值 ±0.03 / 尺度 ±3% 的缓正弦；太阳雨的云要显得「活」。")]
        [SerializeField, Range(0f, 2f)] float cloudShadowStrength = 1f;

        // ==================== 运行时句柄（全部 Awake 创建、OnDestroy 释放） ====================

        struct BeamVisual
        {
            public GameObject GameObject;
            public Mesh Mesh;
            public Material Material;
            public float Period;
            public float Phase;
        }

        struct RippleVisual
        {
            public Transform Transform;
            public Material Material;
        }

        GameObject _rainGameObject;
        Material _rainMaterial;
        BeamVisual[] _beams;
        GameObject _rainbowGameObject;
        Material _rainbowMaterial;
        Mesh _rainbowMesh;
        RippleVisual[] _ripples;
        Mesh _rippleMesh;
        bool _built;

        // 云影基线：PostLineEnvironment 每帧在 AfterRenderingPostProcessing 趟里重推常量，
        // 本组件在下一帧 Update 改回去即生效（对象着色发生在那之前）。基线不硬编码常量值
        // （那是 PostLineEnvironment 的私域），而是哨兵捕获：_CloudScale 被推成非 0（默认全局
        // 是 0）那一刻读入两个基线，之后按缓存基线做加性调制，不逐帧自读自乘防漂移。
        bool _cloudBaseKnown;
        float _cloudBaseScale;
        float _cloudBaseThreshold;
        bool _cloudBaselineWarned;
        int _framesSinceStart;
        float _rainbowNextShow;
        float _rainbowShowStart;
        float _rainbowShowDuration;
        bool _rainbowVisible;

        // ==================== 构建 ====================

        void Awake()
        {
            // 闸门首发（Update 里每帧刷新；这里只是让首个材质校验前状态就正确）。
            PushChainGate();

            Shader shader = null;
            bool needShader = rainEnabled || beamEnabled || rainbowEnabled;
            if (needShader)
            {
                shader = PixelartShaders.Load(RainShaderName);
                if (shader == null)
                    Log.Error("[CloudSeaWeather] 雨天气 shader \"" + RainShaderName
                        + "\" 摸不到——雨 / 光柱 / 彩虹三件视觉全部跳过（涟漪与云影调制不受影响）。");
            }

            if (rainEnabled && shader != null) BuildRain(shader);
            if (beamEnabled && shader != null) BuildBeams(shader);
            if (rainbowEnabled && shader != null) BuildRainbow(shader);
            if (rippleEnabled) BuildRipples();

            // 彩虹按计划表首秀（静默期先空场，"偶尔淡彩虹"）。
            _rainbowNextShow = Time.time + Random.Range(rainbowMinInterval, rainbowMaxInterval);

            _built = true;
        }

        /// <summary>雨：ParticleSystem 拉伸公告板。发射带在柱顶一条窄带，出生后全程贯穿可视柱。</summary>
        void BuildRain(Shader shader)
        {
            int budget = Mathf.Clamp(rainParticleBudget, 0, 1500);
            if (budget <= 0)
                return;

            float avgSpeed = Mathf.Max(0.1f, (rainSpeedMin + rainSpeedMax) * 0.5f);
            float fallHeight = Mathf.Max(1f, rainTopY - rainDeathY);
            // 单一生命期（速度随机已给出变化；生命期再随机只会让死亡深度更散）。
            float lifetime = fallHeight / avgSpeed;

            _rainGameObject = new GameObject("CloudSeaWeather_Rain");
            _rainGameObject.transform.SetParent(transform, false);

            ParticleSystem ps = _rainGameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;  // 根若移动，已发射雨丝留在世界
            main.prewarm = true;                                         // 进场即满柱雨，不用等首圈
            main.startSpeed = 0f;                                        // 速度全交 velocityOverLifetime（方向显式）
            main.startLifetime = lifetime;
            main.startSize = Mathf.Max(0.01f, rainStreakWidth);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white); // 颜色在材质 _Color，顶点色只带渐隐
            main.maxParticles = budget;
            main.gravityModifier = 0f;                                   // 无物理：匀速直落

            // 向下略斜：横漂常数与云影光向族（(0.2,-1,0.2)）同向；y 给 12–18 的速度带。
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 2.2f;
            velocity.y = new ParticleSystem.MinMaxCurve(-rainSpeedMax, -rainSpeedMin);
            velocity.z = 1.6f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = budget / lifetime;                   // 稳态存活数 ≈ 预算

            // 发射带：宽 × 深全铺，高只留 1.2 m 窄带贴着柱顶——粒子出生即穿过整个可视柱。
            const float BandHeight = 1.2f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Mathf.Max(0.1f, rainCoverageWidth), BandHeight, Mathf.Max(0.1f, rainCoverageDepth));
            shape.position = new Vector3(0f, rainTopY - BandHeight * 0.5f, 0f);

            // 生命期末段渐隐：雨丝死于水面之下（死亡线低过海面），末段淡出让"没入水中"
            // 只剩一点点可见的探出，避免整根横切透明水面。
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.78f),
                    new GradientAlphaKey(0f, 1f),
                });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            _rainMaterial = MakeMaterial(shader, rainColor, rainAlpha, rainIntensity,
                BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, tipFade: 1f, edgeFade: 0f);

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;      // 速度对齐的拉伸公告板
            // 长度 = 倍率 × 粒子尺寸（渲染器速度缩放不用——速度进长度会把 12–18 m/s 的速度带
            // 放大成数米不可控的丝；定长倍率让雨丝长度只由序列化参数决定）。
            renderer.lengthScale = rainStreakLength / Mathf.Max(0.01f, rainStreakWidth);
            renderer.velocityScale = 0f;
            renderer.minParticleSize = 0f;                               // 关掉相机距离钳制（正交远机位默认值会缩粒子）
            renderer.maxParticleSize = 0f;
            renderer.sharedMaterial = _rainMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
        }

        /// <summary>光柱：每根一个交叉双四边形（自由方位相机下不侧棱消失）+ 加法材质。</summary>
        void BuildBeams(Shader shader)
        {
            if (beamApexPoints == null || beamApexPoints.Length == 0)
                return;

            Vector3 slant = beamSlant.sqrMagnitude > 1e-6f ? beamSlant.normalized : new Vector3(0.2f, -1f, 0.2f);
            _beams = new BeamVisual[beamApexPoints.Length];

            for (int i = 0; i < beamApexPoints.Length; i++)
            {
                Vector3 apex = beamApexPoints[i];
                float fall = (apex.y - beamGroundY) / -slant.y;
                Vector3 ground = apex + slant * fall;
                Vector3 sideA = Vector3.Cross(slant, Vector3.up).normalized;   // 水平横轴
                Vector3 sideB = Vector3.Cross(slant, sideA).normalized;        // 与前两者正交的横轴

                Vector3[] vertices = BuildBeamVertices(apex, ground, sideA, sideB, out Vector2[] uvs, out Color[] colors);
                Mesh mesh = new Mesh { name = "CloudSeaWeather_Beam" + i };
                mesh.vertices = vertices;
                mesh.uv = uvs;
                mesh.colors = colors;
                mesh.triangles = new[]
                {
                    // 每个四边形 0-1-2-3 = apex−、apex+、ground−、ground+（Cull Off，绕向无关紧要但保持一致）
                    0, 2, 1, 1, 2, 3,
                    4, 6, 5, 5, 6, 7,
                };
                mesh.RecalculateBounds();

                Material material = MakeMaterial(shader, beamColor, beamAlphaMax, beamIntensity,
                    BlendMode.SrcAlpha, BlendMode.One, tipFade: 0f, edgeFade: 1f);

                GameObject go = new GameObject("CloudSeaWeather_Beam" + i);
                go.transform.SetParent(transform, false);
                MeshFilter filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;

                _beams[i] = new BeamVisual
                {
                    GameObject = go,
                    Mesh = mesh,
                    Material = material,
                    Period = Random.Range(Mathf.Max(0.1f, beamPeriodMin), Mathf.Max(beamPeriodMin + 0.1f, beamPeriodMax)),
                    Phase = i * 2.39996f,   // 黄金角错相，四根不同步呼吸
                };
            }
        }

        Vector3[] BuildBeamVertices(Vector3 apex, Vector3 ground, Vector3 sideA, Vector3 sideB, out Vector2[] uvs, out Color[] colors)
        {
            // 两个正交平面的锥形四边形：顶窄底宽，uv.x 横向（配 _EdgeFade 柔边）、uv.y 顶→底。
            uvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            colors = new[]
            {
                Color.white, Color.white, Color.white, Color.white,
                Color.white, Color.white, Color.white, Color.white,
            };
            return new[]
            {
                apex - sideA * (beamApexWidth * 0.5f),
                apex + sideA * (beamApexWidth * 0.5f),
                ground - sideA * (beamBaseWidth * 0.5f),
                ground + sideA * (beamBaseWidth * 0.5f),
                apex - sideB * (beamApexWidth * 0.5f),
                apex + sideB * (beamApexWidth * 0.5f),
                ground - sideB * (beamBaseWidth * 0.5f),
                ground + sideB * (beamBaseWidth * 0.5f),
            };
        }

        /// <summary>彩虹：XY 平面（法线 ±Z）的半环带，u 横穿带宽带光谱顶点色（内蓝紫 → 外红）。</summary>
        void BuildRainbow(Shader shader)
        {
            int segments = Mathf.Max(8, rainbowSegments);
            float innerRadius = Mathf.Max(0.5f, rainbowRadius - rainbowBandWidth);

            _rainbowMesh = new Mesh { name = "CloudSeaWeather_Rainbow" };
            Vector3[] verts = new Vector3[(segments + 1) * 2];
            Vector2[] uvs = new Vector2[verts.Length];
            Color[] colors = new Color[verts.Length];
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.PI * i / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = i * 2 + 1;
                verts[inner] = new Vector3(cos * innerRadius, sin * innerRadius, 0f);
                verts[outer] = new Vector3(cos * rainbowRadius, sin * rainbowRadius, 0f);
                uvs[inner] = new Vector2(0f, 0f);
                uvs[outer] = new Vector2(1f, 0f);
                // 光谱烘进顶点色（内圈蓝紫 → 外圈红）；alpha 包络走材质，顶点色 alpha 恒 1。
                colors[inner] = RainbowSpectrum(0f);
                colors[outer] = RainbowSpectrum(1f);
            }
            int[] tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;          // inner[i]
                int b = i * 2 + 1;      // outer[i]
                int c = i * 2 + 2;      // inner[i+1]
                int d = i * 2 + 3;      // outer[i+1]
                int t = i * 6;
                tris[t] = a; tris[t + 1] = b; tris[t + 2] = c;
                tris[t + 3] = b; tris[t + 4] = d; tris[t + 5] = c;
            }
            _rainbowMesh.vertices = verts;
            _rainbowMesh.uv = uvs;
            _rainbowMesh.colors = colors;
            _rainbowMesh.triangles = tris;
            _rainbowMesh.RecalculateBounds();

            _rainbowMaterial = MakeMaterial(shader, Color.white, rainbowAlpha, rainbowIntensity,
                BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, tipFade: 0f, edgeFade: 1f);

            _rainbowGameObject = new GameObject("CloudSeaWeather_Rainbow");
            _rainbowGameObject.transform.SetParent(transform, false);
            _rainbowGameObject.transform.localPosition = rainbowCenter;
            MeshFilter filter = _rainbowGameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _rainbowMesh;
            MeshRenderer meshRenderer = _rainbowGameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _rainbowMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            _rainbowGameObject.SetActive(false);   // 首秀前空场
        }

        /// <summary>内圈(u=0)蓝紫 → 外圈(u=1)红；饱和度压低，配合极淡 alpha 不刺眼。</summary>
        static Color RainbowSpectrum(float u)
        {
            Color c = Color.HSVToRGB(Mathf.Lerp(0.68f, 0f, u), 0.55f, 1f);
            c.a = 1f;
            return c;
        }

        // 涟漪 shader 的属性名（只有这一处写字面量）。
        static readonly int RingColorId = Shader.PropertyToID("_RingColor");
        static readonly int RingAlphaId = Shader.PropertyToID("_RingAlpha");
        static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
        static readonly int RingRadiusId = Shader.PropertyToID("_Radius");
        static readonly int LoopPeriodId = Shader.PropertyToID("_LoopPeriod");
        static readonly int LoopGapId = Shader.PropertyToID("_LoopGap");
        static readonly int LoopPhaseId = Shader.PropertyToID("_Phase");

        /// <summary>
        /// 涟漪：共享一张单位方片（XZ 平面，半径 1 = <see cref="rippleRadius"/> 米）。扩散 / 渐隐 /
        /// 空档全在 shader 里按时间走（CloudSeaRipple 的自动循环）——建完之后每帧零开销。
        /// </summary>
        void BuildRipples()
        {
            int count = ripplePoints == null ? 0 : ripplePoints.Length;
            if (count == 0)
                return;

            Shader shader = PixelartShaders.Load(CloudSeaPath.RippleShaderName);
            if (shader == null)
            {
                Log.Error("[CloudSeaWeather] 涟漪 shader \"" + CloudSeaPath.RippleShaderName
                    + "\" 摸不到——涟漪跳过（其余天气件不受影响）。");
                return;
            }

            _rippleMesh = new Mesh { name = "CloudSeaWeather_RippleQuad" };
            _rippleMesh.vertices = new[]
            {
                new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f),
                new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
            };
            _rippleMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _rippleMesh.RecalculateBounds();

            _ripples = new RippleVisual[count];
            for (int i = 0; i < count; i++)
            {
                // 一个循环周期 = 一圈时长 × (1 + 空档)：shader 的扩散进度在一圈时长内走满，
                // 其余时间环已出画，隔一段再重起。
                float life = Random.Range(rippleLifeMin, rippleLifeMax);
                Material material = new Material(shader) { name = "CloudSeaWeather_Ripple" };
                material.SetColor(RingColorId, rippleColor);
                material.SetFloat(RingAlphaId, rippleAlpha);
                material.SetFloat(RingWidthId, rippleRingWidth);
                material.SetFloat(RingRadiusId, rippleRadius);
                material.SetFloat(LoopPeriodId, life * (1f + rippleGap));
                material.SetFloat(LoopGapId, rippleGap);
                material.SetFloat(LoopPhaseId, Random.value);   // 各圈相位错开，不同起
                material.renderQueue = TransparentQueueStandard;

                GameObject go = new GameObject("CloudSeaWeather_Ripple" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = ripplePoints[i];
                go.transform.localScale = new Vector3(rippleRadius, 1f, rippleRadius);
                MeshFilter filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _rippleMesh;
                MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;

                _ripples[i] = new RippleVisual
                {
                    Transform = go.transform,
                    Material = material,
                };
            }
        }

        /// <summary>统一出材质：装扮（混向 / 两个渐变）+ 着色 + 队列 3001（两链都收，见 shader 文件头）。</summary>
        static Material MakeMaterial(Shader shader, Color color, float alpha, float intensity,
            BlendMode srcBlend, BlendMode dstBlend, float tipFade, float edgeFade)
        {
            Material material = new Material(shader) { name = "CloudSeaWeather_FX" };
            material.SetColor(ColorId, new Color(color.r, color.g, color.b, alpha));
            material.SetFloat(SrcBlendId, (float)srcBlend);
            material.SetFloat(DstBlendId, (float)dstBlend);
            material.SetFloat(TipFadeId, tipFade);
            material.SetFloat(EdgeFadeId, edgeFade);
            material.renderQueue = TransparentQueueStandard;
            return material;
        }

        // ==================== 每帧（零堆分配：只有 struct 数学与缓存 ID 的 Set*） ====================

        void Update()
        {
            float t = Time.time;
            PushChainGate();

            // 云影全局族由 PostLineEnvironment 在录制趟里每帧推；这里在 Update 里先补推同一组常量，
            // 顺序必须在 UpdateCloudShadow 之前——否则会把调制值压回基线。
            global::PirateCrew.Rendering.PostLine.PostLineEnvironment.PushCloudShadowGlobals();

            if (_beams != null) UpdateBeams(t);
            if (_rainbowMaterial != null) UpdateRainbow(t);
            if (cloudShadowModulation) UpdateCloudShadow(t);
        }

        /// <summary>链闸门：CellShade rig 在场 = 像素化域（标准趟让位），否则标准趟接管。</summary>
        void PushChainGate()
        {
            // RenderStyleState 切换以场景重载生效，rig 是链的真实载体（无 rig 的普通 URP 场景
            // 里闸门必须为 0，否则标准趟自裁 = 整类消失）。PostLine 请求但渲染器缺失时 rig 会
            // 回退 CellShade 而真值仍写 PostLine——闸门保持 0，叠加相机的标准趟兜底显示。
            bool pixelDomain = RenderStyleState.Current == RenderStyle.CellShade
                && PixelartPath.ActiveRig != null;
            Shader.SetGlobalFloat(PixelDomainGateId, pixelDomain ? 1f : 0f);
        }

        void UpdateBeams(float t)
        {
            for (int i = 0; i < _beams.Length; i++)
            {
                BeamVisual beam = _beams[i];
                if (beam.Material == null)
                    continue;
                float wave = 0.5f + 0.5f * Mathf.Sin(Tau * t / beam.Period + beam.Phase);
                float alpha = Mathf.Lerp(beamAlphaMin, beamAlphaMax, wave);
                beam.Material.SetColor(ColorId, new Color(beamColor.r, beamColor.g, beamColor.b, alpha));
            }
        }

        void UpdateRainbow(float t)
        {
            if (!_rainbowVisible)
            {
                if (t >= _rainbowNextShow && _rainbowGameObject != null)
                {
                    _rainbowVisible = true;
                    _rainbowShowStart = t;
                    _rainbowShowDuration = Random.Range(rainbowMinDuration, rainbowMaxDuration);
                    _rainbowGameObject.SetActive(true);
                }
                else
                {
                    return;
                }
            }

            float k = (t - _rainbowShowStart) / Mathf.Max(0.01f, _rainbowShowDuration);
            if (k >= 1f)
            {
                _rainbowVisible = false;
                _rainbowNextShow = t + Random.Range(rainbowMinInterval, rainbowMaxInterval);
                if (_rainbowGameObject != null)
                    _rainbowGameObject.SetActive(false);
                return;
            }

            // 首尾各约 1/6 时长淡入 / 淡出。
            float envelope = Mathf.Clamp01(Mathf.Min(k, 1f - k) * 6f);
            _rainbowMaterial.SetColor(ColorId, new Color(
                rainbowColor.r, rainbowColor.g, rainbowColor.b, rainbowAlpha * envelope));
        }

        void UpdateCloudShadow(float t)
        {
            _framesSinceStart++;

            if (!_cloudBaseKnown)
            {
                // 哨兵捕获：PostLineEnvironment 首次推流后 _CloudScale = 50（默认全局 = 0）。
                // 拿不到基线就不调制——宁可"没效果"也不能拿 0 当基线把全局压成负/零。
                float pushedScale = Shader.GetGlobalFloat(PostLinePath.CloudScaleId);
                if (pushedScale > 0.001f)
                {
                    _cloudBaseKnown = true;
                    _cloudBaseScale = pushedScale;
                    _cloudBaseThreshold = Shader.GetGlobalFloat(PostLinePath.CloudThresholdId);
                }
                else if (RenderStyleState.Current == RenderStyle.PostLine
                    && !_cloudBaselineWarned && _framesSinceStart > 900)
                {
                    // PostLine 在场却约 15 秒仍读不到推送值（cmd 全局与 CPU 侧读回不通的极端情形）
                    // ——响一次，不刷屏。
                    _cloudBaselineWarned = true;
                    Log.Warn("[CloudSeaWeather] 云影基线未捕获（PostLine 云影全局一直为 0）——调制未生效。");
                }
                return;
            }

            // 轻调制：尺度 ±3% / 阈值 ±0.03 的缓正弦（两个不同周期，避免同步呼吸的机械感）。
            // 只在本帧 Update → 渲染的窗口生效，PostLine 的帧末重推会在下一帧被再次覆盖。
            float scaleWave = Mathf.Sin(Tau * t / 23f);
            float thresholdWave = Mathf.Sin(Tau * t / 17f + 1.3f);
            Shader.SetGlobalFloat(PostLinePath.CloudScaleId,
                _cloudBaseScale * (1f + 0.03f * cloudShadowStrength * scaleWave));
            Shader.SetGlobalFloat(PostLinePath.CloudThresholdId,
                _cloudBaseThreshold + 0.03f * cloudShadowStrength * thresholdWave);
        }

        // ==================== 释放 ====================

        void OnDestroy()
        {
            // 还原云影全局：组件退场后不能把调制值留在全局里污染别的场景。
            if (_cloudBaseKnown)
            {
                Shader.SetGlobalFloat(PostLinePath.CloudScaleId, _cloudBaseScale);
                Shader.SetGlobalFloat(PostLinePath.CloudThresholdId, _cloudBaseThreshold);
            }

            if (!_built)
                return;

            // 子件 GO 播放期随本物体销毁，但编辑器下单独移除本组件会留孤儿——显式拆（口径同 TrajectoryPreview）。
            if (_rainGameObject != null) UnityObjectCleanup.DestroySafe(_rainGameObject);
            if (_rainbowGameObject != null) UnityObjectCleanup.DestroySafe(_rainbowGameObject);
            if (_beams != null)
            {
                for (int i = 0; i < _beams.Length; i++)
                {
                    UnityObjectCleanup.DestroySafe(_beams[i].GameObject);
                    UnityObjectCleanup.DestroySafe(_beams[i].Mesh);
                    UnityObjectCleanup.DestroySafe(_beams[i].Material);
                }
            }
            if (_ripples != null)
            {
                for (int i = 0; i < _ripples.Length; i++)
                {
                    if (_ripples[i].Transform != null)
                        UnityObjectCleanup.DestroySafe(_ripples[i].Transform.gameObject);
                    UnityObjectCleanup.DestroySafe(_ripples[i].Material);
                }
            }
            UnityObjectCleanup.DestroySafe(_rippleMesh);
            UnityObjectCleanup.DestroySafe(_rainbowMesh);
            UnityObjectCleanup.DestroySafe(_rainMaterial);
            UnityObjectCleanup.DestroySafe(_rainbowMaterial);
        }
    }
}
