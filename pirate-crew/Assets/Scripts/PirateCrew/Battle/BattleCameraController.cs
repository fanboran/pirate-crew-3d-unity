using Cinemachine;
using PirateCrew.Core;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 战斗相机控制（Cinemachine 跟随 + §3.2 panToCharacter）。
    ///
    /// 【对应章节】§3.2（首回合/默认镜头目标 <c>panToCharacter</c>）、§4.5（选中反馈）、§8.1（相机平移思想）。
    ///
    /// 【参照模式】取自参照库 <c>rts-camera-cinemachine</c> 的 "CameraTarget 中转"：
    ///   虚拟相机 <c>Follow</c> 指向一个空 <see cref="cameraTarget"/>，本脚本只对该 Transform 做
    ///   平滑平移，从而"跟随当前行动角色"与"手动平移/锁定目标"可以共存，且不与 Cinemachine 阻尼打架。
    ///
    /// 【API 版本】目标 Cinemachine 2.9.7（风险清单 R2）：用 <c>CinemachineVirtualCamera</c>（2.x），
    ///   不是 Unity 6 的 <c>CinemachineCamera</c>。
    ///
    /// 【为什么这里能直接用强类型】<c>PirateCrew.Runtime.asmdef</c> 的 references 显式列了 <c>"Cinemachine"</c>。
    ///   注意 Unity 只对名称以 "UnityEngine." 开头的程序集（如 uGUI 的 <c>UnityEngine.UI</c>）自动引用；
    ///   Cinemachine 是普通包程序集，asmdef 不显式引用就编译不过——不要误以为"引用了 UnityEngine 就够"。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleCameraController : MonoBehaviour
    {
        [Header("Cinemachine（2.9.7）")]
        [Tooltip("CinemachineVirtualCamera 组件（Cinemachine 2.x）。依赖 PirateCrew.Runtime.asmdef 对 Cinemachine 程序集的显式引用。")]
        [SerializeField] CinemachineVirtualCamera virtualCamera;

        [Tooltip("虚拟相机的 Follow 目标（空物体）；本脚本平滑移动它来实现跟随/平移。")]
        [SerializeField] Transform cameraTarget;

        [Tooltip("无 Cinemachine 时的回退相机（直接移动其 Transform）。")]
        [SerializeField] Camera fallbackCamera;

        [Header("参数")]
        [Tooltip("panToCharacter 的平滑速度（1/s）。")]
        [SerializeField] float focusLerpPerSecond = 6f;

        [Tooltip("启用时给虚拟相机设置的优先级（Brain 会自动选优先级最高的 vcam）。")]
        [SerializeField] int activePriority = 20;

        Transform _focusTarget;
        Vector3 _goalPosition;

        /// <summary>当前相机聚焦参考点（供 panToCharacter 的"离相机中心最近"判定）。</summary>
        public Vector3 FocusPoint
        {
            get
            {
                if (cameraTarget != null)
                    return cameraTarget.position;
                if (fallbackCamera != null)
                    return fallbackCamera.transform.position;
                return transform.position;
            }
        }

        void Awake()
        {
            ConfigureVirtualCamera();
        }

        void OnEnable()
        {
            EventBus.Subscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
        }

        void LateUpdate()
        {
            if (_focusTarget != null)
                _goalPosition = _focusTarget.position;

            float t = 1f - Mathf.Exp(-focusLerpPerSecond * Time.deltaTime);

            if (cameraTarget != null)
                cameraTarget.position = Vector3.Lerp(cameraTarget.position, _goalPosition, t);
            else if (fallbackCamera != null)
                fallbackCamera.transform.position = Vector3.Lerp(fallbackCamera.transform.position, _goalPosition, t);
        }

        /// <summary>聚焦到某 Transform（回合开始 / 选中角色时调用）。</summary>
        public void FocusOn(Transform target)
        {
            if (target == null)
                return;

            _focusTarget = target;
            _goalPosition = target.position;
        }

        /// <summary>聚焦到某世界坐标（一次性）。</summary>
        public void FocusOn(Vector3 worldPosition)
        {
            _focusTarget = null;
            _goalPosition = worldPosition;
        }

        /// <summary>解除跟随（相机停在当前位置）。</summary>
        public void Release()
        {
            _focusTarget = null;
        }

        // ------------------------------------------------------------------
        // Cinemachine 接线（强类型）
        // ------------------------------------------------------------------

        /// <summary>补齐虚拟相机的优先级与 Follow 目标（Follow 只在未手工指定时代填，不覆盖美术/策划配置）。</summary>
        void ConfigureVirtualCamera()
        {
            if (virtualCamera == null)
                return;

            virtualCamera.Priority = activePriority;

            if (virtualCamera.Follow == null && cameraTarget != null)
                virtualCamera.Follow = cameraTarget;
        }

        // ------------------------------------------------------------------
        // EventBus 回调
        // ------------------------------------------------------------------

        void OnTurnStarted(object payload)
        {
            // TurnStartedPayload 已带 PanTarget（§3.2 panToCharacter），直接聚焦。
            if (payload is TurnStartedPayload turn && turn.PanTarget != null)
                FocusOn(turn.PanTarget);
        }

        void OnCameraFocusRequested(object payload)
        {
            if (payload is Transform target)
                FocusOn(target);
        }
    }
}
