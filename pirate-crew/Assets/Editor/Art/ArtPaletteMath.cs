using System.Globalization;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 调色板取色与 **OkLab 最近邻**查找的纯 C# 实现（无 Unity API 依赖，可无头断言）。
    ///
    /// 【为什么 C# 侧还要一份 OkLab】本仓库现在有三份等价实现，且**故意不共享代码**：
    ///   · `tools/palette/palette_tool.py`（离线量化主力，numpy 向量版）
    ///   · `tools/blender/pixel/pixel_export.py`（Blender 侧，纯 Python 版 —— Blender 里没有 PIL，
    ///     共享会把 blender 拖进无效依赖）
    ///   · 本文件（Unity 编辑器侧，材质工厂/UI 派生用）
    /// 三者都以 **Ottosson 官方 XYZ→OkLab 测试表**做回归（容差 1e-3）。两个独立实现同时通过同一张
    /// 官方表，比共享一份实现更能证明转换没写错；任一份漂移都会被它自己的自检当场抓住
    /// （本文件的自检入口：菜单 `PirateCrew/Art/调色板/自检 OkLab 转换`）。
    ///
    /// 【色空间】矩阵是线性 sRGB ↔ OkLab，与本工程 `m_ActiveColorSpace = 0`（Gamma）下的
    /// 贴图字节语义一致：hex 的 8bit 值就是屏幕值，先 sRGB→线性再进 OkLab。
    /// </summary>
    public static class ArtPaletteMath
    {
        // --- OkLab（Björn Ottosson 2021 修订版）---
        static readonly float[] XyzToLms =
        {
            0.8190224379967030f, 0.3619062600528904f, -0.1288737815209879f,
            0.0329836539323885f, 0.9292868615863434f, 0.0361446663506424f,
            0.0481771893596242f, 0.2642395317527308f, 0.6335478284694309f,
        };

        static readonly float[] LmsToLab =
        {
            0.2104542683093140f, 0.7936177747023054f, -0.0040720430116193f,
            1.9779985324311684f, -2.4285922420485799f, 0.4505937096174110f,
            0.0259040424655478f, 0.7827717124575296f, -0.8086757549230774f,
        };

        static readonly float[] SrgbToXyz =
        {
            0.4123907992659595f, 0.3575843393838780f, 0.1804807884018343f,
            0.2126390058715104f, 0.7151686787677559f, 0.0721923153607337f,
            0.0193308187155918f, 0.1191947797946259f, 0.9505321522496608f,
        };

        /// <summary>Ottosson 原文公布的 XYZ→OkLab 参考值（自检用）。</summary>
        static readonly float[][] OklabReferenceXyz =
        {
            new[] { 0.950f, 1.000f, 1.089f },
            new[] { 1.000f, 0.000f, 0.000f },
            new[] { 0.000f, 1.000f, 0.000f },
            new[] { 0.000f, 0.000f, 1.000f },
        };

        static readonly float[][] OklabReferenceLab =
        {
            new[] { 1.000f, 0.000f, 0.000f },
            new[] { 0.450f, 1.236f, -0.019f },
            new[] { 0.922f, -0.671f, 0.263f },
            new[] { 0.153f, -1.415f, -0.449f },
        };

        /// <summary>sRGB 8bit 分量 → 线性光。</summary>
        public static float SrgbToLinear(float c)
        {
            return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>XYZ(D65) → OkLab。**不钳非负**：XYZ 可落在 Oklab 域外，负 LMS 的实立方根是合法解。</summary>
        public static Vector3 XyzToOklab(Vector3 xyz)
        {
            Vector3 lms = Mul(XyzToLms, xyz);
            lms = new Vector3(Cbrt(lms.x), Cbrt(lms.y), Cbrt(lms.z));
            return Mul(LmsToLab, lms);
        }

        /// <summary>线性 sRGB → OkLab。</summary>
        public static Vector3 LinearToOklab(Vector3 linear)
        {
            return XyzToOklab(Mul(SrgbToXyz, linear));
        }

        /// <summary>Color（Gamma 直存，0~1）→ OkLab。</summary>
        public static Vector3 ToOklab(Color color)
        {
            var linear = new Vector3(SrgbToLinear(color.r), SrgbToLinear(color.g), SrgbToLinear(color.b));
            return LinearToOklab(linear);
        }

        /// <summary>hex（RRGGBB / #RRGGBB）→ OkLab；解析失败返回零向量。</summary>
        public static Vector3 HexToOklab(string hex)
        {
            if (!PaletteAsset.ParseHex(hex, out Color color))
                return Vector3.zero;
            return ToOklab(color);
        }

        /// <summary>加权平方欧氏距离 `d = ΔL² + w·(Δa² + Δb²)`（调研-量化 §1 第 2 条）。</summary>
        public static float Distance(Vector3 a, Vector3 b, float chromaWeight = 1f)
        {
            float dl = a.x - b.x;
            float da = a.y - b.y;
            float db = a.z - b.z;
            return dl * dl + chromaWeight * (da * da + db * db);
        }

        /// <summary>
        /// 在一组候选里找与目标 OkLab 最近的下标（暴力比较，无数据结构差异 —— 与离线工具同口径）。
        /// candidates 为空返回 -1。
        /// </summary>
        public static int NearestIndex(Vector3 targetLab, Vector3[] candidates, float chromaWeight = 1f)
        {
            int best = -1;
            float bestDistance = 0f;
            for (int i = 0; i < candidates.Length; i++)
            {
                float d = Distance(targetLab, candidates[i], chromaWeight);
                if (best < 0 || d < bestDistance)
                {
                    best = i;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>
        /// 在 PaletteAsset 里找与给定颜色最近的槽位（返回 null = 板为空）。
        /// 「把一张现有材质的基色归到板上哪个色」是材质工厂的核心一步。
        /// </summary>
        public static PaletteSlot NearestSlot(PaletteAsset palette, Color color, float chromaWeight = 1f)
        {
            if (palette == null || palette.slots == null || palette.slots.Count == 0)
                return null;

            var candidates = new Vector3[palette.slots.Count];
            for (int i = 0; i < palette.slots.Count; i++)
                candidates[i] = HexToOklab(palette.slots[i].hex);

            int index = NearestIndex(ToOklab(color), candidates, chromaWeight);
            return index < 0 ? null : palette.slots[index];
        }

        /// <summary>OkLab 官方测试表回归：返回最大偏差（&lt;= 1e-3 视为通过）。</summary>
        public static float SelfCheckOklab()
        {
            float worst = 0f;
            for (int i = 0; i < OklabReferenceXyz.Length; i++)
            {
                Vector3 got = XyzToOklab(new Vector3(OklabReferenceXyz[i][0], OklabReferenceXyz[i][1],
                    OklabReferenceXyz[i][2]));
                Vector3 want = new Vector3(OklabReferenceLab[i][0], OklabReferenceLab[i][1],
                    OklabReferenceLab[i][2]);
                worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(got.x - want.x),
                    Mathf.Max(Mathf.Abs(got.y - want.y), Mathf.Abs(got.z - want.z))));
            }
            return worst;
        }

        /// <summary>Color → `#RRGGBB`（大写，报告用）。</summary>
        public static string ToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture)
                       + g.ToString("X2", CultureInfo.InvariantCulture)
                       + b.ToString("X2", CultureInfo.InvariantCulture);
        }

        static Vector3 Mul(float[] m, Vector3 v)
        {
            return new Vector3(
                m[0] * v.x + m[1] * v.y + m[2] * v.z,
                m[3] * v.x + m[4] * v.y + m[5] * v.z,
                m[6] * v.x + m[7] * v.y + m[8] * v.z);
        }

        /// <summary>实立方根（负数有定义；<c>Mathf.Pow</c> 对负数返回 NaN，不能用）。</summary>
        static float Cbrt(float x)
        {
            return Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 1f / 3f);
        }
    }
}
