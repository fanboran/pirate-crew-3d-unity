using System;
using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.Visual;
using PirateCrew.PirateCrew.Battle.WorldMaps;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 职业视觉预制体选择项：外观档 → 预制体。由 <c>CrewVisualPrefabBuilder</c> 生成预制体后，
    /// 场景装配方把 7 个职业预制体填进 <see cref="BattleController"/> 的数组字段。
    /// </summary>
    [Serializable]
    public struct CrewVisualPrefabEntry
    {
        /// <summary>职业外观档。</summary>
        public CrewProfession profession;

        /// <summary>对应预制体（根含 BoxCollider + Rigidbody + PirateBase + UnitOutlineBinder + 视觉子层级）。</summary>
        public PirateBase prefab;
    }

    /// <summary>
    /// 战斗组装与结算根（翻译自 Godot <c>scripts/battle.gd</c> 的运行时职责）。
    ///
    /// 【对应章节】§4.3（按关卡 XML 坐标/队伍实例化出战单位）、§5.5（水位 = waterTileY*32）、
    ///             §5.3（Physics.OverlapSphere 取候选 → <see cref="ExplosionResolver"/> 纯逻辑算分 →
    ///             应用伤害与击退）、§3.3（胜负 <see cref="TurnRules.ComputeOutcome"/> /
    ///             得分 <see cref="ScoreRules.LevelScore"/>）、§4.4（落水即死）。
    ///
    /// 【3D 化决策】坐标/重力/初速换算全部集中在 <see cref="LevelGeometry"/>；
    /// 全局重力设为 Flash weight=1 的等价连续重力，物理帧率设为原版 25fps，
    /// 保证 <see cref="TrajectoryPreview"/> 与实弹轨迹同源（详见 LevelGeometry 类头）。
    ///
    /// 【爆炸坐标约定】<see cref="ExplosionResolver.Resolve"/> 在 Flash 平面约定下算平面分量（单位 px/帧）；
    /// 本类把世界坐标投到 XZ 平面换成 Flash 像素，再拿回结果：
    /// 平面两分量经 <see cref="LevelGeometry.FlashVelocityDeltaToArena"/> 落到世界 (X, Z)，
    /// 竖直项 <c>DeltaVUp</c>（含原版固定上抛 6k）直接落到世界 +Y。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleController : MonoBehaviour
    {
        [Header("关卡数据")]
        [Tooltip("优先使用本资产；为空时回退到 LevelCatalog 的 fallbackLevelNumber。")]
        [SerializeField] LevelDefinition level;
        [SerializeField] int fallbackLevelNumber = 1;

        [Header("组装引用（场景内直连）")]
        [SerializeField] PirateBase piratePrefab;
        [Tooltip("按职业外观档覆盖预制体（可选）。命中则用职业预制体，未命中/为空回落 piratePrefab；"
                 + "生成顺序与职业外观由 CrewVisualPrefabBuilder 产出，见 docs/角色造型规范.md §3。")]
        [SerializeField] CrewVisualPrefabEntry[] crewVisualPrefabs = new CrewVisualPrefabEntry[0];
        [SerializeField] Transform team0Root;
        [SerializeField] Transform team1Root;
        [Tooltip("旧水面物体（承载常驻的 WaterSimulationDriver 水模拟）。世界地图模式下只定向禁用其"
                 + "MeshRenderer/WaterTessellator，物体保持活跃；运行时把 y 设为水位（§5.5）。")]
        [SerializeField] Transform waterPlane;
        [SerializeField] TurnManager turnManager;
        [SerializeField] AimThrowController aimController;
        [SerializeField] BattleCameraController battleCamera;

        [Header("武器弹体（可选 Prefab；为空时程序化构建，无需重新装配既有场景）")]
        [Tooltip("弹体 Prefab；需要含 Rigidbody/Collider/WeaponProjectile。为空时用图元 + 颜色兜底构建。")]
        [SerializeField] GameObject projectilePrefab;

        [Header("瓦片地形（可选；为空时竞技场保持平坦地面）")]
        [Tooltip("地形视图；由 M2BattleSceneSetup 装配。为空时地形系统仍会建网格（供 AI 查询），但不渲染碰撞块。")]
        [SerializeField] BattleTerrainView terrainView;

        [Header("场景美术（可选）")]
        [Tooltip("关卡无关的静态陈设装配器：开局按实际关卡号重建船/岛/道具的合并网格。"
                 + "由 M2BattleSceneSetup 装配；为空时场景保持无静态陈设（地形与玩法不受影响）。")]
        [SerializeField] RuntimeSceneArt sceneArt;

        [Header("M4 世界地图（可选）")]
        [Tooltip("世界地图的 kit 资产表（FBX 预制体引用）；为空时世界地图用灰盒站面兜底。")]
        [SerializeField] WorldMapAssetSet worldMapAssetSet;
        [Tooltip("大海域海面材质（Ocean_Water.mat）；必须由场景引用入构建包——运行时 Shader.Find "
                 + "在播放器里查不到未引用的 shader（实拍踩坑：海面变纯白兜底盘）。为空时运行时兜底。")]
        [SerializeField] Material worldOceanMaterial;

        [Header("层掩码")]
        [Tooltip("爆炸候选与单位射线用的层。")]
        [SerializeField] LayerMask pirateLayerMask = ~0;
        [Tooltip("瞄准目标点射线用的地面/瓦片层。")]
        [SerializeField] LayerMask groundLayerMask = ~0;

        [Header("模式")]
        [Tooltip("true = 1P（team2 由 AI 控制）；false = 2P 热座。")]
        [SerializeField] bool team1IsAi = true;

        readonly List<PirateBase> _allPirates = new List<PirateBase>();
        readonly List<WeaponProjectile> _projectiles = new List<WeaponProjectile>();
        readonly List<int> _destroyedTerrainCells = new List<int>();
        readonly BattleTeam[] _teams = new BattleTeam[2];
        BattlePlan _plan;
        /// <summary>M4：本局激活的世界地图（null = 走原版关卡 1–33 链路，见 BuildPlan 优先级）。</summary>
        WorldMapDefinition _worldMap;
        bool _spawned;
        bool _matchFinished;
        float _waterWorldY;
        int _nextPirateId;

        /// <summary>当前关卡序号。</summary>
        public int LevelNumber => _plan != null ? _plan.LevelNumber : fallbackLevelNumber;

        /// <summary>队伍数量（恒 2）。</summary>
        public int TeamCount => 2;

        /// <summary>出战计划（组装结果）。</summary>
        public BattlePlan Plan => _plan;

        /// <summary>瓦片地形网格（纯 C#；平坦竞技场时也非 null，只是全部 0 块）。AI 的落点/放置查询用它。</summary>
        public TileTerrainGrid Terrain { get; private set; }

        /// <summary>全部角色。</summary>
        public IReadOnlyList<PirateBase> AllPirates => _allPirates;

        /// <summary>水面世界 Y（Unity 约定，y 向上）。</summary>
        public float WaterWorldY => _waterWorldY;

        /// <summary>本局是否为世界地图模式（地形/陈设/破坏/水面走 WorldMaps 分支）。</summary>
        public bool IsWorldMapActive => _worldMap != null;

        /// <summary>本局的世界地图定义（非世界地图模式返回 null）。</summary>
        public WorldMapDefinition WorldMap => _worldMap;

        /// <summary>爆炸/单位射线层掩码。</summary>
        public LayerMask PirateLayerMask => pirateLayerMask;

        /// <summary>地面/瞄准射线层掩码。</summary>
        public LayerMask GroundLayerMask => groundLayerMask;

        /// <summary>当前回合队伍（由 TurnManager 维护）。</summary>
        public BattleTeam CurrentTeam => turnManager != null ? turnManager.CurrentTeam : _teams[0];

        /// <summary>相机聚焦参考点（用于 panToCharacter 的"离相机中心最近"判定）。</summary>
        public Vector3 CameraFocusPoint => battleCamera != null ? battleCamera.FocusPoint : transform.position;

        /// <summary>对局是否已结束。</summary>
        public bool IsMatchOver => TurnRules.IsMatchOver(_teams[0].AnyAlive, _teams[1].AnyAlive);

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void Awake()
        {
            BuildPlan();
            BuildTerrain();
            RebuildSceneArt();
            ApplyPhysicsConvention();
            SpawnTeams();
        }

        void Start()
        {
            if (waterPlane != null)
            {
                Vector3 p = waterPlane.position;
                p.y = _waterWorldY;
                waterPlane.position = p;
            }

            if (_worldMap != null)
                SetupWorldMapEnvironment();

            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(LevelNumber, TeamCount));

            if (turnManager != null)
                turnManager.StartBattle();
            else
                Debug.LogError("[BattleController] 未接线 TurnManager，回合不会推进。");
        }

        void OnEnable()
        {
            // §5.2 limitedToTurn=true 的弹体在回合结束时销毁；常驻类（mine/箱体）保留。
            EventBus.Subscribe(BattleEvents.TurnEnded, OnTurnEnded);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
        }

        void OnTurnEnded(object payload)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                WeaponProjectile projectile = _projectiles[i];
                if (projectile == null)
                {
                    _projectiles.RemoveAt(i);
                    continue;
                }

                if (ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(WeaponCatalog.Get(projectile.WeaponId)))
                    Destroy(projectile.gameObject);
            }
        }

        void Update()
        {
            if (!_spawned || _matchFinished)
                return;

            // §4.4 落水即死：全局规则，每帧对所有存活角色判定。
            for (int i = 0; i < _allPirates.Count; i++)
            {
                PirateBase pirate = _allPirates[i];
                if (pirate != null && pirate.Alive)
                    pirate.CheckWaterDeath(_waterWorldY);
            }

            CheckMatchOver();
        }

        // ------------------------------------------------------------------
        // 组装
        // ------------------------------------------------------------------

        void BuildPlan()
        {
            // 【M4 世界地图】选图优先级：ArtReview 覆盖 > 世界地图(-worldMap / SetPending) >
            // 场景 level 资产 > CampaignApi 待战关 > fallback。命中世界地图时本局走 WorldMaps 分支：
            // 出战计划由目录直构（关卡号 101–108），地形/陈设/破坏/水面在对应阶段分流。
            if (ArtReview.ArtReviewCaptureOverride.LevelNumber <= 0 && WorldMapRuntime.TryGetPending(out _worldMap))
            {
                _plan = WorldMapRuntime.BuildBattlePlan(_worldMap);
                _waterWorldY = _plan.WaterWorldY;
                _teams[0] = new BattleTeam(1, aiControlled: false);
                _teams[1] = new BattleTeam(2, aiControlled: team1IsAi);
                return;
            }
            _worldMap = null;

            // 【关卡注入】场景未指定 level 资产时，改由战役侧「已选、等待结算的关卡」决定加载哪张竞技场
            // （CampaignApi.PendingBattleLevelNumberOr 是 M3 agent 备好的衔接点：只在 LevelCatalog
            // 已转写的关卡上生效，未转写/无待战关卡时回落到 fallbackLevelNumber，不会抛 KeyNotFound）。
            // 美术评审专用覆盖（PlayerArtCapture -artReviewLevel N）：优先级最高，仅用于无头出图验收，
            // 正常玩法不走这条（走 CampaignApi 选关链）。
            int levelNumber = ArtReview.ArtReviewCaptureOverride.LevelNumber;
            if (levelNumber <= 0)
            {
                levelNumber = level == null
                    ? CampaignApi.PendingBattleLevelNumberOr(fallbackLevelNumber)
                    : level.LevelNumber;
            }

            // 【样板三关】1/2/3 号被 ShowcaseLevels 覆盖（云朵场/双大船/山包+空岛）：
            // 出战数据手写（自由几何场景），不经 LevelCatalog 的原版转写表。
            LevelData? showcaseData = SceneArt.ShowcaseLevels.BuildLevelData(levelNumber);

            _plan = level != null && ArtReview.ArtReviewCaptureOverride.LevelNumber <= 0
                ? LevelGeometry.BuildBattlePlan(level)
                : LevelGeometry.BuildBattlePlan(
                    showcaseData != null ? showcaseData.Value : LevelCatalog.Get(levelNumber));

            _waterWorldY = _plan.WaterWorldY;
            _teams[0] = new BattleTeam(1, aiControlled: false);
            _teams[1] = new BattleTeam(2, aiControlled: team1IsAi);
        }

        /// <summary>
        /// 构建瓦片地形网格。**优先走平台簇布局**（<see cref="PlatformClusterLayout.BuildFor"/> 对任意关
        /// 都能推导出逐格水陆的平台地图，见其类头规则表）——场景里不再烘死大地面，
        /// 可站面全部来自这条路径；关卡数据未转写时退回 <see cref="TerrainCatalog"/> 的旧列式地形，
        /// 再退回平坦竞技场（与既有行为一致）。
        /// 网格无论是否转写都非 null（<see cref="Terrain"/>），便于 AI/小地图统一查询。
        /// </summary>
        void BuildTerrain()
        {
            // 【M4 世界地图】站面 box 栅格化为逻辑格（块高 = TopY/0.5）；碰撞与视觉由
            // WorldMapComposer 的独立 BoxCollider/灰盒负责，terrainView 的瓦片渲染不参与。
            if (_worldMap != null)
            {
                Terrain = WorldMapRuntime.BuildTerrainGrid(_worldMap)
                          ?? TileTerrainGrid.Flat(_plan.WidthTiles, _plan.DepthTiles);
                return;
            }

            int levelNumber = _plan.LevelNumber;

            // 【样板三关】逻辑格子 = ShowcaseLevels 手拼的隐形高度场（只喂站位 Y 与 AI 落点）；
            // BattleTerrainView 只建碰撞层（隐形 BoxCollider），不建格子渲染层。
            if (SceneArt.ShowcaseLevels.IsShowcase(levelNumber))
            {
                Terrain = SceneArt.ShowcaseLevels.BuildLogicGrid(levelNumber);
                if (terrainView != null)
                    terrainView.RenderCollidersOnly(Terrain);
                return;
            }

            TileTerrainGrid built = null;
            if (PlatformClusterLayout.TryBuildFor(levelNumber, out PlatformMap map)
                && map.WidthTiles == _plan.WidthTiles && map.DepthTiles == _plan.DepthTiles)
            {
                built = new TileTerrainGrid(_plan.WidthTiles, _plan.DepthTiles, null,
                    TerrainCatalog.DefaultBlockWorldHeight, map);
            }

            if (built == null)
                built = TerrainCatalog.Build(levelNumber, _plan.WidthTiles, _plan.DepthTiles);

            Terrain = built ?? TileTerrainGrid.Flat(_plan.WidthTiles, _plan.DepthTiles);

            if (terrainView != null)
                terrainView.Render(built);
        }

        /// <summary>
        /// 按**实际关卡号**重建关卡无关的静态陈设（船 / 岛 / 道具的合并网格）。
        /// 只做表现：不触碰地形破坏协议（那是 <see cref="BattleTerrainView"/> 的职责）。
        /// </summary>
        void RebuildSceneArt()
        {
            // 【M4 世界地图】原版陈设装配器不适用：kit 件 + 灰盒站面由 WorldMapComposer 摆放。
            if (_worldMap != null)
            {
                Transform artRoot = sceneArt != null ? sceneArt.transform : transform;
                WorldMapComposer.Build(artRoot, _worldMap, worldMapAssetSet);
                return;
            }

            if (sceneArt == null)
                return;

            sceneArt.RebuildFor(LevelNumber);
        }

        /// <summary>
        /// M4 世界地图的环境接线（Start 调用）：大海域海面（<see cref="Water.OceanRig"/> 替换旧
        /// Water Cube，落水死亡仍是纯 Y 阈值判定，不依赖水面碰撞）、相机全景档随地图跨度、
        /// 远裁剪保住 4200u 远场裙边、氛围档按地图定义。
        ///
        /// 【水模拟契约】旧 waterPlane 只做**定向退役**（禁 MeshRenderer/WaterTessellator 两个组件，
        /// GameObject 保持活跃，见 <see cref="RetireLegacyWaterPlane"/>），并把常驻的
        /// <see cref="Water.WaterSimulationDriver"/> 模拟域重配到本地图：域心 = 图心
        /// (SpanX/2, SpanZ/2)、域边长随最大跨度（<see cref="Water.WaterSimulationDriver.ConfigureWorldDomain"/>，
        /// 纯函数规则在 <see cref="Water.WaterSimRules.WorldDomainSizeForSpan"/>，clamp [128, 256]）。
        /// 直接组件调用沿用 <c>Water.OceanRig.Create</c> / <c>Water.WaterSimulationDriver.InjectSplash</c>
        /// 的先例，不做 GameObject.Find。
        /// </summary>
        void SetupWorldMapEnvironment()
        {
            RetireLegacyWaterPlane();

            // 水模拟域重配（装配期一次）：驱动常驻后其 Awake 推出的默认域仍钉在旧竞技场口径，
            // 需要显式搬到世界地图图心；Instance 为空（未接线）时静默跳过，不阻塞装配。
            if (Water.WaterSimulationDriver.Instance != null)
            {
                Water.WaterSimulationDriver.Instance.ConfigureWorldDomain(
                    new Vector2(_worldMap.SpanX * 0.5f, _worldMap.SpanZ * 0.5f),
                    Mathf.Max(_worldMap.SpanX, _worldMap.SpanZ));
            }

            HideBakedMinimapTiles();

            Water.OceanRig.Create(
                Water.OceanConfig.ForArena(
                    new Vector2(_worldMap.SpanX * 0.5f, _worldMap.SpanZ * 0.5f),
                    _worldMap.SpanX * 0.5f, _worldMap.SpanZ * 0.5f),
                material: worldOceanMaterial,
                parent: transform,
                followCamera: battleCamera != null ? battleCamera.GetComponent<Camera>() : null);

            Camera cam = battleCamera != null ? battleCamera.GetComponent<Camera>() : Camera.main;
            if (cam != null)
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4500f);
            if (battleCamera != null)
                battleCamera.SetWorldSpan(Mathf.Max(_worldMap.SpanX, _worldMap.SpanZ));

            var ambientDirector = FindObjectOfType<Ambient.AmbientDirector>();
            if (ambientDirector != null)
            {
                Ambient.AmbientTimeOfDay tier = Ambient.AmbientTimeOfDay.Noon;
                if (string.Equals(_worldMap.AmbientTier, "Dusk", StringComparison.OrdinalIgnoreCase))
                    tier = Ambient.AmbientTimeOfDay.Dusk;
                else if (string.Equals(_worldMap.AmbientTier, "Storm", StringComparison.OrdinalIgnoreCase))
                    tier = Ambient.AmbientTimeOfDay.Overcast;
                ambientDirector.SetTimeOfDay(tier);
            }
        }

        /// <summary>
        /// 定向退役旧水面：只禁 MeshRenderer 与 <see cref="Water.WaterTessellator"/>（enabled = false），
        /// **不关 GameObject**——同物体上的 <see cref="Water.WaterSimulationDriver"/> 是新海洋 shader
        /// 高度场全局变量（涟漪/泡沫累积/障碍绕射）的唯一发布者，整物体失活会连带停掉它并触发其
        /// OnDisable 把 <c>_WaterSimEnabled</c> 清 0，高度场路径整体静默失效，所以驱动必须常驻。
        /// 物体上没有碰撞体（Transform/Filter/Renderer/Tessellator/Driver 五件套），保持活跃不会挡
        /// 瞄准/爆炸射线；渲染与逐帧细分随两个组件停用，无残留开销。带 null 容错：旧场景缺某个组件
        /// 时静默跳过。旧竞技场模式不进世界地图分支、不走本方法，水面行为完全不变。
        /// </summary>
        void RetireLegacyWaterPlane()
        {
            if (waterPlane == null)
                return;

            Renderer legacyRenderer = waterPlane.GetComponent<Renderer>();
            if (legacyRenderer != null)
                legacyRenderer.enabled = false;

            Water.WaterTessellator legacyTessellator = waterPlane.GetComponent<Water.WaterTessellator>();
            if (legacyTessellator != null)
                legacyTessellator.enabled = false;
        }

        /// <summary>
        /// 世界地图模式下隐藏烘焙的 IslandLayer 小地图底板——它按原版关卡网格烘死（IslandTile_*），
        /// 与世界地图的栅格不符；小地图的帧框与运行时单位点不受影响。同场景一次性清理，非跨模块引用。
        /// </summary>
        void HideBakedMinimapTiles()
        {
            foreach (var image in FindObjectsOfType<UnityEngine.UI.Image>(true))
            {
                if (image.name.StartsWith("IslandTile_") && image.transform.parent != null)
                {
                    image.transform.parent.gameObject.SetActive(false);
                    return; // 全部 IslandTile_* 同属一层，关掉父级一次即可
                }
            }
        }

        /// <summary>
        /// 3D 化决策：把 PhysX 全局重力设为 Flash weight=1 的等价重力（-19.53125），
        /// 并把物理帧率设为原版 25fps，使离散积分与 Ballistics 逐步一致（预览 = 实弹）。
        /// 角色/武器的 Rigidbody 用 <c>useGravity</c> 吃这份全局重力；weight=0 的 cannonball
        /// 由其实弹脚本自行 <c>useGravity = false</c>，weight=1.5 的 boulder 同理自定义。
        /// </summary>
        void ApplyPhysicsConvention()
        {
            Physics.gravity = LevelGeometry.WorldGravity(CrewCatalog.Weight);
            Time.fixedDeltaTime = LevelGeometry.FrameSeconds;
        }

        void SpawnTeams()
        {
            if (piratePrefab == null && !HasAnyCrewVisualPrefab())
            {
                Debug.LogError("[BattleController] 未配置 PirateBase 预制体，无法生成出战单位。");
                return;
            }

            string[] activeSymbols = ResolveActiveRosterSymbols();
            bool filterRed = activeSymbols != null && CountRedMatching(activeSymbols) > 0;

            for (int i = 0; i < _plan.Entries.Count; i++)
            {
                SpawnPlanEntry entry = _plan.Entries[i];
                if (entry.TeamIndex == 0 && filterRed && !MatchesAny(entry.TypeName, activeSymbols))
                    continue;

                Transform root = entry.TeamIndex == 0 ? team0Root : team1Root;
                if (root == null)
                    root = transform;

                // 地形抬升：单位脚底要落在该格地表上（LevelGeometry 的 WorldPosition 只算基础地面，
                // 地形高度在 BattleController 这一层叠加，避免改 LevelGeometry 的口径）。
                Vector3 spawnPosition = entry.WorldPosition;
                if (Terrain != null)
                {
                    spawnPosition.y = Terrain.SurfaceWorldY(entry.GridX, entry.GridY)
                                      + LevelGeometry.UnitPivotHeight;
                }

                var spawnEntry = new SpawnPlanEntry(
                    entry.TeamIndex, entry.TypeName, entry.Luck, entry.GridX, entry.GridY,
                    spawnPosition, entry.InitialWeapons);

                // 按职业外观档取预制体；未命中回落 piratePrefab（docs/角色造型规范.md §3 职业表）。
                PirateBase prefab = ResolveCrewVisualPrefab(entry.TypeName) ?? piratePrefab;
                if (prefab == null)
                    continue;

                PirateBase pirate = Instantiate(prefab, spawnPosition, Quaternion.identity, root);
                pirate.Initialize(_nextPirateId++, spawnEntry);
                _allPirates.Add(pirate);
                _teams[entry.TeamIndex].Add(pirate);
            }

            _spawned = true;
        }

        /// <summary>是否至少配置了一个职业视觉预制体。</summary>
        bool HasAnyCrewVisualPrefab()
        {
            if (crewVisualPrefabs == null)
                return false;
            for (int i = 0; i < crewVisualPrefabs.Length; i++)
            {
                if (crewVisualPrefabs[i].prefab != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 按战斗导出符号映射到职业外观档，取对应预制体；未命中返回 null（调用方回落 piratePrefab）。
        /// 映射规则见 <see cref="CrewVisualCatalog.ProfessionFromBattleSymbol"/>。
        /// </summary>
        PirateBase ResolveCrewVisualPrefab(string typeName)
        {
            if (crewVisualPrefabs == null || crewVisualPrefabs.Length == 0)
                return null;

            CrewProfession profession = CrewVisualCatalog.ProfessionFromBattleSymbol(typeName);
            for (int i = 0; i < crewVisualPrefabs.Length; i++)
            {
                if (crewVisualPrefabs[i].profession == profession && crewVisualPrefabs[i].prefab != null)
                    return crewVisualPrefabs[i].prefab;
            }

            return null;
        }

        /// <summary>
        /// 当前编成阵容对应的战斗导出符号（§4.2）。返回 null 表示**不做过滤**（回落全队）：
        ///   · 非战役入口（<see cref="CampaignApi.PendingLevelId"/> 为空）——主菜单直进 / 2P / PlayMode
        ///     直接加载场景，编成不应改变关卡作者写好的出征名单；
        ///   · 编成为空，或没有一名船员能映射到 <see cref="Data.CrewCatalog"/> 的导出符号。
        ///
        /// 【为什么用「战役入口」做开关】<c>CrewManagementApi.Roster</c> 是个跨场景常驻的静态名册，
        /// 初始名册（sailor）始终非空，若无条件过滤会让「直接 Play 战斗场景」从 5 人变 1 人
        /// （破坏既有 PlayMode 用例与手感）。战役入口才代表「本局按编成出征」。
        /// </summary>
        string[] ResolveActiveRosterSymbols()
        {
            if (CampaignApi.PendingLevelId == null)
                return null;

            IReadOnlyList<string> active = CrewManagementApi.Roster.Active;
            if (active == null || active.Count == 0)
                return null;

            var symbols = new List<string>(active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                if (CrewRosterCatalog.TryGet(active[i], out CrewRosterEntry entry)
                    && !string.IsNullOrEmpty(entry.BattleSymbol)
                    && !symbols.Contains(entry.BattleSymbol))
                {
                    symbols.Add(entry.BattleSymbol);
                }
            }

            return symbols.Count > 0 ? symbols.ToArray() : null;
        }

        int CountRedMatching(string[] symbols)
        {
            int n = 0;
            for (int i = 0; i < _plan.Entries.Count; i++)
            {
                SpawnPlanEntry entry = _plan.Entries[i];
                if (entry.TeamIndex == 0 && MatchesAny(entry.TypeName, symbols))
                    n++;
            }
            return n;
        }

        static bool MatchesAny(string typeName, string[] symbols)
        {
            if (string.IsNullOrEmpty(typeName) || symbols == null)
                return false;
            for (int i = 0; i < symbols.Length; i++)
            {
                if (string.Equals(typeName, symbols[i], System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>取队伍（teamIndex 0/1）。</summary>
        public BattleTeam GetTeam(int teamIndex)
        {
            if (teamIndex < 0 || teamIndex >= _teams.Length)
                return null;
            return _teams[teamIndex];
        }

        /// <summary>§3.2 startTurn：对本队全部存活角色做重置（行动经济 / evilness / 保底武器）。</summary>
        public void ResetTeamForTurnStart(BattleTeam team)
        {
            if (team == null)
                return;

            IReadOnlyList<PirateBase> characters = team.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                PirateBase c = characters[i];
                if (c != null && c.Alive)
                    c.ResetForTurnStart();
            }
        }

        /// <summary>inactivity 判定：是否有角色/弹体在动，或玩家正在瞄准。</summary>
        public bool IsAnythingActive()
        {
            for (int i = 0; i < _allPirates.Count; i++)
            {
                PirateBase pirate = _allPirates[i];
                if (pirate != null && pirate.Alive && pirate.IsMoving())
                    return true;
            }

            // 弹体飞行期间回合不应推进（§3.1「武器在飞」也算活动）。
            for (int i = 0; i < _projectiles.Count; i++)
            {
                if (_projectiles[i] != null && _projectiles[i].IsInFlight)
                    return true;
            }

            return aimController != null && aimController.IsAiming;
        }

        /// <summary>选中角色（AimThrowController 在点选命中后调用）。</summary>
        public void SelectCharacter(PirateBase pirate)
        {
            BattleTeam team = CurrentTeam;
            if (team == null || pirate == null || !pirate.Alive || pirate.TeamIndex != team.TeamIndex)
                return;

            // 已经选过同一角色（continueTurn）或尚未选人时才允许。
            bool again = team.SelectedCharacter == pirate;
            if (!team.Select(pirate, again))
                return;

            if (aimController != null)
                aimController.ResetForSelection(pirate);

            // action_selected 在真正执行动作（抛自己/用武器/end go）时由 AimThrowController 发布；
            // 这里只做镜头聚焦与清零 inactivity（选择本身也是"有活动"）。
            EventBus.Publish(BattleEvents.CameraFocusRequested, pirate.transform);
            if (turnManager != null)
                turnManager.NotifyActivity();
        }

        /// <summary>广播一次动作选择（AimThrowController 在真正执行动作时调用）。</summary>
        public void NotifyActionSelected(PirateBase pirate, BattleActionKind kind)
        {
            if (pirate == null)
                return;

            EventBus.Publish(BattleEvents.ActionSelected, new ActionSelectedPayload(
                pirate.PirateId, pirate.TeamIndex, kind));
            if (turnManager != null)
                turnManager.NotifyActivity();
        }

        // ------------------------------------------------------------------
        // §5.3 爆炸结算
        // ------------------------------------------------------------------

        /// <summary>
        /// 一次爆炸结算：用 <see cref="Physics.OverlapSphere"/> 取候选，再全部交给
        /// <see cref="ExplosionResolver.Resolve"/> 算分（半径/衰减/击退/evilness 均为纯逻辑）。
        /// </summary>
        /// <param name="worldCenter">爆心世界坐标（Unity，y 向上）。</param>
        /// <param name="size">爆炸 size（Flash px，radius = size/2 + 20）。</param>
        /// <param name="maxDamage">爆心最大伤害（Flash）。</param>
        /// <param name="caster">施暴者（累加 evilness；可为 null，如火药桶连锁）。</param>
        public ExplosionResult ResolveExplosion(Vector3 worldCenter, float size, float maxDamage, PirateBase caster)
        {
            return ResolveExplosion(worldCenter, size, maxDamage, caster, null);
        }

        /// <summary>
        /// 一次爆炸结算的完整入口。
        /// </summary>
        /// <param name="source">本次爆炸的弹体自身（连锁扫描时跳过，避免自触发）；可为 null。</param>
        public ExplosionResult ResolveExplosion(
            Vector3 worldCenter, float size, float maxDamage, PirateBase caster, WeaponProjectile source)
        {
            float radiusWorld = LevelGeometry.PixelsToUnits(ExplosionResolver.Radius(size));
            Collider[] overlaps = Physics.OverlapSphere(
                worldCenter, radiusWorld, pirateLayerMask, QueryTriggerInteraction.Ignore);

            var candidates = new List<PirateBase>(overlaps.Length);
            var targets = new List<ExplosionTarget>(overlaps.Length);

            for (int i = 0; i < overlaps.Length; i++)
            {
                PirateBase pirate = overlaps[i] != null ? overlaps[i].GetComponentInParent<PirateBase>() : null;
                if (pirate == null || candidates.Contains(pirate))
                    continue;

                candidates.Add(pirate);
                Vector3 position = pirate.transform.position;
                Vector2 px = LevelGeometry.ArenaToPixel(position);
                // 高度也换算到 Flash px 口径，使 3D 距离的平面分量与高度分量同尺度
                // （否则"高度差"会被按世界单位参与、与 px 混算，爆炸范围会失真）。
                targets.Add(new ExplosionTarget(
                    px.x, px.y, LevelGeometry.UnitsToPixels(position.y), pirate.Alive));
            }

            Vector2 centerPx = LevelGeometry.ArenaToPixel(worldCenter);
            ExplosionResult result = ExplosionResolver.Resolve(
                size, maxDamage, centerPx.x, centerPx.y,
                LevelGeometry.UnitsToPixels(worldCenter.y), targets);

            for (int i = 0; i < result.Hits.Length; i++)
            {
                ExplosionHit hit = result.Hits[i];
                PirateBase target = candidates[hit.Index];

                // ExplosionResolver 已 3D 泛化（docs/M2-3D空间模型对齐.md §5）：
                // 击退的平面两分量 → 世界 (X, Z)，竖直输出 DeltaVUp（世界 +Y，含固定 6k 抬升）→ 世界 Y。
                // 单位换算仍走 LevelGeometry.FlashSpeedScale（Flash px/帧 → 世界单位/秒）。
                Vector3 deltaV = LevelGeometry.FlashVelocityDeltaToArena(hit.DeltaVx, hit.DeltaVy);
                deltaV.y = hit.DeltaVUp * LevelGeometry.FlashSpeedScale;
                target.ApplyImpulseDelta(deltaV);
                target.SubtractHealth(hit.Damage);
            }

            if (caster != null)
                caster.AddEvilness(result.EvilnessGain);

            // 【瓦片地形破坏】爆炸同时整格摧毁半径内的地形块（§5.4：角色/AI 可借墙弹；
            // 文档未定义地形 HP，本工程取「一次爆炸整格摧毁」为最简可玩口径，见 TileTerrainGrid 类头）。
            DestroyTerrainInBlast(worldCenter, radiusWorld);

            // §5.3：对箱体以距离判定命中即 box.explode()（火药桶连锁）。
            // M2 近似：以爆心球形 OverlapSphere 扫描场上的可连锁弹体（文档为 AABB 最近点距离）。
            TriggerChainReactions(worldCenter, radiusWorld, source);

            return result;
        }

        /// <summary>爆炸范围内整格摧毁地形块，并通知视图刷新。</summary>
        void DestroyTerrainInBlast(Vector3 worldCenter, float radiusWorld)
        {
            // 【样板三关】地形不可摧毁（原版语义：原版没有地形破坏，只有木箱/火药桶可破坏——
            // 逆向文档 §对比表）。样板关的格子只是隐形逻辑高度场，炸了会让站位高度漂移。
            // 【M4 世界地图】同理禁破坏：站面碰撞是独立 BoxCollider，炸格子只会造成
            // 「逻辑说有洞、碰撞还在」的失真。
            if (SceneArt.ShowcaseLevels.IsShowcase(LevelNumber) || _worldMap != null)
                return;

            if (Terrain == null)
                return;

            _destroyedTerrainCells.Clear();
            int destroyed = Terrain.DestroyInRadius(worldCenter, radiusWorld, _destroyedTerrainCells);
            if (destroyed > 0 && terrainView != null)
                terrainView.ApplyDestruction(_destroyedTerrainCells);
        }

        /// <summary>扫描爆炸范围内可连锁引爆的弹体（§5.2 gunpowderBarrel），逐个触发。</summary>
        void TriggerChainReactions(Vector3 worldCenter, float radiusWorld, WeaponProjectile source)
        {
            Collider[] overlaps = Physics.OverlapSphere(
                worldCenter, radiusWorld, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlaps.Length; i++)
            {
                WeaponProjectile projectile = overlaps[i] != null
                    ? overlaps[i].GetComponentInParent<WeaponProjectile>()
                    : null;
                if (projectile == null || projectile == source || !projectile.TriggersOnBlast)
                    continue;

                projectile.DetonateFromBlast();
            }
        }

        // ------------------------------------------------------------------
        // §5.1/§5.2 武器弹体生成
        // ------------------------------------------------------------------

        /// <summary>场上全部弹体（只读，调试/测试用）。</summary>
        public IReadOnlyList<WeaponProjectile> AllProjectiles => _projectiles;

        /// <summary>
        /// 使用一件武器：按 <see cref="ProjectileSpawnPlanner"/> 生成弹体（放置类生成 N 个），
        /// 并接上物理与引爆判定。返回生成的弹体数量。
        ///
        /// 【单一入口】<see cref="AimThrowController"/>（玩家）与 <see cref="AiController"/>（AI）
        /// 都必须经此生成，避免两条路径各写一套。
        /// </summary>
        /// <param name="stats">武器数值（来自 <see cref="WeaponCatalog"/>）。</param>
        /// <param name="owner">投掷者（累加 evilness；可为 null）。</param>
        /// <param name="ownerWorldPosition">投掷者位置（弹弓发射点）。</param>
        /// <param name="aimWorldPosition">瞄准落点（放置类铺开中心）。</param>
        /// <param name="vxFlash">弹弓初速 vx（Flash px/帧）。</param>
        /// <param name="vyFlash">弹弓初速 vy（Flash px/帧）。</param>
        public int SpawnWeaponProjectiles(
            WeaponStats stats, PirateBase owner,
            Vector3 ownerWorldPosition, Vector3 aimWorldPosition,
            float vxFlash, float vyFlash)
        {
            IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                stats, ownerWorldPosition, aimWorldPosition, vxFlash, vyFlash);

            for (int i = 0; i < plan.Count; i++)
                CreateProjectile(stats, owner, plan[i]);

            return plan.Count;
        }

        WeaponProjectile CreateProjectile(WeaponStats stats, PirateBase owner, in ProjectileSpawn spawn)
        {
            ProjectileProfile profile = ProjectileProfile.FromStats(stats);

            GameObject go;
            Rigidbody body;
            BoxCollider box;

            if (projectilePrefab != null)
            {
                go = Instantiate(projectilePrefab, spawn.WorldPosition, Quaternion.identity, transform);
                body = go.GetComponent<Rigidbody>();
                if (body == null)
                    body = go.AddComponent<Rigidbody>();
                box = go.GetComponent<BoxCollider>();
                if (box == null)
                    box = go.AddComponent<BoxCollider>();
            }
            else
            {
                go = new GameObject("WeaponProjectile_" + stats.DisplayName);
                go.transform.SetParent(transform, false);
                go.transform.position = spawn.WorldPosition;

                BuildFallbackVisual(go.transform, profile, stats.Id);

                body = go.AddComponent<Rigidbody>();
                box = go.AddComponent<BoxCollider>();
            }

            box.size = new Vector3(profile.ColliderWidth, profile.ColliderHeight, profile.ColliderDepth);
            box.isTrigger = false;

            WeaponProjectile projectile = go.GetComponent<WeaponProjectile>();
            if (projectile == null)
                projectile = go.AddComponent<WeaponProjectile>();

            projectile.Initialize(stats, this, owner, spawn.Kinematic, spawn.WorldVelocity);

            if (!_projectiles.Contains(projectile))
                _projectiles.Add(projectile);

            return projectile;
        }

        /// <summary>程序化兜底外观：图元 + 颜色（无 Prefab 时仍可区分武器）。</summary>
        static void BuildFallbackVisual(Transform parent, ProjectileProfile profile, WeaponId id)
        {
            GameObject visual = GameObject.CreatePrimitive(
                profile.Shape == ProjectileShape.Box ? PrimitiveType.Cube : PrimitiveType.Sphere);
            visual.name = "Visual";
            visual.transform.SetParent(parent, false);
            visual.transform.localScale = new Vector3(
                profile.ColliderWidth, profile.ColliderHeight, profile.ColliderDepth);

            // 物理碰撞由根节点的 BoxCollider 负责；图元自带碰撞体移除，避免重复。
            Collider primitiveCollider = visual.GetComponent<Collider>();
            if (primitiveCollider != null)
                Destroy(primitiveCollider);

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                if (shader != null)
                {
                    var material = new Material(shader);
                    material.color = TintFor(id);
                    renderer.material = material;
                }
                else
                {
                    // 找不到 shader 时禁用渲染器而不是留着图元默认材质——
                    // 播放器构建里 builtin 默认材质会被剥离，渲染成粉色方块。
                    renderer.enabled = false;
                }
            }
        }

        /// <summary>武器颜色（仅表现，便于在场景里区分弹体）。</summary>
        static Color TintFor(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Cannonball: return new Color(0.15f, 0.15f, 0.18f);
                case WeaponId.CherryBomb: return new Color(0.85f, 0.1f, 0.12f);
                case WeaponId.Dynamite: return new Color(0.9f, 0.3f, 0.08f);
                case WeaponId.Boulder: return new Color(0.45f, 0.42f, 0.38f);
                case WeaponId.Banana: return new Color(0.95f, 0.85f, 0.15f);
                case WeaponId.Mine: return new Color(0.25f, 0.25f, 0.3f);
                case WeaponId.ParachuteBomb: return new Color(0.2f, 0.35f, 0.8f);
                case WeaponId.RumBottle: return new Color(0.3f, 0.7f, 0.35f);
                case WeaponId.PiecesOfEight: return new Color(0.95f, 0.78f, 0.2f);
                case WeaponId.GunpowderBarrel: return new Color(0.5f, 0.28f, 0.1f);
                case WeaponId.WoodenCrate: return new Color(0.6f, 0.42f, 0.2f);
                default: return Color.white;
            }
        }

        /// <summary>由弹体在 <c>OnDestroy</c> 反注册。</summary>
        public void UnregisterProjectile(WeaponProjectile projectile)
        {
            if (projectile != null)
                _projectiles.Remove(projectile);
        }

        /// <summary>
        /// 半径内的全部角色（三维 <see cref="Physics.OverlapSphere"/>，按实例去重），写入 <paramref name="results"/>。
        /// 供 6 把特殊武器的纯规则/胶水层做范围判定（tidalWave 横扫、anchor 命中、seagull 目标、
        /// cannon AI 选敌、SweepingFlame 命中）——避免各弹体各写一份 OverlapSphere + GetComponentInParent。
        /// </summary>
        public void CollectPiratesInRadius(Vector3 worldCenter, float radiusWorld, List<PirateBase> results)
        {
            if (results == null)
                return;

            results.Clear();
            Collider[] overlaps = Physics.OverlapSphere(
                worldCenter, radiusWorld, pirateLayerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlaps.Length; i++)
            {
                PirateBase pirate = overlaps[i] != null
                    ? overlaps[i].GetComponentInParent<PirateBase>()
                    : null;
                if (pirate != null && !results.Contains(pirate))
                    results.Add(pirate);
            }
        }

        /// <summary>
        /// rumBottle 落地（引爆）时额外生成 2 个 SweepingFlame（§5.2 rumBottle 行 / 表格末行）。
        /// 生成计划与其它武器共用 <see cref="ProjectileSpawnPlanner"/>，保证"数据只有一份"。
        /// </summary>
        public int SpawnSweepingFlames(Vector3 worldCenter)
        {
            WeaponStats stats = WeaponCatalog.Get(WeaponId.SweepingFlame);
            IReadOnlyList<ProjectileSpawn> plan = ProjectileSpawnPlanner.Plan(
                stats, worldCenter, worldCenter, 0f, 0f);

            for (int i = 0; i < plan.Count; i++)
                CreateProjectile(stats, null, plan[i]);

            return plan.Count;
        }

        /// <summary>该队是否由 AI 控制（cannon 的 §6.3 <c>aiFireTime=25</c> 自动发射判定用）。</summary>
        public bool IsTeamAi(int teamIndex)
        {
            BattleTeam team = GetTeam(teamIndex);
            return team != null && team.AiControlled;
        }

        // ------------------------------------------------------------------
        // §3.3 胜负与得分
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // §3.3 胜负与得分
        // ------------------------------------------------------------------

        /// <summary>检查并结算对局结果（任一方全灭即结束）。幂等。</summary>
        public void CheckMatchOver()
        {
            if (_matchFinished)
                return;

            bool team0Alive = _teams[0].AnyAlive;
            bool team1Alive = _teams[1].AnyAlive;
            if (!TurnRules.IsMatchOver(team0Alive, team1Alive))
                return;

            _matchFinished = true;

            MatchOutcome outcome = TurnRules.ComputeOutcome(team0Alive, team1Alive, _teams[1].AiControlled);

            // §3.3 / §7.3：1P 才有该得分口径；2P 热座记 0。
            int score = 0;
            if (_teams[1].AiControlled)
                score = ScoreRules.LevelScore(_teams[0].AverageHealth, _teams[0].TotalTurnsTaken, LevelNumber);

            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                (int)outcome, score, _teams[1].AiControlled));
        }
    }
}
