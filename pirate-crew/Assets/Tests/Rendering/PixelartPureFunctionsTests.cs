using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 像素化着色路径的**纯函数面**断言（无 GameObject——无头验证台 All 域真跑）：
    /// 滚轮档位步进、机位方向向量、sRGB hex 解析。这三处都是"装配器与出图脚本共用"或
    /// "字符串契约拼错即静默失效"的口径（出处见各用例内引用的源码文件）。
    ///
    /// 【刻意的边界】URP 渲染器/feature 胶水（Pixelart*Feature、RT 分配、shader 装载）依赖
    /// Unity 运行时，不在纯 C# 可测面内——本文件不碰。
    /// </summary>
    public class PixelartPureFunctionsTests
    {
        // ------------------------------------------------------------------
        // PixelartCameraRig.SteppedPixelScale：滚轮档位步进（端点钳制）
        // ------------------------------------------------------------------

        [Test]
        public void SteppedPixelScale_StepsInsideRange_AndClampsAtBounds()
        {
            // 端点再同向步进必须返回原值（"已在端点再同向步进返回原值"，PixelartCameraRig.cs:54）——
            // 滚轮滚到底/顶不能把档位挤出 [Min, Max]，否则像素化分辨率直接算崩。
            Assert.That(PixelartCameraRig.SteppedPixelScale(3, +1), Is.EqualTo(4));
            Assert.That(PixelartCameraRig.SteppedPixelScale(3, -1), Is.EqualTo(2));
            Assert.That(PixelartCameraRig.SteppedPixelScale(PixelartCameraRig.PixelScaleMin, -1),
                Is.EqualTo(PixelartCameraRig.PixelScaleMin), "下界再往下滚应停在原档");
            Assert.That(PixelartCameraRig.SteppedPixelScale(PixelartCameraRig.PixelScaleMax, +1),
                Is.EqualTo(PixelartCameraRig.PixelScaleMax), "上界再往上滚应停在原档");
        }

        // ------------------------------------------------------------------
        // PixelartPilotScene.CameraDirection：机位方向（注释里的文档值）
        // ------------------------------------------------------------------

        [Test]
        public void CameraDirection_Pitch30Azimuth45_MatchesDocumentedVector()
        {
            // 文档化期望值（PixelartPilotScene.cs:79 注释：θ=30°、φ=45° ⇒ (0.6124, 0.5, 0.6124)）——
            // 装配器与出图脚本共用这一处定义，30° 是"规则像素阶梯（横移 2 像素 / 下降 1 像素）"的口径，
            // 漂了的话所有像素场景的俯角一起漂。
            Vector3 dir = PixelartPilotScene.CameraDirection(30f, 45f);

            Assert.That(dir.x, Is.EqualTo(0.6124f).Within(0.001f));
            Assert.That(dir.y, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(dir.z, Is.EqualTo(0.6124f).Within(0.001f));
        }

        [Test]
        public void CameraDirection_IsUnitLengthForArbitraryAngles()
        {
            // 俯角/方位滑到任何角度都不应改变向量长度（拖动取景机位的输入是本函数的输出）。
            foreach (float pitch in new[] { 0f, 15f, 30f, 58f, 89f })
            {
                foreach (float azimuth in new[] { -45f, 0f, 45f, 140f, 315f })
                {
                    Vector3 dir = PixelartPilotScene.CameraDirection(pitch, azimuth);
                    Assert.That(dir.magnitude, Is.EqualTo(1f).Within(0.0001f),
                        $"pitch={pitch} azimuth={azimuth} 应为单位向量");
                }
            }
        }

        // ------------------------------------------------------------------
        // PixelartMaterialFactory.Hex：sRGB hex 解析（口径同编辑器侧）
        // ------------------------------------------------------------------

        [Test]
        public void Hex_ValidSixDigitParses_ToNormalizedChannels()
        {
            // C4A76A = 站面沙色（PixelartMaterialFactory.StandSand 的字面量）；
            // '#' 可省、大小写不限——三个写法必须落到同一个 Color。
            Color expected = new Color(0xC4 / 255f, 0xA7 / 255f, 0x6A / 255f, 1f);

            Assert.That(PixelartMaterialFactory.Hex("#C4A76A"), Is.EqualTo(expected).Using(ColorWithin(0.0001f)));
            Assert.That(PixelartMaterialFactory.Hex("C4A76A"), Is.EqualTo(expected).Using(ColorWithin(0.0001f)));
            Assert.That(PixelartMaterialFactory.Hex("c4a76a"), Is.EqualTo(expected).Using(ColorWithin(0.0001f)));
        }

        [Test]
        public void Hex_MalformedInput_ThrowsArgumentException()
        {
            // 守卫契约（PixelartMaterialFactory.Hex 内注释）：非法串在入口就带原始串报
            // ArgumentException——失败语义不变、失败原因说人话，不许漏进 Substring/int.Parse
            // 变成深处的越界或 FormatException。
            Assert.Throws<ArgumentException>(() => PixelartMaterialFactory.Hex("C4A76"));
            Assert.Throws<ArgumentException>(() => PixelartMaterialFactory.Hex("C4A76AA"));
            Assert.Throws<ArgumentException>(() => PixelartMaterialFactory.Hex("C4A7ZZ"));
            Assert.Throws<ArgumentException>(() => PixelartMaterialFactory.Hex(""));
            Assert.Throws<ArgumentException>(() => PixelartMaterialFactory.Hex(null));
        }

        static IEqualityComparer<Color> ColorWithin(float tolerance) => new ColorTolerance(tolerance);

        /// <summary>Color 通道级容差比较器（对齐 SmoothNormalsBakerTests 的 Vector3Tolerance 做法）。</summary>
        sealed class ColorTolerance : IEqualityComparer<Color>
        {
            readonly float _tolerance;

            public ColorTolerance(float tolerance) => _tolerance = tolerance;

            public bool Equals(Color x, Color y)
            {
                return Mathf.Abs(x.r - y.r) <= _tolerance
                       && Mathf.Abs(x.g - y.g) <= _tolerance
                       && Mathf.Abs(x.b - y.b) <= _tolerance
                       && Mathf.Abs(x.a - y.a) <= _tolerance;
            }

            public int GetHashCode(Color obj) => obj.GetHashCode();
        }
    }
}
