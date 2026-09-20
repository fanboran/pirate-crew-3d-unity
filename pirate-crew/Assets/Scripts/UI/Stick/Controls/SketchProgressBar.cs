using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦进度条（贴图槽版）—— game-2 SketchProgress（sketch_progress.gd）的
    /// UGUI 复刻：九宫格沸腾贴图（progress_bg 轨道 + progress_fill 填充），帧由
    /// <see cref="SketchBoil"/> 驱动（gd SketchTextures 全局帧轮换同语义）。
    ///
    /// 【进度实现】填充 Sliced Image 左对齐改宽度（fill 锚左、纵向拉伸、
    /// 宽 = 控件宽 × 进度）。注意 sliced 下改宽会压缩九宫边带——Godot ProgressBar 的
    /// fill（StyleBoxTexture 同贴图边距、同轴拉伸语义）同为压缩行为，两侧一致不追究。
    /// 【纯展示件】bg/fill/自层均不拦截点击。
    /// </summary>
    public sealed class SketchProgressBar : MonoBehaviour
    {
        private RectTransform _rect;
        private RectTransform _fillRect;
        private float _progress;

        /// <summary>当前进度只读快照（0..1，已 clamp）。</summary>
        public float Progress => _progress;

        /// <summary>建一条贴图进度条（bg + fill 双沸腾件）。</summary>
        public static SketchProgressBar Create(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var bar = go.AddComponent<SketchProgressBar>();
            bar._rect = rect;

            // 轨道：progress_bg 槽（gd add_theme_stylebox("background", progress_bg())）
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SketchSkin.Frame("progress_bg", 0);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = false;
            rect.gameObject.AddComponent<SketchBoil>().Slot = "progress_bg";

            // 填充：progress_fill 槽，锚左纵向拉伸，宽度表达进度
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            RectTransform frt = fillGo.GetComponent<RectTransform>();
            frt.SetParent(rect, false);
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(0f, 0f);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = SketchSkin.Frame("progress_fill", 0);
            fill.type = Image.Type.Sliced;
            fill.color = Color.white;
            fill.raycastTarget = false;
            fillGo.AddComponent<SketchBoil>().Slot = "progress_fill";
            bar._fillRect = frt;
            return bar;
        }

        /// <summary>
        /// 更新进度（clamp 收敛到 [0,1]，gd set value 同语义）；值未变不动布局。
        /// </summary>
        public void SetProgress(float ratio)
        {
            float p = Mathf.Clamp01(ratio);
            if (Mathf.Approximately(p, _progress))
                return;
            _progress = p;
            ApplyWidth();
        }

        private void OnEnable()
        {
            ApplyWidth();
        }

        private void ApplyWidth()
        {
            if (_fillRect == null || _rect == null)
                return;
            _fillRect.sizeDelta = new Vector2(_rect.rect.width * _progress, 0f);
        }
    }
}
