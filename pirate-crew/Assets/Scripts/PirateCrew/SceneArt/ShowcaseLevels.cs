using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>烘焙陈设件种类（= SceneArtBaker 的产物；一个种类一个 prefab 资产）。</summary>
    public enum ShowcasePieceId
    {
        /// <summary>低模云场（第 1 关「云端漫步」主景）。</summary>
        CloudField = 1,

        /// <summary>废弃化工厂整场（第 4 关主景；Blender 手作总装件，见 tools/blender/scene/chemplant/）。
        /// 预制引用 = FBX 本体（`Assets/Art/Models/WorldKit/ChemPlant/ChemPlant_Level.fbx`）。</summary>
        ChemPlantYard = 2,

        /// <summary>废弃化工厂整场·六件并行版（第 5 关主景；`Assets/Art/Models/SceneKit/ChemPlant.fbx`，
        /// 六名建模 Agent 分件 + 总装，见 tools/blender/scene/chemplant/README.md）。</summary>
        ChemPlantTeamYard = 3,
    }

    /// <summary>一件烘焙陈设的摆位（纯数据）。prefab 原点 = 几何烘焙原点（云场=构图中心，其余=竞技场原点）。</summary>
    public readonly struct ShowcasePiecePlacement
    {
        public readonly ShowcasePieceId Piece;

        /// <summary>实例世界位置。</summary>
        public readonly Vector3 Position;

        /// <summary>绕 Y 朝向（度）。</summary>
        public readonly float YawDegrees;

        /// <summary>实例名（层级可读性，非逻辑键）。</summary>
        public readonly string InstanceName;

        public ShowcasePiecePlacement(ShowcasePieceId piece, Vector3 position, float yawDegrees, string instanceName)
        {
            Piece = piece;
            Position = position;
            YawDegrees = yawDegrees;
            InstanceName = instanceName;
        }
    }

    /// <summary>样板关数据层的**读口**（数据本身在关卡资产里，不在代码里）：
    ///   L1 云端漫步（教学：投掷手感 / 回合流转 / 小心坠落）
    ///   L3 天空之岛（考核：以少打多 4v5 / 越水控场武器）
    /// 数值依据逐条引用在各设计文档（docs/设计/关卡/L0N-*.md，AI 提案，待用户终审）。
    ///
    /// 【本类现在是什么】纯读口：编成 / luck / 武器池 / 逻辑高度场 / 烘焙件摆位全部来自
    /// <see cref="LevelDefinition"/> 资产（`Assets/Data/Levels/*.asset`，文本锚点 = `_golden/*.json`）。
    /// 重构前这里是一份手写 C# 表——改一个数值要改代码重编译、且没有工具能校验它。
    /// 改数据请改 golden JSON 再跑迁移器，见 <c>docs/技术/架构/关卡数据资产.md</c>。
    ///
    /// 【架构口径（未变）】高度场是**不可见的逻辑地形**，只承担两件玩家看不见的事：
    ///   1) 单位站位高度——<c>BattleController.SpawnTeams</c> 用 <c>HeightfieldGrid.SurfaceWorldY</c>；
    ///   2) AI 落点评估——<c>AiTerrain</c> 按采样格判实心/落水。
    /// 渲染层由烘焙 prefab 按摆位表实例化（RuntimeSceneArt），与逻辑层共用同一份高度场资产对齐。
    ///
    /// 【尺度】数值全米：块高 0.5 m、水面 y = −0.4 m；运行时地形按 2 m 采样
    /// （<see cref="LevelGeometry.TileWorldSize"/>，只是数据粒度）。L1/L3 场地 40×30 m。
    ///
    /// 【WidthTiles / DepthTiles 为何还在】它们是 2 m 采样口径下的格数（20×15），
    /// 仍是场景装配 / 水面资产构建等编辑器工具的对齐口径；资产与玩法数值一律用米。
    /// </summary>
    public static class ShowcaseLevels
    {
        /// <summary>关卡号首位。</summary>
        public const int FirstLevel = 1;

        /// <summary>关卡号末位。</summary>
        public const int LastLevel = 5;

        /// <summary>场地宽度（采样格数，= 40 m / 2 m）——样板关统一尺寸，也是空岛展示件对齐用的场地口径。</summary>
        public const int WidthTiles = 20;

        /// <summary>场地纵深（采样格数，= 30 m / 2 m）。</summary>
        public const int DepthTiles = 15;

        /// <summary>该关卡号是否有对应关卡资产。</summary>
        public static bool IsShowcase(int levelNumber) =>
            LevelAssetLibrary.TryGetLevel(levelNumber, out _);

        /// <summary>样板关的出战数据；无对应资产时返回 null（调用方回落海图或兜底关）。</summary>
        public static LevelData? BuildLevelData(int levelNumber)
        {
            if (!LevelAssetLibrary.TryGetLevel(levelNumber, out LevelAssetPayload payload))
                return null;
            return payload.ToLevelData();
        }

        /// <summary>
        /// 样板关的逻辑地形（唯一栅格语义；读资产里的高度场）。
        /// 资产栅格不自洽时返回全平网格——由内容门禁把坏数据拦在构建期。
        /// </summary>
        public static HeightfieldGrid BuildLogicGrid(int levelNumber)
        {
            LevelAssetLibrary.TryGetLevel(levelNumber, out LevelAssetPayload payload);
            return LevelRasterFromAsset.Build(payload);
        }

        /// <summary>
        /// 某样板关的烘焙件摆位表（<see cref="RuntimeSceneArt"/> 实例化消费）。
        /// 换这里的数 = 换摆位，不需要重新烘焙。
        ///
        /// 【号段有意不连续】样板关号不做重编号——重编号会牵动存档/选关/测试的
        /// 既有语义，而空号在数据层是允许的（校验器不要求号段连续）。
        /// </summary>
        public static List<ShowcasePiecePlacement> BakedPlacements(int levelNumber)
        {
            var list = new List<ShowcasePiecePlacement>();
            if (!LevelAssetLibrary.TryGetLevel(levelNumber, out LevelAssetPayload payload))
                return list;

            List<BakedPieceEntry> entries = payload.bakedPieces;
            for (int i = 0; i < entries.Count; i++)
            {
                BakedPieceEntry e = entries[i];
                list.Add(new ShowcasePiecePlacement(
                    (ShowcasePieceId)e.pieceId,
                    new Vector3(e.x, e.y, e.z),
                    e.yawDeg,
                    e.instanceName));
            }
            return list;
        }
    }
}
