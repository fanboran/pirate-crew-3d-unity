using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦分隔线 —— game-2 SketchSeparator（sketch_separator.gd）的 UGUI 复刻（**已换 Beveled Pixel 皮**）。
    ///
    /// 【皮肤口径（换装后）】像素皮不再自绘波浪线：分隔线 = 一张 <see cref="PixelSkin.Separator"/>
    /// 蚀刻线贴图（水平 / 垂直两档，1u 厚的凹刻线，色阶由调色板烘焙），按控件矩形铺开。
    /// 旧版是逐公式对照的自绘波形（BORDER@0.35）；像素皮改走贴图件后，"线宽/羽化"由烘焙器负责，
    /// 本类只做方向 → 贴图的映射。
    ///
    /// 【公开 API 不变】Dir / SeedBase / Create 四参签名保留——四个调用点（主菜单标题下、
    /// 设置面板标题下、结算弹窗、样张页）零改动即换皮。SeedBase 在贴图件下不再驱动动画，
    /// 仅为兼容保留（旧沸腾重掷序列的确定性 seed 概念随自绘层下线）。
    ///
    /// 【自绘层文件保留】旧的波浪自绘件（独立文件）仍在，归协调者统一处置——本类不再挂它。
    /// </summary>
    public sealed class SketchSeparator : MonoBehaviour
    {
        /// <summary>方向（gd Dir.HORIZONTAL/VERTICAL 同名）。</summary>
        public enum Direction
        {
            Horizontal,
            Vertical,
        }

        /// <summary>方向（运行时可切）。</summary>
        public Direction Dir = Direction.Horizontal;

        /// <summary>seed 基值（贴图件下不再驱动动画；保留字段以维持调用点签名兼容）。</summary>
        public int SeedBase;

        /// <summary>建一条分隔线（纯展示件，不拦截点击）。direction 缺省水平。</summary>
        public static SketchSeparator Create(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            Direction direction = Direction.Horizontal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            // 线厚纪律：蚀刻线是 1u（3px）件——矩形过薄会把包边挤没、过厚会把线拉成一条带，
            // 故厚度轴统一钉到 PixelSkin.Unit，长度轴按调用方给的值铺开（调用点尺寸字面量不用改）。
            rect.sizeDelta = direction == Direction.Horizontal
                ? new Vector2(size.x, PixelSkin.Unit)
                : new Vector2(PixelSkin.Unit, size.y);
            rect.anchoredPosition = anchoredPosition;

            var separator = go.AddComponent<SketchSeparator>();
            separator.Dir = direction;
            separator.Apply();
            return separator;
        }

        private void OnEnable() => Apply();

        /// <summary>方向 → 蚀刻线贴图。Sliced：贴图若带切片边框则两端不拉花，
        /// 无边框时退化为整图拉伸（对一条 1u 直线等价）。</summary>
        private void Apply()
        {
            var image = GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Separator(Dir == Direction.Horizontal);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：线色烘在贴图里
            image.raycastTarget = false;
        }
    }
}
