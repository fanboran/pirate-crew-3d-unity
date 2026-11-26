// ============================================================================
// CloudSeaSeabed.shader —— 云端漫步海床：起伏沙丘的卡通沙地（PostLine 前向 + 深度法线）。
//
// 【画法】不透明、写深度；颜色全部程序生成、不吃贴图，也不乘光照全局——阳光下的颜色就是
//   材质上填的颜色，方便对着调色板直接配。从下往上叠七层：
//   ① 沙色按世界高度分三档（洼地偏冷、脊顶偏暖）——起伏的沙丘靠颜色就读得出来；
//   ② 大色斑：低频噪声换一种沙色，打散大面积单色；
//   ③ 藻斑：偏洼地长的绿灰色斑；
//   ④ 沙纹：沿走向的细条纹，低频扭曲成波浪状，线宽按艺术像素计；
//   ⑤ 卵石：每格按哈希决定有没有、偏哪、多大；
//   ⑥ 焦散：两层低频噪声各取「值 ≈ 0.5 的脊线」，合成缓慢漂移的亮网，只在阳光区出现；
//   ⑦ 坡面两档明暗 × 岛影两档，都是硬切。
//
// 【取样板】以上各层与岛影都在艺术像素中心求值（CloudSeaCommon.hlsl）：细条纹与焦散线
//   不会被降采样平均成脏色。
//
// 【为什么带 DepthNormals】PostLine 的深度 / 法线纹理只收带 DepthNormals 通道的物体。
//   海床写进去，云台立柱插进水里那一段与海床之间才有深度差，立柱的水下部分照常描边。
//   海床自身是缓坡，逐像素深度差远低于剪影阈值，不会被描出线。
//
// 【高度分档用的是世界 Y】_HighY / _LowY 以 CloudWalk 根的世界高度 0 为基准；整件 prefab
//   上下挪了位置要同步改这两个值。
//
// 【颜色默认值是 sRGB】ShaderLab 颜色默认值、材质里的颜色属性、Inspector 取色器同一口径：按
//   sRGB 写，引擎上传时转线性。改默认值直接写调色板的 sRGB 数字；脚本 SetColor 不要先 .linear。
// 【调参速查】沙丘起伏读不读得出 ↔ _LowColor 与 _BaseColor 的冷暖差；沙纹疏密 ↔ _RippleSpacing；
//   焦散网眼大小 ↔ _CausticScale，亮线粗细 ↔ _CausticWidth，多少 ↔ _CausticThreshold；
//   岛影里的沙色 ↔ _ShadowTint。
// ============================================================================

Shader "PirateCrew/CloudSea/CloudSeaSeabed"
{
    Properties
    {
        [Header(Sand Height Bands)]
        _BaseColor ("脊顶沙色", Color) = (0.88, 0.8, 0.32, 1)
        _LowColor ("洼地沙色（偏冷）", Color) = (0.34, 0.6, 0.56, 1)
        _HighY ("脊顶高度（世界 Y，及以上全取脊顶色）", Float) = -2.2
        _LowY ("洼地高度（世界 Y，及以下全取洼地色）", Float) = -3.0

        [Header(Patches)]
        [ToggleUI] _PatchEnable ("大色斑开关", Float) = 1
        _PatchColor ("斑块色", Color) = (0.78, 0.7, 0.3, 1)
        _PatchScale ("斑块尺度（格/米，越小越大片）", Range(0.01, 1.0)) = 0.06
        _PatchCoverage ("斑块覆盖（越大越多）", Range(0.0, 1.0)) = 0.3

        [Header(Sand Ripples)]
        [ToggleUI] _RippleEnable ("沙纹开关", Float) = 1
        _RippleColor ("沙纹色", Color) = (0.72, 0.65, 0.28, 1)
        _RippleSpacing ("沙纹间距（米）", Range(0.15, 3.0)) = 0.75
        _RippleAngle ("沙纹走向（度，世界 XZ）", Range(0, 180)) = 90
        _RippleWidth ("沙纹线宽（艺术像素）", Range(0.5, 4.0)) = 1.0
        _RippleWarp ("沙纹扭曲", Range(0.0, 2.0)) = 0.18

        [Header(Algae)]
        [ToggleUI] _AlgaeEnable ("藻斑开关", Float) = 1
        _AlgaeColor ("藻斑色", Color) = (0.262, 0.478, 0.214, 1)
        _AlgaeScale ("藻斑尺度（格/米，越小越大片）", Range(0.02, 1.0)) = 0.07
        _AlgaeCoverage ("藻斑覆盖（越大越多）", Range(0.0, 1.0)) = 0.09
        _AlgaeLowBias ("藻斑偏洼地（越大越只在低处）", Range(0.0, 1.0)) = 0.5

        [Header(Pebbles)]
        [ToggleUI] _PebbleEnable ("卵石开关", Float) = 1
        _PebbleColor ("卵石色", Color) = (0.478, 0.478, 0.369, 1)
        _PebbleCell ("卵石格（米）", Range(0.05, 1.0)) = 0.14
        _PebbleDensity ("卵石密度（0 ~ 0.5）", Range(0.0, 0.5)) = 0.05

        [Header(Caustics In Sun)]
        [ToggleUI] _CausticEnable ("焦散开关", Float) = 1
        _CausticColor ("焦散亮色", Color) = (1, 0.976, 0.672, 1)
        _CausticStrength ("焦散强度", Range(0.0, 1.0)) = 0.5
        _CausticThreshold ("焦散阈值（越大网越稀、线越细）", Range(0.0, 1.0)) = 0.6
        _CausticScale ("焦散网眼（米）", Range(0.5, 8.0)) = 3.2
        _CausticWidth ("焦散线宽（米）", Range(0.02, 0.5)) = 0.07
        _CausticSpeed ("焦散漂移（网眼/秒）", Range(0.0, 1.0)) = 0.08
        _CausticFps ("焦散流动帧率（次/秒）", Range(1, 30)) = 8

        [Header(Slope And Island Shadow)]
        _SlopeThreshold ("背光坡面切点（N·L 低于它算背光）", Range(-1.0, 1.0)) = 0.35
        _SlopeTint ("背光坡面乘色", Color) = (0.84, 0.88, 0.94, 1)
        _ShadowStep ("岛影切点（阴影衰减不低于该值算阳光）", Range(0.0, 1.0)) = 0.5
        _ShadowTint ("岛影乘色（偏冷青）", Color) = (0.5, 0.66, 0.88, 1)
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

        // 【SRP Batcher】材质属性的唯一 cbuffer，三个 pass 共享这一份定义。
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LowColor;
            float _HighY;
            float _LowY;

            float _PatchEnable;
            half4 _PatchColor;
            float _PatchScale;
            float _PatchCoverage;

            float _RippleEnable;
            half4 _RippleColor;
            float _RippleSpacing;
            float _RippleAngle;
            float _RippleWidth;
            float _RippleWarp;

            float _AlgaeEnable;
            half4 _AlgaeColor;
            float _AlgaeScale;
            float _AlgaeCoverage;
            float _AlgaeLowBias;

            float _PebbleEnable;
            half4 _PebbleColor;
            float _PebbleCell;
            float _PebbleDensity;

            float _CausticEnable;
            half4 _CausticColor;
            float _CausticStrength;
            float _CausticThreshold;
            float _CausticScale;
            float _CausticWidth;
            float _CausticSpeed;
            float _CausticFps;

            float _SlopeThreshold;
            half4 _SlopeTint;
            float _ShadowStep;
            half4 _ShadowTint;
        CBUFFER_END
        ENDHLSL

        // ==================================================================
        // Pass 1 / ForwardLit：七层程序纹理 + 两档坡面 × 两档岛影。
        // ==================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // 岛影要采主光阴影图：与 PostLineToonLit 同一组关键字（含软阴影四级）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "CloudSeaCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 取样板：各层纹理与岛影都在艺术像素中心的世界位置上求值。
                // 岛影查点按波高偏移（CloudSeaShadowProbe）：和水面色上的影边是同一条波，两边同步起伏。
                float3 ws = CloudSeaArtPixelWorld(input.positionWS, input.positionCS.xy);
                float2 xz = ws.xz;
                // 全局缺失时的兜底：1 艺术像素 = 1/24 米（画布 960 宽、可视 40 米的口径）。
                float unit = _PixelartUnitSize > 0.0 ? _PixelartUnitSize : (1.0 / 24.0);
                float sun = CloudSeaSunMask(CloudSeaShadowProbe(ws), _ShadowStep);

                // ---- ① 沙色按高度分三档 ----
                float height = saturate((ws.y - _LowY) / max(_HighY - _LowY, 0.01));
                float3 albedo = lerp(_LowColor.rgb, _BaseColor.rgb, floor(height * 3.0) / 2.0);

                // ---- ② 大色斑 ----
                if (_PatchEnable > 0.5)
                {
                    float patch = step(1.0 - _PatchCoverage, CloudSeaNoise(xz * _PatchScale + 7.3));
                    albedo = lerp(albedo, _PatchColor.rgb, patch);
                }

                // ---- ③ 藻斑：噪声减去高度偏置，越高的地方越不容易长 ----
                if (_AlgaeEnable > 0.5)
                {
                    float algaeNoise = CloudSeaNoise(xz * _AlgaeScale + 19.1) - height * _AlgaeLowBias * 0.5;
                    albedo = lerp(albedo, _AlgaeColor.rgb, step(1.0 - _AlgaeCoverage, algaeNoise));
                }

                // ---- ④ 沙纹：沿走向坐标 + 侧向低频扭曲，硬切成 1 像素细线 ----
                if (_RippleEnable > 0.5)
                {
                    float ang = radians(_RippleAngle);
                    float2 dir = float2(cos(ang), sin(ang));
                    float across = dot(xz, float2(-dir.y, dir.x));
                    float along = dot(xz, dir) + _RippleWarp * 0.30 * sin(across * 0.9 + 1.7);
                    float stripe = step(frac(along / _RippleSpacing),
                        saturate(_RippleWidth * unit / _RippleSpacing));
                    albedo = lerp(albedo, _RippleColor.rgb, stripe);
                }

                // ---- ⑤ 卵石 ----
                if (_PebbleEnable > 0.5)
                {
                    float2 pebbleUv = xz / _PebbleCell;
                    float2 h = CloudSeaHash22(floor(pebbleUv));
                    float2 d = (frac(pebbleUv) - (0.3 + 0.4 * h)) / (0.10 + 0.10 * frac(h.x * 7.31));
                    albedo = lerp(albedo, _PebbleColor.rgb, step(dot(d, d), 1.0) * step(h.x, _PebbleDensity));
                }

                // ---- ⑥ 焦散：两层脊线相加过阈值 → 亮网；岛影里没有 ----
                float caustic = 0.0;
                if (_CausticEnable > 0.5)
                {
                    float causticQ = floor(_Time.y * _CausticFps) / _CausticFps;
                    float halfWidth = _CausticWidth / max(_CausticScale, 0.01);
                    float ridgeA = 1.0 - saturate(abs(CloudSeaNoise(
                        xz / _CausticScale + causticQ * _CausticSpeed * float2(0.8, 0.3)) - 0.5) / halfWidth);
                    float ridgeB = 1.0 - saturate(abs(CloudSeaNoise(
                        xz / (_CausticScale * 0.77) - causticQ * _CausticSpeed * float2(0.4, 0.9) + 31.7)
                        - 0.5) / halfWidth);
                    caustic = step(_CausticThreshold, ridgeA + ridgeB) * sun;
                }

                // ---- ⑦ 合成：坡面两档 × 岛影两档，焦散最后叠在亮色上 ----
                float3 normalWS = normalize(input.normalWS);
                float lit = step(_SlopeThreshold, dot(normalWS, CloudSeaLightDir(GetMainLight().direction)));
                float3 shade = lerp(_SlopeTint.rgb, float3(1.0, 1.0, 1.0), lit)
                    * lerp(_ShadowTint.rgb, float3(1.0, 1.0, 1.0), sun);

                float3 color = albedo * shade;
                color = lerp(color, _CausticColor.rgb, caustic * _CausticStrength);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2 / DepthOnly：URP 深度预通道（URP14 官方 DepthOnlyPass.hlsl）。
        // ==================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // ==================================================================
        // Pass 3 / DepthNormals：写法线纹理（PostLine 描边的棱线判据读它）。
        // 直写归一化世界法线，与 PostLineToonLit / URP 自带 Lit 同口径。
        // ==================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    // 自带全部 pass，不回退内置 Lit（回退会丢掉程序纹理，海床变成一块纯色板）。
    Fallback Off
}
