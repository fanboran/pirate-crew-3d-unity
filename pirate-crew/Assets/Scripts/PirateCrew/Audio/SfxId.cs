namespace PirateCrew.Audio
{
    /// <summary>
    /// 程序化音效清单（全部为原创合成，零第三方素材，符合版权纪律）。
    ///
    /// 【命名与目录】枚举名即 wav 资产文件名（<c>SfxId.WavesLoop</c> → <c>AmbientWavesLoop.wav</c>
    /// 这种带前缀的形式由 <see cref="SfxCatalog.AssetFileName"/> 统一生成），
    /// wav 资产平铺在 <c>Assets/Resources/PirateCrewAudio/</c>（前缀只体现在文件名，
    /// 如 <c>AmbientWavesLoop.wav</c>，由 <see cref="SfxCatalog.AssetFileName"/> 统一生成）。
    ///
    /// 【内容依据】GDD 支柱 4「海盗味」列出的听觉符号——
    /// 「船歌小调、海浪声、木桶碎裂、大炮轰鸣」（F:\VSCode\game-3\docs\gdd.md:113）；
    /// 场景氛围基调「加勒比正午海岛」（docs/美术风格指南.md:12-16）。
    /// 具体每个音效的配方要点见 <see cref="SfxCatalog"/> 的 Recipe 字段。
    /// </summary>
    public enum SfxId
    {
        // ---------------- 战斗（Sfx） ----------------
        /// <summary>爆炸：低频冲击 + 噪声爆 + 尾音混响（大炮/炸药/樱桃炸弹/巨石）。</summary>
        Explosion = 0,

        /// <summary>木桶/木箱碎裂：木质腔体 + 多段木片断裂噪声。</summary>
        WoodCrack = 1,

        /// <summary>命中肉体：低频闷响 + 短促拍打噪声。</summary>
        FleshHit = 2,

        /// <summary>弹体入水：水花噪声 + 下沉气泡尾。</summary>
        WaterSplash = 3,

        /// <summary>投掷出手 whoosh：带通噪声呼啸（中心频率先升后降）。</summary>
        ThrowWhoosh = 4,

        /// <summary>落地弹跳：短促弹性音高下坠。</summary>
        Bounce = 5,

        /// <summary>石块滚动：低频隆隆 + 慢速摩擦调制。</summary>
        StoneRoll = 6,

        /// <summary>地雷引信蜂鸣（对应 <c>battle_mine_beep</c> 事件，§5.2 beepTimes）。</summary>
        MineBeep = 7,

        /// <summary>船员阵亡：下行低音号角（crew_died 无位置载荷，2D 播放）。</summary>
        CrewDown = 8,

        // ---------------- 场景 / 环境（Ambient） ----------------
        /// <summary>海浪循环（多段噪声调制，无缝循环）。</summary>
        WavesLoop = 9,

        /// <summary>海鸥鸣叫变体 1。</summary>
        SeagullCry1 = 10,

        /// <summary>海鸥鸣叫变体 2。</summary>
        SeagullCry2 = 11,

        /// <summary>海鸥鸣叫变体 3。</summary>
        SeagullCry3 = 12,

        /// <summary>风声循环（慢速阵风调制，无缝循环）。</summary>
        WindLoop = 13,

        // ---------------- 反馈（Sfx） ----------------
        /// <summary>单位选中：两音上行短提示。</summary>
        UnitSelect = 14,

        /// <summary>武器切换：点击 + 木扣声 + 第二下确认。</summary>
        WeaponSwitch = 15,

        /// <summary>回合开始：三音上行琶音。</summary>
        TurnStart = 16,

        /// <summary>回合结束：两音下行。</summary>
        TurnEnd = 17,

        /// <summary>危险提示（落水边缘）：急促双音警笛。</summary>
        DangerWarning = 18,

        // ---------------- UI（Sfx） ----------------
        /// <summary>按钮点击。</summary>
        UiClick = 19,

        /// <summary>面板展开：柔和上扫 whoosh。</summary>
        UiPanelOpen = 20,

        /// <summary>错误/禁用：低沉粗糙双音。</summary>
        UiError = 21,

        // ---------------- 结果（Music） ----------------
        /// <summary>胜利短乐句（A 小调 i-VI-III-VII，约 4 秒）。</summary>
        VictoryJingle = 22,

        /// <summary>失败短乐句（A 和声小调下行 i-VII-VI-V，约 3.6 秒）。</summary>
        DefeatJingle = 23,

        // ---------------- 环境底床（外部素材，无程序化合成） ----------------
        /// <summary>
        /// 环境底床垫底循环：**外部素材**（隔壁 Game-2 自产的
        /// <c>bgm/ambient_pad.wav</c>，22 秒无缝单声道），由
        /// <see cref="Game2AudioAssets"/> 登记、<c>AudioAssetBuilder.SyncPortedAssets</c> 搬入；
        /// 本 id 没有 <c>SynthRenderer</c> 实现（<see cref="SynthRenderer.CanRender"/> 为 false），
        /// 资产缺失时静音而不是回落成别的音色。
        /// </summary>
        BedPad = 24,
    }
}
