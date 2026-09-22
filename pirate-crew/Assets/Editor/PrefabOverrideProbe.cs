using System.Text;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 折叠态排障探针（临时）：打开 Battle 场景，用引擎 <see cref="PrefabUtility.GetPropertyModifications"/>
    /// 把所有被判定为覆盖的属性逐条 dump 到工程根 export/prefab-override-probe.txt。
    /// 用完即删，不入库。
    /// </summary>
    public static class PrefabOverrideProbe
    {
        public static void Dump()
        {
            var sb = new StringBuilder();
            DumpScene("Assets/Scenes/Battle.unity", sb);
            string projectRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(projectRoot, "export/prefab-override-probe.txt"),
                sb.ToString());
            Debug.Log("[PrefabOverrideProbe] dumped -> export/prefab-override-probe.txt");
        }

        static void DumpScene(string scenePath, StringBuilder sb)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                GameObject root = scene.GetRootGameObjects()[0];
                sb.AppendLine("=== " + scenePath + " root=" + root.name
                    + " overrides=" + PrefabUtility.HasPrefabInstanceAnyOverrides(root, false));

                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(node.gameObject))
                        continue;
                    foreach (var ov in PrefabUtility.GetPropertyModifications(node.gameObject))
                    {
                        if (ov.propertyPath.StartsWith("m_LocalPosition")
                            || ov.propertyPath.StartsWith("m_LocalRotation")
                            || ov.propertyPath.StartsWith("m_LocalEulerAnglesHint")
                            || ov.propertyPath == "m_Name"
                            || ov.propertyPath.StartsWith("m_RootOrder"))
                            continue;   // 实例化自动记录的等值项
                        string value = ov.value != null ? ov.value.ToString() : "null";
                        sb.AppendLine("  [" + node.name + "] " + ov.propertyPath + " = " + value);
                    }
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
