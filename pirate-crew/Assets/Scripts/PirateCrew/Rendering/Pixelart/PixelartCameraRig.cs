using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化着色路径**的相机装配（挂在本场景的相机上，一场景一个）。
    ///
    /// 【装配出什么】
    /// <list type="bullet">
    ///   <item><b>Cast 相机</b>：本相机的**子物体、local 恒等**——于是它自动继承主相机的 Transform，
    ///         相机怎么动它怎么动，不需要任何接线（v3 `SloanePixelartCamera.cs:283-285` 的同一手法）。
    ///         它 `targetTexture = ResultBuffer`，用 Cast 渲染器（物体 pass + 低分辨率域着色）。</item>
    ///   <item><b>主相机退化成上屏器</b>：清空 cullingMask、清屏改 Nothing——它自己什么都不画，
    ///         由 Screen 渲染器上的 CopyFeature 把 ResultBuffer 点采样放大上屏。</item>
    ///   <item><b>低分辨率缓冲</b>：3 张 G-buffer + 深度 + ResultBuffer，全部按
    ///         「高锁档位、宽随屏幕宽高比、偶数对齐」分配（**这是本仓口径，不照抄 v3 的固定 320×180**：
    ///         v3 上屏那次 blit 不随宽高比调整，非 16:9 屏会横向拉伸——蓝图 §1.1 ⚠）。</item>
    /// </list>
    ///
    /// 【为什么几何真的渲染在低分辨率】G-buffer 是低分辨率 RT，物体 pass 在它上面 DrawRenderers——
    /// 光栅化分辨率由**渲染目标**决定，不由相机决定。所以这条路是"几何直接渲进低分辨率 RT"，
    /// 没有"全分辨率渲染再降采"那一步（渲染篇 §3.1 推论 4 的前一半）。
    ///
    /// 【光】主光方向/颜色与环境光每帧下发为 shader 全局（本路径不做 URP 的 shadow variant；
    /// 色带表面接实时投影必脏、且试点场景就一盏平行光）。暗部色的来源是**环境光项**
    /// （v3 口径：`diffuse = multiStep(ndotl)·lightColor·albedo`，暗面 = `albedo × 环境色`）。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class PixelartCameraRig : MonoBehaviour
    {
        const string CastCameraName = "Pixelart Cast Camera";

        [Header("低分辨率域")]
        [Tooltip("低分辨率 RT 的高度（像素）。宽按屏幕宽高比自适应并对齐偶数。216 = 1920 宽屏上 1 像素 5 屏幕像素。")]
        [Min(32)] public int renderHeight = 216;

        [Header("渲染器索引（装配器写入；名字见 PixelartPath.CastRendererName / ScreenRendererName）")]
        [Tooltip("Cast 相机的渲染器索引（物体 pass + 着色）。-1 = 不改写（用场景里已配好的）。")]
        public int castRendererIndex = -1;

        [Tooltip("主相机（上屏）的渲染器索引。必须是只挂 CopyFeature 的那个 Screen 渲染器——"
            + "挂成 Cast 渲染器会让主相机重跑一遍物体/着色 pass 并把结果冲掉。-1 = 不改写。")]
        public int screenRendererIndex = -1;

        [Header("Cast 相机（留空则运行时按子物体 local 恒等自动创建）")]
        [Tooltip("低分辨率渲染代理相机。装配器会在编辑器侧建好并写进场景；为空时运行时自动创建。")]
        public Camera castCamera;

        [Header("光")]
        [Tooltip("主光。留空则用 RenderSettings.sun（场景的烘焙主光）。")]
        public Light sun;

        [Tooltip("主光强度乘数（色带亮度整体的美术旋钮；URP 非物理模式下 _MainLightColor = color × intensity）。")]
        [Range(0f, 4f)] public float lightIntensity = 1f;

        Camera _screenCamera;
        Camera _castCamera;
        UniversalAdditionalCameraData _castCameraData;
        bool _castCameraCreatedAtRuntime;

        int _allocatedWidth;
        int _allocatedHeight;

        // 主相机被本组件改掉的设置（OnDisable 还原，避免"挂上来就回不去"）。
        CameraClearFlags _savedClearFlags;
        int _savedCullingMask;
        float _savedFarClip;
        bool _savedPostProcessing;

        /// <summary>上屏相机（主相机；本组件的宿主）。</summary>
        public Camera ScreenCamera
        {
            get { return _screenCamera; }
        }

        /// <summary>低分辨率渲染代理相机（主相机的子物体）。</summary>
        public Camera CastCamera
        {
            get { return _castCamera; }
        }

        /// <summary>低分辨率结果缓冲（Cast 相机的 targetTexture；FilterMode.Point = 上屏时点采样放大）。</summary>
        public RenderTexture ResultBuffer { get; private set; }

        /// <summary>G-buffer：albedo（rgb）+ 覆盖标记（a）。</summary>
        public RenderTexture AlbedoBuffer { get; private set; }

        /// <summary>G-buffer：世界法线（原样存 [-1,1]）。</summary>
        public RenderTexture NormalBuffer { get; private set; }

        /// <summary>G-buffer：逐物体着色参数。</summary>
        public RenderTexture PropertyBuffer { get; private set; }

        /// <summary>G-buffer 的深度附件（物体 pass 的深度测试）。</summary>
        public RenderTexture DepthBuffer { get; private set; }

        /// <summary>低分辨率 RT 的当前宽度（像素）。</summary>
        public int RenderWidth { get; private set; }

        /// <summary>低分辨率 RT 的当前高度（像素）。</summary>
        public int RenderHeight { get; private set; }

        /// <summary>1 低分辨率像素的世界长度 = 2×正交size ÷ RT高（相机 snap 用）。</summary>
        public float UnitSize
        {
            get
            {
                if (_screenCamera == null || RenderHeight <= 0)
                    return 0f;
                return _screenCamera.orthographicSize * 2f / RenderHeight;
            }
        }

        /// <summary>本装配是否已就绪（缓冲与相机齐备；未就绪时各 Feature 直接跳过）。</summary>
        public bool IsReady
        {
            get { return _castCamera != null && ResultBuffer != null && AlbedoBuffer != null; }
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

            // 关掉主相机的后处理：它跑在像素化**之后**的全屏域（AfterRendering 之前），
            // 作用对象是一张"什么都不画、不清屏"的颜色缓冲——既没意义又是"上采样后再做颜色操作"
            // 这条红线的现成入口（渲染篇 §1 红线 4）。
            UniversalAdditionalCameraData screenData = _screenCamera.GetUniversalAdditionalCameraData();
            if (screenData != null)
            {
                _savedPostProcessing = screenData.renderPostProcessing;
                screenData.renderPostProcessing = false;
            }

            EnsureCastCamera();
            EnsureBuffers();
            PushStaticGlobals();

            PixelartPath.ActiveRig = this;
        }

        /// <summary>给相机钉渲染器索引（<paramref name="index"/> &lt; 0 = 不改写）。</summary>
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

            // 屏幕尺寸变化（窗口拉伸/分辨率切换）→ 低分辨率 RT 与上屏尺寸一起重算。
            int wantWidth = ComputeWidth(renderHeight);
            if (wantWidth != _allocatedWidth || renderHeight != _allocatedHeight)
                EnsureBuffers();

            // Cast 相机必须与主相机同视野：它是代理，不是第二台取景器。
            _castCamera.orthographic = _screenCamera.orthographic;
            _castCamera.orthographicSize = _screenCamera.orthographicSize;
            _castCamera.backgroundColor = _screenCamera.backgroundColor;
            // 【层掩码用"主相机被清掉之前"的那份】主相机自己的 cullingMask 已被置 0（它只上屏），
            // 若这里跟着同步，Cast 相机就什么都看不见——第一版的实测症状正是"画面只剩背景色"
            // （物体 pass 的 cullResults 空、DrawRenderers 画不出任何东西，且不报错）。
            _castCamera.cullingMask = _savedCullingMask;

            LogSelfCheckOnce();

            PushLightGlobals();
        }

        /// <summary>按屏幕宽高比算低分辨率宽度（偶数对齐，免半像素列；16:9 高 180 → 320）。</summary>
        int ComputeWidth(int height)
        {
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            return Mathf.Max(2, Mathf.CeilToInt(height * aspect * 0.5f) * 2);
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
            _castCameraData.renderShadows = false;
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

        void EnsureBuffers()
        {
            int height = Mathf.Max(32, renderHeight);
            int width = ComputeWidth(height);
            if (width == _allocatedWidth && height == _allocatedHeight
                && ResultBuffer != null && AlbedoBuffer != null)
                return;

            ReleaseBuffers();

            _allocatedWidth = width;
            _allocatedHeight = height;
            RenderWidth = width;
            RenderHeight = height;

            // G-buffer 三张：都是"数据"而非"颜色"，故 sRGB 一律关（线性值直存直取）。
            // - albedo：UNorm8 足够（色带是有限调色板，且暗部误差被环境项吸收）
            // - 法线：16 位浮点原样存 [-1,1]，不用 v3 的 SNorm16 + 字节打包
            //   （那套打包与 MRT 格式强耦合、换格式即静默失效——蓝图 §2 注意 1；本路径不需要它）
            AlbedoBuffer = NewBuffer(width, height, RenderTextureFormat.ARGB32, "PixelartAlbedo", false);
            NormalBuffer = NewBuffer(width, height, RenderTextureFormat.ARGBHalf, "PixelartNormal", false);
            PropertyBuffer = NewBuffer(width, height, RenderTextureFormat.ARGBHalf, "PixelartProperty", false);
            DepthBuffer = new RenderTexture(width, height, 24, RenderTextureFormat.Depth)
            {
                name = "PixelartDepth",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            DepthBuffer.Create();

            // 结果缓冲 = Cast 相机的 targetTexture。FilterMode.Point 是像素化的最后一环
            // （上屏 blit 走 CoreBlit 的 Nearest pass，点采样放大）。
            ResultBuffer = NewBuffer(width, height, RenderTextureFormat.ARGB32, "PixelartResult", false);
            ResultBuffer.filterMode = FilterMode.Point;

            if (_castCamera != null)
                _castCamera.targetTexture = ResultBuffer;

            PushStaticGlobals();
        }

        /// <summary>
        /// 装配自检（只打一次）：把"这条路有没有接上"的证据写进日志。
        /// 这条路径的失效方式大多是**静默**的（shader 被剥离 / 渲染器索引没写上 / 层掩码为 0 →
        /// 画面只剩背景色而不报错），所以首帧自检比事后猜要省事得多。
        /// </summary>
        bool _selfCheckLogged;

        void LogSelfCheckOnce()
        {
            if (_selfCheckLogged || !IsReady)
                return;

            _selfCheckLogged = true;
            int shaderMatched = 0;
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                    && renderer.sharedMaterial.shader.name == PixelartPath.ObjectShaderName)
                    shaderMatched++;
            }

            Debug.Log("[PixelartCameraRig] 自检：屏幕相机 " + _screenCamera.name
                + "（层掩码 0 + 不清屏）、Cast 相机 " + (_castCamera != null ? _castCamera.name : "<无>")
                + "（层掩码 " + _savedCullingMask + "）、低分辨率 RT " + RenderWidth + "×" + RenderHeight
                + "、走本路径物体 shader 的 renderer 数 " + shaderMatched
                + "（为 0 说明材质没挂上 or 场景没内容）。");
        }

        static RenderTexture NewBuffer(int width, int height, RenderTextureFormat format, string name, bool sRGB)
        {
            // Unity 2022.3 的 RenderTexture.sRGB 是只读的——sRGB 与否要在
            // RenderTextureDescriptor 上给（这是本版本的正确 API，不是绕路）。
            var desc = new RenderTextureDescriptor(width, height, format, 0)
            {
                sRGB = sRGB,
                useMipMap = false,
                autoGenerateMips = false,
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
            // 这里逐字段 Release，不做任何取巧。（缓冲是属性，ref 传参在 C# 里不合法，改返回值。）
            ResultBuffer = Release(ResultBuffer);
            AlbedoBuffer = Release(AlbedoBuffer);
            NormalBuffer = Release(NormalBuffer);
            PropertyBuffer = Release(PropertyBuffer);
            DepthBuffer = Release(DepthBuffer);
            _allocatedWidth = 0;
            _allocatedHeight = 0;
            RenderWidth = 0;
            RenderHeight = 0;
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
            Shader.SetGlobalFloat(PixelartPath.RTHeightId, RenderHeight);
            Shader.SetGlobalFloat(PixelartPath.RTWidthId, RenderWidth);
            Shader.SetGlobalFloat(PixelartPath.UnitSizeId, UnitSize);
        }

        /// <summary>下发主光与环境光（每帧；色带数学只有这两个光源量）。</summary>
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

            // 【颜色一律显式 .linear + SetGlobalVector】材质 Color 属性（albedo 那一侧）在
            // 线性工程里会被自动做 sRGB→线性，而 shader **全局**颜色的转换行为是另一条路径、
            // 口径不明确（同一份代码既可能"已转换"也可能"没转换"，取决于 Unity 版本与属性声明）。
            // 本路径不赌它：三个颜色全局全部显式 .linear 下发。
            Shader.SetGlobalVector(PixelartPath.LightDirId, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
            Shader.SetGlobalVector(PixelartPath.LightColorId, ToLinear(lightColor));
            Shader.SetGlobalVector(PixelartPath.AmbientColorId, ToLinear(ambient));

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
                + "unitSize={9:F6} rt={10}x{11}",
                lightDir.x, lightDir.y, lightDir.z,
                lightLinear.r, lightLinear.g, lightLinear.b,
                ambientLinear.r, ambientLinear.g, ambientLinear.b,
                UnitSize, RenderWidth, RenderHeight));
        }

        bool _pushedValuesLogged;

        /// <summary>把三张 G-buffer 设成 shader 全局（物体 pass 画完后调；着色 pass 只吃全局）。</summary>
        public void PublishBuffersToShaders(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(PixelartPath.AlbedoBufferId, AlbedoBuffer);
            cmd.SetGlobalTexture(PixelartPath.NormalBufferId, NormalBuffer);
            cmd.SetGlobalTexture(PixelartPath.PropertyBufferId, PropertyBuffer);
        }
    }
}
