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

        /// <summary>拖拽方位增量（度）。**俯仰不参与**——俯角恒 30°（创始人裁决：拖动旋转俯仰角不变），
        /// 自由飞行（WASD/QE）已随真自由镜头一起退役。</summary>
        public float LookYawDelta;

        /// <summary>拖拽已超过阈值（右键按住且位移 &gt; 阈值——区分"点右键"与"拖拽转方位"）。</summary>
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

        /// <summary>取景换档（Tab，边沿；近景 ⇄ 远景两档切换，交互操作契约 §B17）。</summary>
        public bool ZoomTogglePressed;

        // ---- 标准投掷模组参数意图（-1/0/+1；只该操作中态消费）----

        /// <summary>方向角输入：A=-1（逆时针）/ D=+1（顺时针）。</summary>
        public int ThrowYawInput;

        /// <summary>仰角输入：S=-1（降）/ W=+1（升）。</summary>
        public int ThrowElevationInput;

        /// <summary>力度输入：Shift=-1（降）/ Space=+1（升）。</summary>
        public int ThrowPowerInput;
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
        [Tooltip("拖拽转方位的灵敏度（度/鼠标位移单位），自由/环绕两态共用。")]
        [SerializeField] float lookDegreesPerMouseUnit = 0.25f;

        [Tooltip("拖拽阈值（px）：右键按住位移超过它才算拖拽（区分点按）。")]
        [SerializeField] float orbitDragThresholdPixels = 6f;

        Vector2 _rightPressPosition;
        bool _haveRightPress;

        /// <summary>采样一次输入，产出意图帧。</summary>
        public BattleIntentFrame Sample()
        {
            var frame = new BattleIntentFrame();

            bool lookHeld = Input.GetMouseButton(1);
            if (lookHeld)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    _rightPressPosition = Input.mousePosition;
                    _haveRightPress = true;
                }

                frame.LookYawDelta = Input.GetAxis("Mouse X") * lookDegreesPerMouseUnit;
                frame.OrbitDragHeld = _haveRightPress
                    && Vector2.Distance(Input.mousePosition, _rightPressPosition) >= orbitDragThresholdPixels;
            }
            else
            {
                _haveRightPress = false;
            }

            frame.PrimaryPressed = Input.GetMouseButtonDown(0);
            frame.ConfirmPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            frame.CancelPressed = Input.GetKeyDown(KeyCode.Escape);
            frame.PausePressed = Input.GetKeyDown(KeyCode.P);
            frame.ZoomTogglePressed = Input.GetKeyDown(KeyCode.Tab);

            frame.ThrowYawInput =
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            frame.ThrowElevationInput =
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            frame.ThrowPowerInput =
                (Input.GetKey(KeyCode.Space) ? 1 : 0)
                - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1 : 0);

            return frame;
        }
    }
}
