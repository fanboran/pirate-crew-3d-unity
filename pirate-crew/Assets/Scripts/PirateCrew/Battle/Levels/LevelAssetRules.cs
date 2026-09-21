using System.Collections.Generic;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 关卡资产的**语义校验规则**（纯 C#，无头可跑）——校验器（编辑器）与内容门禁（测试）
    /// 共用的唯一一份判据，避免"测试里一套、工具里又一套"。
    ///
    /// 【分工】本类只判**数据自身**是否自洽、是否满足玩法硬约束；"资产文件是否登记进清单""资产文本
    /// 是否等于 golden JSON"这类**文件层**判据在 <c>Assets/Editor/Levels/LevelAssetValidator.cs</c>
    /// （需要 AssetDatabase 的判据）与 <c>Assets/Tests/Battle/LevelAssetTests.cs</c>（无头能做的那部分）。
    ///
    /// 【为什么不静默修】全部返回问题描述列表，不改数据。关卡数据是构建期内容，
    /// 坏数据应当把门禁打红、由人决定怎么改，而不是被工具"猜着修好"。
    /// </summary>
    public static class LevelAssetRules
    {
        /// <summary>海图接敌距离上限（双方出生质心间距，米）。超限只告警——是【提案/待定】的平衡口径。</summary>
        public const float RecommendedEngagementDistance = 120f;

        /// <summary>逻辑高度场的最高允许顶面（米）。超过视为数据错误（块数填错一位就会命中）。</summary>
        public const float MaxTerrainTopWorldY = 40f;

        // ------------------------------------------------------------------
        // 海图
        // ------------------------------------------------------------------

        /// <summary>一张海图的硬约束问题（空 = 通过）。id/跨度/氛围档/武器池/连通性/出生点/栅格。</summary>
        public static List<string> Problems(WorldMapDefinition map)
        {
            var problems = new List<string>();
            if (map == null)
            {
                problems.Add("海图为空");
                return problems;
            }

            string tag = "[" + (string.IsNullOrEmpty(map.Id) ? map.LevelNumber.ToString() : map.Id) + "] ";

            if (string.IsNullOrEmpty(map.Id))
                problems.Add(tag + "缺 id");
            if (map.LevelNumber < WorldMapCatalog.FirstLevelNumber)
                problems.Add(tag + "关卡号 " + map.LevelNumber + " 小于海图段起点 " + WorldMapCatalog.FirstLevelNumber);
            if (map.SpanX <= 0f || map.SpanZ <= 0f)
                problems.Add(tag + "图幅必须为正值（" + map.SpanX + "×" + map.SpanZ + "）");
            if (string.IsNullOrEmpty(map.AmbientTier))
                problems.Add(tag + "缺氛围档（Noon/Dusk/Storm）");
            if (map.Spawns.Count == 0)
                problems.Add(tag + "没有出生点");
            if (map.Terrain.Count == 0)
                problems.Add(tag + "没有地形/船坞件（站面全空）");

            problems.AddRange(WeaponProblems(tag, "船员初配", map.CrewWeapons));
            problems.AddRange(WeaponProblems(tag, "船长初配", map.CaptainWeapons));
            problems.AddRange(WeaponProblems(tag, "空投池", map.AirdropPool));

            // 连通性 + 出生点落位（复用玩法规则层，判据只有这一份实现）。
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            if (boxes.Count == 0)
            {
                problems.Add(tag + "站面 box 为空（kit 资产的 standable 表缺失？）");
            }
            else
            {
                List<int> unreachable = WorldMapRules.UnreachableSpawnBoxes(map, boxes);
                if (unreachable.Count > 0)
                {
                    problems.Add(tag + "出生点站面不可达（连通性破产），box 索引 "
                        + string.Join(",", unreachable));
                }
                problems.AddRange(WorldMapRules.ValidateSpawns(map, boxes));
            }

            // 栅格化（进 TileTerrainGrid 的通道必须通）。
            if (!WorldMapRules.TryRasterize(map, out int widthTiles, out int depthTiles, out int[] blocks))
            {
                problems.Add(tag + "栅格化失败");
            }
            else
            {
                int maxBlocks = 0;
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (blocks[i] > maxBlocks)
                        maxBlocks = blocks[i];
                }
                float top = maxBlocks * 0.5f;
                if (top > MaxTerrainTopWorldY)
                {
                    problems.Add(tag + "站面最高顶 " + top + "u 超过上限 " + MaxTerrainTopWorldY
                        + "u（站面 box 的 TopY 可能填错）");
                }
                if (blocks.Length != widthTiles * depthTiles)
                    problems.Add(tag + "栅格块数与尺寸不自洽");
            }

            // 尺度告警（不阻断）：出生质心间距与满力射程的关系是审计里未决的平衡口径。
            float distance = TeamSpawnCentroidDistance(map);
            if (distance > RecommendedEngagementDistance)
            {
                problems.Add("【告警】" + tag + "双方出生质心间距 " + distance.ToString("F1")
                    + "u 超过建议上限 " + RecommendedEngagementDistance + "u（提案/待定）");
            }

            return problems;
        }

        /// <summary>双方出生质心平面距离；某一方没有出生点返回 0。</summary>
        public static float TeamSpawnCentroidDistance(WorldMapDefinition map)
        {
            float ax = 0f, az = 0f, bx = 0f, bz = 0f;
            int an = 0, bn = 0;
            for (int i = 0; i < map.Spawns.Count; i++)
            {
                WorldMapSpawn s = map.Spawns[i];
                if (s.TeamIndex == 0)
                {
                    ax += s.X;
                    az += s.Z;
                    an++;
                }
                else
                {
                    bx += s.X;
                    bz += s.Z;
                    bn++;
                }
            }

            if (an == 0 || bn == 0)
                return 0f;

            ax /= an;
            az /= an;
            bx /= bn;
            bz /= bn;
            float dx = ax - bx, dz = az - bz;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        static List<string> WeaponProblems(string tag, string slot, IReadOnlyList<WeaponStack> stacks)
        {
            var problems = new List<string>();
            if (stacks == null)
                return problems;

            for (int i = 0; i < stacks.Count; i++)
            {
                WeaponStack stack = stacks[i];
                if (!WeaponCatalog.TryGet(stack.id, out _))
                    problems.Add(tag + slot + " 含未知武器 id " + stack.id + "（不在 WeaponCatalog 里）");
                if (stack.count <= 0)
                    problems.Add(tag + slot + " 的 " + stack.id + " 件数为 " + stack.count + "（应 &gt; 0）");
            }
            return problems;
        }

        // ------------------------------------------------------------------
        // 非海图关卡
        // ------------------------------------------------------------------

        /// <summary>一张关卡资产的问题（空 = 通过）：尺寸 / 栅格 / 站位 / 编成 / 武器 / 摆件。</summary>
        public static List<string> Problems(LevelAssetPayload payload)
        {
            var problems = new List<string>();
            if (payload == null)
            {
                problems.Add("关卡载荷为空");
                return problems;
            }

            string tag = "[" + payload.levelNumber + " " + payload.assetName + "] ";

            if (string.IsNullOrEmpty(payload.assetName))
                problems.Add(tag + "缺 assetName（资产文件名/JSON 文件名都靠它）");
            if (string.IsNullOrEmpty(payload.displayName))
                problems.Add(tag + "缺 displayName");
            if (payload.widthTiles <= 0 || payload.depthTiles <= 0)
                problems.Add(tag + "格子尺寸非法 " + payload.widthTiles + "×" + payload.depthTiles);

            // 单一栅格语义：尺寸自洽 + 块高与工程常量一致。
            if (!payload.terrain.IsWellFormed)
            {
                problems.Add(tag + "逻辑高度场不自洽（块数 " + BlockCount(payload)
                    + " ≠ " + payload.widthTiles + "×" + payload.depthTiles + "）");
            }
            else
            {
                if (payload.terrain.widthTiles != payload.widthTiles
                    || payload.terrain.depthTiles != payload.depthTiles)
                {
                    problems.Add(tag + "栅格尺寸 " + payload.terrain.widthTiles + "×" + payload.terrain.depthTiles
                        + " 与场地尺寸 " + payload.widthTiles + "×" + payload.depthTiles + " 不一致");
                }

                float expectedBlock = LevelGeometry.BlockWorldHeight;
                if (System.Math.Abs(payload.terrain.blockWorldHeight - expectedBlock) > 1e-4f)
                {
                    problems.Add(tag + "块高 " + payload.terrain.blockWorldHeight
                        + " 与 LevelGeometry.BlockWorldHeight=" + expectedBlock + " 不一致");
                }

                int solid = 0, maxBlocks = 0;
                List<int> blocks = payload.terrain.blocks;
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (blocks[i] > 0)
                        solid++;
                    if (blocks[i] > maxBlocks)
                        maxBlocks = blocks[i];
                    if (blocks[i] < 0)
                        problems.Add(tag + "栅格存在负块数（第 " + i + " 格）");
                }

                if (solid == 0)
                    problems.Add(tag + "逻辑高度场全是空格（没有可站地面）");

                float top = maxBlocks * payload.terrain.blockWorldHeight;
                if (top > MaxTerrainTopWorldY)
                    problems.Add(tag + "最高顶 " + top + "u 超过上限 " + MaxTerrainTopWorldY + "u");
            }

            if (payload.units.Count == 0)
                problems.Add(tag + "没有出战单位");

            int red = 0, blue = 0;
            for (int i = 0; i < payload.units.Count; i++)
            {
                LevelUnit unit = payload.units[i];
                if (unit.teamIndex == 0)
                    red++;
                else
                    blue++;

                if (string.IsNullOrEmpty(unit.typeName))
                    problems.Add(tag + "单位 #" + i + " 缺 typeName");
                else if (unit.teamIndex != CrewCatalog.TeamIndexOf(unit.typeName))
                {
                    problems.Add(tag + "单位 #" + i + " " + unit.typeName + " 的队伍 " + unit.teamIndex
                        + " 与导出符号的队伍归属 " + CrewCatalog.TeamIndexOf(unit.typeName) + " 不一致");
                }

                if (unit.luck <= 0)
                    problems.Add(tag + "单位 #" + i + " (" + unit.typeName + ") luck=" + unit.luck + " 非法");

                // 站位必须在实心格上（R2 引申：开局悬空/落水等于开局就是靶子）。
                int cellX = unit.gridX, cellY = unit.gridY;
                if (payload.terrain.IsWellFormed)
                {
                    if (cellX < 0 || cellY < 0 || cellX >= payload.widthTiles || cellY >= payload.depthTiles)
                    {
                        problems.Add(tag + "单位 #" + i + " (" + unit.typeName + ") 落在场外格 ("
                            + cellX + "," + cellY + ")");
                    }
                    else if (payload.terrain.blocks[cellX + cellY * payload.widthTiles] <= 0)
                    {
                        problems.Add(tag + "单位 #" + i + " (" + unit.typeName + ") 站在空格 ("
                            + cellX + "," + cellY + ") 上（会开局坠落）");
                    }
                }

                problems.AddRange(WeaponProblems(tag, "单位 #" + i + " (" + unit.typeName + ") 初始武器",
                    unit.initialWeapons));
            }

            if (red == 0 || blue == 0)
                problems.Add(tag + "红队 " + red + " 人 / 蓝队 " + blue + " 人（双方都必须有人）");

            problems.AddRange(WeaponProblems(tag, "空投池", payload.airdropPool));

            // 摆件：件 id 必须在已登记的枚举范围内；实例名非空。
            for (int i = 0; i < payload.bakedPieces.Count; i++)
            {
                BakedPieceEntry piece = payload.bakedPieces[i];
                if (!System.Enum.IsDefined(typeof(SceneArt.ShowcasePieceId), piece.pieceId))
                    problems.Add(tag + "摆件 #" + i + " 的 pieceId " + piece.pieceId + " 未在 ShowcasePieceId 登记");
                if (string.IsNullOrEmpty(piece.instanceName))
                    problems.Add(tag + "摆件 #" + i + " 缺 instanceName");
            }

            return problems;
        }

        static int BlockCount(LevelAssetPayload payload)
        {
            return payload.terrain.blocks == null ? 0 : payload.terrain.blocks.Count;
        }
    }
}
