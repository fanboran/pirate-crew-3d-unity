using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 单位脚底「接触阴影面片」标记组件（美术风格指南 §4.1 `:239` 的落地载体，判据 A-6 `:536`）。
    ///
    /// 【它解决什么】主光仰角 48°（Art Bible §4.1 `:234`）下，单位自投影又短又基本被躯干自身挡住，
    /// 脚底与地面几乎同亮（r3 实测脚底 lum 182.9 vs 周围 180.8，差 -1.1%，判据要求 ≥8%）→ 单位"浮"在沙上。
    /// 对策按 §4.1 的提案：脚底贴一张直径 0.6 单位的半透明径向渐变面片（中心深、边缘全透），
    /// 由编辑器脚本 <c>CrewVisualPrefabBuilder</c> 程序化生成贴图与材质后挂到**单位根**下，名为 <c>ContactShadow</c>。
    ///
    /// 【为什么是纯预制体子物体、零运行时代码】
    ///   面片是单位根的子物体，位置/朝向/缩放都在预制体里烘好，单位的位移自动带走它，无需每帧同步。
    ///   贴地高度也不需要每帧对齐地面：出生逻辑把单位摆在平台顶面上，于是**单位根的 local y = -0.5
    ///   恰好就是脚底平面**（推导：脚底 world y = rootY + rootScale.y × (Visual.localY + …) = rootY − 0.25，
    ///   而 rootScale.y = 0.5，故 root 局部 -0.5 即脚底）。落水/掉平台已有独立死亡表现（下沉+摇晃+淡出），
    ///   面片残留一帧无关观感，不值得为它引入每帧射线/地形查询。
    ///
    /// 【为什么必须让 UnitOutlineBinder 排除它】它是贴地的半透明面片，不是角色部件：
    ///   ① inverted-hull 描边在平面上会画出一圈**方形**轮廓，违反 R-9「薄配件描边完整无缺边」的意图；
    ///   ② 阵营色 MPB 会把阴影染成队伍色（红队红影、蓝队蓝影），比没阴影更假。
    ///   故 <c>UnitOutlineBinder.CollectRenderers</c> 显式跳过带本组件的 renderer。
    ///
    /// 【参数口径】数值与 docs/美术风格指南.md §4.1 的提案一致；标【提案】。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContactShadowDecal : MonoBehaviour
    {
        /// <summary>提案直径：0.6 世界单位（约 1.2 倍单位脚间距，能同时罩住两只靴子）。</summary>
        public const float DefaultDiameter = 0.6f;

        /// <summary>提案贴地抬高：0.02 世界单位（避免与地面 z-fighting，肉眼仍视为贴地）。</summary>
        public const float DefaultGroundOffset = 0.02f;

        /// <summary>提案中心不透明度：0.45（中心压暗 45%，远高于 A-6 的 8% 门槛，边缘渐隐不产生硬边）。</summary>
        public const float DefaultCenterAlpha = 0.45f;

        [Tooltip("面片直径（世界单位）。真值在预制体的 Transform.localScale 里，此字段只作参数留档/后续工具读取。")]
        [SerializeField] float diameter = DefaultDiameter;

        [Tooltip("面片中心离脚底平面的抬高（世界单位）。真值在预制体的 Transform.localPosition 里。")]
        [SerializeField] float groundOffset = DefaultGroundOffset;

        [Tooltip("面片中心不透明度。真值在 CrewContactShadow.mat 的 _BaseColor.a 里。")]
        [SerializeField] float centerAlpha = DefaultCenterAlpha;

        /// <summary>提案直径（世界单位）。</summary>
        public float Diameter => diameter;

        /// <summary>提案贴地抬高（世界单位）。</summary>
        public float GroundOffset => groundOffset;

        /// <summary>提案中心不透明度。</summary>
        public float CenterAlpha => centerAlpha;
    }
}
