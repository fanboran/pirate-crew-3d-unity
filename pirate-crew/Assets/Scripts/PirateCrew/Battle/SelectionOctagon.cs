using UnityEngine;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Visual;

namespace PirateCrew.Battle
{
    /// <summary>
    /// **选中单位的八角框标记**（地面投影；替换“选中靠反壳描边”的旧路）。
    ///
    /// 【为什么是地面八角框而不是描边】像素等距游戏的选择标记惯例是脚下框选件：
    /// 它不干扰角色像素本身、自动被地形深度遮挡、进入像素化路径后与画面同颗粒度。
    /// 旧路（`UnitOutlineBinder` 写 `_OutlineState` 的反壳描边）依赖 `PirateOutline`
    /// 旧链，在像素路径下本就不可靠（船员不做描边），故不再作为选中载体。
    ///
    /// 【几何口径】正八边形**有厚度的框**：外半径 0.62 / 内半径 0.46 世界米、高 0.12 米。
    /// 顶点取 22.5°+k·45°，四条边正对屏幕纵横——等距 30° 下画面上读作 2:1 的八边形；
    /// 顶面 + 外壁 + 内壁三段烘焙不同明度（顶亮、外壁暗、内壁中），在 30° 俯角下
    /// 读出“块”的厚度，而不是一条平贴地面的线环。
    ///
    /// 【走透明趟，不走 G-buffer】本件是 UI 语义的覆盖标记，不应参与屏幕空间描边
    /// （扁平环贴地时，描边特性会沿环周围的地面像素生成一圈黑接触线），也不应被
    /// 色带/光照二次着色。材质走 <c>PixelartTransparent</c>，由
    /// <see cref="global::PirateCrew.Rendering.Pixelart.PixelartTransparentFeature"/>
    /// 画进艺术画布：与画面同像素化，并按细像素深度被地形/单位正确遮挡。
    ///
    /// 【挂载】由 <see cref="PirateBase.Awake"/> 自动追加（Prefab/场景零改动即可生效）。
    /// 【状态】选中且存活才显示；呼吸脉动是极小幅度的整件缩放（像素域内会随边缘吸附，属预期）。
    /// </summary>
    [RequireComponent(typeof(PirateBase))]
    public sealed class SelectionOctagon : MonoBehaviour
    {
        /// <summary>正八边形边数。</summary>
        const int Sides = 8;
        /// <summary>框外半径（米）。</summary>
        const float OuterRadius = 0.62f;
        /// <summary>框内半径（米）。</summary>
        const float InnerRadius = 0.46f;
        /// <summary>框体高度（米）：30° 俯角下约 2 艺术像素的立面，读出“块”的体积。</summary>
        const float Height = 0.12f;
        /// <summary>离地高度（米）：压在地面上方、低于角色脚面，避免 z-fighting。</summary>
        const float GroundOffset = 0.02f;
        /// <summary>脉动幅度（±3%）与周期（秒）。</summary>
        const float PulseAmplitude = 0.03f;
        const float PulsePeriod = 1.6f;

        // 6 圈顶点：外壁底/外壁顶/顶面外/顶面内/内壁顶/内壁底。
        // 顶面与侧壁各用独立顶点圈，才能把不同明度烘进顶点色而不被插值糊在一起。
        const int RingCount = 6;

        static readonly Mesh[] _sharedMeshes = new Mesh[2];
        static Material _sharedMaterial;

        PirateBase _pirate;
        Transform _ring;
        Vector3 _baseScale = Vector3.one;

        void Awake()
        {
            _pirate = GetComponent<PirateBase>();
        }

        void Update()
        {
            if (_pirate == null)
                return;

            bool visible = _pirate.Selected && _pirate.Alive;
            if (visible && _ring == null)
                Build();

            if (_ring == null)
                return;

            if (_ring.gameObject.activeSelf != visible)
                _ring.gameObject.SetActive(visible);

            if (!visible)
                return;

            float pulse = 1f + PulseAmplitude
                * Mathf.Sin(Time.time * Mathf.PI * 2f / PulsePeriod);
            _ring.localScale = new Vector3(
                _baseScale.x * pulse, _baseScale.y, _baseScale.z * pulse);
            // 框体保持世界轴向（不随单位转身），离地高度也按世界 Y 给，
            // 避免父级若有俯仰时把框带斜。
            _ring.position = transform.position + Vector3.up * GroundOffset;
            _ring.rotation = Quaternion.identity;
        }

        /// <summary>首次选中时懒建（不选中就没开销；建了之后只切 active）。</summary>
        void Build()
        {
            var ringGo = new GameObject("SelectionOctagon");
            _ring = ringGo.transform;
            _ring.SetParent(transform, false);

            // 单位根节点带非 1 的缩放（Crew.prefab 根 0.375×0.5×0.375；物理模板 0.75×1×0.75）——
            // 子物体不补偿就会被同比缩小（0.62 m 外径被压成 0.23 m，肉眼等于没有）。
            // 这里按父级 lossyScale 取逆，保证**世界空间**外径恒为 OuterRadius。
            Vector3 lossy = transform.lossyScale;
            _baseScale = new Vector3(
                Mathf.Abs(lossy.x) > 0.0001f ? 1f / lossy.x : 1f,
                Mathf.Abs(lossy.y) > 0.0001f ? 1f / lossy.y : 1f,
                Mathf.Abs(lossy.z) > 0.0001f ? 1f / lossy.z : 1f);
            _ring.localScale = _baseScale;

            var filter = ringGo.AddComponent<MeshFilter>();
            filter.sharedMesh = SharedMesh(_pirate.TeamIndex);

            Material material = SharedMaterial();
            if (material == null)
                return;

            var renderer = ringGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>透明趟材质（全单位共享一份白材质；阵营色烘在网格顶点色里）。</summary>
        static Material SharedMaterial()
        {
            if (_sharedMaterial != null)
                return _sharedMaterial;

            Shader shader = PixelartShaders.Load(PixelartPath.TransparentShaderName);
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                global::PirateCrew.Core.Log.Error(
                    "[SelectionOctagon] 找不到像素透明 shader「" + PixelartPath.TransparentShaderName
                    + "」，选中框不会显示。");
                return null;
            }

            _sharedMaterial = new Material(shader) { name = "SelectionOctagon_Transparent" };
            if (_sharedMaterial.HasProperty("_Color"))
                _sharedMaterial.SetColor("_Color", Color.white);
            return _sharedMaterial;
        }

        static Mesh SharedMesh(int teamIndex)
        {
            int slot = teamIndex == 0 ? 0 : 1;
            if (_sharedMeshes[slot] != null)
                return _sharedMeshes[slot];

            Color baseColor = slot == 0 ? CrewVisualCatalog.TeamRed : CrewVisualCatalog.TeamBlue;
            baseColor.a = 1f;
            Color32 top = baseColor;
            Color32 outerWall = Color.Lerp(baseColor, Color.black, 0.5f);
            Color32 innerWall = Color.Lerp(baseColor, Color.black, 0.28f);

            var vertices = new Vector3[Sides * RingCount];
            var colors = new Color32[Sides * RingCount];

            for (int ring = 0; ring < RingCount; ring++)
            {
                float radius;
                float y;
                Color32 color;
                switch (ring)
                {
                    case 0: radius = OuterRadius; y = 0f; color = outerWall; break;      // 外壁底
                    case 1: radius = OuterRadius; y = Height; color = outerWall; break;  // 外壁顶
                    case 2: radius = OuterRadius; y = Height; color = top; break;        // 顶面外
                    case 3: radius = InnerRadius; y = Height; color = top; break;        // 顶面内
                    case 4: radius = InnerRadius; y = Height; color = innerWall; break;  // 内壁顶
                    default: radius = InnerRadius; y = 0f; color = innerWall; break;     // 内壁底
                }

                for (int i = 0; i < Sides; i++)
                {
                    // 22.5° + k·45°：四条边正对屏幕纵横，画面读作 2:1 八边形框。
                    // 【不能除以 2*Sides】那会变成 22.5° 步进的八个点、只铺半圈，
                    // 连回去是一条横跨圆心的长边（曾表现为“畸形的环”）。
                    float angle = (2 * i + 1) * Mathf.PI / Sides;
                    int index = ring * Sides + i;
                    vertices[index] = new Vector3(
                        Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    colors[index] = color;
                }
            }

            // 3 条带 × 8 段 × 2 三角形 × 3 顶点；透明 shader 是 Cull Off，绕序不重要。
            // 【绘制顺序】侧壁先、顶面最后：透明趟 ZWrite Off，同一网格内部没有深度排序，
            // 顶面最后画才能在屏幕空间盖住远侧外壁的投影（否则远壁会在顶面远边上叠一条暗带）。
            var triangles = new int[Sides * 3 * 2 * 3];
            int cursor = 0;
            for (int i = 0; i < Sides; i++)
            {
                int next = (i + 1) % Sides;
                AddQuad(triangles, ref cursor,
                    i, Sides + i, Sides + next, next);                                    // 外壁
                AddQuad(triangles, ref cursor,
                    4 * Sides + i, 5 * Sides + i, 5 * Sides + next, 4 * Sides + next);     // 内壁
                AddQuad(triangles, ref cursor,
                    2 * Sides + i, 2 * Sides + next, 3 * Sides + next, 3 * Sides + i);   // 顶面
            }

            var mesh = new Mesh { name = slot == 0 ? "SelectionOctagon_Red" : "SelectionOctagon_Blue" };
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _sharedMeshes[slot] = mesh;
            return mesh;
        }

        static void AddQuad(int[] triangles, ref int cursor, int a, int b, int c, int d)
        {
            triangles[cursor++] = a;
            triangles[cursor++] = b;
            triangles[cursor++] = c;
            triangles[cursor++] = a;
            triangles[cursor++] = c;
            triangles[cursor++] = d;
        }
    }
}
