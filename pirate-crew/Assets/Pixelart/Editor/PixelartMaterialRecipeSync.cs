using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 把 <see cref="PixelartMaterialFactory.Configure"/> 的配方**推平到既有材质资产**。
    ///
    /// 【为什么需要这一步】配方活在代码常量里，而 `.mat` 资产里存的是**序列化下来的旧值**：
    /// 改了配方却不重跑装配器，既有资产照旧吃旧值——症状是"改了代码画面没变化"，而且
    /// **试点场景（材质是资产）与游戏本体（材质由 <c>PixelartContentConverter</c> 运行期派生）
    /// 会各是各的**。这是"配方是唯一事实源"这条口径缺的那一半：
    /// 事实源改了，得有一个把它推平到资产的入口。
    ///
    /// 【逐物体那几项保留资产自己的值】`_BaseColor`（albedo）、`_MainLightLevel`（色带档数）、
    /// `_OutlinePixels`（描边开关）是**逐预设**的：装配器给不同件传不同档数
    /// （角色 3 档、地面 2 档…），不是配方默认值，不能被推平掉。其余一律按配方覆写。
    /// </summary>
    public static class PixelartMaterialRecipeSync
    {
        [MenuItem("PirateCrew/Pixelart/重新应用物体材质配方（全仓 .mat）")]
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material");
            var skipped = new List<string>();
            int synced = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || material.shader == null
                        || material.shader.name != PixelartPath.ObjectShaderName)
                        continue;

                    if (!PixelartMaterialFactory.TryGetAlbedo(material, out Color albedo))
                    {
                        skipped.Add(material.name);
                        continue;
                    }

                    float bandCount = material.GetFloat("_MainLightLevel");
                    float outlinePixels = material.GetFloat("_OutlinePixels");
                    PixelartMaterialFactory.Configure(material, albedo, bandCount, outlinePixels);
                    EditorUtility.SetDirty(material);
                    synced++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[PixelartMaterialRecipeSync] 已按配方重写 " + synced + " 个物体材质资产"
                + (skipped.Count > 0
                    ? "；跳过 " + skipped.Count + " 个取不到色的（" + string.Join("、", skipped) + "）"
                    : "。"));
        }
    }
}
