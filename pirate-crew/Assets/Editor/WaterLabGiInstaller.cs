using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PirateCrew.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// WaterLab GI 复刻（I3a）装配辅助：把 <see cref="ReplicaGiFeature"/>（随
    /// WaterLabGi.compute 引用）幂等注入现役 URP 渲染器资产。手法沿
    /// WaterLabSetup.EnsureWhitewaterFeature（子资产 + m_RendererFeatures/
    /// m_RendererFeatureMap 同步补位）；本文件是独立新文件，不改 Setup.cs（I2 域）——
    /// I2/I6b 收口集成时可把本段并入 WaterLabSetup，菜单入口仅为过渡期手动装配。
    /// </summary>
    internal static class WaterLabGiInstaller
    {
        const string ComputePath = "Assets/Art/Shaders/WaterLab/WaterLabGi.compute";
        const string Menu = "PirateCrew/WaterLab/装配 GI Feature（I3a）";

        /// <summary>渲染器资产与 EnsureWhitewaterFeature 同单（WaterLab 取景跟随质量档）。</summary>
        static readonly string[] RendererPaths =
        {
            "Assets/Settings/URP/PC_Balanced_Renderer.asset",
            "Assets/Settings/URP/PC_Performant_Renderer.asset",
        };

        [MenuItem(Menu, false, 40)]
        public static void Install()
        {
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            if (compute == null)
            {
                Debug.LogError($"[WaterLabGi] 找不到 compute：{ComputePath}（先让 Unity 导入新资产）");
                return;
            }

            foreach (var path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null)
                {
                    Debug.LogError($"[WaterLabGi] 找不到渲染器资产：{path}");
                    continue;
                }

                bool found = false;
                foreach (var existing in data.rendererFeatures.OfType<ReplicaGiFeature>())
                {
                    found = true;
                    // compute 后到：已有 Feature 但没挂引用（如早前轮次注入）也补上
                    if (existing.GiCompute == null)
                    {
                        existing.GiCompute = compute;
                        EditorUtility.SetDirty(existing);
                    }
                }
                if (found)
                    continue;

                var feature = ScriptableObject.CreateInstance<ReplicaGiFeature>();
                feature.name = "ReplicaGi";
                feature.GiCompute = compute;
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.ImportAsset(path); // 子资产登记后才有 localId（map 语义 = localId）

                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                list.GetArrayElementAtIndex(idx).objectReferenceValue = feature;
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue =
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId)
                        ? localId : 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log($"[WaterLabGi] 已挂 ReplicaGiFeature：{path}");
            }

            AssetDatabase.SaveAssets();
        }
    }
}
