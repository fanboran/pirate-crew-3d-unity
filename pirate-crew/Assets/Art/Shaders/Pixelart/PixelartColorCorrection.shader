// ============================================================================
// PixelartColorCorrection.shader —— 帧级调色板（P5）：**LUT 最近邻映射（就地一趟）**
//
// 【它在做什么】把已经合成好的画面**逐像素**换成调色板上最近的那个色——
//   这就是"帧级调色板"的物理含义：整幅图只许出现板上的颜色（量化后的观感），
//   与逐物体的色带量化是两件事（后者在着色那四趟里已经做完了）。
//
// 【采样公式 = v3 `Shaders/Blit/ColorCorrection.shader:35-40`，逐行】：
//     c = LinearToSRGB(画面色)                    // LUT 的索引域是"显示空间"（sRGB 编码）
//     c = clamp(c, 0.001, 0.999)                  // 贴边时点采样会落到相邻纹素/越界
//     uv_lut = float2(c.r / res + floor(c.b * res) / res, 1.0 - c.g)
//     color  = LUT.Sample(point, uv_lut).rgb       // 命中板上**某一个色**（点采样，不做插值混色）
//   其中 `res` = LUT 的高度（= 烘焙时的分辨率），与 `PaletteGenerationCIEDE.compute` 的
//   输出布局互为逆（见那个 compute 的文件头）。
//
// 【LUT 分辨率从哪来——**不为它新造一个 shader 全局**】契约 §2.2 的全局表里只有
//   `_PixelartPaletteLut`，没有"调色板分辨率"这一项；v3 是另发一个 `_Resolution` 全局。
//   本仓不签新名字，直接用 HLSL 的 `Texture2D.GetDimensions` 从纹理本身取（LUT 高度 = res）。
//   ⚠ 这是本文件**唯一**需要"真实渲染路径验证"的点：`GetDimensions` 在 D3D11/SM4.5+
//   与 GLES3 都成立，但它不像采样那样被本仓其它 shader 用过。
//   若首轮出图报编译错，退路是：由 C# 每帧多下发一个全局（名字需先进契约表），
//   或把 res 编成 `_PixelartPaletteLut_TexelSize` 的用法——**不要**改成从 `_ScreenParams` 推。
//
// 【为什么本趟是"就地"却安全】源与目标都是 Cast 相机的颜色目标；同一个资源同时当 SRV 与 RTV
//   在 D3D11 上是非法绑定（会静默读到零/脏值）。故 C# 侧（`PixelartColorCorrectionFeature`）
//   先把颜色目标拷到一张临时 RT，再让本 pass 从 `_BlitTexture`（= 那张临时 RT，由
//   `Blitter.BlitTexture` 自动绑定）写回颜色目标。
//
// 【本仓与 v3 的一处差异】v3 的 `_Palette` 是 `Texture2D` + `int _Resolution`；
//   本仓的 LUT 走全局纹理 `_PixelartPaletteLut`（由 Feature 下发），分辨率自取自纹理。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartColorCorrection"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // 【契约 §2.1】名字固定，Feature 按名字找 pass。
            Name "PixelartColorCorrection"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex   Vert
            #pragma fragment PixelartColorCorrectionFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形 + `_BlitTexture` / `_BlitScaleBias` / 全局采样器（`sampler_PointClamp`）
            // 由官方 Blit.hlsl 提供。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 帧级调色板 LUT（res×res 宽 × res 高；由 PixelartPalette.BuildLut 烘、Feature 下发）。
            TEXTURE2D(_PixelartPaletteLut);     SAMPLER(sampler_PixelartPaletteLut);

            half4 PixelartColorCorrectionFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // LUT 高度 = 烘焙分辨率（契约 §4.5：2D RGB 条带，尺寸与采样公式照 v3）。
                uint lutWidth, lutHeight;
                _PixelartPaletteLut.GetDimensions(lutWidth, lutHeight);
                float res = max((float)lutHeight, 1.0);

                // 源画面 = 上屏前的低分辨率结果（线性值写在非 sRGB 的颜色目标里，
                // 显示时由屏幕写入端做一次 linear→sRGB —— 这正是下面索引域要 sRGB 的原因）。
                float3 outputColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                outputColor = LinearToSRGB(outputColor);
                outputColor = clamp(outputColor, 0.001, 0.999);

                float2 targetCoord = float2(outputColor.r / res + floor(outputColor.b * res) / res,
                                            1.0 - outputColor.g);

                // 点采样：命中板上某一个色（不插值 ⇒ 画面唯一色数 ≤ 调色板色数，验收判据之一）。
                outputColor = SAMPLE_TEXTURE2D(_PixelartPaletteLut, sampler_PixelartPaletteLut, targetCoord).rgb;

                return half4(outputColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
