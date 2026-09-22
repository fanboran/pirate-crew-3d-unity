using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **遥控进 Play + 现场诊断**（评审辅助）：编辑器开着时，外部往标记文件里写一行命令，它就执行。
    /// 为什么要有它：<c>-executeMethod</c> 只在编辑器启动时跑一次，够不着一个已经开着的编辑器。
    ///
    /// 【协议】标记文件 <c>external/editor-remote-play.flag</c>（external 已整目录 gitignore），
    /// 一行一条命令，读到即删（防重复触发）：
    /// <list type="bullet">
    /// <item>场景名（如 <c>Bootstrapper</c>）→ 打开该场景并进 Play；</item>
    /// <item><c>play:&lt;关卡号|海图id&gt;</c> → **不依赖启动参数**直达样板关：
    ///       先 <see cref="PirateCrew.Battle.WorldMaps.WorldMapRuntime.SetPendingShowcase"/>，
    ///       再开 Battle 进 Play（编辑器不是本会话启动、argv 里没有 -bootBattle 时用这条）；</item>
    /// <item><c>stop</c> → 停 Play；</item>
    /// <item><c>diag</c> → 像素化路径现场取证：rig/三台相机状态 + Cast 视锥内物体数 +
    ///       ResultBuffer / AlbedoBuffer 落 PNG + Game 视图落 PNG，全写 <c>external/pixelart-diag/</c>。</item>
    /// </list>
    /// 进程参数里的 <c>-bootBattle</c> 照常生效（Bootstrapper 直跳那一关）。
    /// batchmode 下整趟禁用。
    /// </summary>
    [InitializeOnLoad]
    public static class RemotePlayControl
    {
        const string FlagPath = "../external/editor-remote-play.flag";
        const string DiagDir = "../external/pixelart-diag";
        const string BattleScene = "Assets/Scenes/Battle.unity";
        const double PollInterval = 0.25;
        static double _nextPoll;
        static bool _busy;

        static RemotePlayControl()
        {
            if (Application.isBatchMode)
                return;
            EditorApplication.update += Poll;
            Debug.Log("[RemotePlayControl] 遥控监听已装载（标记文件 " + FlagPath + "）。");
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + PollInterval;
            if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            string path;
            try { path = Path.GetFullPath(FlagPath); } catch { return; }
            if (!File.Exists(path))
                return;

            string content;
            try { content = File.ReadAllText(path).Trim(); File.Delete(path); }
            catch (IOException) { return; }     // 写方还没写完/被占用，下一轮再读
            if (string.IsNullOrEmpty(content))
                return;

            if (content == "stop")
            {
                _busy = true;
                Debug.Log("[RemotePlayControl] 收到 stop，停 Play。");
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => _busy = false;
                return;
            }

            if (content == "diag")
            {
                _busy = true;
                try { RunDiag(); }
                catch (System.Exception e) { Debug.LogError("[RemotePlayControl] diag 失败：" + e); }
                EditorApplication.delayCall += () => _busy = false;
                return;
            }

            if (content == "camdiag")
            {
                _busy = true;
                try { RunCamDiag(); }
                catch (System.Exception e) { Debug.LogError("[RemotePlayControl] camdiag 失败：" + e); }
                EditorApplication.delayCall += () => _busy = false;
                return;
            }

            if (content.StartsWith("play:"))
            {
                string target = content.Substring(5).Trim();
                _busy = true;
                if (int.TryParse(target, out int level) && level > 0)
                    PirateCrew.Battle.WorldMaps.WorldMapRuntime.SetPendingShowcase(level);
                else
                    PirateCrew.Battle.WorldMaps.WorldMapRuntime.SetPending(target);
                Debug.Log("[RemotePlayControl] 收到 play:" + target + "，开 Battle 进 Play（不依赖启动参数）。");
                EditorSceneManager.OpenScene(BattleScene, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
                EditorApplication.delayCall += () => _busy = false;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                string scenePath = "Assets/Scenes/" + content + ".unity";
                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[RemotePlayControl] 标记里的场景不存在：" + scenePath);
                    return;
                }

                _busy = true;
                Debug.Log("[RemotePlayControl] 收到 " + content + "，进 Play（-bootBattle "
                    + (CommandLineOptions.GetValue(ToolFlags.BootBattle) ?? "<无>") + "）。");
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
                EditorApplication.delayCall += () => _busy = false;
            }
        }

        // ---------------- diag：像素化路径现场取证 ----------------

        static void RunDiag()
        {
            string dir = Path.GetFullPath(DiagDir);
            Directory.CreateDirectory(dir);

            PixelartCameraRig rig = PixelartPath.ActiveRig;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("time=" + System.DateTime.Now.ToString("HH:mm:ss.fff")
                + "  playing=" + EditorApplication.isPlaying);

            if (rig == null)
            {
                sb.AppendLine("ActiveRig=<null> —— 没有 rig 在跑（场景不是 Battle / 路径没接线）");
                File.WriteAllText(Path.Combine(dir, "rig-dump.txt"), sb.ToString());
                return;
            }

            Camera screen = rig.ScreenCamera;
            sb.AppendLine("rig: IsReady=" + rig.IsReady
                + " DeviceOK=" + PixelartCameraRig.DeviceSupportsPath
                + " canvas=" + rig.RenderWidth + "x" + rig.RenderHeight
                + " fine=" + rig.FineWidth + "x" + rig.FineHeight);

            AppendCamera(sb, "screen", screen);
            AppendCamera(sb, "cast", FindCast(screen));

            // Cast 视锥内到底有多少东西：把"看得见"从玄学变成数字。
            int pixelartTotal = 0, pixelartInFrustum = 0, activeWorldTotal = 0;
            if (screen != null)
            {
                Camera cast = FindCast(screen);
                Plane[] planes = cast != null
                    ? GeometryUtility.CalculateFrustumPlanes(cast)
                    : null;
                foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (r.sharedMaterial == null || r.sharedMaterial.shader == null)
                        continue;
                    bool pixelart = r.sharedMaterial.shader.name == PixelartPath.ObjectShaderName;
                    if (pixelart) pixelartTotal++;
                    if (!r.enabled || !r.gameObject.activeInHierarchy)
                        continue;
                    activeWorldTotal++;
                    if (pixelart && planes != null
                        && GeometryUtility.TestPlanesAABB(planes, r.bounds))
                        pixelartInFrustum++;
                }
                sb.AppendLine("pixelartShader renderers: total=" + pixelartTotal
                    + " activeInHierarchy_world=" + activeWorldTotal
                    + " inCastFrustum=" + pixelartInFrustum);
            }

            File.WriteAllText(Path.Combine(dir, "rig-dump.txt"), sb.ToString());

            // 两张缓冲各落一张 PNG：Result 有物 = 上屏链路问题；Result 空 / Albedo 有物 = 着色合成断了；
            // Albedo 也空 = Cast 相机根本没画物体。
            CaptureRT(rig.ResultBuffer, Path.Combine(dir, "result.png"));
            CaptureRT(rig.AlbedoBuffer, Path.Combine(dir, "albedo.png"));
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(dir, "gameview.png")));
            Debug.Log("[RemotePlayControl] diag 完成 → " + dir);
        }

        static Camera FindCast(Camera screen)
        {
            if (screen == null)
                return null;
            foreach (Transform child in screen.transform)
            {
                Camera c = child.GetComponent<Camera>();
                if (c != null && c.name.Contains("Cast"))
                    return c;
            }
            return null;
        }

        static void AppendCamera(StringBuilder sb, string tag, Camera c)
        {
            if (c == null)
            {
                sb.AppendLine(tag + ": <null>");
                return;
            }
            UniversalAdditionalCameraData d = c.GetUniversalAdditionalCameraData();
            sb.AppendLine(tag + ": name=" + c.name
                + " enabled=" + c.enabled
                + " activeInHierarchy=" + c.gameObject.activeInHierarchy
                + " pos=" + c.transform.position.ToString("F2")
                + " rot=" + c.transform.eulerAngles.ToString("F1")
                + " ortho=" + c.orthographic + "/" + c.orthographicSize.ToString("F2")
                + " clip=" + c.nearClipPlane.ToString("F2") + ".." + c.farClipPlane.ToString("F1")
                + " mask=" + c.cullingMask
                + " clear=" + c.clearFlags
                + " display=" + c.targetDisplay
                + " rendererIndex=" + GetRendererIndex(c)
                + " targetTex=" + (c.targetTexture != null ? c.targetTexture.name : "<null>"));
        }

        // URP 14 的 UniversalAdditionalCameraData 没有公开的索引 getter（`renderer` 会解析到
        // 已废弃的 Component.renderer，CS0619），读索引只能反射。
        static int GetRendererIndex(Camera c)
        {
            UniversalAdditionalCameraData d = c.GetUniversalAdditionalCameraData();
            if (d == null)
                return -999;
            var prop = d.GetType().GetProperty("renderer");
            if (prop == null)
                return -999;
            try { return (int)prop.GetValue(d, null); }
            catch { return -999; }
        }

        // ---------------- camdiag：BattleCameraController 现场状态 ----------------

        /// <summary>反射捞相机控制器的私有状态 + 暂停标志：回答"输入链到底断在哪一层"。</summary>
        static void RunCamDiag()
        {
            string dir = Path.GetFullPath(DiagDir);
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();

            var cam = Object.FindFirstObjectByType<PirateCrew.Battle.BattleCameraController>();
            if (cam == null)
            {
                sb.AppendLine("BattleCameraController=<null>（活动对象里没有）—— 清点未激活与全部相机：");
                foreach (var any in Object.FindObjectsByType<PirateCrew.Battle.BattleCameraController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
                    sb.AppendLine("  控制器(未活动): " + any.name
                        + " activeInHierarchy=" + any.gameObject.activeInHierarchy
                        + " enabled=" + any.enabled);
            }
            else
            {
                var t = typeof(PirateCrew.Battle.BattleCameraController);
                foreach (string name in new[] {
                    "_manualCaptured", "_manualYaw", "_targetYaw",
                    "_manualOrthoSize", "_targetOrthoSize", "_baseOrthoSize",
                    "_baseDistance", "_dragPitchDegrees", "enableManualZoom", "orbitDegreesPerMouseUnit" })
                {
                    var fld = t.GetField(name, System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance);
                    sb.AppendLine(name + " = " + (fld != null ? fld.GetValue(cam)?.ToString() ?? "null" : "<无字段>"));
                }
                sb.AppendLine("ObserveMode = " + cam.ObserveMode);
                sb.AppendLine("BakedOrthoSize = " + cam.BakedOrthoSize);
                sb.AppendLine("mousePos = " + Input.mousePosition
                    + "  rightDown = " + Input.GetMouseButton(1));

                // ---- Cinemachine 链体检（反射，不依赖编辑器程序集引用 Cinemachine）----
                var fldVcam = t.GetField("virtualCamera", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                var vcam = fldVcam?.GetValue(cam) as Behaviour;
                if (vcam == null)
                {
                    sb.AppendLine("virtualCamera = <null> —— 控制器写的全是空气");
                }
                else
                {
                    sb.AppendLine("virtualCamera = " + vcam.name
                        + " activeInHierarchy=" + vcam.gameObject.activeInHierarchy
                        + " enabled=" + vcam.enabled);
                    sb.AppendLine("vcam 组件: " + string.Join(", ", vcam.GetComponents<Component>()
                        .Select(c => c == null ? "<缺失脚本>" : c.GetType().Name)));
                    var prio = vcam.GetType().GetProperty("Priority");
                    sb.AppendLine("vcam Priority = " + (prio != null ? prio.GetValue(vcam, null) : "?"));
                    var lensFld = vcam.GetType().GetField("m_Lens");
                    var lens = lensFld?.GetValue(vcam);
                    sb.AppendLine("vcam lens.OrthographicSize = " + lens?.GetType()
                        .GetField("OrthographicSize")?.GetValue(lens));
                }

                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    var comps = mainCam.GetComponents<Component>();
                    sb.AppendLine("Main Camera 组件: " + string.Join(", ", comps
                        .Select(c => c == null ? "<缺失脚本>" : c.GetType().Name)));
                    var brain = comps.FirstOrDefault(c => c != null && c.GetType().Name == "CinemachineBrain");
                    if (brain == null)
                        sb.AppendLine("CinemachineBrain = <无> —— 没人把虚机应用到主相机（旋转/缩放全部无效的充分原因）");
                    else
                    {
                        sb.AppendLine("Brain enabled = " + ((Behaviour)brain).enabled);
                        var liveProp = brain.GetType().GetProperty("ActiveVirtualCamera");
                        var live = liveProp?.GetValue(brain, null);
                        sb.AppendLine("Brain.ActiveVirtualCamera = "
                            + (live != null ? live.ToString() : "<null>"));
                    }
                }
            }

            var pauseType = typeof(PirateCrew.Battle.BattleCameraController).Assembly
                .GetType("PirateCrew.Battle.BattlePause");
            var pauseProp = pauseType != null ? pauseType.GetProperty("IsPaused") : null;
            sb.AppendLine("BattlePause.IsPaused = "
                + (pauseProp != null ? pauseProp.GetValue(null)?.ToString() : "<无>")
                + "  Time.timeScale = " + Time.timeScale);

            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine("Camera: " + c.name
                    + " active=" + c.gameObject.activeInHierarchy + " enabled=" + c.enabled
                    + " ortho=" + c.orthographic + "/" + c.orthographicSize.ToString("F1")
                    + " depth=" + c.depth + " display=" + c.targetDisplay);

            File.WriteAllText(Path.Combine(dir, "cam-dump.txt"), sb.ToString());
            Debug.Log("[RemotePlayControl] camdiag 完成 → " + dir);
        }

        static void CaptureRT(RenderTexture rt, string path)
        {            if (rt == null)
            {
                File.WriteAllText(path + ".txt", "null");
                return;
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
