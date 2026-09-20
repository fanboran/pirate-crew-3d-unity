using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦拨动开关 —— game-2 SketchCheckButton（sketch_check_button.gd）的 UGUI
    /// 复刻：手绘凹槽 + 圆钮，全部一层自绘 Graphic 承担（血条同源 boiling）。
    ///
    /// 【尺寸口径】凹槽 34×18（gd GROOVE 同值）、圆钮半径 6.5（KNOB_R 同值）。
    /// 【画法】gd _draw 逐式：凹槽 = draw_panel 胶囊（圆角拉到半高 9）；
    /// 开：琥珀凹槽底 14% + 琥珀描边 @0.95，圆钮右移（§1.2 选中态琥珀只上底不上字）；
    /// 关：白（TEXT）描边凹槽 @0.32 + 白圆钮；悬停 BORDER_STRONG；禁用整体 dim 0.4。
    /// 圆钮 = 14 段 wobble 圆（wobble(i*3, seed+31) * 0.95）+ 深墨描边 INK@0.95*dim，
    /// off 左 / on 右各留 2px 内边。
    /// 【命中语义】gd CheckButton 整控件可点 → 根上透明 hit Image 承担，点击切换
    /// <see cref="IsOn"/>；文字标签可选（gd CheckButton text 同位：凹槽右 6px）。
    /// </summary>
    public sealed class SketchSwitch : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
        IPointerExitHandler
    {
        /// <summary>凹槽尺寸（gd GROOVE = (34, 18)）。</summary>
        internal const float GrooveW = 34f;
        internal const float GrooveH = 18f;

        /// <summary>圆钮半径（gd KNOB_R = 6.5）。</summary>
        internal const float KnobR = 6.5f;

        /// <summary>凹槽与文字间距（gd h_separation=6）。</summary>
        internal const float Sep = 6f;

        [SerializeField] private bool _isOn;
        private bool _hovered;

        internal SketchSwitchGraphic Graphic;
        internal TextMeshProUGUI Label;

        /// <summary>可交互（false 时整体 0.4 变暗且点击无效，gd is_disabled 同义）。</summary>
        public bool Interactable = true;

        /// <summary>开启态（gd button_pressed）。</summary>
        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value)
                    return;
                _isOn = value;
                if (Graphic != null)
                    Graphic.SetVerticesDirty();
                Changed?.Invoke(value);
            }
        }

        /// <summary>开启态变化回调（gd toggled 信号）。</summary>
        public event Action<bool> Changed;

        /// <summary>建一个拨动开关。label 为空则只有凹槽；fontSize 0 = 主题默认（FONT_BODY）。</summary>
        public static SketchSwitch Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font, string label = null,
            bool isOn = false, float fontSize = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var toggle = go.AddComponent<SketchSwitch>();
            toggle._isOn = isOn;

            // 透明命中层（gd 整控件可点同语义）
            var hit = go.AddComponent<Image>();
            hit.sprite = null;
            hit.color = Color.clear;
            hit.raycastTarget = true;

            // 自绘凹槽槽位：左侧 34×18 垂直居中（gd 图标槽占位同语义）
            var grooveGo = new GameObject("Groove", typeof(RectTransform));
            RectTransform grt = grooveGo.GetComponent<RectTransform>();
            grt.SetParent(rect, false);
            grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f);
            grt.pivot = new Vector2(0f, 0.5f);
            grt.sizeDelta = new Vector2(GrooveW, GrooveH);
            grt.anchoredPosition = Vector2.zero;
            var g = grooveGo.AddComponent<SketchSwitchGraphic>();
            g.Owner = toggle;
            toggle.Graphic = g;

            if (!string.IsNullOrEmpty(label))
            {
                var labelGo = new GameObject("Label", typeof(RectTransform));
                RectTransform lrt = labelGo.GetComponent<RectTransform>();
                lrt.SetParent(rect, false);
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.sizeDelta = new Vector2(Mathf.Max(0f, size.x - GrooveW - Sep), size.y);
                lrt.anchoredPosition = new Vector2(GrooveW + Sep, 0f);
                var text = labelGo.AddComponent<TextMeshProUGUI>();
                text.text = label;
                if (font != null)
                    text.font = font;
                text.fontSize = fontSize > 0f ? fontSize : StickTokens.FONT_BODY;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.color = StickTokens.TEXT;
                text.enableWordWrapping = false;
                text.raycastTarget = false;
                text.margin = Vector4.zero;
                toggle.Label = text;
            }
            return toggle;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactable)
                return;
            IsOn = !IsOn;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Dirty();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Dirty();
        }

        internal bool Hovered => _hovered;

        private void Dirty()
        {
            if (Graphic != null)
                Graphic.SetVerticesDirty();
        }

        /// <summary>拨动开关自绘层（sketch_check_button.gd _draw 逐式，本文件自包含网格原语）。</summary>
        internal sealed class SketchSwitchGraphic : MaskableGraphic
        {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置，Awake 里设太晚（实测全部自绘件静默空白）
        protected SketchSwitchGraphic() { useLegacyMeshGeneration = false; }
            internal SketchSwitch Owner;

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
                if (GetPixelAdjustedRect().width <= 0f)
                    return;

                bool on = Owner != null && Owner.IsOn;
                bool interactable = Owner == null || Owner.Interactable;
                bool hovered = Owner != null && Owner.Hovered && interactable;
                float dim = interactable ? 1f : 0.4f;
                float t = on ? 1f : 0f;

                // 凹槽画在图标槽内（gd：向内留 1px 防裁剪容器切边）；wobble 胶囊（圆角拉到半高）
                var groove = new Rect(1f, 0f, SketchSwitch.GrooveW - 2f, SketchSwitch.GrooveH);
                Color fill = on ? WithAlpha(StickTokens.ACCENT, 0.14f * dim) : Color.clear;
                Color outline = on
                    ? WithAlpha(StickTokens.ACCENT, 0.95f * dim)
                    : hovered
                        ? StickTokens.BORDER_STRONG
                        : WithAlpha(StickTokens.TEXT, 0.32f * dim);
                WritePanel(vh, groove, _seed, fill, outline, 1.4f, SketchSwitch.GrooveH * 0.5f);

                // 圆钮：wobble 圆，off 左 / on 右，各留 2px 内边（gd lerp 逐式）
                float cx = Mathf.Lerp(groove.x + SketchSwitch.KnobR + 2f,
                    groove.xMax - SketchSwitch.KnobR - 2f, t);
                Vector2 center = new Vector2(cx, groove.center.y);
                List<Vector2> knob = WobbleCircle(center, SketchSwitch.KnobR, _seed);
                WriteFan(vh, knob, on ? WithAlpha(StickTokens.ACCENT, dim)
                    : WithAlpha(StickTokens.TEXT, dim));
                Color inkOutline = StickTokens.INK;     // 血条 COLOR_OUTLINE 同源墨色
                inkOutline.a = 0.95f * dim;
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

            private int NextBoilSeed()
            {
                double v = System.Math.Sin(_tick * 127.1 + Seed * 0.3117) * 43758.5453;
                double f = v - System.Math.Floor(v);
                return (int)(f * 2000000000.0) - 1000000000;
            }

            /// <summary>14 段 wobble 圆（gd _draw_knob：rr = radius + wobble(i*3, seed+31) * 0.95）。</summary>
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
        }

        // ---- 网格写出原语（与 SketchWobbleGraphic 各持一份小实现，保持文件自包含） ----

        private const float FeatherPx = 1f;

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

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

        /// <summary>线段单位法向（退化线段返回零向量；与 SketchSeparator 同款小实现，文件自包含）。</summary>
        private static Vector2 SegmentNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude < 1e-10f ? Vector2.zero : new Vector2(-d.y, d.x).normalized;
        }

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

        /// <summary>手绘面板底（gd SketchDraw.draw_panel 等价，含过小矩形退化保护）。</summary>
        private static void WritePanel(VertexHelper vh, Rect r, int seed, Color fill,
            Color outline, float outlineW, float cornerR)
        {
            if (r.width < 8f || r.height < 8f)
            {
                if (fill.a > 0f)
                    WriteQuad(vh, r, fill);
                if (outline.a > 0f)
                    WriteLoop(vh, RectCorners(r), outline, outlineW, 0f);
                return;
            }
            List<Vector2> pts = SketchDrawMath.WobblyRectPath(r, seed, cornerR, -1f);
            if (fill.a > 0f)
                WriteFan(vh, pts, fill);
            if (outline.a > 0f)
                WriteLoop(vh, pts, outline, outlineW, FeatherPx);
        }
    }
}
