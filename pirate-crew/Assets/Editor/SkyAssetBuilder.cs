using System;
using PirateCrew.Ambient;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 三档天空盒材质生成器（视觉审计遗留 #6「环境光升级为天空盒驱动」的资产层）。
    ///
    /// 【它做什么】按 <see cref="AmbientSkyboxCatalog"/> 的三档预设，生成/就地更新三张天空盒材质：
    ///   Assets/Art/Materials/Sky/Sky_Noon.mat / Sky_Dusk.mat / Sky_Overcast.mat
    /// 材质用的 shader 是 Assets/Art/Shaders/Sky/PirateGradientSky.shader。
    ///
    /// 【本脚本不接线】它只产出资产，**不写 RenderSettings**（不改 ambientMode / skybox 引用）。
    /// 原因：环境光一换，全场景观感基准就变了，而视觉审计批次 A–F 的【提案/待定】数值尚未实拍转正
    /// （docs/审计/视觉审计报告.md §四 遗留 1），此时动全局参数会让两轮调参互相覆盖。
    /// 接线步骤（ambientMode=Skybox + 按档切 skybox 材质）见
    /// docs/隔壁交接-3-天空盒环境光驱动.md 的「接线轮操作清单」。
    ///
    /// 【为什么"就地更新"而不是"已存在就跳过"】同 <see cref="BattleSceneLighting"/>：
    /// 改了 <see cref="AmbientSkyboxCatalog"/> 的常量必须能生效，否则会出现"代码改了、画面没变"
    /// 的排查陷阱。本脚本每次执行都把全部属性按目录值重写一遍。
    ///
    /// 【色空间】写的是 sRGB 原值（目录里的十六进制原样解析）。Unity 对普通 Color 属性会自动做
    /// sRGB→Linear 转换，脚本不该再乘 .linear——口径与证据见 BattleSceneLighting 类头「色空间」段。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Rendering/生成天空盒三档材质
    ///   无头: -batchmode -nographics -quit
    ///         -executeMethod PirateCrew.EditorTools.SkyAssetBuilder.BuildAll
    ///   （已挂进 ArtGate 一键管线，见 ArtGate 步骤表）
    /// </summary>
    public static class SkyAssetBuilder
    {
        /// <summary>天空盒材质目录（本脚本专属）。</summary>
        public const string SkyMaterialFolder = "Assets/Art/Materials/Sky";

        /// <summary>渐变天空盒 shader 名（唯一事实源在运行时目录 <see cref="AmbientSkyboxCatalog"/>）。</summary>
        public const string SkyShaderName = AmbientSkyboxCatalog.SkyShaderName;

        /// <summary>三档档位（顺序 = 材质生成顺序 = <c>skyboxMaterials[]</c> 下标顺序）。</summary>
        public static AmbientTimeOfDay[] Tiers => AmbientSkyboxCatalog.Tiers;

        /// <summary>档位对应的材质文件名（不含扩展名）。</summary>
        public static string MaterialFileName(AmbientTimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case AmbientTimeOfDay.Dusk: return "Sky_Dusk";
                case AmbientTimeOfDay.Overcast: return "Sky_Overcast";
                default: return "Sky_Noon";
            }
        }

        /// <summary>档位对应的材质资产路径。</summary>
        public static string MaterialPath(AmbientTimeOfDay timeOfDay)
        {
            return SkyMaterialFolder + "/" + MaterialFileName(timeOfDay) + ".mat";
        }

        /// <summary>取该档天空盒材质（接线轮用；资产不存在时返回 null，由调用方决定兜底）。</summary>
        public static Material LoadMaterial(AmbientTimeOfDay timeOfDay)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(timeOfDay));
        }

        /// <summary>幂等生成三档天空盒材质（ArtGate 步骤 / 菜单 / -executeMethod 共用入口）。</summary>
        [MenuItem("PirateCrew/Rendering/生成天空盒三档材质")]
        public static void BuildAll()
        {
            Shader shader = Shader.Find(SkyShaderName);
            if (shader == null)
            {
                // 硬失败：shader 找不到说明资产没导入或 Shader 声明名写错了，
                // 继续下去会得到"空材质"，必须让管线整体失败（ArtGate 会汇总报错并以非零退出）。
                throw new InvalidOperationException(
                    "[SkyAssetBuilder] 找不到 shader " + SkyShaderName
                    + "。请确认 Assets/Art/Shaders/Sky/PirateGradientSky.shader 已导入且无编译错误"
                    + "（read_console 里 shader error 必须为 0）。");
            }

            EnsureFolder(SkyMaterialFolder);

            var log = new System.Text.StringBuilder();
            log.Append("[SkyAssetBuilder] 三档天空盒材质已生成（数值【提案/待定】，须实拍验收）：");

            for (int i = 0; i < Tiers.Length; i++)
            {
                AmbientTimeOfDay tier = Tiers[i];
                AmbientSkyboxPreset preset = AmbientSkyboxCatalog.For(tier);
                Material material = EnsureMaterial(tier, shader);
                int written = AmbientSkyboxCatalog.ApplyPreset(material, preset);
                if (written < AmbientSkyboxCatalog.MaterialPropertyCount)
                {
                    // 漂移探测：shader 属性改名后 HasProperty 会静默跳过，预设就悄悄失效了。
                    Debug.LogWarning("[SkyAssetBuilder] " + MaterialFileName(tier)
                        + ".mat 只写进了 " + written + "/" + AmbientSkyboxCatalog.MaterialPropertyCount
                        + " 个属性——shader 属性名与 AmbientSkyboxCatalog 里的常量可能已不一致。");
                }

                EditorUtility.SetDirty(material);

                log.Append("\n  ").Append(MaterialFileName(tier)).Append(".mat")
                   .Append(" 档=").Append(AmbientTimeOfDayCatalog.DisplayName(tier))
                   .Append("（原版天空档 ").Append(preset.SkyTierIndex).Append("）")
                   .Append(" 天顶=").Append(ToHex(preset.ZenithColor))
                   .Append(" 地平=").Append(ToHex(preset.HorizonColor))
                   .Append(" 地面=").Append(ToHex(preset.GroundColor))
                   .Append(" 过渡=").Append(preset.HorizonBlend.ToString("0.00"))
                   .Append('/').Append(preset.GroundBlend.ToString("0.00"))
                   .Append(" 曝光=").Append(preset.Exposure.ToString("0.00"))
                   .Append(" 太阳盘=").Append(preset.HasSunDisk
                        ? preset.SunDiskSize.ToString("0.000") + "rad/" + preset.SunDiskIntensity.ToString("0.0")
                        : "关");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(log.ToString());
        }

        /// <summary>取/建材质资产，并保证 shader 正确（幂等：已存在的材质会就地换 shader）。</summary>
        static Material EnsureMaterial(AmbientTimeOfDay tier, Shader shader)
        {
            string fileName = MaterialFileName(tier);
            string path = MaterialPath(tier);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = fileName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>颜色转 <c>#RRGGBB</c>（仅用于日志；纯 C#，不走 ColorUtility 的 ECall）。</summary>
        static string ToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }
    }
}
