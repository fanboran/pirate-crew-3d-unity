using NUnit.Framework;
using PirateCrew.SceneArt;
using PirateCrew.SceneArt.Showcase;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 草坪三档斑块规则（t3ssel8r 口径几何版）的确定性 / 空间连贯性 / 档位覆盖断言，
    /// 以及草叶「强制朝上法线」（地形法线着色）的落库验证。
    /// </summary>
    public class GrassPatchRulesTests
    {
        [Test]
        public void SelectBand_SamePointTwice_Identical()
        {
            var p = new Vector3(17.3f, 0f, 11.7f);
            Assert.AreEqual(GrassPatchRules.SelectBand(p), GrassPatchRules.SelectBand(p),
                "同位置两次选档必须一致（确定性红线）");
        }

        [Test]
        public void SelectBand_BandIsAlwaysZeroOneOrTwo()
        {
            for (int x = 0; x <= 40; x += 3)
                for (int z = 0; z <= 30; z += 3)
                    Assert.That(GrassPatchRules.SelectBand(new Vector3(x, 0f, z)), Is.InRange(0, 2));
        }

        [Test]
        public void SelectBand_NearbyPointsAreMostlyCoherent()
        {
            // 斑块的意义就是空间连贯：相距 0.5 的两点应大概率同档（全场网格采样）。
            int same = 0, total = 0;
            for (int x = 1; x <= 39; x += 2)
            {
                for (int z = 1; z <= 29; z += 2)
                {
                    var a = new Vector3(x, 0f, z);
                    var b = a + new Vector3(0.5f, 0f, 0.5f);
                    total++;
                    if (GrassPatchRules.SelectBand(a) == GrassPatchRules.SelectBand(b))
                        same++;
                }
            }
            Assert.GreaterOrEqual((float)same / total, 0.6f,
                "相邻点同档率过低（实测 " + same + "/" + total + "）——斑块退化成纸屑");
        }

        [Test]
        public void SelectBand_IslandFootprint_AllThreeBandsOccur_AndMidDominates()
        {
            int light = 0, dark = 0, mid = 0;
            for (int x = 0; x <= 40; x++)
                for (int z = 0; z <= 30; z++)
                {
                    switch (GrassPatchRules.SelectBand(new Vector3(x, 0f, z)))
                    {
                        case 1: light++; break;
                        case 2: dark++; break;
                        default: mid++; break;
                    }
                }

            Assert.Greater(light, 0, "亮斑一处都没有——阈值过严或噪声塌了");
            Assert.Greater(dark, 0, "暗斑一处都没有——阈值过严或噪声塌了");
            Assert.Greater(mid, light + dark, "主底色应是 Mid 档（斑块只是点缀）");
        }

        [Test]
        public void AddLeaf_ForcedNormal_AllNormalsEqualOverride()
        {
            var b = new MeshBuffers();
            b.AddLeaf(new Vector3(3f, 0f, 4f), new Vector3(0.4f, 1f, 0.2f), 0.5f, 0.1f, 0.05f, 2, Vector3.up);
            Assert.Greater(b.VertexCount, 0, "草叶应有几何");

            Vector3[] normals = b.ToNormals();
            for (int i = 0; i < normals.Length; i++)
                Assert.AreEqual(1f, normals[i].y, 1e-4f, "顶点[" + i + "] 法线应强制朝上");
        }
    }
}
