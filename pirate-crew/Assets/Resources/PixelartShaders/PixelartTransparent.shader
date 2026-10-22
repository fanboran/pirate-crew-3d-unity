// ============================================================================
// PixelartTransparent.shader —— 像素化着色路径的**透明趟**材质
//
// 【它在解决什么】叠加档（Overlay 相机）的效果件是全分辨率绘制、深度每帧清空——
// 崖后的弹道预览会画在崖前（穿透地形）。本 shader 让半透明效果件挪进**像素化域**：
// 由 `PixelartTransparentFeature`（Cast 渲染器第 7 趟，着色合成之后）把声明了
// `LightMode = PixelartTransparent` 的材质画进**艺术画布**——与画面同像素化（上屏
// 点采样放大天然覆盖），并被既有几何正确遮挡。
//
// 【遮挡怎么判：手动深度比较，不绑深度附件】艺术画布上没有这趟自己的深度缓冲
// （既有深度是屏幕档尺寸，也绑不上），所以 `ZTest` 形同虚设、片元里**手动比深度**：
//   ① 用屏幕 uv 点采样 `_PixelartDepthBuffer`（该全局在连通域趟之后指向 Patch 版
//      细像素深度，与着色各趟同一份——块锚口径 `floor((id+0.5)*k)` 一致）；
//   ② 片元自己的设备深度 = `clipPos.z / clipPos.w` 重映射到 [0,1]
//      （`UNITY_NEAR_CLIP_VALUE` 消 D3D/GL 的 NDC 范围差）；
//   ③ 等效 `ZTest LEqual`：片元比已画几何近（或同深）才通过，`clip` 掉被挡片元。
//      reversed-Z 平台数值大 = 近，方向要反过来（正反两分支都要保留）。
// 无几何处深度是清屏的最远值，任何片元都比它近，正常通过——不需要特判。
//
// 【颜色怎么算】`贴图 × 顶点色 × _Color` 后 alpha 混合（`Blend SrcAlpha
// OneMinusSrcAlpha`，`ZWrite Off`）——弹道预览的锥形色烘在珠点/环的**顶点色**里
// （`TrajectoryPreview.CreateTintedQuad` / LineRenderer start/endColor），材质保持白。
// 不吃光照/色带/描边：透明件是"覆盖层"语义，不参与 G-buffer 与着色数学。
//
// 【为什么 LightMode 自立门户】与 `PixelartOpaque` 同理（AGENTS.md 铁律）：标准 URP
// pass 匹配不到本材质 ⇒ Cast 相机的普通不透明趟与叠加相机的标准透明趟都不会画它，
// 同一件物体不会被画两遍；本趟也只按 `PixelartTransparent` 这一个 tag 收件。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartTransparent"
{
    Properties
    {
        _MainTex ("贴图（留空 = 白，颜色全靠顶点色）", 2D) = "white" {}
        _Color ("乘色（与顶点色相乘）", Color) = (1, 1, 1, 1)
        _DepthTest ("手动深度遮挡（1 = 开，0 = 纯覆盖/选中框）", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PixelartTransparent"
            Tags { "LightMode" = "PixelartTransparent" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // 双面：珠点面片/贴地三角/环条带都可能被自由方位的相机从背面看到。
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);                    SAMPLER(sampler_MainTex);
            // 细像素深度（全局名由 rig/连通域趟发布，透明趟只按名采样，不自己持有 RT）。
            TEXTURE2D(_PixelartDepthBuffer);        SAMPLER(sampler_PixelartDepthBuffer);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _DepthTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // 珠点锥形色 / 环青色（TrajectoryPreview 烘进 mesh）
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;  // NDC：/w = 屏幕 uv（与全屏趟同一约定）
                float2 uv         : TEXCOORD1;
                float4 color      : COLOR;
                float4 clipPos    : TEXCOORD2;  // 原始 clip 坐标（不走 SV_POSITION，z/w = NDC 深度口径明确）
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vpi.positionCS;
                output.clipPos = vpi.positionCS;
                output.screenPos = vpi.positionNDC;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tint = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color * _Color;

                // —— 手动深度遮挡（等效 ZTest LEqual，对细像素深度的块锚点采样）——
                // `_DepthTest = 0` 的材质（选中角框）跳过整段：选中框是 UI 覆盖语义，
                // 8 个角必须全部可见，不因角色身体/地形挡住而缺角。
                if (_DepthTest > 0.5)
                {
                    float2 screenUv = input.screenPos.xy / input.screenPos.w;
                    float storedRaw = SAMPLE_TEXTURE2D(_PixelartDepthBuffer, sampler_PixelartDepthBuffer, screenUv).r;
                    float ndcDepth = input.clipPos.z / input.clipPos.w;
                    // NDC → 深度附件里的 raw device depth（逐平台口径）：
                    //   · reversed-Z（D3D/Metal/Switch）：附件存的就是 clip z ∈ [0,1]，近 = 1，直接用；
                    //   · 正向 Z（GL/GLES）：附件存 NDC 的 [0,1] 重映射，NDC z ∈ [-1,1] → [0,1]。
                    // 【不要写 (ndc - UNITY_NEAR_CLIP_VALUE) / (1 - UNITY_NEAR_CLIP_VALUE)】：
                    // reversed-Z 下该宏 = 1.0，分母 0，全屏片元会拿到 -inf 并被下面 clip 得一个不剩
                    // （弹道预览曾因此整条不可见）。
#if defined(UNITY_REVERSED_Z)
                    float deviceDepth = ndcDepth;
                    clip(deviceDepth - storedRaw);     // reversed-Z：数值大 = 近；片元 ≥ 存储 → 通过
#else
                    float deviceDepth = ndcDepth * 0.5 + 0.5;
                    clip(storedRaw - deviceDepth);     // 正向 Z：数值小 = 近；片元 ≤ 存储 → 通过
#endif
                }
                return tint;
            }
            ENDHLSL
        }
    }
}
