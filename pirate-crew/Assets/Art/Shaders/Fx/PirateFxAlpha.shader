// ============================================================================
// PirateFxAlpha.shader —— 海盗军团夺宝 3D / 特效「透明混合」粒子·木屑·烟·伤害数字
//
// 【用途】烟雾余留、木屑/沙尘、水沫、冲击波的内圈、伤害数字（字形贴图）。
//   标准 Alpha 混合（SrcAlpha, OneMinusSrcAlpha），颜色不会被背景"加亮"，适合读作实体碎屑
//   与需要保持可读性的数字。
//
// 【与 Additive 版的关系】除 Blend 与文件头注释外，顶点/片元实现逐行相同 —— 刻意复制而非抽 .hlsl：
//   本工程已有结论（PirateSurface.shader:179-180）"多一条 include 就多一条静态核对全绿、真实编译才炸
//   的风险面"，两个 20 行的片元重复成本远低于一条共享 include 链的风险。
//
// 【Pass 与 LightMode】单 Pass，用 "UniversalForward"；同 shader 内不得再出现同 LightMode 的 Pass
//   （会被 URP 静默丢弃，见 docs/描边Shader调试.md §八-2）。
//
// 【半透明与深度】ZWrite Off + Queue=Transparent。半透明不写深度纹理，故本 shader 不提供
//   DepthOnly Pass —— 特效不参与 URP 的深度预通道是正确的（否则水面泡沫会被特效挡住）。
//
// 【_DebugMode 分档】0 正常 / 1 只 alpha / 2 只顶点色 / 3 只贴图 RGB；预期画面见片元分支。
//
// 【[CHECK]】同 Additive 版：Core.hlsl 提供变换与采样宏；仍需在有渲染路径的编辑器里
//   play 一次 + read_console 确认 0 shader error（本文件作者无法启动 Unity 进程）。
// ============================================================================

Shader "PirateCrew/Fx/Alpha"
{
    Properties
    {
        _BaseMap    ("贴图", 2D) = "white" {}
        _Color      ("着色", Color) = (1, 1, 1, 1)
        _Intensity  ("强度（仅提亮，不改 alpha）", Range(0, 8)) = 1.0
        _DebugMode  ("调试模式 0=正常 1=alpha 2=顶点色 3=贴图", Range(0, 3)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "FxAlpha"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            // 3.5 是 URP 下 GPU Instancing（#pragma multi_compile_instancing）的最低着色器模型。
            #pragma target 3.5
            #pragma vertex   FxVertex
            #pragma fragment FxFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float  _Intensity;
                float  _DebugMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings FxVertex(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color      = IN.color;
                return OUT;
            }

            half4 FxFragment(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 col = tex * _Color * IN.color;

                // 档 1：只 alpha。预期：木屑/烟为白色软斑，数字为白色字形剪影。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(col.a, col.a, col.a, 1.0h);
                // 档 2：只顶点色。预期：粒子系统起始色；木屑/数字常为纯白。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(IN.color.rgb, 1.0h);
                // 档 3：只贴图 RGB。预期：木屑棕色、烟灰色、字形白色。
                if (_DebugMode > 2.5)
                    return half4(tex.rgb, 1.0h);

                // 透明混合：RGB 提亮但不改透明度，保持"实体碎屑"的读感。
                col.rgb = saturate(col.rgb * _Intensity);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
