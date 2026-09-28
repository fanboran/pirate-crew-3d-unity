using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 像素化场景**序列化契约**：11 个像素场景（试点/样板关/海图）里烘焙的
    /// <c>rig.pixelScale</c> 与环境光，必须等于 <see cref="PixelartSceneContract"/> 的常量。
    ///
    /// 【为什么要有它】常量改档（3→2，创始人 2026-09-24 裁决）后场景里的序列化副本
    /// 没人重跑装配器就静默漂移——出图判据（读常量算期望）与实机画面（读序列化值渲染）劈叉，
    /// 而且不报错。本测试把"场景序列化值 vs 契约常量"的漂移变成当场红；
    /// 修法一律是**重跑该场景的装配器**（或按常量手改场景后由装配器幂等确认），不是改测试。
    ///
    /// 【实现口径】只读场景 YAML 文本、正则提取（11 个场景全用 EditorSceneManager 打开太慢）；
    /// 契约常量住在运行时程序集（<see cref="PixelartSceneContract"/>），本测试因此不需要引用
    /// 编辑器程序集类型。非 Unity 环境（无头 harness）下场景目录不存在，跳过。
    /// </summary>
    public class PixelartSceneContractTests
    {
        static readonly string[] PixelartSceneNames =
        {
            "PixelartPilot",
            "PixelartCloud",
            "PixelartSkyIsland",
            "PixelartMap101", "PixelartMap102", "PixelartMap103", "PixelartMap104",
            "PixelartMap105", "PixelartMap106", "PixelartMap107", "PixelartMap108",
        };

        [Test]
        public void PixelartScenes_MatchAssemblingContract()
        {
            string scenesDir;
            try
            {
                // 无头 harness 里 Application.dataPath 走原生 ECall 必抛 SecurityException
                // （AGENTS 调试规范的已知边界）——本测试只在 Unity EditMode 下有意义。
                scenesDir = Path.Combine(Application.dataPath, "Scenes");
            }
            catch (System.Security.SecurityException)
            {
                Assert.Ignore("无头 harness 环境调不到 Application.dataPath——跳过（Unity EditMode 下照常执行）。");
                return;
            }

            if (!Directory.Exists(scenesDir))
            {
                Assert.Ignore("场景目录不存在——跳过场景契约检查。");
                return;
            }

            var problems = new List<string>();
            Color ambient = PixelartSceneContract.AmbientColor;

            foreach (string name in PixelartSceneNames)
            {
                string path = Path.Combine(scenesDir, name + ".unity");
                if (!File.Exists(path))
                {
                    problems.Add(name + ": 场景资产缺失 " + path);
                    continue;
                }

                string text = File.ReadAllText(path);

                // ---- 像素档位：场景里每一个 pixelScale: N 都必须是契约值（rig 恰好一台）----
                foreach (Match m in Regex.Matches(text, @"pixelScale:\s*(\d+)"))
                {
                    int serialized = int.Parse(m.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (serialized != PixelartSceneContract.PixelScale)
                        problems.Add(name + ": 序列化 pixelScale " + serialized + " ≠ 契约 "
                            + PixelartSceneContract.PixelScale + "（重跑像素场景装配器）");
                }

                // ---- 环境光（Flat 模式存进 m_AmbientSkyColor）：逐通道比对契约色 ----
                Match amb = Regex.Match(text,
                    @"m_AmbientSkyColor: \{r: ([-\d.eE]+), g: ([-\d.eE]+), b: ([-\d.eE]+)");
                if (!amb.Success)
                {
                    problems.Add(name + ": 找不到 m_AmbientSkyColor");
                }
                else
                {
                    float r = ParseInvariant(amb.Groups[1].Value);
                    float g = ParseInvariant(amb.Groups[2].Value);
                    float b = ParseInvariant(amb.Groups[3].Value);
                    if (Mathf.Abs(r - ambient.r) > 1e-5f || Mathf.Abs(g - ambient.g) > 1e-5f
                        || Mathf.Abs(b - ambient.b) > 1e-5f)
                        problems.Add(name + ": 环境光 (" + r + "," + g + "," + b + ") ≠ 契约 "
                            + PixelartSceneContract.AmbientHex + " (" + ambient.r + "," + ambient.g
                            + "," + ambient.b + ")（重跑像素场景装配器）");
                }
            }

            Assert.IsEmpty(problems, "像素场景序列化与契约漂移：\n" + string.Join("\n", problems));
        }

        static float ParseInvariant(string value)
        {
            return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
