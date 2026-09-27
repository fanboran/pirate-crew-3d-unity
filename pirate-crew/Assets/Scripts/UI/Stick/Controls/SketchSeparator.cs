// 【产线合一 W2】按钮族（AseButtonBase / SketchButton / SketchButtonSet / SketchButtonSetIcon / SketchSlider）
// 已迁 Assets/Scripts/UI/Skin/，本目录为待退役旧件。
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦分隔线 —— game-2 SketchSeparator（sketch_separator.gd）的 UGUI 复刻（**已换 Beveled Pixel 皮**）。
    ///
    /// 【皮肤口径（换装后）】像素皮不再自绘波浪线：分隔线 = theme 蚀刻点线直切件
    /// （<c>horizontal_separator</c> / <c>vertical_separator</c> 解析出的 <c>separator_horz</c> / <c>separator_vert</c>，
    /// 1u 厚的凹刻点线，色阶由库件自带），按控件矩形铺开。
    /// 旧版是逐公式对照的自绘波形（BORDER@0.35）；像素皮改走贴图件后，"线宽/羽化"由库件负责，
    /// 本类只做方向 → 件 id 的映射。
    ///
    /// 【公开 API】Dir / Create 签名——四个调用点（主菜单标题下、
    /// 设置面板标题下、结算弹窗、样张页）零改动即换皮。
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

        /// <summary>建一条分隔线（纯展示件，不拦截点击）。direction 缺省水平。</summary>
        public static SketchSeparator Create(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            Direction direction = Direction.Horizontal)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            // 线厚 = theme 分隔线件的高（separator_horz/vert 是 9×5 / 5×9 的**点状蚀刻线**，
            // 3 像素周期：2 实 1 虚，色 #202125 = theme disabled）——盒子厚 5 设计格即整件落位；
            // 长度轴按调用方给的值铺开（调用点尺寸字面量不用改）。
            rect.sizeDelta = direction == Direction.Horizontal
                ? new Vector2(size.x, SeparatorThickness)
                : new Vector2(SeparatorThickness, size.y);
            rect.anchoredPosition = anchoredPosition;

            var separator = rect.gameObject.AddComponent<SketchSeparator>();
            separator.Dir = direction;
            separator.Apply();
            return separator;
        }

        /// <summary>分隔线盒子厚（设计格）= theme 件 separator_horz 的高（9×5）；垂直件宽同值。</summary>
        const float SeparatorThickness = 5f;

        private void OnEnable() => Apply();

        /// <summary>
        /// 方向 → theme 直切件。件 id 由 <see cref="AseThemeLayers"/> 从 theme.xml
        /// <c>&lt;style id="horizontal_separator"&gt;</c> / <c>&lt;style id="vertical_separator"&gt;</c>
        /// 的 background-border 层解析（<c>separator_horz</c> 32,80 / <c>separator_vert</c> 32,96，
        /// 无状态层）。
        /// **必须 Tiled**：件是 3 像素周期的点状线，拉伸会把点拉成实线（旧实现走自绘/自烘的
        /// 1u 实线件，与库不同——本波改回库里那件）。
        /// </summary>
        private void Apply()
        {
            var image = GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            string styleId = Dir == Direction.Horizontal ? "horizontal_separator" : "vertical_separator";
            string part = AseThemeLayers.ResolveBackgroundPart(styleId, AseStates.None);
            if (part != null)
                image.sprite = PixelSkin.Ase(part);
            image.type = Image.Type.Tiled;
            image.pixelsPerUnitMultiplier = 1f;   // ×1 终局：贴图纹素 = 画布像素
            image.color = Color.white;      // 像素件禁止乘色：线色烘在贴图里
            image.raycastTarget = false;
        }
    }
}
