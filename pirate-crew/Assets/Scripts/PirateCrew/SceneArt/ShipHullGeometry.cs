using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 真实比例的放样船体（用户裁决 2026-09-14："两艘船就得真的建模两艘真实比例的大船，
    /// 意译不要直译"）。由 <c>SceneKitComposer</c> 在装配 <see cref="SceneKitPiece.ShipHullLoft"/>
    /// 时调用，替代旧的 HullBow/HullMid/HullStern 盒子三件套。
    ///
    /// 【放样法（造船的正经做法）】沿船长取 N 个横剖站（station），每站给出
    /// 半宽与剖面控制点（龙骨→舭部→最宽点→内倾舷墙→栏杆），相邻站的对应点连成四边形条带，
    /// 即得连续弯曲的船壳：
    ///   · 半宽剖面 w(x)：舯最宽，向艏按幂曲线收尖到艏柱（0.04×B），向艉收到艉板（0.62×B）；
    ///   · 舷弧线（sheer）：甲板边线在艏艉上扬（艏多艉少）——海盗船的标志性剪影；
    ///   · 剖面：龙骨窄→舭部转弯外扩→最宽点→向上内倾（tumblehome）到栏杆；
    ///   · 艉板（transom）：船艉的封板，略后倾；
    ///   · 龙骨：中线下方一条更深的地板条；
    ///   · 船艏斜桁（bowsprit）：从艏柱顶端伸出的斜杆。
    /// 材质只进 Wood/WoodDark（船族纯净性：纯木，无岩无草）。
    /// 可走无头测试（纯 MeshBuffers 运算）。
    /// </summary>
    public static class ShipHullGeometry
    {
        /// <summary>横剖站数（奇数，中站居中）。</summary>
        public const int Stations = 15;

        /// <summary>栏杆高度（含在舷弧线上）。</summary>
        public const float BulwarkHeight = 0.42f;

        /// <summary>
        /// 生成一艘放样船体。
        /// </summary>
        /// <param name="wood">亮木缓冲（甲板缘材/栏杆帽/艏斜桁）。</param>
        /// <param name="woodDark">暗木缓冲（船壳/艉板/龙骨）。</param>
        /// <param name="deckCenter">甲板面中心（世界坐标，甲板高度由 y 分量给出）。</param>
        /// <param name="yawDegrees">长轴朝向（0 = 长轴沿 +X）。</param>
        /// <param name="length">总长（含艏艉悬出）。</param>
        /// <param name="beam">最大船宽。</param>
        /// <param name="hullDepth">甲板到龙骨的吃深。</param>
        public static void AddLoftedHull(MeshBuffers wood, MeshBuffers woodDark,
            Vector3 deckCenter, float yawDegrees, float length, float beam, float hullDepth)
        {
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Cos(yawRad), 0f, Mathf.Sin(yawRad));
            Vector3 side = new Vector3(-axis.z, 0f, axis.x);

            // 局部坐标 → 世界：P(沿长轴距离, 横向距离, 高度偏移)
            Vector3 P(float along, float lateral, float up) =>
                deckCenter + axis * along + side * lateral + Vector3.up * up;

            float halfLen = length * 0.5f;
            float halfBeam = beam * 0.5f;

            // 每站数据：[0]=艉 … [N-1]=艏
            var alongs = new float[Stations];
            var halfWidths = new float[Stations];
            var deckYs = new float[Stations];
            for (int i = 0; i < Stations; i++)
            {
                float t = i / (float)(Stations - 1);          // 0=艉 1=艏
                alongs[i] = Mathf.Lerp(-halfLen, halfLen, t);
                halfWidths[i] = HalfWidthAt(t) * halfBeam;
                // 舷弧线：艏上扬 1.6×舷弧幅、艉上扬 0.7×（t=1 是艏）。
                deckYs[i] = SheerAmp(hullDepth)
                    * (Mathf.Pow(t, 2.6f) * 1.0f + Mathf.Pow(1f - t, 2.2f) * 0.55f);
            }

            // 剖面控制点的横向/垂向比例（0=龙骨 1=栏杆）：五点定义一个弯曲的舷侧。
            // (lateralFrac, upFrac)：upFrac 0=龙骨深，1=栏杆顶。
            (float lat, float up)[] Section = new (float, float)[]
            {
                (0.04f, 0.00f),   // 龙骨（几乎在中线上）
                (0.55f, 0.30f),   // 舭部转弯
                (0.97f, 0.62f),   // 最宽点
                (0.90f, 0.86f),   // 内倾（tumblehome）起点
                (0.80f, 1.00f),   // 栏杆顶
            };

            Vector3 StationPoint(int s, int cpt, float sideSign) => P(
                alongs[s],
                sideSign * halfWidths[s] * Section[cpt].lat,
                deckYs[s] + Mathf.Lerp(-hullDepth, BulwarkHeight, Section[cpt].up));

            // ---- 船壳条带：相邻站、相邻控制点连四边形（两侧一起，法线朝外） ----
            for (int s = 0; s < Stations - 1; s++)
            {
                for (int c = 0; c < Section.Length - 1; c++)
                {
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        Vector3 a0 = StationPoint(s, c, sd);
                        Vector3 a1 = StationPoint(s, c + 1, sd);
                        Vector3 b1 = StationPoint(s + 1, c + 1, sd);
                        Vector3 b0 = StationPoint(s + 1, c, sd);
                        Vector3 normal = Vector3.Cross(b0 - a0, a1 - a0).normalized * sd;
                        if (normal.sqrMagnitude < 1e-10f)
                            normal = side * sd;
                        // 船壳板条：中段用亮木（水上可见面），靠龙骨两条带用暗木（水影里）。
                        bool belowWaterline = c <= 1;
                        (belowWaterline ? woodDark : wood).AddQuad(a0, a1, b1, b0, normal);
                    }
                }
            }

            // ---- 艉板（transom）：船艉封板，略后倾 ----
            int stern = 0;
            Vector3 sternRake = -axis * (hullDepth * 0.18f);   // 后倾量
            Vector3 tR = StationPoint(stern, Section.Length - 1, 1) + sternRake;
            Vector3 bR = StationPoint(stern, 1, 1) + sternRake;
            Vector3 bL = StationPoint(stern, 1, -1) + sternRake;
            Vector3 tL = StationPoint(stern, Section.Length - 1, -1) + sternRake;
            woodDark.AddQuad(tL, tR, bR, bL, -axis);

            // ---- 船艏斜桁（bowsprit）：从艏柱顶向前下方伸出 ----
            Vector3 stemTop = P(alongs[Stations - 1], 0f, deckYs[Stations - 1] + BulwarkHeight);
            Vector3 spritDir = (axis * 1f + Vector3.down * 0.28f).normalized;
            wood.AddRod(stemTop, stemTop + spritDir * (length * 0.20f), 0.07f, 6);

            // ---- 龙骨：中线下方更深的一条，艏艉收窄 ----
            float keelLen = length * 0.78f;
            for (int k = 0; k < 6; k++)
            {
                float kx0 = Mathf.Lerp(-keelLen * 0.5f, keelLen * 0.5f, k / 6f);
                float kx1 = Mathf.Lerp(-keelLen * 0.5f, keelLen * 0.5f, (k + 1) / 6f);
                float taper = Mathf.Sin((k + 0.5f) / 6f * Mathf.PI) * 0.5f + 0.5f; // 中段宽艏艉窄
                float w = 0.16f + 0.20f * taper;
                Vector3 kb0 = P(kx0, -w, -hullDepth - 0.10f);
                Vector3 kb1 = P(kx1, -w, -hullDepth - 0.10f);
                Vector3 kt1 = P(kx1, w, -hullDepth - 0.10f);
                Vector3 kt0 = P(kx0, w, -hullDepth - 0.10f);
                woodDark.AddQuad(kb0, kb1, kt1, kt0, Vector3.down);
            }

            // ---- 栏杆帽（rail cap）：沿两舷栏杆顶一条亮木圆杆 ----
            for (int sd = -1; sd <= 1; sd += 2)
            {
                for (int s = 0; s < Stations - 1; s += 2)
                {
                    wood.AddRod(
                        StationPoint(s, Section.Length - 1, sd),
                        StationPoint(Mathf.Min(s + 2, Stations - 1), Section.Length - 1, sd),
                        0.07f, 6);
                }
            }
        }

        /// <summary>半宽剖面：0=艉板 0.62 → 舯 1.0 → 艏柱 0.04（幂曲线收尖）。</summary>
        static float HalfWidthAt(float t)
        {
            // 控制点插值（艉→艏）：艉板略窄、快速到全宽、艏前 30% 急剧收尖。
            if (t < 0.5f)
            {
                float u = t / 0.5f;
                return Mathf.Lerp(0.62f, 1f, Mathf.SmoothStep(0f, 1f, u));
            }
            float v = (t - 0.5f) / 0.5f;
            return Mathf.Lerp(1f, 0.04f, Mathf.Pow(v, 2.8f));
        }

        /// <summary>舷弧幅：船越大舷弧越明显。</summary>
        static float SheerAmp(float hullDepth) => Mathf.Max(0.35f, hullDepth * 0.45f);
    }
}
