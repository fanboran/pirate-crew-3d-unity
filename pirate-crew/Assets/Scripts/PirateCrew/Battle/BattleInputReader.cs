using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗玩家输入的一次采样（纯数据意图帧）。<see cref="BattleInputReader"/> 只读 legacy Input、
    /// 只产出本结构；**全工程唯一的玩家输入采样点**——交互控制器消费它驱动状态机，
    /// 相机驱动经控制器的 <c>LastIntent</c> 拉取（交互操作契约不变量 3：禁止绕过采样点直读 Input）。
    ///
    /// 【键位语义】每个键在每个交互状态下的含义由交互操作契约 §B 唯一裁决；本结构只承载
    /// 「原始意图」（按住/边沿/增量），不预设任何状态语义。
    /// </summary>
    public struct BattleIntentFrame
    {
        // ---- 相机意图（自由镜头/环绕共用通道，解释权归相机驱动按模式分派）----

        /// <summary>右键是否按住（自由镜头的转视角/飞行修饰键；环绕的拖拽键）。</summary>
        public bool LookHeld;

        /// <summary>转视角方位增量（度，横纵**同一灵敏度**——修旧观察模式 0.35 对 3.0 的 9 倍失配）。</summary>
        public float LookYawDelta;

        /// <summary>转视角俯仰增量（度，向上为正）。</summary>
        public float LookPitchDelta;

        /// <summary>飞行移动意图（归一化：x=平移、y=升降、z=前后；仅 LookHeld 时非零）。</summary>
        public Vector3 FlyMove;

        /// <summary>飞行加速修饰（Shift）。</summary>
        public bool FlyFast;

        /// <summary>环绕拖拽已超过阈值（右键按住且位移 &gt; 阈值——区分"点右键"与"拖拽环绕"）。</summary>
        public bool OrbitDragHeld;

        // ---- 交互意图 ----

        /// <summary>左键按下（边沿）。UI 悬停判定在消费方（控制器）做。</summary>
        public bool PrimaryPressed;

        /// <summary>确认（回车，边沿）。</summary>
        public bool ConfirmPressed;

        /// <summary>取消（Esc，边沿；经交互操作契约 §C 矩阵解析）。</summary>
        public bool CancelPressed;

        /// <summary>暂停（P，边沿；全局键，不经 Esc 矩阵）。</summary>
        public bool PausePressed;

        // ---- 标准投掷模组参数意图（-1/0/+1；只该操作中态消费）----

        /// <summary>方向角输入：A=-1（逆时针）/ D=+1（顺时针）。</summary>
        public int ThrowYawInput;

        /// <summary>仰角输入：S=-1（降）/ W=+1（升）。</summary>
        public int ThrowElevationInput;

        /// <summary>力度输入：Shift=-1（降）/ Space=+1（升）。</summary>
        public int ThrowPowerInput;

        /// <summary>滚轮增量（现役无消费者；模组扩展位，相机永不消费——契约 §B16）。</summary>
        public float ScrollDelta;
    }

    /// <summary>
    /// 战斗输入读取器：**拉模型**——由 <see cref="BattleInteractionController"/> 在自己的 Update 里
    /// 调用 <see cref="Sample"/>（不用 MonoBehaviour.Update，消费顺序确定、无双 Update 帧序抖动）。
    ///
    /// 【规格出处】docs/技术/交互操作契约.md §B（键位表）；灵敏度/速度数值【提案/待定】。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleInputReader : MonoBehaviour
    {
        [Tooltip("转视角灵敏度（度/鼠标位移单位）——方位与俯仰共用同一值（契约 §B1：横纵同灵敏度）。")]
        [SerializeField] float lookDegreesPerMouseUnit = 0.25f;

        [Tooltip("自由镜头飞行速度（m/s，未按 Shift）。")]
        [SerializeField] float flySpeed = 12f;

        [Tooltip("自由镜头飞行加速倍率（按住 Shift）。")]
        [SerializeField] float flyFastScale = 3f;

        [Tooltip("环绕拖拽阈值（px）：右键按住位移超过它才算拖拽（区分点按）。")]
        [SerializeField] float orbitDragThresholdPixels = 6f;

        Vector2 _rightPressPosition;
        bool _haveRightPress;

        /// <summary>采样一次输入，产出意图帧。</summary>
        public BattleIntentFrame Sample()
        {
            var frame = new BattleIntentFrame();

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            frame.LookHeld = Input.GetMouseButton(1);
            if (frame.LookHeld)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    _rightPressPosition = Input.mousePosition;
                    _haveRightPress = true;
                }

                frame.LookYawDelta = mouseX * lookDegreesPerMouseUnit;
                frame.LookPitchDelta = -mouseY * lookDegreesPerMouseUnit;   // 鼠标上推 = 抬视角
                frame.OrbitDragHeld = _haveRightPress
                    && Vector2.Distance(Input.mousePosition, _rightPressPosition) >= orbitDragThresholdPixels;
                frame.FlyMove = ReadFlyMove();
                frame.FlyFast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            else
            {
                _haveRightPress = false;
            }

            frame.PrimaryPressed = Input.GetMouseButtonDown(0);
            frame.ConfirmPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            frame.CancelPressed = Input.GetKeyDown(KeyCode.Escape);
            frame.PausePressed = Input.GetKeyDown(KeyCode.P);

            frame.ThrowYawInput =
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            frame.ThrowElevationInput =
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            frame.ThrowPowerInput =
                (Input.GetKey(KeyCode.Space) ? 1 : 0)
                - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1 : 0);

            frame.ScrollDelta = Input.mouseScrollDelta.y;

            return frame;
        }

        /// <summary>
        /// 自由镜头飞行意图（编辑器飞行式）：WASD 沿相机水平朝向、Q/E 降/升。
        /// 返回**归一化方向**（速度与加速在相机侧换算——保持读取器只出意图不出位移）。
        /// </summary>
        static Vector3 ReadFlyMove()
        {
            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float side = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float up = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);

            var move = new Vector3(side, up, fwd);
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }
    }
}
