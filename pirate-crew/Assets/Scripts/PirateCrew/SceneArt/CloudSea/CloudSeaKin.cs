using System;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 关节矩阵：一个仿射，三行 =「旋转 × 缩放」的行 + 平移，v' = R·v + t。
    /// 行的排法与水下 shader 的蒙皮缓冲里一个关节的三行完全一致（<c>_CloudSeaJoints</c>），
    /// 所以求解器算出来的矩阵可以原样写进缓冲，不再做一次转置。
    /// </summary>
    public struct CloudSeaAffine
    {
        public Vector4 R0, R1, R2;

        public static CloudSeaAffine Identity => new CloudSeaAffine
        {
            R0 = new Vector4(1f, 0f, 0f, 0f),
            R1 = new Vector4(0f, 1f, 0f, 0f),
            R2 = new Vector4(0f, 0f, 1f, 0f),
        };

        public Vector3 Apply(Vector3 p)
        {
            return new Vector3(
                R0.x * p.x + R0.y * p.y + R0.z * p.z + R0.w,
                R1.x * p.x + R1.y * p.y + R1.z * p.z + R1.w,
                R2.x * p.x + R2.y * p.y + R2.z * p.z + R2.w);
        }

        public Vector3 ApplyDir(Vector3 d)
        {
            return new Vector3(
                R0.x * d.x + R0.y * d.y + R0.z * d.z,
                R1.x * d.x + R1.y * d.y + R1.z * d.z,
                R2.x * d.x + R2.y * d.y + R2.z * d.z);
        }

        /// <summary>纯旋转的逆（旋转部分转置，平移丢弃）。</summary>
        public CloudSeaAffine Transposed => new CloudSeaAffine
        {
            R0 = new Vector4(R0.x, R1.x, R2.x, 0f),
            R1 = new Vector4(R0.y, R1.y, R2.y, 0f),
            R2 = new Vector4(R0.z, R1.z, R2.z, 0f),
        };

        /// <summary>先做 b 再做 a。</summary>
        public static CloudSeaAffine operator *(CloudSeaAffine a, CloudSeaAffine b)
        {
            CloudSeaAffine r;
            r.R0 = a.R0.x * b.R0 + a.R0.y * b.R1 + a.R0.z * b.R2;
            r.R1 = a.R1.x * b.R0 + a.R1.y * b.R1 + a.R1.z * b.R2;
            r.R2 = a.R2.x * b.R0 + a.R2.y * b.R1 + a.R2.z * b.R2;
            r.R0.w += a.R0.w;
            r.R1.w += a.R1.w;
            r.R2.w += a.R2.w;
            return r;
        }

        public static CloudSeaAffine Translation(Vector3 t) => new CloudSeaAffine
        {
            R0 = new Vector4(1f, 0f, 0f, t.x),
            R1 = new Vector4(0f, 1f, 0f, t.y),
            R2 = new Vector4(0f, 0f, 1f, t.z),
        };

        /// <summary>绕过 pivot 的单位轴转 angle 弧度。</summary>
        public static CloudSeaAffine Rotation(Vector3 axis, float angle, Vector3 pivot)
        {
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            float k = 1f - c;
            float x = axis.x, y = axis.y, z = axis.z;
            CloudSeaAffine r;
            r.R0 = new Vector4(c + k * x * x, k * x * y - s * z, k * x * z + s * y, 0f);
            r.R1 = new Vector4(k * y * x + s * z, c + k * y * y, k * y * z - s * x, 0f);
            r.R2 = new Vector4(k * z * x - s * y, k * z * y + s * x, c + k * z * z, 0f);
            r.R0.w = pivot.x - (r.R0.x * pivot.x + r.R0.y * pivot.y + r.R0.z * pivot.z);
            r.R1.w = pivot.y - (r.R1.x * pivot.x + r.R1.y * pivot.y + r.R1.z * pivot.z);
            r.R2.w = pivot.z - (r.R2.x * pivot.x + r.R2.y * pivot.y + r.R2.z * pivot.z);
            return r;
        }

        /// <summary>把方向 from 转到 to 的最小旋转（不带绕轴的自旋），绕 pivot。</summary>
        public static CloudSeaAffine FromTo(Vector3 from, Vector3 to, Vector3 pivot)
        {
            // R = I + [v]× + [v]×² / (1 + c)，v = a × b、c = a · b：不取反三角函数，小角度也精确
            // （早期版本在夹角 < 0.8° 时直接返回单位矩阵，节段末端会随车身晃动偏出毫米级的抖动）。
            Vector3 a = CloudSeaKin.Unit(from, Vector3.right);
            Vector3 b = CloudSeaKin.Unit(to, a);
            float c = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
            Vector3 v = Vector3.Cross(a, b);
            float s2 = v.sqrMagnitude;
            if (s2 < 1e-12f)
                return c > 0f ? Identity : Rotation(CloudSeaKin.Perpendicular(a), Mathf.PI, pivot);
            if (c < -0.99f)
                return Rotation(v.normalized, Mathf.Acos(c), pivot);        // 接近反向：1 + c 趋零，换轴角式
            float k = 1f / (1f + c);
            float x = v.x, y = v.y, z = v.z;
            CloudSeaAffine r;
            r.R0 = new Vector4(1f + k * (x * x - s2), k * x * y - z, k * x * z + y, 0f);
            r.R1 = new Vector4(k * x * y + z, 1f + k * (y * y - s2), k * y * z - x, 0f);
            r.R2 = new Vector4(k * x * z - y, k * y * z + x, 1f + k * (z * z - s2), 0f);
            r.R0.w = pivot.x - (r.R0.x * pivot.x + r.R0.y * pivot.y + r.R0.z * pivot.z);
            r.R1.w = pivot.y - (r.R1.x * pivot.x + r.R1.y * pivot.y + r.R1.z * pivot.z);
            r.R2.w = pivot.z - (r.R2.x * pivot.x + r.R2.y * pivot.y + r.R2.z * pivot.z);
            return r;
        }

        public static CloudSeaAffine Scale(Vector3 s, Vector3 pivot) => new CloudSeaAffine
        {
            R0 = new Vector4(s.x, 0f, 0f, pivot.x * (1f - s.x)),
            R1 = new Vector4(0f, s.y, 0f, pivot.y * (1f - s.y)),
            R2 = new Vector4(0f, 0f, s.z, pivot.z * (1f - s.z)),
        };

        /// <summary>
        /// 一个骨骼节段的矩阵：静止时从 restStart 沿 restDir，现在从 start 沿 dir。
        /// 节段绕自己的静止起点转，再整体平移到 start；长度不在这里缩放。
        /// </summary>
        public static CloudSeaAffine Segment(Vector3 restStart, Vector3 restDir, Vector3 start, Vector3 dir)
        {
            if (restDir.sqrMagnitude < 1e-10f || dir.sqrMagnitude < 1e-10f)
                return Translation(start - restStart);
            CloudSeaAffine r = FromTo(restDir, dir, restStart);
            Vector3 shift = start - restStart;
            r.R0.w += shift.x;
            r.R1.w += shift.y;
            r.R2.w += shift.z;
            return r;
        }

        /// <summary>在平移上加一段（旋转不变）。</summary>
        public void AddTranslation(Vector3 t)
        {
            R0.w += t.x;
            R1.w += t.y;
            R2.w += t.z;
        }
    }

    /// <summary>
    /// 云海生物的运动学工具：方向运算、两骨解析 IK、FABRIK、阻尼弹簧、Verlet 链、踩地步态。
    /// 只用 Vector3 / Mathf 的托管数学，不碰 Transform——验证台里能直接跑。
    /// 坐标系由调用方定；角度一律弧度。
    /// </summary>
    public static class CloudSeaKin
    {
        public const float Tau = 6.2831853f;

        public static Vector3 Unit(Vector3 v, Vector3 fallback)
        {
            float m = v.magnitude;
            return m > 1e-6f ? v / m : fallback;
        }

        /// <summary>方向 d 绕单位轴 axis 转 angle。</summary>
        public static Vector3 Rotate(Vector3 d, Vector3 axis, float angle)
        {
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            return d * c + Vector3.Cross(axis, d) * s + axis * (Vector3.Dot(axis, d) * (1f - c));
        }

        /// <summary>与 v 垂直的任一单位向量。</summary>
        public static Vector3 Perpendicular(Vector3 v)
        {
            Vector3 side = Mathf.Abs(v.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(v, side).normalized;
        }

        /// <summary>单位方向 d 与 parent 的夹角超过 maxRad 时，绕 parent × d 把它夹回来。</summary>
        public static Vector3 ClampBend(Vector3 parent, Vector3 d, float maxRad)
        {
            float dot = Mathf.Clamp(Vector3.Dot(parent, d), -1f, 1f);
            if (dot >= Mathf.Cos(maxRad))
                return d;
            Vector3 axis = Vector3.Cross(parent, d);
            if (axis.sqrMagnitude < 1e-10f)
                return Rotate(parent, Perpendicular(parent), maxRad);
            return Rotate(parent, axis.normalized, maxRad);
        }

        /// <summary>把方向 from 转向 to，最多转 maxRad。</summary>
        public static Vector3 TurnToward(Vector3 from, Vector3 to, float maxRad)
        {
            float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            float angle = Mathf.Acos(dot);
            if (angle < 1e-5f)
                return from;
            if (angle <= maxRad)
                return Unit(to, from);
            Vector3 axis = Vector3.Cross(from, to);
            if (axis.sqrMagnitude < 1e-10f)
                axis = Perpendicular(from);
            return Rotate(from, axis.normalized, maxRad);
        }

        /// <summary>
        /// 两骨解析 IK：根 a，骨长 l1 / l2，末端够到 target。bendHint = 中关节该鼓向哪边（去掉沿根 → 目标的
        /// 分量后用）。够不着时末端停在伸直能及的位置。
        /// </summary>
        public static void TwoBone(Vector3 a, float l1, float l2, Vector3 target, Vector3 bendHint,
            out Vector3 mid, out Vector3 end)
        {
            Vector3 to = target - a;
            Vector3 dir = Unit(to, Vector3.forward);
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            end = a + dir * d;
            bool straight = to.magnitude >= l1 + l2 - 1e-4f;
            float cos1 = straight ? 1f : Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float along = l1 * cos1;
            float side = l1 * Mathf.Sqrt(Mathf.Max(0f, 1f - cos1 * cos1));
            Vector3 perp = bendHint - dir * Vector3.Dot(bendHint, dir);
            perp = Unit(perp, Perpendicular(dir));
            mid = a + dir * along + perp * side;
        }

        /// <summary>
        /// FABRIK：p[0..n-1] 就地迭代；len[k] = 第 k 节长度。hint 不为 null 时，每轮把中间节点往 hint 拉一段
        /// 再满足长度（章鱼腕要拱起来、海马要保持卷曲的体形，靠它）。根不动。
        /// </summary>
        public static void Fabrik(Vector3[] p, int n, float[] len, Vector3 target, int iterations,
            Vector3[] hint, float hintWeight)
        {
            if (n < 2)
                return;
            Vector3 root = p[0];
            for (int it = 0; it < iterations; it++)
            {
                p[n - 1] = target;
                for (int k = n - 2; k >= 0; k--)
                    p[k] = p[k + 1] + Unit(p[k] - p[k + 1], Vector3.right) * len[k];
                p[0] = root;
                if (hint != null && hintWeight > 0f)
                {
                    for (int k = 1; k < n - 1; k++)
                        p[k] += (hint[k] - p[k]) * hintWeight;
                }
                for (int k = 0; k < n - 2; k++)
                    p[k + 1] = p[k] + Unit(p[k + 1] - p[k], Vector3.right) * len[k];
            }
        }

        /// <summary>三组不成倍数的正弦叠加，seed 换相位；范围约 -1..1，比单个正弦少一些机械感。</summary>
        public static float Wobble(float t, float seed)
        {
            float a = Frac(seed * 0.618034f) * Tau;
            float b = Frac(seed * 0.381966f) * Tau;
            float c = Frac(seed * 0.236068f) * Tau;
            return 0.5f * Mathf.Sin(t * 0.83f + a) + 0.3f * Mathf.Sin(t * 1.57f + b)
                + 0.2f * Mathf.Sin(t * 2.71f + c);
        }

        public static float Frac(float v)
        {
            return v - Mathf.Floor(v);
        }
    }

    /// <summary>阻尼弹簧的标量状态：按频率与阻尼比做闭式步进，帧长再大也不发散。</summary>
    public struct CloudSeaSpring
    {
        public float X;
        public float V;

        public void Step(float target, float dt, float freqHz, float zeta)
        {
            if (dt <= 0f)
                return;
            float d = X - target;
            if (Mathf.Abs(d) < 1e-5f && Mathf.Abs(V) < 1e-4f)
            {
                X = target;
                V = 0f;
                return;
            }
            float omega = freqHz * CloudSeaKin.Tau;
            zeta = Mathf.Min(zeta, 0.999f);
            float decay = Mathf.Exp(-zeta * omega * dt);
            if (decay < 1e-4f)
            {
                X = target;
                V = 0f;
                return;
            }
            if (zeta < 0.99f)
            {
                float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
                float a = d;
                float b = (V + zeta * omega * d) / wd;
                float cs = Mathf.Cos(wd * dt);
                float sn = Mathf.Sin(wd * dt);
                X = target + decay * (a * cs + b * sn);
                V = decay * ((b * wd - zeta * omega * a) * cs - (zeta * omega * b + a * wd) * sn);
            }
            else
            {
                float b = V + omega * d;
                X = target + decay * (d + b * dt);
                V = decay * (b - omega * (d + b * dt));
            }
            if (float.IsNaN(X) || float.IsNaN(V))
            {
                X = target;
                V = 0f;
            }
        }

        public void Impulse(float dv)
        {
            V += dv;
        }
    }

    /// <summary>
    /// Verlet 链：只靠位置推进的质点链，节段长度靠约束保持。触手、海葵、细尾这类「根被带着走、
    /// 其余部分靠惯性拖在后面」的东西用它。
    /// </summary>
    public sealed class CloudSeaStrand
    {
        /// <summary>质点位置（父空间或物体空间，由调用方定）。下标 0 = 根。</summary>
        public readonly Vector3[] P;
        /// <summary>本步的加速度，调用方每步填（重力 / 浮力 / 阻力 / 回复力）。</summary>
        public readonly Vector3[] Acc;
        readonly Vector3[] _prev;
        public readonly float[] Len;
        float _prevDt;

        public CloudSeaStrand(Vector3[] rest)
        {
            P = (Vector3[])rest.Clone();
            _prev = (Vector3[])rest.Clone();
            Acc = new Vector3[rest.Length];
            Len = new float[rest.Length - 1];
            for (int k = 0; k < Len.Length; k++)
                Len[k] = (rest[k + 1] - rest[k]).magnitude;
        }

        public int Count => P.Length;

        /// <summary>质点当前速度（由前后位置差估计）。</summary>
        public Vector3 Velocity(int i, float dt)
        {
            return (P[i] - _prev[i]) / Mathf.Max(dt, 1e-4f);
        }

        public void Reset(Vector3[] start)
        {
            Array.Copy(start, P, P.Length);
            Array.Copy(start, _prev, P.Length);
        }

        /// <summary>
        /// 时间校正的 Verlet 积分：速度每秒保留 keepPerSecond。下标 0（根）只记下上一帧的位置、不推进——
        /// 它由调用方在 <see cref="Constrain"/> 里钉住。
        /// </summary>
        public void Integrate(float dt, float keepPerSecond)
        {
            if (dt <= 0f)
                return;
            float ratio = _prevDt > 0f ? Mathf.Clamp(dt / _prevDt, 0.5f, 2f) : 1f;
            _prevDt = dt;
            float keep = Mathf.Pow(keepPerSecond, dt);
            float dt2 = dt * dt;
            for (int i = 0; i < P.Length; i++)
            {
                Vector3 v = (P[i] - _prev[i]) * (ratio * keep);
                _prev[i] = P[i];
                if (i > 0)
                    P[i] += v + Acc[i] * dt2;
            }
        }

        /// <summary>从根到梢满足节段长度，根钉在 root。</summary>
        public void Constrain(Vector3 root, int passes)
        {
            P[0] = root;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int k = 0; k < Len.Length; k++)
                    P[k + 1] = P[k] + CloudSeaKin.Unit(P[k + 1] - P[k], Vector3.right) * Len[k];
            }
        }
    }

    /// <summary>
    /// 一只踩地的脚：踩实时钉在世界里的锚点上（身体怎么走它都不动），迈步时沿弧线摆到新落点。
    /// 什么时候抬脚由调用方按「锚点离该在的位置多远、邻居抬没抬」决定。
    /// </summary>
    public struct CloudSeaFoot
    {
        public bool Planted;
        /// <summary>踩地点。</summary>
        public Vector3 Anchor;
        /// <summary>当前位置（踩实 = 锚点）。</summary>
        public Vector3 Position;
        Vector3 _from;
        Vector3 _to;
        float _t;
        float _duration;
        float _lift;

        public static CloudSeaFoot Plant(Vector3 p)
        {
            return new CloudSeaFoot { Planted = true, Anchor = p, Position = p, _t = 1f };
        }

        /// <summary>从当前位置抬脚，duration 秒后踩到 to；lift = 弧线最高点离地高度。</summary>
        public void Lift(Vector3 to, float duration, float lift)
        {
            if (!Planted)
                return;
            Planted = false;
            _from = Position;
            _to = to;
            _t = 0f;
            _duration = Mathf.Max(0.03f, duration);
            _lift = lift;
        }

        public void Step(float dt)
        {
            if (Planted)
            {
                Position = Anchor;
                return;
            }
            _t += dt / _duration;
            if (_t >= 1f)
            {
                _t = 1f;
                Planted = true;
                Anchor = _to;
                Position = _to;
                return;
            }
            float u = _t * _t * (3f - 2f * _t);
            Position = Vector3.Lerp(_from, _to, u) + Vector3.up * (_lift * Mathf.Sin(Mathf.PI * _t));
        }
    }
}
