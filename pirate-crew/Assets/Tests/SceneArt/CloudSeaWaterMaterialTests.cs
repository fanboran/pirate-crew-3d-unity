using NUnit.Framework;
using PirateCrew.SceneArt.CloudSea;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 云端漫步云台水洼 / 水壁 / 玻璃换装判据的纯 C# 用例（无头可跑，不 new 任何 Unity 对象）。
    ///
    /// 【覆盖】
    ///   · 材质名 → 槽位：水洼 / 水壁 / 玻璃各归各档；
    ///   · 开阔海面的清澈水材质不在这套换装里——它是烘焙器直接落的，运行时若被换回水洼
    ///     shader，整片海的岛影 / 折射 / 碎金就没了（回归用例）；
    ///   · 玻璃恒换、水洼受总闸；实心 alpha 按档兜底；水波幅度呼吸不出带。
    /// </summary>
    public class CloudSeaWaterMaterialTests
    {
        [Test]
        public void KindOf_NamedSlots_MapToTheirKind()
        {
            Assert.AreEqual(CloudSeaWaterMaterial.SlotKind.Shallow, CloudSeaWaterMaterial.KindOf("Cloud_Water"));
            Assert.AreEqual(CloudSeaWaterMaterial.SlotKind.Deep, CloudSeaWaterMaterial.KindOf("Cloud_WaterDeep"));
            Assert.AreEqual(CloudSeaWaterMaterial.SlotKind.Glass, CloudSeaWaterMaterial.KindOf("Cloud_Glass"));
            Assert.AreEqual(CloudSeaWaterMaterial.SlotKind.None, CloudSeaWaterMaterial.KindOf("Cloud_White"));
        }

        [Test]
        public void KindOf_OpenSeaSurfaceMaterial_IsNone_AndNeverSwapped()
        {
            CloudSeaWaterMaterial.SlotKind kind = CloudSeaWaterMaterial.KindOf("Cloud_WaterSurface");

            Assert.AreEqual(CloudSeaWaterMaterial.SlotKind.None, kind, "海面材质是烘焙器直接落的清澈水，不归运行时换装");
            Assert.IsFalse(CloudSeaWaterMaterial.ShouldSwap(kind, enablePuddles: true));
        }

        [Test]
        public void ShouldSwap_GlassAlways_OthersGatedByPuddles()
        {
            Assert.IsTrue(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.Glass, enablePuddles: false));
            Assert.IsTrue(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.Glass, enablePuddles: true));
            Assert.IsTrue(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.Shallow, enablePuddles: true));
            Assert.IsTrue(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.Deep, enablePuddles: true));
            Assert.IsFalse(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.Shallow, enablePuddles: false));
            Assert.IsFalse(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.Deep, enablePuddles: false));
            Assert.IsFalse(CloudSeaWaterMaterial.ShouldSwap(CloudSeaWaterMaterial.SlotKind.None, enablePuddles: true));
        }

        [Test]
        public void ResolveColor_SolidAlpha_FallsBackPerSlot()
        {
            var shallow = new Color(0.3f, 0.8f, 0.8f, 0.6f);
            var deep = new Color(0.1f, 0.4f, 0.5f, 0.6f);

            // FBX 内嵌色 alpha 恒 1（实心）：按档兜底，不能定格成不透明的水洼。
            Color puddle = CloudSeaWaterMaterial.ResolveColor(CloudSeaWaterMaterial.SlotKind.Shallow,
                true, new Color(0.4f, 0.9f, 0.9f, 1f), shallow, deep);
            Assert.AreEqual(0.60f, puddle.a, 1e-5f);
            Assert.AreEqual(0.4f, puddle.r, 1e-5f, "rgb 尊重源色");

            Color glass = CloudSeaWaterMaterial.ResolveColor(CloudSeaWaterMaterial.SlotKind.Glass,
                true, new Color(1f, 1f, 1f, 1f), shallow, deep);
            Assert.AreEqual(0.25f, glass.a, 1e-5f);

            Color partial = CloudSeaWaterMaterial.ResolveColor(CloudSeaWaterMaterial.SlotKind.Shallow,
                true, new Color(0.4f, 0.9f, 0.9f, 0.33f), shallow, deep);
            Assert.AreEqual(0.33f, partial.a, 1e-5f, "源材质显式带了 (0,1) 的 alpha 则尊重源");

            Color missing = CloudSeaWaterMaterial.ResolveColor(CloudSeaWaterMaterial.SlotKind.Deep,
                false, Color.white, shallow, deep);
            Assert.AreEqual(deep.r, missing.r, 1e-5f, "缺色时按档取兜底色");
        }

        [Test]
        public void WaveStrengthModulated_StaysWithinBreathBand()
        {
            const float amplitude = 0.06f;
            float min = float.MaxValue;
            float max = float.MinValue;
            for (float t = 0f; t < 20f; t += 0.05f)
            {
                float value = CloudSeaWaterMaterial.WaveStrengthModulated(amplitude, t);
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            Assert.AreEqual(amplitude * 0.8f, min, 1e-4f);
            Assert.AreEqual(amplitude * 1.2f, max, 1e-4f);
        }
    }
}
