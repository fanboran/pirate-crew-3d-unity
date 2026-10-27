// ============================================================================
// ReplicaFrame.shader —— Sandcastle Lit/Base（sa0 pid600，Frame 白模材质）的移植
// （I5a 可选件）。Frame 不属于 Borders 三件套（H 报告 §1.1）：1.23×1.23 平面网格
// （sa0 pid398）+ Lit/Base 受光白模。
//
// 【反汇编依据】external/sandcastle-ref/rt/out/Borders/Lit_Base@654.vs_5_0.hlslasm
//   （VS：obj→world→clip + 世界法线 + 顶点色/UV 透传）与 Lit_Base@27222.ps_5_0.hlslasm
//   （PS，阴影变体）。pass 状态 Cull Back(2)/ZWrite On（H 报告 §1.1 表）。
//
// 【PS 结构（@27222 逐段）】
//   shadow = 比较采样：worldPos 经 cb0[134..137] 矩阵投影 → sample_c_lz(_ShadowMap,
//            uv.xy, ref=uv.z)，ref∉[0,1] 恒 1 —— 与 ReplicaSkybox 同款矩阵，
//            即管线全局 _MainLightWorldToShadow（见 ReplicaSkybox 文件头的语义
//            修正说明）；URP 等价 = MainLightRealtimeShadow（TODO 同款：级联与
//            单矩阵有差，L2 对账前记此差）。
//   lit    = sat(N·−sunDirection)·sunColor·shadow + sat(N·−moonDirection)·moonColor
//            （@27222 #27-38；阴影只乘太阳项）
//   o.rgb  = sat(顶点色)·lit + sat(顶点色)·ambient(cb0[40])   （#39-44）
//   o.a    = worldPos.y（原作管线大气混合用 alpha；本仓无 Blend，惰性保留）
//
// 【未移植变体（TODO）】Lit/Base 共 4 pass，另有 GI 体素变体（@28354：
//   worldXZ/cb0[116].xz 采 giVoxels、按沙盘 bounds 掩码混合）与波影×阴影全变体
//   （@29922：exp2(−0.721348·水量)×阴影 只乘太阳）——体素 GI 归 I3（E 流
//   E-gi-pipeline.md 出报告后统一接），本 shader 先落 @27222 结构。
//
// 【符号约定】_WaterLabSunDir 指向太阳（= −原作 sunDirection），N·L 直接用它；
//   月亮方向走 property（光传播方向语义，与原作槽一致），菜单月黑光禁用。
// ============================================================================

Shader "PirateCrew/WaterLab/ReplicaFrame"
{
    Properties
    {
        // 月亮：菜单时刻 moonColor=黑、enabled=false（menu-state.json）
        _MoonDirection ("月光方向（光传播方向）", Vector) = (0, -1, 0, 0)
        _MoonColor   ("月光色", Vector) = (0, 0, 0, 1)
        // cb0[40]：t=1/3 运行时真值（= environmentGradient 经 HSV 偏移后写入）
        _Ambient     ("环境光（t=1/3 运行时真值）", Vector) = (0.698039, 0.564706, 0.569923, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // ================================================================
        // 主着色 pass（@27222 阴影变体直译）
        // ================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ReplicaFrameVertex
            #pragma fragment ReplicaFrameFragment
            // 原作 = _MainLightWorldToShadow 单矩阵查 _ShadowMap；URP 等价走级联阴影
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MoonDirection;
                float4 _MoonColor;
                float4 _Ambient;
            CBUFFER_END

            // 实验台全局（WaterLabSimulation.PublishSunDir 发布，指向太阳）
            float4 _WaterLabSunDir;
            // WaterLab 复刻全局族（I1/I5 统一下发；t=1/3 真值 (1,0.940429,0.872353)）
            float4 _WaterLabSunColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // @654 v2 → o3（基色，白模为白）
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;  // @654 o1
                float3 normalWS   : TEXCOORD1;  // @654 o2
                float4 color      : TEXCOORD2;  // @654 o3
            };

            // @654 逐指令：obj→world（cb1）→clip（cb0[62..65]）；法线 rsq 归一
            Varyings ReplicaFrameVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.color = IN.color;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 ReplicaFrameFragment(Varyings IN) : SV_Target
            {
                // @27222 #1-12：M·worldPos → 比较采样（URP 等价，见文件头 TODO）
                float shadow = MainLightRealtimeShadow(
                    TransformWorldToShadowCoord(IN.positionWS));

                // #27-38：双光 Lambert，阴影只乘太阳
                float ndlSun  = saturate(dot(IN.normalWS, _WaterLabSunDir.xyz));
                float ndlMoon = saturate(dot(IN.normalWS, _MoonDirection.xyz));
                float3 lit = ndlSun * _WaterLabSunColor.rgb * shadow
                           + ndlMoon * _MoonColor.rgb;

                // #39-44：o.rgb = sat(baseColor)·lit + sat(baseColor)·ambient。
                // @27222 的 baseColor = sat(顶点色)（mov_sat v3——VS 把 COLOR 属性
                // 原样透传，白模网格顶点色即白）。无 COLOR 属性的网格 Unity 供
                // (1,1,1,1) 白，装配时无需额外处理。
                float3 base = saturate(IN.color.rgb);
                float3 col = base * lit + base * _Ambient.rgb;

                // #45：o.a = worldPos.y（原作大气混合；本仓无 Blend，惰性）
                return half4(col, IN.positionWS.y);
            }
            ENDHLSL
        }

        // ================================================================
        // ShadowCaster。Lit/Base 无 structure.json 记录，原作 4 pass 逐 pass 角色
        // 未核对（H 报告只给 pass0 颜色语义）——本 pass 是 URP 常规阴影件；若 I5
        // 对账发现 Frame 原作不投影，删本 pass 即可。
        // ================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ReplicaFrameShadowVertex
            #pragma fragment ReplicaFrameShadowFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // URP 惯例：由使用方声明
            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ReplicaFrameShadowVertex(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 ReplicaFrameShadowFragment(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
