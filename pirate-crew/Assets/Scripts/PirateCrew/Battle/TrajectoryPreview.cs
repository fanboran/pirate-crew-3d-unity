using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="ThrowTrajectory"/>）。
    ///
    /// 【预览规格（投掷行为契约 #9）】可判定行为：
    ///   1. **落点终止**：弧线积分到首次穿地（<see cref="ThrowTrajectory.TryPredictUntilImpact"/>），
    ///      不按飞行时长截断；
    ///   2. **不入地**：写入的全部采样点 y ≥ 地面（穿地点按 y 插值收在地面）；
    ///   3. **落点标记恒显**：兜底步数内穿地必显示标记；
    ///   4. **方向可读**：整条弧线连续可见，落点用高对比度准星标出。
    ///
    /// 【视觉形态 — 变体 A「磁轨光带」】与像素点阵弧完全不同的连续光带：
    ///   · **双层轨道**：外层青色宽发光带（6 px，末端收窄）+ 内层亮青窄芯（2 px），
    ///     走 <c>LineRenderer</c>（View 对齐 = 面向相机），沿抛物线连续铺满；
    ///   · **刻度短线**：沿弧线等距铺 14 条 1×5 px 横向刻度，读起来像扫描出来的测距轨；
    ///   · **流动脉冲**：3 颗白色菱形光点沿轨道循环飞行（同源采样步进 = 与实弹同速感），
    ///     末端与起点渐隐，避免循环跳变；
    ///   · **落点准星**：品红虚线环（缓慢自转）+ 四角括号（呼吸缩放）+ 菱形内环 + 中心点；
    ///   · **起点节点**：投掷手位置的青色菱形轮廓，给弧线一个可读的"枪口"。
    /// 全部件都是世界物体、被单位/地形正常遮挡（落点准星除外，见下）。
    ///
    /// 【遮挡机制（像素化域透明趟接管）】本件材质走 <c>PirateCrew/Pixelart/PixelartTransparent</c>
    /// （本仓透明趟专用路径），由 Cast 渲染器的透明趟（<see cref="global::PirateCrew.Rendering.Pixelart.PixelartTransparentFeature"/>，
    /// 着色合成之后）画进**艺术画布**：与画面同像素化，并由 shader 手动比细像素深度实现
    /// 逐像素遮挡（崖后光带被 clip 掉）。落点准星是 UI 语义的覆盖标记，
    /// 材质 `_DepthTest = 0`，贴地时不会被块锚深度误裁。
    ///
    /// 【预览 = 实弹】初速与重力由调用方（<see cref="BattleInteractionController"/>）从
    /// <see cref="StandardThrowRules"/> 取**与执行同源**的值传入；本类不自算任何弹道参数。
    /// </summary>
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        /// <summary>落点标记的抬高（世界单位，防与地面 z-fight）。</summary>
        public const float ImpactMarkerLift = 0.06f;

        // ------------------------------------------------------------------
        // 调参（视觉常量，不影响弹道）
        // ------------------------------------------------------------------

        [Header("磁轨光带")]
        [Tooltip("外层发光带宽（艺术像素）。")]
        [SerializeField] float railGlowPixels = 6f;

        [Tooltip("内层亮芯宽（艺术像素）。")]
        [SerializeField] float railCorePixels = 1.8f;

        [Tooltip("沿弧线铺设的横向刻度条数。")]
        [SerializeField] int tickCount = 14;

        [Tooltip("单条刻度的长度（艺术像素，横向跨过光带）。")]
        [SerializeField] float tickLengthPixels = 5f;

        [Header("流动脉冲")]
        [Tooltip("沿光带循环飞行的菱形光点数。")]
        [SerializeField] int pulseCount = 3;

        [Tooltip("脉冲沿整条弧线循环一次的频率（次/秒）。")]
        [SerializeField] float pulseTravelsPerSecond = 0.55f;

        [Tooltip("脉冲菱形边长（艺术像素）。")]
        [SerializeField] float pulseSizePixels = 4f;

        [Header("落点准星")]
        [Tooltip("是否显示预测落点准星。")]
        [SerializeField] bool showImpactMarker = true;

        [Tooltip("准星虚线环半径（艺术像素）。")]
        [SerializeField] float reticleRadiusPixels = 11f;

        [Tooltip("预制体上遗留的旧轨迹 LineRenderer（两态重构退役件）；Awake 时直接禁用。")]
        [SerializeField] LineRenderer legacyLine;

        // ------------------------------------------------------------------
        // 运行时视觉件
        // ------------------------------------------------------------------

        Material _baseMaterial;

        LineRenderer _railGlow;
        LineRenderer _railCore;

        Mesh _tickMesh;
        MeshFilter _tickFilter;
        Vector3[] _tickVertices;
        Color32[] _tickColors;
        int[] _tickTriangles;

        Transform[] _pulses;
        Material _pulseMaterial;

        Transform _originNode;
        Material _originMaterial;

        Transform _reticle;
        Transform _reticleRing;
        Transform _reticleBrackets;
        Material _ringMaterial;
        Material _bracketMaterial;

        Camera _camera;
        readonly Vector3[] _samples = new Vector3[StandardThrowRules.PreviewMaxSteps];
        int _sampleCount;
        bool _visible;

        static readonly Color32 kGlowStart = new Color32(0x1F, 0xA8, 0xC8, 0x52);
        static readonly Color32 kGlowEnd = new Color32(0x1F, 0xA8, 0xC8, 0x1E);
        static readonly Color32 kCoreStart = new Color32(0x9C, 0xF6, 0xFF, 0xF2);
        static readonly Color32 kCoreEnd = new Color32(0x5F, 0xD8, 0xEE, 0x8C);
        static readonly Color32 kTickColor = new Color32(0x0E, 0x7C, 0x99, 0xD9);
        static readonly Color32 kCyan = new Color32(0x9C, 0xF6, 0xFF, 0xE6);
        static readonly Color32 kMagenta = new Color32(0xFF, 0x4F, 0xA3, 0xF2);
        static readonly Color32 kMagentaBright = new Color32(0xFF, 0xD1, 0xE8, 0xFF);

        void Awake()
        {
            if (legacyLine == null)
                legacyLine = GetComponent<LineRenderer>();
            if (legacyLine != null)
                legacyLine.enabled = false;   // 旧连续细线随预览重做退役

            _camera = Camera.main;
            EnsureVisuals();
        }

        /// <summary>
        /// 刷新轨迹。初速/重力与执行路径同源（<see cref="StandardThrowRules"/>，由调用方传入）——
        /// 「预览 = 实弹」的换算不在本类内发生第二次。
        /// </summary>
        /// <param name="originWorld">投掷起点（= 单位枢轴 + ThrowOriginHeight）。</param>
        /// <param name="velocity">完整初速向量（米制）。</param>
        /// <param name="terrain">本局地形（<see cref="BattleController.Terrain"/>）；null = 平地口径。</param>
        public void Show(Vector3 originWorld, Vector3 velocity, HeightfieldGrid terrain = null)
        {
            bool landed = ThrowTrajectory.TryPredictUntilImpact(
                originWorld, velocity, StandardThrowRules.LaunchGravityY,
                _samples, out int count, out Vector3 impact,
                groundSampler: ResolveGroundSampler(terrain));

            // 兜底步数内不穿地（理论不可达的极端平射）：整条隐藏，不出半截线。
            if (!landed || count < 2)
            {
                HideVisuals();
                return;
            }

            Camera cam = Camera.main != null ? Camera.main : _camera;
            _camera = cam;
            _sampleCount = count;
            _visible = true;

            BuildRail(count);
            RebuildTicks(cam, count);

            if (showImpactMarker)
                PlaceReticle(impact, cam);
            else if (_reticle != null)
                _reticle.gameObject.SetActive(false);

            PlaceBillboard(_originNode, originWorld, cam);
            if (_originNode != null)
                _originNode.gameObject.SetActive(true);

            for (int i = 0; i < _pulses.Length; i++)
                _pulses[i].gameObject.SetActive(cam != null);

            // 立即摆一次脉冲/准星姿态，避免首帧全部堆在原点。
            Animate(Time.time);
        }

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            HideVisuals();
        }

        void HideVisuals()
        {
            _visible = false;
            _sampleCount = 0;

            if (_railGlow != null)
            {
                _railGlow.positionCount = 0;
                _railGlow.gameObject.SetActive(false);
            }
            if (_railCore != null)
            {
                _railCore.positionCount = 0;
                _railCore.gameObject.SetActive(false);
            }
            if (_tickFilter != null)
                _tickFilter.gameObject.SetActive(false);
            if (_reticle != null)
                _reticle.gameObject.SetActive(false);
            if (_originNode != null)
                _originNode.gameObject.SetActive(false);
            if (_pulses != null)
            {
                for (int i = 0; i < _pulses.Length; i++)
                {
                    if (_pulses[i] != null && _pulses[i].gameObject.activeSelf)
                        _pulses[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 按地形取地表采样器；地形引用变化时才重建 delegate（战斗内每帧调用不分配）。
        /// 水格由 <see cref="HeightfieldGrid.SurfaceWorldYAtWorld"/> 返回
        /// <see cref="HeightfieldGrid.WaterVoidY"/>，预览据此表现落水。
        /// </summary>
        System.Func<float, float, float> ResolveGroundSampler(HeightfieldGrid terrain)
        {
            if (terrain == null)
                return null;

            if (!ReferenceEquals(_samplerTerrain, terrain))
            {
                _samplerTerrain = terrain;
                _groundSampler = terrain.SurfaceWorldYAtWorld;
            }
            return _groundSampler;
        }

        HeightfieldGrid _samplerTerrain;
        System.Func<float, float, float> _groundSampler;

        void Update()
        {
            if (!_visible || _sampleCount < 2)
                return;

            Camera cam = Camera.main != null ? Camera.main : _camera;
            if (cam == null)
                return;
            _camera = cam;
            Animate(Time.time);
        }

        // ------------------------------------------------------------------
        // 每帧动画：流动脉冲 / 准星呼吸与自转
        // ------------------------------------------------------------------

        void Animate(float time)
        {
            Camera cam = _camera;
            if (cam == null)
                return;

            float u = CameraFraming.WorldPerPixel;
            Quaternion billboard = cam.transform.rotation;

            // 脉冲：按**采样索引**推进（采样 = 等时间步，因此走速与实弹同源）；
            // 端点缩放渐隐，循环回起点时不出现瞬移跳变。
            if (_pulses != null)
            {
                int n = Mathf.Max(1, pulseCount);
                for (int i = 0; i < _pulses.Length; i++)
                {
                    float t = Mathf.Repeat(time * pulseTravelsPerSecond + (float)i / n, 1f);
                    Vector3 p = SnapToPixelGrid(SampleAt(t), cam);
                    float edgeFade = Mathf.Clamp01(Mathf.Min(t, 1f - t) * 10f);
                    float breathe = 0.85f + 0.15f * Mathf.Sin(time * 11f + i * 2.1f);
                    float size = pulseSizePixels * u * breathe * (0.35f + 0.65f * edgeFade);

                    Transform pulse = _pulses[i];
                    pulse.position = p;
                    pulse.rotation = billboard;
                    pulse.localScale = new Vector3(size, size, 1f);
                }
                if (_pulseMaterial != null)
                {
                    float a = 0.78f + 0.22f * Mathf.Sin(time * 8f);
                    _pulseMaterial.color = new Color(1f, 1f, 1f, a);
                }
            }

            // 准星：虚线环自转 + 四角括号呼吸缩放 + 透明度呼吸。
            if (_reticle != null && _reticle.gameObject.activeSelf)
            {
                if (_reticleRing != null)
                    _reticleRing.localRotation = Quaternion.AngleAxis(time * 32f, Vector3.forward);

                float breathe = 1f + 0.06f * Mathf.Sin(time * 4.2f);
                if (_reticleBrackets != null)
                    _reticleBrackets.localScale = new Vector3(breathe, breathe, 1f);

                if (_bracketMaterial != null)
                {
                    float a = 0.70f + 0.30f * (0.5f + 0.5f * Mathf.Sin(time * 4.2f));
                    _bracketMaterial.color = new Color(1f, 1f, 1f, a);
                }
                if (_ringMaterial != null)
                    _ringMaterial.color = new Color(1f, 1f, 1f, 0.92f);
            }

            // 起点节点：轻微呼吸。
            if (_originNode != null && _originNode.gameObject.activeSelf)
            {
                float s = 1f + 0.10f * Mathf.Sin(time * 5.5f);
                _originNode.localScale = new Vector3(s, s, 1f);
                if (_originMaterial != null)
                {
                    float a = 0.72f + 0.28f * (0.5f + 0.5f * Mathf.Sin(time * 5.5f));
                    _originMaterial.color = new Color(1f, 1f, 1f, a);
                }
            }
        }

        /// <summary>按归一化弧长索引取样点（线性插值，t ∈ [0,1]）。</summary>
        Vector3 SampleAt(float t)
        {
            int last = _sampleCount - 1;
            float f = Mathf.Clamp01(t) * last;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, last);
            int j = Mathf.Min(i + 1, last);
            return Vector3.Lerp(_samples[i], _samples[j], f - i);
        }

        // ------------------------------------------------------------------
        // 双层光带
        // ------------------------------------------------------------------

        void BuildRail(int count)
        {
            _railGlow.positionCount = count;
            _railCore.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = _samples[i];
                _railGlow.SetPosition(i, p);
                _railCore.SetPosition(i, p);
            }
            _railGlow.gameObject.SetActive(true);
            _railCore.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------
        // 等距刻度条（单 mesh，逐帧更新顶点）
        // ------------------------------------------------------------------

        void RebuildTicks(Camera cam, int count)
        {
            int n = Mathf.Clamp(tickCount, 0, _tickVertices.Length / 4);
            if (cam == null || n <= 0)
            {
                _tickFilter.gameObject.SetActive(false);
                return;
            }

            Vector3 forward = cam.transform.forward;
            float u = CameraFraming.WorldPerPixel;
            float halfLen = tickLengthPixels * 0.5f * u;
            float halfThick = 0.5f * u;
            int v = 0;

            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n;
                int i = Mathf.Clamp(Mathf.RoundToInt(t * (count - 1)), 0, count - 1);
                int i0 = Mathf.Max(0, i - 1);
                int i1 = Mathf.Min(count - 1, i + 1);

                Vector3 tangent = _samples[i1] - _samples[i0];
                tangent -= forward * Vector3.Dot(tangent, forward);
                if (tangent.sqrMagnitude < 1e-8f)
                    continue;
                tangent.Normalize();

                Vector3 side = Vector3.Cross(forward, tangent).normalized;
                Vector3 center = SnapToPixelGrid(_samples[i], cam);
                Vector3 a = side * halfLen;
                Vector3 b = tangent * halfThick;

                _tickVertices[v] = transform.InverseTransformPoint(center + a + b);
                _tickVertices[v + 1] = transform.InverseTransformPoint(center + a - b);
                _tickVertices[v + 2] = transform.InverseTransformPoint(center - a - b);
                _tickVertices[v + 3] = transform.InverseTransformPoint(center - a + b);
                v += 4;
            }

            if (v == 0)
            {
                _tickFilter.gameObject.SetActive(false);
                return;
            }

            _tickMesh.Clear();
            _tickMesh.SetVertices(_tickVertices, 0, v);
            _tickMesh.SetColors(_tickColors, 0, v);
            _tickMesh.SetTriangles(_tickTriangles, 0, v / 4 * 6, 0, true);
            _tickFilter.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------
        // 落点 / 起点物件
        // ------------------------------------------------------------------

        void PlaceReticle(Vector3 impact, Camera cam)
        {
            if (_reticle == null)
                return;
            Vector3 position = impact + Vector3.up * ImpactMarkerLift;
            _reticle.position = cam != null ? SnapToPixelGrid(position, cam) : position;
            _reticle.rotation = cam != null ? cam.transform.rotation : Quaternion.identity;
            _reticle.gameObject.SetActive(true);
        }

        static void PlaceBillboard(Transform target, Vector3 world, Camera cam)
        {
            if (target == null)
                return;
            target.position = cam != null ? SnapToPixelGrid(world, cam) : world;
            target.rotation = cam != null ? cam.transform.rotation : Quaternion.identity;
        }

        /// <summary>
        /// 世界坐标在相机平面内吸附到艺术像素网格；深度沿视线保持不变。
        /// </summary>
        static Vector3 SnapToPixelGrid(Vector3 world, Camera cam)
        {
            Transform t = cam.transform;
            Vector3 right = t.right;
            Vector3 up = t.up;
            Vector3 forward = t.forward;
            Vector3 origin = t.position;
            Vector3 local = world - origin;
            float unit = CameraFraming.WorldPerPixel;
            float x = Mathf.Round(Vector3.Dot(local, right) / unit) * unit;
            float y = Mathf.Round(Vector3.Dot(local, up) / unit) * unit;
            float z = Vector3.Dot(local, forward);
            return origin + right * x + up * y + forward * z;
        }

        // ------------------------------------------------------------------
        // 装配（懒建一次，之后只改 transform / 顶点）
        // ------------------------------------------------------------------

        void EnsureVisuals()
        {
            if (_railGlow != null)
                return;

            _baseMaterial = legacyLine != null && legacyLine.sharedMaterial != null
                ? legacyLine.sharedMaterial
                : FallbackPreviewMaterial();

            _railGlow = MakeRail("TrajectoryRailGlow",
                EffectMaterial(_baseMaterial, "TrajectoryRailGlow", depthTest: true),
                railGlowPixels,
                new AnimationCurve(
                    new Keyframe(0f, 1.25f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.32f)),
                MakeGradient(kGlowStart, kGlowEnd));

            _railCore = MakeRail("TrajectoryRailCore",
                EffectMaterial(_baseMaterial, "TrajectoryRailCore", depthTest: true),
                railCorePixels,
                new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 0.55f)),
                MakeGradient(kCoreStart, kCoreEnd));

            EnsureTicks();
            EnsurePulses();
            EnsureOrigin();
            EnsureReticle();
        }

        LineRenderer MakeRail(string name, Material material, float widthPixels,
            AnimationCurve widthCurve, Gradient gradient)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.widthMultiplier = widthPixels * CameraFraming.WorldPerPixel;
            line.widthCurve = widthCurve;
            line.colorGradient = gradient;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = false;
            line.positionCount = 0;
            go.SetActive(false);
            return line;
        }

        void EnsureTicks()
        {
            int maxQuads = Mathf.Max(1, tickCount);
            _tickVertices = new Vector3[maxQuads * 4];
            _tickColors = new Color32[maxQuads * 4];
            _tickTriangles = new int[maxQuads * 6];
            for (int i = 0; i < _tickColors.Length; i++)
                _tickColors[i] = kTickColor;
            for (int q = 0; q < maxQuads; q++)
            {
                int v = q * 4;
                int t = q * 6;
                _tickTriangles[t] = v;
                _tickTriangles[t + 1] = v + 1;
                _tickTriangles[t + 2] = v + 2;
                _tickTriangles[t + 3] = v;
                _tickTriangles[t + 4] = v + 2;
                _tickTriangles[t + 5] = v + 3;
            }

            var go = new GameObject("TrajectoryTicks");
            go.transform.SetParent(transform, false);
            _tickMesh = new Mesh { name = "TrajectoryTicks" };
            _tickMesh.MarkDynamic();
            _tickFilter = go.AddComponent<MeshFilter>();
            _tickFilter.sharedMesh = _tickMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EffectMaterial(_baseMaterial, "TrajectoryTick", depthTest: true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
        }

        void EnsurePulses()
        {
            int n = Mathf.Max(1, pulseCount);
            _pulses = new Transform[n];
            _pulseMaterial = EffectMaterial(_baseMaterial, "TrajectoryPulse", depthTest: true);
            Mesh diamond = CreateDiamondMesh();

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("TrajectoryPulse" + i);
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = diamond;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _pulseMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                _pulses[i] = go.transform;
            }
        }

        void EnsureOrigin()
        {
            var go = new GameObject("TrajectoryOrigin");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateDiamondOutlineMesh(4f, 1f, kCyan);
            var renderer = go.AddComponent<MeshRenderer>();
            _originMaterial = EffectMaterial(_baseMaterial, "TrajectoryOrigin", depthTest: true);
            renderer.sharedMaterial = _originMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            _originNode = go.transform;
        }

        void EnsureReticle()
        {
            var root = new GameObject("TrajectoryReticle");
            root.transform.SetParent(transform, false);

            _ringMaterial = EffectMaterial(_baseMaterial, "TrajectoryReticleRing", depthTest: false);
            _bracketMaterial = EffectMaterial(_baseMaterial, "TrajectoryReticleBrackets", depthTest: false);

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(root.transform, false);
            var ringFilter = ringGo.AddComponent<MeshFilter>();
            ringFilter.sharedMesh = CreateReticleRingMesh(reticleRadiusPixels);
            var ringRenderer = ringGo.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = _ringMaterial;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            _reticleRing = ringGo.transform;

            var bracketGo = new GameObject("Brackets");
            bracketGo.transform.SetParent(root.transform, false);
            var bracketFilter = bracketGo.AddComponent<MeshFilter>();
            bracketFilter.sharedMesh = CreateReticleBracketsMesh();
            var bracketRenderer = bracketGo.AddComponent<MeshRenderer>();
            bracketRenderer.sharedMaterial = _bracketMaterial;
            bracketRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bracketRenderer.receiveShadows = false;
            _reticleBrackets = bracketGo.transform;

            root.SetActive(false);
            _reticle = root.transform;
        }

        Material EffectMaterial(Material source, string name, bool depthTest)
        {
            var material = new Material(source) { name = name };
            material.color = Color.white;
            if (material.HasProperty("_DepthTest"))
                material.SetFloat("_DepthTest", depthTest ? 1f : 0f);
            return material;
        }

        static Gradient MakeGradient(Color32 start, Color32 end)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[]
                {
                    new GradientAlphaKey(start.a / 255f, 0f),
                    new GradientAlphaKey(end.a / 255f, 1f),
                });
            return gradient;
        }

        static Material FallbackPreviewMaterial()
        {
            // 无序列化材质时的兜底（正常装配链都带 Trajectory.mat）；着色全靠顶点色，材质保白。
            // 本工程透明路径 shader 走 PixelartShaders 取用口（Resources 持有，防播放器剥离）；
            // 极端情况（shader 丢失）退 Sprites/Default——画面颗粒不对但至少可见，Find 口会响亮报错。
            var shader = global::PirateCrew.Rendering.Pixelart.PixelartShaders.Load(
                global::PirateCrew.Rendering.Pixelart.PixelartPath.TransparentShaderName);
            if (shader == null)
                shader = global::PirateCrew.Rendering.Pixelart.PixelartShaders.Find("Sprites/Default");
            var material = shader != null ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
            material.color = Color.white;
            return material;
        }

        // ------------------------------------------------------------------
        // 网格构造（局部单位 = 艺术像素；单位菱形的缩放由 transform 负责）
        // ------------------------------------------------------------------

        /// <summary>单位菱形（半边长 0.5，便于用 localScale = 像素尺寸 × WorldPerPixel）。</summary>
        static Mesh CreateDiamondMesh()
        {
            var mesh = new Mesh { name = "TrajectoryPulse" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0f, -0.5f, 0f),
            };
            mesh.colors32 = new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>空心菱形轮廓（起点节点）：四条 1 px 边。</summary>
        static Mesh CreateDiamondOutlineMesh(float radiusPixels, float thicknessPixels, Color32 color)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color32>();
            var triangles = new System.Collections.Generic.List<int>();
            var corners = new[]
            {
                new Vector2(0f, radiusPixels),
                new Vector2(radiusPixels, 0f),
                new Vector2(0f, -radiusPixels),
                new Vector2(-radiusPixels, 0f),
            };
            for (int i = 0; i < 4; i++)
                AddSegment(vertices, colors, triangles, corners[i], corners[(i + 1) % 4], thicknessPixels, color);
            return Finish(vertices, colors, triangles, "TrajectoryOrigin");
        }

        /// <summary>落点虚线环：8 段弧、1 px 粗，缓慢自转时读得出环在转。</summary>
        static Mesh CreateReticleRingMesh(float radiusPixels)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color32>();
            var triangles = new System.Collections.Generic.List<int>();

            const int dashes = 8;
            float radius = Mathf.Max(2f, radiusPixels);
            float halfArc = 2f / radius;   // 每段弧长 ≈ 4 px
            for (int i = 0; i < dashes; i++)
            {
                float center = i * (Mathf.PI * 2f / dashes);
                float a0 = center - halfArc;
                float a1 = center + halfArc;
                var p0 = new Vector2(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
                var p1 = new Vector2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                AddSegment(vertices, colors, triangles, p0, p1, 1f, kMagenta);
            }
            return Finish(vertices, colors, triangles, "TrajectoryReticleRing");
        }

        /// <summary>落点支架：四角括号 + 内层菱形环 + 中心亮点（与环分开自转/呼吸）。</summary>
        static Mesh CreateReticleBracketsMesh()
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color32>();
            var triangles = new System.Collections.Generic.List<int>();

            // 四角括号：角点 (15,15) 向内伸 6 px 的两条边。
            const float corner = 15f;
            const float arm = 6f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    var c = new Vector2(sx * corner, sy * corner);
                    AddSegment(vertices, colors, triangles, c, new Vector2(sx * (corner - arm), sy * corner), 1f, kMagenta);
                    AddSegment(vertices, colors, triangles, c, new Vector2(sx * corner, sy * (corner - arm)), 1f, kMagenta);
                }
            }

            // 内层菱形环（半对角线 6 px）。
            var diamond = new[]
            {
                new Vector2(0f, 6f), new Vector2(6f, 0f), new Vector2(0f, -6f), new Vector2(-6f, 0f),
            };
            for (int i = 0; i < 4; i++)
                AddSegment(vertices, colors, triangles, diamond[i], diamond[(i + 1) % 4], 1f, kMagenta);

            // 中心 2×2 亮点。
            AddQuad(vertices, colors, triangles,
                new Vector2(-1f, -1f), new Vector2(1f, -1f),
                new Vector2(1f, 1f), new Vector2(-1f, 1f), kMagentaBright);

            return Finish(vertices, colors, triangles, "TrajectoryReticleBrackets");
        }

        /// <summary>把线段展开成 thickness 宽的四边形（局部单位 = 艺术像素）。</summary>
        static void AddSegment(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Color32> colors,
            System.Collections.Generic.List<int> triangles,
            Vector2 from, Vector2 to, float thickness, Color32 color)
        {
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 1e-8f)
                return;
            dir.Normalize();
            var normal = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
            AddQuad(vertices, colors, triangles, from - normal, to - normal, to + normal, from + normal, color);
        }

        static void AddQuad(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Color32> colors,
            System.Collections.Generic.List<int> triangles,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            int v = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(v);
            triangles.Add(v + 1);
            triangles.Add(v + 2);
            triangles.Add(v);
            triangles.Add(v + 2);
            triangles.Add(v + 3);
        }

        /// <summary>像素单位 → 世界单位（局部空间），并落成 Mesh。</summary>
        static Mesh Finish(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Color32> colors,
            System.Collections.Generic.List<int> triangles, string name)
        {
            float u = CameraFraming.WorldPerPixel;
            var world = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
                world[i] = vertices[i] * u;

            var mesh = new Mesh { name = name };
            mesh.vertices = world;
            mesh.colors32 = colors.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
