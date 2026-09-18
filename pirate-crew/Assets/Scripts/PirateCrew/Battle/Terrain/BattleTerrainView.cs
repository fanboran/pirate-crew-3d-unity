using System;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 瓦片地形的场景视图（MonoBehaviour 薄壳）：把纯 C# 的 <see cref="TileTerrainGrid"/>
    /// 落成**隐形碰撞层**——每实心格一个 Cube + BoxCollider（Renderer 关闭），是单位与弹体的
    /// 物理地面。视觉层不在本类：样板三关外观由烘焙 prefab（<c>RuntimeSceneArt</c>）承担，
    /// 世界图外观由 <c>WorldMapComposer</c> 的 kit 件与站面承担。
    ///
    /// 【一代视觉壳为何删除（2026-09-19，管线合并阶段 D）】旧"台地壳/潮沟/平台底部"路径由
    /// <c>IslandShellGeometry</c> 在运行时逐格生成合并网格——该几何生成器已随糖豆人式资产架构
    /// 改岗为编辑器烘焙器（<c>Assets/Editor/SceneArtBaker.cs</c>），运行时程序集不再含几何生成代码，
    /// 本类的视觉路径随之退役（<c>Render</c>/<c>ApplyDestruction</c> 退役前已无调用方——
    /// 一代退场后战斗只有世界图与样板三关两条路，都不建格子渲染层）。
    ///
    /// 【接线】由 <c>M2BattleSceneSetup</c> 在场景里创建并接好 <c>blockRoot</c>；
    /// 运行时由 <c>BattleController.BuildTerrain</c> 调 <see cref="RenderCollidersOnly"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTerrainView : MonoBehaviour
    {
        [Header("组装引用（场景内直连）")]
        [Tooltip("地形块父节点；为空时用本物体 transform。")]
        [SerializeField] Transform blockRoot;

        // 运行时的纯 C# 网格（非 UnityEngine.Object，不进 Inspector；由 BattleController 注入）。
        TileTerrainGrid grid;

        GameObject[] _cellObjects = new GameObject[0];

        int _version;

        /// <summary>当前地形网格；未渲染前为 null。</summary>
        public TileTerrainGrid Grid => grid;

        /// <summary>地形变化计数（小地图等只在变化时刷新）。</summary>
        public int Version => _version;

        /// <summary>地形发生变化（初次渲染/重建）时触发。同场景内直接订阅，不走 EventBus。</summary>
        public event Action Changed;

        /// <summary>已建出的碰撞地形块数量（调试/测试用）。</summary>
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
        /// 只建**碰撞层**（每实心格一个隐形 Cube+BoxCollider，单位与弹体的物理地面），
        /// 不建视觉——外观由烘焙件承担（样板三关玩家看不到任何格子）。幂等：先清旧物。
        /// </summary>
        public void RenderCollidersOnly(TileTerrainGrid terrainGrid)
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

                    _cellObjects[index] = CreateCollisionBlock(root, index);
                }
            }

            Bump();
        }

        // ------------------------------------------------------------------
        // 碰撞层
        // ------------------------------------------------------------------

        /// <summary>
        /// 碰撞块：Cube + BoxCollider（单位的物理地面），**Renderer 关闭**——外观交给烘焙件。
        /// </summary>
        GameObject CreateCollisionBlock(Transform root, int index)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TerrainCollision_" + grid.CellXOf(index) + "_" + grid.CellYOf(index);
            go.transform.SetParent(root, false);

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = false;

            ApplyBlockTransform(go, index);
            return go;
        }

        /// <summary>按当前块数把碰撞立方体放到「底面贴地、顶面 = 地表」的尺寸/位置。</summary>
        void ApplyBlockTransform(GameObject go, int index)
        {
            int gx = grid.CellXOf(index);
            int gy = grid.CellYOf(index);
            float height = grid.BlocksAt(gx, gy) * grid.BlockWorldHeight;
            if (height < 0.01f)
                height = 0.01f;   // 防御：0 高度会让 PhysX 产生退化碰撞体

            // 1 格 = LevelGeometry.TileWorldSize 世界单位。
            float size = LevelGeometry.TileWorldSize;
            go.transform.localScale = new Vector3(size, height, size);
            Vector2 center = LevelGeometry.TileCenterWorld(gx, gy);
            go.transform.localPosition = new Vector3(
                center.x,
                LevelGeometry.GroundTopY + height * 0.5f,
                center.y);
        }

        void OnDestroy()
        {
            ClearAll();
        }

        void ClearAll()
        {
            for (int i = 0; i < _cellObjects.Length; i++)
            {
                if (_cellObjects[i] != null)
                    DestroyUnityObject(_cellObjects[i]);
            }

            _cellObjects = new GameObject[0];
        }

        /// <summary>
        /// 运行时用 Object.Destroy、编辑器（EditMode 测试 / 装配期重建）用 Object.DestroyImmediate
        /// （模式照抄 <c>Ambient/AmbientLibrary</c>）。
        /// </summary>
        static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }

        void Bump()
        {
            _version++;
            Changed?.Invoke();
        }
    }
}
