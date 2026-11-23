// 屏后描边线（PostLine）Pass3：把描边遮罩合成进低分辨率画布色。
// 规格：docs/技术/渲染/调研/PostLine规格.md §2.3；
// 源级参照 external/ref/unity-isometric-pixel-pipeline 的 CompositeShader.shader
//（MIT License, Copyright (c) 2026 bababuyyy——移植按其许可保留声明）。
//
// 【公式】result = lerp(画布色.rgb, 遮罩.rgb, 遮罩.a)，alpha 恒 1——遮罩 alpha 直接当混合因子：
//         剪影像素被替换成"压暗的最近邻色"，棱线像素按权重混入"提亮的中心色"，其余像素原样。
// 【为什么两张图同尺寸】画布色与遮罩都是画布分辨率，uv 1:1 对齐，采样落在纹素中心——
//         遮罩 RT 的 Point 过滤与此处采样器的过滤方式不影响结果（不串色）。
Shader "Hidden/PirateCrew/PostLineComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形与 uv 口径由官方 Blit.hlsl 提供（Vert / Varyings / _BlitTexture）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Pass2 的产物，feature 经 cmd.SetGlobalTexture 注入（名字契约见 PostLinePath.OutlineTextureId）。
            TEXTURE2D(_PostLineOutlineTexture);
            SAMPLER(sampler_PostLineOutlineTexture);

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // 低分辨率画布色（本趟 blit 源）。
                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                // 描边遮罩（RGB = 线色，A = 强度）。
                half4 outline = SAMPLE_TEXTURE2D(_PostLineOutlineTexture, sampler_PostLineOutlineTexture, uv);

                half3 result = lerp(color.rgb, outline.rgb, outline.a);
                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
