using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 一件跨回合常驻物的登记条目（纯数据快照，不引用 MonoBehaviour——核心注册表可无头测试）。
    /// 将来宿主：地雷 / 火药桶 / 木箱 / 加农炮（武器系统重做 §2 放置类与跨回合常驻档）。
    /// </summary>
    public readonly struct PersistentObjectEntry
    {
        /// <summary>注册表分配的句柄 id（登记后不变，供移除/查询）。</summary>
        public readonly int Id;

        /// <summary>武器 id（辨识哪格武器产出的常驻物）。</summary>
        public readonly WeaponId Weapon;

        /// <summary>登记时的世界位置快照（常驻物默认静置；动态同步需求属逐格定案）。</summary>
        public readonly Vector3 Position;

        /// <summary>碰撞盒半尺寸（世界单位；爆炸波及按 AABB 最近点距离判定的几何输入）。</summary>
        public readonly Vector3 HalfExtents;

        /// <summary>归属队伍索引（-1 = 无归属；殉爆的施暴者归属语义属逐格定案）。</summary>
        public readonly int TeamIndex;

        public PersistentObjectEntry(int id, WeaponId weapon, Vector3 position, Vector3 halfExtents, int teamIndex)
        {
            Id = id;
            Weapon = weapon;
            Position = position;
            HalfExtents = halfExtents;
            TeamIndex = teamIndex;
        }
    }

    /// <summary>
    /// 跨回合常驻物容器（纯 C# 注册表，武器系统地基）。
    ///
    /// 【职责边界】只做登记/查询/爆炸波及询问，<b>不做任何玩法</b>：
    ///   · 生成 = <see cref="Register"/>（调用方负责实例化场景物体）；
    ///   · 查询 = <see cref="TryGet"/> / <see cref="All"/>；
    ///   · 爆炸波及询问 = <see cref="CollectBlastAffected"/>（按 AABB 最近点距离判定，
    ///     对齐武器系统 §3.1「以最近点距离判定波及」；比现役弹体连锁的球心
    ///     OverlapSphere 更贴文档口径，供逐格定案时接线）；
    ///   · <b>回合边界不销毁</b>：本类没有任何回合清理入口——跨回合存活是容器的存在意义，
    ///     离场只有显式 <see cref="Remove"/>（常驻物自爆/被破坏）与整场 <see cref="Clear"/>。
    ///
    /// 【与现役弹体列表的分工】BattleController._projectiles 是弹体域（MonoBehaviour 引用、
    /// 回合结束按寿命清理）；本容器是放置物域（纯数据、跨回合）。骨架期两者并存、互不接管，
    /// 将来常驻武器格（mine/gunpowderBarrel/woodenCrate/cannon）在放置时登记进来。
    ///
    /// 【胶水】MonoBehaviour 侧见 <see cref="PersistentObjectHandle"/>（登记/注销自身，
    /// 最薄）；注册表实例由 BattleController 持有。
    /// </summary>
    public sealed class PersistentObjectRegistry
    {
        readonly List<PersistentObjectEntry> _entries = new List<PersistentObjectEntry>();
        int _nextId = 1;

        /// <summary>已登记常驻物数。</summary>
        public int Count => _entries.Count;

        /// <summary>全部条目（只读，登记序）。</summary>
        public IReadOnlyList<PersistentObjectEntry> All => _entries;

        /// <summary>
        /// 登记一件常驻物（生成）。返回分配的句柄 id。
        /// </summary>
        public int Register(PersistentObjectEntry entry)
        {
            // 调用方不持有 id 写入权（entry.Id 由注册表重写为分配值），句柄始终自洽。
            int id = _nextId++;
            _entries.Add(new PersistentObjectEntry(
                id, entry.Weapon, entry.Position, entry.HalfExtents, entry.TeamIndex));
            return id;
        }

        /// <summary>按句柄查询。</summary>
        public bool TryGet(int id, out PersistentObjectEntry entry)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == id)
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// 显式移除（常驻物自爆/被破坏时调用）。
        /// <b>没有</b>回合边界清理入口——需要清场只用 <see cref="Clear"/>（整场重置）。
        /// </summary>
        public bool Remove(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id != id)
                    continue;
                _entries.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>清空全部条目（整场重置用；回合推进不调用）。</summary>
        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>
        /// 爆炸波及询问：爆心球（世界坐标）波及到的常驻物写入 results（不改变登记）。
        /// 判定 = 常驻物 AABB 到爆心的<b>最近点距离</b> &lt;= radius（贴边命中），
        /// 与「中心点距离」的差别在 AABB 大于球时显著（木箱贴爆心必波及）。
        /// </summary>
        public void CollectBlastAffected(
            Vector3 blastCenter, float radius, List<PersistentObjectEntry> results)
        {
            if (results == null)
                return;

            results.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                PersistentObjectEntry entry = _entries[i];
                Vector3 closest = new Vector3(
                    Mathf.Clamp(blastCenter.x, entry.Position.x - entry.HalfExtents.x, entry.Position.x + entry.HalfExtents.x),
                    Mathf.Clamp(blastCenter.y, entry.Position.y - entry.HalfExtents.y, entry.Position.y + entry.HalfExtents.y),
                    Mathf.Clamp(blastCenter.z, entry.Position.z - entry.HalfExtents.z, entry.Position.z + entry.HalfExtents.z));
                if ((closest - blastCenter).sqrMagnitude <= radius * radius)
                    results.Add(entry);
            }
        }
    }
}
