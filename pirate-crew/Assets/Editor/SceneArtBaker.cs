using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PirateCrew.Battle;
using PirateCrew.EditorTools.BuildSystem;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 样板场景件的**接线收口器**：两关岛体（云端漫步 / 天空之岛）走 Blender 无头建模管线
    /// （<c>tools/blender/scene/islands/</c>，岛形驱动——顶面逐点复刻关卡 terrain 轮廓，
    /// 视觉与逻辑同源），本类只负责把出库 FBX 的导入参数显式化并写进 Battle 场景的
    /// <see cref="RuntimeSceneArt"/> 序列化引用。程序化几何烘焙（云场/岛壳）已随岛体
    /// Blender 化退役；像素化落色由运行时 <c>PixelartContentConverter</c> 按源材质取色就地换血
    /// （与化工厂整场件同一条链）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/烘焙/样板场景件（岛体接线）
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.SceneArtBaker.BuildAll
    /// </summary>
    public static class SceneArtBaker
    {
        /// <summary>Blender 手作件目录（岛体 FBX 与化工厂整场件同目录）。</summary>
        const string BakeFolder = "Assets/Art/Models/SceneKit";

        static string BattleScenePath = BuildScenes.PathOf("Battle");

        [MenuItem("PirateCrew/烘焙/样板场景件（岛体接线）")]
        public static void BuildAll()
        {
            EnsureModelImporter(BakeFolder + "/CloudWalk.fbx");
            BakeCloudWalkPrefab();
            WireBattleScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SceneArtBaker] 样板场景件接线完成：云场 Blender 件（CloudWalk）→ Battle 场景。");
        }

        /// <summary>
        /// 云场翻译件 prefab 化：实例化 CloudWalk.fbx 后把 MeshRenderer 的内嵌材质替换为
        /// 旧运行时同款材质资产（Assets/Art/Materials/Lowpoly/Lowpoly_*.mat）——几何是
        /// C# 生成器的忠实翻译，材质也必须走**同一份** Pixelart 三档色资产，而不是让
        /// PixelartContentConverter 对 FBX 内嵌 Standard 材质自动派生（两条派生路径的
        /// 三档色不同，观感会漂：内嵌路径渲染成饱和金，旧资产渲染近白）。
        /// FBX 子节点名 = 槽名（WarmWhite / PaleGold），内嵌材质名与旧槽同名。
        /// </summary>
        static void BakeCloudWalkPrefab()
        {
            const string ModelPath = BakeFolder + "/CloudWalk.fbx";
            const string PrefabPath = BakeFolder + "/CloudWalk.prefab";
            const string MaterialFolder = "Assets/Art/Materials/Lowpoly";

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError("[SceneArtBaker] 缺 " + ModelPath
                    + "（先跑 tools/blender/scene/islands/translate_cloudfield.py 出库）");
                return;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            int swapped = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material source = renderer.sharedMaterial;
                if (source == null)
                    continue;
                Material same = AssetDatabase.LoadAssetAtPath<Material>(
                    MaterialFolder + "/" + source.name + ".mat");
                if (same == null)
                {
                    Debug.LogError("[SceneArtBaker] 找不到同源材质 " + MaterialFolder + "/"
                        + source.name + ".mat——翻译件材质名必须与旧运行时槽同名。");
                    continue;
                }
                renderer.sharedMaterial = same;
                swapped++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[SceneArtBaker] CloudWalk.prefab 已烘（材质同源替换 " + swapped + " 个渲染器）。");
        }

        /// <summary>
        /// 岛体 FBX 导入参数显式化（幂等），与 ChemPlant.fbx 同口径：
        /// ImportStandard（None 读不到内嵌材质，ContentConverter 取不了色）、
        /// useFileScale（吃 FBX 根单位层）、bakeAxisConversion（不设模型躺倒 -90°）。
        /// </summary>
        public static void EnsureModelImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[SceneArtBaker] 岛体件缺失: " + path
                    + "（先跑 tools/blender/scene/islands/ 的 build 脚本出库）");
                return;
            }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard
                || !importer.useFileScale || !importer.bakeAxisConversion)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------
        // 场景接线（把岛体件写进 Battle.unity 的 RuntimeSceneArt）
        // ------------------------------------------------------------------

        /// <summary>
        /// 打开 Battle 场景，把岛体 FBX 写进 <see cref="RuntimeSceneArt"/> 的序列化引用后存盘。
        /// <c>BattleSceneSetup.WireRuntimeSceneArt</c> 也会按同路径装载（资产缺失时留 null），
        /// 故本步与场景重建的先后次序无关紧要；这里是显式收口。
        /// </summary>
        static void WireBattleScene()
        {
            var scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            RuntimeSceneArt sceneArt = Object.FindObjectOfType<RuntimeSceneArt>();
            if (sceneArt == null)
            {
                Debug.LogError("[SceneArtBaker] Battle 场景里没有 RuntimeSceneArt（先跑 BattleSceneSetup.BuildAll）。");
                return;
            }

            var so = new SerializedObject(sceneArt);
            SetPrefabRef(so, "cloudFieldPrefab", BakeFolder + "/CloudWalk.prefab");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void SetPrefabRef(SerializedObject so, string fieldName, string path)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError("[SceneArtBaker] RuntimeSceneArt 找不到序列化字段 " + fieldName + "（字段名漂移？）");
                return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogError("[SceneArtBaker] 岛体件缺失: " + path);
            prop.objectReferenceValue = prefab;
        }
    }
}
