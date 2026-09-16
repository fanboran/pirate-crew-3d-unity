using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 场景美术的**运行时装配**：把纯 C# 的摆位表（<see cref="ScenePropLayout"/> 的道具、
    /// <see cref="SceneKitCatalog.BuildFor"/> 的构件、<see cref="IslandShellGeometry.AddPlatformUndersides"/>
    /// 的平台底部）合成为**每个材质组一个合并网格**（1 组 = 1 DrawCall），再挂成
    /// <see cref="MeshFilter"/> + <see cref="MeshRenderer"/>。
    ///
    /// ==================================================================
    /// 【为什么需要它：烘焙侧退位】
    /// ==================================================================
    /// 旧做法是编辑器把 **level_1** 的陈设烘成静态网格存进场景（<c>Assets/Editor/SceneArtBuilder.cs</c>）。
    /// 换关卡时地形瓦片会变（<see cref="TerrainCatalog"/> / <see cref="PlatformClusterLayout.BuildFor"/> 支持任意关），
    /// 但烘死的陈设不变 → 平台簇与陈设脱节。现在场景烘成**关卡无关**（材质资产 + 接线保留），
    /// 陈设在开局按**实际关卡号**在运行时重建，见 <see cref="RebuildFor"/>。
    ///
    /// 【它不做什么（边界）】可破坏地形仍完全由 <see cref="BattleTerrainView"/> 负责：
    /// 碰撞块、视觉壳、爆炸后的 <c>ApplyDestruction</c> 重建协议一概不碰。本类只负责**静态陈设**
    /// （道具 / kit / 平台底部），故爆炸打掉一格平台后，该格上的静态陈设不会逐格消失——
    /// 这是"静态陈设"的固有取舍（合并网格无法逐格删），已与"地形走 BattleTerrainView"的分工一致。
    ///
    /// 【材质从哪来】场景里由 <c>M2BattleSceneSetup</c> 烘焙时写进 <see cref="groupMaterials"/>
    /// （按 <see cref="GroupNames"/> 的顺序，资产在 <c>Assets/Art/Materials/Scene/</c>）。
    /// 【取舍】没有新建 ScriptableObject 资产、没有走 Resources 路径常量：材质仍是
    /// <c>SceneArtBuilder</c> 生成的同一批 .mat 资产，场景只是多了一个组件持引用。
    /// 好处是"最小侵入、不新增资产类型、与既有材质管线同源"；代价是场景文件里多一条组件记录，
    /// 且换材质名/顺序时要同步改这里的表（顺序错 = 组与材质错配，故 <see cref="GroupNames"/>
    /// 是唯一的顺序权威，编辑器侧按它填）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeSceneArt : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 材质组表（顺序 = 编辑器填 <see cref="groupMaterials"/> 的顺序）
        // ------------------------------------------------------------------

        /// <summary>材质组数（= <see cref="GroupNames"/> 长度）。</summary>
        public const int GroupCount = 21;

        /// <summary>
        /// 材质组名（**顺序权威**）。前 13 组来自 <see cref="ScenePropBuffers"/>，
        /// 后 8 组是远景/植被的**分层附加组**（<see cref="ScenePropTierBuffers"/>，见其类头）。
        /// </summary>
        public static readonly string[] GroupNames =
        {
            "Wood", "WoodDark", "Rock", "Metal", "Foliage", "Cloth",
            "FlagRed", "FlagBlue", "SandWet", "Foam", "DangerLine",
            "FarSilhouette", "Clouds",
            "FarSilNear", "FarSilMid", "FarSilFar", "CloudCore", "CloudFringe",
            "SailFar", "GrassRoot", "GrassLight",
        };

        /// <summary>
        /// 各组材质资产名（<c>Assets/Art/Materials/Scene/&lt;name&gt;.mat</c>）。
        /// 由 <c>SceneArtBuilder.SceneArtMaterials</c> 生成；顺序与 <see cref="GroupNames"/> 一一对应。
        /// </summary>
        public static readonly string[] GroupMaterialNames =
        {
            "Scene_Wood", "Scene_WoodDark", "Scene_Rock", "Scene_Metal", "Scene_Foliage", "Scene_Cloth",
            "Scene_FlagRed", "Scene_FlagBlue", "Scene_SandWet", "Scene_Foam", "Scene_Danger",
            "Scene_Silhouette", "Scene_Cloud",
            "Scene_SilhouetteNear", "Scene_SilhouetteMid", "Scene_SilhouetteFar",
            "Scene_CloudCore", "Scene_CloudFringe",
            "Scene_SailFar", "Scene_GrassRoot", "Scene_GrassLight",
        };

        const int GWood = 0;
        const int GWoodDark = 1;
        const int GRock = 2;
        const int GMetal = 3;
        const int GFoliage = 4;
        const int GCloth = 5;
        const int GFlagRed = 6;
        const int GFlagBlue = 7;
        const int GSandWet = 8;
        const int GFoam = 9;
        const int GDangerLine = 10;
        const int GFarSilhouette = 11;
        const int GClouds = 12;
        const int GFarSilNear = 13;
        const int GFarSilMid = 14;
        const int GFarSilFar = 15;
        const int GCloudCore = 16;
        const int GCloudFringe = 17;
        const int GSailFar = 18;
        const int GGrassRoot = 19;
        const int GGrassLight = 20;

        /// <summary>危险虚线参数（与 <c>SceneArtBuilder</c> 同值：偏移 3.15 / 线宽 0.12 / 水面顶 -0.15）。</summary>
        const float DangerLineOffset = 3.15f;
        const float DangerLineWidth = 0.12f;
        const float WaterTopY = -0.15f;

        // ------------------------------------------------------------------
        // 序列化接线（由 M2BattleSceneSetup 烘焙时写入）
        // ------------------------------------------------------------------

        [Tooltip("生成物的父节点；为空时用本物体 transform（SceneArt 根）。")]
        [SerializeField] Transform sceneryRoot;

        [Tooltip("材质组材质（顺序必须与 RuntimeSceneArt.GroupNames 一致）；由 M2BattleSceneSetup 烘焙时写入。")]
        [SerializeField] Material[] groupMaterials = new Material[GroupCount];

        [Tooltip("远景/植被是否走分层材质组（关闭则远景并进单组，与旧烘焙口径一致）。")]
        [SerializeField] bool useTieredGroups = true;

        [Tooltip("超美空岛根（场景内静态物，由 FloatingIslandShowcaseMenu.PlaceIntoBattleCenter 烘进场景）。"
            + "只有样板第 3 关激活，其余关卡隐藏。")]
        [SerializeField] GameObject skyIslandRoot;

        // 生成物（每次重建先删后建；只删自己的，不动 Ambient 等同级子节点）。
        readonly List<GameObject> _groups = new List<GameObject>(GroupCount);

        /// <summary>最近一次重建的关卡号（未重建过为 0）。</summary>
        public int LastLevelNumber { get; private set; }

        /// <summary>最近一次重建的簇数（0 = 未重建 / 无平台布局）。</summary>
        public int LastClusterCount { get; private set; }

        /// <summary>最近一次重建的合并网格三角面合计。</summary>
        public int LastTriangleCount { get; private set; }

        /// <summary>最近一次重建实际落盘的材质组数。</summary>
        public int LastGroupCount { get; private set; }

        /// <summary>生成物父节点（为空回落自身）。</summary>
        Transform Root => sceneryRoot != null ? sceneryRoot : transform;

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>
        /// 按实际关卡号重建全部静态陈设（幂等：先删上次生成的组）。由 <c>BattleController.Awake</c> 调用。
        /// 关卡数据未转写时不生成任何陈设（清空 + 告警），避免"layout 与关卡对不上"的错位。
        /// </summary>
        public void RebuildFor(int levelNumber)
        {
            Clear();

            // 场景内静态空岛：只有样板第 3 关可见（其余关卡/旧关一律隐藏）。
            if (skyIslandRoot != null && skyIslandRoot.activeSelf != (levelNumber == 3))
                skyIslandRoot.SetActive(levelNumber == 3);

            // 【样板三关】自由几何装配（云朵/双大船/山包+超美空岛），完全不走格子链——
            // 轮廓由 ShowcaseLevels 的几何装配器生成，格子只作为隐形逻辑高度场存在。
            if (SceneArt.ShowcaseLevels.IsShowcase(levelNumber))
            {
                var showcaseBuffers = new ScenePropBuffers();
                ShowcaseLevels.ComposeInto(showcaseBuffers, levelNumber, Root);

                LastGroupCount = EmitGroups(showcaseBuffers, null);
                LastLevelNumber = levelNumber;
                LastClusterCount = 0;
                LastTriangleCount = showcaseBuffers.TotalTriangles;
                return;
            }

            if (!LevelCatalog.IsTranscribed(levelNumber))
            {
                global::PirateCrew.Core.Log.Warn("[RuntimeSceneArt] LevelCatalog 未转写关卡 " + levelNumber
                    + "，静态陈设留空（不生成与关卡错位的几何）。");
                LastLevelNumber = levelNumber;
                LastClusterCount = 0;
                LastTriangleCount = 0;
                LastGroupCount = 0;
                return;
            }

            LevelData level = LevelCatalog.Get(levelNumber);
            PlatformMap map = PlatformClusterLayout.BuildFor(level);

            TileTerrainGrid grid = map != null
                && map.WidthTiles == level.WidthTiles && map.DepthTiles == level.HeightTiles
                ? new TileTerrainGrid(level.WidthTiles, level.HeightTiles, null,
                    TerrainCatalog.DefaultBlockWorldHeight, map)
                : TileTerrainGrid.Flat(level.WidthTiles, level.HeightTiles);

            int seed = levelNumber * 1013 + 7;

            var buffers = new ScenePropBuffers();
            bool tiered = useTieredGroups && HasTierMaterials();
            ScenePropTierBuffers tiers = tiered
                ? new ScenePropTierBuffers(new MeshBuffers(), new MeshBuffers(), new MeshBuffers(),
                    new MeshBuffers(), new MeshBuffers(), new MeshBuffers(), new MeshBuffers(), new MeshBuffers())
                : null;

            // 1. 道具（搁浅船 / 栈桥 / 箱桶 / 旗 / 棕榈 / 草丛 / 礁石 / 远景剪影 / 云）。
            SceneLayout propLayout = ScenePropLayout.Build(grid, CollectSpawnCells(level), seed);
            ScenePropComposer.Compose(buffers, propLayout, seed, level.HeightTiles, tiers);

            // 2. 模块化构件（大船 / 空岛 / 梯田小岛的伪装）。
            int kitSeed = seed + 500;
            SceneKitComposer.Compose(buffers, SceneKitCatalog.BuildFor(levelNumber, map, kitSeed), kitSeed);

            // 3. 悬空平台底部（船体侧板+龙骨 / 岩锥 / 岩层）——与 kit 用同一批材质组合并。
            if (map != null)
                IslandShellGeometry.AddPlatformUndersides(buffers, grid, IslandShellSettings.Default);

            // 4. 落水危险虚线（绕竞技场矩形一圈）。
            IslandShellGeometry.AddDashedBorder(buffers.Danger, level.WidthTiles, level.HeightTiles,
                DangerLineOffset, WaterTopY + 0.012f, 0.9f, 0.55f, DangerLineWidth);

            // 5. 落盘：每个非空材质组 = 1 网格 + 1 渲染器。
            LastGroupCount = EmitGroups(buffers, tiers);

            LastLevelNumber = levelNumber;
            LastClusterCount = map != null ? map.Clusters.Count : 0;
            LastTriangleCount = buffers.TotalTriangles;
        }

        /// <summary>删除本组件上次生成的组（幂等重建用）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (_groups[i] == null)
                    continue;
                MeshFilter filter = _groups[i].GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                DestroyObject(_groups[i]);
                if (mesh != null)
                    DestroyObject(mesh);
            }
            _groups.Clear();
        }

        // ------------------------------------------------------------------
        // 合成 → 网格
        // ------------------------------------------------------------------

        int EmitGroups(ScenePropBuffers buffers, ScenePropTierBuffers tiers)
        {
            int groups = 0;
            for (int i = 0; i < GroupCount; i++)
            {
                MeshBuffers source = BufferFor(i, buffers, tiers);
                Material material = MaterialFor(i);
                if (source == null || source.IsEmpty || material == null)
                    continue;

                EmitGroup(i, source, material);
                groups++;
            }
            return groups;
        }

        void EmitGroup(int index, MeshBuffers source, Material material)
        {
            // 【命名必须与烘焙侧一致（"SceneArt_" + 组名）】AmbientDirector.ResolveSceneArtRoot /
            // AmbientWindBinder.Bind 用 `Transform.Find("SceneArt_Foliage")` 这类**直接子节点名**
            // 定位组渲染器（做顶点风摆）。烘焙退位后组由本类在运行时生成，名字不同就会
            // "找不到 Foliage/FlagRed/FlagBlue/Cloth → 风摆静默失效"。故沿用同一命名。
            string objectName = "SceneArt_" + GroupNames[index];

            var go = new GameObject(objectName);
            go.transform.SetParent(Root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var vertices = new List<Vector3>(source.VertexCount);
            var normals = new List<Vector3>(source.VertexCount);
            var triangles = new List<int>(source.IndexCount);
            source.CopyTo(vertices, normals, triangles);

            var mesh = new Mesh { name = objectName };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            // 投影口径与烘焙侧一致：道具/草投影并接收；效果类（泡沫/危险线/远景/云）不投影。
            bool castShadows = index == GWood || index == GWoodDark || index == GRock || index == GMetal
                || index == GFoliage || index == GCloth || index == GFlagRed || index == GFlagBlue
                || index == GSandWet || index == GGrassRoot || index == GGrassLight;
            bool receiveShadows = castShadows;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;

            _groups.Add(go);
        }

        MeshBuffers BufferFor(int index, ScenePropBuffers buffers, ScenePropTierBuffers tiers)
        {
            switch (index)
            {
                case GWood: return buffers.Wood;
                case GWoodDark: return buffers.WoodDark;
                case GRock: return buffers.Rock;
                case GMetal: return buffers.Metal;
                case GFoliage: return buffers.Foliage;
                case GCloth: return buffers.Cloth;
                case GFlagRed: return buffers.FlagRed;
                case GFlagBlue: return buffers.FlagBlue;
                case GSandWet: return buffers.SandWet;
                case GFoam: return buffers.Foam;
                case GDangerLine: return buffers.Danger;
                case GFarSilhouette: return buffers.Silhouette;
                case GClouds: return buffers.Cloud;
                case GFarSilNear: return tiers != null ? tiers.SilhouetteNear : buffers.Silhouette;
                case GFarSilMid: return tiers != null ? tiers.SilhouetteMid : buffers.Silhouette;
                case GFarSilFar: return tiers != null ? tiers.SilhouetteFar : buffers.Silhouette;
                case GCloudCore: return tiers != null ? tiers.CloudCore : buffers.Cloud;
                case GCloudFringe: return tiers != null ? tiers.CloudFringe : buffers.Cloud;
                case GSailFar: return tiers != null ? tiers.SailFar : buffers.Silhouette;
                case GGrassRoot: return tiers != null ? tiers.GrassRoot : buffers.Foliage;
                case GGrassLight: return tiers != null ? tiers.GrassLight : buffers.Foliage;
                default: return null;
            }
        }

        Material MaterialFor(int index)
        {
            if (groupMaterials == null || index >= groupMaterials.Length)
            {
                WarnMissing(index, "材质数组未接线或长度不足");
                return null;
            }

            Material material = groupMaterials[index];
            if (material == null)
                WarnMissing(index, "材质槽为空（烘焙时未找到 " + GroupMaterialNames[index] + ".mat？）");

            return material;
        }

        bool HasTierMaterials()
        {
            if (groupMaterials == null || groupMaterials.Length < GroupCount)
                return false;

            for (int i = GFarSilNear; i <= GGrassLight; i++)
            {
                if (groupMaterials[i] == null)
                    return false;
            }
            return true;
        }

        readonly HashSet<int> _warned = new HashSet<int>();

        void WarnMissing(int index, string reason)
        {
            if (!_warned.Add(index))
                return;
            global::PirateCrew.Core.Log.Warn("[RuntimeSceneArt] 材质组 " + GroupNames[index] + " 跳过：" + reason
                + "（该组几何不渲染；这不影响地形与玩法）。");
        }

        static List<Vector2Int> CollectSpawnCells(LevelData level)
        {
            var cells = new List<Vector2Int>();
            if (level.Units == null)
                return cells;

            for (int i = 0; i < level.Units.Count; i++)
                cells.Add(new Vector2Int(level.Units[i].gridX, level.Units[i].gridY));

            return cells;
        }

        static void DestroyObject(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
