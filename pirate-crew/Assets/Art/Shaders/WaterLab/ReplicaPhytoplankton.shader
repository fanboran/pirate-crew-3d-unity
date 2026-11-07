// 夜光浮游生物粒子渲染（D 流件4，ReplicaPhytoplanktonSystem 配套）。
//
// 【对位说明】原作 Lit_Phytoplankton blob 在 D 流解出时 D3DDisassemble 乱码
//（D-sand.md §4.3：粒子族 shader "Dirt 2 程序 / Gravel 4 程序"，Phytoplankton 同批，
// PS 逐指令未完成——外观=深色小方片 + 径向/距离淡出的低配粒子）。本 shader 是
// X-exp2.md §3 可用信息 + 第一手 Culling DXBC 的直译简化：
//   VS：实例位置取 _PhytoPool[drawList[instance]]（y/z=世界 XZ），贴水皮高度
//       = η（_WaterLabHeightTex 解码）+ 0.02；尺寸 = 0.05 × 0.25×(1−exp2(−3·life))（@2450
//       的 e^(−3·life) 位移/生长曲线，exp2 语义）；alpha = 1−exp(−4.328085·life)（Culling
//       kernel 同式，自然 exp）。
//   PS：夜光色 × intensity（DayCycler 夜相强度，主菜单恒 0）× 径向淡出；距离淡出兜底。
// 主菜单 Night=0 → intensity=0 → 全粒子 alpha 0（与原作「不绘制」等价的透明直出）。
Shader "WaterLab/ReplicaPhytoplankton"
{
    Properties
    {
        _BaseColor("夜光基色", Color) = (0.55, 0.95, 0.85, 1.0)
        _Size("粒子基准尺寸", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_WaterLabHeightTex); SAMPLER(sampler_WaterLabHeightTex);
            TEXTURE2D(_WaterLabGroundTex); SAMPLER(sampler_WaterLabGroundTex);

            StructuredBuffer<float4> _PhytoPool;   // x=life y=wx z=wz w=water
            StructuredBuffer<uint> _PhytoDrawList;
            float4 _WaterLabOrigin;                 // x,z=域中心 y=域边长 w=水格数
            float _Size;
            float intensity;                        // 原作材质属性名（DayCycler 夜相强度）
            half4 _BaseColor;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                float fade : TEXCOORD2;
            };

            float DecodeGround(float e) { return e * 5.0 - 1.0; }

            Varyings vert(uint sv : SV_VertexID, uint inst : SV_InstanceID)
            {
                Varyings o;
                float4 p = _PhytoPool[_PhytoDrawList[inst]];
                float life = p.x;

                // 角点展开（DrawProcedural：sv 0..5 → 三角对角点）
                static const float2 corners[6] = {
                    float2(-0.5, -0.5), float2(-0.5, 0.5), float2(0.5, -0.5),
                    float2(0.5, -0.5), float2(-0.5, 0.5), float2(0.5, 0.5)
                };
                float2 corner = corners[sv];

                // 贴水皮高度：η = ground + max(amount−0.05, 0)（与 OutputField 同式反解，
                // 0.02 上抬同白水口径）
                float2 uv = (p.yz + _WaterLabOrigin.y * 0.5) / _WaterLabOrigin.y;
                float ground = DecodeGround(SAMPLE_TEXTURE2D_LOD(
                    _WaterLabGroundTex, sampler_WaterLabGroundTex, uv, 0).r);
                float eta = DecodeGround(SAMPLE_TEXTURE2D_LOD(
                    _WaterLabHeightTex, sampler_WaterLabHeightTex, uv, 0).r);
                float surface = ground + max(eta - ground - 0.05, 0.0) + 0.02;

                // 尺寸生长曲线（X-exp2.md §3 @2450：0.25×(1−exp2(−3·life))，exp2 语义）
                float size = _Size * 0.25 * (1.0 - exp2(-3.0 * life)) * 40.0;
                float3 world = float3(p.y + corner.x * size, surface, p.z + corner.y * size);

                o.positionCS = TransformWorldToHClip(world);
                o.uv = corner + 0.5;
                // Culling kernel 同式（自然 exp）：alpha 门曲线
                o.alpha = saturate(1.0 - exp(-4.328085 * life)) * saturate(intensity);
                // 距离淡出（D 表低配粒子口径）
                float dist = length(_WorldSpaceCameraPos - world);
                o.fade = saturate(1.0 - (dist - 20.0) / 15.0);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float radial = 1.0 - smoothstep(0.35, 0.5, length(IN.uv - 0.5));
                half4 col = _BaseColor;
                col.a *= IN.alpha * radial * IN.fade;
                return col;
            }
            ENDHLSL
        }
    }
}
