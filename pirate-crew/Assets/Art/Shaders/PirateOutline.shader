// ============================================================================
// PirateOutline.shader —— 海盗军团夺宝 3D / M2 单位选中·悬停单体描边
//
// 【设计参照】Godot 版真实接线的那套描边 shader（本项目是 Godot 版重制）：
//   - modules/pirate_crew/shaders/outline_hover.gdshader   （悬停，inverted hull）
//   - modules/pirate_crew/shaders/outline_selected.gdshader（选中，inverted hull + 屏幕空间虚线）
//   - assets/shaders/outline.gdshader                     （基础描边，近处增粗）
//   接线在 modules/pirate_crew/scripts/characters/pirate_base.gd：
//     悬停 -> 生成一圈复制网格、material_override = outline_hover，cull_front
//     选中 -> 不启用 inverted hull，改由全屏后处理 outline_post.gdshader 画（见 PirateOutlinePost.shader）
//   本 shader 把 hover/selected 两种状态合并进同一个材质，用 _OutlineState 切换，
//   免去 Godot 版"为每个骨骼复制一圈网格、再换 material_override"的做法。
//
// 【实现路径】inverted hull（法线外扩 + 只画背面）：
//   第 1 个 Pass（Base）  ：正常画本体（简单 Lambert + SH 环境光）。
//   第 2 个 Pass（Outline）：Cull Front 只画背面，顶点沿法线外扩，得到一圈描边。
//   hover 与 selected 的差异（翻译自 Godot，逐条对应）：
//     | 维度     | hover                              | selected                          |
//     | 颜色     | 淡白 a≈0.22                        | 青色 #49d9d6 a≈0.949              |
//     | 粗细     | 细（_OutlineWidthHover=0.0025）     | 粗（_OutlineWidthSelected=0.006） |
//     | 线型     | 实线                               | 屏幕空间流动虚线（sin 相位）        |
//     | 语义     | "可选中"提示                        | "已选中"                          |
//   Godot 的 hover/selected 都做了"距离衰减"（pow(clip.w, 1-attenuation)），
//   使远处线条变细、近处不变，避免远处单位糊成一片；本 shader 保留该参数。
//
// 【_DebugMode 分档】图形学调试截图规范要求 shader 内含 debug_mode 拆分管线步骤。
//   注意：本环境无可用渲染路径，实际逐层截图待有图形界面的编辑器会话补跑，
//         采集脚本见 Assets/Editor/OutlineDebugCapture.cs，说明见 docs/描边Shader调试.md。
//   // [DEBUG] 档位语义（每一档的预期画面写在各 Pass 对应分支处）：
//     0  关闭调试 = 正常合成（本体 + 描边）
//     1  只显示本体不描边
//     2  只显示法线外扩结果（品红实色壳）
//     3  只显示描边掩码（单色剪影，无虚线）
//     4  显示深度/法线原始数据（本体换成法线 RGB + 视空间深度）
//
// 【为什么用 uniform 分支而不是 multi_compile 关键字】
//   _DebugMode 只在人工调试时改，且 5 档分支都是极短片段着色器分支，现代 GPU 上
//   动态分支代价可忽略；用关键字会为每个 Pass 生成 5 个变体（加上 hover/selected
//   若也用关键字则变体再翻倍），并且采集脚本每次切档都要 SetKeyword 管理全局关键字
//   状态，容易残留。故本 shader 零关键字，调试档与状态全部走 uniform。
//   （唯一代价：无法在 build 里彻底剔除调试分支；调试分支只有几行，可接受。）
//
// 【[CHECK] 本地 URP 包核对结果】（完整清单见 docs/描边Shader调试.md）
//   以下每个 #include / 宏都已在
//   Library/PackageCache/com.unity.render-pipelines.universal@14.0.12 与
//   Library/PackageCache/com.unity.render-pipelines.core@14.0.12 中逐条核实存在：
//     Core.hlsl                                -> ShaderLibrary/Core.hlsl
//     Lighting.hlsl                            -> ShaderLibrary/Lighting.hlsl（:4-9 引入 BRDF/Debugging3D/GlobalIllumination/RealtimeLights/AmbientOcclusion/DBuffer）
//     TransformObjectToHClip                   -> core/SpaceTransforms.hlsl:108
//     TransformObjectToWorldNormal             -> core/SpaceTransforms.hlsl:199
//     TransformWorldToView                     -> core/SpaceTransforms.hlsl:97
//     TransformWorldToViewDir                  -> core/SpaceTransforms.hlsl:155
//     GetVertexPositionInputs / GetVertexNormalInputs -> URP/ShaderVariablesFunctions.hlsl:7/21
//     UNITY_MATRIX_P                           -> URP/ShaderLibrary/Input.hlsl:193（#define 到 OptimizeProjectionMatrix）
//     struct Light / GetMainLight              -> URP/RealtimeLights.hlsl:12/97（经 Lighting.hlsl 引入）
//     SampleSH                                 -> URP/GlobalIllumination.hlsl:21（经 Lighting.hlsl 引入）
//     _Time                                    -> URP/UnityInput.hlsl:40
//   注：core 与 URP 均未声明 ComputeScreenPos，故屏幕 UV 由 clip.xy/w 手算，未使用该函数。
//   注：本清单只能证明"符号存在"，**不能**证明 include 自洽——GlobalIllumination.hlsl 那次
//       静态核对全绿、真实编译照样失败（见下方 Base Pass 的踩坑记录）。
// ============================================================================

Shader "PirateCrew/PirateOutline"
{
    Properties
    {
        // ---- 本体 ----
        _BaseColor              ("本体基础色", Color) = (0.85, 0.85, 0.90, 1.0)

        // ---- 描边：三套色 + 三套宽（对应 Godot outline_hover / outline_selected）----
        // _OutlineColor 是"无状态"兜底色；实际运行时由 _OutlineState 选中 hover/selected 两套。
        _OutlineColor           ("描边兜底色（state=0）", Color) = (0.286, 0.851, 0.839, 0.949)
        _OutlineColorHover      ("悬停描边色", Color) = (1.0, 1.0, 1.0, 0.22)
        _OutlineColorSelected   ("选中描边色 #49d9d6", Color) = (0.286, 0.851, 0.839, 0.949)

        _OutlineWidth           ("描边宽度[状态兜底]", Range(0.0, 0.05)) = 0.006
        _OutlineWidthHover      ("悬停描边宽度", Range(0.0, 0.05)) = 0.0025
        _OutlineWidthSelected   ("选中描边宽度", Range(0.0, 0.05)) = 0.006

        // 0 = 无描边（透明），1 = 悬停，2 = 选中。由选中/悬停系统每帧写。
        _OutlineState           ("描边状态 0=无 1=悬停 2=选中", Range(0.0, 2.0)) = 0.0
        _OutlineAlpha           ("描边整体透明度（叠加乘算）", Range(0.0, 1.0)) = 1.0

        // ---- 外扩方式 ----
        // [0] 屏幕空间恒定粗细：距离无关，对应 Godot hover/selected 的做法（推荐）
        // [1] 世界/物体空间法线外扩：经典 inverted hull，粗细随距离变小；
        //     该模式下 _OutlineWidth* 单位变成"米"，需调到 0.01~0.05 量级（见调参文档）
        _OutlineExpandMode      ("外扩模式 0=屏幕空间恒定 1=世界空间法线", Range(0.0, 1.0)) = 0.0
        // 距离衰减：0=恒定粗细，1=远处显著变细（Godot 默认 0.4）
        _OutlineDistanceAttenuation ("距离衰减 0=恒定 1=远处显著变细", Range(0.0, 1.0)) = 0.4

        // ---- 选中虚线 ----
        _DashSpeed              ("虚线流动速度", Range(0.0, 20.0)) = 5.0
        _DashFrequency          ("虚线密度", Range(1.0, 200.0)) = 50.0

        // ---- 调试（AGENTS.md 图形学调试截图规范）----
        // 0 正常 / 1 只本体 / 2 只外扩壳 / 3 只描边掩码 / 4 深度法线原始数据
        _DebugMode              ("调试模式 0=正常 1=只本体 2=只外扩 3=只掩码 4=深度法线", Range(0.0, 4.0)) = 0.0
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

        // ====================================================================
        // Pass 1 / Base：正常渲染本体
        // ====================================================================
        Pass
        {
            Name "PirateOutlineBase"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   BaseVertex
            #pragma fragment BaseFragment
            // 主光阴影本 Pass 不采样（用 GetMainLight() 无阴影重载），故不声明 shadow 关键字。
            // 若后续要给本体接阴影，再加：
            //   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            // 说明：多编译关键字会成倍增加变体，本模块刻意保持零关键字（见文件头）。

            // Core.hlsl 已含 Common.hlsl / URP Input.hlsl / ShaderVariablesFunctions.hlsl，
            // 因此 TransformObjectToHClip、GetVertexPositionInputs 等无需再单独 include。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // struct Light / GetMainLight()（RealtimeLights.hlsl:12/97）与 SampleSH（GlobalIllumination.hlsl:21）
            // 由 Lighting.hlsl 的 include 链提供（Lighting.hlsl:4-9 = BRDF/Debugging3D/GlobalIllumination/
            // RealtimeLights/AmbientOcclusion/DBuffer）。
            //
            // 【踩坑记录 · 2026-09-13，务必不要再"优化"回去】
            //   这里原先只 include GlobalIllumination.hlsl（想缩小编译面），本环境无渲染路径时
            //   静态核对"用到的符号都在"看起来成立，但在真实 GPU 会话里 d3d11 直接编译失败：
            //     Shader error in 'PirateCrew/PirateOutline': unrecognized identifier 'BRDFData'
            //       at .../ShaderLibrary/GlobalIllumination.hlsl(353)
            //   原因：GlobalIllumination.hlsl 自己并不 include BRDF.hlsl，而它的
            //   GlobalIllumination(BRDFData, BRDFData, ...) 函数体用到了 BRDF.hlsl 里的 BRDFData；
            //   它只对"调用者已引入 BRDF"的场景自洽。Lighting.hlsl 正是把这一组一起引入的标准入口
            //   （URP 自带 Lit/SimpleLit 也走它）。
            //   教训：URP 的"轻量 include"不能靠文件名/符号检索推断自洽性，必须在有渲染路径的
            //         编辑器里真实编译过才算验证；此后本 shader 的任何 include 改动都要重跑一次
            //         play mode 并 read_console 确认 0 shader error。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // CBUFFER 字段顺序必须与 Properties 声明顺序一致，否则 SRP Batcher 会判定不兼容。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _OutlineColor;
                float4 _OutlineColorHover;
                float4 _OutlineColorSelected;
                float  _OutlineWidth;
                float  _OutlineWidthHover;
                float  _OutlineWidthSelected;
                float  _OutlineState;
                float  _OutlineAlpha;
                float  _OutlineExpandMode;
                float  _OutlineDistanceAttenuation;
                float  _DashSpeed;
                float  _DashFrequency;
                float  _DebugMode;
            CBUFFER_END

            struct AttributesBase
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsBase
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            VaryingsBase BaseVertex(AttributesBase IN)
            {
                VaryingsBase OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = p.positionCS;
                OUT.normalWS   = n.normalWS;
                OUT.positionWS = p.positionWS;
                return OUT;
            }

            half4 BaseFragment(VaryingsBase IN) : SV_Target
            {
                // [DEBUG] 档 2/3：本体不画，只留外扩壳 / 掩码。
                //   预期画面：物体本体消失，场景里只剩一圈（档2 品红 / 档3 单色）壳。
                //   若此时仍能看到本体 -> 说明本体 Pass 的 discard 未生效（档位值传错）。
                if (_DebugMode > 1.5 && _DebugMode < 3.5)
                    discard;

                // [DEBUG] 档 4：深度/法线原始数据。
                //   预期画面：物体表面按世界法线染色 —— R=法线.x 映射, G=法线.y 映射,
                //             B=视空间深度（越近越黑、越远越蓝，50m 封顶）。
                //   常见异常：整块死灰蓝 -> 法线没传进 Varyings（normalOS 丢失或模型无法线）；
                //             整块同色 -> 模型是硬边平面（属正常，换圆球/角色模型看渐变）。
                if (_DebugMode > 3.5)
                {
                    float3 n = normalize(IN.normalWS) * 0.5 + 0.5;
                    float  viewDepth = -TransformWorldToView(IN.positionWS).z;
                    float  d = saturate(viewDepth / 50.0);
                    return half4(n.x, n.y, d, 1.0);
                }

                // 正常档 0/1：简单 Lambert + SH 环境光（unshaded 的描边不受光照影响）。
                Light mainLight = GetMainLight();
                float3 nrm = normalize(IN.normalWS);
                float  ndotl = saturate(dot(nrm, mainLight.direction));
                half3  ambient = SampleSH(nrm) * 0.5;
                half3  lit = _BaseColor.rgb * (ambient + mainLight.color * ndotl);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 2 / Outline：inverted hull（法线外扩 + Cull Front 只画背面）
        // 对应 Godot outline_hover.gdshader / outline_selected.gdshader 的 vertex() 段
        //
        // 【LightMode 必须与 Base Pass 不同，这是本 shader 最隐蔽的一处坑】
        //   URP 的不透明前向 DrawObjectsPass 用固定的 ShaderTagId 列表取 pass
        //   （DrawObjectsPass.cs:133 = SRPDefaultUnlit / UniversalForward / UniversalForwardOnly），
        //   而 Unity 的 DrawRenderers **每个 tag 槽位只会取一个 pass**。所以若本 Pass 也标
        //   "UniversalForward"，它会和 Base Pass 撞同一个槽位、被静默丢弃——
        //   表现为"材质正常、本体正常、就是没有描边"，且 Console 无任何报错。
        //   实测（2026-09-13，play mode + _DebugMode=2）：本体 Pass 已 discard（单位消失），
        //   描边 Pass 的品红壳却一像素都没出现，据此定位。
        //   SRPDefaultUnlit 在 URP 列表里是独立槽位（也是 URP 官方"额外 unlit pass"的常规做法），
        //   故本 Pass 用它。
        // ====================================================================
        Pass
        {
            Name "PirateOutlineHull"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front          // 只画背面：外扩后背面绕到轮廓外侧，形成一圈描边
            ZWrite Off          // 描边是叠加层，不写深度（hover 半透明时必须）
            ZTest LEqual        // 被本体/其他物体挡住的部分自动剔除

            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   OutlineVertex
            #pragma fragment OutlineFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 与 Base Pass 完全相同的 CBUFFER（同名同序，SRP Batcher 才兼容）。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _OutlineColor;
                float4 _OutlineColorHover;
                float4 _OutlineColorSelected;
                float  _OutlineWidth;
                float  _OutlineWidthHover;
                float  _OutlineWidthSelected;
                float  _OutlineState;
                float  _OutlineAlpha;
                float  _OutlineExpandMode;
                float  _OutlineDistanceAttenuation;
                float  _DashSpeed;
                float  _DashFrequency;
                float  _DebugMode;
            CBUFFER_END

            struct AttributesOutline
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsOutline
            {
                float4 positionCS : SV_POSITION;
                float4 color      : TEXCOORD0; // rgb = 描边色, a = 该状态基础透明度
                float2 screenNDC  : TEXCOORD1; // 用于选中虚线的屏幕空间相位
                float  dashed     : TEXCOORD2; // 1 = 走虚线（选中），0 = 实线
            };

            // 按 _OutlineState 取色（Godot 用 material_override 切换，本 shader 用 uniform 选）。
            float4 OutlineColorForState()
            {
                if (_OutlineState > 1.5) return _OutlineColorSelected;
                if (_OutlineState > 0.5) return _OutlineColorHover;
                return _OutlineColor;
            }

            // 按 _OutlineState 取宽。
            float OutlineWidthForState()
            {
                if (_OutlineState > 1.5) return _OutlineWidthSelected;
                if (_OutlineState > 0.5) return _OutlineWidthHover;
                return _OutlineWidth;
            }

            VaryingsOutline OutlineVertex(AttributesOutline IN)
            {
                VaryingsOutline OUT;

                float4 positionOS = IN.positionOS;
                float  width = OutlineWidthForState();

                // ---- 外扩模式 1：世界/物体空间法线外扩（经典 inverted hull）----
                // 该模式下 width 的单位是"米"（因为直接加在 positionOS 上），
                // 所以 _OutlineWidth* 需要放到 0.01~0.05，否则 0.006 几乎看不见。
                if (_OutlineExpandMode > 0.5)
                {
                    positionOS.xyz += normalize(IN.normalOS) * width;
                }

                VertexPositionInputs p = GetVertexPositionInputs(positionOS.xyz);
                float4 positionCS = p.positionCS;

                // ---- 外扩模式 0：屏幕空间恒定粗细（Godot hover/selected 的原始做法）----
                // Godot: clip.xy += normalize(mat3(P)*view_normal).xy * width * pow(w, 1-att)
                // 逐项对应：normalize(mat3(P)*view_normal).xy 即 clipNormal.xy；
                //           pow(clip.w, 1.0-distance_attenuation) 即 distFactor。
                if (_OutlineExpandMode <= 0.5)
                {
                    float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                    float3 normalVS   = TransformWorldToViewDir(normalWS, true);
                    // UNITY_MATRIX_P 在 URP ShaderLibrary/Input.hlsl:193 定义，随 Core.hlsl 引入。
                    float3 clipNormal = mul((float3x3)UNITY_MATRIX_P, normalVS);
                    // +1e-5 防止法线正对镜头时 xy≈0 -> normalize 出 NaN（表现为闪点/破面）。
                    float2 dirCS = normalize(clipNormal.xy + float2(1e-5, 1e-5));
                    float  w = max(positionCS.w, 1e-3);
                    float  distFactor = pow(w, 1.0 - _OutlineDistanceAttenuation);
                    positionCS.xy += dirCS * width * distFactor;
                }

                OUT.positionCS = positionCS;

                // 屏幕空间虚线相位用 NDC（不随 3D 形体变形，对应 Godot 的 screen_pos = clip.xy / clip.w）。
                float w = max(positionCS.w, 1e-3);
                OUT.screenNDC = positionCS.xy / w;

                float4 color = OutlineColorForState();
                color.a *= _OutlineAlpha;
                OUT.color  = color;
                OUT.dashed = (_OutlineState > 1.5) ? 1.0 : 0.0;
                return OUT;
            }

            half4 OutlineFragment(VaryingsOutline IN) : SV_Target
            {
                // [DEBUG] 档 1：只显示本体。描边 Pass 整段丢弃。
                //   预期画面：干净的、无任何描边的单位。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    discard;

                // [DEBUG] 档 4：原始数据档，描边 Pass 整段丢弃（画面完全由 Base Pass 提供）。
                if (_DebugMode > 3.5)
                    discard;

                // [DEBUG] 档 2：只显示法线外扩结果 —— 品红实色壳。
                //   预期画面：本体消失，物体轮廓发胖一圈、纯品红、无描边颜色/无虚线。
                //   常见异常：发胖过量 -> _OutlineWidth 太大；
                //             壳上有破洞/裂缝 -> 模型法线被压平或顶点色硬边（见调参文档）。
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return half4(1.0, 0.0, 1.0, 1.0);

                // [DEBUG] 档 3：只显示描边掩码 —— 单色剪影，忽略虚线、忽略 hover 半透明。
                //   预期画面：一圈不透明、无流动的纯青色轮廓（像给物体描了实线）。
                //   常见异常：轮廓断断续续 -> 模型是硬边/法线不连续；
                //             完全没有轮廓 -> Cull Front 写反了（改成 Cull Back 试试）或
                //             材质渲染队列被物体自身深度覆盖。
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return half4(_OutlineColorSelected.rgb, 1.0);

                // 正常档 0：实线 / 虚线（选中）。
                float alpha = IN.color.a;
                if (IN.dashed > 0.5)
                {
                    // 屏幕空间流动虚线，对应 Godot outline_selected.gdshader 的
                    // phase = screen_pos.y * dash_frequency + TIME * dash_speed。
                    float phase = IN.screenNDC.y * _DashFrequency + _Time.y * _DashSpeed;
                    float dash  = smoothstep(-0.15, 0.15, sin(phase));
                    alpha *= dash;
                }

                // 虚线被 sin 压到 0 的片段直接丢弃，避免叠加层上的极淡残影。
                if (alpha < 0.002)
                    discard;

                return half4(IN.color.rgb, alpha);
            }
            ENDHLSL
        }

        // ====================================================================
        // Pass 3 / DepthOnly：让描边单位进入 URP 深度预通道
        // 手工极简实现（不引用 URP 自带 DepthOnlyPass.hlsl，避免引入 _BaseMap 等
        // 本 shader 不存在的 CBUFFER 字段导致编译失败）。
        // ====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVertex
            #pragma fragment DepthFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesDepth { float4 positionOS : POSITION; };
            struct VaryingsDepth   { float4 positionCS : SV_POSITION; };

            VaryingsDepth DepthVertex(AttributesDepth IN)
            {
                VaryingsDepth OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFragment(VaryingsDepth IN) : SV_Target
            {
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    // 本 shader 自带本体 + 描边 + 深度，不继承任何内置回退。
    Fallback Off
}
