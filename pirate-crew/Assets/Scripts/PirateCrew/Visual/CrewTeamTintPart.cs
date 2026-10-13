using UnityEngine;

namespace PirateCrew.Visual
{
    /// <summary>
    /// 标记组件：挂在"阵营色部件"的渲染器所在 GameObject 上。
    /// <c>UnitOutlineBinder</c> 只对这些渲染器写阵营色 <c>_BaseColor</c>，
    /// 其余部件（皮肤/铁/木/骨/皮革）保留各自材质的基础色，
    /// 满足 docs/设计/美术/角色造型.md §2.1「阵营色不得污染肤色/铁/木部件」的纪律。
    /// （顶级类独占同名文件是 Unity MonoScript 的硬规则，见 ScriptAssetSerializabilityTests。）
    /// </summary>
    public sealed class CrewTeamTintPart : MonoBehaviour
    {
    }
}
