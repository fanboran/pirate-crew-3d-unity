using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 纯 C# 旋转/基向量工具（无头可测）。
    ///
    /// 【为什么不能直接用 <c>Quaternion.Euler</c>】它是原生 <c>ECall</c>
    /// （内部走 <c>Quaternion.Internal_FromEulerRad</c>），脱离 Unity 运行时必抛
    /// <c>SecurityException</c>（见 <c>external/m2-harness/README.md</c>）。
    /// 道具几何大量按"偏航/侧倾"摆放（桅杆侧倾 22°、船体侧倾 14°…），
    /// 一旦用了它，整个道具三角面预算与形状就无法在无头验证台上断言。
    /// 这里用手算四元数（<c>Mathf.Sin/Cos</c> 与 <c>Quaternion</c> 乘法都是托管实现）替代。
    ///
    /// 【语义与 Unity 一致】<see cref="Euler"/> 的旋转顺序与 <c>Quaternion.Euler</c> 相同：
    /// 先绕 Z、再绕 X、最后绕 Y（即 q = qy · qx · qz）。全工程只有这一处实现欧拉角→四元数。
    /// </summary>
    public static class SceneArtRot
    {
        /// <summary>
        /// 欧拉角（度）→ 四元数，顺序与 <c>Quaternion.Euler(x, y, z)</c> 相同（先 Z、再 X、后 Y）。
        /// </summary>
        public static Quaternion Euler(float xDegrees, float yDegrees, float zDegrees)
        {
            const float halfToRad = 0.5f * Mathf.Deg2Rad;

            float hx = xDegrees * halfToRad;
            float hy = yDegrees * halfToRad;
            float hz = zDegrees * halfToRad;

            var qx = new Quaternion(Mathf.Sin(hx), 0f, 0f, Mathf.Cos(hx));
            var qy = new Quaternion(0f, Mathf.Sin(hy), 0f, Mathf.Cos(hy));
            var qz = new Quaternion(0f, 0f, Mathf.Sin(hz), Mathf.Cos(hz));

            return qy * qx * qz;
        }

        /// <summary>纯偏航旋转。</summary>
        public static Quaternion Yaw(float yDegrees)
        {
            return Euler(0f, yDegrees, 0f);
        }

        /// <summary>
        /// 位置 + 偏航 + 侧倾（绕 X）的变换矩阵。船的"搁浅姿态"、桅杆的"向远侧倒"都走这里。
        /// 用 <c>SceneArtRot.Trs(position, quaternion, Vector3.one)</c>，它本身是托管实现（SetTRS）。
        /// </summary>
        public static Matrix4x4 YawRoll(Vector3 position, float yawDegrees, float rollDegrees)
        {
            return SceneArtRot.Trs(position, Euler(rollDegrees, yawDegrees, 0f), Vector3.one);
        }

        /// <summary>
        /// 平移·旋转·缩放的变换矩阵（托管实现，语义与 <c>Matrix4x4.TRS</c> 一致）。
        ///
        /// 【为什么自己算】<c>Matrix4x4.TRS</c> 也是原生 <c>ECall</c>（实测在无头验证台上抛
        /// <c>SecurityException</c>）；而 <c>Matrix4x4</c> 的字段访问、<c>*</c> 运算符、
        /// <c>MultiplyPoint3x4</c> / <c>MultiplyVector</c> 都是托管实现，所以只要自己把矩阵装配出来，
        /// 后续整条几何管线就能在无头环境跑。
        /// </summary>
        public static Matrix4x4 Trs(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            float x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;
            float x2 = x + x, y2 = y + y, z2 = z + z;
            float xx = x * x2, xy = x * y2, xz = x * z2;
            float yy = y * y2, yz = y * z2, zz = z * z2;
            float wx = w * x2, wy = w * y2, wz = w * z2;

            var m = new Matrix4x4();
            m.m00 = (1f - (yy + zz)) * scale.x;
            m.m01 = (xy - wz) * scale.y;
            m.m02 = (xz + wy) * scale.z;
            m.m03 = position.x;

            m.m10 = (xy + wz) * scale.x;
            m.m11 = (1f - (xx + zz)) * scale.y;
            m.m12 = (yz - wx) * scale.z;
            m.m13 = position.y;

            m.m20 = (xz - wy) * scale.x;
            m.m21 = (yz + wx) * scale.y;
            m.m22 = (1f - (xx + yy)) * scale.z;
            m.m23 = position.z;

            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }

        /// <summary>位置 + 旋转（无缩放）。</summary>
        public static Matrix4x4 Tr(Vector3 position, Quaternion rotation)
        {
            return Trs(position, rotation, Vector3.one);
        }

        /// <summary>
        /// 由"前向"构造正交基矩阵（无平移）。用于把一根杆/一根肋骨沿任意方向摆放：
        /// 传入 <see cref="MeshBuffers.AddBox(Matrix4x4,Vector3,Vector3,float)"/> 后，
        /// 盒子的局部 +Z 即对齐 <paramref name="forward"/>。
        /// </summary>
        public static Matrix4x4 BasisFromForward(Vector3 forward, Vector3 upHint)
        {
            Vector3 f = forward.sqrMagnitude > 1e-12f ? forward.normalized : Vector3.forward;
            Vector3 up = upHint.sqrMagnitude > 1e-12f ? upHint.normalized : Vector3.up;

            // 前向与上向近似平行时换一个参考上向，避免叉积退化。
            if (Mathf.Abs(Vector3.Dot(f, up)) > 0.99f)
                up = Mathf.Abs(f.y) > 0.9f ? Vector3.forward : Vector3.up;

            Vector3 right = Vector3.Cross(up, f).normalized;
            Vector3 realUp = Vector3.Cross(f, right).normalized;

            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(right.x, right.y, right.z, 0f));
            m.SetColumn(1, new Vector4(realUp.x, realUp.y, realUp.z, 0f));
            m.SetColumn(2, new Vector4(f.x, f.y, f.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return m;
        }
    }
}
