using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite <c>ui::IntEntry</c>（<c>external/aseprite-ref/src/ui/int_entry.cpp</c> +
    /// <c>int_entry.h</c>）的移植，照源的继承关系（IntEntry : Entry）派生自
    /// <see cref="AseEntry"/>。
    ///
    /// 【已移植】
    ///   · <c>int_entry.h:23-65</c>（m_min/m_max/m_useSlider/m_maxValueUnbounded/onValueChange/
    ///     onAcceptUnicodeChar 面）
    ///   · <c>int_entry.cpp:55-59</c>（getValue：convertTextToValue 后夹到 [min, max]）
    ///   · <c>int_entry.cpp:61-77</c>（setValue：夹取 → 写文本 → onValueChange）
    ///   · <c>int_entry.cpp:79-137</c>（onProcessMessage：kFocusLeave 回填夹取 +
    ///     deselectText；kMouseDown 请求焦点 + 全选；kMouseWheel 滚轮改值 + 全选；
    ///     kKeyDown 非数字「吃键」）
    ///   · <c>int_entry.cpp:165-179</c>（onChange → onValueChange；onAcceptUnicodeChar = '0'..'9'）
    ///
    /// 【未移植（登记偏差）】①源 IntEntry 点开一个 <c>TransparentPopupWindow</c> 里的
    /// <c>Slider</c> 拖拽改值（int_entry.cpp:181-257），本端无此弹出滑条——滚轮/键盘改值已够用；
    /// ②<see cref="UseSlider"/> 只保留状态位，不生成弹层。③source 的 slider delegate
    /// （convertTextToValue/convertValueToText）在本端由 <see cref="ConvertTextToValue"/> /
    /// 整数字符串承担（<c>sketchslider.cpp</c> 同款 strtol 前缀解析语义）。
    /// </summary>
    public class AseIntEntry : AseEntry, IScrollHandler
    {
        int _min;
        int _max;
        bool _maxValueUnbounded;
        bool _useSlider = true;

        /// <summary>源 <c>getMinValue()</c> 语义（int_entry.cpp:37 ctor 的 min）。</summary>
        public int MinValue => _min;

        /// <summary>源 <c>getMaxValue()</c> 语义。</summary>
        public int MaxValue => _max;

        /// <summary>源 <c>useSlider()</c>（int_entry.h:33）；本端只保留状态位（弹层未移植）。</summary>
        public bool UseSlider
        {
            get => _useSlider;
            set => _useSlider = value;
        }

        /// <summary>源 <c>maxValueUnbounded()</c>（int_entry.h:36-37）。</summary>
        public bool MaxValueUnbounded
        {
            get => _maxValueUnbounded;
            set => _maxValueUnbounded = value;
        }

        /// <summary>源 ctor <c>IntEntry(min, max, delegate)</c> 的值域参数。</summary>
        public void Init(int min, int max)
        {
            _min = min;
            _max = max;
        }

        /// <summary>源 <c>getValue()</c>（int_entry.cpp:55-59）。</summary>
        public virtual int GetValue()
        {
            int value = ConvertTextToValue(Text);
            return Mathf.Clamp(value, _min, _maxValueUnbounded ? int.MaxValue : _max);
        }

        /// <summary>源 <c>setValue()</c>（int_entry.cpp:61-77）：夹取后写文本（源另管弹层，本端无）。</summary>
        public virtual void SetValue(int value)
        {
            value = Mathf.Clamp(value, _min, _maxValueUnbounded ? int.MaxValue : _max);
            SetText(value.ToString(CultureInfo.InvariantCulture));
            OnValueChange();
        }

        /// <summary>源 <c>getValue()/setValue()</c> 的属性面。</summary>
        public int Value
        {
            get => GetValue();
            set => SetValue(value);
        }

        /// <summary>源 <c>onValueChange()</c>（int_entry.cpp:171-174，缺省 do nothing）。</summary>
        protected virtual void OnValueChange() { }

        /// <summary>源 <c>IntEntry::onChange()</c>（int_entry.cpp:165-169）：Entry::onChange + onValueChange。</summary>
        protected override void OnChange()
        {
            base.OnChange();
            OnValueChange();
        }

        /// <summary>源 <c>onAcceptUnicodeChar()</c>（int_entry.cpp:176-179）：只收 '0'..'9'。</summary>
        protected override bool OnAcceptUnicodeChar(int unicodeChar)
        {
            return unicodeChar >= '0' && unicodeChar <= '9';
        }

        /// <summary>
        /// 源 kMouseDownMessage（int_entry.cpp:88-94）：requestFocus + captureMouse +
        /// openPopup + **selectAllText**，并 return true——**不**像 Entry 那样按落点定位 caret。
        /// 本端无弹层，保留「请求焦点 + 全选」两条可观察行为。
        /// </summary>
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!IsEnabled)
                return;
            RequestFocus();
            eventData.useDragThreshold = false;
            SelectAllText();
        }

        /// <summary>
        /// 源 kFocusLeaveMessage（int_entry.cpp:82-86）：失焦先回填夹取后的合法值再 deselect。
        /// 在基类失焦逻辑之前执行。
        /// </summary>
        public override void OnDeselect(BaseEventData eventData)
        {
            SetValue(GetValue());
            base.OnDeselect(eventData);
        }

        /// <summary>
        /// 源 kMouseWheelMessage（int_entry.cpp:108-120）：
        /// <c>newValue = oldValue + wheelDelta.x - wheelDelta.y</c>，夹到 [min,max]，
        /// 变了才 setValue + selectAllText。
        /// </summary>
        public void OnScroll(PointerEventData eventData)
        {
            if (!IsEnabled)
                return;
            int oldValue = GetValue();
            int newValue = oldValue
                + Mathf.RoundToInt(eventData.scrollDelta.x)
                - Mathf.RoundToInt(eventData.scrollDelta.y);
            newValue = Mathf.Clamp(newValue, _min, _maxValueUnbounded ? int.MaxValue : _max);
            if (newValue != oldValue)
            {
                SetValue(newValue);
                SelectAllText();
            }
            eventData.Use();
        }

        /// <summary>
        /// 源 <c>Slider::convertTextToValue</c>（slider.cpp:98-105 的缺省实现，
        /// int_entry.cpp:57 经 slider 调用）：<c>std::strtol(text, nullptr, 10)</c>——
        /// 前缀解析（"12x" → 12），无数字前缀 → 0。
        /// </summary>
        public static int ConvertTextToValue(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            int start = i;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                i++;
            int digits = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                i++;
            if (i == digits)
                return 0;
            long v;
            if (!long.TryParse(text.Substring(start, i - start), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out v))
                return 0;
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }
    }
}
