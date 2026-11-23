// 屏后描边线（PostLine）Pass4：低分辨率合成结果锐利放大到全分辨率相机色目标。
// 规格：docs/技术/渲染/调研/PostLine规格.md §2.4；
// 源级参照 external/ref/unity-isometric-pixel-pipeline 的 SharpUpscaleShader.shader
//（MIT License, Copyright (c) 2026 bababuyyy——移植按其许可保留声明）。
//
// 【公式（逐行对应规格 §2.4）】
//   uv      = texcoord + 平移补偿.xy                 // 亚纹素滚动补偿（相机 snap 第二步）
//   px      = 画布纹素（1/宽, 1/高）
//   fw      = clamp(fwidth(uv) / px, 1e-5, 1)        // 屏幕像素 : 画布纹素的密度比（≤1）
//   grid    = uv / px - 0.5 * fw
//   blend   = smoothstep(1 - fw, 1, frac(grid))      // 纹素边界处的平滑窗口
//   finalUV = (floor(grid) + 0.5 + blend) * px      // 块内取中心、边界处过渡
//   color   = SAMPLE_LOD(源, LinearClamp, finalUV, 0)
// 【性质】整数倍放大（本仓 2:1）时 fw = 1/k，过渡窗口宽恰为 1 屏幕像素——介于 Point（k=1 退化）
//         与 Bilinear 之间的"数学上平滑的锐利边"。平移补偿非纹素整数倍，产生亚纹素级滚动：
//         像素网格静止、画面内容平滑移动（规格 §2.4 / §7.1）。
//
// 【与参照库的移植差异】
//  · 平移补偿量：参照叫 _PixelPanOffset；本仓接 rig 预留的 _PixelartSnapOffsetUv
//   （PixelartPath.SnapOffsetUvId，规格 §10"避免双全局"）。未下发时全局为 0 = 不补偿。
//  · 纹素尺寸：参照读 _BlitTexture_TexelSize；本仓从画布尺寸全局推（与 Outline 同理由：
//    Blitter 不保证填充该伴生量；本趟 blit 源即画布尺寸的合成图，数值等价）。
Shader "Hidden/PirateCrew/PostLineSharpUpscale"
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

            // 亚纹素平移补偿（xy，画布 uv 空间；名字契约见 PostLinePath.PanOffsetId）。
            float4 _PixelartSnapOffsetUv;

            // 画布尺寸（feature 每帧下发；名字契约见 PostLinePath.CanvasWidthId/CanvasHeightId）。
            float _PixelartRTWidth;
            float _PixelartRTHeight;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord + _PixelartSnapOffsetUv.xy;

                float2 px = float2(1.0 / _PixelartRTWidth, 1.0 / _PixelartRTHeight);

                float2 fw = clamp(fwidth(uv) / px, 1e-5, 1.0);
                float2 grid = uv / px - 0.5 * fw;
                float2 blend = smoothstep(1.0 - fw, float2(1.0, 1.0), frac(grid));
                float2 finalUV = (floor(grid) + 0.5 + blend) * px;

                // LinearClamp + 显式 LOD0：块内只取中心、边界 1 屏幕像素内线性过渡。
                return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, finalUV, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
