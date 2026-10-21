using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 跨回合常驻物的 MonoBehaviour 胶水（最薄）：把所在物体登记进
    /// <see cref="PersistentObjectRegistry"/>，销毁时注销——除此之外不做任何事。
    /// 玩法（感应/殉爆/掩体/蓄力）属武器格实现，将来挂在这个物体上的其他组件负责。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersistentObjectHandle : MonoBehaviour
    {
        PersistentObjectRegistry _registry;
        int _registeredId;

        /// <summary>是否已登记（未登记的 handle 不参与注销）。</summary>
        public bool IsRegistered => _registry != null;

        /// <summary>
        /// 登记进注册表（快照当前位置与半尺寸）。重复调用 = 注销旧条目后按当前位置重新登记。
        /// </summary>
        public void Register(
            PersistentObjectRegistry registry, WeaponId weapon, Vector3 halfExtents, int teamIndex)
        {
            if (_registry != null)
                _registry.Remove(_registeredId);
            _registry = registry;
            _registeredId = registry.Register(new PersistentObjectEntry(
                0, weapon, transform.position, halfExtents, teamIndex));
        }

        void OnDestroy()
        {
            if (_registry != null)
                _registry.Remove(_registeredId);
        }
    }
}
