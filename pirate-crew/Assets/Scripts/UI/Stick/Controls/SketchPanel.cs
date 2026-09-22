using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦面板 —— game-2 SketchPanel（sketch_panel.gd）的 UGUI 复刻（**已换 Beveled Pixel 皮**）。
    ///
    /// 【贴图皮肤（换装后）】底 = <see cref="PixelSkin.Plate"/>（色阶烘在贴图里的九宫格）+
    /// 底垫投影 <see cref="PixelSkin.ShadowSprite"/>（按 <see cref="PixelSkin.ShadowOffset"/>
    /// 右下错开 1u，独立剪影件、不拦截点击）——"面板 = 凸起块 + 投影"是像素皮的层级表达，
    /// 替代旧手绘涂鸦皮的"贴图槽 + 逐帧沸腾"（槽位散落是上一版换皮难的根因）。
    /// Dark = <see cref="PixelTone.Frame"/>（大弹窗主底）；Light = <see cref="PixelTone.Light"/>
    /// （暖白内容片：HUD 横条 / 列表行 / 内嵌区块）。
    ///
    /// 【为什么根节点不再挂 Image】投影必须画在面板本体之下，而 UGUI 里子节点的绘制永远在父节点
    /// 自身 Graphic 之后——故本体与投影都做成<b>同级子件</b>（Shadow 先建 → Plate 后建），
    /// 根节点只留 RectTransform 与布局语义。调用方拿到的仍是根 RectTransform，公开 API 不变。
    ///
    /// 【内边距】gd PanelContainer content_margin：Dark 16/12（sketch_panel.gd
    /// PANEL_PAD_*，与 StickTokens.SketchPanelPadX/Y 令牌同值）；Light 16/9
    /// （PAD_LIGHT_*）；紧凑 12/7（compact 小对话框）。Unity 侧容器不约束子件，
    /// 边距经 <see cref="ContentPadding"/> 暴露给装配方摆内容（样张/工厂同口径）。
    ///
    /// 【行为差异（有意）】gd 的 fill_override / outline_override / corner_radius 是
    /// 自绘底时代的参数，像素皮仍不适用——本侧不提供；需要异色底时换 tone 或另叠件。
    /// </summary>
    public sealed class SketchPanel : MonoBehaviour
    {
        /// <summary>深浅底预设：DARK = 大弹窗主底（Frame tone）；LIGHT = 暖白内容片（gd Tone 同名）。</summary>
        public enum Tone
        {
            Dark,
            Light,
        }

        /// <summary>投影子件名（Apply 重建引用时按名查找，见 <see cref="FindPart"/>）。</summary>
        const string PlatePartName = "Plate";

        private Tone _tone = Tone.Dark;
        private UnityEngine.UI.Image _plate;

        /// <summary>深浅底（运行时可切，gd tone setter → queue_redraw 同义换 tone）。</summary>
        public Tone PanelTone
        {
            get => _tone;
            set { _tone = value; Apply(); }
        }

        /// <summary>紧凑内边距（小对话框：内容有多少占多少，gd compact 同义）。</summary>
        public bool Compact;

        /// <summary>内容内边距（gd content_margin 同源；装配方摆内容用）。</summary>
        public Vector2 ContentPadding => Compact
            ? new Vector2(12f, 7f)
            : _tone == Tone.Light
                ? new Vector2(16f, 9f)
                : new Vector2(StickTokens.SketchPanelPadX, StickTokens.SketchPanelPadY);

        /// <summary>建一块面板（根 + Shadow/Plate 两个子件 + 本组件）。tone 缺省 Dark。</summary>
        public static SketchPanel Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, Tone tone = Tone.Dark)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var panel = go.AddComponent<SketchPanel>();
            panel.Compact = false;
            BuildVisuals(rect, out panel._plate);
            panel._tone = tone;     // 直接写字段，Create 路径不重入 Apply 两次
            panel.Apply();
            return panel;
        }

        private void OnEnable() => Apply();

        /// <summary>
        /// 建投影 + 本体两个子件（顺序即绘制序：先 Shadow 后 Plate，投影垫在本体下方）。
        /// 两件都铺满根矩形（跟随装配方的摆放），本体不拦截点击（面板是装饰/容器层，
        /// 命中留给行内动作钮——旧实现同口径）。
        /// </summary>
        private static void BuildVisuals(RectTransform root, out UnityEngine.UI.Image plate)
        {
            UnityEngine.UI.Image shadow = AddPart(root, "Shadow", PixelSkin.ShadowSprite);
            RectTransform shadowRect = (RectTransform)shadow.transform;
            // 铺满后按 ShadowOffset 整体右下错开 1u（偏移是位移不是尺寸，九宫格切片不错位）。
            shadowRect.anchoredPosition = PixelSkin.ShadowOffset;

            plate = AddPart(root, PlatePartName, null);
        }

        private static UnityEngine.UI.Image AddPart(RectTransform root, string partName, Sprite sprite)
        {
            var go = new GameObject(partName, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：tone 色阶烘在贴图里
            image.raycastTarget = false;
            return image;
        }

        /// <summary>按 tone 换 Plate 贴图（投影不随 tone 变——它是 INK 剪影）。</summary>
        private void Apply()
        {
            if (_plate == null)
                _plate = FindPart();
            if (_plate != null)
                _plate.sprite = PixelSkin.Plate(_tone == Tone.Light ? PixelTone.Light : PixelTone.Frame);
        }

        /// <summary>场景重载后私有字段不序列化，按子件名重新取引用（同上一个九砖实现的兜底口径）。</summary>
        private UnityEngine.UI.Image FindPart()
        {
            Transform part = transform.Find(PlatePartName);
            return part != null ? part.GetComponent<UnityEngine.UI.Image>() : null;
        }

        /// <summary>tone（运行时可切）→ 像素 tone 的公开查询，装配方按同一口径摆内容底色。</summary>
        public PixelTone PixelToneOfPanel => _tone == Tone.Light ? PixelTone.Light : PixelTone.Frame;
    }
}
