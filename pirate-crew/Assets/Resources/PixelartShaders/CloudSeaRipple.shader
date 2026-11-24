// ============================================================================
// CloudSeaRipple.shader —— 云端漫步涟漪：水面 / 水洼上扩散的像素同心环（透明、不写深度）。
//
// 【画法】单位方片（半径 1）贴在水面略上方，片元里算到中心的距离 r，画 1~2 道硬边环：
//   主环半径 = 扩散进度，后面跟一道更淡的副环；渐隐按档位离散跳变，不做连续淡出。
//   环宽以艺术像素计，环边在艺术像素中心求值（CloudSeaCommon.hlsl）——是一圈干净的像素
//   阶梯，不是抗锯齿的圆弧。
//
// 【两种驱动】
//   · 自动循环（_AutoLoop）：相位全在 shader 里走 _Time，零脚本开销，适合常驻的滴水点；
//   · 脚本驱动（关掉 _AutoLoop）：脚本每帧写 _Progress（0 → 1），适合一次性事件（海鸥掠水）。
//
// 【用在哪】云瀑落水点、海面 / 云台水洼的雨点。本作没有海岸线，不做出岸浪花——水面的
//   「动态点缀」全部由同心环承担。
//
// 【调参速查】环的粗细 ↔ _RingWidth；扩散多快 ↔ _LoopPeriod；淡得多快 ↔ _FadeSteps；
//   环的最大半径改脚本的半径参数（本材质的 _Radius 跟着同步）。
// ============================================================================

Shader "PirateCrew/CloudSea/CloudSeaRipple"
{
    Properties
    {
        [Header(Ring)]
        _RingColor ("环色", Color) = (0.92, 0.99, 0.98, 1)
        _RingAlpha ("环不透明度", Range(0.0, 1.0)) = 0.80
        _RingWidth ("环宽（艺术像素）", Range(1, 4)) = 1.5
        _RingGap ("副环间距（占最大半径）", Range(0.05, 0.5)) = 0.22
        _TrailAlpha ("副环不透明度（相对主环）", Range(0.0, 1.0)) = 0.55
        _FadeSteps ("渐隐档数（1 = 不淡出）", Range(1, 6)) = 3
        _Radius ("环最大世界半径（米，脚本按缩放同步）", Float) = 0.7

        [Header(Drive)]
        [ToggleUI] _AutoLoop ("自动循环（关 = 读扩散进度）", Float) = 1
        _Progress ("扩散进度 0 到 1（脚本驱动）", Range(0.0, 1.0)) = 0
        _LoopPeriod ("自动循环周期（秒）", Range(0.5, 10)) = 1.6
        _LoopGap ("环消失后的空档（占周期比例，0 = 无缝重起）", Range(0.0, 2.0)) = 0.7
        _Phase ("自动循环相位（0~1）", Range(0.0, 1.0)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            // Transparent + 1：排在海面（3000）之后画，不被水色盖住。
            "Queue" = "Transparent+1"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 【SRP Batcher】材质属性的唯一 cbuffer。
        CBUFFER_START(UnityPerMaterial)
            half4 _RingColor;
            float _RingAlpha;
            float _RingWidth;
            float _RingGap;
            float _TrailAlpha;
            float _FadeSteps;
            float _Radius;

            float _AutoLoop;
            float _Progress;
            float _LoopPeriod;
            float _LoopGap;
            float _Phase;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardTransparent"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "CloudSeaCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;   // 物体空间位置：方片的 XZ 就是单位半径坐标
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionOS = input.positionOS.xyz;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 取样板：环的半径在艺术像素中心求值 → 环边是干净的像素阶梯。
                float2 dpx = CloudSeaArtPixelCentre(input.positionCS.xy) - input.positionCS.xy;
                float2 os = input.positionOS.xz
                    + mul(dpx, float2x2(ddx(input.positionOS.xz), ddy(input.positionOS.xz)));
                float r = length(os);

                // 自动循环：进度 0→1 占周期的 1/(1+空档)，其余时间进度 > 1 = 环已出画，留一段空档再重起。
                float progress = _AutoLoop > 0.5
                    ? frac(_Time.y / max(_LoopPeriod, 0.05) + _Phase) * (1.0 + _LoopGap)
                    : saturate(_Progress);

                // 环宽从艺术像素坐标换算到单位半径（方片半径 1 = _Radius 米）。
                // 全局缺失时的兜底：1 艺术像素 = 1/24 米。
                float unit = _PixelartUnitSize > 0.0 ? _PixelartUnitSize : (1.0 / 24.0);
                float halfWidth = saturate(0.5 * _RingWidth * unit / max(_Radius, 0.01));

                float mainRing = 1.0 - step(halfWidth, abs(r - progress));
                float trailRadius = progress - _RingGap;
                float trailRing = (1.0 - step(halfWidth, abs(r - trailRadius)))
                    * step(0.0, trailRadius) * _TrailAlpha;

                // 渐隐分档：扩散全程只跳几档不透明度。
                float fade = 1.0 - floor(progress * _FadeSteps) / _FadeSteps;
                float alpha = max(mainRing, trailRing) * fade * _RingAlpha * step(progress, 1.0);
                clip(alpha - 0.001);   // 环外 / 空档期整片裁掉，不浪费透明混合
                return half4(_RingColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
