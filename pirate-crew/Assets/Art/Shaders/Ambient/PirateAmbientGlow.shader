// ============================================================================
// PirateAmbientGlow.shader —— 环境动效「辉光贴片」（灯笼 / 篝火 / 萤火）
//
// 【解决什么问题】本工程 0 贴图，无法用"径向渐变贴图"做光晕；灯笼若只放一个点光源，
//   在 PirateSurface / PirateOutline 材质上根本不生效（那两个 shader 明确只取主方向光，
//   见 PirateSurface.shader 头注释「不支持点光/聚光」）。故光晕必须**自发光贴片**来表现：
//   在片元里用**局部坐标到中心的距离**算径向衰减（不需要 UV、不需要贴图）。
//
// 【朝向】不做 shader 内 billboard：运行时由 AmbientSwayNode 每帧把贴片朝向相机
//   （低模场景里"灯笼壳 + 一张朝向相机的辉光片"足够读作光源）。
//
// 【Pass / LightMode】只有一个 Pass，LightMode 留默认（不写 Tags），
//   它走 URP 的 SRPDefaultUnlit 通道 —— 与本体 Pass 的 UniversalForward 不冲突。
//   （URP 额外 Pass 撞 LightMode 会被静默丢弃，见 docs/描边Shader调试.md §八-2。）
//
// 【调试】无 _DebugMode（unlit 单色，出错时画面表现为"整片实心圆盘"，一眼可辨）。
//
// 【[CHECK] 依赖符号（本地 URP 14.0.12）】
//   TransformObjectToHClip -> SpaceTransforms.hlsl（经 Core.hlsl）
//   本 shader 只用 Core.hlsl，不用 Lighting.hlsl —— 光晕不该被场景光照/阴影影响。
// ============================================================================
Shader "PirateCrew/Ambient/Glow"
{
    Properties
    {
        _BaseColor    ("辉光颜色", Color) = (1.0, 0.72, 0.34, 1.0)
        _Intensity    ("强度", Range(0.0, 8.0)) = 1.6
        _Radius       ("衰减半径（局部坐标）", Range(0.001, 8.0)) = 0.35
        _FalloffPower ("衰减指数", Range(0.5, 6.0)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "Glow"

            // 加性混合 + 不写深度：辉光只加亮，不遮挡任何东西（可读性红线：装饰不遮挡）。
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex   GlowVertex
            #pragma fragment GlowFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Intensity;
                float  _Radius;
                float  _FalloffPower;
            CBUFFER_END

            struct AttributesGlow
            {
                float4 positionOS : POSITION;
            };

            struct VaryingsGlow
            {
                float4 positionCS : SV_POSITION;
                float2 localXY    : TEXCOORD0;
            };

            VaryingsGlow GlowVertex(AttributesGlow IN)
            {
                VaryingsGlow OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localXY = IN.positionOS.xy;
                return OUT;
            }

            half4 GlowFragment(VaryingsGlow IN) : SV_Target
            {
                // 径向衰减：中心 1 → 边缘 0（贴片本身就画在局部 XY 平面上）。
                half r = saturate(length(IN.localXY) / max(_Radius, 1e-4));
                half falloff = pow(1.0h - r, (half)_FalloffPower);
                half a = falloff * (half)_Intensity * (half)_BaseColor.a;

                // Blend SrcAlpha One：输出 rgb × a 被加到帧缓冲。
                return half4(_BaseColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
