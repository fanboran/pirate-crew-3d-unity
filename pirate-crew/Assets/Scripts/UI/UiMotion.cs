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
    ///  · 同一目标开新动画会取消旧动画（后到优先），不会叠加抖动；
    ///  · 全部动画用 <see cref="Time.unscaledDeltaTime"/>——hit-stop 顿帧时 UI 反馈照常；
    ///  · <see cref="OnDisable"/> 收尾复位，防止场景切换时把缩放/透明度停在半路。
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
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                float s = UiMotionRules.PunchCurve(t, from, overshoot);
                tr.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            tr.localScale = Vector3.one;
            _punches.Remove(target);
        }

        // ------------------------------------------------------------------
        // 面板 滑入/淡出（CanvasGroup 透明度 + 根节点纵向位移）
        // ------------------------------------------------------------------

        Coroutine _panelRoutine;

        /// <summary>
        /// 面板出现：<c>go.SetActive(true)</c> → 透明度 ease-out 淡入、位置从下方
        /// <paramref name="slideOffsetPixels"/> 滑回原位。结束时不改 alpha（保持 1）。
        /// </summary>
        public void ShowPanel(GameObject panel, float seconds, float slideOffsetPixels)
        {
            if (panel == null || seconds <= 0f)
                return;

            if (_panelRoutine != null)
                StopCoroutine(_panelRoutine);

            // 恢复 HidePanel 断掉的交互开关
            CanvasGroup restore = panel.GetComponent<CanvasGroup>();
            if (restore != null)
            {
                restore.interactable = true;
                restore.blocksRaycasts = true;
            }

            panel.SetActive(true);
            _panelRoutine = StartCoroutine(ShowPanelRoutine(panel, seconds, slideOffsetPixels));
        }

        IEnumerator ShowPanelRoutine(GameObject panel, float seconds, float slideOffsetPixels)
        {
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            RectTransform rect = panel.transform as RectTransform;
            Vector2 basePosition = rect != null ? rect.anchoredPosition : Vector2.zero;

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
            _panelRoutine = null;
        }

        /// <summary>
        /// 面板消失：ease-in 淡出后 <c>SetActive(false)</c>（由本协程负责收尾，调用方不要再 SetActive）。
        /// 起手即断交互（CanvasGroup.blocksRaycasts/interactable=false），防止淡出半路还能点。
        /// </summary>
        public void HidePanel(GameObject panel, float seconds)
        {
            if (panel == null)
                return;

            if (_panelRoutine != null)
                StopCoroutine(_panelRoutine);

            if (seconds <= 0f)
            {
                panel.SetActive(false);
                _panelRoutine = null;
                return;
            }

            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null)
                group = panel.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            _panelRoutine = StartCoroutine(HidePanelRoutine(panel, group, seconds));
        }

        IEnumerator HidePanelRoutine(GameObject panel, CanvasGroup group, float seconds)
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
            _panelRoutine = null;
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

            if (_panelRoutine != null)
            {
                StopCoroutine(_panelRoutine);
                _panelRoutine = null;
            }

            for (int i = 0; i < _fills.Count; i++)
            {
                if (_fills[i].Fill != null)
                    SnapFill(_fills[i].Fill, _fills[i].Target);
            }
        }
    }
}
