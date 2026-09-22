// ============================================================================
// PixelartShading.shader —— 像素化着色路径的**低分辨率域着色**
//
// 一趟全屏，读三张 G-buffer（全局纹理，由物体 pass 画完后 SetGlobalTexture 下发），
// 算出最终颜色。**这条路的所有着色都发生在这里**，而且只发生一次/低分辨率像素。
//
// 【核心数学 = v3 的 DiffuseShading】（`ShadingPass.hlsl:37-63`，逐行翻译）：
//     ndotl = saturate(dot(L, N))
//     ndotl = pow(ndotl, 1/2.2)            // 让色带边界落在感知均匀的位置
//     ndotl += singleLevel * offset         // 抖动：偏移量 = 一个色带步长 × 逐物体图案
//     ndotl = multiStep(ndotl, level, 0, 0) // 量化成 level 档（"色带"本体）
//     ndotl = clamp(ndotl, 0, 1 + singleLevel)
//     color = ndotl * lightColor * albedo
//   本路径额外加的只有**环境光项**（`+ ambientColor * albedo`）：v3 那项来自 SH/lightmap
//   （其 lightmap 通道实际是空的、等于只吃 SH），本路径按渲染篇 §4.6 简化为单色环境光——
//   这一项同时也是"暗部色"这个美术自由度的所在（暗面 = albedo × 环境色）。
//
// 【墨线（外轮廓）不在这一趟】它是物体 pass 的反向壳（`PixelartObject.shader` Pass 2），
//   写进同一批 G-buffer，靠 prop.a = 1 标记；本途径见到标记就原样输出墨色、跳过色带与环境光。
//   曾经有一版在这里按"覆盖度差 + 法线差"做屏幕空间轮廓检测——那是**自造方案，已撤掉**：
//   渲染篇 §5 明确"不用屏幕空间边缘检测"（正交下反向壳线宽天然恒定、内缘不出杂线），
//   蓝本 §8 裁决点 3 的推荐口径也是"外轮廓 = 反向壳"。v3 自己的 `OutlinePass.hlsl` 虽是
//   屏幕空间方案，但它在 v3 里**是关着的**（蓝本 §4.3），且门控依赖 P4 才有的连通域结果。
//
// 【本阶段刻意留空的两处】
//   ① 连通域降档：v3 在量化后按 `connect < threshold` 减一档（内部转折出"内线"）。
//      它要 P4 的连通域三张缓冲，本阶段无输入，故不设该分支。
//   ② 法线边加成（`normalDiff > threshold` 时加档）：同属连通域产物，一并留给 P4。
//      prop.b 已把它从物体 pass 带过来了，接的时候只需要这里多一个分支。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartShading"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PixelartShading"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment PixelartShadingFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形 + _BlitScaleBias 的翻转口径由官方 Blit.hlsl 提供（Vert / Varyings）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 三张 G-buffer 走 shader 全局（物体 pass 下发）；采样器跟纹理名绑定。
            TEXTURE2D(_PixelartAlbedoBuffer);   SAMPLER(sampler_PixelartAlbedoBuffer);
            TEXTURE2D(_PixelartNormalBuffer);   SAMPLER(sampler_PixelartNormalBuffer);
            TEXTURE2D(_PixelartPropertyBuffer); SAMPLER(sampler_PixelartPropertyBuffer);

            float4 _PixelartLightDirWS;    // 指向光源
            float4 _PixelartLightColor;
            float4 _PixelartAmbientColor;

            // v3 `Math/Step.hlsl` 的 multiStep，逐行翻译：
            // 把 value 量化成 level 档；minValue 是"第 0 档占多少"（0 = 最暗档全黑），
            // offset 是量化前的偏置（抖动就从这里进来，但本路径把它加在 ndotl 上，同 v3）。
            float MultiStep(float value, float level, float minValue, float offset)
            {
                if (level <= 1.0)
                    return 1.0;

                float curLevel = value * level;
                curLevel = floor(curLevel + offset);

                float curOffset = curLevel / (level - 1.0);
                curLevel += lerp(minValue, 1.0, curOffset);
                curLevel = curLevel / level;

                return saturate(curLevel);
            }

            half4 PixelartShadingFragment(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half4 albedo = SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv);
                // 没有几何的像素直接丢弃：Cast 相机的颜色目标已被物体 pass 清成场景背景色，
                // 丢弃即保留背景（v3 用同一手法：`clip(diffuse.a - 1)`）。
                clip(albedo.a - 0.5);

                float4 prop = SAMPLE_TEXTURE2D(_PixelartPropertyBuffer, sampler_PixelartPropertyBuffer, uv);

                // 墨线像素：原样输出。墨线是"画上去的线"，不参与色带量化、也不吃环境光——
                // 否则深墨色会被环境光染成带色偏的暗带，看上去就不是墨线了（prop.a 是墨线标记）。
                if (prop.a > 0.5)
                    return half4(albedo.rgb, 1.0);

                float3 normalWS = SAMPLE_TEXTURE2D(_PixelartNormalBuffer, sampler_PixelartNormalBuffer, uv).xyz;

                float level  = prop.r;
                float dither = (prop.g - 0.5) * 2.0;      // 0..1 → -1..+1

                float3 N = normalize(normalWS);
                float3 L = normalize(_PixelartLightDirWS.xyz);

                float ndotl = saturate(dot(L, N));

                if (level > 1.0)
                {
                    float singleLevel = 1.0 / (level - 1.0);

                    ndotl = pow(ndotl, 1.0 / 2.2);
                    ndotl = saturate(ndotl);
                    ndotl += singleLevel * dither;

                    ndotl = MultiStep(ndotl, level, 0.0, 0.0);
                    ndotl = clamp(ndotl, 0.0, 1.0 + singleLevel);
                }

                float3 lit = ndotl * _PixelartLightColor.rgb * albedo.rgb;
                float3 ambient = _PixelartAmbientColor.rgb * albedo.rgb;

                return half4(lit + ambient, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
