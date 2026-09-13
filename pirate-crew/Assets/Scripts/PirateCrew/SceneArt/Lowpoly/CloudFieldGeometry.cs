using System.Collections.Generic;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Lowpoly
{
    /// <summary>
    /// 云朵平台场的尺寸/布局参数（纯 C#，无头可测）。
    ///
    /// 【坐标口径】XZ 水平竞技场、重力 -Y；竞技场地面 y=0、水面 y=-0.4
    /// （<c>LevelGeometry.WaterSurfaceY</c> 现值为准）。云朵是**悬空平台**：
    /// 顶面（可站位）在 y 2.5-8.5 一带，底面削平，角色掉下云即落水（全局落水即死规则）。
    ///
    /// 【布局（手工骨架 + 零布局抖动 → 出生点表可精确写死）】
    ///   中心 1 朵主角云（12×12 台面，够摆 4 个角色）+ 第一环 4 朵（半径 6.5）+
    ///   第二环 6 朵（半径 11.5，交错取位）+（数量>11 时）第三环 3 朵（半径 15.5）。
    ///   相邻云水平边距约 2-4 单位（中心距减两侧半宽）、高低差 2-5 单位，可跳跃/可投掷到达。
    /// 有机感全部交给几何层的逐顶点抖动，布局本身是**确定的手工表**。
    /// </summary>
    public sealed class CloudFieldSpec
    {
        /// <summary>确定性种子：同种子必得同一片云场（blob 形状抖动用；布局不抖）。</summary>
        public int Seed = 26091401;

        /// <summary>云朵数量（钳制 8-14）。</summary>
        public int CloudCount = 11;

        /// <summary>云场中心（XZ；y 忽略）。默认竞技场原点。</summary>
        public Vector3 ArenaCenter = Vector3.zero;

        /// <summary>主角云下标（0）：台面 ≥12×12，摆两支小队的主力平台。</summary>
        public int HeroCloudIndex = 0;

        /// <summary>主角云台面半宽（6 → 12×12 ≥ 需求的 10×10）。</summary>
        public float HeroTopHalf = 6f;

        /// <summary>主角云顶面世界 Y（第一环各云以此为基准高低错落）。</summary>
        public float HeroTopY = 4.5f;

        /// <summary>云朵数量上限保护（需求 8-14）。</summary>
        public const int MinClouds = 8;
        public const int MaxClouds = 14;

        /// <summary>交付构图。</summary>
        public static CloudFieldSpec Default => new CloudFieldSpec();

        /// <summary>同构图、换种子。</summary>
        public static CloudFieldSpec FromSeed(int seed)
        {
            return new CloudFieldSpec { Seed = seed };
        }
    }

    /// <summary>一朵云的平台数据（布局输出，纯数据；坐标全部世界系）。</summary>
    public sealed class CloudPlatformData
    {
        /// <summary>云台面中心（XZ 用；y 恒 0，高度看 <see cref="TopY"/>）。</summary>
        public Vector3 CenterXZ;

        /// <summary>顶面（可站位面）世界 Y —— 也是碰撞盒顶面。</summary>
        public float TopY;

        /// <summary>底面（削平）世界 Y。</summary>
        public float BaseY;

        /// <summary>台面半宽（X）。</summary>
        public float TopHalfWidth;

        /// <summary>台面半深（Z）。</summary>
        public float TopHalfDepth;

        /// <summary>是否主角云（≥12×12 台面）。</summary>
        public bool IsHero;

        /// <summary>台面可站面积估算（㎡ / 平方单位）。</summary>
        public float StandArea => 4f * TopHalfWidth * TopHalfDepth;
    }

    /// <summary>
    /// 云朵平台场几何（纯 C#，无头可测）：一朵低模云 = 3-6 个融合的低分段球体
    /// （UV 球 5 环 × 7-9 段），拉扁（squash）、上下削平（站位顶面 / 平底），
    /// 按"每云局部高度阈值"把三角面拆进 暖白/淡金 两个材质槽（低模双色分带 = 按高度 lerp 的低成本实现）。
    ///
    /// 【碰撞不在本层】本类只写几何与布局数据；BoxCollider 由
    /// <see cref="LowpolyStageBuilder"/> 按 <see cref="CloudPlatformData"/> 生成
    /// （台面平盒 + 云身粗盒，双盒组合——比非凸 MeshCollider 更稳更便宜，角色必站平盒顶）。
    /// </summary>
    public static class CloudFieldGeometry
    {
        /// <summary>
        /// 布局：确定性云台面表（无随机抖动的手工骨架）。**先调它再 Compose/SpawnPoints**
        /// 或直接用 <see cref="SpawnPoints"/>（内部同样走这里）。
        /// </summary>
        public static List<CloudPlatformData> Layout(CloudFieldSpec spec)
        {
            if (spec == null)
                spec = CloudFieldSpec.Default;

            int count = Mathf.Clamp(spec.CloudCount, CloudFieldSpec.MinClouds, CloudFieldSpec.MaxClouds);
            var list = new List<CloudPlatformData>(count);
            Vector3 c = spec.ArenaCenter;

            // ---- 主角云：中心，12×12 台面 ----
            list.Add(new CloudPlatformData
            {
                CenterXZ = new Vector3(c.x, 0f, c.z),
                TopY = spec.HeroTopY,
                BaseY = spec.HeroTopY - 2.6f,
                TopHalfWidth = spec.HeroTopHalf,
                TopHalfDepth = spec.HeroTopHalf,
                IsHero = true,
            });

            // ---- 第一环 4 朵：半径 6.5，高度 {7.5, 2.5, 6.5, 2.5}（与主角云差 2-5）----
            float[] ring1Top = { 7.5f, 2.5f, 6.5f, 2.5f };
            for (int i = 0; i < 4 && list.Count < count; i++)
            {
                float ang = (90f + i * 90f) * Mathf.Deg2Rad;
                list.Add(Make(list.Count, c, ang, 6.5f, ring1Top[i], 3.2f, 4.0f, false));
            }

            // ---- 第二环 6 朵：半径 11.5，交错取位（数量不足时先取相距最远的槽位）----
            int[] ring2Order = { 0, 3, 1, 4, 2, 5 };
            float[] ring2Top = { 5f, 8f, 4f, 7f, 3f, 6f };
            for (int k = 0; k < 6 && list.Count < count; k++)
            {
                int slot = ring2Order[k];
                float ang = (30f + slot * 60f) * Mathf.Deg2Rad;
                list.Add(Make(list.Count, c, ang, 11.5f, ring2Top[slot], 3.4f, 4.3f, false));
            }

            // ---- 第三环 3 朵（数量 >11 才出现）：半径 15.5 ----
            float[] ring3Top = { 5.5f, 8.5f, 3.5f };
            for (int i = 0; i < 3 && list.Count < count; i++)
            {
                float ang = (20f + i * 120f) * Mathf.Deg2Rad;
                list.Add(Make(list.Count, c, ang, 15.5f, ring3Top[i], 3.0f, 3.8f, false));
            }

            return list;
        }

        /// <summary>按环参数生成一朵云的平台数据（半宽在 [minHalf, maxHalf] 内由下标哈希取值）。</summary>
        static CloudPlatformData Make(int index, Vector3 center, float ang, float radius,
            float topY, float minHalf, float maxHalf, bool isHero)
        {
            float half = Mathf.Lerp(minHalf, maxHalf, LowpolyHash.Hash01(0, index, 71));
            float height = 1.9f + 1.1f * LowpolyHash.Hash01(0, index, 73);
            return new CloudPlatformData
            {
                CenterXZ = new Vector3(center.x + Mathf.Cos(ang) * radius, 0f,
                    center.z + Mathf.Sin(ang) * radius),
                TopY = topY,
                BaseY = topY - height,
                TopHalfWidth = half,
                TopHalfDepth = half * (0.82f + 0.30f * LowpolyHash.Hash01(0, index, 79)),
                IsHero = isHero,
            };
        }

        /// <summary>
        /// 合成整片云场的几何（幂等：同 spec 必得同一几何）。白云/金云按每云高度阈值分槽。
        /// </summary>
        public static void Compose(LowpolyBuffers buffers, CloudFieldSpec spec)
        {
            if (buffers == null)
                return;
            if (spec == null)
                spec = CloudFieldSpec.Default;

            List<CloudPlatformData> platforms = Layout(spec);
            for (int i = 0; i < platforms.Count; i++)
                ComposeCloud(buffers, platforms[i], spec.Seed + i * 131);
        }

        /// <summary>合成一朵云（3-6 个融合 blob：顶盘 + 环绕中球 + 底盘，全部上下削平）。</summary>
        static void ComposeCloud(LowpolyBuffers buffers, CloudPlatformData p, int seed)
        {
            float topY = p.TopY;
            float baseY = p.BaseY;
            float half = Mathf.Max(p.TopHalfWidth, p.TopHalfDepth);
            float r0 = half / 0.9f;   // 顶盘 blob 半径：削平后台面半径 ≈ 0.9R，保证盖住台面

            // 高度分带阈值：55% 以上算"晒到太阳"→ 淡金；以下暖白。
            float goldThreshold = Mathf.Lerp(baseY, topY, 0.55f);

            int blobCount = p.IsHero ? 5 : (half > 3.6f ? 4 : 3);
            var temp = new MeshBuffers();

            // 顶盘：圆心近台面中心，顶削平在 TopY（可站位面的可视面）。
            AddCloudBlob(temp, new Vector3(p.CenterXZ.x, topY - r0 * 0.32f, p.CenterXZ.z),
                r0, 0.55f, 4, p.IsHero ? 9 : 7, seed, 1, baseY, topY);

            // 中层球：绕中心一圈，制造云的起伏体积。
            for (int k = 0; k < blobCount - 2; k++)
            {
                float h1 = LowpolyHash.Hash01(seed, k, 101);
                float h2 = LowpolyHash.Hash01(seed, k, 103);
                float ang = k * 2.399963f + h1 * 1.2f;
                float dist = r0 * 0.42f;
                float radius = r0 * (0.44f + 0.18f * h2);
                Vector3 c = new Vector3(
                    p.CenterXZ.x + Mathf.Cos(ang) * dist,
                    Mathf.Lerp(baseY + 0.5f, topY - 0.6f, h2),
                    p.CenterXZ.z + Mathf.Sin(ang) * dist);
                AddCloudBlob(temp, c, radius, 0.6f, 3, 7, seed, 11 + k, baseY, topY);
            }

            // 底盘：压扁的大球托住整朵云，底面削平（云底齐平的"蛋糕底"读法）。
            AddCloudBlob(temp, new Vector3(p.CenterXZ.x, baseY + 0.7f, p.CenterXZ.z),
                r0 * 0.82f, 0.5f, 3, 7, seed, 19, baseY, topY);

            // 按高度阈值拆进两个材质槽（保留绕序，面法线不变）。
            SplitByHeight(temp, buffers.CloudWarmWhite, buffers.CloudPaleGold, goldThreshold);
        }

        /// <summary>
        /// 低模云团 blob：UV 球（latRings 环 × lonSegments 段 + 上下极点扇帽），
        /// Y 压扁（squash），高于 flatTopY / 低于 flatBottomY 的部分削平（XZ 抖动不动 Y，保平顶平底）。
        /// 逐顶点径向抖动 ±8% 给"揉出来的云"感；每面独立顶点 → flat shading。
        /// </summary>
        static void AddCloudBlob(MeshBuffers target, Vector3 center, float radius, float squash,
            int latRings, int lonSegments, int seed, int salt, float flatBottomY, float flatTopY)
        {
            latRings = Mathf.Max(3, latRings);
            lonSegments = Mathf.Max(5, lonSegments);

            var pts = new Vector3[latRings][];
            for (int r = 0; r < latRings; r++)
            {
                float theta = Mathf.PI * (r + 0.5f) / latRings;
                float y = ClampBand(center.y + Mathf.Cos(theta) * radius * squash, flatBottomY, flatTopY);
                float rr = Mathf.Sin(theta);
                pts[r] = new Vector3[lonSegments];
                for (int s = 0; s < lonSegments; s++)
                {
                    float a = Mathf.PI * 2f * s / lonSegments;
                    float jitter = 1f + 0.08f * LowpolyHash.SignedHash(seed, r * 37 + s, salt);
                    pts[r][s] = new Vector3(
                        center.x + Mathf.Cos(a) * radius * rr * jitter, y,
                        center.z + Mathf.Sin(a) * radius * rr * jitter);
                }
            }

            Vector3 poleTop = new Vector3(center.x,
                ClampBand(center.y + radius * squash, flatBottomY, flatTopY), center.z);
            Vector3 poleBottom = new Vector3(center.x,
                ClampBand(center.y - radius * squash, flatBottomY, flatTopY), center.z);

            int last = latRings - 1;

            // 顶盖扇 + 底盖扇（环序仿 MeshBuffers.AddFrustum，法线方向已验证）。
            for (int s = 0; s < lonSegments; s++)
            {
                int t = (s + 1) % lonSegments;
                target.AddTriangle(poleTop, pts[0][t], pts[0][s]);
                target.AddTriangle(poleBottom, pts[last][s], pts[last][t]);
            }

            // 侧面（绕序同 AddFrustum：下环_s → 上环_s → 上环_t → 下环_t，外法线指向 |mid-center|）。
            for (int r = 0; r < last; r++)
            {
                for (int s = 0; s < lonSegments; s++)
                {
                    int t = (s + 1) % lonSegments;
                    Vector3 lowerS = pts[r + 1][s], upperS = pts[r][s];
                    Vector3 upperT = pts[r][t], lowerT = pts[r + 1][t];
                    Vector3 mid = (lowerS + upperS + upperT + lowerT) * 0.25f;
                    Vector3 hint = mid - center;
                    target.AddQuad(lowerS, upperS, upperT, lowerT,
                        hint.sqrMagnitude > 1e-10f ? hint.normalized : Vector3.up);
                }
            }
        }

        static float ClampBand(float y, float bottom, float top)
        {
            return Mathf.Clamp(y, bottom, top);
        }

        /// <summary>
        /// 把 source 的每个三角面按面心高度拆进 below/above（保留绕序 → 面法线不变）。
        /// 双色渐变（按高度 lerp）的低模实现：分带而非插值，两个纯色槽即成"上金下白"。
        /// </summary>
        static void SplitByHeight(MeshBuffers source, MeshBuffers below, MeshBuffers above, float thresholdY)
        {
            Vector3[] v = source.ToVertices();
            int[] tris = source.ToTriangles();
            for (int i = 0; i < tris.Length; i += 3)
            {
                float y = (v[tris[i]].y + v[tris[i + 1]].y + v[tris[i + 2]].y) / 3f;
                MeshBuffers target = y < thresholdY ? below : above;
                target.AddTriangle(v[tris[i]], v[tris[i + 1]], v[tris[i + 2]]);
            }
        }

        // ==================================================================
        // 出生点（云朵关）
        // ==================================================================

        /// <summary>
        /// 出生点建议表（8 个，脚底贴地坐标，**落角色时另加 UnitPivotHeight**）：
        /// 主角云 4 个（±3.6 方阵，台面 12×12 绰绰有余）+ 周边云各 1 个（云心，半宽 ≥3.0）。
        /// </summary>
        public static List<LowpolySpawnPoint> SpawnPoints(CloudFieldSpec spec)
        {
            List<CloudPlatformData> platforms = Layout(spec);
            var spawns = new List<LowpolySpawnPoint>(8);

            CloudPlatformData hero = platforms[Mathf.Clamp(spec.HeroCloudIndex, 0, platforms.Count - 1)];
            float inset = Mathf.Min(3.6f, hero.TopHalfWidth - 1.2f);
            spawns.Add(new LowpolySpawnPoint(
                new Vector3(hero.CenterXZ.x - inset, hero.TopY, hero.CenterXZ.z - inset), "CloudHero SW"));
            spawns.Add(new LowpolySpawnPoint(
                new Vector3(hero.CenterXZ.x + inset, hero.TopY, hero.CenterXZ.z - inset), "CloudHero SE"));
            spawns.Add(new LowpolySpawnPoint(
                new Vector3(hero.CenterXZ.x - inset, hero.TopY, hero.CenterXZ.z + inset), "CloudHero NW"));
            spawns.Add(new LowpolySpawnPoint(
                new Vector3(hero.CenterXZ.x + inset, hero.TopY, hero.CenterXZ.z + inset), "CloudHero NE"));

            int added = 0;
            for (int i = 0; i < platforms.Count && added < 4; i++)
            {
                CloudPlatformData p = platforms[i];
                if (p == hero || p.TopHalfWidth < 2.8f)
                    continue;
                spawns.Add(new LowpolySpawnPoint(
                    new Vector3(p.CenterXZ.x, p.TopY, p.CenterXZ.z), "Cloud" + i + " center"));
                added++;
            }

            return spawns;
        }
    }
}
