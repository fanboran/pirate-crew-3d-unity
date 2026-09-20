using System.Collections.Generic;
using PirateCrew.Data;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 角色的武器背包（纯 C#，不引用 MonoBehaviour）。
    ///
    /// 【对应章节】§5.5（<c>Character.setWeapons</c>：count == 10 表示无限）、
    ///             §3.2（每回合开始若空则补 cannonball）、
    ///             §5.2（piecesOfEight 可重复使用 8 次）。
    ///
    /// 【数据结构】内部按"槽位"展开：有限武器 count = n 展开成 n 个槽位（消费一次移除一个），
    ///   无限武器只占 1 个槽位并标记 <see cref="Slot.Infinite"/>。
    ///   piecesOfEight 等带 <see cref="WeaponStats.MaxReuses"/> 的武器，每个槽位有独立的复用次数，
    ///   用完 8 次才移除——这与原版"爆后 <c>owner.equip(同 index)</c> 复位"的语义一致。
    ///
    /// 【索引稳定性】不消费（无限/复用未耗尽）的槽位不会被移除，因此其索引在多次 Equip 间保持不变，
    ///   可直接复用 <c>owner.equip(同 index)</c> 的复位语义。
    /// </summary>
    public sealed class WeaponInventory
    {
        /// <summary>一个武器槽位。</summary>
        sealed class Slot
        {
            public WeaponId Id;
            public bool Infinite;
            public int UsesRemaining;
        }

        readonly List<Slot> _slots = new List<Slot>();
        int _equippedIndex = -1;

        /// <summary>空背包。</summary>
        public WeaponInventory()
        {
        }

        /// <summary>按关卡初始武器栈展开背包（§5.5）。</summary>
        public WeaponInventory(IEnumerable<WeaponStack> initialStacks)
        {
            if (initialStacks == null)
                return;

            foreach (WeaponStack stack in initialStacks)
            {
                if (stack.IsInfinite)
                {
                    // count == 10：无限武器，只占一个槽位。
                    _slots.Add(new Slot { Id = stack.id, Infinite = true, UsesRemaining = int.MaxValue });
                    continue;
                }

                int count = stack.count;
                if (count <= 0)
                    continue;   // 0 / 负数：不发

                int reuses = ReuseCount(stack.id);
                for (int i = 0; i < count; i++)
                    _slots.Add(new Slot { Id = stack.id, Infinite = false, UsesRemaining = reuses });
            }
        }

        /// <summary>槽位数量（无限武器算 1）。</summary>
        public int Count => _slots.Count;

        /// <summary>是否有任意武器（§3.2 保底判定 <c>hasWeapons.length &lt; 1</c>）。</summary>
        public bool HasAny => _slots.Count > 0;

        /// <summary>当前装备的槽位索引；-1 表示未装备。</summary>
        public int EquippedIndex => _equippedIndex;

        /// <summary>是否已装备有效武器。</summary>
        public bool HasEquipped => _equippedIndex >= 0 && _equippedIndex < _slots.Count;

        /// <summary>当前装备的武器 id；未装备时返回 false。</summary>
        public bool TryGetEquipped(out WeaponId id)
        {
            if (HasEquipped)
            {
                id = _slots[_equippedIndex].Id;
                return true;
            }

            id = default;
            return false;
        }

        /// <summary>当前装备的武器 id；未装备时抛 <see cref="System.InvalidOperationException"/>。</summary>
        public WeaponId EquippedWeapon
        {
            get
            {
                if (!HasEquipped)
                    throw new System.InvalidOperationException("WeaponInventory 当前没有装备武器。");
                return _slots[_equippedIndex].Id;
            }
        }

        /// <summary>取槽位武器 id；越界返回 false。</summary>
        public bool TryGetWeaponAt(int index, out WeaponId id)
        {
            if (index < 0 || index >= _slots.Count)
            {
                id = default;
                return false;
            }

            id = _slots[index].Id;
            return true;
        }

        /// <summary>该槽位是否为无限武器（count == 10）。</summary>
        public bool IsInfiniteAt(int index)
        {
            return index >= 0 && index < _slots.Count && _slots[index].Infinite;
        }

        /// <summary>该槽位剩余可用次数；无限武器返回 <see cref="int.MaxValue"/>；越界返回 0。</summary>
        public int UsesRemainingAt(int index)
        {
            if (index < 0 || index >= _slots.Count)
                return 0;
            return _slots[index].UsesRemaining;
        }

        /// <summary>第一个该武器 id 的槽位索引；不存在返回 -1（原版 firstIndexOfWeapon）。</summary>
        public int FirstIndexOf(WeaponId id)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Id == id)
                    return i;
            }
            return -1;
        }

        /// <summary>是否拥有该武器（任意槽位）。</summary>
        public bool Contains(WeaponId id)
        {
            return FirstIndexOf(id) >= 0;
        }

        /// <summary>装备指定槽位；索引非法返回 false。</summary>
        public bool Equip(int index)
        {
            if (index < 0 || index >= _slots.Count)
                return false;
            _equippedIndex = index;
            return true;
        }

        /// <summary>装备第一个该武器；不存在返回 false（对应 <c>owner.equip(firstIndexOfWeapon(w))</c>）。</summary>
        public bool EquipFirst(WeaponId id)
        {
            return Equip(FirstIndexOf(id));
        }

        /// <summary>卸下当前武器（原版 <c>unequip()</c>）。</summary>
        public void Unequip()
        {
            _equippedIndex = -1;
        }

        /// <summary>
        /// 保底武器（§3.2 <c>startTurn</c>）：背包为空时补一件 <see cref="CrewCatalog.FallbackWeapon"/>（cannonball）。
        /// 返回 true 表示本次确实补发。补发的 cannonball 是<b>有限</b>的（原版 push 一个字符串，用掉即消耗）。
        /// </summary>
        public bool EnsureFallbackWeapon()
        {
            if (_slots.Count >= 1)
                return false;

            _slots.Add(new Slot
            {
                Id = CrewCatalog.FallbackWeapon,
                Infinite = false,
                UsesRemaining = ReuseCount(CrewCatalog.FallbackWeapon),
            });
            return true;
        }

        /// <summary>消费一个槽位的一次使用。返回 true 表示该槽位已被移除（有限且耗尽）。</summary>
        public bool ConsumeAt(int index)
        {
            if (index < 0 || index >= _slots.Count)
                return false;

            Slot slot = _slots[index];
            if (slot.Infinite)
                return false;   // 无限武器不消耗

            slot.UsesRemaining--;
            if (slot.UsesRemaining > 0)
                return false;   // piecesOfEight 等还剩复用次数：槽位保留（即"复位"）

            _slots.RemoveAt(index);
            if (_equippedIndex == index)
                _equippedIndex = -1;
            else if (_equippedIndex > index)
                _equippedIndex--;   // 移除后修正装备索引

            return true;
        }

        /// <summary>消费当前装备武器的一次使用（§3.2 finishTurn → weaponExpired）。返回是否移除槽位。</summary>
        public bool ConsumeEquipped()
        {
            if (!HasEquipped)
                return false;
            return ConsumeAt(_equippedIndex);
        }

        /// <summary>当前背包的武器 id 列表快照（仅供 UI/调试，顺序与槽位一致）。</summary>
        public IReadOnlyList<WeaponId> ToWeaponIdList()
        {
            var list = new List<WeaponId>(_slots.Count);
            for (int i = 0; i < _slots.Count; i++)
                list.Add(_slots[i].Id);
            return list;
        }

        /// <summary>取武器的复用次数：0 视为 1 次（普通武器用掉即没）。</summary>
        static int ReuseCount(WeaponId id)
        {
            if (!WeaponCatalog.TryGet(id, out WeaponStats stats))
                return 1;

            return stats.MaxReuses > 0 ? stats.MaxReuses : 1;
        }
    }
}
