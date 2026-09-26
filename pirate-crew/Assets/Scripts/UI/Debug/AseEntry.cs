using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite <c>ui::Entry</c>（<c>external/aseprite-ref/src/ui/entry.cpp</c> +
    /// <c>entry.h</c>）**编辑路径的逐函数移植**，替代「TMP_InputField 自带编辑语义」
    /// 那套近似实现（<see cref="AseWidgetKit.SunkenEntry"/> 曾经件）。
    ///
    /// 【权威源】
    ///   · <c>entry.h:24-154</c>（Range / 状态字段 m_caret·m_scroll·m_select·m_hidden·
    ///     m_state·m_readonly·m_recent_focused·m_lock_selection·m_persist_selection /
    ///     CharBox / 虚函数面）
    ///   · <c>entry.cpp:46-49</c>（is_word_char）
    ///   · <c>entry.cpp:109-224</c>（showCaret/hideCaret/lastCaretPos/setCaretPos/
    ///     setCaretToEnd/selectText/selectAllText/deselectText/selectedText/selectedRange/
    ///     setSuffix/getSuffix）
    ///   · <c>entry.cpp:272-452</c>（onProcessMessage：timer/focus enter·leave/key down/
    ///     mouse down·move·up/double click/enter·leave）
    ///   · <c>entry.cpp:528-578</c>（onChange / onGetEntryTextBounds / getCaretFromMouse）
    ///   · <c>entry.cpp:580-792</c>（onExecuteCmd 全部 Cmd 分支）
    ///   · <c>entry.cpp:794-942</c>（forwardWord/backwardWord/wordRange/isPosInSelection/
    ///     recalcCharBoxes/deleteRange）
    ///   · <c>entry.cpp:944-958</c>（startTimer/stopTimer：500ms 光标闪烁）
    ///   · <c>textcmd.cpp:21-99</c>（cmdFromKeyMessage 键位→命令映射）
    ///   · <c>skin_theme.cpp:1299-1315</c>（getCaretSize：位图字光标 w=2·guiscale、
    ///     h=textHeight+2·guiscale）
    ///   · <c>skin_theme.cpp:1357-1549</c>（paintEntry/drawEntryText/DrawEntryTextDelegate：
    ///     sunken 底件 / 文本 / 选区底色 / 光标）
    ///   · <c>skin_theme.cpp:1915-1923</c>（drawEntryCaret）
    ///   · <c>theme.xml:1059-1063</c>（style textedit：textbox_face 底、selected 选区、
    ///     textbox_text/selected_text 两色）
    ///
    /// 【为什么不走 TMP_InputField】TMP 自带 caret/clipboard/selection 语义，与源的三条
    /// 核心口径都对不上：①源的字符盒 m_boxes 是**单一几何源**，光标 x、选区范围、鼠标命中
    /// 三路同取它（entry.cpp:263-270/543-578/903-931）；②源的 setText/executeCmd 状态机
    /// 是显式 caret/select/scroll 三整数（entry.cpp:580-792），不发 TMP 的 onValueChanged；
    /// ③源的选区绘制逐字符换 fg/bg（skin_theme.cpp:1412-1446）。故本控件**自建全部编辑
    /// 语义**，显示只用只读 TMP 标签 + 自绘选区/光标 quad。
    ///
    /// 【对外契约】<see cref="AseWidgetKit.SunkenEntry"/> 仍需返回 <c>TMP_InputField</c>
    /// （调用方 AseDialogLoader 注册 id、DebugMenuHost 读写 <c>.text</c>）。该件在根上以
    /// **禁用态**挂一个 <c>TMP_InputField</c> 纯作外向文本模型：程序写 <c>.text</c> 经
    /// onValueChanged/轮询被本控件收养；本控件编辑后回写 <c>.text</c>。参数映射与偏差
    /// 见 <see cref="AseWidgetKit.SunkenEntry"/> 注释与交付报告。
    ///
    /// 【登记偏差】①IME/中文候选窗/dead-key 不在源范围（Aseprite 英文键盘），
    /// <c>setTextInput</c>/dead-key 支路未移植；②源的空闲计时器全员共享（entry.cpp:44），
    /// 本控件每实例独立，闪烁相位不共享；③右键 <c>showEditPopupMenu</c>（Cut/Copy/Paste
    /// 菜单，textcmd.cpp:108-144）未移植，仅置 m_lock_selection。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class AseEntry : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        // ------------------------------------------------------------------
        // 源数值常量
        // ------------------------------------------------------------------

        /// <summary>光标宽 = getCaretSize().w = 2·guiscale（skin_theme.cpp:1312）。</summary>
        public const int CaretWidthPx = 2;

        /// <summary>光标闪烁周期 500ms（entry.cpp:948 Timer(500, this)）。</summary>
        public const float CaretBlinkSeconds = 0.5f;

        /// <summary>entry.cpp:41 kMaxWidthHintForEntry 的替身（本端布局由装载器算，仅备查）。</summary>
        public const int MaxWidthHintForEntry = 400;

        // 颜色：theme.xml 实值（skin_theme.cpp paintEntry/DrawEntryTextDelegate 取色处）
        static readonly Color32 ColText = PixelSkin.Theme.Text;                 // textbox_text  #c0c0c0
        static readonly Color32 ColSelected = PixelSkin.Theme.Selected;         // selected      #e1b85f
        static readonly Color32 ColSelectedText = PixelSkin.Theme.SelectedText; // selected_text #41444a
        /// <summary>entry_suffix theme.xml:48 #c6c6c6（源 colors.entrySuffix()，skin_theme.cpp:1427）。</summary>
        static readonly Color32 ColSuffix = new Color32(0xC6, 0xC6, 0xC6, 0xFF);

        // ------------------------------------------------------------------
        // 源 entry.h:123-151 的状态字段
        // ------------------------------------------------------------------

        /// <summary>源 <c>Entry::Range</c>（entry.h:25-32）。</summary>
        public struct EntryRange
        {
            public int from;
            public int to;
            public EntryRange(int from, int to) { this.from = from; this.to = to; }
            public bool IsEmpty => from < 0;
            public int Size => to - from;
            public void Reset() { from = to = -1; }
        }

        /// <summary>源 <c>Entry::CharBox</c>（entry.h:123-129）：单字符盒
        /// （from/to = 文本下标，x/width = 文本坐标系像素）。</summary>
        struct CharBox
        {
            public int codepoint;
            public int from;
            public int to;
            public float x;
            public float width;
        }

        readonly List<CharBox> _boxes = new List<CharBox>();

        [SerializeField] int _maxsize = 6;
        [SerializeField] int _caret;
        [SerializeField] int _scroll;
        [SerializeField] int _select;
        bool _hidden;                // m_hidden
        bool _state;                 // m_state：光标当前是否可见（闪烁）
        bool _readonly;              // m_readonly
        bool _recentFocused;         // m_recent_focused
        bool _lockSelection;         // m_lock_selection
        bool _persistSelection;      // m_persist_selection
        bool _translateDeadKeys = true; // m_translate_dead_keys（IME 未移植）
        EntryRange _selectingWords;  // m_selecting_words（双击选词拖动续选）
        string _suffix;              // m_suffix
        string _text = string.Empty;
        string _placeholder = string.Empty;

        /// <summary>源 m_scale（默认 1,1）。本端布局 1:1，保留面供直接换算（entry.cpp:157/558）。</summary>
        public Vector2 Scale { get; set; } = Vector2.one;

        /// <summary>源 <c>obs::signal&lt;void()&gt; Change</c>（entry.h:81）：文本被编辑路径改动时发。</summary>
        public event Action Change;

        // ------------------------------------------------------------------
        // 视觉件（自建：sunken 底 + 文本区 + 选区 quad + 暗色选区字 + 光标 quad）
        // ------------------------------------------------------------------

        RectTransform _textArea;
        Image _face;
        Image _selection;            // 选区高亮 quad（cols.selected()）
        TextMeshProUGUI _lightLabel; // 常态字（cols.text()）
        RectTransform _darkClip;     // 选区裁剪框（RectMask2D）
        TextMeshProUGUI _darkLabel;  // 选区字（cols.selectedText()），被 _darkClip 裁到选区范围
        Image _caretQuad;            // 光标 quad
        TextMeshProUGUI _suffixLabel;
        bool _resolved;

        float _blinkTimer;

        // 鼠标交互
        bool _dragging;

        // 外向文本模型（禁用态 TMP_InputField）
        TMP_InputField _field;
        bool _syncingField;

        int _fontSize = UiSkin.Font.Tiny;

        // ------------------------------------------------------------------
        // 源 entry.h:39-78 公共面
        // ------------------------------------------------------------------

        public bool IsReadOnly => _readonly;
        public void SetReadOnly(bool state) { _readonly = state; }

        /// <summary>源 <c>caretPos()</c>。</summary>
        public int CaretPos => _caret;

        /// <summary>源 <c>isCaretVisible()</c>（entry.h:51）：!m_hidden &amp;&amp; m_state。</summary>
        public bool IsCaretVisible => !_hidden && _state;

        /// <summary>源 <c>text()/setText()</c>。setText 不触发 <see cref="Change"/>（源同）。</summary>
        public string Text
        {
            get => _text;
            set => SetText(value);
        }

        public void SetMaxTextLength(int maxsize) { _maxsize = Mathf.Max(0, maxsize); }

        public void SetSuffix(string suffix)
        {
            _suffix = suffix;
            if (_suffixLabel != null)
            {
                _suffixLabel.SetText(suffix ?? string.Empty);
                _suffixLabel.gameObject.SetActive(!string.IsNullOrEmpty(suffix));
            }
        }

        public string GetSuffix() => string.IsNullOrEmpty(_suffix) ? string.Empty : _suffix;

        public void SetTranslateDeadKeys(bool state) { _translateDeadKeys = state; }

        /// <summary>源 <c>m_translate_dead_keys</c>（entry.h:144）；IME 未移植，仅保留状态面。</summary>
        public bool TranslateDeadKeys => _translateDeadKeys;

        public void SetPlaceholder(string placeholder) { _placeholder = placeholder ?? string.Empty; Refresh(); }
        public string Placeholder => _placeholder;

        /// <summary>源 <c>setPersistSelection</c>（entry.h:61）。</summary>
        public void SetPersistSelection(bool state) { _persistSelection = state; }

        // ==================================================================
        // 装配
        // ==================================================================

        /// <summary>
        /// 在**本组件所在根**下建齐视觉件并绑定外向文本模型。
        /// 根上须已有 <see cref="Image"/>（sunken 底，raycastTarget）——由
        /// <see cref="AseWidgetKit.SunkenEntry"/> 保证。
        /// </summary>
        public void Build(TMP_InputField model, string initialText, string suffix, int maxsize)
        {
            _field = model;
            _maxsize = maxsize;
            _suffix = suffix;
            _text = initialText ?? string.Empty;
            // 源 ctor（entry.cpp:54-56）：m_caret/m_scroll/m_select 初始 0
            _caret = 0;
            _scroll = 0;
            _select = 0;
            _selectingWords = new EntryRange(-1, -1);

            RectTransform root = (RectTransform)transform;
            _face = GetComponent<Image>();

            // 文本区：源 getEntryTextBounds = clientBounds 内缩 border（左右 4 / 上下 1，
            // 与旧 SunkenEntry 的 textarea 盒一致；即 theme textedit border=2 + 内衬）。
            _textArea = UiKit.CreateRect("TextArea", root);
            _textArea.anchorMin = Vector2.zero;
            _textArea.anchorMax = Vector2.one;
            _textArea.pivot = new Vector2(0.5f, 0.5f);
            _textArea.offsetMin = new Vector2(4f, 1f);
            _textArea.offsetMax = new Vector2(string.IsNullOrEmpty(suffix) ? -4f : -16f, -1f);
            _textArea.gameObject.AddComponent<RectMask2D>();   // 源 drawEntryText 的 IntersectClip(bounds)

            _selection = CreateQuad("Selection", _textArea, ColSelected);
            _lightLabel = CreateLabel("Text", _textArea, ColText);

            _darkClip = UiKit.CreateRect("SelectionClip", _textArea);
            _darkClip.gameObject.AddComponent<RectMask2D>();
            _darkLabel = CreateLabel("TextDark", _darkClip, ColSelectedText);

            _caretQuad = CreateQuad("Caret", _textArea, ColText);

            if (!string.IsNullOrEmpty(suffix))
            {
                // 后缀（源 colors.entrySuffix() 色；旧实现错用了 status_bar_text，这里改回
                // theme.xml:48 的 entry_suffix #c6c6c6）。锚右缘——源是紧接正文追加，
                // 本端沿用既有右锚语义（公共观感不变）。
                _suffixLabel = DebugWindowKit.PlaceLabel(root, suffix, _fontSize,
                    ColSuffix, 0f, 2f, 10f);
                RectTransform sr = _suffixLabel.rectTransform;
                sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(1f, 1f);
                sr.anchoredPosition = new Vector2(-3f, -2f);
                _suffixLabel.raycastTarget = false;
            }

            // 外向模型：订阅 onValueChanged（程序写 .text 立即收养），Build 后再写初值。
            if (_field != null)
                _field.onValueChanged.AddListener(AdoptExternalText);
            SetText(_text);

            _resolved = true;
            Refresh();
        }

        static Image CreateQuad(string name, Transform parent, Color32 color)
        {
            Image img = UiKit.CreateRect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        TextMeshProUGUI CreateLabel(string name, Transform parent, Color32 color)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UiKit.ResolvePixelFont(_fontSize, DebugWindowKit.HandFont);
            if (font != null)
                label.font = font;
            label.fontSize = _fontSize;
            label.fontStyle = FontStyles.Normal;          // 位图字禁伪粗
            label.enableWordWrapping = false;             // 源单行
            label.overflowMode = TextOverflowModes.Overflow;
            // 源 SkinTheme::drawText 整数居中口径：本端文本区按左对齐 + 垂直 Middle 摆放，
            // 具体 x 由 m_boxes 决定（选区/光标同取它）。
            label.alignment = TextAlignmentOptions.Left;
            label.margin = Vector4.zero;
            label.richText = false;                       // 选区换色走暗色覆层，不用富文本
            label.color = color;
            label.raycastTarget = false;
            label.gameObject.AddComponent<PixelSnapText>();
            PixelAtlasPointFilter.Ensure(label.font);
            return label;
        }

        /// <summary>件缺失兜底重取（装配只许走 <see cref="Build"/>；同名递归查找同
        /// <c>SketchSlider.Resolve</c>）。</summary>
        bool Resolve()
        {
            if (_resolved)
                return true;
            Transform t = transform;
            if (_textArea == null) _textArea = t.Find("TextArea") as RectTransform;
            if (_textArea == null) return false;
            if (_selection == null) _selection = FindImage(t, "TextArea/Selection");
            if (_lightLabel == null) _lightLabel = FindText(t, "TextArea/Text");
            if (_darkClip == null) _darkClip = t.Find("TextArea/SelectionClip") as RectTransform;
            if (_darkLabel == null) _darkLabel = FindText(t, "TextArea/SelectionClip/TextDark");
            if (_caretQuad == null) _caretQuad = FindImage(t, "TextArea/Caret");
            _resolved = _textArea != null && _lightLabel != null;
            return _resolved;
        }

        static Image FindImage(Transform root, string path)
        {
            Transform child = root.Find(path);
            return child != null ? child.GetComponent<Image>() : null;
        }

        static TextMeshProUGUI FindText(Transform root, string path)
        {
            Transform child = root.Find(path);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        // ==================================================================
        // 文本与字符盒（源 entry.cpp:203-224 / 506-514 / 903-931）
        // ==================================================================

        public void SetText(string value)
        {
            _text = value ?? string.Empty;
            RecalcCharBoxes(_text);

            // 源 onSetText（entry.cpp:506-514）：caret 越界回夹
            int textlen = LastCaretPos();
            if (_caret > textlen) _caret = textlen;
            if (_caret < 0) _caret = 0;
            _scroll = Mathf.Clamp(_scroll, 0, textlen);

            SyncToField();
            Refresh();
        }

        /// <summary>源 <c>lastCaretPos()</c>（entry.cpp:124-127）= boxes.size()-1。</summary>
        public int LastCaretPos() => _boxes.Count - 1;

        /// <summary>
        /// 源 <c>recalcCharBoxes()</c>（entry.cpp:903-931）：重建每个字符的盒 + 末尾
        /// 哨兵盒（宽 = 光标宽）。源用 text shaper 的 charBounds；本端取 TMP 的
        /// characterInfo.xAdvance 累加，语义同为「标量游标位置 + 前进宽」。
        /// </summary>
        void RecalcCharBoxes(string text)
        {
            _boxes.Clear();
            float x = 0f;
            if (_lightLabel != null)
            {
                _lightLabel.text = text;
                _lightLabel.ForceMeshUpdate();
                TMP_TextInfo info = _lightLabel.textInfo;
                int n = text.Length;
                if (!string.IsNullOrEmpty(text) && info != null && info.characterCount >= n)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float adv = info.characterInfo[i].xAdvance;
                        if (adv <= 0f) adv = _fontSize;      // 不可见/零宽字符兜底
                        _boxes.Add(new CharBox
                        {
                            codepoint = text[i],
                            from = i,
                            to = i + 1,
                            x = x,
                            width = adv,
                        });
                        x += adv;
                    }
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    // TMP 信息缺失（字体没烘/rich 索引错位）时的均匀兜底，避免空盒。
                    for (int i = 0; i < n; i++)
                    {
                        _boxes.Add(new CharBox
                        {
                            codepoint = text[i], from = i, to = i + 1,
                            x = x, width = _fontSize,
                        });
                        x += _fontSize;
                    }
                }
            }

            // 末位哨兵盒（entry.cpp:924-930）
            _boxes.Add(new CharBox
            {
                codepoint = 0,
                from = text.Length,
                to = text.Length,
                x = x,
                width = CaretWidthPx,
            });
        }

        int CodeAt(int i)
        {
            if (i < 0 || i >= _boxes.Count) return 0;
            return _boxes[i].codepoint;
        }

        /// <summary>源 <c>is_word_char()</c>（entry.cpp:46-49）：非空白、非标点符号。</summary>
        protected static bool IsWordChar(int ch)
        {
            if (ch == 0) return false;
            char c = (char)ch;
            return !char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c);
        }

        // ==================================================================
        // caret / selection（源 entry.cpp:109-224）
        // ==================================================================

        /// <summary>源 <c>setCaretPos()</c>（entry.cpp:138-173）：夹取 + 双向滚动 + 起表。</summary>
        public void SetCaretPos(int pos)
        {
            int textlen = LastCaretPos();
            _caret = Mathf.Clamp(pos, 0, textlen);
            _scroll = Mathf.Clamp(_scroll, 0, textlen);

            if (_caret < _scroll)
            {
                _scroll = _caret;                                   // 向后滚
            }
            else if (_caret > _scroll)
            {
                int xLimit = TextBoundsWidth;                       // 源 bounds.x2()
                while (_caret > _scroll)
                {
                    float visible = _boxes[_caret].x - _boxes[_scroll].x;
                    int x = Mathf.RoundToInt(visible * Scale.x) + CaretWidthPx;
                    if (x < xLimit) break;
                    ++_scroll;                                      // 向前滚
                }
            }

            if (ShouldStartTimer(HasFocus))
                StartTimer();
            _state = true;
            Refresh();
        }

        public void SetCaretToEnd()
        {
            int end = LastCaretPos();
            SelectText(end, end);
        }

        /// <summary>源 <c>selectText()</c>（entry.cpp:181-190）：to&lt;0 表示「到末尾」。</summary>
        public void SelectText(int from, int to)
        {
            int end = LastCaretPos();
            _select = from;
            SetCaretPos(from);
            SetCaretPos((to >= 0) ? to : end + to + 1);
            Refresh();
        }

        public void SelectAllText() { SelectText(0, -1); }

        public void DeselectText() { _select = -1; Refresh(); }

        /// <summary>源 <c>selectedRange()</c>（entry.cpp:213-224）。</summary>
        public EntryRange SelectedRange()
        {
            EntryRange r = new EntryRange(-1, -1);
            if (_select >= 0 && _caret != _select)
            {
                r.from = Mathf.Min(_caret, _select);
                r.to = Mathf.Max(_caret, _select);
                r.from = Mathf.Clamp(r.from, 0, Mathf.Max(0, _boxes.Count - 1));
                r.to = Mathf.Clamp(r.to, 0, _boxes.Count);
            }
            return r;
        }

        /// <summary>源 <c>selectedText()</c>（entry.cpp:203-211）。</summary>
        public string SelectedText()
        {
            EntryRange r = SelectedRange();
            if (!r.IsEmpty)
                return _text.Substring(_boxes[r.from].from, _boxes[r.to - 1].to - _boxes[r.from].from);
            return string.Empty;
        }

        public void ShowCaret()
        {
            _hidden = false;
            if (ShouldStartTimer(HasFocus)) StartTimer();
            Refresh();
        }

        public void HideCaret()
        {
            _hidden = true;
            StopTimer();
            Refresh();
        }

        /// <summary>源 <c>getEntryThemeInfo()</c>（entry.cpp:246-256）。</summary>
        public void GetEntryThemeInfo(out int scroll, out int caret, out bool caretVisible, out EntryRange range)
        {
            scroll = _scroll;
            caret = _caret;
            caretVisible = !_hidden && _state;
            range = SelectedRange();
        }

        /// <summary>源 <c>nodeTextBounds</c> 替身：文本区（clientBounds 内缩 border）。</summary>
        public Rect GetEntryTextBounds()
        {
            if (_textArea == null) return new Rect(0f, 0f, 0f, TextHeight);
            int th = TextHeight;
            float h = _textArea.rect.height;
            return new Rect(0f, Mathf.FloorToInt((h - th) / 2f), TextBoundsWidth, th);
        }

        int TextBoundsWidth => _textArea != null ? Mathf.RoundToInt(_textArea.rect.width) : 0;

        /// <summary>源 <c>textHeight()</c> = 字体行高（单行）。</summary>
        public int TextHeight
        {
            get
            {
                if (_lightLabel != null && _lightLabel.font != null)
                {
                    var fi = _lightLabel.font.faceInfo;
                    float ps = fi.pointSize > 0.0001f ? fi.pointSize : _lightLabel.fontSize;
                    float h = fi.lineHeight * (_lightLabel.fontSize / ps);
                    if (h > 0.1f) return Mathf.Max(1, Mathf.RoundToInt(h));
                }
                return Mathf.Max(1, _fontSize);
            }
        }

        /// <summary>源 getCaretSize()（skin_theme.cpp:1311-1314）位图字分支：h = textHeight + 2。</summary>
        public int CaretHeight => TextHeight + 2;   // 2*guiscale，本端 guiscale=1

        // ==================================================================
        // 时序（源 entry.cpp:933-958）
        // ==================================================================

        bool ShouldStartTimer(bool hasFocus) => !_hidden && hasFocus && IsEnabled;
        void StartTimer() { _blinkTimer = 0f; }
        void StopTimer() { _blinkTimer = 0f; }

        protected bool IsEnabled => isActiveAndEnabled;

        /// <summary>源 <c>hasFocus()</c> → EventSystem 当前选中项（同 SketchSlider.HasFocus）。</summary>
        public bool HasFocus
        {
            get
            {
                EventSystem es = EventSystem.current;
                return es != null && es.currentSelectedGameObject == gameObject;
            }
        }

        // ==================================================================
        // 生命周期
        // ==================================================================

        void Update()
        {
            if (!Resolve())
                return;

            SyncFromField();

            if (!IsEnabled)
                return;

            bool focus = HasFocus;

            // 源 kKeyDownMessage（entry.cpp:320-354）：仅 hasFocus && !isReadOnly
            if (focus && !_readonly)
                HandleKeyboard();

            // 源 kTimerMessage（entry.cpp:275-281）+ startTimer：500ms 翻转 m_state
            if (focus && ShouldStartTimer(true))
            {
                _blinkTimer += Time.unscaledDeltaTime;
                if (_blinkTimer >= CaretBlinkSeconds)
                {
                    _blinkTimer -= CaretBlinkSeconds;
                    _state = !_state;
                    Refresh();
                }
            }
        }

        void OnRectTransformDimensionsChange()
        {
            if (_resolved)
                Refresh();
        }

        // ==================================================================
        // 键盘（源 onProcessMessage kKeyDown + textcmd.cpp cmdFromKeyMessage）
        // ==================================================================

        /// <summary>
        /// 源 <c>TextCmdProcessor::Cmd</c>（textcmd.h:21-43）。
        /// </summary>
        protected enum Cmd
        {
            NoOp, InsertChar,
            PrevChar, PrevWord, PrevLine,
            NextChar, NextWord, NextLine,
            BegOfLine, EndOfLine, BegOfFile, EndOfFile,
            DeletePrevChar, DeleteNextChar, DeletePrevWord, DeleteNextWord,
            DeleteToEndOfLine, Cut, Copy, Paste, SelectAll,
        }

        static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /// <summary>
        /// 源 <c>cmdFromKeyMessage()</c>（textcmd.cpp:21-99）逐条移植。
        /// 【通道差异，登记 subst】源是 kKeyDownMessage 事件；UGUI 不派发这些键，
        /// 故在 Update 里以 Input.GetKeyDown 等价实现（同 SketchSlider.HandleKeyboard）。
        /// Left/Right 的「cmdPressed → Beg/EndOfLine」（macOS Cmd/Win 键）未映射（无 Win 键分支）。
        /// </summary>
        Cmd CmdFromKeyMessage()
        {
            bool ctrl = CtrlHeld, alt = AltHeld, shift = ShiftHeld;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
                return (ctrl || alt) ? Cmd.PrevWord : Cmd.PrevChar;
            if (Input.GetKeyDown(KeyCode.RightArrow))
                return (ctrl || alt) ? Cmd.NextWord : Cmd.NextChar;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                return Cmd.PrevLine;
            if (Input.GetKeyDown(KeyCode.DownArrow))
                return Cmd.NextLine;
            if (Input.GetKeyDown(KeyCode.Home))
                return ctrl ? Cmd.BegOfFile : Cmd.BegOfLine;
            if (Input.GetKeyDown(KeyCode.End))
                return ctrl ? Cmd.EndOfFile : Cmd.EndOfLine;
            if (Input.GetKeyDown(KeyCode.Delete))
            {
                if (shift)
                {
                    if (ctrl) return Cmd.DeleteToEndOfLine;
                    if (HasValidSelection()) return Cmd.Cut;
                }
                if (ctrl) return Cmd.DeleteNextWord;
                return Cmd.DeleteNextChar;
            }
            if (Input.GetKeyDown(KeyCode.Insert))
            {
                if (shift) return Cmd.Paste;
                if (ctrl) return Cmd.Copy;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
                return (ctrl || alt) ? Cmd.DeletePrevWord : Cmd.DeletePrevChar;

            // onlyCtrlPressed()（message.h:58 = 仅 Ctrl，无 shift/alt/cmd）
            if (ctrl && !alt && !shift)
            {
                if (Input.GetKeyDown(KeyCode.X)) return Cmd.Cut;
                if (Input.GetKeyDown(KeyCode.C)) return Cmd.Copy;
                if (Input.GetKeyDown(KeyCode.V)) return Cmd.Paste;
                if (Input.GetKeyDown(KeyCode.A)) return Cmd.SelectAll;
            }
            return Cmd.NoOp;
        }

        void HandleKeyboard()
        {
            Cmd cmd = CmdFromKeyMessage();

            // 源：Ignore Up/Down（焦点在控件间移动用），break 不消费（entry.cpp:328-329）
            if (cmd == Cmd.PrevLine || cmd == Cmd.NextLine)
                return;

            if (cmd != Cmd.NoOp)
            {
                ExecuteCmd(cmd, 0, ShiftHeld);   // 源：msg->shiftPressed()
                return;
            }

            // 源：unicodeChar >= 32 → InsertChar（entry.cpp:336-345）
            InsertTypedChars();
        }

        void InsertTypedChars()
        {
            string typed = Input.inputString;
            if (string.IsNullOrEmpty(typed))
                return;
            bool ctrl = CtrlHeld;
            for (int i = 0; i < typed.Length; i++)
            {
                char c = typed[i];
                if (c < 32 || c == 127) continue;   // 控制字符（Backspace/Enter/Tab/Ctrl 组合）
                if (ctrl) continue;                 // Ctrl 组合走命令，不插入
                int cp = c;
                if (!OnAcceptUnicodeChar(cp))       // 源 IntEntry 吃非法字符（int_entry.cpp:126-129）
                    continue;
                ExecuteCmd(Cmd.InsertChar, cp, false);
            }
        }

        /// <summary>源 <c>onAcceptUnicodeChar</c>（int_entry.cpp:176-179）；Entry 基类恒真。</summary>
        protected virtual bool OnAcceptUnicodeChar(int unicodeChar) => true;

        bool HasValidSelection() => _select >= 0;

        // ==================================================================
        // 命令执行（源 entry.cpp:580-792 onExecuteCmd 逐分支）
        // ==================================================================

        /// <summary>源 <c>onExecuteCmd</c>（entry.cpp:580-792）。</summary>
        protected virtual void ExecuteCmd(Cmd cmd, int unicodeChar, bool expandSelection)
        {
            string text = _text;
            EntryRange range = SelectedRange();

            switch (cmd)
            {
                case Cmd.NoOp:
                    break;

                case Cmd.InsertChar:
                    if (!range.IsEmpty)
                    {
                        DeleteRange(range, ref text);
                        RecalcCharBoxes(text);
                        SetCaretPos(_caret);
                    }
                    if (LastCaretPos() < _maxsize)
                    {
                        int oldnboxes = _boxes.Count;
                        text = text.Insert(_boxes[_caret].from, ((char)unicodeChar).ToString());
                        RecalcCharBoxes(text);
                        int delta = _boxes.Count - oldnboxes;
                        _caret += delta;
                    }
                    _select = -1;
                    break;

                case Cmd.PrevChar:
                case Cmd.PrevWord:
                    if (expandSelection) { if (_select < 0) _select = _caret; }
                    else _select = -1;
                    if (cmd == Cmd.PrevWord) BackwardWord();
                    else if (_caret > 0) _caret--;
                    break;

                case Cmd.NextChar:
                case Cmd.NextWord:
                    if (expandSelection) { if (_select < 0) _select = _caret; }
                    else _select = -1;
                    if (cmd == Cmd.NextWord) ForwardWord();
                    else if (_caret < text.Length) _caret++;
                    break;

                case Cmd.BegOfLine:
                case Cmd.BegOfFile:
                    if (expandSelection) { if (_select < 0) _select = _caret; }
                    else _select = -1;
                    _caret = 0;
                    break;

                case Cmd.EndOfLine:
                case Cmd.EndOfFile:
                    if (expandSelection) { if (_select < 0) _select = _caret; }
                    else _select = -1;
                    _caret = LastCaretPos();
                    break;

                case Cmd.DeleteNextChar:
                case Cmd.Cut:
                    if (!range.IsEmpty)
                    {
                        if (cmd == Cmd.Cut && HasValidSelection())
                            SetClipboard(SelectedText());
                        DeleteRange(range, ref text);
                    }
                    else if (_caret < text.Length)
                    {
                        text = text.Remove(_boxes[_caret].from, _boxes[_caret].to - _boxes[_caret].from);
                    }
                    _select = -1;
                    break;

                case Cmd.Paste:
                {
                    string clip = GetClipboard();
                    if (!string.IsNullOrEmpty(clip))
                    {
                        if (!range.IsEmpty)
                        {
                            DeleteRange(range, ref text);
                            _select = -1;
                        }
                        RecalcCharBoxes(text);
                        int oldBoxes = _boxes.Count;
                        text = text.Insert(_boxes[_caret].from, clip);
                        RecalcCharBoxes(text);
                        if (LastCaretPos() > _maxsize)
                        {
                            text = text.Remove(_boxes[_maxsize].from,
                                text.Length - _boxes[_maxsize].from);
                            RecalcCharBoxes(text);
                        }
                        int newBoxes = _boxes.Count;
                        SetCaretPos(_caret + (newBoxes - oldBoxes));
                    }
                    break;
                }

                case Cmd.Copy:
                    if (!range.IsEmpty)
                        SetClipboard(SelectedText());
                    break;

                case Cmd.DeletePrevChar:
                    if (!range.IsEmpty)
                    {
                        DeleteRange(range, ref text);
                    }
                    else if (_caret > 0)
                    {
                        --_caret;
                        text = text.Remove(_boxes[_caret].from, _boxes[_caret].to - _boxes[_caret].from);
                    }
                    _select = -1;
                    break;

                case Cmd.DeletePrevWord:
                    _select = _caret;
                    BackwardWord();
                    if (_caret < _select)
                        text = text.Remove(_boxes[_caret].from,
                            _boxes[_select - 1].to - _boxes[_caret].from);
                    _select = -1;
                    break;

                case Cmd.DeleteNextWord:
                    _select = _caret;
                    ForwardWord();
                    if (_caret > _select)
                    {
                        text = text.Remove(_boxes[_select].from,
                            _boxes[_caret].to - _boxes[_select].from);
                        _caret = _select;
                    }
                    _select = -1;
                    break;

                case Cmd.DeleteToEndOfLine:
                    text = text.Remove(_boxes[_caret].from);
                    break;

                case Cmd.SelectAll:
                    SelectAllText();
                    break;
            }

            if (text != _text)
            {
                SetText(text);
                OnChange();
            }

            SetCaretPos(_caret);
            Refresh();
        }

        /// <summary>源 <c>deleteRange()</c>（entry.cpp:938-942）。</summary>
        void DeleteRange(EntryRange range, ref string text)
        {
            text = text.Remove(_boxes[range.from].from,
                _boxes[range.to - 1].to - _boxes[range.from].from);
            _caret = range.from;
        }

        /// <summary>源 <c>forwardWord()</c>（entry.cpp:794-807）。</summary>
        void ForwardWord()
        {
            int textlen = LastCaretPos();
            for (; _caret < textlen; ++_caret)
                if (IsWordChar(CodeAt(_caret))) break;
            for (; _caret < textlen; ++_caret)
                if (!IsWordChar(CodeAt(_caret))) break;
        }

        /// <summary>源 <c>backwardWord()</c>（entry.cpp:809-825）。</summary>
        void BackwardWord()
        {
            for (--_caret; _caret >= 0; --_caret)
                if (IsWordChar(CodeAt(_caret))) break;
            for (; _caret >= 0; --_caret)
            {
                if (!IsWordChar(CodeAt(_caret))) { ++_caret; break; }
            }
            if (_caret < 0) _caret = 0;
        }

        /// <summary>源 <c>wordRange()</c>（entry.cpp:827-860）。</summary>
        EntryRange WordRange(int pos)
        {
            int last = LastCaretPos();
            pos = Mathf.Clamp(pos, 0, last);
            int i, j;
            i = j = pos;

            if (IsWordChar(CodeAt(pos)))
            {
                for (; i >= 0; --i) if (!IsWordChar(CodeAt(i))) break;
                ++i;
                for (; j <= last; ++j) if (!IsWordChar(CodeAt(j))) break;
            }
            else
            {
                for (; i >= 0; --i) if (IsWordChar(CodeAt(i))) break;
                ++i;
                for (; j <= last; ++j) if (IsWordChar(CodeAt(j))) break;
            }
            return new EntryRange(i, j);
        }

        /// <summary>源 <c>isPosInSelection()</c>（entry.cpp:862-865）。</summary>
        bool IsPosInSelection(int pos)
        {
            return pos >= Mathf.Min(_caret, _select) && pos <= Mathf.Max(_caret, _select);
        }

        /// <summary>源 <c>onChange()</c> → <c>Change()</c>（entry.cpp:528-531）。</summary>
        protected virtual void OnChange()
        {
            Change?.Invoke();
        }

        // ==================================================================
        // 鼠标（源 entry.cpp:357-448）
        // ==================================================================

        /// <summary>源 <c>requestFocus()</c>：把 EventSystem 选中项设为本件 → 触发 OnSelect。</summary>
        protected void RequestFocus()
        {
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != gameObject)
                es.SetSelectedGameObject(gameObject);
        }

        /// <summary>
        /// 源 kMouseDownMessage（entry.cpp:357-365）+ fallthrough 到 move：
        /// captureMouse → 清 m_selecting_words → 定位 caret。
        /// 双击（entry.cpp:432-441）→ 选词；三击 → 全选（非源扩展，单行「选行」= 全选）。
        /// </summary>
        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (!Resolve() || !IsEnabled)
                return;

            RequestFocus();
            eventData.useDragThreshold = false;   // 源 capture 后每个 move 都跟手，无 UGUI 死区
            _dragging = true;

            if (!_selectingWords.IsEmpty)
                _selectingWords.Reset();

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                ApplyMouse(eventData, true);
                return;
            }

            int clicks = eventData.clickCount;
            if (clicks >= 3)
            {
                SelectAllText();
                return;
            }
            if (clicks == 2)
            {
                // 源 kDoubleClickMessage（entry.cpp:432-441）
                _selectingWords = WordRange(_caret);
                SelectText(_selectingWords.from, _selectingWords.to);
                return;
            }

            ApplyMouse(eventData, true);
        }

        /// <summary>源 kMouseMoveMessage with capture（entry.cpp:367-410）。</summary>
        public virtual void OnDrag(PointerEventData eventData)
        {
            ApplyMouse(eventData, false);
            eventData.Use();
        }

        public virtual void OnBeginDrag(PointerEventData eventData) { }

        /// <summary>源 kMouseUpMessage（entry.cpp:414-430）：释放 capture；右键锁选区。</summary>
        public virtual void OnPointerUp(PointerEventData eventData)
        {
            ReleaseMouse(eventData);
        }

        public virtual void OnEndDrag(PointerEventData eventData)
        {
            ReleaseMouse(eventData);
        }

        void ReleaseMouse(PointerEventData eventData)
        {
            if (!Resolve() || !_dragging)
                return;
            _dragging = false;
            if (!_selectingWords.IsEmpty)
                _selectingWords.Reset();

            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                // 源：kFocusEnter 会清 m_lock_selection，右键抬起再置 true 锁住选区。
                _lockSelection = true;
                // 源 showEditPopupMenu(display, pos)（textcmd.cpp:108-144）未移植（登记偏差）
                RequestFocus();
            }
        }

        /// <summary>源 kMouseDown/kMouseMove 的共用定位逻辑（entry.cpp:367-410）。</summary>
        void ApplyMouse(PointerEventData eventData, bool isDown)
        {
            if (!_dragging)
                return;

            bool left = eventData.button == PointerEventData.InputButton.Left;
            int c = GetCaretFromMouse(eventData);
            bool dirty = false;

            if (left || !IsPosInSelection(c))
            {
                if (_caret != c)
                {
                    SetCaretPos(c);
                    dirty = true;
                }
                if (_recentFocused)
                {
                    _recentFocused = false;
                    _select = _caret;
                }
                else if (isDown)
                {
                    _select = _caret;
                }
                else if (!_selectingWords.IsEmpty)
                {
                    EntryRange toWord = WordRange(_caret);
                    if (toWord.from < _selectingWords.from)
                    {
                        _select = Mathf.Max(_selectingWords.to, toWord.to);
                        SetCaretPos(Mathf.Min(_selectingWords.from, toWord.from));
                    }
                    else
                    {
                        _select = Mathf.Min(_selectingWords.from, toWord.from);
                        SetCaretPos(Mathf.Max(_selectingWords.to, toWord.to));
                    }
                }
            }

            if (dirty)
            {
                if (ShouldStartTimer(true)) StartTimer();
                _state = true;
                Refresh();
            }
        }

        /// <summary>源 <c>getCaretFromMouse()</c>（entry.cpp:543-578）。</summary>
        int GetCaretFromMouse(PointerEventData eventData)
        {
            float mouseX = MouseXInTextArea(eventData);

            if (mouseX < 0f)
                return Mathf.Max(0, _scroll - 1);       // 向左滚

            int lastPos = LastCaretPos();
            int scroll = _scroll;
            int i = Mathf.Min(scroll, lastPos);
            float scrollX = _boxes[scroll].x;
            int width = TextBoundsWidth;

            for (; i < lastPos; ++i)
            {
                float x = _boxes[i].x * Scale.x - scrollX;
                if (mouseX >= x && mouseX < x + _boxes[i].width)
                    break;

                if (mouseX > width)
                {
                    if (x >= width)
                    {
                        i = Mathf.Min(++scroll, lastPos);
                        if (i == lastPos) break;
                        scrollX = _boxes[scroll].x;
                    }
                }
                else if (x > mouseX)
                    break;
            }

            return Mathf.Clamp(i, 0, lastPos);
        }

        /// <summary>鼠标在文本区内的整格横坐标（相对文本区左缘）。</summary>
        float MouseXInTextArea(PointerEventData eventData)
        {
            RectTransform rect = _textArea;
            Camera cam = eventData.pressEventCamera != null
                ? eventData.pressEventCamera
                : eventData.enterEventCamera;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, cam, out local))
                return 0f;
            return local.x + rect.rect.width * rect.pivot.x;
        }

        public void OnPointerEnter(PointerEventData eventData) { if (IsEnabled) Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { if (IsEnabled) Refresh(); }

        // ==================================================================
        // 焦点（源 kFocusEnter/kFocusLeaveMessage entry.cpp:283-318）
        // ==================================================================

        public virtual void OnSelect(BaseEventData eventData)
        {
            if (!IsEnabled)
                return;
            if (ShouldStartTimer(true))
                StartTimer();
            _state = true;
            Refresh();

            if (_lockSelection)
            {
                _lockSelection = false;
            }
            else
            {
                if (!_persistSelection)
                    SelectAllText();
                _recentFocused = true;
            }
            // setTextInput(true, caretPosOnScreen):IME/dead-key 未移植（登记偏差）
        }

        public virtual void OnDeselect(BaseEventData eventData)
        {
            Refresh();
            StopTimer();

            if (!_lockSelection && !_persistSelection)
                DeselectText();
            _recentFocused = false;
            _dragging = false;
        }

        // ==================================================================
        // 绘制（源 skin_theme.cpp:1357-1549 paintEntry/drawEntryText + 1915-1923 drawEntryCaret）
        // ==================================================================

        /// <summary>
        /// 复刻 paintEntry：sunken 底件按 hasFocus 换件（skin_theme.cpp:1381-1386）；
        /// 文本/选区/光标按 m_boxes 单一几何源摆放（drawEntryText/DrawEntryTextDelegate）。
        /// </summary>
        public void Refresh()
        {
            if (!Resolve())
                return;

            bool focus = HasFocus;
            if (_face != null)
                _face.sprite = PixelSkin.Sunken(focus);

            int th = TextHeight;
            int areaH = Mathf.RoundToInt(_textArea.rect.height);
            int textTop = Mathf.FloorToInt((areaH - th) / 2f);
            int scrollPx = _boxes.Count > 0
                ? Mathf.RoundToInt(_boxes[Mathf.Clamp(_scroll, 0, _boxes.Count - 1)].x)
                : 0;

            // ---- 选区（源 DrawEntryTextDelegate::preProcessChar 的 bg = colors.selected()）----
            EntryRange r = SelectedRange();
            bool hasSel = !r.IsEmpty;
            if (hasSel)
            {
                int from = Mathf.Clamp(r.from, 0, _boxes.Count - 1);
                int to = Mathf.Clamp(r.to, 0, _boxes.Count - 1);
                int x0 = Mathf.RoundToInt(_boxes[from].x) - scrollPx;
                int x1 = Mathf.RoundToInt(_boxes[to].x) - scrollPx;
                int sw = x1 - x0;

                SetTopLeft(_selection.rectTransform, x0, textTop, sw, th);
                _selection.enabled = sw > 0;

                if (_darkClip != null)
                {
                    SetTopLeft(_darkClip, x0, textTop, sw, th);
                    _darkClip.gameObject.SetActive(sw > 0);
                    // 暗色层与常态层同源排版：暗色层局部左缘 = -scroll - x0，
                    // 使其字形与常态层在文本区同一 x 上重合，仅被裁到选区窗口。
                    if (_darkLabel != null)
                        _darkLabel.rectTransform.anchoredPosition = new Vector2(-scrollPx - x0, 0f);
                }
            }
            else
            {
                _selection.enabled = false;
                if (_darkClip != null)
                    _darkClip.gameObject.SetActive(false);
            }

            // ---- 正文（源 colors.text()；placeholder 走 entrySuffix 色）----
            bool showPlaceholder = _text.Length == 0 && !_readonly && !string.IsNullOrEmpty(_placeholder);
            string display = showPlaceholder ? _placeholder : _text;
            if (_lightLabel.text != display)
                _lightLabel.text = display;
            _lightLabel.color = showPlaceholder ? (Color)ColSuffix : (Color)ColText;
            _lightLabel.rectTransform.anchoredPosition = new Vector2(-scrollPx, 0f);

            if (_darkLabel != null)
            {
                if (_darkLabel.text != _text)
                    _darkLabel.text = _text;
                _darkLabel.color = ColSelectedText;
            }

            // ---- 光标（源 drawEntryCaret：state && focus && enabled；1 条 2px 宽竖线）----
            bool caretOn = IsCaretVisible && focus && IsEnabled;
            _caretQuad.enabled = caretOn;
            if (caretOn)
            {
                int idx = Mathf.Clamp(_caret, 0, _boxes.Count - 1);
                int cx = Mathf.RoundToInt(_boxes[idx].x * Scale.x) - scrollPx;
                int cy = textTop + th / 2 - CaretHeight / 2;
                SetTopLeft(_caretQuad.rectTransform, cx, cy, CaretWidthPx, CaretHeight);
            }
        }

        /// <summary>左顶锚摆放（整数像素坐标）。</summary>
        static void SetTopLeft(RectTransform rect, int x, int yTop, int w, int h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, -yTop);
        }

        // ==================================================================
        // 外向文本模型（禁用态 TMP_InputField）
        // ==================================================================

        void SyncToField()
        {
            if (_field == null)
                return;
            _syncingField = true;
            if (_field.text != _text)
                _field.text = _text;
            _syncingField = false;
        }

        void SyncFromField()
        {
            if (_field == null || _syncingField)
                return;
            string ft = _field.text;
            if (ft != null && ft != _text)
                SetText(ft);
        }

        void AdoptExternalText(string value)
        {
            if (_syncingField)
                return;
            if (value == null) value = string.Empty;
            if (value != _text)
                SetText(value);
        }

        // ==================================================================
        // 剪贴板（源 set_clipboard_text/get_clipboard_text）
        // ==================================================================

        protected static string GetClipboard()
        {
            try { return GUIUtility.systemCopyBuffer ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        protected static void SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { GUIUtility.systemCopyBuffer = text; }
            catch (Exception) { /* 无系统剪贴板环境忽略 */ }
        }
    }
}
