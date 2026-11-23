using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 合并化工厂整场件（`SceneKit/ChemPlantMerged.fbx`）的导入设置。
    ///
    /// 与两套旧 kit 的装配器（<c>PixelartChemPlantSetup</c> / <c>PixelartChemPlantTeamSetup</c>）
    /// 逐项同值——这三条是实测踩出来的，别照抄别的 kit：
    ///   · <c>materialImportMode = ImportStandard</c>（不是 None：None 读不到 <c>Kit_</c> 槽名，会串色）；
    ///   · <c>useFileScale = true</c>（吃掉 FBX 根上的 100×）；
    ///   · <c>bakeAxisConversion = true</c>（Blender Z-up → Unity Y-up，不设模型躺倒 -90°）。
    /// </summary>
    public static class ChemPlantMergedImport
    {
        public const string ModelPath = "Assets/Art/Models/SceneKit/ChemPlantMerged.fbx";

        /// <summary>把导入设置对齐；已有正确设置时不重导入（Battle 装配链每次都会调，避免无谓重导）。</summary>
        public static void EnsureImportSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[ChemPlantMergedImport] 找不到模型 " + ModelPath
                    + "——先确认 Blender 总装已导出（assemble_chemplant_merged.py）。");
                return;
            }

            bool changed = importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard
                || !importer.useFileUnits
                || !importer.useFileScale
                || Mathf.Abs(importer.globalScale - 1f) > 1e-4f
                || !importer.bakeAxisConversion
                || importer.importAnimation
                || importer.importBlendShapes
                || importer.importCameras
                || importer.importLights;
            if (!changed)
                return;

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.useFileUnits = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            Debug.Log("[ChemPlantMergedImport] 导入设置已应用：" + ModelPath);
        }

        [MenuItem("PirateCrew/烘焙/化工厂合并件导入设置")]
        static void ApplyImportSettingsMenu()
        {
            EnsureImportSettings();
        }
    }
}
