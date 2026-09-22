using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦进度条（贴图槽版）—— game-2 SketchProgress（sketch_progress.gd）的 UGUI 复刻
    /// （**已换 Beveled Pixel 皮**）：底槽 = <see cref="PixelSkin.Track"/>（凹槽，默认 Frame tone），
    /// 填充 = <see cref="PixelSkin.Fill"/>（语义色：进度 <see cref="PixelFillKind.Neutral"/>、
    /// 冷却 <see cref="PixelFillKind.Warn"/>）。旧版是手绘槽位贴图 + 逐帧沸腾；
    /// 像素皮把"槽/条"表达为 Track + Fill 两层贴图件（色阶烘在贴图里，不做乘色）。
    ///
    /// 【进度实现】填充 Sliced Image 左对齐改宽度（fill 锚左、纵向拉伸、
    /// 宽 = 控件宽 × 进度）。注意 sliced 下改宽会压缩九宫边带——Godot ProgressBar 的
    /// fill（StyleBoxTexture 同贴图边距、同轴拉伸语义）同为压缩行为，两侧一致不追究。
    /// 【纯展示件】bg/fill 均不拦截点击。
    /// </summary>
    public sealed class SketchProgressBar : MonoBehaviour
    {
        private RectTransform _rect;
        private RectTransform _fillRect;
        private Image _fill;
        private PixelFillKind _fillKind = PixelFillKind.Neutral;
        private float _progress;

        /// <summary>当前进度只读快照（0..1，已 clamp）。</summary>
        public float Progress => _progress;

        /// <summary>填充语义色（运行时可切：进度 Neutral / 冷却 Warn）。</summary>
        public PixelFillKind FillKind
        {
            get => _fillKind;
            set { _fillKind = value; ApplyFill(); }
        }

        /// <summary>建一条像素进度条（Track 底槽 + Fill 填充）。</summary>
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

            // 轨道：凹槽件（Frame tone——与面板同族、下沉一档的"槽"感）
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = PixelSkin.Track(PixelTone.Frame);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;         // 像素件禁止乘色：槽色烘在贴图里
            bg.raycastTarget = false;

            // 填充：语义色条，锚左纵向拉伸，宽度表达进度
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            RectTransform frt = fillGo.GetComponent<RectTransform>();
            frt.SetParent(rect, false);
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(0f, 0f);
            var fill = fillGo.AddComponent<Image>();
            fill.type = Image.Type.Sliced;
            fill.color = Color.white;       // 像素件禁止乘色：填充色烘在贴图里
            fill.raycastTarget = false;
            bar._fillRect = frt;
            bar._fill = fill;
            bar.ApplyFill();

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
            if (_fill == null)
                _fill = FindFill();
        }

        /// <summary>按语义色换 Fill 贴图（进度 Neutral / 冷却 Warn）。</summary>
        private void ApplyFill()
        {
            if (_fill == null)
                _fill = FindFill();
            if (_fill != null)
                _fill.sprite = PixelSkin.Fill(_fillKind);
        }

        /// <summary>场景重载后私有字段不序列化，按子件名重取 Fill 引用（不能用
        /// GetComponentInChildren——那会先命中根上的 Track 贴图）。</summary>
        private Image FindFill()
        {
            Transform fill = transform.Find("Fill");
            return fill != null ? fill.GetComponent<Image>() : null;
        }

        private void ApplyWidth()
        {
            if (_fillRect == null || _rect == null)
                return;
            _fillRect.sizeDelta = new Vector2(_rect.rect.width * _progress, 0f);
        }
    }
}
