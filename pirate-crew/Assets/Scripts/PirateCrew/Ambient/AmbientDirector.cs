using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.Ambient
{
    /// <summary>
    /// 环境与活物总控（MonoBehaviour，场景里唯一需要挂的 Ambient 组件）。
    ///
    /// 【它做什么】把 <c>Assets/Scripts/PirateCrew/Ambient/</c> 里的纯 C# 规则与程序化网格
    /// 组装成一个"活着的"场景层：
    ///   · 海鸥（李萨如盘旋 + 拍翅 + 偶发俯冲 + 爆炸惊飞）；
    ///   · 螃蟹（岸边横爬 + 钳子摆动 + 遇单位/爆炸缩沙）；
    ///   · 鱼群（水下 boids）；
    ///   · 远景海鸟剪影；
    ///   · 既有植被/旗帜的顶点风摆（换材质，见 <see cref="AmbientWindBinder"/>）；
    ///   · 灯笼（悬挂摆动 + 自发光辉光 + 可选点光）；
    ///   · 燕尾旗绳（缆绳 + 6 面垂旗随风摆）；
    ///   · 近岸浮标（随波起伏 + 周期性涟漪，涟漪复用 Fx 模块）；
    ///   · 昼夜/天气档位（默认正午 = 与现状逐值一致，切档才改氛围）。
    ///
    /// 【接线纪律（任务书）】
    ///   · 本组件**不做任何全局搜索**（无 <c>GameObject.Find</c> / <c>FindObjectsOfType</c>）；
    ///     必需引用（SceneArt 根、地面、主光、双方单位根）全部由 <c>[SerializeField]</c> 注入；
    ///   · 场景由协调者用 <c>M2BattleSceneSetup</c> 装配（本 agent 不改该文件）——接线清单见交付报告；
    ///   · 若本组件挂在 <c>SceneArt</c> 根节点下，可不填 <c>sceneArtRoot</c>（自动取父节点）。
    ///
    /// 【可玩性红线】本模块创建的所有物体：
    ///   · 无 Collider / Rigidbody（不改变任何物理与弹道，场景文档 §9.4）；
    ///   · 不进入竞技场内的 Z≥13 近侧带（灯笼/旗绳全部放竞技场**外**水域）；
    ///   · 海鸥位置写回前必过禁飞区钳制（不遮挡投掷视线）；
    ///   · 不触碰 <c>TileTerrainGrid</c> / <c>AiTerrain</c> / 出生位。
    ///
    /// 【性能】只有一个 <c>Update</c>：活物 Tick（少量正弦 + 一次 O(n²) boids，n ≤ 20）
    /// + 摆动节点 Tick。全部计算量在报告里给出实测公式估算；无每帧堆分配
    /// （boids 用预分配数组，材质属性只在构建期写）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientDirector : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 序列化配置
        // ------------------------------------------------------------------

        [Header("接线（协调者在场景里填；留空有容错回落）")]
        [Tooltip("场景美术根节点（名字应为 SceneArt）。本组件挂在它下面时可留空（自动取父节点）。")]
        [SerializeField] Transform sceneArtRoot;

        [Tooltip("地面物体：填了就按它的 localScale.x/z 推竞技场尺寸；留空用下面的瓦片数。")]
        [SerializeField] Transform groundPlane;

        [Tooltip("主方向光：昼夜/天气档位写入它。留空则只改雾/环境光，并在 Console 记一条警告。")]
        [SerializeField] Light sunLight;

        [Tooltip("红队单位根（Team0_Red）：用于螃蟹/鱼群避让。留空则不做单位距离判定。")]
        [SerializeField] Transform team0Root;

        [Tooltip("蓝队单位根（Team1_Blue）：同上。")]
        [SerializeField] Transform team1Root;

        [Tooltip("主相机：用于辉光贴片朝向。留空回落 Camera.main。")]
        [SerializeField] Camera targetCamera;

        [Header("风摆材质覆盖（留空则运行时程序化生成；填 AmbientAssetBuilder 的产物即可让美术调参）")]
        [SerializeField] Material windFoliageMaterial;
        [SerializeField] Material windFlagRedMaterial;
        [SerializeField] Material windFlagBlueMaterial;
        [SerializeField] Material windClothMaterial;

        [Header("竞技场（groundPlane 未接线时使用；默认 level_1 = 50×17）")]
        [SerializeField] int arenaWidthTiles = 50;
        [SerializeField] int arenaDepthTiles = 17;

        [Header("活物开关（数量 -1 = 用模块默认值）")]
        [SerializeField] bool enableGulls = true;
        [SerializeField] int gullCount = -1;
        [SerializeField] bool enableCrabs = true;
        [SerializeField] int crabCount = -1;
        [SerializeField] bool enableFish = true;
        [SerializeField] int fishCount = -1;
        [SerializeField] bool enableDistantBirds = true;
        [SerializeField] bool enableVegetationWind = true;
        [SerializeField] bool enableLanterns = true;
        [SerializeField] bool enablePennantLine = true;
        [SerializeField] bool enableCorkFloats = true;

        [Header("昼夜 / 天气（默认正午 = 不改变默认可玩状态）")]
        [SerializeField] AmbientTimeOfDay timeOfDay = AmbientTimeOfDay.Noon;
        [SerializeField] bool applyPresetOnStart = true;

        [Tooltip("三档天空盒材质（可选；下标 = AmbientTimeOfDay：0 正午 / 1 黄昏 / 2 阴云）。"
            + "留空或对应项为空时，按 AmbientSkyboxCatalog 程序化建一张（切档时就地重写参数）——"
            + "这样本功能不依赖场景接线即可生效。仅在环境光来源开关 = 天空盒时才被读取，"
            + "开关见 AmbientSkyboxCatalog.DefaultAmbientSource（默认 Trilight = 现役基准不变）。")]
        [SerializeField] Material[] skyboxMaterials;

        [Tooltip("灯笼点光。**默认关**：本工程 PirateSurface / PirateOutline 只取主方向光，"
            + "额外的点光对场上绝大多数材质不产生照明（等于纯开销）。"
            + "灯笼的「亮」由自发光辉光片承担；将来 shader 补 _ADDITIONAL_LIGHTS 后再打开。")]
        [SerializeField] bool enableLanternPointLights = false;

        [Header("其它")]
        [SerializeField] int seed = 20260913;
        [SerializeField] bool verboseLog = true;

        // ------------------------------------------------------------------
        // 运行时
        // ------------------------------------------------------------------

        AmbientArena _arena;
        AmbientNoFlyZone _noFly;
        AmbientMeshSet _meshes;
        AmbientMaterialSet _materials;
        AmbientWindBinder _windBinder;

        Transform _lifeRoot;
        readonly List<SeagullAgent> _gulls = new List<SeagullAgent>(AmbientBudget.MaxGulls);
        readonly List<CrabAgent> _crabs = new List<CrabAgent>(AmbientBudget.MaxCrabs);
        readonly List<AmbientSwayNode> _swayNodes = new List<AmbientSwayNode>(8);
        readonly List<Transform> _distantBirds = new List<Transform>(3);
        FishSchoolAgent _fishSchool;
        Camera _camera;

        int _alarmFrames;
        bool _warnedMissingSun;
        bool _warnedMissingSkybox;

        /// <summary>命令行 -ambientTimeOfDay 覆盖生效中（真 = SetTimeOfDay 的调用方档位被忽略）。</summary>
        bool _cliTierOverride;

        /// <summary>运行时程序化建的天空盒材质（<see cref="ResolveSkyboxMaterial"/> 的兜底路径；Teardown 里销毁）。</summary>
        Material _runtimeSkyboxMaterial;

        bool _built;

        /// <summary>全部活物 + 环境动效的三角面合计（报告用）。</summary>
        public int AmbientTriangleCount { get; private set; }

        /// <summary>新增渲染器数量（≈ DrawCall 上界；同材质会被合批，实际更低）。</summary>
        public int AmbientRendererCount { get; private set; }

        /// <summary>当前昼夜档位。</summary>
        public AmbientTimeOfDay TimeOfDay => timeOfDay;

        /// <summary>海鸥数量。</summary>
        public int GullCount => _gulls.Count;

        /// <summary>螃蟹数量。</summary>
        public int CrabCount => _crabs.Count;

        /// <summary>相机侧可读的竞技场边界（供外部调试/UI）。</summary>
        public AmbientArena Arena => _arena;

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void Start()
        {
            Build();
        }

        void OnDestroy()
        {
            Teardown();
        }

        void Update()
        {
            if (!_built)
                return;

            // 卡顿保护：单帧 dt 上限 50ms（否则一帧大步长会让 boids 与俯冲路径跳变）。
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f)
                return;

            if (_alarmFrames > 0)
                _alarmFrames--;

            CollectUnitPositions();

            for (int i = 0; i < _gulls.Count; i++)
                _gulls[i].Tick(dt);

            for (int i = 0; i < _crabs.Count; i++)
                _crabs[i].Tick(dt, NearestUnitDistance(_crabs[i].transform.position));

            if (_fishSchool != null)
                _fishSchool.Tick(dt);

            for (int i = 0; i < _swayNodes.Count; i++)
                _swayNodes[i].Tick(dt, _camera);

            TickDistantBirds(dt);
        }

        // ------------------------------------------------------------------
        // 装配
        // ------------------------------------------------------------------

        void Build()
        {
            if (_built)
                return;

            _camera = targetCamera != null ? targetCamera : Camera.main;

            _arena = ResolveArena();
            _noFly = AmbientNoFlyZone.ForArena(_arena);

            _materials = new AmbientMaterialSet();
            _meshes = new AmbientMeshSet();

            var lifeGo = new GameObject("AmbientLife");
            lifeGo.transform.SetParent(transform, false);
            _lifeRoot = lifeGo.transform;

            AmbientRandom rng = new AmbientRandom(seed);

            if (enableGulls)
                SpawnGulls(rng);

            if (enableCrabs)
                SpawnCrabs(rng);

            if (enableFish)
                SpawnFish(rng);

            if (enableLanterns)
                SpawnLanterns(rng);

            if (enablePennantLine)
                SpawnPennantLine(rng);

            if (enableCorkFloats)
                SpawnCorkFloats(rng);

            if (enableDistantBirds)
                SpawnDistantBirds(rng);

            if (enableVegetationWind)
            {
                _windBinder = new AmbientWindBinder();
                int bound = _windBinder.Bind(ResolveSceneArtRoot(),
                    ResolveWindMaterial(windFoliageMaterial, _materials.WindFoliage),
                    ResolveWindMaterial(windFlagRedMaterial, _materials.WindFlagRed),
                    ResolveWindMaterial(windFlagBlueMaterial, _materials.WindFlagBlue),
                    ResolveWindMaterial(windClothMaterial, _materials.WindCloth),
                    verboseLog);
            }

            // 现有事件契约里唯一带"世界坐标"的爆炸事件（未新增任何事件）。
            EventBus.Subscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);

            // 命令行档位覆盖（-ambientTimeOfDay，见 AmbientTimeOfDayCatalog）：三档对比捕图 / 试玩
            // 验证用，优先级**高于**世界地图档（BattleController 稍后 SetTimeOfDay 会被 _cliTierOverride 拦下）。
            string[] commandLineArgs = global::System.Environment.GetCommandLineArgs();
            if (AmbientTimeOfDayCatalog.TryParseCommandLineTier(commandLineArgs, out AmbientTimeOfDay cliTier))
            {
                timeOfDay = cliTier;
                _cliTierOverride = true;
            }
            else if (AmbientTimeOfDayCatalog.CommandLineTierSwitchPresent(commandLineArgs))
            {
                global::PirateCrew.Core.Log.Warn("[Ambient] " + AmbientTimeOfDayCatalog.CommandLineTierSwitch
                    + " 的档名不合法（应为 Noon/Dusk/Overcast，Storm=Overcast 别名）→ 忽略，按地图档运行。");
            }

            if (applyPresetOnStart)
                ApplyPreset();

            _built = true;

            if (verboseLog)
            {
                global::PirateCrew.Core.Log.Info("[Ambient] 环境与活物就绪。\n"
                    + "  竞技场: " + _arena.Width + "×" + _arena.Depth
                    + "（禁飞区 X " + _noFly.MinX.ToString("0.0") + "~" + _noFly.MaxX.ToString("0.0")
                    + "，天花板 y=" + _noFly.CeilingY + "）\n"
                    + "  海鸥 " + _gulls.Count + " / 螃蟹 " + _crabs.Count
                    + " / 鱼 " + (_fishSchool != null ? _fishSchool.Count : 0)
                    + " / 远景鸟 " + _distantBirds.Count
                    + " / 摆动节点 " + _swayNodes.Count + "\n"
                    + "  风摆绑定渲染器: " + (_windBinder != null ? _windBinder.BoundCount : 0)
                    + " / 新增渲染器 " + AmbientRendererCount
                    + " / 新增三角面 " + AmbientTriangleCount
                    + " / 昼夜档位 " + AmbientTimeOfDayCatalog.DisplayName(timeOfDay));
            }
        }

        void Teardown()
        {
            EventBus.Unsubscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);

            // 运行时建的天空盒材质是 DontSave 的孤儿资产，必须显式销毁（本工程资源生命周期的口径，
            // 同 _runtimeMeshes 的处理）。
            if (_runtimeSkyboxMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeSkyboxMaterial);
                else
                    DestroyImmediate(_runtimeSkyboxMaterial);
                _runtimeSkyboxMaterial = null;
            }

            if (_windBinder != null)
            {
                _windBinder.Restore();
                _windBinder = null;
            }

            if (_lifeRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(_lifeRoot.gameObject);
                else
                    DestroyImmediate(_lifeRoot.gameObject);
                _lifeRoot = null;
            }

            _gulls.Clear();
            _crabs.Clear();
            _swayNodes.Clear();
            _distantBirds.Clear();
            _fishSchool = null;

            for (int i = 0; i < _runtimeMeshes.Count; i++)
            {
                if (_runtimeMeshes[i] == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(_runtimeMeshes[i]);
                else
                    DestroyImmediate(_runtimeMeshes[i]);
            }

            _runtimeMeshes.Clear();

            if (_meshes != null)
            {
                _meshes.Dispose();
                _meshes = null;
            }

            if (_materials != null)
            {
                _materials.Dispose();
                _materials = null;
            }

            _built = false;
        }

        // ------------------------------------------------------------------
        // 事件
        // ------------------------------------------------------------------

        void OnBattleStarted(object payload)
        {
            _alarmFrames = 0;
        }

        void OnProjectileDetonated(object payload)
        {
            if (!(payload is ProjectileDetonatedPayload detonated))
                return;

            Vector3 blast = detonated.Position;

            for (int i = 0; i < _gulls.Count; i++)
            {
                float distance = Vector3.Distance(_gulls[i].transform.position, blast);
                float strength = Mathf.Clamp01(1f - distance / 24f);
                if (strength > 0.05f)
                    _gulls[i].Panic(blast, strength);
            }

            // 螃蟹：爆心附近 → 强制缩沙约 1 秒（60 帧）。
            for (int i = 0; i < _crabs.Count; i++)
            {
                if (Vector3.Distance(_crabs[i].transform.position, blast) <= CrabBehaviorRules.AlarmRadius)
                    _crabs[i].Alarm(60);
            }

            if (_fishSchool != null)
            {
                float distance = Vector3.Distance(_fishSchool.Centroid, blast);
                float strength = Mathf.Clamp01(1f - distance / 20f);
                if (strength > 0.05f)
                    _fishSchool.Scatter(blast, strength);
            }
        }

        // ------------------------------------------------------------------
        // 昼夜 / 天气
        // ------------------------------------------------------------------

        /// <summary>切换昼夜/天气档位（主光 + 雾 + 环境光同步切换，美术风格指南判据 C-5）。</summary>
        public void SetTimeOfDay(AmbientTimeOfDay value)
        {
            // 命令行显式指定的档位优先级最高（-ambientTimeOfDay，对比捕图用）：
            // 世界地图/BattleController 的切档被覆盖，保证拍出来的就是目标档。
            if (_cliTierOverride)
                value = timeOfDay;

            timeOfDay = value;
            ApplyPreset();
        }

        /// <summary>切到下一档（循环），返回新档位。</summary>
        public AmbientTimeOfDay CycleTimeOfDay()
        {
            SetTimeOfDay(AmbientTimeOfDayCatalog.Next(timeOfDay));
            return timeOfDay;
        }

        void ApplyPreset()
        {
            AmbientLightingPreset preset = AmbientTimeOfDayCatalog.For(timeOfDay);

            if (sunLight != null)
            {
                sunLight.color = preset.SunColor;
                sunLight.intensity = preset.SunIntensity;
                sunLight.transform.rotation = Quaternion.Euler(preset.SunEuler);

                // 登记 RenderSettings.sun：场景烘焙的 m_Sun 为空（{fileID: 0}），下游（如
                // WaterSimulationDriver 的太阳方向兜底链、URP 主光阴影判定）只能靠
                // "找最亮平行光"兜底；这里在应用档位时显式登记是正路（判空，未接线不覆盖场景默认）。
                RenderSettings.sun = sunLight;
            }
            else if (!_warnedMissingSun)
            {
                _warnedMissingSun = true;
                global::PirateCrew.Core.Log.Warn("[Ambient] sunLight 未接线：昼夜档位只切换雾与环境光，主光不动"
                    + "（接线清单见交付报告）。");
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = preset.FogColor;
            RenderSettings.fogStartDistance = preset.FogStart;
            RenderSettings.fogEndDistance = preset.FogEnd;
            RenderSettings.ambientIntensity = preset.AmbientIntensity;

            // ---- 环境光来源（视觉遗留 #6）----
            // 开关默认 Trilight = 现役基准：下面这个分支不执行，三色由场景烘焙提供（本方法只写强度，
            // 见 AmbientTimeOfDayCatalog 类头"三方逐值一致"）。开关翻成 Skybox 后，环境光的 SH
            // 完全由天空盒卷积而来（ambientSky/Equator/GroundColor 失效），此时才写 skybox 与模式。
            if (AmbientSkyboxCatalog.SkyboxAmbientEnabled)
            {
                Material skybox = ResolveSkyboxMaterial(timeOfDay);
                if (skybox != null)
                {
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                    RenderSettings.skybox = skybox;
                }
                else if (!_warnedMissingSkybox)
                {
                    _warnedMissingSkybox = true;
                    global::PirateCrew.Core.Log.Warn("[Ambient] 环境光来源开关=天空盒，但拿不到天空盒材质"
                        + "（shader 未入构建且场景未接线）→ 本次退回 Trilight 三色，画面不跳变。");
                }
            }

            // Skybox 环境模式下 ambientLight 不生效（URP 从天空盒取），此时只改强度。
            if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox)
                RenderSettings.ambientLight = preset.AmbientColor;
        }

        /// <summary>
        /// 取该档天空盒材质：优先用场景接线的 <c>skyboxMaterials[]</c>（资产引用，出包自包含），
        /// 留空则程序化建一张（按 <see cref="AmbientSkyboxCatalog"/> 写参数，切档时就地重写）——
        /// 后者让本功能不必等场景接线即可生效，shader 由 ArtGate ⓪ 登记进 Always Included 保证入包。
        /// </summary>
        Material ResolveSkyboxMaterial(AmbientTimeOfDay tier)
        {
            int index = (int)tier;
            if (skyboxMaterials != null && index >= 0 && index < skyboxMaterials.Length
                && skyboxMaterials[index] != null)
                return skyboxMaterials[index];

            if (_runtimeSkyboxMaterial == null)
            {
                Shader shader = Shader.Find(AmbientSkyboxCatalog.SkyShaderName);
                if (shader == null)
                    return null;

                _runtimeSkyboxMaterial = new Material(shader) { name = "RuntimeGradientSky" };
                _runtimeSkyboxMaterial.hideFlags = HideFlags.DontSave;
            }

            // 每次切档都重写参数：三档共用这一张运行时材质。
            AmbientSkyboxCatalog.ApplyPreset(_runtimeSkyboxMaterial, tier);
            return _runtimeSkyboxMaterial;
        }

        // ------------------------------------------------------------------
        // 竞技场 / 引用解析（全部容错，无全局搜索）
        // ------------------------------------------------------------------

        AmbientArena ResolveArena()
        {
            if (groundPlane != null)
            {
                Vector3 scale = groundPlane.localScale;
                if (scale.x > 1f && scale.z > 1f)
                    return AmbientArena.FromTiles(scale.x, scale.z);
            }

            int width = arenaWidthTiles > 0 ? arenaWidthTiles : 50;
            int depth = arenaDepthTiles > 0 ? arenaDepthTiles : 17;
            return AmbientArena.FromTiles(width, depth);
        }

        Transform ResolveSceneArtRoot()
        {
            if (sceneArtRoot != null)
                return sceneArtRoot;

            // 本组件挂在 SceneArt 根节点之下时，父节点就是 SceneArt。
            Transform parent = transform.parent;
            if (parent != null && LooksLikeSceneArtRoot(parent))
                return parent;

            // 本组件直接挂在 SceneArt 根节点上时，直接子节点里就有 SceneArt_* 组。
            if (LooksLikeSceneArtRoot(transform))
                return transform;

            return parent;
        }

        /// <summary>用"是否有 SceneArtBuilder 生成的具名子渲染器"判断这是不是 SceneArt 根（层级内查找，非全局搜索）。</summary>
        static bool LooksLikeSceneArtRoot(Transform candidate)
        {
            if (candidate == null)
                return false;

            return candidate.Find(AmbientWindBinder.FoliageObjectName) != null
                || candidate.Find(AmbientWindBinder.FlagRedObjectName) != null
                || candidate.Find(AmbientWindBinder.ClothObjectName) != null
                || candidate.Find("SceneArt_Wood") != null;
        }

        Material ResolveWindMaterial(Material overrideMaterial, Material fallback)
        {
            return overrideMaterial != null ? overrideMaterial : fallback;
        }

        readonly List<Vector3> _unitPositions = new List<Vector3>(32);

        /// <summary>收集双方单位位置（每帧一次，≤28 个）。没有单位根引用时列表为空。</summary>
        void CollectUnitPositions()
        {
            _unitPositions.Clear();
            CollectChildren(team0Root);
            CollectChildren(team1Root);
        }

        void CollectChildren(Transform root)
        {
            if (root == null)
                return;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.gameObject.activeInHierarchy)
                    _unitPositions.Add(child.position);
            }
        }

        /// <summary>到最近单位的水平距离（无单位时返回正无穷 → 螃蟹永远不缩沙）。</summary>
        float NearestUnitDistance(Vector3 from)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < _unitPositions.Count; i++)
            {
                Vector3 p = _unitPositions[i];
                float dx = p.x - from.x;
                float dz = p.z - from.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < nearest)
                    nearest = d2;
            }

            if (float.IsPositiveInfinity(nearest))
                return nearest;

            return Mathf.Sqrt(nearest);
        }

        // ------------------------------------------------------------------
        // 海鸥
        // ------------------------------------------------------------------

        void SpawnGulls(AmbientRandom rng)
        {
            int count = gullCount >= 0 ? gullCount : AmbientBudget.DefaultGulls;
            count = Mathf.Clamp(count, 0, AmbientBudget.MaxGulls);

            // 轨道布局在纯 C# 规则层（GullFlightRules.BuildPlans），测试对同一份布局断言"永不进禁飞区"。
            GullPlan[] plans = GullFlightRules.BuildPlans(_arena, rng, count);

            for (int i = 0; i < plans.Length; i++)
            {
                GullOrbit orbit = plans[i].Orbit;
                Vector3 diveTarget = plans[i].DiveTarget;

                var go = new GameObject("Seagull_" + i);
                go.transform.SetParent(_lifeRoot, false);

                CreateRenderable(go.transform, "Body", _meshes.GullBody, _materials.GullBody, castShadow: false);

                Transform leftWing = CreateRenderable(go.transform, "WingLeft", _meshes.GullWing,
                    _materials.GullWing, castShadow: false).transform;
                leftWing.localPosition = new Vector3(0.012f, 0.020f, 0f);

                Transform rightWing = CreateRenderable(go.transform, "WingRight", _meshes.GullWing,
                    _materials.GullWing, castShadow: false).transform;
                rightWing.localPosition = new Vector3(-0.012f, 0.020f, 0f);
                rightWing.localScale = new Vector3(-1f, 1f, 1f);

                var agent = go.AddComponent<SeagullAgent>();
                float phaseOffset = rng.Range(0f, 1f);
                agent.Configure(orbit, diveTarget, _noFly, phaseOffset,
                    GullFlightRules.MinDiveInterval, GullFlightRules.MaxDiveInterval,
                    leftWing, rightWing, plans[i].Dives);

                _gulls.Add(agent);
            }
        }

        // ------------------------------------------------------------------
        // 螃蟹
        // ------------------------------------------------------------------

        void SpawnCrabs(AmbientRandom rng)
        {
            int count = crabCount >= 0 ? crabCount : AmbientBudget.DefaultCrabs;
            count = Mathf.Clamp(count, 0, AmbientBudget.MaxCrabs);

            float bodyY = AmbientShore.CrabBodyY(_arena.WaterY, _arena.GroundY);
            float cx = _arena.CenterX;
            float d = _arena.Depth;

            for (int i = 0; i < count; i++)
            {
                Vector3 a;
                Vector3 b;

                switch (i)
                {
                    case 1:
                        a = new Vector3(cx + 5f, bodyY, d + 1.1f);
                        b = new Vector3(cx + 11f, bodyY, d + 1.1f);
                        break;
                    case 2:
                        a = new Vector3(-AmbientShore.CrabShoreOffset, bodyY, 4.5f);
                        b = new Vector3(-AmbientShore.CrabShoreOffset, bodyY, 10.5f);
                        break;
                    default:
                        a = new Vector3(cx - 11f, bodyY, d + AmbientShore.CrabShoreOffset);
                        b = new Vector3(cx - 5f, bodyY, d + AmbientShore.CrabShoreOffset);
                        break;
                }

                var go = new GameObject("Crab_" + i);
                go.transform.SetParent(_lifeRoot, false);

                CreateRenderable(go.transform, "Shell", _meshes.CrabBody, _materials.CrabShell, castShadow: false);

                Transform leftClaw = CreateRenderable(go.transform, "ClawLeft", _meshes.CrabClaw,
                    _materials.CrabClaw, castShadow: false).transform;
                leftClaw.localPosition = new Vector3(0.085f, 0.030f, 0.040f);

                Transform rightClaw = CreateRenderable(go.transform, "ClawRight", _meshes.CrabClaw,
                    _materials.CrabClaw, castShadow: false).transform;
                rightClaw.localPosition = new Vector3(-0.085f, 0.030f, 0.040f);
                rightClaw.localScale = new Vector3(-1f, 1f, 1f);

                var agent = go.AddComponent<CrabAgent>();
                float halfPeriod = rng.Range(5.5f, 9.5f);
                agent.Configure(a, b, halfPeriod, rng.Range(0f, 1f),
                    CrabBehaviorRules.BuryDepth, leftClaw, rightClaw);

                _crabs.Add(agent);
            }
        }

        // ------------------------------------------------------------------
        // 鱼群
        // ------------------------------------------------------------------

        void SpawnFish(AmbientRandom rng)
        {
            int count = fishCount >= 0 ? fishCount : AmbientBudget.FishPerSchool;
            count = Mathf.Clamp(count, 0, AmbientBudget.MaxFishPerSchool);
            if (count <= 0)
                return;

            BoidsSettings settings = BoidsSettings.Default(_arena);
            var positions = new Vector3[count];
            var velocities = new Vector3[count];
            var fishTransforms = new Transform[count];

            var schoolGo = new GameObject("FishSchool");
            schoolGo.transform.SetParent(_lifeRoot, false);

            for (int i = 0; i < count; i++)
            {
                GameObject fish = CreateRenderable(schoolGo.transform, "Fish_" + i,
                    _meshes.Fish, _materials.Fish, castShadow: false);
                fishTransforms[i] = fish.transform;
            }

            var agent = schoolGo.AddComponent<FishSchoolAgent>();
            Vector3 anchorRadius = new Vector3(settings.Extents.x * 0.45f, 0f, settings.Extents.z * 0.45f);
            agent.Configure(settings, positions, velocities, fishTransforms,
                settings.Center, anchorRadius, 0.28f, rng, rng.Range(0f, 6.283f));
            _fishSchool = agent;

            // 鱼群恒定数量：每条鱼 1 个渲染器（共享网格 + 材质 → 会被 SRP Batcher 合批）。
            AmbientRendererCount += count;
            AmbientTriangleCount += count * MeshTriangles(_meshes.Fish);
        }

        // ------------------------------------------------------------------
        // 灯笼
        // ------------------------------------------------------------------

        void SpawnLanterns(AmbientRandom rng)
        {
            int count = Mathf.Clamp(AmbientBudget.MaxLanterns, 0, AmbientBudget.MaxLanterns);
            if (count <= 0)
                return;

            // 全部放在竞技场**外**的水里（满足场景文档 §7.3「>1.5 高物只允许 Z≤4 或竞技场外」）。
            // 刻意不放近侧带（+Z，屏幕下缘）：45° 相机下 1.8 高的柱子会向上遮住竞技场近排，
            // 故只放西 / 东 / 远侧三个方向（屏幕左 / 右 / 上缘），中间战场完全不被压。
            Vector3[] spots =
            {
                new Vector3(-1.6f, _arena.WaterY - 0.35f, _arena.CenterZ),
                new Vector3(_arena.Width + 1.6f, _arena.WaterY - 0.35f, _arena.CenterZ * 0.6f),
                new Vector3(_arena.CenterX + 13f, _arena.WaterY - 0.35f, -1.6f),
            };

            for (int i = 0; i < count; i++)
            {
                var postGo = new GameObject("LanternPost_" + i);
                postGo.transform.SetParent(_lifeRoot, false);
                postGo.transform.position = spots[i];

                CreateRenderable(postGo.transform, "Post", _meshes.Post, _materials.Post, castShadow: true);

                // 挂点（摆动节点挂在这里，灯笼整体绕它摆）。
                var pivotGo = new GameObject("LanternPivot_" + i);
                pivotGo.transform.SetParent(postGo.transform, false);
                pivotGo.transform.localPosition = new Vector3(0f, AmbientMeshFactory.PostHeight + 0.05f, 0f);

                GameObject frame = CreateRenderable(pivotGo.transform, "Frame",
                    _meshes.LanternFrame, _materials.LanternMetal, castShadow: false);
                frame.transform.localPosition = new Vector3(0f, -AmbientMeshFactory.LanternHeight * 1.05f, 0f);

                // 灯芯挂在框**下方**（框是带顶盖的金属笼，放里面会被近侧面遮住、看不见）。
                GameObject core = CreateRenderable(pivotGo.transform, "Core",
                    _meshes.LanternCore, _materials.LanternCore, castShadow: false);
                core.transform.localPosition = new Vector3(0f, -AmbientMeshFactory.LanternHeight * 1.28f, 0f);

                // 辉光片：朝向相机的加性贴片 —— 本工程"灯笼在发光"的主要视觉载体。
                GameObject glow = CreateRenderable(pivotGo.transform, "Glow", _meshes.GlowQuad,
                    _materials.LanternGlow, castShadow: false);
                glow.transform.localPosition = new Vector3(0f, -AmbientMeshFactory.LanternHeight * 0.62f, 0f);
                float glowScale = AmbientMeshFactory.LanternHeight * 2.6f;
                glow.transform.localScale = new Vector3(glowScale, glowScale, glowScale);

                if (enableLanternPointLights)
                {
                    var lightGo = new GameObject("LanternLight");
                    lightGo.transform.SetParent(pivotGo.transform, false);
                    lightGo.transform.localPosition = new Vector3(0f, -AmbientMeshFactory.LanternHeight * 0.6f, 0f);
                    var light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = AmbientTimeOfDayCatalog.Hex("#FFC98A");
                    light.intensity = 1.1f;
                    light.range = 3.2f;
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.ForceVertex;
                }

                var sway = pivotGo.AddComponent<AmbientSwayNode>();
                sway.Configure(AmbientSwayMode.Pendulum,
                    angularSpeed: 0.85f,
                    phase: rng.Range(0f, 6.283f),
                    primaryAmplitudeDeg: 9f,
                    secondaryAmplitudeDeg: 4.5f,
                    bobAmplitude: 0f,
                    billboard: glow.transform,
                    rippleInterval: 0f,
                    rippleSpeed: 0f);
                _swayNodes.Add(sway);
            }
        }

        // ------------------------------------------------------------------
        // 燕尾旗绳（缆绳 + 垂旗）
        // ------------------------------------------------------------------

        void SpawnPennantLine(AmbientRandom rng)
        {
            float cx = _arena.CenterX;
            float baseY = _arena.WaterY - 0.45f;
            float z = -2.4f;
            float leftX = cx - 7f;
            float rightX = cx + 7f;

            // 两根立在浅水里的木桩。
            var leftPost = new GameObject("PennantPost_L");
            leftPost.transform.SetParent(_lifeRoot, false);
            leftPost.transform.position = new Vector3(leftX, baseY, z);
            CreateRenderable(leftPost.transform, "Post", _meshes.Post, _materials.Post, castShadow: true);

            var rightPost = new GameObject("PennantPost_R");
            rightPost.transform.SetParent(_lifeRoot, false);
            rightPost.transform.position = new Vector3(rightX, baseY, z);
            CreateRenderable(rightPost.transform, "Post", _meshes.Post, _materials.Post, castShadow: true);

            float topY = baseY + AmbientMeshFactory.PostHeight + 0.05f;

            // 缆绳：3 段折线，中间下垂（低模里 3 折已读作"绳"）。
            var ropeBuffers = new global::PirateCrew.SceneArt.MeshBuffers();
            Vector3 a = new Vector3(leftX, topY, z);
            Vector3 m1 = new Vector3(Mathf.Lerp(leftX, rightX, 0.34f), topY - 0.16f, z);
            Vector3 m2 = new Vector3(Mathf.Lerp(leftX, rightX, 0.66f), topY - 0.16f, z);
            Vector3 b = new Vector3(rightX, topY, z);
            ropeBuffers.AddRod(a, m1, 0.014f, 4);
            ropeBuffers.AddRod(m1, m2, 0.014f, 4);
            ropeBuffers.AddRod(m2, b, 0.014f, 4);
            Mesh ropeMesh = AmbientMeshSet.CreateMesh(ropeBuffers, "Ambient_PennantRope");
            var ropeGo = new GameObject("PennantRope");
            ropeGo.transform.SetParent(_lifeRoot, false);
            ropeGo.AddComponent<MeshFilter>().sharedMesh = ropeMesh;
            var ropeRenderer = ropeGo.AddComponent<MeshRenderer>();
            ropeRenderer.sharedMaterial = _materials.Post;
            ropeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AmbientRendererCount += 1;
            AmbientTriangleCount += ropeBuffers.TriangleCount;
            _runtimeMeshes.Add(ropeMesh);

            // 6 面垂旗，沿绳分布（悬挂点在上缘）。
            Material pennantMaterial = ResolveWindMaterial(windClothMaterial, _materials.WindCloth);
            const int pennants = 6;
            for (int i = 0; i < pennants; i++)
            {
                float t = (i + 1f) / (pennants + 1f);
                Vector3 anchor = Vector3.Lerp(a, b, t);
                anchor.y -= 0.012f;

                var go = new GameObject("Pennant_" + i);
                go.transform.SetParent(_lifeRoot, false);
                go.transform.position = anchor;
                // 旗面法线朝 +Z（相机侧），与风向无关。
                go.transform.rotation = Quaternion.Euler(0f, rng.Range(-6f, 6f), 0f);
                // 轻微错相：每面旗的朝向不同，读作"各自被风吹"。
                go.transform.localScale = Vector3.one * rng.Range(0.9f, 1.15f);

                CreateRenderable(go.transform, "Cloth", _meshes.Pennant, pennantMaterial, castShadow: false);
            }
        }

        // ------------------------------------------------------------------
        // 近岸浮标（随波起伏 + 周期性涟漪）
        // ------------------------------------------------------------------

        void SpawnCorkFloats(AmbientRandom rng)
        {
            var spots = new[]
            {
                new Vector3(_arena.CenterX - 4f, _arena.WaterY, _arena.Depth + 1.5f),
                new Vector3(_arena.CenterX + 3.5f, _arena.WaterY, _arena.Depth + 0.9f),
                new Vector3(_arena.CenterX + 9f, _arena.WaterY, _arena.Depth + 1.8f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var go = new GameObject("CorkFloat_" + i);
                go.transform.SetParent(_lifeRoot, false);
                go.transform.position = spots[i];

                CreateRenderable(go.transform, "Cork", _meshes.CorkFloat, _materials.CorkFloat, castShadow: false);

                var sway = go.AddComponent<AmbientSwayNode>();
                sway.Configure(AmbientSwayMode.Bob,
                    angularSpeed: 1.15f,
                    phase: rng.Range(0f, 6.283f),
                    primaryAmplitudeDeg: 0f,
                    secondaryAmplitudeDeg: 0f,
                    bobAmplitude: 0.045f,
                    billboard: null,
                    // 每 6-11 秒一次小涟漪：复用 Fx 的入水水花/涟漪系统，不重造涟漪。
                    rippleInterval: rng.Range(6f, 11f),
                    rippleSpeed: 2.2f);
                _swayNodes.Add(sway);
            }
        }

        // ------------------------------------------------------------------
        // 远景海鸟剪影
        // ------------------------------------------------------------------

        void SpawnDistantBirds(AmbientRandom rng)
        {
            int count = 3;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("DistantBird_" + i);
                go.transform.SetParent(_lifeRoot, false);
                go.transform.localScale = Vector3.one * 2.2f;
                CreateRenderable(go.transform, "Silhouette", _meshes.DistantBird,
                    _materials.DistantBird, castShadow: false);
                _distantBirds.Add(go.transform);
            }

            _distantBirdPhase = rng.Range(0f, 6.283f);
        }

        float _distantBirdPhase;
        float _distantBirdTime;

        void TickDistantBirds(float dt)
        {
            if (_distantBirds.Count == 0)
                return;

            _distantBirdTime += dt;

            // 远景（Z ≈ -55）一大圈缓慢盘旋：纯剪影、极低成本，但让天际线"有东西在动"。
            float baseZ = -55f;
            float centerX = _arena.CenterX;
            for (int i = 0; i < _distantBirds.Count; i++)
            {
                float phase = _distantBirdPhase + i * 2.1f;
                float t = _distantBirdTime * 0.045f + phase;
                float radius = 26f + i * 5f;
                Vector3 p = new Vector3(
                    centerX + Mathf.Cos(t) * radius,
                    11f + Mathf.Sin(t * 1.7f) * 1.2f + i * 0.9f,
                    baseZ + Mathf.Sin(t) * 9f - i * 3f);

                Transform bird = _distantBirds[i];
                Vector3 prev = bird.position;
                bird.position = p;

                Vector3 velocity = p - prev;
                if (velocity.sqrMagnitude > 1e-8f)
                    bird.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            }
        }

        // ------------------------------------------------------------------
        // 构建辅助
        // ------------------------------------------------------------------

        readonly List<Mesh> _runtimeMeshes = new List<Mesh>(2);

        /// <summary>建一个带 MeshFilter + MeshRenderer 的子物体（无 Collider）。</summary>
        GameObject CreateRenderable(Transform parent, string name, Mesh mesh, Material material, bool castShadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadow
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;

            AmbientRendererCount += 1;
            AmbientTriangleCount += MeshTriangles(mesh);
            return go;
        }

        static int MeshTriangles(Mesh mesh)
        {
            return mesh == null ? 0 : (int)(mesh.GetIndexCount(0) / 3);
        }
    }
}
