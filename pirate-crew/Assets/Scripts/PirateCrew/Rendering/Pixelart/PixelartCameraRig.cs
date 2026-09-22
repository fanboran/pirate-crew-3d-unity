using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化着色路径**的相机装配（挂在本场景的相机上，一场景一个）。
    ///
    /// 【装配出什么】（接口契约：docs/技术/渲染/像素化着色路径-P4P5接口契约.md §0/§1）
    /// <list type="bullet">
    ///   <item><b>Cast 相机</b>：本相机的**子物体、local 恒等**——自动继承主相机的 Transform，
    ///         相机怎么动它怎么动，不需要任何接线（v3 `SloanePixelartCamera.cs:283-285` 同一手法）。
    ///         它 `targetTexture = ResultBuffer`，用 Cast 渲染器（物体/连通域/描边/边缘光/着色/调色板）。</item>
    ///   <item><b>主相机退化成上屏器</b>：清空 cullingMask、清屏改 Nothing——自己什么都不画，
    ///         由 Screen 渲染器上的 CopyFeature 把 ResultBuffer 点采样放大上屏。</item>
    ///   <item><b>两个分辨率域</b>：<b>屏幕档</b>（= 艺术画布 × pixelScale，几何与 7 张 G-buffer 在这里，
    ///         连通域判据也在这里）+ <b>艺术画布</b>（= 屏幕 ÷ pixelScale，描边/着色/合成/调色板在这里）。
    ///         尺寸都按「**锁 pixelScale 整数倍**」从实际屏幕反推（**这是本仓口径，不照抄 v3 的固定尺寸**：
    ///         v3 上屏那次 blit 不随宽高比调整，非 16:9 屏会横向拉伸——蓝图 §1.1 ⚠）。</item>
    /// </list>
    ///
    /// 【为什么几何画在屏幕档】v3 的艺术画布 320×180、G-buffer 1600×900 = 画布 × 5 = 它的**屏幕分辨率**
    /// ——"几何/G-buffer 就是常规延迟渲染的 G-buffer，全分辨率"，省 fill rate 的是**着色那几趟**。
    /// 本仓同构（创始人 2026-09-22 裁决）。
    ///
    /// 【像素密度口径】`worldPerPixel = 可见米数@1080p ÷ (1080 ÷ pixelScale)`：可见米数是**美术锚**、
    /// 不随 pixelScale 变（所以 5×→3× 时取景不动、只是颗粒变细）；而**屏幕分辨率越高、可见范围越大**
    /// （1080p 28m → 1440p 37m），因为艺术画布随屏幕变大。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class PixelartCameraRig : MonoBehaviour
    {
        const string CastCameraName = "Pixelart Cast Camera";

        /// <summary>透明件叠加相机的物体名（子物体、local 恒等，随主相机一起动）。</summary>
        const string OverlayCameraName = "Pixelart Overlay Camera";

        [Header("低分辨率域")]
        [Tooltip("像素化档位 = 一个艺术像素占几个**屏幕**像素（整数放大倍数，锁死）。3 = 1920×1080 下 640×360。")]
        [Min(1)] public int pixelScale = 3;

        [Tooltip("一个艺术像素的世界尺寸（米）。它与艺术画布高一起决定可见世界范围 = 艺术像素数 × 本值。"
            + "0.07778 = 1080p 下可见 28m 高（= 广角机位），改 pixelScale 时它要按"
            + "「可见米数 ÷ (1080 ÷ pixelScale)」重算，取景才不动。")]
        [Min(0.0001f)] public float worldPerPixel = 0.07778f;

        [Tooltip("由 pixelScale/worldPerPixel 反推正交 size 并写进相机。"
            + "关掉 = 相机自己管取景（本路径只保证像素网格整数倍，不保证范围随分辨率变大）。")]
        public bool deriveOrthographicSize = true;

        [Tooltip("连通域降档阈值 = 本值 ÷ 2（v3 的 AAScaler 同口径，默认 0.75）。")]
        [Range(0f, 2f)] public float aaScaler = 1.5f;

        [Header("墨线")]
        [Tooltip("墨线颜色。**中性近黑**：UI 面板令牌 INK 是 #120C14（带一点紫，在面板底色上稳），"
            + "但同一色画在 3D 的蓝灰地面上会读成紫——创始人 2026-09-22 报的就是这个。"
            + "3D 侧用中性近黑，UI 侧维持令牌，两边各自服务自己的底色。")]
        public Color inkColor = new Color(0.050980f, 0.050980f, 0.058824f, 1f);   // #0D0D0F

        [Header("渲染器索引（装配器写入；名字见 PixelartPath.CastRendererName / ScreenRendererName）")]
        [Tooltip("Cast 相机的渲染器索引。-1 = 不改写（用场景里已配好的）。")]
        public int castRendererIndex = -1;

        [Tooltip("主相机（上屏）的渲染器索引。必须是只挂 CopyFeature 的那个 Screen 渲染器——"
            + "挂成 Cast 渲染器会让主相机重跑一遍物体/着色 pass 并把结果冲掉。-1 = 不改写。")]
        public int screenRendererIndex = -1;

        [Header("Cast 相机（留空则运行时按子物体 local 恒等自动创建）")]
        public Camera castCamera;

        [Header("透明件叠加（可选：游戏本体用；试点场景不填）")]
        [Tooltip("叠加相机的渲染器索引（`PixelartOverlay_Renderer` 的索引，由装配器查出后写入）。"
            + "**-1 = 不建叠加相机**：本路径的物体 pass 只画不透明材质，半透明内容（FX/危险虚线/"
            + "接触阴影/弹道预览）会整类消失，所以游戏本体的相机必须填这个索引；"
            + "试点场景没有半透明内容，留 -1 即可。")]
        public int overlayRendererIndex = -1;

        [Header("光")]
        [Tooltip("主光。留空则用 RenderSettings.sun（场景的烘焙主光）。")]
        public Light sun;

        [Tooltip("主光强度乘数（色带亮度整体的美术旋钮）。")]
        [Range(0f, 4f)] public float lightIntensity = 1f;

        Camera _screenCamera;
        Camera _castCamera;
        UniversalAdditionalCameraData _castCameraData;
        bool _castCameraCreatedAtRuntime;

        /// <summary>透明件叠加相机（只在 <see cref="overlayRendererIndex"/> ≥ 0 时存在；总是运行时自建）。</summary>
        Camera _overlayCamera;

        int _allocatedWidth;
        int _allocatedHeight;

        // 屏幕 → 像素网格的映射（日志与整数性判断用）。
        int _chosenScale = 3;
        int _screenWidth;
        int _screenHeight;
        int _residualX;
        int _residualY;
        bool _exactScale = true;
        string _loggedMappingKey;

        // 主相机被本组件改掉的设置（OnDisable 还原，避免"挂上来就回不去"）。
        CameraClearFlags _savedClearFlags;
        int _savedCullingMask;
        float _savedFarClip;
        bool _savedPostProcessing;

        /// <summary>上屏相机（主相机；本组件的宿主）。</summary>
        public Camera ScreenCamera { get { return _screenCamera; } }

        /// <summary>低分辨率渲染代理相机（主相机的子物体）。</summary>
        public Camera CastCamera { get { return _castCamera; } }

        /// <summary>低分辨率结果缓冲（Cast 相机的 targetTexture；Point = 上屏时点采样放大）。</summary>
        public RenderTexture ResultBuffer { get; private set; }

        // ---- 屏幕档（几何 + G-buffer）----
        /// <summary>G-buffer：亮部色 + 覆盖标记（a）。</summary>
        public RenderTexture AlbedoBuffer { get; private set; }
        /// <summary>G-buffer：屏幕空间几何法线（连通域输入）。</summary>
        public RenderTexture Normal0Buffer { get; private set; }
        /// <summary>G-buffer：切线空间法线贴图后的世界法线（着色输入）。</summary>
        public RenderTexture Normal1Buffer { get; private set; }
        /// <summary>G-buffer：光滑度 / 金属度。</summary>
        public RenderTexture PhysicalBuffer { get; private set; }
        /// <summary>G-buffer：优先级 / 法线边阈值 / AA 缩放。</summary>
        public RenderTexture ShapeBuffer { get; private set; }
        /// <summary>G-buffer：主光档数 / 抖动 / 边光档数 / applyOutline。</summary>
        public RenderTexture PaletteBuffer { get; private set; }
        /// <summary>G-buffer：逐物体边缘光色。</summary>
        public RenderTexture RimLightPropertyBuffer { get; private set; }
        /// <summary>屏幕档深度（可采样；着色里重建 positionWS 用）。</summary>
        public RenderTexture DepthBuffer { get; private set; }

        // ---- 艺术画布域 ----
        /// <summary>连通域结论。</summary>
        public RenderTexture ConnectivityResultBuffer { get; private set; }
        /// <summary>墨线标记。</summary>
        public RenderTexture OutlineBuffer { get; private set; }
        /// <summary>漫反射结果。</summary>
        public RenderTexture DiffuseBuffer { get; private set; }
        /// <summary>高光结果。</summary>
        public RenderTexture SpecularBuffer { get; private set; }
        /// <summary>环境光结果。</summary>
        public RenderTexture GIBuffer { get; private set; }
        /// <summary>边缘光累加结果。</summary>
        public RenderTexture RimLightBuffer { get; private set; }

        /// <summary>艺术画布宽（像素）。</summary>
        public int RenderWidth { get; private set; }
        /// <summary>艺术画布高（像素）。</summary>
        public int RenderHeight { get; private set; }
        /// <summary>屏幕档宽（像素）= 艺术画布宽 × pixelScale。</summary>
        public int FineWidth { get; private set; }
        /// <summary>屏幕档高（像素）。</summary>
        public int FineHeight { get; private set; }
        /// <summary>本帧采用的像素化档位。</summary>
        public int PixelScale { get { return _chosenScale; } }

        /// <summary>1 艺术像素的世界长度（= 2×正交size ÷ 艺术画布高）。物体级吸附也用它。</summary>
        public float UnitSize
        {
            get
            {
                if (_screenCamera == null || RenderHeight <= 0)
                    return 0f;
                return _screenCamera.orthographicSize * 2f / RenderHeight;
            }
        }

        /// <summary>1 细像素（屏幕档）的世界长度。</summary>
        public float FineUnitSize
        {
            get
            {
                if (_screenCamera == null || FineHeight <= 0)
                    return 0f;
                return _screenCamera.orthographicSize * 2f / FineHeight;
            }
        }

        /// <summary>连通域降档阈值（= aaScaler ÷ 2）。</summary>
        public float AAThreshold { get { return aaScaler * 0.5f; } }

        /// <summary>本装配是否已就绪（缓冲与相机齐备；未就绪时各 Feature 直接跳过）。</summary>
        public bool IsReady
        {
            get
            {
                return DeviceSupportsPath && _castCamera != null && ResultBuffer != null
                    && AlbedoBuffer != null && OutlineBuffer != null;
            }
        }

        /// <summary>物体 pass 的 G-buffer 张数（albedo/normal0/normal1/physical/shape/palette/rim）。</summary>
        public const int GbufferCount = 7;

        /// <summary>
        /// **本设备能不能跑本路径**：要能同时绑 7 张渲染目标、且支持 compute 的 UAV 写。
        ///
        /// 【为什么必须挡住，而不是让它抛异常】无头环境（`-nographics`，PlayMode 测试用的空设备）
        /// 只支持 1 张渲染目标、也不支持"格式可随机写"：物体 pass 的 `SetRenderTarget(7 张)` 会抛
        /// `ArgumentException: colors.Length is 7 and exceeds the maximum number of supported render targets`，
        /// 缓冲创建会刷 `RenderTexture.Create failed: format unsupported for random writes`。
        /// 不挡的话，"能不能跑 PlayMode 测试"会变成"看设备支持不支持"，而且报错会污染
        /// `LogAssert`（测试框架把意外 Error/Exception 记成失败）。
        /// 挡在这里之后，本路径在这种设备上**整趟 inert**（每个 Feature 都先问 IsReady）——
        /// 不抛异常、不刷错误日志，测试照跑，游戏在真 GPU 上照常。
        ///
        /// 目标平台是 Windows standalone（DX11/12 支持 8 张），所以这条不是"给低端机降级"，
        /// 而是"给没有 GPU 的环境留一条不炸的路"。
        /// </summary>
        public static bool DeviceSupportsPath
        {
            get { return SystemInfo.supportedRenderTargetCount >= GbufferCount && SystemInfo.supportsComputeShaders; }
        }

        void OnEnable()
        {
            _screenCamera = GetComponent<Camera>();
            if (_screenCamera == null)
            {
                Debug.LogError("[PixelartCameraRig] 本组件必须挂在相机上。");
                enabled = false;
                return;
            }

            _savedClearFlags = _screenCamera.clearFlags;
            _savedCullingMask = _screenCamera.cullingMask;
            _savedFarClip = _screenCamera.farClipPlane;

            // 主相机先钉渲染器：它就是上屏器，不该跑物体/着色 pass。
            ApplyRendererIndex(_screenCamera, screenRendererIndex);

            // 关掉主相机的后处理：它跑在像素化**之后**的全屏域，作用对象是一张"什么都不画、不清屏"的
            // 颜色缓冲——既没意义又是"上采样后再做颜色操作"这条红线的现成入口（渲染篇 §1 红线 4）。
            UniversalAdditionalCameraData screenData = _screenCamera.GetUniversalAdditionalCameraData();
            if (screenData != null)
            {
                _savedPostProcessing = screenData.renderPostProcessing;
                screenData.renderPostProcessing = false;
            }

            EnsureCastCamera();
            EnsureBuffers();
            EnsureOverlayCamera();
            PixelartPath.ActiveRig = this;
        }

        static void ApplyRendererIndex(Camera camera, int index)
        {
            if (camera == null || index < 0)
                return;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null)
                return;

            data.SetRenderer(index);
        }

        void OnDisable()
        {
            if (PixelartPath.ActiveRig == this)
                PixelartPath.ActiveRig = null;

            if (_castCamera != null)
            {
                _castCamera.targetTexture = null;
                // 只销毁运行时自己建的；装配器写进场景的那台留着（场景资产不动）。
                if (_castCameraCreatedAtRuntime && _castCamera.gameObject != null)
                    DestroyImmediate(_castCamera.gameObject);
                _castCamera = null;
                _castCameraData = null;
            }

            if (_overlayCamera != null)
            {
                if (_overlayCamera.gameObject != null)
                    DestroyImmediate(_overlayCamera.gameObject);
                _overlayCamera = null;
            }

            ReleaseBuffers();

            if (_screenCamera != null)
            {
                _screenCamera.clearFlags = _savedClearFlags;
                _screenCamera.cullingMask = _savedCullingMask;
                _screenCamera.farClipPlane = _savedFarClip;

                UniversalAdditionalCameraData screenData = _screenCamera.GetUniversalAdditionalCameraData();
                if (screenData != null)
                    screenData.renderPostProcessing = _savedPostProcessing;
            }
        }

        void Update()
        {
            if (_castCamera == null)
                return;

            // 屏幕尺寸变化（窗口拉伸/分辨率切换）→ 两档缓冲与上屏尺寸一起重算。
            ComputeTargetSize(out int wantWidth, out int wantHeight);
            if (wantWidth != _allocatedWidth || wantHeight != _allocatedHeight)
                EnsureBuffers();

            // 由"一个艺术像素的世界尺寸"反推正交 size：可见世界 = 艺术像素数 × worldPerPixel。
            if (deriveOrthographicSize)
                _screenCamera.orthographicSize = RenderHeight * worldPerPixel * 0.5f;

            LogScaleMapping();

            // Cast 相机必须与主相机同视野：它是代理，不是第二台取景器。
            _castCamera.orthographic = _screenCamera.orthographic;
            _castCamera.orthographicSize = _screenCamera.orthographicSize;
            _castCamera.backgroundColor = _screenCamera.backgroundColor;
            // 【层掩码用"主相机被清掉之前"的那份】主相机自己的 cullingMask 已被置 0（它只上屏），
            // 若这里跟着同步，Cast 相机就什么都看不见——症状是"画面只剩背景色"（且不报错）。
            _castCamera.cullingMask = _savedCullingMask;

            // 叠加相机同样只跟着主相机走，不需要任何接线。
            if (_overlayCamera != null)
            {
                _overlayCamera.orthographic = _screenCamera.orthographic;
                _overlayCamera.orthographicSize = _screenCamera.orthographicSize;
                _overlayCamera.nearClipPlane = _screenCamera.nearClipPlane;
                _overlayCamera.farClipPlane = _screenCamera.farClipPlane;
                _overlayCamera.backgroundColor = _screenCamera.backgroundColor;
            }

            LogSelfCheckOnce();
            PushLightGlobals();
        }

        /// <summary>
        /// 按**实际屏幕**反推两档尺寸：**锁"一个艺术像素占几个屏幕像素"这个整数倍数**（pixelScale）。
        ///
        /// 【为什么口径是"锁倍数"而不是"锁画布高"】曾经是"高锁 216、宽随宽高比"，那套只在
        /// **屏幕高恰好是 216 的整数倍**时成立：1920×1080 → 384×216、块 5（✓），
        /// 但 2560×1440 → 2560÷384 = 6.67（**非整数 ✗**）。非整数放大让块边长在 6/7 之间混排——
        /// **全屏看着"像素不齐"、放大到 1:1 或整数倍看却是干净的**（创始人报过这个症状）。
        ///
        /// 【屏幕档 = 艺术画布 × pixelScale】取法确定，不依赖屏幕是否被整除——
        /// 于是"屏幕档的块中心"与"艺术画布像素中心"是同一个点（`id*k + k/2`），
        /// 点采样取到的就是它，降采与"直接在画布上光栅化"逐点等价。
        /// </summary>
        void ComputeTargetSize(out int width, out int height)
        {
            int screenWidth = Mathf.Max(2, Screen.width);
            int screenHeight = Mathf.Max(2, Screen.height);
            int k = Mathf.Max(1, pixelScale);

            int exactWidth = screenWidth / k * k;
            int exactHeight = screenHeight / k * k;

            _chosenScale = k;
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            _residualX = screenWidth - exactWidth;
            _residualY = screenHeight - exactHeight;
            _exactScale = _residualX == 0 && _residualY == 0;

            width = Mathf.Max(2, screenWidth / k);
            height = Mathf.Max(2, screenHeight / k);
        }

        void EnsureCastCamera()
        {
            if (_castCamera != null)
                return;

            if (castCamera != null)
            {
                _castCamera = castCamera;
                _castCameraCreatedAtRuntime = false;
            }
            else
            {
                var go = new GameObject(CastCameraName);
                go.transform.SetParent(transform, false);   // local 恒等：继承主相机的 Transform
                _castCamera = go.AddComponent<Camera>();
                _castCameraCreatedAtRuntime = true;
            }

            _castCamera.orthographic = true;
            _castCamera.nearClipPlane = _screenCamera.nearClipPlane;
            _castCamera.farClipPlane = _screenCamera.farClipPlane;
            _castCamera.clearFlags = CameraClearFlags.SolidColor;
            _castCamera.backgroundColor = _screenCamera.backgroundColor;
            _castCamera.cullingMask = _screenCamera.cullingMask;
            _castCamera.depth = _screenCamera.depth - 1f;   // 先渲染：结果给主相机上屏
            _castCamera.useOcclusionCulling = false;
            _castCamera.allowHDR = false;                   // 渲染篇 §6：HDR 关（色带渐变会脏）
            _castCamera.allowMSAA = false;                  // MSAA 关（灰边头号来源）

            _castCameraData = _castCamera.GetUniversalAdditionalCameraData();
            if (_castCameraData == null)
                _castCameraData = _castCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            _castCameraData.renderType = CameraRenderType.Base;
            _castCameraData.renderPostProcessing = false;   // 后处理全在低分辨率域之后手工做
            // 【阴影本轮打开】色带 × 实时阴影同屏是创始人 2026-09-22 的裁决（渲染篇 §4.6 原裁决为跳过）。
            // 阴影图由 URP 的 MainLightShadow 在**主光**上跑；着色那几趟在低分辨率域读 shadowCoord。
            _castCameraData.renderShadows = true;
            _castCameraData.requiresColorOption = CameraOverrideOption.Off;
            _castCameraData.requiresDepthOption = CameraOverrideOption.Off;
            _castCameraData.antialiasing = AntialiasingMode.None;
            _castCameraData.volumeLayerMask = 0;            // 不吃任何 Volume（Bloom/调色一律不参与）
            _castCameraData.volumeTrigger = null;
            ApplyRendererIndex(_castCamera, castRendererIndex);

            // 主相机退化成"纯变换持有者 + 上屏 blit"（v3 `SloanePixelartCamera.cs:113-119`）：
            // 不画任何东西、不清屏（清屏会盖掉将要贴上去的 ResultBuffer）。
            _screenCamera.clearFlags = CameraClearFlags.Nothing;
            _screenCamera.cullingMask = 0;
        }

        /// <summary>
        /// 建**透明件叠加相机**（只有 <see cref="overlayRendererIndex"/> ≥ 0 时才建）。
        ///
    /// 【为什么必须有这么一台】本路径的几何只有一份"不透明数据 + 全屏着色"，**没有混合**：
    /// 半透明内容在它下面会整类消失（不报错，就是不画）。这台相机用**标准 URP 渲染器**
    /// 队列过滤成"只画 Transparent"，以**栈内 Overlay** 跟在主相机之后往同一目标上画
    /// （见下方 renderType 注释：独立 Base 相机会整屏盖掉成图），保住 FX / 危险虚线 / 接触阴影 /
    /// 弹道预览这些"看不清就没法玩"的东西。
        ///
        /// 【代价（已登记待办，别当成已解决）】它们的颗粒是全分辨率的（不参与像素化），
        /// 而且这台相机的深度缓冲是空的 ⇒ 遮挡关系不判定（崖后的爆炸会画在崖前）。
        /// </summary>
        void EnsureOverlayCamera()
        {
            if (_overlayCamera != null || overlayRendererIndex < 0 || _screenCamera == null)
                return;

            var go = new GameObject(OverlayCameraName);
            go.transform.SetParent(transform, false);   // local 恒等：随主相机（Cinemachine）一起动

            _overlayCamera = go.AddComponent<Camera>();
            _overlayCamera.orthographic = _screenCamera.orthographic;
            _overlayCamera.orthographicSize = _screenCamera.orthographicSize;
            _overlayCamera.nearClipPlane = _screenCamera.nearClipPlane;
            _overlayCamera.farClipPlane = _screenCamera.farClipPlane;
            // 只清深度、**保留像素化成图的颜色**（清颜色会把上屏结果抹掉）。
            _overlayCamera.clearFlags = CameraClearFlags.Depth;
            _overlayCamera.backgroundColor = _screenCamera.backgroundColor;
            _overlayCamera.cullingMask = ~0;            // 画什么由渲染器的队列过滤决定，不用层
            _overlayCamera.depth = _screenCamera.depth + 1f;
            _overlayCamera.useOcclusionCulling = false;
            _overlayCamera.allowHDR = false;
            _overlayCamera.allowMSAA = false;

            UniversalAdditionalCameraData data = _overlayCamera.GetUniversalAdditionalCameraData();
            if (data == null)
                data = _overlayCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            // 【必须是栈内 Overlay，不能是第二台 Base】URP 里每台 Base 相机最后都会把自己的
            // 中间目标**整屏 blit 到后备缓冲**——叠加相机一过，主相机刚 blit 上去的像素化成图
            // 就被它盖掉，屏幕只剩"背景色 + 特效"（r13 编辑器首跑实测，result.png 是好的、
            // Game 视图是纯色）。入栈后它往**同一目标**上接着画、不做最终 blit，成图才保得住。
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.renderShadows = false;                 // 阴影已由 Cast 相机那趟画过
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = 0;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            // 队列过滤沿用渲染器 3 资产上的 m_OpaqueLayerMask=0 / m_TransparentLayerMask=~0
            // （装配期 `PixelartPathInstaller.SetQueueFilter` 写入——URP 14 相机数据上没有这对掩码，
            // 过滤只能落在渲染器上）。栈内与基相机跨渲染器在 URP 14 无强约束，实测为准。
            data.SetRenderer(overlayRendererIndex);

            // 入栈：跟在主相机（Base）之后往同一目标上画。
            UniversalAdditionalCameraData mainData = _screenCamera.GetUniversalAdditionalCameraData();
            if (mainData != null && !mainData.cameraStack.Contains(_overlayCamera))
                mainData.cameraStack.Add(_overlayCamera);

            Debug.Log("[PixelartCameraRig] 透明件叠加相机已建（栈内 Overlay，渲染器 " + overlayRendererIndex
                + "，只画 Transparent 队列）—— FX / 危险虚线 / 接触阴影 / 弹道预览走这一档。");
        }

        /// <summary>
        /// 设备不支持时打一行**警告**（只打一次，用 LogWarning 而不是 Error：无头测试里
        /// Error/Exception 会被测试框架记成失败，而这条是环境事实、不是缺陷）。
        /// 说清"本路径 inert、画面会停在主相机的空屏"，免得下次有人对着空屏查半天。
        /// </summary>
        static void LogUnsupportedDeviceOnce()
        {
            if (_unsupportedDeviceLogged)
                return;
            _unsupportedDeviceLogged = true;
            Debug.LogWarning("[PixelartCameraRig] 本设备不支持本路径（需要同时绑 " + GbufferCount
                + " 张渲染目标 + compute UAV）：当前 渲染目标上限 " + SystemInfo.supportedRenderTargetCount
                + "、compute " + (SystemInfo.supportsComputeShaders ? "支持" : "不支持")
                + "。像素化路径整趟 inert（常见于批处理 `-nographics` 与无头测试环境，真 GPU 上不会出现）。");
        }

        static bool _unsupportedDeviceLogged;

        void EnsureBuffers()
        {
            // 设备跑不了（无头空设备 / 没有 compute）就不建缓冲：建了也绑不上，
            // 只会刷一串 "RenderTexture.Create failed: format unsupported for random writes"（见 DeviceSupportsPath）。
            if (!DeviceSupportsPath)
            {
                LogUnsupportedDeviceOnce();
                return;
            }
            ComputeTargetSize(out int width, out int height);
            if (width == _allocatedWidth && height == _allocatedHeight
                && ResultBuffer != null && AlbedoBuffer != null)
                return;

            ReleaseBuffers();

            _allocatedWidth = width;
            _allocatedHeight = height;
            RenderWidth = width;
            RenderHeight = height;
            FineWidth = width * Mathf.Max(1, _chosenScale);
            FineHeight = height * Mathf.Max(1, _chosenScale);

            // ---- 屏幕档：几何 + 7 张 G-buffer + 可采样深度 ----
            // 格式都是"数据"而非颜色，故 sRGB 一律关（线性值直存直取）。
            // albedo 用 8 位够（色带是有限调色板），其余用 16 位浮点：
            //   v3 用 SNorm16 + PackFloatInt8bit 字节打包，那套与格式强耦合、换格式即静默失效
            //   （蓝图 §2 注意 1）——本仓直存，不赌它。
            AlbedoBuffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGB32, "PixelartAlbedo", false);
            Normal0Buffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGBHalf, "PixelartNormal0", false);
            Normal1Buffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGBHalf, "PixelartNormal1", false);
            PhysicalBuffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGBHalf, "PixelartPhysical", false);
            ShapeBuffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGBHalf, "PixelartShape", false);
            PaletteBuffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGBHalf, "PixelartPalette", false);
            RimLightPropertyBuffer = NewColor(FineWidth, FineHeight, RenderTextureFormat.ARGBHalf, "PixelartRimLightProperty", false);
            DepthBuffer = NewDepth(FineWidth, FineHeight, "PixelartDepth");

            // ---- 艺术画布域 ----
            // 【两张带 UAV 的】连通域结论与边缘光都由 compute 用 `RWTexture2D` 写，
            // 而 `RenderTextureDescriptor.enableRandomWrite` 不设就是**绑不上 UAV、写入被静默丢弃**
            // （Unity 不抛异常）。所以这两张必须显式打开。
            ConnectivityResultBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGB32, "PixelartConnectivityResult", false, randomWrite: true);
            OutlineBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGB32, "PixelartOutline", false);
            DiffuseBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGBHalf, "PixelartDiffuse", false);
            SpecularBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGBHalf, "PixelartSpecular", false);
            GIBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGBHalf, "PixelartGI", false);
            RimLightBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGBHalf, "PixelartRimLight", false, randomWrite: true);

            // 结果缓冲 = Cast 相机的 targetTexture。Point 是像素化的最后一环
            // （上屏 blit 走 CoreBlit 的 Nearest pass）。
            ResultBuffer = NewColor(RenderWidth, RenderHeight, RenderTextureFormat.ARGB32, "PixelartResult", false);
            ResultBuffer.filterMode = FilterMode.Point;

            if (_castCamera != null)
                _castCamera.targetTexture = ResultBuffer;

            PushStaticGlobals();
        }

        /// <summary>
        /// 把"屏幕 ↔ 像素网格"的映射打一行日志（映射变化时打一次）。
        /// 【为什么要打】"像素不齐"这种症状只能靠这组数判断：屏幕宽高、放大倍数、两档尺寸、
        /// 以及**残余像素**（不能被倍数整除时那几像素会落在一条边上）。静默的整数性失效
        /// 正是这条路径最难发现的一类问题。
        /// </summary>
        void LogScaleMapping()
        {
            string key = _screenWidth + "x" + _screenHeight + "@" + _chosenScale;
            if (key == _loggedMappingKey)
                return;
            _loggedMappingKey = key;

            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[PixelartCameraRig] 像素网格：屏幕 {0}×{1} → 放大 {2}× → 艺术画布 {3}×{4}"
                + "（屏幕档 {5}×{6}，块 {2} 屏幕像素，可见 {7:F1}m 高，每艺术像素 {8:F4}m）",
                _screenWidth, _screenHeight, _chosenScale, RenderWidth, RenderHeight,
                FineWidth, FineHeight, RenderHeight * worldPerPixel, worldPerPixel);

            if (_exactScale)
                Debug.Log(line + " —— 整数映射严格成立。");
            else
                Debug.LogWarning(line + " —— **非严格整数映射**：右/下残余 "
                    + _residualX + "×" + _residualY + " 屏幕像素（落在一条边上）。"
                    + "屏幕宽高同时能被 " + _chosenScale + " 整除时才会完全对齐。");
        }

        /// <summary>
        /// 装配自检（只打一次）：把"这条路有没有接上"的证据写进日志。
        /// 这条路径的失效方式大多是**静默**的（shader 被剥离 / 渲染器索引没写上 / 层掩码为 0 →
        /// 画面只剩背景色而不报错），所以首帧自检比事后猜要省事得多。
        /// </summary>
        void LogSelfCheckOnce()
        {
            if (_selfCheckedLogged || !IsReady)
                return;

            _selfCheckedLogged = true;
            int shaderMatched = 0;
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                    && renderer.sharedMaterial.shader.name == PixelartPath.ObjectShaderName)
                    shaderMatched++;
            }

            Debug.Log("[PixelartCameraRig] 自检：屏幕相机 " + _screenCamera.name
                + "（层掩码 0 + 不清屏）、Cast 相机 " + (_castCamera != null ? _castCamera.name : "<无>")
                + "（层掩码 " + _savedCullingMask + "）、艺术画布 " + RenderWidth + "×" + RenderHeight
                + "、屏幕档 " + FineWidth + "×" + FineHeight
                + "、走本路径物体 shader 的 renderer 数 " + shaderMatched
                + "（为 0 说明材质没挂上 or 场景没内容）。");
        }

        bool _selfCheckedLogged;

        static RenderTexture NewColor(int width, int height, RenderTextureFormat format, string name, bool sRGB,
            bool randomWrite = false)
        {
            return NewBuffer(width, height, format, 0, name, sRGB, randomWrite);
        }

        static RenderTexture NewDepth(int width, int height, string name)
        {
            return NewBuffer(width, height, RenderTextureFormat.Depth, 24, name, false);
        }

        static RenderTexture NewBuffer(int width, int height, RenderTextureFormat format, int depthBits,
            string name, bool sRGB, bool randomWrite = false)
        {
            // Unity 2022.3 的 RenderTexture.sRGB 是只读的——sRGB 与否要在 RenderTextureDescriptor 上给
            // （这是本版本的正确 API，不是绕路）。enableRandomWrite 同理：不设就是 compute 写不进去。
            var desc = new RenderTextureDescriptor(Mathf.Max(1, width), Mathf.Max(1, height), format, depthBits)
            {
                sRGB = sRGB,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = randomWrite,
            };
            var rt = new RenderTexture(desc)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void ReleaseBuffers()
        {
            // 逐个释放：v3 的释放循环在循环体内把容器置空，实际只放掉第 0 张（蓝图 §4.4 第 1 条）——
            // 这里逐字段 Release，不做任何取巧。
            AlbedoBuffer = Release(AlbedoBuffer);
            Normal0Buffer = Release(Normal0Buffer);
            Normal1Buffer = Release(Normal1Buffer);
            PhysicalBuffer = Release(PhysicalBuffer);
            ShapeBuffer = Release(ShapeBuffer);
            PaletteBuffer = Release(PaletteBuffer);
            RimLightPropertyBuffer = Release(RimLightPropertyBuffer);
            DepthBuffer = Release(DepthBuffer);
            ConnectivityResultBuffer = Release(ConnectivityResultBuffer);
            OutlineBuffer = Release(OutlineBuffer);
            DiffuseBuffer = Release(DiffuseBuffer);
            SpecularBuffer = Release(SpecularBuffer);
            GIBuffer = Release(GIBuffer);
            RimLightBuffer = Release(RimLightBuffer);
            ResultBuffer = Release(ResultBuffer);

            _allocatedWidth = 0;
            _allocatedHeight = 0;
            RenderWidth = 0;
            RenderHeight = 0;
            FineWidth = 0;
            FineHeight = 0;
        }

        static RenderTexture Release(RenderTexture rt)
        {
            if (rt == null)
                return null;
            if (rt.IsCreated())
                rt.Release();
            DestroyImmediateIfNotPlaying(rt);
            return null;
        }

        static void DestroyImmediateIfNotPlaying(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        /// <summary>下发与尺寸相关的全局量（尺寸变化时调一次；每帧由 BeforeRender pass 再确认）。</summary>
        void PushStaticGlobals()
        {
            PushSizeGlobals();
        }

        void PushSizeGlobals()
        {
            Shader.SetGlobalFloat(PixelartPath.RTHeightId, RenderHeight);
            Shader.SetGlobalFloat(PixelartPath.RTWidthId, RenderWidth);
            Shader.SetGlobalFloat(PixelartPath.FineHeightId, FineHeight);
            Shader.SetGlobalFloat(PixelartPath.FineWidthId, FineWidth);
            Shader.SetGlobalFloat(PixelartPath.UnitSizeId, UnitSize);
            Shader.SetGlobalFloat(PixelartPath.FineUnitSizeId, FineUnitSize);
            Shader.SetGlobalFloat(PixelartPath.SamplingScaleId, _chosenScale);
            Shader.SetGlobalFloat(PixelartPath.AAThresholdId, AAThreshold);
        }

        /// <summary>下发主光/环境光/墨色（每帧；色带数学只有这几个光照量）。</summary>
        void PushLightGlobals()
        {
            Light light = sun != null ? sun : RenderSettings.sun;
            Vector3 lightDir = Vector3.up;
            Color lightColor = Color.white;

            if (light != null)
            {
                // 指向光源（着色用 saturate(dot(L, N))）：Unity 的 forward 是光行进方向，取反。
                lightDir = -light.transform.forward;
                lightColor = light.color * (light.intensity * lightIntensity);
            }

            Color ambient = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor;

            // 【颜色一律显式 .linear + SetGlobalVector】材质 Color 属性（albedo 那一侧）在线性工程里会被
            // 自动做 sRGB→线性，而 shader **全局**颜色的转换行为是另一条路径、口径不明确。
            // 本路径不赌它：所有颜色全局全部显式 .linear 下发。
            Shader.SetGlobalVector(PixelartPath.LightDirId, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
            Shader.SetGlobalVector(PixelartPath.LightColorId, ToLinear(lightColor));
            Shader.SetGlobalVector(PixelartPath.AmbientColorId, ToLinear(ambient));
            Shader.SetGlobalVector(PixelartPath.InkColorId, ToLinear(inkColor));

            PushSizeGlobals();
            LogPushedValuesOnce(lightDir, lightColor, ambient);
        }

        static Vector4 ToLinear(Color color)
        {
            Color linear = color.linear;
            return new Vector4(linear.r, linear.g, linear.b, linear.a);
        }

        /// <summary>
        /// 把实际下发的光照量打一行日志。
        /// 【为什么值得专门打】"某面色号跟 albedo 逐位相同"这类症状可以来自三个完全不同的根因
        /// （着色被旁路 / 光色是白的 / 线性-伽马错一层），只看出图分不清；把下发值打出来，
        /// 判据脚本就能按 <c>sRGB(multiStep(ndotl)·lightColor·albedo + ambient·albedo)</c>
        /// 算出**期望值**去对，颜色管线有没有错一层立刻可判。
        /// </summary>
        void LogPushedValuesOnce(Vector3 lightDir, Color lightColor, Color ambient)
        {
            if (_pushedValuesLogged)
                return;
            _pushedValuesLogged = true;

            Color lightLinear = lightColor.linear;
            Color ambientLinear = ambient.linear;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[PixelartCameraRig] 下发值：lightDir=({0:F4},{1:F4},{2:F4}) "
                + "lightColorLinear=({3:F4},{4:F4},{5:F4}) ambientLinear=({6:F4},{7:F4},{8:F4}) "
                + "unitSize={9:F6} fineUnitSize={10:F6} rt={11}x{12} fine={13}x{14} k={15}",
                lightDir.x, lightDir.y, lightDir.z,
                lightLinear.r, lightLinear.g, lightLinear.b,
                ambientLinear.r, ambientLinear.g, ambientLinear.b,
                UnitSize, FineUnitSize, RenderWidth, RenderHeight, FineWidth, FineHeight, _chosenScale));
        }

        bool _pushedValuesLogged;

        /// <summary>
        /// 把屏幕档的 7 张 G-buffer + 深度设成 shader 全局（物体 pass 画完后调；后续各趟只吃全局）。
        /// </summary>
        public void PublishGbuffers(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(PixelartPath.AlbedoBufferId, AlbedoBuffer);
            cmd.SetGlobalTexture(PixelartPath.Normal0BufferId, Normal0Buffer);
            cmd.SetGlobalTexture(PixelartPath.Normal1BufferId, Normal1Buffer);
            cmd.SetGlobalTexture(PixelartPath.PhysicalBufferId, PhysicalBuffer);
            cmd.SetGlobalTexture(PixelartPath.ShapeBufferId, ShapeBuffer);
            cmd.SetGlobalTexture(PixelartPath.PaletteBufferId, PaletteBuffer);
            cmd.SetGlobalTexture(PixelartPath.RimLightPropertyBufferId, RimLightPropertyBuffer);
            cmd.SetGlobalTexture(PixelartPath.DepthBufferId, DepthBuffer);
        }

        /// <summary>把艺术画布域的结果缓冲设成全局（连通域结论 / 墨线标记，供着色各趟读）。</summary>
        public void PublishArtBuffers(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(PixelartPath.ConnectivityResultId, ConnectivityResultBuffer);
            cmd.SetGlobalTexture(PixelartPath.OutlineBufferId, OutlineBuffer);
        }
    }
}
