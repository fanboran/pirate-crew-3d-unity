using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// M4 世界套件（WorldKit）换装管线：扫描 <c>Assets/Art/Models/WorldKit/**</c> 的 FBX，
    /// 按踩坑参数导入（<c>SceneKitPilotSetup</em> 同款：useFileScale=false / bakeAxisConversion=true /
    /// materialImportMode=None），按 <c>Kit_*</c> 槽名生成 URP 材质并写回 renderer，
    /// 生成 <see cref="PirateCrew.Battle.WorldMaps.WorldMapAssetSet"/> 资产
    /// （资产名 → FBX 根对象引用），最后把它赋给 Battle.unity 的 BattleController 字段。
    ///
    /// 入口（菜单 / 无头）：<c>PirateCrew.EditorTools.WorldMapAssetSetBuilder.BuildAll</c>。
    /// 槽位表与 <c>tools/blender/scene/style_tokens.py</c> 的 SLOTS 同源——
    /// 未知 Kit_ 槽仍用品红暴露（零容忍纪律）。
    /// </summary>
    public static class WorldMapAssetSetBuilder
    {
        const string WorldKitRoot = "Assets/Art/Models/WorldKit";
        const string MaterialsDir = WorldKitRoot + "/Materials";
        const string AssetSetPath = WorldKitRoot + "/WorldMapAssetSet.asset";
        const string BattleScenePath = "Assets/Scenes/Battle.unity";

        /// <summary>Kit_ 槽位表（hex / smoothness / metallic；与 style_tokens.SLOTS 同源）。</summary>
        static readonly (string name, string hex, float smoothness, float metallic, bool doubleSided, bool emissive)[]
            Slots =
            {
                ("Kit_WoodLight", "#D4A76A", 0.28f, 0f, false, false),
                ("Kit_WoodMid", "#A67B42", 0.28f, 0f, false, false),
                ("Kit_WoodDark", "#6B4C28", 0.26f, 0f, false, false),
                ("Kit_SandLight", "#E8D5A3", 0.18f, 0f, false, false),
                ("Kit_SandMid", "#C4A76A", 0.18f, 0f, false, false),
                ("Kit_SandDark", "#8B7355", 0.18f, 0f, false, false),
                ("Kit_WetSand", "#5C4A34", 0.40f, 0f, false, false),
                ("Kit_GrassLight", "#7BC67E", 0.14f, 0f, true, false),
                ("Kit_GrassMid", "#4A8C4A", 0.14f, 0f, true, false),
                ("Kit_GrassDark", "#2D5A2D", 0.14f, 0f, true, false),
                ("Kit_RockLight", "#B8A99A", 0.22f, 0f, false, false),
                ("Kit_RockMid", "#8C7B6A", 0.22f, 0f, false, false),
                ("Kit_RockDark", "#5C4F42", 0.22f, 0f, false, false),
                ("Kit_Iron", "#6E6A63", 0.42f, 0.85f, false, false),
                ("Kit_Brass", "#C9A227", 0.50f, 1f, false, false),
                ("Kit_Sail", "#F5E8C8", 0.12f, 0f, true, false),
                ("Kit_Rope", "#8A6F4D", 0.22f, 0f, true, false),
                ("Kit_Ember", "#FFB347", 0.10f, 0f, false, true),
                ("Kit_Coral", "#E8845A", 0.30f, 0f, false, false),
                ("Kit_FarNear", "#7E93A8", 0.10f, 0f, false, false),
                ("Kit_FarFar", "#AFC2D4", 0.10f, 0f, false, false),
                ("Kit_Cloud", "#FFFFFF", 0.10f, 0f, false, false),
                ("Kit_FarSail", "#E8E8E0", 0.10f, 0f, false, false),
            };

        [MenuItem("Tools/PirateCrew/WorldKit/Build Asset Set")]
        public static void BuildAllMenu() => BuildAll();

        /// <summary>无头入口：导入 → 材质 → 资产表 → 接线 Battle 场景。</summary>
        public static void BuildAll()
        {
            if (!Directory.Exists(WorldKitRoot))
            {
                Debug.LogError("[WorldMapAssetSetBuilder] 找不到 " + WorldKitRoot);
                return;
            }

            var fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { WorldKitRoot });
            if (fbxGuids.Length == 0)
            {
                Debug.LogError("[WorldMapAssetSetBuilder] WorldKit 下没有模型资产");
                return;
            }

            var set = ScriptableObject.CreateInstance<WorldMapAssetSet>();
            int imported = 0;

            foreach (string guid in fbxGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ApplyImportSettings(path))
                    continue;
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null)
                    continue;
                ApplyKitMaterials(root);
                string assetName = Path.GetFileNameWithoutExtension(path);
                set.Set(assetName, root);
                imported++;
            }

            Directory.CreateDirectory(MaterialsDir);
            foreach (var slot in Slots)
                GetOrCreateMaterial(slot.name);

            if (AssetDatabase.LoadAssetAtPath<Object>(AssetSetPath) != null)
                AssetDatabase.DeleteAsset(AssetSetPath);
            AssetDatabase.CreateAsset(set, AssetSetPath);
            AssetDatabase.SaveAssets();

            WireBattleScene(set);
            Debug.Log(string.Format(
                "[WorldMapAssetSetBuilder] 完成：导入/换装 {0} 件 → {1} → Battle 场景已接线", imported, AssetSetPath));
        }

        static bool ApplyImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                return false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.useFileUnits = true;
            // 【本管线实测 2026-09-17】Blender FBX_SCALE_NONE 导出会把 (100,100,100) 挂在 FBX 根节点上
            // （UnitScaleFactor=100 的补偿量）。useFileScale=true 时 Unity 走"换算单位"路径把它吃掉，
            // 得到 1:1 米制（实测断舷船 6.2×17×6.4 ✓）；=false 则节点缩放原样进来，模型放大 ×100。
            // 旧注释"true 会 ×0.01"来自另一种导出模式（缩放已烘进顶点），对本管线不适用。
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;     // 不设模型躺倒 -90°（实测踩坑）
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            return true;
        }

        static void ApplyKitMaterials(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                ApplyKitMaterials(renderer);
        }

        static void ApplyKitMaterials(Renderer renderer)
        {
            var mats = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                string slot = mats[i] != null ? mats[i].name : null;
                if (!string.IsNullOrEmpty(slot) && slot.StartsWith("Kit_"))
                {
                    mats[i] = FindSlotMaterial(slot) ?? MakeMagenta(slot);
                    changed = true;
                }
                else if (mats[i] == null)
                {
                    mats[i] = MakeMagenta("Kit_Fallback");
                    changed = true;
                }
            }
            if (changed)
                renderer.sharedMaterials = mats;
        }

        static Material FindSlotMaterial(string slot)
        {
            foreach (var s in Slots)
                if (s.name == slot)
                    return GetOrCreateMaterial(s.name);
            return null;
        }

        static Material GetOrCreateMaterial(string slotName)
        {
            string path = MaterialsDir + "/" + slotName + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            foreach (var s in Slots)
            {
                if (s.name != slotName)
                    continue;
                var mat = MakeMaterial(s.hex, s.smoothness, s.metallic, s.doubleSided, s.emissive);
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }
            return null;
        }

        static Material MakeMaterial(string hex, float smoothness, float metallic, bool doubleSided, bool emissive)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color color;
            if (!ColorUtility.TryParseHtmlString(hex, out color))
                color = Color.magenta;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (doubleSided)
            {
                mat.SetFloat("_Cull", 0f); // 双面（叶/草/帆/绳为单层面片）
            }
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.6f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return mat;
        }

        static Material MakeMagenta(string slot)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", Color.magenta);
            mat.name = slot;
            return mat;
        }

        /// <summary>把资产表赋给 Battle.unity 的 BattleController.worldMapAssetSet（场景 bake 重建后重跑本方法即可）。</summary>
        static void WireBattleScene(WorldMapAssetSet set)
        {
            if (!File.Exists(BattleScenePath))
            {
                Debug.LogError("[WorldMapAssetSetBuilder] 找不到 " + BattleScenePath);
                return;
            }
            var scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            var controller = Object.FindObjectOfType<BattleController>();
            if (controller == null)
            {
                Debug.LogError("[WorldMapAssetSetBuilder] Battle 场景里没有 BattleController");
                return;
            }
            var field = typeof(BattleController)
                .GetField("worldMapAssetSet",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogError("[WorldMapAssetSetBuilder] worldMapAssetSet 字段不存在");
                return;
            }
            field.SetValue(controller, set);

            // 大海域海面材质：**不再接线**（新管线口径，创始人 2026-09-22「以后走新管线」）。
            //
            // 【为什么这里必须留空而不是接 Ocean_Water.mat】那个材质用的是 `PirateCrew/Ocean`：
            // 半透明（Queue=Transparent）。像素化路径的几何只有"不透明数据 + 全屏着色"，放不下混合，
            // 于是半透明内容全被交给**叠加档**（`PixelartOverlay_Renderer` + 叠加相机）画在成图之上——
            // 也就是说，接上它 = 一层全屏的旧水盖住整个新管线画面（实测：屏幕上看不到任何场地与单位，
            // 全是旧管线的水）。留空后 `OceanRig` 会在运行期用 `PixelartMaterialFactory` 造
            // **不透明替身海面**（平色 + 色带，与海图试点场景的替身同色同档）。
            //
            // 【为什么它必须在这里清，而不是在 BattleSceneSetup 里清】本步（装配链第 ⑦ 步）跑在
            // BattleSceneSetup 的 BuildAll（第 ① 步）**之后**：在 ① 里清、⑦ 里再写回，等于没清
            // （实测踩过：prefab 里仍是 Ocean_Water.mat）。
            var oceanField = typeof(BattleController).GetField("worldOceanMaterial",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (oceanField == null)
                Debug.LogError("[WorldMapAssetSetBuilder] worldOceanMaterial 字段不存在");
            else if (oceanField.GetValue(controller) != null)
            {
                oceanField.SetValue(controller, null);
                Debug.Log("[WorldMapAssetSetBuilder] 已清空 worldOceanMaterial（旧半透明海洋会盖住新管线画面；"
                    + "海面改由 OceanRig 运行期造不透明替身）。");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
