using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图远景特征放置规则（纯 C#，无头可测）：把 <see cref="WorldMapDefinition.HorizonFeatures"/>
    /// 列出的本图专属远景件沿「地平线环带」确定性散布。
    ///
    /// 【为什么存在】WorldMapKit.cs 定义的 HorizonSeed/HorizonFeatures 此前全工程零消费者——
    /// 每图「专属远景签名」（鲸 / 触手 / 巨肋…）从未落地，8 图俯视同质化。本类是其唯一规则层，
    /// 装配由 <see cref="WorldMapComposer"/> 调 <c>Place</c> 后复用 kit 摆放路径完成。
    ///
    /// 【环带口径】以图心 (SpanX/2, SpanZ/2) 为圆心、半径 <see cref="InnerRadius"/>..<see cref="OuterRadius"/>
    /// 的环带（docs/M4-设计理念与实现档案.md §1.2「地平线永远有内容」）。依据：相机全景档距图心
    /// ≤160u、55° 俯角，可见海面斜距 ~350u → 200–280u 环带在画面内且脱离玩法区（最大图 280×280
    /// 的对角半径 ≈198u &lt; 内界，地形件永远进不了环带）；雾距已修 150→1200，环带处雾含量低，件真的可见。
    ///
    /// 【确定性】随机全部来自 <c>System.Random(map.HorizonSeed)</c>（同 seed 同布局，可测可复现；
    /// 禁用 UnityEngine.Random——它受全局状态/种子影响，破坏「同一张图永远同一片天际线」）。
    ///
    /// 【朝向】Horizon kit FBX 的正面朝 Unity 本地 +Z（tools/blender/scene/horizon/horizon_kit.py
    /// 坐标口径），故面向图心 = yaw = 方位角 + 180°（±抖动）。远景是剪影件，精确朝向不关键，
    /// 抖动让同类件不排成刻板的一圈。
    ///
    /// 【高度语义（每类默认，【提案/待定】观感终审走实拍轮】Horizon kit 建模口径为
    /// 「局部 0 = 水面、底部已含吃水」（鲸轴基 -2.9 / 肋腿插水 / 触手底圈骑水），
    /// 因此海洋件默认 y = <see cref="LevelGeometry.WaterSurfaceY"/>（模型局部 0 对齐静水面，
    /// 1:1 还原 Blender 设计吃水）；鲸再下沉一点做「半浸」；云带抬高脱离海面。
    /// </summary>
    public static class HorizonFeatureRules
    {
        /// <summary>环带内界半径（u，距图心）。【提案/待定】按全景机位可见斜距 ~350u 的 6 成取整。</summary>
        public const float InnerRadius = 200f;

        /// <summary>环带外界半径（u）。【提案/待定】可见斜距 ~350u 留 70u 余量收边。</summary>
        public const float OuterRadius = 280f;

        /// <summary>环带一圈远景件总量上限（含手摆 Horizon 避让目标不算在内）。克制纪律：远景是幕布，别喧宾夺主。</summary>
        public const int MaxTotalInstances = 12;

        /// <summary>单类特征件的放置尝试次数：避让冲突时重摇位置的上限（确定性——重摇继续消费同一 rng）。</summary>
        const int MaxPlacementTries = 8;

        /// <summary>面向图心的 yaw 抖动半幅（度）。剪影件「大致面向」即可。【提案/待定】</summary>
        public const float FaceCenterYawJitter = 10f;

        /// <summary>单个特征件的放置规格（footprint = 防重叠占用半径，u）。数值【提案/待定】。</summary>
        public struct FeatureSpec
        {
            public readonly string Asset;
            /// <summary>该类默认实例数（克制默认：低矮剪影 2、巨构 1）。</summary>
            public readonly int DefaultCount;
            /// <summary>足印占用半径（u）：取建模长宽的一半 + 余量，件间距 = 两件 footprint 之和。</summary>
            public readonly float FootprintRadius;
            /// <summary>摆放根 y（世界系）。理由见各类注释。</summary>
            public readonly float PositionY;

            public FeatureSpec(string asset, int defaultCount, float footprintRadius, float positionY)
            {
                Asset = asset;
                DefaultCount = defaultCount;
                FootprintRadius = footprintRadius;
                PositionY = positionY;
            }
        }

        // ------------------------------------------------------------------
        // 规格表（与 tools/blender/scene/horizon/horizon_kit.py 的八件资产一一对应）
        // ------------------------------------------------------------------

        /// <summary>水面件统一基准：模型局部 0 对齐静水面（kit 建模底部已含吃水，见类头）。</summary>
        public static float WaterLevelY => LevelGeometry.WaterSurfaceY;

        /// <summary>
        /// 全部已知远景特征规格（asset 名 = Horizon kit FBX 名）：
        ///   WhaleSurfacing     长 25、背弧出水 4 → 2 件（低剪影，双鲸呼应）；
        ///     y = 水面 - 0.8：建模出水 4 的基础上再下沉 0.8 做「半浸」，剪影更含蓄。【提案/待定】
        ///   LeviathanTentacle  高 18、底圈骑水 → 1 件（18u 高耸剪影一件即可，多了喧宾夺主）；
        ///     y = 水面：建模底圈已骑水面，原样对齐。
        ///   GiantRibs          宽 40 巨肋拱 → 1 件（巨构签名，独一号）；
        ///     y = 水面：建模肋腿已插水（cz=-4.2、弧角超半圆）。
        ///   FarFleet           3 艘一组的远帆船队 → 1 件；y = 水面（船底 -0.9 已含吃水）。
        ///   CloudBankL         80×20×8 云带 → 2 件；y = 水面 + 14 抬到低空（与海面件分层，
        ///     相机高 ~131u 下 240u 外云顶仍入画）。【提案/待定】
        ///   DistantIsleS/M/L   远岛三档 → 各 1 件；y = 水面（z_bot -2.5/-3 已插水）。
        /// </summary>
        public static readonly FeatureSpec[] Specs =
        {
            new FeatureSpec("WhaleSurfacing",     2, 14f, LevelGeometry.WaterSurfaceY - 0.8f),
            new FeatureSpec("LeviathanTentacle",  1, 10f, LevelGeometry.WaterSurfaceY),
            new FeatureSpec("GiantRibs",          1, 24f, LevelGeometry.WaterSurfaceY),
            new FeatureSpec("FarFleet",           1, 16f, LevelGeometry.WaterSurfaceY),
            new FeatureSpec("CloudBankL",         2, 42f, LevelGeometry.WaterSurfaceY + 14f),
            new FeatureSpec("DistantIsleS",       1, 22f, LevelGeometry.WaterSurfaceY),
            new FeatureSpec("DistantIsleM",       1, 48f, LevelGeometry.WaterSurfaceY),
            new FeatureSpec("DistantIsleL",       1, 92f, LevelGeometry.WaterSurfaceY),
        };

        /// <summary>未登记特征件的兜底占用半径（u）——只影响避让距离，不产生实例。</summary>
        const float UnknownFootprintRadius = 30f;

        static readonly Dictionary<string, FeatureSpec> _specIndex = BuildSpecIndex();

        static Dictionary<string, FeatureSpec> BuildSpecIndex()
        {
            var index = new Dictionary<string, FeatureSpec>(Specs.Length);
            for (int i = 0; i < Specs.Length; i++)
                index[Specs[i].Asset] = Specs[i];
            return index;
        }

        /// <summary>特征名是否已登记规格（catalog 里出现 kit 没有的名字时装配侧会静默跳过，测试用它守门）。</summary>
        public static bool IsKnownFeature(string featureName) => _specIndex.ContainsKey(featureName);

        /// <summary>一个远景特征件的决定性摆放结果（纯数据，无 GameObject 依赖）。</summary>
        public struct HorizonFeaturePlacement
        {
            public readonly string Asset;
            public readonly Vector3 Position;
            public readonly float YawDeg;

            public HorizonFeaturePlacement(string asset, Vector3 position, float yawDeg)
            {
                Asset = asset;
                Position = position;
                YawDeg = yawDeg;
            }
        }

        /// <summary>避让占位（特征件之间、特征件与手摆 Horizon 件之间共用）。</summary>
        readonly struct Occupant
        {
            public readonly Vector2 Center;
            public readonly float Footprint;

            public Occupant(Vector2 center, float footprint)
            {
                Center = center;
                Footprint = footprint;
            }
        }

        /// <summary>
        /// 对一张图求全部远景特征件的确定性摆放。
        /// 每类实例数 = <see cref="FeatureSpec.DefaultCount"/>；拉平后沿环带均分角度段（±40% 段宽抖动）、
        /// 半径在环带内均匀抖动、朝向图心 ±10°；与已放件（含手摆 Horizon）距离 &lt; 两件足印之和时
        /// 重摇（最多 <see cref="MaxPlacementTries"/> 次，耗尽则放最后候选——环带弧长 ~1500u、
        /// 现役 ≤6 件，兜底分支实际不可达，且不放弃放置以保证「每类至少一件」的签名完整性）。
        /// </summary>
        public static List<HorizonFeaturePlacement> Place(WorldMapDefinition map)
        {
            var result = new List<HorizonFeaturePlacement>();
            if (map == null || map.HorizonFeatures == null || map.HorizonFeatures.Count == 0)
                return result;

            // 1) 拉平实例清单：catalog 特征顺序 × 每类默认数（未知类名跳过——不硬造 kit 里没有的件）。
            var pending = new List<FeatureSpec>();
            int total = 0;
            for (int f = 0; f < map.HorizonFeatures.Count; f++)
            {
                if (_specIndex.TryGetValue(map.HorizonFeatures[f], out FeatureSpec spec))
                {
                    pending.Add(spec);
                    total += spec.DefaultCount;
                }
            }
            if (total == 0)
                return result;
            if (total > MaxTotalInstances)
                total = MaxTotalInstances; // 上限裁剪：超过时按清单顺序保留前件

            // 2) 占位集：手摆 Horizon 件先入表（特征件要避开它们；footprint 未登记用兜底值）。
            var occupied = new List<Occupant>(map.Horizon.Count + total);
            for (int i = 0; i < map.Horizon.Count; i++)
            {
                WorldKitPlacement placed = map.Horizon[i];
                float footprint = _specIndex.TryGetValue(placed.Asset, out FeatureSpec spec)
                    ? spec.FootprintRadius
                    : UnknownFootprintRadius;
                occupied.Add(new Occupant(
                    new Vector2(placed.Position.x, placed.Position.z), footprint));
            }

            // 3) 确定性散布（System.Random(seed)：同 seed 两次调用产出逐位一致）。
            var rng = new System.Random(map.HorizonSeed);
            float cx = map.SpanX * 0.5f, cz = map.SpanZ * 0.5f;
            float seg = 360f / total;
            int emitted = 0;

            for (int p = 0; p < pending.Count && emitted < total; p++)
            {
                FeatureSpec item = pending[p];
                for (int n = 0; n < item.DefaultCount && emitted < total; n++)
                {
                    float angle = 0f, radius = 0f;
                    Vector2 spot = Vector2.zero;
                    for (int tryIndex = 0; tryIndex < MaxPlacementTries; tryIndex++)
                    {
                        // 均分段 ±40% 抖动：同类件天然错开，整圈不出现刻板等距，也不会挤成一坨。
                        angle = emitted * seg + ((float)rng.NextDouble() * 2f - 1f) * seg * 0.4f;
                        radius = InnerRadius + (float)rng.NextDouble() * (OuterRadius - InnerRadius);
                        // 方位角 bearing 的单位向量 = (sin, cos)（与 Unity yaw 口径一致）。
                        spot = new Vector2(cx + Mathf.Sin(angle * Mathf.Deg2Rad) * radius,
                                           cz + Mathf.Cos(angle * Mathf.Deg2Rad) * radius);
                        if (!Conflicts(occupied, spot, item.FootprintRadius))
                            break;
                    }

                    occupied.Add(new Occupant(spot, item.FootprintRadius));
                    // 面向图心：本地 +Z 是件正面，指向图心 = bearing + 180°，±10° 让剪影不排排坐。
                    float yaw = angle + 180f + ((float)rng.NextDouble() * 2f - 1f) * FaceCenterYawJitter;
                    result.Add(new HorizonFeaturePlacement(
                        item.Asset, new Vector3(spot.x, item.PositionY, spot.y), yaw));
                    emitted++;
                }
            }
            return result;
        }

        /// <summary>候选点与任一占位的中心距是否小于两件足印之和（防重叠）。</summary>
        static bool Conflicts(List<Occupant> occupied, Vector2 spot, float footprint)
        {
            for (int i = 0; i < occupied.Count; i++)
            {
                float minDist = occupied[i].Footprint + footprint;
                if ((occupied[i].Center - spot).sqrMagnitude < minDist * minDist)
                    return true;
            }
            return false;
        }

        /// <summary>指定特征类的足印占用半径（未登记返回兜底值）。</summary>
        public static float FootprintOf(string assetName) =>
            _specIndex.TryGetValue(assetName, out FeatureSpec spec)
                ? spec.FootprintRadius
                : UnknownFootprintRadius;
    }
}
