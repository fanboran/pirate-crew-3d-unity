using System;

namespace PirateCrew.Data
{
    /// <summary>
    /// 武器引爆 / 触发条件（对应静态逆向文档 §5.2「触发/引爆条件」列）。
    ///
    /// 【为什么是 [Flags]】原版部分武器有多个并列触发条件，最典型的是 banana：
    ///   「静止 <b>或</b> 玩家点击鼠标 <b>或</b> AI 近距条件」都要能引爆。
    ///   用按位组合表达才不会丢失语义（单枚举值只能二选一）。
    ///
    /// 【语义速查】
    ///   OnContact      撞瓦片 / 箱体 / 敌人即爆（cherryBomb / parachuteBomb / rumBottle / piecesOfEight / cannonball；boulder 为碾压接触）
    ///   OnRest         静止时引爆（dynamite 在 <c>vx==0 &amp;&amp; |vy|&lt;0.2</c> 引爆）
    ///   OnClick        玩家鼠标点击触发（banana 点击、anchor 点击放置、seagull 点击选高/投弹、tidalWave 点击引爆）
    ///   ProximityFuse  有角色进入 60px 且处于移动状态 → 启动 60 帧引信（mine）
    ///   OnExplosionHit 自身不主动爆，被任意 Explosion 命中时触发（gunpowderBarrel 连锁）
    ///   OnPlace        放置类道具，一次放置固定数量、跨回合常驻（woodenCrate / gunpowderBarrel / cannon 摆位）
    ///   Special        由专用脚本/机制处理（voodooDoll 锁定目标、cannon 蓄力、SweepingFlame 由 rumBottle 生成）
    /// </summary>
    [Flags]
    public enum WeaponTrigger
    {
        /// <summary>无引爆触发器：纯物理 / 掩体 / 固定伤害类（woodenCrate、boulder、anchor、tidalWave 的持续伤害等由各自机制结算）。</summary>
        None = 0,

        /// <summary>接触即爆：撞到瓦片 / 箱体 / 敌人时引爆。</summary>
        OnContact = 1 << 0,

        /// <summary>静止时引爆：速度归零（<c>vx==0</c> 且 <c>|vy|&lt;0.2</c>）后触发。</summary>
        OnRest = 1 << 1,

        /// <summary>玩家点击触发：需要一次鼠标点击才引爆 / 放置 / 发射。</summary>
        OnClick = 1 << 2,

        /// <summary>有人 60px 内且移动 → 引信 60 帧：须有角色进入 60px 且处于移动状态才启动倒计时。</summary>
        ProximityFuse = 1 << 3,

        /// <summary>被爆炸命中触发：自身不主动爆炸，被任意 Explosion 命中时 <c>explode()</c>（火药桶连锁）。</summary>
        OnExplosionHit = 1 << 4,

        /// <summary>放置类：一次放置固定数量并跨回合常驻（数量见 WeaponStats.PlaceableCount）。</summary>
        OnPlace = 1 << 5,

        /// <summary>特殊机制：由武器专用脚本处理（锁定目标 / 蓄力发射 / 落地生成等）。</summary>
        Special = 1 << 6,
    }
}
