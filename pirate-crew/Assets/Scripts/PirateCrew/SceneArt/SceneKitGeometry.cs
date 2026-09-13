using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 构件几何生成（纯 C#，无头可测）：<see cref="SceneKitCatalog"/> 只描述"用什么构件、摆在哪"，
    /// 本类只负责"每类构件长什么样"。构件几何**认种类不认船只/岛**——同一
    /// <see cref="SceneKitPiece.HullMid"/> 既用于大帆船也用于小艇，只是长度/材质不同（kit 复用）。
    ///
    /// 【硬约束】所有构件都**不得高于所在平台顶面**（场景文档 §9.1：单位脚底必须贴合
    /// <c>SurfaceWorldY</c>）。故岛顶岩台 / 船体段都以"顶面即地表、往下长"的方式生成，绝不抬高地表。
    ///
    /// 【无 Collider】与场景其它装饰一致（§9.4：避免投掷物被弹开而破坏"预览 = 实弹"）。
    /// </summary>
    public static class SceneKitGeometry
    {
        static Vector3 Dir(float yawDegrees)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        }

        /// <summary>
        /// 船体段（艏/舯/艉）：一块沿长轴的侧板木箱，顶面 = 甲板面 <paramref name="deckCenter"/>.y，
        /// 向下长 <paramref name="height"/>（覆盖平台侧壁并探入水线）。
        /// <paramref name="taper"/> 控制船宽方向的收窄（艏尖、艉方）。
        /// </summary>
        public static void AddHullSegment(MeshBuffers b, Vector3 deckCenter, float yawDegrees,
            float length, float height, float beam, float taper, int seed)
        {
            if (b == null)
                return;

            Vector3 c = new Vector3(deckCenter.x, deckCenter.y - height * 0.5f, deckCenter.z);
            Vector3 size = new Vector3(Mathf.Max(0.2f, length), Mathf.Max(0.1f, height),
                Mathf.Max(0.2f, beam * taper));
            b.AddBox(c, size, yawDegrees);

            // 压条：顶缘一条窄木带，让侧板有分层（船体读感）。
            b.AddBox(new Vector3(c.x, deckCenter.y - 0.06f, c.z),
                new Vector3(Mathf.Max(0.22f, length + 0.02f), 0.1f, Mathf.Max(0.22f, beam * taper + 0.05f)),
                yawDegrees);

            // 艏/艉端各加一条竖向肋木。
            Vector3 dir = Dir(yawDegrees);
            Vector3 end = c - dir * (length * 0.5f);
            b.AddBox(new Vector3(end.x, c.y, end.z),
                new Vector3(0.14f, height * 0.92f, beam * taper * 0.85f), yawDegrees);
        }

        /// <summary>甲板木板（顶面与地表齐平，微凸 0.02 避免 z-fighting）。</summary>
        public static void AddDeckPlank(MeshBuffers b, Vector3 surfaceCenter, float yawDegrees,
            float length, float thickness, float width, int seed)
        {
            if (b == null)
                return;

            b.AddBox(new Vector3(surfaceCenter.x, surfaceCenter.y + 0.02f, surfaceCenter.z),
                new Vector3(Mathf.Max(0.2f, length), Mathf.Max(0.03f, thickness), Mathf.Max(0.08f, width)),
                yawDegrees);
        }

        /// <summary>舷墙 / 栏杆：沿船侧的一段薄墙。</summary>
        public static void AddBulwark(MeshBuffers b, Vector3 baseCenter, float yawDegrees,
            float length, float height, int seed)
        {
            if (b == null)
                return;

            b.AddBox(new Vector3(baseCenter.x, baseCenter.y + height * 0.5f, baseCenter.z),
                new Vector3(Mathf.Max(0.2f, length), Mathf.Max(0.1f, height), 0.1f), yawDegrees);
        }

        /// <summary>
        /// 桅杆：锥形杆 + 2 根横桁 + 桅顶铁环。木写 <paramref name="wood"/>，铁件写 <paramref name="metal"/>。
        /// </summary>
        public static void AddMast(MeshBuffers wood, MeshBuffers metal, Vector3 basePos, float yawDegrees,
            float radius, float height, int seed)
        {
            if (wood == null)
                return;

            wood.AddFrustum(basePos, radius * 1.25f, radius * 0.7f, height, 7, yawDegrees);

            Vector3 dir = Dir(yawDegrees);
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 top = basePos + Vector3.up * height;

            // 横桁（沿船宽方向）。
            wood.AddRod(top - Vector3.up * (height * 0.22f), top + side * 1.5f - Vector3.up * (height * 0.22f), 0.045f, 5);
            wood.AddRod(top - Vector3.up * (height * 0.4f), top - side * 1.2f - Vector3.up * (height * 0.4f), 0.04f, 5);

            if (metal != null)
            {
                metal.AddRod(top - Vector3.up * 0.06f, top + Vector3.up * 0.06f, 0.05f, 5);
            }
        }

        /// <summary>索具：从桅顶拉到船体某点的一根缆绳。</summary>
        public static void AddRigging(MeshBuffers b, Vector3 mastBase, float yawDegrees,
            float reach, float mastHeight, int seed)
        {
            if (b == null)
                return;

            Vector3 top = mastBase + Vector3.up * (mastHeight * 0.92f);
            // reach<0 表示拉向船艉；否则拉向该 yaw 方向。
            Vector3 anchor = reach < 0f
                ? mastBase - Dir(yawDegrees) * (-reach) + Vector3.up * 0.1f
                : mastBase + Dir(yawDegrees) * reach + Vector3.up * 0.1f;
            b.AddRod(top, anchor, 0.016f, 4);
        }

        /// <summary>帆布：从桅上横桁下垂的双面四边形（轻微鼓风）。</summary>
        public static void AddSail(MeshBuffers b, Vector3 center, float yawDegrees,
            float width, float height, int seed)
        {
            if (b == null || width <= 0f || height <= 0f)
                return;

            Vector3 dir = Dir(yawDegrees);          // 帆面宽度方向
            Vector3 normal = new Vector3(-dir.z, 0f, dir.x);
            float hw = width * 0.5f, hh = height * 0.5f;
            float bulge = Mathf.Min(0.18f, width * 0.12f);

            Vector3 bl = center - dir * hw - Vector3.up * hh;
            Vector3 br = center + dir * hw - Vector3.up * hh;
            Vector3 tr = center + dir * hw + Vector3.up * hh;
            Vector3 tl = center - dir * hw + Vector3.up * hh;
            Vector3 mid = center + normal * bulge;

            b.AddTrianglesDoubleSided(bl, br, mid, mid, normal);
            b.AddTrianglesDoubleSided(br, tr, mid, mid, normal);
            b.AddTrianglesDoubleSided(tr, tl, mid, mid, normal);
            b.AddTrianglesDoubleSided(tl, bl, mid, mid, normal);
        }

        /// <summary>
        /// 岛顶岩台：沿平台边缘铺一圈低矮岩唇（顶面**不高于**地表，<paramref name="surfaceY"/> 即地表）。
        /// <paramref name="radiusX"/>/<paramref name="radiusZ"/> = 簇包络半径。
        /// </summary>
        public static void AddIslandTop(MeshBuffers b, Vector3 surfaceCenter, float radiusX, float radiusZ, int seed)
        {
            if (b == null)
                return;

            int segments = 10;
            for (int i = 0; i < segments; i++)
            {
                float ang = i / (float)segments * Mathf.PI * 2f + SceneArtHash.SignedHash(seed, i, 61) * 0.18f;
                float px = surfaceCenter.x + Mathf.Cos(ang) * radiusX;
                float pz = surfaceCenter.z + Mathf.Sin(ang) * radiusZ;
                float size = 0.26f + SceneArtHash.Hash01(seed, i, 67) * 0.16f;
                b.AddBox(new Vector3(px, surfaceCenter.y - 0.11f, pz),
                    new Vector3(size, 0.22f, size),
                    SceneArtHash.Hash01(seed, i, 71) * 90f);
            }
        }

        /// <summary>棱线岩块（岩唇之外的散落石）：低多边形不规则石块，顶面贴地。</summary>
        public static void AddRockChunk(MeshBuffers b, Vector3 surfacePos, float radius, int seed)
        {
            if (b == null)
                return;

            // 中心下移，使石块顶面大致在地表附近（不抬高碰撞语义）。
            Vector3 c = surfacePos + Vector3.down * (radius * 0.55f);
            b.AddRock(c, radius, new Vector3(1f, 0.75f, 1f), seed, 7);
        }
    }
}
