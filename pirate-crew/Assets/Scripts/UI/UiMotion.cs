using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// HUD 动效驱动（MonoBehaviour 薄壳）：曲线与时长在 <see cref="UiMotionRules"/>（纯逻辑、可无头测），
    /// 本类只负责喂 unscaled 时间并把值写回 UGUI 控件。由 <see cref="BattleHud"/> 在 Awake 时挂到自身，
    /// 不进场景装配（避免给既有场景增加必填引用）。
    ///
    /// 【语义】
    ///  · 同一目标开新动画会取消旧动画（后到优先），不会叠加抖动；面板动画按面板各自管理，
    ///    一个面板的 Show/Hide 不影响其他面板在跑的协程；
    ///  · 全部动画用 <see cref="Time.unscaledDeltaTime"/>——hit-stop 顿帧时 UI 反馈照常；
    ///  · <see cref="OnDisable"/> 收尾复位：punch/pop 缩放归位、全部面板协程停掉、
    ///    血条滚动落位，防止场景切换时把缩放/透明度停在半路。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiMotion : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // punch（按钮/横幅）
        // ------------------------------------------------------------------

        readonly Dictionary<Graphic, Coroutine> _punches = new Dictionary<Graphic, Coroutine>();

        /// <summary>对控件的 RectTransform 做一次 punch 缩放（详见 <see cref="UiMotionRules.PunchCurve"/>）。</summary>
        public void Punch(Graphic target, float seconds)
        {
            if (target == null || seconds <= 0f)
                return;

            if (_punches.TryGetValue(target, out Coroutine running) && running != null)
                StopCoroutine(running);

            _punches[target] = StartCoroutine(PunchRoutine(target, seconds));
        }

        IEnumerator PunchRoutine(Graphic target, float seconds)
        {
            Transform tr = target.rectTransform;
            float from = UiMotionRules.PunchFromScale;
            float overshoot = UiMotionRules.PunchOvershootScale;
            float t = 0f;
            while (t < 1f)
            {
                if (target == null)
                {
                    // 目标中途被销毁：无处可写，退出并清掉取消表条目（防异常终止刷日志）
                    _punches.Remove(target);
                    yield break;
                }
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                float s = UiMotionRules.PunchCurve(t, from, overshoot);
                tr.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            tr.localScale = Vector3.one;
            _punches.Remove(target);
        }

        /// <summary>
        /// pop：从 0.6 起手 back-out 弹到 1（<see cref="UiMotionRules.PopScale"/>）。
        /// 与 punch 共用取消表（同一目标后到取消先到）。回合徽章 / 星级 / 弹窗入场用。
        /// </summary>
        public void Pop(Graphic target)
        {
            if (target == null)
                return;

            if (_punches.TryGetValue(target, out Coroutine running) && running != null)
                StopCoroutine(running);

            _punches[target] = StartCoroutine(PopRoutine(target));
        }

        IEnumerator PopRoutine(Graphic target)
        {
            Transform tr = target.rectTransform;
            float t = 0f;
            while (t < 1f)
            {
                if (target == null)
                {
                    // 目标中途被销毁：无处可写，退出并清掉取消表条目（防异常终止刷日志）
                    _punches.Remove(target);
                    yield break;
                }
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / UiMotionRules.PopSeconds);
                float s = UiMotionRules.PopScale(t);
                tr.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            tr.localScale = Vector3.one;
            _punches.Remove(target);
        }

        // ------------------------------------------------------------------
        // 面板 滑入/淡出（CanvasGroup 透明度 + 根节点纵向位移）
        // ------------------------------------------------------------------

        /// <summary>一个面板的动画登记：当前协程 + 面板原位。按面板各一份，互不掐。</summary>
        sealed class PanelAnim
        {
            public Coroutine Routine;
            /// <summary>面板原位（首次 ShowPanel 入口捕获一次）：Show 的纵向位移从它
            /// 绝对写回，动画中途被打断也不会把残偏移固化成新原位。</summary>
            public Vector2 BasePosition;
        }

        /// <summary>面板 → 动画登记。Show/Hide 只停同一面板的协程。</summary>
        readonly Dictionary<GameObject, PanelAnim> _panels = new Dictionary<GameObject, PanelAnim>();

        /// <summary>取该面板的动画登记（没有则建档并捕获原位），停掉它正在跑的旧协程。</summary>
        PanelAnim RegisterPanel(GameObject panel)
        {
            if (_panels.TryGetValue(panel, out PanelAnim anim))
            {
                if (anim.Routine != null)
                {
                    StopCoroutine(anim.Routine);
                    anim.Routine = null;
                }
                return anim;
            }

            anim = new PanelAnim
            {
                BasePosition = panel.transform is RectTransform rect ? rect.anchoredPosition : Vector2.zero,
            };
            _panels[panel] = anim;
            return anim;
        }

        /// <summary>
        /// 面板出现：<c>go.SetActive(true)</c> → 透明度 ease-out 淡入、位置从下方
        /// <paramref name="slideOffsetPixels"/> 滑回原位。结束时不改 alpha（保持 1）。
        /// </summary>
        public void ShowPanel(GameObject panel, float seconds, float slideOffsetPixels)
        {
            if (panel == null || seconds <= 0f)
                return;

            PanelAnim anim = RegisterPanel(panel);

            // 恢复 HidePanel 断掉的交互开关
            CanvasGroup restore = panel.GetComponent<CanvasGroup>();
            if (restore != null)
            {
                restore.interactable = true;
                restore.blocksRaycasts = true;
            }

            panel.SetActive(true);
            anim.Routine = StartCoroutine(
                ShowPanelRoutine(panel, anim, seconds, slideOffsetPixels));
        }

        IEnumerator ShowPanelRoutine(GameObject panel, PanelAnim anim, float seconds, float slideOffsetPixels)
        {
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            RectTransform rect = panel.transform as RectTransform;
            Vector2 basePosition = anim.BasePosition;

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                float e = UiMotionRules.EaseOutCubic(t);
                if (group != null)
                    group.alpha = e;
                if (rect != null)
                    rect.anchoredPosition = basePosition + Vector2.down * (slideOffsetPixels * (1f - e));
                yield return null;
            }

            if (group != null)
                group.alpha = 1f;
            if (rect != null)
                rect.anchoredPosition = basePosition;
            anim.Routine = null;
        }

        /// <summary>
        /// 面板消失：ease-in 淡出后 <c>SetActive(false)</c>（由本协程负责收尾，调用方不要再 SetActive）。
        /// 起手即断交互（CanvasGroup.blocksRaycasts/interactable=false），防止淡出半路还能点。
        /// </summary>
        public void HidePanel(GameObject panel, float seconds)
        {
            if (panel == null)
                return;

            PanelAnim anim = RegisterPanel(panel);

            if (seconds <= 0f)
            {
                panel.SetActive(false);
                anim.Routine = null;
                return;
            }

            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null)
                group = panel.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            anim.Routine = StartCoroutine(HidePanelRoutine(panel, anim, group, seconds));
        }

        IEnumerator HidePanelRoutine(GameObject panel, PanelAnim anim, CanvasGroup group, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                if (group != null)
                    group.alpha = 1f - UiMotionRules.EaseInQuad(t);
                yield return null;
            }

            panel.SetActive(false);
            if (group != null)
                group.alpha = 1f;                       // 下次 ShowPanel 从头淡入
            anim.Routine = null;
        }

        // ------------------------------------------------------------------
        // 血条滚动（Image.fillAmount 语义无关——本项目血条用 anchorMax 表达 28 帧比例）
        // ------------------------------------------------------------------

        sealed class FillState
        {
            public Image Fill;
            public float Current;
            public float Target;
        }

        readonly List<FillState> _fills = new List<FillState>();

        /// <summary>建档/重置：直接把显示比例钉在 <paramref name="ratio"/>（不滚动，用于初始构建）。</summary>
        public void SnapFill(Image fill, float ratio)
        {
            FillState state = FindOrCreate(fill);
            state.Current = ratio;
            state.Target = ratio;
            ApplyRatio(state);
        }

        /// <summary>设定目标比例，显示值按 <see cref="UiMotionRules.ApproachExponential"/> 逐帧追赶。</summary>
        public void SetFillTarget(Image fill, float ratio)
        {
            FillState state = FindOrCreate(fill);
            state.Target = Mathf.Clamp01(ratio);
            if (!gameObject.activeInHierarchy)
            {
                // 不可驱动时直接落位，等激活后不会出现"从旧值追新值"的跳变
                state.Current = state.Target;
            }
            ApplyRatio(state);
        }

        // ------------------------------------------------------------------
        // damage ghost 双条（主填充快、白色残影慢；见 UiMotionRules.StepGhost）
        // ------------------------------------------------------------------

        sealed class GhostPair
        {
            public Image Fill;
            public Image Ghost;
            public float FillCurrent, FillTarget;
            public float GhostCurrent;
        }

        readonly List<GhostPair> _ghosts = new List<GhostPair>();

        /// <summary>建档/重置（双条）：主填充与 ghost 都钉在 <paramref name="ratio"/>。</summary>
        public void SnapFillPair(Image fill, Image ghost, float ratio)
        {
            GhostPair pair = FindOrCreateGhost(fill, ghost);
            pair.FillCurrent = ratio;
            pair.FillTarget = ratio;
            pair.GhostCurrent = ratio;
            ApplyGhostPair(pair);
        }

        /// <summary>
        /// 双条落值：主填充快速追（<see cref="UiMotionRules.HealthDrainSpeedPerSecond"/>），
        /// 白色 ghost 只降不升、慢速追（<see cref="UiMotionRules.StepGhost"/>）——
        /// 受击时主条先掉、白条拖出残影。
        /// </summary>
        public void SetFillPairTarget(Image fill, Image ghost, float ratio)
        {
            GhostPair pair = FindOrCreateGhost(fill, ghost);
            pair.FillTarget = Mathf.Clamp01(ratio);
            if (!gameObject.activeInHierarchy)
            {
                pair.FillCurrent = pair.FillTarget;
                pair.GhostCurrent = UiMotionRules.StepGhost(pair.GhostCurrent, pair.FillTarget, 0f);
            }
            ApplyGhostPair(pair);
        }

        GhostPair FindOrCreateGhost(Image fill, Image ghost)
        {
            for (int i = 0; i < _ghosts.Count; i++)
            {
                if (_ghosts[i].Fill == fill && _ghosts[i].Ghost == ghost)
                    return _ghosts[i];
            }

            var pair = new GhostPair { Fill = fill, Ghost = ghost };
            _ghosts.Add(pair);
            return pair;
        }

        void ApplyGhostPair(GhostPair pair)
        {
            if (pair.Fill != null)
                pair.Fill.rectTransform.anchorMax = new Vector2(pair.FillCurrent, 1f);
            if (pair.Ghost != null)
                pair.Ghost.rectTransform.anchorMax = new Vector2(pair.GhostCurrent, 1f);
        }

        void StepGhosts(float dt)
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                GhostPair pair = _ghosts[i];
                if (pair.Fill == null && pair.Ghost == null)
                {
                    _ghosts.RemoveAt(i);
                    continue;
                }

                pair.FillCurrent = UiMotionRules.ApproachExponential(
                    pair.FillCurrent, pair.FillTarget, dt, UiMotionRules.HealthDrainSpeedPerSecond);
                pair.GhostCurrent = UiMotionRules.StepGhost(pair.GhostCurrent, pair.FillTarget, dt);
                ApplyGhostPair(pair);
            }
        }

        FillState FindOrCreate(Image fill)
        {
            for (int i = 0; i < _fills.Count; i++)
            {
                if (_fills[i].Fill == fill)
                    return _fills[i];
            }

            var state = new FillState { Fill = fill };
            _fills.Add(state);
            return state;
        }

        void ApplyRatio(FillState state)
        {
            if (state.Fill != null)
                state.Fill.rectTransform.anchorMax = new Vector2(state.Current, 1f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _fills.Count - 1; i >= 0; i--)
            {
                FillState state = _fills[i];
                if (state.Fill == null)
                {
                    _fills.RemoveAt(i);                    // 行被销毁（重建名册）时清理
                    continue;
                }

                if (Mathf.Approximately(state.Current, state.Target))
                    continue;

                state.Current = UiMotionRules.ApproachExponential(
                    state.Current, state.Target, dt, UiMotionRules.HealthDrainSpeedPerSecond);
                ApplyRatio(state);
            }

            StepGhosts(dt);
        }

        // ------------------------------------------------------------------
        // 收尾
        // ------------------------------------------------------------------

        void OnDisable()
        {
            // 场景切换/关闭时把动画状态复位，不留半路值
            foreach (KeyValuePair<Graphic, Coroutine> pair in _punches)
            {
                if (pair.Key != null)
                    pair.Key.rectTransform.localScale = Vector3.one;
            }
            _punches.Clear();

            // 全部面板协程停掉并清登记（下次 OnEnable 从头建档、按当时原位捕获）
            foreach (KeyValuePair<GameObject, PanelAnim> pair in _panels)
            {
                if (pair.Value.Routine != null)
                    StopCoroutine(pair.Value.Routine);
                pair.Value.Routine = null;
            }
            _panels.Clear();

            for (int i = 0; i < _fills.Count; i++)
            {
                if (_fills[i].Fill != null)
                    SnapFill(_fills[i].Fill, _fills[i].Target);
            }
        }
    }
}
