// ============================================================================
// WaterLabWhitewater.shader —— 白水粒子（I9：Lit_Whitewater 逐指令移植）。
// 仿真侧 spawn/更新/计数（WaterLabSim.compute 的 Whitewater* kernel，泡沫场驱动）
// → drawArgs → WaterLabRefractionFeature 的 pass DrawProceduralIndirect 出画。
// 透明队列画在水面之上（水体 Transparent+10 已写深度，浪背面的白水被正确遮挡）。
// StructuredBuffer 需要 target 4.5。
//
// 真值源（D 流逐指令表，行号 = rt/reports/D-sand.md §3 的 hlasm 行）：
//   PS = Lit_Whitewater@12070.ps_5_0（83 实指令）——D-whitewater.hlsl 为集成底稿
//   VS = Lit_Whitewater@1702.vs_5_0（24 实指令；@2954/@4230/@450 同族）
// 与 Sand 共用机制（同槽位同解包）：22bit 场双线性、阴影点比较、cb0[40] 回退
// tint、域尺寸 cb0[116].xz、灯 cb0[123..126]、阴影矩阵 cb0[134..137]；差异：
// 网格 dims 槽位在 cb0[139]（Sand PS 在 cb0[143]）、灯只取方向 .y（粒子无法线）。
//
// 本仓代位/登记（不阻塞直译的架构差）：
//   ① 原作场=渲染缓冲 22bit 逐像素双线性；本仓 η−ground 解析场代位（Sand 偏差
//     表 §5-7 同口径，等渲染缓冲层）。② 域 tint=lerp(cb0[40], t2.rgb, inDomain)：
//     t2 身份疑 Gi bake、cb0[40] 上传路径待 A/E 对账（Sand TODO 同款），当前以
//     _AmbientColor 常数代位。③ y<0 裁剪改 y<ground 等价式（本仓地形低至 −1.3 m）。
//   ④ 第二灯按式实现但菜单固定时刻 MoonLight enabled=false（menu-state/H 定案）
//     → 恒 0，昼夜循环移植后恢复。⑤ 深度-only VS @15410（预深度/投影）与阴影视
//     锥 drawList 我方无对应物（白水不投影，见 compute Compact 注释）。
// ============================================================================

Shader "PirateCrew/WaterLab/Whitewater"
{
    Properties
    {
        _Alpha        ("不透明度上限（D 表 PS 常量 0.4）", Range(0.0, 1.0)) = 0.4
        _SizeScale    ("尺寸缩放（D 无此项，默认 1=恒等）", Range(0.1, 2.0)) = 1.0
        _AmbientColor ("域 tint 代位（cb0[40]/t2 待对账，C 级登记）", Color) = (0.0627, 0.1333, 0.2196, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WhitewaterUnlit"
            Tags { "LightMode" = "SRPDefault" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex   WhitewaterVertex
            #pragma fragment WhitewaterFragment

            // 主光阴影（原作白水 PS 即收阴影：sample_c_lz 比较采样，PS:68-77）。
            // 不含 _MAIN_LIGHT_SHADOWS_SOFT——原作点比较无 PCF 无软化（Sand 同口径）。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里（单独 include Shadows 不够）
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Alpha;
                float  _SizeScale;
                float3 _AmbientColor;
            CBUFFER_END

            StructuredBuffer<float4> _WaterLabWWPos; // xyz=世界坐标 w=age 秒（原作池语义）
            StructuredBuffer<float4> _WaterLabWWVel; // xyz=速度 w=基准尺寸 (m)
            StructuredBuffer<uint> _WaterLabWWDraw;  // 活粒子槽位（实例号→池槽，Compact 产出）

            // 模拟域（WorldToDomainUv：.xz=域中心、.y=域边长——与水体/沙 shader 同一约定）
            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;      // 世界空间指向太阳（PublishSunDir 下发）
            float4 _WaterLabSunColor;    // cb0[125] 太阳色单源（WaterLabReplicaGlobals 下发）
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);

            float DecodeEta(float e) { return e * 5.0 - 1.0; }

            // 6 顶点两三角（DrawProcedural 顶点序，vertexCountPerInstance=6）
            static const float2 QuadCorners[6] =
            {
                float2(-0.5, -0.5), float2(0.5, -0.5), float2(-0.5, 0.5),
                float2(-0.5, 0.5),  float2(0.5, -0.5), float2(0.5, 0.5),
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;   // o2.xy = quadUV（0..1）
                float  alphaK     : TEXCOORD1;   // o2.z = sat(age×10)×0.4（VS 端算好）
                float3 positionWS : TEXCOORD2;   // o1.xyz = worldPos
            };

            // ---- VS @1702（D-whitewater.hlsl L25-32）----
            // particle = pool[index[instanceID]]（float4，w=age 秒）
            // scale    = 0.1 × (1 − exp2(−5.77078·age))          【X 流：=0.1×(1−e^(−4·age))，
            //                                                       0→10 cm，τ=0.25 s】
            // alphaK   = saturate(age×10) × 0.4                  0.1 s 内升满，上限 0.4
            // world    = particle.xyz + float3(quad.x, 0, quad.y)×scale + float3(0,0.02,0)
            //            ——水平扁四边形（世界 XZ 平面基，非相机 billboard；D 表§3.1 修正④：
            //            45° 相机下读作贴水皮浮沫），抬 2 cm。
            // 本仓两处代位：① +0.02 已在池 y 里（compute Spawn/Update 写
            // ground+max(h−0.05,0)+_WwSurfaceOffset，_WwSurfaceOffset 即该字面量），VS 不再
            // 另抬——最终世界位与原作逐项恒等。② 基准尺寸保留 ±40% 个体随机（W4 登记，
            // vel.w，均值 0.1=D 字面量 0.1）；生长曲线照原作。
            Varyings WhitewaterVertex(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                Varyings OUT;
                uint slot = _WaterLabWWDraw[instanceId]; // 实例号→池槽（drawList 已只含活粒子）
                float4 p = _WaterLabWWPos[slot];
                float age = p.w;
                float size = _WaterLabWWVel[slot].w
                           * (1.0 - exp2(-5.77078 * age)) * _SizeScale;  // exp2 语义（X 流）
                float2 corner = QuadCorners[vertexId];
                float3 worldPos = p.xyz + float3(corner.x, 0.0, corner.y) * size;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.positionWS = worldPos;
                OUT.uv = corner + 0.5;
                OUT.alphaK = saturate(age * 10.0) * _Alpha;
                return OUT;
            }

            // ---- PS @12070（D-whitewater.hlsl L35-88；段行号=D 表§3.2）----
            half4 WhitewaterFragment(Varyings IN) : SV_Target
            {
                float3 wpos = IN.positionWS;

                // ---- 域裁剪（PS:1-6）：|worldXZ|>域半宽逐分量 discard；worldY<0 discard。
                // 本仓地形低至 −1.3 m（静水面恰在 y≈0），照抄 y<0 会把离岸粒子整片丢掉
                // ——等价语义「沉到沙面之下」= y<ground(x,z)。
                float2 domUv = (wpos.xz - _WaterLabOrigin.xz)
                             / max(_WaterLabOrigin.y, 1e-4) + 0.5;
                if (_WaterLabSimEnabled > 0.5)
                {
                    if (any(domUv < 0.0) || any(domUv > 1.0)) discard;   // PS:1-4
                }
                float2 sUv = saturate(domUv);                            // PS:18（sat 寻址）
                float ground = DecodeEta(SAMPLE_TEXTURE2D(
                    _WaterLabGroundTex, sampler_WaterLabGroundTex, sUv).r);
                if (_WaterLabSimEnabled > 0.5 && wpos.y < ground) discard; // PS:5-6 等价式

                // ---- 径向 alpha（PS:7-14）：程序化 falloff，无形状贴图。
                // d=|uv−0.5|；alpha=max(0.5−d,0)×alphaK；负 falloff discard（PS:11,14）。
                float d = length(IN.uv - 0.5);
                float radial = 0.5 - d;
                if (radial < 0.0) discard;
                float alpha = radial * IN.alphaK;

                // ---- 域 tint（PS:15-26）：tint = lerp(cb0[40], t2.SampleLevel(域UV,0),
                // inDomain) × 0.95——无光照加性域色，域外回退 cb0[40]。t2 身份疑 Gi bake、
                // cb0[40] 上传路径待 A/E 流对账（WaterLabSand TODO 同款）：当前以
                // _AmbientColor 常数代位（C 级登记），域贴图接入时按左式补全。
                float3 tint = _AmbientColor.rgb * 0.95;                  // PS:26 ×0.95

                // ---- 水柱场衰减（PS:27-67）：column = exp2(−0.721348×场) = e^(−0.5×场)
                //（X 流 exp2 语义）。原作场=渲染缓冲 22bit 逐像素双线性；本仓以粒子处
                // 可见水深 η−ground 解析代位（Sand 偏差表 §5-7 同口径）。
                float eta = DecodeEta(SAMPLE_TEXTURE2D(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, sUv).r);
                float column = exp2(-0.721348 * max(eta - ground, 0.0));

                // ---- 阴影（PS:68-77）：cb0[134..137] 矩阵 + sample_c_lz 点比较，越界恒 1
                //（无 PCF 无软化）；column×sh。URP MainLightRealtimeShadow 同口径（距离外
                // 返回 1；multi_compile 不含 _SOFT）。
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(wpos));
                column *= shadow;

                // ---- 光照（PS:78-82）：粒子无法线，两盏灯只取方向 .y 分量。
                // direct = max(−cb0[123].y,0)×cb0[125]：_WaterLabSunDir 指向太阳，取 +y；
                // 太阳色 cb0[125] 单源 = _WaterLabSunColor（ReplicaGlobals，A/H 时刻对账闭）。
                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25)
                    ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);
                float3 direct = _WaterLabSunColor.rgb * max(sunDir.y, 0.0);
                // second = max(−cb0[124].y,0)×cb0[126]（天光第二灯，同式同待遇）——菜单
                // 固定时刻 MoonLight enabled=false（menu-state/H 定案），该项恒 0；
                // 昼夜循环移植后按 live 灯方向/颜色恢复本式。
                float3 second = float3(0.0, 0.0, 0.0);

                // ---- 合成（PS:83）：o0.rgb = 0.95×[(direct+second)×column + tint]
                //（= 光照项 ×0.95 + tint，tint 已含 ×0.95——D 表§3.2 修正⑤：exp 只乘
                // 光照项，域 tint 不衰减）。
                // 【删满照归一】旧版 _BaseColor×(lightNow/lightFull) 是按「原作 HDR+tonemap」
                // 旧判断做的显示链补偿——F 流定案原作无 tonemap（HDR RT 直出），按 D 直译。
                float3 col = (direct + second) * column * 0.95 + tint;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
