using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// M3 新增场景名常量（<c>CrewManagement</c> / <c>LevelSelect</c>）。
    ///
    /// 【为什么不在 Core/SceneNames】<c>Core/SceneNames.cs</c> 是 M2 前的既有文件，
    ///   M3 实施期间限定 Core 只读（见 M3 派单白名单），故两个新场景名暂放这里。
    ///   「应并入 <c>Core/SceneNames</c> 统一登记」已写进 M3 交付报告，由协调者裁决。
    /// </summary>
    public static class M3Scenes
    {
        /// <summary>船员管理场景（招募 / 编成）。</summary>
        public const string CrewManagement = "CrewManagement";

        /// <summary>关卡选择场景（选关 → 进 Battle）。</summary>
        public const string LevelSelect = "LevelSelect";
    }

    /// <summary>
    /// M3 界面用的 UGUI 构建辅助（运行时建控件，不依赖 Prefab / Sprite 资源）。
    ///
    /// 【为什么用代码建】M3 只做「最小可用」界面：列表行数随名册/章节变化，
    ///   运行时生成比摆 Prefab 更省接线；且不新增美术资源。
    ///   ⚠ 本工程未装 TextMeshPro（见 <c>SceneSetup</c> 类头），文本统一
    ///   <c>UnityEngine.UI.Text</c> + 内置 <c>LegacyRuntime.ttf</c>。
    ///   按钮底图用纯色 <see cref="Image"/>（运行时拿不到 Editor 的内置 UISprite）。
    /// </summary>
    public static class M3UiBuilder
    {
        /// <summary>按钮底色（深蓝灰）。</summary>
        public static readonly Color ButtonColor = new Color(0.16f, 0.22f, 0.34f, 1f);

        /// <summary>按钮不可用态底色。</summary>
        public static readonly Color DisabledButtonColor = new Color(0.16f, 0.16f, 0.18f, 1f);

        /// <summary>列表行底色（半透深色）。</summary>
        public static readonly Color RowColor = new Color(0.1f, 0.14f, 0.22f, 0.85f);

        static Font _font;

        /// <summary>UI 字体（Unity 2022.2+ 内置资源名 <c>LegacyRuntime.ttf</c>）。</summary>
        public static Font UiFont
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                return _font;
            }
        }

        /// <summary>建一个带 RectTransform 的空 UI 对象。</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>建文本控件。</summary>
        public static Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = UiFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>建按钮（纯色底 + 居中文本）。</summary>
        public static Button CreateButton(string name, Transform parent, string label, int fontSize = 20)
        {
            RectTransform rect = CreateRect(name, parent);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText("Text", rect, label, fontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);

            return button;
        }

        /// <summary>
        /// 在列表容器里建一行（纵向堆叠，锚在容器顶部）。
        /// </summary>
        /// <param name="container">列表容器（需有确定大小）。</param>
        /// <param name="index">行序号（0 起）。</param>
        /// <param name="rowHeight">行高。</param>
        /// <param name="spacing">行间距。</param>
        /// <param name="leftPadding">左内边距。</param>
        public static RectTransform CreateRow(Transform container, int index, float rowHeight,
            float spacing = 6f, float leftPadding = 8f)
        {
            RectTransform row = CreateRect("Row" + index, container);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-leftPadding * 2f, rowHeight);
            row.anchoredPosition = new Vector2(0f, -index * (rowHeight + spacing));

            var background = row.gameObject.AddComponent<Image>();
            background.color = RowColor;
            background.raycastTarget = false;

            return row;
        }

        /// <summary>清空容器下的全部子对象（重建列表前调用）。</summary>
        public static void ClearChildren(Transform container)
        {
            if (container == null)
                return;

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                GameObject child = container.GetChild(i).gameObject;
                // Destroy 是延迟到帧末执行的；先 SetActive(false) 避免本帧出现新旧两套列表叠在一起。
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        /// <summary>铺满父容器（可选内边距）。</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>按锚点 + 尺寸摆放（anchoredPosition 相对锚点）。</summary>
        public static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>把行内文本放在左侧、按钮放在右侧的常用布局。</summary>
        public static void LayoutRowContent(RectTransform row, Text label, Button action, float rowHeight)
        {
            float buttonWidth = 140f;
            float buttonHeight = rowHeight - 8f;

            if (label != null)
            {
                RectTransform rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = new Vector2(12f, 0f);
                rect.offsetMax = new Vector2(-(buttonWidth + 16f), 0f);
            }

            if (action != null)
            {
                RectTransform rect = action.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
                rect.anchoredPosition = new Vector2(-12f, 0f);
            }
        }
    }
}
