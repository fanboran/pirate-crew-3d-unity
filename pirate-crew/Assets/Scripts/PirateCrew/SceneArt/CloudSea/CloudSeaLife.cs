using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步场景的**活物层**：鱼群巡游 + 海鸥群（飞行 + 停栖）。
    ///
    /// 【三方契约】鱼群/停栖鸥对象由 FBX 带入（烘焙侧按 <see cref="CloudSeaPath"/> 的
    /// <c>Cloud_FishSchool_*</c> / <c>Cloud_GullPerch_*</c> 命名），本组件 Start 时**只在
    /// 自己的子树里按前缀找**（不用任何 Find 系列全场景搜索）；飞行海鸥则全部由本组件在
    /// 运行时纯代码生成（低模网格 + 像素路径材质），不进 FBX、不进资产。
    ///
    /// 【坐标系】组件挂在 CloudWalk 预制体根（世界 (20,0,15)，本地原点 = 竞技场中心，
    /// 根无旋转缩放 ⇒ 本地 Y == 世界 Y）。水面 y=-0.4，云台顶 2.5..7.5；鱼群上沿
    /// （<see cref="fishYMax"/>）与俯冲掠水高度（<see cref="diveY"/>）都是**本地 Y**。
    ///
    /// 【贴床定深】海床是起伏沙丘（脊顶与洼地差近 1 米）。烘焙器把海床网格烘成一张高度格
    /// 写进本组件（<see cref="CloudSeaBedGrid"/>），鱼群 / 螃蟹每帧查「脚下沙丘多高」：鱼群
    /// 下沿 = 沙丘最高处 + 净空，上沿夹进巡游带；螃蟹脚底贴沙。不穿海面 / 海床是几何保证，
    /// 不是调参凑的。没烘格时（单独预览 prefab）鱼群退回固定深度带 fishYMin..fishYMax、
    /// 螃蟹停在原高度。
    ///
    /// 【运动形状】全部是**解析参数路径**（圆/8 字、椭圆/李萨如 + 正弦浮动），没有物理、
    /// 没有碰撞体、没有刚体；朝向一律由解析速度推出（鱼群偏航、海鸥 LookRotation 含俯仰，
    /// 俯冲时自然低头再拉起）。俯冲只改路径参数（在巡航高度与掠水高度之间走一个平滑钟形
    /// 插值），水平轨迹照走不误。
    ///
    /// 【性能口径】Start 一次性建齐（查子物体/建网格/建材质/抽随机参数），Update 是
    /// **唯一入口**（海鸥不是 MonoBehaviour，与 AmbientDirector 同一手法定期驱动），
    /// 每帧只有三角函数与 Transform 写回，**零堆分配**；随机走 <see cref="System.Random"/>
    /// （<see cref="randomSeed"/> 播种，同 seed 同布局，不用全局的 UnityEngine.Random）。
    ///
    /// 【静态批合红线】被本组件驱动的 FBX 对象（鱼群/停栖鸥）**不可 isStatic**——静态批合的
    /// 物体不响应 Transform（烘焙侧保证，同 <c>FloatingIslandDrift</c> 的口径）。
    ///
    /// 【开关生效时机】三个开关（鱼群/海鸥/停栖）只在 Start 读一次：运行中改值不建也不删，
    /// 要换布局改参数后重开场景。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudSeaLife : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 参数（Inspector 可调；Start 时读定）
        // ------------------------------------------------------------------

        [Header("远景云团漂移（动态云彩；绕各自中心自转 + 微升沉）")]
        [Tooltip("远景云团总开关（按对象名前缀 Cloud_Far_ 识别）")]
        [SerializeField] bool farCloudDrift = true;
        [Tooltip("自转角速度（度/秒，正反随机）")]
        [SerializeField] float farCloudSpinDegPerSec = 1.2f;
        [Tooltip("升沉幅度（米）")]
        [SerializeField] float farCloudBobMeters = 0.15f;

        /// <summary>远景云团（Start 按前缀收集：Cloud_Far_SW/SE/NW/NE/Tower）。</summary>
        readonly List<Transform> _farClouds = new List<Transform>();
        readonly List<float> _farCloudBaseY = new List<float>();
        readonly List<float> _farCloudPhase = new List<float>();
        readonly List<float> _farCloudSpin = new List<float>();

        [Header("通用")]
        [Tooltip("随机播种（System.Random）：同 seed 同一批相位/半径/高度，改数即换一套布局")]
        [SerializeField] int randomSeed = 0;

        [Tooltip("鱼群巡游总开关")]
        [SerializeField] bool fishEnabled = true;
        [Tooltip("飞行海鸥总开关")]
        [SerializeField] bool gullsEnabled = true;
        [Tooltip("停栖海鸥摆动总开关")]
        [SerializeField] bool perchSwayEnabled = true;
        [Tooltip("海鸥掠水俯冲总开关")]
        [SerializeField] bool diveEnabled = true;

        [Header("鱼群（FBX: Cloud_FishSchool_*）")]
        [Tooltip("巡游线速度下/上限（m/s）")]
        [SerializeField] float fishSpeedMin = 0.15f;
        [SerializeField] float fishSpeedMax = 0.35f;
        [Tooltip("路径半径下/上限（m，中心 = 群初始位置）")]
        [SerializeField] float fishRadiusMin = 1.5f;
        [SerializeField] float fishRadiusMax = 3f;
        [Tooltip("8 字路径占比（0..1，其余走圆）")]
        [SerializeField, Range(0f, 1f)] float fishFigure8Chance = 0.45f;
        [Tooltip("上下浮动幅度下/上限（米）。幅度再大也会被「脚下沙丘 + 净空」与巡游带上沿夹住")]
        [SerializeField] float fishBobAmplitudeMin = 0.03f;
        [SerializeField] float fishBobAmplitudeMax = 0.06f;
        [Tooltip("上下浮动周期下/上限（s）")]
        [SerializeField] float fishBobPeriodMin = 3f;
        [SerializeField] float fishBobPeriodMax = 6f;
        [Tooltip("巡游带上沿（本地 Y）：鱼群最终 Y 不高于它（离水面 -0.4 的最小距离由它定）")]
        [SerializeField] float fishYMax = -0.75f;
        [Tooltip("巡游带下沿（本地 Y）：只在没有海床高度格时生效（固定深度带兜底）")]
        [SerializeField] float fishYMin = -2.4f;
        [Tooltip("鱼群离脚下沙丘的净空（米；另加鱼群网格的半高）")]
        [SerializeField] float fishBedClearance = 0.15f;

        [Header("海床高度格（烘焙器写入，勿手改）")]
        [SerializeField, HideInInspector] int bedGridCells = 40;
        [SerializeField, HideInInspector] float bedGridHalfSize = 20f;
        [SerializeField, HideInInspector] float[] bedHeights = new float[0];

        [Header("海底螃蟹（FBX: Cloud_Crab_*）")]
        [Tooltip("螃蟹横向爬行总开关")]
        [SerializeField] bool crabEnabled = true;
        [Tooltip("爬行线速度下/上限（m/s）：螃蟹横着走，慢速巡逻")]
        [SerializeField] float crabSpeedMin = 0.05f;
        [SerializeField] float crabSpeedMax = 0.12f;
        [Tooltip("巡游半长下/上限（m，沿一条直线往复）")]
        [SerializeField] float crabPatrolMin = 0.30f;
        [SerializeField] float crabPatrolMax = 0.55f;
        [Tooltip("横向爬行偏角（度）：蟹身朝向 = 行进方向 + 该角（90 = 标准横着走）")]
        [SerializeField] float crabSidewaysDeg = 90f;
        [Tooltip("步足抬落频率下/上限（Hz）")]
        [SerializeField] float crabStepHzMin = 2f;
        [SerializeField] float crabStepHzMax = 3.5f;

        [Header("飞行海鸥（运行时生成）")]
        [Tooltip("海鸥只数（原口径 14-18，可放宽到 12-20）")]
        [SerializeField, Range(4, 32)] int gullCount = 16;
        [Tooltip("绕群岛轨道半径下/上限（m，中心 = 本地原点附近）")]
        [SerializeField] float gullRadiusMin = 8f;
        [SerializeField] float gullRadiusMax = 18f;
        [Tooltip("飞行高度带下/上限（本地 Y；云台顶最高 7.5，低于它的轨道沿云台外缘走）")]
        [SerializeField] float gullHeightMin = 6f;
        [SerializeField] float gullHeightMax = 14f;
        [Tooltip("巡航线速度下/上限（m/s）")]
        [SerializeField] float gullSpeedMin = 2f;
        [SerializeField] float gullSpeedMax = 5f;
        [Tooltip("李萨如（8 字形）轨道占比")]
        [SerializeField, Range(0f, 1f)] float gullFigure8Chance = 0.3f;
        [Tooltip("体型缩放下/上限（相对单位模型，均匀缩放）")]
        [SerializeField] float gullSizeMin = 0.85f;
        [SerializeField] float gullSizeMax = 1.25f;
        [Tooltip("上下浮动幅度下/上限（m）")]
        [SerializeField] float gullBobAmplitudeMin = 0.2f;
        [SerializeField] float gullBobAmplitudeMax = 0.5f;

        [Header("海鸥拍翅 / 滑翔")]
        [Tooltip("拍翅频率下/上限（Hz）")]
        [SerializeField] float gullFlapHzMin = 2f;
        [SerializeField] float gullFlapHzMax = 4f;
        [Tooltip("拍翅摆角幅度（度，绕翼根的滚转角）")]
        [SerializeField] float gullFlapAmplitudeDegrees = 38f;
        [Tooltip("滑翔时双翼上抬角（度，停拍姿态）")]
        [SerializeField] float gullGlideAngleDegrees = 12f;

        [Header("掠水俯冲")]
        [Tooltip("触发间隔下/上限（s，全局调度：到点随机挑一只不在俯冲的）")]
        [SerializeField] float diveIntervalMin = 8f;
        [SerializeField] float diveIntervalMax = 15f;
        [Tooltip("掠水高度（本地 Y；≈ -0.1 即贴着 -0.4 的水面上方 0.3m）")]
        [SerializeField] float diveY = -0.1f;
        [Tooltip("俯冲全程时长（s，前半下扎后半拉起）")]
        [SerializeField] float diveDuration = 2.4f;

        [Header("停栖海鸥（FBX: Cloud_GullPerch_*）")]
        [Tooltip("摆动幅度下/上限（度；pitch 全幅，yaw/roll 按比例打折）")]
        [SerializeField] float perchAmplitudeMin = 1f;
        [SerializeField] float perchAmplitudeMax = 3f;
        [Tooltip("摆动周期下/上限（s；yaw/roll 各自微失谐避免机械同步）")]
        [SerializeField] float perchPeriodMin = 3f;
        [SerializeField] float perchPeriodMax = 7f;

        // ------------------------------------------------------------------
        // 运行时状态（Start 建齐，Update 只读）
        // ------------------------------------------------------------------

        System.Random _rng;

        FishSchoolState[] _schools;
        PerchGullState[] _perches;
        CrabState[] _crabs;
        FlyingGullState[] _gulls;
        float _diveCountdown;

        // 运行时自建资产（OnDestroy 释放；FBX 子物体不属于本组件，绝不销毁）
        GameObject _gullContainer;
        Mesh _gullBodyMesh;
        Mesh _gullHeadMesh;
        Mesh _gullBeakMesh;
        Mesh _gullWingRightMesh;
        Mesh _gullWingLeftMesh;
        Material _gullBodyMaterial;
        Material _gullWingMaterial;
        Material _gullBeakMaterial;

        // ------------------------------------------------------------------
        // 建局
        // ------------------------------------------------------------------

        void Start()
        {
            _rng = new System.Random(randomSeed);

            GatherFbxCreatures();
            GatherFarClouds();

            if (gullsEnabled)
                CreateGullFlock();

            // 首次俯冲错开：不从 0 开始，避免开局立刻有一只离群。
            _diveCountdown = Range(diveIntervalMin, diveIntervalMax);
        }

        /// <summary>
        /// 收集远景云团（FBX 根级 Cloud_Far_* 对象；mod_sky 的体积云岛/塔）。
        /// 各团记录基准高度、自转速度（正反随机）与相位——Update 里绕**自身枢轴**
        /// 缓转 + 微升沉，读作云在天上慢慢漂（mod_sky 的对象枢轴即云团中心）。
        /// </summary>
        void GatherFarClouds()
        {
            if (!farCloudDrift)
                return;
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Cloud_Far_", System.StringComparison.Ordinal))
                {
                    _farClouds.Add(child);
                    _farCloudBaseY.Add(child.position.y);
                    _farCloudPhase.Add((float)(_rng != null ? _rng.NextDouble() : 0.5) * Mathf.PI * 2f);
                    _farCloudSpin.Add((float)(_rng != null ? _rng.NextDouble() : 0.5) > 0.5f ? 1f : -1f);
                }
            }
        }

        /// <summary>远景云团缓转 + 升沉（动态云彩；幅度小、速度慢，只求"活着"）。</summary>
        void TickFarClouds(float dt)
        {
            if (!farCloudDrift)
                return;
            float time = Time.time;
            for (int i = 0; i < _farClouds.Count; i++)
            {
                Transform cloud = _farClouds[i];
                if (cloud == null)
                    continue;
                cloud.Rotate(0f, _farCloudSpin[i] * farCloudSpinDegPerSec * dt, 0f, Space.Self);
                Vector3 pos = cloud.position;
                pos.y = _farCloudBaseY[i]
                    + farCloudBobMeters * Mathf.Sin(time * 0.18f + _farCloudPhase[i]);
                cloud.position = pos;
            }
        }

        /// <summary>只遍历自己的子树按前缀收集鱼群/停栖鸥/螃蟹（前缀契约在 <see cref="CloudSeaPath"/>）。</summary>
        void GatherFbxCreatures()
        {
            var schools = new List<FishSchoolState>();
            var perches = new List<PerchGullState>();
            var crabs = new List<CrabState>();
            Transform[] children = GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == transform)
                    continue;

                if (fishEnabled && child.name.StartsWith(CloudSeaPath.FishSchoolPrefix, System.StringComparison.Ordinal))
                    schools.Add(BuildFishSchool(child));

                if (perchSwayEnabled && child.name.StartsWith(CloudSeaPath.GullPerchPrefix, System.StringComparison.Ordinal))
                    perches.Add(BuildPerchGull(child));

                if (crabEnabled && child.name.StartsWith(CloudSeaPath.CrabPrefix, System.StringComparison.Ordinal))
                    crabs.Add(BuildCrab(child));
            }

            _schools = schools.ToArray();
            _perches = perches.ToArray();
            _crabs = crabs.ToArray();

            // 缺对象要响亮（FBX 没导入 / 名字改了是烘焙侧契约破裂，不静默）。
            if (fishEnabled && _schools.Length == 0)
                Debug.LogWarning("[CloudSeaLife] 子物体里没找到 " + CloudSeaPath.FishSchoolPrefix + "*，鱼群层空置。", this);
            if (perchSwayEnabled && _perches.Length == 0)
                Debug.LogWarning("[CloudSeaLife] 子物体里没找到 " + CloudSeaPath.GullPerchPrefix + "*，停栖鸥层空置。", this);
            if (crabEnabled && _crabs.Length == 0)
                Debug.LogWarning("[CloudSeaLife] 子物体里没找到 " + CloudSeaPath.CrabPrefix + "*，螃蟹层空置。", this);
        }

        CrabState BuildCrab(Transform child)
        {
            Vector3 origin = child.localPosition;
            var state = new CrabState
            {
                transform = child,
                basePosition = origin,
                // 脚底离沙的 authored 高差（建模时贴床 +0.02）：爬行时每帧按脚下沙丘高度 + 它重算 Y。
                groundOffset = HasBedGrid ? origin.y - BedHeightAt(origin.x, origin.z) : 0f,
                axis = Range(0f, Mathf.PI * 2f),
                phase = Range(0f, Mathf.PI * 2f),
                patrol = Range(crabPatrolMin, crabPatrolMax),
                speed = Range(crabSpeedMin, crabSpeedMax),
                stepHz = Range(crabStepHzMin, crabStepHzMax),
                yawWobble = Range(0f, 8f),
            };
            // 横向爬行：蟹身 +X 轴（建模前进向）转到「行进方向 + 90°」；
            // authored 随机朝向只留 15% 当个体差异，主体朝向由巡逻轴决定。
            state.bodyYaw = state.axis * Mathf.Rad2Deg + crabSidewaysDeg
                + Mathf.DeltaAngle(0f, child.localRotation.eulerAngles.y) * 0.15f;
            return state;
        }

        FishSchoolState BuildFishSchool(Transform child)
        {
            var state = new FishSchoolState
            {
                transform = child,
                baseRotation = child.localRotation,
            };

            Vector3 origin = child.localPosition;
            state.centerX = origin.x;
            state.centerZ = origin.z;

            // 巡游带以 authored 高度为基准；authored 值越界时先夹进带内再扣浮动余量。
            state.bobAmplitude = Mathf.Min(Range(fishBobAmplitudeMin, fishBobAmplitudeMax),
                Mathf.Max(0.02f, (fishYMax - fishYMin) * 0.25f));
            float baseY = Mathf.Clamp(origin.y, fishYMin + state.bobAmplitude, fishYMax - state.bobAmplitude);
            state.baseY = baseY;

            // 贴床净空 = 鱼群半高 + 余量；取样半径取网格水平半宽的一半（大群跨过沙脊时
            // 多探几个点，不只有中心一点）。
            // 【必须用世界包围盒换算回根空间】FBX 对象自带 90° 轴向旋转与 100 倍缩放，
            // sharedMesh.bounds 是网格本地系的小数（半宽 0.0x）——拿它的 Y 分量去算净空，
            // 要么小到贴沙，要么（乘上缩放后）把群的水平半宽当成半高、整群被顶出水面。
            // 渲染器的 bounds 是世界轴对齐包围盒，除以根的缩放就是根空间的半尺寸。
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            Bounds bounds = renderer != null
                ? renderer.bounds
                : new Bounds(child.position, Vector3.zero);
            float rootScale = Mathf.Max(0.0001f, transform.lossyScale.y);
            Vector3 extent = bounds.extents / rootScale;
            state.bedClearance = extent.y + fishBedClearance;
            state.bedProbeRadius = Mathf.Max(extent.x, extent.z) * 0.5f;

            float radius = Range(fishRadiusMin, fishRadiusMax);
            state.figure8 = Range(0f, 1f) < fishFigure8Chance;
            state.radiusX = radius;
            state.radiusZ = radius * Range(0.75f, 1f);
            state.phaseZ = Range(0f, Mathf.PI * 2f);
            state.angularSpeed = Range(fishSpeedMin, fishSpeedMax) / Mathf.Max(0.5f, radius);
            state.bobFrequency = Mathf.PI * 2f / Mathf.Max(0.5f, Range(fishBobPeriodMin, fishBobPeriodMax));
            state.bobPhase = Range(0f, Mathf.PI * 2f);

            // 初始朝向参考：路径在 t=0 的速度方向（之后只在此参考上做相对偏航，
            // t=0 时刻保持 FBX authored 姿态，不开局甩头）。
            state.yaw0 = FishYaw(state, 0f);
            return state;
        }

        PerchGullState BuildPerchGull(Transform child)
        {
            float amplitude = Range(perchAmplitudeMin, perchAmplitudeMax);
            float period = Mathf.Max(0.5f, Range(perchPeriodMin, perchPeriodMax));
            return new PerchGullState
            {
                transform = child,
                // 脚底即原点：纯旋转组合，脚底顶点不动（原点契约由 FBX 侧保证）。
                baseRotation = child.localRotation,
                amplitude = amplitude,
                frequency = Mathf.PI * 2f / period,
                yawRatio = 0.7f,
                rollRatio = 0.5f,
                yawDetune = 1.13f,
                rollDetune = 0.87f,
                phasePitch = Range(0f, Mathf.PI * 2f),
                phaseYaw = Range(0f, Mathf.PI * 2f),
                phaseRoll = Range(0f, Mathf.PI * 2f),
            };
        }

        // ------------------------------------------------------------------
        // 飞行海鸥生成
        // ------------------------------------------------------------------

        void CreateGullFlock()
        {
            BuildGullMeshes();

            _gullBodyMaterial = CreateGullMaterial("CloudSeaLife_GullBody", PixelartMaterialFactory.Hex("F2EFE6"));
            _gullWingMaterial = CreateGullMaterial("CloudSeaLife_GullWing", PixelartMaterialFactory.Hex("9FA6AD"));
            _gullBeakMaterial = CreateGullMaterial("CloudSeaLife_GullBeak", PixelartMaterialFactory.Hex("E0A63C"));

            // 工厂口径：shader 缺失宁可整层放弃并报错，绝不静默拿品红顶上。
            if (_gullBodyMaterial == null || _gullWingMaterial == null || _gullBeakMaterial == null)
            {
                Debug.LogError("[CloudSeaLife] 海鸥材质创建失败（像素路径 shader 取不到），飞行海鸥层放弃。", this);
                ReleaseGullAssets();
                return;
            }

            _gullContainer = new GameObject("CloudSeaLife_Gulls");
            _gullContainer.transform.SetParent(transform, false);

            var gulls = new List<FlyingGullState>(gullCount);
            for (int i = 0; i < gullCount; i++)
                gulls.Add(BuildGull(i));

            _gulls = gulls.ToArray();
        }

        FlyingGullState BuildGull(int index)
        {
            var root = new GameObject("CloudSeaLife_Gull_" + index);
            root.transform.SetParent(_gullContainer.transform, false);

            // 单位模型 + 均匀缩放：网格与材质全群共享，个体只差 Transform。
            float size = Range(gullSizeMin, gullSizeMax);
            root.transform.localScale = new Vector3(size, size, size);

            AddMeshChild(root.transform, "Body", _gullBodyMesh, _gullBodyMaterial, Vector3.zero);
            AddMeshChild(root.transform, "Head", _gullHeadMesh, _gullBodyMaterial, new Vector3(0f, 0.058f, 0.19f));
            AddMeshChild(root.transform, "Beak", _gullBeakMesh, _gullBeakMaterial, new Vector3(0f, 0.054f, 0.255f));

            // 翼根在肩部；几何从根向外伸（右 +X / 左镜像 -X），拍翅 = 绕局部 Z 滚转。
            Transform wingRight = AddMeshChild(root.transform, "WingRight", _gullWingRightMesh, _gullWingMaterial,
                new Vector3(0.04f, 0.032f, 0.02f));
            Transform wingLeft = AddMeshChild(root.transform, "WingLeft", _gullWingLeftMesh, _gullWingMaterial,
                new Vector3(-0.04f, 0.032f, 0.02f));

            float radius = Range(gullRadiusMin, gullRadiusMax);
            var state = new FlyingGullState
            {
                root = root.transform,
                wingRight = wingRight,
                wingLeft = wingLeft,
                centerX = Range(-1.5f, 1.5f),
                centerZ = Range(-1.5f, 1.5f),
                radiusX = radius,
                radiusZ = radius * Range(0.7f, 1f),
                figure8 = Range(0f, 1f) < gullFigure8Chance,
                phase = Range(0f, Mathf.PI * 2f),
                phaseZ = Range(0f, Mathf.PI * 2f),
                angularSpeed = Range(gullSpeedMin, gullSpeedMax) / Mathf.Max(1f, radius),
                baseY = Range(gullHeightMin, gullHeightMax),
                bobAmplitude = Range(gullBobAmplitudeMin, gullBobAmplitudeMax),
                bobFrequency = Mathf.PI * 2f / Range(3f, 7f),
                bobPhase = Range(0f, Mathf.PI * 2f),
                flapHz = Range(gullFlapHzMin, gullFlapHzMax),
                flapPhase = Range(0f, Mathf.PI * 2f),
                wingAngle = 0f,
                gliding = false,
                glideCountdown = Range(3f, 9f),
                glideRemaining = 0f,
                diving = false,
                diveT = 0f,
            };
            return state;
        }

        static Transform AddMeshChild(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;

            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return child.transform;
        }

        void BuildGullMeshes()
        {
            // 身体：低模椭球（前 +Z 拉长，平面着色硬边，同 MeshBuffers 口径的逐面独立顶点）。
            // 【比例返工（创始人 2026-11-21"翅短身胖"）】真实鸥翼身比 ≈ 5-6:1：
            // 身 0.065×0.052×0.24（瘦长），翼展 0.62/边（总 1.24 ≈ 身长 5 倍）、
            // 根弦收窄、翼尖后掠 0.18 挂出鸥式尖翼；头/喙同步缩一号。
            _gullBodyMesh = BuildEllipsoid(0.065f, 0.052f, 0.24f, 8, 4);
            _gullBodyMesh.name = "CloudSeaLife_GullBody";
            _gullHeadMesh = BuildEllipsoid(0.052f, 0.048f, 0.058f, 7, 3);
            _gullHeadMesh.name = "CloudSeaLife_GullHead";
            _gullBeakMesh = BuildCone(0.018f, 0.12f, 6);
            _gullBeakMesh.name = "CloudSeaLife_GullBeak";
            _gullWingRightMesh = BuildWing(0.62f, 0.12f, 0.028f, 0.18f, mirrored: false);
            _gullWingRightMesh.name = "CloudSeaLife_GullWingRight";
            _gullWingLeftMesh = BuildWing(0.62f, 0.12f, 0.028f, 0.18f, mirrored: true);
            _gullWingLeftMesh.name = "CloudSeaLife_GullWingLeft";
        }

        /// <summary>
        /// 海鸥材质：先走工厂配方；工厂失败（shader 取不到）按名再取一次并套同一配方——
        /// 两次都失败返回 null，调用方整层放弃并报错。
        /// </summary>
        static Material CreateGullMaterial(string name, Color albedo)
        {
            Material material = PixelartMaterialFactory.Create(name, albedo);
            if (material != null)
                return material;

            Shader shader = PixelartShaders.Load(PixelartPath.ObjectShaderName);
            if (shader == null)
                return null;

            material = new Material(shader) { name = name };
            PixelartMaterialFactory.Configure(material, albedo);
            return material;
        }

        // ------------------------------------------------------------------
        // 每帧驱动（零堆分配：只 for 循环 + 三角函数 + Transform 写回）
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            TickFarClouds(dt);

            if (_schools != null)
            {
                for (int i = 0; i < _schools.Length; i++)
                    TickFish(_schools[i], dt);
            }

            if (_perches != null)
            {
                for (int i = 0; i < _perches.Length; i++)
                    TickPerch(_perches[i], dt);
            }

            if (_crabs != null)
            {
                for (int i = 0; i < _crabs.Length; i++)
                    TickCrab(_crabs[i], dt);
            }

            if (_gulls != null && _gulls.Length > 0)
            {
                TickDiveScheduler(dt);
                for (int i = 0; i < _gulls.Length; i++)
                    TickGull(_gulls[i], dt);
            }
        }

        // ---- 海底螃蟹 ----

        /// <summary>横向爬行：沿一条随机直线往复（sin 端点自然减速），蟹身侧对行进方向，
        /// 步足抬落用 1 cm 级 Y 抖动 + 4° 偏航摆动，静止帧也读得出「在走」。
        /// 实例方法：有海床高度格时脚底贴着沙丘起伏（没格时停在 authored 高度）。</summary>
        void TickCrab(CrabState c, float dt)
        {
            c.time += dt;
            float u = c.phase + c.time * (c.speed / Mathf.Max(0.05f, c.patrol));
            float s = Mathf.Sin(u);
            float step = Mathf.Abs(Mathf.Sin(c.time * c.stepHz * Mathf.PI * 2f));

            Vector3 p = c.basePosition;
            p.x += Mathf.Cos(c.axis) * s * c.patrol;
            p.z += Mathf.Sin(c.axis) * s * c.patrol;
            if (HasBedGrid)
                p.y = BedHeightAt(p.x, p.z) + c.groundOffset;
            p.y += step * 0.008f;
            c.transform.localPosition = p;

            float yaw = c.bodyYaw + Mathf.Sin(c.time * c.stepHz * Mathf.PI * 2f) * 4f
                + step * c.yawWobble;
            c.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ---- 鱼群 ----

        static float FishYaw(FishSchoolState s, float u)
        {
            // 解析速度（u 对时间求导后乘角速度 w，方向与 w 符号无关此处只取方向）。
            float w = s.angularSpeed;
            float dx = s.radiusX * Mathf.Cos(u) * w;
            float dz = s.radiusZ * ZFreq(s) * Mathf.Cos(ZFreq(s) * u + s.phaseZ) * w;
            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        static float ZFreq(FishSchoolState s)
        {
            return s.figure8 ? 2f : 1f;
        }

        // 实例方法：巡游带参数（fishYMin/Max 等）与海床高度格是序列化实例字段。
        void TickFish(FishSchoolState s, float dt)
        {
            s.time += dt;
            s.u += s.angularSpeed * dt;

            float x = s.centerX + s.radiusX * Mathf.Sin(s.u);
            float z = s.centerZ + s.radiusZ * Mathf.Sin(ZFreq(s) * s.u + s.phaseZ);
            float y = s.baseY + s.bobAmplitude * Mathf.Sin(s.time * s.bobFrequency + s.bobPhase);
            // 不穿海面 / 海床是几何保证：上沿夹巡游带，下沿贴脚下沙丘 + 净空；
            // 没烘高度格时退回固定深度带。沙丘高到巡游带里时以「不出水」优先（夹下沿）。
            float floor = HasBedGrid ? BedFloor(s, x, z) : fishYMin;
            s.transform.localPosition = new Vector3(x, Mathf.Clamp(y, Mathf.Min(floor, fishYMax), fishYMax), z);
            // 群整体朝速度方向缓慢偏航（相对初始速度方向，开局不甩头）。
            float yawDelta = Mathf.DeltaAngle(s.yaw0, FishYaw(s, s.u));
            s.transform.localRotation = s.baseRotation * Quaternion.Euler(0f, yawDelta, 0f);
        }

        // ------------------------------------------------------------------
        // 海床高度格
        // ------------------------------------------------------------------

        /// <summary>写进海床高度格（CloudSeaAssetBaker 烘焙时调用；产物见 <see cref="CloudSeaBedGrid.Build"/>）。</summary>
        public void SetBedHeightGrid(float[] heights, int cells, float halfSize)
        {
            bedHeights = heights ?? new float[0];
            bedGridCells = cells;
            bedGridHalfSize = halfSize;
        }

        /// <summary>高度格齐全才启用贴床（长度对不上 = 没烘或烘坏了，退回固定带而不是读越界）。</summary>
        bool HasBedGrid =>
            bedGridCells >= 1 && bedHeights != null && bedHeights.Length == (bedGridCells + 1) * (bedGridCells + 1);

        float BedHeightAt(float x, float z)
        {
            return CloudSeaBedGrid.Sample(bedHeights, bedGridCells, bedGridHalfSize, x, z);
        }

        /// <summary>鱼群下沿：中心与前后左右四个偏移点里最高的沙丘，再加净空。</summary>
        float BedFloor(FishSchoolState s, float x, float z)
        {
            float highest = BedHeightAt(x, z);
            float r = s.bedProbeRadius;
            highest = Mathf.Max(highest, BedHeightAt(x + r, z));
            highest = Mathf.Max(highest, BedHeightAt(x - r, z));
            highest = Mathf.Max(highest, BedHeightAt(x, z + r));
            highest = Mathf.Max(highest, BedHeightAt(x, z - r));
            return highest + s.bedClearance;
        }

        // ---- 停栖鸥 ----

        static void TickPerch(PerchGullState p, float dt)
        {
            p.time += dt;
            float pitch = p.amplitude * Mathf.Sin(p.time * p.frequency + p.phasePitch);
            float yaw = p.amplitude * p.yawRatio * Mathf.Sin(p.time * p.frequency * p.yawDetune + p.phaseYaw);
            float roll = p.amplitude * p.rollRatio * Mathf.Sin(p.time * p.frequency * p.rollDetune + p.phaseRoll);
            p.transform.localRotation = p.baseRotation * Quaternion.Euler(pitch, yaw, roll);
        }

        // ---- 飞行海鸥 ----

        /// <summary>全局俯冲调度：每 8-15s 随机挑一只不在俯冲的海鸥入水掠一次。</summary>
        void TickDiveScheduler(float dt)
        {
            if (!diveEnabled)
                return;

            _diveCountdown -= dt;
            if (_diveCountdown > 0f)
                return;

            _diveCountdown = Range(diveIntervalMin, diveIntervalMax);

            int start = _rng.Next(_gulls.Length);
            for (int k = 0; k < _gulls.Length; k++)
            {
                FlyingGullState candidate = _gulls[(start + k) % _gulls.Length];
                if (!candidate.diving)
                {
                    candidate.diving = true;
                    candidate.diveT = 0f;
                    return;
                }
            }
        }

        /// <summary>海鸥在时刻 t（含俯冲包络）的本地位置——俯冲只改这条路径的 Y 参数。</summary>
        Vector3 EvaluateGull(FlyingGullState g, float t)
        {
            float u = g.phase + g.angularSpeed * t;
            float zFreq = g.figure8 ? 2f : 1f;
            float x = g.centerX + g.radiusX * Mathf.Sin(u);
            float z = g.centerZ + g.radiusZ * Mathf.Sin(zFreq * u + g.phaseZ);
            float cruiseY = g.baseY + g.bobAmplitude * Mathf.Sin(t * g.bobFrequency + g.bobPhase);

            float y = cruiseY;
            if (g.diving)
            {
                float p = Mathf.Clamp01(g.diveT / Mathf.Max(0.1f, diveDuration));
                y = Mathf.Lerp(cruiseY, diveY, SmoothBell(p));
            }
            return new Vector3(x, y, z);
        }

        // 实例方法：拍翅/滑翔/俯冲参数是序列化实例字段。
        void TickGull(FlyingGullState g, float dt)
        {
            g.time += dt;

            // 滑翔节奏：飞一阵停拍一阵（停在双翼微抬的姿态）。
            if (g.gliding)
            {
                g.glideRemaining -= dt;
                if (g.glideRemaining <= 0f)
                {
                    g.gliding = false;
                    g.glideCountdown = Range(4f, 11f);
                }
            }
            else
            {
                g.glideCountdown -= dt;
                if (g.glideCountdown <= 0f)
                {
                    g.gliding = true;
                    g.glideRemaining = Range(1f, 2.5f);
                }
            }

            if (g.diving)
            {
                g.diveT += dt;
                if (g.diveT >= diveDuration)
                    g.diving = false;
            }

            Vector3 position = EvaluateGull(g, g.time);
            // 朝向 = 路径速度（含俯冲的竖直分量，自然低头/抬头）；差分步长固定，无分配。
            Vector3 velocity = (EvaluateGull(g, g.time + 0.03f) - position) / 0.03f;

            g.root.localPosition = position;
            if (velocity.sqrMagnitude > 1e-8f)
                g.root.localRotation = Quaternion.LookRotation(velocity);

            // 拍翅：正弦目标角；滑翔时目标角换成停拍姿态，wingAngle 指数趋近（帧率无关）。
            float target;
            if (g.gliding)
            {
                target = gullGlideAngleDegrees;
            }
            else
            {
                g.flapPhase += dt * Mathf.PI * 2f * g.flapHz;
                target = Mathf.Sin(g.flapPhase) * gullFlapAmplitudeDegrees;
            }
            g.wingAngle += (target - g.wingAngle) * (1f - Mathf.Exp(-12f * dt));
            // 右翼（+X）抬 = 绕 +Z 正角；左翼镜像，取反。
            g.wingRight.localRotation = Quaternion.Euler(0f, 0f, g.wingAngle);
            g.wingLeft.localRotation = Quaternion.Euler(0f, 0f, -g.wingAngle);
        }

        /// <summary>钟形包络：0→1→0 平滑（俯冲下扎—贴水—拉起）。</summary>
        static float SmoothBell(float p)
        {
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.42f));
            float down = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 0.58f) / 0.42f));
            return up * down;
        }

        // ------------------------------------------------------------------
        // 确定性随机（模块口径：同 seed 同布局，不用全局 UnityEngine.Random）
        // ------------------------------------------------------------------

        float Range(float min, float max)
        {
            if (max < min)
                (min, max) = (max, min);
            return min + (max - min) * (float)_rng.NextDouble();
        }

        // ------------------------------------------------------------------
        // 低模网格（运行时一次性构建，平面着色：逐面独立顶点，硬边低模风）
        // ------------------------------------------------------------------

        static void PushTri(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-16f)
                return;   // 退化面直接丢（零面积面对渲染无意义）
            normal.Normalize();

            int baseIndex = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
        }

        /// <summary>
        /// 低模椭球（经纬分环，平面着色）。绕序已按外法线校验：四边面 (p00,p10,p11,p01)、
        /// 底盖三角 (p00,p10,p11)、顶盖三角 (p00,p10,p01)。
        /// </summary>
        static Mesh BuildEllipsoid(float radiusX, float radiusY, float radiusZ, int segments, int rings)
        {
            segments = Mathf.Max(3, segments);
            rings = Mathf.Max(2, rings);

            var vertices = new List<Vector3>(segments * rings * 6);
            var normals = new List<Vector3>(vertices.Capacity);
            var triangles = new List<int>(vertices.Capacity);

            Vector3 Point(float theta, float phi)
            {
                float ct = Mathf.Cos(theta);
                float cp = Mathf.Cos(phi);
                float sp = Mathf.Sin(phi);
                return new Vector3(radiusX * ct * cp, radiusY * Mathf.Sin(theta), radiusZ * ct * sp);
            }

            for (int i = 0; i < rings; i++)
            {
                float theta0 = -Mathf.PI * 0.5f + Mathf.PI * i / rings;
                float theta1 = -Mathf.PI * 0.5f + Mathf.PI * (i + 1) / rings;

                for (int j = 0; j < segments; j++)
                {
                    float phi0 = Mathf.PI * 2f * j / segments;
                    float phi1 = Mathf.PI * 2f * (j + 1) / segments;

                    Vector3 p00 = Point(theta0, phi0);
                    Vector3 p01 = Point(theta0, phi1);
                    Vector3 p10 = Point(theta1, phi0);
                    Vector3 p11 = Point(theta1, phi1);

                    if (i == 0)
                    {
                        // 底盖（theta0 = -90°，p00/p01 均为极点）：三角。
                        PushTri(vertices, normals, triangles, p00, p10, p11);
                    }
                    else if (i == rings - 1)
                    {
                        // 顶盖（theta1 = +90°，p10/p11 均为极点）：三角。
                        PushTri(vertices, normals, triangles, p00, p10, p01);
                    }
                    else
                    {
                        PushTri(vertices, normals, triangles, p00, p10, p11);
                        PushTri(vertices, normals, triangles, p00, p11, p01);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        /// <summary>低模圆锥（喙）：轴沿 +Z、底面在 z=0、尖在 z=length。底面不封（藏在头椭球里）。</summary>
        static Mesh BuildCone(float radius, float length, int segments)
        {
            segments = Mathf.Max(3, segments);

            var vertices = new List<Vector3>(segments * 3);
            var normals = new List<Vector3>(vertices.Capacity);
            var triangles = new List<int>(vertices.Capacity);

            Vector3 apex = new Vector3(0f, 0f, length);
            for (int i = 0; i < segments; i++)
            {
                float phi0 = Mathf.PI * 2f * i / segments;
                float phi1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector3 r0 = new Vector3(Mathf.Cos(phi0) * radius, Mathf.Sin(phi0) * radius, 0f);
                Vector3 r1 = new Vector3(Mathf.Cos(phi1) * radius, Mathf.Sin(phi1) * radius, 0f);
                // 侧面三角 (r0, r1, apex)，绕序推出外法线。
                PushTri(vertices, normals, triangles, r0, r1, apex);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        /// <summary>
        /// 三角翼：翼根弦沿 Z（前缘在 +Z 侧）、翼尖向外 <paramref name="span"/> 并后掠/微抬。
        /// 零厚度面片**正反各写一份**（背面剔除会吃掉单面；等距俯视下翼的底面随时可见）。
        /// <paramref name="mirrored"/> = 左翼（X 取镜像、绕序翻转保持法线正确）。
        /// </summary>
        static Mesh BuildWing(float span, float rootChord, float tipLift, float sweepBack, bool mirrored)
        {
            Vector3 rootFront = new Vector3(0f, 0f, rootChord * 0.45f);
            Vector3 rootBack = new Vector3(0f, 0f, -rootChord * 0.55f);
            Vector3 tip = new Vector3(span, tipLift, -sweepBack);

            var vertices = new List<Vector3>(6);
            var normals = new List<Vector3>(6);
            var triangles = new List<int>(6);

            Vector3 S(Vector3 p)
            {
                return mirrored ? new Vector3(-p.x, p.y, p.z) : p;
            }

            // 上表面（法线朝上）+ 下表面（镜像绕序、法线朝下）。
            PushTri(vertices, normals, triangles, S(rootFront), S(tip), S(rootBack));
            PushTri(vertices, normals, triangles, S(rootFront), S(rootBack), S(tip));

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // ------------------------------------------------------------------
        // 释放
        // ------------------------------------------------------------------

        void OnDestroy()
        {
            // FBX 子物体（鱼群/停栖鸥）属于预制体，只动过 Transform，不销毁。
            if (_gullContainer != null)
                Destroy(_gullContainer);

            ReleaseGullAssets();

            _schools = null;
            _perches = null;
            _gulls = null;
        }

        void ReleaseGullAssets()
        {
            if (_gullBodyMesh != null) Destroy(_gullBodyMesh);
            if (_gullHeadMesh != null) Destroy(_gullHeadMesh);
            if (_gullBeakMesh != null) Destroy(_gullBeakMesh);
            if (_gullWingRightMesh != null) Destroy(_gullWingRightMesh);
            if (_gullWingLeftMesh != null) Destroy(_gullWingLeftMesh);
            if (_gullBodyMaterial != null) Destroy(_gullBodyMaterial);
            if (_gullWingMaterial != null) Destroy(_gullWingMaterial);
            if (_gullBeakMaterial != null) Destroy(_gullBeakMaterial);

            _gullBodyMesh = null;
            _gullHeadMesh = null;
            _gullBeakMesh = null;
            _gullWingRightMesh = null;
            _gullWingLeftMesh = null;
            _gullBodyMaterial = null;
            _gullWingMaterial = null;
            _gullBeakMaterial = null;
        }

        // ------------------------------------------------------------------
        // 个体状态（Start 一次性建，Update 只读字段——不是 MonoBehaviour）
        // ------------------------------------------------------------------

        sealed class FishSchoolState
        {
            public Transform transform;
            public Quaternion baseRotation;
            public float centerX;
            public float centerZ;
            public float baseY;
            public float radiusX;
            public float radiusZ;
            public bool figure8;
            public float phaseZ;
            public float angularSpeed;
            public float bobAmplitude;
            public float bobFrequency;
            public float bobPhase;
            public float yaw0;
            public float time;
            public float u;
            public float bedClearance;    // 鱼群下沿离脚下沙丘的净空（网格半高 + 余量）
            public float bedProbeRadius;  // 查沙丘高度时前后左右偏移点的半径
        }

        sealed class PerchGullState
        {
            public Transform transform;
            public Quaternion baseRotation;
            public float amplitude;
            public float frequency;
            public float yawRatio;
            public float rollRatio;
            public float yawDetune;
            public float rollDetune;
            public float phasePitch;
            public float phaseYaw;
            public float phaseRoll;
            public float time;
        }

        sealed class CrabState
        {
            public Transform transform;
            public Vector3 basePosition;
            public float groundOffset;    // 脚底离脚下沙丘的高差（爬行时 Y = 沙丘高度 + 它）
            public float axis;
            public float phase;
            public float patrol;
            public float speed;
            public float stepHz;
            public float bodyYaw;
            public float yawWobble;
            public float time;
        }

        sealed class FlyingGullState
        {
            public Transform root;
            public Transform wingRight;
            public Transform wingLeft;
            public float centerX;
            public float centerZ;
            public float radiusX;
            public float radiusZ;
            public bool figure8;
            public float phase;
            public float phaseZ;
            public float angularSpeed;
            public float baseY;
            public float bobAmplitude;
            public float bobFrequency;
            public float bobPhase;
            public float flapHz;
            public float flapPhase;
            public float wingAngle;
            public bool gliding;
            public float glideCountdown;
            public float glideRemaining;
            public bool diving;
            public float diveT;
            public float time;
        }
    }
}
