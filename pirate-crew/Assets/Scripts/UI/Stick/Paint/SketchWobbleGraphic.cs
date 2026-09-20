using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// SketchDraw 的确定性数学层 —— 逐公式移植自 game-2/stick-world
    /// <c>modules/ui_global/scripts/sketch/sketch_draw.gd</c>（行号见各成员注释）。
    ///
    /// 【确定性纪律】随机数不走 System.Random：噪声/扰动全部走 gd 同款确定性哈希公式
    /// （sin(i*127.1+seed*0.3117)*43758.5453 → 取小数），同 seed 同输出。
    /// gd 的 float 是 64 位，wobble 的小数部分对大数取模会把精度差放大，
    /// 因此 <see cref="Wobble"/> 内部保持 double 参与运算；其余位置计算用 float
    /// （误差 1e-4 px 量级，视觉不可分辨）。
    /// </summary>
    public static class SketchDrawMath
    {
        /// <summary>boiling 抖动幅度基准 px（sketch_draw.gd:17 WOBBLE_AMP）。</summary>
        public const float WobbleAmp = 0.95f;

        /// <summary>重掷间隔 s（sketch_draw.gd:19 WOBBLE_INTERVAL，与血条一致）。</summary>
        public const float WobbleInterval = 0.12f;

        /// <summary>描边宽 px（sketch_draw.gd:21 OUTLINE_WIDTH）。</summary>
        public const float OutlineWidth = 1.6f;

        /// <summary>每段长度 px，顶点采样密度（sketch_draw.gd:23 SEG_LEN）。</summary>
        public const float SegLen = 18f;

        /// <summary>UI 面板圆角半径 px（sketch_draw.gd:25 CORNER_R）。</summary>
        public const float CornerR = 7f;

        /// <summary>每个角的弧采样数（sketch_draw.gd:27 ARC_STEPS）。</summary>
        public const int ArcSteps = 4;

        /// <summary>
        /// 确定性伪噪声，返回 -0.5~0.5（sketch_draw.gd:53-55 逐公式移植）。
        /// gd float 为 64 位，这里用 double 复算同一条式子再收窄到 float。
        /// </summary>
        public static float Wobble(int i, int seed)
        {
            double v = System.Math.Sin(i * 127.1 + seed * 0.3117) * 43758.5453;
            return (float)(v - System.Math.Floor(v) - 0.5);
        }

        /// <summary>
        /// 按控件短边自适应的抖动幅度：clamp 到 [WOBBLE_AMP, 1.3]
        /// （sketch_draw.gd:60-62 amp_for 逐式移植）。
        /// </summary>
        public static float AmpFor(Rect r)
        {
            float m = Mathf.Min(r.width, r.height);
            return Mathf.Clamp(WobbleAmp * m / 40f, WobbleAmp, 1.3f);
        }

        /// <summary>
        /// 手绘圆角矩形路径（sketch_draw.gd:87-150 wobbly_rect_path 逐式移植）：
        /// 顺时针，四边按 <see cref="SegLen"/> 分段 + 四角外凸圆弧，全部带扰动；
        /// 顶点噪声索引连续递增，保证任意两点扰动独立。首尾不重复点，闭合由绘制层处理。
        /// </summary>
        /// <param name="r">目标矩形（UGUI 局部坐标）。</param>
        /// <param name="seed">boiling 种子。</param>
        /// <param name="cornerR">圆角半径，自动钳到不塌陷（同 gd）。</param>
        /// <param name="amp">抖幅，&lt;0 时按 <see cref="AmpFor"/> 自适应（同 gd 哨兵值）。</param>
        public static List<Vector2> WobblyRectPath(Rect r, int seed,
            float cornerR = CornerR, float amp = -1f)
        {
            if (amp < 0f)
                amp = AmpFor(r);
            cornerR = Mathf.Min(cornerR, r.width * 0.5f - 2f);
            cornerR = Mathf.Min(cornerR, r.height * 0.5f - 2f);
            if (cornerR < 2f)
                cornerR = 2f;
            int idx = 0;
            var pts = new List<Vector2>(64);
            float left = r.xMin, top = r.yMin, right = r.xMax, bottom = r.yMax;

            // 直边采样（顺时针：顶→右→底→左），法向 = 指向矩形外侧
            // 顶边（左→右）
            int n = Mathf.Max(2, Mathf.RoundToInt((right - left - cornerR * 2f) / SegLen));
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float x = left + cornerR + (right - left - cornerR * 2f) * t;
                float ny = Wobble(idx, seed) * amp;
                idx++;
                pts.Add(new Vector2(x, top + ny));
            }

            // 右上角弧
            AppendArc(pts, new Vector2(right - cornerR, top + cornerR),
                -Mathf.PI * 0.5f, 0f, cornerR, seed, idx, amp);
            idx += ArcSteps;

            // 右边（上→下）
            n = Mathf.Max(2, Mathf.RoundToInt((bottom - top - cornerR * 2f) / SegLen));
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float y = top + cornerR + (bottom - top - cornerR * 2f) * t;
                float nx = Wobble(idx, seed) * amp;
                idx++;
                pts.Add(new Vector2(right + nx, y));
            }

            // 右下角弧
            AppendArc(pts, new Vector2(right - cornerR, bottom - cornerR),
                0f, Mathf.PI * 0.5f, cornerR, seed, idx, amp);
            idx += ArcSteps;

            // 底边（右→左）
            n = Mathf.Max(2, Mathf.RoundToInt((right - left - cornerR * 2f) / SegLen));
            for (int i = 0; i <= n; i++)
            {
                float t = 1f - (float)i / n;
                float x = left + cornerR + (right - left - cornerR * 2f) * t;
                float ny = Wobble(idx, seed) * amp;
                idx++;
                pts.Add(new Vector2(x, bottom + ny));
            }

            // 左下角弧
            AppendArc(pts, new Vector2(left + cornerR, bottom - cornerR),
                Mathf.PI * 0.5f, Mathf.PI, cornerR, seed, idx, amp);
            idx += ArcSteps;

            // 左边（下→上）
            n = Mathf.Max(2, Mathf.RoundToInt((bottom - top - cornerR * 2f) / SegLen));
            for (int i = 0; i <= n; i++)
            {
                float t = 1f - (float)i / n;
                float y = top + cornerR + (bottom - top - cornerR * 2f) * t;
                float nx = Wobble(idx, seed) * amp;
                idx++;
                pts.Add(new Vector2(left + nx, y));
            }

            // 左上角弧（收尾）
            AppendArc(pts, new Vector2(left + cornerR, top + cornerR),
                Mathf.PI, Mathf.PI * 1.5f, cornerR, seed, idx, amp);
            return pts;
        }

        /// <summary>
        /// 角弧采样：半径带小幅双向扰动，扰动下限钳住 0.55r 防自交
        /// （sketch_draw.gd:155-161 _append_arc 逐式移植；gd 侧私有，数学层放开为公共）。
        /// </summary>
        public static void AppendArc(List<Vector2> pts, Vector2 center, float a0,
            float a1, float radius, int seed, int idxBase, float amp)
        {
            for (int i = 0; i < ArcSteps; i++)
            {
                float t = (float)i / ArcSteps;
                float a = a0 + (a1 - a0) * t;
                float rr = radius + Wobble(idxBase + i, seed) * amp * 0.9f;
                float clamped = Mathf.Max(rr, radius * 0.55f);
                pts.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * clamped);
            }
        }

        /// <summary>
        /// 手绘波浪线的路径生成段（sketch_draw.gd:165-180 draw_wavy_line 的顶点部分）：
        /// 沿线按 <see cref="SegLen"/> 分段 + 法向扰动（o = wobble(i*3,seed)*WOBBLE_AMP*1.4）。
        /// gd 侧 len&lt;4 时直接 draw_line——这里退化为两点直线段，粗线绘制交给绘制层的线带。
        /// </summary>
        public static List<Vector2> WavyLinePoints(Vector2 from, Vector2 to, int seed)
        {
            var pts = new List<Vector2>(16);
            float len = Vector2.Distance(from, to);
            if (len < 4f)
            {
                pts.Add(from);
                pts.Add(to);
                return pts;
            }
            int n = Mathf.Max(2, Mathf.RoundToInt(len / SegLen));
            Vector2 dir = (to - from) / len;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                Vector2 p = Vector2.LerpUnclamped(from, to, t);
                float o = Wobble(i * 3, seed) * WobbleAmp * 1.4f;
                pts.Add(p + normal * o);
            }
            return pts;
        }
    }

    /// <summary>绘制模式，一一对应 gd draw_panel（sketch_draw.gd:76-82）的实际绘制 op。</summary>
    public enum SketchDrawMode
    {
        /// <summary>实心填充三角扇 —— draw_colored_polygon（sketch_draw.gd:78）。</summary>
        Fill,

        /// <summary>闭合粗描边线带 —— draw_polyline(loop, ..., antialiased=true)（sketch_draw.gd:80-82）。</summary>
        Outline,

        /// <summary>填充+描边一步到位 —— draw_panel 全序；底/描边需异色时叠两个组件（Fill + Outline）。</summary>
        FillAndOutline,
    }

    /// <summary>
    /// 手绘沸腾自绘 Graphic —— game-2 SketchDraw.draw_panel / SketchPanel 自绘底的 UGUI 等价物。
    ///
    /// 【对应关系】OnPopulateMesh 三种模式对应 gd 侧实际绘制 op（见 <see cref="SketchDrawMode"/>）；
    /// 过小矩形回退普通矩形边框（gd draw_panel:70-75 退化保护同款）。
    /// 【沸腾】Update 里按 Time.unscaledTime 每 0.12s（WOBBLE_INTERVAL）重掷 seed 并
    /// SetVerticesDirty；gd 侧重掷用 randi()（非确定），这里改用同族哈希的确定重掷序列
    /// （禁 System.Random 的工程纪律），<see cref="SeedBase"/> 定序列。
    /// 【AA】Godot canvas 的 polyline AA 在引擎内画；UGUI 网格没有——沿轮廓法向外扩
    /// 约 1px 做顶点色 alpha 线性衰减（羽化带），视觉逼近。详见本目录 README。
    /// </summary>
    public sealed class SketchWobbleGraphic : MaskableGraphic
    {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置，Awake 里设太晚（实测全部自绘件静默空白）
        protected SketchWobbleGraphic() { useLegacyMeshGeneration = false; }
        /// <summary>seed 基值：boiling 重掷序列由它决定（同基值同序列，确定性）。</summary>
        public int SeedBase = 0;

        /// <summary>抖幅覆盖；&lt;0 = 按控件短边自适应（gd wobbly_rect_path 的 amp 哨兵同款）。</summary>
        public float AmpOverride = -1f;

        /// <summary>描边宽 px（gd OUTLINE_WIDTH = 1.6）。</summary>
        public float OutlineWidth = SketchDrawMath.OutlineWidth;

        /// <summary>圆角半径 px（gd CORNER_R = 7，自动钳到不塌陷）。</summary>
        public float CornerRadius = SketchDrawMath.CornerR;

        /// <summary>绘制模式（对应 gd 绘制 op，默认 draw_panel 全序）。</summary>
        public SketchDrawMode Mode = SketchDrawMode.FillAndOutline;

        /// <summary>冻结沸腾：不再重掷 seed，画面静止（验收截图/录屏用）。</summary>
        public bool FreezeBoil = false;

        /// <summary>AA 羽化带宽 px（≈Godot canvas 画线的 1px 衰减边）。</summary>
        internal const float FeatherPx = 1f;

        /// <summary>token 默认色是否已落（区分"用户没设过色"与"用户设了白色"）。</summary>
        [SerializeField] private bool _tokenColorApplied;

        private int _seed;
        private int _tick;
        private float _next;

        /// <summary>颜色默认取 StickTokens：填充=窗底 WINDOW_BG，描边=墨线 INK（键名 = ui_tokens.json 原键）。</summary>
        private Color DefaultTokenColor()
        {
            return Mode == SketchDrawMode.Outline ? StickTokens.INK : StickTokens.WINDOW_BG;
        }

        protected override void Awake()
        {
            base.Awake();
            // 纯绘制层不拦截点击（gd 的 _draw 不参与命中语义）
            raycastTarget = false;
            if (!_tokenColorApplied)
            {
                color = DefaultTokenColor();
                _tokenColorApplied = true;
            }
            _seed = SeedBase;
            _next = Time.unscaledTime + SketchDrawMath.WobbleInterval;
        }

        private void Reset()
        {
            // Editor 挂组件时的默认值入口；_tokenColorApplied 序列化后 Awake 不再覆盖
            color = DefaultTokenColor();
            _tokenColorApplied = true;
        }

        private void Update()
        {
            if (FreezeBoil)
                return;
            // 沸腾节拍：0.12s 重掷（gd WOBBLE_INTERVAL）。gd 侧 _process delta 受 time_scale
            // 影响，这里按 unscaledTime 计——与本项目 SketchBoil.cs 口径一致，UI 沸腾不随
            // 游戏暂停停摆（有意差异，见 README）。
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + SketchDrawMath.WobbleInterval;
            _tick++;
            _seed = NextBoilSeed();
            SetVerticesDirty();
        }

        /// <summary>
        /// 确定重掷序列：gd 用 randi()（非确定），本工程禁 System.Random 且要求同 seed
        /// 同输出——改用 wobble 同族哈希，SeedBase 定整条序列，tick 定相位。
        /// </summary>
        private int NextBoilSeed()
        {
            double v = System.Math.Sin(_tick * 127.1 + SeedBase * 0.3117) * 43758.5453;
            double f = v - System.Math.Floor(v);
            return (int)(f * 2000000000.0) - 1000000000;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f)
                return;

            bool wantFill = Mode != SketchDrawMode.Outline;
            bool wantOutline = Mode != SketchDrawMode.Fill;
            Color c = color;

            // 退化保护（sketch_draw.gd:70-75）：过小矩形回退普通矩形（无扰动、无 AA）
            if (r.width < 8f || r.height < 8f)
            {
                if (wantFill && c.a > 0f)
                    WriteQuad(vh, r, c);
                if (wantOutline && c.a > 0f)
                    WriteLoop(vh, RectCorners(r), c, OutlineWidth, 0f);
                return;
            }

            List<Vector2> pts = SketchDrawMath.WobblyRectPath(r, _seed, CornerRadius, AmpOverride);
            if (wantFill && c.a > 0f)
            {
                // draw_colored_polygon（sketch_draw.gd:78）
                WriteFan(vh, pts, c);
                // 填充外沿 1px 羽化（Godot 填充本无 AA，此处为 UGUI 单独 Fill 模式补的近似，见 README）
                WriteFeatherRing(vh, pts, c, FeatherPx);
            }
            if (wantOutline && c.a > 0f)
            {
                // 闭合 draw_polyline（sketch_draw.gd:80-82，antialiased=true）
                WriteLoop(vh, pts, c, OutlineWidth, FeatherPx);
            }
        }

        // ---- 网格写出原语（与 SketchBarGraphic 各持一份小实现，保持文件自包含） ----

        /// <summary>矩形填充 quad（两个三角形，硬边——对应 Godot draw_rect filled 无 AA）。</summary>
        private static void WriteQuad(VertexHelper vh, Rect r, Color c)
        {
            int b = vh.currentVertCount;
            AddVert(vh, new Vector2(r.xMin, r.yMin), c);
            AddVert(vh, new Vector2(r.xMax, r.yMin), c);
            AddVert(vh, new Vector2(r.xMax, r.yMax), c);
            AddVert(vh, new Vector2(r.xMin, r.yMax), c);
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }

        /// <summary>
        /// 闭合粗折线带（对应 Godot draw_polyline 闭合 + antialiased）：
        /// 核心带用 miter 关节（折角 &gt;~70° 钳成近似斜切防尖刺），feather&gt;0 时内外各挂
        /// 一条 1px 顶点色 alpha 线性衰减带逼近引擎 AA。
        /// </summary>
        private static void WriteLoop(VertexHelper vh, List<Vector2> pts, Color c,
            float width, float feather)
        {
            int n = pts.Count;
            if (n < 2)
                return;
            float half = Mathf.Max(width * 0.5f, 0.05f);
            int b = vh.currentVertCount;

            // 每个关节的 miter 方向与长度
            var mdirs = new Vector2[n];
            var mlens = new float[n];
            for (int j = 0; j < n; j++)
            {
                Vector2 nPrev = SegmentNormal(pts[(j - 1 + n) % n], pts[j]);
                Vector2 nCur = SegmentNormal(pts[j], pts[(j + 1) % n]);
                Vector2 m = nPrev + nCur;
                m = m.sqrMagnitude < 1e-10f ? nCur : m.normalized;
                float dot = Vector2.Dot(m, nCur);
                float len = half / Mathf.Max(dot, 0.35f);
                mdirs[j] = m;
                mlens[j] = Mathf.Min(len, half * 3f);
            }

            // 顶点布局：每关节 [核外, 核内, 外羽化, 内羽化]（无 feather 时只有前两个）
            for (int j = 0; j < n; j++)
            {
                Vector2 p = pts[j];
                Vector2 m = mdirs[j];
                float ml = mlens[j];
                AddVert(vh, p + m * ml, c);
                AddVert(vh, p - m * ml, c);
                if (feather > 0f)
                {
                    AddVertFade(vh, p + m * (ml + feather), c);
                    AddVertFade(vh, p - m * (ml + feather), c);
                }
            }

            int stride = feather > 0f ? 4 : 2;
            for (int j = 0; j < n; j++)
            {
                int a = b + j * stride;
                int d = b + ((j + 1) % n) * stride;
                vh.AddTriangle(a, a + 1, d + 1);
                vh.AddTriangle(a, d + 1, d);
                if (feather > 0f)
                {
                    vh.AddTriangle(a, d, d + 2);
                    vh.AddTriangle(a, d + 2, a + 2);
                    vh.AddTriangle(a + 1, d + 3, d + 1);
                    vh.AddTriangle(a + 1, a + 3, d + 3);
                }
            }
        }

        /// <summary>填充三角扇（近凸路径下与 Godot draw_colored_polygon 的耳切结果等价，见 README）。</summary>
        private static void WriteFan(VertexHelper vh, List<Vector2> pts, Color c)
        {
            int n = pts.Count;
            if (n < 3)
                return;
            int b = vh.currentVertCount;
            for (int i = 0; i < n; i++)
                AddVert(vh, pts[i], c);
            for (int i = 1; i < n - 1; i++)
                vh.AddTriangle(b, b + i, b + i + 1);
        }

        /// <summary>沿轮廓的 1px 羽化环：内沿全 alpha、外沿 alpha=0，线性衰减（填充模式的 AA 近似）。</summary>
        private static void WriteFeatherRing(VertexHelper vh, List<Vector2> pts, Color c,
            float feather)
        {
            int n = pts.Count;
            if (n < 3)
                return;
            int b = vh.currentVertCount;
            for (int j = 0; j < n; j++)
            {
                Vector2 m = JointNormal(pts, j);
                AddVert(vh, pts[j], c);
                AddVertFade(vh, pts[j] + m * feather, c);
            }
            for (int j = 0; j < n; j++)
            {
                int a = b + j * 2;
                int d = b + ((j + 1) % n) * 2;
                vh.AddTriangle(a, d, d + 1);
                vh.AddTriangle(a, d + 1, a + 1);
            }
        }

        /// <summary>四角点列表（顺序同 Godot draw_rect unfilled 的 polyline：TL→TR→BR→BL）。</summary>
        private static List<Vector2> RectCorners(Rect r)
        {
            return new List<Vector2>(4)
            {
                new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax),
                new Vector2(r.xMin, r.yMax),
            };
        }

        /// <summary>线段单位法向（退化线段返回零向量）。</summary>
        private static Vector2 SegmentNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude < 1e-10f ? Vector2.zero : new Vector2(-d.y, d.x).normalized;
        }

        /// <summary>关节法向：相邻两段单位法向的归一化和（关节数 ≥3 的闭合环内用）。</summary>
        private static Vector2 JointNormal(List<Vector2> pts, int j)
        {
            int n = pts.Count;
            Vector2 nPrev = SegmentNormal(pts[(j - 1 + n) % n], pts[j]);
            Vector2 nCur = SegmentNormal(pts[j], pts[(j + 1) % n]);
            Vector2 m = nPrev + nCur;
            return m.sqrMagnitude < 1e-10f ? nCur : m.normalized;
        }

        private static void AddVert(VertexHelper vh, Vector2 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }

        /// <summary>同色 alpha=0 的羽化端点。</summary>
        private static void AddVertFade(VertexHelper vh, Vector2 p, Color c)
        {
            Color f = c;
            f.a = 0f;
            AddVert(vh, p, f);
        }
    }
}
