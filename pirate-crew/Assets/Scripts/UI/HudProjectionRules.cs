using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// HUD 在**透视相机**下的屏幕尺度换算（纯 C# 静态类，可在无头验证台断言）。
    ///
    /// 【为什么需要它】HUD 本体是 UGUI <c>ScreenSpaceOverlay</c>（<c>M2BattleSceneSetup.CreateCanvas</c>），
    /// 不随相机透视变形——这一条已满足，无需改动。但战斗里有一个**屏幕像素口径**的命中量：
    /// <see cref="PirateCrew.PirateCrew.Battle.LevelGeometry.SelectionRadiusPixels"/> = 30px
    /// （Flash §3.4 <c>minD2 = 900</c>，用于选中/悬停/拖拽拾取）。
    /// 原版是恒定缩放的 2D，30px 对每个单位等价；换成透视 FOV 60 / 距离 18
    /// （<c>docs/M2-3D空间模型对齐.md</c> §2）后，**同一个 30px 在不同距离上覆盖的单位屏幕尺寸不同**：
    /// 近处的单位屏幕上更大、同样 30px 更容易点中；远处的单位更小、更难选中、也更容易误选邻近单位。
    ///
    /// 【定位】本类只提供**可调参数化**的候选公式与默认值，**不改动**
    /// <see cref="PirateCrew.PirateCrew.Battle.AimThrowController"/>（其拾取仍走 Flash 的固定 30px，
    /// 见 <c>docs/M2-3D空间模型对齐.md</c> §3「选中拾取…不动」）。
    /// 是否切换到这个按屏幕尺寸缩放的口径属**提案/待定**，需人眼验收手感后再决定。
    ///
    /// 【公式】透视投影下，世界高度 h 距相机 d 时的屏幕像素高度：
    ///   <c>px = h × screenHeightPx / (2 × tan(fovY/2) × d)</c>
    /// 半径按「单位屏幕尺寸相对基准距离（Godot 基准距离 18）的比例」线性缩放并夹在上下限内。
    /// </summary>
    public static class HudProjectionRules
    {
        /// <summary>Flash §3.4 的固定拾取半径（px），也是缩放公式的基准半径。</summary>
        public const float ReferenceRadiusPixels = 30f;

        /// <summary>缩放基准距离 = 相机默认距离 18（`docs/M2-3D空间模型对齐.md` §2 的 Godot 基准）。</summary>
        public const float ReferenceDistance = 18f;

        /// <summary>半径下限（px，**提案/待定**）：防止远处单位小到点不中。</summary>
        public const float MinRadiusPixels = 18f;

        /// <summary>半径上限（px，**提案/待定**）：防止近处单位把拾取圈撑得过大、误选邻居。</summary>
        public const float MaxRadiusPixels = 64f;

        /// <summary>单位世界高度（= Flash 16px / 32px 每单位 = 0.5；§4.1 的 top/bottomExtent=8）。</summary>
        public const float UnitWorldHeight = CrewCatalog.TopExtent * 2f / LevelGeometry.PixelsPerUnit;

        /// <summary>
        /// 透视投影屏幕高度（px）。
        /// </summary>
        /// <param name="worldHeight">物体世界高度（&gt;0）。</param>
        /// <param name="distanceFromCamera">相机到物体的沿视线距离（&gt;0）。</param>
        /// <param name="verticalFovDegrees">相机竖直 FOV（度）。</param>
        /// <param name="screenHeightPixels">屏幕像素高（如 Screen.height）。</param>
        public static float ProjectedScreenHeightPixels(
            float worldHeight, float distanceFromCamera, float verticalFovDegrees, float screenHeightPixels)
        {
            if (worldHeight <= 0f || screenHeightPixels <= 0f)
                return 0f;
            if (distanceFromCamera <= 1e-6f || verticalFovDegrees <= 0f || verticalFovDegrees >= 180f)
                return 0f;

            float halfExtent = Mathf.Tan(verticalFovDegrees * Mathf.Deg2Rad * 0.5f) * distanceFromCamera;
            if (halfExtent <= 1e-6f)
                return 0f;

            return worldHeight * (screenHeightPixels / (2f * halfExtent));
        }

        /// <summary>
        /// 按单位屏幕尺寸比例缩放拾取半径并夹在 [min,max]。
        /// 基准单位屏幕高度为 0（理论不会发生）时退回固定基准半径。
        /// </summary>
        public static float PerspectiveSelectionRadiusPixels(
            float referenceRadiusPixels,
            float referenceUnitScreenHeightPixels,
            float actualUnitScreenHeightPixels,
            float minRadiusPixels,
            float maxRadiusPixels)
        {
            float min = Mathf.Min(minRadiusPixels, maxRadiusPixels);
            float max = Mathf.Max(minRadiusPixels, maxRadiusPixels);

            if (referenceUnitScreenHeightPixels <= 1e-4f || actualUnitScreenHeightPixels <= 0f)
                return Mathf.Clamp(referenceRadiusPixels, min, max);

            float scaled = referenceRadiusPixels
                           * (actualUnitScreenHeightPixels / referenceUnitScreenHeightPixels);
            return Mathf.Clamp(scaled, min, max);
        }

        /// <summary>
        /// 由单位世界位置与相机位置一步算出建议拾取半径（px）。
        /// 纯数学，不需要 <see cref="Camera"/> 实例（便于无头测试与不依赖场景的调用方）。
        /// </summary>
        public static float SelectionRadiusPixelsFor(
            Vector3 unitWorldPosition,
            Vector3 cameraPosition,
            float verticalFovDegrees,
            float screenHeightPixels,
            float unitWorldHeight = UnitWorldHeight,
            float referenceRadiusPixels = ReferenceRadiusPixels,
            float minRadiusPixels = MinRadiusPixels,
            float maxRadiusPixels = MaxRadiusPixels,
            float referenceDistance = ReferenceDistance)
        {
            float distance = Vector3.Distance(cameraPosition, unitWorldPosition);
            float referenceHeight = ProjectedScreenHeightPixels(
                unitWorldHeight, referenceDistance, verticalFovDegrees, screenHeightPixels);
            float actualHeight = ProjectedScreenHeightPixels(
                unitWorldHeight, distance, verticalFovDegrees, screenHeightPixels);

            return PerspectiveSelectionRadiusPixels(
                referenceRadiusPixels, referenceHeight, actualHeight, minRadiusPixels, maxRadiusPixels);
        }
    }
}
