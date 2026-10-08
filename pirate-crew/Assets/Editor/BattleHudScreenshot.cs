using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗截图器（创始人裁决 2026-10-05：视觉改动**先出图验收**，不再让人进游戏逐项看）。
    ///
    /// 【流程】进 Play（Battle 场景）→ 等帧稳定 → 抓帧 → 落 <c>&lt;仓库根&gt;/temp/hud-shots/</c> → 退 Play。
    /// 【抓法】世界层：主相机渲到 RenderTexture（1920×1080 固定）。UI 层：**所有根画布**切
    /// ScreenSpaceCamera + 专用 UI 相机渲到自己的透明底 RT → C# 按 alpha 合成。
    /// 【渲染器铁律】裸 UI 相机默认继承管线默认渲染器（像素化 Cast——专为 rig 铸图，单独跑
    /// 输出全空屏），必须改挂 rig 叠加相机同款 <c>PixelartOverlay</c> 渲染器（标准前向 +
    /// 只画透明队列，索引读 rig 的公开字段 <c>overlayRendererIndex</c>）。
    /// 【域重载存活】进入 Play 会触发域重载，静态字段清零、update 订阅解绑——计数器全放
    /// <see cref="SessionState"/>（跨重载存活、随编辑器会话消亡），<c>[InitializeOnLoad]</c>
    /// 静态构造检测到"armed"就重新挂 Tick。
    /// 【运行方式】菜单 PirateCrew/Debug/战斗截图（编辑器手按）；无头 -executeMethod
    /// <see cref="CaptureHeadless"/>（**必须带图形跑，禁 -nographics**；协调者在锁空闲时执行）。
    /// </summary>
    [InitializeOnLoad]
    public static class BattleHudScreenshot
    {
        const string ScenePath = "Assets/Scenes/Game/Battle.unity";
        const int ShotCount = 3;
        const int FirstShotDelayTicks = 90;   // 进场稳定（装配/首回合/相机落位）
        const int ShotIntervalTicks = 90;     // 缩放档切换后的平滑到位余量
        const string KeyArmed = "BattleHudScreenshot.Armed";
        const string KeyCountdown = "BattleHudScreenshot.Countdown";
        const string KeyShotsLeft = "BattleHudScreenshot.ShotsLeft";
        const string KeyIndex = "BattleHudScreenshot.Index";
        const string KeyHeadless = "BattleHudScreenshot.Headless";
        const string KeyCanvasPrepared = "BattleHudScreenshot.CanvasPrepared";
        const string KeyCaptureLevel = "BattleHudScreenshot.CaptureLevel";

        static string OutputDir =>
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..",
                System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_DIR") is string dir && dir.Length > 0
                    ? dir
                    : Path.Combine("temp", "hud-shots")));

        static bool Armed => SessionState.GetBool(KeyArmed, false);
        static int Countdown
        {
            get => SessionState.GetInt(KeyCountdown, 0);
            set => SessionState.SetInt(KeyCountdown, value);
        }
        static int ShotsLeft
        {
            get => SessionState.GetInt(KeyShotsLeft, 0);
            set => SessionState.SetInt(KeyShotsLeft, value);
        }
        static int ShotIndex
        {
            get => SessionState.GetInt(KeyIndex, 0);
            set => SessionState.SetInt(KeyIndex, value);
        }
        static bool Headless
        {
            get => SessionState.GetBool(KeyHeadless, false);
            set => SessionState.SetBool(KeyHeadless, value);
        }

        /// <summary>拍图目标关卡号（0 = 不覆写，走正常选关链/兜底关）。存 SessionState 跨域重载。</summary>
        static int CaptureLevel
        {
            get => SessionState.GetInt(KeyCaptureLevel, 0);
            set => SessionState.SetInt(KeyCaptureLevel, value);
        }

        static BattleHudScreenshot()
        {
            if (Application.isBatchMode && !Armed)
                return;
            if (Armed)
            {
                // 拍图关卡覆写：写 ArtReviewCaptureOverride（官方出图覆盖通道，消费点在
                // LevelSourceResolver 的 ① 级）。InitializeOnLoad 构造在 Play 域重载后、
                // 场景 Awake 前跑 ⇒ BattleController 解析关卡时已就位；Play 结束回编辑器域
                // 再重载即清零，不残留劫持后续选关。
                if (CaptureLevel > 0)
                    global::PirateCrew.ArtReview.ArtReviewCaptureOverride.LevelNumber = CaptureLevel;
                EditorApplication.update += Tick;   // 域重载后重新挂上（进入 Play 必触发重载）
            }
        }

        [MenuItem("PirateCrew/Debug/战斗截图（进 Play 抓取）")]
        public static void CaptureFromMenu() => Start(headless: false);

        /// <summary>无头入口：-executeMethod（带图形 batchmode）。
        /// 环境变量 <c>PC3D_CAPTURE_SCENE</c> 可换目标场景（如调试场景对比取景口径），
        /// 不设 = Battle。非 Battle 场景没有 BattleCameraDriver，缩放档自动跳过。</summary>
        public static void CaptureHeadless()
        {
            if (Armed)
                return;   // 已在进行中（不该发生在单次 -executeMethod 里，防重入）
            Start(headless: true);
        }

        static string ScenePathOrOverride =>
            System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_SCENE") is string s && s.Length > 0
                ? s
                : ScenePath;

        static void Start(bool headless)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[BattleHudScreenshot] 编辑器已在 Play 中，先退出再抓。");
                if (headless)
                    EditorApplication.Exit(1);
                return;
            }

            Headless = headless;
            ApplyEnvMaterialTuning();
            ShotsLeft = ShotCount;
            ShotIndex = 0;
            Countdown = FirstShotDelayTicks;
            // 拍图关卡（环境变量 PC3D_CAPTURE_LEVEL，如 3 = 天空之岛）：存 SessionState，
            // 由静态构造在 Play 域里写成关卡覆写（见其注释的时序说明）。
            CaptureLevel = System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_LEVEL") is string lvRaw
                && int.TryParse(lvRaw, out int lv) && lv > 0 ? lv : 0;
            SessionState.SetBool(KeyArmed, true);
            _framedShot = 0;
            _frameAttempts = 0;

            // 【口径对齐】像素管线的艺术画布 = 屏幕后台缓冲 ÷ pixelScale（PixelartPath 顶部口径）。
            // batchmode 的隐藏窗口只有几百像素高，不先撑到 1920×1080，抓出的图会比实机粗 3 倍
            // （实测块 9×6 而非 3×3——艺术画布跟着小后台缓冲走，再被拉伸到大 RT 上）。
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);

            EditorSceneManager.OpenScene(ScenePathOrOverride, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;   // 触发域重载 → 静态构造重挂 Tick
            EditorApplication.update += Tick;     // 重载前这几帧也直接跑（若该项目关了重载则不触发）
            Debug.Log("[BattleHudScreenshot] 已武装：进 Play 后自动抓 " + ShotCount + " 张 → " + OutputDir);
        }

        // ---- 出图取景覆写（PC3D_CAPTURE_CENTER / PC3D_CAPTURE_ORTHO）----
        static int _framedShot;      // 已对第 N 枪完成取景引导（N = ShotIndex + 1，域内静态即可）
        static int _frameAttempts;   // 引导重试计数（关卡源晚建时的等待上限）

        static bool CaptureCenterFraming =>
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_CENTER"));

        /// <summary>场地跨度 → 整图入画的 ortho：横向按 16:9 视口装下 spanX；纵向地面深度在
        /// 30° 俯角下按 sin 压缩后装下 spanZ；留 15% 边距；下限基准档、上限全景档封顶。</summary>
        static float OrthoForSpans(float spanX, float spanZ)
        {
            const float aspect = 16f / 9f;
            float pitchSin = Mathf.Sin(global::PirateCrew.Battle.CameraFraming.BasePitchDegrees * Mathf.Deg2Rad);
            float byWidth = spanX * 1.15f / (2f * aspect);
            float byDepth = spanZ * 1.15f * pitchSin / 2f;
            return Mathf.Min(
                Mathf.Max(byWidth, byDepth, global::PirateCrew.Battle.CameraFraming.CloseUpOrthoSize),
                global::PirateCrew.Battle.CameraFraming.PanoramaMaxOrthoSize);
        }

        /// <summary>
        /// 读 BattleController 的关卡源（SpanX/SpanZ，反射取私有字段——编辑器侧出图工具，
        /// 不为它给战斗组装层开公开口），把 Driver 取景覆写成「焦点 = 本关场地中心 +
        /// 整图入画 ortho」。菜单等非战斗场景返回 false（调用方按现状抓帧）。
        /// </summary>
        static bool TryApplyArenaCenterFraming()
        {
            var driver = Object.FindFirstObjectByType<global::PirateCrew.Battle.BattleCameraDriver>();
            if (driver == null)
                return false;
            var controller = Object.FindFirstObjectByType<global::PirateCrew.Battle.BattleController>();
            if (controller == null)
                return false;
            var sourceField = typeof(global::PirateCrew.Battle.BattleController).GetField(
                "_source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            object source = sourceField != null ? sourceField.GetValue(controller) : null;
            if (source == null)
                return false;
            var type = source.GetType();
            var fx = type.GetField("SpanX");
            var fz = type.GetField("SpanZ");
            if (fx == null || fz == null)
                return false;
            float spanX = (float)fx.GetValue(source);
            float spanZ = (float)fz.GetValue(source);
            float ortho = System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_ORTHO") is string oRaw
                && float.TryParse(oRaw, out float manualOrtho) && manualOrtho >= 1f
                ? manualOrtho
                : OrthoForSpans(spanX, spanZ);
            // 焦点默认在地面；PC3D_CAPTURE_FOCUS_Y 抬高焦点（高塔关卡整栋入画又不至于拉太远
            // 保像素密度——塔顶裁切是焦点太低，不是 ortho 一定不够）。
            float focusY = System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_FOCUS_Y") is string fyRaw
                && float.TryParse(fyRaw, out float fy) ? fy : 0f;
            var focus = new Vector3(spanX * 0.5f, focusY, spanZ * 0.5f);
            driver.ApplyCaptureFraming(focus, ortho);
            Debug.Log("[BattleHudScreenshot] 出图取景：场地中心 (" + focus.x.ToString("F1") + ", "
                + focus.z.ToString("F1") + ") y=" + focusY.ToString("F1")
                + "，span " + spanX.ToString("F0") + "×" + spanZ.ToString("F0")
                + "，ortho " + ortho.ToString("F1"));
            return true;
        }

        static void Tick()
        {
            if (!Armed || !EditorApplication.isPlaying)
                return;

            // 拍图档位覆盖（环境变量 PC3D_PIXEL_SCALE）：**每帧写**直到生效——Start 时 Play 还没起，
            // PixelScaleService.Install 的读档（batchmode 下能读到存档档位）会覆盖 ScaleDefault；
            // rig 下一帧 Update 按 Unit 自适应。1×（关像素化）不在设置域内，此处专供拍图对照。
            if (System.Environment.GetEnvironmentVariable("PC3D_PIXEL_SCALE") is string psRaw
                && int.TryParse(psRaw, out int psOverride) && psOverride >= 1 && psOverride <= 5
                && global::PirateCrew.Core.PixelScaleState.Unit != psOverride)
                global::PirateCrew.Core.PixelScaleState.Unit = psOverride;

            Countdown--;
            if (Countdown > 0)
                return;

            if (ShotsLeft <= 0)
            {
                Finish();
                return;
            }

            // 第一枪前先把 HUD 画布切到相机域并留几帧重建网格（当场切当场渲会渲出空 UI）。
            if (!SessionState.GetBool(KeyCanvasPrepared, false))
            {
                PrepareCanvasForCapture();
                DumpCrewScales();
                SessionState.SetBool(KeyCanvasPrepared, true);
                Countdown = 3;
                return;
            }

            // 【出图取景覆写】PC3D_CAPTURE_CENTER 非空 = 每枪抓帧前把视野指向本关场地中心
            //（场景烘的机位是装配那关的中心，关卡覆写拍别的关会偏心——云关实拍内容缩在
            // 画面左下就是它）；PC3D_CAPTURE_ORTHO 可指定整图入画的 ortho，不设则按场地
            // 跨度自适应。档值只服务出图，不回写任何常量（同 55a6155e 缩放档对比的纪律）。
            if (CaptureCenterFraming && _framedShot != ShotIndex + 1)
            {
                if (TryApplyArenaCenterFraming())
                {
                    _framedShot = ShotIndex + 1;
                    Countdown = 60;   // 焦点/ortho 平滑逼近余量
                    return;
                }
                if (++_frameAttempts < 120)
                {
                    Countdown = 1;   // 关卡源还没建好，等它（最多再等 120 帧）
                    return;
                }
                Debug.LogWarning("[BattleHudScreenshot] 场地中心引导失败（无 Driver/关卡源），按现状抓帧。");
                _framedShot = ShotIndex + 1;
                _frameAttempts = 0;
            }

            ShotIndex++;
            try
            {
                CaptureOne(ShotIndex);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BattleHudScreenshot] 抓帧失败：" + e);
                Finish();
                return;
            }

            ShotsLeft--;
            Countdown = ShotIntervalTicks;
        }

        /// <summary>运行时取证：出战船员的根/Visual 世界缩放（身高争议现场数据）。</summary>
        static void DumpCrewScales()
        {
            foreach (PirateCrew.Battle.PirateBase p in Object.FindObjectsByType<PirateCrew.Battle.PirateBase>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Transform visual = p.transform.Find("Visual");
                Debug.Log("[BattleHudScreenshot] 船员 " + p.name
                    + " root.lossyScale=" + p.transform.lossyScale.ToString("F3")
                    + " visual.localScale=" + (visual != null ? visual.localScale.ToString("F3") : "<无 Visual>")
                    + " visual.lossyScale=" + (visual != null ? visual.lossyScale.ToString("F3") : "-")
                    + " crewType=" + p.CrewType);
            }
        }

        static RenderTexture _rt;
        static RenderTexture _rtUi;
        static Camera _uiCam;
        static GameObject _uiCamGo;
        static readonly List<Canvas> _switchedCanvases = new List<Canvas>();

        static void PrepareCanvasForCapture()
        {
            if (TryGetDebugMode(out float debugMode))
                Shader.SetGlobalFloat(global::PirateCrew.Rendering.Pixelart.PixelartPath.DebugModeId, debugMode);

            Camera cam = Camera.main;
            if (cam == null)
                cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
                return;

            // 【取证】活动管线资产的渲染器表全量打印——rig 的叠加索引是按装配时资产查的，
            // 若活动资产不同表，同一索引会指到别的渲染器上。
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                is UniversalRenderPipelineAsset urp)
            {
                var so = new SerializedObject(urp);
                var list = so.FindProperty("m_RendererDataList");
                if (list != null)
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var r = list.GetArrayElementAtIndex(i).objectReferenceValue;
                        Debug.Log("[BattleHudScreenshot] 活动管线渲染器[" + i + "] = "
                            + (r != null ? r.name : "<null>"));
                    }
            }

            // 【顺序敏感】先把主相机渲到固定 RT——ScreenSpaceCamera 画布按相机当时的像素目标
            // 定尺寸，反过来切会按错误尺寸布局（部件漂到画外，走查实锤）。
            if (_rt == null)
                _rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = _rt;

            // 【UI 层独立 RT】像素化 rig 给主相机配了剔除掩码（UI 被裁）；URP 里两台相机先后
            // 渲同一张 RT 会互相清屏——所以 UI 相机渲到**自己的透明底 RT**，C# 里按 alpha 合成。
            if (_rtUi == null)
                _rtUi = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            _uiCamGo = new GameObject("ShotUICam");
            _uiCam = _uiCamGo.AddComponent<Camera>();
            _uiCam.orthographic = true;
            _uiCam.orthographicSize = 540f;           // 画布设计高 540（ConstantPixelSize × Unit=2）
            _uiCam.nearClipPlane = -10f;
            _uiCam.farClipPlane = 10f;
            _uiCam.cullingMask = 1 << 5;              // 只渲 UI 层
            _uiCam.clearFlags = CameraClearFlags.SolidColor;
            _uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 透明底 → 合成用 alpha
            _uiCam.targetTexture = _rtUi;

            // 【渲染器是关键】裸相机默认拿到管线默认渲染器（像素化 Cast——专为 rig 铸图，
            // 单独跑直接输出空屏，13/16 轮取证 UI RT 全透明就栽在这）。UI 相机改用 rig 叠加
            // 相机同款 PixelartOverlay 渲染器（标准前向 + 只画透明队列）。
            var pixelRig = Object.FindFirstObjectByType<PirateCrew.Rendering.Pixelart.PixelartCameraRig>();
            int overlayIdx = pixelRig != null ? pixelRig.overlayRendererIndex : -1;
            Debug.Log("[BattleHudScreenshot] rig=" + (pixelRig != null ? pixelRig.name : "<null>")
                + " 叠加渲染器索引 = " + overlayIdx);
            if (overlayIdx >= 0)
                _uiCam.GetUniversalAdditionalCameraData().SetRenderer(overlayIdx);

            // 【所有根画布一起切】FindObjectOfType 只拿一个——拿到 SceneLoaderOverlay 而
            // 漏掉真 HUD 画布 BattleCanvas 的可能性存在（16 轮仍全空的候选根因之一）。
            // 层是硬前提：builder 建的画布挂 Default 层（Overlay 渲染不挑层所以一直没暴露），
            // UI 相机带剔除掩码，画布不上 UI 层就整块被裁。Play 模式内临时改动，退出自动还原。
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0)
                uiLayer = 5;
            _switchedCanvases.Clear();
            foreach (Canvas rc in Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!rc.isRootCanvas)
                    continue;
                foreach (Transform child in rc.gameObject.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = uiLayer;
                rc.worldCamera = _uiCam;    // 画布跟 UI 相机走（正交覆盖 1920×1080 视口）
                rc.planeDistance = 1f;
                rc.renderMode = RenderMode.ScreenSpaceCamera;
                _switchedCanvases.Add(rc);
                Debug.Log("[BattleHudScreenshot] 画布 " + rc.name + " → 层UI + ScreenSpaceCamera(UI cam)，"
                    + "可绘元件 " + rc.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length
                    + " 个，pos=" + rc.transform.position);
            }
            Canvas.ForceUpdateCanvases();
        }

        static void RestoreCanvas()
        {
            Camera cam = Camera.main;
            if (cam != null)
                cam.targetTexture = null;
            if (_uiCam != null)
                _uiCam.targetTexture = null;
            if (_uiCamGo != null)
                Object.DestroyImmediate(_uiCamGo);
            _uiCam = null;
            _uiCamGo = null;
            if (_rt != null)
            {
                _rt.Release();
                _rt = null;
            }
            if (_rtUi != null)
            {
                _rtUi.Release();
                _rtUi = null;
            }
            foreach (Canvas canvas in _switchedCanvases)
            {
                if (canvas == null)
                    continue;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
            _switchedCanvases.Clear();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// 同帧导出墨线标记缓冲（battle-hud-N-outline.png，艺术画布原尺寸，描边第二趟缝闭合后的
        /// 最终版）——拆管线用：最终画面与描边趟的标记逐像素对得上（同一帧、同一次渲染），
        /// 不存在跨进程相位差。
        /// </summary>
        static void DumpOutlineBufferSameFrame(int index)
        {
            var rig = global::PirateCrew.Rendering.Pixelart.PixelartPath.ActiveRig;
            RenderTexture outline = rig != null ? rig.OutlineClosedBuffer : null;
            if (outline == null)
                outline = rig != null ? rig.OutlineBuffer : null;   // 旧资产/闭合趟未跑时兜底
            if (outline == null)
                return;

            // 对照导出第一趟原始判定（battle-hud-N-outline-pass1.png）：两份相减 = 闭合趟补了哪些格。
            if (rig != null && rig.OutlineBuffer != null && rig.OutlineBuffer != outline)
            {
                RenderTexture prev0 = RenderTexture.active;
                RenderTexture.active = rig.OutlineBuffer;
                var tex0 = new Texture2D(rig.OutlineBuffer.width, rig.OutlineBuffer.height, TextureFormat.RGBA32, false);
                tex0.ReadPixels(new Rect(0, 0, rig.OutlineBuffer.width, rig.OutlineBuffer.height), 0, 0);
                tex0.Apply();
                RenderTexture.active = prev0;
                File.WriteAllBytes(Path.Combine(OutputDir, "battle-hud-" + index + "-outline-pass1.png"),
                    tex0.EncodeToPNG());
                Object.DestroyImmediate(tex0);
            }

            Debug.Log("[BattleHudScreenshot][勘] pixelScale=" + rig.pixelScale
                + " Screen=" + Screen.width + "x" + Screen.height
                + " 画布=" + outline.width + "x" + outline.height);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = outline;
            var tex = new Texture2D(outline.width, outline.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, outline.width, outline.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(Path.Combine(OutputDir, "battle-hud-" + index + "-outline.png"),
                tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// 同帧导出覆盖缓冲（battle-hud-N-cover.png，屏幕档原尺寸，灰度 = albedo.a × 255）——
        /// 拆"块内细像素覆盖分布"用：缝诊断要数每个艺术像素块 (k×k) 里有几何的细像素数，
        /// 判"锚点细像素落空但块内有几何"（偶数档锚偏角上的量化丢边）。
        /// 环境变量 PC3D_DUMP_COVER 非空时才导（例常截图不多写两张大图）。
        /// </summary>
        static void DumpCoverageBufferSameFrame(int index)
        {
            if (System.Environment.GetEnvironmentVariable("PC3D_DUMP_COVER") is not string on
                || on.Length == 0)
                return;

            var rig = global::PirateCrew.Rendering.Pixelart.PixelartPath.ActiveRig;
            RenderTexture albedo = rig != null ? rig.AlbedoBuffer : null;
            if (albedo == null)
                return;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = albedo;
            var tex = new Texture2D(albedo.width, albedo.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, albedo.width, albedo.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // 只留覆盖灰度（RGB = a），脚本侧免解 alpha 通道。
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(px[i].a, px[i].a, px[i].a, 255);
            tex.SetPixels32(px);

            File.WriteAllBytes(Path.Combine(OutputDir, "battle-hud-" + index + "-cover.png"),
                tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            // 同帧导出 Patch 副本覆盖（battle-hud-N-coverpatch.png）——与 cover 对照即 BlockPatch
            // 的提升/直通实况（假覆盖、错位一眼可辨）。
            RenderTexture albedoPatch = rig.AlbedoPatchBuffer;
            if (albedoPatch != null)
            {
                RenderTexture.active = albedoPatch;
                var texAP = new Texture2D(albedoPatch.width, albedoPatch.height, TextureFormat.RGBA32, false);
                texAP.ReadPixels(new Rect(0, 0, albedoPatch.width, albedoPatch.height), 0, 0);
                texAP.Apply();
                RenderTexture.active = prev;
                var pxAP = texAP.GetPixels32();
                for (int i = 0; i < pxAP.Length; i++)
                    pxAP[i] = new Color32(pxAP[i].a, pxAP[i].a, pxAP[i].a, 255);
                texAP.SetPixels32(pxAP);
                File.WriteAllBytes(Path.Combine(OutputDir, "battle-hud-" + index + "-coverpatch.png"),
                    texAP.EncodeToPNG());
                Object.DestroyImmediate(texAP);
            }

            // 同帧导出逐物体描边开关（battle-hud-N-palette.png，灰度 = palette.a × 255）——
            // 缝诊断要区分"墨线与物体之间的空格"是病（物体开着描边却没出墨）还是
            // 合法（物体本就不描边，墨线是旁边另一个物体的）。
            RenderTexture palette = rig.PaletteBuffer;
            if (palette == null)
                return;
            RenderTexture.active = palette;
            var texP = new Texture2D(palette.width, palette.height, TextureFormat.RGBA32, false);
            texP.ReadPixels(new Rect(0, 0, palette.width, palette.height), 0, 0);
            texP.Apply();
            RenderTexture.active = prev;
            var pxP = texP.GetPixels32();
            for (int i = 0; i < pxP.Length; i++)
                pxP[i] = new Color32(pxP[i].a, pxP[i].a, pxP[i].a, 255);
            texP.SetPixels32(pxP);
            File.WriteAllBytes(Path.Combine(OutputDir, "battle-hud-" + index + "-palette.png"),
                texP.EncodeToPNG());
            Object.DestroyImmediate(texP);
        }

        /// <summary>
        /// 拍摄帧 dump 全部战斗单位（勘日志）：世界坐标 + Cast 相机视口坐标 + 激活状态——
        /// "画面里少了谁/谁被谁挡"这类问题靠它一锤定音（视口 [0,1]²，出界 = 不在取景内）。
        /// </summary>
        static void DumpUnitsSameFrame(int index)
        {
            var rig = global::PirateCrew.Rendering.Pixelart.PixelartPath.ActiveRig;
            Camera cam = rig != null ? rig.CastCamera : Camera.main;
            var units = Object.FindObjectsByType<global::PirateCrew.Battle.PirateBase>(
                FindObjectsSortMode.None);
            Debug.Log("[BattleHudScreenshot][勘] shot" + index + " 单位数=" + units.Length
                + " 相机=" + (cam != null ? cam.name : "空"));
            foreach (var u in units)
            {
                Vector3 p = u.transform.position;
                Vector3 vp = cam != null ? cam.WorldToViewportPoint(p) : Vector3.zero;
                Debug.Log("[BattleHudScreenshot][勘]   " + u.name
                    + " pos=(" + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2")
                    + ") viewport=(" + vp.x.ToString("F2") + "," + vp.y.ToString("F2") + ")"
                    + " active=" + u.gameObject.activeInHierarchy);
            }
        }

        static void Finish()
        {
            Shader.SetGlobalFloat(global::PirateCrew.Rendering.Pixelart.PixelartPath.DebugModeId, 0f);
            foreach (var (mat, prop, value) in _EnvTuningBackup)
                mat.SetFloat(prop, value);      // 恢复资产原值,防退出时自动保存泄漏临时值
            _EnvTuningBackup.Clear();
            SessionState.SetBool(KeyArmed, false);
            SessionState.SetBool(KeyCanvasPrepared, false);
            EditorApplication.update -= Tick;
            RestoreCanvas();
            EditorApplication.isPlaying = false;
            Debug.Log("[BattleHudScreenshot] 完成，输出目录 " + OutputDir);
            if (Headless)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// 环境变量材质调参（参数扫描出图用，只改内存值不落盘）：
        /// PC3D_EDGE_T = 法线边阈值（云两材质 _NormalEdgeThreshold，1.0 ≈ 面夹角 60°）
        /// PC3D_EDGE_L = 法线边档位（负 = 压暗档数）
        /// PC3D_CUTS   = 色带档数（_MainLightLevel）
        /// </summary>
        // 环境变量调参的资产原值备份(退出前恢复,防 Unity 资产自动保存把临时值写盘)
        static readonly List<(Material mat, string prop, float value)> _EnvTuningBackup
            = new List<(Material, string, float)>();

        static void ApplyEnvMaterialTuning()
        {
            string tRaw = System.Environment.GetEnvironmentVariable("PC3D_EDGE_T");
            string lRaw = System.Environment.GetEnvironmentVariable("PC3D_EDGE_L");
            string cRaw = System.Environment.GetEnvironmentVariable("PC3D_CUTS");
            if (tRaw == null && lRaw == null && cRaw == null)
                return;

            foreach (string path in new[]
            {
                "Assets/Art/Materials/Lowpoly/Lowpoly_CloudWarmWhite.mat",
                "Assets/Art/Materials/Lowpoly/Lowpoly_CloudPaleGold.mat",
            })
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                    continue;
                if (tRaw != null && float.TryParse(tRaw, out float tv))
                {
                    _EnvTuningBackup.Add((m, "_NormalEdgeThreshold", m.GetFloat("_NormalEdgeThreshold")));
                    m.SetFloat("_NormalEdgeThreshold", tv);
                }
                if (lRaw != null && float.TryParse(lRaw, out float lv))
                {
                    _EnvTuningBackup.Add((m, "_NormalEdgeLevel", m.GetFloat("_NormalEdgeLevel")));
                    m.SetFloat("_NormalEdgeLevel", lv);
                }
                if (cRaw != null && float.TryParse(cRaw, out float cv))
                {
                    _EnvTuningBackup.Add((m, "_MainLightLevel", m.GetFloat("_MainLightLevel")));
                    m.SetFloat("_MainLightLevel", cv);
                }
            }
            Debug.Log("[BattleHudScreenshot] 材质调参 EDGE_T=" + tRaw + " EDGE_L=" + lRaw + " CUTS=" + cRaw);
        }

        /// <summary>
        /// 出图诊断档（环境变量 PC3D_CAPTURE_DEBUG）：非空时把
        /// <c>_PixelartDebugMode</c> 置为该值再抓帧（契约 §2.2 档位表——1 = albedo、
        /// 4 = 墨线标记、8/9/10 = 描边趟自有诊断）。抓完 Finish 里归 0。
        /// </summary>
        static bool TryGetDebugMode(out float mode)
        {
            mode = 0f;
            string raw = System.Environment.GetEnvironmentVariable("PC3D_CAPTURE_DEBUG");
            return !string.IsNullOrEmpty(raw) && float.TryParse(raw, out mode);
        }

        static void CaptureOne(int index)
        {
            Camera cam = Camera.main;
            if (cam == null)
                cam = Object.FindObjectOfType<Camera>();
            if (cam == null || cam.targetTexture == null || _rtUi == null)
                throw new System.InvalidOperationException("相机/RT 未就绪（PrepareCanvas 未跑？）");

            int width = _rt.width;
            int height = _rt.height;

            Canvas.ForceUpdateCanvases();
            cam.Render();                       // 世界层 → _rt（像素化 rig 链，含栈内叠加相机）
            _uiCam.Render();                    // UI 层 → _rtUi（透明底）

            RenderTexture.active = _rt;
            var world = new Texture2D(width, height, TextureFormat.RGB24, false);
            world.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            world.Apply();

            RenderTexture.active = _rtUi;
            var ui = new Texture2D(width, height, TextureFormat.RGBA32, false);
            ui.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            ui.Apply();
            RenderTexture.active = null;

            // 【取证】UI RT 里到底有没有东西——alpha 全零 = UI 相机啥也没画，别再让合成背锅。
            Color32[] fg = ui.GetPixels32();
            int nonzero = 0;
            int maxA = 0;
            foreach (Color32 px in fg)
            {
                if (px.a > maxA)
                    maxA = px.a;
                if (px.a != 0)
                    nonzero++;
            }
            Debug.Log("[BattleHudScreenshot] UI RT 统计：非零 alpha 像素 " + nonzero
                + "/" + fg.Length + "  maxA=" + maxA);

            // C# alpha-over 合成：out = UI*α + 世界*(1-α)——两台相机各渲各的 RT，在此归并。
            Color32[] bg = world.GetPixels32();
            var merged = new Color32[fg.Length];
            for (int i = 0; i < fg.Length; i++)
            {
                byte a = fg[i].a;
                if (a == 255)
                    merged[i] = fg[i];
                else if (a == 0)
                    merged[i] = bg[i];
                else
                    merged[i] = new Color32(
                        (byte)((fg[i].r * a + bg[i].r * (255 - a)) / 255),
                        (byte)((fg[i].g * a + bg[i].g * (255 - a)) / 255),
                        (byte)((fg[i].b * a + bg[i].b * (255 - a)) / 255),
                        255);
            }

            var final = new Texture2D(width, height, TextureFormat.RGB24, false);
            final.SetPixels32(merged);
            final.Apply();

            Directory.CreateDirectory(OutputDir);
            string path = Path.Combine(OutputDir, "battle-hud-" + index + ".png");
            File.WriteAllBytes(path, final.EncodeToPNG());
            DumpOutlineBufferSameFrame(index);
            DumpCoverageBufferSameFrame(index);
            DumpUnitsSameFrame(index);
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(final);
            Debug.Log("[BattleHudScreenshot] 已写入 " + path
                + "（ortho " + cam.orthographicSize.ToString("F2")
                + "，可见高 " + (cam.orthographicSize * 2f).ToString("F1") + " m）");
        }
    }
}
