using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦滑条 —— game-2 SketchHSlider（sketch_hslider.gd）的 UGUI 复刻。
    /// 不用 Unity Slider 皮肤（gd 也不用原生 slider stylebox）：轨道/填充/wobble 圆点
    /// 滑块全部一层自绘 Graphic 承担，逐公式对照 gd _draw。
    ///
    /// 【值域】value 0..1（gd min/max range 的归一化形态，任务口径）。
    /// 【几何公式】（gd 逐行，y 轴翻转仅影响上下标注、布局对称故逐像素等价）：
    ///  · grab_r = clamp(h * 0.42, 5, 9)（grabber_radius）；
    ///  · 轨道：x 让出滑块半径，高 6px 垂直居中；
    ///  · 凹槽底 = GROOVE_BG 硬边矩形；上下缘 = BORDER@0.28 波浪墨线（宽 1.3，
    ///    下缘 seed+13 错相位）；
    ///  · 填充 = 中心粗马克笔笔画（宽 5 = track_h-1，ACCENT，seed+77）+ 两端 ACCENT
    ///    圆头端帽（cap_r = (6-1)/2 = 2.5），ratio &gt; 0.01 才画；
    ///  · 滑块 = 14 段 wobble 圆（wobble(i*3, seed+31) * 0.95），白实心 TEXT + 深墨
    ///    描边 INK@0.95（宽 1.6）；gd has_focus（拖动/键盘）转琥珀 ACCENT →
    ///    本侧映射为拖动态 <see cref="IsDragging"/>。
    /// 【命中语义】gd 整控件可拖拽（HSlider 全 rect 命中）→ 根上透明 hit Image 承担
    /// （UGUI 命中须有 raycastTarget 的 Graphic），自绘层不拦截。
    /// </summary>
    public sealed class SketchSlider : MonoBehaviour, IPointerDownHandler, IDragHandler,
    IPointerUpHandler
    {
        [SerializeField] private float _value;

        internal SketchSliderGraphic Graphic;

        /// <summary>刻度数（gd 原生 tick_count 机制：≥2 出均匀短竖线，0 = 无）。</summary>
        public int Ticks;

        /// <summary>拖动态（gd has_focus 的本侧等价：滑块转琥珀）。</summary>
        public bool IsDragging { get; private set; }

        /// <summary>值变化回调（gd value_changed 信号）。</summary>
        public event Action<float> ValueChanged;

        /// <summary>当前值 0..1（写入 clamp 并请求重绘，gd value_changed → queue_redraw 同义）。</summary>
        public float Value
        {
            get => _value;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, _value))
                return;
                _value = v;
                if (Graphic != null)
                Graphic.SetVerticesDirty();
                ValueChanged?.Invoke(v);
            }
        }

        /// <summary>滑块圆点半径（gd grabber_radius：clamp(h*0.42, 5, 9)）。</summary>
        public static float GrabberRadius(float height)
        {
            return Mathf.Clamp(height * 0.42f, 5f, 9f);
        }

        /// <summary>建一条滑条（自绘层 + 交互层一体）。value 缺省 0。</summary>
        public static SketchSlider Create(Transform parent, string name, Vector2 anchor,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 size, float value = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var slider = go.AddComponent<SketchSlider>();
            slider._value = Mathf.Clamp01(value);

            // 透明命中层（整控件可点/可拖，gd HSlider 全 rect 命中同语义）
            var hit = go.AddComponent<Image>();
            hit.sprite = null;
            hit.color = Color.clear;
            hit.raycastTarget = true;

            // 自绘层放子对象：同 GameObject 双 Graphic 抢同一 CanvasRenderer 会陷入
            // 无限重建死循环（UGUI 单 CanvasRenderer 只供一个 Graphic；Toggle/Switch
            // 的自绘 Graphic 均在子对象上，此处对齐）
            var gfxGo = new GameObject("Gfx", typeof(RectTransform));
            gfxGo.transform.SetParent(rect, false);
            var gfxRect = gfxGo.GetComponent<RectTransform>();
            gfxRect.anchorMin = Vector2.zero;
            gfxRect.anchorMax = Vector2.one;
            gfxRect.offsetMin = Vector2.zero;
            gfxRect.offsetMax = Vector2.zero;
            var g = gfxGo.AddComponent<SketchSliderGraphic>();
            g.Owner = slider;
            slider.Graphic = g;
            return slider;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsDragging = true;
            UpdateFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateFromPointer(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsDragging = false;
            if (Graphic != null)
            Graphic.SetVerticesDirty(); // 松手退琥珀
        }

        private void UpdateFromPointer(PointerEventData eventData)
        {
            RectTransform rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;
            float grabR = GrabberRadius(rect.rect.height);
            float trackW = rect.rect.width - grabR * 2f;
            if (trackW <= 0f)
            return;
            Value = (local.x - grabR) / trackW; // Value 内部 clamp + dirty
        }

    }

    /// <summary>滑条自绘层（sketch_hslider.gd _draw 逐式，本文件自包含网格原语）。
    /// 原嵌套于 SketchSlider 类内——Unity 对嵌套 MonoBehaviour 的 AddComponent 解析不稳（返回 null），提为顶级类。</summary>
    // （原嵌套于 SketchSlider 类内——Unity 对嵌套 MonoBehaviour 的 AddComponent 解析不稳，返回 null；提为顶级类）
    internal sealed class SketchSliderGraphic : MaskableGraphic
    {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置，Awake 里设太晚（实测全部自绘件静默空白）
        protected SketchSliderGraphic() { useLegacyMeshGeneration = false; }
        internal SketchSlider Owner;

        private bool _init;
        private int _seed;
        private int _tick;
        private float _next;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;  // 命中走根上的透明 hit Image
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

            float ratio = Owner != null ? Mathf.Clamp01(Owner.Value) : 0f;
            int ticks = Owner != null ? Owner.Ticks : 0;
            bool active = Owner != null && Owner.IsDragging;

            float h = r.height;
            float grabR = SketchSlider.GrabberRadius(h);
            const float trackH = 6f;    // gd 轨道条高
            // 轨道区：水平让出滑块半径，垂直居中细条
            Rect track = new Rect(grabR, (h - trackH) * 0.5f, r.width - grabR * 2f, trackH);
            Color edge = StickTokens.BORDER;
            edge.a = 0.28f;             // gd 凹槽上下缘墨线色

            WriteQuad(vh, track, StickTokens.GROOVE_BG);    // 凹槽底（硬边矩形）

            // 原生 tick 机制的自绘接管：均匀短竖线，上下各露凹槽 1.5px（gd draw_line 直线）
            if (ticks >= 2)
            {
                Color tickC = StickTokens.BORDER;
                tickC.a = 0.38f;
                float cy = h * 0.5f;
                for (int i = 0; i < ticks; i++)
                {
                    float t = (float)i / (ticks - 1);
                    float tx = track.x + track.width * t;
                    var line = new List<Vector2>(2)
                    {
                        new Vector2(tx, cy - trackH * 0.5f - 1.5f),
                        new Vector2(tx, cy + trackH * 0.5f + 1.5f),
                    };
                    WriteStrip(vh, line, tickC, 1.3f, 0f);
                }
            }

            // 凹槽上下缘波浪墨线（下缘 seed+13 错相位；抖动不随矩形缩小，gd 同注）
            WriteStrip(vh, SketchDrawMath.WavyLinePoints(
            new Vector2(track.x, track.y),
            new Vector2(track.x + track.width, track.y), _seed),
            edge, 1.3f, FeatherPx);
            WriteStrip(vh, SketchDrawMath.WavyLinePoints(
            new Vector2(track.x, track.y + trackH),
            new Vector2(track.x + track.width, track.y + trackH), _seed + 13),
            edge, 1.3f, FeatherPx);

            // 填充 = 中心一条粗马克笔笔画（血条「实心线条」同思路，沸腾 + 长度 = 值）
            if (ratio > 0.01f)
            {
                float fw = track.width * ratio;
                float midY = track.y + trackH * 0.5f;
                float capR = (trackH - 1f) * 0.5f;  // gd 圆头端帽半径
                var from = new Vector2(track.x + capR, midY);
                var to = new Vector2(track.x + fw - capR, midY);
                WriteStrip(vh, SketchDrawMath.WavyLinePoints(from, to, _seed + 77),
                StickTokens.ACCENT, trackH - 1f, FeatherPx);
                WriteFan(vh, WobbleCircle(from, capR, _seed), StickTokens.ACCENT);
                WriteFan(vh, WobbleCircle(to, capR, _seed), StickTokens.ACCENT);
            }

            // 手绘圆点滑块：白实心 + 深墨描边；拖动（gd has_focus）转琥珀 = §1.2 选中态
            float cx = track.x + track.width * ratio;
            List<Vector2> knob = WobbleCircle(new Vector2(cx, h * 0.5f), grabR, _seed);
            WriteFan(vh, knob, active ? StickTokens.ACCENT : StickTokens.TEXT);
            Color inkOutline = StickTokens.INK;     // 血条 COLOR_OUTLINE 同源墨色
            inkOutline.a = 0.95f;
            WriteLoop(vh, knob, inkOutline, SketchDrawMath.OutlineWidth, FeatherPx);
        }

        private void LazyInit()
        {
            if (_init)
            return;
            _init = true;
            _seed = Seed;
            _next = Time.unscaledTime + SketchDrawMath.WobbleInterval;
        }

        /// <summary>seed 基值（沸腾重掷序列由它决定，确定性）。</summary>
        public int Seed;

        /// <summary>确定性重掷序列（同 SketchWobbleGraphic.NextBoilSeed）。</summary>
        private int NextBoilSeed()
        {
            double v = System.Math.Sin(_tick * 127.1 + Seed * 0.3117) * 43758.5453;
            double f = v - System.Math.Floor(v);
            return (int)(f * 2000000000.0) - 1000000000;
        }

        /// <summary>14 段 wobble 圆（gd _draw_grabber：rr = radius + wobble(i*3, seed+31) * 0.95）。</summary>
        private static List<Vector2> WobbleCircle(Vector2 center, float radius, int seed)
        {
            const int seg = 14;
            var pts = new List<Vector2>(seg);
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                float rr = radius + SketchDrawMath.Wobble(i * 3, seed + 31) * SketchDrawMath.WobbleAmp;
                pts.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
            }
            return pts;
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

    /// <summary>填充三角扇（draw_colored_polygon 等价，凸路径下耳切结果一致）。</summary>
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

    /// <summary>
    /// 闭合粗折线带（gd draw_polyline 闭合 + antialiased）：miter 关节（折角钳制防
    /// 尖刺），feather&gt;0 时内外各挂 1px alpha 衰减带逼近引擎 AA。同 SketchWobbleGraphic.WriteLoop。
    /// </summary>
    private static void WriteLoop(VertexHelper vh, List<Vector2> pts, Color c,
    float width, float feather)
    {
        int n = pts.Count;
        if (n < 2)
        return;
        float half = Mathf.Max(width * 0.5f, 0.05f);
        int b = vh.currentVertCount;

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

    /// <summary>
    /// 开口粗折线带（draw_polyline 非闭合版：首尾平头无端帽，段间 miter）。
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
