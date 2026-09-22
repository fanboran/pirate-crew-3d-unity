using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗相机输入的一次采样（纯数据）。<see cref="CameraInputReader"/> 只读 Input、
    /// 只产出本结构；消费与落相机全在 <see cref="BattleCameraDriver"/>。
    /// </summary>
    public struct CameraInputFrame
    {
        /// <summary>方位角增量（度）：右键拖拽（玩家输入）；观察模式激活时鼠标位移也会再加一份（实机等价的双份累加）。</summary>
        public float YawDeltaDegrees;

        /// <summary>观察模式俯仰增量（度，未夹紧——夹紧在 Driver 的状态上做）。</summary>
        public float ObservePitchDeltaDegrees;

        /// <summary>观察模式飞行位移（世界单位，已乘 dt；WASD 沿相机水平朝向、Space/Shift 升降）。</summary>
        public Vector3 ObserveFlyDelta;

        /// <summary>中键点击（边沿触发）：请求切换自由锚。</summary>
        public bool FreeAnchorToggleRequested;
    }

    /// <summary>
    /// 战斗相机**输入读取器**（三件套之二）：只读 legacy Input、只出增量，不写任何相机状态。
    ///
    /// 【玩家输入只有一条】右键拖拽 = 方位角环绕（创始人裁决 2026-09-22：俯角锁 30°、
    /// 左键是选人/瞄准，不承担旋转；滚轮缩放已随 2026-09-23 裁决退役——此路没有输入）。
    ///
    /// 【标注的调试/辅助出口】中键自由锚与观察模式（我的世界式 WASD 飞行 + 自由俯仰）
    /// 是 r12 用户反馈留下的辅助手段（HUD 模式 3 依赖它），**允许破坏等距口径**；
    /// 若创始人裁决删除，删本类的观察分支 + Driver 的锚状态即可，玩家输入路径不受影响。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraInputReader : MonoBehaviour
    {
        [Tooltip("环绕/观察遗留的鼠标灵敏度（度/单位 Mouse X）。")]
        [SerializeField] float orbitDegreesPerMouseUnit = 3f;

        [Tooltip("观察模式：鼠标 Y → 俯仰的灵敏度系数（实机值 0.35）。")]
        [SerializeField] float observePitchPerMouseUnit = 0.35f;

        [Tooltip("观察模式飞行速度（世界单位/秒）。")]
        [SerializeField] float observeFlySpeed = 12f;

        /// <summary>环绕灵敏度（camdiag 诊断读数用）。</summary>
        public float OrbitDegreesPerMouseUnit => orbitDegreesPerMouseUnit;

        /// <summary>
        /// 采样一次输入。**拉模型**：由 Driver 在自己的 Update 里调用（不用 MonoBehaviour.Update，
        /// 消费顺序确定、没有双 Update 的帧序抖动）。<paramref name="observeActive"/> = 观察模式是否激活；
        /// <paramref name="cameraTransform"/> = 主相机 Transform（观察飞行的水平基向量来源）。
        /// </summary>
        public CameraInputFrame Sample(bool observeActive, Transform cameraTransform)
        {
            var frame = new CameraInputFrame();

            // 右键拖拽环绕 = 只改方位角（俯角无输入路径）。观察模式下鼠标位移会再累加一份
            // ——与原控制器两条 if 顺序执行的行为逐位等价。
            float mouseX = Input.GetAxis("Mouse X");
            if (Input.GetMouseButton(1))
                frame.YawDeltaDegrees += mouseX * orbitDegreesPerMouseUnit;
            if (observeActive)
                frame.YawDeltaDegrees += mouseX * orbitDegreesPerMouseUnit;

            if (Input.GetMouseButtonDown(2))
                frame.FreeAnchorToggleRequested = true;

            if (observeActive)
            {
                frame.ObservePitchDeltaDegrees = -Input.GetAxis("Mouse Y") * observePitchPerMouseUnit;
                frame.ObserveFlyDelta = ObserveFlyDelta(cameraTransform);
            }

            return frame;
        }

        /// <summary>观察模式飞行：WASD 沿相机水平朝向平移、Space 升 / Shift 降（我的世界创造式）。</summary>
        Vector3 ObserveFlyDelta(Transform cameraTransform)
        {
            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float side = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float up = (Input.GetKey(KeyCode.Space) ? 1f : 0f)
                - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1f : 0f);

            if (fwd == 0f && side == 0f && up == 0f)
                return Vector3.zero;

            Vector3 flatFwd = Vector3.Scale(cameraTransform.forward, new Vector3(1f, 0f, 1f)).normalized;
            Vector3 flatRight = Vector3.Scale(cameraTransform.right, new Vector3(1f, 0f, 1f)).normalized;

            return (flatFwd * fwd + flatRight * side + Vector3.up * up)
                * (observeFlySpeed * Time.deltaTime);
        }
    }
}
