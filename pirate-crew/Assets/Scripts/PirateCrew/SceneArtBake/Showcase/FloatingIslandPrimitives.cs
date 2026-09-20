using UnityEngine;

namespace PirateCrew.SceneArt.Showcase
{
    /// <summary>
    /// 空岛形态库（纯 C#，无头可测）：把 <see cref="MeshBuffers"/> 已有的图元
    /// （盒/圆台/弯管/岩块/叶片/圆盘）补上浮空岛特有的形状——
    /// **不规则环、穹顶、岩层带、低模球团、晶体、水帘、云团、石笋、岩鳍、鸟**。
    ///
    /// 【为什么全部写在这里、不散进总装】总装（<see cref="FloatingIslandComposer"/>）只该读成
    /// "这座岛由哪些部件、摆在什么角度"；一旦每个部件内联着顶点推演，改一个曲率就要在几百行里找。
    /// 形状与布置分离后，"调形状"和"调构图"是两件互不干扰的事。
    ///
    /// 【绕序纪律】所有面都经 <see cref="MeshBuffers.AddQuad"/>/本类 <see cref="AddTri"/> 的
    /// **法线提示**自动纠正绕序：调用方只声明"这个面朝外/朝上"，不手推顶点顺序——
    /// 这是本工程几何里最容易出错、也最不值得手推的地方。
    ///
    /// 【无碰撞】只写三角面，不产生任何 Collider（与本工程所有装饰同一纪律）。
    /// </summary>
    public static class IslandPrimitives
    {
        // ==================================================================
        // 基础：绕序自纠正的三角形
        // ==================================================================

        /// <summary>追加三角形，绕序由 <paramref name="outwardHint"/> 决定（反了就翻）。</summary>
        public static void AddTri(MeshBuffers b, Vector3 a, Vector3 v1, Vector3 v2, Vector3 outwardHint)
        {
            if (b == null)
                return;

            Vector3 n = Vector3.Cross(v1 - a, v2 - a);
            if (n.sqrMagnitude < 1e-16f)
                return;   // 退化面直接丢（与 MeshBuffers.AddTriangle 同口径）

            if (Vector3.Dot(n, outwardHint) < 0f)
                b.AddTriangle(a, v2, v1);
            else
                b.AddTriangle(a, v1, v2);
        }

        /// <summary>追加双面三角形（叶片/鸟翼/薄旗这类零厚度面片，45° 俯视下两面都要可见）。</summary>
        public static void AddTriDouble(MeshBuffers b, Vector3 a, Vector3 v1, Vector3 v2, Vector3 outwardHint)
        {
            if (b == null)
                return;

            Vector3 n = Vector3.Cross(v1 - a, v2 - a);
            if (n.sqrMagnitude < 1e-16f)
                return;

            bool positive = Vector3.Dot(n, outwardHint) >= 0f;
            if (positive)
            {
                b.AddTriangle(a, v1, v2);
                b.AddTriangle(a, v2, v1);
            }
            else
            {
                b.AddTriangle(a, v2, v1);
                b.AddTriangle(a, v1, v2);
            }
        }

        // ==================================================================
        // 不规则环（岛形轮廓的最小单元）
        // ==================================================================

        /// <summary>
        /// 绕 Y 一圈的**不规则**环：逐顶点角度抖动 ±0.42 半段、半径抖动 <paramref name="radiusJitter"/>、
        /// 高度抖动 <paramref name="yJitter"/>。
        ///
        /// 【为什么必须抖】等半径等分角的环 + 低多边形 = "正多边形花盆"，一眼假。
        /// 角度抖动要**小于半段**（0.42）才不会让顶点在环上交叉自交。
        /// </summary>
        public static Vector3[] Ring(int segments, float y, float rx, float rz, int seed, int salt,
            float radiusJitter = 0.14f, float yJitter = 0f)
        {
            segments = Mathf.Max(3, segments);
            float step = Mathf.PI * 2f / segments;
            var pts = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float ang = step * i + SceneArtHash.SignedHash(seed, i, salt) * step * 0.42f;
                float r = 1f + SceneArtHash.SignedHash(seed, i, salt + 7) * radiusJitter;
                float py = y + SceneArtHash.SignedHash(seed, i, salt + 13) * yJitter;
                pts[i] = new Vector3(Mathf.Cos(ang) * rx * r, py, Mathf.Sin(ang) * rz * r);
            }
            return pts;
        }

        /// <summary>把环整体平移（环本身绕原点生成，布置时再挪）。</summary>
        public static Vector3[] OffsetRing(Vector3[] src, Vector3 offset)
        {
            if (src == null)
                return null;

            var pts = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++)
                pts[i] = src[i] + offset;
            return pts;
        }

        /// <summary>
        /// 把环按 XZ 比例缩放并抬到新高度（岩层带之间"逐层收进"的主算子）。
        /// <paramref name="stepJitter"/> 让每层收进量本身也带抖动——层理才不会像套筒。
        /// </summary>
        public static Vector3[] ScaleRing(Vector3[] src, float scaleXZ, float y, int seed, int salt,
            float stepJitter = 0.05f, float yJitter = 0f)
        {
            if (src == null)
                return null;

            var pts = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float k = scaleXZ * (1f + SceneArtHash.SignedHash(seed, i, salt) * stepJitter);
                pts[i] = new Vector3(src[i].x * k, y + SceneArtHash.SignedHash(seed, i, salt + 5) * yJitter,
                    src[i].z * k);
            }
            return pts;
        }

        /// <summary>两个环之间的侧面（<paramref name="top"/> 在上、<paramref name="bottom"/> 在下，法线朝径向外）。</summary>
        public static void AddSideRing(MeshBuffers b, Vector3[] top, Vector3[] bottom)
        {
            if (b == null || top == null || bottom == null || top.Length != bottom.Length)
                return;

            int n = top.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 mid = (top[i] + top[j] + bottom[i] + bottom[j]) * 0.25f;
                Vector3 outward = new Vector3(mid.x, 0f, mid.z);
                if (outward.sqrMagnitude < 1e-10f)
                    outward = Vector3.right;

                b.AddQuad(top[i], bottom[i], bottom[j], top[j], outward.normalized);
            }
        }

        /// <summary>从 <paramref name="hub"/> 到环的三角扇（顶盖/底盖/锥尖）。</summary>
        public static void AddFan(MeshBuffers b, Vector3[] ring, Vector3 hub, Vector3 normalHint)
        {
            if (b == null || ring == null)
                return;

            int n = ring.Length;
            for (int i = 0; i < n; i++)
                AddTri(b, hub, ring[i], ring[(i + 1) % n], normalHint);
        }

        /// <summary>两环之间的环形带（同向，用于平顶/平的底面）。</summary>
        public static void AddAnnulus(MeshBuffers b, Vector3[] outer, Vector3[] inner, Vector3 normalHint)
        {
            if (b == null || outer == null || inner == null || outer.Length != inner.Length)
                return;

            int n = outer.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                b.AddQuad(outer[i], inner[i], inner[j], outer[j], normalHint);
            }
        }

        // ==================================================================
        // 低模球团（树冠/灌木/云/雾团的最小单元）
        // ==================================================================

        /// <summary>
        /// 低模椭球团：<paramref name="layers"/> 条纬线 + 上下两个极点。
        /// <paramref name="jitter"/> 让每条纬线的半径各不相同——球团才不是"乒乓球"。
        /// 顶点数 ≈ (layers×segments×2 + 极点扇)，8 段 3 层约 60 面。
        /// </summary>
        public static void AddBlob(MeshBuffers b, Vector3 center, Vector3 radii, int layers, int segments,
            int seed, int salt, float jitter = 0.15f)
        {
            if (b == null || radii.x <= 0f || radii.y <= 0f || radii.z <= 0f)
                return;

            layers = Mathf.Clamp(layers, 2, 8);
            segments = Mathf.Clamp(segments, 4, 16);

            var grid = new Vector3[layers][];
            for (int k = 0; k < layers; k++)
            {
                float t = (k + 1f) / (layers + 1f);
                float phi = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                float rf = Mathf.Cos(phi);
                float yy = Mathf.Sin(phi) * radii.y;

                grid[k] = new Vector3[segments];
                for (int s = 0; s < segments; s++)
                {
                    float ang = Mathf.PI * 2f * s / segments;
                    float jr = 1f + SceneArtHash.SignedHash(seed, k * 37 + s, salt) * jitter;
                    grid[k][s] = center + new Vector3(Mathf.Cos(ang) * rf * radii.x * jr, yy,
                        Mathf.Sin(ang) * rf * radii.z * jr);
                }
            }

            // 侧面：相邻纬线之间的四边形环带。
            for (int k = 0; k < layers - 1; k++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int t = (s + 1) % segments;
                    Vector3 mid = (grid[k][s] + grid[k][t] + grid[k + 1][s] + grid[k + 1][t]) * 0.25f;
                    Vector3 outward = mid - center;
                    if (outward.sqrMagnitude < 1e-10f)
                        outward = Vector3.up;

                    b.AddQuad(grid[k][s], grid[k + 1][s], grid[k + 1][t], grid[k][t], outward.normalized);
                }
            }

            Vector3 topPole = center + new Vector3(
                radii.x * 0.20f * SceneArtHash.SignedHash(seed, 1, salt + 3),
                radii.y * (1f + SceneArtHash.SignedHash(seed, 2, salt + 4) * jitter * 0.5f),
                radii.z * 0.20f * SceneArtHash.SignedHash(seed, 3, salt + 5));
            Vector3 bottomPole = center + new Vector3(
                radii.x * 0.20f * SceneArtHash.SignedHash(seed, 4, salt + 6),
                -radii.y * (1f + SceneArtHash.SignedHash(seed, 5, salt + 7) * jitter * 0.4f),
                radii.z * 0.20f * SceneArtHash.SignedHash(seed, 6, salt + 8));

            AddFan(b, grid[0], bottomPole, Vector3.down);
            AddFan(b, grid[layers - 1], topPole, Vector3.up);
        }

        // ==================================================================
        // 晶体（空岛的"魔力"来源）
        // ==================================================================

        /// <summary>
        /// 六棱水晶：下锥（短）→ 腰环 → 上锥（长尖）。
        /// 【为什么尖在上、锥在下】低模晶体的辨识度全在"长尖"——尖端给出方向感，
        /// 悬浮晶因此读得出"生长/漂浮"的朝向，而不是一块石头。
        /// 先在局部坐标搭好再 <see cref="MeshBuffers.AppendTransformed"/> 到目标位姿
        /// （<c>Quaternion.Euler</c> / <c>Matrix4x4.TRS</c> 是原生 ECall，无头必抛，
        /// 故一律走 <see cref="SceneArtRot"/>）。
        /// </summary>
        public static void AddCrystal(MeshBuffers b, Vector3 basePos, float radius, float height,
            Vector3 eulerDegrees, int seed, int sides = 5, float jitter = 0.13f)
        {
            if (b == null || radius <= 0f || height <= 0f)
                return;

            var local = new MeshBuffers();

            Vector3[] lower = Ring(sides, height * 0.20f, radius, radius, seed, 41, jitter, 0f);
            Vector3[] waist = Ring(sides, height * 0.52f, radius * 0.90f, radius * 0.90f, seed, 47, jitter, 0f);

            AddSideRing(local, waist, lower);

            Vector3 apex = new Vector3(
                radius * 0.16f * SceneArtHash.SignedHash(seed, 0, 51), height,
                radius * 0.16f * SceneArtHash.SignedHash(seed, 1, 53));
            AddFan(local, waist, apex, Vector3.up);

            Vector3 foot = new Vector3(
                radius * 0.14f * SceneArtHash.SignedHash(seed, 2, 57), -height * 0.24f,
                radius * 0.14f * SceneArtHash.SignedHash(seed, 3, 59));
            AddFan(local, lower, foot, Vector3.down);

            b.AppendTransformed(local,
                SceneArtRot.Trs(basePos, SceneArtRot.Euler(eulerDegrees.x, eulerDegrees.y, eulerDegrees.z), Vector3.one));
        }

        // ==================================================================
        // 水帘 / 雾团
        // ==================================================================

        /// <summary>
        /// 沿折线扫出的双面帘（瀑布主体与水沫芯共用）：<paramref name="halfWidths"/> 逐点给半宽，
        /// 于是"水流收窄 → 落到底部散开"这条剖面曲线是**数据**，不是写死的。
        /// 宽度方向 = cross(段方向, <paramref name="normalHint"/>)，
        /// 故传"径向外"即得一张朝外的帘；双面保证从岛内侧看也不漏。
        /// </summary>
        public static void AddRibbon(MeshBuffers b, Vector3[] points, float[] halfWidths, Vector3 normalHint,
            bool doubleSided = true)
        {
            if (b == null || points == null || halfWidths == null || points.Length < 2)
                return;

            int n = Mathf.Min(points.Length, halfWidths.Length);
            if (n < 2)
                return;

            Vector3 hint = normalHint.sqrMagnitude > 1e-10f ? normalHint.normalized : Vector3.up;

            for (int k = 0; k < n - 1; k++)
            {
                Vector3 dir = points[k + 1] - points[k];
                if (dir.sqrMagnitude < 1e-10f)
                    continue;

                dir.Normalize();
                Vector3 side = Vector3.Cross(dir, hint);
                if (side.sqrMagnitude < 1e-8f)
                    side = Vector3.Cross(dir, Vector3.up);
                if (side.sqrMagnitude < 1e-8f)
                    continue;

                side.Normalize();

                Vector3 a = points[k] - side * halfWidths[k];
                Vector3 c = points[k] + side * halfWidths[k];
                Vector3 d = points[k + 1] + side * halfWidths[k + 1];
                Vector3 e = points[k + 1] - side * halfWidths[k + 1];

                if (doubleSided)
                    b.AddTrianglesDoubleSided(a, c, d, e, hint);
                else
                    b.AddQuadOrdered(a, c, d, e);
            }
        }

        /// <summary>
        /// 低模云团：<paramref name="lobes"/> 个扁平球团围一圈 + 中心一个大的。
        /// 扁平（y 半径 ≈ 0.6×水平）是"云"与"球"的分界——饱满的球团读成棉花糖。
        /// </summary>
        public static void AddCloudPuff(MeshBuffers b, Vector3 center, float size, int seed, int lobes = 4)
        {
            if (b == null || size <= 0f)
                return;

            lobes = Mathf.Clamp(lobes, 2, 6);
            for (int i = 0; i < lobes; i++)
            {
                float ang = Mathf.PI * 2f * i / lobes + SceneArtHash.Hash01(seed, i, 3) * 0.95f;
                float dist = size * (0.20f + 0.32f * SceneArtHash.Hash01(seed, i, 5));
                float r = size * (0.40f + 0.30f * SceneArtHash.Hash01(seed, i, 7));
                Vector3 c = center + new Vector3(Mathf.Cos(ang) * dist,
                    size * 0.16f * (SceneArtHash.Hash01(seed, i, 11) - 0.5f),
                    Mathf.Sin(ang) * dist * 0.72f);

                AddBlob(b, c, new Vector3(r, r * 0.60f, r * 0.84f), 3, 7, seed + i * 13, 61, 0.17f);
            }

            AddBlob(b, center + Vector3.up * (size * 0.20f),
                new Vector3(size * 0.74f, size * 0.48f, size * 0.62f), 3, 8, seed + 101, 67, 0.15f);
        }

        // ==================================================================
        // 岩体零件
        // ==================================================================

        /// <summary>
        /// 石笋 / 垂岩（岛底倒挂的尖齿）：圆台倒转 180° + 一点倾斜。
        /// 尖端朝下 = 悬空岩体的重力指示，是"浮空"读感的关键部件之一。
        /// </summary>
        public static void AddStalactite(MeshBuffers b, Vector3 rootPos, float length, float radius,
            float yawDegrees, float tiltDegrees, int seed)
        {
            if (b == null || length <= 0f || radius <= 0f)
                return;

            var local = new MeshBuffers();
            // 局部：底面（宽）在 y=0，尖端在 y=+length，capTop=true 封尖。
            local.AddFrustum(Vector3.zero, radius, radius * 0.09f, length, 5, yawDegrees, true, true);

            // 倒转（绕 X 180°）再倾斜，最后平移。SceneArtRot.Euler 顺序与 Quaternion.Euler 一致。
            Matrix4x4 m = SceneArtRot.Trs(rootPos,
                SceneArtRot.Euler(180f + tiltDegrees, yawDegrees, SceneArtHash.SignedHash(seed, 0, 3) * 8f),
                Vector3.one);
            b.AppendTransformed(local, m);
        }

        /// <summary>
        /// 岩鳍 / 扶壁：从 <paramref name="from"/> 到 <paramref name="to"/> 的**收锥长方体**
        /// （两端截面可不同）。崖壁上几条竖向岩鳍能把"一坨圆石头"变成有走向的山体剪影。
        /// </summary>
        public static void AddTaperedBox(MeshBuffers b, Vector3 from, Vector3 to, float halfWidth0,
            float halfThick0, float halfWidth1, float halfThick1, Vector3 upHint)
        {
            if (b == null)
                return;

            Vector3 axis = to - from;
            if (axis.sqrMagnitude < 1e-8f)
                return;

            Vector3 dir = axis.normalized;
            Vector3 up = upHint.sqrMagnitude > 1e-8f ? upHint.normalized : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.98f)
                up = Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up;

            Vector3 side = Vector3.Cross(dir, up).normalized;
            Vector3 realUp = Vector3.Cross(side, dir).normalized;

            Vector3 a0 = from - side * halfWidth0 - realUp * halfThick0;
            Vector3 a1 = from + side * halfWidth0 - realUp * halfThick0;
            Vector3 a2 = from + side * halfWidth0 + realUp * halfThick0;
            Vector3 a3 = from - side * halfWidth0 + realUp * halfThick0;
            Vector3 b0 = to - side * halfWidth1 - realUp * halfThick1;
            Vector3 b1 = to + side * halfWidth1 - realUp * halfThick1;
            Vector3 b2 = to + side * halfWidth1 + realUp * halfThick1;
            Vector3 b3 = to - side * halfWidth1 + realUp * halfThick1;

            b.AddQuad(a0, a1, b1, b0, -realUp);   // 下
            b.AddQuad(a3, a2, b2, b3, realUp);    // 上
            b.AddQuad(a0, a3, b3, b0, -side);     // 左
            b.AddQuad(a1, a2, b2, b1, side);      // 右
            b.AddQuad(b0, b1, b2, b3, dir);       // 远端
            b.AddQuad(a0, a1, a2, a3, -dir);      // 近端
        }

        /// <summary>
        /// 不规则石板/台基（遗迹台基、踏步、过梁）：
        /// 上盖三角扇 + 侧环 + 下盖，全部走不规则环，故"人工石"仍带低模的歪斜感。
        /// 【口径】<paramref name="center"/> **只取 XZ**（y 被忽略），<paramref name="topY"/> 是绝对顶面高度——
        /// 台基要能"叠在另一块台基的顶面高度上"，把两者混在一个 y 里会叠加两次。
        /// </summary>
        public static void AddSlab(MeshBuffers b, Vector3 center, float radiusXZ, float topY, float thickness,
            int seed, int segments = 8, float radiusJitter = 0.12f)
        {
            if (b == null || radiusXZ <= 0f)
                return;

            Vector3 flat = new Vector3(center.x, 0f, center.z);
            Vector3[] topRing = OffsetRing(Ring(segments, topY, radiusXZ, radiusXZ, seed, 71, radiusJitter, 0f), flat);
            Vector3[] botRing = OffsetRing(
                ScaleRing(Ring(segments, topY, radiusXZ, radiusXZ, seed, 71, radiusJitter, 0f),
                    1.02f, topY - thickness, seed, 73, 0.02f, 0f), flat);

            AddFan(b, topRing, new Vector3(center.x, topY + 0.02f, center.z), Vector3.up);
            AddSideRing(b, topRing, botRing);
            AddFan(b, botRing, new Vector3(center.x, topY - thickness, center.z), Vector3.down);
        }

        /// <summary>
        /// 石柱（遗迹）：柱础 + 收分裂柱身 + 柱头（<paramref name="broken"/> = 断柱，顶部换成参差断口）。
        /// </summary>
        public static void AddPillar(MeshBuffers b, Vector3 baseCenter, float height, float radius,
            float yawDegrees, bool broken, int seed)
        {
            if (b == null || height <= 0f || radius <= 0f)
                return;

            // 柱础（双层：宽座 + 上一层略窄的过渡）
            b.AddBox(new Vector3(baseCenter.x, baseCenter.y + 0.10f, baseCenter.z),
                new Vector3(radius * 2.6f, 0.20f, radius * 2.6f), yawDegrees);
            b.AddBox(new Vector3(baseCenter.x, baseCenter.y + 0.26f, baseCenter.z),
                new Vector3(radius * 2.15f, 0.14f, radius * 2.15f), yawDegrees + 12f);

            // 柱身：上细下粗（低模柱不许等宽，等宽读成"管子"）
            b.AddFrustum(baseCenter + Vector3.up * 0.33f, radius * 1.06f, radius * 0.86f, height, 8, yawDegrees,
                !broken, true);

            Vector3 top = baseCenter + Vector3.up * (0.33f + height);

            if (broken)
            {
                // 断口：3 块高矮不一的残石压在断面上，其中一块明显挑出（"刚崩过"的读感）
                for (int i = 0; i < 3; i++)
                {
                    float ang = Mathf.PI * 2f * i / 3f + SceneArtHash.Hash01(seed, i, 5) * 0.9f;
                    float r = radius * (0.20f + 0.55f * SceneArtHash.Hash01(seed, i, 7));
                    float h = radius * (0.55f + 1.75f * SceneArtHash.Hash01(seed, i, 11));
                    b.AddBox(new Vector3(top.x + Mathf.Cos(ang) * r, top.y + h * 0.5f, top.z + Mathf.Sin(ang) * r),
                        new Vector3(radius * 0.85f, h, radius * 0.85f),
                        yawDegrees + SceneArtHash.SignedHash(seed, i, 13) * 28f);
                }
            }
            else
            {
                // 柱头：压顶盘 + 顶檐（两层），比柱身外扩，读得出"这是柱子"的收头
                b.AddBox(new Vector3(top.x, top.y + 0.11f, top.z),
                    new Vector3(radius * 2.25f, 0.22f, radius * 2.25f), yawDegrees);
                b.AddBox(new Vector3(top.x, top.y + 0.31f, top.z),
                    new Vector3(radius * 1.75f, 0.18f, radius * 1.75f), yawDegrees + 8f);
            }
        }

        /// <summary>石堆（路径旁的路标/坟堆）：4-5 块逐层缩小的圆钝岩块。</summary>
        public static void AddCairn(MeshBuffers b, Vector3 basePos, float height, int seed)
        {
            if (b == null || height <= 0f)
                return;

            int layers = 4;
            float r = height * 0.34f;
            float y = basePos.y;
            for (int i = 0; i < layers; i++)
            {
                float t = i / (float)(layers - 1);
                float rr = Mathf.Lerp(r, r * 0.42f, t) * (0.88f + 0.24f * SceneArtHash.Hash01(seed, i, 3));
                y += rr * 0.62f;
                Vector3 c = basePos + new Vector3(
                    r * 0.28f * SceneArtHash.SignedHash(seed, i, 5), y - basePos.y,
                    r * 0.28f * SceneArtHash.SignedHash(seed, i, 7));
                b.AddRock(c, rr, new Vector3(1.05f, 0.62f, 0.95f), seed + i * 17, 6);
                y += rr * 0.35f;
            }
        }

        // ==================================================================
        // 植被零件
        // ==================================================================

        /// <summary>
        /// 草簇：<paramref name="blades"/> 片叶（双面薄片）从一点向外斜出。
        /// 用 <see cref="MeshBuffers.AddLeaf"/>（沿方向"中间隆起、末端下垂"的折面条），
        /// 段数取 1 即单片 4 面 —— 72 簇也只 1200 面。
        /// </summary>
        public static void AddGrassTuft(MeshBuffers b, Vector3 basePos, float scale, int seed, int blades = 5)
        {
            if (b == null || scale <= 0f)
                return;

            blades = Mathf.Clamp(blades, 2, 8);
            for (int i = 0; i < blades; i++)
            {
                float ang = Mathf.PI * 2f * i / blades + SceneArtHash.Hash01(seed, i, 3) * 1.15f;
                float tilt = 0.28f + 0.55f * SceneArtHash.Hash01(seed, i, 7);
                float len = scale * (0.72f + 0.55f * SceneArtHash.Hash01(seed, i, 11));
                Vector3 dir = new Vector3(Mathf.Cos(ang) * tilt, 1f, Mathf.Sin(ang) * tilt);
                b.AddLeaf(basePos, dir, len, scale * 0.30f, scale * 0.16f, 1);
            }
        }

        /// <summary>灌木：3-5 个扁平球团挤在一起（比草簇体量大、比树小一档）。</summary>
        public static void AddBush(MeshBuffers b, Vector3 basePos, float scale, int seed)
        {
            if (b == null || scale <= 0f)
                return;

            int lobes = 3 + (int)(SceneArtHash.Hash01(seed, 0, 3) * 3f);
            for (int i = 0; i < lobes; i++)
            {
                float ang = Mathf.PI * 2f * i / lobes + SceneArtHash.Hash01(seed, i, 5);
                float dist = scale * 0.30f * SceneArtHash.Hash01(seed, i, 7);
                float r = scale * (0.48f + 0.30f * SceneArtHash.Hash01(seed, i, 11));
                Vector3 c = basePos + new Vector3(Mathf.Cos(ang) * dist, r * 0.62f, Mathf.Sin(ang) * dist);
                AddBlob(b, c, new Vector3(r, r * 0.72f, r), 3, 7, seed + i * 23, 91, 0.18f);
            }
        }

        /// <summary>
        /// 阔叶树：微弯树干 + 3 层树冠（下缘暗 / 主体中 / 顶亮）。
        /// 【为什么树冠必须分三层槽位】单一槽位的球团在日光下是一坨同色剪影；
        /// 三层不同草皮档位让树冠自带"上亮下暗"的体积感，是零贴图下最便宜的可读性。
        /// </summary>
        public static void AddTree(IslandBuffers buffers, Vector3 basePos, float height, float yawDegrees, int seed)
        {
            if (buffers == null || height <= 0f)
                return;

            MeshBuffers wood = buffers.Wood;
            float rad = yawDegrees * Mathf.Deg2Rad;
            Vector3 leanDir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            float lean = 0.05f + 0.06f * SceneArtHash.Hash01(seed, 1, 5);

            var trunk = new Vector3[5];
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                trunk[i] = basePos + Vector3.up * (height * t) + leanDir * (height * lean * t * t)
                    + Vector3.up * (height * 0.02f * SceneArtHash.SignedHash(seed, i, 9));
            }
            wood.AddBentTube(trunk, height * 0.062f, height * 0.024f, 6);

            // 露出地面的根须（3 条）：把树"钉"在草皮上，不然树干像是插上去的
            for (int r = 0; r < 3; r++)
            {
                float ra = rad + Mathf.PI * 2f * r / 3f + SceneArtHash.Hash01(seed, r, 13) * 0.8f;
                Vector3 foot = basePos + new Vector3(Mathf.Cos(ra), 0f, Mathf.Sin(ra)) * (height * 0.26f);
                foot.y = basePos.y - height * 0.03f;
                buffers.Dirt.AddRod(basePos + Vector3.up * (height * 0.06f), foot, height * 0.030f, 4);
            }

            Vector3 crown = trunk[4];
            float cw = height * 0.60f;
            AddBlob(buffers.GrassDark, crown - Vector3.up * (height * 0.12f),
                new Vector3(cw * 0.94f, cw * 0.44f, cw * 0.94f), 3, 8, seed, 11, 0.16f);
            AddBlob(buffers.GrassMid, crown + Vector3.up * (height * 0.06f),
                new Vector3(cw, cw * 0.54f, cw), 3, 9, seed, 17, 0.15f);
            AddBlob(buffers.GrassLight, crown + Vector3.up * (height * 0.22f) + leanDir * (height * 0.04f),
                new Vector3(cw * 0.60f, cw * 0.36f, cw * 0.60f), 3, 8, seed, 23, 0.14f);
        }

        /// <summary>
        /// 老树（岛主景）：更高的树干、4 团树冠、**根须越过崖沿垂下去**——把"树在浮岛边上"
        /// 这件事讲清楚，也让崖沿轮廓不是一刀切。
        /// </summary>
        public static void AddElderTree(IslandBuffers buffers, Vector3 basePos, float height, float yawDegrees, int seed)
        {
            if (buffers == null || height <= 0f)
                return;

            float rad = yawDegrees * Mathf.Deg2Rad;
            Vector3 leanDir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            float lean = 0.09f;

            var trunk = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                trunk[i] = basePos + Vector3.up * (height * t) + leanDir * (height * lean * t * t)
                    + Vector3.up * (height * 0.018f * SceneArtHash.SignedHash(seed, i, 3));
            }
            buffers.Wood.AddBentTube(trunk, height * 0.085f, height * 0.030f, 7);

            // 4 条根：向外铺开并在末端下垂（垂到崖外）
            for (int r = 0; r < 4; r++)
            {
                float ra = rad + Mathf.PI * 2f * r / 4f + SceneArtHash.Hash01(seed, r, 19) * 0.7f;
                Vector3 outDir = new Vector3(Mathf.Cos(ra), 0f, Mathf.Sin(ra));
                Vector3 mid = basePos + outDir * (height * 0.34f) + Vector3.up * (height * 0.02f);
                Vector3 tip = basePos + outDir * (height * (0.55f + 0.30f * SceneArtHash.Hash01(seed, r, 23)))
                    - Vector3.up * (height * (0.14f + 0.20f * SceneArtHash.Hash01(seed, r, 29)));
                buffers.Dirt.AddBentTube(new[] { basePos + Vector3.up * (height * 0.05f), mid, tip },
                    height * 0.040f, height * 0.014f, 4);
            }

            Vector3 crown = trunk[5];
            float cw = height * 0.68f;
            AddBlob(buffers.GrassDark, crown - Vector3.up * (height * 0.14f),
                new Vector3(cw * 1.00f, cw * 0.42f, cw * 1.00f), 3, 9, seed, 31, 0.17f);
            AddBlob(buffers.GrassMid, crown + Vector3.up * (height * 0.04f),
                new Vector3(cw * 1.06f, cw * 0.56f, cw * 1.02f), 4, 10, seed, 37, 0.16f);
            AddBlob(buffers.GrassMid, crown + leanDir * (cw * 0.55f) + Vector3.up * (height * 0.02f),
                new Vector3(cw * 0.66f, cw * 0.44f, cw * 0.66f), 3, 8, seed, 41, 0.15f);
            AddBlob(buffers.GrassLight, crown + Vector3.up * (height * 0.20f) - leanDir * (cw * 0.25f),
                new Vector3(cw * 0.64f, cw * 0.38f, cw * 0.62f), 3, 8, seed, 43, 0.14f);
        }

        /// <summary>垂藤：从崖沿垂下的一根微摆藤 + 3-4 片叶（崖沿的"柔软化"要素）。</summary>
        public static void AddVine(MeshBuffers b, Vector3 top, float length, float yawDegrees, int seed)
        {
            if (b == null || length <= 0f)
                return;

            float rad = yawDegrees * Mathf.Deg2Rad;
            Vector3 sway = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            var pts = new Vector3[5];
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                pts[i] = top + Vector3.down * (length * t)
                    + sway * (length * 0.14f * Mathf.Sin(t * 3.1f + SceneArtHash.Hash01(seed, 0, 3) * 6f));
            }

            b.AddBentTube(pts, 0.045f, 0.02f, 4);

            for (int i = 1; i < 5; i++)
            {
                float ang = rad + Mathf.PI * 0.5f + SceneArtHash.SignedHash(seed, i, 7) * 1.1f;
                Vector3 leafDir = new Vector3(Mathf.Cos(ang), -0.45f, Mathf.Sin(ang));
                b.AddLeaf(pts[i], leafDir, length * 0.16f, length * 0.10f, length * 0.05f, 1);
            }
        }

        /// <summary>发光花簇：3 片草叶 + 5-7 个微小发光球（暖光/晶体两色混编）。</summary>
        public static void AddFlowerCluster(IslandBuffers buffers, Vector3 basePos, float scale, int seed)
        {
            if (buffers == null || scale <= 0f)
                return;

            AddGrassTuft(buffers.GrassMid, basePos, scale * 0.9f, seed, 3);

            int blooms = 5 + (int)(SceneArtHash.Hash01(seed, 0, 5) * 3f);
            for (int i = 0; i < blooms; i++)
            {
                float ang = Mathf.PI * 2f * i / blooms + SceneArtHash.Hash01(seed, i, 9) * 0.9f;
                float dist = scale * 0.22f * SceneArtHash.Hash01(seed, i, 11);
                float h = scale * (0.30f + 0.40f * SceneArtHash.Hash01(seed, i, 13));
                Vector3 c = basePos + new Vector3(Mathf.Cos(ang) * dist, h, Mathf.Sin(ang) * dist);
                float r = scale * (0.085f + 0.055f * SceneArtHash.Hash01(seed, i, 17));

                MeshBuffers target = SceneArtHash.Hash01(seed, i, 19) > 0.62f ? buffers.Crystal : buffers.Glow;
                AddBlob(target, c, new Vector3(r, r * 0.85f, r), 2, 5, seed + i * 7, 77, 0.20f);
            }
        }

        /// <summary>木灯柱（路径旁/瞭望台挂灯）：细木柱 + 顶部发光球 + 一盏小窗格。</summary>
        public static void AddLantern(IslandBuffers buffers, Vector3 basePos, float height, int seed)
        {
            if (buffers == null || height <= 0f)
                return;

            buffers.Wood.AddFrustum(basePos, height * 0.055f, height * 0.038f, height, 5,
                SceneArtHash.Hash01(seed, 0, 3) * 90f);
            buffers.Wood.AddBox(basePos + Vector3.up * (height + 0.03f),
                new Vector3(height * 0.22f, 0.05f, height * 0.22f), 0f);

            float r = height * 0.115f;
            AddBlob(buffers.Glow, basePos + Vector3.up * (height + 0.04f + r * 1.5f),
                new Vector3(r, r * 1.15f, r), 2, 6, seed + 11, 83, 0.16f);
        }

        // ==================================================================
        // 远景活物：鸟
        // ==================================================================

        /// <summary>低模飞鸟：双面 V 形双翼 + 小身躯（远景剪影，不参与玩法）。</summary>
        public static void AddBird(MeshBuffers b, Vector3 pos, float yawDegrees, float scale, int seed)
        {
            if (b == null || scale <= 0f)
                return;

            float rad = yawDegrees * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 side = new Vector3(-fwd.z, 0f, fwd.x);
            float flap = 0.30f + 0.55f * SceneArtHash.Hash01(seed, 0, 3);
            float span = scale * (1.6f + 0.6f * SceneArtHash.Hash01(seed, 1, 5));

            Vector3 root = pos;
            Vector3 wingTipL = pos + side * span - Vector3.up * (span * flap * 0.7f);
            Vector3 wingTipR = pos - side * span - Vector3.up * (span * flap * 0.7f);

            AddTriDouble(b, root, root + fwd * (span * 0.30f), wingTipL, Vector3.up);
            AddTriDouble(b, root, wingTipR, root + fwd * (span * 0.30f), Vector3.up);
            AddTriDouble(b, root - fwd * (span * 0.26f), root + fwd * (span * 0.26f), pos, Vector3.up);
        }
    }
}
