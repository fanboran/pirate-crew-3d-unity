// ============================================================================
// PirateMinimapTopFlat.shader —— 海盗军团夺宝 3D / 战斗小地图顶视平色
//
// 【用途】海图窗体（HUD 顶部正中）的顶视缩略底图：播放器出图组件
//   MinimapTopViewCapture 把场景 renderer 临时换成本 shader 的平色材质、
//   从正上方渲一帧低分辨率图存 PNG（Resources/Minimaps/level-<关卡号>.png）。
//   运行时只贴图 + 单位点，不做任何实时渲染。
//
// 【为什么不是 URP/Unlit】顶视缩略图只需要"每物体一个纯色"，不需要贴图/光照/雾；
//   单 Pass 纯色输出的编译面最小（Always Included 会强制编译全部变体，
//   见 ArtGate.RuntimeFindShaderNames 注释里 Particles-Unlit 连爆 OOM 的教训）。
//
// 【Pass 与 LightMode】单 Pass，用 "UniversalForward"——顶视相机走普通 URP
//   前向渲染器（不是像素化管线的 PixelartCast 渲染器，那条路的 PixelartOpaque
//   材质只写 G-buffer、没有可被普通相机取用的颜色 pass）。同 shader 内不得再
//   出现同 LightMode 的 Pass（会被 URP 静默丢弃，见 docs/描边Shader调试.md §八-2）。
//
// 【入构建保障】只被运行时按名 Shader.Find 取用、无材质资产引用，
//   由 ArtGate.RuntimeFindShaderNames 登记进 Always Included Shaders
//   （渲染管线.md §8 静默失效点 1 的口径；名字常量在 MinimapTopViewCapture.TopFlatShaderName）。
//
// 【[CHECK]】Core.hlsl 提供变换宏；需在有渲染路径的会话里确认 0 shader error
//   （播放器出图即第一次真编译，出图前先过构建门禁的 "shader error" grep）。
// ============================================================================

Shader "PirateCrew/Minimap/TopFlat"
{
    Properties
    {
        _BaseColor ("平色", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite On
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "TopFlat"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TopFlatVertex
            #pragma fragment TopFlatFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings TopFlatVertex(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 TopFlatFragment(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
