using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 一次性落地 URP：创建渲染器/管线资产并挂载到 Graphics 与 Quality 设置。
    /// 入口：-executeMethod PirateCrew.EditorTools.UrpSetup.Configure（可重复执行）。
    /// </summary>
    public static class UrpSetup
    {
        const string SettingsFolder = "Assets/Settings";
        const string UrpFolder = "Assets/Settings/URP";
        const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
        const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
        const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

        [MenuItem("PirateCrew/Rendering/落地 URP 管线")]
        public static void Configure()
        {
            EnsureFolder(SettingsFolder, "Settings");
            EnsureFolder(UrpFolder, "URP");

            // 高画质档：HDR 关 + MSAA 关（等距像素卡通口径：MSAA 的平滑边经像素化降采成脏边）+ 更远阴影
            var balancedRenderer = CreateOrLoadRendererData("PC_Balanced_Renderer");
            var balanced = CreateOrLoadPipelineAsset("PC_Balanced_URPAsset", balancedRenderer);
            Tune(balanced, hdr: false, msaaSampleCount: 0, shadowDistance: 50f, cascadeCount: 4);

            // 低画质档：HDR 关 + MSAA 关 + 近阴影
            var performantRenderer = CreateOrLoadRendererData("PC_Performant_Renderer");
            var performant = CreateOrLoadPipelineAsset("PC_Performant_URPAsset", performantRenderer);
            Tune(performant, hdr: false, msaaSampleCount: 0, shadowDistance: 25f, cascadeCount: 2);

            // 工程默认走 Balanced；低画质档再单独覆盖为 Performant
            GraphicsSettings.defaultRenderPipeline = balanced;
            SetGraphicsSettingsPipeline(balanced);
            SetQualityLevelPipeline(new[] { "High", "Very High", "Ultra" }, balanced);
            SetQualityLevelPipeline(new[] { "Very Low", "Low", "Medium" }, performant);

            EditorUtility.SetDirty(balanced);
            EditorUtility.SetDirty(performant);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[UrpSetup] URP 落地完成：默认管线 = " + AssetDatabase.GetAssetPath(balanced)
                + "；QualitySettings 映射 Very Low/Low/Medium -> PC_Performant_URPAsset，High/Very High/Ultra -> PC_Balanced_URPAsset。");
        }

        static void Tune(UniversalRenderPipelineAsset asset, bool hdr, int msaaSampleCount, float shadowDistance, int cascadeCount)
        {
            asset.supportsHDR = hdr;
            asset.msaaSampleCount = msaaSampleCount;
            asset.renderScale = 1.0f;
            asset.shadowDistance = shadowDistance;
            asset.shadowCascadeCount = cascadeCount;
            EditorUtility.SetDirty(asset);
        }

        static UniversalRendererData CreateOrLoadRendererData(string assetName)
        {
            string path = UrpFolder + "/" + assetName + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data != null)
                return data;

            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            AssetDatabase.CreateAsset(data, path);
            // 与 URP 自带创建菜单一致：补齐内置 shader / 后处理资源引用
            ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
            return data;
        }

        static UniversalRenderPipelineAsset CreateOrLoadPipelineAsset(string assetName, UniversalRendererData renderer)
        {
            string path = UrpFolder + "/" + assetName + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset != null)
            {
                // 重复执行时修正渲染器引用（m_RendererDataList 为 URP 14 实际序列化字段）
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                if (list != null && list.arraySize > 0)
                {
                    var element = list.GetArrayElementAtIndex(0);
                    if (element.objectReferenceValue != renderer)
                    {
                        element.objectReferenceValue = renderer;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                return asset;
            }

            asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void SetGraphicsSettingsPipeline(RenderPipelineAsset asset)
        {
            var obj = LoadProjectSettingsObject(GraphicsSettingsPath);
            if (obj == null)
            {
                Debug.LogWarning("[UrpSetup] 未找到 " + GraphicsSettingsPath);
                return;
            }

            var so = new SerializedObject(obj);
            var prop = so.FindProperty("m_CustomRenderPipeline");
            if (prop == null)
            {
                Debug.LogWarning("[UrpSetup] GraphicsSettings 未找到 m_CustomRenderPipeline");
                return;
            }

            prop.objectReferenceValue = asset;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
        }

        static void SetQualityLevelPipeline(string[] levelNames, RenderPipelineAsset asset)
        {
            var obj = LoadProjectSettingsObject(QualitySettingsPath);
            if (obj == null)
            {
                Debug.LogWarning("[UrpSetup] 未找到 " + QualitySettingsPath);
                return;
            }

            var wanted = new HashSet<string>(levelNames, System.StringComparer.OrdinalIgnoreCase);
            var so = new SerializedObject(obj);
            var levels = so.FindProperty("m_QualitySettings");
            if (levels == null)
            {
                Debug.LogWarning("[UrpSetup] QualitySettings 未找到 m_QualitySettings");
                return;
            }

            int matched = 0;
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                var nameProp = level.FindPropertyRelative("name");
                if (nameProp == null || !wanted.Contains(nameProp.stringValue))
                    continue;

                var pipelineProp = level.FindPropertyRelative("customRenderPipeline");
                if (pipelineProp == null)
                    continue;

                pipelineProp.objectReferenceValue = asset;
                matched++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
            Debug.Log("[UrpSetup] QualitySettings [" + string.Join("/", levelNames) + "] -> " + asset.name + "，命中 " + matched + " 档。");
        }

        static UnityEngine.Object LoadProjectSettingsObject(string path)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all != null && all.Length > 0)
                return all[0];
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }

        static void EnsureFolder(string path, string leafName)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, leafName);
        }
    }
}
