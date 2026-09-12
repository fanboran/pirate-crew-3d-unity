using PirateCrew.PirateCrew.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 把 <see cref="OutlineRendererFeature"/> 程序化挂到 URP 的两个 Renderer 资产上。
    ///
    /// 【背景】描边代码（Scripts/PirateCrew/Rendering/OutlineRendererFeature.cs）与
    ///         PirateOutlinePost.shader 已就位，但没有任何 Renderer 资产引用它，
    ///         因此 URP 后处理描边实际不生效——本脚本补上这个挂载缺口。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Rendering/挂载描边 RendererFeature
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.M2UrpRendererFeatureSetup.SetupAll
    ///
    /// 【幂等】Renderer 上已存在 OutlineRendererFeature 时只补齐 shader 引用并跳过，
    ///         不重复 AddObjectToAsset / 不重复入列。
    ///
    /// 【程序集】本脚本在 Assets/Editor（Assembly-CSharp-Editor），能引用 autoReferenced 的
    ///           PirateCrew.Rendering 程序集（其 asmdef 已引用 URP Core/Universal Runtime）；
    ///           未修改任何 .asmdef。
    /// </summary>
    public static class M2UrpRendererFeatureSetup
    {
        const string PerformantRendererPath = "Assets/Settings/URP/PC_Performant_Renderer.asset";
        const string BalancedRendererPath = "Assets/Settings/URP/PC_Balanced_Renderer.asset";
        const string OutlinePostShaderPath = "Assets/Art/Shaders/PirateOutlinePost.shader";
        const string FeatureName = "PirateCrew Outline";

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Rendering/挂载描边 RendererFeature")]
        public static void SetupAll()
        {
            int added = 0;
            added += SetupRenderer(PerformantRendererPath);
            added += SetupRenderer(BalancedRendererPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M2UrpRendererFeatureSetup] 描边 RendererFeature 挂载完成：新增 " + added
                + " 个（其余为已存在，幂等跳过）。\n"
                + "  " + PerformantRendererPath + "\n"
                + "  " + BalancedRendererPath);
        }

        /// <summary>挂载单个 Renderer；返回新增的 Feature 数量（0 = 已存在或资产缺失）。</summary>
        static int SetupRenderer(string path)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (rendererData == null)
            {
                Debug.LogWarning("[M2UrpRendererFeatureSetup] 未找到 Renderer 资产: " + path
                    + "（可先执行 PirateCrew.EditorTools.UrpSetup.Configure 创建）。");
                return 0;
            }

            // 幂等：已存在同类 Feature 时只补齐 shader 引用。
            var features = rendererData.rendererFeatures;
            if (features != null)
            {
                for (int i = 0; i < features.Count; i++)
                {
                    if (features[i] is OutlineRendererFeature existing)
                    {
                        EnsureShader(existing);
                        EditorUtility.SetDirty(rendererData);
                        Debug.Log("[M2UrpRendererFeatureSetup] " + path + " 已存在 OutlineRendererFeature，跳过。");
                        return 0;
                    }
                }
            }

            var feature = ScriptableObject.CreateInstance<OutlineRendererFeature>();
            feature.name = FeatureName;
            EnsureShader(feature);
            feature.SetActive(true);

            // 作为 Renderer 资产的子资产保存，引用才能随资产持久化（否则重开工程会丢引用）。
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            features.Add(feature);
            EditorUtility.SetDirty(rendererData);
            Debug.Log("[M2UrpRendererFeatureSetup] 已挂载 OutlineRendererFeature -> " + path);
            return 1;
        }

        /// <summary>补齐描边后处理 shader 引用（留空时 Feature 会退化为 Shader.Find，出包有被剔除风险）。</summary>
        static void EnsureShader(OutlineRendererFeature feature)
        {
            if (feature.settings == null)
                feature.settings = new OutlineRendererFeature.OutlineSettings();

            if (feature.settings.outlinePostShader == null)
                feature.settings.outlinePostShader = AssetDatabase.LoadAssetAtPath<Shader>(OutlinePostShaderPath);

            if (feature.settings.outlinePostShader == null)
                Debug.LogWarning("[M2UrpRendererFeatureSetup] 未找到描边 shader: " + OutlinePostShaderPath
                    + "，运行时会回退到 Shader.Find(\"" + OutlineRendererFeature.OutlinePostShaderName + "\")。");

            EditorUtility.SetDirty(feature);
        }
    }
}
