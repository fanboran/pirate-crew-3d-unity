using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步场景的**活物层**：水下生物（鱼群 / 大鱼 / 水母 / 蟹 / 海星……）+ 海鸥群（飞行 + 停栖）。
    ///
    /// 【三方契约】水下生物 / 停栖鸥对象由 FBX 带入（烘焙侧按 <see cref="CloudSeaPath"/> 的
    /// <c>Cloud_Creature_&lt;物种&gt;_&lt;序号&gt;</c> / <c>Cloud_GullPerch_*</c> 命名），本组件 Start 时
    /// **只在自己的子树里按前缀找**（不用任何 Find 系列全场景搜索），再按物种名
    /// （<see cref="CloudSeaCreatureSpecies"/>）分派控制器；飞行海鸥则全部由本组件在运行时纯代码
    /// 生成（低模网格 + 像素路径材质），不进 FBX、不进资产。
    ///
    /// 【骨架在这里接上】每只生物对象下挂着 Blender 侧登记的关节空物体（<c>J&lt;关节号&gt;_…</c>），
    /// 网格的三套 UV 带「关节号 + 权重」。Start 时把它们读成 <see cref="CloudSeaRig"/>，按物种建一个
    /// 运动学控制器 <see cref="CloudSeaBrain"/>（纯 C#：两骨 IK / FABRIK / Verlet 链 / 弹簧 / 踩地步态，
    /// 验证台可测）。Update 里控制器解出每个关节的矩阵，写进一个全局 <see cref="ComputeBuffer"/>，
    /// 水下 shader 自己蒙皮（CloudSeaAnim.hlsl 的 CloudSeaSkin）——不走 SkinnedMeshRenderer，
    /// 不用 MaterialPropertyBlock（水下材质按颜色共享，SRP Batcher 也不许）。
    ///
    /// 【谁管整只的位移】鱼群 / 单体游泳者 / 水母的**水平路径**在这里（解析的圆 / 8 字 + 漂移），
    /// 控制器只管肢体；蟹 / 海星 / 章鱼 / 海马 / 海葵对象不动，整只的位移在骨架里做（脚要踩在
    /// 世界里的锚点上）。水母的升沉由控制器推出来（发放—松弛），这里只把结果套到对象上。
    ///
    /// 【坐标系】组件挂在 CloudWalk 预制体根（世界 (20,0,15)，本地原点 = 竞技场中心，
    /// 根无旋转缩放 ⇒ 本地 Y == 世界 Y）。水面 y=-0.4（<see cref="waterY"/>），云台顶 2.5..7.5；
    /// 掠水俯冲高度（<see cref="diveY"/>）也是**本地 Y**。生物建模面朝本地 +X。
    ///
    /// 【贴床定深】海床是起伏沙丘（脊顶与洼地差近 1 米）。烘焙器把海床网格烘成一张高度格
    /// 写进本组件（<see cref="CloudSeaBedGrid"/>）：游泳的生物每帧查「脚下沙丘多高」——下沿 =
    /// 沙丘最高处 + 净空 + 物种自身的下伸，上沿 = 水面 − 余量 − 物种自身的上伸；踩地的生物
    /// （蟹腿、海星腕、章鱼腕）落点时查沙面高度。不穿海面 / 海床是几何保证，不是调参凑的。
    /// 没烘格时（单独预览 prefab）游泳的退回固定深度带 <see cref="bedYMin"/>，踩地的当成平地。
    ///
    /// 【运动形状】路径全是解析参数（圆 / 8 字 + 正弦浮动），没有碰撞体、没有刚体；朝向由解析
    /// 速度推出，并以有限角速度追随（掉头是转过去，不是瞬间跳）。大鱼转弯时朝圈内侧侧倾、
    /// 升降时俯仰。俯冲只改路径参数（在巡航高度与掠水高度之间走一个平滑钟形插值），水平轨迹照走不误。
    ///
    /// 【性能口径】Start 一次性建齐（查子物体 / 读骨架 / 建网格 / 建材质 / 抽随机参数），Update 是
    /// **唯一入口**（海鸥不是 MonoBehaviour，与 AmbientDirector 同一手法定期驱动）：每帧只有
    /// 三角函数、控制器的浮点运算、一次关节缓冲上传和 Transform 写回，**零堆分配**；随机走
    /// <see cref="System.Random"/>（<see cref="randomSeed"/> 播种，同 seed 同布局，不用全局的 UnityEngine.Random）。
    ///
    /// 【静态批合红线】被本组件驱动的 FBX 对象（生物 / 停栖鸥）**不可 isStatic**——静态批合的
    /// 物体不响应 Transform（烘焙侧保证，同 <c>FloatingIslandDrift</c> 的口径）。
    ///
    /// 【开关生效时机】开关（水下生物 / 海鸥 / 停栖）只在 Start 读一次：运行中改值不建也不删，
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
        [Header("水下生物（FBX: Cloud_Creature_<物种>_<序号>；骨架 + 运动学控制器）")]
        [Tooltip("水下生物总开关：关 = 不读骨架、不驱动，它们停在 FBX 的静止姿态")]
        [SerializeField] bool creaturesEnabled = true;
        [Tooltip("水面高度（本地 Y）：生物的上伸不越过它减去余量")]
        [SerializeField] float waterY = -0.4f;
        [Tooltip("上沿离水面的余量（米）")]
        [SerializeField] float surfaceClearance = 0.08f;
        [Tooltip("贴底净空（米）：游泳者的下沿离脚下沙丘的距离，另加物种自身的下伸")]
        [SerializeField] float bedClearance = 0.12f;
        [Tooltip("固定深度带下沿（本地 Y）：只在没有海床高度格时生效（固定深度带兜底）")]
        [SerializeField] float bedYMin = -2.4f;
        [Tooltip("鱼群 8 字路径占比（0..1，其余走圆）")]
        [SerializeField, Range(0f, 1f)] float schoolFigure8Chance = 0.45f;
        [Tooltip("巡游圈半径倍率下/上限（乘物种基准半径；鱼群与游泳者共用）")]
        [SerializeField] float swimRadiusScaleMin = 0.9f;
        [SerializeField] float swimRadiusScaleMax = 1.2f;
        [Tooltip("巡游速度倍率下/上限（乘物种基准速度）")]
        [SerializeField] float swimSpeedScaleMin = 0.85f;
        [SerializeField] float swimSpeedScaleMax = 1.2f;
        [Tooltip("侧倾角（度）= 物种系数 × 线速度 × 角速度 × 本值：剑鱼 / 魔鬼鱼转弯时内侧翼下压")]
        [SerializeField] float bankScale = 40f;
        [Tooltip("朝向追随的角速度上限（度/秒）：所有会转身的生物共用")]
        [SerializeField] float turnRateDegPerSec = 240f;

        [Tooltip("飞行海鸥总开关")]
        [SerializeField] bool gullsEnabled = true;
        [Tooltip("停栖海鸥摆动总开关")]
        [SerializeField] bool perchSwayEnabled = true;
        [Tooltip("海鸥掠水俯冲总开关")]
        [SerializeField] bool diveEnabled = true;

        [Header("海床高度格（烘焙器写入，勿手改）")]
        [SerializeField, HideInInspector] int bedGridCells = 40;
        [SerializeField, HideInInspector] float bedGridHalfSize = 20f;
        [SerializeField, HideInInspector] float[] bedHeights = new float[0];
        [SerializeField, HideInInspector] float[] bedGround = new float[0];

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

        CreatureState[] _creatures;
        ComputeBuffer _jointBuffer;         // 全局蒙皮缓冲（_CloudSeaJoints）：每个关节三行
        Vector4[] _jointRows;               // 与缓冲等长的暂存，各控制器往里写、每帧整块上传
        CloudSeaBed _bedSampler;            // 海床高度采样（控制器找落点用）；没有高度格时为 null
        PerchGullState[] _perches;
        FlyingGullState[] _gulls;
        float _diveCountdown;

        // 出图诊断：环境变量 PC3D_CLOUDSEA_POSE_DUMP=<文件路径> 时，模拟时间过 PC3D_CLOUDSEA_POSE_TIME
        // （默认 8 秒）的那一帧把全部生物的关节坐标落盘一次。步态对不对先读这份坐标，不从画面反推。
        const string PoseDumpPathKey = "PC3D_CLOUDSEA_POSE_DUMP";
        const string PoseDumpTimeKey = "PC3D_CLOUDSEA_POSE_TIME";
        string _poseDumpPath;
        float _poseDumpTime;
        bool _poseDumped;
        float _dtMax;

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

        void OnEnable()
        {
            CloudSeaSkin.BindRest();
        }

        void OnDisable()
        {
            ReleaseJointBuffer();
        }

        void Start()
        {
            _rng = new System.Random(randomSeed);

            GatherFbxCreatures();
            GatherFarClouds();
            CreateJointBuffer();

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

        /// <summary>
        /// 只遍历自己的子树，按前缀收集水下生物与停栖鸥（前缀契约在 <see cref="CloudSeaPath"/>）。
        /// 生物按物种名分派控制器（<see cref="CloudSeaBrain"/>）；表里没有的物种逐个报错、停在静止姿态——
        /// 那是 Blender 侧与 C# 两侧的物种表对不上，不能悄悄当成别的东西。
        /// </summary>
        void GatherFbxCreatures()
        {
            var creatures = new List<CreatureState>();
            var perches = new List<PerchGullState>();
            Transform[] children = GetComponentsInChildren<Transform>(true);
            // 控制器找落点用的是贴地的那张（脚要踩在真实沙面上）；没烘贴地格就退回净空格。
            CloudSeaBed sampler = null;
            if (HasBedGround)
                sampler = new CloudSeaBed(GroundHeightAt);
            else if (HasBedGrid)
                sampler = new CloudSeaBed(BedHeightAt);
            _bedSampler = sampler;

            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == transform)
                    continue;

                if (creaturesEnabled && child.name.StartsWith(CloudSeaPath.CreaturePrefix, System.StringComparison.Ordinal))
                {
                    string species = null;
                    var profile = default(CloudSeaCreatureSpecies.Profile);
                    bool known = CloudSeaCreatureSpecies.TryParse(child.name, out species)
                        && CloudSeaCreatureSpecies.TryGet(species, out profile);
                    if (!known)
                        Debug.LogError("[CloudSeaLife] 不认识的水下生物 " + child.name
                            + "：物种名不在 CloudSeaCreatureSpecies 的表里，它停在静止姿态。", child);
                    creatures.Add(BuildCreature(child, species ?? child.name,
                        known ? profile : (CloudSeaCreatureSpecies.Profile?)null));
                }

                if (perchSwayEnabled && child.name.StartsWith(CloudSeaPath.GullPerchPrefix, System.StringComparison.Ordinal))
                    perches.Add(BuildPerchGull(child));
            }

            _creatures = creatures.ToArray();
            _perches = perches.ToArray();

            // 缺对象要响亮（FBX 没导入 / 名字改了是烘焙侧契约破裂，不静默）。
            if (creaturesEnabled && _creatures.Length == 0)
            {
                Debug.LogWarning("[CloudSeaLife] 子物体里没找到 " + CloudSeaPath.CreaturePrefix
                    + "*，水下生物层空置。", this);
            }
            if (perchSwayEnabled && _perches.Length == 0)
                Debug.LogWarning("[CloudSeaLife] 子物体里没找到 " + CloudSeaPath.GullPerchPrefix + "*，停栖鸥层空置。", this);
        }

        /// <summary>
        /// 读骨架：对象下名字以 J 开头的空物体 = 关节（J&lt;关节号&gt;_&lt;角色&gt;；有父关节时是
        /// J&lt;关节号&gt;_P&lt;父关节号&gt;_&lt;角色&gt;，烘焙器的 VerifyCreatureJoints 是同一套读法的门禁）。
        /// 静止位置取物体空间的本地位置（导入后是「x 前、y 上、z 侧」、米）。按关节号升序排——父关节号
        /// 一定小于子关节号，所以排完就是「父在子前」。骨架读不成人（父子顺序不对等）时报错并给空骨架。
        /// </summary>
        static CloudSeaRig ReadRig(Transform creature, string species)
        {
            var entries = new List<KeyValuePair<int, Transform>>();
            foreach (Transform child in creature)
            {
                if (!child.name.StartsWith("J", System.StringComparison.Ordinal))
                    continue;
                int underscore = child.name.IndexOf('_');
                int index;
                if (underscore > 1 && int.TryParse(child.name.Substring(1, underscore - 1), out index) && index >= 0)
                    entries.Add(new KeyValuePair<int, Transform>(index, child));
            }
            entries.Sort((a, b) => a.Key.CompareTo(b.Key));

            int count = entries.Count;
            var roles = new string[count];
            var rest = new Vector3[count];
            var parent = new int[count];
            var slot = new int[count];
            for (int j = 0; j < count; j++)
            {
                Transform joint = entries[j].Value;
                string tail = joint.name.Substring(joint.name.IndexOf('_') + 1);
                int parentIndex = -1;
                if (tail.StartsWith("P", System.StringComparison.Ordinal))
                {
                    int next = tail.IndexOf('_');
                    int parentSlot;
                    if (next > 1 && int.TryParse(tail.Substring(1, next - 1), out parentSlot))
                    {
                        parentIndex = entries.FindIndex(entry => entry.Key == parentSlot);
                        tail = tail.Substring(next + 1);
                    }
                }
                roles[j] = tail;
                rest[j] = creature.InverseTransformPoint(joint.position);
                parent[j] = parentIndex;
                slot[j] = entries[j].Key;
            }
            try
            {
                return new CloudSeaRig(species, roles, rest, parent, slot);
            }
            catch (System.ArgumentException e)
            {
                Debug.LogError("[CloudSeaLife] " + creature.name + " 的骨架读不起来，它停在静止姿态：" + e.Message,
                    creature);
                return new CloudSeaRig(species, new string[0], new Vector3[0], new int[0], new int[0]);
            }
        }

        /// <summary>按骨架里最大的关节号建蒙皮缓冲（一个关节三行），先填一遍静止姿态并绑成全局。</summary>
        void CreateJointBuffer()
        {
            int count = 0;
            for (int i = 0; i < _creatures.Length; i++)
            {
                CloudSeaRig rig = _creatures[i].Rig;
                for (int j = 0; j < rig.Count; j++)
                    count = Mathf.Max(count, rig.Slot[j] + 1);
            }
            if (count == 0)
                return;

            _jointRows = new Vector4[count * 3];
            for (int j = 0; j < count; j++)
            {
                _jointRows[j * 3] = new Vector4(1f, 0f, 0f, 0f);
                _jointRows[j * 3 + 1] = new Vector4(0f, 1f, 0f, 0f);
                _jointRows[j * 3 + 2] = new Vector4(0f, 0f, 1f, 0f);
            }
            _jointBuffer = CloudSeaSkin.CreateBuffer(count);
            _jointBuffer.SetData(_jointRows);
            Shader.SetGlobalBuffer(CloudSeaSkin.JointsId, _jointBuffer);
            Shader.SetGlobalFloat(CloudSeaSkin.OnId, 1f);
        }

        void ReleaseJointBuffer()
        {
            if (_jointBuffer != null)
            {
                _jointBuffer.Release();
                _jointBuffer = null;
            }
            _jointRows = null;
            CloudSeaSkin.BindRest();
        }

        /// <summary>
        /// 一只生物：读骨架、建控制器，再按行为族定水平路径的参数。
        /// 上下沿由渲染器包围盒换算——群原点（= 巡游高度基准）不一定在包围盒正中，上下沿各取真实距离。
        /// 【必须用渲染器的世界包围盒】FBX 对象可能自带轴向旋转与缩放，sharedMesh.bounds 是
        /// 网格本地系的数——拿它的 Y 分量去算净空，要么小到贴沙，要么把群的水平半宽当成半高、
        /// 整群被顶出水面。渲染器的 bounds 是世界轴对齐包围盒，换回父空间再用。
        /// </summary>
        CreatureState BuildCreature(Transform child, string species, CloudSeaCreatureSpecies.Profile? profile)
        {
            var state = new CreatureState
            {
                transform = child,
                Rig = ReadRig(child, species),
                centerX = child.localPosition.x,
                centerZ = child.localPosition.z,
                baseY = child.localPosition.y,
            };
            state.Brain = CloudSeaBrain.Create(state.Rig, (float)_rng.NextDouble());
            if (profile == null)
                return state;
            CloudSeaCreatureSpecies.Profile p = profile.Value;

            state.Kind = p.Kind;
            state.bottomExtent = p.Bottom;
            state.ceiling = waterY - surfaceClearance - p.Top;
            state.probeRadius = p.Radius;
            Vector3 origin = child.localPosition;
            float floor = bedYMin;

            switch (p.Kind)
            {
                case CloudSeaCreatureKind.School:
                {
                    MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                    Bounds bounds = renderer != null ? renderer.bounds : new Bounds(child.position, Vector3.zero);
                    Transform frame = child.parent != null ? child.parent : child;
                    Vector3 min = frame.InverseTransformPoint(bounds.min);
                    Vector3 max = frame.InverseTransformPoint(bounds.max);
                    state.bottomExtent = origin.y - min.y;
                    state.ceiling = waterY - surfaceClearance - (max.y - origin.y);
                    // 取样半径取网格水平半宽的一半（大群跨过沙脊时多探几个点，不只有中心一点）。
                    state.probeRadius = Mathf.Max(max.x - min.x, max.z - min.z) * 0.5f;

                    float radius = p.Radius * Range(swimRadiusScaleMin, swimRadiusScaleMax);
                    state.figure8 = Range(0f, 1f) < schoolFigure8Chance;
                    state.radiusX = radius;
                    state.radiusZ = radius * Range(0.75f, 1f);
                    state.phaseZ = Range(0f, Mathf.PI * 2f);
                    state.u = Range(0f, Mathf.PI * 2f);
                    state.angularSpeed = p.Speed * Range(swimSpeedScaleMin, swimSpeedScaleMax)
                        / Mathf.Max(0.5f, radius);
                    state.bobFrequency = Mathf.PI * 2f / Mathf.Max(0.5f, Range(3f, 6f));
                    state.bobPhase = Range(0f, Mathf.PI * 2f);
                    // 开局朝向 = 当前速度方向，不从零度甩过去。
                    state.yaw = CloudSeaCreatureSpecies.HeadingOf(
                        state.radiusX * Mathf.Cos(state.u),
                        state.radiusZ * ZFreq(state) * Mathf.Cos(ZFreq(state) * state.u + state.phaseZ));
                    break;
                }
                case CloudSeaCreatureKind.Swimmer:
                {
                    float radius = p.Radius * Range(swimRadiusScaleMin, swimRadiusScaleMax);
                    state.radius = radius;
                    state.angularSpeed = p.Speed * Range(swimSpeedScaleMin, swimSpeedScaleMax) / radius;
                    state.bank = p.Bank * bankScale;
                    state.bobFrequency = Mathf.PI * 2f / Mathf.Max(0.5f, Range(5f, 8f));
                    state.bobPhase = Range(0f, Mathf.PI * 2f);
                    state.u = Range(0f, Mathf.PI * 2f);
                    state.yaw = CloudSeaCreatureSpecies.HeadingOf(Mathf.Cos(state.u), -Mathf.Sin(state.u));
                    break;
                }
                case CloudSeaCreatureKind.Jelly:
                {
                    // 开局放在巡游带中间；之后的高度由控制器（发放—松弛）接管。
                    floor = HasBedGrid ? BedFloor(origin.x, origin.z, 0.3f, p.Bottom) : bedYMin;
                    state.baseY = (floor + state.ceiling) * 0.5f;
                    state.drift = p.Radius;
                    state.spin = Range(4f, 9f) * (Range(0f, 1f) < 0.5f ? -1f : 1f);
                    state.yaw = Range(0f, 360f);
                    state.tiltPhase = Range(0f, Mathf.PI * 2f);
                    return state;
                }
                case CloudSeaCreatureKind.Bottom:
                {
                    // 对象不动、整只在骨架里挪：渲染器的包围盒放大一圈，走出原始包围盒也不会被剔除
                    // （自定义蒙皮不像 SkinnedMeshRenderer，没人替它更新包围盒）。
                    MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Bounds local = renderer.localBounds;
                        local.Expand(p.Radius * 2f);
                        renderer.localBounds = local;
                    }
                    return state;
                }
            }

            floor = HasBedGrid ? BedFloor(origin.x, origin.z, state.probeRadius, state.bottomExtent) : bedYMin;
            // 巡游带以 authored 高度为基准；越界时先夹进带内再扣浮动余量。
            state.bobAmplitude = Mathf.Min(p.Bob, Mathf.Max(0.02f, (state.ceiling - floor) * 0.25f));
            state.baseY = Mathf.Clamp(origin.y, floor + state.bobAmplitude, state.ceiling - state.bobAmplitude);
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
        // 每帧驱动（零堆分配：只 for 循环 + 三角函数 + 控制器浮点运算 + 关节缓冲上传 + Transform 写回）
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            TickFarClouds(dt);

            if (_creatures != null && _creatures.Length > 0)
            {
                float time = Time.time;
                for (int i = 0; i < _creatures.Length; i++)
                    TickCreature(_creatures[i], dt, time);

                // 各控制器已经把关节矩阵写进 _jointRows：整块上传，绑成全局给水下 shader 蒙皮。
                if (_jointBuffer != null)
                {
                    _jointBuffer.SetData(_jointRows);
                    Shader.SetGlobalBuffer(CloudSeaSkin.JointsId, _jointBuffer);
                }
                DumpCreaturePoses(time, dt);
            }

            if (_perches != null)
            {
                for (int i = 0; i < _perches.Length; i++)
                    TickPerch(_perches[i], dt);
            }

            if (_gulls != null && _gulls.Length > 0)
            {
                TickDiveScheduler(dt);
                for (int i = 0; i < _gulls.Length; i++)
                    TickGull(_gulls[i], dt);
            }
        }

        // ---- 水下生物 ----

        /// <summary>朝向以有限角速度追随目标角：掉头是转过去，不是瞬间跳到新朝向。</summary>
        static float TurnToward(float yaw, float target, float maxStep)
        {
            return yaw + Mathf.Clamp(Mathf.DeltaAngle(yaw, target), -maxStep, maxStep);
        }

        static float ZFreq(CreatureState s)
        {
            return s.figure8 ? 2f : 1f;
        }

        /// <summary>
        /// 一只生物的一帧：先按行为族挪对象（解析路径），再把位姿与速度装进帧信息交给控制器解骨架，
        /// 关节矩阵写进蒙皮缓冲。贴底生物对象不动（整只的位移在骨架里做）。
        /// 水母的升沉由控制器给（它这一步推出来的高度），这里只把结果套到对象上。
        /// </summary>
        void TickCreature(CreatureState s, float dt, float time)
        {
            s.time += dt;
            Vector3 before = s.transform.localPosition;
            float yawBefore = s.yaw;

            float floor = 0f;
            float ceiling = -1f;                 // 上沿低于下沿 = 不设限
            switch (s.Kind)
            {
                case CloudSeaCreatureKind.School:
                    MoveSchool(s, dt);
                    break;
                case CloudSeaCreatureKind.Swimmer:
                    MoveSwimmer(s, dt);
                    break;
                case CloudSeaCreatureKind.Jelly:
                    MoveJelly(s, dt);
                    if (HasBedGrid)
                    {
                        Vector3 here = s.transform.localPosition;
                        floor = BedFloor(here.x, here.z, 0.3f, s.bottomExtent);
                        ceiling = s.ceiling;
                    }
                    break;
            }

            Vector3 position = s.transform.localPosition;
            CloudSeaFrame frame = new CloudSeaFrame
            {
                Time = time,
                Position = position,
                Rotation = ToAffine(s.transform.localRotation),
                Velocity = (position - before) / dt,
                YawRate = Mathf.DeltaAngle(yawBefore, s.yaw) * Mathf.Deg2Rad / dt,
                Bed = _bedSampler,
                FloorY = floor,
                CeilingY = ceiling,
            };
            s.Brain.Step(dt, frame);
            s.Rig.Compose(_jointRows);

            if (s.Kind == CloudSeaCreatureKind.Jelly && s.Brain.DrivesAltitude)
            {
                position.y = s.Brain.AltitudeY;
                s.transform.localPosition = position;
            }
        }

        /// <summary>
        /// 出图诊断：把每只生物的关节坐标落盘（物体空间的静止位置与当前位置，外加对象位姿、脚下的沙面高度、
        /// 到这一帧为止的最大帧长）。只在设了 <see cref="PoseDumpPathKey"/> 时生效，整局只落一次。
        /// </summary>
        void DumpCreaturePoses(float time, float dt)
        {
            if (_poseDumpPath == null)
            {
                _poseDumpPath = System.Environment.GetEnvironmentVariable(PoseDumpPathKey) ?? string.Empty;
                _poseDumpTime = float.TryParse(System.Environment.GetEnvironmentVariable(PoseDumpTimeKey), out float t)
                    ? t
                    : 8f;
            }
            if (_poseDumped || _poseDumpPath.Length == 0)
                return;
            if (dt > _dtMax)
                _dtMax = dt;
            if (time < _poseDumpTime)
                return;
            _poseDumped = true;

            var sb = new System.Text.StringBuilder();
            sb.Append("time ").Append(time.ToString("F3")).Append(" dt ").Append(dt.ToString("F4"))
                .Append(" dtMax ").Append(_dtMax.ToString("F4")).Append('\n');
            // 海面下发的全局也落一行：岛影交界线不动时，先看波参数到底发出去没有。
            sb.Append("surface shadowWave ").Append(Shader.GetGlobalVector(Shader.PropertyToID("_CloudSeaShadowWave")))
                .Append(" dir ").Append(Shader.GetGlobalVector(Shader.PropertyToID("_CloudSeaShadowWaveDir")))
                .Append(" waterY ").Append(Shader.GetGlobalFloat(PixelartPath.WaterYId).ToString("F3"))
                .Append(" lightDir ").Append(Shader.GetGlobalVector(PixelartPath.LightDirId)).Append('\n');
            foreach (CreatureState s in _creatures)
            {
                CloudSeaRig rig = s.Rig;
                if (rig == null)
                    continue;
                Vector3 p = s.transform.localPosition;
                sb.Append("creature ").Append(s.transform.name)
                    .Append(" pos ").Append(p.ToString("F4"))
                    .Append(" yaw ").Append(s.transform.localEulerAngles.y.ToString("F2"))
                    .Append(" bed ").Append(HasBedGrid ? _bedSampler(p.x, p.z).ToString("F3") : "none")
                    .Append(" kind ").Append(s.Kind).Append('\n');
                for (int j = 0; j < rig.Count; j++)
                {
                    sb.Append("joint ").Append(rig.Roles[j]).Append(' ')
                        .Append(rig.Rest[j].ToString("F4")).Append(' ')
                        .Append(rig.Pose[j].Apply(rig.Rest[j]).ToString("F4")).Append('\n');
                }
            }
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_poseDumpPath);
                if (dir.Length > 0)
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(_poseDumpPath, sb.ToString());
                Debug.Log("[CloudSeaLife] 生物关节坐标已落盘：" + _poseDumpPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CloudSeaLife] 生物关节坐标落盘失败：" + e.Message);
            }
        }

        /// <summary>四元数 → 帧信息里的三行旋转（对象 → 父空间）。</summary>
        static CloudSeaAffine ToAffine(Quaternion rotation)
        {
            Matrix4x4 m = Matrix4x4.Rotate(rotation);
            return new CloudSeaAffine
            {
                R0 = new Vector4(m.m00, m.m01, m.m02, 0f),
                R1 = new Vector4(m.m10, m.m11, m.m12, 0f),
                R2 = new Vector4(m.m20, m.m21, m.m22, 0f),
            };
        }

        /// <summary>
        /// 鱼群：绕锚点走圆或 8 字，群头（群的本地 +X）对准速度方向。
        /// 不穿海面 / 海床是几何保证：上沿 = 群内最高的鱼不越过巡游带上沿，下沿 = 脚下沙丘 + 净空；
        /// 沙丘高到巡游带里时以「不出水」优先。
        /// </summary>
        void MoveSchool(CreatureState s, float dt)
        {
            s.u += s.angularSpeed * dt;

            float x = s.centerX + s.radiusX * Mathf.Sin(s.u);
            float z = s.centerZ + s.radiusZ * Mathf.Sin(ZFreq(s) * s.u + s.phaseZ);
            float y = s.baseY + s.bobAmplitude * Mathf.Sin(s.time * s.bobFrequency + s.bobPhase);
            float floor = HasBedGrid ? BedFloor(x, z, s.probeRadius, s.bottomExtent) : bedYMin;
            s.transform.localPosition = new Vector3(x, Mathf.Clamp(y, Mathf.Min(floor, s.ceiling), s.ceiling), z);

            float yaw = CloudSeaCreatureSpecies.HeadingOf(
                s.radiusX * Mathf.Cos(s.u),
                s.radiusZ * ZFreq(s) * Mathf.Cos(ZFreq(s) * s.u + s.phaseZ));
            s.yaw = TurnToward(s.yaw, yaw, turnRateDegPerSec * dt);
            s.transform.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
        }

        /// <summary>
        /// 大型游泳者：绕锚点画圆。朝向追速度；升降给俯仰（升则抬头），转弯给侧倾（朝圈内侧压）。
        /// 河豚鼓起来以后推不动水：路径的角速度乘控制器给的速度倍率。
        /// 物体本地 x = 前、y = 上、z = 侧，所以侧倾是绕 x 的旋转、俯仰是绕 z 的旋转，偏航最后套。
        /// </summary>
        void MoveSwimmer(CreatureState s, float dt)
        {
            float angular = s.angularSpeed * s.Brain.SpeedScale;
            s.u += angular * dt;

            float sinU = Mathf.Sin(s.u);
            float cosU = Mathf.Cos(s.u);
            float x = s.centerX + s.radius * sinU;
            float z = s.centerZ + s.radius * cosU;
            float floor = HasBedGrid ? BedFloor(x, z, s.probeRadius, s.bottomExtent) : bedYMin;
            float y = Mathf.Clamp(
                s.baseY + s.bobAmplitude * Mathf.Sin(s.time * s.bobFrequency + s.bobPhase),
                Mathf.Min(floor, s.ceiling), s.ceiling);
            s.transform.localPosition = new Vector3(x, y, z);

            s.yaw = TurnToward(s.yaw, CloudSeaCreatureSpecies.HeadingOf(cosU, -sinU),
                turnRateDegPerSec * dt);
            float rise = s.bobAmplitude * s.bobFrequency * Mathf.Cos(s.time * s.bobFrequency + s.bobPhase);
            float pitch = Mathf.Atan2(rise, Mathf.Max(0.05f, s.radius * angular));
            s.pitch += (pitch - s.pitch) * Mathf.Min(1f, 6f * dt);

            // 圈心相对鱼的那一侧：本地 +Z 方向上的分量符号（圆周运动里恒定，一整圈不翻）。
            float sideX = Mathf.Sin(s.yaw * Mathf.Deg2Rad);
            float sideZ = Mathf.Cos(s.yaw * Mathf.Deg2Rad);
            float side = Mathf.Sign(-sinU * sideX - cosU * sideZ);
            float bank = side * s.bank * s.radius * angular * angular;
            s.transform.localRotation = Quaternion.Euler(bank, s.yaw, s.pitch * Mathf.Rad2Deg);
        }

        /// <summary>水母：水平慢漂、缓缓自转、轻微摇摆。升沉不在这里（控制器推出来，TickCreature 套上）。</summary>
        void MoveJelly(CreatureState j, float dt)
        {
            float x = j.centerX + j.drift * Mathf.Sin(j.time * 0.11f + j.yaw * Mathf.Deg2Rad);
            float z = j.centerZ + j.drift * Mathf.Sin(j.time * 0.09f + j.tiltPhase * 3f);
            Vector3 p = j.transform.localPosition;
            j.transform.localPosition = new Vector3(x, p.y, z);

            float tilt = 4f * Mathf.Sin(j.time * 0.4f + j.tiltPhase);
            j.transform.localRotation = Quaternion.Euler(tilt, j.yaw + j.spin * j.time, tilt * 0.6f);
        }

        // ------------------------------------------------------------------
        // 海床高度格
        // ------------------------------------------------------------------

        /// <summary>
        /// 写进海床高度格（CloudSeaAssetBaker 烘焙时调用）。两张同规格的格：<paramref name="heights"/>
        /// 是净空格（<see cref="CloudSeaBedGrid.Build"/>），<paramref name="ground"/> 是贴地格
        /// （<see cref="CloudSeaBedGrid.BuildGround"/>，可以不给）。
        /// </summary>
        public void SetBedHeightGrid(float[] heights, int cells, float halfSize, float[] ground = null)
        {
            bedHeights = heights ?? new float[0];
            bedGround = ground ?? new float[0];
            bedGridCells = cells;
            bedGridHalfSize = halfSize;
        }

        /// <summary>高度格齐全才启用贴床（长度对不上 = 没烘或烘坏了，退回固定带而不是读越界）。</summary>
        bool HasBedGrid =>
            bedGridCells >= 1 && bedHeights != null && bedHeights.Length == (bedGridCells + 1) * (bedGridCells + 1);

        /// <summary>贴地格齐全才给脚用。</summary>
        bool HasBedGround => HasBedGrid && bedGround != null && bedGround.Length == bedHeights.Length;

        float BedHeightAt(float x, float z)
        {
            return CloudSeaBedGrid.Sample(bedHeights, bedGridCells, bedGridHalfSize, x, z);
        }

        float GroundHeightAt(float x, float z)
        {
            return CloudSeaBedGrid.Sample(bedGround, bedGridCells, bedGridHalfSize, x, z);
        }

        /// <summary>
        /// 游泳者的下沿：中心与前后左右四个偏移点里最高的沙丘，再加净空与该生物自身的下伸。
        /// 取最高而不是平均：对活物而言「最坏情况的高度」才是不能穿的那条线。
        /// </summary>
        float BedFloor(float x, float z, float probeRadius, float bottomExtent)
        {
            float highest = BedHeightAt(x, z);
            highest = Mathf.Max(highest, BedHeightAt(x + probeRadius, z));
            highest = Mathf.Max(highest, BedHeightAt(x - probeRadius, z));
            highest = Mathf.Max(highest, BedHeightAt(x, z + probeRadius));
            highest = Mathf.Max(highest, BedHeightAt(x, z - probeRadius));
            return highest + bedClearance + bottomExtent;
        }
        // ---- 停栖鸥 ----

        static void TickPerch(PerchGullState p, float dt)
        {
            p.time += dt;
            float pitch = p.amplitude * Mathf.Sin(p.time * p.frequency + p.phasePitch);
            float yaw = p.amplitude * p.yawRatio * Mathf.Sin(p.time * p.frequency * p.yawDetune + p.phaseYaw);
            float roll = p.amplitude * p.rollRatio * Mathf.Sin(p.time * p.frequency * p.rollDetune + p.phaseRoll);
            p.transform.localRotation = p.baseRotation * Quaternion.Euler(roll, yaw, pitch);
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

            _creatures = null;
            ReleaseJointBuffer();
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

        sealed class CreatureState
        {
            public Transform transform;
            public CloudSeaRig Rig;
            public CloudSeaBrain Brain;
            public CloudSeaCreatureKind Kind;

            // 水平路径（父空间）：鱼群走圆 / 8 字，游泳者走圆，水母慢漂
            public float centerX;
            public float centerZ;
            public float baseY;
            public float radiusX;      // 鱼群路径的两个半轴
            public float radiusZ;
            public float radius;       // 游泳者的巡游圈半径
            public bool figure8;
            public float phaseZ;
            public float angularSpeed; // 路径角速度（弧度 / 秒）
            public float bobAmplitude;
            public float bobFrequency;
            public float bobPhase;
            public float yaw;          // 度；以有限角速度追随速度方向
            public float pitch;        // 弧度（游泳者）
            public float bank;         // 侧倾系数（已乘 Inspector 的 bankScale）：侧倾角 = 它 × 半径 × 角速度²
            public float drift;        // 水母水平漂移半径
            public float spin;         // 水母自转角速度（度 / 秒，正反随机）
            public float tiltPhase;
            public float time;
            public float u;
            public float bottomExtent; // 下沿到对象原点的距离（贴床净空用它）
            public float probeRadius;  // 查沙丘高度时前后左右偏移点的半径
            public float ceiling;      // 巡游带上沿换算到对象原点
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
