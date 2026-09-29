using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 潮汐巨浪（tidalWave）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【口径】起扫点、横扫速度、每帧伤害、±150px 判定圈、垂直有效范围均为本工程设计值
    ///   （<b>【提案/待定】</b>：当前无已裁决文档为其取值背书）。
    ///
    /// 【坐标口径与 3D 映射决策（提案/待定）】原文是 2D 侧视：x 横向、y 竖直、water.y 为水面。
    ///   映射到 3D（见 docs/3D空间模型对齐.md §1）：
    ///     · 横扫轴 x → 世界 **X**（浪沿 X 推进）；
    ///     · 高度 y → 世界 **Y**，`waterY` → `LevelGeometry.WaterSurfaceY`（世界水位 -0.4）；
    ///     · 世界 **Z（纵深）被折叠**——浪是横跨整个纵深的水墙，同一 X 上任意 Z 的玩家都被扫到
    ///       （"对所有角色一视同仁"口径）。
    ///   因此 `±150px` 在 3D 里实现为 **X 向距离 + Y 向距离** 的平方和判定，不含 Z。
    ///   若评审要求把 Z 也计入（球形判定），改 <see cref="ShouldDamage"/> 一处即可。
    /// </summary>
    public static class TidalWaveRules
    {
        /// <summary>起扫 x（Flash px，地图左侧外）。</summary>
        public const float SpawnFlashX = -550f;

        /// <summary>横扫速度（Flash px/帧）。</summary>
        public const float SweepSpeed = 20f;

        /// <summary>每帧伤害（非爆炸、无衰减）。</summary>
        public const float DamagePerFrame = 5f;

        /// <summary>伤害判定的距离阈值（Flash px）。</summary>
        public const float HitRadius = 150f;

        /// <summary>垂直有效范围（Flash px）：目标 y 须 ≥ waterY-300。</summary>
        public const float VerticalReach = 300f;

        /// <summary>横扫一帧后的 x。</summary>
        public static float StepX(float flashX)
        {
            return flashX + SweepSpeed;
        }

        /// <summary>横扫若干帧后的 x。</summary>
        public static float StepX(float flashX, int frames)
        {
            return flashX + SweepSpeed * frames;
        }

        /// <summary>
        /// 是否落在浪的 ±150px 判定圈内。传入 dx = 浪与目标的 **X** 差、dy = **Flash 平面 y 差**
        /// （向下为正；调用方 <see cref="ShouldDamage"/> 传 targetFlashY − waveFlashY）。
        /// Z 折叠，见类头。
        /// </summary>
        public static bool IsWithinBlast(float dx, float dy)
        {
            return dx * dx + dy * dy <= HitRadius * HitRadius;
        }

        /// <summary>是否满足垂直门槛：目标不低于水面下方 300px（Flash y 向下为正）。</summary>
        public static bool IsAboveVerticalReach(float targetFlashY, float waterFlashY)
        {
            return targetFlashY >= waterFlashY - VerticalReach;
        }

        /// <summary>
        /// 本帧是否应伤害目标：同时满足「±150px 内」与「y ≥ waterY-300」。
        /// </summary>
        public static bool ShouldDamage(
            float waveFlashX, float waveFlashY,
            float targetFlashX, float targetFlashY,
            float waterFlashY)
        {
            return IsWithinBlast(targetFlashX - waveFlashX, targetFlashY - waveFlashY)
                   && IsAboveVerticalReach(targetFlashY, waterFlashY);
        }

        /// <summary>
        /// 是否已扫出右边界。取地图右边界 <c>levelWidthTiles*32</c> 为界
        /// （<b>【提案/待定】</b>，若需余量改此一处）。
        /// </summary>
        public static bool IsPastRightEdge(float flashX, float levelWidthTiles)
        {
            return flashX > levelWidthTiles * 32f;
        }
    }
}
