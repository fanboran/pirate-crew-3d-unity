using System.Collections.Generic;
using UnityEngine;
using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.AssetInspect
{
    /// <summary>
    /// **资产特写观察调试场**（`Assets/Scenes/Pixelart/PixelartAssetInspect.unity`）的运行时控制器。
    ///
    /// 【这个场景是干什么的】把**任意一件游戏内资产**（船员预制体 / 云 / 岛体零件 / 装饰件…）
    /// 单独摆进像素化路径的观察台，环绕、缩放、拨灯光与色带档看观感——渲染/建模/描边调参
    /// 不再依赖进战斗场景拍图（创始人 2026-10-07 提出）。
    ///
    /// 【资产从哪来】编辑器域按目录扫描（<see cref="CatalogRoots"/> 下的 Prefab / Model / Mesh，
    /// 同 `SceneAssetManifestBuilder.Collect` 的口径），面板里过滤选中。**不搞注册表**——
    /// 观察台要的是"资产落了库就能看"，目录扫描天然不漏新件。场景进 Build Settings
    /// **仅开发集**、发行包不含 ⇒ AssetDatabase 直载在编辑器里可用即达标（播放器构建里
    /// 本场景不存在）。
    ///
    /// 【机制全部复用既有口径】像素化路径（<see cref="PixelartPathInstaller"/> +
    /// <see cref="PixelartCameraRig"/>）、机位方向（<see cref="PixelartPilotScene.CameraDirection"/>，
    /// 30°/45° 与试点场景同源）、worldPerPixel 换算（同 CharCamDebugController）、
    /// 运行期材质派生（<see cref="PixelartMaterialFactory"/>，TryGetAlbedo 走
    /// _BaseColor/_Color/_BaseColorA 三档）、组件剥除白名单（Transform/MeshFilter/MeshRenderer，
    /// 同 PixelartStageKit.PlaceCrew——Rigidbody/PirateBase 留着会在播放器里自由落体/
    /// 按战斗网格重摆）。抖动图案编辑器域按路径补挂（运行期无按路径加载入口，见
    /// PixelartMaterialFactory 类头；本场景只在编辑器里跑，无播放器退化路径）。
    ///
    /// 【交互】F1 面板开关、F12 截图（落 `export/asset-inspect/`）、Esc 返回主菜单；
    /// 右键拖拽环绕、滚轮缩放（右键环绕与两态交互惯例一致）；面板滑杆与鼠标写同一组字段。
    /// 调试工具不走游戏 UI 三层：IMGUI 一次性、零资产依赖（CharCamDebugController 同款）。
    /// </summary>
    public sealed class AssetInspectController : MonoBehaviour
    {
        /// <summary>场景名（装配器保存的资产名；调试菜单按名 LoadScene 用）。</summary>
        public const string SceneName = "PixelartAssetInspect";

        // ------------------------------------------------------------------
        // 口径常量（装配器与运行时同一份真源）
        // ------------------------------------------------------------------

        /// <summary>参考屏幕高（worldPerPixel 换算的分母；同 CharCamDebugController 的 1080p 口径）。</summary>
        public const int ReferenceScreenHeight = 1080;

        /// <summary>机位到构图中心的距离（正交相机下只影响裁剪面）。</summary>
        public const float CameraDistance = 60f;

        /// <summary>默认可见米数：特写起步 8 m（PixelartPilotScene 机位梯子的近档）。</summary>
        public const float DefaultVisibleMeters = 8f;

        /// <summary>默认像素档 3×（角色镜头调试场同款起步档，只管本场景起始状态）。</summary>
        public const int DefaultPixelScale = 3;

        /// <summary>默认俯角 30°（规则像素阶梯，PixelartPilotScene.PitchDegrees 同源）。</summary>
        public const float DefaultPitchDegrees = 30f;

        /// <summary>默认方位角 45°（对称菱形；观察台要 360° 全向，面板滑杆与右键拖拽不受 90° 限）。</summary>
        public const float DefaultAzimuthDegrees = 45f;

        /// <summary>裸网格件的兜底色（同装配器参照件的本色；源材质取不到 albedo 时的显式兜底）。</summary>
        public static readonly Color BareMeshColor = PixelartMaterialFactory.Hex("D8BE8A");

        /// <summary>资产扫描根（代码即真源；同 SceneAssetManifestBuilder 的 ModelsRoot 口径）。</summary>
        static readonly string[] CatalogRoots =
        {
            "Assets/Prefabs",
            "Assets/Art/Models",
        };

        /// <summary>抖动图案资产路径（编辑器域装载；与 PixelartStageKit.DitherFolder 指同一张）。</summary>
        const string DitherPatternPath = "Assets/Pixelart/Textures/Dither/ToonDither_0.png";

        /// <summary>右键拖拽环绕的角速度（度/像素；横纵同灵敏度，两态相机 0.25°/px 同款）。</summary>
        const float OrbitDegreesPerMousePixel = 0.25f;

        /// <summary>把「可见多少米高」换算成一个艺术像素的世界尺寸（米）。</summary>
        public static float WorldPerPixel(float visibleMeters, int pixelScale)
        {
            return visibleMeters * Mathf.Max(1, pixelScale) / ReferenceScreenHeight;
        }

        // ------------------------------------------------------------------
        // 场景引用（装配器接线）
        // ------------------------------------------------------------------

        [Header("镜头（装配器接线）")]
        [Tooltip("像素化相机 rig：worldPerPixel / pixelScale 写它，取景由它重算。")]
        public PixelartCameraRig rig;
        [Tooltip("主相机（上屏器）的 Transform：俯角/方位角改了要重摆机位。")]
        public Transform cameraTransform;
        [Tooltip("太阳（留空用 RenderSettings.sun 兜底，与 rig.PushLightGlobals 同口径）。")]
        public Light sun;

        [Header("观察台（装配器接线）")]
        [Tooltip("特写件实例化挂点（原点）。")]
        public Transform subjectRoot;
        [Tooltip("场景根：材质旋钮全场景生效的扫描范围（地面/参照件 + 特写件）。")]
        public Transform sceneRoot;

        // ------------------------------------------------------------------
        // 可调参数（面板滑杆与鼠标直接改这些）
        // ------------------------------------------------------------------

        [Header("镜头取景")]
        [Tooltip("可见米数（美术锚）：1080p 屏上画面高度看到的米数。装载特写件时按包围盒自动估。")]
        [Min(1f)] public float visibleMeters = DefaultVisibleMeters;
        [Tooltip("像素档位（一个艺术像素占几个屏幕像素，2–5）。")]
        [Range(PixelartCameraRig.PixelScaleMin, PixelartCameraRig.PixelScaleMax)]
        public int pixelScale = DefaultPixelScale;
        [Tooltip("俯角（度）。30° = 规则像素阶梯。")]
        [Range(5f, 80f)] public float pitchDegrees = DefaultPitchDegrees;
        [Tooltip("方位角（度）。观察台全向 0–360。")]
        [Range(0f, 360f)] public float azimuthDegrees = DefaultAzimuthDegrees;
        [Tooltip("构图中心：装载特写件时按包围盒中心重算。")]
        public Vector3 framingTarget = new Vector3(0f, 0.5f, 0f);

        [Header("渲染风格（默认值 = 场景装配口径）")]
        [Tooltip("渲染路径：0 = 像素化着色路径（现役）；1 = URP 原生平滑渲染（对照档：rig 停用 + 材质换 URP/Lit）。")]
        [Range(0, 1)] public int renderPath;
        [Tooltip("抖动：0 = 关；1 = Bayer 4×4（渐变态）；2 = 1-bit 密度图案。")]
        [Range(0, 2)] public int ditherMode = 1;
        [Tooltip("色带档数（主光 quantization 档）。")]
        [Range(2, 4)] public int bandCount = 4;
        [Tooltip("描边总开关（地面恒不描边——大平面加轮廓线只会全屏糊边，实现口径 §4.1）。")]
        public bool outlineOn = true;
        [Tooltip("边缘光：拨亮逐物体 _RimLightColor（白）。")]
        public bool rimOn;
        [Tooltip("连通域降档阈值系数（rig.aaScaler）：0 = 内线降档永不成立。")]
        [Range(0f, 2f)] public float aaScaler = 0.69f;
        [Tooltip("主光强度乘数（rig.lightIntensity，色带亮度整体旋钮）。")]
        [Range(0f, 2f)] public float lightIntensity = 1f;
        [Tooltip("太阳实时投影（硬阴影）。打开时自动切小物件口径的 bias（装配默认 0.06/0.65 是大平面口径，会把小件的影子推没）。")]
        public bool sunShadowsOn;

        [Header("面板")]
        public bool showPanel = true;
        public Rect panelRect = new Rect(12f, 12f, 430f, 60f);

        // 滑杆/鼠标 → 应用 的脏标记（相机重摆与材质重放都只在变化时做）。
        bool _cameraDirty = true;
        bool _styleDirty = true;
        bool _pathDirty = true;
        int _appliedPixelScale = -1;

        // 资产清单与当前选中（编辑器域填充；播放器构建里恒空）。
        readonly List<string> _catalog = new List<string>();
        readonly List<string> _filtered = new List<string>();
        string _filter = string.Empty;
        Vector2 _listScroll;
        int _current = -1;
        GameObject _subject;
        Vector3 _subjectBoundsSize;
        string _status = "从列表选一件资产开始观察。";

        // URP 原生对照档：renderer 与其原材质的对位表（退出对照档时原样还原）。
        readonly List<MeshRenderer> _urpSwapped = new List<MeshRenderer>();
        readonly List<Material> _urpOriginals = new List<Material>();
        readonly List<Material> _urpProxies = new List<Material>();
        bool _urpModeActive;

        // 派生材质缓存（同一源材质只派生一次；换件重载不重复造）。
        readonly Dictionary<string, Material> _derived = new Dictionary<string, Material>();
        Texture2D _ditherPattern;

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void Start()
        {
#if UNITY_EDITOR
            _ditherPattern = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(DitherPatternPath);
            if (_ditherPattern == null)
                global::PirateCrew.Core.Log.Warn("[AssetInspectController] 抖动图案没摸到："
                    + DitherPatternPath + "（派生材质的抖动会退化成硬边色带）。");
            BuildCatalog();
            if (_filtered.Count > 0)
                LoadEntry(_catalog.IndexOf(_filtered[0]));
#else
            _status = "资产观察台是编辑器专用场景（仅开发集，发行包不含）。";
#endif
        }

        void LateUpdate()
        {
            ApplyIfDirty();
        }

        void ApplyIfDirty()
        {
            if (_cameraDirty || (rig != null && rig.pixelScale != pixelScale))
                ApplyCamera();
            if (_pathDirty)
                ApplyRenderPath();
            if (_styleDirty)
                ApplyStyle();
        }

        // ------------------------------------------------------------------
        // 资产清单（编辑器域目录扫描）
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        void BuildCatalog()
        {
            var seen = new HashSet<string>();
            _catalog.Clear();
            AddCatalogKind(seen, "t:Prefab");
            AddCatalogKind(seen, "t:Model");
            AddCatalogKind(seen, "t:Mesh");
            _catalog.Sort(System.StringComparer.Ordinal);
            Refilter();
        }

        void AddCatalogKind(HashSet<string> seen, string filter)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets(filter, CatalogRoots);
            for (int i = 0; i < guids.Length; i++)
            {
                if (!seen.Add(guids[i]))
                    continue;
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                    _catalog.Add(path);
            }
        }

        void Refilter()
        {
            _filtered.Clear();
            for (int i = 0; i < _catalog.Count; i++)
            {
                if (string.IsNullOrEmpty(_filter)
                    || _catalog[i].IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(_catalog[i]);
            }
        }

        /// <summary>按清单序号装载：FBX/Prefab 直接实例化，裸 .asset 网格搭 MeshFilter+MeshRenderer。</summary>
        GameObject InstantiateAsset(string assetPath)
        {
            if (assetPath.EndsWith(".asset"))
            {
                Mesh mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                if (mesh == null)
                    return null;
                var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetPath));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>();
                return go;
            }

            GameObject source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return source != null ? Object.Instantiate(source) : null;
        }
#endif

        /// <summary>装载清单里第 <paramref name="catalogIndex"/> 件：剥组件 → 派生换装 → 包围盒自动取景。</summary>
        public void LoadEntry(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= _catalog.Count)
                return;
            _current = catalogIndex;
            string path = _catalog[catalogIndex];

            // 对照档开着时先退出（新实例按像素化派生换装，装载完再按面板状态重进对照档）。
            int wantedPath = renderPath;
            if (wantedPath == 1)
            {
                renderPath = 0;
                ApplyRenderPath();
            }

            ClearSubject();

#if UNITY_EDITOR
            GameObject go = InstantiateAsset(path);
#else
            GameObject go = null;
#endif
            if (go == null)
            {
                _status = "装载失败：" + path;
                return;
            }

            _subject = go;
            _subject.transform.SetParent(subjectRoot, false);
            _subject.transform.localPosition = Vector3.zero;
            StripNonVisualComponents(go);
            ConvertToPathMaterials(go);

            bool hasRenderers = AutoFrame(go);
            _subjectBoundsSize = hasRenderers ? ComputeBounds(go).size : Vector3.zero;

            if (wantedPath == 1)
            {
                renderPath = 1;
                ApplyRenderPath();
            }
            _cameraDirty = true;
            _styleDirty = true;

            _status = "当前：" + DisplayOf(path) + (hasRenderers ? "" : "（没有网格 renderer，画面上看不到东西）");
        }

        void ClearSubject()
        {
            if (_subject == null)
                return;
            Destroy(_subject);
            _subject = null;
        }

        /// <summary>
        /// 剥掉可视化三件套（Transform/MeshFilter/MeshRenderer）以外的全部组件——
        /// 口径同 <c>PixelartStageKit.PlaceCrew</c>：观察台只画几何，物理与游戏逻辑
        /// （Rigidbody 自由落体、PirateBase 按战斗网格重摆）留进来全是干扰。多趟清扫
        /// 对付 <c>[RequireComponent]</c> 连锁补回。
        /// </summary>
        void StripNonVisualComponents(GameObject root)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                int removed = 0;
                Component[] components = root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component is Transform || component is MeshFilter || component is MeshRenderer)
                        continue;
                    Destroy(component);
                    removed++;
                }
                if (removed == 0)
                    break;
            }
        }

        /// <summary>特写件全部换成本路径材质：派生档（PixelartMaterialFactory，取色三段式）。</summary>
        void ConvertToPathMaterials(GameObject root)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material original = renderers[i].sharedMaterial;
                if (original != null && original.shader != null
                    && original.shader.name == PixelartPath.ObjectShaderName)
                    continue;   // 已是本路径材质（如对照档还原后的原件）

                Material derived = DeriveMaterial(original);
                if (derived != null)
                    renderers[i].sharedMaterial = derived;
            }
        }

        Material DeriveMaterial(Material original)
        {
            string key = original != null ? original.name : "<无>";
            Material material;
            if (_derived.TryGetValue(key, out material))
                return material;

            Color albedo;
            if (!PixelartMaterialFactory.TryGetAlbedo(original, out albedo))
            {
                global::PirateCrew.Core.Log.Warn("[AssetInspectController] 材质 \"" + key
                    + "\" 取不到 albedo（无 _BaseColor/_Color/_BaseColorA），按裸网格兜底色上。");
                albedo = BareMeshColor;
            }

            material = PixelartMaterialFactory.Create(PixelartMaterialFactory.DerivedName(key), albedo);
            if (material == null)
                return null;

            // 抖动图案运行期没有按路径加载入口（PixelartMaterialFactory 类头），编辑器域补挂——
            // 本场景只在编辑器里跑，没有播放器退化路径。
            if (_ditherPattern != null)
            {
                material.SetTexture("_DitherPattern", _ditherPattern);
                material.SetFloat("_DitherPatternSize", _ditherPattern.width);
            }

            _derived[key] = material;
            return material;
        }

        /// <summary>按包围盒自动取景：构图中心 = 包围盒中心，可见米数 = 竖向尺寸留 40% 边。返回有无 renderer。</summary>
        bool AutoFrame(GameObject root)
        {
            Bounds bounds = ComputeBounds(root);
            if (bounds.size == Vector3.zero && root.GetComponentsInChildren<MeshRenderer>().Length == 0)
                return false;

            framingTarget = bounds.center;
            float horizontalRadius = new Vector2(bounds.size.x, bounds.size.z).magnitude * 0.5f;
            float extent = Mathf.Max(bounds.size.y, horizontalRadius);
            visibleMeters = Mathf.Clamp(extent * 1.4f, 2f, 200f);
            return true;
        }

        Bounds ComputeBounds(GameObject root)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>清单里的上一件/下一件（按过滤后的可见序，两端回绕）。</summary>
        public void StepFiltered(int direction)
        {
            if (_filtered.Count == 0)
                return;
            string currentPath = _current >= 0 ? _catalog[_current] : null;
            int position = currentPath != null ? _filtered.IndexOf(currentPath) : -1;
            int next = (position + direction + _filtered.Count) % _filtered.Count;
            LoadEntry(_catalog.IndexOf(_filtered[next]));
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
            cameraTransform.position = framingTarget + dir * CameraDistance;
            cameraTransform.LookAt(framingTarget);
        }

        // ------------------------------------------------------------------
        // 渲染风格（像素化路径内的旋钮 + URP 原生对照档；机制同 CharCamDebugController）
        // ------------------------------------------------------------------

        /// <summary>
        /// 渲染路径切换。**URP 原生对照档**：停用 rig、关 Cast 相机、场景材质换成 URP/Lit 代理
        /// （_BaseColor 取自像素化材质的同名属性）——像素化物体 shader 在标准 URP 渲染器下不画
        /// （物体整类消失的静默失效家族），材质必须一起换。切回时逐 renderer 还原原材质。
        /// </summary>
        void ApplyRenderPath()
        {
            _pathDirty = false;
            if (rig == null || sceneRoot == null)
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
                    global::PirateCrew.Core.Log.Error("[AssetInspectController] 找不到 URP/Lit，对照档不可用，回退像素化。");
                    renderPath = 0;
                    return;
                }

                MeshRenderer[] renderers = sceneRoot.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material original = renderers[i].sharedMaterial;
                    if (original == null || original.shader == null
                        || original.shader.name != PixelartPath.ObjectShaderName)
                        continue;

                    var proxy = new Material(lit) { name = original.name + "_URPProxy" };
                    proxy.SetColor("_BaseColor", original.HasProperty("_BaseColor")
                        ? original.GetColor("_BaseColor") : Color.white);
                    if (proxy.HasProperty("_Smoothness"))
                        proxy.SetFloat("_Smoothness", 0f);

                    _urpSwapped.Add(renderers[i]);
                    _urpOriginals.Add(original);
                    _urpProxies.Add(proxy);
                    renderers[i].sharedMaterial = proxy;
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
        /// 像素化材质旋钮全量重放（幂等），机制与 CharCamDebugController 同款：改的是
        /// <c>renderer.material</c>（实例化副本）——Play 模式下直接写 sharedMaterial 会
        /// 持久化进材质资产（编辑器知名坑），实例副本退出 Play 自动还原。
        /// </summary>
        void ApplyStyle()
        {
            _styleDirty = false;
            if (rig == null || sceneRoot == null)
                return;

            rig.aaScaler = aaScaler;
            rig.lightIntensity = lightIntensity;

            Light sunLight = sun != null ? sun : RenderSettings.sun;
            if (sunLight != null)
            {
                // 开投影同步切小物件口径的 bias：装配默认 0.06/0.65 是 160m 大平面压自遮挡的
                // 口径，对单人高的件会把影子沿法线推淡到不可辨（CharCamDebugController 同款教训）。
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

            MeshRenderer[] renderers = sceneRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer sceneRenderer = renderers[i];
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
                    case 1:   // Bayer 4×4 渐变态
                        material.SetFloat("_DitherMode", 0f);
                        material.SetFloat("_DitherStrength", 0.5f);
                        break;
                    case 2:   // 1-bit 密度图案，两态撕边
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
        // 截图与返回
        // ------------------------------------------------------------------

        /// <summary>整屏（Game View = 像素化上屏结果）落 PNG 到 <c>export/asset-inspect/</c>。</summary>
        public void CaptureScreenshot()
        {
            string repoRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string dir = System.IO.Path.Combine(repoRoot, "export", "asset-inspect");
            System.IO.Directory.CreateDirectory(dir);

            string name = _current >= 0 ? DisplayOf(_catalog[_current]) : "none";
            string invalid = new string(System.IO.Path.GetInvalidFileNameChars());
            for (int i = 0; i < invalid.Length; i++)
                name = name.Replace(invalid[i], '_');

            string path = System.IO.Path.Combine(dir,
                "asset-inspect-" + name + "-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            _status = "已请求截图（帧末落盘）：" + path;
            Debug.Log("[AssetInspectController] " + _status);
        }

        /// <summary>
        /// 返回来源场景：从主菜单「调试菜单 → 资产特写观察」跳进来时 SceneLoader 的返回栈里
        /// 就是主菜单（GoBack 弹栈回去）；编辑器直接 Play 本场景则栈空，退而直接跳主菜单
        /// （按 <see cref="SceneLoader.StackDepth"/> 分流，不发空 GoBack）。
        /// </summary>
        public void GoBack()
        {
            SceneLoader loader = SceneLoader.Instance;
            if (loader == null)
            {
                global::PirateCrew.Core.Log.Warn("[AssetInspectController] SceneLoader 服务不在（脱离 Bootstrapper 运行？），无法跳转。");
                return;
            }

            if (loader.StackDepth > 0)
                loader.GoBack();
            else
                loader.ChangeScene(SceneNames.MainMenu);
        }

        /// <summary>重置取景：有特写件按它的包围盒重估，没有回默认档。</summary>
        public void ResetFraming()
        {
            visibleMeters = DefaultVisibleMeters;
            pixelScale = DefaultPixelScale;
            pitchDegrees = DefaultPitchDegrees;
            azimuthDegrees = DefaultAzimuthDegrees;
            framingTarget = new Vector3(0f, 0.5f, 0f);
            if (_subject != null)
                AutoFrame(_subject);
            _cameraDirty = true;
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                showPanel = !showPanel;
            if (Input.GetKeyDown(KeyCode.F12))
                CaptureScreenshot();
            if (Input.GetKeyDown(KeyCode.Escape))
                GoBack();

            if (Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dx) > 0.001f || Mathf.Abs(dy) > 0.001f)
                {
                    azimuthDegrees = Mathf.Repeat(azimuthDegrees + dx * OrbitDegreesPerMousePixel, 360f);
                    pitchDegrees = Mathf.Clamp(pitchDegrees - dy * OrbitDegreesPerMousePixel, 5f, 80f);
                    _cameraDirty = true;
                }
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                visibleMeters = Mathf.Clamp(scroll > 0f ? visibleMeters * 0.9f : visibleMeters / 0.9f, 2f, 200f);
                _cameraDirty = true;
            }
        }

        // ------------------------------------------------------------------
        // IMGUI 面板（调试工具不走游戏 UI 三层：一次性、零资产依赖）
        // ------------------------------------------------------------------

        void OnGUI()
        {
            if (!showPanel)
            {
                if (GUILayout.Button("显示观察面板 (F1)", GUILayout.Width(150f)))
                    showPanel = true;
                return;
            }

            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawPanel, "资产特写观察");
        }

        void DrawPanel(int windowId)
        {
            GUILayout.Label("—— 资产（扫 " + CatalogRoots[0] + " / " + CatalogRoots[1] + "）——");

            GUILayout.BeginHorizontal();
            GUILayout.Label("过滤", GUILayout.Width(36f));
            string nextFilter = GUILayout.TextField(_filter);
            GUILayout.EndHorizontal();
            if (nextFilter != _filter)
            {
                _filter = nextFilter;
#if UNITY_EDITOR
                Refilter();
#endif
            }

            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(150f));
            for (int i = 0; i < _filtered.Count; i++)
            {
                string path = _filtered[i];
                bool current = _current >= 0 && _catalog[_current] == path;
                GUIStyle style = current ? GUI.skin.box : GUI.skin.button;
                if (GUILayout.Button((current ? "▶ " : "") + DisplayOf(path), style))
                    LoadEntry(_catalog.IndexOf(path));
            }
            GUILayout.EndScrollView();
            GUILayout.Label("共 " + _catalog.Count + " 件，命中 " + _filtered.Count + " 件"
                + (_current >= 0 ? "｜当前 " + DisplayOf(_catalog[_current]) : ""));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一件"))
                StepFiltered(-1);
            if (GUILayout.Button("下一件"))
                StepFiltered(1);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("—— 镜头取景（右键拖拽环绕 / 滚轮缩放）——");
            visibleMeters = LabeledSlider("可见米数", visibleMeters, 2f, 200f, ref _cameraDirty, "0.#");

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

            pitchDegrees = LabeledSlider("俯角", pitchDegrees, 5f, 80f, ref _cameraDirty, "0.#");
            azimuthDegrees = LabeledSlider("方位角", azimuthDegrees, 0f, 360f, ref _cameraDirty, "0.#");

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

            GUILayout.Space(6f);
            GUILayout.Label("—— 读数 ——");
            if (rig != null && rig.IsReady && rig.UnitSize > 0f)
            {
                float unit = rig.UnitSize;
                if (_subject != null && _subjectBoundsSize != Vector3.zero)
                    GUILayout.Label($"包围盒 {_subjectBoundsSize.x:0.##} × {_subjectBoundsSize.y:0.##} × {_subjectBoundsSize.z:0.##} m｜高 {_subjectBoundsSize.y / unit:0.#} 艺术像素");
                GUILayout.Label($"每艺术像素 {unit:0.####} m｜可见 {rig.RenderHeight * rig.worldPerPixel:0.#} m（画布 {rig.RenderWidth}×{rig.RenderHeight}）");
            }
            else
            {
                GUILayout.Label($"worldPerPixel {WorldPerPixel(visibleMeters, pixelScale):0.####} m（rig 未就绪，读数待首帧）");
            }

            GUILayout.Space(4f);
            GUILayout.Label(_status, GUI.skin.box);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("截图 (F12)"))
                CaptureScreenshot();
            if (GUILayout.Button("重置取景"))
                ResetFraming();
            if (GUILayout.Button("隐藏 (F1)"))
                showPanel = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            if (GUILayout.Button("返回主菜单 (Esc)"))
                GoBack();

            GUI.DragWindow();
        }

        /// <summary>
        /// 互斥选中项的点击判定（纯函数，CharCamDebugController 同款）：
        /// <paramref name="clicked"/> 是 Toggle 的**返回值**，只有当它不等于该项当前的选中态时
        /// 才代表用户**真的点了它**——否则当前选中项每帧都返回 true，会把刚点下的新值覆盖回去。
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

        /// <summary>清单条目显示名：父目录/文件名（同名件在不同目录可分辨）。</summary>
        static string DisplayOf(string assetPath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(assetPath));
            return string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
        }
    }
}
