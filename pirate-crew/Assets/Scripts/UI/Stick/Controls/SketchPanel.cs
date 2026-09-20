using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦面板 —— game-2 SketchPanel（sketch_panel.gd）的 UGUI 复刻。
    ///
    /// 【贴图皮肤】gd 走 SketchTextures 九宫格沸腾贴图（panel / panel_light 槽全局帧
    /// 驱动轮换）→ 本侧 <see cref="Sketch9Slice"/> 九砖平铺（逐像素等价复刻
    /// StyleBoxTexture 的 TILE 行为）+ <see cref="SketchBoil"/> 沸腾。
    /// Dark = panel 槽（大弹窗主底）；Light = panel_light 槽（HUD 横条/内嵌区块）。
    ///
    /// 【内边距】gd PanelContainer content_margin：Dark 16/12（sketch_panel.gd
    /// PANEL_PAD_*，与 StickTokens.SketchPanelPadX/Y 令牌同值）；Light 16/9
    /// （PAD_LIGHT_*）；紧凑 12/7（compact 小对话框）。Unity 侧容器不约束子件，
    /// 边距经 <see cref="ContentPadding"/> 暴露给装配方摆内容（样张/工厂同口径）。
    ///
    /// 【行为差异（有意）】gd 的 fill_override / outline_override / corner_radius 是
    /// 自绘底（SketchDraw.draw_panel）时代的参数，贴图皮肤下不适用——本侧不提供；
    /// 自绘异色底需求走 SketchWobbleGraphic（Fill + Outline 双层）另行组装。
    /// </summary>
    public sealed class SketchPanel : MonoBehaviour
    {
        /// <summary>深浅底预设：DARK = 大弹窗主底；LIGHT = HUD 横条/内嵌区块（gd Tone 同名）。</summary>
        public enum Tone
        {
            Dark,
            Light,
        }

        private Tone _tone = Tone.Dark;
        private UnityEngine.UI.Image _image;
        private SketchBoil _boil;

        /// <summary>深浅底（运行时可切，gd tone setter → queue_redraw 同义换槽）。</summary>
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

        /// <summary>建一块面板（根 + Sketch9Slice 九砖 + 本组件）。tone 缺省 Dark。</summary>
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

            // 单图 Tiled（spriteBorder=10 由导入器设置）：对齐 Godot StyleBoxTexture 的
            // TILE 语义（边角 1:1、框内平铺）；九砖切图只随旧集烘焙管线产出（夜海蓝），
            // 像素级复刻必须吃 StickWorld 新集整图帧，故弃 Sketch9Slice。
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.type = UnityEngine.UI.Image.Type.Tiled;
            image.raycastTarget = false;
            var boil = go.AddComponent<SketchBoil>();
            boil.BindStates(null);      // 静态件：无四态槽，仅帧轮换
            var panel = go.AddComponent<SketchPanel>();
            panel._image = image;
            panel._boil = boil;
            panel.Compact = false;
            panel._tone = tone;     // 直接写字段，Create 路径不重入 Apply 两次
            panel.Apply();
            return panel;
        }

        private void OnEnable() => Apply();

        /// <summary>换槽（panel / panel_light）；九砖首帧由 Sketch9Slice 自建（f0 由
        /// <see cref="SketchSkin.Frame"/> 取，edit 模式静态样张由 builder 反射直调 Build）。</summary>
        private void Apply()
        {
            string slot = _tone == Tone.Light ? "panel_light" : "panel";
            if (_image == null)
                _image = GetComponent<UnityEngine.UI.Image>();
            if (_image != null)
                _image.sprite = SketchSkin.Frame(slot, 0);
            if (_boil != null)
                _boil.Slot = slot;
        }
    }
}
