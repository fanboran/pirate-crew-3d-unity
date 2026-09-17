using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 给 <see cref="UniversalRendererData"/>（PC_Balanced_Renderer）程序化加装 URP 内置
    /// SSAO RendererFeature，作为 M4 视觉审计批次 C「光照资产补课」的一部分
    /// （审计结论：渲染器上唯一 Feature 是自研描边，无 SSAO；间接光无遮蔽差异是画面"塑料感"根因之一，
    /// 见 docs/审计/视觉审计报告.md §二.4 / §三 批次 C）。
    ///
    /// 【为什么只对 URP/Lit 物件生效】URP 内置 SSAO 的产出路径有两条：
    ///   1. 表现侧：SSAO Pass 把 AO 图设为全局纹理 _ScreenSpaceOcclusionTexture，
    ///      由着色器内的 multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION 关键字分支主动采样
    ///      （URP 14: Shaders/Lit.shader:143，光照 pass 内把 AO 乘进间接光）；
    ///   2. SSAO Pass 本身只是在 Execute 里全局开关该关键字（Runtime/RendererFeatures/
    ///      ScreenSpaceAmbientOcclusion.cs，ScreenSpaceAmbientOcclusionPass.Execute）。
    ///   自研 Pirate shader（PirateSurface / PirateTerrain / PirateOcean / Outline）不含该关键字分支，
    ///   不会采样 AO 图——因此 SSAO 只会作用于 URP/Lit 物件（世界地图 kit 道具、站面灰盒、
    ///   后续走 Lit 的资产），而这批正是审计里最"塑料"的一批。Pirate shader 的接蔽若将来要做，
    ///   属于 shader 侧改动，不在本脚本职责内。
    ///
    /// 【为什么只装 Balanced 不装 Performant】SSAO 是全屏多 Pass（AO + 数次模糊），
    /// 即使半分辨率也有可观的带宽与耗时；Performant 档面向 Very Low/Low/Medium 画质，
    /// 优先保帧数，宁缺阴影细节。若未来 Performant 也要装，用本脚本改 RENDERER_PATH 常量即可，
    /// 建议同时把 Samples 降到 Low。
    ///
    /// 【幂等语义】本脚本可反复执行：每次执行都会——
    ///   1. 移除渲染器上已存在的同类 SSAO Feature（含游离在 m_RendererFeatures 列表外的
    ///      孤儿子资产），子资产从渲染器资产中卸下并销毁；
    ///   2. 重新 new 一个 SSAO Feature 并把 settings 钉成本脚本顶部的常量值，再挂载入列。
    ///   与描边安装脚本（M2UrpRendererFeatureSetup）"已存在则跳过"不同，这里选择
    ///   "先卸再装"：SSAO 是 URP 内置类型，settings 由本脚本统一钉值，
    ///   重跑永远收敛到同一份已知配置，避免手工在 Inspector 里改过之后悄悄漂移。
    ///
    /// 【为什么不手工改 YAML】SSAO Feature 的类型 ScreenSpaceAmbientOcclusion 在 URP 14 中是
    /// internal（没有公开的 ScreenSpaceAmbientOcclusionRendererFeature 类型），settings 也是
    /// internal class——外部程序集无法直接 new。本脚本用反射定位类型与 settings 字段
    /// （字段名均已在 com.unity.render-pipelines.universal@14.0.12 源码内核对），
    /// 交给 AssetDatabase 正常序列化，避免手写 m_RendererFeatures 列表的脆弱格式。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Rendering/挂载 SSAO RendererFeature（Balanced）
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.BalancedSsaoInstaller.InstallBalancedSsao
    /// </summary>
    public static class BalancedSsaoInstaller
    {
        const string RendererPath = "Assets/Settings/URP/PC_Balanced_Renderer.asset";

        /// <summary>URP 14.0.12 中 SSAO Feature 的真实类型全名（internal，见文件头说明）。</summary>
        const string SsaoFeatureTypeName = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";

        /// <summary>子资产显示名，与 URP Inspector 挂载时的命名保持一致。</summary>
        const string FeatureDisplayName = "Screen Space Ambient Occlusion";

        // ---- 钉死的 SSAO settings（强度/半径为【提案/待定】，须实拍验收后回写审计文档）----
        const float SsaoIntensity = 0.55f;              // 【提案/待定】遮蔽强度
        const float SsaoRadius = 0.55f;                 // 【提案/待定】遮蔽半径
        const float SsaoDirectLightingStrength = 0.25f; // URP 默认：AO 对直接光的压制，先不动
        const float SsaoFalloff = 100f;                 // URP 默认：距离衰减
        const bool SsaoHalfResolution = true;           // Half Resolution：半分辨率算 AO，性能优先
        const bool SsaoAfterOpaque = false;             // URP 默认：在不透明阶段前注入，Lit 间接光吃到 AO

        [MenuItem("PirateCrew/Rendering/挂载 SSAO RendererFeature（Balanced）")]
        public static void InstallBalancedSsao()
        {
            // SSAO Feature 类型在 URP 程序集里是 internal，用"同程序集定位"避免硬编码程序集名。
            var ssaoType = typeof(UniversalRendererData).Assembly.GetType(SsaoFeatureTypeName);
            if (ssaoType == null)
            {
                Debug.LogError("[BalancedSsaoInstaller] 在 URP 程序集中找不到类型 " + SsaoFeatureTypeName
                    + "。URP 包版本可能不是 14.0.12，请重新核对该包的 SSAO Feature 类型名。");
                return;
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                Debug.LogWarning("[BalancedSsaoInstaller] 未找到 Renderer 资产: " + RendererPath
                    + "（可先执行 PirateCrew.EditorTools.UrpSetup.Configure 创建）。");
                return;
            }

            int removed = RemoveExistingFeatures(rendererData, ssaoType);
            InstallSsaoFeature(rendererData, ssaoType);

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BalancedSsaoInstaller] SSAO 安装完成 -> " + RendererPath
                + "\n  移除旧实例 " + removed + " 个（含游离子资产），Intensity=" + SsaoIntensity
                + "，Radius=" + SsaoRadius + "，HalfRes=" + SsaoHalfResolution + "，Source=Depth【提案/待定】。");
        }

        /// <summary>
        /// 移除同类 Feature 与游离子资产，返回移除数。列表内的先出列再卸子资产，
        /// 列表外但已序列化成子资产的（此前接线残留）也一并清掉，防止反复运行堆积孤儿对象。
        /// </summary>
        static int RemoveExistingFeatures(UniversalRendererData rendererData, Type ssaoType)
        {
            int removed = 0;
            var features = rendererData.rendererFeatures;

            if (features != null)
            {
                for (int i = features.Count - 1; i >= 0; i--)
                {
                    var feature = features[i];
                    if (feature == null || feature.GetType() != ssaoType)
                        continue;

                    features.RemoveAt(i);
                    AssetDatabase.RemoveObjectFromAsset(feature);
                    UnityEngine.Object.DestroyImmediate(feature, true);
                    removed++;
                }
            }

            // 清理不在列表里的孤儿子资产（LoadAllAssetsAtPath 会把主资产与所有子资产都给回来）。
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(RendererPath);
            foreach (var asset in allAssets)
            {
                if (asset == null || asset.GetType() != ssaoType)
                    continue;
                if (features != null && features.Contains((ScriptableRendererFeature)asset))
                    continue; // 上一段已经处理过的情况（理论到不了，防御性判断）

                AssetDatabase.RemoveObjectFromAsset(asset);
                UnityEngine.Object.DestroyImmediate(asset, true);
                removed++;
            }

            return removed;
        }

        /// <summary>创建 SSAO Feature、钉 settings、回填包内资源引用并挂载入列。</summary>
        static void InstallSsaoFeature(UniversalRendererData rendererData, Type ssaoType)
        {
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
            feature.name = FeatureDisplayName;
            ConfigureSettings(feature, ssaoType);

            // 与 UrpSetup 同款做法：把 Feature 身上 [Reload] 标记的 shader / 蓝噪声贴图
            // 从 URP 包路径回填进序列化字段，保证落盘的子资产引用自包含，
            // 不依赖运行期 Create() 里 ResourceReloader 的兜底。
            ResourceReloader.ReloadAllNullIn(feature, UniversalRenderPipelineAsset.packagePath);

            feature.SetActive(true);

            // 作为渲染器资产的子资产保存，引用才能随资产持久化（同 M2UrpRendererFeatureSetup）。
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
        }

        /// <summary>
        /// 通过反射把 settings 钉成常量值。ScreenSpaceAmbientOcclusionSettings 是 internal class
        /// （非 struct），反射拿到 m_Settings 的对象引用后改字段即可直接生效，无需写回。
        /// 字段名与枚举值均来自 com.unity.render-pipelines.universal@14.0.12
        /// Runtime/RendererFeatures/ScreenSpaceAmbientOcclusion.cs:12-59。
        /// </summary>
        static void ConfigureSettings(ScriptableRendererFeature feature, Type ssaoType)
        {
            var settingsField = ssaoType.GetField("m_Settings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (settingsField == null)
                throw new InvalidOperationException("SSAO Feature 上找不到序列化字段 m_Settings，URP 包结构可能已变化。");

            var settings = settingsField.GetValue(feature);
            if (settings == null)
            {
                settings = Activator.CreateInstance(settingsField.FieldType);
                settingsField.SetValue(feature, settings);
            }

            // AO 算法：BlueNoise 抖动（URP 默认），配 High 双边模糊。
            SetField(settings, "AOMethod", "BlueNoise");
            SetField(settings, "BlurQuality", "High");
            // Half Resolution 开：AO 与模糊都在半分辨率下算，再双线性上采样，省大头带宽。
            SetField(settings, "Downsample", SsaoHalfResolution);
            // 注入时机：不透明阶段前，URP/Lit 的间接光分支才能吃到 AO（AfterOpaque=false 为 URP 默认）。
            SetField(settings, "AfterOpaque", SsaoAfterOpaque);
            // 深度源用纯 Depth：省掉 DepthNormals prepass，法线由深度重建（质量 Medium，URP 默认）。
            SetField(settings, "Source", "Depth");
            SetField(settings, "NormalSamples", "Medium");
            SetField(settings, "Intensity", SsaoIntensity);           // 【提案/待定】
            SetField(settings, "Radius", SsaoRadius);                 // 【提案/待定】
            SetField(settings, "DirectLightingStrength", SsaoDirectLightingStrength);
            SetField(settings, "Falloff", SsaoFalloff);
            // 采样数 Medium（8 采样，URP 默认）；Performant 若要装建议降 Low。
            SetField(settings, "Samples", "Medium");

            EditorUtility.SetDirty(feature);
        }

        /// <summary>按字段名写值：枚举按名字解析（抗枚举数值重排），其余走类型转换。</summary>
        static void SetField(object settings, string fieldName, object value)
        {
            var field = settings.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException("SSAO settings 上找不到字段 " + fieldName
                    + "，请核对 com.unity.render-pipelines.universal 包内的 ScreenSpaceAmbientOcclusion.cs。");

            object converted = field.FieldType.IsEnum
                ? Enum.Parse(field.FieldType, (string)value)
                : Convert.ChangeType(value, field.FieldType);
            field.SetValue(settings, converted);
        }
    }
}
