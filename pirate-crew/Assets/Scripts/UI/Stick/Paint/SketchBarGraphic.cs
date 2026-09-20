using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 扁平进度条自绘 Graphic —— game-2 ProgressPainter（core/ui_framework/components/
    /// progress_painter.gd）的 UGUI 等价物，供头顶血条/进度条复用。
    ///
    /// 【绘制语义】逐句移植 draw_bar（progress_painter.gd:28-34）：
    /// bg 填充 quad + 前景 quad（宽 = 宽×进度，进度 ≤0 不画前景）+ 1px 描边框。
    /// 全部硬边 quad——Godot draw_rect 本就无 AA，此层不做羽化（与隔壁
    /// SketchWobbleGraphic 的沸腾描边不是一回事，勿混用）。
    /// 【描边实现】Godot draw_rect(unfilled) 实为四角闭合 polyline（宽 1、无 AA），
    /// 这里用四点闭合 miter 线带等价复刻——角部单次覆盖，不做双重混合。
    /// </summary>
    public sealed class SketchBarGraphic : MaskableGraphic
    {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置，Awake 里设太晚（实测全部自绘件静默空白）
        protected SketchBarGraphic() { useLegacyMeshGeneration = false; }
        /// <summary>底色（progress_painter.gd:12 COLOR_BG，半透明黑 60%）。</summary>
        public Color BackgroundColor = new Color(0f, 0f, 0f, 0.6f);

        /// <summary>描边色（progress_painter.gd:14 COLOR_BORDER，深墨 80%）。</summary>
        public Color BorderColor = new Color(0f, 0f, 0f, 0.8f);

        /// <summary>
        /// 前景色。gd draw_bar 的 fg_color 由调用方传入、类本身无默认；
        /// 组件形态下取强调色 ACCENT 作运行时缺省（ui_tokens.json 原键）。
        /// </summary>
        public Color FillColor = StickTokens.ACCENT;

        /// <summary>当前进度 [0,1]，一律经 <see cref="SetProgress"/> 更新。</summary>
        [SerializeField] private float _progress;

        /// <summary>进度只读快照（调试/HUD 绑定用）。</summary>
        public float Progress => _progress;

        protected override void Awake()
        {
            base.Awake();
            // 走 VertexHelper 路径（Awake 先于首次网格重建，场景反序列化也覆盖到）
            // 纯绘制层不拦截点击（gd 侧为 Node2D _draw，本就不参与 UI 命中）
            raycastTarget = false;
        }

        // Reset 是编辑器魔法方法（非 virtual），必须 new 隐藏否则 CS0114 警告
        private new void Reset()
        {
            raycastTarget = false;
        }

        /// <summary>
        /// 更新进度并请求重绘（clamp 收敛到 [0,1]；值未变不 dirty）——
        /// 对应 gd set_progress_value（progress_painter.gd:21-23），
        /// "变了才 dirty" 是 UGUI 侧的等价 queue_redraw 语义。
        /// </summary>
        public void SetProgress(float ratio)
        {
            float clamped = Mathf.Clamp01(ratio);
            if (Mathf.Approximately(clamped, _progress))
                return;
            _progress = clamped;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f)
                return;

            // draw_bar（progress_painter.gd:28-34）逐句移植
            WriteQuad(vh, r, BackgroundColor);
            float fgW = r.width * Mathf.Clamp(_progress, 0f, 1f);
            if (fgW > 0f)
                WriteQuad(vh, new Rect(r.x, r.y, fgW, r.height), FillColor);
            WriteLoop(vh, RectCorners(r), BorderColor, 1f, 0f);
        }

        // ---- 网格写出原语（与 SketchWobbleGraphic 各持一份小实现，保持文件自包含） ----

        /// <summary>矩形填充 quad（两个三角形，硬边）。</summary>
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

        /// <summary>闭合 miter 线带（无羽化版，四角单次覆盖）。</summary>
        private static void WriteLoop(VertexHelper vh, List<Vector2> pts, Color c,
            float width, float feather)
        {
            int n = pts.Count;
            if (n < 2)
                return;
            float half = Mathf.Max(width * 0.5f, 0.05f);
            int b = vh.currentVertCount;
            for (int j = 0; j < n; j++)
            {
                Vector2 nPrev = SegmentNormal(pts[(j - 1 + n) % n], pts[j]);
                Vector2 nCur = SegmentNormal(pts[j], pts[(j + 1) % n]);
                Vector2 m = nPrev + nCur;
                m = m.sqrMagnitude < 1e-10f ? nCur : m.normalized;
                float dot = Vector2.Dot(m, nCur);
                float len = Mathf.Min(half / Mathf.Max(dot, 0.35f), half * 3f);
                AddVert(vh, pts[j] + m * len, c);
                AddVert(vh, pts[j] - m * len, c);
            }
            int stride = feather > 0f ? 4 : 2;
            for (int j = 0; j < n; j++)
            {
                int a = b + j * stride;
                int d = b + ((j + 1) % n) * stride;
                vh.AddTriangle(a, a + 1, d + 1);
                vh.AddTriangle(a, d + 1, d);
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

        private static void AddVert(VertexHelper vh, Vector2 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }
    }
}
