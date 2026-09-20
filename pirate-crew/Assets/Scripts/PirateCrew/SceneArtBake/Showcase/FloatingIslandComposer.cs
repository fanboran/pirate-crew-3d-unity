using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt.Showcase
{
    /// <summary>
    /// 空岛尺寸/构图参数（纯 C#，无头可测）。全部字段都有默认值，<see cref="Default"/> 即"交付构图"。
    ///
    /// 【坐标口径】空岛自带一套局部坐标：**草皮穹顶顶面 ≈ y=0，岛尖 ≈ y=-RockDepth**，
    /// 水平中心在原点。摆进场景时由 <see cref="FloatingIslandComposer.PlacementHeight"/> 把整座岛抬到
    /// 竞技场上方——这样"岛体尺寸"与"摆多高"是两件互不干扰的事。
    ///
    /// 【构图意图（AI 自有设计，本工程无既有空岛资产）】
    ///   一座**有生活痕迹的浮空岛**，而不是一块浮石：岩层分 6 层读得出地质史；3 条悬瀑落向云海；
    ///   老树 + 树群 + 垂藤 + 发光花草；东侧一座半塌的遗迹（石柱/断拱/台上悬浮的主晶）；
    ///   西侧崖沿一座木制瞭望台（海盗旗 + 灯）；一条石板小径把两处连起来；
    ///   岛外 9 颗悬浮晶、7 块浮石、64 点萤光尘、12 团云、6 只飞鸟——把"它在天上"讲完整。
    /// </summary>
    public sealed class FloatingIslandSpec
    {
        /// <summary>确定性种子：同种子必得同一座岛（无头测试据此断言）。</summary>
        public int Seed = 20260914;

        /// <summary>草皮顶面椭圆的 X 半径（世界单位）。</summary>
        public float RadiusX = 14.5f;

        /// <summary>草皮顶面椭圆的 Z 半径。略小于 X → 岛形是椭圆，不是圆盘。</summary>
        public float RadiusZ = 12.6f;

        /// <summary>岛尖相对草皮顶面的深度（正值；岛尖在 y = -RockDepth）。</summary>
        public float RockDepth = 19.5f;

        /// <summary>草皮穹顶中心高度（不是纯平顶：低模浮岛的顶面必须有一点隆起才读得出体积）。</summary>
        public float PlateauDome = 1.35f;

        /// <summary>岛形轮廓的环段数（不规则环的分辨率；16 段在 45° 俯视下已读不出多边形）。</summary>
        public int RimSegments = 16;

        /// <summary>阔叶树数量（不含老树）。</summary>
        public int TreeCount = 8;

        /// <summary>灌木数量。</summary>
        public int BushCount = 13;

        /// <summary>草簇数量。</summary>
        public int GrassTuftCount = 76;

        /// <summary>发光花簇数量。</summary>
        public int FlowerCount = 20;

        /// <summary>崖沿垂藤数量。</summary>
        public int VineCount = 9;

        /// <summary>悬瀑数量（每条自带水潭 + 溪流 + 末端雾团）。</summary>
        public int WaterfallCount = 3;

        /// <summary>悬瀑长度（从崖沿往下落多少米）。</summary>
        public float WaterfallLength = 23f;

        /// <summary>浮尘光点数量。</summary>
        public int MoteCount = 64;

        /// <summary>悬浮晶簇数量。</summary>
        public int SatelliteCrystalCount = 9;

        /// <summary>岛外浮石（带草帽/晶簇的小块）数量。</summary>
        public int FloatingRockCount = 7;

        /// <summary>云团数量（不含悬瀑末端雾团与岛底云裙）。</summary>
        public int CloudPuffCount = 12;

        /// <summary>飞鸟数量。</summary>
        public int BirdCount = 6;

        /// <summary>遗迹所在方位（弧度，0=+X 方向；默认东偏北）。</summary>
        public float RuinAngle = 0.62f;

        /// <summary>瞭望台所在方位（弧度；默认西侧，与遗迹相对）。</summary>
        public float WatchAngle = 2.92f;

        /// <summary>交付构图。</summary>
        public static FloatingIslandSpec Default => new FloatingIslandSpec();

        /// <summary>同构图、换种子（用于"一岛一景"或测试里造对照件）。</summary>
        public static FloatingIslandSpec FromSeed(int seed)
        {
            var spec = new FloatingIslandSpec { Seed = seed };
            return spec;
        }
    }

    /// <summary>一次 <see cref="FloatingIslandComposer.Compose"/> 的产出统计（纯 C#，日志与测试用）。</summary>
    public sealed class FloatingIslandStats
    {
        /// <summary>三角面总数。</summary>
        public readonly int Triangles;
        /// <summary>非空材质槽数（= DrawCall 数）。</summary>
        public readonly int Slots;
        /// <summary>几何包围盒（空岛局部坐标）。</summary>
        public readonly Vector3 BoundsMin;
        /// <summary>几何包围盒。</summary>
        public readonly Vector3 BoundsMax;
        /// <summary>悬瀑条数。</summary>
        public readonly int Waterfalls;
        /// <summary>树的数量（含老树）。</summary>
        public readonly int Trees;
        /// <summary>浮尘光点数。</summary>
        public readonly int Motes;

        /// <summary>构造统计。</summary>
        public FloatingIslandStats(int triangles, int slots, Vector3 boundsMin, Vector3 boundsMax,
            int waterfalls, int trees, int motes)
        {
            Triangles = triangles;
            Slots = slots;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            Waterfalls = waterfalls;
            Trees = trees;
            Motes = motes;
        }

        /// <summary>水平直径（包围盒 XZ 较大边）。</summary>
        public float HorizontalSpan => Mathf.Max(BoundsMax.x - BoundsMin.x, BoundsMax.z - BoundsMin.z);

        /// <summary>竖直高度（顶面到岛尖/悬瀑末端）。</summary>
        public float VerticalSpan => BoundsMax.y - BoundsMin.y;
    }

    /// <summary>
    /// 空岛总装（纯 C#，无头可测）：把一座"超级美观"的浮空岛的全部零件按构图写进
    /// <see cref="IslandBuffers"/>。**不含任何 Unity 对象**——顶点推演全在
    /// <see cref="MeshBuffers"/> 里，故本文件的构图逻辑能在无头验证台上断言
    /// （确定性、三角面预算、包围盒、零件是否贴在草皮面上）。
    ///
    /// 【分层（自下而上读）】
    ///   1. 岛底：6 层不规则岩层带（逐层收进 + 台阶层理）→ 收锥成尖；7 条竖向岩鳍贴在崖壁；
    ///      12 根石笋倒挂，其中 3 根长垂（岛尖的"须"）。
    ///   2. 崖沿：草皮垂帘（暗草）→ 土层（泥）→ 岩亮/岩中/岩暗带；各带外沿摆散石打散直线轮廓。
    ///   3. 顶面：穹顶草皮（4 环不规则环）+ 2 处缓丘 + 1 处浅洼。
    ///   4. 生活：3 条悬瀑（崖沿凹口 + 水潭 + 溪流 + 末端雾团）→ 老树 + 树群 + 灌木 + 草簇 +
    ///      发光花 + 垂藤。
    ///   5. 遗迹：3 级台基 + 6 根石柱（2 断 1 倒）+ 断拱 + 台中石柱上悬浮主晶 + 环绕碎晶 + 散落石。
    ///   6. 瞭望台：木台 + 围栏 + 桅杆 + 海盗旗 + 挂灯 + 爬梯 + 木箱木桶。
    ///   7. 小径：遗迹 → 瞭望台的 16 块石板 + 3 盏灯柱 + 2 座石堆。
    ///   8. 天外：9 悬浮晶 + 7 浮石（带草帽）+ 64 萤光尘 + 12 云团 + 岛底云裙 + 6 飞鸟。
    ///
    /// 【为什么全部按"确定性哈希"取随机】不用 <c>System.Random</c>：哈希是"由坐标/序号直接得到值"，
    /// 与遍历顺序无关，于是"改一个零件不会让其余零件全变样"，也让无头断言能复现同结果
    /// （与 <see cref="SceneArtHash"/> 在既有场景生成里的用法一致）。
    /// </summary>
    public static class FloatingIslandComposer
    {
        /// <summary>把整座岛摆到场景里时，岛尖离参考地面（Battle 竞技场 y=0）应留的净空。</summary>
        public const float ArenaClearance = 9f;

        /// <summary>
        /// 摆放高度：让全岛**最深的零件**（含抖动后的悬瀑末端与其雾团）恰好不低于
        /// <paramref name="arenaY"/> + <see cref="ArenaClearance"/>。
        /// 【为什么按包围盒算而不写常量】悬瀑/石笋的长度都是可调参数，写死高度会在调参后
        /// "岛尖穿进竞技场"或"飞得太高出画"，而这两种失败都只能靠出图发现。
        /// 【为什么悬瀑要乘 1.15 再加 2】<c>AddWaterfalls</c> 的实际帘长 = WaterfallLength × (0.86-1.14)
        /// （逐条抖动），末端雾团再向下垂 0.5-1.9——只按标称值取深会把"净空 9"漏成"净空约 4"。
        /// </summary>
        public static float PlacementHeight(FloatingIslandSpec spec, float arenaY = 0f)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            // 最深零件：岛体尖端 vs 抖动后的悬瀑末端 + 末端雾团（1.15 覆盖 ±14% 帘长抖动，+2 覆盖雾团下垂）。
            float deepest = Mathf.Max(spec.RockDepth, spec.WaterfallLength * 1.15f + 2f);
            return arenaY + ArenaClearance + deepest;
        }

        /// <summary>合成一座完整空岛（幂等：同 spec 必得同一几何）。</summary>
        public static FloatingIslandStats Compose(IslandBuffers buffers, FloatingIslandSpec spec)
        {
            if (buffers == null)
                return new FloatingIslandStats(0, 0, Vector3.zero, Vector3.zero, 0, 0, 0);

            if (spec == null)
                spec = FloatingIslandSpec.Default;

            int seed = spec.Seed;

            // 岛形轮廓：全岛共用这一圈（草皮顶面 / 垂帘 / 各岩层带都从它派生），
            // 于是"顶面与崖壁严丝合缝"，不会出现两套轮廓互相穿帮。
            Vector3[] rim = IslandPrimitives.Ring(spec.RimSegments, 0f, spec.RadiusX, spec.RadiusZ,
                seed, 101, 0.13f, 0f);
            for (int i = 0; i < rim.Length; i++)
                rim[i].y = PlateauY(spec, rim[i].x, rim[i].z);

            // ---- 预留区：散布零件不得压到遗迹/瞭望台/水潭上 ----
            var keepOut = new List<KeepOutZone>();
            Vector3 ruinCenter = OnPlateau(spec, spec.RuinAngle, 0.44f);
            Vector3 watchCenter = OnPlateau(spec, spec.WatchAngle, 0.70f);
            keepOut.Add(new KeepOutZone(ruinCenter, 4.6f));
            keepOut.Add(new KeepOutZone(watchCenter, 3.4f));

            // ---- 1/2/3：岛体 ----
            AddPlateau(buffers, spec, rim);
            AddStrata(buffers, spec, rim, seed);
            AddFins(buffers, spec, rim, seed);
            AddUnderside(buffers, spec, seed);

            // ---- 4：水 ----
            var ponds = new List<Vector3>();
            AddWaterfalls(buffers, spec, rim, seed, ponds);
            for (int i = 0; i < ponds.Count; i++)
                keepOut.Add(new KeepOutZone(ponds[i], 2.6f));

            // ---- 5：遗迹（先于植被，好把遗迹压在预留区里）----
            AddRuins(buffers, spec, ruinCenter, seed);

            // ---- 6：瞭望台 ----
            AddWatchPlatform(buffers, spec, watchCenter, seed);

            // ---- 7：小径 ----
            AddPath(buffers, spec, ruinCenter, watchCenter, seed);

            // ---- 8：植被 ----
            AddVegetation(buffers, spec, ruinCenter, watchCenter, keepOut, seed);

            // ---- 9：天外 ----
            AddOuterWorld(buffers, spec, seed);

            buffers.TryGetBounds(out Vector3 min, out Vector3 max);

            return new FloatingIslandStats(buffers.TotalTriangles, buffers.NonEmptySlots, min, max,
                spec.WaterfallCount, spec.TreeCount + 1, spec.MoteCount);
        }

        // ==================================================================
        // 草皮面高度场（所有落在顶面的零件都据此落地）
        // ==================================================================

        /// <summary>
        /// 草皮穹顶高度场：主穹顶（越靠中心越高）+ 2 处缓丘 + 1 处浅洼。
        /// 【唯一真源】顶面网格与所有"站在顶面上"的零件（树/石/台/径）都调它，
        /// 因此不存在"树浮在草皮上 10cm"这种只能靠出图发现的错位。
        /// </summary>
        public static float PlateauY(FloatingIslandSpec spec, float x, float z)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            float nx = x / Mathf.Max(0.001f, spec.RadiusX);
            float nz = z / Mathf.Max(0.001f, spec.RadiusZ);
            float r = Mathf.Sqrt(nx * nx + nz * nz);

            float y = spec.PlateauDome * Mathf.Pow(Mathf.Clamp01(1f - r), 0.85f);

            // 缓丘 / 浅洼：给顶面起伏，避免"一口平底锅"。
            y += Mound(x, z, 0.34f * spec.RadiusX, -0.12f * spec.RadiusZ, 0.30f * spec.RadiusX,
                0.46f * spec.PlateauDome);
            y += Mound(x, z, -0.40f * spec.RadiusX, 0.28f * spec.RadiusZ, 0.27f * spec.RadiusX,
                0.34f * spec.PlateauDome);
            y -= Mound(x, z, 0.06f * spec.RadiusX, 0.50f * spec.RadiusZ, 0.23f * spec.RadiusX,
                0.26f * spec.PlateauDome);
            return y;
        }

        /// <summary>圆润丘：中心 (cx,cz)、半径 <paramref name="radius"/>、峰高 <paramref name="height"/>。</summary>
        static float Mound(float x, float z, float cx, float cz, float radius, float height)
        {
            if (radius <= 0f)
                return 0f;

            float dx = x - cx, dz = z - cz;
            float d = Mathf.Sqrt(dx * dx + dz * dz) / radius;
            if (d >= 1f)
                return 0f;

            float t = 1f - d;
            return height * t * t;
        }

        /// <summary>顶面上某个方位/半径比例处的落点（y 已贴合草皮面）。公开给出生点表/测试复用。</summary>
        public static Vector3 OnPlateau(FloatingIslandSpec spec, float angle, float scaleXZ)
        {
            float x = Mathf.Cos(angle) * spec.RadiusX * scaleXZ;
            float z = Mathf.Sin(angle) * spec.RadiusZ * scaleXZ;
            return new Vector3(x, PlateauY(spec, x, z), z);
        }

        /// <summary>
        /// 顶面**预留区**快照（不可站位/摆放的圆形区域）：遗迹台基、瞭望台、老树、两处水潭。
        /// <see cref="Compose"/> 内部的排斥圈与 <see cref="FloatingIslandSpawnTable"/> 的出生点
        /// 校验共用这一份——两处各写一遍必然漂移。
        /// </summary>
        public static IslandKeepOutZone[] BuildKeepOutZones(FloatingIslandSpec spec)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            var zones = new List<IslandKeepOutZone>(6);
            zones.Add(new IslandKeepOutZone(OnPlateau(spec, spec.RuinAngle, 0.44f), 4.6f));
            zones.Add(new IslandKeepOutZone(OnPlateau(spec, spec.WatchAngle, 0.70f), 3.4f));
            zones.Add(new IslandKeepOutZone(OnPlateau(spec, 2.55f, 0.34f), 2.4f));   // 老树

            // 水潭：只有前两条瀑布带潭（第三条是干瀑）；角度公式与 AddWaterfalls 同一出处。
            int count = Mathf.Clamp(spec.WaterfallCount, 0, 6);
            for (int i = 0; i < Mathf.Min(2, count); i++)
                zones.Add(new IslandKeepOutZone(OnPlateau(spec, WaterfallAngle(spec, i, count), 0.42f), 2.6f));

            return zones.ToArray();
        }

        /// <summary>
        /// 第 <paramref name="index"/> 条悬瀑的方位角（AddWaterfalls 与 BuildKeepOutZones 共用，
        /// 含"避开遗迹/瞭望台扇区"的两段偏移）。
        /// </summary>
        static float WaterfallAngle(FloatingIslandSpec spec, int index, int count)
        {
            float ang = Mathf.PI * 2f * index / count + 0.7f
                + SceneArtHash.SignedHash(spec.Seed, index, 401) * 0.35f;
            if (AngleDistance(ang, spec.RuinAngle) < 0.45f)
                ang += 0.5f;
            if (AngleDistance(ang, spec.WatchAngle) < 0.45f)
                ang -= 0.5f;
            return ang;
        }

        // ==================================================================
        // 可玩性：顶面碰撞代理（第 3 关把它当地面）
        // ==================================================================

        /// <summary>
        /// 顶面**碰撞代理**网格：与 <see cref="AddPlateau"/> 同一套环（rim → 0.72 → 0.44 → 0.18 → 毂）
        /// + 草皮垂帘侧裙 + 底盖，构成一个近似封闭的薄壳。供 MeshCollider（凹面、静态）使用——
        /// 单位（Rigidbody）站在上面不掉、弹体（物理回调）从任何方向撞壳都会起爆。
        ///
        /// 【为什么是独立薄壳而不是给渲染网格挂 Collider】草皮槽里混着几百片双面草叶/树冠，
        /// 给它们烘焙物理网格既慢又是噪声；薄壳只有 ~160 面且与顶面同公式（本函数与 AddPlateau
        /// 都从同一 rim 推导），"看着站的地方"与"物理上站的地方"不会分叉。
        /// 【近似取舍】崖壁中下段（垂帘 0.8m 以下）没有专门碰撞：弹体穿入壳内会打在底盖/顶棚上
        /// 起爆，表现上仍是"打在岛体上"，不为它加整套岩层碰撞。
        /// </summary>
        public static void BuildCollisionSurface(MeshBuffers target, FloatingIslandSpec spec)
        {
            if (target == null)
                return;
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            int seed = spec.Seed;
            Vector3[] rim = IslandPrimitives.Ring(spec.RimSegments, 0f, spec.RadiusX, spec.RadiusZ,
                seed, 101, 0.13f, 0f);
            for (int i = 0; i < rim.Length; i++)
                rim[i].y = PlateauY(spec, rim[i].x, rim[i].z);

            Vector3[] ringA = Conform(spec, rim, 0.72f);
            Vector3[] ringB = Conform(spec, rim, 0.44f);
            Vector3[] ringC = Conform(spec, rim, 0.18f);
            Vector3 hub = new Vector3(0f, PlateauY(spec, 0f, 0f), 0f);

            // 顶面（可站面）。
            IslandPrimitives.AddAnnulus(target, rim, ringA, Vector3.up);
            IslandPrimitives.AddAnnulus(target, ringA, ringB, Vector3.up);
            IslandPrimitives.AddAnnulus(target, ringB, ringC, Vector3.up);
            IslandPrimitives.AddFan(target, ringC, hub, Vector3.up);

            // 侧裙（垂帘同位：外扩 3.5% 落 0.8m）+ 底盖（弹体从下方/侧方打岛的兜底面）。
            Vector3[] fringe = IslandPrimitives.ScaleRing(rim, 1.035f, 0f, seed, 131, 0.045f, 0.22f);
            for (int i = 0; i < fringe.Length; i++)
                fringe[i].y = rim[i].y - 0.8f;
            IslandPrimitives.AddSideRing(target, rim, fringe);
            IslandPrimitives.AddFan(target, fringe, new Vector3(0f, fringe[0].y - 0.2f, 0f), Vector3.down);
        }

        // ==================================================================
        // 1/3：草皮顶面 + 崖沿
        // ==================================================================

        /// <summary>
        /// 草皮顶面（4 环不规则环：rim → 0.72 → 0.44 → 0.18 → 中心），
        /// 加一圈**外扩下垂的草皮垂帘**（暗草）压住崖沿——浮空岛的"边缘"必须是软的，
        /// 一刀切的岩沿会读成"切开的蛋糕"。
        /// </summary>
        static void AddPlateau(IslandBuffers buffers, FloatingIslandSpec spec, Vector3[] rim)
        {
            MeshBuffers grass = buffers.GrassLight;
            MeshBuffers dark = buffers.GrassDark;

            Vector3[] ringA = Conform(spec, rim, 0.72f);
            Vector3[] ringB = Conform(spec, rim, 0.44f);
            Vector3[] ringC = Conform(spec, rim, 0.18f);
            Vector3 hub = new Vector3(0f, PlateauY(spec, 0f, 0f), 0f);

            IslandPrimitives.AddAnnulus(grass, rim, ringA, Vector3.up);
            IslandPrimitives.AddAnnulus(grass, ringA, ringB, Vector3.up);
            IslandPrimitives.AddAnnulus(grass, ringB, ringC, Vector3.up);
            IslandPrimitives.AddFan(grass, ringC, hub, Vector3.up);

            // 垂帘：外扩 3.5% 并落 0.8m（外沿比顶面宽 → 形成"草从崖沿挂下来"的倒悬感）
            Vector3[] fringe = IslandPrimitives.ScaleRing(rim, 1.035f, 0f, spec.Seed, 131, 0.045f, 0.22f);
            for (int i = 0; i < fringe.Length; i++)
                fringe[i].y = rim[i].y - 0.8f;
            IslandPrimitives.AddSideRing(dark, rim, fringe);
        }

        /// <summary>把轮廓环按比例收缩并在 XZ 上贴合草皮高度场。</summary>
        static Vector3[] Conform(FloatingIslandSpec spec, Vector3[] rim, float scaleXZ)
        {
            var pts = new Vector3[rim.Length];
            for (int i = 0; i < rim.Length; i++)
            {
                float x = rim[i].x * scaleXZ;
                float z = rim[i].z * scaleXZ;
                pts[i] = new Vector3(x, PlateauY(spec, x, z), z);
            }
            return pts;
        }

        // ==================================================================
        // 2：岩层带 + 收锥 + 层理散石
        // ==================================================================

        /// <summary>
        /// 6 层环带 + 收锥成尖。每层**外沿比上层内沿宽 2-4%**（台阶层理），
        /// 高度与收进量都带逐顶点抖动 → 读得出"一层层堆出来的岩体"而不是"一个圆锥杯"。
        /// 材质自亮到暗（草皮垂帘 → 土层 → 岩亮 → 岩中 → 岩暗），
        /// 越深越暗 = 越照不到光，这是零光源调色下唯一能讲"深度"的手段。
        /// </summary>
        static void AddStrata(IslandBuffers buffers, FloatingIslandSpec spec, Vector3[] rim, int seed)
        {
            float top = -0.8f;   // 与草皮垂帘末端接续

            Vector3[] dirtTop = IslandPrimitives.ScaleRing(rim, 1.03f, top, seed, 141, 0.05f, 0.20f);
            Vector3[] dirtBottom = IslandPrimitives.ScaleRing(rim, 0.995f, -2.3f, seed, 143, 0.04f, 0.18f);
            IslandPrimitives.AddSideRing(buffers.Dirt, dirtTop, dirtBottom);

            Vector3[] rock1Top = IslandPrimitives.ScaleRing(rim, 1.02f, -2.4f, seed, 151, 0.05f, 0.16f);
            Vector3[] rock1Bottom = IslandPrimitives.ScaleRing(rim, 0.86f, -5.4f, seed, 153, 0.06f, 0.24f);
            IslandPrimitives.AddSideRing(buffers.RockLight, rock1Top, rock1Bottom);

            Vector3[] rock2Top = IslandPrimitives.ScaleRing(rim, 0.885f, -5.5f, seed, 157, 0.06f, 0.22f);
            Vector3[] rock2Bottom = IslandPrimitives.ScaleRing(rim, 0.66f, -9.6f, seed, 159, 0.07f, 0.30f);
            IslandPrimitives.AddSideRing(buffers.RockMid, rock2Top, rock2Bottom);

            Vector3[] rock3Top = IslandPrimitives.ScaleRing(rim, 0.685f, -9.7f, seed, 163, 0.07f, 0.26f);
            Vector3[] rock3Bottom = IslandPrimitives.ScaleRing(rim, 0.43f, -13.8f, seed, 167, 0.08f, 0.36f);
            IslandPrimitives.AddSideRing(buffers.RockDark, rock3Top, rock3Bottom);

            Vector3[] rootTop = IslandPrimitives.ScaleRing(rim, 0.455f, -13.9f, seed, 173, 0.09f, 0.30f);
            Vector3[] rootBottom = IslandPrimitives.ScaleRing(rim, 0.19f, -17.4f, seed, 179, 0.11f, 0.42f);
            IslandPrimitives.AddSideRing(buffers.RockDark, rootTop, rootBottom);

            // 收锥成尖：岛尖稍微偏离轴心（正圆锥尖读成"陀螺"，偏一点才像被掰下来的岩块）
            Vector3 tip = new Vector3(
                spec.RadiusX * 0.06f * SceneArtHash.SignedHash(seed, 0, 181),
                -spec.RockDepth,
                spec.RadiusZ * 0.06f * SceneArtHash.SignedHash(seed, 1, 191));
            IslandPrimitives.AddFan(buffers.RockDark, rootBottom, tip, Vector3.down);

            // 层理散石：每条阶地外沿摆几块，把笔直的水平棱线打散。
            AddLedgeRocks(buffers.RockLight, rock1Bottom, 5, seed, 211, 1.05f, 0.55f, 1.15f);
            AddLedgeRocks(buffers.RockMid, rock2Bottom, 6, seed, 223, 1.1f, 0.65f, 1.35f);
            AddLedgeRocks(buffers.RockDark, rock3Bottom, 6, seed, 227, 1.15f, 0.75f, 1.5f);
            AddLedgeRocks(buffers.RockDark, rootBottom, 4, seed, 229, 1.2f, 0.8f, 1.6f);
        }

        /// <summary>沿一条环摆散石（<paramref name="outward"/> = 外扩比例，让石块半身探出崖外）。</summary>
        static void AddLedgeRocks(MeshBuffers target, Vector3[] ring, int count, int seed, int salt,
            float outward, float minRadius, float maxRadius)
        {
            if (target == null || ring == null || ring.Length == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                float pick = SceneArtHash.Hash01(seed, i, salt) * ring.Length;
                int idx = ((int)pick) % ring.Length;
                float sub = pick - Mathf.Floor(pick);
                int nxt = (idx + 1) % ring.Length;

                Vector3 basePt = Vector3.Lerp(ring[idx], ring[nxt], sub);
                Vector3 radial = new Vector3(basePt.x, 0f, basePt.z);
                if (radial.sqrMagnitude < 1e-8f)
                    continue;

                radial.Normalize();
                float radius = Mathf.Lerp(minRadius, maxRadius, SceneArtHash.Hash01(seed, i, salt + 31));
                Vector3 c = basePt + radial * (radius * outward * 0.5f);
                c.y += radius * (0.10f * SceneArtHash.SignedHash(seed, i, salt + 37));

                target.AddRock(c, radius, new Vector3(1.12f, 0.78f, 1.0f), seed + i * 29 + salt, 6);
            }
        }

        // ==================================================================
        // 2b：竖向岩鳍（把"圆石头"变成有走向的山体剪影）
        // ==================================================================

        /// <summary>
        /// 7 条扶壁状岩鳍：沿崖壁从上向下、由外向内斜插，宽度与厚度向下收窄。
        /// 侧视图里这些竖线就是这座岛的"骨架"，也是低模浮岛最有效的剪影手段。
        /// </summary>
        static void AddFins(IslandBuffers buffers, FloatingIslandSpec spec, Vector3[] rim, int seed)
        {
            int fins = 7;
            for (int i = 0; i < fins; i++)
            {
                float pick = SceneArtHash.Hash01(seed, i, 241) * rim.Length;
                int idx = ((int)pick) % rim.Length;
                Vector3 basePt = rim[idx];
                Vector3 radial = new Vector3(basePt.x, 0f, basePt.z);
                if (radial.sqrMagnitude < 1e-8f)
                    continue;

                radial.Normalize();

                float outward0 = 1.00f + 0.03f * SceneArtHash.Hash01(seed, i, 251);
                float inward1 = 0.56f + 0.16f * SceneArtHash.Hash01(seed, i, 257);
                float y0 = -2.6f - 1.4f * SceneArtHash.Hash01(seed, i, 263);
                float y1 = -11.0f - 3.0f * SceneArtHash.Hash01(seed, i, 269);

                Vector3 from = new Vector3(radial.x * spec.RadiusX * outward0, y0, radial.z * spec.RadiusZ * outward0);
                Vector3 to = new Vector3(radial.x * spec.RadiusX * inward1, y1, radial.z * spec.RadiusZ * inward1);

                float w0 = 0.50f + 0.75f * SceneArtHash.Hash01(seed, i, 271);
                float t0 = 0.34f + 0.44f * SceneArtHash.Hash01(seed, i, 277);
                float w1 = w0 * (0.45f + 0.30f * SceneArtHash.Hash01(seed, i, 281));
                float t1 = t0 * 0.55f;

                MeshBuffers target = SceneArtHash.Hash01(seed, i, 283) > 0.55f ? buffers.RockLight : buffers.RockMid;
                IslandPrimitives.AddTaperedBox(target, from, to, w0, t0, w1, t1, radial);
            }
        }

        // ==================================================================
        // 1b：岛底石笋（重力指示）
        // ==================================================================

        /// <summary>
        /// 12 根倒挂石笋（其中 3 根长垂）：尖端朝下是"这块岩体被从地面撕下来"的视觉证据，
        /// 也是浮空岛与"一块石头"的分界——没有向下的尖齿就没有悬空感。
        /// </summary>
        static void AddUnderside(IslandBuffers buffers, FloatingIslandSpec spec, int seed)
        {
            int count = 12;
            for (int i = 0; i < count; i++)
            {
                float ang = Mathf.PI * 2f * i / count + SceneArtHash.SignedHash(seed, i, 301) * 0.5f;
                float r = 0.06f + 0.30f * SceneArtHash.Hash01(seed, i, 307);
                float rootY = -11.5f - 6.5f * SceneArtHash.Hash01(seed, i, 311);
                float length = 2.4f + 4.6f * SceneArtHash.Hash01(seed, i, 313);
                float radius = 0.42f + 0.85f * SceneArtHash.Hash01(seed, i, 317);

                // 岛越靠尖端，石笋越短，避免尖端"炸毛"
                length *= Mathf.Lerp(0.55f, 1f, r / 0.36f);

                Vector3 root = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r, rootY,
                    Mathf.Sin(ang) * spec.RadiusZ * r);

                IslandPrimitives.AddStalactite(buffers.RockDark, root, length, radius, ang * Mathf.Rad2Deg,
                    10f * SceneArtHash.SignedHash(seed, i, 331), seed + i * 41);
            }

            // 岛尖 3 根长须（比石笋更细更长，把视觉重量往云海里引）
            for (int i = 0; i < 3; i++)
            {
                float ang = Mathf.PI * 2f * i / 3f + SceneArtHash.Hash01(seed, i, 337) * 1.4f;
                float r = 0.02f + 0.07f * SceneArtHash.Hash01(seed, i, 347);
                Vector3 root = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r, -16.4f,
                    Mathf.Sin(ang) * spec.RadiusZ * r);
                IslandPrimitives.AddStalactite(buffers.RockDark, root,
                    4.0f + 5.0f * SceneArtHash.Hash01(seed, i, 349), 0.30f + 0.26f * SceneArtHash.Hash01(seed, i, 353),
                    ang * Mathf.Rad2Deg, 6f, seed + 900 + i);
            }
        }

        // ==================================================================
        // 4：悬瀑（水潭 + 溪流 + 水帘 + 水沫芯 + 末端雾团）
        // ==================================================================

        /// <summary>
        /// 每条悬瀑是一整套小系统：崖沿凹口（两块唇石）→ 顶面水潭 → 溪流 → 垂落水帘
        /// （水材料 + 3 条白色水沫芯）→ 末端雾团（云团 + 水花）。
        /// 【为什么必须有雾团】直落 23m 的水帘若戛然而止，会读成"一根蓝带子挂在天上"；
        /// 末端用云团接住，水便"化进云海"，同时把岛底与云层缝在一起。
        /// </summary>
        static void AddWaterfalls(IslandBuffers buffers, FloatingIslandSpec spec, Vector3[] rim, int seed,
            List<Vector3> ponds)
        {
            int count = Mathf.Clamp(spec.WaterfallCount, 0, 6);
            if (count == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                // 方位：均匀分布 + 抖动 + 避开遗迹/瞭望台扇区（公式在 WaterfallAngle，
                // 与 BuildKeepOutZones 的水潭圈共用，两处永不漂移）。
                float ang = WaterfallAngle(spec, i, count);

                int idx = IndexNearest(rim, ang);
                Vector3 rimPt = rim[idx];
                Vector3 outward = new Vector3(rimPt.x, 0f, rimPt.z);
                if (outward.sqrMagnitude < 1e-8f)
                    outward = Vector3.right;
                outward.Normalize();
                Vector3 side = new Vector3(-outward.z, 0f, outward.x);

                float len = spec.WaterfallLength * (0.86f + 0.28f * SceneArtHash.Hash01(seed, i, 409));
                float swayA = SceneArtHash.SignedHash(seed, i, 419) * 1.1f;
                float swayB = SceneArtHash.SignedHash(seed, i, 421) * 0.9f;

                // 帘心线：先向外探出 ≥1m（水有水平初速），再近乎垂直落下，末段微微回摆。
                var pts = new Vector3[7];
                pts[0] = rimPt + outward * 0.05f + Vector3.up * 0.05f;
                pts[1] = rimPt + outward * 0.85f + Vector3.down * (len * 0.055f) + side * (swayA * 0.12f);
                pts[2] = rimPt + outward * 1.24f + Vector3.down * (len * 0.19f) + side * (swayA * 0.30f);
                pts[3] = rimPt + outward * 1.42f + Vector3.down * (len * 0.38f) + side * (swayB * 0.38f);
                pts[4] = rimPt + outward * 1.38f + Vector3.down * (len * 0.60f) + side * (swayB * 0.30f);
                pts[5] = rimPt + outward * 1.16f + Vector3.down * (len * 0.82f) + side * (swayA * 0.20f);
                pts[6] = rimPt + outward * 0.88f + Vector3.down * len;

                float[] halfWidth = { 1.05f, 0.86f, 0.66f, 0.56f, 0.54f, 0.66f, 0.94f };
                for (int k = 0; k < halfWidth.Length; k++)
                    halfWidth[k] *= 0.78f + 0.44f * SceneArtHash.Hash01(seed, i, 431 + k);

                IslandPrimitives.AddRibbon(buffers.Water, pts, halfWidth, outward);

                // 水沫芯：3 条更窄的白帘，沿法线方向前后错开 → 正面看是"翻涌的水柱"。
                for (int f = 0; f < 3; f++)
                {
                    float offN = (f - 0.5f) * 0.10f;
                    float offS = (f - 1f) * 0.34f;
                    var fpts = new Vector3[5];
                    var fw = new float[5];
                    for (int k = 0; k < 5; k++)
                    {
                        int src = Mathf.Min(6, k + 1);   // 水沫比水帘短一截（先落地）
                        fpts[k] = pts[src] + outward * offN + side * offS
                            + outward * (0.05f + 0.05f * SceneArtHash.Hash01(seed, i * 7 + f, 441 + k));
                        fw[k] = halfWidth[src] * (0.24f + 0.16f * SceneArtHash.Hash01(seed, i * 5 + f, 451 + k));
                    }
                    IslandPrimitives.AddRibbon(buffers.Foam, fpts, fw, outward);
                }

                // 末端雾团 + 水花
                Vector3 end = pts[6];
                for (int m = 0; m < 3; m++)
                {
                    Vector3 c = end + new Vector3(
                        outward.x * (0.5f + 1.4f * SceneArtHash.Hash01(seed, i * 3 + m, 461)),
                        -0.5f - 1.4f * SceneArtHash.Hash01(seed, i * 3 + m, 463),
                        outward.z * (0.5f + 1.4f * SceneArtHash.Hash01(seed, i * 3 + m, 467)));
                    IslandPrimitives.AddCloudPuff(buffers.Cloud, c,
                        1.5f + 1.5f * SceneArtHash.Hash01(seed, i * 3 + m, 471), seed + i * 50 + m, 4);
                }

                for (int s = 0; s < 5; s++)
                {
                    Vector3 c = end + outward * (0.3f + 1.7f * SceneArtHash.Hash01(seed, i * 5 + s, 481))
                        + side * (SceneArtHash.SignedHash(seed, i * 5 + s, 487) * 1.7f)
                        + Vector3.up * (0.2f + 0.8f * SceneArtHash.Hash01(seed, i * 5 + s, 491));
                    float r = 0.22f + 0.34f * SceneArtHash.Hash01(seed, i * 5 + s, 493);
                    IslandPrimitives.AddBlob(buffers.Foam, c, new Vector3(r, r * 0.8f, r), 2, 5,
                        seed + i * 13 + s, 497, 0.22f);
                }

                // 崖沿唇石：把水口夹出来（两块，左右各一）
                for (int l = 0; l < 2; l++)
                {
                    float sgn = l == 0 ? -1f : 1f;
                    float w = 0.7f + 0.5f * SceneArtHash.Hash01(seed, i * 2 + l, 501);
                    Vector3 c = rimPt + side * (sgn * (halfWidth[0] + w * 0.6f)) - outward * (0.15f)
                        + Vector3.up * 0.06f;
                    buffers.RockMid.AddRock(c, w, new Vector3(0.9f, 0.7f, 1.15f), seed + i * 11 + l, 6);
                }

                // 顶面水潭 + 溪流（前两条瀑布带水潭；第三条是"干瀑"，直接从崖沿落下）
                if (i < 2)
                {
                    Vector3 pond = OnPlateau(spec, ang, 0.42f);
                    float pondR = 1.5f + 0.7f * SceneArtHash.Hash01(seed, i, 511);
                    buffers.Water.AddDisc(new Vector3(pond.x, pond.y + 0.035f, pond.z), pondR, 11, Vector3.up, true);
                    ponds.Add(pond);

                    // 潭边湿石 + 泡沫
                    for (int s = 0; s < 5; s++)
                    {
                        float sa = Mathf.PI * 2f * s / 5f + SceneArtHash.Hash01(seed, i * 5 + s, 521) * 0.9f;
                        Vector3 sc = pond + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * (pondR * 1.08f);
                        sc.y = PlateauY(spec, sc.x, sc.z) + 0.02f;
                        buffers.RockMid.AddRock(sc, 0.28f + 0.34f * SceneArtHash.Hash01(seed, i * 5 + s, 523),
                            new Vector3(1.0f, 0.55f, 1.05f), seed + i * 7 + s, 6);

                        Vector3 fc = pond + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * (pondR * 0.86f);
                        fc.y += 0.06f;
                        IslandPrimitives.AddBlob(buffers.Foam, fc, new Vector3(0.34f, 0.16f, 0.34f), 2, 5,
                            seed + i * 5 + s, 531, 0.24f);
                    }

                    // 溪流：潭 → 崖沿凹口，逐点贴草皮面
                    var stream = new Vector3[7];
                    var sw = new float[7];
                    for (int k = 0; k < 7; k++)
                    {
                        float t = k / 6f;
                        Vector3 p = Vector3.Lerp(pond, new Vector3(rimPt.x, 0f, rimPt.z), t);
                        p += new Vector3(side.x, 0f, side.z) * (Mathf.Sin(t * Mathf.PI) * 0.9f
                            * SceneArtHash.SignedHash(seed, i, 541));
                        p.y = PlateauY(spec, p.x, p.z) + 0.045f;
                        stream[k] = p;
                        sw[k] = Mathf.Lerp(0.42f, 0.62f, t) * (0.85f + 0.3f * SceneArtHash.Hash01(seed, i, 547 + k));
                    }
                    IslandPrimitives.AddRibbon(buffers.Foam, stream, sw, Vector3.up);
                }
            }
        }

        // ==================================================================
        // 5：遗迹
        // ==================================================================

        /// <summary>
        /// 东侧半塌遗迹：3 级台基 + 6 根石柱（2 断 + 1 倒）+ 断拱 + 台中主晶（悬浮 + 光环 + 环绕碎晶）
        /// + 散落石块 + 苔草。
        /// 【构图用意】浮空岛最怕"只有自然物"——一处人工废墟立刻给出"有人来过、然后离开了"的叙事，
        /// 也让视线在岛面上有落点。断柱与倒塌的过梁是"时间"的读法，比完好的神庙更耐看。
        /// </summary>
        /// <summary>台基（3 级石板）顶面高度：取覆盖半径 r 的那级石板顶（与 AddRuins 的三级 AddSlab 参数一致）。</summary>
        static float DaisTop(float radius, float y0)
        {
            if (radius <= 2.35f) return y0 + 0.76f;
            if (radius <= 3.0f) return y0 + 0.52f;
            if (radius <= 3.6f) return y0 + 0.26f;
            return y0;
        }

        static void AddRuins(IslandBuffers buffers, FloatingIslandSpec spec, Vector3 center, int seed)
        {
            MeshBuffers stone = buffers.Stone;
            float y0 = center.y;

            // 台基：3 级不规则石板，逐级收小
            IslandPrimitives.AddSlab(stone, center, 3.6f, y0 + 0.26f, 0.30f, seed, 9, 0.10f);
            IslandPrimitives.AddSlab(stone, center, 3.0f, y0 + 0.52f, 0.28f, seed + 17, 9, 0.10f);
            IslandPrimitives.AddSlab(stone, center, 2.35f, y0 + 0.76f, 0.26f, seed + 31, 8, 0.11f);

            float deck = y0 + 0.76f;

            // 石柱：6 根（朝一个"未完成的环"排布，缺口朝西 = 正对瞭望台方向）
            float[] heights = { 3.7f, 3.1f, 2.5f, 1.55f, 0.95f, 3.3f };
            bool[] broken = { false, false, false, true, true, false };
            for (int i = 0; i < 6; i++)
            {
                float ang = spec.RuinAngle + Mathf.PI * 0.30f + Mathf.PI * 1.55f * i / 5f;
                float r = 2.55f + 0.45f * SceneArtHash.Hash01(seed, i, 601);
                Vector3 bp = new Vector3(center.x + Mathf.Cos(ang) * r, DaisTop(r, y0), center.z + Mathf.Sin(ang) * r);

                IslandPrimitives.AddPillar(stone, bp, heights[i] * (0.92f + 0.16f * SceneArtHash.Hash01(seed, i, 607)),
                    0.26f + 0.06f * SceneArtHash.Hash01(seed, i, 611), ang * Mathf.Rad2Deg,
                    broken[i], seed + i * 19);
            }

            // 倒塌的柱：整体转到 76° 躺在台基外（用临时缓冲 + 位姿矩阵，避免手推旋转）
            {
                var temp = new MeshBuffers();
                IslandPrimitives.AddPillar(temp, Vector3.zero, 3.2f, 0.28f, 0f, false, seed + 77);
                Vector3 lie = new Vector3(center.x + Mathf.Cos(spec.RuinAngle + 1.9f) * 4.6f, y0 + 0.18f,
                    center.z + Mathf.Sin(spec.RuinAngle + 1.9f) * 4.6f);
                lie.y = PlateauY(spec, lie.x, lie.z) + 0.24f;
                stone.AppendTransformed(temp, SceneArtRot.Trs(lie,
                    SceneArtRot.Euler(76f, SceneArtHash.Hash01(seed, 0, 617) * 360f, 0f), Vector3.one));
            }

            // 断拱：两根柱 + 两段过梁（中间留缺口），另有一段落在台基上
            float archAng = spec.RuinAngle + 0.5f;
            const float archRadius = 3.1f;
            Vector3 archA = new Vector3(center.x + Mathf.Cos(archAng) * archRadius, DaisTop(archRadius, y0),
                center.z + Mathf.Sin(archAng) * archRadius);
            Vector3 archB = new Vector3(center.x + Mathf.Cos(archAng + 0.62f) * archRadius, DaisTop(archRadius, y0),
                center.z + Mathf.Sin(archAng + 0.62f) * archRadius);
            IslandPrimitives.AddPillar(stone, archA, 3.2f, 0.24f, archAng * Mathf.Rad2Deg, false, seed + 101);
            IslandPrimitives.AddPillar(stone, archB, 3.0f, 0.24f, (archAng + 0.62f) * Mathf.Rad2Deg, false, seed + 103);

            Vector3 mid = (archA + archB) * 0.5f;
            float span = Vector3.Distance(archA, archB);
            float spanYaw = Mathf.Atan2(archB.z - archA.z, archB.x - archA.x) * Mathf.Rad2Deg;
            // 过梁压在柱头位置（柱身顶 = 基座 0.33 + 柱高，柱头再高 0.2/0.4）
            float lintelY = archA.y + 3.66f;
            stone.AddBox(new Vector3(mid.x - (archB.x - archA.x) * 0.22f, lintelY, mid.z - (archB.z - archA.z) * 0.22f),
                new Vector3(span * 0.42f, 0.30f, 0.36f), spanYaw);
            stone.AddBox(new Vector3(mid.x + (archB.x - archA.x) * 0.28f, lintelY + 0.12f, mid.z + (archB.z - archA.z) * 0.28f),
                new Vector3(span * 0.36f, 0.28f, 0.34f), spanYaw + 7f);

            {
                var temp = new MeshBuffers();
                temp.AddBox(Vector3.zero, new Vector3(span * 0.4f, 0.30f, 0.36f), 0f);
                Vector3 fallen = center + new Vector3(Mathf.Cos(archAng - 0.4f) * 4.2f, 0f, Mathf.Sin(archAng - 0.4f) * 4.2f);
                fallen.y = PlateauY(spec, fallen.x, fallen.z) + 0.2f;
                stone.AppendTransformed(temp, SceneArtRot.Trs(fallen,
                    SceneArtRot.Euler(0f, archAng * Mathf.Rad2Deg + 24f, 12f), Vector3.one));
            }

            // 台中石柱 + 悬浮主晶 + 光环 + 环绕碎晶
            float pedY = deck;
            IslandPrimitives.AddSlab(stone, new Vector3(center.x, 0f, center.z), 1.05f, pedY + 0.22f, 0.24f, seed + 131, 8, 0.08f);
            IslandPrimitives.AddSlab(stone, new Vector3(center.x, 0f, center.z), 0.82f, pedY + 0.42f, 0.22f, seed + 137, 8, 0.08f);
            stone.AddFrustum(new Vector3(center.x, pedY + 0.52f, center.z), 0.30f, 0.24f, 1.55f, 7,
                spec.RuinAngle * Mathf.Rad2Deg, true, true);
            stone.AddBox(new Vector3(center.x, pedY + 2.13f, center.z), new Vector3(0.78f, 0.16f, 0.78f), 0f);

            Vector3 core = new Vector3(center.x, pedY + 2.21f + 1.05f, center.z);
            IslandPrimitives.AddCrystal(buffers.Crystal, core, 0.58f, 1.45f,
                new Vector3(0f, spec.RuinAngle * Mathf.Rad2Deg, 0f), seed + 151, 6, 0.10f);

            // 光环（复用场景道具的闭合环实现：轴 = up）
            ScenePropGeometry.AddRingLoop(buffers.Glow, core - Vector3.up * 0.05f, Vector3.up, 0.92f, 0.045f, 14);

            for (int i = 0; i < 4; i++)
            {
                float ang = Mathf.PI * 2f * i / 4f + SceneArtHash.Hash01(seed, i, 661) * 0.8f;
                float r = 0.78f + 0.28f * SceneArtHash.Hash01(seed, i, 673);
                Vector3 c = core + new Vector3(Mathf.Cos(ang) * r,
                    -0.55f + 1.1f * SceneArtHash.Hash01(seed, i, 677), Mathf.Sin(ang) * r);
                IslandPrimitives.AddCrystal(buffers.Crystal, c,
                    0.14f + 0.12f * SceneArtHash.Hash01(seed, i, 683),
                    0.46f + 0.42f * SceneArtHash.Hash01(seed, i, 691),
                    new Vector3(18f + 26f * SceneArtHash.Hash01(seed, i, 701), ang * Mathf.Rad2Deg,
                        18f + 26f * SceneArtHash.Hash01(seed, i, 703)), seed + i * 23, 4, 0.16f);
            }

            // 散落石块（施工废料）：9 块，绕台基一圈
            for (int i = 0; i < 9; i++)
            {
                float ang = spec.RuinAngle + Mathf.PI * 2f * i / 9f + SceneArtHash.SignedHash(seed, i, 711) * 0.5f;
                float r = 3.3f + 3.0f * SceneArtHash.Hash01(seed, i, 719);
                Vector3 p = new Vector3(center.x + Mathf.Cos(ang) * r, 0f, center.z + Mathf.Sin(ang) * r);
                p.y = PlateauY(spec, p.x, p.z) + 0.10f;
                float w = 0.30f + 0.55f * SceneArtHash.Hash01(seed, i, 727);
                stone.AddBox(new Vector3(p.x, p.y + w * 0.4f, p.z),
                    new Vector3(w * 1.5f, w * 0.8f, w * 1.2f),
                    SceneArtHash.Hash01(seed, i, 733) * 360f);
            }

            // 苔草：柱底与台基边
            for (int i = 0; i < 7; i++)
            {
                float ang = spec.RuinAngle + Mathf.PI * 2f * i / 7f + SceneArtHash.Hash01(seed, i, 739) * 0.7f;
                float r = 2.2f + 1.9f * SceneArtHash.Hash01(seed, i, 743);
                Vector3 p = new Vector3(center.x + Mathf.Cos(ang) * r, 0f, center.z + Mathf.Sin(ang) * r);
                p.y = PlateauY(spec, p.x, p.z) + 0.02f;
                IslandPrimitives.AddGrassTuft(buffers.GrassMid, p,
                    0.34f + 0.32f * SceneArtHash.Hash01(seed, i, 751), seed + i * 31, 5);
            }
        }

        // ==================================================================
        // 6：瞭望台（海盗味）
        // ==================================================================

        /// <summary>
        /// 崖沿木制瞭望台：四柱高台 + 木铺板 + 围栏 + 桅杆（挂海盗旗）+ 挂灯 + 爬梯 + 木箱木桶。
        /// 【为什么要有它】"浮空岛"+"海盗舰队"两个题材的交点就是这座台子——
        /// 一件木构把这座岛从"风景"变成"这个地方属于某个人"，也让旗帜在高处有风可吃。
        /// </summary>
        static void AddWatchPlatform(IslandBuffers buffers, FloatingIslandSpec spec, Vector3 center, int seed)
        {
            MeshBuffers wood = buffers.Wood;
            float groundY = center.y;
            float deckY = groundY + 0.62f;
            float half = 1.6f;

            // 四柱（从地面升到台面）
            for (int i = 0; i < 4; i++)
            {
                float dx = (i % 2 == 0 ? -1f : 1f) * half;
                float dz = (i < 2 ? -1f : 1f) * half;
                wood.AddBox(new Vector3(center.x + dx, groundY + 0.34f, center.z + dz),
                    new Vector3(0.16f, 0.72f, 0.16f), SceneArtHash.Hash01(seed, i, 801) * 30f);
            }

            // 台面铺板（6 块，逐块微小偏转 → 手作感）
            for (int i = 0; i < 6; i++)
            {
                float t = (i + 0.5f) / 6f;
                float z = center.z - half + t * (half * 2f);
                wood.AddBox(new Vector3(center.x, deckY, z),
                    new Vector3(half * 2f + 0.2f, 0.11f, (half * 2f) / 6f * 0.86f),
                    SceneArtHash.SignedHash(seed, i, 811) * 3.5f);
            }

            // 围栏：西/北两面（面向岛内留开口，供爬梯上来）
            float railY = deckY + 0.86f;
            wood.AddBox(new Vector3(center.x - half, railY, center.z), new Vector3(0.10f, 0.10f, half * 2f), 0f);
            wood.AddBox(new Vector3(center.x, railY, center.z + half), new Vector3(half * 2f, 0.10f, 0.10f), 0f);
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f;
                wood.AddBox(new Vector3(center.x - half, deckY + 0.44f, center.z - half + t * half * 2f),
                    new Vector3(0.10f, 0.86f, 0.10f), 0f);
                wood.AddBox(new Vector3(center.x - half + t * half * 2f, deckY + 0.44f, center.z + half),
                    new Vector3(0.10f, 0.86f, 0.10f), 0f);
            }

            // 桅杆 + 横桁 + 海盗旗
            Vector3 mastBase = new Vector3(center.x + half * 0.82f, deckY + 0.06f, center.z + half * 0.82f);
            float mastH = 4.2f;
            wood.AddFrustum(mastBase, 0.11f, 0.075f, mastH, 6, 0f);
            Vector3 mastTop = mastBase + Vector3.up * mastH;
            Vector3 yardDir = new Vector3(Mathf.Cos(spec.WatchAngle + 0.4f), 0f, Mathf.Sin(spec.WatchAngle + 0.4f));
            wood.AddRod(mastTop - Vector3.up * 0.42f - yardDir * 1.25f, mastTop - Vector3.up * 0.42f + yardDir * 1.25f,
                0.055f, 5);

            // 旗：从横桁往下挂的竖帘（波浪靠帘心线的横向漂移做），双面
            {
                Vector3 flagAnchor = mastTop - Vector3.up * 0.52f - yardDir * 0.95f;
                Vector3 faceNormal = new Vector3(-yardDir.z, 0f, yardDir.x);
                var fpts = new Vector3[4];
                var fw = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    float t = k / 3f;
                    fpts[k] = flagAnchor + Vector3.down * (1.55f * t)
                        + faceNormal * (0.26f * Mathf.Sin(t * 3.0f)) + yardDir * (0.10f * t);
                    fw[k] = 0.62f * (1f - 0.22f * t);
                }
                IslandPrimitives.AddRibbon(buffers.Banner, fpts, fw, faceNormal);
            }

            // 挂灯（桅杆下一盏 + 台角一盏）
            IslandPrimitives.AddLantern(buffers, new Vector3(center.x - half * 0.78f, deckY + 0.02f, center.z - half * 0.78f),
                1.15f, seed + 41);
            {
                Vector3 lamp = mastBase + Vector3.up * 2.1f - yardDir * 0.55f;
                buffers.Wood.AddRod(mastBase + Vector3.up * 2.15f, lamp, 0.035f, 4);
                IslandPrimitives.AddBlob(buffers.Glow, lamp - Vector3.up * 0.16f,
                    new Vector3(0.17f, 0.20f, 0.17f), 2, 6, seed + 53, 821, 0.18f);
            }

            // 爬梯：两根边梁 + 7 级踏木（开口一侧）
            Vector3 ladderTop = new Vector3(center.x, deckY + 0.02f, center.z - half);
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                wood.AddBox(ladderTop + Vector3.down * (0.72f * t) + Vector3.forward * 0.26f,
                    new Vector3(0.9f, 0.055f, 0.055f), 0f);
            }
            wood.AddBox(ladderTop + Vector3.down * 0.36f + Vector3.forward * 0.26f + Vector3.right * 0.42f,
                new Vector3(0.07f, 0.78f, 0.07f), 0f);
            wood.AddBox(ladderTop + Vector3.down * 0.36f + Vector3.forward * 0.26f - Vector3.right * 0.42f,
                new Vector3(0.07f, 0.78f, 0.07f), 0f);

            // 木箱 + 木桶（货物）
            for (int i = 0; i < 3; i++)
            {
                float ang = spec.WatchAngle + 1.7f + i * 0.5f;
                Vector3 p = new Vector3(center.x + Mathf.Cos(ang) * 2.3f, 0f, center.z + Mathf.Sin(ang) * 2.3f);
                p.y = PlateauY(spec, p.x, p.z) + 0.02f;
                float s = 0.42f + 0.16f * SceneArtHash.Hash01(seed, i, 831);
                wood.AddBox(new Vector3(p.x, p.y + s * 0.5f, p.z), new Vector3(s, s, s),
                    SceneArtHash.Hash01(seed, i, 839) * 60f);
                // 箱角铁（暗木带过，视觉上就是铁箍）
                buffers.Dirt.AddBox(new Vector3(p.x, p.y + s * 0.5f, p.z),
                    new Vector3(s * 1.03f, s * 0.16f, s * 1.03f), SceneArtHash.Hash01(seed, i, 839) * 60f);
            }
            {
                Vector3 b = new Vector3(center.x + Mathf.Cos(spec.WatchAngle - 1.2f) * 2.1f, 0f,
                    center.z + Mathf.Sin(spec.WatchAngle - 1.2f) * 2.1f);
                b.y = PlateauY(spec, b.x, b.z) + 0.02f;
                wood.AddFrustum(b, 0.30f, 0.28f, 0.62f, 7, 0f);
                buffers.Dirt.AddRod(b + Vector3.up * 0.14f, b + Vector3.up * 0.48f, 0.305f, 7);
            }
        }

        // ==================================================================
        // 7：小径
        // ==================================================================

        /// <summary>
        /// 遗迹 → 瞭望台的 16 块石板（二次贝塞尔采样，逐块贴草皮面并朝切线偏转）+ 3 盏灯柱 + 2 座石堆。
        /// 【为什么石板是"块"而不是"条"】低模里连续贴地的条带会与草皮面 z-fighting；
        /// 断续的石板既避开这个问题，也在俯视时给出"人走出来的路"的节奏感。
        /// </summary>
        static void AddPath(IslandBuffers buffers, FloatingIslandSpec spec, Vector3 ruin, Vector3 watch, int seed)
        {
            MeshBuffers stone = buffers.Stone;
            Vector3 a = ruin;
            Vector3 b = watch;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 perp = new Vector3(-(b.z - a.z), 0f, b.x - a.x);
            if (perp.sqrMagnitude > 1e-6f)
                perp.Normalize();
            Vector3 control = mid + perp * (Vector3.Distance(a, b) * 0.22f);

            int slabs = 16;
            Vector3 prev = a;
            for (int i = 0; i < slabs; i++)
            {
                float t = (i + 0.5f) / slabs;
                Vector3 p = Bezier(a, control, b, t);
                p.y = PlateauY(spec, p.x, p.z) + 0.05f;

                Vector3 tangent = p - prev;
                float yaw = tangent.sqrMagnitude > 1e-6f ? Mathf.Atan2(tangent.z, tangent.x) * Mathf.Rad2Deg : 0f;
                float w = 0.92f + 0.20f * SceneArtHash.Hash01(seed, i, 901);
                stone.AddBox(new Vector3(p.x, p.y, p.z), new Vector3(w, 0.10f, w * 0.78f),
                    yaw + SceneArtHash.SignedHash(seed, i, 907) * 9f);
                prev = p;

                // 灯柱：每 5 块石板一盏，交替左右
                if (i % 5 == 2)
                {
                    Vector3 lampPos = p + new Vector3(-tangent.z, 0f, tangent.x).normalized * ((i % 10 == 2 ? 1f : -1f) * 1.15f);
                    lampPos.y = PlateauY(spec, lampPos.x, lampPos.z) + 0.02f;
                    IslandPrimitives.AddLantern(buffers, lampPos, 1.25f, seed + i * 13);
                }
            }

            // 石堆（路标）：两座
            for (int i = 0; i < 2; i++)
            {
                float t = i == 0 ? 0.26f : 0.72f;
                Vector3 p = Bezier(a, control, b, t);
                Vector3 side = new Vector3(-(b.z - a.z), 0f, b.x - a.x).normalized * ((i == 0 ? 1f : -1f) * 1.9f);
                Vector3 c = p + side;
                c.y = PlateauY(spec, c.x, c.z);
                IslandPrimitives.AddCairn(stone, c, 0.78f + 0.22f * SceneArtHash.Hash01(seed, i, 911), seed + i * 37);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return a * (u * u) + control * (2f * u * t) + b * (t * t);
        }

        // ==================================================================
        // 8：植被
        // ==================================================================

        /// <summary>老树（岛主景）+ 树群 + 灌木 + 草簇 + 发光花 + 崖沿垂藤。</summary>
        static void AddVegetation(IslandBuffers buffers, FloatingIslandSpec spec, Vector3 ruin, Vector3 watch,
            List<KeepOutZone> keepOut, int seed)
        {
            var zones = new List<KeepOutZone>(keepOut);

            // 老树：立在西侧缓丘上（丘顶最高处，树冠正好是侧影的最高点）
            Vector3 elder = OnPlateau(spec, 2.55f, 0.34f);
            IslandPrimitives.AddElderTree(buffers, elder, 5.6f, 2.55f * Mathf.Rad2Deg + 20f, seed + 5);
            zones.Add(new KeepOutZone(elder, 2.4f));

            // 树群
            int placed = 0;
            for (int i = 0; i < spec.TreeCount * 6 && placed < spec.TreeCount; i++)
            {
                if (!TryFindSpot(seed + 17, i, spec, zones, 0.16f, 0.80f, out Vector3 p))
                    continue;

                float h = 2.5f + 1.9f * SceneArtHash.Hash01(seed, i, 971);
                float yaw = SceneArtHash.Hash01(seed, i, 977) * 360f;
                IslandPrimitives.AddTree(buffers, p, h, yaw, seed + i * 7);
                zones.Add(new KeepOutZone(p, 1.5f));
                placed++;
            }

            // 灌木
            placed = 0;
            for (int i = 0; i < spec.BushCount * 8 && placed < spec.BushCount; i++)
            {
                if (!TryFindSpot(seed + 31, i, spec, zones, 0.14f, 0.90f, out Vector3 p))
                    continue;

                IslandPrimitives.AddBush(buffers.GrassMid, p, 0.45f + 0.45f * SceneArtHash.Hash01(seed, i, 983),
                    seed + i * 11);
                placed++;
            }

            // 草簇（不占预留区，但允许靠近，故不写入 zones —— 否则会把顶面塞满排斥圈）
            for (int i = 0; i < spec.GrassTuftCount; i++)
            {
                if (!TryFindSpot(seed + 47, i, spec, keepOut, 0.08f, 0.97f, out Vector3 p))
                    continue;

                MeshBuffers target = SceneArtHash.Hash01(seed, i, 991) > 0.72f
                    ? buffers.GrassLight : buffers.GrassMid;
                IslandPrimitives.AddGrassTuft(target, p, 0.28f + 0.34f * SceneArtHash.Hash01(seed, i, 997),
                    seed + i * 13, 4 + (int)(SceneArtHash.Hash01(seed, i, 1009) * 3f));
            }

            // 发光花簇
            for (int i = 0; i < spec.FlowerCount; i++)
            {
                if (!TryFindSpot(seed + 61, i, spec, keepOut, 0.14f, 0.90f, out Vector3 p))
                    continue;

                IslandPrimitives.AddFlowerCluster(buffers, p, 0.30f + 0.22f * SceneArtHash.Hash01(seed, i, 1013),
                    seed + i * 17);
            }

            // 垂藤：挂在一圈崖沿上
            for (int i = 0; i < spec.VineCount; i++)
            {
                float ang = Mathf.PI * 2f * i / Mathf.Max(1, spec.VineCount)
                    + SceneArtHash.SignedHash(seed, i, 1021) * 0.55f;
                float x = Mathf.Cos(ang) * spec.RadiusX * 0.99f;
                float z = Mathf.Sin(ang) * spec.RadiusZ * 0.99f;
                Vector3 top = new Vector3(x, PlateauY(spec, x, z) - 0.55f, z);
                IslandPrimitives.AddVine(buffers.GrassDark, top,
                    1.8f + 3.4f * SceneArtHash.Hash01(seed, i, 1031), ang * Mathf.Rad2Deg, seed + i * 19);
            }

            // 崖沿外挂的草皮团（把轮廓再打散一层）
            for (int i = 0; i < 10; i++)
            {
                float ang = SceneArtHash.Hash01(seed, i, 1039) * Mathf.PI * 2f;
                float s = 0.98f + 0.06f * SceneArtHash.Hash01(seed, i, 1049);
                float x = Mathf.Cos(ang) * spec.RadiusX * s;
                float z = Mathf.Sin(ang) * spec.RadiusZ * s;
                Vector3 c = new Vector3(x, PlateauY(spec, x, z) - 0.25f, z);
                float r = 0.34f + 0.40f * SceneArtHash.Hash01(seed, i, 1051);
                IslandPrimitives.AddBlob(buffers.GrassDark, c, new Vector3(r, r * 0.42f, r * 0.9f), 2, 6,
                    seed + i * 23, 1053, 0.20f);
            }
        }

        // ==================================================================
        // 9：天外（悬浮晶 / 浮石 / 萤光尘 / 云 / 鸟）
        // ==================================================================

        /// <summary>
        /// 岛外世界：9 颗悬浮晶、7 块浮石（带草帽与晶）、64 点萤光尘、12 团云 + 岛底云裙、6 只飞鸟。
        /// 【构图用意】这些都不是"装饰"，而是**尺度参照**：没有它们，浮空岛只是一块悬空的石头，
        /// 看不出多大、多高、是否在动。云给出海拔，晶给出魔力，鸟给出尺度。
        /// </summary>
        static void AddOuterWorld(IslandBuffers buffers, FloatingIslandSpec spec, int seed)
        {
            // 悬浮晶簇
            for (int i = 0; i < spec.SatelliteCrystalCount; i++)
            {
                float ang = i * 2.399963f + SceneArtHash.SignedHash(seed, i, 1101) * 0.6f;
                float r = 1.32f + 0.72f * SceneArtHash.Hash01(seed, i, 1103);
                float y = -8f + 17f * SceneArtHash.Hash01(seed, i, 1109);
                Vector3 p = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r, y, Mathf.Sin(ang) * spec.RadiusZ * r);

                IslandPrimitives.AddCrystal(buffers.Crystal, p,
                    0.34f + 0.42f * SceneArtHash.Hash01(seed, i, 1117),
                    1.25f + 1.45f * SceneArtHash.Hash01(seed, i, 1123),
                    new Vector3(12f + 34f * SceneArtHash.Hash01(seed, i, 1129), ang * Mathf.Rad2Deg,
                        12f + 34f * SceneArtHash.Hash01(seed, i, 1131)), seed + i * 29, 5, 0.14f);

                // 每两颗晶旁边挂一点萤光尘
                if (i % 2 == 0)
                {
                    for (int m = 0; m < 3; m++)
                    {
                        float ma = ang + SceneArtHash.SignedHash(seed, i * 3 + m, 1137) * 1.2f;
                        float mr = r * (0.92f + 0.22f * SceneArtHash.Hash01(seed, i * 3 + m, 1141));
                        Vector3 mc = new Vector3(Mathf.Cos(ma) * spec.RadiusX * mr,
                            y + SceneArtHash.SignedHash(seed, i * 3 + m, 1151) * 1.6f,
                            Mathf.Sin(ma) * spec.RadiusZ * mr);
                        float s = 0.07f + 0.07f * SceneArtHash.Hash01(seed, i * 3 + m, 1153);
                        IslandPrimitives.AddBlob(buffers.Glow, mc, new Vector3(s, s, s), 2, 5,
                            seed + i * 7 + m, 1157, 0.22f);
                    }
                }
            }

            // 浮石（带草帽 / 带小晶）：岛外的"卫星碎片"
            for (int i = 0; i < spec.FloatingRockCount; i++)
            {
                float ang = i * 2.399963f + 1.9f + SceneArtHash.SignedHash(seed, i, 1161) * 0.7f;
                float r = 1.22f + 0.92f * SceneArtHash.Hash01(seed, i, 1163);
                float rad = 0.85f + 1.6f * SceneArtHash.Hash01(seed, i, 1169);
                float y = -7f + 11f * SceneArtHash.Hash01(seed, i, 1171);
                Vector3 c = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r, y, Mathf.Sin(ang) * spec.RadiusZ * r);

                buffers.RockMid.AddRock(c, rad, new Vector3(1.15f, 0.88f, 1.0f), seed + i * 31, 7);

                // 草帽：顶面一片压扁的草皮球团
                if (SceneArtHash.Hash01(seed, i, 1173) > 0.35f)
                {
                    MeshBuffers cap = SceneArtHash.Hash01(seed, i, 1179) > 0.5f
                        ? buffers.GrassMid : buffers.GrassDark;
                    Vector3 top = c + Vector3.up * (rad * 0.72f);
                    IslandPrimitives.AddBlob(cap, top, new Vector3(rad * 0.82f, rad * 0.24f, rad * 0.80f),
                        2, 7, seed + i * 41, 1181, 0.16f);
                }

                // 小晶
                if (SceneArtHash.Hash01(seed, i, 1187) > 0.45f)
                {
                    IslandPrimitives.AddCrystal(buffers.Crystal, c + Vector3.up * (rad * 0.62f),
                        0.16f + 0.16f * SceneArtHash.Hash01(seed, i, 1191),
                        0.55f + 0.55f * SceneArtHash.Hash01(seed, i, 1193),
                        new Vector3(16f, ang * Mathf.Rad2Deg, 22f), seed + i * 43, 4, 0.16f);
                }

                // 底面几根小石笋，浮石才有"撕下来"的读感
                for (int s = 0; s < 2; s++)
                {
                    Vector3 root = c + Vector3.down * (rad * 0.6f)
                        + new Vector3(Mathf.Cos(ang + s), 0f, Mathf.Sin(ang + s)) * (rad * 0.28f);
                    IslandPrimitives.AddStalactite(buffers.RockDark, root,
                        rad * (0.5f + 0.5f * SceneArtHash.Hash01(seed, i * 2 + s, 1197)), rad * 0.28f,
                        ang * Mathf.Rad2Deg, 12f, seed + i * 5 + s);
                }
            }

            // 萤光尘（两色混编：暖光与晶体）
            for (int i = 0; i < spec.MoteCount; i++)
            {
                float ang = i * 2.399963f + SceneArtHash.SignedHash(seed, i, 1201) * 0.9f;
                float r = 0.55f + 1.35f * SceneArtHash.Hash01(seed, i, 1207);
                float y = -15f + 26f * SceneArtHash.Hash01(seed, i, 1213);
                Vector3 p = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r, y, Mathf.Sin(ang) * spec.RadiusZ * r);
                float s = 0.045f + 0.085f * SceneArtHash.Hash01(seed, i, 1217);

                MeshBuffers target = i % 3 == 0 ? buffers.Crystal : buffers.Glow;
                IslandPrimitives.AddCrystal(target, p, s * 0.72f, s * 2.1f,
                    new Vector3(20f * SceneArtHash.Hash01(seed, i, 1223), ang * Mathf.Rad2Deg,
                        20f * SceneArtHash.Hash01(seed, i, 1229)), seed + i * 53, 4, 0.18f);
            }

            // 云团：低层厚云（托住岛）/ 中层 / 高层薄云
            for (int i = 0; i < spec.CloudPuffCount; i++)
            {
                int tier = i % 3;
                float ang = i * 2.399963f + SceneArtHash.SignedHash(seed, i, 1231) * 0.8f;
                float size;
                float y;
                float r;
                switch (tier)
                {
                    case 0:
                        size = 3.0f + 2.4f * SceneArtHash.Hash01(seed, i, 1237);
                        y = -12f - 7f * SceneArtHash.Hash01(seed, i, 1249);
                        r = 0.95f + 0.95f * SceneArtHash.Hash01(seed, i, 1259);
                        break;
                    case 1:
                        size = 2.2f + 1.4f * SceneArtHash.Hash01(seed, i, 1237);
                        y = -2f + 7f * SceneArtHash.Hash01(seed, i, 1249);
                        r = 1.45f + 0.95f * SceneArtHash.Hash01(seed, i, 1259);
                        break;
                    default:
                        size = 1.8f + 1.3f * SceneArtHash.Hash01(seed, i, 1237);
                        y = 5f + 6f * SceneArtHash.Hash01(seed, i, 1249);
                        r = 1.85f + 1.05f * SceneArtHash.Hash01(seed, i, 1259);
                        break;
                }

                Vector3 c = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r, y, Mathf.Sin(ang) * spec.RadiusZ * r);
                IslandPrimitives.AddCloudPuff(buffers.Cloud, c, size, seed + i * 59, 4 + i % 3);
            }

            // 岛底云裙：直接挂在岛尖下方，把岛"托"起来
            for (int i = 0; i < 3; i++)
            {
                float ang = Mathf.PI * 2f * i / 3f + SceneArtHash.Hash01(seed, i, 1261) * 1.2f;
                Vector3 c = new Vector3(Mathf.Cos(ang) * spec.RadiusX * 0.22f,
                    -spec.RockDepth - 0.4f - 1.1f * i,
                    Mathf.Sin(ang) * spec.RadiusZ * 0.22f);
                IslandPrimitives.AddCloudPuff(buffers.Cloud, c, 2.8f + 1.2f * SceneArtHash.Hash01(seed, i, 1267),
                    seed + 1300 + i, 5);
            }

            // 飞鸟
            for (int i = 0; i < spec.BirdCount; i++)
            {
                float ang = i * 2.399963f + 0.4f + SceneArtHash.SignedHash(seed, i, 1277) * 0.6f;
                float r = 0.85f + 0.95f * SceneArtHash.Hash01(seed, i, 1279);
                Vector3 p = new Vector3(Mathf.Cos(ang) * spec.RadiusX * r,
                    5.5f + 9f * SceneArtHash.Hash01(seed, i, 1283), Mathf.Sin(ang) * spec.RadiusZ * r);
                IslandPrimitives.AddBird(buffers.RockDark, p, ang * Mathf.Rad2Deg + 90f,
                    0.34f + 0.34f * SceneArtHash.Hash01(seed, i, 1289), seed + i * 61);
            }
        }

        // ==================================================================
        // 工具
        // ==================================================================

        /// <summary>两个方位角的最小夹角（弧度）。</summary>
        static float AngleDistance(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a - b, Mathf.PI * 2f));
            return Mathf.Min(d, Mathf.PI * 2f - d);
        }

        /// <summary>取环上方位最接近 <paramref name="angle"/> 的顶点下标。</summary>
        static int IndexNearest(Vector3[] ring, float angle)
        {
            float best = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < ring.Length; i++)
            {
                float a = Mathf.Atan2(ring[i].z, ring[i].x);
                float d = AngleDistance(a, angle);
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        /// <summary>
        /// 在草皮面上找一个落点：黄金角螺旋 + 抖动，最多试 10 次，
        /// 必须落在 [innerScale,outerScale] 环带内且不压到任何预留区。
        /// 【为什么要"试"而不是"算"】拒绝采样比手算可行域简单得多，且**确定性**
        /// （同 seed 同 index 必得同点）——这就够无头断言了。
        /// </summary>
        static bool TryFindSpot(int seed, int index, FloatingIslandSpec spec, List<KeepOutZone> keepOut,
            float innerScale, float outerScale, out Vector3 pos)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int s = seed + index * 131 + attempt * 977;
                float ang = index * 2.399963f + SceneArtHash.SignedHash(s, index, 7) * 0.85f;
                float r = Mathf.Lerp(innerScale, outerScale, Mathf.Sqrt(SceneArtHash.Hash01(s, index, 11)));

                float x = Mathf.Cos(ang) * spec.RadiusX * r;
                float z = Mathf.Sin(ang) * spec.RadiusZ * r;
                float y = PlateauY(spec, x, z);

                bool ok = true;
                if (keepOut != null)
                {
                    for (int k = 0; k < keepOut.Count; k++)
                    {
                        KeepOutZone zone = keepOut[k];
                        float dx = x - zone.X, dz = z - zone.Z;
                        if (dx * dx + dz * dz < zone.Radius * zone.Radius)
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (ok)
                {
                    pos = new Vector3(x, y, z);
                    return true;
                }
            }

            pos = Vector3.zero;
            return false;
        }

        /// <summary>草皮面上的圆形预留区（<see cref="TryFindSpot"/> 用）。</summary>
        struct KeepOutZone
        {
            public readonly float X;
            public readonly float Z;
            public readonly float Radius;

            public KeepOutZone(Vector3 center, float radius)
            {
                X = center.x;
                Z = center.z;
                Radius = radius;
            }
        }
    }

    /// <summary>
    /// 空岛顶面的**预留区**（公开快照，<see cref="FloatingIslandComposer.BuildKeepOutZones"/> 产出）：
    /// 遗迹台基 / 瞭望台 / 老树 / 水潭——出生点与任何"要站在顶面上的新零件"都必须避开它们。
    /// </summary>
    public readonly struct IslandKeepOutZone
    {
        /// <summary>区域中心（局部坐标，y 已贴草皮面）。</summary>
        public readonly Vector3 Center;

        /// <summary>区域半径（世界单位）。</summary>
        public readonly float Radius;

        /// <summary>构造预留区。</summary>
        public IslandKeepOutZone(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }
}
