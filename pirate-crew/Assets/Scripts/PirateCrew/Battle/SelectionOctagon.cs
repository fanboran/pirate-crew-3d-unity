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
    /// 旧链，在像素路径下本就不可靠（船员描边迁像素另有待办），故不再作为选中载体。
    ///
    /// 【几何口径】正八边形**环**（外半径 0.62 / 内半径 0.50 世界米，环厚 ≈2 艺术像素），
    /// 顶点取 22.5°+k·45°，让四条边正对屏幕纵横——等距 30° 下画面上读作 2:1 的八边形框。
    /// 单位占地 AABB 为 12×16 px = 0.75×1.0 m（`LevelGeometry.PixelsToUnits`），外径 1.24 罩住脚底一圈。
    /// 网格与材质**全单位共享**（静态缓存）：每个单位只多一个 MeshRenderer，不新增资产。
    ///
    /// 【挂载】由 <see cref="PirateBase.Awake"/> 自动追加（`RequireComponent` 同思路，
    /// 但保持可选依赖：Prefab/场景零改动即可生效）。
    /// 【状态】选中且存活才显示；呼吸脉动是极小幅度的整件缩放（像素域内会随边缘吸附，属预期）。
    /// </summary>
    [RequireComponent(typeof(PirateBase))]
    public sealed class SelectionOctagon : MonoBehaviour
    {
        /// <summary>环外半径（米）。</summary>
        const float OuterRadius = 0.62f;
        /// <summary>环内半径（米）。</summary>
        const float InnerRadius = 0.50f;
        /// <summary>离地高度（米）：压在地面上方、低于角色脚面，避免 z-fighting。</summary>
        const float GroundOffset = 0.02f;
        /// <summary>脉动幅度（±3%）与周期（秒）。</summary>
        const float PulseAmplitude = 0.03f;
        const float PulsePeriod = 1.6f;

        static Mesh _sharedMesh;
        static readonly Material[] _sharedMaterials = new Material[2];

        PirateBase _pirate;
        Transform _ring;
        MeshRenderer _renderer;

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

            if (visible)
            {
                float pulse = 1f + PulseAmplitude
                    * Mathf.Sin(Time.time * Mathf.PI * 2f / PulsePeriod);
                _ring.localScale = new Vector3(pulse, 1f, pulse);
            }
        }

        /// <summary>首次选中时懒建（不选中就没开销；建了之后只切 active）。</summary>
        void Build()
        {
            var ringGo = new GameObject("SelectionOctagon");
            _ring = ringGo.transform;
            _ring.SetParent(transform, false);
            _ring.localPosition = new Vector3(0f, GroundOffset, 0f);

            var filter = ringGo.AddComponent<MeshFilter>();
            filter.sharedMesh = SharedMesh();

            _renderer = ringGo.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = SharedMaterial(_pirate.TeamIndex);
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        static Mesh SharedMesh()
        {
            if (_sharedMesh != null)
                return _sharedMesh;

            const int sides = 8;
            var vertices = new Vector3[sides * 2];
            var normals = new Vector3[sides * 2];
            var triangles = new int[sides * 6];

            for (int i = 0; i < sides; i++)
            {
                // 22.5° + k·45°：四条边正对屏幕纵横，画面读作 2:1 八边形框
                float angle = (2 * i + 1) * Mathf.PI / (2f * sides);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(cos * OuterRadius, 0f, sin * OuterRadius);
                vertices[i * 2 + 1] = new Vector3(cos * InnerRadius, 0f, sin * InnerRadius);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int outer = i * 2;
                int inner = i * 2 + 1;
                int outerNext = next * 2;
                int innerNext = next * 2 + 1;
                int t = i * 6;
                // 绕序按 +Y 法线：从上方看为顺时针（Unity 正面）
                triangles[t] = outer;
                triangles[t + 1] = innerNext;
                triangles[t + 2] = outerNext;
                triangles[t + 3] = outer;
                triangles[t + 4] = inner;
                triangles[t + 5] = innerNext;
            }

            _sharedMesh = new Mesh { name = "SelectionOctagonRing" };
            _sharedMesh.vertices = vertices;
            _sharedMesh.normals = normals;
            _sharedMesh.triangles = triangles;
            _sharedMesh.RecalculateBounds();
            return _sharedMesh;
        }

        static Material SharedMaterial(int teamIndex)
        {
            int slot = teamIndex == 0 ? 0 : 1;
            if (_sharedMaterials[slot] != null)
                return _sharedMaterials[slot];

            Color color = slot == 0 ? CrewVisualCatalog.TeamRed : CrewVisualCatalog.TeamBlue;
            // 2 档色带、自身不描边；priority 0 = 装饰件（不参与描边接触线判定）
            _sharedMaterials[slot] = PixelartMaterialFactory.Create(
                slot == 0 ? "SelectionOctagon_Red" : "SelectionOctagon_Blue",
                color, bandCount: 2f, outlinePixels: 0f, priority: 0f);
            return _sharedMaterials[slot];
        }
    }
}
