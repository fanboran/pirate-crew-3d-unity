using System;
using System.Collections.Generic;

namespace PirateCrew.Data
{
    /// <summary>
    /// 海盗共享属性快照（纯 C# 结构）。出处：静态逆向文档 §4.1。
    ///
    /// <b>关键结论：原版所有海盗没有任何属性差异</b>——没有 HP / 体重 / 速度 / 技能的区别，
    /// 全部共用同一个 <c>Character</c> 类；不同海盗（cabinBoy / squid / oldPirate / skeletonPirate / bossGuy…）
    /// <b>只有美术、luck 值（来自关卡 XML）和初始武器不同</b>。
    /// 等级差由「角色数 + 初始武器 + luck」三者共同体现，而不是靠属性差。
    /// 因此本结构对所有海盗返回同一份数值；CrewDefinition 的存在只为承接美术与关卡配置。
    /// </summary>
    public readonly struct CrewStats
    {
        /// <summary>初始/最大生命。</summary>
        public readonly int MaxHealth;

        /// <summary>AABB 半宽。</summary>
        public readonly float LeftExtent;

        /// <summary>AABB 半宽（右）。</summary>
        public readonly float RightExtent;

        /// <summary>AABB 半高（上）。</summary>
        public readonly float TopExtent;

        /// <summary>AABB 半高（下）。</summary>
        public readonly float BottomExtent;

        /// <summary>重量（每帧 velocityY += weight 的重力）。</summary>
        public readonly float Weight;

        /// <summary>摩擦力（着地时 |vx| 的每帧递减量）。</summary>
        public readonly float Friction;

        /// <summary>弹跳系数（撞地 vy *= -bounce）。</summary>
        public readonly float Bounce;

        /// <summary>弹弓最大初速。</summary>
        public readonly float TwangMaxForce;

        /// <summary>拖拽范围（仅范围圈显示与拖拽判定）。</summary>
        public readonly float DragRange;

        /// <summary>拖拽偏移（仅范围圈显示）。</summary>
        public readonly float DragOffset;

        /// <summary>默认 luck（可被关卡 XML 覆盖）。</summary>
        public readonly int Luck;

        public CrewStats(
            int maxHealth,
            float leftExtent,
            float rightExtent,
            float topExtent,
            float bottomExtent,
            float weight,
            float friction,
            float bounce,
            float twangMaxForce,
            float dragRange,
            float dragOffset,
            int luck)
        {
            MaxHealth = maxHealth;
            LeftExtent = leftExtent;
            RightExtent = rightExtent;
            TopExtent = topExtent;
            BottomExtent = bottomExtent;
            Weight = weight;
            Friction = friction;
            Bounce = bounce;
            TwangMaxForce = twangMaxForce;
            DragRange = dragRange;
            DragOffset = dragOffset;
            Luck = luck;
        }
    }

    /// <summary>
    /// 海盗属性与种类名录的纯 C# 静态目录（真值来源）。
    ///
    /// 【出处】静态逆向文档：
    ///   §4.1 角色属性（health/extents/weight/friction/bounce/twangMaxForce/dragRange/dragOffset/luck）
    ///   §4.2 海盗种类导出符号
    ///   §4.3 队伍归属硬编码规则与坐标换算
    ///   §3.2 每回合开始时的保底武器规则
    ///
    /// 【架构】不引用任何 UnityEngine 类型，可在无头验证台直接断言。
    /// </summary>
    public static class CrewCatalog
    {
        // ------------------------------------------------------------------
        // §4.1 全属性（所有海盗共享同一份）
        // ------------------------------------------------------------------

        /// <summary>初始/最大生命（§4.1：health / maxHealth / shownHealth = 100/100/100）。</summary>
        public const int MaxHealth = 100;

        /// <summary>AABB 半宽（§4.1：leftExtent = 6）。</summary>
        public const float LeftExtent = 6f;

        /// <summary>AABB 半宽（§4.1：rightExtent = 6）。</summary>
        public const float RightExtent = 6f;

        /// <summary>AABB 半高（§4.1：topExtent = 8）。</summary>
        public const float TopExtent = 8f;

        /// <summary>AABB 半高（§4.1：bottomExtent = 8；坐标换算 y 会用到）。</summary>
        public const float BottomExtent = 8f;

        /// <summary>重量（§4.1：weight = 1，继承自 Solid；击退不乘体重）。</summary>
        public const float Weight = 1f;

        /// <summary>摩擦力（§4.1：friction = 2）。</summary>
        public const float Friction = 2f;

        /// <summary>弹跳系数（§4.1：bounce = 0.2）。</summary>
        public const float Bounce = 0.2f;

        /// <summary>弹弓最大初速（§4.1：twangMaxForce = 20）。</summary>
        public const float TwangMaxForce = 20f;

        /// <summary>拖拽范围（§4.1：dragRange = 130；地雷为 180，见 WeaponCatalog）。</summary>
        public const float DragRange = 130f;

        /// <summary>拖拽偏移（§4.1：dragOffset = -100；仅范围圈显示）。</summary>
        public const float DragOffset = -100f;

        /// <summary>默认 luck（§4.1：luck = 5，可被关卡 XML 覆盖）。</summary>
        public const int DefaultLuck = 5;

        /// <summary>共享属性快照（§4.1）。</summary>
        public static readonly CrewStats SharedStats = new CrewStats(
            MaxHealth, LeftExtent, RightExtent, TopExtent, BottomExtent,
            Weight, Friction, Bounce, TwangMaxForce, DragRange, DragOffset, DefaultLuck);

        // ------------------------------------------------------------------
        // §4.3 队伍归属
        // ------------------------------------------------------------------

        /// <summary>红队（team1，玩家侧）的队伍索引。</summary>
        public const int RedTeamIndex = 0;

        /// <summary>蓝队（team2）的队伍索引；原版规则里「非 redPirate 一律为 1」。</summary>
        public const int BlueTeamIndex = 1;

        /// <summary>红队两个导出符号（§4.3 硬编码白名单）。</summary>
        public static readonly IReadOnlyList<string> RedTeamSymbols = new[]
        {
            "redPirate",
            "redPirateCaptain",
        };

        /// <summary>
        /// 按 §4.3 硬编码规则返回队伍索引：
        /// <c>teamIndex = (type == "redPirate" || type == "redPirateCaptain") ? 0 : 1</c>。
        ///
        /// 【语义】条件是「白名单命中红队，否则一律蓝队」——因此<b>未知/拼写错误的名字也返回 1（蓝队）</b>，
        /// 且 null / 空串同样返回 1。这是对原版"else 分支兜底为 team2"的忠实还原，
        /// 不在数据层做报错，避免关卡 XML 里出现未收录符号时直接导致解析中断。
        /// 比较使用 <see cref="StringComparison.Ordinal"/>（原版字符串 == 为精确匹配）。
        /// </summary>
        public static int TeamIndexOf(string typeName)
        {
            if (string.Equals(typeName, "redPirate", StringComparison.Ordinal)
                || string.Equals(typeName, "redPirateCaptain", StringComparison.Ordinal))
            {
                return RedTeamIndex;
            }

            return BlueTeamIndex;
        }

        // ------------------------------------------------------------------
        // §4.2 海盗种类（导出符号）
        // ------------------------------------------------------------------

        /// <summary>
        /// §4.2 列出的全部海盗导出符号（Captain 变体展开后共 27 个）。
        ///
        /// 【与任务书的差异】任务书写作「21 个」，但 §4.2 原文把
        /// cabinBoy(Captain) / soldier(Captain) / blindPirate(Captain) / femalePirate(Captain) /
        /// oldPirate(Captain) / rainbowBeard(Captain) / skeletonPirate(Captain) 这 7 组简写各代表 2 个符号，
        /// 展开后为 4 + 7×2 + 9 = <b>27</b> 个（关卡 XML 里 cabinBoyCaptain / soldierCaptain /
        /// skeletonPirateCaptain 等也确实作为独立导出符号出现）。本表以文档为准取 27；
        /// 这一数字与任务书的不一致已在交付报告中显式说明。
        /// </summary>
        public static readonly IReadOnlyList<string> ExportSymbols = new[]
        {
            // 红/蓝基础海盗
            "redPirate",
            "redPirateCaptain",
            "bluePirate",
            "bluePirateCaptain",
            // 7 组「基础 + Captain」简写展开
            "cabinBoy",
            "cabinBoyCaptain",
            "soldier",
            "soldierCaptain",
            "blindPirate",
            "blindPirateCaptain",
            "femalePirate",
            "femalePirateCaptain",
            "oldPirate",
            "oldPirateCaptain",
            "rainbowBeard",
            "rainbowBeardCaptain",
            "skeletonPirate",
            "skeletonPirateCaptain",
            // 原住民与动物
            "tribe",
            "tribeChief",
            "monkey",
            "crab",
            "shark",
            "squid",
            "parrot",
            // Boss
            "bossGuy",
            "bossGuyZombie",
        };

        /// <summary>导出符号数量（见 <see cref="ExportSymbols"/> 的 27 个说明）。</summary>
        public static int ExportSymbolCount => ExportSymbols.Count;

        // ------------------------------------------------------------------
        // §3.2 保底武器
        // ------------------------------------------------------------------

        /// <summary>保底武器（§3.2：每回合开始若 <c>hasWeapons.length &lt; 1</c> 则 push cannonball）。</summary>
        public static readonly WeaponId FallbackWeapon = WeaponId.Cannonball;

        /// <summary>
        /// 原版武器栈里值 10 表示"无限"（§5.5：<c>if n == 10 → infiniteWeapons</c>）。
        /// </summary>
        public const int InfiniteWeaponCount = 10;

        /// <summary>
        /// 应用保底武器规则（§3.2 <c>startTurn</c>）：
        /// 若 <paramref name="hasWeapons"/> 为空则补一件 <see cref="FallbackWeapon"/>。
        /// 返回 true 表示本次确实补发了保底武器。
        /// </summary>
        public static bool EnsureFallbackWeapon(IList<WeaponId> hasWeapons)
        {
            if (hasWeapons == null)
                throw new ArgumentNullException(nameof(hasWeapons));

            if (hasWeapons.Count < 1)
            {
                hasWeapons.Add(FallbackWeapon);
                return true;
            }

            return false;
        }
    }
}
