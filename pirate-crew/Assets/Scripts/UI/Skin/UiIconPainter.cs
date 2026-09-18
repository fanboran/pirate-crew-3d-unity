using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 武器 / 职业图标的纯像素绘制器（多彩卡通"道具小静物"）。
    ///
    /// 【为什么纯像素而不是相机烘焙】本机 batchmode 没有可用的渲染路径
    /// （AGENTS 环境铁律：不带 -nographics 会卡 GfxDevice），相机拍程序化模型的方案
    /// 在无头 CI 里跑不了；逐像素绘制是纯 CPU，batchmode / harness 都能跑，
    /// 且与 <see cref="UiGlyphs"/> / <see cref="CartoonSpriteFactory"/> 同一套平涂语言。
    ///
    /// 【绘制语言（卡通拟物·极度抽离）】每个部件 = 平涂色块 + 自动 1px 深边 + 单点白色高光，
    /// 不做渐变 / 材质 / 阴影——三件套刚好够读出"这是颗炸弹 / 这是个木桶"。
    /// 主色取 <see cref="UiSkin.WeaponColor"/> / <see cref="UiSkin.CrewColor"/>，
    /// 与 UI 上的图标格底色天然同色系。
    ///
    /// 【产出方式】算法在本类（运行时程序集，无 UnityEditor 依赖），
    /// <c>Assets/Editor/UiSkinAssetBaker</c> 负责调它把 PNG 落到
    /// <c>Assets/Resources/UIIcons/</c>（运行时 <c>Resources.Load</c>）。
    /// </summary>
    public static class UiIconPainter
    {
        /// <summary>图标边长（px）。</summary>
        public const int Size = 96;

        /// <summary>画武器图标（17 种，键 = <see cref="WeaponId"/>）。</summary>
        public static Texture2D PaintWeapon(WeaponId id)
        {
            var r = new Raster(Size);
            Color main = UiSkin.WeaponColor(id);

            switch (id)
            {
                case WeaponId.Cannonball:
                    r.FillCircle(48, 46, 30, main);
                    r.ArcGloss(48, 46, 23, 25, 115, 0.28f);
                    r.Gloss(38, 57, 7, 0.5f);
                    break;

                case WeaponId.CherryBomb:
                    r.FillCircle(48, 42, 26, main);
                    r.Line(52, 66, 62, 80, 4, Raster.Brown);
                    r.FillEllipse(67, 81, 9, 5, Raster.LeafGreen);
                    r.Gloss(38, 51, 6, 0.55f);
                    break;

                case WeaponId.Dynamite:
                    r.FillRoundRect(48, 38, 26, 46, 6, main);
                    r.FillRoundRect(48, 38, 26, 11, 4, Raster.Cream);
                    r.Line(48, 62, 58, 77, 3, Raster.Brown);
                    r.FillCircle(59, 81, 8, Raster.Orange);
                    r.FillCircle(59, 84, 4, Raster.OrangeCore);
                    r.Gloss(41, 48, 4, 0.45f);
                    break;

                case WeaponId.Boulder:
                    r.FillCircle(40, 44, 20, main);
                    r.FillCircle(59, 46, 19, main);
                    r.FillCircle(49, 58, 16, main);
                    r.Line(50, 62, 44, 48, 2, Raster.DarkEdge);
                    r.Line(44, 48, 53, 35, 2, Raster.DarkEdge);
                    r.Gloss(38, 60, 5, 0.3f);
                    break;

                case WeaponId.Banana:
                    r.ThickArc(52, 52, 30, 205, 335, 11, main);
                    r.FillCircle(26, 37, 4, Raster.Brown);
                    r.FillCircle(78, 37, 4, Raster.Brown);
                    r.ThickArc(52, 55, 24, 220, 320, 4, new Color(1f, 1f, 1f, 0.30f));
                    break;

                case WeaponId.Mine:
                    for (int k = 0; k < 6; k++)
                    {
                        float a = (k * 60 + 30) * Mathf.Deg2Rad;
                        float x0 = 48 + Mathf.Cos(a) * 20f;
                        float y0 = 48 + Mathf.Sin(a) * 20f;
                        float x1 = 48 + Mathf.Cos(a) * 30f;
                        float y1 = 48 + Mathf.Sin(a) * 30f;
                        r.Line(x0, y0, x1, y1, 7, main);
                        r.FillCircle(x1, y1, 4, main);
                    }
                    r.FillCircle(48, 48, 22, main);
                    r.FillCircle(48, 48, 6, Raster.Lighten(main, 0.25f));
                    r.Gloss(40, 56, 5, 0.4f);
                    break;

                case WeaponId.ParachuteBomb:
                    r.Line(30, 55, 41, 28, 2, Raster.Cord);
                    r.Line(48, 52, 48, 28, 2, Raster.Cord);
                    r.Line(66, 55, 55, 28, 2, Raster.Cord);
                    r.FillCircle(48, 24, 10, Raster.Orange);
                    r.FillCircle(48, 24, 3, Raster.OrangeCore);
                    r.FillCircle(48, 58, 22, main);
                    r.Line(27, 58, 69, 58, 11, main);
                    r.Gloss(40, 65, 5, 0.4f);
                    break;

                case WeaponId.RumBottle:
                    r.FillRoundRect(48, 32, 22, 32, 8, main);
                    r.FillRoundRect(48, 52, 15, 10, 5, main);
                    r.FillRoundRect(48, 61, 10, 9, 3, main);
                    r.FillRoundRect(48, 67, 13, 6, 2, Raster.Brown);
                    r.FillRoundRect(48, 32, 16, 14, 3, Raster.Cream);
                    r.Line(40, 22, 40, 46, 3, new Color(1f, 1f, 1f, 0.32f));
                    break;

                case WeaponId.PiecesOfEight:
                    r.FillEllipse(40, 36, 17, 10, main);
                    r.FillEllipse(57, 36, 17, 10, main);
                    r.FillEllipse(48, 54, 17, 10, main);
                    r.FillCircle(48, 54, 2.5f, Raster.DarkEdge);
                    r.Gloss(41, 57, 3, 0.5f);
                    break;

                case WeaponId.GunpowderBarrel:
                    r.FillRoundRect(48, 44, 32, 44, 10, main);
                    r.FillRoundRect(48, 60, 34, 5, 2, Raster.DarkGray);
                    r.FillRoundRect(48, 28, 34, 5, 2, Raster.DarkGray);
                    r.FillEllipse(48, 66, 16, 7, Raster.Lighten(main, 0.22f));
                    r.FillCircle(48, 46, 5.5f, Raster.Cream);
                    r.FillCircle(45.8f, 47, 1.4f, Raster.DarkEdge);
                    r.FillCircle(50.2f, 47, 1.4f, Raster.DarkEdge);
                    r.Line(41, 26, 41, 60, 3, new Color(1f, 1f, 1f, 0.25f));
                    break;

                case WeaponId.WoodenCrate:
                    r.FillRoundRect(48, 48, 44, 44, 6, main);
                    r.Line(28, 28, 68, 28, 5, Raster.Darken(main, 0.28f));
                    r.Line(28, 68, 68, 68, 5, Raster.Darken(main, 0.28f));
                    r.Line(28, 30, 28, 66, 5, Raster.Darken(main, 0.28f));
                    r.Line(68, 30, 68, 66, 5, Raster.Darken(main, 0.28f));
                    r.Line(31, 31, 65, 65, 5, Raster.Darken(main, 0.28f));
                    r.Line(65, 31, 31, 65, 5, Raster.Darken(main, 0.28f));
                    r.Gloss(38, 60, 5, 0.3f);
                    break;

                case WeaponId.Anchor:
                    r.StrokeCircle(48, 74, 8, 5, main);
                    r.FillRoundRect(48, 46, 8, 50, 3, main);
                    r.FillRoundRect(48, 62, 26, 6, 3, main);
                    r.ThickArc(48, 26, 22, 25, 155, 6, main);
                    r.Triangle(26, 40, 22, 26, 34, 30, main);
                    r.Triangle(70, 40, 74, 26, 62, 30, main);
                    r.Gloss(45, 60, 4, 0.4f);
                    break;

                case WeaponId.Seagull:
                    r.FillEllipse(50, 40, 23, 13, main);
                    r.ThickArc(36, 50, 15, 110, 175, 7, Raster.WingGray);
                    r.ThickArc(64, 50, 15, 5, 70, 7, Raster.WingGray);
                    r.FillCircle(31, 50, 8, Raster.Cream);
                    r.Triangle(24, 51, 13, 49, 24, 47, Raster.Orange);
                    r.FillCircle(29, 53, 1.6f, Raster.DarkEdge);
                    r.Triangle(70, 40, 82, 44, 70, 47, Raster.WingGray);
                    break;

                case WeaponId.TidalWave:
                    r.ThickArc(44, 30, 30, 40, 180, 14, main);
                    r.ThickArc(44, 30, 21, 40, 180, 6, Raster.Lighten(main, 0.3f));
                    r.FillCircle(15, 30, 5, Raster.Cream);
                    r.FillCircle(26, 55, 6, Raster.Cream);
                    r.FillCircle(49, 64, 5, Raster.Cream);
                    r.FillCircle(63, 64, 3.5f, Raster.Cream);
                    break;

                case WeaponId.VoodooDoll:
                    r.FillRoundRect(48, 34, 24, 26, 9, main);
                    r.FillRoundRect(30, 40, 13, 7, 3, main);
                    r.FillRoundRect(66, 40, 13, 7, 3, main);
                    r.FillRoundRect(40, 16, 8, 12, 3, main);
                    r.FillRoundRect(56, 16, 8, 12, 3, main);
                    r.FillCircle(48, 62, 13, Raster.Cloth);
                    r.FillCircle(43.5f, 64, 2f, Raster.DarkEdge);
                    r.FillCircle(52.5f, 64, 2f, Raster.DarkEdge);
                    r.Line(44, 57, 52, 57, 1.5f, Raster.DarkEdge);
                    r.Line(48, 57, 46, 54, 1.2f, Raster.DarkEdge);
                    r.Line(48, 57, 50, 54, 1.2f, Raster.DarkEdge);
                    r.FillCircle(48, 40, 4, Raster.HeartRed);
                    r.Gloss(43, 68, 3, 0.35f);
                    break;

                case WeaponId.Cannon:
                    r.Triangle(40, 38, 56, 38, 48, 20, Raster.WoodDark);
                    r.FillRoundRect(48, 22, 32, 7, 2, Raster.WoodDark);
                    r.FillRoundRect(48, 52, 44, 18, 8, main);
                    r.FillEllipse(27, 52, 4, 9, Raster.DarkEdge);
                    r.FillRoundRect(29, 52, 5, 20, 2, Raster.DarkGray);
                    r.FillRoundRect(66, 52, 5, 18, 2, Raster.DarkGray);
                    r.Line(45, 54, 45, 46, 3, new Color(1f, 1f, 1f, 0.3f));
                    break;

                case WeaponId.SweepingFlame:
                    r.FillCircle(48, 34, 20, main);
                    r.Triangle(33, 42, 63, 42, 46, 80, main);
                    r.FillCircle(48, 32, 13, Raster.FlameMid);
                    r.Triangle(39, 38, 57, 38, 47, 66, Raster.FlameMid);
                    r.FillCircle(48, 31, 7, Raster.FlameCore);
                    r.Triangle(44, 34, 52, 34, 47, 52, Raster.FlameCore);
                    r.Gloss(44, 26, 3, 0.5f);
                    break;
            }

            return r.ToTexture("Icon_Weapon_" + id);
        }

        /// <summary>画职业头像（7 档，键 = <see cref="UiSkin.CrewKey"/> 短名）。</summary>
        public static Texture2D PaintCrew(string crewKey)
        {
            var r = new Raster(Size);
            Color main = UiSkin.CrewColor(crewKey);

            // 共用底：两件式船员正视（与 3D 低模同构：圆台身 + 圆球头）。
            bool skeleton = crewKey == "skeleton";
            r.FillRoundRect(48, 32, 27, 28, 9, main);
            r.FillCircle(48, 58, 14, skeleton ? Raster.Bone : Raster.Skin);
            // 头巾 / 帽：头顶半圆盖（骷髅不盖，露骨）。
            if (!skeleton && crewKey != "captain")
                r.ThickArc(48, 58, 14, 0, 180, 9, main);
            // 眼睛（骷髅是黑眼窝）。
            r.FillCircle(43, 58, skeleton ? 3f : 2f, Raster.DarkEdge);
            r.FillCircle(53, 58, skeleton ? 3f : 2f, Raster.DarkEdge);
            r.Gloss(42, 66, 3, 0.3f);

            switch (crewKey)
            {
                case "sailor":
                    r.FillRoundRect(62, 62, 7, 9, 2, main);        // 头巾侧结
                    break;
                case "gunner":
                    r.ThickArc(48, 53, 8, 190, 350, 4, Raster.DarkEdge);  // 黑胡子
                    r.FillCircle(68, 44, 8, Raster.CannonballGray);       // 肩上炮弹
                    r.Gloss(65, 47, 2.5f, 0.4f);
                    break;
                case "sniper":
                    r.StrokeCircle(53, 58, 4.5f, 2.5f, Raster.DarkEdge);  // 单片镜
                    r.Line(57, 60, 64, 66, 2, Raster.DarkEdge);
                    break;
                case "hooker":
                    r.StrokeCircle(68, 34, 7, 3, Raster.HookSteel);
                    r.Triangle(70, 40, 74, 44, 66, 44, Raster.HookSteel);
                    break;
                case "arsonist":
                    r.FillCircle(48, 76, 5, Raster.Orange);
                    r.Triangle(44, 76, 52, 76, 48, 88, Raster.Orange);
                    r.FillCircle(48, 75, 2.5f, Raster.FlameCore);
                    break;
                case "skeleton":
                    r.Line(39, 38, 57, 38, 2, Raster.DarkEdge);    // 肋骨两道
                    r.Line(39, 30, 57, 30, 2, Raster.DarkEdge);
                    break;
                case "captain":
                    r.FillRoundRect(48, 70, 32, 5, 2, Raster.DarkEdge);   // 帽檐
                    r.FillRoundRect(48, 78, 22, 12, 4, Raster.DarkEdge);  // 帽体
                    r.FillCircle(48, 76, 3, UiSkin.Gold);                 // 金徽
                    break;
            }

            return r.ToTexture("Icon_Crew_" + crewKey);
        }

        // ------------------------------------------------------------------
        // 光栅器（平涂色块 + 自动 1px 深边 + alpha 混合；坐标 y 向上）
        // ------------------------------------------------------------------

        internal sealed class Raster
        {
            internal static readonly Color Brown = new Color(0.55f, 0.36f, 0.18f, 1f);
            internal static readonly Color LeafGreen = new Color(0.35f, 0.62f, 0.28f, 1f);
            internal static readonly Color Cream = new Color(0.94f, 0.90f, 0.80f, 1f);
            internal static readonly Color Orange = new Color(0.95f, 0.55f, 0.20f, 1f);
            internal static readonly Color OrangeCore = new Color(1f, 0.85f, 0.35f, 1f);
            internal static readonly Color DarkGray = new Color(0.22f, 0.24f, 0.27f, 1f);
            internal static readonly Color CannonballGray = new Color(0.36f, 0.42f, 0.50f, 1f);
            internal static readonly Color WingGray = new Color(0.58f, 0.64f, 0.72f, 1f);
            internal static readonly Color Cloth = new Color(0.91f, 0.85f, 0.69f, 1f);
            internal static readonly Color Skin = new Color(0.91f, 0.72f, 0.54f, 1f);
            internal static readonly Color Bone = new Color(0.90f, 0.88f, 0.80f, 1f);
            internal static readonly Color HookSteel = new Color(0.55f, 0.62f, 0.70f, 1f);
            internal static readonly Color HeartRed = new Color(0.88f, 0.25f, 0.30f, 1f);
            internal static readonly Color WoodDark = new Color(0.42f, 0.28f, 0.14f, 1f);
            internal static readonly Color FlameMid = new Color(0.98f, 0.72f, 0.22f, 1f);
            internal static readonly Color FlameCore = new Color(1f, 0.94f, 0.60f, 1f);
            internal static readonly Color DarkEdge = new Color(0.16f, 0.16f, 0.16f, 1f);
            internal static readonly Color Cord = new Color(0.85f, 0.80f, 0.68f, 1f);

            readonly int size;
            readonly Color32[] pixels;

            internal Raster(int size)
            {
                this.size = size;
                pixels = new Color32[size * size];
            }

            internal static Color Lighten(Color c, float amount)
            {
                return new Color(
                    Mathf.Min(1f, c.r + amount), Mathf.Min(1f, c.g + amount), Mathf.Min(1f, c.b + amount), c.a);
            }

            internal static Color Darken(Color c, float amount)
            {
                return new Color(
                    Mathf.Max(0f, c.r - amount), Mathf.Max(0f, c.g - amount), Mathf.Max(0f, c.b - amount), c.a);
            }

            /// <summary>部件色 → 1px 深边色（亮度压到 ~45%，饱和色上读作同色系深线）。</summary>
            static Color EdgeOf(Color c)
            {
                return new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 1f);
            }

            internal void FillCircle(float cx, float cy, float radius, Color fill)
            {
                ForEachPixel(cx - radius - 1, cx + radius + 1, cy - radius - 1, cy + radius + 1, (x, y) =>
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    BlendShape(x, y, radius - d, fill);
                });
            }

            internal void FillEllipse(float cx, float cy, float rx, float ry, Color fill)
            {
                ForEachPixel(cx - rx - 1, cx + rx + 1, cy - ry - 1, cy + ry + 1, (x, y) =>
                {
                    float d = Mathf.Sqrt(Sq((x - cx) / rx) + Sq((y - cy) / ry)) * Mathf.Min(rx, ry);
                    BlendShape(x, y, Mathf.Min(rx, ry) - d, fill);
                });
            }

            internal void FillRoundRect(float cx, float cy, float width, float height, float radius, Color fill)
            {
                float hw = width * 0.5f, hh = height * 0.5f;
                ForEachPixel(cx - hw - 1, cx + hw + 1, cy - hh - 1, cy + hh + 1, (x, y) =>
                {
                    float sd = CartoonSpriteFactory.SdRoundRect(x - cx, y - cy, Mathf.Max(hw - 0.5f, radius),
                        Mathf.Min(radius, Mathf.Min(hw, hh)));
                    BlendShape(x, y, -sd, fill);
                });
            }

            internal void StrokeCircle(float cx, float cy, float radius, float thickness, Color fill)
            {
                ForEachPixel(cx - radius - 1, cx + radius + 1, cy - radius - 1, cy + radius + 1, (x, y) =>
                {
                    float d = Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius);
                    BlendShape(x, y, thickness * 0.5f - d, fill);
                });
            }

            /// <summary>粗弧（角度制、0=右、逆时针为正）。</summary>
            internal void ThickArc(float cx, float cy, float radius, float a0, float a1, float thickness, Color fill)
            {
                ForEachPixel(cx - radius - 1, cx + radius + 1, cy - radius - 1, cy + radius + 1, (x, y) =>
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius);
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float delta = Mathf.DeltaAngle(angle, (a0 + a1) * 0.5f);
                    float halfSpan = Mathf.Abs(Mathf.DeltaAngle(a0, a1)) * 0.5f + 1.5f;
                    if (Mathf.Abs(delta) > halfSpan)
                        return;
                    BlendShape(x, y, thickness * 0.5f - d, fill);
                });
            }

            internal void Line(float x1, float y1, float x2, float y2, float thickness, Color fill)
            {
                float minX = Mathf.Min(x1, x2) - thickness, maxX = Mathf.Max(x1, x2) + thickness;
                float minY = Mathf.Min(y1, y2) - thickness, maxY = Mathf.Max(y1, y2) + thickness;
                ForEachPixel(minX, maxX, minY, maxY, (x, y) =>
                {
                    float d = UiGlyphs.DistToSegment(x, y, x1, y1, x2, y2);
                    BlendShape(x, y, thickness * 0.5f - d, fill);
                });
            }

            internal void Triangle(float x1, float y1, float x2, float y2, float x3, float y3, Color fill)
            {
                float minX = Mathf.Min(x1, Mathf.Min(x2, x3)) - 1, maxX = Mathf.Max(x1, Mathf.Max(x2, x3)) + 1;
                float minY = Mathf.Min(y1, Mathf.Min(y2, y3)) - 1, maxY = Mathf.Max(y1, Mathf.Max(y2, y3)) + 1;
                ForEachPixel(minX, maxX, minY, maxY, (x, y) =>
                {
                    float d = SdTriangle(x, y, x1, y1, x2, y2, x3, y3);
                    BlendShape(x, y, -d, fill);
                });
            }

            /// <summary>白色高光点（alpha 混合，不盖边）。</summary>
            internal void Gloss(float cx, float cy, float radius, float alpha)
            {
                FillCircleSoft(cx, cy, radius, new Color(1f, 1f, 1f, alpha));
            }

            void FillCircleSoft(float cx, float cy, float radius, Color tint)
            {
                ForEachPixel(cx - radius - 1, cx + radius + 1, cy - radius - 1, cy + radius + 1, (x, y) =>
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float coverage = Mathf.Clamp01(radius - d);
                    if (coverage > 0f)
                        Blend(x, y, tint, coverage * tint.a);
                });
            }

            /// <summary>形状混合入口：inside = 形状内距离（&gt;0 在内），0..1 为 1px 深边带。</summary>
            void BlendShape(int x, int y, float inside, Color fill)
            {
                if (inside <= 0f)
                    return;
                Color c = inside < 1f ? EdgeOf(fill) : fill;      // 1px 深边
                Blend(x, y, c, Mathf.Clamp01(inside));
            }

            /// <summary>白色描弧（ThickArc 的发光版——给炮弹加受光弧）。</summary>
            internal void ArcGloss(float cx, float cy, float radius, float a0, float a1, float alpha)
            {
                ThickArc(cx, cy, radius, a0, a1, 3f, new Color(1f, 1f, 1f, alpha));
            }

            void Blend(int x, int y, Color source, float coverage)
            {
                if (x < 0 || y < 0 || x >= size || y >= size || coverage <= 0f)
                    return;

                int row = size - 1 - y;                            // 数组行 0 = 贴图底部
                int index = row * size + x;
                Color dest = pixels[index];
                float srcA = source.a * coverage;
                float outA = srcA + dest.a * (1f - srcA);
                if (outA <= 0f)
                    return;
                pixels[index] = new Color(
                    (source.r * srcA + dest.r * dest.a * (1f - srcA)) / outA,
                    (source.g * srcA + dest.g * dest.a * (1f - srcA)) / outA,
                    (source.b * srcA + dest.b * dest.a * (1f - srcA)) / outA,
                    outA);
            }

            void ForEachPixel(float minX, float maxX, float minY, float maxY,
                System.Action<int, int> action)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX));
                int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(maxX));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
                int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(maxY));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        action(x, y);
            }

            static float Sq(float v) => v * v;

            /// <summary>三角形 SDF（负 = 内）。</summary>
            static float SdTriangle(float px, float py,
                float ax, float ay, float bx, float by, float cx, float cy)
            {
                float Sign(float x0, float y0, float x1, float y1, float x2, float y2)
                    => (x0 - x2) * (y1 - y2) - (x1 - x2) * (y0 - y2);

                float d = Mathf.Min(
                    Mathf.Min(
                        UiGlyphs.DistToSegment(px, py, ax, ay, bx, by),
                        UiGlyphs.DistToSegment(px, py, bx, by, cx, cy)),
                    UiGlyphs.DistToSegment(px, py, cx, cy, ax, ay));

                float s = Sign(ax, ay, bx, by, px, py) >= 0f &&
                          Sign(bx, by, cx, cy, px, py) >= 0f &&
                          Sign(cx, cy, ax, ay, px, py) >= 0f
                    ? -1f : 1f;
                return s * d;
            }

            internal Texture2D ToTexture(string name)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.name = name;
                texture.SetPixels32(pixels);
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.Apply(false, false);
                return texture;
            }
        }
    }
}
