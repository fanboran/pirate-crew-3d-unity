using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 水面高度采样器（纯 C# 静态类，无 GameObject/引擎运行时依赖，可无头测）：
    /// 在任意世界 XZ 处合成与 <c>PirateOcean.shader</c> 顶点位移**同口径**的水面高度，
    /// 供漂浮道具（<see cref="FloatingPropView"/>）做纯表现的视觉起伏。
    ///
    /// 【同步纪律（改波参必须三处同源，缺一就是"采样器与画面不同步"）】
    /// 波参数以 <see cref="OceanRules.DefaultSwellWaves"/>（长涌 S1/S2）与
    /// <see cref="WaterRules.DefaultWaves"/>（chop W1-W4）为单一事实源——shader 的
    /// <c>_S1…_W4</c> Properties 默认值与其一一对应；材质实例（Ocean_Water.mat /
    /// OceanRig 运行时材质）若覆写波参，必须与这里保持一致。本采样器只引用常量，
    /// 不抄任何数值副本。
    ///
    /// 【与 shader 的公式对照（行号以工作树版 PirateOcean.shader 为准）】
    /// · 单波相位/高度：<c>θ = k·dot(D,xz) − speed·√(9.81·k)·t</c>，<c>h += amp·sinθ</c>
    ///   —— shader PirateAddWave（428-451 行，高度项在 447 行）；C# 侧即
    ///   <see cref="WaterRules.Phase"/>（WaterRules.cs:92-96，深水色散 ω 在
    ///   WaterWave.AngularFrequency，WaterRules.cs:44-45）。
    /// · 长涌振幅 × 包络：smoothstep(保护半径, 全涌半径, 距竞技场中心)
    ///   —— shader OceanResolveFades（480-483 行）＝ <see cref="OceanRules.SwellEnvelope"/>
    ///   （OceanRules.cs:140-143）；半径来自 <see cref="OceanRules.ProtectRadius"/>/
    ///   <see cref="OceanRules.FullSwellRadius"/>，与 OceanRig.PublishGlobals 发布的
    ///   <c>_OceanArenaCenter.zw</c> 同源。
    /// · 全部波 × 几何淡出：<see cref="OceanRules.WaveGeometricFade"/>（OceanRules.cs:160-164）
    ///   ＝ shader OceanGeometricFade（456-460 行）；环宽 <see cref="OceanGridRules.RingWidthAtRadius"/>
    ///   （OceanGridRules.cs:110-119）＝ shader OceanLocalCellSize（463-471 行），圆心是**网格中心**。
    /// · 振幅合成顺序：shader 先乘衰减再加波（OceanGerstner 496-508 行：_S1Amp·gS1 等），
    ///   本类同序（amp·fade·sin），浮点口径一致。
    ///
    /// 【刻意不实现：位移远场淡出（DisplaceFadeStart/End = 2000-3000，距相机）】
    /// 漂浮道具全部位于近场（竞技场内及视野圈），距相机远小于 2000u，该淡出恒为 1；
    /// 采样器也不持有相机引用（保持纯函数、无逐帧相机查询）。
    /// 【刻意不实现：水平位移（Gerstner Δx/Δz）】漂浮起伏只需要 Y；水平漂移会让道具
    /// 离开玩法层钉死的 XZ（落点/寻路格子），违背"纯表现、零玩法影响"的批次契约。
    /// </summary>
    public static class WaterSurfaceSampler
    {
        /// <summary>
        /// 世界 XZ 处的水面高度偏移（相对静水面 <see cref="LevelGeometry.WaterSurfaceY"/> 的 Y 偏移）。
        /// 任务书签名：竞技场中心以 <paramref name="gridCenter"/> 近似（两者都在图心附近时误差可忽略）；
        /// 网格中心随相机步进、竞技场中心固定的精确口径用
        /// <see cref="HeightAt(Vector2,Vector2,Vector2,float,float)"/> 重载。
        /// </summary>
        public static float HeightAt(Vector2 xz, Vector2 gridCenter, float arenaRadius, float time)
        {
            return HeightAt(xz, gridCenter, gridCenter, arenaRadius, time);
        }

        /// <summary>
        /// 精确口径：包络圆心（竞技场中心）与环宽圆心（网格中心）分开传，
        /// 与 shader 的 <c>_OceanArenaCenter</c> / <c>_OceanGridCenter</c> 一一对应。
        /// </summary>
        public static float HeightAt(Vector2 xz, Vector2 gridCenter, Vector2 arenaCenter,
            float arenaRadius, float time)
        {
            return HeightFromWaves(OceanRules.DefaultSwellWaves, WaterRules.DefaultWaves,
                xz, gridCenter, arenaCenter, arenaRadius, time);
        }

        /// <summary>
        /// 全参数核心：波表外置（长涌表 + chop 表），供测试用裁剪波表钉"单波贡献归零"契约。
        /// 衰减口径与 shader OceanGerstner 相同：长涌 ×（包络×几何淡出），chop ×几何淡出；
        /// 几何淡出的环宽取采样点**距网格中心**处（<see cref="OceanGridRules.RingWidthAtRadius"/>）。
        /// </summary>
        public static float HeightFromWaves(WaterWave[] swellWaves, WaterWave[] chopWaves,
            Vector2 xz, Vector2 gridCenter, Vector2 arenaCenter, float arenaRadius, float time)
        {
            // 长涌包络（近岸 0 → 外海 1）：半径规则与 OceanRig.PublishGlobals 发布值同源。
            float dArena = Vector2.Distance(xz, arenaCenter);
            float envelope = OceanRules.SwellEnvelope(
                dArena,
                OceanRules.ProtectRadius(arenaRadius),
                OceanRules.FullSwellRadius(arenaRadius));

            // 几何解析淡出：网格在该点半径处的环宽决定"多短的波还能有几何位移"。
            float cell = OceanGridRules.RingWidthAtRadius(Vector2.Distance(xz, gridCenter));

            float h = 0f;
            for (int i = 0; i < swellWaves.Length; i++)
            {
                WaterWave wave = swellWaves[i];
                float fade = envelope * OceanRules.WaveGeometricFade(wave.Wavelength, cell);
                h += SingleWaveHeight(wave, xz, time, fade);
            }
            for (int i = 0; i < chopWaves.Length; i++)
            {
                WaterWave wave = chopWaves[i];
                float fade = OceanRules.WaveGeometricFade(wave.Wavelength, cell);
                h += SingleWaveHeight(wave, xz, time, fade);
            }
            return h;
        }

        /// <summary>
        /// 单波垂直位移 <c>amp·fade·sin(θ)</c>：与 shader PirateAddWave 的高度项
        /// （PirateOcean.shader:447）同式；相位走 <see cref="WaterRules.Phase"/>
        /// （深水色散 ω = speed·√(9.81·k)，WaterRules.cs:44-45 与 shader:437-438 同式）。
        /// </summary>
        static float SingleWaveHeight(in WaterWave wave, Vector2 xz, float time, float amplitudeScale)
        {
            return wave.Amplitude * amplitudeScale * Mathf.Sin(WaterRules.Phase(wave, xz.x, xz.y, time));
        }
    }
}
