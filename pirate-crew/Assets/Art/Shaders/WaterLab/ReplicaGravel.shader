// ============================================================================
// ReplicaGravel.shader —— Sandcastle Lit/Gravel 直译（C 流）。
//
// 翻译源：external/sandcastle-ref/extracted/shader-621/prog00-03（D 流切出的
// Lit/Gravel DXBC，D 报告 §4.3 的乱码件由 C 流重提 LZ4 blob 后反汇编成功）。
//
// 结构（逐段对 prog 编号）：
//   prog00 = Forward VS：aliveList[instanceID]→粒子槽；pos = local×w + 粒子位
//            （w=粒子第二字段，原作名 age、实为尺寸）；每实例 rand=√(PCG²(slot))
//            经 TEXCOORD2.z 下传（PS 同链重算出同一值做双色 lerp）。
//   prog01 = Forward PS：域界 discard（|xz| > 尺寸/2、y<0）；主光 NdotL×阴影
//            （比较采样）＋第二光 NdotL×色（无阴影）；albedo = lerp(c1,c2,rand)；
//            环境项 = albedo×cb0[40]；**无水柱吸收**（砾石变色由水面 pass 承担）。
//   prog02 = ShadowCaster VS（NdotL 偏置）；prog03 = ShadowCaster 占位 PS（恒 0）。
//
// 我方件（非原作，登记 C-scatter.md §4）：_LightScale 显示链曝光对齐（本仓无
// tonemap 直出，与 WaterLabProp/沙面同口径）；灯色走 URP 主光 + 菜单 MoonLight
// 真值常量。颜色一律 float4/Vector 下发（Linear 工程材质色 gamma 陷阱）。
// ============================================================================
Shader "PirateCrew/WaterLab/ReplicaGravel"
{
    Properties
    {
        _BaseColor1  ("砾石暗色（Carbonate gravelColor，linear 直填）", Vector) = (0.2392, 0.2118, 0.1804, 0)
        _BaseColor2  ("砾石亮色（Carbonate gravelColor2，linear 直填）", Vector) = (0.4902, 0.4431, 0.3656, 0)
        _SunColor    ("主光色（cb0[125] 语义；t=1/3 Morning 真值）", Vector) = (1.0, 0.9404, 0.8724, 1)
        _AmbientColor("环境色（cb0[40] 同槽语义；t=1/3 ambientSkyColor）", Vector) = (0.698039, 0.564706, 0.569923, 1)
        _MoonColor   ("第二光色（cb0[126] 语义；菜单 MoonLight 实测）", Vector) = (0.0627, 0.1333, 0.2196, 1)
        _LightScale  ("显示链曝光对齐（我方件，非原作）", Range(0.2, 3.0)) = 1.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex   GravelVertex
            #pragma fragment GravelFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor1;
                float4 _BaseColor2;
                float4 _SunColor;
                float4 _AmbientColor;
                float4 _MoonColor;
                float  _LightScale;
            CBUFFER_END

            // 粒子缓冲（Gravel.Init：material.SetBuffer("gravel", buffer)）
            StructuredBuffer<float4> _Gravel;        // xyz=position, w=size（原作 age）
            StructuredBuffer<uint>   _GravelAliveList;

            float4 _WaterLabSunDir;                  // 沙面/prop 同一全局（模拟层下发）

            float _WaterLabDomainSize;               // 域界 discard（cb0[116].xz 语义）

            // ---- PCG hash（VS prog00 双哈希段逐指令；= 逆向台 Lit/Plant 链）----
            uint GravelPcgHash(uint v)
            {
                uint t = (v ^ 61u) ^ (v >> 16);
                t = t * 9u;
                t = t ^ (t >> 4);
                t = t * 0x27d4eb2du;
                t = t ^ (t >> 15);
                return t;
            }

            // bfi(23,0) 进 0x3f800000 尾数 −1 ∈ [0,1)；再 rsq→div 得 √rand（VS 原样）
            float GravelRand01(uint h)
            {
                return asfloat(0x3f800000u | (h & 0x7fffffu)) - 1.0f;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
                uint  instanceID  : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;       // o1：域界 discard 与阴影坐标用
                float3 normalWS   : TEXCOORD1;       // o2
                float3 misc       : TEXCOORD2;       // o3：xy=uv 直通，z=√rand（尺度/选色）
            };

            Varyings GravelVertex(Attributes IN)
            {
                Varyings OUT;
                uint slot = _GravelAliveList[IN.instanceID];   // ld t1[instanceID]
                float4 particle = _Gravel[slot];               // ld t0[slot]

                // mad r0.yzw, v0.xxyz, r1.wwww, r1.xxyz —— local×w + position
                //（_Gravel 是 float4：xyz=position/w=size，无 .position 成员——首版误写）
                float3 local = IN.positionOS.xyz * particle.w + particle.xyz;
                float3 positionWS = local;                     // 场景根无变换（原作 cb1=identity）

                // 域界 discard 的判定留在 PS（prog01 原样）；VS 只出世界位
                OUT.positionWS = positionWS;
                OUT.normalWS = normalize(IN.normalOS);         // 原作 cb1 法线矩阵=identity 后 rsq
                OUT.positionCS = TransformWorldToHClip(positionWS);

                // o3.z：rand=√(PCG²(slot))——PS 端同链重算（prog01 无此 varying 消费，
                // 色选直接用同链值；此处保留与 VS 一致的传值路径）
                float r = GravelRand01(GravelPcgHash(slot));
                OUT.misc = float3(IN.uv0, sqrt(r));
                return OUT;
            }

            half4 GravelFragment(Varyings IN) : SV_Target
            {
                // ---- 域界 discard（prog01：尺寸/2 越界、y<0）----
                float halfSize = _WaterLabDomainSize * 0.5;
                if (abs(IN.positionWS.x) > halfSize || abs(IN.positionWS.z) > halfSize)
                    discard;
                if (IN.positionWS.y < 0.0)
                    discard;

                // ---- 每实例选色（albedo = lerp(c1,c2,rand)，prog01 mad_sat 段）----
                // rand 从 VS 的 TEXCOORD2.z 插值而来（实例内恒定）
                float3 albedo = lerp(_BaseColor1.rgb, _BaseColor2.rgb, saturate(IN.misc.z));

                // ---- 主光：NdotL×色×阴影（prog01 dp3/max/mul + 比较采样；灯色=uniform 真值）----
                float3 n = normalize(IN.normalWS);
                float3 sunDir = normalize(_WaterLabSunDir.xyz);
                if (dot(sunDir, sunDir) < 0.25)
                    sunDir = normalize(float3(-0.525, 0.669, -0.525));   // 菜单时刻回退（prop 同式）
                float ndl = max(dot(n, sunDir), 0.0);
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));

                // ---- 第二光：NdotL×色，无阴影（prog01 cb0[124]/cb0[126] 段）----
                // 菜单 MoonLight：色 (0.0627,0.1333,0.2196)、方向取场景灯仰角近似顶光
                // （H 流：MoonLight 无影、强度 1；静态菜单下第二光贡献极小）
                float3 moonDir = normalize(float3(0.2, 0.9, -0.2));
                float ndl2 = max(dot(n, moonDir), 0.0);

                // ---- 合成（prog01 mad/add：albedo×(主光×影 + 第二光) + albedo×环境）----
                float3 col = albedo * (ndl * shadow * _SunColor.rgb + ndl2 * _MoonColor.rgb);
                col += albedo * _AmbientColor.rgb;

                return half4(col * _LightScale, 1.0);   // _LightScale=我方显示链对齐件
            }
            ENDHLSL
        }

        // 投影 Pass（原作 prog02/03 同名职责；复用 URP 官方 ShadowCasterPass——
        // include 次序坑见 WaterLabSand/WaterLabProp 同款注释）
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
