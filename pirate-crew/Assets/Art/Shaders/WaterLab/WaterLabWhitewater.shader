// ============================================================================
// WaterLabWhitewater.shader —— 白水粒子（W4，外包债③清偿）：GPU indirect 点精灵。
// 仿真侧 spawn/更新/计数（WaterLabSim.compute 的 Whitewater* kernel，泡沫场驱动）
// → drawArgs → WaterLabRefractionFeature 的 pass DrawProceduralIndirect 出画。
// 对标原作 Whitewater（泡沫场实例化粒子，Culling kernel 写 drawList——相机视锥那一路
// 由 WhitewaterCompact 承担（frustumOverscan 0.1）；原作另有阴影视锥 drawShadowList/
// argsShadow，我方白水不投影，无对应物）。
// 透明队列画在水面之上（水体 Transparent+10 已写深度，浪背面的白水被正确遮挡）。
// StructuredBuffer 需要 target 4.5。
//
// 片元对齐原作 Lit_Whitewater PS（86 条实指令，2026-10-18 反汇编）：
//   域裁剪（worldXZ 出模拟域 / y<0 → discard）→ billboard 径向 alpha=
//   max(0.5−‖uv−0.5‖,0) × sat(life×10) × 0.4（原作 PS 末条 o0.w）
//   → 受光： (直射×阴影×水柱透射 + 天光) —— 原作天光为第二盏平行光，本仓为平坦环境色；
//      直射 = 太阳色 × max(-光向.y, 0)（原作 cb0[123] 是光向；本仓 _WaterLabSunDir 指向
//      太阳，故取 +y）；水柱透射 = exp(−0.721348×水深)，与 Lit_Sand 直射项同一常数。
//   【显示链适配】原作直射+底色两项叠加后走 HDR+tonemap；本仓 LDR 直出，故按「满照归一」
//   （满照=无影且水柱透明）——满照水沫与旧版平色等亮，淹没/背光才压暗。
// ============================================================================

Shader "PirateCrew/WaterLab/Whitewater"
{
    Properties
    {
        _BaseColor    ("水沫色", Color) = (0.95, 0.97, 0.96, 1.0)
        _Alpha        ("不透明度（原作 0.4）", Range(0.0, 1.0)) = 0.4
        _SizeScale    ("尺寸缩放", Range(0.1, 2.0)) = 1.0
        _AmbientColor ("环境色", Color) = (0.0627, 0.1333, 0.2196, 1.0)
        _SunTint      ("太阳色", Color) = (1.0, 0.9709, 0.9403, 1.0)
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

            // 主光阴影（原作白水 PS 即收阴影：sample_c_lz 比较采样 + 视锥外恒 1）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Shadows.hlsl 的 LerpWhiteTo 在 core CommonMaterial 里（单独 include Shadows 不够）
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _BaseColor;
                float  _Alpha;
                float  _SizeScale;
                float3 _AmbientColor;
                float3 _SunTint;
            CBUFFER_END

            StructuredBuffer<float4> _WaterLabWWPos; // xyz=世界坐标 w=寿命 [0,1]
            StructuredBuffer<float4> _WaterLabWWVel; // xyz=速度 w=基准尺寸 (m)
            StructuredBuffer<uint> _WaterLabWWDraw;  // 活粒子槽位（实例号→池槽，Compact 产出）

            // 模拟域（WorldToDomainUv：.xz=域中心、.y=域边长——与水体/沙 shader 同一约定）
            float4 _WaterLabOrigin;
            float4 _WaterLabSunDir;
            float  _WaterLabSimEnabled;
            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);

            float DecodeEta(float e) { return e * 5.0 - 1.0; }

            // 水柱透射（原作 Lit_Whitewater PS 与 Lit_Sand 直射项同一字面量 0.721348）
            static const float WaterColumnExtinction = 0.721348;

            // 6 顶点两三角（DrawProcedural 顶点序，vertexCountPerInstance=6）
            static const float2 QuadCorners[6] =
            {
                float2(-0.5, -0.5), float2(0.5, -0.5), float2(-0.5, 0.5),
                float2(-0.5, 0.5),  float2(0.5, -0.5), float2(0.5, 0.5),
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  life       : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings WhitewaterVertex(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                Varyings OUT;
                uint slot = _WaterLabWWDraw[instanceId]; // 实例号→池槽（drawList 已只含活粒子）
                float4 p = _WaterLabWWPos[slot];
                float life = p.w;
                // 死粒子双保险：寿命=0 → 尺寸塌缩成零面积 + alpha=0（drawArgs 的
                // instanceCount 只数活粒子，这里兜缓冲环残留的死槽）
                // 尺寸按原作 Lit_Whitewater VS：0.1×(1−exp(−5.77078×life))；
                // vel.w 存的是 0.1×(0.6+0.8·随机)，用同一指数曲线收口。
                float size = _WaterLabWWVel[slot].w
                           * (1.0 - exp(-5.77078 * saturate(life))) * _SizeScale;
                float2 corner = QuadCorners[vertexId];
                float3 right = normalize(UNITY_MATRIX_V[0].xyz);
                float3 up = normalize(UNITY_MATRIX_V[1].xyz);
                float3 worldPos = p.xyz + (right * corner.x + up * corner.y) * size;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.positionWS = worldPos;
                OUT.uv = corner + 0.5;
                OUT.life = life;
                return OUT;
            }

            half4 WhitewaterFragment(Varyings IN) : SV_Target
            {
                // ---- 域裁剪（原作：worldXZ 出模拟域、或落到世界地面之下 → discard。
                // 原作地面在 y=0 以上，故写 y<0；本仓近岸地形低至 −1.3 m（静水面恰在 y≈0），
                // 直接照抄 y<0 会把离岸粒子整片丢掉——等价语义是「沉到沙面之下」）
                float2 domUv = (IN.positionWS.xz - _WaterLabOrigin.xz)
                             / max(_WaterLabOrigin.y, 1e-4) + 0.5;
                if (_WaterLabSimEnabled > 0.5)
                {
                    if (any(domUv < 0.0) || any(domUv > 1.0)) discard;
                }
                float2 sUv = saturate(domUv);
                float ground = DecodeEta(SAMPLE_TEXTURE2D(
                    _WaterLabGroundTex, sampler_WaterLabGroundTex, sUv).r);
                if (_WaterLabSimEnabled > 0.5 && IN.positionWS.y < ground) discard;

                // ---- billboard 径向 alpha（原作 PS：max(0.5−‖uv−0.5‖,0) ×
                // sat(life×10) × 0.4；_Alpha 即那个 0.4）
                float d = length(IN.uv - 0.5);
                float falloff = saturate(0.5 - d);
                float alpha = falloff * saturate(IN.life * 10.0) * _Alpha;
                if (alpha <= 0.0) discard;

                // ---- 水柱透射：粒子处水深 = η − 地形
                float eta = DecodeEta(SAMPLE_TEXTURE2D(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, sUv).r);
                float columnT = exp(-WaterColumnExtinction * max(eta - ground, 0.0));

                // ---- 受光泡沫：直射×阴影×水柱 + 天光，乘水沫色（满照归一，见文件头）
                float3 sunRaw = _WaterLabSunDir.xyz;
                float3 sunDir = (dot(sunRaw, sunRaw) < 0.25)
                    ? normalize(float3(-0.525, 0.669, -0.525)) : normalize(sunRaw);
                float sunUp = max(sunDir.y, 0.0);
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                float3 lightFull = _AmbientColor + _SunTint * sunUp;
                float3 lightNow = _AmbientColor + _SunTint * sunUp * shadow * columnT;
                float3 col = _BaseColor * (lightNow / max(lightFull, 1e-4));

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
