using PirateCrew.Core;
using PirateCrew.UI.Stick;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
// SketchButtonKind 是 StickTokens 的嵌套类型（与 UiGalleryPage 同一别名手法）。
using SketchButtonKind = PirateCrew.UI.Stick.StickTokens.SketchButtonKind;

namespace PirateCrew.UI
{
    /// <summary>
    /// 组件展示**实机调试窗口**的场景引导（<c>Assets/Scenes/UIShowcase.unity</c> 唯一职责）：
    /// 建 overlay Canvas + 纵向滚动的 <see cref="PixelShowcasePage"/>（组件总表的**运行时真件版**）
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
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // 与各构建器统一

            // 全屏深底：像素皮最暗档——组件在它们真实所属的深底上展示。
            RectTransform backdrop = UiKit.CreateRect("Backdrop", go.transform);
            UiKit.Stretch(backdrop);
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = PixelSkin.DarkOf(PixelTone.Frame);
            backdropImage.raycastTarget = false;
            return go;
        }

        /// <summary>
        /// 纵向 ScrollRect：页面比一屏高（≈1140 艺术像素），滚轮 / 拖动 / 右侧像素滚动条。
        /// </summary>
        static void BuildScroll(Transform canvas)
        {
            RectTransform scrollRect = UiKit.CreateRect("Scroll", canvas);
            UiKit.Stretch(scrollRect);

            RectTransform viewport = UiKit.CreateRect("Viewport", scrollRect);
            UiKit.Stretch(viewport);
            viewport.offsetMin = new Vector2(0f, 0f);
            viewport.offsetMax = new Vector2(-PixelSkin.Unit * 5f, 0f);   // 右侧留出滚动条（含边距）
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = UiKit.CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            float contentHeight = PixelShowcasePage.Build(content);
            content.sizeDelta = new Vector2(0f, contentHeight);

            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;
            scroll.verticalScrollbar = BuildScrollbar(scrollRect, viewport);
        }

        /// <summary>像素滚动条：Track(Frame) 槽 + Plate(Light) 滑块，宽 12 艺术像素。</summary>
        static Scrollbar BuildScrollbar(RectTransform scrollRect, RectTransform viewport)
        {
            RectTransform bar = UiKit.CreateRect("VBar", scrollRect);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(PixelSkin.Unit * 4f, -PixelSkin.Unit * 2f);   // 上下各缩进 1u
            bar.anchoredPosition = new Vector2(-PixelSkin.Unit * 0.5f, 0f);

            RectTransform slidingArea = UiKit.CreateRect("SlidingArea", bar);
            UiKit.Stretch(slidingArea);
            slidingArea.offsetMin = Vector2.zero;
            slidingArea.offsetMax = Vector2.zero;
            var track = slidingArea.gameObject.AddComponent<Image>();
            track.sprite = PixelSkin.Track(PixelTone.Frame);
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;

            RectTransform handle = UiKit.CreateRect("Handle", slidingArea);
            UiKit.Stretch(handle);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = PixelSkin.Plate(PixelTone.Light);
            handleImage.type = Image.Type.Sliced;
            handleImage.color = Color.white;

            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleImage.rectTransform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        /// <summary>返回总览（右上角小钮，对齐 game-2 组件展示的版式）。</summary>
        void BuildBackButton(Transform canvas)
        {
            // 尺寸取 3 的整数倍（对齐像素栅格）：156×36 = 52×12 艺术像素。
            SketchButton back = SketchButton.Create(canvas, "BackToMenu",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f),
                new Vector2(156f, 36f),
                PixelShowcasePage.PixelFont(), SketchButtonKind.Dark,
                "返回总览", 36);   // 36 = 12 艺术像素（像素字体只认 12 的整数倍）
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
