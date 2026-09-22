using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 手绘小控件集（组件陈列页 / 波次 B 菜单屏共用的轻量件）——**已换 Beveled Pixel 皮**：
    /// 页签条（<see cref="PixelSkin.Tab"/> 两态贴图 + SpriteSwap）、Toast 横幅
    /// （<see cref="PixelTone.Light"/> Plate + 底垫投影）、键帽（Plate(Light)）。
    /// 皮肤一律经 <see cref="PixelSkin"/> 取件，像素件不做乘色（状态靠换贴图）。
    /// </summary>
    public static class SketchWidgets
    {
        /// <summary>
        /// 建一排页签（选中 = Tab(Light) 暖白页签、未选中 = Tab(Dense) 暗页签；
        /// 悬停/按压 = SpriteSwap 换到选中档贴图，即"hover 预演选中"）。
        /// 返回根 RectTransform；点击回调 <paramref name="onChange"/>（传入选中序号）。
        /// </summary>
        public static RectTransform Tabs(Transform parent, Vector2 position, Vector2 size,
            string[] titles, TMP_FontAsset font, int selected, Action<int> onChange)
        {
            RectTransform root = UiKit.CreateRect("Tabs", parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Snap(size);
            root.anchoredPosition = Round(position);

            float tabWidth = root.sizeDelta.x / titles.Length;
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                RectTransform tab = UiKit.CreateRect("Tab_" + titles[i], root);
                tab.anchorMin = tab.anchorMax = tab.pivot = new Vector2(0f, 0.5f);
                tab.sizeDelta = new Vector2(SnapUnit(tabWidth - 6f), SnapUnit(root.sizeDelta.y));
                tab.anchoredPosition = new Vector2(Mathf.Round(i * tabWidth), 0f);

                var image = tab.gameObject.AddComponent<Image>();
                image.sprite = TabSprite(i == selected);
                image.type = Image.Type.Sliced;
                image.color = Color.white;      // 像素件禁止乘色：tone 色阶烘在贴图里
                image.raycastTarget = true;

                var button = tab.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                // 状态靠换贴图（SpriteSwap）：hover/pressed/selected 都换到"选中档"贴图。
                // （ColorBlock 已退役——SpriteSwap 下被忽略，且乘色会毁掉烘死的 tone 色阶。）
                button.transition = Selectable.Transition.SpriteSwap;
                SpriteState states = button.spriteState;
                states.highlightedSprite = TabSprite(true);
                states.pressedSprite = TabSprite(true);
                states.selectedSprite = TabSprite(true);
                button.spriteState = states;

                TextMeshProUGUI text = UiKit.CreateText("Label", tab, titles[i], UiSkin.Font.Body,
                    TextAlignmentOptions.Center, TabTextColor(i == selected), font);
                UiKit.Stretch(text.rectTransform, 6f);

                button.onClick.AddListener(() =>
                {
                    for (int j = 0; j < root.childCount; j++)
                    {
                        Transform other = root.GetChild(j);
                        bool isSel = j == index;
                        var otherImage = other.GetComponent<Image>();
                        if (otherImage != null)
                            otherImage.sprite = TabSprite(isSel);
                        var label = other.GetComponentInChildren<TextMeshProUGUI>();
                        if (label != null)
                            label.color = TabTextColor(isSel);
                    }
                    onChange?.Invoke(index);
                });
            }
            return root;
        }

        /// <summary>弹一条 Toast 横幅（Plate(Light) 暖白条 + 底垫投影；语义色的文字由调用方给；
        /// 3.2s 自动消失）。</summary>
        public static void Toast(RectTransform layer, string message, Color color, TMP_FontAsset font)
        {
            // 矮件走单图 Sliced：Plate 的切片边框 4u，56→57（3 的整数倍）不切进内容区。
            RectTransform toast = UiKit.CreateRect("Toast", layer);
            toast.anchorMin = toast.anchorMax = new Vector2(0.5f, 1f);
            toast.pivot = new Vector2(0.5f, 1f);
            toast.sizeDelta = new Vector2(420f, 57f);
            toast.anchoredPosition = new Vector2(0f, -36f);
            // 面板 = Plate + 底垫投影（投影先建，垫在本体之下——子件绘制序在同级按加入序）。
            AddBackplate(toast, PixelTone.Light, withShadow: true);
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

        /// <summary>建一行键位说明（键帽 Plate(Light) 像素件 + 说明文字），返回行高。</summary>
        public static float KeymapRow(RectTransform parent, Vector2 position, string key,
            string description, TMP_FontAsset font)
        {
            RectTransform cap = UiKit.CreateRect("Key_" + key, parent);
            cap.anchorMin = cap.anchorMax = new Vector2(0.5f, 0.5f);
            cap.pivot = new Vector2(0f, 0.5f);
            cap.sizeDelta = new Vector2(93f, 39f);      // 3 的整数倍（±1 对齐像素栅格）
            cap.anchoredPosition = Round(position);
            var image = cap.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(PixelTone.Light);
            image.type = Image.Type.Sliced;
            image.color = Color.white;                  // 像素件禁止乘色
            image.raycastTarget = false;
            UiKit.CreateText("Key", cap, key, UiSkin.Font.Body,
                TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Light), font);

            TextMeshProUGUI desc = UiKit.CreateText("Desc", parent, description, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, PixelSkin.TextColorOn(PixelTone.Frame), font);
            desc.enableWordWrapping = false;
            desc.rectTransform.anchorMin = desc.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            desc.rectTransform.pivot = new Vector2(0f, 0.5f);
            desc.rectTransform.sizeDelta = new Vector2(320f, 30f);
            desc.rectTransform.anchoredPosition = Round(position) + new Vector2(108f, 0f);
            return 48f;
        }

        // ------------------------------------------------------------------
        // 像素件小工具（本类内共用，避免逐处重写"投影 + 本体"两件式）
        // ------------------------------------------------------------------

        /// <summary>面板 = 底垫投影（先建）+ Plate 本体（后建）；返回本体 Image。</summary>
        static Image AddBackplate(RectTransform root, PixelTone tone, bool withShadow)
        {
            if (withShadow)
            {
                RectTransform shadow = UiKit.CreateRect("Shadow", root);
                UiKit.Stretch(shadow);
                shadow.anchoredPosition = PixelSkin.ShadowOffset;   // 右下错开 1u
                var shadowImage = shadow.gameObject.AddComponent<Image>();
                shadowImage.sprite = PixelSkin.ShadowSprite;
                shadowImage.type = Image.Type.Sliced;
                shadowImage.color = Color.white;
                shadowImage.raycastTarget = false;
            }

            RectTransform plate = UiKit.CreateRect("Plate", root);
            UiKit.Stretch(plate);
            var image = plate.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(tone);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>选中档 = Tab(Light)；未选中档 = Tab(Dense)。</summary>
        static Sprite TabSprite(bool selected) =>
            PixelSkin.Tab(selected ? PixelTone.Light : PixelTone.Dense);

        /// <summary>页签字色：浅底给墨字、暗底给本 tone 亮档字（彩色阶烘在贴图里，字色按底自动取）。</summary>
        static Color TabTextColor(bool selected) =>
            PixelSkin.TextColorOn(selected ? PixelTone.Light : PixelTone.Dense);

        /// <summary>v 取到最近的 <see cref="PixelSkin.Unit"/> 整数倍（分数像素会把 3px 带糊成 4px）。</summary>
        static float SnapUnit(float v) => Mathf.Round(v / PixelSkin.Unit) * PixelSkin.Unit;

        /// <summary>尺寸两轴同时对齐像素栅格。</summary>
        static Vector2 Snap(Vector2 size) => new Vector2(SnapUnit(size.x), SnapUnit(size.y));

        /// <summary>坐标取整（分数像素的摆放同样会糊带）。</summary>
        static Vector2 Round(Vector2 v) => new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));
    }
}
