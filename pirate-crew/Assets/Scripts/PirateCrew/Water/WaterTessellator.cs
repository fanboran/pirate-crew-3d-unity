using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Water
{
    /// <summary>
    /// 把水面的低模网格（工程默认是 <c>PrimitiveType.Cube</c>，顶面仅 4 个角点）
    /// 替换成一张 XZ 细分平面网格，让 <c>PirateWater.shader</c> 的 Gerstner 顶点位移
    /// 真的能画出波峰起伏。
    ///
    /// 【接线】必须挂在名为 <c>Water</c> 的物体上（编辑器里加一个组件即可；
    /// 协调者在 <c>BattleSceneSetup.CreateWaterPlane</c> 里补一行 <c>AddComponent</c> 最省事）。
    /// **不挂也能跑**：shader 的解析法线是逐像素算的，不依赖网格密度；只是浪的"几何起伏/轮廓"
    /// 会退化成一块平板。
    ///
    /// 【局部坐标】生成的网格只在 Cube 顶面（局部 y = +0.5），x/z ∈ [−0.5, 0.5]；
    /// 物体会带着自己的 localScale（水是 (宽, 0.1, 深)），所以：
    ///   · 格数按 **lossyScale** 反推，保证世界空间格距 ≈ <see cref="targetCellSize"/>；
    ///   · 世界 Y 的顶点位移在 shader 里发生（对象空间位移会被 0.1 的 Y 缩放吃掉 90%，
    ///     这是原 shader 已经踩过的坑）。
    /// 丢掉 Cube 的四个侧壁不影响观感（水厚 0.1，45° 俯视看不到侧面），还能省一半三角面。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterTessellator : MonoBehaviour
    {
        [Tooltip("目标格距（世界单位）。默认 1.6（格 1→2 单位 ×2）：最短波长 4.8 下每波约 3 格。")]
        [SerializeField] float targetCellSize = WaterMeshRules.DefaultCellSize;

        [Tooltip("每轴格数上限（防止把水做太大时网格爆炸）。")]
        [SerializeField] int maxCellsPerAxis = WaterMeshRules.DefaultMaxCellsPerAxis;

        Mesh _mesh;

        /// <summary>当前网格的三角面数（调试/报告用）。</summary>
        public int TriangleCount { get; private set; }

        /// <summary>当前网格的格数（调试/报告用）。</summary>
        public int CellsX { get; private set; }

        /// <summary>当前网格的格数（调试/报告用）。</summary>
        public int CellsZ { get; private set; }

        void Awake()
        {
            Rebuild();
        }

        /// <summary>按当前 lossyScale 重建网格。幂等（重复调用覆盖同一 Mesh）。</summary>
        public void Rebuild()
        {
            Vector3 scale = transform.lossyScale;
            float worldX = Mathf.Abs(scale.x);
            float worldZ = Mathf.Abs(scale.z);

            int cx = WaterMeshRules.CellCount(worldX, targetCellSize, maxCellsPerAxis);
            int cz = WaterMeshRules.CellCount(worldZ, targetCellSize, maxCellsPerAxis);
            CellsX = cx;
            CellsZ = cz;

            int vertexCount = WaterMeshRules.VertexCount(cx, cz);
            var vertices = new List<Vector3>(vertexCount);
            var uvs = new List<Vector2>(vertexCount);
            var triangles = new List<int>(WaterMeshRules.IndexCount(cx, cz));

            // 局部坐标：Cube 顶面 y=+0.5，x/z ∈ [−0.5, 0.5]。
            for (int z = 0; z <= cz; z++)
            {
                float tz = (float)z / cz;
                for (int x = 0; x <= cx; x++)
                {
                    float tx = (float)x / cx;
                    vertices.Add(new Vector3(tx - 0.5f, 0.5f, tz - 0.5f));
                    uvs.Add(new Vector2(tx, tz));
                }
            }

            int stride = cx + 1;
            for (int z = 0; z < cz; z++)
            {
                for (int x = 0; x < cx; x++)
                {
                    int a = z * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;

                    // 顶面朝 +Y：从上方看逆时针为 (a, c, d) / (a, d, b)。
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(b);
                }
            }

            if (_mesh == null)
                _mesh = new Mesh { name = "WaterSurfaceGrid" };

            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            var filter = GetComponent<MeshFilter>();
            if (filter != null)
                filter.mesh = _mesh;

            TriangleCount = triangles.Count / 3;
        }
    }
}
