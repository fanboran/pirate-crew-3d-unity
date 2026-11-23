// 屏后描边线（PostLine）Pass2：低分辨率画布上的屏幕空间描边遮罩。
// 规格与逐行公式：docs/技术/渲染/调研/PostLine规格.md §2.2；
// 源级参照 external/ref/unity-isometric-pixel-pipeline 的 OutlineShader.shader
//（MIT License, Copyright (c) 2026 bababuyyy——移植按其许可保留声明）。
//
// 【产物】RGBA 遮罩：剪影像素 = 最近邻像素色 × _LineDarken（A = _LineAlpha）；
//         棱线像素 = 中心色 × (1 + _CreaseBrighten)（A = 权重 × _CreaseAlpha）；其余 0。
//         合成趟（PostLineComposite）用 A 当混合因子——A 载线强度是硬要求。
//
// 【与参照库的三处移植差异】
//  · 死参数不搬：参照的 _DepthDiffLow/_DepthDiffHigh（算出后未被使用）与 tint/overlay/feather 族
//    （声明了但不参与计算）一律删去——参数面收敛到实际生效的 9 个（规格 §9 坑 11）。
//  · kernel 步长固定为 1 **画布**纹素：参照用 _BlitTexture_TexelSize 作步长基数，该伴生量
//    Blitter 不保证填充且在 kernel≠1 时语义失真（规格 §9 坑 10）。本仓写死 kernel=1，
//    步长从画布尺寸全局（_PixelartRTWidth/_PixelartRTHeight）推，语义明确为"按画布像素计"。
//  · 深度/法线按 URP14 标准口径采样（法线纹理存的是归一化世界法线），**不复刻**参照
//    ToonLit DepthNormals 的 n*0.5+0.5 偏置写法——那是参照自身"剪影阈值随相机方位角漂移"
//    的根因（规格 §9 坑 7 / §8.3）。
Shader "Hidden/PirateCrew/PostLineOutline"
{
    Properties
    {
        // 默认值 = 参照库资产实测（规格 §5.4 的调优值，非参照代码默认）。
        _LineDarken ("剪影线压暗", Range(0, 1)) = 0.5
        _LineAlpha ("剪影线不透明度", Range(0, 1)) = 1
        _ZDeltaCutoff ("剪影深度阈值(视图米)", Range(0, 1)) = 0.21
        _AngleZCutoff ("掠射阈值起点", Range(0, 1)) = 0.3
        _AngleZScale ("掠射阈值放大倍数", Range(0, 4)) = 4
        _CreaseBrighten ("棱线提亮", Range(0, 1)) = 0.5
        _CreaseAlpha ("棱线不透明度", Range(0, 1)) = 1
        _NormalSmoothLow ("棱线对比度平滑下限", Range(0, 2)) = 0.05
        _NormalSmoothHigh ("棱线对比度平滑上限", Range(0, 2)) = 0.3
    }
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
            // 深度/法线读**全分辨率引擎缓冲**（本趟 blit 源是画布色，uv 按 1 画布像素步进
            // 去采样全分辨率缓冲——等价于"内部直接渲染"的邻域，规格 §9 坑 10）。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            // ---- 参数族（feature 每帧 SetFloat；名字契约见 PostLinePath）----
            float _LineDarken;
            float _LineAlpha;
            float _ZDeltaCutoff;
            float _AngleZCutoff;
            float _AngleZScale;
            float _CreaseBrighten;
            float _CreaseAlpha;
            float _NormalSmoothLow;
            float _NormalSmoothHigh;

            // ---- 画布尺寸（feature 每帧下发；与 rig 的 _PixelartRTWidth/Height 同全局）----
            float _PixelartRTWidth;
            float _PixelartRTHeight;

            // 1 画布像素的 uv 步长（kernel 恒 1；参照的 _KernelRadius 参数已删，见头注）。
            float2 CanvasTexelStep()
            {
                return float2(1.0 / _PixelartRTWidth, 1.0 / _PixelartRTHeight);
            }

            // NDC(x,y) + 设备深度 → 视图空间位置。
            // 【矩阵口径】UNITY_MATRIX_I_P = unity_MatrixInvP，URP 每相机按 **GPU 投影**取逆下发
            //（ScriptableRenderer.SetCameraMatrices）——与 SampleSceneDepth 读到的设备深度同一口径；
            // 本链正交投影下逆映射是线性的，除 w 恒等（w=1）。
            float3 ReconstructViewPos(float2 uv, float rawDepth)
            {
                float4 clipPos = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clipPos.y = -clipPos.y;
                #endif
                float4 viewPos = mul(UNITY_MATRIX_I_P, clipPos);
                return viewPos.xyz / viewPos.w;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 stepUV = CanvasTexelStep();

                // ① 3×3 网格：9 点视图空间位置 + 视图空间法线（规格 §2.2①）。
                float3 vp[9];
                float3 vn[9];
                float2 sampleUVs[9];
                [unroll]
                for (int k = 0; k < 9; k++)
                {
                    float2 offset = float2(k % 3 - 1, k / 3 - 1);
                    sampleUVs[k] = uv + offset * stepUV;

                    vp[k] = ReconstructViewPos(sampleUVs[k], SampleSceneDepth(sampleUVs[k]));

                    // 世界法线 → 视图空间。视图空间 +z 朝远离相机方向，故 vn.z 越大**越近**。
                    vn[k] = mul((float3x3)UNITY_MATRIX_V, SampleSceneNormals(sampleUVs[k]));
                }

                // ② 剪影角度自适应阈值（§2.2②）：facing 越大越掠射，掠射面的深度差天然偏大，
                //    阈值跟着放大 _AngleZScale 倍，防止斜坡沿全长出剪影线。
                float facing = 1.0 - vn[4].z;
                float t01 = saturate((facing - _AngleZCutoff) / (1.0 - _AngleZCutoff));
                float zThresh = _ZDeltaCutoff * (t01 * _AngleZScale + 1.0);

                // ③ 棱线：4 正邻方向对比度（§2.2③）。参照同位的 depthDifference 累计量算出后
                //    未被使用（规格 §9 坑 4），不搬；只保留"中心比正邻近多少"这一路。
                int cardinals[4] = { 1, 3, 5, 7 };
                float invDepthDifference = 0.0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    int idx = cardinals[i];
                    invDepthDifference += clamp(vp[4].z - vp[idx].z, 0.0, 1.0);
                }
                invDepthDifference = clamp(invDepthDifference, 0.0, 1.0);
                // 参照写 clamp(smoothstep(0.9, 0.9, x) * 10, 0, 1)——同边沿 smoothstep 即阶跃
                //（规格 §2.2 行 119 注），照抄会引入除零未定义行为；语义等价的 step：
                // 中心显著更近（累计 ≥ 0.9 视图米）→ 1，即近剪影带内抑制棱线。
                invDepthDifference = step(0.9, invDepthDifference);

                float d0 = 1.0 - dot(vn[4], vn[cardinals[0]]);
                float d1 = 1.0 - dot(vn[4], vn[cardinals[1]]);
                float d2 = 1.0 - dot(vn[4], vn[cardinals[2]]);
                float d3 = 1.0 - dot(vn[4], vn[cardinals[3]]);
                // 方向对比度：对面两邻的法线差取 max——曲面两侧等量变化 → ≈0，
                // 真棱线单侧剧变 → 大（这是"棱线只画在折痕上"的机制核心）。
                float contrast = max(abs(d0 - d3), abs(d1 - d2));
                float normalDifference = smoothstep(_NormalSmoothLow, _NormalSmoothHigh, contrast);
                float creaseWeight = saturate(normalDifference - invDepthDifference);

                // ④ 剪影：8 邻域深度不连续（§2.2④）。比较发生在**视图空间米**，与分辨率无关
                //    （规格 §8.1：阈值族原样搬运）。
                bool hasLine = false;
                int closestIdx = 4;
                float maxZ = vp[4].z;                       // 视图 z 越大越近（见①注）
                int neighbors[8] = { 0, 1, 2, 3, 5, 6, 7, 8 };
                [unroll]
                for (int s = 0; s < 8; s++)
                {
                    int idx = neighbors[s];
                    if (vp[idx].z - vp[4].z > zThresh)
                        hasLine = true;                     // 邻居比中心近超过阈值 → 中心在剪影远侧
                    if (vp[idx].z > maxZ)
                    {
                        maxZ = vp[idx].z;
                        closestIdx = idx;                   // 记录最近邻（线色的来源）
                    }
                }

                // ⑤ 输出遮罩（§2.2⑤）：剪影优先于棱线——同一像素不会既压暗又提亮。
                float3 result = float3(0.0, 0.0, 0.0);
                float alpha = 0.0;
                if (hasLine)
                {
                    // 线色 = 最近邻像素色 × _LineDarken：线自带物体固有色，不是固定墨色。
                    float3 closestColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, sampleUVs[closestIdx]).rgb;
                    result = closestColor * _LineDarken;
                    alpha = _LineAlpha;
                }
                else if (creaseWeight > 0.0)
                {
                    // 棱线色 = 中心色 × (1 + _CreaseBrighten)，alpha 由对比度权重调制。
                    float3 centerColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
                    result = centerColor * (1.0 + _CreaseBrighten);
                    alpha = creaseWeight * _CreaseAlpha;
                }

                return half4(result, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
