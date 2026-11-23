// ============================================================================
// CloudSeaWater.shader —— 云端漫步（7 座云台浮在浅海）的海水/水洼/玻璃**双趟透明**材质
//
// 【它在解决什么】两条渲染链各有一套透明件收件口径，同一材质必须两链都可见、
//   换风格不换材质：
//   · CellShade（像素化域）：`PixelartTransparentFeature` 只收 `LightMode =
//     PixelartTransparent` + 透明队列 [3000,5000] 的材质，画进**艺术画布**——那里没有
//     本趟自己的深度附件，片元里手动比 `_PixelartDepthBuffer`（细像素深度）做遮挡。
//   · PostLine（全分辨率前向）：主相机是普通 URP 前向，按标准 `LightMode =
//     UniversalForward` 收件：`Blend SrcAlpha OneMinusSrcAlpha` + `ZWrite Off` +
//     硬件 `ZTest LEqual`。
//
// 【2026-11-18 重写：两趟各自独立 CBUFFER，删除 SubShader 级 HLSLINCLUDE】
//   旧版把 UnityPerMaterial/水波函数放在 SubShader 的 HLSLINCLUDE 里共享，实测两趟
//   **都不出图**（PostLine 水下只剩沙底；用同样的最小代码另起一趟却正常）。逐项排除
//   后确认问题出在这套共享写法与两趟的组合上；本版每趟自带 CBUFFER、函数各自内联，
//   结构上不再有共享块可以失效。改动时保持“两趟颜色/水波公式逐字一致”。
//
// 【顶点色默认不乘（2026-11-18）】FBX 水网格没有 COLOR 通道，Unity 给顶点色输入 0
//   （不是白）——`贴图 × 顶点色 × _Color` 会把整片水乘成 alpha 0。`_VertexColorWeight`
//   默认 0，只有将来真烘了顶点渐变的水件才置 1。水色不靠贴图，`_MainTex` 也刻意不参与
//   颜色（贴图缺失时采样可能为 0，同样会把水乘没）。
//
// 【水波：平涂色块的缓慢起伏】世界坐标上 3 组不同方向/频率的 sin 叠加成缓涌
//   → 量化 3 档明度块（floor 取整、档间硬边）→ 上下调明度 ±_WaveStrength（中档恒为原色）。
//   相位吃 `_Time.y × _WaveSpeed`，运行时组件只做幅度呼吸。alpha 全程不参与水波、不挖洞。
// ============================================================================

Shader "PirateCrew/CloudSea/Water"
{
    Properties
    {
        _MainTex ("贴图（保留槽位；当前不参与颜色）", 2D) = "white" {}
        _Color ("乘色 rgba（a = 不透明度；深浅水色在这里分档）", Color) = (0.31, 0.83, 0.85, 0.60)
        _WaveStrength ("水波明度幅度（±；量化块间，0 = 全平）", Range(0, 0.5)) = 0.22
        _WaveSpeed ("水波速度（相位 = _Time.y × 此值）", Range(0, 4)) = 0.6
        _DepthTest ("手动深度遮挡（仅像素化趟；0 = 纯覆盖不裁）", Range(0, 1)) = 1
        _VertexColorWeight ("顶点色权重（0 = 不乘，网格无 COLOR 通道时必须 0）", Range(0, 1)) = 0
        // WaterLab 域内挖洞：**只有 Cloud_Sea_Surface 专用材质置 1**（水洼/玻璃保持 0）。
        // 简版水面与 ocean 同高共存时，透明排序会把大平面简版水画在 ocean 之上，
        // 把 WaterLab 水整片盖住（2026-11-21 实拍：多轮"焦散/海浪看不见"的真因）。
        _WaterLabHole ("WaterLab 域内挖洞（仅海面材质置 1）", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ==================================================================
        // Pass 1 / PixelartTransparent：CellShade 像素化域的透明趟。
        //   结构同 PixelartTransparent.shader（手动深度遮挡 + 覆盖语义），
        //   差异只有一处：输出 rgb 过一遍量化水波明度。
        // ==================================================================
        Pass
        {
            Name "PixelartTransparent"
            Tags { "LightMode" = "PixelartTransparent" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // 单面：海面/水洼/玻璃都从上方看。水洼是闭合透镜，Cull Back 只画上表面，
            // 避免上下两层各混合一次 alpha（实拍：水洼中心发黑、像洞）。
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _WaveStrength;
                float _WaveSpeed;
                float _DepthTest;
                float _VertexColorWeight;
                // 海面挖洞开关（必须进 UnityPerMaterial：CBUFFER 外的材质属性在 SRP Batcher
                // 下不会绑定，读到的永远是 0——2026-11-21 实拍"挖洞没生效"的真因）。
                float _WaterLabHole;
            CBUFFER_END

            // 域闸门（全局）：1 = CellShade 像素化域，只走本趟；0 = PostLine 前向。
            float _CloudSeaPixelDomain;

            // WaterLab 挖洞（仅海面材质 _WaterLabHole=1，声明在 UnityPerMaterial）：
            // 域中心/边长来自模拟核全局，域缘内缩 1.5 m 与 ocean 的 _EdgeFade 渐隐带
            // 重叠，接缝处两层水接力不露底。
            float4 _WaterLabOrigin;      // (centerX, size, centerZ, cells)
            float _WaterLabSimEnabled;

            void ClipWaterLabHole(float3 positionWS)
            {
                if (_WaterLabHole < 0.5 || _WaterLabSimEnabled < 0.5)
                    return;
                // 挖洞半径 = ocean 网格半宽 45 m − 1 m：整张可见海面只留 WaterLab ocean
                // 一层（简版水只做 90 m 外的远景兜底），避免域外叠出方形环带。
                float2 halfDomain = float2(44.0, 44.0);
                float2 d = halfDomain - abs(positionWS.xz - _WaterLabOrigin.xz);
                if (d.x > 0.0 && d.y > 0.0)
                    clip(-1);
            }

            TEXTURE2D(_PixelartDepthBuffer);        SAMPLER(sampler_PixelartDepthBuffer);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;  // NDC：/w = 屏幕 uv（手动深度采样口径）
                float4 color      : COLOR;
                float4 clipPos    : TEXCOORD1;  // 原始 clip 坐标（z/w = NDC 深度口径明确）
                float3 positionWS : TEXCOORD2;  // 水波相位取样点
            };

            // 水波明度：世界 XZ + 少量 Y 斜切（水洼/斜面借 Y 拿纵向变化）。
            float WaveBand(float3 positionWS, float phase)
            {
                float2 p = positionWS.xz + positionWS.yy * 0.37;
                float s = sin(p.x * 2.6 + phase * 1.3)
                        + sin((p.x + p.y) * 1.8 - phase * 0.9)
                        + sin(p.y * 3.4 + phase * 1.7);
                float t = saturate(s * (1.0 / 3.0) * 0.5 + 0.5);
                return floor(t * 3.0) / 3.0;               // 量化 3 档：0 / 1/3 / 2/3
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vpi.positionCS;
                output.clipPos = vpi.positionCS;
                output.screenPos = vpi.positionNDC;
                output.color = input.color;
                output.positionWS = vpi.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_CloudSeaPixelDomain < 0.5) clip(-1);   // PostLine：让位给前向趟
                ClipWaterLabHole(input.positionWS);

                // 顶点色权重默认 0（网格无 COLOR 通道时输入是 0，直接乘会整片消失）。
                half4 vcol = lerp(half4(1.0, 1.0, 1.0, 1.0), input.color,
                    saturate(_VertexColorWeight));
                half4 tint = vcol * _Color;

                // 手动深度遮挡（等效 ZTest LEqual，对细像素深度的块锚点采样）。
                if (_DepthTest > 0.5)
                {
                    float2 screenUv = input.screenPos.xy / input.screenPos.w;
                    float storedRaw = SAMPLE_TEXTURE2D(_PixelartDepthBuffer,
                        sampler_PixelartDepthBuffer, screenUv).r;
                    float ndcDepth = input.clipPos.z / input.clipPos.w;
#if defined(UNITY_REVERSED_Z)
                    float deviceDepth = ndcDepth;
                    clip(deviceDepth - storedRaw);     // reversed-Z：数值大 = 近
#else
                    float deviceDepth = ndcDepth * 0.5 + 0.5;
                    clip(storedRaw - deviceDepth);     // 正向 Z：数值小 = 近
#endif
                }

                float band = WaveBand(input.positionWS, _Time.y * _WaveSpeed);
                tint.rgb *= lerp(1.0 - _WaveStrength, 1.0 + _WaveStrength, band);
                return tint;
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2 / UniversalForward：PostLine 主相机（普通 URP 前向）的透明趟。
        //   标准透明收件：SrcAlpha 混合、ZWrite Off、硬件 ZTest LEqual。
        // ==================================================================
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertForward
            #pragma fragment FragForward

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _WaveStrength;
                float _WaveSpeed;
                float _DepthTest;
                float _VertexColorWeight;
                // 海面挖洞开关（必须进 UnityPerMaterial：CBUFFER 外的材质属性在 SRP Batcher
                // 下不会绑定，读到的永远是 0——2026-11-21 实拍"挖洞没生效"的真因）。
                float _WaterLabHole;
            CBUFFER_END

            float _CloudSeaPixelDomain;

            float4 _WaterLabOrigin;
            float _WaterLabSimEnabled;

            void ClipWaterLabHoleForward(float3 positionWS)
            {
                if (_WaterLabHole < 0.5 || _WaterLabSimEnabled < 0.5)
                    return;
                // 挖洞半径 = ocean 网格半宽 45 m − 1 m：整张可见海面只留 WaterLab ocean
                // 一层（简版水只做 90 m 外的远景兜底），避免域外叠出方形环带。
                float2 halfDomain = float2(44.0, 44.0);
                float2 d = halfDomain - abs(positionWS.xz - _WaterLabOrigin.xz);
                if (d.x > 0.0 && d.y > 0.0)
                    clip(-1);
            }

            struct AttributesForward
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct VaryingsForward
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float3 positionWS : TEXCOORD0;
            };

            float WaveBandForward(float3 positionWS, float phase)
            {
                float2 p = positionWS.xz + positionWS.yy * 0.37;
                float s = sin(p.x * 2.6 + phase * 1.3)
                        + sin((p.x + p.y) * 1.8 - phase * 0.9)
                        + sin(p.y * 3.4 + phase * 1.7);
                float t = saturate(s * (1.0 / 3.0) * 0.5 + 0.5);
                return floor(t * 3.0) / 3.0;
            }

            VaryingsForward VertForward(AttributesForward input)
            {
                VaryingsForward output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.positionWS = positionWS;
                return output;
            }

            half4 FragForward(VaryingsForward input) : SV_Target
            {
                if (_CloudSeaPixelDomain > 0.5) clip(-1);   // CellShade：让位给像素化趟
                ClipWaterLabHoleForward(input.positionWS);

                half4 vcol = lerp(half4(1.0, 1.0, 1.0, 1.0), input.color,
                    saturate(_VertexColorWeight));
                half4 tint = vcol * _Color;

                float band = WaveBandForward(input.positionWS, _Time.y * _WaveSpeed);
                tint.rgb *= lerp(1.0 - _WaveStrength, 1.0 + _WaveStrength, band);
                return tint;
            }
            ENDHLSL
        }
    }
}
