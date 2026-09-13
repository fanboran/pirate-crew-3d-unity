using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.PirateCrew.Visual
{
    /// <summary>
    /// 程序化网格工厂：按 docs/角色造型规范.md §6.2 的生成器清单，
    /// 用确定性算法产出零件几何（顶点 / 法线 / UV / 三角面），**不依赖任何美术资产**。
    ///
    /// 【与规格的对应】
    ///   · 躯干圆台（"梯形"本体，Unity 无内置图元）→ <see cref="Frustum"/> / <see cref="Lathe"/>
    ///   · 球头 / 眼睛 / 肩球 / 火球 / 胡须球 → <see cref="LowPolySphere"/> / <see cref="BlobCluster"/>
    ///   · 四肢 / 木腿 / 刀柄 → <see cref="Cylinder"/> / <see cref="Capsule"/>
    ///   · 三角帽 → <see cref="Tricorn"/>
    ///   · 铁钩 / 肋骨 → <see cref="ArcRib"/>
    ///   · 刀身 / 帽檐片 / 腰带扣 → <see cref="Box"/>
    ///
    /// 【法线与 UV 口径】
    ///   · 曲面（球 / 车削 / 弯管）用**光滑法线**，接缝列重复一圈顶点保证 UV 连续且不裂缝；
    ///   · 硬边（盒 / 端盖）用**分面法线**（每面独立顶点），避免"假圆角"；
    ///   · 两极用扇形三角（不产生退化三角，见 <see cref="LowPolySphere"/>）。
    ///
    /// 【三角面预算】单单位 ≤ 2500 tri（docs/角色造型规范.md §1.4）。默认分段按"低模"取值，
    /// 实测各职业预算见 <c>CrewVisualPrefabBuilder</c> 的报告输出。
    ///
    /// 【无头边界】本类的生成函数全部是纯 C#；只有 <see cref="CreateMesh"/> 触碰原生 Mesh。
    /// </summary>
    public static class CrewMeshFactory
    {
        /// <summary>单单位三角面预算（docs/角色造型规范.md §1.4：≤2500）。</summary>
        public const int MaxTrianglesPerUnit = 2500;

        // ------------------------------------------------------------------
        // 分段默认值（docs/角色造型规范.md §6.2）
        // ------------------------------------------------------------------

        /// <summary>圆台/车削默认分段（§6.2：sides=10）。</summary>
        public const int DefaultFrustumSides = 10;

        /// <summary>低模球默认经向分段（§6.2：10×6 ≈120 tri）。</summary>
        public const int DefaultSphereSegments = 10;

        /// <summary>低模球默认纬向分段（§6.2：10×6 ≈120 tri）。</summary>
        public const int DefaultSphereRings = 6;

        /// <summary>弯管默认轴向分段。</summary>
        public const int DefaultArcSegments = 8;

        /// <summary>弯管默认截面分段（8 段小尺寸即可，§5.1 Torus 慎用）。</summary>
        public const int DefaultTubeSides = 6;

        // ------------------------------------------------------------------
        // 球
        // ------------------------------------------------------------------

        /// <summary>
        /// 低模 UV 球：两极用扇形三角收口（不产生零面积三角），经线一圈重复顶点保证 UV 接缝。
        /// 三角面数 = <c>2 × segments × (rings − 1)</c>（默认 10×6 = 100 tri）。
        /// </summary>
        public static MeshData LowPolySphere(float radius, int segments, int rings)
        {
            if (segments < 3) segments = 3;
            if (rings < 2) rings = 2;
            if (radius <= 0f) radius = 1e-4f;

            int cols = segments + 1;              // 接缝重复列
            int rows = rings + 1;
            var vertices = new Vector3[cols * rows];
            var normals = new Vector3[cols * rows];
            var uvs = new Vector2[cols * rows];

            for (int i = 0; i < rows; i++)
            {
                float vRatio = (float)i / rings;
                float theta = Mathf.PI * vRatio;          // 0=北极，π=南极
                float y = Mathf.Cos(theta);
                float r = Mathf.Sin(theta);

                for (int j = 0; j < cols; j++)
                {
                    float uRatio = (float)j / segments;
                    float phi = 2f * Mathf.PI * uRatio;

                    var unit = new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
                    int index = i * cols + j;
                    vertices[index] = unit * radius;
                    normals[index] = unit;                // 球心在原点，单位方向即法线
                    uvs[index] = new Vector2(uRatio, 1f - vRatio);
                }
            }

            var triangles = new List<int>(segments * (rings - 1) * 2);
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = i * cols + j;                 // (i, j)
                    int b = (i + 1) * cols + j;           // (i+1, j)
                    int c = (i + 1) * cols + j + 1;       // (i+1, j+1)
                    int d = i * cols + j + 1;             // (i, j+1)

                    if (i == 0)
                    {
                        // 北极扇：a 与 d 重合，只发一个三角。
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    }
                    else if (i == rings - 1)
                    {
                        // 南极扇：b 与 c 重合，只发一个三角。
                        triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    }
                }
            }

            return new MeshData(vertices, normals, uvs, triangles.ToArray());
        }

        // ------------------------------------------------------------------
        // 车削：圆台 / 圆柱 / 胶囊 / 瓶身（通用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 车削面：profile 是 (半径, 高度) 折线，按 Y 轴旋转 <paramref name="sides"/> 段。
        /// 曲面用光滑法线（由折线切线推出），可选上下端盖（分面法线）。
        ///
        /// 【半径=0 的行】视作极点，对应波段只发一个扇形三角（不产生退化三角）。
        /// 【零点位置】几何沿 Y 居中于 profile 的高度范围，调用方用 Transform 摆位。
        /// </summary>
        public static MeshData Lathe(IList<Vector2> profile, int sides, bool capBottom, bool capTop)
        {
            if (profile == null || profile.Count < 2)
                return MeshData.Empty;
            if (sides < 3) sides = 3;

            int rows = profile.Count;
            int cols = sides + 1;
            var vertices = new Vector3[rows * cols];
            var normals = new Vector3[rows * cols];
            var uvs = new Vector2[rows * cols];

            float minY = profile[0].y;
            float maxY = profile[0].y;
            for (int k = 1; k < rows; k++)
            {
                minY = Mathf.Min(minY, profile[k].y);
                maxY = Mathf.Max(maxY, profile[k].y);
            }
            float heightSpan = Mathf.Max(maxY - minY, 1e-6f);

            for (int k = 0; k < rows; k++)
            {
                Vector2 cur = profile[k];

                // 折线切线（首尾用单侧差分）。
                Vector2 tangent;
                if (k == 0) tangent = profile[1] - profile[0];
                else if (k == rows - 1) tangent = profile[rows - 1] - profile[rows - 2];
                else tangent = profile[k + 1] - profile[k - 1];

                Vector2 radialNormal;
                if (cur.x <= 1e-6f)
                {
                    // 极点：法线只有 ±Y 分量。首行（底极）朝下、末行（顶极）朝上，
                    // 中间出现的极点按折线走向取符号。
                    float sign = k <= 0 ? -1f : (k >= rows - 1 ? 1f : (tangent.y >= 0f ? 1f : -1f));
                    radialNormal = new Vector2(0f, sign);
                }
                else
                {
                    // 折线 (dr, dy) 的垂线朝外：(dy, -dr)。
                    radialNormal = new Vector2(tangent.y, -tangent.x);
                    if (radialNormal.sqrMagnitude < 1e-12f)
                        radialNormal = new Vector2(1f, 0f);
                    radialNormal.Normalize();
                }

                float v = (cur.y - minY) / heightSpan;
                for (int j = 0; j < cols; j++)
                {
                    float u = (float)j / sides;
                    float phi = 2f * Mathf.PI * u;
                    float cos = Mathf.Cos(phi);
                    float sin = Mathf.Sin(phi);

                    int index = k * cols + j;
                    vertices[index] = new Vector3(cur.x * cos, cur.y, cur.x * sin);
                    normals[index] = new Vector3(radialNormal.x * cos, radialNormal.y, radialNormal.x * sin);
                    uvs[index] = new Vector2(u, v);
                }
            }

            var triangles = new List<int>((rows - 1) * sides * 2 + sides * 2);
            for (int k = 0; k < rows - 1; k++)
            {
                bool bottomPole = profile[k].x <= 1e-6f;
                bool topPole = profile[k + 1].x <= 1e-6f;

                for (int j = 0; j < sides; j++)
                {
                    int a = k * cols + j;
                    int b = (k + 1) * cols + j;
                    int c = (k + 1) * cols + j + 1;
                    int d = k * cols + j + 1;

                    if (bottomPole)
                    {
                        // 底极：a 与 d 重合 → 只发 (a, b, c)。profile 自下而上，绕向与球面相反。
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    }
                    else if (topPole)
                    {
                        // 顶极：b 与 c 重合 → 只发 (a, c, d)。
                        triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    }
                }
            }

            // 端盖（分面法线，独立顶点）。数组不可变，用 MeshData.Combine 拼接。
            MeshData body = new MeshData(vertices, normals, uvs, triangles.ToArray());
            if (capBottom && profile[0].x > 1e-6f)
                body = MeshData.Combine(body, BuildCap(profile[0], sides, up: false));
            if (capTop && profile[rows - 1].x > 1e-6f)
                body = MeshData.Combine(body, BuildCap(profile[rows - 1], sides, up: true));
            return body;
        }

        /// <summary>构建端盖：中心顶点 + 扇形，法线 ±Y（朝向由 up 决定）。</summary>
        static MeshData BuildCap(Vector2 profilePoint, int sides, bool up)
        {
            int vertCount = sides + 1;
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[sides * 3];

            float y = profilePoint.y;
            float r = profilePoint.x;
            float ny = up ? 1f : -1f;

            vertices[0] = new Vector3(0f, y, 0f);
            normals[0] = new Vector3(0f, ny, 0f);
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int j = 0; j < sides; j++)
            {
                float phi = 2f * Mathf.PI * (float)j / sides;
                vertices[j + 1] = new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
                normals[j + 1] = new Vector3(0f, ny, 0f);
                uvs[j + 1] = new Vector2(Mathf.Cos(phi) * 0.5f + 0.5f, Mathf.Sin(phi) * 0.5f + 0.5f);
            }

            for (int j = 0; j < sides; j++)
            {
                int p0 = j + 1;
                int p1 = (j + 1) % sides + 1;
                int t = j * 3;
                if (up)
                {
                    // 顶盖朝 +Y：逆序使几何法线 = +Y。
                    triangles[t] = 0; triangles[t + 1] = p1; triangles[t + 2] = p0;
                }
                else
                {
                    // 底盖朝 −Y。
                    triangles[t] = 0; triangles[t + 1] = p0; triangles[t + 2] = p1;
                }
            }

            return new MeshData(vertices, normals, uvs, triangles);
        }

        /// <summary>
        /// 圆台/截锥（躯干本体）：上半径 + 下半径 + 高度，沿 Y 居中。
        /// 这是用户原话"一个梯形"的几何载体（Unity 无内置 Cone/Frustum 图元）。
        /// </summary>
        public static MeshData Frustum(float topRadius, float bottomRadius, float height,
            int sides = DefaultFrustumSides, bool capTop = true, bool capBottom = true)
        {
            return Lathe(
                new[]
                {
                    new Vector2(bottomRadius, -height * 0.5f),
                    new Vector2(topRadius, height * 0.5f),
                },
                sides, capBottom, capTop);
        }

        /// <summary>圆柱（四肢 / 木腿 / 柄）：上下等径的圆台。</summary>
        public static MeshData Cylinder(float radius, float height, int sides = 8,
            bool capTop = true, bool capBottom = true)
        {
            return Frustum(radius, radius, height, sides, capTop, capBottom);
        }

        /// <summary>
        /// 胶囊（前臂 / 小腿，要圆端）：圆柱 + 两端半球。
        /// 总高 = <paramref name="cylinderHeight"/> + 2 × <paramref name="radius"/>，沿 Y 居中。
        /// </summary>
        public static MeshData Capsule(float radius, float cylinderHeight, int sides = 8, int hemisphereRings = 3)
        {
            if (radius <= 0f) radius = 1e-4f;
            if (hemisphereRings < 1) hemisphereRings = 1;

            var profile = new List<Vector2>(2 * hemisphereRings + 4);
            float half = cylinderHeight * 0.5f;

            // 下半球：从底极 (−half−r) 到赤道 (−half)。
            profile.Add(new Vector2(0f, -half - radius));
            for (int i = 1; i <= hemisphereRings; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * i / hemisphereRings; // −90° → 0°
                profile.Add(new Vector2(radius * Mathf.Cos(a), -half + radius * Mathf.Sin(a)));
            }

            // 上半球：从赤道 (+half) 到顶极 (+half+r)。
            for (int i = 0; i <= hemisphereRings; i++)
            {
                float a = Mathf.PI * 0.5f * i / hemisphereRings;                    // 0° → 90°
                profile.Add(new Vector2(radius * Mathf.Cos(a), half + radius * Mathf.Sin(a)));
            }

            return Lathe(profile, sides, capBottom: false, capTop: false);
        }

        // ------------------------------------------------------------------
        // 盒
        // ------------------------------------------------------------------

        /// <summary>
        /// 轴对齐盒（刀身 / 帽檐片 / 腰带扣 / 火药袋口）：6 面 × 2 三角 = 12 tri，硬边法线。
        /// 规格明确"单位本体任何部分不得由盒主导"（Art Bible §5.4），盒只作小部件。
        /// </summary>
        public static MeshData Box(Vector3 size)
        {
            float hx = Mathf.Max(Mathf.Abs(size.x) * 0.5f, 1e-5f);
            float hy = Mathf.Max(Mathf.Abs(size.y) * 0.5f, 1e-5f);
            float hz = Mathf.Max(Mathf.Abs(size.z) * 0.5f, 1e-5f);

            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];

            // (normal, right, up, halfRight, halfUp)，满足 cross(right, up) = normal。
            AddBoxFace(vertices, normals, uvs, triangles, 0, new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0), hz, hy, hx);
            AddBoxFace(vertices, normals, uvs, triangles, 1, new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), hz, hy, hx);
            AddBoxFace(vertices, normals, uvs, triangles, 2, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), hx, hz, hy);
            AddBoxFace(vertices, normals, uvs, triangles, 3, new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), hx, hz, hy);
            AddBoxFace(vertices, normals, uvs, triangles, 4, new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), hx, hy, hz);
            AddBoxFace(vertices, normals, uvs, triangles, 5, new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), hx, hy, hz);

            return new MeshData(vertices, normals, uvs, triangles);
        }

        static void AddBoxFace(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles,
            int faceIndex, Vector3 normal, Vector3 right, Vector3 up, float halfRight, float halfUp, float halfNormal)
        {
            Vector3 center = normal * halfNormal;
            int v = faceIndex * 4;
            vertices[v + 0] = center - right * halfRight - up * halfUp;
            vertices[v + 1] = center + right * halfRight - up * halfUp;
            vertices[v + 2] = center + right * halfRight + up * halfUp;
            vertices[v + 3] = center - right * halfRight + up * halfUp;

            for (int i = 0; i < 4; i++)
                normals[v + i] = normal;

            uvs[v + 0] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(1f, 0f);
            uvs[v + 2] = new Vector2(1f, 1f);
            uvs[v + 3] = new Vector2(0f, 1f);

            int t = faceIndex * 6;
            triangles[t + 0] = v + 0; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 0; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }

        // ------------------------------------------------------------------
        // 弯管 / 环
        // ------------------------------------------------------------------

        /// <summary>
        /// 弯管（XZ 平面内的圆弧管，绕 Y 轴）：用于铁钩（270°）与骷髅肋骨（180°）。
        /// 管截面为正圆，法线由管心径向给出。两端不封口（口径小、被手/躯干遮挡）。
        /// 三角面数 = <c>2 × segments × tubeSides</c>（默认 8×6 = 96 tri，与规格"钩约 96 tri"一致）。
        /// </summary>
        public static MeshData ArcRib(float radius, float tubeRadius, float sweepDegrees,
            int segments = DefaultArcSegments, int tubeSides = DefaultTubeSides)
        {
            if (segments < 2) segments = 2;
            if (tubeSides < 3) tubeSides = 3;
            radius = Mathf.Max(radius, 1e-4f);
            tubeRadius = Mathf.Max(tubeRadius, 1e-4f);

            float sweep = sweepDegrees * Mathf.Deg2Rad;
            int cols = tubeSides + 1;

            var vertices = new Vector3[(segments + 1) * cols];
            var normals = new Vector3[(segments + 1) * cols];
            var uvs = new Vector2[(segments + 1) * cols];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float theta = sweep * t;
                float cosT = Mathf.Cos(theta);
                float sinT = Mathf.Sin(theta);

                for (int j = 0; j <= tubeSides; j++)
                {
                    float u = (float)j / tubeSides;
                    float phi = 2f * Mathf.PI * u;
                    float cosP = Mathf.Cos(phi);
                    float sinP = Mathf.Sin(phi);

                    int index = i * cols + j;
                    vertices[index] = new Vector3(
                        (radius + tubeRadius * cosP) * cosT,
                        tubeRadius * sinP,
                        (radius + tubeRadius * cosP) * sinT);
                    normals[index] = new Vector3(cosP * cosT, sinP, cosP * sinT);
                    uvs[index] = new Vector2(t, u);
                }
            }

            var triangles = new List<int>(segments * tubeSides * 2);
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < tubeSides; j++)
                {
                    int a = i * cols + j;
                    int b = (i + 1) * cols + j;
                    int c = (i + 1) * cols + j + 1;
                    int d = i * cols + j + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                }
            }

            return new MeshData(vertices, normals, uvs, triangles.ToArray());
        }

        // ------------------------------------------------------------------
        // 三角帽
        // ------------------------------------------------------------------

        /// <summary>
        /// 三角帽（船长专属）：帽冠圆台 + 三片 120° 均布的上翻帽檐片（盒，闭合几何以保描边完整）。
        /// 【提案】三片翻檐的倾角/长度按 docs/角色造型规范.md §3.8 的"宽 0.13 / 高 0.07"反推，
        /// 原版无三角帽几何数据，规格也标为自定义网格。
        /// </summary>
        public static MeshData Tricorn(float brimRadius, float crownRadius, float crownHeight,
            float foldAngleDegrees, float brimThickness = 0.012f, int sides = 10)
        {
            // 帽冠：上窄下宽（Art Bible §5.1 禁止上下等径）。
            float crownBottomY = 0f;
            MeshData crown = Lathe(
                new[]
                {
                    new Vector2(crownRadius, crownBottomY),
                    new Vector2(crownRadius * 0.72f, crownBottomY + crownHeight),
                },
                sides, capBottom: false, capTop: true);

            // 三片翻檐：每片是沿 X 方向伸出的薄盒，绕 Z 上翻 foldAngle，再绕 Y 均布。
            float flapLength = brimRadius * 1.85f;
            float flapWidth = brimRadius * 0.95f;
            MeshData flap = Box(new Vector3(flapLength, brimThickness, flapWidth));
            // 盒心前移到约 0.45×brimRadius，使内缘与帽冠相接、外缘外挑。
            flap = Translate(flap, new Vector3(brimRadius * 0.45f, crownBottomY, 0f));
            flap = RotateZ(flap, foldAngleDegrees);

            var parts = new List<MeshData>(4) { crown };
            for (int k = 0; k < 3; k++)
                parts.Add(RotateY(flap, k * 120f));

            return MeshData.Combine(parts.ToArray());
        }

        // ------------------------------------------------------------------
        // 球簇
        // ------------------------------------------------------------------

        /// <summary>球簇中的一个椭球：中心 + 三轴半径（用于压扁胡须球/肩球）。</summary>
        public struct Blob
        {
            public Vector3 Center;
            public Vector3 Radii;

            public Blob(Vector3 center, Vector3 radii)
            {
                Center = center;
                Radii = radii;
            }
        }

        /// <summary>
        /// 球簇（胡须 / 乱发 / 脊椎球串）：把若干椭球合并成单个网格以省 draw call。
        /// 椭球用对角阵缩放 → 法线按"逆尺度"修正（component-wise 除以 radii²）。
        /// </summary>
        public static MeshData BlobCluster(IList<Blob> blobs, int segments = 8, int rings = 5)
        {
            if (blobs == null || blobs.Count == 0)
                return MeshData.Empty;

            var parts = new List<MeshData>(blobs.Count);
            for (int i = 0; i < blobs.Count; i++)
            {
                MeshData sphere = LowPolySphere(1f, segments, rings);
                parts.Add(ScaleEllipsoid(sphere, blobs[i]));
            }

            return MeshData.Combine(parts.ToArray());
        }

        // ------------------------------------------------------------------
        // 网格变换（供组合几何用；装配层用 Transform，不在此处烘坐标）
        // ------------------------------------------------------------------

        /// <summary>平移一份网格数据（法线不变）。</summary>
        public static MeshData Translate(MeshData source, Vector3 offset)
        {
            var vertices = new Vector3[source.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = source.Vertices[i] + offset;
            return new MeshData(vertices, (Vector3[])source.Normals.Clone(), (Vector2[])source.Uvs.Clone(), (int[])source.Triangles.Clone());
        }

        /// <summary>绕 X 轴旋转（角度制，纯三角函数实现——不能用 Quaternion.Euler：那是 ECall，无头会崩）。</summary>
        public static MeshData RotateX(MeshData source, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return ApplyRotation(source, p => new Vector3(p.x, p.y * c - p.z * s, p.y * s + p.z * c));
        }

        /// <summary>绕 Y 轴旋转（Unity 左手系：+Z 转向 +X）。</summary>
        public static MeshData RotateY(MeshData source, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return ApplyRotation(source, p => new Vector3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c));
        }

        /// <summary>绕 Z 轴旋转（+X 转向 +Y）。</summary>
        public static MeshData RotateZ(MeshData source, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return ApplyRotation(source, p => new Vector3(p.x * c - p.y * s, p.x * s + p.y * c, p.z));
        }

        /// <summary>对顶点与法线施加同一个旋转。</summary>
        static MeshData ApplyRotation(MeshData source, System.Func<Vector3, Vector3> rotate)
        {
            var vertices = new Vector3[source.VertexCount];
            var normals = new Vector3[source.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = rotate(source.Vertices[i]);
                normals[i] = rotate(source.Normals[i]);
            }
            return new MeshData(vertices, normals, (Vector2[])source.Uvs.Clone(), (int[])source.Triangles.Clone());
        }

        /// <summary>椭球缩放：位置按 radii 缩放，法线按 component-wise 除以 radii² 后归一化。</summary>
        static MeshData ScaleEllipsoid(MeshData source, Blob blob)
        {
            Vector3 r = blob.Radii;
            float rx = Mathf.Max(Mathf.Abs(r.x), 1e-5f);
            float ry = Mathf.Max(Mathf.Abs(r.y), 1e-5f);
            float rz = Mathf.Max(Mathf.Abs(r.z), 1e-5f);

            var vertices = new Vector3[source.VertexCount];
            var normals = new Vector3[source.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = source.Vertices[i];
                vertices[i] = new Vector3(p.x * rx, p.y * ry, p.z * rz) + blob.Center;

                Vector3 n = source.Normals[i];
                var adjusted = new Vector3(n.x / (rx * rx), n.y / (ry * ry), n.z / (rz * rz));
                normals[i] = adjusted.sqrMagnitude < 1e-12f ? Vector3.up : adjusted.normalized;
            }

            return new MeshData(vertices, normals, (Vector2[])source.Uvs.Clone(), (int[])source.Triangles.Clone());
        }

        // ------------------------------------------------------------------
        // Unity 适配（唯一触碰原生对象的一步）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把纯数据打包成 Unity <see cref="Mesh"/>（仅可在 Unity 运行时/编辑器调用；无头验证台请勿调用）。
        /// </summary>
        public static Mesh CreateMesh(string name, MeshData data)
        {
            var mesh = new Mesh { name = name };
            if (data.VertexCount > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uvs);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>把已存在的 mesh 资产就地刷新（幂等重建用，避免换 GUID 破坏引用）。</summary>
        public static void ApplyToMesh(Mesh mesh, MeshData data)
        {
            if (mesh == null)
                return;

            mesh.Clear();
            mesh.indexFormat = data.VertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uvs);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
