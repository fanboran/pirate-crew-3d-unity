using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **像素化着色路径**的渲染器装配（编辑器侧）：造两个专用渲染器资产、装齐七个特征、追加进两档 URP 资产。
    ///
    /// 【为什么要两个渲染器】这条路径是"一台相机只写数据、另一台只上屏"的形状：
    /// <list type="bullet">
    ///   <item><b>Cast 渲染器</b>（数组顺序 = 同一 RenderPassEvent 内的执行顺序，即契约 §3）：
    ///         BeforeRender（相机 snap + 尺寸下发）→ Object（几何 → 屏幕档 7 张 G-buffer）→
    ///         Connectivity（连通域三阶段）→ Outline（屏幕空间描边）→ RimLight（边缘光）→
    ///         Shading（四趟着色 + 合成）→ ColorCorrection（帧级调色板）。</item>
    ///   <item><b>Screen 渲染器</b>：只有 ScreenCopy（ResultBuffer 点采样放大上屏）。</item>
    /// </list>
    /// 两台相机若共用同一个渲染器，主相机会把刚画好的 G-buffer 清掉再着色，画面变纯背景色——
    /// 这正是"两个渲染器 + 两个相机"不可省的机械原因。
    ///
    /// 【为什么追加而新建 URP 资产】两档 URP 资产已被既有场景/画质档引用，重建会牵动全套设置；
    /// 追加一个渲染器只是给列表加一项，`m_DefaultRendererIndex` 不动，既有相机不受影响。
    /// **索引必须两档一致**——故本装配器按固定顺序往两档追加同一对渲染器。
    ///
    /// 【shader / compute / 调色板一律留资产引用】这是**构建剥离防线**：只在 C# 里 `Shader.Find` /
    /// `Resources.Load` 的 shader 与 compute 在播放器构建里会被剥离，症状是"这条 pass 静默不生效、
    /// 没有任何报错"。所以本装配器把每个 Feature 的资产字段都写满，且用**强类型字段赋值**——
    /// 字段名对不上会直接编译报错，不给静默失效留口子。
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

        /// <summary>Cast 渲染器的特征顺序（= 执行顺序，契约 §3）。改这里就是改管线次序。</summary>
        static readonly System.Type[] CastFeatureOrder =
        {
            typeof(PixelartBeforeRenderFeature),
            typeof(PixelartObjectFeature),
            typeof(PixelartConnectivityFeature),
            typeof(PixelartOutlineFeature),
            typeof(PixelartRimLightFeature),
            typeof(PixelartShadingFeature),
            typeof(PixelartColorCorrectionFeature),
        };

        /// <summary>本装配器上次成功装配的渲染器索引（同一次会话内供装配场景使用）。</summary>
        public static int LastCastIndex { get; private set; } = -1;

        /// <summary>Screen 渲染器索引。</summary>
        public static int LastScreenIndex { get; private set; } = -1;

        /// <summary>透明件叠加渲染器索引（见 <see cref="EnsureOverlayRenderer"/>）。</summary>
        public static int LastOverlayIndex { get; private set; } = -1;

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

            RebuildFeatures(cast, CastFeatureOrder);
            AssignFeatureAssets(cast);

            // ---- Screen 渲染器：只有上屏 ----
            RebuildFeatures(screen, new System.Type[] { typeof(PixelartScreenCopyFeature) });

            // 附加光与主光阴影是 URP **管线资产**上的开关；色带贴着实时阴影是创始人裁决，
            // 少了这一步"阴影永远不出现"且不会有任何报错。
            EnsureUrpLightingSettings();

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

        /// <summary>
        /// 装配/刷新**透明件叠加渲染器**并追加进两档 URP 资产，返回它的索引。
        ///
        /// 【它在解决什么】本路径的物体 pass 只画声明了 `LightMode = PixelartOpaque` 的材质，
        /// 而像素化域是"数据缓冲 + 几趟全屏着色"——**没有混合，也放不下半透明几何**。
        /// 于是半透明内容（爆炸/水花等 FX、水沫、危险虚线、接触阴影、弹道预览）在新管线下
        /// 会**整类消失**（不报错，就是不画）。这一档让它们由一台"叠加相机"用**自己的材质**在
        /// 像素化成图之上再画一遍：把渲染队列过滤设成"只画 Transparent、不画 Opaque"，
        /// 相机的层掩码留全开——**半透明内容的判据是队列不是层**，不必要求全项目把 FX 挪到某个层上。
        ///
        /// 【已知代价（不要当成已解决）】
        /// <list type="bullet">
        ///   <item>这些内容是**全分辨率**画的，不参与像素化，与成图的颗粒不一致；</item>
        ///   <item>叠加相机自带的深度缓冲是空的 ⇒ 它们的**遮挡关系不参与判定**：白刃落在崖后也会画在崖前。
        ///         真解是"像素化域里的透明趟"（用现成的细像素深度缓冲做遮挡 + 在艺术画布上画），
        ///         那件事另行处置（见 待办）。</item>
        /// </list>
        /// </summary>
        public static bool EnsureOverlayRenderer(out int overlayIndex, out string error)
        {
            overlayIndex = -1;
            error = null;

            UniversalRendererData overlay = EnsureRendererData(PixelartPath.OverlayRendererName);
            if (overlay == null)
            {
                error = "创建/加载叠加渲染器资产失败";
                return false;
            }

            // 这一档要的是**标准的 URP 渲染**（不挂本路径任何一趟），所以清空特征列表。
            RebuildFeatures(overlay, new System.Type[0]);
            SetQueueFilter(overlay, opaque: false, transparent: true);

            foreach (string path in UrpAssetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    error = "未找到 URP 资产：" + path;
                    return false;
                }

                if (!TryAppendRenderer(asset, overlay, out int index))
                {
                    error = "追加叠加渲染器失败：" + path;
                    return false;
                }

                if (overlayIndex < 0)
                    overlayIndex = index;
                else if (overlayIndex != index)
                {
                    error = "两档 URP 资产的叠加渲染器索引不一致（" + overlayIndex + " vs " + index + "）";
                    return false;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// 只留下"画什么队列"的过滤：不透明关、半透明开。URP 的渲染器**本来就有**这两个掩码
        /// （`m_OpaqueLayerMask` / `m_TransparentLayerMask`），本仓从没有把它们当"队列过滤"用过——
        /// 这里用 SerializedObject 写，避免赌 C# 字段的可见性。
        /// </summary>
        static void SetQueueFilter(UniversalRendererData rendererData, bool opaque, bool transparent)
        {
            var serialized = new SerializedObject(rendererData);
            SerializedProperty opaqueProperty = serialized.FindProperty("m_OpaqueLayerMask");
            SerializedProperty transparentProperty = serialized.FindProperty("m_TransparentLayerMask");
            if (opaqueProperty == null || transparentProperty == null)
            {
                Debug.LogError("[PixelartPathInstaller] 渲染器上没有 m_OpaqueLayerMask / m_TransparentLayerMask —— "
                    + "URP 版本变了？叠加档会把不透明几何也画一遍（画面重影）。");
                return;
            }

            opaqueProperty.intValue = opaque ? ~0 : 0;
            transparentProperty.intValue = transparent ? ~0 : 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);
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
        /// 把各 Feature 的 **shader / compute / 调色板资产引用**写满（构建剥离防线）。
        ///
        /// 【为什么用强类型赋值而不是 SerializedObject.FindProperty】按字符串找属性时，字段名写错
        /// 只会静默返回 null、缺陷要到播放器里才现形（表现是"某一趟不生效"）。强类型赋值让名字对不上一律
        /// **编译报错**，在装配之前就把问题挡住。
        /// </summary>
        static void AssignFeatureAssets(UniversalRendererData rendererData)
        {
            var features = rendererData.rendererFeatures;

            PixelartConnectivityFeature connectivity = Find<PixelartConnectivityFeature>(features);
            if (connectivity != null)
            {
                connectivity.checkShader = LoadCompute(PixelartPath.ConnectivityComputeFolder + "/ConnectivityCheck.compute");
                connectivity.floodShader = LoadCompute(PixelartPath.ConnectivityComputeFolder + "/ConnectivityFlood.compute");
                connectivity.resultShader = LoadCompute(PixelartPath.ConnectivityComputeFolder + "/ConnectivityResult.compute");
                EditorUtility.SetDirty(connectivity);
            }

            PixelartOutlineFeature outline = Find<PixelartOutlineFeature>(features);
            if (outline != null)
            {
                outline.outlineShader = LoadShader(PixelartPath.ShaderFolder + "/PixelartOutline.shader");
                EditorUtility.SetDirty(outline);
            }

            PixelartRimLightFeature rimLight = Find<PixelartRimLightFeature>(features);
            if (rimLight != null)
            {
                rimLight.rimLightShader = LoadShader(PixelartPath.ShaderFolder + "/PixelartRimLight.shader");
                rimLight.rimLightCorrectionShader = LoadCompute(PixelartPath.ComputeFolder + "/RimLightCorrection.compute");
                EditorUtility.SetDirty(rimLight);
            }

            PixelartShadingFeature shading = Find<PixelartShadingFeature>(features);
            if (shading != null)
            {
                shading.shadingShader = LoadShader(PixelartPath.ShaderFolder + "/PixelartShading.shader");
                EditorUtility.SetDirty(shading);
            }

            PixelartColorCorrectionFeature colorCorrection = Find<PixelartColorCorrectionFeature>(features);
            if (colorCorrection != null)
            {
                colorCorrection.colorCorrectionShader = LoadShader(PixelartPath.ShaderFolder + "/PixelartColorCorrection.shader");
                colorCorrection.palette = EnableFramePalette ? EnsurePaletteAsset() : null;
                if (!EnableFramePalette)
                {
                    Debug.LogWarning("[PixelartPathInstaller] 帧级调色板**本轮关闭**（palette 字段置空 ⇒ 那一趟整趟跳过）。"
                        + "原因：首轮出图实测 LUT 索引口径有问题——背景蓝灰被映射成暗紫，整屏发怪，"
                        + "而且它把判据脚本的颜色假设一起带偏了（连墨线检测器都认不出墨色）。"
                        + "调色板资产本身仍然生成（" + PixelartPath.PaletteAssetPath + "），"
                        + "把 EnableFramePalette 改回 true 并重跑本装配器 + 出包即可打开。");
                }
                EditorUtility.SetDirty(colorCorrection);
            }

            EditorUtility.SetDirty(rendererData);
        }

        /// <summary>
        /// **帧级调色板开关**（P5）。默认关，理由见 <see cref="AssignFeatureAssets"/> 里的告警：
        /// 首轮出图证明 LUT 的索引口径还没对（背景蓝灰被映射成暗紫），开着它会让整屏发怪、
        /// 也让判据脚本的颜色假设失效。关掉时那一趟整趟跳过，画面回到"色带 + 光照"的 v3 口径。
        /// **LUT 口径修好后把这里改回 true**（然后重跑装配器 + 出包）。
        /// </summary>
        const bool EnableFramePalette = false;

        static T Find<T>(List<ScriptableRendererFeature> features) where T : ScriptableRendererFeature
        {
            for (int i = 0; i < features.Count; i++)
            {
                var typed = features[i] as T;
                if (typed != null)
                    return typed;
            }

            Debug.LogError("[PixelartPathInstaller] Cast 渲染器里没有 " + typeof(T).Name
                + "——特征列表被改过？资产引用未写入，该趟在播放器里会被剥离。");
            return null;
        }

        static Shader LoadShader(string path)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
                Debug.LogError("[PixelartPathInstaller] 找不到 shader：" + path
                    + "（编译失败或被改名？）——播放器里这条 pass 会被剥离。");
            return shader;
        }

        static ComputeShader LoadCompute(string path)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (shader == null)
                Debug.LogError("[PixelartPathInstaller] 找不到 compute：" + path
                    + "（编译失败或被改名？）——播放器里这个 kernel 会被剥离。");
            return shader;
        }

        /// <summary>
        /// 生成/刷新帧级调色板资产：**种子色取自本路径的材质色**（创始人 2026-09-22 裁决：
        /// 首版从场景材质色自动生成，观感变化最小），另加墨色与环境暗部色各一档。
        /// </summary>
        static PixelartPalette EnsurePaletteAsset()
        {
            var colors = new List<Color>();
            var seen = new HashSet<string>();

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { PixelartStageKit.MaterialFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || !material.HasProperty("_BaseColor"))
                    continue;

                Color c = material.GetColor("_BaseColor");
                string key = ((int)(c.r * 255f)) + "_" + ((int)(c.g * 255f)) + "_" + ((int)(c.b * 255f));
                if (seen.Add(key))
                    colors.Add(c);
            }

            if (colors.Count == 0)
            {
                Debug.LogWarning("[PixelartPathInstaller] 没扫到任何本路径材质（"
                    + PixelartStageKit.MaterialFolder + "）——调色板会是空的，调色板那一趟自动跳过。"
                    + "先跑 PixelartPilotSetup.BuildAll 造材质再重跑本装配器。");
                return null;
            }

            // 墨色与环境暗部色必须进板：墨线像素会被映射到最近的板色，板里没有近黑就会把线染成别的暗色。
            colors.Add(new Color(0.070588f, 0.047059f, 0.078431f, 1f));
            colors.Add(RenderSettings.ambientMode == AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor);

            EnsureFolder(System.IO.Path.GetDirectoryName(PixelartPath.PaletteAssetPath).Replace('\\', '/'));
            var palette = PixelartPalette.CreateFromColors(PixelartPath.PaletteAssetPath, colors);
            Debug.Log("[PixelartPathInstaller] 调色板资产已刷新：" + PixelartPath.PaletteAssetPath
                + "（" + colors.Count + " 色，种子来自本路径材质色 + 墨色 + 环境暗部色）。");
            return palette;
        }

        /// <summary>
        /// 打开两档 URP 资产上的**附加光与主光阴影**（色带贴实时阴影需要它们）。
        /// 属性名在 URP 各版本间改过，故按候选名逐个试；一个都没找到就明确告警，不静默跳过。
        /// </summary>
        static void EnsureUrpLightingSettings()
        {
            string[] additionalLightCandidates =
            {
                "m_AdditionalLightsRenderingMode",   // 0=Disabled 1=PerVertex 2=PerPixel
                "m_AdditionalLightsMode",
            };
            string[] additionalShadowCandidates =
            {
                "m_AdditionalLightShadowsSupported",
                "m_AdditionalLightsCastShadows",
            };
            string[] mainShadowCandidates =
            {
                "m_MainLightShadowsSupported",
            };

            foreach (string path in UrpAssetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                    continue;

                var so = new SerializedObject(asset);
                bool ok = SetFirstFound(so, additionalLightCandidates, 2);   // 2 = Per Pixel
                ok &= SetFirstFound(so, additionalShadowCandidates, 1);
                ok &= SetFirstFound(so, mainShadowCandidates, 1);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);

                if (!ok)
                {
                    Debug.LogWarning("[PixelartPathInstaller] URP 资产 " + path
                        + " 的附加光/阴影开关没能全部写入（属性名未命中）。"
                        + "请在 Inspector 的 Lighting 里手工确认：Additional Lights = Per Pixel、"
                        + "Cast Shadows 打开、Main Light 的 Cast Shadows 打开。"
                        + "这两项不开时表现是「阴影永远不出现且没有任何报错」。");
                }
            }
        }

        static bool SetFirstFound(SerializedObject so, string[] names, int value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty property = so.FindProperty(names[i]);
                if (property == null)
                    continue;
                if (property.propertyType == SerializedPropertyType.Boolean)
                    property.boolValue = value != 0;
                else
                    property.intValue = value;
                return true;
            }

            return false;
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
