using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 文本渲染对比样张（stick-world UI 复刻 · Unity 侧基准图）：
    /// 程序化搭一个 1920×1080 的 8 档字号 TMP 样张场景并离屏渲染成 PNG，
    /// 与隔壁 Godot stick-world 的同规格基准图逐项对比（字号/摆位/配色/描边）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/文本样张/搭建 8 档字号样张场景（不落盘）
    ///   菜单: PirateCrew/文本样张/渲染样张 PNG（GUI 编辑器，不退出编辑器）
    ///   批处理: -executeMethod PirateCrew.EditorTools.TextSampleBuilder.CaptureSample
    ///           （渲染成功后 EditorApplication.Exit(0)，失败 Exit(非0)）
    ///
    /// 【产物】&lt;Unity 工程&gt;/export/text-sample/unity_1920x1080.png（覆盖写）
    ///   即 pirate-crew/export/（仓库根 .gitignore 的 export/ 规则任意层生效，不入库）。
    ///
    /// 【为什么不能走本项目的两条老出图路】
    ///   · 本机 batchmode 必须 -nographics（否则卡 GfxDevice），无渲染路径拿不到像素
    ///     ——见 OutlineDebugCapture / docs/技术/描边Shader调试.md 的实测结论；
    ///   · ScreenCapture.CaptureScreenshot 抓的是 Game View 当前帧，分辨率受 Game View
    ///     窗口尺寸限制，压不出"精确 1920×1080"；
    ///   · 故本工具走 **编辑器离屏单相机渲染**：RenderPipeline.SubmitRenderRequest +
    ///     URP SingleCameraRequest（URP 14 里 RenderSingleCamera 已标 Obsolete，本包
    ///     UniversalRenderPipeline.cs:547/2001 核实），同步渲到自定义 RenderTexture，
    ///     分辨率完全自定。前置条件 = **有图形界面的编辑器会话**（GPU 渲染路径可用）。
    ///
    /// 【为什么渲染时要临时把 Canvas 切 ScreenSpaceCamera】
    ///   ScreenSpaceOverlay Canvas 由引擎在帧末直绘 backbuffer，**不经过任何相机**，
    ///   离屏相机渲染拿不到它；ScreenSpaceCamera Canvas 才会作为相机的场景内容被渲染。
    ///   所以内存场景里 Canvas 常态保持 Overlay（与工程惯例一致，见 M3SceneSetup.CreateCanvas），
    ///   渲染期间临时切 S-S-C 挂临时正交相机，渲完恢复（场景不保存，无序列化残留）。
    ///   CanvasScaler = ScaleWithScreenSize 1920×1080，输出恰为参考分辨率 → 缩放系数 1，
    ///   两态像素摆位一致。
    ///
    /// 【材质实例，不动共享资产】
    ///   描边写在 tmp.fontMaterial（首次访问即实例化）上；**绝不能写 fontSharedMaterial**
    ///   ——那是 StickHand 字体资产的默认材质，全工程 UI 共用，改了会污染整套 UI。
    ///
    /// 【幂等】每次运行 EditorSceneManager.NewScene 重建内存场景（会丢弃当前打开的
    ///   未保存场景——与 M2/M3SceneSetup 同纪律），PNG 覆盖写，RT/相机用后即毁。
    /// </summary>
    public static class TextSampleBuilder
    {
        // ------------------------------------------------------------------
        // 样张规格（与 Godot 基准图逐项对齐，勿单方面改动；对不上先怀疑 Godot 侧）
        // ------------------------------------------------------------------

        const int CanvasWidth = 1920;
        const int CanvasHeight = 1080;

        /// <summary>8 档字号，索引 i=0..7，与 Godot 基准 [44,36,24,17,15,14,12,11] 一致。</summary>
        static readonly int[] FontSizes = { 44, 36, 24, 17, 15, 14, 12, 11 };

        /// <summary>行距（anchoredPosition.y 每 -125 一行）与首行边距 40，同 Godot 基准。</summary>
        const float RowStep = 125f;
        const float Margin = 40f;

        /// <summary>统一文案（含中英混排/数字/标点/破折号，覆盖字形覆盖面检查）。</summary>
        const string SampleText = "火柴人大战略 StickWorld 按E敲击建造 0123456789 ——木石金沥青！";

        /// <summary>背景纯色（近黑深海蓝），同 Godot 基准。</summary>
        static readonly Color BgColor = new Color(0.012f, 0.014f, 0.02f, 1f);

        /// <summary>正文色（近白冷灰），同 Godot 基准。</summary>
        static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f, 1f);

        /// <summary>墨描边色（近黑暖褐），同 Godot 基准。</summary>
        static readonly Color OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f);

        /// <summary>
        /// TMP 描边宽度初值 0.2（材质属性 Range(0,1)）。
        /// 【待校准】TMP 的 _OutlineWidth 是归一化值，像素粗细 = _OutlineWidth × 0.5 ×
        /// "每张图集纹素的屏幕像素数"（见 TMP_SDF_SSD.cginc:73 param.z 与 :105-110 的 SDF
        /// 距离场公式），随渲染字号等比变粗——与 Godot outline_size=3px 的固定像素语义
        /// 不是一套量纲，无直接换算式。首图出来后与 Godot 基准并排目测校准本值。
        /// </summary>
        const float OutlineWidthInitial = 0.2f;

        const string FontResourcePath = "Fonts/StickHand-Regular SDF";
        const string FontAssetPath = "Assets/Art/Fonts/StickHand-Regular SDF.asset";

        const string OutputRelativeDir = "export/text-sample";
        const string OutputFileName = "unity_1920x1080.png";

        // ------------------------------------------------------------------
        // 菜单 / 批处理入口
        // ------------------------------------------------------------------

        /// <summary>在内存里搭样张场景（不保存、不落盘）。菜单直接看摆位用。</summary>
        [MenuItem("PirateCrew/文本样张/搭建 8 档字号样张场景（不落盘）", priority = 10)]
        public static void BuildSampleScene()
        {
            TryBuildSampleScene();
        }

        /// <summary>搭样张场景；字体缺失等致命问题时返回 false（调用方决定报错/退出方式）。</summary>
        static bool TryBuildSampleScene()
        {
            EditorSceneManager.SaveOpenScenes(); // GUI 模式下场景 dirty 会弹模态保存框挂起批处理
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Canvas canvas = CreateSampleCanvas();
            CreateBackground(canvas.transform);

            TMP_FontAsset font = LoadFontAsset();
            if (font == null)
            {
                Debug.LogError("[TextSampleBuilder] 找不到 StickHand TMP 字体资产（Resources: \""
                    + FontResourcePath + "\" 与 " + FontAssetPath + " 均为空）。请先跑 "
                    + "PirateCrew/Fonts/生成 TMP 中文字体资产。");
                return false;
            }

            for (int i = 0; i < FontSizes.Length; i++)
                CreateTextRow(canvas.transform, font, i);

            Canvas.ForceUpdateCanvases();
            Debug.Log("[TextSampleBuilder] 样张场景已搭好（内存，不保存）：Canvas 1920×1080 Overlay，"
                + FontSizes.Length + " 行 TMP（字号 " + string.Join("/", System.Array.ConvertAll(FontSizes, s => s.ToString()))
                + "），字体 " + font.name + "。");
            return true;
        }

        /// <summary>渲染样张 PNG，成功后退出编辑器（-executeMethod 专用入口）。</summary>
        public static void CaptureSample()
        {
            CaptureInternal(exitWhenDone: true);
        }

        /// <summary>渲染样张 PNG，保留编辑器会话（菜单用）。</summary>
        [MenuItem("PirateCrew/文本样张/渲染样张 PNG（GUI 编辑器，不退出编辑器）", priority = 11)]
        public static void CaptureSampleInteractive()
        {
            CaptureInternal(exitWhenDone: false);
        }

        // ------------------------------------------------------------------
        // 渲染主流程（edit mode 离屏单相机）
        // ------------------------------------------------------------------

        static void CaptureInternal(bool exitWhenDone)
        {
            // 前置检查沿用 OutlineDebugCapture 的纪律：无渲染路径时明确报错，
            // 不静默失败、不生成占位图/空白图。
            string unavailableReason = DetectUnavailableRenderPath();
            if (unavailableReason != null)
            {
                Debug.LogError("[TextSampleBuilder] 当前会话没有可用渲染路径，无法渲染样张。原因："
                    + unavailableReason + "\n  请改用**有图形界面的 Unity 编辑器**运行：\n"
                    + "    菜单 PirateCrew/文本样张/渲染样张 PNG，或\n"
                    + "    \"F:/Unity/2022.3.62f1c1/Editor/Unity.exe\" -projectPath <Unity工程> "
                    + "-executeMethod PirateCrew.EditorTools.TextSampleBuilder.CaptureSample -logFile -\n"
                    + "  注意：不要带 -batchmode/-nographics（本机该模式没有 GfxDevice）。");
                if (exitWhenDone)
                    EditorApplication.Exit(1);
                return;
            }

            if (!TryBuildSampleScene())
            {
                if (exitWhenDone)
                    EditorApplication.Exit(1);
                return;
            }

            string outputPath = GetOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            Canvas canvas = Object.FindObjectOfType<Canvas>();
            TextMeshProUGUI[] rows = Object.FindObjectsOfType<TextMeshProUGUI>();

            // 动态字体图集暖场：ForceMeshUpdate 促成缺字（含 fallback 霞鹜文楷）同步入图集。
            Canvas.ForceUpdateCanvases();
            foreach (TextMeshProUGUI row in rows)
                row.ForceMeshUpdate();

            // 临时正交相机 + Canvas 切 S-S-C（Overlay 不经相机，离屏拿不到；见类注释）。
            Camera captureCamera = CreateCaptureCamera();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = captureCamera;
            canvas.planeDistance = 100f;

            RenderTexture rt = RenderTexture.GetTemporary(CanvasWidth, CanvasHeight, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                // 连续渲两遍取第二张：首遍后动态图集/网格才完全稳定（任务口径"两帧取第二帧"）。
                for (int pass = 0; pass < 2; pass++)
                {
                    Canvas.ForceUpdateCanvases();

                    // URP 14 官方离屏单相机渲染（RenderSingleCamera 已 Obsolete）：
                    // 同步渲到 request.destination。SubmitRenderRequest 是 void、不回传受理状态，
                    // 失败与否靠下方"全零帧探针 + 产物校验"兜底（背景图铺满，成功帧绝非全零）。
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    RenderPipeline.SubmitRenderRequest(captureCamera, request);

                    if (pass == 0)
                        continue;

                    Texture2D snapshot = ReadRtToTexture(rt);
                    if (IsBlankFrame(snapshot))
                    {
                        Debug.LogError("[TextSampleBuilder] 离屏渲染结果为全零帧（当前渲染管线未受理 "
                            + "SingleCameraRequest 或无渲染路径），拒绝写出假图。");
                        Object.DestroyImmediate(snapshot);
                        if (exitWhenDone)
                            EditorApplication.Exit(1);
                        return;
                    }
                    File.WriteAllBytes(outputPath, snapshot.EncodeToPNG());
                    Object.DestroyImmediate(snapshot);
                }
            }
            finally
            {
                // 恢复现场：Canvas 回 Overlay、毁临时相机、还 RT（内存场景不保存，无序列化残留）。
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                Object.DestroyImmediate(captureCamera.gameObject);
                RenderTexture.ReleaseTemporary(rt);
            }

            // 产物校验（沿用 ArtReviewCapture：存在且非空才算成功）。
            FileInfo info = new FileInfo(outputPath);
            if (!info.Exists || info.Length == 0)
            {
                Debug.LogError("[TextSampleBuilder] 渲染完成但产物缺失/为空: " + outputPath);
                if (exitWhenDone)
                    EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[TextSampleBuilder] 样张已输出: " + info.FullName + "（" + info.Length + " 字节，"
                + CanvasWidth + "×" + CanvasHeight + "）。请与 Godot 基准图并排对比；描边宽度初值 "
                + OutlineWidthInitial + " 待目测校准。");

            if (exitWhenDone)
                EditorApplication.Exit(0);
        }

        static string DetectUnavailableRenderPath()
        {
            if (Application.isBatchMode)
                return "Application.isBatchMode == true（批处理模式）";
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return "SystemInfo.graphicsDeviceType == Null（无图形设备，通常是 -nographics）";
            if (string.IsNullOrEmpty(SystemInfo.graphicsDeviceVersion))
                return "SystemInfo.graphicsDeviceVersion 为空（图形驱动未初始化）";
            return null;
        }

        /// <summary>RT → sRGB Texture2D → PNG 字节（坐标系：ReadPixels/EncodeToPNG 内部已对齐，无需翻转）。</summary>
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

        /// <summary>
        /// 全零帧探针：背景图铺满全屏且不透明，正常渲染帧不可能逐像素全 0。
        /// 采样网格上全为 0 即判定管线没真正渲（沿用"不静默失败、不写假图"纪律）。
        /// </summary>
        static bool IsBlankFrame(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            const int stride = 64; // 每 64 像素取一点，网格覆盖全图
            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color32 c = pixels[i];
                if (c.r != 0 || c.g != 0 || c.b != 0 || c.a != 0)
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 场景搭建
        // ------------------------------------------------------------------

        /// <summary>Canvas：Overlay + ScaleWithScreenSize 1920×1080（缩放系数恰为 1）。</summary>
        static Canvas CreateSampleCanvas()
        {
            var go = new GameObject("TextSampleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasWidth, CanvasHeight);
            // 对齐 Godot canvas_items+expand 口径（Expand(1)；采样画布渲染尺寸=参考分辨率时缩放恒 1，输出不受影响）。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            return canvas;
        }

        static void CreateBackground(Transform parent)
        {
            var go = new GameObject("Background", typeof(Image));
            go.transform.SetParent(parent, false);

            Image bg = go.GetComponent<Image>();
            bg.color = BgColor;
            bg.raycastTarget = false;

            RectTransform rect = bg.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 行 i：anchor 左上角、pivot(0,1)、anchoredPosition=(40, -(40+i×125))——与 Godot
        /// 基准摆位逐像素一致。宽 1840 = 1920 - 左右各 40，仅作排版容器，不裁字。
        /// </summary>
        static void CreateTextRow(Transform parent, TMP_FontAsset font, int index)
        {
            int fontSize = FontSizes[index];
            var go = new GameObject("Text_" + fontSize + "px_Row" + index, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = SampleText;
            tmp.fontSize = fontSize;
            tmp.color = TextColor;
            tmp.alignment = TextAlignmentOptions.TopLeft; // 同 Godot Label 左上起排
            tmp.enableWordWrapping = false;               // 单行样张，禁换行
            tmp.raycastTarget = false;
            tmp.margin = Vector4.zero;

            RectTransform rect = tmp.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(CanvasWidth - Margin * 2f, RowStep);
            rect.anchoredPosition = new Vector2(Margin, -(Margin + index * RowStep));

            // 墨描边（TMP outline 走材质）：写实例材质，绝不碰共享的 StickHand 默认材质。
            Material mat = tmp.fontMaterial; // 首次访问即实例化
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline); // "OUTLINE_ON"
            mat.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColor);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidthInitial);
        }

        /// <summary>
        /// 临时正交相机：orthographicSize=540 → 视口高 1080 世界单位，S-S-C Canvas 在
        /// planeDistance 处恰好充满视口。disabled 状态不参与编辑器每帧渲染，
        /// 只被 SubmitRenderRequest 按需渲染。
        /// </summary>
        static Camera CreateCaptureCamera()
        {
            var go = new GameObject("TextSampleCaptureCamera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CanvasHeight / 2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BgColor; // 背景图铺满，此色只是兜底
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

        /// <summary>产物绝对路径：&lt;Unity 工程&gt;/export/text-sample/unity_1920x1080.png。</summary>
        static string GetOutputPath()
        {
            // Application.dataPath = .../pirate-crew/Assets，上溯一级到 Unity 工程根。
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, OutputRelativeDir, OutputFileName).Replace('\\', '/');
        }
    }
}
