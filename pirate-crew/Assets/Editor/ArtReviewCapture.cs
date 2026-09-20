using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 美术评审出图工具：按 <see cref="ArtReviewShots"/> 的机位清单逐张摆相机并落盘，
    /// 用于"没有美术出图能力"时快速产出可评审的场景截图。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/美术评审/采集评审图（当前场景 EditMode）
    ///   菜单: PirateCrew/美术评审/采集评审图（进 Play 自动出图）
    ///   菜单: PirateCrew/美术评审/压缩评审图为 1600px 宽
    ///   菜单: PirateCrew/美术评审/取消当前采集
    ///   批处理: -executeMethod PirateCrew.EditorTools.ArtReviewCapture.CaptureEditMode
    ///           （PlayMode 那条必须人点，见下）
    ///   自动: 创建 &lt;仓库根&gt;/export/art-review/.auto-capture 后打开工程/触发域重载，
    ///         &lt;see cref="AutoCaptureOnRequest"/&gt; 会删掉请求文件并在首次资产导入完成后
    ///         自动进 PlayMode 出图（失败 LogError 报因，不重试）。
    ///
    /// 【产物】&lt;仓库根&gt;/export/art-review/&lt;yyyy-MM-dd&gt;-&lt;序号&gt;-&lt;slug&gt;.png
    ///   仓库根由 Application.dataPath 上溯两级得到；export/ 在 Unity 工程外，用 System.IO 直落盘。
    ///
    /// 【为什么必须有两个入口】
    ///   · EditMode：不开 Play，摆相机拍当前已保存的场景（构图/天空/地形/HUD 静态检查）。
    ///     单位在 PlayMode 的 BattleController.Awake 里才生成，所以 EditMode 拍不到角色。
    ///   · PlayMode：进 Play → 等 BattleController + 单位就绪 → 逐张出图 → 退出 Play。
    ///     "单角色特写"这类机位必须有运行时单位才有意义。
    ///
    /// 【域重载】进 Play 默认会触发域重载，静态字段与 EditorApplication.update 订阅都会丢。
    ///   因此用 <see cref="SessionState"/> 记一个待恢复标志，<see cref="ResumeIfPending"/> 在
    ///   域重载后重新接管状态机。若工程关掉了 Enter Play Mode 的域重载，静态字段保留、状态机不中断。
    ///
    /// 【安全】沿用 OutlineDebugCapture 的渲染路径前置检查：batchmode / GraphicsDeviceType.Null /
    ///   驱动未初始化时直接 Debug.LogError 拒绝执行，**不生成任何占位图或空白图**。
    ///   采集结束后还会逐个校验产物存在且非空，失败即 LogError 报因。
    /// </summary>
    public static class ArtReviewCapture
    {
        const string OutputFolderRelative = "export/art-review";
        const string ReviewCameraName = "ArtReviewCamera";
        const string HudCanvasName = "BattleCanvas";
        const string PendingPlaySessionKey = "PirateCrew.ArtReview.PendingPlay";

        /// <summary>摆好相机后等几帧再抓（给渲染/后处理稳定留时间）。</summary>
        const int SettleFrames = 6;

        /// <summary>请求截图后等几帧再摆下一张（ScreenCapture 是异步写盘）。</summary>
        const int PostCaptureFrames = 6;

        /// <summary>整体超时（编辑器 update 帧数）。约 1 分钟量级，防止无限挂起。</summary>
        const int TimeoutFrames = 4000;

        enum Phase
        {
            Idle = 0,
            WaitBattleReady,
            PlaceShot,
            WaitSettle,
            WaitCapture,
            Finish,
            Abort,
        }

        static Phase s_Phase = Phase.Idle;
        static bool s_Running;
        static bool s_OwnsPlayMode;
        static List<ArtReviewShot> s_Shots;
        static int s_ShotIndex;
        static int s_ElapsedFrames;

        static string s_OutputFolder;
        static string s_Date;
        static int s_Sequence;
        static readonly List<string> s_Written = new List<string>();

        static int s_WaitTicks;
        static int s_WaitTargetFrame;

        static Camera s_ReviewCamera;
        static Camera s_MainCamera;
        static bool s_MainCameraWasEnabled;

        static GameObject s_HudRoot;
        static bool s_HudWasActive;

        static BattleController s_Battle;
        static bool s_ArenaResolved;
        static Vector3 s_ArenaCenter;

        // ------------------------------------------------------------------
        // 菜单 / 批处理入口
        // ------------------------------------------------------------------

        /// <summary>EditMode 静态构图出图。也可 -executeMethod 调用。</summary>
        [MenuItem("PirateCrew/美术评审/采集评审图（当前场景 EditMode）", priority = 10)]
        public static void CaptureEditMode()
        {
            if (GuardBusy())
                return;
            if (!Preflight())
                return;

            BuildSessionPaths();
            s_Shots = ArtReviewShots.Enabled();
            s_ShotIndex = 0;
            s_ElapsedFrames = 0;
            s_OwnsPlayMode = false;
            s_Phase = Phase.WaitBattleReady;
            s_Running = true;

            EnsureReviewCamera();
            Hook();

            Debug.Log("[ArtReviewCapture] 开始 EditMode 采集（" + s_Shots.Count + " 个机位）。\n"
                + "  产物目录: " + s_OutputFolder + "\n"
                + "  需要 Game View 可见；采集期间请勿操作编辑器（暂停会让画面不重绘）。\n"
                + "  注意: EditMode 下单位未生成，依赖运行时单位的机位会自动跳过并给出日志。");
        }

        /// <summary>进 Play 自动出图。**必须人点菜单**（批处理里没有可靠渲染路径）。</summary>
        [MenuItem("PirateCrew/美术评审/采集评审图（进 Play 自动出图）", priority = 11)]
        public static void CaptureInPlayMode()
        {
            if (GuardBusy())
                return;
            if (!Preflight())
                return;

            BuildSessionPaths();
            s_Shots = ArtReviewShots.Enabled();
            s_ShotIndex = 0;
            s_ElapsedFrames = 0;
            s_OwnsPlayMode = true;
            s_Phase = Phase.WaitBattleReady;
            s_Running = true;

            SessionState.SetBool(PendingPlaySessionKey, true);

            if (Application.isPlaying)
            {
                // 已经在 Play：无需再进，就地开拍（域重载不会发生）。
                Debug.Log("[ArtReviewCapture] 已在 PlayMode，直接开始采集。");
                s_OwnsPlayMode = false;
                SessionState.EraseBool(PendingPlaySessionKey);
                EnsureReviewCamera();
                Hook();
                return;
            }

            Hook();
            Debug.Log("[ArtReviewCapture] 即将进入 PlayMode 采集（" + s_Shots.Count + " 个机位）。\n"
                + "  进入 Play 会触发域重载，本工具用 SessionState 自动恢复，无需手动干预。\n"
                + "  产物目录: " + s_OutputFolder);
            EditorApplication.isPlaying = true;
        }

        /// <summary>把 export/art-review 下超过阈值的 PNG 压到 1600px 宽（控制入库体积）。</summary>
        [MenuItem("PirateCrew/美术评审/压缩评审图为 1600px 宽", priority = 30)]
        public static void CompressArtifacts()
        {
            string folder = GetOutputFolder();
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning("[ArtReviewCapture] 目录不存在，无需压缩: " + folder);
                return;
            }

            const int maxWidth = 1600;
            string[] files = Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly);
            long beforeTotal = 0;
            long afterTotal = 0;
            int resized = 0;

            foreach (string path in files)
            {
                long before = new FileInfo(path).Length;
                beforeTotal += before;

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    Debug.LogWarning("[ArtReviewCapture] 解码失败，跳过: " + path);
                    Object.DestroyImmediate(texture);
                    afterTotal += before;
                    continue;
                }

                if (texture.width > maxWidth)
                {
                    int targetHeight = Mathf.Max(1, Mathf.RoundToInt(
                        texture.height * (maxWidth / (float)texture.width)));

                    RenderTexture rt = RenderTexture.GetTemporary(
                        maxWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(texture, rt);

                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    Texture2D scaled = new Texture2D(maxWidth, targetHeight, TextureFormat.RGBA32, false);
                    scaled.ReadPixels(new Rect(0, 0, maxWidth, targetHeight), 0, 0);
                    scaled.Apply();
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);

                    Object.DestroyImmediate(texture);
                    texture = scaled;
                    resized++;
                }

                byte[] outBytes = texture.EncodeToPNG();
                File.WriteAllBytes(path, outBytes);
                afterTotal += outBytes.Length;
                Object.DestroyImmediate(texture);
            }

            Debug.Log("[ArtReviewCapture] 压缩完成：扫描 " + files.Length + " 张，缩放 " + resized + " 张。\n"
                + "  体积 " + (beforeTotal / 1024) + " KB → " + (afterTotal / 1024) + " KB"
                + "（阈值：宽度 > 1600px 才缩放）。");
        }

        /// <summary>中断当前采集并还原相机/HUD/Play 状态。</summary>
        [MenuItem("PirateCrew/美术评审/取消当前采集", priority = 31)]
        public static void CancelCapture()
        {
            if (!s_Running)
            {
                Debug.LogWarning("[ArtReviewCapture] 当前没有正在进行的采集。");
                return;
            }

            Fail("用户手动取消");
        }

        // ------------------------------------------------------------------
        // 会话启动
        // ------------------------------------------------------------------

        static void BuildSessionPaths()
        {
            s_OutputFolder = GetOutputFolder();
            Directory.CreateDirectory(s_OutputFolder);

            s_Date = System.DateTime.Now.ToString("yyyy-MM-dd");
            s_Sequence = NextSequence(s_Date);
            s_Written.Clear();

            s_Battle = null;
            s_ArenaResolved = false;
        }

        /// <summary>当天已有产物时从下一个序号开始，避免覆盖上一轮。</summary>
        static int NextSequence(string date)
        {
            int max = 0;
            if (Directory.Exists(s_OutputFolder))
            {
                string prefix = date + "-";
                foreach (string path in Directory.GetFiles(s_OutputFolder, prefix + "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    // 形如 2026-09-13-03-slug
                    if (name.Length < prefix.Length + 3)
                        continue;
                    string numberPart = name.Substring(prefix.Length, 2);
                    if (int.TryParse(numberPart, out int value) && value > max)
                        max = value;
                }
            }
            return max + 1;
        }

        static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static bool GuardBusy()
        {
            if (s_Running || SessionState.GetBool(PendingPlaySessionKey, false))
            {
                Debug.LogWarning("[ArtReviewCapture] 上一轮采集还在进行（或正在等待进入 PlayMode），"
                    + "先等它结束或用菜单「取消当前采集」。");
                return true;
            }
            return false;
        }

        static bool Preflight()
        {
            string reason = DetectUnavailableRenderPath();
            if (reason != null)
            {
                Debug.LogError("[ArtReviewCapture] 当前会话没有可用渲染路径，无法出真实评审图。原因：" + reason + "\n"
                    + "  本脚本不会生成占位图/空白图。请改用**有图形界面的 Unity 编辑器会话**：\n"
                    + "    1) 打开 Assets/Scenes/Battle.unity；\n"
                    + "    2) 菜单 PirateCrew/美术评审/采集评审图（当前场景 EditMode 或 进 Play 自动出图）。\n"
                    + "  注意: 不要用 -batchmode -nographics 跑截图（GfxDevice 创建不了且无真实像素）。");
                return false;
            }

            if (Application.isPlaying && EditorApplication.isPaused)
            {
                Debug.LogError("[ArtReviewCapture] 编辑器处于 PlayMode 暂停状态，画面不会随相机重绘，"
                    + "无法出图。请先取消暂停后重试。");
                return false;
            }

            if (Object.FindObjectOfType<BattleController>() == null)
            {
                Debug.LogError("[ArtReviewCapture] 当前打开的场景里没有 BattleController。\n"
                    + "  请先打开 Assets/Scenes/Battle.unity 再运行本工具（当前场景: "
                    + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + "）。");
                return false;
            }

            if (ArtReviewShots.Enabled().Count == 0)
            {
                Debug.LogError("[ArtReviewCapture] 机位清单为空（ArtReviewShots 里 Enabled 全为 false？）。");
                return false;
            }

            return true;
        }

        /// <summary>返回 null 表示有可用渲染路径；否则返回不可用原因。</summary>
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

        // ------------------------------------------------------------------
        // 状态机
        // ------------------------------------------------------------------

        /// <summary>域重载后恢复（进 Play 默认会触发域重载）。</summary>
        [InitializeOnLoadMethod]
        static void ResumeIfPending()
        {
            if (!SessionState.GetBool(PendingPlaySessionKey, false))
                return;

            if (!Application.isPlaying)
            {
                SessionState.EraseBool(PendingPlaySessionKey);
                Debug.LogWarning("[ArtReviewCapture] 检测到待恢复的采集标志，但当前不在 PlayMode，已清除该标志。");
                return;
            }

            if (s_OutputFolder == null)
                BuildSessionPaths();

            s_Shots = ArtReviewShots.Enabled();
            s_ShotIndex = 0;
            s_ElapsedFrames = 0;
            s_OwnsPlayMode = true;
            s_Phase = Phase.WaitBattleReady;
            s_Running = true;

            Hook();
            Debug.Log("[ArtReviewCapture] 域重载后已恢复采集，等待战斗场景就绪…");
        }

        // ------------------------------------------------------------------
        // 自动采集（打开工程即出图）
        // ------------------------------------------------------------------

        /// <summary>
        /// 自动采集请求文件（相对仓库根）。**存在即触发、触发后立即删除**，因此是一次性开关：
        /// 用户（或协调者）只要在打开工程前创建该文件，Editor 启动/域重载后就会自动出图；
        /// 文件不存在时本方法什么都不做。
        /// </summary>
        const string AutoCaptureRequestRelative = OutputFolderRelative + "/.auto-capture";

        /// <summary>等待资产导入/编译完成的 delayCall 轮询上限（约等于编辑器空闲帧数；~60s 量级）。</summary>
        const int AutoCaptureMaxDeferrals = 3600;

        static bool s_AutoCaptureScheduled;
        static int s_AutoCaptureDeferrals;

        /// <summary>
        /// 打开工程时检查自动采集请求文件：存在则删除并安排一次 PlayMode 采集。
        /// 【为什么不直接调用】Editor 刚启动时资产导入/脚本编译可能还在进行，
        /// 此刻进 Play 会拿到半成品场景；故先删文件（保证只触发一次、不会随域重载无限重试），
        /// 再用 delayCall 轮询到编辑器空闲（不编译/不导入）后触发。
        /// 【失败行为】任何失败都 Debug.LogError 报因并放弃，**不死循环重试**。
        /// </summary>
        [InitializeOnLoadMethod]
        static void AutoCaptureOnRequest()
        {
            if (s_AutoCaptureScheduled)
                return;

            string requestPath = GetAutoCaptureRequestPath();
            if (!File.Exists(requestPath))
                return; // 没有请求文件 → 什么都不做（正常打开工程不打扰）

            // 先删再触发：即使下面触发失败也不再重试，避免每次域重载都弹一次。
            try
            {
                File.Delete(requestPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ArtReviewCapture] 自动采集请求文件删除失败（已放弃本次自动采集）：" + e.Message);
                return;
            }

            s_AutoCaptureScheduled = true;
            s_AutoCaptureDeferrals = 0;
            Debug.Log("[ArtReviewCapture] 检测到自动采集请求，等资产导入完成后自动进 PlayMode 出图…");
            EditorApplication.delayCall += TryAutoCapture;
        }

        static void TryAutoCapture()
        {
            s_AutoCaptureDeferrals++;

            // 等首次资产导入/编译结束（两者都空闲才认为场景/资产已稳定）。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (s_AutoCaptureDeferrals > AutoCaptureMaxDeferrals)
                {
                    s_AutoCaptureScheduled = false;
                    Debug.LogError("[ArtReviewCapture] 自动采集放弃：等待资产导入/编译超过 "
                        + AutoCaptureMaxDeferrals + " 帧仍繁忙。请等编辑器空闲后手动执行菜单 "
                        + "PirateCrew/美术评审/采集评审图（进 Play 自动出图）。");
                    return;
                }

                EditorApplication.delayCall += TryAutoCapture;
                return;
            }

            s_AutoCaptureScheduled = false;

            if (Application.isBatchMode)
            {
                Debug.LogError("[ArtReviewCapture] 自动采集放弃：Application.isBatchMode == true，"
                    + "批处理模式没有渲染路径，无法出真实评审图。请在图形界面的编辑器会话里出图。");
                return;
            }

            try
            {
                if (!EnsureBattleSceneOpenForAutoCapture())
                    return;

                // 复用既有入口：它内部会做渲染路径前置检查、状态机启动与 PlayMode 进入。
                CaptureInPlayMode();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ArtReviewCapture] 自动采集触发失败：" + e);
            }
        }

        /// <summary>
        /// 自动采集前的场景准备：当前场景没有 BattleController 时，若 Battle.unity 存在且
        /// 当前场景无未保存修改，则静默切到 Battle 场景；否则报因放弃（不擅自覆盖用户改动）。
        /// </summary>
        static bool EnsureBattleSceneOpenForAutoCapture()
        {
            if (Object.FindObjectOfType<BattleController>() != null)
                return true;

            const string battleScenePath = "Assets/Scenes/Battle.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(battleScenePath) == null)
            {
                Debug.LogError("[ArtReviewCapture] 自动采集放弃：找不到 " + battleScenePath
                    + "，请先跑 PirateCrew.EditorTools.ArtGate.BuildAll 生成战斗场景。");
                return false;
            }

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                Debug.LogError("[ArtReviewCapture] 自动采集放弃：当前场景有未保存修改，"
                    + "不擅自切换场景。请保存后手动执行菜单 PirateCrew/美术评审/采集评审图（进 Play 自动出图）。");
                return false;
            }

            Debug.Log("[ArtReviewCapture] 自动采集：当前场景无 BattleController，已打开 " + battleScenePath);
            EditorSceneManager.OpenScene(battleScenePath, OpenSceneMode.Single);

            if (Object.FindObjectOfType<BattleController>() == null)
            {
                Debug.LogError("[ArtReviewCapture] 自动采集放弃：已打开 " + battleScenePath
                    + " 但仍找不到 BattleController（场景是坏的吗？）。");
                return false;
            }
            return true;
        }

        static string GetAutoCaptureRequestPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath); // pirate-crew
            string repoRoot = Path.GetDirectoryName(projectRoot);              // 仓库根
            return Path.Combine(repoRoot, AutoCaptureRequestRelative).Replace('\\', '/');
        }

        static void BeginWait(int frames)
        {
            s_WaitTicks = frames;
            s_WaitTargetFrame = Time.frameCount + frames;
        }

        static bool IsWaiting()
        {
            if (Application.isPlaying)
                return Time.frameCount < s_WaitTargetFrame;

            if (s_WaitTicks > 0)
            {
                s_WaitTicks--;
                return true;
            }
            return false;
        }

        static void Tick()
        {
            if (!s_Running)
            {
                EditorApplication.update -= Tick;
                return;
            }

            // EditMode 没有 player loop，需要主动推一帧，否则相机改了画面不重绘。
            if (!Application.isPlaying)
            {
                InternalEditorUtility.RepaintAllViews();
                EditorApplication.QueuePlayerLoopUpdate();
            }

            s_ElapsedFrames++;
            if (s_ElapsedFrames > TimeoutFrames)
            {
                Fail("等待超时（" + TimeoutFrames + " 帧），采集未完成");
                return;
            }

            if (IsWaiting())
                return;

            switch (s_Phase)
            {
                case Phase.WaitBattleReady:
                    // 选了"进 Play"但还没真正进 Play：继续等。
                    if (s_OwnsPlayMode && !Application.isPlaying)
                    {
                        BeginWait(2);
                        return;
                    }
                    // PlayMode 里等 BattleController 与单位生成完成。
                    if (Application.isPlaying && !BattleReady())
                    {
                        BeginWait(2);
                        return;
                    }
                    s_ShotIndex = 0;
                    s_Phase = Phase.PlaceShot;
                    return;

                case Phase.PlaceShot:
                    if (s_ShotIndex >= s_Shots.Count)
                    {
                        s_Phase = Phase.Finish;
                        return;
                    }
                    if (PlaceCurrentShot())
                    {
                        BeginWait(SettleFrames);
                        s_Phase = Phase.WaitSettle;
                    }
                    else
                    {
                        // 该机位不可用：已给日志，跳过，继续下一个。
                        s_ShotIndex++;
                    }
                    return;

                case Phase.WaitSettle:
                    CaptureCurrentShot();
                    BeginWait(PostCaptureFrames);
                    s_Phase = Phase.WaitCapture;
                    return;

                case Phase.WaitCapture:
                    s_ShotIndex++;
                    s_Phase = Phase.PlaceShot;
                    return;

                case Phase.Finish:
                    Finish();
                    return;

                case Phase.Abort:
                    EndRun();
                    return;
            }
        }

        /// <summary>PlayMode 就绪判定：BattleController 存在且已生成单位。</summary>
        static bool BattleReady()
        {
            if (s_Battle == null)
                s_Battle = Object.FindObjectOfType<BattleController>();

            return s_Battle != null
                && s_Battle.AllPirates != null
                && s_Battle.AllPirates.Count > 0;
        }

        // ------------------------------------------------------------------
        // 摆机位 / 截图
        // ------------------------------------------------------------------

        static bool PlaceCurrentShot()
        {
            ArtReviewShot shot = s_Shots[s_ShotIndex];

            // 进 Play 路径不在菜单里建相机（会触发域重载丢引用），第一张前懒创建。
            if (s_ReviewCamera == null)
                EnsureReviewCamera();

            if (shot.RequiresPlayMode && !Application.isPlaying)
            {
                Debug.LogWarning("[ArtReviewCapture] 跳过机位「" + shot.Label + "」（" + shot.Slug
                    + "）：该机位需要运行时单位，只在 PlayMode 有效。");
                return false;
            }

            if (!ResolvePivot(shot.Pivot, out Vector3 pivot))
            {
                Debug.LogWarning("[ArtReviewCapture] 跳过机位「" + shot.Label + "」（" + shot.Slug
                    + "）：对焦点 " + shot.Pivot + " 解析失败（场景缺对象或单位未生成）。");
                return false;
            }

            Vector3 position = pivot + shot.Offset;
            Vector3 lookAt = pivot + shot.LookAtOffset;

            if (shot.LookAtPivot)
            {
                Vector3 forward = lookAt - position;
                if (forward.sqrMagnitude < 1e-6f)
                {
                    Debug.LogError("[ArtReviewCapture] 机位「" + shot.Label + "」相机位置与对焦点重合，无法定向。");
                    return false;
                }
                s_ReviewCamera.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
            else
            {
                s_ReviewCamera.transform.rotation = Quaternion.Euler(shot.Euler);
            }

            s_ReviewCamera.transform.position = position;
            s_ReviewCamera.fieldOfView = shot.Fov;

            SetHudVisible(shot.ShowHud);

            Debug.Log("[ArtReviewCapture] 摆机位 [" + (s_ShotIndex + 1) + "/" + s_Shots.Count + "] "
                + shot.Label + "（" + shot.Slug + "）pos=" + position.ToString("F2")
                + " fov=" + shot.Fov + (shot.ShowHud ? " 含HUD" : ""));
            return true;
        }

        static void CaptureCurrentShot()
        {
            ArtReviewShot shot = s_Shots[s_ShotIndex];
            string fileName = s_Date + "-" + s_Sequence.ToString("00") + "-" + shot.Slug + ".png";
            string fullPath = Path.Combine(s_OutputFolder, fileName).Replace('\\', '/');
            s_Sequence++;
            s_Written.Add(fullPath);

            ScreenCapture.CaptureScreenshot(fullPath);
            Debug.Log("[ArtReviewCapture] 已请求截图 → " + fullPath + "\n  应看到: " + shot.Note);
        }

        static bool ResolvePivot(ArtReviewPivot pivot, out Vector3 center)
        {
            switch (pivot)
            {
                case ArtReviewPivot.WorldOrigin:
                    center = Vector3.zero;
                    return true;

                case ArtReviewPivot.SelectedUnit:
                    return TryResolveSelectedUnit(out center);

                default:
                    return TryResolveArenaCenter(out center);
            }
        }

        /// <summary>竞技场中心由场景里的 Ground/Water/Terrain 包围盒推算，不写死关卡尺寸。</summary>
        static bool TryResolveArenaCenter(out Vector3 center)
        {
            if (s_ArenaResolved)
            {
                center = s_ArenaCenter;
                return true;
            }

            string[] candidates = { "Ground", "Water", "Terrain" };
            foreach (string name in candidates)
            {
                GameObject go = GameObject.Find(name);
                if (go == null)
                    continue;

                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                Vector3 boundsCenter = renderer.bounds.center;
                // 地面顶面 y=0（LevelGeometry.GroundTopY），把中心 Y 归零，机位偏移才有统一基准。
                s_ArenaCenter = new Vector3(boundsCenter.x, 0f, boundsCenter.z);
                s_ArenaResolved = true;
                center = s_ArenaCenter;
                return true;
            }

            center = Vector3.zero;
            return false;
        }

        static bool TryResolveSelectedUnit(out Vector3 position)
        {
            if (s_Battle == null)
                s_Battle = Object.FindObjectOfType<BattleController>();

            if (s_Battle == null)
            {
                position = Vector3.zero;
                return false;
            }

            BattleTeam team = s_Battle.CurrentTeam;
            PirateBase unit = null;
            if (team != null)
                unit = team.SelectedCharacter != null ? team.SelectedCharacter : team.FirstAlive();

            if (unit == null && s_Battle.AllPirates.Count > 0)
                unit = s_Battle.AllPirates[0];

            if (unit == null || !unit.Alive)
            {
                position = Vector3.zero;
                return false;
            }

            position = unit.transform.position;
            return true;
        }

        // ------------------------------------------------------------------
        // 相机 / HUD 开关
        // ------------------------------------------------------------------

        static void EnsureReviewCamera()
        {
            if (s_ReviewCamera != null)
                return;

            s_MainCamera = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
            if (s_MainCamera == null)
            {
                Debug.LogWarning("[ArtReviewCapture] 场景里找不到主相机，评审相机会用默认参数创建。");
            }

            var go = new GameObject(ReviewCameraName, typeof(Camera));
            if (!Application.isPlaying)
                go.hideFlags = HideFlags.HideAndDontSave; // EditMode 下不污染/不弄脏场景

            s_ReviewCamera = go.GetComponent<Camera>();
            if (s_MainCamera != null)
                s_ReviewCamera.CopyFrom(s_MainCamera);

            s_ReviewCamera.clearFlags = CameraClearFlags.Skybox;
            s_ReviewCamera.depth = (s_MainCamera != null ? s_MainCamera.depth : 0f) + 10f;
            s_ReviewCamera.enabled = true;
            s_ReviewCamera.tag = "Untagged";

            // 采集期间禁用主相机，避免两套相机互相覆盖导致抓到的不是评审机位。
            s_MainCameraWasEnabled = s_MainCamera != null && s_MainCamera.enabled;
            if (s_MainCamera != null)
                s_MainCamera.enabled = false;
        }

        static void CacheHud()
        {
            if (s_HudRoot != null)
                return;

            BattleHud hud = Object.FindObjectOfType<BattleHud>();
            if (hud != null)
                s_HudRoot = hud.transform.root.gameObject;
            else
                s_HudRoot = GameObject.Find(HudCanvasName);

            if (s_HudRoot != null)
                s_HudWasActive = s_HudRoot.activeSelf;
        }

        static void SetHudVisible(bool visible)
        {
            CacheHud();
            if (s_HudRoot != null)
                s_HudRoot.SetActive(visible);
        }

        // ------------------------------------------------------------------
        // 收尾
        // ------------------------------------------------------------------

        static void Finish()
        {
            RestoreState();
            VerifyArtifacts();

            Debug.Log("[ArtReviewCapture] 采集流程结束，共请求 " + s_Written.Count + " 张。\n"
                + "  产物目录: " + s_OutputFolder + "\n"
                + "  截图由 ScreenCapture 异步写盘，可能比本日志晚几帧才出现。\n"
                + "  入库前建议: 单张控制在几百 KB 内；菜单 PirateCrew/美术评审/压缩评审图为 1600px 宽 可批量缩放。");
            EndRun();
        }

        static void RestoreState()
        {
            if (s_HudRoot != null)
                s_HudRoot.SetActive(s_HudWasActive);
            s_HudRoot = null;

            if (s_MainCamera != null)
                s_MainCamera.enabled = s_MainCameraWasEnabled;
            s_MainCamera = null;

            if (s_ReviewCamera != null)
            {
                GameObject go = s_ReviewCamera.gameObject;
                s_ReviewCamera = null;
                if (Application.isPlaying)
                    Object.Destroy(go);
                else
                    Object.DestroyImmediate(go);
            }
        }

        static void EndRun()
        {
            EditorApplication.update -= Tick;
            s_Phase = Phase.Idle;
            s_Running = false;
            s_Shots = null;
            SessionState.EraseBool(PendingPlaySessionKey);

            if (s_OwnsPlayMode && Application.isPlaying)
            {
                s_OwnsPlayMode = false;
                Debug.Log("[ArtReviewCapture] 退出 PlayMode。");
                EditorApplication.isPlaying = false;
            }
            s_OwnsPlayMode = false;
        }

        /// <summary>中断：报因、还原、退出 Play。绝不静默。</summary>
        static void Fail(string reason)
        {
            Debug.LogError("[ArtReviewCapture] 采集中断：" + reason
                + "\n  已请求 " + s_Written.Count + " 张；产物可能不完整，请到 " + s_OutputFolder
                + " 核对后重跑。相机/HUD/Play 状态会被还原。");

            RestoreState();
            EndRun();
        }

        /// <summary>校验产物是否真的落盘且非空，避免"静默产空白图"。</summary>
        static void VerifyArtifacts()
        {
            if (s_Written.Count == 0)
            {
                Debug.LogWarning("[ArtReviewCapture] 本轮没有产生任何截图（可能所有机位都被跳过）。");
                return;
            }

            int missing = 0;
            foreach (string path in s_Written)
            {
                if (!File.Exists(path))
                {
                    missing++;
                    Debug.LogError("[ArtReviewCapture] 产物未落盘: " + path
                        + "（ScreenCapture 失败或 Game View 不可见/尺寸异常）。");
                    continue;
                }

                long size = new FileInfo(path).Length;
                if (size < 2048)
                {
                    missing++;
                    Debug.LogError("[ArtReviewCapture] 产物过小疑似空白图: " + path + "（" + size + " 字节）。"
                        + "请确认 Game View 可见、分辨率正常且编辑器未暂停。");
                }
            }

            if (missing == 0)
                Debug.Log("[ArtReviewCapture] 产物校验通过：" + s_Written.Count + " 张均存在且非空。");
        }

        static string GetOutputFolder()
        {
            // Application.dataPath = <仓库根>/pirate-crew/Assets，上溯两级到仓库根。
            string projectRoot = Path.GetDirectoryName(Application.dataPath); // pirate-crew
            string repoRoot = Path.GetDirectoryName(projectRoot);              // 仓库根
            return Path.Combine(repoRoot, OutputFolderRelative).Replace('\\', '/');
        }
    }
}
