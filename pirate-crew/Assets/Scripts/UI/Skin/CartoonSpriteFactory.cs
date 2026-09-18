using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 多彩卡通平色九宫格皮肤工厂（本波次 UI 换肤的核心算法，纯像素程序化）。
    ///
    /// 【tintable 模板（最重要的设计）】彩色件不逐色烘图：贴图烘成
    /// **白填充 + 0.16 深灰 1px 细边**的模板，运行时 <c>Image.color</c> 乘色染色——
    /// 乘法规则是 黑(0)保黑、白(1)取染色，于是 17 个武器彩色格 / 7 个职业色 pip /
    /// 双队血条共用**一张** Chip 模板，换色相零资产成本（"五颜六色"不等于资产爆炸）。
    ///
    /// 【固定色家族】深底面板 / 血条凹槽颜色固定（深底上 tintable 的灰边会不可辨），
    /// 直接烘焙成品色，边线比底亮一档。
    ///
    /// 【形状语言】<see cref="UiSkin"/> 的圆角 Token（8/6/10），1px 细边，无渐变无投影——
    /// 平涂 + SDF 抗锯齿边缘，仅此而已（拟物极度抽离）。
    ///
    /// 【双轨同源】运行时内存 Sprite（<see cref="Get"/>）与编辑器 PNG 烘焙
    /// （<c>UiSkinAssetBaker</c> 调 <see cref="CreateTexture"/>）共用同一算法，
    /// 两边外观一致——沿用 <see cref="UiSprites"/> 已验证的模式。
    /// 本类触碰 Texture2D（ECall），只可在 Unity 运行时 / 编辑器里用，不进无头断言；
    /// 几何判定（<see cref="RoundedRectCoverage"/>）是纯函数可无头测。
    /// </summary>
    public static class CartoonSpriteFactory
    {
        /// <summary>皮肤形状（tintable 家族用 Image.color 染色；固定色家族见注释）。</summary>
        public enum Shape
        {
            /// <summary>按钮 / 小 chip（圆角 6，tintable）。</summary>
            Chip,

            /// <summary>图标格 / 大 chip（圆角 10，tintable；武器格 / pip 底）。</summary>
            Slot,

            /// <summary>全圆 pill（血条填充 / ghost 残影 / HP 条，tintable）。</summary>
            Pill,

            /// <summary>圆环（罗盘 / 徽章环，tintable；不切片，按原尺寸显示）。</summary>
            Ring,

            /// <summary>实心圆（pip 圆点 / 遮罩图，tintable；不切片）。</summary>
            Circle,

            /// <summary>深底面板（固定色 <see cref="UiSkin.InkDeep"/> + 1px 亮边线，不 tint）。</summary>
            PanelInk,

            /// <summary>血条凹槽（固定色 <see cref="UiSkin.BarTrackInk"/>，不 tint）。</summary>
            BarTrack,
        }

        /// <summary>tintable 细边的灰度（乘染色后 = 16% 亮度的同色系深边）。</summary>
        const float EdgeGray = 0.16f;

        /// <summary>固定色家族的边线亮度提升（底色 ×1.35 + 少量抬升，1px 分隔线）。</summary>
        static Color EdgeLineOf(Color baseColor)
        {
            return new Color(
                Mathf.Min(1f, baseColor.r * 1.35f + 0.06f),
                Mathf.Min(1f, baseColor.g * 1.35f + 0.06f),
                Mathf.Min(1f, baseColor.b * 1.35f + 0.06f),
                1f);
        }

        /// <summary>各形状的贴图边长 / 圆角 / 切片边框（九宫格四边等宽）。</summary>
        struct Spec
        {
            public int size;
            public float radius;
            public int border;
        }

        static Spec SpecOf(Shape shape)
        {
            switch (shape)
            {
                case Shape.Chip: return new Spec { size = 32, radius = 6f, border = 8 };
                case Shape.Slot: return new Spec { size = 48, radius = 10f, border = 12 };
                case Shape.Pill: return new Spec { size = 20, radius = 10f, border = 6 };
                case Shape.Ring: return new Spec { size = 96, radius = 48f, border = 0 };
                case Shape.Circle: return new Spec { size = 48, radius = 24f, border = 0 };
                case Shape.PanelInk: return new Spec { size = 48, radius = 8f, border = 12 };
                case Shape.BarTrack: return new Spec { size = 20, radius = 10f, border = 6 };
                default: return new Spec { size = 32, radius = 6f, border = 8 };
            }
        }

        static readonly Dictionary<Shape, Sprite> Cache = new Dictionary<Shape, Sprite>();

        /// <summary>取内存 Sprite（运行时动态件用；按形状缓存，颜色由 Image.color 承担）。</summary>
        public static Sprite Get(Shape shape)
        {
            if (Cache.TryGetValue(shape, out Sprite cached) && cached != null)
                return cached;

            Texture2D texture = CreateTexture(shape, out Vector4 border);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border,
                false);
            sprite.name = "Cartoon_" + shape;
            Cache[shape] = sprite;
            return sprite;
        }

        /// <summary>
        /// 画一张皮肤贴图。编辑器 PNG 烘焙（<c>UiSkinAssetBaker</c>）与运行时内存 Sprite 共用。
        /// </summary>
        public static Texture2D CreateTexture(Shape shape, out Vector4 border)
        {
            Spec spec = SpecOf(shape);
            int n = spec.size;
            var pixels = new Color32[n * n];

            bool tintable = shape != Shape.PanelInk && shape != Shape.BarTrack;
            Color fillColor = tintable ? Color.white : FillColorOf(shape);
            Color edgeColor = tintable ? new Color(EdgeGray, EdgeGray, EdgeGray, 1f) : EdgeLineOf(fillColor);
            bool isRing = shape == Shape.Ring;

            float half = n * 0.5f;
            float b = half - 0.5f;
            float innerRadius = isRing ? n * 0.36f : 0f;   // Ring = 环（外缘到 0.36n 为实心）

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;

                    // 圆角矩形 SDF（负 = 形状内；环再叠加内圆挖空）。
                    float sd = SdRoundRect(px, py, b, spec.radius);
                    if (isRing)
                    {
                        float innerSd = Mathf.Sqrt(px * px + py * py) - innerRadius;
                        sd = Mathf.Max(sd, -innerSd);       // 并集挖洞：max(外形状, -内圆)
                    }

                    float dIn = -sd;                        // >0 = 形状内，值 = 向内距离

                    // SDF 抗锯齿覆盖度（形状外 1px 渐出）。
                    float coverage = Mathf.Clamp01(0.5f - sd);
                    if (coverage <= 0f)
                    {
                        pixels[y * n + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // 1px 细边（贴着形状边缘向内）。
                    Color c = dIn < 1f ? edgeColor : fillColor;
                    pixels[y * n + x] = new Color(c.r, c.g, c.b, coverage);
                }
            }

            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            texture.name = "CartoonTex_" + shape;
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, false);

            border = new Vector4(spec.border, spec.border, spec.border, spec.border);
            return texture;
        }

        static Color FillColorOf(Shape shape)
        {
            switch (shape)
            {
                case Shape.PanelInk: return UiSkin.InkDeep;
                case Shape.BarTrack: return UiSkin.BarTrackInk;
                default: return Color.white;
            }
        }

        /// <summary>
        /// 圆角矩形有符号距离场（负 = 形状内）。纯函数，可无头断言
        /// （中心为负、远角为正、直边距离与参数一致）。
        /// </summary>
        public static float SdRoundRect(float px, float py, float halfExtent, float radius)
        {
            float qx = Mathf.Abs(px) - (halfExtent - radius);
            float qy = Mathf.Abs(py) - (halfExtent - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)
                + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>圆角矩形覆盖度（0..1，SDF 抗锯齿）——几何判定的可测出口。</summary>
        public static float RoundedRectCoverage(float px, float py, float halfExtent, float radius)
        {
            return Mathf.Clamp01(0.5f - SdRoundRect(px, py, halfExtent, radius));
        }
    }
}
