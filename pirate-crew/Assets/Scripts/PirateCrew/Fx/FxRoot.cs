using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Battle;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 特效总控：订阅 EventBus 的**现有**事件，把它们翻译成具体特效调用。
    ///
    /// 【为什么需要它（装配约束）】Battle.unity / Prefab 不在本 agent 的白名单里，不能靠场景装配把
    /// 特效组件挂上去；<see cref="FxBootstrap"/> 用 <c>RuntimeInitializeOnLoadMethod</c> 在进入播放时
    /// 自动建一个 <c>DontDestroyOnLoad</c> 的 <c>[FxRoot]</c>，因此**不需要改任何场景/Prefab**。
    ///
    /// 【订阅的事件（全部已登记在 docs/EventBus事件契约.md，未新增）】
    ///   · <c>battle_started</c>            → 建「PirateId → PirateBase」注册表、复位标记与拖尾跟踪；
    ///   · <c>battle_projectile_detonated</c>→ 爆心 y 在水面附近则水花，否则按武器是否有爆炸播爆炸/尘爆；
    ///   · <c>crew_damaged</c>              → 命中火花 + 尘土 + 伤害数字（位置经注册表由 id 还原）；
    ///   · <c>crew_died</c>                 → 落水（<c>PirateBase.Drowned</c>）播水花强反馈，否则播尘烟；
    ///   · <c>turn_started</c>              → 地面光环跟到本回合默认角色，用队伍色；
    ///   · <c>camera_focus_requested</c>    → 地面光环跟到被点选单位，用选中青；
    ///   · <c>match_finished</c>            → 收起光环。
    ///
    /// 【两处"绕路"（已向协调者报备，见交付报告"缺触发信息"）】
    ///   1. <c>crew_damaged</c> 载荷无世界坐标 → 用注册表由 PirateId 还原位置。注册表在
    ///      battle_started 时用一次 <c>FindObjectOfType&lt;BattleController&gt;</c> 建立。
    ///   2. 事件契约里没有"弹体生成" → 拖尾改为**低频轮询** `BattleController.AllProjectiles`
    ///      （0.25s 一次，只遍历弹体列表，不扫全场对象）。
    ///   两者都不改动 Battle/ 下任何文件；若协调者后续给这两个载荷补字段，本类直接受益。
    ///
    /// 【性能】Update 只做：计时器递减 + 光环脉动 + 每 0.25s 一次弹体列表遍历。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FxRoot : MonoBehaviour
    {
        /// <summary>弹体轮询间隔（秒）。事件契约没有弹体生成事件，用低频轮询替代。</summary>
        const float TrailPollInterval = 0.25f;

        /// <summary>判定"弹体落水"的水面抬高阈值（世界单位）：爆心 y 低于
        /// <c>WaterSurfaceY + 此值</c> 视为入水。取 0.1（格 1→2 单位 ×2）是为了不与"贴地爆炸（y≈0）"混淆。</summary>
        const float WaterDetectLift = 0.1f;

        /// <summary>弹体落水的默认水花冲击速度（世界单位/秒，事件载荷不带速度，【AI 提案】）。
        /// 速度类 ×2（格 1→2 单位；重力/落差翻倍 → 同场景落速确实翻倍）。</summary>
        const float ProjectileSplashSpeed = 10f;

        /// <summary>拖尾跟踪集合的修剪阈值（超过就按当前弹体重建，避免 id 集合无限增长）。</summary>
        const int TrailTrackLimit = 256;

        /// <summary>全局唯一实例（DontDestroyOnLoad）。</summary>
        public static FxRoot Instance { get; private set; }

        readonly Dictionary<int, PirateBase> _pirates = new Dictionary<int, PirateBase>(32);
        readonly HashSet<int> _trailedProjectiles = new HashSet<int>();

        BattleController _battle;
        TurnMarkerFx _marker;
        float _trailTimer;

        /// <summary>取现有实例；不存在则创建（幂等，可在任意时刻调用）。</summary>
        public static FxRoot Ensure()
        {
            if (Instance != null)
                return Instance;
            if (!Application.isPlaying)
                return null;

            var go = new GameObject("[FxRoot]");
            DontDestroyOnLoad(go);
            return go.AddComponent<FxRoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _marker = new TurnMarkerFx();
            // 必须挂在 [FxRoot] 之下：只有 [FxRoot] 是 DontDestroyOnLoad，标记若是独立根对象
            // 会在第一次切场景时被销毁，而 FxRoot 还留着已销毁的引用 → 光环永久消失。
            FxSpriteFx markerRing = FxSpriteFx.Create("FxTurnMarker", additive: true, billboard: false);
            markerRing.transform.SetParent(transform, false);
            _marker.Bind(markerRing);

            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Unsubscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);

            if (_marker != null)
                _marker.Dispose();

            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // 供 FxApi 调用的公开表面
        // ------------------------------------------------------------------

        /// <summary>让光环跟随目标并染成指定颜色。</summary>
        public void ShowMarker(Transform target, Color color)
        {
            if (_marker != null)
                _marker.Show(target, color);
        }

        /// <summary>收起光环。</summary>
        public void HideMarker()
        {
            if (_marker != null)
                _marker.Hide();
        }

        // ------------------------------------------------------------------
        // 事件处理
        // ------------------------------------------------------------------

        void OnBattleStarted(object payload)
        {
            // 事件载荷是 struct（装箱成 object），此处只做类型确认，不依赖其字段。
            if (!(payload is BattleStartedPayload))
                return;

            _battle = Object.FindObjectOfType<BattleController>();
            RebuildPirateRegistry();
            _trailedProjectiles.Clear();
            _trailTimer = 0f;
            if (_marker != null)
                _marker.Hide();
        }

        void OnProjectileDetonated(object payload)
        {
            if (!(payload is ProjectileDetonatedPayload detonated))
                return;

            // 入水引爆（§5.2 water 行为）：先判水，再判是否带爆炸。
            if (detonated.Position.y <= LevelGeometry.WaterSurfaceY + WaterDetectLift)
            {
                WaterSplashFx.Play(detonated.Position, ProjectileSplashSpeed);
                return;
            }

            WeaponStats stats = WeaponCatalog.Get(detonated.Weapon);
            if (stats.HasExplosion)
                ExplosionFx.Play(detonated.Position, stats.ExplosionSize);
            else
                ExplosionFx.PlayImpactPuff(detonated.Position, 1f);
        }

        void OnCrewDamaged(object payload)
        {
            if (!(payload is CrewDamagedPayload damaged))
                return;
            if (damaged.Damage < 0.5f)
                return;   // 0 伤害不弹数字（避免刷屏）

            PirateBase pirate = ResolvePirate(damaged.PirateId);
            if (pirate == null)
                return;

            HitFx.Play(pirate.transform.position, damaged.Damage, damaged.MaxHealth);
        }

        void OnCrewDied(object payload)
        {
            if (!(payload is CrewDiedPayload died))
                return;

            PirateBase pirate = ResolvePirate(died.PirateId);
            if (pirate == null)
                return;

            if (pirate.Drowned)
            {
                WaterSplashFx.PlayDrown(pirate.transform.position);
                // 落水同时向水面波动方程场注入涟漪（CrewDiedPayload 不带坐标，
                // 这里复用本类的 PirateId 注册表定位；只驱动观感，不参与判定）。
                Water.WaterSimulationDriver.InjectSplash(pirate.transform.position, 1f);
            }
            else
                HitFx.PlayDeathPuff(pirate.transform.position);

            if (_marker != null && _marker.Target == pirate.transform)
                _marker.Hide();
        }

        void OnTurnStarted(object payload)
        {
            if (!(payload is TurnStartedPayload turn))
                return;
            if (_marker == null)
                return;

            if (turn.PanTarget != null)
                _marker.Show(turn.PanTarget, FxRules.TeamMarkerColor(turn.TeamNumber));
            else
                _marker.Hide();
        }

        void OnCameraFocusRequested(object payload)
        {
            if (_marker == null)
                return;
            if (!(payload is Transform target) || target == null)
                return;

            // 玩家点选 → 选中青（与描边选中色同源，Art Bible §2.2）。
            _marker.Show(target, FxRules.SelectedMarkerColor());
        }

        void OnMatchFinished(object payload)
        {
            if (_marker != null)
                _marker.Hide();
        }

        // ------------------------------------------------------------------
        // Update：光环脉动 + 弹体拖尾低频轮询
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;

            if (_marker != null)
                _marker.Tick(dt);

            _trailTimer -= dt;
            if (_trailTimer > 0f)
                return;
            _trailTimer = TrailPollInterval;

            TrackProjectileTrails();
        }

        /// <summary>把拖尾挂到尚未跟踪的弹体上（列表来自 BattleController，不扫全场对象）。</summary>
        void TrackProjectileTrails()
        {
            if (_battle == null)
                return;

            IReadOnlyList<WeaponProjectile> projectiles = _battle.AllProjectiles;
            if (projectiles == null)
                return;

            if (_trailedProjectiles.Count > TrailTrackLimit)
                _trailedProjectiles.Clear();

            for (int i = 0; i < projectiles.Count; i++)
            {
                WeaponProjectile projectile = projectiles[i];
                if (projectile == null)
                    continue;

                int id = projectile.GetInstanceID();
                if (_trailedProjectiles.Contains(id))
                    continue;

                _trailedProjectiles.Add(id);
                ProjectileTrailFx.Attach(projectile.gameObject, projectile.WeaponId);
            }
        }

        // ------------------------------------------------------------------
        // PirateId → PirateBase 注册表
        // ------------------------------------------------------------------

        void RebuildPirateRegistry()
        {
            _pirates.Clear();

            if (_battle != null)
            {
                IReadOnlyList<PirateBase> list = _battle.AllPirates;
                for (int i = 0; i < list.Count; i++)
                {
                    PirateBase pirate = list[i];
                    if (pirate != null)
                        _pirates[pirate.PirateId] = pirate;
                }
                return;
            }

            // 回落：直接找场上的单位（事件先于 BattleStarted 到达等边缘情况）。
            PirateBase[] pirates = Object.FindObjectsOfType<PirateBase>();
            for (int i = 0; i < pirates.Length; i++)
            {
                if (pirates[i] != null)
                    _pirates[pirates[i].PirateId] = pirates[i];
            }
        }

        /// <summary>由 id 还原单位；未命中时重建注册表再试一次（应对"注册表建立前就受伤"的时序）。</summary>
        PirateBase ResolvePirate(int pirateId)
        {
            if (_pirates.TryGetValue(pirateId, out PirateBase pirate) && pirate != null)
                return pirate;

            RebuildPirateRegistry();
            return _pirates.TryGetValue(pirateId, out pirate) ? pirate : null;
        }
    }
}
