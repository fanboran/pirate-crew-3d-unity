// =====================================================================
// PirateGradientSky —— 三色渐变天空盒（视觉审计遗留 #6：环境光升级为天空盒驱动）
//
// 【它是什么】一个"天顶 / 地平线 / 地面"三色渐变 + 可选太阳盘的天空盒 shader。
//   三档氛围（正午 / 黄昏 / 阴云）各由一张材质资产给一组参数，
//   参数来源是纯 C# 的 AmbientSkyboxCatalog（Assets/Scripts/PirateCrew/Ambient/）。
//
// 【为什么要它（而不是继续用 Skybox/Procedural）】
//   审计（docs/审计/视觉审计报告.md §二.4、§四 遗留 #6）认定环境光是"画面假"的根因之一：
//   现状 RenderSettings.ambientMode = Trilight，环境光只有三个手挑色按法线 Y 分区，
//   与天空、太阳位置、云量都没有关系。切 ambientMode = Skybox 后，环境光的 SH
//   **完全由天空盒卷积而来**——天空盒有三色渐变与太阳盘，环境光就自然有方向与色彩变化。
//   Skybox/Procedural 也能做环境光源，但它的参数（大气厚度/散射）无法直接表达
//   "该档的地平线色必须等于该档雾色"这条契约；渐变式三色是可控且可测的那条路。
//
// 【渲染管线与 Pass 选择】URP 14。天空盒由引擎的 SkyboxRendererList 绘制
//   （URP 侧入口 Packages/com.unity.render-pipelines.universal@14.0.12/Runtime/Passes/DrawSkyboxPass.cs:87
//    → ScriptableRenderContext.CreateSkyboxRendererList），**不经过 URP 的 DrawObjectsPass**，
//   因此本 Pass 刻意**不写 LightMode tag**（与内置 Skybox/Procedural 一致——它同样没有 LightMode
//   且在本工程 URP 下正常出图）。
//   【复核欠账，实拍轮必须确认】"Pass 被静默丢弃"这类故障 Console 不报错（本项目描边 Pass 有过
//   同族事故，见 docs/描边Shader调试.md §八）。静态核对只能证明 shader 能编译，证明不了它被画出来；
//   本 shader 是否真的出图，须在接线轮用实拍图确认（判据：天际线以上是渐变天而非纯色/黑，
//   太阳盘出现在主光方位）。
//
// 【太阳盘方位 = 主光方位（同一来源，不会错位）】太阳盘读 URP 全局 _MainLightPosition
//   （URP 每帧在 ForwardLights.SetupShaderLightConstants 里 cmd.SetGlobalVector 写死：
//   .../Runtime/ForwardLights.cs:511；该调用在 ScriptableRenderer.Execute 的 SetupLights 阶段完成，
//   早于天空盒 Pass 执行——见 Runtime/ScriptableRenderer.cs:1161）。
//   对平行光，_MainLightPosition.xyz = 指向光源的方向 = 指向太阳的方向。
//   这条正是 PirateOcean 太阳光路方位门（PirateOcean.shader 的 azimuth 门）用的同一个主光方向，
//   故"天上一轮日、海上两条光路"的错位在本实现里不可能发生，无需额外对齐校验。
//
// 【色空间】工程 m_ActiveColorSpace = Linear。本 shader 的颜色属性一律声明为**普通 Color**
//   （不是 [HDR]），Unity 会对普通 Color 属性自动做 sRGB→Linear 转换，脚本写 sRGB 原值即可
//   （口径与证据见 Assets/Editor/BattleSceneLighting.cs 类头「色空间」段）。
//   太阳盘强度走独立 float 属性，因此太阳盘可以 > 1 溢出去喂 Bloom（阈值 0.85）。
//
// 【属性清单】见下方 Properties 块；三档取值见 AmbientSkyboxCatalog。
// =====================================================================

Shader "PirateCrew/Skybox/PirateGradientSky"
{
    Properties
    {
        // ---- 三色渐变（数值口径见 AmbientSkyboxCatalog）----
        _SkyZenithColor     ("天顶色（上半球）", Color) = (0.302, 0.651, 0.851, 1)
        _SkyHorizonColor    ("地平线色（契约：= 该档雾色）", Color) = (0.690, 0.831, 0.945, 1)
        _SkyGroundColor     ("地面回照色（下半球 = 暖反弹）", Color) = (0.910, 0.835, 0.639, 1)

        // ---- 渐变形状 ----
        // 过渡带宽以 |dir.y| 为单位：0.14 ≈ 上下各 8°。带宽越小，地平线越"利"。
        _SkyHorizonBlend    ("地平线过渡带宽（|dir.y|）", Range(0.01, 1.0)) = 0.14
        _SkyGroundBlend     ("地面过渡带宽（|dir.y|）", Range(0.01, 1.0)) = 0.34
        // 1 = 线性；越大天顶色越"贴顶"（正午 2.0 保留大片干净蓝）。
        _SkyGradientPower   ("天顶过渡曲线", Range(0.25, 8.0)) = 2.0
        _SkyExposure        ("曝光", Range(0.0, 4.0)) = 1.1

        // ---- 太阳盘（角半径 0 = 关闭；阴云档即用 0）----
        _SkySunDiskColor    ("太阳盘色", Color) = (1.0, 0.957, 0.878, 1)
        _SkySunDiskSize     ("太阳盘角半径（弧度，0=关闭）", Range(0.0, 0.30)) = 0.045
        _SkySunDiskSoftness ("太阳盘边缘柔度（弧度）", Range(0.001, 0.30)) = 0.035
        _SkySunDiskIntensity("太阳盘强度", Range(0.0, 8.0)) = 2.0
    }

    SubShader
    {
        // 天空盒三件套：Background 队列 + 不写深度 + 不裁剪（相机在盒子内部）
        Tags
        {
            "Queue"            = "Background"
            "RenderType"       = "Background"
            "PreviewType"      = "Skybox"
            "IgnoreProjector"  = "True"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        // ================================================================
        // Pass 1 / Sky：三色渐变 + 太阳盘（本 shader 唯一 Pass）
        // ================================================================
        Pass
        {
            Name "Sky"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   Vert
            #pragma fragment Frag

            // Core.hlsl 提供顶点/空间变换（TransformObjectToHClip）与采样宏。
            // 本 shader 不需要 BRDF / 光照常量缓冲：天空盒是自发光的，不吃主光衰减与阴影。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ---- 材质属性（SRP Batcher 要求的 UnityPerMaterial 块）----
            CBUFFER_START(UnityPerMaterial)
                float4 _SkyZenithColor;
                float4 _SkyHorizonColor;
                float4 _SkyGroundColor;
                float  _SkyHorizonBlend;
                float  _SkyGroundBlend;
                float  _SkyGradientPower;
                float  _SkyExposure;
                float4 _SkySunDiskColor;
                float  _SkySunDiskSize;
                float  _SkySunDiskSoftness;
                float  _SkySunDiskIntensity;
            CBUFFER_END

            // ---- 主光方向（指向光源）----
            // _MainLightPosition 由 URP 的 Input.hlsl 声明（com.unity.render-pipelines.universal
            // 14.0.12/ShaderLibrary/Input.hlsl:101，经 Core.hlsl 的 include 链进入本 Pass），
            // 这里**不要重复声明**——重复声明会报 redefinition，且该错误只在真实图形 API 的
            // 变体编译（播放器构建）时暴露，-nographics 导入不报（本项目 2026-09-18 实测踩坑）。
            // URP 每帧 ForwardLights.SetupShaderLightConstants 里 SetGlobalVector 写值
            // （.../Runtime/ForwardLights.cs:511），早于天空盒 Pass（ScriptableRenderer.cs:1161）。

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // 天空盒网格由引擎按"相机为原点 + 相机旋转"绘制，故物体空间方向即世界空间视线方向。
                // （内置 Skybox/Procedural 同款做法：顶点位置只做投影，方向直接透传。）
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.directionWS = IN.positionOS.xyz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.directionWS);
                float  y   = dir.y;

                // ---- 三色渐变 ----
                // 上半球：地平线色 → 天顶色；下半球：地平线色 → 地面回照色。
                // 两支在 y = 0 处都等于 _SkyHorizonColor，故地平线处连续、无接缝。
                // 用 pow 而非 saturate 直接插值：让天顶色"贴顶"，中低空保持地平线附近的浅色。
                float  upT   = saturate(y / max(_SkyHorizonBlend, 1e-4));
                float3 upper = lerp(_SkyHorizonColor.rgb, _SkyZenithColor.rgb,
                                    pow(upT, _SkyGradientPower));

                float  dnT   = saturate(-y / max(_SkyGroundBlend, 1e-4));
                float3 lower = lerp(_SkyHorizonColor.rgb, _SkyGroundColor.rgb,
                                    pow(dnT, _SkyGradientPower));

                float3 color = y >= 0.0 ? upper : lower;

                // ---- 太阳盘 ----
                // 角半径 0 = 关闭（阴云档）。分支条件对整屏一致，不会引起像素级 warp 发散。
                if (_SkySunDiskSize > 0.0 && _SkySunDiskIntensity > 0.0)
                {
                    float3 sunDir = _MainLightPosition.xyz;
                    sunDir = dot(sunDir, sunDir) > 1e-6
                        ? normalize(sunDir)                    // 平行光：指向光源 = 指向太阳
                        : float3(0.0, 1.0, 0.0);               // 未接线兜底：盘放天顶，便于一眼发现

                    // cos 空间做平滑：smoothstep(外缘 cos, 内缘 cos, dot) —— 内缘满强度、外缘 0。
                    float cosInner = cos(_SkySunDiskSize);
                    float cosOuter = cos(_SkySunDiskSize + max(_SkySunDiskSoftness, 1e-3));
                    float disk = smoothstep(cosOuter, cosInner, dot(dir, sunDir));

                    // 沉到地平线以下不画盘（否则日落时会出现"半个太阳插在海里"的穿帮）。
                    disk *= smoothstep(0.0, 0.06, y);

                    color += _SkySunDiskColor.rgb * _SkySunDiskIntensity * disk;
                }

                return half4(color * _SkyExposure, 1.0h);
            }
            ENDHLSL
        }
    }

    // 天空盒没有"回退到纯色"的必要路径：材质丢失时 Unity 会退回内置默认天空盒，
    // 不会像普通物件那样变洋红，故不写 Fallback。
}
