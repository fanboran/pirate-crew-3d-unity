using System.Collections.Generic;
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
        /// <summary>逻辑高度场的最高允许顶面（米）。超过视为数据错误（高度填错一位就会命中）。</summary>
        public const float MaxTerrainTopWorldY = 40f;

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
        // 关卡资产
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
            if (payload.sizeX <= 0f || payload.sizeZ <= 0f)
                problems.Add(tag + "场地尺幅非法 " + payload.sizeX + "m × " + payload.sizeZ + "m");

            // 水位是全局常量（落水即死，§4.4）：资产里那一栏必须与工程常量同值，免得两边各记一份。
            if (System.Math.Abs(payload.waterWorldY - LevelGeometry.WaterSurfaceY) > 1e-4f)
            {
                problems.Add(tag + "水面 Y " + payload.waterWorldY
                    + "m 与全局 LevelGeometry.WaterSurfaceY=" + LevelGeometry.WaterSurfaceY + "m 不一致");
            }

            // 采样格尺寸必须与运行时地形粒度一致（两个常量分处 Data / Battle 程序集，靠这条盯住）。
            if (System.Math.Abs(LevelAssetSchema.RasterCellSize - LevelGeometry.SampleCellSize) > 1e-4f)
            {
                problems.Add(tag + "采样格尺寸 " + LevelAssetSchema.RasterCellSize
                    + "m 与运行时 LevelGeometry.SampleCellSize=" + LevelGeometry.SampleCellSize + "m 不一致");
            }
            else
            {
                foreach (float span in new[] { payload.sizeX, payload.sizeZ })
                {
                    if (System.Math.Abs(LevelAssetSchema.RasterCells(span) * LevelAssetSchema.RasterCellSize - span) > 1e-4f)
                        problems.Add(tag + "场地尺幅 " + span + "m 不是采样格尺寸 "
                            + LevelAssetSchema.RasterCellSize + "m 的整数倍（会静默四舍五入）");
                }
            }

            // 单一岛形语义：结构自洽 + 块高与工程常量一致。
            if (!payload.terrain.IsWellFormed)
            {
                problems.Add(tag + "岛形地形不自洽（岛列表为空，或存在顶面高度非正 / 顶点数非法的岛）");
            }
            else
            {
                float expectedBlock = LevelGeometry.BlockWorldHeight;
                if (System.Math.Abs(payload.terrain.blockWorldHeight - expectedBlock) > 1e-4f)
                {
                    problems.Add(tag + "块高 " + payload.terrain.blockWorldHeight
                        + " 与 LevelGeometry.BlockWorldHeight=" + expectedBlock + " 不一致");
                }

                for (int i = 0; i < payload.terrain.islands.Count; i++)
                {
                    TerrainIsland island = payload.terrain.islands[i];
                    for (int k = 0; k + 1 < island.outline.Count; k += 2)
                    {
                        if (island.outline[k] < 0f || island.outline[k] > payload.sizeX
                            || island.outline[k + 1] < 0f || island.outline[k + 1] > payload.sizeZ)
                        {
                            problems.Add(tag + "岛 #" + i + " 的轮廓顶点 ("
                                + island.outline[k] + ", " + island.outline[k + 1] + ")m 落在场地外");
                        }
                    }
                    HeightGridProblems(payload, island, i, problems, tag);
                }

                List<float> heights = TerrainShapeRasterizer.ToHeights(payload.terrain, payload.sizeX, payload.sizeZ);
                int solid = 0;
                float maxHeight = 0f;
                for (int i = 0; i < heights.Count; i++)
                {
                    if (heights[i] > 0f)
                        solid++;
                    if (heights[i] > maxHeight)
                        maxHeight = heights[i];
                }

                if (solid == 0)
                    problems.Add(tag + "地形没有可站地面（岛形全部落在采样格外？）");

                if (maxHeight > MaxTerrainTopWorldY)
                    problems.Add(tag + "最高顶 " + maxHeight + "m 超过上限 " + MaxTerrainTopWorldY + "m");
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

                // 站位必须在实心地面上（R2 引申：开局悬空/落水等于开局就是靶子）。
                if (payload.terrain.IsWellFormed)
                {
                    if (unit.x < 0f || unit.z < 0f || unit.x >= payload.sizeX || unit.z >= payload.sizeZ)
                    {
                        problems.Add(tag + "单位 #" + i + " (" + unit.typeName + ") 落在场外 ("
                            + unit.x + ", " + unit.z + " 米)");
                    }
                    else if (!TerrainShapeRasterizer.PointOnGround(payload.terrain, unit.x, unit.z))
                    {
                        problems.Add(tag + "单位 #" + i + " (" + unit.typeName + ") 站在空地 ("
                            + unit.x + ", " + unit.z + ") 米上（会开局坠落）");
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

        /// <summary>
        /// 岛逐格高度（v4 类体素）的问题清单：结构（长度 = cols×rows，IsWellFormed 兜底）、
        /// 覆盖（网格必须盖住轮廓包围盒——漏盖的格运行时只能平顶兜底，数据即错）、
        /// 值域（0 ≤ 值 ≤ 上限；岛内格不许是 0——那是数据洞，岛内必有地面）。
        /// </summary>
        static void HeightGridProblems(LevelAssetPayload payload, TerrainIsland island, int index,
            List<string> problems, string tag)
        {
            if (!island.HasHeights)
                return;

            if (island.heightCols <= 0 || island.heightRows <= 0
                || island.heights.Count != island.heightCols * island.heightRows)
            {
                problems.Add(tag + "岛 #" + index + " 逐格高度结构非法：cols=" + island.heightCols
                    + " rows=" + island.heightRows + " 数组长=" + island.heights.Count
                    + "（应 cols×rows；结构问题同时会让 IsWellFormed 失败）");
                return;
            }

            // 覆盖：网格 [col0·cell, (col0+cols)·cell) 必须罩住轮廓包围盒。
            float cell = LevelAssetSchema.RasterCellSize;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int k = 0; k + 1 < island.outline.Count; k += 2)
            {
                minX = System.Math.Min(minX, island.outline[k]);
                maxX = System.Math.Max(maxX, island.outline[k]);
                minZ = System.Math.Min(minZ, island.outline[k + 1]);
                maxZ = System.Math.Max(maxZ, island.outline[k + 1]);
            }
            if (island.heightCol0 * cell > minX + 1e-4f
                || (island.heightCol0 + island.heightCols) * cell < maxX - 1e-4f
                || island.heightRow0 * cell > minZ + 1e-4f
                || (island.heightRow0 + island.heightRows) * cell < maxZ - 1e-4f)
            {
                problems.Add(tag + "岛 #" + index + " 逐格高度网格（col " + island.heightCol0 + ".."
                    + (island.heightCol0 + island.heightCols) + " / row " + island.heightRow0 + ".."
                    + (island.heightRow0 + island.heightRows) + "）未盖住轮廓包围盒 ("
                    + minX + ".." + maxX + ", " + minZ + ".." + maxZ + ")——漏盖的格运行时只能平顶兜底");
            }

            // 值域 + 岛内数据洞：只查岛自己的网格，轮廓外的包围盒角落允许 0（那里本来就不是岛）。
            for (int row = 0; row < island.heightRows; row++)
            {
                float cz = (island.heightRow0 + row + 0.5f) * cell;
                for (int col = 0; col < island.heightCols; col++)
                {
                    float value = island.heights[col + row * island.heightCols];
                    if (value < 0f || value > MaxTerrainTopWorldY)
                    {
                        problems.Add(tag + "岛 #" + index + " 逐格高度值 " + value
                            + " 超出 [0, " + MaxTerrainTopWorldY + "]（col " + (island.heightCol0 + col)
                            + " row " + (island.heightRow0 + row) + "）");
                        continue;
                    }
                    if (value <= 0f)
                    {
                        float cx = (island.heightCol0 + col + 0.5f) * cell;
                        if (TerrainShapeRasterizer.PointInIsland(cx, cz, island))
                        {
                            problems.Add(tag + "岛 #" + index + " 岛内有逐格高度为 0 的数据洞（col "
                                + (island.heightCol0 + col) + " row " + (island.heightRow0 + row)
                                + "：格中心在轮廓内但高度为 0，该格会变成无地面）");
                        }
                    }
                }
            }
        }

    }
}
