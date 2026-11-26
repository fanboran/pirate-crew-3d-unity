// ============================================================================
// CloudSeaUnderwater.shader —— 云端漫步水下生态：海草 / 珊瑚 / 岩石 / 贝 / 鱼 / 蟹 / 海星……
// 共用的一支平面卡通 shader（PostLine 前向 + 投影）。
//
// 【为什么单独一支、不走 PostLineToonLit】
//   · 不描边：本 shader 没有 DepthOnly / DepthNormals 通道，PostLine 的深度 / 法线纹理里没有这些物体，
//     描边判据看不到它们——鱼、珊瑚、摇曳的海草周围不会冒出墨线（海草逐帧摆动，描了线必抖）。
//   · 影子照有：带 ShadowCaster 通道，生物 / 岩石 / 珊瑚在海床上投出真实的硬影。
//
// 【平面两档卡通色】固有色 ×（受光面乘色 | 背光面乘色），硬切；再乘岛影 / 云影的冷色。
//   明暗档位在**艺术像素中心**求值（法线按屏幕导数外推到像素中心）：同一艺术像素内的片元同档，
//   色带边缘落在像素格上，没有半透明过渡带。颜色不乘光照全局，直接对着调色板配。
//   鳍 / 眼白 / 瞳孔这类要纯平的件：材质上把两个乘色配成同值。
//
// 【蒙皮】生物的骨架由运行时的运动学控制器每帧解出来（CloudSeaLife），关节矩阵放在全局缓冲
//   _CloudSeaJoints 里，顶点着色器按顶点上的权重自己蒙皮（CloudSeaAnim.hlsl 的 CloudSeaSkin；
//   关节号与权重写在三套 UV 上）。前向与投影两个通道调同一个函数——影子和身体同步。
//   海草不走蒙皮，另走「离根高度比」摆动（uv0.x），同样两个通道同函数。
//
// 【颜色默认值是 sRGB】ShaderLab 颜色默认值、材质里的颜色属性、Inspector 取色器同一口径：按
//   sRGB 写，引擎上传时转线性。改默认值直接写调色板的 sRGB 数字；脚本 SetColor 不要先 .linear。
// 【调参速查】某件东西看不清 ↔ 调亮材质的 _BaseColor；暗面太死 ↔ 提 _ShadowColor；
//   明暗分界位置 ↔ _BandCut；海草摆幅 / 快慢 ↔ _SwayAmplitude / _SwaySpeed；要定格动画感 ↔ _SwayFps。
// ============================================================================

Shader "PirateCrew/CloudSea/CloudSeaUnderwater"
{
    Properties
    {
        [Header(Albedo And Bands)]
        _BaseColor ("固有色", Color) = (1, 1, 1, 1)
        _HighlightColor ("受光面色（乘固有色）", Color) = (1.04, 1.02, 0.96, 1)
        _ShadowColor ("背光面色（乘固有色，偏冷）", Color) = (0.68, 0.74, 0.92, 1)
        _BandCut ("明暗切点（越大暗面越多）", Range(0.0, 1.0)) = 0.5
        _Wrap ("半兰伯特偏移（越大暗面越少）", Range(0.0, 0.5)) = 0.25
        [ToggleUI] _TwoSidedLight ("薄片（鳍 / 海草）", Float) = 0

        [Header(Island Shadow)]
        _ShadowStep ("岛影切点（阴影衰减不低于该值算阳光）", Range(0.0, 1.0)) = 0.5
        _IslandShadowTint ("影区乘色（偏冷，不压死）", Color) = (0.70, 0.78, 0.95, 1)

        [Header(Sway)]
        _SwayAmplitude ("梢部摆幅（米）", Range(0.0, 0.5)) = 0.0
        _SwaySpeed ("摆动角速度（弧度/秒）", Range(0.0, 6.0)) = 1.2
        _SwayFps ("定帧帧率（次/秒，0 = 连续）", Range(0, 30)) = 0
        _SwayDirection ("摆动方向（世界 XZ）", Vector) = (1, 0.35, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "CloudSeaAnim.hlsl"

        // 【SRP Batcher】材质属性的唯一 cbuffer，两个 pass 共享这一份定义。
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _HighlightColor;
            half4 _ShadowColor;
            float _BandCut;
            float _Wrap;
            float _TwoSidedLight;

            float _ShadowStep;
            half4 _IslandShadowTint;

            float _SwayAmplitude;
            float _SwaySpeed;
            float _SwayFps;
            half4 _SwayDirection;
        CBUFFER_END

        // 海草摆动位移（前向与投影共用）。rootWS = 未位移的世界位置；uv0.x = 离根高度比。
        // 生物的 uv0.x 是关节号，不能进这条——uv2.x = 1 的（蒙皮标记）只走 CloudSeaSkin。
        float3 CloudSeaSwayOffset(float3 rootWS, float2 uv0, float2 uv2)
        {
            if (uv2.x >= 0.5)
                return 0.0.xxx;
            float weight = uv0.x * uv0.x;
            if (weight <= 0.0 || _SwayAmplitude <= 0.0)
                return 0.0.xxx;

            // 相位取自世界 XZ：相邻植株不同步，整片海草不会像一块布一起摆。
            // _SwayFps > 0 时时间分格——逐格跳的定格动画感。
            float t = _SwayFps > 0.5 ? floor(_Time.y * _SwayFps) / _SwayFps : _Time.y;
            float swing = sin(t * _SwaySpeed + rootWS.x * 0.9 + rootWS.z * 1.1)
                + 0.35 * sin(t * _SwaySpeed * 2.3 + rootWS.z * 2.7);
            float2 dir = normalize(_SwayDirection.xz + 1e-5);
            return float3(dir.x, 0.0, dir.y) * (swing * _SwayAmplitude * weight);
        }

        // 生物形变 + 海草摆动：物体空间进，世界空间出。前向与投影只走这一条，影子才跟得上身体。
        float3 CloudSeaPlace(float3 positionOS, float2 uv0, float2 uv2)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            positionWS += CloudSeaSwayOffset(positionWS, uv0, uv2);
            return positionWS;
        }

        // 生物蒙皮 + 海草摆动：物体空间进，世界空间出。前向与投影只走这一条，影子才跟得上身体。
        float3 CloudSeaPlaceSkinned(float3 positionOS, inout float3 normalOS, float2 uv0, float2 uv1,
                                    float2 uv2)
        {
            CloudSeaSkin(positionOS, normalOS, uv0, uv1, uv2);
            return CloudSeaPlace(positionOS, uv0, uv2);
        }

        // 所有通道共用的输入 / 输出结构（这里的 pass 都不引 URP 标准 pass 的同名结构体）。
        struct CloudSeaAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;   // 生物：(关节号 A, 权重 A)；海草：uv.x = 离根高度比
            float2 uv1        : TEXCOORD1;   // 生物：(关节号 B, 权重 B)
            float2 uv2        : TEXCOORD2;   // 生物：(1 = 要蒙皮, 个体种子)；其余物体没有这一层
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct CloudSeaVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float  swayWeight : TEXCOORD2;   // 海草：离根高度比的平方（拾回权重用）；其余 0
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        CloudSeaVaryings CloudSeaVertex(CloudSeaAttributes input)
        {
            CloudSeaVaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 normalOS = input.normalOS;
            output.positionWS = CloudSeaPlaceSkinned(input.positionOS.xyz, normalOS, input.uv, input.uv1,
                input.uv2);
            output.normalWS = TransformObjectToWorldNormal(normalOS);
            output.swayWeight = input.uv.x * input.uv.x;
            output.positionCS = TransformWorldToHClip(output.positionWS);
            return output;
        }
        ENDHLSL

        // ==================================================================
        // Pass 1 / ForwardLit：平面两档色 × 岛影 / 云影。
        // ==================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   CloudSeaVertex
            #pragma fragment frag

            // 岛影要采主光阴影图：与 PostLineToonLit 同一组关键字（含软阴影四级）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "CloudSeaCommon.hlsl"

            half4 frag(CloudSeaVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 取样板：把世界位置和法线都外推到本艺术像素的中心（CloudSeaCommon.hlsl）。
                float k = max(_PixelartSamplingScale, 1.0);
                float2 dpx = (floor(input.positionCS.xy / k) + 0.5) * k - input.positionCS.xy;
                float3 ws = input.positionWS + ddx(input.positionWS) * dpx.x + ddy(input.positionWS) * dpx.y;
                float3 normalWS = normalize(input.normalWS
                    + ddx(input.normalWS) * dpx.x + ddy(input.normalWS) * dpx.y);

                // 岛影查点按波高偏移（CloudSeaShadowProbe）：生物身上的明暗边与水面色、海床上的影边同一条波。
                float sun = CloudSeaSunMask(CloudSeaShadowProbe(ws), _ShadowStep);

                // 两档：半兰伯特过切点算受光。薄片取 |N·L|，整片海草的背面不会全落进暗面。
                float ndl = dot(normalWS, CloudSeaLightDir(GetMainLight().direction));
                if (_TwoSidedLight > 0.5)
                    ndl = abs(ndl);
                float lit = step(_BandCut, ndl * 0.5 + 0.5 + _Wrap);

                float3 color = _BaseColor.rgb * lerp(_ShadowColor.rgb, _HighlightColor.rgb, lit);
                color *= lerp(_IslandShadowTint.rgb, float3(1.0, 1.0, 1.0), sun);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2 / ShadowCaster：写主光阴影图（生物 / 岩石 / 珊瑚 / 海草在海床上的影子）。
        // 顶点着色器手写：URP 官方 ShadowPassVertex 不知道顶点形变，影子会和身体分离；
        // 这里在官方版本之上只多做一步形变，其余（ApplyShadowBias、近裁面夹取）逐行相同。
        // ==================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   CloudSeaShadowVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            // 【include 顺序硬要求】Core → CommonMaterial → ShadowCasterPass：Shadows.hlsl 的
            // LerpWhiteTo 定义在 CommonMaterial.hlsl 而不自引，漏掉中间那行会报 undeclared identifier。
            // _LightDirection / _LightPosition / ShadowPassFragment 都来自 ShadowCasterPass。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            // 官方 Varyings 只带 positionCS，直接复用。
            Varyings CloudSeaShadowVertex(CloudSeaAttributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalOS = input.normalOS;
                float3 positionWS = CloudSeaPlaceSkinned(input.positionOS.xyz, normalOS, input.uv,
                    input.uv1, input.uv2);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

                output.positionCS = positionCS;
                return output;
            }
            ENDHLSL
        }
    }

    // 自带两个 pass，不回退内置 Lit（回退会多出深度法线通道，水下物全部被描边）。
    Fallback Off
}
