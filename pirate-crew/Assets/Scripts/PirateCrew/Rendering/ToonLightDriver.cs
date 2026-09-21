using UnityEngine;

namespace PirateCrew.Rendering
{
    /// <summary>Toon 全局 uniform 的属性 ID（PirateToon.shader 与 PixelationRendererFeature 共用）。</summary>
    public static class ToonShaderGlobals
    {
        public static readonly int ToonLightDirWS = Shader.PropertyToID("_ToonLightDirWS");
        public static readonly int ToonLightColor = Shader.PropertyToID("_ToonLightColor");
        public static readonly int ToonAmbientColor = Shader.PropertyToID("_ToonAmbientColor");
        public static readonly int PixelRTHeight = Shader.PropertyToID("_PixelRTHeight");
        public static readonly int PixelRTWidth = Shader.PropertyToID("_PixelRTWidth");
    }

    /// <summary>
    /// 赛璐璐单光驱动：把场景方向光与环境光下发为 PirateToon 的全局 uniform
    /// （渲染篇 §4.6「单平行光 + 单环境光，烘成静态 uniform，跳过 URP shadow 关键字族」）。
    ///
    /// 【为什么每帧读而不是烘焙进材质】方向「永远左上」是全局风格约定，但试点期要频繁调
    /// 光照方向/颜色/强度看效果——材质内烘焙会让每次调参都要重烘资产。全局 uniform 一处改、
    /// 全部 Toon 材质即时生效，且不产生任何 SRP Batcher 批次问题（全局变量在 CBUFFER 之外）。
    ///
    /// 【M2d 的去向】AmbientDirector 收编光照时（立项任务书 M2d），本驱动的下发逻辑并入
    /// AmbientDirector 或由其取代；试点阶段它只是「场景方向光 → Toon uniform」的透传层。
    ///
    /// 【ExecuteAlways】编辑器 SceneView 也要能预览 Toon 光照（shader 的全局 uniform 默认值
    /// 只是兜底）；SceneView 相机不走 PixelationFeature（被显式排除），故 RT 尺寸在编辑器
    /// 预览里保持 shader 默认 360，描边粗细的编辑器失真可接受（验收一律以 Game 视图/播放器为准）。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("PirateCrew/Toon Light Driver")]
    public sealed class ToonLightDriver : MonoBehaviour
    {
        [Tooltip("环境光兜底色（场景环境模式不是 Flat 时用它；试点提案值，冷调低强度）")]
        [SerializeField] Color fallbackAmbient = new Color(0.22f, 0.24f, 0.30f, 1f);

        Light _sun;

        void Update()
        {
            if (_sun == null)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                    return;
            }

            // 指向光源的方向（光 forward 是照射方向，取负）；左上约定由场景摆位落实。
            Vector3 dir = -_sun.transform.forward;
            Shader.SetGlobalVector(ToonShaderGlobals.ToonLightDirWS, new Vector4(dir.x, dir.y, dir.z, 0f));
            Shader.SetGlobalVector(ToonShaderGlobals.ToonLightColor,
                _sun.color.linear * _sun.intensity);
            Shader.SetGlobalVector(ToonShaderGlobals.ToonAmbientColor,
                RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                    ? RenderSettings.ambientLight.linear
                    : (Color)fallbackAmbient.linear);
        }
    }
}
