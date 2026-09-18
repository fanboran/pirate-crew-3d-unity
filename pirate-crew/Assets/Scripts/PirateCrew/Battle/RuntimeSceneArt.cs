using System.Collections.Generic;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

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
    /// 几何生成退出运行时：船体放样 / 云场合成 / 材质合并 / 网格落盘全部发生在编辑器烘焙期
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
        // 序列化接线（由 M2BattleSceneSetup / SceneArtBaker 写入）
        // ------------------------------------------------------------------

        [Tooltip("生成物的父节点；为空时用本物体 transform（SceneArt 根）。")]
        [SerializeField] Transform sceneryRoot;

        [Tooltip("烘焙件：大帆船（LargeShipRecipe+种子 20 的固定输出）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject galleonPrefab;

        [Tooltip("烘焙件：小艇（SmallBoatRecipe+种子 20 的固定输出）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject longboatPrefab;

        [Tooltip("烘焙件：低模云场（CloudFieldSpec 默认构图+种子的固定输出）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject cloudFieldPrefab;

        [Tooltip("烘焙件：落水危险虚线（样板三关共用一圈）。由 SceneArtBaker 烘焙并接线。")]
        [SerializeField] GameObject dangerBorderPrefab;

        [Tooltip("超美空岛根（场景内静态物，由 FloatingIslandShowcaseMenu.PlaceIntoBattleCenter 烘进场景）。"
            + "只有样板第 3 关激活，其余关卡隐藏。")]
        [SerializeField] GameObject skyIslandRoot;

        // 运行时实例（每次重建先删后建；只删自己的，不动 Ambient 等同级子节点）。
        readonly List<GameObject> _instances = new List<GameObject>(8);

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

        /// <summary>删除本组件上次生成的实例（幂等重建用；prefab 网格是资产，只销毁实例物体）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null)
                    DestroyObject(_instances[i]);
            }
            _instances.Clear();
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
                case ShowcasePieceId.CloudField: return cloudFieldPrefab;
                case ShowcasePieceId.DangerBorder: return dangerBorderPrefab;
                default: return null;
            }
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
