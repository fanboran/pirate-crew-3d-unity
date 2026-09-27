using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Aseprite 滑条控件（theme <c>&lt;style id="slider"&gt;</c>，theme.xml:1053-1058）的逐函数移植，
    /// 替代「UGUI <see cref="Slider"/> + <see cref="SliderValueLabel"/> 补丁件」那套近似实现。
    ///
    /// 【权威源】
    ///   · <c>external/aseprite-ref/src/ui/slider.h:25-74</c>（值域/信号/虚函数面）
    ///   · <c>external/aseprite-ref/src/ui/slider.cpp:32-296</c>（构造 / setRange / setValue /
    ///     convertValueToText / onProcessMessage 全部分支 / onChange / onSliderReleased）
    ///   · <c>external/aseprite-ref/src/app/ui/skin/skin_theme.cpp:1667-1794</c>（SkinTheme::paintSlider：
    ///     分区绘制 + 双色文本）
    ///   · <c>skin_theme.cpp:1838-1903</c>（SkinTheme::drawText：**整数**居中）
    ///   · <c>src/ui/theme.cpp:609-630</c>（Theme::calcTextInfo：文本区 = 控件矩形内缩 border）
    ///
    /// 【为什么推翻 UGUI Slider】UGUI <see cref="Slider"/> 的取值是 0..1 float + 事件驱动，
    /// 与源的三条语义都对不上：①源的值是**整数** min..max（slider.h:30-31），步进/钳制/显示全走
    /// 整数除法；②源 <c>setValue</c> **刻意不发 Change**（slider.cpp:69），UGUI 的 setter 会发
    /// onValueChanged；③源的分界线、文本双色、文本水平位置**同取一个整数 value** 推出来
    /// （paintSlider），旧实现是「fillRect 实宽取整 → 裁剪框」「Mathf.RoundToInt(value*100) → 文案」
    /// 「Slider 自己的分数锚点 → 填充边」三路各算，值/边界/文案天然会互相错位（创始人实测
    /// 「拖动/显值不对」）。本控件用**单一几何源** <see cref="TrackRect"/> + <see cref="SplitX"/>
    /// 把这三路收成一路。
    ///
    /// 【几何偏差（已登记，唯一一处不逐行照搬）】源 <c>paintSlider:1672</c> 取
    /// <c>rc = widget->clientBounds().shrink(widget->border())</c>，其中 border =
    /// （左 5 / 右 5 = 件切片 w1/w3，theme.xml:175；上 4 / 下 5 = <c>&lt;style id="slider"
    /// border-top="4" border-bottom="5"&gt;</c> 覆盖切片 h1=5/h3=6，theme.xml:1053）。
    /// 源的控件矩形 = border(9) + 文本行高（<c>Slider</c> 未覆写 onSizeHint，尺寸由
    /// <c>Theme::calcWidgetMetrics</c> 算，theme.cpp:785-819），槽是内缩后的小框。
    /// 本工程调用方给的矩形**就是槽件自然盒**（16 高 = 件切片 5+5+6，交接档「按件高 16 摆」），
    /// 槽件 1:1 铺满——再按源内缩，16 高盒里的槽只剩 7 高：九宫格竖向压缩、双色裁剪框（高 = rc.h）
    /// 会把 8px 位图字切顶切底。故本移植令 <c>rc = 控件矩形</c>，等价于把
    /// <see cref="SourceBorderLeft"/> 全取 0 代进源公式（rc.x=0、rc.width=w）——除这一处常量外，
    /// 源的每条公式都逐字照搬。要切回源口径：把常量换成源值、并在调用点把控件盒加高到
    /// <see cref="SourceSizeHintHeight"/>。
    ///
    /// 【装配】只走 <see cref="Create"/>；孩子件名见 <see cref="Resolve"/>（场景重载后私有字段
    /// 不序列化，按名兜底重取——同 <see cref="SketchButton"/> 的坑）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SketchSlider : Selectable, IDragHandler, IScrollHandler
    {
        // ================== theme 派生常量（源的数值，备查 / 备切换） ==================

        /// <summary>源 border 左 = slider 件切片 w1（theme.xml:175 w1="5"）。</summary>
        public const int SourceBorderLeft = 5;
        /// <summary>源 border 上 = <c>&lt;style id="slider" border-top="4"&gt;</c>（theme.xml:1053）。</summary>
        public const int SourceBorderTop = AseLayout.SliderBorderTop;          // 4
        /// <summary>源 border 右 = 件切片 w3（theme.xml:175 w3="5"）。</summary>
        public const int SourceBorderRight = 5;
        /// <summary>源 border 下 = <c>&lt;style id="slider" border-bottom="5"&gt;</c>（theme.xml:1053）。</summary>
        public const int SourceBorderBottom = AseLayout.SliderBorderBottom;    // 5

        /// <summary>slider 件自然宽（切片 w1+w2+w3 = 5+6+5，theme.xml:175）。</summary>
        public const int PartWidth = 16;
        /// <summary>slider 件自然高（切片 h1+h2+h3 = 5+5+6，theme.xml:175-178）。</summary>
        public const int PartHeight = 16;

        /// <summary>键盘连打窗口（slider.cpp:193 <c>&gt; 1500</c> 毫秒后清缓冲）。</summary>
        const int KeyBufferTimeoutMs = 1500;

        /// <summary>
        /// 【onSizeHint 的移植】源 <c>Slider</c> **没有**覆写 onSizeHint——尺寸来自 theme：
        /// <c>Theme::calcWidgetMetrics</c>（theme.cpp:785-819）把 border（= 上面四个常量）与
        /// 文本行高相加。本工程控件矩形 = 槽件自然盒，故实际取 <see cref="PartHeight"/>；
        /// 本函数留给「要按源口径摆控件盒」的调用点。
        /// </summary>
        public static int SourceSizeHintHeight(int textLineHeight)
        {
            return SourceBorderTop + SourceBorderBottom + textLineHeight;
        }

        // ================== 值域（slider.h:29-37 / slider.cpp:32-85） ==================

        [SerializeField] int _min;
        [SerializeField] int _max = 100;
        [SerializeField] int _value;
        [SerializeField] bool _readOnly;

        /// <summary>源 <c>obs::signal&lt;void()&gt; Change</c>（slider.h:46）：**只在用户操作改变值时发**，
        /// 程序设值（<see cref="Value"/> setter）不发——见 slider.cpp:69「It DOES NOT emit CHANGE
        /// signal! to avoid recursive calls.」载荷是新的整数值（源无载荷，调用点直读 getValue）。</summary>
        public event Action<int> ValueChanged;

        /// <summary>源 <c>obs::signal&lt;void()&gt; SliderReleased</c>（slider.h:47）：鼠标抬起且原本持有
        /// capture 时发（slider.cpp:171-179）。</summary>
        public event Action Released;

        /// <summary>源 <c>SliderDelegate::onGetTextFromValue</c>（slider.cpp:87-96）。
        /// 缺省 = <c>"%d"</c>——**源不带百分号**；要显示 "70%" 的调用点在这里接（同 AlphaSlider
        /// 走 delegate 出带后缀文案的做法）。</summary>
        public Func<int, string> ValueToText;

        /// <summary>源 <c>SliderDelegate::onGetValueFromText</c>（slider.cpp:98-105），缺省 strtol 十进制。</summary>
        public Func<string, int> TextToValue;

        /// <summary>源 <c>getMinValue()</c>。</summary>
        public int MinValue => _min;

        /// <summary>源 <c>getMaxValue()</c>。</summary>
        public int MaxValue => _max;

        /// <summary>源 <c>getValue()/setValue()</c>。**setter 不触发 <see cref="ValueChanged"/>**
        /// （slider.cpp:60-70），与源一致。</summary>
        public int Value
        {
            get => _value;
            set => SetValue(value);
        }

        /// <summary>源 <c>setReadOnly()</c>（slider.h:36-37）：只读仍显示、不接受任何输入。</summary>
        public bool ReadOnly
        {
            get => _readOnly;
            set => _readOnly = value;
        }

        /// <summary>【非源扩展】0..1 归一化出入口——本工程音量这类调用点按 0..1 float 存储
        /// （源只有整值 min..max）。取值为线性映射，写值四舍五入到整格。</summary>
        public float Value01
        {
            get => _max == _min ? 0f : (float)(_value - _min) / (_max - _min);
            set => SetValue(Mathf.RoundToInt(Mathf.Lerp(_min, _max, Mathf.Clamp01(value))));
        }

        /// <summary>源 <c>setRange()</c>（slider.cpp:43-47）：纠正值域 + 重绘。</summary>
        public void SetRange(int min, int max)
        {
            EnforceValidRange(min, max);
            Refresh();
        }

        /// <summary>源 <c>setValue()</c>：钳制 + 变了才重绘；**不发信号**。</summary>
        public void SetValue(int value)
        {
            int old = _value;
            _value = Mathf.Clamp(value, _min, _max);
            if (_value != old)
                Refresh();
        }

        /// <summary>UGUI 时代调用点（<c>SetValueWithoutNotify</c>）的迁移别名——语义与
        /// <see cref="SetValue"/> 完全一致（源 setValue 本就不通知）。</summary>
        public void SetValueWithoutNotify(int value)
        {
            SetValue(value);
        }

        /// <summary>源 <c>enforceValidRange()</c>（slider.cpp:49-58）：min &gt; max 时 max = min；
        /// value 钳进新值域。</summary>
        public void EnforceValidRange(int min, int max)
        {
            if (min > max)
                max = min;                 // 源：Do not allow min > max
            _min = min;
            _max = max;
            _value = Mathf.Clamp(_value, _min, _max);
        }

        /// <summary>源 <c>convertValueToText()</c>（slider.cpp:87-96）：delegate 优先，缺省 "%d"。</summary>
        public string ConvertValueToText(int value)
        {
            return ValueToText != null ? ValueToText(value) : value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>源 <c>convertTextToValue()</c>（slider.cpp:98-105）：delegate 优先，
        /// 缺省 <c>std::strtol(text, nullptr, 10)</c>——**前缀解析**（"12x" → 12），
        /// 无数字前缀 → 0。</summary>
        public int ConvertTextToValue(string text)
        {
            if (TextToValue != null)
                return TextToValue(text);
            if (string.IsNullOrEmpty(text))
                return 0;

            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))        // strtol 跳前导空白
                i++;
            int start = i;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                i++;
            int digits = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                i++;
            if (i == digits)
                return 0;                                                // 无数字 → strtol 给 0
            long v;
            if (!long.TryParse(text.Substring(start, i - start), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out v))
                return 0;
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        /// <summary>源 <c>updateValue()</c>（slider.cpp:82-85）：虚函数，拖动/键盘路径走它，
        /// 滚轮路径走 <see cref="SetValue"/>（源两处不一致，照搬——见 slider.cpp:163 / 256）。
        /// AlphaSlider 一类子类在源里靠覆写它拦截，C# 侧留同一虚点。</summary>
        protected virtual void UpdateValue(int value)
        {
            SetValue(value);
        }

        /// <summary>源 <c>onChange()</c> → <c>Change()</c>（slider.cpp:276-279）。</summary>
        protected virtual void OnChange()
        {
            if (ValueChanged != null)
                ValueChanged(_value);
        }

        /// <summary>源 <c>onSliderReleased()</c> → <c>SliderReleased()</c>（slider.cpp:281-284）。</summary>
        protected virtual void OnSliderReleased()
        {
            if (Released != null)
                Released();
        }

        // ================== 几何：单一源（输入映射与分区绘制共用） ==================

        RectTransform _rect;

        RectTransform Rect
        {
            get
            {
                if (_rect == null)
                    _rect = (RectTransform)transform;
                return _rect;
            }
        }

        /// <summary>
        /// 【源 <c>rc</c>】paintSlider:1672 <c>rc = widget->clientBounds().shrink(widget->border())</c>；
        /// 本工程口径 = 控件矩形（几何偏差见类注释）。输入映射与 <see cref="SplitX"/> 都取它——
        /// 拖到哪里、分界线就落在哪里（旧实现三路各算才是「显值不对」的根）。
        /// </summary>
        protected RectInt TrackRect()
        {
            Rect r = Rect.rect;
            return new RectInt(0, 0, Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height));
        }

        /// <summary>
        /// 【源 paintSlider:1674-1677 的分界线，纯函数面】整数运算（C++ int 除法截断；C# 同）——
        /// **不是**比例浮点，保证分界线永远落在整格上。
        /// <code>
        /// if (min != max) x = rc.x + rc.width * (value - min) / (max - min);
        /// else            x = rc.x;
        /// </code>
        /// </summary>
        public static int SplitXFor(int value, int min, int max, int rcX, int rcW)
        {
            if (min == max)
                return rcX;
            return rcX + rcW * (value - min) / (max - min);
        }

        /// <summary>
        /// 【源 slider.cpp:143-161 的取值公式，纯函数面】<paramref name="mouseX"/> 是相对控件左缘的
        /// 整格横坐标（源是 positionForDisplay 的整型屏幕坐标；减 <paramref name="rcX"/> 即相对槽左缘）。
        /// <paramref name="leftMode"/> = true 走左键绝对映射（slider.cpp:147-151），
        /// false 走右键相对映射 + 精细步进（slider.cpp:153-159）。
        /// <code>
        /// range = max - min + 1;  w = rc.width;
        /// 左键: if (w == 0) w = 1;            value = min + range * (mouseX - rc.x) / w;
        /// 右键: if (w == 0 || range > w) { w = 1; range = 1; }
        ///                                     value = pressValue + (mouseX - pressX) * range / w;
        /// value = clamp(value, min, max);
        /// </code>
        /// </summary>
        public static int ValueFromPointer(int mouseX, int rcX, int rcW, int min, int max,
            bool leftMode, int pressX, int pressValue)
        {
            int range = max - min + 1;
            int w = rcW;
            int value;
            if (leftMode)
            {
                if (w == 0)
                    w = 1;
                value = min + range * (mouseX - rcX) / w;
            }
            else
            {
                if (w == 0 || range > w)
                {
                    w = 1;
                    range = 1;
                }
                value = pressValue + (mouseX - pressX) * range / w;
            }
            return Mathf.Clamp(value, min, max);
        }

        protected int SplitX()
        {
            RectInt rc = TrackRect();
            return SplitXFor(_value, _min, _max, rc.x, rc.width);
        }

        // ================== 鼠标：源 onProcessMessage 的 kMouseDown / kMouseMove / kMouseUp ==================

        int _pressX;        // 源 static slider_press_x（这里按实例存，同时只有一个在拖）
        int _pressValue;    // 源 static slider_press_value
        bool _pressLeft;    // 源 static slider_press_left
        bool _dragging;     // 源 hasCapture()

        /// <summary>
        /// 源 <c>kMouseDownMessage</c>（slider.cpp:116-134）：<c>!isEnabled() || isReadOnly()</c> → 吃掉
        /// 消息什么都不做；否则 setSelected + captureMouse，记下滑块按下点，并
        /// <c>[[fallthrough]]</c> 到 move 分支——**左键按下即按落点取值**（这就是「拖动跟手」）。
        /// </summary>
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable() || _readOnly)
                return;                                          // 源：return true（不处理）

            _dragging = true;                                    // 源 captureMouse()
            _pressLeft = eventData.button == PointerEventData.InputButton.Left;
            _pressX = PointerX(eventData);
            _pressValue = _value;                                // 源 getSliderThemeInfo(…, &value)

            ApplyPointer(eventData);                             // 源 kMouseDown 的 [[fallthrough]]

            // UGUI 有 10px 拖拽死区（EventSystem.pixelDragThreshold + ShouldStartDrag）——
            // 源是 capture 后每个 move 都跟手，没有死区。关掉它（UGUI 自己的 Slider 也这么干）。
            eventData.useDragThreshold = false;

            base.OnPointerDown(eventData);                       // 源 setFocusStop(true) → 键盘焦点
        }

        /// <summary>
        /// 源 <c>kMouseMoveMessage</c>（slider.cpp:136-169）的 C# 通道。**绝对映射**（左键）：
        /// <c>value = min + range * (mousePos.x - rc.x) / w</c>（range = max-min+1），
        /// 分界线永远压在鼠标脚下；**相对映射**（右键）：<c>press_value + (mx - press_x) * range / w</c>，
        /// <c>range &gt; w</c> 时退化为 1 格 = 1 值（源的精细步进）。
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || !IsInteractable() || _readOnly)     // 源：hasCapture() 才处理
                return;
            ApplyPointer(eventData);
            eventData.Use();
        }

        /// <summary>源 <c>kMouseUpMessage</c>（slider.cpp:171-179）：释放 capture + SliderReleased。</summary>
        public override void OnPointerUp(PointerEventData eventData)
        {
            if (!_dragging)
                return;
            _dragging = false;
            base.OnPointerUp(eventData);                         // 源 setSelected(false) + releaseMouse
            OnSliderReleased();
        }

        void ApplyPointer(PointerEventData eventData)
        {
            RectInt rc = TrackRect();
            int value = ValueFromPointer(PointerX(eventData), rc.x, rc.width,
                _min, _max, _pressLeft, _pressX, _pressValue);
            if (_value != value)
            {
                UpdateValue(value);                              // 源 updateValue（虚）
                OnChange();                                      // 源 onChange → Change 信号
            }
        }

        /// <summary>鼠标在控件内的整格横坐标（源用 <c>positionForDisplay()</c> 的整型屏幕坐标，
        /// 减 rc.x 后即「相对槽左缘的格数」；本工程 rc.x = 0，故返回相对控件左缘的格数）。
        /// 取整用 floor：分界线落在光标所在那一格的左缘，不会跑到光标右边去。</summary>
        int PointerX(PointerEventData eventData)
        {
            RectTransform rect = Rect;
            Vector2 local;
            Camera cam = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, cam, out local))
                return 0;
            // ScreenPointToLocalPointInRectangle 的原点在 rect 的 pivot 上 → 换算到左缘
            float left = local.x + rect.rect.width * rect.pivot.x;
            return Mathf.FloorToInt(left);
        }

        // ================== 滚轮（slider.cpp:249-262） ==================

        /// <summary>源 <c>kMouseWheelMessage</c>：<c>value += wheelDelta.x - wheelDelta.y</c>（整数档位），
        /// 钳制后变了才 <c>setValue + onChange</c>（注意源此处走 setValue 而非 updateValue）。
        /// 符号：aseprite 在 Windows 上滚轮向上 <c>wheelDelta.y = -1</c>
        /// （state_with_wheel_behavior.cpp:285-290，与 macOS 反号），UGUI <c>scrollDelta.y</c>
        /// 向上为 <c>+1</c> ——代入源公式前先取负，即「滚轮向上 = 增值」（与 View 滚动同口径）。</summary>
        public void OnScroll(PointerEventData eventData)
        {
            if (!IsInteractable() || _readOnly)                  // 源：isEnabled() && !isReadOnly()
                return;

            int dx = Mathf.RoundToInt(eventData.scrollDelta.x);
            int dy = Mathf.RoundToInt(eventData.scrollDelta.y);
            int value = Mathf.Clamp(_value + dx + dy, _min, _max);   // = 源 x - (-dy)
            if (_value != value)
            {
                SetValue(value);                                 // 源 slider.cpp:257
                OnChange();
            }
            eventData.Use();
        }

        // ================== 键盘（slider.cpp:188-247） ==================

        string _keyBuffer = string.Empty;   // 源 m_keyBuffer
        int _keyTiming;                     // 源 m_keyTiming（ms 时基）

        /// <summary>源 <c>hasFocus()</c>——本工程映射到 EventSystem 的当前选中对象
        /// （源 setFocusStop(true)，点击/导航选中即「有焦点」；focused 件与文本 y=1 偏移跟它走）。</summary>
        public bool HasFocus
        {
            get
            {
                EventSystem es = EventSystem.current;
                return es != null && es.currentSelectedGameObject == gameObject;
            }
        }

        /// <summary>
        /// 源 <c>kKeyDownMessage</c>（slider.cpp:188-247）：仅 <c>hasFocus() &amp;&amp; !isReadOnly()</c> 时处理。
        /// ←/→ ±1；PageDown/PageUp ±(max-min+1)/4；Home/End → min/max；Backspace 按十进制退位；
        /// 其余按键进 1500ms 键入缓冲（可直接敲数字）。
        /// 【通道差异】源是 kKeyDownMessage 事件，本工程 UGUI 不派发这些键 → 在
        /// <see cref="Update"/> 里等价实现（登记为 subst，键位/公式/顺序逐条对齐）。
        /// </summary>
        void Update()
        {
            if (!HasFocus || !IsInteractable() || _readOnly)
                return;
            HandleKeyboard();
        }

        /// <summary>
        /// ←/→ 在源里是**取值键**（slider.cpp:200-201，kKeyDownMessage 消费掉 → 焦点不挪）。
        /// 这里把横向 Move 消费掉，免得 UGUI 导航同时把焦点搬走；取值本身在
        /// <see cref="Update"/> 按 <c>Input.GetKeyDown</c> 处理——UGUI 的 Move 事件走
        /// <c>Input.GetAxis</c> 平滑轴 + 模块级重复延迟，短按会被死区吃掉，与源「一次键按下 = 一步」
        /// 不等价。↑/↓ 源未消费（落到 Widget 默认处理 → 管理器挪焦点），故原样交给 base。
        /// </summary>
        public override void OnMove(AxisEventData eventData)
        {
            bool horizontal = eventData.moveDir == MoveDirection.Left
                || eventData.moveDir == MoveDirection.Right;
            if (horizontal && IsInteractable() && !_readOnly)
            {
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        void HandleKeyboard()
        {
            int min = _min, max = _max;
            int value = _value;
            int oldValue = value;

            if (NowMs() - _keyTiming > KeyBufferTimeoutMs)       // 源 slider.cpp:193-196
            {
                _keyBuffer = string.Empty;
                _keyTiming = NowMs();
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
                --value;
            else if (Input.GetKeyDown(KeyCode.RightArrow))
                ++value;
            else if (Input.GetKeyDown(KeyCode.PageDown))
                value -= (max - min + 1) / 4;                    // 源：(max - min + 1) / 4
            else if (Input.GetKeyDown(KeyCode.PageUp))
                value += (max - min + 1) / 4;
            else if (Input.GetKeyDown(KeyCode.Home))
                value = min;
            else if (Input.GetKeyDown(KeyCode.End))
                value = max;
            else if (Input.GetKeyDown(KeyCode.Backspace))
                value = BackspaceValue(value, min);

            if (oldValue == value)                               // 源：只有没被上面改掉才吃键入缓冲
            {
                if (AppendTypedChars())
                {
                    if (_keyBuffer == "-")                        // 源：允许输负号
                        return;
                    int parsed;
                    if (!int.TryParse(_keyBuffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        // 源 catch (...) { m_keyBuffer.pop_back(); goto not_used; } —— 回退并当作没处理
                        _keyBuffer = _keyBuffer.Substring(0, _keyBuffer.Length - 1);
                        return;
                    }
                    value = parsed;
                }
            }

            value = Mathf.Clamp(value, min, max);                // 源 slider.cpp:238
            if (oldValue != value)
            {
                _keyBuffer = value.ToString(CultureInfo.InvariantCulture);   // 源：缓冲同步成结果
                UpdateValue(value);
                OnChange();
            }
        }

        /// <summary>源 <c>kKeyBackspace</c>（slider.cpp:206-218）：按十进制字符串退回一位；
        /// 单位数或已到 min 时回 0（min &lt; 0 且当前为正）或 min。</summary>
        static int BackspaceValue(int value, int min)
        {
            string s = value.ToString(CultureInfo.InvariantCulture);
            if (s.Length == 1 || (value < 0 && s.Length == 2) || value == min)
                return (value > 0 && min < 0) ? 0 : min;
            return int.Parse(s.Substring(0, s.Length - 1), CultureInfo.InvariantCulture);
        }

        /// <summary>把本帧键入的数字/负号追加进键盘缓冲（源 slider.cpp:222：unicodeChar 入缓冲）。
        /// Unity 通道走 <c>Input.inputString</c>；非数字字符源里会被 stoi 拒绝并回退，净效果等同忽略。</summary>
        bool AppendTypedChars()
        {
            bool appended = false;
            string typed = Input.inputString;
            for (int i = 0; i < typed.Length; i++)
            {
                char c = typed[i];
                if ((c >= '0' && c <= '9') || c == '-')
                {
                    _keyBuffer += c;
                    appended = true;
                }
            }
            return appended;
        }

        static int NowMs()
        {
            return (int)(Time.realtimeSinceStartup * 1000f);
        }

        // ================== 绘制（skin_theme.cpp:1667-1794 paintSlider） ==================

        RectTransform _clipFull;        // 左段裁剪框：源 IntersectClip(rc.x, x-rc.x+1)
        RectTransform _clipRest;        // 右段裁剪框：源 IntersectClip(x+1, rc.width-(x-rc.x+1))
        Image _hitBox;                  // 命中盒（控件矩形全域可点 = 源 Widget::bounds）
        Image _emptyImage;              // 槽底件 slider_empty / slider_empty_focused
        Image _fillImage;               // 充满段件 slider_full / slider_full_focused（按整槽画、被裁剪）
        TextMeshProUGUI _lightLabel;    // slider_full_text #C0C0C0（压在充满段上）
        TextMeshProUGUI _darkLabel;     // slider_empty_text #202125（压在空槽段上）
        int _shownValue = int.MinValue;
        bool _warned;

        /// <summary>孩子件按名兜底重取（场景重载后私有字段不序列化——同
        /// <see cref="SketchButton"/> 的「文本写进去不显示」那类坑）。缺件只返回 false，
        /// 不在这里报错：工厂路径的 <c>AddComponent</c> 会先触发一次 <see cref="OnEnable"/>，
        /// 那一刻孩子还没建出来，就地报错是假红灯（见 <see cref="LateUpdate"/>）。
        /// </summary>
        bool Resolve()
        {
            if (_clipFull != null && _clipRest != null && _emptyImage != null
                && _fillImage != null && _lightLabel != null && _darkLabel != null)
                return true;

            Transform t = transform;
            if (_clipFull == null) _clipFull = t.Find("ClipFull") as RectTransform;
            if (_clipRest == null) _clipRest = t.Find("ClipRest") as RectTransform;
            if (_emptyImage == null) _emptyImage = FindPart(t, "Empty");
            if (_hitBox == null) _hitBox = FindPart(t, "HitBox");
            if (_fillImage == null) _fillImage = FindPart(t, "ClipFull/Fill");
            if (_lightLabel == null) _lightLabel = FindText(t, "ClipFull/ValueLight");
            if (_darkLabel == null) _darkLabel = FindText(t, "ClipRest/ValueDark");

            return _clipFull != null && _clipRest != null && _emptyImage != null
                && _fillImage != null && _lightLabel != null && _darkLabel != null;
        }

        /// <summary>缺件红灯（第一帧末，孩子件已建齐；装配只许走 <see cref="Create"/>）。</summary>
        void LateUpdate()
        {
            if (_warned)
                return;
            _warned = true;
            if (!Resolve())
                Debug.LogError("[SketchSlider] 件缺失（Empty/ClipFull/Fill/ValueLight/ClipRest/ValueDark）"
                    + "——滑条只许经 SketchSlider.Create 装配。");
        }

        static Image FindPart(Transform root, string path)
        {
            Transform child = root.Find(path);
            return child != null ? child.GetComponent<Image>() : null;
        }

        static TextMeshProUGUI FindText(Transform root, string path)
        {
            Transform child = root.Find(path);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        /// <summary>
        /// 【源 paintSlider 的 UGUI 复刻】三分支 + 双色裁剪：
        /// <code>
        /// if (value == min)      drawRect(rc, empty_part);                 // 整槽只画空槽件
        /// else if (value == max) drawRect(rc, full_part);                  // 整槽只画充满件
        /// else                   drawRect2(rc, x, full_part, empty_part);  // 两件都按整槽画，各裁一半
        /// </code>
        /// 关键：<c>drawRect2</c>（skin_theme.cpp:2024-2039）是「整槽九宫格 → 裁到 [rc.x, x] /
        /// [x+1, rc.x2]」，**不是**把件缩到半宽——否则切点会长出一个假的圆端盖。所以这里用
        /// RectMask2D 裁，件本身按整槽尺寸铺。
        /// </summary>
        public void Refresh()
        {
            if (!Resolve())
                return;

            RectInt rc = TrackRect();
            int x = SplitX();
            int clipFullW = x - rc.x + 1;                          // 源：x - rc.x + 1
            int clipRestX = x + 1;                                 // 源：x + 1
            int clipRestW = rc.width - clipFullW;                      // 源：rc.width - (x - rc.x + 1)

            // ---- 件：焦点换件（源 paintSlider:1716-1723；全尺寸滑条跟 hasFocus，不跟 mouse）----
            bool focused = HasFocus;
            _emptyImage.sprite = PixelSkin.SliderEmpty(focused);
            _fillImage.sprite = PixelSkin.SliderFull(focused);

            // ---- 值文案（源：widget->text 先设成 convertValueToText(value) 再量文本）----
            if (_shownValue != _value)
            {
                _shownValue = _value;
                string text = ConvertValueToText(_value);
                _lightLabel.SetText(text);
                _darkLabel.SetText(text);
            }

            // ---- 三分支：value == max 不画空槽件；value == min 不画充满件 ----
            _emptyImage.enabled = _value != _max;
            _fillImage.enabled = _value != _min;

            // ---- 左段：Rect(rc.x, rc.y, clipFullW, rc.h)，件按整槽画 ----
            _clipFull.gameObject.SetActive(clipFullW > 0);
            SetStretchBox(_clipFull, rc.x, rc.x + clipFullW - rc.width);
            SetStretchBox(_fillImage.rectTransform, 0, rc.width - clipFullW);

            // ---- 右段：Rect(x+1, rc.y, clipRestW, rc.h)；宽 <= 0 时源 IntersectClip 直接跳过
            //      （值 = max 时 clipRestW = -1） ----
            _clipRest.gameObject.SetActive(clipRestW > 0);
            SetStretchBox(_clipRest, clipRestX, clipRestW - rc.width + clipRestX);   // 宽 = rc.width - clipRestX + 右内缩

            // ---- 文本：源 SkinTheme::drawText:1863-1866 的**整数**居中 ----
            int textW = Mathf.RoundToInt(_lightLabel.preferredWidth);
            int textX = rc.x + rc.width / 2 - textW / 2;               // 源：rc.center().x - textW/2
            int focusDy = focused ? -1 : 0;                        // theme.xml:1057 focus 文本层 y="1"
            SetLabelBox(_lightLabel.rectTransform, textW, textX - rc.x, focusDy);
            SetLabelBox(_darkLabel.rectTransform, textW, textX - clipRestX, focusDy);
        }

        /// <summary>矩形 children 的横向 stretch 盒（左内缩 + 右内缩，父 rect 宽为 rc.width）。</summary>
        static void SetStretchBox(RectTransform rect, int leftInset, int rightInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(leftInset, 0f);
            rect.offsetMax = new Vector2(rightInset, 0f);
        }

        /// <summary>文本盒：宽 = 文本整数实宽、左缘落在 <paramref name="left"/>（相对裁剪框左缘），
        /// 高随裁剪框、垂直居中 + 焦点时下移 1 格（源的 textrc 高 = 行高、y = rc 中心 - 行高/2；
        /// 这里用等高盒 + 垂直居中，等价到 TMP 内部行盒居中的取整）。</summary>
        static void SetLabelBox(RectTransform rect, int textW, int left, int downBy)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(textW, 0f);
            rect.anchoredPosition = new Vector2(left, -downBy);
        }

        // ================== 生命周期 ==================

        protected override void OnEnable()
        {
            base.OnEnable();          // UGUI 状态机 / Selectable 登记（必须走 base，同 SketchCheck 的坑）
            _shownValue = int.MinValue;
            _rect = (RectTransform)transform;
            Refresh();
        }

        protected override void OnDisable()
        {
            _dragging = false;
            _keyBuffer = string.Empty;
            base.OnDisable();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (isActiveAndEnabled)
                Refresh();
        }

        /// <summary>焦点变化 → 换 *_focused 件 + 文本 y=1（源 kFocusEnter/LeaveMessage → invalidate，
        /// paintSlider 按 hasFocus 选件）。</summary>
        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            Refresh();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            Refresh();
        }

        void OnValidate()
        {
            // Inspector 里改值域时先纠偏（源 enforceValidRange 的编辑器侧对应物），
            // 不在这里重绘——装配期孩子还没建出来。
            if (_min > _max)
                _max = _min;
            _value = Mathf.Clamp(_value, _min, _max);
        }

        // ================== 静态工厂（同 SketchButton / SketchCheck 风格） ==================

        /// <summary>
        /// 建一枚 Aseprite 滑条：命中盒 + 槽底件 + 左右两段裁剪框（各带一件/一枚文本）。
        /// 尺寸请按 <see cref="PartHeight"/>（16）给——槽件是 16×16 九宫格。
        /// </summary>
        /// <param name="min">源 Slider(min, max, value) 的 min。</param>
        /// <param name="max">源 min..max（min &gt; max 时源会把 max 拉到 min，slider.cpp:49-58）。</param>
        /// <param name="value">初值（会钳进值域）。</param>
        /// <param name="valueToText">源 SliderDelegate::onGetTextFromValue；缺省 "%d"（不带百分号）。</param>
        public static SketchSlider Create(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            int min, int max, int value, TMP_FontAsset font, float fontSize,
            Func<int, string> valueToText = null)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var slider = rect.gameObject.AddComponent<SketchSlider>();
            slider._rect = rect;
            slider.ValueToText = valueToText;
            // theme slider 无悬停档、无按压档（<style id="slider"> 只有常态与 focus），
            // 状态反馈只换件；ColorTint 会把 0.96/0.78 的灰乘上直切件（破坏调色板）→ 关掉。
            slider.transition = Transition.None;

            // 命中盒：源命中面 = Widget::bounds（整控件矩形），件本身不挡射线。
            // 透明但可射线（Graphics 不按 alpha 命中，除非开了 alphaHitTestMinimumThreshold）。
            Image hit = UiKit.CreateRect("HitBox", rect).gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            UiKit.Stretch(hit.rectTransform);
            slider._hitBox = hit;
            slider.targetGraphic = hit;

            // 槽底件（theme background 层 = slider_empty；paintSlider 只画 rc 内的分区件，
            // 空槽件同时充当右段底色）
            Image empty = UiKit.CreateRect("Empty", rect).gameObject.AddComponent<Image>();
            empty.type = Image.Type.Sliced;
            empty.pixelsPerUnitMultiplier = 1f;      // ×1 终局：贴图纹素 = 画布像素
            empty.color = Color.white;               // 像素件禁止乘色
            empty.raycastTarget = false;
            UiKit.Stretch(empty.rectTransform);
            slider._emptyImage = empty;

            // 左段：裁剪框（源 IntersectClip）+ 件按整槽铺 + 浅字
            RectTransform clipFull = UiKit.CreateRect("ClipFull", rect);
            clipFull.gameObject.AddComponent<RectMask2D>();
            slider._clipFull = clipFull;

            Image fill = UiKit.CreateRect("Fill", clipFull).gameObject.AddComponent<Image>();
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = 1f;
            fill.color = Color.white;
            fill.raycastTarget = false;
            UiKit.Stretch(fill.rectTransform);
            slider._fillImage = fill;
            slider._lightLabel = CreateValueLabel(clipFull, "ValueLight",
                PixelSkin.Theme.Text, font, fontSize);            // slider_full_text #c0c0c0

            // 右段：裁剪框 + 深字
            RectTransform clipRest = UiKit.CreateRect("ClipRest", rect);
            clipRest.gameObject.AddComponent<RectMask2D>();
            slider._clipRest = clipRest;
            slider._darkLabel = CreateValueLabel(clipRest, "ValueDark",
                PixelSkin.Theme.Disabled, font, fontSize);        // slider_empty_text #202125

            slider.EnforceValidRange(min, max);
            slider._value = Mathf.Clamp(value, min, max);
            slider.Refresh();
            return slider;
        }

        /// <summary>槽内值文本（theme slider 的 text 层：居中、8px 位图字）。
        /// 两枚同文案标签只有字色不同——源 paintSlider 把同一句画两遍、逐像素换色。</summary>
        static TextMeshProUGUI CreateValueLabel(Transform parent, string name, Color color,
            TMP_FontAsset font, float fontSize)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset resolved = UiKit.ResolvePixelFont(Mathf.RoundToInt(fontSize), font);
            if (resolved != null)
                label.font = resolved;
            else if (font != null)
                label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Normal;         // 位图字禁伪粗
            label.enableWordWrapping = false;            // 源 drawText 单行
            label.overflowMode = TextOverflowModes.Overflow;
            // theme align="center middle"。水平居中**不用** TMP 的 Center（浮点居中会让整列字形
            // 压在半格上——交接档量化过的 392 条奇数屏像素就出在这里），而是照源
            // SkinTheme::drawText 的整数除法算左缘（Refresh 里 SetLabelBox），故这里取 Left。
            label.alignment = TextAlignmentOptions.Left;
            label.margin = Vector4.zero;
            label.color = color;
            label.raycastTarget = false;
            label.gameObject.AddComponent<PixelSnapText>();
            PixelAtlasPointFilter.Ensure(label.font);
            return label;
        }
    }
}
