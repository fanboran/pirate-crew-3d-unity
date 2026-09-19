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
                case Shape.Chip: return new Spec { size = 32, radius = UiSkin.RadiusChip, border = 8 };
                case Shape.Slot: return new Spec { size = 48, radius = UiSkin.RadiusSlot, border = 12 };
                case Shape.Pill: return new Spec { size = 20, radius = 10f, border = 6 };
                case Shape.Ring: return new Spec { size = 96, radius = 48f, border = 0 };
                case Shape.Circle: return new Spec { size = 48, radius = 24f, border = 0 };
                case Shape.PanelInk: return new Spec { size = 48, radius = UiSkin.RadiusPanel, border = 12 };
                case Shape.BarTrack: return new Spec { size = 20, radius = 10f, border = 6 };
                default: return new Spec { size = 32, radius = 6f, border = 8 };
            }
        }

        // ------------------------------------------------------------------
        // 设计语言修饰（2026-09-19 用户裁决"连血条都要有风格修饰"后加入；
        // 设计意图详见 docs/设计/UI设计语言.md §三.3）
        //
        // 【两段卡通明暗】tintable 家族不做纯平涂：贴图灰度分"顶亮带 1.0 / 主段 ~0.88"
        // 两档阶梯——乘色后顶带=染色全值、主段=染色略暗，任意色相下都保留卡通体积
        // （糖豆人/哈迪斯填充的"上受光"读感，不用渐变保持平涂纪律）。
        // 【凹槽构造线】BarTrack = 顶 1px 内反光 + 底 30% 暗带 + 两端 2px 端箍
        // （木桶箍的极度抽象——凹槽是"被箍住的容器"不是一条黑缝）。
        // 【切片安全】所有修饰只依赖 y（垂直）或落在九宫格 border 区（端箍），
        // 任意拉伸下形态稳定；依赖 x 的斜纹类纹理会被中段拉伸抹除，一律不做。
        // ------------------------------------------------------------------

        /// <summary>tintable 件主段灰度（顶亮带恒 1.0；乘染色 = 主段略暗）。</summary>
        const float ShadeLow = 0.88f;

        /// <summary>Pill（血条填充）主段更深——液体的上受光更强。</summary>
        const float ShadeLowPill = 0.82f;

        /// <summary>顶亮带的底线（贴图 y 上 32% 为亮带；py 向上为正）。</summary>
        const float HighlightTopRatio = 0.32f;

        static float ShadeOf(Shape shape, float py, int n)
        {
            if (shape == Shape.PanelInk || shape == Shape.BarTrack)
                return 1f;   // 固定色家族的修饰走构造线，不做明暗阶梯

            float hiLine = (n * 0.5f) - n * HighlightTopRatio;   // py > hiLine = 亮带
            if (shape == Shape.Ring || shape == Shape.Circle)
                hiLine = 0f;                                     // 圆件以赤道分界
            return py > hiLine ? 1f : (shape == Shape.Pill ? ShadeLowPill : ShadeLow);
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

                    if (dIn >= 1f)
                    {
                        if (tintable)
                        {
                            // 两段卡通明暗：顶亮带 1.0 / 主段 ~0.88（乘色后保留体积）。
                            float shade = ShadeOf(shape, py, n);
                            c = new Color(c.r * shade, c.g * shade, c.b * shade, c.a);
                        }
                        else if (shape == Shape.BarTrack)
                        {
                            // 凹槽构造线：顶 1px 内反光 → 底 40% 暗带 → 两端 3px 端箍。
                            // 【端箍必须落在 border 区内】九宫格中段是 border 内侧整段
                            // 拉伸，画在中段采样区的竖线会被拉成杂线；只有 border 区
                            // （BarTrack border=6，两端 6px）被原样复制到条的两端。
                            if (py > half - 2f)
                                c = Tint(c, 0.16f);
                            else if (py < -(half - 1f - n * 0.40f))
                                c = new Color(c.r * 0.70f, c.g * 0.70f, c.b * 0.70f, c.a);

                            float ax = Mathf.Abs(px);
                            if (ax > half - 5.5f && ax < half - 2.5f)
                                c = Tint(c, 0.20f);
                        }
                        else if (shape == Shape.PanelInk && py > half - 2f)
                        {
                            // 面板上沿 1px 微光（与凹槽顶反光同语言）。
                            c = Tint(c, 0.06f);
                        }
                    }

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

        /// <summary>向白色抬升（构造线的反光叠加；tint=0.1 即混入 10% 白）。</summary>
        static Color Tint(Color color, float amount)
        {
            return Color.Lerp(color, Color.white, amount);
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
