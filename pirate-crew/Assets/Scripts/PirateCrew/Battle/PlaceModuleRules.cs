using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 放置模组的参数状态（纯数据）：放置点（世界坐标）。
    /// </summary>
    public struct PlaceParams
    {
        /// <summary>放置点世界坐标（骨架期 = 单位前方占位点；逐格定案后由瞄准交互推进）。</summary>
        public Vector3 TargetPosition;

        public PlaceParams(Vector3 targetPosition)
        {
            TargetPosition = targetPosition;
        }
    }

    /// <summary>
    /// 放置模组的纯规则骨架（无头可测）。
    ///
    /// 【骨架边界】只定参数结构与初始占位点；瞄准交互（点选地面/吸附可放置面/
    /// 重叠校验）与逐件放置计数（WeaponStats.PlaceableCount）是火药桶/木箱格的
    /// 逐格定案项，TODO 留桩，不在地基实现。
    /// 常数为骨架期占位工程值（直接固定常数口径，逐格定案时随规格重立）。
    /// </summary>
    public static class PlaceModuleRules
    {
        /// <summary>占位落点离单位的水平距离（m）：Initial 的「单位前方」口径。</summary>
        public const float PlaceholderSpawnDistance = 2f;

        /// <summary>调度链路验证用占位立方的边长（m）。</summary>
        public const float PlaceholderCubeSize = 0.5f;

        /// <summary>
        /// 进入操作时的初始放置点：单位脚底前方 <see cref="PlaceholderSpawnDistance"/>
        /// （沿 yaw 方向，yaw 约定与标准投掷相同：0 = +Z、90 = +X）。
        /// TODO(逐格定案)：初始点/瞄准手势/可放置面判定。
        /// </summary>
        public static PlaceParams Initial(Vector3 unitPivotPosition, float yawDegrees)
        {
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            return new PlaceParams(unitPivotPosition + forward * PlaceholderSpawnDistance);
        }
    }
}
