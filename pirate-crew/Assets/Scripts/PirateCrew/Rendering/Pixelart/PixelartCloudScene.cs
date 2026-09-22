using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **云彩关（L01 云端漫步）像素化试点**的场景名与取景口径（唯一来源）。
    ///
    /// 【这个场景在回答什么】<see cref="PixelartPilotScene"/> 那个试点用的是图元（地面 + 三级台阶 + 木箱），
    /// 它证明的是"机制对不对"；本场景换成**云彩关的真实内容**——`CloudField.prefab`（已过审的低模云场）、
    /// 落水危险虚线、以及按关卡数据摆位的 7 个船员——回答的是"这套观感用在真关卡上是什么样"。
    ///
    /// 【内容从哪来（不许自己编）】装配器 `PixelartCloudPilotSetup` 从**关卡资产**读摆位与出生表
    /// （`LevelAssetLibrary` 的 `cloud_walk` / `ShowcaseLevels.BakedPlacements`），
    /// 与主战斗场景 `RuntimeSceneArt` / `BattlePlan` 走同一份数据；本类只负责**取景**。
    ///
    /// 【构图上心为什么在 (20, 4.5, 15)】本工程的格 → 世界换算（`LevelGeometry.TileToWorld`，
    /// 1 格 = <c>TileWorldSize</c> 世界单位）把 20×15 格的竞技场心映射到世界 (20, ·, 15)；
    /// 竖直方向取 **4.5** = 主角云顶面高度（`CloudFieldSpec.HeroTopY`），也就是云场的视觉重心。
    /// **这三个数不许各自漂**：装配时会用 `LevelGeometry` 现场推一遍场心并与本常量逐值比对，
    /// 对不上就报错（见 `PixelartCloudPilotSetup.AssertFramingAgainstLevelGeometry`）。
    ///
    /// 【俯角/方位/机位距离沿用 <see cref="PixelartPilotScene"/>】那是同一份定义（30° = 规则像素阶梯）。
    /// 两个场景各写一份的后果本仓踩过：场景写 35.264°、出图脚本写 30°，"对照图"根本不是同一机位。
    /// </summary>
    public static class PixelartCloudScene
    {
        /// <summary>场景名（Build Settings 里的名字，也是装配器保存的资产名）。</summary>
        public const string SceneName = "PixelartCloud";

        /// <summary>本场景对应的关卡号（读关卡数据用；云彩关 = 1）。</summary>
        public const int LevelNumber = 1;

        /// <summary>
        /// 构图中心（世界坐标）：竞技场心 (20, ·, 15) + 云顶中位高 4.5。
        /// 数值的来路与不变式见类头；装配器会与 `LevelGeometry` 推出的场心逐值比对。
        /// </summary>
        public static readonly Vector3 Target = new Vector3(20f, 4.5f, 15f);

        // ---------------- 机位梯子（按"可见多少米高"给；换算口径与试点场景同一份）----------------

        /// <summary>
        /// 宽机位：可见 **32m** 高（1080p 下正交 size 16、可见宽 56.9m）。
        /// 【为什么是 32】整片云场在 45° 方位下投到屏上的横距 ≈ (40+30)/√2 ≈ 49.5m、
        /// 纵距 ≈ 24.7m（30° 俯角把横向压一半），32m 留出四边余量，云场不出框。
        /// </summary>
        public const float WideVisibleMeters = 32f;

        /// <summary>中机位：可见 16m 高——主角云 + 相邻几朵云 + 云上的船员群。</summary>
        public const float MidVisibleMeters = 16f;

        /// <summary>近机位：可见 7m 高——单个船员与云台边缘的细节（台柱形/描边在这档才读得出）。</summary>
        public const float CloseVisibleMeters = 7f;

        /// <summary>把"可见多少米高"换算成一个艺术像素的世界尺寸（米）。口径与试点场景同源。</summary>
        public static float WorldPerPixel(float visibleMeters)
        {
            return PixelartPilotScene.WorldPerPixel(visibleMeters);
        }
    }
}
