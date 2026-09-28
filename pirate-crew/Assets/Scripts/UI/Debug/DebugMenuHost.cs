using PirateCrew.CharCamDebug;
using PirateCrew.Core;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 调试菜单宿主：主菜单「调试场景」按钮点开的那枚**可拖动启动器窗**（创始人 2026-09-25
    /// 流程：主菜单按钮 → 弹出可拖动菜单 → 菜单里一堆按钮 → 各自打开具体调试面板）。
    ///
    /// 面板四枚，全部可拖动、独立 × 关闭：①组件实摆（全交互）②New Sprite 对话框复刻
    /// ③Aseprite 菜单栏复刻（File/Edit/Sprite 真下拉）④部件陈列廊（theme 345 件全量，
    /// 数据驱动）。启动器本体 = 一列 SketchButton，再点「调试场景」收起。
    /// </summary>
    public static class DebugMenuHost
    {
        static RectTransform _root;
        static RectTransform _launcher;
        static RectTransform _widgetGallery;
        static RectTransform _newSprite;
        static RectTransform _menuBarDemo;
        static RectTransform _partsGallery;
        static RectTransform _dupDialog;
        static RectTransform _gotoDialog;

        /// <summary>主菜单「调试场景」入口：开/收启动器（首次点开时整树构建）。</summary>
        public static void Toggle(Transform canvas)
        {
            if (_root == null)
                Build(canvas);
            bool show = !_launcher.gameObject.activeSelf;
            _root.SetAsLastSibling();
            _launcher.gameObject.SetActive(show);
        }

        static void Build(Transform canvas)
        {
            var rootGo = new GameObject("DebugMenuHost", typeof(RectTransform));
            _root = rootGo.GetComponent<RectTransform>();
            _root.SetParent(canvas, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            _launcher = DebugWindowKit.CreateWindow(_root, "DebugLauncher", "调试菜单",
                new Vector2(20f, 44f), new Vector2(150f, 144f), closeButton: false);

            float y = DebugWindowKit.ContentTopOf(_launcher);
            // 四窗默认位错开排布（画布 960×540）：陈列廊右大块 / 对话框与菜单栏右列上下，
            // 启动器左列——默认位互不压盖，全幅陈列廊例外（打开即覆盖、× 即还原）。
            // 首开一律延一帧（DebugBuildQueue）：按压反馈先落地，重构建不吞点击帧。
            y = MakeLauncherButton("组件实摆", y, () => ToggleWindow(_widgetGallery,
                () => { _widgetGallery = WidgetGalleryPanel.Build(_root, new Vector2(170f, 26f)); _widgetGallery.SetAsLastSibling(); }));
            y = MakeLauncherButton("新建精灵（xml 装载）", y, () => ToggleWindow(_newSprite,
                () => { _newSprite = LoadNewSprite(new Vector2(510f, 26f)); _newSprite.SetAsLastSibling(); }));
            y = MakeLauncherButton("Aseprite 菜单栏", y, () => ToggleWindow(_menuBarDemo,
                () => { _menuBarDemo = BuildMenuBarDemo(new Vector2(510f, 250f)); _menuBarDemo.SetAsLastSibling(); }));
            y = MakeLauncherButton("库对话框 Duplicate/Goto", y, () =>
            {
                ToggleWindow(_dupDialog,
                    () => { _dupDialog = LoadDialog("duplicate_sprite", new Vector2(510f, 330f)); WindowDragger.RaiseToCanvasTop(_dupDialog); });
                ToggleWindow(_gotoDialog,
                    () => { _gotoDialog = LoadDialog("goto_frame", new Vector2(510f, 430f)); WindowDragger.RaiseToCanvasTop(_gotoDialog); });
            });
            y = MakeLauncherButton("部件陈列廊", y, () => ToggleWindow(_partsGallery,
                () => { _partsGallery = BuildPartsGalleryWindow(new Vector2(170f, 26f)); WindowDragger.RaiseToCanvasTop(_partsGallery); }));
            // 3D 调试场入口：跳 PixelartCharCamDebug（像素化路径下调角色体格与镜头取景）。
            // 走 ChangeScene 事件 = 标准场景流转通道（SceneLoader 把主菜单记进返回栈，
            // 调试场里的「返回主菜单」走 GoBack 弹栈）。
            MakeLauncherButton("角色镜头调试场", y, () =>
                EventBus.Publish(SceneEvents.ChangeScene, CharCamDebugController.SceneName));

            // Build 完先收起：首开走 Toggle 的 show 分支（旧版建好即激活，第一次点
            // 调试场景反而把它藏了——「要点击两下才能点开」的根因）。
            _launcher.gameObject.SetActive(false);
        }

        static float MakeLauncherButton(string label, float y, System.Action onClick)
        {
            SketchButton button = SketchButton.Create(_launcher, "Open_" + label,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(DebugWindowKit.Pad, -y),
                new Vector2(138f, 16f), DebugWindowKit.HandFont, label, UiSkin.Font.Tiny);
            button.onClick.AddListener(() => onClick());
            return y + 16f + 4f;
        }

        /// <summary>开/合一枚面板窗（首开延一帧构建——闭包自赋值自置顶；再点切换显隐）。</summary>
        static void ToggleWindow(RectTransform window, System.Action build)
        {
            if (window == null)
            {
                DebugBuildQueue.Ensure(_root).RunNextFrame(build);
                return;
            }
            bool show = !window.gameObject.activeSelf;
            window.gameObject.SetActive(show);
            if (show)
            {
                WindowDragger.RaiseToCanvasTop(window);
                AseMenuKit.CloseAll();
            }
        }

        /// <summary>装载一个库对话框（返回窗根；装载器细节见 AseDialogLoader）。</summary>
        static RectTransform LoadDialog(string widget, Vector2 topLeft)
        {
            AseDialogLoader.Result r = AseDialogLoader.Load(widget, _root, topLeft);
            return r != null ? r.Window : null;
        }

        /// <summary>装载 data/widgets/new_sprite.xml（cmd 层接线，同 cmd_new_file.cpp 分工：
        /// 默认值 32×32；advanced 勾选 → advanced 盒显隐 + 重排收窗高）。</summary>
        static RectTransform LoadNewSprite(Vector2 topLeft)
        {
            AseDialogLoader.Result result = AseDialogLoader.Load("new_sprite", _root, topLeft);
            if (result == null)
                return null;
            TMPro.TMP_InputField width = result.Get<TMPro.TMP_InputField>("width");
            TMPro.TMP_InputField height = result.Get<TMPro.TMP_InputField>("height");
            if (width != null) width.text = "32";
            if (height != null) height.text = "32";
            Button advancedCheck = result.Get<Button>("advanced_check");
            if (advancedCheck != null)
            {
                result.SetHidden("advanced", true);   // aseprite 默认未勾选 → 盒隐藏且不占高
                advancedCheck.onClick.AddListener(() =>
                    result.SetHidden("advanced", !result.IsHidden("advanced")));
            }
            return result.Window;
        }

        // ------------------------------------------------------------------

        /// <summary>Aseprite 菜单栏复刻窗：File / Edit / Sprite 真下拉（分隔线/快捷键/
        /// 勾选/子菜单），点选在内容区回显。</summary>
        static RectTransform BuildMenuBarDemo(Vector2 topLeft)
        {
            RectTransform window = DebugWindowKit.CreateWindow(_root, "MenuBarDemo",
                "Aseprite 菜单栏复刻", topLeft, new Vector2(300f, 120f));

            TextMeshProUGUI feedback = DebugWindowKit.PlaceLabel(window, "（点菜单项在这里回显）",
                UiSkin.Font.Tiny, PixelSkin.Theme.StatusText, DebugWindowKit.Pad,
                DebugWindowKit.ContentTopOf(window) + 20f, 260f);

            System.Action<string> echo = path => feedback.text = path;

            var menus = new (string, AseMenuKit.Item[])[]
            {
                ("File", new AseMenuKit.Item[]
                {
                    // New… 走真弹窗（xml 装载器 + cmd 层接线，与启动器第 2 钮同一开合件）
                    AseMenuKit.Item_("New…", "Ctrl+N", () =>
                    {
                        echo("File → New…");
                        ToggleWindow(_newSprite,
                            () => { _newSprite = LoadNewSprite(new Vector2(510f, 26f)); _newSprite.SetAsLastSibling(); });
                    }),
                    AseMenuKit.Item_("Open…", "Ctrl+O", () => echo("File → Open…")),
                    AseMenuKit.Item_("Open Recent", null, null, false, new AseMenuKit.Item[]
                    {
                        AseMenuKit.Item_("（最近文件空）", action: () => echo("File → Open Recent")),
                    }),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Save", "Ctrl+S", () => echo("File → Save")),
                    AseMenuKit.Item_("Save As…", "Ctrl+Shift+S", () => echo("File → Save As…")),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Close File", "Ctrl+W", () => echo("File → Close File")),
                    AseMenuKit.Item_("Exit", "Alt+F4", () => echo("File → Exit")),
                }),
                ("Edit", new AseMenuKit.Item[]
                {
                    AseMenuKit.Item_("Undo", "Ctrl+Z", () => echo("Edit → Undo")),
                    AseMenuKit.Item_("Redo", "Ctrl+Shift+Z", () => echo("Edit → Redo")),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Cut", "Ctrl+X", () => echo("Edit → Cut")),
                    AseMenuKit.Item_("Copy", "Ctrl+C", () => echo("Edit → Copy")),
                    AseMenuKit.Item_("Paste", "Ctrl+V", () => echo("Edit → Paste")),
                    AseMenuKit.Item_("Clear", "Del", () => echo("Edit → Clear")),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Preferences…", "Ctrl+K", () => echo("Edit → Preferences…")),
                }),
                ("Sprite", new AseMenuKit.Item[]
                {
                    AseMenuKit.Item_("Sprite Properties…", action: () => echo("Sprite → Properties")),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Color Mode", null, null, false, new AseMenuKit.Item[]
                    {
                        AseMenuKit.Item_("RGB", check: true, action: () => echo("Color Mode → RGB")),
                        AseMenuKit.Item_("Grayscale", action: () => echo("Color Mode → Grayscale")),
                        AseMenuKit.Item_("Indexed", action: () => echo("Color Mode → Indexed")),
                    }),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Duplicate…", "Ctrl+U", () => echo("Sprite → Duplicate")),
                    AseMenuKit.Item_("Crop Sprite", action: () => echo("Sprite → Crop")),
                    AseMenuKit.Item_("Trim", "Ctrl+T", () => echo("Sprite → Trim")),
                }),
                ("View", new AseMenuKit.Item[]
                {
                    AseMenuKit.Item_("Preview", "F5", () => echo("View → Preview")),
                    AseMenuKit.Sep(),
                    AseMenuKit.Item_("Show Grid", "Ctrl+’", () => echo("View → Show Grid"), check: true),
                    AseMenuKit.Item_("Snap to Grid", "Shift+S", () => echo("View → Snap to Grid")),
                }),
            };

            RectTransform bar = AseMenuKit.BuildMenuBar(window, "AseMenuBar", menus);
            bar.anchoredPosition = new Vector2(DebugWindowKit.Pad, -DebugWindowKit.ContentTopOf(window));
            return window;
        }

        /// <summary>部件陈列廊窗（theme 345 件全量，数据驱动）：竖向滚动 + theme 滚动条。
        /// 窗宽 784 起于启动器右侧（x=170）——全幅 940 会从 x=120 越出 960 画布右缘且整片
        /// 盖住启动器；列数由滚动区宽自适应（PartsGalleryPage.Build）。</summary>
        static RectTransform BuildPartsGalleryWindow(Vector2 topLeft)
        {
            RectTransform window = DebugWindowKit.CreateWindow(_root, "PartsGalleryWindow",
                "部件陈列廊（theme.xml 全 345 件）", topLeft, new Vector2(784f, 540f - 60f));
            Vector2 windowSize = window.sizeDelta;

            // 滚动视图 = theme view 整装：window_face 底 + sunken 边框，12 宽条**按需**出在
            // 框内右缘并挤窄视口（scroll_helper.cpp:75-81）——源里没有「框外独立条」的组合
            AseView scroll = AseWidgetKit.ScrollView(window, "Scroll",
                DebugWindowKit.Pad, DebugWindowKit.ContentTopOf(window),
                windowSize.x - DebugWindowKit.Pad * 2f,
                windowSize.y - DebugWindowKit.ContentTopOf(window) - DebugWindowKit.Pad);

            RectTransform content = UiKit.CreateRect("Content", scroll.Viewport);
            UiKit.SetAnchored(content, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            // 陈列廊列数自适应：可用宽 = 视口宽（框 3+3 与条 12 都在框内）。
            // **分帧构建**（345 格不分帧会整秒吞帧，实机走查「点下去一会才有反应」的
            // 元凶之一）——content 高度随建随长。
            float galleryWidth = windowSize.x - DebugWindowKit.Pad * 2f
                - AseWidgetKit.ViewBorderLeft - AseWidgetKit.ViewBorderRight
                - AseLayout.ScrollbarSize;

            scroll.AttachToView(content);

            // 分帧铺 345 格：每步回告累计高，sizeHint 与滚动几何随建随长（源在
            // Viewport 尺寸变化时 updateView，此处按步喂）
            DebugBuildQueue.Ensure(window.parent).RunSteps(
                PartsGalleryPage.BuildSteps(content, 0f, galleryWidth, total =>
                {
                    scroll.SetContentHint((int)galleryWidth, (int)total);
                    scroll.UpdateView();
                }));
            return window;
        }
    }
}
