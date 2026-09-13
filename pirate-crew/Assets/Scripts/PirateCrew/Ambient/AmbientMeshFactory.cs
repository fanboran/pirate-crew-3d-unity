using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>
    /// 环境活物/动效道具的**程序化网格工厂**（纯 C#，无头可测）。
    ///
    /// 【为什么复用 <c>SceneArt.MeshBuffers</c>】它是本工程既有的纯 C# 三角面缓冲
    /// （平面着色、每面独立顶点），已在 <c>Assets/Tests/SceneArt/</c> 下有无头用例；
    /// 复用它而不是复制一份 400 行几何工具，代价是 Ambient → SceneArt 的一个只读依赖
    /// （**不改 SceneArt 任何文件**）。若将来 SceneArt 的该 API 变动，本文件是唯一受影响处。
    ///
    /// 【尺寸口径】全部世界单位。尺度换算：海盗单位总高 0.5 单位 ≈ 1.7 m
    /// （<c>docs/美术风格指南.md</c> §5.2 表），故 1 单位 ≈ 3.4 m。
    /// 真实海鸥翼展 ~1.2 m = 0.35 单位、螃蟹甲宽 ~0.1 m = 0.03 单位——
    /// 按真实比例会小到看不见，故**刻意放大到 1.5-2 倍**（美术风格指南 §1.2 "部件放大保可读"同精神）：
    /// 海鸥翼展约 0.6 单位、螃蟹甲宽约 0.30 单位、鱼长约 0.12 单位。**全部为【提案】**。
    ///
    /// 【朝向约定】海鸥/鱼/螃蟹的水平朝向 = 局部 **+Z**（因此运行时用
    /// <c>Quaternion.LookRotation(heading, up)</c> 即可对齐）；翅膀向 ±X 展开、绕 Z 拍动。
    /// </summary>
    public static class AmbientMeshFactory
    {
        // ------------------------------------------------------------------
        // 尺寸常量（【提案】，见类头注释）
        // ------------------------------------------------------------------

        /// <summary>海鸥躯干长度（世界单位）【提案】。</summary>
        public const float GullBodyLength = 0.22f;

        /// <summary>海鸥单翼展长（从肩到翼尖）【提案】：两翼合计翼展约 0.6 单位。</summary>
        public const float GullWingSpan = 0.27f;

        /// <summary>螃蟹甲壳宽度【提案】。</summary>
        public const float CrabShellWidth = 0.30f;

        /// <summary>螃蟹单侧步足展长【提案】。</summary>
        public const float CrabLegSpan = 0.21f;

        /// <summary>鱼体长度【提案】。</summary>
        public const float FishLength = 0.12f;

        /// <summary>灯笼总高【提案】。</summary>
        public const float LanternHeight = 0.26f;

        /// <summary>挂灯/旗绳立柱高度【提案】。</summary>
        public const float PostHeight = 1.8f;

        // ------------------------------------------------------------------
        // 海鸥
        // ------------------------------------------------------------------

        /// <summary>
        /// 海鸥躯干（含头/喙/尾），**不含翅膀**。翅膀是独立网格以便绕肩拍动。
        /// 三角面约 60（躯干 6 段侧面 12 + 端盖 12 + 头 12 + 喙 12 + 尾 2 双面 4 ≈ 52）。
        /// </summary>
        public static MeshBuffers BuildGullBody()
        {
            var body = new MeshBuffers();

            // 躯干：沿 +Y 建再转到 +Z（Buffer 的 AddFrustum 只沿 +Y）。
            var torso = new MeshBuffers();
            torso.AddFrustum(Vector3.zero, 0.052f, 0.036f, GullBodyLength * 0.72f, 6, 0f, true, false);
            AppendRotated(body, torso, new Vector3(90f, 0f, 0f),
                new Vector3(0f, 0f, -GullBodyLength * 0.5f), 1f);

            // 头：小球（用短圆台近似，端盖封顶）。
            var head = new MeshBuffers();
            head.AddFrustum(Vector3.zero, 0.032f, 0.020f, 0.062f, 6);
            AppendRotated(body, head, new Vector3(90f, 0f, 0f), new Vector3(0f, 0.012f, GullBodyLength * 0.22f), 1f);

            // 喙：细锥（朝 +Z）。
            var beak = new MeshBuffers();
            beak.AddFrustum(Vector3.zero, 0.012f, 0.0018f, 0.055f, 4, 0f, false, false);
            AppendRotated(body, beak, new Vector3(90f, 0f, 0f), new Vector3(0f, 0.012f, GullBodyLength * 0.46f), 1f);

            // 尾：后掠小三角（双面）。
            var tail = new MeshBuffers();
            tail.AddTrianglesDoubleSided(
                new Vector3(-0.030f, 0f, 0f),
                new Vector3(0.030f, 0f, 0f),
                new Vector3(0.016f, 0f, -0.085f),
                new Vector3(-0.016f, 0f, -0.085f),
                Vector3.up);
            AppendRotated(body, tail, Vector3.zero, new Vector3(0f, 0.004f, -GullBodyLength * 0.5f), 1f);

            // 脊柱鳍（细长条，双面）—— 45° 俯视下给鸟一个中轴读点。
            var spine = new MeshBuffers();
            spine.AddTrianglesDoubleSided(
                new Vector3(0f, 0f, 0.02f),
                new Vector3(0f, 0.022f, 0.02f),
                new Vector3(0f, 0.014f, -0.09f),
                new Vector3(0f, 0f, -0.09f),
                Vector3.right);
            AppendRotated(body, spine, Vector3.zero, new Vector3(0f, 0.030f, -GullBodyLength * 0.2f), 1f);

            return body;
        }

        /// <summary>
        /// 海鸥单翼（左翼，向 +X 展开，翼根在原点）。4 个三角面（双面）。
        /// 右翼用 <c>localScale.x = -1</c> 镜像，不需要第二份网格。
        /// </summary>
        public static MeshBuffers BuildGullWing()
        {
            var wing = new MeshBuffers();
            Vector3 rootFront = new Vector3(0.015f, 0f, 0.055f);
            Vector3 rootBack = new Vector3(0.015f, 0f, -0.062f);
            Vector3 tipFront = new Vector3(GullWingSpan, 0.010f, 0.010f);
            Vector3 tipBack = new Vector3(GullWingSpan, 0.010f, -0.030f);
            wing.AddTrianglesDoubleSided(rootFront, tipFront, tipBack, rootBack, Vector3.up);

            // 翼尖折角（读作"主飞羽"的一小片）。
            Vector3 f2 = new Vector3(tipFront.x + GullWingSpan * 0.20f, tipFront.y + 0.006f, tipFront.z - 0.004f);
            Vector3 b2 = new Vector3(tipBack.x + GullWingSpan * 0.16f, tipBack.y + 0.006f, tipBack.z - 0.012f);
            wing.AddTrianglesDoubleSided(tipFront, f2, b2, tipBack, Vector3.up);

            return wing;
        }

        // ------------------------------------------------------------------
        // 螃蟹
        // ------------------------------------------------------------------

        /// <summary>
        /// 螃蟹躯干（甲壳 + 8 条步足 + 双眼柄），**不含钳子**（钳子独立网格，绕根部摆动）。
        /// 三角面约 90。
        /// </summary>
        public static MeshBuffers BuildCrabBody()
        {
            var crab = new MeshBuffers();

            // 甲壳：扁球——用 6 段圆台 + 端盖，压扁 X/Z 比例后读作甲。
            var shell = new MeshBuffers();
            shell.AddFrustum(Vector3.zero, 0.075f, 0.062f, 0.052f, 7);
            // 压扁：整体放大 X 2.0、Z 1.55 → 甲宽 0.30、甲长 0.23。
            AppendRotated(crab, shell, Vector3.zero, new Vector3(0f, 0.028f, 0f), 1f, new Vector3(2.0f, 1f, 1.55f));

            // 8 条步足：每侧 4 条细杆，向前/后张开。
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                for (int i = 0; i < 4; i++)
                {
                    float t = i / 3f;                       // 0 = 前足、1 = 后足
                    float along = Mathf.Lerp(0.055f, -0.055f, t);
                    float spread = Mathf.Lerp(CrabLegSpan * 0.75f, CrabLegSpan, Mathf.Abs(t - 0.5f) * 2f);
                    Vector3 knee = new Vector3(sign * spread * 0.55f, 0.022f, along);
                    Vector3 tip = new Vector3(sign * spread, 0f, along * 1.5f);
                    var leg = new MeshBuffers();
                    leg.AddRod(Vector3.zero, knee, 0.006f, 4);
                    leg.AddRod(knee, tip, 0.004f, 4);
                    AppendRotated(crab, leg, Vector3.zero, Vector3.zero, 1f);
                }
            }

            // 双眼柄：两根细杆 + 小圆头。
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                var eye = new MeshBuffers();
                Vector3 stem = new Vector3(sign * 0.028f, 0.052f, 0.052f);
                eye.AddRod(Vector3.zero, stem, 0.005f, 4);
                var head = new MeshBuffers();
                head.AddFrustum(Vector3.zero, 0.012f, 0.010f, 0.018f, 5);
                AppendRotated(eye, head, Vector3.zero, stem, 1f);
                AppendRotated(crab, eye, Vector3.zero, Vector3.zero, 1f);
            }

            return crab;
        }

        /// <summary>
        /// 螃蟹单只钳子（向 +X 伸出，根部在原点）。三角面约 26。
        /// 右钳用 <c>localScale.x = -1</c> 镜像。
        /// </summary>
        public static MeshBuffers BuildCrabClaw()
        {
            var claw = new MeshBuffers();

            // 前臂：细杆。
            Vector3 elbow = new Vector3(0.075f, 0.004f, 0.012f);
            claw.AddRod(Vector3.zero, elbow, 0.008f, 5);

            // 钳掌：小盒。
            var palm = new MeshBuffers();
            palm.AddBox(Vector3.zero, new Vector3(0.070f, 0.036f, 0.048f));
            AppendRotated(claw, palm, Vector3.zero, elbow + new Vector3(0.040f, 0f, 0.004f), 1f);

            // 上下钳齿：两片薄楔（盒压扁），张一个开口角。
            var upper = new MeshBuffers();
            upper.AddBox(Vector3.zero, new Vector3(0.052f, 0.014f, 0.026f));
            AppendRotated(claw, upper, new Vector3(0f, 0f, -16f),
                elbow + new Vector3(0.100f, 0.014f, 0.002f), 1f);

            var lower = new MeshBuffers();
            lower.AddBox(Vector3.zero, new Vector3(0.052f, 0.014f, 0.026f));
            AppendRotated(claw, lower, new Vector3(0f, 0f, 16f),
                elbow + new Vector3(0.100f, -0.014f, 0.002f), 1f);

            return claw;
        }

        // ------------------------------------------------------------------
        // 鱼
        // ------------------------------------------------------------------

        /// <summary>低模鱼（躯干 + 尾鳍 + 背鳍），朝向 +Z。三角面约 26。</summary>
        public static MeshBuffers BuildFish()
        {
            var fish = new MeshBuffers();

            var torso = new MeshBuffers();
            torso.AddFrustum(Vector3.zero, 0.024f, 0.008f, FishLength * 0.62f, 5, 0f, true, true);
            AppendRotated(fish, torso, new Vector3(90f, 0f, 0f),
                new Vector3(0f, 0f, -FishLength * 0.45f), 1f);

            // 尾鳍：竖立的双面三角。
            var tail = new MeshBuffers();
            tail.AddTrianglesDoubleSided(
                new Vector3(0f, 0.030f, 0f),
                new Vector3(0f, 0.030f, -0.034f),
                new Vector3(0f, -0.030f, -0.034f),
                new Vector3(0f, -0.030f, 0f),
                Vector3.forward);
            AppendRotated(fish, tail, Vector3.zero, new Vector3(0f, 0f, -FishLength * 0.5f), 1f);

            // 背鳍。
            var dorsal = new MeshBuffers();
            dorsal.AddTrianglesDoubleSided(
                new Vector3(0f, 0f, 0.030f),
                new Vector3(0f, 0.020f, 0.004f),
                new Vector3(0f, 0.020f, -0.020f),
                new Vector3(0f, 0f, -0.028f),
                Vector3.right);
            AppendRotated(fish, dorsal, Vector3.zero, new Vector3(0f, 0.020f, 0.004f), 1f);

            return fish;
        }

        // ------------------------------------------------------------------
        // 环境动效道具
        // ------------------------------------------------------------------

        /// <summary>灯笼金属件（框架 + 顶盖 + 提梁弧），三角面约 70。灯泡/辉光是独立 quad。</summary>
        public static MeshBuffers BuildLanternFrame()
        {
            var lantern = new MeshBuffers();

            // 框架：六棱柱（上下略收）。
            var frame = new MeshBuffers();
            frame.AddFrustum(Vector3.zero, 0.058f, 0.046f, LanternHeight * 0.62f, 6, 0f, false, false);
            AppendRotated(lantern, frame, Vector3.zero, Vector3.zero, 1f);

            // 顶盖：小圆台 + 顶盖。
            var roof = new MeshBuffers();
            roof.AddFrustum(Vector3.zero, 0.070f, 0.020f, 0.045f, 6);
            AppendRotated(lantern, roof, Vector3.zero, new Vector3(0f, LanternHeight * 0.62f, 0f), 1f);

            // 底座。
            var bottom = new MeshBuffers();
            bottom.AddFrustum(Vector3.zero, 0.050f, 0.060f, 0.020f, 6);
            AppendRotated(lantern, bottom, Vector3.zero, Vector3.zero, 1f);

            // 提梁：3 段折线构成的拱。
            var bail = new MeshBuffers();
            Vector3 a = new Vector3(-0.052f, LanternHeight * 0.82f, 0f);
            Vector3 b = new Vector3(-0.026f, LanternHeight * 1.02f, 0f);
            Vector3 c = new Vector3(0.026f, LanternHeight * 1.02f, 0f);
            Vector3 d = new Vector3(0.052f, LanternHeight * 0.82f, 0f);
            bail.AddRod(a, b, 0.006f, 4);
            bail.AddRod(b, c, 0.006f, 4);
            bail.AddRod(c, d, 0.006f, 4);
            AppendRotated(lantern, bail, Vector3.zero, Vector3.zero, 1f);

            return lantern;
        }

        /// <summary>
        /// 灯笼里的"灯芯"发光体（暖色自发光小立方），三角面 12。几何**以原点为中心**，
        /// 由运行时把它挂在灯笼框**下方**（见 <c>AmbientDirector.SpawnLanterns</c>）——
        /// 放在金属框内部会被框的近侧面深度遮挡（相机 45° 俯视 + 框有顶盖），看不见等于白画。
        /// </summary>
        public static MeshBuffers BuildLanternCore()
        {
            var core = new MeshBuffers();
            core.AddBox(Vector3.zero, new Vector3(0.062f, 0.075f, 0.062f));
            return core;
        }

        /// <summary>
        /// 辉光贴片（XY 平面朝向 +Z 的四边形，双面）。运行时用 billboard 材质朝向相机。
        /// 三角面 4（双面）。
        /// </summary>
        public static MeshBuffers BuildGlowQuad(float size)
        {
            var quad = new MeshBuffers();
            float h = size * 0.5f;
            quad.AddTrianglesDoubleSided(
                new Vector3(-h, -h, 0f),
                new Vector3(h, -h, 0f),
                new Vector3(h, h, 0f),
                new Vector3(-h, h, 0f),
                Vector3.forward);
            return quad;
        }

        /// <summary>
        /// 三角小旗/燕尾旗（挂在绳上的垂片），**顶端为挂点（y=0）**、向下展开 0.16 单位。
        /// 三角面 4（双面）。用顶点风 shader（_WindWeightDirection = -1，向下权重递增）。
        /// </summary>
        public static MeshBuffers BuildPennant()
        {
            var flag = new MeshBuffers();
            float w = 0.115f;   // 半宽
            float h = 0.170f;   // 垂长
            // 上边贴绳（挂点），下边收成燕尾。
            flag.AddTrianglesDoubleSided(
                new Vector3(-w, 0f, 0f),
                new Vector3(w, 0f, 0f),
                new Vector3(w * 0.45f, -h, 0f),
                new Vector3(-w * 0.45f, -h, 0f),
                Vector3.forward);
            return flag;
        }

        /// <summary>立柱（灯笼杆/旗绳杆），三角面约 14。</summary>
        public static MeshBuffers BuildPost(float height, float radius, int segments = 6)
        {
            var post = new MeshBuffers();
            post.AddFrustum(Vector3.zero, radius * 1.15f, radius * 0.85f, height, segments, 0f, true, true);
            // 顶帽。
            post.AddFrustum(new Vector3(0f, height, 0f), radius * 1.35f, radius * 0.6f, 0.06f, segments, 0f, true, true);
            return post;
        }

        /// <summary>缆绳（两点之间的细杆），三角面约 10。</summary>
        public static MeshBuffers BuildRope(Vector3 from, Vector3 to, float radius = 0.012f, int segments = 4)
        {
            var rope = new MeshBuffers();
            rope.AddRod(from, to, radius, segments);
            return rope;
        }

        /// <summary>浮标（软木浮子 + 小杆），三角面约 20。用于近岸随波起伏的小道具。</summary>
        public static MeshBuffers BuildCorkFloat()
        {
            var cork = new MeshBuffers();
            cork.AddFrustum(Vector3.zero, 0.055f, 0.042f, 0.105f, 6, 0f, true, true);
            cork.AddFrustum(new Vector3(0f, 0.105f, 0f), 0.010f, 0.008f, 0.075f, 4, 0f, true, true);
            return cork;
        }

        /// <summary>
        /// 远景海鸟剪影（极简 V 形，2 个三角面，双面）。纯装饰、无光照材质，
        /// 成本可忽略（<c>docs/场景设计-战斗竞技场.md</c> §6.3 同思路的"远景活着"）。
        /// </summary>
        public static MeshBuffers BuildDistantBird()
        {
            var bird = new MeshBuffers();
            float span = 0.42f;
            float chord = 0.075f;
            // 两片后掠翼拼成 V。
            bird.AddTrianglesDoubleSided(
                new Vector3(0f, 0f, chord * 0.5f),
                new Vector3(span, 0.05f, -chord * 0.5f),
                new Vector3(span * 0.92f, 0.05f, -chord * 1.2f),
                new Vector3(0f, 0f, -chord * 0.5f),
                Vector3.up);
            bird.AddTrianglesDoubleSided(
                new Vector3(0f, 0f, chord * 0.5f),
                new Vector3(-span, 0.05f, -chord * 0.5f),
                new Vector3(-span * 0.92f, 0.05f, -chord * 1.2f),
                new Vector3(0f, 0f, -chord * 0.5f),
                Vector3.up);
            return bird;
        }

        // ------------------------------------------------------------------
        // 内部：局部 → 目标缓冲的带缩放旋转追加
        // ------------------------------------------------------------------

        /// <summary>
        /// 把 <paramref name="part"/> 经「缩放 → 欧拉旋转 → 平移」追加到 <paramref name="target"/>。
        /// 用自实现的 <see cref="ComposeTrs"/>（纯托管）而不是 <c>Matrix4x4.TRS(…, Quaternion.Euler(…), …)</c>：
        /// <c>Quaternion.Euler</c> 内部调 <c>Internal_FromEulerRad</c> 是原生 ECall，
        /// 脱离 Unity 运行时会抛 <c>SecurityException</c>，整个网格工厂就无法在无头验证台上断言
        /// （实测踩到：<c>ECS.Quaternion.Internal_FromEulerRad</c>）。自实现后网格生成全程纯 C#。
        /// </summary>
        static void AppendRotated(MeshBuffers target, MeshBuffers part, Vector3 euler,
            Vector3 translate, float uniformScale, Vector3? nonUniformScale = null)
        {
            if (target == null || part == null || part.IsEmpty)
                return;

            Vector3 scale = nonUniformScale.HasValue
                ? nonUniformScale.Value * uniformScale
                : Vector3.one * uniformScale;

            Matrix4x4 m = ComposeTrs(translate, euler, scale);
            target.AppendTransformed(part, m);
        }

        /// <summary>
        /// 纯托管 TRS 矩阵（等价 Unity 的 <c>Matrix4x4.TRS</c>，Euler 顺序与 <c>Quaternion.Euler</c> 一致：
        /// R = Ry · Rx · Rz）。**公开**是为了让无头测试能构造镜像/旋转矩阵而不碰 ECall。
        /// </summary>
        public static Matrix4x4 ComposeTrs(Vector3 translate, Vector3 eulerDegrees, Vector3 scale)
        {
            float rx = eulerDegrees.x * Mathf.Deg2Rad;
            float ry = eulerDegrees.y * Mathf.Deg2Rad;
            float rz = eulerDegrees.z * Mathf.Deg2Rad;

            float cx = Mathf.Cos(rx), sx = Mathf.Sin(rx);
            float cy = Mathf.Cos(ry), sy = Mathf.Sin(ry);
            float cz = Mathf.Cos(rz), sz = Mathf.Sin(rz);

            // R = Ry · Rx · Rz（各行推导见提交说明；与 Unity Euler 约定一致）。
            float r00 = cy * cz + sy * sx * sz;
            float r01 = -cy * sz + sy * sx * cz;
            float r02 = sy * cx;

            float r10 = cx * sz;
            float r11 = cx * cz;
            float r12 = -sx;

            float r20 = -sy * cz + cy * sx * sz;
            float r21 = sy * sz + cy * sx * cz;
            float r22 = cy * cx;

            Matrix4x4 m = default;
            m.m00 = r00 * scale.x; m.m01 = r01 * scale.y; m.m02 = r02 * scale.z; m.m03 = translate.x;
            m.m10 = r10 * scale.x; m.m11 = r11 * scale.y; m.m12 = r12 * scale.z; m.m13 = translate.y;
            m.m20 = r20 * scale.x; m.m21 = r21 * scale.y; m.m22 = r22 * scale.z; m.m23 = translate.z;
            m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
            return m;
        }
    }
}
