using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 队伍形状标记（色盲冗余）：红队 = 三角、蓝队 = 圆。
    /// 8×8 原生点阵、白形（由调用方按队色染色）、Point/Clamp/无 mip。
    ///
    /// 【为什么不用文字】队伍归属不该依赖「红/蓝」二字：小字号中文字数多、扫描慢，
    /// 形状剪影在 8px 仍可辨，且与主色一起构成「颜色 + 形状」双编码。
    /// </summary>
    public static class TeamGlyphs
    {
        /// <summary>点阵边长（px）。</summary>
        public const int Size = 8;

        static Sprite _red;
        static Sprite _blue;

        /// <summary>取队伍标记（0=红=三角，其余=蓝=圆）。</summary>
        public static Sprite Get(int teamIndex)
        {
            if (teamIndex == 0)
                return _red != null ? _red : (_red = BuildTriangle());
            return _blue != null ? _blue : (_blue = BuildCircle());
        }

        /// <summary>静态缓存跨播放复位（与 UiGlyphs 同纪律：关域重载时 Sprite/Texture 不跨局滞留）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticResources()
        {
            _red = null;
            _blue = null;
        }

        static Sprite BuildTriangle()
        {
            // 向上实心三角：底部 8 宽、顶部 2 宽。
            bool[,] mask =
            {
                { false, false, false, true, true, false, false, false },
                { false, false, true, true, true, true, false, false },
                { false, true, true, true, true, true, true, false },
                { true, true, true, true, true, true, true, true },
                { true, true, true, true, true, true, true, true },
                { true, true, true, true, true, true, true, true },
                { true, true, true, true, true, true, true, true },
                { true, true, true, true, true, true, true, true },
            };
            return Build(mask, "TeamGlyph_Triangle");
        }

        static Sprite BuildCircle()
        {
            bool[,] mask =
            {
                { false, false, false, false, false, false, false, false },
                { false, false, true, true, true, true, false, false },
                { false, true, true, true, true, true, true, false },
                { true, true, true, true, true, true, true, true },
                { true, true, true, true, true, true, true, true },
                { false, true, true, true, true, true, true, false },
                { false, false, true, true, true, true, false, false },
                { false, false, false, false, false, false, false, false },
            };
            return Build(mask, "TeamGlyph_Circle");
        }

        static Sprite Build(bool[,] mask, string name)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                // 纹理原点在左下；mask 行 0 是视觉顶部。
                int srcY = Size - 1 - y;
                for (int x = 0; x < Size; x++)
                {
                    pixels[y * Size + x] = mask[srcY, x]
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
        }
    }
}
