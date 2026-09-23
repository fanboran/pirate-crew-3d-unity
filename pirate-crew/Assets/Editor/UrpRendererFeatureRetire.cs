using PirateCrew.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 把全屏 Sobel 描边 Feature（<see cref="OutlineRendererFeature"/>）从两档 URP Renderer 上**摘除**，
    /// 并清掉渲染器资产里的孤儿子资产——写实观感栈退役的一部分（任务书 §3 表 M2b 行）。
    ///
    /// 【为什么退役】它与像素块感互斥，且职责已被反壳描边完全接管：
    ///   · 全屏 Sobel 是**屏幕空间邻域算子**——它按物理像素邻域算梯度，在"全分辨率渲染 → point 降采到
    ///     640×360"的管线里，边缘强度在降采后被 3× 欠采成断续/抖动的线（渲染篇 §3.1 推论 4 同一机理）；
    ///   · 反向壳描边（`PirateToon.shader` 的 `SRPDefaultUnlit` pass）在**几何域**出线，线宽由 RT 高
    ///     直接定义（渲染篇 §5 线宽公式），降采后天然均匀；
    ///   · 选中反馈也不再依赖它（审计 §2.3：它只是调试期遗留）。
    ///   · 附带收益：少一次全屏深度采样 pass，且 `_CameraDepthTexture` 少一个消费者。
    ///
    /// 【为什么改的是"安装器"而不是删脚本】本类原为 `UrpRendererFeatureRetire`（挂载器）。
    /// 挂载器留着 = 任何人重跑一次 `PirateCrew/Rendering/*` 就可能把退役项装回去（M0 的配置漂移
    /// 同族事故）。所以把它**改成退役器**：名字与入口换成 Retire，行为是先卸再清孤儿子资产、
    /// 且幂等（已摘除时报告"无变化"，不会重复写资产）。`OutlineRendererFeature.cs` 与
    /// `PirateOutlinePost.shader` 本体保留（删类型会影响构建里的 shader 保活清单，且反壳描边
    /// 调试时可能还要对照），只是**没有任何 Renderer 引用它**。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Rendering/摘除全屏 Sobel 描边 Feature
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.UrpRendererFeatureRetire.RetireAll
    ///
    /// 【验证】摘除后两档 Renderer 的 `m_RendererFeatures` 只应剩 Pixation 一项；
    /// 出图判据见 docs/技术/资产管线/像素海面技术方案.md 之外的渲染篇 §9 排障速查。
    /// </summary>
    public static class UrpRendererFeatureRetire
    {
        const string PerformantRendererPath = "Assets/Settings/URP/PC_Performant_Renderer.asset";
        const string BalancedRendererPath = "Assets/Settings/URP/PC_Balanced_Renderer.asset";

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。幂等。</summary>
        [MenuItem("PirateCrew/Rendering/摘除全屏 Sobel 描边 Feature")]
        public static void RetireAll()
        {
            int removed = 0;
            removed += Retire(BalancedRendererPath);
            removed += Retire(PerformantRendererPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (removed == 0)
            {
                Debug.Log("[UrpRendererFeatureRetire] 两档 Renderer 上都没有 OutlineRendererFeature（已摘除，幂等无变化）。");
                return;
            }
            Debug.Log("[UrpRendererFeatureRetire] 已摘除全屏 Sobel 描边 Feature " + removed + " 处：\n"
                + "  " + BalancedRendererPath + "\n  " + PerformantRendererPath + "\n"
                + "  替代物 = PirateToon.shader 的反壳描边 pass（SRPDefaultUnlit，墨色 StickTokens.INK）。");
        }

        /// <summary>摘除单个 Renderer 上的 Outline Feature；返回摘除数量（0 = 本来就没有）。</summary>
        static int Retire(string path)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (rendererData == null)
            {
                Debug.LogWarning("[UrpRendererFeatureRetire] 未找到 Renderer 资产: " + path
                    + "（可先执行 PirateCrew.EditorTools.UrpSetup.Configure 创建）。");
                return 0;
            }

            int removed = 0;
            var features = rendererData.rendererFeatures;
            if (features != null)
            {
                for (int i = features.Count - 1; i >= 0; i--)
                {
                    var feature = features[i];
                    if (feature == null || feature.GetType() != typeof(OutlineRendererFeature))
                        continue;

                    features.RemoveAt(i);
                    AssetDatabase.RemoveObjectFromAsset(feature);
                    Object.DestroyImmediate(feature, true);
                    removed++;
                    Debug.Log("[UrpRendererFeatureRetire] 已摘除 OutlineRendererFeature -> " + path);
                }
            }

            // 清掉渲染器资产里的**游离**同名子资产（列表里没引用但还留在文件里的），
            // 否则反复挂载/摘除会在 .asset 里堆一堆死对象。
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(rendererData)))
            {
                if (asset == null || asset.GetType() != typeof(OutlineRendererFeature))
                    continue;
                if (features != null && features.Contains((ScriptableRendererFeature)asset))
                    continue;

                AssetDatabase.RemoveObjectFromAsset(asset);
                Object.DestroyImmediate(asset, true);
                removed++;
                Debug.Log("[UrpRendererFeatureRetire] 已清除游离的 OutlineRendererFeature 子资产 -> " + path);
            }

            if (removed > 0)
                EditorUtility.SetDirty(rendererData);
            return removed;
        }
    }
}
