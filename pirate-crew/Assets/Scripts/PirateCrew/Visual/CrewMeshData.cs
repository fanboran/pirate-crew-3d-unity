using UnityEngine;

namespace PirateCrew.Visual
{
    /// <summary>
    /// 程序化网格的**纯 C# 数据载体**（顶点 / 法线 / UV / 三角面索引）。
    ///
    /// 【为什么单独抽一层】Unity 的 <see cref="Mesh"/> 是原生对象（ECall），
    /// 在无头验证台（external/harness）里 <c>new Mesh()</c> 会抛 SecurityException；
    /// 因此几何生成全部返回本结构，只有 <see cref="CrewMeshFactory.CreateMesh"/> 这一步
    /// 才依赖 Unity 运行时。这样"顶点数 / 包围盒 / 法线单位化 / 无退化三角"都能无头断言
    /// （见 <c>Assets/Tests/Visual/CrewMeshFactoryTests.cs</c>）。
    ///
    /// 【口径】所有生成器都在**建模空间**（spec space）产几何：脚底 y=0、单位总高约 0.5
    /// （见 docs/角色造型规范.md §1.2）。装配时靠 Transform 摆位，不在网格里烘坐标。
    /// </summary>
    public struct MeshData
    {
        /// <summary>顶点位置（建模空间，1 单位 = 32px）。</summary>
        public Vector3[] Vertices;

        /// <summary>顶点法线（单位向量）。</summary>
        public Vector3[] Normals;

        /// <summary>顶点 UV0。</summary>
        public Vector2[] Uvs;

        /// <summary>三角面索引（每 3 个一组，顺时针面向观察者的约定由生成器保证朝外）。</summary>
        public int[] Triangles;

        public MeshData(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
        {
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
            Triangles = triangles;
        }

        /// <summary>顶点数。</summary>
        public int VertexCount => Vertices == null ? 0 : Vertices.Length;

        /// <summary>三角面数（= 索引数 / 3）。</summary>
        public int TriangleCount => Triangles == null ? 0 : Triangles.Length / 3;

        /// <summary>是否为空（无顶点或无三角面）。</summary>
        public bool IsEmpty => VertexCount == 0 || TriangleCount == 0;

        /// <summary>空网格。</summary>
        public static MeshData Empty =>
            new MeshData(new Vector3[0], new Vector3[0], new Vector2[0], new int[0]);

        /// <summary>
        /// 把多个网格合并成一个（顶点数组直接拼接，三角面索引带偏移）。
        /// 用于胡须球簇 / 三角帽（帽冠 + 三片翻檐）这类"一个部件由多段几何拼成"的情况。
        /// </summary>
        public static MeshData Combine(params MeshData[] parts)
        {
            if (parts == null || parts.Length == 0)
                return Empty;

            int vertexCount = 0;
            int indexCount = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                vertexCount += parts[i].VertexCount;
                indexCount += parts[i].Triangles == null ? 0 : parts[i].Triangles.Length;
            }

            if (vertexCount == 0 || indexCount == 0)
                return Empty;

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var triangles = new int[indexCount];

            int vOffset = 0;
            int iOffset = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                MeshData part = parts[i];
                int vCount = part.VertexCount;
                if (vCount == 0)
                    continue;

                System.Array.Copy(part.Vertices, 0, vertices, vOffset, vCount);
                System.Array.Copy(part.Normals, 0, normals, vOffset, vCount);
                System.Array.Copy(part.Uvs, 0, uvs, vOffset, vCount);

                int[] src = part.Triangles;
                for (int t = 0; t < src.Length; t++)
                    triangles[iOffset + t] = src[t] + vOffset;

                vOffset += vCount;
                iOffset += src.Length;
            }

            return new MeshData(vertices, normals, uvs, triangles);
        }
    }
}
