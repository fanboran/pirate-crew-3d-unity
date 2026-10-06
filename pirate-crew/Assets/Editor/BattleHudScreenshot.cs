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

        static BattleHudScreenshot()
        {
            if (Application.isBatchMode && !Armed)
                return;
            if (Armed)
                EditorApplication.update += Tick;   // 域重载后重新挂上（进入 Play 必触发重载）
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
            ShotsLeft = ShotCount;
            ShotIndex = 0;
            Countdown = FirstShotDelayTicks;
            SessionState.SetBool(KeyArmed, true);

            // 【口径对齐】像素管线的艺术画布 = 屏幕后台缓冲 ÷ pixelScale（PixelartPath 顶部口径）。
            // batchmode 的隐藏窗口只有几百像素高，不先撑到 1920×1080，抓出的图会比实机粗 3 倍
            // （实测块 9×6 而非 3×3——艺术画布跟着小后台缓冲走，再被拉伸到大 RT 上）。
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);

            EditorSceneManager.OpenScene(ScenePathOrOverride, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;   // 触发域重载 → 静态构造重挂 Tick
            EditorApplication.update += Tick;     // 重载前这几帧也直接跑（若该项目关了重载则不触发）
            Debug.Log("[BattleHudScreenshot] 已武装：进 Play 后自动抓 " + ShotCount + " 张 → " + OutputDir);
        }

        static void Tick()
        {
            if (!Armed || !EditorApplication.isPlaying)
                return;

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
            ApplyZoomTier(ShotIndex + 1);
        }

        /// <summary>缩放档对比（创始人裁决「还是太小了」）：第 2/3 枪把 Driver 的目标 ortho
        /// 反射改档（<c>_targetOrthoSize</c> 是单一写入者的输入口，随帧平滑到位），
        /// 第 1 枪保持默认近景档不动。档值只服务对比出图，不回写任何常量。</summary>
        static readonly float[] ZoomTierTargets = { 0f, 5.15f, 3.43f };   // 0 = 保持默认

        static void ApplyZoomTier(int nextShot)
        {
            if (nextShot < 1 || nextShot > ZoomTierTargets.Length)
                return;
            float tier = ZoomTierTargets[nextShot - 1];
            if (tier <= 0f)
                return;
            var driver = Object.FindFirstObjectByType<PirateCrew.Battle.BattleCameraDriver>();
            if (driver == null)
            {
                Debug.Log("[BattleHudScreenshot] 场景无 BattleCameraDriver（非 Battle 对比拍），缩放档跳过。");
                return;
            }
            var fld = typeof(PirateCrew.Battle.BattleCameraDriver).GetField("_targetOrthoSize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fld == null)
            {
                Debug.LogError("[BattleHudScreenshot] 找不到 BattleCameraDriver._targetOrthoSize，缩放档跳过。");
                return;
            }
            fld.SetValue(driver, tier);
            Debug.Log("[BattleHudScreenshot] 第 " + nextShot + " 枪目标 ortho = " + tier
                + "（可见高 " + (tier * 2f).ToString("F1") + " m）");
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

        static void Finish()
        {
            SessionState.SetBool(KeyArmed, false);
            SessionState.SetBool(KeyCanvasPrepared, false);
            EditorApplication.update -= Tick;
            RestoreCanvas();
            EditorApplication.isPlaying = false;
            Debug.Log("[BattleHudScreenshot] 完成，输出目录 " + OutputDir);
            if (Headless)
                EditorApplication.Exit(0);
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
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(final);
            Debug.Log("[BattleHudScreenshot] 已写入 " + path
                + "（ortho " + cam.orthographicSize.ToString("F2")
                + "，可见高 " + (cam.orthographicSize * 2f).ToString("F1") + " m）");
        }
    }
}
