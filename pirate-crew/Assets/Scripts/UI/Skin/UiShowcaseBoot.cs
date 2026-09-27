using PirateCrew.Core;
using PirateCrew.UI.DebugUi;
using PirateCrew.UI.Stick;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 组件展示**实机调试窗口**的场景引导（<c>Assets/Scenes/UIShowcase.unity</c> 唯一职责）：
    /// 建 overlay Canvas + 纵向滚动的 <see cref="PartsGalleryPage"/>（Aseprite 直切件全量陈列廊）
    /// + "返回总览"小钮。悬停/按压/页签切换全部真交互（创始人 2026-09-22："做成游戏内展示，
    /// 专门开一个实机调试窗口"；后续走查加码："字体用像素字体、件按 3:1 栅格"）。
    ///
    /// 【怎么开】编辑器菜单 PirateCrew/UI/打开组件展示（构建并播放），或直接播放
    /// UIShowcase 场景。场景**自足**：像素图集走 Resources（<see cref="PixelSkin"/>）、
    /// 像素字体走 Resources/Fonts/FusionPixel12-px，不依赖 Bootstrapper 的全局服务；
    /// "返回总览"在有服务时走 <see cref="SceneLoader"/>，直开本场景（没有服务）时回
    /// 引导场景重建（引导会自动落到主菜单）。
    /// </summary>
    public sealed class UiShowcaseBoot : MonoBehaviour
    {
        const int Width = 1920;
        const int Height = 1080;

        void Start()
        {
            Screen.SetResolution(Width, Height, false);

            GameObject canvas = BuildCanvas();
            // CanvasScaler 的 scaleFactor 在自己的 Update 里才应用到 Canvas（Start 同帧
            // 读 stretch rect 拿到的是应用前的屏幕像素口径——实测 1920 而非 960），
            // 用它算列宽会把 16 列铺进 942 宽视口（后一半裁掉、横条误出）。延一帧，
            // 等 canvas 稳定到艺术像素口径再建滚动区。
            StartCoroutine(BuildAfterScalerSettles(canvas));
        }

        System.Collections.IEnumerator BuildAfterScalerSettles(GameObject canvas)
        {
            yield return null;
            BuildScroll(canvas.transform);
            BuildBackButton(canvas.transform);
        }

        /// <summary>overlay Canvas（与战斗 HUD / 采集链路同缩放口径）+ 全屏深底。</summary>
        static GameObject BuildCanvas()
        {
            var go = new GameObject("UiShowcaseCanvas", typeof(Canvas));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;   // 压过一切常规 UI

            var scaler = go.AddComponent<CanvasScaler>();
            // 【恒定像素密度（红警2 式）】与三画布工厂统一：1 画布单位 = Unit 屏幕像素。
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = PixelSkin.Unit;

            // 全屏深底：像素皮最暗档——组件在它们真实所属的深底上展示。
            RectTransform backdrop = UiKit.CreateRect("Backdrop", go.transform);
            UiKit.Stretch(backdrop);
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = PixelSkin.DarkOf(PixelTone.Frame);
            backdropImage.raycastTarget = false;
            return go;
        }

        /// <summary>
        /// 整页纵向滚动区 = theme <c>view</c> 组合（源 workspace_view extends view）：
        /// window_face 底 + sunken 边框（3/顶4），12 宽滚动条**按需**出在框内右缘、
        /// 视口被条挤窄（scroll_helper.cpp:75-81）——源里没有「框外独立条 + 缩窄视口」的组合。
        /// 滚轮 / 拖拇指 / 点轨道翻页（<see cref="AseView"/>）。
        /// </summary>
        static void BuildScroll(Transform canvas)
        {
            RectTransform view = UiKit.CreateRect("Scroll", canvas);
            UiKit.Stretch(view);
            AseWidgetKit.PaintViewSkin(view);
            AseView scroll = AseView.Attach(view,
                (int)AseWidgetKit.ViewBorderLeft, (int)AseWidgetKit.ViewBorderTop,
                (int)AseWidgetKit.ViewBorderRight, (int)AseWidgetKit.ViewBorderBottom);

            RectTransform content = UiKit.CreateRect("Content", scroll.Viewport);
            UiKit.SetAnchored(content, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            // Aseprite 直切件陈列廊是唯一内容页（旧 tone 族演示页随件族退役删除）。
            // 列宽按**出条后**的视口宽算（内容必高过一屏，竖条必出）。
            float viewportW = view.rect.width
                - AseWidgetKit.ViewBorderLeft - AseWidgetKit.ViewBorderRight
                - AseLayout.ScrollbarSize;
            float contentHeight = PartsGalleryPage.Build(content, 0f, viewportW);

            scroll.AttachToView(content);
            scroll.SetContentHint(Mathf.RoundToInt(viewportW), Mathf.RoundToInt(contentHeight));
            scroll.UpdateView();
        }

        /// <summary>返回主菜单（右上角小钮）：件 144×72 = 48×24 艺术像素（令牌按钮），
        /// 字号 36 = 12 艺术像素。第一版把按钮画成了 156×36（一半高）字号却 36——
        /// 文字顶满按钮还溢出（创始人 2026-09-23 走查"返回总览比窗口还大"）。</summary>
        void BuildBackButton(Transform canvas)
        {
            SketchButton back = SketchButton.Create(canvas, "BackToMenu",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-72f, -72f),
                new Vector2(144f, 72f),
                PartsGalleryPage.PixelFont(),
                "返回主菜单", 36);
            back.onClick.AddListener(GoBack);
        }

        static void GoBack()
        {
            if (SceneLoader.Instance != null)
                SceneLoader.Instance.ChangeScene(SceneNames.MainMenu);
            else
                SceneManager.LoadScene(SceneNames.Bootstrapper, LoadSceneMode.Single);
        }
    }
}
