using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 发布资产收口：程序化生成应用图标 + 定版版本号。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Release/应用图标与版本（幂等，可重复跑）
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.ReleaseGate.Apply
    ///
    /// 【图标】1024×1024 程序化绘制（零第三方素材，符合版权纪律）：
    ///   深海纵向渐变底 + 底部三条浪带 + 骨白骷髅与交叉骨 + 黄铜圆环描边。
    ///   与项目美术语言同源（深海蓝 + 骨白羊皮 + 黄铜强调，见 docs/美术风格指南.md §2.3）。
    ///   同一张 PNG 同时赋给 Standalone 全部图标尺寸槽（Unity 构建时自行缩放）。
    ///
    /// 【版本】bundleVersion 1.0.0（发布定版；UiStrings.MainVersion 的"版本 1.0"是给玩家看的文案）。
    /// </summary>
    public static class ReleaseGate
    {
        /// <summary>图标 PNG 资产路径。</summary>
        public const string IconAssetPath = "Assets/Art/Textures/AppIcon.png";

        /// <summary>发布版本号（与 UiStrings.MainVersion 同步改）。</summary>
        public const string BundleVersion = "1.0.0";

        const int IconSize = 1024;

        [MenuItem("PirateCrew/Release/应用图标与版本")]
        public static void Apply()
        {
            Texture2D icon = EnsureIconAsset();

            // Standalone 的全部尺寸槽都指向同一张 1024 图（构建期 Unity 按槽缩放）。
            int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++)
                icons[i] = icon;

            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, icons);
            PlayerSettings.companyName = "BoranFan";
            PlayerSettings.productName = "Pirate Crew 3D";
            PlayerSettings.bundleVersion = BundleVersion;

            Debug.Log("[ReleaseGate] 图标与版本已应用：" + IconAssetPath
                + "，bundleVersion=" + PlayerSettings.bundleVersion
                + "，Standalone 图标槽 " + icons.Length + " 个。");
        }

        /// <summary>每次都重绘并覆盖导入（幂等：改绘制代码后重跑菜单即可更新图标，不必先删 PNG）。</summary>
        static Texture2D EnsureIconAsset()
        {
            MenuUiBuilder.EnsureFolder("Assets/Art");
            MenuUiBuilder.EnsureFolder("Assets/Art/Textures");

            Texture2D drawn = DrawIcon();
            byte[] png = drawn.EncodeToPNG();
            Object.DestroyImmediate(drawn);

            string absolutePath = Path.Combine(Application.dataPath,
                IconAssetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(absolutePath, png);
            AssetDatabase.ImportAsset(IconAssetPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
        }

        // ------------------------------------------------------------------
        // 程序化绘制（1024 画布；y 向下为 PNG 行序，绘制时按数学坐标再翻转）
        // ------------------------------------------------------------------

        // 调色（与 UiTheme 同源：深海 / 骨白羊皮 / 黄铜）。
        static readonly Color SkyTop = Rgb(0x0A1D33);
        static readonly Color SkyBottom = Rgb(0x14344E);
        static readonly Color Wave = new Color(0x2A / 255f, 0x6E / 255f, 0x96 / 255f, 0.85f);
        static readonly Color Bone = Rgb(0xEDE0BE);
        static readonly Color BoneShadow = Rgb(0xC9B98F);
        static readonly Color Pupil = Rgb(0x081522);
        static readonly Color Brass = Rgb(0xC9A227);

        static Texture2D DrawIcon()
        {
            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[IconSize * IconSize];

            float size = IconSize;
            float cx = size * 0.5f;
            float cy = size * 0.5f;

            for (int y = 0; y < IconSize; y++)
            {
                // 【坐标口径】SetPixels32 的数组行 0 = 贴图底部（Unity 纹理原点在左下），
                // EncodeToPNG 导出时翻转成常规顶行在上的 PNG——所以数组行号 y 本身就是"数学 y 向上"，
                // 直接按 y 绘制即可；再手工翻转一次会把整张图上下颠倒（已实测踩过）。
                float my = y;
                float t = my / (size - 1f);
                Color background = Color.Lerp(SkyTop, SkyBottom, t);

                for (int x = 0; x < IconSize; x++)
                {
                    float mx = x;
                    Color color = background;

                    // 浪带：三条正弦细带（视觉压舱，让图标"浮在海面"）。
                    float waveBand = WaveBand(mx, my, 150f, 14f, 5.0f, 0.045f)
                        + WaveBand(mx, my, 96f, 13f, 7.3f, 0.05f)
                        + WaveBand(mx, my, 52f, 12f, 9.1f, 0.055f);
                    if (waveBand > 0.5f)
                        color = Wave;

                    // 交叉骨（两根长胶囊斜交在头骨后方，骨端从两侧下方露出）。
                    if (IsCapsule(mx, my, cx, cy - 60f, 640f, 26f, 26f)
                        || IsCapsule(mx, my, cx, cy - 60f, 640f, -26f, 26f)
                        || IsKnob(mx, my, cx, cy - 60f, 640f, 26f, 42f)
                        || IsKnob(mx, my, cx, cy - 60f, 640f, -26f, 42f))
                        color = BoneShadow;

                    // 骷髅头盖（椭圆）+ 下颚（圆角块）。
                    if (IsEllipse(mx, my, cx, cy + 16f, 196f, 188f))
                        color = Bone;
                    if (IsRoundedRect(mx, my, cx, cy - 180f, 218f, 96f, 30f))
                        color = Bone;

                    // 眼窝 × 2 + 鼻腔（三角）+ 颚缝 × 3。
                    if (IsEllipse(mx, my, cx - 72f, cy + 8f, 46f, 52f)
                        || IsEllipse(mx, my, cx + 72f, cy + 8f, 46f, 52f))
                        color = Pupil;
                    if (IsTriangle(mx, my,
                            cx, cy - 118f,
                            cx - 26f, cy - 62f,
                            cx + 26f, cy - 62f))
                        color = Pupil;
                    if (IsJawSeam(mx, my, cx, cy))
                        color = Pupil;

                    // 黄铜圆环描边（最外圈，最后画不被遮）。
                    float ring = Mathf.Abs(Distance(mx, my, cx, cy) - 470f);
                    if (ring <= 8f)
                        color = Brass;

                    pixels[y * IconSize + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>正弦浪带：带心 y=baseline、厚 thickness、振幅 amplitude、频率 frequency。</summary>
        static float WaveBand(float x, float y, float baseline, float thickness, float amplitude, float frequency)
        {
            float center = baseline + Mathf.Sin(x / (IconSize / (2f * Mathf.PI) * frequency)) * amplitude;
            return Mathf.Abs(y - center) < thickness * 0.5f ? 1f : 0f;
        }

        /// <summary>从 (cx,cy) 出发、长 length、与水平夹 angleDeg 的胶囊（圆头棒）。</summary>
        static bool IsCapsule(float x, float y, float cx, float cy, float length, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float dx = Mathf.Cos(rad) * length * 0.5f;
            float dy = Mathf.Sin(rad) * length * 0.5f;
            return SegmentDistance(x, y, cx - dx, cy - dy, cx + dx, cy + dy) <= radius;
        }

        /// <summary>胶囊端头的骨球（四端各一）。</summary>
        static bool IsKnob(float x, float y, float cx, float cy, float length, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float dx = Mathf.Cos(rad) * length * 0.5f;
            float dy = Mathf.Sin(rad) * length * 0.5f;
            return Distance(x, y, cx + dx, cy + dy) <= radius
                || Distance(x, y, cx - dx, cy - dy) <= radius;
        }

        /// <summary>颚缝：下颚上的三条竖缝。</summary>
        static bool IsJawSeam(float x, float y, float cx, float cy)
        {
            float jawTop = cy - 132f;
            float jawBottom = cy - 228f;
            if (y < jawBottom || y > jawTop)
                return false;

            return Mathf.Abs(x - (cx - 66f)) <= 4f
                || Mathf.Abs(x - cx) <= 4f
                || Mathf.Abs(x - (cx + 66f)) <= 4f;
        }

        static bool IsEllipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float nx = (x - cx) / rx;
            float ny = (y - cy) / ry;
            return nx * nx + ny * ny <= 1f;
        }

        static bool IsRoundedRect(float x, float y, float cx, float cy, float width, float height, float radius)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float dx = Mathf.Max(Mathf.Abs(x - cx) - (halfW - radius), 0f);
            float dy = Mathf.Max(Mathf.Abs(y - cy) - (halfH - radius), 0f);
            return dx * dx + dy * dy <= radius * radius;
        }

        static bool IsTriangle(float px, float py, float ax, float ay, float bx, float by, float cx2, float cy2)
        {
            float d1 = Sign(px, py, ax, ay, bx, by);
            float d2 = Sign(px, py, bx, by, cx2, cy2);
            float d3 = Sign(px, py, cx2, cy2, ax, ay);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        static float Sign(float px, float py, float ax, float ay, float bx, float by)
        {
            return (px - bx) * (ay - by) - (ax - bx) * (py - by);
        }

        static float Distance(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float lengthSq = abx * abx + aby * aby;
            if (lengthSq <= 1e-6f)
                return Distance(px, py, ax, ay);

            float t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / lengthSq);
            return Distance(px, py, ax + t * abx, ay + t * aby);
        }

        static Color Rgb(int hex)
        {
            return new Color(
                ((hex >> 16) & 0xFF) / 255f,
                ((hex >> 8) & 0xFF) / 255f,
                (hex & 0xFF) / 255f,
                1f);
        }
    }
}
