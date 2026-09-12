using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 瓦片地形的场景视图（MonoBehaviour 薄壳）：把纯 C# 的 <see cref="TileTerrainGrid"/> 渲染成
    /// 一组带碰撞体的立方块，并在爆炸破坏后同步更新。
    ///
    /// 【分层】高度/破坏/归一化等全部规则在 <see cref="TileTerrainGrid"/> 与
    ///   <see cref="TerrainCatalog"/>（纯 C#，可无头测试）；本类只做「格 → GameObject」的实例化与刷新，
    ///   不含任何数值推导。
    ///
    /// 【为什么每格一个立方体而不是逐块堆叠】每格最多 8 块，若逐块建物体会让 level_1 出现近 3000 个
    ///   GameObject；改用「每格一个按高度缩放的立方体」（顶面 = 该格地表），碰撞与视觉等价，
    ///   破坏时只改这一个物体的高度。块数仍由 <see cref="TileTerrainGrid"/> 记账。
    ///
    /// 【接线】由 <c>M2BattleSceneSetup</c> 在场景里创建并接好 <c>blockRoot</c> / <c>blockMaterial</c>；
    ///   运行时由 <c>BattleController.Awake</c> 调 <see cref="Render"/>，爆炸时调 <see cref="ApplyDestruction"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTerrainView : MonoBehaviour
    {
        [Header("组装引用（场景内直连）")]
        [Tooltip("地形块父节点；为空时用本物体 transform。")]
        [SerializeField] Transform blockRoot;

        [Tooltip("地形块材质；为空时运行时用 URP/Lit 建一个兜底材质。")]
        [SerializeField] Material blockMaterial;

        // 运行时的纯 C# 网格（非 UnityEngine.Object，不进 Inspector；由 BattleController 注入）。
        TileTerrainGrid grid;

        GameObject[] _cellObjects = new GameObject[0];
        Material _fallbackMaterial;
        int _version;

        /// <summary>当前地形网格；未 <see cref="Render"/> 前为 null。</summary>
        public TileTerrainGrid Grid => grid;

        /// <summary>地形变化计数（小地图等只在变化时刷新）。</summary>
        public int Version => _version;

        /// <summary>地形发生变化（初次渲染 / 破坏后）时触发。同场景内直接订阅，不走 EventBus。</summary>
        public event Action Changed;

        /// <summary>已建出的实心地形块数量（调试/测试用）。</summary>
        public int BlockObjectCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cellObjects.Length; i++)
                {
                    if (_cellObjects[i] != null)
                        n++;
                }
                return n;
            }
        }

        /// <summary>该格当前堆叠块数（小地图点阵用）；无网格时返回 0。</summary>
        public int BlocksAtCell(int cellIndex)
        {
            if (grid == null || cellIndex < 0 || cellIndex >= grid.WidthTiles * grid.DepthTiles)
                return 0;
            return grid.BlocksAt(grid.CellXOf(cellIndex), grid.CellYOf(cellIndex));
        }

        /// <summary>
        /// 按网格重建全部地形块（幂等：先清旧块）。由 <c>BattleController.Awake</c> 调用。
        /// </summary>
        public void Render(TileTerrainGrid terrainGrid)
        {
            grid = terrainGrid;
            ClearAll();

            if (grid == null)
            {
                Bump();
                return;
            }

            Transform root = blockRoot != null ? blockRoot : transform;
            _cellObjects = new GameObject[grid.WidthTiles * grid.DepthTiles];

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    int index = gx + gy * grid.WidthTiles;
                    if (grid.BlocksAt(gx, gy) <= 0)
                        continue;

                    _cellObjects[index] = CreateBlock(root, index);
                }
            }

            Bump();
        }

        /// <summary>
        /// 爆炸破坏后刷新被摧毁的格（高度归零 → 移除块）。
        /// </summary>
        public void ApplyDestruction(IReadOnlyList<int> cellIndices)
        {
            if (grid == null || cellIndices == null || cellIndices.Count == 0)
                return;

            Transform root = blockRoot != null ? blockRoot : transform;
            bool any = false;

            for (int i = 0; i < cellIndices.Count; i++)
            {
                int index = cellIndices[i];
                if (index < 0 || index >= _cellObjects.Length)
                    continue;

                any = true;
                GameObject existing = _cellObjects[index];

                if (grid.BlocksAt(grid.CellXOf(index), grid.CellYOf(index)) <= 0)
                {
                    // 整格已摧毁：移除物体（回到基础地面）。
                    if (existing != null)
                        Destroy(existing);
                    _cellObjects[index] = null;
                }
                else
                {
                    // 逐块递减：复用/重建该格的块并改高度。
                    if (existing == null)
                    {
                        existing = CreateBlock(root, index);
                        _cellObjects[index] = existing;
                    }
                    else
                    {
                        ApplyBlockTransform(existing, index);
                    }
                }
            }

            if (any)
                Bump();
        }

        void ClearAll()
        {
            for (int i = 0; i < _cellObjects.Length; i++)
            {
                if (_cellObjects[i] != null)
                    Destroy(_cellObjects[i]);
            }

            _cellObjects = new GameObject[0];
        }

        GameObject CreateBlock(Transform root, int index)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TerrainCell_" + grid.CellXOf(index) + "_" + grid.CellYOf(index);
            go.transform.SetParent(root, false);

            // 方块自身附带 BoxCollider（PhysX 靠它做墙/地面接触），保留。
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = ResolveMaterial();

            ApplyBlockTransform(go, index);
            return go;
        }

        /// <summary>按当前块数把每格立方体放到「底面贴地、顶面 = 地表」的尺寸/位置。</summary>
        void ApplyBlockTransform(GameObject go, int index)
        {
            int gx = grid.CellXOf(index);
            int gy = grid.CellYOf(index);
            float height = grid.BlocksAt(gx, gy) * grid.BlockWorldHeight;
            if (height < 0.01f)
                height = 0.01f;   // 防御：0 高度会让 PhysX 产生退化碰撞体

            go.transform.localScale = new Vector3(1f, height, 1f);
            go.transform.localPosition = new Vector3(
                gx + 0.5f,
                LevelGeometry.GroundTopY + height * 0.5f,
                gy + 0.5f);
        }

        Material ResolveMaterial()
        {
            if (blockMaterial != null)
                return blockMaterial;

            if (_fallbackMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                _fallbackMaterial = new Material(shader) { name = "TerrainFallback" };

                // 与 URP/Lit / Standard 两种属性名都兼容。
                Color sand = new Color(0.62f, 0.52f, 0.38f, 1f);
                if (_fallbackMaterial.HasProperty("_BaseColor"))
                    _fallbackMaterial.SetColor("_BaseColor", sand);
                if (_fallbackMaterial.HasProperty("_Color"))
                    _fallbackMaterial.SetColor("_Color", sand);
            }

            return _fallbackMaterial;
        }

        void Bump()
        {
            _version++;
            Changed?.Invoke();
        }
    }
}
