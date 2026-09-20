using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦分隔线 —— game-2 SketchSeparator（sketch_separator.gd）的 UGUI 复刻。
    ///
    /// 【口径选择】任务给的两条路线里取**源码口径**（gd SketchSeparator 是 wavy 自绘，
    /// 非贴图件）：BORDER @ 0.35 波浪线、线宽 1.3、两端各让 2px，逐公式对照。
    /// sep_h/sep_v 贴图槽在 gd 侧是原生 HSeparator/VSeparator 兜底的 stylebox
    /// （SketchStyle.separator()），不是本控件源码的渲染路径，故不取。
    ///
    /// 【沸腾】gd _process 每 WOBBLE_INTERVAL(0.12s) 重掷 seed → 确定性重掷序列
    /// （同 SketchWobbleGraphic 的哈希方案，禁 System.Random 工程纪律）。
    /// edit 模式 Update 不跑，样张取 SeedBase 初相静态帧。
    /// </summary>
    public sealed class SketchSeparator : MonoBehaviour
    {
        /// <summary>方向（gd Dir.HORIZONTAL/VERTICAL 同名）。</summary>
        public enum Direction
        {
            Horizontal,
            Vertical,
        }

        /// <summary>方向（运行时可切）。</summary>
        public Direction Dir = Direction.Horizontal;

        /// <summary>seed 基值（沸腾重掷序列由它决定，确定性）。</summary>
        public int SeedBase;

        /// <summary>建一条分隔线（纯展示件，不拦截点击）。direction 缺省水平。</summary>
        public static SketchSeparator Create(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            Direction direction = Direction.Horizontal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var separator = go.AddComponent<SketchSeparator>();
            separator.Dir = direction;
            var g = go.AddComponent<WavyLineGraphic>();
            g.Owner = separator;
            return separator;
        }

        /// <summary>自绘波浪线层（draw_wavy_line 的 UGUI 等价物，本文件自包含网格原语）。</summary>
        internal sealed class WavyLineGraphic : MaskableGraphic
        {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置（Awake 里设太晚，自绘件静默空白）
        protected WavyLineGraphic() { useLegacyMeshGeneration = false; }
            internal SketchSeparator Owner;

            private bool _init;
            private int _seed;
            private int _tick;
            private float _next;

            protected override void Awake()
            {
                base.Awake();
                raycastTarget = false;  // 纯展示件（gd _draw 不参与命中语义）
            }

            private void Update()
            {
                if (Time.unscaledTime < _next)
                    return;
                _next = Time.unscaledTime + SketchDrawMath.WobbleInterval;
                _tick++;
                _seed = NextBoilSeed();
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                LazyInit();
                Rect r = GetPixelAdjustedRect();
                if (r.width <= 0f || r.height <= 0f)
                    return;

                bool vertical = Owner != null && Owner.Dir == Direction.Vertical;
                // 两端各让 2px（gd _draw 同值），线居中
                List<Vector2> pts = vertical
                    ? SketchDrawMath.WavyLinePoints(
                        new Vector2(r.width * 0.5f, 2f),
                        new Vector2(r.width * 0.5f, r.height - 2f), _seed)
                    : SketchDrawMath.WavyLinePoints(
                        new Vector2(2f, r.height * 0.5f),
                        new Vector2(r.width - 2f, r.height * 0.5f), _seed);

                // gd：Color(BORDER.r/g/b, 0.35)，draw_polyline 宽 1.3 antialiased
                Color line = StickTokens.BORDER;
                line.a = 0.35f;
                WriteStrip(vh, pts, line, 1.3f, FeatherPx);
            }

            private void LazyInit()
            {
                if (_init)
                    return;
                _init = true;
                _seed = Owner != null ? Owner.SeedBase : 0;
                _next = Time.unscaledTime + SketchDrawMath.WobbleInterval;
            }

            /// <summary>确定性重掷序列（同 SketchWobbleGraphic.NextBoilSeed）。</summary>
            private int NextBoilSeed()
            {
                // SeedBase 在 Owner 上（同 LazyInit 取式），嵌套类内不直接可见。
                double v = System.Math.Sin(_tick * 127.1 + (Owner != null ? Owner.SeedBase : 0) * 0.3117) * 43758.5453;
                double f = v - System.Math.Floor(v);
                return (int)(f * 2000000000.0) - 1000000000;
            }
        }

        // ---- 网格写出原语（与 SketchWobbleGraphic 各持一份小实现，保持文件自包含） ----

        private const float FeatherPx = 1f;

        private static void AddVert(VertexHelper vh, Vector2 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }

        private static void AddVertFade(VertexHelper vh, Vector2 p, Color c)
        {
            Color f = c;
            f.a = 0f;
            AddVert(vh, p, f);
        }

        /// <summary>
        /// 开口粗折线带（gd draw_polyline 非闭合版：首尾平头无端帽，段间 miter，
        /// feather&gt;0 时内外各挂 1px alpha 衰减带逼近引擎 AA）。
        /// </summary>
        private static void WriteStrip(VertexHelper vh, List<Vector2> pts, Color c,
            float width, float feather)
        {
            int n = pts.Count;
            if (n < 2)
                return;
            float half = Mathf.Max(width * 0.5f, 0.05f);
            int b = vh.currentVertCount;
            for (int j = 0; j < n; j++)
            {
                Vector2 m = StripJointNormal(pts, j);
                AddVert(vh, pts[j] + m * half, c);
                AddVert(vh, pts[j] - m * half, c);
                if (feather > 0f)
                {
                    AddVertFade(vh, pts[j] + m * (half + feather), c);
                    AddVertFade(vh, pts[j] - m * (half + feather), c);
                }
            }
            int stride = feather > 0f ? 4 : 2;
            for (int j = 0; j < n - 1; j++)
            {
                int a = b + j * stride;
                int d = a + stride;
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

        /// <summary>开口折线的关节法向（端点退用相邻段法向）。</summary>
        private static Vector2 StripJointNormal(List<Vector2> pts, int j)
        {
            Vector2 nPrev = j > 0 ? SegmentNormal(pts[j - 1], pts[j]) : SegmentNormal(pts[j], pts[j + 1]);
            Vector2 nCur = j < pts.Count - 1 ? SegmentNormal(pts[j], pts[j + 1]) : nPrev;
            Vector2 m = nPrev + nCur;
            return m.sqrMagnitude < 1e-10f ? nCur : m.normalized;
        }

        /// <summary>线段单位法向（退化线段返回零向量）。</summary>
        private static Vector2 SegmentNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude < 1e-10f ? Vector2.zero : new Vector2(-d.y, d.x).normalized;
        }
    }
}
