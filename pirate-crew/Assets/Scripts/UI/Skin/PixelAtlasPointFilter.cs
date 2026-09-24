using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 位图字体动态图集的 Point 过滤纠偏件：TMP 动态图集新增字形页时会把新贴图重置成
    /// Bilinear——位图口径（1 字体像素 = 1 艺术像素/屏幕像素，字号即原生档）下任何
    /// 双线性都是栅格糊掉。本件每帧把字体图集钉回 Point。
    /// 经 <see cref="Ensure"/> 挂载（每字体一个隐藏宿主，跨场景常驻，勿手工挂）。
    /// </summary>
    public sealed class PixelAtlasPointFilter : MonoBehaviour
    {
        static readonly Dictionary<TMP_FontAsset, PixelAtlasPointFilter> s_active = new();

        /// <summary>确保某字体有纠偏件在岗（幂等；宿主销毁后自动重建）。</summary>
        public static void Ensure(TMP_FontAsset font)
        {
            if (font == null)
                return;
            if (s_active.TryGetValue(font, out PixelAtlasPointFilter existing)
                && existing != null)
                return;
            if (s_active.ContainsKey(font))
                s_active.Remove(font);

            var host = new GameObject("PixelAtlasPointFilter(" + font.name + ")");
            if (Application.isPlaying)
                DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            var filter = host.AddComponent<PixelAtlasPointFilter>();
            filter.font = font;
            s_active[font] = filter;
        }

        public TMP_FontAsset font;

        void LateUpdate()
        {
            if (font == null || font.atlasTextures == null)
                return;
            foreach (Texture2D texture in font.atlasTextures)
            {
                if (texture == null)
                    continue;
                if (texture.filterMode != FilterMode.Point)
                    texture.filterMode = FilterMode.Point;
            }
        }
    }
}
