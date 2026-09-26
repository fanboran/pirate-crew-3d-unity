using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// Aseprite widgets 声明资产同步（调试对话框装载器的资产侧）：
    /// external/aseprite-ref/data/widgets/*.xml 与 data/strings/en.ini →
    /// Assets/Resources/AseWidgets/（en.ini 落成 en.ini.txt——.ini 不是 TextAsset 扩展名）。
    ///
    /// 【原封不动纪律】拷贝不做任何内容改写——「以库为源」的复刻要求声明文件与仓库逐字节一致；
    /// 装载器（AseDialogLoader）在运行时解读。.meta 只在缺失时生成（GUID 稳定，重跑不漂移）。
    /// </summary>
    public static class AseWidgetAssetSync
    {
        const string SourceDir = "../external/aseprite-ref/data/widgets";
        const string StringsSource = "../external/aseprite-ref/data/strings/en.ini";
        // theme.xml：状态层匹配引擎（AseThemeLayers）的权威数据。源取工程自己的
        // Assets/Art/Sprites/UI/Aseprite/theme.xml（与参考库 dark 主题逐字节一致，
        // git 4d2dc0bf 引入），落成 theme.xml.txt（.xml 不是 TextAsset 扩展名）。
        const string ThemeSource = "Assets/Art/Sprites/UI/Aseprite/theme.xml";
        const string TargetDir = "Assets/Resources/AseWidgets";

        [MenuItem("PirateCrew/同步 Aseprite widgets 资产")]
        public static void Sync()
        {
            Directory.CreateDirectory(TargetDir);
            int copied = 0;

            foreach (string xml in Directory.GetFiles(Path.GetFullPath(SourceDir), "*.xml"))
            {
                string dest = Path.Combine(TargetDir, Path.GetFileName(xml));
                File.Copy(xml, dest, overwrite: true);
                EnsureMeta(dest);
                copied++;
            }

            string stringsDest = Path.Combine(TargetDir, "en.ini.txt");
            File.Copy(Path.GetFullPath(StringsSource), stringsDest, overwrite: true);
            EnsureMeta(stringsDest);

            string themeDest = Path.Combine(TargetDir, "theme.xml.txt");
            File.Copy(Path.GetFullPath(ThemeSource), themeDest, overwrite: true);
            EnsureMeta(themeDest);

            EnsureFolderMeta(TargetDir);
            AssetDatabase.Refresh();
            Debug.Log("[AseWidgetAssetSync] 同步完成：" + copied + " 个 widgets xml + en.ini.txt + theme.xml.txt → "
                + TargetDir);
        }

        static void EnsureMeta(string assetPath)
        {
            string meta = assetPath + ".meta";
            if (File.Exists(meta))
                return;
            File.WriteAllText(meta, "fileFormatVersion: 2\nguid: " + NewGuid()
                + "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n"
                + "  assetBundleName: \n  assetBundleVariant: \n", new UTF8Encoding(true));
        }

        static void EnsureFolderMeta(string folderPath)
        {
            string meta = folderPath + ".meta";
            if (File.Exists(meta))
                return;
            File.WriteAllText(meta, "fileFormatVersion: 2\nguid: " + NewGuid()
                + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n"
                + "  assetBundleName: \n  assetBundleVariant: \n", new UTF8Encoding(true));
        }

        static string NewGuid() => System.Guid.NewGuid().ToString("N");
    }
}
