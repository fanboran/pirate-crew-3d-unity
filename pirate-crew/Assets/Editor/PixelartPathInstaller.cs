using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **像素化着色路径**的渲染器装配（编辑器侧）：造两个专用渲染器资产并追加进两档 URP 资产。
    ///
    /// 【为什么要两个渲染器】这条路径是"一台相机只写数据、另一台只上屏"的形状：
    /// <list type="bullet">
    ///   <item><b>Cast 渲染器</b>（AfterRendering 顺序即此处数组顺序）：
    ///         BeforeRender（相机 snap）→ Object（几何 → 低分辨率 G-buffer）→ Shading（低分辨率域着色）。</item>
    ///   <item><b>Screen 渲染器</b>：只有 ScreenCopy（ResultBuffer 点采样放大上屏）。</item>
    /// </list>
    /// 两台相机若共用同一个渲染器，主相机会把刚画好的 G-buffer 清掉再着色，画面变纯背景色——
    /// 这正是"两个渲染器 + 两个相机"不可省的机械原因（蓝图 §9 落地清单第一条）。
    ///
    /// 【为什么追加而新建 URP 资产】两档 URP 资产已被既有场景/画质档引用，重建会牵动全套设置；
    /// 追加一个渲染器只是给列表加一项，`m_DefaultRendererIndex` 不动，既有相机不受影响。
    /// **索引必须两档一致**——故本装配器按固定顺序往两档追加同一对渲染器。
    ///
    /// 【既有的两档渲染器一律不动】本路径与旧视觉链（`PirateToon` + `PixelationRendererFeature`）
    /// 并行存在、互不引用；后者的退役另行处置。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/装配像素化路径渲染器</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartPathInstaller.Install</c>。
    /// </summary>
    public static class PixelartPathInstaller
    {
        static readonly string[] UrpAssetPaths =
        {
            "Assets/Settings/URP/PC_Balanced_URPAsset.asset",
            "Assets/Settings/URP/PC_Performant_URPAsset.asset",
        };

        /// <summary>本装配器上次成功装配的渲染器索引（同一次会话内供装配场景使用）。</summary>
        public static int LastCastIndex { get; private set; } = -1;

        /// <summary>Screen 渲染器索引。</summary>
        public static int LastScreenIndex { get; private set; } = -1;

        [MenuItem("PirateCrew/Pixelart/装配像素化路径渲染器（两档 URP）")]
        public static void Install()
        {
            if (!TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError("[PixelartPathInstaller] 装配失败：" + error);
                return;
            }

            LastCastIndex = castIndex;
            LastScreenIndex = screenIndex;
            Debug.Log("[PixelartPathInstaller] 装配完成：Cast 渲染器索引 " + castIndex
                + "、Screen 渲染器索引 " + screenIndex
                + "（两档 URP 资产一致；场景装配器会把这两个索引写进 PixelartCameraRig）。");
        }

        /// <summary>装配并回报索引。供场景装配器直接调用（菜单/命令行亦走同一条路径）。</summary>
        public static bool TryInstall(out int castIndex, out int screenIndex, out string error)
        {
            castIndex = -1;
            screenIndex = -1;
            error = null;

            UniversalRendererData cast = EnsureRendererData(PixelartPath.CastRendererName);
            UniversalRendererData screen = EnsureRendererData(PixelartPath.ScreenRendererName);
            if (cast == null || screen == null)
            {
                error = "创建/加载渲染器资产失败";
                return false;
            }

            // ---- Cast 渲染器：三个特征，数组顺序 = 同一 RenderPassEvent 内的执行顺序 ----
            RebuildFeatures(cast, new System.Type[]
            {
                typeof(PixelartBeforeRenderFeature),
                typeof(PixelartObjectFeature),
                typeof(PixelartShadingFeature),
            });

            // 着色 shader 必须留下**资产引用**，否则播放器构建会把它剥离（Feature 里那段注释是实测事故）。
            AssignShadingShader(cast);

            // ---- Screen 渲染器：只有上屏 ----
            RebuildFeatures(screen, new System.Type[]
            {
                typeof(PixelartScreenCopyFeature),
            });

            // ---- 追加进两档 URP 资产（索引必须一致） ----
            int indexInAll = -1;
            foreach (string path in UrpAssetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    error = "未找到 URP 资产：" + path;
                    return false;
                }

                if (!TryAppendRenderer(asset, cast, out int c))
                {
                    error = "追加 Cast 渲染器失败：" + path;
                    return false;
                }

                if (!TryAppendRenderer(asset, screen, out int s))
                {
                    error = "追加 Screen 渲染器失败：" + path;
                    return false;
                }

                if (indexInAll < 0)
                {
                    indexInAll = c;
                }
                else if (indexInAll != c)
                {
                    error = "两档 URP 资产的渲染器索引不一致（" + indexInAll + " vs " + c + "），"
                        + "请检查资产列表是否被手工改过";
                    return false;
                }

                castIndex = c;
                screenIndex = s;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        static UniversalRendererData EnsureRendererData(string assetName)
        {
            string path = PixelartPath.RendererFolder + "/" + assetName + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data != null)
                return data;

            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            AssetDatabase.CreateAsset(data, path);
            // 与 URP 自带创建菜单一致：补齐内置 shader / 后处理资源引用（同 UrpSetup 口径）。
            ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>
        /// 把渲染器的特征列表重建成给定类型序列（幂等：先卸同类再按序装）。
        /// 重跑永远收敛到同一份顺序——顺序即执行次序，不能让历史残留改掉它。
        /// </summary>
        static void RebuildFeatures(UniversalRendererData rendererData, System.Type[] types)
        {
            // 先卸：列表内的同类 + 列表外的孤儿子资产（反复运行不堆积）。
            var features = rendererData.rendererFeatures;
            for (int i = features.Count - 1; i >= 0; i--)
            {
                ScriptableRendererFeature feature = features[i];
                if (feature == null || !IsOurs(feature.GetType()))
                    continue;

                features.RemoveAt(i);
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(rendererData)))
            {
                if (asset == null || !IsOurs(asset.GetType()))
                    continue;
                if (features.Contains((ScriptableRendererFeature)asset))
                    continue;

                AssetDatabase.RemoveObjectFromAsset(asset);
                Object.DestroyImmediate(asset, true);
            }

            foreach (System.Type type in types)
            {
                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = type.Name;
                feature.SetActive(true);
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
            }

            EditorUtility.SetDirty(rendererData);
        }

        static bool IsOurs(System.Type type)
        {
            return typeof(PixelartCameraRig).Assembly == type.Assembly
                && type.Namespace == typeof(PixelartPath).Namespace;
        }

        /// <summary>
        /// 把着色 shader 资产写进 Feature 的序列化字段（构建剥离防线）。
        /// 找不到资产说明 shader 编译失败或被改名——这里必须明确报错，否则表现是"画面全是背景色"。
        /// </summary>
        static void AssignShadingShader(UniversalRendererData rendererData)
        {
            const string path = "Assets/Art/Shaders/Pixelart/PixelartShading.shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.LogError("[PixelartPathInstaller] 找不到着色 shader：" + path
                    + "（编译失败或被改名？）——播放器里这条 pass 会被剥离。");
                return;
            }

            var features = rendererData.rendererFeatures;
            if (features == null || features.Count < 3)
            {
                Debug.LogError("[PixelartPathInstaller] Cast 渲染器的特征数量不对，着色 shader 未写入。");
                return;
            }

            var shading = features[2] as PixelartShadingFeature;
            if (shading == null)
            {
                Debug.LogError("[PixelartPathInstaller] Cast 渲染器第 3 个特征不是着色 Feature（顺序被改过？）。");
                return;
            }

            shading.shadingShader = shader;
            EditorUtility.SetDirty(shading);
            EditorUtility.SetDirty(rendererData);
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
