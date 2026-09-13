using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Fx
{
    /// <summary>
    /// 投掷物拖尾：给飞行中的弹体挂一条**发光尾迹**（TrailRenderer）。
    ///
    /// 【与预览线的区分（用户明确要求）】
    ///   · 预览线（`Battle/TrajectoryPreview.cs`，线宽 0.06、单色、无发光）回答"**将要**飞去哪"；
    ///   · 本拖尾回答"**已经**飞到哪"——暖橙色（爆炸类）/ 选中青（其它）、带宽度收细与
    ///     透明度衰减、用加法发光贴图，与预览线在颜色与质感上都不同，不会混淆。
    ///
    /// 【挂载方式】<see cref="Attach"/> 由 <see cref="FxRoot"/> 在发现新弹体时调用
    /// （弹体列表来自 `BattleController.AllProjectiles`，见 FxRoot 的说明）。拖尾组件挂在**弹体自身**
    /// 的 GameObject 上，弹体销毁时拖尾一并消失——不需要额外的生命周期管理。
    ///
    /// 【为什么没有"弹体生成"事件】事件契约里只有引爆/蜂鸣，没有生成。这是本 agent 向协调者
    /// 报备的"缺触发信息"之一（更干净的做法是加 `battle_projectile_spawned`）。
    ///
    /// 【预算】每条拖尾 1 个 DrawCall（TrailRenderer + 共享材质），材质按"爆炸类/普通类"两档共享。
    /// 同时飞行的弹体通常 1-5 条。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileTrailFx : MonoBehaviour
    {
        TrailRenderer _trail;

        /// <summary>拖尾宽度曲线上的最大宽度（世界单位，供测试/调试读取）。</summary>
        public float Width { get; private set; }

        /// <summary>给弹体挂上拖尾（已挂过则直接返回，幂等）。</summary>
        public static ProjectileTrailFx Attach(GameObject projectile, WeaponId weapon)
        {
            if (projectile == null || !Application.isPlaying)
                return null;

            ProjectileTrailFx existing = projectile.GetComponent<ProjectileTrailFx>();
            if (existing != null)
                return existing;

            ProjectileTrailFx fx = projectile.AddComponent<ProjectileTrailFx>();
            fx.Build(weapon);
            return fx;
        }

        void Build(WeaponId weapon)
        {
            // 弹体可能自己带一个 TrailRenderer（外部 Prefab）；有就直接用，不重复加。
            _trail = GetComponent<TrailRenderer>();
            if (_trail == null)
                _trail = gameObject.AddComponent<TrailRenderer>();

            Width = FxRules.TrailWidth(weapon);

            _trail.time = FxRules.TrailTime;
            _trail.minVertexDistance = FxRules.TrailMinVertexDistance;
            _trail.numCapVertices = 2;
            _trail.numCornerVertices = 2;
            _trail.autodestruct = false;
            _trail.emitting = true;
            _trail.generateLightingData = false;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.alignment = LineAlignment.View;
            // Stretch：把软圆贴图沿轨迹拉长成一条光带（比 Tiling 更像高速尾迹）。
            _trail.textureMode = LineTextureMode.Stretch;

            // 宽度：尾端细 → 头部最宽（TrailRenderer 的 time=0 是最旧的点）。
            _trail.widthMultiplier = 1f;
            _trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, Width * 0.05f),
                new Keyframe(0.65f, Width),
                new Keyframe(1f, Width * 0.35f));

            // 顶点色只做 alpha 渐隐（色相交给共享材质的 _Color，避免二次相乘）。
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.75f), new GradientAlphaKey(0.95f, 1f) });
            _trail.colorGradient = gradient;

            // 材质按"爆炸类/普通类"共享，避免每条拖尾各建一份材质（那也是各一个 DrawCall 的开销）。
            bool explosive = IsExplosive(weapon);
            _trail.sharedMaterial = FxMaterials.Get(
                explosive ? FxMaterial.TrailExplosive : FxMaterial.TrailDefault);
            if (_trail.sharedMaterial == null)
                _trail.emitting = false;
        }

        /// <summary>该武器是否带爆炸（决定拖尾配色档）。</summary>
        static bool IsExplosive(WeaponId weapon)
        {
            return WeaponCatalog.Get(weapon).HasExplosion;
        }
    }
}
