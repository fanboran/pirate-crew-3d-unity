using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PirateCrew.Battle;
using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.CrewProbe
{
    /// <summary>
    /// **角色像素探针场**（`Assets/Scenes/Pixelart/PixelartCrewProbe.unity`）的运行时控制器。
    ///
    /// 【为什么要有这个场】战斗里一个约 10 艺术像素宽的角色，在 2× 档的块级量化下会偶尔出现
    /// "填充—灰/背景—墨线"的一像素漏缝（尤其头部左侧与身体底缘）。战斗场景里同一帧还叠着
    /// 选中件/弹道预览/UI，没法把锅分给某一层。本场把**同一个船员几何**单独放到绿幕前，
    /// 用**同一套实机像素管线**（PixelartCameraRig + Cast/Screen 渲染器 + 同一份 shader）
    /// 放大观察，并把每一层的中间缓冲原样落盘。
    ///
    /// 【和战斗同源的部分】（不要在这里另写一份近似口径）
    ///   · 角色：`Assets/Prefabs/PirateCrew/Crew/Crew.prefab` 的两件式几何，材质用队伍红 +
    ///     木色头；与战斗单位同一份网格/材质配方。
    ///   · 取景：可见米数 21.6（= 正交 10.8 = 战斗恒档）、像素档 2、俯角 30°、方位 45°（战斗 yaw 0
    ///     的基准机位）、相机焦点抬高 `CameraFraming.LookAtHeight`。
    ///   · 光照：运行时正午档 `#FFF4E0 / 1.55 / Euler(48,140)` + Trilight 环境（AmbientDirector
    ///     每局开场应用的那一组），主光不投影（与战斗一致）。
    ///
    /// 【操作】
    ///   · 编辑态：Game 视图直接是实机管线（rig 的 `runInEditMode` 打开）。选中本对象，在
    ///     Inspector 改 `azimuthDegrees` / `groundVariant` / `debugView`，`OnValidate` 即时应用；
    ///     ContextMenu「抓当前帧」「导出全部缓冲」也能用。
    ///   · 播放态：F1 面板、F8 角度扫描、F9 抓当前帧、F12 抓图；右键拖拽转方位。
    ///   · 产物：`<仓库根>/export/crew-probe/<时间戳>/`，包含 `result.png`、`probe_vis.png`
    ///     （红=漏缝、黑=墨线、灰=角色）、全部中间缓冲与 `probe.txt` 读数。
    ///
    /// 【度量口径】角色掩码 = Patch 版 albedo.a（有几何）且 Palette.a（逐物体描边开关）——
    /// 地面 `_OutlinePixels=0` 因此不会混进角色掩码。漏缝 = 角色与墨线之间夹着 1–3 个
    /// 既非角色也非墨线的像素（水平/垂直都查）。左右对称 = 以画布中轴镜像比较角色掩码/
    /// 墨线掩码；颜色镜像差异单独统计（它测的是光照，不是几何）。
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    public sealed class CrewProbeController : MonoBehaviour
    {
        public const string SceneName = "PixelartCrewProbe";

        /// <summary>参考屏幕高（worldPerPixel 换算分母；与 rig 同源）。</summary>
        public const int ReferenceScreenHeight = 1080;

        /// <summary>战斗恒档可见米数 = 2 × CameraFraming.CloseUpOrthoSize(11.25) = 22.5m
        /// （1m = 24 艺术像素；身/头栅格宽同奇偶 ⇒ 双轴重合在像素交界线 480.0）。</summary>
        public const float BattleVisibleMeters = 22.5f;

        /// <summary>战斗 prefab 的像素档。</summary>
        public const int BattlePixelScale = 2;

        /// <summary>俯角（与战斗/出图同一常量 30°）。</summary>
        public const float PitchDegrees = 30f;

        /// <summary>战斗 yaw 0 的基准方位（CameraFraming.OffsetDirectionForPitch 的 45°）。</summary>
        public const float BaseAzimuthDegrees = 45f;

        /// <summary>正交相机下只影响裁剪面；与战斗 BaseDistance 一致。</summary>
        public const float CameraDistance = 30f;

        public enum GroundVariant
        {
            None = 0,
            Flat = 1,
            SlopeTowardCamera = 2,
            SlopeAwayFromCamera = 3,
            Ridge = 4,
        }

        public enum ProbeBackground
        {
            Green = 0,
            White = 1,
            Black = 2,
        }

        /// <summary>
        /// 面板顺序用的连续枚举；写到 shader 的值见 <see cref="DebugModeValue"/>：
        /// 0–5 是着色/描边契约档，Gate/Cover/Branch 分别映射到描边自己的 8/9/10。
        /// </summary>
        public enum DebugView
        {
            Final = 0,
            Albedo = 1,
            Normal = 2,
            Palette = 3,
            OutlineMarker = 4,
            Connectivity = 5,
            OutlineGate = 6,
            Coverage = 7,
            OutlineBranches = 8,
        }

        [Header("场景引用（装配器接线）")]
        public PixelartCameraRig rig;
        public Transform cameraTransform;
        public Light sun;
        public GameObject subject;
        public GameObject groundFlat;
        public GameObject groundSlopeTowardCamera;
        public GameObject groundSlopeAwayFromCamera;
        public GameObject groundRidge;

        [Header("取景（默认 = 战斗同源）")]
        [Tooltip("可见米数（美术锚）。32 = 战斗恒档；调小会把角色放大，但会改变 1/4 块的相对尺度。")]
        [Range(4f, 64f)] public float visibleMeters = BattleVisibleMeters;

        [Tooltip("像素档位（1 艺术像素 = N×N 屏幕像素）。2 = 战斗 prefab 档。")]
        [Range(PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax)]
        public int pixelScale = BattlePixelScale;

        [Tooltip("相机方位角（度）。45 = 战斗 yaw 0 的基准机位；扫一圈可观察光照/网格相位。")]
        [Range(0f, 360f)] public float azimuthDegrees = BaseAzimuthDegrees;

        [Header("地面 / 背景")]
        [Tooltip("地面变体：平地把接触线摊平；坡/屋脊用来触发法线不连续的接触门控。")]
        public GroundVariant groundVariant = GroundVariant.Flat;

        [Tooltip("背景：绿幕 / 白 / 黑。绿幕用于抠图与一眼看剪影；灰地面与角色红形成对比。")]
        public ProbeBackground background = ProbeBackground.Green;

        [Header("对称校准（按艺术像素步进）")]
        [Tooltip("把角色沿相机 right 方向挪 N 个艺术像素（整数步进，过物体原点吸附后仍是整格）。"
            + "用来测“只差一格相位”的对称问题；图像/墨线左右差最小的一格就是最佳相位。")]
        [Range(-4, 4)] public int screenOffsetXPixels;

        [Tooltip("沿相机 up 方向挪 N 个艺术像素（会改变脚底与地面的接触，慎用）。")]
        [Range(-4, 4)] public int screenOffsetYPixels;

        [Header("头/身相对校准（按艺术像素步进）")]
        [Tooltip("只挪 HeadPivot（不动身体）：测头部底缘丢格透出身体红，是头/身两套原点各自吸附的相位差。")]
        [Range(-4, 4)] public int headOffsetXPixels;

        [Tooltip("只挪 HeadPivot 的屏幕 y；会改头颈重叠，慎用。")]
        [Range(-4, 4)] public int headOffsetYPixels;

        [Header("亚像素相位（探针专用）")]
        [Tooltip("整数艺术像素偏移之上再叠加的亚像素量（艺术像素，-1..1）。"
            + "只有关闭物体吸附时才真正进入光栅；用来扫出镜像对称的相位。")]
        [Range(-1f, 1f)] public float screenOffsetXSubpixels;

        [Tooltip("角色材质的 _SnapToPixelGrid。实机口径 = 开；做亚像素扫描时必须关，"
            + "否则亚像素位移会被物体原点吸附抵消。")]
        public bool snapSubjectToPixelGrid = true;


        [Header("调试")]
        [Tooltip("直通 `_PixelartDebugMode`。Final 之外的档位会把中间缓冲直接画到屏幕。")]
        public DebugView debugView = DebugView.Final;

        [Tooltip("纯覆盖膨胀描边：中心不描边+四邻域有描边物体即出墨，无门控。")]
        public bool simpleOutline = true;

        [Tooltip("主体材质的 _OutlinePixels。关掉 = 抓“无墨填充帧”，用于离线合成着色后描边的各线色模式。")]
        public bool subjectOutline = true;

        [Tooltip("播放态是否显示 IMGUI 面板（F1 切换）。编辑态用 Inspector + ContextMenu。")]
        public bool showPanel = true;

        [Header("导出")]
        [Tooltip("相对路径按仓库根解析（Application.dataPath/../..）。")]
        public string outputRoot = "export/crew-probe";

        [Tooltip("角度扫描步长（度）。5° = 72 帧；只在播放态跑。")]
        [Range(1f, 30f)] public float sweepStepDegrees = 5f;

        [Tooltip("扫描结束后对漏缝最多的角度再导一份全缓冲。")]
        public bool sweepDumpFullWorst = true;

        [Tooltip("单次抓图时同时用 ScreenCapture 存上屏图（播放态）。")]
        public bool captureScreenPng = true;

        // ---- 运行期 ----
        Camera _camera;
        bool _sweepRunning;
        bool _alignRunning;
        bool _subpixelRunning;
        string _status = "待命";
        Metrics _lastMetrics;
        float _lastCaptureYaw;
        int _savedPixelScaleUnit;
        bool _pixelScaleSaved;
        Vector3 _subjectBaseLocalPosition;
        bool _subjectBaseCaptured;
        Transform _headPivot;
        Vector3 _headBaseLocalPosition;
        bool _headBaseCaptured;
        readonly List<string> _logLines = new List<string>();

        /// <summary>漏缝/对称度量。坐标一律是**艺术画布**像素，y 从图像顶部往下。</summary>
        struct Metrics
        {
            public int Gaps;
            public int GapRuns;
            public int MaskMismatch;
            public int InkMismatch;
            public int ColorMismatch;
            public int CharPixels;
            public int ArtWidth;
            public int ArtHeight;
            public int BoundsMinX;
            public int BoundsMinY;
            public int BoundsMaxX;
            public int BoundsMaxY;
            public List<Vector2Int> GapCoords;
            public bool[] GapMask;

            // 头/身部件漏色：头部圆盘内出现身体红 = 头部底缘/侧缘丢格后透出身体。
            public int PartLeaks;
            public int HeadPixels;
            public int BodyPixels;
            public float HeadDiskCenterX;
            public float HeadDiskCenterY;
            public float HeadDiskRadius;

            // 轮廓对称的唯一验收口径：每行 char∪ink 左右外沿到画布中轴的距离差之和。
            public int ContourError;
            // 头/身分别的轮廓误差（块锚中心采样的部件掩码，各自逐行外沿）。
            public int BodyContourError;
            public int HeadContourError;
            // 内部横向墨段数（长度≥3、上下都有身体块）——头/身接缝双线。
            public int SeamInk;
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void OnEnable()
        {
            if (!_pixelScaleSaved)
            {
                _savedPixelScaleUnit = PixelScaleState.Unit;
                _pixelScaleSaved = true;
            }
            _camera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : GetComponent<Camera>();
            ApplyAll();
        }

        void OnDisable()
        {
            StopSweep();
            // 调试档是 shader 全局；离开探针场必须归零，否则别的场景会接着按调试档出图。
            Shader.SetGlobalFloat(PixelartPath.DebugModeId, 0f);
            if (_pixelScaleSaved)
            {
                PixelScaleState.Unit = _savedPixelScaleUnit;
                _pixelScaleSaved = false;
            }
        }

        void OnValidate()
        {
            // 编辑态滑杆/枚举一改就生效；播放态也允许在 Inspector 里调。
            ApplyAll();
        }

        void Start()
        {
            // 场景资产以 rig 禁用态保存（避免把运行期相机索引写进资产）；Play 里由这里启用。
            // 不能改成“打开场景时在编辑态先启用”：那会建一套 DontSave 相机/缓冲，
            // 进 Play 后可能留下陈旧相机，实拍表现为只剩绿背景、角色不进 G-buffer。
            if (rig != null && !rig.enabled)
                rig.enabled = true;
            ApplyAll();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            bool dragged = Input.GetMouseButton(1);
            if (dragged)
            {
                float dx = Input.GetAxis("Mouse X");
                if (Mathf.Abs(dx) > 1e-5f)
                {
                    azimuthDegrees = Mathf.Repeat(azimuthDegrees + dx * 2.0f, 360f);
                    ApplyCamera();
                }
            }
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            if (Input.GetKeyDown(KeyCode.F1))
                showPanel = !showPanel;
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (_sweepRunning || _subpixelRunning || _alignRunning)
                    StopSweep();
                else
                    StartSweep();
            }
            if (Input.GetKeyDown(KeyCode.F7))
                AutoAlignSymmetry();
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (_subpixelRunning)
                    StopSweep();
                else
                    StartSubpixelSweep();
            }
            if (Input.GetKeyDown(KeyCode.F9) || Input.GetKeyDown(KeyCode.F12))
                CaptureNow();
        }

        // ------------------------------------------------------------------
        // 应用
        // ------------------------------------------------------------------

        /// <summary>把面板/Inspector 的参数写进 rig、相机、地面与 shader 全局。</summary>
        public void ApplyAll()
        {
            if (rig == null)
                return;

            // rig 的 Update 会把 pixelScale 钳到 PixelScaleState.Unit；探针也要能改档，
            // 所以先写运行期真值，再写 rig。
            PixelScaleState.Unit = Mathf.Clamp(
                pixelScale, PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax);
            rig.pixelScale = PixelScaleState.Unit;
            rig.worldPerPixel = WorldPerPixel(visibleMeters, rig.pixelScale);

            Shader.SetGlobalFloat(PixelartPath.DebugModeId, DebugModeValue(debugView));
            Shader.SetGlobalFloat("_PixelartOutlineSimple", simpleOutline ? 1f : 0f);

            ApplyBackground();
            ApplyGround();
            ApplyCamera();
        }

        /// <summary>「可见多少米高」→ 一个艺术像素的世界尺寸（与 rig 的唯一换算式同源）。</summary>
        public static float WorldPerPixel(float visible, int scale)
        {
            return visible * Mathf.Max(1, scale) / ReferenceScreenHeight;
        }

        void ApplyBackground()
        {
            if (_camera == null)
                return;

            Color color;
            switch (background)
            {
                case ProbeBackground.White:
                    color = Color.white;
                    break;
                case ProbeBackground.Black:
                    color = Color.black;
                    break;
                default:
                    // 背景是 raw 写进线性 RT 的（渲染管线.md §6）；.linear 预补偿后上屏才是纯绿。
                    color = new Color(0f, 1f, 0f, 1f).linear;
                    break;
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = color;
        }

        void ApplyGround()
        {
            SetActive(groundFlat, groundVariant == GroundVariant.Flat);
            SetActive(groundSlopeTowardCamera, groundVariant == GroundVariant.SlopeTowardCamera);
            SetActive(groundSlopeAwayFromCamera, groundVariant == GroundVariant.SlopeAwayFromCamera);
            SetActive(groundRidge, groundVariant == GroundVariant.Ridge);
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }

        void ApplyCamera()
        {
            if (cameraTransform == null)
                return;

            // 与战斗同源：焦点 = 单位位置 + LookAtHeight；正交下相机距离只影响裁剪。
            Vector3 target = new Vector3(0f, CameraFraming.LookAtHeight, 0f);
            Vector3 direction = PixelartPilotScene.CameraDirection(PitchDegrees, azimuthDegrees);
            Vector3 position = target + direction * CameraDistance;
            Quaternion rotation = Quaternion.LookRotation(target - position, Vector3.up);

            if ((cameraTransform.position - position).sqrMagnitude > 1e-12f)
                cameraTransform.position = position;
            if (Quaternion.Angle(cameraTransform.rotation, rotation) > 1e-4f)
                cameraTransform.rotation = rotation;

            if (_camera == null)
                _camera = cameraTransform.GetComponent<Camera>();
            if (_camera != null)
            {
                // rig 的 deriveOrthographicSize 会在它自己的 Update 里按 RenderHeight×wpp
                // 重算；这里给编辑态/首帧一个同式兜底，避免 rig 尚未 Update 时视锥是旧的。
                _camera.orthographic = true;
                float derived = visibleMeters * Mathf.Max(1, Screen.height) / 2160f;
                if (_camera.orthographicSize <= 0f || !Application.isPlaying)
                    _camera.orthographicSize = derived;

                // 与战斗相机逐参对齐（BattleCameraDriver:295 同源）：近 0.1 / 远 200。
                // 远裁剪改变归一化深度范围，连通域的 closer 判定对它有量级敏感；
                // 探针若停在场景默认的 300，抓出来的描边门控就不是局内那套。
                _camera.nearClipPlane = CameraFraming.OrthoNearClip;
                _camera.farClipPlane = CameraFraming.OrthoFarClip;
                if (rig != null && rig.CastCamera != null)
                {
                    rig.CastCamera.nearClipPlane = CameraFraming.OrthoNearClip;
                    rig.CastCamera.farClipPlane = CameraFraming.OrthoFarClip;
                }
            }

            ApplySubjectOffset();
        }

        /// <summary>
        /// 把角色沿相机 right/up 挪 N 个**艺术像素**。物体原点吸附会把整块吸到格点，
        /// 所以整数步进正好是"换一格相位"；用来测头部/底缘的丢格是不是只差这一格。
        /// </summary>
        void ApplySubjectOffset()
        {
            if (subject == null || cameraTransform == null)
                return;

            if (!_subjectBaseCaptured)
            {
                _subjectBaseLocalPosition = subject.transform.localPosition;
                _subjectBaseCaptured = true;
            }

            float unit = rig != null && rig.UnitSize > 0f
                ? rig.UnitSize
                : WorldPerPixel(visibleMeters, pixelScale);
            Vector3 offset =
                cameraTransform.right * ((screenOffsetXPixels + screenOffsetXSubpixels) * unit)
                + cameraTransform.up * (screenOffsetYPixels * unit);
            subject.transform.localPosition = _subjectBaseLocalPosition + offset;
            ApplyObjectSnapFlag();

            // 头/身相对相位：只挪 HeadPivot。头与身体是两套 renderer，各自的 UNITY_MATRIX_M
            // 原点独立吸附；头底的 1/4 块丢格会让身体红透出来。整体平移改不了这个相对相位。
            if (_headPivot == null)
            {
                MeshRenderer head = FindHeadRenderer();
                if (head != null)
                    _headPivot = head.transform.parent;
            }
            if (_headPivot != null)
            {
                if (!_headBaseCaptured)
                {
                    _headBaseLocalPosition = _headPivot.localPosition;
                    _headBaseCaptured = true;
                }
                Vector3 headOffset =
                    cameraTransform.right * (headOffsetXPixels * unit)
                    + cameraTransform.up * (headOffsetYPixels * unit);
                _headPivot.localPosition = _headBaseLocalPosition + headOffset;
            }
        }

        static readonly int SnapToPixelGridId = Shader.PropertyToID("_SnapToPixelGrid");
        static readonly int OutlinePixelsId = Shader.PropertyToID("_OutlinePixels");

        /// <summary>
        /// 逐 renderer 覆盖 `_SnapToPixelGrid` / `_SnapPhase`（MPB，不克隆/不弄脏共享材质）。
        /// 扫描亚像素相位时必须关掉物体吸附，否则物体原点会被 round 回整艺术像素，亚像素位移消失；
        /// 开吸附验证 −0.25 相位时则把相位写进 MPB，复现实机的格点对齐。
        /// </summary>
        void ApplyObjectSnapFlag()
        {
            if (subject == null)
                return;

            float snap = snapSubjectToPixelGrid ? 1f : 0f;
            MeshRenderer[] renderers = subject.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var block = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(block);
                block.SetFloat(SnapToPixelGridId, snap);
                block.SetFloat(OutlinePixelsId, subjectOutline ? 1f : 0f);
                renderers[i].SetPropertyBlock(block);
            }
        }

        static float DebugModeValue(DebugView view)
        {
            switch (view)
            {
                case DebugView.OutlineGate: return 8f;
                case DebugView.Coverage: return 9f;
                case DebugView.OutlineBranches: return 10f;
                default: return (float)(int)view;
            }
        }

        // ------------------------------------------------------------------
        // 抓图 / 导出
        // ------------------------------------------------------------------

        [ContextMenu("抓当前帧（导出全缓冲）")]
        public void CaptureNow()
        {
            string dir = NewSessionDir("capture");
            Capture(dir, full: true, saveScreen: captureScreenPng);
        }

        [ContextMenu("导出全部缓冲（当前帧）")]
        public void DumpBuffersNow()
        {
            string dir = NewSessionDir("dump");
            Capture(dir, full: true, saveScreen: captureScreenPng);
        }

        [ContextMenu("开始角度扫描（仅播放态）")]
        public void StartSweep()
        {
            if (!Application.isPlaying)
            {
                Log.Warn("[CrewProbe] 角度扫描需要 Play（编辑态请拖 azimuth 滑杆逐角度看，或按 F9 抓当前帧）。");
                return;
            }
            if (_sweepRunning || _alignRunning || _subpixelRunning)
                return;
            StartCoroutine(RunSweep());
        }

        [ContextMenu("停止扫描")]
        public void StopSweep()
        {
            if (!_sweepRunning && !_alignRunning && !_subpixelRunning)
                return;
            StopAllCoroutines();
            _sweepRunning = false;
            _alignRunning = false;
            _subpixelRunning = false;
            _status = "扫描已停止";
            Log.Info("[CrewProbe] 扫描已停止。");
        }

        /// <summary>
        /// 只搜屏幕 x 方向的整艺术像素偏移（不搜 y）：y 会改变脚底与地面的接触，可能用“把角色
        /// 抬起来”来假装修好；x 才是剪影左右镜像的相位。评分优先部件漏色，其次漏缝/镜像差。
        /// </summary>
        [ContextMenu("自动对称校准（仅播放态）")]
        public void AutoAlignSymmetry()
        {
            if (!Application.isPlaying)
            {
                Log.Warn("[CrewProbe] 自动对称校准需要 Play（要逐帧重渲染）；编辑态请在 Inspector 调 screenOffsetXPixels，"
                    + "每改一次用「抓当前帧」对比 probe.txt。");
                return;
            }
            if (_alignRunning || _sweepRunning || _subpixelRunning)
                return;
            if (rig == null || !rig.IsReady)
            {
                Log.Error("[CrewProbe] 对称校准需要像素路径就绪（rig.IsReady=false）。");
                return;
            }
            StartCoroutine(RunAutoAlign());
        }

        IEnumerator RunAutoAlign()
        {
            _alignRunning = true;
            _status = "对称校准中…";
            string root = Path.Combine(OutputRootAbsolute(), "align-" + Stamp());
            Directory.CreateDirectory(root);

            int originalX = screenOffsetXPixels;
            int originalY = screenOffsetYPixels;
            int bestX = originalX;
            int bestY = 0;
            int bestScore = int.MaxValue;
            Metrics bestMetrics = default;

            Log.Info("[CrewProbe] 对称校准开始（x = -2..2，y 固定 0）：" + root);

            for (int dx = -2; dx <= 2 && _alignRunning; dx++)
            {
                screenOffsetXPixels = dx;
                screenOffsetYPixels = 0;
                ApplyCamera();
                yield return null;
                yield return new WaitForEndOfFrame();

                string dir = Path.Combine(root, "x" + (dx >= 0 ? "+" : "") + dx);
                Metrics m = Capture(dir, full: false, saveScreen: false);
                int score = m.PartLeaks * 100 + m.Gaps * 10 + m.MaskMismatch + m.InkMismatch;
                Log.Info("[CrewProbe] 对齐 x=" + dx + "：partLeaks=" + m.PartLeaks
                    + " gaps=" + m.Gaps + " mask=" + m.MaskMismatch
                    + " ink=" + m.InkMismatch + " score=" + score);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestX = dx;
                    bestY = 0;
                    bestMetrics = m;
                }
                yield return null;
            }

            if (!_alignRunning)
                yield break;

            screenOffsetXPixels = bestX;
            screenOffsetYPixels = bestY;
            ApplyCamera();
            yield return null;
            yield return new WaitForEndOfFrame();
            Capture(Path.Combine(root, "best"), full: true, saveScreen: captureScreenPng);

            _alignRunning = false;
            _status = "对称校准完成：offset=" + bestX + "," + bestY
                + " partLeak=" + bestMetrics.PartLeaks + " gap=" + bestMetrics.Gaps;
            Log.Info("[CrewProbe] 对称校准完成：最佳 x=" + bestX
                + "（原 " + originalX + "，y 原 " + originalY + "），partLeaks=" + bestMetrics.PartLeaks
                + " gaps=" + bestMetrics.Gaps + " mask=" + bestMetrics.MaskMismatch
                + " ink=" + bestMetrics.InkMismatch + "，产物 " + root);
        }

        /// <summary>
        /// **亚像素相位扫描**（Play）：关掉角色材质的 `_SnapToPixelGrid`，沿相机 right 方向
        /// 从 −1 到 +1 艺术像素、0.05 步进移动整个角色，每步重渲染并记录
        /// gaps / mask / ink / partLeak / color。这是“垂直镜头方向一点一点挪”的真机测试。
        ///
        /// 数学判据（块中心采样）：艺术像素 i 的采样点是 `i + floor(k/2)/k`；镜像线 c 必须满足
        /// `2c ≡ 2·floor(k/2)/k (mod 1)`，即 `c = floor(k/2)/k + n/2`（艺术像素单位）。
        /// 物体吸附把轴线放在**整数** c：偶数 k（战斗 2×）时 `floor(k/2)/k = 0.5`，整数/半整数
        /// 都是镜像线，整数合法；奇数 k（如 3×）时 `floor(k/2)/k = 1/3`，整数**不是**镜像线，
        /// 轴在整数上本来就会光栅化歪——扫描日志会把理论偏移写出来对照。
        /// </summary>
        [ContextMenu("亚像素相位扫描（仅播放态）")]
        public void StartSubpixelSweep()
        {
            if (!Application.isPlaying)
            {
                Log.Warn("[CrewProbe] 亚像素扫描需要 Play（要逐帧重渲染）。");
                return;
            }
            if (_subpixelRunning || _sweepRunning || _alignRunning)
                return;
            // 探针场景曾经在“rig 禁用”状态下被保存（m_Enabled:0），Play 后没有缓冲；
            // 扫描入口自己兜底启用一次，避免“桥 call 进来只报 rig.IsReady=false”。
            if (rig != null && !rig.enabled)
                rig.enabled = true;
            if (rig == null || !rig.IsReady)
            {
                Log.Error("[CrewProbe] 亚像素扫描需要像素路径就绪（rig.IsReady=false）。");
                return;
            }
            StartCoroutine(RunSubpixelSweep());
        }

        /// <summary>给编辑器桥/菜单调的静态入口：找到场景里的探针并启动亚像素扫描。</summary>
        public static void StartSubpixelSweepInScene()
        {
            CrewProbeController controller = Object.FindFirstObjectByType<CrewProbeController>();
            if (controller == null)
            {
                Log.Error("[CrewProbe] 场景里没有 CrewProbeController，无法启动亚像素扫描。");
                return;
            }
            controller.StartSubpixelSweep();
        }

        /// <summary>抓一张“无墨填充帧”（主体 _OutlinePixels=0），用于着色后描边的模式对比。</summary>
        public static void CaptureNoOutlineInScene()
        {
            CrewProbeController controller = Object.FindFirstObjectByType<CrewProbeController>();
            if (controller == null)
            {
                Log.Error("[CrewProbe] 场景里没有 CrewProbeController，无法抓帧。");
                return;
            }
            controller.subjectOutline = false;
            controller.ApplyAll();
            // MPB 只影响下一帧的渲染；必须等一帧再抓，否则读到的还是上一帧带墨的缓冲。
            controller.StartCoroutine(controller.CaptureNoOutlineRoutine());
        }

        IEnumerator CaptureNoOutlineRoutine()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            CaptureNow();
            subjectOutline = true;
            ApplyAll();
        }

        /// <summary>抓一帧着色趟眼里的 albedo 覆盖（DebugMode=1）：
        /// 「覆盖说有、画面没有」的像素级仲裁——着色采到的覆盖 ≠ albedo 缓冲原始值时一目了然。</summary>
        public static void CaptureCoverageDebugInScene()
        {
            CrewProbeController controller = Object.FindFirstObjectByType<CrewProbeController>();
            if (controller == null)
            {
                Log.Error("[CrewProbe] 场景里没有 CrewProbeController，无法抓帧。");
                return;
            }
            controller.StartCoroutine(controller.CaptureCoverageDebugRoutine());
        }

        IEnumerator CaptureCoverageDebugRoutine()
        {
            var savedView = debugView;
            debugView = DebugView.Albedo;
            ApplyAll();
            yield return null;
            yield return new WaitForEndOfFrame();
            CaptureNow();
            debugView = savedView;
            ApplyAll();
        }

        /// <summary>给编辑器桥/菜单调的静态入口：抓当前帧（完整缓冲）。</summary>
        public static void CaptureNowInScene()
        {
            CrewProbeController controller = Object.FindFirstObjectByType<CrewProbeController>();
            if (controller == null)
            {
                Log.Error("[CrewProbe] 场景里没有 CrewProbeController，无法抓帧。");
                return;
            }
            controller.CaptureNow();
        }

        IEnumerator RunSubpixelSweep()
        {
            _subpixelRunning = true;
            _status = "亚像素扫描中…";
            string root = Path.Combine(OutputRootAbsolute(), "subpixel-" + Stamp());
            Directory.CreateDirectory(root);

            bool oldSnap = snapSubjectToPixelGrid;
            float oldSub = screenOffsetXSubpixels;
            snapSubjectToPixelGrid = false;
            screenOffsetXSubpixels = 0f;

            int k = Mathf.Max(1, rig.PixelScale);
            float a = (k / 2) / (float)k;   // floor(k/2)/k：k=2→0.5，k=3→1/3
            var mirrorMath = new StringBuilder();
            mirrorMath.AppendLine("k=" + k + " block-center a=floor(k/2)/k=" + a.ToString("F4", CultureInfo.InvariantCulture));
            mirrorMath.AppendLine("mirror lines c = a + n*0.5 (art px); object snap base c0 is integer");
            mirrorMath.AppendLine("valid sub-pixel offsets in [-1,1]:");
            for (int n = -4; n <= 4; n++)
            {
                float c = a + n * 0.5f;
                if (c >= -1f - 1e-4f && c <= 1f + 1e-4f)
                    mirrorMath.AppendLine("  " + c.ToString("F4", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(Path.Combine(root, "mirror-math.txt"), mirrorMath.ToString());

            var csv = new StringBuilder();
            csv.AppendLine("offsetArtPx,snap,gaps,maskMismatch,inkMismatch,partLeaks,colorMismatch,contourError,bodyContour,headContour,seamInk,score");

            int bestScore = int.MaxValue;
            float bestOffset = 0f;
            Metrics bestMetrics = default;
            const float step = 0.05f;

            Log.Info("[CrewProbe] 亚像素扫描开始：k=" + k + "，a=" + a.ToString("F4", CultureInfo.InvariantCulture)
                + "，步长 " + step + "，输出 " + root);

            for (float offset = -1f; offset <= 1f + 1e-4f && _subpixelRunning; offset += step)
            {
                screenOffsetXSubpixels = offset;
                ApplyCamera();
                yield return null;
                yield return new WaitForEndOfFrame();

                string dir = Path.Combine(root, "x" + offset.ToString("+0.00;-0.00", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(dir);
                Metrics m = Capture(dir, full: false, saveScreen: false);
                int score = m.PartLeaks * 100 + m.Gaps * 50 + m.MaskMismatch * 10 + m.InkMismatch * 20;
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},0,{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}",
                    offset, m.Gaps, m.MaskMismatch, m.InkMismatch, m.PartLeaks,
                    m.ColorMismatch, m.ContourError, m.BodyContourError,
                    m.HeadContourError, m.SeamInk, score));

                if (score < bestScore)
                {
                    bestScore = score;
                    bestOffset = offset;
                    bestMetrics = m;
                }
                yield return null;
            }

            File.WriteAllText(Path.Combine(root, "subpixel.csv"), csv.ToString());

            if (!_subpixelRunning)
                yield break;

            screenOffsetXSubpixels = bestOffset;
            ApplyCamera();
            yield return null;
            yield return new WaitForEndOfFrame();
            Capture(Path.Combine(root, "best"), full: true, saveScreen: captureScreenPng);

            // 扫描结束后回到实机口径（开吸附），否则探针会话会停在“吸附关 + 最佳亚像素”
            // 的实验室状态，后续抓帧不再是战斗同源。
            screenOffsetXSubpixels = oldSub;
            snapSubjectToPixelGrid = oldSnap;
            ApplyCamera();

            _subpixelRunning = false;
            _status = "亚像素扫描完成：best x=" + bestOffset.ToString("F2", CultureInfo.InvariantCulture)
                + " contour=" + bestMetrics.ContourError
                + " seam=" + bestMetrics.SeamInk
                + " gap=" + bestMetrics.Gaps + " partLeak=" + bestMetrics.PartLeaks;
            Log.Info("[CrewProbe] 亚像素扫描完成：k=" + k
                + "，最佳 sub=" + bestOffset.ToString("F2", CultureInfo.InvariantCulture)
                + "，gaps=" + bestMetrics.Gaps + " mask=" + bestMetrics.MaskMismatch
                + " ink=" + bestMetrics.InkMismatch + " partLeaks=" + bestMetrics.PartLeaks
                + " contour=" + bestMetrics.ContourError + " seamInk=" + bestMetrics.SeamInk
                + "（身 " + bestMetrics.BodyContourError
                + " / 头 " + bestMetrics.HeadContourError + "）"
                + "（理论镜像偏移见 " + Path.Combine(root, "mirror-math.txt") + "）");
        }

        IEnumerator RunSweep()
        {
            _sweepRunning = true;
            string root = Path.Combine(OutputRootAbsolute(), "sweep-" + Stamp());
            Directory.CreateDirectory(root);

            var csv = new StringBuilder();
            csv.AppendLine("yawOffset,azimuthDegrees,gaps,gapRuns,maskMismatch,inkMismatch,colorMismatch,charPixels");

            float start = azimuthDegrees;
            float step = Mathf.Max(1f, sweepStepDegrees);
            int worstGaps = -1;
            float worstYaw = 0f;
            string worstDir = null;

            _status = "扫描中…";
            Log.Info("[CrewProbe] 角度扫描开始：步长 " + step + "°，输出 " + root);

            for (float yaw = 0f; yaw < 360f && _sweepRunning; yaw += step)
            {
                azimuthDegrees = Mathf.Repeat(BaseAzimuthDegrees + yaw, 360f);
                ApplyCamera();
                yield return null;
                yield return new WaitForEndOfFrame();

                string dir = Path.Combine(root, "yaw-" + Mathf.RoundToInt(yaw).ToString("000", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(dir);
                Metrics m = Capture(dir, full: false, saveScreen: false);

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F1},{1:F1},{2},{3},{4},{5},{6},{7}",
                    yaw, azimuthDegrees, m.Gaps, m.GapRuns, m.MaskMismatch,
                    m.InkMismatch, m.ColorMismatch, m.CharPixels));

                if (m.Gaps > worstGaps)
                {
                    worstGaps = m.Gaps;
                    worstYaw = yaw;
                    worstDir = dir;
                }

                yield return null;
            }

            File.WriteAllText(Path.Combine(root, "sweep.csv"), csv.ToString());

            if (sweepDumpFullWorst && worstDir != null && _sweepRunning)
            {
                azimuthDegrees = Mathf.Repeat(BaseAzimuthDegrees + worstYaw, 360f);
                ApplyCamera();
                yield return null;
                yield return new WaitForEndOfFrame();
                Capture(Path.Combine(root, "worst-yaw-" + Mathf.RoundToInt(worstYaw).ToString("000", CultureInfo.InvariantCulture)),
                    full: true, saveScreen: captureScreenPng);
            }

            azimuthDegrees = start;
            ApplyCamera();
            _sweepRunning = false;
            _status = "扫描完成：最差 yaw=" + worstYaw.ToString("F0", CultureInfo.InvariantCulture)
                + "°，gaps=" + worstGaps;
            Log.Info("[CrewProbe] 角度扫描完成。最差 yaw=" + worstYaw.ToString("F1", CultureInfo.InvariantCulture)
                + "°，gaps=" + worstGaps + "，产物 " + root);
        }

        /// <summary>抓一帧：度量 + 落盘。返回度量值（rig 未就绪时返回空结构）。</summary>
        Metrics Capture(string dir, bool full, bool saveScreen)
        {
            Directory.CreateDirectory(dir);

            if (rig == null || !rig.IsReady
                || rig.ResultBuffer == null || rig.OutlineClosedBuffer == null
                || rig.AlbedoPatchBuffer == null || rig.PalettePatchBuffer == null)
            {
                Log.Error("[CrewProbe] 像素路径未就绪（rig/缓冲为空），本次不落盘。"
                    + " 编辑态请确认 Game 视图可见且 rig 的 runInEditMode 已打开。");
                _status = "像素路径未就绪";
                return default;
            }

            int artW = rig.RenderWidth;
            int artH = rig.RenderHeight;
            int fineW = rig.FineWidth;
            int fineH = rig.FineHeight;
            int k = Mathf.Max(1, rig.PixelScale);

            Color32[] albedo = ReadColor(rig.AlbedoPatchBuffer, out int aw, out int ah);
            Color32[] palette = ReadColor(rig.PalettePatchBuffer, out int pw, out int ph);
            // 必须读**本帧真正发布**的墨线：着色读哪张，度量就读哪张。
            // simple 模式跳过闭合趟，Closed 从未写过；直接读它会把墨线度量变成“没有墨线可比较”的空真。
            RenderTexture outlineRt = rig.OutlineClosedReady
                ? rig.OutlineClosedBuffer
                : rig.OutlineBuffer;
            Color32[] outline = ReadColor(outlineRt, out int ow, out int oh);
            Color32[] result = ReadColor(rig.ResultBuffer, out int rw, out int rh);

            if (aw != fineW || ah != fineH || pw != fineW || ph != fineH
                || ow != artW || oh != artH || rw != artW || rh != artH)
            {
                Log.Warn("[CrewProbe] 缓冲尺寸与 rig 记录不一致：albedo "
                    + aw + "×" + ah + " / palette " + pw + "×" + ph
                    + " / outline " + ow + "×" + oh + " / result " + rw + "×" + rh
                    + "（期望 fine " + fineW + "×" + fineH + "、art " + artW + "×" + artH + "）。"
                    + " 度量按实际缓冲尺寸继续。");
                fineW = aw;
                fineH = ah;
                artW = ow;
                artH = oh;
            }

            bool[] charMask = new bool[artW * artH];
            bool[] inkMask = new bool[artW * artH];
            bool[] bodyMask = new bool[artW * artH];
            bool[] headMask = new bool[artW * artH];
            // 块锚中心采样的部件掩码：头/身分别做轮廓对称用（块内任一细像素会把
            // 接缝处被头遮住的身体红也算进头块，污染逐行外沿）。
            bool[] bodyArtMask = new bool[artW * artH];
            bool[] headArtMask = new bool[artW * artH];

            // 头/身部件色：albedo 缓冲存的是材质 _BaseColor（线性、未吃光照），
            // 但不同 dump/回读路径可能给出 sRGB 或线性字节，两套参考都参与最近色匹配，
            // 避免把 (255,11,6) 误判成非红（历史上这里只写线性、结果 bodyPixels=0）。
            Color32 bodyColorSrgb = new Color32(255, 58, 41, 255);     // #FF3A29
            Color32 bodyColorLinear = LinearFromSrgb(255, 58, 41);
            Color32 headColorSrgb = new Color32(212, 167, 106, 255);   // #D4A76A
            Color32 headColorLinear = LinearFromSrgb(212, 167, 106);
            const int partColorToleranceSq = 45 * 45;

            for (int y = 0; y < artH; y++)
            {
                for (int x = 0; x < artW; x++)
                {
                    int fx = Mathf.Clamp(x * k + k / 2, 0, fineW - 1);
                    int fy = Mathf.Clamp(y * k + k / 2, 0, fineH - 1);
                    int fi = fy * fineW + fx;
                    int ai = y * artW + x;
                    charMask[ai] = albedo[fi].a >= 128 && palette[fi].a > 127;
                    inkMask[ai] = outline[ai].r > 127;

                    // 块锚中心采样只用于“头/身各自轮廓对称”；
                    // 部件漏色（partLeaks）仍用块内任一细像素，避免 1/4 块漏红被判空。
                    bool centerBody, centerHead;
                    ClassifyPart(albedo[fi], bodyColorSrgb, bodyColorLinear,
                        headColorSrgb, headColorLinear, partColorToleranceSq,
                        out centerBody, out centerHead);
                    if (centerBody) bodyArtMask[ai] = true;
                    if (centerHead) headArtMask[ai] = true;

                    for (int sy = 0; sy < k; sy++)
                    {
                        for (int sx = 0; sx < k; sx++)
                        {
                            int pfxi = Mathf.Clamp(x * k + sx, 0, fineW - 1);
                            int pfyi = Mathf.Clamp(y * k + sy, 0, fineH - 1);
                            bool body, head;
                            ClassifyPart(albedo[pfyi * fineW + pfxi],
                                bodyColorSrgb, bodyColorLinear,
                                headColorSrgb, headColorLinear, partColorToleranceSq,
                                out body, out head);
                            if (body) bodyMask[ai] = true;
                            if (head) headMask[ai] = true;
                        }
                    }
                }
            }

            Metrics metrics = ComputeMetrics(charMask, inkMask, result, artW, artH);
            ComputePartMetrics(bodyMask, headMask, inkMask,
                bodyArtMask, headArtMask, artW, artH, ref metrics);

            // ---- 必存：最终图 + 掩码 + 可视化 ----
            SavePixels(Gamma(result), artW, artH, Path.Combine(dir, "result.png"));
            SaveMask(charMask, artW, artH, Path.Combine(dir, "char_mask.png"));
            SaveMask(inkMask, artW, artH, Path.Combine(dir, "ink_mask.png"));
            SaveMask(metrics.GapMask, artW, artH, Path.Combine(dir, "gap_mask.png"));
            SaveMask(bodyMask, artW, artH, Path.Combine(dir, "body_mask.png"));
            SaveMask(headMask, artW, artH, Path.Combine(dir, "head_mask.png"));
            SaveProbeVisual(metrics, charMask, inkMask, artW, artH, Path.Combine(dir, "probe_vis.png"));
            SavePartVisual(metrics, charMask, inkMask, bodyMask, headMask, artW, artH,
                Path.Combine(dir, "part_vis.png"));

            if (full)
                SaveAllBuffers(dir, artW, artH, fineW, fineH, albedo, palette, outline);

            WriteReport(dir, metrics, artW, artH, fineW, fineH, k);

            if (saveScreen && Application.isPlaying)
            {
                // ScreenCapture 是异步写盘；给的是上屏后的正确 sRGB 图。
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "screen.png"));
            }

            _lastMetrics = metrics;
            _lastCaptureYaw = azimuthDegrees;
            _status = "抓帧完成：gaps=" + metrics.Gaps + " / 部件漏色=" + metrics.PartLeaks;
            Log.Info("[CrewProbe] 抓帧完成 " + dir
                + "：gaps=" + metrics.Gaps + "（" + metrics.GapRuns + " 段）"
                + "，部件漏色=" + metrics.PartLeaks
                + "，角色掩码左右差=" + metrics.MaskMismatch
                + "，墨线左右差=" + metrics.InkMismatch
                + "，颜色左右差=" + metrics.ColorMismatch
                + "，轮廓误差=" + metrics.ContourError
                + "（身 " + metrics.BodyContourError + " / 头 " + metrics.HeadContourError + "）"
                + "，接缝墨段=" + metrics.SeamInk
                + "，角色像素=" + metrics.CharPixels
                + "，offset=" + screenOffsetXPixels + "," + screenOffsetYPixels
                + "，subX=" + screenOffsetXSubpixels.ToString("F3", CultureInfo.InvariantCulture)
                + "，snap=" + snapSubjectToPixelGrid
                + "，headOffset=" + headOffsetXPixels + "," + headOffsetYPixels);
            return metrics;
        }

        /// <summary>
        /// 漏缝与镜像度量。漏缝 = 角色与墨线之间夹着 1–3 个既非角色也非墨线的像素
        /// （水平/垂直都查）。镜像轴 = 画布中轴（相机焦点在角色轴线上）。
        /// </summary>
        static Metrics ComputeMetrics(bool[] charMask, bool[] inkMask, Color32[] result, int w, int h)
        {
            var metrics = new Metrics
            {
                ArtWidth = w,
                ArtHeight = h,
                GapCoords = new List<Vector2Int>(64),
                BoundsMinX = w,
                BoundsMinY = h,
                BoundsMaxX = -1,
                BoundsMaxY = -1,
            };

            bool[] gapMask = new bool[w * h];
            metrics.GapMask = gapMask;

            // ---- 水平漏缝：空 run 两端分别是角色/墨线，run ≤ 3 ----
            for (int y = 0; y < h; y++)
            {
                int x = 0;
                while (x < w)
                {
                    int i = y * w + x;
                    if (charMask[i] || inkMask[i])
                    {
                        x++;
                        continue;
                    }

                    int start = x;
                    while (x < w && !charMask[y * w + x] && !inkMask[y * w + x])
                        x++;
                    int end = x - 1;

                    if (end - start + 1 <= 3 && start > 0 && x < w)
                    {
                        bool leftChar = charMask[y * w + start - 1];
                        bool leftInk = inkMask[y * w + start - 1];
                        bool rightChar = charMask[y * w + x];
                        bool rightInk = inkMask[y * w + x];
                        if ((leftChar && rightInk) || (leftInk && rightChar))
                        {
                            metrics.GapRuns++;
                            for (int i2 = start; i2 <= end; i2++)
                                gapMask[y * w + i2] = true;
                        }
                    }
                }
            }

            // ---- 垂直漏缝 ----
            for (int x = 0; x < w; x++)
            {
                int y = 0;
                while (y < h)
                {
                    int i = y * w + x;
                    if (charMask[i] || inkMask[i])
                    {
                        y++;
                        continue;
                    }

                    int start = y;
                    while (y < h && !charMask[y * w + x] && !inkMask[y * w + x])
                        y++;
                    int end = y - 1;

                    if (end - start + 1 <= 3 && start > 0 && y < h)
                    {
                        bool topChar = charMask[(start - 1) * w + x];
                        bool topInk = inkMask[(start - 1) * w + x];
                        bool bottomChar = charMask[y * w + x];
                        bool bottomInk = inkMask[y * w + x];
                        if ((topChar && bottomInk) || (topInk && bottomChar))
                        {
                            metrics.GapRuns++;
                            for (int i2 = start; i2 <= end; i2++)
                                gapMask[i2 * w + x] = true;
                        }
                    }
                }
            }

            // ---- 角色 bbox + 镜像 ----
            int axis = w / 2;
            int bboxMinX = w, bboxMinY = h, bboxMaxX = -1, bboxMaxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (charMask[i])
                    {
                        metrics.CharPixels++;
                        if (x < bboxMinX) bboxMinX = x;
                        if (x > bboxMaxX) bboxMaxX = x;
                        if (y < bboxMinY) bboxMinY = y;
                        if (y > bboxMaxY) bboxMaxY = y;
                    }
                    if (gapMask[i])
                    {
                        metrics.Gaps++;
                        if (metrics.GapCoords.Count < 64)
                            metrics.GapCoords.Add(new Vector2Int(x, y));
                    }
                }
            }

            metrics.BoundsMinX = bboxMinX;
            metrics.BoundsMinY = bboxMinY;
            metrics.BoundsMaxX = bboxMaxX;
            metrics.BoundsMaxY = bboxMaxY;

            if (bboxMaxX >= bboxMinX && bboxMaxY >= bboxMinY)
            {
                for (int y = bboxMinY; y <= bboxMaxY; y++)
                {
                    for (int x = 0; x < axis; x++)
                    {
                        int mx = w - 1 - x;
                        int i = y * w + x;
                        int mi = y * w + mx;
                        if (charMask[i] != charMask[mi])
                            metrics.MaskMismatch++;
                        if (inkMask[i] != inkMask[mi])
                            metrics.InkMismatch++;
                        if (charMask[i] && charMask[mi] && !SameColor(result[i], result[mi]))
                            metrics.ColorMismatch++;
                    }
                }
            }

            // ---- 轮廓对称（唯一验收口径）----
            // 只看 char∪ink 的左右外沿到画布中轴的距离差；填充内部 XOR 与侧光颜色差都不参与。
            for (int y = 0; y < h; y++)
            {
                int left = -1, right = -1;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (charMask[i] || inkMask[i]) { left = x; break; }
                }
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    if (charMask[i] || inkMask[i]) { right = x + 1; break; }
                }
                if (left >= 0)
                    metrics.ContourError += Mathf.Abs((axis - left) - (right - axis));
            }

            return metrics;
        }

        static bool SameColor(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }

        static Color32 LinearFromSrgb(byte r, byte g, byte b)
        {
            return new Color32(ToLinearByte(r), ToLinearByte(g), ToLinearByte(b), 255);
        }

        static byte ToLinearByte(byte srgbByte)
        {
            float linear = Mathf.GammaToLinearSpace(srgbByte / 255f);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(linear * 255f), 0, 255);
        }

        static int ColorDistanceSq(Color32 a, Color32 b)
        {
            int dr = a.r - b.r;
            int dg = a.g - b.g;
            int db = a.b - b.b;
            return dr * dr + dg * dg + db * db;
        }

        /// <summary>albedo 像素 → 头/身部件；sRGB 与线性两套参考都参与最近色匹配。</summary>
        static void ClassifyPart(Color32 c, Color32 bodySrgb, Color32 bodyLinear,
            Color32 headSrgb, Color32 headLinear, int toleranceSq,
            out bool body, out bool head)
        {
            body = false;
            head = false;
            if (c.a < 128)
                return;

            int db = Mathf.Min(
                ColorDistanceSq(c, bodySrgb), ColorDistanceSq(c, bodyLinear));
            int dh = Mathf.Min(
                ColorDistanceSq(c, headSrgb), ColorDistanceSq(c, headLinear));
            if (db < dh && db <= toleranceSq)
                body = true;
            else if (dh < db && dh <= toleranceSq)
                head = true;
        }

        /// <summary>部件掩码的逐行左右外沿误差（到画布中轴）。头/身各自对称的验收口径。</summary>
        static int ContourErrorOf(bool[] mask, int w, int h, int axis)
        {
            int error = 0;
            for (int y = 0; y < h; y++)
            {
                int left = -1;
                int right = -1;
                for (int x = 0; x < w; x++)
                {
                    if (mask[y * w + x]) { left = x; break; }
                }
                for (int x = w - 1; x >= 0; x--)
                {
                    if (mask[y * w + x]) { right = x + 1; break; }
                }
                if (left >= 0)
                    error += Mathf.Abs((axis - left) - (right - axis));
            }
            return error;
        }

        /// <summary>
        /// 部件漏色：albedo 已按头/身色分类；头部圆盘内出现身体红 = 头部底/侧缘丢格后透出身体。
        /// 头部圆盘用 Head renderer 的世界包围盒投影到艺术画布，不依赖被挖洞后的 tan 掩码 bbox。
        /// </summary>
        void ComputePartMetrics(bool[] bodyMask, bool[] headMask, bool[] inkMask,
            bool[] bodyArtMask, bool[] headArtMask, int w, int h, ref Metrics metrics)
        {
            for (int i = 0; i < bodyMask.Length; i++)
            {
                if (bodyMask[i]) metrics.BodyPixels++;
                if (headMask[i]) metrics.HeadPixels++;
            }

            // 身体最高可见行：只有出现在它**上方**的身体红才算“头底透红”；
            // 头盘底缘与身体顶面同一行是正常肩部，不能算漏色。
            int bodyTop = h;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (bodyMask[y * w + x]) { bodyTop = y; break; }
                }
                if (bodyTop < h)
                    break;
            }

            // 头/身分别的轮廓误差：各用块锚中心采样的部件掩码，逐行左右外沿到画布中轴。
            // 这是“先让头和身各自对称，再组合”的验收口径。
            metrics.BodyContourError = ContourErrorOf(bodyArtMask, w, h, w / 2);
            metrics.HeadContourError = ContourErrorOf(headArtMask, w, h, w / 2);

            // 内部横向墨段：长度 ≥3、上下 2 行内都能碰到身体块的墨段。
            // 头/身接缝的“双描边线”就是这种墨段（头底一条 + 身体顶一条，中间夹红边）。
            for (int y = 0; y < h; y++)
            {
                int x = 0;
                while (x < w)
                {
                    if (!inkMask[y * w + x]) { x++; continue; }
                    int x0 = x;
                    while (x < w && inkMask[y * w + x]) x++;
                    int len = x - x0;
                    if (len < 3)
                        continue;

                    bool above = false, below = false;
                    for (int dx = 0; dx < len && !(above && below); dx += 2)
                    {
                        int px = Mathf.Min(x0 + dx, w - 1);
                        for (int dy = 1; dy <= 2; dy++)
                        {
                            int ya = y - dy;
                            if (ya >= 0 && bodyMask[ya * w + px]) above = true;
                            int yb = y + dy;
                            if (yb < h && bodyMask[yb * w + px]) below = true;
                        }
                    }
                    if (above && below)
                        metrics.SeamInk++;
                }
            }

            MeshRenderer head = FindHeadRenderer();
            Camera cam = rig != null && rig.CastCamera != null
                ? rig.CastCamera
                : (cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null);
            if (head == null || cam == null)
                return;

            Bounds bounds = head.bounds;
            Vector3 center = bounds.center;
            float worldRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            Vector3 vpCenter = cam.WorldToViewportPoint(center);
            Vector3 vpEdge = cam.WorldToViewportPoint(center + cam.transform.right * worldRadius);
            float cx = vpCenter.x * w;
            float cy = (1f - vpCenter.y) * h;
            float radius = Vector2.Distance(
                new Vector2(cx, cy),
                new Vector2(vpEdge.x * w, (1f - vpEdge.y) * h));

            metrics.HeadDiskCenterX = cx;
            metrics.HeadDiskCenterY = cy;
            metrics.HeadDiskRadius = radius;

            float r2 = radius * radius;
            for (int y = 0; y < h; y++)
            {
                float dy = y + 0.5f - cy;
                for (int x = 0; x < w; x++)
                {
                    float dx = x + 0.5f - cx;
                    if (dx * dx + dy * dy > r2)
                        continue;
                    int i = y * w + x;
                    if (y < bodyTop && bodyMask[i])
                        metrics.PartLeaks++;
                }
            }
        }

        MeshRenderer FindHeadRenderer()
        {
            if (subject == null)
                return null;
            MeshRenderer[] renderers = subject.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].name == "Head")
                    return renderers[i];
            }
            return null;
        }

        void SavePartVisual(Metrics metrics, bool[] charMask, bool[] inkMask,
            bool[] bodyMask, bool[] headMask, int w, int h, string path)
        {
            var vis = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float dx = x + 0.5f - metrics.HeadDiskCenterX;
                    float dy = y + 0.5f - metrics.HeadDiskCenterY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    bool diskRing = metrics.HeadDiskRadius > 0f
                        && Mathf.Abs(dist - metrics.HeadDiskRadius) < 0.65f;

                    if (diskRing)
                        vis[i] = new Color32(0, 200, 255, 255);       // 头部圆盘边界
                    else if (bodyMask[i])
                        vis[i] = new Color32(255, 60, 60, 255);       // 身体红
                    else if (headMask[i])
                        vis[i] = new Color32(212, 167, 106, 255);     // 头木色
                    else if (inkMask[i])
                        vis[i] = new Color32(0, 0, 0, 255);           // 墨线
                    else if (charMask[i])
                        vis[i] = new Color32(140, 140, 140, 255);     // 未分类角色
                    else
                        vis[i] = new Color32(255, 255, 255, 255);     // 地面/背景
                }
            }
            SavePixels(vis, w, h, path);
        }

        // ------------------------------------------------------------------
        // 落盘
        // ------------------------------------------------------------------

        void SaveAllBuffers(string dir, int artW, int artH, int fineW, int fineH,
            Color32[] albedo, Color32[] palette, Color32[] outline)
        {
            // 屏幕档（细档）——覆盖/描边开关用 alpha 单独存灰度，原图保留 RGBA。
            SavePixels(Gamma(albedo), fineW, fineH, Path.Combine(dir, "albedo_patch.png"));
            SaveAlpha(albedo, fineW, fineH, Path.Combine(dir, "albedo_patch_cover.png"));
            SaveAlpha(palette, fineW, fineH, Path.Combine(dir, "palette_patch_alpha.png"));

            SaveBuffer("albedo", rig.AlbedoBuffer, dir, gamma: true);
            SaveBuffer("palette", rig.PaletteBuffer, dir, gamma: false);
            SaveBuffer("normal0", rig.Normal0Buffer, dir, gamma: false);
            SaveBuffer("normal1", rig.Normal1Buffer, dir, gamma: false);
            SaveBuffer("physical", rig.PhysicalBuffer, dir, gamma: false);
            SaveBuffer("shape", rig.ShapeBuffer, dir, gamma: false);
            SaveBuffer("rimlight_property", rig.RimLightPropertyBuffer, dir, gamma: false);
            SaveBuffer("normal0_patch", rig.Normal0PatchBuffer, dir, gamma: false);
            SaveBuffer("normal1_patch", rig.Normal1PatchBuffer, dir, gamma: false);
            SaveBuffer("physical_patch", rig.PhysicalPatchBuffer, dir, gamma: false);
            SaveBuffer("shape_patch", rig.ShapePatchBuffer, dir, gamma: false);
            SaveBuffer("rimlight_property_patch", rig.RimLightPropertyPatchBuffer, dir, gamma: false);

            if (rig.DepthPatchBuffer != null)
                SaveBuffer("depth_patch", rig.DepthPatchBuffer, dir, gamma: false);

            // 艺术画布域：墨线两趟分开存，一眼看出第二趟补了哪些格。
            if (rig.OutlineBuffer != null)
                SaveGrayChannel(rig.OutlineBuffer, 0, Path.Combine(dir, "outline_pass1.png"));
            SaveGrayChannel(rig.OutlineClosedBuffer, 0, Path.Combine(dir, "outline_closed.png"));

            if (rig.ConnectivityResultBuffer != null)
            {
                SaveBuffer("connectivity", rig.ConnectivityResultBuffer, dir, gamma: false);
                SaveConnectivityVisual(rig.ConnectivityResultBuffer, Path.Combine(dir, "conn_vis.png"));
            }
        }

        void SaveBuffer(string name, RenderTexture rt, string dir, bool gamma)
        {
            if (rt == null)
                return;
            Color32[] px = ReadColor(rt, out int w, out int h);
            SavePixels(gamma ? Gamma(px) : px, w, h, Path.Combine(dir, name + ".png"));
        }

        void SaveGrayChannel(RenderTexture rt, int channel, string path)
        {
            if (rt == null)
                return;
            Color32[] px = ReadColor(rt, out int w, out int h);
            var gray = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                byte v = channel == 0 ? px[i].r : channel == 1 ? px[i].g : channel == 2 ? px[i].b : px[i].a;
                gray[i] = new Color32(v, v, v, 255);
            }
            SavePixels(gray, w, h, path);
        }

        void SaveConnectivityVisual(RenderTexture rt, string path)
        {
            Color32[] px = ReadColor(rt, out int w, out int h);
            var vis = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                int bits = px[i].a;
                int connected = (bits >> 4) & 0xF;
                int closer = bits & 0xF;
                // R = 四方向 closer 命中数，G = 四方向 connected 命中数（0..4 × 63）。
                vis[i] = new Color32((byte)(closer * 63), (byte)(connected * 63), 0, 255);
            }
            SavePixels(vis, w, h, path);
        }

        void SaveProbeVisual(Metrics metrics, bool[] charMask, bool[] inkMask, int artW, int artH, string path)
        {
            Color32[] vis = new Color32[artW * artH];
            for (int y = 0; y < artH; y++)
            {
                for (int x = 0; x < artW; x++)
                {
                    int i = y * artW + x;
                    if (charMask[i] && inkMask[i])
                        vis[i] = new Color32(0, 160, 0, 255);       // 角色与墨线同格（不应出现）
                    else if (inkMask[i])
                        vis[i] = new Color32(0, 0, 0, 255);         // 墨线
                    else if (charMask[i])
                        vis[i] = new Color32(170, 170, 170, 255);   // 角色
                    else if (metrics.GapMask != null && metrics.GapMask[i])
                        vis[i] = new Color32(255, 0, 0, 255);       // 漏缝
                    else
                        vis[i] = new Color32(255, 255, 255, 255);   // 背景/地面
                }
            }
            SavePixels(vis, artW, artH, path);
        }

        // ---- 像素读写 ------------------------------------------------------

        /// <summary>
        /// 从 RT 读回 CPU 像素。ARGB32 直读；ARGBHalf/RFloat 先 blit 到 ARGB32（调试图，
        /// 负值/超 1 的值会被钳——判读数值请用同名 .compute 或原始缓冲）。
        /// </summary>
        static Color32[] ReadColor(RenderTexture rt, out int width, out int height)
        {
            width = rt.width;
            height = rt.height;
            RenderTexture previous = RenderTexture.active;
            RenderTexture temp = null;
            RenderTexture source = rt;

            bool needsBlit = rt.format != RenderTextureFormat.ARGB32;
            if (needsBlit)
            {
                temp = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(rt, temp);
                source = temp;
            }

            RenderTexture.active = source;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            Color32[] px = tex.GetPixels32();
            DestroySafe(tex);
            if (temp != null)
                RenderTexture.ReleaseTemporary(temp);

            FlipRows(px, width, height);
            return px;
        }

        static void FlipRows(Color32[] px, int width, int height)
        {
            for (int y = 0; y < height / 2; y++)
            {
                int a = y * width;
                int b = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 tmp = px[a + x];
                    px[a + x] = px[b + x];
                    px[b + x] = tmp;
                }
            }
        }

        static void SaveMask(bool[] mask, int w, int h, string path)
        {
            if (mask == null)
                return;
            var px = new Color32[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                byte v = mask[i] ? (byte)255 : (byte)0;
                px[i] = new Color32(v, v, v, 255);
            }
            SavePixels(px, w, h, path);
        }

        static void SaveAlpha(Color32[] src, int w, int h, string path)
        {
            var px = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                byte a = src[i].a;
                px[i] = new Color32(a, a, a, 255);
            }
            SavePixels(px, w, h, path);
        }

        static void SavePixels(Color32[] px, int w, int h, string path)
        {
            if (px == null || px.Length != w * h)
                return;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            // GPU 读回数组是自上而下，Texture2D.SetPixels32 期望自下而上 ⇒ 翻一次行序。
            // 不翻的话所有 result/mask/buffer PNG 相对 screen.png 都是上下颠倒的（历史 bug）。
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++)
                System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            tex.SetPixels32(flipped);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroySafe(tex);
        }

        static Color32[] Gamma(Color32[] src)
        {
            var px = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                px[i] = new Color32(
                    ToGamma(src[i].r), ToGamma(src[i].g), ToGamma(src[i].b), src[i].a);
            }
            return px;
        }

        static byte ToGamma(byte linearByte)
        {
            float linear = linearByte / 255f;
            float gamma = Mathf.LinearToGammaSpace(linear);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(gamma * 255f), 0, 255);
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        // ---- 报告 ----------------------------------------------------------

        void WriteReport(string dir, Metrics m, int artW, int artH, int fineW, int fineH, int k)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CrewProbe]");
            sb.AppendLine("time=" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("azimuthDegrees=" + azimuthDegrees.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine("visibleMeters=" + visibleMeters.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine("pixelScale=" + k);
            sb.AppendLine("worldPerPixel=" + WorldPerPixel(visibleMeters, k).ToString("F6", CultureInfo.InvariantCulture));
            sb.AppendLine("ground=" + groundVariant);
            sb.AppendLine("background=" + background);
            sb.AppendLine("debugView=" + debugView + " (mode " + (int)debugView + ")");
            sb.AppendLine("screenOffsetPixels=" + screenOffsetXPixels + "," + screenOffsetYPixels
                + "（沿相机 right/up 的艺术像素步进）");
            sb.AppendLine("screenOffsetXSubpixels=" + screenOffsetXSubpixels.ToString("F3", CultureInfo.InvariantCulture)
                + " snapSubjectToPixelGrid=" + snapSubjectToPixelGrid);
            sb.AppendLine("headOffsetPixels=" + headOffsetXPixels + "," + headOffsetYPixels
                + "（只挪 HeadPivot，测头/身相对相位）");
            sb.AppendLine("art=" + artW + "x" + artH + " fine=" + fineW + "x" + fineH);
            sb.AppendLine("charPixels=" + m.CharPixels);
            sb.AppendLine("bbox=" + m.BoundsMinX + "," + m.BoundsMinY + " - " + m.BoundsMaxX + "," + m.BoundsMaxY);
            sb.AppendLine("gaps=" + m.Gaps + " gapRuns=" + m.GapRuns);
            sb.AppendLine("maskMismatch=" + m.MaskMismatch);
            sb.AppendLine("inkMismatch=" + m.InkMismatch);
            sb.AppendLine("colorMismatch=" + m.ColorMismatch + "（颜色镜像差测的是光照，不是几何）");
            sb.AppendLine("contourError=" + m.ContourError
                + "（逐行 char∪ink 左右外沿到中轴的距离差；轮廓对称的唯一验收口径）");
            sb.AppendLine("bodyContour=" + m.BodyContourError
                + " headContour=" + m.HeadContourError
                + "（头/身分别的逐行外沿误差；先各自对称再组合）");
            sb.AppendLine("seamInk=" + m.SeamInk + "（内部横向墨段数；头/身接缝双线）");
            sb.AppendLine("partLeaks=" + m.PartLeaks + "（身体最高可见行上方、头盘内的身体红像素）");
            sb.AppendLine("bodyPixels=" + m.BodyPixels + " headPixels=" + m.HeadPixels);
            sb.AppendLine("headDisk=" + m.HeadDiskCenterX.ToString("F2", CultureInfo.InvariantCulture)
                + "," + m.HeadDiskCenterY.ToString("F2", CultureInfo.InvariantCulture)
                + " r=" + m.HeadDiskRadius.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine("gapCoords（艺术画布，y 自图像顶部；最多 64 个）:");
            if (m.GapCoords != null)
            {
                for (int i = 0; i < m.GapCoords.Count; i++)
                    sb.AppendLine("  " + m.GapCoords[i].x + "," + m.GapCoords[i].y);
            }
            File.WriteAllText(Path.Combine(dir, "probe.txt"), sb.ToString());
            _logLines.Clear();
            _logLines.AddRange(sb.ToString().Split('\n'));
        }

        // ---- 路径 ----------------------------------------------------------

        string OutputRootAbsolute()
        {
            if (Path.IsPathRooted(outputRoot))
                return outputRoot;
            // Application.dataPath = <仓库根>/pirate-crew/Assets
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", outputRoot));
        }

        static string Stamp()
        {
            return System.DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff", CultureInfo.InvariantCulture);
        }

        string NewSessionDir(string kind)
        {
            string dir = Path.Combine(OutputRootAbsolute(), Stamp() + "-" + kind);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ------------------------------------------------------------------
        // IMGUI 面板（播放态；编辑态用 Inspector）
        // ------------------------------------------------------------------

        void OnGUI()
        {
            if (!showPanel || !Application.isPlaying)
                return;

            const float width = 470f;
            var rect = new Rect(12f, 12f, width, 790f);
            GUI.Box(rect, "");
            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));

            GUILayout.Label("角色像素探针 · 绿幕观察场");
            GUILayout.Label("状态：" + _status);

            GUILayout.Label("可见米数 " + visibleMeters.ToString("F1", CultureInfo.InvariantCulture)
                + "（32 = 战斗档）");
            float visible = GUILayout.HorizontalSlider(visibleMeters, 4f, 64f);
            if (!Mathf.Approximately(visible, visibleMeters))
            {
                visibleMeters = visible;
                ApplyAll();
            }

            GUILayout.Label("像素档 " + pixelScale + "×");
            int scale = Mathf.RoundToInt(GUILayout.HorizontalSlider(pixelScale,
                PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax));
            if (scale != pixelScale)
            {
                pixelScale = scale;
                ApplyAll();
            }

            GUILayout.Label("方位角 " + azimuthDegrees.ToString("F1", CultureInfo.InvariantCulture) + "°");
            float yaw = GUILayout.HorizontalSlider(azimuthDegrees, 0f, 360f);
            if (!Mathf.Approximately(yaw, azimuthDegrees))
            {
                azimuthDegrees = yaw;
                ApplyCamera();
            }

            GUILayout.Label("地面");
            int groundIndex = GUILayout.SelectionGrid((int)groundVariant,
                new[] { "无", "平地", "朝相机坡", "背相机坡", "屋脊" }, 5);
            if (groundIndex != (int)groundVariant)
            {
                groundVariant = (GroundVariant)groundIndex;
                ApplyGround();
            }

            GUILayout.Label("背景");
            int bgIndex = GUILayout.SelectionGrid((int)background,
                new[] { "绿幕", "白", "黑" }, 3);
            if (bgIndex != (int)background)
            {
                background = (ProbeBackground)bgIndex;
                ApplyBackground();
            }

            GUILayout.Label("调试档 " + debugView);
            int debugIndex = GUILayout.SelectionGrid((int)debugView,
                new[] { "Final", "Albedo", "Normal", "Palette", "Outline", "Connect", "Gate", "Cover", "Branch" }, 3);
            if (debugIndex != (int)debugView)
            {
                debugView = (DebugView)debugIndex;
                Shader.SetGlobalFloat(PixelartPath.DebugModeId, DebugModeValue(debugView));
            }

            GUILayout.Space(4f);
            GUILayout.Label("漏缝 " + _lastMetrics.Gaps + "（" + _lastMetrics.GapRuns + " 段）"
                + " · 部件漏色 " + _lastMetrics.PartLeaks
                + " · 掩码左右差 " + _lastMetrics.MaskMismatch
                + " · 墨线左右差 " + _lastMetrics.InkMismatch
                + " · 颜色左右差 " + _lastMetrics.ColorMismatch
                + " @ " + _lastCaptureYaw.ToString("F1", CultureInfo.InvariantCulture) + "°");

            GUILayout.Label("对称偏移（艺术像素） X=" + screenOffsetXPixels + " Y=" + screenOffsetYPixels);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("X−"))
            {
                screenOffsetXPixels = Mathf.Max(-4, screenOffsetXPixels - 1);
                ApplyCamera();
            }
            if (GUILayout.Button("X+"))
            {
                screenOffsetXPixels = Mathf.Min(4, screenOffsetXPixels + 1);
                ApplyCamera();
            }
            if (GUILayout.Button("Y−"))
            {
                screenOffsetYPixels = Mathf.Max(-4, screenOffsetYPixels - 1);
                ApplyCamera();
            }
            if (GUILayout.Button("Y+"))
            {
                screenOffsetYPixels = Mathf.Min(4, screenOffsetYPixels + 1);
                ApplyCamera();
            }
            if (GUILayout.Button("自动对称 (F7)"))
                AutoAlignSymmetry();
            GUILayout.EndHorizontal();

            GUILayout.Label("亚像素 X=" + screenOffsetXSubpixels.ToString("F2", CultureInfo.InvariantCulture)
                + "（物体吸附 " + (snapSubjectToPixelGrid ? "开" : "关") + "）");
            float sub = GUILayout.HorizontalSlider(screenOffsetXSubpixels, -1f, 1f);
            if (!Mathf.Approximately(sub, screenOffsetXSubpixels))
            {
                screenOffsetXSubpixels = sub;
                ApplyCamera();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(snapSubjectToPixelGrid ? "关闭物体吸附" : "打开物体吸附"))
            {
                snapSubjectToPixelGrid = !snapSubjectToPixelGrid;
                ApplyCamera();
            }
            if (GUILayout.Button(_subpixelRunning ? "停止亚像素 (F6)" : "亚像素扫描 (F6)"))
            {
                if (_subpixelRunning)
                    StopSweep();
                else
                    StartSubpixelSweep();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("头/身相对偏移（艺术像素） X=" + headOffsetXPixels + " Y=" + headOffsetYPixels);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("头X−"))
            {
                headOffsetXPixels = Mathf.Max(-4, headOffsetXPixels - 1);
                ApplyCamera();
            }
            if (GUILayout.Button("头X+"))
            {
                headOffsetXPixels = Mathf.Min(4, headOffsetXPixels + 1);
                ApplyCamera();
            }
            if (GUILayout.Button("头Y−"))
            {
                headOffsetYPixels = Mathf.Max(-4, headOffsetYPixels - 1);
                ApplyCamera();
            }
            if (GUILayout.Button("头Y+"))
            {
                headOffsetYPixels = Mathf.Min(4, headOffsetYPixels + 1);
                ApplyCamera();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("抓当前帧 (F9)"))
                CaptureNow();
            if (GUILayout.Button(_sweepRunning ? "停止扫描 (F8)" : "角度扫描 (F8)"))
            {
                if (_sweepRunning)
                    StopSweep();
                else
                    StartSweep();
            }
            if (GUILayout.Button("重置方位"))
            {
                azimuthDegrees = BaseAzimuthDegrees;
                ApplyCamera();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("右键拖拽转方位 · 产物在 " + OutputRootAbsolute());
            GUILayout.EndArea();
        }
    }
}
