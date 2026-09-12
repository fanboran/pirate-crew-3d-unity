// ============================================================================
// PirateOutlinePost.shader —— 海盗军团夺宝 3D / M2 全屏后处理描边
//
// 【设计参照】Godot modules/pirate_crew/shaders/outline_post.gdshader（126 行）
//   它把"选中单位"渲染进一个 SubViewport 得到 mask_texture，再在全屏 quad 上做
//   3x3 Sobel 边缘检测 + 沿边缘切线的流动虚线。
//   本 shader 逐段翻译其 fragment()，差异只在平台写法（见下）。
//
// 【与 Godot 版的对应关系】
//   Godot                                   -> 本 shader
//   texelFetch(mask_texture, coord, 0).a    -> SAMPLE_TEXTURE2D_X(_SelectionMask, sampler_PointClamp, uv).r
//   SCREEN_UV * VIEWPORT_SIZE               -> uv * _ScaledScreenParams.xy
//   step(0.5, alpha) 二值化                  -> 同名 step()
//   sobel_kernel_x/y(pos)=pos.x/(x²+y²)     -> 同名公式（逐字照搬）
//   TIME * dash_speed                       -> _Time.y * _DashSpeed
//   hint_default_white/filter_nearest        -> 由 C# 侧把 mask RT 建为 Point 采样（见 OutlineRendererFeature.cs）
//
// 【Pass 顺序（重要）】
//   Pass 0 "SelectionMask"  ：几何 Pass。把指定 Layer 上的"已选中单位"画成纯白，
//                             写进 C# 分配的 mask RT。由 RendererFeature 用
//                             overrideMaterial + overrideMaterialPassIndex = 0 调用。
//                             其 LightMode = "SRPDefaultUnlit"，以便被 DrawingSettings 命中。
//   Pass 1 "PostOutline"    ：全屏 Pass。由 Blitter.BlitCameraTexture(..., material, 1) 调用。
//                             采样 _BlitTexture（相机颜色）+ _SelectionMask（全局 mask），
//                             Sobel 出边缘后与原画面合成。
//
// 【_DebugMode 分档】逐字继承 Godot outline_post.gdshader 的 debug_mode：
//   // [DEBUG] 档 0 正常虚线描边（最终效果）
//   // [DEBUG] 档 1 原始 mask（未二值化，白=选中）
//   // [DEBUG] 档 2 二值化 mask（step(0.5) 之后）
//   // [DEBUG] 档 3 Sobel 边缘强度灰度图（越亮=边缘越强）
//   // [DEBUG] 档 4 纯边缘 mask（阈值后，无虚线）
//   注意：本环境无渲染路径，实际逐层截图待有图形界面的编辑器会话补跑。
//
// 【[CHECK] 本地 URP/Core 包核对结果】
//     Core.hlsl                                     -> URP ShaderLibrary/Core.hlsl
//     Runtime/Utilities/Blit.hlsl                   -> core 包（提供 _BlitTexture / Varyings / Vert / GlobalSamplers）
//     SAMPLE_TEXTURE2D_X / TEXTURE2D_X              -> core ShaderLibrary/Common.hlsl
//     sampler_PointClamp / sampler_LinearClamp      -> core ShaderLibrary/GlobalSamplers.hlsl（经 Blit.hlsl 引入）
//     _ScaledScreenParams                           -> URP ShaderLibrary/Input.hlsl:92
//     _Time                                         -> URP ShaderLibrary/UnityInput.hlsl:40
//     TransformObjectToHClip                        -> core ShaderLibrary/SpaceTransforms.hlsl:108
//   注：core 14.0.12 的 ShaderLibrary/ 下**没有** Blit.hlsl；正确路径是
//       Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl
//       （依据 URP 自带 Shaders/Utils/Blit.shader 的实际 include 行）。
// ============================================================================

Shader "PirateCrew/PirateOutlinePost"
{
    Properties
    {
        // 选中描边色，默认对应 Godot #49d9d6f2
        _OutlineColor   ("选中描边色 #49d9d6", Color) = (0.286, 0.851, 0.839, 0.949)
        // Sobel 边缘强度阈值：越大描边越细/越少。Godot 默认 0.2
        _EdgeThreshold  ("边缘阈值（越大描边越少）", Range(0.01, 0.9)) = 0.2
        _DashLength     ("虚线长度(像素)", Range(2.0, 20.0)) = 8.0
        _DashGap        ("虚线间隔(像素)", Range(2.0, 20.0)) = 6.0
        _DashSpeed      ("虚线流动速度", Range(0.0, 20.0)) = 5.0
        // 调试档：0 正常 / 1 原始mask / 2 二值mask / 3 Sobel灰度 / 4 纯边缘
        _DebugMode      ("调试模式 0=正常 1=原始mask 2=二值mask 3=Sobel 4=纯边缘", Range(0.0, 4.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        // ====================================================================
        // Pass 0 / SelectionMask：把选中单位画成纯白，写进 mask RT
        // 用 overrideMaterialPassIndex = 0 指定本 Pass。
        // ====================================================================
        Pass
        {
            Name "SelectionMask"
            // 必须落在 RendererFeature 的 shaderTagIdList 里，否则 DrawingSettings 不会命中这个 Pass。
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // 关键：mask 只取"选中单位投影的并集"，不需要深度排序，
            // 故 ZWrite Off + ZTest Always。这同时避免了写脏相机深度缓冲
            // （若绑定相机深度目标又 ZWrite On，会破坏后续透明物体的深度测试）。
            ZWrite Off
            ZTest Always
            Cull Back
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex   MaskVertex
            #pragma fragment MaskFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _EdgeThreshold;
                float  _DashLength;
                float  _DashGap;
                float  _DashSpeed;
                float  _DebugMode;
            CBUFFER_END

            struct AttributesMask { float4 positionOS : POSITION; };
            struct VaryingsMask   { float4 positionCS : SV_POSITION; };

            VaryingsMask MaskVertex(AttributesMask IN)
            {
                VaryingsMask OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            // 纯白 = "这里有一个选中单位"。RGB 与 A 都写 1，
            // 全屏 Pass 采样 .r（Godot 采样的是 .a，两边等价，见文件头差异表）。
            half4 MaskFragment(VaryingsMask IN) : SV_Target
            {
                return half4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 1 / PostOutline：全屏 Sobel + 虚线，与相机颜色合成
        // 由 Blitter.BlitCameraTexture(cmd, src, dst, material, 1) 调用。
        // ====================================================================
        Pass
        {
            Name "PostOutline"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert     // 来自 core Blit.hlsl 的全屏三角形顶点着色器
            #pragma fragment PostFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // [CHECK] 注意路径在 Runtime/Utilities/ 而非 ShaderLibrary/（core 14.0.12 实测）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 选中掩码：由 RendererFeature 通过 cmd.SetGlobalTexture("_SelectionMask", ...) 每帧绑定。
            TEXTURE2D_X(_SelectionMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _EdgeThreshold;
                float  _DashLength;
                float  _DashGap;
                float  _DashSpeed;
                float  _DebugMode;
            CBUFFER_END

            // 采样 mask（最近邻，避免 Sobel 被插值糊掉；对应 Godot filter_nearest）
            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_SelectionMask, sampler_PointClamp, uv).r;
            }

            // 二值化（对应 Godot sample_binary）
            float SampleMaskBinary(float2 uv)
            {
                return step(0.5, SampleMask(uv));
            }

            half4 PostFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 srcColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // 二值化 mask（对应 Godot 的 sample_binary），供后续分支使用。
                float maskBinary = SampleMaskBinary(uv);

                // [DEBUG] 档 1：原始 mask。预期画面：白=选中单位，边缘可能有轻微灰阶过渡；
                //              若整屏全黑 -> mask Pass 没画进去（Layer/LightMode Tag 没命中），
                //              若整屏全白 -> mask RT 没被 Clear，或 maskLayer 设成了 Everything。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(SampleMask(uv).xxx, 1.0);

                // [DEBUG] 档 2：二值化 mask。预期画面：选中单位变成纯白剪影，其余全黑。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(maskBinary.xxx, 1.0);

                // 掩码内部不是"外轮廓"：直接透出原画面。
                // （Godot 此处是 discard；本实现改返回原色，使调试截图语义更直观，已在文档标注差异。）
                if (maskBinary > 0.5)
                    return half4(srcColor, 1.0);

                // ---- 3x3 Sobel（逐字翻译 Godot 的 sobel_kernel_x/y）----
                float2 texel = 1.0 / _ScaledScreenParams.xy;
                float edgeX = 0.0;
                float edgeY = 0.0;
                float kernelAbsSum = 0.0;

                for (int j = -1; j <= 1; ++j)
                {
                    for (int i = -1; i <= 1; ++i)
                    {
                        if (i == 0 && j == 0)
                            continue;

                        float denom = float(i * i + j * j);
                        float kx = float(i) / denom;
                        float ky = float(j) / denom;
                        kernelAbsSum += abs(kx);

                        float m = SampleMaskBinary(uv + float2(i, j) * texel);
                        edgeX += m * kx;
                        edgeY += m * ky;
                    }
                }

                float edge = sqrt(edgeX * edgeX + edgeY * edgeY) / max(kernelAbsSum, 1e-5);

                // [DEBUG] 档 3：Sobel 边缘强度灰度图。预期画面：黑色背景上一圈灰白轮廓，
                //              越白=边缘越强。若无任何亮线 -> mask 为空（先看档 1/2 定位）。
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                {
                    float v = smoothstep(0.0, 0.5, edge);
                    return half4(v.xxx, 1.0);
                }

                // smoothstep 阈值（对应 Godot edge_mask）
                float edgeMask = smoothstep(_EdgeThreshold - 0.05, _EdgeThreshold + 0.05, edge);
                if (edgeMask < 0.001)
                    return half4(srcColor, 1.0);

                float alpha = _OutlineColor.a * edgeMask;

                // [DEBUG] 档 4：纯边缘 mask（阈值后、无虚线）。预期画面：连续青色实线轮廓。
                //              若线条断续 -> _EdgeThreshold 偏高或 mask 分辨率不足。
                // [档 0] 正常最终效果：在边缘上再乘一层沿边缘切线流动的虚线。
                if (_DebugMode <= 0.5)
                {
                    // 沿边缘切线切虚线（对应 Godot：tangent = normalize(vec2(-edge_y, edge_x))）
                    float2 tangent = normalize(float2(-edgeY, edgeX) + float2(1e-6, 1e-6));
                    float2 px = uv * _ScaledScreenParams.xy;
                    float s = dot(px, tangent);

                    float period = max(_DashLength + _DashGap, 1e-3);
                    float phase = s / period - _Time.y * _DashSpeed / period;
                    float segPos = frac(phase);

                    float segCenter = 0.5 * _DashLength / period;
                    float halfLen = 0.5 * _DashLength / period;
                    float r = 0.5 * min(_DashLength, _DashGap) / period;
                    float d = abs(segPos - segCenter) - (halfLen - r);
                    float dash = 1.0 - smoothstep(0.0, max(r, 1e-4), d);

                    alpha *= dash;
                }

                if (alpha < 0.002)
                    return half4(srcColor, 1.0);

                // 把描边色叠到原画面上（源色保持，避免 HDR 亮度被拉低）。
                float3 outColor = lerp(srcColor, _OutlineColor.rgb, saturate(alpha));
                return half4(outColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
