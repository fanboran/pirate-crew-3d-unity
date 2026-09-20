using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 武器栈条目：一件武器 + 数量。
    ///
    /// 【出处】静态逆向文档 §5.5 <c>Character.setWeapons(attrs)</c>。
    /// 【count 约定】<c>count == 10</c> 表示<b>无限</b>（原版 <c>if n == 10 → infiniteWeapons.push(key)</c>）；
    ///               其余正整数为有限件数；生成器 / 关卡解析按此规则展开。
    /// </summary>
    [Serializable]
    public struct WeaponStack
    {
        /// <summary>武器 id。</summary>
        public WeaponId id;

        /// <summary>件数；10 = 无限。</summary>
        public int count;

        public WeaponStack(WeaponId id, int count)
        {
            this.id = id;
            this.count = count;
        }

        /// <summary>是否为无限弹药（§5.5：count == 10）。</summary>
        public bool IsInfinite => count == CrewCatalog.InfiniteWeaponCount;
    }

    /// <summary>
    /// 海盗种类的 ScriptableObject 定义。
    ///
    /// 【出处】静态逆向文档 §4.1（共享属性）、§4.2（导出符号种类）、§4.3（队伍归属）。
    ///
    /// 【关键结论】原版所有海盗无属性差异——只有美术、luck（来自关卡 XML）和初始武器不同。
    /// 因此本资产的属性字段对每个 crewId 都是同一份 §4.1 数值；差异化信息在关卡侧
    /// （<see cref="LevelUnit"/> 覆盖 luck 与初始武器）。
    ///
    /// 【TeamIndexOf 语义】§4.3 硬编码：
    /// <c>teamIndex = (type == "redPirate" || type == "redPirateCaptain") ? 0 : 1</c>。
    /// 即白名单命中为红队（0），<b>其余全部（含 bluePirate/bluePirateCaptain/bossGuy/skeletonPirate
    /// 以及任何未知名字）一律为蓝队（1）</b>。运行时请用 <see cref="CrewCatalog.TeamIndexOf"/> 求值，
    /// 本资产的 <see cref="teamIndex"/> 字段只做存档 / 展示用快照。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/船员定义", fileName = "CrewDefinition")]
    public class CrewDefinition : ScriptableObject
    {
        [Header("标识")]
        [SerializeField, Tooltip("船员种类 id（§4.2 导出符号名，如 redPirate / cabinBoyCaptain / bossGuy）。")]
        string crewId;

        [SerializeField, Tooltip("显示名。原版无中文名，暂用导出符号名。")]
        string displayName;

        [Header("属性（§4.1，所有海盗共享同一份）")]
        [SerializeField, Tooltip("初始/最大生命。§4.1：100。")]
        int maxHealth;

        [SerializeField, Tooltip("重量。§4.1：1（继承 Solid；击退不乘体重）。")]
        float weight;

        [SerializeField, Tooltip("摩擦力。§4.1：2。")]
        float friction;

        [SerializeField, Tooltip("弹跳系数。§4.1：0.2。")]
        float bounce;

        [SerializeField, Tooltip("AI 评估的武器随机投掷次数基数。§4.1：默认 5，可被关卡 XML 覆盖。")]
        int luck;

        [Header("队伍（§4.3）")]
        [SerializeField, Tooltip("队伍索引快照：0=红队(team1)，1=蓝队(team2)。用 CrewCatalog.TeamIndexOf 求值。")]
        int teamIndex;

        [Header("初始武器（§5.5）")]
        [SerializeField, Tooltip("初始武器栈；count=10 表示无限。关卡内每单位的实际配置见 LevelUnit.initialWeapons。")]
        List<WeaponStack> initialWeapons = new List<WeaponStack>();

        public string CrewId => crewId;
        public string DisplayName => displayName;
        public int MaxHealth => maxHealth;
        public float Weight => weight;
        public float Friction => friction;
        public float Bounce => bounce;
        public int Luck => luck;
        public int TeamIndex => teamIndex;
        public IReadOnlyList<WeaponStack> InitialWeapons => initialWeapons;

        /// <summary>
        /// 用共享属性 + 种类信息覆盖本资产（生成器幂等覆盖用）。
        /// 数值来自 <see cref="CrewCatalog.SharedStats"/>，不在此硬编码第二份。
        /// </summary>
        public void Apply(string symbol, int team, CrewStats stats, List<WeaponStack> weapons)
        {
            crewId = symbol;
            displayName = symbol;
            teamIndex = team;
            maxHealth = stats.MaxHealth;
            weight = stats.Weight;
            friction = stats.Friction;
            bounce = stats.Bounce;
            luck = stats.Luck;
            initialWeapons = weapons ?? new List<WeaponStack>();
        }
    }
}
