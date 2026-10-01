using NUnit.Framework;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 确定性哈希（<see cref="SceneArtHash"/>）的值域与敏感性断言。
    /// 它是网格顶点剪影扰动与烘焙几何共用的地基（SceneArtHash.cs 类头：MeshBuffers 顶点抖动
    /// 与烘焙器几何层共用本实现）——值域越界会产出坏顶点，salt 位丢失会让"换个种子"
    /// 变成空操作且肉眼难辨。纯 C# 整数运算，无 GameObject。
    /// </summary>
    public class SceneArtHashTests
    {
        [Test]
        public void Hash01_SampledDomain_AlwaysInUnitRange()
        {
            // 全符号组合大采样：Hash01 声称映射到 [0, 1)（SceneArtHash.Hash01 注释）——
            // 输入带负整数与大整数（unchecked 溢出是设计内行为），输出仍不许越界。
            for (int a = -50; a <= 50; a += 7)
            {
                for (int b = -50; b <= 50; b += 7)
                {
                    for (int salt = -3; salt <= 3; salt++)
                    {
                        float v = SceneArtHash.Hash01(a, b, salt);
                        Assert.That(v, Is.InRange(0f, 1f), $"({a},{b},{salt}) 应落在 [0,1)");
                        Assert.That(v, Is.Not.EqualTo(1f), $"({a},{b},{salt}) 不应触达 1（区间右开）");
                    }
                }
            }
        }

        [Test]
        public void Hash01_SameInputTwice_IsIdentical()
        {
            // 确定性红线：烘焙几何要求"同输入两次逐位一致"（SceneArtBakeDeterminismTests 的前提）。
            for (int i = -5; i <= 5; i++)
            {
                Assert.AreEqual(SceneArtHash.Hash01(i, 3, 9), SceneArtHash.Hash01(i, 3, 9));
            }
        }

        [Test]
        public void Hash01_SaltChanges_AreNotCollapsedToOneValue()
        {
            // salt 敏感性：换 seed 是美术侧调剪影的唯一手段——如果混合里 salt 位丢失，
            // 所有 seed 产出同一个值，"换种子"静默变成空操作。要求 distinct 率压倒性高。
            const int saltCount = 64;
            var seen = new System.Collections.Generic.HashSet<float>();
            for (int salt = 0; salt < saltCount; salt++)
                seen.Add(SceneArtHash.Hash01(17, 23, salt));

            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(saltCount - 4),
                $"64 个 salt 只产出 {seen.Count} 个不同值——salt 位疑似塌缩");
        }

        [Test]
        public void SignedHash_SampledDomain_AlwaysInSignedRange()
        {
            // SignedHash 声称映射到 [-1, 1)（SceneArtHash.SignedHash 注释）。
            for (int a = -50; a <= 50; a += 7)
            {
                for (int b = -50; b <= 50; b += 7)
                {
                    float v = SceneArtHash.SignedHash(a, b, 5);
                    Assert.That(v, Is.InRange(-1f, 1f), $"({a},5,{b}) 应落在 [-1,1)");
                }
            }
        }
    }
}
