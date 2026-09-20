using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 特效模块的**公开静态出口**（对应架构原则「跨模块调用的公共出口放各模块 <c>XxxApi</c>」）。
    ///
    /// 【容错纪律】所有方法在以下情况**静默返回、不抛异常、不报错**：
    ///   · 非播放模式（编辑模式预览/无头编译）；
    ///   · FX shader 不可用（材质为 null）；
    ///   · 传 null 目标或非法数值。
    /// 特效是纯表现层，绝不能因为"特效没准备好"打断战斗逻辑。
    ///
    /// 【触发方式】正常路径由 <see cref="FxRoot"/> 订阅 EventBus 现有事件后调用；
    /// 其它模块（如武器脚本、UI）也可直接调本类，不需要新增事件。
    /// </summary>
    public static class FxApi
    {
        /// <summary>爆炸（火球/火花/碎屑/烟/冲击波）。</summary>
        public static void PlayExplosion(Vector3 center, float explosionSize)
        {
            if (!Application.isPlaying)
                return;
            ExplosionFx.Play(center, explosionSize);
        }

        /// <summary>小型尘爆（无爆炸弹体的落地/命中）。</summary>
        public static void PlayImpactPuff(Vector3 center, float scale = 1f)
        {
            if (!Application.isPlaying)
                return;
            ExplosionFx.PlayImpactPuff(center, scale);
        }

        /// <summary>入水水花（世界坐标，y 会被吸附到水面）。</summary>
        public static void PlayWaterSplash(Vector3 position, float fallSpeed)
        {
            if (!Application.isPlaying)
                return;
            WaterSplashFx.Play(position, fallSpeed);
        }

        /// <summary>落水即死的强反馈（水花 + 大涟漪）。</summary>
        public static void PlayDrownSplash(Vector3 position)
        {
            if (!Application.isPlaying)
                return;
            WaterSplashFx.PlayDrown(position);
        }

        /// <summary>命中反馈（火花 + 尘土 + 伤害数字）。</summary>
        public static void PlayHit(Vector3 worldPosition, float damage, int maxHealth)
        {
            if (!Application.isPlaying)
                return;
            HitFx.Play(worldPosition, damage, maxHealth);
        }

        /// <summary>只弹一个伤害数字。</summary>
        public static void ShowDamageNumber(Vector3 worldPosition, float damage, int maxHealth)
        {
            if (!Application.isPlaying)
                return;
            DamageNumberFx.Play(worldPosition, damage, maxHealth);
        }

        /// <summary>给弹体挂拖尾（幂等）。</summary>
        public static void AttachProjectileTrail(GameObject projectile, WeaponId weapon)
        {
            if (!Application.isPlaying)
                return;
            ProjectileTrailFx.Attach(projectile, weapon);
        }

        /// <summary>显示回合光环（队伍色）。teamNumber：1 = 红队，2 = 蓝队。</summary>
        public static void ShowTurnMarker(Transform target, int teamNumber)
        {
            if (!Application.isPlaying || target == null)
                return;
            FxRoot root = FxRoot.Ensure();
            if (root != null)
                root.ShowMarker(target, FxRules.TeamMarkerColor(teamNumber));
        }

        /// <summary>显示选中光环（选中青，与描边选中色同源）。</summary>
        public static void ShowSelectionMarker(Transform target)
        {
            if (!Application.isPlaying || target == null)
                return;
            FxRoot root = FxRoot.Ensure();
            if (root != null)
                root.ShowMarker(target, FxRules.SelectedMarkerColor());
        }

        /// <summary>隐藏回合光环。</summary>
        public static void HideTurnMarker()
        {
            if (!Application.isPlaying)
                return;
            FxRoot root = FxRoot.Ensure();
            if (root != null)
                root.HideMarker();
        }

        /// <summary>
        /// 清空全部存活特效与对象池（结算/切场景/测试收尾用）。
        /// 【注意】对象池容器是 <c>DontDestroyOnLoad</c>，本方法会把它一并销毁。
        /// </summary>
        public static void ClearAll()
        {
            if (!Application.isPlaying)
                return;
            FxPool.Clear();
            if (FxRoot.Instance != null)
                FxRoot.Instance.HideMarker();
        }
    }
}
