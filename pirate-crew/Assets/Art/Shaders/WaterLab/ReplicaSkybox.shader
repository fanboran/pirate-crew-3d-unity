// ============================================================================
// ReplicaSkybox.shader —— Sandcastle 主菜单程序化天空（shader 名 "Skybox"，sa0
// pid573）的逐指令移植（I5a，裁决 A 案：完整移植）。
//
// 【反汇编依据】external/sandcastle-ref/rt/out/Borders/Skybox@202.prog.hlslasm（VS，
//   vs_4_0，11 指令）+ Skybox@942.prog.hlslasm（PS，ps_4_0，61 指令）。
//   原 pass 状态：Cull Off / ZWrite On / ZTest Always（structure.json 同级记录，
//   ZTest 8=Always）/ 队列 Background。SM 4.0 → 本仓 #pragma target 3.5。
//
// 【⚠ 对 H 报告一处语义修正】reports/H-borders-sky.md §3 称 cb0[134..137] 为
//   "渐变系数组（运行时专用）"。逐指令回读后这不是渐变颜色，而是**世界→阴影空间
//   投影矩阵**（= BeachRenderPipeline.cs:777 上传的 _MainLightWorldToShadow）：
//     shadowUV.xy = pt.x·cb0[134] + pt.y·cb0[135] + pt.z·cb0[136] + cb0[137]
//     ref         = shadowUV.z
//     sample_c_lz（比较采样）@ t0（_ShadowMap）
//     ref ∉ [0,1] → 恒 1（界外不投影）
//   证据：① Skybox PS 中该向量只作为比较采样坐标/参考值，从不作为颜色输出；
//         ② Lit_Base@27222/@29922（Frame）对**片元世界坐标**做完全同构的
//            矩阵投影+sample_c_lz——这是标准片元阴影查表；
//         ③ 管线源码存在该全局矩阵的上传点（_MainLightWorldToShadow）。
//   真正的"天空渐变"在反汇编里是**硬编码形状**：base×(1−0.8·|dir.y|^1.5)，
//   其中 base = cb0[122]×sunColor + ambient。运行时不可恢复项只剩 cb0[122]
//   （本仓 uniform _SkyBaseTint，默认 (1,1,1)，待抓帧）。
//
// 【PS 数学（61 指令逐段）】
//   dir  = normalize(顶点局部位置)                     （@942 #1-3）
//   t    = -camPos.y / dir.y；pt = dir·t + camPos      （#4-5，地平平面 y=0 交点）
//   门控：t≥0 且 |dir.y|≥1e-6，否则 pt≡0               （#6-12，视线朝天时交点无效）
//   dirY15 = |dir.y|^1.5                               （#9-11，log/mul/exp = pow）
//   shadow = 比较采样(_ShadowMap, M·pt)（出界=1）        （#20-25，见上）
//   d    = length(max(|pt.xz| − cb0[118].xz, 0))       （#16-19，界外盒距）
//   base = cb0[122]·sunColor + ambient                 （#50）
//   base = base − 0.8·dirY15·base                      （#51-52，竖向渐变）
//   col  = base·(1−0.2d)·(0.2 + 0.8·min(d/20,1)^0.3)   （#53-57，沙盘外圈大气衰减）
//   elev = sat((sunDirY + 0.1)·13.333333)              （#44-45，cb0[123].y 原作槽语义
//         = 光传播方向 y。日出日落时 =1、正午/深夜 =0；t=1/3 菜单时该因子=0）
//   light = min(shadow + min(d/20,1)^1.5 + elev²(3−2elev), 1)   （#25,42-49；末项
//         = smoothstep(0,1,elev) 的展开式）
//   o   = col·(0.7 + 0.3·light)                        （#58-60）
//   注：DXBC 的 exp/log 是 exp2/log2（常量按 2 底设计）；pow() 编译即 log2/mul/exp2。
//
// 【sunColor/sunDirection 槽位符号】原作 cb0[123]/[125] = DayCycler 写的
//   sunDirection（光**传播**方向）与 sunColor（t=1/3 真值 (1,0.940429,0.872353)）。
//   本仓太阳方向复用实验台已有全局 _WaterLabSunDir（**指向太阳**，WaterLabSimulation.
//   PublishSunDir 发布），故 elev 公式符号取反（sunDirY = −_WaterLabSunDir.y）。
//   菜单时刻 _WaterLabSunDir ≈ (−0.607832, 0.784886, 0.120398)（= −sunDirection）。
//
// 【遗留 TODO（I8/抓帧收口）】
//   1. cb0[122]（_SkyBaseTint）运行时真值待抓帧；默认 (1,1,1) 下天空基色 =
//      sunColor+ambient ≈ (1.698,1.505,1.442)（HDR 域，地平线近白）。
//   2. cb0[118].xz（_SkyBorderSize）语义推定 beachExtents.xz=(15,15)（B 流沙盘
//      30×30 m 的一半），未逐字钉死。
//   3. 阴影比较采样用 URP 级联等价（MainLightRealtimeShadow）；原作是**单矩阵**
//      _MainLightWorldToShadow + _ShadowMap 比较采样（无级联）。语义同构（出界
//      返回 1），但级联选择/软化曲线与原作有差——L2 恒等门禁对账前先记此差。
// ============================================================================

Shader "PirateCrew/WaterLab/ReplicaSkybox"
{
    Properties
    {
        // cb0[122] 运行时专用待抓帧（见文件头修正说明）；Linear/HDR 域物理量用 Vector
        _SkyBaseTint   ("天空基色调制 cb0[122]（运行时专用待抓帧）", Vector) = (0.5660378, 0.4619582, 0.4040584, 1)  // G 流 eid2522 cb0[122] 运行时真值（base=cb0[122]×sunColor+cb0[40] 第一手证实）
        // sunGradient @ t=1/3 Morning（menu-state.json，DayCycler Evaluate 真值）
        _SkySunColor   ("太阳色（t=1/3 真值）", Vector) = (1, 0.940429, 0.872353, 1)
        // RenderSettings.ambientSkyColor 运行时真值 @ t=1/3（menu-state.json，
        // = environmentGradient 经 HSV 色相偏移后 DayCycler 写入；cb0[40] 槽）
        _SkyAmbient    ("环境光色（t=1/3 运行时真值）", Vector) = (0.698039, 0.564706, 0.569923, 1)
        // cb0[118].xz 语义推定 beachExtents.xz（B 流：沙盘 30×30 m → 半宽 15）
        _SkyBorderSize ("大气衰减边界半宽（语义推定）", Vector) = (15, 15, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Skybox"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Skybox"
            Cull Off
            // 原作 pass 状态 ZWrite On / ZTest Always（instruction-tables §4）——成立
            // 前提是其自研管线 **skybox 先于不透明** 绘制（CreateSkyboxRendererList
            // 在背景位）。URP 绘制序是不透明之后才画 skybox，照搬 Always 会把已渲
            // 好的场景整屏覆盖——这里按**绘制序差异**改为标准天空盒状态
            // ZWrite Off / ZTest LEqual（只填未覆盖像素），数学不受影响。
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ReplicaSkyboxVertex
            #pragma fragment ReplicaSkyboxFragment
            // 阴影比较采样的 URP 等价（原作为单矩阵 _MainLightWorldToShadow 查
            // _ShadowMap；TODO 见文件头第 3 条）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyBaseTint;
                float4 _SkySunColor;
                float4 _SkyAmbient;
                float4 _SkyBorderSize;
            CBUFFER_END

            // 实验台已有全局（WaterLabSimulation.PublishSunDir 发布，指向太阳）
            float4 _WaterLabSunDir;

            struct Attributes
            {
                float4 positionOS : POSITION;   // Unity 天空盒约定：局部位置即视线方向
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;  // 原作 o1 = v0（原始局部位置）
            };

            // Skybox@202：obj→world（cb1）→clip（cb0[62..65]）；o1 = v0。
            // Unity 天空盒网格随相机、无旋转 → TransformObjectToHClip 同构。
            Varyings ReplicaSkyboxVertex(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            half4 ReplicaSkyboxFragment(Varyings IN) : SV_Target
            {
                // #1-3：视线方向归一
                float3 dir = normalize(IN.dir);
                // cb0[6] = _WorldSpaceCameraPos
                float3 camPos = GetCameraPositionWS();

                // #4-5：地平平面（y=0）交点。dir.y→0 时 t 发散为 inf/NaN，
                // 原作按同一顺序先算再由门控清零（D3D 与门按位清 NaN）。
                float  t  = -camPos.y / dir.y;
                float3 pt = camPos + dir * t;

                // #6-12：双门控——t≥0（交点在前方）且 |dir.y|≥1e-6（不近水平），
                // 否则 pt ≡ 0（朝天视线：后续阴影采样坐标=矩阵平移行，出界返回 1）。
                // !(t >= 0) 的写法让 NaN 落入清零分支（与 asm 的 ge+and 等价）。
                if (!(t >= 0.0) || !(abs(dir.y) >= 1e-6))
                    pt = float3(0.0, 0.0, 0.0);

                // #9-11：|dir.y|^1.5（log2/mul/exp2 = pow；竖向渐变形状因子）
                float dirY15 = pow(abs(dir.y), 1.5);

                // #20-25：阴影比较采样。原作 = M·pt → sample_c_lz(_ShadowMap, uv.xy,
                // ref=uv.z)，ref∉[0,1] 恒 1。URP 等价：级联阴影坐标 + 比较采样
                // （界外/超阴影距离同样返回 1）。TODO：L2 对账时评估换精确单矩阵路径。
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(pt));

                // #16-19：交点到沙盘边界的盒外距 d（|pt.xz| 对称四象限）
                float d = length(max(abs(pt.xz) - _SkyBorderSize.xy, 0.0));

                // #50：base = cb0[122]·sunColor + ambient
                float3 base = _SkyBaseTint.rgb * _SkySunColor.rgb + _SkyAmbient.rgb;
                // #51-52：竖向渐变（地平线 base → 天顶 0.2·base）
                base = base - 0.8 * dirY15 * base;

                // #53-57：沙盘外圈大气衰减
                float dPow = min(d * 0.05, 1.0);                       // min(d/20,1)
                float3 col = base * (1.0 - 0.2 * d)
                           * (0.2 + 0.8 * pow(dPow, 0.3));

                // #44-45：太阳仰角因子。原作槽 cb0[123] = sunDirection（光传播方向）；
                // _WaterLabSunDir 指向太阳 → sunDirection.y = -_WaterLabSunDir.y。
                // sat((sunDirection.y + 0.1) × 13.333333)；t=1/3 菜单时刻 = 0。
                // 零向量防护与 Ocean 同式：未发布时退菜单真值（指向太阳）。
                float3 sunToSky = _WaterLabSunDir.xyz;
                if (dot(sunToSky, sunToSky) < 0.25)
                    sunToSky = float3(-0.607832, 0.784886, 0.120398);
                float elev = saturate((0.1 - sunToSky.y) * 13.333333);

                // #42-49：light = min(shadow + min(d/20,1)^1.5 + elev²(3−2elev), 1)
                // 末项为 smoothstep(0,1,elev) 的多项式展开（3e²−2e³）
                float lightTerm = min(shadow + pow(dPow, 1.5)
                                      + elev * elev * (3.0 - 2.0 * elev), 1.0);

                // #58-60：o = col·(0.7 + 0.3·light)；#61 alpha=1
                return half4(col * (0.7 + 0.3 * lightTerm), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
