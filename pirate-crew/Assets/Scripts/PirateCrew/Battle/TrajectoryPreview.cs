using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="ThrowTrajectory"/>）。
    ///
    /// 【预览规格（投掷行为契约 #9）】可判定行为：
    ///   1. **落点终止**：弧线积分到首次穿地（<see cref="ThrowTrajectory.TryPredictUntilImpact"/>），
    ///      不按飞行时长截断；
    ///   2. **不入地**：写入的全部珠点 y ≥ 地面（穿地点按 y 插值收在地面）；
    ///   3. **落点标记恒显**：兜底步数内穿地必显示标记（旧预览远射程时标记消失）；
    ///   4. **方向可读 + 像素化**：珠点沿弧线按艺术像素量化（3px → 1px 方点），颜色三档；
    ///      落点用 5×5 像素 X 标记。全部件都是世界物体、被单位/地形正常遮挡，
    ///      禁止覆盖式渲染（旧预览线浮在角色上层的病灶）。
    ///
    /// 【视觉形态】像素方点弧线（亮黄→橙→深橙红三档）+ 青色 5×5 像素 X；
    /// 珠点与 X 的中心都吸附到艺术像素网格，读起来是像素游戏的点阵弹道，不是亚像素 3D 曲线。
    /// 读数（方向/仰角/力度）归操作 HUD，不在 3D 场景写字。
    ///
    /// 【遮挡机制（像素化域透明趟接管）】本件材质走 <c>PirateCrew/Pixelart/PixelartTransparent</c>
    /// （本仓透明趟专用路径），由 Cast 渲染器的透明趟（<see cref="global::PirateCrew.Rendering.Pixelart.PixelartTransparentFeature"/>，
    /// 着色合成之后）画进**艺术画布**：与画面同像素化，并由 shader 手动比细像素深度实现
    /// 逐像素遮挡（崖后珠点被 clip 掉）。**PhysX 射线等效补位（occlusionTest/IsOccluded）
    /// 已随之退役**——逐珠点显隐的窄带误差（崖棱半切珠点）由逐像素判定从根上取代；
    /// 珠点不再因遮挡 SetActive(false)，被挡片元在渲染层丢弃。
    ///
    /// 【预览 = 实弹】初速与重力由调用方（<see cref="BattleInteractionController"/>）从
    /// <see cref="StandardThrowRules"/> 取**与执行同源**的值传入；本类不自算任何弹道参数。
    /// </summary>
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        /// <summary>落点标记的抬高（世界单位，防与地面 z-fight）。</summary>
        public const float ImpactMarkerLift = 0.06f;

        /// <summary>落点像素 X 的边长（艺术像素，5×5）。</summary>
        public const int ImpactCrossPixels = 5;

        /// <summary>珠点锥形的末端像素尺寸（起点像素由 dotSize 换算）。</summary>
        public const int DotEndPixels = 1;

        [Tooltip("珠点池大小（弧线最长显示的点数；更长弧线自动拉开点距）。")]
        [SerializeField] int dotPoolSize = 48;

        [Tooltip("珠点基准边长（世界单位）：按 1 艺术像素量化，0.18 在基准档下为 3 px。")]
        [SerializeField] float dotSize = 0.18f;

        [Tooltip("是否显示预测落点标记（像素 X）。")]
        [SerializeField] bool showImpactMarker = true;

        [Tooltip("预制体上遗留的旧轨迹 LineRenderer（两态重构退役件）；Awake 时直接禁用。")]
        [SerializeField] LineRenderer legacyLine;

        Transform _dotsRoot;
        Transform[] _dots;
        Transform _impactCross;
        readonly Vector3[] _samples = new Vector3[StandardThrowRules.PreviewMaxSteps];

        // 真实地形采样器（掉血未生效 ③a）：同一场战斗复用同一 delegate，避免逐帧瞄准时分配。
        HeightfieldGrid _samplerTerrain;
        System.Func<float, float, float> _groundSampler;

        void Awake()
        {
            if (legacyLine == null)
                legacyLine = GetComponent<LineRenderer>();
            if (legacyLine != null)
            {
                legacyLine.enabled = false;   // 旧连续细线随预览重做退役
                if (legacyLine.sharedMaterial == null)
                    legacyLine.sharedMaterial = FallbackPreviewMaterial();
            }

            EnsureDots();
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

            if (!landed)
            {
                // 兜底步数内不穿地（理论不可达的极端平射）：整条隐藏，不出半截线。
                SetDotsActive(0);
                HideImpactGroup();
                return;
            }

            // 珠点沿弧线**均匀铺满**：点距 = 采样数 / 池大小（向上取整 ≥1），弧线长短都完整显示；
            // 尺寸按艺术像素量化（起点 3px → 落点 1px），并且逐点吸附到艺术像素网格，
            // 读起来是像素方点组成的弧线，而不是亚像素的 3D 圆点。
            int step = Mathf.Max(1, (count + dotPoolSize - 1) / dotPoolSize);
            Camera cam = Camera.current != null ? Camera.current : Camera.main;
            int startPixels = Mathf.Clamp(
                Mathf.RoundToInt(dotSize / CameraFraming.WorldPerPixel), 1, 5);

            int shown = 0;
            for (int i = 0; i < count && shown < _dots.Length; i += step)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0f;
                int pixels = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(startPixels, DotEndPixels, t)),
                    DotEndPixels, startPixels);
                float scale = CameraFraming.WorldPerPixel * pixels;
                Transform dot = _dots[shown++];
                dot.position = cam != null ? SnapToPixelGrid(_samples[i], cam) : _samples[i];
                dot.localScale = new Vector3(scale, scale, scale);
                if (cam != null)
                    dot.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                // 被崖/单位挡住的珠点不再逐点显隐——透明趟在 shader 里逐像素比细像素深度，
                // 崖后片元直接 clip（比旧射线判定的"整点隐藏"更细，也免了每珠点一条 PhysX 射线）。
                dot.gameObject.SetActive(true);
            }
            SetDotsActive(shown);

            // 落点标记随珠点一起交给透明趟（"落点标记恒显"约束的是采样兜底；遮挡由渲染层判定）。
            if (showImpactMarker)
                PlaceImpactGroup(impact, cam);
            else
                HideImpactGroup();
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

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            SetDotsActive(0);
            HideImpactGroup();
        }

        // ------------------------------------------------------------------
        // 珠点池（顶点色锥形：颜色按池位渐变烘进各自 Mesh，共享一份白材质）
        // ------------------------------------------------------------------

        /// <summary>关掉前 <paramref name="count"/> 个池位之后的所有珠点（前缀恒显示，遮挡由透明趟 clip）。</summary>
        void SetDotsActive(int count)
        {
            for (int i = count; i < _dots.Length; i++)
            {
                if (_dots[i].gameObject.activeSelf)
                    _dots[i].gameObject.SetActive(false);
            }
        }

        void EnsureDots()
        {
            if (_dots != null)
                return;

            var root = new GameObject("TrajectoryDots");
            root.transform.SetParent(transform, false);
            _dotsRoot = root.transform;

            Material material = legacyLine != null && legacyLine.sharedMaterial != null
                ? legacyLine.sharedMaterial
                : FallbackPreviewMaterial();

            _dots = new Transform[dotPoolSize];
            for (int i = 0; i < dotPoolSize; i++)
            {
                var dot = new GameObject("Dot" + i);
                dot.transform.SetParent(_dotsRoot, false);
                dot.transform.localScale = new Vector3(dotSize, dotSize, dotSize);
                var meshFilter = dot.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = CreateTintedQuad(DotColor(i, dotPoolSize));
                var renderer = dot.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _dots[i] = dot.transform;
                dot.SetActive(false);
            }
        }

        /// <summary>珠点颜色按三档像素色阶：亮黄 → 橙 → 深橙红（不做连续渐变）。</summary>
        static Color32 DotColor(int poolIndex, int poolSize)
        {
            float t = poolSize > 1 ? (float)poolIndex / (poolSize - 1) : 0f;
            if (t < 1f / 3f)
                return new Color32(0xFF, 0xEC, 0x78, 0xE6);
            if (t < 2f / 3f)
                return new Color32(0xFF, 0xB0, 0x3C, 0xE6);
            return new Color32(0xFF, 0x6A, 0x28, 0xE6);
        }

        /// <summary>
        /// 世界坐标在相机平面内吸附到艺术像素网格；深度沿视线保持不变。
        /// 珠点/X 的尺寸都取奇数艺术像素，中心落在像素中心时边缘正好对齐。
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

        /// <summary>带顶点色的单位面片（-0.5..0.5）；透明趟 shader 乘顶点色，材质保持白色。</summary>
        static Mesh CreateTintedQuad(Color32 tint)
        {
            var mesh = new Mesh { name = "PreviewDot" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.triangles = new[] { 0, 3, 1, 0, 2, 3 };
            mesh.colors32 = new[] { tint, tint, tint, tint };
            mesh.RecalculateBounds();
            return mesh;
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
        // 落点标记：5×5 像素 X（面向相机、1px 方块拼成，不做平滑圆环）
        // ------------------------------------------------------------------

        void PlaceImpactGroup(Vector3 impact, Camera cam)
        {
            EnsureImpactGroup();
            if (_impactCross == null)
                return;

            Vector3 position = impact + Vector3.up * ImpactMarkerLift;
            _impactCross.position = cam != null ? SnapToPixelGrid(position, cam) : position;
            if (cam != null)
                _impactCross.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            _impactCross.gameObject.SetActive(true);
        }

        void HideImpactGroup()
        {
            if (_impactCross != null && _impactCross.gameObject.activeSelf)
                _impactCross.gameObject.SetActive(false);
        }

        void EnsureImpactGroup()
        {
            if (_impactCross != null)
                return;

            var go = new GameObject("ImpactCross");
            go.transform.SetParent(transform, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateImpactCrossMesh();

            var renderer = go.AddComponent<MeshRenderer>();
            Material baseMaterial = legacyLine != null
                ? legacyLine.sharedMaterial
                : FallbackPreviewMaterial();
            // 落点 X 是 UI 语义的覆盖标记：关掉透明趟的手动深度，避免贴地时被
            // 块锚深度误裁（珠点仍保留深度遮挡）。
            var crossMaterial = new Material(baseMaterial) { name = "ImpactCross" };
            if (crossMaterial.HasProperty("_DepthTest"))
                crossMaterial.SetFloat("_DepthTest", 0f);
            renderer.sharedMaterial = crossMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            _impactCross = go.transform;
        }

        /// <summary>
        /// 5×5 像素 X：两条对角线各 5 个 1px 方块（中心共用），共 9 个方块。
        /// 网格局部坐标以 X 中心为原点，单位 = 1 艺术像素；颜色走顶点色。
        /// </summary>
        static Mesh CreateImpactCrossMesh()
        {
            Color32 teal = new Color32(0x49, 0xD9, 0xD6, 0xE6);
            var points = new[]
            {
                new Vector2Int(0, 0), new Vector2Int(1, 1), new Vector2Int(2, 2),
                new Vector2Int(3, 3), new Vector2Int(4, 4),
                new Vector2Int(4, 0), new Vector2Int(3, 1), new Vector2Int(1, 3),
                new Vector2Int(0, 4),
            };
            float unit = CameraFraming.WorldPerPixel;
            float half = unit * 0.5f;
            var vertices = new Vector3[points.Length * 4];
            var colors = new Color32[vertices.Length];
            var triangles = new int[points.Length * 6];
            for (int i = 0; i < points.Length; i++)
            {
                float cx = (points[i].x - 2) * unit;
                float cy = (points[i].y - 2) * unit;
                int v = i * 4;
                vertices[v] = new Vector3(cx - half, cy - half, 0f);
                vertices[v + 1] = new Vector3(cx + half, cy - half, 0f);
                vertices[v + 2] = new Vector3(cx + half, cy + half, 0f);
                vertices[v + 3] = new Vector3(cx - half, cy + half, 0f);
                colors[v] = teal;
                colors[v + 1] = teal;
                colors[v + 2] = teal;
                colors[v + 3] = teal;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "ImpactCross" };
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
