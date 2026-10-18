// ============================================================================
// WaterLabWhitewater.shader —— 白水粒子（W4，外包债③清偿）：GPU indirect 点精灵。
// 仿真侧 spawn/更新/计数（WaterLabSim.compute 的 Whitewater* kernel，泡沫场驱动）
// → drawArgs → WaterLabRefractionFeature 的 pass DrawProceduralIndirect 出画。
// 对标原作 Whitewater（泡沫场实例化粒子，Culling kernel 写 args——本仓粒子全在
// 64 m 域内且随相机取景，不做逐粒视锥剔除，instanceCount 只数活粒子）。
// 透明队列画在水面之上（水体 Geometry+400 已写深度，浪背面的白水被正确遮挡）。
// StructuredBuffer 需要 target 4.5。
// ============================================================================

Shader "PirateCrew/WaterLab/Whitewater"
{
    Properties
    {
        _BaseColor  ("水沫色", Color) = (0.95, 0.97, 0.96, 1.0)
        _Alpha      ("不透明度", Range(0.0, 1.0)) = 0.85
        _SizeScale  ("尺寸缩放", Range(0.1, 2.0)) = 1.0
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float3 _BaseColor;
                float  _Alpha;
                float  _SizeScale;
            CBUFFER_END

            StructuredBuffer<float4> _WaterLabWWPos; // xyz=世界坐标 w=寿命 [0,1]
            StructuredBuffer<float4> _WaterLabWWVel; // xyz=速度 w=基准尺寸 (m)
            StructuredBuffer<uint> _WaterLabWWDraw;  // 活粒子槽位（实例号→池槽，Compact 产出）

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
            };

            Varyings WhitewaterVertex(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                Varyings OUT;
                uint slot = _WaterLabWWDraw[instanceId]; // 实例号→池槽（drawList 已只含活粒子）
                float4 p = _WaterLabWWPos[slot];
                float life = p.w;
                // 死粒子双保险：寿命=0 → 尺寸塌缩成零面积 + alpha=0（drawArgs 的
                // instanceCount 只数活粒子，这里兜缓冲环残留的死槽）
                float size = _WaterLabWWVel[slot].w * (0.4 + 0.6 * life) * _SizeScale;
                float2 corner = QuadCorners[vertexId];
                float3 right = normalize(UNITY_MATRIX_V[0].xyz);
                float3 up = normalize(UNITY_MATRIX_V[1].xyz);
                float3 worldPos = p.xyz + (right * corner.x + up * corner.y) * size;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = corner + 0.5;
                OUT.life = life;
                return OUT;
            }

            half4 WhitewaterFragment(Varyings IN) : SV_Target
            {
                float d = length(IN.uv - 0.5) * 2.0; // 0 中心 → 1 边缘
                float falloff = saturate(1.0 - d);
                float alpha = falloff * falloff * saturate(IN.life) * _Alpha;
                return half4(_BaseColor, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
