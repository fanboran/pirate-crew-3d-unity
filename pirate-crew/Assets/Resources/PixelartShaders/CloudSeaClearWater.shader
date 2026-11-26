// ============================================================================
// CloudSeaClearWater.shader —— 云端漫步海面：空岛下方的清澈透明水（PostLine 前向透明趟）。
//
// 【画法】透明、不写深度、不进深度 / 法线预通道——水面自身不描边，海床与水下物从
//   _CameraOpaqueTexture 透出来，水只做这几件事：
//   ① 岛影两档：采主光阴影图，step 硬切成「阳光水色 / 岛影水色」两块；查影点按波高沿光向偏移，
//      影子与水面的交界线随波起伏（水面的波浪不直接画，靠这条线的运动读出来）；
//   ② 像素微折射：世界格 + 时间分格的低频噪声给出折射方向，偏移取整到艺术像素——
//      水下画面成片缓移，不糊、不逐帧闪；
//   ③ 阳光区的波纹亮带与碎金：噪声条带过阈值；碎金按艺术像素哈希、定帧重掷。
//
// 【取样板】程序纹理与岛影一律在艺术像素中心求值（CloudSeaCommon.hlsl）：同一艺术像素内
//   的片元同色，降采样后色块边缘落在像素格上。
//
// 【深度安全门】折射点若落在比水面更近的物体上（云台、立柱），该像素回退为不偏移，
//   不把前景物体的边拽进水里。
//
// 【岛影从哪来】主光的硬阴影由 CloudSeaSunShadow（挂 CloudWalk 根）在运行时打开；
//   没开时阴影衰减恒 1，整片是阳光水色，不报错。
//
// 【颜色默认值是 sRGB】ShaderLab 颜色默认值、材质里的颜色属性、Inspector 取色器同一口径：按
//   sRGB 写，引擎上传时转线性。改默认值直接写调色板的 sRGB 数字；脚本 SetColor 不要先 .linear。
// 【调参速查】看清海床 ↔ 调 _WaterOpacity；影子的深浅 ↔ _ShadowWaterColor / _ShadowOpacity；
//   水下画面晃动的强弱 ↔ _RefractionPixels（偏移量，艺术像素）/ _WobbleFps（快慢）；
//   影子交界线起伏 ↔ _ShadowWaveAmplitude（幅度）/ _ShadowWaveLength（波长）/ _ShadowWaveSpeed（海床、
//   水下生物上的影边跟着同一条波，见 CloudSeaCommon.hlsl 的 CloudSeaShadowProbe）；
//   亮带多少 ↔ _CrestCoverage；碎金多少 ↔ _SparkleThreshold（0.88 多、0.95 少）。
// ============================================================================

Shader "PirateCrew/CloudSea/CloudSeaClearWater"
{
    Properties
    {
        [Header(Water Tint)]
        _SunWaterColor ("阳光区水色", Color) = (0.2, 0.7, 0.72, 1)
        _ShadowWaterColor ("岛影区水色", Color) = (0.01, 0.13, 0.3, 1)
        _WaterOpacity ("水色不透明度（越大海床越看不清）", Range(0.0, 1.0)) = 0.01
        _ShadowOpacity ("岛影区额外加深", Range(0.0, 0.6)) = 0.6

        [Header(Pixel Refraction)]
        _RefractionPixels ("折射最大偏移（艺术像素；0 = 无折射）", Range(0.0, 4.0)) = 2.2
        _WobbleCell ("折射块边长（米）", Range(0.1, 4.0)) = 0.33
        _WobbleFps ("晃动帧率（次/秒，定帧不插值）", Range(1, 30)) = 8

        [Header(Crest Bands)]
        [ToggleUI] _CrestEnable ("波纹亮带开关", Float) = 1
        _CrestColor ("亮带色", Color) = (0.36, 0.78, 0.79, 1)
        _CrestOpacity ("亮带浓度", Range(0.0, 1.0)) = 0.69
        _CrestCoverage ("亮带覆盖（越大越多）", Range(0.0, 1.0)) = 0.3
        _CrestScale ("亮带尺度（格/米，越小条越宽）", Range(0.02, 1.0)) = 0.22
        _CrestSpeed ("亮带漂移（噪声格/秒）", Range(0.0, 2.0)) = 0.05
        _CrestFps ("亮带流动帧率（次/秒）", Range(1, 30)) = 6

        [Header(Sparkles)]
        [ToggleUI] _SparkleEnable ("碎金开关", Float) = 1
        _SparkleColor ("碎金色", Color) = (1, 0.956, 0.604, 1)
        _SparkleThreshold ("碎金阈值（越大越稀疏）", Range(0.5, 1.0)) = 0.955
        _SparkleFps ("碎金闪烁帧率（次/秒）", Range(1, 30)) = 5

        [Header(Vertex Wave)]
        [ToggleUI] _WaveEnable ("顶点微起伏开关", Float) = 1
        _WaveAmplitude ("波幅（米）", Range(0.0, 0.2)) = 0.02
        _WaveLength ("波长（米）", Range(1, 30)) = 7
        _WaveSpeed ("波速（米/秒）", Range(0, 4)) = 0.6
        _WaveDirection ("波的行进方向（世界 XZ）", Vector) = (0.8, 0.6, 0, 0)

        [Header(Island Shadow)]
        _ShadowStep ("岛影切点（阴影衰减不低于该值算阳光）", Range(0.0, 1.0)) = 0.5
        // 以下四个是海面材质上的调参面：shader 本体不读，CloudSeaWater 每帧读出后下发成全局，
        // 水面色 / 海床 / 水下生物共用（公式在 CloudSeaCommon.hlsl 的 CloudSeaShadowProbe）。
        _ShadowWaveAmplitude ("影子交界线起伏幅度（米；0 = 不起伏）", Range(0.0, 1.5)) = 0.8
        _ShadowWaveLength ("影子交界线起伏波长（米）", Range(1.0, 12.0)) = 3.2
        _ShadowWaveSpeed ("影子交界线起伏相速度（米/秒）", Range(0.0, 3.0)) = 0.8
        _ShadowWaveFps ("影子交界线定帧帧率（0 = 连续）", Range(0, 30)) = 12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            // Transparent + 1：URP 标准透明档的收件下界实测是 3001（队列 3000 会被漏在档外，
            // 口径见 CloudSeaRain.shader 头注），与雨 / 涟漪材质同队列。
            "Queue" = "Transparent+1"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 【SRP Batcher】材质属性的唯一 cbuffer，字段与 Properties 一一对应。
        CBUFFER_START(UnityPerMaterial)
            half4 _SunWaterColor;
            half4 _ShadowWaterColor;
            float _WaterOpacity;
            float _ShadowOpacity;

            float _RefractionPixels;
            float _WobbleCell;
            float _WobbleFps;

            float _CrestEnable;
            half4 _CrestColor;
            float _CrestOpacity;
            float _CrestCoverage;
            float _CrestScale;
            float _CrestSpeed;
            float _CrestFps;

            float _SparkleEnable;
            half4 _SparkleColor;
            float _SparkleThreshold;
            float _SparkleFps;

            float _WaveEnable;
            float _WaveAmplitude;
            float _WaveLength;
            float _WaveSpeed;
            half4 _WaveDirection;

            float _ShadowStep;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardTransparent"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // 岛影要采主光阴影图：与 PostLineToonLit 同一组关键字（含软阴影四级）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "CloudSeaCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  eyeDepth   : TEXCOORD1;   // 水面该点的视深（深度安全门的基准）
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 顶点微起伏：相位全在 shader 内走 _Time，零脚本开销。幅度只是让水面大轮廓
                // 不至于是一条死直线，不参与折射计算。
                if (_WaveEnable > 0.5 && _WaveAmplitude > 0.0)
                {
                    float2 dir = normalize(_WaveDirection.xz + 1e-5);
                    float waveK = 6.2831853 / max(_WaveLength, 0.1);
                    float phase = dot(positionWS.xz, dir) * waveK - _Time.y * waveK * _WaveSpeed;
                    positionWS.y += _WaveAmplitude * sin(phase);
                }

                output.positionWS = positionWS;
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // ---- 取样板：本艺术像素中心的世界位置（见 CloudSeaCommon.hlsl）----
                float k = max(_PixelartSamplingScale, 1.0);
                float2 screenUV = input.positionCS.xy * _ScreenSize.zw;
                float3 ws = CloudSeaArtPixelWorld(input.positionWS, input.positionCS.xy);

                // ---- ① 岛影两档：主光阴影图硬切（1 = 阳光，0 = 岛影）----
                // 在水面上求值：落在水面上的是云岛与飞行物的影子；水下物的影子只落在海床上
                // （海床 shader 自己收），不会浮到水面上来。
                // 查点按波高偏移（公式在 CloudSeaCommon.hlsl，海床 / 水下生物共用同一个）：
                // 影子与水面的交界线随波起伏——水面的波浪不直接画，靠这条线的运动读出来。
                float sun = CloudSeaSunMask(CloudSeaShadowProbe(ws), _ShadowStep);

                // ---- ② 像素微折射 ----
                // 公式在 CloudSeaAnim.hlsl。
                float2 offsetPixels = CloudSeaRefractOffsetEx(ws.xz, _Time.y, _RefractionPixels, _WobbleCell, _WobbleFps);

                // 深度安全门：折射点的场景深度比水面还近 = 落在前景物体上，回退为不偏移。
                float2 refractedUV = screenUV + offsetPixels * k * _ScreenSize.zw;
                float sceneEye = CloudSeaEyeDepth(SampleSceneDepth(refractedUV));
                float2 sampleUV = sceneEye < input.eyeDepth - 0.05 ? screenUV : refractedUV;
                float3 sceneColor = SampleSceneColor(sampleUV).rgb;

                // ---- ③ 水色：阳光 / 岛影两档硬切，影区更实一点（影子读成一整块深青）----
                float3 waterTint = lerp(_ShadowWaterColor.rgb, _SunWaterColor.rgb, sun);
                float opacity = saturate(_WaterOpacity + (1.0 - sun) * _ShadowOpacity);
                float3 color = lerp(sceneColor, waterTint, opacity);

                // ---- ④ 波纹亮带：顺世界 X 轴拉长的低频噪声过阈值，定帧缓慢漂移；只在阳光区 ----
                float crest = 0.0;
                if (_CrestEnable > 0.5)
                {
                    float crestQ = floor(_Time.y * _CrestFps) / _CrestFps;
                    float streak = CloudSeaNoise(
                        float2(ws.x, ws.z * 5.0) * _CrestScale + crestQ * _CrestSpeed * float2(1.0, 0.35));
                    crest = step(1.0 - _CrestCoverage, streak) * sun;
                    color = lerp(color, lerp(sceneColor, _CrestColor.rgb, _CrestOpacity), crest);
                }

                // ---- ⑤ 碎金：按艺术像素坐标做双层哈希，时间分格重掷；只在阳光区的亮带里 ----
                if (_SparkleEnable > 0.5)
                {
                    // 时间步对 64 s 取模：哈希入参不随时长增长（float 精度到几小时后会糊）。
                    float stepQ = floor(fmod(_Time.y, 64.0) * _SparkleFps);
                    float2 artPixel = floor(input.positionCS.xy / k);
                    float sparkle = step(_SparkleThreshold, CloudSeaHash12(artPixel + stepQ * 17.0))
                        * step(_SparkleThreshold, CloudSeaHash12(artPixel * 1.37 - stepQ * 29.0 + 101.0));
                    float onCrest = _CrestEnable > 0.5 ? crest : sun;
                    color = lerp(color, _SparkleColor.rgb, sparkle * onCrest);
                }

                // 颜色已自行合成完（水下色 + 水色 + 高光），alpha 置 1 让混合原样落盘。
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    // 自带前向透明 pass，不回退内置 shader（回退会丢掉折射与岛影，水面变成一块色板）。
    Fallback Off
}
