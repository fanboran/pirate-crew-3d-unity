using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 单条方向波（Gerstner / sum-of-sines）的参数与解析量（纯 C#，无头可测）。
    ///
    /// 【为什么用 Gerstner 而不是"噪声梯度当法线"】旧水面用 FBM 高度场的有限差分求法线，
    /// 法线与顶点位移**各算各的** → 低频起伏不上法线 → "看起来像平的"。
    /// Gerstner 的位移与法线来自**同一组公式的解析导数**，几何与明暗天然一致。
    ///
    /// 【不自交条件】单条波需 <c>Q·A·k ≤ 1</c>；多条波的充分条件是
    /// <c>Σ Q_i·A_i·k_i &lt; 1</c>（见 <see cref="WaterRules.IsFoldFreeByMetric"/>）。
    /// </summary>
    public readonly struct WaterWave
    {
        /// <summary>波传播方向（世界 XZ 平面；构造时归一化）。</summary>
        public readonly Vector2 Direction;

        /// <summary>波长（世界单位）。</summary>
        public readonly float Wavelength;

        /// <summary>振幅（世界单位）。</summary>
        public readonly float Amplitude;

        /// <summary>陡度 Q ∈ [0,1]：0 = 纯正弦（只上下），1 = 接近卷曲的 Gerstner。</summary>
        public readonly float Steepness;

        /// <summary>相速倍率（1 = 深水色散速度 <c>√(g/k)</c>）。</summary>
        public readonly float SpeedScale;

        public WaterWave(Vector2 direction, float wavelength, float amplitude, float steepness, float speedScale)
        {
            Direction = direction.sqrMagnitude < 1e-8f ? new Vector2(1f, 0f) : direction.normalized;
            Wavelength = Mathf.Max(wavelength, WaterRules.MinWavelength);
            Amplitude = Mathf.Max(amplitude, 0f);
            Steepness = Mathf.Clamp01(steepness);
            SpeedScale = Mathf.Max(speedScale, 0f);
        }

        /// <summary>波数 <c>k = 2π/L</c>。</summary>
        public float WaveNumber => Mathf.PI * 2f / Wavelength;

        /// <summary>角频率 <c>ω = speedScale·√(g·k)</c>（深水色散）。</summary>
        public float AngularFrequency => SpeedScale * Mathf.Sqrt(WaterRules.Gravity * WaveNumber);

        /// <summary>水平位移因子 <c>Q·A·k</c>（用于不自交的充分条件）。</summary>
        public float HorizontalFactor => Steepness * Amplitude * WaveNumber;
    }

    /// <summary>
    /// 宏观海浪（Gerstner）与水下光斑（假焦散）的纯公式层（纯 C#，无头可测）。
    ///
    /// 【与 HLSL 的关系】<c>PirateWater.shader</c> 内嵌了**同一组公式**（HLSL 无法无头验证）。
    /// 本类是"参考实现"：改这里必须同步改 shader，反之亦然；测试断言的是这套数值契约
    /// （解析法线与数值微分一致、默认参数不自交、焦散随深度单调）。
    ///
    /// 【分工】顶点几何 = Gerstner（宏观形状）；高度场模拟（<see cref="WaterWaveField2D"/>）
    /// 只做局部法线扰动与泡沫，**不参与顶点位移**，避免双重计高。
    /// </summary>
    public static class WaterRules
    {
        /// <summary>重力加速度（m/s²，与 LevelGeometry 的物理换算无关，仅用于深水色散）。</summary>
        public const float Gravity = 9.81f;

        /// <summary>最小波长（世界单位），防止参数为 0 导致除零。</summary>
        public const float MinWavelength = 0.25f;

        /// <summary>竞技场最短允许波峰间距（世界单位）——协调者定的观感下限。</summary>
        public const float MinCrestSpacing = 2f;

        /// <summary>
        /// 默认 4 条波（与 shader 的 <c>_W1…_W4</c> Properties 默认值一一对应）。
        ///
        /// 【默认值依据（提案/待定）】竞技场 50×17 单位、相机 45° 俯视 18 单位。
        /// 波长取 13 / 7.5 / 4.2 / 2.4（最短 2.4 ≥ 2 的观感下限，否则单位站上去像"踩碎浪"）；
        /// 振幅合计 0.139 < 地面到水面的 0.2 间距 → 浪尖不会穿出地面（有测试断言）；
        /// Σ Q·A·k = 0.081 ≪ 1 → 绝不自交。波速按深水色散 → 长浪快、短浪慢，符合观感。
        /// </summary>
        public static readonly WaterWave[] DefaultWaves =
        {
            new WaterWave(new Vector2(1.00f, 0.25f), 13.0f, 0.055f, 0.65f, 1.00f),
            new WaterWave(new Vector2(0.60f, 1.00f), 7.5f, 0.040f, 0.60f, 1.15f),
            new WaterWave(new Vector2(-0.30f, 1.00f), 4.2f, 0.028f, 0.55f, 1.30f),
            new WaterWave(new Vector2(1.00f, -0.50f), 2.4f, 0.016f, 0.50f, 1.50f),
        };

        /// <summary>相位 <c>θ = k·(D·x) − ω·t</c>。</summary>
        public static float Phase(in WaterWave wave, float x, float z, float time)
        {
            return wave.WaveNumber * (wave.Direction.x * x + wave.Direction.y * z)
                 - wave.AngularFrequency * time;
        }

        /// <summary>垂直位移（只统计上下分量）<c>h = Σ A·sin θ</c>。</summary>
        public static float Height(WaterWave[] waves, float x, float z, float time)
        {
            float h = 0f;
            for (int i = 0; i < waves.Length; i++)
                h += waves[i].Amplitude * Mathf.Sin(Phase(waves[i], x, z, time));
            return h;
        }

        /// <summary>水平位移（Gerstner）<c>(Δx, Δz) = Σ Q·A·D·cos θ</c>。</summary>
        public static Vector2 HorizontalDisplacement(WaterWave[] waves, float x, float z, float time)
        {
            float dx = 0f, dz = 0f;
            for (int i = 0; i < waves.Length; i++)
            {
                WaterWave w = waves[i];
                float qak = w.Steepness * w.Amplitude * w.WaveNumber;
                float c = Mathf.Cos(Phase(w, x, z, time));
                dx += qak * w.Direction.x * c;
                dz += qak * w.Direction.y * c;
            }
            return new Vector2(dx, dz);
        }

        /// <summary>
        /// 位移后表面在基准点 <c>(x,z)</c> 处的解析法线（世界空间，y &gt; 0）。
        ///
        /// 由参数曲面 <c>P(x,z) = (x+Δx, h, z+Δz)</c> 的两个切向量叉乘得到：
        /// <code>
        /// ∂P/∂x = (1 − Σ Q A k Dx² sinθ, Σ A k Dx cosθ, −Σ Q A k Dx Dz sinθ)
        /// ∂P/∂z = (−Σ Q A k Dx Dz sinθ, Σ A k Dz cosθ, 1 − Σ Q A k Dz² sinθ)
        /// N      = normalize(∂P/∂z × ∂P/∂x)
        /// </code>
        /// </summary>
        public static Vector3 AnalyticNormal(WaterWave[] waves, float x, float z, float time)
        {
            float txx = 1f, txy = 0f, txz = 0f; // ∂P/∂x，初始为恒等（平水面）
            float tzx = 0f, tzy = 0f, tzz = 1f; // ∂P/∂z

            for (int i = 0; i < waves.Length; i++)
            {
                WaterWave w = waves[i];
                float theta = Phase(w, x, z, time);
                float s = Mathf.Sin(theta);
                float c = Mathf.Cos(theta);
                float qak = w.Steepness * w.Amplitude * w.WaveNumber;
                float ak = w.Amplitude * w.WaveNumber;

                txx += -qak * w.Direction.x * w.Direction.x * s;
                txy += ak * w.Direction.x * c;
                txz += -qak * w.Direction.x * w.Direction.y * s;

                tzx += -qak * w.Direction.x * w.Direction.y * s;
                tzy += ak * w.Direction.y * c;
                tzz += -qak * w.Direction.y * w.Direction.y * s;
            }

            Vector3 tx = new Vector3(txx, txy, txz);
            Vector3 tz = new Vector3(tzx, tzy, tzz);

            // cross(∂P/∂z, ∂P/∂x)：平水面 (0,0,1)×(1,0,0) = (0,1,0)。
            Vector3 n = Vector3.Cross(tz, tx);
            return n.sqrMagnitude < 1e-12f ? Vector3.up : n.normalized;
        }

        /// <summary>
        /// 位移映射的雅可比行列式 <c>J = ∂(x+Δx,z+Δz)/∂(x,z)</c>。
        /// <c>J ≤ 0</c> 表示局部翻转（波峰自交/卷曲）。
        /// </summary>
        public static float Jacobian(WaterWave[] waves, float x, float z, float time)
        {
            float jxx = 1f, jxz = 0f, jzx = 0f, jzz = 1f;

            for (int i = 0; i < waves.Length; i++)
            {
                WaterWave w = waves[i];
                float s = Mathf.Sin(Phase(w, x, z, time));
                float qak = w.Steepness * w.Amplitude * w.WaveNumber;

                jxx += -qak * w.Direction.x * w.Direction.x * s;
                jxz += -qak * w.Direction.x * w.Direction.y * s;
                jzx += -qak * w.Direction.x * w.Direction.y * s;
                jzz += -qak * w.Direction.y * w.Direction.y * s;
            }

            return jxx * jzz - jxz * jzx;
        }

        /// <summary>采样点是否无自交（雅可比 &gt; epsilon）。</summary>
        public static bool IsFoldFree(WaterWave[] waves, float x, float z, float time, float epsilon = 1e-3f)
        {
            return Jacobian(waves, x, z, time) > epsilon;
        }

        /// <summary>不自交的充分条件：<c>Σ Q·A·k &lt; 1</c>。</summary>
        public static float SumHorizontalFactor(WaterWave[] waves)
        {
            float sum = 0f;
            for (int i = 0; i < waves.Length; i++)
                sum += waves[i].HorizontalFactor;
            return sum;
        }

        /// <summary>不自交的充分条件判定。</summary>
        public static bool IsFoldFreeByMetric(WaterWave[] waves)
        {
            return SumHorizontalFactor(waves) < 1f;
        }

        /// <summary>所有波振幅之和（用于"浪尖不穿出地面"的验收）。</summary>
        public static float MaxAmplitude(WaterWave[] waves)
        {
            float sum = 0f;
            for (int i = 0; i < waves.Length; i++)
                sum += waves[i].Amplitude;
            return sum;
        }

        /// <summary>最短波长（波峰间距的观感下限判据）。</summary>
        public static float MinWavelengthOf(WaterWave[] waves)
        {
            float min = float.MaxValue;
            for (int i = 0; i < waves.Length; i++)
                min = Mathf.Min(min, waves[i].Wavelength);
            return min;
        }

        /// <summary>
        /// 假焦散随水深的衰减（单调不增，∈[0,1]）：<c>(1 − d/f)²</c>（d &lt; f），否则 0。
        /// 浅处最亮、到 <paramref name="fadeDistance"/> 处归零。
        /// </summary>
        public static float CausticDepthFade(float waterDepth, float fadeDistance)
        {
            float f = Mathf.Max(fadeDistance, 1e-3f);
            float k = Mathf.Clamp01(1f - Mathf.Max(waterDepth, 0f) / f);
            return k * k;
        }

        /// <summary>
        /// 泡沫"涌岸"脉冲相位因子（∈[0,1]，先亮后灭）：<c>sin²</c> 的行波。
        /// 相位 <c>t·speed − depth·frequency</c>：随水深变浅相位提前 → 亮带由深向浅推进（向岸）。
        /// </summary>
        public static float FoamPulse(float waterDepth, float time, float speed, float frequency)
        {
            float ph = time * speed - Mathf.Max(waterDepth, 0f) * frequency;
            float s = 0.5f + 0.5f * Mathf.Sin(ph);
            return s * s;
        }

        /// <summary>泡沫破碎因子（∈[0,1]）：两层噪声之差，把"一条均匀白带"打碎。</summary>
        public static float FoamBreakup(float noiseA, float noiseB, float strength)
        {
            return Mathf.Clamp01(0.5f + (noiseA - noiseB) * Mathf.Clamp01(strength) * 2f);
        }
    }
}
