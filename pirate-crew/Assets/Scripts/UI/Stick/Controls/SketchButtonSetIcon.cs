using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Aseprite theme <c>buttonset_item_text_top_icon_bottom</c> 变体（New Sprite 对话框的
    /// Color Mode / Background 三连）：与 <see cref="SketchButtonSet"/>（居中文字款）平行的
    /// 选项块——**文字上置 + 16×16 图标下置**（theme.xml:1118-1128：padding-top 2 /
    /// padding-bottom 1，图标取 parts 表直切件如 icon_rgb / icon_transparent，sheet 原色）。
    /// 四态 SpriteSwap + 业务"当前值"换 hot 件（selected→hot，无彩色），同 buttonset 语义；
    /// 状态位映射与件解析由 <see cref="AseButtonBase"/> 按 <see cref="StyleId"/> 统一实现。
    /// </summary>
    public sealed class SketchButtonSetIcon : AseButtonBase
    {
        /// <summary>theme 样式 id——变体继承 buttonset_item 同一件表，底皮无差异。</summary>
        protected override string StyleId => "buttonset_item";

        /// <summary>建一枚图文选项块。图标按件原生 16×16 贴底居中，文字贴顶居中。</summary>
        public static SketchButtonSetIcon Create(Transform parent, string name, string label,
            string iconPart, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.AddComponent<Image>();
            image.raycastTarget = true;

            var item = go.AddComponent<SketchButtonSetIcon>();
            item.InitAseSkin(image);      // targetGraphic + SpriteSwap + 全白 ColorBlock

            // 文字 top：边框内再收内距——border-top 3 + padding-top 2 = 5（theme.xml:1067/:1114），
            // 字色 button_normal_text #C0C0C0
            var labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -5f);
            labelRect.sizeDelta = new Vector2(size.x - 6f, 10f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = UiSkin.Font.Tiny;
            TMP_FontAsset resolved = UiKit.ResolvePixelFont(UiSkin.Font.Tiny, null);
            if (resolved != null)
            {
                tmp.font = resolved;
                PixelAtlasPointFilter.Ensure(resolved);
            }
            tmp.fontStyle = FontStyles.Normal;   // 位图字禁伪粗
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.color = PixelSkin.Theme.Text;
            tmp.raycastTarget = false;
            tmp.gameObject.AddComponent<PixelSnapText>();

            // 图标 bottom：border-bottom 5 + padding-bottom 1 = 6（theme.xml:1067/:1114），
            // 16×16 直切件 sheet 原色
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0f);
            iconRect.pivot = new Vector2(0.5f, 0f);
            iconRect.anchoredPosition = new Vector2(0f, 6f);
            iconRect.sizeDelta = new Vector2(16f, 16f);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = PixelSkin.Ase(iconPart);
            icon.pixelsPerUnitMultiplier = 1f;
            icon.color = Color.white;            // 像素件禁止乘色
            icon.raycastTarget = false;

            item.ApplySkin();
            return item;
        }
    }
}
