using PirateCrew.PirateCrew.Data;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 弹体生命周期规则（纯 C#）。
    ///
    /// 【出处】静态逆向文档 §5.2「limitedToTurn」「放置数量」「可重复使用次数」与 §3.4 行动经济。
    ///
    /// 【语义】
    ///   · <see cref="ShouldDestroyAtTurnEnd"/>：§5.2 limitedToTurn=true 的武器（发射/使用后仅存活本回合）
    ///     在回合结束时销毁；limitedToTurn=false 的跨回合常驻类（mine / gunpowderBarrel / woodenCrate / cannon）
    ///     保留至其自身引爆或被玩家用掉——这样地雷能埋伏到对手回合、箱体掩体可长期使用。
    ///   · <see cref="ReuseCount"/>：piecesOfEight 每件可用 8 次；爆后由
    ///     <see cref="WeaponInventory"/> 保留同一槽位实现「复位可再用」，用完才移除。
    ///   · <see cref="ShouldDestroyAfterDetonation"/>：引爆即从场上移除；复用是库存层语义，不是留弹体。
    /// </summary>
    public static class ProjectileLifetimeRules
    {
        /// <summary>回合结束时是否销毁该武器的场上弹体（= !LimitedToTurn）。</summary>
        public static bool ShouldDestroyAtTurnEnd(WeaponStats stats)
        {
            return stats.LimitedToTurn;
        }

        /// <summary>引爆后是否从场上移除弹体（恒 true；复用由库存槽位负责）。</summary>
        public static bool ShouldDestroyAfterDetonation(WeaponStats stats)
        {
            return true;
        }

        /// <summary>库存侧复用次数：MaxReuses &gt; 0 取其值，否则 1（用掉即没）。</summary>
        public static int ReuseCount(WeaponStats stats)
        {
            return stats.MaxReuses > 0 ? stats.MaxReuses : 1;
        }

        /// <summary>该武器是否复用型（MaxReuses &gt; 1）。</summary>
        public static bool IsReusable(WeaponStats stats)
        {
            return stats.MaxReuses > 1;
        }
    }
}
