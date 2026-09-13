// ============================================================================
// PirateFxAdditive.shader —— 海盗军团夺宝 3D / 特效「加法混合」粒子·拖尾·光环
//
// 【用途】爆炸火球/火花/冲击波环、水花飞沫、回合光环、伤害数字的发光层。
//   加法混合（SrcAlpha, One）在暖色叠暖色时自然过曝成白心，正是"打起来有手感"的关键观感。
//
// 【为什么自写而不用 URP/Particles/Unlit】
//   1. URP 官方 Particles/Unlit 的变体很多（软/硬粒子、相机淡出、雾、遮挡），
//      本工程只需要"贴图 × 顶点色 × 常量色 × 强度"，自写变体少、编译快；
//   2. 需要与工程既有 shader 相同的 _DebugMode 分档约定（AGENTS.md 图形学调试截图规范）；
//   3. 避免依赖包内 shader 的 Blending 属性在运行时用 SetProperty 改的脆弱做法 —— 加法/透明
//      分成两个 shader，各自 Blend 写死，材质生成脚本（Assets/Editor/FxAssetBuilder.cs）按用途选。
//
// 【Pass 与 LightMode（URP 陷阱，见 docs/描边Shader调试.md §八-2）】
//   本 shader 只有一个 Pass，用 "UniversalForward" —— 它是 URP 前向 DrawObjectsPass 取用的
//   着色器标签，独立 unlit shader 用它即会被画到。注意"同一 shader 里再加一个同 LightMode 的
//   Pass 会被静默丢弃"，若将来给特效加额外 Pass，必须换一个未被占用的 LightMode。
//
// 【透明度与排序】ZWrite Off + Queue=Transparent；Billboard 粒子由 ParticleSystem 自行排序，
//   多个特效系统之间靠 SortingFudge/RenderQueue 微调。加法混合不需要深度写入。
//
// 【雾】不参与（#pragma multi_compile_fog 未开）：战场相机距离 ≈18、雾起点 25
//   （docs/场景设计-战斗竞技场.md §8），特效全在近景，加雾只会让爆炸发灰。
//
// 【_DebugMode 分档】每档预期画面写在片元对应分支处。
//   0 正常合成 / 1 只 alpha（灰度）/ 2 只顶点色 / 3 只贴图 RGB
//
// 【[CHECK] 本地 URP 14.0.12 符号核对】
//   Core.hlsl                    -> ShaderLibrary/Core.hlsl（TransformObjectToHClip、
//                                   TEXTURE2D/SAMPLER、TRANSFORM_TEX、UNITY_SETUP_INSTANCE_ID）
//   TransformObjectToHClip       -> Core.hlsl 内 SpaceTransforms.hlsl 提供
//   SAMPLE_TEXTURE2D             -> API/D3D11/.../Texture.hlsl（由 Core.hlsl 引入）
//   注意：符号存在不等于 include 链自洽；仍必须在有渲染路径的编辑器里 play 一次 + read_console
//   确认 0 shader error（本文件作者无法启动 Unity 进程，此项留给协调者收口）。
// ============================================================================

Shader "PirateCrew/Fx/Additive"
{
    Properties
    {
        _BaseMap    ("贴图", 2D) = "white" {}
        _Color      ("叠加色", Color) = (1, 1, 1, 1)
        _Intensity  ("发光强度", Range(0, 8)) = 1.0
        // 0 正常 / 1 只 alpha / 2 只顶点色 / 3 只贴图 RGB
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

        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "FxAdditive"
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

                // 档 1：只 alpha。预期：白色灰度软斑，黑色为完全透明区；若全黑 -> 贴图 alpha 全 0。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(col.a, col.a, col.a, 1.0h);
                // 档 2：只顶点色。预期：爆炸为暖色渐变/粒子系统起始色；若纯白 -> 顶点色未写入。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(IN.color.rgb, 1.0h);
                // 档 3：只贴图 RGB。预期：形状类贴图为纯白（颜色靠 _Color/顶点色乘），
                //        烟团为灰色云纹、木屑为棕色木条；若全黑 -> 采样坐标/贴图未绑定。
                if (_DebugMode > 2.5)
                    return half4(tex.rgb, 1.0h);

                // 加法混合：alpha 决定"叠多少"，RGB 决定"叠什么色"。
                col.rgb *= _Intensity;
                return col;
            }
            ENDHLSL
        }
    }

    // 不回退：回退会把材质悄悄换成别的 shader 的外观，掩盖"shader 没找到"的问题。
    Fallback Off
}
