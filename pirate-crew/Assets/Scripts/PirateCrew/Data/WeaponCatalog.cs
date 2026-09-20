using System.Collections.Generic;

namespace PirateCrew.Data
{
    /// <summary>
    /// 单件武器的静态数值（纯 C# 数据结构，可在无头验证台断言）。
    ///
    /// 【出处】静态逆向文档 §5.2「武器总表」逐行逐值。
    /// 【缺省约定】文档表格里写作「—」的格子统一落成 <c>0</c>，
    ///             并在 <see cref="Remark"/> 里注明该格在原表中为「—」，避免被误读成"数值 0"。
    /// 【语义】
    ///   <see cref="LimitedToTurn"/>：true = 仅限本回合（发射/使用后销毁）；
    ///                                false = 跨回合常驻（mine / gunpowderBarrel / woodenCrate）。
    ///   <see cref="DirectDamage"/>：非爆炸的固定/持续伤害，具体结算粒度见 <see cref="Remark"/>。
    /// </summary>
    public readonly struct WeaponStats
    {
        /// <summary>武器 id。</summary>
        public readonly WeaponId Id;

        /// <summary>显示名（沿用原版英文类名 / XML 属性键）。</summary>
        public readonly string DisplayName;

        /// <summary>水平 AABB 半径（px）。</summary>
        public readonly float AabbRadius;

        /// <summary>垂直 AABB 半径（px）；箱体为 15（原表 16/16/15/15），一般武器与水平相同。</summary>
        public readonly float AabbVerticalRadius;

        /// <summary>摩擦力（着地时每帧 |vx| 的递减量）。</summary>
        public readonly float Friction;

        /// <summary>重量（每帧 velocityY += weight 的重力）。</summary>
        public readonly float Weight;

        /// <summary>弹跳系数（撞地 vy *= -bounce）。</summary>
        public readonly float Bounce;

        /// <summary>弹弓最大初速（0 = 原表为「—」，不通过弹弓发射）。</summary>
        public readonly float TwangMax;

        /// <summary>引爆 / 触发条件。</summary>
        public readonly WeaponTrigger Trigger;

        /// <summary>爆炸范围大小（0 = 原表为「—」，无爆炸）。</summary>
        public readonly float ExplosionSize;

        /// <summary>爆炸中心最大伤害（0 = 无爆炸）。</summary>
        public readonly float ExplosionMaxDamage;

        /// <summary>true = 限本回合；false = 跨回合常驻。</summary>
        public readonly bool LimitedToTurn;

        /// <summary>拖拽半径（画圈 &amp; 拖拽判定用，0 = 非拖拽 / 原表未单列）。</summary>
        public readonly float DragRange;

        /// <summary>放置类一次放置数量（0 = 非放置类）。</summary>
        public readonly int PlaceableCount;

        /// <summary>可重复使用次数（0 = 非复用；piecesOfEight = 8）。</summary>
        public readonly int MaxReuses;

        /// <summary>非爆炸的固定/持续伤害（0 = 无）。</summary>
        public readonly float DirectDamage;

        /// <summary>备注：特殊行为与出处。</summary>
        public readonly string Remark;

        public WeaponStats(
            WeaponId id,
            string displayName,
            float aabbRadius,
            float aabbVerticalRadius,
            float friction,
            float weight,
            float bounce,
            float twangMax,
            WeaponTrigger trigger,
            float explosionSize,
            float explosionMaxDamage,
            bool limitedToTurn,
            float dragRange,
            int placeableCount,
            int maxReuses,
            float directDamage,
            string remark)
        {
            Id = id;
            DisplayName = displayName;
            AabbRadius = aabbRadius;
            AabbVerticalRadius = aabbVerticalRadius;
            Friction = friction;
            Weight = weight;
            Bounce = bounce;
            TwangMax = twangMax;
            Trigger = trigger;
            ExplosionSize = explosionSize;
            ExplosionMaxDamage = explosionMaxDamage;
            LimitedToTurn = limitedToTurn;
            DragRange = dragRange;
            PlaceableCount = placeableCount;
            MaxReuses = maxReuses;
            DirectDamage = directDamage;
            Remark = remark;
        }

        /// <summary>是否带爆炸（size 与 maxDamage 均为正）。</summary>
        public bool HasExplosion => ExplosionSize > 0f && ExplosionMaxDamage > 0f;
    }

    /// <summary>
    /// 17 种武器的纯 C# 静态目录表（真值来源）。
    ///
    /// 【出处】静态逆向文档 §5.2「武器总表」17 行逐行转写；
    ///         特殊行为补充自同表「备注」列、§5.1（投掷/速度）、§5.3（爆炸公式）与 §8.4（官方文案）。
    ///
    /// 【架构】本类刻意不引用任何 UnityEngine 类型：它既能在 Unity 里被
    ///         <c>M2DataAssetGenerator</c> 写进 ScriptableObject，也能在无头验证台直接断言。
    ///         任何数值修改都应先改这里，SO 只是它的序列化投影。
    /// </summary>
    public static class WeaponCatalog
    {
        // 转写时反复用到的说明文字（保持每条备注完整、可追溯）。
        const string Src = "静态逆向 §5.2";

        static readonly List<WeaponStats> _all = new List<WeaponStats>
        {
            new WeaponStats(
                WeaponId.Cannonball, "cannonball", 10f, 10f, 0.3f, 0f, 0.2f, 0f,
                WeaponTrigger.OnContact, 100f, 50f, true, 0f, 0, 0, 0f,
                Src + "：保底武器。无重力（weight=0）；撞瓦片/箱/敌人即爆；飞出地图或落水即消失。"
                + "原表 twangMax 为「—」，§5.1 记其速度由 cannon 决定，故此处记 0。"),

            new WeaponStats(
                WeaponId.CherryBomb, "cherryBomb", 9f, 9f, 0.3f, 1f, 0.2f, 20f,
                WeaponTrigger.OnContact, 80f, 40f, true, 130f, 0, 0, 0f,
                Src + "：接触即爆；§5.1 速度上限 20；最弱的新手武器。"),

            new WeaponStats(
                WeaponId.Dynamite, "dynamite", 11f, 11f, 1.7f, 1f, 0.2f, 20f,
                WeaponTrigger.OnRest, 250f, 70f, true, 130f, 0, 0, 0f,
                Src + "：静止时（vx==0 且 |vy|<0.2）引爆；落水后变 unlit，仍按静止判定爆炸（水中下沉不静止）。"),

            new WeaponStats(
                WeaponId.Boulder, "boulder", 31f, 31f, 0.25f, 1.5f, 0.2f, 20f,
                WeaponTrigger.OnContact, 0f, 0f, true, 130f, 0, 0, 0f,
                Src + "：无爆炸；碾压接触（水平 ±32px 内），伤害 = |vx| * 1.5，"
                + "把敌人推到 x±32 并继承 vx；§5.1 release() 后速度再 ×0.5；静止后逐渐淡出。weight=1.5 为全表唯一的非 1 重量。"),

            new WeaponStats(
                WeaponId.Banana, "banana", 7f, 7f, 0.5f, 1f, 0.8f, 30f,
                WeaponTrigger.OnRest | WeaponTrigger.OnClick, 160f, 80f, true, 130f, 0, 0, 0f,
                Src + "：静止 或 玩家点击鼠标 或 AI 近距条件均可引爆；会弹跳（bounce=0.8，全表最高）；§5.1 速度上限 30。"
                + "AI 近距条件：最近敌人距离在增大且 <50px，或 <20px。"),

            new WeaponStats(
                WeaponId.Mine, "mine", 14f, 14f, 1.5f, 1f, 0.2f, 20f,
                WeaponTrigger.ProximityFuse, 250f, 70f, false, 180f, 0, 0, 0f,
                Src + "：有角色在 60px 内且移动 → 引信 60 帧后引爆；limitedToTurn=false 跨回合常驻；"
                + "ignoreTime=10；dragRange=180（全表最大）；beepTimes=[0,15,30,38,45,49,53,55,57,59]。"),

            new WeaponStats(
                WeaponId.ParachuteBomb, "parachuteBomb", 11f, 11f, 0.3f, 1f, 0.2f, 30f,
                WeaponTrigger.OnContact, 160f, 50f, true, 130f, 0, 0, 0f,
                Src + "：接触引爆；空中减速（vy>1 → vy-=2；vx*=0.95）；按住鼠标当扇子：vx ±= 0.2（朝光标反方向）；§5.1 速度上限 30。"),

            new WeaponStats(
                WeaponId.RumBottle, "rumBottle", 14f, 14f, 0.3f, 1f, 0.2f, 30f,
                WeaponTrigger.OnContact, 80f, 25f, true, 130f, 0, 0, 0f,
                Src + "：接触引爆；落在 FLOOR 时额外生成 2 个 SweepingFlame（向左右蔓延的火）；§5.1 速度上限 30。"),

            new WeaponStats(
                WeaponId.PiecesOfEight, "piecesOfEight", 7f, 7f, 0.3f, 1f, 0.2f, 20f,
                WeaponTrigger.OnContact, 50f, 25f, true, 130f, 0, 8, 0f,
                Src + "：接触 / 落水引爆；可重复使用 8 次——爆后 owner.equip(同 index) 复位并 weaponLocked=true；"
                + "AI 用 aiContinue() 从 10 次随机投掷中选优。§8.4 官方文案「You get eight turns with this weapon」。"),

            new WeaponStats(
                WeaponId.GunpowderBarrel, "gunpowderBarrel", 16f, 15f, 0f, 0f, 0f, 0f,
                WeaponTrigger.OnPlace | WeaponTrigger.OnExplosionHit, 150f, 30f, false, 0f, 2, 0, 0f,
                Src + "（BoxWeapon）：放置 2 个；limitedToTurn=false 跨回合常驻；AABB 非正圆（l/r=16、top/bottom=15）；"
                + "被任意 Explosion 命中触发 explode()（caster=null，故不累加施暴者 evilness），可连锁引爆。"),

            new WeaponStats(
                WeaponId.WoodenCrate, "woodenCrate", 16f, 15f, 0f, 0f, 0f, 0f,
                WeaponTrigger.OnPlace, 0f, 0f, false, 0f, 3, 0, 0f,
                Src + "（BoxWeapon）：放置 3 个；跨回合常驻；不爆炸，用作掩体（§8.4「form a wall to protect your characters」）。AABB l/r=16、top/bottom=15。"),

            new WeaponStats(
                WeaponId.Anchor, "anchor", 48f, 96f, 0f, 0f, 0f, 0f,
                WeaponTrigger.OnClick, 0f, 0f, true, 0f, 0, 0, 60f,
                Src + "：点击放置，从 y=-200 以 vy=40 直落；命中条件 |x-anchorX| < 48 且 anchorY-64 < y < anchorY；"
                + "60 点固定伤害（非爆炸）；落地 hold 30 帧后淡出 10 帧。AABB 为 l/r=48、top=96、bottom=0（原表），"
                + "故垂直半径记 96；bottom=0 的特殊性由专用脚本处理。"),

            new WeaponStats(
                WeaponId.Seagull, "seagull", 0f, 0f, 0f, 0f, 0f, 0f,
                WeaponTrigger.OnClick, 50f, 50f, true, 0f, 0, 0, 50f,
                Src + "：点击选高度，从 x=-300 以 vx=10 向右飞；再点击投弹，每发 50/50；无重力、hitsTiles=false；"
                + "可连投多颗；飞出 levelWidth*32+275 且无弹在飞即结束。原表 AABB 各格为「—」。"),

            new WeaponStats(
                WeaponId.TidalWave, "tidalWave", 0f, 0f, 0f, 0f, 0f, 0f,
                WeaponTrigger.OnClick, 0f, 0f, true, 0f, 0, 0, 5f,
                Src + "：点击引爆——x=-550, y=water.y, vx=20 横扫到最右；每帧对 ±150px 内且 y >= waterY-300 的角色造成 5 点伤害"
                + "（非爆炸，无随距离衰减）；无重力、hitsTiles=false；对满血/残血一视同仁。原表 AABB 各格为「—」。"),

            new WeaponStats(
                WeaponId.VoodooDoll, "voodooDoll", 0f, 0f, 0f, 1f, 0f, 20f,
                WeaponTrigger.OnClick | WeaponTrigger.Special, 0f, 0f, true, 130f, 0, 0, 0f,
                Src + "：先点敌人锁定目标 → 弹弓抛出木偶；落地 10 帧后镜头切到目标，再 10 帧后把木偶的投掷速度赋给目标角色"
                + "（目标被同方向抛飞，可落水）；无爆炸；weight=1、twangMax=20。原表 AABB 各格为「—」。"),

            new WeaponStats(
                WeaponId.Cannon, "cannon", 0f, 0f, 0f, 0f, 0f, 0f,
                WeaponTrigger.OnPlace | WeaponTrigger.OnClick, 0f, 0f, false, 0f, 0, 0, 0f,
                Src + "：placeableWeapon；放置后拖尾部 pin 调角度/蓄力，松手发射 cannonball；fireStrength 达 30 才发射（≤4 不发射）；"
                + "AI 走 aiFireTime=25 延时后发射；炮弹走 cannonball（100,50），故本行的爆炸列不重复计数。"
                + "limitedToTurn=false（摆位常驻）；§6.3 记 AI 用 dragRange/2 定位，但原表未单列其 dragRange，故记 0。原表 AABB 各格为「—」。"),

            new WeaponStats(
                WeaponId.SweepingFlame, "sweepingFlame", 0f, 0f, 0f, 0f, 0f, 0f,
                WeaponTrigger.Special, 0f, 0f, true, 0f, 0, 0, 30f,
                Src + "：rumBottle 落地生成；沿地面每 8px 蔓延一段（下方有瓦片且上方无瓦片）；命中条件 dist < 8px，"
                + "每段 30 点伤害，附加随机击退（(rand-0.5)*8 / -(rand*2+6)）。原表 AABB 各格为「—」。"),
        };

        static readonly Dictionary<WeaponId, WeaponStats> _byId = BuildIndex();

        static Dictionary<WeaponId, WeaponStats> BuildIndex()
        {
            var map = new Dictionary<WeaponId, WeaponStats>(_all.Count);
            for (int i = 0; i < _all.Count; i++)
                map[_all[i].Id] = _all[i];
            return map;
        }

        /// <summary>全部武器（只读，顺序与 §5.2 表格行序一致）。</summary>
        public static IReadOnlyList<WeaponStats> All => _all;

        /// <summary>武器总数（应为 17）。</summary>
        public static int Count => _all.Count;

        /// <summary>按 id 取武器数值；id 非法时抛 <see cref="KeyNotFoundException"/>。</summary>
        public static WeaponStats Get(WeaponId id)
        {
            if (!_byId.TryGetValue(id, out WeaponStats stats))
                throw new KeyNotFoundException("WeaponCatalog 中不存在武器 id: " + id);
            return stats;
        }

        /// <summary>按 id 尝试取武器数值。</summary>
        public static bool TryGet(WeaponId id, out WeaponStats stats)
        {
            return _byId.TryGetValue(id, out stats);
        }
    }
}
