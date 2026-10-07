using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>
    /// **近裁剪守卫**（创始人 2026-10-07 裁决："碰到的物件整个消失，别切成一半"）：
    /// 当本物体与相机近平面相交（会被切掉一部分）时，把整件渲染器关掉——宁可整件不见，
    /// 也不出现"半个石头"。相交解除（物体整体回到近平面之前）时立刻恢复。
    ///
    /// 【为什么需要它】正交相机的近裁剪是**屏幕平面**：物体只要跨在平面上，画面里就是被齐平切掉
    /// 一半（读作模型坏了）。空岛的天外浮件壳（悬浮晶/云/浮石）比基准机位更靠外，壳最外圈本来
    /// 就压着近裁剪面；浮沉一走动更是反复切进切出。把近裁剪放到 -80（见
    /// <c>CameraFraming.OrthoNearClip</c>）解决"相机在壳内"的普遍情形，本组件兜住最后那圈
    /// 仍可能跨面的浮件。
    ///
    /// 【判据】包围球（半径 = 各子件世界包围盒的 extents 幅值，绕物体局部中心）与相机平面求交：
    ///   depth = dot(中心 − 相机位置, 相机前向)；depth − 半径 &gt; near ⇒ 整体在平面之前 → 显示，
    ///   否则（跨面或整体在后）→ 整件隐藏。
    /// 用包围球而不是精确 AABB：球对旋转不变，物体带缓慢摆头时判据不会抖。
    ///
    /// 【只在必要时开关渲染器】状态不变化时一帧都不写，避免无谓的 Renderer 脏标记。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NearClipHider : MonoBehaviour
    {
        Renderer[] _renderers;
        Vector3 _localCenter;
        float _radius;
        bool _hidden;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            RecomputeBounds();
        }

        /// <summary>重算包围球（子件集合变化后手动调用；烘焙出来的浮件是静态层级，Awake 一次即可）。</summary>
        public void RecomputeBounds()
        {
            bool any = false;
            Bounds world = default;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null)
                    continue;

                if (!any)
                {
                    world = r.bounds;
                    any = true;
                }
                else
                {
                    world.Encapsulate(r.bounds);
                }
            }

            if (!any)
            {
                _localCenter = Vector3.zero;
                _radius = 0f;
                return;
            }

            _localCenter = transform.InverseTransformPoint(world.center);
            _radius = world.extents.magnitude;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || _renderers.Length == 0)
                return;

            // 正交与透视共用同一条判据：到相机平面的**沿前向深度**。
            // （透视相机下"被切一半"的现象一致，判据也一致；本工程现役是正交。）
            Vector3 center = transform.TransformPoint(_localCenter);
            float depth = Vector3.Dot(center - cam.transform.position, cam.transform.forward);
            bool shouldHide = depth - _radius <= cam.nearClipPlane;

            if (shouldHide == _hidden)
                return;

            _hidden = shouldHide;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = !shouldHide;
            }
        }
    }
}
