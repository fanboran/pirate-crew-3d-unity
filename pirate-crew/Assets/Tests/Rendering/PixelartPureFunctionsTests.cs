using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 像素化着色路径的**纯函数面**断言（无 GameObject——无头验证台 All 域真跑）：
    /// 滚轮档位步进、可见米数↔wpp 换算（换档取景不变量）、机位方向向量、透明收件队列界、
    /// sRGB hex 解析。这些都是"装配器与出图脚本共用"或
    /// "字符串契约拼错即静默失效"的口径（出处见各用例内引用的源码文件）。
    ///
    /// 【刻意的边界】URP 渲染器/feature 胶水（Pixelart*Feature、RT 分配、shader 装载）依赖
    /// Unity 运行时，不在纯 C# 可测面内——本文件不碰（特征顺序的资产级契约在
    /// PixelartSceneContractTests 里按 YAML 文本断言）。
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
        // PixelartCameraRig.WorldPerPixelFor：可见米数 → wpp（换档取景不变量，审计 R2）
        // ------------------------------------------------------------------

        [Test]
        public void WorldPerPixelFor_KeepsVisibleHeightAcrossPixelScales()
        {
            // 取景推导：可见世界高度 = (屏幕高 ÷ k) × wpp。wpp 内嵌档数（= 可见米数×k÷1080）后，
            // 屏高恒定时可见高度必须与 k 无关——契约是"取景不动、只变颗粒粗细"（渲染管线.md §3.1），
            // 反过来（wpp 不带档数）k 2→3 时可见 32m→21.3m，滚轮一滚镜头就被凭空拉近（审计 R2）。
            const float visibleMeters = 32f;
            const float screenHeight = 1080f;
            foreach (int k in new[] { 2, 3, 4, 5 })
            {
                float wpp = PixelartCameraRig.WorldPerPixelFor(visibleMeters, k);
                float visible = screenHeight / k * wpp;
                Assert.That(visible, Is.EqualTo(visibleMeters).Within(0.001f),
                    $"k={k} 时可见高度应锁在 {visibleMeters}m（取景不动、只变颗粒粗细）");
            }
        }

        [Test]
        public void WorldPerPixelFor_ShapeAnchors_AndScaleClamp()
        {
            // 形状锚：wpp(32m, 2) = 32×2/1080 = 32/540（"1080p·2× 等效"的参考口径）；
            // 3× 档 = 32×3/1080 = 0.0889（BattleSceneWiringTests 对 rig.worldPerPixel 的期望式同源）。
            Assert.That(PixelartCameraRig.WorldPerPixelFor(32f, 2), Is.EqualTo(32f / 540f).Within(1e-6f));
            Assert.That(PixelartCameraRig.WorldPerPixelFor(32f, 3), Is.EqualTo(32f * 3f / 1080f).Within(1e-6f));
            // 钳底：0/负档按 1 算，不产出无穷大/负值（换算式里 Mathf.Max(1, pixelScale) 的守卫）。
            Assert.That(PixelartCameraRig.WorldPerPixelFor(32f, 0), Is.EqualTo(32f / 1080f).Within(1e-6f));
        }

        // ------------------------------------------------------------------
        // PixelartPath：透明趟收件队列界（审计 R11 的可无头判据）
        // ------------------------------------------------------------------

        [Test]
        public void TransparentQueueRange_LowerBoundCoversTransparentQueue()
        {
            // 下界必须含 3000：本路径透明材质声明 Queue=Transparent 即 3000，若跟 URP 的
            // RenderQueueRange.transparent（下界 3001）会整类漏掉（弹道预览曾因此一个片都不画）。
            Assert.That(PixelartPath.TransparentQueueMin, Is.EqualTo(3000),
                "透明趟收件下界必须取 3000（含 Queue=Transparent）");
            Assert.That(PixelartPath.TransparentQueueMax, Is.GreaterThan(PixelartPath.TransparentQueueMin),
                "上界必须大于下界（RenderQueueRange 的区间语义）");
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
