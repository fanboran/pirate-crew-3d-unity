using System;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 一个音效的合成配方元数据（纯值类型，可无头断言）。
    ///
    /// 【为什么把「配方」与「合成代码」分开】<see cref="SfxCatalog"/> 是**数据**：
    /// 时长/声道/循环/空间化/音量/触发来源，测试可以直接遍历它做一致性校验
    /// （时长与渲染结果吻合、采样率统一、峰值不削波、首尾淡出）。
    /// 实际波形生成在 <c>SynthRenderer</c> 按 <see cref="SfxId"/> 分派，
    /// 二者由 <see cref="SfxCatalog.Get"/> 的同一份表约束，不会各说各话。
    /// </summary>
    public readonly struct SfxRecipe
    {
        /// <summary>音效 id。</summary>
        public readonly SfxId Id;

        /// <summary>总线分类。</summary>
        public readonly AudioCategory Category;

        /// <summary>时长（秒）；循环音为循环体长度。</summary>
        public readonly double DurationSeconds;

        /// <summary>是否循环（Ambient 的 Waves/Wind 为 true）。</summary>
        public readonly bool Loop;

        /// <summary>空间化模式。</summary>
        public readonly SpatialMode Spatial;

        /// <summary>默认相对音量（0–1，总线之上再做分类缩放的「素材平衡」）。</summary>
        public readonly float DefaultVolume;

        /// <summary>3D 最小距离（世界单位）；小于它不衰减。</summary>
        public readonly float MinDistance;

        /// <summary>3D 最大距离（世界单位）；大于它静音。</summary>
        public readonly float MaxDistance;

        /// <summary>触发来源（人可读，写入交付报告/资产 README）。</summary>
        public readonly string Trigger;

        /// <summary>合成配方要点（人可读；数值均为 AI 提案/待定，需人耳验收）。</summary>
        public readonly string Recipe;

        public SfxRecipe(
            SfxId id,
            AudioCategory category,
            double durationSeconds,
            bool loop,
            SpatialMode spatial,
            float defaultVolume,
            float minDistance,
            float maxDistance,
            string trigger,
            string recipe)
        {
            Id = id;
            Category = category;
            DurationSeconds = durationSeconds;
            Loop = loop;
            Spatial = spatial;
            DefaultVolume = defaultVolume;
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            Trigger = trigger;
            Recipe = recipe;
        }
    }

    /// <summary>
    /// 程序化音效配方总表（唯一数据源）。
    ///
    /// 【内容依据】GDD 支柱 4「海盗味」的听觉符号清单
    /// （F:\VSCode\game-3\docs\gdd.md:113：船歌小调 / 海浪声 / 木桶碎裂 / 大炮轰鸣）
    /// 与场景氛围基调「加勒比正午海岛」（docs/美术风格指南.md:12-16）。
    ///
    /// 【版权纪律】全表零第三方素材，波形 100% 由 Synth 目录的纯函数合成，可审计。
    ///
    /// 【数值标注】时长、音量平衡、衰减距离、配方参数均为 **AI 提案/待定**：
    /// 原版 Flash 逆向文档只给了机制（如 mine 的 beepTimes，§5.2），没有音频素材与混音参数。
    /// 交付报告列出建议人耳验收的组合。
    /// </summary>
    public static class SfxCatalog
    {
        /// <summary>全表统一采样率（Hz）。</summary>
        public const int SampleRate = 44100;

        /// <summary>全表统一声道数（单声道；3D 空间音必须单声道，见 AudioBuffer 注释）。</summary>
        public const int Channels = 1;

        /// <summary>
        /// 归一化目标峰值。留约 1 dB 余量（0.89 ≈ -1.0 dBFS），
        /// 避免多路同时播放时的总线削波，也避免 float→int16 的边界回绕。
        /// </summary>
        public const float PeakTarget = 0.89f;

        /// <summary>3D 默认最小距离（世界单位）；竞技场相机距离约 30（美术风格指南 §1.1）。
        /// 【格 1→2 单位 ×2】3 → 6（空间衰减距离类一律 ×2，保持"同一格数处响度相同"）。</summary>
        public const float DefaultMinDistance = 6f;

        /// <summary>3D 默认最大距离（世界单位）。【格 1→2 单位 ×2】50 → 100。</summary>
        public const float DefaultMaxDistance = 100f;

        static readonly SfxRecipe[] Recipes =
        {
            // ================= 战斗 =================
            new SfxRecipe(SfxId.Explosion, AudioCategory.Sfx, 1.60d, false, SpatialMode.ThreeD, 0.90f, 8f, 120f,
                "battle_projectile_detonated（cannonball / cherryBomb / dynamite / boulder / mine / parachuteBomb / rumBottle / SweepingFlame）",
                "低频冲击（正弦 95→32 Hz 指数滑落，tau≈0.22s）+ 噪声爆（白噪 → 二阶低通 1500→600 Hz，tau≈0.09s）+ 隆隆尾（低通 220 Hz，tau≈0.55s）+ Schroeder 混响 25%"),

            new SfxRecipe(SfxId.WoodCrack, AudioCategory.Sfx, 0.60d, false, SpatialMode.ThreeD, 0.80f, 6f, 90f,
                "battle_projectile_detonated（gunpowderBarrel / woodenCrate）",
                "木质腔体（三角 190 Hz + 分音 320/540 Hz，tau 0.10–0.18s）+ 4–6 段木片断裂噪声（带通 900–3500 Hz，5–18 ms 随机错位）"),

            new SfxRecipe(SfxId.FleshHit, AudioCategory.Sfx, 0.24d, false, SpatialMode.ThreeD, 0.70f, 4f, 70f,
                "crew_damaged（载荷无世界坐标 → 目前退化为 2D，见交付报告待裁决项）",
                "低频闷响（正弦 150→65 Hz 指数滑落，tau≈0.05s）+ 拍打噪声（低通 1400 Hz，tau≈0.025s）"),

            new SfxRecipe(SfxId.WaterSplash, AudioCategory.Sfx, 0.70d, false, SpatialMode.ThreeD, 0.75f, 6f, 100f,
                "无事件（弹体落水消失路径不广播）；公开 API PlaySfx 手动触发",
                "水花（白噪 → 高通 700 Hz，起音 4 ms，tau≈0.08s）+ 音高下坠（正弦 700→250 Hz）+ 气泡尾（低通 500 Hz，11 Hz 振幅调制，tau≈0.25s）"),

            new SfxRecipe(SfxId.ThrowWhoosh, AudioCategory.Sfx, 0.42d, false, SpatialMode.TwoD, 0.65f, 4f, 80f,
                "battle_shot_released（载荷为拖拽距离 px → 映射音高/音量）",
                "带通噪声中心频率 180→2400→420 Hz 两段扫（Q≈1.2）+ 幅度 sin^1.5 包络；拖拽越远音高越高"),

            new SfxRecipe(SfxId.Bounce, AudioCategory.Sfx, 0.16d, false, SpatialMode.ThreeD, 0.60f, 4f, 60f,
                "无事件（弹体落地弹跳在 WeaponProjectile 内部结算）；公开 API 手动触发",
                "弹性音高下坠（正弦 420→130 Hz，tau≈0.05s）+ 3 ms 高频点击（噪声高通 2000 Hz）"),

            new SfxRecipe(SfxId.StoneRoll, AudioCategory.Sfx, 1.40d, false, SpatialMode.ThreeD, 0.55f, 6f, 80f,
                "无事件（boulder 滚动在 WeaponProjectile 内部结算）；公开 API 手动触发",
                "低频隆隆（低通 420 Hz + 900 Hz 双层噪声）+ 4/7 Hz 摩擦调制 + 70 Hz 次低音抖动"),

            new SfxRecipe(SfxId.MineBeep, AudioCategory.Sfx, 0.09d, false, SpatialMode.ThreeD, 0.62f, 6f, 80f,
                "battle_mine_beep（§5.2 beepTimes：0/15/30/38/45/49/53/55/57/59 帧）",
                "方波 2093 Hz（C7）60 ms，5 ms 起音/释音，轻微音高下坠；越接近引爆音量略升（由 AudioService 按 ElapsedFrames 缩放）"),

            new SfxRecipe(SfxId.CrewDown, AudioCategory.Sfx, 0.70d, false, SpatialMode.TwoD, 0.62f, 4f, 60f,
                "crew_died（载荷无世界坐标 → 2D 播放）",
                "下行低音号角（锯齿+谐波 330→165 Hz，低通 1200→400 Hz 扫落）+ 短混响"),

            // ================= 环境 =================
            new SfxRecipe(SfxId.WavesLoop, AudioCategory.Ambient, 6.00d, true, SpatialMode.TwoD, 0.50f, 0f, 0f,
                "battle_started → AudioService.StartAmbient；无战斗场景时由场景控制器手动调用",
                "三层：低频隆隆（低通 260 Hz，0.35 基幅）+ 每 2 秒一次涌浪（低通 1800 Hz，sin^1.6 包络）+ 浪花泡沫（高通 3000 Hz，sin^4 包络）；调制频率取 6 秒整除（3/2/1 周期）保证循环无缝，末尾用 FoldSeamlessLoop 交叉淡化"),

            new SfxRecipe(SfxId.SeagullCry1, AudioCategory.Ambient, 0.90d, false, SpatialMode.TwoD, 0.42f, 0f, 0f,
                "battle_started 后 AudioService 随机间隔点缀（3 个变体随机取一）",
                "两声「嘎」：基频 1150→1600→900 Hz 滑音 + 4 个谐波（1/h^1.5）+ 带通 2 kHz 共振峰 + 少量噪声嘶哑"),

            new SfxRecipe(SfxId.SeagullCry2, AudioCategory.Ambient, 1.10d, false, SpatialMode.TwoD, 0.42f, 0f, 0f,
                "同 SeagullCry1（变体 2：基频更低、三声）",
                "三声「嘎」：基频 980→1380→820 Hz，间距更长，尾音上扬"),

            new SfxRecipe(SfxId.SeagullCry3, AudioCategory.Ambient, 0.75d, false, SpatialMode.TwoD, 0.42f, 0f, 0f,
                "同 SeagullCry1（变体 3：单声短促）",
                "单声短促「嘎」：基频 1250→1700→1000 Hz，快速收尾"),

            new SfxRecipe(SfxId.WindLoop, AudioCategory.Ambient, 8.00d, true, SpatialMode.TwoD, 0.38f, 0f, 0f,
                "battle_started → AudioService.StartAmbient",
                "带通噪声（250–900 Hz）+ 两层慢速阵风调制（0.125/0.25 Hz，8 秒整除）+ 180 Hz 低频底噪；末尾 FoldSeamlessLoop 交叉淡化"),

            // ================= 反馈 =================
            new SfxRecipe(SfxId.UnitSelect, AudioCategory.Sfx, 0.16d, false, SpatialMode.TwoD, 0.55f, 0f, 0f,
                "无事件（选角在 AimThrowController 内部）；公开 API PlayUi 手动触发",
                "两音上行：正弦 1046.5 Hz（C6）40 ms → 1567.98 Hz（G6）50 ms，5 ms 起音"),

            new SfxRecipe(SfxId.WeaponSwitch, AudioCategory.Sfx, 0.22d, false, SpatialMode.TwoD, 0.60f, 0f, 0f,
                "ai_decided（载荷含 WeaponSlotIndex → 槽位变化时播放）",
                "机械点击（噪声高通 2500 Hz，6 ms）+ 木扣（三角 520 Hz，tau≈0.03s）+ 70 ms 后第二下低音确认"),

            new SfxRecipe(SfxId.TurnStart, AudioCategory.Sfx, 0.70d, false, SpatialMode.TwoD, 0.62f, 0f, 0f,
                "turn_started",
                "三音上行琶音 C5-E5-G5-C6（MIDI 72/76/79/84），三角波叠加 3 次谐波，每音 90 ms 错位，尾部混响 20%"),

            new SfxRecipe(SfxId.TurnEnd, AudioCategory.Sfx, 0.55d, false, SpatialMode.TwoD, 0.55f, 0f, 0f,
                "turn_ended",
                "两音下行 G4→C4（MIDI 67/60），三角波，慢起音 12 ms，尾部混响 20%"),

            new SfxRecipe(SfxId.DangerWarning, AudioCategory.Sfx, 0.60d, false, SpatialMode.TwoD, 0.66f, 0f, 0f,
                "无事件（危险判定在投掷/移动规则内）；公开 API PlayUi 手动触发",
                "急促双音警笛：440/660 Hz 方波交替 3 次（每次 90 ms），25 Hz 振幅调制，小幅噪声粗糙化"),

            // ================= UI =================
            new SfxRecipe(SfxId.UiClick, AudioCategory.Sfx, 0.09d, false, SpatialMode.TwoD, 0.50f, 0f, 0f,
                "无事件（UI 按钮在 UI 层）；公开 API PlayUi 手动接线（UI 文件不在本波次白名单，见报告）",
                "正弦 1200 Hz 25 ms tau + 3 ms 噪声点击（高通 1500 Hz）"),

            new SfxRecipe(SfxId.UiPanelOpen, AudioCategory.Sfx, 0.36d, false, SpatialMode.TwoD, 0.50f, 0f, 0f,
                "无事件；公开 API PlayUi 手动触发",
                "柔和上扫噪声（带通 380→2600 Hz，起音 40 ms）+ 叠加上行三角 300→620 Hz；尾部轻微混响"),

            new SfxRecipe(SfxId.UiError, AudioCategory.Sfx, 0.32d, false, SpatialMode.TwoD, 0.55f, 0f, 0f,
                "无事件；公开 API PlayUi 手动触发",
                "低沉粗糙双音：方波 160 Hz + 120 Hz 交替（各 140 ms），5 Hz 调幅，噪声粗糙化"),

            // ================= 结果 =================
            new SfxRecipe(SfxId.VictoryJingle, AudioCategory.Music, 4.00d, false, SpatialMode.TwoD, 0.70f, 0f, 0f,
                "match_finished（Outcome = Team0Win，1P 玩家胜）",
                "A 小调 i-VI-III-VII（Am-F-C-G）进行，每和弦 1 秒：低音 + 琶音（八分）+ 旋律层；每和弦由 MusicTheory.TriadOnDegree 生成，失谐叠加 + 混响 30%"),

            new SfxRecipe(SfxId.DefeatJingle, AudioCategory.Music, 3.60d, false, SpatialMode.TwoD, 0.66f, 0f, 0f,
                "match_finished（LevelFailed / Draw / 1P 模式下 AI 胜）",
                "A 和声小调下行 i-VII-VI-V（Am-G-F-E），每和弦 0.9 秒，低音区弦垫（失谐锯齿 + 低通）+ 下行旋律 E4-D4-C4-B3；混响 30%"),

            // ================= 环境底床（外部素材） =================
            new SfxRecipe(SfxId.BedPad, AudioCategory.Ambient, 22.00d, true, SpatialMode.TwoD, 0.34f, 0f, 0f,
                "battle_started → AudioService.StartAmbientBed（底床第 3 层「垫底」）",
                "外部素材（非合成）：隔壁 Game-2 自产 bgm/ambient_pad.wav，44100 Hz 单声道 22.0 s 无缝循环；搬运登记见 Game2AudioAssets.cs。本 id 无程序化合成，资产缺失时静音（SynthRenderer.CanRender = false）"),
        };

        static readonly SfxRecipe[] ById = BuildById();

        /// <summary>全表数量。</summary>
        public static int Count => Recipes.Length;

        /// <summary>按顺序访问全表（测试遍历用；不要修改返回数组内容）。</summary>
        public static SfxRecipe[] All => Recipes;

        /// <summary>取某音效的配方；未知 id 抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
        public static SfxRecipe Get(SfxId id)
        {
            int index = (int)id;
            if (index < 0 || index >= ById.Length)
                throw new ArgumentOutOfRangeException(nameof(id), "未登记的 SfxId: " + id);
            return ById[index];
        }

        /// <summary>分类是否会导致循环播放（循环音必须无缝，非循环音必须首尾淡出）。</summary>
        public static bool IsLoop(SfxId id)
        {
            return Get(id).Loop;
        }

        /// <summary>
        /// wav 资产文件名（不含目录与扩展名）。沿用枚举名，按分类加前缀避免跨目录重名歧义。
        /// 例：<c>AmbientWavesLoop</c>、<c>MusicVictoryJingle</c>、<c>SfxExplosion</c>。
        /// </summary>
        public static string AssetFileName(SfxId id)
        {
            return CategoryPrefix(Get(id).Category) + id.ToString();
        }

        /// <summary>分类 → 资产文件名前缀。</summary>
        public static string CategoryPrefix(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.Ambient:
                    return "Ambient";
                case AudioCategory.Music:
                    return "Music";
                default:
                    return "Sfx";
            }
        }

        /// <summary>
        /// 分类 → 相对 Assets 的资产根（仅作说明用途；wav 实际平铺在
        /// <c>Assets/Resources/PirateCrewAudio/</c>，运行时按 <see cref="AssetFileName"/> 经
        /// <c>Resources.Load</c> 加载，AudioAssetBuilder 也按平铺路径落盘）。
        /// </summary>
        public static string CategoryFolder(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.Ambient:
                    return "Assets/Resources/PirateCrewAudio";
                case AudioCategory.Music:
                    return "Assets/Resources/PirateCrewAudio";
                default:
                    return "Assets/Resources/PirateCrewAudio";
            }
        }

        static SfxRecipe[] BuildById()
        {
            // 用 Id 的最大值 +1 建索引，保证 Get 是 O(1) 且能查出漏登记
            int max = 0;
            for (int i = 0; i < Recipes.Length; i++)
            {
                int id = (int)Recipes[i].Id;
                if (id > max)
                    max = id;
            }

            var table = new SfxRecipe[max + 1];
            for (int i = 0; i < table.Length; i++)
                table[i] = default; // default 的 Id 为 Explosion；下面用 seen 标记真正的漏项

            var seen = new bool[max + 1];
            for (int i = 0; i < Recipes.Length; i++)
            {
                int id = (int)Recipes[i].Id;
                table[id] = Recipes[i];
                seen[id] = true;
            }

            // 漏登记的 id 用「零时长」标记，测试会断言全表无零时长项
            for (int i = 0; i < table.Length; i++)
            {
                if (!seen[i])
                    table[i] = new SfxRecipe((SfxId)i, AudioCategory.Sfx, 0d, false, SpatialMode.TwoD, 0f, 0f, 0f,
                        "(未登记)", "(未登记)");
            }

            return table;
        }
    }
}
