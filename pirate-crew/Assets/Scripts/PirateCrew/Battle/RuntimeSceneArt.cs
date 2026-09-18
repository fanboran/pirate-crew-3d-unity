using System.Collections.Generic;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.PirateCrew.Battle
{
        /// <summary>
        /// 场景美术的**运行时装配**：样板三关的静态陈设由**编辑器烘焙 prefab** 提供
        /// （<c>SceneArtBaker</c> 产出：配方 + 种子 → 按材质组合并网格 → prefab），本类只按
        /// 关卡号读取摆位表（<see cref="ShowcaseLevels.BakedPlacements"/>）做**实例化组装**。
        ///
        /// ==================================================================
        /// 【糖豆人式资产架构（管线合并任务书）】
        /// ==================================================================
        /// 几何生成退出运行时：船体放样 / 材质合并 / 网格落盘全部发生在编辑器烘焙期
        /// （Assets/Editor/SceneArtBaker.cs），同配方重跑逐顶点一致（确定性测试钉住）。
        /// 多样性来自"更多预制变体"而非运行时随机——需要第 N 种船时加一条配方烘焙第 N 个
        /// prefab，运行时零成本换装。
        ///
        /// 【世界海域图不经本类】陈设走 <see cref="WorldMapComposer"/>（kit FBX + 灰盒站面）。
        ///
        /// 【超美空岛】是场景内静态物（FloatingIslandShowcaseMenu.PlaceIntoBattleCenter 烘进
        /// Battle.unity），本类只按关卡号开关（只有样板第 3 关激活）。
        ///
        /// 【烘焙件缺失怎么办】烘焙 prefab 引用为空（未跑 SceneArtBaker）时告警一次、陈设留空，
        /// 不影响地形与玩法——先跑 PirateCrew/烘焙/样板场景件。
        /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeSceneArt : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 材质组表（顺序 = 编辑器填 <see cref="groupMaterials"/> 的顺序；烘焙命名权威）
        // ------------------------------------------------------------------

        /// <summary>材质组数（= <see cref="GroupNames"/> 长度）。</summary>
        public const int GroupCount = 21;

        /// <summary>
        /// 材质组名（**顺序权威**）。前 13 组来自 <see cref="ScenePropBuffers"/>，
        /// 后 8 组是远景/植被的**分层附加组**（<see cref="ScenePropTierBuffers"/>，见其类头）。
        /// 烘焙器用同名字约定落 prefab 子网格（"SceneArt_&lt;组名&gt;"——AmbientWindBinder 依赖）。
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
        /// 顺序与 <see cref="GroupNames"/> 一一对应。
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

        // 组下标常量（BufferFor/EmitGroup 的过渡期旧路径消费；阶段 B 云场烘焙落地后随旧路径一起删）。
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

        // ------------------------------------------------------------------
        // 序列化接线（由 M2BattleSceneSetup / SceneArtBaker 写入）
        // ------------------------------------------------------------------

        [Tooltip("生成物的父节点；为空时用本物体 transform（SceneArt 根）。")]
        [SerializeField] Transform sceneryRoot;

        [Tooltip("材质组材质（顺序必须与 RuntimeSceneArt.GroupNames 一致）；第 1 关云场的过渡路径仍消费。")]
        [SerializeField] Material[] groupMaterials = new Material[GroupCount];

        [Tooltip("烘焙件：大帆船（LargeShipRecipe+种子 20 的固定输出）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject galleonPrefab;

        [Tooltip("烘焙件：小艇（SmallBoatRecipe+种子 20 的固定输出）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject longboatPrefab;

        [Tooltip("烘焙件：落水危险虚线（样板三关共用一圈）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject dangerBorderPrefab;

        [Tooltip("超美空岛根（场景内静态物，由 FloatingIslandShowcaseMenu.PlaceIntoBattleCenter 烘进场景）。"
            + "只有样板第 3 关激活，其余关卡隐藏。")]
        [SerializeField] GameObject skyIslandRoot;

        // 运行时实例（每次重建先删后建；只删自己的，不动 Ambient 等同级子节点）。
        readonly List<GameObject> _instances = new List<GameObject>(8);

        // 【阶段 A 过渡】第 1 关云场暂走旧运行时装配（烘焙在阶段 B 落地），产物记录在 _legacyGroups。
        readonly List<GameObject> _legacyGroups = new List<GameObject>(GroupCount);

        /// <summary>最近一次重建的关卡号（未重建过为 0）。</summary>
        public int LastLevelNumber { get; private set; }

        /// <summary>生成物父节点（为空回落自身）。</summary>
        Transform Root => sceneryRoot != null ? sceneryRoot : transform;

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>
        /// 按关卡号重建全部静态陈设（幂等：先删上次实例）。由 <c>BattleController.Awake</c> 调用。
        /// 样板关走烘焙件实例化；非样板关留空并告警（世界图陈设走 WorldMapComposer）。
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
                return;
            }

            if (levelNumber == 1)
            {
                // 【阶段 A 过渡】云场烘焙（CloudField.prefab）在阶段 B 落地；此前保留旧运行时装配
                //（自由几何 + 危险虚线一并由旧路径产出，避免过渡期双份虚线）。
                RebuildLegacy(levelNumber);
                return;
            }

            // 【烘焙件实例化】摆位表是数据层（件 id + 摆位），几何/种子已固定在 prefab 里。
            List<ShowcasePiecePlacement> placements = SceneArt.ShowcaseLevels.BakedPlacements(levelNumber);
            int instantiated = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                if (InstantiatePiece(placements[i]))
                    instantiated++;
            }

            LastLevelNumber = levelNumber;
            global::PirateCrew.Core.Log.Info("[RuntimeSceneArt] 样板关 " + levelNumber + " 烘焙陈设实例化 "
                + instantiated + "/" + placements.Count + " 件（prefab 见 Assets/Art/Models/SceneKit/）。");
        }

        /// <summary>删除本组件上次生成的实例与过渡期旧装配产物（幂等重建用）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null)
                    DestroyObject(_instances[i]);
            }
            _instances.Clear();

            // 过渡期旧装配产物（运行时网格必须随物体一起回收，防泄漏）。
            for (int i = 0; i < _legacyGroups.Count; i++)
            {
                if (_legacyGroups[i] == null)
                    continue;
                MeshFilter filter = _legacyGroups[i].GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                DestroyObject(_legacyGroups[i]);
                if (mesh != null)
                    DestroyObject(mesh);
            }
            _legacyGroups.Clear();
        }

        // ------------------------------------------------------------------
        // 烘焙件实例化
        // ------------------------------------------------------------------

        bool InstantiatePiece(in ShowcasePiecePlacement placement)
        {
            GameObject prefab = PrefabFor(placement.Piece);
            if (prefab == null)
            {
                global::PirateCrew.Core.Log.Warn("[RuntimeSceneArt] 烘焙件缺失（" + placement.Piece
                    + "）：先跑菜单 PirateCrew/烘焙/样板场景件（SceneArtBaker.BuildAll）。该件本局不渲染。");
                return false;
            }

            Quaternion rotation = Quaternion.Euler(0f, placement.YawDegrees, 0f);
            GameObject instance = Instantiate(prefab, placement.Position, rotation, Root);
            instance.name = placement.InstanceName;
            _instances.Add(instance);
            return true;
        }

        GameObject PrefabFor(ShowcasePieceId piece)
        {
            switch (piece)
            {
                case ShowcasePieceId.Galleon: return galleonPrefab;
                case ShowcasePieceId.Longboat: return longboatPrefab;
                case ShowcasePieceId.DangerBorder: return dangerBorderPrefab;
                default: return null;
            }
        }

        // ------------------------------------------------------------------
        // 【阶段 A 过渡】第 1 关旧运行时装配（阶段 B 云场烘焙落地后整段删除）
        // ------------------------------------------------------------------

        void RebuildLegacy(int levelNumber)
        {
            var showcaseBuffers = new ScenePropBuffers();
            ShowcaseLevels.ComposeInto(showcaseBuffers, levelNumber, Root);
            EmitGroups(showcaseBuffers, null);
            LastLevelNumber = levelNumber;
        }

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
            // 【命名必须与烘焙侧一致（"SceneArt_" + 组名）】AmbientWindBinder 按名定位组渲染器做风摆。
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

            _legacyGroups.Add(go);
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
                return null;

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
