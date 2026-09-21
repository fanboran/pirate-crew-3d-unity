using System;
using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using PirateCrew.Visual;
using PirateCrew.Battle.Levels;
using PirateCrew.Battle.WorldMaps;
using UnityEngine;

namespace PirateCrew.Battle
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
    /// 【对应章节】§4.3（按布阵坐标/队伍实例化出战单位）、§5.5（水位 = waterTileY*32）、
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
    ///
    /// 【内容来源】关卡数据全部来自**关卡资产**（<c>Assets/Data/**</c>）：海图
    /// （<c>WorldMapCatalog</c> 的 8 张）与关卡快照（<c>SceneArt.ShowcaseLevels</c> 读的 1–3）。
    /// 本类**不再判定内容来源**——优先级（-artReviewLevel 覆盖 &gt; -worldMap 待战 &gt; 兜底关 1）
    /// 只写在 <see cref="LevelSourceResolver"/> 一处，结果经 <see cref="LevelSource"/> 注入。
    /// 原「场景 level 资产 / 战役注入关卡序号 / LevelCatalog 33 关」三条一代链路已随一代退场删除。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleController : MonoBehaviour
    {
        [Header("组装引用（场景内直连）")]
        [SerializeField] PirateBase piratePrefab;
        [Tooltip("按职业外观档覆盖预制体（可选）。命中则用职业预制体，未命中/为空回落 piratePrefab；"
                 + "生成顺序与职业外观由 CrewVisualPrefabBuilder 产出，见 docs/角色造型规范.md §3。")]
        [SerializeField] CrewVisualPrefabEntry[] crewVisualPrefabs = new CrewVisualPrefabEntry[0];
        [SerializeField] Transform team0Root;
        [SerializeField] Transform team1Root;
        [Tooltip("水面物体（承载常驻的 WaterSimulationDriver 水模拟；其 MeshRenderer/WaterTessellator "
                 + "已在装配期禁用，海面渲染统一走 OceanRig）。运行时只把 y 设为水位（§5.5）。")]
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
        readonly BattleTeam[] _teams = new BattleTeam[2];
        BattlePlan _plan;
        /// <summary>本局的关卡来源（计划/地形/水位/图幅/氛围档都在它里面；解析只发生在 Battle/Levels）。</summary>
        LevelSource _source;
        /// <summary>M4：本局激活的世界地图（null = 关卡资产路径）。</summary>
        WorldMapDefinition _worldMap;
        bool _spawned;
        bool _matchFinished;
        float _waterWorldY;
        int _nextPirateId;

        /// <summary>本场战斗序号（世界图 101–108 / 样板 1–3）。</summary>
        public int LevelNumber => _plan != null ? _plan.LevelNumber : 1;

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
            if (!BuildLevelSource())
                return;

            RebuildSceneArt();
            ApplyPhysicsConvention();
            SpawnTeams();
        }

        void Start()
        {
            // Awake 取不到关卡资产时已报错，这里不再往下推进（回合/结算都需要出战计划）。
            if (_plan == null)
                return;

            if (waterPlane != null)
            {
                Vector3 p = waterPlane.position;
                p.y = _waterWorldY;
                waterPlane.position = p;
            }

            SetupBattleEnvironment();

            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(LevelNumber, TeamCount));

            if (turnManager != null)
                turnManager.StartBattle();
            else
                Debug.LogError("[BattleController] 未接线 TurnManager，回合不会推进。");
        }

        void OnEnable()
        {
            // §5.2 limitedToTurn=true 的弹体在回合结束时销毁；常驻类（mine/箱体）保留。
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
        }

        void OnTurnEnded(int teamNumber)
        {
            // 载荷是队伍编号，本方法只做“回合结束”的收尾（清掉到期的弹体），不用队伍号。
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

        /// <summary>
        /// 组装本局：**关卡从哪里来**这件事整块交给 <see cref="LevelSourceResolver"/>
        /// （全仓唯一的内容来源判定处；重构前这段分叉散在本类的 BuildPlan / BuildTerrain /
        /// RebuildSceneArt / SetupBattleEnvironment 四处，战斗根类同时是内容路由器）。
        ///
        /// 解析结果把出战计划、逻辑高度场、水位、图幅、氛围档全部算好，本方法只做两件本地事：
        /// 取出计划/地形（供 <see cref="Plan"/> / <see cref="Terrain"/> 查询），以及给非海图路径
        /// 建碰撞层（<c>BattleTerrainView</c> 只建隐形 BoxCollider，不建格子渲染层）。
        /// </summary>
        /// <returns>取到关卡内容返回 true；资产缺失（构建配置错误）返回 false 并报错。</returns>
        bool BuildLevelSource()
        {
            _source = LevelSourceResolver.Resolve();
            if (_source == null)
            {
                Debug.LogError("[BattleController] 取不到任何关卡数据：检查 Assets/Data/Levels/Resources/LevelCatalog.asset "
                    + "是否已生成并列入构建（或开发者机上的 Assets/Data/**/_golden/*.json 是否齐全）。本局不组建战斗。");
                return false;
            }

            if (!string.IsNullOrEmpty(_source.Notice))
                Debug.LogWarning(_source.Notice);

            _worldMap = _source.WorldMap;
            _plan = _source.Plan;
            Terrain = _source.Terrain;
            _waterWorldY = _source.WaterWorldY;

            _teams[0] = new BattleTeam(1, aiControlled: false);
            _teams[1] = new BattleTeam(2, aiControlled: team1IsAi);

            // 非海图路径的地形只有碰撞层（视觉由烘焙件负责）；海图路径的碰撞与灰盒由 WorldMapComposer 摆。
            if (!_source.IsWorldMapActive && terrainView != null)
                terrainView.RenderCollidersOnly(Terrain);

            return true;
        }

        /// <summary>
        /// 按内容来源重建静态陈设。世界图：kit 件 + 灰盒站面由 <see cref="WorldMapComposer"/> 摆放；
        /// 关卡资产（样板关等）：<see cref="RuntimeSceneArt"/> 走烘焙 prefab 装配（云朵/碎岛/危险虚线）。
        /// 只做表现：不触碰地形协议。
        /// </summary>
        void RebuildSceneArt()
        {
            Transform artRoot = sceneArt != null ? sceneArt.transform : transform;

            switch (_source.Kind)
            {
                case LevelSourceKind.WorldMap:
                    WorldMapComposer.Build(artRoot, _source.WorldMap, worldMapAssetSet);
                    break;

                default:
                    if (sceneArt != null)
                        sceneArt.RebuildFor(_source.LevelNumber);
                    break;
            }
        }

        /// <summary>
        /// 战斗环境接线（Start 调用，**所有内容来源统一走这一条路径**）：
        /// 大海域海面（<see cref="Water.OceanRig"/> 圆盘，世界图按图幅、样板关按竞技场外扩）、
        /// 水模拟域重配（障碍掩码按本局地形实时烘）。落水死亡仍是纯 Y 阈值判定，不依赖水面碰撞。
        ///
        /// 【退役器为何删除（2026-09-19，管线合并前置）】旧场景靠运行时退役器藏一代残留
        /// （Seabed_* 海床 / 烘焙小地图瓦层 / 旧水面渲染）。场景重烘后：装配器已不产 Seabed_* 与
        /// 烘焙瓦层，旧水面的 Renderer/WaterTessellator 在 <c>M2BattleSceneSetup.CreateWaterPlane</c>
        /// 烘焙期就置为禁用——渲染退役在场景层落实，运行时不再需要任何"定向隐藏"逻辑。
        ///
        /// 【水模拟契约】<c>Water.WaterSimulationDriver</c> 常驻（其物体失活会触发 OnDisable 清
        /// <c>_WaterSimEnabled</c>，高度场路径整体静默失效，所以驱动必须活跃），模拟域按本局重配：
        /// 域心 = 图心、域边长随最大跨度（<see cref="Water.WaterSimRules.WorldDomainSizeForSpan"/>，
        /// clamp [128, 256]）；地形栅格传入后障碍掩码按站面实时烘，涟漪在岛缘反射、不再穿岛。
        /// 直接组件调用沿用 <c>Water.OceanRig.Create</c> / <c>Water.WaterSimulationDriver.InjectSplash</c>
        /// 的先例，不做 GameObject.Find。
        /// </summary>
        void SetupBattleEnvironment()
        {
            // 域尺寸与环境半径：海图取图幅、关卡资产取竞技场世界尺寸——差别已在 LevelSource 里算完，
            // 本方法只读数字（不判"这局是不是海图"）。
            float spanX = _source.SpanX;
            float spanZ = _source.SpanZ;
            var arenaCenter = new Vector2(spanX * 0.5f, spanZ * 0.5f);

            // 水模拟域重配（装配期一次）：驱动常驻后其 Awake 推出的默认域仍钉在旧竞技场口径，
            // 需要显式搬到本局图心；Instance 为空（未接线）时静默跳过，不阻塞装配。
            // 地形栅格（Awake → BuildLevelSource 已建）一并传入：障碍掩码按站面实时烘，
            // 涟漪在岛缘反射、不再穿岛（视觉审计 §四.2）。
            if (Water.WaterSimulationDriver.Instance != null)
            {
                Water.WaterSimulationDriver.Instance.ConfigureWorldDomain(
                    arenaCenter,
                    Mathf.Max(spanX, spanZ),
                    Terrain,
                    LevelGeometry.WaterSurfaceY,
                    _source.ArenaWorldWidth,
                    _source.ArenaWorldDepth);
            }

            Water.OceanRig.Create(
                Water.OceanConfig.ForArena(
                    arenaCenter, spanX * 0.5f, spanZ * 0.5f),
                material: worldOceanMaterial,
                parent: transform,
                followCamera: battleCamera != null ? battleCamera.GetComponent<Camera>() : null);

            Camera cam = battleCamera != null ? battleCamera.GetComponent<Camera>() : Camera.main;
            if (cam != null)
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4500f);

            // 大地图专属（图幅/氛围档由关卡数据给出）：全景档随图幅 + 氛围档按海图定义；
            // 关卡资产路径这两项都是"未提供"（CameraWorldSpan = 0 / AmbientTier = null），
            // 于是保持场景烘焙的正午档与既有相机边界。
            if (_source.CameraWorldSpan > 0f)
            {
                if (battleCamera != null)
                    battleCamera.SetWorldSpan(_source.CameraWorldSpan);
            }

            if (!string.IsNullOrEmpty(_source.AmbientTier))
                SetupWorldMapAmbient(_source.AmbientTier);
        }

        /// <summary>海图的氛围档接线：Storm→Overcast、Dusk→Dusk、其余→Noon。</summary>
        void SetupWorldMapAmbient(string ambientTier)
        {
            var ambientDirector = FindObjectOfType<Ambient.AmbientDirector>();
            if (ambientDirector == null)
                return;

            Ambient.AmbientTimeOfDay tier = Ambient.AmbientTimeOfDay.Noon;
            if (string.Equals(ambientTier, "Dusk", StringComparison.OrdinalIgnoreCase))
                tier = Ambient.AmbientTimeOfDay.Dusk;
            else if (string.Equals(ambientTier, "Storm", StringComparison.OrdinalIgnoreCase))
                tier = Ambient.AmbientTimeOfDay.Overcast;
            ambientDirector.SetTimeOfDay(tier);
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

            for (int i = 0; i < _plan.Entries.Count; i++)
            {
                SpawnPlanEntry entry = _plan.Entries[i];
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

            // §5.3：对箱体以距离判定命中即 box.explode()（火药桶连锁）。
            // M2 近似：以爆心球形 OverlapSphere 扫描场上的可连锁弹体（文档为 AABB 最近点距离）。
            TriggerChainReactions(worldCenter, radiusWorld, source);

            return result;
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

            // 放置/常驻类（mine / gunpowderBarrel / woodenCrate / cannon）脚下补一张接触阴影面片：
            // 实时阴影全关，跨回合摆在地上的弹体若没有脚下压暗就会"浮"在沙面上（与船员同源问题，判据 A-6）。
            // 判据 = ProjectileProfile.IsPersistent（= !limitedToTurn，§5.2 里恰好是这四件）；
            // 飞行弹体（落地即爆/本回合即消失）不挂——面片没有可被看到的窗口。
            // 尺寸/贴地高度/不透明度的换算与理由见 ProjectileContactShadow 类头。
            if (ProjectileContactShadow.ShouldAttach(profile))
                ProjectileContactShadow.Attach(go.transform, profile);

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
