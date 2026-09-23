using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>
    /// 三角面缓冲（纯 C#，无头可测）：所有程序化几何先写进这里，再由 Unity 侧一次性
    /// <c>Mesh.SetVertices/SetTriangles/SetNormals</c> 落成网格资产。
    ///
    /// 【为什么拆成"缓冲区"而不是直接建 Mesh】<c>new Mesh()</c> 是 UnityEngine.Object 的实例化，
    /// 脱离 Unity 运行时走原生 ECall 会抛 <c>SecurityException</c>（见
    /// <c>external/harness/README.md</c>）。把几何算法写成纯 <c>List&lt;Vector3&gt;</c> 后，
    /// 布局/倒角/裙边这些**最容易出边界错误**的部分就能在无头验证台上断言。
    ///
    /// 【顶点不复用】每面独立输出 3 个顶点、法线为该面法线（平面着色）。低多边形块面风格要的
    /// 正是硬边平面着色；换来顶点数上升约 2-3 倍，但总三角面预算只有 ~5 万（场景文档 §8 上限 15 万），
    /// 顶点量完全不是瓶颈。
    /// </summary>
    public sealed class MeshBuffers
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<int> _triangles = new List<int>();

        /// <summary>顶点数（= 三倍三角面数，因每面独立输出）。</summary>
        public int VertexCount => _vertices.Count;

        /// <summary>三角面索引数（= 3 × 面数）。</summary>
        public int IndexCount => _triangles.Count;

        /// <summary>三角面数。</summary>
        public int TriangleCount => _triangles.Count / 3;

        /// <summary>是否为空。</summary>
        public bool IsEmpty => _triangles.Count == 0;

        /// <summary>输出用的顶点数组（拷贝）。</summary>
        public Vector3[] ToVertices() => _vertices.ToArray();

        /// <summary>输出用的法线数组（拷贝）。</summary>
        public Vector3[] ToNormals() => _normals.ToArray();

        /// <summary>输出用的三角面索引（拷贝）。</summary>
        public int[] ToTriangles() => _triangles.ToArray();

        /// <summary>把顶点/法线/索引写入调用方提供的列表（避免拷贝；Unity 侧用）。</summary>
        public void CopyTo(List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            vertices.AddRange(_vertices);
            normals.AddRange(_normals);
            triangles.AddRange(_triangles);
        }

        /// <summary>追加一个三角形（顶点按逆时针给出时法线朝观察者）。</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-16f)
                return;   // 退化三角形直接丢弃（零面积面对渲染无意义，还会污染法线）

            normal.Normalize();
            Append(a, normal);
            Append(b, normal);
            Append(c, normal);
        }

        /// <summary>
        /// 追加一个四边形（a→b→c→d 顺序环绕）。若计算出的法线与
        /// <paramref name="outwardHint"/> 反向，则翻转绕序 —— 这样调用方只要给"外法线应该朝哪"，
        /// 不必逐个手推绕序符号（本项目几何里最容易写错的地方）。
        /// </summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outwardHint)
        {
            Vector3 normal = Vector3.Cross(b - a, d - a);
            if (normal.sqrMagnitude < 1e-16f)
                return;

            normal.Normalize();
            if (Vector3.Dot(normal, outwardHint) < 0f)
            {
                // 绕序反了：整体翻面（a,d,c,b）。
                AddTriangle(a, d, c);
                AddTriangle(a, c, b);
            }
            else
            {
                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }
        }

        /// <summary>追加一个四边形（显式给绕序 a→b→c→d，法线由绕序推出）。</summary>
        public void AddQuadOrdered(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        /// <summary>
        /// 轴对齐长方体（可带 yaw 旋转与逐轴尺寸）。中心 <paramref name="center"/>、全尺寸 <paramref name="size"/>。
        /// </summary>
        public void AddBox(Vector3 center, Vector3 size, float yawDegrees = 0f)
        {
            // 注意：单位矩阵必须用 Matrix4x4.identity；new Matrix4x4() 是**全零矩阵**，
            // 会把 8 个角点全部压到原点 → 6 个面全退化 → 道具静默消失（本工程踩过一次）。
            AddBox(Matrix4x4.identity, center, size, yawDegrees);
        }

        /// <summary>长方体：先绕 <paramref name="euler"/> 旋转，再平移到 <paramref name="center"/>。</summary>
        public void AddBox(Vector3 euler, Vector3 center, Vector3 size)
        {
            Matrix4x4 matrix = SceneArtRot.Trs(center, SceneArtRot.Euler(euler.x, euler.y, euler.z), Vector3.one);
            AddBox(matrix, Vector3.zero, size, 0f);
        }

        /// <summary>在给定矩阵下追加长方体（矩阵可含旋转/平移/缩放）。</summary>
        public void AddBox(Matrix4x4 matrix, Vector3 center, Vector3 size, float yawDegrees)
        {
            Matrix4x4 local = SceneArtRot.Trs(center, SceneArtRot.Euler(0f, yawDegrees, 0f), Vector3.one);
            Matrix4x4 m = matrix * local;

            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;

            // 8 个角点（局部）。
            Vector3 p000 = m.MultiplyPoint3x4(new Vector3(-hx, -hy, -hz));
            Vector3 p100 = m.MultiplyPoint3x4(new Vector3(hx, -hy, -hz));
            Vector3 p110 = m.MultiplyPoint3x4(new Vector3(hx, hy, -hz));
            Vector3 p010 = m.MultiplyPoint3x4(new Vector3(-hx, hy, -hz));
            Vector3 p001 = m.MultiplyPoint3x4(new Vector3(-hx, -hy, hz));
            Vector3 p101 = m.MultiplyPoint3x4(new Vector3(hx, -hy, hz));
            Vector3 p111 = m.MultiplyPoint3x4(new Vector3(hx, hy, hz));
            Vector3 p011 = m.MultiplyPoint3x4(new Vector3(-hx, hy, hz));

            Vector3 up = m.MultiplyVector(Vector3.up);
            Vector3 down = -up;
            Vector3 fwd = m.MultiplyVector(Vector3.forward);
            Vector3 back = -fwd;
            Vector3 right = m.MultiplyVector(Vector3.right);
            Vector3 left = -right;

            AddQuad(p010, p110, p111, p011, up);      // 顶
            AddQuad(p001, p101, p100, p000, down);    // 底
            AddQuad(p001, p011, p111, p101, fwd);     // +Z
            AddQuad(p000, p100, p110, p010, back);    // -Z
            AddQuad(p100, p101, p111, p110, right);   // +X
            AddQuad(p000, p010, p011, p001, left);    // -X
        }

        /// <summary>
        /// 圆台/圆柱（低多边形）：底面中心 <paramref name="baseCenter"/>、底面半径
        /// <paramref name="radiusBottom"/>、顶面半径 <paramref name="radiusTop"/>、高 <paramref name="height"/>。
        /// <paramref name="segments"/> 段侧面 + 两端封盖。用于桅杆/棕榈干/火药桶/木桶。
        /// </summary>
        public void AddFrustum(Vector3 baseCenter, float radiusBottom, float radiusTop, float height,
            int segments, float yawDegrees = 0f, bool capTop = true, bool capBottom = true)
        {
            segments = Mathf.Max(3, segments);
            float yaw = yawDegrees * Mathf.Deg2Rad;

            Vector3[] bottom = new Vector3[segments];
            Vector3[] top = new Vector3[segments];

            for (int i = 0; i < segments; i++)
            {
                float a = yaw + Mathf.PI * 2f * i / segments;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                bottom[i] = baseCenter + new Vector3(cx * radiusBottom, 0f, cz * radiusBottom);
                top[i] = baseCenter + new Vector3(cx * radiusTop, height, cz * radiusTop);
            }

            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                // 侧面外法线取该段中点的径向方向。
                Vector3 mid = (bottom[i] + bottom[j] + top[i] + top[j]) * 0.25f;
                Vector3 radial = new Vector3(mid.x - baseCenter.x, 0f, mid.z - baseCenter.z);
                if (radial.sqrMagnitude < 1e-12f)
                    radial = Vector3.right;

                AddQuad(bottom[i], top[i], top[j], bottom[j], radial.normalized);
            }

            if (capTop && radiusTop > 1e-4f)
            {
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    // 环序在 yaw 递增下从上方看是顺时针，故顶盖取 (中心, j, i) 才得 +Y 法线。
                    AddTriangle(baseCenter + new Vector3(0f, height, 0f), top[j], top[i]);
                }
            }

            if (capBottom && radiusBottom > 1e-4f)
            {
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    AddTriangle(baseCenter, bottom[i], bottom[j]);
                }
            }
        }

        /// <summary>
        /// 沿折线扫出的"弯曲圆柱"（棕榈树干、缆绳）：每段一个圆台，段间用中点插值形成折角。
        /// </summary>
        /// <param name="points">折线点（至少 2 个）。</param>
        /// <param name="radiusStart">起点半径。</param>
        /// <param name="radiusEnd">终点半径。</param>
        public void AddBentTube(IReadOnlyList<Vector3> points, float radiusStart, float radiusEnd, int segments)
        {
            if (points == null || points.Count < 2)
                return;

            int n = points.Count;
            for (int i = 0; i < n - 1; i++)
            {
                float t0 = i / (float)(n - 1);
                float t1 = (i + 1) / (float)(n - 1);
                Vector3 a = points[i];
                Vector3 b = points[i + 1];
                float r0 = Mathf.Lerp(radiusStart, radiusEnd, t0);
                float r1 = Mathf.Lerp(radiusStart, radiusEnd, t1);

                // 段方向 → 构造正交基，把圆环沿段方向摆放。
                Vector3 dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-5f)
                    continue;

                dir /= len;
                Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                Vector3 side = Vector3.Cross(dir, up).normalized;
                Vector3 fwd = Vector3.Cross(dir, side).normalized;

                Vector3[] ringA = new Vector3[segments];
                Vector3[] ringB = new Vector3[segments];
                for (int s = 0; s < segments; s++)
                {
                    float ang = Mathf.PI * 2f * s / segments;
                    Vector3 offset = side * Mathf.Cos(ang) + fwd * Mathf.Sin(ang);
                    ringA[s] = a + offset * r0;
                    ringB[s] = b + offset * r1;
                }

                for (int s = 0; s < segments; s++)
                {
                    int t = (s + 1) % segments;
                    Vector3 midOffset = (ringA[s] + ringB[s]) * 0.5f - (a + b) * 0.5f;
                    AddQuad(ringA[s], ringB[s], ringB[t], ringA[t], midOffset.normalized);
                }
            }
        }

        /// <summary>
        /// 低多边形不规则石块：绕 Y 一圈不规则顶点 + 上下两个锥顶（14-16 面）。
        /// <paramref name="aspect"/> = (x 半径, y 半径, z 半径) 比例。
        /// </summary>
        public void AddRock(Vector3 center, float radius, Vector3 aspect, int seed, int segments = 7)
        {
            segments = Mathf.Max(5, segments);
            float rx = radius * aspect.x, ry = radius * aspect.y, rz = radius * aspect.z;

            Vector3[] ring = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float ang = Mathf.PI * 2f * i / segments;
                float jitter = 0.72f + 0.56f * SceneArtHash.Hash01(seed, i, 11);
                ring[i] = center + new Vector3(
                    Mathf.Cos(ang) * rx * jitter,
                    ry * (0.22f * SceneArtHash.SignedHash(seed, i, 23)),
                    Mathf.Sin(ang) * rz * jitter);
            }

            Vector3 topOffset = new Vector3(
                rx * 0.28f * SceneArtHash.SignedHash(seed, 0, 31),
                ry,
                rz * 0.28f * SceneArtHash.SignedHash(seed, 1, 37));
            Vector3 bottomOffset = new Vector3(
                rx * 0.2f * SceneArtHash.SignedHash(seed, 2, 41),
                -ry,
                rz * 0.2f * SceneArtHash.SignedHash(seed, 3, 43));

            Vector3 top = center + topOffset;
            Vector3 bottom = center + bottomOffset;

            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                Vector3 outward = new Vector3(ring[i].x - center.x, 0.35f * ry, ring[i].z - center.z);
                AddQuad(ring[i], top, ring[j], bottom, outward.sqrMagnitude > 1e-12f ? outward.normalized : Vector3.up);
            }
        }

        /// <summary>
        /// 双面叶片/帆布：沿 <paramref name="direction"/> 方向铺一段"中间隆起、末端下垂"的折面条
        /// （<paramref name="length"/> 分 <paramref name="segments"/> 段，每段 1 个四边形，正反各画一次）。
        /// </summary>
        public void AddLeaf(Vector3 basePos, Vector3 direction, float length, float width, float droop, int segments = 3)
        {
            segments = Mathf.Max(1, segments);
            Vector3 dir = direction.normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.right;
            side.Normalize();

            Vector3 prev = basePos;
            float prevW = width * 0.25f;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 next = basePos + dir * (length * t) + Vector3.down * (droop * t * t);
                float w = Mathf.Lerp(width * 0.25f, width, t) * (1f - 0.35f * t);

                Vector3 a = prev - side * prevW;
                Vector3 b = prev + side * prevW;
                Vector3 c = next + side * w * 0.5f;
                Vector3 d = next - side * w * 0.5f;

                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                AddTrianglesDoubleSided(a, b, c, d, n);

                prev = next;
                prevW = w * 0.5f;
            }
        }

        /// <summary>
        /// 双面四边形（帆/叶/旗/破帆）：正面绕序按 <paramref name="normalHint"/> 定，再叠一份反向绕序，
        /// 使 45° 俯视下正反两面都可看见（帆与叶是零厚度贴片，单面会被背面剔除吃掉）。
        /// </summary>
        public void AddTrianglesDoubleSided(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normalHint)
        {
            Vector3 n = Vector3.Cross(b - a, d - a);
            if (n.sqrMagnitude < 1e-16f)
                return;

            n.Normalize();
            bool frontIsPositive = Vector3.Dot(n, normalHint) >= 0f;

            // 正面
            if (frontIsPositive)
            {
                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }
            else
            {
                AddTriangle(a, d, c);
                AddTriangle(a, c, b);
            }

            // 反面（绕序相反）
            if (frontIsPositive)
            {
                AddTriangle(a, c, b);
                AddTriangle(a, d, c);
            }
            else
            {
                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }
        }

        /// <summary>水平圆盘（贝壳/泡沫贴片/水膜）：<paramref name="up"/> 为盘面法线。</summary>
        public void AddDisc(Vector3 center, float radius, int segments, Vector3 up, bool doubleSided = true)
        {
            segments = Mathf.Max(3, segments);
            Vector3 axis = up.normalized;
            Vector3 refDir = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 side = Vector3.Cross(axis, refDir).normalized;
            Vector3 fwd = Vector3.Cross(axis, side).normalized;

            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments;
                float a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector3 p0 = center + (side * Mathf.Cos(a0) + fwd * Mathf.Sin(a0)) * radius;
                Vector3 p1 = center + (side * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * radius;

                // 正面（法线朝 axis）。
                if (Vector3.Dot(Vector3.Cross(p0 - center, p1 - center), axis) < 0f)
                    AddTriangle(center, p1, p0);
                else
                    AddTriangle(center, p0, p1);

                // 反面（法线朝 -axis）：双面贴片在 45° 俯视下两面都可能被看到。
                if (doubleSided)
                {
                    if (Vector3.Dot(Vector3.Cross(p0 - center, p1 - center), axis) < 0f)
                        AddTriangle(center, p0, p1);
                    else
                        AddTriangle(center, p1, p0);
                }
            }
        }

        /// <summary>细长圆柱（缆绳/缆索/链条的直线段）：两点之间一个低段数圆台。</summary>
        public void AddRod(Vector3 from, Vector3 to, float radius, int segments = 5)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 1e-5f)
                return;

            AddBentTube(new[] { from, to }, radius, radius, segments);
        }

        /// <summary>
        /// 把另一个缓冲区的三角面经 <paramref name="matrix"/> 变换后追加进来。
        /// 用途：把"零件"先在局部坐标里搭好（桅杆沿 +Y、船体沿 +X），再整体侧倾/平移/转向，
        /// 避免在每个生成函数里手推旋转变换。
        /// </summary>
        public void AppendTransformed(MeshBuffers source, Matrix4x4 matrix)
        {
            if (source == null || source.IsEmpty)
                return;

            for (int i = 0; i < source._triangles.Count; i++)
            {
                int index = source._triangles[i];
                Vector3 position = matrix.MultiplyPoint3x4(source._vertices[index]);
                Vector3 normal = matrix.MultiplyVector(source._normals[index]);
                if (normal.sqrMagnitude < 1e-12f)
                    normal = Vector3.up;
                else
                    normal.Normalize();

                Append(position, normal);
            }
        }

        void Append(Vector3 position, Vector3 normal)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _triangles.Add(_triangles.Count);
        }
    }
}
