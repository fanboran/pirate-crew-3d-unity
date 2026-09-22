// ============================================================================
// PixelartObject.shader —— 像素化着色路径的**物体 pass**
//
// 【本 pass 只写数据，不着色】输出三张 MRT（低分辨率 G-buffer）：
//   SV_Target0  albedo   rgb = 本物体亮部色；a = 覆盖标记（1 = 这里有几何）
//   SV_Target1  normal   世界法线，原样存 [-1,1]（ARGBHalf，不做 ×0.5+0.5 编码——
//                        编码只是 8 位时代的省空间手段，本路径用 16 位浮点，编解码是多余风险）
//   SV_Target2  prop     r = 色带档数  g = 抖动偏移（0..1，中心 0.5）  b = 法线边加成档
//                        a = **墨线标记（1 = 本像素是墨线）**
//
// 【prop.a 那个坑，别再踩】它一度被写成 `_AAScale`（一个"抗锯齿缩放"占位属性），而那个属性
//   的 Properties 默认值恰好是 **1.0** —— 于是**每一个不透明像素**都满足了着色 pass 里
//   "prop.a > 0.5 即墨线、原样输出 albedo"的旁路条件，整张画面直接跳过全部着色数学。
//   症状：所有面同色、与 albedo 逐位相同、没有任何光影，且没有任何报错。
//   现在这个通道的语义是**排他的**（只有墨线 pass 写 1，本体 pass 必须写 0），
//   且 `_AAScale` 属性已删除；将来要放"边缘遮蔽/连通域"之类的数据时，
//   **必须换一个新通道并同时改着色 pass 的判据**，不许复用。
//
// 【内线（面转折）不在本 pass】蓝本 §8 裁决点 3 的推荐口径是
//   「外轮廓 = 反向壳（本 pass）+ 内部转折 = 连通域降档（P4）」——两者正交。
//   P4 的连通域三张缓冲落地前，内部转折没有输入，本阶段不设该分支。
//
// 【为什么档数/抖动幅度是"逐物体"而不是全局】v3 把它们放在 Palette 缓冲里逐物体给
//   （`DefaultPass.hlsl:72-79`：r=主光档位 g=抖动偏移 b=边光档位 a=applyOutline 位），
//   理由很实在：远景大平面与近景角色需要不同的色带档数，全局常量做不到。
//   本路径的对应关系：Albedo = v3 的 Albedo，Normal = v3 的 Normal0，prop = v3 的 Palette。
//   本仓尚未有连通域，故暂不建 v3 的 Shape 缓冲（priority / normalEdgeThreshold / AAScale
//   三项全部是 P4 连通域与优先级仲裁的输入，见蓝本 §7.2 第 5 条的 4 张缓冲口径）。
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

        // ---- 反向壳描边（外轮廓）----
        _InkColor           ("描边墨色（与 UI 令牌 INK 同色）", Color) = (0.07, 0.05, 0.08, 1.0)
        _OutlinePixels      ("描边线宽（低分辨率像素数；0 = 本物体不描边）", Range(0.0, 4.0)) = 1.0
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
                float4 _InkColor;
                float  _OutlinePixels;
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

                // a = 墨线标记。本体必须写 0（语义排他，见文件头"prop.a 那个坑"）。
                output.prop = half4(_MainLightLevel, dither, _NormalEdgeLevel, 0.0);
                return output;
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / PixelartInk：外轮廓反向壳描边（写进同一批 G-buffer）
        //
        // 【配方来源】本仓六轮实拍定案的配方，口径写在
        //   渲染管线-等距像素卡通.md §5：`Cull Front + ZWrite Off + ZTest LEqual`，
        //   顶点沿法线外扩、**不对 normalCS.xy 二次归一**（ToonRP 生产级公式），
        //   线宽在**低分辨率域**定义（1 RT 像素起步），斜面前的 z-fight 用
        //   **沿视线拉近 5× 线宽**解决（`positionVS.z -= pixelWorld * 5.0`）。
        //   蓝本 §8 裁决点 3 的推荐口径同样是"外轮廓 = 反向壳"（v3 自己的
        //   `OutlinePass.hlsl` 是屏幕空间方案，但它在 v3 里是关着的、且门控依赖 P4 的连通域）。
        //
        // 【三条因果链，缺一条描边就断】（旧链实测结论，逐条都有症状）
        //   ① 壳先画、本体后画：重叠区由本体自然盖回、只余轮廓环。反过来先画本体时，
        //      壳的背面外扩在**本体内侧**（本体更近），LEqual 全灭 ⇒ 一根线都看不到。
        //   ② 壳 **不写深度**（ZWrite Off）：写了深度就会挡住随后画的本体，剪影被墨色糊住。
        //   ③ 沿视线拉近 5× 线宽：正交俯视下**轮廓外侧那圈像素落在地面上、地面比壳更近**
        //      （45° 俯视前下缘的深度差 ~0.4u），不拉近时前缘环输给地面深度。
        //      配套要求大平面（地面/海面）**先于描边物**画完 —— 由 PixelartObjectFeature
        //      按 renderQueue 分段保证（大平面回 1999，描边物在 Geometry）。
        //
        // 【线宽单位 = 低分辨率像素】外扩量 = _PixelartUnitSize × _OutlinePixels，
        //   其中 _PixelartUnitSize 是"1 低分辨率像素的世界尺寸"（BeforeRender 每帧下发）。
        //   它与旧链公式 `2×_OutlinePixels / (RT高 × |P.m11|)` 等价（2/|P.m11| = 2×正交size），
        //   但**不需要 abs/max 钳位**——旧链那个 `max(P.m11, 1e-6)` 曾在投影翻转时把线宽放大
        //   ~5000 倍（壳推出屏幕、调试档全黑的实测事故），走 C# 下发的世界尺度直接绕开。
        //
        // 【墨线不走光照】片元往 G-buffer 写 prop.a = 1 当"这是墨线"的标记，着色 pass 见到就直接
        //   原样输出——否则墨线会被色带量化 + 环境光染成"深蓝的带"，就不是墨线了。
        //
        // 【大平面不描边】_OutlinePixels = 0 时本 pass 整片丢弃（地面/海面这种铺满画面的大平面，
        //   壳环会顶到画面边缘，既无观感意义、拉近补丁还会让壳整面盖住本体）。
        // ====================================================================
        Pass
        {
            Name "PixelartInk"
            Tags { "LightMode" = "PixelartInk" }

            Cull Front
            ZWrite Off
            // 【ZTest Always：这条是实测改出来的，不是照抄】渲染篇 §5 的配方写的是 LEqual + 沿视线拉近，
            // 那套在本路径的取景下**下缘仍然一条线都没有**（实测：平台远侧/上缘墨线覆盖 97%，
            // 近侧/下缘 0%，一条竖切上是"亮面 → 立面 → 地面"、中间零墨线）。
            // 原因：深度测试的对手是**先画完的地面**（大平面在背景队列里先整片画完），
            // 而壳在下缘那圈投射出的深度是物体的**背面**——一个 18m 见方的台面，它的背面深达十几米，
            // "拉近 5 倍线宽"（0.65m）根本不够。
            // 而深度测试对这条路径本来就是多余的：墨线**画在本体之前**，本体随后会把它盖回重叠区，
            // 所以它只需要"别被先画的东西挡住"。改成 Always 之后环才闭合。
            // 代价（反向壳固有限制，旧链也记过同一条）：**相邻物体互相漏线**——比如角色站在台面上时，
            // 它底下那圈会被随后画的台面本体盖掉。要抑制得按层规划 Stencil，属后续项。
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   PixelartInkVertex
            #pragma fragment PixelartInkFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _MainLightLevel;
                float  _DitherMode;
                float  _DitherStrength;
                float  _DitherPatternSize;
                float  _NormalEdgeLevel;
                float4 _InkColor;
                float  _OutlinePixels;
            CBUFFER_END

            // 全局：1 低分辨率像素的世界尺寸（PixelartBeforeRenderFeature / rig 下发）。
            float _PixelartUnitSize = 0.1296;

            struct AttributesInk
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsInk
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct GBufferOutInk
            {
                half4 albedo : SV_Target0;
                half4 normal : SV_Target1;
                half4 prop   : SV_Target2;
            };

            VaryingsInk PixelartInkVertex(AttributesInk input)
            {
                VaryingsInk output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                float3 normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS), true);

                float pixelWorld = _PixelartUnitSize * _OutlinePixels;

                // 【外扩方向必须归一化——这一条与旧链的取舍相反，实测结论在下面】
                // 不归一化时，外扩量 = normalVS.xy 本身：法线朝屏的面（比如方盒的近侧立面）
                // 外扩不足一个像素，**整圈线会被光栅化吃掉**。实测（1920 宽屏、RT 384×216、线宽 1）：
                // 平台"远侧/上缘"墨线覆盖 97%，而**"近侧/下缘"是 0%**（一条竖切上是
                // 亮面 → 立面 → 地面，中间零墨线）——因为近侧下缘那一圈对应的是**朝前的面**，
                // 被 Cull Front 剔除，只剩底面在屏幕上下移 0.866 像素 ⇒ 不足一像素、没被光栅化。
                // 归一化之后每个方向都严格 _OutlinePixels 个低分辨率像素，环才会闭合。
                // 代价：斜面不再"自然变细"（线条等宽）——本路径要的正是等宽。
                float2 dir = normalVS.xy;
                float  len = max(length(dir), 1e-4);
                positionVS.xy += (dir / len) * pixelWorld;
                // 沿视线拉近 5× 线宽：覆盖正交俯视下"轮廓外侧那圈像素压在地面上"的深度差。
                positionVS.z -= pixelWorld * 5.0;

                output.positionCS = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
                return output;
            }

            GBufferOutInk PixelartInkFragment(VaryingsInk input)
            {
                if (_OutlinePixels < 0.01)
                    discard;

                GBufferOutInk output;
                output.albedo = half4(_InkColor.rgb, 1.0);
                output.normal = half4(0.0, 0.0, 1.0, 1.0);
                output.prop   = half4(1.0, 0.5, 0.0, 1.0);   // a=1 ⇒ 着色 pass 原样输出（不吃光照/色带）
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

