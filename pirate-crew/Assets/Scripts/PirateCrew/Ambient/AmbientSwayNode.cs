using UnityEngine;

namespace PirateCrew.Ambient
{
    /// <summary>摆动模式。</summary>
    public enum AmbientSwayMode
    {
        /// <summary>悬挂摆动（灯笼绕挂点两轴摆）。</summary>
        Pendulum = 0,

        /// <summary>随波起伏（浮标上下浮）。</summary>
        Bob = 1,
    }

    /// <summary>
    /// 通用环境摆动节点：悬挂物（灯笼）摆动 / 水面浮标起伏 / 辉光贴片朝向相机。
    ///
    /// 【为什么做成通用小组件】灯笼、浮标、燕尾旗挂点、远景剪影都是"少量对象的低频正弦运动"，
    /// 共用一个组件比给每类写一个专用组件更省代码，也保证只有一条时间源（相位不会互相漂移）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientSwayNode : MonoBehaviour
    {
        AmbientSwayMode _mode;
        float _primaryAmplitudeDeg;
        float _secondaryAmplitudeDeg;
        float _angularSpeed;
        float _phase;
        float _bobAmplitude;

        Vector3 _basePosition;
        Quaternion _baseRotation;

        Transform _billboard;

        float _time;

        /// <summary>绑定参数。</summary>
        public void Configure(AmbientSwayMode mode, float angularSpeed, float phase,
            float primaryAmplitudeDeg, float secondaryAmplitudeDeg, float bobAmplitude,
            Transform billboard)
        {
            _mode = mode;
            _angularSpeed = angularSpeed;
            _phase = phase;
            _primaryAmplitudeDeg = primaryAmplitudeDeg;
            _secondaryAmplitudeDeg = secondaryAmplitudeDeg;
            _bobAmplitude = bobAmplitude;
            _billboard = billboard;
            // 初始相位由 _phase 承担（Tick 里 phase = _phase + _time * _angularSpeed）。
            // 【_time 不能预存 phase】Tick 又会按 _phase 起算，预存会让初相位变成
            // phase × (1 + angularSpeed)（双重计入），绑定时摆动/起伏全都不在预期相位上。
            _time = 0f;

            _basePosition = transform.position;
            _baseRotation = transform.localRotation;
        }

        /// <summary>推进一帧。<paramref name="camera"/> 可为 null（无相机则不旋转辉光片）。</summary>
        public void Tick(float dt, Camera camera)
        {
            if (dt <= 0f)
                return;

            _time += dt;
            float phase = _phase + _time * _angularSpeed;

            if (_mode == AmbientSwayMode.Bob)
            {
                float y = Mathf.Sin(phase) * _bobAmplitude;
                transform.position = _basePosition + new Vector3(0f, y, 0f);
            }
            else
            {
                // 悬挂摆动：绕挂点（节点自身原点）两轴小幅摆，第二个轴低频错相 → 读作"被风带动"。
                float a = Mathf.Sin(phase) * _primaryAmplitudeDeg;
                float b = Mathf.Sin(phase * 0.73f + 1.1f) * _secondaryAmplitudeDeg;
                transform.localRotation = _baseRotation * Quaternion.Euler(a, 0f, b);
            }

            // 辉光片朝向相机（低模场景里"灯"的读法：壳 + 一张朝向相机的亮片）。
            if (_billboard != null && camera != null)
            {
                Vector3 toCamera = _billboard.position - camera.transform.position;
                if (toCamera.sqrMagnitude > 1e-6f)
                    _billboard.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }
        }
    }
}
