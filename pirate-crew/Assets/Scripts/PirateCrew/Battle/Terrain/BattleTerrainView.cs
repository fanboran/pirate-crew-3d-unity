using System;
using System.Collections.Generic;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 瓦片地形的场景视图（MonoBehaviour 薄壳）：把纯 C# 的 <see cref="TileTerrainGrid"/> 渲染成
    /// **有倒角/岩层/裙边的海岛地块壳**，并在爆炸破坏后同步更新。
    ///
    /// 【分层（视觉层与碰撞层解耦，方案见 <c>docs/场景设计-战斗竞技场.md</c> §3.1/§9.1）】
    ///   · 碰撞层：每实心格一个 Cube + BoxCollider，**Renderer 关闭**（不可见但仍是单位的物理地面）；
    ///   · 视觉层：由 <see cref="IslandShellGeometry"/> 生成的"台地壳"——顶面与该格
    ///     <see cref="TileTerrainGrid.SurfaceWorldY"/> 严格等高（偏差 ≤ ±0.02）、边缘 0.15 宽 45° 倒角、
    ///     侧面 3 段岩层、同列沿 Z 的剪影扰动、边界台地外侧下延成裙边（到 y=-0.6）；
    ///     0 块列（原版水道列）另铺一层湿沙"潮沟"贴片。
    ///   · 高度/破坏/归一化等规则全部仍在 <see cref="TileTerrainGrid"/> 与
    ///     <see cref="TerrainCatalog"/>（纯 C#，可无头测试）；本类不含任何数值推导。
    ///
    /// 【为什么整块合成一个网格】每关实心格约 700 个，若每格一个 Renderer 就是约 700 个 DrawCall
    ///   （预算见场景文档 §8：≤250）。故把全部格合成 **1 个网格**（同类共材质），
    ///   破坏时整块重建（爆炸是回合制下的低频事件，重建约几毫秒，可接受）。
    ///   代价：失去逐格视锥剔除（合并网格只有一个包围盒）。三角面总量约 3 万，桌面 1080p 无压力。
    ///
    /// 【接线】由 <c>M2BattleSceneSetup</c> 在场景里创建并接好 <c>blockRoot</c> / <c>blockMaterial</c>；
    ///   运行时由 <c>BattleController.Awake</c> 调 <see cref="Render"/>，爆炸时调 <see cref="ApplyDestruction"/>。
    ///   <c>wetMaterial</c> 由 <c>SceneArtBuilder</c> 可选接管（潮沟湿沙），为空时回落 <c>blockMaterial</c>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTerrainView : MonoBehaviour
    {
        [Header("组装引用（场景内直连）")]
        [Tooltip("地形块父节点；为空时用本物体 transform。")]
        [SerializeField] Transform blockRoot;

        [Tooltip("地形块材质；为空时运行时用 URP/Lit 建一个兜底材质。视觉壳与碰撞块共用。")]
        [SerializeField] Material blockMaterial;

        [Tooltip("潮沟（0 块列）湿沙材质；为空时回落 blockMaterial。由 SceneArtBuilder 可选接线。")]
        [SerializeField] Material wetMaterial;

        [Tooltip("是否用海岛地块壳替换方块外观（关闭则回到「每格一个可见立方体」）。")]
        [SerializeField] bool enableVisualShell = true;

        [Tooltip("是否由运行时视图生成平台底部（船体/岩锥）。场景美术已在构建期生成底部时应关闭，避免重复几何。")]
        [SerializeField] bool buildUnderside = true;

        // 运行时的纯 C# 网格（非 UnityEngine.Object，不进 Inspector；由 BattleController 注入）。
        TileTerrainGrid grid;

        GameObject[] _cellObjects = new GameObject[0];
        Material _fallbackMaterial;

        // 视觉壳（合并网格）：整块地形 1 个 MeshRenderer，破坏时整块重建。
        GameObject _shellObject;
        MeshFilter _shellFilter;
        MeshRenderer _shellRenderer;
        Mesh _shellMesh;

        // 潮沟湿沙贴片（0 块列）。
        GameObject _lowZoneObject;
        MeshFilter _lowZoneFilter;
        MeshRenderer _lowZoneRenderer;
        Mesh _lowZoneMesh;

        // 悬空平台底部（船体侧板+龙骨 / 岩锥 / 岩层）：单个合并网格。
        GameObject _underShellObject;
        MeshFilter _underShellFilter;
        MeshRenderer _underShellRenderer;
        Mesh _underShellMesh;

        // 复用缓冲，避免每次破坏都产生大数组垃圾。
        readonly List<Vector3> _vertexScratch = new List<Vector3>(60000);
        readonly List<Vector3> _normalScratch = new List<Vector3>(60000);
        readonly List<int> _indexScratch = new List<int>(120000);

        int _version;

        /// <summary>视觉壳参数（= 场景文档 §3.1 的取值；见 <see cref="IslandShellSettings.Default"/>）。</summary>
        static IslandShellSettings ShellSettings => IslandShellSettings.Default;

        /// <summary>当前地形网格；未 <see cref="Render"/> 前为 null。</summary>
        public TileTerrainGrid Grid => grid;

        /// <summary>地形变化计数（小地图等只在变化时刷新）。</summary>
        public int Version => _version;

        /// <summary>地形发生变化（初次渲染 / 破坏后）时触发。同场景内直接订阅，不走 EventBus。</summary>
        public event Action Changed;

        /// <summary>已建出的碰撞地形块数量（调试/测试用）；视觉壳不计入。</summary>
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

        /// <summary>视觉壳的三角面数（报告/性能自证用；未建壳时为 0）。</summary>
        public int ShellTriangleCount { get; private set; }

        /// <summary>潮沟贴片的三角面数。</summary>
        public int LowZoneTriangleCount { get; private set; }

        /// <summary>悬空平台底部的三角面数（船体/岩锥/岩层）。</summary>
        public int UndersideTriangleCount { get; private set; }

        /// <summary>该格当前堆叠块数（小地图点阵用）；无网格时返回 0。</summary>
        public int BlocksAtCell(int cellIndex)
        {
            if (grid == null || cellIndex < 0 || cellIndex >= grid.WidthTiles * grid.DepthTiles)
                return 0;
            return grid.BlocksAt(grid.CellXOf(cellIndex), grid.CellYOf(cellIndex));
        }

        /// <summary>
        /// 按网格重建全部地形（幂等：先清旧物）。由 <c>BattleController.Awake</c> 调用。
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

                    _cellObjects[index] = CreateCollisionBlock(root, index);
                }
            }

            if (enableVisualShell)
                RebuildVisualShell(root);

            Bump();
        }

        /// <summary>
        /// 样板三关模式：只建**碰撞层**（每实心格一个隐形 Cube+BoxCollider，单位与弹体的物理地面），
        /// 不建格子视觉壳——外观全部由 ShowcaseLevels 的自由几何承担（玩家看不到任何格子）。
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

        /// <summary>
        /// 爆炸破坏后刷新被摧毁的格（高度归零 → 移除碰撞块并重建视觉壳）。
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
                    // 整格已摧毁：移除碰撞块（回到基础地面）。
                    if (existing != null)
                        Destroy(existing);
                    _cellObjects[index] = null;
                }
                else
                {
                    // 逐块递减：复用/重建该格的碰撞块并改高度。
                    if (existing == null)
                    {
                        existing = CreateCollisionBlock(root, index);
                        _cellObjects[index] = existing;
                    }
                    else
                    {
                        ApplyBlockTransform(existing, index);
                    }
                }
            }

            if (!any)
                return;

            // 视觉壳随破坏同步降高/消失（场景文档 §9.1 的硬要求）。
            if (enableVisualShell)
                RebuildVisualShell(root);

            Bump();
        }

        // ------------------------------------------------------------------
        // 碰撞层
        // ------------------------------------------------------------------

        /// <summary>
        /// 碰撞块：仍是 Cube + BoxCollider（单位的物理地面，<c>BattleController.cs:253-257</c> 依赖它），
        /// 但 **Renderer 关闭** —— 外观交给视觉壳。
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

            // 1 格 = LevelGeometry.TileWorldSize 世界单位（格 1→2 单位后碰撞块边长随之放大，与视觉壳同口径）。
            float size = LevelGeometry.TileWorldSize;
            go.transform.localScale = new Vector3(size, height, size);
            Vector2 center = LevelGeometry.TileCenterWorld(gx, gy);
            go.transform.localPosition = new Vector3(
                center.x,
                LevelGeometry.GroundTopY + height * 0.5f,
                center.y);
        }

        // ------------------------------------------------------------------
        // 视觉层（海岛地块壳）
        // ------------------------------------------------------------------

        void RebuildVisualShell(Transform root)
        {
            // ---- 实心格：台地壳 ----
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, ShellSettings);
            ApplyBuffers(EnsureShellMesh(root), _shellRenderer, shell, ResolveShellMaterial());
            ShellTriangleCount = shell.TriangleCount;
            ApplyShellShaderTuning();

            // ---- 0 块列：湿沙潮沟贴片（平台化后仅列式旧地形的平地面格） ----
            MeshBuffers low = IslandShellGeometry.BuildLowZone(grid, ShellSettings.LowPlateYOffset);
            ApplyBuffers(EnsureLowZoneMesh(root), _lowZoneRenderer, low, ResolveWetMaterial());
            LowZoneTriangleCount = low.TriangleCount;

            // ---- 悬空平台底部：船体 / 岩锥 / 岩层（场景美术已生成时由 buildUnderside 关闭） ----
            if (buildUnderside)
            {
                var under = new MeshBuffers();
                IslandShellGeometry.AddPlatformUnderside(under, grid, ShellSettings);
                // 目标渲染器必须显式传：旧签名 isShell:false 会把材质写到 lowZone，
                // underside 渲染器保持空材质 → 播放器构建里落到 URP 默认材质（构建缺席）
                // → error 洋红（r3 岸线连续洋红带根因，98.5% 像素归因见 external/harness-magenta/）。
                ApplyBuffers(EnsureUnderShellMesh(root), _underShellRenderer, under, ResolveShellMaterial());
                UndersideTriangleCount = under.TriangleCount;
            }
            else
            {
                UndersideTriangleCount = 0;
            }
        }

        Mesh EnsureUnderShellMesh(Transform root)
        {
            if (_underShellObject == null)
            {
                _underShellObject = new GameObject("TerrainUnderside");
                _underShellObject.transform.SetParent(root, false);
                _underShellFilter = _underShellObject.AddComponent<MeshFilter>();
                _underShellRenderer = _underShellObject.AddComponent<MeshRenderer>();
                _underShellMesh = new Mesh { name = "TerrainUndersideMesh" };
                _underShellMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _underShellFilter.sharedMesh = _underShellMesh;

                _underShellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _underShellRenderer.receiveShadows = true;
                _underShellRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }

            return _underShellMesh;
        }

        Mesh EnsureShellMesh(Transform root)
        {
            if (_shellObject == null)
            {
                _shellObject = new GameObject("TerrainShell");
                _shellObject.transform.SetParent(root, false);
                _shellFilter = _shellObject.AddComponent<MeshFilter>();
                _shellRenderer = _shellObject.AddComponent<MeshRenderer>();
                _shellMesh = new Mesh { name = "TerrainShellMesh" };
                _shellMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _shellFilter.sharedMesh = _shellMesh;

                // 只受主光投影（场景文档 §8：全场唯一投影光源）。
                _shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _shellRenderer.receiveShadows = true;
                _shellRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }

            return _shellMesh;
        }

        Mesh EnsureLowZoneMesh(Transform root)
        {
            if (_lowZoneObject == null)
            {
                _lowZoneObject = new GameObject("TerrainLowZone");
                _lowZoneObject.transform.SetParent(root, false);
                _lowZoneFilter = _lowZoneObject.AddComponent<MeshFilter>();
                _lowZoneRenderer = _lowZoneObject.AddComponent<MeshRenderer>();
                _lowZoneMesh = new Mesh { name = "TerrainLowZoneMesh" };
                _lowZoneMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _lowZoneFilter.sharedMesh = _lowZoneMesh;

                // 潮沟是"刚退潮的沙洼"：不投影（否则会在自己身上打出一层脏影）。
                _lowZoneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _lowZoneRenderer.receiveShadows = true;
            }

            return _lowZoneMesh;
        }

        void ApplyBuffers(Mesh mesh, MeshRenderer target, MeshBuffers buffers, Material material)
        {
            _vertexScratch.Clear();
            _normalScratch.Clear();
            _indexScratch.Clear();
            buffers.CopyTo(_vertexScratch, _normalScratch, _indexScratch);

            mesh.Clear(false);
            if (_vertexScratch.Count > 0)
            {
                mesh.SetVertices(_vertexScratch);
                mesh.SetNormals(_normalScratch);
                mesh.SetTriangles(_indexScratch, 0, true);
            }

            mesh.RecalculateBounds();

            // 空材质 = 播放器构建里的粉色 error 方块（URP 默认材质不保证入构建），
            // 材质解析失败宁可告警并隐藏，绝不留空。
            if (target != null)
            {
                if (material != null)
                {
                    target.sharedMaterial = material;
                }
                else
                {
                    target.enabled = false;
                    Debug.LogWarning("[BattleTerrainView] 材质解析失败，" + target.name + " 已隐藏（粉色方块防线）。");
                }
            }
        }

        /// <summary>
        /// 用 MaterialPropertyBlock（不改渲染波次生成的材质资产）把地形高度混合阈值调到
        /// 与场景文档 §3.1 的高度分层表一致：1-2 块 = 干沙、3-5 块 = 岩沙过渡、6-8 块 = 礁岩。
        /// **提案**：Environment 里的 <c>Terrain_Island</c> 材质默认 <c>_HeightSandGrass=0.6</c>、
        /// <c>_HeightGrassRock=3.0</c>（那是渲染波次按"低平台"设的），按本关高度分布会整片变草；
        /// 这里覆盖为 0.85 / 1.35，使 y≥1.5（6 块以上）读作岩、1 块读作沙。
        /// 覆盖只作用于本 Renderer，材质资产本身不动。
        /// </summary>
        void ApplyShellShaderTuning()
        {
            if (_shellRenderer == null)
                return;

            var mpb = new MaterialPropertyBlock();
            _shellRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat("_HeightSandGrass", 0.85f);
            mpb.SetFloat("_HeightGrassRock", 1.35f);
            _shellRenderer.SetPropertyBlock(mpb);
        }

        void ClearAll()
        {
            for (int i = 0; i < _cellObjects.Length; i++)
            {
                if (_cellObjects[i] != null)
                    Destroy(_cellObjects[i]);
            }

            _cellObjects = new GameObject[0];
            ShellTriangleCount = 0;
            LowZoneTriangleCount = 0;
            UndersideTriangleCount = 0;

            if (_shellObject != null)
            {
                Destroy(_shellObject);
                _shellObject = null;
                _shellFilter = null;
                _shellRenderer = null;
                _shellMesh = null;
            }

            if (_lowZoneObject != null)
            {
                Destroy(_lowZoneObject);
                _lowZoneObject = null;
                _lowZoneFilter = null;
                _lowZoneRenderer = null;
                _lowZoneMesh = null;
            }

            if (_underShellObject != null)
            {
                Destroy(_underShellObject);
                _underShellObject = null;
                _underShellFilter = null;
                _underShellRenderer = null;
                _underShellMesh = null;
            }
        }

        Material ResolveShellMaterial()
        {
            if (blockMaterial != null)
                return blockMaterial;

            return ResolveFallbackMaterial();
        }

        Material ResolveWetMaterial()
        {
            if (wetMaterial != null)
                return wetMaterial;

            return ResolveShellMaterial();
        }

        Material ResolveFallbackMaterial()
        {
            if (_fallbackMaterial != null)
                return _fallbackMaterial;

            // 兜底链顺序：URP/Lit 在播放器构建里**不保证入包**（r3 实测 globalgamemanagers 缺席，
            // 无 .mat 引用它）；PirateSurface 在 Always Included 里（ArtGate ⓪ 步保证），
            // 作为安全兜底一定可用。全部落空则返回 null——ApplyBuffers 会隐藏渲染器并告警，
            // 绝不让空材质落到 URP 默认材质（构建缺席 = 粉色方块）。
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("PirateCrew/PirateSurface");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[BattleTerrainView] 兜底 shader 全部落空，地形视觉层隐藏。");
                return null;
            }
            _fallbackMaterial = new Material(shader) { name = "TerrainFallback" };

            // 与 URP/Lit / PirateSurface / Standard 的属性名都兼容。
            Color sand = new Color(0.62f, 0.52f, 0.38f, 1f);
            if (_fallbackMaterial.HasProperty("_BaseColor"))
                _fallbackMaterial.SetColor("_BaseColor", sand);
            if (_fallbackMaterial.HasProperty("_Color"))
                _fallbackMaterial.SetColor("_Color", sand);

            return _fallbackMaterial;
        }

        void Bump()
        {
            _version++;
            Changed?.Invoke();
        }
    }
}
