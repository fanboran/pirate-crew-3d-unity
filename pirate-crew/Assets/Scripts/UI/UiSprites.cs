using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 程序化贴图工厂：木板 / 羊皮纸 / 黄铜三段材质语言的九宫格 Sprite。
    ///
    /// 【为什么程序化】规范 §1.2 要求「木板 / 羊皮纸 / 黄铜」实体材质感，且明确禁止引入来源不明的图片素材。
    /// 本类用确定性算法生成像素（木纹 = 确定性哈希噪声条带；圆角 + 描边 = 距离场），
    /// **同一算法同时供**：
    ///   · 运行时列表行/按钮（<see cref="Get"/>，内存 Sprite，不落盘）；
    ///   · 编辑器装配的场景面板（Assets/Editor/MenuUiBuilder 把它烘成 Assets/Art/Sprites/UI/*.png 持久资产）。
    /// 两边共用 <see cref="CreateTexture"/>，保证运行时与编辑器外观一致。
    ///
    /// 【边界】不参与 3D 光照（UGUI 走 UI/Default）；不做高光渐变（规范 §1.2「UI 里要平」）。
    /// 本类触碰 Texture2D（ECall），只可在 Unity 运行时/编辑器里用，不进纯 C# 无头断言。
    /// </summary>
    public static class UiSprites
    {
        /// <summary>可生成的 UI 贴图种类。</summary>
        public enum Kind
        {
            /// <summary>木框深色面板（HUD 面板 / 弹窗底）。</summary>
            PanelWood,

            /// <summary>羊皮纸面板（正文底 / 名册行 / 弹窗信息区）。</summary>
            PanelParchment,

            /// <summary>木板按钮（正常态底）。</summary>
            ButtonWood,

            /// <summary>羊皮纸按钮（次按钮）。</summary>
            ButtonParchment,

            /// <summary>黄铜按钮（主行动点 / 当前页签）。</summary>
            ButtonBrass,

            /// <summary>危险按钮（退出游戏 / 放弃本局）。</summary>
            ButtonDanger,

            /// <summary>血条底槽。</summary>
            BarBackground,

            /// <summary>血条填充。</summary>
            BarFill,

            /// <summary>五角星（星级图标）。</summary>
            Star,

            /// <summary>罗盘玫瑰准星。</summary>
            Crosshair,
        }

        /// <summary>九宫格边框（左、下、右、上），单位 px。</summary>
        const int Slice = 14;

        /// <summary>生成纹理边长（> 2×Slice 才有中间可拉伸区）。</summary>
        const int Size = 48;

        static readonly Dictionary<Kind, Sprite> Cache = new Dictionary<Kind, Sprite>();

        /// <summary>取内存 Sprite（按种类缓存）。运行时列表用。</summary>
        public static Sprite Get(Kind kind)
        {
            if (Cache.TryGetValue(kind, out Sprite cached) && cached != null)
                return cached;

            Vector4 border;
            Texture2D texture = CreateTexture(kind, out border);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border,
                false);
            sprite.name = "Ui_" + kind;
            Cache[kind] = sprite;
            return sprite;
        }

        /// <summary>
        /// 生成像素。编辑器落盘（PNG + TextureImporter）与运行时内存 Sprite 共用本方法。
        /// </summary>
        /// <param name="kind">贴图种类。</param>
        /// <param name="border">输出的九宫格边框（左、下、右、上）。</param>
        public static Texture2D CreateTexture(Kind kind, out Vector4 border)
        {
            int size = Size;
            border = new Vector4(Slice, Slice, Slice, Slice);
            var pixels = new Color32[size * size];

            switch (kind)
            {
                case Kind.Star:
                case Kind.Crosshair:
                    border = Vector4.zero;
                    FillShape(kind, size, pixels);
                    break;
                default:
                    FillPanel(kind, size, pixels);
                    break;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "UiTex_" + kind;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // ------------------------------------------------------------------
        // 面板 / 按钮底（圆角 + 木纹 / 纸纹 + 内描边）
        // ------------------------------------------------------------------

        static void FillPanel(Kind kind, int size, Color32[] pixels)
        {
            Color baseColor = BaseColorOf(kind);
            Color lineColor = LineColorOf(kind);
            float radius = kind == Kind.PanelWood || kind == Kind.PanelParchment ? 8f : 4f;
            bool grainy = kind == Kind.PanelWood || kind == Kind.ButtonWood;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;
                    if (!InsideRounded(x, y, size, radius))
                    {
                        pixels[index] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color c = baseColor;

                    if (grainy)
                    {
                        // 木纹：横向条带 + 确定性噪声，幅度小（不干扰文字对比）。
                        float grain = (Hash(x / 3, y, 11) & 0x3) - 1.5f;   // -1.5 .. 1.5
                        float streak = ((Hash(x, y / 6, 23) & 0x7) - 3.5f) * 0.4f;
                        float delta = (grain + streak) * 0.012f;
                        c = new Color(
                            Mathf.Clamp01(c.r + delta),
                            Mathf.Clamp01(c.g + delta * 0.8f),
                            Mathf.Clamp01(c.b + delta * 0.6f),
                            c.a);
                    }
                    else if (kind == Kind.PanelParchment || kind == Kind.ButtonParchment)
                    {
                        // 纸纹：轻微斑驳 + 边缘微暗，避免大面积死平。
                        float mottle = ((Hash(x / 2, y / 2, 37) & 0x7) - 3.5f) * 0.006f;
                        c = new Color(
                            Mathf.Clamp01(c.r + mottle),
                            Mathf.Clamp01(c.g + mottle),
                            Mathf.Clamp01(c.b + mottle * 0.5f),
                            c.a);
                    }

                    // 内描边（2px）：黄铜框或深色墨线，保证面板边界在任意底上都可辨。
                    int edge = DistanceToEdge(x, y, size);
                    if (edge < 2)
                        c = Color.Lerp(lineColor, c, edge * 0.35f);

                    pixels[index] = c;
                }
            }
        }

        static Color BaseColorOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.PanelWood: return UiTheme.PanelWood;
                case Kind.PanelParchment: return UiTheme.Parchment;
                case Kind.ButtonWood: return UiTheme.WoodMid;
                case Kind.ButtonParchment: return UiTheme.Parchment;
                case Kind.ButtonBrass: return UiTheme.Brass;
                case Kind.ButtonDanger: return UiTheme.DangerDark;
                case Kind.BarBackground: return new Color(0.12f, 0.12f, 0.12f, 0.9f);
                case Kind.BarFill: return new Color(0.42f, 0.78f, 0.34f, 1f);
                default: return UiTheme.WoodDark;
            }
        }

        static Color LineColorOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.PanelParchment:
                case Kind.ButtonParchment:
                    return UiTheme.WoodDark;
                case Kind.ButtonBrass:
                    return UiTheme.Ink;
                case Kind.BarBackground:
                    return UiTheme.Ink;
                default:
                    return UiTheme.Brass;
            }
        }

        // ------------------------------------------------------------------
        // 形状：五角星 / 罗盘准星（透明背景 + 实色形状）
        // ------------------------------------------------------------------

        static void FillShape(Kind kind, int size, Color32[] pixels)
        {
            float center = size * 0.5f;
            float outer = size * 0.46f;
            Color fill = kind == Kind.Star ? UiTheme.Brass : UiTheme.Select;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f - center;
                    float fy = y + 0.5f - center;   // 纹理坐标 y 向上
                    bool inside;

                    if (kind == Kind.Star)
                    {
                        inside = InsideStar(fx, fy, outer, outer * 0.44f);
                    }
                    else
                    {
                        float r = Mathf.Sqrt(fx * fx + fy * fy);
                        bool ring = r <= outer && r >= outer * 0.72f;
                        bool spokes = (Mathf.Abs(fx) <= 1.2f && Mathf.Abs(fy) <= outer * 0.95f)
                                      || (Mathf.Abs(fy) <= 1.2f && Mathf.Abs(fx) <= outer * 0.95f);
                        inside = ring || spokes;
                    }

                    pixels[y * size + x] = inside ? (Color32)fill : new Color32(0, 0, 0, 0);
                }
            }
        }

        /// <summary>标准五角星内外判定（角度法：10 段半径在内外径间线性插值）。</summary>
        static bool InsideStar(float x, float y, float outerRadius, float innerRadius)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > outerRadius)
                return false;

            float angle = Mathf.Atan2(y, x);
            const float step = Mathf.PI / 5f;              // 10 个顶点，每段 36°
            float phase = angle - Mathf.PI * 0.5f;         // 让一个尖朝上
            float t = Mathf.Repeat(phase, step) / step;    // 0..1 段内位置
            // 段内半径在「外→内→外」之间线性变化：用三角波。
            float wave = Mathf.Abs(2f * t - 1f);           // 1 → 0 → 1
            float limit = Mathf.Lerp(innerRadius, outerRadius, wave);
            return r <= limit;
        }

        // ------------------------------------------------------------------
        // 几何工具
        // ------------------------------------------------------------------

        static bool InsideRounded(int x, int y, int size, float radius)
        {
            float fx = x + 0.5f;
            float fy = y + 0.5f;
            float cx = Mathf.Clamp(fx, radius, size - radius);
            float cy = Mathf.Clamp(fy, radius, size - radius);
            float dx = fx - cx;
            float dy = fy - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        static int DistanceToEdge(int x, int y, int size)
        {
            int min = Mathf.Min(x, y);
            min = Mathf.Min(min, size - 1 - x);
            min = Mathf.Min(min, size - 1 - y);
            return min;
        }

        /// <summary>确定性哈希（不依赖 <c>Random</c>，保证每次生成结果一致）。</summary>
        static int Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }
    }
}
