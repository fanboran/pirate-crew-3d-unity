using System;
using System.Collections.Generic;
using PirateCrew.Battle;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 一条「隔壁 Game-2 素材 → 本项目 SfxId」的搬运记录（纯值类型，可无头断言）。
    ///
    /// 【为什么做成数据而不是散落的复制脚本】搬运关系要能被测试遍历：
    /// ① 每个映射的**源文件确实存在**；② 每个映射的**目标资产确实落盘**；
    /// ③ 每个映射的**目标事件确实被 AudioService 订阅**。任何一项缺失都是静默故障
    /// （「文件忘了拷」= 运行时回退合成、听感不一致；「事件没订阅」= 声音永远不响）。
    /// </summary>
    public readonly struct Game2Port
    {
        /// <summary>本项目落地的音效 id（沿用 <see cref="SfxCatalog"/> 的命名与总线分类）。</summary>
        public readonly SfxId Id;

        /// <summary>源文件相对 <see cref="Game2AudioAssets.SourceRoot"/> 的路径（如 <c>sfx/bodyfall_a.wav</c>）。</summary>
        public readonly string[] Sources;

        /// <summary>
        /// 该音效被哪个 EventBus 事件驱动（<c>BattleEvents</c> 常量）；
        /// 空串表示没有对应事件、只能走公开 API 手动接线（见 <see cref="Note"/>）。
        /// </summary>
        public readonly string EventName;

        /// <summary>人可读的用途/接线说明。</summary>
        public readonly string Note;

        public Game2Port(SfxId id, string[] sources, string eventName, string note)
        {
            Id = id;
            Sources = sources;
            EventName = eventName;
            Note = note;
        }

        /// <summary>变奏数量（同一事件可随机取一个，用来消解「每次都一样」的重复感）。</summary>
        public int VariantCount => Sources == null ? 0 : Sources.Length;

        /// <summary>是否由事件总线自动接线（false = 需要手动调 AudioService 的公开 API）。</summary>
        public bool WiredToEventBus => !string.IsNullOrEmpty(EventName);
    }

    /// <summary>
    /// 声音素材搬运表：把隔壁 Game-2（stick-world）自产 WAV 映射到本项目的
    /// <see cref="SfxId"/> / 事件，替代原来的程序化合成素材。
    ///
    /// 【为什么可以跨仓库复制】两边是同一作者的原创资产（用户在 2026-09-14 裁决
    /// 「声音直接复用隔壁 Game-2 的，全是自己做的音效」），不引入第三方版权风险；
    /// 项目原有的「零第三方素材、波形可审计」纪律由本表 + 搬运来源登记继续保证
    /// （每条都记了源文件相对路径，可逐条核对）。
    ///
    /// 【与合成回退的关系】**资产优先、合成兜底**的既有优先级不变
    /// （见 <c>AudioService.ResolveClip</c>）：本表列出的 id 会由
    /// <c>AudioAssetBuilder.SyncPortedAssets</c> 把真实 WAV 拷进
    /// <c>Assets/Resources/PirateCrewAudio/</c>，运行时按
    /// <see cref="SfxCatalog.AssetFileName"/> 直接加载；只有资产缺失时才回落到
    /// <c>SynthRenderer</c> 的程序化版本（听感会与真实素材不同，属最后兜底）。
    ///
    /// 【命名与 GUID】目标文件名 = <see cref="SfxCatalog.AssetFileName"/>（分类前缀 + 枚举名），
    /// 变奏依次加 <c>_2</c>/<c>_3</c>… 后缀。主变奏文件名与搬运前**完全一致**，
    /// 因此覆盖写不会改变 Unity 资产 GUID；新变奏由构建器按「字节不变则不写」的幂等策略
    /// 落盘（见 AudioAssetBuilder），GUID 同样稳定。
    ///
    /// 【未搬运的 id】Game-2 侧没有对应素材的（弹跳/滚石/地雷蜂鸣/武器切换/选中/危险提示/
    /// 海浪/风声/回合结束/UI 报错/落水）继续用程序化合成素材，不在本表内。
    /// </summary>
    public static class Game2AudioAssets
    {
        /// <summary>Game-2 音频素材根目录（`.temp` 下的构建中间产物；本表只读它，不写）。</summary>
        public const string SourceRoot =
            "F:/VSCode/game-2/.temp/building-pipeline-v2/stick-world/assets/audio";

        /// <summary>目标资产平铺目录（与 <c>AudioAssetBuilder.AudioRoot</c> 一致）。</summary>
        public const string ResourcesFolder = "Assets/Resources/PirateCrewAudio";

        /// <summary>运行时 Resources 路径前缀（与 <c>AudioService.ResourcesPrefix</c> 一致）。</summary>
        public const string ResourcesPrefix = "PirateCrewAudio/";

        /// <summary>变奏文件名的数字后缀格式（第 0 个变奏无后缀）。</summary>
        public const string VariantSuffixFormat = "_{0}";

        // 事件名常量：全部取自 BattleEvents，禁止在本文件写裸字符串。
        const string EvBattleStarted = BattleEvents.BattleStarted;
        const string EvTurnStarted = BattleEvents.TurnStarted;
        const string EvShotReleased = BattleEvents.ShotReleased;
        const string EvDetonated = BattleEvents.ProjectileDetonated;
        const string EvCrewDamaged = BattleEvents.CrewDamaged;
        const string EvCrewDied = BattleEvents.CrewDied;
        const string EvMatchFinished = BattleEvents.MatchFinished;

        static readonly Game2Port[] Ports =
        {
            // ================= 环境底床 =================
            new Game2Port(SfxId.BedPad,
                new[] { "bgm/ambient_pad.wav" },
                EvBattleStarted,
                "环境底床主循环（22 s 无缝垫底）；battle_started 由 AudioService.StartAmbientBed 起播，受镜头离场中心的距离衰减"),

            new Game2Port(SfxId.SeagullCry1,
                new[] { "sfx/bird_chirp_a.wav" },
                EvBattleStarted,
                "鸟鸣变体 1；battle_started 后按随机间隔点缀（AudioService 的海鸥/鸟鸣例程）"),

            new Game2Port(SfxId.SeagullCry2,
                new[] { "sfx/bird_chirp_b.wav" },
                EvBattleStarted,
                "鸟鸣变体 2；同 SeagullCry1"),

            new Game2Port(SfxId.SeagullCry3,
                new[] { "sfx/bird_chirp_c.wav" },
                EvBattleStarted,
                "鸟鸣变体 3；同 SeagullCry1"),

            // ================= 战斗 =================
            new Game2Port(SfxId.Explosion,
                new[] { "sfx/magikill_blast_a.wav", "sfx/magikill_blast_b.wav", "sfx/clang_b.wav" },
                EvDetonated,
                "爆炸（炮弹/炸药/巨石等）；battle_projectile_detonated 按爆心世界坐标 3D 播放，3 变奏随机"),

            new Game2Port(SfxId.WoodCrack,
                new[] { "sfx/harvest_wood.wav", "sfx/harvest_hit_c.wav" },
                EvDetonated,
                "木箱/火药桶碎裂；battle_projectile_detonated（木质武器）3D 播放"),

            new Game2Port(SfxId.FleshHit,
                new[] { "sfx/thump_a.wav", "sfx/thump_b.wav", "sfx/headbutt.wav" },
                EvCrewDamaged,
                "受击闷响；crew_damaged 载荷无坐标 → 2D 播放（待裁决项）"),

            new Game2Port(SfxId.CrewDown,
                new[] { "sfx/bodyfall_a.wav", "sfx/bodyfall_b.wav", "sfx/bodyfall_c.wav" },
                EvCrewDied,
                "倒地/阵亡；crew_died 载荷无坐标 → 2D 播放（待裁决项）"),

            new Game2Port(SfxId.ThrowWhoosh,
                new[] { "sfx/swoosh_a.wav", "sfx/swoosh_b.wav", "sfx/swoosh_c.wav", "sfx/swoosh_d.wav" },
                EvShotReleased,
                "投掷出手 whoosh；battle_shot_released 按拖拽距离映射音高/音量，4 变奏随机"),

            // ================= 回合 =================
            new Game2Port(SfxId.TurnStart,
                new[] { "sfx/battle_started.wav" },
                EvTurnStarted,
                "回合开始提示音（用户裁决：用 Game-2 的 battle_started 素材）；turn_started 2D 播放"),

            // ================= 结果 =================
            new Game2Port(SfxId.VictoryJingle,
                new[] { "sfx/battle_ended_win.wav" },
                EvMatchFinished,
                "胜利乐句；match_finished（Team0Win，或 1P 下玩家胜）"),

            new Game2Port(SfxId.DefeatJingle,
                new[] { "sfx/battle_ended_lose.wav" },
                EvMatchFinished,
                "失败乐句；match_finished（LevelFailed / Draw / 1P 下 AI 胜）"),

            // ================= UI（无事件，需 UI 层手动接线） =================
            new Game2Port(SfxId.UiClick,
                new[] { "sfx/ui_click.wav" },
                "",
                "按钮点击；当前无对应 EventBus 事件，UI 层未接线 → 调 AudioService.PlayUi(SfxId.UiClick)"),

            new Game2Port(SfxId.UiPanelOpen,
                new[] { "sfx/ui_confirm.wav" },
                "",
                "面板展开/确认；当前无对应 EventBus 事件，UI 层未接线 → 调 AudioService.PlayUi(SfxId.UiPanelOpen)"),
        };

        static readonly Dictionary<SfxId, Game2Port> ById = BuildById();

        /// <summary>全部搬运记录（测试遍历用；不要修改返回数组内容）。</summary>
        public static Game2Port[] All => Ports;

        /// <summary>搬运记录数量。</summary>
        public static int Count => Ports.Length;

        /// <summary>该 id 是否改用了 Game-2 素材。</summary>
        public static bool IsPorted(SfxId id)
        {
            return ById.ContainsKey(id);
        }

        /// <summary>取搬运记录；未搬运的 id 抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
        public static Game2Port Get(SfxId id)
        {
            if (!ById.TryGetValue(id, out Game2Port port))
                throw new ArgumentOutOfRangeException(nameof(id), "该 SfxId 未登记 Game-2 搬运: " + id);
            return port;
        }

        /// <summary>取搬运记录（不抛异常）。</summary>
        public static bool TryGet(SfxId id, out Game2Port port)
        {
            return ById.TryGetValue(id, out port);
        }

        /// <summary>该 id 的变奏数量（未搬运返回 1，即只有程序化素材本身）。</summary>
        public static int VariantCount(SfxId id)
        {
            return ById.TryGetValue(id, out Game2Port port) ? port.VariantCount : 1;
        }

        /// <summary>
        /// 第 <paramref name="variantIndex"/> 个变奏的目标文件名（不含目录与扩展名）。
        /// 主变奏复用 <see cref="SfxCatalog.AssetFileName"/>（与搬运前同名 → GUID 不变），
        /// 其余加 <c>_2</c>/<c>_3</c>… 后缀。
        /// </summary>
        public static string TargetFileName(SfxId id, int variantIndex)
        {
            if (variantIndex < 0)
                variantIndex = 0;

            string baseName = SfxCatalog.AssetFileName(id);
            return variantIndex == 0
                ? baseName
                : baseName + string.Format(VariantSuffixFormat, variantIndex + 1);
        }

        /// <summary>该 id 全部变奏的目标文件名。</summary>
        public static string[] TargetFileNames(SfxId id)
        {
            int count = VariantCount(id);
            var names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = TargetFileName(id, i);
            return names;
        }

        /// <summary>该 id 全部变奏的目标资产路径（相对工程根，如 <c>Assets/Resources/PirateCrewAudio/SfxExplosion.wav</c>）。</summary>
        public static string[] TargetAssetPaths(SfxId id)
        {
            string[] names = TargetFileNames(id);
            var paths = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
                paths[i] = ResourcesFolder + "/" + names[i] + ".wav";
            return paths;
        }

        /// <summary>某条搬运记录第 <paramref name="variantIndex"/> 个变奏的源文件绝对路径。</summary>
        public static string SourcePath(Game2Port port, int variantIndex)
        {
            if (port.Sources == null || variantIndex < 0 || variantIndex >= port.Sources.Length)
                return null;
            return SourceRoot + "/" + port.Sources[variantIndex];
        }

        /// <summary>全部目标资产文件名（含变奏）——AudioAssetBuilder 的孤儿清理白名单。</summary>
        public static HashSet<string> AllTargetFileNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Ports.Length; i++)
            {
                string[] names = TargetFileNames(Ports[i].Id);
                for (int j = 0; j < names.Length; j++)
                    set.Add(names[j] + ".wav");
            }

            return set;
        }

        static Dictionary<SfxId, Game2Port> BuildById()
        {
            var map = new Dictionary<SfxId, Game2Port>(Ports.Length);
            for (int i = 0; i < Ports.Length; i++)
            {
                if (map.ContainsKey(Ports[i].Id))
                    throw new InvalidOperationException("Game-2 搬运表出现重复 SfxId: " + Ports[i].Id);
                if (Ports[i].VariantCount == 0)
                    throw new InvalidOperationException("Game-2 搬运表缺源文件: " + Ports[i].Id);
                map.Add(Ports[i].Id, Ports[i]);
            }

            return map;
        }
    }
}
