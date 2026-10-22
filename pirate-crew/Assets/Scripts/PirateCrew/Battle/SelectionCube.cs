using UnityEngine;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Visual;

namespace PirateCrew.Battle
{
    /// <summary>
    /// **选中单位的立方体角框**（8 个角、每角 3 条短棱，共 24 段；红色警戒式选中框）。
    ///
    /// 【形态】以单位脚底为底、包住整只单位的轴对齐立方体。只在 8 个角附近沿三条棱各画
    /// 一段短臂（<see cref="ArmLength"/>），棱中段留空——读起来是八个角框，而不是完整线框。
    /// 每条短臂用一条面向相机的细长四边形表示，宽度 = **1 艺术像素**
    /// （<see cref="CameraFraming.WorldPerPixel"/>），和场景像素同一颗粒度。
    ///
    /// 【像素化透明趟】角框走 <c>PixelartTransparent</c>（与弹道预览同一趟）：画进 540p
    /// 艺术画布，再随场景一起点采样上屏；因此它的像素大小就是艺术像素，不会出现
    /// “全分辨率细线比场景像素更细”的割裂。材质名带 SelectionCube，命中
    /// <c>PixelartContentConverter</c> 的效果件白名单，不会被转成不透明像素化材质。
    ///
    /// 【覆盖关系】透明趟 shader 手动采样 Cast 路径发布的 `_PixelartDepthBuffer` 做逐像素
    /// 比较：角色身体/地形在前时，后面的棱被 clip（等效 ZTest LEqual）。角框不进 G-buffer，
    /// 不会触发屏幕空间描边。
    ///
    /// 【挂载】由 <see cref="PirateBase.Awake"/> 自动追加。线框物体**不挂在单位下**：
    /// 单位根带 0.375×0.5×0.375 的非均匀缩放，父级会把它剪切变形；改为世界空间独立物体，
    /// 每帧跟随单位位置，OnDestroy 清理。
    /// </summary>
    [RequireComponent(typeof(PirateBase))]
    public sealed class SelectionCube : MonoBehaviour
    {
        /// <summary>选中框专用层号（ProjectSettings 里名为 SelectionOverlay；不与标准透明趟抢件）。</summary>
        const int OverlayLayer = 31;

        /// <summary>立方体半宽（米）：1.2 m 见方，包住单位视觉体宽。</summary>
        const float HalfWidth = 0.60f;
        /// <summary>立方体高（米）：单位视觉总高 + 0.10 m 余量。</summary>
        const float CubeHeight = CameraFraming.UnitVisualHeight + 0.10f;
        /// <summary>底边离地（米），避免与地面 z-fighting。</summary>
        const float GroundOffset = 0.02f;
        /// <summary>棱线宽 = 1 艺术像素（0.0593 m）；画在像素化透明趟里，与场景同像素大小。</summary>
        const float LineWidth = CameraFraming.WorldPerPixel;

        /// <summary>每条角臂的长度（米）：只画角附近，棱中段留空。</summary>
        const float ArmLength = 0.28f;

        const int ArmCount = 24;          // 8 个角 × 每角 3 条短臂
        const int VertsPerArm = 4;
        const int VertexCount = ArmCount * VertsPerArm;

        // 8 个角：0..3 为底面，4..7 为顶面（同序：-x-z, +x-z, +x+z, -x+z）。
        static readonly Vector3[] Corners = BuildCorners();
        // 24 条角臂：从每个角沿三条棱向内伸 ArmLength，棱中段不画。
        static readonly Arm[] Arms = BuildArms();

        static Mesh _sharedMesh;
        static readonly Vector3[] _vertices = new Vector3[VertexCount];
        static readonly Material[] _sharedMaterials = new Material[2];
        static Vector3 _lastRight;
        static Vector3 _lastUp;
        static bool _hasOrientation;

        PirateBase _pirate;
        Transform _cube;
        bool _built;

        void Awake()
        {
            _pirate = GetComponent<PirateBase>();
        }

        void Update()
        {
            if (_pirate == null)
                return;

            bool visible = _pirate.Selected && _pirate.Alive;
            if (visible && !_built)
                Build();

            if (!_built || _cube == null)
                return;

            if (_cube.gameObject.activeSelf != visible)
                _cube.gameObject.SetActive(visible);
            if (!visible)
                return;

            // 世界空间独立物体：位置跟单位，旋转/缩放保持单位阵——线框始终轴对齐，
            // 且不受单位根非均匀缩放影响。
            _cube.position = transform.position
                + Vector3.up * (GroundOffset + CubeHeight * 0.5f);
            _cube.rotation = Quaternion.identity;
            _cube.localScale = Vector3.one;

            Camera cam = Camera.main;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            UpdateSharedMesh(right, up);
        }

        void Build()
        {
            _built = true;
            var go = new GameObject("SelectionCube");
            _cube = go.transform;
            _cube.SetParent(null, false);
            go.layer = OverlayLayer;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = SharedMesh();

            Material material = SharedMaterial(_pirate.TeamIndex);
            if (material == null)
                return;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void OnDisable()
        {
            if (_cube != null)
                _cube.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_cube == null)
                return;
            if (Application.isPlaying)
                Destroy(_cube.gameObject);
            else
                DestroyImmediate(_cube.gameObject);
        }

        /// <summary>像素化透明趟材质（按阵营各一份）；线宽由几何保证 1 艺术像素。</summary>
        static Material SharedMaterial(int teamIndex)
        {
            int slot = teamIndex == 0 ? 0 : 1;
            if (_sharedMaterials[slot] != null)
                return _sharedMaterials[slot];

            Shader shader = PixelartShaders.Load(PixelartPath.TransparentShaderName);
            if (shader == null)
            {
                global::PirateCrew.Core.Log.Error(
                    "[SelectionCube] 找不到像素透明 shader「" + PixelartPath.TransparentShaderName
                    + "」，选中框不会显示。");
                return null;
            }

            Color color = slot == 0 ? CrewVisualCatalog.TeamRed : CrewVisualCatalog.TeamBlue;
            color.a = 1f;
            var material = new Material(shader)
            {
                name = slot == 0 ? "SelectionCube_Red" : "SelectionCube_Blue",
            };
            material.SetColor("_Color", color);
            if (material.HasProperty("_DepthTest"))
                material.SetFloat("_DepthTest", 1f);
            _sharedMaterials[slot] = material;
            return material;
        }

        static Mesh SharedMesh()
        {
            if (_sharedMesh != null)
                return _sharedMesh;

            var triangles = new int[ArmCount * 6];
            for (int arm = 0; arm < ArmCount; arm++)
            {
                int v = arm * VertsPerArm;
                int t = arm * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var colors = new Color32[VertexCount];
            var uv = new Vector2[VertexCount];
            for (int i = 0; i < VertexCount; i++)
            {
                colors[i] = new Color32(255, 255, 255, 255);
                uv[i] = Vector2.zero;
            }

            var mesh = new Mesh { name = "SelectionCube" };
            mesh.MarkDynamic();
            FillVertices(Vector3.right, Vector3.up);
            mesh.vertices = _vertices;
            mesh.colors32 = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(
                Vector3.zero,
                new Vector3(HalfWidth * 2f + 0.3f, CubeHeight + 0.3f, HalfWidth * 2f + 0.3f));
            _sharedMesh = mesh;
            return mesh;
        }

        /// <summary>按相机右/上方向重算 24 条角臂（相机转动时才重算）。</summary>
        static void UpdateSharedMesh(Vector3 right, Vector3 up)
        {
            if (_sharedMesh == null)
                return;
            if (_hasOrientation
                && Vector3.Dot(right, _lastRight) > 0.99999f
                && Vector3.Dot(up, _lastUp) > 0.99999f)
                return;

            FillVertices(right, up);
            _sharedMesh.SetVertices(_vertices);
            _lastRight = right;
            _lastUp = up;
            _hasOrientation = true;
        }

        static void FillVertices(Vector3 right, Vector3 up)
        {
            for (int arm = 0; arm < ArmCount; arm++)
            {
                Vector3 p0 = Arms[arm].A;
                Vector3 p1 = Arms[arm].B;
                Vector3 delta = p1 - p0;

                // 棱在屏幕上的方向；把线宽方向取成它的屏幕垂线，四边形投影后宽 1 艺术像素。
                Vector3 screenDir = new Vector3(
                    Vector3.Dot(delta, right), Vector3.Dot(delta, up), 0f);
                Vector3 offset;
                if (screenDir.sqrMagnitude < 1e-8f)
                {
                    // 棱正对镜头（投影成点）：任取一个屏幕方向，至少留一个像素点。
                    offset = right * (LineWidth * 0.5f);
                }
                else
                {
                    Vector3 perpendicular = new Vector3(-screenDir.y, screenDir.x, 0f).normalized;
                    offset = (right * perpendicular.x + up * perpendicular.y)
                        * (LineWidth * 0.5f);
                }

                int v = arm * VertsPerArm;
                _vertices[v] = p0 - offset;
                _vertices[v + 1] = p0 + offset;
                _vertices[v + 2] = p1 + offset;
                _vertices[v + 3] = p1 - offset;
            }
        }

        static Vector3[] BuildCorners()
        {
            float half = CubeHeight * 0.5f;
            float h = HalfWidth;
            return new[]
            {
                new Vector3(-h, -half, -h),
                new Vector3( h, -half, -h),
                new Vector3( h, -half,  h),
                new Vector3(-h, -half,  h),
                new Vector3(-h,  half, -h),
                new Vector3( h,  half, -h),
                new Vector3( h,  half,  h),
                new Vector3(-h,  half,  h),
            };
        }

        /// <summary>
        /// 8 个角 × 3 条短臂：从角点沿三条棱向内各伸 <see cref="ArmLength"/>。
        /// 棱中段不画，所以读起来是"八个角框"，不是完整线框。
        /// </summary>
        static Arm[] BuildArms()
        {
            var arms = new Arm[ArmCount];
            int index = 0;
            for (int c = 0; c < Corners.Length; c++)
            {
                Vector3 corner = Corners[c];
                float sx = Mathf.Sign(corner.x);
                float sy = Mathf.Sign(corner.y);
                float sz = Mathf.Sign(corner.z);
                arms[index++] = new Arm(corner, corner + new Vector3(-sx * ArmLength, 0f, 0f));
                arms[index++] = new Arm(corner, corner + new Vector3(0f, -sy * ArmLength, 0f));
                arms[index++] = new Arm(corner, corner + new Vector3(0f, 0f, -sz * ArmLength));
            }
            return arms;
        }

        readonly struct Arm
        {
            public readonly Vector3 A;
            public readonly Vector3 B;

            public Arm(Vector3 a, Vector3 b)
            {
                A = a;
                B = b;
            }
        }
    }
}
