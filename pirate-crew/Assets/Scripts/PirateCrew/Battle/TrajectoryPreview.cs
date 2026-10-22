using System.Collections.Generic;
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
    /// 【视觉形态 — 变体 A「磁轨光带」· 像素块状版】
    ///   所有视觉元素都先换算到**相机平面艺术像素网格**（1 格 = <see cref="CameraFraming.WorldPerPixel"/>，
    ///   1080p 下 = 2 屏幕像素），再用 1×1 艺术像素的小方块拼出来——没有 LineRenderer、
    ///   没有连续丝带、没有任意角度的斜边，边缘与场景同颗粒：
    ///   · **光带**：1 px 亮青核心 + 1 px 半透明青色晕边，沿弧线逐像素光栅化（阶梯状）；
    ///   · **刻度**：每 10 art px 一条 1×3 px 深青横档，读成扫描测距轨；
    ///   · **脉冲**：3 颗 3×3 px 菱形像素光点沿弧线逐像素移动；
    ///   · **落点准星**：17×17 px 块状像素准星（四角括号 + 两帧交替的虚线菱形环 + 中心十字）；
    ///   · **起手**：角色根部（transform.position）放 5×5 px 菱形锚点，并用 1 px 虚线引线
    ///     连到实际出手点（手部）——视觉从脚下起弧，物理弹道仍严格从手部起算。
    ///   起手锚点/引线是 UI 语义的覆盖标记（`_DepthTest = 0`），保证不被角色本体盖住；
    ///   光带本体仍吃逐像素深度遮挡。
    ///
    /// 【遮挡机制（像素化域透明趟接管）】本件材质走 <c>PirateCrew/Pixelart/PixelartTransparent</c>
    /// （本仓透明趟专用路径），由 Cast 渲染器的透明趟（<see cref="global::PirateCrew.Rendering.Pixelart.PixelartTransparentFeature"/>，
    /// 着色合成之后）画进**艺术画布**：与画面同像素化，并由 shader 手动比细像素深度实现
    /// 逐像素遮挡（崖后光带被 clip 掉）。
    ///
    /// 【预览 = 实弹】初速与重力由调用方（<see cref="BattleInteractionController"/>）从
    /// <see cref="StandardThrowRules"/> 取**与执行同源**的值传入；本类不自算任何弹道参数。
    /// 视觉下移到根部只画“根部→出手点”的引线，不改采样起点、不改落点。
    /// </summary>
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        /// <summary>落点标记的抬高（世界单位，防与地面 z-fight）。</summary>
        public const float ImpactMarkerLift = 0.06f;

        // ------------------------------------------------------------------
        // 调参（长度单位一律 = 艺术像素，1 art px = CameraFraming.WorldPerPixel）
        // ------------------------------------------------------------------

        [Header("光带（单位：艺术像素）")]
        [Tooltip("亮芯宽度（艺术像素，1 = 单像素线）。")]
        [SerializeField] int railCorePixels = 1;

        [Tooltip("亮芯两侧的晕边宽度（艺术像素）。")]
        [SerializeField] int railHaloPixels = 1;

        [Tooltip("刻度间隔（沿弧线的艺术像素数）。")]
        [SerializeField] int tickEveryPixels = 10;

        [Tooltip("刻度长度（艺术像素）。")]
        [SerializeField] int tickLengthPixels = 3;

        [Header("流动脉冲（单位：艺术像素）")]
        [Tooltip("沿光带循环飞行的像素光点数。")]
        [SerializeField] int pulseCount = 3;

        [Tooltip("脉冲沿整条弧线循环一次的频率（次/秒）。")]
        [SerializeField] float pulseTravelsPerSecond = 0.55f;

        [Tooltip("脉冲菱形边长（艺术像素，奇数）。")]
        [SerializeField] int pulseSizePixels = 3;

        [Header("落点准星（单位：艺术像素）")]
        [Tooltip("是否显示预测落点准星。")]
        [SerializeField] bool showImpactMarker = true;

        [Tooltip("准星外框半径（艺术像素，17×17 对应 8）。")]
        [SerializeField] int reticleRadiusPixels = 8;

        [Tooltip("预制体上遗留的旧轨迹 LineRenderer（两态重构退役件）；Awake 时直接禁用。")]
        [SerializeField] LineRenderer legacyLine;

        // ------------------------------------------------------------------
        // 像素格累积（键 = (x,y)，value 保留最高优先级的颜色）
        // ------------------------------------------------------------------

        struct PixelCell
        {
            public int Priority;
            public Vector3 Center;
            public Color32 Color;
        }

        struct PixelPoint
        {
            public int X;
            public int Y;
            public float Z;
        }

        const int PriorityHalo = 0;
        const int PriorityCore = 1;
        const int PriorityTick = 2;

        readonly Dictionary<long, PixelCell> _cells = new Dictionary<long, PixelCell>(4096);
        readonly List<PixelCell> _cellList = new List<PixelCell>(4096);

        Mesh _arcMesh;
        MeshFilter _arcFilter;
        Vector3[] _vertices;
        Color32[] _colors;
        int[] _triangles;

        Mesh _originMesh;
        MeshFilter _originFilter;

        Transform[] _pulses;
        Material _pulseMaterial;

        Transform _reticle;
        Transform _ringA;
        Transform _ringB;
        Transform _brackets;

        Camera _camera;
        readonly Vector3[] _samples = new Vector3[StandardThrowRules.PreviewMaxSteps];
        int _sampleCount;
        bool _visible;
        Vector3 _lastImpact;
        bool _hasImpact;

        // 重建时的相机基（光栅化与方块落点共用）
        Vector3 _camPos;
        Vector3 _camRight;
        Vector3 _camUp;
        Vector3 _camForward;
        float _unit;

        // 重建签名（同一次瞄准内相机不动、参数不变时跳过重建）
        bool _hasSignature;
        Vector3 _sigOrigin;
        Vector3 _sigVelocity;
        Vector3 _sigCamPos;
        Quaternion _sigCamRot;
        HeightfieldGrid _sigTerrain;

        HeightfieldGrid _samplerTerrain;
        System.Func<float, float, float> _groundSampler;

        static readonly Color32 kCoreNear = new Color32(0xB8, 0xFA, 0xFF, 0xF5);
        static readonly Color32 kCoreMid = new Color32(0x6E, 0xE8, 0xF5, 0xEC);
        static readonly Color32 kCoreFar = new Color32(0x2F, 0xB8, 0xD8, 0xE0);
        static readonly Color32 kTick = new Color32(0x06, 0x5A, 0x70, 0xE6);
        static readonly Color32 kCyan = new Color32(0x9C, 0xF6, 0xFF, 0xE6);
        static readonly Color32 kCyanDim = new Color32(0x6E, 0xE8, 0xF5, 0x8C);
        static readonly Color32 kPulse = new Color32(0xFF, 0xFF, 0xFF, 0xF5);
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
        /// 注意：<paramref name="originWorld"/> 仍是**实际出手点**（手部），视觉下移到根部
        /// 只体现在“根部锚点 + 虚线引线”，采样/落点分毫不动。
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
            if (cam == null)
            {
                HideVisuals();
                return;
            }
            _camera = cam;
            _sampleCount = count;
            _visible = true;
            _lastImpact = impact;
            _hasImpact = true;

            bool dirty = !_hasSignature
                || _sigOrigin != originWorld
                || _sigVelocity != velocity
                || !ReferenceEquals(_sigTerrain, terrain)
                || _sigCamPos != cam.transform.position
                || _sigCamRot != cam.transform.rotation;

            if (dirty)
            {
                CacheCamera(cam);
                RebuildArc(originWorld, count);
                RebuildOrigin(originWorld);
                _sigOrigin = originWorld;
                _sigVelocity = velocity;
                _sigTerrain = terrain;
                _sigCamPos = cam.transform.position;
                _sigCamRot = cam.transform.rotation;
                _hasSignature = true;
            }

            _arcFilter.gameObject.SetActive(true);
            _originFilter.gameObject.SetActive(true);

            if (showImpactMarker)
                PlaceReticle(impact, cam);
            else if (_reticle != null)
                _reticle.gameObject.SetActive(false);

            for (int i = 0; i < _pulses.Length; i++)
                _pulses[i].gameObject.SetActive(true);

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
            _hasSignature = false;
            _hasImpact = false;

            if (_arcFilter != null)
                _arcFilter.gameObject.SetActive(false);
            if (_originFilter != null)
                _originFilter.gameObject.SetActive(false);
            if (_reticle != null)
                _reticle.gameObject.SetActive(false);
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

        void LateUpdate()
        {
            if (!_visible || _sampleCount < 2)
                return;

            Camera cam = Camera.main != null ? Camera.main : _camera;
            if (cam == null)
                return;
            _camera = cam;

            // 相机在 Show 之后才被相机驱动摆位（抓图脚本/后续震屏）时，像素格必须按新相机重建，
            // 否则 1px 方块会对不上屏幕像素格。
            if (_sigCamPos != cam.transform.position || _sigCamRot != cam.transform.rotation)
            {
                CacheCamera(cam);
                RebuildArc(_sigOrigin, _sampleCount);
                RebuildOrigin(_sigOrigin);
                _sigCamPos = cam.transform.position;
                _sigCamRot = cam.transform.rotation;
                if (showImpactMarker && _hasImpact)
                    PlaceReticle(_lastImpact, cam);
            }

            Animate(Time.time);
        }

        // ------------------------------------------------------------------
        // 每帧动画：像素脉冲游走 + 准星帧交替
        // ------------------------------------------------------------------

        void Animate(float time)
        {
            Camera cam = _camera;
            if (cam == null)
                return;

            // 脉冲：按采样索引推进（采样 = 等时间步，走速与实弹同源）；
            // 位置吸附到艺术像素格，端点缩放不变（避免亚像素边），只做透明度呼吸。
            if (_pulses != null)
            {
                int n = Mathf.Max(1, pulseCount);
                for (int i = 0; i < _pulses.Length; i++)
                {
                    float t = Mathf.Repeat(time * pulseTravelsPerSecond + (float)i / n, 1f);
                    Vector3 p = SnapToPixelGrid(SampleAt(t), cam);
                    _pulses[i].position = p;
                    _pulses[i].rotation = cam.transform.rotation;
                }
                if (_pulseMaterial != null)
                {
                    float a = 0.80f + 0.20f * Mathf.Sin(time * 8f);
                    _pulseMaterial.color = new Color(1f, 1f, 1f, a);
                }
            }

            // 准星：两帧虚线环交替（像素域的“走马灯”，不做平滑旋转/缩放）。
            if (_reticle != null && _reticle.gameObject.activeSelf)
            {
                bool frameA = (Mathf.FloorToInt(time * 7f) & 1) == 0;
                if (_ringA != null && _ringA.gameObject.activeSelf != frameA)
                    _ringA.gameObject.SetActive(frameA);
                if (_ringB != null && _ringB.gameObject.activeSelf != !frameA)
                    _ringB.gameObject.SetActive(!frameA);
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
        // 相机平面像素网格
        // ------------------------------------------------------------------

        void CacheCamera(Camera cam)
        {
            Transform t = cam.transform;
            _camPos = t.position;
            _camRight = t.right;
            _camUp = t.up;
            _camForward = t.forward;
            _unit = CameraFraming.WorldPerPixel;
        }

        PixelPoint ToPixel(Vector3 world)
        {
            Vector3 local = world - _camPos;
            return new PixelPoint
            {
                X = Mathf.RoundToInt(Vector3.Dot(local, _camRight) / _unit),
                Y = Mathf.RoundToInt(Vector3.Dot(local, _camUp) / _unit),
                Z = Vector3.Dot(local, _camForward),
            };
        }

        Vector3 CellCenter(int x, int y, float z)
        {
            return _camPos
                + _camRight * (x * _unit)
                + _camUp * (y * _unit)
                + _camForward * z;
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
        // 光带光栅化（全部格子 = 1 艺术像素）
        // ------------------------------------------------------------------

        void RebuildArc(Vector3 originWorld, int count)
        {
            _cells.Clear();

            float sinceTick = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                float ta = (float)i / (count - 1);
                float tb = (float)(i + 1) / (count - 1);
                RasterizeArcSegment(_samples[i], _samples[i + 1], ta, tb, ref sinceTick);
            }

            BuildArcMesh();
        }

        void RasterizeArcSegment(Vector3 a, Vector3 b, float ta, float tb, ref float sinceTick)
        {
            PixelPoint p0 = ToPixel(a);
            PixelPoint p1 = ToPixel(b);

            int dx = p1.X - p0.X;
            int dy = p1.Y - p0.Y;
            int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            if (steps <= 0)
            {
                AddArcCell(p0.X, p0.Y, p0.Z, ta);
                sinceTick += 1f;
                return;
            }

            float length = Mathf.Sqrt((float)dx * dx + (float)dy * dy);
            float stepLength = length / steps;
            float dirX = dx / length;
            float dirY = dy / length;

            for (int s = 0; s <= steps; s++)
            {
                float k = (float)s / steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(p0.X, p1.X, k));
                int y = Mathf.RoundToInt(Mathf.Lerp(p0.Y, p1.Y, k));
                float z = Mathf.Lerp(p0.Z, p1.Z, k);
                float t = Mathf.Lerp(ta, tb, k);

                AddArcCell(x, y, z, t);

                sinceTick += stepLength;
                if (sinceTick >= tickEveryPixels && s > 0 && s < steps)
                {
                    sinceTick = 0f;
                    AddTick(x, y, z, dirX, dirY);
                }
            }
        }

        void AddArcCell(int x, int y, float z, float t)
        {
            Color32 core = ArcColor(t);
            AddCell(x, y, z, core, PriorityCore);

            Color32 halo = WithAlpha(core, 0.28f);
            for (int h = 1; h <= railHaloPixels; h++)
            {
                for (int ox = -h; ox <= h; ox++)
                {
                    for (int oy = -h; oy <= h; oy++)
                    {
                        if (Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oy)) != h)
                            continue;
                        AddCell(x + ox, y + oy, z, halo, PriorityHalo);
                    }
                }
            }
        }

        void AddTick(int x, int y, float z, float dirX, float dirY)
        {
            // 像素空间的垂直方向（-dy, dx），横向铺 1×3 px；斜向时自然是阶梯像素。
            float px = -dirY;
            float py = dirX;
            int half = Mathf.Max(1, tickLengthPixels / 2);
            for (int q = -half; q <= half; q++)
            {
                int tx = Mathf.RoundToInt(x + px * q);
                int ty = Mathf.RoundToInt(y + py * q);
                AddCell(tx, ty, z, kTick, PriorityTick);
            }
        }

        void AddCell(int x, int y, float z, Color32 color, int priority)
        {
            long key = ((long)x << 32) | (uint)y;
            if (_cells.TryGetValue(key, out PixelCell cell))
            {
                if (cell.Priority >= priority)
                    return;
                cell.Priority = priority;
                cell.Color = color;
                cell.Center = CellCenter(x, y, z);
                _cells[key] = cell;
                return;
            }

            _cells[key] = new PixelCell
            {
                Priority = priority,
                Color = color,
                Center = CellCenter(x, y, z),
            };
        }

        static Color32 ArcColor(float t)
        {
            if (t < 1f / 3f)
                return kCoreNear;
            if (t < 2f / 3f)
                return kCoreMid;
            return kCoreFar;
        }

        static Color32 WithAlpha(Color32 color, float scale)
        {
            return new Color32(
                color.r, color.g, color.b,
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * scale), 0, 255));
        }

        void BuildArcMesh()
        {
            _cellList.Clear();
            foreach (KeyValuePair<long, PixelCell> pair in _cells)
                _cellList.Add(pair.Value);

            int count = _cellList.Count;
            if (count == 0)
            {
                _arcFilter.gameObject.SetActive(false);
                return;
            }

            EnsureCapacity(count);
            float half = _unit * 0.5f;
            Vector3 rx = _camRight * half;
            Vector3 uy = _camUp * half;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;

            int v = 0;
            int t = 0;
            for (int i = 0; i < count; i++)
            {
                PixelCell cell = _cellList[i];
                Vector3 c = cell.Center;

                _vertices[v] = toLocal.MultiplyPoint3x4(c - rx - uy);
                _vertices[v + 1] = toLocal.MultiplyPoint3x4(c + rx - uy);
                _vertices[v + 2] = toLocal.MultiplyPoint3x4(c + rx + uy);
                _vertices[v + 3] = toLocal.MultiplyPoint3x4(c - rx + uy);
                _colors[v] = cell.Color;
                _colors[v + 1] = cell.Color;
                _colors[v + 2] = cell.Color;
                _colors[v + 3] = cell.Color;

                _triangles[t] = v;
                _triangles[t + 1] = v + 1;
                _triangles[t + 2] = v + 2;
                _triangles[t + 3] = v;
                _triangles[t + 4] = v + 2;
                _triangles[t + 5] = v + 3;

                v += 4;
                t += 6;
            }

            _arcMesh.Clear();
            _arcMesh.SetVertices(_vertices, 0, v);
            _arcMesh.SetColors(_colors, 0, v);
            _arcMesh.SetTriangles(_triangles, 0, t, 0, true);
        }

        void EnsureCapacity(int cells)
        {
            int need = cells * 4;
            if (_vertices != null && _vertices.Length >= need)
                return;

            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(4, need));
            _vertices = new Vector3[capacity];
            _colors = new Color32[capacity];
            _triangles = new int[capacity / 4 * 6];
        }

        // ------------------------------------------------------------------
        // 起手：根部锚点 + 根部→出手点虚线引线（覆盖层，不被角色挡住）
        // ------------------------------------------------------------------

        void RebuildOrigin(Vector3 handWorld)
        {
            // 视觉根部 = 角色枢轴（StandardThrowRules.ThrowOrigin 的逆运算）；
            // 只影响视觉，实际弹道起点仍是 handWorld。
            Vector3 root = handWorld - Vector3.up * StandardThrowRules.ThrowOriginHeight;
            PixelPoint rootPx = ToPixel(root);
            PixelPoint handPx = ToPixel(handWorld);

            var verts = new List<Vector3>();
            var colors = new List<Color32>();
            var tris = new List<int>();

            // 根部 5×5 菱形锚点（|dx|+|dy| == 2）。
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) == 2)
                        AddPixelCellQuad(verts, colors, tris, rootPx.X + dx, rootPx.Y + dy, rootPx.Z, kCyan);
                }
            }

            // 根部 → 手部虚线引线（每 2 px 一格，末端留给光带亮芯）。
            int steps = Mathf.Max(
                Mathf.Abs(handPx.X - rootPx.X),
                Mathf.Abs(handPx.Y - rootPx.Y));
            if (steps > 0)
            {
                for (int s = 0; s < steps; s += 2)
                {
                    float k = (float)s / steps;
                    int x = Mathf.RoundToInt(Mathf.Lerp(rootPx.X, handPx.X, k));
                    int y = Mathf.RoundToInt(Mathf.Lerp(rootPx.Y, handPx.Y, k));
                    float z = Mathf.Lerp(rootPx.Z, handPx.Z, k);
                    AddPixelCellQuad(verts, colors, tris, x, y, z, kCyanDim);
                }
            }

            var local = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
                local[i] = transform.InverseTransformPoint(verts[i]);

            _originMesh.Clear();
            _originMesh.SetVertices(local);
            _originMesh.SetColors(colors);
            _originMesh.SetTriangles(tris, 0);
            _originMesh.RecalculateBounds();
        }

        /// <summary>把一格像素（世界空间中心）展开成面向相机的 1×1 方块。</summary>
        void AddPixelCellQuad(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            int x, int y, float z, Color32 color)
        {
            float half = _unit * 0.5f;
            Vector3 c = CellCenter(x, y, z);
            AddQuad(vertices, colors, triangles,
                c - _camRight * half - _camUp * half,
                c + _camRight * half - _camUp * half,
                c + _camRight * half + _camUp * half,
                c - _camRight * half + _camUp * half,
                color);
        }

        // ------------------------------------------------------------------
        // 装配（懒建一次）
        // ------------------------------------------------------------------

        void EnsureVisuals()
        {
            if (_arcFilter != null)
                return;

            Material baseMaterial = legacyLine != null && legacyLine.sharedMaterial != null
                ? legacyLine.sharedMaterial
                : FallbackPreviewMaterial();

            _arcMesh = new Mesh { name = "TrajectoryArc" };
            _arcMesh.MarkDynamic();
            var arcGo = new GameObject("TrajectoryArc");
            arcGo.transform.SetParent(transform, false);
            _arcFilter = arcGo.AddComponent<MeshFilter>();
            _arcFilter.sharedMesh = _arcMesh;
            var arcRenderer = arcGo.AddComponent<MeshRenderer>();
            arcRenderer.sharedMaterial = EffectMaterial(baseMaterial, "TrajectoryArc", depthTest: true);
            arcRenderer.shadowCastingMode = ShadowCastingMode.Off;
            arcRenderer.receiveShadows = false;
            arcGo.SetActive(false);

            _originMesh = new Mesh { name = "TrajectoryOrigin" };
            _originMesh.MarkDynamic();
            var originGo = new GameObject("TrajectoryOrigin");
            originGo.transform.SetParent(transform, false);
            _originFilter = originGo.AddComponent<MeshFilter>();
            _originFilter.sharedMesh = _originMesh;
            var originRenderer = originGo.AddComponent<MeshRenderer>();
            originRenderer.sharedMaterial = EffectMaterial(baseMaterial, "TrajectoryOrigin", depthTest: false);
            originRenderer.shadowCastingMode = ShadowCastingMode.Off;
            originRenderer.receiveShadows = false;
            originGo.SetActive(false);

            EnsurePulses(baseMaterial);
            EnsureReticle(baseMaterial);
        }

        void EnsurePulses(Material baseMaterial)
        {
            int n = Mathf.Max(1, pulseCount);
            _pulses = new Transform[n];
            _pulseMaterial = EffectMaterial(baseMaterial, "TrajectoryPulse", depthTest: true);
            Mesh diamond = CreatePixelDiamondMesh(pulseSizePixels);

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

        void EnsureReticle(Material baseMaterial)
        {
            var root = new GameObject("TrajectoryReticle");
            root.transform.SetParent(transform, false);

            Material ringMaterial = EffectMaterial(baseMaterial, "TrajectoryReticleRing", depthTest: false);
            Material bracketMaterial = EffectMaterial(baseMaterial, "TrajectoryReticleBrackets", depthTest: false);
            int radius = Mathf.Max(4, reticleRadiusPixels);

            _ringA = MakeReticleChild(root.transform, "RingA",
                CreateReticleRingMesh(radius, 0), ringMaterial);
            _ringB = MakeReticleChild(root.transform, "RingB",
                CreateReticleRingMesh(radius, 1), ringMaterial);
            _brackets = MakeReticleChild(root.transform, "Brackets",
                CreateReticleBracketsMesh(radius), bracketMaterial);

            _ringB.gameObject.SetActive(false);
            root.SetActive(false);
            _reticle = root.transform;
        }

        static Transform MakeReticleChild(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        void PlaceReticle(Vector3 impact, Camera cam)
        {
            if (_reticle == null)
                return;
            Vector3 position = impact + Vector3.up * ImpactMarkerLift;
            _reticle.position = SnapToPixelGrid(position, cam);
            _reticle.rotation = cam.transform.rotation;
            _reticle.gameObject.SetActive(true);
        }

        Material EffectMaterial(Material source, string name, bool depthTest)
        {
            var material = new Material(source) { name = name };
            material.color = Color.white;
            if (material.HasProperty("_DepthTest"))
                material.SetFloat("_DepthTest", depthTest ? 1f : 0f);
            return material;
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
        // 网格构造（全部以艺术像素为单位）
        // ------------------------------------------------------------------

        /// <summary>菱形像素光点：|dx|+|dy| ≤ r 的格子拼成块状菱形。</summary>
        static Mesh CreatePixelDiamondMesh(int sizePixels)
        {
            int r = Mathf.Max(1, sizePixels / 2);
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();

            float u = CameraFraming.WorldPerPixel;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) > r)
                        continue;
                    AddPixelQuad(vertices, colors, triangles, dx * u, dy * u, u * 0.5f, kPulse);
                }
            }
            return Finish(vertices, colors, triangles, "TrajectoryPulse");
        }

        /// <summary>17×17 准星外框：四角括号 + 中心十字（块状像素）。</summary>
        static Mesh CreateReticleBracketsMesh(int radiusPixels)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            float u = CameraFraming.WorldPerPixel;
            int r = Mathf.Max(3, radiusPixels);

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int a = 0; a <= 2; a++)
                    {
                        AddPixelQuad(vertices, colors, triangles, (sx * (r - a)) * u, (sy * r) * u, u * 0.5f, kMagenta);
                        AddPixelQuad(vertices, colors, triangles, (sx * r) * u, (sy * (r - a)) * u, u * 0.5f, kMagenta);
                    }
                }
            }

            for (int dx = -1; dx <= 1; dx++)
                AddPixelQuad(vertices, colors, triangles, dx * u, 0f, u * 0.5f, kMagentaBright);
            for (int dy = -1; dy <= 1; dy++)
                AddPixelQuad(vertices, colors, triangles, 0f, dy * u, u * 0.5f, kMagentaBright);

            return Finish(vertices, colors, triangles, "TrajectoryReticleBrackets");
        }

        /// <summary>虚线菱形环：|x|+|y| = r-3，按 x 奇偶拆成两帧交替。</summary>
        static Mesh CreateReticleRingMesh(int radiusPixels, int parity)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            float u = CameraFraming.WorldPerPixel;
            int r = Mathf.Max(3, radiusPixels - 3);

            for (int x = -r; x <= r; x++)
            {
                for (int y = -r; y <= r; y++)
                {
                    if (Mathf.Abs(x) + Mathf.Abs(y) != r)
                        continue;
                    if ((x & 1) != parity)
                        continue;
                    AddPixelQuad(vertices, colors, triangles, x * u, y * u, u * 0.5f, kMagenta);
                }
            }

            return Finish(vertices, colors, triangles, "TrajectoryReticleRing");
        }

        /// <summary>世界空间（相机平面）的 1×1 方块。</summary>
        static void AddPixelQuad(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            float x, float y, float half, Color32 color)
        {
            vertices.Add(new Vector3(x - half, y - half, 0f));
            vertices.Add(new Vector3(x + half, y - half, 0f));
            vertices.Add(new Vector3(x + half, y + half, 0f));
            vertices.Add(new Vector3(x - half, y + half, 0f));
            for (int i = 0; i < 4; i++)
                colors.Add(color);
            int v = vertices.Count - 4;
            AddTriangles(triangles, v);
        }

        static void AddQuad(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
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
            AddTriangles(triangles, v);
        }

        static void AddTriangles(List<int> triangles, int v)
        {
            triangles.Add(v);
            triangles.Add(v + 1);
            triangles.Add(v + 2);
            triangles.Add(v);
            triangles.Add(v + 2);
            triangles.Add(v + 3);
        }

        static Mesh Finish(List<Vector3> vertices, List<Color32> colors, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices.ToArray();
            mesh.colors32 = colors.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
