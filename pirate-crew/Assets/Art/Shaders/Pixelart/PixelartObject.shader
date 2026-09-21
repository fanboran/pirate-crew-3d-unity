// ============================================================================
// PixelartObject.shader —— 像素化着色路径的**物体 pass**
//
// 【本 pass 只写数据，不着色】输出三张 MRT（低分辨率 G-buffer）：
//   SV_Target0  albedo   rgb = 本物体亮部色；a = 1（着色 pass 用 a 判"这里有几何"）
//   SV_Target1  normal   世界法线，原样存 [-1,1]（ARGBHalf，不做 ×0.5+0.5 编码——
//                        编码只是 8 位时代的省空间手段，本路径用 16 位浮点，编解码是多余风险）
//   SV_Target2  prop     r = 色带档数  g = 抖动偏移（0..1，中心 0.5）  b = 法线边加成档  a = AA 缩放
//
// 【为什么档数/抖动幅度是"逐物体"而不是全局】v3 把它们放在 Shape/Palette 缓冲里逐物体给
//   （`DefaultPass.hlsl:70-80`），理由很实在：远景大平面与近景角色需要不同的色带档数，
//   全局常量做不到。本路径只保留真正会被逐物体调的三个量，砍掉 v3 的优先级、
//   法线边阈值、AA 缩放、outline 位——前两个在 v3 里分别是死 pass 的输入与连通域参数，
//   后两个属于 P4/P5。
//
// 【抖动坐标为什么就是 positionCS】几何是**直接渲进低分辨率 RT** 的，所以
//   `positionCS.xy` 天然就是低分辨率像素坐标——一个低分辨率像素一个图案纹素，
//   不存在"块对齐"问题（那是"全分辨率渲染+事后降采"架构才需要的补丁）。
//   图案形状可选：0 = 4×4 Bayer 矩阵（有序抖动，16 级渐变态）、
//   1 = 1-bit 密度图案纹理（v3 口径，两态"撕边"）。两者不可叠加，由 _DitherMode 二选一。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartObject"
{
    Properties
    {
        _BaseColor          ("亮部色（albedo）", Color) = (0.93, 0.85, 0.68, 1.0)
        _MainLightLevel     ("色带档数（1 = 不量化；逐物体可调）", Range(1.0, 8.0)) = 3.0
        _DitherMode         ("抖动图案 0=Bayer4 1=1-bit密度图案（v3）", Range(0.0, 1.0)) = 0.0
        _DitherStrength     ("抖动幅度（占一个色带步长的比例；0 = 观感上等于关）", Range(0.0, 1.0)) = 0.5
        _DitherPattern      ("1-bit 密度图案（v3 口径；需 Point/Repeat 导入）", 2D) = "gray" {}
        _DitherPatternSize  ("图案边长（纹素）：4/6/8/16，须与图案资产一致", Float) = 4.0
        _NormalEdgeLevel    ("法线边加成档（0 = 不加深法线转折线；P4 生效）", Range(0.0, 1.0)) = 0.0
        _AAScale            ("抗锯齿缩放（P4 连通域用；本阶段未消费，先占位）", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "PixelartOpaque"
            // 【独立 LightMode 铁律】这条不是 URP 任何标准 pass 的 tag，所以标准不透明 pass
            // 在此材质上匹配不到东西——几何只由 PixelartObjectFeature 画一次。
            Tags { "LightMode" = "PixelartOpaque" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   PixelartObjectVertex
            #pragma fragment PixelartObjectFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _MainLightLevel;
                float  _DitherMode;
                float  _DitherStrength;
                float  _DitherPatternSize;
                float  _NormalEdgeLevel;
                float  _AAScale;
            CBUFFER_END

            TEXTURE2D(_DitherPattern);
            // 用 inline 点采样 + Repeat：图案是数据纹理，采样必须一纹素一像素、且 UV 会越界
            //（UV = 像素坐标 ÷ 边长）。靠资产导入设置虽然也行，但导入设置被改坏就静默糊掉。
            SAMPLER(sampler_point_repeat);

            // 4×4 Bayer，中点归一化 (m+0.5)/16（调研-赛璐璐 §4：直接 /16 会整体偏亮）。
            static const int kBayer4[16] =
            {
                 0,  8,  2, 10,
                12,  4, 14,  6,
                 3, 11,  1,  9,
                15,  7, 13,  5
            };

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct GBufferOut
            {
                half4 albedo : SV_Target0;
                half4 normal : SV_Target1;
                half4 prop   : SV_Target2;
            };

            Varyings PixelartObjectVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

                output.positionCS = pos.positionCS;
                output.normalWS   = nrm.normalWS;
                return output;
            }

            // 抖动取值 [0,1]（0.5 = 无偏移），两种图案二选一。
            half DitherValue(uint2 pixelCoord)
            {
                half value;
                if (_DitherMode < 0.5)
                {
                    value = (kBayer4[(pixelCoord.y & 3) * 4 + (pixelCoord.x & 3)] + 0.5) / 16.0;
                }
                else
                {
                    float2 uv = pixelCoord / max(_DitherPatternSize, 1.0);
                    value = SAMPLE_TEXTURE2D(_DitherPattern, sampler_point_repeat, uv).r;
                }

                // 【幅度在这里施加，不在着色 pass 里】着色 pass 只负责"读出偏移量"，
                // 逐物体的幅度是材质的旋钮。第一版漏了这一步（_DitherStrength 只声明没用），
                // 于是抖动恒等于满幅度——症状是"关着也有规律点阵"，即一个 4 低分辨率像素周期的网。
                return (value - 0.5) * _DitherStrength + 0.5;
            }

            GBufferOut PixelartObjectFragment(Varyings input)
            {
                GBufferOut output;

                output.albedo = half4(_BaseColor.rgb, 1.0);

                float3 normalWS = normalize(input.normalWS);
                output.normal = half4(normalWS, 1.0);

                uint2 pixelCoord = (uint2)input.positionCS.xy;
                half dither = DitherValue(pixelCoord);

                output.prop = half4(_MainLightLevel, dither, _NormalEdgeLevel, _AAScale);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
