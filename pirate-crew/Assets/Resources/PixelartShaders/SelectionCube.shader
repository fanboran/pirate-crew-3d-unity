// ============================================================================
// SelectionCube.shader —— 选中单位立方体角框的**全分辨率叠加**材质
//
// 【为什么不在像素化域】角框要求 1 屏幕像素线宽。像素化域在 540p 艺术画布上光栅化，
// 再 2× 上屏，1 艺术像素 = 2 屏幕像素；要 1 屏幕像素只能走全分辨率叠加相机
// （PixelartCameraRig 的 Overlay 相机，渲染器 PixelartOverlay_Renderer）。
//
// 【叠加层的覆盖关系】Overlay 相机清深度、不读场景深度，所以片元里手动采样
// Cast 路径发布的 `_PixelartDepthBuffer` 做逐像素比较：角色身体/地形在前时，
// 后面那几条棱被 clip（等效 ZTest LEqual），角框不再是永远置顶的覆盖层。
//
// 【队列】Queue = Transparent+1（3001）：URP 标准透明趟收件范围是 3001–5000；
// 写 3000 会被整类漏掉。LightMode = UniversalForward 才会被叠加渲染器的透明趟拾取。
// ============================================================================

Shader "PirateCrew/Pixelart/SelectionCube"
{
    Properties
    {
        _Color ("线框色", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+1"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SelectionCube"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 场景细像素深度（Cast 路径在着色前发布；Overlay 相机随后渲染时全局仍在）。
            // Overlay 相机自己的深度被清空，所以 ZTest 恒过；遮挡靠片元里手动比这张深度。
            TEXTURE2D(_PixelartDepthBuffer);        SAMPLER(sampler_PixelartDepthBuffer);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 clipPos    : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.clipPos = output.positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 手动深度遮挡：Overlay 相机没有场景深度附件，因此采样 Cast 路径发布的
                // 细像素深度，等效 ZTest LEqual。角色身体/地形在前时，后面那几条棱被 clip。
                float2 screenUv = input.clipPos.xy / input.clipPos.w;
                float storedRaw = SAMPLE_TEXTURE2D(
                    _PixelartDepthBuffer, sampler_PixelartDepthBuffer, screenUv).r;
                float ndcDepth = input.clipPos.z / input.clipPos.w;
#if defined(UNITY_REVERSED_Z)
                float deviceDepth = ndcDepth;
                clip(deviceDepth - storedRaw);     // reversed-Z：片元比场景近才保留
#else
                float deviceDepth = ndcDepth * 0.5 + 0.5;
                clip(storedRaw - deviceDepth);     // 正向 Z：片元比场景近才保留
#endif
                return _Color;
            }
            ENDHLSL
        }
    }
}
