using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// WaterLab DoF autofocus 机制移植（B 流管线件）。原作 cs-full/CameraRig.cs 的
    /// autofocus 链逐语义搬运：每帧从相机位置沿 forward 打 raycast，命中即把
    /// 命中距离回写 DoF 的 focusDistance（CameraRig.SetFocusDistance 的直接赋值，
    /// 无平滑/无惯性）；未命中保持上次值（不回写）。菜单是静止场景，raycast 结果
    /// 每帧恒定 → 与 F 流定案的静态值 37.49 数值等价——这里移植的是**机制**，
    /// 37.49 保留为场景 Volume 的初始值（WaterLabSetup.EnsurePostProcess）。
    ///
    /// 【原作对应点】CameraRig.LateUpdate：autofocus 开关短路 →
    /// Ray(cam.position, cam.forward) → Game.Raycast(hit, QueryTriggerInteraction.Ignore)
    /// → 写 depthOfField.focusDistance。层掩码取 Physics 默认
    /// （原作 Game.Raycast 封装语义不可静态恢复，default(LayerMask) 按 All 处理），
    /// 触发器不参与（原作 Ignore）；限距取相机 far（超 far 命中无渲染意义）。
    ///
    /// 【接线】WaterLabSetup.EnsurePostProcess 挂到 LabCamera 上并注入
    /// ReplicaPostVolume；非 WaterLab 场景不挂本组件（BuildScene 全量重建语义），
    /// 不设全局闸门。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class WaterLabDofAutofocus : MonoBehaviour
    {
        /// <summary>autofocus 开关（原作 CameraRig.autofocus 同名语义，默认开）。</summary>
        public bool autofocus = true;

        /// <summary>回写目标 Volume（ReplicaPostVolume，装配器注入；null = 不工作）。</summary>
        public Volume volume;

        DepthOfField _dof;
        Camera _cam;

        void LateUpdate()
        {
            if (!autofocus || volume == null)
                return;
            // sharedProfile 是装配域内存对象（EnsurePostProcess CreateInstance 不落盘）；
            // 从磁盘重开场景再进 Play 时悬空为 null——安静跳过，不刷 NRE。
            if (volume.sharedProfile == null)
                return;
            if (_cam == null)
                _cam = GetComponent<Camera>();
            if (_dof == null && !volume.sharedProfile.TryGet(out _dof))
                return;

            var ray = new Ray(_cam.transform.position, _cam.transform.forward);
            if (Physics.Raycast(ray, out var hit, _cam.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                _dof.focusDistance.Override(hit.distance);
            }
        }
    }
}
