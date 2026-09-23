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

        /// <summary>伤害数字贴图缓存上限（超过则按插入序驱逐最旧条目给新数字腾位）。</summary>
        const int DamageNumberCacheLimit = 64;

        static readonly Dictionary<string, Texture2D> DamageNumberCache = new Dictionary<string, Texture2D>();

        /// <summary>伤害数字缓存键的插入序（从旧到新）：驱逐按它从最旧开始。</summary>
        static readonly List<string> DamageNumberOrder = new List<string>();

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

            if (DamageNumberCache.TryGetValue(key, out Texture2D cached))
            {
                if (cached != null)
                    return cached;
                // 条目已被外部销毁（Unity 假 null）：清掉旧键再重建，保持缓存与插入序一致
                DamageNumberCache.Remove(key);
                DamageNumberOrder.Remove(key);
            }

            EvictOldestDamageNumber();

            Color32[] pixels = FxDigitFont.CreatePixels(
                text, scale, 1, fill, outline, out int width, out int height);
            Texture2D texture = Upload("FxDmg_" + text, width, height, pixels, FilterMode.Point);
            DamageNumberCache[key] = texture;
            DamageNumberOrder.Add(key);
            return texture;
        }

        /// <summary>
        /// 缓存满 <see cref="DamageNumberCacheLimit"/> 张时按插入序逐条驱逐最旧条目，
        /// 跳过最近创建的一条（屏上正在飘的数字多半引用它）。
        /// 【为什么不能整体 Destroy + Clear】存活中的伤害数字材质仍引用着各自贴图，
        /// 整体清空会让屏上所有数字瞬间丢贴图；逐条驱逐只牺牲最旧的，破坏面最小。
        /// </summary>
        static void EvictOldestDamageNumber()
        {
            if (DamageNumberCache.Count < DamageNumberCacheLimit)
                return;

            int newestIndex = DamageNumberOrder.Count - 1;
            for (int i = 0; i < newestIndex && DamageNumberCache.Count >= DamageNumberCacheLimit; i++)
            {
                string key = DamageNumberOrder[i];
                if (DamageNumberCache.TryGetValue(key, out Texture2D texture))
                {
                    if (texture != null)
                        Object.Destroy(texture);
                    DamageNumberCache.Remove(key);
                }
                DamageNumberOrder.RemoveAt(i);
                i--;
            }
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
