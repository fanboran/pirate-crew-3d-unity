#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 关卡数据资产的 **Unity 侧内容门禁**（EditMode）：只有真正在编辑器里加载得到的资产才算数。
    ///
    /// 【为什么单独一个文件 + #if UNITY_EDITOR】它必须用 <c>AssetDatabase</c>/<c>Resources</c>，而
    /// 无头验证台把 <c>Assets/Tests/Battle/*.cs</c> 编进不引用 UnityEditor 的域——不加条件会
    /// 直接把主控的 Battle 域门禁打成编译错误（同 <c>Assets/Art/Tests/PixelArtTextureImportTests</c>
    /// 的处置）。加条件后：Unity 侧照常发现并执行；无头侧该文件编译为空。
    ///
    /// 【它多验了什么（无头侧验不到的）】
    ///   ① 手写/generated 的 `.asset` YAML 真能被 Unity 反序列化成 <see cref="LevelDefinition"/> /
    ///      <see cref="WorldMapDefinitionAsset"/>（<c>m_Script</c> guid 对得上、字段名与类型吻合）；
    ///   ② <c>Resources.Load("LevelCatalog")</c> 能取到清单，且清单把 10 张资产全带进来
    ///      （8 张海图 + 2 张关卡；关卡 2「碎岛雨」已删除 2026-09-22，号段有意不连续）；
    ///   ③ 校验器（<see cref="PirateCrew.EditorTools.LevelAssetValidator"/>）在同一批资产上零错误。
    ///
    /// 跑法：Test Runner → EditMode；无头见 docs/项目/工业级重构总纲.md §5。
    /// </summary>
    [TestFixture]
    public class LevelAssetGateTests
    {
        const string WorldMapsFolder = "Assets/Data/WorldMaps";
        const string LevelsFolder = "Assets/Data/Levels";

        [Test]
        public void Catalog_LoadsFromResources_WithEveryLevelAsset()
        {
            var catalog = Resources.Load<LevelCatalog>(LevelAssetSchema.CatalogResourcePath);
            Assert.That(catalog, Is.Not.Null,
                "Resources.Load(\"" + LevelAssetSchema.CatalogResourcePath + "\") 取不到清单——"
                + "清单必须落在 Assets/Data/Levels/Resources/ 下才会进构建包");

            Assert.That(catalog.WorldMaps.Count, Is.EqualTo(8), "清单里的海图数不对");
            Assert.That(catalog.Levels.Count, Is.EqualTo(2), "清单里的关卡数不对（关卡 2 已删除，只剩 1、3）");

            for (int i = 0; i < catalog.WorldMaps.Count; i++)
                Assert.That(catalog.WorldMaps[i], Is.Not.Null, "清单 worldMaps[" + i + "] 是空引用");
            for (int i = 0; i < catalog.Levels.Count; i++)
                Assert.That(catalog.Levels[i], Is.Not.Null, "清单 levels[" + i + "] 是空引用");
        }

        [Test]
        public void Assets_DeserializeIntoPayloads_EqualToGoldenJson()
        {
            int checkedAssets = 0;

            // 海图：资产反序列化出的载荷必须与 golden JSON 解析出的载荷逐字段一致
            // （字段顺序/名字对不上、m_Script guid 指错脚本，都会在这里现形）。
            string[] mapGuids = AssetDatabase.FindAssets("t:WorldMapDefinitionAsset", new[] { WorldMapsFolder });
            for (int i = 0; i < mapGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(mapGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<WorldMapDefinitionAsset>(path);
                Assert.That(asset, Is.Not.Null, path + " 加载失败（m_Script guid 不对？）");

                string goldenPath = WorldMapsFolder + "/_golden/" + asset.Id + ".json";
                WorldMapAssetPayload expected =
                    LevelAssetJson.ReadWorldMap(System.IO.File.ReadAllText(goldenPath));
                Assert.That(expected, Is.Not.Null, goldenPath + " 解析失败");

                Assert.That(LevelAssetJson.Write(asset.Data), Is.EqualTo(LevelAssetJson.Write(expected)),
                    path + " 反序列化出的数据与 golden JSON 不一致");
                checkedAssets++;
            }

            string[] levelGuids = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelsFolder });
            for (int i = 0; i < levelGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(levelGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                Assert.That(asset, Is.Not.Null, path + " 加载失败（m_Script guid 不对？）");

                string goldenPath = LevelsFolder + "/_golden/" + asset.Data.assetName + ".json";
                LevelAssetPayload expected =
                    LevelAssetJson.ReadLevel(System.IO.File.ReadAllText(goldenPath));
                Assert.That(expected, Is.Not.Null, goldenPath + " 解析失败");

                Assert.That(LevelAssetJson.Write(asset.Data), Is.EqualTo(LevelAssetJson.Write(expected)),
                    path + " 反序列化出的数据与 golden JSON 不一致");
                checkedAssets++;
            }

            // 计数 = 上面两轮循环各自找到的资产数：8 张海图 + 2 张关卡 = 10
            //（关卡 2 已删除，不参与计数；关卡号不连续不影响这里——它只数文件）。
            Assert.That(checkedAssets, Is.EqualTo(10), "应有 8 张海图 + 2 张关卡资产");
        }

        [Test]
        public void LevelAssetValidator_ReportsNoErrors()
        {
            object report = RunValidator();
            Assert.That(report, Is.Not.Null);

            var errors = (System.Collections.IList)report.GetType()
                .GetField("Errors", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .GetValue(report);

            Assert.That(errors.Count, Is.EqualTo(0),
                "关卡资产校验失败：\n" + JoinToList(errors));
        }

        /// <summary>
        /// 校验器真源在 `Assets/Editor`（预定义程序集 <c>Assembly-CSharp-Editor</c>），而 asmdef 汇编
        /// **不能**引用预定义程序集——所以这里用装配件限定名反射取类型（同
        /// `Assets/Art/Tests/PixelArtTextureImportTests.cs:22-25` 的处置）。
        /// 好处是"校验器被改名/删除"会变成一条明确的红灯，而不是静默跳过。
        /// </summary>
        const string ValidatorTypeName =
            "PirateCrew.EditorTools.LevelAssetValidator, Assembly-CSharp-Editor";

        static object RunValidator()
        {
            System.Type type = System.Type.GetType(ValidatorTypeName, throwOnError: false);
            if (type == null)
            {
                // 单汇编工具链（无头验证台把全工程源码编进一个程序集）下兜底按类型名找。
                System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && type == null; i++)
                    type = assemblies[i].GetType("PirateCrew.EditorTools.LevelAssetValidator", false);
            }

            Assert.That(type, Is.Not.Null,
                "找不到关卡资产校验器（PirateCrew.EditorTools.LevelAssetValidator）——被改名或删了？");

            System.Reflection.MethodInfo validate = type.GetMethod(
                "Validate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.That(validate, Is.Not.Null, "校验器缺少静态 Validate()");
            return validate.Invoke(null, null);
        }

        static string JoinToList(System.Collections.IList items)
        {
            var lines = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
                lines.Add(System.Convert.ToString(items[i]));
            return string.Join("\n", lines);
        }

        [Test]
        public void WorldMapCatalog_ReadsFromAssets_NotFromCode()
        {
            // 海图目录现在只是"资产 → 运行时定义"的查表口；条数对不上说明加载链断了。
            Assert.That(WorldMapCatalog.Count, Is.EqualTo(8));
            Assert.That(WorldMapCatalog.TryGet("wreck_hymn", out WorldMapDefinition wreck), Is.True);
            Assert.That(wreck.SpanX, Is.EqualTo(150f));
            Assert.That(WorldMapCatalog.TryGet("no_such_map", out _), Is.False);
        }
    }
}
#endif
