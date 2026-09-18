using System.Collections.Generic;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.PirateCrew.Battle
{
        /// <summary>
        /// 场景美术的**运行时装配**：把样板三关的摆位表（<see cref="ShowcaseLevels.ComposeInto"/> 的
        /// 自由几何：云朵/双大船/山包+超美空岛）合成为**每个材质组一个合并网格**（1 组 = 1 DrawCall），
        /// 再挂成 <see cref="MeshFilter"/> + <see cref="MeshRenderer"/>。
        ///
        /// ==================================================================
        /// 【一代退场后的职责边界】
        /// ==================================================================
        /// 世界海域图的陈设走 <c>WorldMapComposer</c>（kit FBX + 灰盒站面），不经本类；
        /// 一代 33 关的道具/构件装配链（<c>ScenePropLayout</c> / <c>SceneKitCatalog.BuildFor</c> /
        /// 平台底部）随一代退场删除。本类只服务样板三关。
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
        /// 按关卡号重建全部静态陈设（幂等：先删上次生成的组）。由 <c>BattleController.Awake</c> 调用。
        /// 只服务样板三关（自由几何装配）；非样板关留空并告警（世界图陈设走 WorldMapComposer）。
        /// </summary>
        public void RebuildFor(int levelNumber)
        {
            Clear();

            // 场景内静态空岛：只有样板第 3 关可见（其余关一律隐藏）。
            if (skyIslandRoot != null && skyIslandRoot.activeSelf != (levelNumber == 3))
                skyIslandRoot.SetActive(levelNumber == 3);

            if (!SceneArt.ShowcaseLevels.IsShowcase(levelNumber))
            {
                // 一代瓦片竞技场退场后，本装配器只服务样板三关；世界图陈设走 WorldMapComposer。
                global::PirateCrew.Core.Log.Warn("[RuntimeSceneArt] 非样板关 " + levelNumber
                    + "，静态陈设留空（世界图陈设由 WorldMapComposer 装配）。");
                LastLevelNumber = levelNumber;
                LastClusterCount = 0;
                LastTriangleCount = 0;
                LastGroupCount = 0;
                return;
            }

            // 【样板三关】自由几何装配（云朵/双大船/山包+超美空岛），完全不走格子链——
            // 轮廓由 ShowcaseLevels 的几何装配器生成，格子只作为隐形逻辑高度场存在。
            var showcaseBuffers = new ScenePropBuffers();
            ShowcaseLevels.ComposeInto(showcaseBuffers, levelNumber, Root);

            LastGroupCount = EmitGroups(showcaseBuffers, null);
            LastLevelNumber = levelNumber;
            LastClusterCount = 0;
            LastTriangleCount = showcaseBuffers.TotalTriangles;
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

        readonly HashSet<int> _warned = new HashSet<int>();

        void WarnMissing(int index, string reason)
        {
            if (!_warned.Add(index))
                return;
            global::PirateCrew.Core.Log.Warn("[RuntimeSceneArt] 材质组 " + GroupNames[index] + " 跳过：" + reason
                + "（该组几何不渲染；这不影响地形与玩法）。");
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
