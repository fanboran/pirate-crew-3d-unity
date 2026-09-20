using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 特效贴图的运行时缓存：把 <see cref="FxTextureRules"/> 生成的像素上传成 <c>Texture2D</c>。
    ///
    /// 【与编辑器资产的关系】运行时走内存贴图（`hideFlags = DontSave`，不进场景、不落盘），
    /// 编辑器执行 `Assets/Editor/FxAssetBuilder.cs` 时用**同一份算法**烘出 `Assets/Art/Textures/Fx/*.png`
    /// 持久资产（供美术查看/调参、以及把 FX shader 带进构建）。两边像素一致。
    ///
    /// 【导入设置】落盘资产在 FxAssetBuilder 里写死：形状类 sRGB + alphaIsTransparency +
    /// Clamp + 无 mipmap + 按种类 Bilinear/Point；运行时内存贴图的 filterMode 同口径。
    ///
    /// 【ECall 边界】本类触碰 <c>Texture2D</c>，只在 Unity 运行时/编辑器里用，不进无头断言。
    /// </summary>
    public static class FxTextures
    {
        static readonly Texture2D[] Cache = new Texture2D[FxTextureRules.All.Length];

        /// <summary>伤害数字贴图缓存上限（超过则整体清空重建，避免数字变化多时无限增长）。</summary>
        const int DamageNumberCacheLimit = 64;

        static readonly Dictionary<string, Texture2D> DamageNumberCache = new Dictionary<string, Texture2D>();

        /// <summary>取（或创建）形状贴图。算法失败/尺寸非法时返回 1×1 白图兜底，绝不返回 null。</summary>
        public static Texture2D Get(FxTextureKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= Cache.Length)
                return GetFallback();

            Texture2D cached = Cache[index];
            if (cached != null)
                return cached;

            int size = FxTextureRules.DefaultSize(kind);
            Color32[] pixels = FxTextureRules.CreatePixels(kind, size);
            Texture2D texture = Upload("FxTex_" + kind, size, size, pixels, FxTextureRules.DefaultFilter(kind));
            Cache[index] = texture;
            return texture;
        }

        /// <summary>
        /// 取（或创建）伤害数字贴图：按「文本 + 字号 + 填充色 + 描边色」缓存。
        /// 颜色直接烘进像素（描边固定 #2A2A2A，Art Bible §2.5 场景叠加文字要求），
        /// 运行时只用材质 alpha 做淡出，不做二次染色，保证描边不会被染红。
        /// </summary>
        public static Texture2D GetDamageNumber(string text, int scale, Color32 fill, Color32 outline)
        {
            string key = text + "|" + scale + "|" + fill.r + "," + fill.g + "," + fill.b
                       + "|" + outline.r + "," + outline.g + "," + outline.b;

            if (DamageNumberCache.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;

            if (DamageNumberCache.Count >= DamageNumberCacheLimit)
            {
                foreach (KeyValuePair<string, Texture2D> pair in DamageNumberCache)
                {
                    if (pair.Value != null)
                        Object.Destroy(pair.Value);
                }
                DamageNumberCache.Clear();
            }

            Color32[] pixels = FxDigitFont.CreatePixels(
                text, scale, 1, fill, outline, out int width, out int height);
            Texture2D texture = Upload("FxDmg_" + text, width, height, pixels, FilterMode.Point);
            DamageNumberCache[key] = texture;
            return texture;
        }

        /// <summary>1×1 白图兜底（任何算法异常时都把渲染降级成"单色方块"而不是崩）。</summary>
        static Texture2D _fallback;

        static Texture2D GetFallback()
        {
            if (_fallback == null)
                _fallback = Upload("FxTex_Fallback", 1, 1, new[] { new Color32(255, 255, 255, 255) }, FilterMode.Point);
            return _fallback;
        }

        static Texture2D Upload(string name, int width, int height, Color32[] pixels, FilterMode filter)
        {
            var texture = new Texture2D(
                Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = filter,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }
    }
}
