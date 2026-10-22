// ============================================================================
// SelectionCube.shader —— 选中单位立方体角框的**全分辨率叠加**材质
//
// 【为什么不在像素化域】角框要求 1 屏幕像素线宽。像素化域在 540p 艺术画布上光栅化，
// 再 2× 上屏，1 艺术像素 = 2 屏幕像素；要 1 屏幕像素只能走全分辨率叠加相机
// （PixelartCameraRig 的 Overlay 相机，渲染器 PixelartOverlay_Renderer）。
//
// 【叠加层的覆盖关系】Overlay 相机清深度、不读场景深度 ⇒ 角框画在所有场景内容之上，
// 8 个角全部可见（红色警戒式选中框）。这是选中 UI 的预期语义，不是遮挡 bug；
// 若要"被地形/角色挡住"的版本，需要在这里手动采样 _PixelartDepthBuffer 做逐像素深度比较。
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
