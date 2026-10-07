using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>
    /// 空岛天外件的**缓慢浮沉**（浮石 / 悬浮晶 / 云 / 鸟）：原地小幅上下 + 侧向漂移 + 轻微摆头。
    ///
    /// 【为什么按件挂而不是整组动】这些件在天上"各有各的相位"，整组同步上下会读成"整座岛在呼吸"；
    /// 幅度/速度/相位全部由构图侧（<c>FloatingIslandComposer</c> 的浮件表）烘进字段，
    /// 运行时**不再掷随机**——同 seed 的岛每次开局都从同一姿态起漂。
    ///
    /// 【幅度口径】"缓慢"= 周期 8-80 秒、幅度 0.2-1.1 米：读得出来在动，又不会像气球。
    /// 物体挂在岛根下、网格顶点已在岛局部坐标里，故本组件只写 localPosition/localRotation 的偏移。
    ///
    /// 【静态批合】本组件所在物体**不设 isStatic**（烘焙侧已保证）——静态批合的物体不响应 Transform。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloatingIslandDrift : MonoBehaviour
    {
        /// <summary>竖直浮动幅度（米）。</summary>
        public float bobAmplitude = 0.5f;

        /// <summary>水平漂移幅度（米）。</summary>
        public float swayAmplitude = 0.3f;

        /// <summary>角速度（弧度/秒）。</summary>
        public float speed = 0.2f;

        /// <summary>相位（弧度）。</summary>
        public float phase;

        /// <summary>摆头幅度（度；绕 Y 的极缓慢偏转，给"悬着"一点活气）。</summary>
        public float yawAmplitudeDegrees = 3f;

        Vector3 _basePosition;
        float _angle;

        void Awake()
        {
            _basePosition = transform.localPosition;
            _angle = phase;
        }

        void Update()
        {
            // 主频 = 竖直浮沉；次频（0.63 倍 + 固定相位）供水平漂移——两个频率不成整数比，
            // 轨迹不闭环，长时间看不会读成"来回平移的机械件"。
            _angle += Time.deltaTime * speed;
            float up = Mathf.Sin(_angle);
            float side = Mathf.Sin(_angle * 0.63f + 1.7f);

            transform.localPosition = _basePosition + new Vector3(
                swayAmplitude * side,
                bobAmplitude * up,
                swayAmplitude * 0.7f * side);

            if (yawAmplitudeDegrees != 0f)
                transform.localRotation = Quaternion.Euler(0f, yawAmplitudeDegrees * side, 0f);
        }
    }
}
