// ============================================================================
// PixelartRimLight.shader —— 像素化着色路径的**边缘光累加趟**（艺术画布域）
//
// 【这一趟在干什么】一趟全屏，读"连通域结论 + 逐物体边缘光色 + 覆盖标记 + 墨线标记"，
//   算出本像素的边缘光，写进 `_PixelartRimLightBuffer`（艺术画布）。它是**光照层的一项**：
//   合成趟（`PixelartShading.shader` 的 Combine）把 Diffuse / Specular / GI / **RimLight**
//   四项相加。清空缓冲与 3×3 去孤立点由 `PixelartRimLightFeature` 负责（契约 §4.3）。
//
// 【边缘光 ≠ 墨线，命名不许混】两者正交、只共享"边缘"这个词：
//   · 墨线 = **画上去的线**，标记在 `_PixelartOutlineBuffer.r`，由描边趟写、合成趟取；
//   · 边缘光 = **光照的提亮**，写在本趟的缓冲里，是相加项。
//   本 shader 里出现 Outline 字样的唯一目的就是那条排除规则（见下）——不要因为两者都叫
//   "边缘"就把它们合并成一个概念（蓝本 §6 P4 明写：本仓"边缘光"这个词一度指反向壳墨线，
//   是两回事）。
//
// 【两条必须输出 0 的像素（本仓口径，不是 v3 的行为）】
//   ① 墨线像素（`_PixelartOutlineBuffer.r > 0.5`）必须输出 0。墨线是"画上去的线"，
//      它不吃光照；不排除的话合成时墨线会被边缘光染亮，1 艺术像素的线就不再是墨色。
//   ② 背景像素（`_PixelartAlbedoBuffer.a < 0.5`，覆盖标记）输出 0：背景没有光照层，
//      谈不上"光照层的边缘提亮"。
//   注意 v3 的对应趟是 `clip(albedo.a - 0.0001)` 丢掉背景、且它**没有墨线概念**
//   （墨线是它自己的 `OutlinePass.hlsl` 的事，且那 feature 在 v3 里是关着的）。
//
// 【门控本体在 Includes/RimLight.hlsl】那边是 v3 `RimLight.hlsl:16-20` 的逐行翻译
//   （屏幕空间光向 × 连通域四方向），连同 `.a` 打包字节的解码与"为什么 v3 那两步
//   （ndotl / multiStep）本仓不接"都写在文件头。本文件只负责"哪些像素有资格"。
//
// 【为什么这一趟没有 `_PixelartDebugMode` 调试档】调试档的可见性是合成趟给的：
//   本趟的输出**永远不会直接上屏**（它跑在合成之前，画面由合成趟决定）。要把边缘光
//   画出来看，应该在合成趟加一个档位（"只吐 RimLight 缓冲"），在那里做——在本趟里加
//   分支是加了也看不到的死代码（AGENTS.md 图形学调试规范要的是"能出图"，不是"有分支"）。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartRimLight"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // 契约 §2.1 的 pass 名：Feature 按名字解析索引（不写死序号）。
            Name "PixelartRimLight"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment PixelartRimLightFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形 + `_BlitScaleBias` 的 uv 口径由官方 Blit.hlsl 提供（Vert / Varyings）；
            // Feature 侧用 DrawProcedural(3 顶点) 驱动，且必须显式把 `_BlitScaleBias` 置成
            // (1,1,0,0)——不置的话 Vert 算出的 uv 恒为 0（只采到一个像素，全屏同色）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // 门控本体（v3 `RimLight.hlsl:16-20` 的翻译 + 打包字节解码）。
            #include "Includes/RimLight.hlsl"

            // 输入全是 shader 全局（契约 §2.2）：屏幕档的覆盖标记、艺术画布的连通域结论与
            // 逐物体边缘光色。连通域与墨线两张的绑定由本趟自己发布（Feature 侧
            // `rig.PublishArtBuffers`），不依赖别的 Feature 的发布次序。
            TEXTURE2D(_PixelartAlbedoBuffer);              SAMPLER(sampler_PixelartAlbedoBuffer);
            TEXTURE2D(_PixelartConnectivityResultBuffer);  SAMPLER(sampler_PixelartConnectivityResultBuffer);
            TEXTURE2D(_PixelartRimLightPropertyBuffer);    SAMPLER(sampler_PixelartRimLightPropertyBuffer);
            TEXTURE2D(_PixelartOutlineBuffer);             SAMPLER(sampler_PixelartOutlineBuffer);

            // 主光方向（世界空间，**指向光源**）：门控要靠它算屏幕空间光向。
            float4 _PixelartLightDirWS;

            half4 PixelartRimLightFragment(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // ① 覆盖标记：背景像素没有光照层（口径见文件头）。
                //    艺术画布这里按 uv 点采样屏幕档缓冲 ⇒ 取到的正是本像素对应那个块的
                //    中心纹素（契约 §0 的降采口径），与"直接在画布上光栅化"逐点等价。
                float coverage = SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv).a;
                if (coverage < 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);

                // ② 墨线像素：必须输出 0（否则合成时墨线被边缘光染色）。**本仓口径**。
                float inkMark = SAMPLE_TEXTURE2D(_PixelartOutlineBuffer, sampler_PixelartOutlineBuffer, uv).r;
                if (inkMark > 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);

                // ③ 门控：连通域四方向 × 屏幕空间光向（v3 RimLight.hlsl:16-20）。
                float4 connectivity = SAMPLE_TEXTURE2D(_PixelartConnectivityResultBuffer,
                    sampler_PixelartConnectivityResultBuffer, uv);
                // ④ 逐物体边缘光色：本像素所属物体的光照层提亮色。
                float3 rimLightColor = SAMPLE_TEXTURE2D(_PixelartRimLightPropertyBuffer,
                    sampler_PixelartRimLightPropertyBuffer, uv).rgb;

                uint packedConnectivity = PixelartUnpackConnectivityByte(connectivity.a);
                float3 rimLight = PixelartRimLightShading(packedConnectivity, _PixelartLightDirWS.xyz, rimLightColor);
                return half4(rimLight, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
