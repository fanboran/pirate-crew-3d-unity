using PirateCrew.Rendering.Pixelart;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步的主光硬阴影开关（挂 CloudWalk 预制体根）：启用时把主光切到硬阴影、阴影强度
    /// 拉满，停用时原样还回去。
    ///
    /// 【为什么是运行时组件、不是写进灯光资产】主光由战斗装配链生成（<c>BattleSceneLighting
    /// </c>CreateDirectionalLight</c>：白光、无阴影），重跑装配第 ① 步会原样重建。开关跟着
    /// 场景件走，只在云端漫步这一关生效，别的关卡的光照口径不动。
    ///
    /// 【阴影强度必须是 1】海面 / 海床 / 水下物都按阴影衰减 step 硬切成「阳光 / 岛影」两档；
    /// 衰减不到 0 就会被整片误判成阳光（装配链给的 0.65 正是这种值）。
    ///
    /// 【灯从哪来】与装配器的光照下发同源：<see cref="PixelartCameraRig.sun"/>，没有则退
    /// <see cref="RenderSettings.sun"/>。不用任何 Find 系列全场景搜索（SceneLookupGate）。
    /// 装配顺序不保证本组件启用时灯已就位——拿不到就下一帧再试，拿到为止。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudSeaSunShadow : MonoBehaviour
    {
        [Tooltip("阴影强度（岛影两档靠硬切判定，保持 1）")]
        [SerializeField, Range(0f, 1f)] float shadowStrength = 1f;

        [Tooltip("拿不到主光时最多重试几秒（之后放弃并告警一次）")]
        [SerializeField, Range(0f, 60f)] float resolveTimeoutSeconds = 10f;

        Light _light;
        LightShadows _savedShadows;
        float _savedStrength;
        bool _applied;
        float _resolveElapsed;
        bool _resolveWarned;

        /// <summary>已把主光切到硬阴影（灯还没就位时为 false）。</summary>
        public bool Applied => _applied;

        void OnEnable()
        {
            _applied = false;
            _resolveElapsed = 0f;
            Apply();
        }

        void OnDisable()
        {
            Restore();
        }

        void Update()
        {
            if (_applied)
            {
                // 开着的时候只守住强度：别处改了灯光强度也不让岛影二档失效。
                if (_light != null && !Mathf.Approximately(_light.shadowStrength, shadowStrength))
                    _light.shadowStrength = shadowStrength;
                return;
            }

            _resolveElapsed += Time.unscaledDeltaTime;
            if (_resolveElapsed > resolveTimeoutSeconds)
            {
                if (!_resolveWarned)
                {
                    _resolveWarned = true;
                    Debug.LogWarning("[CloudSeaSunShadow] " + resolveTimeoutSeconds
                        + " 秒内没拿到主光（装配器 sun / RenderSettings.sun 都为空）——岛影不会出现，"
                        + "海面整片是阳光水色。", this);
                }
                enabled = false;
                return;
            }

            Apply();
        }

        void Apply()
        {
            Light light = ResolveLight();
            if (light == null)
                return;

            _light = light;
            _savedShadows = light.shadows;
            _savedStrength = light.shadowStrength;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = shadowStrength;
            _applied = true;
        }

        void Restore()
        {
            if (!_applied)
                return;
            _applied = false;

            if (_light != null)
            {
                _light.shadows = _savedShadows;
                _light.shadowStrength = _savedStrength;
            }
            _light = null;
        }

        /// <summary>主光：装配器登记的那盏，没有则退场景环境里登记的太阳。</summary>
        static Light ResolveLight()
        {
            PixelartCameraRig rig = PixelartPath.ActiveRig;
            if (rig != null && rig.sun != null)
                return rig.sun;
            return RenderSettings.sun;
        }
    }
}
