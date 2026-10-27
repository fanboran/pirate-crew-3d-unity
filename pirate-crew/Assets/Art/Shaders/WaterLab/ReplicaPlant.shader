// ============================================================================
// ReplicaPlant.shader —— Sandcastle "Lit/Plant" 八面体视向 imposter 植物着色器（I6a）
// ============================================================================
// 翻译源：external/sandcastle-ref/rt/out/Lit_Plant/seg00(VS Forward)/seg04(PS Forward)/
//         seg08(VS ShadowCaster)/seg12(PS ShadowCaster)，逐指令对表见
//         rt/reports/G-plants.md §2（行号引用均为该反汇编文件的行序）。
// 底稿 G-plant.hlsl 在落成时按 seg04 原文修正三处：
//   ① 八面体投影取局部视向 (x,z) 分量（seg04 L44 `div r0.xy, r0.xzxx`，非 x/y）；
//   ② 三格第二格偏移 = (k, 1−k)（seg04 L106-107，k=ceil(fx−fy)∈{0,1}，即
//      fx>fy 取右邻、否则取上邻；G 报告/底稿的 (k,−k) 系笔误）；
//   ③ 切线基 tA/tB 按 L62-68 字面转录（底稿的 cross(n.zxy,up) 与反汇编不符）。
// 带 [真值] = 反汇编字面量/材质直读；带 [URP] = 换槽位（语义等价，I6b/L2 对账清单见
// G-plants.md §6）。装配（材质/凸包网格/挂点）由 I6b 接，本文件只定 shader。
// ============================================================================
Shader "WaterLab/PlantImposter"
{
    Properties
    {
        [MainTexture] _atlasMap ("Imposter Atlas", 2D) = "white" {}
        // 网格格数挂在纹理 ST 上：Unity 对 2D 属性自动生成 _atlasMap_ST 槽（材质 texenv
        // m_Scale=(N,N)），即原作 cb1[0] 真身（G-plants.md §1 cb1 表）——不在 Properties
        // 重复声明 Vector（会与隐式 ST 槽冲突）。offset 恒 (0,0)。
        pivotOffset ("Pivot Offset (xyz=center, w=size)", Vector) = (0, 0.3, 0, 1.3)
        shadowDepthBiais ("Shadow Depth Biais", Float) = 0.147    // 原作拼写如此（cb1[2].x）
        _Cutoff ("Coverage Cutoff", Float) = 0.5                  // 原作材质 _Cutoff=0.5
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest"
               "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Back        // [真值] 原作材质 _Cull=2
        ZWrite On

        HLSLINCLUDE
        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        // ---- 逐实例数据（Plant.cs/Particle.cs 缓冲布局，stride=48=3×float4，G §3.1）----
        //  [0] rotation.xyzw  四元数（切线对齐 ∘ 随机偏航；Distribution 写入/Update 维护）
        //  [1] position.xyz   世界位 = (wx, 地面高, wz)（xz 永不移动，Update 只写 y@+20）
        //      size           Distribution 一次性写入 = lerp(sizeRange,u)·肥力^0.2
        //  [2] velocity.xyz   交互力阻尼速度（Update 维护，只驱动姿态不驱动 xz 位移）
        //      age            出生计时（VS 出生曲线用；Update +=dt）
        StructuredBuffer<float4> _Plants;    // 原作 material.SetBuffer("plants")
        StructuredBuffer<uint>  _AliveList;  // 原作 material.SetBuffer("aliveList")

        TEXTURE2D(_atlasMap); SAMPLER(sampler_atlasMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _atlasMap_ST;   // = cb1[0]：xy=网格数 N（纹理 ST 天然承载，真身）
            float4 pivotOffset;    // = cb1[1]：xyz=imposter 立方中心局部偏移，w=立方世界尺寸
            float  shadowDepthBiais; // = cb1[2].x（仅 ShadowCaster PS 用）
            float  _Cutoff;        // 总覆盖阈值（原作 0.5）
        CBUFFER_END

        // ---- 全局（$Globals，由 ReplicaPlantSystem 下发）----
        float4 _WaterLabPlantBoundsSize;  // [URP↔cb0[116].xz] 沙滩世界 XZ 尺寸（B 流 30×30；.y 备用）
        float3 _WaterLabPlantAmbient;     // [URP↔cb0[40]] 环境色——运行时专用 TODO（见系统注释）

        struct Attributes
        {
            float3 position : POSITION;   // 代理凸包顶点（mesh=<植物>_01_Collider，I6b 由 placement JSON 生成）
            float3 normal   : NORMAL;
            uint   instance : SV_InstanceID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 cornerL   : TEXCOORD0; // o1 = v0.xyz（未缩放局部角点，PS 视线起点）[seg00]
            float3 normalW   : TEXCOORD1; // o2 = q·normal（世界假法线，仅光照 NdotL）[seg00]
            float3 centerVS  : TEXCOORD2; // o3 = VIEW·(粒子位+q·pivotOffset.xyz)（imposter 中心平面）[seg00]
            float4 viewLocal : TEXCOORD3; // o4 = (invQ·(视图原点−中心), 缩放后尺寸) [seg00]
        };

        // 出生缩放曲线 [真值]（seg00：ld+44 → mul_sat·3 → −1 → y²,y³ → mad 2.70158,1.70158）：
        // t = saturate(age*3)−1 ∈ [−1,0]；s = 2.70158·t³ + 1.70158·t² + 1（t=−1→0，age≥1/3s→1）
        float GrowScale(float age)
        {
            float t = saturate(age * 3.0) - 1.0;
            float t2 = t * t;
            return 2.70158 * (t2 * t) + 1.70158 * t2 + 1.0;
        }

        // 四元数旋转（与原作 cross 乘积展开式恒等，seg00 的 r3/r4 链）
        float3 QuatRot(float4 q, float3 v)
        {
            return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
        }

        // cell 索引 → cell 中心方向（seg04 L52-61：div→·2−1→(x+y, x−y)·0.5→1−|px|−|py|→nrm）
        float3 OctCellDir(float2 cell, float2 cellMax)
        {
            float2 n = cell / cellMax * 2.0 - 1.0;
            float2 p = float2(n.x + n.y, n.x - n.y) * 0.5;   // L54-56
            float  z = 1.0 - abs(p.x) - abs(p.y);            // L57-58
            return normalize(float3(p.x, z, p.y));           // L59-61（y=up，与正向投影 x/z 对偶）
        }

        // 单格采样（seg04 L52-102 每格重复的整段：解码→视向-平面求交→切线投影→格内 UV→守卫→覆盖权重）
        // camLocal/ray 用原尺寸单位；centerN = cb1[1].xyz/w 归一化中心（原作 r5）——勿把 cam 也除 w。
        void SampleCellAt(float2 cellN, float2 cellMax, float2 grid,
                          float3 ray, float3 camLocal, float3 centerN, float size, float coverageK,
                          out float3 rgb, out float hOff, out float weight)
        {
            weight = 0; rgb = 0; hOff = 0;
            float2 base = floor(cellN);
            float3 n = OctCellDir(base, cellMax);

            // 视线 ∩ 过 centerN、法线 n 的平面：
            // t = (dot(n,cam)−dot(n,centerN))/dot(n,ray)（seg04 L69-78，两条 dp3 的差除以 denom）
            float denom = dot(n, ray);
            float t = (dot(n, camLocal) - dot(n, centerN)) / denom;
            float3 hit = camLocal + ray * t - centerN;       // L79-80（归一化框架下的命中点）

            // 切线基（seg04 L62-68 字面转录；轴序敏感，勿"化简"为 cross 习惯式）
            float3 tA = normalize(float3(0.0, -n.x, n.z));                       // L62-66
            float3 tB = float3(-n.x * n.y, n.z * n.z + n.x * n.x, -n.y * n.z);   // L67-68

            // 格内 UV：L81 dot(tB,hit)；L82-83 dp2 取负 → (−(n.z·hx−n.x·hz))；L84 ·w+0.5
            float inCellX = -(n.z * hit.x - n.x * hit.z);
            float2 inCell = float2(inCellX, dot(tB, hit)) * pivotOffset.w + 0.5;

            // 格内守卫：0<u<1（L85-92 四条 lt + and → 0/1）；出格权重 0
            float2 g = step(0.0, inCell) * step(inCell, 1.0);
            float guard = g.x * g.y;

            // 图集 UV：(base+inCell)/grid 后 div_sat（L87-88）
            float2 uv = saturate((base + inCell) / grid);

            float4 col = SAMPLE_TEXTURE2D_LOD(_atlasMap, sampler_atlasMap, uv, 0);
            // 高度：原作第二次采样（s2，同 alpha 通道 L94）——同状态采样器下与 col.a 恒等（G §6）
            float h = col.a;
            hOff = (h - 0.5) / pivotOffset.w * size;         // L95-97：div cb1[1].w; mul v4.w

            // 覆盖权重：smoothstep(saturate(a·K))，K=2.5(Forward)/10(ShadowCaster) [真值 L98-102]
            float s = saturate(col.a * coverageK);
            weight = (s * s * (3.0 - 2.0 * s)) * guard;
            rgb = col.rgb;
        }

        // 三格混合主体（Forward/ShadowCaster 共用；seg04/seg12 主体）
        void ImposterShade(float3 camLocal, float size, float3 cornerL, float coverageK,
                           out float3 albedo, out float depthOff, out float coverage)
        {
            float3 ray = normalize(camLocal - cornerL);      // L73-76：nrm(v4.xyz − v1.xyz)

            // 原作只有中心点是归一化的（r5 = cb1[1].xyz / cb1[1].w，L70）；camLocal/ray 保持原尺寸
            float3 centerN = pivotOffset.xyz / pivotOffset.w;

            float2 grid    = _atlasMap_ST.xy;                // cb1[0].xy
            float2 cellMax = grid - 1.0;                     // L48：add cb1[0].xxxy, (0,0,−1,−1)

            // 视向八面体投影（seg04 L40-47 [真值]）：
            //   归一 camLocal → Σ|分量| → **取 (x,z) 两分量**除之（L44 div r0.xy, r0.xzxx）
            //   → oct = ((z+x), (x−z))·0.5 + 0.5（L45-47）
            float3 vdir = normalize(camLocal);
            float2 ab = float2(vdir.x, vdir.z);
            float2 oct = float2(ab.y + ab.x, ab.x - ab.y) * 0.5 + 0.5;
            float2 cellUV = oct * cellMax;                   // L49
            float2 base = floor(cellUV);                     // L50 round_ni
            float2 f = frac(cellUV);                         // L51

            // 三格 + barycentric 权重（和恒 1）[L103-108 真值]：
            //   k = ceil(fx−fy)（round_pi）；cell1 = base + (k, 1−k)——fx>fy 取右邻 (1,0)、
            //   否则取上邻 (0,1)（底稿/G 报告的 (k,−k) 系笔误，按反汇编修正）
            float k = ceil(f.x - f.y);
            float w0 = min(1.0 - f.x, 1.0 - f.y);            // cell0 = base
            float w1 = abs(f.x - f.y);                       // cell1 = base+(k, 1−k)
            float w2 = min(f.x, f.y);                        // cell2 = base+(1,1)

            albedo = 0; depthOff = 0; coverage = 0;
            float3 rgb; float hOff; float w;

            SampleCellAt(base, cellMax, grid, ray, camLocal, centerN, size, coverageK, rgb, hOff, w);
            float ww = w * w0;
            albedo += rgb * ww; depthOff += hOff * ww; coverage += ww;

            SampleCellAt(base + float2(k, 1.0 - k), cellMax, grid, ray, camLocal, centerN, size, coverageK, rgb, hOff, w);
            ww = w * w1;
            albedo += rgb * ww; depthOff += hOff * ww; coverage += ww;

            SampleCellAt(base + 1.0, cellMax, grid, ray, camLocal, centerN, size, coverageK, rgb, hOff, w);
            ww = w * w2;
            albedo += rgb * ww; depthOff += hOff * ww; coverage += ww;
        }
        ENDHLSL

        // ====================================================================
        // Forward（seg00 VS + seg04 PS 恒等翻译；光照/阴影/unproject 槽位换 URP）
        // ====================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _MAIN_LIGHT_SHADOWS_SOFT

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                uint idx = _AliveList[IN.instance];           // L：ld t1[instanceID] → 粒子索引
                float4 rot   = _Plants[idx * 3 + 0];          // 偏移 0
                float4 posSz = _Plants[idx * 3 + 1];          // 偏移 16
                float4 velAg = _Plants[idx * 3 + 2];          // 偏移 32（age=.w）

                float scale = GrowScale(velAg.w) * posSz.w;   // seg00：mul r0.x, s, r1.w
                float3 surfW = posSz.xyz + QuatRot(rot, scale * IN.position);
                float3 cenW  = posSz.xyz + QuatRot(rot, pivotOffset.xyz);

                OUT.positionCS = TransformWorldToHClip(surfW); // [URP↔cb0[62..65] VP]
                OUT.cornerL    = IN.position;                  // o1（未缩放）
                OUT.normalW    = QuatRot(rot, IN.normal);      // o2
                OUT.centerVS   = mul(GetWorldToViewMatrix(), float4(cenW, 1)).xyz; // o3 [↔cb0[50..53]]

                // 四元数逆 = (−x,−y,−z,w)/|q|²（seg00：dp4 后 mul (−1,−1,−1,1)）
                float n2 = dot(rot, rot);
                float4 invQ = rot * float4(-1, -1, -1, 1) / n2;
                OUT.viewLocal.xyz = QuatRot(invQ, _WorldSpaceCameraPos - cenW);    // [↔cb0[6]]
                OUT.viewLocal.w = scale;                                            // o4.w
                return OUT;
            }

            struct FragOut
            {
                float4 color : SV_Target0;
                float  depth : SV_Depth;   // 原作 SV_Depth：imposter 中心平面深度 + 高度场校正
            };

            FragOut Frag(Varyings IN)
            {
                FragOut OUT;
                float3 albedo; float depthOff; float coverage;
                ImposterShade(IN.viewLocal.xyz, IN.viewLocal.w, IN.cornerL,
                              2.5, albedo, depthOff, coverage);   // [真值] Forward 覆盖权重 K=2.5
                clip(coverage - _Cutoff);                          // [真值] add −0.5; lt; discard_nz（材质 _Cutoff=0.5）

                // SV_Depth：视空间中心平面 z 逐像素减加权高度偏移后重投影 [seg04 oDepth 段]
                float3 vs = IN.centerVS;
                vs.z -= depthOff;
                float4 cp = mul(UNITY_MATRIX_P, float4(vs, 1.0));  // [URP↔cb0[46..49] z/w 列]

                // unproject→世界位（seg04 尾段 [↔cb0[66..69] VP⁻¹]）：出沙滩 XZ 界 / y<0 剔除
                float3 planeW = mul(UNITY_MATRIX_I_V, float4(vs, 1.0)).xyz;
                float2 halfSize = _WaterLabPlantBoundsSize.xz * 0.5;
                clip(halfSize.x - abs(planeW.x));
                clip(halfSize.y - abs(planeW.z));
                clip(planeW.y);

                // 光照 [seg04 尾段]：太阳 NdotL·色·阴影 + 月 NdotL·色（无高光无雾）
                // [↔cb0[123]/[125] 太阳方向/色（阴影只乘太阳）、cb0[124]/[126] 月]
                float4 shadowCoord = TransformWorldToShadowCoord(planeW);
                Light main = GetMainLight(shadowCoord);
                float3 lit = saturate(dot(IN.normalW, -main.direction)) * main.color * main.shadowAttenuation;
                Light moon = GetAdditionalLight(0, planeW);        // [↔cb0[124]/[126] MoonLight]
                lit += saturate(dot(IN.normalW, -moon.direction)) * moon.color;
                float3 col = albedo * lit + albedo * _WaterLabPlantAmbient;  // [↔cb0[40] 环境]

                OUT.color = float4(col, 1.0);
                OUT.depth = cp.z / cp.w;
                return OUT;
            }
            ENDHLSL
        }

        // ====================================================================
        // ShadowCaster（seg08 VS + seg12 PS）。差异三处 [真值]：
        //   ① 视图原点 = 阴影虚拟相机位 cb0[57].xyz/w（URP 无直暴露——TODO(I6b/L2)：要恒等时
        //     脚本注入级联原点，草稿按主相机位处理）；
        //   ② 覆盖阈值 K=10（seg12 mul_sat l(10.0)）；
        //   ③ 深度拉回 z' = centerVS.z − (depthOff + size·shadowDepthBiais)（seg12，cb1[2].x，
        //     向光侧消除凸包阴影）。VS 无出生曲线（seg08 无 age 读入，阴影即时全尺寸）。
        // ====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 cornerL   : TEXCOORD0;
                float3 worldPos  : TEXCOORD1;  // seg08 的 o2 = 世界位（地形剔除用，不转法线）
                float3 centerVS  : TEXCOORD2;
                float4 viewLocal : TEXCOORD3;
            };

            ShadowVaryings ShadowVert(Attributes IN)
            {
                ShadowVaryings OUT;
                uint idx = _AliveList[IN.instance];
                float4 rot   = _Plants[idx * 3 + 0];
                float4 posSz = _Plants[idx * 3 + 1];
                float scale = posSz.w;                       // [真值] 阴影 VS 无出生曲线
                float3 surfW = posSz.xyz + QuatRot(rot, scale * IN.position);
                float3 cenW  = posSz.xyz + QuatRot(rot, pivotOffset.xyz);

                OUT.worldPos   = surfW;
                OUT.positionCS = TransformWorldToHClip(surfW);
                OUT.cornerL    = IN.position;
                OUT.centerVS   = mul(GetWorldToViewMatrix(), float4(cenW, 1)).xyz;
                float n2 = dot(rot, rot);
                float4 invQ = rot * float4(-1, -1, -1, 1) / n2;
                OUT.viewLocal.xyz = QuatRot(invQ, _WorldSpaceCameraPos - cenW); // 恒等化见 pass 头注释①
                OUT.viewLocal.w = scale;
                return OUT;
            }

            struct ShadowFragOut
            {
                float depth : SV_Depth;
            };

            ShadowFragOut ShadowFrag(ShadowVaryings IN)
            {
                ShadowFragOut OUT;
                // 地形剔除 [seg12 头部 3 条 discard]：出沙滩 XZ 界 / y<0 一律裁掉
                float2 halfSize = _WaterLabPlantBoundsSize.xz * 0.5;
                clip(halfSize.x - abs(IN.worldPos.x));
                clip(halfSize.y - abs(IN.worldPos.z));
                clip(IN.worldPos.y);

                float3 albedo; float depthOff; float coverage;
                ImposterShade(IN.viewLocal.xyz, IN.viewLocal.w, IN.cornerL,
                              10.0, albedo, depthOff, coverage);  // [真值] 阴影通道 K=10
                clip(coverage - _Cutoff);

                // 深度拉回：向光侧偏移 hOff + size·shadowDepthBiais [seg12 mad 链，cb1[2].x]
                float3 vs = IN.centerVS;
                vs.z -= depthOff + IN.viewLocal.w * shadowDepthBiais;
                float4 cp = mul(UNITY_MATRIX_P, float4(vs, 1.0));
                OUT.depth = cp.z / cp.w;
                return OUT;
            }
            ENDHLSL
        }
    }
}
