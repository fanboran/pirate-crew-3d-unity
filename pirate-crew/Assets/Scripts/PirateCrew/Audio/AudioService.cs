using System;
using System.Collections;
using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Audio.Synth;
using PirateCrew.Battle;
using PirateCrew.Combat;
using UnityEngine;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 音频服务（模块内静态服务，**刻意不放在 Core/**，避免动地基）。
    ///
    /// 【架构定位】模块级单例 MonoBehaviour，由自身的
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 在进入播放前自动创建并
    /// <c>DontDestroyOnLoad</c>——因此不需要修改 <c>Core/Bootstrapper</c> 或任何场景。
    /// 若协调者将来希望由 Bootstrapper 统一组装，只要在 Bootstrapper 里 AddComponent，
    /// Awake 的重复实例保护会自动让后到的实例自毁。
    ///
    /// 【触发方式（两条并存）】
    ///   ① **EventBus 订阅**（只订阅现有事件，不新增、不改他人文件；订阅表见
    ///      <see cref="SubscribedEvents"/>，可被测试断言）：
    ///      battle_started / turn_started / turn_ended / battle_shot_released /
    ///      battle_projectile_detonated / battle_mine_beep / crew_damaged / crew_died /
    ///      ai_decided / match_finished / scene_load_started。
    ///   ② **公开静态 API**（给没有事件的场合手动接线）：
    ///      <see cref="PlaySfx"/>（3D）、<see cref="PlaySfx2D"/>、<see cref="PlayUi"/>、
    ///      <see cref="PlayAmbient"/>、<see cref="StartAmbientBed"/>、<see cref="PlayMusic"/> 等。
    ///
    /// 【素材来源】程序化合成 + 隔壁 Game-2 自产 WAV 搬运（见 <see cref="Game2AudioAssets"/>）：
    /// 每个 id 解析成一组「变奏」剪辑，播放时随机取一个；同一段素材反复出现的问题
    /// 再由 <see cref="AudioVariation"/>（±8% 音高 / ±10% 音量）进一步打散。
    ///
    /// 【环境底床】海浪 + 海风 + 垫底三层循环，各层音量与鸟鸣频率可调
    /// （<see cref="AmbientBedRules"/> / <see cref="AmbientBedMix"/>），
    /// 并按镜头离场景中心的水平距离做轻微衰减。
    ///
    /// 【音量总线】Master × (Sfx | Ambient | Music)，见 <see cref="VolumeMixer"/>；
    /// 持久化走 <see cref="AudioSettingsStore"/>（复用 Core/SaveManager 的键值 API）。
    ///
    /// 【播放闸门】同音效 50 ms 去抖 + 每分类并发上限，见 <see cref="PlaybackGate"/>。
    ///
    /// 【剪辑来源优先级】ClipProvider 委托（只覆盖主变奏）→ Resources 约定路径
    /// （主变奏 + 变奏）→ **运行时合成回退**（<c>AudioClip.Create</c>）。
    /// 纯外部素材（如底床垫底）没有合成实现，缺失时静默。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        /// <summary>音效资产在 Resources 下的约定目录（若团队选择资产播放）。</summary>
        public const string ResourcesPrefix = "PirateCrewAudio/";

        /// <summary>一次性播放的 AudioSource 池大小（提案/待定）。</summary>
        public const int VoicePoolSize = 24;

        /// <summary>循环源（海浪/风声/音乐）数量。</summary>
        public const int LoopSourceCount = 4;

        /// <summary>投掷 whoosh 的「满力拖拽」参考值（Flash px）。
        /// 出处：<c>CrewCatalog.cs:127 DragRange = 130</c>（拖拽面板行程）。
        /// 用于把 <c>battle_shot_released</c> 的拖拽距离映射到音高/音量（提案/待定）。</summary>
        public const float WhooshFullDragPixels = 130f;

        /// <summary>环境底床按镜头距离重算衰减的节流间隔（秒）。</summary>
        public const float AmbientUpdateIntervalSeconds = 0.25f;

        /// <summary>
        /// AudioListener 未命中时的重扫间隔（秒，提案/待定）：保留"场景后补监听器"兜底
        /// （最迟一个间隔后恢复发声），同时把最坏情况（场景始终没有监听器）的全场扫描
        /// 限到每秒一次。
        /// </summary>
        const double ListenerProbeRetrySeconds = 1.0d;

        /// <summary>
        /// 场景开始加载事件名。Core 的 SceneLoader 直接发字符串字面量
        /// （<c>Core/SceneLoader.cs:260</c>，无公开常量），已登记在
        /// <c>docs/EventBus事件契约.md</c> §1.3；这里定一个本地常量避免魔法字符串散落。
        /// </summary>
        const string SceneLoadStartedEvent = "scene_load_started";

        /// <summary>
        /// 本服务订阅的**全部** EventBus 事件名（订阅/退订共用同一份，杜绝两处漂移）。
        ///
        /// 【为什么要能对外读】音频是「发了事件没人播」最难发现的静默故障：
        /// 事件名拼错、订阅表漏项在运行时都不报错。把它暴露成可枚举的静态数据后，
        /// <c>Tests/Audio/Game2AudioPortTests</c> 能直接断言「每条搬运映射的目标事件确实有人订阅」。
        /// </summary>
        static readonly string[] EventNames =
        {
            BattleEvents.BattleStarted,
            BattleEvents.TurnStarted,
            BattleEvents.TurnEnded,
            BattleEvents.ShotReleased,
            BattleEvents.ProjectileDetonated,
            BattleEvents.MineBeep,
            BattleEvents.CrewDamaged,
            BattleEvents.CrewDied,
            BattleEvents.AiDecided,
            BattleEvents.MatchFinished,
            SceneLoadStartedEvent,
        };

        /// <summary>订阅表副本（诊断/测试用；避免外部改动内部数组）。</summary>
        public static string[] SubscribedEvents()
        {
            return (string[])EventNames.Clone();
        }

        /// <summary>订阅事件数量。</summary>
        public static int SubscribedEventCount => EventNames.Length;

        static AudioService _instance;

        /// <summary>全局访问入口；未启动时为 null（所有静态 API 都做了 null 容错）。</summary>
        public static AudioService Instance => _instance;

        /// <summary>服务是否可用（已创建且找到 AudioListener）。</summary>
        public static bool IsAvailable => _instance != null;

        /// <summary>
        /// 剪辑提供者（可选外部接线）：返回 null 则继续尝试 Resources 与运行时合成。
        /// 场景组装层可以用它把导出的 wav 资产直接塞进来，无需走 Resources。
        /// </summary>
        public static Func<SfxId, AudioClip> ClipProvider;

        readonly VolumeMixer _mixer = new VolumeMixer();
        readonly PlaybackGate _gate = new PlaybackGate();

        /// <summary>按 id 缓存的剪辑组：搬运素材的多个变奏都放这里，播放时随机取一个。</summary>
        readonly Dictionary<SfxId, AudioClip[]> _clips = new Dictionary<SfxId, AudioClip[]>();

        /// <summary>正在循环播放的源（环境底床 3 层 + 音乐之外的循环音）；值含层权重。</summary>
        readonly Dictionary<SfxId, LoopVoice> _loopSources = new Dictionary<SfxId, LoopVoice>();

        /// <summary>事件名 → 已缓存的委托实例（订阅与退订必须用同一个实例才能退干净）。</summary>
        readonly Dictionary<string, Action<object>> _handlers = new Dictionary<string, Action<object>>();

        readonly AmbientBedMix _bedMix = new AmbientBedMix();

        AudioSource[] _voicePool;
        AudioSource[] _loopPool;
        AudioSource _musicSource;
        int _voiceCursor;

        bool _listenerCheckDone;
        bool _listenerPresent;
        bool _listenerWarned;
        /// <summary>监听器未命中时的下次允许重扫时刻（unscaled 秒）：避免爆炸多目标同帧
        /// 结算时每次播放都 <c>FindObjectOfType</c> 全场扫描。</summary>
        double _nextListenerProbeTime;

        bool _bedActive;
        Coroutine _birdRoutine;
        float _ambientDistanceGain = 1f;
        float _nextAmbientUpdateTime;
        Vector3 _ambientCenter = Vector3.zero;

        readonly System.Random _ambientRng = new System.Random(20260913);

        /// <summary>变奏（音高/音量抖动）用的随机源；与鸟鸣分开，避免互相干扰随机序列。</summary>
        readonly System.Random _variationRng = new System.Random(20260914);

        int[] _lastAiWeaponSlot = { int.MinValue, int.MinValue };

        /// <summary>循环源 + 其层权重（权重只对底床层有意义，其余为 1）。</summary>
        struct LoopVoice
        {
            public AudioSource Source;
            public float Weight;
        }

        /// <summary>进入播放前自动引导（无需改 Bootstrapper / 场景）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBootstrap()
        {
            if (_instance != null)
                return;

            var go = new GameObject("[AudioService]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AudioService>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                global::PirateCrew.Core.Log.Warn("[AudioService] 已存在实例，销毁重复对象: " + name);
                Destroy(gameObject);
                return;
            }

            _instance = this;

            AudioSettingsStore.ApplyDefaults(_mixer);
            AudioSettingsStore.TryLoadInto(_mixer);

            BuildSources();
            SubscribeEvents();

            global::PirateCrew.Core.Log.Info("[AudioService] 已启动：音效 " + SfxCatalog.Count + " 条配方，"
                      + "播放池 " + VoicePoolSize + " 路。");
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            StopBirdRoutine();
            StopAllLoops();
            StopMusicInternal();
            StopVoicePool();

            if (_instance == this)
                _instance = null;
        }

        /// <summary>
        /// 环境底床的镜头距离衰减按 <see cref="AmbientUpdateIntervalSeconds"/> 节流重算，
        /// 不做逐帧计算（音量变化是慢变量，逐帧算纯属浪费）。
        /// </summary>
        void Update()
        {
            if (!_bedActive)
                return;

            if (Time.unscaledTime < _nextAmbientUpdateTime)
                return;

            _nextAmbientUpdateTime = Time.unscaledTime + AmbientUpdateIntervalSeconds;
            RefreshAmbientDistance();
        }

        void BuildSources()
        {
            _voicePool = new AudioSource[VoicePoolSize];
            for (int i = 0; i < _voicePool.Length; i++)
                _voicePool[i] = CreateSource("Voice" + i, loop: false, spatial: true);

            _loopPool = new AudioSource[LoopSourceCount];
            for (int i = 0; i < _loopPool.Length; i++)
                _loopPool[i] = CreateSource("Loop" + i, loop: true, spatial: false);

            _musicSource = CreateSource("Music", loop: false, spatial: false);
        }

        AudioSource CreateSource(string childName, bool loop, bool spatial)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;
            source.volume = 1f;
            return source;
        }

        // ==================================================================
        // EventBus 订阅（只订阅现有事件，不新增、不改他人文件）
        // ==================================================================

        void SubscribeEvents()
        {
            for (int i = 0; i < EventNames.Length; i++)
                EventBus.Subscribe(EventNames[i], HandlerFor(EventNames[i]));
        }

        void UnsubscribeEvents()
        {
            for (int i = 0; i < EventNames.Length; i++)
                EventBus.Unsubscribe(EventNames[i], HandlerFor(EventNames[i]));

            _handlers.Clear();
        }

        /// <summary>
        /// 取某事件名的委托实例；同一事件名永远返回同一个实例，
        /// 这样 <see cref="EventBus.Unsubscribe"/> 能按引用把监听者摘干净
        /// （每次现场 new 一个 lambda 闭包是摘不掉的——闭包实例不相等）。
        /// </summary>
        Action<object> HandlerFor(string eventName)
        {
            if (_handlers.TryGetValue(eventName, out Action<object> cached))
                return cached;

            Action<object> handler;
            switch (eventName)
            {
                case BattleEvents.BattleStarted:
                    handler = OnBattleStarted;
                    break;
                case BattleEvents.TurnStarted:
                    handler = OnTurnStarted;
                    break;
                case BattleEvents.TurnEnded:
                    handler = OnTurnEnded;
                    break;
                case BattleEvents.ShotReleased:
                    handler = OnShotReleased;
                    break;
                case BattleEvents.ProjectileDetonated:
                    handler = OnProjectileDetonated;
                    break;
                case BattleEvents.MineBeep:
                    handler = OnMineBeep;
                    break;
                case BattleEvents.CrewDamaged:
                    handler = OnCrewDamaged;
                    break;
                case BattleEvents.CrewDied:
                    handler = OnCrewDied;
                    break;
                case BattleEvents.AiDecided:
                    handler = OnAiDecided;
                    break;
                case BattleEvents.MatchFinished:
                    handler = OnMatchFinished;
                    break;
                case SceneLoadStartedEvent:
                    handler = OnSceneLoadStarted;
                    break;
                default:
                    Debug.LogError("[AudioService] 订阅表里有未接线的事件名: " + eventName);
                    return null;
            }

            _handlers[eventName] = handler;
            return handler;
        }

        void OnBattleStarted(object payload)
        {
            _gate.Reset();
            StopMusic();
            StartAmbientBedInternal();
        }

        void OnTurnStarted(object payload)
        {
            PlaySfx2D(SfxId.TurnStart);
        }

        void OnTurnEnded(object payload)
        {
            PlaySfx2D(SfxId.TurnEnd);
        }

        void OnShotReleased(object payload)
        {
            float drag = payload is float value ? value : 0f;
            float pitch = SpatialAudioRules.PitchForDrag(drag, WhooshFullDragPixels);
            float volume = SpatialAudioRules.VolumeForDrag(drag, WhooshFullDragPixels);
            PlayInternal(SfxId.ThrowWhoosh, Vector3.zero, volume, pitch);
        }

        void OnProjectileDetonated(object payload)
        {
            if (!(payload is ProjectileDetonatedPayload detonated))
                return;

            SfxId id = AudioEventMapper.SfxForDetonation(detonated.Weapon);
            PlaySfx(id, detonated.Position);
        }

        void OnMineBeep(object payload)
        {
            if (!(payload is MineBeepPayload beep))
                return;

            float gain = SpatialAudioRules.MineBeepGain(beep.ElapsedFrames);
            PlayInternal(SfxId.MineBeep, beep.Position, gain, 1f);
        }

        void OnCrewDamaged(object payload)
        {
            // 载荷只有 PirateId/队伍/伤害，没有世界坐标 → 只能 2D 播放（待裁决项，见报告）。
            PlaySfx2D(SfxId.FleshHit, 0.85f);
        }

        void OnCrewDied(object payload)
        {
            // 同上：无坐标 → 2D。
            PlaySfx2D(SfxId.CrewDown, 0.90f);
        }

        void OnAiDecided(object payload)
        {
            if (!(payload is AiDecidedPayload decided))
                return;

            int team = decided.TeamIndex;
            if (team < 0 || team >= _lastAiWeaponSlot.Length)
                return;

            if (decided.WeaponSlotIndex >= 0 && decided.WeaponSlotIndex != _lastAiWeaponSlot[team])
            {
                _lastAiWeaponSlot[team] = decided.WeaponSlotIndex;
                PlaySfx2D(SfxId.WeaponSwitch, 0.75f);
            }
        }

        void OnMatchFinished(object payload)
        {
            StopBirdRoutine();
            StopAmbientBedInternal();

            if (!(payload is MatchFinishedPayload finished))
                return;

            // 乐句裁决 = mapper 唯一真值（false = 这一局不放任何乐句，2P 热座蓝队胜）。
            if (!AudioEventMapper.MusicForMatchOutcome(
                (MatchOutcome)finished.Outcome, finished.Team1IsAi, out SfxId cue))
                return;

            PlayMusic(cue);
        }

        void OnSceneLoadStarted(object payload)
        {
            // 离开战斗场景时清干净，避免海浪/音乐泄漏到菜单
            StopBirdRoutine();
            StopAmbientBedInternal();
            StopMusic();
            _gate.Reset();
        }

        // ==================================================================
        // 公开静态 API
        // ==================================================================

        /// <summary>3D 空间音播放（爆炸/命中/弹跳等）。服务不可用时返回 false。</summary>
        public static bool PlaySfx(SfxId id, Vector3 position, float volumeScale = 1f)
        {
            AudioService service = _instance;
            return service != null && service.PlayInternal(id, position, volumeScale, 1f);
        }

        /// <summary>2D 非空间音播放（反馈/UI/结果）。</summary>
        public static bool PlaySfx2D(SfxId id, float volumeScale = 1f, float pitch = 1f)
        {
            AudioService service = _instance;
            return service != null && service.PlayInternal(id, Vector3.zero, volumeScale, pitch);
        }

        /// <summary>UI 音便捷入口（UI 层接线用；语义上等价于 2D 播放）。</summary>
        public static bool PlayUi(SfxId id, float volumeScale = 1f)
        {
            return PlaySfx2D(id, volumeScale);
        }

        /// <summary>播放/更新循环环境音（海浪/风声）。重复调用同一 id 不会叠加。</summary>
        public static bool PlayAmbient(SfxId id)
        {
            AudioService service = _instance;
            return service != null && service.StartLoop(id, 1f);
        }

        /// <summary>
        /// 起播环境底床（海浪 + 海风 + 垫底三层循环 + 鸟鸣点缀）。
        /// <c>battle_started</c> 会自动调用；菜单等无战斗场景可手动调。
        /// </summary>
        public static bool StartAmbientBed()
        {
            AudioService service = _instance;
            return service != null && service.StartAmbientBedInternal();
        }

        /// <summary>停止环境底床（含鸟鸣点缀）。</summary>
        public static void StopAmbientBed()
        {
            AudioService service = _instance;
            if (service == null)
                return;
            service.StopBirdRoutine();
            service.StopAmbientBedInternal();
        }

        /// <summary>
        /// 底床混音参数（每层音量 + 鸟鸣间隔）；服务未启动时为 null。
        /// 直接改它即可调参，下一次 <c>Update</c> 节流点会生效。
        /// </summary>
        public static AmbientBedMix AmbientMix => _instance?._bedMix;

        /// <summary>设置底床某一层的音量权重（不是底床层则忽略）。</summary>
        public static void SetAmbientLayerVolume(SfxId id, float weight)
        {
            AudioService service = _instance;
            if (service == null)
                return;
            service._bedMix.SetWeight(id, weight);
            service.ApplyLoopVolumes();
        }

        /// <summary>设置鸟鸣点缀的触发间隔（秒）。</summary>
        public static void SetBirdInterval(double minSeconds, double maxSeconds)
        {
            _instance?._bedMix.SetBirdInterval(minSeconds, maxSeconds);
        }

        /// <summary>设置场景中心（世界坐标，用于镜头距离衰减；默认原点）。</summary>
        public static void SetAmbientCenter(Vector3 center)
        {
            AudioService service = _instance;
            if (service == null)
                return;
            service._ambientCenter = center;
            service.RefreshAmbientDistance();
        }

        /// <summary>停止某个循环环境音。</summary>
        public static void StopAmbient(SfxId id)
        {
            _instance?.StopLoop(id);
        }

        /// <summary>停止全部循环环境音。</summary>
        public static void StopAllAmbient()
        {
            _instance?.StopAllLoops();
        }

        /// <summary>播放结果/背景音乐（单路，新音乐替换旧音乐）。</summary>
        public static bool PlayMusic(SfxId id)
        {
            AudioService service = _instance;
            return service != null && service.StartMusic(id);
        }

        /// <summary>停止音乐。</summary>
        public static void StopMusic()
        {
            _instance?.StopMusicInternal();
        }

        /// <summary>设置某分类音量（0–1）。已在循环播放的环境底床音量会立刻跟随。</summary>
        public static void SetVolume(AudioCategory category, float volume)
        {
            if (_instance == null)
                return;
            _instance._mixer.SetVolume(category, volume);
            _instance.ApplyLoopVolumes();
        }

        /// <summary>读取某分类音量（服务不可用时返回默认值）。</summary>
        public static float GetVolume(AudioCategory category)
        {
            return _instance != null ? _instance._mixer.GetVolume(category) : AudioSettingsStore.DefaultVolume;
        }

        /// <summary>读取某分类的最终增益（含 Master 缩放与静音）。</summary>
        public static float GetEffectiveGain(AudioCategory category)
        {
            return _instance != null ? _instance._mixer.EffectiveGain(category) : 0f;
        }

        /// <summary>设置/取消某分类静音。</summary>
        public static void SetMuted(AudioCategory category, bool muted)
        {
            if (_instance == null)
                return;
            _instance._mixer.SetMuted(category, muted);
            _instance.ApplyLoopVolumes();
        }

        /// <summary>把当前音量落盘（设置面板「应用」用）。</summary>
        public static bool SaveVolumes()
        {
            return _instance != null && AudioSettingsStore.TrySaveFrom(_instance._mixer);
        }

        /// <summary>从存档重新读取音量。</summary>
        public static bool LoadVolumes()
        {
            return _instance != null && AudioSettingsStore.TryLoadInto(_instance._mixer);
        }

        /// <summary>停止全部声音（场景切换/暂停用）。</summary>
        public static void StopAll()
        {
            if (_instance == null)
                return;
            _instance.StopAllLoops();
            _instance.StopMusicInternal();
            _instance.StopVoicePool();
        }

        /// <summary>停止一次性播放池（Awake 未完成时为 no-op）。</summary>
        void StopVoicePool()
        {
            if (_voicePool == null)
                return;
            for (int i = 0; i < _voicePool.Length; i++)
            {
                if (_voicePool[i] != null)
                    _voicePool[i].Stop();
            }
        }

        // ==================================================================
        // 内部播放实现
        // ==================================================================

        /// <summary>去抖/并发占位用的单调时钟：不受 timeScale 影响，暂停时仍正确。</summary>
        double Now => Time.unscaledTimeAsDouble;

        bool PlayInternal(SfxId id, Vector3 position, float volumeScale, float pitch)
        {
            if (!EnsureListener())
                return false;

            SfxRecipe recipe = SfxCatalog.Get(id);

            float gain = _mixer.EffectiveGain(recipe.Category) * recipe.DefaultVolume
                         * (volumeScale < 0f ? 0f : volumeScale);
            if (gain <= 0f)
                return false;

            // 整型 key 直存闸门字典，替代 id.ToString()（受击/地雷蜂鸣高频路径避免装箱+字符串分配）。
            if (!_gate.TryAcquire((int)id, recipe.Category, Now, recipe.DurationSeconds))
                return false;

            AudioClip clip = ResolveClip(id);
            if (clip == null)
                return false;

            AudioSource source = NextVoiceSource();
            ApplySpatial(source, recipe, position);

            // 变奏（第一优先级目标：消解重复感）：一次性音效每次播放抖 ±8% 音高 / ±10% 音量。
            // 循环音与音乐不抖（会在循环点跳变 / 让乐句走音），由 AudioVariation.AppliesTo 判定。
            bool vary = AudioVariation.Enabled && AudioVariation.AppliesTo(recipe.Category, recipe.Loop);
            AudioVariation.Apply(
                vary,
                (float)_variationRng.NextDouble(),
                (float)_variationRng.NextDouble(),
                pitch,
                gain,
                out float finalPitch,
                out float finalVolume);

            source.pitch = finalPitch;
            source.clip = clip;
            source.volume = finalVolume;
            source.Play();
            return true;
        }

        void ApplySpatial(AudioSource source, SfxRecipe recipe, Vector3 position)
        {
            source.spatialBlend = SpatialAudioRules.SpatialBlend(recipe.Spatial);
            if (recipe.Spatial == SpatialMode.ThreeD)
            {
                source.transform.position = position;
                source.minDistance = recipe.MinDistance;
                source.maxDistance = recipe.MaxDistance;
            }
        }

        bool StartLoop(SfxId id, float weight)
        {
            if (!EnsureListener())
                return false;

            SfxRecipe recipe = SfxCatalog.Get(id);
            if (!recipe.Loop)
                return false;

            if (_loopSources.TryGetValue(id, out LoopVoice existing)
                && existing.Source != null && existing.Source.isPlaying)
            {
                // 已在播：只更新层权重（调参时能立刻生效），不重启循环
                _loopSources[id] = new LoopVoice { Source = existing.Source, Weight = weight < 0f ? 0f : weight };
                existing.Source.volume = LoopGain(recipe, weight);
                return true;
            }

            float gain = LoopGain(recipe, weight);
            if (gain <= 0f)
                return false;

            AudioClip clip = ResolveClip(id);
            if (clip == null)
                return false;

            AudioSource source = NextLoopSource();
            source.spatialBlend = 0f;
            source.pitch = 1f;
            source.clip = clip;
            source.loop = true;
            source.volume = gain;
            source.Play();

            _loopSources[id] = new LoopVoice { Source = source, Weight = weight < 0f ? 0f : weight };
            return true;
        }

        /// <summary>
        /// 循环源音量 = 配方默认音量 × 总线增益 × 层权重 × 镜头距离衰减。
        /// 距离衰减只作用于环境底床层（<see cref="AmbientBedRules.IsBedLayer"/>），
        /// 手动播的其它循环音不受影响。
        /// </summary>
        float LoopGain(SfxRecipe recipe, float weight)
        {
            float distanceGain = AmbientBedRules.IsBedLayer(recipe.Id) ? _ambientDistanceGain : 1f;
            return AmbientBedRules.LayerVolume(
                recipe.DefaultVolume, _mixer.EffectiveGain(recipe.Category), weight, distanceGain);
        }

        /// <summary>按当前层权重/总线音量/镜头距离重算全部循环源音量（调参或镜头移动后调用）。</summary>
        void ApplyLoopVolumes()
        {
            if (_loopSources.Count == 0)
                return;

            foreach (var pair in _loopSources)
            {
                LoopVoice voice = pair.Value;
                if (voice.Source == null)
                    continue;
                voice.Source.volume = LoopGain(SfxCatalog.Get(pair.Key), voice.Weight);
            }
        }

        void StopLoop(SfxId id)
        {
            if (_loopSources.TryGetValue(id, out LoopVoice voice))
            {
                if (voice.Source != null)
                {
                    voice.Source.Stop();
                    voice.Source.clip = null;
                }
                _loopSources.Remove(id);
            }
        }

        void StopAllLoops()
        {
            if (_loopSources.Count == 0)
                return;

            var keys = new List<SfxId>(_loopSources.Keys);
            for (int i = 0; i < keys.Count; i++)
                StopLoop(keys[i]);
        }

        bool StartMusic(SfxId id)
        {
            if (!EnsureListener())
                return false;

            SfxRecipe recipe = SfxCatalog.Get(id);
            float gain = _mixer.EffectiveGain(recipe.Category) * recipe.DefaultVolume;
            if (gain <= 0f)
                return false;

            AudioClip clip = ResolveClip(id);
            if (clip == null)
                return false;

            _musicSource.Stop();
            _musicSource.spatialBlend = 0f;
            _musicSource.loop = recipe.Loop;
            _musicSource.clip = clip;
            _musicSource.volume = gain;
            _musicSource.pitch = 1f;
            _musicSource.Play();
            return true;
        }

        void StopMusicInternal()
        {
            if (_musicSource == null)
                return;
            _musicSource.Stop();
            _musicSource.clip = null;
        }

        // ==================================================================
        // 环境底床（海浪 + 海风 + 垫底三层循环 + 鸟鸣点缀）
        // ==================================================================

        /// <summary>
        /// 起播底床三层循环，并按镜头距离刷新衰减；随后开始鸟鸣点缀例程。
        /// 任一层因总线静音/资产缺失起不来时不影响其它层（返回是否有层成功起播）。
        /// </summary>
        bool StartAmbientBedInternal()
        {
            bool started = false;
            for (int i = 0; i < _bedMix.LayerCount; i++)
                started |= StartLoop(_bedMix.LayerId(i), _bedMix.GetWeightAt(i));

            _bedActive = true;
            RefreshAmbientDistance();
            StartBirdRoutine();
            return started;
        }

        void StopAmbientBedInternal()
        {
            _bedActive = false;
            for (int i = 0; i < _bedMix.LayerCount; i++)
                StopLoop(_bedMix.LayerId(i));

            _ambientDistanceGain = 1f;
        }

        /// <summary>
        /// 按镜头离场景中心的**水平**距离重算底床衰减（Y 不参与：镜头俯仰/抬高不应造成误衰减）。
        /// 拿不到主相机（无渲染路径的测试场景）时按满增益处理。
        /// </summary>
        void RefreshAmbientDistance()
        {
            float gain = 1f;
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 p = camera.transform.position;
                float distance = AmbientBedRules.CameraDistance(
                    p.x, p.z, _ambientCenter.x, _ambientCenter.z);
                gain = AmbientBedRules.DistanceGain(distance);
            }

            _ambientDistanceGain = gain;
            ApplyLoopVolumes();
        }

        void StartBirdRoutine()
        {
            if (_birdRoutine == null)
                _birdRoutine = StartCoroutine(BirdRoutine());
        }

        void StopBirdRoutine()
        {
            if (_birdRoutine != null)
            {
                StopCoroutine(_birdRoutine);
                _birdRoutine = null;
            }
        }

        /// <summary>
        /// 鸟鸣点缀：按 <see cref="AmbientBedMix"/> 配置的随机间隔触发一个变奏，
        /// 音量随镜头距离一并轻微衰减（与底床同进退）。
        /// </summary>
        IEnumerator BirdRoutine()
        {
            while (true)
            {
                double wait = _bedMix.NextBirdInterval(_ambientRng.NextDouble());
                yield return new WaitForSecondsRealtime((float)wait);

                if (!_bedActive)
                    yield break;

                SfxId variant = AudioEventMapper.SeagullVariantForRoll((float)_ambientRng.NextDouble());
                if (_mixer.EffectiveGain(AudioCategory.Ambient) > 0f)
                {
                    PlayInternal(variant, Vector3.zero,
                        _bedMix.BirdVolumeScale * _ambientDistanceGain, 1f);
                }
            }
        }

        AudioSource NextVoiceSource()
        {
            AudioSource source = _voicePool[_voiceCursor];
            _voiceCursor++;
            if (_voiceCursor >= _voicePool.Length)
                _voiceCursor = 0;
            return source;
        }

        AudioSource NextLoopSource()
        {
            for (int i = 0; i < _loopPool.Length; i++)
            {
                if (!_loopPool[i].isPlaying)
                    return _loopPool[i];
            }

            // 全忙时复用第 0 路（环境音最多 2–3 路，正常不会走到这里）
            return _loopPool[0];
        }

        /// <summary>
        /// 剪辑解析（变奏感知）：解析成**剪辑组**后按 id 缓存；
        /// 有多个变奏时每次播放随机取一个（与 <see cref="AudioVariation"/> 一起消解重复感）。
        /// </summary>
        AudioClip ResolveClip(SfxId id)
        {
            AudioClip[] clips = ResolveClips(id);
            if (clips == null || clips.Length == 0)
                return null;
            if (clips.Length == 1)
                return clips[0];

            return clips[_variationRng.Next(clips.Length)];
        }

        /// <summary>
        /// 剪辑组解析优先级：ClipProvider（只覆盖主变奏）→ Resources 约定路径
        /// （主变奏 + <c>_2</c>/<c>_3</c>… 变奏，见 <see cref="Game2AudioAssets.TargetFileNames"/>）
        /// → 运行时合成回退。结果（含空数组）会缓存，避免每次播放都查找。
        /// </summary>
        AudioClip[] ResolveClips(SfxId id)
        {
            if (_clips.TryGetValue(id, out AudioClip[] cached))
                return cached;

            string[] names = Game2AudioAssets.TargetFileNames(id);
            var resolved = new List<AudioClip>(names.Length);

            for (int i = 0; i < names.Length; i++)
            {
                AudioClip clip = null;

                // ClipProvider 是「换素材源」的覆盖钩子，只作用于主变奏，不承担变奏编排
                if (i == 0 && ClipProvider != null)
                {
                    try
                    {
                        clip = ClipProvider(id);
                    }
                    catch (Exception e)
                    {
                        global::PirateCrew.Core.Log.Warn("[AudioService] ClipProvider 异常（改用资产/回退合成）: " + e.Message);
                    }
                }

                if (clip == null)
                    clip = Resources.Load<AudioClip>(ResourcesPrefix + names[i]);

                if (clip != null)
                    resolved.Add(clip);
            }

            // 搬运登记的 id 有变奏没解析到 → 多半是资产没拷全，早点告警（否则只是少几个变奏，静默无声）
            if (Game2AudioAssets.IsPorted(id) && resolved.Count < names.Length)
            {
                global::PirateCrew.Core.Log.Warn("[AudioService] " + id + " 的变奏资产缺 " + (names.Length - resolved.Count)
                                 + "/" + names.Length + " 个（已解析到的变奏仍会播放）；"
                                 + "请跑 PiratesCrew/音频/生成程序化音频资产（幂等）或检查 Resources/PirateCrewAudio。");
            }

            if (resolved.Count == 0)
            {
                AudioClip synth = BuildRuntimeClip(id);
                if (synth != null)
                    resolved.Add(synth);
            }

            AudioClip[] result = resolved.ToArray();
            _clips[id] = result;
            return result;
        }

        /// <summary>
        /// 运行时合成回退：用 <c>AudioClip.Create</c> 现场生成 PCM。
        /// 与离线 wav 资产同源（都走 <see cref="SynthRenderer"/>），因此两条路出声一致。
        /// </summary>
        AudioClip BuildRuntimeClip(SfxId id)
        {
            // 纯外部素材（如底床垫底）没有合成实现：资产缺失就静音，而不是回落成别的音色
            if (!SynthRenderer.CanRender(id))
            {
                global::PirateCrew.Core.Log.Warn("[AudioService] " + id + " 是外部搬运素材且资产缺失，本次静音（无合成回退）。");
                return null;
            }

            try
            {
                float[] pcm = SynthRenderer.CreateRuntimeClipBody(id, out int sampleRate, out int channels);
                if (pcm == null || pcm.Length == 0 || channels <= 0)
                    return null;

                AudioClip clip = AudioClip.Create(
                    SfxCatalog.AssetFileName(id), pcm.Length / channels, channels, sampleRate, false);
                clip.SetData(pcm, 0);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioService] 运行时合成失败 " + id + ": " + e.Message);
                return null;
            }
        }

        // ==================================================================
        // AudioListener 容错
        // ==================================================================

        /// <summary>
        /// 没有 AudioListener 时：只告警一次、跳过本次播放、不抛异常。
        /// 找到后缓存判定直接放行；未命中则按 <see cref="ListenerProbeRetrySeconds"/> 限频重扫——
        /// 爆炸多目标同帧结算会连续走播放路径，逐次 <c>FindObjectOfType</c> 全场扫描是纯浪费，
        /// 而"后补监听器立即恢复发声"的兜底语义保留（延迟至多一个间隔）。
        /// </summary>
        bool EnsureListener()
        {
            if (_listenerPresent)
                return true;

            double now = Now;
            if (now < _nextListenerProbeTime)
                return false;

            _nextListenerProbeTime = now + ListenerProbeRetrySeconds;
            _listenerPresent = FindObjectOfType<AudioListener>() != null;
            _listenerCheckDone = true;

            if (!_listenerPresent && !_listenerWarned)
            {
                _listenerWarned = true;
                global::PirateCrew.Core.Log.Warn("[AudioService] 当前场景没有 AudioListener，音频将不发声（仅告警一次）。");
            }

            return _listenerPresent;
        }

        /// <summary>供测试/诊断：是否已确认存在监听器。</summary>
        public bool ListenerPresent => _listenerPresent;

        /// <summary>供诊断：监听器检查是否执行过。</summary>
        public bool ListenerCheckDone => _listenerCheckDone;
    }
}
