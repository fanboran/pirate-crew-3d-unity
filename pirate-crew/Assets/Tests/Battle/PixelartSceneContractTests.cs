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
    /// 像素化场景**序列化契约**：现役像素场景（试点 + 样板关）里烘焙的
    /// <c>rig.pixelScale</c> 与环境光，必须等于 <see cref="PixelartSceneContract"/> 的常量。
    ///
    /// 【为什么要有它】常量改档（3→2，创始人 2026-09-24 裁决）后场景里的序列化副本
    /// 没人重跑装配器就静默漂移——出图判据（读常量算期望）与实机画面（读序列化值渲染）劈叉，
    /// 而且不报错。本测试把"场景序列化值 vs 契约常量"的漂移变成当场红；
    /// 修法一律是**重跑该场景的装配器**（或按常量手改场景后由装配器幂等确认），不是改测试。
    ///
    /// 【海图试点场景：当前 0 张】八张海图（101–108）已删除待重做，其场景名从下表移除；
    /// 海图重做后随装配器 `PixelartWorldMapPilotSetup` 一起补回。
    ///
    /// 【实现口径】只读场景 YAML 文本、正则提取（场景全用 EditorSceneManager 打开太慢）；
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
            // 海图试点场景（PixelartMap101…108）随海图删除，重做后补回。
        };

        [Test]
        public void PixelartScenes_MatchAssemblingContract()
        {
            try
            {
                RunContractCheck();
            }
            catch (System.Security.SecurityException)
            {
                // 无头 harness 环境走不到 Unity 原生 API（Application.dataPath 等原生 ECall
                // 必抛 SecurityException，AGENTS 调试规范的已知边界）——本测试只在 Unity
                // EditMode 下有意义。守卫必须包住**整个**检查体：除 dataPath 外，Unity 类型
                // （Color/Mathf 等）在脱离 Unity 运行时的程序集里同样可能触发原生绑定。
                Assert.Ignore("无头 harness 环境调不到 Unity 原生 API——跳过（Unity EditMode 下照常执行）。");
            }
        }

        static void RunContractCheck()
        {
            string scenesDir = Path.Combine(Application.dataPath, "Scenes");
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
