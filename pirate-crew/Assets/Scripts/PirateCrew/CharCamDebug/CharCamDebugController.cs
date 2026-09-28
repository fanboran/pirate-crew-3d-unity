using UnityEngine;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Visual;

namespace PirateCrew.CharCamDebug
{
    /// <summary>
    /// **角色/镜头参数调试场**（`Assets/Scenes/PixelartCharCamDebug.unity`）的运行时控制器。
    ///
    /// 【这个场景是干什么的】画面中心站一个**两件式程序化角色**（圆台 Body + 圆球 Head，几何与
    /// 游戏角色同一条生成链），挂一个 IMGUI 面板实时调两组参数，回答"镜头参数怎么设置"：
    ///   · **角色体格**：身体顶/底半径、身体高度、头部半径——改完当场重建网格；
    ///   · **镜头取景**：可见米数（美术锚）、像素档位、俯角/方位角——直接写进
    ///     <see cref="PixelartCameraRig"/> 的公开字段（worldPerPixel / pixelScale）与相机 Transform。
    /// 面板同时给**读数**：角色总高占多少**艺术像素**、占画面高度百分比、每艺术像素多少米——
    /// 像素化路径下"角色清不清楚"就是这几个数决定的（面片小于艺术像素等于白费，见
    /// `CrewVisualPrefabBuilder.GodotHeadSegments` 的推导），调参时照着读数定，不靠目测。
    ///
    /// 【为什么不直接实例化游戏角色预制体】预制体的两件式网格是**烘死的资产**（尺寸即规格值，
    /// 缩放恒 1），改不了参数；本场景要的恰恰是"参数可跑"。几何走同一个
    /// <see cref="CrewMeshFactory"/>（Frustum + LowPolySphere，分段同
    /// `CrewVisualPrefabBuilder` 的 16 / 12×8），调出来的形状与正式角色**同一套数学**，
    /// 参数可直接回填 Builder 常量。**本组件不写任何游戏角色的资产/预制体**。
    ///
    /// 【worldPerPixel 换算口径】可见米数是美术锚、不随像素档位变：
    /// <c>worldPerPixel = 可见米数 × pixelScale ÷ 1080</c>（1080 = 参考屏幕高；
    /// 与 `PixelartPilotScene.WorldPerPixel`（÷540）同义——540 = 1080 ÷ 现役档 2）。
    /// 取景重算由 rig 的 <c>deriveOrthographicSize</c> 在下一帧自动完成，本组件不碰 orthoSize。
    ///
    /// 【装配】编辑器侧 `PixelartCharCamDebugSetup` 建场景并接好全部引用后调一次
    /// <see cref="ApplyAll"/>（编辑态就能看到角色）；运行时 <see cref="LateUpdate"/> 按脏标记应用。
    /// </summary>
    public sealed class CharCamDebugController : MonoBehaviour
    {
        /// <summary>场景名（装配器保存的资产名；按名 LoadScene 用）。</summary>
        public const string SceneName = "PixelartCharCamDebug";

        // ------------------------------------------------------------------
        // 口径常量（跨"编辑器装配 / 运行时"两方的单一真源）
        // ------------------------------------------------------------------

        /// <summary>参考屏幕高（worldPerPixel 换算的分母；1080p 口径）。</summary>
        public const int ReferenceScreenHeight = 1080;

        /// <summary>头球与柱顶的轴向重叠（米）——Godot 基准 0.05（`CrewVisualPrefabBuilder` 同值）。</summary>
        public const float HeadBodyOverlap = 0.05f;

        /// <summary>机位到构图中心的距离（正交相机下只影响裁剪，不影响观感大小）。</summary>
        public const float CameraDistance = 60f;

        /// <summary>构图中心：固定在角色腰高附近，不随体格参数漂（构图稳定才好横向对比）。</summary>
        public static readonly Vector3 FramingTarget = new Vector3(0f, 1.0f, 0f);

        /// <summary>Body 圆台侧壁分段——镜像 `CrewVisualPrefabBuilder.GodotBodySides`（16，private 不可引用）。</summary>
        public const int BodySides = 16;

        /// <summary>Head 球经向分段——镜像 `GodotHeadSegments`（12）。</summary>
        public const int HeadSegments = 12;

        /// <summary>Head 球纬向分段——镜像 `GodotHeadRings`（8）。</summary>
        public const int HeadRings = 8;

        /// <summary>把「可见多少米高」换算成一个艺术像素的世界尺寸（米）。</summary>
        public static float WorldPerPixel(float visibleMeters, int pixelScale)
        {
            return visibleMeters * Mathf.Max(1, pixelScale) / ReferenceScreenHeight;
        }

        // ------------------------------------------------------------------
        // 默认参数（= CrewVisualPrefabBuilder 现役两件式值 + 关卡现役机位）
        // ------------------------------------------------------------------

        /// <summary>默认身体顶半径（= Builder <c>BodyTopRadius</c> 0.35）。</summary>
        public const float DefaultTopRadius = 0.35f;
        /// <summary>默认身体底半径（= Builder <c>BodyBottomRadius</c> 0.4667）。</summary>
        public const float DefaultBottomRadius = 0.4667f;
        /// <summary>默认身体高度（= Builder <c>BodyHeight</c> 1.20）。</summary>
        public const float DefaultBodyHeight = 1.20f;
        /// <summary>默认头部半径（= Builder <c>HeadSphereRadius</c> 0.35）。</summary>
        public const float DefaultHeadRadius = 0.35f;
        /// <summary>默认可见米数：游戏内特写档 14m（`PixelartLevelScene` 现役三档 32/14/7 的中档，
        /// 与游戏内正交档 7 的可见高度一致）。</summary>
        public const float DefaultVisibleMeters = 14f;
        /// <summary>默认俯角（30° = 规则像素阶梯，`PixelartPilotScene.PitchDegrees` 同源）。</summary>
        public const float DefaultPitchDegrees = 30f;
        /// <summary>默认方位角（45° = 对称菱形，`PixelartPilotScene.AzimuthDegrees` 同源）。</summary>
        public const float DefaultAzimuthDegrees = 45f;

        // ------------------------------------------------------------------
        // 场景引用（装配器接线）
        // ------------------------------------------------------------------

        [Header("角色骨架（装配器接线）")]
        [Tooltip("Body 圆台的枢轴（脚底贴地：localPosition.y 由本组件按身高写）。")]
        public Transform bodyPivot;
        [Tooltip("Head 圆球的枢轴（localPosition.y = 球心高度，本组件写）。")]
        public Transform headPivot;
        [Tooltip("Body 的网格过滤器（网格由本组件运行时生成）。")]
        public MeshFilter bodyFilter;
        [Tooltip("Head 的网格过滤器（网格由本组件运行时生成）。")]
        public MeshFilter headFilter;

        [Header("镜头（装配器接线）")]
        [Tooltip("像素化相机 rig：worldPerPixel / pixelScale 写它，取景由它重算。")]
        public PixelartCameraRig rig;
        [Tooltip("主相机（上屏器）的 Transform：俯角/方位角改了要重摆机位。")]
        public Transform cameraTransform;

        // ------------------------------------------------------------------
        // 可调参数（面板滑杆直接改这些）
        // ------------------------------------------------------------------

        [Header("角色体格（米）")]
        [Min(0.02f)] public float topRadius = DefaultTopRadius;
        [Min(0.02f)] public float bottomRadius = DefaultBottomRadius;
        [Min(0.1f)] public float bodyHeight = DefaultBodyHeight;
        [Min(0.02f)] public float headRadius = DefaultHeadRadius;

        [Header("镜头取景")]
        [Tooltip("可见米数（美术锚）：1080p 屏上画面高度看到的米数；屏幕分辨率越高实际可见越多。")]
        [Min(1f)] public float visibleMeters = DefaultVisibleMeters;
        [Tooltip("像素档位（一个艺术像素占几个屏幕像素，2–5）。")]
        [Range(PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax)]
        public int pixelScale = PixelartPilotScene.PixelScale;
        [Tooltip("俯角（度）。30° = 地面轴屏幕斜率 0.5（规则像素阶梯）。")]
        [Range(10f, 70f)] public float pitchDegrees = DefaultPitchDegrees;
        [Tooltip("方位角（度）。45° = 对称菱形。")]
        [Range(0f, 90f)] public float azimuthDegrees = DefaultAzimuthDegrees;

        [Header("面板")]
        public bool showPanel = true;
        public Rect panelRect = new Rect(12f, 12f, 380f, 40f);

        // 运行时生成的两块网格（重建时销毁旧的，避免泄漏）。
        Mesh _bodyMesh;
        Mesh _headMesh;
        // 滑杆 → 应用 的脏标记（网格重建与相机重摆都只在变化时做）。
        bool _meshDirty = true;
        bool _cameraDirty = true;
        int _appliedPixelScale = -1;

        /// <summary>头心高度（自动联动：球底与柱顶保持 <see cref="HeadBodyOverlap"/> 重叠）。</summary>
        public float HeadCenterY => bodyHeight - HeadBodyOverlap + headRadius;

        /// <summary>角色总高（脚底到头顶，米）。</summary>
        public float TotalHeight => HeadCenterY + headRadius;

        void LateUpdate()
        {
            ApplyIfDirty();
        }

        /// <summary>全部应用一遍（装配器在编辑态调用；运行时由脏标记驱动）。</summary>
        public void ApplyAll()
        {
            RebuildMeshes();
            ApplyCamera();
        }

        void ApplyIfDirty()
        {
            if (_meshDirty)
                RebuildMeshes();
            if (_cameraDirty || (rig != null && rig.pixelScale != pixelScale))
                ApplyCamera();
        }

        // ------------------------------------------------------------------
        // 角色网格（CrewMeshFactory 同链重建）
        // ------------------------------------------------------------------

        void RebuildMeshes()
        {
            _meshDirty = false;
            if (bodyFilter == null || headFilter == null || bodyPivot == null || headPivot == null)
                return;

            _bodyMesh = ReplaceMesh(_bodyMesh, CrewMeshFactory.CreateMesh("CharCamDebug_BodyFrustum",
                CrewMeshFactory.Frustum(topRadius, bottomRadius, bodyHeight, BodySides)), bodyFilter);
            _headMesh = ReplaceMesh(_headMesh, CrewMeshFactory.CreateMesh("CharCamDebug_HeadSphere",
                CrewMeshFactory.LowPolySphere(headRadius, HeadSegments, HeadRings)), headFilter);

            // Frustum 沿 Y 居中 → 枢轴抬到半高，脚底贴 y=0；Head 球心在原点 → 枢轴抬到球心高。
            bodyPivot.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
            headPivot.localPosition = new Vector3(0f, HeadCenterY, 0f);
        }

        static Mesh ReplaceMesh(Mesh oldMesh, Mesh newMesh, MeshFilter filter)
        {
            // 编辑态（装配器）没有"下一帧"，旧网格必须当场 DestroyImmediate。
            if (oldMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(oldMesh);
                else
                    DestroyImmediate(oldMesh);
            }

            filter.mesh = newMesh;
            return newMesh;
        }

        // ------------------------------------------------------------------
        // 镜头（写 rig 的公开字段，取景由 rig 重算）
        // ------------------------------------------------------------------

        void ApplyCamera()
        {
            _cameraDirty = false;
            if (rig == null || cameraTransform == null)
                return;

            rig.pixelScale = Mathf.Clamp(pixelScale, PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax);
            rig.worldPerPixel = WorldPerPixel(visibleMeters, rig.pixelScale);
            _appliedPixelScale = rig.pixelScale;

            Vector3 dir = PixelartPilotScene.CameraDirection(pitchDegrees, azimuthDegrees);
            cameraTransform.position = FramingTarget + dir * CameraDistance;
            cameraTransform.LookAt(FramingTarget);
        }

        void ResetDefaults()
        {
            topRadius = DefaultTopRadius;
            bottomRadius = DefaultBottomRadius;
            bodyHeight = DefaultBodyHeight;
            headRadius = DefaultHeadRadius;
            visibleMeters = DefaultVisibleMeters;
            pixelScale = PixelartPilotScene.PixelScale;
            pitchDegrees = DefaultPitchDegrees;
            azimuthDegrees = DefaultAzimuthDegrees;
            _meshDirty = true;
            _cameraDirty = true;
        }

        /// <summary>当前参数导成文本（可整段贴进 CrewVisualPrefabBuilder / 装配器常量对照）。</summary>
        public string ExportParameters()
        {
            return "CharCamDebug 参数（米 / 度；可直接对照 CrewVisualPrefabBuilder 常量）\n"
                + $"BodyTopRadius     = {topRadius.ToString("0.####")}\n"
                + $"BodyBottomRadius  = {bottomRadius.ToString("0.####")}\n"
                + $"BodyHeight        = {bodyHeight.ToString("0.####")}\n"
                + $"HeadSphereRadius  = {headRadius.ToString("0.####")}\n"
                + $"HeadSphereCenterY = {HeadCenterY.ToString("0.####")}（自动：身高−0.05+头半径）\n"
                + $"TotalHeight       = {TotalHeight.ToString("0.####")}\n"
                + $"可见米数          = {visibleMeters.ToString("0.#")}（worldPerPixel {WorldPerPixel(visibleMeters, pixelScale).ToString("0.####")}）\n"
                + $"PixelScale        = {pixelScale}\n"
                + $"PitchDegrees      = {pitchDegrees.ToString("0.#")}\n"
                + $"AzimuthDegrees    = {azimuthDegrees.ToString("0.#")}";
        }

        // ------------------------------------------------------------------
        // IMGUI 面板（调试工具不走游戏 UI 三层：一次性、零资产依赖）
        // ------------------------------------------------------------------

        void OnGUI()
        {
            if (!showPanel)
            {
                if (GUILayout.Button("显示参数面板 (F1)", GUILayout.Width(140f)))
                    showPanel = true;
                return;
            }

            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawPanel, "角色 / 镜头参数调试");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                showPanel = !showPanel;
        }

        void DrawPanel(int windowId)
        {
            GUILayout.Label("—— 角色体格（改后自动重建网格）——");
            topRadius = LabeledSlider("身体顶半径", topRadius, 0.05f, 1.0f, ref _meshDirty);
            bottomRadius = LabeledSlider("身体底半径", bottomRadius, 0.05f, 1.0f, ref _meshDirty);
            bodyHeight = LabeledSlider("身体高度", bodyHeight, 0.3f, 2.5f, ref _meshDirty);
            headRadius = LabeledSlider("头部半径", headRadius, 0.05f, 0.8f, ref _meshDirty);
            GUILayout.Label($"  头心 y = {HeadCenterY.ToString("0.###")}，总高 = {TotalHeight.ToString("0.###")}（自动联动，头身重叠 {HeadBodyOverlap}）");

            GUILayout.Space(6f);
            GUILayout.Label("—— 镜头取景 ——");
            visibleMeters = LabeledSlider("可见米数", visibleMeters, 2f, 40f, ref _cameraDirty, "0.#");

            GUILayout.BeginHorizontal();
            GUILayout.Label("像素档位", GUILayout.Width(88f));
            for (int k = PixelartCameraRig.PixelScaleMin; k <= PixelartCameraRig.PixelScaleMax; k++)
            {
                bool previous = GUI.enabled;
                GUI.enabled = pixelScale != k;
                if (GUILayout.Button(k + "×"))
                {
                    pixelScale = k;
                    _cameraDirty = true;
                }
                GUI.enabled = previous;
            }
            GUILayout.EndHorizontal();

            pitchDegrees = LabeledSlider("俯角", pitchDegrees, 10f, 70f, ref _cameraDirty, "0.#");
            azimuthDegrees = LabeledSlider("方位角", azimuthDegrees, 0f, 90f, ref _cameraDirty, "0.#");

            GUILayout.Space(6f);
            GUILayout.Label("—— 读数 ——");
            if (rig != null && rig.IsReady && rig.UnitSize > 0f)
            {
                float unit = rig.UnitSize;
                float totalPx = TotalHeight / unit;
                float bottomPx = bottomRadius * 2f / unit;
                float headPx = headRadius * 2f / unit;
                float canvasH = Mathf.Max(1, rig.RenderHeight);
                GUILayout.Label($"每艺术像素 {unit.ToString("0.####")} m｜可见 {(rig.RenderHeight * rig.worldPerPixel).ToString("0.#")} m（实际画布 {rig.RenderWidth}×{canvasH}）");
                GUILayout.Label($"角色总高 {totalPx.ToString("0.#")} px = 画面高 {100f * totalPx / canvasH:0.#}%");
                GUILayout.Label($"底径宽 {bottomPx.ToString("0.#")} px｜头径宽 {headPx.ToString("0.#")} px");
            }
            else
            {
                GUILayout.Label($"worldPerPixel {WorldPerPixel(visibleMeters, pixelScale).ToString("0.####")} m（rig 未就绪，读数待首帧）");
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("复制参数"))
                GUIUtility.systemCopyBuffer = ExportParameters();
            if (GUILayout.Button("重置默认"))
                ResetDefaults();
            if (GUILayout.Button("隐藏 (F1)"))
                showPanel = false;
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        /// <summary>标签 + 数值 + 滑杆一行；变化时置脏并返回新值。</summary>
        static float LabeledSlider(string label, float value, float min, float max, ref bool dirty, string format = "0.###")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(88f));
            GUILayout.Label(value.ToString(format), GUILayout.Width(52f));
            float next = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(next, value))
                dirty = true;
            return next;
        }
    }
}
