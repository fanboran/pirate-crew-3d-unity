using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 潮汐巨浪（tidalWave）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【出处】静态逆向文档（docs/参考游戏逆向-海盗军团抢宝藏-静态.md）：
    ///   · §5.2「武器总表」tidalWave 行（表格第 14 行）——
    ///     「点击引爆：x=-550, y=water.y, vx=20 横扫到最右」；「每帧 5 点（±150px 内、且 y &gt;= waterY-300）」；
    ///     「无重力、hitsTiles=false；对所有角色一视同仁」。
    ///   · §3「重要结论」——TidalWave 伤害对满血/残血一视同仁（无属性差异）。
    ///   · §6.3 tidalWave 评分——`aiPerform` 直接 `startWave()`（浪从左侧自动扫）。
    ///
    /// 【坐标口径与 3D 映射决策（提案/待定）】原文是 2D 侧视：x 横向、y 竖直、water.y 为水面。
    ///   映射到 3D（见 docs/M2-3D空间模型对齐.md §1）：
    ///     · 横扫轴 x → 世界 **X**（浪沿 X 推进）；
    ///     · 高度 y → 世界 **Y**，`waterY` → `LevelGeometry.WaterSurfaceY`（世界水位 -0.2）；
    ///     · 世界 **Z（纵深）被折叠**——浪是横跨整个纵深的水墙，同一 X 上任意 Z 的玩家都被扫到
    ///       （§5.2「对所有角色一视同仁」与 §8.4「across the bottom of the stage… affect all players it hits」支持这一点）。
    ///   因此 `±150px` 在 3D 里实现为 **X 向距离 + Y 向距离** 的平方和判定，不含 Z。
    ///   若评审要求把 Z 也计入（球形判定），改 <see cref="ShouldDamage"/> 一处即可。
    /// </summary>
    public static class TidalWaveRules
    {
        /// <summary>起扫 x（Flash px，地图左侧外）。§5.2 tidalWave 行。</summary>
        public const float SpawnFlashX = -550f;

        /// <summary>横扫速度（Flash px/帧）。§5.2「vx=20 横扫到最右」。</summary>
        public const float SweepSpeed = 20f;

        /// <summary>每帧伤害（非爆炸、无衰减）。§5.2「每帧 5 点」。</summary>
        public const float DamagePerFrame = 5f;

        /// <summary>伤害判定的距离阈值（Flash px）。§5.2「±150px 内」。</summary>
        public const float HitRadius = 150f;

        /// <summary>垂直有效范围（Flash px）：目标 y 须 ≥ waterY-300。§5.2。</summary>
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
        /// 是否落在浪的 ±150px 判定圈内。3D 口径：传入 dx = 浪与目标的 **X** 差、dy = **世界 Y** 差
        /// （Z 折叠，见类头）。
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
        /// 本帧是否应伤害目标：同时满足「±150px 内」与「y ≥ waterY-300」（§5.2 两个条件）。
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
        /// 是否已扫出右边界。§5.2 只给「横扫到最右」，未给数值余量——
        /// 取地图右边界 <c>levelWidthTiles*32</c> 为界（**提案/待定**，若需余量改此一处）。
        /// </summary>
        public static bool IsPastRightEdge(float flashX, float levelWidthTiles)
        {
            return flashX > levelWidthTiles * 32f;
        }
    }
}
