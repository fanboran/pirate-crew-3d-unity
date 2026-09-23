using System.Collections.Generic;
using System.IO;
using PirateCrew.Data;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 把纯 C# 目录表（WeaponCatalog / CrewCatalog / BalanceConfig.Defaults）
    /// 写进 ScriptableObject <c>.asset</c>，供 Unity 侧引用与策划调参。
    /// 一代退场后不再生成关卡资产（<c>Assets/Data/Levels/</c> 遗留资产已失去消费方，
    /// 手工删除即可；世界海图走 <c>WorldMapCatalog</c> 纯 C# 目录，无 SO 资产）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Data/生成数值资产
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.DataAssetGenerator.GenerateAll
    ///
    /// 【产物】
    ///   Assets/Data/Weapons/*.asset          17 件武器
    ///   Assets/Data/Crews/*.asset            每种海盗符号一个（属性共享，仅种类/队伍不同）
    ///   Assets/Data/Balance/BalanceConfig.asset
    ///
    /// 【幂等】同名资产已存在时复用该实例并覆盖字段，不产生重名副本。
    /// 【单一来源】数值全部从 Catalog / BalanceConfig.Defaults 读取并调用各自的 Apply* 方法灌入，
    ///             本生成器内不出现第二份硬编码数值副本。
    /// </summary>
    public static class DataAssetGenerator
    {
        const string RootFolder = "Assets/Data";
        const string WeaponsFolder = RootFolder + "/Weapons";
        const string CrewsFolder = RootFolder + "/Crews";
        const string BalanceFolder = RootFolder + "/Balance";

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Data/生成数值资产")]
        public static void GenerateAll()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(WeaponsFolder);
            EnsureFolder(CrewsFolder);
            EnsureFolder(BalanceFolder);

            var manifest = new List<string>();
            int weaponCount = GenerateWeapons(manifest);
            int crewCount = GenerateCrews(manifest);
            GenerateBalance(manifest);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[DataAssetGenerator] 数值资产生成完成："
                + weaponCount + " 件武器 / " + crewCount + " 名船员 / 1 份平衡常数。\n"
                + string.Join("\n", manifest));
        }

        // ------------------------------------------------------------------
        // 武器
        // ------------------------------------------------------------------

        static int GenerateWeapons(List<string> manifest)
        {
            IReadOnlyList<WeaponStats> all = WeaponCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                WeaponStats stats = all[i];
                string path = WeaponsFolder + "/" + stats.DisplayName + ".asset";
                WeaponDefinition asset = EnsureAsset<WeaponDefinition>(path);
                asset.Apply(stats);
                EditorUtility.SetDirty(asset);
                manifest.Add("  [武器] " + path);
            }
            return all.Count;
        }

        // ------------------------------------------------------------------
        // 船员（§4.2 每个导出符号一个；属性来自 §4.1 共享值）
        // ------------------------------------------------------------------

        static int GenerateCrews(List<string> manifest)
        {
            IReadOnlyList<string> symbols = CrewCatalog.ExportSymbols;
            for (int i = 0; i < symbols.Count; i++)
            {
                string symbol = symbols[i];
                string path = CrewsFolder + "/" + symbol + ".asset";
                CrewDefinition asset = EnsureAsset<CrewDefinition>(path);
                // 初始武器因关卡而异（见 LevelUnit.initialWeapons），故 asset 层留空栈。
                asset.Apply(symbol, CrewCatalog.TeamIndexOf(symbol), CrewCatalog.SharedStats, new List<WeaponStack>());
                EditorUtility.SetDirty(asset);
                manifest.Add("  [船员] " + path);
            }
            return symbols.Count;
        }

        // ------------------------------------------------------------------
        // 平衡常数
        // ------------------------------------------------------------------

        static void GenerateBalance(List<string> manifest)
        {
            string path = BalanceFolder + "/BalanceConfig.asset";
            BalanceConfig asset = EnsureAsset<BalanceConfig>(path);
            asset.ApplyDefaults();
            EditorUtility.SetDirty(asset);
            manifest.Add("  [平衡] " + path);
        }

        // ------------------------------------------------------------------
        // 幂等资产生成辅助
        // ------------------------------------------------------------------

        /// <summary>路径已有资产则复用，否则新建并落盘。返回可继续覆盖字段的实例。</summary>
        static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>确保文件夹存在：父目录不存在时先递归建父目录，再建自己。</summary>
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            // CreateFolder 不建中间层级：调用方若直接传 "Assets/A/B/C"，父目录不存在就会静默失败。
            // 本类的 GenerateAll 恰好按父→子顺序调用，但递归一遍更稳，也免得日后有人单独调用踩坑。
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
