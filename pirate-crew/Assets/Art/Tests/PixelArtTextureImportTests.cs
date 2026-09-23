using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.ArtPipeline.Tests
{
    /// <summary>
    /// 像素纹理导入规范的 EditMode 门禁用例（像素纹理资产管线 §3 的机器判据）。
    ///
    /// 【为什么这个文件在 Assets/Art/Tests/ 而不是 Assets/Tests/】
    /// 无头验证台的 <c>-p:HarnessScope=All</c> 域会编译 <c>Assets/Tests/**</c>，而该域**不引用
    /// UnityEditor 程序集**（external/harness/Harness.csproj 里 UnityEditor 只在 DataEditor 域
    /// 条件引用）。本用例必须用 <see cref="AssetImporter.GetAtPath"/> 才谈得上"断言导入设置"，
    /// 一旦放进 Assets/Tests/ 就会把主控的 1130 条 All 域门禁打成编译错误。
    /// 放在 Assets/Art/Tests/（本轨道自己的域）后：Unity 侧的 Test Runner 照常发现并执行它，
    /// 无头验证台两个域都不碰它 —— 两边都不受伤。
    ///
    /// 【为什么要反射读约定】约定真源 <c>PixelArtTextureRules</c> 在 Assets/Editor（预定义程序集
    /// Assembly-CSharp-Editor），而 asmdef 汇编**不能**引用预定义程序集。用装配件限定名取类型
    /// 既保住"约定只有一份"，又让"约定体被删/改名"变成一条明确的红灯，而不是静默跳过。
    ///
    /// 【跑法】
    ///   Unity: Test Runner → EditMode → PirateCrew.ArtPipelineTests
    ///   无头: 见 docs/技术/资产管线/像素纹理导入规范.md §4（batchmode -runTests -testPlatform EditMode）
    /// </summary>
    [TestFixture]
    public class PixelArtTextureImportTests
    {
        const string RulesTypeName =
            "PirateCrew.EditorTools.Art.PixelArtTextureRules, Assembly-CSharp-Editor";

        /// <summary>样品贴图（由 <c>python tools/palette/palette_tool.py sample</c> 生成）。</summary>
        const string SampleAssetPath =
            "Assets/Art/Textures/Pixel/Diagnostics/PixelSpecSample_64.png";

        static Type RulesType()
        {
            Type type = Type.GetType(RulesTypeName);
            Assert.That(type, Is.Not.Null,
                "找不到 PixelArtTextureRules（" + RulesTypeName + "）。约定真源被删/改名/挪出 Assets/Editor 了？"
                + "导入规则的唯一真源必须留在 Assets/Editor/Art/PixelArtTextureRules.cs。");
            return type;
        }

        static string PixelRoot()
        {
            FieldInfo field = RulesType().GetField("PixelRoot", BindingFlags.Public | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "PixelArtTextureRules.PixelRoot 常量不见了");
            return (string)field.GetRawConstantValue();
        }

        static bool IsPixelAsset(string assetPath)
        {
            MethodInfo method = RulesType().GetMethod("IsPixelAsset", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "PixelArtTextureRules.IsPixelAsset 不见了");
            return (bool)method.Invoke(null, new object[] { assetPath });
        }

        static string Expectation()
        {
            MethodInfo method = RulesType().GetMethod("Expectation", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "PixelArtTextureRules.Expectation 不见了");
            return (string)method.Invoke(null, null);
        }

        static List<string> Check(TextureImporter importer)
        {
            MethodInfo method = RulesType().GetMethod("Check", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "PixelArtTextureRules.Check 不见了");
            return (List<string>)method.Invoke(null, new object[] { importer });
        }

        // ------------------------------------------------------------------
        // 1. 约定规则本身：范围自解释、且不误伤存量
        // ------------------------------------------------------------------
        [Test]
        public void Convention_MatchesOnlyPixelRootPrefix()
        {
            string root = PixelRoot();
            Assert.That(root, Is.EqualTo("Assets/Art/Textures/Pixel"),
                "约定根目录变了就必须同步改文档（像素纹理导入规范 §2）与本用例");

            Assert.That(IsPixelAsset(root + "/Diagnostics/PixelSpecSample_64.png"), Is.True,
                "约定目录内的贴图必须是像素纹理");
            Assert.That(IsPixelAsset(root + "/deep/nested/tile.png"), Is.True,
                "任意深度子目录都属于约定域（FindAssets 递归、约定也不必逐层登记）");
            Assert.That(IsPixelAsset("Assets/Art/Textures/Fx/Droplet.png"), Is.False,
                "存量贴图（Textures/Fx）不得被约定域收编 —— 规则必须只对新增生效");
            Assert.That(IsPixelAsset("Assets/Art/Textures/PixelKit/atlas.png"), Is.False,
                "同前缀的兄弟目录不算命中（避免 'Pixel*' 通配把范围放大）");
            Assert.That(IsPixelAsset(null), Is.False);
            Assert.That(IsPixelAsset(""), Is.False);
        }

        [Test]
        public void Convention_AllTexturesUnderRootAreClassifiedPixel()
        {
            string root = PixelRoot();
            if (!AssetDatabase.IsValidFolder(root))
                Assert.Pass("约定目录尚不存在，无可误收编的资产");

            // 约定目录里出现的每张图都应是**新产出**的像素纹理；本仓的存量 55 张全在
            // Textures/{Crew,Fx,UI,Water} 之下，因此这里断言"约定域内没有任何一张是旧目录搬过来的"
            // 只能靠人眼/文档保证，机器能保证的是范围判定的确定性（上一条用例）。
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Assert.That(IsPixelAsset(path), Is.True, path + " 在约定目录内但未被判定为像素纹理");
            }
        }

        // ------------------------------------------------------------------
        // 2. 导入设置：三项关键 + 一条隐藏坑
        // ------------------------------------------------------------------
        [Test]
        public void PixelTextures_HaveRequiredImportSettings()
        {
            string root = PixelRoot();

            // 靶子必须在：否则下面的循环空转，"门禁绿"变成"没检查"（步骤 1 事故的同类失败）
            var sample = AssetDatabase.LoadAssetAtPath<Texture2D>(SampleAssetPath);
            Assert.That(sample, Is.Not.Null,
                "样品像素贴图缺失：" + SampleAssetPath + "\n"
                + "重跑 `python tools/palette/palette_tool.py sample` 生成（它是本用例的判定靶子）。");

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            Assert.That(guids.Length, Is.GreaterThan(0), "约定目录内一张纹理都没有，用例无法验证任何东西");

            var failures = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                List<string> problems = Check(importer);
                for (int k = 0; k < problems.Count; k++)
                    failures.Add(path + "：" + problems[k]);
            }

            Assert.That(failures, Is.Empty,
                "像素纹理导入设置不合规（期望 " + Expectation() + "，共 " + guids.Length + " 个资产）：\n  "
                + string.Join("\n  ", failures.ToArray())
                + "\n修复：把资产 Reimport（约定目录由 PixelArtTexturePostprocessor 在导入期强制），"
                + "或跑菜单 PirateCrew/Art/像素纹理/校验约定目录 看全量报告。");
        }

        [Test]
        public void LegacyTextures_StayOutsideConvention()
        {
            // 这条是**反向对照**：证明规则的判定确实有边界（判据三律第 1 条"自证"）。
            // 存量贴图若恰好也满足 Point/mip off，也不算违规；本用例只断言
            // 「规则不会把它们判成像素纹理」——即上一条用例的失败清单里不应出现它们。
            var legacy = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Fx/Droplet.png");
            if (legacy == null)
                Assert.Pass("Canvas 存量贴图不在本工程，跳过反向对照");

            Assert.That(IsPixelAsset(AssetDatabase.GetAssetPath(legacy)), Is.False,
                "存量贴图 " + AssetDatabase.GetAssetPath(legacy) + " 被误判进像素纹理约定域");
        }
    }
}
