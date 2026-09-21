using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 像素纹理约定域的**导入设置校验器**（资产篇 §8「单资产：导入设置三项齐全」的机器判据）。
    ///
    /// 【它和 EditMode 测试的分工】本类可以随时手动跑并打印全量报告（含每个资产的违反项），
    /// 适合排障；<c>Assets/Art/Tests/PixelArtTextureImportTests.cs</c> 是同一份判据的门禁用例，
    /// 适合自动化。两者调用 <see cref="PixelArtTextureRules.Check"/>，判据只有一份。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Art/像素纹理/校验约定目录
    ///   无头: -executeMethod PirateCrew.EditorTools.Art.PixelArtTextureValidator.ValidateAll
    ///         （违规定资产时抛异常 ⇒ batchmode 退出码非 0，可以直接当门禁）
    /// </summary>
    public static class PixelArtTextureValidator
    {
        /// <summary>校验结果：合规资产数 / 违规清单 / 报告文本。</summary>
        public struct Report
        {
            public int checkedCount;
            public List<string> violations;
            public string text;
        }

        [MenuItem("PirateCrew/Art/像素纹理/校验约定目录", priority = 120)]
        public static void ValidateAll()
        {
            Report report = Validate();
            Debug.Log(report.text);
            if (report.violations.Count > 0)
            {
                // 抛异常 = 无头执行时退出码非 0（警告会被忽略，这门禁必须是硬的）
                throw new System.InvalidOperationException(
                    "[PixelArtTextureValidator] " + report.violations.Count
                    + " 个像素纹理的导入设置不合规，见上方报告（期望：" + PixelArtTextureRules.Expectation() + "）。");
            }
            Debug.Log("[PixelArtTextureValidator] 合规：" + report.checkedCount + " 个像素纹理全部满足 "
                + PixelArtTextureRules.Expectation() + "。");
        }

        /// <summary>跑一遍校验并返回报告（不抛异常；调用方决定怎么处置）。</summary>
        public static Report Validate()
        {
            var violations = new List<string>();
            var sb = new StringBuilder();
            sb.Append("[PixelArtTextureValidator] 约定域 ").Append(PixelArtTextureRules.PixelRoot)
              .Append("/**，期望 ").Append(PixelArtTextureRules.Expectation()).Append('\n');

            if (!AssetDatabase.IsValidFolder(PixelArtTextureRules.PixelRoot))
            {
                sb.Append("  约定目录尚不存在（合法状态：还没有像素纹理）。\n");
                return new Report { checkedCount = 0, violations = violations, text = sb.ToString() };
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PixelArtTextureRules.PixelRoot });
            int checkedCount = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!PixelArtTextureRules.IsPixelAsset(path))
                    continue; // 理论上不会发生（FindAssets 已在根目录内），防御性保留
                checkedCount++;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                List<string> problems = PixelArtTextureRules.Check(importer);
                if (problems.Count == 0)
                {
                    sb.Append("  [ok]   ").Append(path).Append('\n');
                    continue;
                }

                sb.Append("  [FAIL] ").Append(path).Append('\n');
                for (int k = 0; k < problems.Count; k++)
                {
                    sb.Append("         · ").Append(problems[k]).Append('\n');
                    violations.Add(path + "：" + problems[k]);
                }
            }

            sb.Append("  合计 ").Append(checkedCount).Append(" 个资产，")
              .Append(violations.Count == 0 ? "全部合规" : violations.Count + " 项违规")
              .Append("。\n");
            sb.Append("  说明：本规则只对约定目录生效，存量贴图（Assets/Art/Textures 其它子目录）不受影响。\n");

            return new Report { checkedCount = checkedCount, violations = violations, text = sb.ToString() };
        }
    }
}
