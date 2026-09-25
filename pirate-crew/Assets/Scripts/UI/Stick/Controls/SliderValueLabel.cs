using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 滑条槽内数值标签（theme <c>&lt;style id="slider"&gt;</c> 的
    /// <c>&lt;text color="slider_empty_text" align="center middle"/&gt;</c> 层）。
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
        /// <summary>数值文本件（装配时赋值；场景重载后按名兜底重取）。</summary>
        public TextMeshProUGUI Label;

        private Slider _slider;
        private int _lastPercent = int.MinValue;

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
            TextMeshProUGUI label = Label;
            if (label == null)
            {
                Transform child = transform.Find("Value");
                label = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                Label = label;
            }
            if (label == null || _slider == null)
                return;

            // 顶点像素对齐兜底：文本顶点必须落整画布格（本标签是 Stretch 铺满滑条的，
            // 相位由父链决定；缺 PixelSnapText 时字会整列压在半格上——实拍该文本
            // 104 条边缘全为奇数屏像素，而面板内其余文本（按钮/字段/分组）全偶）。
            if (label.GetComponent<PixelSnapText>() == null)
                label.gameObject.AddComponent<PixelSnapText>();

            int percent = Mathf.RoundToInt(Mathf.Clamp01(_slider.value) * 100f);
            if (percent == _lastPercent)
                return;

            _lastPercent = percent;
            label.SetText(percent + "%");
        }
    }
}
