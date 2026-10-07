namespace PirateCrew.Data
{
    /// <summary>
    /// 本工程 17 种武器 id。
    ///
    /// 【口径】枚举本体为本工程设计值；成员名沿用关卡 XML 的属性键
    ///         （<c>cherryBomb="10"</c> 等的小驼峰形式），以便代码索引与关卡数据一一对应。
    ///
    /// 【枚举顺序】cannonball … cannon 共 16 种，
    ///             第 17 个 SweepingFlame 是 rumBottle 落地后生成的蔓延火焰。
    ///
    /// 【注意】cannonball 是每回合的保底武器，
    ///         不属于"面板可投放"的常规武器，但仍在总表内。
    /// </summary>
    public enum WeaponId
    {
        /// <summary>炮弹（保底武器）。</summary>
        Cannonball = 0,

        /// <summary>樱桃炸弹。</summary>
        CherryBomb = 1,

        /// <summary>炸药。</summary>
        Dynamite = 2,

        /// <summary>巨石。</summary>
        Boulder = 3,

        /// <summary>香蕉（会弹跳）。</summary>
        Banana = 4,

        /// <summary>地雷（跨回合常驻）。</summary>
        Mine = 5,

        /// <summary>跳伞炸弹。</summary>
        ParachuteBomb = 6,

        /// <summary>朗姆酒瓶（落地生火）。</summary>
        RumBottle = 7,

        /// <summary>八枚金币（可复用 8 次）。</summary>
        PiecesOfEight = 8,

        /// <summary>火药桶（放置 2 个、可连锁）。</summary>
        GunpowderBarrel = 9,

        /// <summary>木箱（放置 3 个、掩体）。</summary>
        WoodenCrate = 10,

        /// <summary>船锚（点击直落、60 固定伤害）。</summary>
        Anchor = 11,

        /// <summary>海鸥（右键选高度 + 投弹）。</summary>
        Seagull = 12,

        /// <summary>潮汐巨浪（点击引爆横扫）。</summary>
        TidalWave = 13,

        /// <summary>巫毒娃娃（锁定敌人后抛飞目标）。</summary>
        VoodooDoll = 14,

        /// <summary>加农炮（放置 + 拖 pin 发射）。</summary>
        Cannon = 15,

        /// <summary>蔓延火焰（rumBottle 落地生成，非面板投放）。</summary>
        SweepingFlame = 16,
    }
}
