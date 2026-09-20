using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦复选框 —— game-2 SketchCheckBox（sketch_check_box.gd）的 UGUI 复刻：
    /// wobble 方框 + 马克笔对勾，全部一层自绘 Graphic 承担（血条同源 boiling）。
    ///
    /// 【尺寸口径】勾选框 18²——按任务口径对齐 sketch_icons.gd BOX_SIZE=(18,18) 兜底
    /// （gd SketchCheckBox.BOX=16 是控件自绘常量，渲染兜底图标族用 18；两值差 2px 在
    /// 文本容差 1~3px 红线内，取任务定的 18）；方框-文字间距 6px（gd h_separation）。
    /// 【画法】gd _draw 逐式：方框 = draw_panel(rect, fill, outline, 1.4, 4.0)，
    /// 选中琥珀只上底不上字（§1.5）——琥珀描边 @0.95 + 琥珀 14% 底，对勾用白（TEXT）；
    /// 悬停 BORDER_STRONG；常态 TEXT@0.32。对勾 = 三折点马克笔笔画（宽 2.2，逐点
    /// wobble 扰动 0.8），y 向量自 gd 顶向下量取翻转为 UGUI 底向上。
    /// 【命中语义】gd CheckBox 整控件可点 → 根上透明 hit Image 承担（自绘层不拦截），
    /// 点击切换 <see cref="IsOn"/>。
    /// </summary>
    public sealed class SketchToggle : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
        IPointerExitHandler
    {
        /// <summary>自绘方框边长（px）——sketch_icons.gd BOX_SIZE 兜底口径（见类注）。</summary>
        internal const float BoxSide = 18f;

        /// <summary>方框与文字间距（gd h_separation=6）。</summary>
        internal const float Sep = 6f;

        [SerializeField] private bool _isOn;
        private bool _hovered;

        internal SketchToggleGraphic Graphic;
        internal TextMeshProUGUI Label;

        /// <summary>可交互（false 时整体 0.4 变暗且点击无效，gd is_disabled 同义）。</summary>
        public bool Interactable = true;

        /// <summary>选中态（gd button_pressed）。</summary>
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

        /// <summary>选中态变化回调（gd toggled 信号）。</summary>
        public event Action<bool> Changed;

        /// <summary>建一个复选框。label 为空则只有方框；fontSize 0 = 主题默认（FONT_BODY）。</summary>
        public static SketchToggle Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
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

            var toggle = go.AddComponent<SketchToggle>();
            toggle._isOn = isOn;

            // 透明命中层（gd 整控件可点同语义）
            var hit = go.AddComponent<Image>();
            hit.sprite = null;
            hit.color = Color.clear;
            hit.raycastTarget = true;

            // 自绘方框槽：左侧 18×18 垂直居中（gd 图标槽占位，防塌陷的 blank 纹理
            // 语义在 UGUI 用固定 rect 直接等价）
            var boxGo = new GameObject("Box", typeof(RectTransform));
            RectTransform brt = boxGo.GetComponent<RectTransform>();
            brt.SetParent(rect, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.sizeDelta = new Vector2(BoxSide, BoxSide);
            brt.anchoredPosition = Vector2.zero;
            var g = boxGo.AddComponent<SketchToggleGraphic>();
            g.Owner = toggle;
            toggle.Graphic = g;

            if (!string.IsNullOrEmpty(label))
            {
                var labelGo = new GameObject("Label", typeof(RectTransform));
                RectTransform lrt = labelGo.GetComponent<RectTransform>();
                lrt.SetParent(rect, false);
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.sizeDelta = new Vector2(Mathf.Max(0f, size.x - BoxSide - Sep), size.y);
                lrt.anchoredPosition = new Vector2(BoxSide + Sep, 0f);
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

        /// <summary>复选框自绘层（sketch_check_box.gd _draw 逐式，本文件自包含网格原语）。</summary>
        internal sealed class SketchToggleGraphic : MaskableGraphic
        {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置，Awake 里设太晚（实测全部自绘件静默空白）
        protected SketchToggleGraphic() { useLegacyMeshGeneration = false; }
            internal SketchToggle Owner;

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

                // 方框画在图标槽内（gd：向内留 0.5px 防裁剪容器切边）
                var box = new Rect(0.5f, 0.5f, SketchToggle.BoxSide - 1f, SketchToggle.BoxSide - 1f);
                Color fill = on ? WithAlpha(StickTokens.ACCENT, 0.14f * dim) : Color.clear;
                Color outline = on
                    ? WithAlpha(StickTokens.ACCENT, 0.95f * dim)
                    : hovered
                        ? StickTokens.BORDER_STRONG
                        : WithAlpha(StickTokens.TEXT, 0.32f * dim);
                WritePanel(vh, box, _seed, fill, outline, 1.4f, 4f);

                // 对勾：三折点马克笔笔画（白），逐点小幅扰动（gd 逐式；gd y 自顶向下量，
                // UGUI y 向上——offset 统一从 box.yMax 反减换算）
                if (on)
                {
                    Color ink = WithAlpha(StickTokens.TEXT, dim);
                    float side = SketchToggle.BoxSide;
                    var pts = new List<Vector2>(3);
                    for (int i = 0; i < 3; i++)
                    {
                        float t = i / 2f;
                        float x = box.x + Mathf.Lerp(side * 0.22f, side * 0.78f, t);
                        float fromTop = Mathf.Lerp(side * 0.55f, side * 0.25f, t);
                        if (i == 1)
                            fromTop = side * 0.74f;     // 中点下沉（对勾的底）
                        var p = new Vector2(x, box.yMax - fromTop);
                        p += new Vector2(SketchDrawMath.Wobble(i * 3, _seed),
                            SketchDrawMath.Wobble(i * 3 + 1, _seed)) * 0.8f;
                        pts.Add(p);
                    }
                    WriteStrip(vh, pts, ink, 2.2f, FeatherPx);
                }
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

        /// <summary>填充三角扇（draw_colored_polygon 等价）。</summary>
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

        /// <summary>闭合粗折线带（gd draw_polyline 闭合 + antialiased 的 miter + 羽化版）。</summary>
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
        /// 手绘面板底（gd SketchDraw.draw_panel 等价）：实心扰动多边形 + 闭合粗描边，
        /// 过小矩形回退普通矩形（gd 退化保护同款）。
        /// </summary>
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

        /// <summary>开口粗折线带（draw_polyline 非闭合版：首尾平头无端帽，段间 miter）。</summary>
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

        private static Vector2 StripJointNormal(List<Vector2> pts, int j)
        {
            Vector2 nPrev = j > 0 ? SegmentNormal(pts[j - 1], pts[j]) : SegmentNormal(pts[j], pts[j + 1]);
            Vector2 nCur = j < pts.Count - 1 ? SegmentNormal(pts[j], pts[j + 1]) : nPrev;
            Vector2 m = nPrev + nCur;
            return m.sqrMagnitude < 1e-10f ? nCur : m.normalized;
        }

        private static Vector2 SegmentNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude < 1e-10f ? Vector2.zero : new Vector2(-d.y, d.x).normalized;
        }
    }
}
