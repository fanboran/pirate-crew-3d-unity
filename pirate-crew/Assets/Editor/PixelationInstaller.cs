using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 给两档 URP Renderer（PC_Balanced / PC_Performant）程序化加装
    /// <see cref="global::PirateCrew.Rendering.PixelationRendererFeature"/>——
    /// 等距像素卡通开工序列第 1 步（docs/技术/渲染管线-等距像素卡通.md §8）。
    ///
    /// 【为什么两档都装（与 BalancedSsaoInstaller 只装 Balanced 不同）】像素化是本作
    ///   画面风格的**基底**而非画质增强项：Performant 档若不装，切低画质时整个风格消失。
    ///   低分辨率 RT 本身还省 fill rate（渲染篇 §7），与 Performant 定位不冲突。
    ///
    /// 【幂等语义（同 BalancedSsaoInstaller 的"先卸再装"）】每次执行都移除两档渲染器上
    ///   已存在的同类型 Feature（含列表外孤儿子资产），重新创建并钉 settings，再挂载入列。
    ///   重跑永远收敛到同一份已知配置。
    ///
    /// 【shader 剥离防线】安装时把 Assets/Art/Shaders/PixelBlit.shader 显式
    ///   AssetDatabase.Load 进 settings.blitShader 序列化保存——资产引用链保 shader
    ///   入播放器构建（r2 洋红事故的同族风险，Feature 头注释有说明）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Rendering/挂载像素化 RendererFeature（两档渲染器）
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.PixelationInstaller.Install
    /// </summary>
    public static class PixelationInstaller
    {
        static readonly string[] RendererPaths =
        {
            "Assets/Settings/URP/PC_Balanced_Renderer.asset",
            "Assets/Settings/URP/PC_Performant_Renderer.asset",
        };

        const string FeatureDisplayName = "PirateCrew Pixelation";

        [MenuItem("PirateCrew/Rendering/挂载像素化 RendererFeature（两档渲染器）")]
        public static void Install()
        {
            int installed = 0;
            foreach (string path in RendererPaths)
            {
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null)
                {
                    Debug.LogWarning("[PixelationInstaller] 未找到 Renderer 资产: " + path
                        + "（可先执行 PirateCrew.EditorTools.UrpSetup.Configure 创建），跳过。");
                    continue;
                }

                RemoveExistingFeatures(rendererData);
                InstallFeature(rendererData);
                EditorUtility.SetDirty(rendererData);
                installed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PixelationInstaller] 像素化 Feature 安装完成 -> " + installed + "/"
                + RendererPaths.Length + " 个渲染器（RT 高 360，两步 nearest blit；"
                + "渲染篇 §8 开工序列第 1 步）。");
        }

        /// <summary>移除同类 Feature 与孤儿子资产（含列表外游离的），防反复运行堆积。</summary>
        static void RemoveExistingFeatures(UniversalRendererData rendererData)
        {
            var features = rendererData.rendererFeatures;
            if (features != null)
            {
                for (int i = features.Count - 1; i >= 0; i--)
                {
                    var feature = features[i];
                    if (feature == null || feature.GetType() != typeof(global::PirateCrew.Rendering.PixelationRendererFeature))
                        continue;

                    features.RemoveAt(i);
                    AssetDatabase.RemoveObjectFromAsset(feature);
                    UnityEngine.Object.DestroyImmediate(feature, true);
                }
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(rendererData)))
            {
                if (asset == null || asset.GetType() != typeof(global::PirateCrew.Rendering.PixelationRendererFeature))
                    continue;
                if (features != null && features.Contains((ScriptableRendererFeature)asset))
                    continue;

                AssetDatabase.RemoveObjectFromAsset(asset);
                UnityEngine.Object.DestroyImmediate(asset, true);
            }
        }

        static void InstallFeature(UniversalRendererData rendererData)
        {
            var feature = ScriptableObject.CreateInstance<global::PirateCrew.Rendering.PixelationRendererFeature>();
            feature.name = FeatureDisplayName;

            // blit 材质用 URP 自带 Hidden/Universal/CoreBlit（pass 0 Nearest）——URP 运行时
            // 自身引用该 shader，播放器构建必然保活，无需也不应回填自定义 shader 引用
            //（首版自写 PixelBlit 在播放器真实 GPU 会话不兼容，已删，见 Feature 头注释）。
            feature.SetActive(true);

            // 作为渲染器资产的子资产保存，引用随资产持久化（同 BalancedSsaoInstaller）。
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
        }
    }
}
