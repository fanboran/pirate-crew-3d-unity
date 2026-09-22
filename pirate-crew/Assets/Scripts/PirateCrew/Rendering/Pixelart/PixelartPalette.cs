using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **帧级调色板（P5）的数据侧**：一组颜色 + 把颜色烘成 **LUT** 的能力。
    ///
    /// 【它在整条链里的位置】`PixelartColorCorrection.shader` 逐像素把画面换成"板上最近的那个色"，
    /// 而"哪个色最近"是**烘**出来的：`PaletteGenerationCIEDE.compute` 用 CIEDE2000 把一张 RGB 均匀
    /// 立方映射到本资产的颜色表，结果是一张 `res×res × res` 的 2D 条带 LUT（布局见那个 compute 的文件头）。
    /// 于是运行期只需要一次点采样——不需要在 shader 里跑色差、也不需要传颜色数组。
    ///
    /// 【两个接口为什么长这样】
    /// <list type="bullet">
    ///   <item><see cref="CreateFromColors"/>：装配器（`PixelartPathInstaller.EnsurePaletteAsset`）
    ///         用**试点场景的材质色 + 墨色 + 环境暗部色**调它生成/刷新资产（创始人 2026-09-22 裁决：
    ///         首版从场景材质色自动生成、观感变化最小）。**已存在的资产就地改**而不重建文件，
    ///         免得渲染器上那份序列化引用跟着资产一起被换掉。</item>
    ///   <item><see cref="BuildLut"/>：惰性构建 + 颜色/分辨率变了才重烘 + 可释放（RT 自管生命周期）。</item>
    /// </list>
    ///
    /// 【compute 引用为什么必须落在资产字段上】播放器构建只收"被资产引用链摸到"的资源；
    /// 只在 C# 里 <c>Resources.Load</c>/<c>Shader.Find</c> 的 compute 会被剥离，症状是
    /// "调色板那一趟静默失效、没有任何报错"。故 <see cref="bakeComputeShader"/> 是序列化字段
    /// （由 <see cref="CreateFromColors"/> 在编辑器里按固定路径自动写满，也可以手工改）。
    /// </summary>
    [CreateAssetMenu(fileName = "PixelartPalette", menuName = "PirateCrew/像素化路径/帧级调色板")]
    public sealed class PixelartPalette : ScriptableObject
    {
        /// <summary>烘焙用 compute 的固定路径（`CreateFromColors` 在编辑器里按它自动写引用）。</summary>
        public const string BakeComputeAssetPath = "Assets/Pixelart/Compute/Palette/PaletteGenerationCIEDE.compute";

        /// <summary>烘焙 kernel 名（`PaletteGenerationCIEDE.compute` 里 `#pragma kernel Main`）。</summary>
        public const string BakeKernelName = "Main";

        /// <summary>默认 LUT 分辨率（= 每个通道的格子数；v3 的 `GenerateTexture2D(resolution = 16)` 同值）。</summary>
        public const int DefaultResolution = 16;

        /// <summary>分辨率上限：LUT 宽 = res²、面积 = res³，64 已经是 26 万纹素（烘焙要 res³ 次比对）。</summary>
        public const int MaxResolution = 64;

        [Tooltip("调色板颜色（线性分量）。每个值都是「最终画面被允许出现的颜色」之一。")]
        public Color[] colors = new Color[0];

        [Tooltip("烘焙用 compute（PaletteGenerationCIEDE）。留空 = 本调色板不生效（那一趟整趟跳过）。")]
        public ComputeShader bakeComputeShader;

        [Tooltip("LUT 分辨率 = 每个通道的格子数。16 ⇒ 256×16 的 LUT。")]
        [Range(2, MaxResolution)]
        public int resolution = DefaultResolution;

        // ---- 与 .compute / .shader 之间的名字契约（本类里唯一的字符串字面量）----
        static readonly int s_OutputBufferId = Shader.PropertyToID("_OutputBuffer");
        static readonly int s_PaletteId = Shader.PropertyToID("_Palette");
        static readonly int s_ResolutionId = Shader.PropertyToID("_Resolution");
        static readonly int s_CountId = Shader.PropertyToID("_Count");

        /// <summary>texel 尺寸的通道数（StructuredBuffer&lt;float3&gt;：stride = 3 个 float）。</summary>
        const int PaletteStrideBytes = sizeof(float) * 3;

        // 运行期缓存（不序列化）：LUT 与"它对应哪一份输入"的指纹。
        [NonSerialized] RenderTexture m_Lut;
        [NonSerialized] int m_BuiltStamp;

        /// <summary>颜色数（= 烘 LUT 时的比对次数；空板 = 0）。</summary>
        public int ColorCount { get { return colors != null ? colors.Length : 0; } }

        /// <summary>已烘好的 LUT（没烘过 / 已释放时为 null）。给需要直接看一眼的调试代码用。</summary>
        public RenderTexture BakedLut { get { return m_Lut; } }

        /// <summary>
        /// 取 LUT：**惰性构建 + 变了才重烘 + 已释放则重建**。
        /// 返回 null 表示"本调色板不可用"（没有色、没有 compute、或 kernel 找不到）——
        /// 调用方（`PixelartColorCorrectionFeature`）见到 null 就**整趟跳过**，即"不映射，可回退"。
        /// </summary>
        public RenderTexture BuildLut()
        {
            if (colors == null || colors.Length == 0)
            {
                ReleaseLut();
                return null;
            }

            if (bakeComputeShader == null)
            {
                Debug.LogError("[PixelartPalette] 调色板没有 compute 引用（"
                    + BakeComputeAssetPath + "）——那一趟会整趟跳过。"
                    + "重跑装配器 PirateCrew/Pixelart/装配像素化路径渲染器可自动写满它。");
                return null;
            }

            int res = Mathf.Clamp(resolution, 2, MaxResolution);

            // 【指纹口径】分辨率 + 色数 + 每个色的量化分量（量化到 8 位与 LUT 的存储精度一致：
            // 亚 8 位的变化烘出来本来就是同一个 LUT，不值得重烘）。
            int stamp = ComputeStamp(res);
            if (m_Lut != null && m_BuiltStamp == stamp && m_Lut.IsCreated())
                return m_Lut;

            ReleaseLut();
            m_Lut = Bake(res);
            m_BuiltStamp = m_Lut != null ? stamp : 0;
            return m_Lut;
        }

        /// <summary>释放已烘的 LUT（禁用/销毁/重烘前调用；幂等）。</summary>
        public void ReleaseLut()
        {
            if (m_Lut == null)
                return;

            if (m_Lut.IsCreated())
                m_Lut.Release();
            // 【销毁要走 DestroyImmediate/Destroy 分支】播放器里 DestroyImmediate 不合法，
            // 而本资产既可能在编辑器（装配/出图）也可能在播放器（采集）里被用。
            if (Application.isPlaying)
                Destroy(m_Lut);
            else
                DestroyImmediate(m_Lut);
            m_Lut = null;
            m_BuiltStamp = 0;
        }

        void OnDisable()
        {
            ReleaseLut();
        }

        void OnDestroy()
        {
            ReleaseLut();
        }

        int ComputeStamp(int res)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + res;
                hash = hash * 31 + colors.Length;
                for (int i = 0; i < colors.Length; i++)
                {
                    Color c = colors[i];
                    hash = hash * 31 + Mathf.RoundToInt(c.r * 255f);
                    hash = hash * 31 + Mathf.RoundToInt(c.g * 255f);
                    hash = hash * 31 + Mathf.RoundToInt(c.b * 255f);
                    hash = hash * 31 + Mathf.RoundToInt(c.a * 255f);
                }
                return hash;
            }
        }

        /// <summary>
        /// 真烘一趟：三维派发（`[numthreads(8,8,8)]` ⇒ 每维 `CeilToInt(res/8)` 组）。
        /// 【为什么不走 `ComputeShaderPass` 那类通用封装】它的 z 恒为 1，装不下三维派发
        /// （蓝图 §4.2/§6 P5 明确点过），所以这里像 v3 `SloanePixelartPalette.cs:62-63` 一样自己算组数。
        /// </summary>
        RenderTexture Bake(int res)
        {
            int kernel = bakeComputeShader.FindKernel(BakeKernelName);
            if (kernel < 0)
            {
                Debug.LogError("[PixelartPalette] compute 里找不到 kernel「" + BakeKernelName
                    + "」——那一趟会整趟跳过（改过 kernel 名？）");
                return null;
            }

            // LUT 尺寸：宽 = res²（横向 res 个蓝分片 × 片内 res 个红）、高 = res。
            // 【格式】R8G8B8A8（ARGB32）**非 sRGB**：里面存的是调色板色的原值，
            // 一旦按 sRGB 解码，采出来的颜色就会比板上浅一档（这类错层靠肉眼很难判）。
            var desc = new RenderTextureDescriptor(res * res, res, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1,
                volumeDepth = 1,
                dimension = TextureDimension.Tex2D,
            };

            var lut = new RenderTexture(desc)
            {
                name = "PixelartPaletteLut",
                // 点采样是"输出必然落在板上"的前提（线性过滤会混出板外的中间色）；
                // clamp 是因为索引公式在 0/1 边界上会算出正好等于 1 的 uv。
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            lut.Create();

            var paletteBuffer = new ComputeBuffer(colors.Length, PaletteStrideBytes);
            var data = new Vector3[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                data[i] = new Vector3(colors[i].r, colors[i].g, colors[i].b);
            paletteBuffer.SetData(data);

            bakeComputeShader.SetTexture(kernel, s_OutputBufferId, lut);
            bakeComputeShader.SetBuffer(kernel, s_PaletteId, paletteBuffer);
            bakeComputeShader.SetInt(s_ResolutionId, res);
            bakeComputeShader.SetInt(s_CountId, colors.Length);

            int groups = Mathf.CeilToInt(res / 8f);
            bakeComputeShader.Dispatch(kernel, groups, groups, groups);

            paletteBuffer.Release();
            return lut;
        }

        /// <summary>
        /// 生成/刷新调色板资产：**已存在就地改**（保 GUID、保渲染器上那份引用），不存在才新建。
        ///
        /// 【为什么是编辑器侧】它要写资产文件。放在本类里而不是装配器里，是为了让"调色板资产长什么样"
        /// 与调色板的字段定义待在一起；装配器只负责决定"喂哪些色"。
        /// </summary>
        public static PixelartPalette CreateFromColors(string assetPath,
            System.Collections.Generic.IEnumerable<Color> colors)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[PixelartPalette] CreateFromColors 的 assetPath 为空。");
                return null;
            }

            var list = new System.Collections.Generic.List<Color>();
            if (colors != null)
            {
                foreach (Color c in colors)
                    list.Add(c);
            }

            var palette = UnityEditor.AssetDatabase.LoadAssetAtPath<PixelartPalette>(assetPath);
            bool created = palette == null;
            if (created)
                palette = CreateInstance<PixelartPalette>();

            palette.colors = list.ToArray();

            // compute 引用按固定路径补上（**只在为空时补**：手工换过就别覆盖）。
            if (palette.bakeComputeShader == null)
            {
                palette.bakeComputeShader =
                    UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(BakeComputeAssetPath);
            }

            if (created)
                UnityEditor.AssetDatabase.CreateAsset(palette, assetPath);
            else
                UnityEditor.EditorUtility.SetDirty(palette);

            UnityEditor.AssetDatabase.SaveAssets();
            return palette;
#else
            Debug.LogError("[PixelartPalette] CreateFromColors 只在编辑器里可用（它要写资产文件）。");
            return null;
#endif
        }
    }
}
