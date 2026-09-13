using System.Collections.Generic;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Lowpoly
{
    /// <summary>
    /// 山包大岛的尺寸参数（纯 C#，无头可测）。
    ///
    /// 【已废弃（2026-09-14 规格变更）】第 3 关改为超美空岛云场，山包不再接线——
    /// 本文件与 <see cref="HillIslandGeometry"/> 仅保留编译，勿在新装配流程调用。
    ///
    ///
    /// 【坐标口径】XZ 水平竞技场、重力 -Y；水面 y=-0.4（<c>LevelGeometry.WaterSurfaceY</c> 现值为准）。
    /// 岛基座在水下延伸到 BaseY=-1.7 → 水线（-0.4）穿过暖岩层，入水线可见。
    /// 山顶台面 y=4.8、外接半径 9.0 → 十边形台面内接正方形约 12.6×12.6 ≥ 需求的 12×12（够摆两支小队）。
    /// </summary>
    public sealed class HillIslandSpec
    {
        /// <summary>确定性种子（岩层抖动与水线散石；台面环不抖，保证 12×12）。</summary>
        public int Seed = 26091402;

        /// <summary>岛中心（XZ；y 忽略）。默认竞技场原点。</summary>
        public Vector3 Center = Vector3.zero;

        /// <summary>基座底面 Y（水下延伸，低于水面 -0.4 约 1.3）。</summary>
        public float BaseY = -1.7f;

        /// <summary>暖岩层顶面 Y（入水线一带往上是草）。</summary>
        public float RockTopY = 0.9f;

        /// <summary>草绿层顶面 Y。</summary>
        public float GrassTopY = 3.4f;

        /// <summary>山顶台面 Y（顶层草绿缓坡到顶 + 沙色平顶）。</summary>
        public float TopY = 4.8f;

        /// <summary>基座外接半径（竞技场里的大岛体量）。</summary>
        public float BaseHalf = 15.5f;

        /// <summary>顶层外接半径（台面尺寸的权威：9.0 → 内接 12×12）。</summary>
        public float TopHalf = 9f;

        /// <summary>每层环段数（低模硬边；10 段在 45° 俯视下已读得出棱面节奏）。</summary>
        public int Segments = 10;

        /// <summary>水线散石数量（打破基座与水面的交线）。</summary>
        public int ShoreRockCount = 6;

        /// <summary>交付构图。</summary>
        public static HillIslandSpec Default => new HillIslandSpec();

        /// <summary>同构图、换种子。</summary>
        public static HillIslandSpec FromSeed(int seed)
        {
            return new HillIslandSpec { Seed = seed };
        }
    }

    /// <summary>
    /// 山包大岛几何（纯 C#，无头可测）：单个大岛 = 分层圆台堆叠（3 层棱带 + 沙色平顶）——
    /// 底层暖岩（入水）、中层草绿、顶层草绿偏亮、台面沙色；每层侧面是低模硬边斜坡。
    ///
    /// 【已废弃（2026-09-14 规格变更）】山包关卡取消（只要空岛云场）：
    /// 不再投入时间，勿在装配器调用 <c>Compose</c>/<c>SpawnPoints</c>；实现保留备查。
    ///
    /// 【碰撞不在本层】装配层（<see cref="LowpolyStageBuilder"/>）对各材质槽网格挂
    /// 静态非凸 MeshCollider（分层棱台是静态凸/近凸壳，PhysX 静态碰撞稳；
    /// 台面本身就是网格平顶，角色站得住、弹体打得响）。
    /// </summary>
    public static class HillIslandGeometry
    {
        /// <summary>山顶台面世界 Y（出生点/相机取景用）。</summary>
        public static float TopSurfaceY(HillIslandSpec spec)
        {
            return (spec ?? HillIslandSpec.Default).TopY;
        }

        /// <summary>合成整座山包（幂等：同 spec 必得同一几何）。</summary>
        public static void Compose(LowpolyBuffers buffers, HillIslandSpec spec)
        {
            if (buffers == null)
                return;
            if (spec == null)
                spec = HillIslandSpec.Default;

            int seg = Mathf.Max(6, spec.Segments);
            Vector3 c = new Vector3(spec.Center.x, 0f, spec.Center.z);

            // 各层半径（外接）：底层最大，往上收窄，顶层近直筒（台面平）。
            float rBase = spec.BaseHalf;
            float rRockTop = Mathf.Lerp(spec.BaseHalf, spec.TopHalf, 0.42f);   // 暖岩层顶 ≈ 11.5
            float rGrassTop = Mathf.Lerp(spec.BaseHalf, spec.TopHalf, 0.76f);  // 草绿层顶 ≈ 10.1
            float rTop = spec.TopHalf;

            // 1. 暖岩层：BaseY → RockTopY（水下基座 + 入水线）。逐顶点径向抖动 ±5%。
            AddBand(buffers.HillRockWarm, c, seg, spec.BaseY, spec.RockTopY,
                rBase, rRockTop, spec.Seed, 11, 0.05f, 0.05f, 0f);

            // 2. 草绿层：RockTopY → GrassTopY。
            AddBand(buffers.HillGrassMid, c, seg, spec.RockTopY, spec.GrassTopY,
                rRockTop, rGrassTop, spec.Seed, 13, 0.05f, 0.03f, Mathf.PI / seg);

            // 3. 顶层草绿偏亮：GrassTopY → TopY（缓坡收上台面；顶环不抖 → 台面规整）。
            AddBand(buffers.HillGrassTop, c, seg, spec.GrassTopY, spec.TopY,
                rGrassTop, rTop, spec.Seed, 17, 0.03f, 0f, Mathf.PI / seg);

            // 4. 沙色台面：平顶圆盘，半径取顶环内切半径（与棱面严丝合缝，不戳出棱外）。
            float apothem = rTop * Mathf.Cos(Mathf.PI / seg);
            buffers.HillSand.AddDisc(new Vector3(c.x, spec.TopY, c.z), apothem, seg, Vector3.up, false);

            // 5. 水线散石：6 块暖岩色砾石骑在入水线上，打散"圆锥杯"轮廓。
            for (int i = 0; i < Mathf.Max(0, spec.ShoreRockCount); i++)
            {
                float ang = Mathf.PI * 2f * i / Mathf.Max(1, spec.ShoreRockCount)
                    + LowpolyHash.SignedHash(spec.Seed, i, 31) * 0.5f;
                float r = rBase * Mathf.Lerp(0.86f, 1.0f, LowpolyHash.Hash01(spec.Seed, i, 33));
                float rockR = 0.8f + 1.1f * LowpolyHash.Hash01(spec.Seed, i, 37);
                Vector3 rc = new Vector3(
                    c.x + Mathf.Cos(ang) * r,
                    -0.1f + 0.5f * LowpolyHash.Hash01(spec.Seed, i, 39),
                    c.z + Mathf.Sin(ang) * r);
                buffers.HillRockWarm.AddRock(rc, rockR, new Vector3(1.15f, 0.7f, 1.0f),
                    spec.Seed + i * 53, 6);
            }
        }

        /// <summary>
        /// 一层棱带（低模圆台侧面 + 上下封盖）：底/顶环逐顶点径向抖动 ±<paramref name="jitterTop"/>，
        /// 整环偏转 <paramref name="yawOffset"/> 让相邻层棱线错开（"砖缝"读法）。
        /// </summary>
        static void AddBand(MeshBuffers target, Vector3 center, int segments,
            float yBottom, float yTop, float rBottom, float rTop, int seed, int salt,
            float jitterBottom, float jitterTop, float yawOffset)
        {
            var bottom = new Vector3[segments];
            var top = new Vector3[segments];

            for (int i = 0; i < segments; i++)
            {
                float a = yawOffset + Mathf.PI * 2f * i / segments;
                float jb = 1f + jitterBottom * LowpolyHash.SignedHash(seed, i, salt);
                float jt = 1f + jitterTop * LowpolyHash.SignedHash(seed, i, salt + 1);
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                bottom[i] = new Vector3(center.x + ca * rBottom * jb, yBottom, center.z + sa * rBottom * jb);
                top[i] = new Vector3(center.x + ca * rTop * jt, yTop, center.z + sa * rTop * jt);
            }

            // 侧面（绕序仿 MeshBuffers.AddFrustum：底_s → 顶_s → 顶_t → 底_t，外法线指向径向）。
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                Vector3 mid = (bottom[i] + top[i] + top[j] + bottom[j]) * 0.25f;
                Vector3 radial = new Vector3(mid.x - center.x, 0f, mid.z - center.z);
                target.AddQuad(bottom[i], top[i], top[j], bottom[j],
                    radial.sqrMagnitude > 1e-10f ? radial.normalized : Vector3.right);
            }

            // 底盖（-Y，法线朝下；岛底在水下也封住，避免仰视穿帮）。
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                target.AddTriangle(new Vector3(center.x, yBottom, center.z), bottom[i], bottom[j]);
            }

            // 顶盖（+Y）：上层棱带会盖住大部分，但沿内切一圈仍有露缝，封上保险。
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                target.AddTriangle(new Vector3(center.x, yTop, center.z), top[j], top[i]);
            }
        }

        // ==================================================================
        // 出生点（山包关）
        // ==================================================================

        /// <summary>
        /// 山顶出生点建议表（8 个 = 两支 4 人小队，脚底贴地坐标，**落角色时另加 UnitPivotHeight**）：
        /// 台面（内接 ≈12.6×12.6）上半径 4.6 的圆环均布——两队各占半环，环内距台沿 ≥4 单位。
        /// </summary>
        public static List<LowpolySpawnPoint> SpawnPoints(HillIslandSpec spec)
        {
            if (spec == null)
                spec = HillIslandSpec.Default;

            float r = Mathf.Min(4.6f, spec.TopHalf * 0.5f);
            var spawns = new List<LowpolySpawnPoint>(8);
            for (int i = 0; i < 8; i++)
            {
                float ang = Mathf.PI * 2f * i / 8f;
                string team = i < 4 ? "team0" : "team1";
                spawns.Add(new LowpolySpawnPoint(
                    new Vector3(spec.Center.x + Mathf.Cos(ang) * r, spec.TopY,
                        spec.Center.z + Mathf.Sin(ang) * r),
                    "HillTop " + i + " (" + team + ")"));
            }
            return spawns;
        }
    }
}
