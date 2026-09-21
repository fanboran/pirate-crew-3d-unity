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
        /// <summary>落水危险虚线（样板三关共用一圈）。</summary>
        DangerBorder = 0,

        /// <summary>低模云场（第 1 关「云端漫步」主景）。</summary>
        CloudField = 1,

        /// <summary>碎岛礁群（第 2 关「碎岛雨」主景：岛壳烘焙，与逻辑高度场按构造对齐）。</summary>
        Islets = 2,
    }

    /// <summary>一件烘焙陈设的摆位（纯数据）。prefab 原点 = 几何烘焙原点（云场=构图中心，碎岛=竞技场原点）。</summary>
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

    /// <summary>
    /// 样板三关数据层的**读口**（数据本身在关卡资产里，不在代码里）：
    ///   L1 云端漫步（教学：投掷手感 / 回合流转 / 小心坠落）
    ///   L2 碎岛雨（进阶：落水威胁 / 跨岛精度 / 阵地武器）
    ///   L3 天空之岛（考核：以少打多 4v5 / 越水控场武器）
    /// 数值依据逐条引用在各设计文档（docs/设计/关卡/L0N-*.md，AI 提案，待用户终审）。
    ///
    /// 【本类现在是什么】纯读口：编成 / luck / 武器池 / 逻辑高度场 / 烘焙件摆位全部来自
    /// <see cref="LevelDefinition"/> 资产（`Assets/Data/Levels/*.asset`，文本锚点 = `_golden/*.json`）。
    /// 重构前这里是一份手写 C# 表——改一个数值要改代码重编译、且没有工具能校验它。
    /// 改数据请改 golden JSON 再跑迁移器，见 <c>docs/技术/架构/关卡数据资产.md</c>。
    ///
    /// 【架构口径（未变）】格子是**不可见的逻辑高度场**，只承担两件玩家看不见的事：
    ///   1) 单位站位高度——<c>BattleController.SpawnTeams</c> 用 <c>TileTerrainGrid.SurfaceWorldY</c>；
    ///   2) AI 落点评估——<c>AiTerrain</c> 按格判实心/落水。
    /// 渲染层由烘焙 prefab 按摆位表实例化（RuntimeSceneArt）；L2 的碎岛壳直接从逻辑高度场
    /// 烘出（IslandShellGeometry.BuildSolidShell），逻辑-视觉按构造对齐。
    ///
    /// 【尺度】1 格 = 2 单位（LevelGeometry.TileWorldSize）；块高 0.5 单位
    /// （LevelGeometry.BlockWorldHeight = PixelsToUnits(8)）；水面 y=-0.4。场地统一 20×15 格。
    /// </summary>
    public static class ShowcaseLevels
    {
        /// <summary>关卡号首位。</summary>
        public const int FirstLevel = 1;

        /// <summary>关卡号末位。</summary>
        public const int LastLevel = 3;

        /// <summary>场地宽度（逻辑格）——样板三关统一尺寸，也是空岛展示件对齐用的场地口径。</summary>
        public const int WidthTiles = 20;

        /// <summary>场地纵深（逻辑格）。</summary>
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
        public static TileTerrainGrid BuildLogicGrid(int levelNumber)
        {
            LevelAssetLibrary.TryGetLevel(levelNumber, out LevelAssetPayload payload);
            return LevelRasterFromAsset.Build(payload);
        }

        /// <summary>
        /// 某样板关的烘焙件摆位表（<see cref="RuntimeSceneArt"/> 实例化消费）。
        /// 换这里的数 = 换摆位，不需要重新烘焙；碎岛壳例外——它与逻辑高度场按构造对齐，
        /// 改 L2 布局必须重跑 SceneArtBaker（资产栅格 → Islets prefab）。
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
