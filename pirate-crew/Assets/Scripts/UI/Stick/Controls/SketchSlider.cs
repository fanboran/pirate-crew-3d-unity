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
}
