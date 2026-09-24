using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 整像素吸附：每帧把画布下全部 RectTransform 的位置与尺寸取整。布局数学（居中/均分）
    /// 会产生小数落位，GPU 按小数采样时每个元素的取整方向各自为政——同一列元素
    /// 左上/右上角细节各不相同的病根（创始人走查多轮指出）。仅在值变化时写入，
    /// 静止层级零开销；菜单类界面层级浅，逐帧全扫无压力。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPixelSnap : MonoBehaviour
    {
        void LateUpdate() => Snap((RectTransform)transform);

        static void Snap(RectTransform rect)
        {
            if (rect.anchorMin == rect.anchorMax)
            {
                Vector2 pos = rect.anchoredPosition;
                Vector2 snapped = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
                if (snapped != pos)
                    rect.anchoredPosition = snapped;
            }
            else
            {
                Vector2 min = rect.offsetMin, max = rect.offsetMax;
                Vector2 sMin = new Vector2(Mathf.Round(min.x), Mathf.Round(min.y));
                Vector2 sMax = new Vector2(Mathf.Round(max.x), Mathf.Round(max.y));
                if (sMin != min || sMax != max)
                {
                    rect.offsetMin = sMin;
                    rect.offsetMax = sMax;
                }
            }

            Vector2 size = rect.sizeDelta;
            Vector2 sSize = new Vector2(Mathf.Round(size.x), Mathf.Round(size.y));
            if (sSize != size)
                rect.sizeDelta = sSize;

            for (int i = 0; i < rect.childCount; i++)
                if (rect.GetChild(i) is RectTransform child)
                    Snap(child);
        }
    }
}
