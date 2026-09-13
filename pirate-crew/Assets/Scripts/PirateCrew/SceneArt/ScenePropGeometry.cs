using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 场景道具的**分材质三角面缓冲组**（纯 C#，无头可测）。
    ///
    /// 【为什么要分组】合批的前提是"同材质合到一个网格"。编辑器构建时把全部同类道具
    /// 写进同一个缓冲，最后每个缓冲落成 1 个网格 + 1 个材质 → 1 个 DrawCall
    /// （场景文档 §8「同类共材质 + GPU Instancing / 静态合批」，本实现取"构建期合并"这条
    /// 更彻底的路：DrawCall 与实例数无关）。
    ///
    /// 【分组与材质的对应】见 <c>Assets/Editor/SceneArtBuilder.cs</c>：
    ///   Wood/WoodDark/Rock/Metal/Foliage/Cloth/FlagRed/FlagBlue 用
    ///   <c>PirateCrew/PirateOutline</c>（本体 + #2A2A2A 描边，满足场景文档 M12）；
    ///   Foam/Danger/WaterDark 用 URP/Unlit 半透明（不参与光照、不投影，场景文档 §5.3）；
    ///   Silhouette/Cloud 用 URP/Unlit（远景剪影与云带）；
    ///   SandWet 用环境湿沙材质（潮间带坡 + 海床坡）。
    /// </summary>
    public sealed class ScenePropBuffers
    {
        /// <summary>木材中档 #A67B42：船舷、甲板、木箱、栈桥面、桅杆、旗杆。</summary>
        public readonly MeshBuffers Wood = new MeshBuffers();

        /// <summary>木材暗档 #6B4C28：水线以下船板、桶身、栈桥桩。</summary>
        public readonly MeshBuffers WoodDark = new MeshBuffers();

        /// <summary>岩石档 #8C7B6A：礁石、掩体石、潮间带石、海床坡。</summary>
        public readonly MeshBuffers Rock = new MeshBuffers();

        /// <summary>铁档 #5C4F42：锚、锚链、铁箍、箱角铁、桅顶铁环。</summary>
        public readonly MeshBuffers Metal = new MeshBuffers();

        /// <summary>植被档 #4A8C4A：棕榈叶、灌木、草丛。</summary>
        public readonly MeshBuffers Foliage = new MeshBuffers();

        /// <summary>布/索档 #D4A76A：帆布、缆绳、贝壳碎屑。</summary>
        public readonly MeshBuffers Cloth = new MeshBuffers();

        /// <summary>红队旗 #FF3A29。</summary>
        public readonly MeshBuffers FlagRed = new MeshBuffers();

        /// <summary>蓝队旗 #3366FF。</summary>
        public readonly MeshBuffers FlagBlue = new MeshBuffers();

        /// <summary>浪花/泡沫（URP/Unlit 半透明白）。</summary>
        public readonly MeshBuffers Foam = new MeshBuffers();

        /// <summary>落水危险虚线（URP/Unlit 半透明 #CC2222）。</summary>
        public readonly MeshBuffers Danger = new MeshBuffers();

        /// <summary>岸边暗水带（URP/Unlit 半透明 #1A4F7A）。</summary>
        public readonly MeshBuffers WaterDark = new MeshBuffers();

        /// <summary>远景剪影岛/帆船（URP/Unlit 不透明）。</summary>
        public readonly MeshBuffers Silhouette = new MeshBuffers();

        /// <summary>云带/低模积云（URP/Unlit 半透明白）。</summary>
        public readonly MeshBuffers Cloud = new MeshBuffers();

        /// <summary>湿沙（潮间带坡 / 水下海床坡）。</summary>
        public readonly MeshBuffers SandWet = new MeshBuffers();

        /// <summary>全部组的三角面总和。</summary>
        public int TotalTriangles
        {
            get
            {
                return Wood.TriangleCount + WoodDark.TriangleCount + Rock.TriangleCount
                    + Metal.TriangleCount + Foliage.TriangleCount + Cloth.TriangleCount
                    + FlagRed.TriangleCount + FlagBlue.TriangleCount + Foam.TriangleCount
                    + Danger.TriangleCount + WaterDark.TriangleCount + Silhouette.TriangleCount
                    + Cloud.TriangleCount + SandWet.TriangleCount;
            }
        }
    }

    /// <summary>
    /// 道具几何生成（纯 C#，无头可测）。每个 <c>Add*</c> 都接受**底面着地点**
    /// （除注明者），方便直接按地形 <c>SurfaceWorldY</c> 落地。
    ///
    /// 【尺寸出处】<c>docs/场景设计-战斗竞技场.md</c> §3.2/§3.4/§4.1-§4.6
    /// （该文档把这些尺寸标为【AI 提案】，本文件沿用并逐条注明）。
    ///
    /// 【纪律】本类只写三角面，**不产生任何 Collider**（场景文档 §9.4：除地形方块外所有装饰无碰撞体，
    /// 以免投掷物被"不该挡的东西"弹开而破坏"预览 = 实弹"）。
    /// </summary>
    public static class ScenePropGeometry
    {
        // ------------------------------------------------------------------
        // 通用零件
        // ------------------------------------------------------------------

        /// <summary>闭合圆环（桅顶铁环、锚环、链环）：轴 = <paramref name="axis"/>。</summary>
        public static void AddRingLoop(MeshBuffers b, Vector3 center, Vector3 axis, float radius,
            float tubeRadius, int segments = 8)
        {
            Vector3 n = axis.normalized;
            Vector3 refDir = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 side = Vector3.Cross(n, refDir).normalized;
            Vector3 fwd = Vector3.Cross(n, side).normalized;

            var points = new List<Vector3>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float ang = Mathf.PI * 2f * i / segments;
                points.Add(center + (side * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)) * radius);
            }

            b.AddBentTube(points, tubeRadius, tubeRadius, 4);
        }

        /// <summary>锚链段：<paramref name="links"/> 个交替朝向的椭圆环连成一段。</summary>
        public static void AddChain(MeshBuffers b, Vector3 from, Vector3 to, int links, float ringRadius = 0.12f)
        {
            links = Mathf.Max(2, links);
            for (int i = 0; i < links; i++)
            {
                float t = i / (float)(links - 1);
                Vector3 center = Vector3.Lerp(from, to, t);
                // 相邻链环绕链轴交替旋转 90°，是"链"的辨识特征。
                Vector3 axis = i % 2 == 0 ? Vector3.up : Vector3.right;
                AddRingLoop(b, center, axis, ringRadius * 0.55f, 0.03f, 6);
            }
        }

        // ------------------------------------------------------------------
        // §4.3 木箱与火药桶
        // ------------------------------------------------------------------

        /// <summary>
        /// 木箱：0.6 立方 + 板缝角铁（场景文档 §4.3）。<paramref name="basePos"/> = 底面中心。
        /// 箱体走木档，竖向角铁与两道横箍走铁档。
        /// </summary>
        public static void AddCrate(ScenePropBuffers b, Vector3 basePos, float yawDegrees, float size = 0.6f)
        {
            var body = new MeshBuffers();
            body.AddBox(new Vector3(0f, size * 0.5f, 0f), new Vector3(size, size, size));
            b.Wood.AppendTransformed(body, SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, yawDegrees, 0f), Vector3.one));

            var iron = new MeshBuffers();
            float batten = size * 0.07f;
            float half = size * 0.5f - batten * 0.5f;
            // 4 根竖向角铁。
            iron.AddBox(new Vector3(-half, size * 0.5f, -half), new Vector3(batten, size * 1.01f, batten));
            iron.AddBox(new Vector3(half, size * 0.5f, -half), new Vector3(batten, size * 1.01f, batten));
            iron.AddBox(new Vector3(-half, size * 0.5f, half), new Vector3(batten, size * 1.01f, batten));
            iron.AddBox(new Vector3(half, size * 0.5f, half), new Vector3(batten, size * 1.01f, batten));
            // 2 道横箍。
            for (int i = 0; i < 2; i++)
            {
                float y = size * (0.25f + 0.45f * i);
                iron.AddBox(new Vector3(0f, y, 0f), new Vector3(size * 1.01f, batten, size * 1.01f));
            }

            b.Metal.AppendTransformed(iron, SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, yawDegrees, 0f), Vector3.one));
        }

        /// <summary>
        /// 火药桶：圆柱 0.5 径 × 0.7 高 + 两道铁箍（场景文档 §4.3）。
        /// 桶身走暗木，箍走铁档；<paramref name="steelBand"/> 的警示漆由材质/贴图承担（本轮 0 贴图，见报告）。
        /// </summary>
        public static void AddBarrel(ScenePropBuffers b, Vector3 basePos, float yawDegrees, float radius = 0.25f, float height = 0.7f)
        {
            var body = new MeshBuffers();
            body.AddFrustum(Vector3.zero, radius * 0.92f, radius * 0.92f, 0.06f, 12);
            body.AddFrustum(new Vector3(0f, 0.06f, 0f), radius, radius * 0.86f, height - 0.12f, 12);
            body.AddFrustum(new Vector3(0f, height - 0.06f, 0f), radius * 0.86f, radius * 0.92f, 0.06f, 12);
            b.WoodDark.AppendTransformed(body, SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, yawDegrees, 0f), Vector3.one));

            var hoops = new MeshBuffers();
            hoops.AddFrustum(new Vector3(0f, height * 0.22f, 0f), radius * 1.03f, radius * 1.0f, 0.07f, 12, 0f, false, false);
            hoops.AddFrustum(new Vector3(0f, height * 0.68f, 0f), radius * 1.0f, radius * 0.95f, 0.07f, 12, 0f, false, false);
            b.Metal.AppendTransformed(hoops, SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, yawDegrees, 0f), Vector3.one));
        }

        // ------------------------------------------------------------------
        // §4.4 旗帜与告示牌
        // ------------------------------------------------------------------

        /// <summary>
        /// 旗杆（高 3.0-3.5、径 0.12）+ 撕裂边旗帜（场景文档 §4.4）。
        /// 杆走木档、杆顶铁球走铁档、旗面按队伍分档。
        /// </summary>
        public static void AddFlagPole(ScenePropBuffers b, Vector3 basePos, float yawDegrees, bool redTeam,
            float height = 3.2f, int seed = 0)
        {
            var pole = new MeshBuffers();
            pole.AddFrustum(Vector3.zero, 0.075f, 0.06f, height, 7);
            b.Wood.AppendTransformed(pole, SceneArtRot.Trs(basePos, Quaternion.identity, Vector3.one));

            var cap = new MeshBuffers();
            cap.AddRock(new Vector3(0f, height + 0.06f, 0f), 0.075f, new Vector3(1f, 1f, 1f), seed + 5, 6);
            b.Metal.AppendTransformed(cap, SceneArtRot.Trs(basePos, Quaternion.identity, Vector3.one));

            // 旗面：0.8 × 0.5，右下角带撕裂缺口（拆成两条不同长度的飘带）。
            MeshBuffers flag = redTeam ? b.FlagRed : b.FlagBlue;
            Quaternion yaw = SceneArtRot.Euler(0f, yawDegrees, 0f);
            float top = height - 0.1f;

            var cloth = new MeshBuffers();
            float fly = 0.8f, drop = 0.5f;
            // 上飘带（完整长度）。
            cloth.AddTrianglesDoubleSided(
                new Vector3(0f, top, 0f),
                new Vector3(fly * 0.62f, top - 0.03f, 0.03f),
                new Vector3(fly * 0.62f, top - drop * 0.5f, 0.05f),
                new Vector3(0f, top - drop * 0.5f, 0f),
                Vector3.forward);
            // 下飘带（撕裂：长度只有 62%）。
            cloth.AddTrianglesDoubleSided(
                new Vector3(0f, top - drop * 0.5f, 0f),
                new Vector3(fly * 0.62f, top - drop * 0.5f, 0.05f),
                new Vector3(fly * 0.4f, top - drop, 0.07f),
                new Vector3(0f, top - drop * 0.92f, 0f),
                Vector3.forward);

            flag.AppendTransformed(cloth, SceneArtRot.Trs(basePos, yaw, Vector3.one));
        }

        /// <summary>告示牌：柱 0.8 + 牌 0.6×0.4、斜角 10°（场景文档 §4.4；文字内容【待定】，本轮不画字）。</summary>
        public static void AddSignPost(ScenePropBuffers b, Vector3 basePos, float yawDegrees, float postHeight = 0.8f)
        {
            Quaternion yaw = SceneArtRot.Euler(0f, yawDegrees, 0f);
            var post = new MeshBuffers();
            post.AddFrustum(Vector3.zero, 0.06f, 0.05f, postHeight, 6);
            b.Wood.AppendTransformed(post, SceneArtRot.Trs(basePos, yaw, Vector3.one));

            var board = new MeshBuffers();
            board.AddBox(new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.42f, 0.05f));
            var matrix = SceneArtRot.Trs(basePos, yaw, Vector3.one)
                * SceneArtRot.Trs(new Vector3(0f, postHeight + 0.05f, 0.03f), SceneArtRot.Euler(10f, 0f, 0f), Vector3.one);
            b.WoodDark.AppendTransformed(board, matrix);
        }

        // ------------------------------------------------------------------
        // §4.5 锚与链
        // ------------------------------------------------------------------

        /// <summary>锚（总高 1.2、宽 0.9、厚 0.15，半埋露约 60%）+ 8-12 环锚链（场景文档 §4.5）。全走铁档。</summary>
        public static void AddAnchor(ScenePropBuffers b, Vector3 groundPos, float yawDegrees, float scale = 1f)
        {
            Quaternion yaw = SceneArtRot.Euler(0f, yawDegrees, 0f);
            var anchor = new MeshBuffers();

            // 半埋：整体下沉 0.4 × 总高。
            float sink = -0.48f * scale;
            Vector3 origin = new Vector3(0f, sink, 0f);

            // 锚杆（竖直）。
            anchor.AddBox(origin + new Vector3(0f, 0.55f * scale, 0f), new Vector3(0.09f, 0.9f, 0.09f) * scale);
            // 横杆（锚冠横木）。
            anchor.AddBox(origin + new Vector3(0f, 0.95f * scale, 0f), new Vector3(0.62f, 0.07f, 0.07f) * scale);
            // 锚环。
            AddRingLoop(anchor, origin + new Vector3(0f, 1.06f * scale, 0f), Vector3.forward, 0.1f * scale, 0.035f * scale, 6);
            // 两臂 + 锚爪。
            anchor.AddBox(origin + new Vector3(-0.26f, 0.2f * scale, 0f), new Vector3(0.1f, 0.55f, 0.12f) * scale);
            anchor.AddBox(origin + new Vector3(0.26f, 0.2f * scale, 0f), new Vector3(0.1f, 0.55f, 0.12f) * scale);
            anchor.AddBox(origin + new Vector3(-0.36f, 0.5f * scale, 0f), new Vector3(0.3f, 0.09f, 0.1f) * scale);
            anchor.AddBox(origin + new Vector3(0.36f, 0.5f * scale, 0f), new Vector3(0.3f, 0.09f, 0.1f) * scale);

            b.Metal.AppendTransformed(anchor, SceneArtRot.Trs(groundPos, yaw, Vector3.one));

            // 锚链：从锚环旁一段斜插进沙里（一端没入沙中，场景文档 §4.5）。
            var chain = new MeshBuffers();
            Vector3 chainStart = new Vector3(0.5f, 0.18f, 0f);
            Vector3 chainEnd = new Vector3(1.7f, -0.12f, 0.35f);
            AddChain(chain, chainStart, chainEnd, 9, 0.12f);
            b.Metal.AppendTransformed(chain, SceneArtRot.Trs(groundPos, yaw, Vector3.one));
        }

        // ------------------------------------------------------------------
        // §3.2 礁石 / 掩体石 / 潮间带石
        // ------------------------------------------------------------------

        /// <summary>不规则低模石块。<paramref name="basePos"/> = 触地点，石块中心自动抬高 <paramref name="radius"/>×0.6。</summary>
        public static void AddRock(ScenePropBuffers b, Vector3 basePos, float radius, int seed, float squash = 0.78f)
        {
            var rock = new MeshBuffers();
            rock.AddRock(new Vector3(0f, radius * squash * 0.85f, 0f), radius,
                new Vector3(1f, squash, 0.85f + 0.3f * SceneArtHash.Hash01(seed, 3, 5)), seed, 7);
            b.Rock.AppendTransformed(rock, SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, seed * 37f % 360f, 0f), Vector3.one));
        }

        // ------------------------------------------------------------------
        // §3.4 植被
        // ------------------------------------------------------------------

        /// <summary>
        /// 棕榈树：略弯的干（径 0.35→0.22、干高 4-6）+ 6-9 片下垂叶（叶长 2.2-3.0）
        /// （场景文档 §3.4）。干走木档、叶走植被档。**无碰撞**（§3.4：加碰撞会让投掷物在半空被弹开）。
        /// </summary>
        public static void AddPalm(ScenePropBuffers b, Vector3 basePos, float yawDegrees, float trunkHeight, int seed)
        {
            float lean = 0.55f + 0.5f * SceneArtHash.Hash01(seed, 1, 9);
            float leanDir = SceneArtHash.Hash01(seed, 2, 13) * Mathf.PI * 2f;

            var trunkPoints = new List<Vector3>(4);
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f;
                float bend = lean * t * t;
                trunkPoints.Add(new Vector3(
                    Mathf.Cos(leanDir) * bend,
                    trunkHeight * t,
                    Mathf.Sin(leanDir) * bend));
            }

            var trunk = new MeshBuffers();
            trunk.AddBentTube(trunkPoints, 0.30f, 0.19f, 6);
            b.Wood.AppendTransformed(trunk, SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, yawDegrees, 0f), Vector3.one));

            Vector3 top = trunkPoints[3];
            var leaves = new MeshBuffers();
            int leafCount = 6 + (int)(SceneArtHash.Hash01(seed, 5, 17) * 4f);   // 6-9 片
            for (int i = 0; i < leafCount; i++)
            {
                float ang = Mathf.PI * 2f * i / leafCount + SceneArtHash.Hash01(seed, i, 19) * 0.5f;
                float length = 2.2f + 0.8f * SceneArtHash.Hash01(seed, i, 23);
                float pitch = -0.22f - 0.42f * SceneArtHash.Hash01(seed, i, 29);
                Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Tan(pitch), Mathf.Sin(ang)).normalized;
                leaves.AddLeaf(top, dir, length, 0.42f, 0.85f, 3);
            }

            b.Foliage.AppendTransformed(leaves, SceneArtRot.Trs(basePos, Quaternion.identity, Vector3.one));
        }

        /// <summary>灌木：2-4 个 0.5-0.9 的圆丘成组（场景文档 §3.4）。走植被档。</summary>
        public static void AddBush(ScenePropBuffers b, Vector3 basePos, float scale, int seed)
        {
            var bush = new MeshBuffers();
            int blobs = 2 + (int)(SceneArtHash.Hash01(seed, 0, 3) * 3f);
            for (int i = 0; i < blobs; i++)
            {
                float ang = Mathf.PI * 2f * i / blobs + SceneArtHash.Hash01(seed, i, 7);
                float dist = 0.22f * scale * SceneArtHash.Hash01(seed, i, 11);
                float radius = (0.24f + 0.16f * SceneArtHash.Hash01(seed, i, 13)) * scale;
                bush.AddRock(
                    new Vector3(Mathf.Cos(ang) * dist, radius * 0.72f, Mathf.Sin(ang) * dist),
                    radius, new Vector3(1.15f, 1f, 1.15f), seed + i, 6);
            }

            b.Foliage.AppendTransformed(bush, SceneArtRot.Trs(basePos, Quaternion.identity, Vector3.one));
        }

        /// <summary>
        /// 草丛：单簇 0.2-0.4、3-5 片窄叶（场景文档 §3.4「单簇 0.2-0.4」）。
        /// 本轮用不透明低模叶代替 alpha-test 贴片（工程 0 贴图约束，见报告取舍）。
        /// </summary>
        public static void AddGrassTuft(ScenePropBuffers b, Vector3 basePos, float scale, int seed)
        {
            var grass = new MeshBuffers();
            int blades = 3 + (int)(SceneArtHash.Hash01(seed, 0, 5) * 3f);
            for (int i = 0; i < blades; i++)
            {
                float ang = Mathf.PI * 2f * i / blades + SceneArtHash.Hash01(seed, i, 3) * 0.9f;
                float h = (0.18f + 0.22f * SceneArtHash.Hash01(seed, i, 9)) * scale;
                float offset = 0.05f * SceneArtHash.Hash01(seed, i, 15);
                Vector3 baseLocal = new Vector3(Mathf.Cos(ang) * offset, 0f, Mathf.Sin(ang) * offset);
                Vector3 tip = baseLocal + new Vector3(
                    Mathf.Cos(ang) * h * 0.45f, h, Mathf.Sin(ang) * h * 0.45f);
                grass.AddRod(baseLocal, tip, 0.022f, 3);
            }

            b.Foliage.AppendTransformed(grass, SceneArtRot.Trs(basePos, Quaternion.identity, Vector3.one));
        }

        /// <summary>贝壳碎屑：0.05-0.12 的扁平小片（场景文档 §3.3）。走布档（浅色）。</summary>
        public static void AddShell(ScenePropBuffers b, Vector3 pos, float size, int seed)
        {
            b.Cloth.AddDisc(pos, size, 5, Vector3.up, false);
        }

        // ------------------------------------------------------------------
        // §4.6 散落杂物
        // ------------------------------------------------------------------

        /// <summary>散落杂物（断桨 / 破木板 / 烂渔网 / 空瓶 的形态族）。走木/布档。</summary>
        public static void AddDebris(ScenePropBuffers b, Vector3 basePos, float yawDegrees, int kind, int seed)
        {
            Quaternion yaw = SceneArtRot.Euler(0f, yawDegrees, 0f);
            var piece = new MeshBuffers();

            switch (kind % 4)
            {
                case 0:   // 断桨：长杆 + 桨叶
                    piece.AddBox(new Vector3(0f, 0.03f, 0f), new Vector3(1.55f, 0.06f, 0.07f));
                    piece.AddBox(new Vector3(0.86f, 0.03f, 0f), new Vector3(0.34f, 0.035f, 0.2f));
                    b.Wood.AppendTransformed(piece, SceneArtRot.Trs(basePos, yaw, Vector3.one));
                    break;
                case 1:   // 破木板
                    piece.AddBox(new Vector3(0f, 0.03f, 0f), new Vector3(1.0f, 0.05f, 0.3f));
                    b.Wood.AppendTransformed(piece, SceneArtRot.Trs(basePos, yaw, Vector3.one));
                    break;
                case 2:   // 烂渔网：几段交错细索
                    for (int i = 0; i < 3; i++)
                    {
                        float t = i / 2f - 0.5f;
                        piece.AddRod(new Vector3(-0.35f, 0.02f, t * 0.5f), new Vector3(0.35f, 0.02f, t * 0.5f), 0.014f, 3);
                        piece.AddRod(new Vector3(t * 0.5f, 0.02f, -0.35f), new Vector3(t * 0.5f, 0.02f, 0.35f), 0.014f, 3);
                    }
                    b.Cloth.AppendTransformed(piece, SceneArtRot.Trs(basePos, yaw, Vector3.one));
                    break;
                default:  // 空瓶：细长圆台
                    piece.AddFrustum(Vector3.zero, 0.06f, 0.035f, 0.16f, 6);
                    piece.AddFrustum(new Vector3(0f, 0.16f, 0f), 0.035f, 0.028f, 0.1f, 6);
                    b.WoodDark.AppendTransformed(piece, SceneArtRot.Trs(basePos, yaw, Vector3.one));
                    break;
            }
        }

        // ------------------------------------------------------------------
        // §4.2 木栈桥
        // ------------------------------------------------------------------

        /// <summary>
        /// 木栈桥：桥面宽 1.2-1.6、厚 0.12、面高 y=+0.6；桩径 0.3、桩距 1.2-1.5、顶 y=+0.6、底 y=-1.4
        /// （场景文档 §4.2）。桥面走木档、桩走暗木档（水位线下更深）。**纯装饰、无碰撞**。
        /// </summary>
        /// <param name="deckY">桥面顶面世界 Y。</param>
        public static void AddJetty(ScenePropBuffers b, float xFrom, float xTo, float centerZ, float deckY,
            float width = 1.4f, float pileBottomY = -1.4f)
        {
            float length = Mathf.Abs(xTo - xFrom);
            if (length < 0.5f)
                return;

            float dir = Mathf.Sign(xTo - xFrom);
            float midX = (xFrom + xTo) * 0.5f;

            var deck = new MeshBuffers();
            // 3 条纵向板 + 缝（板缝是"这是木栈道"的辨识特征）。
            for (int i = 0; i < 3; i++)
            {
                float z = centerZ + (i - 1) * (width / 3f);
                deck.AddBox(new Vector3(midX, deckY - 0.06f, z), new Vector3(length, 0.12f, width / 3f * 0.82f));
            }

            b.Wood.AppendTransformed(deck, Matrix4x4.identity);

            var piles = new MeshBuffers();
            float spacing = 1.35f;
            int count = Mathf.Max(2, Mathf.RoundToInt(length / spacing) + 1);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float x = xFrom + dir * length * t;
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = centerZ + s * (width * 0.5f - 0.12f);
                    float pileTop = deckY - 0.1f;
                    float h = pileTop - pileBottomY;
                    if (h <= 0f)
                        continue;

                    piles.AddFrustum(new Vector3(x, pileBottomY, z), 0.15f, 0.13f, h, 6);
                }
            }

            b.WoodDark.AppendTransformed(piles, Matrix4x4.identity);
        }

        // ------------------------------------------------------------------
        // §4.1 搁浅木船（全场主角）
        // ------------------------------------------------------------------

        /// <summary>
        /// 搁浅断成两截的木船：船体主残骸（两截 + 断裂口）、倾斜甲板、船肋骨外露、
        /// 断桅（向 -Z 倒）、横桁、缆绳、破帆、跳板（场景文档 §4.1）。
        /// <paramref name="basePos"/> = 船体世界中点、**水面线高度**（船体绕该点侧倾）。
        /// </summary>
        /// <param name="halfLength">单截长度（文档：整船长 12-14 → 半截 6-7）。</param>
        public static void AddWreck(ScenePropBuffers b, Vector3 basePos, float yawDegrees, float rollDegrees,
            float beam = 4.6f, float depth = 2.0f, float halfLength = 6.4f, int seed = 0)
        {
            Quaternion yaw = SceneArtRot.Euler(0f, yawDegrees, 0f);
            Matrix4x4 world = SceneArtRot.Trs(basePos, yaw, Vector3.one);

            var wood = new MeshBuffers();
            var woodDark = new MeshBuffers();
            var cloth = new MeshBuffers();
            var metal = new MeshBuffers();

            // ---- 两截船体：绕 X 轴侧倾（搁浅姿态），中间留断裂口 ----
            float gap = 0.75f;
            AddHullLoft(wood, woodDark, -halfLength, -gap, beam, deepOffset: 0f, depth: depth, stations: 7, seed: seed);
            AddHullLoft(wood, woodDark, gap, halfLength, beam, deepOffset: 0f, depth: depth, stations: 7, seed: seed + 31);

            // ---- 断裂口：外露肋骨（弧形的框）----
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                float zPos = Mathf.Lerp(-beam * 0.45f, beam * 0.45f, t);
                float yPos = -0.15f - 0.75f * Mathf.Abs(t - 0.5f) * 2f;
                AddRibFrame(woodDark, -gap - 0.1f, zPos, yPos, 0.16f);
                AddRibFrame(woodDark, gap + 0.1f, zPos, yPos, 0.16f);
            }

            // ---- 甲板（倾斜、残缺、板缝可见）----
            for (int i = 0; i < 5; i++)
            {
                float z = -beam * 0.36f + i * (beam * 0.72f / 4f);
                wood.AddBox(new Vector3(-halfLength * 0.55f, -0.55f, z), new Vector3(halfLength * 0.8f, 0.08f, 0.28f));
                wood.AddBox(new Vector3(halfLength * 0.5f, -0.5f, z), new Vector3(halfLength * 0.72f, 0.08f, 0.28f));
            }

            // ---- 主桅（折断，残高 6.4，基径 0.45→0.28，向 -Z 倾斜 22°）----
            var mast = new MeshBuffers();
            mast.AddFrustum(new Vector3(0f, -0.5f, 0f), 0.45f, 0.28f, 6.4f, 8);
            wood.AppendTransformed(mast, SceneArtRot.Trs(
                new Vector3(-halfLength * 0.42f, 0f, 0f), SceneArtRot.Euler(-22f, 0f, 0f), Vector3.one));

            // ---- 副桅（上截断裂的残桩，向 -Z 倒 12°）----
            var stump = new MeshBuffers();
            stump.AddFrustum(new Vector3(0f, -0.5f, 0f), 0.38f, 0.3f, 2.1f, 8);
            wood.AppendTransformed(stump, SceneArtRot.Trs(
                new Vector3(halfLength * 0.45f, 0f, 0f), SceneArtRot.Euler(-12f, 6f, 0f), Vector3.one));

            // ---- 横桁：桅残高约 60% 处，水平，沿船宽方向（≈ 垂直相机视线）----
            float mastHeight = 6.4f;
            var yard = new MeshBuffers();
            yard.AddFrustum(new Vector3(0f, 0f, -1.9f), 0.18f, 0.11f, 3.8f, 6, 0f);
            // 让"横桁"沿 Z 轴摆放：先建成沿 +Y 的锥，再绕 X 旋转 -90°。
            var yardRotated = new MeshBuffers();
            yardRotated.AppendTransformed(yard, SceneArtRot.Trs(Vector3.zero, SceneArtRot.Euler(-90f, 0f, 0f), Vector3.one));
            var yardMatrix = SceneArtRot.Trs(new Vector3(-halfLength * 0.42f, 0f, 0f), SceneArtRot.Euler(-22f, 0f, 0f), Vector3.one)
                * SceneArtRot.Trs(new Vector3(0f, mastHeight * 0.6f, 0f), Quaternion.identity, Vector3.one);
            woodDark.AppendTransformed(yardRotated, yardMatrix);

            // ---- 破帆：2 片挂在横桁上（双面、带撕裂缺口）----
            Vector3 sailTopWorldLocal = new Vector3(-halfLength * 0.42f, mastHeight * 0.6f, 0f);
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -1.0f : 0.9f;
                cloth.AddTrianglesDoubleSided(
                    sailTopWorldLocal + new Vector3(0.05f, 0f, z),
                    sailTopWorldLocal + new Vector3(0.05f, -1.35f, z + 0.55f),
                    sailTopWorldLocal + new Vector3(0.05f, -1.15f, z + 0.05f),
                    sailTopWorldLocal + new Vector3(0.05f, 0.05f, z - 0.5f),
                    Vector3.right);
            }

            // ---- 缆绳：桅顶斜拉到船体两端（2 条）+ 桅顶到副桅（1 条）----
            Vector3 mastTopLocal = new Vector3(-halfLength * 0.42f + 6.4f * Mathf.Sin(22f * Mathf.Deg2Rad), 6.4f * Mathf.Cos(22f * Mathf.Deg2Rad) - 0.5f, 0f);
            cloth.AddRod(mastTopLocal, new Vector3(-halfLength * 0.95f, 0.2f, beam * 0.4f), 0.05f, 4);
            cloth.AddRod(mastTopLocal, new Vector3(halfLength * 0.9f, -0.2f, -beam * 0.35f), 0.05f, 4);
            cloth.AddRod(mastTopLocal, new Vector3(halfLength * 0.45f, 1.4f, 0f), 0.045f, 4);

            // ---- 桅顶铁环 ----
            AddRingLoop(metal, mastTopLocal, Vector3.forward, 0.16f, 0.05f, 6);

            // ---- 跳板：从舷侧斜搭到沙面（2 块）----
            wood.AddBox(new Vector3(-2.2f, -1.1f, beam * 0.62f), new Vector3(3.4f, 0.08f, 0.5f));
            wood.AddBox(new Vector3(2.6f, -1.2f, -beam * 0.6f), new Vector3(2.8f, 0.08f, 0.45f));

            // ---- 整船绕 X 轴侧倾（搁浅姿态）：12-18° ----
            Matrix4x4 roll = SceneArtRot.Trs(Vector3.zero, SceneArtRot.Euler(rollDegrees, 0f, 0f), Vector3.one);
            b.Wood.AppendTransformed(wood, world * roll);
            b.WoodDark.AppendTransformed(woodDark, world * roll);
            b.Cloth.AppendTransformed(cloth, world * roll);
            b.Metal.AppendTransformed(metal, world * roll);
        }

        // ------------------------------------------------------------------
        // 加分项 P2：水下海床坡（沙脊/碎石）
        // ------------------------------------------------------------------

        /// <summary>
        /// 水下沙脊：给定**脊顶世界高度**反推埋入深度，使脊顶恰好停在想要的深度
        /// （场景文档 §5.1「水下加非碰撞海床坡，从竞技场边缘向外缓降」；P2「浅滩能看见水下沙脊轮廓」）。
        /// 无碰撞、不投影；它同时是 PirateWater 读 <c>_CameraDepthTexture</c> 的浅水线索之一。
        /// </summary>
        /// <param name="target">写入的缓冲（通常 <see cref="ScenePropBuffers.SandWet"/>）。</param>
        /// <param name="topY">脊顶世界 Y（建议 -0.4 ~ -0.55，即水面下 0.2-0.35）。</param>
        public static void AddShoal(MeshBuffers target, float x, float z, float topY, float radius, int seed, float squash = 0.4f)
        {
            if (target == null)
                return;

            var mound = new MeshBuffers();
            // AddRock 的局部中心在 radius*squash*0.85、脊顶在 +radius*squash，
            // 故局部顶点最高 = radius*squash*1.85。
            mound.AddRock(new Vector3(0f, radius * squash * 0.85f, 0f), radius,
                new Vector3(1.25f, squash, 1f), seed, 8);

            float baseY = topY - radius * squash * 1.85f;
            target.AppendTransformed(mound, SceneArtRot.Trs(
                new Vector3(x, baseY, z), SceneArtRot.Euler(0f, seed * 53f % 360f, 0f), Vector3.one));
        }

        // ------------------------------------------------------------------
        // 加分项 P1：低模积云（球体扰动 + 平底）
        // ------------------------------------------------------------------

        /// <summary>低模积云团：3-5 个扁圆团块簇成（场景文档 §6.2「每团 20-80 三角面，球体扰动+平底」）。</summary>
        public static void AddCloudPuff(ScenePropBuffers b, Vector3 center, float radius, int seed)
        {
            var cloud = new MeshBuffers();
            int blobs = 3 + (int)(SceneArtHash.Hash01(seed, 0, 7) * 3f);
            for (int i = 0; i < blobs; i++)
            {
                float ang = Mathf.PI * 2f * i / blobs + SceneArtHash.Hash01(seed, i, 11);
                float dist = radius * 0.5f * SceneArtHash.Hash01(seed, i, 13);
                float r = radius * (0.5f + 0.3f * SceneArtHash.Hash01(seed, i, 17));
                cloud.AddRock(
                    new Vector3(Mathf.Cos(ang) * dist, r * 0.35f, Mathf.Sin(ang) * dist),
                    r, new Vector3(1.5f, 0.62f, 1.25f), seed + i * 7, 7);
            }

            b.Cloud.AppendTransformed(cloud, SceneArtRot.Trs(center, Quaternion.identity, Vector3.one));
        }

        // ------------------------------------------------------------------
        // M8 / P5：远景剪影岛与帆船剪影
        // ------------------------------------------------------------------

        /// <summary>
        /// 远景剪影岛：不规则起伏的棱柱剪影（**边缘必须有起伏，不许是三角尖**，场景文档 §6.3）。
        /// 岛体从 <paramref name="baseY"/> 起（<c>baseY</c> 由场景侧按"与水面远缘同屏高"反推，
        /// 见 <c>SceneArtBuilder</c> 的说明），顶脊由 7 个高度不一的峰组成。
        /// </summary>
        public static void AddFarIsland(ScenePropBuffers b, Vector3 basePos, float width, float height, int seed)
        {
            var island = new MeshBuffers();
            const int segments = 7;
            float half = width * 0.5f;

            var top = new Vector3[segments];
            var bottom = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1);
                float x = Mathf.Lerp(-half, half, t);
                // 山峰高度：两端低、中间 2-3 个峰；用确定性哈希做起伏（绝非三角尖）。
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * 0.75f + 0.25f;
                float peak = 0.55f + 0.45f * SceneArtHash.Hash01(seed, i, 23);
                float h = height * envelope * peak;
                top[i] = new Vector3(x, h, SceneArtHash.SignedHash(seed, i, 29) * 0.12f * width);
                bottom[i] = new Vector3(x, 0f, SceneArtHash.SignedHash(seed, i, 31) * 0.18f * width);
            }

            for (int i = 0; i < segments - 1; i++)
            {
                island.AddQuad(bottom[i], bottom[i + 1], top[i + 1], top[i], new Vector3(0f, 0.4f, 1f));
                island.AddQuad(bottom[i + 1], bottom[i], top[i], top[i + 1], new Vector3(0f, 0.4f, -1f));
            }

            // 顶脊封口（从斜上方看不会穿帮）。
            for (int i = 0; i < segments - 1; i++)
            {
                island.AddQuad(top[i], top[i + 1],
                    (top[i] + top[i + 1]) * 0.5f + Vector3.up * 0.4f,
                    (top[i] + top[i + 1]) * 0.5f + Vector3.up * 0.4f, Vector3.up);
            }

            b.Silhouette.AppendTransformed(island, SceneArtRot.Trs(basePos, Quaternion.identity, Vector3.one));
        }

        /// <summary>远景帆船剪影：船身 (4-8 单位) + 2 片白帆（场景文档 §6.3）。船身走剪影档、帆走云白档。</summary>
        public static void AddFarShip(ScenePropBuffers b, Vector3 basePos, float scale, int seed)
        {
            float len = 5.5f * scale;
            float beam = 1.8f * scale;
            float mast = 4.2f * scale;

            var hull = new MeshBuffers();
            hull.AddBox(new Vector3(0f, 0.35f * scale, 0f), new Vector3(len, 0.7f * scale, beam));
            hull.AddBox(new Vector3(len * 0.4f, 0.95f * scale, 0f), new Vector3(len * 0.25f, 0.5f * scale, beam * 0.5f));
            hull.AddFrustum(new Vector3(0f, 0.6f * scale, 0f), 0.11f * scale, 0.07f * scale, mast, 5);

            var world = SceneArtRot.Trs(basePos, SceneArtRot.Euler(0f, seed * 29f % 360f, 0f), Vector3.one);
            b.Silhouette.AppendTransformed(hull, world);

            var sails = new MeshBuffers();
            float sailTop = 0.6f * scale + mast;
            // 主帆（梯形）+ 前帆（三角）。
            sails.AddTrianglesDoubleSided(
                new Vector3(-0.1f * scale, sailTop, 0f),
                new Vector3(-0.1f * scale, sailTop - 2.6f * scale, 1.3f * scale),
                new Vector3(-0.1f * scale, sailTop - 2.6f * scale, 0f),
                new Vector3(-0.1f * scale, sailTop, 0f) + new Vector3(0f, 0f, 1.3f * scale),
                Vector3.right);
            sails.AddTrianglesDoubleSided(
                new Vector3(len * 0.28f, sailTop - 0.6f * scale, 0f),
                new Vector3(len * 0.28f, sailTop - 2.2f * scale, 0f),
                new Vector3(len * 0.28f, sailTop - 2.2f * scale, -0.9f * scale),
                new Vector3(len * 0.28f, sailTop - 0.6f * scale, -0.2f * scale),
                Vector3.right);
            b.Cloud.AppendTransformed(sails, world);
        }

        // ------------------------------------------------------------------
        // §4.1 船体细节（肋骨 / 放样）
        // ------------------------------------------------------------------

        /// <summary>单根船肋骨（外露的弧形框，用 5 段短木拼成弧）。</summary>
        static void AddRibFrame(MeshBuffers b, float x, float z, float y, float thickness)
        {
            const int segments = 5;
            float halfBeam = 1.9f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments;
                float t1 = (i + 1) / (float)segments;
                float a0 = Mathf.Lerp(-1f, 1f, t0);
                float a1 = Mathf.Lerp(-1f, 1f, t1);
                Vector3 p0 = new Vector3(x, y - 0.5f * (1f - a0 * a0) * 1.4f, z + a0 * halfBeam);
                Vector3 p1 = new Vector3(x, y - 0.5f * (1f - a1 * a1) * 1.4f, z + a1 * halfBeam);
                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 dir = p1 - p0;
                float len = dir.magnitude;
                if (len < 1e-4f)
                    continue;

                // 用正交基矩阵代替 Quaternion.LookRotation（后者是 ECall，无头验证台跑不了）。
                b.AddBox(SceneArtRot.BasisFromForward(dir, Vector3.up), mid,
                    new Vector3(thickness, thickness, len), 0f);
            }
        }

        /// <summary>
        /// 船体放样（沿本地 X 轴的长度方向）：横剖面为"舷顶 → 舭 → 龙骨"的 V 形，
        /// 水线以下的剖面单独用暗木再放样一次（形成"水线色带"，场景文档 §4.1）。
        /// </summary>
        static void AddHullLoft(MeshBuffers wood, MeshBuffers woodDark, float xFrom, float xTo,
            float beam, float deepOffset, float depth, int stations, int seed)
        {
            // 舷顶半宽沿长度收窄（首尾收拢）。
            float HalfWidth(float t) => beam * 0.5f * (0.55f + 0.45f * Mathf.Sin(Mathf.Lerp(0.35f, Mathf.PI - 0.35f, t)));

            float[] levels = { 0f, 0.42f, 0.78f, 1f };

            // 上段（舷侧，木档）与下段（水线以下，暗木档）分两次放样。
            AddHullBand(wood, xFrom, xTo, HalfWidth, levels, 0f, 0.62f, depth, stations, 0.0f);
            AddHullBand(woodDark, xFrom, xTo, HalfWidth, levels, 0.58f, 1f, depth, stations, 0.03f);
        }

        static void AddHullBand(MeshBuffers b, float xFrom, float xTo, System.Func<float, float> halfWidth,
            float[] levels, float vFrom, float vTo, float depth, int stations, float widthBias)
        {
            // 在 levels 里插入 vFrom / vTo 两个截断位置，得到本段的剖面层级。
            var vs = new List<float>();
            vs.Add(vFrom);
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] > vFrom + 1e-4f && levels[i] < vTo - 1e-4f)
                    vs.Add(levels[i]);
            }

            vs.Add(vTo);

            stations = Mathf.Max(2, stations);
            int rows = vs.Count;

            // 每个站位生成左右两条剖线点。
            var sectionPoints = new Vector3[stations, (rows * 2)];

            for (int sIdx = 0; sIdx < stations; sIdx++)
            {
                float t = sIdx / (float)(stations - 1);
                float x = Mathf.Lerp(xFrom, xTo, t);
                float hw = halfWidth(t) + widthBias;

                for (int r = 0; r < rows; r++)
                {
                    float v = vs[r];
                    float w = hw * (1f - 0.82f * v * v);
                    float y = -depth * v;
                    sectionPoints[sIdx, r * 2] = new Vector3(x, y, -w);        // 左舷
                    sectionPoints[sIdx, r * 2 + 1] = new Vector3(x, y, w);     // 右舷
                }
            }

            for (int sIdx = 0; sIdx < stations - 1; sIdx++)
            {
                for (int r = 0; r < rows - 1; r++)
                {
                    Vector3 lp0 = sectionPoints[sIdx, r * 2];
                    Vector3 lp1 = sectionPoints[sIdx + 1, r * 2];
                    Vector3 lp2 = sectionPoints[sIdx + 1, r * 2 + 2];
                    Vector3 lp3 = sectionPoints[sIdx, r * 2 + 2];

                    Vector3 rp0 = sectionPoints[sIdx, r * 2 + 1];
                    Vector3 rp1 = sectionPoints[sIdx + 1, r * 2 + 1];
                    Vector3 rp2 = sectionPoints[sIdx + 1, r * 2 + 3];
                    Vector3 rp3 = sectionPoints[sIdx, r * 2 + 3];

                    // 左舷外法线朝 -Z，右舷朝 +Z（提示向量交给 AddQuad 定绕序）。
                    b.AddQuad(lp0, lp1, lp2, lp3, new Vector3(0f, 0.3f, -1f));
                    b.AddQuad(rp0, rp1, rp2, rp3, new Vector3(0f, 0.3f, 1f));
                }

                // 龙骨下沿：左右剖线在最深处相接。
                Vector3 keel0 = sectionPoints[sIdx, rows * 2 - 2];
                Vector3 keel1 = sectionPoints[sIdx + 1, rows * 2 - 2];
                Vector3 keel2 = sectionPoints[sIdx + 1, rows * 2 - 1];
                Vector3 keel3 = sectionPoints[sIdx, rows * 2 - 1];
                b.AddQuad(keel3, keel2, keel1, keel0, Vector3.down);
            }
        }
    }
}
