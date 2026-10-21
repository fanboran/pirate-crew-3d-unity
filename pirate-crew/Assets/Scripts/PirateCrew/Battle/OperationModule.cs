using System.Collections.Generic;
using PirateCrew.Data;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 操作模组种类（操作与交互 §6 模组体系；交互层不为单件武器改状态机，
    /// 武器格声明用哪个模组接入）。
    /// </summary>
    public enum OperationModuleKind
    {
        /// <summary>标准投掷模组（现役唯一实装：三参数抛掷，服务跳跃与绝大多数武器格）。</summary>
        StandardThrow = 0,

        /// <summary>
        /// 放置模组（骨架槽）：摆位不抛——将来宿主：火药桶 / 木箱（§2 放置模组组）。
        /// 瞄准与逐件放置计数留 TODO 桩，骨架期只验证调度链路。
        /// </summary>
        Place = 1,

        /// <summary>
        /// 直线发射模组（骨架槽）：原作加农炮走直线——将来宿主：加农炮（§2 直线发射组）。
        /// 拖拽炮尾手势/蓄力阈值留 TODO 桩，骨架期只验证调度链路。
        /// </summary>
        LineLaunch = 2,
    }

    /// <summary>
    /// 武器格 → 操作模组的路由注册表（纯 C#，武器系统地基）。
    ///
    /// 【空表回退】现役没有任何武器格定案专用模组（17 格全部走标准投掷的单一弹体口径）——
    /// 未登记武器一律路由 <see cref="OperationModuleKind.StandardThrow"/>，与路由表引入前
    /// 的行为一致。逐格定案时在此登记（放置类 → Place、加农炮 → LineLaunch 等）。
    /// 调度消费方：<see cref="BattleInteractionController"/>（SelectWeapon 查表后按模组分派）。
    /// </summary>
    public static class OperationModuleRegistry
    {
        static readonly Dictionary<WeaponId, OperationModuleKind> _byId =
            new Dictionary<WeaponId, OperationModuleKind>();

        /// <summary>已登记的路由数（0 = 全部武器走标准投掷）。</summary>
        public static int Count => _byId.Count;

        /// <summary>登记一条武器 → 模组路由（逐格定案时调用；同 id 重复登记后写覆盖）。</summary>
        public static void Register(WeaponId id, OperationModuleKind kind)
        {
            _byId[id] = kind;
        }

        /// <summary>查路由（未登记返回 false）。</summary>
        public static bool TryGet(WeaponId id, out OperationModuleKind kind)
        {
            return _byId.TryGetValue(id, out kind);
        }

        /// <summary>武器的操作模组（未登记回退 <see cref="OperationModuleKind.StandardThrow"/>）。</summary>
        public static OperationModuleKind ModuleFor(WeaponId id)
        {
            return TryGet(id, out OperationModuleKind kind) ? kind : OperationModuleKind.StandardThrow;
        }

        /// <summary>清空全部路由（测试隔离用；运行期无调用方）。</summary>
        public static void Clear()
        {
            _byId.Clear();
        }
    }
}
