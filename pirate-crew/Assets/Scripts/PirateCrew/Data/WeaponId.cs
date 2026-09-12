namespace PirateCrew.PirateCrew.Data
{
    /// <summary>
    /// 原版《海盗军团抢宝藏》(Mutiny) 的 17 种武器 id。
    ///
    /// 【出处】静态逆向文档 §5.2「武器总表」与 §9.2「武器数量 = 17 种」。
    ///         成员名直接采用文档表格里的英文 id（= 原版 ActionScript 类名的小驼峰形式），
    ///         以便与关卡 XML 的属性键（<c>cherryBomb="10"</c> 等）一一对应。
    ///
    /// 【枚举顺序】与 §5.2 表格行序一致：cannonball … cannon 共 16 种，
    ///             第 17 个 SweepingFlame 是 rumBottle 落地后生成的蔓延火焰。
    ///
    /// 【注意】cannonball 是每回合的保底武器（§3.2「保底武器」），
    ///         不属于"面板可投放"的常规武器，但仍在总表内。
    /// </summary>
    public enum WeaponId
    {
        /// <summary>炮弹（保底武器）；§5.2 第 1 行。</summary>
        Cannonball = 0,

        /// <summary>樱桃炸弹；§5.2 第 2 行。</summary>
        CherryBomb = 1,

        /// <summary>炸药；§5.2 第 3 行。</summary>
        Dynamite = 2,

        /// <summary>巨石；§5.2 第 4 行。</summary>
        Boulder = 3,

        /// <summary>香蕉（会弹跳）；§5.2 第 5 行。</summary>
        Banana = 4,

        /// <summary>地雷（跨回合常驻）；§5.2 第 6 行。</summary>
        Mine = 5,

        /// <summary>跳伞炸弹；§5.2 第 7 行。</summary>
        ParachuteBomb = 6,

        /// <summary>朗姆酒瓶（落地生火）；§5.2 第 8 行。</summary>
        RumBottle = 7,

        /// <summary>八枚金币（可复用 8 次）；§5.2 第 9 行。</summary>
        PiecesOfEight = 8,

        /// <summary>火药桶（放置 2 个、可连锁）；§5.2 第 10 行。</summary>
        GunpowderBarrel = 9,

        /// <summary>木箱（放置 3 个、掩体）；§5.2 第 11 行。</summary>
        WoodenCrate = 10,

        /// <summary>船锚（点击直落、60 固定伤害）；§5.2 第 12 行。</summary>
        Anchor = 11,

        /// <summary>海鸥（右键选高度 + 投弹）；§5.2 第 13 行。</summary>
        Seagull = 12,

        /// <summary>潮汐巨浪（点击引爆横扫）；§5.2 第 14 行。</summary>
        TidalWave = 13,

        /// <summary>巫毒娃娃（锁定敌人后抛飞目标）；§5.2 第 15 行。</summary>
        VoodooDoll = 14,

        /// <summary>加农炮（放置 + 拖 pin 发射）；§5.2 第 16 行。</summary>
        Cannon = 15,

        /// <summary>蔓延火焰（rumBottle 落地生成，非面板投放）；§5.2 第 17 行。</summary>
        SweepingFlame = 16,
    }
}
