using System.IO;
using System.Reflection;
using PirateCrew.UI;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
// SketchButtonKind 是 StickTokens 的嵌套类型，using static 提到顶层免逐处限定（同 SketchButton.cs）。
using static PirateCrew.UI.Stick.StickTokens;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 控件陈列样张（stick-world UI 复刻 P1 · Unity 侧基准图）：程序化搭一个 1920×1080
    /// 的 P1 控件陈列场景（6 变体按钮 / 面板 / 滑条 / 复选 / 开关 / 双向分隔线 / 双进度条）
    /// 并离屏渲染成 PNG，与隔壁 Godot stick-world 的同规格样张逐项 diff。
    ///
    /// 【摆位表】逐字对齐批次口径（勿单方面改动；对不上先怀疑 Godot 侧）：
    ///   画布 1920×1080，背景 WINDOW_BG 提 alpha=1 铺满
    ///   标题 "组件陈列 P1" 24 号 TEXT 色 INK 描边 @ 左上 (40,-16)
    ///   按钮行 y=80 高 44：6 变体 [Dark,Accent,Primary,Danger,Paper,IconSquare]
    ///     各宽 180，x=[40,240,440,640,840,1040]，文字"行动"字号 = sketch_button 默认（FONT_HUD）
    ///   面板 Dark 600×200 @ (40,160)，内文 "面板内文" 15 号 @ 面板内偏移 (16,12)
    ///   滑条 400×32 @ (40,400) value=0.4；复选 "选项一" @ (500,400) 勾选态；
    ///   开关 @ (640,400) 开态；竖分隔 2×60 @ (760,380)；横分隔 300×2 @ (820,415)
    ///   进度条(贴图槽) 400×20 @ (40,480) progress=0.6；
    ///   进度条(Painter 自绘 SketchBarGraphic) 400×20 @ (40,530) progress=0.75
    ///   注记 "P1 controls sample v1" 11 号 @ (40,-1040)
    ///   （y 坐标统一锚左上 pivot(0,1) 负向，与 TextSampleBuilder 同约定）
    ///
    /// 【入口】
    ///   菜单: PirateCrew/控件样张/搭建 P1 控件陈列场景（不落盘）
    ///   菜单: PirateCrew/控件样张/渲染样张 PNG（GUI 编辑器，不退出编辑器）
    ///   批处理: -executeMethod PirateCrew.EditorTools.ControlsSampleBuilder.CaptureSample
    ///           （渲染成功后 EditorApplication.Exit(0)，失败 Exit(非0)）
    /// 【产物】&lt;Unity 工程&gt;/export/controls-sample/unity_1920x1080.png（覆盖写）
    ///
    /// 【渲染路线】同 TextSampleBuilder：编辑器离屏单相机（URP SubmitRenderRequest +
    /// SingleCameraRequest，本机 batchmode 无 GfxDevice 不可用）、Canvas 临时切
    /// ScreenSpaceCamera、两遍渲染取第二帧、全零帧探针拒写假图、材质走实例不碰共享。
    ///
    /// 【edit 模式特例（对 P1 控件的静态帧口径）】Sketch9Slice 九砖靠协程延帧搭建、
    /// SketchBoil/SketchWobbleGraphic 沸腾靠 Update——两者在非播放态都不跑。离屏前：
    ///  · 反射直调 Sketch9Slice.Build()（首次构建无 Destroy 分支，edit 模式安全）；
    ///  · 贴图件由工厂直接铺 f0 帧、自绘件用 SeedBase 初相——样张取静态首帧，
    ///    与 Godot 基准图（同样单帧随机相位）同口径对照。
    /// 【幂等】每次 NewScene 重建内存场景（不保存），PNG 覆盖写，RT/相机用后即毁。
    /// </summary>
    public static class ControlsSampleBuilder
    {
        const int CanvasWidth = 1920;
        const int CanvasHeight = 1080;

        const string SampleCanvasName = "ControlsSampleCanvas";
        const string FontResourcePath = "Fonts/StickHand-Regular SDF";
        const string FontAssetPath = "Assets/Art/Fonts/StickHand-Regular SDF.asset";

        const string OutputRelativeDir = "export/controls-sample";
        const string OutputFileName = "unity_1920x1080.png";

        /// <summary>左上锚点（摆位表统一约定：anchor/pivot = (0,1)，y 负向）。</summary>
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        // ------------------------------------------------------------------
        // 菜单 / 批处理入口
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/控件样张/搭建 P1 控件陈列场景（不落盘）", priority = 20)]
        public static void BuildSampleScene()
        {
            TryBuildSampleScene();
        }

        [MenuItem("PirateCrew/控件样张/渲染样张 PNG（GUI 编辑器，不退出编辑器）", priority = 21)]
        public static void CaptureSampleInteractive()
        {
            CaptureInternal(exitWhenDone: false);
        }

        /// <summary>渲染样张 PNG，成功后退出编辑器（-executeMethod 专用入口）。</summary>
        public static void CaptureSample()
        {
            CaptureInternal(exitWhenDone: true);
        }

        // ------------------------------------------------------------------
        // 场景搭建（摆位表逐字）
        // ------------------------------------------------------------------

        static bool TryBuildSampleScene()
        {
            // 不 NewScene 不保存：GUI 模式下任何场景保存路径都会弹模态框挂起批处理
            // （NewScene 弹"是否保存"、SaveOpenScenes 对脏场景弹保存框——强杀恢复的
            // 脏场景必踩）。改为直接在当前场景内存里建样张 Canvas，渲完 DestroyImmediate，
            // 不落盘（幂等：同名 Canvas 先清）。当前场景若有 UI 也无碍：离屏相机只渲样张层。
            GameObject stale = GameObject.Find(SampleCanvasName);
            if (stale != null)
                Object.DestroyImmediate(stale);

            Canvas canvas = CreateSampleCanvas();

            TMP_FontAsset font = LoadFontAsset();
            if (font == null)
            {
                Debug.LogError("[ControlsSampleBuilder] 找不到 StickHand TMP 字体资产（Resources: \""
                    + FontResourcePath + "\" 与 " + FontAssetPath + " 均为空）。请先跑 "
                    + "PirateCrew/Fonts/生成 TMP 中文字体资产。");
                return false;
            }

            CreateBackground(canvas.transform);
            CreateTitle(canvas.transform, font);
            CreateButtonRow(canvas.transform, font);
            Debug.Log("[ControlsSampleBuilder] sub: button row done");
            CreatePanelWithText(canvas.transform, font);
            Debug.Log("[ControlsSampleBuilder] sub: panel done");
            CreateSliderRow(canvas.transform, font);
            Debug.Log("[ControlsSampleBuilder] sub: slider row done");
            CreateProgressBars(canvas.transform);
            Debug.Log("[ControlsSampleBuilder] sub: progress bars done");
            CreateNote(canvas.transform, font);
            Debug.Log("[ControlsSampleBuilder] sub: note done");

            Canvas.ForceUpdateCanvases();
            Debug.Log("[ControlsSampleBuilder] P1 控件陈列场景已搭好（内存，不保存）。");
            return true;
        }

        static void CreateBackground(Transform parent)
        {
            var go = new GameObject("Background", typeof(Image));
            go.transform.SetParent(parent, false);
            Image bg = go.GetComponent<Image>();
            Color c = StickTokens.WINDOW_BG;
            c.a = 1f; // 任务口径：WINDOW_BG 提 alpha=1 铺满
            bg.color = c;
            bg.raycastTarget = false;
            RectTransform rect = bg.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void CreateTitle(Transform parent, TMP_FontAsset font)
        {
            TextMeshProUGUI tmp = CreateText("Title", parent, "组件陈列 P1", font,
                StickTokens.FONT_TITLE, TextAlignmentOptions.TopLeft, StickTokens.TEXT);
            PlaceTopLeft(tmp.rectTransform, new Vector2(600f, 40f), new Vector2(40f, -16f));

            // 墨描边（TMP outline 走材质实例）：宽度初值同 SketchButton 描边口径
            Material mat = tmp.fontMaterial; // 首次访问即实例化
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, StickTokens.INK);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, SketchButton.TmpOutlineWidth);
        }

        static void CreateButtonRow(Transform parent, TMP_FontAsset font)
        {
            var kinds = new[]
            {
                SketchButtonKind.Dark, SketchButtonKind.Accent, SketchButtonKind.Primary,
                SketchButtonKind.Danger, SketchButtonKind.Paper, SketchButtonKind.IconSquare,
            };
            var xs = new[] { 40f, 240f, 440f, 640f, 840f, 1040f };
            for (int i = 0; i < kinds.Length; i++)
            {
                // 高 44（BTN_H_LG）、宽 180、字号 0 = 控件默认档（gd 主题 Button = FONT_HUD）
                SketchButton.Create(parent, "Btn_" + kinds[i], TopLeft, TopLeft,
                    new Vector2(xs[i], -80f), new Vector2(180f, 44f), font,
                    kinds[i], "行动", 0f);
            }
        }

        static void CreatePanelWithText(Transform parent, TMP_FontAsset font)
        {
            SketchPanel panel = SketchPanel.Create(parent, "Panel_Dark", TopLeft, TopLeft,
                new Vector2(40f, -160f), new Vector2(600f, 200f), SketchPanel.Tone.Dark);

            TextMeshProUGUI inner = CreateText("PanelText", panel.transform, "面板内文", font,
                StickTokens.FONT_BODY, TextAlignmentOptions.TopLeft, StickTokens.TEXT);
            // 面板内偏移 (16,12)：gd PANEL_PAD_* content margin 同源（ContentPadding 同值）
            PlaceTopLeft(inner.rectTransform, new Vector2(400f, 30f), new Vector2(16f, -12f));
        }

        static void CreateSliderRow(Transform parent, TMP_FontAsset font)
        {
            Debug.Log("[ControlsSampleBuilder] subsub: Slider.Create ...");
            SketchSlider.Create(parent, "Slider", TopLeft, TopLeft,
                new Vector2(40f, -400f), new Vector2(400f, 32f), 0.4f);
            Debug.Log("[ControlsSampleBuilder] subsub: Slider ok");

            Debug.Log("[ControlsSampleBuilder] subsub: Toggle.Create ...");
            SketchToggle.Create(parent, "Toggle", TopLeft, TopLeft,
                new Vector2(500f, -400f), new Vector2(140f, 24f), font, "选项一", true, 0f);
            Debug.Log("[ControlsSampleBuilder] subsub: Toggle ok");

            Debug.Log("[ControlsSampleBuilder] subsub: Switch.Create ...");
            SketchSwitch.Create(parent, "Switch", TopLeft, TopLeft,
                new Vector2(640f, -400f), new Vector2(34f, 18f), font, null, true, 0f);
            Debug.Log("[ControlsSampleBuilder] subsub: Switch ok");

            Debug.Log("[ControlsSampleBuilder] subsub: VSep.Create ...");
            SketchSeparator.Create(parent, "VSep", TopLeft, TopLeft,
                new Vector2(760f, -380f), new Vector2(2f, 60f), SketchSeparator.Direction.Vertical);
            Debug.Log("[ControlsSampleBuilder] subsub: VSep ok");

            Debug.Log("[ControlsSampleBuilder] subsub: HSep.Create ...");
            SketchSeparator.Create(parent, "HSep", TopLeft, TopLeft,
                new Vector2(820f, -415f), new Vector2(300f, 2f), SketchSeparator.Direction.Horizontal);
            Debug.Log("[ControlsSampleBuilder] subsub: HSep ok");
        }

        static void CreateProgressBars(Transform parent)
        {
            // 贴图槽版（progress_bg/progress_fill 九宫沸腾）
            SketchProgressBar texBar = SketchProgressBar.Create(parent, "ProgressTex", TopLeft, TopLeft,
                new Vector2(40f, -480f), new Vector2(400f, 20f));
            texBar.SetProgress(0.6f);

            // Painter 自绘版（SketchBarGraphic：扁平 quad + 1px 描边，progress_painter 同源）
            var go = new GameObject("ProgressPainter", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = TopLeft;
            rect.anchorMax = TopLeft;
            rect.pivot = TopLeft;
            rect.sizeDelta = new Vector2(400f, 20f);
            rect.anchoredPosition = new Vector2(40f, -530f);
            var bar = go.AddComponent<SketchBarGraphic>();
            bar.SetProgress(0.75f);
        }

        static void CreateNote(Transform parent, TMP_FontAsset font)
        {
            TextMeshProUGUI note = CreateText("Note", parent, "P1 controls sample v1", font,
                StickTokens.FONT_TINY, TextAlignmentOptions.TopLeft, StickTokens.TEXT_FAINT);
            PlaceTopLeft(note.rectTransform, new Vector2(400f, 20f), new Vector2(40f, -1040f));
        }

        // ------------------------------------------------------------------
        // 渲染主流程（edit mode 离屏单相机，同 TextSampleBuilder）
        // ------------------------------------------------------------------

        static bool _capturePrepared;
        static int _captureTick;
        static Canvas _captureCanvas;
        static Camera _captureCamera;
        static RenderTexture _captureRt;
        static string _captureOutputPath;
        static System.Collections.Generic.List<Canvas> _disabledCanvases;

        /// <summary>
        /// 渲染主流程 = 同步准备 + <see cref="EditorApplication.update"/> 等帧状态机。
        /// 【为什么不等帧不行】executeMethod 同步调用栈里，自定义 Graphic 的 CanvasRenderer
        /// 被引擎即时回收（建了即 fake-null），UGUI 网格重建不完整——历史出图路线
        /// （OutlineDebugCapture）同为 update 状态机等帧，让 UGUI 走完整生命周期后取证。
        /// 全程 try-catch：异常落日志并 Exit(1)，绝不悬死编辑器（烧 CPU 僵尸的根源）。
        /// </summary>
        static void CaptureInternal(bool exitWhenDone)
        {
            try
            {
                string unavailableReason = DetectUnavailableRenderPath();
                if (unavailableReason != null)
                {
                    Debug.LogError("[ControlsSampleBuilder] 当前会话没有可用渲染路径。原因：" + unavailableReason);
                    if (exitWhenDone) EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("[ControlsSampleBuilder] step0: ensuring StickWorld sprites imported ...");
                EnsureStickWorldSprites();
                Debug.Log("[ControlsSampleBuilder] step1: building sample scene ...");
                if (!TryBuildSampleScene())
                {
                    if (exitWhenDone) EditorApplication.Exit(1);
                    return;
                }
                Debug.Log("[ControlsSampleBuilder] step2: scene built; entering frame-tick state machine ...");

                _captureCanvas = GameObject.Find(SampleCanvasName).GetComponent<Canvas>();
                _disabledCanvases = new System.Collections.Generic.List<Canvas>();
                foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
                    if (c.gameObject != _captureCanvas.gameObject && c.enabled)
                    {
                        c.enabled = false;
                        _disabledCanvases.Add(c);
                    }
                _captureOutputPath = GetOutputPath();
                Directory.CreateDirectory(Path.GetDirectoryName(_captureOutputPath));
                _capturePrepared = true;
                _captureTick = 0;
                EditorApplication.update -= CaptureTick;   // 幂等防重入
                EditorApplication.update += CaptureTick;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ControlsSampleBuilder] prepare failed: " + e);
                if (exitWhenDone) EditorApplication.Exit(1);
            }
        }

        /// <summary>编辑器帧 tick：前 4 帧让 UGUI 正常建 CR/重建网格，第 5 帧取证渲染。</summary>
        static void CaptureTick()
        {
            _captureTick++;
            if (_captureTick < 5)
                return;
            try
            {
                Debug.Log("[ControlsSampleBuilder] step3: diag dump after " + _captureTick + " editor frames ...");
                foreach (UnityEngine.UI.Image img in _captureCanvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    Debug.Log("[diag][Image] " + img.transform.name + " sprite="
                        + (img.sprite ? img.sprite.name : "NULL") + " rect=" + img.rectTransform.rect.size);
                foreach (MaskableGraphic g in _captureCanvas.GetComponentsInChildren<MaskableGraphic>(true))
                {
                    if (g is UnityEngine.UI.Image || g is TextMeshProUGUI)
                        continue;
                    Mesh m = g.canvasRenderer != null ? g.canvasRenderer.GetMesh() : null;
                    Debug.Log("[diag][SelfDraw] " + g.GetType().Name + "@" + g.transform.name
                        + " rect=" + ((RectTransform)g.transform).rect.size
                        + " meshVerts=" + (m != null ? m.vertexCount : -1)
                        + " crAlive=" + (g.canvasRenderer != null));
                }

                TextMeshProUGUI[] texts = _captureCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                Canvas.ForceUpdateCanvases();
                foreach (TextMeshProUGUI text in texts)
                    text.ForceMeshUpdate();

                Debug.Log("[ControlsSampleBuilder] step4: rendering offscreen (2 passes) ...");
                _captureCamera = CreateCaptureCamera();
                _captureCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                _captureCanvas.worldCamera = _captureCamera;
                _captureCanvas.planeDistance = 100f;
                _captureRt = RenderTexture.GetTemporary(CanvasWidth, CanvasHeight, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                string errorReason = null;
                for (int pass = 0; pass < 2; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _captureRt };
                    RenderPipeline.SubmitRenderRequest(_captureCamera, request);
                    if (pass == 0)
                        continue;
                    Texture2D snapshot = ReadRtToTexture(_captureRt);
                    if (IsBlankFrame(snapshot))
                    {
                        errorReason = "离屏渲染结果为全零帧（渲染管线未受理 SingleCameraRequest），拒绝写出假图。";
                        Object.DestroyImmediate(snapshot);
                        break;
                    }
                    File.WriteAllBytes(_captureOutputPath, snapshot.EncodeToPNG());
                    Debug.Log("[ControlsSampleBuilder] step5: PNG written -> " + _captureOutputPath);
                    Object.DestroyImmediate(snapshot);
                }

                _captureCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _captureCanvas.worldCamera = null;
                Object.DestroyImmediate(_captureCamera.gameObject);
                RenderTexture.ReleaseTemporary(_captureRt);
                Object.DestroyImmediate(_captureCanvas.gameObject);   // 样张不落盘
                foreach (Canvas c in _disabledCanvases)
                    if (c != null)
                        c.enabled = true;

                if (errorReason != null)
                {
                    Debug.LogError("[ControlsSampleBuilder] " + errorReason);
                    EditorApplication.Exit(1);
                    return;
                }
                FileInfo info = new FileInfo(_captureOutputPath);
                if (!info.Exists || info.Length == 0)
                {
                    Debug.LogError("[ControlsSampleBuilder] 渲染完成但产物缺失/为空: " + _captureOutputPath);
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log("[ControlsSampleBuilder] 样张已输出: " + info.FullName + "（" + info.Length
                    + " 字节，" + CanvasWidth + "×" + CanvasHeight + "）。请与 Godot 基准图并排对比。");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ControlsSampleBuilder] capture tick failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        static string DetectUnavailableRenderPath()
        {
            if (Application.isBatchMode)
                return "Application.isBatchMode == true（批处理模式）";
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return "SystemInfo.graphicsDeviceType == Null（无图形设备，通常是 -nographics）";
            if (string.IsNullOrEmpty(SystemInfo.graphicsDeviceVersion))
                return "SystemInfo.graphicsDeviceVersion 为空（图形驱动未初始化）";
            return null;
        }

        static Texture2D ReadRtToTexture(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex;
        }

        static bool IsBlankFrame(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            const int stride = 64;
            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color32 c = pixels[i];
                if (c.r != 0 || c.g != 0 || c.b != 0 || c.a != 0)
                    return false;
            }
            return true;
        }

        static Camera CreateCaptureCamera()
        {
            var go = new GameObject("ControlsSampleCaptureCamera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CanvasHeight / 2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = StickTokens.WINDOW_BG; // 背景图铺满，此色只是兜底
            camera.cullingMask = ~0;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.enabled = false;
            return camera;
        }

        static TMP_FontAsset LoadFontAsset()
        {
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(FontResourcePath);
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            return font;
        }

        static string GetOutputPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, OutputRelativeDir, OutputFileName).Replace('\\', '/');
        }

        // ------------------------------------------------------------------
        // 摆放原语
        // ------------------------------------------------------------------

        /// <summary>Canvas：Overlay + ScaleWithScreenSize 1920×1080（缩放系数恰为 1）。</summary>
        /// <summary>同步进来的 png 默认导入为 Texture（非 Sprite），Resources.Load&lt;Sprite&gt;
        /// 全 null → 贴图件白块。此处批量把 StickWorld 资产设为 Sprite 导入（幂等，首跑重导入一次）。</summary>
        static void EnsureStickWorldSprites()
        {
            string root = "Assets/Resources/UI/StickWorld";
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            int fixedCount = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                if (path.Contains("/sketch/") && (path.Contains("/panel_f") || path.Contains("/panel_light_f")))
                {
                    // 九宫格边距 = 烘焙 MARGIN=10（sketch_textures.gd texture_margin 同源）；
                    // 对已导入为 Sprite 的旧条目也要补（无条件确保）
                    var want = new Vector4(10f, 10f, 10f, 10f);
                    if (importer.spriteBorder != want)
                    {
                        importer.spriteBorder = want;
                        importer.SaveAndReimport();
                        fixedCount++;
                    }
                    continue;
                }
                if (importer.textureType == TextureImporterType.Sprite)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                fixedCount++;
            }
            Debug.Log("[ControlsSampleBuilder] step0: sprites ensured, reimported " + fixedCount
                + " / total " + guids.Length);
        }

        static Canvas CreateSampleCanvas()
        {
            var go = new GameObject(SampleCanvasName, typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasWidth, CanvasHeight);
            // Expand 对齐 Godot canvas_items+expand（C 批次 CanvasScaler 契约；1920×1080 恒等缩放）
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return canvas;
        }

        /// <summary>锚左上、pivot(0,1)、anchoredPosition=(x, -y) 摆放（摆位表统一约定）。</summary>
        static void PlaceTopLeft(RectTransform rect, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = TopLeft;
            rect.anchorMax = TopLeft;
            rect.pivot = TopLeft;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent, string content,
            TMP_FontAsset font, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            if (font != null)
                tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.margin = Vector4.zero;
            return tmp;
        }
    }
}
