using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 头顶阵营指示 + 手绘风血条 —— game-2 HealthBarIndicator 的 UGUI 移植
    /// （只读移植源：stick-world/modules/units/scripts/entity/health_bar_indicator.gd）。
    ///
    /// 【视觉语义】满血 = 头顶一颗阵营色小圆点；第一次掉血后圆点展开为横条
    /// （实心填充 + 粗黑描边），条填充色 = 阵营色，血量 = 填充长度；回满退回圆点；
    /// 死亡整组隐藏——即 ShowWhenDamaged：满血隐藏横条 / 受击显示横条。
    ///
    /// 【挂载方式】组件自装配（Awake 建子物体），宿主挂到 world-space Canvas 下
    /// 的 RectTransform、把根节点摆在单位头顶 (0, OffsetY×WorldScale) 处
    /// （本批只出条本体，Canvas 装配与对账图在 P2；条本体 1:1 diff 下批）。
    /// gd 侧为 Node2D 子节点 z_index=1000 绝对顶层（gd:181-183），Unity 侧排序
    /// 由 world canvas sortingOrder / 层级次序表达，组件不管。
    ///
    /// 【未移植（P2 接玩法时定）】gd 的白色掉血残影（gd:387-390）、受击抖动
    /// （gd:376-377）、脱战渐隐状态机（gd:263-271）、圆点→横条 7/s 展开动画
    /// （EXPAND_SPEED gd:46，本批直接切换终态）、boiling 逐帧重掷（gd:276-279，
    /// 本批扰动取固定相位 seed=0）。血条描边宽 gd 为 1.6px（gd:36），而
    /// SketchBarGraphic 描边固定 1px——下批对账如需逐像素一致再补宽描边版。
    /// </summary>
    public sealed class StickWorldHealthBar : MonoBehaviour
    {
        /// <summary>
        /// stick-world 是 32px 网格像素口径，pirate 3D 世界坐标换算系数
        /// P2 接玩法时定（1.0 = gd 像素值直用）。
        /// </summary>
        public const float WorldScale = 1f;

        // ---- 几何常量（gd 原值，注释即出处行号） ----
        /// <summary>头顶标记 y 偏移（gd health_bar_indicator.gd:22 OFFSET_Y = -78.0）。</summary>
        public const float OffsetY = -78f;
        /// <summary>圆点半径 px（gd:27 DOT_RADIUS = 6.0）。</summary>
        public const float DotRadius = 6f;
        /// <summary>条高 px（gd:29 BAR_HEIGHT = 7.0）。</summary>
        public const float BarHeight = 7f;
        /// <summary>条宽 = clamp(34 + maxHp×0.42, 34, 96)，与血量上限成正比（gd:31-34）。</summary>
        public const float WidthBase = 34f;
        /// <summary>每点 maxHp 增宽（gd:32 WIDTH_PER_HP = 0.42）。</summary>
        public const float WidthPerHp = 0.42f;
        /// <summary>条宽下限（gd:33 WIDTH_MIN = 34.0）。</summary>
        public const float WidthMin = 34f;
        /// <summary>条宽上限（gd:34 WIDTH_MAX = 96.0）。</summary>
        public const float WidthMax = 96f;

        // ---- 阵营色（gd:48-53 FACTION_COLORS，0 = 中性灰） ----
        /// <summary>阵营 id：进攻方（gd:49 蓝）。</summary>
        public const int FactionAttacker = 1;
        /// <summary>阵营 id：防守方（gd:50 红）。</summary>
        public const int FactionDefender = 2;
        /// <summary>蓝方（gd:49 Color(0.38, 0.58, 0.98)）。</summary>
        public static readonly Color FactionAttackerColor = new Color(0.38f, 0.58f, 0.98f, 1f);
        /// <summary>红方（gd:50 Color(0.98, 0.44, 0.35)，2026-09-05 提亮档）。</summary>
        public static readonly Color FactionDefenderColor = new Color(0.98f, 0.44f, 0.35f, 1f);
        /// <summary>未参战灰点（gd:53 COLOR_NEUTRAL）。</summary>
        public static readonly Color FactionNeutralColor = new Color(0.75f, 0.75f, 0.72f, 1f);

        /// <summary>条底色（gd:54 COLOR_BG，暗底 72%）。</summary>
        public static readonly Color BarBackgroundColor = new Color(0.08f, 0.07f, 0.06f, 0.72f);
        /// <summary>描边墨色（gd:55 COLOR_OUTLINE）。</summary>
        public static readonly Color OutlineColor = new Color(0.05f, 0.04f, 0.03f, 0.95f);

        private SketchBarGraphic _bar;
        private WobbledDotGraphic _dot;
        private RectTransform _barRect;
        /// <summary>当前血量比例 [0,1]（gd:62 _ratio）。</summary>
        private float _hpRatio = 1f;
        /// <summary>死亡硬开关（gd:316-319 hp≤0 visible=false）。</summary>
        private bool _deadHidden;

        private void Awake()
        {
            _dot = CreateChildGraphic<WobbledDotGraphic>("Dot");
            ((RectTransform)_dot.transform).sizeDelta =
                new Vector2(DotRadius * 2f, DotRadius * 2f) * WorldScale;

            GameObject barGo = CreateChild("Bar");
            _barRect = (RectTransform)barGo.transform;
            _barRect.sizeDelta = new Vector2(WidthMin, BarHeight) * WorldScale;
            _bar = barGo.AddComponent<SketchBarGraphic>();
            _bar.BackgroundColor = BarBackgroundColor;
            _bar.BorderColor = OutlineColor;
            ApplyFormVisibility(); // 初始满血：只显示圆点（ShowWhenDamaged）
        }

        /// <summary>
        /// 设置阵营（语义同 gd set_faction，gd:239-241）：切圆点/条填充色。
        /// </summary>
        public void SetFaction(int factionId)
        {
            Color c = factionId == FactionAttacker ? FactionAttackerColor
                    : factionId == FactionDefender ? FactionDefenderColor
                    : FactionNeutralColor;
            SetFactionColor(c);
        }

        /// <summary>直接给阵营色（pirate 侧自定义阵营时用；圆点与条一起着色）。</summary>
        public void SetFactionColor(Color color)
        {
            if (_dot == null)
                return;
            _dot.FillColor = color;
            _dot.SetVerticesDirty();
            _bar.FillColor = color;
            _bar.SetVerticesDirty();
        }

        /// <summary>
        /// 设置血量（ShowWhenDamaged 主入口）：hp ≤ 0 整组隐藏（gd _on_died:316-319）；
        /// 满血只留圆点（gd _refresh:332-335 回满退回圆点）；掉血显示横条，
        /// 条宽按 maxHp 定长（gd _draw_bar:375），填充长度 = 血量比例。
        /// </summary>
        public void SetHealth(float hp, float maxHp)
        {
            _hpRatio = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
            _deadHidden = _hpRatio <= 0f;
            if (_deadHidden)
            {
                ApplyFormVisibility();
                return;
            }
            float width = Mathf.Clamp(WidthBase + Mathf.Max(maxHp, 0f) * WidthPerHp,
                WidthMin, WidthMax);
            _barRect.sizeDelta = new Vector2(width, BarHeight) * WorldScale;
            _bar.SetProgress(_hpRatio);
            ApplyFormVisibility();
        }

        /// <summary>按当前状态落显隐：死亡全隐；满血=点；受击=条（展开动画 P2）。</summary>
        private void ApplyFormVisibility()
        {
            bool fullHp = _hpRatio >= 1f;
            _dot.enabled = !_deadHidden && fullHp;
            _bar.enabled = !_deadHidden && !fullHp;
        }

        private GameObject CreateChild(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        private T CreateChildGraphic<T>(string name) where T : MaskableGraphic
        {
            GameObject go = CreateChild(name);
            T g = go.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }
    }

    /// <summary>
    /// 阵营色手绘圆点 —— gd _draw_dot（health_bar_indicator.gd:360-369）+
    /// _wobbled_circle（gd:492-498）的 UGUI 复刻：10 段扰动圆，实心填充 +
    /// 闭合 1.6px 描边。boiling 相位本批取固定 seed=0（gd 每 0.12s 重掷，P2 接）。
    /// 不用方块 SketchBarGraphic/Image：圆点形是设计语义（满血=点），方点失真，
    /// 且 Image 无 sprite 不出图——故在文件内私有复刻扰动圆网格。
    /// </summary>
    public sealed class WobbledDotGraphic : MaskableGraphic
    {
        // useLegacy 必须在 OnEnable 注册 mesh 回调前生效——构造器设置（Awake 里设太晚，自绘件静默空白）
        protected WobbledDotGraphic() { useLegacyMeshGeneration = false; }
        /// <summary>填充色（阵营色，经 SetFactionColor 注入）。</summary>
        public Color FillColor = StickWorldHealthBar.FactionNeutralColor;
        /// <summary>圆点半径 px（gd:27 DOT_RADIUS = 6.0）。</summary>
        public float Radius = StickWorldHealthBar.DotRadius;
        /// <summary>扰动幅度 px（gd:38 WOBBLE_AMP = 0.9）。</summary>
        public float WobbleAmp = 0.9f;
        /// <summary>描边宽 px（gd:36 OUTLINE_WIDTH = 1.6）。</summary>
        public float OutlineWidth = 1.6f;
        /// <summary>描边墨色（gd:55 COLOR_OUTLINE）。</summary>
        public Color OutlineColor = new Color(0.05f, 0.04f, 0.03f, 0.95f);

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f)
                return;
            float scale = StickWorldHealthBar.WorldScale;
            Vector2 c = r.center;
            const int seg = 10; // gd:364 _wobbled_circle(DOT_RADIUS, 10)
            var pts = new List<Vector2>(seg);
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                float rr = Radius * scale + Wobble(i) * WobbleAmp * scale;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
            }
            // 实心扇面（gd draw_colored_polygon:365）
            AddVert(vh, c, FillColor);
            for (int i = 0; i < seg; i++)
                AddVert(vh, pts[i], FillColor);
            for (int i = 0; i < seg; i++)
                vh.AddTriangle(0, 1 + i, 1 + (i + 1) % seg);
            // 闭合描边带（gd draw_polyline 闭环:367-369）
            WriteLoop(vh, pts, OutlineColor, OutlineWidth * scale);
        }

        /// <summary>
        /// 确定性伪噪声（gd _wobble:441-443 同公式：sin(i×127.1+seed×0.3117)
        /// × 43758.5453 取小数 − 0.5），固定 seed=0 → 同机重跑逐位一致。
        /// </summary>
        private static float Wobble(int i)
        {
            float v = Mathf.Sin(i * 127.1f) * 43758.5453f;
            return v - Mathf.Floor(v) - 0.5f;
        }

        private static void AddVert(VertexHelper vh, Vector2 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }

        /// <summary>闭合 miter 线带（与 SketchBarGraphic.WriteLoop 同款等价复刻，
        /// 该处 private，按「各文件自持小实现」先例自持一份）。</summary>
        private static void WriteLoop(VertexHelper vh, List<Vector2> pts, Color c, float width)
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
            for (int j = 0; j < n; j++)
            {
                int a = b + j * 2;
                int d = b + ((j + 1) % n) * 2;
                vh.AddTriangle(a, a + 1, d + 1);
                vh.AddTriangle(a, d + 1, d);
            }
        }

        private static Vector2 SegmentNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude < 1e-10f ? Vector2.zero : new Vector2(-d.y, d.x).normalized;
        }
    }
}
