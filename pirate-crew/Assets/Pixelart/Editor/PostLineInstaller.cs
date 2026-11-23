using PirateCrew.Rendering.Pixelart;
using PirateCrew.Rendering.PostLine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **屏后描边线（PostLine）** 的渲染器装配（编辑器侧）：造一个专用渲染器资产、装
    /// <see cref="PostLineRendererFeature"/>（5 pass），把三张管线材质资产挂进 feature 字段
    /// （构建剥离防线：只靠 Resources.Shader.Find 的 shader 在播放器里会被剥掉），
    /// 最后追加进两档 URP 资产列表（与 Cast/Screen 渲染器同索引）。
    ///
    /// 【与 CellShade 装配器的关系】两套渲染器互不影响：CellShade 的 Cast/Screen 由
    /// <see cref="PixelartPathInstaller"/> 装配，本类只往列表追加第三项；rig 按
    /// <see cref="PixelartPath.PostLineRendererName"/> 运行时解析索引，风格切换只改相机
    /// 的 Renderer 索引（见 docs/技术/渲染/管线/双风格切换.md）。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/装配屏后描边渲染器</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PostLineInstaller.Install</c>。
    /// </summary>
    public static class PostLineInstaller
    {
        static readonly string[] UrpAssetPaths =
        {
            "Assets/Settings/URP/PC_Balanced_URPAsset.asset",
            "Assets/Settings/URP/PC_Performant_URPAsset.asset",
        };

        const string MaterialFolder = "Assets/Art/Materials/PostLine";
        const string RendererFolder = "Assets/Settings/URP";

        static string RendererPath => RendererFolder + "/" + PixelartPath.PostLineRendererName + ".asset";

        [MenuItem("PirateCrew/Pixelart/装配屏后描边渲染器")]
        public static void InstallFromMenu()
        {
            if (Install())
                EditorUtility.DisplayDialog("PostLine", "屏后描边渲染器已装配。", "好");
        }

        /// <summary>菜单/命令行入口。失败打印错误并返回 false。</summary>
        public static bool Install()
        {
            if (!TryInstall(out int index, out string error))
            {
                Debug.LogError("[PostLineInstaller] 装配失败：" + error);
                return false;
            }

            Debug.Log("[PostLineInstaller] 屏后描边渲染器装配完成，索引 " + index
                + "（风格切换在游戏设置页「渲染风格」）。");
            return true;
        }

        /// <summary>装配并回报索引（两档 URP 资产中的同一位置）。</summary>
        public static bool TryInstall(out int index, out string error)
        {
            index = -1;
            error = null;

            EnsureFolder(MaterialFolder);

            Material outline = EnsureMaterialAsset("PostLine_Outline", PostLinePath.OutlineShaderName);
            Material composite = EnsureMaterialAsset("PostLine_Composite", PostLinePath.CompositeShaderName);
            Material upscale = EnsureMaterialAsset("PostLine_Upscale", PostLinePath.SharpUpscaleShaderName);
            if (outline == null || composite == null || upscale == null)
            {
                error = "管线材质创建失败（shader 未导入或编译失败）";
                return false;
            }

            UniversalRendererData data = EnsureRendererData();
            if (data == null)
            {
                error = "创建/加载 " + RendererPath + " 失败";
                return false;
            }

            RebuildFeatures(data, outline, composite, upscale);

            int indexInAll = -1;
            foreach (string path in UrpAssetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    error = "未找到 URP 资产：" + path;
                    return false;
                }

                if (!TryAppendRenderer(asset, data, out int i))
                {
                    error = "追加屏后描边渲染器失败：" + path;
                    return false;
                }

                if (indexInAll < 0)
                    indexInAll = i;
                else if (indexInAll != i)
                {
                    error = "两档 URP 资产的渲染器索引不一致（" + indexInAll + " vs " + i + "）";
                    return false;
                }
            }

            index = indexInAll;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        static Material EnsureMaterialAsset(string name, string shaderName)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[PostLineInstaller] 找不到 shader " + shaderName
                    + "（shader 编译失败或未导入？）");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static UniversalRendererData EnsureRendererData()
        {
            EnsureFolder(RendererFolder);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data != null)
                return data;

            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            AssetDatabase.CreateAsset(data, RendererPath);
            // 与 URP 自带创建菜单一致：补齐内置 shader / 后处理资源引用。
            ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>重建特征列表：清空本资产内的全部 feature，只装一个 PostLine feature 并赋材质。</summary>
        static void RebuildFeatures(UniversalRendererData data, Material outline, Material composite, Material upscale)
        {
            var features = data.rendererFeatures;
            for (int i = features.Count - 1; i >= 0; i--)
            {
                ScriptableRendererFeature feature = features[i];
                if (feature == null)
                    continue;

                features.RemoveAt(i);
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(data)))
            {
                if (asset is ScriptableRendererFeature orphan)
                {
                    AssetDatabase.RemoveObjectFromAsset(orphan);
                    Object.DestroyImmediate(orphan, true);
                }
            }

            var created = (PostLineRendererFeature)ScriptableObject.CreateInstance(typeof(PostLineRendererFeature));
            created.name = nameof(PostLineRendererFeature);
            created.SetActive(true);
            created.settings.outlineMaterial = outline;
            created.settings.compositeMaterial = composite;
            created.settings.upscaleMaterial = upscale;
            AssetDatabase.AddObjectToAsset(created, data);
            data.rendererFeatures.Add(created);

            EditorUtility.SetDirty(created);
            EditorUtility.SetDirty(data);
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>把渲染器数据追加进管线资产（已在列表则返回既有索引）。</summary>
        static bool TryAppendRenderer(UniversalRenderPipelineAsset asset, UniversalRendererData data, out int index)
        {
            index = -1;

            var so = new SerializedObject(asset);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null)
                return false;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data)
                {
                    index = i;
                    return true;
                }
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            index = list.arraySize - 1;
            return true;
        }
    }
}
