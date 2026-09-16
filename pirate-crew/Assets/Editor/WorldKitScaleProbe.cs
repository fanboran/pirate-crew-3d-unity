using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>临时诊断：打印 WorldKit 若干 FBX 导入后的实际包围盒（对照 Blender STAT 的米制尺寸）。</summary>
    public static class WorldKitScaleProbe
    {
        [MenuItem("Tools/PirateCrew/WorldKit/Scale Probe")]
        public static void Run()
        {
            foreach (var path in new[]
            {
                "Assets/Art/Models/WorldKit/Marine/WreckBowHalf.fbx",
                "Assets/Art/Models/WorldKit/Archipelago/TerraceIslandL.fbx",
                "Assets/Art/Models/WorldKit/Marine/LighthouseTower.fbx",
            })
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) { Debug.LogError("缺失 " + path); continue; }
                var renderers = root.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                Debug.Log("[ScaleProbe] " + path + " 本地缩放=" + root.transform.localScale
                    + " 世界包围盒=" + bounds.size.ToString("F2"));
            }
        }
    }
}
