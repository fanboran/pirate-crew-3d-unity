using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 手绘小控件集（组件陈列页 / 波次 B 菜单屏共用的轻量件）：
    /// 页签条（tab 槽 + 琥珀下划线贴图）、Toast 横幅——对齐隔壁 SketchTabBar / StickKit.toast。
    /// 皮肤经 <see cref="SketchSkin"/> 槽位 + <see cref="SketchBoil"/> 沸腾。
    /// </summary>
    public static class SketchWidgets
    {
        /// <summary>
        /// 建一排页签（选中=tab_selected 槽自带琥珀马克笔下划线，悬停=tab_hover）。
        /// 返回根 RectTransform；点击回调 <paramref name="onChange"/>（传入选中序号）。
        /// </summary>
        public static RectTransform Tabs(Transform parent, Vector2 position, Vector2 size,
            string[] titles, TMP_FontAsset font, int selected, Action<int> onChange)
        {
            RectTransform root = UiKit.CreateRect("Tabs", parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = size;
            root.anchoredPosition = position;

            float tabWidth = size.x / titles.Length;
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                RectTransform tab = UiKit.CreateRect("Tab_" + titles[i], root);
                tab.anchorMin = tab.anchorMax = tab.pivot = new Vector2(0f, 0.5f);
                tab.sizeDelta = new Vector2(tabWidth - 6f, size.y);
                tab.anchoredPosition = new Vector2(i * tabWidth, 0f);

                var image = tab.gameObject.AddComponent<Image>();
                image.sprite = SketchSkin.Frame(i == selected ? "tab_selected" : "tab_hover", 0);
                image.type = Image.Type.Sliced;
                image.color = Color.white;
                image.raycastTarget = true;
                var boil = tab.gameObject.AddComponent<SketchBoil>();
                boil.Slot = i == selected ? "tab_selected" : "tab_hover";

                var button = tab.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.colors = UiKit.FourState(Color.white);
                TextMeshProUGUI text = UiKit.CreateText("Label", tab, titles[i], UiSkin.Font.Body,
                    TextAlignmentOptions.Center, i == selected ? UiSkin.Gold : UiSkin.TextOnInk, font);
                UiKit.Stretch(text.rectTransform, 6f);

                button.onClick.AddListener(() =>
                {
                    for (int j = 0; j < root.childCount; j++)
                    {
                        Transform other = root.GetChild(j);
                        var otherImage = other.GetComponent<Image>();
                        var otherBoil = other.GetComponent<SketchBoil>();
                        bool isSel = j == index;
                        if (otherImage != null)
                            otherImage.sprite = SketchSkin.Frame(isSel ? "tab_selected" : "tab_hover", 0);
                        if (otherBoil != null)
                            otherBoil.Slot = isSel ? "tab_selected" : "tab_hover";
                        var label = other.GetComponentInChildren<TextMeshProUGUI>();
                        if (label != null)
                            label.color = isSel ? UiSkin.Gold : UiSkin.TextOnInk;
                    }
                    onChange?.Invoke(index);
                });
            }
            return root;
        }

        /// <summary>弹一条 Toast 横幅（panel_light 手绘条 + 语义色文字；3.2s 自动消失）。</summary>
        public static void Toast(RectTransform layer, string message, Color color, TMP_FontAsset font)
        {
            // 56px 矮件走单图 Sliced——九砖中缝塞不下 76px 平铺砖（r13 溢出事故）。
            RectTransform toast = UiKit.CreateRect("Toast", layer);
            toast.anchorMin = toast.anchorMax = new Vector2(0.5f, 1f);
            toast.pivot = new Vector2(0.5f, 1f);
            toast.sizeDelta = new Vector2(420f, 56f);
            toast.anchoredPosition = new Vector2(0f, -36f);
            var image = toast.gameObject.AddComponent<Image>();
            image.sprite = SketchSkin.Frame("panel_light", 0);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            var boil = toast.gameObject.AddComponent<SketchBoil>();
            boil.Slot = "panel_light";
            UiKit.CreateText("Msg", toast, message, UiSkin.Font.Body,
                TextAlignmentOptions.Center, color, font);
            toast.gameObject.AddComponent<ToastFader>();
        }

        /// <summary>Toast 消失器（独立 MonoBehaviour 承载协程）。</summary>
        sealed class ToastFader : MonoBehaviour
        {
            IEnumerator Start()
            {
                yield return new WaitForSeconds(3.2f);
                var group = GetComponent<CanvasGroup>();
                if (group == null)
                    group = gameObject.AddComponent<CanvasGroup>();
                float t = 0f;
                while (t < 0.4f)
                {
                    t += Time.unscaledDeltaTime;
                    group.alpha = 1f - t / 0.4f;
                    yield return null;
                }
                Destroy(gameObject);
            }
        }

        /// <summary>建一行键位说明（键帽手绘 chip + 说明文字），返回行高。</summary>
        public static float KeymapRow(RectTransform parent, Vector2 position, string key,
            string description, TMP_FontAsset font)
        {
            RectTransform cap = UiKit.CreateRect("Key_" + key, parent);
            cap.anchorMin = cap.anchorMax = new Vector2(0.5f, 0.5f);
            cap.pivot = new Vector2(0f, 0.5f);
            cap.sizeDelta = new Vector2(92f, 38f);
            cap.anchoredPosition = position;
            var image = cap.gameObject.AddComponent<Image>();
            image.sprite = SketchSkin.Frame("btn_normal", 0);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            var boil = cap.gameObject.AddComponent<SketchBoil>();
            boil.Slot = "btn_normal";
            UiKit.CreateText("Key", cap, key, UiSkin.Font.Body,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, font);

            TextMeshProUGUI desc = UiKit.CreateText("Desc", parent, description, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, UiSkin.TextDim, font);
            desc.enableWordWrapping = false;
            desc.rectTransform.anchorMin = desc.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            desc.rectTransform.pivot = new Vector2(0f, 0.5f);
            desc.rectTransform.sizeDelta = new Vector2(320f, 30f);
            desc.rectTransform.anchoredPosition = position + new Vector2(108f, 0f);
            return 48f;
        }
    }
}
