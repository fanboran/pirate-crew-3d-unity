using System;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>二维波动方程水面模拟的配置（值类型，便于测试里造小场）。</summary>
    public struct WaterFieldConfig
    {
        /// <summary>横向格数。</summary>
        public int CellsX;

        /// <summary>纵向格数。</summary>
        public int CellsZ;

        /// <summary>格距（世界单位）。</summary>
        public float Dx;

        /// <summary>波速（世界单位/秒，深水小振幅波速）。</summary>
        public float WaveSpeed;

        /// <summary>全域基础阻尼（每步速度保留比例 = 1 − BaseDamping）。0 = 无耗散。</summary>
        public float BaseDamping;

        /// <summary>海绵吸收层厚度（格、域四周各一圈）。</summary>
        public int SpongeCells;

        /// <summary>海绵层最大阻尼系数（贴边处）。</summary>
        public float SpongeSigmaMax;

        /// <summary>泡沫每步衰减比例（0.995 ≈ 2.3s 半衰期 @60Hz）。</summary>
        public float FoamDecay;

        /// <summary>泡沫生成曲率阈值。</summary>
        public float FoamThreshold;

        /// <summary>泡沫生成增益。</summary>
        public float FoamGain;

        /// <summary>泡沫扩散系数（0 = 不扩散；小值让泡沫随波"糊开"一点）。</summary>
        public float FoamSpread;

        /// <summary>
        /// 默认配置：128×128 覆盖 64×64 世界单位（dx=0.5），波速 9，
        /// 海绵层 8%（10 格，σ_max=0.10），泡沫半衰期约 2.3s。
        ///
        /// 【CFL 核对】C = 9·dt/0.5，取 dt = 1/60 → C = 0.30 ≤ 0.495（余量 39%）。
        /// 最大可用 dt = 0.495·0.5/9 = 0.0275s（≈36Hz），故 60Hz 固定步长安全。
        /// </summary>
        public static WaterFieldConfig Default
        {
            get
            {
                return new WaterFieldConfig
                {
                    CellsX = WaterSimRules.DefaultCellsPerAxis,
                    CellsZ = WaterSimRules.DefaultCellsPerAxis,
                    Dx = WaterSimRules.DefaultDomainSize / WaterSimRules.DefaultCellsPerAxis,
                    WaveSpeed = 9f,
                    BaseDamping = 0.0008f,
                    SpongeCells = 10,
                    SpongeSigmaMax = 0.10f,
                    FoamDecay = 0.995f,
                    FoamThreshold = 0.02f,
                    FoamGain = 4f,
                    FoamSpread = 0.12f,
                };
            }
        }
    }

    /// <summary>
    /// 二维波动方程高度场（纯 C#，无头可测；不碰任何 Unity 对象/ECall）。
    ///
    /// 【方程与离散化】<c>u_tt = c²∇²u</c>，显式中心差分：
    /// <code>
    /// v        = (u − u_prev) · (1 − σ) · (1 − baseDamping)   // σ = 海绵层阻尼
    /// u_next   = u + v + C² · ∇²u                             // C = c·dt/dx
    /// ∇²u      = u_left + u_right + u_down + u_up − 4u
    /// </code>
    /// <c>C ≤ 0.495</c>（<see cref="WaterSimRules.CflLimit"/>），步长超限会抛异常（不静默发散）。
    ///
    /// 【边界与障碍】障碍格（地形高于水面的岛/礁）恒为 0，自由格在求 ∇²u 时把"越界或障碍"
    /// 的邻格取**自身值** → 该侧差分退化为 0 → 诺伊曼（∂u/∂n = 0）反射墙。
    /// 域外圈另有海绵吸收层（<see cref="WaterSimRules.SpongeSigma"/>）吸收外向波，
    /// 避免固定边界二次反射把能量弹回场内。
    ///
    /// 【泡沫场】从浪峰曲率生成、指数衰减、轻微扩散：
    /// <code>
    /// foamNext = max(foam · decay, max(0, (−∇²u/dx² − threshold)) · gain)
    /// foam     = foamNext + spread · (邻域均值 − foamNext)
    /// </code>
    /// 浪峰（−∇²u 大）持续生成泡沫，波走后只衰减 → 泡沫带随浪前进（"生成式传输"）。
    ///
    /// 【输出只驱动观感】本类不参与任何玩法判定：落水判定仍是 <c>LevelGeometry.WaterWorldY</c>
    /// 标量阈值，"预览 = 实弹"的确定性预演（AiEvaluation）不读这里。
    /// </summary>
    public sealed class WaterWaveField2D
    {
        readonly WaterFieldConfig _cfg;
        readonly float[] _sigma;       // 每格海绵阻尼（静态，只依赖位置）
        readonly byte[] _obstacle;     // 1 = 障碍（反射墙）
        readonly float[] _foamScratch; // 泡沫扩散的第二缓冲（复用，避免每步 new）

        float[] _uPrev;
        float[] _u;
        float[] _uNext;
        float[] _foam;

        public WaterWaveField2D(WaterFieldConfig config)
        {
            _cfg = config;
            _cfg.CellsX = Mathf.Max(4, _cfg.CellsX);
            _cfg.CellsZ = Mathf.Max(4, _cfg.CellsZ);
            _cfg.Dx = Mathf.Max(_cfg.Dx, 1e-3f);
            _cfg.WaveSpeed = Mathf.Max(_cfg.WaveSpeed, 1e-3f);
            // 海绵层不得吞掉一半以上的域。
            _cfg.SpongeCells = Mathf.Clamp(_cfg.SpongeCells, 0, Mathf.Min(_cfg.CellsX, _cfg.CellsZ) / 4);

            int n = _cfg.CellsX * _cfg.CellsZ;
            _uPrev = new float[n];
            _u = new float[n];
            _uNext = new float[n];
            _foam = new float[n];
            _foamScratch = new float[n];
            _obstacle = new byte[n];
            _sigma = new float[n];

            BuildSponge();
        }

        public int CellsX => _cfg.CellsX;
        public int CellsZ => _cfg.CellsZ;
        public float Dx => _cfg.Dx;
        public float WaveSpeed => _cfg.WaveSpeed;
        public int SpongeCells => _cfg.SpongeCells;
        public int CellCount => _cfg.CellsX * _cfg.CellsZ;

        /// <summary>内部高度场（t 时刻）——仅供测试/调试读取与写入，不要在生产代码里持有。</summary>
        public float[] RawHeights => _u;

        /// <summary>内部泡沫场——仅供测试/调试。</summary>
        public float[] RawFoam => _foam;

        /// <summary>给定 dt 是否落在 CFL 稳定域内。</summary>
        public bool IsStable(float dt) => WaterSimRules.IsStable(_cfg.WaveSpeed, dt, _cfg.Dx);

        /// <summary>本场允许的最大时间步长。</summary>
        public float MaxStableDt => WaterSimRules.MaxStableDt(_cfg.WaveSpeed, _cfg.Dx);

        public void SetObstacle(int cx, int cz, bool solid)
        {
            if (!WaterSimRules.InBounds(cx, cz, _cfg.CellsX, _cfg.CellsZ))
                return;
            _obstacle[WaterSimRules.GridIndex(cx, cz, _cfg.CellsX)] = solid ? (byte)1 : (byte)0;
        }

        public bool IsObstacle(int cx, int cz)
        {
            if (!WaterSimRules.InBounds(cx, cz, _cfg.CellsX, _cfg.CellsZ))
                return true; // 域外按障碍处理（自由格邻域用自身值 → 诺伊曼）
            return _obstacle[WaterSimRules.GridIndex(cx, cz, _cfg.CellsX)] != 0;
        }

        /// <summary>整片障碍图（长度 = CellCount，行主序）。用于从烘焙纹理一次性导入。</summary>
        public void SetObstacleFromMask(bool[] mask)
        {
            if (mask == null)
                return;
            int n = Math.Min(mask.Length, _obstacle.Length);
            for (int i = 0; i < n; i++)
                _obstacle[i] = mask[i] ? (byte)1 : (byte)0;
        }

        void BuildSponge()
        {
            int cxCount = _cfg.CellsX, czCount = _cfg.CellsZ;
            for (int cz = 0; cz < czCount; cz++)
            {
                for (int cx = 0; cx < cxCount; cx++)
                {
                    int d = Math.Min(Math.Min(cx, cxCount - 1 - cx), Math.Min(cz, czCount - 1 - cz));
                    _sigma[WaterSimRules.GridIndex(cx, cz, cxCount)] =
                        WaterSimRules.SpongeSigma(d, _cfg.SpongeCells, _cfg.SpongeSigmaMax);
                }
            }
        }

        /// <summary>
        /// 推进一步。dt 必须满足 CFL（<see cref="IsStable"/>），否则抛 <see cref="InvalidOperationException"/>
        /// —— 显式差分超限会指数发散，宁可响亮失败也不静默糊掉。
        /// </summary>
        public void Step(float dt)
        {
            if (dt <= 0f)
                return;

            if (!IsStable(dt))
            {
                throw new InvalidOperationException(
                    "WaterWaveField2D: CFL 超限 c·dt/dx = " +
                    WaterSimRules.CflNumber(_cfg.WaveSpeed, dt, _cfg.Dx).ToString("F3") +
                    " > " + WaterSimRules.CflLimit.ToString("F3") +
                    "（c=" + _cfg.WaveSpeed + " dt=" + dt + " dx=" + _cfg.Dx + "）。");
            }

            int cxCount = _cfg.CellsX, czCount = _cfg.CellsZ;
            float dx = _cfg.Dx;
            float c2 = (_cfg.WaveSpeed * dt / dx);
            c2 *= c2;
            float invDx2 = 1f / (dx * dx);
            float keep = 1f - Mathf.Clamp01(_cfg.BaseDamping);
            float foamDecay = Mathf.Clamp01(_cfg.FoamDecay);
            float foamThreshold = _cfg.FoamThreshold;
            float foamGain = Mathf.Max(_cfg.FoamGain, 0f);

            // 热循环刻意内联索引与邻域判界（不用辅助方法）：16k 格 × 每帧，
            // 方法调用/边界判定的开销实测占了大头（Debug 尤其明显）。
            for (int cz = 0; cz < czCount; cz++)
            {
                int row = cz * cxCount;
                bool hasDown = cz > 0;
                bool hasUp = cz < czCount - 1;
                int rowDown = row - cxCount;
                int rowUp = row + cxCount;

                for (int cx = 0; cx < cxCount; cx++)
                {
                    int idx = row + cx;

                    if (_obstacle[idx] != 0)
                    {
                        _uNext[idx] = 0f; // 障碍格固定为 0：反射墙
                        _foamScratch[idx] = 0f;
                        continue;
                    }

                    float u = _u[idx];

                    // 越界/障碍的邻格取自身 → 该侧无通量（诺伊曼反射）。
                    float l = cx > 0 ? (_obstacle[idx - 1] != 0 ? u : _u[idx - 1]) : u;
                    float r = cx < cxCount - 1 ? (_obstacle[idx + 1] != 0 ? u : _u[idx + 1]) : u;
                    float d = hasDown ? (_obstacle[rowDown + cx] != 0 ? u : _u[rowDown + cx]) : u;
                    float up = hasUp ? (_obstacle[rowUp + cx] != 0 ? u : _u[rowUp + cx]) : u;
                    float lap = l + r + d + up - 4f * u;

                    float vel = (u - _uPrev[idx]) * (1f - _sigma[idx]) * keep;
                    _uNext[idx] = u + vel + c2 * lap;

                    // 泡沫：浪峰曲率生成 + 指数衰减。curvature = −∇²u（波峰为正）。
                    float curvature = -lap * invDx2;
                    float over = curvature - foamThreshold;
                    float gen = over > 0f ? over * foamGain : 0f;
                    float decayed = _foam[idx] * foamDecay;
                    _foamScratch[idx] = decayed > gen ? decayed : gen;
                }
            }

            // 泡沫轻扩散（读 _foamScratch 全量，写回 _foam）：让泡沫带边缘毛糙、随波"糊开"。
            float spread = Mathf.Clamp01(_cfg.FoamSpread);
            if (spread > 0f)
            {
                for (int cz = 0; cz < czCount; cz++)
                {
                    int row = cz * cxCount;
                    bool hasDown = cz > 0;
                    bool hasUp = cz < czCount - 1;
                    int rowDown = row - cxCount;
                    int rowUp = row + cxCount;

                    for (int cx = 0; cx < cxCount; cx++)
                    {
                        int idx = row + cx;
                        if (_obstacle[idx] != 0)
                        {
                            _foam[idx] = 0f;
                            continue;
                        }

                        float self = _foamScratch[idx];
                        float l = cx > 0 ? (_obstacle[idx - 1] != 0 ? self : _foamScratch[idx - 1]) : self;
                        float r = cx < cxCount - 1 ? (_obstacle[idx + 1] != 0 ? self : _foamScratch[idx + 1]) : self;
                        float d = hasDown ? (_obstacle[rowDown + cx] != 0 ? self : _foamScratch[rowDown + cx]) : self;
                        float up = hasUp ? (_obstacle[rowUp + cx] != 0 ? self : _foamScratch[rowUp + cx]) : self;
                        float avg = (l + r + d + up) * 0.25f;
                        float v = self + spread * (avg - self);
                        _foam[idx] = v > 0f ? v : 0f;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _foam.Length; i++)
                    _foam[i] = _obstacle[i] != 0 ? 0f : _foamScratch[i];
            }

            // 三缓冲轮转：uPrev ← u，u ← uNext，uNext ← 旧 uPrev（下步整体覆写）。
            float[] recycled = _uPrev;
            _uPrev = _u;
            _u = _uNext;
            _uNext = recycled;
        }

        /// <summary>
        /// 注入高斯脉冲（爆炸/落水）。<paramref name="u"/>/<paramref name="v"/> 是域 UV；
        /// <paramref name="radiusUv"/> 是高斯半径（UV）；<paramref name="amplitude"/> 是峰值高度。
        /// 同时写入 u 与 u_prev（零初速位移脉冲）→ 向外分裂出涟漪，不会产生异常速度尖峰。
        /// </summary>
        public void InjectGaussian(float u, float v, float radiusUv, float amplitude)
        {
            float sigma = Mathf.Max(radiusUv, 1e-4f);
            float twoSigma2 = 2f * sigma * sigma;
            float invCx = 1f / _cfg.CellsX, invCz = 1f / _cfg.CellsZ;

            // 只遍历受影响的格盒（3σ 截断），避免每次注入都全扫。
            int cx0 = Mathf.Max(0, Mathf.FloorToInt((u - 3f * sigma) * _cfg.CellsX - 0.5f));
            int cx1 = Mathf.Min(_cfg.CellsX - 1, Mathf.CeilToInt((u + 3f * sigma) * _cfg.CellsX - 0.5f));
            int cz0 = Mathf.Max(0, Mathf.FloorToInt((v - 3f * sigma) * _cfg.CellsZ - 0.5f));
            int cz1 = Mathf.Min(_cfg.CellsZ - 1, Mathf.CeilToInt((v + 3f * sigma) * _cfg.CellsZ - 0.5f));

            for (int cz = cz0; cz <= cz1; cz++)
            {
                float pv = (cz + 0.5f) * invCz;
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int idx = cz * _cfg.CellsX + cx;
                    if (_obstacle[idx] != 0)
                        continue;

                    float pu = (cx + 0.5f) * invCx;
                    float du = pu - u, dv = pv - v;
                    float g = amplitude * Mathf.Exp(-(du * du + dv * dv) / twoSigma2);
                    _u[idx] += g;
                    _uPrev[idx] += g;
                }
            }
        }

        /// <summary>
        /// 域边缘持续涌浪源（行波）：沿指定边内侧一格带写入正弦位移。
        /// <paramref name="edge"/> 0=−Z 1=+Z 2=−X 3=+X；带位于海绵层内缘（<see cref="SpongeCells"/> 格处），
        /// 向内辐射的波不受海绵吸收、向外的分量被海绵吃掉。
        /// </summary>
        public void InjectEdgeSwell(int edge, float amplitude, float phase)
        {
            float value = amplitude * Mathf.Sin(phase);
            int inset = Mathf.Clamp(_cfg.SpongeCells, 0, Mathf.Min(_cfg.CellsX, _cfg.CellsZ) - 1);

            switch (edge)
            {
                case 0: // −Z
                    WriteStripLine(inset, true, value);
                    break;
                case 1: // +Z
                    WriteStripLine(_cfg.CellsZ - 1 - inset, true, value);
                    break;
                case 2: // −X
                    WriteStripLine(inset, false, value);
                    break;
                default: // +X
                    WriteStripLine(_cfg.CellsX - 1 - inset, false, value);
                    break;
            }
        }

        void WriteStripLine(int line, bool alongX, float value)
        {
            if (alongX)
            {
                int row = line * _cfg.CellsX;
                for (int cx = 0; cx < _cfg.CellsX; cx++)
                {
                    int idx = row + cx;
                    if (_obstacle[idx] != 0)
                        continue;
                    _u[idx] = value;
                    _uPrev[idx] = value;
                }
            }
            else
            {
                for (int cz = 0; cz < _cfg.CellsZ; cz++)
                {
                    int idx = cz * _cfg.CellsX + line;
                    if (_obstacle[idx] != 0)
                        continue;
                    _u[idx] = value;
                    _uPrev[idx] = value;
                }
            }
        }

        /// <summary>直接在格上加泡沫（接触带/测试用）。</summary>
        public void AddFoam(int cx, int cz, float amount)
        {
            if (!WaterSimRules.InBounds(cx, cz, _cfg.CellsX, _cfg.CellsZ))
                return;
            int idx = WaterSimRules.GridIndex(cx, cz, _cfg.CellsX);
            if (_obstacle[idx] != 0)
                return;
            _foam[idx] = Mathf.Max(_foam[idx], 0f) + Mathf.Max(amount, 0f);
        }

        public float HeightAtCell(int cx, int cz)
        {
            if (!WaterSimRules.InBounds(cx, cz, _cfg.CellsX, _cfg.CellsZ))
                return 0f;
            return _u[WaterSimRules.GridIndex(cx, cz, _cfg.CellsX)];
        }

        public float FoamAtCell(int cx, int cz)
        {
            if (!WaterSimRules.InBounds(cx, cz, _cfg.CellsX, _cfg.CellsZ))
                return 0f;
            return _foam[WaterSimRules.GridIndex(cx, cz, _cfg.CellsX)];
        }

        public void SetHeightCell(int cx, int cz, float height)
        {
            if (!WaterSimRules.InBounds(cx, cz, _cfg.CellsX, _cfg.CellsZ))
                return;
            int idx = WaterSimRules.GridIndex(cx, cz, _cfg.CellsX);
            _u[idx] = height;
            _uPrev[idx] = height;
        }

        /// <summary>域 UV 处的高度（双线性；域外返回 0）。</summary>
        public float HeightAt(float u, float v)
        {
            if (!SampleBilinear(_u, u, v, out float h))
                return 0f;
            return h;
        }

        /// <summary>域 UV 处的泡沫（双线性；域外 0）。</summary>
        public float FoamAt(float u, float v)
        {
            if (!SampleBilinear(_foam, u, v, out float f))
                return 0f;
            return f;
        }

        /// <summary>
        /// 域 UV 处的法线扰动（高度场梯度解析式）：<c>n = normalize((−∂h/∂x, 1, −∂h/∂z))</c>。
        /// 域外返回 (0,1,0)。
        /// </summary>
        public Vector3 NormalAt(float u, float v)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return Vector3.up;

            float du = 1f / _cfg.CellsX;
            float dv = 1f / _cfg.CellsZ;
            float hL = HeightAt(Mathf.Max(u - du, 0f), v);
            float hR = HeightAt(Mathf.Min(u + du, 1f), v);
            float hD = HeightAt(u, Mathf.Max(v - dv, 0f));
            float hU = HeightAt(u, Mathf.Min(v + dv, 1f));

            float dhdx = (hR - hL) / (2f * _cfg.Dx);
            float dhdz = (hU - hD) / (2f * _cfg.Dx);
            return new Vector3(-dhdx, 1f, -dhdz).normalized;
        }

        /// <summary>域 UV 处的曲率 <c>∇²h</c>（波峰为负）。域外 0。</summary>
        public float CurvatureAt(float u, float v)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return 0f;

            float du = 1f / _cfg.CellsX;
            float dv = 1f / _cfg.CellsZ;
            float h = HeightAt(u, v);
            float hL = HeightAt(Mathf.Max(u - du, 0f), v);
            float hR = HeightAt(Mathf.Min(u + du, 1f), v);
            float hD = HeightAt(u, Mathf.Max(v - dv, 0f));
            float hU = HeightAt(u, Mathf.Min(v + dv, 1f));
            float invDx2 = 1f / (_cfg.Dx * _cfg.Dx);
            return (hL + hR + hD + hU - 4f * h) * invDx2;
        }

        bool SampleBilinear(float[] field, float u, float v, out float value)
        {
            value = 0f;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            // 格心约定：格 (cx) 中心在 uv = (cx+0.5)/Cells。
            float gx = Mathf.Clamp(u * _cfg.CellsX - 0.5f, 0f, _cfg.CellsX - 1f);
            float gz = Mathf.Clamp(v * _cfg.CellsZ - 0.5f, 0f, _cfg.CellsZ - 1f);
            int x0 = (int)gx, z0 = (int)gz;
            int x1 = Mathf.Min(x0 + 1, _cfg.CellsX - 1);
            int z1 = Mathf.Min(z0 + 1, _cfg.CellsZ - 1);
            float tx = gx - x0, tz = gz - z0;

            int row0 = z0 * _cfg.CellsX, row1 = z1 * _cfg.CellsX;
            float a = field[row0 + x0], b = field[row0 + x1];
            float c = field[row1 + x0], d = field[row1 + x1];
            value = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
            return true;
        }

        /// <summary>
        /// 离散波动能量（诊断/测试）：
        /// <c>E = Σ ½((u−u_prev)/dt)² + ½c²|∇u|²</c>（自由格；障碍/域外梯度按 0）。
        /// </summary>
        public float ComputeEnergy(float dt)
        {
            if (dt <= 0f)
                return 0f;

            int cxCount = _cfg.CellsX, czCount = _cfg.CellsZ;
            float invDt = 1f / dt;
            float invDx = 1f / _cfg.Dx;
            float c2 = _cfg.WaveSpeed * _cfg.WaveSpeed;
            double e = 0.0;

            for (int cz = 0; cz < czCount; cz++)
            {
                for (int cx = 0; cx < cxCount; cx++)
                {
                    int idx = cz * cxCount + cx;
                    if (_obstacle[idx] != 0)
                        continue;

                    float u = _u[idx];
                    float v = (u - _uPrev[idx]) * invDt;
                    e += 0.5 * v * v;

                    float gx = 0f, gz = 0f;
                    if (WaterSimRules.InBounds(cx + 1, cz, cxCount, czCount))
                    {
                        int n = idx + 1;
                        if (_obstacle[n] == 0)
                            gx = (_u[n] - u) * invDx;
                    }
                    if (WaterSimRules.InBounds(cx, cz + 1, cxCount, czCount))
                    {
                        int n = idx + cxCount;
                        if (_obstacle[n] == 0)
                            gz = (_u[n] - u) * invDx;
                    }
                    e += 0.5 * c2 * (gx * gx + gz * gz);
                }
            }

            return (float)e;
        }
    }
}
