using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 滑条槽内数值标签（theme <c>&lt;style id="slider"&gt;</c> 的 <c>&lt;text&gt;</c> 层）。
    ///
    /// 【双色口径（paintSlider 源码实锄，skin_theme.cpp:1756-1790）】同一句居中文案画两遍：
    /// 充满段（暗面 #41444A）上用 <c>slider_full_text</c> #C0C0C0、空槽段（亮面 #575B61）上用
    /// <c>slider_empty_text</c> #202125，分界线穿过字形中间就逐像素换色。本组件按帧驱动
    /// 两枚同文案标签（各在 RectMask2D 裁剪框下）与裁剪框宽度——填充边界取 Slider 自己的
    /// fillRect 实宽取整，与可见填充同源，换色线不会和填充边错位。
    ///
    /// 【为什么按帧对齐而不是挂 onValueChanged】控制器刷新控件走的是
    /// <c>SetValueWithoutNotify</c>（刻意避免把"刷新"当成一次用户输入）——它**不触发**
    /// onValueChanged，只挂事件的标签会停在初值（实拍：槽内恒显 0% 而槽色已按真实音量填充）。
    /// 本组件在 <c>LateUpdate</c> 里对比最后显示值，值一变就重写；对"用户拖"和"程序设"
    /// 两条路径都成立，代价只是一次整数比较。
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public sealed class SliderValueLabel : MonoBehaviour
    {
        /// <summary>充满段上的浅字（slider_full_text）。装配时赋值；场景重载后按名兜底重取。</summary>
        public TextMeshProUGUI LabelLight;

        /// <summary>空槽段上的深字（slider_empty_text）。装配时赋值；场景重载后按名兜底重取。</summary>
        public TextMeshProUGUI LabelDark;

        private Slider _slider;
        private RectTransform _clipFull;
        private RectTransform _clipRest;
        private int _lastPercent = int.MinValue;
        private bool _snapChecked;

        private void OnEnable()
        {
            _slider = GetComponent<Slider>();
            _lastPercent = int.MinValue;
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (_slider == null)
                _slider = GetComponent<Slider>();
            if (_slider == null)
                return;

            TextMeshProUGUI light = LabelLight;
            TextMeshProUGUI dark = LabelDark;
            if (light == null || dark == null)
            {
                Transform full = transform.Find("ClipFull");
                Transform rest = transform.Find("ClipRest");
                if (light == null && full != null)
                {
                    Transform child = full.Find("ValueLight");
                    light = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                }
                if (dark == null && rest != null)
                {
                    Transform child = rest.Find("ValueDark");
                    dark = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                }
                LabelLight = light;
                LabelDark = dark;
                _clipFull = full as RectTransform;
                _clipRest = rest as RectTransform;
            }
            if (light == null || dark == null)
                return;

            // 顶点像素对齐兜底：结果缓存，只在缺件时每帧重查（旧版无条件两次 GetComponent）。
            if (!_snapChecked)
            {
                EnsureSnap(light);
                EnsureSnap(dark);
                _snapChecked = light.GetComponent<PixelSnapText>() != null
                    && dark.GetComponent<PixelSnapText>() != null;
            }

            int percent = Mathf.RoundToInt(Mathf.Clamp01(_slider.value) * 100f);
            if (percent == _lastPercent)
                return;

            _lastPercent = percent;
            string text = percent + "%";
            light.SetText(text);
            dark.SetText(text);

            if (_clipFull == null)
                _clipFull = light.transform.parent as RectTransform;
            if (_clipRest == null)
                _clipRest = dark.transform.parent as RectTransform;
            if (_clipFull == null || _clipRest == null)
                return;

            // 裁剪框宽 = 填充实宽取整：换色线与填充边同源同取整，缝 <1 画布格不可见。
            float w = _slider.fillRect != null ? Mathf.Round(_slider.fillRect.rect.width) : 0f;
            _clipFull.sizeDelta = new Vector2(w, 0f);
            _clipRest.offsetMin = new Vector2(w, 0f);
        }

        private static void EnsureSnap(TextMeshProUGUI label)
        {
            if (label != null && label.GetComponent<PixelSnapText>() == null)
                label.gameObject.AddComponent<PixelSnapText>();
        }
    }
}
