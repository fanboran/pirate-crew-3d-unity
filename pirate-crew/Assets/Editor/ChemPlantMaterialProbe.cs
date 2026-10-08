using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 化工厂两关「关4 灰盒」取证：把两份 FBX 的**内嵌材质子资产**（ImportStandard 产物，
    /// 战斗路径 RuntimeSceneArt 直接实例化模型预制时 renderer 拿到的就是它们）逐个打印
    /// 名字 + shader + _BaseColor 到 export/chemplant-materials.txt。
    /// 判据：关4/关5 的 Kit_* 基色若一边白一边有色 → 颜色差在 FBX 内嵌材质本身。
    /// 无头 -executeMethod 直接跑（不用进 Play）。
    /// </summary>
    public static class ChemPlantMaterialProbe
    {
        static readonly (string label, string path)[] Files =
        {
            ("关4 ChemPlant_Level.fbx", "Assets/Art/Models/WorldKit/ChemPlant/ChemPlant_Level.fbx"),
            ("关5 SceneKit/ChemPlant.fbx", "Assets/Art/Models/SceneKit/ChemPlant.fbx"),
        };

        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach ((string label, string path) in Files)
            {
                sb.AppendLine("==== " + label + " ====");
                Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
                int matCount = 0;
                foreach (Object o in all)
                {
                    if (o is Material m)
                    {
                        matCount++;
                        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.magenta;
                        sb.AppendLine("  材质 " + m.name
                            + "  shader=" + (m.shader != null ? m.shader.name : "<null>")
                            + "  _BaseColor=(" + c.r.ToString("F3") + "," + c.g.ToString("F3")
                            + "," + c.b.ToString("F3") + ")");
                    }
                }
                sb.AppendLine("  —— 内嵌材质总数 " + matCount);

                // 模型预制实例化后的 renderer 槽位实况
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    GameObject inst = Object.Instantiate(prefab);
                    int slot = 0;
                    foreach (MeshRenderer r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        foreach (Material m in r.sharedMaterials)
                        {
                            slot++;
                            if (slot <= 20)
                                sb.AppendLine("  槽#" + slot + " " + r.name + " → "
                                    + (m != null ? m.name : "<null>"));
                        }
                    }
                    sb.AppendLine("  —— renderer 槽位总数 " + slot);
                    Object.DestroyImmediate(inst);
                }
                else
                {
                    sb.AppendLine("  <模型预制读不到>");
                }
            }

            string outPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), "..", "export", "chemplant-materials.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[ChemPlantMaterialProbe] 转储 → " + outPath);
        }
    }
}
