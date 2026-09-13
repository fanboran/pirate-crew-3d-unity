using System;
using System.Collections;
using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Audio.Synth;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Combat;
using UnityEngine;

namespace PirateCrew.PirateCrew.Audio
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
    ///   ① **EventBus 订阅**（只订阅现有事件，不新增、不改他人文件）：
    ///      battle_started / turn_started / turn_ended / battle_shot_released /
    ///      battle_projectile_detonated / battle_mine_beep / crew_damaged / crew_died /
    ///      ai_decided / match_finished / scene_load_started。
    ///   ② **公开静态 API**（给没有事件的场合手动接线）：
    ///      <see cref="PlaySfx"/>（3D）、<see cref="PlaySfx2D"/>、<see cref="PlayUi"/>、
    ///      <see cref="PlayAmbient"/>、<see cref="PlayMusic"/> 等。
    ///
    /// 【音量总线】Master × (Sfx | Ambient | Music)，见 <see cref="VolumeMixer"/>；
    /// 持久化走 <see cref="AudioSettingsStore"/>（复用 Core/SaveManager 的键值 API）。
    ///
    /// 【播放闸门】同音效 50 ms 去抖 + 每分类并发上限，见 <see cref="PlaybackGate"/>。
    ///
    /// 【剪辑来源优先级】ClipProvider 委托（手工接线）→ Resources 约定路径 →
    /// **运行时合成回退**（<c>AudioClip.Create</c>）。取舍见本文件末注释与交付报告。
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

        /// <summary>海鸥点缀的最短/最长间隔（秒，提案/待定）。</summary>
        const double SeagullMinInterval = 6d;
        const double SeagullMaxInterval = 14d;

        /// <summary>
        /// 场景开始加载事件名。Core 的 SceneLoader 直接发字符串字面量
        /// （<c>Core/SceneLoader.cs:260</c>，无公开常量），已登记在
        /// <c>docs/EventBus事件契约.md</c> §1.3；这里定一个本地常量避免魔法字符串散落。
        /// </summary>
        const string SceneLoadStartedEvent = "scene_load_started";

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
        readonly Dictionary<SfxId, AudioClip> _clips = new Dictionary<SfxId, AudioClip>();
        readonly Dictionary<SfxId, AudioSource> _loopSources = new Dictionary<SfxId, AudioSource>();

        AudioSource[] _voicePool;
        AudioSource[] _loopPool;
        AudioSource _musicSource;
        int _voiceCursor;

        bool _listenerCheckDone;
        bool _listenerPresent;
        bool _listenerWarned;

        bool _ambientActive;
        Coroutine _seagullRoutine;
        readonly System.Random _ambientRng = new System.Random(20260913);

        int[] _lastAiWeaponSlot = { int.MinValue, int.MinValue };

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
                Debug.LogWarning("[AudioService] 已存在实例，销毁重复对象: " + name);
                Destroy(gameObject);
                return;
            }

            _instance = this;

            AudioSettingsStore.ApplyDefaults(_mixer);
            AudioSettingsStore.TryLoadInto(_mixer);

            BuildSources();
            SubscribeEvents();

            Debug.Log("[AudioService] 已启动：音效 " + SfxCatalog.Count + " 条配方，"
                      + "播放池 " + VoicePoolSize + " 路。");
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            StopSeagulls();
            StopAllLoops();
            StopMusicInternal();
            StopVoicePool();

            if (_instance == this)
                _instance = null;
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
        // EventBus 订阅（只订阅现有事件）
        // ==================================================================

        void SubscribeEvents()
        {
            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe(BattleEvents.ShotReleased, OnShotReleased);
            EventBus.Subscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Subscribe(BattleEvents.MineBeep, OnMineBeep);
            EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe(BattleEvents.AiDecided, OnAiDecided);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Subscribe(SceneLoadStartedEvent, OnSceneLoadStarted);
        }

        void UnsubscribeEvents()
        {
            EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Unsubscribe(BattleEvents.ShotReleased, OnShotReleased);
            EventBus.Unsubscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Unsubscribe(BattleEvents.MineBeep, OnMineBeep);
            EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe(BattleEvents.AiDecided, OnAiDecided);
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Unsubscribe(SceneLoadStartedEvent, OnSceneLoadStarted);
        }

        void OnBattleStarted(object payload)
        {
            _gate.Reset();
            StopMusic();
            PlayAmbient(SfxId.WavesLoop);
            PlayAmbient(SfxId.WindLoop);
            StartSeagulls();
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
            StopSeagulls();
            StopAllAmbient();

            if (!(payload is MatchFinishedPayload finished))
                return;

            SfxId cue = AudioEventMapper.MusicForMatchOutcome(
                (MatchOutcome)finished.Outcome, finished.Team1IsAi, out bool hasMusic);

            if (hasMusic)
                PlayMusic(cue);
        }

        void OnSceneLoadStarted(object payload)
        {
            // 离开战斗场景时清干净，避免海浪/音乐泄漏到菜单
            StopSeagulls();
            StopAllAmbient();
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
            return service != null && service.StartLoop(id);
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

        /// <summary>设置某分类音量（0–1）。</summary>
        public static void SetVolume(AudioCategory category, float volume)
        {
            if (_instance == null)
                return;
            _instance._mixer.SetVolume(category, volume);
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
            _instance?._mixer.SetMuted(category, muted);
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

            if (!_gate.TryAcquire(id.ToString(), recipe.Category, Now, recipe.DurationSeconds))
                return false;

            AudioClip clip = ResolveClip(id);
            if (clip == null)
                return false;

            AudioSource source = NextVoiceSource();
            ApplySpatial(source, recipe, position);
            source.pitch = pitch <= 0f ? 1f : pitch;
            source.clip = clip;
            source.volume = gain;
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

        bool StartLoop(SfxId id)
        {
            if (!EnsureListener())
                return false;

            SfxRecipe recipe = SfxCatalog.Get(id);
            if (!recipe.Loop)
                return false;

            if (_loopSources.TryGetValue(id, out AudioSource existing) && existing != null && existing.isPlaying)
                return true;

            float gain = _mixer.EffectiveGain(recipe.Category) * recipe.DefaultVolume;
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

            _loopSources[id] = source;
            return true;
        }

        void StopLoop(SfxId id)
        {
            if (_loopSources.TryGetValue(id, out AudioSource source))
            {
                if (source != null)
                {
                    source.Stop();
                    source.clip = null;
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

        void StartSeagulls()
        {
            _ambientActive = true;
            if (_seagullRoutine == null)
                _seagullRoutine = StartCoroutine(SeagullRoutine());
        }

        void StopSeagulls()
        {
            _ambientActive = false;
            if (_seagullRoutine != null)
            {
                StopCoroutine(_seagullRoutine);
                _seagullRoutine = null;
            }
        }

        IEnumerator SeagullRoutine()
        {
            while (_ambientActive)
            {
                double wait = SeagullMinInterval + _ambientRng.NextDouble() * (SeagullMaxInterval - SeagullMinInterval);
                yield return new WaitForSecondsRealtime((float)wait);

                if (!_ambientActive)
                    yield break;

                SfxId variant = AudioEventMapper.SeagullVariantForRoll((float)_ambientRng.NextDouble());
                if (_mixer.EffectiveGain(AudioCategory.Ambient) > 0f)
                    PlayInternal(variant, Vector3.zero, 1f, 1f);
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
        /// 剪辑解析：ClipProvider → Resources 约定路径 → 运行时合成回退。
        /// 结果（含 null）会缓存，避免每次播放都查找。
        /// </summary>
        AudioClip ResolveClip(SfxId id)
        {
            if (_clips.TryGetValue(id, out AudioClip cached))
                return cached;

            AudioClip clip = null;

            if (ClipProvider != null)
            {
                try
                {
                    clip = ClipProvider(id);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[AudioService] ClipProvider 异常（改用回退合成）: " + e.Message);
                }
            }

            if (clip == null)
            {
                string name = SfxCatalog.AssetFileName(id);
                clip = Resources.Load<AudioClip>(ResourcesPrefix + name);
            }

            if (clip == null)
                clip = BuildRuntimeClip(id);

            _clips[id] = clip;
            return clip;
        }

        /// <summary>
        /// 运行时合成回退：用 <c>AudioClip.Create</c> 现场生成 PCM。
        /// 与离线 wav 资产同源（都走 <see cref="SynthRenderer"/>），因此两条路出声一致。
        /// </summary>
        AudioClip BuildRuntimeClip(SfxId id)
        {
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
        /// 不做一次性缓存（未命中时每次重新查找），这样后面场景补上监听器后立即恢复发声。
        /// </summary>
        bool EnsureListener()
        {
            if (_listenerPresent)
                return true;

            _listenerPresent = FindObjectOfType<AudioListener>() != null;
            _listenerCheckDone = true;

            if (!_listenerPresent && !_listenerWarned)
            {
                _listenerWarned = true;
                Debug.LogWarning("[AudioService] 当前场景没有 AudioListener，音频将不发声（仅告警一次）。");
            }

            return _listenerPresent;
        }

        /// <summary>供测试/诊断：是否已确认存在监听器。</summary>
        public bool ListenerPresent => _listenerPresent;

        /// <summary>供诊断：监听器检查是否执行过。</summary>
        public bool ListenerCheckDone => _listenerCheckDone;
    }
}
