// ============================================================================
// CloudSeaRain.shader —— 云端漫步「太阳雨」天气件的**双趟透明** shader。
// 雨丝 / 云隙光柱 / 彩虹 / 涟漪共用这一份 shader，材质按
// _SrcBlend / _DstBlend（普通透明 vs 加法）与两个 UV 渐变开关（_TipFade / _EdgeFade）
// 分装扮；颜色 / 强度全走 _Color / _Intensity + 顶点色。
//
// 【为什么是两个 pass】本工程两条渲染链各自收件（docs/技术/渲染/管线/双风格切换.md）：
//   · **CellShade（单元着色线）**：像素化透明趟（`PixelartTransparentFeature`，Cast 渲染器
//     第 7 趟）按 `LightMode = "PixelartTransparent"` 收透明队列材质，画进艺术画布——
//     与画面同像素颗粒，遮挡在片元里手动比细像素深度（口径逐行同 PixelartTransparent.shader）；
//   · **PostLine / 无 rig 的普通 URP 相机**：标准前向透明趟（无 LightMode 标签 = SRPDefaultUnlit，
//     队列 >= 3001）在真实深度缓冲上正常画。
// 与 `CloudSeaPath.WaterShaderName`（PirateCrew/CloudSea/Water）同一「双趟透明」契约：
// CellShade 走 PixelartTransparent 趟，PostLine 走标准前向透明趟。
//
// 【双 pass 会不会画两遍——闸门】CellShade 在场时，标准趟仍会被**叠加相机**（标准透明
// 队列、全分辨率、清空深度）再画一遍——重影 + 颗粒错档。所以由 `CloudSeaWeather`
// 每帧下发闸门全局 `_CloudSeaRainPixelDomain`（CellShade rig 在场 = 1，否则 = 0）：
//   · 闸门 = 1：PixelartTransparent 趟通过，标准趟整片 clip（叠加相机画不出东西）；
//   · 闸门 = 0：标准趟画，PixelartTransparent 趟自裁（PostLine 没有 Cast 相机，
//     那一趟本来也不会被收；无 rig 的普通相机只有这一趟可走）。
// Cast 相机自己的标准透明趟也会先画一遍（队列 >= 3001 收得到），但着色合成趟是
// 整屏覆盖写、会把它抹掉——这一重不依赖闸门也安全。
// 闸门缺省（没人下发）= 0 = 标准趟可见：组件缺席时退化为全分辨率覆盖层，宁可观感降档
// 也不整类消失。
//
// 【颜色与两个渐变】rgb = _Color × _Intensity × 顶点色；alpha = _Color.a × 顶点色.a，再乘：
//   · `_TipFade`：沿 **uv.y** 淡出——拉伸公告板把 v=1 端对齐到速度方向（下落的前端），
//     v=0 端是尾端；_TipFade = 1 即尾端渐隐（雨丝装扮），= 0 关闭；
//   · `_EdgeFade`：沿 **uv.x** 向两侧柔边（u=0/1 处 alpha→0）——光柱 / 彩虹带 / 涟漪环
//     的宽向柔边（= 1 开，雨丝关闭）。
//
// 【混向逐材质】`Blend [_SrcBlend] [_DstBlend]`：雨 / 彩虹 / 涟漪 = SrcAlpha,
// OneMinusSrcAlpha（普通透明）；光柱 = SrcAlpha, One（加法——云隙光要的是叠亮）。
// 全程 ZWrite Off、Cull Off（细长面片与交叉光柱都可能被自由方位相机从背面看到）、不写深度。
//
// 【队列】材质一律 3001（Transparent + 1）：URP 标准透明档实测下界是 3001 而不是 3000
// （口径见 `PixelartPath.TransparentQueueMin` 头注的实测记录），队列 3000 的材质标准相机
// 摸不到；像素化透明趟收 [3000, 5000]，3001 两头都覆盖。
// ============================================================================

Shader "PirateCrew/CloudSea/CloudSeaRain"
{
    Properties
    {
        _Color ("着色（rgb = 色，a = 基础 alpha）", Color) = (1, 0.949, 0.816, 0.42)
        _Intensity ("亮度倍率", Range(0, 4)) = 1
        _TipFade ("尾端沿 uv.y 淡出（雨丝 1，其余 0）", Range(0, 1)) = 0
        _EdgeFade ("宽向沿 uv.x 柔边（光柱/带/环 1）", Range(0, 1)) = 0
        _SrcBlend ("混合源因子（BlendMode 枚举值）", Float) = 5
        _DstBlend ("混合目标因子（BlendMode 枚举值）", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Intensity;
            float _TipFade;
            float _EdgeFade;
        CBUFFER_END

        // 闸门全局（不进 UnityPerMaterial：它是链级开关，不是材质参数）。见文件头。
        float _CloudSeaRainPixelDomain;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;
            float4 color      : COLOR;      // 粒子 startColor（含生命期渐隐）/ 网格烘焙色（彩虹光谱）
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            float4 color      : COLOR;
            float4 screenPos  : TEXCOORD1;  // NDC：/w = 屏幕 uv（像素化趟手动深度采样用）
            float4 clipPos    : TEXCOORD2;  // 原始 clip 坐标（不走 SV_POSITION，z/w = NDC 深度口径明确）
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = vpi.positionCS;
            output.screenPos = vpi.positionNDC;
            output.clipPos = vpi.positionCS;
            output.uv = input.uv;
            output.color = input.color;
            return output;
        }

        // 两趟共用的颜色数学：贴图没有——太阳雨全是程序色，颜色全靠 _Color × 顶点色。
        half4 FragCommon(float2 uv, half4 vertexColor)
        {
            half alpha = _Color.a * vertexColor.a;
            alpha *= lerp(1.0, uv.y, _TipFade);                          // 雨丝尾端淡出
            alpha *= lerp(1.0, 1.0 - abs(uv.x * 2.0 - 1.0), _EdgeFade);  // 宽向两侧柔边
            half3 rgb = _Color.rgb * _Intensity * vertexColor.rgb;
            return half4(rgb, saturate(alpha));
        }
        ENDHLSL

        // ---------- Pass 1：标准前向透明趟（PostLine 相机 / 叠加相机 / 普通 URP） ----------
        Pass
        {
            Name "CloudSeaRainForward"
            // 无 LightMode 标签 = SRPDefaultUnlit：URP 标准 DrawObjects（透明档）收这一条。

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragForward

            half4 FragForward(Varyings input) : SV_Target
            {
                // CellShade 像素化域在场时整趟让位：透明队列由像素化透明趟负责，
                // 标准趟再画就是叠加相机的重影（闸门口径见文件头）。
                clip(0.5 - _CloudSeaRainPixelDomain);
                return FragCommon(input.uv, input.color);
            }
            ENDHLSL
        }

        // ---------- Pass 2：像素化域透明趟（CellShade Cast 渲染器第 7 趟收件） ----------
        Pass
        {
            Name "CloudSeaRainPixelart"
            Tags { "LightMode" = "PixelartTransparent" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPixelart

            // 细像素深度（连通域趟之后指向 Patch 版，与着色各趟同一份）。
            // 手动比深度的口径与 PixelartTransparent.shader 逐行一致。
            TEXTURE2D(_PixelartDepthBuffer);        SAMPLER(sampler_PixelartDepthBuffer);

            half4 FragPixelart(Varyings input) : SV_Target
            {
                // 闸门 = 0（PostLine / 无 rig）时自裁：那一域没有 _PixelartDepthBuffer 可比，
                // 也不该由 Cast 相机以外的域画这一趟。
                clip(_CloudSeaRainPixelDomain - 0.5);

                // —— 手动深度遮挡（等效 ZTest LEqual，对细像素深度的块锚点采样）——
                // 平台分支沿用 PixelartTransparent.shader。【不要写
                // (ndc - UNITY_NEAR_CLIP_VALUE) / (1 - UNITY_NEAR_CLIP_VALUE)】：
                // reversed-Z 下该宏 = 1.0，分母 0，全屏片元拿到 -inf 被 clip 光
                //（弹道预览曾因此整条不可见，该 shader 头注有事故记录）。
                float2 screenUv = input.screenPos.xy / input.screenPos.w;
                float storedRaw = SAMPLE_TEXTURE2D(_PixelartDepthBuffer, sampler_PixelartDepthBuffer, screenUv).r;
                float ndcDepth = input.clipPos.z / input.clipPos.w;
#if defined(UNITY_REVERSED_Z)
                clip(ndcDepth - storedRaw);     // reversed-Z（D3D/Metal/Switch）：数值大 = 近
#else
                float deviceDepth = ndcDepth * 0.5 + 0.5;
                clip(storedRaw - deviceDepth);  // 正向 Z（GL/GLES）：数值小 = 近
#endif
                return FragCommon(input.uv, input.color);
            }
            ENDHLSL
        }
    }
}
