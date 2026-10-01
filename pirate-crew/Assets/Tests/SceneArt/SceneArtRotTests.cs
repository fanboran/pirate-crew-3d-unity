using NUnit.Framework;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 纯 C# 旋转/基向量工具（<see cref="SceneArtRot"/>）的语义断言。
    /// 它存在的理由就是替代 <c>Quaternion.Euler</c> / <c>Matrix4x4.TRS</c> 这两个原生 ECall
    /// （SceneArtRot.cs 类头：脱离 Unity 运行时必抛 SecurityException）——本文件全部走
    /// 托管链（<see cref="SceneArtRot.Trs"/> + <c>MultiplyVector</c>），无头验证台可跑。
    /// </summary>
    public class SceneArtRotTests
    {
        [Test]
        public void Yaw_NinetyDegrees_RotatesForwardToRight()
        {
            // Unity 语义：Quaternion.Euler(0, 90, 0) 把 forward (0,0,1) 转到 right (1,0,0)、
            // 把 right (1,0,0) 转到 back (0,0,-1)。SceneArtRot.Euler 声称与它同顺序
            // （先绕 Z、再绕 X、最后绕 Y）——用矩阵乘向量（托管）在无头环境锁住这一语义，
            // 否则道具的"偏航摆放"两头（烘焙几何 / 运行时壳体）各转各的。
            Matrix4x4 m = SceneArtRot.Trs(Vector3.zero, SceneArtRot.Yaw(90f), Vector3.one);

            Vector3 rotatedForward = m.MultiplyVector(new Vector3(0f, 0f, 1f));
            Vector3 rotatedRight = m.MultiplyVector(new Vector3(1f, 0f, 0f));

            Assert.That(rotatedForward.x, Is.EqualTo(1f).Within(1e-4f), "forward 应转到 +X");
            Assert.That(rotatedForward.z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(rotatedRight.z, Is.EqualTo(-1f).Within(1e-4f), "right 应转到 -Z");
            Assert.That(rotatedRight.x, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Trs_PlacesPositionAndKeepsBottomRow()
        {
            // 平移列（m03/m13/m23）与底行（0,0,0,1）——齐次矩阵的形状契约，
            // 底行坏了 MultiplyPoint3x4 的 w 分量就不再是 1，透视会悄悄畸变。
            var pos = new Vector3(3f, -2f, 7.5f);
            Matrix4x4 m = SceneArtRot.Trs(pos, SceneArtRot.Euler(10f, 20f, 30f), Vector3.one);

            Assert.That(m.m03, Is.EqualTo(pos.x).Within(1e-5f));
            Assert.That(m.m13, Is.EqualTo(pos.y).Within(1e-5f));
            Assert.That(m.m23, Is.EqualTo(pos.z).Within(1e-5f));
            Assert.That(m.m30 + m.m31 + m.m32, Is.EqualTo(0f).Within(1e-6f), "底行前三列应为 0");
            Assert.That(m.m33, Is.EqualTo(1f), "底行末位应为 1");
        }

        [Test]
        public void BasisFromForward_ParallelUpHint_DoesNotCollapse()
        {
            // 退化分支：前向与上向（近似）平行时换参考上向（SceneArtRot.BasisFromForward 内守卫）——
            // 典型调用是"把一根杆沿 +Y 立起来"（forward = upHint = Vector3.up），
            // 守卫失效会叉积零向量 → NaN 顶点 → 烘焙件整块黑影。
            Matrix4x4 m = SceneArtRot.BasisFromForward(Vector3.up, Vector3.up);

            Vector3 right = m.GetColumn(0);
            Vector3 up = m.GetColumn(1);
            Vector3 forward = m.GetColumn(2);

            Assert.That(float.IsNaN(right.x + right.y + right.z), Is.False, "right 列不应含 NaN");
            Assert.That(float.IsNaN(up.x + up.y + up.z), Is.False, "up 列不应含 NaN");
            Assert.That(forward, Is.EqualTo(new Vector3(0f, 1f, 0f)).Using(Vector3Tolerance(1e-4f)),
                "+Z 列（局部前向）应仍对齐传入的 forward");

            Assert.That(Vector3.Dot(right, up), Is.EqualTo(0f).Within(1e-4f), "三列应两两正交");
            Assert.That(Vector3.Dot(right, forward), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Vector3.Dot(up, forward), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void BasisFromForward_RegularInputs_BuildRightHandedOrthobasis()
        {
            // 常规分支：forward=+Z、upHint=+Y → right=+X、up=+Y、forward=+Z（单位基）。
            Matrix4x4 m = SceneArtRot.BasisFromForward(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f));

            // Matrix4x4.GetColumn 返回 Vector4——断言用 Vector3 口径，显式收窄（基向量只关心 xyz）。
            Assert.That((Vector3)m.GetColumn(0), Is.EqualTo(new Vector3(1f, 0f, 0f)).Using(Vector3Tolerance(1e-4f)));
            Assert.That((Vector3)m.GetColumn(1), Is.EqualTo(new Vector3(0f, 1f, 0f)).Using(Vector3Tolerance(1e-4f)));
            Assert.That((Vector3)m.GetColumn(2), Is.EqualTo(new Vector3(0f, 0f, 1f)).Using(Vector3Tolerance(1e-4f)));
        }

        static System.Collections.Generic.IEqualityComparer<Vector3> Vector3Tolerance(float tolerance)
        {
            return new Vector3ToleranceComparer(tolerance);
        }

        sealed class Vector3ToleranceComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            readonly float _tolerance;

            public Vector3ToleranceComparer(float tolerance) => _tolerance = tolerance;

            public bool Equals(Vector3 x, Vector3 y) => (x - y).magnitude <= _tolerance;

            public int GetHashCode(Vector3 obj) => obj.GetHashCode();
        }
    }
}
