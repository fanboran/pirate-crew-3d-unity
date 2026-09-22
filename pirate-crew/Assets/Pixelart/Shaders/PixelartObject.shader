// ============================================================================
// PixelartObject.shader —— 像素化着色路径的**物体 pass**（唯一的几何 pass）
//
// 【本 pass 只写数据，不着色】输出**屏幕档**的 7 张 MRT：
//   SV_Target0  albedo    rgb = 亮部色；a = 覆盖标记（1 = 这里有几何）
//   SV_Target1  normal0   **屏幕空间几何法线** normalize(cross(ddy(positionWS), ddx(positionWS)))
//                         —— 连通域判据的输入（它要的是"面片级别"的法线，不是插值平滑法线）
//   SV_Target2  normal1   切线空间法线贴图→世界法线（没有贴图/没有切线时 = 平滑法线）
//                         —— 着色用的法线
//   SV_Target3  physical  r = 光滑度  g = 金属度
//   SV_Target4  shape     r = 优先级（本仓未移植优先级仲裁，恒 0）  b = 法线边阈值  a = AA 缩放
//   SV_Target5  palette   r = 主光档数  g = 抖动标量（0..1，中心 0.5）  b = 边光档数
//                         a = applyOutline（逐物体描边开关；**屏幕空间描边那一趟读它**）
//   SV_Target6  rimlight  rgb = 逐物体边缘光色
//
// 【为什么在屏幕档而不是艺术画布】v3 的艺术画布 320×180、G-buffer 1600×900 = 画布 × 5 = 它的
//   **屏幕分辨率**——"几何/G-buffer 就是常规延迟渲染的 G-buffer，全分辨率"，省 fill rate 的是
//   **着色那几趟**（只有画布那么多像素）。本仓同构（创始人 2026-09-22 裁决；契约 §0）。
//   降采是隐式的：后续各趟在艺术画布上按 uv 点采样，取到的就是"块中心那个纹素"，
//   与"直接在艺术画布上光栅化"逐点等价（`id*k + k/2` 同时是画布像素中心的放大像）。
//
// 【墨线不在这里】描边已从"反向壳几何"改为**艺术画布上的屏幕空间 4 邻域膨胀**
//   （创始人 2026-09-22 裁决，`PixelartOutline.shader`）——几何壳沿面法线外扩时，
//   箱体左下/右下两条剪影边（正面墙底边）投影到 cos45°≈0.7 艺术像素 < 1 ⇒ 亚像素 ⇒ 量化后断续，
//   且角部外扩重叠出堆块。故本 shader **没有**墨线 pass、没有 `_InkColor`/`_PixelartInkDepthPull`。
//
// 【物体级像素吸附】`_SnapToPixelGrid`（默认开，创始人裁决）：把物体**原点**在**视图空间**吸附到
//   1 艺术像素的整数倍，位移对所有顶点相同 ⇒ 形状不变形、整块落在格子边界上。
//   这是 v3 `CommonPass.hlsl` 两层里的**第二层**（顶点着色器原点对齐），蓝图 §1.4 ⚠ 说两层作用重叠、
//   只需一层——本仓取这一层。与 CameraRig 的**视图矩阵 snap**（第一步）叠加时，两者的网格是同一个
//   （都以 `_PixelartUnitSize` 为单位），所以不会互相打架。
//
// 【抖动坐标】几何是渲进屏幕档的，所以 `positionCS.xy` 是**细像素**坐标；抖动图案要贴艺术像素网格，
//   故先 `floor(positionCS.xy / pixelScale)` 取艺术像素坐标（本仓口径：贴屏幕 + 块对齐，蓝图 §7.2 第 6 条）。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartObject"
{
    Properties
    {
        _BaseColor            ("亮部色（albedo）", Color) = (0.93, 0.85, 0.68, 1.0)
        _MainLightLevel       ("色带档数（1 = 不量化；逐物体可调）", Range(1.0, 8.0)) = 3.0
        _DitherMode           ("抖动图案 0=Bayer4 1=1-bit密度图案（v3）", Range(0.0, 1.0)) = 0.0
        _DitherStrength       ("抖动幅度（占一个色带步长的比例；0 = 观感上等于关）", Range(0.0, 1.0)) = 0.5
        _DitherPattern        ("1-bit 密度图案（v3 口径；需 Point/Repeat 导入）", 2D) = "gray" {}
        _DitherPatternSize    ("图案边长（纹素）：4/6/8/16，须与图案资产一致", Float) = 4.0

        _NormalEdgeLevel      ("法线边加成档（连通域降档的提亮项）", Range(0.0, 1.0)) = 0.0
        _NormalEdgeThreshold  ("法线边阈值", Range(0.0, 1.0)) = 0.5
        _AAScale              ("AA 缩放（连通域降档的门控乘数；1 = 不缩放）", Range(0.0, 1.0)) = 1.0

        _Smoothness           ("光滑度", Range(0.0, 1.0)) = 0.5
        _Metallic             ("金属度", Range(0.0, 1.0)) = 0.0
        _BumpMap              ("法线贴图（默认平坦 = 不干预）", 2D) = "gray" {}
        _BumpScale            ("法线贴图强度", Range(0.0, 4.0)) = 1.0

        _RimLightColor        ("逐物体边缘光色（黑 = 无边缘光）", Color) = (0, 0, 0, 1)
        _Priority             ("优先级（本仓未移植优先级仲裁，保留）", Float) = 0.0
        _OutlinePixels        ("描边开关（&gt;0 ⇒ 本物体的 applyOutline 置位）", Range(0.0, 1.0)) = 1.0

        _SnapToPixelGrid      ("物体级像素吸附（1 = 开）", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
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
            #pragma target 4.5
            #pragma vertex   PixelartObjectVertex
            #pragma fragment PixelartObjectFragment
            #pragma multi_compile_instancing
            // 法线贴图通道**默认不参与编译**：只有材质里真挂了法线贴图并翻开这个开关，
            // 那段切线空间解码才会进来（见 fragment 里那段实测事故的注释）。
            #pragma shader_feature_local _NORMALMAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _MainLightLevel;
                float  _DitherMode;
                float  _DitherStrength;
                float  _DitherPatternSize;
                float  _NormalEdgeLevel;
                float  _NormalEdgeThreshold;
                float  _AAScale;
                float  _Smoothness;
                float  _Metallic;
                float  _BumpScale;
                float4 _RimLightColor;
                float  _Priority;
                float  _OutlinePixels;
                float  _SnapToPixelGrid;
            CBUFFER_END

            TEXTURE2D(_DitherPattern);  SAMPLER(sampler_point_repeat);   // UV 会越界，必须 Repeat
            TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);

            // 全局：1 艺术像素的世界尺寸（BeforeRender / rig 下发）。
            float _PixelartUnitSize = 0.0778;
            // 全局：一个艺术像素占几个细像素（= pixelScale）。抖动取块坐标要用。
            float _PixelartSamplingScale = 3.0;

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
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct GBufferOut
            {
                half4 albedo   : SV_Target0;
                half4 normal0  : SV_Target1;
                half4 normal1  : SV_Target2;
                half4 physical : SV_Target3;
                half4 shape    : SV_Target4;
                half4 palette  : SV_Target5;
                half4 rimlight : SV_Target6;
            };

            /// 物体级像素吸附：把物体原点在**视图空间**吸附到 1 艺术像素的整数倍。
            /// 位移对所有顶点相同（不 snap 逐顶点）⇒ 形状不变形，只是整块落到格子边界上。
            float2 PixelartOriginSnap(float3 originWS, float3 positionVS)
            {
                if (_SnapToPixelGrid < 0.5)
                    return 0;

                float unit = max(_PixelartUnitSize, 1e-6);
                float3 originVS = mul(UNITY_MATRIX_V, float4(originWS, 1.0)).xyz;
                float2 snapped = float2(round(originVS.x / unit), round(originVS.y / unit)) * unit;
                return snapped - originVS.xy;
            }

            Varyings PixelartObjectVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 positionVS = mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).xyz;

                // 物体原点 = 模型矩阵的平移分量（v3 `CommonPass.hlsl` 同法取 originWS）。
                float3 originWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                positionVS.xy += PixelartOriginSnap(originWS, positionVS);

                output.positionCS = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz),
                                          input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                return output;
            }

            // 抖动取值 [0,1]（0.5 = 无偏移），两种图案二选一。
            // 坐标用**艺术像素**（细像素坐标 ÷ pixelScale 取整），保证图案贴在像素网格上、不随相机游动。
            half DitherValue(Varyings input)
            {
                float scale = max(_PixelartSamplingScale, 1.0);
                uint2 pixelCoord = (uint2)floor(input.positionCS.xy / scale);

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
                // 逐物体的幅度是材质的旋钮。曾经漏了这一步（_DitherStrength 只声明没用），
                // 于是抖动恒等于满幅度——症状是"关着也有规律点阵"。
                return (value - 0.5) * _DitherStrength + 0.5;
            }

            GBufferOut PixelartObjectFragment(Varyings input)
            {
                GBufferOut output;

                output.albedo = half4(_BaseColor.rgb, 1.0);

                // normal0：屏幕空间几何法线（面片级别）。连通域判据靠它预测深度。
                float3 g = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                output.normal0 = half4(g, 1.0);

                // normal1：平滑法线。**只有材质真的挂了法线贴图时才叠 bump**。
                //
                // 【为什么必须用 _NORMALMAP 关键词把这段隔开，不能用"默认灰图 = 恒等"】实测事故：
                // 不隔开时，没指定法线贴图的材质会去采样 Properties 里声明的 `"gray"` 内建贴图，
                // 而 `UnpackNormalScale` 在非 DXT5nm 分支走的是 `UnpackNormalmapRGorAG`：
                // 灰 (0.5,0.5,0.5,0.5) 经它解码 = xy(0.25*2-1, 0.5*2-1) = (-0.5, 0) ⇒ **一个倾斜约 30°
                // 的假法线**（本该是 (0,0,1) 的恒等）。
                // 后果是**逐物体不同**的：法线贴图活在**切线空间**，同一张灰图在每个物体各自的切线基下
                // 解成不同朝向的世界法线 ⇒ 同一世界朝向的两个面（两个角色、台面与地面）打光方向不一致、
                // 各自的色带档位也整体偏一档。创始人报的"两个角色的打光方向不一致""地面没有全局光照"
                // 正是这一个根因（我已用解析式核对过：地面实测 = 纯环境项、台面 = 掉一档）。
                float3 normalWS = normalize(input.normalWS);
                float3 normalShaded = normalWS;
                #if defined(_NORMALMAP)
                    float3 tangentWS = input.tangentWS.xyz;
                    if (dot(tangentWS, tangentWS) > 1e-6)
                    {
                        float3 nTS = UnpackNormalScale(
                            SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                        float3 bitangent = input.tangentWS.w * cross(normalWS, normalize(tangentWS));
                        normalShaded = normalize(TransformTangentToWorld(
                            nTS, float3x3(normalize(tangentWS), bitangent, normalWS)));
                    }
                #endif
                output.normal1 = half4(normalShaded, 1.0);

                output.physical = half4(_Smoothness, _Metallic, 0.0, 1.0);
                output.shape    = half4(_Priority, 0.0, _NormalEdgeThreshold, _AAScale);
                output.palette  = half4(_MainLightLevel, DitherValue(input), _NormalEdgeLevel,
                                        _OutlinePixels > 0.5 ? 1.0 : 0.0);
                output.rimlight = half4(_RimLightColor.rgb, 1.0);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
