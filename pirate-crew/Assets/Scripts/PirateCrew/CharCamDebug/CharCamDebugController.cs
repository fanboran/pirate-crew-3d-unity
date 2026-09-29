using System.Collections.Generic;
using UnityEngine;
using PirateCrew.Core;
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
        // 默认参数（创始人 2026-09-29 在调试场定的默认档；正式角色常量仍以
        // CrewVisualPrefabBuilder 为准——这里是调试场的起始状态，不是造型裁决）
        // ------------------------------------------------------------------

        /// <summary>默认身体顶半径。</summary>
        public const float DefaultTopRadius = 0.3f;
        /// <summary>默认身体底半径。</summary>
        public const float DefaultBottomRadius = 0.3f;
        /// <summary>默认身体高度。</summary>
        public const float DefaultBodyHeight = 1.3215f;
        /// <summary>默认头部半径。</summary>
        public const float DefaultHeadRadius = 0.3f;
        /// <summary>默认头颈间距（正 = 头上拉，0 = Godot 重叠口径）。</summary>
        public const float DefaultHeadLift = 0.1606f;
        /// <summary>默认可见米数：游戏内特写档 14m（`PixelartLevelScene` 现役三档 32/14/7 的中档，
        /// 与游戏内正交档 7 的可见高度一致）。</summary>
        public const float DefaultVisibleMeters = 14f;
        /// <summary>调试场默认像素档 3×。**只管本场景的起始档**，与出图契约
        /// `PixelartPilotScene.PixelScale`（2×）是两回事——调试场按创始人习惯档起步。</summary>
        public const int DefaultPixelScale = 3;
        /// <summary>默认俯角（30° = 规则像素阶梯，`PixelartPilotScene.PitchDegrees` 同源）。</summary>
        public const float DefaultPitchDegrees = 30f;
        /// <summary>默认方位角（45° = 对称菱形；正式关卡里它是**每关可调**的取景参数，
        /// 见 <c>PixelartLevelScene.View.AzimuthDegrees</c>）。</summary>
        public const float DefaultAzimuthDegrees = 45f;
        /// <summary>默认内线降档系数（阈值 = 本值 ÷ 2）。</summary>
        public const float DefaultAaScaler = 0.69f;
        /// <summary>默认抖动档（1 = Bayer 渐变态）。</summary>
        public const int DefaultDitherMode = 1;

        // ------------------------------------------------------------------
        // 角色槽（场上多角色：画面左上/右上角各有对应方块，点击跳转编辑目标）
        // ------------------------------------------------------------------

        /// <summary>
        /// 一个可调角色（骨架引用 + 体格参数 + 运行时网格）。场上摆两具（左/右站位），
        /// 点击屏幕左上/右上角的方块把**编辑目标与相机焦点**跳到对应角色——体格滑杆只动
        /// 当前选中的那具，方便并排对比"不同体格在同一镜头口径下"的观感。
        /// </summary>
        [System.Serializable]
        public class SubjectSlot
        {
            [Min(0.02f)] public float topRadius = DefaultTopRadius;
            [Min(0.02f)] public float bottomRadius = DefaultBottomRadius;
            [Min(0.1f)] public float bodyHeight = DefaultBodyHeight;
            [Min(0.02f)] public float headRadius = DefaultHeadRadius;
            [Tooltip("头颈间距：0 = Godot 基准（球底与柱顶重叠 0.05）；正 = 头上拉，负 = 压进身体。")]
            [Range(-0.3f, 0.5f)] public float headLift = DefaultHeadLift;

            [Header("骨架（装配器接线）")]
            public Transform bodyPivot;
            public Transform headPivot;
            public MeshFilter bodyFilter;
            public MeshFilter headFilter;

            /// <summary>头心高度 = 身高 − 头身重叠 + 头半径 + 头颈间距。</summary>
            public float HeadCenterY => bodyHeight - HeadBodyOverlap + headRadius + headLift;
            /// <summary>角色总高（脚底到头顶，米）。</summary>
            public float TotalHeight => HeadCenterY + headRadius;

            // 运行时生成的两块网格（重建时销毁旧的，避免泄漏；编辑态生成后随场景嵌存）。
            public Mesh bodyMesh;
            public Mesh headMesh;
            /// <summary>网格是否已生成（编辑态 ApplyAll 建全部槽；运行时切到未建槽先建）。</summary>
            public bool built;
        }

        /// <summary>站位 X：槽 0 站画面左（-1.2m）、槽 1 站右（+1.2m）。焦点跳转按它摆相机。</summary>
        public static float StationX(int index) => index == 0 ? -1.2f : 1.2f;

        public static string SlotLabel(int index) => index == 0 ? "角色A" : "角色B";

        [Header("角色槽（装配器摆双角色并接线）")]
        public SubjectSlot[] slots = { new SubjectSlot(), new SubjectSlot() };

        /// <summary>当前编辑目标（画面方块与面板滑杆都指向它）。</summary>
        public int activeSlot;

        /// <summary>当前选中的槽（越界钳回，装配漏接时返回 null 让面板显式提示）。</summary>
        public SubjectSlot Active => slots == null || slots.Length == 0
            ? null
            : slots[Mathf.Clamp(activeSlot, 0, slots.Length - 1)];

        [Header("镜头（装配器接线）")]
        [Tooltip("像素化相机 rig：worldPerPixel / pixelScale 写它，取景由它重算。")]
        public PixelartCameraRig rig;
        [Tooltip("主相机（上屏器）的 Transform：俯角/方位角改了要重摆机位。")]
        public Transform cameraTransform;

        // ------------------------------------------------------------------
        // 可调参数（面板滑杆直接改这些；体格在 SubjectSlot 上，镜头/风格全局共享）
        // ------------------------------------------------------------------

        [Header("镜头取景")]
        [Tooltip("可见米数（美术锚）：1080p 屏上画面高度看到的米数；屏幕分辨率越高实际可见越多。")]
        [Min(1f)] public float visibleMeters = DefaultVisibleMeters;
        [Tooltip("像素档位（一个艺术像素占几个屏幕像素，2–5）。")]
        [Range(PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax)]
        public int pixelScale = DefaultPixelScale;
        [Tooltip("俯角（度）。30° = 地面轴屏幕斜率 0.5（规则像素阶梯）。")]
        [Range(10f, 70f)] public float pitchDegrees = DefaultPitchDegrees;
        [Tooltip("方位角（度）。45° = 对称菱形。")]
        [Range(0f, 90f)] public float azimuthDegrees = DefaultAzimuthDegrees;

        [Header("渲染风格（默认值 = 场景装配口径）")]
        [Tooltip("渲染路径：0 = 像素化着色路径（现役）；1 = URP 原生平滑渲染（对照档：rig 停用 + 材质换 URP/Lit）。")]
        [Range(0, 1)] public int renderPath;
        [Tooltip("抖动：0 = 关；1 = Bayer 4×4（渐变态）；2 = 1-bit 密度图案（v3 两态撕边）。")]
        [Range(0, 2)] public int ditherMode = DefaultDitherMode;
        [Tooltip("色带档数（主光 quantization 档）。")]
        [Range(2, 4)] public int bandCount = 3;
        [Tooltip("描边总开关（地面恒不描边——铺满画面的大平面加轮廓线只会全屏糊边，见实现口径 §4.1）。")]
        public bool outlineOn = true;
        [Tooltip("边缘光：拨亮逐物体 _RimLightColor（白）。")]
        public bool rimOn;
        [Tooltip("连通域降档阈值系数（rig.aaScaler）：0 = 内线降档永不成立（降档 A/B 的 B 图口径）。看内线请先关抖动——抖动会把色带差异打碎。")]
        [Range(0f, 2f)] public float aaScaler = DefaultAaScaler;
        [Tooltip("主光强度乘数（rig.lightIntensity，色带亮度整体旋钮）。")]
        [Range(0f, 2f)] public float lightIntensity = 1f;
        [Tooltip("太阳实时投影（硬阴影）。默认关：开着会改暗面判读；打开时自动切小物件口径的 bias（装配默认 0.65 法线偏置是 160m 大平面的口径，会把 2m 角色的影子推没）。")]
        public bool sunShadowsOn;
        [Tooltip("太阳（留空用 RenderSettings.sun 兜底，与 rig.PushLightGlobals 同口径）。")]
        public Light sun;

        [Header("面板")]
        public bool showPanel = true;
        public Rect panelRect = new Rect(12f, 12f, 380f, 40f);

        // 滑杆 → 应用 的脏标记（网格重建与相机重摆都只在变化时做）。
        bool _meshDirty = true;
        bool _cameraDirty = true;
        bool _styleDirty = true;
        bool _pathDirty = true;
        int _appliedPixelScale = -1;

        // URP 原生对照档：场景 renderer 与其原材质的对位表（退出对照档时原样还原）。
        MeshRenderer[] _sceneRenderers;
        readonly List<MeshRenderer> _urpSwapped = new List<MeshRenderer>();
        readonly List<Material> _urpOriginals = new List<Material>();
        readonly List<Material> _urpProxies = new List<Material>();
        bool _urpModeActive;

        /// <summary>相机焦点 = 当前角色的腰高眼位（跳槽即跳焦点）。</summary>
        public Vector3 FocusTarget()
        {
            int index = Active != null ? Mathf.Clamp(activeSlot, 0, slots.Length - 1) : 0;
            return new Vector3(StationX(index), 1.0f, 0f);
        }

        void LateUpdate()
        {
            ApplyIfDirty();
        }

        /// <summary>全部应用一遍（装配器在编辑态调用：建全部槽网格 + 摆相机；运行时由脏标记驱动）。</summary>
        public void ApplyAll()
        {
            if (slots != null)
                for (int i = 0; i < slots.Length; i++)
                    RebuildSlot(slots[i]);
            ApplyCamera();
        }

        void ApplyIfDirty()
        {
            if (_meshDirty && Active != null)
                RebuildSlot(Active);
            if (_cameraDirty || (rig != null && rig.pixelScale != pixelScale))
                ApplyCamera();
            if (_pathDirty)
                ApplyRenderPath();
            if (_styleDirty)
                ApplyStyle();
        }

        /// <summary>点击方块/面板槽位按钮：编辑目标与相机焦点跳到对应角色。</summary>
        public void SwitchSlot(int index)
        {
            if (slots == null || index < 0 || index >= slots.Length || index == activeSlot)
                return;
            activeSlot = index;
            _cameraDirty = true;   // 焦点跳到新站位
            SubjectSlot slot = Active;
            if (slot != null && !slot.built)
                RebuildSlot(slot);
        }

        // ------------------------------------------------------------------
        // 渲染风格（像素化路径内的旋钮 + URP 原生对照档）
        // ------------------------------------------------------------------

        void Start()
        {
            _sceneRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        }

        /// <summary>
        /// 渲染路径切换。**URP 原生对照档**：停用 rig（它的 OnDisable 会把主相机的
        /// clearFlags / 层掩码 / 渲染器索引 / 后处理原样还原——saved 系列字段），关掉 Cast 相机
        /// （其渲染器六个 Feature 对空缓冲全跳过，留着只是多一次清屏），再把场景材质换成
        /// **URP/Lit 代理材质**（_BaseColor 取自像素化材质的同名属性）——像素化物体 shader
        /// 只写了 PixelartOpaque 那一个 pass，标准 URP 渲染器下它根本不画（物体整类消失，
        /// 实现口径 §6 的静默失效家族），所以材质必须一起换。切回时逐 renderer 还原原材质。
        /// </summary>
        void ApplyRenderPath()
        {
            _pathDirty = false;
            if (rig == null || _sceneRenderers == null)
                return;

            bool wantUrp = renderPath == 1;
            if (wantUrp == _urpModeActive)
                return;

            if (wantUrp)
            {
                rig.enabled = false;
                if (rig.castCamera != null)
                    rig.castCamera.gameObject.SetActive(false);

                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null)
                {
                    Debug.LogError("[CharCamDebugController] 找不到 URP/Lit，对照档不可用，回退像素化。");
                    renderPath = 0;
                    return;
                }

                for (int i = 0; i < _sceneRenderers.Length; i++)
                {
                    Material original = _sceneRenderers[i].sharedMaterial;
                    if (original == null || original.shader == null
                        || original.shader.name != PixelartPath.ObjectShaderName)
                        continue;

                    var proxy = new Material(lit) { name = original.name + "_URPProxy" };
                    proxy.SetColor("_BaseColor", original.HasProperty("_BaseColor")
                        ? original.GetColor("_BaseColor") : Color.white);
                    if (proxy.HasProperty("_Smoothness"))
                        proxy.SetFloat("_Smoothness", 0f);

                    _urpSwapped.Add(_sceneRenderers[i]);
                    _urpOriginals.Add(original);
                    _urpProxies.Add(proxy);
                    _sceneRenderers[i].sharedMaterial = proxy;
                }

                _urpModeActive = true;
            }
            else
            {
                for (int i = 0; i < _urpSwapped.Count; i++)
                {
                    if (_urpSwapped[i] != null)
                        _urpSwapped[i].sharedMaterial = _urpOriginals[i];
                }

                _urpSwapped.Clear();
                _urpOriginals.Clear();
                _urpProxies.Clear();
                _urpModeActive = false;

                if (rig.castCamera != null)
                    rig.castCamera.gameObject.SetActive(true);
                rig.enabled = true;
                _styleDirty = true;   // 回像素化：按面板状态重放材质旋钮
            }
        }

        /// <summary>
        /// 像素化材质旋钮全量重放（幂等）：色带档数/描边走 <see cref="PixelartMaterialFactory.Configure"/>
        ///（它会把抖动与边缘光洗回默认关），随后按面板状态重写抖动与边缘光——顺序不能反。
        /// 改的是 <c>renderer.material</c>（实例化副本）：Play 模式下直接写 sharedMaterial 会
        /// **持久化进材质资产**（编辑器知名坑），实例副本退出 Play 自动还原。
        /// </summary>
        void ApplyStyle()
        {
            _styleDirty = false;
            if (rig == null || _sceneRenderers == null)
                return;

            rig.aaScaler = aaScaler;
            rig.lightIntensity = lightIntensity;

            Light sunLight = sun != null ? sun : RenderSettings.sun;
            if (sunLight != null)
            {
                // 开投影同步切**小物件口径**的 bias：装配默认 0.06/0.65 是 160m 大平面压自遮挡的
                // 口径（PixelartStageKit），对 2m 角色会把投影沿法线推淡到不可辨——这正是
                // "投影开关像没用"的根因。小件口径 0.02/0.05 影子才落得住；本场的太阳是
                // 场景私有实例，直接改无外溢。
                if (sunShadowsOn)
                {
                    sunLight.shadows = LightShadows.Hard;
                    sunLight.shadowBias = 0.02f;
                    sunLight.shadowNormalBias = 0.05f;
                }
                else
                {
                    sunLight.shadows = LightShadows.None;
                }
            }

            if (_urpModeActive)
                return;   // 对照档下材质是 URP 代理，像素化旋钮暂不生效（面板上已禁用）

            for (int i = 0; i < _sceneRenderers.Length; i++)
            {
                MeshRenderer sceneRenderer = _sceneRenderers[i];
                if (sceneRenderer == null || sceneRenderer.sharedMaterial == null
                    || sceneRenderer.sharedMaterial.shader == null
                    || sceneRenderer.sharedMaterial.shader.name != PixelartPath.ObjectShaderName)
                    continue;

                Material material = sceneRenderer.material;   // 实例化副本，见方法注释
                Color albedo = material.GetColor("_BaseColor");
                // 地面恒不描边：大平面描边 = 画面四边糊一圈墨（实现口径 §4.1 的既有裁决）。
                float outline = sceneRenderer.name == "Ground" ? 0f : (outlineOn ? 1f : 0f);
                PixelartMaterialFactory.Configure(material, albedo, bandCount, outline);

                switch (ditherMode)
                {
                    case 1:   // Bayer 4×4 渐变态（出图档 pa-mid-bayer 同幅度）
                        material.SetFloat("_DitherMode", 0f);
                        material.SetFloat("_DitherStrength", 0.5f);
                        break;
                    case 2:   // 1-bit 密度图案，v3 两态撕边（pa-mid-density 同幅度）
                        material.SetFloat("_DitherMode", 1f);
                        material.SetFloat("_DitherStrength", 1f);
                        break;
                    default:
                        material.SetFloat("_DitherStrength", 0f);
                        break;
                }

                material.SetColor("_RimLightColor", rimOn ? Color.white : Color.black);
            }
        }

        // ------------------------------------------------------------------
        // 角色网格（CrewMeshFactory 同链重建）
        // ------------------------------------------------------------------

        void RebuildSlot(SubjectSlot slot)
        {
            if (slot == null || slot.bodyFilter == null || slot.headFilter == null
                || slot.bodyPivot == null || slot.headPivot == null)
                return;

            slot.bodyMesh = ReplaceMesh(slot.bodyMesh, CrewMeshFactory.CreateMesh(
                "CharCamDebug_BodyFrustum",
                CrewMeshFactory.Frustum(slot.topRadius, slot.bottomRadius, slot.bodyHeight, BodySides)),
                slot.bodyFilter);
            slot.headMesh = ReplaceMesh(slot.headMesh, CrewMeshFactory.CreateMesh(
                "CharCamDebug_HeadSphere",
                CrewMeshFactory.LowPolySphere(slot.headRadius, HeadSegments, HeadRings)),
                slot.headFilter);

            // Frustum 沿 Y 居中 → 枢轴抬到半高，脚底贴 y=0；Head 球心在原点 → 枢轴抬到球心高。
            slot.bodyPivot.localPosition = new Vector3(0f, slot.bodyHeight * 0.5f, 0f);
            slot.headPivot.localPosition = new Vector3(0f, slot.HeadCenterY, 0f);
            slot.built = true;
            _meshDirty = false;   // 滑杆只重建当前槽；不清脏会每帧重建
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
            Vector3 focus = FocusTarget();
            cameraTransform.position = focus + dir * CameraDistance;
            cameraTransform.LookAt(focus);
        }

        void ResetDefaults()
        {
            SubjectSlot active = Active;
            if (active != null)
            {
                active.topRadius = DefaultTopRadius;
                active.bottomRadius = DefaultBottomRadius;
                active.bodyHeight = DefaultBodyHeight;
                active.headRadius = DefaultHeadRadius;
                active.headLift = DefaultHeadLift;
            }
            visibleMeters = DefaultVisibleMeters;
            pixelScale = DefaultPixelScale;
            pitchDegrees = DefaultPitchDegrees;
            azimuthDegrees = DefaultAzimuthDegrees;
            ditherMode = DefaultDitherMode;
            bandCount = 3;
            outlineOn = true;
            rimOn = false;
            aaScaler = DefaultAaScaler;
            lightIntensity = 1f;
            sunShadowsOn = false;
            _meshDirty = true;
            _cameraDirty = true;
            _styleDirty = true;
        }

        /// <summary>当前角色的参数导成文本（可整段贴进 CrewVisualPrefabBuilder / 装配器常量对照）。</summary>
        public string ExportParameters()
        {
            SubjectSlot active = Active;
            string slotLine = active != null
                ? $"（编辑目标：{SlotLabel(activeSlot)}，站位 x {StationX(activeSlot).ToString("0.#")}）\n"
                : string.Empty;
            float top = active != null ? active.topRadius : 0f;
            float bottom = active != null ? active.bottomRadius : 0f;
            float bodyH = active != null ? active.bodyHeight : 0f;
            float headR = active != null ? active.headRadius : 0f;
            float lift = active != null ? active.headLift : 0f;
            float centerY = active != null ? active.HeadCenterY : 0f;
            float total = active != null ? active.TotalHeight : 0f;

            return "CharCamDebug 参数（米 / 度；可直接对照 CrewVisualPrefabBuilder 常量）\n"
                + slotLine
                + $"BodyTopRadius     = {top.ToString("0.####")}\n"
                + $"BodyBottomRadius  = {bottom.ToString("0.####")}\n"
                + $"BodyHeight        = {bodyH.ToString("0.####")}\n"
                + $"HeadSphereRadius  = {headR.ToString("0.####")}\n"
                + $"HeadLift          = {lift.ToString("0.####")}（头颈间距，0 = Godot 基准）\n"
                + $"HeadSphereCenterY = {centerY.ToString("0.####")}（身高−0.05+头半径+间距）\n"
                + $"TotalHeight       = {total.ToString("0.####")}\n"
                + $"可见米数          = {visibleMeters.ToString("0.#")}（worldPerPixel {WorldPerPixel(visibleMeters, pixelScale).ToString("0.####")}）\n"
                + $"PixelScale        = {pixelScale}\n"
                + $"PitchDegrees      = {pitchDegrees.ToString("0.#")}\n"
                + $"AzimuthDegrees    = {azimuthDegrees.ToString("0.#")}\n"
                + "—— 渲染风格 ——\n"
                + $"渲染路径          = {(renderPath == 0 ? "像素化" : "URP 原生")}\n"
                + $"抖动              = {ditherMode}（0 关 / 1 Bayer / 2 密度）\n"
                + $"色带档数          = {bandCount}\n"
                + $"描边              = {outlineOn}（地面恒不描边）\n"
                + $"边缘光            = {rimOn}\n"
                + $"内线降档 aaScaler = {aaScaler.ToString("0.##")}\n"
                + $"主光强度          = {lightIntensity.ToString("0.##")}\n"
                + $"太阳投影          = {sunShadowsOn}";
        }

        // ------------------------------------------------------------------
        // IMGUI 面板（调试工具不走游戏 UI 三层：一次性、零资产依赖）
        // ------------------------------------------------------------------

        void OnGUI()
        {
            // 角色跳转方块**常驻**（面板收起也能点）：槽 0 在左上、槽 1 在右上，
            // 点击把编辑目标与相机焦点跳到对应角色（SwitchSlot）。
            DrawSlotButtons();

            if (!showPanel)
            {
                if (GUI.Button(new Rect(8f, 40f, 140f, 22f), "显示参数面板 (F1)"))
                    showPanel = true;
                return;
            }

            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawPanel, "角色 / 镜头参数调试");
        }

        /// <summary>
        /// 角色方块（左上/右上）：选中态染绿 + ▶ 前缀。放在面板窗之外，是"跳转到对应角色"
        /// 的常驻入口——调参数时面板开着，看另一具角色时随手点一下就切过去。
        /// </summary>
        void DrawSlotButtons()
        {
            if (slots == null)
                return;

            for (int i = 0; i < slots.Length && i < 2; i++)
            {
                Rect rect = i == 0
                    ? new Rect(8f, 8f, 96f, 26f)
                    : new Rect(Screen.width - 104f, 8f, 96f, 26f);

                Color previous = GUI.backgroundColor;
                if (i == activeSlot)
                    GUI.backgroundColor = new Color(0.45f, 0.85f, 0.5f);
                if (GUI.Button(rect, (i == activeSlot ? "▶ " : "") + SlotLabel(i)))
                    SwitchSlot(i);
                GUI.backgroundColor = previous;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                showPanel = !showPanel;
            if (Input.GetKeyDown(KeyCode.Escape))
                GoBackToMainMenu();
        }

        /// <summary>
        /// 返回来源场景：从主菜单「调试菜单 → 角色镜头调试场」跳进来时，SceneLoader 的返回栈里
        /// 就是主菜单（GoBack 弹栈回去）；编辑器直接 Play 本场景则栈空，退而直接跳主菜单
        /// （按 <see cref="SceneLoader.StackDepth"/> 分流，不发空 GoBack——栈空它只告警不动作）。
        /// </summary>
        void GoBackToMainMenu()
        {
            SceneLoader loader = SceneLoader.Instance;
            if (loader == null)
            {
                Debug.LogWarning("[CharCamDebugController] SceneLoader 服务不在（脱离 Bootstrapper 运行？），无法跳转。");
                return;
            }

            if (loader.StackDepth > 0)
                loader.GoBack();
            else
                loader.ChangeScene(SceneNames.MainMenu);
        }

        void DrawPanel(int windowId)
        {
            SubjectSlot active = Active;
            if (active == null)
            {
                GUILayout.Label("角色槽未接线——重跑装配器（PirateCrew/Pixelart/烘焙角色镜头调试场景）。");
                GUI.DragWindow();
                return;
            }

            // 槽内切换（与屏幕左上/右上角的方块同效，面板里再给一行显式入口）。
            GUILayout.BeginHorizontal();
            GUILayout.Label("编辑目标", GUILayout.Width(88f));
            for (int i = 0; i < slots.Length && i < 2; i++)
                if (GUILayout.Button((activeSlot == i ? "▶ " : "") + SlotLabel(i)))
                    SwitchSlot(i);
            GUILayout.EndHorizontal();

            GUILayout.Label("—— 角色体格（只动当前角色，改后自动重建网格）——");
            active.topRadius = LabeledSlider("身体顶半径", active.topRadius, 0.05f, 1.0f, ref _meshDirty);
            active.bottomRadius = LabeledSlider("身体底半径", active.bottomRadius, 0.05f, 1.0f, ref _meshDirty);
            active.bodyHeight = LabeledSlider("身体高度", active.bodyHeight, 0.3f, 2.5f, ref _meshDirty);
            active.headRadius = LabeledSlider("头部半径", active.headRadius, 0.05f, 0.8f, ref _meshDirty);
            active.headLift = LabeledSlider("头颈间距", active.headLift, -0.3f, 0.5f, ref _meshDirty);
            GUILayout.Label($"  头心 y = {active.HeadCenterY.ToString("0.###")}，总高 = {active.TotalHeight.ToString("0.###")}（间距 0 = Godot 基准，重叠 {HeadBodyOverlap}）");

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
            GUILayout.Label("—— 渲染风格 ——");

            GUILayout.BeginHorizontal();
            GUILayout.Label("渲染路径", GUILayout.Width(88f));
            int newPath = renderPath;
            newPath = MutuallyExclusive(renderPath, 0, GUILayout.Toggle(renderPath == 0, "像素化")) ? 0 : newPath;
            newPath = MutuallyExclusive(renderPath, 1, GUILayout.Toggle(renderPath == 1, "URP 原生")) ? 1 : newPath;
            GUILayout.EndHorizontal();
            if (newPath != renderPath) { renderPath = newPath; _pathDirty = true; }

            // 像素化路径内的旋钮：URP 对照档下无意义，整组禁用。
            GUI.enabled = renderPath == 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label("抖动", GUILayout.Width(88f));
            int newDither = ditherMode;
            newDither = MutuallyExclusive(ditherMode, 0, GUILayout.Toggle(ditherMode == 0, "关")) ? 0 : newDither;
            newDither = MutuallyExclusive(ditherMode, 1, GUILayout.Toggle(ditherMode == 1, "Bayer")) ? 1 : newDither;
            newDither = MutuallyExclusive(ditherMode, 2, GUILayout.Toggle(ditherMode == 2, "密度")) ? 2 : newDither;
            GUILayout.EndHorizontal();
            if (newDither != ditherMode) { ditherMode = newDither; _styleDirty = true; }

            GUILayout.BeginHorizontal();
            GUILayout.Label("色带档数", GUILayout.Width(88f));
            int newBand = bandCount;
            for (int level = 2; level <= 4; level++)
                newBand = MutuallyExclusive(bandCount, level, GUILayout.Toggle(bandCount == level, level + " 档")) ? level : newBand;
            GUILayout.EndHorizontal();
            if (newBand != bandCount) { bandCount = newBand; _styleDirty = true; }

            GUILayout.BeginHorizontal();
            GUILayout.Label("描边", GUILayout.Width(88f));
            bool newOutline = GUILayout.Toggle(outlineOn, outlineOn ? "开" : "关");
            GUILayout.Space(16f);
            GUILayout.Label("边缘光", GUILayout.Width(52f));
            bool newRim = GUILayout.Toggle(rimOn, rimOn ? "开" : "关");
            GUILayout.EndHorizontal();
            if (newOutline != outlineOn) { outlineOn = newOutline; _styleDirty = true; }
            if (newRim != rimOn) { rimOn = newRim; _styleDirty = true; }

            aaScaler = LabeledSlider("内线降档", aaScaler, 0f, 2f, ref _styleDirty, "0.##");
            lightIntensity = LabeledSlider("主光强度", lightIntensity, 0f, 2f, ref _styleDirty, "0.##");

            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            GUILayout.Label("太阳投影", GUILayout.Width(88f));
            bool newShadow = GUILayout.Toggle(sunShadowsOn, sunShadowsOn ? "硬阴影" : "关");
            GUILayout.EndHorizontal();
            if (newShadow != sunShadowsOn) { sunShadowsOn = newShadow; _styleDirty = true; }

            // 链路反馈：旋钮有没有真的落到 rig / 光上，一行可判（不用对着画面猜）。
            Light sunNow = sun != null ? sun : RenderSettings.sun;
            GUILayout.Label($"  内线阈值 {rig.AAThreshold:0.###}｜光强乘数 {rig.lightIntensity:0.##}｜太阳投影 "
                + (sunNow != null && sunNow.shadows != LightShadows.None
                    ? $"开（bias {sunNow.shadowBias:0.###}/normal {sunNow.shadowNormalBias:0.###}）" : "关"));

            GUILayout.Space(6f);
            GUILayout.Label("—— 读数 ——");
            if (rig != null && rig.IsReady && rig.UnitSize > 0f && active != null)
            {
                float unit = rig.UnitSize;
                float totalPx = active.TotalHeight / unit;
                float bottomPx = active.bottomRadius * 2f / unit;
                float headPx = active.headRadius * 2f / unit;
                float canvasH = Mathf.Max(1, rig.RenderHeight);
                GUILayout.Label($"每艺术像素 {unit.ToString("0.####")} m｜可见 {(rig.RenderHeight * rig.worldPerPixel).ToString("0.#")} m（实际画布 {rig.RenderWidth}×{canvasH}）");
                GUILayout.Label($"{SlotLabel(activeSlot)} 总高 {totalPx.ToString("0.#")} px = 画面高 {100f * totalPx / canvasH:0.#}%");
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

            GUILayout.Space(4f);
            if (GUILayout.Button("返回主菜单 (Esc)"))
                GoBackToMainMenu();

            GUI.DragWindow();
        }

        /// <summary>
        /// 互斥选中项的点击判定（纯函数）：<paramref name="clicked"/> 是 Toggle 的**返回值**，
        /// 只有当它不等于该项当前的选中态时才代表用户**真的点了它**。
        ///
        /// 【为什么需要它】未被点击的 Toggle 返回它的 value 参数——旧写法
        /// <c>new = Toggle(current == level) ? level : new</c> 里，**当前选中项**的 Toggle
        /// 每帧都返回 true、把同帧刚点下的新值覆盖回去：3 档时点「2 档」永远落回 3、
        /// 抖动「密度」时点「关」永远回不来——"调不了 2 / 调不回去"就是它。
        /// </summary>
        static bool MutuallyExclusive(int current, int candidate, bool clicked)
        {
            return clicked != (current == candidate);
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
